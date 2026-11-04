using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PINCER BEAMS (team sync, Expert). Two friends (III: three) take stations around one foe, a
    /// right angle and more apart, each with a clear line to it; they sink into the same beam stance
    /// on the same frame and gather power together (Charging: a fast blow breaks either one, and a
    /// broken pincer is just one beam). A glint on both, and they fire at once: two beams in their
    /// own schools' colours race in from two sides and cross on the foe. Where they cross a knot of
    /// light flares (rings of both colours, sparks, the foe trapped and shaking in it); a heartbeat
    /// later the knot overloads: both beams snap off and it bursts, hurling the foe out of the
    /// crossing and knocking down whoever stands near. The beams run on past the foe and burn
    /// whatever else is in their lines (and clash with an enemy beam meeting one head on).
    /// I: thin beams, a small burst; II: wider, longer beams that trench the ground, an impact frame
    /// at the burst, a crater; III: a third beam when a friend is free (a star of light), the widest
    /// beams, the burst a crater that burns and a world-stopping flash.
    /// Damage (x each one's own damage): each beam on one caught all the way I 1.4, II 1.9, III 2.6;
    /// the knot's burst I 1.1, II 1.5, III 2.1 (x0.6 on those round it).
    /// </summary>
    internal sealed class PincerBeamsTech : Technique
    {
        public override string Id => "pincer_beams";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Beam | Form.Ki | Form.Charge | Form.Line | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 26f, 44f, 70f };
        public override float[] Cooldown => new[] { 15f, 16f, 17f };

        private static readonly float[] Shares = { 16f, 26f, 40f };
        private static readonly List<Actor> _who = new List<Actor>(4);
        private static readonly List<Vector2> _at = new List<Vector2>(4);
        private static readonly float[] Pair = { 115f, 100f, 130f, 145f, 90f };
        private static readonly float[] Star = { 120f, 110f, 130f, 100f };

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float BeamMult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.6f;
        }

        private static float KnotMult(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.5f : 2.1f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 0.72f : level == 2 ? 0.8f : 0.88f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            if (c.Allies.Count == 0)
            {
                return null;
            }
            int max = level >= 3 ? 2 : 1;
            float share = Share(level);
            Actor best = null;
            float bestScore = 0f;
            List<Actor> bestPartners = null;
            for (int i = -1; i < c.Enemies.Count && i < 4; i++)
            {
                Actor v = i < 0 ? c.Focus : c.Enemies[i];
                if (v == null || i >= 0 && v == c.Focus)
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                float d = c.Dist(v);
                if (d > (c.Forced ? 12f : 9f) || !Syncs.Worthy(c, v))
                {
                    continue;
                }
                List<Actor> partners = Syncs.Pick(c, v, level, max, share, 8f, 9f);
                partners.RemoveAll(u => !Combat.Allowed(u, v));   // (a beam only burns what its shooter may fight)
                if (partners.Count == 0)
                {
                    continue;
                }
                _who.Clear();
                _who.Add(c.Self);
                _who.AddRange(partners);
                if (!Stations(_who, v.current_position, _at))
                {
                    continue;
                }
                int n = 0;
                for (int j = 0; j < _at.Count; j++)
                {
                    if (_at[j].x > -9000f)
                    {
                        n++;
                    }
                }
                if (n < 2)
                {
                    continue;
                }
                float score = c.Value(v, c.Expected(v, (n * BeamMult(level) + KnotMult(level)) * 0.8f), cm);
                score += 6f * Mathf.Max(0, c.CountNear(v.current_position, 1.6f) - 1);
                score += Syncs.Bonus(c, v, partners);
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
                    bestPartners = new List<Actor>(partners);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                Others = bestPartners,
                Data = c.Forced
            };
        }

        /// <summary>
        /// The stations round 'vp' (into 'at', one per member; x -9999 where one has none): the leader
        /// keeps its ground if it is 3-6.5 tiles out with a clear line, else it takes its own side 4.2
        /// tiles out; a partner takes a point 90-145 degrees round from the leader (III: the three 120
        /// degrees apart), on its own side, on safe ground with a clear line to the foe and a clear run
        /// to it. False if the leader has none.
        /// </summary>
        private static bool Stations(List<Actor> who, Vector2 vp, List<Vector2> at)
        {
            at.Clear();
            Actor a = who[0];
            float k = Mathf.Lerp(1f, Choreo.KOf(a), 0.5f);
            float r = 4.2f * k;
            Vector2 la = a.current_position;
            float phi = Syncs.Ang(la - vp);
            float dl = Vector2.Distance(la, vp);
            if (dl >= 3f && dl <= 6.5f && Combat.ClearPath(la, vp, true) && Combat.SafeSpot(la))
            {
                at.Add(la);
            }
            else
            {
                Vector2 q = Combat.Clamp(vp + Crew.Dir(phi) * r);
                if (!Combat.SafeSpot(q) || !Combat.ClearPath(q, vp, true) || !Combat.ClearPath(la, q))
                {
                    return false;
                }
                at.Add(q);
            }
            int n = who.Count;
            for (int i = 1; i < n; i++)
            {
                Actor u = who[i];
                Vector2 up = u.current_position;
                float pu = Mathf.DeltaAngle(phi, Syncs.Ang(up - vp));
                float sign = pu >= 0f ? 1f : -1f;
                if (n >= 3)
                {
                    sign = i == 1 ? sign : -SignOf(at, vp, phi);
                }
                Vector2 found = new Vector2(-9999f, 0f);
                float[] tries = n >= 3 ? Star : Pair;
                for (int s = 0; s < 2 && found.x < -9000f; s++)
                {
                    float sg = s == 0 ? sign : -sign;
                    for (int t = 0; t < tries.Length; t++)
                    {
                        Vector2 q = Combat.Clamp(vp + Crew.Dir(phi + sg * tries[t]) * r);
                        if (Taken(at, q) || !Combat.SafeSpot(q) || !Combat.ClearPath(q, vp, true))
                        {
                            continue;
                        }
                        if (Vector2.Distance(up, q) > 0.4f && !Combat.ClearPath(up, q))
                        {
                            continue;
                        }
                        found = q;
                        break;
                    }
                }
                at.Add(found);
            }
            return true;
        }

        /// <summary>Which side of the leader's line the first partner's station took (+1 / -1).</summary>
        private static float SignOf(List<Vector2> at, Vector2 vp, float phi)
        {
            if (at.Count < 2 || at[1].x < -9000f)
            {
                return -1f;
            }
            return Mathf.DeltaAngle(phi, Syncs.Ang(at[1] - vp)) >= 0f ? 1f : -1f;
        }

        private static bool Taken(List<Vector2> at, Vector2 q)
        {
            for (int i = 0; i < at.Count; i++)
            {
                if (at[i].x > -9000f && (at[i] - q).sqrMagnitude < 2.25f)
                {
                    return true;
                }
            }
            return false;
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
            List<Actor> partners = Syncs.Recheck(a, v, p.Others, level >= 3 ? 2 : 1, Share(level), forced, 10f);
            partners.RemoveAll(u => !Combat.Allowed(u, v));
            if (partners.Count == 0)
            {
                return false;
            }
            List<Actor> who = new List<Actor>(partners.Count + 1) { a };
            who.AddRange(partners);
            List<Vector2> at = new List<Vector2>(who.Count);
            Vector2 vp = v.current_position;
            if (!Stations(who, vp, at))
            {
                Crew.Fail("pincer_beams", v);
                return false;
            }
            TeamSync s = new TeamSync(this, level, forced, v, vp, vp - a.current_position);
            int joined = 0;
            for (int i = 0; i < who.Count; i++)
            {
                if (i > 0 && at[i].x < -9000f)
                {
                    continue;   // no station for this friend: it stays out of it
                }
                SyncMove m = s.Join(who[i]);
                m.Mark = at[i];
                Script(m);
                joined++;
            }
            if (joined < 2)
            {
                Crew.Fail("pincer_beams", v);
                return false;
            }
            Knot knot = new Knot();
            s.Data = knot;
            s.OnTick = (ss, dt) => Tick(ss, knot, dt);
            return s.Go(1, Share(level), 6f);
        }

        // ------------------------------------------------------------- parts --

        private static void Script(SyncMove m)
        {
            TeamSync s = m.S;
            int L = s.Level;
            Vector2 station = m.Mark;
            m.Call();
            // a blur to its station (nothing if it is there)
            m.Dash(mm => station, 0.03f);
            m.OnlyIf(mm => Vector2.Distance(mm.Stance, station) > 0.35f);
            m.Meet("set", 1.1f);
            m.Do(mm => s.Ting("set"));
            // they gather together: the same stance, the same length (a fast blow breaks either)
            m.Charge(ChargeTime(L), ChargeStyle.Hip, L <= 1 ? 2.4f : L == 2 ? 2.9f : 3.4f, Element.School);
            m.Last.Tempo = Tempo.Free;
            m.Meet("fire", 0.4f);
            m.Do(mm =>
            {
                // the pincer locks: a thread from each shooter through the foe (a telegraph), once
                if (s.Once("lock") && s.VOk)
                {
                    Vector2 hit = Choreo.BodyAt(s.V, 0.5f);
                    for (int i = 0; i < s.Cast.Count; i++)
                    {
                        SyncMove o = s.Cast[i];
                        if (o.In && o.Vis)
                        {
                            Crew.Link(o.BodyPos(), hit, o.Px, o.Hue.Edge, 0.12f);
                        }
                    }
                }
            });
            float len = Vector2.Distance(station, s.Point) + 2.5f + 0.5f * L;
            m.Fire(new BeamSpec
            {
                Length = len / Mathf.Lerp(1f, m.Kk, 0.5f),
                Width = L <= 1 ? 4f : L == 2 ? 5f : 6.5f,
                Speed = 50f,
                Life = L <= 1 ? 0.6f : L == 2 ? 0.7f : 0.8f,
                Mult = BeamMult(L),
                Push = 0.25f,
                Trench = L <= 1 ? 0f : L == 2 ? 0.6f : 0.85f,
                Element = Element.School
            });
            m.Recover(0.3f);
        }

        /// <summary>The crossing of the beams: when it formed, where, and the flicker of its light.</summary>
        private sealed class Knot
        {
            public bool Crossed;
            public float T0, Fx;
            public Vector2 At;
            public Vector2 Bisector;
        }

        /// <summary>Once a frame: do the beams cross on the foe? Then the knot flares, and overloads.</summary>
        private static void Tick(TeamSync s, Knot k, float dt)
        {
            if (s.Happened("boom"))
            {
                return;
            }
            int firing = 0, reached = 0;
            Vector2 sum = Vector2.zero;
            Vector2 target = s.VOk ? Choreo.BodyAt(s.V, 0.5f) : k.At;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                SyncMove m = s.Cast[i];
                Beam b = m.Dropped ? null : m.Beam;
                if (b == null || !b.Firing || b.Done)
                {
                    continue;
                }
                firing++;
                sum += b.Dir;
                if (b.Len >= Vector2.Distance(b.From, target) - 0.4f)
                {
                    reached++;
                }
            }
            if (!k.Crossed)
            {
                if (reached < 2 || !s.VOk)
                {
                    return;
                }
                k.Crossed = true;
                k.T0 = s.Clock;
                k.At = target;
                k.Bisector = sum.sqrMagnitude > 1e-4f ? sum.normalized : s.Dir;
                s.Union("cross", target, 1.1f + 0.1f * s.Level, false);
                SyncMove f = s.First;
                if (f != null)
                {
                    Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", f.A.current_tile, 0.1f);
                }
            }
            if (s.VOk)
            {
                k.At = Choreo.BodyAt(s.V, 0.5f);
            }
            KnotFx(s, k, dt);
            float hold = s.Level <= 1 ? 0.3f : s.Level == 2 ? 0.36f : 0.42f;
            if (s.Clock - k.T0 >= hold || firing < 2)
            {
                Burst(s, k);
            }
        }

        /// <summary>The knot burning where the beams cross: a pulsing core, sparks of each shooter's colour, the foe shaking white.</summary>
        private static void KnotFx(TeamSync s, Knot k, float dt)
        {
            SyncMove f = s.First;
            if (f == null || !Kit.Visible(k.At))
            {
                return;
            }
            k.Fx -= dt;
            if (k.Fx > 0f)
            {
                return;
            }
            k.Fx = 0.05f;
            float px = f.Px;
            int r = Mathf.RoundToInt((3f + s.Level) * f.Kk * (0.85f + 0.3f * Random.value));
            Kit.Flash(k.At, px, r, 0.07f, Fx.WithA(Color.white, 0.9f));
            Color c = s.Braid(Random.Range(0, s.Cast.Count));
            Kit.Sparks(k.At, px, 3, 60f, 130f, c);
            Fx.Ring(k.At, px, 0.12f, 2f, (7f + 2f * s.Level) * f.Kk, 1, 1, FxLayer.Top, 5, 1f, 2.2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
            if (s.VOk)
            {
                Victims.Flash(s.V, c, 0.04f);
            }
        }

        /// <summary>
        /// The knot overloads: every beam snaps off, and it bursts: the foe hurled out of the crossing
        /// (along the line between the shooters' beams), everyone near knocked down, the ground blasted.
        /// </summary>
        private static void Burst(TeamSync s, Knot k)
        {
            if (!s.Once("boom"))
            {
                return;
            }
            SyncMove by = s.First;
            for (int i = 0; i < s.Cast.Count; i++)
            {
                Beam b = s.Cast[i].Dropped ? null : s.Cast[i].Beam;
                if (b != null && b.Firing)
                {
                    b.Collapse();
                }
            }
            if (by == null)
            {
                return;
            }
            int L = s.Level;
            Vector2 at = k.At;
            Vector2 g = s.VOk ? s.VG : at - new Vector2(0f, 0.5f * by.Kk);
            WorldTile tile = Combat.TileAt(g);
            s.Union("boom", at, 1.4f + 0.2f * L, true);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile, 0.1f);
            if (Kit.Visible(at))
            {
                Kit.Flash(at, by.Px, Mathf.RoundToInt((7f + 2f * L) * by.Kk), 0.12f, Color.white);
                Kit.Shockwave(g, by.Px, 4f, (1.6f + 0.3f * L) / by.Px, 0.4f, Fx.WithA(by.Hue.Edge, 0.9f), 2);
                Kit.DustRing(g, by.Px, tile, 12 + 2 * L, 44f, 0.55f, by.Kk);
                Kit.Debris(g, by.Px, tile, 6 + 2 * L, by.Kk);
                if (L >= 2)
                {
                    Kit.ImpactFrame(at, by.Px, (0.34f + 0.08f * L) * by.Kk, by.Hue.Pal, false, FxLayer.Behind);
                    Kit.Crater(g, by.Px * by.Kk, tile, Ruin.Force(by.Tier, s.Scale) >= 3, 6f, L <= 2, by.Hue.Edge);
                }
            }
            if (s.VOk)
            {
                Vector2 dir = k.Bisector;
                by.HitOn(s.V, new Hit(Blow.Palm, KnotMult(L)) { Hurt = Hurt.Launch, Push = 1.8f + 0.4f * L, Lift = 1.5f + 0.3f * L, Finisher = true, Lethal = true, Power = 1.5f + 0.15f * L, Daze = 0.8f }, dir, 0.5f);
            }
            by.Burst(g, 1.3f + 0.3f * L, KnotMult(L) * 0.6f, L >= 3 ? Hurt.Launch : Hurt.Knockdown, 1.1f + 0.2f * L, 1f, s.VOk ? s.V : null);
            if (L >= 2)
            {
                Ruin.Blast(by.A, g, 0.75f + 0.25f * L, s.Scale, by.Pose.Ground);
            }
            Ruin.Quake(by.A, g, s.Scale, 1.2f, by.Kk);
        }
    }
}
