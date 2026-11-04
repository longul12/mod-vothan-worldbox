using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// MOUNTAIN TOSS (Earth Breaker, Master). Made for big bodies (brutes, titans, kaiju). It runs in,
    /// seizes the foe and hoists it over its head, sinks under the weight a heartbeat (the ground
    /// cracking under its feet), stands tall again, and hurls it 8-14 tiles: the body tumbles along a
    /// high arc and comes down like a falling mountain, a crater and a shockwave where it lands
    /// (everyone near it knocked down), preferring to land in a pile of its friends (Link.Thrown).
    /// II spins round once with the body overhead before the throw, III twice, and the crash is a
    /// Heavy one (wider, deeper, an impact frame). A giant too heavy or too big to hold up (a landed
    /// dragon, a titan, a house-sized beast) is seized at its base instead: the Earth Breaker braces and
    /// strains (both tremble, the ground cracks), then heaves: the earth under it tears loose in a ring
    /// of rock and the giant itself goes up, thrown by the world's own forces 6-10 tiles, and where it
    /// comes down it is driven into its own crater, staggered, everyone near knocked flat. What nothing
    /// can move (the player's Crabzilla) is rocked and wrestled down where it stands, into the same
    /// ruin. Throw I 8, II 11, III 14 tiles.
    /// Damage (x the user's damage): I 0.15 + 0.9 + 1.3 at the crash; II 0.15 + 1.2 + 1.8; III 0.15 + 1.6
    /// + 2.4; others at the crash x0.45 of it. On a giant: I 2.6, II 3.4, III 4.4.
    /// </summary>
    internal sealed class MountainTossTech : Technique
    {
        public override string Id => "mountain_toss";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Throw | Form.Heavy;
        public override Prey Prey => Prey.Titan | Prey.Brute | Prey.Kaiju;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 20f, 34f, 55f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };

        public override Scale ScaleAt(int level)
        {
            return level <= 1 ? Scale.Light : Scale.Heavy;
        }

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Distance(int level)
        {
            return EarthKit.L(level, 8f, 11f, 14f);
        }

        private static float ReleaseMult(int level)
        {
            return EarthKit.L(level, 0.9f, 1.2f, 1.6f);
        }

        private static float CrashMult(int level)
        {
            return EarthKit.L(level, 1.3f, 1.8f, 2.4f);
        }

        private static float HeaveMult(int level)
        {
            return EarthKit.L(level, 2.45f, 3.25f, 4.25f);
        }

        private static float Crater(int level)
        {
            return EarthKit.L(level, 1f, 1.3f, 1.6f);
        }

        private static float Area(int level)
        {
            return EarthKit.L(level, 1.6f, 2.1f, 2.6f);
        }

        public static float Total(int level)
        {
            return 0.15f + ReleaseMult(level) + CrashMult(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.6f, Total = Total(1), Crowd = 0.06f, Alone = 10f, IntoGuard = 1.2f, Worth = 20f },
            new MeleeWant { Reach = 2.8f, Total = Total(2), Crowd = 0.05f, Alone = 12f, IntoGuard = 1.2f, Worth = 26f },
            new MeleeWant { Reach = 3f, Total = Total(3), Crowd = 0.04f, Alone = 14f, IntoGuard = 1.2f, Worth = 32f }
        };

        /// <summary>Too heavy or too big to lift, but standing on the ground: it heaves the earth from under it instead.</summary>
        public static bool Giant(Actor v, Actor by)
        {
            if (v == null || by == null || v.isFlying() || v.asset.is_boat || !Combat.Grounded(v))
            {
                return false;
            }
            return Weights.Heavy(v) || v.actor_scale > Mathf.Max(0.3f, by.actor_scale * 2.4f);
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            bool lift = Choreo.Holdable(v, c.Self);
            if (!lift && !Giant(v, c.Self))
            {
                return null;   // neither liftable nor a giant to heave (flying, held by another...)
            }
            if (c.Forced)
            {
                return p;
            }
            // it needs footing for the heave: not into the water
            if (!Combat.SafeSpot(v.current_position) && !Choreo.Wading(v.current_position))
            {
                return null;
            }
            if (lift)
            {
                // a pile of its friends where it would land: thrown into them
                Vector2 dir = (v.current_position - c.Pos).normalized;
                int pile = c.CountNear(c.Pos + dir * Distance(level), 2.5f);
                p.Score += Mathf.Min(3, pile) * 8f;
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
            Actor v = p.Target;
            if (v != null && !Choreo.Holdable(v, a) && Giant(v, a))
            {
                Heave(m);
            }
            else
            {
                Throw(m);
            }
            return m.Start();
        }

        // ------------------------------------------------------------- the throw --

        private static bool Holding(ChoreoMove m)
        {
            return m.Held != null;
        }

        private static void Throw(ChoreoMove m)
        {
            int level = m.Level;
            m.RunUp().OnlyIf(mm => mm.V != null && Vector2.Distance(mm.Stance, mm.VG) > 1.8f * mm.Kk);
            m.StepIn()
                .Grab(0.15f)
                // up over its head, arms locked out
                .Lift(Grip.Overhead, 0.24f, new Shape(-4f, 0.9f, 1.16f, 0f));
            // it sinks under the weight (the ground cracks under its feet), then stands tall again
            Beat sink = new Beat { Tempo = Tempo.Wind, Dur = 0.1f, To = new Shape(4f, 1.14f, 0.86f, 0f), Ease = Ease.OutQuad, Tremble = true, Track = false, If = Holding };
            sink.Enter = mm =>
            {
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 8, 30f, 0.45f, mm.Kk);
                    if (mm.Level >= 2)
                    {
                        Kit.Crater(mm.Pose.Ground, mm.Px * mm.Kk, tile, false, 4f, true, mm.Hue.Edge);
                    }
                }
                Ruin.Quake(mm.A, mm.Pose.Ground, Scale.Light, 0.6f, mm.Kk);
            };
            m.Add(sink);
            m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(-6f, 0.9f, 1.16f, 0f), Ease = Ease.OutBack, Track = false, If = Holding });
            if (level >= 2)
            {
                // the airplane spin: round and round with the body overhead, dust whirling
                int turns = level - 1;
                m.SpinInPlace(turns, 0.24f * turns).OnlyIf(Holding);
                float ringT = 0f;
                m.Every((mm, k) =>
                {
                    ringT -= Clock.Dt;
                    if (ringT <= 0f && mm.Vis)
                    {
                        ringT = 0.07f;
                        ChoreoFx.HeelDust(mm.Pose.Ground, Random.insideUnitCircle, mm.Px, mm.A.current_tile, mm.Kk);
                        Fx.Ring(mm.Pose.Ground, mm.Px, 0.2f, 4f * mm.Kk, 14f * mm.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                            Fx.WithA(mm.Hue.Edge, 0.7f), Fx.WithA(mm.Hue.Edge, 0f));
                    }
                });
            }
            // drawn back behind its head...
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.12f, To = new Shape(-22f, 0.9f, 1.12f, float.NaN), Grip = new Grip(-3f, 8f, -40f), Ease = Ease.OutQuad, Track = false, If = Holding });
            // ...and hurled
            Beat hurl = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(28f, 1.24f, 0.86f, float.NaN, 0.16f), Grip = new Grip(7f, 5f, 60f), Ease = Ease.OutExpo, Track = false, If = Holding };
            Flight flight = new Flight();
            hurl.Exit = mm => Release(mm, flight);
            m.Add(hurl);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.16f, To = new Shape(18f, 1.12f, 0.9f, 0f, 0.1f), Ease = Ease.OutQuad, Track = false });
            // it watches the body fly and come down (no one else takes it out of the air meanwhile)
            m.WaitUntil(mm => !flight.Out || flight.Down || mm.T > flight.Until, 2.5f);
            m.Recover(0.28f);
        }

        private static readonly List<Actor> _pile = new List<Actor>();

        /// <summary>Where the body comes down: 'dist' tiles ahead, onto a pile of its friends if one stands near there, on solid ground.</summary>
        private static Vector2 ThrowSpot(ChoreoMove m, Actor thrown, float dist)
        {
            Vector2 dir = m.Dir;
            Vector2 ahead = Combat.Clamp(m.Stance + dir * dist);
            Vector2 want = ahead;
            Choreo.Foes(m.A, ahead, 3.5f, _pile);
            float best = float.MaxValue;
            for (int i = 0; i < _pile.Count; i++)
            {
                Actor u = _pile[i];
                if (u == thrown)
                {
                    continue;
                }
                float d = (u.current_position - ahead).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    want = u.current_position;
                }
            }
            if (Combat.FindLanding(want, 3f, out Vector2 spot) && Vector2.Distance(m.Stance, spot) > 2f)
            {
                return spot;
            }
            for (float f = 0.75f; f >= 0.3f; f -= 0.15f)
            {
                if (Combat.FindLanding(Combat.Clamp(m.Stance + dir * dist * f), 1.5f, out spot))
                {
                    return spot;
                }
            }
            return m.Ahead(2f);
        }

        /// <summary>A thrown body on its way (the move waits for it to come down).</summary>
        private sealed class Flight
        {
            public bool Out, Down;
            public float Until;
        }

        private static void Release(ChoreoMove m, Flight fl)
        {
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim h = m.Held;
            Actor v = h.A;
            long vid = v.Sid();
            Vector2 from = h.P.Pose.Ground;
            Vector2 dest = ThrowSpot(m, v, Distance(level) * Mathf.Lerp(1f, m.Kk, 0.4f));
            HitResult r = Combat.Hit(m.A, v, ReleaseMult(level) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            m.Held = null;
            float d = Vector2.Distance(from, dest);
            Impact im = Impact.Of(Hurt.Thrown, dest - from, 0f, 1.4f);
            im.To = dest;
            im.Lift = Mathf.Clamp(1.2f + 0.16f * d, 1.4f, 3.6f);
            im.Spin = -(480f + 40f * d);
            im.Crater = Crater(level) * 0.6f;
            im.Daze = 1.2f;
            h.LetGo(im, m.A, m);
            m.SetLink(Link.Thrown, v);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                Kit.SpeedLines(h.P.BodyPos(), dest - from, m.Px, 5, 5f, 0.16f, Fx.WithA(m.Hue.Edge, 0.85f));
                Fx.FocusLines(h.P.BodyPos(), m.Px, Fx.Count(6), 8f * m.Kk, 14f * m.Kk, 0.1f);
            }
            float cm = m.Cm;
            // the crash, as it comes down (the flight of a Thrown body lasts 0.32 + 0.055 s a tile)
            float flight = 0.32f + 0.055f * d;
            fl.Out = true;
            fl.Until = m.T + flight + 0.25f;
            Areas.Add(new CueHazard(m, flight, ch =>
            {
                fl.Down = true;
                Crash(ch, m, v, vid, dest, level, cm);
            }));
        }

        /// <summary>The thrown body comes down: the crash's blow, the crater, the shockwave.</summary>
        private static void Crash(CueHazard ch, ChoreoMove m, Actor v, long vid, Vector2 dest, int level, float cm)
        {
            if (v == null || v.Sid() != vid || !Combat.Living(v) || Vector2.Distance(Victims.GroundOf(v), dest) > 1.8f)
            {
                return;   // killed in the air, or taken: nothing came down here
            }
            Actor a = m.A;
            float px = m.Px, k = m.Kk;
            HitResult hr = Combat.Hit(a, v, CrashMult(level) * cm, true, false, 0f, m);
            if (Dev.On) Dev.Trace("mountain_toss crash: " + v.asset.id + "#" + vid + " x" + (CrashMult(level) * cm).ToString("F2") + (hr.Killed ? " killed" : " hp " + v.getHealth()));
            Victims.Flash(v, Color.white, 0.07f);
            WorldTile tile = Combat.TileAt(dest);
            Scale s = level >= 2 ? Scale.Heavy : Scale.Light;
            bool vis = Kit.Visible(dest, 0.4f);
            if (vis)
            {
                Vector2 hit = dest + new Vector2(0f, 0.6f * k);
                if (level >= 2)
                {
                    Kit.ImpactFrame(hit, px, (0.3f + 0.1f * level) * k, m.Hue.Pal, false, FxLayer.Behind);
                }
                ChoreoFx.Impact(hit, Vector2.down, px, k, 1.4f + 0.2f * level, m.Hue, FxLayer.Top, true);
                Kit.DustRing(dest, px, tile, 14 + 4 * level, 50f + 10f * level, 0.6f, k);
                Kit.Debris(dest, px, tile, 8 + 3 * level, k);
                EarthKit.Boulders(dest, px, 2 + level, k, EarthKit.Stone(dest));
                Kit.Shockwave(dest, px, 4f, Area(level) * 1.2f / px, 0.4f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
                Kit.Crater(dest, px * k, tile, Ruin.Force(Tiers.Of(a), s) >= 3, 7f, false, m.Hue.Edge);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.05f);
            Kit.Stop(dest, (0.03f + 0.015f * level) * m.ImpactK);
            Ruin.Blast(a, dest, Crater(level), s, a.current_position);
            Ruin.Quake(a, dest, s, 1.4f, k);
            // the shockwave: everyone near where it lands is knocked down
            m.AreaBlow(dest, Area(level), CrashMult(level) * 0.45f, v, Hurt.Knockdown, 0.9f, 1.1f);
            if (!m.Ended && Combat.Living(v))
            {
                m.SetLink(Link.Down, v);
            }
        }

        // -------------------------------------------------------------- the heave --

        /// <summary>The giant it heaves: thrown by the world's own forces when it can be moved at all, else wrestled down where it stands.</summary>
        private sealed class Heaved
        {
            public Actor V;
            public long Id = -1;
            public bool Thrown, Up, Crashed;
            public float T0;

            public bool Same => V != null && V.Sid() == Id;

            public bool Ok => Same && Combat.Living(V);

            /// <summary>The thrown giant has come down (or it is gone, or the wait is over).</summary>
            public bool Down(ChoreoMove m)
            {
                if (!Ok)
                {
                    return true;
                }
                if (V.under_forces || V.position_height > 0.05f)
                {
                    Up = true;
                    return false;
                }
                return Up || m.T - T0 > 0.6f;
            }
        }

        private static void Heave(ChoreoMove m)
        {
            Heaved gi = new Heaved();
            m.RunUp().OnlyIf(mm => mm.V != null && Vector2.Distance(mm.Stance, mm.VG) > 1.8f * mm.Kk);
            m.StepIn();
            // it seizes the giant at its base (a lunge, arms out)
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.08f, To = new Shape(-8f, 0.94f, 1.05f, 0f, -0.04f), Ease = Ease.OutQuad, NeedsVictim = true, Forms = Form.Grab });
            Beat seize = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(14f, 1.18f, 0.9f, 0f, 0.16f), Ease = Ease.OutExpo, Forms = Form.Grab };
            seize.On(0.5f, mm =>
            {
                if (Seize(mm))
                {
                    gi.V = mm.V;
                    gi.Id = mm.VId;
                }
            });
            m.Add(seize);
            // it braces and strains: the ground cracks under it and under the giant
            Beat brace = new Beat { Tempo = Tempo.Wind, Dur = 0.14f, To = new Shape(8f, 1.22f, 0.8f, 0f, 0.1f), Ease = Ease.OutQuad, Track = false, Forms = Form.Grab | Form.Heavy };
            brace.Enter = mm =>
            {
                if (mm.Lost)
                {
                    return;
                }
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
                if (mm.Vis)
                {
                    Kit.Crater(mm.Pose.Ground, mm.Px * mm.Kk, tile, false, 5f, true, mm.Hue.Edge);
                    EarthKit.Crack(mm.Pose.Ground, mm.VG, mm.Px, 1.6f, mm.Level >= 2 ? mm.Hue.Edge : (Color?)null);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 10, 36f, 0.5f, mm.Kk);
                }
            };
            m.Add(brace);
            Beat strain = new Beat { Tempo = Tempo.Free, Dur = 0.26f, Tremble = true, Track = false, Forms = Form.Grab | Form.Heavy };
            float rumbleT = 0f;
            strain.Every = (mm, k) =>
            {
                if (gi.Ok)
                {
                    Combat.Pin(gi.V, 1f);
                    if (Random.value < 0.25f)
                    {
                        gi.V.startShake(0.1f, 0.08f, true, false);
                    }
                }
                rumbleT -= Clock.Dt;
                if (rumbleT > 0f)
                {
                    return;
                }
                rumbleT = 0.09f;
                Ruin.Quake(mm.A, mm.Pose.Ground, Scale.Casual, 0.7f + k, mm.Kk);
                if (mm.Vis)
                {
                    ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                    Kit.Debris(mm.VG, mm.Px, Combat.TileAt(mm.VG), 1, mm.Kk * 0.6f);
                }
            };
            m.Add(strain);
            // the heave: it surges up, arms over its head, the earth under the giant tears loose and the giant goes up
            Beat heave = new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(-14f, 0.86f, 1.24f, 0.12f), Ease = Ease.OutExpo, Track = false, Forms = Form.Grab | Form.Throw | Form.Heavy };
            heave.Enter = mm => Uproot(mm, gi);
            m.Add(heave);
            // thrown: it watches the giant fly, arms still up, and the earth shakes where it comes down
            m.PoseTo(0.14f, new Shape(-20f, 0.9f, 1.16f, 0.04f), Ease.OutQuad, Tempo.Follow).OnlyIf(mm => gi.Thrown);
            m.WaitUntil(mm => gi.Down(mm), 2.6f).OnlyIf(mm => gi.Thrown).Every((mm, k) =>
            {
                if (gi.Same)
                {
                    Vector2 d = gi.V.current_position - mm.Stance;
                    if (Mathf.Abs(d.x) > 0.1f)
                    {
                        mm.Face(new Vector2(d.x, 0f));
                    }
                }
            });
            m.Do(mm => Landed(mm, gi)).OnlyIf(mm => gi.Thrown);
            // too much even to throw: it is wrestled down into its own crater
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, Tremble = true, Track = false, If = mm => !gi.Thrown });
            Beat slam = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(30f, 1.3f, 0.74f, 0f, 0.14f), Ease = Ease.InQuad, Track = false, Ghosts = 0.03f, Forms = Form.Throw | Form.Heavy | Form.Overhead, If = mm => !gi.Thrown };
            slam.Exit = mm =>
            {
                if (!mm.Lost && mm.VOk)
                {
                    Smash(mm, mm.V, mm.VG);
                }
            };
            m.Add(slam);
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, To = new Shape(34f, 1.32f, 0.72f, 0f, 0.15f), Ease = Ease.OutExpo, Track = false, If = mm => !gi.Thrown });
            m.Recover(0.3f);
        }

        private static bool Seize(ChoreoMove m)
        {
            if (!m.VOk)
            {
                m.Lose();
                return false;
            }
            float reach = m.Stand + 0.8f * m.Kk + m.V.stats["size"] * 0.5f + 0.4f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                m.Lose();
                return false;
            }
            if (!m.Touch(m.V))
            {
                return false;   // it answered (the engine plays the recoil)
            }
            Combat.Hit(m.A, m.V, 0.15f * m.Cm, false, false, 0f, m);
            Combat.Pin(m.V, 2.5f);
            Victims.Flash(m.V, Color.white, 0.05f);
            Choreo.Sound("event:/SFX/OTHER/Punch", m.A.current_tile);
            if (m.Vis)
            {
                Vector2 at = m.ContactPoint(0.3f);
                Fx.HitSpark(at, m.Px, 0, -m.Dir, 3);
                Fx.FocusLines(at, m.Px, Fx.Count(6), 9f * m.Kk, 16f * m.Kk, 0.1f);
            }
            m.HoldFor(0.04f * m.ImpactK);
            return true;
        }

        /// <summary>
        /// The earth under the giant tears loose and heaves up (a ring of rock bursting round its feet),
        /// and the giant goes up with it: thrown by the world's own forces when anything can move it,
        /// else (the player's Crabzilla) it is only rocked, and wrestled down after.
        /// </summary>
        private static void Uproot(ChoreoMove m, Heaved gi)
        {
            if (m.Lost)
            {
                return;
            }
            if (!m.VOk || !gi.Ok)
            {
                m.Lose();   // the giant fell to another: nothing to heave
                return;
            }
            Actor v = gi.V;
            Vector2 g = Victims.GroundOf(v);
            float vr = Mathf.Clamp(Choreo.SpritePx(v) * Choreo.PxOf(v) * 0.42f, 0.4f, 3.5f);
            WorldTile tile = Combat.TileAt(g);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            Ruin.Quake(m.A, g, Scale.Heavy, 1.2f, m.Kk);
            Ruin.Blast(m.A, g, 0.8f + vr * 0.3f, Scale.Light, m.Pose.Ground, false);
            if (Kit.Visible(g, 0.4f))
            {
                float px = m.Px;
                Color stone = EarthKit.Stone(g);
                int n = 6 + 2 * m.Level;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                    Vector2 at = g + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * vr;
                    int hpx = Mathf.Clamp(Mathf.RoundToInt(vr / px * Random.Range(0.6f, 1f)), 5, 60);
                    Particle p = Fx.Spawn(ChoreoArt.Spike(Mathf.Clamp(hpx / 3, 3, 14), hpx), at, px, 0.55f, Mathf.Sin(a) > 0f ? FxLayer.Behind : FxLayer.Over, 4);
                    if (p == null)
                    {
                        break;
                    }
                    p.Rot = -Mathf.Cos(a) * 18f;
                    p.Sized(0.25f, 1f).Color(stone, stone, Fx.Darken(stone, 0.1f), Fx.WithA(stone, 0f));
                }
                Kit.DustRing(g, px, tile, 16, 46f * Mathf.Sqrt(vr), 0.6f, m.Kk);
                Kit.Debris(g, px, tile, 10, m.Kk);
                EarthKit.Boulders(g, px, 3, m.Kk, stone);
            }
            // over and away: forward, past where it stood, onto dry land
            int level = m.Level;
            Vector2 dir = m.Dir;
            float dist = Distance(level) * 0.75f;
            Vector2 land = Combat.Clamp(g + dir * dist);
            if (!Combat.SafeSpot(land))
            {
                if (Combat.FindLanding(land, 3f, out Vector2 q) && (q - g).sqrMagnitude > 1f)
                {
                    dist = Vector2.Distance(g, q);
                    dir = (q - g) / dist;
                }
                else
                {
                    dist *= 0.5f;
                }
            }
            if (Hurl(v, dir, dist, 2.2f + 0.8f * level))
            {
                gi.Thrown = true;
                gi.T0 = m.T;
                Trails.Follow(v, 1.6f, Fx.WithA(m.Hue.Ghost, 0.45f));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.SpeedLines(Choreo.BodyAt(v, 0.5f), Vector2.up, m.Px, 5, 6f, 0.16f, Fx.WithA(Color.white, 0.85f));
                    Fx.FocusLines(m.HeadPos(), m.Px, Fx.Count(8), 10f * m.Kk, 18f * m.Kk, 0.12f);
                }
                if (Dev.On) Dev.Trace("mountain_toss hurls " + v.asset.id + "#" + gi.Id + " " + dist.ToString("F1") + " tiles");
            }
            else
            {
                // nothing moves it: it is rocked where it stands (and wrestled down next)
                Victims.Hurt(v, m.A, m, Impact.Of(Hurt.Stagger, m.Dir, 0f, 1.2f));
                v.startShake(0.4f, 0.15f, true, true);
            }
        }

        /// <summary>
        /// Throws a unit by the world's own forces (what a god power's wind does), even one too heavy for a
        /// blow to move: 'dist' tiles along 'dir', 'apex' high. False if nothing can move it (the player's
        /// Crabzilla, a boat, one in another's hands).
        /// </summary>
        private static bool Hurl(Actor v, Vector2 dir, float dist, float apex)
        {
            if (v == null || !Combat.Living(v) || v.asset == null || !v.asset.can_be_moved_by_powers || v.is_immovable || v.isInMagnet()
                || v.isInsideSomething() || Moves.Busy(v) || ControllableUnit.isControllingUnit(v) || v.asset.is_boat)
            {
                return false;
            }
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            apex = Mathf.Max(0.3f, apex);
            dist = Mathf.Max(0f, dist);
            Vector2 p = v.current_position;
            for (int i = 0; i < 8; i++)
            {
                Vector2 land = p + dir * dist;
                if (Toolbox.inMapBorder(ref land) && land.x > 2f && land.y > 2f && land.x < MapBox.width - 3 && land.y < MapBox.height - 3)
                {
                    break;
                }
                dist *= 0.6f;
            }
            float g = Mathf.Min(Mathf.Max(0.2f, v.stats["mass"]) * SimGlobals.m.gravity, 20f);
            float vz = Mathf.Sqrt(0.6f * apex);
            float vxy = 0.25f * dist * g / apex;
            float mul = SimGlobals.m.unit_force_multiplier;
            try
            {
                v.cancelAllBeh();
            }
            catch
            {
                // (an AI mid-rebuild)
            }
            v.addForce(dir.x * vxy * mul, dir.y * vxy * mul, vz * mul, true, true);
            return v.under_forces;
        }

        /// <summary>The thrown giant comes down: the crash where it lands.</summary>
        private static void Landed(ChoreoMove m, Heaved gi)
        {
            if (gi.Crashed || !gi.Ok)
            {
                return;
            }
            gi.Crashed = true;
            Smash(m, gi.V, gi.V.current_position);
        }

        /// <summary>The giant meets the ground (slammed down where it stands, or come down from its flight): the blow, its crater, the shockwave.</summary>
        private static void Smash(ChoreoMove m, Actor v, Vector2 g)
        {
            int level = m.Level;
            float vr = Mathf.Clamp(Choreo.SpritePx(v) * Choreo.PxOf(v) * 0.42f, 0.4f, 3.5f);
            HitResult r = Combat.Hit(m.A, v, HeaveMult(level) * m.Cm, true, false, 0f, m);
            m.LastHit = r;
            Victims.Flash(v, Color.white, 0.08f);
            if (Combat.Living(v))
            {
                Combat.Stagger(v, 1f + 0.3f * level);
            }
            WorldTile tile = Combat.TileAt(g);
            Scale s = level >= 2 ? Scale.Heavy : Scale.Light;
            float px = m.Px, k = m.Kk;
            if (Kit.Visible(g, 0.4f))
            {
                Vector2 hit = g + new Vector2(0f, vr * 0.5f);
                if (level >= 2 || r.Crit)
                {
                    Kit.ImpactFrame(hit, px, (0.35f + 0.1f * level) * Mathf.Max(k, vr), m.Hue.Pal, r.Crit, FxLayer.Behind);
                }
                ChoreoFx.Impact(hit, Vector2.down, px, k, 1.6f + 0.2f * level, m.Hue, FxLayer.Top, true);
                Kit.DustRing(g, px, tile, 18 + 4 * level, 60f * Mathf.Sqrt(vr), 0.7f, k);
                Kit.Debris(g, px, tile, 12, k);
                EarthKit.Boulders(g, px, 3 + level, k, EarthKit.Stone(g));
                Kit.Shockwave(g, px, vr / px, (vr + Area(level)) / px, 0.45f, Fx.WithA(m.Hue.Edge, 0.9f), 3);
                Kit.Crater(g, px * Mathf.Max(k, vr), tile, Ruin.Force(m.Tier, s) >= 3, 8f, false, m.Hue.Edge);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.05f);
            Kit.Stop(g, (0.05f + 0.02f * level) * m.ImpactK);
            Ruin.Blast(m.A, g, Crater(level) + vr * 0.5f, s, m.Pose.Ground);
            Ruin.Quake(m.A, g, Scale.Heavy, 1.6f, k);
            m.AreaBlow(g, vr + Area(level), CrashMult(level) * 0.45f, v, Hurt.Knockdown, 1f, 1.1f);
            m.SetLink(Link.Down, v);
            m.HoldFor(0.05f * m.ImpactK);
        }
    }
}
