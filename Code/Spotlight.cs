using System;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// THE SPOTLIGHT: big moments the player should not miss. Every challenge, duel, God ascension,
    /// Primordial awakening and World's End is written in the world log (click it and the camera goes
    /// there, as with the game's own entries). With the "Spotlight" setting on, the camera also goes to
    /// the biggest of them by itself ("Spotlight from tier", Grandmasters and up by default, so a growing
    /// kingdom's Adepts and Masters don't pull the camera away): challenges and duels whose stronger
    /// fighter is at least that tier, the most important one first, at most every 15 seconds, never while
    /// a window is open or while the player follows a unit of their own; a God's ascension (the world
    /// slows to normal speed for its ceremony, sonic speed comes back after); a Primordial's awakening
    /// and the battle of two Primordials always.
    /// </summary>
    internal static class Spotlight
    {
        private static WorldLogAsset _fight, _ascension, _primordial, _worldsEnd;
        private static float _lastPan = -99f;
        private static Tier _lastPanTier;
        private static long _following = -1;

        // the world's speed while an ascension plays at normal speed
        private static WorldTimeScaleAsset _speedWas;
        private static bool _sonicWas;
        private static float _restoreAt = -1f;
        private static Actor _ascending;
        private static long _ascendingId = -1;

        [SakugaInit]
        private static void Init()
        {
            try
            {
                _fight = AssetManager.world_log_library.get("sakuga_log_fight") ?? AssetManager.world_log_library.add(new WorldLogAsset
                {
                    id = "sakuga_log_fight",
                    group = "wars",
                    path_icon = "ui/Icons/sakuga/iconTierMaster",
                    color = Toolbox.color_log_warning,
                    text_replacer = Replace
                });
                _ascension = AssetManager.world_log_library.get("sakuga_log_ascension") ?? AssetManager.world_log_library.add(new WorldLogAsset
                {
                    id = "sakuga_log_ascension",
                    group = "kings",
                    path_icon = "ui/Icons/sakuga/iconTierGod",
                    color = Toolbox.color_log_good,
                    text_replacer = Replace
                });
                _primordial = AssetManager.world_log_library.get("sakuga_log_primordial") ?? AssetManager.world_log_library.add(new WorldLogAsset
                {
                    id = "sakuga_log_primordial",
                    group = "kings",
                    path_icon = "ui/Icons/sakuga/iconTierPrimordial",
                    color = Toolbox.color_log_warning,
                    text_replacer = Replace
                });
                _worldsEnd = AssetManager.world_log_library.get("sakuga_log_worlds_end") ?? AssetManager.world_log_library.add(new WorldLogAsset
                {
                    id = "sakuga_log_worlds_end",
                    group = "wars",
                    path_icon = "ui/Icons/sakuga/iconTierPrimordial",
                    color = Toolbox.color_log_warning,
                    text_replacer = Replace
                });
            }
            catch (Exception e)
            {
                SakugaMod.Report("spotlight log", e);
            }
            Hooks.FrameTick += dt => Tick();
            Hooks.WorldCleared += () =>
            {
                Restore();
                _lastPan = -99f;
                _following = -1;
            };
        }

        private static void Replace(WorldLogMessage m, ref string text)
        {
            text = text.Replace("$a$", m.getSpecial(1)).Replace("$b$", m.getSpecial(2));
        }

        /// <summary>A challenge or a duel began between a and b (the stronger's tier is 'tier').</summary>
        public static void Fight(Actor a, Actor b, Tier tier)
        {
            if (a == null || b == null || !a.isAlive() || !b.isAlive())
            {
                return;
            }
            Log(_fight, a, b);
            // (the camera only for fights of the chosen tier and up: Grandmasters by default)
            if (!Settings.Spotlight || (int)tier < Settings.SpotlightFrom)
            {
                return;
            }
            float now = Time.unscaledTime;
            bool sooner = tier > _lastPanTier && now - _lastPan > 4f;   // something bigger may cut a smaller one short
            if (now - _lastPan < 15f && !sooner)
            {
                return;
            }
            if (Pan(Tiers.Of(a) >= Tiers.Of(b) ? a : b, false))
            {
                _lastPan = now;
                _lastPanTier = tier;
            }
        }

        /// <summary>
        /// A God ascends. Logged; with the Spotlight on, the camera goes to it and the world slows to normal
        /// speed so its ceremony plays in full. True if the ceremony should wait a moment for the camera.
        /// </summary>
        public static bool Ascension(Actor god)
        {
            if (god == null || !god.isAlive())
            {
                return false;
            }
            Log(_ascension, god, null);
            if (!Settings.Spotlight || Settings.Cinematics < 2 || (int)Tier.God < Settings.SpotlightFrom)
            {
                return false;
            }
            WorldTimeScaleAsset speed = Config.time_scale_asset;
            if (speed != null && speed.multiplier > 5f && _speedWas == null)
            {
                _speedWas = speed;
                _sonicWas = DebugConfig.isOn(DebugOption.SonicSpeed);
                Config.setWorldSpeed("x1");
            }
            _ascending = god;
            _ascendingId = god.Sid();
            _restoreAt = Time.unscaledTime + 14f;
            bool moved = Pan(god, true);
            _lastPan = Time.unscaledTime;
            _lastPanTier = Tier.God;
            return moved || _speedWas != null;
        }

        /// <summary>
        /// Two Primordials meet and the World's End begins: logged, and with the Spotlight on the camera
        /// goes to them (whatever the tier setting: nothing is bigger) and follows the battle.
        /// </summary>
        public static void WorldsEnd(Actor a, Actor b)
        {
            if (a == null || b == null || !a.isAlive() || !b.isAlive())
            {
                return;
            }
            Log(_worldsEnd, a, b);
            if (!Settings.Spotlight)
            {
                return;
            }
            if (Pan(a, true))
            {
                _lastPan = Time.unscaledTime;
                _lastPanTier = Tier.God;
            }
        }

        /// <summary>
        /// A Primordial awakens: logged, and with the Spotlight on the camera goes to it whatever the tier
        /// setting (the whole world stops for it); the world slows to normal speed while it plays.
        /// </summary>
        public static void Primordial(Actor a)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            Log(_primordial, a, null);
            if (!Settings.Spotlight)
            {
                return;   // (it plays where it is: in full if it is on screen)
            }
            WorldTimeScaleAsset speed = Config.time_scale_asset;
            if (speed != null && speed.multiplier > 5f && _speedWas == null)
            {
                _speedWas = speed;
                _sonicWas = DebugConfig.isOn(DebugOption.SonicSpeed);
                Config.setWorldSpeed("x1");
            }
            _ascending = a;
            _ascendingId = a.Sid();
            _restoreAt = Time.unscaledTime + 16f;
            Pan(a, true);
            _lastPan = Time.unscaledTime;
            _lastPanTier = Tier.God;
        }

        private static void Log(WorldLogAsset asset, Actor a, Actor b)
        {
            if (asset == null)
            {
                return;
            }
            try
            {
                WorldLogMessage m = new WorldLogMessage(asset, a.getName(), b != null ? b.getName() : null)
                {
                    unit = a,
                    location = a.current_position,
                    kingdom = a.kingdom
                };
                m.color_special_1 = Tiers.Info(Tiers.Of(a))?.Hex ?? "#FFFFFF";
                if (b != null)
                {
                    m.color_special_2 = Tiers.Info(Tiers.Of(b))?.Hex ?? "#FFFFFF";
                }
                m.add();
            }
            catch (Exception e)
            {
                SakugaMod.Report("spotlight log", e);
            }
        }

        /// <summary>The camera goes to (and follows) this unit, unless the player is busy with the view.</summary>
        private static bool Pan(Actor a, bool always)
        {
            try
            {
                if (ScrollWindow.isWindowActive() || World.world == null)
                {
                    return false;
                }
                // the player's own follow is the player's (one the Spotlight started may be taken over)
                if (MoveCamera.hasFocusUnit() && !always && !FollowingOurs())
                {
                    return false;
                }
                World.world.locateAndFollow(a, null, null);
                _following = a.Sid();
                return true;
            }
            catch (Exception e)
            {
                SakugaMod.Report("spotlight pan", e);
                return false;
            }
        }

        private static bool FollowingOurs()
        {
            Actor f = MoveCamera.getFocusUnit();
            return f != null && f.Sid() == _following;
        }

        private static void Tick()
        {
            if (_speedWas == null)
            {
                return;
            }
            // back to the player's speed once the ascension is over (or after 14 s at most)
            bool still = _ascending != null && _ascending.Sid() == _ascendingId && _ascending.isAlive()
                && Moves.TryGet(_ascending, out Move m) && (m is Ceremony || m is Awakening);
            if (Time.unscaledTime >= _restoreAt || !still && Time.unscaledTime >= _restoreAt - 12f)
            {
                Restore();
            }
        }

        private static void Restore()
        {
            if (_speedWas == null)
            {
                return;
            }
            try
            {
                // only if the player has not changed the speed meanwhile
                if (Config.time_scale_asset != null && Config.time_scale_asset.id == "x1")
                {
                    if (_sonicWas)
                    {
                        DebugConfig.setOption(DebugOption.SonicSpeed, true);
                    }
                    else
                    {
                        Config.setWorldSpeed(_speedWas);
                    }
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("spotlight speed", e);
            }
            _speedWas = null;
            _ascending = null;
            _ascendingId = -1;
        }
    }
}
