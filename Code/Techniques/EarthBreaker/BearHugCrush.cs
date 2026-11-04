using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BEAR HUG (Earth Breaker, Master). It steps in, wraps the foe in both arms and hauls it off its
    /// feet against its chest, leaning back under the weight. Then it crushes: squeeze after squeeze,
    /// faster and faster, rocking from side to side as the body is pinched thin in its arms (the held
    /// one struggles, kicking), each squeeze a crack of sparks and a ring of force closing on the pair.
    /// The last squeeze is the hardest (a held, trembling crush; an impact frame from II), and the limp
    /// body is flung away to land on its back (Link.Thrown). I squeezes 3 times, II 4, III 5 (and its
    /// fling cracks the ground where it lands, knocking down those near). A grab: it beats a guard; a
    /// body it can't lift (giants, flyers) is not for it. Damage (x the user's damage): I 2 x 0.35 + 0.6
    /// + 0.8 (the fling); II 3 x 0.38 + 0.7 + 0.95; III 4 x 0.4 + 0.8 + 1.2.
    /// </summary>
    internal sealed class BearHugCrushTech : Technique
    {
        public override string Id => "bear_hug_crush";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Body | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 30f, 48f };
        public override float[] Cooldown => new[] { 9f, 10f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static int Squeezes(int level)
        {
            return EarthKit.L(level, 3, 4, 5);
        }

        private static float SqueezeMult(int level)
        {
            return EarthKit.L(level, 0.35f, 0.38f, 0.4f);
        }

        private static float FinalMult(int level)
        {
            return EarthKit.L(level, 0.6f, 0.7f, 0.8f);
        }

        private static float FlingMult(int level)
        {
            return EarthKit.L(level, 0.8f, 0.95f, 1.2f);
        }

        public static float Total(int level)
        {
            return 0.12f + (Squeezes(level) - 1) * SqueezeMult(level) + FinalMult(level) + FlingMult(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Total(1), Hold = true, Crowd = 0.12f, Alone = 12f, IntoGuard = 1.2f, Worth = 16f },
            new MeleeWant { Reach = 2.4f, Total = Total(2), Hold = true, Crowd = 0.1f, Alone = 14f, IntoGuard = 1.2f, Worth = 20f },
            new MeleeWant { Reach = 2.6f, Total = Total(3), Hold = true, Crowd = 0.08f, Alone = 16f, IntoGuard = 1.2f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // a long hold: not in the middle of a mob (it would be beaten while it squeezes)
            if (c.Near2 >= 4)
            {
                return null;
            }
            return p;
        }

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
            int level = m.Level;
            int n = Squeezes(level);
            m.StepIn()
                .Grab(0.12f, Grip.Hug)
                // hauled off its feet against its chest, leaning back under the weight
                .Lift(new Grip(2f, 2.6f, -4f, false, 0.9f, 1.06f), 0.14f, new Shape(-12f, 0.94f, 1.1f, 0f));
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool last = i == n - 1;
                float pace = Mathf.Pow(0.88f, i);
                float sway = (i & 1) == 0 ? -18f : -6f;   // it rocks from side to side as it squeezes
                // the squeeze: the arms close, the body is pinched thin
                Beat crush = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = (last ? 0.1f : 0.07f) * pace,
                    To = new Shape(last ? -22f : sway, last ? 1.2f : 1.12f, last ? 0.84f : 0.9f, 0f, 0.02f),
                    Grip = new Grip(1.6f, 2.6f, (i & 1) == 0 ? 8f : -8f, false, last ? 0.72f : 0.8f, last ? 1.14f : 1.1f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = Form.Grab | Form.Body | Form.Heavy,
                    If = Holding
                };
                crush.On(0.6f, mm => Crush(mm, idx, last));
                m.Add(crush);
                if (last)
                {
                    // the hardest squeeze is held, trembling
                    m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.16f, Tremble = true, Track = false, If = Holding });
                }
                else
                {
                    // eases a hair, the body struggling, and squeezes again
                    m.Add(new Beat
                    {
                        Tempo = Tempo.Follow,
                        Dur = 0.08f * pace,
                        To = new Shape(-10f, 0.95f, 1.06f, 0f),
                        Grip = new Grip(2f, 2.6f, (i & 1) == 0 ? -3f : 3f, false, 0.9f, 1.06f),
                        Ease = Ease.OutQuad,
                        Tremble = true,
                        Track = false,
                        If = Holding
                    });
                }
            }
            // flung away: a twist back, and the limp body is hurled
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.1f, To = new Shape(-26f, 0.92f, 1.1f, 0f), Grip = new Grip(-1f, 4f, -40f), Ease = Ease.OutQuad, Track = false, If = Holding });
            Beat fling = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(24f, 1.22f, 0.86f, 0f, 0.14f), Grip = new Grip(6f, 3f, 50f), Ease = Ease.OutExpo, Track = false, If = Holding };
            fling.Exit = Fling;
            m.Add(fling);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.12f, To = new Shape(14f, 1.1f, 0.92f, 0f, 0.08f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>A squeeze lands: a crack of sparks, a ring of force closing on the pair, the body flashing.</summary>
        private static void Crush(ChoreoMove m, int i, bool last)
        {
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim h = m.Held;
            Actor v = h.A;
            HitResult r = Combat.Hit(m.A, v, (last ? FinalMult(level) : SqueezeMult(level)) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            h.P.Flash(last ? 0.08f : 0.05f);
            m.HoldFor((last ? 0.06f : 0.025f) * m.ImpactK);
            Choreo.Sound(last ? "event:/SFX/HIT/HitStone" : "event:/SFX/HIT/HitGeneric", m.A.current_tile, 0.04f);
            float px = m.Px, k = m.Kk;
            if (m.Vis)
            {
                Vector2 at = h.P.BodyPos();
                Kit.Sparks(at, px, (last ? 8 : 4) + i, 30f, last ? 110f : 80f, Color.Lerp(m.Hue.Core, m.Hue.Edge, Random.value));
                Fx.Ring(at, px, 0.13f, (last ? 14f : 10f) * k, 2.5f * k, last ? 2 : 1, 1, FxLayer.Over, 6, 0.9f, 1.5f,
                    Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Core, 0.95f));
                ChoreoFx.Impact(at + new Vector2(m.Fwd * Random.Range(-1.5f, 1.5f), Random.Range(-1f, 1.5f)) * px, Vector2.up, px, k, last ? 1.6f : 0.8f, m.Hue, m.TopLayer, last);
                if (last && (level >= 2 || r.Crit))
                {
                    Kit.ImpactFrame(at, px, 0.3f * k, m.Hue.Pal, r.Crit, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
            }
            if (last)
            {
                Kit.Stop(m.BodyPos(), 0.04f * m.ImpactK);
                Ruin.Quake(m.A, m.Pose.Ground, m.Scale, 1.1f, k);
            }
            else if (i >= 2)
            {
                Ruin.Quake(m.A, m.Pose.Ground, Scale.Casual, 0.6f, k);
            }
        }

        private static void Fling(ChoreoMove m)
        {
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim h = m.Held;
            Actor v = h.A;
            Vector2 from = h.P.Pose.Ground;
            float dist = EarthKit.L(level, 3f, 4f, 5f) * Mathf.Lerp(1f, m.Kk, 0.5f);
            Vector2 want = Combat.Clamp(m.Stance + m.Dir * dist);
            Vector2 dest = Combat.FindLanding(want, 2f, out Vector2 spot) ? spot : m.Ahead(dist * 0.5f);
            HitResult r = Combat.Hit(m.A, v, FlingMult(level) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            m.Held = null;
            Impact im = Impact.Of(Hurt.Thrown, dest - from, 0f, 1.3f);
            im.To = dest;
            im.Lift = 0.9f + 0.12f * dist;
            im.Spin = -420f;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            im.Crater = level >= 2 ? (level == 2 ? 0.6f : 0.9f) : 0f;
            im.AreaR = level >= 3 ? 1.2f : 0f;
            im.AreaMult = 0.4f * FlingMult(level);
            im.Daze = 1.1f;
            h.LetGo(im, m.A, m);
            m.SetLink(Link.Thrown, v);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                Kit.SpeedLines(h.P.BodyPos(), dest - from, m.Px, 4, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
            }
        }
    }
}
