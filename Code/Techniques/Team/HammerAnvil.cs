using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// HAMMER AND ANVIL (team sync, Master). The anvil (whichever of the two stands nearer the foe
    /// and can lift it) seizes it from behind and hoists it, arms pinned, holding it out like a
    /// target; the hammer has run to the far side and, the moment the hold locks, cocks its fist and
    /// gathers its power (Charging: a fast blow from anyone else breaks it). Braced, trembling, the
    /// anvil holds the struggling foe out to it. At full charge the hammer blurs in and drives its
    /// fist through the body: the anvil lets go on that very frame (the shared flash, threads from
    /// both to the blow) and springs back as the foe is hurled away tumbling, to crash in a crater
    /// that knocks down whoever it lands among. If the hammer's charge is broken or it falls, the
    /// anvil finishes alone with a suplex; if the hold never locks, the hammer throws its charged
    /// straight at the free foe.
    /// I: hurled 4 tiles; II: 5.5 tiles, the blow's pressure ploughs a furrow under it, an impact
    /// frame; III: 7 tiles, a deep crater, the furrow runs the whole flight, the world stops on it.
    /// Damage (x each one's own damage): the grab 0.12; the blow I 3, II 4, III 5.2 (the crash x0.4
    /// of it on those it lands among); the anvil's lone suplex I 2, II 2.6, III 3.2.
    /// </summary>
    internal sealed class HammerAnvilTech : Technique
    {
        public override string Id => "hammer_anvil";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Grab | Form.Punch | Form.Charge | Form.Heavy | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 36f, 56f, 84f };
        public override float[] Cooldown => new[] { 15f, 16f, 17f };

        private static readonly float[] Shares = { 22f, 32f, 48f };
        private static readonly float[] Turns = { 0f, 25f, -25f, 50f, -50f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float BlowMult(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 4f : 5.2f;
        }

        private static float SoloMult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.6f : 3.2f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.7f : 0.8f;
        }

        private static float Hurl(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5.5f : 7f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.8f, Total = BlowMult(1), Crowd = 0.1f, Alone = 12f, IntoGuard = 1.1f, Worth = 20f },
            new MeleeWant { Reach = 3f, Total = BlowMult(2), Crowd = 0.08f, Alone = 14f, IntoGuard = 1.1f, Worth = 26f },
            new MeleeWant { Reach = 3.2f, Total = BlowMult(3), Crowd = 0.06f, Alone = 16f, IntoGuard = 1.1f, Worth = 32f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c) || c.Allies.Count == 0)
            {
                return null;
            }
            Plan p = Syncs.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null)
            {
                return null;
            }
            Actor v = p.Target;
            List<Actor> partners = Syncs.Pick(c, v, level, 1, Share(level), 7f, 6.5f);
            if (partners.Count == 0)
            {
                return null;
            }
            // one of them must be able to lift it (no giants, no flyers, nothing in another's hands)
            if (!Choreo.Holdable(v, c.Self) && !Choreo.Holdable(v, partners[0]))
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
            if (v == null || p.Others == null || !Moves.CanPerform(a) || !Combat.Living(v))
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
            bool leadHolds = Choreo.Holdable(v, a), partHolds = Choreo.Holdable(v, u);
            if (!leadHolds && !partHolds)
            {
                return false;
            }
            // the anvil: the one nearer the foe that can lift it; the hammer: the other
            bool leadIsAnvil = leadHolds && (!partHolds || Vector2.Distance(a.current_position, vp) <= Vector2.Distance(u.current_position, vp) + 0.3f);
            Actor anvil = leadIsAnvil ? a : u, hammer = leadIsAnvil ? u : a;
            if (!Station(anvil, hammer, vp, out Vector2 station))
            {
                Crew.Fail("hammer_anvil", v);
                return false;
            }
            TeamSync s = new TeamSync(this, level, forced, v, vp, vp - a.current_position);
            SyncMove lead = s.Join(a);
            SyncMove part = s.Join(u);
            SyncMove an = leadIsAnvil ? lead : part, hm = leadIsAnvil ? part : lead;
            an.Role = 0;
            hm.Role = 1;
            hm.Mark = station;
            Anvil(an);
            Hammer(hm, an.Slot);
            return s.Go(1, Share(level), 7f);
        }

        /// <summary>The hammer's station: on the far side of the foe from the anvil, 2.2 tiles out, with a clear run to it and from it.</summary>
        private static bool Station(Actor anvil, Actor hammer, Vector2 vp, out Vector2 station)
        {
            float k = Choreo.KOf(hammer);
            float a0 = Syncs.Ang(vp - anvil.current_position);
            Vector2 hp = hammer.current_position;
            for (int i = 0; i < Turns.Length; i++)
            {
                Vector2 q = Combat.Clamp(vp + Crew.Dir(a0 + Turns[i]) * 2.2f * k);
                if (!Combat.SafeSpot(q) || !Combat.ClearPath(q, vp))
                {
                    continue;
                }
                if (Vector2.Distance(hp, q) > 0.3f && !Combat.ClearPath(hp, q))
                {
                    continue;
                }
                station = q;
                return true;
            }
            station = vp;
            return false;
        }

        // -------------------------------------------------------------- anvil --

        private static void Anvil(SyncMove a)
        {
            TeamSync s = a.S;
            int L = s.Level;
            a.Call();
            a.StepIn();
            // seized from behind: the body faces away from it, toward the hammer
            a.Grab(0.12f, new Grip(3f, 1f, -6f, true));
            a.Unless(mm => mm.Held != null, "after");
            a.Lift(new Grip(2.6f, 2.2f, -4f, true), 0.16f, new Shape(-12f, 0.94f, 1.08f, 0f));
            a.Do(mm =>
            {
                SyncMove m = (SyncMove)mm;
                m.Pass("held");
                if (s.Once("held") && m.Vis && m.Held != null)
                {
                    Vector2 at = m.Held.P.BodyPos();
                    Fx.Ring(at, m.Px, 0.24f, 3f, 10f * m.Kk, 2, 1, FxLayer.Top, 6, 1f, 2.2f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    Fx.Glint(m.HeadPos() + new Vector2(0f, 1.5f * m.Px), m.Px, 0.16f, m.Hue.Core, 0.9f);
                }
            });
            a.Meet("held", 1.2f);
            // braced, trembling, holding the struggling foe out while the hammer gathers its blow
            Beat hold = new Beat
            {
                Dur = 0f,
                Tempo = Tempo.Free,
                MaxDur = 2.6f,
                To = new Shape(-14f, 0.96f, 1.06f, 0f),
                Ease = Ease.OutQuad,
                Forms = Form.Grab | Form.Team,
                Until = mm => mm.Held == null || s.All("strike")
            };
            float dust = 0f;
            hold.Enter = mm => ((SyncMove)mm).Pass("strike");
            hold.Every = (mm, k) =>
            {
                mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.5f);
                dust -= Clock.Dt;
                if (dust <= 0f && mm.Vis)
                {
                    dust = 0.25f;
                    ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk * 0.6f);
                }
            };
            a.Add(hold);
            a.Unless(mm => mm.Held == null, "solo");
            // let go on the blow: it springs back out of the way of the flying body
            a.BackHop(0.6f, 0.18f);
            a.Goto("after");
            // the hammer never came (its charge was broken, it fell): the anvil finishes alone
            a.Section("solo");
            a.Suplex(SoloMult(L), 0.8f + 0.2f * L, 1.2f);
            a.Section("after");
            a.Recover(0.3f);
        }

        // ------------------------------------------------------------- hammer --

        private static void Hammer(SyncMove h, int anvilSlot)
        {
            TeamSync s = h.S;
            int L = s.Level;
            Vector2 station = h.Mark;
            h.Call();
            h.RunUp(mm => station, 0.075f);
            h.OnlyIf(mm => Vector2.Distance(mm.Stance, station) > 0.3f);
            h.Do(mm =>
            {
                if (s.VOk)
                {
                    mm.Face(s.VG - mm.Stance);
                }
            });
            h.Meet("held", 1.3f);
            h.Unless(mm => s.Happened("held") && s.In(anvilSlot), "free");
            // the fist cocked back: it gathers the blow while the anvil holds the foe out to it
            h.Charge(ChargeTime(L), ChargeStyle.Fist, L <= 1 ? 2.2f : L == 2 ? 2.7f : 3.2f, Element.School);
            h.Last.Tempo = Tempo.Free;
            h.Unless(mm => s.VOk, "done");
            // a blur in, and the fist driven through the held body
            h.Dash(ChoreoMove.ToTarget, 0.02f);
            Beat blow = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(22f, 1.28f, 0.86f, 0f, 0.2f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Punch | Form.Straight | Form.Heavy | Form.Team
            };
            blow.Enter = mm =>
            {
                mm.SpendOrb();
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                if (mm.Vis && s.VOk)
                {
                    Vector2 from = mm.Root(Limb.Fist);
                    Vector2 to = Choreo.BodyAt(s.V, 0.6f);
                    ChoreoFx.Smear(from, to, mm.Px, 0.14f, L >= 3 ? 3 : 2, 2, mm.Hue.Core, mm.Hue.Edge, FxLayer.Over, 7);
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), to - from, mm.Px, 3, 3f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f), FxLayer.Over);
                    Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -6f, 0.08f, Fx.WithA(mm.Hue.Ghost, 0.6f), mm.Hue.GhostEnd);
                }
            };
            blow.On(0.45f, mm => Smash((SyncMove)mm, anvilSlot));
            h.Add(blow);
            h.PoseTo(0.2f, new Shape(14f, 1.12f, 0.92f, 0f, 0.14f), Ease.OutQuad, Tempo.Follow);
            h.Goto("done");
            // nobody holds the foe (the grab was answered, or never came): a charged straight on its own
            h.Section("free");
            h.Strike(new Hit(Blow.Cross, BlowMult(L) * 0.7f * Syncs.Share(h.A, s.V))
            {
                Charged = true,
                WindScale = 4f,
                Hurt = Hurt.Launch,
                Push = 2.5f,
                Lift = 1f,
                Finisher = true,
                Power = 1.5f,
                Shows = Form.Punch | Form.Charge | Form.Heavy | Form.Team
            });
            h.Section("done");
            h.Do(mm => mm.SpendOrb());
            h.Recover(0.3f);
        }

        /// <summary>
        /// The fist lands: the anvil lets go on this very frame, the blow goes through, and the foe is
        /// hurled away (the shared flash, threads from both of them to the blow).
        /// </summary>
        private static void Smash(SyncMove h, int anvilSlot)
        {
            TeamSync s = h.S;
            int L = s.Level;
            h.Pass("strike");
            if (!s.VOk)
            {
                return;
            }
            SyncMove anvil = s.At(anvilSlot);
            bool held = anvil != null && anvil.Held != null && anvil.Held.A == s.V;
            if (held)
            {
                anvil.ReleaseHeld(Hurt.Flinch, 0f);
            }
            Vector2 dir = s.VG - h.Stance;
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = h.Dir;
            }
            dir.Normalize();
            Vector2 contact = Choreo.BodyAt(s.V, 0.6f);
            Hit hit = new Hit(Blow.Cross, BlowMult(L) * (held ? 1f : 0.85f))
            {
                Hurt = Hurt.Flinch,
                Finisher = true,
                Lethal = false,
                Power = 1.7f + 0.15f * L,
                OnLand = (mm, v, r) => Hurled((SyncMove)mm, v, dir)
            };
            HitResult res = h.HitOn(s.V, hit, dir, 0.6f);
            if (res.Landed || res.Killed)
            {
                s.Union("strike", contact, 1.3f + 0.15f * L, true);
            }
        }

        /// <summary>The foe is hurled along the blow and crashes far off (a crater, those it lands among knocked down).</summary>
        private static void Hurled(SyncMove h, Actor v, Vector2 dir)
        {
            TeamSync s = h.S;
            int L = s.Level;
            Vector2 from = Victims.GroundOf(v);
            Vector2 want = Combat.Clamp(from + dir * Hurl(L) * Mathf.Lerp(1f, h.Kk, 0.5f));
            Vector2 to = Combat.FindLanding(want, 4f, out Vector2 land) ? land : from;
            Impact im = Impact.Of(Hurt.Thrown, dir, Hurl(L), 2f);
            im.To = to;
            im.Lift = 0.7f + 0.15f * L;
            im.Spin = -720f;
            im.Crater = L <= 1 ? 0.7f : L == 2 ? 1f : 1.4f;
            im.AreaR = 1f + 0.4f * L;
            im.AreaMult = 0.4f * BlowMult(L);
            im.Daze = 1.2f;
            im.Stop = 0.06f;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            Victims.Hurt(v, h.A, h, im);
            h.SetLink(Link.Thrown, v);
            if (h.Vis)
            {
                Kit.SpeedLines(Choreo.BodyAt(v, 0.5f), dir, h.Px, 5, 4f, 0.16f, Fx.WithA(h.Hue.Edge, 0.9f));
                Kit.Shockwave(from, h.Px, 3f, 16f * h.Kk, 0.3f, Fx.WithA(h.Hue.Edge, 0.85f), 2);
            }
            if (L >= 2)
            {
                // the blow's pressure ploughs the ground under its flight (III: all the way)
                Vector2 end = L >= 3 ? to : Vector2.Lerp(from, to, 0.45f);
                Ruin.Furrow(h.A, from, end, 0.5f + 0.15f * L, s.Scale, h.Pose.Ground);
            }
        }
    }
}
