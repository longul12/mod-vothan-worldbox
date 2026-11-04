using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// PARRY (Thousand Blades, Adept). The blade meets the blow. As a fist, a foot or a sword comes in,
    /// the swordsman's blade snaps across into its path: steel rings on it in a ring of sparks and a
    /// flash, the blow rebounds and the attacker's arm bounces off (its move reels back: Parried), the
    /// swordsman gives a hair, braced behind the blade, and before the attacker has its balance back
    /// the riposte is in.
    ///   I    the parry, one quick cut (it snaps the foe's head back)
    ///   II   a bigger ring of sparks; a cut and then a thrust that drives the foe back
    ///   III  the clash blazes (a burst of light, a held beat); a cut and a thrust on an impact frame
    ///        that sends the foe reeling, dazed
    /// A flying blade (a sword wave, a disc) is beaten aside instead: it spins off harmlessly in sparks.
    /// Reactive (Answers Strike | Blade): limbs and blades, never an area blow. It turns plain blows of
    /// the game aside too (the attacker staggers). Shows Guard while it plays (grabs beat it).
    /// Riposte damage: I 1.0; II 0.7 + 0.9; III 0.8 + 1.3.
    /// </summary>
    internal sealed class BladeParryTech : Technique
    {
        public override string Id => "blade_parry";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Guard | Form.Counter;
        public override Form Answers => Form.Strike | Form.Blade;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 6f, 5f, 4f };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Shot shot = Shots.Reaching;
            if (shot != null)
            {
                // a flying blade: beaten aside
                if (shot.Done || !shot.Hostile)
                {
                    return Contact.Hit;
                }
                Reactions.Free(defender);
                return BeatAside(defender, attacker, shot, level) ? Contact.Parried : Contact.Hit;
            }
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.6f + attacker.stats["size"] * 0.5f)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return Parry(defender, attacker, level, null) ? Contact.Parried : Contact.Hit;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            if (!Parry(defender, attacker, level, null))
            {
                return false;
            }
            if (!Moves.Busy(attacker))
            {
                Combat.Stagger(attacker, 0.3f + 0.1f * level);   // its arm bounces off the blade
            }
            return true;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 3f)
            {
                return null;
            }
            Vector2 d = v.current_position - c.Pos;
            return new Plan { Target = v, Point = v.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && Parry(a, p.Target, p.Level, p);
        }

        /// <summary>The parry and the riposte ('forced': a demonstration on a foe that threw nothing).</summary>
        private bool Parry(Actor d, Actor atk, int level, Plan forced)
        {
            TblMove m = forced != null ? TblMove.Make(d, forced, this) : TblMove.Counter(d, atk, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Blade | Form.Guard | Form.Counter;
            if (forced != null)
            {
                m.StepIn();
            }
            // MEET: the blade snaps across into the blow's path; steel rings in a ring of sparks
            Beat meet = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.05f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.12f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                To = new Shape(-8f, 0.92f, 1.08f, 0f, -0.02f),
                Ease = Ease.OutExpo,
                Forms = forms
            };
            meet.Enter = mm =>
            {
                TblMove t = (TblMove)mm;
                t.SwingBlade(64f);
                Clash(t, lv);
            };
            m.Add(meet);
            // BRACED: it gives a hair behind the blade while the blow rebounds
            m.Posture(lv >= 3 ? 0.09f : 0.06f, new Shape(-12f, 0.94f, 1.06f, 0f, -0.05f), Tempo.Follow, 72f, forms);
            m.Last.Tremble = true;
            // THE RIPOSTE
            Form riposte = Form.Blade | Form.Counter;
            if (lv <= 1)
            {
                m.Cut(new Hit(Blow.Slash, 1f) { Hurt = Hurt.SnapBack, Push = 0.45f, WindScale = 0.5f, Shows = riposte });
            }
            else
            {
                m.Cut(new Hit(Blow.Slash, lv == 2 ? 0.7f : 0.8f) { Hurt = Hurt.Flinch, Push = 0.12f, WindScale = 0.5f, ActScale = 0.85f, Lethal = false, Shows = riposte });
                m.Cut(new Hit(Blow.Thrust, lv == 2 ? 0.9f : 1.3f)
                {
                    Hurt = Hurt.Reel,
                    Push = lv == 2 ? 1f : 1.4f,
                    Daze = lv >= 3 ? 0.5f : 0f,
                    Power = lv >= 3 ? 1.25f : 1.05f,
                    Finisher = lv >= 3,
                    WindScale = 0.6f,
                    Shows = riposte | Form.Straight
                });
            }
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>Steel on the blow: sparks thrown all round, a flash, the ting; III blazes and holds a beat.</summary>
        private static void Clash(TblMove m, int lv)
        {
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", m.A.current_tile);
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.08f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 at = Vector2.Lerp(m.BodyPos() + m.Dir * 2f * px, m.VPoint(0.62f), 0.45f);
            TblFx.SparkRing(at, px, 7 + 3 * lv, 70f + 15f * lv, m.Hue, m.Kk, 6f + 2f * lv);
            Fx.Glint(at, px, 0.18f, Color.white, 0.8f + 0.2f * lv);
            Kit.Flash(at, px, 2 + lv, 0.06f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer, 22);
            if (lv >= 3)
            {
                Kit.Flare(at, px, m.Hue.Edge, 12f * m.Kk, 0.25f);
                m.HoldFor(0.06f);
            }
        }

        /// <summary>A flying blade beaten aside: it spins off harmlessly in sparks, the blade swept through it.</summary>
        private bool BeatAside(Actor d, Actor atk, Shot shot, int level)
        {
            TblMove m = TblMove.Facing(d, shot.Pos - shot.Dir, this, level);
            if (m == null)
            {
                return false;
            }
            Vector2 at = shot.Pos + new Vector2(0f, shot.Z);
            Beat meet = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.06f,
                To = new Shape(-10f, 0.92f, 1.08f, 0f, -0.03f),
                Ease = Ease.OutExpo,
                Forms = Form.Blade | Form.Guard | Form.Counter
            };
            meet.Enter = mm =>
            {
                ((TblMove)mm).SwingBlade(70f);
                Choreo.Sound("event:/SFX/HIT/HitSwordSword", mm.A.current_tile);
                if (mm.Vis)
                {
                    TblFx.SparkRing(at, mm.Px, 8 + 2 * mm.Level, 80f, mm.Hue, mm.Kk, 7f);
                    Fx.Glint(at, mm.Px, 0.16f, Color.white, 1f);
                }
            };
            meet.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(70f, -30f, Beat.Apply(Ease.OutExpo, k)));
            m.Add(meet);
            m.Posture(0.1f, new Shape(8f, 1.08f, 0.94f, 0f, 0.02f), Tempo.Follow, -30f, Form.Blade | Form.Guard | Form.Counter);
            m.Last.Track = false;
            m.Recover(0.18f);
            if (!m.Start())
            {
                return false;
            }
            Vector2 side = Choreo.Perp(shot.Dir) * (Random.value < 0.5f ? 1f : -1f);
            shot.Deflect(side * 0.8f - shot.Dir * 0.5f + Vector2.up * 0.3f);
            return true;
        }
    }
}
