using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GALEWALKER (Tempest Leg, God; a casual answer to whatever flies). The God springs off the ground
    /// and walks up the air on steps of wind, a pale ring flaring under each foot, unhurried, to the
    /// nearest flyer, and kicks it out of the sky: a roundhouse, a gust bursting off the blow, and the
    /// flyer drops (a dragon is driven down to land, stunned, ploughing the ground where it touches
    /// down; a bird or a bat simply falls). Then it strolls on through the air to the next one, and
    /// the next, and walks back down when the sky is clear. A flick, not a fight: no hit-stop, no
    /// cinematic. A god's order with nothing flying about walks up over its target and drops a heel
    /// on it instead. I: 2 flyers; II: 3; III: 4. Damage (x the user's damage) on each: I 1.4, II 1.8,
    /// III 2.4; down for I 2 s, II 3 s, III 4 s.
    /// </summary>
    internal sealed class GalewalkerTech : Technique
    {
        public override string Id => "galewalker";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Air;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Aerial | Form.Hunt;
        public override Prey Prey => Prey.Flyer | Prey.Dragon;
        public override Scale Scale => Scale.Casual;
        public override float[] ManaCost => new[] { 30f, 40f, 50f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public static int Count(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        public static float Kick(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.4f;
        }

        public static float Stun(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 3f : 4f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 12f : 14f;
        }

        /// <summary>Up in the sky: flying, or a flyer or dragon well off the ground.</summary>
        public static bool Aloft(Actor v)
        {
            return v != null && (v.isFlying() || Preys.Is(v, Prey.Flyer | Prey.Dragon) && Combat.HeightOf(v) > 0.8f);
        }

        /// <summary>Is there ground to come back down to near the spot under it?</summary>
        private static bool Landable(Vector2 p)
        {
            return Combat.SafeSpot(p) || Choreo.Wading(p) || Combat.FindLanding(p, 3f, out _);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float range = Range(level) * Choreo.KOf(c.Self) * (c.Forced ? 1.4f : 1f);
            List<Actor> list = TempestHigh.Tmp;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Aloft(v) || !Choreo.Fair(c.Self, v, out _, c.Forced) || !Landable(v.current_position))
                {
                    continue;
                }
                if (Wingbreaker.Measure(v).Center > 14f)
                {
                    continue;   // out of reach even for it
                }
                list.Add(v);
            }
            if (list.Count == 0)
            {
                if (!c.Forced)
                {
                    return null;
                }
                // (an order with nothing in the sky: it walks up over its target and drops a heel on it)
                Actor f = c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (f == null || !Choreo.Fair(c.Self, f, out _, true))
                {
                    return null;
                }
                list.Add(f);
            }
            TempestHigh.Path(c.Pos, list);
            int n = Mathf.Min(list.Count, Count(level));
            if (n < list.Count)
            {
                list.RemoveRange(n, list.Count - n);
            }
            float score = 0f;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Kick(level)), cm) + 15f;
            }
            Vector2 d = list[0].current_position - c.Pos;
            return new Plan
            {
                Target = list[0],
                Point = list[0].current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(1f, score) : score,
                Others = new List<Actor>(list),
                Data = c.Forced
            };
        }

        private sealed class State
        {
            public readonly List<Actor> T = new List<Actor>(4);
            public readonly List<long> Ids = new List<long>(4);
            public int I = -1;
            /// <summary>The current one has had its kick (or is gone): the next step goes on to the next.</summary>
            public bool Kicked = true;
            public Vector2 Side = Vector2.left, Home, Land;
        }

        private const int Steps = 3;

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State { Home = a.current_position };
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
            Hue wind = m.Hue.As(Element.Wind);

            // a light spring off the ground
            m.Crouch(0.08f, 0.6f).Shows(Form.Aerial | Form.Kick | Form.Hunt);
            for (int i = 0; i < st.T.Count; i++)
            {
                m.Do(mm => Next(mm, st));
                for (int j = 0; j < Steps; j++)
                {
                    m.Add(Step(st, wind, j));
                }
                // the kick: a casual roundhouse in the air (a heel from above on one on the ground)
                // (the wind-up glides after a flyer that keeps flying: level with it, beside it)
                Beat cock = new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.08f,
                    To = new Shape(-14f, 0.92f, 1.08f, float.NaN),
                    Ease = Ease.OutQuad,
                    Route = Route.Line,
                    PathEase = Ease.OutQuad,
                    Chase = true,
                    Dest = mm => mm.VOk ? GoalGround(mm, st) : mm.Stance,
                    Forms = Form.Kick | Form.Aerial | Form.Hunt
                };
                cock.Every = (mm, kk) =>
                {
                    if (mm.VOk)
                    {
                        mm.Current.To = new Shape(-14f, 0.92f, 1.08f, GoalHeight(mm) / mm.Kk);
                    }
                };
                m.Add(cock);
                Beat act = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(22f, 1.2f, 0.88f, float.NaN), Ease = Ease.OutExpo, Forms = Form.Kick | Form.Aerial | Form.Hunt };
                act.Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Kit.Swish(mm, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(10f * mm.Kk), 5, 40), 0.42f, 190f, 370f, 3), 0.12f, new Vector2(1f * mm.Fwd, 1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 7);
                    }
                };
                act.On(0.55f, mm =>
                {
                    st.Kicked = true;
                    KickIt(mm, wind);
                });
                m.Add(act);
                m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.1f, To = new Shape(8f, 1.04f, 0.97f, float.NaN), Ease = Ease.OutQuad, Track = false });
            }
            // the sky is clear: it walks back down the air
            m.Do(mm => st.Land = LandSpot(mm, st)).Label("down");
            m.Add(Down(st, wind, 0)).OnlyIf(mm => mm.Pose.Height > 0.6f * mm.Kk);
            m.Add(Down(st, wind, 1)).OnlyIf(mm => mm.Pose.Height > 0.3f * mm.Kk);
            m.Drop(0.12f).OnlyIf(mm => mm.Pose.Height > 0.02f);
            m.Land();
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>The next flyer still up there, or down it walks (or ends, if it never left the ground).</summary>
        private static void Next(ChoreoMove m, State st)
        {
            if (!st.Kicked && m.VOk)
            {
                return;   // (still on its way to this one)
            }
            st.Kicked = false;
            while (++st.I < st.T.Count)
            {
                Actor v = st.T[st.I];
                if (v == null || v.Sid() != st.Ids[st.I] || !Choreo.Fair(m.A, v, out _, m.Forced) || !Aloft(v) && !m.Forced)
                {
                    continue;
                }
                if (Vector2.Distance(m.Stance, v.current_position) > Range(m.Level) * m.Kk + 4f)
                {
                    continue;
                }
                m.SetVictim(v);
                Vector2 s = m.Stance - v.current_position;
                st.Side = s.sqrMagnitude > 1e-4f ? s.normalized : -m.Dir;
                return;
            }
            if (m.Pose.Height > 0.3f * m.Kk)
            {
                m.Jump("down");
            }
            else
            {
                m.Lose();
            }
        }

        /// <summary>The height (world units) its feet aim for: its body level with the flyer's middle (above the head of one on the ground).</summary>
        private static float GoalHeight(ChoreoMove m)
        {
            Actor v = m.V;
            if (v == null || v.Sid() != m.VId)
            {
                return m.Pose.Height;
            }
            float body = m.BodyPos().y - (m.Pose.Ground.y + m.Pose.Height);
            if (!Aloft(v))
            {
                return Combat.HeightOf(v) + m.VHeight + 0.8f * m.Kk;
            }
            Wingbreaker.Box b = Wingbreaker.Measure(v);
            return Mathf.Max(0.5f * m.Kk, b.Center - body);
        }

        /// <summary>The ground point under where it kicks from: beside the flyer, on the side it came from.</summary>
        private static Vector2 GoalGround(ChoreoMove m, State st)
        {
            Actor v = m.V;
            if (v == null || v.Sid() != m.VId)
            {
                return m.Stance;
            }
            float half = Aloft(v) ? Wingbreaker.Measure(v).Half : 0.2f;
            return Combat.Clamp(v.current_position + st.Side * (half + 0.55f * m.Kk));
        }

        /// <summary>One step up the air: a hop onto a ring of wind, a third of the way there.</summary>
        private static Beat Step(State st, Hue wind, int j)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.12f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Apex = 0.14f,
                To = new Shape(-6f, 0.92f, 1.1f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.05f,
                Track = false,
                Forms = Form.Aerial | Form.Kick | Form.Hunt
            };
            b.Dest = mm => Vector2.Lerp(mm.Stance, GoalGround(mm, st), 1f / (Steps - j));
            b.Chase = j == Steps - 1;
            b.Enter = mm =>
            {
                if (!mm.VOk)
                {
                    // the one it was walking to is gone: on to the next (or back down)
                    st.Kicked = true;
                    Beat was = mm.Current;
                    Next(mm, st);
                    if (mm.Current != was || mm.Finished || !mm.VOk)
                    {
                        return;
                    }
                }
                float h = Mathf.Lerp(mm.Pose.Height, GoalHeight(mm), 1f / (Steps - j));
                mm.Current.To = new Shape(-6f, 0.92f, 1.1f, h / mm.Kk);
                if (mm.V != null)
                {
                    Vector2 d = mm.VG - mm.Stance;
                    if (d.sqrMagnitude > 1e-4f)
                    {
                        mm.Face(d);
                    }
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.06f);
                if (mm.Vis)
                {
                    Vector2 feet = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                    TempestHigh.WindRing(feet, mm.Px, mm.Kk, wind, 8f);
                    if (mm.Pose.Height < 0.1f)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 6, 30f, 0.4f, mm.Kk);
                    }
                }
            };
            return b;
        }

        /// <summary>One step back down the air towards solid ground.</summary>
        private static Beat Down(State st, Hue wind, int j)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.12f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Apex = 0.1f,
                To = new Shape(4f, 0.96f, 1.04f, 0f),
                Ease = Ease.InOutSine,
                Ghosts = 0.06f,
                Track = false,
                Forms = Form.Aerial
            };
            b.Dest = mm => Vector2.Lerp(mm.Stance, st.Land, 1f / (2 - j));
            b.Enter = mm =>
            {
                float h = Mathf.Lerp(mm.Pose.Height, 0.15f * mm.Kk, 1f / (2 - j));
                mm.Current.To = new Shape(4f, 0.96f, 1.04f, h / mm.Kk);
                Vector2 d = st.Land - mm.Stance;
                if (d.sqrMagnitude > 0.01f)
                {
                    mm.Face(d);
                }
                if (mm.Vis)
                {
                    TempestHigh.WindRing(mm.Pose.Ground + new Vector2(0f, mm.Pose.Height), mm.Px, mm.Kk, wind, 7f);
                }
            };
            return b;
        }

        /// <summary>Solid ground to come down on: under it, or the nearest land, or where it set out from.</summary>
        private static Vector2 LandSpot(ChoreoMove m, State st)
        {
            Vector2 p = m.Stance;
            if (Combat.SafeSpot(p) || Choreo.Wading(p))
            {
                return p;
            }
            return Combat.FindLanding(p, 4f, out Vector2 q) ? q : st.Home;
        }

        /// <summary>The kick: the flyer is knocked out of the sky (a dragon driven down to land, stunned).</summary>
        private static void KickIt(ChoreoMove m, Hue wind)
        {
            if (!m.VOk)
            {
                return;
            }
            Actor v = m.V;
            bool aloft = Aloft(v);
            Vector2 target;
            float half;
            if (aloft)
            {
                Wingbreaker.Box b = Wingbreaker.Measure(v);
                target = new Vector2(v.current_position.x + b.HeadX * 0.4f, v.current_position.y + b.Center);
                half = b.Half;
            }
            else
            {
                target = Choreo.BodyAt(v, 0.9f);
                half = 0.3f;
            }
            float px = m.Px;
            if (Vector2.Distance(m.BodyPos(), target) > 2.6f * m.Kk + half + 1f)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                return;   // it slipped away on the wind: the next step chases the next one
            }
            if (!m.Touch(v))
            {
                return;
            }
            Blow blow = aloft ? Blow.Roundhouse : Blow.AxeKick;
            Hit h = new Hit(blow, Kick(m.Level))
            {
                Lethal = true,
                Power = 1.25f,
                Hurt = aloft ? Hurt.Stagger : Hurt.DriveIn,
                Daze = 0.6f
            };
            Vector2 down = aloft ? (Vector2.down * 0.7f + m.Dir * 0.3f).normalized : Vector2.down;
            m.Deliver(v, h, ChoreoMove.ShapeOf(blow), target, down);
            if (aloft && Combat.Living(v))
            {
                // out of the sky: a dragon is made to land (ploughing the ground where it touches down), anything else falls
                WingbreakerGround.Down(v, Stun(m.Level), m.A, Ruin.Force(m.Tier, m.Scale), 1.2f);
            }
            if (m.Vis)
            {
                Particle ring = Fx.Ring(target, px, 0.24f, 3f, (12f + 2f * m.Level) * m.Kk, 2, 1, FxLayer.Top, 8, 2.2f, 2f,
                    Fx.WithA(Color.white, 0.9f), Fx.WithA(wind.Edge, 0.6f), Fx.WithA(wind.Edge, 0f));
                if (ring != null)
                {
                    ring.Rot = Mathf.Atan2(down.y, down.x) * Mathf.Rad2Deg;
                }
                Kit.SpeedLines(target, down, px, 4, 5f, 0.16f, Fx.WithA(wind.Edge, 0.85f));
                Kit.Line(target, new Vector2(target.x, Mathf.Max(v.current_position.y, target.y - 4f)), px, 0.12f, 1, FxLayer.Top, 10,
                    Fx.WithA(wind.Core, 0.8f), Fx.WithA(wind.Edge, 0f));
                Kit.Sparks(target, px, 4, 40f, 100f, wind.Edge);
            }
            Choreo.Sound("event:/SFX/HIT/HitGeneric", v.current_tile);
        }
    }
}
