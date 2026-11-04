using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GATHERINGS: the great team techniques, where five to ten fighters drop what they are doing and
    /// act as one. Each needs its crowd and its moment (the AI only calls one when the allies are
    /// there and free, and the enemy is worth it), and every partner visibly takes part: they run to
    /// their places, plant themselves, pour their power in, and the result is as big as the crowd.
    ///   Seal of the Ten Heavens  (6-10, against one terrible foe: a God, a Grandmaster, a titan or a
    ///                            kaiju) a ring is formed round it, a seal drawn on the ground between
    ///                            them, chains of light bind it, and a column falls from the sky on it
    ///   Hundred Volley           (5-10, even spread far apart) a raised hand, and every one of them
    ///                            looses at the same enemy crowd: the shots arc in from everywhere and
    ///                            land together
    ///   Choir of Falling Stars   (6-10) hands raised, beams of every colour into the sky above the
    ///                            enemy, a star forming where they meet, and it falls
    ///   Iron Wedge               (5-10 fighters) a wedge forms behind the leader, they stamp as one, and
    ///                            charge through the enemy line together, bowling it over
    /// </summary>
    internal abstract class GatherMove : Move
    {
        protected sealed class Mate
        {
            public Actor U;
            public TeamPartner P;
            public Vector2 From, Spot;
            public Color Col;
            public float K, Px, Ph;
            public bool Ok => P != null && !P.Gone;
        }

        protected readonly List<Mate> Mates = new List<Mate>();
        protected readonly Actor Focus;
        protected readonly long FocusId;
        protected Vector2 Goal;
        protected readonly Hue Hue;
        protected int Phase;
        protected float Pt;
        protected readonly float K, PxA;
        protected Vector2 Home;

        protected GatherMove(Actor a, int level, Actor focus, Vector2 goal) : base(a)
        {
            Level = level;
            Focus = focus;
            FocusId = focus != null ? focus.Sid() : -1;
            Goal = goal;
            Hue = Hue.For(a, School.None, level);
            K = Choreo.KOf(a);
            PxA = Choreo.PxOf(a);
            Home = a.current_position;
            Pose = Pose.Of(a);
            Pose.PivotUp = 3.5f;
        }

        public override bool SuperArmor => true;

        protected bool FocusOk => Focus != null && Focus.Sid() == FocusId && Combat.Living(Focus);

        protected void Next()
        {
            Phase++;
            Pt = 0f;
        }

        public override void Tick(float dt)
        {
            Pt += dt;
            if (!Combat.Living(A))
            {
                Finished = true;
                return;
            }
            Step(dt);
        }

        protected abstract void Step(float dt);

        /// <summary>Poses a partner: where it stands, facing what, how high, leaning, squashed.</summary>
        protected static void PoseMate(Mate m, Vector2 ground, Vector2 face, float rot = 0f, float sy = 1f, float h = 0f)
        {
            Pose q = m.P.Pose;
            q.Ground = ground;
            q.Height = h;
            q.Rot = rot;
            q.SX = 2f - sy;
            q.SY = sy;
            q.FaceRight = Crew.Face(ground, face, q.FaceRight);
            q.Nudge = Vector2.zero;
            m.P.Pose = q;
        }

        protected void PoseSelf(Vector2 ground, Vector2 face, float rot = 0f, float sy = 1f, float h = 0f)
        {
            Pose.Ground = ground;
            Pose.Height = h;
            Pose.Rot = rot;
            Pose.SX = 2f - sy;
            Pose.SY = sy;
            Pose.FaceRight = Crew.Face(ground, face, Pose.FaceRight);
            Pose.Nudge = Vector2.zero;
        }

        /// <summary>A run from 'a' to 'b' over 'k' 0..1: eased, a little bob, dust at the feet now and then.</summary>
        protected static Vector2 Run(Vector2 a, Vector2 b, float k, out float bob)
        {
            float e = k * k * (3f - 2f * k);
            bob = Mathf.Abs(Mathf.Sin(k * Mathf.PI * 4f)) * 0.06f;
            return Vector2.Lerp(a, b, e);
        }

        protected int Alive
        {
            get
            {
                int n = 0;
                foreach (Mate m in Mates)
                {
                    if (m.Ok)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        protected void ReleaseAll()
        {
            foreach (Mate m in Mates)
            {
                if (m.P != null && !m.P.Finished)
                {
                    m.P.Release(m.P.Pose.Height <= 0.02f);
                }
                Director.Release(m.U);
            }
        }

        public override void Abort()
        {
            ReleaseAll();
        }

        public override void OnEnd(bool aborted)
        {
            ReleaseAll();
            Director.Release(this);
            foreach (Mate m in Mates)
            {
                Crew.Rest(m.U);
            }
        }

        /// <summary>Starts it with its partners (each one a TeamPartner the move poses), claims the cast, takes the shares.</summary>
        public static bool Launch(GatherMove m, Technique tech, List<Actor> partners, float share, bool forced, float claim)
        {
            Actor a = m.A;
            if (!Moves.CanPerform(a) || partners == null || partners.Count == 0)
            {
                return false;
            }
            m.Tech = tech;
            List<Actor> cast = new List<Actor>(partners) { a };
            if (!Director.Claim(m, claim, cast.ToArray()))
            {
                return false;
            }
            foreach (Actor u in partners)
            {
                TeamPartner pm = new TeamPartner(u, m, Crew.Face(u.current_position, a.current_position, u.flip));
                if (!Moves.Start(pm))
                {
                    Director.Release(u);
                    continue;
                }
                m.Mates.Add(new Mate
                {
                    U = u,
                    P = pm,
                    From = u.current_position,
                    Spot = u.current_position,
                    Col = Tiers.Of(u) == Tier.None ? new Color(1f, 0.9f, 0.7f, 1f) : Tiers.Color(Tiers.Of(u)),
                    K = Crew.KOf(u),
                    Px = Crew.PxOf(u),
                    Ph = Random.Range(0f, 6.28f)
                });
            }
            if (m.Mates.Count == 0)
            {
                Director.Release(m);
                return false;
            }
            m.Setup();
            if (!Moves.Start(m))
            {
                m.ReleaseAll();
                Director.Release(m);
                return false;
            }
            if (!forced)
            {
                foreach (Mate mb in m.Mates)
                {
                    Crew.Pay(mb.U, tech, share, 15f);
                }
            }
            Dev.Watch(m.Goal + new Vector2(0f, 1f));
            if (Dev.On)
            {
                Dev.Trace("gathering " + tech.Id + " " + Techniques.Roman(m.Level) + " with " + m.Mates.Count + " partners");
            }
            return true;
        }

        /// <summary>Places the partners (their spots) once they are all taken.</summary>
        protected abstract void Setup();

        // ------------------------------------------------------------ choosing --

        /// <summary>The free allies within 'range' who can take part (and pay), strongest first, at most 'max'.</summary>
        public static List<Actor> Recruit(Ctx c, float range, Tier min, float share, int max)
        {
            List<Actor> list = null;
            for (int i = 0; i < c.Allies.Count; i++)
            {
                Actor u = c.Allies[i];
                if ((u.current_position - c.Pos).sqrMagnitude > range * range)
                {
                    continue;
                }
                Tier t = Tiers.Of(u);
                if (t == Tier.None && !c.Forced || t != Tier.None && t < min && !c.Forced || !Crew.Free(c.Self, u) || !c.Forced && !Crew.CanPay(u, share))
                {
                    continue;
                }
                (list ??= new List<Actor>(12)).Add(u);
            }
            if (list != null && list.Count > max)
            {
                list.Sort((x, y) => Crew.Rank(y).CompareTo(Crew.Rank(x)));
                list.RemoveRange(max, list.Count - max);
            }
            return list;
        }

        /// <summary>The most terrible enemy in reach: a God, a Grandmaster, a titan, a kaiju, or one far above the leader.</summary>
        public static Actor Terror(Ctx c, float range)
        {
            Actor best = null;
            float bp = 0f;
            float self = Mathf.Max(1f, Tiers.Power(c.Self));
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (Duels.InDuel(v))
                {
                    continue;   // (someone's duel: team techniques leave duelists to each other, Crew.Target)
                }
                float p = Tiers.Power(v);
                bool dread = Tiers.Of(v) >= Tier.Grandmaster || Weights.Species(v) >= Weight.Titan || p >= self * 2.5f;
                if (dread && p > bp)
                {
                    bp = p;
                    best = v;
                }
            }
            return best;
        }
    }

    // ============================================================ HEAVEN SEAL ==

    internal sealed class HeavenSealMove : GatherMove
    {
        private const float TRun = 0.6f, TPlant = 0.35f, TBind = 0.85f, TSmite = 1.3f, TRise = 0.3f;
        private float _r;
        private Vector2 _mine;

        public HeavenSealMove(Actor a, int level, Actor focus) : base(a, level, focus, focus.current_position)
        {
        }

        public override bool Charging => Phase <= 1;

        protected override void Setup()
        {
            int n = Mates.Count + 1;
            _r = (2.4f + 0.12f * n) * Mathf.Clamp(Choreo.KOf(Focus), 1f, 2.2f);
            float a0 = Mathf.Atan2(A.current_position.y - Goal.y, A.current_position.x - Goal.x);
            _mine = Goal + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _r;
            for (int i = 0; i < Mates.Count; i++)
            {
                float a = a0 + (i + 1) * Mathf.PI * 2f / n;
                Mates[i].Spot = Goal + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _r;
            }
        }

        private List<Vector2> Spots()
        {
            List<Vector2> s = new List<Vector2> { Pose.Ground };
            foreach (Mate m in Mates)
            {
                if (m.Ok)
                {
                    s.Add(m.P.Pose.Ground);
                }
            }
            return s;
        }

        protected override void Step(float dt)
        {
            if (FocusOk)
            {
                Goal = Focus.current_position;
            }
            else if (Phase <= 2)
            {
                // its quarry fell before the seal closed: the light gutters out, they stand down
                Crew.Fizzle(Goal + new Vector2(0f, 0.5f * K), PxA, false);
                Finished = true;
                return;
            }
            bool vis = Kit.Visible(Goal, 0.8f);
            switch (Phase)
            {
                case 0:
                    {
                        float k = Mathf.Clamp01(Pt / TRun);
                        PoseSelf(Run(Home, _mine, k, out float b), Goal, 8f, 1f, b);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, Run(m.From, m.Spot, k, out float mb), Goal, 8f, 1f, mb);
                            }
                        }
                        if (FocusOk)
                        {
                            Crew.Pin(Focus, 0.3f);
                        }
                        if (Pt >= TRun)
                        {
                            Next();
                            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile);
                        }
                        break;
                    }
                case 1:
                    {
                        // planted: the seal is drawn on the ground between them, a star through the ring
                        float k = Mathf.Clamp01(Pt / TPlant);
                        PoseSelf(_mine, Goal, -4f, Mathf.Lerp(1f, 0.82f, k));
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot, Goal, -4f, Mathf.Lerp(1f, 0.82f, k));
                            }
                        }
                        if (Pt - dt <= 0f && vis)
                        {
                            List<Vector2> s = Spots();
                            Fx.Ring(Goal, PxA, 3f, _r / PxA, _r / PxA, 2, 2, FxLayer.Ground, 4, Art.Ground, 1f, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                            int step = s.Count >= 5 ? 2 : 1;
                            for (int i = 0; i < s.Count; i++)
                            {
                                Kit.Line(s[i], s[(i + step) % s.Count], PxA, 3f, 1, FxLayer.Ground, 5, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
                                Fx.Ring(s[i], PxA, 0.5f, 2f, 9f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                            }
                        }
                        if (FocusOk)
                        {
                            Crew.Pin(Focus, 0.3f);
                        }
                        if (Pt >= TPlant)
                        {
                            Next();
                        }
                        break;
                    }
                case 2:
                    {
                        // bound: pillars of light rise from them, chains of light run to it, it trembles
                        PoseSelf(_mine + Choreo.Tremble(Pt, 0.01f), Goal, -4f, 0.82f);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot + Choreo.Tremble(Pt + m.Ph, 0.01f), Goal, -4f, 0.82f);
                            }
                        }
                        if (FocusOk)
                        {
                            Crew.Pin(Focus, 0.4f);
                            if (vis && ((int)(Pt * 30f) & 1) == 0)
                            {
                                Vector2 core = Choreo.BodyAt(Focus, 0.5f);
                                foreach (Vector2 s in Spots())
                                {
                                    Crew.Link(s + new Vector2(0f, 0.5f * K), core, PxA, Hue.Edge, 0.06f);
                                    Kit.Line(s, s + new Vector2(0f, 4f * K), PxA, 0.06f, 2, FxLayer.Top, 8, Fx.WithA(Color.white, 0.7f), Fx.WithA(Hue.Core, 0f));
                                }
                                Victims.Flash(Focus, Hue.Core, 0.04f);
                            }
                        }
                        if (Pt >= TBind)
                        {
                            Next();
                            int n = Alive + 1;
                            float mult = (2f + 0.7f * n) * (Level <= 1 ? 1f : Level == 2 ? 1.35f : 1.8f);
                            Areas.Add(new SkyBeamHazard(A, this, Hue, Scale.Ultimate, Goal, 1.6f + 0.1f * n, mult, 0.9f));
                        }
                        break;
                    }
                case 3:
                    {
                        PoseSelf(_mine, Goal, -12f, 0.9f);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot, Goal, -12f, 0.9f);
                            }
                        }
                        if (Pt >= TSmite)
                        {
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        float k = Mathf.Clamp01(Pt / TRise);
                        PoseSelf(_mine, Goal, 0f, Mathf.Lerp(0.9f, 1f, k));
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot, Goal, 0f, Mathf.Lerp(0.9f, 1f, k));
                            }
                        }
                        if (Pt >= TRise)
                        {
                            Finished = true;
                        }
                        break;
                    }
            }
        }
    }

    internal sealed class HeavenSeal : Technique
    {
        public override string Id => "heaven_seal";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Team | Form.Area | Form.Ki | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 90f, 130f, 180f };
        public override float[] Cooldown => new[] { 60f, 65f, 70f };
        private static readonly float[] Shares = { 30f, 40f, 55f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            Actor v = c.Forced && c.ForceTarget != null ? c.ForceTarget : GatherMove.Terror(c, 9f);
            if (v == null)
            {
                return null;
            }
            List<Actor> mates = GatherMove.Recruit(c, 10f, Tier.Adept, Shares[level - 1], 9);
            if (mates == null || mates.Count < (c.Forced ? 1 : 5))
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Others = mates, Count = mates.Count + 1, Score = 900f + 80f * mates.Count };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (p.Target == null || p.Others == null)
            {
                return false;
            }
            return GatherMove.Launch(new HeavenSealMove(a, p.Level, p.Target), this, p.Others, Shares[Mathf.Clamp(p.Level, 1, 3) - 1], false, 6f);
        }
    }

    // ========================================================= HUNDRED VOLLEY ==

    internal sealed class VolleyMove : GatherMove
    {
        private const float TSignal = 0.45f, TLoose = 0.5f, TWait = 1.3f;
        private readonly List<Mate> _shot = new List<Mate>();
        private float _r;

        public VolleyMove(Actor a, int level, Vector2 goal) : base(a, level, null, goal)
        {
        }

        protected override void Setup()
        {
            _r = 1.6f + 0.1f * Mates.Count;
        }

        private static ShotKind KindOf(Actor u)
        {
            List<School> s = Schools.Of(u);
            School first = s.Count > 0 ? s[0] : School.None;
            switch (first)
            {
                case School.KiArts: return ShotKind.Orb;
                case School.HuntersWay: return ShotKind.Spear;
                case School.EarthBreaker: return ShotKind.Stone;
                case School.ThousandBlades: return ShotKind.LightSword;
                case School.TempestLeg: return ShotKind.WindBlade;
                case School.PhantomStep: return ShotKind.Disc;
                default: return ShotKind.Bullet;
            }
        }

        protected override void Step(float dt)
        {
            switch (Phase)
            {
                case 0:
                    {
                        // the raised hand: a flash over it; everyone turns to the enemy and draws
                        float k = Mathf.Clamp01(Pt / TSignal);
                        PoseSelf(Home, Goal, -10f * k, 1f + 0.12f * k);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.From, Goal, 6f * k, 1f - 0.12f * k);
                            }
                        }
                        if (Pt - dt <= 0f && Kit.Visible(Home))
                        {
                            Vector2 top = Choreo.BodyAt(A, 1.2f);
                            Kit.Flash(top, PxA, 5, 0.25f, Hue.Core);
                            Fx.Ring(top, PxA, 0.3f, 1f, 10f, 1, 1, FxLayer.Top, 12, 1f, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                            Fx.Ring(Goal, PxA, 0.6f, _r / PxA * 1.2f, _r / PxA, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Hue.Edge, 0f));
                        }
                        if (Pt >= TSignal)
                        {
                            Next();
                            Choreo.Sound("event:/SFX/WEAPONS/WeaponStartThrow", A.current_tile);
                        }
                        break;
                    }
                case 1:
                    {
                        // they loose, one after another, timed so the shots land together
                        float k = Mathf.Clamp01(Pt / TLoose);
                        foreach (Mate m in Mates)
                        {
                            if (!m.Ok)
                            {
                                continue;
                            }
                            PoseMate(m, m.From, Goal, -8f, 1.05f);
                            if (!_shot.Contains(m) && k >= m.Ph / 6.28f * 0.8f)
                            {
                                _shot.Add(m);
                                Loose(m.U, m.Px, m.K, KindOf(m.U), 2 + Level / 2);
                            }
                        }
                        PoseSelf(Home, Goal, -8f, 1.05f);
                        if (Pt - dt <= 0f)
                        {
                            Loose(A, PxA, K, KindOf(A), 2 + Level / 2);
                        }
                        if (Pt >= TLoose)
                        {
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        PoseSelf(Home, Goal);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.From, Goal);
                            }
                        }
                        if (Pt >= TWait - 0.8f)
                        {
                            ReleaseAll();   // (free to fight on while the shots are in the air)
                        }
                        if (Pt >= TWait)
                        {
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        private void Loose(Actor u, float px, float k, ShotKind kind, int n)
        {
            Hue hue = Hue.For(u, School.None, Level);
            Vector2 from = Choreo.BodyAt(u, 0.65f);
            for (int i = 0; i < n; i++)
            {
                Vector2 goal = Goal + Random.insideUnitCircle * _r;
                float d = Vector2.Distance(u.current_position, goal);
                ShotSpec s = new ShotSpec
                {
                    Kind = kind, Flight = Flight.Lob, Speed = Mathf.Max(8f, d / 0.9f), Apex = 2.2f + d * 0.12f, Range = d + 2f, Size = kind == ShotKind.Stone ? 1.3f : 1f,
                    Mult = 0.9f + 0.15f * Level, Hurt = Hurt.Knockdown, Push = 0.6f, Crater = 0.5f, Area = 0.7f, AreaMult = 0.4f, Delay = i * 0.07f
                };
                Shots.Fire(u, this, from, goal - u.current_position, null, goal, s, hue);
            }
        }
    }

    internal sealed class HundredVolley : Technique
    {
        public override string Id => "hundred_volley";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Team | Form.Projectile | Form.Area;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 25f, 35f, 50f };
        public override float[] Cooldown => new[] { 30f, 32f, 34f };
        private static readonly float[] Shares = { 8f, 12f, 16f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            Vector2 at = c.Cluster(2.4f, 3f, 16f, out int count, out Actor center);
            if (center == null || count < (c.Forced ? 1 : 4))
            {
                return null;
            }
            List<Actor> mates = GatherMove.Recruit(c, 14f, Tier.Novice, Shares[level - 1], 9);
            if (mates == null || mates.Count < (c.Forced ? 1 : 4))
            {
                return null;
            }
            return new Plan { Target = center, Point = at, Others = mates, Count = count, Score = 420f + 50f * count + 30f * mates.Count };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Others != null && GatherMove.Launch(new VolleyMove(a, p.Level, p.Point), this, p.Others, Shares[Mathf.Clamp(p.Level, 1, 3) - 1], false, 3f);
        }
    }

    // ===================================================== CHOIR OF FALLING STARS ==

    internal sealed class StarChoirMove : GatherMove
    {
        private const float TRaise = 0.45f, TForm = 1.1f, TFall = 1.6f;
        private Vector2 _sky;
        private Particle _star;

        public StarChoirMove(Actor a, int level, Vector2 goal, Actor focus) : base(a, level, focus, goal)
        {
        }

        public override bool Charging => Phase <= 1;

        protected override void Setup()
        {
            _sky = Goal + new Vector2(0f, 9f * K);
        }

        protected override void Step(float dt)
        {
            bool vis = Kit.Visible(Goal, 1f) || Kit.Visible(Home, 0.5f);
            switch (Phase)
            {
                case 0:
                case 1:
                    {
                        float k = Mathf.Clamp01(Pt / TRaise);
                        PoseSelf(Home, Goal, -6f * k, 1f + 0.12f * k, 0.05f * k);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.From, Goal, -6f * k, 1f + 0.12f * k, 0.04f * k);
                            }
                        }
                        if (Phase == 1)
                        {
                            float f = Mathf.Clamp01(Pt / TForm);
                            if (vis && ((int)(Pt * 30f) & 1) == 0)
                            {
                                // beams of every colour into the sky, meeting where the star forms
                                Kit.Line(Choreo.BodyAt(A, 1f), _sky, PxA, 0.06f, 1, FxLayer.Top, 8, Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Core, 0.3f));
                                foreach (Mate m in Mates)
                                {
                                    if (m.Ok)
                                    {
                                        Kit.Line(Choreo.BodyAt(m.U, 1f), _sky, m.Px, 0.06f, 1, FxLayer.Top, 8, Fx.WithA(Color.white, 0.7f), Fx.WithA(m.Col, 0.3f));
                                    }
                                }
                            }
                            if (vis)
                            {
                                if (_star == null || !_star.Alive)
                                {
                                    _star = Spec.Blob(_sky, 0.6f * K, PxA, 9f, FxLayer.Top, 22, 1f, 0.6f, Fx.WithA(Color.white, 0.95f));
                                    if (_star != null)
                                    {
                                        _star.Held = true;
                                    }
                                }
                                if (_star != null)
                                {
                                    _star.Pos = _sky;
                                    _star.SX = _star.SY = 0.4f + 1.6f * f * (1f + 0.06f * Mathf.Sin(Pt * 40f));
                                    _star.R.color = Color.Lerp(Color.white, Spec.Molten, f * 0.6f);
                                }
                                if (Random.value < 0.4f)
                                {
                                    ChoreoFx.Motes(_sky, PxA, 2, 18f * K, 0.3f, Color.white, Hue.Edge, FxLayer.Top);
                                }
                            }
                            if (Pt >= TForm)
                            {
                                Next();
                                KillStar();
                                int n = Alive + 1;
                                float mult = (2.2f + 0.6f * n) * (Level <= 1 ? 1f : Level == 2 ? 1.35f : 1.8f);
                                Areas.Add(new MeteorHazard(A, this, Hue, Scale.Ultimate, FocusOk ? Focus.current_position : Goal, 2.6f + 0.2f * n, mult, 1.5f + 0.15f * n, SkyFall.Sun));
                            }
                        }
                        else if (Pt >= TRaise)
                        {
                            Next();
                            Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile);
                        }
                        break;
                    }
                default:
                    {
                        PoseSelf(Home, Goal, -10f, 1.08f);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.From, Goal, -10f, 1.08f);
                            }
                        }
                        if (Pt >= TFall)
                        {
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        private void KillStar()
        {
            if (_star != null)
            {
                _star.Held = false;
                _star.Life = _star.Age;
                _star = null;
            }
        }

        public override void Abort()
        {
            KillStar();
            base.Abort();
        }
    }

    internal sealed class StarChoir : Technique
    {
        public override string Id => "star_choir";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Team | Form.Area | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 80f, 120f, 170f };
        public override float[] Cooldown => new[] { 55f, 60f, 65f };
        private static readonly float[] Shares = { 25f, 35f, 45f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            Actor dread = GatherMove.Terror(c, 12f);
            Vector2 at = c.Cluster(3f, 4f, 14f, out int count, out Actor center);
            if (dread == null && (center == null || count < (c.Forced ? 1 : 6)))
            {
                return null;
            }
            List<Actor> mates = GatherMove.Recruit(c, 10f, Tier.Adept, Shares[level - 1], 9);
            if (mates == null || mates.Count < (c.Forced ? 1 : 4))
            {
                return null;
            }
            Vector2 goal = dread != null && (center == null || count < 6) ? dread.current_position : at;
            return new Plan { Target = dread ?? center, Point = goal, Others = mates, Count = count, Score = 800f + 60f * count + 50f * mates.Count };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Others != null && GatherMove.Launch(new StarChoirMove(a, p.Level, p.Point, p.Target), this, p.Others, Shares[Mathf.Clamp(p.Level, 1, 3) - 1], false, 5f);
        }
    }

    // ============================================================= IRON WEDGE ==

    internal sealed class WedgeMove : GatherMove
    {
        private const float TForm = 0.5f, TStomp = 0.3f, TCharge = 0.55f, THalt = 0.35f;
        private readonly Vector2 _dir;
        private readonly float _len;
        private Vector2 _start;
        private readonly HashSet<long> _hit = new HashSet<long>();
        private static readonly List<Actor> _near = new List<Actor>();
        private float _lineT;

        public WedgeMove(Actor a, int level, Vector2 dir, float len) : base(a, level, null, a.current_position + dir * len)
        {
            _dir = dir.normalized;
            _len = len;
        }

        protected override void Setup()
        {
            _start = Home;
            Vector2 side = Choreo.Perp(_dir);
            for (int i = 0; i < Mates.Count; i++)
            {
                int row = i / 2 + 1;
                float s = (i % 2 == 0 ? 1f : -1f) * row * 0.8f;
                Mates[i].Spot = _start - _dir * row * 0.75f + side * s;
            }
        }

        protected override void Step(float dt)
        {
            bool vis = Kit.Visible(Home, 0.6f);
            Vector2 face = Home + _dir * 10f;
            switch (Phase)
            {
                case 0:
                    {
                        float k = Mathf.Clamp01(Pt / TForm);
                        PoseSelf(_start, face, 4f);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, Run(m.From, m.Spot, k, out float b), face, 8f, 1f, b);
                            }
                        }
                        if (Pt >= TForm)
                        {
                            Next();
                        }
                        break;
                    }
                case 1:
                    {
                        // they stamp as one: a crouch, a stamp, dust off every foot, a shudder through the ground
                        float k = Mathf.Clamp01(Pt / TStomp);
                        float sy = k < 0.6f ? Mathf.Lerp(1f, 0.8f, k / 0.6f) : Mathf.Lerp(0.8f, 1.05f, (k - 0.6f) / 0.4f);
                        PoseSelf(_start, face, 10f, sy);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot, face, 10f, sy);
                            }
                        }
                        if (Pt - dt < TStomp * 0.6f && Pt >= TStomp * 0.6f)
                        {
                            if (vis)
                            {
                                Kit.DustRing(_start, PxA, A.current_tile, 6, 30f, 0.45f, K);
                                foreach (Mate m in Mates)
                                {
                                    if (m.Ok)
                                    {
                                        Kit.DustRing(m.Spot, m.Px, Combat.TileAt(m.Spot), 4, 24f, 0.4f, m.K);
                                    }
                                }
                                Fx.Ring(_start, PxA, 0.35f, 2f, 30f * K, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Edge, 0f));
                            }
                            Choreo.Sound("event:/SFX/HIT/HitStone", A.current_tile);
                        }
                        if (Pt >= TStomp)
                        {
                            Next();
                        }
                        break;
                    }
                case 2:
                    {
                        // the charge: all at once, straight through the enemy line
                        float k = Mathf.Clamp01(Pt / TCharge);
                        float e = k * k * (3f - 2f * k);
                        Vector2 off = _dir * _len * e;
                        PoseSelf(_start + off, face, 18f, 0.95f);
                        Hit(A, _start + off);
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot + off, face, 18f, 0.95f);
                                Hit(m.U, m.Spot + off);
                            }
                        }
                        _lineT -= dt;
                        if (vis && _lineT <= 0f)
                        {
                            _lineT = 0.04f;
                            Kit.SpeedLines(Choreo.BodyAt(A, 0.5f), _dir, PxA, 3, 6f, 0.1f, Fx.WithA(Color.white, 0.8f));
                            Kit.DustRing(_start + off, PxA, Combat.TileAt(_start + off), 2, 14f, 0.3f, K);
                        }
                        if (Pt >= TCharge)
                        {
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        float k = Mathf.Clamp01(Pt / THalt);
                        Vector2 off = _dir * (_len + 0.4f * k);
                        PoseSelf(_start + off, face, Mathf.Lerp(-12f, 0f, k));
                        foreach (Mate m in Mates)
                        {
                            if (m.Ok)
                            {
                                PoseMate(m, m.Spot + off, face, Mathf.Lerp(-12f, 0f, k));
                            }
                        }
                        if (Pt >= THalt)
                        {
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        /// <summary>A charger meets whoever stands in its way: thrown along the charge (once each).</summary>
        private void Hit(Actor u, Vector2 at)
        {
            Choreo.Foes(A, at, 0.75f * K, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!_hit.Add(v.Sid()) || !Choreo.FairBlow(u, v, this, out float cm, out bool full, true))
                {
                    continue;
                }
                Combat.Hit(u, v, (1.1f + 0.25f * Level) * cm, true, true, 0f, this);
                if (Combat.Living(v))
                {
                    Impact im = Impact.Of(full ? Hurt.Launch : Hurt.Stagger, _dir + Random.insideUnitCircle * 0.4f, 1.4f * K, 1.3f);
                    im.Lift = 1.2f * K;
                    Victims.Hurt(v, u, this, im);
                }
                if (Kit.Visible(at))
                {
                    ChoreoFx.Impact(Choreo.BodyAt(v, 0.5f), _dir, PxA, K, 1.1f, Hue);
                }
            }
        }
    }

    internal sealed class IronWedge : Technique
    {
        public override string Id => "iron_wedge";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Team | Form.Dash | Form.Line;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 20f, 30f, 40f };
        public override float[] Cooldown => new[] { 28f, 30f, 32f };
        private static readonly float[] Shares = { 6f, 9f, 12f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            float len = 6f + level;
            Vector2 dir = c.BestRay(len, 1.4f, out int count);
            if (count < (c.Forced ? 1 : 4))
            {
                return null;
            }
            List<Actor> mates = GatherMove.Recruit(c, 7f, Tier.Novice, Shares[level - 1], 9);
            if (mates == null || mates.Count < (c.Forced ? 1 : 4))
            {
                return null;
            }
            return new Plan { Dir = dir, Point = c.Pos + dir * len, Others = mates, Count = count, Score = 380f + 50f * count + 25f * mates.Count };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (p.Others == null || p.Dir.sqrMagnitude < 1e-4f)
            {
                return false;
            }
            return GatherMove.Launch(new WedgeMove(a, p.Level, p.Dir, 6f + p.Level), this, p.Others, Shares[Mathf.Clamp(p.Level, 1, 3) - 1], false, 3f);
        }
    }

    /// <summary>The gatherings' names and lines.</summary>
    internal static class GatheringTexts
    {
        [SakugaInit]
        internal static void Texts()
        {
            Put("heaven_seal", "Seal of the Ten Heavens", "Up to ten fighters surround one terrible foe, draw a seal on the ground between them, bind it in chains of light, and call a column of heaven down on it");
            Put("hundred_volley", "Hundred Volley", "A raised hand, and five to ten allies, however far apart, loose at the same enemy crowd: the shots arc in from everywhere and land together");
            Put("star_choir", "Choir of Falling Stars", "Six to ten fighters raise their hands and pour their light into the sky above the enemy; where it meets, a star forms and falls");
            Put("iron_wedge", "Iron Wedge", "Five to ten fighters form a wedge behind their leader, stamp as one, and charge straight through the enemy line, bowling it over");
        }

        private static void Put(string id, string name, string info)
        {
            // Fallback only: Locales/team/cz.json (etc.) must win over English recipe lines.
            Traits.PutFallback("trait_sakuga_" + id, name);
            Traits.PutFallback("trait_sakuga_" + id + "_info", info);
        }
    }
}
