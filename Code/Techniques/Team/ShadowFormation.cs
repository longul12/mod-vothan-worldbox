using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SHADOW FORMATION (team sync, Expert). Three friends form a triangle in front of a group of
    /// foes: the leader at the point, the two others blurring into the back corners. A violet
    /// outline lights the triangle between them and all three drop into the same low stance on the
    /// same frame; then the whole triangle blurs through the group as one shape (all three
    /// untouchable, afterimages, the outline dragged along between them). Every foe the triangle
    /// passes over, or that a corner brushes, is cut by the nearest of the three and spun aside,
    /// one after another as the shape sweeps on. On the far side they skid to a stop in the same
    /// pose, and a beat later the cuts glow violet on everyone they struck.
    /// I: one pass; II: they pivot together and sweep back through; III: the return pass flings
    /// everything it cuts into the air (Scattered) and leaves three shadow-scarred furrows.
    /// Damage (x the striker's own damage): each cut I 0.9, II 1.15, III 1.4.
    /// </summary>
    internal sealed class ShadowFormationTech : Technique
    {
        public override string Id => "shadow_formation";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Dash | Form.Line | Form.Blade | Form.Fast | Form.Team;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 24f, 40f, 62f };
        public override float[] Cooldown => new[] { 14f, 15f, 16f };

        private static readonly float[] Shares = { 14f, 22f, 34f };
        private static readonly List<Actor> _ray = new List<Actor>();
        private static readonly List<Actor> _near = new List<Actor>();
        /// <summary>The sweep beats' tag (the sync's tick strikes only while its members sweep).</summary>
        private static readonly object SweepTag = new object();

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float Cut(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.15f : 1.4f;
        }

        private static int Passes(int level)
        {
            return level <= 1 ? 1 : 2;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c) || c.Allies.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            float k = Mathf.Min(1.4f, Choreo.KOf(c.Self));
            float len = 8f * k;
            Vector2 dir = c.BestRay(len, 1.1f * k, out int count);
            if (c.ForceTarget != null && c.Dist(c.ForceTarget) > 0.5f)
            {
                dir = (c.ForceTarget.current_position - c.Pos).normalized;
            }
            _ray.Clear();
            count = c.OnRay(dir, len, 1.1f * k, _ray);
            int need = c.Forced ? 1 : c.Linked ? 2 : 3;
            if (count < need && !(count >= 2 && Worthy(_ray)))
            {
                return null;
            }
            // the band: up to just past the furthest foe in it
            float far = 0f;
            Actor first = null;
            float firstAlong = 99f;
            for (int i = 0; i < _ray.Count; i++)
            {
                float along = Vector2.Dot(_ray[i].current_position - c.Pos, dir);
                far = Mathf.Max(far, along);
                if (along < firstAlong)
                {
                    firstAlong = along;
                    first = _ray[i];
                }
            }
            if (first == null)
            {
                return null;
            }
            float L = Mathf.Clamp(far + 1.6f * k, 3f, 9.5f);
            List<Actor> partners = Syncs.Pick(c, first, level, 2, Share(level), 5f, 12f);
            if (partners.Count < (c.Forced ? 1 : 2) || !Lanes(c.Self, partners, c.Pos, dir, L, null))
            {
                return null;
            }
            float score = 0f;
            int passes = Passes(level);
            for (int i = 0; i < _ray.Count; i++)
            {
                Actor v = _ray[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Cut(level) * passes), cm);
            }
            score += Syncs.Bonus(c, first, partners);
            return new Plan
            {
                Target = first,
                Point = c.Pos + dir * L,
                Dir = dir,
                Count = count,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
                Others = new List<Actor>(partners),
                Data = c.Forced,
                AreaRadius = L * 0.5f
            };
        }

        /// <summary>Two foes in the band are enough if one of them is a real fighter (tiered, or a brute and up).</summary>
        private static bool Worthy(List<Actor> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Tiers.Of(list[i]) != Tier.None || Weights.Species(list[i]) >= Weight.Brute)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The triangle's lanes: the leader's from where it stands along 'dir' for 'len' tiles; the two
        /// back corners 0.9 tiles behind it and 0.85 to either side (each friend takes the corner nearer
        /// to it). Every start and end on safe ground, every lane clear and each friend able to reach its
        /// corner. Fills 'starts' (leader first) when it is given.
        /// </summary>
        private static bool Lanes(Actor lead, List<Actor> partners, Vector2 p0, Vector2 dir, float len, Vector2[] starts)
        {
            float k = Mathf.Lerp(1f, Choreo.KOf(lead), 0.5f);
            Vector2 side = Choreo.Perp(dir);
            Vector2 left = p0 - dir * 0.9f * k + side * 0.85f * k;
            Vector2 right = p0 - dir * 0.9f * k - side * 0.85f * k;
            if (!Lane(p0, dir, len))
            {
                return false;
            }
            bool firstLeft = partners.Count < 2 || (partners[0].current_position - left).sqrMagnitude <= (partners[1].current_position - left).sqrMagnitude;
            for (int i = 0; i < partners.Count && i < 2; i++)
            {
                Vector2 s = (i == 0) == firstLeft ? left : right;
                if (!Lane(s, dir, len))
                {
                    return false;
                }
                Vector2 up = partners[i].current_position;
                if (Vector2.Distance(up, s) > 0.3f && !Combat.ClearPath(up, s))
                {
                    return false;
                }
                if (starts != null)
                {
                    starts[i + 1] = s;
                }
            }
            if (starts != null)
            {
                starts[0] = p0;
            }
            return true;
        }

        private static bool Lane(Vector2 s, Vector2 dir, float len)
        {
            Vector2 e = Combat.Clamp(s + dir * len);
            return Combat.SafeSpot(s) && Combat.SafeSpot(e) && Combat.ClearPath(s, e);
        }

        public override bool Begin(Actor a, Plan p)
        {
            bool forced = p.Data is bool b && b;
            if (p.Others == null || !Moves.CanPerform(a) || p.Dir.sqrMagnitude < 1e-4f)
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            Actor first = p.Target;
            List<Actor> partners = Syncs.Recheck(a, first, p.Others, 2, Share(level), forced, 7f);
            if (partners.Count < (forced ? 1 : 2))
            {
                return false;
            }
            Vector2 dir = p.Dir.normalized;
            Vector2 p0 = a.current_position;
            float len = Mathf.Clamp(Vector2.Distance(p0, p.Point), 3f, 9.5f);
            Vector2[] starts = new Vector2[partners.Count + 1];
            if (!Lanes(a, partners, p0, dir, len, starts))
            {
                if (first != null)
                {
                    Crew.Fail("shadow_formation", first);
                }
                return false;
            }
            TeamSync s = new TeamSync(this, level, forced, first, p.Point, dir);
            Sweep sw = new Sweep { Len = len };
            s.Data = sw;
            for (int i = 0; i < starts.Length; i++)
            {
                SyncMove m = s.Join(i == 0 ? a : partners[i - 1]);
                m.Mark = starts[i];
                m.Mark2 = Combat.Clamp(starts[i] + dir * len);
                Script(m, sw);
            }
            s.OnTick = (ss, dt) => Tick(ss, sw, dt);
            return s.Go(forced ? 1 : 2, Share(level), 6f);
        }

        // ------------------------------------------------------------ script --

        /// <summary>The formation's pass in play: its number, who it struck (for the afterglow), the outline's flicker.</summary>
        private sealed class Sweep
        {
            public float Len;
            public int Pass;
            public float LineT;
            public bool Flung;
            public readonly List<Actor> Cut = new List<Actor>(12);
        }

        private static void Script(SyncMove m, Sweep sw)
        {
            TeamSync s = m.S;
            int L = s.Level;
            Vector2 start = m.Mark, end = m.Mark2;
            Vector2 dir = s.Dir;
            m.Call();
            if (!m.Leader)
            {
                // a blur into its corner of the triangle
                m.Dash(mm => start, 0.025f);
                m.OnlyIf(mm => Vector2.Distance(mm.Stance, start) > 0.3f);
            }
            m.Do(mm => mm.Face(dir));
            m.Meet("form", 1f);
            m.Do(mm => Formed(s));
            for (int pass = 0; pass < Passes(L); pass++)
            {
                bool back = pass == 1;
                Vector2 to = back ? start : end;
                Vector2 way = back ? -dir : dir;
                int n = pass;
                // the set: low, hands back, the same pose on the same frame
                m.PoseTo(back ? 0.1f : 0.16f, new Shape(16f, 1.14f, 0.84f, 0f, -0.06f), Ease.OutQuad, Tempo.Free);
                m.Last.Tremble = true;
                m.Meet("go" + n, 0.35f);
                // the sweep: the triangle blurs through as one shape
                Beat sweep = new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.08f + 0.022f * sw.Len,
                    Route = Route.Line,
                    PathEase = Ease.OutQuad,
                    Dest = mm => to,
                    To = new Shape(22f, 1.26f, 0.84f, 0f),
                    Ease = Ease.OutQuad,
                    Intangible = true,
                    Ghosts = 0.016f,
                    Lines = 0.03f,
                    Track = false,
                    Forms = Form.Dash | Form.Line | Form.Blade | Form.Fast | Form.Team,
                    Tag = SweepTag
                };
                sweep.Enter = mm =>
                {
                    mm.SetVictim(null);   // (it strikes whatever the shape passes over: no one target to face)
                    mm.Face(way);
                    if (s.Once("pass" + n))
                    {
                        s.Struck.Clear();
                        sw.Pass = n;
                        sw.Cut.Clear();
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                        ChoreoFx.HeelDust(mm.Stance, -way, mm.Px, mm.A.current_tile, mm.Kk);
                    }
                };
                m.Add(sweep);
                // the stop: a skid, then the same frozen follow-through for a beat
                m.PoseTo(0.1f, new Shape(-10f, 1.12f, 0.9f, 0f), Ease.OutExpo, Tempo.Free);
                m.Dusting(0.04f);
                m.Hold(0.12f, false, Tempo.Free);
                m.Do(mm => Afterglow(s, sw, n));
                if (pass + 1 < Passes(L))
                {
                    // they pivot together to face back the way they came
                    m.Do(mm => mm.Face(-way));
                    m.PoseTo(0.08f, new Shape(-6f, 0.9f, 1.06f, 0f), Ease.OutBack, Tempo.Free);
                }
            }
            if (L >= 3)
            {
                m.Do(mm => Scar((SyncMove)mm));
            }
            // (they end far from what they cut: no close-range chain, only the scattered crowd at III)
            m.Do(mm => mm.SetLink(L >= 3 && ((SyncMove)mm).Leader && sw.Flung ? Link.Scattered : Link.None, null));
            m.Recover(0.25f);
        }

        /// <summary>The triangle forms: a violet outline lights between the three, a glint on each, together.</summary>
        private static void Formed(TeamSync s)
        {
            if (!s.Once("formed"))
            {
                return;
            }
            Outline(s, 0.22f, 2);
            s.Ting("formed-ting");
        }

        /// <summary>The outline of the formation between its members' bodies (violet, the members' tier at the rim).</summary>
        private static void Outline(TeamSync s, float life, int thick)
        {
            SyncMove prev = null, first = null;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove m = s.Cast[i];
                if (!m.In)
                {
                    continue;
                }
                if (prev != null)
                {
                    Edge(prev, m, life, thick);
                }
                else
                {
                    first = m;
                }
                prev = m;
            }
            if (prev != null && first != null && prev != first && s.Live >= 3)
            {
                Edge(prev, first, life, thick);
            }
        }

        private static void Edge(SyncMove a, SyncMove b, float life, int thick)
        {
            if (!a.Vis && !b.Vis)
            {
                return;
            }
            Hue h = a.Hue.As(Element.Shadow);
            Kit.Line(a.BodyPos(), b.BodyPos(), a.Px, life, thick, FxLayer.Behind, 3, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Main, 0.7f), Fx.WithA(h.Rim, 0f));
        }

        /// <summary>
        /// Once a frame while the formation sweeps: the outline dragged along between them, and every foe
        /// inside the triangle (or brushed by a corner) cut once by the nearest of them and spun aside.
        /// </summary>
        private static void Tick(TeamSync s, Sweep sw, float dt)
        {
            SyncMove any = null;
            int sweeping = 0;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove m = s.Cast[i];
                if (m.In && m.Current != null && m.Current.Tag == SweepTag)
                {
                    sweeping++;
                    any ??= m;
                }
            }
            if (any == null)
            {
                return;
            }
            sw.LineT -= dt;
            if (sw.LineT <= 0f)
            {
                sw.LineT = 0.03f;
                Outline(s, 0.12f, 2);
            }
            // the shape now: the members' places (the point and the corners)
            Vector2 mid = Vector2.zero;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                if (s.Cast[i].In)
                {
                    mid += s.Cast[i].Pose.Ground;
                }
            }
            int live = s.Live;
            if (live == 0)
            {
                return;
            }
            mid /= live;
            float reach = 0f;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                if (s.Cast[i].In)
                {
                    reach = Mathf.Max(reach, Vector2.Distance(mid, s.Cast[i].Pose.Ground));
                }
            }
            Choreo.Foes(any.A, mid, reach + 1f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                long id = u.Sid();
                if (s.Struck.Contains(id))
                {
                    continue;
                }
                Vector2 up = u.current_position;
                float pad = 0.55f + u.stats["size"] * 0.5f;
                SyncMove by = null;
                float bd = float.MaxValue;
                bool inside = live >= 3 && Inside(s, up);
                for (int j = 0; j < s.Cast.Count; j++)
                {
                    SyncMove m = s.Cast[j];
                    if (!m.In || m.Current == null || m.Current.Tag != SweepTag)
                    {
                        continue;
                    }
                    float d = Vector2.Distance(m.Pose.Ground, up);
                    if ((inside || d <= pad) && d < bd)
                    {
                        bd = d;
                        by = m;
                    }
                }
                if (by == null)
                {
                    continue;
                }
                s.Struck.Add(id);
                Strike(s, sw, by, u);
            }
        }

        /// <summary>Is 'p' inside the triangle the first three members still in it make?</summary>
        private static bool Inside(TeamSync s, Vector2 p)
        {
            Vector2 a = Vector2.zero, b = Vector2.zero, c = Vector2.zero;
            int n = 0;
            for (int i = 0; i < s.Cast.Count && n < 3; i++)
            {
                if (!s.Cast[i].In)
                {
                    continue;
                }
                Vector2 g = s.Cast[i].Pose.Ground;
                if (n == 0)
                {
                    a = g;
                }
                else if (n == 1)
                {
                    b = g;
                }
                else
                {
                    c = g;
                }
                n++;
            }
            if (n < 3)
            {
                return false;
            }
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        /// <summary>One cut: spun aside off the formation's line (III's return pass flings it into the air).</summary>
        private static void Strike(TeamSync s, Sweep sw, SyncMove by, Actor u)
        {
            int L = s.Level;
            Vector2 way = sw.Pass == 1 ? -s.Dir : s.Dir;
            Vector2 side = Choreo.Perp(way);
            float sign = Vector2.Dot(u.current_position - by.Pose.Ground, side) >= 0f ? 1f : -1f;
            Vector2 push = side * sign + way * 0.35f;
            bool fling = L >= 3 && sw.Pass == 1;
            Hit h = new Hit(Blow.Slash, Cut(L))
            {
                Hurt = fling ? Hurt.Launch : Hurt.Spin,
                Push = fling ? 1.6f : 0.7f,
                Lift = fling ? 1.4f : -1f,
                Lethal = true,
                Power = 1f + 0.1f * L,
                Daze = fling ? 0.6f : 0f
            };
            HitResult r = by.HitOn(u, h, push, 0.55f);
            if (!r.Landed && !r.Killed)
            {
                return;
            }
            sw.Cut.Add(u);
            sw.Flung |= fling;
        }

        /// <summary>A beat after the stop: the cuts glow violet on everyone the pass struck.</summary>
        private static void Afterglow(TeamSync s, Sweep sw, int pass)
        {
            if (!s.Once("glow" + pass))
            {
                return;
            }
            SyncMove f = s.First ?? s.Lead;
            if (f == null)
            {
                return;
            }
            Hue h = f.Hue.As(Element.Shadow);
            bool any = false;
            for (int i = 0; i < sw.Cut.Count; i++)
            {
                Actor u = sw.Cut[i];
                if (u == null || !u.isAlive())
                {
                    continue;
                }
                Vector2 at = Choreo.BodyAt(u, 0.5f);
                if (!Kit.Visible(at))
                {
                    continue;
                }
                any = true;
                float px = Choreo.PxOf(u);
                float r = 5f * Choreo.KOf(u) * px;
                float a = Random.Range(20f, 70f) * Mathf.Deg2Rad;
                Vector2 d1 = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Vector2 d2 = new Vector2(-d1.x, d1.y);
                Kit.Line(at - d1, at + d1, px, 0.14f, 1, FxLayer.Top, 14, Color.white, Fx.WithA(h.Main, 0.8f), Fx.WithA(h.Rim, 0f));
                Kit.Line(at - d2, at + d2, px, 0.14f, 1, FxLayer.Top, 14, Color.white, Fx.WithA(h.Main, 0.8f), Fx.WithA(h.Rim, 0f));
                Victims.Flash(u, h.Main, 0.06f);
            }
            if (any)
            {
                Choreo.Sound("event:/SFX/HIT/HitSwordSword", f.A.current_tile, 0.1f);
            }
        }

        /// <summary>III: each lane is left a shadow-scarred furrow (the user's feet spared).</summary>
        private static void Scar(SyncMove m)
        {
            Ruin.Furrow(m.A, m.Mark, m.Mark2, 0.45f, Scale.Light, m.Pose.Ground);
        }
    }
}
