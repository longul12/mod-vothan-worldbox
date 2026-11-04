using System;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    internal static class TrueGodAscension
    {
        internal const string Prefix = "sakuga_true_god_";
        private const string Since = Prefix + "since", Kills = Prefix + "kills_v2";
        private const string Victims = Prefix + "victims_v2", BrinkAt = Prefix + "brink_at";
        private const string BrinkLife = Prefix + "brink_life", BrinkDone = Prefix + "brink_done";
        private static int _cursor;
        private static float _scan;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += () => { _cursor = 0; _scan = 0; };
        }

        internal static bool BusyElsewhere(Actor actor)
        {
            return Competitive(actor)
                || Optional("SecretRealmSystem", "IsParticipant", actor)
                || Optional("LootHuntSystem", "Involved", actor);
        }

        private static bool Competitive(Actor actor)
        {
            return Duels.Of(actor) != null || Optional("TournamentSystem", "Contains", actor);
        }

        private static bool Optional(string type, string method, Actor actor)
        {
            Type t = AccessTools.TypeByName("CultivationWarPrototype." + type);
            var m = t == null ? null : AccessTools.Method(t, method, new[] { typeof(Actor) });
            return m != null && (bool)m.Invoke(null, new object[] { actor });
        }

        internal static bool Monster(Actor actor)
        {
            if (actor?.traits == null) return false;
            foreach (ActorTrait t in actor.traits)
                if (t.id.StartsWith("cwp_monster_") || t.id == "cwp_guardian") return true;
            return false;
        }

        public static void EnemyKilled(Actor killer, Actor victim)
        {
            if (killer == null || victim == null || killer == victim || !killer.isAlive()
                || killer.kingdom == null || victim.kingdom == null
                || Tiers.Of(killer) != Tier.God || Primordials.Is(killer)
                || Tiers.Of(victim) != Tier.God || Primordials.Is(victim)
                || Monster(killer) || Monster(victim) || Competitive(killer)
                || Competitive(victim) || !killer.areFoes(victim)) return;
            killer.data.get(Kills, out int count, 0);
            if (count >= 1000) return;
            killer.data.get(Victims, out string ids, "|");
            string token = victim.Sid() + "|";
            if (ids.Contains("|" + token)) return;
            killer.data.set(Victims, ids + token);
            killer.data.set(Kills, count + 1);
        }

        internal static void SurvivedHit(Actor actor, Actor attacker, float before)
        {
            if (actor == null || attacker == null || actor == attacker || !actor.isAlive()
                || actor.kingdom == null || attacker.kingdom == null
                || before <= actor.getHealth()
                || !TribulationRules.NearDeath(actor.getHealth(), actor.getMaxHealth()) || Tiers.Of(actor) != Tier.God
                || Primordials.Is(actor) || Monster(actor) || Monster(attacker) || Competitive(actor)
                || Competitive(attacker) || !actor.areFoes(attacker)) return;
            actor.data.get(BrinkDone, out bool done, false);
            actor.data.get(BrinkAt, out float at, -1f);
            if (done || at >= 0f) return;
            actor.data.set(BrinkAt, (float)World.world.getCurWorldTime());
            actor.data.set(BrinkLife, actor.asset.id);
        }

        private static void CheckBrink(Actor actor)
        {
            actor.data.get(BrinkAt, out float at, -1f);
            if (at < 0f) return;
            actor.data.get(BrinkLife, out string life, "");
            float now = (float)World.world.getCurWorldTime();
            if (!actor.isAlive() || actor.getHealth() <= 0 || actor.asset.id != life || at > now)
            {
                actor.data.set(BrinkAt, -1f);
                return;
            }
            if (!TribulationRules.Survived(true, true, actor.getHealth(), now - at)) return;
            actor.data.set(BrinkDone, true);
            actor.data.set(BrinkAt, -1f);
            NinefoldTribulation.Notify(actor, "sống sót qua thập tử nhất sinh, lĩnh ngộ cơ duyên Chân Thần");
        }

        private static void Tick(float dt)
        {
            if (dt <= 0 || World.world?.units == null) return;
            _scan -= dt;
            if (_scan > 0) return;
            _scan = .25f;
            var units = World.world.units.getSimpleList();
            int n = units.Count;
            for (int i = 0; i < Mathf.Min(32, n); i++)
            {
                if (_cursor >= n) _cursor = 0;
                Actor actor = units[_cursor++];
                if (actor?.data == null || actor.asset == null) continue;
                CheckBrink(actor);
                NinefoldTribulation.TryRestore(actor);
            }
        }

        public static void Try(Actor actor, Tier tier)
        {
            if (tier != Tier.God || Primordials.Is(actor))
            {
                actor.data.set(Since, -1f);
                return;
            }
            float now = (float)World.world.getCurWorldTime();
            actor.data.get(Since, out float since, -1f);
            if (since < 0f || since > now)
            {
                actor.data.set(Since, now);
                return;
            }
            if (!Settings.TrueGodGrowth || Settings.MaxTier < (int)Tier.God
                || Primordials.Trait == null || Settings.TrueGodCap <= 0
                || Monster(actor) || !actor.isAlive() || actor.getHealth() <= 0) return;
            actor.data.get(Kills, out int kills, 0);
            actor.data.get(BrinkDone, out bool brink, false);
            actor.data.get(Prefix + "retry", out float retry, 0f);
            if (!TribulationRules.Qualified(Progression.Sxp(actor), Settings.TrueGodXp,
                (now - since) / 60f, Settings.TrueGodYears, kills, Settings.TrueGodKills,
                brink, Settings.TrueGodRequireBrink) || now < retry) return;
            int living = 0;
            foreach (Actor other in Primordials.Living)
                if (other != null && other.isAlive()) living++;
            if (living >= Settings.TrueGodCap || BusyElsewhere(actor) || actor.has_attack_target
                || actor.isInsideSomething() || actor.isInMagnet() || actor.position_height > .05f
                || actor.is_unconscious || Moves.Busy(actor) || !Moves.CanPerform(actor)
                || actor.getHealth() < actor.getMaxHealth() * .95f) return;
            NinefoldTribulation.Begin(actor);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.getHit))]
    internal static class TrueGodNearDeathPatch
    {
        private static void Prefix(Actor __instance, out float __state)
        {
            __state = __instance?.getHealth() ?? 0;
        }
        private static void Postfix(Actor __instance, BaseSimObject pAttacker, float __state)
        {
            try
            {
                if (pAttacker != null && pAttacker.isActor())
                    TrueGodAscension.SurvivedHit(__instance, pAttacker.a, __state);
            }
            catch (Exception e) { SakugaMod.Report("true god near death", e); }
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.die))]
    internal static class TrueGodDeathPatch
    {
        private static void Postfix(Actor __instance)
        {
            if (__instance?.data != null && (!__instance.isAlive() || __instance.getHealth() <= 0))
                __instance.data.set(TrueGodAscension.Prefix + "brink_at", -1f);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.updateMovement))]
    internal static class TribulationMovementPatch
    {
        private static bool Prefix(Actor __instance)
        {
            return !Moves.TryGet(__instance, out Move move) || !(move is NinefoldTribulation);
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToAttack))]
    internal static class TribulationAttackPatch
    {
        private static bool Prefix(Actor __instance, ref bool __result)
        {
            if (!Moves.TryGet(__instance, out Move move) || !(move is NinefoldTribulation)) return true;
            __result = false;
            return false;
        }
    }
}
