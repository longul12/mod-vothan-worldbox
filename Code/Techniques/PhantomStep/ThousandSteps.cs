using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THOUSAND STEPS (Phantom Step, Master). A crouch, a flash in the eyes, and it steps: gone from one
    /// spot and already beside the next foe, mid-blow, a snap kick here, an elbow there, a knee, a
    /// palm, every blow a different one and every foe within reach taking one, faster and faster.
    /// Each spot it leaves keeps its frame: an afterimage frozen in the pose of the blow it threw
    /// there, so the field fills with a gallery of itself, joined by thin violet threads along the
    /// ground where it stepped. It ends beside the last foe with the heaviest blow; then the gallery
    /// shatters. Distinct from Phantom Rush (which returns and opens every wound at once): here every
    /// blow lands as it is thrown, the poses stay, and it never goes back.
    ///
    /// Levels: I up to 4 foes within 6 tiles, the gallery just fades. II up to 6 within 7: the
    /// gallery bursts into shards, each step dents the ground. III up to 9 within 8: every frozen
    /// frame strikes its foe once more as it shatters (an echo, x0.35), the last blow an uppercut
    /// that launches. Damage per step (x the user's damage): I 0.9, II 1.0, III 1.1; the last x1.5.
    /// </summary>
    internal sealed class ThousandStepsTech : Technique
    {
        public override string Id => "thousand_steps";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Chain;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Dash | Form.Teleport | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 26f, 44f, 68f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Thrown;
        }

        private static int MaxSteps(int level)
        {
            return level <= 1 ? 4 : level == 2 ? 6 : 9;
        }

        private static float Radius(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7f : 8f;
        }

        private static float StepMult(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1f : 1.1f;
        }

        private const float Echo = 0.35f;

        /// <summary>The blows it throws, one per step, in order (the last step throws the finisher).</summary>
        private static readonly Blow[] Blows = { Blow.SnapKick, Blow.Elbow, Blow.Knee, Blow.Palm, Blow.Hook, Blow.Body, Blow.Backfist, Blow.Cross, Blow.Headbutt };

        private static readonly List<Actor> _scratch = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 2f : 0f);
            List<Actor> list = _scratch;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 24; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out _, c.Forced) || !PsHiKit.Standing(v))
                {
                    continue;
                }
                if (!PsHiKit.SpotBeside(v, c.Pos, 0.62f * k + v.stats["size"] * 0.3f, out _))
                {
                    continue;
                }
                list.Add(v);
            }
            int need = c.Forced || c.Linked ? 1 : 2;
            if (list.Count < need)
            {
                return null;
            }
            PsHiKit.Path(list, c.Pos);
            int n = Mathf.Min(list.Count, MaxSteps(level));
            float score = 0f;
            bool worth = false;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                float mult = StepMult(level) * (i == n - 1 ? 1.5f : 1f) + (level >= 3 && i < n - 1 ? Echo : 0f);
                score += c.Value(v, c.Expected(v, mult), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 1.2f) * cm)
                {
                    worth = true;
                }
            }
            if (!c.Forced && !worth && n < 3)
            {
                return null;   // two peasants are a pair of ordinary blows, not a chain of steps
            }
            score += 6f * (n - 1);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            List<Actor> path = new List<Actor>(n);
            for (int i = 0; i < n; i++)
            {
                path.Add(list[i]);
            }
            return new Plan
            {
                Target = path[0],
                Point = path[0].current_position,
                Dir = PsHiKit.DirTo(c.Pos, path[0].current_position),
                Count = n,
                Score = score,
                Others = path,
                Data = c.Forced
            };
        }

        /// <summary>One chain of steps: whom, in what order, the gallery of frames it leaves.</summary>
        private sealed class State
        {
            public readonly List<Actor> T = new List<Actor>(9);
            public readonly List<long> Ids = new List<long>(9);
            public readonly List<Particle> Gallery = new List<Particle>(9);
            public readonly List<Actor> GalleryOf = new List<Actor>(9);
            public readonly List<Vector2> GalleryAt = new List<Vector2>(9);
            public readonly List<bool> Shattered = new List<bool>(9);
            public Actor Cur;
            public Blow Blow;
            public Vector2 To;
            public bool Struck, Posed;
            public Actor PosedOn;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            State st = new State();
            List<Actor> path = p.Others;
            if (path == null || path.Count == 0)
            {
                if (p.Target == null)
                {
                    return false;
                }
                path = new List<Actor> { p.Target };
            }
            foreach (Actor v in path)
            {
                if (v != null)
                {
                    st.T.Add(v);
                    st.Ids.Add(v.Sid());
                }
            }
            int n = st.T.Count;
            if (n == 0)
            {
                return false;
            }
            // (a chain across many: none of them is "the" target the move ends on when it answers)
            m.SetVictim(null);
            m.Face(PsHiKit.DirTo(m.Stance, st.T[0].current_position, m.Dir));
            Form shows = Form.Dash | Form.Teleport | Form.Fast;
            // the coil: a crouch, a flash in the eyes, a dial at its feet
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.1f,
                To = new Shape(10f, 1.12f, 0.86f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                        Fx.Glint(mm.HeadPos(), mm.Px, 0.12f, mm.Hue.Core, 0.8f);
                        PsHiKit.Dial(mm.Stance, mm.Px, 3f * mm.Kk, 10f * mm.Kk, 0.35f, mm.Hue.Edge);
                    }
                }
            });
            for (int i = 0; i < n; i++)
            {
                int step = i;
                float f = n <= 1 ? 1f : i / (float)(n - 1);
                m.Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = Mathf.Lerp(0.2f, 0.12f, f),
                    Ease = Ease.Linear,
                    Forms = shows,
                    Intangible = true,
                    Track = false,
                    Enter = mm => StepIn(mm, st, step),
                    Every = (mm, k) => Stepping(mm, st, step, k)
                });
            }
            // the last blow's pose is held: it reads
            m.Hold(0.08f, true, Tempo.Impact);
            // the gallery shatters (III: each frame strikes its foe once more as it goes)
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = m.Level >= 3 ? 0.34f : 0.2f,
                Forms = shows,
                Track = false,
                Every = (mm, k) => Shatter(mm, st, k)
            });
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>A step: the frame it leaves stays standing, and it is already beside the next foe, cocked.</summary>
        private static void StepIn(ChoreoMove m, State st, int i)
        {
            st.Cur = null;
            st.Struck = false;
            Actor v = st.T[i];
            // the frame of the blow it just threw stays where it was
            if (st.Posed)
            {
                st.Posed = false;
                Color gc = Fx.WithA(Color.Lerp(m.Hue.Ghost, m.Hue.Main, m.Level >= 3 ? 0.45f : 0.25f), 0.72f);
                Particle g = PsHiKit.Still(m, gc);
                if (g != null)
                {
                    st.Gallery.Add(g);
                    st.GalleryOf.Add(st.PosedOn);
                    st.GalleryAt.Add(m.BodyPos());
                    st.Shattered.Add(false);
                }
            }
            if (v == null || v.Sid() != st.Ids[i] || !Choreo.Fair(m.A, v, out _, true) || !PsHiKit.Standing(v))
            {
                m.BeatDur = 0.01f;   // gone, or out of reach: the chain steps on
                return;
            }
            float stand = 0.62f * m.Kk + v.stats["size"] * 0.3f;
            Vector2 from = m.Stance;
            if (!PsHiKit.SpotBeside(v, from, stand, out Vector2 to))
            {
                m.BeatDur = 0.01f;
                return;
            }
            float px = m.Px;
            if (m.Vis)
            {
                Fx.Afterimage(m, 0.1f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                Fx.Ring(from, px, 0.16f, 6f * m.Kk, 1f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                if (m.Level >= 2)
                {
                    // a thread along the ground where it stepped
                    Particle t = Kit.Line(from, to, px, 1.3f, 1, FxLayer.Ground, 3, Fx.WithA(m.Hue.Edge, 0.85f), Fx.WithA(m.Hue.Main, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                    if (t != null)
                    {
                        t.Snap = true;
                    }
                }
            }
            m.Stance = to;
            m.Pose.Ground = to;
            m.Face(Victims.GroundOf(v) - to);
            st.Blow = i == st.T.Count - 1 ? (m.Level >= 3 ? Blow.Uppercut : Blow.SideKick) : Blows[i % Blows.Length];
            BlowShape s = ChoreoMove.ShapeOf(st.Blow);
            // arrived already cocked
            m.Pose.Rot = s.Wind.Rot;
            m.Pose.SX = s.Wind.SX;
            m.Pose.SY = s.Wind.SY;
            m.Vis = Fx.OnScreen(to, 0.35f);
            if (m.Vis)
            {
                Fx.Ring(to, px, 0.18f, 2f, 8f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.4f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                ChoreoFx.HeelDust(to, from - to, px, m.A.current_tile, m.Kk);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
            Combat.Pin(v, m.BeatDur + 0.3f);
            st.Cur = v;
            st.To = to;
        }

        /// <summary>A frame of a step: the cocked pose snaps into the blow, which lands at 42% of it, then holds.</summary>
        private static void Stepping(ChoreoMove m, State st, int i, float k)
        {
            if (st.Cur == null)
            {
                return;
            }
            BlowShape s = ChoreoMove.ShapeOf(st.Blow);
            Shape from = s.Wind, to = s.Hit;
            float e = k < 0.3f ? 0f : Beat.Apply(Ease.OutExpo, Mathf.Clamp01((k - 0.3f) / 0.2f));
            if (k > 0.75f)
            {
                // the follow-through relaxes a little
                float r = (k - 0.75f) / 0.25f * s.Relax * 0.6f;
                to = new Shape(to.Rot * (1f - r), Mathf.Lerp(to.SX, 1f, r), Mathf.Lerp(to.SY, 1f, r), to.H, to.Lunge, 0f);
            }
            m.Pose.Rot = Mathf.Lerp(from.Rot, to.Rot, e);
            m.Pose.SX = Mathf.Lerp(from.SX, to.SX, e);
            m.Pose.SY = Mathf.Lerp(from.SY, to.SY, e);
            m.Pose.Height = Mathf.Max(0f, Mathf.Lerp(from.H, to.H, e) * m.Kk);
            Vector2 lunge = st.To + m.Dir * Mathf.Lerp(from.Lunge, to.Lunge, e) * m.Kk;
            m.Pose.Ground = Combat.SafeSpot(lunge) || Choreo.Wading(lunge) ? lunge : st.To;
            if (!st.Struck && k >= 0.42f)
            {
                st.Struck = true;
                Strike(m, st, i, s);
            }
            if (k >= 0.95f)
            {
                m.Stance = m.Pose.Ground;   // (it stays where the lunge took it: no pop back)
                st.To = m.Stance - m.Dir * to.Lunge * m.Kk;
            }
        }

        private static void Strike(ChoreoMove m, State st, int i, BlowShape s)
        {
            Actor v = st.Cur;
            if (!Combat.Living(v))
            {
                return;
            }
            float reach = 0.62f * m.Kk + v.stats["size"] * 0.5f + 0.9f * m.Kk;
            Vector2 vg = Victims.GroundOf(v);
            if (Vector2.Distance(m.Pose.Ground, vg) > reach)
            {
                PsHiKit.Whiff(m);
                return;
            }
            Vector2 dir = PsHiKit.DirTo(m.Stance, vg, m.Dir);
            Vector2 at = Choreo.BodyAt(v, Mathf.Clamp(s.Contact, 0.2f, 0.8f)) - dir * 1f * m.Px;
            if (m.Vis)
            {
                Vector2 root = m.Root(s.Limb);
                ChoreoFx.Smear(root, Vector2.Lerp(at, root, 0.12f), m.Px, 0.08f, Mathf.Min(s.SmearHead + (m.Level >= 3 ? 1 : 0), 3), Mathf.Max(1, s.SmearThick),
                    m.Hue.Core, m.Hue.Edge);
                Fx.Ghost(m, -m.Dir * 2f * m.Px, -6f, 0.07f, Fx.WithA(m.Hue.Ghost, 0.55f), m.Hue.GhostEnd);
            }
            st.Posed = true;
            st.PosedOn = v;
            if (!m.Touch(v))
            {
                if (m.Vis)
                {
                    Kit.Sparks(at, m.Px, 3, 40f, 80f, m.Hue.Edge);   // it slipped this one: the chain steps on
                }
                return;
            }
            int level = m.Level;
            bool last = i == st.T.Count - 1;
            Hit h = new Hit(st.Blow, StepMult(level) * (last ? 1.5f : 1f))
            {
                Lethal = true,
                Finisher = last,
                Power = last ? 1.25f : 0.9f
            };
            if (last)
            {
                h.Hurt = level >= 3 ? Hurt.Launch : Hurt.Reel;
                h.Push = level >= 3 ? 1.2f : 1.4f;
                h.Lift = level >= 3 ? 2f : -1f;
            }
            m.Deliver(v, h, s, at, dir);
            m.HoldFor(last ? 0.06f : 0.025f);
            if (level >= 2)
            {
                // every step dents the ground under the blow (and whatever stands on it)
                Ruin.Blast(m.A, vg, 0.35f, m.Scale, m.Pose.Ground);
            }
        }

        /// <summary>The gallery goes, frame by frame along the path: it fades (I), shatters (II), and strikes once more as it does (III).</summary>
        private static void Shatter(ChoreoMove m, State st, float k)
        {
            int n = st.Gallery.Count;
            for (int j = 0; j < n; j++)
            {
                if (st.Shattered[j] || k < j / (float)Mathf.Max(1, n) * 0.85f)
                {
                    continue;
                }
                st.Shattered[j] = true;
                Particle g = st.Gallery[j];
                Vector2 at = st.GalleryAt[j];
                int level = m.Level;
                if (level >= 2 && g != null && g.Alive && Kit.Visible(at))
                {
                    PsHiKit.Shards(at, at - new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale), m.Px, m.Kk, m.Hue, 7);
                    Fx.Ring(at, m.Px, 0.16f, 2f, 7f * m.Kk, 1, 1, FxLayer.Top, 5, 1f, 2.4f, Fx.WithA(Color.white, 0.85f), Fx.WithA(m.Hue.Edge, 0f));
                    Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(at), 0.06f);
                }
                PsHiKit.Fade(m, g, level >= 2 ? 0.1f : 0.25f, Fx.WithA(m.Hue.Ghost, 0.7f));
                if (level >= 3)
                {
                    EchoStrike(m, st.GalleryOf[j], at);
                }
            }
        }

        /// <summary>A frozen frame strikes its foe once more as it shatters (III).</summary>
        private static void EchoStrike(ChoreoMove m, Actor v, Vector2 from)
        {
            if (v == null || !Combat.Living(v) || !Choreo.Fair(m.A, v, out float cm, true))
            {
                return;
            }
            Vector2 body = Choreo.BodyAt(v, 0.55f);
            if (Vector2.Distance(from, body) > 3f * m.Kk + v.stats["size"] || !m.Touch(v))
            {
                return;
            }
            Vector2 dir = PsHiKit.DirTo(from, body, m.Dir);
            Combat.Hit(m.A, v, Echo * cm, true, false, 0f, m);
            if (Combat.Living(v))
            {
                Victims.Hurt(v, m.A, m, Impact.Of(Hurt.Reel, dir, 0.7f, 0.8f));
            }
            if (m.Vis)
            {
                ChoreoFx.Smear(from, body, m.Px, 0.07f, 1, 1, m.Hue.Core, m.Hue.Edge);
                ChoreoFx.Impact(body, dir, m.Px, m.Kk, 0.9f, m.Hue, m.TopLayer);
            }
        }
    }
}
