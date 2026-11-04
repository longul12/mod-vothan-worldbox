using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Shared control rules for both WorldBox statuses/force and Sakuga body reactions.
    /// A higher realm ignores lower-realm control until five enemies focus it at close range.
    /// </summary>
    internal static class RealmControl
    {
        internal const int BodyBreakAttackers = 5;
        private const float BodyBreakRadius = 8f;
        private const float CountCacheSeconds = 0.45f;
        private const float RecentHitSeconds = 0.6f;

        private sealed class Crowd
        {
            public Actor Defender;
            public float Until;
            public int Count;
        }

        private sealed class Source
        {
            public Actor Attacker;
            public long AttackerId;
            public float Until;
        }

        private static readonly Dictionary<long, Crowd> _crowds = new Dictionary<long, Crowd>();
        private static readonly Dictionary<long, Source> _sources = new Dictionary<long, Source>();
        private static readonly Dictionary<long, float> _breakFx = new Dictionary<long, float>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            _crowds.Clear();
            _sources.Clear();
            _breakFx.Clear();
        }

        /// <summary>Primordials sit one control rank above Gods.</summary>
        internal static int Rank(Actor a)
        {
            if (a == null)
            {
                return 0;
            }
            return Primordials.Any && Primordials.Is(a) ? Tiers.Count + 1 : (int)Tiers.Of(a);
        }

        internal static bool IsLower(Actor attacker, Actor defender)
        {
            int defenderRank = Rank(defender);
            return defenderRank > 0 && attacker != null && Rank(attacker) < defenderRank;
        }

        /// <summary>The attacker stands at a higher realm and therefore bypasses stun resistance.</summary>
        internal static bool IsHigher(Actor attacker, Actor defender)
        {
            int defenderRank = Rank(defender);
            return defenderRank > 0 && attacker != null && Rank(attacker) > defenderRank;
        }

        /// <summary>Remember who caused a hit for the force/stun calls that immediately follow it.</summary>
        internal static void RecordHit(Actor defender, BaseSimObject source)
        {
            if (defender == null || source == null || !source.isActor() || source.a == null || source.a == defender)
            {
                return;
            }
            long id = defender.Sid();
            Actor attacker = source.a;
            long attackerId = attacker.Sid();
            if (id < 0 || attackerId < 0)
            {
                return;
            }
            if (_sources.Count > 1024)
            {
                _sources.Clear();
            }
            _sources[id] = new Source
            {
                Attacker = attacker,
                AttackerId = attackerId,
                Until = Time.unscaledTime + RecentHitSeconds
            };
        }

        internal static Actor RecentAttacker(Actor defender)
        {
            if (defender == null || !_sources.TryGetValue(defender.Sid(), out Source source))
            {
                return null;
            }
            Actor attacker = source.Attacker;
            if (Time.unscaledTime > source.Until || attacker == null || attacker.Sid() != source.AttackerId || !attacker.isAlive())
            {
                _sources.Remove(defender.Sid());
                return null;
            }
            return attacker;
        }

        /// <summary>
        /// True when lower-realm control must be suppressed. Body Break at five attackers opens
        /// the guard, so the same control attempt is allowed through.
        /// </summary>
        internal static bool BlocksLower(Actor defender, Actor attacker = null)
        {
            if (attacker == null)
            {
                attacker = RecentAttacker(defender);
            }
            if (!IsLower(attacker, defender))
            {
                return false;
            }
            if (Breached(defender, attacker))
            {
                BodyBreakFx(defender);
                return false;
            }
            return true;
        }

        /// <summary>At least five nearby hostile units are genuinely focused on this defender.</summary>
        internal static bool Breached(Actor defender, Actor immediate = null)
        {
            if (defender == null || !defender.isAlive() || defender.current_tile == null || defender.kingdom == null)
            {
                return false;
            }
            long id = defender.Sid();
            float now = Time.unscaledTime;
            if (!_crowds.TryGetValue(id, out Crowd cached) || cached.Defender != defender || now >= cached.Until)
            {
                if (_crowds.Count > 512)
                {
                    _crowds.Clear();
                }
                cached = new Crowd { Defender = defender, Until = now + CountCacheSeconds, Count = CountAttackers(defender) };
                _crowds[id] = cached;
            }
            if (cached.Count >= BodyBreakAttackers)
            {
                return true;
            }
            return cached.Count == BodyBreakAttackers - 1 && ImmediateAddsOne(defender, immediate);
        }

        private static int CountAttackers(Actor defender)
        {
            int count = 0;
            float radiusSquared = BodyBreakRadius * BodyBreakRadius;
            foreach (Actor unit in Finder.getUnitsFromChunk(defender.current_tile, 1))
            {
                if (unit == null || unit == defender || !Combat.IsFoe(defender, unit)
                    || (unit.current_position - defender.current_position).sqrMagnitude > radiusSquared)
                {
                    continue;
                }
                BaseSimObject target = unit.attack_target;
                if (unit.has_attack_target && target != null && target.isActor() && target.a == defender)
                {
                    count++;
                    if (count >= BodyBreakAttackers)
                    {
                        break;
                    }
                }
            }
            return count;
        }

        private static bool ImmediateAddsOne(Actor defender, Actor attacker)
        {
            if (attacker == null || !Combat.IsFoe(defender, attacker)
                || (attacker.current_position - defender.current_position).sqrMagnitude > BodyBreakRadius * BodyBreakRadius)
            {
                return false;
            }
            BaseSimObject target = attacker.attack_target;
            return !attacker.has_attack_target || target == null || !target.isActor() || target.a != defender;
        }

        private static void BodyBreakFx(Actor defender)
        {
            long id = defender.Sid();
            float now = Time.unscaledTime;
            if (_breakFx.TryGetValue(id, out float next) && now < next)
            {
                return;
            }
            if (_breakFx.Count > 512)
            {
                _breakFx.Clear();
            }
            _breakFx[id] = now + 1.25f;
            Vector2 feet = defender.current_position;
            if (Kit.Visible(feet))
            {
                float px = Mathf.Clamp(defender.actor_scale, 0.04f, 0.4f);
                Kit.Flare(feet, px, Tiers.LightColor(Tiers.Of(defender)), 16f, 0.42f);
            }
            if (Dev.On)
            {
                Dev.Trace("body break: " + defender.asset.id + "#" + id + " loses lower-realm control immunity to " + BodyBreakAttackers + " attackers");
            }
        }
    }
}
