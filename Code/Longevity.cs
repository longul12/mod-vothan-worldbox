using System;
using HarmonyLib;

namespace Sakuga
{
    /// <summary>
    /// How long the tiers live. Experts, Masters and Grandmasters live far beyond their people (their
    /// tiers add 125, 250 and 500 years to the lifespan: Tiers), and Combat Gods and Primordials never die
    /// of old age at all: only a fight can end them. Their unit window shows their lifespan as "???", as
    /// the game does for its own immortals. (The game's "immortal" trait is not used: an immortal of 300
    /// years and more may turn evil.)
    /// </summary>
    internal static class Longevity
    {
        /// <summary>Never dies of old age: a Combat God or a Primordial (Tiers.Of answers God for both).</summary>
        public static bool Ageless(Actor a)
        {
            return a != null && Tiers.Of(a) == Tier.God;
        }
    }

    /// <summary>The game's old-age death (each year past the lifespan, a growing chance): never for the ageless.</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.checkNaturalDeath))]
    internal static class AgelessPatch
    {
        private static bool Prefix(Actor __instance, ref bool __result)
        {
            try
            {
                if (Longevity.Ageless(__instance))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("ageless", e);
            }
            return true;
        }
    }

    /// <summary>The unit window's lifespan: "???" for the ageless (in the God tier's gold).</summary>
    [HarmonyPatch(typeof(UnitStatsElement), nameof(UnitStatsElement.setIconValue))]
    internal static class AgelessLifespanPatch
    {
        private static bool Prefix(UnitStatsElement __instance, string pName)
        {
            try
            {
                if (pName == "i_lifespan" && Longevity.Ageless(__instance.actor) && __instance._stats_icons != null)
                {
                    __instance._stats_icons.setText("i_lifespan", "???", Tiers.Info(Tier.God).Hex);
                    return false;
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("ageless lifespan", e);
            }
            return true;
        }
    }
}
