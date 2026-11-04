using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TIME SKIP (Phantom Step, Master): THE interrupt. Somewhere up to ten tiles away an enemy plants
    /// its feet and starts gathering power (a beam between its palms, a haymaker cocked, an orb over
    /// its head)... and the world stutters. One frame the Phantom Step master is where it was, the
    /// next it is already beside the charger with its leg cocked: no dash, no travel, only the torn
    /// frame it left behind (its body split three ways in violet, white and its tier's colour,
    /// horizontal tears flicking across, a clock dial on the ground) and the world holding its
    /// breath for a few hundredths. Then the side kick: the charge bursts out of the charger
    /// (Interrupts.Break, the shared interrupt path: motes scattering, its ring breaking) and it is
    /// sent flying. A beam already pouring out is cut off at its source (Choreo.Break: it collapses).
    ///
    /// Proactive: planned the moment a foe within reach is charging or firing a beam (the Brain
    /// weighs Interrupt x3.2 against a charging foe, and this answers Charge). Reactive: a beam or a
    /// charged blow reaching it is met the same way (the beam passes through the frame it left).
    ///
    /// Levels: I (Master) reaches 10 tiles; one kick (x1.6), the charger flies ~3.5 tiles.
    ///         II reaches 11.5; the skipped time already held a knee to the gut: its frame hangs
    ///            beside the foe when the world resumes (x2 in all), a dent in the ground.
    ///         III reaches 13; three skipped blows hang round the foe (knee, elbow, palm), all of
    ///            them land in the instant of the kick (x3), a wider crater, the charger flies ~6.
    /// </summary>
    internal sealed class TimeSkipTech : Technique
    {
        public override string Id => "time_skip";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override bool Reactive => true;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Interrupt | Form.Kick;
        public override Form Answers => Form.Charge | Form.Beam;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 26f, 40f };
        public override float[] Cooldown => new[] { 9f, 8f, 7f };

        /// <summary>How far the skip reaches (tiles, for a human; a bigger fighter a little further).</summary>
        public static float Range(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 11.5f : 13f;
        }

        /// <summary>The kick's damage, the skipped blows included (x the user's damage).</summary>
        public static float Mult(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2f : 3f;
        }

        /// <summary>How far the broken charger flies (Interrupts.Break's force: a Choreo charger flies 1.5x that in tiles).</summary>
        private static float Force(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3f : 4f;
        }

        private static float Reach(Actor self, int level)
        {
            return Range(level) * Mathf.Lerp(1f, Choreo.KOf(self), 0.5f);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float range = Reach(c.Self, level);
            if (c.Forced)
            {
                Actor t = c.ForceTarget ?? c.Focus;
                if (t == null || !Choreo.Fair(c.Self, t, out _, true))
                {
                    t = c.Enemies.Count > 0 ? c.Enemies[0] : null;
                }
                if (t == null || c.Dist(t) > range + 4f || !Combat.Grounded(t) || !PsHiKit.SpotBeside(t, c.Pos, 0.7f * k, out _))
                {
                    return null;
                }
                return new Plan { Target = t, Point = t.current_position, Dir = PsHiKit.DirTo(c.Pos, t.current_position), Count = 1, Score = 1f, Data = true };
            }
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                float d = c.EnemyDist[i];
                if (d > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                bool charging = Moves.Charging(v);
                bool firing = !charging && Beams.Of(v) != null;
                if (!charging && !firing)
                {
                    continue;   // only a wind-up or a beam calls for it: it is the answer, not an opener
                }
                if (!Choreo.Fair(c.Self, v, out float cm) || v.isFlying() || Combat.HeightOf(v) > 0.5f
                    || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                if (!PsHiKit.SpotBeside(v, c.Pos, 0.7f * k + v.stats["size"] * 0.3f, out _))
                {
                    continue;
                }
                // breaking a charge is worth the blow it stops (the charger's power and then some), and the kick
                float score = c.D * 3f + 30f + 10f * Tiers.Power(v) + c.Value(v, c.Expected(v, Mult(level)), cm);
                if (firing)
                {
                    score += 40f;   // a beam already pouring out: cut it off at its source
                }
                if (d > 4f)
                {
                    score += 15f;   // too far for any other answer to arrive in time
                }
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                return null;
            }
            return new Plan { Target = best, Point = best.current_position, Dir = PsHiKit.DirTo(c.Pos, best.current_position), Count = 1, Score = bestScore };
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || incoming == null || attacker == null || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            // the one gathering (or pouring out a beam) must be within the skip's reach, on the ground,
            // with somewhere to stand beside it
            if (!Combat.Living(attacker) || attacker.isFlying() || Combat.HeightOf(attacker) > 0.5f
                || Vector2.Distance(defender.current_position, attacker.current_position) > Reach(defender, level) + 1f
                || !Choreo.Fair(defender, attacker, out _, true)
                || !PsHiKit.SpotBeside(attacker, defender.current_position, 0.7f * Choreo.KOf(defender), out _))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            ChoreoMove m = ChoreoMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            Build(m, true);
            return m.Start() ? Contact.Evaded : Contact.Hit;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            Build(m, false);
            return m.Start();
        }

        /// <summary>What one skip carries between its beats: the frames of the skipped blows hanging by the foe.</summary>
        private sealed class State
        {
            public readonly List<Particle> Frames = new List<Particle>(3);
            public readonly List<Particle> Stars = new List<Particle>(3);
            public readonly List<Vector2> At = new List<Vector2>(3);
        }

        private static void Build(ChoreoMove m, bool react)
        {
            int level = m.Level;
            State st = new State();
            // no wind-up worth the name (that is the point): a breath, and its eyes flash
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = react ? 0.03f : 0.06f,
                To = new Shape(-6f, 1.07f, 0.93f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = Form.Teleport | Form.Interrupt,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.HeadPos() + new Vector2(mm.Fwd * 0.6f * mm.Px, -0.8f * mm.Px), mm.Px, 0.12f, mm.Hue.Core, 0.7f);
                    }
                }
            });
            // the world stutters: it is simply there
            m.Do(mm => Skip(mm, st));
            // the kick (it arrived already cocked: a short wind)
            PsHiKit.Blow(m, Blow.SideKick, 0.55f, 0.85f, mm => Land(mm, st), Form.Kick | Form.Interrupt | Form.Teleport, 1.4f);
            m.Hold(0.07f + 0.02f * level, true, Tempo.Impact);
            m.Recover(0.22f);
        }

        /// <summary>The skip: what it leaves (a torn frame), where it arrives (beside the foe, already cocked), the stutter.</summary>
        private static void Skip(ChoreoMove m, State st)
        {
            if (!m.VOk)
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            Vector2 from = m.Stance;
            // from afar it arrives on the near side; from close by it is suddenly at the foe's back
            bool close = Vector2.Distance(from, m.VG) < 2.5f * m.Kk;
            Vector2 to;
            if (!(close ? PsHiKit.SpotBehind(v, from, m.Stand, out to) : PsHiKit.SpotBeside(v, from, m.Stand, out to)))
            {
                m.Lose();
                return;
            }
            float px = m.Px, k = m.Kk;
            int level = m.Level;
            if (m.Vis)
            {
                // the frame it stood in, torn: three of it split apart, tears, a clock dial
                PsHiKit.Split(m, 0.34f, 2.5f);
                PsHiKit.Tear(m.BodyPos(), px, k, m.Hue, 6 + 3 * level, 20f);
                PsHiKit.Dial(from, px, 4f * k, 13f * k, 0.42f, m.Hue.Edge);
            }
            m.Stance = to;
            m.Pose.Ground = to;
            m.Face(m.VG - to);
            // already cocked: the skipped time was spent arriving
            BlowShape kick = ChoreoMove.ShapeOf(Blow.SideKick);
            m.Pose.Rot = kick.Wind.Rot;
            m.Pose.SX = kick.Wind.SX;
            m.Pose.SY = kick.Wind.SY;
            m.Vis = Fx.OnScreen(to, 0.35f);
            if (m.Vis)
            {
                PsHiKit.Split(m, 0.12f, 3.5f);
                PsHiKit.Tear(m.BodyPos(), px, k, m.Hue, 4 + 2 * level, 14f);
                Fx.Ring(to, px, 0.2f, 2f, 10f * k, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
            if (level >= 2)
            {
                SkippedBlows(m, st, level == 2 ? 1 : 3);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            // the world holds still for a few frames: the stutter
            Kit.Stop(to, 0.045f + 0.015f * level);
            Kit.Shake(to, 0.1f, 0.0025f, k);
            Combat.Pin(v, 0.6f);
            m.WatchMe();
        }

        /// <summary>
        /// The blows the skipped time held: frames of it frozen mid-blow round the foe (a knee, an elbow,
        /// a palm), each with its hit star hanging where it landed. They resolve with the kick.
        /// </summary>
        private static void SkippedBlows(ChoreoMove m, State st, int n)
        {
            if (!m.Vis || m.V == null)
            {
                return;
            }
            Blow[] blows = { Blow.Knee, Blow.Elbow, Blow.Palm };
            Pose keep = m.Pose;
            Vector2 vg = m.VG;
            Vector2 toMe = m.Stance - vg;
            float baseAng = toMe.sqrMagnitude > 1e-4f ? Mathf.Atan2(toMe.y, toMe.x) : 0f;
            float stand = m.Stand * 1.15f;
            // (frames of itself in the school's violet, half there: time that was skipped)
            Color c = Fx.WithA(Color.Lerp(m.Hue.Main, m.Hue.Edge, 0.35f), 0.62f);
            for (int j = 0; j < n; j++)
            {
                float side = (j & 1) == 0 ? 1f : -1f;
                float a = baseAng + side * (1.75f + 0.55f * (j / 2));
                Vector2 spot = vg + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * stand;
                BlowShape s = ChoreoMove.ShapeOf(blows[j % blows.Length]);
                m.Pose.Ground = spot;
                m.Pose.FaceRight = vg.x >= spot.x;
                m.Pose.Rot = s.Hit.Rot;
                m.Pose.SX = s.Hit.SX;
                m.Pose.SY = s.Hit.SY;
                m.Pose.Height = s.Hit.H * m.Kk;
                Particle p = PsHiKit.Still(m, c, 1);
                if (p != null)
                {
                    st.Frames.Add(p);
                }
                Vector2 hit = Choreo.BodyAt(m.V, Mathf.Clamp(s.Contact, 0.3f, 0.85f)) + (spot - vg).normalized * 1.5f * m.Px;
                Particle star = Fx.Spawn(Art.HitStars[0], hit, m.Px, 1f, FxLayer.Top, 20);
                if (star != null)
                {
                    star.Rot = Random.Range(0f, 90f);
                    star.Color(Fx.WithA(Color.Lerp(Color.white, m.Hue.Core, 0.4f), 0.85f));
                    m.Glue(star);
                    st.Stars.Add(star);
                    st.At.Add(hit);
                }
            }
            m.Pose = keep;
        }

        /// <summary>The kick lands: the charge is broken (the shared interrupt path), the foe flies, the skipped blows resolve.</summary>
        private static void Land(ChoreoMove m, State st)
        {
            int level = m.Level;
            if (!m.VOk)
            {
                PsHiKit.Whiff(m);
                Resolve(m, st, false);
                m.Lose();
                return;
            }
            Actor v = m.V;
            float reach = m.Stand + 0.7f * m.Kk + v.stats["size"] * 0.5f + 0.35f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach + 0.6f)
            {
                PsHiKit.Whiff(m);
                Resolve(m, st, false);
                return;
            }
            if (!m.Touch(v))
            {
                Resolve(m, st, false);
                return;   // answered: the engine plays the recoil or the overreach
            }
            Vector2 dir = PsHiKit.DirTo(m.Stance, m.VG, m.Dir);
            bool charging = Moves.Charging(v);
            bool firing = !charging && Beams.Of(v) != null;
            bool broke = false;
            if (charging)
            {
                broke = Interrupts.Break(v, m.A, dir, Force(level));
            }
            else if (firing)
            {
                broke = Choreo.Break(v, m.A, dir, Force(level) * 1.5f);   // (a beam's firing is no charge: its source is cut off)
            }
            Hit h = new Hit(Blow.SideKick, Mult(level))
            {
                Hurt = Hurt.Launch,
                Push = 1.6f + 0.8f * level,
                Lift = 0.9f + 0.4f * level,
                Finisher = true,
                Lethal = true,
                Power = 1.1f + 0.15f * level
            };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.SideKick), m.ContactPoint(0.5f), dir);
            Resolve(m, st, true);
            Vector2 at = m.VG;
            if (broke && m.Vis)
            {
                // the gathered power's clock stops: a dial bursts outward from the charger
                Vector2 body = Choreo.BodyAt(v, 0.5f);
                PsHiKit.Dial(at, m.Px, 5f * m.Kk, 22f * m.Kk, 0.32f, m.Hue.Core, FxLayer.Top);
                PsHiKit.Shards(body, at, m.Px, m.Kk, m.Hue, 8);
                Fx.FocusLines(body, m.Px, Fx.Count(6), 8f * m.Kk, 14f * m.Kk, 0.1f);
            }
            if (level >= 2)
            {
                // a Master's kick dents the earth (and whatever stands on it)
                if (m.Vis)
                {
                    Kit.Crater(at, m.Px * m.Kk, Combat.TileAt(at), false, 4f, true, m.Hue.Edge);
                }
                Ruin.Blast(m.A, at, level == 2 ? 0.5f : 0.85f, m.Scale, m.Pose.Ground);
            }
        }

        /// <summary>The skipped blows resolve in the instant of the kick: their stars burst, their frames fade.</summary>
        private static void Resolve(ChoreoMove m, State st, bool landed)
        {
            for (int i = 0; i < st.Stars.Count; i++)
            {
                if (landed && m.Vis)
                {
                    Vector2 at = st.At[i];
                    ChoreoFx.Impact(at, (at - m.VG).sqrMagnitude > 1e-4f ? m.VG - at : m.Dir, m.Px, m.Kk, 1f, m.Hue, m.TopLayer);
                }
                m.Unglue(st.Stars[i]);
            }
            for (int i = 0; i < st.Frames.Count; i++)
            {
                PsHiKit.Fade(m, st.Frames[i], landed ? 0.2f : 0.1f, Fx.WithA(m.Hue.Ghost, 0.7f));
            }
            st.Stars.Clear();
            st.Frames.Clear();
            st.At.Clear();
        }
    }

    /// <summary>
    /// Shared pieces of the Phantom Step masters' techniques (Time Skip, Ghost Pierce, Thousand Steps,
    /// Phantom Legion, Afterimage Prison, Wraith Walk, Horizon Step, Eternal Second, Hall of Mirrors):
    /// where to stand by a foe, the torn frame time leaves behind, afterimages frozen where they were,
    /// blows whose landing is the technique's own, armour a phantom passes through.
    /// </summary>
    internal static class PsHiKit
    {
        // ------------------------------------------------------------- places --

        /// <summary>The unit direction from a to b ('fallback' when they coincide).</summary>
        public static Vector2 DirTo(Vector2 a, Vector2 b, Vector2? fallback = null)
        {
            Vector2 d = b - a;
            return d.sqrMagnitude > 1e-6f ? d.normalized : fallback ?? Vector2.right;
        }

        /// <summary>A safe spot 'stand' tiles from 'v' on the side facing 'from' (else round it); false if there is none.</summary>
        public static bool SpotBeside(Actor v, Vector2 from, float stand, out Vector2 spot)
        {
            Vector2 g = Victims.GroundOf(v);
            Vector2 d = from - g;
            return SpotAround(g, d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.y, d.x) : 0f, stand, out spot);
        }

        /// <summary>A safe spot 'stand' tiles behind 'v' (its far side, seen from 'from'), else round it.</summary>
        public static bool SpotBehind(Actor v, Vector2 from, float stand, out Vector2 spot)
        {
            Vector2 g = Victims.GroundOf(v);
            Vector2 d = g - from;
            return SpotAround(g, d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.y, d.x) : 0f, stand, out spot);
        }

        /// <summary>A safe spot 'stand' tiles from 'g' at angle 'ang' (radians), else the nearest angle round it that is.</summary>
        public static bool SpotAround(Vector2 g, float ang, float stand, out Vector2 spot)
        {
            for (int i = 0; i <= 7; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    if (i == 0 && s > 0)
                    {
                        continue;
                    }
                    float a = ang + s * i * 0.42f;
                    Vector2 q = Combat.Clamp(g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * stand);
                    if (Combat.SafeSpot(q) || Choreo.Wading(q))
                    {
                        spot = q;
                        return true;
                    }
                }
            }
            spot = g;
            return false;
        }

        /// <summary>Orders 'list' as a short path starting from 'from' (nearest neighbour next).</summary>
        public static void Path(List<Actor> list, Vector2 from)
        {
            Vector2 at = from;
            for (int i = 0; i < list.Count; i++)
            {
                int best = i;
                float bd = float.MaxValue;
                for (int j = i; j < list.Count; j++)
                {
                    float d = (list[j].current_position - at).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = j;
                    }
                }
                Actor t = list[i];
                list[i] = list[best];
                list[best] = t;
                at = list[i].current_position;
            }
        }

        /// <summary>A blow's multiplier against 'v' with its armour ignored (a phantom passes through it), at most x'cap'.</summary>
        public static float Pierce(Actor v, float cap = 3f)
        {
            if (v == null)
            {
                return 1f;
            }
            float armor = Mathf.Min(v.stats["armor"], 95f);
            return Mathf.Min(cap, 1f / Mathf.Max(0.05f, 1f - armor / 100f));
        }

        /// <summary>Is 'v' a body a phantom's blow can reach where it stands (on the ground, not swimming)?</summary>
        public static bool Standing(Actor v)
        {
            return v != null && Combat.Grounded(v) && (!v.isInLiquid() || Choreo.Wading(v.current_position));
        }

        // -------------------------------------------------------------- blows --

        /// <summary>
        /// A blow of shape 'b' whose landing is the technique's own ('contact' runs at the blow's moment):
        /// the wind-up, the strike (the body snaps into the hit pose, the limb's smear, the body blurred
        /// back along it), the follow-through (held 'hold' times as long). For blows that must do what the
        /// engine's Strike doesn't (break a charge before it lands, cut through armour).
        /// </summary>
        public static ChoreoMove Blow(ChoreoMove m, Blow b, float windScale, float actScale, Action<ChoreoMove> contact, Form shows, float hold = 1f)
        {
            BlowShape s = ChoreoMove.ShapeOf(b);
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = s.WindDur * windScale, To = s.Wind, Ease = Ease.OutQuad, Forms = shows });
            Beat act = new Beat { Tempo = Tempo.Act, Dur = s.ActDur * actScale, To = s.Hit, Ease = Ease.OutExpo, Forms = shows };
            act.Enter = mm => Smear(mm, s);
            act.On(s.At, contact);
            m.Add(act);
            Shape relax = new Shape(s.Hit.Rot * (1f - s.Relax), Mathf.Lerp(s.Hit.SX, 1f, s.Relax), Mathf.Lerp(s.Hit.SY, 1f, s.Relax), 0f, s.Hit.Lunge * 0.85f);
            return m.Add(new Beat { Tempo = Tempo.Follow, Dur = s.FollowDur * hold, To = relax, Ease = Ease.OutQuad, Forms = shows, Track = false });
        }

        /// <summary>A blow's smear (an arc swoosh or a straight streak with the limb's head) and the body's blur back along it.</summary>
        public static void Smear(ChoreoMove m, BlowShape s)
        {
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            float life = m.BeatDur * 1.6f + 0.03f;
            if (s.ArcR > 0)
            {
                float offY = s.ArcOff.y < -50f ? -m.Pose.PivotUp + 1.5f : s.ArcOff.y;
                int r = Mathf.Clamp(Mathf.RoundToInt(s.ArcR * Mathf.Lerp(1f, m.Kk, 0.6f) * (m.Level >= 3 ? 1.15f : 1f)), 4, 60);
                int thick = s.ArcThick + (m.Level >= 3 && s.ArcThick < 4 ? 1 : 0);
                Kit.Swish(m, Art.Arc(r, s.ArcRatio, s.A0, s.A1, thick), life, new Vector2(s.ArcOff.x * m.Fwd, offY), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
            }
            if (s.Straight)
            {
                Vector2 from = m.Root(s.Limb);
                Vector2 to = m.V != null ? Vector2.Lerp(m.ContactPoint(s.Contact), from, 0.15f) : from + m.Dir * m.Stand;
                ChoreoFx.Smear(from, to, px, life, Mathf.Min(s.SmearHead + (m.Level >= 3 ? 1 : 0), 3), s.SmearThick, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
                if (s.Weight >= 1f)
                {
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), m.Dir, px, 2, 2.5f, 0.08f, Fx.WithA(m.Hue.Edge, 0.75f), FxLayer.Over);
                }
            }
            Fx.Ghost(m, -m.Dir * 2f * px, -6f, 0.07f, Fx.WithA(m.Hue.Ghost, 0.55f), m.Hue.GhostEnd);
        }

        /// <summary>A blow that meets nothing: a white swish through the air and a whoosh.</summary>
        public static void Whiff(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), m.Hand(Limb.Fist) + new Vector2(m.Fwd * 2f * m.Px, 0f), m.Px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = m.Fwd;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        // ------------------------------------------------------------ images --

        /// <summary>
        /// An afterimage of the performer as it is drawn now that stays where it is, in colour 'c' (held:
        /// it dies with the move, or when it is faded with Fade). Null off screen.
        /// </summary>
        public static Particle Still(ChoreoMove m, Color c, int order = 0)
        {
            if (!m.Vis)
            {
                return null;
            }
            Particle p = Fx.Afterimage(m, 1f, c);
            if (p == null)
            {
                return null;
            }
            p.Color(c);
            p.R.sortingOrder += order;
            return m.Glue(p);
        }

        /// <summary>Lets a held particle go: a copy of it fades out over 'life' from colour 'from', the held one dies now.</summary>
        public static void Fade(ChoreoMove m, Particle p, float life, Color from)
        {
            if (p == null)
            {
                return;
            }
            if (p.Alive && p.Frames != null && p.Frames.Length > 0 && p.Frames[0] != null)
            {
                Particle q = Fx.Spawn(p.Frames[0], p.Pos, p.Px, life, FxLayer.Behind, 0, true);
                if (q != null)
                {
                    q.R.sortingLayerID = p.R.sortingLayerID;
                    q.R.sortingOrder = p.R.sortingOrder;
                    q.R.sharedMaterial = p.R.sharedMaterial;
                    q.UseEuler = p.UseEuler;
                    q.Euler = p.Euler;
                    q.SX = p.SX;
                    q.SY = p.SY;
                    q.Rot = p.Rot;
                    q.Z = p.Z;
                    q.Cine = p.Cine;
                    q.Color(from, Fx.WithA(from, 0f));
                }
            }
            m.Unglue(p);
        }

        /// <summary>
        /// A silhouette laid over the performer (its ghost or wraith form): held, glued to the move;
        /// keep it on the body every frame with Cover.
        /// </summary>
        public static Particle Veil(ChoreoMove m, int order = 6)
        {
            Sprite s = Fx.UnitSprite(m.A);
            if (s == null || !Fx.HasSilhouette)
            {
                return null;
            }
            Particle p = Fx.Spawn(s, m.DrawPos(), 1f, 1f, FxLayer.Over, order, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            m.Glue(p);
            Fx.Glue(p, m, Fx.WithA(Color.white, 0f));
            return p;
        }

        /// <summary>Keeps a veil on the performer's body this frame, in colour 'c'.</summary>
        public static void Cover(ChoreoMove m, Particle p, Color c)
        {
            if (p != null && p.Alive)
            {
                Fx.Glue(p, m, c);
            }
        }

        /// <summary>The body split in three like a torn frame (the school's colour, the tier's edge, white), fading where it was.</summary>
        public static void Split(ChoreoMove m, float life, float offPx)
        {
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Fx.Ghost(m, new Vector2(-offPx * px, 0f), 0f, life, Fx.WithA(m.Hue.Main, 0.8f), Fx.WithA(m.Hue.Main, 0f));
            Fx.Ghost(m, new Vector2(offPx * px, 0f), 0f, life, Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
            Fx.Afterimage(m, life * 0.7f, new Color(1f, 1f, 1f, 0.9f), new Color(1f, 1f, 1f, 0f));
        }

        // ------------------------------------------------------------ effects --

        /// <summary>
        /// The frame tears where time skipped: thin horizontal lines in white and the school's colours
        /// flick sideways across 'body' and are gone in a few hundredths.
        /// </summary>
        public static void Tear(Vector2 body, float px, float k, Hue hue, int n, float spanPx, FxLayer layer = FxLayer.Top)
        {
            if (!Kit.Visible(body))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float y = Random.Range(-9f, 11f) * px * k;
                float len = Random.Range(0.35f, 1f) * spanPx * px * k;
                float x = Random.Range(-0.4f, 0.4f) * spanPx * px * k;
                Color c = i % 3 == 0 ? Color.white : i % 3 == 1 ? hue.Edge : hue.Main;
                Particle p = Kit.Line(body + new Vector2(x - len * 0.5f, y), body + new Vector2(x + len * 0.5f, y), px, Random.Range(0.05f, 0.1f), 1, layer, 14,
                    Fx.WithA(c, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
                p?.Moving(new Vector2((Random.value < 0.5f ? -1f : 1f) * Random.Range(40f, 90f) * px * k, 0f));
            }
        }

        /// <summary>A clock dial on the ground (a ring of twelve ticks) swelling from r0 to r1 pixels and fading: time bent here.</summary>
        public static Particle Dial(Vector2 at, float px, float r0, float r1, float life, Color c, FxLayer layer = FxLayer.Ground)
        {
            if (!Kit.Visible(at))
            {
                return null;
            }
            Particle p = Fx.Spawn(Art.Ring(Mathf.RoundToInt(r0), 1, Art.Ground, 12), at, px, life, layer, 5);
            if (p == null)
            {
                return null;
            }
            p.Snap = true;
            p.Color(Fx.WithA(c, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
            p.Step = (q, dt) =>
            {
                float e = 1f - (1f - q.K) * (1f - q.K);
                Sprite s = Art.Ring(Mathf.RoundToInt(Mathf.Lerp(r0, r1, e)), 1, Art.Ground, 12);
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
            };
            return p;
        }

        /// <summary>Glass shards (petals of the school's colour) bursting from 'at' and falling to 'ground'.</summary>
        public static void Shards(Vector2 at, Vector2 ground, float px, float k, Hue hue, int n, float speed = 1f, FxLayer layer = FxLayer.Top)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), ground, px, Random.Range(0.3f, 0.55f), layer, 12);
                if (p == null)
                {
                    break;
                }
                p.Moving(d * Random.Range(40f, 90f) * px * k * speed, Random.Range(10f, 40f) * px, 2f, 200f * px);
                p.Z = Mathf.Max(0f, at.y - ground.y);
                p.Spin = Random.Range(-600f, 600f);
                p.Bounce = 0.2f;
                p.Color(Color.white, Fx.WithA(hue.Main, 0.9f), Fx.WithA(hue.Edge, 0f));
            }
        }
    }

    /// <summary>
    /// Dev staging of the signature interrupt, to watch it up close:
    ///   tskip [tierA] [tierB] [dist] [mode] [zoom] [tech]
    /// a Phantom Step fighter A (tierA, human) and a Ki Arts foe B (tierB, orc) 'dist' tiles apart; B
    /// winds up 'tech' (dragon_wave) at A. mode 0: the moment B gathers, A's Time Skip is forced on it;
    /// 1: A only answers as the beam reaches it (Reactions.DevForce); 2: A's own Brain decides (nothing
    /// forced: does it choose the skip?). Logs "tskip done" 6 s after B starts.
    /// </summary>
    internal static class TimeSkipStage
    {
        private static Actor _a, _b;
        private static float _t = -1f;
        private static bool _went, _skipped, _fired;
        private static float _why;
        private static int _mode;
        private static string _tech;
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["tskip"] = Command;
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
                catch (Exception e)
                {
                    SakugaMod.Report("tskip", e);
                    End();
                }
            };
            // (the staged pair act only as staged: B's own Brain stays out, A's too unless mode 2)
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && (a == _b || a == _a && _mode != 2));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            Tier ta = Dev.ParseTier(Dev.Arg(args, 1, "master"));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 2, "expert"));
            float dist = Mathf.Clamp(Dev.ArgF(args, 3, 8f), 2f, 14f);
            _mode = Mathf.RoundToInt(Dev.ArgF(args, 4, 0f));
            float zoom = Dev.ArgF(args, 5, 16f);
            _tech = Dev.Arg(args, 6, "dragon_wave");
            if (ta == Tier.None)
            {
                ta = Tier.Master;
            }
            if (tb == Tier.None)
            {
                tb = Tier.Expert;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 10);
            if (c == null)
            {
                Dev.Log("tskip: no ground");
                return;
            }
            int half = Mathf.RoundToInt(dist * 0.5f);
            _a = Dev.Spawn("human", World.world.GetTile(c.x - half, c.y) ?? c, true);
            _b = Dev.Spawn("orc", World.world.GetTile(c.x + Mathf.RoundToInt(dist) - half, c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("tskip: could not spawn");
                return;
            }
            Progression.Promote(_a, ta, false);
            Progression.Promote(_b, tb, false);
            ActorTrait ps = Schools.Trait(School.PhantomStep), ki = Schools.Trait(School.KiArts);
            if (ps != null)
            {
                _a.addTrait(ps, true);
            }
            if (ki != null)
            {
                _b.addTrait(ki, true);
            }
            _a.data.favorite = false;
            _b.data.favorite = false;
            if (_mode != 2)
            {
                Director.Claim(_scene, 9f, _a, _b);   // (no duel claims the pair while it is staged)
            }
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _went = _skipped = _fired = false;
            _why = 0f;
            Dev.Log("tskip: " + ta + " human #" + _a.Sid() + " vs " + tb + " orc #" + _b.Sid() + ", " + dist + " tiles, mode " + _mode + ", " + _tech);
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > 1.5f)
                {
                    Dev.Log("tskip done (one fell)");
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
            if (_went && !_fired && _t < 5f)
            {
                Fire();
            }
            if (_went && !_skipped && _mode == 0 && Moves.Charging(_b))
            {
                _skipped = true;
                Technique t = Techniques.Get("time_skip");
                Moves.ReleaseHeld();
                bool ok = t != null && Brain.Force(_a, t, Mathf.Max(1, Techniques.Level(_a, t)), _b);
                Dev.Log("tskip: " + _b.asset.id + " is charging (" + Vector2.Distance(_a.current_position, _b.current_position).ToString("F1") + " tiles); the skip: " + ok);
            }
            if (_went && _t >= 7.5f)
            {
                Dev.Log("tskip done");
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
            if (_mode == 1)
            {
                Reactions.DevForce[_a.Sid()] = "time_skip";
            }
            Technique t = Techniques.Get(_tech);
            if (t == null)
            {
                Dev.Log("tskip: no technique " + _tech);
                End();
                return;
            }
            Moves.ReleaseHeld();
            if (_mode == 1)
            {
                Moves.Hold(_a);   // it stands its ground until it answers
            }
            Fire();
        }

        /// <summary>B winds up at A (tried every frame until it can: a fresh unit may still be landing or busy).</summary>
        private static void Fire()
        {
            Technique t = Techniques.Get(_tech);
            if (t == null)
            {
                return;
            }
            _fired = Brain.Force(_b, t, Mathf.Max(1, Techniques.Level(_b, t)), _a);
            if (_fired || _t >= _why)
            {
                _why = _t + 0.5f;
                Dev.Log("tskip: " + _tech + " by " + _b.asset.id + ": " + _fired + (_fired ? "" : " (can perform " + Moves.CanPerform(_b) + ", move "
                    + (Moves.TryGet(_b, out Move m) ? m.GetType().Name : "-") + ", h " + _b.position_height.ToString("F2") + ", forces " + _b.under_forces
                    + ", stunned " + _b.hasStatusStunned() + ", foes " + Combat.IsFoe(_b, _a) + ", dist " + Vector2.Distance(_a.current_position, _b.current_position).ToString("F1") + ")"));
            }
        }

        private static void End()
        {
            if (_a != null)
            {
                Reactions.DevForce.Remove(_a.Sid());
            }
            Director.Release(_scene);
            _t = -1f;
            _a = _b = null;
            Moves.ReleaseHeld();
        }

        // ------------------------------------------------------------ the fight --

        private static readonly string[] Mine =
        {
            "thousand_steps", "ghost_pierce", "time_skip", "phantom_legion", "afterimage_prison", "wraith_walk", "instant_strike", "eternal_second", "hall_of_mirrors"
        };

        /// <summary>
        /// Dev: "psfight [tierA] [nA] [tierB] [nB] [raceB] [zoom]": nA Phantom Step humans of tierA against nB
        /// raceB fighters of tierB who walk Ki Arts and Iron Fist (beams, haymakers: charges to break),
        /// nobody held: the AI fights it out. "psstats" logs how often the Phantom Step masters' techniques
        /// were started so far.
        /// </summary>
        [SakugaInit]
        private static void InitFight()
        {
            Dev.Commands["psfight"] = Fight;
            Dev.Commands["psstats"] = args =>
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("psstats:");
                foreach (string id in Mine)
                {
                    Brain.Started.TryGetValue(id, out int n);
                    sb.Append(' ').Append(id).Append('=').Append(n);
                }
                Dev.Log(sb.ToString());
            };
        }

        private static void Fight(string[] args)
        {
            Tier ta = Dev.ParseTier(Dev.Arg(args, 1, "master"));
            int na = Mathf.Clamp(Mathf.RoundToInt(Dev.ArgF(args, 2, 2f)), 1, 12);
            Tier tb = Dev.ParseTier(Dev.Arg(args, 3, "expert"));
            int nb = Mathf.Clamp(Mathf.RoundToInt(Dev.ArgF(args, 4, 5f)), 1, 30);
            string rb = Dev.Arg(args, 5, "orc");
            float zoom = Dev.ArgF(args, 6, 20f);
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 12);
            if (c == null)
            {
                Dev.Log("psfight: no ground");
                return;
            }
            ActorTrait ps = Schools.Trait(School.PhantomStep), ki = Schools.Trait(School.KiArts), fist = Schools.Trait(School.IronFist);
            for (int i = 0; i < na; i++)
            {
                Actor u = Dev.Spawn("human", World.world.GetTile(c.x - 6, c.y - na + 2 * i) ?? c, false);
                if (u == null)
                {
                    continue;
                }
                if (ta != Tier.None)
                {
                    Progression.Promote(u, ta, false);
                }
                if (ps != null)
                {
                    u.addTrait(ps, true);
                }
                u.data.favorite = false;
            }
            for (int i = 0; i < nb; i++)
            {
                Actor u = Dev.Spawn(rb, World.world.GetTile(c.x + 6 + i % 3, c.y - nb / 3 + i / 3 * 2) ?? c, false);
                if (u == null)
                {
                    continue;
                }
                if (tb != Tier.None)
                {
                    Progression.Promote(u, tb, false);
                }
                if (ki != null)
                {
                    u.addTrait(ki, true);
                }
                if (fist != null)
                {
                    u.addTrait(fist, true);
                }
                u.data.favorite = false;
            }
            Vector2 mid = new Vector2(c.x + 0.5f, c.y + 1f);
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid);
            Dev.Log("psfight: " + na + " " + ta + " phantom steppers vs " + nb + " " + tb + " " + rb);
        }
    }
}
