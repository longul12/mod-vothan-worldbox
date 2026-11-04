using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// AURAS (settings: one switch per tier, all on by default). Everyone with abilities shows it, all
    /// the time, in its tier's colour:
    ///   - an outline one pixel out around the unit exactly as the game draws it this frame (four
    ///     flat-colour copies under it, a pixel out each way). It breathes (a slow pulse) and burns
    ///     brighter in a fight. Grandmasters and Gods have a second, fainter ring outside it (a God's
    ///     a pale-gold glow).
    ///   - particles rising off the body, more and livelier the higher the tier:
    ///       Novice       a faint silver shimmer, a mote now and then
    ///       Adept        blue motes
    ///       Expert       yellow motes, now and then a second one
    ///       Master       orange motes and small ki flames licking up its sides
    ///       Grandmaster  crimson flames and motes, a red crackle now and then
    ///       Combat God   gold embers, gold flames, white glints
    ///   - a unit that only knows single moves (no tier) shimmers like a Novice (the Novice switch).
    /// Only units the game draws this frame get one (its own visible list, read right after it has
    /// worked out where and how to draw them), at most MaxRings at once (the strongest first). The
    /// particles only close up (from afar they would be noise) and from a small budget of their own,
    /// so a war of thousands never chokes the techniques' effects. Particles stop with the world
    /// (paused, hit-stop, frozen cinematic); the outline keeps breathing.
    /// </summary>
    internal static class Auras
    {
        private const int MaxRings = 160;
        /// <summary>Camera size above which no particles are spawned (they would be specks).</summary>
        private const float ParticleZoom = 40f;

        private sealed class Look
        {
            public Color Edge, Mote, Mote2, Flame0, Flame1, Spark;
            public float Idle, Fight, Pulse, Hz;
            /// <summary>Seconds between motes, flames, sparks out of a fight (halved in one; 0: none).</summary>
            public float MoteEvery, FlameEvery, SparkEvery;
            public bool Glint;
            public bool Outer;
            public Color OuterC;
            /// <summary>The outer ring's alpha relative to the inner one.</summary>
            public float OuterA;
        }

        private sealed class Ring
        {
            public long Id;
            public readonly SpriteRenderer[] R = new SpriteRenderer[8];
            public int N;
            public int Seen;
            public float Phase, Mote, Flame, Spark;
        }

        private static readonly Look[] _looks = new Look[Tiers.Count + 1];
        private static readonly Dictionary<long, Ring> _rings = new Dictionary<long, Ring>();
        private static readonly List<long> _drop = new List<long>();
        private static readonly Stack<SpriteRenderer> _free = new Stack<SpriteRenderer>();
        private static readonly List<int> _cands = new List<int>();
        private static readonly List<Tier> _candTier = new List<Tier>();
        private static int[] _order = new int[512];
        private static Transform _root;
        private static Sprite[] _flame;
        private static int _frame;
        private static float _budget;
        private static bool _anyShown;

        /// <summary>Rings drawn last frame (dev).</summary>
        internal static int Shown;

        private static readonly Vector2[] Offsets =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(2f, 0f), new Vector2(-2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f)
        };

        [SakugaInit]
        private static void Init()
        {
            Hooks.WorldCleared += ReleaseAll;
            for (int t = 1; t <= Tiers.Count; t++)
            {
                TierInfo i = Tiers.Info((Tier)t);
                _looks[t] = Make((Tier)t, i);
            }
            _looks[0] = _looks[1];
        }

        private static Look Make(Tier t, TierInfo i)
        {
            Color main = i.Main, light = i.Light;
            switch (t)
            {
                case Tier.Novice:
                    // silver with a breath of blue: the faintest
                    Color silver = new Color(0.82f, 0.88f, 1f, 1f);
                    return new Look { Edge = silver, Mote = Color.white, Mote2 = silver, Idle = 0.3f, Fight = 0.45f, Pulse = 0.08f, Hz = 0.6f, MoteEvery = 1.6f };
                case Tier.Adept:
                    return new Look { Edge = main, Mote = light, Mote2 = main, Idle = 0.4f, Fight = 0.58f, Pulse = 0.1f, Hz = 0.8f, MoteEvery = 1.1f };
                case Tier.Expert:
                    return new Look { Edge = main, Mote = light, Mote2 = main, Idle = 0.48f, Fight = 0.65f, Pulse = 0.11f, Hz = 1f, MoteEvery = 0.75f };
                case Tier.Master:
                    return new Look
                    {
                        Edge = main, Mote = light, Mote2 = main, Flame0 = light, Flame1 = main, Idle = 0.5f, Fight = 0.7f, Pulse = 0.12f, Hz = 1.3f,
                        MoteEvery = 0.55f, FlameEvery = 1.2f
                    };
                case Tier.Grandmaster:
                    return new Look
                    {
                        Edge = main, Mote = light, Mote2 = main, Flame0 = light, Flame1 = main, Spark = light, Idle = 0.58f, Fight = 0.8f,
                        Pulse = 0.14f, Hz = 1.6f, MoteEvery = 0.42f, FlameEvery = 0.8f, SparkEvery = 2.4f,
                        Outer = true, OuterC = main, OuterA = 0.3f
                    };
                default:
                    // gold: a gold ring inside a soft pale-gold glow, gold embers and flames, white glints
                    return new Look
                    {
                        Edge = main, Mote = light, Mote2 = main, Flame0 = Color.white, Flame1 = main, Spark = Color.white, Idle = 0.66f, Fight = 0.9f,
                        Pulse = 0.15f, Hz = 1.1f, MoteEvery = 0.3f, FlameEvery = 0.9f, SparkEvery = 1.8f, Glint = true,
                        Outer = true, OuterC = light, OuterA = 0.45f
                    };
            }
        }

        /// <summary>Any tier's aura switched on.</summary>
        private static bool AnyOn()
        {
            for (int t = 1; t <= Tiers.Count; t++)
            {
                if (Settings.Aura[t])
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Right after the game has worked out where and how it draws every visible unit.</summary>
        internal static void Draw(ActorManager units)
        {
            _frame++;
            MapBox w = World.world;
            Camera cam = w != null ? w.camera : null;
            if (cam == null || units == null || !AnyOn() || !MapBox.isRenderGameplay())
            {
                if (_anyShown)
                {
                    ReleaseAll();
                }
                return;
            }
            ActorVisibleDataArray vis = units.visible_units;
            ActorRenderData rd = units.render_data;
            int count = Mathf.Min(vis.count, rd.positions.Length);
            float dt = Clock.Dt;
            _budget = Mathf.Min(30f, _budget + 90f * dt);
            bool particles = dt > 0f && cam.orthographicSize <= ParticleZoom;
            float time = Time.unscaledTime;

            // who shows one: tiered (or move-trait) fighters drawn this frame, the strongest first
            _cands.Clear();
            _candTier.Clear();
            for (int i = 0; i < count; i++)
            {
                Actor a = vis.array[i];
                if (a == null || !rd.has_normal_render[i] || !Fighters.TryGet(a, out Fighter f) || !a.isAlive())
                {
                    continue;
                }
                Tier t = f.Tier;
                if (!Settings.Aura[t == Tier.None ? 1 : (int)t] || a.isInsideSomething())
                {
                    continue;
                }
                _cands.Add(i);
                _candTier.Add(t);
            }
            int n = _cands.Count;
            if (_order.Length < n)
            {
                _order = new int[Mathf.NextPowerOfTwo(n)];
            }
            if (n > MaxRings)
            {
                // (counting sort by tier: no allocation, stable)
                int k = 0;
                for (int t = Tiers.Count; t >= 0 && k < MaxRings; t--)
                {
                    for (int j = 0; j < n && k < MaxRings; j++)
                    {
                        if ((int)_candTier[j] == t)
                        {
                            _order[k++] = j;
                        }
                    }
                }
                n = k;
            }
            else
            {
                for (int j = 0; j < n; j++)
                {
                    _order[j] = j;
                }
            }
            int shown = 0;
            for (int j = 0; j < n; j++)
            {
                int c = _order[j];
                int i = _cands[c];
                Actor a = vis.array[i];
                Sprite s = rd.main_sprite_colored[i] ?? rd.main_sprites[i];
                Vector3 sc = rd.scales[i];
                if (s == null || Mathf.Abs(sc.x) < 1e-4f || Mathf.Abs(sc.y) < 1e-4f)
                {
                    continue;   // (hidden mid-blink, or not drawn this frame)
                }
                Tier t = _candTier[c];
                Look look = _looks[(int)t];
                Fighters.TryGet(a, out Fighter f);
                bool fight = f != null && (f.InCombat || a.isFighting());
                long id = a.Sid();
                if (!_rings.TryGetValue(id, out Ring r))
                {
                    r = new Ring { Id = id, Phase = Random.value * 6.283f, Mote = Random.value * look.MoteEvery, Flame = Random.value * look.FlameEvery };
                    _rings[id] = r;
                }
                r.Seen = _frame;
                Place(r, look, s, rd.positions[i], sc, rd.rotations[i], fight, time);
                shown++;
                if (particles && _budget >= 1f)
                {
                    Emit(r, look, s, rd.positions[i], sc, fight, dt);
                }
            }
            Shown = shown;
            _anyShown = shown > 0 || _rings.Count > 0;

            // rings not drawn this frame go back to the pool
            _drop.Clear();
            foreach (KeyValuePair<long, Ring> kv in _rings)
            {
                if (kv.Value.Seen != _frame)
                {
                    _drop.Add(kv.Key);
                }
            }
            for (int j = 0; j < _drop.Count; j++)
            {
                Release(_rings[_drop[j]]);
                _rings.Remove(_drop[j]);
            }
        }

        /// <summary>The outline: copies of the unit's sprite as the game draws it, a pixel out each way.</summary>
        private static void Place(Ring r, Look look, Sprite s, Vector3 pos, Vector3 sc, Vector3 rot, bool fight, float time)
        {
            int want = look.Outer ? 8 : 4;
            while (r.N < want)
            {
                SpriteRenderer sr = Take();
                if (sr == null)
                {
                    break;
                }
                sr.sortingOrder = r.N < 4 ? 900 : 899;
                r.R[r.N++] = sr;
            }
            float texel = Mathf.Abs(sc.y) / Mathf.Max(1f, s.pixelsPerUnit);
            float wave = Mathf.Sin(time * look.Hz * 6.283f + r.Phase);
            float a = (fight ? look.Fight : look.Idle) * (1f + look.Pulse * wave / Mathf.Max(0.2f, fight ? look.Fight : look.Idle));
            Color edge = look.Edge;
            if (look.Glint && wave > 0.6f)
            {
                edge = Color.Lerp(edge, Color.white, (wave - 0.6f) * 0.6f);   // a God's ring flashes white at the top of its breath
            }
            edge.a = Mathf.Clamp01(a);
            Color outer = look.OuterC;
            outer.a = Mathf.Clamp01(a * look.OuterA);
            Quaternion q = Quaternion.Euler(rot);
            for (int k = 0; k < r.N; k++)
            {
                SpriteRenderer sr = r.R[k];
                if ((object)sr.sprite != s)
                {
                    sr.sprite = s;
                }
                Vector2 o = Offsets[k] * texel;
                Transform tr = sr.transform;
                tr.SetPositionAndRotation(new Vector3(pos.x + o.x, pos.y + o.y, pos.z), q);
                tr.localScale = sc;
                sr.color = k < 4 ? edge : outer;
                if (!sr.enabled)
                {
                    sr.enabled = true;
                }
            }
        }

        private static void Emit(Ring r, Look look, Sprite s, Vector3 pos, Vector3 sc, bool fight, float dt)
        {
            float texel = Mathf.Abs(sc.y) / Mathf.Max(1f, s.pixelsPerUnit);
            float h = s.rect.height * texel;
            float w = s.rect.width * texel;
            float rate = fight ? 2f : 1f;
            r.Mote -= dt * rate;
            if (r.Mote <= 0f && look.MoteEvery > 0f)
            {
                r.Mote += look.MoteEvery * Random.Range(0.7f, 1.3f);
                Mote(look, pos, w, h, texel);
                if (look.FlameEvery <= 0f && Random.value < (look.MoteEvery < 0.8f ? 0.35f : 0f))
                {
                    Mote(look, pos, w, h, texel);   // an Expert's motes come in pairs now and then
                }
            }
            if (look.FlameEvery > 0f)
            {
                r.Flame -= dt * rate;
                if (r.Flame <= 0f)
                {
                    r.Flame += look.FlameEvery * Random.Range(0.7f, 1.3f);
                    Flame(look, pos, w, h, texel);
                }
            }
            if (look.SparkEvery > 0f)
            {
                r.Spark -= dt * rate;
                if (r.Spark <= 0f)
                {
                    r.Spark += look.SparkEvery * Random.Range(0.7f, 1.3f);
                    Spark(look, pos, w, h, texel);
                }
            }
        }

        /// <summary>A pixel of light drifting up off the body's edge.</summary>
        private static void Mote(Look look, Vector3 pos, float w, float h, float texel)
        {
            if (_budget < 1f)
            {
                return;
            }
            _budget -= 1f;
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector2 at = new Vector2(pos.x + side * w * Random.Range(0.2f, 0.5f), pos.y + h * Random.Range(0.1f, 0.8f));
            bool front = Random.value < 0.4f;
            // (behind the unit: on the ground-effects layer, which every unit is drawn over)
            Particle p = Fx.Spawn(Art.Pixel, at, texel, Random.Range(0.7f, 1.1f), front ? FxLayer.Over : FxLayer.Ground, front ? -5 : 902);
            if (p == null)
            {
                return;
            }
            p.Snap = true;
            p.Moving(new Vector2(side * Random.Range(0.5f, 2f), Random.Range(6f, 11f)) * texel, 0f, 0.5f);
            p.Color(Fx.WithA(look.Mote, 0f), Fx.WithA(look.Mote, 0.95f), Fx.WithA(look.Mote2, 0.7f), Fx.WithA(look.Mote2, 0f));
        }

        /// <summary>A small ki flame licking up the side of the body (Masters and up).</summary>
        private static void Flame(Look look, Vector3 pos, float w, float h, float texel)
        {
            if (_budget < 1f)
            {
                return;
            }
            _budget -= 1f;
            Sprite[] frames = FlameFrames();
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector2 at = new Vector2(pos.x + side * w * Random.Range(0.25f, 0.45f), pos.y + h * Random.Range(0f, 0.35f));
            Particle p = Fx.Spawn(frames, at, texel, Random.Range(0.4f, 0.55f), FxLayer.Ground, 901);
            if (p == null)
            {
                return;
            }
            p.Snap = true;
            p.Moving(new Vector2(-side * Random.Range(0f, 1.5f), Random.Range(9f, 14f)) * texel, 0f, 0.8f);
            p.Color(Fx.WithA(look.Flame0, 0.85f), Fx.WithA(look.Flame1, 0.8f), Fx.WithA(look.Flame1, 0f));
        }

        /// <summary>A Grandmaster's red crackle, a God's white glint.</summary>
        private static void Spark(Look look, Vector3 pos, float w, float h, float texel)
        {
            if (_budget < 1f)
            {
                return;
            }
            _budget -= 1f;
            Vector2 at = new Vector2(pos.x + w * Random.Range(-0.45f, 0.45f), pos.y + h * Random.Range(0.2f, 0.95f));
            if (look.Glint)
            {
                Particle g = Fx.Spawn(Art.Glint, at, texel, 0.35f, FxLayer.Over, 6);
                if (g != null)
                {
                    g.Snap = true;
                    g.Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(look.Edge, 0.8f), Fx.WithA(look.Edge, 0f));
                }
                return;
            }
            Particle p = Fx.Spawn(Art.Sparks[0], at, texel, 0.14f, FxLayer.Over, 6);
            if (p != null)
            {
                p.Snap = true;
                p.Rot = Random.Range(0f, 360f);
                p.Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(look.Spark, 0.8f), Fx.WithA(look.Spark, 0f));
            }
        }

        /// <summary>A ki flame in four frames: a bud, a tongue, a thin lick, a wisp (pivot at its base).</summary>
        private static Sprite[] FlameFrames()
        {
            if (_flame != null)
            {
                return _flame;
            }
            // (top row first; '@' is the white-hot core)
            string[][] shapes =
            {
                new[] { ".#.", "#@#", "###" },
                new[] { ".#.", "##.", "#@#", "#@#", "###" },
                new[] { "#..", "##.", ".##", "#@#", "#@#", ".#." },
                new[] { ".#.", "#..", ".#.", "##.", ".#." , "..."},
                new[] { "..#", ".#.", "...", ".#.", "...", "..." },
            };
            _flame = new Sprite[shapes.Length];
            for (int f = 0; f < shapes.Length; f++)
            {
                string[] rows = shapes[f];
                Canvas c = new Canvas(3, rows.Length);
                for (int y = 0; y < rows.Length; y++)
                {
                    string row = rows[rows.Length - 1 - y];   // top row first in the strings
                    for (int x = 0; x < 3; x++)
                    {
                        char ch = row[x];
                        if (ch == '#')
                        {
                            c.Set(x, y, 0.85f, f == 3 ? 0.6f : 0.9f);
                        }
                        else if (ch == '@')
                        {
                            c.Set(x, y, 1f, 1f);
                        }
                    }
                }
                _flame[f] = c.ToSprite("sakuga_aura_flame_" + f, new Vector2(0.5f, 0f));
            }
            return _flame;
        }

        private static SpriteRenderer Take()
        {
            while (_free.Count > 0)
            {
                SpriteRenderer pooled = _free.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }
            if (_root == null)
            {
                GameObject root = new GameObject("CombatGodsAuras");
                Object.DontDestroyOnLoad(root);
                _root = root.transform;
            }
            GameObject go = new GameObject("aura");
            go.transform.SetParent(_root, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = Fx.SilhouetteMaterial;
            // under every unit (only the pixel that sticks out past the unit's own shape shows), over the
            // ground and its scars
            sr.sortingLayerID = Fx.GroundLayer;
            sr.enabled = false;
            return sr;
        }

        private static void Release(Ring r)
        {
            for (int k = 0; k < r.N; k++)
            {
                SpriteRenderer sr = r.R[k];
                if (sr != null)
                {
                    sr.enabled = false;
                    _free.Push(sr);
                }
                r.R[k] = null;
            }
            r.N = 0;
        }

        private static void ReleaseAll()
        {
            foreach (Ring r in _rings.Values)
            {
                Release(r);
            }
            _rings.Clear();
            _anyShown = false;
            Shown = 0;
        }
    }

    /// <summary>Auras are drawn right after the game has worked out how to draw every visible unit.</summary>
    [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.calculateVisibleActors))]
    internal static class AuraPatch
    {
        private static void Postfix(ActorManager __instance)
        {
            try
            {
                Auras.Draw(__instance);
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("auras", e);
            }
        }
    }
}
