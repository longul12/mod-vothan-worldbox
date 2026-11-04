using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// NOVA BURST (Ki Arts, Master). Hemmed in by a crowd, it pulls everything in: it curls up round
    /// its own middle (squashed small, light leaking out of its crossed arms, motes rushing in from
    /// every side, the dust drawn in across the ground to its feet), holds it a heartbeat trembling,
    /// then throws itself wide open: a dome of gold ki swells out of it and bursts, and everyone
    /// inside is flung away tumbling (Scattered), rays of light thrown out all round, the ground
    /// blasted bare under it. Made for being surrounded: the Brain reaches for it when three or more
    /// stand within its reach.
    /// I: radius 2.6. II: 3.2, an impact frame, and a shock ring races out after the dome knocking
    /// down those beyond it. III: 4, a first small pulse clears its feet before the great dome, the
    /// ground under it is torn into a glowing crater.
    /// Damage (x the user's damage) to everyone inside: I 1.5, II 2, III 2.6 (+0.45 the first pulse;
    /// the ring 30% beyond the dome).
    /// </summary>
    internal sealed class NovaBurstTech : Technique
    {
        public override string Id => "nova_burst";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 26f, 44f, 70f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        /// <summary>A combo that ends in the thick of them: the burst clears the ring.</summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.2f : 4f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 2.6f;
        }

        private static float Push(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.2f : 4f;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            _near.Clear();
            int n = c.CountNear(c.Pos, r, _near);
            if (!c.Forced && n < 3 && !(n >= 2 && c.Surrounded))
            {
                return null;   // not hemmed in: a single foe gets a single blow
            }
            float score = 0f;
            Actor nearest = null;
            float best = float.MaxValue;
            bool worth = false;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
                float d = c.Dist(v);
                if (d < best)
                {
                    best = d;
                    nearest = v;
                }
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute)
                {
                    worth = true;
                }
            }
            if (nearest == null)
            {
                if (!c.Forced || c.Enemies.Count == 0)
                {
                    return null;
                }
                nearest = c.Focus ?? c.Enemies[0];
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            else
            {
                // the more sides they press from, the more it is the answer
                score *= 1f + 0.08f * c.Sides + (worth ? 0.1f : 0f);
            }
            Vector2 dir = nearest.current_position - c.Pos;
            return new Plan
            {
                Target = nearest,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float r = Radius(level), mult = Mult(level), push = Push(level);
            float moteT = 0f, dustT = 0f;
            Particle glow = null;
            Form shows = Form.Ki | Form.Area;
            // it pulls itself in round its middle: everything is drawn to it
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.2f,
                To = new Shape(-4f, 0.84f, 0.84f, 0f),
                Ease = Ease.OutCubic,
                Track = false,
                Forms = shows,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.2f);
                    if (mm.Vis)
                    {
                        glow = mm.Glue(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(mm.Hpx * 0.5f), 2, 30), 1f, 0.5f), mm.BodyPos(), mm.Px, 1f, FxLayer.Over, 8));
                        Fx.Ring(mm.Pose.Ground, mm.Px, 0.2f, r / mm.Px, 3f, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.4f,
                            Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.7f), Fx.WithA(mm.Hue.Core, 0.9f));
                    }
                },
                Every = (mm, k) =>
                {
                    if (k > 0.55f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, k > 0.85f ? 0.8f : 0.5f);
                    }
                    Vector2 at = mm.BodyPos();
                    if (glow != null && glow.Alive)
                    {
                        glow.Pos = at;
                        glow.Grad = null;
                        glow.R.color = Fx.WithA(Color.Lerp(mm.Hue.Edge, mm.Hue.Core, k), 0.3f + 0.55f * k);
                        glow.Size0 = glow.Size1 = 0.4f + 0.75f * k + 0.08f * Mathf.Sin(mm.BeatT * 40f);
                    }
                    if (!mm.Vis)
                    {
                        return;
                    }
                    moteT -= Clock.Dt;
                    if (moteT <= 0f)
                    {
                        moteT = 0.025f;
                        ChoreoFx.Motes(at, mm.Px, level >= 3 ? 4 : 3, (16f + 6f * level) * mm.Kk, 0.13f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                    dustT -= Clock.Dt;
                    if (dustT <= 0f)
                    {
                        dustT = 0.09f;
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 5, -26f, 0.4f, mm.Kk);
                    }
                }
            });
            if (level >= 3)
            {
                // a first pulse clears its feet...
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.05f,
                    To = new Shape(0f, 1.14f, 1.12f, 0.04f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = shows,
                    Enter = mm => Pulse(mm, r * 0.5f, 0.45f, 1.4f, false)
                });
                // ...and it draws in again, deeper
                m.Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.12f,
                    To = new Shape(-6f, 0.8f, 0.82f, 0f),
                    Ease = Ease.OutCubic,
                    Track = false,
                    Tremble = true,
                    Forms = shows,
                    Every = (mm, k) =>
                    {
                        if (glow != null && glow.Alive)
                        {
                            glow.Pos = mm.BodyPos();
                            glow.Size0 = glow.Size1 = 1.1f + 0.15f * Mathf.Sin(mm.BeatT * 50f);
                        }
                        if (mm.Vis && Random.value < 0.5f)
                        {
                            ChoreoFx.Motes(mm.BodyPos(), mm.Px, 3, 26f * mm.Kk, 0.1f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                        }
                    }
                });
            }
            // it throws itself wide open: the dome
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(0f, 1.32f, 1.24f, 0.14f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = shows,
                Enter = mm =>
                {
                    if (glow != null)
                    {
                        mm.Unglue(glow);
                        glow = null;
                    }
                    Pulse(mm, r, mult, push, true);
                }
            });
            Beat hold = new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.24f,
                To = new Shape(0f, 1.16f, 1.1f, 0.06f),
                Ease = Ease.OutQuad,
                Track = false,
                Tremble = true,
                Forms = shows
            };
            if (level >= 2)
            {
                // the shock runs on past the dome
                hold.On(0.55f, mm => Areas.Add(new ShockwaveHazard(mm, mm.Pose.Ground, r * 1.45f * Mathf.Lerp(1f, mm.Kk, 0.5f), 16f, mult * 0.3f, Hurt.Knockdown, 0.7f)));
            }
            m.Add(hold);
            m.Recover(0.28f);
            return m.Start();
        }

        /// <summary>The dome leaves it: light, rays, a ring over the ground; the dome itself swells and bursts (DomeHazard).</summary>
        private static void Pulse(ChoreoMove mm, float r, float mult, float push, bool main)
        {
            float rr = r * Mathf.Lerp(1f, mm.Kk, 0.5f);
            Vector2 g = mm.Pose.Ground;
            Areas.Add(new DomeHazard(mm, g, rr, main ? 0.16f : 0.07f, mult, push));
            WorldTile tile = mm.A.current_tile;
            Choreo.Sound(main ? "event:/SFX/EXPLOSIONS/ExplosionForce" : "event:/SFX/EXPLOSIONS/ExplosionSmall", tile);
            if (main)
            {
                Choreo.Sound("event:/SFX/POWERS/DivineLight", tile, 0.2f);
            }
            if (mm.Vis)
            {
                Vector2 at = mm.BodyPos();
                float px = mm.Px;
                Kit.Flash(at, px, Mathf.RoundToInt((main ? 10f : 5f) * mm.Kk), main ? 0.12f : 0.07f, Color.white, mm.TopLayer);
                Fx.Ring(g, px, main ? 0.32f : 0.18f, 3f, rr / px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(mm.Hue.Edge, 0.6f), Fx.WithA(mm.Hue.Rim, 0f));
                // rays of light thrown out all round
                int n = Fx.Count(main ? 12 : 6);
                float a0 = Random.value * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + (i + Random.value * 0.4f) / n * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Kit.SpeedLines(at + d * (main ? 5f : 3f) * px * mm.Kk, d, px, 1, 1f, main ? 0.16f : 0.1f, Fx.WithA(mm.Hue.Edge, 0.85f), mm.TopLayer);
                }
                Kit.DustRing(g, px, tile, main ? 16 : 8, main ? 64f : 36f, 0.55f, mm.Kk);
                if (main && mm.Level >= 2)
                {
                    Kit.ImpactFrame(at, px, (0.45f + 0.2f * mm.Level) * mm.Kk, mm.Hue.Pal, false, FxLayer.Behind);
                }
                if (main && mm.Level >= 3)
                {
                    Kit.Crater(g, px * mm.Kk, tile, true, 6f, false, mm.Hue.Edge);
                    Kit.Debris(g, px, tile, 10, mm.Kk);
                }
            }
            if (main)
            {
                Kit.Stop(g, 0.035f + 0.015f * mm.Level);
                Ruin.Quake(mm.A, g, mm.Scale, 1.2f, mm.Kk);
                if (mm.Level >= 3)
                {
                    // (the move plays at the technique's Scale: III is Heavy, so the crater is dug here)
                    Ruin.Blast(mm.A, g, r * 0.45f, Scale.Heavy, g);
                }
            }
        }
    }
}
