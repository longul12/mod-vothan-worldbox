using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BULL RUSH (Iron Fist, Adept). It drops its lead shoulder, paws the ground once, and charges:
    /// a heavy run of 4-6 tiles, bobbing with every stride, a wake of dust behind it, straight
    /// through the foe it aims at and whoever stands in the corridor. The first 1 / 2 / 3 it meets are
    /// bowled over: a crunch, a beat of hit-stop, and each one flies 2 / 3 / 4 tiles, tumbling off to
    /// the side it stood on (the one it aimed at straight ahead), and lands on its back. Then it skids
    /// to a stop leaning back. A creature too big to move (a giant, a dragon on the ground) stops it
    /// dead: it bounces off. II leaves afterimages, III speed lines and a furrow torn along its track
    /// (a Master's feet break the ground). A gap closer: good onto archers and mages, onto a body
    /// thrown into a crowd. Answerable as a dash (a cross counter meets it with a fist).
    /// Damage (x the user's damage) to each: I 1.4, II 1.8, III 2.3.
    /// </summary>
    internal sealed class ShoulderChargeTech : Technique
    {
        public override string Id => "shoulder_charge";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Mobility;
        public override School School => School.IronFist;
        public override Form Forms => Form.Dash | Form.Body | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Thrown || link == Link.Scattered;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.3f;
        }

        /// <summary>The longest run (tiles, x the user's size).</summary>
        public static float Len(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        /// <summary>How far each bowled body flies.</summary>
        public static float Knock(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 3f : 4f;
        }

        private static readonly List<Actor> _ray = new List<Actor>();
        private static readonly List<float> _along = new List<float>();

        /// <summary>
        /// A run at one foe 1.6 tiles away or more (a charge needs room), along a clear corridor to a
        /// safe spot a little past it: worth the foes it will bowl over (the first 1/2/3 along the way),
        /// more onto a shooter keeping its distance and when it catches several.
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float len = Len(level) * Mathf.Lerp(1f, k, 0.5f);
            int max = Mathf.Clamp(level, 1, 3);
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                Actor v = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > len - 0.3f)
                {
                    break;
                }
                if (d < (c.Forced ? 0.8f : 1.6f) || c.ForceTarget != null && v != c.ForceTarget)
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                Vector2 dir = (v.current_position - c.Pos) / d;
                // the full run if the corridor is open, else just past the foe
                float run = len;
                Vector2 end = Combat.Clamp(c.Pos + dir * run);
                for (int t = 0; t < 3 && (!Combat.SafeSpot(end) || !Combat.ClearPath(c.Pos, end)); t++)
                {
                    run = t == 0 ? Mathf.Min(len, d + 1.2f * k) : d + 0.3f;
                    end = Combat.Clamp(c.Pos + dir * run);
                }
                if (!Combat.SafeSpot(end) || !Combat.ClearPath(c.Pos, end))
                {
                    continue;
                }
                // the corridor: the first 'max' it meets along the run
                _ray.Clear();
                c.OnRay(dir, run, 0.55f, _ray);
                if (!_ray.Contains(v))
                {
                    _ray.Add(v);
                }
                SortAlong(c.Pos, dir);
                float score = 0f;
                int n = 0;
                bool aimIn = false;
                for (int j = 0; j < _ray.Count && n < max; j++)
                {
                    Actor u = _ray[j];
                    if (!c.Allowed(u, out float cu))
                    {
                        continue;
                    }
                    score += c.Value(u, c.Expected(u, Mult(level)), cu);
                    n++;
                    aimIn |= u == v;
                }
                if (!aimIn && !c.Forced)
                {
                    continue;   // it would stop on others before it gets there
                }
                if (!c.Forced && n == 1 && Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute && v.getHealth() <= c.Expected(v, 1.2f) * cm)
                {
                    continue;   // a lone foot soldier one blow ends: not worth the run
                }
                if (c.State(v).Ranged && d > 3f)
                {
                    score += 12f;   // onto the archer, the mage
                }
                score += (n - 1) * 8f;
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = v, Point = end, Dir = dir, Count = n, Score = score, Data = c.Forced };
                }
            }
            return best;
        }

        /// <summary>Sorts _ray by distance along the run (a handful: insertion sort, no garbage).</summary>
        private static void SortAlong(Vector2 from, Vector2 dir)
        {
            _along.Clear();
            for (int i = 0; i < _ray.Count; i++)
            {
                _along.Add(Vector2.Dot(_ray[i].current_position - from, dir));
            }
            for (int i = 1; i < _ray.Count; i++)
            {
                Actor a = _ray[i];
                float x = _along[i];
                int j = i - 1;
                while (j >= 0 && _along[j] > x)
                {
                    _ray[j + 1] = _ray[j];
                    _along[j + 1] = _along[j];
                    j--;
                }
                _ray[j + 1] = a;
                _along[j + 1] = x;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            return Rush.Build(m, p) && m.Start();
        }
    }

    /// <summary>The bull rush's choreography and its bowling.</summary>
    internal static class Rush
    {
        /// <summary>A shoulder: a heavy body blow that sends whoever it meets flying (Deliver's shape).</summary>
        private static readonly BlowShape Shoulder = new BlowShape
        {
            Limb = Limb.Body,
            Weight = 1.25f,
            Hurt = Hurt.Launch,
            Push = 2f,
            Lift = 0.75f,
            Contact = 0.5f,
            Heavy = true,
            Forms = Form.Dash | Form.Body | Form.Heavy
        };

        private const Form Charging = Form.Dash | Form.Body | Form.Heavy;
        private static readonly List<Actor> _near = new List<Actor>();

        public static bool Build(ChoreoMove m, Plan p)
        {
            int level = m.Level;
            int max = Mathf.Clamp(level, 1, 3);
            Vector2 end = p.Point;
            Vector2 start = m.Stance;
            HashSet<long> met = new HashSet<long>();
            int count = 0;
            float stepT = 0f;
            bool furrowed = false;
            m.Face(p.Dir.sqrMagnitude > 1e-4f ? p.Dir : end - start);
            // THE LOWERED SHOULDER: crouched, leaning in, a hoof's paw at the ground (dust kicked back)
            m.PoseTo(0.14f, new Shape(26f, 1.16f, 0.82f, 0f, -0.06f), Ease.OutCubic, Tempo.Wind)
                .Shows(Charging)
                .OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.HeelDust(mm.Stance, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk * 1.2f);
                        Fx.LandingPuff(mm.Stance - mm.Dir * 2f * mm.Px, mm.Px, mm.A.current_tile);
                    }
                })
                .At(0.7f, mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.BodyPos() + new Vector2(mm.Fwd * 3f, 2f) * mm.Px, mm.Px, 0.14f, mm.Hue.Core, 0.8f);
                    }
                });
            // THE RUSH: heavy strides, a dust wake, everyone in the corridor bowled over
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.04f,
                PerTile = level <= 1 ? 0.06f : level == 2 ? 0.052f : 0.045f,
                Route = Route.Line,
                PathEase = Ease.Linear,
                Dest = mm =>
                {
                    Vector2 e = end;
                    for (int i = 0; i < 4; i++)
                    {
                        if (Combat.SafeSpot(e) && Combat.ClearPath(mm.Stance, e))
                        {
                            return e;
                        }
                        e = Vector2.Lerp(mm.Stance, e, 0.7f);
                    }
                    return mm.Stance;
                },
                To = new Shape(30f, 1.12f, 0.88f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Dust = 0.03f,
                Ghosts = level >= 2 ? 0.04f : 0f,
                Lines = level >= 3 ? 0.04f : 0f,
                Forms = Charging,
                Enter = mm =>
                {
                    start = mm.Stance;
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 6, 26f, 0.4f, mm.Kk);
                    }
                },
                Every = (mm, k) =>
                {
                    // strides: the body bobs with each footfall, dust thrown back at every step
                    float stride = 0.1f * mm.ActK;
                    float bob = Mathf.Abs(Mathf.Sin(Mathf.PI * mm.BeatT / stride));
                    mm.Pose.Height = 0.06f * mm.Kk * bob;
                    mm.Pose.SY *= 1f - 0.05f * (1f - bob);
                    stepT -= Clock.Dt;
                    if (stepT <= 0f && mm.Vis)
                    {
                        stepT = stride;
                        ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                    }
                    Bowl(mm, met, ref count, max);
                    if (Dev.On)
                    {
                        Dev.Watch(mm.Pose.Ground + new Vector2(0f, 1.2f * mm.Kk));   // (dev captures follow the run)
                    }
                }
            });
            // THE SKID: it digs its heels in and slides to a stop, leaning back
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.16f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm => mm.Ahead(0.5f * mm.Kk),
                To = new Shape(-14f, 0.94f, 1.06f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Dust = 0.03f,
                Forms = Form.Body,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.Skid(mm.Stance, mm.Ahead(0.5f * mm.Kk), mm.Px, mm.A.current_tile);
                    }
                    Furrow(mm, start, ref furrowed);
                }
            }).Label("skid");
            m.Do(mm => mm.JumpToRecovery());
            // THE BOUNCE: it ran into something it can't move: knocked back a step, rocked
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.18f,
                Route = Route.Arc,
                Apex = 0.18f,
                PathEase = Ease.OutQuad,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.7f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                To = new Shape(-24f, 0.9f, 1.1f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Body,
                Enter = mm =>
                {
                    Furrow(mm, start, ref furrowed);
                    Choreo.Sound("event:/SFX/HIT/HitStone", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Fx.Afterimage(mm, 0.14f, new Color(1f, 1f, 1f, 0.7f), Fx.WithA(mm.Hue.Edge, 0f));
                    }
                }
            }).Label("bounce");
            m.Recover(0.25f);
            return true;
        }

        /// <summary>
        /// Everyone of its enemies its shoulder meets this frame (not yet met) is bowled over, until it has
        /// met 'max'; then it stops. A body too heavy to move stops it at once (it bounces off).
        /// </summary>
        private static void Bowl(ChoreoMove m, HashSet<long> met, ref int count, int max)
        {
            if (count >= max)
            {
                return;
            }
            Vector2 at = m.Pose.Ground + m.Dir * 0.2f * m.Kk;
            Choreo.Foes(m.A, at, 0.42f * m.Kk + 0.22f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                long id = u.Sid();
                if (met.Contains(id) || !Choreo.Fair(m.A, u, out _, m.Forced))
                {
                    continue;
                }
                met.Add(id);
                if (!m.Touch(u))
                {
                    if (m.Finished || m.Interruptible)
                    {
                        return;   // its target answered the charge: the engine plays the whiff
                    }
                    continue;
                }
                count++;
                bool last = count >= max;
                bool solid = !Victims.Movable(u, m.A);
                // off to the side it stood on (the one it aimed at: straight on)
                Vector2 rel = u.current_position - m.Pose.Ground;
                float cross = m.Dir.x * rel.y - m.Dir.y * rel.x;
                Vector2 away = u == m.V || Mathf.Abs(cross) < 0.05f ? m.Dir : (m.Dir * 0.8f + Choreo.Perp(m.Dir) * Mathf.Sign(cross) * 0.6f).normalized;
                Hit h = new Hit(Blow.Palm, ShoulderChargeTech.Mult(m.Level))
                {
                    Hurt = Hurt.Launch,
                    Push = ShoulderChargeTech.Knock(m.Level) * (u == m.V ? 1.15f : 1f),
                    Lift = 0.6f + 0.15f * m.Level,
                    Daze = 0.5f,
                    Power = 1.25f,
                    Finisher = last && m.Level >= 2
                };
                Vector2 contact = Choreo.BodyAt(u, 0.5f);
                m.Deliver(u, h, Shoulder, contact, away);
                m.HoldFor(0.035f * m.ImpactK);
                Choreo.Sound("event:/SFX/HIT/HitGeneric", u.current_tile);
                if (m.Vis)
                {
                    Kit.DustRing(Victims.GroundOf(u), m.Px, u.current_tile, 8, 34f, 0.45f, m.Kk);
                    Kit.SpeedLines(contact, away, m.Px, 3, 3f, 0.12f, Fx.WithA(m.Hue.Edge, 0.85f));
                }
                if (solid)
                {
                    m.Jump("bounce");
                    return;
                }
                if (last)
                {
                    m.Jump("skid");
                    return;
                }
            }
        }

        /// <summary>III (a Master's feet and up): the ground torn up along the track it ran.</summary>
        private static void Furrow(ChoreoMove m, Vector2 start, ref bool done)
        {
            if (done || m.Level < 3)
            {
                return;
            }
            done = true;
            Vector2 to = m.Stance;
            if ((to - start).sqrMagnitude < 1f)
            {
                return;
            }
            Ruin.Furrow(m.A, start + (to - start).normalized * 0.6f, to, 0.45f, m.Scale, m.Pose.Ground);
        }
    }
}
