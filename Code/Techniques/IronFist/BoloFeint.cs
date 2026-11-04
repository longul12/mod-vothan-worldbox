using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BOLO FEINT (Iron Fist, Expert). The showman's bait. Just out of reach it starts winding its rear
    /// arm round in big wheels at its side, bobbing with each turn, the arm brighter every time round:
    /// to every reader it is gathering power (it shows Charge, and it IS open: a blow that lands now
    /// breaks it). That is the point. The moment a foe commits to it (an interrupt darting in, a blow,
    /// a technique aimed at it, a shot about to arrive), it drops under it: the committed blow tears
    /// through the afterimage of the wheel, and the wheeling arm comes up from below with all its
    /// momentum, the rising bolo: the foe is launched straight up (Launched), and remembers it was
    /// fooled. Nobody bites: the wheel ends in a quick jab (II two jabs, III a jab and a cross).
    /// Damage (x the user's damage): the bolo I 2.4, II 3.1, III 4; the jabs I 0.6, II 0.9, III 1.4.
    /// </summary>
    internal sealed class BoloFeintTech : Technique
    {
        public override string Id => "bolo_feint";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.IronFist;
        public override Form Forms => Form.Feint | Form.Rising | Form.Punch;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 9f, 9f, 10f };

        public static float Bolo(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3.1f : 4f;
        }

        public static float Jabs(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.4f;
        }

        /// <summary>How long the wheel turns, at Novice pace (the bait's window).</summary>
        public static float Wheel(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.66f : 0.72f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Bolo(1), Crowd = 0.06f, Alone = 6f, IntoGuard = 1f, Worth = 14f },
            new MeleeWant { Reach = 2.6f, Total = Bolo(2), Crowd = 0.05f, Alone = 7f, IntoGuard = 1f, Worth = 18f },
            new MeleeWant { Reach = 2.8f, Total = Bolo(3), Crowd = 0.04f, Alone = 8f, IntoGuard = 1f, Worth = 22f }
        };

        /// <summary>
        /// Worth what it will probably land: the bolo if the foe bites (likelier for one that breaks
        /// charges, an aggressive one, a Phantom Step walker; much less for one that learned this
        /// feint, and a plain unit held by the show never commits), the jabs if it doesn't; more with
        /// other quick fighters near who might take the bait too.
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            Plan p = IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            float bite = Bite(v, c.Self);
            Combat.Allowed(c.Self, v, out float cm);
            p.Score = c.Value(v, c.Expected(v, bite * Bolo(level) + (1f - bite) * Jabs(level)), cm);
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 4f; i++)
            {
                Actor u = c.Enemies[i];
                if (u != v && Fighters.TryGet(u, out Fighter uf) && (uf.Arsenal & Form.Interrupt) != 0)
                {
                    p.Score += 6f;
                }
            }
            return p;
        }

        /// <summary>The chance 'v' commits to the bait of 'by'.</summary>
        private static float Bite(Actor v, Actor by)
        {
            if (!Fighters.TryGet(v, out Fighter f))
            {
                return 0.08f;   // a plain unit: pinned by the show, it never commits
            }
            float b = 0.4f;
            if ((f.Arsenal & Form.Interrupt) != 0)
            {
                b += 0.3f;
            }
            if (Schools.Knows(v, School.PhantomStep))
            {
                b += 0.1f;
            }
            Persona per = f.Persona;
            if (per.Main == Style.Swarmer || per.Main == Style.Brawler || per.Main == Style.Berserker)
            {
                b += 0.1f;
            }
            FoeNotes n = f.Recall(by);
            if (n != null && n.Fooled > 0 && per.Patience > 0.5f)
            {
                b -= 0.3f;   // it learned this one
            }
            return Mathf.Clamp(b, 0.05f, 0.9f);
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            Bait bait = new Bait();
            int level = m.Level;
            const Form shows = Form.Punch | Form.Heavy | Form.Charge;
            m.StepIn().Shows(Form.Punch | Form.Feint);
            // THE WHEEL: the rear arm turning in big circles at its side, the body bobbing with it (Charging:
            // every reader sees a wind-up, and a blow that lands now does break it)
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Wheel(level),
                To = new Shape(-6f, 1.02f, 0.98f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Charging = true,
                Forms = shows,
                NeedsVictim = true,
                Enter = mm => bait.Reset(),
                Every = (mm, k) => Turn(mm, bait, k)
            });
            // nobody bit: the quick jab (II two, III a jab and a cross)
            m.Strike(new Hit(Blow.Jab, level >= 3 ? 0.5f : level == 2 ? 0.4f : 0.6f) { WindScale = 0.6f, Lethal = true, Shows = Form.Punch | Form.Straight | Form.Fast | Form.Feint });
            if (level == 2)
            {
                m.Strike(new Hit(Blow.Jab, 0.5f) { WindScale = 0.55f, Lethal = true, Shows = Form.Punch | Form.Straight | Form.Fast });
            }
            else if (level >= 3)
            {
                m.Strike(new Hit(Blow.Cross, 0.9f) { WindScale = 0.6f, Hurt = Hurt.SnapBack, Push = 0.5f, Lethal = true, Shows = Form.Punch | Form.Straight });
            }
            m.Do(mm => mm.JumpToRecovery());
            // THE DUCK: under the committed blow (nothing touches it), coiled low, the wheel's afterimage left to take it
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(12f, 1.2f, 0.72f, 0f, 0.02f),
                Ease = Ease.OutCubic,
                Intangible = true,
                Forms = Form.Evasive | Form.Feint | Form.Rising,
                Until = mm => bait.Passed(),
                MaxDur = 0.3f,
                Enter = mm => Duck(mm, bait)
            }).Label("duck");
            // THE BOLO: the wheeling arm comes up from below with all its momentum
            Hit bolo = new Hit(Blow.Uppercut, Bolo(level))
            {
                WindScale = 0.45f,
                ActScale = 1.1f,
                Hurt = Hurt.Launch,
                Push = 0.9f,
                Lift = level <= 1 ? 2.4f : level == 2 ? 2.8f : 3.3f,
                Daze = 0.6f + 0.2f * level,
                Power = 1.5f + 0.15f * level,
                Finisher = true,
                Shows = Form.Punch | Form.Rising | Form.Feint | Form.Counter
            };
            bolo.OnLand = (mm, v, r) => Land(mm, v, bolo, bait);
            m.Strike(bolo).Recover(0.25f);
            return m.Start();
        }

        /// <summary>What the bait caught: who committed, with what, and when.</summary>
        private sealed class Bait
        {
            public Actor By;
            public long ById = -1;
            public Move With;
            public double At;
            public float ScanT, TurnT;
            public int Turns;
            public Vector2 Wheel;

            public void Reset()
            {
                By = null;
                ById = -1;
                With = null;
                ScanT = TurnT = 0f;
                Turns = 0;
            }

            /// <summary>The committed blow has gone by (it whiffed, ended, or turned elsewhere).</summary>
            public bool Passed()
            {
                if (By == null || By.Sid() != ById || !By.isAlive())
                {
                    return true;
                }
                if (With == null)
                {
                    return false;   // a shot: the duck's own length
                }
                if (!Moves.TryGet(By, out Move mv) || mv != With || mv.Finished || mv.Ended || mv.Interruptible)
                {
                    return true;
                }
                Reads.Trace t = Reads.Of(By, false);
                return t != null && t.AnsweredAt >= At;
            }
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>
        /// One frame of the wheel: the arm's circles (a crescent each turn, brighter every time round, the
        /// body bobbing with it), and the watch for a foe that commits to it.
        /// </summary>
        private static void Turn(ChoreoMove m, Bait b, float k)
        {
            float px = m.Px;
            float period = 0.17f * m.WindK;
            b.TurnT -= Clock.Dt;
            float ph = 1f - Mathf.Clamp01(b.TurnT / Mathf.Max(0.01f, period));
            // the body rides the wheel: leaning back as the arm comes up behind, forward as it comes over
            m.Pose.Rot += 5f * Mathf.Sin(Mathf.PI * 2f * ph);
            m.Pose.SY *= 1f + 0.035f * Mathf.Cos(Mathf.PI * 2f * ph);
            b.Wheel = m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.08f, m.Hpx * 0.1f) * px;
            if (b.TurnT <= 0f)
            {
                b.TurnT = period;
                b.Turns++;
                if (m.Vis)
                {
                    float bright = Mathf.Clamp01(0.35f + 0.2f * b.Turns + 0.1f * m.Level);
                    Color core = Color.Lerp(m.Hue.Edge, m.Hue.Core, bright);
                    int r = Mathf.Clamp(Mathf.RoundToInt(6f * m.Kk), 4, 40);
                    // (the bolo's own turn, seen facing right: up the front, over the top, down behind, round
                    // under: the rising blow is this circle let go)
                    Kit.Swish(m, Art.Arc(r, 1f, -100f, 220f, m.Level >= 3 ? 3 : 2), period * 1.15f, new Vector2(-m.Fwd * 0.8f, 1f), m.Fwd, core, m.Hue.Edge, 6);
                    if (m.Level >= 2)
                    {
                        ChoreoFx.Motes(b.Wheel, px, 2 + m.Level, 10f * m.Kk, 0.16f, m.Hue.Core, m.Hue.Edge);
                    }
                    if ((b.Turns & 1) == 0)
                    {
                        ChoreoFx.HeelDust(m.Stance, -m.Dir, px, m.A.current_tile, m.Kk * 0.7f);
                    }
                }
                if ((b.Turns & 1) == 1)
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.1f);
                }
            }
            // the watch: a beat to show the wheel first, then any foe that commits to it is taken under
            if (k < 0.08f)
            {
                return;
            }
            b.ScanT -= Clock.Dt;
            if (b.ScanT > 0f)
            {
                return;
            }
            b.ScanT = 0.03f;
            Shot shot = Shots.Incoming(m.A, 1.6f * m.Kk);
            if (shot != null)
            {
                Bite(m, b, shot.A, null);
                return;
            }
            Choreo.Foes(m.A, m.Stance, 3.6f * m.Kk, _near, false, true);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (!Moves.TryGet(u, out Move mv) || mv.A != u || mv is Puppet || mv.Finished || mv.Ended)
                {
                    continue;
                }
                if (Reads.AimsAt(u, m.A))
                {
                    Bite(m, b, u, mv);
                    return;
                }
            }
        }

        /// <summary>A foe committed: the wheel stops, and it drops under the blow.</summary>
        private static void Bite(ChoreoMove m, Bait b, Actor u, Move with)
        {
            b.By = u;
            b.ById = u != null ? u.Sid() : -1;
            b.With = with;
            b.At = Fighter.Now;
            // the bolo goes to the one that bit (if it is near enough to be hit); a far shooter: the target it had
            if (u != null && u != m.V && Choreo.Fair(m.A, u, out _, true) && Combat.Grounded(u)
                && Vector2.Distance(m.Stance, u.current_position) <= 3.6f * m.Kk)
            {
                m.SetVictim(u);
            }
            if (Dev.On) Dev.Trace("bolo feint: " + (u != null ? u.asset.id + "#" + u.Sid() : "?") + " bites (" + (with != null ? (with.Tech != null ? with.Tech.Id : with.GetType().Name) : "a shot") + ")");
            m.Jump("duck");
        }

        /// <summary>The drop: an afterimage of the wheeling stance left standing to take the blow.</summary>
        private static void Duck(ChoreoMove m, Bait b)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            Fx.Afterimage(m, 0.24f, new Color(1f, 1f, 1f, 0.8f), Fx.WithA(m.Hue.Edge, 0.4f), Fx.WithA(m.Hue.Edge, 0f));
            Fx.LandingPuff(m.Stance, m.Px, m.A.current_tile);
            Fx.Glint(m.HeadPos() + new Vector2(m.Fwd * 2f, 1f) * m.Px, m.Px, 0.14f, m.Hue.Core, 0.9f);
        }

        /// <summary>The bolo lands: a wheel of light rising from below, the foe shot straight up; it was fooled.</summary>
        private static void Land(ChoreoMove m, Actor v, Hit h, Bait b)
        {
            int level = m.Level;
            if (m.Vis)
            {
                int r = Mathf.Clamp(Mathf.RoundToInt((9f + 2f * level) * m.Kk), 6, 60);
                Kit.Swish(m, Art.Arc(r, 1f, -100f, 120f, level >= 3 ? 4 : 3), 0.16f, new Vector2(m.Fwd * 1.5f, -0.5f), m.Fwd, m.Hue.Core, m.Hue.Edge, 7);
                Kit.SpeedLines(m.VPoint(0.5f), Vector2.up, m.Px, 4 + level, 3f, 0.16f, Fx.WithA(m.Hue.Edge, 0.85f));
            }
            Impact im = Impact.Of(Hurt.Launch, m.Dir, h.Push * Mathf.Lerp(1f, m.Kk, 0.5f), h.Power);
            im.Lift = h.Lift;
            im.Daze = h.Daze;
            im.Stop = (0.05f + 0.015f * level) * m.ImpactK;
            IronFist.Rehurt(m, v, h, im, b.With, Form.Strike | Form.Shot | Form.Interrupt | Form.Dash);
            if (b.By != null && b.By == v && Fighters.TryGet(v, out Fighter vf))
            {
                vf.NotesOn(m.A).Fooled++;   // it learned (a patient fighter bites less next time)
            }
        }
    }
}
