using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// AFTERIMAGE PRISON (Phantom Step, Grandmaster). It darts in and runs one lap round the foe so fast
    /// that it leaves itself behind: one afterimage after another stays standing on the circle, facing
    /// in, until the foe is ringed by bars of itself; light threads join them at chest height and a
    /// ring burns on the ground: a cage. Then the bars strike, one after another and ever faster, each
    /// lunging in from its place and back, opposite sides in turn, so the prisoner is knocked from
    /// one side of the cage to the other like a ball and can't fall. At the end the cage closes on it
    /// (a thread of light from every bar into its body) and the original brings it down. Distinct
    /// from Mirage Dance (copies circling and closing in to launch it): here nothing circles once the
    /// cage is shut; the bars stand, the foe bounces between them, and it ends driven into the ground.
    ///
    /// Levels: I six bars, 8 blows (x1.6 together), a palm knocks it down (x1.2). II eight bars, 14
    /// blows (x2.4), the threads bind it and a heel dropped from a hop drives it into the ground (x1.5),
    /// every bar going up as a column of light. III twelve bars, 22 blows (x3.2), the heel (x2.2)
    /// leaves the whole floor of the cage a crater.
    /// </summary>
    internal sealed class AfterimagePrisonTech : Technique
    {
        public override string Id => "afterimage_prison";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Clone | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 38f, 60f, 90f };
        public override float[] Cooldown => new[] { 14f, 15f, 16f };

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static int Bars(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 8 : 12;
        }

        private static int Blows(int level)
        {
            return level <= 1 ? 8 : level == 2 ? 14 : 22;
        }

        private static float BlowsTotal(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.4f : 3.2f;
        }

        private static float Last(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.5f : 2.2f;
        }

        public static float Total(int level)
        {
            return BlowsTotal(level) + Last(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 4f, Total = Total(1), Crowd = 0.12f, Alone = 14f, IntoGuard = 1f, Worth = 20f },
            new MeleeWant { Reach = 4.3f, Total = Total(2), Crowd = 0.12f, Alone = 16f, IntoGuard = 1f, Worth = 26f },
            new MeleeWant { Reach = 4.6f, Total = Total(3), Crowd = 0.1f, Alone = 18f, IntoGuard = 1f, Worth = 32f }
        };

        /// <summary>The cage's radius round 'v' (tiles): a body's length out from its edge.</summary>
        private static float CageR(Actor v, float k)
        {
            return Mathf.Clamp(v.stats["size"] * 0.5f + 0.95f * k, 1f, 4.5f);
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // room for the cage: most of the circle round it must be ground to stand on
            Actor v = p.Target;
            float r = CageR(v, Choreo.KOf(c.Self));
            int ok = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                Vector2 q = v.current_position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (Combat.SafeSpot(q) || Choreo.Wading(q))
                {
                    ok++;
                }
            }
            return ok >= 6 ? p : null;
        }

        /// <summary>One cage: its bars (frozen frames) and where they stand, the threads, the blows' order.</summary>
        private sealed class State
        {
            public readonly List<Particle> Bars = new List<Particle>(12);
            public readonly List<Vector2> Base = new List<Vector2>(12);
            public readonly List<Vector2> Feet = new List<Vector2>(12);
            public readonly List<float> Lunge = new List<float>(12);
            public readonly List<Particle> Threads = new List<Particle>(14);
            public Particle Floor;
            public float R;
            public int Dropped, Blown;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            int bars = Bars(level), blows = Blows(level);
            State st = new State { R = CageR(m.V, m.Kk) };
            Form shows = Form.Clone | Form.Flurry;
            // in to the cage's edge
            m.Dash(mm => CagePoint(mm, st, 0f), 0.026f);
            // one lap round it, leaving itself behind: the bars
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = level <= 1 ? 0.32f : level == 2 ? 0.36f : 0.44f,
                Route = Route.Orbit,
                Dest = mm => mm.Stance,
                To = new Shape(12f, 1.12f, 0.92f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.02f,
                Dust = 0.05f,
                Intangible = true,
                NeedsVictim = true,
                Forms = shows,
                Enter = mm =>
                {
                    mm.OrbitCenter = mm.VG;
                    mm.OrbitTurn = Mathf.PI * 2f;
                    mm.OrbitR = st.R;
                    st.Dropped = 0;
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                },
                Every = (mm, k) =>
                {
                    while (st.Dropped < bars && k >= (st.Dropped + 0.5f) / bars)
                    {
                        DropBar(mm, st);
                    }
                }
            });
            // the cage shuts: threads between the bars, a ring on the ground
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.12f,
                To = new Shape(-4f, 1.05f, 0.96f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm =>
                {
                    while (st.Dropped < bars)
                    {
                        DropBar(mm, st);
                    }
                    Shut(mm, st);
                }
            });
            // the bars strike, opposite sides in turn, faster and faster
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.25f,
                To = new Shape(-4f, 1.05f, 0.96f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                NeedsVictim = true,
                Enter = mm => st.Blown = 0,
                Every = (mm, k) => Pummel(mm, st, blows, k)
            });
            // the cage closes on it: threads shoot from every bar into the prisoner and bind it (II, III)
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = level <= 1 ? 0.04f : 0.12f,
                Forms = shows,
                Enter = mm => Bind(mm, st)
            });
            // and the original brings it down: a palm (I), a heel dropped from a hop that drives it into
            // the ground (II, III), while every bar goes up as a column of light (III: the cage is a crater)
            Hit last = level <= 1
                ? new Hit(Blow.Palm, Last(level)) { Hurt = Hurt.Knockdown, Push = 0.6f, Finisher = true, WindScale = 0.7f, Power = 1.3f }
                : new Hit(Blow.HeelDrop, Last(level))
                {
                    Hurt = Hurt.DriveIn,
                    Crater = level == 2 ? 0.7f : 1.1f,
                    Daze = 0.8f,
                    Finisher = true,
                    WindScale = 1.1f,
                    Power = 1.4f,
                    OnLand = (mm, v, r) => Columns(mm, st)
                };
            m.Strike(last);
            m.Recover(0.26f).OnEnter(mm => Clear(mm, st));
            return m.Start();
        }

        private static Vector2 CagePoint(ChoreoMove m, State st, float turn)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 rel = m.Stance - m.VG;
            float a = (rel.sqrMagnitude > 1e-4f ? Mathf.Atan2(rel.y, rel.x) : 0f) + turn;
            Vector2 q = Combat.Clamp(m.VG + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * st.R);
            return Combat.SafeSpot(q) || Choreo.Wading(q) ? q : m.StandSpot(m.Stand);
        }

        /// <summary>A bar: the frame it is in now stays standing on the circle, turned to face the prisoner, set for a blow.</summary>
        private static void DropBar(ChoreoMove m, State st)
        {
            st.Dropped++;
            Pose keep = m.Pose;
            m.Pose.FaceRight = m.VG.x >= m.Pose.Ground.x;
            m.Pose.Rot = -6f;
            m.Pose.SX = 1.04f;
            m.Pose.SY = 0.97f;
            m.Pose.Height = 0f;
            Color c = Fx.WithA(Color.Lerp(m.Hue.Ghost, m.Hue.Main, 0.35f), 0.78f);
            Particle p = PsHiKit.Still(m, c, 2);
            m.Pose = keep;
            if (p == null)
            {
                return;
            }
            st.Bars.Add(p);
            st.Base.Add(p.Pos);
            st.Feet.Add(m.Pose.Ground);
            st.Lunge.Add(-1f);
            if (m.Vis)
            {
                Fx.Ring(m.Pose.Ground, m.Px, 0.14f, 2f, 6f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.4f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
        }

        /// <summary>The cage shuts: threads of light join the bars at chest height and down to the ground, a ring burns under them.</summary>
        private static void Shut(ChoreoMove m, State st)
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
            if (!m.Vis || st.Bars.Count < 2)
            {
                return;
            }
            float px = m.Px;
            float chest = m.Hpx * 0.55f * px;
            Color thread = Fx.WithA(Color.Lerp(m.Hue.Core, m.Hue.Edge, 0.4f), 0.8f);
            for (int i = 0; i < st.Feet.Count; i++)
            {
                Vector2 a = st.Feet[i] + new Vector2(0f, chest);
                Vector2 b = st.Feet[(i + 1) % st.Feet.Count] + new Vector2(0f, chest);
                Particle line = Kit.Line(a, b, px, 1f, 1, FxLayer.Over, 3, thread);
                if (line != null)
                {
                    st.Threads.Add(m.Glue(line));
                }
            }
            int rx = Mathf.Clamp(Mathf.RoundToInt(st.R / px), 4, 120);
            Particle floor = Fx.Spawn(Art.Ring(rx, 1, Art.Ground, m.Level >= 3 ? 12 : 0), m.VG, px, 1f, FxLayer.Ground, 5);
            if (floor != null)
            {
                floor.Snap = true;
                floor.Color(Fx.WithA(m.Hue.Edge, 0.85f));
                st.Floor = m.Glue(floor);
            }
            Fx.Glint(m.VG + new Vector2(0f, chest * 2f), px, 0.16f, m.Hue.Core, 1f);
        }

        /// <summary>A frame of the beating: bars lunge in and back; each blow knocks the prisoner towards the opposite side.</summary>
        private static void Pummel(ChoreoMove m, State st, int blows, float k)
        {
            float dt = Clock.Dt;
            int n = st.Bars.Count;
            // the bars that are lunging move in and back
            for (int b = 0; b < n; b++)
            {
                float t = st.Lunge[b];
                if (t < 0f)
                {
                    continue;
                }
                t += dt;
                st.Lunge[b] = t;
                Particle p = st.Bars[b];
                if (p == null || !p.Alive)
                {
                    continue;
                }
                float u = Mathf.Clamp01(t / 0.11f);
                Vector2 toward = (m.VG - st.Feet[b]) * 0.42f;
                p.Pos = st.Base[b] + toward * Mathf.Sin(Mathf.PI * u);
                p.Euler.z = 16f * Mathf.Sin(Mathf.PI * u);   // (leaning in, as a body's Rot does whichever way it faces)
                if (u >= 1f)
                {
                    st.Lunge[b] = -1f;
                    p.Pos = st.Base[b];
                    p.Euler.z = 0f;
                }
            }
            while (st.Blown < blows && k >= Mathf.Pow(st.Blown / (float)blows, 0.85f))
            {
                int j = st.Blown++;
                if (!m.VOk)
                {
                    if (!m.Retarget(1.5f))
                    {
                        m.Lose();
                        return;
                    }
                    if (!m.Touch(m.V))
                    {
                        return;
                    }
                }
                else if (j == 0 && !m.Touch(m.V))
                {
                    return;   // answered: the engine ends the move
                }
                // opposite sides in turn: the prisoner is knocked back and forth across the cage
                int b = n > 0 ? ((j >> 1) + ((j & 1) == 0 ? 0 : n / 2)) % n : 0;
                Vector2 from = n > 0 ? st.Feet[b] : m.Stance;
                if (n > 0)
                {
                    st.Lunge[b] = 0f;
                }
                Vector2 dir = PsHiKit.DirTo(from, m.VG, m.Dir);
                Vector2 at = Choreo.BodyAt(m.V, Random.Range(0.35f, 0.75f)) - dir * 1f * m.Px;
                if (m.Vis)
                {
                    Vector2 root = from + new Vector2(0f, m.Hpx * 0.5f * m.Px);
                    ChoreoFx.Smear(Vector2.Lerp(root, at, 0.35f), at, m.Px, 0.06f, m.Level >= 3 ? 2 : 1, 1, m.Hue.Core, m.Hue.Edge);
                    Particle star = Fx.Spawn(Art.HitStars[j % 3 == 0 ? 1 : 0], at, m.Px, 0.06f, m.TopLayer, 20);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 90f);
                        star.Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    }
                    if (j % 3 == 0)
                    {
                        Kit.Sparks(at, m.Px, 2, 40f, 90f, m.Hue.Edge);
                    }
                }
                HitResult r = Combat.Hit(m.A, m.V, BlowsTotal(m.Level) / blows * m.Cm, false, false, 0f, m);
                m.LastHit = r;
                Victims.Hurt(m.V, m.A, m, Impact.Of(Hurt.Flinch, dir, 0.07f, 0.65f));
                if ((j & 1) == 0)
                {
                    ChoreoFx.BlowSound(Limb.Fist, 0.7f, m.Armed, m.V.current_tile);
                }
                if (j % 4 == 3)
                {
                    Victims.Flash(m.V, Color.white, 0.04f);
                }
                Combat.Pin(m.V, 0.4f);
                m.SetLink(Link.Close, m.V);
            }
        }

        /// <summary>The cage closes on the prisoner: a thread of light from every bar into its body, and it is held (II, III).</summary>
        private static void Bind(ChoreoMove m, State st)
        {
            if (m.V == null || m.Level < 2)
            {
                return;
            }
            Combat.Pin(m.V, 0.6f);
            Victims.Flash(m.V, m.Hue.Core, 0.08f);
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 body = Choreo.BodyAt(m.V, 0.5f);
            for (int b = 0; b < st.Bars.Count; b++)
            {
                Vector2 from = st.Feet[b] + new Vector2(0f, m.Hpx * 0.55f * px);
                Kit.Line(from, body, px, 0.3f, 1, FxLayer.Over, 11, Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
            Fx.Ring(body, px, 0.2f, 12f * m.Kk, 3f, 1, 1, FxLayer.Over, 10, 1f, 2f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
        }

        /// <summary>The heel lands: every bar goes up as a column of light and is gone; at III the cage's floor is a crater.</summary>
        private static void Columns(ChoreoMove m, State st)
        {
            if (m.Vis)
            {
                float px = m.Px;
                for (int b = 0; b < st.Feet.Count; b++)
                {
                    Vector2 f = st.Feet[b];
                    Kit.Line(f, f + new Vector2(0f, (2.5f + 1.2f * m.Level) * m.Kk), px, 0.26f, 1, m.TopLayer, 12, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Main, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                    Kit.Flash(f + new Vector2(0f, 2f * px), px, 2, 0.08f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer);
                }
                if (m.V != null)
                {
                    PsHiKit.Shards(Choreo.BodyAt(m.V, 0.5f), m.VG, m.Px, m.Kk, m.Hue, 6 + st.Bars.Count);
                }
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
            Clear(m, st);
            if (m.Level >= 3)
            {
                Ruin.Blast(m.A, m.VG, st.R * 0.8f, Scale.Heavy, m.Pose.Ground);
            }
        }

        /// <summary>The cage is gone: bars, threads, floor ring.</summary>
        private static void Clear(ChoreoMove m, State st)
        {
            for (int i = 0; i < st.Bars.Count; i++)
            {
                if (st.Bars[i] != null)
                {
                    PsHiKit.Fade(m, st.Bars[i], 0.08f, Fx.WithA(m.Hue.Core, 0.8f));
                }
            }
            st.Bars.Clear();
            for (int i = 0; i < st.Threads.Count; i++)
            {
                m.Unglue(st.Threads[i]);
            }
            st.Threads.Clear();
            if (st.Floor != null)
            {
                m.Unglue(st.Floor);
                st.Floor = null;
            }
        }
    }
}
