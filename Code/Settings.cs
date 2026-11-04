using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.ui;
using UnityEngine;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>
    /// The mod's settings (NML's config window: the gear in the mod list, or the Settings button
    /// in the Combat Gods tab). Values are mirrored into these fields; the window writes them live,
    /// so they are re-read on every change and once a second.
    /// </summary>
    internal static class Settings
    {
        public const string GroupProgress = "sakuga_progress";
        public const string GroupStar = "sakuga_star";
        public const string GroupSpectacle = "sakuga_spectacle";
        public const string GroupBalance = "sakuga_balance";
        public const string GroupCrab = "sakuga_crab";
        public const string GroupAura = "sakuga_aura";

        private static readonly string[] Groups = { GroupProgress, GroupStar, GroupSpectacle, GroupAura, GroupBalance, GroupCrab };

        // ---- progression
        /// <summary>0 off, 1 slow, 2 normal (the default), 3 fast, 4 anime.</summary>
        public static int ProgressSpeed = 2;
        public static int GodCap = 2;
        /// <summary>The highest tier natural growth reaches (1 Novice .. 6 God, the default: no limit). Given tiers aren't limited.</summary>
        public static int MaxTier = 6;
        public static bool BeastsAwaken;
        /// <summary>Tier from which promotions are announced at the top of the screen (1..6, 7 = never).</summary>
        public static int AnnounceFrom = 3;

        // ---- favourites
        public static readonly bool[] Star = { false, false, false, false, true, true, true };
        /// <summary>Star the Primordials (a tier of their own above the Gods: Star[God] is the Gods only).</summary>
        public static bool StarPrimordial = true;
        private static bool _starPrimordialWas = true;

        // ---- spectacle
        public static float HitStopScale = 1f;
        /// <summary>0 off (no god abilities), 1 light (no world pause), 2 full.</summary>
        public static int Cinematics = 2;
        /// <summary>Camera shake (and the big flashes) while Primordials fight or awaken: 0 none, 1 a little, 2 full.</summary>
        public static int PrimordialShake = 1;
        public static bool TitleCards = true;
        public static bool Auras = true;
        public static bool CameraFollow;
        /// <summary>The camera goes to ascensions, challenges and duels (Spotlight).</summary>
        public static bool Spotlight = true;
        /// <summary>
        /// The smallest moment the Spotlight's camera goes to, by tier: duels and challenges whose stronger
        /// fighter is at least this (1 Novice .. 6 God; 7: Primordials only). A God's ascension counts as a
        /// God's (6), a Primordial's awakening and World's End always count. Default 5: Grandmasters and up.
        /// </summary>
        public static int SpotlightFrom = 5;

        // ---- auras (index: the tier; a unit with single moves only counts as a Novice)
        public static readonly bool[] Aura = { true, true, true, true, true, true, true };

        // ---- balance
        public static int BareLevel = 3;
        public static float Frequency = 1f;
        public static float ManaCost = 1f;
        public static float Cooldowns = 1f;
        public static bool ClassLimits = true;
        public static bool TeamTechniques = true;
        public static bool Duels = true;
        /// <summary>Two enemy Primordials' battle ends the world (off: they never begin it).</summary>
        public static bool WorldsEnd = false;
        public static bool TrueGodGrowth = true;
        public static int TrueGodCap = 10, TrueGodXp = 50000, TrueGodYears = 50, TrueGodKills = 10;
        public static bool TrueGodRequireBrink = true, TribulationEffects = true;
        public static int TribulationPower = 100;
        public static bool Situations = true;
        /// <summary>Fighters learn only their own schools' techniques (off: every school's).</summary>
        public static bool Schools = true;
        /// <summary>Real damage to the world: 0 none (decals only), 1 scorch marks and light damage, 2 full (craters, smashed trees and buildings, fires).</summary>
        public static int Destruction = 2;
        /// <summary>Technique damage multiplier on top of the tiers' own.</summary>
        public static float Power = 1f;

        // ---- crabzilla
        public static bool CrabzillaCinematic = true;
        public static int CrabzillaGrace = 4;
        public static bool CrabzillaGrandmasters;

        private static bool _ready;
        private static readonly bool[] _starWas = new bool[7];
        private static bool _starKnown;
        private static float _poll;

        public static void Init()
        {
            try
            {
                ModConfig live = SakugaMod.Instance?.GetConfig();
                if (live != null && Canonicalize(live))
                {
                    live.Save();
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("settings order", e);
            }
            Reload();
            _ready = true;
        }

        private static ModConfig _defaults;

        /// <summary>
        /// Puts the settings back in default_config.json's order, groups and rows alike. NML lists them in
        /// whatever order the saved file holds (a setting removed and re-added moves to the end), so the
        /// window shuffled over time. True if anything moved.
        /// </summary>
        internal static bool Canonicalize(ModConfig live)
        {
            string folder = SakugaMod.Instance?.GetDeclaration()?.FolderPath;
            if (live == null || folder == null)
            {
                return false;
            }
            if (_defaults == null)
            {
                _defaults = new ModConfig(Path.Combine(folder, "default_config.json"));
            }
            Dictionary<string, Dictionary<string, ModConfigItem>> src = Traverse.Create(live).Field("_config").GetValue<Dictionary<string, Dictionary<string, ModConfigItem>>>();
            Dictionary<string, Dictionary<string, ModConfigItem>> order = Traverse.Create(_defaults).Field("_config").GetValue<Dictionary<string, Dictionary<string, ModConfigItem>>>();
            if (src == null || order == null)
            {
                return false;
            }
            bool moved = false;
            List<KeyValuePair<string, Dictionary<string, ModConfigItem>>> groups = new List<KeyValuePair<string, Dictionary<string, ModConfigItem>>>();
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> g in order)
            {
                if (!src.TryGetValue(g.Key, out Dictionary<string, ModConfigItem> items))
                {
                    continue;
                }
                List<KeyValuePair<string, ModConfigItem>> rows = new List<KeyValuePair<string, ModConfigItem>>(items.Count);
                if (g.Key == GroupProgress && items.Remove("sakuga_true_god_bosses")) moved = true;
                foreach (string id in g.Value.Keys)
                {
                    if (items.TryGetValue(id, out ModConfigItem it))
                    {
                        rows.Add(new KeyValuePair<string, ModConfigItem>(id, it));
                    }
                    else
                    {
                        rows.Add(new KeyValuePair<string, ModConfigItem>(id, g.Value[id]));
                        moved = true;
                    }
                }
                foreach (KeyValuePair<string, ModConfigItem> kv in items)
                {
                    if (!g.Value.ContainsKey(kv.Key))
                    {
                        rows.Add(kv);
                    }
                }
                int i = 0;
                foreach (string id in items.Keys)
                {
                    moved |= i >= rows.Count || rows[i].Key != id;
                    i++;
                }
                items.Clear();
                foreach (KeyValuePair<string, ModConfigItem> kv in rows)
                {
                    items[kv.Key] = kv.Value;
                }
                groups.Add(new KeyValuePair<string, Dictionary<string, ModConfigItem>>(g.Key, items));
            }
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> kv in src)
            {
                if (!order.ContainsKey(kv.Key))
                {
                    groups.Add(kv);
                }
            }
            int j = 0;
            foreach (string id in src.Keys)
            {
                moved |= j >= groups.Count || groups[j].Key != id;
                j++;
            }
            src.Clear();
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> kv in groups)
            {
                src[kv.Key] = kv.Value;
            }
            // the saved file keeps each row's icon and range from when it was first written: an icon
            // renamed since (iconSakugaTab) showed as a white square. They always come from the defaults
            // (the values stay the player's)
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> g in order)
            {
                if (!src.TryGetValue(g.Key, out Dictionary<string, ModConfigItem> items))
                {
                    continue;
                }
                foreach (KeyValuePair<string, ModConfigItem> d in g.Value)
                {
                    if (d.Value != null && items.TryGetValue(d.Key, out ModConfigItem it) && it != null)
                    {
                        moved |= SyncLook(it, d.Value);
                    }
                }
            }
            return moved;
        }

        /// <summary>A saved row takes its icon and its range from the default row (its value is kept, clamped into the range). True if anything changed.</summary>
        private static bool SyncLook(ModConfigItem live, ModConfigItem def)
        {
            bool changed = false;
            Traverse t = Traverse.Create(live);
            if (live.IconPath != def.IconPath)
            {
                t.Property("IconPath").SetValue(def.IconPath);
                changed = true;
            }
            if (live.Type == ConfigItemType.INT_SLIDER && (live.MinIntVal != def.MinIntVal || live.MaxIntVal != def.MaxIntVal))
            {
                t.Property("MinIntVal").SetValue(def.MinIntVal);
                t.Property("MaxIntVal").SetValue(def.MaxIntVal);
                t.Property("IntVal").SetValue(Mathf.Clamp(live.IntVal, def.MinIntVal, def.MaxIntVal));
                changed = true;
            }
            if (live.Type == ConfigItemType.SLIDER && (live.MinFloatVal != def.MinFloatVal || live.MaxFloatVal != def.MaxFloatVal))
            {
                t.Property("MinFloatVal").SetValue(def.MinFloatVal);
                t.Property("MaxFloatVal").SetValue(def.MaxFloatVal);
                t.Property("FloatVal").SetValue(Mathf.Clamp(live.FloatVal, def.MinFloatVal, def.MaxFloatVal));
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Every setting back to the mod's defaults (default_config.json), through NML so the window,
        /// the saved file and these fields all agree.
        /// </summary>
        public static void ResetToDefaults()
        {
            ModConfig live = SakugaMod.Instance?.GetConfig();
            string folder = SakugaMod.Instance?.GetDeclaration()?.FolderPath;
            if (live == null || folder == null)
            {
                return;
            }
            ModConfig defaults = new ModConfig(Path.Combine(folder, "default_config.json"));
            foreach (string group in Groups)
            {
                Dictionary<string, ModConfigItem> items;
                try
                {
                    items = defaults[group];
                }
                catch (KeyNotFoundException)
                {
                    continue;
                }
                foreach (KeyValuePair<string, ModConfigItem> d in items)
                {
                    ModConfigItem item = Item(live, group, d.Key);
                    if (item != null)
                    {
                        // no NML callback: each one looks its method up by name across every loaded
                        // assembly, and dozens in a row froze the game for seconds (one Reload below)
                        item.SetValue(d.Value.GetValue(), true);
                    }
                }
            }
            _overrides.Clear();
            live.Save();
            Reload();
        }

        /// <summary>One of our config items by id (any group), or null.</summary>
        public static ModConfigItem FindItem(string id)
        {
            ModConfig live = SakugaMod.Instance?.GetConfig();
            if (live == null)
            {
                return null;
            }
            foreach (string group in Groups)
            {
                ModConfigItem item = Item(live, group, id);
                if (item != null)
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>Opens NML's config window on our settings.</summary>
        public static void OpenWindow()
        {
            try
            {
                ModConfig config = SakugaMod.Instance?.GetConfig();
                if (config != null)
                {
                    NeoModLoader.ui.ModConfigureWindow.ShowWindow(config);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] settings window: " + e);
            }
        }

        // NML calls these by name ("Sakuga.Settings:OnChanged") with the new value.
        public static void OnChanged(bool value)
        {
            Changed();
        }

        public static void OnChanged(float value)
        {
            Changed();
        }

        public static void OnChanged(int value)
        {
            Changed();
        }

        private static void Changed()
        {
            Reload();
            if (!_ready)
            {
                return;   // fired while NML loads the config, before the mod exists
            }
        }

        /// <summary>The window writes values without callbacks while it is open: pick them up.</summary>
        public static void Poll()
        {
            _poll -= Time.unscaledDeltaTime;
            if (_poll > 0f)
            {
                return;
            }
            _poll = 1f;
            Reload();
        }

        public static void Reload()
        {
            ModConfig c = SakugaMod.Instance?.GetConfig();
            if (c == null)
            {
                return;
            }
            ProgressSpeed = Mathf.Clamp(Int(c, GroupProgress, "sakuga_progress_speed", 2), 0, 4);
            GodCap = Mathf.Clamp(Int(c, GroupProgress, "sakuga_god_cap", 2), 0, 1000);
            MaxTier = Mathf.Clamp(Int(c, GroupProgress, "sakuga_max_tier", 6), 1, 6);
            BeastsAwaken = Bool(c, GroupProgress, "sakuga_beasts_awaken", false);
            AnnounceFrom = Mathf.Clamp(Int(c, GroupProgress, "sakuga_announce_from", 3), 1, 7);

            Star[1] = Bool(c, GroupStar, "sakuga_star_novice", false);
            Star[2] = Bool(c, GroupStar, "sakuga_star_adept", false);
            Star[3] = Bool(c, GroupStar, "sakuga_star_expert", false);
            Star[4] = Bool(c, GroupStar, "sakuga_star_master", true);
            Star[5] = Bool(c, GroupStar, "sakuga_star_grandmaster", true);
            Star[6] = Bool(c, GroupStar, "sakuga_star_god", true);
            // a tier's auto-favourite switched: off takes the mod's stars back, on gives them
            for (int i = 1; i < Star.Length; i++)
            {
                if (_starKnown && Star[i] != _starWas[i] && World.world != null)
                {
                    try
                    {
                        Progression.Restar((Tier)i, Star[i]);
                    }
                    catch (Exception e)
                    {
                        SakugaMod.Report("restar", e);
                    }
                }
                _starWas[i] = Star[i];
            }
            StarPrimordial = Bool(c, GroupStar, "sakuga_star_primordial", true);
            if (_starKnown && StarPrimordial != _starPrimordialWas && World.world != null)
            {
                try
                {
                    Progression.RestarPrimordials(StarPrimordial);
                }
                catch (Exception e)
                {
                    SakugaMod.Report("restar primordials", e);
                }
            }
            _starPrimordialWas = StarPrimordial;
            _starKnown = true;

            HitStopScale = Mathf.Clamp(Float(c, GroupSpectacle, "sakuga_hitstop", 1f), 0f, 1.5f);
            Cinematics = Mathf.Clamp(Int(c, GroupSpectacle, "sakuga_cinematics", 2), 0, 2);
            PrimordialShake = Mathf.Clamp(Int(c, GroupSpectacle, "sakuga_primordial_shake", 1), 0, 2);
            TitleCards = Bool(c, GroupSpectacle, "sakuga_title_cards", true);
            Auras = Bool(c, GroupSpectacle, "sakuga_auras", true);
            CameraFollow = Bool(c, GroupSpectacle, "sakuga_camera_follow", false);
            Spotlight = Bool(c, GroupSpectacle, "sakuga_spotlight", true);
            SpotlightFrom = Mathf.Clamp(Int(c, GroupSpectacle, "sakuga_spotlight_from", 5), 1, 7);

            BareLevel = Mathf.Clamp(Int(c, GroupBalance, "sakuga_bare_level", 3), 1, 3);
            Frequency = Mathf.Clamp(Float(c, GroupBalance, "sakuga_frequency", 1f), 0.1f, 3f);
            ManaCost = Mathf.Clamp(Float(c, GroupBalance, "sakuga_mana_cost", 1f), 0f, 3f);
            Cooldowns = Mathf.Clamp(Float(c, GroupBalance, "sakuga_cooldowns", 1f), 0.1f, 3f);
            ClassLimits = Bool(c, GroupBalance, "sakuga_class_limits", true);
            TeamTechniques = Bool(c, GroupBalance, "sakuga_team_techniques", true);
            Duels = Bool(c, GroupBalance, "sakuga_duels", true);
            WorldsEnd = Bool(c, GroupBalance, "sakuga_worlds_end", false);
            TrueGodGrowth = Bool(c, GroupProgress, "sakuga_true_god_growth", true);
            TrueGodCap = Mathf.Clamp(Int(c, GroupProgress, "sakuga_true_god_cap", 10), 0, 1000);
            TrueGodXp = Mathf.Clamp(Int(c, GroupProgress, "sakuga_true_god_xp", 50000), 1000, 1000000);
            TrueGodYears = Mathf.Clamp(Int(c, GroupProgress, "sakuga_true_god_years", 50), 1, 1000);
            TrueGodKills = Mathf.Clamp(Int(c, GroupProgress, "sakuga_true_god_kills", 10), 0, 1000);
            TrueGodRequireBrink = Bool(c, GroupProgress, "sakuga_true_god_brink", true);
            TribulationEffects = Bool(c, GroupProgress, "sakuga_tribulation_effects", true);
            TribulationPower = Mathf.Clamp(Int(c, GroupProgress, "sakuga_tribulation_power", 100), 10, 500);
            Situations = Bool(c, GroupBalance, "sakuga_situations", true);
            Schools = Bool(c, GroupBalance, "sakuga_schools", true);
            Destruction = Mathf.Clamp(Int(c, GroupBalance, "sakuga_destruction", 2), 0, 2);
            Power = Mathf.Clamp(Float(c, GroupBalance, "sakuga_power", 1f), 0.25f, 3f);
            try
            {
                Sakuga.Situations.Enabled = Situations;
            }
            catch
            {
                // before the situations exist
            }

            Aura[1] = Bool(c, GroupAura, "sakuga_aura_novice", true);
            Aura[2] = Bool(c, GroupAura, "sakuga_aura_adept", true);
            Aura[3] = Bool(c, GroupAura, "sakuga_aura_expert", true);
            Aura[4] = Bool(c, GroupAura, "sakuga_aura_master", true);
            Aura[5] = Bool(c, GroupAura, "sakuga_aura_grandmaster", true);
            Aura[6] = Bool(c, GroupAura, "sakuga_aura_god", true);

            CrabzillaCinematic = Bool(c, GroupCrab, "sakuga_crabzilla_cinematic", true);
            CrabzillaGrace = Mathf.Clamp(Int(c, GroupCrab, "sakuga_crabzilla_grace", 4), 0, 30);
            CrabzillaGrandmasters = Bool(c, GroupCrab, "sakuga_crabzilla_grandmasters", false);

            // the dev console's values win over the config for the rest of the session
            foreach (KeyValuePair<string, float> o in _overrides)
            {
                Apply(o.Key, o.Value);
            }
        }

        private static readonly Dictionary<string, float> _overrides = new Dictionary<string, float>();

        /// <summary>Dev 'set key value': a setting for this session (the config is re-read every second, so it is kept over it).</summary>
        public static bool Override(string key, float v)
        {
            if (!Apply(key, v))
            {
                return false;
            }
            _overrides[key] = v;
            return true;
        }

        private static bool Apply(string key, float v)
        {
            switch (key)
            {
                // (the same ranges as the config: 'set frequency 0' divided the Brain's gaps by zero)
                case "cinematics": Cinematics = Mathf.Clamp((int)v, 0, 2); break;
                case "primshake": PrimordialShake = Mathf.Clamp((int)v, 0, 2); break;
                case "frequency": Frequency = Mathf.Clamp(v, 0.1f, 3f); break;
                case "hitstop": HitStopScale = Mathf.Clamp(v, 0f, 1.5f); break;
                case "titles": TitleCards = v > 0f; break;
                case "progress": ProgressSpeed = Mathf.Clamp((int)v, 0, 4); break;
                case "maxtier": MaxTier = Mathf.Clamp((int)v, 1, 6); break;
                case "spotlightfrom": SpotlightFrom = Mathf.Clamp((int)v, 1, 7); break;
                case "bare": BareLevel = Mathf.Clamp((int)v, 1, 3); break;
                case "classes": ClassLimits = v > 0f; break;
                case "team": TeamTechniques = v > 0f; break;
                case "duels": Duels = v > 0f; break;
                case "worldsend": WorldsEnd = v > 0f; break;
                case "crab": CrabzillaCinematic = v > 0f; break;
                case "situations":
                    Situations = v > 0f;
                    try
                    {
                        Sakuga.Situations.Enabled = Situations;
                    }
                    catch
                    {
                        // before the situations exist
                    }
                    break;
                case "crabgm": CrabzillaGrandmasters = v > 0f; break;
                case "auras": Auras = v > 0f; break;
                case "aura": for (int t = 1; t < Aura.Length; t++) { Aura[t] = v > 0f; } break;
                case "aura1": Aura[1] = v > 0f; break;
                case "aura2": Aura[2] = v > 0f; break;
                case "aura3": Aura[3] = v > 0f; break;
                case "aura4": Aura[4] = v > 0f; break;
                case "aura5": Aura[5] = v > 0f; break;
                case "aura6": Aura[6] = v > 0f; break;
                case "destruction": Destruction = Mathf.Clamp((int)v, 0, 2); break;
                case "power": Power = Mathf.Clamp(v, 0.25f, 3f); break;
                case "grace": CrabzillaGrace = Mathf.Clamp((int)v, 0, 30); break;
                default: return false;
            }
            return true;
        }

        private static ModConfigItem Item(ModConfig config, string group, string id)
        {
            try
            {
                var items = config[group];
                return items != null && items.TryGetValue(id, out ModConfigItem item) ? item : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool Bool(ModConfig c, string g, string id, bool fallback)
        {
            ModConfigItem i = Item(c, g, id);
            return i != null && i.Type == ConfigItemType.SWITCH ? i.BoolVal : fallback;
        }

        private static float Float(ModConfig c, string g, string id, float fallback)
        {
            ModConfigItem i = Item(c, g, id);
            return i != null && i.Type == ConfigItemType.SLIDER && !float.IsNaN(i.FloatVal) ? i.FloatVal : fallback;
        }

        private static int Int(ModConfig c, string g, string id, int fallback)
        {
            ModConfigItem i = Item(c, g, id);
            return i != null && i.Type == ConfigItemType.INT_SLIDER ? i.IntVal : fallback;
        }

        // ------------------------------------------------- derived values --

        /// <summary>Sakuga XP gain multiplier by progression speed.</summary>
        public static float SxpMult => ProgressSpeed switch { 0 => 0f, 1 => 0.5f, 3 => 2f, 4 => 5f, _ => 1f };

        /// <summary>Promotion trial chance multiplier.</summary>
        public static float TrialMult => ProgressSpeed switch { 0 => 0f, 1 => 0.6f, 3 => 1.5f, 4 => 3f, _ => 1f };

        /// <summary>Years a unit must serve at a tier before the next (multiplier).</summary>
        public static float SeasonMult => ProgressSpeed switch { 1 => 1.5f, 3 => 0.5f, 4 => 0.1f, _ => 1f };

        public static float CapMult => ProgressSpeed == 4 ? 2f : 1f;
    }

    /// <summary>
    /// A "Reset to defaults" button at the top of NML's settings window, shown only while it shows our
    /// settings (the window is shared by every mod: the button is hidden for the others).
    /// </summary>
    [HarmonyPatch(typeof(ModConfigureWindow), nameof(ModConfigureWindow.OnNormalEnable))]
    internal static class ConfigResetButtonPatch
    {
        private const string Name = "CombatGodsReset";

        /// <summary>Our rows always in the same order (see Settings.Canonicalize).</summary>
        private static void Prefix(ModConfigureWindow __instance)
        {
            try
            {
                ModConfig shown = Traverse.Create(__instance).Field("_config").GetValue<ModConfig>();
                if (shown != null && shown == SakugaMod.Instance?.GetConfig())
                {
                    Settings.Canonicalize(shown);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("settings order", e);
            }
        }

        private static void Postfix(ModConfigureWindow __instance)
        {
            try
            {
                Transform content = Traverse.Create(__instance).Property("ContentTransform").GetValue<Transform>();
                if (content == null)
                {
                    return;
                }
                ModConfig shown = Traverse.Create(__instance).Field("_config").GetValue<ModConfig>();
                bool ours = shown != null && shown == SakugaMod.Instance?.GetConfig();
                Transform old = content.Find(Name);
                if (!ours)
                {
                    if (old != null)
                    {
                        old.gameObject.SetActive(false);
                    }
                    return;
                }
                SimpleButton b = old != null ? old.GetComponent<SimpleButton>() : null;
                if (b == null)
                {
                    b = SimpleButton.Instantiate(content, false, Name);
                    LayoutElement le = b.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = 120f;
                    le.preferredHeight = 18f;
                }
                b.gameObject.SetActive(true);
                b.transform.localScale = Vector3.one;
                b.transform.SetAsFirstSibling();
                b.Setup(() => Reset(__instance), null, Locale.Format("sakuga_reset_defaults"), new Vector2(120f, 18f));
                Arrange(content, shown);
            }
            catch (Exception e)
            {
                SakugaMod.Report("reset button", e);
            }
        }

        /// <summary>
        /// NML reuses its group panels and rows from pools, and a reused row keeps the place it had in the
        /// list last time (being parented again to the same panel does not move it): the rows came out in a
        /// different order every so often. Every time the window opens, our panels and rows are put in the
        /// settings' own order.
        /// </summary>
        private static void Arrange(Transform content, ModConfig config)
        {
            Dictionary<string, Dictionary<string, ModConfigItem>> groups = Traverse.Create(config).Field("_config").GetValue<Dictionary<string, Dictionary<string, ModConfigItem>>>();
            if (groups == null)
            {
                return;
            }
            int gi = content.Find(Name) != null && content.Find(Name).gameObject.activeSelf ? 1 : 0;
            foreach (KeyValuePair<string, Dictionary<string, ModConfigItem>> g in groups)
            {
                Transform panel = Active(content, g.Key);
                if (panel == null)
                {
                    continue;
                }
                panel.SetSiblingIndex(gi++);
                Transform rows = panel.Find("Grid");
                if (rows == null)
                {
                    continue;
                }
                int ri = 0;
                foreach (string id in g.Value.Keys)
                {
                    Transform row = Active(rows, id);
                    if (row != null)
                    {
                        row.SetSiblingIndex(ri++);
                    }
                }
            }
        }

        /// <summary>The active child of that name (pools keep inactive leftovers with old names).</summary>
        private static Transform Active(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.gameObject.activeSelf && c.name == name)
                {
                    return c;
                }
            }
            return null;
        }

        private static void Reset(ModConfigureWindow window)
        {
            try
            {
                Settings.ResetToDefaults();
                // the rows already on screen show the restored values: same rows, same order, only the
                // switches and sliders change
                Transform content = Traverse.Create(window).Property("ContentTransform").GetValue<Transform>();
                Type rowType = typeof(ModConfigureWindow).GetNestedType("ModConfigListItem", System.Reflection.BindingFlags.NonPublic);
                System.Reflection.MethodInfo setup = rowType?.GetMethod("Setup");
                if (content != null && setup != null)
                {
                    foreach (Component row in content.GetComponentsInChildren(rowType, false))
                    {
                        ModConfigItem item = Settings.FindItem(row.name);
                        if (item != null)
                        {
                            setup.Invoke(row, new object[] { item });
                        }
                    }
                }
                // (already applied and saved: nothing left for the window to re-apply when it closes)
                Traverse.Create(window).Field("_modifiedItems").Method("Clear").GetValue();
                WorldTip.showNow(Locale.Format("sakuga_reset_done"), false, "top", 2.5f);
            }
            catch (Exception e)
            {
                SakugaMod.Report("reset", e);
            }
        }
    }
}
