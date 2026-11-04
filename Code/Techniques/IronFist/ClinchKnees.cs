using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// CLINCH (Iron Fist, Adept, reactive and planned). The foe comes in throwing a flurry, or loops a
    /// hook: the boxer walks inside it and ties its arms up, hands locked behind its neck (the flurry
    /// smothered: its move breaks), pulls its head down into the plum and drives knees up into its
    /// body, 2 / 3 / 4 of them (two against a light everyday blow), the held body jolting and folding
    /// over each one, then plants both hands on its chest and shoves it off: it skids back, dazed. A
    /// grab: it beats a guard, it needs a body
    /// it can hold (no giants, nothing much bigger than itself, nothing airborne). Also planned on a
    /// foe close by (doubly so mid-flurry, or curled up behind a guard). Damage (x the user's damage):
    /// knees I 0.45, II 0.5, III 0.55 each; the shove I 0.35, II 0.4, III 0.5.
    /// </summary>
    internal sealed class ClinchKneesTech : Technique
    {
        public override string Id => "clinch_knees";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override bool Reactive => true;
        public override School School => School.IronFist;
        public override Form Forms => Form.Grab | Form.Knee | Form.Counter;
        public override Form Answers => Form.Flurry | Form.Hook;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 10f, 16f, 26f };
        public override float[] Cooldown => new[] { 7f, 7f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static int Knees(int level)
        {
            return Mathf.Clamp(level, 1, 3) + 1;
        }

        public static float Knee(int level)
        {
            return level <= 1 ? 0.45f : level == 2 ? 0.5f : 0.55f;
        }

        public static float Shove(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.4f : 0.5f;
        }

        public static float Total(int level)
        {
            return Knees(level) * Knee(level) + Shove(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.6f, Total = Total(1), Crowd = 0.12f, Alone = 6f, IntoGuard = 1.3f, Worth = 10f },
            new MeleeWant { Reach = 1.8f, Total = Total(2), Crowd = 0.1f, Alone = 7f, IntoGuard = 1.3f, Worth = 12f },
            new MeleeWant { Reach = 2f, Total = Total(3), Crowd = 0.08f, Alone = 8f, IntoGuard = 1.3f, Worth = 14f }
        };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!IronFist.InReach(defender, attacker, 1.8f) || !Clinch.Holds(attacker, defender))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return Clinch.Begin(defender, attacker, null, incoming, level, this) ? Contact.Parried : Contact.Hit;
        }

        /// <summary>
        /// A close foe it can hold (a flurrying one doubly so: the Brain answers what it is doing; a
        /// guard is beaten by a grab: the Brain doubles that too). A foe busy with a move of its own is
        /// only taken out of it if that move is a flurry or a hook.
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            Plan p = IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            if (!Clinch.Holds(v, c.Self))
            {
                return null;
            }
            if (Moves.TryGet(v, out Move mv) && mv.A == v && !Choreo.Free(v) && (mv is Puppet || mv.Cinematic || (Reads.FormsOf(mv) & Answers) == 0))
            {
                return null;   // in a set piece of its own: it can't be tied up now
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p != null && p.Target != null && Clinch.Begin(a, p.Target, p, null, p.Level, this);
        }
    }

    /// <summary>The clinch's choreography.</summary>
    internal static class Clinch
    {
        /// <summary>
        /// The three holds of the clinch, for this pair (sprite pixels of the holder: forward, up, the held
        /// body's tilt towards it): locked close and upright, its feet on the ground (arms tied); its head
        /// pulled down into the plum; jolted up and folded over a knee.
        /// </summary>
        private static void Grips(ChoreoMove m, out Grip tied, out Grip plum, out Grip jolted)
        {
            // (how high its middle sits when it stands on its own feet, in the holder's pixels)
            float stand = 0.36f * (m.VHeight - m.Hpx * m.Px) / Mathf.Max(0.01f, m.Px);
            // (a negative tilt bends it over towards the holder)
            tied = new Grip(3f, stand + 0.3f, -6f);
            plum = new Grip(2.7f, stand - 0.2f, -22f);
            jolted = new Grip(3.1f, stand + 1.5f, -34f);
        }

        /// <summary>Can 'by' tie 'v' up: movable, on the ground, not much bigger than itself?</summary>
        public static bool Holds(Actor v, Actor by)
        {
            if (v == null || by == null || !Combat.Grounded(v) || !Victims.Movable(v, by))
            {
                return false;
            }
            // (no taller than itself, give or take: a bigger body would be carried, not tied up)
            return Choreo.SpritePx(v) * Choreo.PxOf(v) <= Choreo.SpritePx(by) * Choreo.PxOf(by) * 1.12f;
        }

        public static bool Begin(Actor a, Actor v, Plan p, Move incoming, int level, Technique t)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, t) : ChoreoMove.Answer(a, v, t, level);
            if (m == null || m.V == null)
            {
                return false;
            }
            level = m.Level;
            Form answers = t.Answers;
            // (against a light everyday blow, a flow strike's hook, the tie-up is a short one: two knees)
            int knees = incoming != null && incoming.Interruptible ? Mathf.Min(2, ClinchKneesTech.Knees(level)) : ClinchKneesTech.Knees(level);
            Grips(m, out Grip tied, out Grip plum, out Grip jolted);
            if (p != null)
            {
                m.StepIn();
            }
            // its flurry (or its hook) is smothered: cut short, so its arms can be tied up
            m.Do(mm => IronFist.CutShort(mm.V, mm.A, incoming, answers, true))
                .Grab(0.1f, tied)
                // the hands lock behind its neck and pull its head down: the plum
                .Lift(plum, 0.1f, new Shape(10f, 1.04f, 0.96f, 0f, 0.02f))
                .OnEnter(mm =>
                {
                    if (mm.Vis && mm.Held != null)
                    {
                        Fx.Glint(mm.HeadPos() + new Vector2(mm.Fwd * 2.5f, 0f) * mm.Px, mm.Px, 0.12f, mm.Hue.Core, 0.7f);
                    }
                });
            for (int i = 0; i < knees; i++)
            {
                bool last = i == knees - 1;
                m.Strike(new Hit(Blow.Knee, ClinchKneesTech.Knee(level))
                {
                    WindScale = i == 0 ? 1f : 0.8f,
                    Power = 1.05f + 0.1f * i,
                    Finisher = last && level >= 3,
                    Shows = Form.Knee | Form.Grab | Form.Body | Form.Counter
                })
                    // the held body jolts up and folds over the knee, then is pulled back down
                    .Lift(jolted, 0.05f, Shape.Keep)
                    .Lift(plum, last ? 0.05f : 0.08f, Shape.Keep);
            }
            // THE SHOVE: both hands on its chest, it is pushed off
            m.PoseTo(0.07f, new Shape(-10f, 0.94f, 1.06f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind)
                .OnlyIf(mm => mm.Held != null)
                .Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.08f,
                    To = new Shape(18f, 1.2f, 0.88f, 0f, 0.18f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = Form.Palm | Form.Grab,
                    If = mm => mm.Held != null
                })
                .At(0.35f, mm => Off(mm, level))
                .Recover(0.22f);
            return m.Start();
        }

        /// <summary>Two hands on its chest: it is let go and skids back, dazed.</summary>
        private static void Off(ChoreoMove m, int level)
        {
            Victim h = m.Held;
            if (h == null)
            {
                return;
            }
            Actor v = h.A;
            m.Held = null;
            HitResult r = Combat.Hit(m.A, v, ClinchKneesTech.Shove(level) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            Impact im = Impact.Of(Hurt.Reel, m.Dir, (1.1f + 0.3f * level) * Mathf.Lerp(1f, m.Kk, 0.5f), 1.2f + 0.1f * level);
            im.Daze = 0.3f + 0.25f * level;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            im.Stop = 0.03f * m.ImpactK;
            h.LetGo(im, m.A, m);
            m.SetLink(Link.Close, v);
            m.HoldFor(0.03f * m.ImpactK);
            Choreo.Sound("event:/SFX/OTHER/Punch", v.current_tile);
            if (m.Vis)
            {
                Vector2 at = m.VPoint(0.55f);
                ChoreoFx.Impact(at, m.Dir, m.Px, m.Kk, 1.1f, m.Hue, m.TopLayer);
                Kit.SpeedLines(at, m.Dir, m.Px, 3, 3f, 0.12f, Fx.WithA(m.Hue.Edge, 0.85f));
            }
        }
    }
}
