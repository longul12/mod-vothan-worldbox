using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Developer tools, off unless the mod folder has a "dev" folder. Commands are lines written
    /// to dev/cmd.txt (read and deleted every 0.25 s); results go to dev/log.txt, frame captures
    /// to dev/frames. tools/sakuga-build/run_demo.sh in the WorldBox-RE repo drives this.
    ///
    ///   status / stats / census     the world and the mod / techniques started / tier counts
    ///   close / console / ui        close windows (they pause the world) / hide the log console / toggle the UI
    ///   move tech [level] [n] [race] [foe] [layout] [zoom] [wait] [allies] [tier]
    ///                               a clean meadow, a 'race' performer at the tier that knows 'tech' at
    ///                               'level', n 'foe's laid out as adjacent|cluster|far|line|lone|ring|scatter,
    ///                               'allies' friends beside it; all held still for 'wait' s, then it performs
    ///   battle n m [race] [foe] [zoom] [tier]
    ///                               n fighters of 'tier' (novice..god, or a technique id for the bare trait)
    ///                               against m foes, the AI in charge
    ///   duel tierA tierB [raceA] [raceB] [zoom]
    ///                               two tiered enemies face each other, the AI in charge
    ///   lineup [race] [zoom] [hold] one unit of each tier in a row (untiered, Novice .. God, a bare-trait one): the auras
    ///   war spec spec [zoom]        two armies: race:tier*count,tier*count (e.g. human:novice*5,adept*1 orc:master)
    ///   demo [dist] [n] [race] [foe] [zoom] [wait]
    ///                               the Meteor Slam demo (a slam onto n foes 'dist' tiles away)
    ///   tempest [n] [race] [foe] [zoom] [wait]
    ///                               the Rising Tempest demo
    ///   promote tier                the latest performer becomes this tier (with its ceremony)
    ///   inspect / settings / tab    open the performer's unit window / the settings window / the Sakuga tab (then 'shot')
    ///   crab [race] [far]           the Kaiju Killer: a God (or a new 'race' God 'far' tiles away) answers a fresh Crabzilla
    ///   stack tier                  the performer holds every tier up to 'tier' at once (the merge rings)
    ///   situation id                stage a battle situation on the performer (last_stand, bodyguard, champions, rescue,
    ///                               rout, underdog, vengeance, lesson, challenge)
    ///   gauge [n]                   set the performer's God gauge (100 = a God ability is ready)
    ///   duelex id                   the next duel exchange (blade_lock, flurry, aerial, chase, ground_shaker, feint, circle,
    ///                               sky; Grandmaster+ pairs: skyfall, tectonic, sky_battle, mountain_breaker, relentless)
    ///   capture n [every] [w] [h] [delay] [folder]
    ///                               save n frames (cropped around the latest move; w=0: whole screen) to dev/[folder]
    ///   slowmo k / zoom z           moves play k times as fast / camera zoom (orthographic size, 10 = closest)
    ///   set key value               change a setting for this session (e.g. set cinematics 2)
    ///   shot / art                  one screenshot / every generated sprite as a png
    ///   layers / verbose            sorting layers / per-frame pose tracing
    ///   cleanup / quit              remove the demo units / close the game
    /// </summary>
    internal static class Dev
    {
        private static string _dir;
        private static float _poll;

        private static readonly List<Actor> _units = new List<Actor>();
        private static Actor _performer;
        private static Vector2 _spot;
        private static Vector2 _focus;
        private static float _wait = -1f;
        private static string _cue;
        private static Technique _tech;
        private static int _level;
        private static Actor _cueTarget;
        private static int _captureLeft, _captureEvery = 1, _captureTick, _captureIndex, _captureW = 640, _captureH = 420;
        private static bool _capturing;
        private static string _session;

        public static bool On => _dir != null;

        /// <summary>Per-frame pose tracing ('verbose' toggles it).</summary>
        public static bool Verbose;

        public static void Init()
        {
            string dir = Path.Combine(SakugaMod.Instance.GetDeclaration().FolderPath, "dev");
            if (!Directory.Exists(dir))
            {
                return;
            }
            _dir = dir;
            Log("dev tools on");
        }

        private static int _watches;

        /// <summary>Captures follow the latest move.</summary>
        public static void Watch(Vector2 focus)
        {
            if (_dir != null)
            {
                _focus = focus;
                _watches++;
            }
        }

        public static void Trace(string s)
        {
            if (_dir != null)
            {
                Log("f" + Time.frameCount + " " + s);
            }
        }

        public static void Log(string s)
        {
            // (the game's console draws each line as one UI text: a line of many thousands of characters
            // overflows its mesh and crashes the game, so a long one goes out in pieces)
            for (int i = 0; i < s.Length; i += 1500)
            {
                Debug.Log("[Sakuga] " + (i > 0 ? "... " : "") + s.Substring(i, Mathf.Min(1500, s.Length - i)));
            }
            if (_dir == null)
            {
                return;
            }
            try
            {
                File.AppendAllText(Path.Combine(_dir, "log.txt"), "[" + Time.realtimeSinceStartup.ToString("F2") + "] " + s + "\n");
            }
            catch
            {
                // logging never breaks the game
            }
        }

        public static void WorldCleared()
        {
            _units.Clear();
            _performer = null;
            _wait = -1f;
            _cue = null;
        }

        // ------------------------------------------------------------- polling --

        public static void Poll()
        {
            if (_dir == null)
            {
                return;
            }
            _poll -= Time.unscaledDeltaTime;
            if (_poll > 0f)
            {
                return;
            }
            _poll = 0.25f;
            string f = Path.Combine(_dir, "cmd.txt");
            if (!File.Exists(f))
            {
                return;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(f);
                File.Delete(f);
            }
            catch
            {
                return;
            }
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    Run(line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (Exception e)
                {
                    Log("command '" + line + "' failed: " + e);
                }
            }
        }

        /// <summary>
        /// Commands other files add (Dev.Commands["name"] = args => ...; args[0] is the name), so new
        /// systems bring their own dev tools without editing this file. Register from a [SakugaInit].
        /// </summary>
        internal static readonly Dictionary<string, Action<string[]>> Commands = new Dictionary<string, Action<string[]>>();

        /// <summary>The latest 'move' performer (null if none or dead).</summary>
        internal static Actor Performer => _performer != null && _performer.isAlive() ? _performer : null;

        /// <summary>Units the dev commands spawned this session (cleanup removes them).</summary>
        internal static List<Actor> Units => _units;

        internal static string Arg(string[] a, int i, string d)
        {
            return a.Length > i ? a[i] : d;
        }

        internal static float ArgF(string[] a, int i, float d)
        {
            return a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
        }

        /// <summary>Runs one command line as if typed (other dev tools script the demos with it).</summary>
        internal static void Exec(string line)
        {
            Run(line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        private static void Run(string[] a)
        {
            Log("> " + string.Join(" ", a));
            switch (a[0])
            {
                case "status":
                    Status();
                    break;
                case "stats":
                    Log("techniques: " + Brain.Stats());
                    break;
                case "population":
                    {
                        int units = 0, builds = 0;
                        foreach (Actor u in World.world.units.getSimpleList())
                        {
                            if (u != null && u.isAlive())
                            {
                                units++;
                            }
                        }
                        foreach (Building bd in World.world.buildings.getSimpleList())
                        {
                            if (bd != null && bd.isAlive())
                            {
                                builds++;
                            }
                        }
                        Log("population: units " + units + " buildings " + builds + " primordials " + Primordials.Living.Count + " world's end " + WorldsEnd.Active);
                        Dictionary<string, int> uk = new Dictionary<string, int>(), bk = new Dictionary<string, int>();
                        double now = World.world.getCurWorldTime();
                        int young = 0;
                        foreach (Actor u in World.world.units.getSimpleList())
                        {
                            if (u != null && u.isAlive())
                            {
                                uk.TryGetValue(u.asset.id, out int n);
                                uk[u.asset.id] = n + 1;
                                if (now - u.data.created_time < 60.0)
                                {
                                    young++;
                                }
                            }
                        }
                        foreach (Building bd in World.world.buildings.getSimpleList())
                        {
                            if (bd != null && bd.isAlive())
                            {
                                bk.TryGetValue(bd.asset.id, out int n);
                                bk[bd.asset.id] = n + 1;
                            }
                        }
                        StringBuilder ps = new StringBuilder("population kinds: units");
                        foreach (KeyValuePair<string, int> kv in uk)
                        {
                            ps.Append(" ").Append(kv.Key).Append(" ").Append(kv.Value);
                        }
                        ps.Append(" (born in the last minute ").Append(young).Append("); buildings");
                        foreach (KeyValuePair<string, int> kv in bk)
                        {
                            ps.Append(" ").Append(kv.Key).Append(" ").Append(kv.Value);
                        }
                        Log(ps.ToString());
                        Log("capture done: population");
                    }
                    break;
                case "census":
                    Census();
                    break;
                case "awaken":
                    {
                        Cleanup();
                        ScrollWindow.hideAllEvent(false);
                        World.world.console?.Hide();
                        WorldTile c = MakeArena(14);
                        if (c == null)
                        {
                            break;
                        }
                        string race = Arg(a, 1, "human");
                        _performer = Spawn(race, c, false);
                        for (int i = 0; i < 18; i++)
                        {
                            WorldTile t = World.world.GetTile(Mathf.Clamp(c.x + UnityEngine.Random.Range(-12, 13), 1, MapBox.width - 2), Mathf.Clamp(c.y + UnityEngine.Random.Range(-10, 11), 1, MapBox.height - 2));
                            if (t != null)
                            {
                                Spawn(i % 3 == 0 ? "orc" : race, t, false);
                            }
                        }
                        _awakenAt = Time.unscaledTime + 1.5f;
                        _focus = new Vector2(c.posV3.x, c.posV3.y + 2f);
                        World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
                        World.world.move_camera.setTargetZoom(ArgF(a, 2, 18f));
                        Log("awaken test at " + c.x + "," + c.y);
                    }
                    break;
                case "newmap":
                    Cleanup();
                    Config.customMapSize = Arg(a, 1, "standard");
                    Config.current_map_template = Arg(a, 2, "continent");
                    World.world.generateNewMap();
                    Log("newmap " + Config.customMapSize + " " + Config.current_map_template);
                    break;
                case "land":
                    {
                        int land = 0, waste = 0, burnt = 0, water = 0, lava = 0, trees = 0, builds = 0;
                        for (int y = 0; y < MapBox.height; y++)
                        {
                            for (int x = 0; x < MapBox.width; x++)
                            {
                                WorldTile t = World.world.GetTileSimple(x, y);
                                if (t == null || t.Type == null)
                                {
                                    continue;
                                }
                                if (t.Type.lava)
                                {
                                    lava++;
                                }
                                else if (t.Type.liquid)
                                {
                                    water++;
                                    continue;
                                }
                                land++;
                                if (t.top_type != null && t.top_type.wasteland)
                                {
                                    waste++;
                                }
                                if (t.burned_stages > 0)
                                {
                                    burnt++;
                                }
                            }
                        }
                        foreach (Building bd in World.world.buildings.getSimpleList())
                        {
                            if (bd != null && bd.isAlive())
                            {
                                builds++;
                                if (bd.asset != null && bd.asset.id.Contains("tree"))
                                {
                                    trees++;
                                }
                            }
                        }
                        Log("land: map " + MapBox.width + "x" + MapBox.height + " land " + land + " dead (wasteland) " + waste + " (" + (land > 0 ? 100 * waste / land : 0) + "%) burnt " + burnt
                            + " lava " + lava + " water " + water + " buildings " + builds + " trees " + trees);
                        Log("capture done: land");
                    }
                    break;
                case "bodies":
                    {
                        int dead = 0, ko = 0, stun = 0, frozen = 0, held = 0, tilted = 0, all = 0;
                        StringBuilder sb = new StringBuilder();
                        foreach (Actor u in World.world.units.getSimpleList())
                        {
                            if (u == null)
                            {
                                continue;
                            }
                            all++;
                            bool d = !u.isAlive(), k = u.is_unconscious, st = u.hasStatusStunned(), fr = u.hasStatus("frozen"), h = Moves.TryGet(u, out Move hm);
                            bool tilt = Mathf.Abs(Mathf.DeltaAngle(0f, u.current_rotation.z)) > 30f;
                            if (d) dead++;
                            if (k) ko++;
                            if (st) stun++;
                            if (fr) frozen++;
                            if (h) held++;
                            if (tilt) tilted++;
                            if ((d || k || tilt) && sb.Length < 1400)
                            {
                                sb.Append(" ").Append(u.asset.id).Append("#").Append(u.Sid()).Append(d ? " dead" : "").Append(k ? " ko" : "").Append(st ? " stun" : "")
                                    .Append(fr ? " frozen" : "").Append(tilt ? " rot" + u.current_rotation.z.ToString("F0") : "").Append(h ? " move:" + hm.GetType().Name : "")
                                    .Append(" hp ").Append(u.getHealth()).Append(";");
                            }
                        }
                        Log("bodies: units " + all + " dead-but-listed " + dead + " ko " + ko + " stunned " + stun + " frozen " + frozen + " in-move " + held + " tilted " + tilted + " |" + sb);
                        Log("capture done: bodies");
                    }
                    break;
                case "schoolaudit":
                    {
                        Form energy = Form.Beam | Form.Ki | Form.Projectile;
                        for (int si = (int)School.IronFist; si <= (int)School.HuntersWay; si++)
                        {
                            School sc = (School)si;
                            StringBuilder sb = new StringBuilder("schoolaudit " + sc + ":");
                            foreach (Technique t in Techniques.All)
                            {
                                if (t.PrimordialOnly || t.Team || (t.FormsAt(3) & energy) == 0)
                                {
                                    continue;
                                }
                                School[] aff = t.Affinity;
                                bool own = t.School == sc;
                                bool free = t.School == School.None && (aff == null || System.Array.IndexOf(aff, sc) >= 0);
                                if (own || free)
                                {
                                    sb.Append(" ").Append(t.Id).Append(own ? "" : aff == null ? "[ANY]" : "[aff]").Append(t.GodAbility ? "[god]" : "");
                                }
                            }
                            Log(sb.ToString());
                        }
                        Log("capture done: schoolaudit");
                    }
                    break;
                case "redrawall":
                    {
                        int n = 0;
                        for (int y = 0; y < MapBox.height; y++)
                        {
                            for (int x = 0; x < MapBox.width; x++)
                            {
                                WorldTile t = World.world.GetTileSimple(x, y);
                                if (t != null)
                                {
                                    World.world.setTileDirty(t);
                                    n++;
                                }
                            }
                        }
                        World.world.redrawMiniMap(true);
                        Log("redrawall " + n + " tiles");
                    }
                    break;
                case "tileat":
                    {
                        WorldTile t = World.world.GetTile((int)ArgF(a, 1, 0f), (int)ArgF(a, 2, 0f));
                        Log("tileat " + (t != null ? t.x + "," + t.y + " " + (t.Type != null ? t.Type.id : "-") + " top " + (t.top_type != null ? t.top_type.id : "-") : "none"));
                    }
                    break;
                case "overview":
                    {
                        Vector2 c = new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f);
                        _focus = c;
                        Camera cam = World.world.camera;
                        float z = Mathf.Max(MapBox.height * 0.55f, MapBox.width * 0.55f / Mathf.Max(0.5f, cam != null ? cam.aspect : 1.6f));
                        World.world.move_camera.focusOn(new Vector3(c.x, c.y, 0f));
                        World.world.move_camera.setTargetZoom(z);
                        if (cam != null)
                        {
                            cam.orthographicSize = z;
                        }
                        Log("overview zoom " + z);
                    }
                    break;
                case "who":
                    foreach (Actor p in Primordials.Living)
                    {
                        // (the living list is rebuilt every 0.5 s: one killed since is skipped, not read)
                        if (p != null && p.isAlive() && p.asset != null)
                        {
                            Log(Who(p));
                        }
                    }
                    Log("capture done: who");
                    break;
                case "close":
                    ScrollWindow.hideAllEvent(false);
                    Log("windows closed; window active = " + ScrollWindow.isWindowActive());
                    break;
                case "move":
                    MoveDemo(Arg(a, 1, "meteor_slam"), (int)ArgF(a, 2, 2f), (int)ArgF(a, 3, 5f), Arg(a, 4, "human"), Arg(a, 5, "skeleton"),
                        Arg(a, 6, "cluster"), ArgF(a, 7, 14f), ArgF(a, 8, 1.2f), (int)ArgF(a, 9, 0f), ParseTier(Arg(a, 10, "")));
                    break;
                case "demo":
                    MoveDemo("meteor_slam", 2, (int)ArgF(a, 2, 5f), Arg(a, 3, "human"), Arg(a, 4, "skeleton"), "far", ArgF(a, 5, 14f), ArgF(a, 6, 0.9f), 0);
                    break;
                case "tempest":
                    MoveDemo("rising_tempest", 2, (int)ArgF(a, 1, 3f), Arg(a, 2, "human"), Arg(a, 3, "skeleton"), "adjacent", ArgF(a, 4, 11f), ArgF(a, 5, 2.5f), 0);
                    break;
                case "battle":
                    Battle((int)ArgF(a, 1, 6f), (int)ArgF(a, 2, 20f), Arg(a, 3, "human"), Arg(a, 4, "skeleton"), ArgF(a, 5, 22f), Arg(a, 6, "adept"));
                    break;
                case "duel":
                    Duel(Arg(a, 1, "master"), Arg(a, 2, "master"), Arg(a, 3, "human"), Arg(a, 4, "orc"), ArgF(a, 5, 14f));
                    break;
                case "lineup":
                    Lineup(Arg(a, 1, "human"), ArgF(a, 2, 9f), ArgF(a, 3, 0f) > 0f);
                    break;
                case "war":
                    War(Arg(a, 1, "human:novice*6,adept*2"), Arg(a, 2, "orc:novice*6,adept*2"), ArgF(a, 3, 24f));
                    break;
                // "primordials human orc 30": two enemy Primordials (World's End) with bystanders round them
                // "primordialgods 4 human orc 22": a Primordial against N Gods
                // "primordialarmy human orc 30 22": a Primordial against an army (some Gods, Grandmasters, the rest lesser)
                case "primordials":
                case "primordialgods":
                case "primordialarmy":
                    PrimordialTest(a[0], a);
                    break;
                case "inspect":
                    if (_performer != null && _performer.isAlive())
                    {
                        ActionLibrary.openUnitWindow(_performer);
                        Log("unit window: " + _performer.getName() + " (" + Tiers.Of(_performer) + ")");
                    }
                    break;
                case "settings":
                    Settings.OpenWindow();
                    Log("settings window opened");
                    break;
                case "tab":
                    try
                    {
                        PowerButtonSelector.instance.unselectAll();
                        foreach (PowersTab t in UnityEngine.Object.FindObjectsOfType<PowersTab>(true))
                        {
                            if (t.name == Powers.Tab)
                            {
                                t.showTab(null);
                                Log("tab shown");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log("tab failed: " + e.Message);
                    }
                    break;
                case "crab":
                    Log(KaijuKiller.DevDemo(Arg(a, 1, "human"), ArgF(a, 2, 30f)));
                    break;
                case "stack":
                    Log(Ceremonies.DevStack(_performer, ParseTier(Arg(a, 1, "god"))));
                    break;
                case "situation":
                    Log(Situations.Force(Arg(a, 1, ""), _performer));
                    break;
                case "gauge":
                    if (_performer != null && _performer.isAlive())
                    {
                        Fighters.Get(_performer).Gauge = ArgF(a, 1, 100f);
                        Log("gauge " + Fighters.Get(_performer).Gauge);
                    }
                    break;
                case "duelex":
                    Duels.Force(Arg(a, 1, ""));
                    Log("next duel exchange: " + Arg(a, 1, ""));
                    break;
                case "promote":
                    if (_performer != null && _performer.isAlive())
                    {
                        _focus = _performer.current_position + new Vector2(0f, 1.5f);
                        World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
                        Moves.Hold(_performer);   // stays put between ceremonies
                        Moves.ReleaseHeld();
                        Progression.Promote(_performer, ParseTier(Arg(a, 1, "master")), false);
                        Log("promoted to " + Tiers.Of(_performer));
                    }
                    break;
                case "capture":
                    StartCapture((int)ArgF(a, 1, 120f), (int)ArgF(a, 2, 1f), (int)ArgF(a, 3, 640f), (int)ArgF(a, 4, 420f), ArgF(a, 5, 0f), Arg(a, 6, "frames"));
                    break;
                case "slowmo":
                    Clock.Scale = Mathf.Clamp(ArgF(a, 1, 1f), 0.02f, 4f);
                    Log("slowmo " + Clock.Scale);
                    break;
                case "zoom":
                    World.world.move_camera.setTargetZoom(ArgF(a, 1, 14f));
                    break;
                case "set":
                    Set(Arg(a, 1, ""), Arg(a, 2, ""));
                    break;
                case "shot":
                    ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "shot.png"));
                    Log("shot saved");
                    break;
                case "layers":
                    Layers();
                    break;
                case "tabs":
                    DumpTabs();
                    break;
                case "resetbtn":
                    {
                        // clicks the settings window's Reset to defaults button (the window must be open)
                        GameObject btn = GameObject.Find("CombatGodsReset");
                        UnityEngine.UI.Button ub = btn != null ? btn.GetComponent<UnityEngine.UI.Button>() : null;
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        ub?.onClick.Invoke();
                        Log("reset button " + (ub != null ? "clicked" : "not found") + " in " + sw.ElapsedMilliseconds + " ms: god cap " + Settings.GodCap
                            + ", aura god " + Settings.Aura[6] + ", power " + Settings.Power + ", star novice " + Settings.Star[1]);
                    }
                    break;
                case "cfgset":
                    {
                        // a setting changed the way the settings window does it (no callback)
                        NeoModLoader.api.ModConfigItem item = Settings.FindItem(Arg(a, 1, ""));
                        item?.SetValue(Arg(a, 2, ""), true);
                        Log("cfgset " + Arg(a, 1, "") + " = " + (item != null ? item.GetValue() : "no such item"));
                    }
                    break;
                case "hsprobe":
                    {
                        var parts = new List<string>();
                        foreach (KeyValuePair<string, int> kv in HeavenSplitterTech.Probe)
                        {
                            parts.Add(kv.Key + " " + kv.Value);
                        }
                        parts.Sort();
                        Log("hs gates: " + string.Join(" | ", parts));
                        parts.Clear();
                        foreach (KeyValuePair<string, int> kv in Brain.ChoiceProbe)
                        {
                            parts.Add(kv.Key + " " + kv.Value);
                        }
                        parts.Sort();
                        Log("hs choices: " + string.Join(" | ", parts));
                        HeavenSplitterTech.Probe.Clear();
                        Brain.ChoiceProbe.Clear();
                    }
                    break;
                case "stunui":
                    // what the trait editor does to a unit after its traits change, on every demo unit
                    foreach (Actor u in _units)
                    {
                        if (u != null && u.isAlive())
                        {
                            u.makeStunnedFromUI();
                            Log("stunui " + Tiers.Of(u) + ": stunned " + u.hasStatusStunned());
                        }
                    }
                    break;
                case "probe":
                    _probeLeft = ArgF(a, 1, 30f);
                    _probeSeen.Clear();
                    Log("probing big sprites for " + _probeLeft + " s");
                    break;
                case "cleanup":
                    Cleanup();
                    break;
                case "art":
                    DumpArt();
                    break;
                case "ui":
                    Config.ui_main_hidden = !Config.ui_main_hidden;
                    Log("ui hidden " + Config.ui_main_hidden);
                    break;
                case "verbose":
                    Verbose = !Verbose;
                    Log("verbose " + Verbose);
                    break;
                case "console":
                    World.world.console?.Hide();
                    break;
                case "quit":
                    Application.Quit();
                    break;
                default:
                    if (Commands.TryGetValue(a[0], out Action<string[]> extra))
                    {
                        extra(a);
                    }
                    else
                    {
                        Log("unknown command");
                    }
                    break;
            }
        }

        private static void Status()
        {
            if (World.world == null)
            {
                Log("no world");
                return;
            }
            Camera cam = World.world.camera;
            Log("map " + MapBox.width + "x" + MapBox.height + " units " + World.world.units.Count
                + " paused " + World.world.isPaused() + " window " + ScrollWindow.isWindowActive()
                + " cam " + (cam != null ? cam.transform.position + " size " + cam.orthographicSize : "none")
                + " moves " + Moves.Count + " fx " + Fx.Live + " fighters " + Fighters.Count + " silhouette " + Fx.HasSilhouette
                + " slams " + MeteorSlam.Started + "/" + MeteorSlam.Landed + " combos " + RisingTempest.Started + "/" + RisingTempest.Finished_
                + " speed " + Config.time_scale_asset.id + " frozen " + Cinema.Frozen + " peak fx " + _peakFx
                + " fps " + (1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0"));
        }

        private static string Who(Actor p)
        {
            Fighter f = Fighters.Get(p);
            StringBuilder sb = new StringBuilder("who: ");
            sb.Append(p.asset.id).Append("#").Append(p.Sid()).Append(" hp ").Append(Combat.HpPct(p).ToString("F2"))
                .Append(" fighting ").Append(p.isFighting())
                .Append(" target ").Append(p.attack_target is Actor at ? at.asset.id + "#" + at.Sid() : p.attack_target != null ? "building" : "none")
                .Append(" inCombat ").Append(f != null && f.InCombat).Append(" busy ").Append(Moves.Busy(p)).Append(" committed ").Append(Moves.Committed(p))
                .Append(" moves ").Append(Moves.Count).Append(" held ").Append(WorldsEnd.Holds(p));
            if (f != null)
            {
                sb.Append(" scene ").Append(Director.InScene(p)).Append(" canPerform ").Append(Moves.CanPerform(p))
                    .Append(" sinceEnd ").Append((Fighter.Now - f.LastEnd).ToString("F1")).Append(" thinkIn ").Append(f.ThinkIn.ToString("F2"))
                    .Append(" move ").Append(Moves.TryGet(p, out Move cm) ? cm.GetType().Name + (cm.Tech != null ? ":" + cm.Tech.Id : "") : "none");
            }
            if (f != null)
            {
                foreach (Technique t in Techniques.All)
                {
                    if (t.PrimordialOnly)
                    {
                        int lv = Techniques.Level(p, t, f.Tier);
                        sb.Append(" ").Append(t.Id.Replace("primordial_", "")).Append(":L").Append(lv).Append(f.Ready(t) ? "r" : "c").Append(lv > 0 && Mana.Has(p, t, lv, false) ? "m" : "-");
                    }
                }
            }
            return sb.ToString();
        }

        private static void Census()
        {
            StringBuilder sb = new StringBuilder("census:");
            for (int i = 1; i <= Tiers.Count; i++)
            {
                sb.Append(" ").Append((Tier)i).Append(" ").Append(Progression.Census((Tier)i)).Append("/").Append(Progression.Cap((Tier)i) == int.MaxValue ? "-" : Progression.Cap((Tier)i).ToString());
            }
            Log(sb.ToString());
        }

        private static void Set(string key, string value)
        {
            float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v);
            // kept over the config for the session (Settings re-reads the config every second)
            if (!Settings.Override(key, v))
            {
                Log("unknown setting");
                return;
            }
            Log("set " + key + " = " + v);
        }

        private static float _probeLeft, _probeTick;
        private static readonly HashSet<string> _probeSeen = new HashSet<string>();

        /// <summary>Logs every new kind of large sprite on screen (what draws a mystery square?).</summary>
        private static void Probe()
        {
            _probeLeft -= Time.unscaledDeltaTime;
            _probeTick -= Time.unscaledDeltaTime;
            if (_probeTick > 0f)
            {
                return;
            }
            _probeTick = 0.1f;
            foreach (Renderer rr in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (rr is SpriteRenderer || rr == null || !rr.enabled || !rr.isVisible)
                {
                    continue;
                }
                Vector3 bs = rr.bounds.size;
                if (bs.x < 2.5f || bs.y < 2.5f || bs.x > 90f)
                {
                    continue;
                }
                string k2 = rr.GetType().Name + "|" + rr.gameObject.name + "|" + (rr.sharedMaterial != null ? rr.sharedMaterial.name : "-");
                if (_probeSeen.Add(k2))
                {
                    Log("probe: " + k2 + " parent " + (rr.transform.parent != null ? rr.transform.parent.name : "-") + " size " + bs.x.ToString("F1") + "x" + bs.y.ToString("F1") + " at " + rr.transform.position);
                }
            }
            foreach (SpriteRenderer r in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (r == null || !r.enabled || !r.isVisible || r.sprite == null || r.color.a < 0.05f)
                {
                    continue;
                }
                Vector3 size = r.bounds.size;
                if (size.x < 2.5f || size.x > 90f || size.y < 2.5f)
                {
                    continue;
                }
                Sprite sp = r.sprite;
                string key = r.gameObject.name + "|" + sp.name + "|" + sp.texture.name + "|" + (int)sp.rect.width + "x" + (int)sp.rect.height + "|" + r.sortingLayerName;
                if (_probeSeen.Add(key))
                {
                    Transform par = r.transform.parent;
                    Log("probe: " + key + " parent " + (par != null ? par.name : "-") + " size " + size.x.ToString("F1") + "x" + size.y.ToString("F1")
                        + " at " + r.transform.position + " color " + r.color);
                }
            }
        }

        /// <summary>The tab buttons' layout (where the game and NML put them).</summary>
        private static void DumpTabs()
        {
            Transform parent = CanvasMain.instance != null ? CanvasMain.instance.canvas_ui.transform.Find("CanvasBottom/BottomElements/BottomElementsMover/TabsButtons") : null;
            if (parent == null)
            {
                Log("no TabsButtons");
                return;
            }
            Log("tabs parent " + parent.name + " < " + (parent.parent != null ? parent.parent.name : "-"));
            foreach (Component comp in parent.GetComponents<Component>())
            {
                string extra = "";
                if (comp is UnityEngine.UI.GridLayoutGroup g)
                {
                    extra = " constraint " + g.constraint + " count " + g.constraintCount + " cell " + g.cellSize + " spacing " + g.spacing + " start " + g.startCorner + " axis " + g.startAxis + " align " + g.childAlignment + " pad " + g.padding;
                }
                Log("  component " + comp.GetType().FullName + extra);
            }
            RectTransform pr = parent as RectTransform;
            if (pr != null)
            {
                Log("  bar rect " + pr.rect + " size " + pr.sizeDelta + " pos " + pr.localPosition);
            }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform t = parent.GetChild(i);
                RectTransform r = t as RectTransform;
                Transform icon = t.Find("Icon");
                RectTransform ir = icon as RectTransform;
                Log("  " + i + " " + t.name + " active " + t.gameObject.activeSelf + " pos " + t.localPosition.ToString("F2") + (r != null ? " size " + r.sizeDelta.ToString("F2") + " anchor " + r.anchorMin.ToString("F2") + "/" + r.anchorMax.ToString("F2") + " pivot " + r.pivot.ToString("F2") : "")
                    + " scale " + t.localScale.ToString("F2") + (ir != null ? " icon " + ir.sizeDelta.ToString("F2") + " at " + ir.localPosition.ToString("F2") : ""));
            }
        }

        private static void Layers()
        {
            StringBuilder sb = new StringBuilder("sorting layers:");
            foreach (SortingLayer l in SortingLayer.layers)
            {
                sb.Append(" ").Append(l.name).Append("(").Append(l.value).Append(")");
            }
            Log(sb.ToString());
        }

        public static Tier ParseTier(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "novice": case "1": return Tier.Novice;
                case "adept": case "2": return Tier.Adept;
                case "expert": case "3": return Tier.Expert;
                case "master": case "4": return Tier.Master;
                case "grandmaster": case "gm": case "5": return Tier.Grandmaster;
                case "god": case "6": return Tier.God;
                default: return Tier.None;
            }
        }

        // ---------------------------------------------------------------- demos --

        internal static Actor Spawn(string race, WorldTile t, bool hold)
        {
            Actor u = World.world.units.spawnNewUnit(race, t, false, false, 0f, null, false, true);
            if (u != null)
            {
                _units.Add(u);
                GiveTestKingdom(u, race);
                if (hold)
                {
                    Moves.Hold(u);
                }
            }
            return u;
        }

        // one kingdom per animal people for the demos, all at war with each other
        private static readonly Dictionary<string, Kingdom> _testKingdoms = new Dictionary<string, Kingdom>();

        /// <summary>
        /// A city-less animal people spawns into the game's shared "neutral animals" (only humans, elves, orcs
        /// and dwarves have nomad kingdoms), so cat folk and bear folk would never be foes in a demo: each
        /// such race gets a kingdom of its own, at war with every other one.
        /// </summary>
        private static void GiveTestKingdom(Actor u, string race)
        {
            try
            {
                if (!Peoples.Is(u.asset) || u.isKingdomCiv() || u.asset.kingdom_id_wild != "neutral_animals")
                {
                    return;
                }
                if (_testKingdoms.TryGetValue(race, out Kingdom k) && k != null && k.isAlive())
                {
                    u.joinKingdom(k);
                    return;
                }
                k = World.world.kingdoms.makeNewCivKingdom(u, null, false);
                _testKingdoms[race] = k;
                foreach (KeyValuePair<string, Kingdom> other in _testKingdoms)
                {
                    if (other.Value != null && other.Value != k && other.Value.isAlive() && !World.world.wars.isInWarWith(k, other.Value))
                    {
                        World.world.wars.newWar(k, other.Value, WarTypeLibrary.normal);
                    }
                }
            }
            catch (Exception e)
            {
                Log("test kingdom for " + race + " failed: " + e.Message);
            }
        }

        /// <summary>A performer that knows 'tech' at 'level', foes in a layout, all held until the cue.</summary>
        private static void MoveDemo(string techId, int level, int n, string race, string foe, string layout, float zoom, float wait, int allies, Tier tierOverride = Tier.None)
        {
            Technique tech = Techniques.Get(techId);
            if (tech == null)
            {
                Log("no technique " + techId + "; known: " + string.Join(",", TechIds()));
                return;
            }
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            WorldTile c = MakeArena(12);
            if (c == null)
            {
                Log("no clear land for a demo");
                return;
            }
            WorldTile from = World.world.GetTile(c.x - 4, c.y);
            _performer = Spawn(race, from, true);
            if (_performer == null)
            {
                Log("could not spawn " + race);
                return;
            }
            Tier tier = (Tier)Mathf.Clamp((int)tech.Entry + level - 1, 1, 6);
            if (tech.GodAbility)
            {
                tier = Tier.God;
            }
            if (tierOverride != Tier.None)
            {
                tier = tierOverride;   // e.g. a God performing a level III technique
            }
            Progression.Promote(_performer, tier, false);
            if (tech.School != School.None && Schools.Trait(tech.School) != null)
            {
                _performer.addTrait(Schools.Trait(tech.School), true);   // it walks the technique's school
            }
            Unstar(_performer);
            Vector2 p0 = new Vector2(from.posV3.x, from.posV3.y);
            List<Vector2> spots = Layout(layout, n, p0);
            // team techniques: friends stand around the first enemy (the far side first), others behind the performer
            Vector2 around = spots.Count > 0 ? spots[0] : p0 + Vector2.right * 3f;
            for (int i = 0; i < allies; i++)
            {
                Vector2 at;
                if (tech.Team)
                {
                    float[] angles = { 0f, 1.6f, -1.6f, 0.8f, -0.8f, 2.3f, -2.3f, 3.1f, 1.2f, -1.2f };
                    float ang = angles[i % angles.Length];
                    at = around + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 2.4f;
                }
                else
                {
                    at = new Vector2(c.x - 5 - i % 2, c.y - 2 + i * 2 - (i / 2) * 3);
                }
                WorldTile t = World.world.GetTile(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y)) ?? from;
                Actor ally = Spawn(race, t, true);
                if (ally != null && (i < 2 || tech.Team))
                {
                    Progression.Promote(ally, (Tier)Mathf.Max(1, (int)tier - (i < 2 ? 0 : 1)), false);
                    Unstar(ally);
                }
            }
            Actor first = null;
            foreach (Vector2 s in spots)
            {
                WorldTile t = World.world.GetTile(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y)) ?? c;
                Actor u = Spawn(foe, t, true);
                if (u != null)
                {
                    first ??= u;
                }
            }
            _tech = tech;
            _level = level;
            _cueTarget = first;
            _spot = spots.Count > 0 ? spots[0] : p0 + Vector2.right * 4f;
            _focus = Vector2.Lerp(p0, _spot, 0.5f) + new Vector2(0f, 2.5f);
            World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            _cue = "move";
            _wait = wait;
            Log("move demo: " + techId + " " + Techniques.Roman(level) + " by a " + tier + " " + race + " vs " + spots.Count + " " + foe + " (" + layout + ")");
        }

        /// <summary>Demo units are not favourites (the game's big star would cover the action in captures).</summary>
        private static void Unstar(Actor u)
        {
            if (u != null && u.data != null)
            {
                u.data.favorite = false;
            }
        }

        private static List<Vector2> Layout(string layout, int n, Vector2 p0)
        {
            List<Vector2> list = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                Vector2 s;
                switch (layout)
                {
                    case "adjacent":
                        s = p0 + new Vector2(1f + i / 3, (i % 3 - 1) * 1.1f);
                        break;
                    case "line":
                        s = p0 + new Vector2(2f + i * 1.3f, 0f);
                        break;
                    case "lone":
                        s = p0 + new Vector2(3.5f + i * 6f, i * 2f);
                        break;
                    case "ring":
                        {
                            float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 2f;
                            s = p0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.3f;
                            break;
                        }
                    case "scatter":
                        {
                            float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 1.6f - 0.8f * Mathf.PI;
                            s = p0 + new Vector2(5f + Mathf.Cos(a) * 3.5f, Mathf.Sin(a) * 3.5f);
                            break;
                        }
                    case "far":
                        s = p0 + new Vector2(8f + i % 3, i / 3 * 2 - (i % 2 == 0 ? 1 : 0));
                        break;
                    default:   // cluster
                        s = p0 + new Vector2(5.5f + i % 3 * 0.9f, (i / 3) * 1.1f - 0.9f);
                        break;
                }
                list.Add(s);
            }
            return list;
        }

        private static IEnumerable<string> TechIds()
        {
            foreach (Technique t in Techniques.All)
            {
                yield return t.Id;
            }
        }

        /// <summary>A real fight: n fighters against m foes, the AI decides.</summary>
        private static void Battle(int n, int m, string race, string foe, float zoom, string tierOrTech)
        {
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            WorldTile c = MakeArena(12);
            if (c == null)
            {
                return;
            }
            Tier tier = ParseTier(tierOrTech);
            Technique tech = tier == Tier.None ? Techniques.Get(tierOrTech) : null;
            for (int i = 0; i < n; i++)
            {
                WorldTile t = World.world.GetTile(c.x - 8 + i % 3, c.y - 3 + i / 3 * 2) ?? c;
                Actor u = Spawn(race, t, false);
                if (u == null)
                {
                    continue;
                }
                if (tier != Tier.None)
                {
                    Progression.Promote(u, tier, false);
                    Unstar(u);
                }
                else if (tech != null)
                {
                    u.addTrait(tech.TraitId);
                }
                _performer ??= u;
            }
            for (int i = 0; i < m; i++)
            {
                WorldTile t = World.world.GetTile(c.x + 5 + i % 5, c.y - 4 + i / 5 * 2) ?? c;
                Spawn(foe, t, false);
            }
            _focus = new Vector2(c.posV3.x, c.posV3.y + 2f);
            _spot = new Vector2(c.posV3.x + 6f, c.posV3.y);
            World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            _peakFx = 0;
            Log("battle: " + n + " " + race + " (" + tierOrTech + ") vs " + m + " " + foe);
        }

        private static void Duel(string a, string b, string raceA, string raceB, float zoom)
        {
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            WorldTile c = MakeArena(10);
            if (c == null)
            {
                return;
            }
            Actor x = Spawn(raceA, World.world.GetTile(c.x - 4, c.y), false);
            Actor y = Spawn(raceB, World.world.GetTile(c.x + 4, c.y), false);
            if (x == null || y == null)
            {
                Log("could not spawn the duellists");
                return;
            }
            Progression.Promote(x, ParseTier(a), false);
            Progression.Promote(y, ParseTier(b), false);
            Unstar(x);
            Unstar(y);
            _performer = x;
            x.addAggro(y);
            y.addAggro(x);
            _focus = new Vector2(c.posV3.x, c.posV3.y + 2f);
            World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Log("duel: " + a + " " + raceA + " (" + x.Sid() + ") vs " + b + " " + raceB + " (" + y.Sid() + ") foes " + (x.kingdom != null && y.kingdom != null && x.areFoes(y)));
        }

        /// <summary>Two armies from specs like "human:novice*5,adept*1,none*10".</summary>
        private static void War(string specA, string specB, float zoom)
        {
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            int most = Mathf.Max(ArmySize(specA), ArmySize(specB));
            _cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(most / 2f)), 4, 14);
            WorldTile c = MakeArena(Mathf.Clamp(Mathf.CeilToInt(most / (float)_cols) + 4, 14, 60));
            if (c == null)
            {
                return;
            }
            _armySum = Vector2.zero;
            _armyN = 0;
            int na = Army(specA, c, -1);
            int nb = Army(specB, c, 1);
            _focus = (_armyN > 0 ? _armySum / _armyN : new Vector2(c.posV3.x, c.posV3.y)) + new Vector2(0f, 1f);
            Frame(_focus, zoom);
            _peakFx = 0;
            Log("war: " + specA + " (" + na + ") vs " + specB + " (" + nb + ")");
        }

        /// <summary>One unit of every tier side by side (untiered, Novice .. God, then one with a single move trait).</summary>
        private static void Lineup(string race, float zoom, bool hold)
        {
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            WorldTile c = MakeArena(10);
            if (c == null)
            {
                return;
            }
            for (int t = 0; t <= Tiers.Count + 1; t++)
            {
                WorldTile tile = World.world.GetTile(c.x - 7 + t * 2, c.y) ?? c;
                Actor u = Spawn(race, tile, hold);
                if (u == null)
                {
                    continue;
                }
                if (t >= 1 && t <= Tiers.Count)
                {
                    Progression.Promote(u, (Tier)t, false);
                    Unstar(u);
                }
                else if (t > Tiers.Count)
                {
                    ActorTrait tr = Techniques.Trait(Techniques.Get("flash_step"));
                    if (tr != null)
                    {
                        u.addTrait(tr);
                    }
                }
                _performer ??= u;
            }
            _focus = new Vector2(c.posV3.x + 1f, c.posV3.y + 1f);
            Frame(_focus, zoom);
            Log("lineup: none, novice .. god, flash step trait (" + race + ")");
        }

        private static int _cols = 4;

        private static int ArmySize(string spec)
        {
            string[] parts = spec.Split(':');
            int n = 0;
            if (parts.Length < 2)
            {
                return 0;
            }
            foreach (string item in parts[1].Split(','))
            {
                string[] tn = item.Split('*');
                n += tn.Length > 1 && int.TryParse(tn[1], out int v) ? v : 1;
            }
            return n;
        }

        private static int Army(string spec, WorldTile c, int side)
        {
            int rows = Mathf.CeilToInt(ArmySize(spec) / (float)_cols);
            string[] parts = spec.Split(':');
            string race = parts[0];
            int count = 0;
            if (parts.Length < 2)
            {
                return 0;
            }
            foreach (string item in parts[1].Split(','))
            {
                string[] tn = item.Split('*');
                Tier tier = ParseTier(tn[0]);
                int k = tn.Length > 1 && int.TryParse(tn[1], out int v) ? v : 1;
                for (int i = 0; i < k; i++, count++)
                {
                    WorldTile t = World.world.GetTile(c.x + side * (6 + count % _cols), c.y - Mathf.Max(6, rows) + count / _cols * 2) ?? c;
                    Actor u = Spawn(race, t, false);
                    if (u != null)
                    {
                        _armySum += u.current_position;
                        _armyN++;
                    }
                    if (u != null && tier != Tier.None)
                    {
                        Progression.Promote(u, tier, false);
                        Unstar(u);
                    }
                    if (u != null && side < 0)
                    {
                        _performer ??= u;
                    }
                }
            }
            return count;
        }

        private static int _peakFx;
        private static Vector2 _armySum;
        private static int _armyN;

        /// <summary>A clean meadow for demos, always in the same place: no buildings, trees, fire or water.</summary>
        private static int _arena;

        /// <summary>
        /// Hooks for a director (the trailer's): where the next arena goes (null: the four meadows), and how
        /// each of its tiles is laid once buildings and fire are cleared (tile, dx, dy, half; null: meadow).
        /// </summary>
        internal static Func<int, WorldTile> ArenaPicker;
        internal static Action<WorldTile, int, int, int> ArenaGround;

        private static void PrimordialTest(string kind, string[] a)
        {
            Cleanup();
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            WorldTile c = MakeArena(14);
            if (c == null || Primordials.Trait == null)
            {
                return;
            }
            Actor Make(string race, int dx, int dy, Tier tier)
            {
                WorldTile t = World.world.GetTile(Mathf.Clamp(c.x + dx, 1, MapBox.width - 2), Mathf.Clamp(c.y + dy, 1, MapBox.height - 2));
                Actor u = t != null ? Spawn(race, t, false) : null;
                if (u != null && tier != Tier.None)
                {
                    Progression.Promote(u, tier, false);
                    Unstar(u);
                }
                return u;
            }
            if (kind == "primordials")
            {
                string ra = Arg(a, 1, "human"), rb = Arg(a, 2, "orc");
                string force = Arg(a, 4, "");
                if (force.Length > 0)
                {
                    WorldsEnd.ForceFirst = new List<string>(force.Split(','));
                }
                Actor x = Make(ra, -9, 0, Tier.None), y = Make(rb, 9, 0, Tier.None);
                x?.addTrait(Primordials.Trait, true);
                y?.addTrait(Primordials.Trait, true);
                for (int i = 0; i < 24; i++)
                {
                    Make(i % 2 == 0 ? ra : rb, UnityEngine.Random.Range(-13, 14), UnityEngine.Random.Range(-13, 14), i % 6 == 0 ? Tier.Master : Tier.None);
                }
                _performer = x;
            }
            else if (kind == "primordialgods")
            {
                int n = Mathf.Clamp((int)ArgF(a, 1, 4f), 1, 10);
                string rp = Arg(a, 2, "human"), rg = Arg(a, 3, "orc");
                Actor x = Make(rp, -6, 0, Tier.None);
                x?.addTrait(Primordials.Trait, true);
                for (int i = 0; i < n; i++)
                {
                    float ang = i * Mathf.PI * 2f / n;
                    Actor g = Make(rg, 4 + (int)(Mathf.Cos(ang) * 4f), (int)(Mathf.Sin(ang) * 4f), Tier.God);
                    if (g != null && x != null)
                    {
                        g.addAggro(x);
                        x.addAggro(g);
                    }
                }
                _performer = x;
            }
            else
            {
                string rp = Arg(a, 1, "human"), rr = Arg(a, 2, "orc");
                int n = Mathf.Clamp((int)ArgF(a, 3, 30f), 4, 120);
                Actor x = Make(rp, -10, 0, Tier.None);
                x?.addTrait(Primordials.Trait, true);
                for (int i = 0; i < n; i++)
                {
                    Tier t = i < 2 ? Tier.God : i < 5 ? Tier.Grandmaster : i < 10 ? Tier.Master : i < 16 ? Tier.Expert : Tier.Novice;
                    Actor u = Make(rr, UnityEngine.Random.Range(0, 13), UnityEngine.Random.Range(-10, 11), t);
                    if (u != null && x != null)
                    {
                        u.addAggro(x);
                    }
                }
                _performer = x;
            }
            _focus = new Vector2(c.posV3.x, c.posV3.y + 2f);
            World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));
            World.world.move_camera.setTargetZoom(ArgF(a, kind == "primordials" ? 3 : 4, 26f));
            Log("primordial test: " + kind + " at " + c.x + "," + c.y + " (living primordials " + Primordials.Living.Count + ")");
        }

        private static WorldTile MakeArena(int half)
        {
            // four meadows in turn: corpses, arrows and fires of the last demo stay behind
            int ax = (_arena & 1) == 0 ? MapBox.width / 4 : MapBox.width * 3 / 4;
            int ay = (_arena & 2) == 0 ? MapBox.height / 4 : MapBox.height * 3 / 4;
            _arena = (_arena + 1) & 3;
            WorldTile c = ArenaPicker?.Invoke(half) ?? World.world.GetTile(Mathf.Clamp(ax, half + 12, MapBox.width - half - 13), Mathf.Clamp(ay, half + 12, MapBox.height - half - 13));
            if (c == null)
            {
                return null;
            }
            int n = 0;
            for (int dx = -half - 10; dx <= half + 10; dx++)
            {
                for (int dy = -half - 6; dy <= half + 12; dy++)
                {
                    WorldTile t = World.world.GetTile(c.x + dx, c.y + dy);
                    if (t == null)
                    {
                        continue;
                    }
                    if (t.hasBuilding())
                    {
                        t.building.removeBuildingFinal();
                    }
                    t.stopFire();
                    t.removeBurn();
                    if (ArenaGround != null)
                    {
                        ArenaGround(t, dx, dy, half);
                    }
                    else
                    {
                        MapAction.terraformTile(t, TileLibrary.soil_low, TopTileLibrary.grass_low);
                    }
                    n++;
                }
            }
            List<Actor> near = new List<Actor>(Finder.getUnitsFromChunk(c, 2, half + 8));
            foreach (Actor u in near)
            {
                if (u != null && u.isAlive() && !_units.Contains(u))
                {
                    u.die(true, AttackType.Other, false, false);
                }
            }
            // no leftovers from the previous demo: decals, scars, effects, settlements its units founded
            Fx.Clear();
            LineScar.ClearAll();
            _arenaCenter = c;
            _arenaHalf = half + 10;
            try
            {
                foreach (City city in World.world.cities.list.ToArray())
                {
                    WorldTile ct = city?.getTile();
                    if (ct != null && Mathf.Abs(ct.x - c.x) <= half + 24 && Mathf.Abs(ct.y - c.y) <= half + 24)
                    {
                        city.destroyCity();
                    }
                }
            }
            catch (Exception e)
            {
                Log("city cleanup: " + e.Message);
            }
            Log("meadow of " + n + " tiles at " + c.x + "," + c.y);
            return c;
        }

        private static void Cleanup()
        {
            foreach (Actor u in _units)
            {
                if (u != null && !u.isRekt() && u.isAlive())
                {
                    u.die(true, AttackType.Other, false, false);
                }
            }
            _units.Clear();
            _performer = null;
            _wait = -1f;
            _cue = null;
            Moves.ReleaseHeld();
        }

        private static WorldTile _arenaCenter;
        private static int _arenaHalf;
        private static float _fireCheck;

        private static float _awakenAt = -1f;

        public static void Update()
        {
            if (_awakenAt > 0f && Time.unscaledTime >= _awakenAt)
            {
                _awakenAt = -1f;
                if (_performer != null && _performer.isAlive() && Primordials.Trait != null)
                {
                    _performer.addTrait(Primordials.Trait, true);
                    Log("awaken: " + _performer.getName() + " given the Primordial trait");
                }
            }
            if (_probeLeft > 0f)
            {
                Probe();
            }
            if (_dir != null && Fx.Live > _peakFx)
            {
                _peakFx = Fx.Live;
            }
            // demo meadows don't burn down (torch throwers): put fires out once a second
            if (_dir != null && _arenaCenter != null && Settings.Destruction == 0)
            {
                _fireCheck -= Time.unscaledDeltaTime;
                if (_fireCheck <= 0f)
                {
                    _fireCheck = 1f;
                    for (int dx = -_arenaHalf; dx <= _arenaHalf; dx++)
                    {
                        for (int dy = -_arenaHalf; dy <= _arenaHalf; dy++)
                        {
                            WorldTile t = World.world.GetTile(_arenaCenter.x + dx, _arenaCenter.y + dy);
                            if (t != null && t.isOnFire())
                            {
                                t.stopFire();
                            }
                        }
                    }
                }
            }
            if (_dir == null || _wait < 0f)
            {
                return;
            }
            _wait -= Time.unscaledDeltaTime;
            if (_wait > 0f)
            {
                return;
            }
            _wait = -1f;
            Moves.ReleaseHeld();
            if (_performer == null || !_performer.isAlive())
            {
                Log("performer gone");
                return;
            }
            if (_cue == "move" && _tech != null)
            {
                Actor target = _cueTarget != null && _cueTarget.isAlive() ? _cueTarget : null;
                int watches = _watches;
                bool ok = Brain.Force(_performer, _tech, _level, target, target != null ? (Vector2?)null : _spot);
                if (ok)
                {
                    Fighters.Get(_performer).SetCooldown(_tech, _tech.CooldownFor(_performer, _level));   // one cast per demo
                }
                Log(_tech.Id + " " + Techniques.Roman(_level) + " started: " + ok + (target != null ? " on " + target.asset.id + " hp " + target.getHealth() : ""));
                if (_watches == watches && Moves.TryGet(_performer, out Move m))
                {
                    Watch(m.BodyPos() + new Vector2(0f, 1.5f));   // only if the move framed nothing itself
                }
            }
            _cue = null;
        }

        /// <summary>Saves every generated sprite to dev/art, to look at the pixel art up close.</summary>
        private static void DumpArt()
        {
            string dir = Path.Combine(_dir, "art");
            Directory.CreateDirectory(dir);
            int n = 0;
            void Save(string name, Sprite s)
            {
                if (s == null || s.texture == null || !s.texture.isReadable)
                {
                    return;
                }
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), s.texture.EncodeToPNG());
                n++;
            }
            for (int v = 0; v < Art.Puffs.Length; v++)
            {
                for (int f = 0; f < Art.Puffs[v].Length; f++)
                {
                    Save("puff_" + v + "_" + f, Art.Puffs[v][f]);
                }
            }
            for (int v = 0; v < Art.Dust.Length; v++)
            {
                for (int f = 0; f < Art.Dust[v].Length; f++)
                {
                    Save("dust_" + v + "_" + f, Art.Dust[v][f]);
                }
            }
            for (int i = 0; i < Art.Glint.Length; i++)
            {
                Save("glint_" + i, Art.Glint[i]);
            }
            for (int i = 0; i < Art.Chunks.Length; i++)
            {
                Save("chunk_" + i, Art.Chunks[i]);
            }
            for (int i = 0; i < Art.Sparks.Length; i++)
            {
                Save("spark_" + i, Art.Sparks[i]);
            }
            for (int i = 0; i < Art.Lines.Length; i++)
            {
                Save("line_" + i, Art.Lines[i]);
            }
            for (int i = 0; i < Art.Bursts.Length; i++)
            {
                Save("burst_" + i, Art.Bursts[i]);
            }
            for (int i = 0; i < Art.Cracks.Length; i++)
            {
                Save("crack_" + i + "_dark", Art.Cracks[i].Dark);
                Save("crack_" + i + "_light", Art.Cracks[i].Light);
                Save("crack_" + i + "_glow", Art.Cracks[i].Glow);
            }
            for (int i = 0; i < Art.SmallCracks.Length; i++)
            {
                Save("smallcrack_" + i + "_dark", Art.SmallCracks[i].Dark);
                Save("smallcrack_" + i + "_light", Art.SmallCracks[i].Light);
            }
            foreach (int r in new[] { 4, 8, 14, 20, 30, 44, 62 })
            {
                Save("ring_" + r + "_3", Art.Ring(r, 3));
                Save("ring_" + r + "_1", Art.Ring(r, 1));
            }
            Save("ring_dashed_18", Art.Ring(18, 1, Art.Ground, 10));
            Save("ring_boom_16", Art.Ring(16, 1, 0.35f));
            Save("disk_flash_22", Art.Disk(22, 0.85f, 0.45f));
            Save("disk_shadow_4", Art.Disk(4, 0.42f, 0.45f));
            Log("art saved: " + n + " sprites in " + dir);
        }

        // ------------------------------------------------------------- capture --

        private static float _captureDelay;
        private static bool _uiWas;

        /// <summary>Points the camera and snaps the zoom (no zoom animation at the start of a capture).</summary>
        private static void Frame(Vector2 at, float zoom)
        {
            World.world.move_camera.focusOn(new Vector3(at.x, at.y, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            if (World.world.camera != null)
            {
                World.world.camera.orthographicSize = zoom;
            }
        }

        private static void StartCapture(int frames, int every, int w, int h, float delay, string folder = "frames")
        {
            _captureDelay = delay;
            _captureLeft = frames;
            _captureEvery = Mathf.Max(1, every);
            _captureW = w;
            _captureH = h;
            _captureTick = 0;
            _captureIndex = 0;
            _session = Path.Combine(_dir, string.IsNullOrEmpty(folder) ? "frames" : folder);
            if (!_capturing)
            {
                _uiWas = Config.ui_main_hidden;
                Config.ui_main_hidden = true;   // nothing covers a capture
                try
                {
                    // (nor a power's brush; never while the player controls a unit: a released Crabzilla blows up)
                    if (!ControllableUnit.isControllingUnit())
                    {
                        PowerButtonSelector.instance?.unselectAll();
                    }
                }
                catch (Exception)
                {
                }
            }
            if (Directory.Exists(_session))
            {
                foreach (string f in Directory.GetFiles(_session, "*.png"))
                {
                    File.Delete(f);
                }
            }
            Directory.CreateDirectory(_session);
            if (!_capturing)
            {
                SakugaMod.Instance.StartCoroutine(CaptureLoop());
            }
            Log("capturing " + frames + " frames every " + _captureEvery);
        }

        private static IEnumerator CaptureLoop()
        {
            _capturing = true;
            while (_captureDelay > 0f)
            {
                _captureDelay -= Time.unscaledDeltaTime;
                yield return null;
            }
            while (_captureLeft > 0)
            {
                yield return new WaitForEndOfFrame();
                if (World.world != null && World.world.camera != null && !Fx.OnScreen(_focus, -0.15f))
                {
                    World.world.move_camera.focusOn(new Vector3(_focus.x, _focus.y, 0f));   // keep a drifting fight in frame
                }
                if (_captureTick++ % _captureEvery != 0)
                {
                    continue;
                }
                try
                {
                    Grab();
                }
                catch (Exception e)
                {
                    Log("capture failed: " + e);
                    _captureLeft = 0;
                }
                _captureLeft--;
            }
            _capturing = false;
            Config.ui_main_hidden = _uiWas;
            Log("capture done: " + _captureIndex + " frames");
        }

        private static void Grab()
        {
            Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
            Texture2D outTex = full;
            if (_captureW > 0 && _captureH > 0 && World.world?.camera != null)
            {
                Vector3 sp = World.world.camera.WorldToScreenPoint(new Vector3(_focus.x, _focus.y, 0f));
                int w = Mathf.Min(_captureW, full.width), h = Mathf.Min(_captureH, full.height);
                int x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x) - w / 2, 0, full.width - w);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y) - h / 2, 0, full.height - h);
                outTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                outTex.SetPixels(full.GetPixels(x0, y0, w, h));
                outTex.Apply();
                UnityEngine.Object.Destroy(full);
            }
            byte[] png = outTex.EncodeToPNG();
            UnityEngine.Object.Destroy(outTex);
            File.WriteAllBytes(Path.Combine(_session, (_captureIndex++).ToString("D4") + "_f" + Time.frameCount + ".png"), png);
        }
    }
}
