using System;
using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>
    /// The Combat Gods tab (the techniques themselves are never buttons: fighters learn them by
    /// climbing the tiers, naturally or made so by the player).
    ///   Novice .. God,
    ///   Primordial     - one button per tier: click a unit once and it becomes that tier (with its
    ///                    ceremony, or the Primordial's awakening), whatever it was before
    ///   Humble         - click a unit: it drops one tier (a Primordial falls to Combat God)
    ///   Unleash        - click a unit: it performs the best technique it knows right now
    ///   Settings       - the mod's settings window
    /// Other files can add buttons with Powers.Add (id, icon, group, action) from a [SakugaInit].
    /// </summary>
    internal static class Powers
    {
        public const string Tab = "sakuga_tab";
        public const string GroupTiers = "sakuga_group_tiers";
        public const string GroupMore = "sakuga_group_more";

        /// <summary>The tier buttons: "sakuga_power_make_" + novice .. god, primordial.</summary>
        public const string MakePower = "sakuga_power_make_";
        public const string HumblePower = "sakuga_power_humble";
        public const string UnleashPower = "sakuga_power_unleash";
        public const string RankButton = "sakuga_rank";
        public const string SettingsButton = "sakuga_settings";

        private struct Button
        {
            public string Id, Icon, Group;
        }

        private static readonly List<Button> _buttons = new List<Button>();

        public static void Register()
        {
            // one click, one tier: no clicking a unit up the ladder step by step
            foreach (TierInfo info in Tiers.All)
            {
                Tier t = info.Tier;
                Add(MakePower + t.ToString().ToLowerInvariant(), "sakuga/" + info.Icon.Substring(info.Icon.LastIndexOf('/') + 1), GroupTiers, tile => OrderMake(tile, t));
            }
            Add(MakePower + "primordial", "sakuga/iconTierPrimordial", GroupTiers, OrderPrimordial);
            Add(HumblePower, "sakuga/iconPowerHumble", GroupTiers, OrderHumble);
            Add(UnleashPower, "sakuga/iconPowerUnleash", GroupMore, OrderUnleash);
        }

        /// <summary>Adds a god power button to the tab (call from a [SakugaInit] method).</summary>
        public static void Add(string id, string icon, string group, Func<WorldTile, bool> action)
        {
            GodPower p = AssetManager.powers.add(new GodPower
            {
                id = id,
                name = id,
                path_icon = icon,
                rank = PowerRank.Rank0_free,
                requires_premium = false,
                unselect_when_window = true,
                set_used_camera_drag_on_long_move = true,
                // the game's yellow ring under the unit nearest the cursor: the one a click will act on
                show_close_actor = true
            });
            p.click_action = (PowerActionWithID)Delegate.Combine(p.click_action, new PowerActionWithID((tile, power) =>
            {
                try
                {
                    return tile != null && action(tile);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] " + id + ": " + e);
                    return false;
                }
            }));
            _buttons.Add(new Button { Id = id, Icon = icon, Group = group });
        }

        public static void AddTab()
        {
            PowersTab tab = TabManager.CreateTab(Tab, Tab, Tab + "_description", Icon("sakuga/iconCombatGodsTab"));
            List<string> groups = new List<string> { GroupTiers, GroupMore };
            tab.SetLayout(groups);
            foreach (string g in groups)
            {
                int n = 0;
                foreach (Button b in _buttons)
                {
                    if (b.Group == g)
                    {
                        tab.AddPowerButton(g, PowerButtonCreator.CreateGodPowerButton(b.Id, Icon(b.Icon)));
                        n++;
                    }
                }
                if (g == GroupMore)
                {
                    tab.AddPowerButton(g, PowerButtonCreator.CreateSimpleButton(RankButton, RankWindow.Show, Icon("sakuga/iconCombatGodsTab")));
                    n++;
                    tab.AddPowerButton(g, PowerButtonCreator.CreateSimpleButton(SettingsButton, Settings.OpenWindow, Icon("sakuga/iconCombatGodsSettings")));
                    n++;
                }
                if (n % 2 == 1)
                {
                    tab.AddPowerButton(g, null);   // an empty slot: each group fills whole columns
                }
            }
            tab.UpdateLayout();
            KeepTabInLine();
            Hooks.FrameTick += dt =>
            {
                _lineCheck -= Time.unscaledDeltaTime;
                if (_lineCheck <= 0f)
                {
                    _lineCheck = 1f;
                    KeepTabInLine();
                }
            };
        }

        private static float _lineCheck;
        private static Transform _entry;

        /// <summary>
        /// The tab headers sit in a grid as wide as the bottom bar (seven 43-wide tabs in 0.51): one tab
        /// too many (another mod's) and ours wrapped under the first. All headers stay on one line (a
        /// fixed column count; over nine, the tabs narrow a little to fit), and ours goes to the right end
        /// at the start and whenever another mod adds a tab (a tab the player drags elsewhere stays there).
        /// </summary>
        private static void KeepTabInLine()
        {
            try
            {
                if (_entry == null)
                {
                    Transform bar = CanvasMain.instance != null ? CanvasMain.instance.canvas_ui.transform.Find("CanvasBottom/BottomElements/BottomElementsMover/TabsButtons") : null;
                    _entry = bar != null ? bar.Find("Button_" + Tab) : null;
                    if (_entry == null)
                    {
                        return;
                    }
                }
                Transform parent = _entry.parent;
                int active = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    if (parent.GetChild(i).gameObject.activeSelf)
                    {
                        active++;
                    }
                }
                if (parent.childCount != _siblings)
                {
                    _siblings = parent.childCount;
                    _entry.SetAsLastSibling();
                }
                GridLayoutGroup grid = parent.GetComponent<GridLayoutGroup>();
                RectTransform rect = parent as RectTransform;
                if (grid == null || rect == null || active <= 0)
                {
                    return;
                }
                if (_cell <= 0f)
                {
                    _cell = grid.cellSize.x;
                }
                float width = rect.rect.width;
                if (active * _cell <= width + 0.5f && grid.constraint == GridLayoutGroup.Constraint.Flexible)
                {
                    return;   // they all fit already
                }
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = active;
                float cell = active > 9 ? Mathf.Max(34f, width / active) : _cell;
                if (Mathf.Abs(grid.cellSize.x - cell) > 0.01f)
                {
                    grid.cellSize = new Vector2(cell, grid.cellSize.y);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("tab line", e);
            }
        }

        private static int _siblings = -1;
        private static float _cell;

        private static Sprite Icon(string path)
        {
            Sprite s = SpriteTextureLoader.getSprite("ui/Icons/" + path);
            return s != null ? s : SpriteTextureLoader.getSprite("ui/Icons/sakuga/iconCombatGodsTab");
        }

        /// <summary>
        /// The unit the click is for: the one the game rings in yellow under the cursor (if it suits),
        /// else the living unit nearest to the click (within 3 tiles).
        /// </summary>
        internal static Actor UnitAt(WorldTile tile, Func<Actor, bool> filter = null)
        {
            Actor ringed = World.world != null ? World.world.getActorNearCursor() : null;
            if (ringed != null && ringed.isAlive() && (filter == null || filter(ringed))
                && Vector2.Distance(ringed.current_position, new Vector2(tile.posV3.x, tile.posV3.y)) <= 3f)
            {
                return ringed;
            }
            Vector2 at = new Vector2(tile.posV3.x, tile.posV3.y);
            Actor best = null;
            float bestD = 3f;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, 1))
            {
                float d = Vector2.Distance(u.current_position, at);
                if (d < bestD && u.isAlive() && (filter == null || filter(u)))
                {
                    bestD = d;
                    best = u;
                }
            }
            return best;
        }

        private static void Tip(string key, params object[] args)
        {
            WorldTip.showNow(Locale.Format(key, args), false, "top", 2.2f);
        }

        /// <summary>Click a unit: it becomes this tier at once (any tier it held, a Primordial's included, is replaced).</summary>
        internal static bool OrderMake(WorldTile tile, Tier tier)
        {
            Actor u = UnitAt(tile, x => !x.asset.is_boat);
            if (u == null)
            {
                Tip("sakuga_tip_no_unit");
                return false;
            }
            bool prim = Primordials.Is(u);
            if (!prim && Tiers.Of(u) == tier)
            {
                Tip("sakuga_tip_already", u.getName(), Tiers.Name(tier));
                return false;
            }
            if (prim)
            {
                u.removeTrait(Primordials.Trait);   // (its tier is the Primordial trait: Promote alone would leave it one)
            }
            Progression.Promote(u, tier, false);
            WorldTip.showNow(Locale.Format("sakuga_tip_trained", u.getName(), Tiers.Name(tier)), false, "top", 2.2f, Tiers.Info(tier).Hex);
            return true;
        }

        /// <summary>Click a unit: it becomes a Primordial (its awakening plays).</summary>
        internal static bool OrderPrimordial(WorldTile tile)
        {
            Actor u = UnitAt(tile, x => !x.asset.is_boat);
            if (u == null)
            {
                Tip("sakuga_tip_no_unit");
                return false;
            }
            if (Primordials.Trait == null)
            {
                return false;
            }
            if (Primordials.Is(u))
            {
                Tip("sakuga_tip_already", u.getName(), Locale.Get("trait_" + Primordials.TraitId));
                return false;
            }
            u.addTrait(Primordials.Trait, true);   // (the trait does the rest: tiers replaced, schools put down, the awakening)
            return true;
        }

        internal static bool OrderHumble(WorldTile tile)
        {
            Actor u = UnitAt(tile, x => Tiers.Of(x) != Tier.None);
            if (u == null)
            {
                Tip("sakuga_tip_no_tiered");
                return false;
            }
            if (Primordials.Is(u))
            {
                // a Primordial falls back to a Combat God (its trait is its tier: Promote alone would leave it one)
                u.removeTrait(Primordials.Trait);
                Progression.Promote(u, Tier.God, false);
                Tip("sakuga_tip_humbled", u.getName(), Tiers.Name(Tier.God));
                return true;
            }
            Tier t = Tiers.Of(u) - 1;
            Progression.Promote(u, t, false);
            Tip(t == Tier.None ? "sakuga_tip_humbled_out" : "sakuga_tip_humbled", u.getName(), Tiers.Name(t));
            return true;
        }

        /// <summary>Click a unit: it performs the best technique it knows, now.</summary>
        internal static bool OrderUnleash(WorldTile tile)
        {
            Actor u = UnitAt(tile, x => Techniques.AnyTrait(x));
            if (u == null)
            {
                Tip("sakuga_tip_no_fighter");
                return false;
            }
            if (!Moves.CanPerform(u))
            {
                Tip("sakuga_tip_busy", u.getName());
                return false;
            }
            // as for the Brain: a scene (a duel between its exchanges, a team technique) has it, or a
            // feature holds its techniques (a duel about to claim it, a God's ability waiting its turn)
            if (Director.InScene(u) || Held(u))
            {
                if (Moves.Locked(u))
                {
                    return true;   // the God's ability it was held for has just begun
                }
                Tip("sakuga_tip_busy", u.getName());
                return false;
            }
            Fighter f = Fighters.Get(u);
            Ctx c = new Ctx();
            c.Build(f);
            Plan best = null;
            foreach (Technique t in Techniques.All)
            {
                if (t.Team && !Settings.TeamTechniques)
                {
                    continue;
                }
                int level = Techniques.Level(u, t, f.Tier);
                if (level <= 0)
                {
                    continue;
                }
                Plan p = null;
                try
                {
                    p = t.Plan(c, level);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] unleash plan " + t.Id + ": " + e);
                }
                if (p == null || p.Score <= 0f)
                {
                    continue;
                }
                p.Tech = t;
                p.Level = level;
                p.Score *= UnityEngine.Random.Range(0.8f, 1.2f);
                if (best == null || p.Score > best.Score)
                {
                    best = p;
                }
            }
            if (best == null || !Brain.Execute(f, best))
            {
                Tip("sakuga_tip_no_opening", u.getName());
                return false;
            }
            return true;
        }

        private static bool Held(Actor u)
        {
            for (int i = 0; i < Hooks.HoldTechniques.Count; i++)
            {
                if (Hooks.HoldTechniques[i](u))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
