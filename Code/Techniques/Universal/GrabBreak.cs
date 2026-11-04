using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// GRAB BREAK (universal, Expert). Hands close on it (a grab, a clinch, a throw's grip): the
    /// instant they take hold, its arms snap outward and the hold bursts open (a white flash at its
    /// chest, the grip's two arms knocked apart in bright strokes, a ring), it draws both hands back
    /// and drives them into the grabber's chest together: a double palm that shoves it away across
    /// the ground. I sends it skidding; II farther, dazed, with a ring of pressure off the palms;
    /// III blasts it off its feet (Launched) and the shock pushes everyone else close back too. The
    /// strike side of the counter web's triangle: a fast blow breaks a grab (the answer to Grab and
    /// Throw: suplexes, piledrivers, shoulder throws and their counters).
    ///
    /// Beat sheet (seconds at Novice pace; the tier's tempo shortens them):
    ///   BREAK   0.06  the body flares (wide, leaning back), the grip bursts open at its chest
    ///   CHAMBER 0.05  both hands drawn back to the hip, the weight sinks
    ///   PALM    0.07  the double palm: two streaks into the chest; contact at 45% (Combat.Reach)
    ///   FOLLOW  0.10  arms out, held
    ///   RECOVER 0.20  Chain: Close (I, II) or Launched (III)
    /// Damage (x the user's damage): I 1.0, II 1.4, III 2.0 (+ the shock x0.4 on others within 1.2).
    /// </summary>
    internal sealed class GrabBreakTech : Technique
    {
        public override string Id => "grab_break";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override Form Forms => Form.Counter | Form.Palm;
        public override Form Answers => Form.Grab | Form.Throw;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 18f, 26f };
        public override float[] Cooldown => new[] { 10f, 11f, 12f };

        public static float Mult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.4f : 2f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            // (a body thrown into it is no hold to break: only hands closing on it)
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (Vector2.Distance(defender.current_position, attacker.current_position) > UniKit.Reach(defender, attacker, 2.2f))
            {
                return Contact.Hit;   // no hands on it from out there
            }
            Reactions.Free(defender);
            UniKit.Settle(defender);
            return Perform(defender, attacker, null, level) ? Contact.Parried : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            return UniKit.ForcedPlan(c, 6f);
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p != null && p.Target != null && Perform(a, p.Target, p, p.Level);
        }

        private bool Perform(Actor a, Actor v, Plan p, int level)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, this) : ChoreoMove.Answer(a, v, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            if (p != null)
            {
                m.StepIn();   // (forced: it is not being held, so it steps in to show the break)
            }
            // the hold bursts open: arms snapped outward, the body flaring
            Beat brk = new Beat { Tempo = Tempo.Act, Dur = 0.06f, To = new Shape(-10f, 1.26f, 0.9f, 0f, -0.04f), Ease = Ease.OutExpo, Forms = Form.Counter | Form.Palm };
            brk.Enter = mm => Burst(mm, lv);
            m.Add(brk);
            // both hands drawn back to the hip
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.05f, To = new Shape(-16f, 0.9f, 1.06f, 0f, -0.1f), Ease = Ease.OutQuad, Forms = Form.Counter | Form.Palm });
            Hit hit = new Hit(Blow.Palm, Mult(lv))
            {
                Hurt = lv >= 3 ? Hurt.Launch : Hurt.Reel,
                Push = lv <= 1 ? 1.7f : lv == 2 ? 2.3f : 3f,
                Lift = lv >= 3 ? 1.1f : -1f,
                Daze = lv <= 1 ? 0f : lv == 2 ? 0.5f : 0.8f,
                Power = lv <= 1 ? 0.9f : lv == 2 ? 1.05f : 1.15f,
                Finisher = lv >= 2,
                Area = lv >= 3 ? 1.2f : 0f,
                AreaMult = 0.2f
            };
            Beat palm = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(18f, 1.28f, 0.88f, 0f, 0.2f), Ease = Ease.OutExpo, Forms = Form.Palm | Form.Straight | Form.Counter };
            palm.Enter = mm => Palms(mm, lv);
            palm.On(0.45f, mm =>
            {
                if (UniKit.Land(mm, hit, ChoreoMove.ShapeOf(Blow.Palm), 0.55f, 0.8f))
                {
                    Pressure(mm, lv);
                }
            });
            m.Add(palm);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.1f, To = new Shape(10f, 1.12f, 0.94f, 0f, 0.14f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>The hold bursts open: a flash at its chest, the grip's two arms knocked apart in bright strokes, a ring.</summary>
        private static void Burst(ChoreoMove mm, int lv)
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
            Choreo.Sound("event:/SFX/OTHER/Punch", mm.A.current_tile, 0.05f);
            if (mm.V != null && mm.V.Sid() == mm.VId && Combat.Living(mm.V))
            {
                Victims.Flash(mm.V, Color.white, 0.05f);
            }
            mm.HoldFor(0.03f * mm.ImpactK);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px, k = mm.Kk;
            Vector2 chest = mm.BodyPos() + new Vector2(mm.Fwd * 1.5f, 0.5f) * px;
            Kit.Flash(chest, px, Mathf.RoundToInt(4f * k), 0.07f, Fx.WithA(Color.white, 0.95f), FxLayer.Top, 26);
            Fx.Ring(chest, px, 0.18f, 2f, 11f * k, 2, 1, FxLayer.Top, 25, 1f, 2.4f, Fx.WithA(mm.Hue.Core, 0.95f), Fx.WithA(mm.Hue.Edge, 0.5f), Fx.WithA(mm.Hue.Edge, 0f));
            // the grip's two arms knocked apart, up and down off the chest
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 d = new Vector2(mm.Fwd * 0.55f, s * 0.85f).normalized;
                Kit.Line(chest + d * 1.5f * px, chest + d * (6.5f * k) * px, px, 0.1f, 2, FxLayer.Top, 27, Color.white, Fx.WithA(mm.Hue.Edge, 0.7f), Fx.WithA(mm.Hue.Edge, 0f));
            }
            Kit.Sparks(chest, px, 4 + lv, 50f, 110f, mm.Hue.Edge);
            if (lv >= 2)
            {
                Fx.FocusLines(chest, px, Fx.Count(5), 7f * k, 12f * k, 0.08f);
            }
        }

        /// <summary>The double palm: two streaks from the hips into the grabber's chest.</summary>
        private static void Palms(ChoreoMove mm, int lv)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            Vector2 from = mm.Root(Limb.Palm);
            Vector2 to = mm.V != null && mm.VOk ? Vector2.Lerp(mm.ContactPoint(0.55f), from, 0.12f) : from + mm.Dir * mm.Stand;
            float life = 0.1f;
            int head = lv >= 3 ? 3 : 2;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 off = new Vector2(0f, s * 1.3f * px);
                ChoreoFx.Smear(from + off, to + off, px, life, head, 2, mm.Hue.Core, mm.Hue.Edge, FxLayer.Over, 7);
            }
            Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), mm.Dir, px, 2, 2.5f, 0.08f, Fx.WithA(mm.Hue.Edge, 0.75f), FxLayer.Over);
            Fx.Ghost(mm, -mm.Dir * 2f * px, -6f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
        }

        /// <summary>II: a ring of pressure off the palms; III: and a shock ring along the ground under the grabber.</summary>
        private static void Pressure(ChoreoMove mm, int lv)
        {
            if (lv < 2 || !mm.Vis)
            {
                return;
            }
            float px = mm.Px, k = mm.Kk;
            Vector2 at = mm.Hand(Limb.Palm) + mm.Dir * 2f * px;
            Fx.Ring(at, px, 0.2f, 3f, (lv >= 3 ? 18f : 13f) * k, 2, 1, mm.TopLayer, 6, 0.6f, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(mm.Hue.Edge, 0.55f), Fx.WithA(mm.Hue.Edge, 0f));
            Kit.SpeedLines(at, mm.Dir, px, 4, 4f * k, 0.12f, Fx.WithA(mm.Hue.Edge, 0.8f));
            if (lv >= 3)
            {
                Kit.Shockwave(mm.VG, px, 3f, 20f * k, 0.32f, Fx.WithA(mm.Hue.Edge, 0.85f));
                Kit.DustRing(mm.VG, px, Combat.TileAt(mm.VG), 8, 34f, 0.45f, k);
            }
        }
    }
}
