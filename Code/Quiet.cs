using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Keeps the game's own little effects out of the way of a technique: a unit in a move doesn't
    /// dodge, block or deflect on its own (that shoves it off its pose), the white weapon-slash sprites
    /// of bystanders' blows aren't drawn over a level II/III set piece, and happiness / XP icons don't
    /// pop up over one. Damage, sounds and everything else still happen.
    /// </summary>
    internal static class Quiet
    {
        /// <summary>Is a level II+ technique being performed within 'r' tiles of 'at'?</summary>
        public static bool NearSetPiece(Vector2 at, float r)
        {
            if (Moves.Count == 0)
            {
                return false;
            }
            float r2 = r * r;
            IReadOnlyList<Move> all = Moves.All;
            for (int i = 0; i < all.Count; i++)
            {
                Move m = all[i];
                if (m.Tech != null && m.Level >= 2 && (m.Pose.Ground - at).sqrMagnitude <= r2)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToUseAdvancedCombatAction))]
    internal static class QuietCombatActionPatch
    {
        private static bool Prefix(Actor __instance, ref CombatActionAsset pResultCombatAsset, ref bool __result)
        {
            try
            {
                if (Moves.Busy(__instance.Sid()))
                {
                    pResultCombatAsset = null;
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("quiet combat action", e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Actor), nameof(Actor.spawnSlash))]
    internal static class QuietSlashPatch
    {
        private static bool Prefix(Vector2 pTowardsPosition)
        {
            try
            {
                return !Quiet.NearSetPiece(pTowardsPosition, 0.9f);
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.showMetaEventEffect), new[] { typeof(string), typeof(Actor) })]
    internal static class QuietMetaEffectPatch
    {
        private static bool Prefix(Actor pActor)
        {
            try
            {
                return pActor == null || !(Moves.Busy(pActor) || Quiet.NearSetPiece(pActor.current_position, 3f));
            }
            catch
            {
                return true;
            }
        }
    }
}
