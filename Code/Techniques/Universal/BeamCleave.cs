using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BEAM CLEAVE (universal, Master). A beam roars down on it: it meets the head of it with the
    /// edge of one hand, chopping down into it (a crack, a flash, the world catching its breath),
    /// and the beam SPLITS on the hand: two streams of the shooter's own light fanning past either
    /// side of it, sparks spraying off the edge, the ground torn behind it (from II). Then it walks
    /// into the beam, step by step, the hand held out edge first, the split riding on it, its body
    /// leaning into the press and trembling with it, the beam shortening before it. If it reaches
    /// the shooter, the hand comes up out of the beam (the beam, cut through, dies) and comes down
    /// on the shooter's collarbone: its power bursts out of it and it drops (I knocked flat, II on
    /// its knees and face, III driven into a crater). If the beam is too long to walk, the hand
    /// slices on through it instead: the cut runs down the beam into the shooter's hands and the
    /// beam dies (III: the cut reaches the shooter and breaks its stance from afar).
    /// A ki shot (a bullet, an orb, a disc, a spear of light) is chopped in two the same way, the
    /// halves flying past, and it strides through where it was (III: on to the shooter, if near).
    /// Answers Beam and Ki; never an area (a dome, a flare) nor a palm of ki in close, nor the
    /// everyday bolts of a master's ranged blows; and a beam far beyond its own power (a God's beam
    /// of beams for anyone but a God) can't be split. The Master's answer to a beam besides a clash.
    ///
    /// Beat sheet (seconds at Novice pace; the tier's tempo shortens them):
    ///   CHOP     0.07  the edge of the hand down into the beam's head: the split (II+: the ground torn)
    ///   WALK     up to 4 / 7 / 10 tiles (x size) at a walk slowed by how hard the beam presses
    ///   either   CUT 0.07 + 0.12 (the beam cut through and dead; III: the shooter's stance broken)
    ///   or       RAISE 0.07 (the beam dies) / CHOP 0.08 on the shooter (Combat.Reach) / held 0.18
    ///   RECOVER  0.24. Chain: Down (it reached the shooter)
    /// Damage (x the user's damage): the chop I 1.8, II 2.4, III 3.2; the far cut (III) 0.8.
    /// </summary>
    internal sealed class BeamCleaveTech : Technique
    {
        public override string Id => "beam_cleave";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override Form Forms => Form.Counter | Form.Palm;
        public override Form Answers => Form.Beam | Form.Ki;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 22f, 32f, 44f };
        public override float[] Cooldown => new[] { 20f, 21f, 22f };

        public static float ChopMult(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.4f : 3.2f;
        }

        /// <summary>How far it walks into a beam (tiles, x its size) before it cuts it instead.</summary>
        private static float MaxWalk(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 7f : 10f;
        }

        /// <summary>The hand's edge against a beam: as a beam's power is reckoned (Beam.Power), a cleave's.</summary>
        private static float Edge(Actor d, int level)
        {
            TierInfo ti = Tiers.Info(Tiers.Of(d));
            return (ti != null ? ti.Might : 1f) * (0.8f + 0.2f * level) * 1.6f;
        }

        /// <summary>The chop: a knife-hand brought down from above (the engine's blow numbers for its weight and reaction).</summary>
        private static readonly BlowShape ChopShape = new BlowShape
        {
            Limb = Limb.Palm,
            Weight = 1.35f,
            Heavy = true,
            Hurt = Hurt.Knockdown,
            Push = 0.4f,
            Lift = -1f,
            Contact = 0.8f,
            Forms = Form.Palm | Form.Overhead | Form.Counter
        };

        /// <summary>What one cleave is doing (the beam it holds split, how far it walked, what it ends in).</summary>
        private sealed class Cleave
        {
            public Beam Beam;
            public Shot Shot;
            public bool Demo, Done;
            /// <summary>1 the cut (it never reached the shooter), 2 the chop on the shooter, 3 a shot chopped (a stride).</summary>
            public int Mode = 1;
            public float Walked, MaxWalk, Speed, Strain, StepT, SparkT, MoteT, ShakeT, SoundT;
            public int LastStep;
            public Vector2 Knot, CutAt;
            public readonly Particle[] Split = new Particle[4];
            public readonly int[] SplitGen = new int[4];
            public Particle Core;
            public int CoreGen;
        }

        // ------------------------------------------------------------------ react --

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            // (a dome or a flare can't be chopped; a master's everyday bolts are not worth the hand)
            if (area || incoming == null || incoming.Interruptible || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Beam beam = BeamOf(incoming, attacker);
            if (beam != null)
            {
                if (!Splittable(defender, beam, level))
                {
                    return Contact.Hit;   // too much for one hand
                }
                Reactions.Free(defender);
                UniKit.Settle(defender);
                return Perform(defender, attacker, beam, null, level) ? Contact.Parried : Contact.Hit;
            }
            Shot shot = ShotOf(incoming, attacker);
            if (shot == null || !Cuttable(defender, shot))
            {
                return Contact.Hit;   // (a palm of ki in close: no beam, no shot to split)
            }
            Reactions.Free(defender);
            UniKit.Settle(defender);
            return PerformShot(defender, attacker, shot, level) ? Contact.Parried : Contact.Hit;
        }

        /// <summary>The beam of 'atk' whose first touch this is (its move is the one reaching), still firing, not locked in a clash.</summary>
        private static Beam BeamOf(Move incoming, Actor atk)
        {
            IReadOnlyList<Beam> all = Beams.All;
            for (int i = 0; i < all.Count; i++)
            {
                Beam b = all[i];
                if (b.M == incoming && b.A == atk && b.Firing && !b.Done && b.Clash == null)
                {
                    return b;
                }
            }
            return null;
        }

        /// <summary>The shot of 'atk' reaching it right now (Shots.Reaching), if it is one a hand can split (not a rock, not a chain).</summary>
        private static Shot ShotOf(Move incoming, Actor atk)
        {
            Shot s = Shots.Reaching;
            if (s == null || s.Done || !s.Hostile || s.A != atk || s.By != incoming)
            {
                return null;
            }
            switch (s.Spec.Kind)
            {
                case ShotKind.Boulder:
                case ShotKind.Stone:
                case ShotKind.Harpoon:
                    return null;
                default:
                    return s;
            }
        }

        /// <summary>A beam the hand can split: never one that always wins (unless a God's), never one far beyond its own power.</summary>
        private static bool Splittable(Actor d, Beam b, int level)
        {
            if (b.Spec.AlwaysWins && Tiers.Of(d) < Tier.God)
            {
                return false;
            }
            return b.Power <= Edge(d, level) * 1.25f;
        }

        /// <summary>A shot the hand can split: not from two tiers above it, nor a huge one from a tier above.</summary>
        private static bool Cuttable(Actor d, Shot s)
        {
            int gap = (int)Tiers.Of(s.A) - (int)Tiers.Of(d);
            return gap < 2 && !(gap == 1 && s.Spec.Size >= 1.8f);
        }

        // ------------------------------------------------------------------- plan --

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            return UniKit.ForcedPlan(c, 12f);
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (p == null || p.Target == null)
            {
                return false;
            }
            // forced: a beam of the target's coming at it is split for real; otherwise it walks the walk to it
            Beam b = Beams.Of(p.Target);
            if (b != null && (b.A == null || b.Clash != null || Beams.Toward(a, 0.5f) != b))
            {
                b = null;
            }
            return Perform(a, p.Target, b, p, p.Level);
        }

        // ------------------------------------------------------------------- beam --

        private bool Perform(Actor a, Actor v, Beam beam, Plan p, int level)
        {
            ChoreoMove m = p != null ? ChoreoMove.Make(a, p, this) : ChoreoMove.Answer(a, v, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Cleave s = new Cleave { Beam = beam, Demo = beam == null };
            s.Strain = beam != null ? Mathf.Clamp01(beam.Power / (Edge(a, lv) * 1.25f)) : 0.3f;
            s.MaxWalk = MaxWalk(lv) * m.Kk;
            s.Speed = (2.4f + 0.9f * lv) * Mathf.Lerp(1f, m.Kk, 0.5f) / Mathf.Max(0.5f, m.ActK) * Mathf.Lerp(1.25f, 0.6f, s.Strain);
            // the edge of the hand chops down into the beam's head: it splits
            Beat chop = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(16f, 1.1f, 0.92f, 0f, 0.05f), Ease = Ease.OutExpo, Forms = Form.Palm | Form.Overhead | Form.Counter };
            chop.Enter = mm => ChopIn(mm, s);
            chop.Every = (mm, k) => Hold(mm, s);
            m.Add(chop);
            // the walk into it
            Beat walk = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.05f,
                To = new Shape(14f, 1.06f, 0.95f, 0f),
                Ease = Ease.OutQuad,
                Forms = Form.Counter | Form.Palm,
                Until = mm => s.Done,
                MaxDur = 4f
            };
            walk.Enter = mm => s.StepT = 0f;
            walk.Every = (mm, k) => Walk(mm, s);
            walk.Exit = mm => mm.Pose.Nudge = Vector2.zero;
            m.Add(walk);
            // it never got there: the hand slices on through and the beam dies
            Beat cut = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(26f, 1.18f, 0.88f, 0f, 0.1f), Ease = Ease.OutExpo, Forms = Form.Palm | Form.Counter, If = mm => s.Mode == 1 };
            cut.Enter = mm => Cut(mm, s, true);
            m.Add(cut);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.12f, To = new Shape(12f, 1.08f, 0.95f, 0f, 0.06f), Ease = Ease.OutQuad, Track = false, If = mm => s.Mode == 1 });
            // it got there: the hand comes up out of the beam and down on the shooter
            Finale(m, s, lv);
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The chop on the shooter (Mode 2): a step in if it is a step away, the hand raised, brought down.</summary>
        private static void Finale(ChoreoMove m, Cleave s, int lv)
        {
            m.StepIn();
            Beat step = m.Last;
            System.Func<ChoreoMove, bool> near = step.If;
            step.If = mm => s.Mode == 2 && (near == null || near(mm));
            Beat raise = new Beat { Tempo = Tempo.Wind, Dur = 0.07f, To = new Shape(-18f, 0.92f, 1.12f, 0f, -0.02f), Ease = Ease.OutQuad, Forms = Form.Palm | Form.Overhead | Form.Counter, If = mm => s.Mode == 2 };
            raise.Enter = mm => Cut(mm, s, false);
            raise.On(0.6f, mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(mm.BodyPos() + new Vector2(mm.Fwd * 1.5f, 5f) * mm.Px, mm.Px, 0.16f, mm.Hue.Core, 1f);
                }
            });
            m.Add(raise);
            Hit hit = new Hit(Blow.Cleave, ChopMult(lv))
            {
                Hurt = lv <= 1 ? Hurt.Knockdown : lv == 2 ? Hurt.Crumple : Hurt.DriveIn,
                Push = lv <= 1 ? 0.6f : 0f,
                Daze = lv <= 1 ? 0.6f : lv == 2 ? 0.9f : 1.2f,
                Power = lv <= 1 ? 1f : lv == 2 ? 1.12f : 1.25f,
                Finisher = true,
                Crater = lv >= 3 ? 0.9f : 0f
            };
            Beat down = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(36f, 1.16f, 0.86f, 0f, 0.16f), Ease = Ease.OutExpo, Forms = Form.Palm | Form.Overhead | Form.Counter | Form.Heavy, If = mm => s.Mode == 2 };
            down.Enter = mm => ChopFx(mm);
            down.On(0.5f, mm =>
            {
                if (UniKit.Land(mm, hit, ChopShape, 0.8f, 0.6f))
                {
                    Shatter(mm, lv);
                }
            });
            m.Add(down);
            Beat held = new Beat { Tempo = Tempo.Follow, Dur = 0.12f, To = new Shape(26f, 1.1f, 0.9f, 0f, 0.12f), Ease = Ease.OutQuad, Track = false, If = mm => s.Mode == 2 };
            held.Enter = mm => mm.BeatDur *= 1.5f;   // (the last blow's pose is held: it reads)
            m.Add(held);
        }

        /// <summary>The hand meets the beam's head: a crack and a flash, the split, the ground torn behind it (II+).</summary>
        private static void ChopIn(ChoreoMove mm, Cleave s)
        {
            WorldTile tile = mm.A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", tile);
            Choreo.Sound("event:/SFX/HIT/HitMetal", tile, 0.05f);
            Vector2 knot = KnotOf(mm);
            s.Knot = knot;
            Beam b = s.Beam;
            if (b != null && b.Firing)
            {
                b.StopAt(knot, 0.2f);
            }
            Hue bh = b != null ? b.Hue : mm.Hue;
            mm.HoldFor(0.04f * mm.ImpactK);
            Kit.Stop(knot, b != null ? 0.05f : 0.02f);
            Ruin.Quake(mm.A, knot, Scale.Light, b != null ? 1f : 0.4f, mm.Kk);
            if (b != null && mm.Level >= 2)
            {
                // the split streams tear the ground behind it
                float len = (1.3f + 0.45f * mm.Level) * Mathf.Lerp(1f, b.K, 0.5f);
                float ang = 13f + 3f * mm.Level;
                Vector2 g = mm.Stance + b.Dir * 0.3f * mm.Kk;
                for (int i = -1; i <= 1; i += 2)
                {
                    Vector2 d = Choreo.Rotate(b.Dir, i * ang);
                    Ruin.Furrow(mm.A, g, g + d * len, 0.5f, Scale.Light, mm.Pose.Ground);
                }
            }
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px, k = mm.Kk;
            int r = Mathf.Clamp(Mathf.RoundToInt(8f * Mathf.Lerp(1f, k, 0.6f)), 4, 40);
            Kit.Swish(mm, Art.Arc(r, 0.9f, 110f, -60f, 2), 0.12f, new Vector2(3f * mm.Fwd, 2f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge);
            Kit.Flash(knot, px, Mathf.RoundToInt(5f * k), 0.08f, Color.white, mm.TopLayer, 30);
            Fx.Ring(knot, px, 0.22f, 2f, 16f * k, 2, 1, mm.TopLayer, 24, 0.7f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(bh.Edge, 0.6f), Fx.WithA(bh.Edge, 0f));
            Kit.Sparks(knot, px, 8, 60f, 140f, bh.Edge);
            if (mm.Level >= 2)
            {
                Fx.FocusLines(knot, px, Fx.Count(7), 9f * k, 16f * k, 0.1f);
            }
        }

        /// <summary>Where the edge of the hand meets the beam: just in front of the hand.</summary>
        private static Vector2 KnotOf(ChoreoMove mm)
        {
            return mm.Hand(Limb.Palm) + mm.Dir * 1.5f * mm.Px;
        }

        /// <summary>Every frame it holds the beam: stopped at the hand, split round it, sparks and motes streaming past.</summary>
        private static void Hold(ChoreoMove mm, Cleave s)
        {
            Beam b = s.Beam;
            if (b == null || !b.Firing || b.Done)
            {
                Unsplit(mm, s);
                return;
            }
            Vector2 knot = KnotOf(mm);
            s.Knot = knot;
            b.StopAt(knot, 0.15f);
            float dt = Clock.Dt;
            s.ShakeT -= dt;
            if (s.ShakeT <= 0f)
            {
                s.ShakeT = 0.3f;
                Kit.Shake(knot, 0.28f, 0.002f + 0.004f * s.Strain, mm.Kk);
            }
            s.SoundT -= dt;
            if (s.SoundT <= 0f)
            {
                s.SoundT = 0.4f;
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
            }
            if (!mm.Vis)
            {
                Unsplit(mm, s);
                return;
            }
            Render(mm, s, b, knot, dt);
        }

        /// <summary>The split: two streams of the beam's own light fanning past either side, a white-hot knot at the hand.</summary>
        private static void Render(ChoreoMove mm, Cleave s, Beam b, Vector2 knot, float dt)
        {
            float bpx = b.Px;
            float wk = Mathf.Lerp(1f, b.K, 0.5f);
            float t = mm.T;
            float w = b.Spec.Width * wk * (1f + 0.12f * Mathf.Sin(t * 47f) + 0.06f * Mathf.Sin(t * 83f));
            float ang = 13f + 3f * mm.Level;
            float len = (1.3f + 0.45f * mm.Level) * wk * (0.9f + 0.1f * Mathf.Sin(t * 31f));
            for (int i = 0; i < 2; i++)
            {
                float sg = i == 0 ? 1f : -1f;
                Vector2 d = Choreo.Rotate(b.Dir, sg * ang);
                float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                UniKit.Bar(mm, ref s.Split[i * 2], ref s.SplitGen[i * 2], knot + d * len * 0.5f, len, Mathf.Max(2f, w * 0.7f + ((int)(t * 30f) & 1)), rot,
                    Fx.WithA(b.Hue.Edge, 0.55f), 10, bpx);
                UniKit.Bar(mm, ref s.Split[i * 2 + 1], ref s.SplitGen[i * 2 + 1], knot + d * len * 0.45f, len * 0.9f, Mathf.Max(1f, w * 0.28f), rot,
                    Fx.WithA(b.Hue.Core, 0.95f), 12, bpx);
            }
            if (!UniKit.Ours(s.Core, s.CoreGen))
            {
                s.Core = mm.Glue(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(w * 0.55f), 2, 12), 1f, 0.3f), knot, bpx, 1f, FxLayer.Top, 14));
                s.CoreGen = s.Core != null ? s.Core.Gen : 0;
            }
            if (s.Core != null)
            {
                s.Core.Pos = knot;
                s.Core.Grad = null;
                s.Core.R.color = Fx.WithA(Color.Lerp(Color.white, b.Hue.Core, 0.3f), 0.8f + 0.2f * Mathf.Sin(t * 61f));
            }
            // sparks off the edge, motes streaming on past it
            s.SparkT -= dt;
            if (s.SparkT <= 0f)
            {
                s.SparkT = 0.035f;
                int n = Fx.Count(3);
                for (int i = 0; i < n; i++)
                {
                    float sg = Random.value < 0.5f ? -1f : 1f;
                    Vector2 d = Choreo.Rotate(b.Dir, sg * (ang + Random.Range(-8f, 18f)));
                    Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], knot, bpx, Random.Range(0.12f, 0.22f), FxLayer.Top, 21);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving(d * Random.Range(70f, 150f) * bpx * b.K, Random.Range(0f, 20f) * bpx, 5f, 120f * bpx);
                    p.FaceVel = true;
                    p.Color(Color.white, b.Hue.Edge, Fx.WithA(b.Hue.Edge, 0f));
                }
            }
            s.MoteT -= dt;
            if (s.MoteT <= 0f)
            {
                s.MoteT = 0.025f;
                float sg = Random.value < 0.5f ? -1f : 1f;
                Vector2 d = Choreo.Rotate(b.Dir, sg * ang);
                Particle m = Fx.Spawn(Art.Pixel, knot + Choreo.Perp(d) * Random.Range(-w, w) * 0.4f * bpx, bpx, 0.14f, FxLayer.Top, 15);
                m?.Moving(d * b.Spec.Speed * 0.3f * b.K, 0f, 2f).Sized(1.6f, 1f).Color(Color.white, Fx.WithA(b.Hue.Edge, 0.8f), Fx.WithA(b.Hue.Edge, 0f));
            }
        }

        /// <summary>The split's held light goes out (the beam died, or it is off screen).</summary>
        private static void Unsplit(ChoreoMove mm, Cleave s)
        {
            for (int i = 0; i < s.Split.Length; i++)
            {
                if (s.Split[i] != null)
                {
                    mm.Unglue(s.Split[i]);
                    s.Split[i] = null;
                }
            }
            if (s.Core != null)
            {
                mm.Unglue(s.Core);
                s.Core = null;
            }
        }

        /// <summary>
        /// One frame of the walk into the beam: a stride toward the shooter (slower the harder it
        /// presses), the body leaning into it and trembling, the beam held split at the hand. It ends
        /// at the shooter (Mode 2), or with the cut (Mode 1): too far to walk, no way on, the beam gone.
        /// </summary>
        private static void Walk(ChoreoMove mm, Cleave s)
        {
            if (s.Done)
            {
                return;
            }
            float dt = Clock.Dt;
            Beam b = s.Beam;
            bool live = s.Demo || b != null && b.Firing && !b.Done && b.Clash == null;
            if (!mm.VOk)
            {
                s.Mode = 1;
                s.Done = true;
                return;
            }
            Vector2 d = mm.VG - mm.Stance;
            float dist = d.magnitude;
            float stop = mm.Stand + 0.15f * mm.Kk;
            if (dist <= stop + 0.05f)
            {
                s.Mode = 2;
                s.Done = true;
                return;
            }
            if (!live || s.Walked >= s.MaxWalk)
            {
                // (a step from its shooter, the walk or the beam ending: the chop all the same)
                s.Mode = dist <= 2.4f * mm.Kk ? 2 : 1;
                s.Done = true;
                return;
            }
            float step = Mathf.Min(s.Speed * dt, dist - stop);
            if (step > 0f)
            {
                Vector2 next = Combat.Clamp(mm.Stance + d / dist * step);
                if (!(Combat.SafeSpot(next) || Choreo.Wading(next)) || !Combat.ClearPath(mm.Stance, next))
                {
                    s.Mode = 1;   // water, a wall: no way on
                    s.Done = true;
                    return;
                }
                s.Walked += step;
                mm.Stance = next;
            }
            // the stride: a bob at each step, the weight driving into the press, a tremble
            s.StepT += dt;
            float stride = 0.17f * mm.ActK;
            float ph = s.StepT / stride;
            float bob = Mathf.Abs(Mathf.Sin(Mathf.PI * ph));
            mm.Pose.Ground = mm.Stance;
            mm.Pose.Height = 0.035f * mm.Kk * bob;
            mm.Pose.Rot = 14f + 5f * s.Strain + 3f * bob;
            mm.Pose.SX = 1.06f - 0.03f * bob;
            mm.Pose.SY = 0.95f + 0.03f * bob;
            mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.3f + 0.6f * s.Strain);
            int stepNo = (int)ph;
            if (stepNo != s.LastStep)
            {
                s.LastStep = stepNo;
                if (mm.Vis)
                {
                    ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                    if ((stepNo & 1) == 0)
                    {
                        Fx.Afterimage(mm, 0.14f, Fx.WithA(mm.Hue.Ghost, 0.45f), mm.Hue.GhostEnd);
                    }
                }
            }
            Hold(mm, s);
        }

        /// <summary>
        /// The hand slices on through the beam: the cut runs down it to the shooter's hands and the beam
        /// dies. 'far' (it never got there): II the shooter's hands flare, III the cut reaches it and
        /// breaks its stance.
        /// </summary>
        private static void Cut(ChoreoMove mm, Cleave s, bool far)
        {
            Beam b = s.Beam;
            Unsplit(mm, s);
            if (b == null || !b.Firing)
            {
                if (far && s.Demo)
                {
                    UniKit.Whiff(mm, Limb.Palm);
                }
                return;
            }
            Vector2 knot = s.Knot.sqrMagnitude > 0f ? s.Knot : KnotOf(mm);
            Vector2 muzzle = b.From;
            b.Collapse();
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", mm.A.current_tile);
            if (mm.Vis || Kit.Visible(muzzle))
            {
                float px = mm.Px;
                Kit.Line(knot, muzzle, px, 0.16f, 1, mm.TopLayer, 26, Color.white, Fx.WithA(b.Hue.Core, 0.8f), Fx.WithA(b.Hue.Edge, 0f));
                Kit.Line(knot, muzzle, px, 0.1f, 3, mm.TopLayer, 25, Fx.WithA(b.Hue.Edge, 0.6f), Fx.WithA(b.Hue.Edge, 0f));
                Kit.Flash(knot, px, Mathf.RoundToInt(4f * mm.Kk), 0.06f, Color.white, mm.TopLayer, 30);
                Kit.Sparks(knot, px, 6, 50f, 120f, b.Hue.Edge);
            }
            if (!far || !mm.VOk || mm.Level < 2)
            {
                return;
            }
            Actor v = mm.V;
            Choreo.BurstAt(muzzle, b.Px, false, mm.A);
            if (mm.Level < 3)
            {
                return;
            }
            // III: the cut reaches the shooter (it may still slip it) and breaks its stance
            if (Combat.Reach(mm, v, true) != Contact.Hit)
            {
                return;
            }
            Combat.Allowed(mm.A, v, out float cm);
            Combat.Hit(mm.A, v, 0.8f * cm, false, true, 0f, mm);
            Vector2 dir = v.current_position - mm.Stance;
            Choreo.Break(v, mm.A, dir.sqrMagnitude > 1e-4f ? dir.normalized : mm.Dir, 1.6f);
            Victims.Flash(v, Color.white, 0.06f);
        }

        /// <summary>The chop on the shooter: a big crescent brought down from above, the body's blur behind it.</summary>
        private static void ChopFx(ChoreoMove mm)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            int r = Mathf.Clamp(Mathf.RoundToInt(10f * Mathf.Lerp(1f, mm.Kk, 0.6f) * (mm.Level >= 3 ? 1.15f : 1f)), 4, 44);
            Kit.Swish(mm, Art.Arc(r, 1f, 110f, -75f, 3), 0.15f, new Vector2(2f * mm.Fwd, 3f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge);
            Fx.Ghost(mm, -mm.Dir * 2f * px, -8f, 0.08f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
        }

        /// <summary>The chop lands: the shooter's gathered power bursts out of it (III: a shock ring along the ground).</summary>
        private static void Shatter(ChoreoMove mm, int lv)
        {
            Vector2 at = mm.ContactPoint(0.6f);
            Choreo.BurstAt(at, mm.VPx, true, mm.A);
            if (lv >= 3 && mm.Vis)
            {
                Kit.Shockwave(mm.VG, mm.Px, 3f, 22f * mm.Kk, 0.34f, Fx.WithA(mm.Hue.Edge, 0.9f));
            }
        }

        // ------------------------------------------------------------------- shot --

        /// <summary>A ki shot chopped in two: the halves fly past, it strides through (III: on to a shooter close by, and the chop).</summary>
        private bool PerformShot(Actor a, Actor v, Shot shot, int level)
        {
            ChoreoMove m = ChoreoMove.Answer(a, v, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Cleave s = new Cleave { Shot = shot, CutAt = shot.Pos + new Vector2(0f, shot.Z) };
            float dist = Vector2.Distance(a.current_position, v.current_position);
            s.Mode = lv >= 3 && dist <= 3.4f * m.Kk && dist > 0.8f && Choreo.Fair(a, v, out _, true) && Combat.ClearPath(a.current_position, v.current_position) ? 2 : 3;
            Beat chop = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(22f, 1.14f, 0.9f, 0f, 0.08f), Ease = Ease.OutExpo, Forms = Form.Palm | Form.Counter };
            chop.Enter = mm => ShotChop(mm, s);
            m.Add(chop);
            // a stride through where it was
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.12f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.Ahead(0.4f * mm.Kk),
                To = new Shape(10f, 1.06f, 0.96f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Forms = Form.Counter | Form.Palm,
                Dust = 0.06f,
                If = mm => s.Mode == 3
            });
            // III: on through it to the shooter, and the chop
            m.Dash();
            m.Last.If = mm => s.Mode == 2 && mm.VOk;
            Finale(m, s, lv);
            m.Recover(0.2f);
            if (!m.Start())
            {
                return false;
            }
            shot.Cut();
            return true;
        }

        /// <summary>The chop meets the shot: a crescent, a crack of light where it splits.</summary>
        private static void ShotChop(ChoreoMove mm, Cleave s)
        {
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", mm.A.current_tile);
            mm.HoldFor(0.03f * mm.ImpactK);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            int r = Mathf.Clamp(Mathf.RoundToInt(8f * Mathf.Lerp(1f, mm.Kk, 0.6f)), 4, 40);
            Kit.Swish(mm, Art.Arc(r, 0.9f, 110f, -60f, 2), 0.12f, new Vector2(3f * mm.Fwd, 2f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge);
            Kit.Flash(s.CutAt, px, Mathf.RoundToInt(4f * mm.Kk), 0.07f, Color.white, mm.TopLayer, 30);
            Kit.Sparks(s.CutAt, px, 5, 50f, 120f, mm.Hue.Edge);
        }
    }

    /// <summary>
    /// Dev staging of the universal answers, so each can be watched up close: two fighters on a clean
    /// meadow, held still, then A attacks and B answers with the answer under test (Reactions.DevForce).
    ///
    ///   ustage KIND [tierA] [tierB] [zoom] [raceA] [raceB] [gap]
    ///     hop      A sweeps B's legs; B hops it and drives the heel down (hop_strike)
    ///     hopc     A throws a Heaven Splitter; B ducks and sweeps (duck_sweep); A hops the sweep (A Expert+)
    ///     grab     A piledrives B (piledriver); B breaks the hold (grab_break)
    ///     air      A leaps and drops a heel on B; B springs up into it (air_intercept, answered)
    ///     airplan  A leaps at B from afar; B leaps to meet it in the air (air_intercept, planned)
    ///     beam     A fires a Dragon Wave at B; B splits it and walks through it (beam_cleave)
    ///     shot     A throws a ki orb at B; B chops it in two (beam_cleave)
    /// Then e.g. "capture 140 1 760 480 1.4 uni_hop". It logs "ustage done" after 5 s.
    /// </summary>
    internal static class UniStage
    {
        private static Actor _a, _b;
        private static string _kind;
        private static float _t = -1f, _wait;
        private static bool _went, _forced;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["ustage"] = Command;
            Hooks.FrameTick += dt =>
            {
                if (_t >= 0f)
                {
                    try
                    {
                        Tick();
                    }
                    catch (System.Exception e)
                    {
                        SakugaMod.Report("ustage", e);
                        End();
                    }
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _kind = Dev.Arg(args, 1, "hop");
            Tier ta = Dev.ParseTier(Dev.Arg(args, 2, "master"));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 3, "master"));
            float zoom = Dev.ArgF(args, 4, 12f);
            string ra = Dev.Arg(args, 5, "human"), rb = Dev.Arg(args, 6, "orc");
            float gap = Dev.ArgF(args, 7, _kind == "beam" ? 7f : _kind == "shot" ? 5f : _kind == "airplan" ? 4f : 1.4f);
            if (ta == Tier.None)
            {
                ta = Tier.Master;
            }
            if (tb == Tier.None)
            {
                tb = Tier.Master;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 10);
            if (c == null)
            {
                Dev.Log("ustage: no ground");
                return;
            }
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _b = Dev.Spawn(rb, World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("ustage: could not spawn");
                return;
            }
            Progression.Promote(_a, ta, false);
            Progression.Promote(_b, tb, false);
            _a.data.favorite = false;
            _b.data.favorite = false;
            _staged.Add(_a.Sid());
            _staged.Add(_b.Sid());
            Director.Claim(_scene, 8f, _a, _b);
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _wait = 1.5f;
            _went = _forced = false;
            Dev.Log("ustage " + _kind + ": " + ta + " " + ra + " #" + _a.Sid() + " vs " + tb + " " + rb + " #" + _b.Sid());
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > _wait)
                {
                    Dev.Log("ustage done (one fell)");
                    End();
                }
                return;
            }
            if (!_went && _t >= _wait)
            {
                _went = true;
                Go();
                return;
            }
            if (_went && _kind == "airplan" && !_forced && Combat.HeightOf(_a) > 0.7f)
            {
                _forced = true;
                Technique t = Techniques.Get("air_intercept");
                Moves.ReleaseHeld();
                bool ok = Brain.Force(_b, t, Mathf.Max(1, Techniques.Level(_b, t)), _a);
                Dev.Log("ustage: " + _a.asset.id + " is in the air (" + Combat.HeightOf(_a).ToString("F2") + "); the intercept: " + ok);
            }
            if (_went && _t >= _wait + 5f)
            {
                Dev.Log("ustage done");
                End();
            }
        }

        private static void Go()
        {
            Fighter fa = Fighters.Get(_a), fb = Fighters.Get(_b);
            fa.ResetCooldowns();
            fb.ResetCooldowns();
            _a.setMana(_a.getMaxMana());
            _b.setMana(_b.getMaxMana());
            Moves.ReleaseHeld();
            if (_kind != "airplan")
            {
                Moves.Hold(_b);   // the defender stands its ground until it answers
            }
            bool ok;
            string what;
            switch (_kind)
            {
                case "hopc":
                    Reactions.DevForce[_b.Sid()] = "duck_sweep";
                    Reactions.DevForce[_a.Sid()] = "hop_strike";
                    ok = Force(_a, "heaven_splitter", _b);
                    what = "heaven_splitter";
                    break;
                case "grab":
                    Reactions.DevForce[_b.Sid()] = "grab_break";
                    ok = Force(_a, "piledriver", _b);
                    what = "piledriver";
                    break;
                case "air":
                    Reactions.DevForce[_b.Sid()] = "air_intercept";
                    ok = HeelFromAbove(_a, _b);
                    what = "a leaping heel drop";
                    break;
                case "airplan":
                    ok = LongLeap(_a, _b);
                    what = "a long leap";
                    break;
                case "beam":
                    Reactions.DevForce[_b.Sid()] = "beam_cleave";
                    ok = Force(_a, "dragon_wave", _b);
                    what = "dragon_wave";
                    break;
                case "shot":
                    Reactions.DevForce[_b.Sid()] = "beam_cleave";
                    ok = KiOrb(_a, _b);
                    what = "a ki orb";
                    break;
                default:
                    Reactions.DevForce[_b.Sid()] = "hop_strike";
                    ok = Sweep(_a, _b);
                    what = "a leg sweep";
                    break;
            }
            if (!Reactions.DevForce.ContainsKey(_a.Sid()))
            {
                Reactions.DevForce[_a.Sid()] = "-";   // (the attacker takes the answer: no counter to it spoils the take)
            }
            Dev.Log("ustage: " + what + " by " + _a.asset.id + ": " + ok);
        }

        private static bool Force(Actor a, string id, Actor v)
        {
            Technique t = Techniques.Get(id);
            if (t == null)
            {
                Dev.Log("ustage: no technique " + id);
                return false;
            }
            return Brain.Force(a, t, Mathf.Max(1, Techniques.Level(a, t)), v);
        }

        /// <summary>A bare attack scripted on the engine (no technique of its own: it shows its blows' forms).</summary>
        private static ChoreoMove Script(Actor a, Actor v)
        {
            Vector2 d = v.current_position - a.current_position;
            Plan p = new Plan { Target = v, Point = v.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Level = 2, Count = 1, Data = true };
            return ChoreoMove.Make(a, p, null);
        }

        private static bool Sweep(Actor a, Actor v)
        {
            ChoreoMove m = Script(a, v);
            if (m == null)
            {
                return false;
            }
            m.StepIn().Strike(new Hit(Blow.Sweep, 1f)).Recover(0.25f);
            return m.Start();
        }

        private static bool HeelFromAbove(Actor a, Actor v)
        {
            ChoreoMove m = Script(a, v);
            if (m == null)
            {
                return false;
            }
            m.Crouch(0.1f).Leap(mm => mm.StandSpot(mm.Stand), 1.3f, 0.4f, 0f, 0.8f)
                .Strike(new Hit(Blow.HeelDrop, 1.2f) { Air = true, Shows = Form.Kick | Form.Aerial | Form.Overhead | Form.Heavy })
                .Recover(0.25f);
            return m.Start();
        }

        private static bool LongLeap(Actor a, Actor v)
        {
            ChoreoMove m = Script(a, v);
            if (m == null)
            {
                return false;
            }
            m.Crouch(0.12f).Leap(mm => mm.StandSpot(mm.Stand), 2.2f, 0.8f).Land().Strike(Blow.Cross, 1f).Recover(0.25f);
            return m.Start();
        }

        private static bool KiOrb(Actor a, Actor v)
        {
            ChoreoMove m = Script(a, v);
            if (m == null)
            {
                return false;
            }
            m.Charge(0.3f, ChargeStyle.Palms, 2f, Element.Ki).Shoot(new ShotSpec { Kind = ShotKind.Orb, Mult = 1f, Speed = 14f, Element = Element.Ki }).Recover(0.5f);
            // (a shot is read by its shooter's move as it lands: a bare script's recovery would show nothing)
            m.Last.Forms = Form.Ki | Form.Projectile;
            return m.Start();
        }

        private static void End()
        {
            if (_a != null)
            {
                Reactions.DevForce.Remove(_a.Sid());
            }
            if (_b != null)
            {
                Reactions.DevForce.Remove(_b.Sid());
            }
            _staged.Clear();
            Director.Release(_scene);
            _t = -1f;
            _a = _b = null;
            Moves.ReleaseHeld();
        }
    }
}
