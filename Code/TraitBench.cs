using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>
    /// Dev: "traitbench" opens the unit window's trait editor on a unit and measures, with the mod's traits
    /// shown and then hidden (what the game alone has): how long opening the editor tab takes (first time and
    /// again), and the frame times while the editor's list scrolls top to bottom and back. Also logs what the
    /// list is made of (buttons, graphics, masks, canvases).
    /// </summary>
    /// <summary>Dev: times Unity's UI rebuild each frame (layouts and graphics queued) while the bench watches.</summary>
    [HarmonyPatch(typeof(CanvasUpdateRegistry), "PerformUpdate")]
    internal static class UiRebuildProbe
    {
        public static bool On;
        public static int Layouts, Graphics;
        public static double Ms;
        private static readonly Stopwatch _sw = new Stopwatch();

        private static void Prefix(CanvasUpdateRegistry __instance)
        {
            if (!On)
            {
                return;
            }
            Layouts = Traverse.Create(__instance).Field("m_LayoutRebuildQueue").Property("Count").GetValue<int>();
            Graphics = Traverse.Create(__instance).Field("m_GraphicRebuildQueue").Property("Count").GetValue<int>();
            _sw.Restart();
        }

        private static void Postfix()
        {
            if (On)
            {
                _sw.Stop();
                Ms = _sw.Elapsed.TotalMilliseconds;
            }
        }
    }

    internal static class TraitBench
    {
        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["traitbench"] = a => World.world.StartCoroutine(Run());
            // "settingsorder": logs the rows of the open settings window in the order they are shown
            Dev.Commands["settingsorder"] = a =>
            {
                NeoModLoader.ui.ModConfigureWindow w = UnityEngine.Object.FindObjectOfType<NeoModLoader.ui.ModConfigureWindow>();
                Transform content = w != null ? Traverse.Create(w).Property("ContentTransform").GetValue<Transform>() : null;
                if (content == null)
                {
                    Dev.Log("settingsorder: window not open");
                    return;
                }
                for (int i = 0; i < content.childCount; i++)
                {
                    Transform panel = content.GetChild(i);
                    if (!panel.gameObject.activeSelf)
                    {
                        continue;
                    }
                    Transform rows = panel.Find("Grid");
                    List<string> names = new List<string>();
                    if (rows != null)
                    {
                        for (int r = 0; r < rows.childCount; r++)
                        {
                            if (rows.GetChild(r).gameObject.activeSelf)
                            {
                                names.Add(rows.GetChild(r).name);
                            }
                        }
                    }
                    Dev.Log("settingsorder " + panel.name + ": " + string.Join(", ", names));
                }
            };
            // "speed sonic|x1|x5": the world's speed; "look x y": the camera to a tile (away from the action)
            Dev.Commands["speed"] = a =>
            {
                string s = Dev.Arg(a, 1, "x1");
                if (s == "sonic")
                {
                    DebugConfig.setOption(DebugOption.SonicSpeed, true);
                }
                else
                {
                    Config.setWorldSpeed(s);
                }
                Dev.Log("speed: " + Config.time_scale_asset.id + " (x" + Config.time_scale_asset.multiplier + ")");
            };
            Dev.Commands["look"] = a =>
            {
                Camera cam = World.world.camera;
                if (cam != null)
                {
                    cam.transform.position = new Vector3(Dev.ArgF(a, 1, 20f), Dev.ArgF(a, 2, 20f), cam.transform.position.z);
                }
            };
            // "airburst [px]": the Kaiju Killer's sky nuke at the middle of the view
            // "scoreprobe [reset]": mean candidate score of forged vs hand-made techniques by tier (and how often each won)
            Dev.Commands["scoreprobe"] = a =>
            {
                if (a.Length > 1 && a[1] == "reset")
                {
                    Brain.Scores.Clear();
                    Brain.Started.Clear();
                    Dev.Log("scoreprobe reset");
                    return;
                }
                var sum = new Dictionary<string, Vector3>();
                foreach (var kv in Brain.Scores)
                {
                    string[] p = kv.Key.Split('|');
                    bool forged = Forge.Is(p[1]);
                    string g = p[0] + (forged ? " forged" : " hand");
                    sum.TryGetValue(g, out Vector3 s);
                    Brain.Started.TryGetValue(p[1], out int won);
                    sum[g] = s + new Vector3(kv.Value.x / Mathf.Max(1f, kv.Value.y), 1f, won);
                }
                foreach (var kv in sum)
                {
                    Dev.Log("scoreprobe " + kv.Key + ": " + kv.Value.y + " techniques, mean score " + (kv.Value.x / Mathf.Max(1f, kv.Value.y)).ToString("F1") + ", started " + kv.Value.z);
                }
                var worst = new List<KeyValuePair<string, Vector2>>(Brain.Scores);
                worst.Sort((x, y) => (x.Value.x / x.Value.y).CompareTo(y.Value.x / y.Value.y));
                foreach (var kv in worst)
                {
                    string id = kv.Key.Split('|')[1];
                    Technique tt = Techniques.Get(id);
                    bool divine = tt != null && tt.School == School.None && tt.Entry >= Tier.God;
                    if (Forge.Is(id) || divine)
                    {
                        Dev.Log("scoreprobe " + (Forge.Is(id) ? "forged " : "divine ") + kv.Key + " mean " + (kv.Value.x / kv.Value.y).ToString("F1") + " x" + kv.Value.y);
                    }
                }
                Dev.Log("capture done: scoreprobe");
            };
            // "auditall [from] [count] [only:forged|hand]": demos every technique in turn (the coherence audit:
            // ChoreoMove.Audit lines per blow, 'audit end' per move), then "capture done: auditall"
            Dev.Commands["auditall"] = a =>
            {
                int from = a.Length > 1 ? int.Parse(a[1]) : 0;
                int count = a.Length > 2 ? int.Parse(a[2]) : 9999;
                string only = a.Length > 3 ? a[3] : "";
                World.world.StartCoroutine(AuditAll(from, count, only));
            };
            // "loc key": a localized text as the game shows it (tooltips)
            Dev.Commands["loc"] = a =>
            {
                for (int i = 1; i < a.Length; i++)
                {
                    Dev.Log("loc " + a[i] + " = " + LocalizedTextManager.getText(a[i]).Replace("\n", " | "));
                }
                Dev.Log("capture done: loc");
            };
            Dev.Commands["airburst"] = a =>
            {
                Camera cam = World.world.camera;
                if (cam == null)
                {
                    return;
                }
                Vector2 at = new Vector2(cam.transform.position.x, cam.transform.position.y + cam.orthographicSize * 0.2f);
                KaijuKillerMove.Airburst(at, Dev.ArgF(a, 1, 0.2f), FxLayer.Top, new Color(1f, 0.85f, 0.4f, 1f));
                Dev.Watch(at);
                Dev.Log("airburst at " + at);
            };
            // "editorscroll 0.3": scrolls the open trait editor (1 top, 0 bottom)
            Dev.Commands["editorscroll"] = a =>
            {
                ScrollWindow w = ScrollWindow.getCurrentWindow();
                ActorTraitsEditor ed = w != null ? w.GetComponentInChildren<ActorTraitsEditor>(true) : null;
                ScrollRect sr = ed != null ? ed.GetComponentInParent<ScrollRect>() : null;
                if (sr != null)
                {
                    sr.verticalNormalizedPosition = Dev.ArgF(a, 1, 0f);
                }
            };
        }

        private static IEnumerator AuditAll(int from, int count, string only)
        {
            List<Technique> list = new List<Technique>();
            foreach (Technique t in Techniques.All)
            {
                if (t.Reactive || only == "forged" && !Forge.Is(t.Id) || only == "hand" && Forge.Is(t.Id))
                {
                    continue;
                }
                list.Add(t);
            }
            int end = Mathf.Min(list.Count, from + count);
            for (int i = from; i < end; i++)
            {
                Technique t = list[i];
                string lay = t.Role == Role.Area ? "cluster 6" : t.Role == Role.Line ? "line 4" : t.Team ? "cluster 5" : "lone 1";
                for (int tryN = 0; tryN < 2; tryN++)
                {
                    string[] parts = lay.Split(' ');
                    Dev.Log("auditall " + i + "/" + list.Count + " " + t.Id + (tryN > 0 ? " (ring)" : ""));
                    Dev.Exec("move " + t.Id + " 2 " + parts[1] + " human skeleton " + parts[0] + " 12 0.6 " + (t.Team ? "6" : "0"));
                    // until its move has played out (at most 9 s), then a breath
                    float waited = 0f;
                    bool seen = false;
                    while (waited < 9f)
                    {
                        yield return new WaitForSeconds(0.25f);
                        waited += 0.25f;
                        bool busy = false;
                        foreach (Move m in Moves.All)
                        {
                            if (m.Tech == t || m is GatherMove)
                            {
                                busy = true;
                                break;
                            }
                        }
                        seen |= busy;
                        if (!busy && (seen || waited >= 1.6f))
                        {
                            break;
                        }
                    }
                    yield return new WaitForSeconds(0.6f);
                    if (seen || t.Role != Role.Area)
                    {
                        break;
                    }
                    lay = "ring 6";   // a self-centred one needs foes around it
                }
            }
            Dev.Log("capture done: auditall");
        }

        private static IEnumerator Run()
        {
            Actor u = Dev.Performer;
            if (u == null)
            {
                foreach (Actor x in World.world.units)
                {
                    if (x != null && x.isAlive() && x.asset != null && x.asset.can_edit_traits && x.isAdult())
                    {
                        u = x;
                        break;
                    }
                }
            }
            if (u == null)
            {
                Dev.Log("traitbench: no unit");
                yield break;
            }
            // idle frames first (the baseline of this machine)
            yield return Frames("idle", 60, null);
            ActionLibrary.openUnitWindow(u);
            yield return null;
            yield return null;
            ScrollWindow w = ScrollWindow.getCurrentWindow();
            ActorTraitsEditor ed = w != null ? w.GetComponentInChildren<ActorTraitsEditor>(true) : null;
            if (ed == null)
            {
                Dev.Log("traitbench: no trait editor in " + (w != null ? w.name : "no window"));
                yield break;
            }
            WindowMetaTab tab = ed.getEditorTab();
            WindowMetaTabButtonsContainer tabs = Traverse.Create(tab).Field("container").GetValue<WindowMetaTabButtonsContainer>();
            WindowMetaTab other = null;
            foreach (WindowMetaTab t in w.GetComponentsInChildren<WindowMetaTab>(true))
            {
                if (t != tab && Traverse.Create(t).Field("container").GetValue<WindowMetaTabButtonsContainer>() == tabs)
                {
                    other = t;
                    break;
                }
            }
            foreach (string pass in new[] { "with mod traits", "game traits only", "with mod traits again" })
            {
                bool ours = pass != "game traits only";
                for (int i = 0; i < 3; i++)
                {
                    if (other != null)
                    {
                        tabs.showTab(other);
                    }
                    yield return null;
                    yield return null;
                    Show(ed, ours);
                    Stopwatch sw = Stopwatch.StartNew();
                    tabs.showTab(tab);
                    UnityEngine.Canvas.ForceUpdateCanvases();
                    sw.Stop();
                    float t0 = Time.realtimeSinceStartup;
                    yield return null;
                    float next = (Time.realtimeSinceStartup - t0) * 1000f;
                    Dev.Log("traitbench " + pass + ": open #" + (i + 1) + " " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms (+ next frame " + next.ToString("F1") + " ms)");
                }
                Describe(ed, pass);
                // what is animating right after the tab opens
                if (other != null)
                {
                    tabs.showTab(other);
                }
                yield return null;
                yield return null;
                UiRebuildProbe.On = true;
                tabs.showTab(tab);
                for (int f = 0; f < 16; f++)
                {
                    yield return null;
                    List<DG.Tweening.Tween> playing = DG.Tweening.DOTween.PlayingTweens();
                    Dictionary<string, int> kinds = new Dictionary<string, int>();
                    if (playing != null)
                    {
                        foreach (DG.Tweening.Tween tw in playing)
                        {
                            string k = tw.target != null ? tw.target.GetType().Name + (tw.target is Component c ? "@" + c.name : "") : "null";
                            if (k.Length > 60)
                            {
                                k = k.Substring(0, 60);
                            }
                            kinds.TryGetValue(k, out int nk);
                            kinds[k] = nk + 1;
                        }
                    }
                    List<string> top = new List<string>();
                    foreach (KeyValuePair<string, int> kv in kinds)
                    {
                        if (kv.Value >= 3 || kinds.Count < 6)
                        {
                            top.Add(kv.Key + " x" + kv.Value);
                        }
                    }
                    Dev.Log("traitbench " + pass + " f" + f + " dt " + (Time.unscaledDeltaTime * 1000f).ToString("F0") + " ui " + UiRebuildProbe.Ms.ToString("F1") + " ms (layouts " + UiRebuildProbe.Layouts + ", graphics " + UiRebuildProbe.Graphics + ") tweens " + (playing != null ? playing.Count : 0));
                }
                ScrollRect sr = ed.GetComponentInParent<ScrollRect>();
                if (sr == null)
                {
                    sr = ed.GetComponentInChildren<ScrollRect>(true);
                }
                yield return Frames("still " + pass, 120, null);
                yield return Frames("scroll " + pass, 180, sr);
            }
            Show(ed, true);
            Dev.Log("traitbench done");
        }

        /// <summary>Shows or hides the mod's buttons in the editor (hidden: as the game alone would be).</summary>
        private static void Show(ActorTraitsEditor ed, bool ours)
        {
            IEnumerable buttons = Traverse.Create(ed).Field("all_augmentation_buttons").GetValue<IEnumerable>();
            if (buttons == null)
            {
                return;
            }
            foreach (object o in buttons)
            {
                ActorTraitEditorButton b = o as ActorTraitEditorButton;
                ActorTrait t = b != null && b.augmentation_button != null ? b.augmentation_button.getElementAsset() : null;
                if (t != null && t.id.StartsWith("sakuga_"))
                {
                    b.gameObject.SetActive(ours);
                }
            }
        }

        private static void Describe(ActorTraitsEditor ed, string pass)
        {
            int graphics = 0, active = 0, shadows = 0, canvases = 0, buttons = 0;
            foreach (Graphic g in ed.GetComponentsInChildren<Graphic>(true))
            {
                graphics++;
                if (g.isActiveAndEnabled)
                {
                    active++;
                }
            }
            foreach (Shadow s in ed.GetComponentsInChildren<Shadow>(false))
            {
                shadows++;
            }
            canvases = ed.GetComponentsInChildren<UnityEngine.Canvas>(true).Length;
            buttons = ed.GetComponentsInChildren<ActorTraitEditorButton>(false).Length;
            ScrollRect sr = ed.GetComponentInParent<ScrollRect>();
            string mask = "?";
            if (sr != null && sr.viewport != null)
            {
                mask = (sr.viewport.GetComponent<RectMask2D>() != null ? "RectMask2D " : "") + (sr.viewport.GetComponent<Mask>() != null ? "Mask" : "");
            }
            else if (sr != null)
            {
                mask = (sr.GetComponent<RectMask2D>() != null ? "RectMask2D " : "") + (sr.GetComponent<Mask>() != null ? "Mask" : "") + " (no viewport)";
            }
            Dev.Log("traitbench " + pass + ": active buttons " + buttons + ", graphics " + active + "/" + graphics + ", shadows " + shadows + ", canvases " + canvases + ", mask " + mask
                + ", scroll " + (sr != null ? sr.name : "none"));
        }

        /// <summary>Frame times over n frames; with a ScrollRect it scrolls top to bottom and back meanwhile.</summary>
        private static IEnumerator Frames(string what, int n, ScrollRect sr)
        {
            List<float> dts = new List<float>(n);
            int gc0 = System.GC.CollectionCount(0);
            long mem0 = System.GC.GetTotalMemory(false);
            float last = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++)
            {
                if (sr != null)
                {
                    float k = i / (float)(n - 1);
                    sr.verticalNormalizedPosition = 1f - Mathf.PingPong(k * 2f, 1f);
                }
                yield return null;
                float now = Time.realtimeSinceStartup;
                dts.Add((now - last) * 1000f);
                last = now;
            }
            string spikes = "";
            for (int i = 0; i < dts.Count; i++)
            {
                if (dts[i] > 30f)
                {
                    spikes += " f" + i + "=" + dts[i].ToString("F0");
                }
            }
            if (spikes.Length > 0)
            {
                Dev.Log("traitbench " + what + " spikes:" + spikes);
            }
            dts.Sort();
            float sum = 0f;
            foreach (float d in dts)
            {
                sum += d;
            }
            Dev.Log("traitbench " + what + ": avg " + (sum / n).ToString("F1") + " ms, p95 " + dts[(int)(n * 0.95f)].ToString("F1") + " ms, max " + dts[n - 1].ToString("F1") + " ms, GCs " + (System.GC.CollectionCount(0) - gc0) + ", heap +" + ((System.GC.GetTotalMemory(false) - mem0) / 1024) + " KB");
        }
    }
}
