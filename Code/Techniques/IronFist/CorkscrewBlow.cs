using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CORKSCREW (Iron Fist, Expert). A twisting straight. It coils, shoulder turned far away and the
    /// fist chambered low with the knuckles up (wind spiralling in round it at III), then the straight
    /// drills out, the arm turning over as it lands: rings of twisted air peel off the fist one after
    /// another down the line, a helix of light winds around it. The foe corkscrews away along the line,
    /// low and spinning like a drill bit, and anyone standing in its path is knocked aside, spun round
    /// (III: off their feet); it comes down on its back 3 / 4 / 5 tiles away. II and III tear a furrow
    /// along its flight (a Master's and a Grandmaster's blows break the ground), III a crater where it
    /// lands. Chains from Close. Damage (x the user's damage): I 2, II 2.7, III 3.6; those knocked
    /// aside x0.5 of it.
    /// </summary>
    internal sealed class CorkscrewBlowTech : Technique
    {
        public override string Id => "corkscrew_blow";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Line;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Drill | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 15f, 28f, 46f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.7f : 3.6f;
        }

        /// <summary>How far the foe drills away (tiles).</summary>
        public static float Fly(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 4f : 5f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Mult(1), Crowd = 0f, Alone = 6f, IntoGuard = 0.75f, Worth = 14f },
            new MeleeWant { Reach = 2.4f, Total = Mult(2), Crowd = 0f, Alone = 7f, IntoGuard = 0.75f, Worth = 18f },
            new MeleeWant { Reach = 2.6f, Total = Mult(3), Crowd = 0f, Alone = 8f, IntoGuard = 0.75f, Worth = 24f }
        };

        private static readonly List<Actor> _ray = new List<Actor>();

        /// <summary>
        /// A foe in reach, worth more for every other foe standing behind it on the line it will drill
        /// along (each knocked aside: half the blow).
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            Plan p = IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            float d = c.Dist(p.Target);
            _ray.Clear();
            int n = c.OnRay(p.Dir, d + Fly(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f) + 0.5f, 0.6f, _ray);
            for (int i = 0; i < n; i++)
            {
                Actor u = _ray[i];
                if (u == p.Target || !c.Allowed(u, out float cu))
                {
                    continue;
                }
                if (Vector2.Dot(u.current_position - p.Target.current_position, p.Dir) <= 0.2f)
                {
                    continue;   // (only those behind it are in the drill's path)
                }
                p.Score += c.Value(u, c.Expected(u, Mult(level) * 0.5f), cu);
                p.Count++;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            float moteT = 0f;
            Drill drill = new Drill();
            const Form shows = Form.Punch | Form.Drill | Form.Straight;
            m.StepIn().Shows(shows);
            // THE COIL: the shoulder turned far away, the fist chambered low, knuckles up
            m.PoseTo(0.13f, new Shape(-18f, 0.84f, 1.08f, 0f, -0.09f), Ease.OutCubic, Tempo.Wind)
                .Shows(shows)
                .Every((mm, k) =>
                {
                    if (!mm.Vis || mm.Level < 3)
                    {
                        return;
                    }
                    moteT -= Clock.Dt;
                    if (moteT <= 0f)
                    {
                        moteT = 0.04f;
                        Vector2 fist = mm.BodyPos() + new Vector2(-mm.Fwd * mm.Hpx * 0.1f, -mm.Hpx * 0.02f) * mm.Px;
                        ChoreoFx.Motes(fist, mm.Px, 3, 8f * mm.Kk, 0.16f, mm.Hue.As(Element.Wind).Core, mm.Hue.Edge);
                    }
                });
            // THE DRILL: the straight, the arm turning over as it lands
            Hit h = new Hit(Blow.Cross, Mult(level))
            {
                WindScale = 0.55f,
                ActScale = 1.15f,
                Hurt = Hurt.Launch,
                Push = Fly(level),
                Lift = 0.45f,
                Daze = 0.6f + 0.2f * level,
                Power = 1.45f + 0.15f * level,
                Finisher = true,
                Shows = shows
            };
            h.OnLand = (mm, v, r) => Land(mm, v, h, drill);
            m.Strike(h);
            // THE PATH: it holds the extended arm while the foe drills away, those in the way knocked aside
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.32f + 0.05f * level,
                To = new Shape(14f, 1.14f, 0.92f, 0f, 0.12f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = shows,
                Every = (mm, k) => Path(mm, drill, k),
                Exit = mm => Furrow(mm, drill, true)
            });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The drill's state: where the foe set off, who was knocked aside, how far the furrow got.</summary>
        private sealed class Drill
        {
            public Actor V;
            public long Id = -1;
            public Vector2 From, Furrowed, Dir;
            public bool Flying;
            public float RingT, FurrowT;
            public readonly HashSet<long> Met = new HashSet<long>();
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>The straight lands: rings of twisted air peel off the fist, the foe set drilling away.</summary>
        private static void Land(ChoreoMove m, Actor v, Hit h, Drill d)
        {
            int level = m.Level;
            Vector2 at = m.ContactPoint(0.62f);
            if (m.Vis)
            {
                float px = m.Px;
                Color[] ring = { Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f) };
                int rings = 2 + level;
                for (int i = 0; i < rings; i++)
                {
                    // edge-on rings (tall, thin) racing down the line, each a little later and wider
                    Particle p = Fx.Spawn(Art.Ring(Mathf.RoundToInt((3f + i) * m.Kk), 1, 2.4f), at, px, 0.2f + 0.04f * i, m.TopLayer, 14);
                    if (p == null)
                    {
                        break;
                    }
                    p.Delay = 0.035f * i;
                    p.Moving(m.Dir * (60f + 25f * i) * px, 0f, 4f).Sized(0.7f, 1.5f).Color(ring);
                    p.SX = m.Fwd;
                }
                Helix(m, at, level);
                Kit.SpeedLines(at, m.Dir, px, 4 + level, 3f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
            }
            Impact im = Impact.Of(Hurt.Launch, m.Dir, Fly(level) * Mathf.Lerp(1f, m.Kk, 0.5f), h.Power);
            im.Lift = 0.45f * Mathf.Sqrt(m.Kk);
            im.Spin = (m.Fwd > 0f ? -1f : 1f) * (720f + 360f * level);
            im.Daze = h.Daze;
            im.Stop = (0.05f + 0.01f * level) * m.ImpactK;
            IronFist.Rehurt(m, v, h, im, null, Form.None);
            d.V = v;
            d.Id = v.Sid();
            d.From = Victims.GroundOf(v);
            d.Furrowed = d.From;
            d.Dir = m.Dir;
            d.Flying = Victims.Get(v) != null;
            d.Met.Add(d.Id);
        }

        /// <summary>A helix of light winding round the line of the blow (two strands, a few dozen pixels).</summary>
        private static void Helix(ChoreoMove m, Vector2 at, int level)
        {
            float px = m.Px;
            Vector2 dir = m.Dir;
            Vector2 up = new Vector2(0f, 1f);
            int n = Fx.Count(10 + 4 * level);
            float len = (16f + 6f * level) * px * m.Kk;
            float rad = (2.2f + 0.4f * level) * px * m.Kk;
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                for (int s = 0; s < 2; s++)
                {
                    float ph = u * Mathf.PI * 5f + s * Mathf.PI;
                    Vector2 q = at + dir * (u * len) + up * (Mathf.Sin(ph) * rad);
                    bool front = Mathf.Cos(ph) > 0f;
                    Particle p = Fx.Spawn(Art.Pixel, q, px, 0.14f + 0.12f * u, front ? m.TopLayer : FxLayer.Behind, front ? 13 : 2);
                    if (p == null)
                    {
                        return;
                    }
                    p.Delay = 0.05f * u;
                    p.Moving(dir * 20f * px, 0f, 3f).Sized(1.2f, 0.6f)
                        .Color(Fx.WithA(front ? Color.white : m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                }
            }
        }

        /// <summary>
        /// While the foe drills away: little twisted rings at its back, the furrow torn after it (II, III),
        /// and everyone of the performer's enemies it passes knocked aside, spun round (III: off their feet).
        /// </summary>
        private static void Path(ChoreoMove m, Drill d, float k)
        {
            Actor v = d.V;
            if (v == null || v.Sid() != d.Id || !v.isAlive() || !d.Flying)
            {
                return;
            }
            Vector2 g = Victims.GroundOf(v);
            float px = m.Px;
            if (Dev.On)
            {
                Dev.Watch(Vector2.Lerp(m.Pose.Ground, g, 0.5f) + new Vector2(0f, 1.2f * m.Kk));   // (dev captures follow the drill)
            }
            d.RingT -= Clock.Dt;
            if (d.RingT <= 0f && m.Vis && k < 0.85f)
            {
                d.RingT = 0.045f;
                Particle p = Fx.Spawn(Art.Ring(Mathf.RoundToInt(3f * m.Kk), 1, 2.4f), Choreo.BodyAt(v, 0.5f), px, 0.18f, FxLayer.Behind, 4);
                p?.Sized(0.8f, 1.6f).Color(Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
            }
            d.FurrowT -= Clock.Dt;
            if (d.FurrowT <= 0f)
            {
                d.FurrowT = 0.1f;
                Furrow(m, d, false);
            }
            // those in its path are knocked aside, off the line
            Choreo.Foes(m.A, g, 0.5f * m.Kk + 0.25f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                long id = u.Sid();
                if (d.Met.Contains(id) || !Choreo.Fair(m.A, u, out float cm, m.Forced))
                {
                    continue;
                }
                d.Met.Add(id);
                if (!m.Touch(u, true))
                {
                    continue;
                }
                Vector2 rel = u.current_position - g;
                float cross = d.Dir.x * rel.y - d.Dir.y * rel.x;
                Vector2 aside = (Choreo.Perp(d.Dir) * (cross >= 0f ? 1f : -1f) + d.Dir * 0.35f).normalized;
                HitResult r = Combat.Hit(m.A, u, CorkscrewBlowTech.Mult(m.Level) * 0.5f * cm, true, false, 0f, m);
                if (Combat.Living(u))
                {
                    Impact im = Impact.Of(m.Level >= 3 ? Hurt.Knockdown : Hurt.Spin, aside, 1f + 0.2f * m.Level, 1.2f);
                    im.Daze = 0.4f;
                    Victims.Hurt(u, m.A, m, im);
                }
                Choreo.Sound("event:/SFX/HIT/HitGeneric", u.current_tile);
                if (m.Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), aside, px, m.Kk, 1f, m.Hue, m.TopLayer);
                }
                if (Dev.On) Dev.Trace("corkscrew: " + u.asset.id + "#" + id + " knocked aside" + (r.Killed ? " (killed)" : ""));
            }
        }

        /// <summary>II, III: the ground torn along the flight, a segment at a time; III a crater where it lands.</summary>
        private static void Furrow(ChoreoMove m, Drill d, bool end)
        {
            if (m.Level < 2 || d.V == null || !d.Flying)
            {
                return;
            }
            Vector2 g = d.V.Sid() == d.Id && d.V.isAlive() ? Victims.GroundOf(d.V) : d.Furrowed;
            if ((g - d.Furrowed).sqrMagnitude >= 0.36f)
            {
                Ruin.Furrow(m.A, d.Furrowed, g, m.Level >= 3 ? 0.6f : 0.4f, m.Scale, m.Pose.Ground);
                d.Furrowed = g;
            }
            if (end && m.Level >= 3 && (g - d.From).sqrMagnitude > 1f)
            {
                Ruin.Blast(m.A, g, 0.8f, m.Scale, m.Pose.Ground);
                if (m.Vis)
                {
                    Kit.Crater(g, m.Px * m.Kk, Combat.TileAt(g), Ruin.Force(m.Tier, m.Scale) >= 3, 5f, true, m.Hue.Edge);
                }
                d.Flying = false;
            }
        }
    }
}
