using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN HEEL (Tempest Leg, God; an Ultimate). The world dims (a cinematic, with its title card
    /// when those are on). The God raises one leg straight up to the sky: a pillar of light climbs from
    /// the heel, motes stream into it, pebbles lift off the ground and the earth trembles. A heartbeat
    /// at the top, trembling. Then the heel comes down: an impact frame, the world held, a crater under
    /// the heel, and the ground splits: a canyon tears away from its foot along the line it faces,
    /// twenty to thirty tiles, dust walls bursting up along both lips, everything standing on it
    /// broken (the God's blow spares no building) and every foe on it thrown into the air; the shock
    /// of the blow lifts everyone round the God off their feet. I: a 20-tile canyon, the shock 4 tiles
    /// round; II: 25 and 5; III: 30 and 6, wider and glowing. Damage (x the user's damage): on the
    /// canyon I 3.5, II 4.5, III 6; the shock I 1.5, II 2, III 2.6.
    /// </summary>
    internal sealed class HeavenHeelTech : Technique
    {
        public override string Id => "heaven_heel";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Overhead | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 150f, 210f, 280f };
        public override float[] Cooldown => new[] { 70f, 80f, 90f };

        public static float Length(int level)
        {
            return level <= 1 ? 20f : level == 2 ? 25f : 30f;
        }

        public static float Width(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.4f : 1.7f;
        }

        public static float HitWidth(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2f : 2.4f;
        }

        public static float Canyon(int level)
        {
            return level <= 1 ? 3.5f : level == 2 ? 4.5f : 6f;
        }

        public static float ShockRadius(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        public static float Shock(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 2.6f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float len = Length(level) * k;
            float shockR = ShockRadius(level) * k;
            Plan p = Choreo.PickLine(c, this, level, len, HitWidth(level), Canyon(level), 1, true);
            // the shock round it
            List<Actor> near = TempestHigh.Tmp;
            near.Clear();
            int n = c.CountNear(c.Pos, shockR, near);
            if (p == null)
            {
                if (n == 0 || n < 3 && !c.Forced)
                {
                    return null;
                }
                Actor t = near[0];
                Vector2 d = t.current_position - c.Pos;
                p = new Plan
                {
                    Target = t,
                    Point = t.current_position,
                    Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                    Count = 0,
                    Score = 0f,
                    Data = c.Forced
                };
            }
            float shock = 0f;
            bool worth = false;
            for (int i = 0; i < near.Count; i++)
            {
                Actor v = near[i];
                c.Allowed(v, out float cm);
                shock += c.Value(v, c.Expected(v, Shock(level)), cm) * 0.6f;
                if (Tiers.Of(v) >= Tier.Master || Weights.Species(v) >= Weight.Titan)
                {
                    worth = true;
                }
            }
            if (p.Target != null && (Tiers.Of(p.Target) >= Tier.Master || Weights.Species(p.Target) >= Weight.Titan))
            {
                worth = true;
            }
            int caught = p.Count + n;
            if (!c.Forced && !worth && caught < 5)
            {
                return null;   // the earth is not split for a handful of soldiers
            }
            // the canyon needs dry land in front of it
            Vector2 first = Combat.Clamp(c.Pos + p.Dir * 2.5f);
            if (!c.Forced && (c.InWater || !Combat.SafeSpot(first) && !Choreo.Wading(first)))
            {
                return null;
            }
            p.Score = (c.Forced ? Mathf.Max(1f, p.Score + shock) : p.Score + shock) + 20f * Mathf.Min(6, caught);
            p.Count = caught;
            p.AreaRadius = shockR;
            return p;
        }

        private sealed class State
        {
            public Vector2 Dir;
            public float T, Shake;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State { Dir = p.Dir.sqrMagnitude > 1e-4f ? p.Dir.normalized : m.Dir };
            m.Face(st.Dir);
            m.OpenCinematic(10f, 0.35f, "sakuga_title_heaven_heel");

            // the leg rises to the sky
            Beat raise = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = level <= 1 ? 0.5f : level == 2 ? 0.55f : 0.6f,
                To = new Shape(-16f, 0.84f, 1.28f, 0f, -0.05f),
                Ease = Ease.OutCubic,
                Track = false,
                Forms = Form.Kick | Form.Overhead | Form.Ultimate
            };
            raise.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", mm.A.current_tile, 0.3f);
                if (mm.Vis)
                {
                    Fx.Glint(Heel(mm), mm.Px, 0.16f, mm.Hue.Core, 1f);
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.4f, 26f * mm.Kk, 4f, 1, 2, FxLayer.Ground, 5, Art.Ground, 1.5f,
                        Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.9f), Fx.WithA(mm.Hue.Core, 1f));
                }
            };
            raise.Every = (mm, kk) => Gather(mm, st, kk);
            raise.On(0.92f, mm =>
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
                if (mm.Vis)
                {
                    Fx.Glint(Heel(mm), mm.Px, 0.22f, mm.Hue.Core, 1.6f);
                }
            });
            m.Add(raise);
            // a heartbeat at the top
            m.Hold(0.1f, true, Tempo.Wind).Every((mm, kk) => Gather(mm, st, 1f));

            // the drop
            Beat drop = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.06f,
                To = new Shape(34f, 1.34f, 0.68f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Kick | Form.Overhead | Form.Ultimate | Form.Heavy
            };
            drop.On(0.55f, mm => Strike(mm, st));
            m.Add(drop);
            m.Do(mm => mm.EndCinematic());
            // crouched over the heel while the earth splits away from it
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.42f, To = new Shape(28f, 1.28f, 0.74f, 0f, 0.08f), Ease = Ease.OutQuad, Track = false, Tremble = true });
            m.Recover(0.4f);
            return m.Start();
        }

        /// <summary>Where the raised heel is (high over its head).</summary>
        private static Vector2 Heel(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(m.Fwd * 1.5f, m.Hpx * 0.95f) * m.Px;
        }

        /// <summary>The leg up: a pillar of light from the heel to the sky, motes streaming in, pebbles rising, the earth trembling.</summary>
        private static void Gather(ChoreoMove m, State st, float k)
        {
            float dt = Clock.Dt;
            if (k > 0.7f)
            {
                m.Pose.Nudge = Choreo.Tremble(m.BeatT, 0.6f);
            }
            st.Shake -= dt;
            if (st.Shake <= 0f)
            {
                st.Shake = 0.15f;
                Kit.Shake(m.Pose.Ground, 0.16f, 0.003f + 0.004f * k, m.Kk);
            }
            st.T -= dt;
            if (st.T > 0f || !m.Vis)
            {
                return;
            }
            st.T = 0.025f;
            float px = m.Px;
            Vector2 heel = Heel(m);
            float up = (2f + 7f * k) * m.Kk;
            Kit.Line(heel, heel + new Vector2(0f, up), px, 0.06f, 3 + 2 * m.Level, m.TopLayer, 11, Fx.WithA(m.Hue.Main, 0.45f), Fx.WithA(m.Hue.Edge, 0.3f), Fx.WithA(m.Hue.Edge, 0f));
            Kit.Line(heel, heel + new Vector2(0f, up * 1.05f), px, 0.06f, 1 + m.Level, m.TopLayer, 12, Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Core, 0f));
            Kit.Flash(heel, px, Mathf.Max(2, Mathf.RoundToInt((2f + 2f * k) * m.Kk)), 0.06f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer, 24);
            ChoreoFx.Motes(heel, px, 3, 16f * m.Kk, 0.14f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
            // pebbles lift off the ground around it
            WorldTile tile = m.A.current_tile;
            Color soil = Fx.SoilColor(tile);
            int n = Fx.Count(2);
            for (int i = 0; i < n; i++)
            {
                Vector2 g = m.Pose.Ground + Random.insideUnitCircle * 2.5f * m.Kk;
                Particle q = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], g, px, Random.Range(0.5f, 0.8f), FxLayer.Top, 4);
                if (q != null)
                {
                    q.Moving(Vector2.zero, Random.Range(0.8f, 1.6f) * m.Kk, 0.6f);
                    q.Spin = Random.Range(-180f, 180f);
                    q.Color(soil, soil, Fx.WithA(soil, 0f));
                }
            }
        }

        /// <summary>The heel lands: the impact, a crater under it, and the canyon tearing away along its line.</summary>
        private static void Strike(ChoreoMove m, State st)
        {
            int level = m.Level;
            float px = m.Px;
            Vector2 at = Combat.Clamp(m.Pose.Ground + st.Dir * 0.5f * m.Kk);
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.05f);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 3f * px);
                Kit.ImpactFrame(hit, px, (1.1f + 0.2f * level) * m.Kk, m.Hue.Pal, false, m.Cinematic ? FxLayer.Cine : FxLayer.Top);
                Kit.Swish(m, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(18f * m.Kk), 8, 50), 1f, 100f, -80f, 4), 0.16f, new Vector2(2f * m.Fwd, 4f), m.Fwd, m.Hue.Core, m.Hue.Edge, 8);
                ChoreoFx.Smear(at + new Vector2(0f, 5f * m.Kk), at, px, 0.12f, 3, 3, m.Hue.Core, m.Hue.Edge, m.TopLayer, 12);
                Fx.FocusLines(hit, px, Fx.Count(10), 14f * m.Kk, 26f * m.Kk, 0.14f);
                Kit.Flash(hit, px, Mathf.RoundToInt(10f * m.Kk), 0.12f, Color.white, m.TopLayer, 30);
                Kit.Crater(at, px * m.Kk, tile, true, 12f, false, m.Hue.Edge);
                Kit.DustRing(at, px, tile, 24, 90f, 0.8f, m.Kk);
                Kit.Debris(at, px, tile, 18, m.Kk * 1.3f);
            }
            if (m.Cinematic)
            {
                m.Freeze(0.14f);
            }
            else
            {
                Kit.Stop(at, 0.1f * m.ImpactK);
            }
            Ruin.Blast(m.A, at, 1.6f, m.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, at, m.Scale, 1.6f, m.Kk);
            float k = Mathf.Lerp(1f, m.Kk, 0.5f);
            Areas.Add(new HeavenHeelCanyon(m, at, st.Dir, Length(level) * k, Width(level), HitWidth(level) * k, Canyon(level), ShockRadius(level) * k, Shock(level)));
            m.SetLink(Link.Scattered, null);
        }
    }

    /// <summary>
    /// The canyon Heaven Heel tears in the earth: it runs away from the heel along its line at 60 tiles a
    /// second, splitting the ground (Ruin.Furrow at the God's force: dug deep, every building on it
    /// broken), dust walls and rocks bursting from both lips, glowing fissures, and every foe on it is
    /// thrown up into the air; the blow's shock first lifts everyone round the heel (those on the line
    /// are left to the canyon). It never runs off the map; water is left alone.
    /// </summary>
    internal sealed class HeavenHeelCanyon : Hazard
    {
        private readonly Vector2 _from, _dir, _side;
        private readonly float _len, _width, _hitW, _mult, _shockR, _shockMult;
        private float _cur, _dug;
        private bool _shocked;
        private int _seg;

        private const float Speed = 60f;

        public HeavenHeelCanyon(ChoreoMove m, Vector2 from, Vector2 dir, float len, float width, float hitW, float mult, float shockR, float shockMult) : base(m)
        {
            _from = from;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _side = Choreo.Perp(_dir);
            _width = width;
            _hitW = hitW;
            _mult = mult;
            _shockR = shockR;
            _shockMult = shockMult;
            // never off the map
            _len = Mathf.Max(2f, len);
            for (int i = 0; i < 12; i++)
            {
                Vector2 end = _from + _dir * _len;
                if (end.x > 1f && end.y > 1f && end.x < MapBox.width - 2 && end.y < MapBox.height - 2)
                {
                    break;
                }
                _len *= 0.85f;
            }
            if (Kit.Visible(from, 0.5f))
            {
                Fx.Ring(from, Px, 0.5f, 4f, _shockR / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(from, Px, 0.4f, 3f, _shockR / Px * 0.7f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.6f,
                    Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            if (!_shocked)
            {
                _shocked = true;
                Shock();
            }
            float was = _cur;
            _cur = Mathf.Min(_len, _cur + Speed * dt);
            Vector2 a = _from + _dir * was;
            Vector2 b = _from + _dir * _cur;
            if (OwnerOk && _cur > was)
            {
                // everyone on the new stretch is thrown up, away from the split
                Choreo.FoesOnLine(A, _from + _dir * Mathf.Max(0f, was - 0.6f), b, _hitW, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    float along = Mathf.Clamp(Vector2.Dot(u.current_position - _from, _dir), 0f, _len);
                    Vector2 q = _from + _dir * along;
                    float f = Mathf.Lerp(1f, 0.7f, along / _len);
                    Strike(u, _mult * f, Hurt.Launch, q, 1.3f, 2.4f + 0.4f * (M != null ? M.Level : 2), 1.5f);
                }
            }
            if (OwnerOk && (_cur - _dug >= 2f || _cur >= _len && _dug < _len))
            {
                Ruin.Furrow(A, _from + _dir * _dug, b, _width, Scale, A.current_position, true);
                _dug = _cur;
            }
            if (_cur > was)
            {
                Draw(a, b);
            }
            return _cur < _len;
        }

        /// <summary>The blow's shock: everyone round the heel lifted off its feet (the canyon takes those on its line).</summary>
        private void Shock()
        {
            if (!OwnerOk)
            {
                return;
            }
            Choreo.Foes(A, _from, _shockR, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                Vector2 rel = u.current_position - _from;
                float along = Vector2.Dot(rel, _dir);
                float off = Mathf.Abs(Vector2.Dot(rel, _side));
                if (along >= -0.3f && off <= _hitW)
                {
                    continue;   // on the line: the canyon's
                }
                float d = rel.magnitude / Mathf.Max(0.1f, _shockR);
                Strike(u, _shockMult * Mathf.Lerp(1f, 0.55f, d), Hurt.Launch, _from, 0.6f + 1.2f * (1f - d), 1.4f + 1.2f * (1f - d), 1.3f);
            }
        }

        private void Draw(Vector2 a, Vector2 b)
        {
            if (!Kit.Visible(a, 0.4f) && !Kit.Visible(b, 0.4f))
            {
                return;
            }
            WorldTile t = Combat.TileAt(b);
            if (t == null || t.Type == null || t.Type.liquid)
            {
                return;
            }
            float px = Px;
            float len = Vector2.Distance(a, b) / px;
            float rot = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            Color soil = Fx.SoilColor(t);
            Color dark = Fx.Darken(soil, 0.65f);
            int lips = M != null && M.Level >= 3 ? 3 : 2;
            for (int i = 0; i < lips; i++)
            {
                float o = (i - (lips - 1) * 0.5f) * _width * 0.5f;
                Particle f = Fx.Spawn(ChoreoArt.Fissure(Mathf.Clamp(Mathf.RoundToInt(len + 3f), 4, 120), _seg + i), a + _side * o, px, 6f, FxLayer.Ground, 2);
                if (f != null)
                {
                    f.Rot = rot;
                    f.Snap = true;
                    f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                }
            }
            Particle g = Fx.Spawn(ChoreoArt.Fissure(Mathf.Clamp(Mathf.RoundToInt(len + 3f), 4, 120), _seg + 5), a, px, 1.6f, FxLayer.Ground, 3);
            if (g != null)
            {
                g.Rot = rot;
                g.Snap = true;
                g.Color(Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
            }
            _seg++;
            // dust walls and rocks burst up from both lips
            Kit.DustRing(b, px, t, 5, 50f, 0.6f, K);
            if ((_seg & 1) == 0)
            {
                Kit.Debris(b, px, t, 3, K);
            }
            ChoreoFx.HeelDust(b + _side * _width * 0.6f, _side, px, t, K * 1.2f);
            ChoreoFx.HeelDust(b - _side * _width * 0.6f, -_side, px, t, K * 1.2f);
            if ((_seg % 4) == 0)
            {
                // a side crack branching off the lip
                float side = Random.value < 0.5f ? -1f : 1f;
                Vector2 bd = Choreo.Rotate(_dir, side * Random.Range(30f, 60f));
                Particle c = Fx.Spawn(ChoreoArt.Fissure(Mathf.RoundToInt(Random.Range(10f, 24f) * K), _seg), b + _side * side * _width * 0.4f, px, 4f, FxLayer.Ground, 2);
                if (c != null)
                {
                    c.Rot = Mathf.Atan2(bd.y, bd.x) * Mathf.Rad2Deg;
                    c.Snap = true;
                    c.Color(Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0f));
                }
            }
        }
    }
}
