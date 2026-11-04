using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// EARTH AND SKY (team sync, Adept). Two friends split the vertical. The "earth" (whichever of
    /// them stands beside the foe) steps in while the "sky" runs to its own side of it and sinks into
    /// a crouch in the same rhythm as the earth's wind-up; the earth's rising blow (an uppercut, a
    /// rising cut when armed) pops the foe high into the air, and on that very frame the sky springs:
    /// a thread of light joins them at the launch. The sky rises past the hanging body, hangs above
    /// it a heartbeat and spikes it straight down with a heel drop (a cleave when armed): a white
    /// streak, the body smashed into the ground at the earth's feet, flattened in a ring of dust that
    /// knocks down whoever stands round it; the earth, braced and looking up the whole time, sees it
    /// land. II: the sky flips on the way up, the spike breaks the ground, and the earth stomps the
    /// fallen foe into the crater (pinned); III: the earth's stomp sends a quake through the crater.
    /// A foe that never went up (the launch was answered or missed) gets the sky's axe kick instead.
    /// Only for bodies the engine can throw (no giants, no flyers).
    /// Damage (x each one's own damage): launch I 0.7, II 0.9, III 1.1; spike I 1.5, II 2, III 2.6
    /// (+ the crash x0.35 of it on those round the crater); stomp II 0.9, III 1.2.
    /// </summary>
    internal sealed class EarthAndSkyTech : Technique
    {
        public override string Id => "earth_and_sky";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Rising | Form.Aerial | Form.Overhead | Form.Kick | Form.Team;
        public override Scale Scale => Scale.Light;
        public override Scale ScaleAt(int level) => level >= 3 ? Scale.Heavy : Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 34f };
        public override float[] Cooldown => new[] { 11f, 12f, 13f };

        private static readonly float[] Shares = { 8f, 12f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float LaunchMult(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.9f : 1.1f;
        }

        private static float SpikeMult(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 2.6f;
        }

        private static float StompMult(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 0.9f : 1.2f;
        }

        /// <summary>How high the foe is popped (tiles x its size).</summary>
        private static float Lift(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.35f : 2.7f;
        }

        public static float Total(int level)
        {
            return LaunchMult(level) + SpikeMult(level) + StompMult(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Total(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 0.8f, Worth = 12f },
            new MeleeWant { Reach = 2.4f, Total = Total(2), Crowd = 0.05f, Alone = 10f, IntoGuard = 0.8f, Worth = 16f },
            new MeleeWant { Reach = 2.6f, Total = Total(3), Crowd = 0.04f, Alone = 12f, IntoGuard = 0.8f, Worth = 20f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            Plan p = Syncs.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            // something it can throw up: no giant, no flyer, nobody held by another
            if (!Victims.Movable(v, c.Self) || !Choreo.Free(v))
            {
                return null;
            }
            List<Actor> partners = Syncs.Pick(c, v, level, 1, Share(level), 7f, 6.5f);
            if (partners.Count == 0)
            {
                return null;
            }
            p.Score += Syncs.Bonus(c, v, partners);
            p.Others = new List<Actor>(partners);
            p.Data = c.Forced;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            bool forced = p.Data is bool b && b;
            if (v == null || p.Others == null || !Moves.CanPerform(a) || !Combat.Living(v) || !Victims.Movable(v, a))
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            List<Actor> partners = Syncs.Recheck(a, v, p.Others, 1, Share(level), forced, 9f);
            if (partners.Count == 0)
            {
                return false;
            }
            Actor u = partners[0];
            Vector2 vp = v.current_position;
            // the earth: whichever of them stands nearer the foe launches it; the other is the sky
            bool leadIsEarth = Vector2.Distance(a.current_position, vp) <= Vector2.Distance(u.current_position, vp) + 0.4f;
            Actor earth = leadIsEarth ? a : u, sky = leadIsEarth ? u : a;
            if (!SkySpot(earth, sky, vp, out Vector2 spot))
            {
                Crew.Fail("earth_and_sky", v);
                return false;
            }
            TeamSync s = new TeamSync(this, level, forced, v, vp, vp - a.current_position);
            SyncMove lead = s.Join(a);
            SyncMove part = s.Join(u);
            SyncMove e = leadIsEarth ? lead : part, k = leadIsEarth ? part : lead;
            e.Role = 0;
            k.Role = 1;
            k.Mark = spot;
            Earth(e);
            Sky(k, v);
            return s.Go(1, Share(level), 5f);
        }

        /// <summary>
        /// Where the sky waits: about 1.5 tiles out on its own side of the foe, never on the earth's
        /// side (at least 70 degrees round from it), on safe ground.
        /// </summary>
        private static bool SkySpot(Actor earth, Actor sky, Vector2 vp, out Vector2 spot)
        {
            float k = Choreo.KOf(sky);
            float ae = Syncs.Ang(earth.current_position - vp);
            float a = Syncs.Ang(sky.current_position - vp);
            float gap = Mathf.DeltaAngle(ae, a);
            if (Mathf.Abs(gap) < 70f)
            {
                a = ae + (gap >= 0f ? 90f : -90f);
            }
            float r = 1.5f * k;
            float[] turns = { 0f, 30f, -30f, 60f, -60f, 100f, -100f };
            for (int i = 0; i < turns.Length; i++)
            {
                Vector2 q = Combat.Clamp(vp + Crew.Dir(a + turns[i]) * r);
                if (Combat.SafeSpot(q))
                {
                    spot = q;
                    return true;
                }
            }
            spot = vp;
            return false;
        }

        // -------------------------------------------------------------- earth --

        private static void Earth(SyncMove e)
        {
            TeamSync s = e.S;
            int L = s.Level;
            float lift = Lift(L);
            float cm = Syncs.Share(e.A, s.V);
            e.Call();
            e.StepIn();
            e.Meet("ready", 0.9f);
            // the launcher: on its contact the foe is popped high and hangs for the sky
            Blow blow = e.Armed ? Blow.RisingSlash : Blow.Uppercut;
            e.Strike(new Hit(blow, LaunchMult(L) * cm)
            {
                Hurt = Hurt.Juggle,
                Lift = lift,
                Push = 0.12f,
                Power = 1.2f,
                Lethal = false,
                Shows = Form.Rising | Form.Punch | Form.Team,
                OnLand = (mm, v, r) => Launched((SyncMove)mm, v, lift)
            });
            // braced, looking up after it
            e.PoseTo(0.14f, new Shape(-18f, 0.92f, 1.1f, 0f), Ease.OutQuad, Tempo.Free);
            e.Meet("spike", 1.1f);
            if (L >= 2)
            {
                // the body comes down at its feet: it stomps it into the crater
                e.WaitUntil(mm => !s.VOk || Combat.HeightOf(s.V) < 0.1f, 0.6f);
                e.Unless(mm => s.Happened("spiked") && mm.VOk && Vector2.Distance(mm.Stance, mm.VG) <= 2.4f * mm.Kk, "end");
                e.Strike(new Hit(Blow.Stomp, StompMult(L) * cm)
                {
                    Hurt = Hurt.Pin,
                    Finisher = true,
                    Power = 1.3f,
                    Daze = 0.8f,
                    Shows = Form.Low | Form.Ground | Form.Team,
                    OnLand = (mm, v, r) =>
                    {
                        if (L >= 3)
                        {
                            Quake((SyncMove)mm, Victims.GroundOf(v), v);
                        }
                    }
                });
                e.Section("end");
            }
            e.Recover(0.25f);
        }

        /// <summary>The launch landed: the foe hangs high for the sky (a longer hang than a juggle's), a thread joins them.</summary>
        private static void Launched(SyncMove e, Actor v, float lift)
        {
            TeamSync s = e.S;
            e.Pass("launch");
            if (!Combat.Living(v) || !s.Once("launch"))
            {
                return;
            }
            Impact im = Impact.Of(Hurt.Juggle, e.Dir, 0.12f, 1.2f);
            im.Lift = lift;
            im.Hold = 0.8f;
            im.Daze = 0.5f;
            Victims.Hurt(v, e.A, e, im);
            s.Union("launched", Choreo.BodyAt(v, 0.55f), 0.8f, true);
        }

        /// <summary>III: the earth's stomp sends a quake through the crater (everyone round it knocked flat).</summary>
        private static void Quake(SyncMove e, Vector2 at, Actor skip)
        {
            WorldTile tile = Combat.TileAt(at);
            if (e.Vis)
            {
                Kit.Shockwave(at, e.Px, 4f, 2.4f / e.Px, 0.4f, Fx.WithA(e.Hue.Edge, 0.9f), 2);
                Kit.DustRing(at, e.Px, tile, 12, 44f, 0.55f, e.Kk);
                Kit.Debris(at, e.Px, tile, 8, e.Kk);
            }
            e.Burst(at, 2.2f, 0.5f, Hurt.Knockdown, 0.9f, 1f, skip);
            Ruin.Blast(e.A, at, 1.1f, Scale.Heavy, e.Pose.Ground);
            Ruin.Quake(e.A, at, Scale.Heavy, 1.2f, e.Kk);
        }

        // ---------------------------------------------------------------- sky --

        private static void Sky(SyncMove k, Actor v)
        {
            TeamSync s = k.S;
            int L = s.Level;
            Vector2 spot = k.Mark;
            float cm = Syncs.Share(k.A, v);
            // how high it must be to come down on the hanging body's head (its own height units)
            float top = Lift(L) * Choreo.KOf(v) + Choreo.SpritePx(v) * Choreo.PxOf(v) * 0.85f;
            float hang = top / Mathf.Max(0.5f, k.Kk);
            k.Call();
            k.RunUp(mm => spot, 0.07f);
            k.OnlyIf(mm => Vector2.Distance(mm.Stance, spot) > 0.25f);
            k.Meet("ready", 0.9f);
            // it sinks into its crouch in the rhythm of the earth's wind-up
            k.Crouch(0.1f, 1.25f);
            k.Last.Tempo = Tempo.Free;
            k.Meet("launch", 0.9f);
            k.Unless(mm => s.Happened("launch") && mm.VOk, "ground");
            // up past the rising body, to above it
            Beat leap = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.24f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = ChoreoMove.ToTarget,
                Apex = 0.25f,
                Spin = L >= 2 ? 360f : 0f,
                To = new Shape(-10f, 0.9f, 1.14f, hang),
                Ease = Ease.OutCubic,
                Ghosts = 0.03f,
                Forms = Form.Aerial | Form.Rising | Form.Team
            };
            leap.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.75f));
                }
            };
            leap.Every = (mm, kk) =>
            {
                float fast = Mathf.Clamp01(1f - kk * 1.3f);
                mm.Pose.SX *= 1f - 0.1f * fast;
                mm.Pose.SY *= 1f + 0.16f * fast;
            };
            k.Add(leap);
            k.Hold(0.04f, false, Tempo.Free);
            // the spike: a heel (a cleave when armed) driven down onto the hanging body
            k.Strike(new Hit(k.Armed ? Blow.Cleave : Blow.HeelDrop, SpikeMult(L) * cm)
            {
                Air = true,
                Hurt = Hurt.Flinch,
                Lethal = false,
                Finisher = true,
                Power = 1.35f + 0.1f * L,
                Shows = Form.Aerial | Form.Overhead | Form.Kick | Form.Team,
                OnLand = (mm, vv, r) => Spiked((SyncMove)mm, vv)
            });
            k.Do(mm => ((SyncMove)mm).Pass("spike"));
            k.Drop(0.18f);
            k.Land();
            k.Goto("done");
            // no body in the air (the launch was answered or missed): an axe kick on the foe where it stands
            k.Section("ground");
            k.Do(mm => ((SyncMove)mm).Pass("spike"));
            k.Strike(new Hit(Blow.AxeKick, SpikeMult(L) * 0.8f * cm) { Hurt = Hurt.DriveIn, Finisher = true, Shows = Form.Kick | Form.Overhead | Form.Team });
            k.Section("done");
            k.Recover(0.25f);
        }

        /// <summary>
        /// The spike landed: the body is smashed straight down into the ground (a white streak from where
        /// it hung, flattened in its crater, a ring of dust knocking down whoever stands round it), and
        /// the sync's shared flash ties the sky above to the earth below.
        /// </summary>
        private static void Spiked(SyncMove k, Actor v)
        {
            TeamSync s = k.S;
            int L = s.Level;
            k.Pass("spike");
            s.Once("spiked");
            Vector2 hit = Choreo.BodyAt(v, 0.6f);
            if (Combat.Living(v) && Combat.HeightOf(v) > 0.25f)
            {
                Vector2 g = Victims.GroundOf(v);
                if (k.Vis)
                {
                    Kit.Line(hit, g + new Vector2(0f, 0.2f * k.Px), k.Px, 0.16f, L >= 3 ? 3 : 2, FxLayer.Top, 12, Color.white, Fx.WithA(k.Hue.Core, 0.85f), Fx.WithA(k.Hue.Edge, 0f));
                    Kit.SpeedLines(Vector2.Lerp(hit, g, 0.5f), Vector2.down, k.Px, 4, 4f, 0.14f, Fx.WithA(k.Hue.Edge, 0.85f));
                }
                Impact im = Impact.Of(Hurt.DriveIn, k.Dir, 0f, 1.6f + 0.1f * L);
                im.Crater = L <= 1 ? 0.55f : L == 2 ? 0.85f : 1.15f;
                im.AreaR = 0.9f + 0.35f * L;
                im.AreaMult = 0.35f * SpikeMult(L);
                im.Daze = 1.1f;
                im.Stop = 0.05f;
                im.Owed = MeteorSlam.AtDeathsDoor(v);
                Syncs.Hurt(v, k.A, k, im);
                // (both leave it Down: whoever follows up meets a body on the ground, not in the air)
                for (int i = 0; i < s.Cast.Count; i++)
                {
                    if (s.Cast[i].In)
                    {
                        s.Cast[i].SetLink(Link.Down, v);
                    }
                }
            }
            s.Union("spike", hit, 0.9f + 0.1f * L, true);
        }
    }
}
