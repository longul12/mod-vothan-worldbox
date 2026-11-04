using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Cinematics: the world holding its breath. A cinematic can
    ///   - freeze the world (nothing moves except the cinematic's own moves and effects,
    ///     those marked Cinematic / Particle.Cine),
    ///   - slow it down (the simulation skips frames),
    ///   - dim the screen (a dark veil over the world; the cinematic draws above it on FxLayer.Cine),
    ///   - show a title card (big pixel letters at the top of the screen).
    /// One cinematic at a time, with a gap between them, and only where the player is looking
    /// at a sensible speed (otherwise the technique plays in "light" mode: no freeze, no veil).
    /// Everything counts in real seconds (scaled by dev slow motion), and stops while paused.
    /// </summary>
    internal static class Cinema
    {
        private static readonly HashSet<object> _holds = new HashSet<object>();
        private static float _freezeLeft;
        private static float _rate = 1f, _rateLeft, _acc;

        private static object _owner;
        private static double _lastEnd = -999;

        private static SpriteRenderer _veil;
        private static float _veilA, _veilTarget, _veilSpeed;
        private static Color _veilColor = Color.black;
        private static bool _vignette;
        private static SpriteRenderer _vig;
        private static Sprite _vigSprite;

        private static readonly List<Particle> _title = new List<Particle>();
        /// <summary>Each title particle's Gen when spawned: once it expires, the pool hands it to other effects.</summary>
        private static readonly List<int> _titleGen = new List<int>();

        /// <summary>Real seconds the cinematic's owner has held nothing (no hold, freeze, slow motion or veil).</summary>
        private static float _idle;

        /// <summary>The world is held still right now.</summary>
        public static bool Frozen => _holds.Count > 0 || _freezeLeft > 0f;

        /// <summary>World speed while a slow motion runs (1 = normal).</summary>
        public static float Rate => _rateLeft > 0f ? _rate : 1f;

        /// <summary>This frame's step for cinematic moves and effects (real time, 0 while paused).</summary>
        public static float Dt;

        public static bool Playing => _owner != null;

        // --------------------------------------------------------- the gate --

        /// <summary>
        /// May a full cinematic (freeze, veil) play at this spot now? Off-screen, zoomed far out,
        /// above x5 speed, or with cinematics set to light, techniques play without them.
        /// </summary>
        public static bool FullAt(Vector2 at)
        {
            if (Settings.Cinematics < 2 || World.world == null || Config.time_scale_asset == null)
            {
                return false;
            }
            if (Config.time_scale_asset.multiplier > 5f)
            {
                return false;
            }
            Camera cam = World.world.camera;
            return cam != null && cam.orthographicSize <= 90f && Fx.OnScreen(at, 0.05f);
        }

        /// <summary>Starts a cinematic if none is playing and the last one ended 'gap' seconds ago.</summary>
        public static bool TryBegin(object owner, float gap = 8f)
        {
            if (_owner != null && _owner != owner)
            {
                return false;
            }
            if (_owner == null && Fighter.Now - _lastEnd < gap)
            {
                return false;
            }
            if (_owner != owner)
            {
                _idle = 0f;
            }
            _owner = owner;
            return true;
        }

        /// <summary>Ends the cinematic: lifts its holds, its veil, its slow motion, its freeze.</summary>
        public static void End(object owner)
        {
            Unhold(owner);
            if (_owner == owner)
            {
                _owner = null;
                _lastEnd = Fighter.Now;
                Undim(0.4f);
                _rateLeft = 0f;
                _freezeLeft = 0f;   // (a freeze it asked for no longer outlives it)
            }
        }

        // ------------------------------------------------------------ time --

        /// <summary>Holds the world still until Unhold(owner).</summary>
        public static void Hold(object owner)
        {
            _holds.Add(owner);
        }

        public static void Unhold(object owner)
        {
            _holds.Remove(owner);
        }

        /// <summary>Freezes the world for 'seconds' (real time).</summary>
        public static void Freeze(float seconds)
        {
            _freezeLeft = Mathf.Max(_freezeLeft, seconds);
        }

        /// <summary>The world runs at 'rate' (0.25 = quarter speed) for 'seconds' of real time.</summary>
        public static void SlowMo(float rate, float seconds)
        {
            _rate = Mathf.Clamp(rate, 0.05f, 1f);
            _rateLeft = seconds;
        }

        /// <summary>Should the simulation skip this frame (frozen, or a slow-motion frame)?</summary>
        public static bool SkipFrame()
        {
            if (Frozen)
            {
                return true;
            }
            if (_rateLeft <= 0f)
            {
                return false;
            }
            _acc += _rate;
            if (_acc >= 1f)
            {
                _acc -= 1f;
                return false;
            }
            return true;
        }

        // ----------------------------------------------------------- veil --

        /// <summary>Darkens the world to 'alpha' over 'fade' seconds (the cinematic draws on FxLayer.Cine above it).</summary>
        public static void Dim(float alpha, Color color, float fade, bool vignette = false)
        {
            _veilTarget = Mathf.Clamp01(alpha);
            _veilColor = color;
            _veilSpeed = fade <= 0f ? 1000f : Mathf.Abs(_veilTarget - _veilA) / fade;
            _vignette = vignette;
        }

        public static void Undim(float fade)
        {
            _veilTarget = 0f;
            _veilSpeed = fade <= 0f ? 1000f : Mathf.Max(0.01f, _veilA) / fade;
        }

        // ----------------------------------------------------------- tick --

        /// <summary>Once per frame, before moves tick.</summary>
        public static void Tick()
        {
            float real = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * Clock.Scale;
            bool paused = World.world == null || World.world.isPaused();
            Dt = paused ? 0f : real;
            if (!paused)
            {
                if (_freezeLeft > 0f)
                {
                    _freezeLeft -= real;
                }
                if (_rateLeft > 0f)
                {
                    _rateLeft -= real;
                }
            }
            _veilA = Mathf.MoveTowards(_veilA, _veilTarget, _veilSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
            UpdateVeil();
            if (_owner == null)
            {
                _idle = 0f;
                return;
            }
            // a cinematic that forgot to End (its move is over, or it has held nothing for a long
            // while, e.g. a timer that was lost): let the next one play
            bool idle = _holds.Count == 0 && _freezeLeft <= 0f && _rateLeft <= 0f && _veilTarget <= 0f;
            _idle = idle ? _idle + Dt : 0f;
            if (_owner is Move m && m.Ended || _idle > 30f)
            {
                End(_owner);
            }
        }

        private static void UpdateVeil()
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (_veilA <= 0.001f || cam == null || !MapBox.isRenderGameplay())
            {
                if (_veil != null)
                {
                    _veil.enabled = false;
                }
                if (_vig != null)
                {
                    _vig.enabled = false;
                }
                return;
            }
            EnsureVeil();
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            Vector3 c = cam.transform.position;
            SpriteRenderer r = _vignette ? _vig : _veil;
            SpriteRenderer other = _vignette ? _veil : _vig;
            other.enabled = false;
            r.enabled = true;
            r.transform.position = new Vector3(c.x, c.y, -0.05f);
            if (_vignette)
            {
                r.transform.localScale = new Vector3(w * 1.15f / _vigSprite.rect.width, h * 1.15f / _vigSprite.rect.height, 1f);
            }
            else
            {
                r.transform.localScale = new Vector3(w * 1.2f, h * 1.2f, 1f);
            }
            r.color = Fx.WithA(_veilColor, _veilA);
        }

        private static void EnsureVeil()
        {
            if (_veil != null)
            {
                return;
            }
            GameObject go = new GameObject("SakugaVeil");
            Object.DontDestroyOnLoad(go);
            _veil = go.AddComponent<SpriteRenderer>();
            _veil.sprite = Art.Pixel;
            _veil.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            _veil.sortingLayerID = SortingLayer.NameToID("EffectsTop");
            _veil.sortingOrder = 900;
            GameObject vg = new GameObject("SakugaVignette");
            Object.DontDestroyOnLoad(vg);
            _vig = vg.AddComponent<SpriteRenderer>();
            _vigSprite = MakeVignette(96, 64);
            _vig.sprite = _vigSprite;
            _vig.sharedMaterial = _veil.sharedMaterial;
            _vig.sortingLayerID = _veil.sortingLayerID;
            _vig.sortingOrder = 900;
        }

        /// <summary>Dark at the edges, clear in the middle (Crabzilla can't be redrawn above a full veil).</summary>
        private static Sprite MakeVignette(int w, int h)
        {
            Canvas c = new Canvas(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 1f;
                    float dy = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((d - 0.45f) / 0.6f);
                    c.Set(x, y, 1f, a * a * (3f - 2f * a));
                }
            }
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(c.Px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        }

        // ---------------------------------------------------------- title --

        /// <summary>
        /// A title card: big pixel letters (A-Z, 0-9, space and !?'.-:) at the top of the screen,
        /// in 'color' with a dark outline, sliding in and fading out. Only when title cards are on
        /// and the spot is on screen.
        /// </summary>
        public static void Title(string text, Color color, float seconds, Vector2 at, float size = 1f, float y = 0.62f)
        {
            if (!Settings.TitleCards || World.world == null || !Fx.OnScreen(at, 0.1f))
            {
                return;
            }
            ClearTitle();
            Sprite fill = PixelFont.Render(text, false);
            Sprite edge = PixelFont.Render(text, true);
            if (fill == null)
            {
                return;
            }
            Particle e = Fx.Spawn(edge, Vector2.zero, 1f, seconds, FxLayer.Cine, 40);
            Particle f = Fx.Spawn(fill, Vector2.zero, 1f, seconds, FxLayer.Cine, 41);
            Color dark = Color.Lerp(color, Color.black, 0.8f);
            Setup(e, dark, seconds, size, 0, y);
            Setup(f, color, seconds, size, 1, y);
        }

        private static void Setup(Particle p, Color c, float seconds, float size, int layer, float y)
        {
            if (p == null)
            {
                return;
            }
            p.Cine = true;
            p.Color(Fx.WithA(c, 0f), c, c, c, c, Fx.WithA(c, 0f));
            p.Step = (q, dt) =>
            {
                Camera cam = World.world.camera;
                if (cam == null)
                {
                    return;
                }
                float ortho = cam.orthographicSize;
                float k = q.K;
                float slide = (1f - Anim.OutCubic(Mathf.Clamp01(k * 6f))) * ortho * 0.08f;
                Vector3 cp = cam.transform.position;
                q.Pos = new Vector2(cp.x - slide, cp.y + ortho * y);
                // about 1/9 of the screen tall, whatever the zoom
                q.Px = ortho * 0.032f * size;
                if (q.R != null && q.R.sprite != null)
                {
                    // a long title shrinks to fit 92% of the screen's width
                    float fit = 0.92f * 2f * ortho * cam.aspect / Mathf.Max(1f, q.R.sprite.rect.width);
                    q.Px = Mathf.Min(q.Px, fit);
                }
            };
            _title.Add(p);
            _titleGen.Add(p.Gen);
        }

        public static void ClearTitle()
        {
            for (int i = 0; i < _title.Count; i++)
            {
                // only if it is still this title's (an expired one is another effect by now)
                Particle p = _title[i];
                if (p != null && p.Alive && p.Gen == _titleGen[i])
                {
                    p.Kill();
                }
            }
            _title.Clear();
            _titleGen.Clear();
        }

        public static void Clear()
        {
            _holds.Clear();
            _freezeLeft = 0f;
            _rateLeft = 0f;
            _owner = null;
            _lastEnd = -999;
            _idle = 0f;
            _veilA = _veilTarget = 0f;
            ClearTitle();
            UpdateVeil();
        }
    }

    /// <summary>A chunky 5x7 pixel font for title cards.</summary>
    internal static class PixelFont
    {
        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".####", "#....", "#....", "#.###", "#...#", "#...#", ".####" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
            ['!'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." },
            ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
            ['\''] = new[] { "..#..", "..#..", ".#...", ".....", ".....", ".....", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
            ['-'] = new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." },
            [':'] = new[] { ".....", ".##..", ".##..", ".....", ".##..", ".##..", "....." },
            [','] = new[] { ".....", ".....", ".....", ".....", ".##..", "..#..", ".#..." },
            [' '] = new[] { "...", "...", "...", "...", "...", "...", "..." }
        };

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>The text as a sprite (pivot at its centre); 'outline' gives the 1-pixel border around the letters.</summary>
        public static Sprite Render(string text, bool outline)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            text = text.ToUpperInvariant();
            string key = (outline ? "o:" : "f:") + text;
            if (_cache.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int w = 2;
            foreach (char ch in text)
            {
                w += (Glyphs.TryGetValue(ch, out string[] g) ? g[0].Length : 5) + 1;
            }
            int h = 7 + 4;
            Canvas c = new Canvas(w + 2, h);
            int x0 = 2;
            foreach (char ch in text)
            {
                if (!Glyphs.TryGetValue(ch, out string[] g))
                {
                    g = Glyphs['?'];
                }
                for (int row = 0; row < 7; row++)
                {
                    string line = g[row];
                    for (int col = 0; col < line.Length; col++)
                    {
                        if (line[col] != '#')
                        {
                            continue;
                        }
                        int x = x0 + col, y = h - 3 - row;
                        if (outline)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    c.Set(x + dx, y + dy, 1f, 1f);
                                }
                            }
                            c.Set(x + 1, y - 2, 1f, 1f);   // a drop shadow
                        }
                        else
                        {
                            // a lighter top half: the classic arcade shine
                            c.Set(x, y, row < 3 ? 1f : 0.82f, 1f);
                        }
                    }
                }
                x0 += g[0].Length + 1;
            }
            s = c.ToSprite("sakuga_title_" + key, new Vector2(0.5f, 0.5f));
            _cache[key] = s;
            return s;
        }
    }
}
