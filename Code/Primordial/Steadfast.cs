using HarmonyLib;

namespace Sakuga
{
    /// <summary>
    /// A Primordial stands where it stands: no blow of anything less knocks it back, throws it into the
    /// air, stuns it or puts it to sleep. (Five Gods hammering one kept it flying and stunned: it could
    /// never act.) In a World's End it is held by the battle itself.
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.addForce))]
    internal static class PrimordialNoKnockback
    {
        private static bool Prefix(Actor __instance)
        {
            return !(Primordials.Any && Primordials.Is(__instance)
                && !RealmControl.Breached(__instance, RealmControl.RecentAttacker(__instance)));
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.makeStunned))]
    internal static class PrimordialNoStun
    {
        private static bool Prefix(Actor __instance)
        {
            return !(Primordials.Any && Primordials.Is(__instance)
                && !RealmControl.Breached(__instance, RealmControl.RecentAttacker(__instance)));
        }
    }

    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.addStatusEffect), new[] { typeof(string), typeof(float), typeof(bool) })]
    internal static class PrimordialNoStatusLock
    {
        private static bool Prefix(BaseSimObject __instance, string pID, ref bool __result)
        {
            if (!Primordials.Any || !(__instance is Actor a) || !Primordials.Is(a)
                || RealmControl.Breached(a, RealmControl.RecentAttacker(a)))
            {
                return true;
            }
            if (pID == "stunned" || pID == "frozen" || pID == "sleeping" || pID == "confused" || pID == "magnetized")
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
