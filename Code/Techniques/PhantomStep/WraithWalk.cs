using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// WRAITH WALK (Phantom Step, Grandmaster). It sinks into itself and goes dark: a shadow of its own
    /// shape, ragged smoke pouring off it, feet off the ground. For two to three seconds it is a wraith:
    /// nothing can touch it, and it glides - unhurried, never a blur - through the enemy, passing
    /// THROUGH one body after another; every body it passes is cut (dark claw lines crossing in it, a
    /// violet flash, the body spun half round). Then it comes back into itself beyond them, smoke
    /// bursting off it. A way through a crowd that has it surrounded, out from under an attack aimed
    /// at it (a beam, a charge, an area blow), or simply a path of cuts through a file of enemies.
    ///
    /// Levels: I a 2 s walk through up to 3 foes (x0.7 each). II 2.4 s, up to 5 (x0.85). III 2.8 s,
    /// up to 7 (x1), and the earth it glided over is left scarred black along its whole path.
    /// Anyone else it passes through on the way is cut too.
    /// </summary>
    internal sealed class WraithWalkTech : Technique
    {
        public override string Id => "wraith_walk";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Mobility;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Evasive | Form.Blade;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 30f, 48f, 72f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        private static int MaxCuts(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 5 : 7;
        }

        private static float MaxLen(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 11f : 13f;
        }

        /// <summary>Seconds it is a wraith (the glide and the dissolving and re-forming round it).</summary>
        private static float Walk(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.4f : 2.8f;
        }

        private static float Cut(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.85f : 1f;
        }

        private static readonly List<Actor> _cand = new List<Actor>();
        private static readonly List<Actor> _path = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            _cand.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 24; i++)
            {
                if (c.EnemyDist[i] > 7.5f * k + (c.Forced ? 3f : 0f))
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (Choreo.Fair(c.Self, v, out _, c.Forced) && PsHiKit.Standing(v))
                {
                    _cand.Add(v);
                }
            }
            if (_cand.Count == 0)
            {
                return null;
            }
            bool beam = Beams.Toward(c.Self, 1f) != null;
            bool escape = c.Surrounded || c.Desperate && c.Near4 >= 2 || beam;
            // which way: through the densest group, or (to get out) away from the worst of it towards allies
            Vector2 heading = Heading(c, escape);
            Vector2 exit;
            float len = FindRoute(c, level, heading, out exit);
            int n = _path.Count;
            int need = c.Forced ? 1 : escape ? 1 : 2;
            if (n < need || len <= 0f)
            {
                return null;
            }
            float score = 0f;
            for (int i = 0; i < n; i++)
            {
                Actor v = _path[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Cut(level)), cm);
            }
            if (c.Surrounded)
            {
                score += 40f;   // out of the ring, cutting
            }
            if (c.Desperate)
            {
                score += 30f;
            }
            if (beam)
            {
                score += 60f;   // the beam pours through a shadow
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = _path[0],
                Point = exit,
                Dir = PsHiKit.DirTo(c.Pos, exit),
                Count = n,
                Score = score,
                Others = new List<Actor>(_path),
                Data = c.Forced
            };
        }

        /// <summary>The way to set out: towards the most foes (weighted near), or away from them towards allies when escaping.</summary>
        private static Vector2 Heading(Ctx c, bool escape)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < _cand.Count; i++)
            {
                Vector2 d = _cand[i].current_position - c.Pos;
                float m = d.magnitude;
                if (m > 1e-3f)
                {
                    sum += d / (m * (0.5f + m * 0.25f));
                }
            }
            if (escape && c.Allies.Count > 0)
            {
                Vector2 ally = c.Allies[0].current_position - c.Pos;
                if (ally.sqrMagnitude > 1f)
                {
                    // through the thinnest side of the ring, towards help
                    Vector2 h = ally.normalized * 0.7f - sum.normalized * 0.3f;
                    if (h.sqrMagnitude > 1e-4f)
                    {
                        return h.normalized;
                    }
                }
            }
            if (sum.sqrMagnitude < 1e-6f)
            {
                return c.Focus != null ? PsHiKit.DirTo(c.Pos, c.Focus.current_position) : Vector2.right;
            }
            return sum.normalized;
        }

        /// <summary>
        /// The path through foes: from where it stands, the next foe each time is the nearest within 3.5
        /// tiles that lies ahead (within 75 degrees of the way it is going), with a clear way to it; it
        /// comes out 1.6 tiles past the last one. Fills _path; returns the length (0: no way).
        /// </summary>
        private static float FindRoute(Ctx c, int level, Vector2 heading, out Vector2 exit)
        {
            _path.Clear();
            float k = Choreo.KOf(c.Self);
            Vector2 at = c.Pos;
            Vector2 dir = heading;
            float len = 0f, maxLen = MaxLen(level) * Mathf.Lerp(1f, k, 0.5f);
            int max = MaxCuts(level);
            float cos = Mathf.Cos(75f * Mathf.Deg2Rad);
            while (_path.Count < max)
            {
                Actor best = null;
                float bd = float.MaxValue;
                for (int i = 0; i < _cand.Count; i++)
                {
                    Actor v = _cand[i];
                    if (_path.Contains(v))
                    {
                        continue;
                    }
                    Vector2 d = v.current_position - at;
                    float m = d.magnitude;
                    if (m < 0.05f || m > 3.5f * k || Vector2.Dot(d / m, dir) < cos || len + m > maxLen)
                    {
                        continue;
                    }
                    if (m < bd && Combat.ClearPath(at, v.current_position))
                    {
                        bd = m;
                        best = v;
                    }
                }
                if (best == null)
                {
                    break;
                }
                Vector2 nd = best.current_position - at;
                if (nd.sqrMagnitude > 1e-4f)
                {
                    dir = Vector2.Lerp(dir, nd.normalized, 0.6f).normalized;
                }
                len += nd.magnitude;
                at = best.current_position;
                _path.Add(best);
            }
            exit = at;
            if (_path.Count == 0)
            {
                return 0f;
            }
            Vector2 want = Combat.Clamp(at + dir * 1.6f * k);
            if ((Combat.SafeSpot(want) || Choreo.Wading(want)) && Combat.ClearPath(at, want))
            {
                exit = want;
            }
            else if (!PsHiKit.SpotAround(at, Mathf.Atan2(dir.y, dir.x), 1.2f * k, out exit) || !Combat.ClearPath(at, exit))
            {
                return 0f;
            }
            return len + Vector2.Distance(at, exit);
        }

        /// <summary>One walk: whom it passes through, who has been cut, the dark veil, the trail.</summary>
        private sealed class State
        {
            public readonly List<Vector2> Points = new List<Vector2>(8);
            public readonly List<Actor> Foes = new List<Actor>(8);
            public readonly List<long> Ids = new List<long>(8);
            public readonly HashSet<long> Cut = new HashSet<long>();
            public readonly List<Actor> Near = new List<Actor>();
            public Particle Veil;
            public float SmokeT, GhostT, ScanT;
            public Vector2 Dir, LegFrom;
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
            // the way: through each foe as it stands now, out beyond the last
            Vector2 at = m.Stance;
            float len = 0f;
            if (p.Others != null)
            {
                foreach (Actor v in p.Others)
                {
                    if (v == null || !Combat.Living(v) || st.Points.Count >= MaxCuts(level))
                    {
                        continue;
                    }
                    Vector2 q = v.current_position;
                    if (!Combat.ClearPath(at, q))
                    {
                        break;
                    }
                    len += Vector2.Distance(at, q);
                    st.Points.Add(q);
                    st.Foes.Add(v);
                    st.Ids.Add(v.Sid());
                    at = q;
                }
            }
            Vector2 exit = p.Point;
            if (!(Combat.SafeSpot(exit) || Choreo.Wading(exit)) || !Combat.ClearPath(at, exit))
            {
                if (!Combat.FindLanding(at, 2f, out exit))
                {
                    exit = at;
                }
            }
            len += Vector2.Distance(at, exit);
            st.Points.Add(exit);
            if (len < 0.5f)
            {
                return false;
            }
            // (a walk through many: none of them is "the" target an answer would stop it on)
            m.SetVictim(null);
            m.Face(PsHiKit.DirTo(m.Stance, st.Points[0], m.Dir));
            Form shows = Form.Teleport | Form.Evasive | Form.Blade;
            Hue dark = m.Hue.As(Element.Shadow);
            Color shade = Color.Lerp(dark.Dark, dark.Edge, 0.35f);
            // it sinks into itself and goes dark
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.16f,
                To = new Shape(8f, 1.1f, 0.88f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Intangible = true,
                Enter = mm =>
                {
                    st.Veil = PsHiKit.Veil(mm, 7);
                    if (mm.Vis)
                    {
                        ChoreoFx.Smoke(mm.Stance + new Vector2(0f, 2f * mm.Px), mm.Px, 6, 5f * mm.Kk, Fx.WithA(shade, 0.8f), 0.6f);
                        PsHiKit.Dial(mm.Stance, mm.Px, 3f * mm.Kk, 10f * mm.Kk, 0.4f, dark.Edge);
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                },
                Every = (mm, k) => PsHiKit.Cover(mm, st.Veil, Fx.WithA(shade, 0.92f * k))
            });
            // the glide: unhurried, the same pace all the way, through every body on the way
            float glide = Mathf.Max(0.6f, Walk(level) - 0.4f);
            float perTile = glide / len;
            for (int i = 0; i < st.Points.Count; i++)
            {
                Vector2 dest = st.Points[i];
                int idx = i;
                bool lastLeg = i == st.Points.Count - 1;
                bool foeLeg = i < st.Foes.Count;
                m.Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.001f,
                    PerTile = perTile,
                    Route = Route.Line,
                    PathEase = lastLeg ? Ease.OutQuad : Ease.Linear,
                    // (it glides at each foe where it is now, following it a little if it backs off)
                    Dest = mm => foeLeg ? Toward(mm, st, idx, dest) : Out(mm, st, dest),
                    Chase = foeLeg,
                    To = new Shape(12f, 0.94f, 1.06f, 0.24f, 0f),
                    Ease = Ease.InOutSine,
                    Forms = shows,
                    Intangible = true,
                    HideShadow = true,
                    Track = false,
                    Enter = mm =>
                    {
                        if (foeLeg)
                        {
                            st.Dir = PsHiKit.DirTo(mm.Stance, Toward(mm, st, idx, dest), mm.Dir);
                        }
                        st.LegFrom = mm.Stance;
                        mm.Face(st.Dir);
                    },
                    Every = (mm, k) => Glide(mm, st, shade, dark),
                    Exit = mm =>
                    {
                        if (mm.Level >= 3)
                        {
                            // the earth it glided over is scarred black behind it
                            Ruin.Furrow(mm.A, st.LegFrom, mm.Stance, 0.5f, mm.Scale, mm.Pose.Ground, false);
                        }
                    }
                });
            }
            // it comes back into itself beyond them
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.22f,
                To = new Shape(-4f, 1.08f, 0.94f, 0f, 0f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Track = false,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.Smoke(mm.BodyPos(), mm.Px, 8, 6f * mm.Kk, Fx.WithA(shade, 0.85f), 0.7f);
                        Fx.Ring(mm.Stance, mm.Px, 0.25f, 2f, 12f * mm.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.4f, Fx.WithA(dark.Edge, 0.9f), Fx.WithA(dark.Edge, 0f));
                        Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                    }
                },
                Every = (mm, k) => PsHiKit.Cover(mm, st.Veil, Fx.WithA(shade, 0.92f * (1f - k)))
            });
            m.Recover(0.22f).OnEnter(mm =>
            {
                if (st.Veil != null)
                {
                    mm.Unglue(st.Veil);
                    st.Veil = null;
                }
            });
            return m.Start();
        }

        /// <summary>Where a leg through a foe leads: the foe where it stands now (within 3 tiles of where it was), else where it was.</summary>
        private static Vector2 Toward(ChoreoMove m, State st, int i, Vector2 fallback)
        {
            Actor v = st.Foes[i];
            if (v != null && v.Sid() == st.Ids[i] && Combat.Living(v) && PsHiKit.Standing(v))
            {
                Vector2 g = Victims.GroundOf(v);
                if ((g - fallback).sqrMagnitude < 9f)
                {
                    return g;
                }
            }
            return fallback;
        }

        /// <summary>Where it comes out: on along the way it was going, past the last body (else where it was meant to).</summary>
        private static Vector2 Out(ChoreoMove m, State st, Vector2 planned)
        {
            Vector2 want = Combat.Clamp(m.Stance + st.Dir * 1.6f * m.Kk);
            if ((Combat.SafeSpot(want) || Choreo.Wading(want)) && Combat.ClearPath(m.Stance, want))
            {
                return want;
            }
            if ((Combat.SafeSpot(planned) || Choreo.Wading(planned)) && Combat.ClearPath(m.Stance, planned))
            {
                return planned;
            }
            return m.Stance;
        }

        /// <summary>A frame of the glide: the dark veil, smoke pouring off, shadow afterimages, and every body it passes through cut.</summary>
        private static void Glide(ChoreoMove m, State st, Color shade, Hue dark)
        {
            PsHiKit.Cover(m, st.Veil, Fx.WithA(shade, 0.92f));
            float dt = Clock.Dt;
            st.SmokeT -= dt;
            st.GhostT -= dt;
            st.ScanT -= dt;
            if (m.Vis)
            {
                if (st.SmokeT <= 0f)
                {
                    st.SmokeT = 0.035f;
                    Vector2 tail = m.BodyPos() - st.Dir * 2f * m.Px + new Vector2(Random.Range(-2f, 2f), Random.Range(-3f, 2f)) * m.Px;
                    Particle s = Fx.Spawn(Art.Dust[Random.Range(0, Art.Dust.Length)], tail, m.Px, Random.Range(0.4f, 0.65f), FxLayer.Behind, 3);
                    s?.Moving(-st.Dir * Random.Range(4f, 10f) * m.Px + new Vector2(0f, 4f * m.Px), 0f, 1.5f).Sized(0.8f, 1.5f)
                        .Color(Fx.WithA(shade, 0.8f), Fx.WithA(dark.Main, 0.45f), Fx.WithA(dark.Main, 0f));
                }
                if (st.GhostT <= 0f)
                {
                    st.GhostT = 0.06f;
                    Fx.Afterimage(m, 0.3f, Fx.WithA(shade, 0.55f), Fx.WithA(dark.Edge, 0f));
                }
            }
            if (st.ScanT > 0f)
            {
                return;
            }
            st.ScanT = 0.03f;
            // every body it passes through is cut
            Choreo.Foes(m.A, m.Pose.Ground, 0.6f * m.Kk, st.Near, false, true);
            for (int i = 0; i < st.Near.Count; i++)
            {
                Actor u = st.Near[i];
                long id = u.Sid();
                if (st.Cut.Contains(id) || !Choreo.Fair(m.A, u, out _, true))
                {
                    continue;
                }
                st.Cut.Add(id);
                CutThrough(m, u, st.Dir, dark);
            }
        }

        private static void CutThrough(ChoreoMove m, Actor u, Vector2 dir, Hue dark)
        {
            Vector2 body = Choreo.BodyAt(u, 0.55f);
            if (!m.Touch(u))
            {
                return;   // (it read the shadow coming and was not there)
            }
            Hit h = new Hit(Blow.Slash, Cut(m.Level)) { Hurt = Hurt.Spin, Push = 0.45f, Lethal = true, Power = 0.95f };
            m.Deliver(u, h, ChoreoMove.ShapeOf(Blow.Slash), body, dir);
            Victims.Flash(u, m.Hue.Main, 0.07f);
            if (m.Vis)
            {
                // dark claw lines crossing in the body
                float px = Choreo.PxOf(u);
                float half = 5f * Mathf.Clamp(Choreo.SpritePx(u) / 10f, 0.8f, 2.5f) * px;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 ld = Choreo.Rotate(dir, s * 50f);
                    Kit.Line(body - ld * half, body + ld * half, px, 0.16f, 2, m.TopLayer, 15, Fx.WithA(dark.Dark, 0.95f), Fx.WithA(dark.Edge, 0.7f), Fx.WithA(dark.Edge, 0f));
                    Kit.Line(body - ld * half * 0.8f, body + ld * half * 0.8f, px, 0.1f, 1, m.TopLayer, 16, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Main, 0f));
                }
                ChoreoFx.Smoke(body, px, 3, 3f, Fx.WithA(dark.Main, 0.7f), 0.45f);
            }
        }
    }
}
