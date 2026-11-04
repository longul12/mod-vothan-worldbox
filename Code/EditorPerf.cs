using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>
    /// The unit trait editor with the mod's ~180 extra trait buttons, kept as quick as the game's own.
    /// Measured with the dev "traitbench": the list is masked with a stencil Mask, which draws (and rebuilds,
    /// as it scrolls) every button even far out of view; and every icon carries a drop Shadow that draws
    /// it a second time. Once per editor: the scroll view clips with a RectMask2D instead (buttons out of
    /// view are culled: not drawn, not rebuilt) and the mod's buttons lose the shadow (their tier frames
    /// already outline them).
    /// </summary>
    [HarmonyPatch(typeof(BaseAugmentationsEditor), "OnEnable")]
    internal static class TraitEditorPerfPatch
    {
        private static readonly HashSet<int> _done = new HashSet<int>();

        private static void Postfix(BaseAugmentationsEditor __instance)
        {
            try
            {
                if (!(__instance is ActorTraitsEditor) || _done.Contains(__instance.GetInstanceID()))
                {
                    return;
                }
                IEnumerable buttons = Traverse.Create(__instance).Field("all_augmentation_buttons").GetValue<IEnumerable>();
                if (buttons == null)
                {
                    return;
                }
                bool any = false;
                foreach (object o in buttons)
                {
                    any = true;
                    ActorTraitEditorButton b = o as ActorTraitEditorButton;
                    ActorTrait t = b != null && b.augmentation_button != null ? b.augmentation_button.getElementAsset() : null;
                    if (t == null || !t.id.StartsWith("sakuga_"))
                    {
                        continue;
                    }
                    foreach (Shadow s in b.GetComponentsInChildren<Shadow>(true))
                    {
                        s.enabled = false;
                    }
                }
                if (!any)
                {
                    return;   // (not built yet: next time)
                }
                _done.Add(__instance.GetInstanceID());
                ScrollRect sr = __instance.GetComponentInParent<ScrollRect>();
                RectTransform port = sr != null ? (sr.viewport != null ? sr.viewport : sr.transform as RectTransform) : null;
                Mask mask = port != null ? port.GetComponent<Mask>() : null;
                if (mask != null && mask.enabled && port.GetComponent<RectMask2D>() == null)
                {
                    bool showGraphic = mask.showMaskGraphic;
                    mask.enabled = false;
                    port.gameObject.AddComponent<RectMask2D>();
                    Image img = port.GetComponent<Image>();
                    if (img != null && !showGraphic)
                    {
                        // the mask's image was never drawn: keep it for touches only
                        Color c = img.color;
                        c.a = 0f;
                        img.color = c;
                    }
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("trait editor", e);
            }
        }
    }
}
