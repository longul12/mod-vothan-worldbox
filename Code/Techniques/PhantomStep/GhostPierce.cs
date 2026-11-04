using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GHOST PIERCE (Phantom Step, Master). The body goes thin: a violet veil of itself settles over
    /// it, motes lift off it, and it drifts - no blur, no dash, a calm glide - straight THROUGH the foe,
    /// one palm out, intangible. As it passes inside, the foe's body ripples (a ring of light in its
    /// chest, a shudder, a violet glow left burning where the hand went through) but nothing more...
    /// until it stands on the far side, solid again, its back turned. Then the wound opens: violet
    /// light bursts out of the foe's back along the line it was crossed, and it folds. Armour means
    /// nothing to a hand that was never there (the blow is dealt as if the foe wore none). It goes
    /// through a raised guard as easily (planned more readily against one).
    ///
    /// Levels: I one pass (x2.2), the foe folds, dazed. II passes back through on a slant, two
    /// wounds open one after the other (x1.2 + x1.6), the foe crumples; the ground under it dents.
    /// III three passes in a star, three wounds, the last one an impact frame and a crater (x0.9 +
    /// x1.1 + x1.7). Damage x the user's damage, armour ignored (at most x3).
    /// </summary>
    internal sealed class GhostPierceTech : Technique
    {
        public override string Id => "ghost_pierce";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 20f, 34f, 52f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static readonly float[][] Wounds =
        {
            new[] { 2.2f },
            new[] { 1.2f, 1.6f },
            new[] { 0.9f, 1.1f, 1.7f }
        };

        public static float Total(int level)
        {
            float t = 0f;
            foreach (float w in Wounds[Mathf.Clamp(level, 1, 3) - 1])
            {
                t += w;
            }
            return t;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 3.2f, Total = Total(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 1.25f, Worth = 14f },
            new MeleeWant { Reach = 3.4f, Total = Total(2), Crowd = 0.05f, Alone = 9f, IntoGuard = 1.25f, Worth = 18f },
            new MeleeWant { Reach = 3.6f, Total = Total(3), Crowd = 0.04f, Alone = 10f, IntoGuard = 1.25f, Worth = 22f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // it must come out on the far side: solid ground beyond the foe, nothing in the way
            float k = Choreo.KOf(c.Self);
            Vector2 dir = PsHiKit.DirTo(c.Pos, v.current_position);
            Vector2 exit = v.current_position + dir * (0.7f * k + v.stats["size"] * 0.5f + 0.35f * k);
            if (!Combat.SafeSpot(exit) && !Choreo.Wading(exit) || !Combat.ClearPath(c.Pos, exit))
            {
                return null;
            }
            // what armour would have stopped counts in full; a raised guard is no wall to it
            p.Score *= Mathf.Lerp(1f, PsHiKit.Pierce(v), 0.6f);
            if (c.State(v).Guarding)
            {
                p.Score *= 1.3f;
            }
            return p;
        }

        /// <summary>One pierce: the veil over the body, the wounds left glowing in the foe, which way each pass went.</summary>
        private sealed class State
        {
            public Particle Veil;
            public readonly List<Particle> Wounds = new List<Particle>(3);
            public readonly List<Vector2> Dirs = new List<Vector2>(3);
            public readonly bool[] Opened = new bool[3];
            public bool Inside;
            public Vector2 Dest;
            public float Prev;
            public float GhostT, MoteT;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            int passes = Mathf.Clamp(level, 1, 3);
            State st = new State();
            Form shows = Form.Teleport | Form.Straight;
            // the body goes thin: a veil of violet settles over it, motes lift off
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.15f,
                To = new Shape(-8f, 0.95f, 1.06f, 0.06f, -0.06f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm =>
                {
                    st.Veil = PsHiKit.Veil(mm);
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.HeadPos(), mm.Px, 0.12f, mm.Hue.Core, 0.7f);
                        PsHiKit.Dial(mm.Stance, mm.Px, 3f * mm.Kk, 9f * mm.Kk, 0.3f, mm.Hue.Edge);
                    }
                },
                Every = (mm, k) =>
                {
                    PsHiKit.Cover(mm, st.Veil, Fx.WithA(Veiled(mm), 0.75f * k));
                    Motes(mm, st, 0.05f);
                }
            });
            for (int i = 0; i < passes; i++)
            {
                int pass = i;
                if (i > 0)
                {
                    // it turns in the open on the far side, still a ghost
                    m.Add(new Beat
                    {
                        Tempo = Tempo.Follow,
                        Dur = 0.08f,
                        To = new Shape(-6f, 0.96f, 1.05f, 0.08f, 0f),
                        Ease = Ease.OutQuad,
                        Forms = shows,
                        Intangible = true,
                        HideShadow = true,
                        Every = (mm, k) =>
                        {
                            PsHiKit.Cover(mm, st.Veil, Fx.WithA(Veiled(mm), 0.75f));
                            Motes(mm, st, 0.04f);
                        }
                    });
                }
                m.Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.1f,
                    PerTile = 0.075f,
                    Route = Route.Line,
                    PathEase = Ease.InOutSine,
                    Dest = mm => PassEnd(mm, st, pass),
                    To = new Shape(16f, 0.92f, 1.08f, 0.08f, 0f),
                    Ease = Ease.OutQuad,
                    Forms = shows,
                    Intangible = true,
                    HideShadow = true,
                    Track = false,
                    Enter = mm =>
                    {
                        st.Inside = false;
                        st.Prev = float.MaxValue;
                        mm.Face(st.Dest - mm.Stance);
                        Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    },
                    Every = (mm, k) => Drift(mm, st, pass, k),
                    Exit = mm =>
                    {
                        if (!st.Inside)
                        {
                            Inside(mm, st, pass);   // (it went round the body's edge: it still went through)
                        }
                    }
                });
            }
            // solid again on the far side, its back turned
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.16f,
                To = new Shape(3f, 1.04f, 0.97f, 0f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Track = false,
                Enter = mm =>
                {
                    if (mm.V != null)
                    {
                        mm.Face(mm.Stance - mm.VG);
                    }
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                    }
                },
                Every = (mm, k) => PsHiKit.Cover(mm, st.Veil, Fx.WithA(Veiled(mm), 0.75f * (1f - k)))
            });
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.06f, Forms = shows, Track = false });   // (its back stays turned)
            // the wounds open, one after the other
            m.Add(new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.06f + 0.07f * passes,
                Forms = shows,
                Track = false,
                Enter = mm =>
                {
                    if (st.Veil != null)
                    {
                        mm.Unglue(st.Veil);
                        st.Veil = null;
                    }
                },
                Every = (mm, k) =>
                {
                    for (int j = 0; j < passes; j++)
                    {
                        if (!st.Opened[j] && k >= j / (float)passes * 0.85f)
                        {
                            st.Opened[j] = true;
                            Open(mm, st, j, passes);
                        }
                    }
                }
            });
            m.Do(mm =>
            {
                if (mm.V != null && mm.V.Sid() == mm.VId)
                {
                    mm.Face(mm.VG - mm.Stance);
                }
            });
            m.Recover(0.22f).OnEnter(mm =>
            {
                if (st.Veil != null)
                {
                    PsHiKit.Fade(mm, st.Veil, 0.12f, Fx.WithA(Veiled(mm), 0.6f));
                    st.Veil = null;
                }
            });
            return m.Start();
        }

        /// <summary>The ghost's colour: the school's violet, paler at level I.</summary>
        private static Color Veiled(ChoreoMove m)
        {
            return Color.Lerp(m.Hue.Main, m.Hue.Edge, 0.25f);
        }

        /// <summary>Where a pass comes out: through the foe and a body's length beyond it (the second and third on a slant).</summary>
        private static Vector2 PassEnd(ChoreoMove m, State st, int pass)
        {
            Vector2 vg = m.V != null ? m.VG : m.Stance + m.Dir * m.Stand;
            Vector2 dir = PsHiKit.DirTo(m.Stance, vg, m.Dir);
            if (pass > 0)
            {
                dir = Choreo.Rotate(dir, pass == 1 ? 30f : -34f);
            }
            float through = m.Stand + m.VHeight * 0.2f + 0.35f * m.Kk;
            Vector2 end = Combat.Clamp(vg + dir * through);
            if (!(Combat.SafeSpot(end) || Choreo.Wading(end)) || !Combat.ClearPath(m.Stance, end))
            {
                if (!PsHiKit.SpotAround(vg, Mathf.Atan2(dir.y, dir.x), through, out end) || !Combat.ClearPath(m.Stance, end))
                {
                    end = m.Stance;   // nowhere to come out: it stays this side (the wound still opens)
                }
            }
            st.Dest = end;
            st.Dirs.Add(PsHiKit.DirTo(m.Stance, end, dir));
            return end;
        }

        /// <summary>A frame of the drift: the veil on the body, trailing ghosts, and the moment it is inside the foe.</summary>
        private static void Drift(ChoreoMove m, State st, int pass, float k)
        {
            PsHiKit.Cover(m, st.Veil, Fx.WithA(Veiled(m), 0.78f));
            st.GhostT -= Clock.Dt;
            if (st.GhostT <= 0f && m.Vis)
            {
                st.GhostT = 0.04f;
                Fx.Afterimage(m, 0.2f, Fx.WithA(Veiled(m), 0.45f), Fx.WithA(m.Hue.Edge, 0f));
            }
            Motes(m, st, 0.035f);
            if (!st.Inside && m.V != null)
            {
                // inside it: within its body, or just past the nearest point of the line through it
                float inside = m.V.stats["size"] * 0.5f + 0.3f * m.Kk;
                float d = Vector2.Distance(m.Pose.Ground, m.VG);
                if (d <= inside || k > 0.2f && d > st.Prev + 0.002f)
                {
                    Inside(m, st, pass);
                }
                st.Prev = d;
            }
        }

        /// <summary>The hand inside the foe: its body ripples and shudders; a wound of violet light stays burning there.</summary>
        private static void Inside(ChoreoMove m, State st, int pass)
        {
            st.Inside = true;
            if (!m.VOk)
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            if (pass == 0 && !m.Touch(v))
            {
                return;   // answered (it slipped away, or met it): the engine ends the move
            }
            Vector2 dir = pass < st.Dirs.Count ? st.Dirs[pass] : m.Dir;
            Combat.Pin(v, 0.8f);
            Victims.Hurt(v, m.A, m, Impact.Of(Hurt.Flinch, dir, 0.03f, 0.5f));
            Victims.Flash(v, m.Hue.Main, 0.08f);
            Choreo.Sound("event:/SFX/HIT/HitGeneric", v.current_tile, 0.1f);
            float frac = 0.45f + 0.1f * pass;
            Vector2 body = Choreo.BodyAt(v, frac);
            if (m.Vis)
            {
                // a ring of light rippling through the body, standing up
                Fx.Ring(body, m.Px, 0.22f, 2f, 7f * m.Kk, 1, 1, FxLayer.Over, 9, 1.7f, 2.4f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                ChoreoFx.Motes(body, m.Px, 5, 6f, 0.14f, m.Hue.Core, m.Hue.Edge);
            }
            // the wound: a small burning core in the foe that follows it until it opens
            Particle w = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(1.5f * m.Kk), 1, 4), 0.85f, 0.3f), body, m.Px, 1f, FxLayer.Over, 10);
            if (w != null)
            {
                w.Color(Fx.WithA(m.Hue.Core, 0.95f));
                long id = v.Sid();
                float vpx = Choreo.PxOf(v);
                float off = (pass - 1) * 1.2f * vpx;
                Color core = Color.Lerp(Color.white, m.Hue.Core, 0.5f);
                w.Step = (q, dt) =>
                {
                    if (v.Sid() == id && v.isAlive())
                    {
                        q.Pos = Choreo.BodyAt(v, frac) + new Vector2(off, 0f);
                    }
                    if (q.Grad != null && q.Grad.Length > 0)
                    {
                        q.Grad[0] = Fx.WithA(core, 0.65f + 0.3f * Mathf.Sin(q.Age * 40f));   // it throbs
                    }
                };
                m.Glue(w);
                st.Wounds.Add(w);
            }
            else
            {
                st.Wounds.Add(null);
            }
        }

        /// <summary>A wound opens: the light bursts out of the foe's back along the pass, and the blow lands (armour ignored).</summary>
        private static void Open(ChoreoMove m, State st, int j, int passes)
        {
            Particle w = j < st.Wounds.Count ? st.Wounds[j] : null;
            Vector2 at = w != null && w.Alive ? w.Pos : m.V != null ? Choreo.BodyAt(m.V, 0.5f) : m.BodyPos();
            if (w != null)
            {
                m.Unglue(w);
            }
            if (!m.VOk || j >= st.Wounds.Count)
            {
                return;
            }
            Actor v = m.V;
            bool last = j == passes - 1;
            int level = m.Level;
            float mult = Wounds[Mathf.Clamp(level, 1, 3) - 1][j] * PsHiKit.Pierce(v);
            Vector2 dir = j < st.Dirs.Count ? st.Dirs[j] : m.Dir;
            Hit h = new Hit(Blow.Palm, mult)
            {
                Hurt = last ? (level <= 1 ? Hurt.Fold : Hurt.Crumple) : Hurt.Flinch,
                Push = last ? 0.25f : 0.05f,
                Daze = last ? 0.5f + 0.3f * level : 0f,
                Finisher = last,
                Lethal = last,
                Power = last ? 1.25f : 0.8f
            };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Palm), at, dir);
            if (m.Vis)
            {
                // the light bursts out of its back along the line the hand crossed
                float px = Choreo.PxOf(v);
                int n = Fx.Count(last ? 14 : 8);
                for (int i = 0; i < n; i++)
                {
                    Vector2 d = Choreo.Rotate(dir, Random.Range(-28f, 28f));
                    Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.2f, 0.4f), m.TopLayer, 9);
                    p?.Moving(d * Random.Range(60f, 140f) * px, Random.Range(0f, 15f) * px, 4f).Sized(2f, 1f)
                        .Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                }
                Kit.Line(at, at + dir * (6f + 3f * level) * px * m.Kk, px, 0.12f, last ? 2 : 1, m.TopLayer, 16, Color.white, Fx.WithA(m.Hue.Main, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.Flash(at, px, Mathf.RoundToInt((last ? 4f : 2.5f) * m.Kk), 0.07f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
            }
            if (last && level >= 2)
            {
                Vector2 g = m.VG;
                if (m.Vis)
                {
                    Kit.Crater(g, m.Px * m.Kk, Combat.TileAt(g), false, 4f, level < 3, m.Hue.Edge);
                }
                Ruin.Blast(m.A, g, level == 2 ? 0.5f : 0.9f, m.Scale, m.Pose.Ground);
            }
        }

        /// <summary>Motes of violet light lifting off the thinned body.</summary>
        private static void Motes(ChoreoMove m, State st, float every)
        {
            st.MoteT -= Clock.Dt;
            if (st.MoteT > 0f || !m.Vis)
            {
                return;
            }
            st.MoteT = every;
            Vector2 at = m.BodyPos() + new Vector2(Random.Range(-3f, 3f), Random.Range(-4f, 4f)) * m.Px;
            Particle p = Fx.Spawn(Art.Pixel, at, m.Px, Random.Range(0.25f, 0.45f), FxLayer.Over, 8);
            p?.Moving(new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 26f)) * m.Px, 0f, 1.5f).Sized(1.4f, 0.8f)
                .Color(Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Main, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
        }
    }
}
