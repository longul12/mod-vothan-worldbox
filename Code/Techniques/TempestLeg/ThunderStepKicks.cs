using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THUNDER STEP (Tempest Leg, Master). Sparks crackle at its heels, and it goes: zigzag steps too
    /// quick to see, each a jagged bolt of light on the ground where it passed, one off to the side and
    /// one in, and at every foe on its path a kick (a snap kick to the gut, a roundhouse, a side kick, a
    /// spinning back kick, in turn) that leaves it twitching with the shock. The last one gets the
    /// dropkick: a hop, both feet driven into it with the body flat in the air, a thunderclap, and the
    /// foe is sent flying (Launched) while it flips back onto its feet. Nothing can touch it while it
    /// steps; the kicks themselves can be answered. It skips whoever fell or fled on the way and saves
    /// the dropkick for whoever is left last. I: 3 foes; II: 4 and a thunderclap ring; III: 5, the
    /// dropkick on an impact frame knocking everyone round the last foe down. Damage (x the user's
    /// damage): each kick I 1.0, II 1.2, III 1.4; the dropkick I 1.8, II 2.3, III 3.0.
    /// </summary>
    internal sealed class ThunderStepKicksTech : Technique
    {
        public override string Id => "thunder_step_kicks";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Chain;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Dash | Form.Kick | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 24f, 40f, 64f };
        public override float[] Cooldown => new[] { 11f, 12f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static int Count(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 4 : 5;
        }

        public static float Kick(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.2f : 1.4f;
        }

        public static float Drop(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.3f : 3f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8f : 9f;
        }

        private static readonly Blow[] Kicks = { Blow.SnapKick, Blow.Roundhouse, Blow.SideKick, Blow.SpinKick };

        public override Plan Plan(Ctx c, int level)
        {
            float range = Range(level) * Choreo.KOf(c.Self);
            List<Actor> list = TempestHigh.Tmp;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(range, 10f) : range))
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Fit(c.Self, v, c.Forced) || !Combat.ClearPath(c.Pos, v.current_position))
                {
                    continue;
                }
                list.Add(v);
            }
            if (list.Count == 0 || list.Count < 2 && !c.Forced && !c.Linked)
            {
                return null;   // a chain needs links
            }
            TempestHigh.Path(c.Pos, list);
            // each step must be clear of walls from the last foe to the next
            Vector2 at = c.Pos;
            int n = 0;
            int max = Count(level);
            for (int i = 0; i < list.Count && n < max; i++)
            {
                if (!Combat.ClearPath(at, list[i].current_position))
                {
                    continue;
                }
                list[n++] = list[i];
                at = list[n - 1].current_position;
            }
            if (n < list.Count)
            {
                list.RemoveRange(n, list.Count - n);
            }
            if (n == 0 || n < 2 && !c.Forced && !c.Linked)
            {
                return null;
            }
            float score = 0f;
            bool worth = false;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, i == n - 1 ? Drop(level) : Kick(level)), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2f) * cm)
                {
                    worth = true;
                }
            }
            if (!worth && n < 3 && !c.Forced)
            {
                return null;   // not for two peasants
            }
            score += 8f * (n - 1);
            Vector2 dir = list[0].current_position - c.Pos;
            return new Plan
            {
                Target = list[0],
                Point = list[0].current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(1f, score) : score,
                Others = new List<Actor>(list),
                Data = c.Forced
            };
        }

        /// <summary>A foe it can kick: fair, standing on dry (or shallow) ground.</summary>
        private static bool Fit(Actor a, Actor v, bool forced)
        {
            return v != null && Choreo.Fair(a, v, out _, forced) && Combat.Grounded(v) && (!v.isInLiquid() || Choreo.Wading(v.current_position))
                && HundredFists.Hopping(v);
        }

        private sealed class State
        {
            public readonly List<Actor> T = new List<Actor>(5);
            public readonly List<long> Ids = new List<long>(5);
            public int I = -1;
            public bool DropNow;
            public float Zig = 1f;
            public Vector2 From;
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
            if (p.Others != null)
            {
                foreach (Actor v in p.Others)
                {
                    if (v != null)
                    {
                        st.T.Add(v);
                        st.Ids.Add(v.Sid());
                    }
                }
            }
            if (st.T.Count == 0 && p.Target != null)
            {
                st.T.Add(p.Target);
                st.Ids.Add(p.Target.Sid());
            }
            if (st.T.Count == 0)
            {
                return false;
            }
            Hue bolt = m.Hue.As(Element.Lightning);
            int n = st.T.Count;

            // the spark: a crackle at its heels, a crouch
            m.Crouch(0.09f, 0.8f)
                .Shows(Form.Dash | Form.Kick | Form.Fast)
                .OnEnter(mm =>
                {
                    Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.1f);
                    if (mm.Vis)
                    {
                        Kit.Sparks(mm.Pose.Ground + new Vector2(0f, mm.Px), mm.Px, 6, 40f, 90f, bolt.Edge);
                        Bolt(mm.Pose.Ground, mm.Pose.Ground + new Vector2(0f, mm.Hpx * mm.Px * 1.1f), mm.Px, bolt, 3, 2f, 0.1f, FxLayer.Over);
                    }
                });

            for (int i = 0; i < n - 1; i++)
            {
                int k = i;
                m.Do(mm => NextKick(mm, st));
                m.Add(Step(st, bolt, true));
                m.Add(Step(st, bolt, false));
                Blow b = Kicks[k % Kicks.Length];
                m.Strike(new Hit(b, Kick(level))
                {
                    Lethal = true,
                    Daze = 0.35f + 0.1f * level,
                    Power = 1.1f,
                    WindScale = 0.5f,
                    ActScale = 0.85f,
                    Push = b == Blow.SnapKick ? 0.5f : b == Blow.Roundhouse ? 0.8f : 1.2f,
                    OnLand = (mm, v, r) => Shock(mm, v, bolt, false)
                });
            }

            // the last: a dropkick
            Hit drop = new Hit(Blow.SideKick, Drop(level))
            {
                Hurt = Hurt.Launch,
                Push = level <= 1 ? 3f : level == 2 ? 4f : 5f,
                Lift = level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.8f,
                Finisher = true,
                Power = 1.35f,
                Daze = 0.8f
            };
            m.Do(mm => NextDrop(mm, st)).Label("drop");
            m.Add(Step(st, bolt, true));
            // the run-in stops short of it: the hop covers the rest
            Beat run = Step(st, bolt, false);
            run.Dest = mm => mm.V != null ? mm.StandSpot(mm.Stand + 1.1f * mm.Kk) : mm.Stance;
            m.Add(run);
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.15f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.V != null ? mm.StandSpot(mm.Stand * 0.9f) : mm.Stance,
                Apex = 0.5f,
                To = new Shape(-40f, 0.9f, 1.12f, 0.42f),
                Ease = Ease.OutQuad,
                Ghosts = 0.025f,
                NeedsVictim = true,
                Forms = Form.Kick | Form.Aerial | Form.Dash,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 8, 34f, 0.4f, mm.Kk);
                        Kit.Sparks(mm.Pose.Ground, mm.Px, 4, 40f, 80f, bolt.Edge);
                    }
                }
            });
            Beat dk = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(-82f, 1.24f, 0.86f, 0.36f, 0.22f),
                Ease = Ease.OutExpo,
                Forms = Form.Kick | Form.Aerial | Form.Straight | Form.Heavy,
                Tag = drop
            };
            dk.On(0.45f, mm => DropKick(mm, drop, bolt));
            m.Add(dk);
            // it springs back off the foe in a backflip and lands on its feet
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.26f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.7f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                Apex = 0.45f,
                Spin = -360f,
                To = new Shape(0f, 1f, 1f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.04f,
                Track = false
            });
            m.Land();
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The next foe still standing for a kick; the dropkick if it is the last; the end if none is left.</summary>
        private static void NextKick(ChoreoMove m, State st)
        {
            if (!Advance(m, st))
            {
                m.Lose();
                return;
            }
            if (!AnyAfter(m, st))
            {
                st.DropNow = true;
                m.Jump("drop");
            }
        }

        private static void NextDrop(ChoreoMove m, State st)
        {
            if (st.DropNow)
            {
                st.DropNow = false;
                if (m.VOk)
                {
                    return;
                }
            }
            if (!Advance(m, st))
            {
                m.Lose();
            }
        }

        private static bool Valid(ChoreoMove m, State st, int i)
        {
            Actor v = st.T[i];
            return v != null && v.Sid() == st.Ids[i] && Fit(m.A, v, m.Forced) && Vector2.Distance(m.Stance, v.current_position) <= (m.Forced ? 14f : 11f) * m.Kk
                && Combat.ClearPath(m.Stance, v.current_position);
        }

        private static bool Advance(ChoreoMove m, State st)
        {
            while (++st.I < st.T.Count)
            {
                if (Valid(m, st, st.I))
                {
                    m.SetVictim(st.T[st.I]);
                    st.From = m.Stance;
                    st.Zig = -st.Zig;
                    return true;
                }
            }
            return false;
        }

        private static bool AnyAfter(ChoreoMove m, State st)
        {
            for (int i = st.I + 1; i < st.T.Count; i++)
            {
                if (Valid(m, st, i))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One crackling step: the first goes off to the side of the line (the zig), the second into
        /// striking range (the zag). Nothing can touch it on the way; a jagged bolt marks the ground.
        /// </summary>
        private static Beat Step(State st, Hue bolt, bool zig)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.03f,
                PerTile = 0.022f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Ease = Ease.Linear,
                To = new Shape(16f, 1.18f, 0.88f, 0f),
                Intangible = true,
                Ghosts = 0.018f,
                Forms = Form.Dash | Form.Fast,
                Track = false,
                NeedsVictim = true
            };
            b.Dest = mm =>
            {
                if (mm.V == null)
                {
                    return mm.Stance;
                }
                Vector2 to = mm.StandSpot(mm.Stand);
                if (!zig)
                {
                    return to;
                }
                Vector2 d = to - mm.Stance;
                float len = d.magnitude;
                if (len < 1.2f * mm.Kk)
                {
                    return mm.Stance;   // already close: no room for a zig
                }
                Vector2 side = Choreo.Perp(d / len);
                Vector2 q = Combat.Clamp(Vector2.Lerp(mm.Stance, to, 0.5f) + side * st.Zig * Mathf.Min(1.3f, len * 0.35f) * mm.Kk);
                return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) && Combat.ClearPath(q, to) ? q : to;
            };
            Vector2 from = Vector2.zero;
            b.Enter = mm =>
            {
                from = mm.Stance;
                Vector2 to = b.Dest(mm);
                if ((to - mm.Stance).sqrMagnitude > 1e-4f)
                {
                    mm.Face(to - mm.Stance);
                }
                if ((to - from).sqrMagnitude < 0.04f)
                {
                    return;
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                if (mm.Vis)
                {
                    float px = mm.Px;
                    Bolt(from, to, px, bolt, 4 + mm.Level, 3f + mm.Level, 0.16f + 0.04f * mm.Level, FxLayer.Ground);
                    Bolt(from + new Vector2(0f, 4f * px), to + new Vector2(0f, 4f * px), px, bolt, 3, 2f, 0.08f, FxLayer.Top);
                    Kit.Sparks(from, px, 3, 40f, 90f, bolt.Edge);
                    ChoreoFx.HeelDust(from, from - to, px, mm.A.current_tile, mm.Kk);
                }
            };
            b.Exit = mm =>
            {
                if ((mm.V != null) && (mm.VG - mm.Stance).sqrMagnitude > 1e-4f)
                {
                    mm.Face(mm.VG - mm.Stance);
                }
                if (mm.Vis && (mm.Stance - from).sqrMagnitude > 0.04f)
                {
                    Kit.Sparks(mm.Pose.Ground + new Vector2(0f, mm.Px), mm.Px, 3, 30f, 70f, bolt.Edge);
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                }
            };
            return b;
        }

        /// <summary>The kick's shock: the foe twitches with sparks, lit in the lightning's colour.</summary>
        private static void Shock(ChoreoMove m, Actor v, Hue bolt, bool big)
        {
            if (v == null)
            {
                return;
            }
            if (Combat.Living(v))
            {
                Victims.Flash(v, Color.Lerp(bolt.Core, bolt.Edge, 0.4f), big ? 0.1f : 0.06f);
            }
            Vector2 at = Choreo.BodyAt(v, 0.55f);
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = m.Px;
            Kit.Sparks(at, px, big ? 8 : 4, 50f, 110f, bolt.Edge);
            int n = big ? 5 : 2;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Bolt(at, at + d * Random.Range(5f, big ? 14f : 8f) * px * m.Kk, px, bolt, 3, 1.5f, 0.08f, FxLayer.Top);
            }
        }

        /// <summary>Both feet into the last foe: a thunderclap; it flies.</summary>
        private static void DropKick(ChoreoMove m, Hit h, Hue bolt)
        {
            if (!m.VOk)
            {
                if (!m.Retarget())
                {
                    m.Lose();
                    return;
                }
            }
            Actor v = m.V;
            float reach = m.Stand + 0.9f * m.Kk + v.stats["size"] * 0.5f + 0.4f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                m.Lose();
                return;
            }
            if (!m.Touch(v))
            {
                return;
            }
            Vector2 at = m.ContactPoint(0.55f);
            WorldTile tile = v.current_tile;
            if (m.Vis)
            {
                float px = m.Px;
                Vector2 hip = m.BodyPos();
                ChoreoFx.Smear(hip + new Vector2(0f, 1.5f * px), at + new Vector2(0f, 1.5f * px), px, 0.1f, 2, 2, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 8);
                ChoreoFx.Smear(hip - new Vector2(0f, 1.5f * px), at - new Vector2(0f, 1.5f * px), px, 0.1f, 2, 2, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 8);
                Kit.Flash(at, px, Mathf.RoundToInt((4f + m.Level) * m.Kk), 0.08f, Fx.WithA(bolt.Core, 0.95f), m.TopLayer, 26);
                if (m.Level >= 2)
                {
                    Fx.Ring(at, px, 0.26f, 3f, (12f + 4f * m.Level) * m.Kk, 2, 1, FxLayer.Top, 7, 0.8f, 2.2f,
                        Fx.WithA(Color.white, 0.95f), Fx.WithA(bolt.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
                }
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.1f);
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.SideKick), at, m.Dir);
            Shock(m, v, bolt, true);
            if (m.Level >= 3)
            {
                m.AreaBlow(m.VG, 1.8f * Mathf.Lerp(1f, m.Kk, 0.5f), h.Mult * 0.45f, v, Hurt.Knockdown, 0.8f);
            }
        }

        /// <summary>A jagged bolt from a to b ('segs' kinks, 'jag' pixels off the line), on 'layer'.</summary>
        private static void Bolt(Vector2 a, Vector2 b, float px, Hue h, int segs, float jag, float life, FxLayer layer)
        {
            if (!Kit.Visible(a) && !Kit.Visible(b))
            {
                return;
            }
            Vector2 d = b - a;
            if (d.sqrMagnitude < px * px)
            {
                return;
            }
            Vector2 side = Choreo.Perp(d.normalized);
            Vector2 last = a;
            segs = Mathf.Clamp(segs, 1, 10);
            for (int i = 1; i <= segs; i++)
            {
                Vector2 p = i == segs ? b : Vector2.Lerp(a, b, i / (float)segs) + side * Random.Range(-jag, jag) * px;
                if (h.Level >= 2)
                {
                    // (a halo of electric blue round the white core from II)
                    Kit.Line(last, p, px, life * 1.2f, layer == FxLayer.Ground ? 4 : 3, layer, layer == FxLayer.Ground ? 3 : 13,
                        Fx.WithA(Hue.EdgeOf(Element.Lightning), 0.6f), Fx.WithA(h.Main, 0.35f), Fx.WithA(h.Edge, 0f));
                }
                Kit.Line(last, p, px, life, layer == FxLayer.Ground ? 2 : 1, layer, layer == FxLayer.Ground ? 4 : 14, Color.white, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Edge, 0f));
                last = p;
            }
        }
    }
}
