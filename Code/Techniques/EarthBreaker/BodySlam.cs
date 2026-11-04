using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// BODY SLAM (Earth Breaker, Novice). It sinks low, arms spread, and launches itself at the foe:
    /// up in an arc, tipping over at the top, and comes down flat on its belly right on top of it.
    /// The foe is flattened (crushed into the ground, squashed, then it keels over: Down; one already
    /// lying is pinned flat, trembling); a ring of dust, a small crater. It pushes itself up off the
    /// body and is back on its feet beside it. II leaps higher with a front flip, the crater is
    /// deeper and the thump knocks down everyone close; III bounces off the body high into the air,
    /// turns over once more and comes down on it again, harder: a wide crater, a shockwave, the foe
    /// pinned, stars round its head. A takedown (Grab): it beats a guard. Nothing it can't bear down
    /// on (no giants, nothing flying), never into water. Damage (x the user's damage): I 1.5,
    /// II 2.0 (+ x0.5 on those close), III 1.1 + 1.9 (+ x0.6 on those close).
    /// </summary>
    internal sealed class BodySlamTech : Technique
    {
        public override string Id => "body_slam";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Body | Form.Grab | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 9f, 16f, 26f };
        public override float[] Cooldown => new[] { 5f, 6f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        private const float FirstSlam = 1.1f;

        /// <summary>The (last) slam's damage.</summary>
        public static float Slam(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 1.9f;
        }

        public static float Total(int level)
        {
            return Slam(level) + (level >= 3 ? FirstSlam : 0f);
        }

        /// <summary>A body landing on a body: a heavy, low blow (dust at the feet), crushing.</summary>
        private static readonly BlowShape Flop = new BlowShape
        {
            Limb = Limb.Body,
            Weight = 1.3f,
            Hurt = Hurt.DriveIn,
            Contact = 0.45f,
            Forms = Form.Body | Form.Grab | Form.Heavy
        };

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.8f, Total = Total(1), Crowd = 0.05f, Alone = 6f, IntoGuard = 1.15f, Worth = 8f },
            new MeleeWant { Reach = 3.2f, Total = Total(2), Crowd = 0.04f, Alone = 7f, IntoGuard = 1.15f, Worth = 10f },
            new MeleeWant { Reach = 3.6f, Total = Total(3), Crowd = 0.03f, Alone = 8f, IntoGuard = 1.15f, Worth = 14f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (MeteorSlam.AtDeathsDoor(p.Target))
            {
                return null;   // (already falling: nothing to spend it on)
            }
            Actor v = p.Target;
            // it would bounce off a giant; it can't land on what flies
            if (Weights.Heavy(v) || !Victims.Movable(v, c.Self))
            {
                return null;
            }
            // it lands where the foe stands: never in the water
            if (!Combat.SafeSpot(v.current_position) && !Choreo.Wading(v.current_position))
            {
                return null;
            }
            if (c.State(v).Down)
            {
                p.Score += 8f;   // flattened where it lies
            }
            if (level >= 2)
            {
                int near = c.CountNear(v.current_position, 1.4f);
                p.Score += Mathf.Max(0, near - 1) * 5f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float apex = level <= 1 ? 1f : level == 2 ? 1.45f : 1.8f;
            float air = level <= 1 ? 0.34f : level == 2 ? 0.4f : 0.44f;
            // anticipation: it sinks low under its own weight, arms spread
            m.Crouch(level <= 1 ? 0.12f : 0.15f, level <= 1 ? 1f : 1.25f);
            if (level >= 2)
            {
                m.Hold(0.04f, true, Tempo.Wind);
            }
            // up and over: it tips forward late in the arc and comes down flat on its belly over the foe
            m.Add(LeapOnto(apex, air, level >= 2 ? 360f : 0f, true));
            if (level >= 3)
            {
                Hit first = new Hit(Blow.Stomp, FirstSlam) { Hurt = Hurt.DriveIn, Power = 1.1f };
                m.Add(Contact(first, false, 0f, 0f));
                // it bounces high off the body, rolls over once in the air, and comes down on it again
                m.Add(LeapOnto(1.25f, 0.34f, 360f, false));
            }
            Hit last = new Hit(Blow.Stomp, Slam(level))
            {
                Hurt = Hurt.DriveIn,
                Finisher = level >= 2,
                Power = level <= 1 ? 1f : level == 2 ? 1.2f : 1.45f,
                Crater = level <= 1 ? 0.5f : level == 2 ? 0.8f : 1.15f,
                Daze = level >= 3 ? 1.2f : 0.6f
            };
            m.Add(Contact(last, true, level <= 1 ? 0f : level == 2 ? 1.2f : 1.6f, level <= 1 ? 0f : level == 2 ? 0.5f : 0.6f));
            // it pushes itself up off the body (II, III: a back flip off it) and lands beside it
            Beat off = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = level <= 1 ? 0.22f : 0.28f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = Beside,
                Apex = level <= 1 ? 0.3f : 0.55f,
                Spin = level >= 2 ? -360f : 0f,
                To = new Shape(0f, 1f, 1f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = level >= 2 ? 0.035f : 0f,
                Track = false,
                Forms = Form.Body | Form.Aerial
            };
            off.Exit = mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                }
            };
            m.Add(off);
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>
        /// The leap onto the foe: an arc ending flat on its belly at the height of the foe's body
        /// (lower on one lying down), turning 'spin' degrees on the way. 'takeoff': from the ground
        /// (a ring of dust, the body stretched as it leaves).
        /// </summary>
        private static Beat LeapOnto(float apex, float dur, float spin, bool takeoff)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Route = Route.Arc,
                PathEase = Ease.InOutSine,
                Dest = OnTop,
                Chase = true,
                Apex = apex,
                Spin = spin,
                To = new Shape(84f, 1.06f, 0.95f, 0.2f),
                Ease = Ease.InCubic,
                Ghosts = 0.035f,
                NeedsVictim = true,
                Forms = Form.Body | Form.Grab | Form.Aerial | Form.Heavy
            };
            b.Enter = m =>
            {
                // (it comes down on the foe's body: high on one standing, low on one lying)
                bool down = Lying(m.V);
                float top = m.VHeight * (down ? 0.3f : 0.62f) - m.Pose.PivotUp * m.A.actor_scale;
                Shape to = m.Current.To;
                to.H = Mathf.Clamp(top, 0f, 1.2f * m.Kk) / m.Kk;
                m.Current.To = to;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    if (takeoff)
                    {
                        Kit.DustRing(m.Stance, m.Px, m.A.current_tile, 8, 34f, 0.45f, m.Kk);
                        Fx.LandingPuff(m.Stance, m.Px, m.A.current_tile);
                    }
                    Kit.SpeedLines(m.BodyPos(), Vector2.up, m.Px, 3, 3f, 0.14f, Fx.WithA(m.Hue.Edge, 0.7f));
                }
            };
            b.Every = (m, k) =>
            {
                // stretched as it leaves the ground, spread wide as it comes down on top
                if (k < 0.3f)
                {
                    m.Pose.SX = Mathf.Lerp(0.88f, 1f, k / 0.3f);
                    m.Pose.SY = Mathf.Lerp(1.16f, 1f, k / 0.3f);
                }
                else if (k > 0.75f)
                {
                    float u = (k - 0.75f) / 0.25f;
                    m.Pose.SX = 1f + 0.1f * u;
                    m.Pose.SY = 1f - 0.06f * u;
                }
                if (m.Vis && k > 0.6f)
                {
                    m.Pose.HideShadow = false;
                }
            };
            return b;
        }

        /// <summary>
        /// It lands on the foe: the blow (crushed into the ground; one already lying is pinned), a ring
        /// of dust and a crater; 'area' tiles round it its enemies are knocked down (x 'areaMult').
        /// Then it presses down on the body a beat.
        /// </summary>
        private static Beat Contact(Hit h, bool last, float area, float areaMult)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Impact,
                Dur = last ? 0.14f : 0.1f,
                To = new Shape(88f, 1.24f, 0.76f, 0.05f),
                Ease = Ease.OutExpo,
                Track = false,
                Tag = h,
                Forms = Form.Body | Form.Grab | Form.Heavy
            };
            b.Enter = m => Flatten(m, h, area, areaMult);
            return b;
        }

        private static void Flatten(ChoreoMove m, Hit h, float area, float areaMult)
        {
            WorldTile tile = Combat.TileAt(m.Stance);
            if (!m.VOk || Vector2.Distance(m.Stance, m.VG) > 0.9f * m.Kk + m.V.stats["size"] * 0.5f + 0.4f)
            {
                // the foe slipped away: a belly flop on the bare ground
                Choreo.Sound("event:/SFX/HIT/HitGeneric", tile);
                if (m.Vis)
                {
                    Kit.DustRing(m.Stance, m.Px, tile, 10, 36f, 0.5f, m.Kk);
                    Fx.LandingPuff(m.Stance, m.Px, tile);
                }
                m.Pose.Height = 0f;
                m.Lose();
                return;
            }
            if (!m.Touch(m.V))
            {
                return;   // answered: the engine plays the recoil or the whiff
            }
            Actor v = m.V;
            bool down = Lying(v);
            h.Hurt = down ? Hurt.Pin : Hurt.DriveIn;
            Vector2 at = m.VPoint(down ? 0.2f : 0.5f);
            m.Deliver(v, h, Flop, at, Vector2.down);
            if (m.Vis)
            {
                Kit.DustRing(m.VG, m.Px, tile, area > 0f ? 14 : 9, area > 0f ? 46f : 36f, 0.55f, m.Kk);
                Kit.Debris(m.VG, m.Px, tile, area > 0f ? 8 : 4, m.Kk);
                if (area > 0f)
                {
                    EarthFx.Cracks(m.VG, m.Px, 5, area * 0.6f, m.Hue, tile);
                    Kit.Shockwave(m.VG, m.Px, 4f, area / m.Px, 0.3f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
                }
            }
            if (area > 0f)
            {
                m.AreaBlow(m.VG, area * Mathf.Lerp(1f, m.Kk, 0.5f), areaMult, v, Hurt.Knockdown, 0.6f);
            }
            if (h.Finisher && h.Daze >= 1f && Combat.Living(v))
            {
                EarthFx.Dizzy(v, h.Daze + 0.6f, m.Hue, 3);
            }
        }

        /// <summary>Lying on the ground (stunned, out cold): it is pinned, not crushed upright.</summary>
        private static bool Lying(Actor v)
        {
            return v != null && (v.is_unconscious || v.hasStatusStunned());
        }

        /// <summary>Where it comes down: on the foe, a hair on its own side of it (safe ground).</summary>
        private static Vector2 OnTop(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 q = m.VG - m.Dir * 0.08f * m.Kk;
            if (Combat.SafeSpot(q) || Choreo.Wading(q))
            {
                return q;
            }
            return Combat.FindLanding(q, 1.5f, out Vector2 spot) ? spot : m.Stance;
        }

        /// <summary>Where it lands after pushing off: just in front of the body, on its own side.</summary>
        private static Vector2 Beside(ChoreoMove m)
        {
            if (m.V == null || m.V.Sid() != m.VId)
            {
                return m.Stance;
            }
            Vector2 q = m.StandSpot(m.Stand + 0.25f * m.Kk);
            return Combat.SafeSpot(q) ? q : m.Stance;
        }
    }
}
