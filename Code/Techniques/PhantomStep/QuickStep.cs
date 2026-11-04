using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// QUICK STEP (Phantom Step, Novice). The answer to whatever comes in a straight line or sweeps
    /// the ground (a straight, a dash, a line, an area blow): at the last instant it drops low and
    /// darts a tile to the side. A violet afterimage stays where it stood and the attack tears
    /// through it (the image ripples, flashes and comes apart in motes) while the real one skids to a
    /// stop, already facing the attacker. An area blow is stepped out of, away from the attacker.
    /// II: a longer step, ending ready (Close on the attacker: the counter-attack follows at once).
    /// III: a double step, a zig-zag that leaves two images, ending at the attacker's flank with a
    /// palm that knocks it off balance (x0.5, it reels).
    /// Reactive (Answers Line | Dash | Area | Straight), plain straight blows too. Never planned;
    /// forced (dev, god power) it steps aside from the nearest enemy.
    /// </summary>
    internal sealed class QuickStepTech : Technique
    {
        public override string Id => "quick_step";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Evasive;
        public override Form FormsAt(int level) => level >= 3 ? Form.Evasive | Form.Palm | Form.Counter : Form.Evasive;
        public override Form Answers => Form.Line | Form.Dash | Form.Area | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 4f, 6f, 9f };
        public override float[] Cooldown => new[] { 5f, 4.5f, 4f };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return QuickStep.Begin(defender, attacker, level, this, area) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A plain straight blow is stepped aside the same way (Reactions reads it, at a share of the chance).</summary>
        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.6f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            return QuickStep.Begin(defender, attacker, level, this, false);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 8f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && QuickStep.Begin(a, p.Target, p.Level, this, false);
        }
    }

    /// <summary>Quick Step's choreography (also played for a plain blow and a forced demonstration).</summary>
    internal static class QuickStep
    {
        public static bool Begin(Actor d, Actor atk, int level, Technique tech, bool area)
        {
            if (d == null || atk == null || d == atk || !Combat.Living(atk))
            {
                return false;
            }
            PhantomMove m = PhantomMove.Answer(d, atk, tech, level);
            if (m == null)
            {
                return false;
            }
            level = m.Level;
            float k = m.Kk;
            Vector2 from = m.Stance;
            Vector2 n = from - (Vector2)atk.current_position;
            n = n.sqrMagnitude > 1e-4f ? n.normalized : Random.insideUnitCircle.normalized;
            float dist = (level <= 1 ? 0.9f : level == 2 ? 1.15f : 1f) * k;
            if (!PhantomKit.SideSpot(from, n, dist, area ? 0.65f : 0.15f, out Vector2 spot, out float side))
            {
                return false;   // walled in: nowhere to step, the blow lands
            }
            // III: a second step closing in on the attacker's flank (a zig-zag), then a palm
            Vector2 flank = spot;
            bool zig = false;
            if (level >= 3)
            {
                Vector2 ag = atk.current_position;
                Vector2 rel = spot - ag;
                float stand = (0.62f * k) + atk.stats["size"] * 0.3f;
                if (rel.sqrMagnitude > 1e-4f && Vector2.Distance(spot, ag) <= 3.2f * k)
                {
                    Vector2 q = Combat.Clamp(ag + rel.normalized * stand);
                    if (Combat.SafeSpot(q) && Combat.ClearPath(spot, q))
                    {
                        flank = q;
                        zig = true;
                    }
                }
            }
            m.Side2 = side;
            // the afterimage takes its place
            m.Do(_ => m.Decoy = PhantomKit.Decoy(m, 0.3f, false));
            m.Add(PhantomKit.Dart(spot, 0.085f, new Shape(-14f, 1.2f, 0.8f, 0f)))
                .At(0.75f, _ => PhantomKit.Pierce(m, m.Decoy, m.Hue));
            // it skids to a stop, low, facing the attacker
            m.PoseTo(0.07f, new Shape(6f, 1.08f, 0.93f, 0f), Ease.OutQuad, Tempo.Follow).Dusting(0.03f)
                .OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Vector2 v = spot - from;
                        ChoreoFx.Skid(mm.Stance - (v.sqrMagnitude > 1e-4f ? v.normalized : n) * 0.3f * mm.Kk, mm.Stance, mm.Px, mm.A.current_tile);
                    }
                });
            if (zig)
            {
                m.Do(_ => m.Decoy = PhantomKit.Decoy(m, 0.24f, false));
                m.Add(PhantomKit.Dart(flank, 0.07f, new Shape(16f, 1.16f, 0.86f, 0f)));
                m.Strike(new Hit(Blow.Palm, 0.5f) { Hurt = Hurt.Reel, Push = 0.7f, WindScale = 0.45f, ActScale = 0.85f, Shows = Form.Palm | Form.Fast | Form.Counter });
            }
            else if (level >= 2)
            {
                // ready: a glint at the fist, the chain open on the attacker
                m.Hold(0.04f, false, Tempo.Follow).OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.Hand(Limb.Fist) + new Vector2(0f, 1.5f * mm.Px), mm.Px, 0.14f, mm.Hue.Core, 0.8f);
                    }
                });
                m.Leaves(Link.Close);
            }
            m.Recover(0.16f);
            return m.Start();
        }
    }

    // =========================================================================================
    // Shared by the Phantom Step techniques
    // =========================================================================================

    /// <summary>
    /// A Phantom Step choreography: a ChoreoMove that also keeps the state of a feint (did the target bite, which answer did it spend), and can be caught
    /// mid-fake (Exposed: a feint seen through ends open to the counter, like an answered blow).
    /// </summary>
    internal sealed class PhantomMove : ChoreoMove
    {
        private PhantomMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        /// <summary>A new choreography (null if the performer can't act now).</summary>
        public static PhantomMove Make(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new PhantomMove(a, t, p);
        }

        /// <summary>A reactive answer: 'defender' answers 'attacker' (its target) at 'level' (see ChoreoMove.Answer).</summary>
        public static PhantomMove Answer(Actor defender, Actor attacker, Technique t, int level)
        {
            if (defender == null || attacker == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true
            };
            return new PhantomMove(defender, t, p);
        }

        /// <summary>The feint was seen through (an answer made for feints took it): caught mid-fake, open to the counter.</summary>
        public bool Exposed;
        /// <summary>The fake was tried (once per move).</summary>
        public bool Baited;
        /// <summary>The target bit: its answer went into a fake.</summary>
        public bool Bit;
        /// <summary>The answer it spent on the fake (cut short before the real blow lands).</summary>
        public Move Bait;
        /// <summary>The afterimage standing in for it (a decoy).</summary>
        public Particle Decoy;
        /// <summary>Its shadows (silhouette clones sent ahead as baits).</summary>
        public Clone Shadow, Shadow2;
        /// <summary>Scratch for the scripts: a side (+1 left / -1 right), a spot, a count.</summary>
        public float Side2 = 1f;
        public Vector2 Spot, Home;
        public int Count;

        public override bool Interruptible => base.Interruptible || Exposed;

        [SakugaInit]
        private static void Init()
        {
            // caught mid-fake: open to the counter (Interruptible), but not free to start anything of its own
            Hooks.HoldTechniques.Add(a => Moves.TryGet(a, out Move m) && m is PhantomMove pm && pm.Exposed && !pm.Finished);
        }

        /// <summary>Bespoke per-frame work that spans beats (a dance's images), after the beats have played.</summary>
        public System.Action<PhantomMove, float> PerFrame;

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (PerFrame != null && !Finished)
            {
                PerFrame(this, dt);
            }
        }
    }

    /// <summary>The look and the tricks shared by the Phantom Step techniques (violet illusions, feints, decoys).</summary>
    internal static class PhantomKit
    {
        /// <summary>
        /// The violet of an illusion, at alpha 'a': the school's violet at every level (an afterimage must
        /// read as one even at level I, where the rest of the palette is white), the tier in its edge.
        /// </summary>
        public static Color Illusion(ChoreoMove m, float a)
        {
            Color c = Color.Lerp(m.Hue.Main, m.Hue.Edge, 0.3f);
            if (m.Level <= 1)
            {
                c = Color.Lerp(c, Schools.Color(m.School), 0.55f);
            }
            return Fx.WithA(c, a);
        }

        /// <summary>
        /// A spot 'dist' tiles to one side of a blow coming along 'n' (attacker to defender), 'away'
        /// of the step going further away from the attacker; the side with safe ground and a clear
        /// path (a random one first), a shorter step if neither is free. False: walled in.
        /// </summary>
        public static bool SideSpot(Vector2 from, Vector2 n, float dist, float away, out Vector2 spot, out float side)
        {
            Vector2 perp = Choreo.Perp(n);
            float first = Random.value < 0.5f ? 1f : -1f;
            for (int pass = 0; pass < 2; pass++)
            {
                float d = pass == 0 ? dist : dist * 0.6f;
                for (int i = 0; i < 2; i++)
                {
                    float s = i == 0 ? first : -first;
                    Vector2 dir = (perp * s + n * away).normalized;
                    Vector2 q = Combat.Clamp(from + dir * d);
                    if (Combat.SafeSpot(q) && Combat.ClearPath(from, q))
                    {
                        spot = q;
                        side = s;
                        return true;
                    }
                }
            }
            spot = from;
            side = first;
            return false;
        }

        /// <summary>
        /// A dart along the ground to 'to': low, fast, untouchable, a stream of violet afterimages and a
        /// speed line; heel dust where it leaves. Shows Evasive.
        /// </summary>
        public static Beat Dart(Vector2 to, float dur, Shape pose)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = m => to,
                To = pose,
                Ease = Ease.OutQuad,
                Intangible = true,
                Ghosts = 0.02f,
                Forms = Form.Evasive
            };
            b.Enter = m =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Vector2 v = to - m.Stance;
                    ChoreoFx.HeelDust(m.Stance, -v, m.Px, m.A.current_tile, m.Kk);
                    if (v.sqrMagnitude > 0.01f)
                    {
                        Kit.SpeedLines(Vector2.Lerp(m.Stance, to, 0.5f) + new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale), v, m.Px, 3, 3f, 0.12f, Illusion(m, 0.85f));
                    }
                }
            };
            return b;
        }

        /// <summary>
        /// An afterimage of the performer as it stands now, in violet, standing in its place for 'life'
        /// seconds (it takes the blow meant for it). 'solid': nearly opaque (a decoy that must fool).
        /// </summary>
        public static Particle Decoy(ChoreoMove m, float life, bool solid)
        {
            if (!m.Vis)
            {
                return null;
            }
            Particle p = Fx.Afterimage(m, life, Illusion(m, solid ? 0.95f : 0.82f), Illusion(m, solid ? 0.9f : 0.75f), Illusion(m, solid ? 0.7f : 0.5f), Fx.WithA(m.Hue.Edge, 0f));
            if (p != null)
            {
                p.R.sortingOrder += 2;
            }
            return p;
        }

        /// <summary>The attack tears through the decoy: it flashes white, ripples, and comes apart in violet motes.</summary>
        public static void Pierce(ChoreoMove m, Particle decoy, Hue hue)
        {
            if (decoy == null || !decoy.Alive || !Kit.Visible(decoy.Pos))
            {
                return;
            }
            float px = m.Px;
            Vector2 chest = decoy.Pos + new Vector2(0f, 1f * px * m.Kk);
            decoy.Color(new Color(1f, 1f, 1f, 0.9f), Illusion(m, 0.6f), Fx.WithA(hue.Edge, 0f));
            decoy.Life = decoy.Age + 0.12f;
            Fx.Glint(chest, px, 0.12f, Color.white, 0.8f);
            Fx.Ring(chest, px, 0.18f, 2f, 7f * m.Kk, 1, 1, FxLayer.Over, 8, 0.6f, 2.2f, Fx.WithA(Color.white, 0.8f), Illusion(m, 0f));
            Motes(chest, px, 7, 5f * m.Kk, 0.32f, hue.Core, Illusion(m, 1f));
        }

        /// <summary>Motes bursting outward from 'at' and drifting up (an illusion coming apart).</summary>
        public static void Motes(Vector2 at, float px, int n, float speedPx, float life, Color c0, Color c1)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f);
                Particle p = Fx.Spawn(Art.Pixel, at + d * px, px, life * Random.Range(0.75f, 1.2f), FxLayer.Over, 9);
                p?.Moving(d * Random.Range(0.6f, 1.2f) * speedPx * px * 3f + new Vector2(0f, 6f * px), 0f, 3f)
                    .Sized(1.4f, 0.6f).Color(Fx.WithA(c0, 0.95f), Fx.WithA(c1, 0.7f), Fx.WithA(c1, 0f));
            }
        }

        /// <summary>A glass-like burst of the decoy: shards fly, a ring, a glint (a mirage shattered by a blow).</summary>
        public static void Shatter(ChoreoMove m, Vector2 ground, float scale)
        {
            Vector2 at = ground + new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale);
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(ground), 0.05f);
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = m.Px;
            int n = Fx.Count(Mathf.RoundToInt(10 * scale));
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), ground, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 12);
                if (p == null)
                {
                    break;
                }
                p.Moving(d * Random.Range(40f, 85f) * px * scale, Random.Range(10f, 40f) * px, 2f, 200f * px);
                p.Z = at.y - ground.y;
                p.Spin = Random.Range(-600f, 600f);
                p.Bounce = 0.2f;
                p.Color(Color.white, Illusion(m, 0.95f), Fx.WithA(m.Hue.Edge, 0f));
            }
            Fx.Ring(at, px, 0.2f, 2f, 9f * m.Kk * scale, 1, 1, FxLayer.Top, 5, 1f, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            Fx.Glint(at + new Vector2(0f, 2f * px), px, 0.18f, Color.white, scale);
        }

        /// <summary>
        /// Violet smoke billowing out from 'at' to fill 'r' tiles (it hangs and thins over about 'life'
        /// seconds). Most of it rolls along the ground behind the bodies, a few wisps drift over them,
        /// so what happens inside the cloud still reads. 'size': the puffs' size (1 a veil's).
        /// </summary>
        public static void Smoke(ChoreoMove m, Vector2 at, float r, int n, float life, float size = 1f)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return;
            }
            float px = m.Px;
            float sk = Mathf.Sqrt(m.Kk) * size;
            Color deep = Color.Lerp(m.Hue.Edge, new Color(0.24f, 0.17f, 0.32f, 1f), 0.4f);
            Color pale = Color.Lerp(Illusion(m, 1f), Color.white, 0.3f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float u = Mathf.Sqrt(Random.value);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 start = at + d * u * r * 0.3f;
                float l = life * Random.Range(0.7f, 1.15f);
                bool over = i % 4 == 3;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, Art.Dust.Length)], start, px, l, over ? FxLayer.Top : FxLayer.Behind, over ? 4 : 1);
                if (p == null)
                {
                    break;
                }
                // (out to its place in the cloud in its first moments, then it hangs and thins)
                p.Moving(d * u * r * Random.Range(2.6f, 3.2f), Random.Range(1f, 3f) * px, 4.5f);
                p.Z = Random.Range(0f, 4f) * px * m.Kk;
                p.Sized(Random.Range(0.8f, 1.05f) * sk, Random.Range(1.3f, 1.7f) * sk);
                float al = over ? 0.38f : 0.72f;
                p.Color(Fx.WithA(pale, al), Fx.WithA(Color.Lerp(pale, deep, 0.45f), al * 0.9f), Fx.WithA(deep, al * 0.6f), Fx.WithA(deep, 0f));
            }
        }

        /// <summary>
        /// The ground under the struck foe cracks when 'h' lands (a crater of base radius 'r' at the
        /// move's Scale: Ruin, so Master+ blows break the buildings there). No fire: these are blows of
        /// speed, not of flame (the engine's own Hit.Crater may set the grass alight round the user).
        /// </summary>
        public static Hit Crack(Hit h, float r)
        {
            if (r <= 0f)
            {
                return h;
            }
            System.Action<ChoreoMove, Actor, HitResult> was = h.OnLand;
            h.OnLand = (m, v, res) =>
            {
                was?.Invoke(m, v, res);
                if (v == null)
                {
                    return;
                }
                Vector2 g = Victims.GroundOf(v);
                if (m.Vis)
                {
                    Kit.Crater(g, m.Px * m.Kk, Combat.TileAt(g), false, 5f, r < 1.2f, m.Hue.Edge);
                }
                Ruin.Blast(m.A, g, r, m.Scale, m.Pose.Ground, false);
            };
            return h;
        }

        // ------------------------------------------------------------- feints --

        /// <summary>
        /// The fake reaches the target: it reads what the move shows right now (the false form) and may
        /// answer it. 0: it didn't bite (the fake flows into the real blow); 1: it bit (its answer is spent
        /// on nothing: the move sways out of it untouchable and Punish catches it flat-footed); 2: it saw
        /// through the feint (an answer made for feints took it: the move is caught mid-fake, Exposed,
        /// and goes to its recovery open to the counter).
        /// </summary>
        public static int Bait(PhantomMove m)
        {
            Actor v = m.V;
            if (m.Baited || v == null || !m.VOk || Moves.Intangible(v))
            {
                return 0;
            }
            m.Baited = true;
            bool stunned = m.A.hasStatusStunned();
            Contact c = Combat.Reach(m, v, false);
            if (c == Contact.Hit)
            {
                return 0;
            }
            Move ans = null;
            if (Moves.TryGet(v, out Move mv) && mv.A == v && !(mv is Puppet) && !mv.Ended)
            {
                ans = mv;
            }
            if (ans != null && ans.Tech != null && (ans.Tech.Answers & Form.Feint) != 0)
            {
                // read: the counter to the feint is already coming
                m.Exposed = true;
                if (Dev.On) Dev.Trace("phantom " + m.Id + " feint seen through by " + v.asset.id + "#" + v.Sid() + " (" + ans.Tech.Id + ")");
                m.JumpToRecovery();
                return 2;
            }
            m.Bit = true;
            m.Bait = ans;
            if (!stunned && m.A.hasStatusStunned())
            {
                m.A.finishStatusEffect("stunned");   // (a parry spent on a fake throws nothing off its stroke)
            }
            if (Fighters.TryGet(v, out Fighter f))
            {
                FoeNotes notes = f.NotesOn(m.A);
                if (notes != null)
                {
                    notes.Fooled++;
                }
            }
            if (m.Vis)
            {
                Kit.Mark(v, new Color(0.84f, 0.84f, 0.9f, 1f), 0.3f, 0.8f);
            }
            if (Dev.On) Dev.Trace("phantom " + m.Id + " feint: " + v.asset.id + "#" + v.Sid() + " bit (" + (ans != null && ans.Tech != null ? ans.Tech.Id : c.ToString()) + ")");
            return 1;
        }

        /// <summary>
        /// The real blow is coming: an answer the target spent on the fake is cut short (it bit on
        /// nothing) and it stands flat-footed for a moment (dazed 'daze' s: it can't read the real one).
        /// </summary>
        public static void Punish(PhantomMove m, float daze)
        {
            if (!m.Bit || !m.VOk)
            {
                return;
            }
            Actor v = m.V;
            if (m.Bait != null && !m.Bait.Ended && Moves.TryGet(v, out Move cur) && cur == m.Bait && !cur.Cinematic)
            {
                Choreo.Kill(cur);
            }
            m.Bait = null;
            if (!Moves.Busy(v))
            {
                Combat.Daze(v, daze);
            }
        }

        /// <summary>A sway out of the line of the answer the target threw at the fake: untouchable, low, a violet blur.</summary>
        public static Beat Sway(float dur, float side)
        {
            return new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                To = new Shape(-16f, 1.12f, 0.86f, 0f, -0.12f, 0.16f * side),
                Ease = Ease.OutQuad,
                Intangible = true,
                Ghosts = 0.025f,
                Forms = Form.Evasive | Form.Feint
            };
        }
    }

    // =========================================================================================
    // Dev staging for the Phantom Step answers and feints
    // =========================================================================================

    /// <summary>
    /// psstage ATTACK ANSWER [tierA] [tierB] [zoom] [raceA] [raceB] [gap]: two fighters on a clean
    /// meadow 'gap' tiles apart; A attacks B with technique ATTACK (forced) and B answers only with
    /// ANSWER (it always reads: Reactions.DevForce). For the reactive techniques (jab_cross into
    /// quick_step) and the feints (feint_jab into duck_sweep: B bites; into reversal: B sees through).
    /// A walks ATTACK's school, B walks ANSWER's. Logs "psstage done" after 5 s.
    /// </summary>
    internal static class PhantomStage
    {
        private static Actor _a, _b;
        private static float _t = -1f;
        private static bool _went;
        private static string _atk, _ans;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["psstage"] = Command;
            Hooks.FrameTick += dt =>
            {
                if (_t < 0f)
                {
                    return;
                }
                try
                {
                    Tick();
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("psstage", e);
                    End();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _atk = Dev.Arg(args, 1, "jab_cross");
            _ans = Dev.Arg(args, 2, "quick_step");
            Technique ta = Techniques.Get(_atk), tb = Techniques.Get(_ans);
            if (ta == null || tb == null)
            {
                Dev.Log("psstage: unknown technique " + (ta == null ? _atk : _ans));
                return;
            }
            Tier tierA = Dev.ParseTier(Dev.Arg(args, 3, "expert"));
            Tier tierB = Dev.ParseTier(Dev.Arg(args, 4, "expert"));
            float zoom = Dev.ArgF(args, 5, 12f);
            string ra = Dev.Arg(args, 6, "human"), rb = Dev.Arg(args, 7, "orc");
            float gap = Dev.ArgF(args, 8, 1.6f);
            if (tierA == Tier.None)
            {
                tierA = Tier.Expert;
            }
            if (tierB == Tier.None)
            {
                tierB = Tier.Expert;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 8);
            if (c == null)
            {
                Dev.Log("psstage: no ground");
                return;
            }
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _b = Dev.Spawn(rb, World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("psstage: could not spawn");
                return;
            }
            Progression.Promote(_a, tierA, false);
            Progression.Promote(_b, tierB, false);
            _a.data.favorite = false;
            _b.data.favorite = false;
            if (ta.School != School.None && Schools.Trait(ta.School) != null)
            {
                _a.addTrait(Schools.Trait(ta.School), true);
            }
            if (tb.School != School.None && Schools.Trait(tb.School) != null)
            {
                _b.addTrait(Schools.Trait(tb.School), true);
            }
            _staged.Add(_a.Sid());
            _staged.Add(_b.Sid());
            Director.Claim(_scene, 8f, _a, _b);
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _went = false;
            Dev.Log("psstage " + _atk + " -> " + _ans + ": " + tierA + " " + ra + " #" + _a.Sid() + " vs " + tierB + " " + rb + " #" + _b.Sid());
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > 1.5f)
                {
                    Dev.Log("psstage done (one fell)");
                    End();
                }
                return;
            }
            if (!_went && _t >= 1.5f)
            {
                _went = true;
                Go();
                return;
            }
            if (_went && _t >= 6.5f)
            {
                Dev.Log("psstage done");
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
            Reactions.DevForce[_b.Sid()] = _ans;
            Technique t = Techniques.Get(_atk);
            Moves.ReleaseHeld();
            Moves.Hold(_b);   // the defender stands its ground until it answers
            int level = Mathf.Max(1, Techniques.Level(_a, t));
            bool ok = Brain.Force(_a, t, level, _b);
            int lb = Techniques.Level(_b, Techniques.Get(_ans));
            Dev.Log("psstage: " + _atk + " " + Techniques.Roman(level) + " by " + _a.asset.id + ": " + ok + "; " + _ans + " known at " + lb);
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
