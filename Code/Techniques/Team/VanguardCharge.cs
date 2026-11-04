using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// VANGUARD CHARGE (team sync, Grandmaster). The strongest of them takes the point and raises its
    /// fist; the others blur into a wedge behind it, a step back and a step out each, and the wedge
    /// lights up between them. All crouch on the same frame, and the wedge charges: a real run, heavy
    /// and fast, dust boiling off every heel, a bow wave of force in the point's colour shoving the
    /// air ahead of it. Every foe in the point's way is bowled aside, flung off to left and right;
    /// the friends running in its wake strike whoever it threw their way, each with its own school's
    /// blow. At the end of the run they skid to a stop together and the point stomps: a shockwave
    /// that knocks down everyone round it while the others punch the ground in the same instant.
    /// I: a wedge of three; II: four, the run kicks up afterimages, the stomp craters; III: five, a
    /// furrow ploughed down every lane, a bigger stomp.
    /// Damage (x each one's own damage): the point's shove I 1.3, II 1.6, III 2; a follower's blow
    /// I 0.9, II 1.1, III 1.35; the stomp I 0.7, II 0.9, III 1.1.
    /// </summary>
    internal sealed class VanguardChargeTech : Technique
    {
        public override string Id => "vanguard_charge";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Dash | Form.Body | Form.Line | Form.Heavy | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 48f, 72f, 104f };
        public override float[] Cooldown => new[] { 20f, 21f, 22f };

        private static readonly float[] Shares = { 24f, 36f, 52f };
        private static readonly List<Actor> _ray = new List<Actor>();
        private static readonly List<Actor> _near = new List<Actor>();
        private static readonly List<Actor> _who = new List<Actor>(5);
        private static readonly object ChargeTag = new object();

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static int MaxPartners(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        private static float PointMult(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.6f : 2f;
        }

        private static float WakeMult(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.1f : 1.35f;
        }

        private static float StompMult(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.9f : 1.1f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c) || c.Allies.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            float k = Mathf.Min(1.4f, Choreo.KOf(c.Self));
            float len = 10f * k;
            Vector2 dir = c.BestRay(len, 1f * k, out int count);
            if (c.ForceTarget != null && c.Dist(c.ForceTarget) > 0.5f)
            {
                dir = (c.ForceTarget.current_position - c.Pos).normalized;
            }
            _ray.Clear();
            count = c.OnRay(dir, len, 1f * k, _ray);
            int need = c.Forced ? 1 : c.Linked ? 2 : 3;
            if (count < need)
            {
                return null;
            }
            float far = 0f, nearest = 99f;
            Actor first = null;
            for (int i = 0; i < _ray.Count; i++)
            {
                float along = Vector2.Dot(_ray[i].current_position - c.Pos, dir);
                far = Mathf.Max(far, along);
                if (along < nearest)
                {
                    nearest = along;
                    first = _ray[i];
                }
            }
            if (first == null || nearest > 7f * k)
            {
                return null;
            }
            float L = Mathf.Clamp(far + 1.5f * k, 4f, 11f);
            List<Actor> partners = Syncs.Pick(c, first, level, MaxPartners(level), Share(level), 6f, 14f);
            if (partners.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            _who.Clear();
            _who.Add(c.Self);
            _who.AddRange(partners);
            Vector2 end = c.Pos + dir * L;
            if (Wedge(_who, end, null, null) < (c.Forced ? 2 : 3))
            {
                return null;
            }
            float score = 0f;
            for (int i = 0; i < _ray.Count; i++)
            {
                Actor v = _ray[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, PointMult(level) + 0.6f * WakeMult(level)), cm);
            }
            score += Syncs.Bonus(c, first, partners);
            return new Plan
            {
                Target = first,
                Point = end,
                Dir = dir,
                Count = count,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
                Others = new List<Actor>(partners),
                Data = c.Forced,
                AreaRadius = L * 0.5f
            };
        }

        /// <summary>
        /// The wedge: the strongest of 'who' at the point, running from where it stands to 'end'; the
        /// others a step back and a step out each, alternating sides (each lane clear and on safe ground,
        /// each friend able to reach its place; a friend with no lane stays out). Returns how many run;
        /// fills 'order' (the point first) and 'starts' when given.
        /// </summary>
        private static int Wedge(List<Actor> who, Vector2 end, List<Actor> order, List<Vector2> starts)
        {
            int point = 0;
            float best = -1f;
            for (int i = 0; i < who.Count; i++)
            {
                float pw = Tiers.Power(who[i]) + (i == 0 ? 0.01f : 0f);
                if (pw > best)
                {
                    best = pw;
                    point = i;
                }
            }
            Actor pa = who[point];
            Vector2 p0 = pa.current_position;
            Vector2 d = end - p0;
            float L = d.magnitude;
            if (L < 3f)
            {
                return 0;
            }
            Vector2 dir = d / L;
            L = Mathf.Min(L, 11f);
            if (!Lane(p0, dir, L))
            {
                return 0;
            }
            float k = Mathf.Lerp(1f, Choreo.KOf(pa), 0.5f);
            Vector2 side = Choreo.Perp(dir);
            order?.Add(pa);
            starts?.Add(p0);
            int n = 1;
            for (int i = 0; i < who.Count; i++)
            {
                if (i == point)
                {
                    continue;
                }
                int row = (n + 1) / 2;
                float sg = n % 2 == 1 ? 1f : -1f;
                Vector2 s = Combat.Clamp(p0 - dir * 0.95f * row * k + side * 0.85f * row * k * sg);
                Vector2 up = who[i].current_position;
                if (!Lane(s, dir, L) || Vector2.Distance(up, s) > 0.3f && !Combat.ClearPath(up, s))
                {
                    continue;
                }
                order?.Add(who[i]);
                starts?.Add(s);
                n++;
            }
            return n;
        }

        private static bool Lane(Vector2 s, Vector2 dir, float len)
        {
            Vector2 e = Combat.Clamp(s + dir * len);
            return Combat.SafeSpot(s) && Combat.SafeSpot(e) && Combat.ClearPath(s, e);
        }

        public override bool Begin(Actor a, Plan p)
        {
            bool forced = p.Data is bool b && b;
            if (p.Others == null || !Moves.CanPerform(a))
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            List<Actor> partners = Syncs.Recheck(a, p.Target, p.Others, MaxPartners(level), Share(level), forced, 8f);
            if (partners.Count < (forced ? 1 : 2))
            {
                return false;
            }
            List<Actor> who = new List<Actor>(partners.Count + 1) { a };
            who.AddRange(partners);
            List<Actor> order = new List<Actor>(who.Count);
            List<Vector2> starts = new List<Vector2>(who.Count);
            int n = Wedge(who, p.Point, order, starts);
            if (n < (forced ? 2 : 3) || !order.Contains(a))
            {
                if (p.Target != null)
                {
                    Crew.Fail("vanguard_charge", p.Target);
                }
                return false;
            }
            Vector2 dir = (p.Point - order[0].current_position).normalized;
            float L = Mathf.Min(11f, Vector2.Distance(p.Point, order[0].current_position));
            TeamSync s = new TeamSync(this, level, forced, p.Target, p.Point, dir);
            // the Brain's leader is the sync's leader (slot 0: it calls); the strongest runs at the point
            int pointSlot = -1;
            for (int i = 0; i < who.Count; i++)
            {
                int j = order.IndexOf(who[i]);
                if (j < 0)
                {
                    continue;
                }
                SyncMove m = s.Join(who[i]);
                m.Role = j;
                m.Mark = starts[j];
                m.Mark2 = Combat.Clamp(starts[j] + dir * L);
                if (j == 0)
                {
                    pointSlot = m.Slot;
                }
            }
            Run run = new Run { Point = pointSlot, Len = L };
            s.Data = run;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                Script(s.Cast[i], run);
            }
            s.OnTick = (ss, dt) => Tick(ss, run);
            return s.Go(forced ? 1 : 2, Share(level), 8f);
        }

        // ------------------------------------------------------------ script --

        /// <summary>The charge: who runs at the point, how far.</summary>
        private sealed class Run
        {
            public int Point;
            public float Len;
        }

        private static void Script(SyncMove m, Run run)
        {
            TeamSync s = m.S;
            int L = s.Level;
            bool point = m.Role == 0;
            Vector2 start = m.Mark, end = m.Mark2, dir = s.Dir;
            m.Call();
            if (point)
            {
                // the point raises its fist: the war cry
                Beat cry = new Beat { Tempo = Tempo.Wind, Dur = 0.16f, To = new Shape(-12f, 0.9f, 1.14f, 0f), Ease = Ease.OutBack, Forms = Form.Team };
                cry.Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/POWERS/Inspiration", mm.A.current_tile, 0.4f);
                    if (mm.Vis)
                    {
                        Kit.Flare(mm.Pose.Ground, mm.Px, mm.Hue.Edge, 22f * mm.Kk, 0.45f);
                        Kit.Flare(mm.Pose.Ground, mm.Px, mm.Hue.Rim, 14f * mm.Kk, 0.35f);
                        Fx.Glint(mm.HeadPos() + new Vector2(mm.Fwd * 2f * mm.Px, 2f * mm.Px), mm.Px, 0.24f, mm.Hue.Core, 1.2f);
                    }
                };
                m.Add(cry);
            }
            else
            {
                m.Dash(mm => start, 0.03f);
                m.OnlyIf(mm => Vector2.Distance(mm.Stance, start) > 0.3f);
            }
            m.Do(mm => mm.Face(dir));
            m.Meet("form", 1.3f);
            m.Do(mm => Formed(s, run));
            m.Crouch(0.14f, 1.2f);
            m.Last.Tempo = Tempo.Free;
            m.Meet("go", 0.3f);
            // the run: heavy, fast, in step
            Particle bow = null;
            float stride = 0f;
            Beat charge = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.12f + 0.06f * run.Len,
                Route = Route.Line,
                PathEase = Ease.InOutSine,
                Dest = mm => end,
                To = new Shape(18f, 1.06f, 0.95f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Dust = 0.05f,
                Ghosts = L >= 2 ? 0.04f : 0f,
                Forms = Form.Dash | Form.Body | Form.Heavy | Form.Line | Form.Team,
                Tag = ChargeTag
            };
            charge.Enter = mm =>
            {
                SyncMove me = (SyncMove)mm;
                me.SetVictim(null);   // (it strikes whatever the run meets: no one target to face)
                me.Face(dir);
                me.Mine.Clear();
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (me.Vis)
                {
                    Kit.DustRing(me.Stance, me.Px, me.A.current_tile, 8, 36f, 0.5f, me.Kk);
                }
                if (point && me.Vis)
                {
                    bow = me.Glue(Fx.Spawn(ChoreoArt.Crescent(Mathf.RoundToInt(me.Hpx * (0.9f + 0.1f * L)), L >= 2 ? 3 : 2), me.BodyPos(), me.Px, 1f, FxLayer.Over, 9));
                }
            };
            charge.Every = (mm, k) =>
            {
                SyncMove me = (SyncMove)mm;
                float ph = me.BeatT / 0.09f;
                float bob = Mathf.Abs(Mathf.Sin(Mathf.PI * ph));
                me.Pose.Height = 0.06f * me.Kk * bob;
                me.Pose.SX *= 1f + 0.05f * (1f - bob);
                me.Pose.SY *= 1f - 0.05f * (1f - bob);
                stride -= Clock.Dt;
                if (stride <= 0f && me.Vis)
                {
                    stride = 0.09f;
                    ChoreoFx.HeelDust(me.Pose.Ground, -dir, me.Px, me.A.current_tile, me.Kk);
                }
                if (bow != null && bow.Alive)
                {
                    bow.Pos = me.BodyPos() + dir * 0.55f * me.Kk;
                    bow.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    bow.Grad = null;
                    float pulse = 0.7f + 0.25f * Mathf.Sin(me.BeatT * 30f);
                    bow.R.color = Fx.WithA(Color.Lerp(me.Hue.Core, me.Hue.Edge, 0.6f), pulse * Mathf.Clamp01(k * 6f));
                }
            };
            charge.Exit = mm =>
            {
                if (bow != null)
                {
                    mm.Unglue(bow);
                    bow = null;
                }
            };
            m.Add(charge);
            // the stop: a skid, leaning back, all together
            m.PoseTo(0.1f, new Shape(-12f, 1.14f, 0.88f, 0f), Ease.OutExpo, Tempo.Free);
            m.Dusting(0.035f);
            m.Meet("stop", 0.3f);
            if (point)
            {
                // the stomp: a hop and a foot driven into the ground
                m.PoseTo(0.08f, new Shape(-6f, 0.9f, 1.14f, 0.22f), Ease.OutQuad, Tempo.Free);
                Beat stomp = new Beat { Tempo = Tempo.Free, Dur = 0.06f, To = new Shape(10f, 1.28f, 0.76f, 0f), Ease = Ease.InQuad, Track = false, Forms = Form.Ground | Form.Area | Form.Team };
                stomp.Exit = mm => Stomp((SyncMove)mm);
                m.Add(stomp);
                m.Hold(0.12f, false, Tempo.Impact);
            }
            else
            {
                // the others punch the ground in the same instant
                m.PoseTo(0.08f, new Shape(-8f, 0.94f, 1.08f, 0f), Ease.OutQuad, Tempo.Free);
                Beat punch = new Beat { Tempo = Tempo.Free, Dur = 0.06f, To = new Shape(34f, 1.2f, 0.8f, 0f, 0.08f), Ease = Ease.OutExpo, Track = false, Forms = Form.Ground | Form.Team };
                punch.Exit = mm =>
                {
                    if (mm.Vis)
                    {
                        Vector2 at = mm.Pose.Ground + mm.Dir * 0.3f * mm.Kk;
                        Kit.DustRing(at, mm.Px, mm.A.current_tile, 8, 34f, 0.45f, mm.Kk);
                        Kit.Debris(at, mm.Px, mm.A.current_tile, 4, mm.Kk);
                        Kit.Shockwave(at, mm.Px, 2f, 10f * mm.Kk, 0.28f, Fx.WithA(mm.Hue.Edge, 0.8f), 1);
                    }
                };
                m.Add(punch);
                m.Hold(0.12f, false, Tempo.Impact);
            }
            if (L >= 3)
            {
                m.Do(mm => Ruin.Furrow(mm.A, start, end, 0.45f, Scale.Light, mm.Pose.Ground));
            }
            m.Recover(0.3f);
        }

        /// <summary>The wedge forms: lines of light from the point to each of the others, a glint on each together.</summary>
        private static void Formed(TeamSync s, Run run)
        {
            if (!s.Once("formed"))
            {
                return;
            }
            SyncMove p = s.At(run.Point);
            s.Ting("formed-ting");
            if (p == null || !p.In)
            {
                return;
            }
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove m = s.Cast[i];
                if (m != p && m.In && (m.Vis || p.Vis))
                {
                    Kit.Line(p.BodyPos(), m.BodyPos(), p.Px, 0.26f, 1, FxLayer.Behind, 3, Fx.WithA(p.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
                }
            }
        }

        /// <summary>
        /// Once a frame while the wedge runs: the point bowls aside every foe in its way (flung off to the
        /// side it stood on), and each runner in its wake strikes the foes that come its way.
        /// </summary>
        private static void Tick(TeamSync s, Run run)
        {
            int L = s.Level;
            Vector2 dir = s.Dir;
            Vector2 side = Choreo.Perp(dir);
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove m = s.Cast[i];
                if (!m.In || m.Current == null || m.Current.Tag != ChargeTag)
                {
                    continue;
                }
                bool point = m.Role == 0;
                Vector2 g = m.Pose.Ground;
                float reach = (point ? 0.85f : 0.7f) * Mathf.Lerp(1f, m.Kk, 0.5f);
                Choreo.Foes(m.A, g + dir * 0.25f, reach + 0.6f, _near, false, true);
                for (int j = 0; j < _near.Count; j++)
                {
                    Actor u = _near[j];
                    long id = u.Sid();
                    if (m.Mine.Contains(id))
                    {
                        continue;
                    }
                    Vector2 rel = u.current_position - g;
                    float along = Vector2.Dot(rel, dir);
                    if (along < -0.35f || rel.magnitude > reach + u.stats["size"] * 0.5f)
                    {
                        continue;
                    }
                    m.Mine.Add(id);
                    float sg = Vector2.Dot(rel, side) >= 0f ? 1f : -1f;
                    if (Mathf.Abs(Vector2.Dot(rel, side)) < 0.05f)
                    {
                        sg = Random.value < 0.5f ? -1f : 1f;
                    }
                    Vector2 away = side * sg + dir * (point ? 0.45f : 0.25f);
                    if (point)
                    {
                        m.HitOn(u, new Hit(Blow.Palm, PointMult(L)) { Hurt = Hurt.Launch, Push = 1.6f + 0.3f * L, Lift = 0.8f + 0.1f * L, Lethal = true, Power = 1.3f, Daze = 0.5f }, away, 0.5f);
                    }
                    else
                    {
                        Blow b = m.BlowOfSchool(m.Mine.Count);
                        Vector2 contact = Choreo.BodyAt(u, 0.55f);
                        m.BlowFx(b, contact);
                        m.HitOn(u, new Hit(b, WakeMult(L)) { Hurt = Hurt.Knockdown, Push = 0.9f, Lethal = true, Power = 1.1f, Daze = 0.4f }, away, 0.55f);
                    }
                }
            }
        }

        /// <summary>The point's stomp: the shared flash at its feet, a shockwave knocking down everyone round it.</summary>
        private static void Stomp(SyncMove m)
        {
            TeamSync s = m.S;
            int L = s.Level;
            Vector2 at = m.Pose.Ground;
            WorldTile tile = m.A.current_tile;
            float r = 1.9f + 0.45f * L;
            s.Union("stomp", at + new Vector2(0f, 0.3f * m.Kk), 1.2f + 0.2f * L, true);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            if (m.Vis)
            {
                Kit.Shockwave(at, m.Px, 4f, r / m.Px, 0.42f, Fx.WithA(m.Hue.Edge, 0.95f), 2);
                Kit.Shockwave(at, m.Px, 3f, r / m.Px * 0.7f, 0.32f, Fx.WithA(m.Hue.Rim, 0.8f), 1);
                Kit.DustRing(at, m.Px, tile, 14, 48f, 0.6f, m.Kk);
                Kit.Debris(at, m.Px, tile, 8 + 2 * L, m.Kk);
                if (L >= 2)
                {
                    Kit.Crater(at, m.Px * m.Kk, tile, Ruin.Force(m.Tier, s.Scale) >= 3, 6f, L <= 2, m.Hue.Edge);
                }
            }
            m.Burst(at, r, StompMult(L), Hurt.Knockdown, 0.9f + 0.2f * L, 1.1f);
            if (L >= 2)
            {
                Ruin.Blast(m.A, at + m.Dir * 0.6f * m.Kk, 0.7f + 0.25f * L, s.Scale, at);
            }
            Ruin.Quake(m.A, at, s.Scale, 1.2f + 0.1f * L, m.Kk);
        }
    }
}
