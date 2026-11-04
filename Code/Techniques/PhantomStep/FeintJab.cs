using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// FEINT JAB (Phantom Step, Novice). It steps in and throws the lead hand at the face: the
    /// shoulder pumps, a pale violet ghost of the fist flicks at the head and a ghost of the body
    /// leans in high (readers see a high straight coming), and it drops under its own fake: the
    /// real blow is a hook to the body (it folds). A foe that answered the high jab (a duck, a
    /// slip, a parry...) bites on nothing: it sways out of the answer untouchable, the answer cuts
    /// air and is cut short, and the body shot finds it flat-footed (folded longer). An answer made
    /// for feints (Reversal) sees through it: it is caught mid-fake. A patient foe it fooled before
    /// bites less (Reactions), and it feints a foe like that less.
    /// II: then the cross to the head the fold brought down (it snaps back). III: the fake is a
    /// double flicker, and an uppercut ends it (Launched).
    /// Damage (x the user's damage): I 1.1; II + 0.8; III + 1.1.
    /// </summary>
    internal sealed class FeintJabTech : Technique
    {
        public override string Id => "feint_jab";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Feint | Form.Punch | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 15f };
        public override float[] Cooldown => new[] { 4f, 5f, 6f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float Body = 1.1f, Cross = 0.8f, Upper = 1.1f;

        /// <summary>What readers see while it fakes: a quick straight at the head (and the bait it is).</summary>
        public const Form Fake = Form.Punch | Form.High | Form.Straight | Form.Fast | Form.Feint;

        public static float Total(int level)
        {
            return Body + (level >= 2 ? Cross : 0f) + (level >= 3 ? Upper : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.9f, Total = Total(1), Worthy = false, Crowd = 0.05f, Alone = 4f, IntoGuard = 1.15f, Worth = 6f },
            new MeleeWant { Reach = 2.1f, Total = Total(2), Worthy = false, Crowd = 0.05f, Alone = 5f, IntoGuard = 1.15f, Worth = 8f },
            new MeleeWant { Reach = 2.3f, Total = Total(3), Worthy = false, Crowd = 0.04f, Alone = 6f, IntoGuard = 1.15f, Worth = 10f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            Combat.Allowed(c.Self, v, out float cm);
            if (v.getHealth() <= c.Expected(v, Body * 0.8f) * cm)
            {
                return null;   // no need to fool what a plain blow ends
            }
            // a foe with an answer up is what a feint is for...
            if (c.Guard(v, Form.Punch | Form.High | Form.Straight) > 0)
            {
                p.Score += 8f;
            }
            // ...but a patient one it fooled before won't bite again so easily
            if (Fighters.TryGet(v, out Fighter fv))
            {
                FoeNotes seen = fv.Recall(c.Self);
                if (seen != null && seen.Fooled > 0 && fv.Persona.Patience > 0.5f)
                {
                    p.Score *= 0.7f;
                }
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            PhantomMove m = PhantomMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            m.StepIn();
            // the fake: the shoulder pumps up (stepping after a foe that backs off), the lead hand flicks at the face
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.07f,
                To = new Shape(-7f, 0.93f, 1.09f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.ApproachSpot(),
                PerTile = 0.07f,
                Chase = true,
                NeedsVictim = true,
                Forms = Fake
            });
            m.PoseTo(0.06f, new Shape(9f, 1.05f, 1.06f, 0.03f, 0.07f), Ease.OutExpo, Tempo.Act).Shows(Fake)
                .OnEnter(_ => FakeFist(m, 0))
                .At(0.55f, _ => PhantomKit.Bait(m));
            if (level >= 3)
            {
                // the double fake: the hand snaps back and flicks again
                m.PoseTo(0.05f, new Shape(-4f, 0.97f, 1.05f, 0f, 0f), Ease.OutQuad, Tempo.Act).Shows(Fake);
                m.PoseTo(0.05f, new Shape(11f, 1.06f, 1.05f, 0.03f, 0.08f), Ease.OutExpo, Tempo.Act).Shows(Fake)
                    .OnEnter(_ => FakeFist(m, 1))
                    .At(0.5f, _ => PhantomKit.Bait(m));
            }
            // it bit: a sway out of the answer, which cuts the air where it was
            m.Add(PhantomKit.Sway(0.14f, 1f)).OnlyIf(_ => m.Bit).OnEnter(_ => SwayFx(m));
            m.Do(_ => PhantomKit.Punish(m, 0.4f));
            // the real blow drops under the fake
            bool master = m.Tier >= Tier.Master;
            Hit body = new Hit(Blow.Body, Body) { Hurt = Hurt.Fold, Daze = 0.25f, WindScale = 0.55f, Shows = Form.Punch | Form.Body | Form.Hook | Form.Fast };
            m.Do(_ =>
            {
                if (m.Bit)
                {
                    body.Daze = 0.8f;   // caught flat-footed: it folds longer
                    body.Power = 1.25f;
                }
            });
            m.Strike(body);
            if (level >= 2)
            {
                m.Strike(PhantomKit.Crack(new Hit(Blow.Cross, Cross) { Hurt = Hurt.SnapBack, Push = 0.5f, WindScale = 0.8f, Finisher = level == 2 }, level == 2 && master ? 0.35f : 0f));
            }
            if (level >= 3)
            {
                m.Strike(PhantomKit.Crack(new Hit(Blow.Uppercut, Upper) { Hurt = Hurt.Launch, Push = 1.2f, Lift = 1.8f, Finisher = true }, master ? 0.4f : 0f));
            }
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>The fake's look: a pale violet ghost of the fist flicking at the head, a ghost of the body leaning in high.</summary>
        private static void FakeFist(PhantomMove m, int i)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 from = m.Root(Limb.Fist);
            Vector2 to = m.V != null && m.VOk ? m.ContactPoint(0.9f) : from + m.Dir * m.Stand;
            to = Vector2.Lerp(to, from, 0.1f) + new Vector2(0f, i * 1f * px);
            ChoreoFx.Smear(from, to, px, 0.12f, 1, 1, PhantomKit.Illusion(m, 0.85f), PhantomKit.Illusion(m, 0.35f), FxLayer.Over, 7);
            Fx.Ghost(m, m.Dir * 2f * px + new Vector2(0f, 1f * px), 10f + 4f * i, 0.16f, PhantomKit.Illusion(m, 0.55f), Fx.WithA(m.Hue.Edge, 0f));
            Fx.Glint(to, px, 0.08f, PhantomKit.Illusion(m, 0.95f), 0.6f);
        }

        /// <summary>It slips out of the answer: an afterimage left in the fake's pose takes the swing.</summary>
        private static void SwayFx(PhantomMove m)
        {
            if (!m.Vis)
            {
                return;
            }
            Particle p = Fx.Afterimage(m, 0.22f, PhantomKit.Illusion(m, 0.75f), PhantomKit.Illusion(m, 0.5f), Fx.WithA(m.Hue.Edge, 0f));
            if (p != null)
            {
                p.R.sortingOrder += 2;
            }
            ChoreoFx.HeelDust(m.Stance, -m.Dir, m.Px, m.A.current_tile, m.Kk);
        }
    }
}
