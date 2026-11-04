using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// RAINBOW ARC (Tempest Leg, God). A low chamber, a glint of every colour on the foot, then one
    /// full-circle roundhouse on one leg, the leg out at full length: the swoosh it leaves is a ring of
    /// all the colours of the rainbow, and when the turn closes the ring leaves it and races out over
    /// the ground, a prismatic wave ten to sixteen tiles wide. Everything it passes is cut: foes are
    /// slashed and hurled outward (the near ones off their feet), trees and buildings in its path are
    /// cut through (a God's blow spares nothing), a thin glowing scar left where it ended.
    /// I: 10 tiles, four colours; II: 13, six; III: 16, the whole spectrum with a white leading edge.
    /// Damage (x the user's damage) on each it passes: I 2.0, II 2.6, III 3.4 (less at the far edge).
    /// </summary>
    internal sealed class RainbowArcTech : Technique
    {
        public override string Id => "rainbow_arc";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Spin | Form.Area;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 110f, 150f, 200f };
        public override float[] Cooldown => new[] { 40f, 45f, 50f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 13f : 16f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.6f : 3.4f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            List<Actor> list = TempestHigh.Tmp;
            list.Clear();
            int n = c.CountNear(c.Pos, r, list);
            if (n == 0)
            {
                return null;
            }
            // (the ring runs along the ground's ellipse: those beyond it north and south are out of reach)
            for (int i = n - 1; i >= 0; i--)
            {
                if (RainbowArcWave.Ell(list[i].current_position - c.Pos) > r + 0.3f)
                {
                    list.RemoveAt(i);
                }
            }
            n = list.Count;
            if (n == 0)
            {
                return null;
            }
            float score = 0f;
            bool worth = false;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                float f = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(RainbowArcWave.Ell(v.current_position - c.Pos) / r));
                score += c.Value(v, c.Expected(v, Mult(level) * f), cm);
                if (Tiers.Of(v) >= Tier.Expert || Weights.Species(v) >= Weight.Brute)
                {
                    worth = true;
                }
            }
            if (!c.Forced && n < 4 && !(worth && n >= 2) && !(c.Surrounded && n >= 3))
            {
                return null;   // a ring across the field is for a field of foes
            }
            Actor t = list[0];
            Vector2 d = t.current_position - c.Pos;
            return new Plan
            {
                Target = t,
                Point = c.Pos,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(1f, score) : score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        private sealed class State
        {
            public float Face;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State();
            int colours = level <= 1 ? 4 : level == 2 ? 6 : 7;

            // the chamber: low, the leg cocked, every colour glinting on the foot
            Beat chamber = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.15f,
                To = new Shape(-10f, 1.1f, 0.88f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = Form.Kick | Form.Spin
            };
            chamber.Enter = mm =>
            {
                st.Face = mm.Fwd;
                if (mm.Vis)
                {
                    Vector2 foot = mm.Hand(Limb.Foot);
                    for (int i = 0; i < 3; i++)
                    {
                        Fx.Glint(foot + new Vector2((i - 1) * 2f, i == 1 ? 2f : 0f) * mm.Px, mm.Px, 0.16f + 0.03f * i, TempestHigh.Prism(i * 2 + 1), 0.8f);
                    }
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.18f, 16f * mm.Kk, 3f, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.5f,
                        Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.85f), Fx.WithA(mm.Hue.Core, 1f));
                }
            };
            m.Add(chamber);

            // the full circle: one turn on one leg, the swoosh a ring of every colour
            Beat spin = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.26f,
                To = new Shape(4f, 1.08f, 0.96f, 0.05f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Kick | Form.Spin | Form.Area
            };
            spin.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (!mm.Vis)
                {
                    return;
                }
                for (int i = 0; i < colours; i++)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt((11f + 1.6f * i) * mm.Kk), 6, 60);
                    Color c = TempestHigh.Prism(i);
                    Kit.Swish(mm, Art.Arc(r, 0.42f, 190f, 550f, 2), mm.BeatDur * 1.5f + 0.06f, new Vector2(0f, -1f), st.Face,
                        Color.Lerp(c, Color.white, 0.35f), c, 5 + i);
                }
            };
            spin.Every = (mm, kk) =>
            {
                float t = Beat.Apply(Ease.InOutSine, kk);
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                mm.Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
                mm.Pose.FaceRight = t > 0.25f && t < 0.75f ? st.Face < 0f : st.Face > 0f;
            };
            spin.On(0.8f, mm => Release(mm, colours));
            spin.Exit = mm => mm.Pose.FaceRight = st.Face > 0f;
            m.Add(spin);

            // the leg held out as the ring races away
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.22f, To = new Shape(10f, 1.14f, 0.92f, 0f, 0.05f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>The ring leaves the kick and races out over the ground.</summary>
        private static void Release(ChoreoMove m, int colours)
        {
            Vector2 at = m.Pose.Ground;
            float r = Radius(m.Level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            Areas.Add(new RainbowArcWave(m, at, r, Mult(m.Level), colours));
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile, 0.1f);
            if (m.Vis)
            {
                float px = m.Px;
                Kit.Flash(m.BodyPos(), px, Mathf.RoundToInt(6f * m.Kk), 0.1f, Color.white, m.TopLayer, 26);
                for (int i = 0; i < colours; i++)
                {
                    Kit.Sparks(m.Hand(Limb.Foot), px, 2, 60f, 140f, TempestHigh.Prism(i));
                }
                Kit.DustRing(at, px, m.A.current_tile, 16, 70f, 0.6f, m.Kk);
            }
            Ruin.Quake(m.A, at, m.Scale, 1.2f, m.Kk);
            Kit.Stop(at, 0.05f * m.ImpactK);
        }
    }

    /// <summary>
    /// Rainbow Arc's wave: concentric rings of the rainbow racing out over the ground to 'R' tiles. It cuts
    /// every foe it passes (once each: a slash, hurled outward; the near ones off their feet at III) and
    /// every tree and building it passes through (Ruin, each once), sparkles flying off its edge; a thin
    /// glowing scar is left where it stopped. It draws nothing off screen.
    /// </summary>
    internal sealed class RainbowArcWave : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _mult, _speed, _ringPx;
        private readonly int _colours, _level;
        private readonly HashSet<Building> _cut = new HashSet<Building>();
        private float _cur, _checked, _sparkT;

        public RainbowArcWave(ChoreoMove m, Vector2 at, float r, float mult, int colours) : base(m)
        {
            _at = at;
            _r = Mathf.Max(1f, r);
            _mult = mult;
            _colours = Mathf.Clamp(colours, 1, 7);
            _level = m.Level;
            _speed = 22f + 2f * _level;
            // (a ring wider than the sprite cache allows is drawn at a coarser pixel)
            _ringPx = Mathf.Max(Px, _r / 130f);
            if (Kit.Visible(at, 1f) || Kit.Visible(at + new Vector2(_r, 0f), 0.2f) || Kit.Visible(at - new Vector2(_r, 0f), 0.2f))
            {
                float life = _r / _speed;
                float r1 = _r / _ringPx;
                for (int i = 0; i < _colours; i++)
                {
                    Color c = TempestHigh.Prism(i);
                    Fx.Ring(at, _ringPx, life + 0.08f, 3f, Mathf.Max(4f, r1 - i * 2.2f), 3, 2, FxLayer.Ground, 6 + i, Art.Ground, 1f,
                        Fx.WithA(Color.Lerp(c, Color.white, 0.15f), 1f), Fx.WithA(c, 0.95f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
                }
                if (_level >= 3)
                {
                    Fx.Ring(at, _ringPx, life + 0.08f, 4f, r1 + 1.5f, 1, 1, FxLayer.Ground, 14, Art.Ground, 1f,
                        Fx.WithA(Color.white, 1f), Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Core, 0f));
                }
            }
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float was = _cur;
            _cur = Mathf.Min(_r, _cur + _speed * dt);
            if (OwnerOk)
            {
                Choreo.Foes(A, _at, _cur + 0.3f, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    // (the ring lies on the ground as it is drawn: an ellipse, flatter north to south)
                    float d = Ell(u.current_position - _at);
                    if (d < was - 0.6f || d > _cur + 0.3f + u.stats["size"] * 0.5f)
                    {
                        continue;
                    }
                    float f = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(d / _r));
                    bool near = _level >= 3 && d < _r * 0.45f;
                    if (Strike(u, _mult * f, near ? Hurt.Launch : Hurt.Reel, _at, (near ? 2.4f : 1.6f) * f, near ? 1.1f : -1f, 1.2f))
                    {
                        Slash(u);
                    }
                }
                Cut(was);
            }
            Sparkle(dt);
            if (_cur >= _r)
            {
                Scar();
                return false;
            }
            return true;
        }

        /// <summary>The distance along the ground's ellipse (what the drawn ring has reached).</summary>
        public static float Ell(Vector2 d)
        {
            float y = d.y / Art.Ground;
            return Mathf.Sqrt(d.x * d.x + y * y);
        }

        /// <summary>A cut across the foe: a white line and one of the colours.</summary>
        private void Slash(Actor u)
        {
            Vector2 at = Choreo.BodyAt(u, 0.55f);
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = Choreo.PxOf(u);
            float ang = Random.Range(-60f, 60f) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float half = 6f * Mathf.Clamp(Choreo.SpritePx(u) / 10f, 0.8f, 3f) * px;
            Color c = TempestHigh.Prism(Random.Range(0, _colours));
            Kit.Line(at - d * half, at + d * half, px, 0.12f, 2, FxLayer.Top, 15, Fx.WithA(c, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
            Kit.Line(at - d * half * 0.85f, at + d * half * 0.85f, px, 0.08f, 1, FxLayer.Top, 16, Color.white, Fx.WithA(Color.white, 0f));
        }

        /// <summary>Trees and buildings the ring passes through are cut (each once).</summary>
        private void Cut(float was)
        {
            if (Settings.Destruction <= 0)
            {
                return;
            }
            float from = Mathf.Max(_checked, 0.8f);
            if (_cur - from < 0.9f && _cur < _r)
            {
                return;
            }
            Kit.BeginTrench();
            try
            {
                for (float rr = from; rr <= _cur; rr += 0.9f)
                {
                    int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * (1f + Art.Ground) * rr / 0.9f), 6, 160);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        Vector2 p = _at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * rr;
                        WorldTile t = Combat.TileAt(p);
                        if (t == null || t.building == null)
                        {
                            continue;
                        }
                        Building b = t.building;
                        if (!b.isAlive() || _cut.Contains(b))
                        {
                            continue;
                        }
                        _cut.Add(b);
                        Ruin.Blast(A, p, 0.6f, Scale, A.current_position, false, -1);
                        if (Kit.Visible(p))
                        {
                            Color c = TempestHigh.Prism(i);
                            Vector2 side = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                            Kit.Line(p - side * 0.7f + new Vector2(0f, 0.4f), p + side * 0.7f + new Vector2(0f, 0.4f), Px, 0.14f, 1, FxLayer.Top, 15, Color.white, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
                            Kit.Sparks(p + new Vector2(0f, 0.4f), Px, 3, 40f, 90f, c);
                        }
                    }
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            _checked = _cur + 0.01f;
        }

        /// <summary>Sparkles of every colour flying off the ring's edge.</summary>
        private void Sparkle(float dt)
        {
            _sparkT -= dt;
            if (_sparkT > 0f)
            {
                return;
            }
            _sparkT = 0.02f;
            int n = Fx.Count(4 + _level);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 p = _at + dir * _cur;
                if (!Kit.Visible(p, 0.1f))
                {
                    continue;
                }
                Color c = TempestHigh.Prism(Random.Range(0, _colours));
                Particle q = Fx.Spawn(Art.Pixel, p, Px, Random.Range(0.2f, 0.4f), FxLayer.Top, 9);
                q?.Moving(dir * Random.Range(1f, 3f), Random.Range(0.6f, 1.6f), 2f).Sized(1.5f, 1f).Color(Color.white, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>A thin glowing scar where the ring stopped.</summary>
        private void Scar()
        {
            if (!Kit.Visible(_at, 1f) && !Kit.Visible(_at + new Vector2(_r, 0f), 0.2f) && !Kit.Visible(_at - new Vector2(_r, 0f), 0.2f))
            {
                return;
            }
            float r1 = _r / _ringPx;
            Particle s = Fx.Spawn(Art.Ring(Mathf.Clamp(Mathf.RoundToInt(r1), 1, 140), 1, Art.Ground), _at, _ringPx, 2.2f, FxLayer.Ground, 4);
            if (s != null)
            {
                s.Snap = true;
                s.Color(Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            }
        }
    }
}
