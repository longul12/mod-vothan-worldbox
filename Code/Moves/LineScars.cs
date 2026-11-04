using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// A scar along a straight line on the ground: Fault Line's jagged fissure, Crescent Fang's
    /// glowing furrow. It is ONE continuous procedural pixel decal per use, rasterized at the exact
    /// angle and length of the line (never a rotated sprite: every pixel sits on the unit's own pixel
    /// grid), with the colour of the ground under each pixel baked in (a fissure that runs from grass
    /// onto sand changes colour with it), then cut into chunks along its length so that a front can
    /// "run" along it and reveal it piece by piece (RevealTo).
    ///
    /// Layers: the ground part (a dark zig-zag fissure body that pinches and widens, a shadow edge on one
    /// side, a broken lip of torn earth on the lit side, hairline side cracks, loose rubble) baked in true
    /// colour, and a glowing core (white texels of uneven brightness, tinted by a cooling gradient) on top.
    /// Two textures per scar, packed as atlases of the chunks; they are destroyed once no particle shows
    /// them anymore (Sweep).
    /// </summary>
    internal sealed class LineScar
    {
        /// <summary>How a scar looks.</summary>
        internal struct Spec
        {
            /// <summary>Body width in pixels at the start (1 thin crack .. 4 a chasm) and at the far end.</summary>
            public int Width, TipWidth;
            /// <summary>Furrow: the body narrows at the start as well (a groove cut by a blade, not torn open).</summary>
            public bool TaperStart;
            /// <summary>How far (pixels) the centre line may wander off the true line: 0 = a clean cut.</summary>
            public float Jag;
            /// <summary>Share of short runs where a crack pinches a pixel narrower (and its glow breaks): 0 = even width.</summary>
            public float Pinch;
            /// <summary>Short side cracks branching off the main one.</summary>
            public bool Branches;
            /// <summary>Loose bits of earth thrown up beside it.</summary>
            public bool Rubble;
            /// <summary>Dotted, broken last pixels (a crack running out), instead of a clean end.</summary>
            public bool BrokenTip;
            /// <summary>How much of the rim is drawn (1 everywhere, less for a hairline crack).</summary>
            public float Rim;
            /// <summary>Fraction of the length with a glowing core (0 = none), and the core's width near the start.</summary>
            public float GlowUntil;
            public int GlowWidth;
            /// <summary>Seconds the ground part stays (it fades over the last quarter) and the core glows.</summary>
            public float Life, GlowLife;
            /// <summary>The core's colour over its life (white-hot first, cooling to nothing).</summary>
            public Color[] GlowColors;
        }

        private sealed class Piece
        {
            public float Start;
            public Vector2 Pos;
            public Sprite Base, Glow;
        }

        // pixel kinds in the working canvas (Hair: a side crack, one dark pixel wide, without rims)
        private const byte Empty = 0, Body = 1, Edge = 2, Lip = 3, Grit = 4, GritShade = 5, Hair = 6;

        private static readonly List<LineScar> _all = new List<LineScar>();

        private readonly List<Piece> _pieces = new List<Piece>();
        private readonly List<Particle> _shown = new List<Particle>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly Spec _spec;
        private readonly float _px;
        private readonly float _born;
        private int _next;
        private bool _closed;
        private float[] _off;      // the centre line's sideways wander per pixel along it

        public readonly Vector2 From, Dir;
        /// <summary>World length actually drawn.</summary>
        public readonly float Length;

        private LineScar(Vector2 from, Vector2 dir, float length, float px, Spec spec)
        {
            From = from;
            Dir = dir;
            Length = length;
            _px = px;
            _spec = spec;
            _born = Time.unscaledTime;
        }

        /// <summary>Scars alive (dev log).</summary>
        public static int Count => _all.Count;

        /// <summary>
        /// A cooling ramp for GlowColors: particles sample their colour stops evenly over their life, so a
        /// glow keyed only by a few colours cools linearly (white-hot for seconds: a bright road on the
        /// ground). This resamples (time, colour) keys into evenly spaced stops: white-hot for a blink,
        /// the tier/fire colour for a moment, then a long dull ember tail. 'at' is increasing seconds
        /// from 0 to 'life', one per colour.
        /// </summary>
        public static Color[] Cooling(float life, float[] at, Color[] colors, int stops = 40)
        {
            int n = Mathf.Min(at.Length, colors.Length);
            Color[] r = new Color[Mathf.Max(2, stops)];
            if (n == 0)
            {
                return new[] { Color.white, Fx.WithA(Color.white, 0f) };
            }
            for (int i = 0; i < r.Length; i++)
            {
                float t = life * i / (r.Length - 1);
                int k = 0;
                while (k < n - 2 && t > at[k + 1])
                {
                    k++;
                }
                if (n == 1)
                {
                    r[i] = colors[0];
                    continue;
                }
                float span = at[k + 1] - at[k];
                float u = span > 1e-4f ? Mathf.Clamp01((t - at[k]) / span) : 1f;
                r[i] = Color.LerpUnclamped(colors[k], colors[k + 1], u);
            }
            return r;
        }

        // ------------------------------------------------------------ build --

        /// <summary>
        /// Draws a scar from 'from' along 'dir' for 'length' world units at 'px' world units per pixel.
        /// Nothing shows until RevealTo. Null when there is nothing to draw (too short, no world).
        /// </summary>
        public static LineScar Build(Vector2 from, Vector2 dir, float length, float px, Spec spec, int seed)
        {
            Sweep();
            if (World.world == null || px <= 0f || dir.sqrMagnitude < 1e-6f)
            {
                return null;
            }
            dir.Normalize();
            float len = Mathf.Min(length / px, 420f);   // pixels (never a texture wider than this)
            if (len < 3f)
            {
                return null;
            }
            LineScar s = new LineScar(from, dir, len * px, px, spec);
            try
            {
                s.Rasterize(len, new System.Random(seed));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Sakuga] scar: " + e);
                s.Destroy();
                return null;
            }
            if (s._pieces.Count == 0)
            {
                s.Destroy();
                return null;
            }
            _all.Add(s);
            return s;
        }

        private void Rasterize(float len, System.Random rng)
        {
            Spec sp = _spec;
            Vector2 d = Dir;
            Vector2 n = new Vector2(-d.y, d.x);
            int width = Mathf.Clamp(sp.Width, 1, 4);
            int margin = width + Mathf.CeilToInt(sp.Jag) + (sp.Branches ? 9 : 0) + (sp.Rubble ? 6 : 0) + 3;
            Vector2 end = d * len;
            int minX = Mathf.FloorToInt(Mathf.Min(0f, end.x)) - margin, maxX = Mathf.CeilToInt(Mathf.Max(0f, end.x)) + margin;
            int minY = Mathf.FloorToInt(Mathf.Min(0f, end.y)) - margin, maxY = Mathf.CeilToInt(Mathf.Max(0f, end.y)) + margin;
            int w = maxX - minX + 1, h = maxY - minY + 1;
            int ox = -minX, oy = -minY;
            Vector2 start = new Vector2(ox + 0.5f, oy + 0.5f);
            // world position of the bottom-left corner of canvas pixel (ox, oy): on the px grid, so the
            // chunks (each placed at an integer pixel offset from it) line up exactly
            Vector2 corner = new Vector2(Mathf.Round((From.x - 0.5f * _px) / _px) * _px, Mathf.Round((From.y - 0.5f * _px) / _px) * _px);

            byte[] kind = new byte[w * h];
            byte[] glow = new byte[w * h];   // the core's brightness per pixel (0 = none)
            short[] chunk = new short[w * h];

            // chunks: about one tile each, never more than 16 along the whole scar
            int chunkPx = Mathf.Max(4, Mathf.RoundToInt(1f / _px));
            if (len / chunkPx > 16f)
            {
                chunkPx = Mathf.CeilToInt(len / 16f);
            }
            int chunks = Mathf.FloorToInt(len / chunkPx) + 1;

            // ---- the centre line: a zig-zag of short kinked runs (a crack), or a clean line (a cut).
            // (Long runs with small random kinks drew straight, even bands: with the rims, a plank.)
            int steps = Mathf.CeilToInt(len) + 2;
            float[] off = new float[steps + 1];
            if (sp.Jag > 0f)
            {
                float t0 = 0f, o0 = 0f;
                while (t0 < len)
                {
                    float t1 = Mathf.Min(len, t0 + 2f + rng.Next(0, 4));
                    float step = (0.7f + (float)rng.NextDouble() * 1.3f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                    if (Mathf.Abs(o0 + step) > sp.Jag)
                    {
                        step = -step;   // turn back towards the true line
                    }
                    float o1 = Mathf.Clamp(o0 + step, -sp.Jag, sp.Jag);
                    int i0 = Mathf.CeilToInt(t0), i1 = Mathf.Min(steps, Mathf.FloorToInt(t1));
                    for (int i = i0; i <= i1; i++)
                    {
                        float k = t1 > t0 ? (i - t0) / (t1 - t0) : 1f;
                        // it leaves the fist point on the line, and wanders more as it runs
                        off[i] = Mathf.Lerp(o0, o1, k) * Anim.Smooth(0f, 6f, i);
                    }
                    t0 = t1;
                    o0 = o1;
                }
                for (int i = Mathf.FloorToInt(len) + 1; i <= steps; i++)
                {
                    off[i] = off[Mathf.FloorToInt(len)];
                }
            }
            _off = off;

            // ---- the crack breathes: short runs a pixel narrower, where its glow breaks too, and the
            // glow itself burns unevenly (hot spots and dimmer stretches, not one even neon tube)
            bool[] pinched = new bool[steps + 1];
            byte[] heat = new byte[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                heat[i] = 255;
            }
            if (sp.Pinch > 0f)
            {
                int t = 0;
                while (t <= steps)
                {
                    int run = 2 + rng.Next(0, 5);
                    bool p = rng.NextDouble() < sp.Pinch;
                    byte hot = rng.NextDouble() < 0.45 ? (byte)255 : (byte)rng.Next(140, 200);
                    for (int i = t; i < t + run && i <= steps; i++)
                    {
                        pinched[i] = p && i > 2;
                        heat[i] = i < 4 ? (byte)255 : hot;
                    }
                    t += run;
                }
            }

            // sample it finely, keep one pixel per cell, then drop the L-corners (a pixel-perfect line)
            List<Vector3Int> line = new List<Vector3Int>();
            List<float> along = new List<float>();
            for (float s = 0f; s <= len; s += 0.25f)
            {
                int i = Mathf.Min(steps - 1, Mathf.FloorToInt(s));
                float o = Mathf.Lerp(off[i], off[i + 1], s - i);
                Vector2 p = start + d * s + n * o;
                Vector3Int px = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), 0);
                if (line.Count > 0 && line[line.Count - 1] == px)
                {
                    continue;
                }
                if (line.Count >= 2)
                {
                    Vector3Int a = line[line.Count - 2], b = line[line.Count - 1];
                    if (Mathf.Abs(a.x - px.x) == 1 && Mathf.Abs(a.y - px.y) == 1 && (b.x == a.x || b.y == a.y) && (b.x == px.x || b.y == px.y))
                    {
                        line.RemoveAt(line.Count - 1);
                        along.RemoveAt(along.Count - 1);
                    }
                }
                line.Add(px);
                along.Add(s);
            }

            // thickness grows across the line, on whole pixels
            bool flat = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            int ax = flat ? 0 : 1, ay = flat ? 1 : 0;

            void Mark(int x, int y, byte k, int c)
            {
                if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                {
                    return;
                }
                int i = y * w + x;
                if (kind[i] == Body)
                {
                    return;
                }
                if (k != Body && kind[i] != Empty)
                {
                    return;
                }
                kind[i] = k;
                chunk[i] = (short)c;
            }

            void Glow(int x, int y, byte a)
            {
                if ((uint)x < (uint)w && (uint)y < (uint)h && glow[y * w + x] < a)
                {
                    glow[y * w + x] = a;
                }
            }

            int ChunkOf(float s)
            {
                return Mathf.Clamp(Mathf.FloorToInt(s / chunkPx), 0, chunks - 1);
            }

            int WidthAt(float s)
            {
                float k = s / len;
                float wf = Mathf.Lerp(width, Mathf.Clamp(sp.TipWidth, 1, width), Anim.Smooth(0.5f, 1f, k));
                if (sp.TaperStart)
                {
                    wf = Mathf.Min(wf, 1f + s / 3f);
                }
                int r = Mathf.Clamp(Mathf.RoundToInt(wf), 1, 4);
                return pinched[Mathf.Clamp((int)s, 0, steps)] ? Mathf.Max(1, r - 1) : r;
            }

            for (int i = 0; i < line.Count; i++)
            {
                Vector3Int p = line[i];
                float s = along[i];
                int c = ChunkOf(s);
                int ww = WidthAt(s);
                if (sp.BrokenTip && s > len - 3.5f && (i & 1) == 1)
                {
                    continue;   // the crack runs out in a few broken dots
                }
                if (sp.TaperStart && s > len - 5f)
                {
                    ww = 1;
                }
                Mark(p.x, p.y, Body, c);
                if (ww >= 2)
                {
                    Mark(p.x + ax, p.y + ay, Body, c);
                }
                if (ww >= 3)
                {
                    Mark(p.x - ax, p.y - ay, Body, c);
                }
                if (ww >= 4)
                {
                    Mark(p.x + 2 * ax, p.y + 2 * ay, Body, c);
                }
                // the glow: one pixel deep in the crack (two only near the impact, where it is widest)
                if (sp.GlowUntil > 0f && s <= sp.GlowUntil * len && (ww >= 2 || sp.TaperStart && s > 1f))
                {
                    byte a = heat[Mathf.Clamp((int)s, 0, steps)];
                    Glow(p.x, p.y, a);
                    if (sp.GlowWidth >= 2 && ww >= 3 && s < 0.25f * len)
                    {
                        Glow(p.x + ax, p.y + ay, a);
                    }
                }
            }

            // ---- side cracks: dark hairlines that leave at a shallow angle and run out (drawn as wide as
            // the crack, with rims, they stood off it like fence posts)
            if (sp.Branches)
            {
                float next = 5f + rng.Next(0, 6);
                for (int i = 0; i < line.Count; i++)
                {
                    float s = along[i];
                    if (s < next || s > 0.85f * len)
                    {
                        continue;
                    }
                    next = s + 9f + rng.Next(0, 10);
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float ang = side * (28f + rng.Next(0, 26)) * Mathf.Deg2Rad;
                    Vector2 bd = new Vector2(d.x * Mathf.Cos(ang) - d.y * Mathf.Sin(ang), d.x * Mathf.Sin(ang) + d.y * Mathf.Cos(ang));
                    float blen = (2.5f + rng.Next(0, 4)) * (1f - 0.5f * s / len);
                    Vector2 bp = new Vector2(line[i].x + 0.5f, line[i].y + 0.5f);
                    int c = ChunkOf(s);
                    for (float u = 0.5f; u <= blen; u += 0.5f)
                    {
                        bp += bd * 0.5f;
                        if (rng.NextDouble() < 0.12)
                        {
                            float wob = ((float)rng.NextDouble() - 0.5f) * 0.9f;
                            bd = new Vector2(bd.x * Mathf.Cos(wob) - bd.y * Mathf.Sin(wob), bd.x * Mathf.Sin(wob) + bd.y * Mathf.Cos(wob));
                        }
                        Mark(Mathf.FloorToInt(bp.x), Mathf.FloorToInt(bp.y), Hair, c);
                    }
                }
            }

            // ---- rims, only across the line: a lip of torn earth on the lit side (above a flat crack, left of a
            // steep one) in broken runs, and a shadow edge on the other side, nearly whole. (Rims on all four
            // sides drew a rung at every stair step of a slanted crack, and an unbroken light lip made the
            // whole thing a striped plank: lip, glow, body, edge.)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (kind[i] != Body)
                    {
                        continue;
                    }
                    int c = chunk[i];
                    float lip = flat ? Art.Hash(x / 3, y, 7) : Art.Hash(x, y / 3, 7);
                    if (lip < sp.Rim * 0.7f)
                    {
                        if (flat)
                        {
                            Mark(x, y + 1, Lip, c);
                        }
                        else
                        {
                            Mark(x - 1, y, Lip, c);
                        }
                    }
                    if (Art.Hash(x, y, 13) < 0.25f + sp.Rim * 0.65f)
                    {
                        if (flat)
                        {
                            Mark(x, y - 1, Edge, c);
                        }
                        else
                        {
                            Mark(x + 1, y, Edge, c);
                        }
                    }
                }
            }

            // ---- loose earth thrown up beside it
            if (sp.Rubble && line.Count > 0)
            {
                int bits = Mathf.Max(2, Mathf.RoundToInt(len / 8f));
                for (int k = 0; k < bits; k++)
                {
                    int i = rng.Next(0, line.Count);
                    float s = along[i];
                    int ww = WidthAt(s);
                    int side = rng.NextDouble() < 0.5 ? -1 : 1;
                    int dist = ww + 2 + rng.Next(0, 3);
                    int x = line[i].x + ax * side * dist + (flat ? rng.Next(-1, 2) : 0);
                    int y = line[i].y + ay * side * dist + (flat ? 0 : rng.Next(-1, 2));
                    int c = ChunkOf(s);
                    Mark(x, y, Grit, c);
                    Mark(x + 1, y - 1, GritShade, c);
                }
            }

            // ---- colours: the ground under each pixel (water shows nothing: a scar never floats)
            Color32[] basePx = new Color32[w * h];
            Color32[] glowPx = new Color32[w * h];
            bool anyGlow = false;
            WorldTile lastTile = null;
            Color soil = Fx.SoilColor(Combat.TileAt(From));
            bool lastLiquid = false;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (kind[i] == Empty && glow[i] == 0)
                    {
                        continue;
                    }
                    Vector2 world = corner + new Vector2(x - ox + 0.5f, y - oy + 0.5f) * _px;
                    WorldTile t = Combat.TileAt(world);
                    if (t != lastTile)
                    {
                        lastTile = t;
                        lastLiquid = t == null || t.Type == null || t.Type.liquid;
                        if (!lastLiquid)
                        {
                            soil = Fx.SoilColor(t);
                        }
                    }
                    if (lastLiquid)
                    {
                        kind[i] = Empty;
                        glow[i] = 0;
                        continue;
                    }
                    // a deep dark body, a shadow edge, and the lip in the soil's own colour (lightened soil
                    // read as a pale tan stripe on grass: a plank's edge)
                    switch (kind[i])
                    {
                        case Body:
                            basePx[i] = C32(Fx.Darken(soil, 0.8f), 0.96f);
                            break;
                        case Hair:
                            basePx[i] = C32(Fx.Darken(soil, 0.7f), 0.8f);
                            break;
                        case Edge:
                            basePx[i] = C32(Fx.Darken(soil, 0.55f), 0.75f);
                            break;
                        case Lip:
                            basePx[i] = C32(Fx.Lighten(soil, 0.06f), 0.7f);
                            break;
                        case Grit:
                            basePx[i] = C32(soil, 0.85f);
                            break;
                        case GritShade:
                            basePx[i] = C32(Fx.Darken(soil, 0.55f), 0.55f);
                            break;
                    }
                    if (glow[i] > 0)
                    {
                        glowPx[i] = new Color32(255, 255, 255, glow[i]);
                        anyGlow = true;
                    }
                }
            }

            // ---- cut into chunks along the line and pack them side by side in two atlases
            int[] cx0 = new int[chunks], cy0 = new int[chunks], cx1 = new int[chunks], cy1 = new int[chunks];
            for (int c = 0; c < chunks; c++)
            {
                cx0[c] = cy0[c] = int.MaxValue;
                cx1[c] = cy1[c] = int.MinValue;
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (kind[i] == Empty && glow[i] == 0)
                    {
                        continue;
                    }
                    int c = chunk[i];
                    cx0[c] = Mathf.Min(cx0[c], x);
                    cy0[c] = Mathf.Min(cy0[c], y);
                    cx1[c] = Mathf.Max(cx1[c], x);
                    cy1[c] = Mathf.Max(cy1[c], y);
                }
            }
            int aw = 0, ah = 1;
            int[] ax0 = new int[chunks];
            for (int c = 0; c < chunks; c++)
            {
                if (cx1[c] < cx0[c])
                {
                    ax0[c] = -1;
                    continue;
                }
                ax0[c] = aw;
                aw += cx1[c] - cx0[c] + 2;
                ah = Mathf.Max(ah, cy1[c] - cy0[c] + 1);
            }
            if (aw == 0)
            {
                return;
            }
            Color32[] baseAtlas = new Color32[aw * ah];
            Color32[] glowAtlas = anyGlow ? new Color32[aw * ah] : null;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (kind[i] == Empty && glow[i] == 0)
                    {
                        continue;
                    }
                    int c = chunk[i];
                    int j = (y - cy0[c]) * aw + ax0[c] + (x - cx0[c]);
                    baseAtlas[j] = basePx[i];
                    if (glowAtlas != null)
                    {
                        glowAtlas[j] = glowPx[i];
                    }
                }
            }
            Texture2D baseTex = MakeTexture(aw, ah, baseAtlas, "sakuga_scar");
            Texture2D glowTex = glowAtlas != null ? MakeTexture(aw, ah, glowAtlas, "sakuga_scar_glow") : null;
            for (int c = 0; c < chunks; c++)
            {
                if (ax0[c] < 0)
                {
                    continue;
                }
                Rect r = new Rect(ax0[c], 0f, cx1[c] - cx0[c] + 1, cy1[c] - cy0[c] + 1);
                Piece p = new Piece
                {
                    // shown once the front is half way through it: the scar appears under the front, never ahead of it
                    Start = Mathf.Min((c + 0.5f) * chunkPx * _px, Length),
                    Pos = corner + new Vector2(cx0[c] - ox, cy0[c] - oy) * _px,
                    Base = Sprite.Create(baseTex, r, Vector2.zero, 1f, 0u, SpriteMeshType.FullRect),
                    Glow = glowTex != null ? Sprite.Create(glowTex, r, Vector2.zero, 1f, 0u, SpriteMeshType.FullRect) : null
                };
                _owned.Add(p.Base);
                if (p.Glow != null)
                {
                    _owned.Add(p.Glow);
                }
                _pieces.Add(p);
            }
        }

        private Texture2D MakeTexture(int w, int h, Color32[] px, string name)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            t.SetPixels32(px);
            t.Apply(false, true);
            _owned.Add(t);
            return t;
        }

        private static Color32 C32(Color c, float a)
        {
            return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }

        /// <summary>Where the drawn centre line really is, 'dist' world units along (it wanders off the true line): forks start here.</summary>
        public Vector2 PointAt(float dist)
        {
            Vector2 p = From + Dir * dist;
            if (_off == null || _off.Length < 2)
            {
                return p;
            }
            float s = Mathf.Clamp(dist / _px, 0f, _off.Length - 1.001f);
            int i = Mathf.FloorToInt(s);
            float o = Mathf.Lerp(_off[i], _off[i + 1], s - i);
            return p + new Vector2(-Dir.y, Dir.x) * o * _px;
        }

        // ----------------------------------------------------------- reveal --

        /// <summary>Shows every chunk that starts within 'dist' world units of the start (a front running along it).</summary>
        public void RevealTo(float dist)
        {
            if (_closed)
            {
                return;
            }
            while (_next < _pieces.Count && _pieces[_next].Start <= dist)
            {
                Show(_pieces[_next]);
                _next++;
            }
            if (_next >= _pieces.Count)
            {
                _closed = true;
            }
        }

        public void RevealAll()
        {
            RevealTo(float.MaxValue);
        }

        /// <summary>No more of it will be revealed (the move ended or was cut short); what is shown stays and fades.</summary>
        public void Close()
        {
            _closed = true;
        }

        private void Show(Piece p)
        {
            Particle b = Fx.Spawn(p.Base, p.Pos, _px, _spec.Life, FxLayer.Ground, 1);
            if (b != null)
            {
                b.Snap = true;
                Color c = Color.white;
                b.Color(c, c, c, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
                _shown.Add(b);
            }
            if (p.Glow != null && _spec.GlowLife > 0f)
            {
                Particle g = Fx.Spawn(p.Glow, p.Pos, _px, _spec.GlowLife, FxLayer.Ground, 3);
                if (g != null)
                {
                    g.Snap = true;
                    g.Color(_spec.GlowColors ?? new[] { Color.white, Fx.WithA(Kit.Fire2, 0f) });
                    _shown.Add(g);
                }
            }
        }

        // ---------------------------------------------------------- cleanup --

        /// <summary>Is any particle still drawing one of this scar's sprites?</summary>
        private bool Showing()
        {
            for (int i = 0; i < _shown.Count; i++)
            {
                Particle p = _shown[i];
                if (p == null || !p.Alive || p.Frames == null || p.Frames.Length == 0)
                {
                    continue;
                }
                if (Owns(p.Frames[0]))
                {
                    return true;
                }
            }
            return false;
        }

        private void Destroy()
        {
            foreach (UnityEngine.Object o in _owned)
            {
                if (o != null)
                {
                    UnityEngine.Object.Destroy(o);
                }
            }
            _owned.Clear();
            _shown.Clear();
            _pieces.Clear();
        }

        /// <summary>
        /// Removes every scar at once (a dev arena reset: demos all run on the same meadow, and the last move's
        /// scar used to lie under the next demo). Only particles still drawing a scar's own sprites are killed:
        /// the pool may have handed the others to new effects. A move still holding a scar finds it empty and
        /// closed (RevealTo does nothing).
        /// </summary>
        public static void ClearAll()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                LineScar s = _all[i];
                for (int j = 0; j < s._shown.Count; j++)
                {
                    Particle p = s._shown[j];
                    if (p != null && p.Alive && p.Frames != null && p.Frames.Length > 0 && s.Owns(p.Frames[0]))
                    {
                        p.Kill();
                    }
                }
                s._closed = true;
                s.Destroy();
            }
            _all.Clear();
        }

        private bool Owns(Sprite sprite)
        {
            for (int k = 0; k < _pieces.Count; k++)
            {
                if ((object)_pieces[k].Base == sprite || (object)_pieces[k].Glow == sprite)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Frees the textures of scars nothing shows anymore (called whenever a scar is built or a move using them ends).</summary>
        public static void Sweep()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                LineScar s = _all[i];
                bool idle = s._closed || Time.unscaledTime - s._born > 120f;
                if (idle && !s.Showing())
                {
                    s.Destroy();
                    _all.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// What the line techniques (Crescent Fang, Fault Line) share beyond the scar decal: real damage to the
    /// ground scaled by level and tier, fire only where the line itself burns (Ignite), the tiers' tempo (a
    /// Master never stands and waits for its own wave: the wave or the crack runs on by itself after the
    /// move ends, see Linger), and a chain link only when the performer has a follow-up that takes it.
    /// </summary>
    internal static class LineKit
    {
        // ------------------------------------------------------------ the tier --

        /// <summary>Masters and up: techniques back to back, no ordinary swings, short recoveries.</summary>
        public static bool Flow(Actor a)
        {
            TierInfo ti = a != null ? Tiers.Info(Tiers.Of(a)) : null;
            return ti != null && ti.Flow;
        }

        public static bool God(Actor a)
        {
            return a != null && Tiers.Of(a) == Tier.God;
        }

        /// <summary>
        /// A recovery/landing/sheathe time for this performer, by its tier's tempo (TierInfo.Linger: Expert 0.85,
        /// Master 0.55, Grandmaster 0.48, God 0.42): the higher the tier, the crisper it lands and goes.
        /// </summary>
        public static float Recover(float seconds, Actor a)
        {
            return seconds * Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
        }

        // ------------------------------------------------------------ the world --

        /// <summary>Ground-damage force for a level's base force: a God's is one stronger (max 4).</summary>
        public static int Force(int force, bool god)
        {
            return force <= 0 ? 0 : god ? Mathf.Min(4, force + 1) : force;
        }

        /// <summary>Ground-damage radius (tiles): a God's is half again as wide.</summary>
        public static float Radius(float tiles, bool god)
        {
            return god ? tiles * 1.5f : tiles;
        }

        /// <summary>
        /// Real damage to the ground at 'at' (Kit.Ravage: scorched, blasted, dug, never into water), skipped over
        /// water (a crater never floats). Never the rim of fire Kit.Ravage lights at force 3+: along a line every
        /// blast's rim lit together into one carpet of flames that buried the crack, the spikes and the enemies
        /// thrown out of it (a God's Tectonic Rend: a burning field 5 tiles wide). The line's own fire is Ignite.
        /// </summary>
        public static void Ravage(Vector2 at, float radiusTiles, int force, Actor by)
        {
            if (force <= 0 || World.world == null)
            {
                return;
            }
            WorldTile t = Combat.TileAt(at);
            if (t == null || t.Type == null || t.Type.liquid)
            {
                return;
            }
            Kit.Ravage(at, radiusTiles, force, by, false);
        }

        /// <summary>Ravage along a line (Kit.Trench's spacing), with the same rules.</summary>
        public static void Trench(Vector2 a, Vector2 b, float widthTiles, int force, Actor by)
        {
            if (force <= 0)
            {
                return;
            }
            float len = Vector2.Distance(a, b);
            int n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(0.8f, widthTiles)), 1, 40);
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i <= n; i++)
                {
                    Ravage(Vector2.Lerp(a, b, i / (float)n), widthTiles, force, by);
                }
            }
            finally
            {
                Kit.EndTrench(by);
            }
        }

        /// <summary>
        /// Sets the tile at 'at' alight (magma bursting out of a crack, a God's burning cut): one tile on the line
        /// itself, which the game's fire then spreads over the grass at its own pace, so the line reads first and
        /// the field burns after. Never with Environment damage below full, never on water, lava or rock, never
        /// a tile already burning or a building of the performer's own kingdom (startFire(true) set a defender's
        /// own houses on its crack alight), and never within 3 tiles of 'feet' when the performer could burn
        /// (only Masters and up are fire-immune). Kit.Ignite's rules.
        /// </summary>
        public static bool Ignite(Vector2 at, Actor by, Vector2 feet)
        {
            return Kit.Ignite(at, by, feet, 3f);
        }

        // ------------------------------------------------------------ the chain --

        /// <summary>
        /// Would leaving 'link' let this fighter chain straight on? Only if it knows a technique that takes it,
        /// ready and affordable. (A link nothing can take holds the Brain to link-only choices for its whole
        /// window: dead air for a God, whose ordinary gap is 0.05 s.)
        /// </summary>
        public static bool CanChain(Actor a, Link link)
        {
            if (link == Link.None || a == null || !a.isAlive() || !Fighters.TryGet(a, out Fighter f))
            {
                return false;
            }
            if (f.Chain >= (f.Info != null ? f.Info.MaxChain : 2))
            {
                return false;
            }
            IReadOnlyList<Technique> all = Techniques.All;
            for (int i = 0; i < all.Count; i++)
            {
                Technique t = all[i];
                if (t.GodAbility || t.Team && !Settings.TeamTechniques || !t.Accepts(link))
                {
                    continue;
                }
                int level = Techniques.Level(a, t, f.Tier);
                if (level > 0 && f.Ready(t) && Mana.Has(a, t, level, true))
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------- lingering --

        /// <summary>The part of a move that goes on after its performer is free again (a wave in flight, a running crack).</summary>
        internal interface ILinger
        {
            /// <summary>Advances it by 'dt' move seconds; false once it is over.</summary>
            bool Linger(float dt);

            /// <summary>The world was cleared, or it failed: drop everything it holds at once.</summary>
            void Drop();
        }

        private struct Entry
        {
            public ILinger L;
            public int Frame;
        }

        private static readonly List<Entry> _lingering = new List<Entry>();
        private static int _frame = -1;

        /// <summary>Lingering parts alive (dev log).</summary>
        public static int Lingering => _lingering.Count;

        [SakugaInit]
        private static void InitLinger()
        {
            Hooks.FrameTick += TickLinger;
            Hooks.WorldCleared += DropAll;
            // (the old world's scar textures go with it, not at the next scar's Sweep)
            Hooks.WorldCleared += LineScar.ClearAll;
        }

        /// <summary>Hands over the rest of a move: it is advanced once a frame on the move clock from the next frame on.</summary>
        public static void Linger(ILinger l)
        {
            if (l != null)
            {
                _lingering.Add(new Entry { L = l, Frame = Time.frameCount });
            }
        }

        /// <summary>
        /// Once a frame on the move clock (Hooks.FrameTick, right after the moves: it stops with the world on
        /// pause, a hit-stop or a cinematic freeze, like a move, and it runs whether or not any fighter is left:
        /// on the fighter tick, a wave whose thrower was the last fighter standing hung in the air forever). The
        /// frame a part was handed over in is skipped: its move already advanced it.
        /// </summary>
        private static void TickLinger(float dt)
        {
            if (_lingering.Count == 0 || Time.frameCount == _frame || dt <= 0f)
            {
                return;
            }
            _frame = Time.frameCount;
            for (int i = _lingering.Count - 1; i >= 0; i--)
            {
                if (i >= _lingering.Count)
                {
                    continue;
                }
                Entry e = _lingering[i];
                if (e.Frame == _frame)
                {
                    continue;
                }
                bool keep;
                try
                {
                    keep = e.L.Linger(dt);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[Sakuga] linger: " + ex);
                    keep = false;
                    try
                    {
                        e.L.Drop();
                    }
                    catch
                    {
                        // nothing more to do
                    }
                }
                if (!keep && i < _lingering.Count && ReferenceEquals(_lingering[i].L, e.L))
                {
                    _lingering.RemoveAt(i);
                }
            }
        }

        private static void DropAll()
        {
            Entry[] all = _lingering.ToArray();
            _lingering.Clear();
            foreach (Entry e in all)
            {
                try
                {
                    e.L.Drop();
                }
                catch
                {
                    // dropping is best effort
                }
            }
        }
    }
}
