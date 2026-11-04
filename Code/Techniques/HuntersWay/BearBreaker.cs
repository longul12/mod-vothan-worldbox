using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BEAR BREAKER (Hunter's Way, Adept). The hunter walks into a big beast's embrace on purpose:
    /// it locks its arms round the creature's middle and the two strain against each other, swaying
    /// left and right, heels ploughing the dirt, trembling with the effort (it shows Grab and Heavy:
    /// a quick blow can still break it up); then the hunter sinks, heaves the beast up over its hip
    /// and turns it right over in the air, bringing it down on its back in front of it with a thud
    /// that throws up a ring of dust (a long beast lands belly-up and is held there a heartbeat
    /// before it rolls back onto its feet, dazed). II: the struggle is longer, the throw breaks the
    /// ground and the shock knocks over whoever stands near. III: the hunter drops an elbow into the
    /// upturned belly on an impact frame. Made for big beasts and brutes (the Brain uses it on
    /// nothing else); a grab: only what it can lift (no giants, no fliers). Damage (x the user's
    /// damage): I 0.1 + 1.8; II 0.1 + 2.4 (+ 0.5 of it round); III 0.1 + 2.6 + 0.9. Leaves Down.
    /// </summary>
    internal sealed class BearBreakerTech : Technique
    {
        public override string Id => "bear_breaker";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Grab | Form.Hunt | Form.Heavy;
        public override Prey Prey => Prey.Brute | Prey.Beast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 26f, 42f };
        public override float[] Cooldown => new[] { 7f, 8.5f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        private const float Clinch = 0.1f, Elbow = 0.9f;

        private static float Flip(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.4f : 2.6f;
        }

        public static float Total(int level)
        {
            return Clinch + Flip(level) + (level >= 3 ? Elbow : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Total(1), Hold = true, Crowd = 0.1f, Alone = 10f, IntoGuard = 1.3f, Worth = 14f },
            new MeleeWant { Reach = 2.6f, Total = Total(2), Hold = true, Crowd = 0.08f, Alone = 10f, IntoGuard = 1.3f, Worth = 18f },
            new MeleeWant { Reach = 2.8f, Total = Total(3), Hold = true, Crowd = 0.06f, Alone = 12f, IntoGuard = 1.3f, Worth = 22f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = HwLoKit.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (!Combat.SafeSpot(p.Target.current_position) && !Choreo.Wading(p.Target.current_position))
            {
                return null;
            }
            // the bigger the beast, the more it is the one this is for
            Actor v = p.Target;
            if (Weights.Species(v) >= Weight.Brute || v.actor_scale > c.Self.actor_scale * 1.15f)
            {
                p.Score += 12f;
            }
            return p;
        }

        // the elbow dropped into the upturned belly
        private static readonly BlowShape ElbowDrop = new BlowShape
        {
            Limb = Limb.Elbow,
            Wind = new Shape(-16f, 0.92f, 1.12f, 0.35f, 0.14f),
            WindDur = 0.14f,
            Hit = new Shape(48f, 1.24f, 0.78f, 0f, 0.36f),
            ActDur = 0.08f,
            FollowDur = 0.1f,
            At = 0.6f,
            Relax = 0.2f,
            Contact = 0.1f,
            ArcR = 7,
            ArcRatio = 0.9f,
            A0 = 110f,
            A1 = -60f,
            ArcThick = 3,
            ArcOff = new Vector2(2f, 2f),
            Weight = 1.35f,
            Heavy = true,
            Hurt = Hurt.Pin,
            Forms = Form.Elbow | Form.Overhead | Form.Heavy | Form.Hunt
        };

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            HwLoKit.FitStand(m);
            int level = m.Level;
            // into the beast's embrace: arms locked round its middle
            m.StepIn().Grab(Clinch, Grip.Hug);
            // the struggle: swaying left and right, heels ploughing, trembling with the effort
            int sways = level <= 1 ? 2 : 3;
            for (int i = 0; i < sways; i++)
            {
                float s = (i & 1) == 0 ? 1f : -1f;
                m.Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = i == 0 ? 0.16f : 0.13f,
                    To = new Shape(12f * s - 4f, 1.07f, 0.93f, 0f, 0.04f * s, 0.06f * s),
                    Ease = Ease.InOutSine,
                    Grip = new Grip(2.5f, 0.5f + 0.4f * s, -10f * s, false, 0.94f, 1.04f),
                    Track = false,
                    Tremble = true,
                    Dust = 0.07f,
                    Forms = Form.Grab | Form.Heavy | Form.Hunt,
                    If = mm => mm.Held != null,
                    Enter = mm =>
                    {
                        if (mm.Vis)
                        {
                            ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                            ChoreoFx.Skid(mm.Pose.Ground, mm.Pose.Ground - mm.Dir * 0.25f * mm.Kk, mm.Px, mm.A.current_tile);
                        }
                        Choreo.Sound("event:/SFX/HIT/HitGeneric", mm.A.current_tile, 0.1f);
                    }
                });
            }
            // it sinks under the weight, then heaves the beast up over its hip...
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.1f,
                To = new Shape(14f, 1.18f, 0.82f, 0f, 0.02f),
                Ease = Ease.OutQuad,
                Grip = new Grip(2.6f, -0.2f, 6f, false, 0.94f, 1.04f),
                Track = false,
                Forms = Form.Grab | Form.Throw | Form.Heavy,
                If = mm => mm.Held != null,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                    }
                }
            });
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.16f,
                To = new Shape(-26f, 0.9f, 1.16f, 0.04f, -0.04f),
                Ease = Ease.OutCubic,
                Grip = new Grip(1.2f, 8.5f, -120f),
                Track = false,
                Ghosts = 0.035f,
                Forms = Form.Grab | Form.Throw | Form.Heavy | Form.Hunt,
                If = mm => mm.Held != null,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.SpeedLines(mm.BodyPos() + new Vector2(0f, 3f * mm.Px), Vector2.up, mm.Px, 3, 4f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.75f));
                    }
                }
            });
            // ...and turns it right over in the air, down on its back in front of it (a long, side-on beast
            // lands belly-up, legs in the air, and is held there; anything upright is laid flat and pinned)
            bool wide = HwLoKit.Wide(p.Target);
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.14f,
                To = new Shape(36f, 1.18f, 0.82f, 0f, 0.26f),
                Ease = Ease.InQuad,
                Grip = wide ? new Grip(4.4f, -0.6f, -180f) : new Grip(4.6f, -1.4f, -270f),
                Track = false,
                Ghosts = 0.025f,
                Forms = Form.Grab | Form.Throw | Form.Heavy | Form.Overhead,
                If = mm => mm.Held != null,
                Every = (mm, k) =>
                {
                    if (mm.Vis && k < 0.85f)
                    {
                        Kit.SpeedLines(mm.BodyPos() + mm.Dir * 3f * mm.Px, Vector2.down, mm.Px, 1, 4f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f));
                    }
                },
                Exit = mm =>
                {
                    float crater = level <= 1 ? 0f : level == 2 ? 0.6f : 0.85f, area = level >= 2 ? 1.3f : 0f;
                    if (wide)
                    {
                        HwLoKit.SlamHeld(mm, Flip(level), crater, area, 0.5f, level >= 2);
                    }
                    else
                    {
                        HwLoKit.ThrowDown(mm, Flip(level), Hurt.Pin, level <= 1 ? 0.9f : 1.1f, level <= 1 ? 1f : 1.5f, crater, area, 0.5f, level >= 2, level <= 2);
                    }
                }
            });
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(30f, 1.2f, 0.82f, 0f, 0.26f), Ease = Ease.OutExpo, Track = false });
            if (level >= 3)
            {
                // the elbow into the upturned belly
                HwLoKit.Strike(m, ElbowDrop, new Hit(Blow.Elbow, Elbow) { Hurt = Hurt.Pin, Daze = 2f, Finisher = true, Power = 1.3f, Crater = 0.6f });
                m.Do(mm => HwLoKit.HuntRing(mm, mm.VG, 1.8f, 0.3f));
            }
            if (wide)
            {
                // held down belly-up a heartbeat, then it rolls back onto its feet, dazed
                m.Hold(0.12f, true, Tempo.Follow).OnlyIf(mm => mm.Held != null);
                HwLoKit.RollFree(m, level <= 1 ? 1f : level == 2 ? 1.5f : 2f, true);
            }
            m.Leaves(Link.Down);
            m.Recover(0.28f);
            return m.Start();
        }
    }
}
