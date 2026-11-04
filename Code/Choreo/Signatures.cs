using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The schools' signatures: every blow that lands carries the mark of the school it was thrown in,
    /// on top of the common impact (ChoreoFx.Impact calls it), so a swordsman's hit never reads like a
    /// boxer's even in a crowd seen from far off:
    ///   Iron Fist        the force goes through: a blowout of streaks out of the far side, a flat shock ring
    ///   Tempest Leg      wind: two crescents whipping round the impact, an air-cut streak along the kick
    ///   Thousand Blades  steel: a long thin crescent cut across the body, a fan of steel sparks; a finisher
    ///                    leaves a second cut crossing the first (an X) a beat later
    ///   Ki Arts          light: a soft bloom swelling out of the impact, motes thrown off it
    ///   Earth Breaker    rock: chips of earth flung up and falling, a dull dust burst
    ///   Phantom Step     shadow: a dark tear across the impact, a ring imploding into it
    ///   Hunter's Way     the kill: sharp lines converging on the point, a mark flashing on it
    /// Its size follows the blow's weight and the technique's level (a level-I jab: a hint of it).
    /// </summary>
    internal static class Signatures
    {
        public static void Mark(Vector2 at, Vector2 d, float px, float k, float weight, Hue hue, FxLayer layer, bool finisher)
        {
            if (hue.School == School.None || weight < 0.25f)
            {
                return;
            }
            if (d.sqrMagnitude < 1e-6f)
            {
                d = Vector2.right;
            }
            d.Normalize();
            float lv = hue.Level <= 1 ? 0.7f : hue.Level == 2 ? 0.9f : 1.1f;
            float w = Mathf.Clamp(weight, 0.3f, 2.2f) * lv * (finisher ? 1.25f : 1f);
            float sk = Mathf.Sqrt(Mathf.Max(0.5f, k));
            switch (hue.School)
            {
                case School.IronFist:
                    Fist(at, d, px, sk, w, hue, layer);
                    break;
                case School.TempestLeg:
                    Wind(at, d, px, sk, w, hue, layer);
                    break;
                case School.ThousandBlades:
                    Steel(at, d, px, sk, w, hue, layer, finisher);
                    break;
                case School.KiArts:
                    Light(at, d, px, sk, w, hue, layer);
                    break;
                case School.EarthBreaker:
                    Rock(at, d, px, sk, w, hue, layer);
                    break;
                case School.PhantomStep:
                    Shadow(at, d, px, sk, w, hue, layer);
                    break;
                case School.HuntersWay:
                    Hunt(at, d, px, sk, w, hue, layer);
                    break;
            }
        }

        private static Vector2 Dir(float a)
        {
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>The force goes through the body: streaks bursting out of its far side, a flat shock ring.</summary>
        private static void Fist(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            float base0 = Mathf.Atan2(d.y, d.x);
            int n = Fx.Count(2 + Mathf.RoundToInt(w * 2.5f));
            for (int i = 0; i < n; i++)
            {
                float a = base0 + Random.Range(-0.45f, 0.45f);
                Vector2 from = at + d * 2f * px;
                Vector2 to = from + Dir(a) * Random.Range(4f, 7f + 4f * w) * px * k;
                Kit.Line(from, to, px, 0.07f + 0.02f * w, 1, layer, 17, Color.white, Fx.WithA(hue.Core, 0.8f), Fx.WithA(hue.Edge, 0f));
            }
            if (w >= 0.8f)
            {
                Fx.Ring(at, px, 0.12f + 0.04f * w, 2f, (4f + 4f * w) * k, 1, 1, layer, 16, 0.45f, 2.6f,
                    Fx.WithA(Color.white, 0.9f), Fx.WithA(hue.Core, 0.6f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>Wind: crescents whipping round the impact and an air-cut streak along the kick.</summary>
        private static void Wind(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt((4f + 2.5f * w) * k), 3, 18);
            for (int s = -1; s <= 1; s += 2)
            {
                Particle c = Fx.Spawn(Art.Arc(r, 0.55f, 40f, -80f, 1), at, px, 0.14f + 0.03f * w, layer, 17);
                if (c == null)
                {
                    break;
                }
                c.Rot = Random.Range(0f, 360f);
                c.Spin = 900f * s;
                c.SX = s;
                c.Sized(0.7f, 1.25f).Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(hue.Edge, 0f));
            }
            Vector2 side = new Vector2(-d.y, d.x);
            float len = (5f + 4f * w) * px * k;
            Kit.Line(at - d * len * 0.4f + side * px, at + d * len, px, 0.08f, 1, layer, 16, Fx.WithA(Color.white, 0.95f), Fx.WithA(hue.Edge, 0f));
            if (w >= 1f)
            {
                Kit.Line(at - d * len * 0.3f - side * 2f * px, at + d * len * 0.8f - side * 2f * px, px, 0.07f, 1, layer, 16, Fx.WithA(hue.Core, 0.85f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>Steel: a long thin crescent cut across the body and a fan of sparks; a finisher crosses it.</summary>
        private static void Steel(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer, bool finisher)
        {
            float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f + Random.Range(-35f, 35f);
            Cut(at, a, px, k, w, hue, layer, 0f);
            if (finisher || w >= 1.5f)
            {
                Cut(at, a + 70f + Random.Range(-15f, 15f), px, k, w * 0.9f, hue, layer, 0.07f);
            }
            int n = Fx.Count(3 + Mathf.RoundToInt(w * 2f));
            float base0 = Mathf.Atan2(d.y, d.x);
            for (int i = 0; i < n; i++)
            {
                Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.12f, 0.22f), layer, 18);
                if (p == null)
                {
                    break;
                }
                float b = base0 + Random.Range(-0.9f, 0.9f);
                p.Moving(Dir(b) * Random.Range(60f, 120f) * px * k, 0f, 4f, 0f);
                p.FaceVel = true;
                p.SY = 2.5f;
                p.Color(Color.white, new Color(1f, 0.95f, 0.7f, 1f), Fx.WithA(hue.Edge, 0f));
            }
        }

        private static void Cut(Vector2 at, float angDeg, float px, float k, float w, Hue hue, FxLayer layer, float delay)
        {
            float rad = angDeg * Mathf.Deg2Rad;
            Vector2 ld = Dir(rad);
            float half = (5f + 2.5f * w) * px * k;
            Particle l0 = Kit.Line(at - ld * half, at + ld * half, px, 0.12f + 0.03f * w, w >= 1.3f ? 2 : 1, layer, 19, Color.white, Fx.WithA(hue.Core, 0.9f), Fx.WithA(hue.Edge, 0f));
            if (l0 != null)
            {
                l0.Delay = delay;
            }
            // the gap the cut leaves: a thin dark line under the bright one, lingering a moment longer
            Particle l1 = Kit.Line(at - ld * half * 0.8f, at + ld * half * 0.8f, px, 0.2f + 0.04f * w, 1, layer, 18, Fx.WithA(hue.Dark, 0.7f), Fx.WithA(hue.Dark, 0f));
            if (l1 != null)
            {
                l1.Delay = delay + 0.03f;
            }
        }

        /// <summary>Light: a soft bloom swelling out of the impact, motes flung off it.</summary>
        private static void Light(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt((2.5f + 2f * w) * k), 2, 14);
            Particle b = Fx.Spawn(Art.Disk(r, 1f, 0.6f), at, px, 0.16f + 0.05f * w, layer, 15);
            b?.Sized(0.5f, 1.4f).Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(hue.Core, 0.7f), Fx.WithA(hue.Main, 0.3f), Fx.WithA(hue.Edge, 0f));
            int n = Fx.Count(3 + Mathf.RoundToInt(w * 3f));
            for (int i = 0; i < n; i++)
            {
                Particle m = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.25f, 0.45f), layer, 16);
                if (m == null)
                {
                    break;
                }
                m.Moving((Random.insideUnitCircle.normalized + d * 0.6f) * Random.Range(25f, 60f) * px * k, 0f, 3f, 0f);
                m.Sized(1.6f, 0.6f).Color(Color.white, Fx.WithA(hue.Core, 0.9f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>Rock: chips of earth flung up and falling, a dull burst of dust.</summary>
        private static void Rock(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            Color soil = Fx.SoilColor(Combat.TileAt(at));
            int n = Fx.Count(3 + Mathf.RoundToInt(w * 3f));
            for (int i = 0; i < n; i++)
            {
                Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px, Random.Range(0.35f, 0.6f), layer, 16);
                if (c == null)
                {
                    break;
                }
                Vector2 v = (Random.insideUnitCircle * 0.8f + d * 0.5f + Vector2.up * 0.9f) * Random.Range(40f, 80f) * px * k;
                c.Moving(v, 0f, 0.5f, 260f * px);
                c.Spin = Random.Range(-600f, 600f);
                c.Color(Color.Lerp(soil, Color.white, 0.2f), soil, Fx.WithA(Color.Lerp(soil, Color.black, 0.3f), 0f));
            }
            if (w >= 0.9f)
            {
                ChoreoFx.Smoke(at, px, 3 + Mathf.RoundToInt(w * 2f), 3f * k, Color.Lerp(soil, Color.white, 0.35f), 0.5f);
            }
        }

        /// <summary>Shadow: a dark tear across the impact, a ring collapsing into it.</summary>
        private static void Shadow(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            float a = Mathf.Atan2(d.y, d.x) + Mathf.PI * 0.5f + Random.Range(-0.6f, 0.6f);
            Vector2 ld = Dir(a);
            float half = (4f + 2f * w) * px * k;
            Color dark = Fx.WithA(Color.Lerp(hue.Dark, Color.black, 0.4f), 0.9f);
            Kit.Line(at - ld * half, at + ld * half, px, 0.16f + 0.04f * w, w >= 1.2f ? 2 : 1, layer, 19, dark, Fx.WithA(hue.Main, 0.6f), Fx.WithA(hue.Main, 0f));
            Fx.Ring(at, px, 0.16f, (5f + 3f * w) * k, 1f, 1, 1, layer, 17, 1f, 2f, Fx.WithA(hue.Main, 0f), Fx.WithA(hue.Main, 0.8f), Fx.WithA(dark, 0.9f));
            int n = Fx.Count(2 + Mathf.RoundToInt(w * 2f));
            for (int i = 0; i < n; i++)
            {
                Particle m = Fx.Spawn(Art.Pixel, at + Random.insideUnitCircle * 3f * px, px, Random.Range(0.3f, 0.5f), layer, 15);
                m?.Moving(new Vector2(Random.Range(-8f, 8f), Random.Range(6f, 16f)) * px, 0f, 1f, 0f).Sized(1.5f, 0.8f).Color(dark, Fx.WithA(hue.Main, 0.5f), Fx.WithA(hue.Main, 0f));
            }
        }

        /// <summary>The kill: sharp lines converging on the point, a mark flashing on it.</summary>
        private static void Hunt(Vector2 at, Vector2 d, float px, float k, float w, Hue hue, FxLayer layer)
        {
            int n = Fx.Count(3 + Mathf.RoundToInt(w));
            float base0 = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = base0 + i * Mathf.PI * 2f / n;
                Vector2 o = Dir(a) * (6f + 3f * w) * px * k;
                Kit.Line(at + o, at + o * 0.25f, px, 0.07f, 1, layer, 17, Fx.WithA(Color.white, 0.95f), Fx.WithA(hue.Edge, 0f));
            }
            float m = 2.5f * px * k;
            Kit.Line(at + new Vector2(-m, -m), at + new Vector2(m, m), px, 0.14f, 1, layer, 18, Fx.WithA(hue.Core, 0.95f), Fx.WithA(hue.Edge, 0f));
            Kit.Line(at + new Vector2(-m, m), at + new Vector2(m, -m), px, 0.14f, 1, layer, 18, Fx.WithA(hue.Core, 0.95f), Fx.WithA(hue.Edge, 0f));
        }
    }
}
