using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HUNDRED LEG STORM (Tempest Leg, Grandmaster). It steps in, draws one knee up, and the leg becomes
    /// a blur: dozens of kicks in a second, high, middle and low in turn, a fan of motion-blurred legs
    /// fanning out from its hip, sparks and stars all over the foe, whose head snaps and body folds
    /// under every one while it is driven back step by step, three or four tiles, the kicker gliding
    /// after it on one leg. The last kick is a spinning back kick that launches it (Launched: an air
    /// follow-up such as Skyfall Bicycle can take it from there). I: 26 kicks; II: 34; III: 46 white-hot
    /// kicks, and the last one blasts a wind ring off the foe, an impact frame, and tears a furrow
    /// along its flight. Damage (x the user's damage): the storm I 2.6, II 3.2, III 4.2 together (none
    /// of them lethal), the last kick I 1.4, II 1.8, III 2.4.
    /// </summary>
    internal sealed class HundredLegStormTech : Technique
    {
        public override string Id => "hundred_leg_storm";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 38f, 60f, 90f };
        public override float[] Cooldown => new[] { 14f, 15f, 16f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static int Kicks(int level)
        {
            return level <= 1 ? 26 : level == 2 ? 34 : 46;
        }

        public static float Storm(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.2f : 4.2f;
        }

        public static float Finish(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.4f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Storm(1) + Finish(1), Crowd = 0.06f, Alone = 10f, IntoGuard = 1.2f, Worth = 18f },
            new MeleeWant { Reach = 2.6f, Total = Storm(2) + Finish(2), Crowd = 0.05f, Alone = 10f, IntoGuard = 1.2f, Worth = 22f },
            new MeleeWant { Reach = 3f, Total = Storm(3) + Finish(3), Crowd = 0.04f, Alone = 12f, IntoGuard = 1.2f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // a foe on the ground getting up is kicked as it rises: fine; one already reeling away out of reach isn't
            if (c.State(p.Target).Airborne)
            {
                return null;
            }
            return p;
        }

        private sealed class State
        {
            public int N, I;
            public float Per, JerkT, Rot, SX = 1f, SY = 1f, FanT;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State { N = Kicks(level), Per = Storm(level) / Kicks(level) };

            m.StepIn();
            // the knee comes up: the storm gathers in the leg
            Beat chamber = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.11f,
                To = new Shape(-12f, 0.9f, 1.1f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                NeedsVictim = true,
                Forms = Form.Kick | Form.Flurry
            };
            chamber.On(0.7f, mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(mm.Hand(Limb.Foot) + new Vector2(0f, 2f * mm.Px), mm.Px, 0.14f, mm.Hue.Core, 0.8f + 0.1f * mm.Level);
                    ChoreoFx.Motes(mm.Hand(Limb.Foot), mm.Px, 4, 10f * mm.Kk, 0.1f, mm.Hue.Core, mm.Hue.Edge);
                }
            });
            m.Add(chamber);

            // the storm
            Beat storm = new Beat
            {
                Tempo = Tempo.Act,
                Dur = level <= 1 ? 0.85f : level == 2 ? 0.95f : 1.1f,
                To = new Shape(-8f, 1.04f, 0.98f, 0.03f, 0.06f),
                Ease = Ease.OutQuad,
                NeedsVictim = true,
                Track = true,
                Forms = Form.Kick | Form.Flurry
            };
            storm.Enter = mm =>
            {
                st.I = 0;
                st.JerkT = 0f;
                if (mm.V != null)
                {
                    Combat.Pin(mm.V, mm.BeatDur + 0.4f);
                }
            };
            storm.Every = (mm, kk) => Tick(mm, st, kk);
            m.Add(storm);

            // the last one: a spinning back kick that launches it
            m.Strike(new Hit(Blow.SpinKick, Finish(level))
            {
                Hurt = Hurt.Launch,
                Push = level <= 1 ? 3f : level == 2 ? 3.5f : 4.2f,
                Lift = level <= 1 ? 1.6f : level == 2 ? 2f : 2.4f,
                Finisher = true,
                Power = 1.4f,
                Daze = 0.6f,
                WindScale = 0.7f,
                OnLand = (mm, v, r) => Last(mm, v)
            });
            m.Recover(0.26f);
            return m.Start();
        }

        private static void Tick(ChoreoMove m, State st, float k)
        {
            float dt = Clock.Dt;
            // the body jerks with the latest kick, held a few hundredths
            st.JerkT -= dt;
            if (st.JerkT > 0f)
            {
                m.Pose.Rot = st.Rot;
                m.Pose.SX = st.SX;
                m.Pose.SY = st.SY;
            }
            // it glides after the foe it drives back
            if (m.V != null && m.VOk)
            {
                if (Vector2.Distance(m.Stance, m.VG) > m.Stand + 0.12f * m.Kk)
                {
                    m.Stance = Vector2.MoveTowards(m.Stance, m.StandSpot(m.Stand), dt * 6f * m.Kk);
                    if (m.Vis && Random.value < 0.25f)
                    {
                        ChoreoFx.HeelDust(m.Pose.Ground, -m.Dir, m.Px, m.A.current_tile, m.Kk * 0.7f);
                    }
                }
            }
            // the fan: a faint blur of legs behind the kicks
            st.FanT -= dt;
            if (st.FanT <= 0f && m.Vis)
            {
                st.FanT = 0.09f;
                float r = Mathf.Clamp(9f * m.Kk * (m.Level >= 3 ? 1.15f : 1f), 5f, 40f);
                Particle fan = Kit.Swish(m, Art.Arc(Mathf.RoundToInt(r), 0.9f, 60f, -50f, 2), 0.12f, new Vector2(1f * m.Fwd, -1f), m.Fwd,
                    Fx.WithA(m.Hue.Core, 0.55f), Fx.WithA(m.Hue.Edge, 0.4f), 4);
                if (fan != null)
                {
                    fan.Rot = Random.Range(-14f, 14f);
                }
            }
            // kicks land faster and faster: the i-th at (i/n)^0.85 of the beat
            while (st.I < st.N && k >= Mathf.Pow(st.I / (float)st.N, 0.85f))
            {
                int i = st.I++;
                if (!m.VOk)
                {
                    if (!m.Retarget())
                    {
                        m.Lose();
                        return;
                    }
                }
                if (i == 0 && !m.Touch(m.V))
                {
                    return;   // answered (a clinch, a guard): the engine plays it out
                }
                OneKick(m, st, i);
            }
        }

        private static void OneKick(ChoreoMove m, State st, int i)
        {
            Actor v = m.V;
            float px = m.Px;
            int band = i % 3;
            float frac = band == 0 ? Random.Range(0.75f, 0.9f) : band == 1 ? Random.Range(0.45f, 0.6f) : Random.Range(0.2f, 0.32f);
            Vector2 at = m.ContactPoint(frac) + new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(-0.8f, 0.8f)) * m.VPx;
            // the body: leaning back on its standing leg, snapping with each kick
            st.Rot = band == 0 ? Random.Range(-20f, -12f) : band == 1 ? Random.Range(-12f, -6f) : Random.Range(-4f, 2f);
            st.SX = Random.Range(1.1f, 1.22f);
            st.SY = Random.Range(0.88f, 0.94f);
            st.JerkT = 0.03f;
            m.Pose.Rot = st.Rot;
            m.Pose.SX = st.SX;
            m.Pose.SY = st.SY;
            if (m.Vis)
            {
                Vector2 hip = m.Root(Limb.Foot) + new Vector2(0f, Random.Range(-0.5f, 1.5f) * px);
                ChoreoFx.Smear(hip, at, px, 0.08f, 2, m.Level >= 2 ? 3 : 2, Color.white, m.Hue.Main, FxLayer.Over, 7);
                if ((i & 1) == 0)
                {
                    // the ghost of the last kick at another height: the fan of legs
                    Vector2 other = m.ContactPoint(band == 0 ? 0.35f : 0.8f);
                    ChoreoFx.Smear(hip, other, px, 0.06f, 1, 2, Fx.WithA(m.Hue.Core, 0.7f), Fx.WithA(m.Hue.Edge, 0.5f), FxLayer.Over, 6);
                    Fx.Ghost(m, -m.Dir * 1.5f * px, -6f, 0.07f, Fx.WithA(m.Hue.Ghost, 0.45f), m.Hue.GhostEnd);
                }
                Particle star = Fx.Spawn(Art.HitStars[0], at, px, 0.05f, m.TopLayer, 20);
                if (star != null)
                {
                    star.Rot = Random.Range(0f, 90f);
                    star.Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                }
                if (i % 3 == 0)
                {
                    Kit.Sparks(at, px, 2, 40f, 90f, m.Hue.Edge);
                }
            }
            HitResult r = Combat.Hit(m.A, v, st.Per * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            // head kicks snap the head back, body kicks fold it, low ones rock it: each drives it a little further
            Hurt hurt = band == 0 ? Hurt.SnapBack : band == 1 ? Hurt.Fold : Hurt.Flinch;
            Impact im = Impact.Of(hurt, m.Dir, band == 2 ? 0f : 0.26f, 1.4f);
            im.Hold = 0.06f;
            Victims.Hurt(v, m.A, m, im);
            if ((i & 1) == 0)
            {
                ChoreoFx.BlowSound(Limb.Foot, 0.7f, false, v.current_tile);
            }
            m.SetLink(Link.Close, v);
        }

        /// <summary>III: the last kick blasts a ring of wind off the foe and tears the ground along its flight.</summary>
        private static void Last(ChoreoMove m, Actor v)
        {
            if (m.Level < 3 || v == null)
            {
                return;
            }
            Vector2 g = Victims.GroundOf(v);
            if (m.Vis)
            {
                Hue wind = m.Hue.As(Element.Wind);
                Particle ring = Fx.Ring(Choreo.BodyAt(v, 0.5f), m.Px, 0.28f, 3f, 18f * m.Kk, 2, 1, FxLayer.Top, 8, 2.2f, 2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(wind.Edge, 0.6f), Fx.WithA(wind.Edge, 0f));
                if (ring != null)
                {
                    ring.Rot = Mathf.Atan2(m.Dir.y, m.Dir.x) * Mathf.Rad2Deg;
                }
                Kit.SpeedLines(Choreo.BodyAt(v, 0.5f), m.Dir, m.Px, 5, 5f, 0.16f, Fx.WithA(wind.Edge, 0.85f));
            }
            Vector2 end = Combat.Clamp(g + m.Dir * 2.6f * m.Kk);
            if (Combat.ClearPath(g, end))
            {
                Ruin.Furrow(m.A, g, end, 0.6f, m.Scale, m.Pose.Ground, false);
            }
        }
    }
}
