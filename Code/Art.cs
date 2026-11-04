using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Procedural pixel art. Every effect is drawn here, pixel by pixel, at the
    /// game's own resolution (1 texel = 1 unit-sprite pixel, the transform scales
    /// it by the unit's size), so the effects look like they belong to the
    /// sprites they come out of. Art is white/grey and gets its colour from the
    /// renderer (dust takes the colour of the ground it comes from).
    ///
    /// Shading convention, like the game's sprites: light from the top left.
    /// Grey levels: 1.0 highlight, 0.82 body, 0.62 shade.
    /// </summary>
    internal static class Art
    {
        public const float Ground = 0.62f;   // ground-plane ellipses are this flat (y/x)

        public static Sprite Pixel;
        public static Sprite[][] Puffs;       // [variant][frame]: grow, then dissolve (fire)
        public static Sprite[][] Dust;        // same shapes, soft: flat shading and see-through edges
        public static Sprite[] Glint;         // 4-point twinkle
        public static Sprite[] Chunks;        // debris
        public static Sprite[] Sparks;        // by length
        public static Sprite[] Lines;         // speed lines by length
        public static Sprite[] Bursts;        // impact starbursts
        public static Sprite[] HitStars;      // small impact stars for punches and kicks, by size
        public static CrackSet[] Cracks;
        public static CrackSet[] SmallCracks;

        internal sealed class CrackSet
        {
            public Sprite Dark;   // fissures and the crater floor
            public Sprite Light;  // crater rim and loose rubble
            public Sprite Glow;   // the hot core of the fissures
        }

        private static readonly Dictionary<long, Sprite> _rings = new Dictionary<long, Sprite>();
        private static readonly Dictionary<string, Sprite[]> _arcs = new Dictionary<string, Sprite[]>();
        private static readonly Dictionary<long, Sprite> _disks = new Dictionary<long, Sprite>();

        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>Ordered-dither threshold 0..1 for a pixel: the classic pixel-art fade.</summary>
        public static float Dither(int x, int y)
        {
            return (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
        }

        /// <summary>Stable per-pixel noise 0..1.</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65536f;
            }
        }

        public static void Init()
        {
            Canvas px = new Canvas(1, 1);
            px.Set(0, 0, 1f, 1f);
            Pixel = px.ToSprite("sakuga_pixel", new Vector2(0.5f, 0.5f));

            System.Random rng = new System.Random(1337);
            Puffs = new Sprite[6][];
            int[] radii = { 3, 4, 5, 5, 6, 7 };
            for (int v = 0; v < Puffs.Length; v++)
            {
                Puffs[v] = MakePuff(radii[v], rng, v, false);
            }
            Dust = new Sprite[6][];
            for (int v = 0; v < Dust.Length; v++)
            {
                Dust[v] = MakePuff(radii[v], rng, v + 11, true);
            }
            Glint = MakeGlint();
            Chunks = MakeChunks();
            Sparks = new[] { MakeStreak(3, true), MakeStreak(5, true), MakeStreak(7, true) };
            Lines = new[] { MakeStreak(8, false), MakeStreak(12, false), MakeStreak(18, false) };
            Bursts = new[] { MakeBurst(26, 11, rng), MakeBurst(26, 13, rng) };
            HitStars = new[] { MakeBurst(5, 7, rng), MakeBurst(8, 8, rng), MakeBurst(12, 10, rng) };
            Cracks = new CrackSet[5];
            for (int i = 0; i < Cracks.Length; i++)
            {
                Cracks[i] = MakeCracks(44, 8, rng.Next(7, 11), 12, 36, rng);
            }
            SmallCracks = new CrackSet[3];
            for (int i = 0; i < SmallCracks.Length; i++)
            {
                SmallCracks[i] = MakeCracks(20, 4, rng.Next(4, 7), 5, 15, rng);
            }
        }

        // ------------------------------------------------------------- rings --

        /// <summary>
        /// An ellipse outline, rx pixels wide (half), 'thick' pixels thick, pixel-perfect:
        /// the filled ellipse minus itself eroded 'thick' times. The outermost pixel row is
        /// full white, the rows inside it a shade darker. Cached.
        /// </summary>
        public static Sprite Ring(int rx, int thick, float ratio = Ground, int dashes = 0)
        {
            rx = Mathf.Clamp(rx, 1, 140);
            thick = Mathf.Clamp(thick, 1, rx);
            long key = (long)rx | ((long)thick << 8) | ((long)Mathf.RoundToInt(ratio * 100f) << 16) | ((long)dashes << 28);
            if (_rings.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int ry = Mathf.Max(1, Mathf.RoundToInt(rx * ratio));
            Canvas c = new Canvas(rx * 2 + 3, ry * 2 + 3);
            bool[] inside = new bool[c.W * c.H];
            float ox = rx + 0.5f, oy = ry + 0.5f;
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    float dx = (x - (rx + 1)) / ox, dy = (y - (ry + 1)) / oy;
                    inside[y * c.W + x] = dx * dx + dy * dy <= 1f;
                }
            }
            int[] depth = Erode(inside, c.W, c.H, thick);
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    int d = depth[y * c.W + x];
                    if (d <= 0 || d > thick)
                    {
                        continue;
                    }
                    if (dashes > 0)
                    {
                        float ang = Mathf.Atan2((y - (ry + 1)) / (float)ry, (x - (rx + 1)) / (float)rx);
                        int seg = (int)((ang + Mathf.PI) / (2f * Mathf.PI) * dashes * 2f);
                        if ((seg & 1) == 1)
                        {
                            continue;
                        }
                    }
                    c.Set(x, y, d == 1 ? 1f : 0.8f, d == 1 ? 1f : 0.85f);
                }
            }
            s = c.ToSprite("sakuga_ring_" + rx + "_" + thick, new Vector2(0.5f, 0.5f));
            _rings[key] = s;
            return s;
        }

        /// <summary>
        /// A filled ellipse. 'soft' fades its outer band with ordered dithering
        /// (flashes, shadows); otherwise hard-edged. Cached.
        /// </summary>
        public static Sprite Disk(int rx, float ratio, float soft)
        {
            rx = Mathf.Clamp(rx, 1, 140);
            long key = (long)rx | ((long)Mathf.RoundToInt(ratio * 100f) << 8) | ((long)Mathf.RoundToInt(soft * 100f) << 20);
            if (_disks.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int ry = Mathf.Max(1, Mathf.RoundToInt(rx * ratio));
            Canvas c = new Canvas(rx * 2 + 3, ry * 2 + 3);
            float ox = rx + 0.5f, oy = ry + 0.5f;
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    float dx = (x - (rx + 1)) / ox, dy = (y - (ry + 1)) / oy;
                    float e = Mathf.Sqrt(dx * dx + dy * dy);
                    if (e > 1f)
                    {
                        continue;
                    }
                    if (soft > 0f)
                    {
                        float edge = 1f - soft;
                        if (e > edge && Dither(x, y) < (e - edge) / soft)
                        {
                            continue;
                        }
                    }
                    c.Set(x, y, 1f, 1f);
                }
            }
            s = c.ToSprite("sakuga_disk_" + rx, new Vector2(0.5f, 0.5f));
            _disks[key] = s;
            return s;
        }

        /// <summary>Chessboard-free erosion depth (4-neighbourhood): 1 = outline, 2 = next ring in, ...</summary>
        private static int[] Erode(bool[] inside, int w, int h, int maxDepth)
        {
            int[] depth = new int[w * h];
            bool[] cur = (bool[])inside.Clone();
            for (int d = 1; d <= maxDepth + 1; d++)
            {
                bool[] next = new bool[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (!cur[i])
                        {
                            continue;
                        }
                        bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1
                            || !cur[i - 1] || !cur[i + 1] || !cur[i - w] || !cur[i + w];
                        if (edge)
                        {
                            depth[i] = d;
                        }
                        else
                        {
                            next[i] = true;
                        }
                    }
                }
                cur = next;
            }
            for (int i = 0; i < depth.Length; i++)
            {
                if (cur[i])
                {
                    depth[i] = maxDepth + 2;   // deep inside
                }
            }
            return depth;
        }

        // -------------------------------------------------------------- arcs --

        /// <summary>
        /// A kick's swoosh: a crescent swept around an ellipse (r texels wide, ratio tall) from angle
        /// a0 to a1 (degrees, counter-clockwise, y up; a1 below a0 sweeps clockwise). Over its frames
        /// the head races ahead, the tail follows and the crescent thins away. Thin at the tail,
        /// fullest just behind the head; the outer rim white, the inside a shade darker. Cached.
        /// </summary>
        public static Sprite[] Arc(int r, float ratio, float a0, float a1, int thick)
        {
            string key = r + "_" + ratio + "_" + a0 + "_" + a1 + "_" + thick;
            if (_arcs.TryGetValue(key, out Sprite[] cached))
            {
                return cached;
            }
            const int frames = 8;
            int ry = Mathf.Max(1, Mathf.RoundToInt(r * ratio));
            int w = r * 2 + 3, h = ry * 2 + 3, cx = r + 1, cy = ry + 1;
            float sweep = Mathf.Abs(a1 - a0), dir = Mathf.Sign(a1 - a0);
            Sprite[] result = new Sprite[frames];
            for (int f = 0; f < frames; f++)
            {
                float k = f / (float)(frames - 1);
                float head = Anim.OutCubic(Mathf.Clamp01(k / 0.5f));
                float tail = Mathf.Min(Anim.Smooth(0.12f, 1f, k), head - 0.03f);
                float thin = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((k - 0.55f) / 0.45f));
                float fade = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((k - 0.65f) / 0.35f));
                Canvas c = new Canvas(w, h);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float nx = (x - cx) / (r + 0.5f), ny = (y - cy) / (ry + 0.5f);
                        float e = Mathf.Sqrt(nx * nx + ny * ny);
                        if (e > 1f || e < 0.15f)
                        {
                            continue;
                        }
                        float d = Mathf.Repeat((Mathf.Atan2(ny, nx) * Mathf.Rad2Deg - a0) * dir, 360f);
                        if (sweep < 360f && d > sweep)
                        {
                            continue;
                        }
                        float s = d / Mathf.Max(1f, sweep);
                        if (s < tail || s > head)
                        {
                            continue;
                        }
                        float u = (s - tail) / Mathf.Max(0.001f, head - tail);
                        float width = Mathf.Max(1f, thick * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 0.62f, u)) * thin);
                        float rr = e * r;
                        if (rr < r - width)
                        {
                            continue;
                        }
                        bool rim = rr > r - 1.3f;
                        c.Set(x, y, rim ? 1f : 0.8f, Mathf.Lerp(0.35f, 1f, u) * fade);
                    }
                }
                result[f] = c.ToSprite("sakuga_arc_" + key + "_" + f, new Vector2(0.5f, 0.5f));
            }
            _arcs[key] = result;
            return result;
        }

        // ------------------------------------------------------------- puffs --

        /// <summary>
        /// A puff of dust/smoke/fire: three overlapping blobs, shaded from the top left,
        /// growing over the first half of its frames and dissolving (ordered dither) over the rest.
        /// </summary>
        private static Sprite[] MakePuff(int rmax, System.Random rng, int variant, bool soft)
        {
            const int frames = 8;
            Vector2[] blobs = new Vector2[3];
            float[] br = new float[3];
            blobs[0] = Vector2.zero;
            br[0] = 1f;
            for (int i = 1; i < 3; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                blobs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.35f + 0.2f * (float)rng.NextDouble());
                br[i] = 0.55f + 0.2f * (float)rng.NextDouble();
            }
            int size = rmax * 2 + 3;
            Sprite[] result = new Sprite[frames];
            for (int f = 0; f < frames; f++)
            {
                float k = f / (float)(frames - 1);
                float grow = 1f - (1f - Mathf.Min(1f, k * 1.6f)) * (1f - Mathf.Min(1f, k * 1.6f));
                float r = Mathf.Lerp(0.45f, 1f, grow) * rmax;
                float dissolve = Mathf.Clamp01((k - 0.4f) / 0.6f);
                Canvas c = new Canvas(size, size);
                bool In(int x, int y)
                {
                    float px = x - (rmax + 1), py = y - (rmax + 1);
                    for (int i = 0; i < 3; i++)
                    {
                        float bx = px - blobs[i].x * r, by = py - blobs[i].y * r;
                        float rr = br[i] * r * 0.82f + 0.35f;
                        if (bx * bx + by * by <= rr * rr)
                        {
                            return true;
                        }
                    }
                    return false;
                }
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (!In(x, y))
                        {
                            continue;
                        }
                        // dissolve: ragged from the outside in (noise + distance from the middle), not a grid
                        if (dissolve > 0f)
                        {
                            float dx = x - (rmax + 1), dy = y - (rmax + 1);
                            float edge = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(1f, r));
                            float keep = 0.55f * Hash(x, y, variant) + 0.45f * (1f - edge);
                            if (keep < dissolve * 1.1f)
                            {
                                continue;
                            }
                        }
                        bool lit = !In(x - 1, y + 1);
                        bool shaded = !lit && (!In(x + 1, y - 1) || !In(x, y - 1));
                        if (soft)
                        {
                            // dust: hardly any shading, and its rim is see-through, so it reads as a cloud, not a rock
                            bool rim = !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1);
                            c.Set(x, y, lit ? 1f : shaded ? 0.86f : 0.94f, rim ? 0.55f : 1f);
                        }
                        else
                        {
                            c.Set(x, y, lit ? 1f : shaded ? 0.62f : 0.82f, 1f);
                        }
                    }
                }
                result[f] = c.ToSprite("sakuga_puff_" + rmax + "_" + f + (soft ? "s" : ""), new Vector2(0.5f, 0.5f));
            }
            return result;
        }

        // ------------------------------------------------------------ glints --

        /// <summary>The anime "ting!": a 4-point star that flares and fades.</summary>
        private static Sprite[] MakeGlint()
        {
            int[] arms = { 1, 3, 5, 4, 2, 1 };
            Sprite[] result = new Sprite[arms.Length];
            for (int f = 0; f < arms.Length; f++)
            {
                Canvas c = new Canvas(13, 13);
                int L = arms[f];
                c.Set(6, 6, 1f, 1f);
                for (int k = 1; k <= L; k++)
                {
                    float a = 1f - (k - 1f) / (L + 0.5f) * 0.65f;
                    c.Set(6 + k, 6, 1f, a);
                    c.Set(6 - k, 6, 1f, a);
                    c.Set(6, 6 + k, 1f, a);
                    c.Set(6, 6 - k, 1f, a);
                }
                if (L >= 3)
                {
                    c.Set(5, 5, 1f, 0.8f);
                    c.Set(7, 7, 1f, 0.8f);
                    c.Set(5, 7, 1f, 0.8f);
                    c.Set(7, 5, 1f, 0.8f);
                }
                if (L >= 5)
                {
                    c.Set(4, 4, 1f, 0.45f);
                    c.Set(8, 8, 1f, 0.45f);
                    c.Set(4, 8, 1f, 0.45f);
                    c.Set(8, 4, 1f, 0.45f);
                }
                result[f] = c.ToSprite("sakuga_glint_" + f, new Vector2(0.5f, 0.5f));
            }
            return result;
        }

        // ------------------------------------------------------------ debris --

        private static Sprite[] MakeChunks()
        {
            string[] shapes =
            {
                "11/11",
                "111/111",
                "11/11/11",
                "011/111/110",
                "0110/1111/1111/0110",
                "110/111",
                "1",
                "10/11"
            };
            Sprite[] result = new Sprite[shapes.Length];
            for (int i = 0; i < shapes.Length; i++)
            {
                string[] rows = shapes[i].Split('/');
                int h = rows.Length, w = rows[0].Length;
                Canvas c = new Canvas(w, h);
                bool On(int x, int y)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h)
                    {
                        return false;
                    }
                    return rows[h - 1 - y][x] == '1';
                }
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (!On(x, y))
                        {
                            continue;
                        }
                        float tone = 0.8f;
                        if (!On(x - 1, y) || !On(x, y + 1))
                        {
                            tone = 1f;
                        }
                        if (!On(x + 1, y) && !On(x, y - 1) && w > 1)
                        {
                            tone = 0.58f;
                        }
                        c.Set(x, y, tone, 1f);
                    }
                }
                result[i] = c.ToSprite("sakuga_chunk_" + i, new Vector2(0.5f, 0.5f));
            }
            return result;
        }

        /// <summary>
        /// A 1-pixel streak along +y. Sparks: bright head, fading tail.
        /// Speed lines: thin at both ends, solid in the front third.
        /// </summary>
        private static Sprite MakeStreak(int len, bool spark)
        {
            Canvas c = new Canvas(1, len);
            for (int y = 0; y < len; y++)
            {
                float k = y / (float)(len - 1);   // 0 tail .. 1 head
                float a;
                if (spark)
                {
                    a = Mathf.Lerp(0.25f, 1f, k);
                }
                else
                {
                    a = k < 0.66f ? Mathf.Lerp(0f, 1f, k / 0.66f) : Mathf.Lerp(1f, 0.2f, (k - 0.66f) / 0.34f);
                }
                c.Set(0, y, 1f, a);
            }
            return c.ToSprite("sakuga_streak_" + len + (spark ? "s" : "l"), new Vector2(0.5f, 0.5f));
        }

        // ------------------------------------------------------------- burst --

        private static readonly Dictionary<int, Sprite[]> _bursts = new Dictionary<int, Sprite[]>();

        /// <summary>A starburst 'r' pixels in radius (two variants, cached), drawn at the unit's own pixel size.</summary>
        public static Sprite Burst(int r, int variant = -1)
        {
            r = Mathf.Clamp(r, 4, 90);
            if (!_bursts.TryGetValue(r, out Sprite[] set))
            {
                System.Random rng = new System.Random(4242 + r);
                set = new[] { MakeBurst(r, Mathf.Clamp(8 + r / 4, 9, 22), rng), MakeBurst(r, Mathf.Clamp(10 + r / 4, 11, 24), rng) };
                _bursts[r] = set;
            }
            return set[variant < 0 ? UnityEngine.Random.Range(0, set.Length) : variant % set.Length];
        }

        /// <summary>The impact starburst: a jagged star, white core, used for one or two frames.</summary>
        private static Sprite MakeBurst(int r, int spikes, System.Random rng)
        {
            int ry = Mathf.RoundToInt(r * 0.8f);
            Canvas c = new Canvas(r * 2 + 3, ry * 2 + 3);
            float[] len = new float[spikes];
            for (int i = 0; i < spikes; i++)
            {
                len[i] = 0.7f + 0.3f * (float)rng.NextDouble();
            }
            float phase = (float)rng.NextDouble();
            for (int y = 0; y < c.H; y++)
            {
                for (int x = 0; x < c.W; x++)
                {
                    float dx = (x - (r + 1)) / (r + 0.5f), dy = (y - (ry + 1)) / (ry + 0.5f);
                    float e = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f + phase) % 1f * spikes;
                    int si = (int)ang % spikes;
                    float frac = ang - Mathf.Floor(ang);
                    float tri = 1f - Mathf.Abs(frac - 0.5f) * 2f;             // 0 between spikes, 1 at a tip
                    float reach = 0.38f + (len[si] - 0.38f) * tri * tri;
                    if (e > reach)
                    {
                        continue;
                    }
                    c.Set(x, y, e < 0.3f ? 1f : 0.92f, 1f);
                }
            }
            return c.ToSprite("sakuga_burst", new Vector2(0.5f, 0.5f));
        }

        // ------------------------------------------------------------ cracks --

        /// <summary>
        /// A crater with fissures running out of it, on the ground plane. Three layers that are
        /// tinted and faded separately: dark (fissures, crater floor), light (rim, rubble)
        /// and glow (the hot inner part of the fissures).
        /// </summary>
        private static CrackSet MakeCracks(int rx, int crater, int count, int minLen, int maxLen, System.Random rng)
        {
            int ry = Mathf.RoundToInt(rx * Ground);
            int w = rx * 2 + 5, h = ry * 2 + 5;
            int cx = rx + 2, cy = ry + 2;
            Canvas dark = new Canvas(w, h);
            Canvas light = new Canvas(w, h);
            Canvas glow = new Canvas(w, h);

            // crater: dark dithered floor, deeper in the middle; lit far rim, dark near rim
            int cry = Mathf.Max(1, Mathf.RoundToInt(crater * Ground));
            for (int y = -cry - 2; y <= cry + 2; y++)
            {
                for (int x = -crater - 2; x <= crater + 2; x++)
                {
                    float e = Mathf.Sqrt((x / (crater + 0.5f)) * (x / (crater + 0.5f)) + (y / (cry + 0.5f)) * (y / (cry + 0.5f)));
                    if (e <= 1f)
                    {
                        float depth = 1f - e;
                        if (Dither(x + cx, y + cy) < 0.35f + depth * 0.9f)
                        {
                            dark.Set(cx + x, cy + y, 1f, Mathf.Lerp(0.45f, 0.85f, depth));
                        }
                        if (e < 0.55f && Dither(x + cx + 1, y + cy) < 0.6f)
                        {
                            glow.Set(cx + x, cy + y, 1f, 0.75f);
                        }
                    }
                    else if (e <= 1.22f)
                    {
                        if (y > 0)
                        {
                            light.Set(cx + x, cy + y, 1f, 0.9f);          // far rim catches the light
                        }
                        else if (Dither(x + cx, y + cy) < 0.5f)
                        {
                            dark.Set(cx + x, cy + y, 1f, 0.5f);
                        }
                    }
                }
            }

            // fissures: wobbly random walks out of the crater, thick near it, with side branches
            float baseAng = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                float a = baseAng + i * Mathf.PI * 2f / count + ((float)rng.NextDouble() - 0.5f) * 0.6f;
                float len = Mathf.Lerp(minLen, maxLen, (float)rng.NextDouble());
                Walk(dark, glow, cx, cy, Mathf.Cos(a) * crater * 0.85f, Mathf.Sin(a) * crater * 0.85f, a, len, true, rng);
            }

            // loose rubble around the crater
            int rubble = 6 + count;
            for (int i = 0; i < rubble; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = crater * (1.3f + (float)rng.NextDouble() * 1.4f);
                int x = cx + Mathf.RoundToInt(Mathf.Cos(a) * d), y = cy + Mathf.RoundToInt(Mathf.Sin(a) * d * Ground);
                light.Set(x, y, 1f, 0.85f);
                if (rng.NextDouble() < 0.5)
                {
                    dark.Set(x + 1, y - 1, 1f, 0.6f);   // its shadow
                }
            }

            return new CrackSet
            {
                Dark = dark.ToSprite("sakuga_crack_dark", new Vector2(0.5f, 0.5f)),
                Light = light.ToSprite("sakuga_crack_light", new Vector2(0.5f, 0.5f)),
                Glow = glow.ToSprite("sakuga_crack_glow", new Vector2(0.5f, 0.5f))
            };
        }

        private static void Walk(Canvas dark, Canvas glow, int cx, int cy, float fx, float fy, float ang, float len, bool main, System.Random rng)
        {
            int steps = Mathf.RoundToInt(len);
            for (int s = 0; s < steps; s++)
            {
                float k = s / (float)steps;
                ang += ((float)rng.NextDouble() - 0.5f) * 0.7f;
                fx += Mathf.Cos(ang);
                fy += Mathf.Sin(ang);
                int x = cx + Mathf.RoundToInt(fx), y = cy + Mathf.RoundToInt(fy * Ground);
                float alpha = main ? Mathf.Lerp(0.95f, 0.55f, k) : 0.6f;
                dark.Set(x, y, 1f, alpha);
                if (main && k < 0.3f)
                {
                    dark.Set(x, y - 1, 1f, alpha * 0.8f);   // wide near the crater
                }
                if (main && k < 0.55f)
                {
                    glow.Set(x, y, 1f, Mathf.Lerp(1f, 0.35f, k / 0.55f));
                }
                if (main && k > 0.2f && rng.NextDouble() < 0.07)
                {
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    Walk(dark, glow, cx, cy, fx, fy, ang + side * (0.5f + (float)rng.NextDouble() * 0.6f), 3f + (float)rng.NextDouble() * len * 0.3f, false, rng);
                }
            }
        }
    }

    /// <summary>A small RGBA pixel buffer that becomes a point-filtered sprite (1 texel = 1 unit).</summary>
    internal sealed class Canvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public Canvas(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color32[w * h];
        }

        public void Set(int x, int y, float value, float alpha)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H)
            {
                return;
            }
            byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            Color32 old = Px[y * W + x];
            if (old.a > a)
            {
                return;   // the strongest stroke wins
            }
            Px[y * W + x] = new Color32(v, v, v, a);
        }

        public Sprite ToSprite(string name, Vector2 pivot)
        {
            Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            t.SetPixels32(Px);
            t.Apply(false, !Dev.On);   // dev tools can save the art (Dev 'art')
            Sprite s = Sprite.Create(t, new Rect(0f, 0f, W, H), pivot, 1f, 0u, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }
    }
}
