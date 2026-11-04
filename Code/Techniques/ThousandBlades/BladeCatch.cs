using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// BLADE CATCH (Thousand Blades, Expert). Empty hands against steel. A blade comes down at it and
    /// the swordsman claps it between its palms: a flash where the palms meet on the flat of the blade,
    /// a ting, the two of them locked a heartbeat, trembling. Then it wrenches the blade aside (the
    /// attacker is twisted round with it) and drives the pommel (an unarmed one: the heel of its palm)
    /// into its face. A missile (a sword wave, a ki shot, a disc: the mod's own shots) is snatched out of
    /// the air instead, held a beat in the hand... and thrown straight back at whoever loosed it, harder.
    ///   I    the catch, the wrench, the pommel (the foe reels back); a missile thrown back x1.2
    ///   II   a harder wrench, the foe reels further, dazed; thrown back x1.35
    ///   III  the clap blazes; the pommel lands on an impact frame and launches the foe; thrown back x1.5
    /// Reactive (Answers Blade | Projectile). It meets plain sword swings of the game too. Forced (dev,
    /// god power) it catches the nearest foe's blow. Damage: wrench 0.3; pommel I 1.2, II 1.5, III 1.9.
    /// </summary>
    internal sealed class BladeCatchTech : Technique
    {
        public override string Id => "blade_catch";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Guard | Form.Counter;
        public override Form Answers => Form.Blade | Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 10f, 16f, 24f };
        public override float[] Cooldown => new[] { 8f, 7f, 6f };

        private static float Pommel(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.9f;
        }

        /// <summary>The wrench: a twist of the foe's own arm, no edge.</summary>
        private static readonly BlowShape Wrench = new BlowShape
        {
            Limb = Limb.Palm,
            Weight = 0.8f,
            Hurt = Hurt.Spin,
            Push = 0.35f,
            Contact = 0.62f,
            Forms = Form.Guard | Form.Counter | Form.Throw
        };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Shot shot = Shots.Reaching;
            if (shot != null)
            {
                if (shot.Done || !shot.Hostile)
                {
                    return Contact.Hit;
                }
                Reactions.Free(defender);
                return Snatch(defender, attacker, shot, level) ? Contact.Parried : Contact.Hit;
            }
            // in the hand only a blade is caught (and a body that came close)
            if ((Reads.FormsOf(incoming) & Form.Blade) == 0
                || Vector2.Distance(defender.current_position, attacker.current_position) > 2.4f + attacker.stats["size"] * 0.5f)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return Catch(defender, attacker, level, null) ? Contact.Parried : Contact.Hit;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if ((forms & Form.Blade) == 0 || Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            return Catch(defender, attacker, level, null);
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
            return p.Target != null && Catch(a, p.Target, p.Level, p);
        }

        /// <summary>Clapped between the palms, wrenched aside, the pommel in the face.</summary>
        private bool Catch(Actor d, Actor atk, int level, Plan forced)
        {
            TblMove m = forced != null ? TblMove.Make(d, forced, this) : TblMove.Counter(d, atk, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Guard | Form.Counter;
            m.Drawn = false;   // bare palms
            if (forced != null)
            {
                m.StepIn();
            }
            // CLAP: both palms meet on the flat of the blade
            Beat clap = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.05f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.08f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                To = new Shape(-6f, 0.9f, 1.12f, 0f, 0.03f),
                Ease = Ease.OutExpo,
                Forms = forms
            };
            clap.Enter = mm => Clap(mm, lv);
            m.Add(clap);
            // LOCKED: the blade held fast, both of them trembling
            m.Posture(lv <= 1 ? 0.1f : lv == 2 ? 0.08f : 0.07f, new Shape(-9f, 0.92f, 1.09f, 0f, 0.02f), Tempo.Follow, float.NaN, forms);
            m.Last.Tremble = true;
            m.At(0.5f, mm =>
            {
                if (mm.Vis)
                {
                    Kit.Sparks(Grip(mm), mm.Px, 2, 20f, 50f, mm.Hue.Edge);
                }
            });
            // WRENCH: twisted aside; the foe goes round with its arm
            Beat wrench = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(-26f, 0.9f, 1.04f, 0f, 0f),
                Ease = Ease.OutExpo,
                NeedsVictim = true,
                Forms = forms | Form.Throw
            };
            wrench.On(0.4f, mm =>
            {
                if (!mm.VOk)
                {
                    mm.Lose();
                    return;
                }
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.Swish(mm, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(7f * mm.Kk), 4, 40), 0.7f, 60f, -60f, 2), 0.1f, new Vector2(mm.Fwd * 3f, 1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                }
                if (mm.Touch(mm.V))
                {
                    Hit h = new Hit(Blow.Palm, 0.3f) { Hurt = Hurt.Spin, Push = lv <= 1 ? 0.35f : 0.5f, Lethal = false, Shows = forms | Form.Throw };
                    mm.Deliver(mm.V, h, Wrench, mm.ContactPoint(0.62f), Choreo.Perp(mm.Dir) * (mm.Fwd > 0f ? -1f : 1f));
                }
            });
            m.Add(wrench);
            // POMMEL: driven into its face
            m.Cut(new Hit(Blow.Palm, Pommel(lv))
            {
                Hurt = lv >= 3 ? Hurt.Launch : Hurt.Reel,
                Push = lv <= 1 ? 1f : lv == 2 ? 1.3f : 1.8f,
                Lift = lv >= 3 ? 1.2f : -1f,
                Daze = lv <= 1 ? 0.2f : lv == 2 ? 0.45f : 0.6f,
                Power = lv <= 1 ? 1.05f : lv == 2 ? 1.15f : 1.3f,
                Finisher = lv >= 2,
                WindScale = 0.55f,
                Shows = Form.Counter | Form.Straight,
                OnLand = (mm, v, r) =>
                {
                    if (mm.Vis)
                    {
                        TblFx.Ting(mm.Hand(Limb.Palm), mm.Px, mm.Hue, 0.8f);
                    }
                }
            });
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>Where the palms hold the blade: between the two of them, at shoulder height.</summary>
        private static Vector2 Grip(ChoreoMove m)
        {
            Vector2 mine = m.BodyPos() + new Vector2(m.Fwd * m.Hpx * 0.32f, m.Hpx * 0.18f) * m.Px;
            return m.V != null && m.V.Sid() == m.VId ? Vector2.Lerp(mine, m.VPoint(0.7f), 0.3f) : mine;
        }

        /// <summary>The palms meet on the blade: a flash, the flat of the blade glinting between them, a ting.</summary>
        private static void Clap(ChoreoMove m, int lv)
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", m.A.current_tile, 0.08f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 at = Grip(m);
            Kit.Flash(at, px, 2 + lv, 0.07f, Fx.WithA(Color.white, 0.95f), m.TopLayer, 22);
            Fx.Glint(at + new Vector2(0f, 1.5f * px), px, 0.2f, Color.white, 0.9f + 0.15f * lv);
            // the flat of the caught blade: a short steel bar standing between the palms
            TblFx.CutLine(at, TblFx.Mirror(78f, m.Fwd), (3f + lv) * px * m.Kk, px, 0f, 0.22f, m.Hue, m.TopLayer);
            Kit.Sparks(at, px, 3 + lv, 30f, 70f, m.Hue.Edge, m.TopLayer);
            if (lv >= 3)
            {
                Kit.Flare(at, px, m.Hue.Edge, 11f * m.Kk, 0.22f);
                m.HoldFor(0.05f);
            }
        }

        /// <summary>A missile snatched out of the air, held a beat, and thrown back at its thrower.</summary>
        private bool Snatch(Actor d, Actor atk, Shot shot, int level)
        {
            TblMove m = TblMove.Facing(d, shot.Pos - shot.Dir, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Guard | Form.Counter;
            float hold = lv <= 1 ? 0.12f : lv == 2 ? 0.1f : 0.08f;
            m.Drawn = false;
            Vector2 at = shot.Pos + new Vector2(0f, shot.Z);
            // SNATCH: a hand flicks up and closes on it, a little hop back with the catch
            Beat snatch = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.06f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.12f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                To = new Shape(-12f, 0.92f, 1.1f, 0f, -0.02f),
                Ease = Ease.OutExpo,
                Forms = forms
            };
            snatch.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.Flash(at, mm.Px, 2 + lv, 0.06f, Fx.WithA(Color.white, 0.95f), mm.TopLayer, 22);
                    Fx.Glint(at, mm.Px, 0.18f, Color.white, 1f);
                    Kit.Sparks(at, mm.Px, 3, 30f, 60f, mm.Hue.Edge, mm.TopLayer);
                }
            };
            m.Add(snatch);
            // HELD: the missile still in its hand, humming
            m.Posture(hold, new Shape(-16f, 0.94f, 1.06f, 0f, -0.08f), Tempo.Free, float.NaN, forms);
            m.Last.Tremble = true;
            // THROWN BACK
            Beat throwBack = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(22f, 1.2f, 0.88f, 0f, 0.12f),
                Ease = Ease.OutExpo,
                Forms = Form.Projectile | Form.Counter
            };
            throwBack.Enter = mm =>
            {
                if (shot.Done)
                {
                    return;
                }
                shot.Age = Mathf.Max(shot.Age, 0f);
                shot.Reflect(mm.A, lv <= 1 ? 1.2f : lv == 2 ? 1.35f : 1.5f);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Vector2 hand = mm.Hand(Limb.Palm);
                    Kit.SpeedLines(hand, mm.Dir, mm.Px, 3, 3f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.85f), FxLayer.Over);
                    Kit.Flash(hand, mm.Px, 2, 0.05f, Fx.WithA(mm.Hue.Core, 0.9f), mm.TopLayer, 22);
                }
            };
            m.Add(throwBack);
            m.Posture(0.1f, new Shape(14f, 1.1f, 0.92f, 0f, 0.1f), Tempo.Follow, float.NaN, forms);
            m.Last.Track = false;
            m.Recover(0.2f);
            if (!m.Start())
            {
                return false;
            }
            // caught: it hangs in the hand, harmless, until it is thrown
            shot.Hostile = false;
            shot.Age = -(hold + 0.25f);
            return true;
        }
    }
}
