using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>What a melee technique wants of its target (Choreo.PickMelee).</summary>
    internal sealed class MeleeWant
    {
        /// <summary>How far it closes in to start (tiles, x the user's size; a link target a little further).</summary>
        public float Reach = 2.2f;
        /// <summary>Its whole damage on the target (x the user's damage), for the value of the plan.</summary>
        public float Total = 1.5f;
        /// <summary>It lifts the target (grabs, throws): only bodies it can hold (Choreo.Holdable).</summary>
        public bool Hold;
        /// <summary>It works on airborne targets (air follow-ups), not grounded ones.</summary>
        public bool Air;
        /// <summary>
        /// Not wasted on a foe one ordinary blow ends, nor (unless linked or forced) on a plain foot
        /// soldier a couple of blows end: set false for bread-and-butter techniques.
        /// </summary>
        public bool Worthy = true;
        /// <summary>In a crowd (3+ foes within 2 tiles of the target) its score shrinks by this much each (a grab gets mobbed).</summary>
        public float Crowd = 0.08f;
        /// <summary>Bonus when the target stands alone (an execution).</summary>
        public float Alone = 8f;
        /// <summary>Into a ready guard (a reactive technique up) the score is multiplied by this.</summary>
        public float IntoGuard = 0.7f;
        /// <summary>Extra score (scaled to the target's power) for a worthy foe: tiered, or Fiend class and up.</summary>
        public float Worth = 10f;
    }

    internal static partial class Choreo
    {
        // ---------------------------------------------------------- small helpers --

        /// <summary>Does it carry a weapon (its blade cuts are steel, not ki)?</summary>
        public static bool ArmedOf(Actor a)
        {
            try
            {
                return a != null && a.hasWeapon();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Shallow water a Master fights in (Moves.Wades).</summary>
        public static bool Wading(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t != null && t.Type != null && !t.Type.lava && !t.Type.block && t.Type.id != null && t.Type.id.Contains("shallow");
        }

        /// <summary>Fights with techniques and flow strikes only (no attack cooldown after a move): Masters and up.</summary>
        public static bool Flowing(Tier t)
        {
            return SingleKit.Flow(t);
        }

        /// <summary>
        /// Can 'by' take hold of 'v' (grabs, throws): not too heavy, too big, flying or held by another?
        /// One of the engine's own reactions (even up in the air) can be taken out of it; a unit
        /// gathering power can be taken out of its charge (the hold disperses it).
        /// </summary>
        public static bool Holdable(Actor v, Actor by)
        {
            if (v == null || by == null)
            {
                return false;
            }
            bool ours = Victims.Ours(v);
            if (!Victims.Movable(v, by, ours) || !Free(v) && !Moves.Charging(v))
            {
                return false;
            }
            return ours ? Combat.Living(v) && !v.isFlying() : HundredFists.Catchable(v, by);
        }

        /// <summary>The tier's tempo (TierInfo.Linger) as a beat multiplier: what a Wind beat lasts at this tier per second authored.</summary>
        public static float TempoOf(Tier t)
        {
            TierInfo i = Tiers.Info(t);
            return Mathf.Sqrt(Mathf.Clamp(i != null ? i.Linger : 1f, 0.3f, 1f));
        }

        // -------------------------------------------------------------- planning --

        /// <summary>
        /// The best target for a melee technique right now, or null: its focus (a link target, the
        /// forced target, its own) and the nearest few in reach, each checked the way the mod's own
        /// techniques do (fair: alive, hostile, allowed by the class limits, not held, not doomed, not
        /// in someone's set piece; on the ground or in the air as asked; a clear path; holdable for
        /// grabs; not a waste) and scored by its value (damage that counts, scaled by the foe's danger),
        /// alone or in a crowd, worthy or not, guard up or just spent. A forced plan (a god power, a dev
        /// command) reaches further and skips the "is it worth it" gates.
        /// </summary>
        public static Plan PickMelee(Ctx c, Technique t, int level, MeleeWant w)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == t)
            {
                return null;   // never the same one straight into itself
            }
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, w, ref best, ref bestScore);
            if (c.Forced && c.ForceTarget != null && best == c.ForceTarget)
            {
                return MeleePlan(c, best, bestScore);   // (a god power or a dev command names its target: that one, if it will do at all)
            }
            float k = KOf(c.Self);
            float reach = w.Reach * k + 1f;
            for (int i = 0; i < c.Enemies.Count && i < 6; i++)
            {
                if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(reach, 6f) : reach))
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], level, w, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            return MeleePlan(c, best, bestScore);
        }

        private static Plan MeleePlan(Ctx c, Actor best, float score)
        {
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = score,
                Data = c.Forced
            };
        }

        private static void Consider(Ctx c, Actor v, int level, MeleeWant w, ref Actor best, ref float bestScore)
        {
            if (v == null || !Fair(c.Self, v, out float cm, c.Forced))
            {
                return;
            }
            bool airborne = !Combat.Grounded(v);
            if (w.Air != airborne && !(w.Air && c.Forced))
            {
                return;
            }
            if (!w.Air && (v.isInLiquid() && !Wading(v.current_position) || !HundredFists.Hopping(v)))
            {
                return;
            }
            if (w.Hold && !Holdable(v, c.Self))
            {
                return;
            }
            float k = KOf(c.Self);
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
                    return;   // one ordinary blow ends it
                }
                if (!linkTarget && Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute && hp <= c.Expected(v, 2.2f) * cm)
                {
                    return;   // a plain foot soldier: not worth this
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
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        private static readonly List<Actor> _ray = new List<Actor>();

        /// <summary>
        /// The best line for a line technique (a beam, a lance, a dash through): towards one of the enemies
        /// in range, the one that lines up the most value ('len' tiles long, 'width' tiles each side),
        /// at least 'need' of them (fewer when linked), no wall in the first tiles. Score: the value of all
        /// it catches (x 'total' damage each). A line on a lone weak foe is never worth it unless forced.
        /// </summary>
        public static Plan PickLine(Ctx c, Technique t, int level, float len, float width, float total, int need = 1, bool worthy = true)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = KOf(c.Self);
            len *= Mathf.Lerp(1f, k, 0.5f);
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                Actor aim = c.Enemies[i];
                if (c.EnemyDist[i] > len || c.EnemyDist[i] < 0.6f || !Fair(c.Self, aim, out _, c.Forced))
                {
                    continue;
                }
                if (c.ForceTarget != null && aim != c.ForceTarget)
                {
                    continue;
                }
                Vector2 dir = (aim.current_position - c.Pos).normalized;
                // a wall or a mountain right in front blocks it
                if (!Combat.ClearPath(c.Pos, c.Pos + dir * Mathf.Min(2.5f, c.EnemyDist[i]), true))
                {
                    continue;
                }
                _ray.Clear();
                int n = c.OnRay(dir, len, width, _ray);
                if (!_ray.Contains(aim))
                {
                    _ray.Add(aim);
                    n++;
                }
                int needed = c.Linked ? Mathf.Max(1, need - 1) : need;
                if (n < needed && !c.Forced)
                {
                    continue;
                }
                float score = 0f;
                bool worth = false;
                for (int j = 0; j < _ray.Count; j++)
                {
                    Actor v = _ray[j];
                    c.Allowed(v, out float cm);
                    score += c.Value(v, c.Expected(v, total), cm);
                    if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2.5f) * cm)
                    {
                        worth = true;
                    }
                }
                if (worthy && !worth && n < 3 && !c.Forced)
                {
                    continue;   // a beam is not for one or two peasants
                }
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = aim, Point = aim.current_position, Dir = dir, Count = n, Score = score, Data = c.Forced };
                }
            }
            return best;
        }

        /// <summary>
        /// The best centre for an area technique: the densest group (radius 'radius') within 'range' tiles,
        /// at least 'need' foes (fewer when linked). Score: the value of all of them (x 'total' each).
        /// </summary>
        public static Plan PickArea(Ctx c, Technique t, int level, float radius, float range, float total, int need = 2)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            Vector2 at = c.Cluster(radius, 0f, range, out int count, out Actor center);
            int needed = c.Linked ? Mathf.Max(1, need - 1) : need;
            if (center == null || count < needed && !c.Forced)
            {
                return null;
            }
            _ray.Clear();
            c.CountNear(at, radius, _ray);
            float score = 0f;
            for (int j = 0; j < _ray.Count; j++)
            {
                Actor v = _ray[j];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, total), cm);
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = at - c.Pos;
            return new Plan
            {
                Target = center,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = count,
                Score = score,
                AreaRadius = radius,
                Data = c.Forced
            };
        }
    }
}
