using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// WHIRLWIND SWEEP (Tempest Leg, Expert). Ringed by foes, it drops onto its hands and spins low on the
    /// ground, legs out like a breakdancer's windmill: the body tilted over, turning (the sprite narrowing
    /// and flipping with every half turn), flat crescents racing round at ankle height, a spiral of dust
    /// whipped off the ground; every foe within reach is swept off its feet as the leg passes it (a ripple
    /// of hits round the circle, never all at once) and goes over on its back (Down). It kips up out of
    /// the spin. The answer to being surrounded (the Brain weighs area techniques up there).
    /// I: 1.8 tiles, one and a half turns. II: 2.2 tiles, two turns, a ring of scorched earth. III: 2.6
    /// tiles, three turns, the swept foes flung higher, and the kip-up sends a shockwave of dust racing
    /// out that knocks down whoever is still standing within 3.4 tiles; the ring of earth is torn up.
    /// Damage (x the user's damage) to each foe swept: I 0.9; II 1.3; III 1.8 (+ the shockwave x0.4).
    /// </summary>
    internal sealed class WhirlwindSweepTech : Technique
    {
        public override string Id => "whirlwind_sweep";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Area;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Low | Form.Spin | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 32f, 50f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.3f : 1.8f;
        }

        /// <summary>How far its legs reach round it (tiles, before the unit's size).</summary>
        public static float Radius(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.2f : 2.6f;
        }

        private static readonly List<Actor> _in = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f);
            _in.Clear();
            int n = c.CountNear(c.Pos, r, _in);
            if (!c.Forced)
            {
                // a sweep for a crowd round it: two in reach (one in a chain window), or hemmed in
                int need = c.Linked ? 1 : 2;
                if (n < need && !(c.Surrounded && n >= 1))
                {
                    return null;
                }
                if (c.InWater)
                {
                    return null;
                }
            }
            else if (n == 0 && c.Focus == null)
            {
                return null;
            }
            float score = 0f;
            Actor center = null;
            float cd = float.MaxValue;
            for (int i = 0; i < _in.Count; i++)
            {
                Actor v = _in[i];
                if (!c.Allowed(v, out float cm))
                {
                    continue;
                }
                float mult = Weights.Heavy(v) ? Mult(level) * 0.5f : Mult(level);
                score += c.Value(v, c.Expected(v, mult), cm);
                float d = c.Dist(v);
                if (d < cd)
                {
                    cd = d;
                    center = v;
                }
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
                center ??= c.Focus;
            }
            if (center == null)
            {
                return null;
            }
            Vector2 dir = center.current_position - c.Pos;
            return new Plan
            {
                Target = center,
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
            float r = Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            float mult = Mult(level);
            float turns = level <= 1 ? 1.5f : level == 2 ? 2f : 3f;
            HashSet<long> swept = new HashSet<long>();
            List<Actor> near = new List<Actor>();
            float[] next = { 0f };
            bool[] scarred = { false };
            // down onto its hands: the body drops and tips over, one leg already reaching out
            m.PoseTo(0.1f, new Shape(34f, 1.24f, 0.64f, 0f, 0f), Ease.OutQuad, Tempo.Wind).Shows(m.Tech.Forms).OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 6, 24f, 0.4f, mm.Kk);
                }
            });
            // the windmill: every foe in reach is swept as the leg passes it
            m.Add(TempestKicks.LowSpin(turns, 0.3f * turns, r, 58f, (mm, th) =>
            {
                if (th >= next[0])
                {
                    // a flat crescent races round at ankle height every half turn
                    next[0] += Mathf.PI;
                    Crescent(mm, r, th);
                }
                Sweep(mm, r, th, mult, swept, near);
            }));
            m.Last.Forms = m.Tech.Forms;
            m.OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Kit.Shockwave(mm.Pose.Ground, mm.Px, 3f, r / mm.Px, 0.3f * turns, Fx.WithA(mm.Hue.Edge, 0.55f), 1);
                }
            });
            m.Every((mm, k) =>
            {
                // (the ground is scorched and, at III, torn up in a ring under the legs as they pass)
                if (mm.Level >= 2 && k >= 0.5f && !scarred[0])
                {
                    scarred[0] = true;
                    Scar(mm, r);
                }
            });
            // the kip-up: it springs out of the spin onto its feet
            m.PoseTo(0.12f, new Shape(0f, 0.88f, 1.16f, 0.16f, 0f), Ease.OutQuad, Tempo.Follow).OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                }
            });
            if (level >= 3)
            {
                m.Shockwave(r + 0.8f, mult * 0.4f, Hurt.Knockdown, 0.8f, 12f);
            }
            m.Land();
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>Foes the leg reaches as it passes them: swept off their feet, once each.</summary>
        private static void Sweep(ChoreoMove m, float r, float th, float mult, HashSet<long> swept, List<Actor> near)
        {
            Choreo.Foes(m.A, m.Pose.Ground, r, near);
            float face = m.Fwd;
            for (int i = 0; i < near.Count; i++)
            {
                Actor u = near[i];
                long id = u.Sid();
                if (swept.Contains(id))
                {
                    continue;
                }
                Vector2 rel = u.current_position - m.Pose.Ground;
                // the angle round it where it stands (from its facing, the way the leg turns)
                float phi = Mathf.Atan2(rel.y / Art.Ground, rel.x * face);
                if (phi < 0f)
                {
                    phi += Mathf.PI * 2f;
                }
                // a foe right on top of it is swept at once; the rest as the leg comes round to them
                if (rel.magnitude > 0.5f && phi > th)
                {
                    continue;
                }
                swept.Add(id);
                if (!Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                float near01 = 1f - Mathf.Clamp01(rel.magnitude / Mathf.Max(0.1f, r));
                float heavy = Weights.Heavy(u) ? 0.5f : 1f;
                HitResult res = Combat.Hit(m.A, u, mult * heavy * cm * Mathf.Lerp(0.8f, 1f, near01), false, false, 0f, m);
                Impact im = Impact.Of(Hurt.Knockdown, rel, (0.35f + 0.15f * m.Level) * Mathf.Lerp(0.7f, 1f, near01), 1.1f);
                im.Lift = m.Level >= 3 ? 0.65f : 0.45f;
                im.Daze = 0.7f + 0.2f * m.Level;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, m.A, m, im);
                }
                if (m.Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.15f), rel, m.Px, m.Kk, 0.9f, m.Hue, m.TopLayer);
                    TempestKicks.Whip(m, Victims.GroundOf(u), rel, 2);
                }
                ChoreoFx.BlowSound(Limb.Foot, 0.9f, false, u.current_tile);
                if (m.V == null || m.V == u || !Combat.Living(m.V))
                {
                    m.SetLink(Link.Down, u);
                }
                if (Dev.On) Dev.Trace("whirlwind sweeps " + u.asset.id + "#" + id + " hp " + u.getHealth() + (res.Killed ? " killed" : ""));
            }
        }

        /// <summary>A flat crescent at ankle height racing round the circle from the leg's angle.</summary>
        private static void Crescent(ChoreoMove m, float r, float th)
        {
            if (!m.Vis)
            {
                return;
            }
            int rp = Mathf.Clamp(Mathf.RoundToInt(r / m.Px * 0.92f), 6, 60);
            // (two arcs only, from the front or from the back: the generated sprites are cached by their angles)
            float a0 = (Mathf.RoundToInt(th / Mathf.PI) & 1) * 180f;
            Particle p = Fx.Spawn(Art.Arc(rp, Art.Ground, a0, a0 + 200f, m.Level >= 3 ? 3 : 2), m.Pose.Ground + new Vector2(0f, 1f * m.Px), m.Px, 0.22f, FxLayer.Ground, 7);
            if (p != null)
            {
                p.SX = m.Fwd;
                p.Color(m.Hue.Core, m.Hue.Edge, Fx.WithA(m.Hue.Edge, 0f));
                p.Cine = m.Cinematic;
            }
        }

        /// <summary>The ring of earth under the legs: scorched (II), torn up in pieces round the circle (III).</summary>
        private static void Scar(ChoreoMove m, float r)
        {
            Vector2 c = m.Pose.Ground;
            if (m.Level >= 3)
            {
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2f;
                    Vector2 at = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r * 0.8f;
                    Ruin.Blast(m.A, at, 0.55f, m.Scale, c);
                }
            }
            else
            {
                Ruin.Blast(m.A, c, r * 0.5f, m.Scale, c);
            }
        }
    }
}
