using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// CONTINENTAL SLAM (Earth Breaker, Grandmaster). It seizes the foe, swings it up over its head by
    /// the ankles and smashes it into the ground in front of it, then whips it back over its head and
    /// smashes it down behind it, and again, side to side like a flail: every slam a crater, a ring of
    /// dust and rock, and a shockwave that knocks down every enemy near where it lands. The last slam is
    /// the biggest (an impact frame, the world holding its breath); the body is left driven into the
    /// earth (Down). I slams 3 times, II 4, III 5 (and every slam of III flashes an impact frame). A
    /// grab: it beats a guard, and needs a body it can lift. Damage (x the user's damage) to the one it
    /// holds: I 0.15 + 0.6 + 0.7 + 1.3; II 0.15 + 0.6 + 0.7 + 0.75 + 1.5; III 0.15 + 0.6 + 0.7 + 0.75 + 0.8
    /// + 1.8; to everyone near a slam x0.4 / 0.45 / 0.5 of the user's damage.
    /// </summary>
    internal sealed class ContinentalSlamTech : Technique
    {
        public override string Id => "continental_slam";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Throw | Form.Area | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 40f, 64f, 95f };
        public override float[] Cooldown => new[] { 15f, 16f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static int Slams(int level)
        {
            return EarthKit.L(level, 3, 4, 5);
        }

        private static readonly float[] SlamMults = { 0.6f, 0.7f, 0.75f, 0.8f };

        private static float FinalMult(int level)
        {
            return EarthKit.L(level, 1.3f, 1.5f, 1.8f);
        }

        private static float AreaMult(int level)
        {
            return EarthKit.L(level, 0.4f, 0.45f, 0.5f);
        }

        private static float AreaR(int level)
        {
            return EarthKit.L(level, 1.6f, 1.9f, 2.2f);
        }

        public static float Total(int level)
        {
            float t = 0.15f + FinalMult(level);
            for (int i = 0; i < Slams(level) - 1; i++)
            {
                t += SlamMults[Mathf.Min(i, SlamMults.Length - 1)];
            }
            return t;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Total(1), Hold = true, Crowd = 0f, Alone = 6f, IntoGuard = 1.2f, Worth = 24f },
            new MeleeWant { Reach = 2.6f, Total = Total(2), Hold = true, Crowd = 0f, Alone = 6f, IntoGuard = 1.2f, Worth = 30f },
            new MeleeWant { Reach = 2.8f, Total = Total(3), Hold = true, Crowd = 0f, Alone = 6f, IntoGuard = 1.2f, Worth = 36f }
        };

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // the shockwaves: everyone standing round it is caught by some of the slams
            _near.Clear();
            c.CountNear(c.Pos, AreaR(level) + 1f, _near);
            float each = AreaMult(level) * Slams(level) * 0.5f;
            int n = 1;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == p.Target || !c.Allowed(u, out float cm))
                {
                    continue;
                }
                p.Score += c.Value(u, c.Expected(u, each), cm);
                n++;
            }
            p.Count = n;
            return p;
        }

        // where the body is held through the swing (the holder's sprite pixels: forward, up, its turn)
        private static readonly Grip Top = new Grip(0f, 11f, 0f);
        private static readonly Grip FrontUp = new Grip(6f, 7f, -50f);
        private static readonly Grip Front = new Grip(7f, -1f, -95f, false, 1.12f, 0.88f);
        private static readonly Grip BackUp = new Grip(-6f, 7f, 50f);
        private static readonly Grip Back = new Grip(-7f, -1f, 95f, false, 1.12f, 0.88f);

        private static bool Holding(ChoreoMove m)
        {
            return m.Held != null;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int n = Slams(m.Level);
            Shape topPose = new Shape(-8f, 0.9f, 1.14f, 0f);
            m.StepIn()
                .Grab(0.15f)
                // swung up over its head by the ankles
                .Lift(Top, 0.2f, topPose);
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool front = (i & 1) == 0;
                bool last = i == n - 1;
                // over...
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.06f,
                    To = front ? new Shape(14f, 1.02f, 1f, 0f) : new Shape(-14f, 1f, 1.02f, 0f),
                    Grip = front ? FrontUp : BackUp,
                    Ease = Ease.InQuad,
                    Track = false,
                    Ghosts = 0.03f,
                    Forms = Form.Grab | Form.Throw | Form.Overhead,
                    If = Holding
                });
                // ...and down: the slam
                Beat slam = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.05f,
                    To = front ? new Shape(30f, 1.24f, 0.8f, 0f, 0.1f) : new Shape(-22f, 1.18f, 0.84f, 0f, -0.08f),
                    Grip = front ? Front : Back,
                    Ease = Ease.InQuad,
                    Track = false,
                    Ghosts = 0.025f,
                    Forms = Form.Grab | Form.Throw | Form.Area | Form.Heavy,
                    If = Holding
                };
                slam.Exit = mm => SlamAt(mm, idx, last);
                m.Add(slam);
                // the impact: crouched over it, the pose shaking with the blow
                m.Add(new Beat
                {
                    Tempo = Tempo.Impact,
                    Dur = last ? 0.16f : 0.05f,
                    To = front ? new Shape(34f, 1.3f, 0.74f, 0f, 0.12f) : new Shape(-26f, 1.24f, 0.78f, 0f, -0.1f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Tremble = last
                });
                if (!last)
                {
                    // the rebound: back up and over its head
                    m.Add(new Beat
                    {
                        Tempo = Tempo.Wind,
                        Dur = 0.07f,
                        To = front ? new Shape(6f, 0.96f, 1.08f, 0f) : new Shape(-6f, 0.96f, 1.08f, 0f),
                        Grip = front ? FrontUp : BackUp,
                        Ease = Ease.OutQuad,
                        Track = false,
                        If = Holding
                    });
                    m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.06f, To = topPose, Grip = Top, Ease = Ease.InOutSine, Track = false, If = Holding });
                }
            }
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>The held body meets the ground: the blow, a crater, a shockwave through everyone near (the last lets it go, driven in).</summary>
        private static void SlamAt(ChoreoMove m, int i, bool last)
        {
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim b = m.Held;
            Actor v = b.A;
            Vector2 at = b.P.Pose.Ground;
            WorldTile tile = Combat.TileAt(at);
            float px = m.Px, k = m.Kk;
            HitResult r;
            if (last)
            {
                r = Combat.Hit(m.A, v, FinalMult(level) * m.Cm, true, false, 0f, m);
                bool owed = MeteorSlam.AtDeathsDoor(v);
                m.Held = null;
                Impact im = Impact.Of(Hurt.DriveIn, at - m.Pose.Ground, 0f, 1.6f);
                im.Owed = owed;
                im.Daze = 1.4f;
                im.Stop = 0.05f * m.ImpactK;
                b.LetGo(im, m.A, m);
                m.SetLink(Link.Down, v);
            }
            else
            {
                r = Combat.Hit(m.A, v, SlamMults[Mathf.Min(i, SlamMults.Length - 1)] * m.Cm, false, false, 0f, m);
                b.P.Flash(0.06f);
            }
            m.LastHit = r;
            Victims.Flash(v, Color.white, 0.06f);
            float crater = last ? EarthKit.L(level, 1.3f, 1.5f, 1.8f) : EarthKit.L(level, 0.8f, 0.9f, 1f);
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 0.8f * px);
                if (last && level >= 2 || level >= 3 || r.Crit)
                {
                    Kit.ImpactFrame(hit, px, (last ? 0.42f : 0.26f) * k, m.Hue.Pal, r.Crit, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
                ChoreoFx.Impact(hit, Vector2.down, px, k, last ? 1.9f : 1.3f, m.Hue, m.TopLayer, last);
                Kit.DustRing(at, px, tile, last ? 18 : 12, last ? 60f : 44f, 0.55f, k);
                Kit.Debris(at, px, tile, last ? 12 : 7, k);
                Kit.Crater(at, px * k, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 7f, !last, m.Hue.Edge);
                Kit.Shockwave(at, px, 4f, Mathf.Max(14f, AreaR(level) / px), 0.35f, Fx.WithA(m.Hue.Edge, 0.9f), last ? 3 : 2);
                if (last)
                {
                    EarthKit.Boulders(at, px, 3 + level, k, EarthKit.Stone(at));
                }
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.04f);
            Ruin.Blast(m.A, at, crater, m.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, at, m.Scale, last ? 1.6f : 1.1f, k);
            Kit.Stop(at, (last ? 0.07f : 0.022f) * m.ImpactK);
            m.HoldFor((last ? 0.06f : 0.035f) * m.ImpactK);
            m.AreaBlow(at, AreaR(level) * Mathf.Lerp(1f, k, 0.5f), AreaMult(level), v, Hurt.Knockdown, 0.8f, 1.1f);
            if (Dev.On) Dev.Trace("continental_slam " + (i + 1) + "/" + Slams(level) + " on " + v.asset.id + " hp " + v.getHealth());
        }
    }
}
