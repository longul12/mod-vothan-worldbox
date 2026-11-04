using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// HOP STRIKE (universal, Adept). A blow comes in low (a leg sweep, a low kick, a cut at the
    /// ankles): the fighter hops straight up over it, the sweep whistling through the air under its
    /// feet (a white streak along the ground, an afterimage left standing where it was), tucks at
    /// the top with the heel cocked, and drives the heel down onto the sweeper, still low and
    /// over-extended in its whiffed sweep. I knocks it flat (Down); II drives it into the ground and
    /// dazes it; III hammers it in hard enough to crack the earth (a small crater, an impact frame),
    /// the shock tripping anyone else close. A breakdance spin (an area sweep) is hopped the same
    /// way; the heel only comes down on a sweeper within reach. In the counter web it is the answer
    /// to Low, and so to Duck and Sweep: a counter to that counter, for those who read that deep.
    ///
    /// Beat sheet (seconds at Novice pace; the tier's tempo shortens them):
    ///   HOP     0.10  up 0.55-0.65 tiles, stretched, drifting toward the sweeper; the sweep passes
    ///                 under (a streak on the ground, dust, its afterimage where it stood)
    ///   TUCK    0.05  knees up, the heel cocked over its head (a glint from II)
    ///   HEEL    0.08  dropping, the heel comes down in a crescent; contact at 70% (Combat.Reach)
    ///   LAND    0.09  squashed on landing, a puff (III: a shock ring)
    ///   RECOVER 0.16  Chain: Down
    /// Damage (x the user's damage): I 1.2, II 1.6, III 2.1 (+ the shock x0.45 on others within 1.3).
    /// </summary>
    internal sealed class HopStrikeTech : Technique
    {
        public override string Id => "hop_strike";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override Form Forms => Form.Aerial | Form.Counter | Form.Kick;
        public override Form Answers => Form.Low;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        private const Form HeelForms = Form.Kick | Form.Overhead | Form.Aerial | Form.Counter;

        public static float Mult(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.6f : 2.1f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (attacker.actor_scale > defender.actor_scale * 2.4f)
            {
                return Contact.Hit;   // a giant's leg is no hop's business
            }
            // a sweep is thrown from right next to it: the heel comes down only on a sweeper in reach
            // (an area sweep may catch it from further off: then it only hops over it)
            bool strike = Vector2.Distance(defender.current_position, attacker.current_position) <= UniKit.Reach(defender, attacker, 2.1f)
                && Choreo.Fair(defender, attacker, out _, true);
            if (!area && !strike)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            UniKit.Settle(defender);
            return Perform(defender, attacker, null, level, strike) ? Contact.Evaded : Contact.Hit;
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
            return p != null && p.Target != null && Perform(a, p.Target, p, p.Level, true);
        }

        /// <summary>The hop (and the heel, if 'strike'), as an answer to 'v' (p null) or forced on it.</summary>
        private bool Perform(Actor a, Actor v, Plan p, int level, bool strike)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, this) : ChoreoMove.Answer(a, v, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            float hopH = 0.5f + 0.06f * lv;
            if (p != null)
            {
                m.StepIn();   // (forced: nothing sweeps at it, so it steps in to show the hop and the heel)
            }
            // up: the sweep passes under its feet
            Beat hop = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.1f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => HopSpot(mm, strike),
                To = new Shape(-8f, 0.86f, 1.18f, hopH),
                Ease = Ease.OutCubic,
                Forms = Form.Aerial | Form.Evasive | Form.Counter,
                Ghosts = 0.03f
            };
            hop.Enter = mm =>
            {
                if (!strike)
                {
                    mm.SetVictim(null);   // (only hopping it: the far sweeper isn't worked over)
                }
                Under(mm);
            };
            m.Add(hop);
            if (strike)
            {
                // the heel cocked over its head at the top of the hop
                m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.05f, To = new Shape(-24f, 1.08f, 0.88f, hopH + 0.06f), Ease = Ease.OutQuad, Forms = HeelForms });
                if (lv >= 2)
                {
                    m.At(0.4f, mm =>
                    {
                        if (mm.Vis)
                        {
                            Fx.Glint(mm.BodyPos() + new Vector2(-mm.Fwd * 2f, 3.5f) * mm.Px, mm.Px, 0.14f, mm.Hue.Core, 0.85f);
                        }
                    });
                }
                Hit hit = new Hit(Blow.HeelDrop, Mult(lv))
                {
                    Hurt = lv <= 1 ? Hurt.Knockdown : Hurt.DriveIn,
                    Push = lv <= 1 ? 0.35f : 0f,
                    Daze = lv <= 1 ? 0f : lv == 2 ? 0.5f : 0.8f,
                    Power = lv <= 1 ? 0.75f : lv == 2 ? 0.9f : 1.05f,
                    Finisher = lv >= 3,
                    Crater = lv >= 3 ? 0.6f : 0f,
                    Area = lv >= 3 ? 1.3f : 0f,
                    AreaMult = 0.22f
                };
                // the heel comes down in a crescent as it drops
                Beat heel = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(32f, 1.14f, 0.86f, 0f), Ease = Ease.InQuad, Forms = HeelForms, Ghosts = 0.025f };
                heel.Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                    if (mm.Vis)
                    {
                        int r = Mathf.Clamp(Mathf.RoundToInt(9f * Mathf.Lerp(1f, mm.Kk, 0.6f) * (lv >= 3 ? 1.15f : 1f)), 4, 40);
                        Kit.Swish(mm, Art.Arc(r, 1f, 105f, -75f, lv >= 3 ? 3 : 2), 0.13f, new Vector2(2f * mm.Fwd, 2.5f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge);
                    }
                };
                heel.On(0.7f, mm => UniKit.Land(mm, hit, ChoreoMove.ShapeOf(Blow.HeelDrop), 0.8f, 0.7f));
                m.Add(heel);
            }
            else
            {
                // nothing to strike: it floats a heartbeat at the top and drops back
                m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.04f, To = new Shape(-4f, 1.04f, 0.94f, hopH + 0.04f), Ease = Ease.OutQuad, Forms = Form.Aerial | Form.Evasive });
                m.Add(new Beat { Tempo = Tempo.Travel, Dur = 0.09f, To = new Shape(4f, 0.9f, 1.12f, 0f), Ease = Ease.InQuad, Forms = Form.Aerial });
            }
            // down: squashed on landing
            Beat land = new Beat { Tempo = Tempo.Follow, Dur = 0.09f, To = new Shape(8f, 1.22f, 0.8f, 0f), Ease = Ease.OutExpo, Track = false };
            land.Enter = mm =>
            {
                mm.Pose.Height = 0f;
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/HIT/HitGeneric", tile, 0.08f);
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, tile);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 6, 26f, 0.4f, mm.Kk);
                    if (lv >= 3 && strike)
                    {
                        Kit.Shockwave(mm.Pose.Ground, mm.Px, 3f, 16f * mm.Kk, 0.3f, Fx.WithA(mm.Hue.Edge, 0.85f));
                    }
                }
            };
            m.Add(land);
            m.Recover(0.16f);
            return m.Start();
        }

        /// <summary>Where the hop carries it: a step toward the sweeper to bring the heel down on it (or straight up).</summary>
        private static Vector2 HopSpot(ChoreoMove mm, bool strike)
        {
            if (!strike || mm.V == null || mm.V.Sid() != mm.VId)
            {
                return mm.Stance;
            }
            Vector2 want = mm.StandSpot(mm.Stand * 0.85f);
            Vector2 d = want - mm.Stance;
            float max = 0.6f * mm.Kk;
            if (d.magnitude > max)
            {
                want = mm.Stance + d.normalized * max;
            }
            return Combat.SafeSpot(want) && Combat.ClearPath(mm.Stance, want) ? want : mm.Stance;
        }

        /// <summary>The sweep whistles under its feet: a white streak along the ground, dust, its afterimage left standing.</summary>
        private static void Under(ChoreoMove mm)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            Vector2 g = mm.Stance + new Vector2(0f, 1f * px);
            Vector2 across = new Vector2(6f * mm.Kk * px, 0f);
            Kit.Line(g - across, g + across, px, 0.1f, 1, FxLayer.Over, 6, Fx.WithA(Color.white, 0.9f), Fx.WithA(mm.Hue.Edge, 0.5f), Fx.WithA(mm.Hue.Edge, 0f));
            Fx.Afterimage(mm, 0.16f, new Color(1f, 1f, 1f, 0.6f), Fx.WithA(mm.Hue.Edge, 0.3f), Fx.WithA(mm.Hue.Edge, 0f));
            Fx.LandingPuff(mm.Stance, px, mm.A.current_tile);
            Kit.DustRing(mm.Stance, px, mm.A.current_tile, 5, 24f, 0.35f, mm.Kk);
        }
    }

    /// <summary>
    /// The universal answers' shared craft (hop_strike, grab_break, air_intercept, beam_cleave): the
    /// reach of a close answer, the forced demonstration plan, and the answering blow itself (the
    /// target may answer it in turn through Combat.Reach; then it is taken out of whatever move of
    /// its own it is still in, so it reacts in full, and the blow lands through ChoreoMove.Deliver).
    /// </summary>
    internal static class UniKit
    {
        /// <summary>How close 'atk' must be for a blow of 'd' to reach it ('basic' tiles plus both sizes).</summary>
        public static float Reach(Actor d, Actor atk, float basic)
        {
            return basic * Choreo.KOf(d) + atk.stats["size"] * 0.5f;
        }

        /// <summary>A forced plan (dev command, god power) on the forced target or the nearest foe within 'range' tiles.</summary>
        public static Plan ForcedPlan(Ctx c, float range)
        {
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > range)
            {
                return null;
            }
            Vector2 d = v.current_position - c.Pos;
            return new Plan
            {
                Target = v,
                Point = v.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Score = 1f,
                Data = true
            };
        }

        /// <summary>
        /// The foe's own move, if it is still in one (not someone's puppet, not a light move or one
        /// already answered, not a cinematic, not a charge: taking hold of a charge breaks it anyway),
        /// is cut short, so the answer's blow can throw it.
        /// </summary>
        public static void TakeOut(Actor v)
        {
            if (v != null && Moves.TryGet(v, out Move vm) && vm.A == v && !(vm is Puppet) && !vm.Interruptible && !vm.Finished && !vm.Ended
                && !vm.Cinematic && !vm.Charging)
            {
                Choreo.Kill(vm);
            }
        }

        /// <summary>
        /// The answer's blow 'h' (of shape 's', landing 'frac' of the way up the target) on the move's
        /// target: in reach ('slack' tiles of play), it may answer (Touch: a counter to the counter),
        /// then it is taken out of its own move and the blow lands. True if it landed.
        /// </summary>
        public static bool Land(ChoreoMove m, Hit h, BlowShape s, float frac, float slack = 0.6f, bool level = false)
        {
            if (!m.VOk)
            {
                Whiff(m, s.Limb);
                m.Lose();   // (a counter's blow is for the one it answered: no one else takes its place)
                return false;
            }
            Actor v = m.V;
            float reach = m.Stand + 0.6f * m.Kk + v.stats["size"] * 0.5f + slack;
            float dist = Vector2.Distance(m.Pose.Ground, m.VG);
            float rise = Mathf.Abs(Combat.HeightOf(v) - m.Pose.Height);
            if (dist > reach || rise > 0.9f * m.Kk + m.VHeight)
            {
                if (Dev.On) Dev.Trace("uni " + (m.Tech != null ? m.Tech.Id : "?") + ": the blow cuts the air (" + dist.ToString("F2") + " tiles, " + rise.ToString("F2") + " up)");
                Whiff(m, s.Limb);
                return false;
            }
            if (!m.Touch(v))
            {
                return false;   // answered in turn: the move recoils or whiffs on its own
            }
            TakeOut(v);
            Vector2 dir = m.VG - m.Pose.Ground;
            if (level)
            {
                // (in the air the two are nearly over one another: it is sent back the way it faces, across the screen)
                dir = new Vector2(Mathf.Abs(dir.x) > 0.15f ? Mathf.Sign(dir.x) : m.Fwd, Mathf.Clamp(dir.y, -0.35f, 0.35f));
            }
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : m.Dir;
            m.Deliver(v, h, s, m.ContactPoint(frac), dir);
            return true;
        }

        /// <summary>
        /// Right after Reactions.Free: a unit whose own move was cut short a hair above the ground (a
        /// hop, a lunge's lift) is set down on it, or it could not start its answer at all.
        /// </summary>
        public static void Settle(Actor d)
        {
            if (d != null && !Moves.Busy(d) && !d.isFlying() && d.position_height > 0f && d.position_height <= 0.35f)
            {
                d.position_height = 0f;
            }
        }

        /// <summary>A blow that meets nothing: a white swish off the limb and a whoosh.</summary>
        public static void Whiff(ChoreoMove m, Limb limb)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), m.Hand(limb) + new Vector2(m.Fwd * 2f * m.Px, 0f), m.Px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = m.Fwd;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        /// <summary>Is a held particle still the one it was (pooled particles are recycled into other effects)?</summary>
        public static bool Ours(Particle p, int gen)
        {
            return p != null && p.Alive && p.Gen == gen;
        }

        /// <summary>
        /// A held bar of light (a stream of a split beam): a stretched pixel from its middle 'mid',
        /// 'len' long, 'thick' pixels, at 'rot' degrees, kept alive with the move (glued).
        /// </summary>
        public static void Bar(ChoreoMove m, ref Particle p, ref int gen, Vector2 mid, float len, float thick, float rot, Color c, int order, float px)
        {
            if (!Ours(p, gen))
            {
                p = m.Glue(Fx.Spawn(Art.Pixel, mid, px, 1f, FxLayer.Top, order));
                if (p == null)
                {
                    return;
                }
                gen = p.Gen;
            }
            p.Pos = mid;
            p.SX = Mathf.Max(0.5f, len / px);
            p.SY = Mathf.Max(1f, Mathf.Round(thick));
            p.Rot = rot;
            p.Grad = null;
            p.R.color = c;
        }
    }
}
