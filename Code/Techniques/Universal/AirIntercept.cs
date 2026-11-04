using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// AIR INTERCEPT (universal, Expert). Someone comes at it through the air (a heel dropped from a
    /// leap, a diving kick, a flying knee): it doesn't wait for it to land. A dip, and it springs up
    /// to meet it at its height (a ring of dust left on the ground, its afterimage climbing behind
    /// it) and plants a flying side kick in its body: the attacker is knocked out of the air and
    /// flies back the way it came. II kicks harder and farther with a ring of wind off the foot and
    /// an impact frame; III meets it with a rising knee first (it pops up, helpless) and then the
    /// side kick that sends it flying. The answer of the counter web to Aerial; and planned, it
    /// leaps at a foe already airborne within reach (one leaping at it, most of all), cutting the
    /// leap short. Not for fliers (wings are the hunters' business) nor giants.
    ///
    /// Beat sheet (seconds at Novice pace; the tier's tempo shortens them; an answer springs at once):
    ///   DIP     0.03 / 0.08  squashed, a puff
    ///   SPRING  0.06 / 0.14  up to the foe's height (tracking it), stretched, afterimages
    ///   (III) KNEE 0.06       the rising knee: it pops up and hangs (Combat.Reach)
    ///   KICK    0.065  the side kick: a streak from the hip to its body; contact at 35% (Launched)
    ///   HANG    0.06   the kick held up there
    ///   DROP    0.15   back down; LAND 0.08 a puff; RECOVER 0.18. Chain: Launched
    /// Damage (x the user's damage): I 1.4, II 1.9, III 0.7 + 2.1.
    /// </summary>
    internal sealed class AirInterceptTech : Technique
    {
        public override string Id => "air_intercept";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override bool Reactive => true;
        public override Form Forms => Form.AntiAir | Form.Aerial | Form.Kick;
        public override Form Answers => Form.Aerial;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 22f, 32f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        private const Form KickForms = Form.Kick | Form.AntiAir | Form.Aerial | Form.Straight;

        /// <summary>How high (tiles, x its size) it can meet a foe.</summary>
        public static float MaxH(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.1f : 2.6f;
        }

        /// <summary>How far away (tiles, x its size) a foe in the air may be for a planned intercept.</summary>
        private static float Range(int level)
        {
            return level <= 1 ? 3.2f : level == 2 ? 3.8f : 4.4f;
        }

        public static float KickMult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.1f;
        }

        private const float KneeMult = 0.7f;

        public static float Total(int level)
        {
            return KickMult(level) + (level >= 3 ? KneeMult : 0f);
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            // (an area blow, a shockwave, is no body in the air to meet)
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!Meetable(defender, attacker, level))
            {
                return Contact.Hit;
            }
            if (Vector2.Distance(defender.current_position, attacker.current_position) > UniKit.Reach(defender, attacker, 2.6f))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            UniKit.Settle(defender);
            return Perform(defender, attacker, null, level) ? Contact.Parried : Contact.Hit;
        }

        /// <summary>Can 'd' meet 'v' in the air at all: no flier, no boat, no giant, within the leap's height?</summary>
        private static bool Meetable(Actor d, Actor v, int level)
        {
            if (v == null || v.isFlying() || v.asset.is_boat || v.actor_scale > d.actor_scale * 2.4f)
            {
                return false;
            }
            return Combat.HeightOf(v) <= MaxH(level) * Choreo.KOf(d);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Forced)
            {
                return UniKit.ForcedPlan(c, 6f);
            }
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * k;
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                if (c.EnemyDist[i] > range + 0.5f)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Meetable(c.Self, v, level) || Combat.HeightOf(v) < 0.5f || !Choreo.Fair(c.Self, v, out float cm))
                {
                    continue;
                }
                // a foe leaping in its own attack (an intercept), or a body knocked up by someone
                Moves.TryGet(v, out Move vm);
                bool puppet = vm is Puppet;
                bool aerial = vm != null && !puppet && vm.A == v && !vm.Cinematic && (Reads.FormsOf(vm) & Form.Aerial) != 0;
                // (a body someone knocked up only when it comes down right by it: the air follow-ups' first)
                bool tossed = (vm == null || puppet && Victims.Ours(v)) && c.EnemyDist[i] <= 2.6f * k;
                if (!aerial && !tossed)
                {
                    continue;
                }
                if (!Combat.ClearPath(c.Pos, v.current_position) || v.getHealth() <= c.Expected(v, 0.5f))
                {
                    continue;   // (walled off, or a plain blow ends it anyway)
                }
                float score = c.Value(v, c.Expected(v, Total(level)), cm);
                if (aerial)
                {
                    score += Reads.AimsAt(v, c.Self) ? 30f : 15f;
                }
                else
                {
                    score = score * 0.6f + 4f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
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
                Score = bestScore,
                Data = false
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p != null && p.Target != null && Perform(a, p.Target, p, p.Level);
        }

        private bool Perform(Actor a, Actor v, Plan p, int level)
        {
            bool react = p == null;
            ChoreoMove m = react ? ChoreoMove.Answer(a, v, this, level) : ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            float maxH = MaxH(lv) * m.Kk;
            float[] h = { 0f, 0f };   // [0] the height it springs from, [1] the height it is at
            if (!react)
            {
                // planned: the dip, then the spring up to its height, tracking it through the air
                Beat dip = new Beat { Tempo = Tempo.Wind, Dur = 0.08f, To = new Shape(8f, 1.14f, 0.84f, 0f), Ease = Ease.OutQuad, Forms = Form.AntiAir | Form.Aerial };
                dip.Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                    }
                };
                m.Add(dip);
                Beat spring = new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.14f,
                    PerTile = 0.03f,
                    Route = Route.Line,
                    PathEase = Ease.OutQuad,
                    Dest = mm => mm.StandSpot(mm.Stand),
                    Chase = true,
                    To = new Shape(-14f, 0.84f, 1.24f, float.NaN),
                    Ease = Ease.OutCubic,
                    Ghosts = 0.015f,
                    Forms = Form.Aerial | Form.AntiAir
                };
                spring.Enter = mm =>
                {
                    h[0] = h[1] = mm.Pose.Height;
                    TakeOff(mm);
                };
                spring.Every = (mm, k) =>
                {
                    h[1] = Mathf.Lerp(h[0], KickH(mm, maxH), Beat.Apply(Ease.OutCubic, k));
                    mm.Pose.Height = h[1];
                    mm.Pose.HideShadow = false;
                };
                m.Add(spring);
            }
            // an answer has no time for a leap: the very next frame it is up there, its first blow
            // landing while the attacker is still in the air (the leap only an afterimage trail)
            bool first = react;
            if (lv >= 3)
            {
                // the rising knee: it pops up, helpless, for the kick
                Hit knee = new Hit(Blow.Knee, KneeMult) { Hurt = Hurt.Juggle, Lift = 0.45f, Push = 0.2f, Power = 0.9f, Lethal = false };
                Beat kb = new Beat { Tempo = Tempo.Act, Dur = 0.06f, To = new Shape(-6f, 0.92f, 1.16f, float.NaN, 0.08f), Ease = Ease.OutExpo, Forms = Form.Knee | Form.AntiAir | Form.Aerial };
                bool snap = first;
                kb.Enter = mm =>
                {
                    if (snap)
                    {
                        Snap(mm, h, maxH);
                    }
                    Streak(mm, Limb.Knee, 0.42f, 2);
                };
                kb.Every = (mm, k) => Track(mm, h, maxH);
                kb.On(snap ? 0f : 0.4f, mm =>
                {
                    // (a juggle hangs at a height of its own: a little above where it is)
                    if (mm.V != null && mm.V.Sid() == mm.VId)
                    {
                        knee.Lift = Combat.HeightOf(mm.V) / Choreo.KOf(mm.V) + 0.35f;
                    }
                    UniKit.Land(mm, knee, ChoreoMove.ShapeOf(Blow.Knee), 0.42f, 0.9f, true);
                });
                m.Add(kb);
                m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.04f, To = new Shape(-18f, 0.96f, 1.08f, float.NaN), Ease = Ease.OutQuad });
                m.Last.Every = (mm, k) => Track(mm, h, maxH);
                first = false;
            }
            Hit kick = new Hit(Blow.SideKick, KickMult(lv))
            {
                Hurt = Hurt.Launch,
                Push = lv <= 1 ? 2.2f : lv == 2 ? 2.8f : 3.4f,
                Lift = lv <= 1 ? 0.9f : lv == 2 ? 1.3f : 1.7f,
                Daze = lv <= 1 ? 0.4f : lv == 2 ? 0.6f : 0.9f,
                Power = lv <= 1 ? 0.95f : lv == 2 ? 1.1f : 1.2f,
                Finisher = lv >= 2
            };
            Beat kb2 = new Beat { Tempo = Tempo.Act, Dur = 0.065f, To = new Shape(-34f, 1.3f, 0.84f, float.NaN, 0.08f), Ease = Ease.OutExpo, Forms = KickForms };
            bool snapKick = first;
            kb2.Enter = mm =>
            {
                if (snapKick)
                {
                    Snap(mm, h, maxH);
                }
                Streak(mm, Limb.Foot, 0.5f, lv >= 3 ? 3 : 2);
            };
            kb2.Every = (mm, k) => Track(mm, h, maxH);
            kb2.On(snapKick ? 0f : 0.35f, mm =>
            {
                if (UniKit.Land(mm, kick, ChoreoMove.ShapeOf(Blow.SideKick), 0.5f, 0.9f, true))
                {
                    Gust(mm, lv);
                }
            });
            m.Add(kb2);
            // the kick held up there a moment, then down
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.06f, To = new Shape(-22f, 1.12f, 0.94f, float.NaN), Ease = Ease.OutQuad, Track = false });
            m.Add(new Beat { Tempo = Tempo.Travel, Dur = 0.15f, To = new Shape(6f, 0.9f, 1.12f, 0f), Ease = Ease.InQuad, Forms = Form.Aerial, Track = false, Ghosts = 0.04f });
            Beat land = new Beat { Tempo = Tempo.Follow, Dur = 0.08f, To = new Shape(6f, 1.2f, 0.82f, 0f), Ease = Ease.OutExpo, Track = false };
            land.Enter = mm =>
            {
                mm.Pose.Height = 0f;
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/HIT/HitGeneric", tile, 0.08f);
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, tile);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 6, 26f, 0.4f, mm.Kk);
                }
            };
            m.Add(land);
            m.Recover(0.18f);
            return m.Start();
        }

        /// <summary>
        /// The height to kick from: its foot at the middle of the target's body as it is drawn now
        /// (in the air or not), within the leap's reach.
        /// </summary>
        private static float KickH(ChoreoMove mm, float maxH)
        {
            if (mm.V == null || mm.V.Sid() != mm.VId)
            {
                return mm.Pose.Height;
            }
            float body = Combat.HeightOf(mm.V) + mm.VHeight * 0.5f;
            float foot = mm.Pose.PivotUp * mm.A.actor_scale * 0.9f - 0.22f * mm.Hpx * mm.Px;
            return Mathf.Clamp(body - foot, 0.3f * mm.Kk, maxH);
        }

        /// <summary>
        /// An answer's leap in no time: from where it stood to a kick's length from the attacker, at
        /// its height, in one frame. The dust ring stays on the ground and its afterimages climb the
        /// way it went (the leap is only seen as the trail it left).
        /// </summary>
        private static void Snap(ChoreoMove mm, float[] h, float maxH)
        {
            Vector2 from = mm.Stance;
            Vector2 to = mm.V != null && mm.V.Sid() == mm.VId ? mm.StandSpot(mm.Stand) : from;
            Vector2 d = to - from;
            float max = 1.2f * mm.Kk;
            if (d.magnitude > max)
            {
                to = from + d.normalized * max;
            }
            if (!(Combat.SafeSpot(to) || Choreo.Wading(to)) || !Combat.ClearPath(from, to))
            {
                to = from;
            }
            float top = KickH(mm, maxH);
            TakeOff(mm);
            if (mm.Vis)
            {
                Pose keep = mm.Pose;
                for (int i = 1; i <= 3; i++)
                {
                    float u = i / 4f;
                    mm.Pose.Ground = Vector2.Lerp(from, to, u);
                    mm.Pose.Height = top * u;
                    mm.Pose.Rot = -14f;
                    mm.Pose.SX = 0.84f;
                    mm.Pose.SY = 1.24f;
                    Fx.Afterimage(mm, 0.08f + 0.04f * i, Fx.WithA(mm.Hue.Ghost, 0.3f + 0.15f * i), mm.Hue.GhostEnd);
                }
                mm.Pose = keep;
                Vector2 rise = new Vector2(to.x - from.x, top);
                ChoreoFx.Smear(mm.BodyPos(), new Vector2(to.x, to.y + top) + (mm.BodyPos() - from), mm.Px, 0.1f, 1, 2, mm.Hue.Core, mm.Hue.Edge, FxLayer.Behind, 1);
                Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f) + new Vector2(0f, top * 0.5f + mm.Pose.PivotUp * mm.A.actor_scale), rise, mm.Px, 4, 3f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.75f));
            }
            mm.Stance = to;
            mm.Pose.Ground = to;
            mm.Pose.Height = top;
            h[0] = h[1] = top;
        }

        /// <summary>Up there, it keeps level with the target (a juggled body pops, a falling one sinks).</summary>
        private static void Track(ChoreoMove mm, float[] h, float maxH)
        {
            h[1] = Mathf.Lerp(h[1], KickH(mm, maxH), Mathf.Clamp01(Clock.Dt * 16f));
            mm.Pose.Height = h[1];
            mm.Pose.HideShadow = false;
        }

        /// <summary>The takeoff: a ring of dust, its afterimage left on the ground, speed lines up, a whoosh.</summary>
        private static void TakeOff(ChoreoMove mm)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            if (!mm.Vis)
            {
                return;
            }
            Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 8, 34f, 0.45f, mm.Kk);
            Fx.Afterimage(mm, 0.16f, Fx.WithA(mm.Hue.Ghost, 0.75f), mm.Hue.GhostEnd);
            Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.75f));
        }

        /// <summary>The limb's smear from the hip to the target's body ('frac' up it), the body's blur pulled back along it.</summary>
        private static void Streak(ChoreoMove mm, Limb limb, float frac, int head)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            Vector2 from = mm.Root(limb);
            Vector2 to = mm.V != null && mm.V.Sid() == mm.VId ? Vector2.Lerp(mm.ContactPoint(frac), from, 0.12f) : from + mm.Dir * mm.Stand;
            ChoreoFx.Smear(from, to, px, 0.11f, head, 2, mm.Hue.Core, mm.Hue.Edge, FxLayer.Over, 7);
            Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), to - from, px, 2, 2.5f, 0.08f, Fx.WithA(mm.Hue.Edge, 0.75f), FxLayer.Over);
            Fx.Ghost(mm, -mm.Dir * 2f * px, 6f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
        }

        /// <summary>II and III: a ring of wind bursting off the foot, speed lines along the flight it sends the foe on.</summary>
        private static void Gust(ChoreoMove mm, int lv)
        {
            if (lv < 2 || !mm.Vis)
            {
                return;
            }
            float px = mm.Px, k = mm.Kk;
            Vector2 at = mm.Hand(Limb.Foot) + mm.Dir * 2f * px;
            Fx.Ring(at, px, 0.2f, 3f, (lv >= 3 ? 17f : 13f) * k, 2, 1, mm.TopLayer, 6, 0.55f, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(mm.Hue.Edge, 0.55f), Fx.WithA(mm.Hue.Edge, 0f));
            Kit.SpeedLines(at, mm.Dir + Vector2.up * 0.35f, px, 4, 4f * k, 0.13f, Fx.WithA(mm.Hue.Edge, 0.8f));
        }
    }
}
