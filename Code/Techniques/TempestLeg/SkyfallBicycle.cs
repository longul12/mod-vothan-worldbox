using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SKYFALL BICYCLE (Tempest Leg, Grandmaster). An air follow-up: a foe knocked into the air hangs
    /// there a heartbeat (anime hang time) while it crouches and leaps up beside it (II and III with a
    /// flip on the way up), lands both feet on it, and rides it down to the earth pedalling a barrage
    /// of bicycle kicks into it all the way, the legs a wheel of smears, rings of wind peeling off the
    /// falling pair; at the bottom it stamps the body into the ground: a crater, a shockwave knocking
    /// everyone near flat, and it springs off. A god's order on a foe standing on the ground opens with
    /// a rising kick that pops it up first. It needs a body it can hold (no flyers, no giants). I: 10
    /// kicks, a 1.2-tile crater; II: 14 kicks, 1.5; III: 20 white-hot kicks, a 1.9-tile crater, an impact
    /// frame and a ring racing out from it. Damage (x the user's damage): the kicks I 2.0, II 2.6,
    /// III 3.4 together (none lethal), the crash I 1.6, II 2.0, III 2.8 (x0.5 on those near it).
    /// </summary>
    internal sealed class SkyfallBicycleTech : Technique
    {
        public override string Id => "skyfall_bicycle";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Air;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Aerial | Form.Flurry;
        /// <summary>(the crash breaks the ground like a heavy blow: it falls from the sky)</summary>
        public override Scale Scale => Scale.Heavy;
        public override Scale ScaleAt(int level) => level >= 3 ? Scale.Heavy : Scale.Light;
        public override float[] ManaCost => new[] { 34f, 55f, 85f };
        public override float[] Cooldown => new[] { 12f, 13f, 15f };

        public override bool Accepts(Link link)
        {
            return link == Link.Launched;
        }

        public static int Kicks(int level)
        {
            return level <= 1 ? 10 : level == 2 ? 14 : 20;
        }

        public static float Pedal(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.6f : 3.4f;
        }

        public static float Crash(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2f : 2.8f;
        }

        private static float Reach(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 4.5f : 5f;
        }

        /// <summary>
        /// A body in the air it can take hold of: one of the engine's own (a launched or juggled body,
        /// in nobody's hands), not flying, not too heavy or big. (Choreo.Holdable says no to any body
        /// above a hop's height, so the air catch is checked here.)
        /// </summary>
        public static bool AirCatchable(Actor v, Actor by)
        {
            if (v == null || by == null || v.isFlying() || Weights.Heavy(v) || v.is_immovable || v.isInMagnet() || v.isInsideSomething()
                || ControllableUnit.isControllingUnit(v) || v.asset == null || v.asset.is_boat)
            {
                return false;
            }
            Victim b = Victims.Get(v);
            return b != null && b.Holder == null && v.actor_scale <= Mathf.Max(0.3f, by.actor_scale * 2.4f);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, k, ref best, ref bestScore);
            for (int i = 0; i < c.Enemies.Count && i < 6; i++)
            {
                if (c.EnemyDist[i] > Reach(level) * k + 2f)
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], level, k, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 d = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(1f, bestScore) : bestScore,
                Data = c.Forced
            };
        }

        private void Consider(Ctx c, Actor v, int level, float k, ref Actor best, ref float bestScore)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
            {
                return;
            }
            bool air = !Combat.Grounded(v);
            if (air ? !AirCatchable(v, c.Self) : !c.Forced || !Choreo.Holdable(v, c.Self))
            {
                return;   // (a body on the ground only on an order: it is popped up first)
            }
            float reach = Reach(level) * k + v.stats["size"] * 0.5f + (c.Linked && v == c.LinkTarget ? 0.8f : 0f);
            if (c.Dist(v) > (c.Forced ? Mathf.Max(reach, 6f) : reach))
            {
                return;
            }
            // it comes down where the body is: solid ground under it
            Vector2 at = v.current_position;
            if (!c.Forced && !Combat.SafeSpot(at) && !Choreo.Wading(at))
            {
                return;
            }
            float score = c.Value(v, c.Expected(v, Pedal(level) + Crash(level)), cm) + 12f;
            if (c.Linked && v == c.LinkTarget)
            {
                score += 14f;   // the chain it is made for: straight off a launch
            }
            if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Fiend)
            {
                score += (16f + 4f * level) * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        private sealed class State
        {
            public int N, I;
            public float Per, JerkT, Rot, RingT;
            public bool Caught;
            public Vector2 Side;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State { N = Kicks(level), Per = Pedal(level) / Kicks(level) };
            bool air = p.Target != null && !Combat.Grounded(p.Target);
            if (!air)
            {
                // (an order on a foe on the ground: a rising kick pops it up into the air first)
                m.StepIn()
                    .Strike(new Hit(Blow.SnapKick, 0.4f)
                    {
                        Hurt = Hurt.Juggle,
                        Lift = 2.2f,
                        Push = 0.3f,
                        Lethal = false,
                        Shows = Form.Kick | Form.Rising,
                        ActScale = 0.9f
                    });
            }
            else
            {
                // the body stops where it is in its flight: hang time
                m.Do(mm => Hang(mm, 0.6f));
            }

            // the crouch and the leap up beside it
            m.Crouch(0.08f, 0.8f)
                .OnlyIf(mm => mm.VOk);
            Beat leap = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.2f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm =>
                {
                    // (asked before the beat's Enter: the side it comes from is settled here)
                    Vector2 d = mm.VG - mm.Stance;
                    st.Side = d.sqrMagnitude > 1e-4f ? -d.normalized : -mm.Dir;
                    return Beside(mm, st);
                },
                Apex = 0.3f,
                Spin = level >= 2 ? -360f : 0f,
                To = new Shape(-10f, 0.9f, 1.12f, 1.5f),
                Ease = Ease.OutCubic,
                Ghosts = 0.03f,
                NeedsVictim = true,
                Track = false,
                Forms = Form.Kick | Form.Aerial
            };
            leap.Enter = mm =>
            {
                mm.Current.To = new Shape(-10f, 0.9f, 1.12f, RideHeight(mm) / mm.Kk);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 10, 40f, 0.45f, mm.Kk);
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.75f));
                }
            };
            leap.Every = (mm, kk) =>
            {
                // the body may drift: the top of the leap follows it
                mm.Current.To = new Shape(-10f, 0.9f, 1.12f, RideHeight(mm) / mm.Kk);
            };
            m.Add(leap);

            // the catch: both feet onto it
            Beat land = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(-18f, 1.12f, 0.9f, float.NaN),
                Ease = Ease.OutExpo,
                // (the grip is already under its feet when the catch lands half way through)
                Grip = new Grip(2.5f, -8f, -90f),
                Track = true,
                Forms = Form.Kick | Form.Aerial
            };
            land.On(0.5f, mm => Catch(mm, st));
            m.Add(land);

            // the ride down: bicycle kicks all the way
            Beat ride = new Beat
            {
                Tempo = Tempo.Act,
                Dur = level <= 1 ? 0.55f : level == 2 ? 0.65f : 0.8f,
                To = new Shape(12f, 1f, 1.02f, 0.35f),
                Ease = Ease.InQuad,
                Grip = new Grip(2.5f, -8f, -90f),
                Ghosts = 0.03f,
                Track = false,
                Forms = Form.Kick | Form.Aerial | Form.Flurry,
                If = mm => mm.Held != null
            };
            ride.Enter = mm =>
            {
                st.I = 0;
                st.RingT = 0f;
            };
            ride.Every = (mm, kk) => Ride(mm, st, kk);
            m.Add(ride);

            // the crash
            m.Slam(Crash(level), level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.9f, level <= 1 ? 1.6f : level == 2 ? 2f : 2.6f, false, 0.08f, 0.5f);
            if (level >= 3)
            {
                m.Do(mm =>
                {
                    if (mm.LastHit.Landed || mm.V != null)
                    {
                        Areas.Add(new TempestWave(mm, mm.Pose.Ground, 3.4f * Mathf.Lerp(1f, mm.Kk, 0.5f), 0.6f, 0.9f, mm.V, mm.Hue.As(Element.Wind), false));
                    }
                }).OnlyIf(mm => st.Caught);
            }
            // it springs off the body and lands beside the crater
            m.BackHop(0.9f, 0.22f).OnlyIf(mm => st.Caught);
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>Holds the airborne body where it is for 'hold' seconds (a juggle from its height).</summary>
        private static void Hang(ChoreoMove m, float hold)
        {
            if (!m.VOk || m.Held != null)
            {
                return;
            }
            Actor v = m.V;
            float h = Combat.HeightOf(v);
            float k = Choreo.KOf(v);
            Impact im = Impact.Of(Hurt.Juggle, m.Dir, 0.1f, 0.8f);
            im.Lift = Mathf.Clamp(h / Mathf.Max(0.5f, k) + 0.2f, 0.9f, 3.6f);
            im.Hold = hold;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            Victim b = Victims.Get(v);
            if (b != null && b.Holder == null)
            {
                // (the engine's own body in the air: its reaction is taken over where it is)
                b.Start(im, m.A, m);
            }
            else
            {
                Victims.Hurt(v, m.A, m, im);
            }
        }

        /// <summary>How high it rides: its feet on the body's middle.</summary>
        private static float RideHeight(ChoreoMove m)
        {
            if (m.V == null || m.V.Sid() != m.VId)
            {
                return m.Pose.Height;
            }
            return Mathf.Max(0.4f * m.Kk, Combat.HeightOf(m.V) + m.VHeight * 0.55f);
        }

        /// <summary>Where it leaps to: just beside the body's ground point, on the side it came from.</summary>
        private static Vector2 Beside(ChoreoMove m, State st)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 s = Combat.Clamp(m.VG + st.Side * m.Stand * 0.45f);
            return Combat.SafeSpot(s) || Choreo.Wading(s) ? s : m.VG;
        }

        /// <summary>Both feet land on the body: it is held under them from now on (or, unholdable, kicked down).</summary>
        private static void Catch(ChoreoMove m, State st)
        {
            if (!m.VOk)
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            Vector2 body = Choreo.BodyAt(v, 0.5f);
            if (Vector2.Distance(body, m.BodyPos()) > 1.6f * m.Kk + m.VHeight)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                m.Lose();   // it got away
                return;
            }
            if (!m.Touch(v))
            {
                return;   // answered in the air: the engine plays out the whiff
            }
            Victim b = Victims.Grab(v, m);
            if (b == null && AirCatchable(v, m.A))
            {
                // (Victims.Grab refuses a body above a hop's height: the engine's own airborne body is taken directly)
                b = Victims.Get(v);
                b?.HoldBy(m);
            }
            if (b == null)
            {
                // it can't be held after all: one stamp sends it down instead
                m.Deliver(v, new Hit(Blow.HeelDrop, Pedal(m.Level) * 0.6f) { Hurt = Hurt.Knockdown, Power = 1.2f }, ChoreoMove.ShapeOf(Blow.HeelDrop), body, Vector2.down);
                m.Lose();
                return;
            }
            m.Held = b;
            st.Caught = true;
            Combat.Pin(v, 3f);
            b.P.Flash(0.06f);
            Choreo.Sound("event:/SFX/OTHER/Punch", m.A.current_tile);
            if (m.Vis)
            {
                Fx.HitSpark(body, m.Px, 1, Vector2.down, 4);
                Fx.FocusLines(body, m.Px, Fx.Count(6), 9f * m.Kk, 16f * m.Kk, 0.1f);
                TempestHigh.WindRing(m.Pose.Ground + new Vector2(0f, m.Pose.Height), m.Px, m.Kk, m.Hue, 10f);
            }
            m.HoldFor(0.05f * m.ImpactK);
            m.SetLink(Link.Launched, v);
        }

        /// <summary>The ride: a kick every few hundredths, alternating feet, the legs a wheel; rings of wind peel off the fall.</summary>
        private static void Ride(ChoreoMove m, State st, float k)
        {
            float dt = Clock.Dt;
            st.JerkT -= dt;
            if (st.JerkT > 0f)
            {
                m.Pose.Rot = st.Rot;
            }
            st.RingT -= dt;
            if (st.RingT <= 0f && m.Vis)
            {
                st.RingT = 0.07f;
                TempestHigh.WindRing(m.Pose.Ground + new Vector2(0f, m.Pose.Height + 2f * m.Px), m.Px, m.Kk, m.Hue, 9f, 0.2f);
                Kit.SpeedLines(m.BodyPos() + new Vector2(0f, 4f * m.Px), Vector2.up, m.Px, 2, 5f, 0.12f, Fx.WithA(m.Hue.Edge, 0.7f));
            }
            while (st.I < st.N && k >= (st.I + 0.5f) / st.N)
            {
                int i = st.I++;
                if (m.Held == null)
                {
                    return;
                }
                Actor v = m.Held.A;
                float px = m.Px;
                Vector2 at = Choreo.BodyAt(v, Random.Range(0.3f, 0.7f)) + new Vector2(Random.Range(-1.5f, 1.5f) * m.VPx, 0f);
                st.Rot = (i & 1) == 0 ? 20f : 4f;
                st.JerkT = 0.03f;
                m.Pose.Rot = st.Rot;
                if (m.Vis)
                {
                    float sx = (i & 1) == 0 ? 1.5f : -1.5f;
                    Vector2 hip = m.BodyPos() + new Vector2(sx * px, -1f * px);
                    ChoreoFx.Smear(hip, at, px, 0.07f, m.Level >= 3 ? 2 : 1, 2, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 9);
                    if ((i & 1) == 0)
                    {
                        // the wheel of the pedalling legs
                        Kit.Swish(m, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(6f * m.Kk), 4, 30), 1f, 120f, -150f, 2), 0.1f, new Vector2(1.5f * m.Fwd, -3f), m.Fwd,
                            Fx.WithA(m.Hue.Core, 0.85f), Fx.WithA(m.Hue.Edge, 0.6f), 6);
                    }
                    Particle star = Fx.Spawn(Art.HitStars[i % 3 == 0 ? 1 : 0], at, px, 0.05f, m.TopLayer, 20);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 90f);
                        star.Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    }
                    if (i % 3 == 0)
                    {
                        Kit.Sparks(at, px, 3, 40f, 90f, m.Hue.Edge);
                    }
                }
                HitResult r = Combat.Hit(m.A, v, st.Per * m.Cm, false, false, 0f, m);
                m.LastHit = r;
                m.Held.P.Flash(0.04f);
                if ((i & 1) == 0)
                {
                    ChoreoFx.BlowSound(Limb.Foot, 0.8f, false, v.current_tile);
                }
            }
        }
    }
}
