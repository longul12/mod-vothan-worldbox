using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// HEADBUTT (Earth Breaker, Novice). A fist in the foe's collar yanks it in, the head cocks back,
    /// and the forehead comes down on its face: a crack, a burst of little stars, the foe's head
    /// snapping back and stars circling it as it reels. Fast: it breaks a charge (Interrupt), and a
    /// foe busy holding someone (a grab, a throw) is knocked out of it and lets go. On something it
    /// can't take by the collar (a giant, a foe mid wind-up, one held by another move) it simply
    /// lunges in and butts whatever it reaches. II butts twice (the first while still holding on);
    /// III three times, the last one a crushing blow that sends the foe reeling with a long daze.
    /// Damage (x the user's damage): I 1.1; II 0.7 + 1.05; III 0.6 + 0.7 + 1.35.
    /// </summary>
    internal sealed class HeadbuttTech : Technique
    {
        public override string Id => "headbutt";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Head | Form.Fast | Form.Interrupt;

        /// <summary>A wind-up (breaks it) and a grab (knocks the grabber out of it).</summary>
        public override Form Answers => Form.Charge | Form.Grab;

        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 5f, 9f, 14f };
        public override float[] Cooldown => new[] { 4f, 4.5f, 5f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        /// <summary>The butts' damage, first to last.</summary>
        private static readonly float[][] Butts =
        {
            new[] { 1.1f },
            new[] { 0.7f, 1.05f },
            new[] { 0.6f, 0.7f, 1.35f }
        };

        public static float Total(int level)
        {
            float t = 0f;
            foreach (float f in Butts[Mathf.Clamp(level, 1, 3) - 1])
            {
                t += f;
            }
            return t;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2f, Total = Total(1), Worthy = false, Crowd = 0.04f, Alone = 4f, IntoGuard = 0.9f, Worth = 6f },
            new MeleeWant { Reach = 2.2f, Total = Total(2), Worthy = false, Crowd = 0.04f, Alone = 5f, IntoGuard = 0.9f, Worth = 7f },
            new MeleeWant { Reach = 2.4f, Total = Total(3), Worthy = false, Crowd = 0.04f, Alone = 6f, IntoGuard = 0.9f, Worth = 9f }
        };

        /// <summary>The forehead: a short hard blow at head height.</summary>
        private static readonly Form Shows = Form.Head | Form.Fast | Form.Interrupt;

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            MeleeWant w = Want[Mathf.Clamp(level, 1, 3) - 1];
            // the interrupt: a foe gathering power (or holding one of ours) within reach comes first
            Plan urgent = Urgent(c, level, w);
            if (urgent != null)
            {
                return urgent;
            }
            Plan p = Choreo.PickMelee(c, this, level, w);
            if (p == null || c.Forced)
            {
                return p;
            }
            // cheap, but not wasted on something an ordinary blow finishes
            Combat.Allowed(c.Self, p.Target, out float cm);
            if (p.Target.getHealth() <= c.Expected(p.Target, 1.1f) * cm || MeteorSlam.AtDeathsDoor(p.Target))
            {
                return null;
            }
            return p;
        }

        /// <summary>A charging foe (or one holding someone) within reach: the headbutt's real work.</summary>
        private static Plan Urgent(Ctx c, int level, MeleeWant w)
        {
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech != null && c.F.LastTech.Id == "headbutt")
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 6; i++)
            {
                Actor v = c.Enemies[i];
                float reach = w.Reach * k + v.stats["size"] * 0.5f;
                if (c.EnemyDist[i] > reach + 0.3f)
                {
                    break;
                }
                bool charging = Moves.Charging(v);
                bool holding = !charging && Holding(v);
                if (!charging && !holding || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || MeteorSlam.AtDeathsDoor(v))
                {
                    continue;
                }
                if (c.EnemyDist[i] > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
                {
                    continue;
                }
                float score = c.D * 3f + 30f + 10f * Tiers.Power(v) + c.Value(v, c.Expected(v, Total(level)), cm);
                if (holding)
                {
                    score *= 0.8f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        /// <summary>Is 'v' holding or throwing someone in a move of its own?</summary>
        private static bool Holding(Actor v)
        {
            if (!Moves.TryGet(v, out Move m) || m.A != v || m is Puppet || m.Interruptible)
            {
                return false;
            }
            if (m is ChoreoMove cm)
            {
                return cm.Held != null;
            }
            return (Reads.FormsOf(m) & (Form.Grab | Form.Throw)) != 0;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float[] butts = Butts[level - 1];
            m.StepIn();
            // by the collar when it can be taken (free, light enough); a charging foe or a giant is
            // butted where it stands
            if (m.V != null && Choreo.Holdable(m.V, a) && !Moves.Charging(m.V))
            {
                Collar(m, butts);
            }
            else
            {
                // a quick clutch of the arms, then the head
                m.PoseTo(0.05f, new Shape(-6f, 0.95f, 1.05f, 0f, 0.05f), Ease.OutQuad, Tempo.Wind);
                m.Last.Forms = Shows | Form.Grab;
                for (int i = 0; i < butts.Length; i++)
                {
                    bool last = i == butts.Length - 1;
                    m.Strike(new Hit(Blow.Headbutt, butts[i])
                    {
                        Shows = Shows,
                        Hurt = last ? (level >= 3 ? Hurt.Reel : Hurt.SnapBack) : Hurt.Flinch,
                        Push = last ? (level >= 3 ? 1.1f : 0.5f) : 0.08f,
                        Daze = last ? (level >= 3 ? 1.4f : level == 2 ? 0.9f : 0.6f) : 0f,
                        Power = last ? (level >= 3 ? 1.35f : 1.1f) : 0.8f,
                        Finisher = last && level >= 2,
                        WindScale = i == 0 ? 1f : 0.7f,
                        OnLand = last ? (System.Action<ChoreoMove, Actor, HitResult>)Rattled : Knock
                    });
                }
            }
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>
        /// Takes the collar and butts: yanked in close, the head cocked back; each butt snaps forward
        /// (the held head knocked back with it); the last one lets go into the blow.
        /// </summary>
        private static void Collar(ChoreoMove m, float[] butts)
        {
            int level = m.Level;
            m.Grab(0.05f, new Grip(3.2f, 1.6f, -4f));
            for (int i = 0; i < butts.Length; i++)
            {
                bool last = i == butts.Length - 1;
                float mult = butts[i];
                // the head cocks back, the hand drags the foe in
                m.Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = i == 0 ? 0.1f : 0.07f,
                    To = new Shape(-26f, 0.93f, 1.08f, 0f, -0.05f),
                    Ease = Ease.OutQuad,
                    Grip = new Grip(2.7f, 1.4f, 8f),
                    Track = false,
                    Forms = Shows | Form.Grab,
                    If = mm => mm.Held != null
                });
                if (last && level >= 3)
                {
                    m.Hold(0.05f, true, Tempo.Wind);
                    m.Last.If = mm => mm.Held != null;
                }
                // the forehead comes down
                Hit h = new Hit(Blow.Headbutt, mult)
                {
                    Shows = Shows,
                    Hurt = last ? (level >= 3 ? Hurt.Reel : Hurt.SnapBack) : Hurt.Flinch,
                    Push = last ? (level >= 3 ? 1.1f : 0.55f) : 0f,
                    Daze = last ? (level >= 3 ? 1.4f : level == 2 ? 0.9f : 0.6f) : 0f,
                    Power = last ? (level >= 3 ? 1.35f : 1.1f) : 0.8f,
                    Finisher = last && level >= 2
                };
                Beat act = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.06f,
                    To = new Shape(32f, 1.12f, 0.9f, 0f, 0.12f),
                    Ease = Ease.OutExpo,
                    Grip = new Grip(2.3f, 1.4f, last ? -12f : -24f),
                    Track = false,
                    Forms = Shows,
                    Tag = h,
                    If = mm => mm.Held != null
                };
                act.Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.Smear(mm.Root(Limb.Head), mm.Hand(Limb.Head) + mm.Dir * 2f * mm.Px, mm.Px, 0.06f, 1, 1, mm.Hue.Core, mm.Hue.Edge);
                        Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -8f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
                    }
                };
                act.On(0.4f, mm => Butt(mm, h, last));
                m.Add(act);
                // the follow-through: the held head lolls back; the last one's pose is held
                m.Add(new Beat
                {
                    Tempo = Tempo.Follow,
                    Dur = last ? 0.14f : 0.07f,
                    To = new Shape(18f, 1.06f, 0.95f, 0f, 0.08f),
                    Ease = Ease.OutQuad,
                    Grip = new Grip(3f, 1.4f, -26f),
                    Track = false,
                    Forms = Shows
                });
            }
        }

        /// <summary>A butt on the held body: a hit on it where it hangs; the last one lets it go into the blow.</summary>
        private static void Butt(ChoreoMove m, Hit h, bool last)
        {
            if (m.Held == null || !m.VOk)
            {
                return;
            }
            Actor v = m.V;
            Vector2 at = Vector2.Lerp(m.Hand(Limb.Head), Choreo.BodyAt(v, 0.85f), 0.5f);
            if (last)
            {
                m.ReleaseHeld(Hurt.Flinch, 0f);
                HitResult r = m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Headbutt), at, m.Dir);
                Rattled(m, v, r);
                return;
            }
            // (still in hand: the head rocks back on the grip, the hit lands where it hangs)
            HitResult hr = Combat.Hit(m.A, v, h.Mult * m.Cm, false, false, 0f, m);
            m.LastHit = hr;
            m.Held.P.Flash(0.05f);
            m.HoldFor(0.035f * m.ImpactK);
            ChoreoFx.BlowSound(Limb.Head, 0.9f, false, v.current_tile);
            if (m.Vis)
            {
                ChoreoFx.Impact(at, m.Dir, m.Px, m.Kk, 0.85f, m.Hue, m.TopLayer);
                Stars(at, m.Px, m.Hue, 3);
            }
            Knock(m, v, hr);
        }

        /// <summary>The last butt landed: little stars burst from the forehead and circle the reeling head.</summary>
        private static void Rattled(ChoreoMove m, Actor v, HitResult r)
        {
            if (m.Vis)
            {
                Stars(m.VPoint(0.85f), m.Px, m.Hue, m.Level >= 3 ? 6 : 4);
            }
            if (r.Landed && !r.Killed && Combat.Living(v))
            {
                EarthFx.Dizzy(v, m.Level >= 3 ? 1.8f : m.Level == 2 ? 1.2f : 0.8f, m.Hue, m.Level >= 3 ? 3 : 2);
            }
            Knock(m, v, r);
        }

        /// <summary>A foe busy holding (or throwing) someone is knocked out of its move: it lets go.</summary>
        private static void Knock(ChoreoMove m, Actor v, HitResult r)
        {
            if (!r.Landed || v == null || !Combat.Living(v) || !Holding(v))
            {
                return;
            }
            Choreo.Break(v, m.A, v.current_position - m.A.current_position, 1.2f);
        }

        /// <summary>A small burst of little stars from the blow.</summary>
        private static void Stars(Vector2 at, float px, Hue hue, int n)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.3f);
                Particle p = Fx.Spawn(Art.Glint, at, px, Random.Range(0.25f, 0.4f), FxLayer.Top, 12);
                if (p == null)
                {
                    break;
                }
                p.Fps = 14f;
                p.Loop = true;
                p.Sized(0.7f, 0.35f);
                p.Moving(d * Random.Range(35f, 60f) * px, 0f, 5f);
                p.Color(Color.white, Fx.WithA(hue.Core, 0.95f), Fx.WithA(hue.Edge, 0f));
            }
        }
    }
}
