using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// LIVER SHOT (Iron Fist, Novice, reactive). "If someone throws an uppercut, you go to the side and
    /// hit the liver." The big swing comes in (an uppercut, a blow from above, a hook or a kick to the
    /// head); the boxer dips and slips to the outside of it, the swing tears through the afterimage
    /// where it stood, the weight sinks onto the lead leg and a short hook digs in under the ribs. The
    /// foe stands a heartbeat as if nothing happened, then the liver tells: it folds over the fist,
    /// squashed and trembling, and stays dazed. II doubles the hook into the same spot and the fold
    /// goes deeper; III's second hook lands on an impact frame and the foe's knees buckle: it goes down
    /// (Down). Also planned: a foe in the middle of such a swing (at anyone) within a step is slipped
    /// and hooked before it lands, its swing cut short. Plain swings of the game are met too.
    /// Damage (x the user's damage): I 1.3; II 0.7 + 1.1; III 0.8 + 1.5. Dazed I 1 s, II 1.4 s, III 1.9 s.
    /// </summary>
    internal sealed class LiverShotTech : Technique
    {
        public override string Id => "liver_shot";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Body | Form.Counter | Form.Fast;
        public override Form Answers => Form.Rising | Form.Overhead | Form.High | Form.Hook;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 15f };
        public override float[] Cooldown => new[] { 6f, 5.5f, 5f };

        public static float First(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 0.7f : 0.8f;
        }

        public static float Second(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 1.1f : 1.5f;
        }

        public static float Total(int level)
        {
            return First(level) + Second(level);
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            // (a swing from the air, from across the field, or by something it can't get beside: let it come)
            if (!IronFist.InReach(defender, attacker, 2f))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return LiverShot.Begin(defender, attacker, null, incoming, level, this) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A plain swing at the head or from above is slipped and answered the same way.</summary>
        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            return IronFist.InReach(defender, attacker, 1.8f) && LiverShot.Begin(defender, attacker, null, null, level, this);
        }

        /// <summary>
        /// A foe within a step in the middle of a swing this answers (an uppercut, a hook, a blow from
        /// above: at it or at anyone) is slipped and hooked before the swing lands. The Brain adds its
        /// x2.2 for answering what the foe is doing; this is the blow's worth and a punish bonus.
        /// </summary>
        public override Plan Plan(Ctx c, int level)
        {
            if (c.Forced)
            {
                Actor f = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (f == null || c.Dist(f) > 4f)
                {
                    return null;
                }
                return new Plan { Target = f, Point = f.current_position, Dir = (f.current_position - c.Pos).normalized, Count = 1, Score = 1f, Data = true };
            }
            Actor best = null;
            float bestScore = 0f;
            float k = Choreo.KOf(c.Self);
            for (int i = 0; i < c.Enemies.Count && i < 5; i++)
            {
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] > 1.9f * k + v.stats["size"] * 0.5f + 0.4f)
                {
                    break;
                }
                if (!Combat.Grounded(v) || !Choreo.Fair(c.Self, v, out float cm))
                {
                    continue;
                }
                if (!Moves.TryGet(v, out Move mv) || mv.A != v || mv is Puppet || mv.Finished || mv.Cinematic)
                {
                    continue;
                }
                if ((Reads.FormsOf(mv) & Answers) == 0)
                {
                    continue;
                }
                float s = c.Value(v, c.Expected(v, Total(level)), cm) + 14f;
                if (s > bestScore)
                {
                    bestScore = s;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 d = best.current_position - c.Pos;
            return new Plan { Target = best, Point = best.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1, Score = bestScore };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p != null && p.Target != null && LiverShot.Begin(a, p.Target, p, null, p.Level, this);
        }
    }

    /// <summary>The liver shot's choreography (an answer, or a planned punish of a swing in progress).</summary>
    internal static class LiverShot
    {
        /// <summary>
        /// 'a' slips 'v''s swing and hooks its liver. 'p': a plan (Brain/forced), else an answer to
        /// 'incoming' (or to a plain blow when that is null). False if it couldn't start.
        /// </summary>
        public static bool Begin(Actor a, Actor v, Plan p, Move incoming, int level, Technique t)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, t) : ChoreoMove.Answer(a, v, t, level);
            if (m == null || m.V == null)
            {
                return false;
            }
            level = m.Level;
            Form answers = t.Answers;
            bool answering = p == null;
            float side = Random.value < 0.5f ? 1f : -1f;
            Vector2 decoyHead = Vector2.zero, slipFrom = a.current_position;
            bool torn = false;

            // THE SLIP: a dip and a slide to the outside of the swing, beside the foe at a hook's
            // length; the afterimage left standing takes the swing (nothing touches the boxer meanwhile)
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.1f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm => IronFist.Outside(mm, slipFrom, ref side, mm.Stand),
                Chase = true,
                To = new Shape(-12f, 1.1f, 0.84f, 0f, 0f),
                Ease = Ease.OutQuad,
                Intangible = true,
                NeedsVictim = true,
                Forms = Form.Evasive | Form.Counter | Form.Body,
                Enter = mm =>
                {
                    slipFrom = mm.Stance;
                    decoyHead = mm.HeadPos();
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Fx.Afterimage(mm, 0.24f, new Color(1f, 1f, 1f, 0.75f), Fx.WithA(mm.Hue.Edge, 0.35f), Fx.WithA(mm.Hue.Edge, 0f));
                        ChoreoFx.HeelDust(mm.Stance, mm.Stance - mm.VG, mm.Px, mm.A.current_tile, mm.Kk);
                    }
                },
                Every = (mm, k) =>
                {
                    // the swing tears through the afterimage where its head was
                    if (!torn && k > 0.5f && answering && mm.Vis)
                    {
                        torn = true;
                        Vector2 at = decoyHead - new Vector2(0f, 1f * mm.Px * mm.Kk);
                        Fx.Glint(at, mm.Px, 0.1f, Color.white, 0.8f);
                        Kit.Sparks(at, mm.Px, 3, 30f, 70f, new Color(0.92f, 0.95f, 1f, 1f));
                        IronFist.Streak(mm.VPoint(0.75f), at + (at - mm.VPoint(0.75f)).normalized * 3f * mm.Px, mm.Px, 0.1f, mm.Hue.Edge);
                    }
                }
            });
            // THE COIL: the weight sinks onto the lead leg, the fist drops to the hip
            m.PoseTo(0.05f, new Shape(-16f, 0.94f, 0.9f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind)
                .Shows(Form.Punch | Form.Body | Form.Counter)
                .OnEnter(mm =>
                {
                    if (mm.Vis && mm.Level >= 2)
                    {
                        Fx.Glint(mm.BodyPos() + new Vector2(mm.Fwd * 2f, -1.5f) * mm.Px, mm.Px, 0.12f, mm.Hue.Core, 0.75f);
                    }
                });
            // THE HOOK under the ribs: it barely moves (a jolt), the damage is inside
            Hit first = new Hit(Blow.Body, LiverShotTech.First(level))
            {
                Hurt = Hurt.Flinch,
                Push = 0.03f,
                WindScale = 0.5f,
                Power = 1.15f,
                Shows = Form.Punch | Form.Body | Form.Hook | Form.Counter | Form.Fast
            };
            first.OnLand = (mm, u, r) => IronFist.Rehurt(mm, u, first, Jolt(mm), incoming, answers);
            m.Strike(first);
            if (level >= 2)
            {
                // II/III: doubled into the same spot, harder
                Hit second = new Hit(Blow.Body, LiverShotTech.Second(level))
                {
                    Hurt = Hurt.Flinch,
                    Push = 0.03f,
                    WindScale = 0.55f,
                    Power = 1.35f,
                    Finisher = level >= 3,
                    Shows = Form.Punch | Form.Body | Form.Hook | Form.Counter
                };
                second.OnLand = (mm, u, r) => IronFist.Rehurt(mm, u, second, Jolt(mm), incoming, answers);
                m.Strike(second);
            }
            // THE DELAY: the boxer holds its follow-through, the foe stands a heartbeat as if nothing happened...
            m.Hold(level <= 1 ? 0.16f : 0.12f, false, Tempo.Follow)
                // ...then the liver tells
                .Do(mm => Fold(mm, level))
                .PoseTo(0.14f, new Shape(-6f, 1.02f, 0.98f, 0f, -0.06f), Ease.OutQuad, Tempo.Follow)
                .Recover(0.22f);
            return m.Start();
        }

        /// <summary>The hook's own reaction: a twitch (the pain comes a beat later).</summary>
        private static Impact Jolt(ChoreoMove m)
        {
            Impact im = Impact.Of(Hurt.Flinch, m.Dir, 0.03f, 0.9f);
            im.Stop = (m.Level >= 2 ? 0.05f : 0.035f) * m.ImpactK;
            return im;
        }

        /// <summary>
        /// The delayed collapse: it folds over the fist, squashed and trembling (III: its knees buckle and
        /// it goes down), a gasp of breath from its mouth, dazed.
        /// </summary>
        private static void Fold(ChoreoMove m, int level)
        {
            Actor v = m.V;
            if (!m.VOk || v == null)
            {
                return;
            }
            Impact im = Impact.Of(level >= 3 ? Hurt.Crumple : Hurt.Fold, m.Dir, 0.06f * m.Kk, level >= 2 ? 1.6f : 1.3f);
            im.Hold = level <= 1 ? 0.45f : 0.7f;
            im.Daze = level <= 1 ? 1f : level == 2 ? 1.4f : 1.9f;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            Victims.Hurt(v, m.A, m, im);
            m.SetLink(level >= 3 ? Link.Down : Link.Close, v);
            Choreo.Sound("event:/SFX/HIT/HitGeneric", v.current_tile, 0.1f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            // the gasp: breath driven out of it, and a ring of the blow's colour where the liver is
            Vector2 mouth = m.VPoint(0.82f) + new Vector2(-m.Fwd * 1.5f * px, 0f);
            for (int i = 0; i < Fx.Count(4); i++)
            {
                Particle q = Fx.Spawn(Art.Pixel, mouth, px, Random.Range(0.25f, 0.4f), FxLayer.Top, 8);
                q?.Moving(new Vector2(m.Fwd * Random.Range(10f, 24f), Random.Range(-6f, 8f)) * px, Random.Range(2f, 6f) * px, 3f)
                    .Sized(1.3f, 0.6f).Color(new Color(1f, 1f, 1f, 0.9f), new Color(0.85f, 0.9f, 1f, 0.5f), new Color(0.85f, 0.9f, 1f, 0f));
            }
            Vector2 liver = m.VPoint(0.42f);
            Fx.Ring(liver, px, 0.18f, 1f, 5f * m.Kk + 1.5f * level, 1, 1, FxLayer.Top, 4, 1f, 2.4f,
                Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
            if (level >= 3)
            {
                Kit.DustRing(Victims.GroundOf(v), m.VPx, v.current_tile, 6, 24f, 0.4f, Mathf.Sqrt(m.Kk));
            }
        }
    }
}
