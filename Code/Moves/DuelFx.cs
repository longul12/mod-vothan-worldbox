using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The duel's visual vocabulary, on top of Kit: starbursts sized in tiles but drawn at the
    /// fighters' own pixel size (so a "1-tile starburst" is ten crisp pixels for a human, not a
    /// scaled-down blur), two-colour clashes where both fighters' tiers burst out between the
    /// spikes, the one-handed "ting", skids, the wind of a stare-down, dust swirling round a
    /// circling pair, and spark fountains. Everything skips itself off screen.
    /// </summary>
    internal static class DuelFx
    {
        /// <summary>Hot metal: sparks off two blades.</summary>
        public static readonly Color Metal = new Color(1f, 0.86f, 0.5f, 1f);
        public static readonly Color MetalHot = new Color(1f, 0.6f, 0.2f, 1f);
        /// <summary>A bead of sweat (a fighter faltering under a pressure aura).</summary>
        public static readonly Color Sweat = new Color(0.72f, 0.88f, 1f, 1f);
        public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        private static readonly Dictionary<int, Sprite> _bursts = new Dictionary<int, Sprite>();

        // ------------------------------------------------------------ bursts --

        /// <summary>
        /// A jagged impact star 'r' texels from the middle to its longest spike (two variants per
        /// size). Same construction as the mod's big starbursts, at any radius. Cached.
        /// </summary>
        public static Sprite Burst(int r, int variant)
        {
            r = Mathf.Clamp(r, 3, 64);
            if (r > 12)
            {
                r = r / 2 * 2;   // fewer distinct textures for big ones
            }
            int key = r * 2 + (variant & 1);
            if (_bursts.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int spikes = r < 6 ? 6 : r < 10 ? 8 : r < 20 ? 10 : 12;
            s = MakeBurst(r, spikes + (variant & 1), 7919 * r + 31 * variant);
            _bursts[key] = s;
            return s;
        }

        private static Sprite MakeBurst(int r, int spikes, int seed)
        {
            System.Random rng = new System.Random(seed);
            int ry = Mathf.Max(2, Mathf.RoundToInt(r * 0.8f));
            Canvas c = new Canvas(r * 2 + 3, ry * 2 + 3);
            float[] len = new float[spikes];
            for (int i = 0; i < spikes; i++)
            {
                len[i] = 0.62f + 0.38f * (float)rng.NextDouble();
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
                    float tri = 1f - Mathf.Abs(frac - 0.5f) * 2f;
                    float reach = 0.36f + (len[si] - 0.36f) * tri * tri;
                    if (e > reach)
                    {
                        continue;
                    }
                    c.Set(x, y, e < 0.3f ? 1f : 0.9f, 1f);
                }
            }
            return c.ToSprite("sakuga_duel_burst_" + r, new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// An impact starburst 'tiles' in radius, drawn at pixel size px: a white core star, and
        /// behind it two stars in the fighters' colours turned a little, so both colours burst out
        /// between the spikes (one colour when one side won). A flash disk under it.
        /// </summary>
        public static void Starburst(Vector2 at, float px, float tiles, Color core, Color edgeA, Color edgeB, float life = 0.1f, FxLayer layer = FxLayer.Top)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            px = Mathf.Max(0.02f, px);
            int r = Mathf.Max(3, Mathf.RoundToInt(tiles / px));
            float rot = Random.Range(0f, 360f);
            Particle a = Fx.Spawn(Burst(Mathf.RoundToInt(r * 1.2f), 1), at, px, life * 1.15f, layer, 28);
            if (a != null)
            {
                a.Rot = rot + 15f;
                a.Sized(0.85f, 1.12f).Color(edgeA, Fx.WithA(edgeA, 0.85f), Fx.WithA(edgeA, 0f));
            }
            if (edgeB != edgeA)
            {
                Particle b = Fx.Spawn(Burst(Mathf.RoundToInt(r * 1.12f), 0), at, px, life * 1.1f, layer, 29);
                if (b != null)
                {
                    b.Rot = rot - 15f;
                    b.Sized(0.85f, 1.12f).Color(edgeB, Fx.WithA(edgeB, 0.85f), Fx.WithA(edgeB, 0f));
                }
            }
            Particle c = Fx.Spawn(Burst(r, 0), at, px, life, layer, 31);
            if (c != null)
            {
                c.Rot = rot;
                c.Sized(0.9f, 1.15f).Color(Color.white, Fx.WithA(core, 0.95f), Fx.WithA(core, 0f));
            }
            Kit.Flash(at, px, Mathf.Max(2, Mathf.RoundToInt(r * 0.45f)), life * 0.8f, Fx.WithA(core, 0.95f), layer, 30);
        }

        // ------------------------------------------------------------- clash --

        /// <summary>
        /// Sparks spraying out of a contact both ways, perpendicular to the line between two fighters
        /// (blades grinding), plus a few falling: 'metal' sparks are hot orange, fists throw white grit.
        /// </summary>
        public static void Spray(Vector2 at, float px, int n, Vector2 axis, bool metal, Color tint)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            Vector2 side = axis.sqrMagnitude > 0f ? new Vector2(-axis.y, axis.x).normalized : Vector2.up;
            if (side.y < 0f)
            {
                side = -side;
            }
            Color mid = metal ? Metal : Color.Lerp(Color.white, tint, 0.35f);
            Color end = metal ? Fx.WithA(MetalHot, 0f) : Fx.WithA(tint, 0f);
            for (int i = 0; i < n; i++)
            {
                float s = (i & 1) == 0 ? 1f : -1f;
                Vector2 dir = (side * s + axis.normalized * Random.Range(-0.6f, 0.6f)).normalized;
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.12f, 0.24f), FxLayer.Top, 19);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(55f, 110f) * px, 0f, 7f);
                p.FaceVel = true;
                p.Color(Color.white, mid, end);
            }
        }

        /// <summary>A fountain of sparks rising out of a contact point and arcing down (big clashes).</summary>
        public static void Fountain(Vector2 at, float px, int n, float speed, Color tint)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(-1.2f, 1.2f);
                Vector2 dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a) * 0.3f);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 18);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(0.4f, 1f) * speed * px, Random.Range(0.6f, 1.1f) * speed * px, 1.5f, 3.2f * speed * px);
                p.FaceVel = true;
                p.Color(Color.white, Color.Lerp(Color.white, tint, 0.5f), tint, Fx.WithA(tint, 0f));
            }
        }

        /// <summary>
        /// The anime "ting!": a deflection star at the contact, a few sparks flying 'away', a tiny
        /// ring and a glint. What a one-handed parry looks like.
        /// </summary>
        public static void Ting(Vector2 at, float px, Color glint, int sparks, Vector2 away, float size = 1f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Fx.Glint(at + new Vector2(0f, 1f * px), px, 0.24f, glint, 1.15f * size);
            Fx.HitSpark(at, px, 0, away.sqrMagnitude > 0f ? away : Vector2.up, sparks);
            Fx.Ring(at, px, 0.16f, 2f, 7f * size, 1, 1, FxLayer.Top, 3, 1f, 2.5f, new Color(1f, 1f, 1f, 0.9f), DuelFx.Clear);
        }

        /// <summary>A ring seen edge-on in the air (a sonic boom around a mid-air clash), r texels.</summary>
        public static Particle AirRing(Vector2 at, float px, float r0, float r1, float life, int thick, params Color[] colors)
        {
            if (!Kit.Visible(at))
            {
                return null;
            }
            return Fx.Ring(at, px, life, r0, r1, thick, 1, FxLayer.Top, 3, 0.35f, 2.5f, colors);
        }

        /// <summary>A ring along the ground, r0 to r1 texels.</summary>
        public static Particle GroundRing(Vector2 at, float px, float r0, float r1, float life, int thick0, int thick1, params Color[] colors)
        {
            if (!Kit.Visible(at))
            {
                return null;
            }
            return Fx.Ring(at, px, life, r0, r1, thick0, thick1, FxLayer.Ground, 6, Art.Ground, 2.4f, colors);
        }

        // ------------------------------------------------------------ ground --

        /// <summary>Dust kicked up behind sliding feet (call every few hundredths of a second while sliding).</summary>
        public static void SkidDust(Vector2 feet, Vector2 slide, float px, WorldTile tile, float k = 1f)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            Vector2 dir = slide.sqrMagnitude > 0f ? slide.normalized : Vector2.zero;
            Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], feet + new Vector2(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f)) * px,
                px, Random.Range(0.3f, 0.48f), FxLayer.Behind, 6);
            p?.Moving(-dir * Random.Range(6f, 14f) * px * k + new Vector2(0f, Random.Range(0.5f, 2f) * px), Random.Range(2f, 5f) * px, 4f)
                .Sized(0.45f, 1f)
                .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
        }

        /// <summary>Two scuffed lines where a pair of feet slid (level II and up: a level I fight leaves no marks).</summary>
        public static void SkidMarks(Vector2 from, Vector2 to, float px, WorldTile tile, float life)
        {
            if (!Kit.Visible(to) || (to - from).sqrMagnitude < px * px * 4f || tile == null || tile.Type == null || tile.Type.liquid)
            {
                return;
            }
            Color dark = Fx.Darken(Fx.SoilColor(tile), 0.45f);
            Vector2 off = new Vector2(0f, 1f * px);
            Kit.Line(from + off, to + off, px, life, 1, FxLayer.Ground, 1, Fx.WithA(dark, 0.7f), Fx.WithA(dark, 0.55f), Fx.WithA(dark, 0f));
            Kit.Line(from - off, to - off, px, life, 1, FxLayer.Ground, 1, Fx.WithA(dark, 0.6f), Fx.WithA(dark, 0.5f), Fx.WithA(dark, 0f));
        }

        /// <summary>A puff of dust at the feet (a plant, a push-off, a step).</summary>
        public static void Puff(Vector2 feet, float px, WorldTile tile, int n = 2, float speed = 12f)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float s = n == 1 ? 0f : (i / (n - 1f)) * 2f - 1f;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], feet + new Vector2(s * 2f * px, 0f), px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 6);
                p?.Moving(new Vector2(s * speed * px, 1.5f * px), Random.Range(3f, 6f) * px, 5f)
                    .Sized(0.5f, 0.95f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>
        /// The wind of a stare-down: one speed line and a mote of dust drifting across the space
        /// between two fighters, along 'dir'.
        /// </summary>
        public static void Wind(Vector2 from, Vector2 to, float px, WorldTile tile, Color line)
        {
            Vector2 mid = (from + to) * 0.5f;
            if (!Kit.Visible(mid))
            {
                return;
            }
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.2f)
            {
                return;
            }
            Vector2 dir = d / len;
            Vector2 side = new Vector2(-dir.y, dir.x);
            // a horizontal streak somewhere between them, at knee to head height
            Vector2 at = Vector2.Lerp(from, to, Random.Range(0.15f, 0.85f)) + side * Random.Range(-3f, 3f) * px + new Vector2(0f, Random.Range(1f, 8f) * px);
            Particle p = Fx.Spawn(Art.Lines[Random.Range(0, 2)], at, px, Random.Range(0.22f, 0.32f), FxLayer.Top, 1);
            if (p != null)
            {
                p.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                p.Moving(dir * Random.Range(28f, 40f) * px);
                p.Color(Fx.WithA(line, 0f), Fx.WithA(line, line.a), Fx.WithA(line, 0f));
            }
            if (Random.value < 0.6f)
            {
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.45f);
                Vector2 start = from + side * Random.Range(-4f, 4f) * px + new Vector2(0f, Random.Range(0f, 2f) * px);
                Particle m = Fx.Spawn(Art.Dust[Random.Range(0, 2)], start, px, Random.Range(0.7f, 1f), FxLayer.Behind, 5);
                m?.Moving(dir * len / 0.9f * Random.Range(0.7f, 1f), Random.Range(0.5f, 2f) * px, 0.4f)
                    .Sized(0.35f, 0.7f)
                    .Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.55f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>
        /// Dust swirling round a point, orbiting and closing in (two fighters circling each other,
        /// power gathering). 'spin' radians per second, negative clockwise.
        /// </summary>
        public static void Swirl(Vector2 center, float px, WorldTile tile, float radius, int n, float life, float spin, Color? tint = null)
        {
            if (!Kit.Visible(center))
            {
                return;
            }
            Color dust = tint ?? Fx.Lighten(Fx.SoilColor(tile), 0.42f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a0 = (i + Random.value * 0.7f) / n * Mathf.PI * 2f;
                float r0 = radius * Random.Range(0.65f, 1.1f);
                float w = spin * Random.Range(0.8f, 1.25f);
                float h = Random.Range(0f, 3f) * px;
                float close = Random.Range(0.25f, 0.5f);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], center, px, life * Random.Range(0.8f, 1.1f), FxLayer.Behind, 4);
                if (p == null)
                {
                    break;
                }
                p.Sized(0.4f, 0.8f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, dust.a * 0.6f), Fx.WithA(dust, dust.a * 0.4f), Fx.WithA(dust, 0f));
                p.Step = (q, dt) =>
                {
                    float k = q.K;
                    float ang = a0 + w * q.Age;
                    float r = r0 * (1f - close * k);
                    q.Pos = center + new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * Art.Ground);
                    q.Z = h + 2f * px * k;
                };
            }
        }

        /// <summary>Embers drifting up out of a point (level III heat), tinted.</summary>
        public static void Embers(Vector2 at, float px, int n, float spread, Color tint)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Kit.Ember(at + Random.insideUnitCircle * spread * px, px, Random.Range(0.6f, 1.2f), new Vector2(Random.Range(-8f, 8f), Random.Range(12f, 30f)) * px, tint);
            }
        }

        // ------------------------------------------------------- catastrophes --

        /// <summary>
        /// A comet's tail along one stretch of a body's flight (call it every tick with where the body
        /// was and is): hot puffs evenly spaced so it reads as one stream at any speed, flames in the
        /// fighter's colour behind them, now and then a wisp of smoke.
        /// </summary>
        public static void Comet(Vector2 from, Vector2 to, float px, Color hot, Color edge)
        {
            Vector2 mid = (from + to) * 0.5f;
            if (!Kit.Visible(mid, 0.4f))
            {
                return;
            }
            float step = 1.8f * px / Fx.Detail();
            int n = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from, to) / step), 1, 22);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle core = Fx.Spawn(Art.Puffs[0], at, px, 0.1f, FxLayer.Behind, 3);
                core?.Sized(0.7f, 0.3f).Color(hot, Fx.WithA(Color.Lerp(hot, edge, 0.5f), 0.7f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.14f, 0.22f), FxLayer.Behind, 2);
                flame?.Moving(Random.insideUnitCircle * 5f * px, 5f * px, 2f).Sized(0.7f, 0.4f)
                    .Color(Fx.WithA(Color.Lerp(hot, edge, 0.35f), 0.95f), Fx.WithA(edge, 0.8f), Fx.WithA(Fx.Darken(edge, 0.4f), 0.4f), Fx.WithA(edge, 0f));
                if (Random.value < 0.12f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at + Random.insideUnitCircle * 1.5f * px, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0);
                    if (smoke != null)
                    {
                        smoke.Moving(Random.insideUnitCircle * 4f * px, 8f * px, 1.5f).Sized(0.6f, 1.1f)
                            .Color(Fx.WithA(Kit.Smoke, 0f), Fx.WithA(Kit.Smoke, 0.3f), Fx.WithA(Kit.Smoke, 0f));
                        smoke.Delay = 0.06f;
                    }
                }
            }
        }

        /// <summary>
        /// A crack torn into the ground from a to b: a dark jagged line (a few kinked segments) that
        /// stays a while, with a glowing seam in 'glow' along it that cools fast. 'thick' pixels.
        /// </summary>
        public static void Fissure(Vector2 a, Vector2 b, float px, WorldTile tile, Color glow, int thick = 2)
        {
            if (!Kit.Visible((a + b) * 0.5f, 0.45f) || (b - a).sqrMagnitude < px * px * 16f)
            {
                return;
            }
            Color dark = Fx.Darken(Fx.SoilColor(tile), 0.6f);
            Vector2 d = b - a;
            Vector2 side = new Vector2(-d.y, d.x).normalized;
            int segs = Mathf.Clamp(Mathf.RoundToInt(d.magnitude / (6f * px)), 2, 6);
            Vector2 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                float k = i / (float)segs;
                Vector2 next = Vector2.Lerp(a, b, k) + (i < segs ? side * Random.Range(-2.5f, 2.5f) * px : Vector2.zero);
                int t = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(thick, 1f, k)));
                Kit.Line(prev, next, px, Random.Range(7f, 9f), t, FxLayer.Ground, 1, Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0.8f), Fx.WithA(dark, 0.6f), Fx.WithA(dark, 0f));
                Kit.Line(prev, next, px, Random.Range(0.9f, 1.4f), 1, FxLayer.Ground, 2, new Color(1f, 0.97f, 0.75f, 1f), Fx.WithA(glow, 0.9f), Fx.WithA(Color.Lerp(glow, Kit.Fire3, 0.5f), 0.5f), Fx.WithA(glow, 0f));
                prev = next;
            }
        }

        /// <summary>Cracks running out from a point in every direction (the ground split by a blow), 'n' of them, 'radius' long.</summary>
        public static void Fissures(Vector2 at, float px, WorldTile tile, float radius, int n, Color glow)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return;
            }
            float a0 = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 from = at + dir * 2f * px;
                Fissure(from, from + dir * radius * Random.Range(0.6f, 1f), px, tile, glow, i % 2 == 0 ? 2 : 1);
            }
        }

        /// <summary>
        /// A blink: where a fighter vanishes or appears, a pillar of light (a vertical line), a ring at
        /// its feet and a few sparks: one moment there, the next a world away.
        /// </summary>
        public static void Blink(Vector2 feet, float px, float k, Color core, Color edge)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            Vector2 top = feet + new Vector2(0f, 26f * px * k);
            Kit.Line(feet, top, px, 0.16f, 3, FxLayer.Top, 6, Color.white, Fx.WithA(core, 0.9f), Fx.WithA(edge, 0f));
            Kit.Line(feet, top, px, 0.24f, 1, FxLayer.Top, 7, Color.white, Fx.WithA(edge, 0.8f), Fx.WithA(edge, 0f));
            Fx.Ring(feet, px, 0.2f, 2f, 12f * k, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.5f, Fx.WithA(core, 0.95f), Fx.WithA(edge, 0f));
            Kit.Sparks(feet + new Vector2(0f, 5f * px * k), px, 5, 40f, 90f, edge);
        }

        /// <summary>A body ploughing through the ground: clods and dust thrown up and out to both sides of its path.</summary>
        public static void Plough(Vector2 at, Vector2 dir, float px, WorldTile tile, float k = 1f)
        {
            if (!Kit.Visible(at) || tile == null || tile.Type == null)
            {
                return;
            }
            if (tile.Type.liquid)
            {
                Kit.Splash(at, px, tile, 0.5f * k);
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
            Color soil = Fx.SoilColor(tile);
            Vector2 d = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.right;
            Vector2 side = new Vector2(-d.y, d.x);
            int n = Fx.Count(4);
            for (int i = 0; i < n; i++)
            {
                float s = (i & 1) == 0 ? 1f : -1f;
                Vector2 v = (side * s * Random.Range(0.6f, 1f) + d * Random.Range(0.2f, 0.6f)) * Random.Range(18f, 34f) * px * k;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at + side * s * 2f * px, px, Random.Range(0.45f, 0.7f), FxLayer.Behind, 6);
                p?.Moving(v, Random.Range(6f, 14f) * px * k, 3f).Sized(0.6f, 1.25f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
            Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px, Random.Range(0.6f, 0.9f), FxLayer.Top, 4);
            if (c != null)
            {
                c.Moving((side * (Random.value < 0.5f ? 1f : -1f) * 0.8f + d * 0.4f) * Random.Range(20f, 40f) * px * k, Random.Range(40f, 80f) * px * k, 0f, 320f * px * k);
                c.Bounce = 0.3f;
                c.Spin = Random.Range(-720f, 720f);
                c.Color(soil, soil, Fx.WithA(soil, 0f));
            }
        }

        /// <summary>What a body smashed through leaves in the air: splinters and leaves (a tree, a house) or rock (a mountain).</summary>
        public static void Smash(Vector2 at, float px, WorldTile tile, bool rock, float k = 1f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color a = rock ? Fx.Lighten(Fx.SoilColor(tile), 0.1f) : new Color(0.45f, 0.3f, 0.17f, 1f);
            Color b = rock ? Fx.Darken(Fx.SoilColor(tile), 0.35f) : new Color(0.3f, 0.52f, 0.2f, 1f);
            int n = Fx.Count(rock ? 8 : 7);
            for (int i = 0; i < n; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground);
                Color c = (i & 1) == 0 ? a : b;
                Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at + dir * 2f * px, px, Random.Range(0.7f, 1.1f), FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(25f, 60f) * px * k, Random.Range(50f, 110f) * px * k, 0f, 330f * px * k);
                p.Bounce = 0.25f;
                p.Spin = Random.Range(-900f, 900f);
                p.Color(c, c, c, Fx.WithA(c, 0f));
            }
            Kit.DustRing(at, px, tile, rock ? 9 : 6, rock ? 75f : 55f, 0.7f, k);
            Kit.Flash(at + new Vector2(0f, 3f * px), px, 7, 0.07f, new Color(1f, 0.97f, 0.85f, 0.9f));
        }

        /// <summary>Light streaks rising around a body (a power-up: Second Wind, a stance charging).</summary>
        public static void Rising(Vector2 body, float px, int n, float width, Color c)
        {
            if (!Kit.Visible(body))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = body + new Vector2(Random.Range(-width, width), Random.Range(-4f, 2f)) * px;
                Particle p = Fx.Spawn(Art.Lines[Random.Range(0, 2)], at, px, Random.Range(0.2f, 0.32f), FxLayer.Top, 2);
                p?.Moving(new Vector2(0f, Random.Range(30f, 50f) * px)).Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.85f), Fx.WithA(c, 0f));
            }
        }
    }
}
