using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// RABBLE SWEEP (Hunter's Way, Novice). For a mob of weak things pressing in (rats, chickens,
    /// a crowd of peasants with pitchforks): the hunter steps into the thick of them, winds its
    /// whole body back and lets fly a wide backhand that bats everything in front of it away
    /// (a flat sweep of light round its front), then rides the turn into a low spinning kick that
    /// takes the legs of everyone round it (a ring of light at its feet, a spiral of dust): they
    /// go down on their backs, scattered. II: the sweep is wider and it ends with a stamp whose
    /// shock knocks down whoever is still standing. III: the kick spins twice, the hunter's ring
    /// closing on the ground, wider still. Made for swarms of small creatures and crowds of
    /// people (the Brain only uses it there), and only worth it on weak ones: at least three of
    /// them (two when surrounded or chained). Damage to each (x the user's damage): I 0.45 + 0.6,
    /// II 0.5 + 0.7 + 0.3, III 0.55 + 0.75 + 0.5 + 0.4. Leaves Scattered.
    /// </summary>
    internal sealed class RabbleSweepTech : Technique
    {
        public override string Id => "rabble_sweep";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Area;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Area | Form.Hunt | Form.Kick;
        public override Prey Prey => Prey.Swarm | Prey.Humanoid;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 8f, 14f, 22f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Thrown;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 1.7f : level == 2 ? 2.1f : 2.5f;
        }

        private static float Back(int level)
        {
            return level <= 1 ? 0.45f : level == 2 ? 0.5f : 0.55f;
        }

        private static float Kick(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.7f : 0.75f;
        }

        public static float Total(int level)
        {
            return Back(level) + Kick(level) + (level >= 2 ? 0.3f : 0f) + (level >= 3 ? 0.6f : 0f);
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>A weak foe: plain folk or smaller, or one this sweep all but finishes.</summary>
        private static bool Weak(Ctx c, Actor v, float cm, int level)
        {
            if (Tiers.Of(v) == Tier.None && Weights.Species(v) <= Weight.Folk)
            {
                return true;
            }
            return v.getHealth() <= c.Expected(v, Total(level) * 1.3f) * cm;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Choreo.KOf(c.Self);
            Plan p = Choreo.PickArea(c, this, level, r, 2.2f, Total(level), c.Forced ? 1 : 2);
            if (p == null)
            {
                return null;
            }
            // only the weak creatures it is made for count: the rabble
            _near.Clear();
            c.CountNear(p.Point, r + 0.3f, _near);
            int n = 0;
            float score = 0f;
            Actor center = null;
            float cd = float.MaxValue;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!HwLoKit.IsPrey(c, this, v) || !c.Allowed(v, out float cm) || !c.Forced && !Weak(c, v, cm, level))
                {
                    continue;
                }
                n++;
                score += c.Value(v, c.Expected(v, Total(level)), cm);
                float d = (v.current_position - p.Point).sqrMagnitude;
                if (d < cd)
                {
                    cd = d;
                    center = v;
                }
            }
            int need = c.Forced ? 1 : c.Linked || c.Surrounded ? 2 : 3;
            if (center == null || n < need)
            {
                return null;
            }
            // (no stepping into the water or through a wall to reach them)
            if (!c.Forced && Vector2.Distance(c.Pos, p.Point) > 0.6f && !Combat.ClearPath(c.Pos, p.Point))
            {
                return null;
            }
            p.Target = center;
            p.Count = n;
            p.Score = c.Forced ? Mathf.Max(score, 1f) : score + (c.Surrounded ? 6f * n : 0f);
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float r = Radius(level) * m.Kk;
            // into the thick of them
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = 0.06f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = Into,
                To = new Shape(14f, 1.1f, 0.9f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.04f,
                Dust = 0.06f,
                Forms = Forms,
                If = mm => Vector2.Distance(mm.Stance, mm.Point) > 0.6f * mm.Kk
            });
            // the whole body winds back
            m.PoseTo(0.11f, new Shape(-22f, 0.9f, 1.08f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind).Shows(Forms);
            // the backhand: a flat sweep of light round its front, everything in it batted away
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.1f,
                To = new Shape(18f, 1.2f, 0.86f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Forms,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        int rp = Mathf.Clamp(Mathf.RoundToInt(r * 0.8f / mm.Px), 6, 60);
                        Kit.Swish(mm, Art.Arc(rp, 0.45f, 165f, 385f, level >= 3 ? 3 : 2), 0.2f, new Vector2(0f, 0f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                        Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -8f, 0.08f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                }
            }.On(0.45f, mm => Sweep(mm, r * 0.95f, 115f, Back(level), Hurt.Reel, level <= 1 ? 0.8f : 1f, 0.9f)));
            // the turn carries on into a low spinning kick: the legs of everyone round it
            AddSpin(m, r, Kick(level), level >= 3 ? 1 : 0, false);
            if (level >= 3)
            {
                m.Do(mm => HwLoKit.HuntRing(mm, mm.Pose.Ground, Radius(level) + 0.4f, 0.3f));
                AddSpin(m, r * 1.1f, 0.5f, 2, true);
            }
            if (level >= 2)
            {
                // a stamp: its shock knocks down whoever still stands
                m.PoseTo(0.08f, new Shape(-6f, 0.92f, 1.1f, 0.12f), Ease.OutQuad, Tempo.Wind);
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.07f,
                    To = new Shape(8f, 1.24f, 0.8f, 0f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = Form.Area | Form.Kick | Form.Ground,
                    Enter = mm =>
                    {
                        if (mm.Vis)
                        {
                            Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                            Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                        }
                        Choreo.Sound("event:/SFX/HIT/HitGeneric", mm.A.current_tile);
                    }
                });
                m.Shockwave(r + 0.6f * m.Kk, level >= 3 ? 0.4f : 0.3f, Hurt.Knockdown, 0.5f, 12f, mm => mm.Pose.Ground);
            }
            m.Leaves(Link.Scattered);
            m.Hold(0.08f, false, Tempo.Follow);
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>Where it steps to: into the crowd's middle (a little short of it), on solid ground.</summary>
        private static Vector2 Into(ChoreoMove m)
        {
            Vector2 d = m.Point - m.Stance;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                return m.Stance;
            }
            Vector2 q = Combat.Clamp(m.Stance + d / len * (len - 0.3f * m.Kk));
            return Combat.SafeSpot(q) && Combat.ClearPath(m.Stance, q) ? q : m.Stance;
        }

        /// <summary>
        /// A low spinning kick round the body ('turns' extra turns: 0 one, 1 a second, 2 a wide second):
        /// the sprite turns on the spot, a flat ring of light at its feet, dust spiralling out, and at
        /// the turn's middle everyone within 'r' is swept off their feet.
        /// </summary>
        private static void AddSpin(ChoreoMove m, float r, float mult, int turns, bool wide)
        {
            float baseFace = 1f;
            float spins = turns >= 1 ? 2f : 1f;
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = turns >= 1 ? 0.3f : 0.22f,
                To = new Shape(10f, 1.14f, 0.86f, 0.02f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Area | Form.Kick | Form.Low | Form.Spin | Form.Hunt,
                Enter = mm =>
                {
                    baseFace = mm.Fwd;
                    if (mm.Vis)
                    {
                        int rp = Mathf.Clamp(Mathf.RoundToInt(r / mm.Px), 6, 70);
                        float offY = -mm.Pose.PivotUp + 1.5f;
                        Kit.Swish(mm, Art.Arc(rp, 0.38f, 0f, 360f * spins, wide ? 3 : 2), mm.BeatDur * 1.3f + 0.05f, new Vector2(0f, offY), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 4);
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, wide ? 12 : 8, wide ? 44f : 34f, 0.5f, mm.Kk);
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                },
                Every = (mm, k) =>
                {
                    // it turns right round on the spot: the sprite narrows, flips, widens
                    float t = Beat.Apply(Ease.InOutSine, k) * spins;
                    float c = Mathf.Cos(Mathf.PI * 2f * t);
                    mm.Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
                    float frac = t - Mathf.Floor(t);
                    bool back = frac > 0.25f && frac < 0.75f;
                    mm.Pose.FaceRight = back ? baseFace < 0f : baseFace > 0f;
                    if (mm.Vis && Random.value < 0.35f)
                    {
                        float ang = t * Mathf.PI * 2f;
                        Vector2 at = mm.Pose.Ground + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * r * 0.7f;
                        ChoreoFx.HeelDust(at, at - mm.Pose.Ground, mm.Px, mm.A.current_tile, mm.Kk * 0.8f);
                    }
                },
                Exit = mm => mm.Pose.FaceRight = mm.Fwd > 0f
            };
            b.On(turns >= 1 ? 0.35f : 0.5f, mm => Sweep(mm, r, 181f, mult, Hurt.Knockdown, 0.7f, 1f));
            if (turns >= 1)
            {
                b.On(0.8f, mm => Sweep(mm, r, 181f, mult * 0.5f, Hurt.Knockdown, 0.5f, 0.8f));
            }
            m.Add(b);
        }

        /// <summary>
        /// Everyone of its enemies within 'r' and 'half' degrees of its facing (181: all round) is hit
        /// (x 'mult'; it may evade an area blow, never parry it) and reacts 'hurt', pushed away.
        /// </summary>
        private static int Sweep(ChoreoMove m, float r, float half, float mult, Hurt hurt, float push, float power)
        {
            Choreo.Foes(m.A, m.Pose.Ground, r, _near);
            int n = 0;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                Vector2 rel = u.current_position - m.Pose.Ground;
                if (half < 180f && rel.sqrMagnitude > 1e-4f && Vector2.Angle(m.Dir, rel) > half)
                {
                    continue;
                }
                if (!Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                float near = 1f - Mathf.Clamp01(rel.magnitude / Mathf.Max(0.1f, r));
                HitResult res = Combat.Hit(m.A, u, mult * cm * Mathf.Lerp(0.75f, 1f, near), true, false, 0f, m);
                n++;
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, m.A, m, Impact.Of(hurt, rel, push * Mathf.Lerp(0.7f, 1f, near), power));
                }
                else
                {
                    Victims.Flash(u, Color.white, 0.05f);
                }
                if (m.Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, hurt == Hurt.Knockdown ? 0.2f : 0.55f), rel, m.Px, m.Kk, 0.7f * power, m.Hue, m.TopLayer);
                }
                ChoreoFx.BlowSound(hurt == Hurt.Knockdown ? Limb.Foot : Limb.Fist, 0.7f, false, u.current_tile);
                if (Dev.On) Dev.Trace("rabble_sweep " + m.Id + " -> " + u.asset.id + "#" + u.Sid() + " hp " + u.getHealth() + (res.Killed ? " killed" : ""));
            }
            if (n > 0)
            {
                m.HoldFor(0.025f * m.ImpactK);
                m.SetLink(Link.Scattered, null);
            }
            return n;
        }
    }
}
