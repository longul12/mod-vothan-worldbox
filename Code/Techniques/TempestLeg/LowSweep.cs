using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// LEG SWEEP (Tempest Leg, Novice). It steps in, drops into a squat on the planted leg, and spins low,
    /// the other leg stretched out along the ground: a flat crescent races round at ankle height, dust
    /// whips off the ground, and the foe's feet fly up: it goes over on its back and lies there (Down: the
    /// axe kick, a stomp or a grab come next). II: the sweep is longer and takes the legs of whoever stands
    /// right beside the foe too. III: the foe's feet are swept higher, and the spin carries on round in a
    /// second turn low on the ground, its crescent knocking down everyone within 1.5 tiles.
    /// Damage (x the user's damage): I 1.0; II 1.25 (+ x0.5 on those beside it); III 1.4 (+ x0.6 around).
    /// Giants are only rocked (their legs are tree trunks): it plans them rarely.
    /// </summary>
    internal sealed class LowSweepTech : Technique
    {
        public override string Id => "low_sweep";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Low | Form.Spin;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 11f, 17f };
        public override float[] Cooldown => new[] { 4.5f, 5f, 6f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const Form Shows = Form.Kick | Form.Low | Form.Spin;

        public static float Mult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.25f : 1.4f;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 1.1f : 1.5f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.8f, Total = Mult(1), Worthy = false, Crowd = 0.04f, Alone = 4f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2f, Total = Mult(2), Worthy = false, Crowd = 0f, Alone = 4f, IntoGuard = 0.8f, Worth = 7f },
            new MeleeWant { Reach = 2.2f, Total = Mult(3), Worthy = false, Crowd = 0f, Alone = 5f, IntoGuard = 0.8f, Worth = 9f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // a body already lying down has no legs to take
            if (c.State(v).Down)
            {
                return null;
            }
            // tree trunks: a giant is only rocked by it
            if (Weights.Heavy(v))
            {
                p.Score *= 0.45f;
            }
            // II and III: the sweep takes the legs of the ones beside it too
            if (level >= 2)
            {
                int near = c.CountNear(v.current_position, Radius(level));
                p.Score += Mathf.Max(0, near - 1) * 5f;
            }
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
            float mult = Mult(level);
            m.StepIn();
            // down into the squat: the weight drops onto the planted leg, the sweeping one already low
            m.PoseTo(0.06f, new Shape(16f, 1.26f, 0.68f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind).Shows(Shows).OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                }
            });
            m.Strike(new Hit(Blow.Sweep, mult)
            {
                Hurt = Hurt.Knockdown,
                Lift = level <= 1 ? 0.45f : level == 2 ? 0.55f : 0.75f,
                Daze = level <= 1 ? 0.7f : level == 2 ? 0.9f : 1.1f,
                Push = level <= 1 ? 0.25f : 0.35f,
                WindScale = 1.1f,
                Power = level >= 3 ? 1.2f : 1f,
                Shows = Shows,
                OnLand = (mm, v, r) =>
                {
                    // the leg rips along the ground: a fan of dust off the sweep, a scuff under the foe
                    TempestKicks.Whip(mm, mm.VG, mm.Side * -mm.Fwd + mm.Dir * 0.4f, 3 + mm.Level);
                    if (mm.Vis)
                    {
                        ChoreoFx.Skid(mm.Pose.Ground + mm.Dir * mm.Px * 3f, mm.VG, mm.Px, mm.A.current_tile);
                    }
                    if (mm.Level == 2)
                    {
                        mm.AreaBlow(mm.VG, Radius(2), Mult(2) * 0.5f, v, Hurt.Knockdown, 0.35f, 0.9f);
                    }
                }
            });
            if (level >= 3)
            {
                // the spin carries on round: a second turn low on the ground, its crescent sweeping everyone near
                bool[] swept = { false };
                m.Add(TempestKicks.LowSpin(1f, 0.24f, Radius(3), 26f, (mm, th) =>
                {
                    if (!swept[0] && th >= Mathf.PI)
                    {
                        swept[0] = true;
                        mm.AreaBlow(mm.Pose.Ground, Radius(3) * Mathf.Lerp(1f, mm.Kk, 0.5f), Mult(3) * 0.6f, mm.V, Hurt.Knockdown, 0.5f, 1f);
                    }
                }));
                m.OnEnter(mm =>
                {
                    TempestKicks.Arc(mm, 15, 0.34f, 180f, 540f, 2, new Vector2(0f, -mm.Pose.PivotUp + 1.2f), mm.BeatDur * 1.5f, 5);
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                        Kit.Shockwave(mm.Pose.Ground, mm.Px, 4f, Radius(3) / mm.Px, 0.3f, Fx.WithA(mm.Hue.Edge, 0.8f), 1);
                    }
                    Ruin.Blast(mm.A, mm.Pose.Ground, 0.8f, mm.Scale, mm.Pose.Ground);
                });
            }
            m.Recover(0.22f);
            return m.Start();
        }
    }
}
