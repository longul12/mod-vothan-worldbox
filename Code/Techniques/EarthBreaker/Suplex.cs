using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SUPLEX (Earth Breaker, Adept). It closes in and locks its arms round the foe's waist, hoists
    /// it off its feet, and arches over backwards: the body swings up over its head in an arc and
    /// comes down head first behind it, an impact frame, a crater, dust and rock; it holds the
    /// bridge a beat and rolls back up. A grab: it beats a guard (a curled-up foe, a stone skin, an
    /// iron wall is simply picked up), and loses to a fast blow. II sinks under the weight first and
    /// throws it harder (a deeper crater, the shock knocks down everyone close); III rolls straight
    /// back up out of the bridge, hauls the body up off the ground again, leaps with it and suplexes
    /// it a second time from the air into a wide crater. It needs a body it can lift (no giants, no
    /// flyers) and solid ground behind it. Damage (x the user's damage): I 2.0, II 2.6 (+ x0.5 on
    /// those close), III 1.25 + 2.3 (+ x0.5 on those close).
    /// </summary>
    internal sealed class SuplexTech : Technique
    {
        public override string Id => "suplex";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Throw;

        /// <summary>A guard (a curled-up foe, a stance) is simply picked up.</summary>
        public override Form Answers => Form.Guard;

        public override Scale Scale => Scale.Light;

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        public override float[] ManaCost => new[] { 16f, 28f, 46f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        private const float GrabMult = 0.1f, FirstSlam = 1.15f;

        /// <summary>The (last) slam's damage.</summary>
        public static float Slam(int level)
        {
            return level <= 1 ? 1.9f : level == 2 ? 2.5f : 2.3f;
        }

        public static float Total(int level)
        {
            return GrabMult + Slam(level) + (level >= 3 ? FirstSlam : 0f);
        }

        /// <summary>Arms locked round its waist, chest to chest.</summary>
        private static readonly Grip Waist = new Grip(2.4f, 0.8f, -6f, false, 0.95f, 1.02f);

        /// <summary>A body lying on its back in front of it (taken up off the ground).</summary>
        private static readonly Grip Low = new Grip(3.6f, -1.6f, -84f);

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Total(1), Hold = true, Crowd = 0.08f, Alone = 8f, IntoGuard = 1.35f, Worth = 12f },
            new MeleeWant { Reach = 2.4f, Total = Total(2), Hold = true, Crowd = 0.06f, Alone = 9f, IntoGuard = 1.35f, Worth = 15f },
            new MeleeWant { Reach = 2.6f, Total = Total(3), Hold = true, Crowd = 0.05f, Alone = 10f, IntoGuard = 1.35f, Worth = 20f }
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
            // the body comes down behind it: solid ground there, never water
            float k = Choreo.KOf(c.Self);
            Vector2 behind = c.Pos - p.Dir * 0.9f * k;
            if (!Combat.SafeSpot(behind) && !Choreo.Wading(behind))
            {
                return null;
            }
            if (c.State(p.Target).Guarding)
            {
                p.Score += 12f;   // a guard is no answer to being picked up
            }
            if (level >= 2)
            {
                int near = c.CountNear(behind, 1.4f);
                p.Score += near * 5f;
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
            bool down = m.V != null && (m.V.is_unconscious || m.V.hasStatusStunned());
            m.StepIn();
            if (down)
            {
                // a foe lying there is taken up off the ground first
                m.Grab(GrabMult, Low);
                m.Lift(Waist, 0.16f, new Shape(-8f, 0.92f, 1.1f, 0f));
            }
            else
            {
                m.Grab(GrabMult, Waist);
            }
            if (level == 1)
            {
                m.Suplex(Slam(1), 0.7f, 0f, 0.3f);
            }
            else if (level == 2)
            {
                // it sinks under the weight, then throws it over harder
                Strain(m, 0.1f, 0.9f);
                m.Suplex(Slam(2), 1f, 1.3f, 0.28f);
            }
            else
            {
                Strain(m, 0.08f, 0.8f);
                m.Suplex(FirstSlam, 0.75f, 0f, 0.26f);
                // straight back up out of the bridge, turned to the body
                m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.12f, To = new Shape(0f, 1.04f, 0.97f, 0f), Ease = Ease.OutCubic, Track = false, Forms = Form.Grab | Form.Throw });
                // hauled up off the ground again
                m.Grab(0f, Low);
                m.Lift(Waist, 0.12f, new Shape(-8f, 0.92f, 1.1f, 0f));
                Strain(m, 0.1f, 1.25f);
                AirSuplex(m, Slam(3), 1.3f, 1.7f);
            }
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>
        /// III's second suplex: it leaps with the body hoisted to its chest, arches over backwards at
        /// the top and falls with it, driving it head first into the ground behind it from the air.
        /// </summary>
        private static void AirSuplex(ChoreoMove m, float mult, float crater, float area)
        {
            Beat up = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.17f,
                To = new Shape(-22f, 0.9f, 1.14f, 0.95f),
                Ease = Ease.OutCubic,
                Grip = new Grip(2.5f, 5f, -18f),
                Track = false,
                Ghosts = 0.035f,
                Forms = Form.Grab | Form.Throw | Form.Aerial,
                If = mm => mm.Held != null
            };
            up.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 10, 40f, 0.45f, mm.Kk);
                    EarthFx.Cracks(mm.Stance, mm.Px, 4, 0.6f * mm.Kk, mm.Hue, mm.A.current_tile, 1.2f);
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.75f));
                }
            };
            m.Add(up);
            Beat over = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.3f,
                To = new Shape(-172f, 1.02f, 0.96f, 0f),
                Ease = Ease.InQuad,
                Grip = new Grip(3f, 3f, 0f),
                Track = false,
                Ghosts = 0.025f,
                Forms = Form.Grab | Form.Throw | Form.Aerial | Form.Overhead,
                If = mm => mm.Held != null
            };
            over.Every = (mm, k) =>
            {
                if (mm.Vis && k > 0.4f && k < 0.95f)
                {
                    Kit.SpeedLines(mm.BodyPos(), Vector2.down, mm.Px, 1, 4f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f));
                }
            };
            over.Exit = mm => AirSlam(mm, mult, crater, area);
            m.Add(over);
            // the bridge, held a beat: it is the landing pose that reads
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.18f, To = new Shape(-152f, 1.08f, 0.9f, 0f), Ease = Ease.OutQuad, Track = false });
        }

        /// <summary>The body hits the ground head first from the air: the big slam of III.</summary>
        private static void AirSlam(ChoreoMove m, float mult, float crater, float area)
        {
            m.Pose.Height = 0f;
            Victim b = m.Held;
            if (b == null)
            {
                return;
            }
            Actor v = b.A;
            Vector2 at = b.P.Pose.Ground;
            WorldTile tile = Combat.TileAt(at);
            HitResult r = Combat.Hit(m.A, v, mult * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            bool owed = MeteorSlam.AtDeathsDoor(v);
            m.Held = null;
            Impact im = Impact.Of(Hurt.DriveIn, m.Dir, 0f, 1.8f);
            im.Owed = owed;
            im.Daze = 1.4f;
            im.Stop = 0.06f * m.ImpactK;
            b.LetGo(im, m.A, m);
            Victims.Flash(v, Color.white, 0.08f);
            m.HoldFor(0.08f * m.ImpactK);
            if (m.Vis)
            {
                float px = m.Px;
                Vector2 hit = at + new Vector2(0f, px);
                Kit.ImpactFrame(hit, px, 0.55f * m.Kk, m.Hue.Pal, r.Crit, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                ChoreoFx.Impact(hit, Vector2.down, px, m.Kk, 2f, m.Hue, m.TopLayer, true);
                Kit.DustRing(at, px, tile, 16, 54f, 0.6f, m.Kk);
                Kit.Debris(at, px, tile, 12, m.Kk);
                Kit.Crater(at, px * m.Kk, tile, Ruin.Force(m.Tier, Scale.Heavy) >= 3, 8f, false, m.Hue.Edge);
                Kit.Shockwave(at, px, 4f, Mathf.Max(16f, area / px), 0.4f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
                EarthFx.Cracks(at, px, 6, area * 0.7f, m.Hue, tile, 2.2f);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            Ruin.Blast(m.A, at, crater, Scale.Heavy, m.Pose.Ground);
            Kit.Stop(at, 0.07f * m.ImpactK);
            Ruin.Quake(m.A, at, Scale.Heavy, 1.6f, m.Kk);
            m.AreaBlow(at, area * Mathf.Lerp(1f, m.Kk, 0.5f), 0.5f, v, Hurt.Knockdown, 0.8f);
            m.SetLink(Link.Down, v);
            if (!owed && Combat.Living(v))
            {
                EarthFx.Dizzy(v, 2f, m.Hue, 3);
            }
        }

        /// <summary>It sinks under the body's weight, legs bent, trembling: then the heave.</summary>
        private static void Strain(ChoreoMove m, float dur, float depth)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = dur,
                To = new Shape(4f * depth, 1f + 0.14f * depth, 1f - 0.16f * depth, 0f),
                Ease = Ease.OutQuad,
                Tremble = true,
                Track = false,
                Forms = Form.Grab | Form.Throw,
                If = mm => mm.Held != null
            };
            b.Enter = mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                    if (depth >= 1f)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 6, 22f, 0.4f, mm.Kk);
                    }
                }
            };
            m.Add(b);
        }
    }
}
