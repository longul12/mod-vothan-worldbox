using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The engine's effect vocabulary on top of Kit and Fx: smears, impacts scaled to a blow,
    /// dust at the heels and skid marks, motes gathering, flashes over whole units. Every call
    /// skips itself off screen and draws at the pixel size it is given (the unit's, Move.Px).
    /// </summary>
    internal static class ChoreoFx
    {
        /// <summary>
        /// A limb's smear from 'from' to 'to' (a fist's head at 'to'), in the move's colours, living
        /// 'life' seconds. 'head': the fist/foot blob's radius in pixels; 'thick': the streak's width.
        /// </summary>
        public static Particle Smear(Vector2 from, Vector2 to, float px, float life, int head, int thick, Color core, Color edge, FxLayer layer = FxLayer.Over, int order = 7)
        {
            Vector2 d = to - from;
            float len = d.magnitude / px;
            if (len < 2f || !Fx.OnScreen(to, 0.2f))
            {
                return null;
            }
            Particle p = Fx.Spawn(ChoreoArt.Smear(Mathf.RoundToInt(len), head, thick), to, px, life, layer, order);
            if (p == null)
            {
                return null;
            }
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Color(core, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
            return p;
        }

        /// <summary>Two puffs kicked out at its heels towards 'back' (drawn behind it, so the pose stays readable).</summary>
        public static void HeelDust(Vector2 at, Vector2 back, float px, WorldTile tile, float k)
        {
            if (!Fx.OnScreen(at))
            {
                return;
            }
            back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector2.left;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            Vector2 side = new Vector2(-back.y, back.x);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                Particle p = Fx.Spawn(Art.Dust[i + Random.Range(0, 2)], at + (back * 1.5f + side * s * 1.6f) * px, px, 0.32f, FxLayer.Behind, 2);
                if (p == null)
                {
                    return;
                }
                p.Moving((back * 10f + side * s * 5f) * px * k, 2.5f * px, 6f).Sized(0.5f, 0.9f)
                    .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>Two dark furrows on the ground where feet were dragged from a to b (they fade over a second).</summary>
        public static void Skid(Vector2 a, Vector2 b, float px, WorldTile tile)
        {
            if (!Fx.OnScreen(a) || tile != null && tile.Type != null && tile.Type.liquid)
            {
                return;
            }
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.01f)
            {
                return;
            }
            Vector2 side = Choreo.Perp(d.normalized);
            Color dark = Fx.WithA(Fx.Darken(Fx.SoilColor(tile), 0.45f), 0.75f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 off = side * s * 1.2f * px;
                Particle p = Kit.Line(a + off, b + off, px, 1.2f, 1, FxLayer.Ground, 3, dark, dark, Fx.WithA(dark, 0f));
                if (p != null)
                {
                    p.Snap = true;
                }
            }
        }

        /// <summary>A white (or coloured) flash over another unit's whole sprite, following it.</summary>
        public static Particle FlashUnit(Actor v, Color c, float life)
        {
            if (v == null || !v.is_visible || !Fx.HasSilhouette)
            {
                return null;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return null;
            }
            Vector3 cp = v.cur_transform_position;
            Particle p = Fx.Spawn(s, new Vector2(cp.x, cp.y), 1f, life, FxLayer.Over, 2, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = v.current_rotation;
            p.SX = v.current_scale.x;
            p.SY = v.current_scale.y;
            p.Color(Fx.WithA(c, 0.95f), Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            long id = v.Sid();
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    Vector3 at = v.cur_transform_position;
                    q.Pos = new Vector2(at.x, at.y);
                    q.Euler = v.current_rotation;
                }
            };
            return p;
        }

        /// <summary>
        /// A blow landing at 'at' along 'd', 'weight' 0.3 (a jab) .. 2 (a crushing finisher): a hit star,
        /// sparks flying on along the blow, a flash disk and a ring for the bigger ones, focus lines for
        /// finishers. Colours from 'hue'.
        /// </summary>
        public static void Impact(Vector2 at, Vector2 d, float px, float k, float weight, Hue hue, FxLayer layer = FxLayer.Top, bool finisher = false)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            // the star: small for a jab, a size up for a solid blow, the big one only for crushing ones
            int size = weight < 0.95f ? 0 : weight < 1.75f ? 1 : 2;
            // (level I's blows are plain and quick: smaller stars)
            float sk = Mathf.Lerp(1f, k, 0.5f) * (size == 0 ? Mathf.Lerp(0.7f, 1f, weight / 0.95f) : 1f) * (hue.Level <= 1 ? 0.78f : hue.Level == 2 ? 0.9f : 1f);
            Particle star = Fx.Spawn(Art.HitStars[size], at, px * sk, 0.05f + 0.015f * size, layer, 20);
            if (star != null)
            {
                star.Rot = Random.Range(0f, 90f);
                star.Sized(1.1f, 0.7f).Color(Color.white, Fx.WithA(hue.Core, 0.95f), Fx.WithA(hue.Edge, 0f));
            }
            float base0 = Mathf.Atan2(d.y, d.x);
            int n = Fx.Count(2 + Mathf.RoundToInt(weight * 2f));
            for (int i = 0; i < n; i++)
            {
                float a = base0 + Random.Range(-1f, 1f);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.08f, 0.16f), layer, 19);
                if (p == null)
                {
                    break;
                }
                p.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(55f, 110f) * px * Mathf.Sqrt(k), 0f, 8f);
                p.FaceVel = true;
                p.Color(Color.white, hue.Edge, Fx.WithA(hue.Edge, 0f));
            }
            if (weight >= 1.1f)
            {
                Kit.Flash(at, px, Mathf.Max(2, Mathf.RoundToInt((1.5f + weight * 1.5f) * k)), 0.05f, Fx.WithA(hue.Core, 0.95f), layer, 18);
            }
            if (weight >= 1.25f || finisher)
            {
                Fx.Ring(at, px, 0.14f + 0.03f * weight, 2f, (3f + 4f * weight) * k, weight >= 1.6f ? 2 : 1, 1, layer, 3, 0.55f, 2.5f,
                    Fx.WithA(hue.Core, 0.85f), Fx.WithA(hue.Edge, 0.5f), Fx.WithA(hue.Rim, 0f));
            }
            if (finisher)
            {
                Fx.FocusLines(at, px, Fx.Count(4 + Mathf.RoundToInt(weight * 2f)), 8f * k, 13f * k, 0.09f);
            }
            // the school's own mark on it
            Signatures.Mark(at, d, px, k, weight, hue, layer, finisher);
        }

        /// <summary>Motes of light drawn in to 'at' from a ring 'r0' pixels out (charging, gathering).</summary>
        public static void Motes(Vector2 at, float px, int n, float r0, float life, Color c0, Color c1, FxLayer layer = FxLayer.Over)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 from = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(r0 * 0.7f, r0) * px;
                Particle m = Fx.Spawn(Art.Pixel, from, px, life, layer, 7);
                m?.Moving((at - from) / life).Sized(1.4f, 0.8f).Color(Fx.WithA(c1, 0f), Fx.WithA(c0, 0.95f), Fx.WithA(c1, 0.6f));
            }
        }

        /// <summary>A puff of smoke or dust of colour 'c' rising from 'at'.</summary>
        public static void Smoke(Vector2 at, float px, int n, float spread, Color c, float life = 0.7f)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 off = Random.insideUnitCircle * spread * px;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, Art.Dust.Length)], at + off, px, life * Random.Range(0.8f, 1.15f), FxLayer.Top, 6);
                p?.Moving(off * 2f + new Vector2(0f, Random.Range(4f, 10f) * px), Random.Range(2f, 6f) * px, 2f).Sized(0.7f, 1.3f)
                    .Color(Fx.WithA(c, 0.75f), Fx.WithA(c, 0.45f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>A sound of a blow fitting its limb and weight.</summary>
        public static void BlowSound(Limb limb, float weight, bool armed, WorldTile tile)
        {
            if (limb == Limb.Blade)
            {
                Choreo.Sound(armed ? "event:/SFX/HIT/HitFlesh" : "event:/SFX/HIT/HitGeneric", tile);
                if (weight >= 1.2f)
                {
                    Choreo.Sound("event:/SFX/HIT/HitSwordSword", tile, 0.08f);
                }
                return;
            }
            Choreo.Sound(weight >= 1.1f ? "event:/SFX/HIT/HitGeneric" : "event:/SFX/OTHER/Punch", tile);
            if (weight >= 1.6f)
            {
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
            }
        }
    }
}
