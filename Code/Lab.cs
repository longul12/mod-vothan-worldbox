using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Dev: the progression lab. "lab [years] [kingdoms] [size] [war]" makes a new continent world of the
    /// given size (tiny .. gigantic), settles that many young civilizations (ten adults each) as far apart
    /// as the land allows, turns on sonic speed and logs how the tiers arise naturally: the year each tier
    /// first appears, a census every 25 years (tiers, kingdoms, wars, and how many units are held back by
    /// which gate), and at the end a summary. War: "normal" (the game decides), "peace" (diplomacy off: no
    /// wars are declared) or "war" (every few years a kingdom at peace declares war on another).
    /// A fifth argument runs it in turbo: that many simulation steps per frame (sonic speed is 2) with the
    /// world's drawing switched off, for long runs ("lab 1000 30 large normal 16").
    /// "labstat" logs the census now; "labstop" ends the run (normal speed).
    /// </summary>
    internal static class Lab
    {
        private static int _stage;
        private static float _wait;
        private static int _years = 200;
        private static int _kingdoms = 6;
        private static string _size = "standard";
        private static string _war = "normal";
        private static readonly string[] Races =
        {
            "human", "orc", "elf", "dwarf", "civ_cat", "civ_dog", "civ_fox", "civ_wolf", "civ_bear", "civ_rabbit",
            "civ_monkey", "civ_frog", "civ_penguin", "civ_capybara", "civ_goat", "civ_sheep", "civ_hyena", "civ_rhino",
            "civ_crocodile", "civ_turtle", "civ_alpaca", "civ_armadillo", "civ_buffalo", "civ_cow", "civ_chicken",
            "civ_rat", "civ_snake", "civ_scorpion", "civ_seal", "civ_beetle", "civ_crab", "civ_piranha", "civ_liliar",
            "civ_garlic_man", "civ_lemon_man", "civ_acid_gentleman", "civ_crystal_golem", "civ_candy_man", "civ_unicorn"
        };
        private static double _start;
        private static int _ticks;
        private static WorldTimeScaleAsset _turbo;
        private static bool _dark;
        private static int _vsync, _fps;
        private static int _lastLogged = -1;
        private static int _lastWarYear = -1;
        private static float _realStart;
        private static readonly int[] _first = new int[Tiers.Count + 1];
        private static readonly int[] _made = new int[Tiers.Count + 1];
        private static readonly int[] _peak = new int[Tiers.Count + 1];
        private static readonly float[] _sumLate = new float[Tiers.Count + 1];
        private static int _lateN;
        private static int _peakPop, _peakKingdoms, _warsSeen, _wars;

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["lab"] = a =>
            {
                _years = (int)Dev.ArgF(a, 1, 200f);
                _kingdoms = Mathf.Clamp((int)Dev.ArgF(a, 2, 6f), 1, 80);
                _size = Dev.Arg(a, 3, "standard");
                _war = Dev.Arg(a, 4, "normal");
                _ticks = (int)Dev.ArgF(a, 5, 0f);
                _dark = Dev.Arg(a, 6, "") == "dark";
                Array.Clear(_first, 0, _first.Length);
                Array.Clear(_made, 0, _made.Length);
                Array.Clear(_peak, 0, _peak.Length);
                Array.Clear(_sumLate, 0, _sumLate.Length);
                _lateN = 0;
                _peakPop = _peakKingdoms = _warsSeen = _wars = 0;
                _lastLogged = -1;
                _lastWarYear = -1;
                Config.current_map_template = "continent";
                Config.customMapSize = _size;
                World.world.generateNewMap();
                _stage = 1;
                _wait = 3f;
                Dev.Log("lab: " + _size + " continent, " + _kingdoms + " kingdoms, war " + _war + ", " + _years + " years");
            };
            Dev.Commands["labstat"] = a => Census(Year(), true);
            Dev.Commands["labstop"] = a => Stop("stopped");
            Hooks.TierChanged += (a, old, now, natural) =>
            {
                if (_stage != 3 || now == Tier.None || now <= old)
                {
                    return;
                }
                _made[(int)now]++;
                if (_first[(int)now] == 0)
                {
                    _first[(int)now] = Mathf.Max(1, Year());
                    Dev.Log("lab: FIRST " + now + " in year " + Year() + " (" + (a.asset != null ? a.asset.id : "?") + ", kills " + a.data.kills
                        + ", level " + a.data.level + ", sxp " + Progression.Sxp(a).ToString("F0") + (natural ? "" : ", not natural") + ")");
                }
            };
            Hooks.FrameTick += dt => Tick(dt);
        }

        private static int Year()
        {
            return World.world == null ? 0 : (int)((World.world.getCurWorldTime() - _start) / 60.0);
        }

        private static void Tick(float dt)
        {
            if (_stage == 0)
            {
                return;
            }
            try
            {
                if (_stage == 1 || _stage == 2)
                {
                    if (SmoothLoader.isLoading() || !Config.game_loaded)
                    {
                        _wait = 3f;
                        return;
                    }
                    _wait -= Time.unscaledDeltaTime;
                    if (_wait > 0f)
                    {
                        return;
                    }
                    if (_stage == 1)
                    {
                        Laws();
                        Settle();
                        _stage = 2;
                        _wait = 1f;
                        return;
                    }
                    _start = World.world.getCurWorldTime();
                    _realStart = Time.realtimeSinceStartup;
                    DebugConfig.setOption(DebugOption.SonicSpeed, true);
                    if (_ticks >= 2)
                    {
                        _vsync = QualitySettings.vSyncCount;
                        _fps = Application.targetFrameRate;
                        _turbo = new WorldTimeScaleAsset
                        {
                            id = "sakuga_lab_turbo",
                            locale_key = "speed_x40",
                            multiplier = 20f,
                            sonic = true,
                            ticks = _ticks,
                            conway_ticks = 40,
                            render_skip = _dark,
                            path_icon = "ui/Icons/iconClockXSonic"
                        };
                        Config.time_scale_asset = _turbo;
                        if (_dark && Camera.main != null)
                        {
                            Camera.main.enabled = false;   // nothing is drawn: every frame goes to the simulation
                        }
                    }
                    _stage = 3;
                    Dev.Log("lab: sonic speed on, year 0");
                    return;
                }
                if (_turbo != null)
                {
                    if (Config.time_scale_asset != _turbo)
                    {
                        Config.time_scale_asset = _turbo;   // (something set the speed back)
                    }
                    // as many frames as the machine can run, and next to nothing to draw in them: the
                    // camera looks closely at the map's corner (the simulation steps stay sonic speed's)
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                    Camera cam = Camera.main;
                    if (cam != null && !_dark)
                    {
                        cam.orthographicSize = 2f;
                        cam.transform.position = new Vector3(1f, 1f, cam.transform.position.z);
                    }
                }
                int y = Year();
                if (_war == "war" && y != _lastWarYear && y % 3 == 0)
                {
                    _lastWarYear = y;
                    Warmonger();
                }
                if (y != _lastLogged && y % 25 == 0)
                {
                    _lastLogged = y;
                    Census(y, y % 100 == 0);
                }
                if (y >= _years)
                {
                    Stop("done");
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("lab", e);
            }
        }

        private static void Laws()
        {
            bool diplomacy = _war != "peace";
            if (World.world.world_laws.dict.TryGetValue("world_law_diplomacy", out PlayerOptionData o))
            {
                o.boolVal = diplomacy;
                World.world.world_laws.updateCaches();
            }
        }

        /// <summary>War mode: every kingdom at peace picks a fight with the nearest other kingdom.</summary>
        private static void Warmonger()
        {
            List<Kingdom> ks = new List<Kingdom>();
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k != null && k.isAlive() && k.isCiv() && k.capital != null)
                {
                    ks.Add(k);
                }
            }
            foreach (Kingdom k in ks)
            {
                if (k.hasEnemies())
                {
                    continue;
                }
                Kingdom best = null;
                float bestD = float.MaxValue;
                WorldTile kt = k.capital.getTile();
                if (kt == null)
                {
                    continue;
                }
                foreach (Kingdom o in ks)
                {
                    WorldTile ot = o != k ? o.capital.getTile() : null;
                    if (ot == null)
                    {
                        continue;
                    }
                    float d = (ot.pos - kt.pos).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = o;
                    }
                }
                if (best != null && World.world.diplomacy.startWar(k, best, WarTypeLibrary.normal, false) != null)
                {
                    _warsSeen++;
                }
            }
        }

        private static void Stop(string why)
        {
            if (_stage == 0)
            {
                return;
            }
            Census(Year(), true);
            List<string> f = new List<string>();
            for (int i = 1; i <= Tiers.Count; i++)
            {
                f.Add((Tier)i + " " + (_first[i] > 0 ? "y" + _first[i] : "never") + " made " + _made[i] + " peak " + _peak[i]
                    + " avg " + (_lateN > 0 ? (_sumLate[i] / _lateN).ToString("F1") : "-"));
            }
            Dev.Log("lab " + why + " after " + Year() + " years (" + ((Time.realtimeSinceStartup - _realStart) / 60f).ToString("F1") + " real min, "
                + (_ticks >= 2 ? "turbo " + _ticks + ", " : "") + _size + ", " + _kingdoms + " kingdoms, war " + _war + ", peak pop " + _peakPop + ", peak kingdoms " + _peakKingdoms
                + ", wars started by lab " + _warsSeen + "): " + string.Join(" | ", f));
            DebugConfig.setOption(DebugOption.SonicSpeed, false);
            if (_turbo != null)
            {
                _turbo = null;
                Config.setWorldSpeed("x1", false);
                QualitySettings.vSyncCount = _vsync;
                Application.targetFrameRate = _fps;
                if (Camera.main != null)
                {
                    Camera.main.enabled = true;
                }
            }
            _stage = 0;
        }

        /// <summary>Settlements as far apart as the land allows.</summary>
        private static void Settle()
        {
            List<WorldTile> land = new List<WorldTile>();
            int step = Mathf.Max(3, Mathf.Min(MapBox.width, MapBox.height) / 60);
            for (int x = 8; x < MapBox.width - 8; x += step)
            {
                for (int y = 8; y < MapBox.height - 8; y += step)
                {
                    WorldTile t = World.world.GetTileSimple(x, y);
                    if (t != null && t.Type != null && t.Type.ground && !t.Type.liquid && !t.Type.block && !t.Type.lava && Grassy(t))
                    {
                        land.Add(t);
                    }
                }
            }
            if (land.Count == 0)
            {
                Dev.Log("lab: no land found");
                return;
            }
            List<WorldTile> spots = new List<WorldTile> { land[UnityEngine.Random.Range(0, land.Count)] };
            while (spots.Count < _kingdoms && spots.Count < land.Count)
            {
                WorldTile best = null;
                float bestD = -1f;
                foreach (WorldTile t in land)
                {
                    float d = float.MaxValue;
                    foreach (WorldTile s in spots)
                    {
                        d = Mathf.Min(d, (t.pos - s.pos).sqrMagnitude);
                    }
                    if (d > bestD)
                    {
                        bestD = d;
                        best = t;
                    }
                }
                spots.Add(best);
            }
            int total = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                string race = Races[i % Races.Length];
                int n = 0;
                for (int k = 0; k < 40 && n < 10; k++)
                {
                    WorldTile t = World.world.GetTileSimple(
                        Mathf.Clamp(spots[i].x + UnityEngine.Random.Range(-3, 4), 0, MapBox.width - 1),
                        Mathf.Clamp(spots[i].y + UnityEngine.Random.Range(-3, 4), 0, MapBox.height - 1));
                    if (t == null || t.Type == null || t.Type.liquid || t.Type.block)
                    {
                        continue;
                    }
                    if (World.world.units.spawnNewUnit(race, t, false, false, 0f, null, false, true) != null)
                    {
                        n++;
                    }
                }
                total += n;
            }
            Dev.Log("lab: settled " + spots.Count + " groups (" + total + " units) on a " + MapBox.width + "x" + MapBox.height + " map");
        }

        private static bool Grassy(WorldTile t)
        {
            return t.top_type != null || t.Type.id.Contains("soil") || t.Type.id.Contains("sand");
        }

        /// <summary>The world now: tiers, kingdoms, wars, and how close the rest are.</summary>
        private static void Census(int year, bool full)
        {
            int pop = 0, adults = 0, killers = 0;
            int[] tiers = new int[Tiers.Count + 1];
            int[] eligible = new int[Tiers.Count + 1];
            int[] nearKills = new int[Tiers.Count + 1];
            int[] nearXp = new int[Tiers.Count + 1];
            int[] nearLevel = new int[Tiers.Count + 1];
            int[] nearYears = new int[Tiers.Count + 1];
            int[] nearProof = new int[Tiers.Count + 1];
            foreach (Actor a in World.world.units)
            {
                if (a == null || !a.isAlive() || a.asset == null || !a.isKingdomCiv())
                {
                    continue;
                }
                pop++;
                if (!a.isAdult())
                {
                    continue;
                }
                adults++;
                int kl = a.data.kills;
                if (kl > 0) killers++;
                float sxp = Progression.Sxp(a);
                Tier cur = Tiers.Of(a);
                tiers[(int)cur]++;
                if (cur == Tier.God)
                {
                    continue;
                }
                Tier next = cur + 1;
                TierInfo info = Tiers.Info(next);
                if (Progression.Eligible(a, cur, next))
                {
                    eligible[(int)next]++;
                }
                if (sxp < info.Sxp) nearXp[(int)next]++;
                else if (Progression.Record(a) < info.Kills) nearKills[(int)next]++;
                else if (a.data.level < info.Level) nearLevel[(int)next]++;
                else if (cur != Tier.None && Progression.Years(a) < info.Seasoning * Settings.SeasonMult) nearYears[(int)next]++;
                else nearProof[(int)next]++;
            }
            int kingdoms = 0;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k != null && k.isAlive() && k.isCiv())
                {
                    kingdoms++;
                }
            }
            int wars = 0;
            foreach (War w in World.world.wars)
            {
                if (w != null && !w.hasEnded())
                {
                    wars++;
                }
            }
            _peakPop = Mathf.Max(_peakPop, pop);
            _peakKingdoms = Mathf.Max(_peakKingdoms, kingdoms);
            for (int i = 1; i <= Tiers.Count; i++)
            {
                _peak[i] = Mathf.Max(_peak[i], tiers[i]);
            }
            if (year >= _years / 2)
            {
                for (int i = 1; i <= Tiers.Count; i++)
                {
                    _sumLate[i] += tiers[i];
                }
                _lateN++;
            }
            string t = "";
            for (int i = 1; i <= Tiers.Count; i++)
            {
                t += " " + ((Tier)i).ToString().Substring(0, 2) + "=" + tiers[i];
            }
            Dev.Log("lab y" + year + ": pop " + pop + " adults " + adults + " killers " + killers + " kingdoms " + kingdoms + " wars " + wars
                + " (units " + World.world.units.Count + ") |" + t + " | eligible " + eligible[1] + "/" + eligible[2] + "/" + eligible[3] + "/" + eligible[4]
                + "/" + eligible[5] + "/" + eligible[6] + " | caps Ex " + Progression.Cap(Tier.Expert) + " Ma " + Progression.Cap(Tier.Master)
                + " GM " + Progression.Cap(Tier.Grandmaster) + " God " + Progression.Cap(Tier.God));
            if (full)
            {
                for (int i = 1; i <= Tiers.Count; i++)
                {
                    Dev.Log("lab   to " + (Tier)i + ": short of xp " + nearXp[i] + ", kills " + nearKills[i] + ", level " + nearLevel[i]
                        + ", years " + nearYears[i] + ", proof/cap " + nearProof[i]);
                }
            }
        }
    }
}
