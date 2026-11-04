using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ETERNAL SECOND (Phantom Step, God; Ultimate). The God lifts one hand; a clock dial opens at its
    /// feet; tick. The world stops and goes grey (a grey veil over everything; title card). Only the
    /// God still has its colours, and in the frozen second it WALKS - real strides, unhurried - from
    /// one frozen enemy to the next, laying a palm on each: a hit star appears and hangs in the air
    /// where it touched, a little clock dial over the head. Its frozen afterimages stand along the
    /// way. Done, it raises its hand and snaps its fingers: colour floods back, the world jolts, and
    /// every blow it laid lands at once: they all fly, the ground cratering under each.
    ///
    /// The cinematic follows the settings (Settings.Cinematics full, on screen, no other cinematic
    /// playing): it holds the world still and veils it. Without it (light mode) nothing stops the
    /// world: each enemy it reaches is locked in time instead (held where it stands under a grey
    /// sheen, trembling against it) and the God walks faster.
    ///
    /// Levels (Gods always III; I-II through a bare trait): I up to 5 foes within 7 tiles (x2.6 each);
    /// II 8 within 8.5 (x3.2); III 12 within 10 (x4), the craters wider, a black impact frame on the
    /// strongest. Damage x the user's damage, on each.
    /// </summary>
    internal sealed class EternalSecondTech : Technique
    {
        public override string Id => "eternal_second";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Flurry | Form.Area | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 200f, 260f, 340f };
        public override float[] Cooldown => new[] { 80f, 85f, 90f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        private static int MaxFoes(int level)
        {
            return level <= 1 ? 5 : level == 2 ? 8 : 12;
        }

        private static float Radius(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8.5f : 10f;
        }

        private static float Mult(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.2f : 4f;
        }

        private static readonly Color Grey = new Color(0.6f, 0.6f, 0.66f, 1f);

        private static readonly List<Actor> _scratch = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 3f : 0f);
            List<Actor> list = _scratch;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 28; i++)
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
                if (!PsHiKit.SpotBeside(v, c.Pos, 0.7f * k + v.stats["size"] * 0.4f, out _))
                {
                    continue;
                }
                list.Add(v);
            }
            int need = c.Forced ? 1 : c.Linked ? 2 : 3;
            if (list.Count < need)
            {
                return null;
            }
            PsHiKit.Path(list, c.Pos);
            int n = Mathf.Min(list.Count, MaxFoes(level));
            float score = 0f;
            List<Actor> path = new List<Actor>(n);
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
                path.Add(v);
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = path[0],
                Point = c.Pos,
                Dir = PsHiKit.DirTo(c.Pos, path[0].current_position),
                Count = n,
                Score = score,
                Others = path,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        /// <summary>One stopped second: whom it walks to, the blows it laid, what the stop holds.</summary>
        private sealed class State
        {
            public readonly List<Actor> T = new List<Actor>(12);
            public readonly List<long> Ids = new List<long>(12);
            public readonly List<bool> Marked = new List<bool>(12);
            public readonly List<Vector2> Dirs = new List<Vector2>(12);
            public readonly List<Particle> Marks = new List<Particle>(24);
            public readonly List<Actor> Locked = new List<Actor>(12);
            public readonly List<Particle> Sheens = new List<Particle>(12);
            public bool Full, Stopped;
            public Particle God;
            public Particle Clock;
            public float LockFor, GhostT, StrideT;
            public Vector2 Dest;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State();
            float len = 0f;
            Vector2 at = m.Stance;
            foreach (Actor v in p.Others ?? new List<Actor> { p.Target })
            {
                if (v == null || st.T.Count >= MaxFoes(level))
                {
                    continue;
                }
                st.T.Add(v);
                st.Ids.Add(v.Sid());
                st.Marked.Add(false);
                st.Dirs.Add(PsHiKit.DirTo(m.Stance, v.current_position));
                len += Vector2.Distance(at, v.current_position);
                at = v.current_position;
            }
            int n = st.T.Count;
            if (n == 0)
            {
                return false;
            }
            // (it walks among many: none of them is "the" target an answer would stop it on)
            m.SetVictim(null);
            m.Face(PsHiKit.DirTo(m.Stance, st.T[0].current_position, m.Dir));
            Form shows = Form.Teleport | Form.Flurry | Form.Area | Form.Ultimate;
            // the stopped second is about 2.4 s of walking at most, however many there are
            const float touch = 0.13f;
            float walk = Mathf.Clamp(2.4f - n * touch, 0.6f, 1.8f);
            float perTile = Mathf.Clamp(walk / Mathf.Max(1f, len), 0.035f, 0.11f);
            st.LockFor = 0.5f + n * touch + perTile * len + 0.6f;
            // it lifts one hand; a dial opens at its feet; tick
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.26f,
                To = new Shape(-8f, 0.94f, 1.08f, 0f, -0.02f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        PsHiKit.Dial(mm.Stance, mm.Px, 4f * mm.Kk, 22f * mm.Kk, 0.5f, mm.Hue.Edge);
                        Fx.Glint(mm.Hand(Limb.Palm) + new Vector2(0f, 3f * mm.Px), mm.Px, 0.2f, mm.Hue.Core, 1f);
                    }
                    Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile);
                }
            });
            // the world stops
            m.Do(mm => Stop(mm, st));
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                // a walk to the next frozen one: real strides, unhurried
                m.Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.04f,
                    PerTile = perTile,
                    Route = Route.Line,
                    PathEase = Ease.Linear,
                    Dest = mm => Approach(mm, st, idx),
                    To = new Shape(6f, 1f, 1f, 0f, 0f),
                    Ease = Ease.OutQuad,
                    Forms = shows,
                    Intangible = true,
                    Track = false,
                    Enter = mm =>
                    {
                        mm.Face(st.Dest - mm.Stance);
                        st.StrideT = 0f;
                    },
                    Every = (mm, k) => Walking(mm, st, k)
                });
                // a palm laid on it: the blow waits in the stopped second
                m.Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = touch,
                    To = ChoreoMove.ShapeOf(Blow.Palm).Hit,
                    Ease = Ease.OutExpo,
                    Forms = shows,
                    Intangible = true,
                    Track = false,
                    Enter = mm =>
                    {
                        Actor v = st.T[idx];
                        if (Valid(mm, st, idx))
                        {
                            mm.Face(Victims.GroundOf(v) - mm.Stance);
                        }
                    },
                    Every = (mm, k) => Follow(mm, st)
                }.On(0.5f, mm => Lay(mm, st, idx)));
            }
            // done: it raises its hand and snaps its fingers
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.24f,
                To = new Shape(-6f, 0.95f, 1.08f, 0f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Intangible = true,
                Track = false,
                Every = (mm, k) => Follow(mm, st)
            }.On(0.75f, mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(mm.Hand(Limb.Palm) + new Vector2(0f, 3f * mm.Px), mm.Px, 0.16f, Color.white, 1.1f);
                }
                Choreo.Sound("event:/SFX/HIT/HitGeneric", mm.A.current_tile);
            }));
            // and time resumes: every blow lands at once
            m.Do(mm => Resume(mm, st));
            m.Hold(0.3f, false, Tempo.Impact);
            m.Recover(0.26f);
            m.Ended2 += (mm, aborted) => Unlock(mm, st);
            return m.Start();
        }

        /// <summary>The world stops: held and greyed (a full cinematic), or (light mode) each foe is locked in time as it is reached.</summary>
        private static void Stop(ChoreoMove m, State st)
        {
            st.Stopped = true;
            st.Full = m.BeginCinematic(12f, 0f, "sakuga_title_eternal_second", 0f, 0f);
            if (st.Full)
            {
                Cinema.Dim(0.55f, Grey, 0.08f, false);
                Cinema.Hold(m);
                st.God = GodImage(m);
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
            if (m.Vis)
            {
                FxLayer layer = m.TopLayer;
                float px = m.Px;
                // the stop sweeps outward from it
                Fx.Ring(m.Stance, px, 0.35f, 4f, 60f * m.Kk, 2, 1, st.Full ? FxLayer.Cine : FxLayer.Ground, 6, Art.Ground, 2.2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.Flash(m.BodyPos(), px, Mathf.RoundToInt(6f * m.Kk), 0.1f, Fx.WithA(Color.white, 0.9f), layer);
                // the dial at its feet stays open while time is stopped
                Particle dial = Fx.Spawn(Art.Ring(Mathf.RoundToInt(16f * m.Kk), 1, Art.Ground, 12), m.Stance, px, 1f, st.Full ? FxLayer.Cine : FxLayer.Ground, 4);
                if (dial != null)
                {
                    dial.Snap = true;
                    dial.Color(Fx.WithA(m.Hue.Edge, 0.75f));
                    st.Clock = m.Glue(dial);
                }
            }
            if (!st.Full)
            {
                // nothing stops the world: the ones it walks to are locked in time instead
                for (int i = 0; i < st.T.Count; i++)
                {
                    if (Valid(m, st, i))
                    {
                        Lock(m, st, st.T[i]);
                    }
                }
            }
        }

        /// <summary>The God redrawn above the grey veil in its own colours (the only thing that still has them).</summary>
        private static Particle GodImage(ChoreoMove m)
        {
            Sprite s = Fx.UnitSprite(m.A);
            if (s == null)
            {
                return null;
            }
            Particle p = Fx.Spawn(s, m.DrawPos(), 1f, 1f, FxLayer.Cine, 6, false);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            m.Glue(p);
            Fx.Glue(p, m, Color.white);
            return p;
        }

        /// <summary>Light mode: held where it stands (a reaction's long hold), a grey sheen over it, trembling against time.</summary>
        private static void Lock(ChoreoMove m, State st, Actor v)
        {
            Impact im = Impact.Of(Hurt.Flinch, PsHiKit.DirTo(m.Stance, v.current_position), 0f, 0.3f);
            im.Stop = st.LockFor;
            Victims.Hurt(v, m.A, m, im);
            Combat.Pin(v, st.LockFor);
            st.Locked.Add(v);
            Particle sheen = ChoreoFx.FlashUnit(v, Grey, st.LockFor);
            if (sheen != null)
            {
                sheen.Color(Fx.WithA(Grey, 0.6f));
                st.Sheens.Add(m.Glue(sheen));
            }
        }

        private static bool Valid(ChoreoMove m, State st, int i)
        {
            Actor v = st.T[i];
            return v != null && v.Sid() == st.Ids[i] && Combat.Living(v) && Choreo.Fair(m.A, v, out _, true);
        }

        /// <summary>Where it walks to: beside the next one, on the side it comes from.</summary>
        private static Vector2 Approach(ChoreoMove m, State st, int i)
        {
            st.Dest = m.Stance;
            if (Valid(m, st, i))
            {
                Actor v = st.T[i];
                if (PsHiKit.SpotBeside(v, m.Stance, 0.7f * m.Kk + v.stats["size"] * 0.4f, out Vector2 to))
                {
                    st.Dest = to;
                }
            }
            return st.Dest;
        }

        /// <summary>A frame of the walk: strides (a bob, a lean), its image above the veil, frozen afterimages along the way.</summary>
        private static void Walking(ChoreoMove m, State st, float k)
        {
            float stride = 0.16f;
            float ph = m.BeatT / stride;
            float bob = Mathf.Abs(Mathf.Sin(Mathf.PI * ph));
            m.Pose.Height = 0.05f * m.Kk * bob;
            m.Pose.SX = 1f + 0.04f * (1f - bob);
            m.Pose.SY = 1f - 0.04f * (1f - bob);
            Follow(m, st);
            st.GhostT -= Cinema.Frozen ? Cinema.Dt : Clock.Dt;
            if (st.GhostT <= 0f && m.Vis)
            {
                st.GhostT = 0.12f;
                // (in the stopped world it hangs where it was until time moves again)
                Fx.Afterimage(m, 0.4f, Fx.WithA(m.Hue.Ghost, 0.5f), Fx.WithA(m.Hue.Edge, 0f));
            }
        }

        /// <summary>Keeps the God's own image on its body above the veil.</summary>
        private static void Follow(ChoreoMove m, State st)
        {
            if (st.God != null)
            {
                PsHiKit.Cover(m, st.God, Color.white);
            }
        }

        /// <summary>The palm laid on a frozen one: the blow's star hangs in the air there, a dial over its head.</summary>
        private static void Lay(ChoreoMove m, State st, int i)
        {
            if (!Valid(m, st, i))
            {
                return;
            }
            Actor v = st.T[i];
            st.Marked[i] = true;
            st.Dirs[i] = PsHiKit.DirTo(m.Stance, Victims.GroundOf(v), st.Dirs[i]);
            if (!st.Full && !st.Locked.Contains(v))
            {
                Lock(m, st, v);
            }
            Choreo.Sound("event:/SFX/HIT/HitGeneric", v.current_tile, 0.06f);
            if (!m.Vis)
            {
                return;
            }
            FxLayer layer = st.Full ? FxLayer.Cine : FxLayer.Top;
            float vpx = Choreo.PxOf(v);
            Vector2 at = Choreo.BodyAt(v, 0.55f) - st.Dirs[i] * 1f * vpx;
            Particle star = Fx.Spawn(Art.HitStars[1], at, vpx, 1f, layer, 20);
            if (star != null)
            {
                star.Rot = Random.Range(0f, 90f);
                star.Color(Fx.WithA(Color.Lerp(Color.white, m.Hue.Core, 0.3f), 0.95f));
                st.Marks.Add(m.Glue(star));
            }
            Particle dial = Fx.Spawn(Art.Ring(Mathf.Clamp(Mathf.RoundToInt(3f * Choreo.KOf(v)), 3, 12), 1, 1f, 12), Choreo.BodyAt(v, 1f) + new Vector2(0f, 2.5f * vpx), vpx, 1f, layer, 21);
            if (dial != null)
            {
                dial.Color(Fx.WithA(Color.Lerp(m.Hue.Core, m.Hue.Edge, 0.3f), 1f));
                dial.Spin = -90f;   // (its hand turns, very slowly)
                st.Marks.Add(m.Glue(dial));
            }
            Kit.Flash(at, vpx, 2, 0.06f, Fx.WithA(m.Hue.Core, 0.9f), layer);
        }

        /// <summary>Time resumes: colour floods back, the world jolts, and every blow it laid lands at once.</summary>
        private static void Resume(ChoreoMove m, State st)
        {
            if (st.Full)
            {
                Cinema.Unhold(m);
                if (st.God != null)
                {
                    m.Unglue(st.God);
                    st.God = null;
                }
                m.EndCinematic();
                Cinema.Undim(0.06f);   // (the colour floods back at once)
            }
            if (st.Clock != null)
            {
                PsHiKit.Fade(m, st.Clock, 0.2f, Fx.WithA(m.Hue.Edge, 0.75f));
                st.Clock = null;
            }
            for (int i = 0; i < st.Marks.Count; i++)
            {
                m.Unglue(st.Marks[i]);
            }
            st.Marks.Clear();
            for (int i = 0; i < st.Sheens.Count; i++)
            {
                m.Unglue(st.Sheens[i]);
            }
            st.Sheens.Clear();
            st.Locked.Clear();
            st.Stopped = false;
            if (m.Vis)
            {
                Fx.Ring(m.Stance, m.Px, 0.3f, 60f * m.Kk, 4f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
            Kit.Stop(m.BodyPos(), 0.12f);
            Ruin.Quake(m.A, m.Stance, m.Scale, 1.5f, m.Kk);
            int level = m.Level;
            float best = -1f;
            Actor strongest = null;
            for (int i = 0; i < st.T.Count; i++)
            {
                if (st.Marked[i] && Valid(m, st, i) && Tiers.Power(st.T[i]) > best)
                {
                    best = Tiers.Power(st.T[i]);
                    strongest = st.T[i];
                }
            }
            for (int i = 0; i < st.T.Count; i++)
            {
                if (!st.Marked[i] || !Valid(m, st, i))
                {
                    continue;
                }
                Actor v = st.T[i];
                Vector2 dir = st.Dirs[i];
                Vector2 g = Victims.GroundOf(v);
                if (!m.Touch(v, true))
                {
                    continue;
                }
                Combat.Allowed(m.A, v, out float cm);
                Combat.Hit(m.A, v, Mult(level) * cm, true, false, 0f, m);
                if (Combat.Living(v))
                {
                    Impact im = Impact.Of(Hurt.Launch, dir, 2.6f + 0.6f * level, 1.6f);
                    im.Lift = 1.4f + 0.4f * level;
                    im.Daze = 0.8f;
                    Victims.Hurt(v, m.A, m, im);
                }
                else
                {
                    Victims.Flash(v, Color.white, 0.06f);
                }
                if (Kit.Visible(g))
                {
                    Vector2 body = Choreo.BodyAt(v, 0.55f);
                    ChoreoFx.Impact(body, dir, Choreo.PxOf(v), Choreo.KOf(v), 1.8f, m.Hue, FxLayer.Top, true);
                    if (v == strongest && level >= 3)
                    {
                        Kit.ImpactFrame(body, Choreo.PxOf(v), 0.4f * Choreo.KOf(v), m.Hue.Pal, true, FxLayer.Behind);
                    }
                    Kit.Crater(g, Choreo.PxOf(v) * Choreo.KOf(v), Combat.TileAt(g), true, 5f, level < 3, m.Hue.Edge);
                    Kit.Debris(g, Choreo.PxOf(v), Combat.TileAt(g), 5, Choreo.KOf(v));
                }
                Ruin.Blast(m.A, g, level >= 3 ? 0.5f : 0.4f, m.Scale, m.Pose.Ground);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile);
        }

        /// <summary>Whatever the stop still holds lets go (the move ended or was cut short).</summary>
        private static void Unlock(ChoreoMove m, State st)
        {
            if (st.Full)
            {
                Cinema.Unhold(m);
            }
            for (int i = 0; i < st.Locked.Count; i++)
            {
                Actor v = st.Locked[i];
                if (v != null && Combat.Living(v) && Victims.Get(v) != null)
                {
                    // the lock is let go: a twitch, and it moves again
                    Victims.Hurt(v, m.A, m, Impact.Of(Hurt.Flinch, PsHiKit.DirTo(m.Stance, v.current_position), 0.02f, 0.3f));
                }
            }
            st.Locked.Clear();
        }
    }
}
