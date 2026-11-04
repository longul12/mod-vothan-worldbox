using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// ROUNDHOUSE (Tempest Leg, Novice). It steps in, rises on the ball of its standing foot and pivots
    /// (the body turning away, narrowing), leans away from the kick, and the shin whips round at head
    /// height: a wide flat crescent sweeps across the front of the foe's head, a gust of air bursts off it,
    /// and the foe's head is turned with the blow: it spins half round and reels sideways. Made for a foe
    /// that curls up behind its guard (it answers Guard: a kick comes round the forearms; the Brain picks
    /// it against a stance). II: a skipping roundhouse (a hop in, the hip whipped over harder): a double
    /// crescent, the foe reels further. III: the tornado kick: a crouch, a leap, one and a half turns in
    /// the air on a ring of afterimages, and the roundhouse lands at the top of the leap on an impact
    /// frame; the foe is spun round and thrown, and the gust knocks back whoever stands near it.
    /// Damage (x the user's damage): I 1.2; II 1.6; III 2.2 (+ x0.4 on those near).
    /// </summary>
    internal sealed class RoundhouseTech : Technique
    {
        public override string Id => "roundhouse";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.High | Form.Spin;
        public override Form Answers => Form.Guard;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 7f, 12f, 19f };
        public override float[] Cooldown => new[] { 5f, 5.5f, 6.5f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const Form Shows = Form.Kick | Form.High | Form.Spin;

        public static float Mult(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.6f : 2.2f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.9f, Total = Mult(1), Worthy = false, Crowd = 0.04f, Alone = 5f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2.4f, Total = Mult(2), Worthy = false, Crowd = 0.04f, Alone = 6f, IntoGuard = 0.8f, Worth = 8f },
            new MeleeWant { Reach = 2.8f, Total = Mult(3), Crowd = 0.04f, Alone = 8f, IntoGuard = 0.8f, Worth = 10f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // III leaps: solid ground to take off from and to come down on
            if (level >= 3 && !Combat.SafeSpot(v.current_position) && !Choreo.Wading(v.current_position))
            {
                return null;
            }
            // not wasted on a body the kick would only flatten twice over
            Combat.Allowed(c.Self, v, out float cm);
            if (v.getHealth() <= c.Expected(v, 0.6f) * cm)
            {
                return null;
            }
            // the head is out of a giant's reach: the shin only meets its leg
            if (v.stats["size"] > c.Self.stats["size"] * 2.5f)
            {
                p.Score *= 0.6f;
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
            if (level >= 3)
            {
                Tornado(m, mult);
            }
            else
            {
                if (level == 2)
                {
                    m.Add(TempestKicks.SkipIn(Shows, 0.045f));
                }
                else
                {
                    m.StepIn();
                }
                // up on the ball of the standing foot, the hips turning away: the chamber
                m.PoseTo(0.07f, new Shape(-10f, 0.84f, 1.08f, 0.03f, -0.02f), Ease.OutQuad, Tempo.Wind).Shows(Shows).OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk * 0.7f);
                    }
                });
                m.Strike(new Hit(Blow.Roundhouse, mult)
                {
                    Hurt = Hurt.Spin,
                    Push = level <= 1 ? 0.8f : 1.1f,
                    Power = level <= 1 ? 1.05f : 1.2f,
                    Shows = Shows,
                    OnLand = (mm, v, r) => Gust(mm, v)
                });
                if (level >= 2)
                {
                    // the hip whipped over harder: a second, higher crescent trails the first
                    m.OnEnter(mm => TempestKicks.Arc(mm, 12, 0.36f, 200f, 380f, 2, new Vector2(1f, 4f), 0.12f, 5));
                }
            }
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>III: the tornado kick (a crouch, a leap turning one and a half times, the roundhouse at its top).</summary>
        private static void Tornado(ChoreoMove m, float mult)
        {
            m.StepIn();
            m.Crouch(0.08f, 0.9f);
            float face = 1f;
            Beat leap = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.24f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.StandSpot(mm.Stand),
                Apex = 0.25f,
                To = new Shape(-12f, 0.92f, 1.08f, 0.42f),
                Ease = Ease.OutQuad,
                Ghosts = 0.025f,
                NeedsVictim = true,
                Forms = Shows | Form.Aerial
            };
            leap.Enter = mm =>
            {
                face = mm.Fwd;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 8, 36f, 0.45f, mm.Kk);
                }
                // a ring of wind wound round it as it turns
                TempestKicks.Arc(mm, 10, 0.4f, 0f, 360f, 2, new Vector2(0f, 0f), mm.BeatDur * 1.4f, 4);
            };
            leap.Every = (mm, k) =>
            {
                // one and a half turns in the air: the sprite narrows, flips, widens
                float th = Mathf.PI * 3f * Beat.Apply(Ease.InOutSine, k);
                float c = Mathf.Cos(th);
                mm.Pose.SX *= Mathf.Max(0.22f, Mathf.Abs(c));
                mm.Pose.FaceRight = c >= 0f ? face > 0f : face < 0f;
            };
            leap.Exit = mm => mm.Pose.FaceRight = mm.Fwd > 0f;
            m.Add(leap);
            m.Strike(new Hit(Blow.Roundhouse, mult)
            {
                Air = true,
                Hurt = Hurt.Spin,
                Push = 1.6f,
                Power = 1.35f,
                WindScale = 0.5f,
                Finisher = true,
                Shows = Shows | Form.Aerial,
                OnLand = (mm, v, r) =>
                {
                    Gust(mm, v);
                    mm.AreaBlow(mm.VG, 1.2f * Mathf.Lerp(1f, mm.Kk, 0.5f), Mult(3) * 0.4f, v, Hurt.Reel, 0.7f, 0.9f);
                }
            });
            m.OnEnter(mm => TempestKicks.Arc(mm, 13, 0.36f, 200f, 390f, 2, new Vector2(1f, 4f), 0.14f, 5));
            m.Drop(0.14f, 0f, 4f);
            m.Land();
        }

        /// <summary>A gust bursting off the shin at the foe's head, along the way the kick swept it.</summary>
        private static void Gust(ChoreoMove m, Actor v)
        {
            Vector2 head = m.VPoint(0.82f);
            Vector2 sweep = Choreo.Rotate(m.Dir, -40f * m.Fwd);
            TempestKicks.Burst(m, head, sweep, m.Level >= 3 ? 1.6f : m.Level == 2 ? 1.2f : 0.9f);
        }
    }
}
