using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SWALLOW RETURN (Thousand Blades, Expert). The swallow's turn in flight. The blade rises high and
    /// falls in a big overhead cut, plain to read: the foe sees it coming (Overhead | Feint) and may
    /// answer it - slip it, parry it, counter it. But the blade never stops: at the bottom of its arc it
    /// turns like a swallow (a glint where it turns) and comes straight back up from below, in the same
    /// breath. That second cut cannot be read: whatever answered the first is caught in its own answer
    /// (the slip, the parry, the counter cut short) and the rising cut throws the foe into the air
    /// (Launched).
    ///   I    the fall and the return (x0.6 + x1.4; x1.9 on a foe caught answering)
    ///   II   a faster turn, a higher launch, an impact frame on a caught foe
    ///   III  the turn is almost one motion; the two cuts leave a V of light on the foe; the return
    ///        launches it highest, and a crescent rises after it
    /// A feint: patient fighters who were fooled before bite less (Dynamics). Chains from Close.
    /// </summary>
    internal sealed class SwallowReturnTech : Technique
    {
        public override string Id => "swallow_return";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Feint | Form.Overhead | Form.Rising;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 28f, 45f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float First = 0.6f;

        private static float Second(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.3f;
        }

        /// <summary>The return on a foe caught in its own answer cuts deeper.</summary>
        private const float Caught = 1.35f;

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.1f, Total = First + Second(1), Worthy = true, Crowd = 0.06f, Alone = 8f, IntoGuard = 1.25f, Worth = 14f },
            new MeleeWant { Reach = 2.3f, Total = First + Second(2), Worthy = true, Crowd = 0.05f, Alone = 10f, IntoGuard = 1.25f, Worth = 18f },
            new MeleeWant { Reach = 2.5f, Total = First + Second(3), Worthy = true, Crowd = 0.04f, Alone = 12f, IntoGuard = 1.25f, Worth = 24f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // a foe with an answer ready for a blow from above is exactly what the swallow is for
            if (c.Guard(p.Target, Form.Overhead | Form.Blade) > 0)
            {
                p.Score += 16f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            bool baited = false;
            Form fall = Form.Blade | Form.Overhead | Form.Feint;
            Form rise = Form.Blade | Form.Rising;
            BlowShape cleave = ChoreoMove.ShapeOf(Blow.Cleave);
            BlowShape rising = ChoreoMove.ShapeOf(Blow.RisingSlash);
            m.StepIn();
            // RAISE: the blade goes up high (plain to read), a glint at its point
            Beat raise = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = lv <= 1 ? 0.15f : 0.13f,
                To = cleave.Wind,
                Ease = Ease.OutQuad,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.ApproachSpot(),
                PerTile = 0.07f,
                Chase = true,
                NeedsVictim = true,
                Forms = fall
            };
            raise.Enter = mm => ((TblMove)mm).HoldBlade(112f);
            raise.On(0.7f, mm =>
            {
                if (mm.Vis)
                {
                    TblFx.Ting(((TblMove)mm).BladeTip, mm.Px, mm.Hue, 0.9f);
                }
            });
            m.Add(raise);
            // FALL: the big overhead cut; the foe may answer it
            Beat cut1 = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = cleave.Hit,
                Ease = Ease.OutExpo,
                Forms = fall
            };
            cut1.Enter = mm =>
            {
                if (mm.V != null && mm.VOk)
                {
                    Combat.Pin(mm.V, mm.BeatDur + 0.3f);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(10f * Mathf.Lerp(1f, mm.Kk, 0.6f)), 5, 50);
                    Kit.Swish(mm, Art.Arc(r, 1f, 100f, -80f, 3), 0.14f, new Vector2(mm.Fwd * 2f, 2f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                }
            };
            cut1.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(112f, -80f, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k / 0.55f))));
            cut1.On(0.55f, mm => baited = Fall((TblMove)mm, lv));
            m.Add(cut1);
            // TURN: at the bottom of its arc the blade turns like a swallow (a glint, a hair's dip)
            Beat turn = new Beat
            {
                Tempo = Tempo.Act,
                Dur = lv <= 1 ? 0.06f : lv == 2 ? 0.045f : 0.03f,
                To = new Shape(-4f, 1.16f, 0.82f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = rise
            };
            turn.Enter = mm =>
            {
                TblMove t = (TblMove)mm;
                t.SwingBlade(-96f);
                if (mm.Vis)
                {
                    Fx.Glint(t.BladeTip, mm.Px, 0.12f, Color.white, 0.9f);
                    Kit.Sparks(t.BladeTip, mm.Px, 2, 30f, 60f, mm.Hue.Edge);
                }
            };
            m.Add(turn);
            // RETURN: straight back up from below, unreadable
            Beat cut2 = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = rising.Hit,
                Ease = Ease.OutExpo,
                Forms = rise
            };
            cut2.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.01f);
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(11f * Mathf.Lerp(1f, mm.Kk, 0.6f)), 5, 50);
                    Kit.Swish(mm, Art.Arc(r, 1f, -80f, 110f, 3), 0.14f, new Vector2(mm.Fwd * 1f, -1f), mm.Fwd, Color.white, mm.Hue.Edge, 7);
                }
            };
            cut2.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(-96f, 110f, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k / 0.45f))));
            cut2.On(0.45f, mm => Return((TblMove)mm, lv, baited));
            m.Add(cut2);
            // FOLLOW-THROUGH: the blade held high where the swallow flew off
            m.Posture(lv >= 3 ? 0.16f : 0.13f, new Shape(-10f, 0.92f, 1.1f, 0f, 0.08f), Tempo.Follow, 104f, rise);
            m.Last.Track = false;
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>
        /// The overhead cut arrives. The foe may read it (Combat.Reach): if it answers, its answer is
        /// already playing and the move does NOT stop (that is the bait). True if it bit.
        /// </summary>
        private static bool Fall(TblMove m, int lv)
        {
            if (!m.VOk && !m.Retarget())
            {
                m.Lose();
                return false;
            }
            Actor v = m.V;
            float reach = m.Stand + 0.6f * m.Kk + v.stats["size"] * 0.5f + 0.3f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                return false;   // it cut the air; the return still comes
            }
            Contact c = Combat.Reach(m, v, false);
            if (c == Contact.Hit)
            {
                Hit h = new Hit(Blow.Cleave, First) { Hurt = Hurt.Flinch, Push = 0.1f, Lethal = false, Shows = Form.Blade | Form.Overhead | Form.Feint };
                m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Cleave), m.ContactPoint(0.62f), m.Dir);
                return false;
            }
            // it bit: its answer is under way, and the blade is already turning
            if (Fighters.TryGet(v, out Fighter f))
            {
                FoeNotes n = f.NotesOn(m.A);
                if (n != null)
                {
                    n.Fooled++;
                }
            }
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", m.A.current_tile, 0.05f);
            if (m.Vis)
            {
                TblFx.Ting(m.BladeTip, m.Px, m.Hue, 0.8f);
            }
            if (Dev.On) Dev.Trace("swallow_return: " + v.asset.id + "#" + v.Sid() + " answered the first cut (" + c + "): the return comes");
            return true;
        }

        /// <summary>The return: it cannot be read. A foe caught in its own answer is cut out of it and thrown into the air.</summary>
        private static void Return(TblMove m, int lv, bool baited)
        {
            if (!m.VOk && !m.Retarget())
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            if (Moves.Intangible(v))
            {
                return;   // it really was gone (a blink): the blade cuts its afterimage
            }
            float k = m.Kk;
            float reach = m.Stand + 0.6f * k + v.stats["size"] * 0.5f + 0.3f;
            float dist = Vector2.Distance(m.Pose.Ground, m.VG);
            if (dist > reach && dist <= reach + 1.3f * k)
            {
                // it slipped aside: the return follows it (a blur of a step)
                Vector2 to = m.StandSpot(m.Stand);
                if (Combat.SafeSpot(to) && Combat.ClearPath(m.Stance, to))
                {
                    if (m.Vis)
                    {
                        Fx.Afterimage(m, 0.12f, Fx.WithA(m.Hue.Ghost, 0.7f), m.Hue.GhostEnd);
                        Kit.SpeedLines(m.BodyPos(), to - m.Stance, m.Px, 3, 3f, 0.1f, Fx.WithA(m.Hue.Edge, 0.8f));
                    }
                    m.Stance = to;
                    m.Pose.Ground = to;
                    dist = Vector2.Distance(m.Pose.Ground, m.VG);
                }
            }
            if (dist > reach)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                return;
            }
            // whatever answered the first cut is cut short by the second
            if (Moves.TryGet(v, out Move mv) && mv.A == v && !(mv is Puppet) && !mv.Ended && !Victims.Ours(v))
            {
                Choreo.Kill(mv);
            }
            Hit h = new Hit(Blow.RisingSlash, Second(lv) * (baited ? Caught : 1f))
            {
                Hurt = Hurt.Launch,
                Lift = lv <= 1 ? 1.6f : lv == 2 ? 2.1f : 2.6f,
                Push = 0.7f,
                Power = baited ? 1.35f : 1.15f,
                Daze = baited ? 0.5f : 0f,
                Finisher = lv >= 3 || baited && lv >= 2,
                Shows = Form.Blade | Form.Rising
            };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.RisingSlash), m.ContactPoint(0.5f), m.Dir);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            TblFx.CutOn(v, 0.5f, TblFx.Mirror(60f, m.Fwd), 5f + lv, px, 0.05f, 0.16f, m.Hue, m.TopLayer);
            if (lv >= 3)
            {
                // the swallow's tail: the two cuts as a V of light on the foe, a crescent rising after it
                TblFx.CutOn(v, 0.5f, TblFx.Mirror(-70f, m.Fwd), 5f + lv, px, 0.05f, 0.18f, m.Hue, m.TopLayer);
                TblFx.Moon(m.VPoint(0.5f), px, m.Kk, m.Fwd, -80f, 110f, m.Hue, m.TopLayer, new Vector2(m.Fwd * 8f, 40f));
            }
            if (baited)
            {
                Fx.FocusLines(m.VPoint(0.5f), px, Fx.Count(6), 8f * m.Kk, 13f * m.Kk, 0.1f);
            }
        }
    }
}
