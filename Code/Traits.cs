using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// The traits. Two groups:
    ///   Combat Tiers - Novice ... Combat God: stats, mana, and every technique of the tier at its level.
    ///                  Mutually exclusive (giving one replaces the other).
    ///   Combat Techniques - one trait per technique: just that move (no stats), at the "bare move" level.
    /// </summary>
    internal static class Traits
    {
        public const string GroupMoves = "sakuga";
        public const string GroupTiers = "sakuga_tiers";
        public const string GroupTeam = "sakuga_moves_team";
        public const string GroupDivine = "sakuga_moves_divine";
        public const string GroupDivineForged = "sakuga_moves_divine_ii";
        public const string GroupPrimordial = "sakuga_moves_primordial";

        /// <summary>The trait group of a school's techniques.</summary>
        public static string MovesGroup(School s, bool high = false)
        {
            return "sakuga_moves_" + Schools.Id(s) + (high ? "_high" : "");
        }

        /// <summary>Which group a technique's trait is listed in.</summary>
        public static string GroupOf(Technique t)
        {
            if (t.School != School.None)
            {
                return MovesGroup(t.School, t.Entry >= Tier.Master);
            }
            if (t.Team)
            {
                return GroupTeam;
            }
            if (t.PrimordialOnly)
            {
                return GroupPrimordial;
            }
            if (t.GodAbility || t.Entry >= Tier.God)
            {
                return GroupDivine;   // (the forged God techniques all belong to a school now)
            }
            return GroupMoves;
        }

        public static void Register()
        {
            Group(GroupTiers, "#FF2A3A");
            Schools.Register();
            // techniques in groups of a few dozen at most: everyone's, each school's, team, divine (the game's
            // trait editor lays a group out in time that grows with the square of its size: one group of
            // 170 made opening the editor stutter)
            Group(GroupMoves, "#FF8A3D");
            for (int i = 1; i <= Schools.Count; i++)
            {
                School s = (School)i;
                Group(MovesGroup(s), "#" + ColorUtility.ToHtmlStringRGB(Schools.Color(s)));
                Group(MovesGroup(s, true), "#" + ColorUtility.ToHtmlStringRGB(Schools.Color(s)));
            }
            Group(GroupTeam, "#7FD6A0");
            Group(GroupDivine, "#FFD35A");
            Group(GroupPrimordial, "#B8A0FF");
            List<ActorTrait> tiers = new List<ActorTrait>();
            foreach (TierInfo info in Tiers.All)
            {
                tiers.Add(TierTrait(info));
            }
            // above the Gods: the Primordial (never earned, only given; a God and more to every other system)
            tiers.Add(Primordials.Trait = PrimordialTrait());
            // tiers replace each other (the trait editor swaps them)
            foreach (ActorTrait t in tiers)
            {
                t.opposite_traits = new HashSet<ActorTrait>();
                t.opposite_list = new List<string>();
                foreach (ActorTrait o in tiers)
                {
                    if (o != t)
                    {
                        t.opposite_traits.Add(o);
                        t.opposite_list.Add(o.id);
                    }
                }
            }
            Tiers.Link();
            foreach (Technique tech in Techniques.All)
            {
                MoveTrait(tech);
            }
            Techniques.LinkTraits();
        }

        private static void Group(string id, string color)
        {
            if (AssetManager.trait_groups.get(id) == null)
            {
                AssetManager.trait_groups.add(new ActorTraitGroupAsset
                {
                    id = id,
                    name = "trait_group_" + id,
                    color = color
                });
            }
        }

        /// <summary>The Primordial's trait: in the tiers' group, replacing any tier, far beyond a God's numbers.</summary>
        private static ActorTrait PrimordialTrait()
        {
            ActorTrait t = AssetManager.traits.add(new ActorTrait
            {
                id = Primordials.TraitId,
                group_id = GroupTiers,
                path_icon = "ui/Icons/sakuga/iconTierPrimordial",
                type = TraitType.Positive,
                rarity = Rarity.R3_Legendary,
                can_be_given = true,
                can_be_removed = true,
                needs_to_be_explored = false,
                rate_birth = 0,
                rate_inherit = 0,
                rate_acquire_grow_up = 0,
                spawn_random_trait_allowed = false,
                is_mutation_box_allowed = false,
                can_be_in_book = false,
                affects_mind = false,
                can_be_cured = false,
                can_be_removed_by_divine_light = false,
                can_be_removed_by_accelerated_healing = false,
                remove_for_zombie_actor_asset = false,
                priority = 107,
                base_stats = new BaseStats()
            });
            BaseStats s = t.base_stats;
            Set(s, "health", 40000f);
            Set(s, "multiplier_health", 1f);
            Set(s, "damage", 380f);
            Set(s, "multiplier_damage", 1f);
            Set(s, "armor", 60f);
            Set(s, "speed", 30f);
            Set(s, "attack_speed", 6f);
            Set(s, "critical_chance", 0.3f);
            Set(s, "critical_damage_multiplier", 2f);
            Set(s, "mana", 6000f);
            Set(s, "stamina", 600f);
            Set(s, "skill_combat", 0.8f);
            Set(s, "lifespan", 500f);
            s.addTag("immunity_fire");
            t.action_on_augmentation_add = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_add, new WorldActionTrait(OnPrimordialAdded));
            t.action_on_augmentation_remove = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_remove, new WorldActionTrait(OnTierRemoved));
            t.action_on_augmentation_load = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_load, new WorldActionTrait(OnLoad));
            return t;
        }

        private static bool OnPrimordialAdded(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null)
                {
                    Primordials.Given(a);
                    Progression.Changed(a, Tier.None, Tiers.Of(a), false);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] primordial added: " + e);
            }
            return true;
        }

        private static ActorTrait TierTrait(TierInfo info)
        {
            ActorTrait t = AssetManager.traits.add(new ActorTrait
            {
                id = info.TraitId,
                group_id = GroupTiers,
                path_icon = info.Icon,
                type = TraitType.Positive,
                rarity = info.Rarity,
                can_be_given = true,
                can_be_removed = true,
                needs_to_be_explored = false,
                rate_birth = 0,
                rate_inherit = 0,
                rate_acquire_grow_up = 0,
                spawn_random_trait_allowed = false,
                is_mutation_box_allowed = false,
                can_be_in_book = false,
                affects_mind = false,
                can_be_cured = false,
                can_be_removed_by_divine_light = false,
                can_be_removed_by_accelerated_healing = false,
                remove_for_zombie_actor_asset = false,
                priority = 100 + (int)info.Tier,
                base_stats = new BaseStats()
            });
            BaseStats s = t.base_stats;
            Set(s, "health", info.Health);
            Set(s, "multiplier_health", info.MultHealth);
            Set(s, "damage", info.Damage);
            Set(s, "multiplier_damage", info.MultDamage);
            Set(s, "armor", info.Armor);
            Set(s, "speed", info.Speed);
            Set(s, "attack_speed", info.AttackSpeed);
            Set(s, "critical_chance", info.Crit);
            Set(s, "critical_damage_multiplier", info.CritDamage);
            Set(s, "mana", info.Mana);
            Set(s, "stamina", info.Stamina);
            Set(s, "skill_combat", info.Skill);
            Set(s, "lifespan", info.Lifespan);
            if (info.Flow)
            {
                s.addTag("immunity_fire");   // Masters and up fight through their own burning craters
            }
            t.action_on_augmentation_add = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_add, new WorldActionTrait(OnTierAdded));
            t.action_on_augmentation_remove = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_remove, new WorldActionTrait(OnTierRemoved));
            t.action_on_augmentation_load = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_load, new WorldActionTrait(OnLoad));
            return t;
        }

        private static void Set(BaseStats s, string id, float v)
        {
            if (v != 0f)
            {
                s[id] = v;
            }
        }

        private static ActorTrait MoveTrait(Technique tech)
        {
            ActorTrait t = AssetManager.traits.get(tech.TraitId);
            if (t != null)
            {
                return t;
            }
            // never Legendary: the game gives every Legendary trait button a pulsing outline that makes the
            // whole trait editor redraw ~40 times a second (with ~90 of them it lagged); the icons' tier
            // frames already say how rare a technique is
            Rarity r = tech.Entry <= Tier.Novice && !tech.GodAbility ? Rarity.R0_Normal
                : tech.Entry == Tier.Adept ? Rarity.R1_Rare
                : Rarity.R2_Epic;
            t = AssetManager.traits.add(new ActorTrait
            {
                id = tech.TraitId,
                group_id = GroupOf(tech),
                path_icon = tech.Icon,
                type = TraitType.Positive,
                rarity = r,
                can_be_given = true,
                can_be_removed = true,
                needs_to_be_explored = false,
                rate_birth = 0,
                rate_inherit = 0,
                spawn_random_trait_allowed = false,
                is_mutation_box_allowed = false,
                affects_mind = false,
                // training dummies teach the basics
                // (never a school's technique: schools are walked, not picked up from a dummy)
                in_training_dummy_combat_pot = tech.Entry <= Tier.Novice && !tech.Team && !tech.GodAbility && tech.School == School.None,
                priority = 200 + tech.SortOrder,
                base_stats = new BaseStats()
            });
            if (t.in_training_dummy_combat_pot && !AssetManager.traits.pot_traits_combat.Contains(t))
            {
                AssetManager.traits.pot_traits_combat.Add(t);
            }
            t.linkSpells();
            t.action_on_augmentation_add = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_add, new WorldActionTrait(OnLoaded));
            t.action_on_augmentation_remove = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_remove, new WorldActionTrait(OnLoaded));
            t.action_on_augmentation_load = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_load, new WorldActionTrait(OnLoad));
            return t;
        }

        /// <summary>A few of the greatest techniques by name (the highest entry and the biggest first): "Among them: ...".</summary>
        private static string Highlights(List<Technique> list, Tier upTo)
        {
            List<Technique> best = new List<Technique>(list);
            best.RemoveAll(t => t.Entry > upTo);
            best.Sort((a, b) =>
            {
                int c = ((int)b.ScaleAt(3)).CompareTo((int)a.ScaleAt(3));
                if (c != 0)
                {
                    return c;
                }
                c = ((int)b.Entry).CompareTo((int)a.Entry);
                // (the hand-made ones first: they are the techniques the world knows by name)
                return c != 0 ? c : Forge.Is(a.Id).CompareTo(Forge.Is(b.Id));
            });
            List<string> names = new List<string>();
            foreach (Technique t in best)
            {
                string n = Locale.Get("trait_" + t.TraitId);
                if (string.IsNullOrEmpty(n) || n.StartsWith("trait_") || names.Contains(n))
                {
                    continue;
                }
                names.Add(n);
                if (names.Count >= 4)
                {
                    break;
                }
            }
            return names.Count > 0 ? "\n" + Locale.Format("sakuga_among", string.Join(", ", names)) : "";
        }

        /// <summary>
        /// Tooltips that list what each tier knows (built from the techniques that exist, so they
        /// are always right), and when each technique is learned.
        /// </summary>
        [SakugaInit]
        private static void Describe()
        {
            // (the forged and team techniques' names first: they are listed below)
            Forge.Texts();
            GatheringTexts.Texts();
            foreach (TierInfo info in Tiers.All)
            {
                // a count by kind and a few of the greatest by name: a full list of several dozen would run off the screen
                int common = 0, team = 0, divine = 0;
                List<Technique> top = new List<Technique>();
                foreach (Technique t in Techniques.All)
                {
                    if (t.School != School.None)
                    {
                        continue;   // a school's techniques: counted on the school's trait
                    }
                    if (TierLevel(t, info.Tier) <= 0)
                    {
                        continue;
                    }
                    if (t.Team)
                    {
                        team++;
                    }
                    else if (t.GodAbility || t.Entry >= Tier.God)
                    {
                        divine++;
                    }
                    else
                    {
                        common++;
                    }
                    top.Add(t);
                }
                List<string> parts = new List<string>();
                if (common > 0)
                {
                    parts.Add(Locale.Format("sakuga_count_common", common));
                }
                if (team > 0)
                {
                    parts.Add(Locale.Format("sakuga_count_team", team));
                }
                if (divine > 0)
                {
                    parts.Add(Locale.Format("sakuga_count_divine", divine));
                }
                if (info.Tier != Tier.None)
                {
                    parts.Add(Locale.Format("sakuga_tier_schools", Schools.Want(info.Tier)));
                }
                string text = Locale.Format("sakuga_tier_knows", string.Join(", ", parts)) + Highlights(top, info.Tier)
                    + "\n" + Locale.Format("sakuga_tier_line", Locale.Get("trait_" + info.TraitId), info.Mana, info.ManaRegen,
                        Locale.Format("sakuga_tier_fights", Locale.Get("sakuga_weight_" + (int)info.MaxClass)));
                Put("trait_" + info.TraitId + "_info_2", text);
            }
            for (int i = 1; i <= Schools.Count; i++)
            {
                School s = (School)i;
                int[] by = new int[7];
                List<Technique> mine = new List<Technique>();
                foreach (Technique t in Techniques.All)
                {
                    if (t.School == s)
                    {
                        by[Mathf.Clamp((int)t.Entry, 0, 6)]++;
                        mine.Add(t);
                    }
                }
                List<string> counts = new List<string>();
                for (int k = 1; k <= 6; k++)
                {
                    if (by[k] > 0)
                    {
                        counts.Add(by[k] + " " + Tiers.Name((Tier)k).Replace("Combat ", ""));
                    }
                }
                Put("trait_" + Schools.TraitId(s) + "_info_2", mine.Count > 0
                    ? Locale.Format("sakuga_school_teaches", mine.Count, string.Join(" · ", counts)) + Highlights(mine, Tier.God) : "");
            }
            foreach (Technique t in Techniques.All)
            {
                // Always rebuild: footer follows 功法等级 (1级…) and must not stay on old Roman lines.
                Put("trait_" + t.TraitId + "_info_2", MoveBareFooter(t));
            }
        }

        /// <summary>
        /// 功法等级序号：不入流1 … 天人6（陆地神仙为原初，不进招式入门表）。
        /// </summary>
        internal static int GongfaGrade(Tier t)
        {
            return t == Tier.None ? 0 : (int)t;
        }

        /// <summary>显示用：后天（2级）/ Adept (grade 2)。</summary>
        internal static string GradeCell(Tier t)
        {
            return t == Tier.None ? "" : Locale.Format("sakuga_grade_cell", Tiers.Name(t), GongfaGrade(t));
        }

        /// <summary>显示用：3级 / level 3。</summary>
        internal static string MoveLevelCell(int level)
        {
            return Locale.Format("sakuga_move_level_cell", Mathf.Clamp(level, 0, 3));
        }

        /// <summary>
        /// Bare-trait footer（与正文括号里的 1/2/3 级同一套招式等级，勿再混入功法档序号）：
        /// 招式等级：上限{MaxLevel}级；
        /// 当前等级：实战招式 1–3 级（无单位时用单招默认档）；
        /// 习得条件：功法等级≥{入门境}。
        /// </summary>
        private static string MoveBareFooter(Technique t)
        {
            return MoveFooterFor(t, 0);
        }

        /// <summary>Rebuild bottom footer for a technique (e.g. live「当前等级」on a selected unit).</summary>
        internal static string MoveFooterFor(Technique t, int moveLevel)
        {
            int max = Mathf.Clamp(t.MaxLevel, 1, 3);
            int bare = Mathf.Min(max, Settings.BareLevel);
            int level = Mathf.Clamp(moveLevel > 0 ? moveLevel : bare, 1, max);
            // {0}=上限招式级，{1}=当前招式级，{2}=入门功法名（不含「功法N级」混称招式）
            return Locale.Format("sakuga_move_bare", MoveLevelCell(max), MoveLevelCell(level), Tiers.Name(t.Entry));
        }

        private static int TierLevel(Technique t, Tier tier)
        {
            if (tier == Tier.God)
            {
                return t.MaxLevel;
            }
            return tier >= t.Entry ? Mathf.Min(t.MaxLevel, tier - t.Entry + 1) : 0;
        }

        internal static void Put(string key, string value)
        {
            LM.Add("en", key, value);
            try
            {
                string lang = LocalizedTextManager.instance != null ? LocalizedTextManager.instance.language : null;
                if (!string.IsNullOrEmpty(lang) && lang != "en")
                {
                    LM.Add(lang, key, value);
                }
                if (LocalizedTextManager.instance != null)
                {
                    LocalizedTextManager.instance._localized_text[key] = value;
                }
            }
            catch
            {
                // the text shows on the next language load
            }
        }

        /// <summary>
        /// English fallback for forged recipes: keep whatever Locales already loaded for the
        /// current language (e.g. cz.json), so Chinese is not overwritten by the recipe's English.
        /// </summary>
        internal static void PutFallback(string key, string value)
        {
            LM.Add("en", key, value);
            try
            {
                var ltm = LocalizedTextManager.instance;
                if (ltm == null || ltm._localized_text == null)
                {
                    return;
                }
                // Prefer any translation already loaded from Locales (cz/ch/…).
                if (ltm._localized_text.ContainsKey(key))
                {
                    string cur = ltm._localized_text[key];
                    if (!string.IsNullOrEmpty(cur) && cur != key)
                    {
                        return;
                    }
                }
                ltm._localized_text[key] = value;
                string lang = ltm.language;
                if (!string.IsNullOrEmpty(lang) && lang != "en")
                {
                    LM.Add(lang, key, value);
                }
            }
            catch
            {
                // the text shows on the next language load
            }
        }

        private static bool OnTierAdded(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null)
                {
                    Progression.Changed(a, Tier.None, Tiers.Of(a), false);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] tier added: " + e);
            }
            return true;
        }

        private static bool OnTierRemoved(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null && Tiers.Of(a) == Tier.None)
                {
                    Progression.Changed(a, Tier.None, Tier.None, false);
                }
                else if (target is Actor b)
                {
                    Fighters.Touch(b);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] tier removed: " + e);
            }
            return true;
        }

        private static bool OnLoaded(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null)
                {
                    Fighters.Touch(a);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] trait: " + e);
            }
            return true;
        }

        /// <summary>
        /// A unit loaded from a save: only registered. The game is still walking its trait set, so
        /// nothing may change it here (tiers a save stacked are merged by Fighters.Maintain moments later).
        /// </summary>
        private static bool OnLoad(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null && a.isAlive())
                {
                    Fighters.Get(a).Refresh();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] trait load: " + e);
            }
            return true;
        }
    }

    /// <summary>
    /// The game knocks a unit out for a moment whenever its traits are edited in its window. A fighter
    /// from Adept up takes its new power on its feet (a God given its tier rises from its ceremony and
    /// stays up); so does any unit in the middle of a move (its ceremony). A Novice still reels.
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.makeStunnedFromUI))]
    internal static class TraitStunPatch
    {
        private static bool Prefix(Actor __instance, ref bool __state)
        {
            try
            {
                if (__instance != null && (Tiers.Of(__instance) >= Tier.Adept || Moves.Busy(__instance)))
                {
                    __instance.updateStats();
                    return false;
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("trait stun", e);
            }
            // a Novice always reels (no stun resistance for this one)
            KiDeflect.Own++;
            __state = true;
            return true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                KiDeflect.Own--;
            }
            return __exception;
        }
    }
}
