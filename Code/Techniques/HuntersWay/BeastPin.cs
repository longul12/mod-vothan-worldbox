using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BEAST PIN (Hunter's Way, Novice). The hunter drops low like a stalking cat, then pounces: a
    /// flat dive along the ground that takes the beast round the middle; both go down in a burst of
    /// dust, the beast flipped onto its back (a long, side-on beast held belly-up, legs kicking in
    /// the air; an upright one laid flat), and a knee comes down on its chest and holds it there,
    /// trembling. II: two hammer fists rain on the pinned beast. III: the knee grinds down once more
    /// on an impact frame while a hunter's ring of green light closes on the ground round the pair.
    /// Let go, the beast rolls back onto its feet and stands there dazed. Made for beasts (the Brain never uses it on anything
    /// else). A grab: only a body it can hold (no giants, no fliers). Damage (x the user's damage):
    /// I 0.5 + 0.7, II + 2 x 0.35, III + 0.9. Leaves Down.
    /// </summary>
    internal sealed class BeastPinTech : Technique
    {
        public override string Id => "beast_pin";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Grab | Form.Hunt;
        public override Prey Prey => Prey.Beast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 7f, 12f, 18f };
        public override float[] Cooldown => new[] { 5f, 6f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down || link == Link.Thrown;
        }

        private const float Tackle = 0.5f, Knee = 0.7f, Fist = 0.35f, Grind = 0.9f;

        public static float Total(int level)
        {
            return Tackle + Knee + (level >= 2 ? 2f * Fist : 0f) + (level >= 3 ? Grind : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 3f, Total = Total(1), Hold = true, Crowd = 0.1f, Alone = 6f, IntoGuard = 1.2f, Worth = 8f },
            new MeleeWant { Reach = 3.4f, Total = Total(2), Hold = true, Crowd = 0.08f, Alone = 7f, IntoGuard = 1.2f, Worth = 10f },
            new MeleeWant { Reach = 3.8f, Total = Total(3), Hold = true, Crowd = 0.06f, Alone = 8f, IntoGuard = 1.2f, Worth = 12f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = HwLoKit.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // the pounce lands on solid ground only
            if (!Combat.SafeSpot(p.Target.current_position) && !Choreo.Wading(p.Target.current_position))
            {
                return null;
            }
            // a beast already lying there is the easiest to pin
            if (c.State(p.Target).Down)
            {
                p.Score += 8f;
            }
            return p;
        }

        // the knee dropped on a pinned body, the hammer fist, the last grinding knee
        private static readonly BlowShape KneeDrop = new BlowShape
        {
            Limb = Limb.Knee,
            Wind = new Shape(-10f, 0.94f, 1.1f, 0.22f, 0.12f),
            WindDur = 0.1f,
            Hit = new Shape(34f, 1.22f, 0.8f, 0f, 0.34f),
            ActDur = 0.07f,
            FollowDur = 0.08f,
            At = 0.6f,
            Relax = 0.15f,
            Contact = 0.08f,
            ArcR = 5,
            ArcRatio = 0.9f,
            A0 = 110f,
            A1 = -40f,
            ArcThick = 2,
            ArcOff = new Vector2(2f, 0f),
            Weight = 1.05f,
            Hurt = Hurt.Pin,
            Forms = Form.Knee | Form.Overhead | Form.Grab | Form.Hunt
        };

        private static readonly BlowShape HammerFist = new BlowShape
        {
            Limb = Limb.Fist,
            Wind = new Shape(-18f, 0.92f, 1.1f, 0.06f, 0.18f),
            WindDur = 0.08f,
            Hit = new Shape(30f, 1.16f, 0.84f, 0f, 0.32f),
            ActDur = 0.06f,
            FollowDur = 0.06f,
            At = 0.55f,
            Relax = 0.2f,
            Contact = 0.1f,
            ArcR = 7,
            ArcRatio = 0.85f,
            A0 = 120f,
            A1 = -50f,
            ArcThick = 2,
            ArcOff = new Vector2(2f, 2f),
            Weight = 0.9f,
            Hurt = Hurt.Pin,
            Forms = Form.Punch | Form.Overhead | Form.Grab | Form.Hunt
        };

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            HwLoKit.FitStand(m);
            int level = m.Level;
            // the stalk: down low, weight on the back foot
            m.PoseTo(0.12f, new Shape(16f, 1.2f, 0.78f, 0f, -0.08f), Ease.OutQuad, Tempo.Wind).Shows(Form.Grab | Form.Hunt);
            // the pounce: a flat dive at the beast's middle, arms out
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.14f,
                PerTile = 0.045f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.V != null ? mm.StandSpot(mm.Stand * 0.75f) : mm.Stance,
                Apex = 0.32f,
                To = new Shape(52f, 1.18f, 0.84f, 0f, 0.1f),
                Ease = Ease.OutQuad,
                Ghosts = 0.03f,
                Dust = 0.06f,
                NeedsVictim = true,
                Forms = Form.Grab | Form.Aerial | Form.Hunt,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 6, 26f, 0.4f, mm.Kk);
                        Kit.SpeedLines(mm.BodyPos(), mm.Dir, mm.Px, 3, 3f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.7f));
                    }
                },
                Exit = mm => HwLoKit.TakeHold(mm, Tackle * 0.3f)
            });
            // both go down: the beast flipped onto its back under it (a long, side-on beast is held belly-up,
            // legs in the air, under the hunter's knee; anything upright is laid flat and pinned)
            bool wide = HwLoKit.Wide(p.Target);
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.14f,
                To = new Shape(38f, 1.16f, 0.82f, 0f, 0.3f),
                Ease = Ease.InQuad,
                Grip = wide ? new Grip(3.4f, -0.5f, 180f) : new Grip(3.6f, -1.6f, -90f),
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Grab | Form.Throw | Form.Hunt,
                If = mm => mm.Held != null,
                Exit = mm =>
                {
                    if (wide)
                    {
                        HwLoKit.SlamHeld(mm, Tackle * 0.7f, 0f, 0f, 0f, false);
                    }
                    else
                    {
                        HwLoKit.ThrowDown(mm, Tackle * 0.7f, Hurt.Pin, 1.1f, 0f, 0f, 0f, 0f, false, false);
                    }
                }
            });
            m.Hold(0.05f, true, Tempo.Follow);
            // the knee on its chest
            HwLoKit.Strike(m, KneeDrop, new Hit(Blow.Knee, Knee) { Hurt = Hurt.Pin, Daze = level <= 1 ? 1f : 0.6f, Finisher = level == 1, Power = 1.1f });
            if (level >= 2)
            {
                HwLoKit.Strike(m, HammerFist, new Hit(Blow.Body, Fist) { Hurt = Hurt.Pin, Daze = 0.4f });
                HwLoKit.Strike(m, HammerFist, new Hit(Blow.Body, Fist) { Hurt = Hurt.Pin, Daze = level == 2 ? 1.4f : 0.4f, Finisher = level == 2, Power = 1.15f });
            }
            if (level >= 3)
            {
                // the knee grinds down: the hunter's ring closes on the ground round them
                m.Do(mm => HwLoKit.HuntRing(mm, mm.VG, 2.2f, 0.32f));
                HwLoKit.Strike(m, KneeDrop, new Hit(Blow.Knee, Grind) { Hurt = Hurt.Pin, Daze = 2f, Finisher = true, Power = 1.35f, WindScale = 1.4f });
            }
            // it holds the beast down a heartbeat, then springs off it (a beast held belly-up rolls back onto its feet, dazed)
            m.PoseTo(0.16f, new Shape(26f, 1.14f, 0.86f, 0f, 0.28f), Ease.OutQuad, Tempo.Follow).Every((mm, k) => mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.4f));
            if (wide)
            {
                HwLoKit.RollFree(m, level <= 1 ? 1f : level == 2 ? 1.4f : 2f, true);
            }
            m.Leaves(Link.Down);
            m.Recover(0.24f);
            return m.Start();
        }
    }

    /// <summary>
    /// What the first half of the Hunter's Way shares (Beast Pin to Hound Lasso): the melee pick with
    /// its prey gate, blows of the hunter's own shapes (a knee dropped on a pinned beast, a kick at a
    /// giant's knee), a held body thrown down hard, a rope drawn between two points, the hunter's
    /// ring of light closing on the ground.
    /// </summary>
    internal static class HwLoKit
    {
        /// <summary>Natural hemp (a lasso, a sling's cord) and old bone.</summary>
        public static readonly Color Hemp = new Color(0.74f, 0.6f, 0.38f, 1f);
        public static readonly Color HempDark = new Color(0.47f, 0.35f, 0.21f, 1f);
        public static readonly Color Bone = new Color(0.95f, 0.92f, 0.82f, 1f);

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["hwschool"] = args => TeachSchool();
            // hwprey [r]: what kind of creature every unit within r tiles of the performer (or the camera) is
            Dev.Commands["hwprey"] = args =>
            {
                float r = Dev.ArgF(args, 1, 12f);
                Actor me = Dev.Performer;
                Vector2 at = me != null ? (Vector2)me.current_position : (Vector2)Camera.main.transform.position;
                List<Actor> seen = new List<Actor>();
                foreach (Actor u in World.world.units)
                {
                    if (u != null && u.isAlive() && (u.current_position - at).sqrMagnitude <= r * r)
                    {
                        seen.Add(u);
                    }
                }
                foreach (Actor u in seen)
                {
                    Dev.Log("hwprey " + u.asset.id + "#" + u.Sid() + " " + Preys.Name(Preys.Of(u)) + " class " + Weights.Of(u) + " hp " + u.getHealth()
                        + (me != null && u != me ? " foe " + Combat.IsFoe(me, u) + " allowed " + Combat.Allowed(me, u) : ""));
                }
            };
        }

        /// <summary>Dev: 'hwschool': every tiered dev unit on the performer's side walks the Hunter's Way too.</summary>
        private static void TeachSchool()
        {
            Actor me = Dev.Performer;
            ActorTrait trait = Schools.Trait(School.HuntersWay);
            int n = 0;
            foreach (Actor u in Dev.Units)
            {
                if (u == null || !u.isAlive() || Tiers.Of(u) == Tier.None || trait == null || me != null && u != me && me.areFoes(u))
                {
                    continue;
                }
                u.addTrait(trait, true);
                n++;
            }
            Dev.Log("hwschool: " + n + " fighters walk the Hunter's Way");
        }

        /// <summary>Is 'v' a creature 't' is made for (a forced use, a god's order or a demo, takes anyone)?</summary>
        public static bool IsPrey(Ctx c, Technique t, Actor v)
        {
            return v != null && (c.Forced || t.Prey == Prey.None || (Preys.Of(v) & t.Prey) != 0);
        }

        // ------------------------------------------------------------ planning --

        /// <summary>
        /// Choreo.PickMelee for a hunter's technique: the same checks and value (fair, reach, a clear
        /// path, holdable for grabs, worth it, alone or mobbed, its guard), but only creatures the
        /// technique is made for are considered (unless forced), so a beast behind a man is still found.
        /// </summary>
        public static Plan PickMelee(Ctx c, Technique t, int level, MeleeWant w)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == t)
            {
                return null;
            }
            Actor best = null;
            float bestScore = 0f;
            Consider(c, t, c.Focus, w, ref best, ref bestScore);
            float k = Choreo.KOf(c.Self);
            float reach = w.Reach * k + 1f;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(reach, 6f) : reach))
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, t, c.Enemies[i], w, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        private static void Consider(Ctx c, Technique t, Actor v, MeleeWant w, ref Actor best, ref float bestScore)
        {
            if (v == null || !IsPrey(c, t, v) || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
            {
                return;
            }
            bool airborne = !Combat.Grounded(v);
            if (w.Air != airborne && !(w.Air && c.Forced))
            {
                return;
            }
            if (!w.Air && (v.isInLiquid() && !Choreo.Wading(v.current_position) || !HundredFists.Hopping(v)))
            {
                return;
            }
            if (w.Hold && !Choreo.Holdable(v, c.Self))
            {
                return;
            }
            float k = Choreo.KOf(c.Self);
            bool linkTarget = c.Linked && v == c.LinkTarget;
            float reach = w.Reach * k + v.stats["size"] * 0.5f + (linkTarget ? 0.8f : 0f);
            if (c.Forced)
            {
                reach = Mathf.Max(reach, 6f);
            }
            float d = c.Dist(v);
            if (d > reach || d > 1.2f && !w.Air && !Combat.ClearPath(c.Pos, v.current_position))
            {
                return;
            }
            float hp = v.getHealth();
            if (!c.Forced && w.Worthy)
            {
                if (hp <= c.Expected(v, 1.2f) * cm)
                {
                    return;
                }
                if (!linkTarget && Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute && hp <= c.Expected(v, 1.6f) * cm)
                {
                    return;
                }
            }
            float score = c.Value(v, c.Expected(v, w.Total), cm);
            if (c.Isolated(v, 2f))
            {
                score += w.Alone;
            }
            else if (w.Crowd > 0f)
            {
                int near = c.CountNear(v.current_position, 2f);
                if (near >= 3)
                {
                    score *= Mathf.Max(0.5f, 1f - w.Crowd * (near - 2));
                }
            }
            if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Fiend)
            {
                score += w.Worth * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
            }
            if (linkTarget)
            {
                score += 6f;
            }
            if (!c.Forced)
            {
                int guard = c.Guard(v);
                if (guard > 0)
                {
                    score *= w.IntoGuard;
                }
                else if (guard < 0)
                {
                    score += 10f;
                }
            }
            else
            {
                score = Mathf.Max(score, 1f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        /// <summary>
        /// The best target for a ranged hunter's technique (a sling, a lasso): a fair creature it is
        /// made for, between 'min' and 'max' tiles (x size), the air included; 'value' (a cached
        /// delegate: no garbage per look) scores it from (ctx, target, its class share, level).
        /// </summary>
        public static Actor PickRanged(Ctx c, Technique t, int level, float min, float max, System.Func<Ctx, Actor, float, int, float> value, out float score)
        {
            score = 0f;
            Actor best = null;
            float k = Choreo.KOf(c.Self);
            min *= Mathf.Lerp(1f, k, 0.5f);
            max *= Mathf.Lerp(1f, k, 0.5f);
            if (c.Forced)
            {
                max = Mathf.Max(max, 12f);
            }
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                Actor v = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > max)
                {
                    break;
                }
                if (c.ForceTarget != null && v != c.ForceTarget)
                {
                    continue;
                }
                if (d < min && !c.Forced || !IsPrey(c, t, v) || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                float s = value(c, v, cm, level);
                if (c.Forced)
                {
                    s = Mathf.Max(s, 1f + 1f / (1f + d));
                }
                if (s > score)
                {
                    score = s;
                    best = v;
                }
            }
            return best;
        }

        // ------------------------------------------------------- custom blows --

        /// <summary>
        /// A blow of the hunter's own shape 's' (its limb, poses, timing, the height it lands at), played
        /// the way the engine plays a Strike: a wind-up that steps after a target that slid away, the
        /// blow (the body snaps into the hit pose, the limb's smear; its first contact goes through
        /// Combat.Reach so an answer can meet it), the follow-through (held longer on the last blow).
        /// </summary>
        public static ChoreoMove Strike(ChoreoMove m, BlowShape s, Hit h)
        {
            Form shows = h.Shows != Form.None ? h.Shows : s.Forms;
            Beat wind = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = s.WindDur * h.WindScale,
                To = s.Wind,
                Ease = Ease.OutQuad,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.Held != null ? mm.Stance : mm.ApproachSpot(),
                PerTile = 0.07f,
                Chase = true,
                NeedsVictim = true,
                Forms = shows,
                Charging = h.Charged,
                RelH = h.Air,
                Dust = 0.07f
            };
            wind.Enter = mm =>
            {
                if (h.Air)
                {
                    mm.AirBase = mm.Pose.Height;
                }
                if (mm.V == null || mm.Held != null)
                {
                    return;   // (a body in its hands is struck where it holds it)
                }
                float d = Vector2.Distance(mm.Stance, mm.ApproachSpot());
                if (d > 2.6f * mm.Kk + (mm.Forced ? 3f : 0f))
                {
                    mm.Lose();   // it got away
                    return;
                }
                Combat.Pin(mm.V, mm.BeatDur + 0.3f);
            };
            if (h.Charged)
            {
                wind.Every = (mm, k) =>
                {
                    if (k > 0.55f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, k > 0.85f ? 0.8f : 0.5f);
                    }
                };
            }
            if (h.Finisher)
            {
                wind.On(0.75f, mm =>
                {
                    if (mm.Vis && mm.Level >= 2)
                    {
                        Fx.Glint(mm.Hand(s.Limb) + new Vector2(0f, 1.5f * mm.Px), mm.Px, 0.14f, mm.Hue.Core, 0.8f);
                    }
                });
            }
            m.Add(wind);
            Beat act = new Beat { Tempo = Tempo.Act, Dur = s.ActDur * h.ActScale, To = s.Hit, Ease = Ease.OutExpo, Forms = shows, Tag = h, RelH = h.Air };
            act.Enter = mm => SmearOf(mm, s);
            act.On(s.At, mm => LandOf(mm, h, s));
            m.Add(act);
            Shape relax = new Shape(s.Hit.Rot * (1f - s.Relax), Mathf.Lerp(s.Hit.SX, 1f, s.Relax), Mathf.Lerp(s.Hit.SY, 1f, s.Relax), h.Air ? float.NaN : 0f, s.Hit.Lunge * 0.85f);
            Beat follow = new Beat { Tempo = Tempo.Follow, Dur = s.FollowDur, To = relax, Ease = Ease.OutQuad, Forms = shows, Track = false, RelH = h.Air };
            follow.Enter = mm =>
            {
                if (mm.IsFinal(h))
                {
                    mm.BeatDur *= 1.7f;   // the last blow's pose is held: it reads
                }
            };
            return m.Add(follow);
        }

        private static void SmearOf(ChoreoMove m, BlowShape s)
        {
            if (m.V != null && m.VOk)
            {
                Combat.Pin(m.V, m.BeatDur + 0.3f);
            }
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            float life = m.BeatDur * 1.6f + 0.03f;
            if (s.ArcR > 0)
            {
                float offY = s.ArcOff.y < -50f ? -m.Pose.PivotUp + 1.5f : s.ArcOff.y;
                int r = Mathf.Clamp(Mathf.RoundToInt(s.ArcR * Mathf.Lerp(1f, m.Kk, 0.6f) * (m.Level >= 3 ? 1.15f : 1f)), 4, 60);
                int thick = s.ArcThick + (m.Level >= 3 && s.ArcThick < 4 ? 1 : 0);
                Kit.Swish(m, Art.Arc(r, s.ArcRatio, s.A0, s.A1, thick), life, new Vector2(s.ArcOff.x * m.Fwd, offY), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
            }
            if (s.Straight)
            {
                Vector2 from = m.Root(s.Limb);
                Vector2 to = m.V != null ? Vector2.Lerp(m.ContactPoint(s.Contact), from, 0.15f) : from + m.Dir * m.Stand;
                ChoreoFx.Smear(from, to, px, life, Mathf.Min(s.SmearHead + (m.Level >= 3 ? 1 : 0), 3), s.SmearThick, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
                if (s.Weight >= 1f)
                {
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), m.Dir, px, 2, 2.5f, 0.08f, Fx.WithA(m.Hue.Edge, 0.75f), FxLayer.Over);
                }
            }
            Fx.Ghost(m, -m.Dir * 2f * px, -6f, 0.07f, Fx.WithA(m.Hue.Ghost, 0.55f), m.Hue.GhostEnd);
            if (s.Heavy || s.Weight >= 1.2f)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
            }
        }

        private static void LandOf(ChoreoMove m, Hit h, BlowShape s)
        {
            if (!m.VOk)
            {
                Whiff(m, s.Limb);
                if (!m.Retarget())
                {
                    m.Lose();
                }
                return;
            }
            Actor v = m.V;
            float reach = m.Stand + 0.6f * m.Kk + v.stats["size"] * 0.5f + 0.3f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                Whiff(m, s.Limb);   // out of reach: this one cuts the air, the next steps after it
                return;
            }
            if (!m.Touch(v))
            {
                return;
            }
            m.Deliver(v, h, s, m.ContactPoint(s.Contact), m.Dir);
        }

        /// <summary>A blow that meets nothing: a white swish through the air and a whoosh.</summary>
        public static void Whiff(ChoreoMove m, Limb limb)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), m.Hand(limb) + new Vector2(m.Fwd * 2f * m.Px, 0f), m.Px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = m.Fwd;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        // ------------------------------------------------------- held bodies --

        /// <summary>
        /// Takes hold of the target at the end of a pounce or a lunge (its answer comes first: Touch).
        /// A body it can't lift is shoved down instead and the move ends. True if it holds it now.
        /// </summary>
        public static bool TakeHold(ChoreoMove m, float mult)
        {
            if (!m.VOk)
            {
                Whiff(m, Limb.Palm);
                m.Lose();
                return false;
            }
            Actor v = m.V;
            float reach = m.Stand + 0.7f * m.Kk + v.stats["size"] * 0.5f + 0.35f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                Whiff(m, Limb.Palm);
                m.Lose();
                return false;
            }
            if (!m.Touch(v))
            {
                return false;
            }
            Victim b = Victims.Grab(v, m);
            if (b == null)
            {
                // too heavy, flying, someone else's: it is bowled over instead
                m.Deliver(v, new Hit(Blow.Palm, mult + 0.3f) { Hurt = Hurt.Knockdown }, ChoreoMove.ShapeOf(Blow.Palm), m.VPoint(0.5f), m.Dir);
                m.Lose();
                return false;
            }
            m.Held = b;
            if (mult > 0f)
            {
                m.LastHit = Combat.Hit(m.A, v, mult * m.Cm, false, false, 0f, m);
            }
            b.P.Flash(0.06f);
            Combat.Pin(v, 3f);
            Choreo.Sound("event:/SFX/OTHER/Punch", m.A.current_tile);
            if (m.Vis)
            {
                Vector2 at = m.VPoint(0.55f);
                Fx.HitSpark(at, m.Px, 0, -m.Dir, 3);
                Fx.FocusLines(at, m.Px, Fx.Count(6), 9f * m.Kk, 16f * m.Kk, 0.1f);
            }
            m.HoldFor(0.04f * m.ImpactK);
            if (Dev.On) Dev.Trace("hunt " + m.Id + " takes hold of " + v.asset.id + "#" + v.Sid());
            return true;
        }

        /// <summary>
        /// The held body is thrown down where the hands put it (a tackle, a flip, the end of a hammer
        /// swing): 'mult' damage (a body worn to the edge dies of it only on the script's last blow:
        /// 'last'), it reacts 'hurt' (Pin: held flat 'hold' s), dazed 'daze' s once let go; 'crater'
        /// breaks the ground at the move's Scale, 'area' knocks the thrower's enemies near down
        /// (x 'areaMult' of the blow), 'heavy' adds an impact frame (and a world stop for the mighty).
        /// </summary>
        public static void ThrowDown(ChoreoMove m, float mult, Hurt hurt, float hold, float daze, float crater, float area, float areaMult, bool heavy, bool last)
        {
            Victim b = m.Held;
            if (b == null)
            {
                return;
            }
            Actor v = b.A;
            Vector2 at = b.P.Pose.Ground;
            m.Held = null;
            Impact im = Impact.Of(hurt, m.Dir, 0f, heavy ? 1.6f : 1.2f);
            im.Hold = hold;
            im.Daze = daze;
            im.Stop = (heavy ? 0.05f : 0.03f) * m.ImpactK;
            HitResult r = Crash(m, v, at, mult, crater, area, areaMult, heavy);
            im.Owed = last && MeteorSlam.AtDeathsDoor(v);
            b.LetGo(im, m.A, m);
            if (Dev.On) Dev.Trace("hunt " + m.Id + " throws " + v.asset.id + "#" + v.Sid() + " down x" + (mult * m.Cm).ToString("F2") + " hp " + v.getHealth() + " " + hurt + (r.Killed ? " killed" : ""));
        }

        /// <summary>
        /// The held body is slammed down where the hands put it but kept in hand (a beast held belly-up
        /// under the hunter): the same blow and impact as ThrowDown, no reaction yet (RollFree lets it go).
        /// </summary>
        public static void SlamHeld(ChoreoMove m, float mult, float crater, float area, float areaMult, bool heavy)
        {
            Victim b = m.Held;
            if (b == null)
            {
                return;
            }
            b.P.Flash(0.07f);
            HitResult r = Crash(m, b.A, b.P.Pose.Ground, mult, crater, area, areaMult, heavy);
            if (Dev.On) Dev.Trace("hunt " + m.Id + " slams " + b.A.asset.id + "#" + b.A.Sid() + " (held) x" + (mult * m.Cm).ToString("F2") + " hp " + b.A.getHealth() + (r.Killed ? " killed" : ""));
        }

        /// <summary>The blow and the ground's answer of a body slammed down at 'at' (never lethal: the caller decides who dies).</summary>
        private static HitResult Crash(ChoreoMove m, Actor v, Vector2 at, float mult, float crater, float area, float areaMult, bool heavy)
        {
            WorldTile tile = Combat.TileAt(at);
            HitResult r = Combat.Hit(m.A, v, mult * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            Victims.Flash(v, Color.white, 0.06f);
            m.HoldFor((heavy ? 0.06f : 0.035f) * m.ImpactK);
            float weight = (heavy ? 1.5f : 1f) + 0.15f * m.Level;
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 1f * m.Px);
                if (heavy)
                {
                    Kit.ImpactFrame(hit, m.Px, (0.22f + 0.06f * m.Level) * m.Kk, m.Hue.Pal, r.Crit, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
                ChoreoFx.Impact(hit, Vector2.down, m.Px, m.Kk, weight, m.Hue, m.TopLayer, heavy);
                Kit.DustRing(at, m.Px, tile, heavy ? 12 : 8, heavy ? 42f : 30f, 0.5f, Mathf.Sqrt(m.Kk));
                if (heavy)
                {
                    Kit.Debris(at, m.Px, tile, 6, m.Kk);
                }
            }
            Choreo.Sound(heavy ? "event:/SFX/DESTRUCTION/DropSimpleImpact" : "event:/SFX/HIT/HitGeneric", tile);
            if (crater > 0f)
            {
                if (m.Vis)
                {
                    Kit.Crater(at, m.Px * m.Kk, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 6f, crater < 1.2f, m.Hue.Edge);
                }
                Ruin.Blast(m.A, at, crater, m.Scale, m.Pose.Ground, false);   // (a body slammed down breaks the ground; it sets nothing alight)
            }
            if (heavy && (m.Tier >= Tier.Master || m.Scale >= Scale.Heavy && m.Level >= 2))
            {
                Kit.Stop(at, (0.03f + 0.015f * m.Level) * m.ImpactK);
            }
            Ruin.Quake(m.A, at, m.Scale, weight, m.Kk);
            if (area > 0f)
            {
                m.AreaBlow(at, area, areaMult * mult, v, Hurt.Knockdown, 0.6f);
            }
            m.SetLink(Link.Down, v);
            return r;
        }

        /// <summary>
        /// A beast held belly-up is let go: it rolls the rest of the way over onto its feet as the hunter
        /// springs off it, and stands there dazed 'daze' s (a body worn to the edge dies of it: 'last').
        /// Nothing if it holds nothing.
        /// </summary>
        public static ChoreoMove RollFree(ChoreoMove m, float daze, bool last)
        {
            return m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.15f,
                To = new Shape(-10f, 0.96f, 1.06f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Grip = new Grip(4.6f, -0.2f, 360f),
                Track = false,
                Forms = Form.Grab | Form.Throw | Form.Hunt,
                If = mm => mm.Held != null,
                Exit = mm =>
                {
                    Victim b = mm.Held;
                    if (b == null)
                    {
                        return;
                    }
                    mm.Held = null;
                    Impact im = Impact.Of(Hurt.Flinch, mm.Dir, 0.25f, 0.8f);
                    im.Daze = daze;
                    im.Owed = last && MeteorSlam.AtDeathsDoor(b.A);
                    b.LetGo(im, mm.A, mm);
                    mm.SetLink(Link.Down, b.A);
                    if (mm.Vis)
                    {
                        Kit.DustRing(b.P.Pose.Ground, mm.Px, mm.A.current_tile, 6, 26f, 0.4f, mm.Kk);
                    }
                }
            });
        }

        /// <summary>A side-on creature wider than it is tall (a wolf, a bear): held belly-up, not lying across.</summary>
        public static bool Wide(Actor v)
        {
            Sprite s = v != null ? Fx.UnitSprite(v) : null;
            return s != null && s.rect.width > s.rect.height * 1.15f;
        }

        /// <summary>
        /// Keeps the blows outside a long creature's body: the engine's striking distance comes from the
        /// heights of the two, so a bear side-on would be struck from inside its sprite.
        /// </summary>
        public static void FitStand(ChoreoMove m)
        {
            if (m == null || m.V == null)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(m.V);
            if (s == null)
            {
                return;
            }
            float half = s.rect.width * 0.5f * m.VPx;
            m.Stand = Mathf.Max(m.Stand, half + 0.24f * m.Kk);
        }

        // ------------------------------------------------------------- effects --

        /// <summary>The hunter's ring: a flat ring of the school's green closing on 'at', glints rising at its rim.</summary>
        public static void HuntRing(ChoreoMove m, Vector2 at, float rTiles, float life)
        {
            if (!m.Vis)
            {
                return;
            }
            float r = rTiles * m.Kk / m.Px;
            Fx.Ring(at, m.Px, life, r, r * 0.25f, 2, 1, FxLayer.Ground, 6, Art.Ground, 1.6f, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            int n = Fx.Count(6);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f + Random.value * 0.4f;
                Vector2 p = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r * 0.8f * m.Px;
                Particle q = Fx.Spawn(Art.Pixel, p, m.Px, Random.Range(0.3f, 0.5f), FxLayer.Top, 9);
                q?.Moving(new Vector2(0f, Random.Range(10f, 18f) * m.Px), 0f, 2f).Sized(1.5f, 1f).Color(m.Hue.Core, Fx.WithA(m.Hue.Edge, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
            }
        }

        /// <summary>Shards (bone, stone, a cut shaft) bursting from 'at' along 'dir' and falling.</summary>
        public static void Shards(Vector2 at, Vector2 dir, float px, int n, float speed, Color c0, Color c1, float life = 0.5f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Choreo.Rotate(dir, Random.Range(-70f, 70f));
                Particle p = Fx.Spawn(Art.Pixel, at + Random.insideUnitCircle * 1.5f * px, px, life * Random.Range(0.7f, 1.2f), FxLayer.Top, 11);
                if (p == null)
                {
                    break;
                }
                p.SX = Random.Range(1.5f, 3f);
                p.SY = 1f;
                p.Rot = Random.Range(0f, 180f);
                p.Spin = Random.Range(-720f, 720f);
                p.Moving(d * speed * Random.Range(0.6f, 1.2f) * px, Random.Range(8f, 20f) * px, 1.5f, 70f * px);
                p.Bounce = 0.3f;
                p.Color(c0, c1, Fx.WithA(c1, 0f));
            }
        }
    }

    /// <summary>
    /// A rope between two points (a lasso's line, a sling's cord): a chain of stretched pixels that
    /// sags in the middle, drawn each frame by its move, glued to it (it vanishes with the move).
    /// </summary>
    internal sealed class HwLoRope
    {
        private const int Max = 14;
        private readonly Particle[] _seg = new Particle[Max];
        private readonly int[] _gen = new int[Max];

        public void Draw(ChoreoMove m, Vector2 a, Vector2 b, float sag, Color c, int n = 10)
        {
            if (!m.Vis)
            {
                Hide(m);
                return;
            }
            n = Mathf.Clamp(n, 2, Max);
            Vector2 prev = a;
            for (int i = 0; i < Max; i++)
            {
                Particle p = _seg[i];
                bool ok = p != null && p.Alive && p.Gen == _gen[i];
                if (i >= n)
                {
                    if (ok)
                    {
                        m.Unglue(p);
                    }
                    _seg[i] = null;
                    continue;
                }
                float t = (i + 1) / (float)n;
                Vector2 q = Vector2.Lerp(a, b, t) + Vector2.down * (sag * 4f * t * (1f - t));
                if (!ok)
                {
                    p = Fx.Spawn(Art.Pixel, q, m.Px, 30f, FxLayer.Over, 8);
                    if (p == null)
                    {
                        _seg[i] = null;
                        break;
                    }
                    p.Held = true;
                    m.Glue(p);
                    _seg[i] = p;
                    _gen[i] = p.Gen;
                }
                Vector2 d = q - prev;
                p.Pos = (prev + q) * 0.5f;
                p.SX = Mathf.Max(1f, d.magnitude / m.Px + 0.6f);
                p.SY = 1f;
                p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                p.Color(c);
                prev = q;
            }
        }

        public void Hide(ChoreoMove m)
        {
            for (int i = 0; i < Max; i++)
            {
                Particle p = _seg[i];
                if (p != null && p.Alive && p.Gen == _gen[i])
                {
                    m.Unglue(p);
                }
                _seg[i] = null;
            }
        }
    }
}
