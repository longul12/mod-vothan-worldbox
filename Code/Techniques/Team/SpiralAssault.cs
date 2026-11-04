using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SPIRAL ASSAULT (team sync, Grandmaster). Three or four friends blur to stations evenly round a
    /// strong foe; a ring of light closes on the ground round it, braided in all their colours, and
    /// they start to run round it together, all the same way, at the same speed: a wheel of
    /// afterimages drawing a spiral in the dust. In rotation, each in its turn darts in off the
    /// wheel with its own school's blow (a boxer's hook, a kicker's roundhouse, a swordsman's cut, a
    /// ki palm...) and is back in its place before the next one comes in from the other side, the foe
    /// rocked from every side and never able to face them all. The spiral tightens with every
    /// lap; at its eye they all dash in at once: one shared flash, the foe thrown straight up out of
    /// the middle, and the ground scarred in a spiral round where it stood. Made for a foe stronger
    /// than any one of them (giants are only rocked by it, never thrown).
    /// I: one lap; II: two laps, a spiral scar; III: two quicker laps, deeper scar, harder blows.
    /// Damage (x each one's own damage): every dart I 0.75, II 0.9, III 1.05; the converging blow
    /// I 1.8, II 2.3, III 2.9 from each.
    /// </summary>
    internal sealed class SpiralAssaultTech : Technique
    {
        public override string Id => "spiral_assault";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Dash | Form.Flurry | Form.Spin | Form.Heavy | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 50f, 76f, 110f };
        public override float[] Cooldown => new[] { 20f, 21f, 22f };

        private static readonly float[] Shares = { 26f, 38f, 56f };
        private static readonly List<Actor> _who = new List<Actor>(4);
        private static readonly Vector2[] _st = new Vector2[4];
        private static readonly bool[] _taken = new bool[4];

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float DartMult(int level)
        {
            return level <= 1 ? 0.75f : level == 2 ? 0.9f : 1.05f;
        }

        private static float FinaleMult(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.3f : 2.9f;
        }

        private static int Laps(int level)
        {
            return level <= 1 ? 1 : 2;
        }

        private static float Seg(int level)
        {
            return level <= 1 ? 0.2f : level == 2 ? 0.18f : 0.16f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 4f, Total = 6f, Crowd = 0.12f, Alone = 14f, IntoGuard = 1f, Worth = 30f },
            new MeleeWant { Reach = 4.2f, Total = 9f, Crowd = 0.1f, Alone = 16f, IntoGuard = 1f, Worth = 36f },
            new MeleeWant { Reach = 4.4f, Total = 11f, Crowd = 0.08f, Alone = 18f, IntoGuard = 1f, Worth = 42f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c) || c.Allies.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            Plan p = Syncs.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            // a strong foe: one worth a whole wheel of them (a peer, a stronger one, a big creature)
            if (!c.Forced && c.Threat(v) < 0.45f && Tiers.Power(v) < 0.8f * Tiers.Power(c.Self) && Weights.Species(v) < Weight.Brute)
            {
                return null;
            }
            List<Actor> partners = Syncs.Pick(c, v, level, 3, Share(level), 8f, 7f);
            if (partners.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            _who.Clear();
            _who.Add(c.Self);
            _who.AddRange(partners);
            if (!Ring(_who, v, _st, out _, out _))
            {
                return null;
            }
            int n = _who.Count;
            Syncs.Allowed(c.Self, v, out float cm);
            float total = n * (Laps(level) * DartMult(level) + FinaleMult(level));
            p.Score = c.Value(v, c.Expected(v, total * 0.8f), cm) + Syncs.Bonus(c, v, partners);
            if (c.Forced)
            {
                p.Score = Mathf.Max(p.Score, 1f);
            }
            p.Others = new List<Actor>(partners);
            p.Data = c.Forced;
            return p;
        }

        /// <summary>
        /// The wheel round 'v': stations evenly round it (the leader's on its own side, each friend the
        /// free one nearest to it), turned in 15-degree steps until every station is safe ground and the
        /// running circle is clear (into 'at', member order). 'r0' the wheel's radius, 'r1' its eye's.
        /// </summary>
        private static bool Ring(List<Actor> who, Actor v, Vector2[] at, out float r0, out float r1)
        {
            return Ring(who, v, at, null, out r0, out r1);
        }

        private static bool Ring(List<Actor> who, Actor v, Vector2[] at, int[] slots, out float r0, out float r1)
        {
            int n = who.Count;
            float k = 0f;
            for (int i = 0; i < n; i++)
            {
                k += Choreo.KOf(who[i]);
            }
            k /= n;
            Vector2 vp = v.current_position;
            r0 = (2.3f + 0.5f * Syncs.SizeOf(v)) * k;
            r1 = Mathf.Max(1.05f * k + 0.3f * Syncs.SizeOf(v), r0 * 0.55f);
            float a0 = Syncs.Ang(who[0].current_position - vp);
            if (n > 4 || !Clear(vp, r0, r1))
            {
                return false;
            }
            bool[] taken = _taken;
            for (int turn = 0; turn < 12; turn++)
            {
                float t0 = a0 + (turn % 2 == 0 ? 1f : -1f) * ((turn + 1) / 2) * 15f;
                bool ok = true;
                for (int j = 0; j < taken.Length; j++)
                {
                    taken[j] = false;
                }
                taken[0] = true;
                if (slots != null)
                {
                    slots[0] = 0;
                }
                at[0] = Spot(vp, t0, r0);
                ok &= Combat.SafeSpot(at[0]);
                for (int i = 1; i < n && ok; i++)
                {
                    float ai = Syncs.Ang(who[i].current_position - vp);
                    int best = -1;
                    float bd = 999f;
                    for (int j = 1; j < n; j++)
                    {
                        float d = Mathf.Abs(Mathf.DeltaAngle(ai, t0 + j * 360f / n));
                        if (!taken[j] && d < bd)
                        {
                            bd = d;
                            best = j;
                        }
                    }
                    taken[best] = true;
                    if (slots != null)
                    {
                        slots[i] = best;
                    }
                    at[i] = Spot(vp, t0 + best * 360f / n, r0);
                    ok &= Combat.SafeSpot(at[i]);
                }
                if (ok)
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector2 Spot(Vector2 c, float deg, float r)
        {
            float a = deg * Mathf.Deg2Rad;
            return Combat.Clamp(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r);
        }

        /// <summary>The running ground: the wheel's circle and its eye's, passable all round.</summary>
        private static bool Clear(Vector2 c, float r0, float r1)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                if (!Combat.Passable(Spot(c, a, r0)) || i % 2 == 0 && !Combat.Passable(Spot(c, a, r1)))
                {
                    return false;
                }
            }
            return true;
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            bool forced = p.Data is bool b && b;
            if (v == null || p.Others == null || !Moves.CanPerform(a) || !Combat.Living(v))
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            List<Actor> partners = Syncs.Recheck(a, v, p.Others, 3, Share(level), forced, 10f);
            if (partners.Count < (forced ? 1 : 2))
            {
                return false;
            }
            List<Actor> who = new List<Actor>(partners.Count + 1) { a };
            who.AddRange(partners);
            Vector2[] at = new Vector2[who.Count];
            int[] slots = new int[who.Count];
            if (!Ring(who, v, at, slots, out float r0, out float r1))
            {
                Crew.Fail("spiral_assault", v);
                return false;
            }
            Vector2 vp = v.current_position;
            TeamSync s = new TeamSync(this, level, forced, v, vp, vp - a.current_position);
            int n = who.Count;
            Wheel w = new Wheel
            {
                N = n,
                R0 = r0,
                R1 = r1,
                Seg = Seg(level),
                K = n * Laps(level),
                Sign = Random.value < 0.5f ? -1f : 1f,
                C = vp
            };
            w.T = w.K * w.Seg;
            w.W = 2f * Mathf.PI / (n * w.Seg);
            s.Data = w;
            for (int i = 0; i < n; i++)
            {
                SyncMove m = s.Join(who[i]);
                m.Role = slots[i];   // its place round the wheel (the darts go round it in order)
                m.Mark = at[i];
                Script(m, w);
            }
            return s.Go(forced ? 1 : 2, Share(level), 8f);
        }

        // ------------------------------------------------------------ script --

        /// <summary>The wheel: how many run it, its radii, its rhythm (a segment per dart), which way it turns, its centre.</summary>
        private sealed class Wheel
        {
            public int N, K;
            public float R0, R1, Seg, T, W, Sign;
            public float T0;
            public bool Started;
            public Vector2 C;
        }

        private static void Script(SyncMove m, Wheel w)
        {
            TeamSync s = m.S;
            int L = s.Level;
            Vector2 station = m.Mark;
            m.Call();
            m.Dash(mm => station, 0.03f);
            m.OnlyIf(mm => Vector2.Distance(mm.Stance, station) > 0.3f);
            m.Meet("ring", 1.3f);
            m.Do(mm => Closed(s, w));
            // the wheel: one long beat on the sync's own clock (every runner in lockstep)
            float th0 = 0f, ghost = 0f, dust = 0f;
            int last = -1;
            Vector2 prev = Vector2.zero;
            Beat spin = new Beat
            {
                Dur = 0f,
                Tempo = Tempo.Free,
                MaxDur = w.T + 2f,
                To = Shape.Keep,
                Forms = Form.Dash | Form.Flurry | Form.Spin | Form.Team,
                Until = mm => w.Started && s.Clock - w.T0 >= w.T
            };
            spin.Enter = mm =>
            {
                if (s.Once("spin"))
                {
                    w.T0 = s.Clock;
                    w.Started = true;
                    w.C = s.VG;
                }
                Vector2 rel = mm.Stance - w.C;
                th0 = Mathf.Atan2(rel.y / Art.Ground, rel.x);
                prev = mm.Stance;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.1f);
            };
            spin.Every = (mm, k) =>
            {
                SyncMove me = (SyncMove)mm;
                float t = Mathf.Max(0f, s.Clock - w.T0);
                float u = Mathf.Clamp01(t / Mathf.Max(0.05f, w.T));
                if (s.VOk)
                {
                    w.C = Vector2.MoveTowards(w.C, s.VG, Clock.Dt * 2f);
                }
                float r = Mathf.Lerp(w.R0, w.R1, u);
                float th = th0 + w.Sign * w.W * t;
                int seg = Mathf.FloorToInt(t / w.Seg);
                float su = t / w.Seg - seg;
                // (the darts go round against the wheel's turn: each comes in from a new side)
                int turn = seg % w.N;
                bool striker = seg < w.K && (w.Sign > 0f ? turn : (w.N - turn) % w.N) == me.Role;
                float dart = striker ? Mathf.Pow(Mathf.Sin(Mathf.PI * su), 1.2f) : 0f;
                // (on the ground's ellipse a blow's length in or out is longer across than up and down)
                float ell = Mathf.Sqrt(Mathf.Cos(th) * Mathf.Cos(th) + Mathf.Sin(th) * Mathf.Sin(th) * Art.Ground * Art.Ground);
                float re = Mathf.Lerp(r, Mathf.Min(r, (me.Stand + 0.05f * me.Kk) / Mathf.Max(0.4f, ell)), dart);
                Vector2 pos = Combat.Clamp(w.C + new Vector2(Mathf.Cos(th), Mathf.Sin(th) * Art.Ground) * re);
                if (Combat.Passable(pos) || Choreo.Wading(pos))
                {
                    me.Stance = pos;
                    me.Pose.Ground = pos;
                }
                if ((w.C - me.Stance).sqrMagnitude > 1e-4f)
                {
                    me.Face(w.C - me.Stance);
                }
                // running round it, leaning in; darting in, the body thrown into the blow
                float bob = Mathf.Abs(Mathf.Sin(t * 26f));
                me.Pose.Rot = 9f + 16f * dart;
                me.Pose.SX = 1.05f + 0.14f * dart;
                me.Pose.SY = 0.96f - 0.1f * dart;
                me.Pose.Height = 0.04f * me.Kk * bob * (1f - dart);
                if (striker && seg != last && su >= 0.5f)
                {
                    last = seg;
                    Dart(me, seg / w.N);
                }
                if (!me.Vis)
                {
                    return;
                }
                float dt = Clock.Dt;
                ghost -= dt;
                if (ghost <= 0f)
                {
                    ghost = dart > 0.2f ? 0.02f : 0.04f;
                    Fx.Afterimage(me, 0.12f, Fx.WithA(me.Hue.Ghost, 0.75f), Fx.WithA(me.Hue.Edge, 0.4f), me.Hue.GhostEnd);
                }
                dust -= dt;
                if (dust <= 0f)
                {
                    dust = 0.07f;
                    ChoreoFx.HeelDust(me.Pose.Ground, prev - me.Pose.Ground, me.Px, me.A.current_tile, me.Kk * 0.8f);
                    if ((me.Pose.Ground - prev).sqrMagnitude > 0.0025f)
                    {
                        Kit.Line(prev, me.Pose.Ground, me.Px, 0.6f, 1, FxLayer.Ground, 2, Fx.WithA(me.Hue.Edge, 0.5f), Fx.WithA(me.Hue.Edge, 0.25f), Fx.WithA(me.Hue.Edge, 0f));
                    }
                    prev = me.Pose.Ground;
                }
            };
            m.Add(spin);
            // the eye: all of them in at once
            m.Meet("converge", 0.4f);
            m.Dash(ChoreoMove.ToTarget, 0.02f);
            m.Last.Tempo = Tempo.Free;
            m.OnlyIf(mm => s.VOk);
            Beat blow = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.07f,
                To = new Shape(20f, 1.24f, 0.86f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Forms = Form.Strike | Form.Heavy | Form.Team
            };
            blow.On(0.5f, mm => Finale((SyncMove)mm));
            m.Add(blow);
            m.PoseTo(0.2f, new Shape(12f, 1.1f, 0.93f, 0f, 0.1f), Ease.OutQuad, Tempo.Follow);
            m.Recover(0.3f);
        }

        /// <summary>The wheel closes: a ring of light on the ground round the foe, braided in every runner's colour.</summary>
        private static void Closed(TeamSync s, Wheel w)
        {
            if (!s.Once("ring"))
            {
                return;
            }
            SyncMove f = s.First;
            if (f == null)
            {
                return;
            }
            s.Ting("ring-ting");
            Vector2 c = s.VG;
            if (!s.VOk || !Kit.Visible(c))
            {
                return;
            }
            int n = 0;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove o = s.Cast[i];
                if (!o.In)
                {
                    continue;
                }
                Fx.Ring(c, f.Px, 0.4f + 0.05f * n, w.R0 / f.Px * 1.2f, w.R0 / f.Px * (0.95f - 0.05f * n), 2, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(o.Hue.Core, 0.9f), Fx.WithA(o.Hue.Edge, 0.6f), Fx.WithA(o.Hue.Edge, 0f));
                Crew.Link(o.BodyPos(), Choreo.BodyAt(s.V, 0.5f), o.Px, o.Hue.Edge, 0.14f);
                n++;
            }
        }

        /// <summary>A dart's blow: this runner's school blow, the foe rocked where it stands; the wheel holds with the impact.</summary>
        private static void Dart(SyncMove m, int k)
        {
            TeamSync s = m.S;
            if (!s.VOk)
            {
                return;
            }
            Blow b = m.BlowOfSchool(k);
            BlowShape sh = ChoreoMove.ShapeOf(b);
            float frac = Mathf.Clamp(sh.Contact, 0.35f, 0.75f);
            Vector2 contact = Choreo.BodyAt(s.V, frac);
            m.BlowFx(b, contact);
            HitResult r = m.HitOn(s.V, new Hit(b, DartMult(s.Level)) { Hurt = Hurt.Flinch, Push = 0.05f, Lethal = false, Power = 0.95f }, s.VG - m.Pose.Ground, frac);
            if (r.Landed)
            {
                s.Freeze = Mathf.Max(s.Freeze, m.HoldOf(sh.Weight * 0.9f));
            }
        }

        /// <summary>
        /// The converging blow: the first to land throws the foe straight up out of the eye (the others add
        /// their weight); the shared flash, and the ground scarred in a spiral round where it stood.
        /// </summary>
        private static void Finale(SyncMove m)
        {
            TeamSync s = m.S;
            int L = s.Level;
            if (!s.VOk)
            {
                return;
            }
            bool first = !s.Happened("eye");
            Blow b = m.BlowOfSchool(1);
            Vector2 contact = Choreo.BodyAt(s.V, 0.6f);
            m.BlowFx(b, contact);
            Hit h = new Hit(b, FinaleMult(L))
            {
                Hurt = first ? Hurt.Launch : Hurt.None,
                Push = 0.35f,
                Lift = 2.4f + 0.3f * L,
                Finisher = true,
                Lethal = true,
                Power = 1.6f + 0.1f * L,
                Daze = 1f
            };
            HitResult r = m.HitOn(s.V, h, s.VG - m.Pose.Ground, 0.6f);
            if (first && (r.Landed || r.Killed) && s.Once("eye"))
            {
                Eye(s, m, contact);
            }
        }

        private static void Eye(TeamSync s, SyncMove m, Vector2 at)
        {
            int L = s.Level;
            Wheel w = (Wheel)s.Data;
            Vector2 c = w.C;
            WorldTile tile = Combat.TileAt(c);
            s.Union("eye", at, 1.4f + 0.2f * L, true);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", tile, 0.1f);
            if (m.Vis)
            {
                Kit.Shockwave(c, m.Px, 4f, w.R0 / m.Px * 1.1f, 0.4f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
                Kit.DustRing(c, m.Px, tile, 16, -30f, 0.6f, m.Kk);
                Kit.DustRing(c, m.Px, tile, 12, 46f, 0.55f, m.Kk);
                Kit.Debris(c, m.Px, tile, 6 + 2 * L, m.Kk);
            }
            Ruin.Quake(m.A, c, s.Scale, 1.3f, m.Kk);
            if (L >= 2)
            {
                // the spiral scar: the wheel's path burnt into the ground, winding into the eye
                float a0 = Random.Range(0f, Mathf.PI * 2f);
                Vector2 p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0) * Art.Ground) * w.R0;
                int segs = L >= 3 ? 10 : 7;
                for (int j = 1; j <= segs; j++)
                {
                    float u = j / (float)segs;
                    float a = a0 + w.Sign * u * Mathf.PI * 2f * (L >= 3 ? 1.25f : 1f);
                    float r = Mathf.Lerp(w.R0, 0.35f, u);
                    Vector2 p1 = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r;
                    Ruin.Furrow(m.A, p0, p1, 0.35f + 0.05f * L, Scale.Light, m.Pose.Ground);
                    p0 = p1;
                }
            }
        }
    }
}
