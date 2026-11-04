using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// The shared visual vocabulary every technique draws with, so the whole mod reads as one
    /// animator's hand: craters, shockwaves, swooshes, slash lines, impact frames, embers, shakes.
    /// Everything is in sprite pixels of the unit that causes it ('px' = world units per pixel,
    /// usually Move.Px) and skips itself when off screen.
    /// </summary>
    internal static class Kit
    {
        public static readonly Color White = new Color(1f, 1f, 1f, 1f);
        public static readonly Color Core = new Color(1f, 0.98f, 0.82f, 1f);
        public static readonly Color Fire1 = new Color(1f, 0.84f, 0.32f, 1f);
        public static readonly Color Fire2 = new Color(1f, 0.48f, 0.12f, 1f);
        public static readonly Color Fire3 = new Color(0.78f, 0.18f, 0.07f, 1f);
        public static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);

        // ------------------------------------------------------------- the world --

        private static TerraformOptions _scorch, _blast, _dig;

        [SakugaInit]
        private static void InitTerraform()
        {
            _scorch = Terra("sakuga_scorch", false);
            _blast = Terra("sakuga_blast", true);
            // lowering a tile with the game's "flash" terraform blinks the whole tile white: dig without it
            _dig = AssetManager.terraform.get("sakuga_dig") ?? AssetManager.terraform.add(new TerraformOptions { id = "sakuga_dig", flash = false });
        }

        private static TerraformOptions Terra(string id, bool explode)
        {
            TerraformOptions t = AssetManager.terraform.get(id);
            if (t != null)
            {
                return t;
            }
            return AssetManager.terraform.add(new TerraformOptions
            {
                id = id,
                add_burned = true,
                explode_tile = explode,
                // the game's explosion lowers soft tiles a step per blast (sand becomes water):
                // Ravage digs itself, carefully, so the game's own lowering is off (-1)
                explode_strength = -1,
                explosion_pixel_effect = explode,
                explode_and_set_random_fire = false,
                remove_frozen = true,
                damage = 0,            // units are hurt by the technique itself, never by the ground
                damage_buildings = false,
                apply_force = false,
                shake = false,
                flash = false
            });
        }

        /// <summary>Real time of the last Ravage (the frame draws scorch marks while the world is held).</summary>
        public static float LastRavage = -99f;

        /// <summary>
        /// The game draws scorch marks and explosion pixels only in its simulation step, which a hit-stop
        /// or a cinematic skips: draw them anyway for a moment after a Ravage, so destruction lands on the
        /// impact frame.
        /// </summary>
        public static void DrawScars(float dt)
        {
            if (World.world == null || Time.unscaledTime - LastRavage > 1.5f)
            {
                return;
            }
            try
            {
                World.world.burned_layer?.draw(dt);
                ExplosionsEffects ex = World.world.explosion_layer;
                if (ex != null && ex.delayedBombs.Count == 0)
                {
                    ex.update(0f);
                    ex.draw(dt);
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("scars", e);
            }
        }

        /// <summary>
        /// The game's explosion pixels (each tile flickers white for a moment) read as flat white squares
        /// up close: only a God-scale blast (force 4) shows them, on its middle third.
        /// </summary>
        private static void Pixels(WorldTile tile, int r)
        {
            int core = Mathf.Max(1, Mathf.RoundToInt(r * 0.35f));
            if (r < 2 || World.world.explosion_layer == null)
            {
                return;
            }
            BrushData brush = Brush.get(core);
            for (int i = 0; i < brush.pos.Length; i++)
            {
                BrushPixelData px = brush.pos[i];
                int x = tile.x + px.x, y = tile.y + px.y;
                if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
                {
                    continue;
                }
                WorldTile w = World.world.GetTileSimple(x, y);
                if (w != null && !w.Type.liquid)
                {
                    World.world.explosion_layer.setDirty(w, px.dist, core + 0.5f);
                }
            }
        }

        /// <summary>The brush radius for a blast radius in tiles (radius under 0.75: a single tile).</summary>
        public static int BrushRadius(float radiusTiles)
        {
            return radiusTiles < 0.75f ? 0 : Mathf.Clamp(Mathf.FloorToInt(radiusTiles + 0.5f), 1, 16);
        }

        /// <summary>
        /// Real damage to the world where a technique lands (the "Environment damage" setting):
        ///   force 1 scorches the ground (burn marks),
        ///   force 2 also tears the grass off the middle,
        ///   force 3 also digs the middle a step deeper and sets the grass around the crater alight,
        ///   force 4 (God scale) digs deeper and wider, its core flickering with the game's explosion pixels.
        /// Digging never turns ground into water. Trees and buildings in the radius take damage,
        /// never the attacker's own kingdom's. Units are never hurt by this (the technique does that).
        /// 'fire': false for a blast without flames. 'keep'/'keepR': a pocket (the performer's feet,
        /// a lane it will dash down) where nothing is dug or lit, only scorched.
        /// 'radiusTiles' is the scorch radius; the crater is about one tile smaller.
        /// </summary>
        public static void Ravage(Vector2 at, float radiusTiles, int force, Actor by, bool fire = true, Vector2? keep = null, float keepR = 0f,
            Actor spare = null, Actor spare2 = null)
        {
            if (Settings.Destruction <= 0 || force <= 0 || World.world == null)
            {
                return;
            }
            // only the schools of heat and magma set the world alight (Ruin.Burns): the rest scorch and dig
            fire = fire && Ruin.Burns(by);
            WorldTile tile = Combat.TileAt(at);
            if (tile == null)
            {
                return;
            }
            if (Settings.Destruction == 1)
            {
                force = 1;
            }
            int r = BrushRadius(radiusTiles);
            LastRavage = Time.unscaledTime;
            try
            {
                // scorch (burn marks) over the whole radius; the dig and the fire below do the rest
                if (_scorch != null)
                {
                    MapAction.applyTileDamage(tile, r, _scorch);
                }
                if (force >= 4)
                {
                    Pixels(tile, r);
                }
                float dig = force >= 4 ? 0.75f : force == 3 ? 0.55f : force == 2 ? 0.35f : -1f;
                float keep2 = keepR * keepR;
                // how hard a building is struck: a light blow (force 1) scars the ground but breaks no wall;
                // a strong one cracks it, a heavy one wrecks it, a world-shaking one levels it
                float dmg = force switch { 1 => 0f, 2 => 60f, 3 => 180f, _ => 500f };
                Kingdom own = by != null ? by.kingdom : null;
                // ('spare'/'spare2': kingdoms that also keep their buildings, e.g. both sides of an even clash)
                Kingdom s1 = spare != null && spare.isAlive() ? spare.kingdom : null;
                Kingdom s2 = spare2 != null && spare2.isAlive() ? spare2.kingdom : null;
                bool all = Unsparing(by);
                Kingdom burnOwn = own;
                if (all)
                {
                    // a blow is a force of nature: it breaks whatever it lands on, its own town's walls too
                    // (fire still never starts under its own people's roofs: fire spreads to them)
                    own = s1 = s2 = null;
                }
                int r2 = Mathf.Max(1, r * r);
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int d2 = dx * dx + dy * dy;
                        if (d2 > r2 && !(r == 0 && d2 == 0))
                        {
                            continue;
                        }
                        int x = tile.x + dx, y = tile.y + dy;
                        if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
                        {
                            continue;
                        }
                        WorldTile w = World.world.GetTileSimple(x, y);
                        if (w == null)
                        {
                            continue;
                        }
                        float d = Mathf.Sqrt(d2);
                        bool kept = keep.HasValue && (new Vector2(x + 0.5f, y + 0.5f) - keep.Value).sqrMagnitude <= keep2;
                        if (!kept && dig >= 0f && d <= Mathf.Max(0.5f, r * dig))
                        {
                            Dig(w, force >= 4 && d <= r * 0.35f ? 2 : force >= 3 ? 1 : 0);
                        }
                        // fire only where something can burn (grass, beyond the dug middle), sparser on big craters;
                        // never under a building of the attacker's own kingdom
                        if (!kept && fire && force >= 3 && Settings.Destruction >= 2 && d > Mathf.Max(0.5f, r * dig) && !w.Type.liquid
                            && w.top_type != null && !(w.building != null && (Spared(w.building.kingdom, own, s1, s2) || w.building.kingdom != null && w.building.kingdom == burnOwn))
                            && UnityEngine.Random.value < (force >= 4 ? 0.25f : 0.18f) * Mathf.Min(1f, 4f / Mathf.Max(1, r)))
                        {
                            w.startFire(true);
                        }
                        if (!w.hasBuilding())
                        {
                            continue;
                        }
                        Building b = w.building;
                        if (dmg <= 0f || b == null || !b.isAlive() || Spared(b.kingdom, own, s1, s2))
                        {
                            continue;
                        }
                        // it stands on every tile of its footprint: one blow, as hard as at its nearest tile
                        float hit = dmg * (1f - 0.6f * d / Mathf.Max(1f, r));
                        if (!_struck.TryGetValue(b, out float was) || hit > was)
                        {
                            _struck[b] = hit;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("ravage", e);
            }
            if (_trench == 0 || _trenchFrame != Time.frameCount)
            {
                _trench = 0;   // (a line left open by a caller that failed: closed now)
                Strike(by);
            }
        }

        private static bool Spared(Kingdom k, Kingdom own, Kingdom s1, Kingdom s2)
        {
            return k != null && (k == own || k == s1 || k == s2);
        }

        /// <summary>
        /// Environment damage at full: a blow breaks every building it lands on, friend's or foe's,
        /// whoever strikes it (how much it breaks depends on the blow's force: see Ravage). Units are
        /// another matter: only enemies are ever hurt.
        /// </summary>
        public static bool Unsparing(Actor by)
        {
            return Settings.Destruction >= 2 && by != null;
        }

        // the buildings a blast (a whole line of blasts) hits, each once, with its blow
        private static readonly Dictionary<Building, float> _struck = new Dictionary<Building, float>();
        private static int _trench, _trenchFrame;

        /// <summary>
        /// Ravages from here to EndTrench (overlapping blasts along a line) strike each building once, at
        /// EndTrench, as hard as the nearest blast would. Kit.Trench does it; so may any loop of Ravages.
        /// Same frame, and always paired (try/finally).
        /// </summary>
        public static void BeginTrench()
        {
            if (_trenchFrame != Time.frameCount)
            {
                _trench = 0;
            }
            _trenchFrame = Time.frameCount;
            _trench++;
        }

        public static void EndTrench(Actor by)
        {
            if (_trench > 0)
            {
                _trench--;
            }
            if (_trench == 0)
            {
                Strike(by);
            }
        }

        private static void Strike(Actor by)
        {
            if (_struck.Count == 0)
            {
                return;
            }
            try
            {
                foreach (KeyValuePair<Building, float> kv in _struck)
                {
                    Building b = kv.Key;
                    if (b != null && b.isAlive())
                    {
                        b.getHit(kv.Value, true, AttackType.Other, by, false, false, true);
                    }
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("ravage", e);
            }
            finally
            {
                _struck.Clear();
            }
        }

        /// <summary>Tears the grass off a tile, then (deeper) lowers its ground a step, never into water.</summary>
        private static void Dig(WorldTile w, int deeper)
        {
            if (w.Type.liquid || w.Type.lava || w.Type.block)
            {
                return;
            }
            if (w.top_type != null)
            {
                MapAction.decreaseTile(w, true, _dig);   // only the grass goes
            }
            for (int i = 0; i < deeper; i++)
            {
                if (w.top_type != null || w.main_type == null)
                {
                    return;
                }
                TileType down = w.main_type.decrease_to;
                if (down == null || down.liquid || down.lava || down.decrease_to == null || down.decrease_to.liquid && i > 0)
                {
                    return;   // never dig through to water
                }
                MapAction.decreaseTile(w, true, _dig);
            }
        }

        /// <summary>
        /// Ravage along a line (a trench, a fissure, a body dragged through the ground). The trench itself
        /// doesn't burn (each blast's burning rim would lie inside the next, a carpet of fire): with
        /// 'fire', force 3+ lights a few tiles along both banks instead.
        /// </summary>
        public static void Trench(Vector2 a, Vector2 b, float widthTiles, int force, Actor by, bool fire = true, Vector2? keep = null, float keepR = 0f,
            Actor spare = null, Actor spare2 = null)
        {
            float len = Vector2.Distance(a, b);
            float step = Mathf.Max(0.8f, widthTiles);
            int n = Mathf.Clamp(Mathf.CeilToInt(len / step), 1, 48);
            // the blasts overlap: a building near the line is struck once, after the last of them
            BeginTrench();
            try
            {
                for (int i = 0; i <= n; i++)
                {
                    Ravage(Vector2.Lerp(a, b, i / (float)n), widthTiles, force, by, false, keep, keepR, spare, spare2);
                }
            }
            finally
            {
                EndTrench(by);
            }
            if (fire && force >= 3 && Settings.Destruction >= 2 && len > 0.01f)
            {
                Vector2 dir = (b - a) / len;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float off = widthTiles + 0.6f;
                float chance = force >= 4 ? 0.3f : 0.2f;
                int m = Mathf.Clamp(Mathf.RoundToInt(len), 1, 64);
                for (int i = 0; i < m; i++)
                {
                    Vector2 mid = a + dir * ((i + 0.5f) * len / m);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (UnityEngine.Random.value < chance)
                        {
                            Vector2 p = mid + side * s * (off + UnityEngine.Random.Range(-0.3f, 0.3f));
                            WorldTile bt = Combat.TileAt(Combat.Clamp(p));
                            bool spared = bt != null && bt.building != null && !Unsparing(by)
                                && Spared(bt.building.kingdom, null, spare != null && spare.isAlive() ? spare.kingdom : null, spare2 != null && spare2.isAlive() ? spare2.kingdom : null);
                            if (!spared && (!keep.HasValue || (p - keep.Value).sqrMagnitude > keepR * keepR))
                            {
                                Ignite(p, by, by != null ? by.current_position : p, 0f);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Lights one tile (a line's fire: its banks, a scar's glowing end) if the Environment damage
        /// setting is at full. Never water, lava or a wall, never a building of 'by's own kingdom, and
        /// never within 'safe' tiles of 'feet' when 'by' can burn.
        /// </summary>
        public static bool Ignite(Vector2 at, Actor by, Vector2 feet, float safe = 3f)
        {
            if (Settings.Destruction < 2 || World.world == null)
            {
                return false;
            }
            if (!Ruin.Burns(by))
            {
                return false;
            }
            bool immune = by != null && by.isAlive() && Combat.Fireproof(by);
            if (!immune && safe > 0f && (at - feet).sqrMagnitude < safe * safe)
            {
                return false;
            }
            WorldTile t = Combat.TileAt(Combat.Clamp(at));
            if (t == null || t.Type == null || t.Type.liquid || t.Type.lava || t.Type.block || t.isOnFire())
            {
                return false;
            }
            if (by != null && by.kingdom != null && t.hasBuilding() && t.building != null && t.building.kingdom == by.kingdom)
            {
                return false;
            }
            try
            {
                LastRavage = Time.unscaledTime;
                return t.startFire(true);
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("ignite", e);
                return false;
            }
        }

        public static bool Visible(Vector2 at, float margin = 0.35f)
        {
            return Fx.OnScreen(at, margin);
        }

        /// <summary>A crater with fissures: dark and light layers, and (hot) a glowing core that cools. 'glow' tints the heat.</summary>
        public static void Crater(Vector2 at, float px, WorldTile tile, bool hot, float life, bool small = false, Color? glow = null)
        {
            if (!Visible(at) || tile != null && tile.Type != null && tile.Type.liquid)
            {
                if (tile != null && tile.Type != null && tile.Type.liquid)
                {
                    Splash(at, px, tile, small ? 0.6f : 1.2f);
                }
                return;
            }
            Art.CrackSet set = small ? Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)] : Art.Cracks[Random.Range(0, Art.Cracks.Length)];
            Color soil = Fx.SoilColor(tile);
            float flip = Random.value < 0.5f ? -1f : 1f;
            Color dark = Fx.Darken(soil, 0.6f);
            Color light = Fx.Lighten(soil, 0.35f);
            Particle d = Fx.Spawn(set.Dark, at, px, life, FxLayer.Ground, 1);
            if (d != null)
            {
                d.Snap = true;
                d.SX = flip;
                d.Color(Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0f));
            }
            Particle l = Fx.Spawn(set.Light, at, px, life, FxLayer.Ground, 2);
            if (l != null)
            {
                l.Snap = true;
                l.SX = flip;
                l.Color(Fx.WithA(light, 0.9f), Fx.WithA(light, 0.9f), Fx.WithA(light, 0.8f), Fx.WithA(light, 0f));
            }
            if (!hot || set.Glow == null)
            {
                return;
            }
            Particle g = Fx.Spawn(set.Glow, at, px, Mathf.Min(life, 2.5f + life * 0.25f), FxLayer.Ground, 3);
            if (g != null)
            {
                g.Snap = true;
                g.SX = flip;
                Color c = glow ?? Fire2;
                g.Color(new Color(1f, 0.97f, 0.7f, 1f), Color.Lerp(Fire2, c, 0.6f), Fx.WithA(Color.Lerp(Fire3, c, 0.4f), 0.8f),
                    new Color(0.4f, 0.06f, 0.03f, 0.35f), new Color(0.2f, 0.03f, 0.02f, 0f));
            }
        }

        /// <summary>Water thrown up instead of a crater.</summary>
        public static void Splash(Vector2 at, float px, WorldTile tile, float k)
        {
            if (!Visible(at))
            {
                return;
            }
            Color water = Fx.SoilColor(tile);
            Fx.Ring(at, px, 0.5f, 3f, 24f * k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(water, 0f));
            int n = Fx.Count(Mathf.RoundToInt(12 * k));
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Pixel, at + dir * 2f * px, px, Random.Range(0.4f, 0.7f), FxLayer.Top, 4);
                p?.Moving(dir * Random.Range(15f, 45f) * px * k, Random.Range(40f, 90f) * px * k, 0f, 300f * px).Sized(1.5f, 1f)
                    .Color(Color.white, Fx.WithA(water, 0.8f), Fx.WithA(water, 0f));
            }
        }

        public static void Ember(Vector2 at, float px, float life, Vector2 vel, Color? tint = null)
        {
            Particle p = Fx.Spawn(Art.Pixel, at, px, life, FxLayer.Top, 9);
            if (p == null)
            {
                return;
            }
            Color c = tint ?? Fire2;
            p.Moving(vel, 0f, 1.2f).Color(Color.Lerp(new Color(1f, 0.95f, 0.6f, 1f), c, 0.3f), c, Fx.WithA(Color.Lerp(c, Fire3, 0.6f), 0f));
        }

        /// <summary>Camera shake, only when the spot is on screen and the camera is close.</summary>
        public static void Shake(Vector2 at, float duration, float strength, float k = 1f)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null || !Fx.OnScreen(at, 0.05f) || cam.orthographicSize > 90f)
            {
                return;
            }
            float intensity = cam.orthographicSize * strength * k;
            if (World.world._shake_timer > 0f && World.world._shake_intensity > intensity)
            {
                return;   // (a bigger shake is still running)
            }
            World.world.startShake(duration, 0.02f, intensity, true, true);
        }

        /// <summary>A hit-stop (the world freezes on the impact for a few frames), scaled by the setting.</summary>
        public static void Stop(Vector2 at, float seconds)
        {
            if (Settings.HitStopScale > 0f)
            {
                HitStop.Request(at, seconds * Settings.HitStopScale);
            }
        }

        /// <summary>A crescent swoosh around a unit's body (Art.Arc frames), mirrored to 'facing' (+1 right, -1 left), glued to it.</summary>
        public static Particle Swish(Move m, Sprite[] arc, float life, Vector2 offsetPx, float facing, Color core, Color edge, int order = 5)
        {
            Vector2 center = m.BodyPos() + offsetPx * m.Px;
            Particle p = Fx.Spawn(arc, center, m.Px, life, FxLayer.Over, order);
            if (p == null)
            {
                return null;
            }
            p.SX = facing >= 0f ? 1f : -1f;
            p.Color(core, edge, Fx.WithA(edge, 0.6f));
            p.Cine = m.Cinematic;
            p.Step = (q, d) => q.Pos = m.BodyPos() + offsetPx * m.Px;
            return p;
        }

        /// <summary>A straight line from a to b, 'thick' pixels wide (slashes, cuts, constellation lines).</summary>
        public static Particle Line(Vector2 a, Vector2 b, float px, float life, int thick, FxLayer layer, int order, params Color[] colors)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < px * 0.5f)
            {
                return null;
            }
            if (thick >= 4 && len > thick * px * 2f)
            {
                // a thick stroke (a beam, a pillar of light): soft edges, a grain of light along it and
                // tapering tips, never a flat block of colour
                return PrimFx.Band(a, b, thick * px, life, layer, order, false, thick >= 7 ? 0 : 1, colors);
            }
            Particle p = Fx.Spawn(Art.Pixel, (a + b) * 0.5f, px, life, layer, order);
            if (p == null)
            {
                return null;
            }
            p.SX = len / px;
            p.SY = Mathf.Max(1, thick);
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Color(colors);
            return p;
        }

        /// <summary>Speed lines streaming along 'dir' around a point.</summary>
        public static void SpeedLines(Vector2 at, Vector2 dir, float px, int n, float spreadPx, float life, Color c, FxLayer layer = FxLayer.Top)
        {
            if (!Visible(at))
            {
                return;
            }
            dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.right;
            Vector2 side = new Vector2(-dir.y, dir.x);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = at + side * Random.Range(-spreadPx, spreadPx) * px - dir * Random.Range(0f, spreadPx) * px;
                Particle p = Fx.Spawn(Art.Lines[Random.Range(0, Art.Lines.Length)], p0, px, life * Random.Range(0.7f, 1.1f), layer, 1);
                if (p == null)
                {
                    break;
                }
                p.Rot = ang;
                p.Moving(dir * 40f * px);
                p.Color(Fx.WithA(c, c.a * 0.85f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>Dust kicked out in a ring along the ground (takeoffs, landings, stomps).</summary>
        public static void DustRing(Vector2 at, float px, WorldTile tile, int n, float speed, float life = 0.55f, float k = 1f)
        {
            if (!Visible(at))
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at + dir * 2f * px, px, life * Random.Range(0.8f, 1.1f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * speed * Random.Range(0.75f, 1.1f) * px * k, Random.Range(3f, 7f) * px, 4f)
                    .Sized(0.5f, 1.15f)
                    .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>A ground shockwave ring from r0 to r1 pixels.</summary>
        public static Particle Shockwave(Vector2 at, float px, float r0, float r1, float life, Color c, int thick = 2, FxLayer layer = FxLayer.Ground)
        {
            if (!Visible(at))
            {
                return null;
            }
            return Fx.Ring(at, px, life, r0, r1, thick, 1, layer, 6, Art.Ground, 2.6f, Fx.WithA(c, c.a), Fx.WithA(c, c.a * 0.6f), Fx.WithA(c, 0f));
        }

        /// <summary>A flash disk (light bursting at a point).</summary>
        public static Particle Flash(Vector2 at, float px, int rx, float life, Color c, FxLayer layer = FxLayer.Top, int order = 30)
        {
            Particle p = Fx.Spawn(Art.Disk(rx, 0.85f, 0.35f), at, px, life, layer, order);
            p?.Sized(0.8f, 1.25f).Color(c, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            return p;
        }

        /// <summary>
        /// An impact frame: flash and starburst the eye snaps to. 'size' scales it (1 = a Meteor Slam).
        /// 'black': the inverted "black flash" (dark core, tier-coloured rim) of a critical finisher.
        /// </summary>
        public static void ImpactFrame(Vector2 at, float px, float size, Palette pal, bool black, FxLayer layer = FxLayer.Top)
        {
            if (!Visible(at))
            {
                return;
            }
            Color core = black ? new Color(0.05f, 0.02f, 0.04f, 1f) : Color.white;
            Color rim = black ? pal.Edge : new Color(1f, 0.96f, 0.75f, 0.9f);
            // sorting orders that frame the impact on whatever layer it is drawn (Behind/Over sit among units)
            int o = layer == FxLayer.Top || layer == FxLayer.Cine ? 30 : layer == FxLayer.Ground ? 10 : 8;
            Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(15 * size), 0.85f, 0.35f), at, px, 0.09f, layer, o);
            flash?.Sized(0.8f, 1.25f).Color(black ? Fx.WithA(pal.Edge, 1f) : Color.white, Fx.WithA(rim, 0.8f), Fx.WithA(rim, 0f));
            int br = Mathf.RoundToInt(26f * Mathf.Max(0.3f, size));
            Particle burst = Fx.Spawn(Art.Burst(br), at, px, 0.07f + 0.02f * size, layer, o + 1);
            if (burst != null)
            {
                burst.Rot = Random.Range(0f, 360f);
                burst.Sized(1f, 1.2f).Color(core, Fx.WithA(core, 0.9f), Fx.WithA(rim, 0f));
            }
            if (black)
            {
                Particle ring = Fx.Spawn(Art.Burst(Mathf.RoundToInt(br * 1.25f)), at, px, 0.09f, layer, o - 1);
                if (ring != null)
                {
                    ring.Rot = Random.Range(0f, 360f);
                    ring.Sized(1f, 1.3f).Color(pal.Edge, Fx.WithA(pal.Edge, 0.7f), Fx.WithA(pal.Edge, 0f));
                }
            }
        }

        /// <summary>Rocks (and clods of grass) thrown out of the ground.</summary>
        public static void Debris(Vector2 at, float px, WorldTile tile, int n, float k = 1f, float z = 0f)
        {
            if (!Visible(at) || tile != null && tile.Type != null && tile.Type.liquid)
            {
                return;
            }
            Color soil = Fx.SoilColor(tile);
            Color top = Fx.TopColor(tile);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                float roll = Random.value;
                Color c = roll < 0.6f ? soil : roll < 0.85f ? Fx.Darken(soil, 0.3f) : top;
                Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at + dir * 2f * px, px, Random.Range(0.8f, 1.3f), FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(18f, 50f) * px * k, Random.Range(45f, 100f) * px * k, 0f, 320f * px * k);
                p.Z = z;   // (thrown from a height: it falls to the ground at 'at', not to its own start)
                p.Bounce = 0.3f;
                p.Spin = Random.Range(-720f, 720f);
                p.Color(c, c, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>Sparks flying out of a point.</summary>
        public static void Sparks(Vector2 at, float px, int n, float speedMin, float speedMax, Color c, FxLayer layer = FxLayer.Top)
        {
            if (!Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.14f, 0.26f), layer, 8);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(speedMin, speedMax) * px, 0f, 6f);
                p.FaceVel = true;
                p.Color(Color.white, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>
        /// A twinkle mark floating above a unit (a target mark, a ready glint), at head height (~9 px).
        /// Don't pair it with your own glyph over the same head (they stack into a dagger shape).
        /// </summary>
        public static Particle Mark(Actor v, Color c, float life, float size = 1f)
        {
            if (v == null || !v.is_visible)
            {
                return null;
            }
            float px = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
            Vector2 head = v.current_position + new Vector2(0f, v.position_height + 9f * px);
            Particle p = Fx.Glint(head, px, life, c, size);
            if (p != null)
            {
                long id = v.Sid();
                p.Step = (q, d) =>
                {
                    if (v.Sid() == id && v.isAlive())
                    {
                        q.Pos = v.current_position + new Vector2(0f, v.position_height + 9f * px);
                    }
                };
            }
            return p;
        }

        /// <summary>A coloured ring flaring at a unit's feet (tier flare when a fight starts, a promotion...).</summary>
        public static void Flare(Vector2 at, float px, Color c, float r1 = 14f, float life = 0.35f)
        {
            if (!Visible(at))
            {
                return;
            }
            Fx.Ring(at, px, life, 3f, r1, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(c, 0.95f), Fx.WithA(c, 0f));
        }
    }
}
