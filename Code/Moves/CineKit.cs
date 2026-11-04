using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// The shared visual vocabulary (Kit, Fx) in a form that keeps moving while a cinematic holds
    /// the world still. Kit and Fx spawn ordinary particles, which freeze with the world (that is
    /// what "the world stopped" means for them); a cinematic's own effects must keep playing, so
    /// every particle here can be flagged Particle.Cine, and any layer can be asked for (FxLayer.Cine
    /// draws above the dark veil). Same looks and pixel rules as Kit: sizes in sprite pixels of the
    /// unit causing it ('px'), skipped off screen, counts through Fx.Count.
    ///
    /// Used by the Kaiju Killer and the promotion ceremonies (the God's ascension freezes the world).
    /// </summary>
    internal static class CineKit
    {
        /// <summary>Marks a particle to keep playing through a cinematic freeze (when 'cine').</summary>
        public static Particle C(Particle p, bool cine)
        {
            if (p != null && cine)
            {
                p.Cine = true;
            }
            return p;
        }

        /// <summary>A coloured ring from r0 to r1 pixels ('ratio' 0.62 on the ground, 1 a circle in the air).</summary>
        public static Particle Ring(Vector2 at, float px, float life, float r0, float r1, int thick0, int thick1, float ratio, FxLayer layer,
            int order, bool cine, float delay, params Color[] colors)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return null;
            }
            Particle p = C(Fx.Ring(at, px, life, Mathf.Clamp(r0, 1f, 140f), Mathf.Clamp(r1, 1f, 140f), thick0, thick1, layer, order, ratio, 2.4f, colors), cine);
            if (p != null)
            {
                p.Delay = delay;
                if (delay > 0f)
                {
                    p.R.enabled = false;
                }
            }
            return p;
        }

        /// <summary>A flash disk ('rx' pixels wide) that blooms and fades.</summary>
        public static Particle Flash(Vector2 at, float px, int rx, float life, FxLayer layer, int order, bool cine, params Color[] colors)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return null;
            }
            Particle p = C(Fx.Spawn(Art.Disk(Mathf.Clamp(rx, 1, 140), 1f, 0.35f), at, px, life, layer, order), cine);
            p?.Sized(0.85f, 1.2f).Color(colors);
            return p;
        }

        /// <summary>A jagged starburst (the impact frame's star).</summary>
        public static Particle Burst(Vector2 at, float px, float life, FxLayer layer, int order, bool cine, params Color[] colors)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return null;
            }
            Particle p = C(Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px, life, layer, order), cine);
            if (p != null)
            {
                p.Rot = Random.Range(0f, 360f);
                p.Sized(1f, 1.18f).Color(colors);
            }
            return p;
        }

        /// <summary>A straight line (slash, constellation line, streak).</summary>
        public static Particle Line(Vector2 a, Vector2 b, float px, float life, int thick, FxLayer layer, int order, bool cine, params Color[] colors)
        {
            if (!Kit.Visible(a, 0.6f) && !Kit.Visible(b, 0.6f))
            {
                return null;
            }
            return C(Kit.Line(a, b, px, life, thick, layer, order, colors), cine);
        }

        /// <summary>Sparks flying out of a point (an 'up' bias lifts them).</summary>
        public static void Sparks(Vector2 at, float px, int n, float speedMin, float speedMax, Color c, FxLayer layer, bool cine, float up = 0f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) + up).normalized;
                Particle p = C(Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.14f, 0.28f), layer, 8), cine);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(speedMin, speedMax) * px, 0f, 6f);
                p.FaceVel = true;
                p.Color(Color.white, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>A punch or kick landing: an impact star and sparks away from the blow.</summary>
        public static void HitSpark(Vector2 at, float px, int size, Vector2 away, int sparks, Color tint, FxLayer layer, bool cine)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            size = Mathf.Clamp(size, 0, Art.HitStars.Length - 1);
            Particle star = C(Fx.Spawn(Art.HitStars[size], at, px, 0.07f + 0.03f * size, layer, 20), cine);
            if (star != null)
            {
                star.Rot = Random.Range(0f, 90f);
                star.Sized(1.15f, 0.7f).Color(Color.white, Color.Lerp(Color.white, tint, 0.45f), Fx.WithA(tint, 0f));
            }
            float baseAng = Mathf.Atan2(away.y, away.x);
            sparks = Fx.Count(sparks);
            for (int i = 0; i < sparks; i++)
            {
                float a = baseAng + Random.Range(-1.1f, 1.1f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = C(Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.1f, 0.18f), layer, 19), cine);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(60f, 130f) * px, 0f, 9f);
                p.FaceVel = true;
                p.Color(Color.white, tint, Fx.WithA(tint, 0f));
            }
        }

        /// <summary>Manga focus lines streaming in at a point.</summary>
        public static void FocusLines(Vector2 center, float px, int count, float r0, float r1, float life, Color c, FxLayer layer, bool cine)
        {
            if (!Kit.Visible(center))
            {
                return;
            }
            count = Fx.Count(count);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = C(Fx.Spawn(Art.Lines[Random.Range(1, 3)], center + dir * Random.Range(r0, r1) * px, px, life, layer, 2), cine);
                if (p == null)
                {
                    break;
                }
                p.Rot = a * Mathf.Rad2Deg - 90f;
                p.Moving(-dir * 45f * px);
                p.Color(Fx.WithA(c, c.a * 0.7f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>Speed lines streaming along 'dir' around a point.</summary>
        public static void SpeedLines(Vector2 at, Vector2 dir, float px, int n, float spreadPx, float life, Color c, FxLayer layer, bool cine, float speed = 40f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.up;
            Vector2 side = new Vector2(-dir.y, dir.x);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = at + side * Random.Range(-spreadPx, spreadPx) * px - dir * Random.Range(0f, spreadPx) * px;
                Particle p = C(Fx.Spawn(Art.Lines[Random.Range(0, Art.Lines.Length)], p0, px, life * Random.Range(0.7f, 1.1f), layer, 1), cine);
                if (p == null)
                {
                    break;
                }
                p.Rot = ang;
                p.Moving(dir * speed * px);
                p.Color(Fx.WithA(c, c.a * 0.85f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>Dust kicked out in a ring along the ground.</summary>
        public static void DustRing(Vector2 at, float px, WorldTile tile, int n, float speed, float life, float k, bool cine)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = C(Fx.Spawn(Art.Dust[Random.Range(0, 4)], at + dir * 2f * px, px, life * Random.Range(0.8f, 1.1f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7), cine);
                p?.Moving(dir * speed * Random.Range(0.75f, 1.1f) * px * k, Random.Range(3f, 7f) * px, 4f)
                    .Sized(0.5f, 1.15f)
                    .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>The small three-puff landing dust.</summary>
        public static void LandingPuff(Vector2 at, float px, WorldTile tile, bool cine)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float side = i == 0 ? -1f : i == 1 ? 1f : 0f;
                Particle p = C(Fx.Spawn(Art.Dust[i], at + new Vector2(side * 2f * px, 0f), px, 0.45f, i == 2 ? FxLayer.Top : FxLayer.Behind), cine);
                if (p == null)
                {
                    return;
                }
                p.Moving(new Vector2(side * 14f * px, 2f * px), i == 2 ? 6f * px : 3f * px, 5f);
                p.Sized(0.6f, 0.9f);
                p.Color(Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.5f));
            }
        }

        /// <summary>
        /// Chunks thrown off a point 'z' world units above the ground point 'at' (rocks, shell
        /// pieces): they arc, fall, bounce once or twice and fade. 'colors' are picked at random.
        /// </summary>
        public static void Debris(Vector2 at, float z, float px, int n, float speed, float lift, bool cine, params Color[] colors)
        {
            if (!Kit.Visible(at + new Vector2(0f, z)) || colors == null || colors.Length == 0)
            {
                return;
            }
            n = Fx.Count(n);
            float g = Mathf.Max(18f, 300f * px);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Color c = colors[Random.Range(0, colors.Length)];
                float fall = Mathf.Sqrt(2f * Mathf.Max(0f, z) / g);
                Particle p = C(Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at + dir * px, px, Random.Range(0.9f, 1.4f) + fall, FxLayer.Top, 4), cine);
                if (p == null)
                {
                    break;
                }
                p.Z = z;
                p.Moving(dir * Random.Range(0.4f, 1f) * speed, Random.Range(0.4f, 1f) * lift, 0f, g);
                p.Bounce = 0.3f;
                p.Spin = Random.Range(-720f, 720f);
                p.Color(c, c, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>A glowing ember drifting on its velocity.</summary>
        public static Particle Ember(Vector2 at, float px, float life, Vector2 vel, Color tint, FxLayer layer, bool cine, float drag = 1.2f)
        {
            Particle p = C(Fx.Spawn(Art.Pixel, at, px, life, layer, 9), cine);
            p?.Moving(vel, 0f, drag).Color(Color.Lerp(new Color(1f, 0.97f, 0.75f, 1f), tint, 0.3f), tint, Fx.WithA(Color.Lerp(tint, Kit.Fire3, 0.5f), 0f));
            return p;
        }

        /// <summary>The anime "ting!".</summary>
        public static Particle Glint(Vector2 at, float px, float life, Color c, float size, FxLayer layer, bool cine)
        {
            if (!Kit.Visible(at))
            {
                return null;
            }
            Particle p = C(Fx.Glint(at, px, life, c, size), cine);
            if (p != null && layer == FxLayer.Cine)
            {
                // Fx.Glint draws on Top (same sorting layer as Cine): lift it above the veil
                p.R.sortingOrder = 1010;
            }
            return p;
        }

        /// <summary>
        /// The impact frame: a flash disk and a starburst. 'black': the inverted black flash (dark core,
        /// a rim of 'rim'). 'size' 1 = a Meteor Slam.
        /// </summary>
        public static void ImpactFrame(Vector2 at, float px, float size, Color rim, bool black, FxLayer layer, bool cine, float hold = 0f)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color core = black ? new Color(0.05f, 0.02f, 0.04f, 1f) : Color.white;
            Particle flash = C(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(15 * size), 1, 140), 0.85f, 0.35f), at, px, 0.1f, layer, 30), cine);
            flash?.Sized(0.8f, 1.25f).Color(black ? rim : Color.white, Fx.WithA(rim, 0.8f), Fx.WithA(rim, 0f));
            // 'hold': the star stays on the frozen frame that long (a hit-stop), then goes
            Particle burst = C(Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * Mathf.Max(0.3f, size), 0.08f + 0.02f * size + hold, layer, 31), cine);
            if (burst != null)
            {
                burst.Rot = Random.Range(0f, 360f);
                if (hold > 0f)
                {
                    burst.Sized(1f, 1.08f).Color(core, core, core, Fx.WithA(core, 0.92f), Fx.WithA(rim, 0f));
                }
                else
                {
                    burst.Sized(1f, 1.2f).Color(core, Fx.WithA(core, 0.92f), Fx.WithA(rim, 0f));
                }
            }
            if (black)
            {
                Particle ring = C(Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * Mathf.Max(0.3f, size) * 1.25f, 0.1f + hold, layer, 29), cine);
                if (ring != null)
                {
                    ring.Rot = Random.Range(0f, 360f);
                    if (hold > 0f)
                    {
                        ring.Sized(1f, 1.1f).Color(rim, rim, Fx.WithA(rim, 0.85f), Fx.WithA(rim, 0f));
                    }
                    else
                    {
                        ring.Sized(1f, 1.3f).Color(rim, Fx.WithA(rim, 0.7f), Fx.WithA(rim, 0f));
                    }
                }
            }
        }

        /// <summary>
        /// The whole view flashes (a white frame or two). Drawn above the veil; 'life' 0.04 is about
        /// two frames at 60 fps, one at 30.
        /// </summary>
        public static void ViewFlash(Color c, float life)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null || !MapBox.isRenderGameplay())
            {
                return;
            }
            float h = cam.orthographicSize * 2f * 1.2f;
            float w = h * cam.aspect;
            Vector3 cp = cam.transform.position;
            Particle p = Fx.Spawn(Art.Pixel, new Vector2(cp.x, cp.y), 1f, life, FxLayer.Cine, 30);
            if (p == null)
            {
                return;
            }
            p.Cine = true;
            p.SX = w;
            p.SY = h;
            p.Color(c);
        }

        /// <summary>
        /// A crater with fissures (like Kit.Crater) whose glowing core lasts as long as asked ('glowLife'):
        /// a kaiju's grave keeps burning. Water gets a splash instead. Decals (not cinematic: they wait
        /// with the world).
        /// </summary>
        public static void Crater(Vector2 at, float px, WorldTile tile, float life, float glowLife, Color glow)
        {
            if (tile != null && tile.Type != null && (tile.Type.liquid || tile.Type.lava))
            {
                if (tile.Type.liquid)
                {
                    Kit.Splash(at, px, tile, 2f);
                }
                return;
            }
            if (!Kit.Visible(at))
            {
                return;
            }
            Art.CrackSet set = Art.Cracks[Random.Range(0, Art.Cracks.Length)];
            Color soil = Fx.SoilColor(tile);
            float flip = Random.value < 0.5f ? -1f : 1f;
            Color dark = Fx.Darken(soil, 0.6f);
            Color light = Fx.Lighten(soil, 0.35f);
            Particle d = Fx.Spawn(set.Dark, at, px, life, FxLayer.Ground, 1);
            if (d != null)
            {
                d.Snap = true;
                d.SX = flip;
                d.Color(Fx.WithA(dark, 0.92f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0f));
            }
            Particle l = Fx.Spawn(set.Light, at, px, life, FxLayer.Ground, 2);
            if (l != null)
            {
                l.Snap = true;
                l.SX = flip;
                l.Color(Fx.WithA(light, 0.9f), Fx.WithA(light, 0.9f), Fx.WithA(light, 0.8f), Fx.WithA(light, 0f));
            }
            float gl = Mathf.Min(life, glowLife);
            Particle g = set.Glow != null ? Fx.Spawn(set.Glow, at, px, gl, FxLayer.Ground, 3) : null;
            if (g != null)
            {
                g.Snap = true;
                g.SX = flip;
                g.Color(CoolingGlow(glow, gl));
            }
        }

        /// <summary>
        /// A hot glow cooling over 'life' seconds (evenly spaced stops, one per second or so): white-hot
        /// for an instant, the given colour within half a second and held, then orange, a dull red, gone.
        /// </summary>
        private static Color[] CoolingGlow(Color glow, float life)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(life * 2f) + 1, 6, 121);
            Color[] stops = new Color[n];
            Color hot = new Color(1f, 0.98f, 0.8f, 1f);
            Color orange = Color.Lerp(glow, Kit.Fire2, 0.55f);
            Color ember = Color.Lerp(Kit.Fire3, Kit.Fire2, 0.25f);
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                Color c;
                if (i == 0)
                {
                    c = hot;
                }
                else if (k < 0.25f)
                {
                    c = Color.Lerp(Fx.WithA(glow, 1f), Fx.WithA(orange, 0.95f), k / 0.25f);
                }
                else if (k < 0.65f)
                {
                    c = Color.Lerp(Fx.WithA(orange, 0.95f), Fx.WithA(ember, 0.6f), (k - 0.25f) / 0.4f);
                }
                else
                {
                    c = Color.Lerp(Fx.WithA(ember, 0.6f), Fx.WithA(Kit.Fire3, 0f), (k - 0.65f) / 0.35f);
                }
                stops[i] = c;
            }
            return stops;
        }

        /// <summary>
        /// Keeps a performer on its idle sprite. The game plays a unit's walk cycle (and its walking
        /// hop) whenever move_jump_offset.y is above zero, and a turned pose puts its pivot lift there
        /// (Moves.Apply): a God gazing at the sky, kneeling or hovering would run in place. The lift is
        /// moved into position_height instead, which the game draws the same way (it adds both). Call
        /// from Frame(), which runs right after the pose is applied.
        /// </summary>
        public static void StandStill(Move m)
        {
            Actor a = m.A;
            if (a == null || !m.Valid)
            {
                return;
            }
            float lift = a.move_jump_offset.y - m.HoverWas;
            if (lift > 0f)
            {
                a.move_jump_offset.y = m.HoverWas;
                a.position_height += lift;
            }
            if (m.HoverWas <= 0f)
            {
                a._jump_time = 0f;
                a.timer_jump_animation = 0f;
            }
        }

        /// <summary>
        /// The size to pass Cinema.Title so 'text' fits the screen's width. Cinema.Title scales its
        /// letters by the screen's height alone (a font pixel is 0.032 of the camera's half height at
        /// size 1), so "A COMBAT GOD HAS ASCENDED" at size 1 was 2.4 screen heights wide and ran off
        /// both sides of any screen. 'want' is kept when it fits.
        /// </summary>
        public static float TitleSize(string text, float want)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            Sprite s = PixelFont.Render(text, true);
            if (cam == null || s == null || s.rect.width <= 0f || cam.aspect <= 0f)
            {
                return want;
            }
            float fit = 0.92f * 2f * cam.aspect / (s.rect.width * 0.032f);
            return Mathf.Min(want, fit);
        }

        // (The world's scars under a freeze or a hit-stop, scorch marks and explosion pixels, are drawn
        // by the framework now: Kit.DrawScars after every skipped simulation step, for 1.5 s after the
        // last Kit.Ravage. Drawing them here as well faded the explosion pixels twice as fast.)

        /// <summary>A crescent swoosh (Art.Arc frames) at a fixed spot, mirrored with 'flip'.</summary>
        public static Particle Arc(Sprite[] arc, Vector2 at, float px, float life, bool flip, Color core, Color edge, FxLayer layer, int order, bool cine)
        {
            if (!Kit.Visible(at, 0.5f))
            {
                return null;
            }
            Particle p = C(Fx.Spawn(arc, at, px, life, layer, order), cine);
            if (p != null)
            {
                p.SX = flip ? -1f : 1f;
                p.Color(core, edge, Fx.WithA(edge, 0.6f));
            }
            return p;
        }

        /// <summary>
        /// A lit copy of the unit's sprite (its real colours) for drawing it above a dark veil, or a
        /// flat-colour silhouette ('silhouette') for flashes and glows. Held: keep it with Fx.Glue
        /// from the move's Frame and Kill() it when done.
        /// </summary>
        public static Particle Copy(Move m, FxLayer layer, int order, bool silhouette)
        {
            if (silhouette && !Fx.HasSilhouette)
            {
                return null;
            }
            Sprite s = Fx.UnitSprite(m.A);
            if (s == null)
            {
                return null;
            }
            Particle p = Fx.Spawn(s, m.DrawPos(), 1f, 1f, layer, order, silhouette);
            if (p == null)
            {
                return null;
            }
            p.Held = true;
            p.UseEuler = true;
            p.Cine = true;
            Fx.Glue(p, m, silhouette ? new Color(1f, 1f, 1f, 0f) : Color.white);
            return p;
        }
    }
}
