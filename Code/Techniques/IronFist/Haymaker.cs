using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HAYMAKER (Iron Fist, Novice). The gamble. It steps in, turns its shoulders away and cocks the
    /// rear arm all the way back, the weight on its back foot, and holds it there trembling while the
    /// power gathers in the fist (crimson motes streaming in; II a ring closing on the knuckles; III the
    /// dust lifting at its feet): Charging, and every reader sees it. A fast blow in that window breaks
    /// it and blows it away. If nothing comes, the fist loops round in a huge hook with everything
    /// behind it (a wide crescent smear, the body turning through past the target): the foe is spun
    /// sideways off its feet, cartwheeling, and comes down on its back. II hits harder on an impact
    /// frame; III's blow sends a shock through everyone standing close. Wind-up I 0.55 s, II 0.5 s,
    /// III 0.42 s (at the tier's tempo). Damage (x the user's damage): I 2.2, II 2.9, III 3.8 (+ x0.4 to
    /// those near at III).
    /// </summary>
    internal sealed class HaymakerTech : Technique
    {
        public override string Id => "haymaker";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Hook | Form.Heavy | Form.Charge;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 10f, 18f, 30f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        /// <summary>What readers see while it winds (a big committed hook, gathering).</summary>
        private const Form Winding = Form.Punch | Form.Hook | Form.Heavy | Form.Charge;

        /// <summary>What they see of it otherwise (no Charge: the wind-up is over).</summary>
        private const Form Swinging = Form.Punch | Form.Hook | Form.High | Form.Heavy;

        public static float Mult(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 2.9f : 3.8f;
        }

        /// <summary>The held part of the wind-up (seconds at Novice pace; the cock adds 0.1).</summary>
        private static float Load(int level)
        {
            return level <= 1 ? 0.45f : level == 2 ? 0.4f : 0.32f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.8f, Total = Mult(1), Crowd = 0.12f, Alone = 8f, IntoGuard = 0.6f, Worth = 12f },
            new MeleeWant { Reach = 2f, Total = Mult(2), Crowd = 0.1f, Alone = 9f, IntoGuard = 0.6f, Worth = 14f },
            new MeleeWant { Reach = 2.2f, Total = Mult(3), Crowd = 0.08f, Alone = 10f, IntoGuard = 0.6f, Worth = 18f }
        };

        /// <summary>
        /// A single big blow on a foe worth it (the Brain weighs the danger of winding up next to quick
        /// enemies, and halves it against a foe winding up itself).
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            Plan p = IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // (III: the shock makes a knot of foes round the target worth more)
            if (level >= 3)
            {
                int near = c.CountNear(p.Target.current_position, 1.3f);
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
            float moteT = 0f;
            bool ringed = false;
            m.StepIn().Shows(Swinging);
            // THE COCK: shoulders turned away, the rear arm drawn all the way back, the weight on the back foot
            m.PoseTo(0.1f, new Shape(-22f, 0.86f, 1.08f, 0f, -0.13f), Ease.OutCubic, Tempo.Wind)
                .AsCharge()
                .Shows(Winding)
                .OnEnter(mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.08f);
                    if (mm.Vis)
                    {
                        ChoreoFx.HeelDust(mm.Stance, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                    }
                });
            // THE LOAD: held there, trembling, the power gathering in the fist (a fast blow breaks it now)
            m.Hold(Load(level), true, Tempo.Wind)
                .AsCharge()
                .Shows(Winding)
                .Every((mm, k) =>
                {
                    if (!mm.Vis)
                    {
                        return;
                    }
                    float px = mm.Px;
                    Vector2 fist = Fist(mm);
                    moteT -= Clock.Dt;
                    if (moteT <= 0f)
                    {
                        moteT = 0.05f;
                        ChoreoFx.Motes(fist, px, 2 + level, 9f * mm.Kk, 0.18f, mm.Hue.Core, mm.Hue.Edge);
                    }
                    if (level >= 2 && !ringed && k > 0.2f)
                    {
                        ringed = true;
                        Fx.Ring(fist, px, 0.3f, 10f * mm.Kk, 2f, 1, 1, FxLayer.Over, 6, 1f, 2f,
                            Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.85f), Fx.WithA(mm.Hue.Core, 0.9f));
                    }
                    if (level >= 3 && Random.value < 0.25f)
                    {
                        ChoreoFx.HeelDust(mm.Stance, Random.insideUnitCircle, px, mm.A.current_tile, mm.Kk * 0.6f);
                    }
                })
                .At(0.8f, mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(Fist(mm), mm.Px, 0.16f, mm.Hue.Core, 0.9f + 0.15f * mm.Level);
                    }
                });
            // THE SWING: the fist loops round with everything behind it
            Hit hay = new Hit(Blow.Hook, Mult(level))
            {
                WindScale = 0.7f,
                ActScale = 1.3f,
                Hurt = Hurt.Launch,
                Push = SpinPush(level),
                Lift = SpinLift(level),
                Daze = 0.8f + 0.2f * level,
                Power = 1.2f + 0.1f * level,
                Finisher = true,
                Shows = Swinging,
                Area = level >= 3 ? 1.3f : 0f,
                AreaMult = 0.4f / Mult(3)
            };
            hay.OnLand = (mm, v, r) => SpinOff(mm, v, hay);
            m.Strike(hay)
                // the follow-through: the body keeps turning past where the foe stood
                .PoseTo(0.09f, new Shape(30f, 1.12f, 0.9f, 0f, 0.16f), Ease.OutQuad, Tempo.Follow)
                .Shows(Swinging)
                .Recover(0.3f)
                .Shows(Swinging);
            return m.Start();
        }

        private static float SpinPush(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.4f;
        }

        private static float SpinLift(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.9f : 1.15f;
        }

        /// <summary>Where the cocked fist is: behind the body, at the shoulder.</summary>
        private static Vector2 Fist(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.3f, m.Hpx * 0.12f) * m.Px;
        }

        /// <summary>
        /// The hook lands: a huge crescent where the fist looped, and the foe is spun sideways off its
        /// feet, cartwheeling through the air, down on its back.
        /// </summary>
        private static void SpinOff(ChoreoMove m, Actor v, Hit h)
        {
            int level = m.Level;
            if (m.Vis)
            {
                int r = Mathf.Clamp(Mathf.RoundToInt((11f + 2f * level) * m.Kk), 6, 60);
                Kit.Swish(m, Art.Arc(r, 0.55f, 195f, 400f, level >= 3 ? 4 : 3), 0.17f, new Vector2(2f * m.Fwd, 1f), m.Fwd, m.Hue.Core, m.Hue.Edge, 7);
                Kit.SpeedLines(m.VPoint(0.6f), Choreo.Rotate(m.Dir, -55f * m.Fwd), m.Px, 3 + level, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
                if (level >= 3)
                {
                    Kit.Shockwave(Victims.GroundOf(v), m.Px, 3f, 14f * m.Kk, 0.32f, Fx.WithA(m.Hue.Edge, 0.85f));
                    Kit.DustRing(Victims.GroundOf(v), m.Px, v.current_tile, 10, 40f, 0.5f, m.Kk);
                }
            }
            Impact im = Impact.Of(Hurt.Launch, Choreo.Rotate(m.Dir, -55f * m.Fwd), SpinPush(level) * Mathf.Lerp(1f, m.Kk, 0.5f), 1.4f + 0.1f * level);
            im.Lift = SpinLift(level);
            im.Spin = (Random.value < 0.5f ? -1f : 1f) * (level >= 3 ? 900f : 720f);
            im.Daze = h.Daze;
            im.Stop = (0.05f + 0.015f * level) * m.ImpactK;
            IronFist.Rehurt(m, v, h, im, null, Form.None);
        }
    }
}
