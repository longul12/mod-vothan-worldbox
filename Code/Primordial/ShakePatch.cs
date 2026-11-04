using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Every camera shake (the mod's and the game's own, its bombs among them) goes through
    /// MapBox.startShake: while Primordials fight or awaken, the "Primordial screen shake" setting
    /// scales it, or stops it.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.startShake))]
    internal static class PrimordialShakePatch
    {
        private static bool Prefix(ref float pIntensity)
        {
            if (!Primordials.ShakeScope)
            {
                return true;
            }
            float k = Primordials.ShakeK;
            if (k <= 0f)
            {
                return false;
            }
            pIntensity *= k;
            return true;
        }
    }

    /// <summary>
    /// The game flashes every tile it reshapes, and tiles are squares: while Primordials fight or awaken
    /// (thousands of tiles torn up at once, every God's blow among them) the land would be covered in
    /// flashing squares. Not then.
    /// </summary>
    [HarmonyPatch(typeof(PixelFlashEffects), nameof(PixelFlashEffects.flashPixel))]
    internal static class PrimordialNoTileFlash
    {
        private static bool Prefix()
        {
            // (and in their aftermath: the fires and the lava they leave flash tile by tile for minutes)
            return !(Primordials.ShakeScope || Primordials.Any || Time.unscaledTime < WorldsEnd.LastEnded + 180f);
        }
    }
}
