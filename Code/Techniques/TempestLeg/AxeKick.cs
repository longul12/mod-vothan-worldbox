using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// AXE KICK (Tempest Leg, Adept). It steps in and the leg goes straight up: a thin crescent climbs in
    /// front of it to the vertical, the body leans back stretched tall on its standing toes, a glint at
    /// the heel; it holds there trembling (Charging: a fast blow now breaks it, the gamble of every big
    /// wind-up), and the heel is hammered down on the crown of the head: a crescent falling over the top,
    /// an impact frame, and the foe is driven into the ground (crushed, sunk, a small crater; Down). Over
    /// a guard (it answers Guard): the heel comes down where the forearms are not. II: a faster rise, a
    /// wider crater, a ring of dust and rock. III: the heel lands on an impact frame that freezes the
    /// world a beat, the crater glows, and the shock knocks down everyone within 1.6 tiles.
    /// Damage (x the user's damage): I 1.6; II 2.2; III 3.0 (+ x0.5 on those around).
    /// </summary>
    internal sealed class AxeKickTech : Technique
    {
        public override string Id => "axe_kick";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Overhead | Form.Heavy;
        public override Form Answers => Form.Guard;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        private const Form Shows = Form.Kick | Form.Overhead | Form.Heavy;

        public static float Mult(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.2f : 3f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2f, Total = Mult(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 0.75f, Worth = 10f },
            new MeleeWant { Reach = 2.2f, Total = Mult(2), Crowd = 0.04f, Alone = 9f, IntoGuard = 0.75f, Worth = 12f },
            new MeleeWant { Reach = 2.4f, Total = Mult(3), Crowd = 0f, Alone = 10f, IntoGuard = 0.75f, Worth = 15f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // the long wind-up is a gamble next to a quick foe that is free to punish it
            FoeState s = c.State(v);
            if (!s.Down && !s.Stunned && !s.Guarding && !s.Busy && Moves.CanPerform(v) && c.Guard(v, Form.Charge) > 0)
            {
                p.Score *= 0.6f;
            }
            // III: the shock knocks down those around it too
            if (level >= 3)
            {
                int near = c.CountNear(v.current_position, 1.6f);
                p.Score += Mathf.Max(0, near - 1) * 8f;
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
            m.StepIn();
            // the leg goes up: leaning back, stretched tall on the standing toes, a crescent climbing to the vertical
            m.PoseTo(0.09f, new Shape(-14f, 0.9f, 1.14f, 0.03f, -0.02f), Ease.OutQuad, Tempo.Wind).Shows(Shows).AsCharge().OnEnter(mm =>
            {
                TempestKicks.Arc(mm, 10, 1f, -45f, 95f, 2, new Vector2(2f, 2f), mm.BeatDur * 2.2f, 5);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                if (mm.Vis)
                {
                    ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                }
            }).At(0.95f, mm =>
            {
                if (mm.Vis)
                {
                    // the heel at the top, over its head
                    Fx.Glint(mm.HeadPos() + new Vector2(mm.Fwd * 2.5f * mm.Px, 3f * mm.Px), mm.Px, 0.2f, mm.Hue.Core, mm.Level >= 2 ? 1f : 0.8f);
                }
            });
            m.Strike(new Hit(Blow.AxeKick, Mult(level))
            {
                Charged = true,
                WindScale = level <= 1 ? 2.4f : level == 2 ? 2.1f : 1.8f,
                Crater = level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.2f,
                Daze = level <= 1 ? 1f : level == 2 ? 1.2f : 1.5f,
                Power = level >= 3 ? 1.3f : 1.1f,
                Finisher = true,
                Shows = Shows,
                OnLand = (mm, v, r) =>
                {
                    Vector2 g = mm.VG;
                    if (mm.Vis)
                    {
                        Kit.DustRing(g, mm.Px, Combat.TileAt(g), 8 + 4 * mm.Level, 34f + 6f * mm.Level, 0.5f, mm.Kk);
                        if (mm.Level >= 2)
                        {
                            Kit.Debris(g, mm.Px, Combat.TileAt(g), 4 + 3 * mm.Level, mm.Kk);
                            Kit.Shockwave(g, mm.Px, 3f, (1.1f + 0.4f * mm.Level) / mm.Px, 0.3f, Fx.WithA(mm.Hue.Edge, 0.85f), 2);
                        }
                        Kit.SpeedLines(mm.VPoint(1f), Vector2.down, mm.Px, 3, 3f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.8f), mm.TopLayer);
                    }
                    if (mm.Level >= 3)
                    {
                        mm.AreaBlow(g, 1.6f * Mathf.Lerp(1f, mm.Kk, 0.5f), Mult(3) * 0.5f, v, Hurt.Knockdown, 0.6f, 1.1f);
                    }
                }
            });
            m.Recover(0.26f);
            return m.Start();
        }
    }
}
