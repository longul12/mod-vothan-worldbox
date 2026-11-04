using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// THE VARIETY TEST (dev). Plays the same fight N times (several at once, each on its own clean
    /// meadow) and logs, per fight, the sequence of what happened: every technique started (by side
    /// A or B), every answer (with its counter depth: ^1 a counter to a counter), every charge broken,
    /// every duel exchange. Then it measures how different the fights were: distinct sequences,
    /// distinct openings, the mean edit distance between sequences (0 identical .. 1 nothing alike)
    /// and the mean distance between their sets of two-step patterns.
    ///
    ///   variety duel N tierA tierB raceA raceB [seconds] [parallel]
    ///   variety battle N nA nB raceA raceB tier [seconds] [parallel]   (nA fighters of 'tier' vs nB plain foes)
    ///   variety stop
    /// The log ends with "variety summary" (and "capture done", so run_demo.sh returns).
    /// </summary>
    internal static class Variety
    {
        private sealed class Fight
        {
            public int Index, Slot;
            public readonly List<Actor> A = new List<Actor>(), B = new List<Actor>();
            public readonly Dictionary<long, char> Side = new Dictionary<long, char>();
            public Vector2 Center;
            public float T;
            public bool Logged;
            public readonly List<string> Seq = new List<string>();
            public int Flow, Breaks, Answers, Techs, Exchanges;
            public string Who = "";
        }

        private static bool _on;
        private static string _mode;
        private static int _total, _started, _par, _nA, _nB;
        private static float _secs;
        private static Tier _tierA, _tierB;
        private static string _raceA, _raceB;
        private static readonly List<Fight> _live = new List<Fight>();
        private static readonly List<Fight> _done = new List<Fight>();
        private static readonly Dictionary<long, Fight> _of = new Dictionary<long, Fight>();
        private static readonly bool[] _busy = new bool[6];

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["variety"] = Command;
            Hooks.FrameTick += dt =>
            {
                if (_on)
                {
                    try
                    {
                        Tick(dt);
                    }
                    catch (Exception e)
                    {
                        SakugaMod.Report("variety", e);
                        _on = false;
                    }
                }
            };
            Hooks.TechniqueStarted += (f, p) => Note(f?.A, p?.Tech != null ? p.Tech.Id + (p.Linked ? "+" : "") : null, 0);
            Hooks.Answered += (d, a, t, depth, c) => Note(d, t.Id + (depth > 0 ? "^" + depth : ""), 1);
            Hooks.ChargeBroken += (v, by, m) =>
            {
                if (_on && v != null && _of.TryGetValue(v.Sid(), out Fight f))
                {
                    char bs = by != null && f.Side.TryGetValue(by.Sid(), out char s) ? s : '?';
                    char vs = f.Side.TryGetValue(v.Sid(), out char s2) ? s2 : '?';
                    f.Seq.Add("break:" + bs + ">" + vs + "(" + (m != null && m.Tech != null ? m.Tech.Id : m != null ? m.GetType().Name : "?") + ")");
                    f.Breaks++;
                }
            };
            Hooks.ExchangeStarted += (d, ex) =>
            {
                if (_on && d != null && _of.TryGetValue(d.IdA, out Fight f))
                {
                    f.Seq.Add("ex:" + ex.Id);
                    f.Exchanges++;
                }
            };
            Hooks.MoveStarted += m =>
            {
                if (_on && m is FlowStrike && _of.TryGetValue(m.Id, out Fight f))
                {
                    f.Flow++;
                }
            };
            Hooks.WorldCleared += Stop;
        }

        private static void Note(Actor a, string what, int kind)
        {
            if (!_on || a == null || what == null || !_of.TryGetValue(a.Sid(), out Fight f))
            {
                return;
            }
            char s = f.Side.TryGetValue(a.Sid(), out char c) ? c : '?';
            f.Seq.Add(s + ":" + what);
            if (kind == 0)
            {
                f.Techs++;
            }
            else
            {
                f.Answers++;
            }
        }

        private static void Command(string[] a)
        {
            string mode = Dev.Arg(a, 1, "duel");
            if (mode == "stop")
            {
                Stop();
                Dev.Log("variety stopped");
                return;
            }
            Stop();
            _mode = mode;
            _total = Mathf.Clamp((int)Dev.ArgF(a, 2, 8), 1, 64);
            if (mode == "battle")
            {
                _nA = Mathf.Clamp((int)Dev.ArgF(a, 3, 3), 1, 12);
                _nB = Mathf.Clamp((int)Dev.ArgF(a, 4, 8), 1, 30);
                _raceA = Dev.Arg(a, 5, "human");
                _raceB = Dev.Arg(a, 6, "orc");
                _tierA = Dev.ParseTier(Dev.Arg(a, 7, "master"));
                _tierB = Tier.None;
                _secs = Dev.ArgF(a, 8, 45f);
                _par = Mathf.Clamp((int)Dev.ArgF(a, 9, 3), 1, _busy.Length);
            }
            else
            {
                _mode = "duel";
                _tierA = Dev.ParseTier(Dev.Arg(a, 3, "god"));
                _tierB = Dev.ParseTier(Dev.Arg(a, 4, "god"));
                _raceA = Dev.Arg(a, 5, "human");
                _raceB = Dev.Arg(a, 6, "orc");
                _secs = Dev.ArgF(a, 7, 50f);
                _par = Mathf.Clamp((int)Dev.ArgF(a, 8, 4), 1, _busy.Length);
                _nA = _nB = 1;
            }
            _started = 0;
            _on = true;
            ScrollWindow.hideAllEvent(false);
            World.world.console?.Hide();
            Dev.Log("variety: " + _total + " " + _mode + "s of " + Setup() + ", " + _secs.ToString("F0") + " s each, " + _par + " at a time");
        }

        private static string Setup()
        {
            return _mode == "battle"
                ? _nA + " " + _raceA + " " + _tierA + " vs " + _nB + " " + _raceB
                : _raceA + " " + _tierA + " vs " + _raceB + " " + _tierB;
        }

        private static void Stop()
        {
            foreach (Fight f in _live)
            {
                Clear(f);
            }
            _live.Clear();
            _done.Clear();
            _of.Clear();
            for (int i = 0; i < _busy.Length; i++)
            {
                _busy[i] = false;
            }
            _on = false;
        }

        // --------------------------------------------------------------- run --

        private static void Tick(float dt)
        {
            if (World.world == null)
            {
                return;
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Fight f = _live[i];
                f.T += dt;
                if (!f.Logged && f.T > 1f)
                {
                    f.Logged = true;
                    f.Who = Describe(f);
                }
                int a = Alive(f.A), b = Alive(f.B);
                if (a == 0 || b == 0 || f.T >= _secs)
                {
                    string result = a == 0 && b == 0 ? "both fell" : a == 0 ? "B wins" : b == 0 ? "A wins" : "time (" + a + "/" + b + " standing)";
                    Finish(f, result);
                    _live.RemoveAt(i);
                }
            }
            while (_live.Count < _par && _started < _total)
            {
                int slot = FreeSlot();
                if (slot < 0)
                {
                    break;
                }
                Fight f = Launch(slot);
                if (f == null)
                {
                    _total--;   // (no ground for it: one fewer)
                    continue;
                }
                _live.Add(f);
            }
            if (_started >= _total && _live.Count == 0)
            {
                Summary();
                _on = false;
            }
        }

        private static int FreeSlot()
        {
            for (int i = 0; i < _par; i++)
            {
                if (!_busy[i])
                {
                    return i;
                }
            }
            return -1;
        }

        private static int Alive(List<Actor> side)
        {
            int n = 0;
            foreach (Actor u in side)
            {
                if (Combat.Living(u))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>The meadow of slot i: a grid over the middle of the map, far apart from each other.</summary>
        private static Vector2 SlotCenter(int i)
        {
            int cols = 3;
            float fx = (i % cols + 0.5f) / cols, fy = (i / cols + 0.5f) / 2f;
            return new Vector2(Mathf.Lerp(40f, MapBox.width - 40f, fx), Mathf.Lerp(40f, MapBox.height - 40f, fy));
        }

        private static Fight Launch(int slot)
        {
            Vector2 c = SlotCenter(slot);
            int half = _mode == "battle" ? 12 : 9;
            WorldTile ct = Meadow(c, half);
            if (ct == null)
            {
                return null;
            }
            Fight f = new Fight { Index = ++_started, Slot = slot, Center = new Vector2(ct.posV3.x, ct.posV3.y) };
            _busy[slot] = true;
            if (_mode == "battle")
            {
                for (int i = 0; i < _nA; i++)
                {
                    Add(f, 'A', Dev.Spawn(_raceA, World.world.GetTile(ct.x - 7 + i % 3, ct.y - 2 + i / 3 * 2) ?? ct, false), _tierA);
                }
                for (int i = 0; i < _nB; i++)
                {
                    Add(f, 'B', Dev.Spawn(_raceB, World.world.GetTile(ct.x + 4 + i % 5, ct.y - 4 + i / 5 * 2) ?? ct, false), Tier.None);
                }
            }
            else
            {
                Add(f, 'A', Dev.Spawn(_raceA, World.world.GetTile(ct.x - 4, ct.y) ?? ct, false), _tierA);
                Add(f, 'B', Dev.Spawn(_raceB, World.world.GetTile(ct.x + 4, ct.y) ?? ct, false), _tierB);
            }
            if (f.A.Count == 0 || f.B.Count == 0)
            {
                Clear(f);
                _busy[slot] = false;
                return null;
            }
            foreach (Actor x in f.A)
            {
                foreach (Actor y in f.B)
                {
                    x.addAggro(y);
                    y.addAggro(x);
                }
            }
            if (slot == 0)
            {
                World.world.move_camera.focusOn(new Vector3(f.Center.x, f.Center.y + 1f, 0f));
            }
            return f;
        }

        private static void Add(Fight f, char side, Actor u, Tier tier)
        {
            if (u == null)
            {
                return;
            }
            if (tier != Tier.None)
            {
                Progression.Promote(u, tier, false);
            }
            if (u.data != null)
            {
                u.data.favorite = false;
            }
            (side == 'A' ? f.A : f.B).Add(u);
            f.Side[u.Sid()] = side;
            _of[u.Sid()] = f;
        }

        private static string Describe(Fight f)
        {
            StringBuilder sb = new StringBuilder();
            foreach (List<Actor> side in new[] { f.A, f.B })
            {
                foreach (Actor u in side)
                {
                    if (!Fighters.TryGet(u, out Fighter fi))
                    {
                        continue;
                    }
                    sb.Append(f.Side.TryGetValue(u.Sid(), out char s) ? s : '?').Append('=').Append(u.asset.id).Append(' ').Append(fi.Tier)
                        .Append(' ').Append(fi.Persona.Main).Append('/').Append(fi.Persona.Second).Append(" [").Append(Dynamics.SchoolList(u)).Append("] ");
                    if (_mode == "battle")
                    {
                        break;   // one per side is enough to read
                    }
                }
            }
            return sb.ToString();
        }

        private static void Finish(Fight f, string result)
        {
            _done.Add(f);
            StringBuilder sb = new StringBuilder("variety fight " + f.Index + " (" + f.T.ToString("F0") + " s, " + result + ", " + f.Techs + " techniques, "
                + f.Answers + " answers, " + f.Breaks + " breaks, " + f.Exchanges + " exchanges, " + f.Flow + " flow chains) " + f.Who + ":");
            for (int i = 0; i < f.Seq.Count && i < 70; i++)
            {
                sb.Append(' ').Append(f.Seq[i]);
            }
            if (f.Seq.Count > 70)
            {
                sb.Append(" ... (+" + (f.Seq.Count - 70) + ")");
            }
            Dev.Log(sb.ToString());
            Clear(f);
            _busy[f.Slot] = false;
        }

        /// <summary>The fight's units go (the survivors too: the next fight starts clean).</summary>
        private static void Clear(Fight f)
        {
            foreach (List<Actor> side in new[] { f.A, f.B })
            {
                foreach (Actor u in side)
                {
                    _of.Remove(u.Sid());
                    if (u != null && !u.isRekt() && u.isAlive())
                    {
                        Moves.Interrupt(u);
                        u.die(true, AttackType.Other, false, false);
                    }
                }
            }
        }

        /// <summary>A clean meadow around 'c' (the same way the dev demos clear theirs).</summary>
        internal static WorldTile Meadow(Vector2 c, int half)
        {
            WorldTile ct = World.world.GetTile(Mathf.Clamp(Mathf.RoundToInt(c.x), half + 12, MapBox.width - half - 13),
                Mathf.Clamp(Mathf.RoundToInt(c.y), half + 12, MapBox.height - half - 13));
            if (ct == null)
            {
                return null;
            }
            for (int dx = -half - 6; dx <= half + 6; dx++)
            {
                for (int dy = -half - 4; dy <= half + 8; dy++)
                {
                    WorldTile t = World.world.GetTile(ct.x + dx, ct.y + dy);
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
                    MapAction.terraformTile(t, TileLibrary.soil_low, TopTileLibrary.grass_low);
                }
            }
            List<Actor> near = new List<Actor>(Finder.getUnitsFromChunk(ct, 2, half + 8));
            foreach (Actor u in near)
            {
                if (u != null && u.isAlive() && !_of.ContainsKey(u.Sid()))
                {
                    u.die(true, AttackType.Other, false, false);
                }
            }
            try
            {
                foreach (City city in World.world.cities.list.ToArray())
                {
                    WorldTile t = city?.getTile();
                    if (t != null && Mathf.Abs(t.x - ct.x) <= half + 20 && Mathf.Abs(t.y - ct.y) <= half + 20)
                    {
                        city.destroyCity();
                    }
                }
            }
            catch (Exception e)
            {
                Dev.Log("variety: city cleanup: " + e.Message);
            }
            return ct;
        }

        // ------------------------------------------------------------ measure --

        private static void Summary()
        {
            int n = _done.Count;
            HashSet<string> whole = new HashSet<string>(), open = new HashSet<string>();
            Dictionary<string, int> uses = new Dictionary<string, int>();
            int breaks = 0, answers = 0, deep = 0;
            foreach (Fight f in _done)
            {
                whole.Add(string.Join(" ", f.Seq));
                open.Add(string.Join(" ", f.Seq.GetRange(0, Mathf.Min(4, f.Seq.Count))));
                breaks += f.Breaks;
                answers += f.Answers;
                foreach (string s in f.Seq)
                {
                    if (s.Contains("^"))
                    {
                        deep++;
                    }
                    string k = s.Length > 2 && s[1] == ':' ? s.Substring(2) : s;
                    int p = k.IndexOf('^');
                    if (p > 0)
                    {
                        k = k.Substring(0, p);
                    }
                    k = k.TrimEnd('+');
                    if (k.StartsWith("break:", StringComparison.Ordinal))
                    {
                        k = "break";
                    }
                    uses.TryGetValue(k, out int c);
                    uses[k] = c + 1;
                }
            }
            float editSum = 0f, editMin = 1f, jacSum = 0f;
            int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float e = Edit(_done[i].Seq, _done[j].Seq, 24);
                    float jd = 1f - Jaccard(Bigrams(_done[i].Seq), Bigrams(_done[j].Seq));
                    editSum += e;
                    jacSum += jd;
                    editMin = Mathf.Min(editMin, e);
                    pairs++;
                }
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(uses);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                sb.Append(' ').Append(list[i].Key).Append('=').Append(list[i].Value);
            }
            Dev.Log("variety summary: " + n + " " + _mode + "s of " + Setup() + "; distinct sequences " + whole.Count + "/" + n
                + "; distinct openings (first 4 events) " + open.Count + "/" + n
                + "; mean edit distance " + (pairs > 0 ? editSum / pairs : 0f).ToString("F2") + " (closest pair " + (pairs > 0 ? editMin : 0f).ToString("F2") + ")"
                + "; mean pattern distance " + (pairs > 0 ? jacSum / pairs : 0f).ToString("F2")
                + "; answers " + answers + " (deeper than one layer " + deep + "), charges broken " + breaks + "; used:" + sb);
            Dev.Log(Dynamics.Stats());
            Dev.Log("capture done (variety)");
        }

        /// <summary>Levenshtein distance between the first 'max' events of two sequences, over the longer length (0..1).</summary>
        private static float Edit(List<string> a, List<string> b, int max)
        {
            int n = Mathf.Min(a.Count, max), m = Mathf.Min(b.Count, max);
            if (n == 0 && m == 0)
            {
                return 0f;
            }
            int[] prev = new int[m + 1], cur = new int[m + 1];
            for (int j = 0; j <= m; j++)
            {
                prev[j] = j;
            }
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Mathf.Min(Mathf.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                }
                int[] t = prev;
                prev = cur;
                cur = t;
            }
            return prev[m] / (float)Mathf.Max(n, m);
        }

        private static HashSet<string> Bigrams(List<string> s)
        {
            HashSet<string> set = new HashSet<string>();
            for (int i = 0; i + 1 < s.Count; i++)
            {
                set.Add(s[i] + ">" + s[i + 1]);
            }
            return set;
        }

        private static float Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 && b.Count == 0)
            {
                return 1f;
            }
            int inter = 0;
            foreach (string x in a)
            {
                if (b.Contains(x))
                {
                    inter++;
                }
            }
            return inter / (float)(a.Count + b.Count - inter);
        }
    }
}
