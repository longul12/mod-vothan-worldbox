using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// The engine's procedural pixel art, in the mod's style (Art): white and grey, lit from the top
    /// left (1.0 highlight, 0.82 body, 0.62 shade), tinted by the renderer, 1 texel = 1 sprite pixel of
    /// the unit that makes it. Everything is made on first use and cached.
    ///   Smear       a limb's motion smear: a bright head (fist, foot, knee) on a tapering streak
    ///   Disc        a spinning disc (ki discs, chakrams), frames of its turn
    ///   Boulder     a rough rock (torn-up ground, thrown boulders)
    ///   Pillar      a stone column with a broken top (pillars, walls), pivot at its foot
    ///   Spike       a stone spike, pivot at its foot
    ///   Crescent    a sword wave / wind blade crescent, opening to the left, flying right
    ///   LightSword  a sword of light pointing down (rains of blades), pivot at its tip
    ///   Sigil       a magic circle: two rings and a hexagram
    ///   Hex         a hexagon outline (barriers)
    ///   Fist        a fist (cones of fists), knuckles to the right
    ///   Fissure     a jagged crack along the ground
    ///   Bolt        a lightning zigzag
    ///   ChainLink   one link of a chain
    ///   Petal       a petal of light
    /// </summary>
    internal static class ChoreoArt
    {
        private static readonly Dictionary<long, Sprite[]> _smears = new Dictionary<long, Sprite[]>();
        private static readonly Dictionary<long, Sprite> _one = new Dictionary<long, Sprite>();
        private static readonly Dictionary<long, Sprite[]> _many = new Dictionary<long, Sprite[]>();

        private static long Key(int kind, int a, int b = 0, int c = 0)
        {
            return ((long)kind << 48) | ((long)(a & 0xFFFF) << 32) | ((long)(b & 0xFFFF) << 16) | (long)(c & 0xFFFF);
        }

        // ------------------------------------------------------------- smear --

        /// <summary>
        /// A limb in motion along +x: a round head 'head' pixels in radius at the front, a streak 'len'
        /// pixels long tapering to nothing behind it, 'thick' pixels at its widest. Four frames: the tail
        /// draws in after the head and thins away. Pivot at the head.
        /// </summary>
        public static Sprite[] Smear(int len, int head, int thick)
        {
            len = Mathf.Clamp(len, 3, 90);
            head = Mathf.Clamp(head, 0, 6);
            thick = Mathf.Clamp(thick, 1, 8);
            long key = Key(1, len, head, thick);
            if (_smears.TryGetValue(key, out Sprite[] s))
            {
                return s;
            }
            const int frames = 4;
            int w = len + head * 2 + 2, h = Mathf.Max(thick, head * 2 + 1) + 4;
            int cy = h / 2;
            int hx = len;   // the head's centre
            s = new Sprite[frames];
            for (int f = 0; f < frames; f++)
            {
                Canvas c = new Canvas(w, h);
                float tail = len * (f / (float)(frames + 1));
                float fade = 1f - 0.18f * f;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        float dx = x - hx, dy = y - cy;
                        // the head: a round blob, lit at the top
                        if (head > 0 && dx * dx + dy * dy <= (head + 0.35f) * (head + 0.35f))
                        {
                            bool lit = dy > 0.2f || dx > head - 0.6f;
                            c.Set(x, y, lit ? 1f : 0.86f, fade);
                            continue;
                        }
                        if (x > hx || x < tail)
                        {
                            continue;
                        }
                        // the streak: widest just behind the head, nothing at the tail
                        float u = (x - tail) / Mathf.Max(1f, hx - tail);
                        float half = Mathf.Max(0.5f, thick * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.05f, 0.62f, u)));
                        if (Mathf.Abs(dy) > half)
                        {
                            continue;
                        }
                        float a = Mathf.Lerp(0.25f, 1f, u) * fade;
                        if (u < 0.3f && Art.Dither(x, y) > u / 0.3f)
                        {
                            continue;   // the tail breaks up
                        }
                        c.Set(x, y, Mathf.Abs(dy) < half - 0.8f ? 1f : 0.8f, a);
                    }
                }
                s[f] = c.ToSprite("sakuga_smear_" + len + "_" + f, new Vector2((hx + 0.5f) / w, (cy + 0.5f) / h));
            }
            _smears[key] = s;
            return s;
        }

        // -------------------------------------------------------------- disc --

        /// <summary>A spinning disc 'r' pixels in radius (seen at a slant): 6 frames of its turn, a notch running round it.</summary>
        public static Sprite[] Disc(int r)
        {
            r = Mathf.Clamp(r, 2, 40);
            long key = Key(2, r);
            if (_many.TryGetValue(key, out Sprite[] s))
            {
                return s;
            }
            const int frames = 6;
            int ry = Mathf.Max(1, Mathf.RoundToInt(r * 0.45f));
            int w = r * 2 + 3, h = ry * 2 + 3;
            s = new Sprite[frames];
            for (int f = 0; f < frames; f++)
            {
                Canvas c = new Canvas(w, h);
                float spin = f / (float)frames * Mathf.PI * 2f;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        float nx = (x - (r + 1)) / (r + 0.5f), ny = (y - (ry + 1)) / (ry + 0.5f);
                        float e = Mathf.Sqrt(nx * nx + ny * ny);
                        if (e > 1f)
                        {
                            continue;
                        }
                        float ang = Mathf.Atan2(ny, nx);
                        float notch = Mathf.Repeat(ang - spin, Mathf.PI * 2f);
                        bool rim = e > 0.72f;
                        if (rim && notch < 0.5f)
                        {
                            continue;   // the notch in its edge shows it turning
                        }
                        float v = rim ? (ny > 0f ? 1f : 0.82f) : (e < 0.3f ? 1f : 0.7f);
                        c.Set(x, y, v, rim ? 1f : 0.55f);
                    }
                }
                s[f] = c.ToSprite("sakuga_disc_" + r + "_" + f, new Vector2(0.5f, 0.5f));
            }
            _many[key] = s;
            return s;
        }

        // ----------------------------------------------------------- boulder --

        /// <summary>A rough boulder 'r' pixels in radius, lit from the top left with a crack across it.</summary>
        public static Sprite Boulder(int r, int variant = 0)
        {
            r = Mathf.Clamp(r, 2, 40);
            variant &= 3;
            long key = Key(3, r, variant);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int n = r * 2 + 3;
            Canvas c = new Canvas(n, n);
            float cx = r + 1, cy = r + 1;
            bool In(int x, int y)
            {
                float dx = x - cx, dy = (y - cy) * 1.12f;
                float a = Mathf.Atan2(dy, dx);
                float rr = r * (0.82f + 0.18f * Art.Hash(Mathf.RoundToInt(a * 3f) + 9, variant, 77));
                return dx * dx + dy * dy <= rr * rr;
            }
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    if (!In(x, y))
                    {
                        continue;
                    }
                    bool lit = !In(x - 1, y + 1);
                    bool shade = !In(x + 1, y - 1) || !In(x + 1, y - 2);
                    float v = lit ? 1f : shade ? 0.6f : 0.8f;
                    if (Art.Hash(x, y, variant + 3) < 0.12f)
                    {
                        v -= 0.1f;
                    }
                    c.Set(x, y, v, 1f);
                }
            }
            // a crack across it
            int y0 = r + 1 + (variant - 1);
            for (int x = r / 2; x < r + r / 2 + 1; x++)
            {
                int y = y0 + (Art.Hash(x, 1, variant) < 0.4f ? 1 : 0);
                if (In(x, y))
                {
                    c.Set(x, y, 0.5f, 1f);
                }
            }
            s = c.ToSprite("sakuga_boulder_" + r, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        // ------------------------------------------------------------ pillar --

        /// <summary>A stone column 'w' pixels wide and 'h' tall with a broken top and strata, pivot at its foot.</summary>
        public static Sprite Pillar(int w, int h, int variant = 0)
        {
            w = Mathf.Clamp(w, 3, 40);
            h = Mathf.Clamp(h, 4, 120);
            variant &= 3;
            long key = Key(4, w, h, variant);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(w + 2, h + 2);
            for (int x = 1; x <= w; x++)
            {
                // the broken top: a jagged edge, higher in the middle
                float mid = 1f - Mathf.Abs((x - 0.5f - w * 0.5f) / (w * 0.5f));
                int top = h - Mathf.RoundToInt((1f - mid) * Mathf.Min(4, w * 0.5f) + Art.Hash(x, 3, variant) * 2f);
                for (int y = 0; y <= top; y++)
                {
                    float u = (x - 1) / (float)Mathf.Max(1, w - 1);
                    float v = u < 0.25f ? 1f : u > 0.72f ? 0.62f : 0.82f;
                    if (y >= top - 1)
                    {
                        v = Mathf.Min(1f, v + 0.12f);   // the top catches the light
                    }
                    else if ((y + variant * 3) % 7 == 0 && Art.Hash(x, y, variant) < 0.8f)
                    {
                        v -= 0.16f;   // strata
                    }
                    c.Set(x, y, v, 1f);
                }
            }
            s = c.ToSprite("sakuga_pillar_" + w + "_" + h, new Vector2(0.5f, 0.5f / (h + 2)));
            _one[key] = s;
            return s;
        }

        /// <summary>A stone spike 'w' pixels wide at its foot and 'h' tall, pivot at its foot.</summary>
        public static Sprite Spike(int w, int h)
        {
            w = Mathf.Clamp(w, 2, 40);
            h = Mathf.Clamp(h, 3, 120);
            long key = Key(5, w, h);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(w + 2, h + 1);
            for (int y = 0; y < h; y++)
            {
                float half = w * 0.5f * (1f - y / (float)h);
                for (int x = 0; x < w + 2; x++)
                {
                    float dx = x - (w + 2) * 0.5f + 0.5f;
                    if (Mathf.Abs(dx) > half + 0.25f)
                    {
                        continue;
                    }
                    c.Set(x, y, dx < -half * 0.3f ? 1f : dx > half * 0.35f ? 0.6f : 0.82f, 1f);
                }
            }
            s = c.ToSprite("sakuga_spike_" + w + "_" + h, new Vector2(0.5f, 0.5f / (h + 1)));
            _one[key] = s;
            return s;
        }

        // ---------------------------------------------------------- crescent --

        /// <summary>A crescent 'r' pixels tall, 'thick' at its middle, tips thinning, bulging to the right (it flies right).</summary>
        public static Sprite Crescent(int r, int thick)
        {
            r = Mathf.Clamp(r, 3, 60);
            thick = Mathf.Clamp(thick, 1, r);
            long key = Key(6, r, thick);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int rx = Mathf.Max(2, Mathf.RoundToInt(r * 0.55f));
            Canvas c = new Canvas(rx + thick + 4, r * 2 + 3);
            float cy = r + 1;
            for (int y = 0; y < c.H; y++)
            {
                float v = (y - cy) / (r + 0.5f);
                if (Mathf.Abs(v) > 1f)
                {
                    continue;
                }
                float outer = rx * Mathf.Sqrt(1f - v * v);
                float t = thick * (1f - v * v);
                for (int x = 0; x < c.W; x++)
                {
                    float d = x - 1f;
                    if (d > outer + 0.3f || d < outer - t - 0.3f)
                    {
                        continue;
                    }
                    bool rim = d > outer - 1.2f;
                    c.Set(x, y, rim ? 1f : 0.8f, rim ? 1f : Mathf.Lerp(0.5f, 0.9f, 1f - Mathf.Abs(v)));
                }
            }
            s = c.ToSprite("sakuga_crescent_" + r, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        // -------------------------------------------------------- light sword --

        /// <summary>A sword of light 'len' pixels long, pointing down, pivot at its tip.</summary>
        public static Sprite LightSword(int len)
        {
            len = Mathf.Clamp(len, 6, 60);
            long key = Key(7, len);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(7, len + 4);
            int blade = len;
            for (int y = 0; y < blade; y++)
            {
                bool tip = y < 2;
                c.Set(3, y, 1f, 1f);
                if (!tip)
                {
                    c.Set(2, y, 0.86f, 0.9f);
                    c.Set(4, y, 0.7f, 0.9f);
                }
            }
            for (int x = 0; x < 7; x++)
            {
                c.Set(x, blade, x == 0 || x == 6 ? 0.7f : 0.9f, 1f);   // the guard
            }
            c.Set(3, blade + 1, 0.8f, 1f);
            c.Set(3, blade + 2, 0.8f, 1f);
            c.Set(3, blade + 3, 1f, 1f);   // the pommel
            s = c.ToSprite("sakuga_lightsword_" + len, new Vector2(0.5f, 0.5f / (len + 4)));
            _one[key] = s;
            return s;
        }

        // ------------------------------------------------------------- sigil --

        /// <summary>A magic circle 'r' pixels in radius on the ground plane: two rings and a hexagram.</summary>
        public static Sprite Sigil(int r)
        {
            r = Mathf.Clamp(r, 5, 90);
            long key = Key(8, r);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int ry = Mathf.Max(2, Mathf.RoundToInt(r * Art.Ground));
            Canvas c = new Canvas(r * 2 + 3, ry * 2 + 3);
            float cx = r + 1, cy = ry + 1;
            void Dot(float x, float y, float v, float a)
            {
                c.Set(Mathf.RoundToInt(cx + x), Mathf.RoundToInt(cy + y * Art.Ground), v, a);
            }
            int steps = Mathf.Max(24, r * 8);
            for (int i = 0; i < steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                Dot(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 1f, 1f);
                Dot(Mathf.Cos(a) * r * 0.82f, Mathf.Sin(a) * r * 0.82f, 0.82f, 0.85f);
            }
            for (int tri = 0; tri < 2; tri++)
            {
                for (int k = 0; k < 3; k++)
                {
                    float a0 = (tri * 60f + k * 120f + 90f) * Mathf.Deg2Rad;
                    float a1 = (tri * 60f + (k + 1) * 120f + 90f) * Mathf.Deg2Rad;
                    Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r * 0.8f;
                    Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r * 0.8f;
                    int n = Mathf.Max(4, Mathf.RoundToInt(Vector2.Distance(p0, p1)));
                    for (int j = 0; j <= n; j++)
                    {
                        Vector2 p = Vector2.Lerp(p0, p1, j / (float)n);
                        Dot(p.x, p.y, 0.9f, 0.8f);
                    }
                }
            }
            s = c.ToSprite("sakuga_sigil_" + r, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        // --------------------------------------------------------------- hex --

        /// <summary>A hexagon outline 'r' pixels across (pointy top), 'thick' pixels wide.</summary>
        public static Sprite Hex(int r, int thick = 1)
        {
            r = Mathf.Clamp(r, 3, 80);
            thick = Mathf.Clamp(thick, 1, 4);
            long key = Key(9, r, thick);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(r * 2 + 3, r * 2 + 3);
            float cx = r + 1, cy = r + 1;
            for (int x = 0; x < c.W; x++)
            {
                for (int y = 0; y < c.H; y++)
                {
                    float dx = Mathf.Abs(x - cx), dy = Mathf.Abs(y - cy);
                    // pointy-top hexagon distance
                    float d = Mathf.Max(dx * 0.866f + dy * 0.5f, dy);
                    if (d <= r + 0.3f && d > r - thick - 0.3f)
                    {
                        c.Set(x, y, d > r - 0.8f ? 1f : 0.82f, 1f);
                    }
                    else if (d <= r - thick && Art.Dither(x, y) < 0.18f)
                    {
                        c.Set(x, y, 0.9f, 0.35f);   // a faint fill
                    }
                }
            }
            s = c.ToSprite("sakuga_hex_" + r, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        // -------------------------------------------------------------- fist --

        /// <summary>A fist 's' pixels big, knuckles to the right (cones of fists, a giant spectral fist).</summary>
        public static Sprite Fist(int size)
        {
            size = Mathf.Clamp(size, 3, 40);
            long key = Key(10, size);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int w = size + 1, h = Mathf.Max(3, Mathf.RoundToInt(size * 0.8f));
            Canvas c = new Canvas(w + 2, h + 2);
            if (size < 8)
            {
                // small: a rounded block with a knuckle line and a thumb (it reads as a fist at a few pixels)
                for (int x = 1; x <= w; x++)
                {
                    for (int y = 1; y <= h; y++)
                    {
                        if ((x == 1 || x == w) && (y == 1 || y == h))
                        {
                            continue;
                        }
                        float v = y == h ? 1f : y == 1 ? 0.62f : x == w ? 0.9f : 0.82f;
                        if (x == w - 1 && y > 1 && y < h && (y % 2 == 0))
                        {
                            v = 0.62f;
                        }
                        c.Set(x, y, v, 1f);
                    }
                }
                for (int x = 2; x < w - 1; x++)
                {
                    c.Set(x, 1, 0.7f, 1f);
                }
            }
            else
            {
                // big: the wrist at the back, the curled fingers in front (four of them, knuckles out along +x),
                // the thumb wrapped across the bottom of them
                int wrist = Mathf.RoundToInt(w * 0.24f), fingers = Mathf.RoundToInt(w * 0.6f);
                float lo = h * 0.22f, hi = h * 0.78f;
                for (int x = 1; x <= w; x++)
                {
                    for (int y = 1; y <= h; y++)
                    {
                        bool inWrist = x <= wrist && y >= lo && y <= hi;
                        bool corner = (x <= wrist + 1 || x >= w - 1) && (y <= 1 || y >= h) || x == w && (y <= 2 || y >= h - 1);
                        if (!(inWrist || x > wrist) || corner)
                        {
                            continue;
                        }
                        float v = y >= h - 1 ? 1f : y <= 2 ? 0.66f : 0.82f;
                        if (x > fingers)
                        {
                            // four fingers: a dark crease between each, the knuckle at the front of each lit
                            float f = (y - 1) / (float)h * 4f;
                            float fr = f - Mathf.Floor(f);
                            if (fr < 0.16f && y > 2)
                            {
                                v = 0.58f;
                            }
                            else if (x >= w - 1)
                            {
                                v = Mathf.Max(v, 0.95f);
                            }
                        }
                        if (x == fingers || x == wrist + 1)
                        {
                            v = Mathf.Min(v, 0.64f);   // where the fingers fold, and the wrist line
                        }
                        c.Set(x, y, v, 1f);
                    }
                }
                // the thumb across the bottom of the fingers
                int t0 = Mathf.RoundToInt(w * 0.42f), t1 = Mathf.RoundToInt(w * 0.82f);
                for (int x = t0; x <= t1; x++)
                {
                    c.Set(x, 2, 0.9f, 1f);
                    c.Set(x, 3, 0.74f, 1f);
                    c.Set(x, 4, 0.6f, 1f);
                }
            }
            s = c.ToSprite("sakuga_fist_" + size, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        // ----------------------------------------------------------- fissure --

        /// <summary>A jagged crack 'len' pixels long along +x (with a side branch), pivot at its start.</summary>
        public static Sprite Fissure(int len, int variant = 0)
        {
            len = Mathf.Clamp(len, 4, 120);
            variant &= 7;
            long key = Key(11, len, variant);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int h = Mathf.Max(7, len / 3);
            Canvas c = new Canvas(len + 2, h);
            float y = h * 0.5f;
            int branchAt = len / 3 + Mathf.RoundToInt(Art.Hash(variant, 2, 5) * len * 0.3f);
            float by = 0f;
            bool branch = false;
            for (int x = 0; x < len; x++)
            {
                y += (Art.Hash(x, variant, 31) - 0.5f) * 1.6f;
                y = Mathf.Clamp(y, 1f, h - 2f);
                float u = x / (float)len;
                float a = Mathf.Lerp(1f, 0.45f, u);
                c.Set(x, Mathf.RoundToInt(y), 1f, a);
                if (u < 0.35f)
                {
                    c.Set(x, Mathf.RoundToInt(y) - 1, 0.8f, a * 0.8f);   // wide near its start
                }
                if (x == branchAt)
                {
                    branch = true;
                    by = y;
                }
                if (branch && x > branchAt && x < branchAt + len / 4)
                {
                    by += Art.Hash(x, variant, 9) < 0.5f ? 0.9f : 0.3f;
                    if (by < h - 1)
                    {
                        c.Set(x, Mathf.RoundToInt(by), 1f, 0.55f);
                    }
                }
            }
            s = c.ToSprite("sakuga_fissure_" + len, new Vector2(0.5f / (len + 2), 0.5f));
            _one[key] = s;
            return s;
        }

        // -------------------------------------------------------------- bolt --

        /// <summary>A lightning zigzag 'len' pixels long along +x, pivot at its start.</summary>
        public static Sprite Bolt(int len, int variant = 0)
        {
            len = Mathf.Clamp(len, 4, 120);
            variant &= 7;
            long key = Key(12, len, variant);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int h = Mathf.Max(7, len / 3);
            Canvas c = new Canvas(len + 2, h);
            float y = h * 0.5f, dy = 0f;
            for (int x = 0; x < len; x++)
            {
                if (x % 3 == 0)
                {
                    dy = (Art.Hash(x, variant, 13) - 0.5f) * 2.4f;
                }
                y = Mathf.Clamp(y + dy, 1f, h - 2f);
                c.Set(x, Mathf.RoundToInt(y), 1f, 1f);
                c.Set(x, Mathf.RoundToInt(y) + 1, 0.8f, 0.6f);
            }
            s = c.ToSprite("sakuga_bolt_" + len, new Vector2(0.5f / (len + 2), 0.5f));
            _one[key] = s;
            return s;
        }

        // -------------------------------------------------------------- bits --

        /// <summary>One link of a chain (3x5, its long side along +x).</summary>
        public static Sprite ChainLink()
        {
            long key = Key(13, 0);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(5, 3);
            for (int x = 0; x < 5; x++)
            {
                c.Set(x, 2, 1f, 1f);
                c.Set(x, 0, 0.7f, 1f);
            }
            c.Set(0, 1, 0.85f, 1f);
            c.Set(4, 1, 0.85f, 1f);
            s = c.ToSprite("sakuga_chainlink", new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }

        /// <summary>A small petal of light (two variants).</summary>
        public static Sprite Petal(int variant)
        {
            variant &= 1;
            long key = Key(14, variant);
            if (_one.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(3, 2);
            if (variant == 0)
            {
                c.Set(0, 0, 0.82f, 1f);
                c.Set(1, 0, 1f, 1f);
                c.Set(1, 1, 1f, 1f);
                c.Set(2, 1, 0.82f, 1f);
            }
            else
            {
                c.Set(0, 1, 0.82f, 1f);
                c.Set(1, 1, 1f, 1f);
                c.Set(1, 0, 1f, 1f);
                c.Set(2, 0, 0.82f, 0.8f);
            }
            s = c.ToSprite("sakuga_petal_" + variant, new Vector2(0.5f, 0.5f));
            _one[key] = s;
            return s;
        }
    }
}
