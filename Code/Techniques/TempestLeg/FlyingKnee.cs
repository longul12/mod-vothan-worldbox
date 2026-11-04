using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// FLYING KNEE (Tempest Leg, Adept). The gap closer: from three to five tiles out it runs at the foe in
    /// real strides (dust at every footfall), plants, and leaps, the knee driving up and forward as it
    /// flies; at the top of the leap the knee meets the chin: the head snaps up in a burst of air, and the
    /// foe is knocked into the air (Launched: Sky Ladder, Wingbreaker and the other air follow-ups take it
    /// from there). It drops back to the ground behind its knee. Good against archers and casters keeping
    /// their distance (the Brain pushes dashes at a ranged foe), and after a throw sent the foe away
    /// (it takes Thrown and Scattered). II: a longer leap, the foe popped higher and hanging there (a juggle
    /// for the chain). III: an impact frame on the knee, the foe sent highest, and it springs off the
    /// foe's chest in a backflip. Never into water, over a wall, or at a giant's chin it can't reach.
    /// Damage (x the user's damage): I 1.4; II 1.9; III 2.6.
    /// </summary>
    internal sealed class FlyingKneeTech : Technique
    {
        public override string Id => "flying_knee";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Mobility;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Dash | Form.Aerial | Form.Knee;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Thrown || link == Link.Scattered;
        }

        private const Form Shows = Form.Dash | Form.Aerial | Form.Knee | Form.Rising;

        public static float Mult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.6f;
        }

        /// <summary>How far it runs and leaps from (tiles, x its size).</summary>
        public static float Range(int level)
        {
            return level <= 1 ? 4.2f : level == 2 ? 4.8f : 5.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float max = Range(level) * k + (c.Linked ? 1f : 0f);
            if (c.Forced)
            {
                max = Mathf.Max(max, 7f);
            }
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, max, ref best, ref bestScore);
            for (int i = 0; i < c.Enemies.Count && i < 8 && c.EnemyDist[i] <= max + 1f; i++)
            {
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], level, max, ref best, ref bestScore);
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
                Score = c.Forced ? Mathf.Max(1f, bestScore) : bestScore,
                Data = c.Forced
            };
        }

        private static void Consider(Ctx c, Actor v, int level, float max, ref Actor best, ref float bestScore)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || !HundredFists.Hopping(v))
            {
                return;
            }
            Vector2 at = v.current_position;
            if (v.isInLiquid() && !Choreo.Wading(at) || !Combat.SafeSpot(at) && !Choreo.Wading(at))
            {
                return;   // it would come down in the water
            }
            float d = c.Dist(v);
            if (d > max + v.stats["size"] * 0.5f || d > 1.2f && !Combat.ClearPath(c.Pos, at))
            {
                return;
            }
            bool link = c.Linked && v == c.LinkTarget;
            float hp = v.getHealth();
            if (!c.Forced && !link && hp <= c.Expected(v, 1.2f) * cm)
            {
                return;   // one ordinary blow ends it: no need to fly at it
            }
            // its knee reaches a man's chin, a bear's jaw: not a giant's
            if (!c.Forced && v.stats["size"] > c.Self.stats["size"] * 3f)
            {
                return;
            }
            float score = c.Value(v, c.Expected(v, Mult(level)), cm);
            // what it is for: closing a gap (more the further it has to go), and shooters keeping their distance
            score += Mathf.Clamp(d - 1.5f, 0f, 3f) * 4f;
            FoeState s = c.State(v);
            if (s.Ranged)
            {
                score += 12f;
            }
            if (link)
            {
                score += 8f;
            }
            if (Tiers.Of(v) != Tier.None)
            {
                score += 6f * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
            }
            if (c.CountNear(at, 2f) >= 3)
            {
                score *= 0.85f;   // it lands in the middle of them
            }
            if (!c.Forced && c.Guard(v) > 0)
            {
                score *= 0.8f;
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || p.Target == null)
            {
                return false;
            }
            int level = m.Level;
            float d = Vector2.Distance(a.current_position, p.Target.current_position);
            if (d > 3.4f * m.Kk)
            {
                // the run: real strides to the takeoff point, a long leap short of the foe
                m.RunUp(mm => mm.StandSpot(2.7f * mm.Kk), 0.08f);
            }
            m.Crouch(0.08f, 0.9f).Shows(Shows);
            Beat leap = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.15f,
                PerTile = 0.055f,
                Route = Route.Arc,
                PathEase = Ease.InOutSine,
                Dest = mm => mm.StandSpot(mm.Stand * 0.85f),
                Apex = level <= 1 ? 0.5f : level == 2 ? 0.62f : 0.75f,
                To = new Shape(-10f, 0.9f, 1.12f, 0.3f),
                Ease = Ease.OutQuad,
                Ghosts = 0.03f,
                Lines = level >= 2 ? 0.035f : 0f,
                NeedsVictim = true,
                Forms = Shows
            };
            leap.Enter = mm =>
            {
                // it comes down with its knee at the foe's chin
                mm.Current.To = new Shape(-10f, 0.9f, 1.12f, Mathf.Clamp(0.32f * mm.VHeight / mm.Kk, 0.18f, 0.9f));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 8, 36f, 0.45f, mm.Kk);
                    Kit.SpeedLines(mm.BodyPos(), (mm.Dir + Vector2.up).normalized, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.75f), mm.TopLayer);
                }
            };
            leap.Every = (mm, k) =>
            {
                // the knee drives up and forward as it flies: tucked, leaning into it
                float tuck = Anim.Smooth(0.45f, 1f, k);
                mm.Pose.Rot = Mathf.Lerp(mm.Pose.Rot, 10f, tuck);
                mm.Pose.SX = Mathf.Lerp(mm.Pose.SX, 1.08f, tuck);
                mm.Pose.SY = Mathf.Lerp(mm.Pose.SY, 0.94f, tuck);
            };
            m.Add(leap);
            m.Strike(new Hit(Blow.Knee, Mult(level))
            {
                Air = true,
                Hurt = level <= 1 ? Hurt.Launch : Hurt.Juggle,
                Lift = level <= 1 ? 1.3f : level == 2 ? 1.6f : 2f,
                Push = level <= 1 ? 0.9f : 0.6f,
                WindScale = 0.35f,
                ActScale = 0.9f,
                Power = level <= 1 ? 1.15f : level == 2 ? 1.25f : 1.45f,
                Finisher = level >= 2,
                Shows = Shows,
                OnLand = (mm, v, r) =>
                {
                    // the chin: the head snaps up in a burst of air
                    Vector2 chin = mm.VPoint(0.86f);
                    TempestKicks.Burst(mm, chin, Vector2.up, mm.Level >= 3 ? 1.5f : 1f);
                    if (mm.Vis)
                    {
                        Fx.HitSpark(chin, mm.Px, mm.Level >= 3 ? 2 : 1, Vector2.up, 3);
                    }
                }
            });
            if (level >= 3)
            {
                // it springs off the foe's chest: a backflip out, landing light
                m.Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.24f,
                    Route = Route.Arc,
                    PathEase = Ease.OutQuad,
                    Dest = mm =>
                    {
                        Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.6f * mm.Kk);
                        return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                    },
                    Apex = 0.3f,
                    Spin = -360f,
                    To = new Shape(float.NaN, 1f, 1f, 0f),
                    Ease = Ease.OutQuad,
                    Ghosts = 0.03f,
                    Track = false,
                    Forms = Form.Aerial | Form.Evasive
                });
                m.Land();
            }
            else
            {
                m.Drop(0.14f, 0f, 6f).Land();
            }
            m.Recover(0.18f);
            return m.Start();
        }
    }
}
