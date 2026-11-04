using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The Primordials' own effects: nothing they do is drawn as a bare line or a flat block.
    ///   Band    a soft-edged stroke with rounded, tapering ends and a grain of light running along it
    ///           (three cuts: a dithered glow, a body, a hard core)
    ///   Beam    void rim, glow, coloured body with flowing grain, white core, two strands of light
    ///           twisting round it, rings sliding down it, sparks shed off it, a flare at each end
    ///   Bolt    branching lightning: a wide glow, a coloured channel and a white core on every kink,
    ///           forks that fork again, a flash and sparks where it strikes
    ///   Crack   a fissure of light racing out over the ground: tapering, glowing, branching, embers
    ///   Ray     a tapered shaft of light (wide at its source, fading to a point)
    ///   Slash   a blade of light, pointed at both ends, a glow round a white edge
    ///   Orb     a ball of light with a halo, a core and a burning trail of puffs and sparks
    /// </summary>
    internal static class PrimFx
    {
        public const int BandW = 64, BandH = 16;
        private const int RayW = 96, RayH = 24, SlashW = 96, SlashH = 12;

        private static readonly Dictionary<int, Sprite> _sprites = new Dictionary<int, Sprite>();
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        private static float Dith(int x, int y)
        {
            return (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
        }

        // ------------------------------------------------------------- sprites --

        /// <summary>A stroke: 'kind' 0 glow (dithered most of the way in), 1 body (dithered rim, flowing grain), 2 core (solid).</summary>
        internal static Sprite BandSprite(int kind, int variant)
        {
            int key = 100 + kind * 10 + variant;
            if (_sprites.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(BandW, BandH);
            const float cap = 8f;
            for (int y = 0; y < BandH; y++)
            {
                for (int x = 0; x < BandW; x++)
                {
                    float dy = Mathf.Abs(y + 0.5f - BandH * 0.5f) / (BandH * 0.5f);
                    float ex = x + 0.5f < cap ? (cap - x - 0.5f) / cap : x + 0.5f > BandW - cap ? (x + 0.5f - (BandW - cap)) / cap : 0f;
                    float e = Mathf.Sqrt(ex * ex + dy * dy);
                    if (e > 1f)
                    {
                        continue;
                    }
                    float solid = kind == 0 ? 0.3f : kind == 1 ? 0.7f : 1f;
                    if (e > solid && Dith(x + variant, y) < (e - solid) / (1f - solid))
                    {
                        continue;
                    }
                    float v = 1f;
                    if (kind == 1)
                    {
                        // a grain of light flowing along it (diagonal streaks)
                        v = 0.78f + 0.22f * Mathf.Sin(x * 0.55f + y * 1.4f + variant * 2.1f);
                    }
                    else if (kind == 0)
                    {
                        v = 0.85f + 0.15f * Mathf.Sin(x * 0.3f - y * 0.9f + variant * 1.7f);
                    }
                    c.Set(x, y, v, 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_band_" + kind + "_" + variant, new Vector2(0.5f, 0.5f));
            _sprites[key] = s;
            return s;
        }

        /// <summary>A shaft of light: wide at its source (the pivot), tapering and fading to a point.</summary>
        private static Sprite RaySprite()
        {
            if (_sprites.TryGetValue(1, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(RayW, RayH);
            for (int x = 0; x < RayW; x++)
            {
                float p = (x + 0.5f) / RayW;
                float half = RayH * 0.5f * Mathf.Pow(1f - p, 0.6f);
                float keep = Mathf.Pow(1f - p, 0.9f);
                for (int y = 0; y < RayH; y++)
                {
                    float dy = Mathf.Abs(y + 0.5f - RayH * 0.5f);
                    if (dy > half)
                    {
                        continue;
                    }
                    float e = half > 0f ? dy / half : 1f;
                    bool solid = e < 0.5f && p < 0.55f;
                    if (!solid && Dith(x, y) > keep * (1f - e * e * 0.85f))
                    {
                        continue;
                    }
                    c.Set(x, y, 0.85f + 0.15f * (1f - e), 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_ray", new Vector2(0f, 0.5f));
            _sprites[1] = s;
            return s;
        }

        /// <summary>A blade: pointed at both ends ('soft' dithers its rim into a glow).</summary>
        private static Sprite SlashSprite(bool soft)
        {
            int key = soft ? 3 : 2;
            if (_sprites.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            Canvas c = new Canvas(SlashW, SlashH);
            for (int x = 0; x < SlashW; x++)
            {
                float u = (x + 0.5f) / SlashW * 2f - 1f;
                float half = SlashH * 0.5f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.9f);
                for (int y = 0; y < SlashH; y++)
                {
                    float dy = Mathf.Abs(y + 0.5f - SlashH * 0.5f);
                    if (dy > half)
                    {
                        continue;
                    }
                    float e = half > 0f ? dy / half : 1f;
                    if (soft && e > 0.35f && Dith(x, y) < (e - 0.35f) / 0.65f)
                    {
                        continue;
                    }
                    c.Set(x, y, 1f, 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_slash_" + key, new Vector2(0.5f, 0.5f));
            _sprites[key] = s;
            return s;
        }

        // -------------------------------------------------------- big bodies of light --

        public const int BodyR = 30;

        private static float Hash(int x, int y, int s)
        {
            int h = x * 374761393 + y * 668265263 + s * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }

        /// <summary>A sun's face (64 px): a darker limb, a brighter middle, cells boiling across it, a white heart.</summary>
        internal static Sprite SunSprite()
        {
            if (_sprites.TryGetValue(10, out Sprite s))
            {
                return s;
            }
            const int N = BodyR * 2 + 4;
            Canvas c = new Canvas(N, N);
            float o = N * 0.5f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - o) * (x + 0.5f - o) + (y + 0.5f - o) * (y + 0.5f - o)) / BodyR;
                    if (d > 1f)
                    {
                        continue;
                    }
                    float v = d > 0.93f ? 0.5f : d > 0.8f ? 0.66f : d > 0.55f ? 0.82f : d > 0.25f ? 0.93f : 1f;
                    float g = Hash(x / 3, y / 3, 5);
                    if (d < 0.93f)
                    {
                        v += g > 0.78f ? 0.1f : g < 0.18f ? -0.09f : 0f;
                    }
                    c.Set(x, y, Mathf.Clamp01(v), 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_sun", new Vector2(0.5f, 0.5f));
            _sprites[10] = s;
            return s;
        }

        /// <summary>A corona (96 px): sixteen tapering rays round a ring of light, ragged at their tips.</summary>
        internal static Sprite CoronaSprite()
        {
            if (_sprites.TryGetValue(11, out Sprite s))
            {
                return s;
            }
            const int N = 100;
            Canvas c = new Canvas(N, N);
            float o = N * 0.5f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - o, dy = y + 0.5f - o;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r < BodyR - 3 || r > 48f)
                    {
                        continue;
                    }
                    float a = Mathf.Atan2(dy, dx);
                    int ray = Mathf.RoundToInt(a / (Mathf.PI * 2f) * 16f);
                    float rayA = ray * Mathf.PI * 2f / 16f;
                    float off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, rayA * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float len = (ray & 1) == 0 ? 48f : 41f;
                    float k = (r - (BodyR - 3)) / (len - (BodyR - 3));
                    bool inRay = k <= 1f && off < 0.17f * (1f - k);
                    bool inRing = r < BodyR + 3;
                    if (!inRay && !inRing)
                    {
                        continue;
                    }
                    if (inRay && k > 0.6f && Dith(x, y) < (k - 0.6f) / 0.4f)
                    {
                        continue;
                    }
                    c.Set(x, y, inRing ? 1f : 1f - 0.35f * k, 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_corona", new Vector2(0.5f, 0.5f));
            _sprites[11] = s;
            return s;
        }

        /// <summary>A sphere of energy (64 px): a bright rim, three spiral arms of light winding into a white heart.</summary>
        internal static Sprite OrbSprite()
        {
            if (_sprites.TryGetValue(12, out Sprite s))
            {
                return s;
            }
            const int N = BodyR * 2 + 4;
            Canvas c = new Canvas(N, N);
            float o = N * 0.5f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - o, dy = y + 0.5f - o;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / BodyR;
                    if (d > 1f)
                    {
                        continue;
                    }
                    float a = Mathf.Atan2(dy, dx);
                    float arm = Mathf.Sin(3f * a + 7f * d);
                    float v = d > 0.9f ? 1f : d < 0.22f ? 1f : arm > 0.55f ? 0.92f : arm > 0f ? 0.72f : 0.55f;
                    c.Set(x, y, v, 1f);
                }
            }
            s = c.ToSprite("sakuga_prim_orb", new Vector2(0.5f, 0.5f));
            _sprites[12] = s;
            return s;
        }

        /// <summary>
        /// A sun 'r' world units across its face, for one frame (call every frame): a turning corona, the
        /// face, a white heart; flares licking off its edge now and then.
        /// </summary>
        public static void Sun(Vector2 at, float r, FxLayer layer, int order, bool cine, Color face, Color limb, Color corona)
        {
            if (!Kit.Visible(at, 3f))
            {
                return;
            }
            float px = r / BodyR;
            float t = Time.time;
            Particle co = CineKit.C(Fx.Spawn(CoronaSprite(), at, px * (1f + 0.05f * Mathf.Sin(t * 7f)), 0.05f, layer, order), cine);
            if (co != null)
            {
                co.Rot = t * 12f;
                co.Color(Fx.WithA(corona, 0.75f));
            }
            Particle co2 = CineKit.C(Fx.Spawn(CoronaSprite(), at, px * 0.9f, 0.05f, layer, order), cine);
            if (co2 != null)
            {
                co2.Rot = -t * 17f + 11f;
                co2.Color(Fx.WithA(Color.Lerp(corona, Color.white, 0.3f), 0.5f));
            }
            Particle body = CineKit.C(Fx.Spawn(SunSprite(), at, px, 0.05f, layer, order + 1), cine);
            if (body != null)
            {
                body.Rot = t * 4f;
                body.Color(Color.Lerp(limb, face, 0.6f));
            }
            if (Random.value < 0.3f)
            {
                float fa = Random.Range(0f, 360f);
                Vector2 rim = at + Choreo.Rotate(Vector2.right, fa) * r * 0.95f;
                Slash(rim, fa + 90f + Random.Range(-25f, 25f), r * 0.6f, r * 0.12f, 0.3f, layer, order + 2, cine, limb);
            }
        }

        private static Particle Delayed(Particle p, float delay)
        {
            if (p != null && delay > 0f)
            {
                p.Delay = delay;
                p.R.enabled = false;
            }
            return p;
        }

        // -------------------------------------------------------------- strokes --

        /// <summary>A soft stroke from a to b, 'w' world units wide.</summary>
        public static Particle Band(Vector2 a, Vector2 b, float w, float life, FxLayer layer, int order, bool cine, int kind, params Color[] colors)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f || w <= 1e-4f)
            {
                return null;
            }
            float px = w / BandH;
            Particle p = CineKit.C(Fx.Spawn(BandSprite(kind, Random.Range(0, 3)), (a + b) * 0.5f, px, life, layer, order), cine);
            if (p == null)
            {
                return null;
            }
            p.SX = len / (BandW * px);
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Color(colors);
            return p;
        }

        /// <summary>
        /// An energy beam from 'from' to 'to', 'width' world units wide (redrawn every frame by the caller
        /// with a short 'life', or once with a long one). 'body' is its colour, 'edge' its outer light.
        /// </summary>
        public static void Beam(Vector2 from, Vector2 to, float width, float life, FxLayer layer, int order, bool cine, Color body, Color edge,
            bool ends = true, bool strands = true)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f || width <= 1e-4f)
            {
                return;
            }
            if (!Kit.Visible(from, 1.5f) && !Kit.Visible(to, 1.5f) && !Kit.Visible((from + to) * 0.5f, 2f))
            {
                return;
            }
            Vector2 dir = d / len;
            Vector2 side = new Vector2(-dir.y, dir.x);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 40f);
            float w = width * pulse;
            bool fade = life > 0.25f;
            Band(from, to, w * 2.4f, life, layer, order, cine, 0, Cols(Fx.WithA(Primordials.Void, 0.5f), fade));
            Band(from, to, w * 1.7f, life, layer, order + 1, cine, 0, Cols(Fx.WithA(edge, 0.55f), fade));
            Band(from, to, w, life, layer, order + 2, cine, 1, Cols(Fx.WithA(Color.Lerp(body, Color.white, 0.15f), 0.95f), fade));
            Band(from, to, w * 0.42f, life, layer, order + 3, cine, 2, Cols(Color.white, fade));
            Flow(from, to, width, life, layer, order, cine, body, edge, strands);
            if (ends)
            {
                float bpx = Mathf.Max(0.01f, width / 10f);
                CineKit.C(Spec.Blob(from, width * 1.1f * pulse, bpx, Mathf.Max(life, 0.05f), layer, order + 5, 1f, 0.45f, Color.white, Fx.WithA(edge, 0.7f)), cine);
                CineKit.C(Spec.Blob(to, width * 1.5f * (2f - pulse), bpx, Mathf.Max(life, 0.05f), layer, order + 5, 1f, 0.5f, Color.white, Fx.WithA(body, 0.8f), Fx.WithA(edge, 0.4f)), cine);
                if (Random.value < 0.5f)
                {
                    DuelFx.Starburst(to, bpx, width * 1.6f, Color.white, edge, body, 0.06f, layer);
                }
            }
        }

        private static Color[] Cols(Color c, bool fade)
        {
            return fade ? new[] { c, c, Fx.WithA(c, c.a * 0.6f), Fx.WithA(c, 0f) } : new[] { c };
        }

        /// <summary>The life of a stream of energy along a to b (no body): strands twisting round it, rings sliding down it, sparks shed.</summary>
        public static void Flow(Vector2 from, Vector2 to, float width, float life, FxLayer layer, int order, bool cine, Color body, Color edge, bool strands = true)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f || width <= 1e-4f)
            {
                return;
            }
            Vector2 dir = d / len;
            Vector2 side = new Vector2(-dir.y, dir.x);
            float t = Time.time;
            if (strands)
            {
                // two strands of light twisting round it
                int n = Fx.Count(Mathf.Clamp(Mathf.RoundToInt(len / Mathf.Max(0.05f, width * 0.45f)), 4, 56));
                float dpx = Mathf.Max(0.01f, width * 0.2f / 5f);
                for (int s = 0; s < 2; s++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float u = (i + 0.5f) / n;
                        float ph = t * 16f - u * len / Mathf.Max(0.05f, width) * 1.6f + s * Mathf.PI;
                        bool front = Mathf.Cos(ph) > 0f;
                        Vector2 at = from + d * u + side * Mathf.Sin(ph) * width * 0.78f;
                        Particle q = CineKit.C(Fx.Spawn(Art.Disk(2, 1f, 0f), at, dpx, Mathf.Max(life, 0.04f), layer, front ? order + 4 : order), cine);
                        if (q == null)
                        {
                            break;
                        }
                        Color c = s == 0 ? edge : body;
                        q.Color(front ? Color.Lerp(c, Color.white, 0.6f) : Fx.WithA(c, 0.7f));
                    }
                }
            }
            // rings sliding down it
            if (Random.value < Mathf.Clamp01(life * 6f + 0.2f))
            {
                float rpx = Mathf.Max(0.01f, width / 12f);
                float rl = Random.Range(0.25f, 0.4f);
                Particle ring = CineKit.C(Fx.Ring(from + dir * width, rpx, rl, 9f, 15f, 2, 1, layer, order + 3, 0.35f, 2f,
                    Color.white, Fx.WithA(edge, 0.85f), Fx.WithA(edge, 0f)), cine);
                if (ring != null)
                {
                    ring.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f;
                    ring.Moving(dir * len / rl);
                }
            }
            // sparks shed off it
            int sparks = Fx.Count(2 + Mathf.RoundToInt(len / Mathf.Max(0.5f, width * 6f)));
            for (int i = 0; i < Mathf.Min(sparks, 8); i++)
            {
                Vector2 at = from + d * Random.value + side * Random.Range(-0.5f, 0.5f) * width;
                float spx = Mathf.Max(0.01f, width * 0.12f);
                Particle sp = CineKit.C(Fx.Spawn(Art.Sparks[1], at, spx, Random.Range(0.18f, 0.3f), layer, order + 4), cine);
                if (sp == null)
                {
                    break;
                }
                sp.Moving((side * Random.Range(-1f, 1f) + dir * Random.Range(-0.3f, 0.6f)) * width * Random.Range(4f, 9f), 0f, 3f);
                sp.FaceVel = true;
                sp.Color(Color.white, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
            }
        }

        /// <summary>Branching lightning from a to b, its channel 'w' world units wide.</summary>
        public static void Bolt(Vector2 a, Vector2 b, float w, float life, Color edge, FxLayer layer, int order, bool cine, int forks = 2)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f || !Kit.Visible((a + b) * 0.5f, 1.2f) && !Kit.Visible(b, 0.8f))
            {
                return;
            }
            Vector2 dir = d / len;
            Vector2 side = new Vector2(-dir.y, dir.x);
            const int n = 7;
            Vector2 last = a;
            for (int i = 1; i <= n; i++)
            {
                float k = i / (float)n;
                Vector2 p = Vector2.Lerp(a, b, k) + (i < n ? side * Random.Range(-1f, 1f) * len * 0.08f : Vector2.zero);
                Band(last, p, w * 3.4f, life * 1.3f, layer, order, cine, 0, Fx.WithA(edge, 0.4f), Fx.WithA(edge, 0f));
                Band(last, p, w * 1.5f, life, layer, order + 1, cine, 1, Fx.WithA(Color.Lerp(edge, Color.white, 0.3f), 1f), Fx.WithA(edge, 0f));
                Band(last, p, w * 0.55f, life * 0.8f, layer, order + 2, cine, 2, Color.white, Fx.WithA(Color.white, 0f));
                if (forks > 0 && (i == 2 || i == 4) && Random.value < 0.75f)
                {
                    Vector2 fd = Choreo.Rotate(dir, Random.Range(25f, 55f) * (Random.value < 0.5f ? -1f : 1f));
                    Bolt(p, p + fd * len * Random.Range(0.2f, 0.35f), w * 0.6f, life * 0.8f, edge, layer, order, cine, forks - 1);
                }
                last = p;
            }
            if (forks >= 2)
            {
                float fpx = Mathf.Max(0.01f, w / 2f);
                CineKit.C(Spec.Blob(b, w * 4f, fpx, life * 1.4f, layer, order + 3, 1f, 0.5f, Color.white, Fx.WithA(edge, 0.7f), Fx.WithA(edge, 0f)), cine);
                for (int i = 0; i < Fx.Count(5); i++)
                {
                    Particle sp = CineKit.C(Fx.Spawn(Art.Sparks[1], b, fpx * 0.6f, Random.Range(0.15f, 0.28f), layer, order + 3), cine);
                    if (sp == null)
                    {
                        break;
                    }
                    sp.Moving(Random.insideUnitCircle.normalized * w * Random.Range(20f, 40f), 0f, 3f);
                    sp.FaceVel = true;
                    sp.Color(Color.white, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
                }
            }
        }

        /// <summary>
        /// A fissure of light racing out from 'from' along 'dir' for 'len' (flattened onto the ground when
        /// 'ground'), 'w' wide at its root, tapering, branching, embers rising from it.
        /// </summary>
        public static void Crack(Vector2 from, Vector2 dir, float len, float w, float life, Color glow, FxLayer layer, int order, bool cine, bool ground, int branches = 1, float delay = 0f, float scar = 0f)
        {
            if (len < 1e-3f || !Kit.Visible(from + dir * len * 0.5f, 1f))
            {
                return;
            }
            Vector2 side = new Vector2(-dir.y, dir.x);
            const int n = 5;
            Vector2 last = from;
            Color hot = Color.Lerp(glow, Color.white, 0.45f);
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = from + dir * len * i / n + side * Random.Range(-0.16f, 0.16f) * len / n;
                if (ground)
                {
                    p.y = from.y + (p.y - from.y) * Art.Ground;
                }
                float taper = 1f - (i - 1f) / n * 0.75f;
                float dl = delay + 0.035f * i;
                WorldTile st = Combat.TileAt(p);
                if (scar > 0f && st != null && st.Type != null && !st.Type.liquid)
                {
                    Color soil = Fx.Darken(Fx.SoilColor(Combat.TileAt(p)), 0.65f);
                    Delayed(Band(last, p, w * 1.3f * taper, scar, FxLayer.Ground, 1, cine, 1, Fx.WithA(soil, 0.9f), Fx.WithA(soil, 0.8f), Fx.WithA(soil, 0f)), dl);
                }
                Delayed(Band(last, p, w * 3f * taper, life, layer, order, cine, 0, Fx.WithA(glow, 0.45f), Fx.WithA(glow, 0.3f), Fx.WithA(glow, 0f)), dl);
                Delayed(Band(last, p, w * taper, life, layer, order + 1, cine, 1, hot, Fx.WithA(glow, 0.85f), Fx.WithA(glow, 0f)), dl);
                Delayed(Band(last, p, w * 0.38f * taper, life * 0.6f, layer, order + 2, cine, 2, Color.white, Fx.WithA(Color.white, 0f)), dl);
                if (branches > 0 && (i == 2 || i == 3) && Random.value < 0.6f)
                {
                    Crack(p, Choreo.Rotate(dir, Random.Range(30f, 55f) * (Random.value < 0.5f ? -1f : 1f)), len * Random.Range(0.2f, 0.3f), w * 0.55f, life * 0.8f, glow, layer, order, cine, ground, branches - 1, dl, scar * 0.8f);
                }
                if (Random.value < 0.5f)
                {
                    Particle em = CineKit.Ember(p, Mathf.Max(0.01f, w * 0.35f), Random.Range(0.5f, 0.9f), new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(1f, 2.2f)) * w * 8f, hot, layer, cine, 1.2f);
                    Delayed(em, dl);
                }
                last = p;
            }
        }

        /// <summary>A tapered shaft of light from 'origin' at 'angle' degrees, 'len' long, 'baseW' wide where it starts.</summary>
        public static Particle Ray(Vector2 origin, float angle, float len, float baseW, float life, FxLayer layer, int order, bool cine, params Color[] colors)
        {
            if (len < 1e-3f || baseW <= 1e-4f)
            {
                return null;
            }
            float px = baseW / RayH;
            Particle p = CineKit.C(Fx.Spawn(RaySprite(), origin, px, life, layer, order), cine);
            if (p == null)
            {
                return null;
            }
            p.SX = len / (RayW * px);
            p.Rot = angle;
            p.Color(colors);
            return p;
        }

        /// <summary>A blade of light at 'at', turned 'angle' degrees, 'len' long and 'w' wide: a glow round a white edge.</summary>
        public static void Slash(Vector2 at, float angle, float len, float w, float life, FxLayer layer, int order, bool cine, Color glow, float delay = 0f, Vector2 vel = default)
        {
            for (int i = 0; i < 2; i++)
            {
                bool soft = i == 0;
                float ww = soft ? w * 2.2f : w;
                float px = ww / SlashH;
                Particle p = CineKit.C(Fx.Spawn(SlashSprite(soft), at, px, life, layer, order + i), cine);
                if (p == null)
                {
                    return;
                }
                p.SX = len * (soft ? 1.12f : 1f) / (SlashW * px);
                p.Rot = angle;
                // it cuts open: thin to full, then thins away
                p.Sized(0.6f, 1.05f);
                p.Color(soft ? new[] { Fx.WithA(glow, 0.9f), Fx.WithA(glow, 0.6f), Fx.WithA(glow, 0f) } : new[] { Color.white, Color.Lerp(glow, Color.white, 0.6f), Fx.WithA(glow, 0f) });
                if (vel != Vector2.zero)
                {
                    p.Moving(vel);
                }
                Delayed(p, delay);
            }
        }

        /// <summary>
        /// Power gathering into a ball (call it every frame): a halo, a body of light, a white heart, rings
        /// closing in on it, lightning crackling round it, sparks drawn into it.
        /// </summary>
        public static void Charge(Vector2 at, float r, FxLayer layer, int order, bool cine, Color c)
        {
            if (r <= 1e-3f || !Kit.Visible(at, 1f))
            {
                return;
            }
            float px = Mathf.Max(0.01f, r / 10f);
            float pulse = 1f + 0.07f * Mathf.Sin(Time.time * 30f);
            float bpx = r * pulse / BodyR;
            Particle co = CineKit.C(Fx.Spawn(CoronaSprite(), at, bpx, 0.05f, layer, order - 1), cine);
            if (co != null)
            {
                co.Rot = Time.time * 40f;
                co.Color(Fx.WithA(c, 0.55f));
            }
            Particle body = CineKit.C(Fx.Spawn(OrbSprite(), at, bpx, 0.05f, layer, order), cine);
            if (body != null)
            {
                body.Rot = -Time.time * 160f;
                body.Color(Color.Lerp(c, Color.white, 0.3f));
            }
            // inside it, light swirling round its heart; on its skin, sparks circling
            float tt = Time.time;
            for (int i = 0; i < 5; i++)
            {
                float ang = tt * 140f + i * 72f;
                Ray(at, ang, r * 0.95f, r * 0.35f, 0.05f, layer, order + 1, cine, Fx.WithA(Color.Lerp(c, Color.white, 0.5f), 0.45f));
            }
            for (int i = 0; i < 3; i++)
            {
                float ang = tt * (3f + i) + i * 2.1f;
                Vector2 o = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * (0.4f + 0.25f * i)) * r * 1.05f;
                CineKit.C(Spec.Blob(at + o, r * 0.12f, px, 0.05f, layer, Mathf.Sin(ang) < 0f ? order + 2 : order - 1, 1f, 0.2f, Color.white, c), cine);
            }
            if (Random.value < 0.35f)
            {
                CineKit.C(Fx.Ring(at, px, 0.25f, 26f, 8f, 1, 2, layer, order + 1, 1f, 2f, Fx.WithA(c, 0f), Fx.WithA(Color.white, 0.85f)), cine);
            }
            if (Random.value < 0.3f)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                Bolt(at + d * r * 0.9f, at + Choreo.Rotate(d, Random.Range(-40f, 40f)) * r * Random.Range(1.6f, 2.4f), r * 0.08f, 0.07f, Color.Lerp(c, Color.white, 0.3f), layer, order + 2, cine, 0);
            }
            for (int i = 0; i < 2; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                float life = Random.Range(0.18f, 0.28f);
                Particle sp = CineKit.C(Fx.Spawn(Art.Sparks[1], at + d * r * 3f, px * 0.7f, life, layer, order + 2), cine);
                if (sp == null)
                {
                    break;
                }
                sp.Moving(-d * r * 3f / life);
                sp.FaceVel = true;
                sp.Color(Fx.WithA(c, 0f), c, Color.white);
            }
        }

        /// <summary>
        /// The smear of something crossing a distance faster than sight: a blade of light pointed at both
        /// ends, bursts of light left hanging along its path, sparks thrown off it. (Never a flat bar.)
        /// </summary>
        public static void Streak(Vector2 from, Vector2 to, float w, float life, FxLayer layer, int order, bool cine, Color c)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f || !Kit.Visible(from, 1f) && !Kit.Visible(to, 1f) && !Kit.Visible((from + to) * 0.5f, 1.5f))
            {
                return;
            }
            Vector2 dir = d / len, side = new Vector2(-dir.y, dir.x);
            Slash((from + to) * 0.5f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, len, w, life, layer, order, cine, c);
            int n = Fx.Count(Mathf.Clamp(Mathf.RoundToInt(len / Mathf.Max(0.05f, w * 2f)), 3, 10));
            float bpx = Mathf.Max(0.01f, w / 8f);
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                Vector2 at = from + d * u + side * Random.Range(-0.35f, 0.35f) * w;
                Particle q = CineKit.C(Spec.Blob(at, w * (0.35f + 0.65f * u), bpx, life * (0.6f + 0.8f * u), layer, order - 1, 1f, 0.55f, Color.white, Fx.WithA(c, 0.7f), Fx.WithA(c, 0f)), cine);
                q?.Sized(1f, 0.25f);
                if (Random.value < 0.6f)
                {
                    Particle sp = CineKit.C(Fx.Spawn(Art.Sparks[0], at, bpx * 0.8f, Random.Range(0.15f, 0.28f), layer, order + 1), cine);
                    if (sp != null)
                    {
                        sp.Moving((side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(4f, 9f) - dir * Random.Range(1f, 4f)) * w, 0f, 3f);
                        sp.FaceVel = true;
                        sp.Color(Color.white, Fx.WithA(c, 0f));
                    }
                }
            }
        }

        /// <summary>A meteor (a shaded boulder, hot or of the void) flying from 'from' to 'to' in 'time' seconds, its trail of fire.</summary>
        public static void Meteor(Vector2 from, Vector2 to, float r, float time, Color hot, Color body, Color ember, Color smoke, FxLayer layer, int order, bool cine)
        {
            if (!Kit.Visible(from, 1f) && !Kit.Visible(to, 1f))
            {
                return;
            }
            Vector2 vel = (to - from) / Mathf.Max(0.02f, time);
            Vector2 back = vel.sqrMagnitude > 1e-4f ? -vel.normalized : Vector2.up;
            float px = Mathf.Max(0.01f, r / 6f);
            Particle head = CineKit.C(Fx.Spawn(ChoreoArt.Boulder(6, Random.Range(0, 4)), from, px, time, layer, order + 1), cine);
            if (head == null)
            {
                return;
            }
            head.Moving(vel);
            head.Spin = Random.Range(-400f, 400f);
            head.Color(body);
            Particle glow = CineKit.C(Spec.Blob(from, r * 1.4f, px, time, layer, order, 1f, 0.8f, Fx.WithA(hot, 0.4f)), cine);
            glow?.Moving(vel);
            float t = 0f;
            head.Step = (q, dt) =>
            {
                t -= dt;
                if (t > 0f)
                {
                    return;
                }
                t = 0.025f;
                Spec.MeteorTrail(q.Pos, back, r, Mathf.Max(0.01f, r / 8f), hot, Color.Lerp(hot, ember, 0.4f), ember, smoke, layer, order - 1, cine);
            };
        }

        /// <summary>A ball of light flying from 'from' to 'to' in 'time' seconds, 'r' world units across, with a burning trail.</summary>
        public static void Orb(Vector2 from, Vector2 to, float r, float time, Color c, FxLayer layer, int order, bool cine)
        {
            if (!Kit.Visible(from, 1f) && !Kit.Visible(to, 1f))
            {
                return;
            }
            Vector2 vel = (to - from) / Mathf.Max(0.02f, time);
            float opx = Mathf.Max(0.01f, r / 10f);
            Particle glow = CineKit.C(Spec.Blob(from, r * 1.6f, opx, time, layer, order, 1f, 0.6f, Fx.WithA(c, 0.55f)), cine);
            glow?.Moving(vel);
            Particle core = CineKit.C(Spec.Blob(from, r, opx, time, layer, order + 1, 1f, 0.25f, Color.white, Color.Lerp(c, Color.white, 0.4f)), cine);
            if (core == null)
            {
                return;
            }
            core.Moving(vel);
            float puffT = 0f;
            core.Step = (q, dt) =>
            {
                puffT -= dt;
                if (puffT > 0f)
                {
                    return;
                }
                puffT = 0.02f;
                // the trail: fire-like puffs falling behind, sparks spat out
                Particle tp = CineKit.C(Fx.Spawn(Art.Puffs[Random.Range(0, 3)], q.Pos + Random.insideUnitCircle * r * 0.3f, opx * 0.8f, Random.Range(0.18f, 0.28f), layer, order - 1), cine);
                tp?.Moving(-vel * 0.08f + Random.insideUnitCircle * r * 2f, 0f, 2f).Sized(1f, 0.35f)
                    .Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.85f), Fx.WithA(Color.Lerp(c, Primordials.Void, 0.5f), 0.4f), Fx.WithA(c, 0f));
                if (Random.value < 0.4f)
                {
                    Particle sp = CineKit.C(Fx.Spawn(Art.Sparks[0], q.Pos, opx * 0.6f, 0.2f, layer, order + 1), cine);
                    if (sp != null)
                    {
                        sp.Moving(Random.insideUnitCircle.normalized * r * 12f, 0f, 3f);
                        sp.FaceVel = true;
                        sp.Color(Color.white, Fx.WithA(c, 0f));
                    }
                }
            };
        }
    }
}
