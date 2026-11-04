using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>
    /// Technique / sakuga trait tooltips: gold parentheticals (Tianren style), and I/II/III → 1级/2级/3级.
    /// </summary>
    internal static class TraitTooltipStyle
    {
        public const string EffectGold = "#FFD700";

        public static bool IsSakugaTrait(ActorTrait trait)
        {
            return trait != null && !string.IsNullOrEmpty(trait.id) && trait.id.StartsWith("sakuga_");
        }

        public static void Apply(Tooltip tooltip)
        {
            if (tooltip == null)
            {
                return;
            }
            ApplyDescription(tooltip.description, colorizeEffects: true);
            ApplyDescription(tooltip.description_2, colorizeEffects: false);
        }

        private static void ApplyDescription(Text text, bool colorizeEffects)
        {
            if (text == null)
            {
                return;
            }
            LocalizedText loc = text.GetComponent<LocalizedText>();
            if (loc != null)
            {
                loc.enabled = false;
                loc.autoField = false;
            }
            text.supportRichText = true;
            string raw = ReplaceMoveLevels(text.text);
            if (!colorizeEffects || string.IsNullOrEmpty(raw))
            {
                text.text = raw;
                return;
            }
            // 只给正文效果括号刷金；底栏保持原色，避免整段变黄
            int footer = raw.IndexOf("招式等级：", System.StringComparison.Ordinal);
            if (footer < 0)
            {
                footer = raw.IndexOf("Move levels:", System.StringComparison.Ordinal);
            }
            if (footer < 0)
            {
                footer = raw.IndexOf("Move rank:", System.StringComparison.Ordinal);
            }
            if (footer > 0)
            {
                string head = ColorizeParentheticalEffects(raw.Substring(0, footer));
                text.text = head + raw.Substring(footer);
            }
            else
            {
                text.text = ColorizeParentheticalEffects(raw);
            }
        }

        /// <summary>正文白字，括号内效果（含括号）金色。</summary>
        public static string ColorizeParentheticalEffects(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }
            if (raw.IndexOf("<color=", System.StringComparison.Ordinal) >= 0)
            {
                return raw;
            }
            string colored = WrapPair(raw, '（', '）');
            colored = WrapPair(colored, '(', ')');
            return colored;
        }

        private static string WrapPair(string text, char open, char close)
        {
            int start = text.IndexOf(open);
            int end = text.LastIndexOf(close);
            if (start < 0 || end <= start)
            {
                return text;
            }
            return text.Substring(0, start)
                + "<color=" + EffectGold + ">"
                + text.Substring(start, end - start + 1)
                + "</color>"
                + text.Substring(end + 1);
        }

        /// <summary>招式等级罗马数字改阿拉伯「N级」（先 III/II 再处理孤立 I）。</summary>
        public static string ReplaceMoveLevels(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            s = s.Replace("III", "3级").Replace("II", "2级");
            s = s.Replace("（I）", "（1级）").Replace("(I)", "(1级)")
                .Replace("（I", "（1级").Replace("(I", "(1级")
                .Replace("；I", "；1级").Replace(";I", ";1级")
                .Replace("、I", "、1级").Replace(" I ", " 1级 ")
                .Replace("等级 I", "等级 1级").Replace("level I", "level 1");
            return s;
        }

        /// <summary>
        /// 选中单位时，底栏「当前等级」改成该单位实战招式等级；无单位则保持单招默认档。
        /// </summary>
        public static void RefreshCurrentMoveLevel(Tooltip tooltip, ActorTrait trait)
        {
            if (tooltip?.description_2 == null || trait == null || string.IsNullOrEmpty(trait.id))
            {
                return;
            }
            if (!trait.id.StartsWith("sakuga_"))
            {
                return;
            }
            Technique tech = Techniques.Get(trait.id.Substring("sakuga_".Length));
            if (tech == null)
            {
                return;
            }
            Actor unit = null;
            try
            {
                unit = SelectedUnit.unit;
            }
            catch
            {
                // SelectedUnit may be unavailable outside the unit UI
            }
            int level = 0;
            if (unit != null && !unit.isRekt())
            {
                level = Techniques.Level(unit, tech);
            }
            string footer = Traits.MoveFooterFor(tech, level);
            Text text = tooltip.description_2;
            LocalizedText loc = text.GetComponent<LocalizedText>();
            if (loc != null)
            {
                loc.enabled = false;
                loc.autoField = false;
            }
            text.supportRichText = true;
            text.text = ReplaceMoveLevels(footer);
            if (tooltip.description_2.transform.parent != null)
            {
                tooltip.description_2.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(footer));
            }
        }
    }

    [HarmonyPatch(typeof(TooltipLibrary), nameof(TooltipLibrary.showTrait))]
    internal static class SakugaTraitTooltipPatch
    {
        private static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            try
            {
                if (pTooltip == null || pData?.trait == null || !TraitTooltipStyle.IsSakugaTrait(pData.trait))
                {
                    return;
                }
                TraitTooltipStyle.Apply(pTooltip);
                TraitTooltipStyle.RefreshCurrentMoveLevel(pTooltip, pData.trait);
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("trait tooltip", e);
            }
        }
    }
}
