using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CROSS COUNTER (Iron Fist, Adept, reactive). The foe's straight comes in (a jab, a cross, a
    /// lunge, a dash): the boxer does not step away from it but INTO it, its head slipping just off
    /// the line so the straight grazes past its ear (a pale streak of the arm going by), and its own
    /// rear cross rides over the top of the foe's arm into its face. The two arms cross: an X of
    /// light at the jaw, both bodies hold the frame, a black-and-white impact frame (a white one at
    /// I), and the foe's own momentum doubles the blow. I: the head snaps back, it slides back on its
    /// heels; II: it is sent skidding back, dazed; III: it is knocked off its feet and flies (Launched).
    /// Plain straight blows of the game (a punch, a thrust) are met too. Damage (x the user's damage):
    /// I 2, II 2.5, III 3.
    /// </summary>
    internal sealed class CrossCounterTech : Technique
    {
        public override string Id => "cross_counter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Straight | Form.Counter;
        public override Form Answers => Form.Punch | Form.Straight | Form.Dash;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 9f, 14f, 20f };
        public override float[] Cooldown => new[] { 7f, 6.5f, 6f };

        public static float Mult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.5f : 3f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            // (a line, a shot or a dash from across the field: nothing to ride over yet)
            if (!IronFist.InReach(defender, attacker, 2f))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return CrossCounter.Begin(defender, attacker, null, incoming, level, this) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A plain straight blow (a punch, a thrust) is met the same way.</summary>
        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            return IronFist.InReach(defender, attacker, 1.8f) && CrossCounter.Begin(defender, attacker, null, null, level, this);
        }

        /// <summary>An answer, never a plan (forced: it is shown on the nearest foe).</summary>
        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 4f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Dir = (v.current_position - c.Pos).normalized, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p != null && p.Target != null && CrossCounter.Begin(a, p.Target, p, null, p.Level, this);
        }
    }

    /// <summary>The cross counter's choreography.</summary>
    internal static class CrossCounter
    {
        public static bool Begin(Actor a, Actor v, Plan p, Move incoming, int level, Technique t)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, t) : ChoreoMove.Answer(a, v, t, level);
            if (m == null || m.V == null)
            {
                return false;
            }
            level = m.Level;
            Form answers = t.Answers;
            float side = Random.value < 0.5f ? 1f : -1f;
            // THE READ: it steps in under the straight, its head slipping just off the line (the foe's arm grazes past)
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm => Inside(mm),
                Chase = true,
                To = new Shape(8f, 1.04f, 0.9f, 0f, 0.03f, side * 0.12f),
                Ease = Ease.OutQuad,
                Intangible = true,
                NeedsVictim = true,
                Forms = Form.Counter | Form.Evasive | Form.Punch,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (!mm.Vis)
                    {
                        return;
                    }
                    // the foe's straight going by: a pale streak from its shoulder past the boxer's ear
                    Vector2 from = mm.VPoint(0.7f);
                    Vector2 ear = mm.HeadPos() + new Vector2(0f, -1f * mm.Px * mm.Kk);
                    Vector2 past = ear + (ear - from).normalized * 4f * mm.Px * mm.Kk;
                    IronFist.Streak(from, past, mm.Px, 0.12f, Color.white);
                    Fx.Afterimage(mm, 0.14f, new Color(1f, 1f, 1f, 0.6f), Fx.WithA(mm.Hue.Edge, 0f));
                }
            });
            // THE CROSS over the top of its arm
            Hit cross = new Hit(Blow.Cross, CrossCounterTech.Mult(level))
            {
                WindScale = 0.45f,
                ActScale = 0.9f,
                Hurt = level >= 3 ? Hurt.Launch : level == 2 ? Hurt.Reel : Hurt.SnapBack,
                Push = level >= 3 ? 2.2f : level == 2 ? 1.5f : 0.7f,
                Lift = level >= 3 ? 1.1f : -1f,
                Daze = level >= 3 ? 1f : level == 2 ? 0.7f : 0.4f,
                Power = level >= 3 ? 2f : level == 2 ? 1.7f : 1.5f,
                // (III draws its own black-and-white frame: no coloured one from the engine on top)
                Finisher = level <= 2,
                Shows = Form.Punch | Form.Straight | Form.Counter
            };
            cross.OnLand = (mm, u, r) => Land(mm, u, cross, incoming, answers);
            m.Strike(cross)
                // the frame holds: both bodies locked where the arms crossed
                .Hold(0.07f, false, Tempo.Impact)
                .Recover(0.22f);
            return m.Start();
        }

        /// <summary>
        /// Where it meets the straight: a blow's length in front of the foe on the line between them, kept
        /// as the foe's lunge carries it in (the read is a lean into it, not a collision).
        /// </summary>
        private static Vector2 Inside(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            float d = Vector2.Distance(m.Stance, m.VG);
            if (Mathf.Abs(d - m.Stand) <= 0.12f * m.Kk)
            {
                return m.Stance;
            }
            return m.StandSpot(m.Stand);
        }

        /// <summary>
        /// The arms cross: an X of light at the jaw, the frame held, black and white (II, III); the foe
        /// takes its own momentum back (its swing cut short), and reels as the level says.
        /// </summary>
        private static void Land(ChoreoMove m, Actor v, Hit h, Move incoming, Form answers)
        {
            int level = m.Level;
            Vector2 at = m.ContactPoint(0.64f);
            if (m.Vis)
            {
                float px = m.Px;
                float len = (5f + 1.5f * level) * px * m.Kk;
                Vector2 d1 = Choreo.Rotate(m.Dir, 38f), d2 = Choreo.Rotate(m.Dir, -38f);
                Kit.Line(at - d1 * len, at + d1 * len, px, 0.14f, 2, m.TopLayer, 16, Color.white, Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.Line(at - d2 * len, at + d2 * len, px, 0.14f, 2, m.TopLayer, 16, Color.white, Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                Palette bw = m.Hue.Pal;
                bw.Edge = Color.white;
                bw.Core = Color.white;
                Kit.ImpactFrame(at, px, (0.22f + 0.08f * level) * m.Kk, bw, level >= 2, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                Fx.FocusLines(at, px, Fx.Count(6 + 2 * level), 9f * m.Kk, 15f * m.Kk, 0.12f);
            }
            m.HoldFor((0.07f + 0.02f * level) * m.ImpactK);
            if (level >= 3 && m.Tier >= Tier.Master)
            {
                Kit.Stop(at, (0.05f + 0.02f * h.Power) * m.ImpactK);
            }
            Impact im = Impact.Of(h.Hurt ?? Hurt.SnapBack, m.Dir, h.Push * Mathf.Lerp(1f, m.Kk, 0.5f), h.Power);
            im.Lift = h.Lift;
            im.Daze = h.Daze;
            im.Stop = (0.08f + 0.02f * level) * m.ImpactK;
            IronFist.Rehurt(m, v, h, im, incoming, answers);
        }
    }
}
