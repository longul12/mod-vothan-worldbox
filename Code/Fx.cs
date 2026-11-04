using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>Where an effect is drawn relative to units.</summary>
    internal enum FxLayer
    {
        /// <summary>On the ground, under units (decals, shadows, ground rings).</summary>
        Ground,
        /// <summary>Among units, behind them (trails, afterimages).</summary>
        Behind,
        /// <summary>Among units, in front of them (a unit's glow).</summary>
        Over,
        /// <summary>Over everything (flashes, sparks, debris, front dust).</summary>
        Top,
        /// <summary>Above a cinematic's dark veil (what stays lit while the world is dimmed).</summary>
        Cine
    }

    /// <summary>
    /// One pooled sprite with simple physics. Positions are in world units, on the
    /// ground plane, with Z the height above it (drawn as +y), so things can fly
    /// up and land where they started.
    /// </summary>
    internal sealed class Particle
    {
        internal GameObject Go;
        internal Transform Tr;
        internal SpriteRenderer R;

        public Sprite[] Frames;
        /// <summary>Frames per second; 0 spreads the frames over the particle's life.</summary>
        public float Fps;
        public bool Loop;

        public Vector2 Pos;
        public float Z;
        public Vector2 Vel;
        public float VZ;
        public float Gravity;
        /// <summary>Velocity lost per second (exponential).</summary>
        public float Drag;
        /// <summary>Bounciness when it hits the ground; negative = no ground (stays where it is).</summary>
        public float Bounce = -1f;
        public int Bounces;

        public float Age, Life, Delay;
        /// <summary>Colour over life (evenly spaced stops, alpha included).</summary>
        public Color[] Grad;
        public float Size0 = 1f, Size1 = 1f;
        /// <summary>Extra non-uniform scale (stretching along the sprite's own axes).</summary>
        public float SX = 1f, SY = 1f;
        public float Rot, Spin;
        /// <summary>Turn the sprite's +y axis along its motion.</summary>
        public bool FaceVel;
        /// <summary>Full euler override (afterimages carry the unit's flip).</summary>
        public bool UseEuler;
        public Vector3 Euler;
        /// <summary>World units per texel.</summary>
        public float Px = 0.1f;
        /// <summary>Lives until Kill() (things a move holds and steers).</summary>
        public bool Held;
        /// <summary>Snap to the texel grid: crisp, pixel-aligned ground art.</summary>
        public bool Snap;
        /// <summary>Keeps moving while a cinematic freezes the world.</summary>
        public bool Cine;
        public Action<Particle, float> Step;
        internal bool Alive;
        /// <summary>Bumped each time it is recycled: a reference kept with its Gen knows when the particle became another effect.</summary>
        internal int Gen;

        /// <summary>The frames of a single-sprite particle, and its colour stops: its own, reused (no garbage per effect).</summary>
        internal readonly Sprite[] One = new Sprite[1];
        private Color[] _g1, _g2, _g3, _g4, _g5;

        public float K => Life > 0f ? Mathf.Clamp01(Age / Life) : 1f;

        public void Kill()
        {
            Held = false;
            Life = 0f;
        }

        public Particle Color(params Color[] stops)
        {
            Grad = stops;
            return this;
        }

        public Particle Color(Color a)
        {
            Color[] g = _g1 ??= new Color[1];
            g[0] = a;
            Grad = g;
            return this;
        }

        public Particle Color(Color a, Color b)
        {
            Color[] g = _g2 ??= new Color[2];
            g[0] = a;
            g[1] = b;
            Grad = g;
            return this;
        }

        public Particle Color(Color a, Color b, Color c)
        {
            Color[] g = _g3 ??= new Color[3];
            g[0] = a;
            g[1] = b;
            g[2] = c;
            Grad = g;
            return this;
        }

        public Particle Color(Color a, Color b, Color c, Color d)
        {
            Color[] g = _g4 ??= new Color[4];
            g[0] = a;
            g[1] = b;
            g[2] = c;
            g[3] = d;
            Grad = g;
            return this;
        }

        public Particle Color(Color a, Color b, Color c, Color d, Color e)
        {
            Color[] g = _g5 ??= new Color[5];
            g[0] = a;
            g[1] = b;
            g[2] = c;
            g[3] = d;
            g[4] = e;
            Grad = g;
            return this;
        }

        public Particle Sized(float from, float to)
        {
            Size0 = from;
            Size1 = to;
            return this;
        }

        public Particle Moving(Vector2 vel, float vz = 0f, float drag = 0f, float gravity = 0f)
        {
            Vel = vel;
            VZ = vz;
            Drag = drag;
            Gravity = gravity;
            return this;
        }
    }

    internal static class Fx
    {
        private const int MaxLive = 2500;

        private static readonly List<Particle> _live = new List<Particle>();
        private static readonly Stack<Particle> _free = new Stack<Particle>();
        private static Transform _root;
        private static Material _normal;
        private static Material _silhouette;
        private static int _layerGround, _layerUnits, _layerTop;

        /// <summary>The flat-colour material (a sprite drawn in its renderer's colour), for renderers of other systems.</summary>
        internal static Material SilhouetteMaterial
        {
            get
            {
                EnsureInit();
                return _silhouette;
            }
        }

        /// <summary>The sorting layer of ground effects: under every unit, over the ground (craters, scars).</summary>
        internal static int GroundLayer
        {
            get
            {
                EnsureInit();
                return _layerGround;
            }
        }

        /// <summary>The sorting layer units are drawn on.</summary>
        internal static int UnitsLayer
        {
            get
            {
                EnsureInit();
                return _layerUnits;
            }
        }

        /// <summary>
        /// Whether whole-sprite silhouettes are available (GUI/Text Shader). Initializes Fx when asked: it
        /// was only known after the first spawn, so the session's very first glow or flash was skipped.
        /// </summary>
        public static bool HasSilhouette
        {
            get
            {
                EnsureInit();
                return _hasSilhouette;
            }
        }

        private static bool _hasSilhouette;

        public static int Live => _live.Count;

        private static void EnsureInit()
        {
            if (_root != null)
            {
                return;
            }
            GameObject root = new GameObject("SakugaFx");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _root = root.transform;
            Shader sprite = Shader.Find("Sprites/Default");
            _normal = new Material(sprite);
            Shader text = Shader.Find("GUI/Text Shader");
            _hasSilhouette = text != null;
            _silhouette = _hasSilhouette ? new Material(text) : _normal;
            _layerGround = Layer("EffectsBack");
            _layerUnits = Layer("Objects");
            _layerTop = Layer("EffectsTop");
        }

        private static int Layer(string name)
        {
            int id = SortingLayer.NameToID(name);
            return SortingLayer.IsValid(id) ? id : 0;
        }

        // ----------------------------------------------------------- spawning --

        /// <summary>A new effect sprite, or null when the effect budget is spent (callers just skip it).</summary>
        public static Particle Spawn(Sprite[] frames, Vector2 pos, float px, float life, FxLayer layer, int order = 0, bool silhouette = false)
        {
            return frames == null || frames.Length == 0 ? null : Take(frames, null, pos, px, life, layer, order, silhouette);
        }

        public static Particle Spawn(Sprite sprite, Vector2 pos, float px, float life, FxLayer layer, int order = 0, bool silhouette = false)
        {
            return sprite == null ? null : Take(null, sprite, pos, px, life, layer, order, silhouette);
        }

        /// <summary>'frames', or (null) the one 'sprite' in the particle's own frame array.</summary>
        private static Particle Take(Sprite[] frames, Sprite sprite, Vector2 pos, float px, float life, FxLayer layer, int order, bool silhouette)
        {
            EnsureInit();
            if (_live.Count >= MaxLive)
            {
                return null;
            }
            Particle p = _free.Count > 0 ? _free.Pop() : Create();
            if (frames == null)
            {
                p.One[0] = sprite;
                frames = p.One;
            }
            p.Frames = frames;
            p.Fps = 0f;
            p.Loop = false;
            p.Pos = pos;
            p.Z = 0f;
            p.Vel = Vector2.zero;
            p.VZ = 0f;
            p.Gravity = 0f;
            p.Drag = 0f;
            p.Bounce = -1f;
            p.Bounces = 0;
            p.Age = 0f;
            p.Life = life;
            p.Delay = 0f;
            p.Grad = null;
            p.Size0 = p.Size1 = 1f;
            p.SX = p.SY = 1f;
            p.Rot = 0f;
            p.Spin = 0f;
            p.FaceVel = false;
            p.UseEuler = false;
            p.Px = px;
            p.Held = false;
            p.Snap = false;
            p.Cine = layer == FxLayer.Cine;
            p.Step = null;
            p.Alive = true;
            p.R.sharedMaterial = silhouette && HasSilhouette ? _silhouette : _normal;
            switch (layer)
            {
                case FxLayer.Ground:
                    p.R.sortingLayerID = _layerGround;
                    p.R.sortingOrder = order;
                    break;
                case FxLayer.Behind:
                    p.R.sortingLayerID = _layerUnits;
                    p.R.sortingOrder = -20 + order;
                    break;
                case FxLayer.Over:
                    p.R.sortingLayerID = _layerUnits;
                    p.R.sortingOrder = 20 + order;
                    break;
                case FxLayer.Top:
                    p.R.sortingLayerID = _layerTop;
                    p.R.sortingOrder = 50 + order;
                    break;
                default:
                    p.R.sortingLayerID = _layerTop;
                    p.R.sortingOrder = 1000 + order;
                    break;
            }
            p.R.sprite = frames[0];
            p.R.color = UnityEngine.Color.white;
            p.R.enabled = false;   // shown on its first update, already in place
            p.Go.SetActive(true);
            _live.Add(p);
            return p;
        }

        private static Particle Create()
        {
            GameObject go = new GameObject("sakuga_fx");
            go.transform.SetParent(_root, false);
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            return new Particle { Go = go, Tr = go.transform, R = r };
        }

        // ------------------------------------------------------------- update --

        public static void Update(float dt, float cineDt)
        {
            if (_root != null)
            {
                bool show = MapBox.isRenderGameplay();
                if (_root.gameObject.activeSelf != show)
                {
                    _root.gameObject.SetActive(show);
                }
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Particle p = _live[i];
                bool alive;
                try
                {
                    alive = Advance(p, p.Cine ? cineDt : dt);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] fx: " + e);
                    alive = false;
                }
                if (!alive)
                {
                    Recycle(p);
                    _live[i] = _live[_live.Count - 1];
                    _live.RemoveAt(_live.Count - 1);
                }
            }
        }

        private static bool Advance(Particle p, float dt)
        {
            if (p.Delay > 0f)
            {
                p.Delay -= dt;
                p.R.enabled = false;
                return true;
            }
            p.Age += dt;
            if (!p.Held && p.Age >= p.Life)
            {
                return false;
            }
            if (dt > 0f)
            {
                if (p.Drag > 0f)
                {
                    float d = Mathf.Exp(-p.Drag * dt);
                    p.Vel *= d;
                    if (p.Bounce < 0f)
                    {
                        p.VZ *= d;
                    }
                }
                p.Pos += p.Vel * dt;
                p.VZ -= p.Gravity * dt;
                p.Z += p.VZ * dt;
                if (p.Bounce >= 0f && p.Z < 0f)
                {
                    p.Z = 0f;
                    if (p.VZ < 0f)
                    {
                        p.VZ = -p.VZ * p.Bounce;
                        p.Vel *= 0.55f;
                        p.Spin *= 0.5f;
                        p.Bounces++;
                        if (p.VZ < 0.4f * p.Px * 10f)
                        {
                            p.VZ = 0f;
                            p.Vel *= 0.6f;
                            p.Spin = 0f;
                        }
                    }
                }
                p.Rot += p.Spin * dt;
            }
            p.Step?.Invoke(p, dt);
            if (!p.Alive)
            {
                return false;
            }
            Render(p);
            return true;
        }

        private static void Render(Particle p)
        {
            float k = p.K;
            Sprite[] frames = p.Frames;
            int n = frames.Length;
            if (n > 1)
            {
                int f = p.Fps > 0f ? (int)(p.Age * p.Fps) : (int)(k * n);
                f = p.Loop ? f % n : Mathf.Min(f, n - 1);
                if ((object)p.R.sprite != frames[f])
                {
                    p.R.sprite = frames[f];
                }
            }
            if (p.Grad != null && p.Grad.Length > 0)
            {
                p.R.color = Sample(p.Grad, k);
            }
            float size = Mathf.LerpUnclamped(p.Size0, p.Size1, 1f - (1f - k) * (1f - k)) * p.Px;
            float x = p.Pos.x, y = p.Pos.y + p.Z;
            if (p.Snap)
            {
                x = Mathf.Round(x / p.Px) * p.Px;
                y = Mathf.Round(y / p.Px) * p.Px;
            }
            p.Tr.position = new Vector3(x, y, -0.01f);
            p.Tr.localScale = new Vector3(size * p.SX, size * p.SY, 1f);
            if (p.UseEuler)
            {
                p.Tr.eulerAngles = p.Euler;
            }
            else if (p.FaceVel)
            {
                Vector2 v = new Vector2(p.Vel.x, p.Vel.y + p.VZ);
                if (v.sqrMagnitude > 1e-6f)
                {
                    p.Tr.eulerAngles = new Vector3(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
                }
            }
            else
            {
                p.Tr.eulerAngles = new Vector3(0f, 0f, p.Rot);
            }
            p.R.enabled = true;
        }

        public static Color Sample(Color[] stops, float k)
        {
            if (stops.Length == 1)
            {
                return stops[0];
            }
            float f = Mathf.Clamp01(k) * (stops.Length - 1);
            int i = Mathf.Min((int)f, stops.Length - 2);
            return UnityEngine.Color.LerpUnclamped(stops[i], stops[i + 1], f - i);
        }

        private static void Recycle(Particle p)
        {
            p.Alive = false;
            p.Gen++;
            p.Step = null;
            p.Frames = null;
            p.One[0] = null;
            if (p.R != null)
            {
                p.R.enabled = false;
                p.R.sprite = null;
            }
            if (p.Go != null)
            {
                p.Go.SetActive(false);
                _free.Push(p);
            }
        }

        public static void Clear()
        {
            foreach (Particle p in _live)
            {
                Recycle(p);
            }
            _live.Clear();
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>Is a world point on screen (with a margin in screen fractions)? Effects off screen are skipped.</summary>
        public static bool OnScreen(Vector2 p, float margin = 0.25f)
        {
            if (!MapBox.isRenderGameplay())
            {
                return false;
            }
            Camera cam = World.world.camera;
            if (cam == null)
            {
                return false;
            }
            Vector3 v = cam.WorldToViewportPoint(new Vector3(p.x, p.y, 0f));
            return v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }

        /// <summary>How much detail to draw: 1 up close, less when zoomed out (where it would be sub-pixel anyway).</summary>
        public static float Detail()
        {
            Camera cam = World.world.camera;
            if (cam == null)
            {
                return 1f;
            }
            float size = cam.orthographicSize;
            return size <= 70f ? 1f : size <= 160f ? 0.55f : 0.3f;
        }

        public static int Count(int n)
        {
            return Mathf.Max(1, Mathf.RoundToInt(n * Detail()));
        }

        /// <summary>The colour of a tile's ground (soil, sand, snow...), not its grass.</summary>
        public static Color SoilColor(WorldTile t)
        {
            if (t == null || t.main_type == null)
            {
                return new Color(0.55f, 0.45f, 0.33f);
            }
            Color c = t.main_type.color;
            c.a = 1f;
            if (t.main_type.liquid)
            {
                return UnityEngine.Color.Lerp(c, UnityEngine.Color.white, 0.55f);   // spray
            }
            return c;
        }

        /// <summary>The colour of what grows on a tile (grass), or its soil.</summary>
        public static Color TopColor(WorldTile t)
        {
            if (t != null && t.top_type != null)
            {
                Color c = t.top_type.color;
                c.a = 1f;
                return c;
            }
            return SoilColor(t);
        }

        public static Color WithA(Color c, float a)
        {
            c.a = a;
            return c;
        }

        public static Color Lighten(Color c, float k)
        {
            Color r = UnityEngine.Color.Lerp(c, UnityEngine.Color.white, k);
            r.a = c.a;
            return r;
        }

        public static Color Darken(Color c, float k)
        {
            Color r = UnityEngine.Color.Lerp(c, UnityEngine.Color.black, k);
            r.a = c.a;
            return r;
        }

        /// <summary>The sprite a unit is drawn with right now (for afterimages and glows).</summary>
        public static Sprite UnitSprite(Actor a)
        {
            try
            {
                if (a.asset.ignore_generic_render || a.asset.has_avatar_prefab)
                {
                    return null;
                }
                Sprite s = a.hasColoredSprite() ? a.getLastColoredSprite() : null;
                if (s == null)
                {
                    s = a.calculateMainSprite();
                }
                return s;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A copy of the unit as it is drawn this frame (same sprite, flip, rotation, squash),
        /// as a flat-colour silhouette that fades: an afterimage.
        /// </summary>
        public static Particle Afterimage(Move m, float life, params Color[] colors)
        {
            Actor a = m.A;
            Sprite s = UnitSprite(a);
            if (s == null)
            {
                return null;
            }
            Particle p = Spawn(s, m.DrawPos(), 1f, life, FxLayer.Behind, 0, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = UnitEuler(m);
            p.SX = a.actor_scale * m.Pose.SX;
            p.SY = a.actor_scale * m.Pose.SY;
            p.Color(colors);
            return p;
        }

        public static Vector3 UnitEuler(Move m)
        {
            Actor a = m.A;
            float y = a.asset.can_flip && m.Pose.FaceRight ? 180f : 0f;
            return new Vector3(0f, y, m.Pose.Rot);
        }

        /// <summary>Keeps a held silhouette glued to its unit (call from Move.Frame).</summary>
        public static void Glue(Particle p, Move m, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Sprite s = UnitSprite(m.A);
            if (s != null && (object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = m.DrawPos();
            p.Z = 0f;
            p.Euler = UnitEuler(m);
            p.SX = m.A.actor_scale * m.Pose.SX;
            p.SY = m.A.actor_scale * m.Pose.SY;
            p.Grad = null;
            p.R.color = c;
            p.Tr.position = new Vector3(p.Pos.x, p.Pos.y, -0.02f);
            p.Tr.localScale = new Vector3(p.SX, p.SY, 1f);
            p.Tr.eulerAngles = p.Euler;
            p.R.enabled = c.a > 0.003f && !m.Pose.Hidden && (m.A.is_visible || Fx.OnScreen(m.Pose.Ground, 0.1f));
        }

        // ------------------------------------------------------ shared bits --

        /// <summary>A puff of dust where a thrown unit hits the ground.</summary>
        public static void LandingPuff(Vector2 at, float px, WorldTile tile)
        {
            if (!OnScreen(at))
            {
                return;
            }
            Color dust = Lighten(SoilColor(tile), 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float side = i == 0 ? -1f : i == 1 ? 1f : 0f;
                Particle p = Spawn(Art.Dust[i], at + new Vector2(side * 2f * px, 0f), px, i == 2 ? 0.3f : 0.45f, FxLayer.Behind);
                if (p == null)
                {
                    return;
                }
                p.Moving(new Vector2(side * 14f * px, 2f * px), i == 2 ? 6f * px : 3f * px, 5f);
                p.Sized(0.6f, 0.9f);
                p.Color(WithA(dust, 0.9f), WithA(dust, 0.5f));
            }
        }

        /// <summary>A copy of the unit as drawn, nudged and turned a little: the blur of a flurry of blows.</summary>
        public static Particle Ghost(Move m, Vector2 offset, float rotOffset, float life, params Color[] colors)
        {
            Particle p = Afterimage(m, life, colors);
            if (p != null)
            {
                // the unit turns around its body centre, not its feet: so does its ghost
                Pose q = m.Pose;
                q.Rot += rotOffset;
                bool flip = m.A.asset.can_flip;
                p.Pos += offset + Moves.PivotOffset(q, m.A.actor_scale, flip) - Moves.PivotOffset(m.Pose, m.A.actor_scale, flip);
                p.Euler.z += rotOffset;
            }
            return p;
        }

        /// <summary>
        /// A punch or kick landing: an impact star (size 0 jab .. 2 heavy) and sparks flying
        /// away from the blow.
        /// </summary>
        public static void HitSpark(Vector2 at, float px, int size, Vector2 away, int sparks)
        {
            if (!OnScreen(at))
            {
                return;
            }
            size = Mathf.Clamp(size, 0, Art.HitStars.Length - 1);
            Particle star = Spawn(Art.HitStars[size], at, px, 0.06f + 0.025f * size, FxLayer.Top, 20);
            if (star != null)
            {
                star.Rot = UnityEngine.Random.Range(0f, 90f);
                star.Sized(1.15f, 0.7f).Color(Color.white, new Color(1f, 0.95f, 0.6f, 1f), new Color(1f, 0.95f, 0.6f, 0f));
            }
            float baseAng = Mathf.Atan2(away.y, away.x);
            for (int i = 0; i < sparks; i++)
            {
                float a = baseAng + UnityEngine.Random.Range(-1.1f, 1.1f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Spawn(Art.Sparks[UnityEngine.Random.Range(0, Art.Sparks.Length)], at, px, UnityEngine.Random.Range(0.1f, 0.17f), FxLayer.Top, 19);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * UnityEngine.Random.Range(60f, 120f) * px, 0f, 9f);
                p.FaceVel = true;
                p.Color(Color.white, new Color(1f, 0.9f, 0.45f, 1f), new Color(1f, 0.55f, 0.15f, 0f));
            }
        }

        /// <summary>
        /// Manga focus lines: short streaks all around a point, pointing at it, flickering.
        /// The eye goes straight to the hit.
        /// </summary>
        public static void FocusLines(Vector2 center, float px, int count, float r0, float r1, float life)
        {
            if (!OnScreen(center))
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Spawn(Art.Lines[UnityEngine.Random.Range(1, 3)], center + dir * UnityEngine.Random.Range(r0, r1) * px, px, life, FxLayer.Top, 2);
                if (p == null)
                {
                    break;
                }
                p.Rot = a * Mathf.Rad2Deg - 90f;
                p.Moving(-dir * 45f * px);
                p.Color(new Color(1f, 1f, 1f, 0.6f), new Color(1f, 1f, 1f, 0f));
            }
        }

        /// <summary>A twinkle star: the anime "ting!".</summary>
        public static Particle Glint(Vector2 at, float px, float life, Color c, float size = 1f)
        {
            Particle p = Spawn(Art.Glint, at, px, life, FxLayer.Top, 10);
            if (p == null)
            {
                return null;
            }
            p.Sized(size, size);
            p.Color(c, c, WithA(c, 0.6f));
            return p;
        }

        /// <summary>
        /// A ground ring that grows from r0 to r1 texels: redrawn at every radius,
        /// so its line stays one crisp pixel (or 'thick' pixels) wide.
        /// </summary>
        public static Particle Ring(Vector2 at, float px, float life, float r0, float r1, int thick0, int thick1, FxLayer layer, int order, float ratio, float ease, params Color[] colors)
        {
            Particle p = Spawn(Art.Ring(Mathf.RoundToInt(r0), thick0, ratio), at, px, life, layer, order);
            if (p == null)
            {
                return null;
            }
            p.Snap = true;
            p.Color(colors);
            p.Step = (q, dt) =>
            {
                float k = q.K;
                float e = 1f - Mathf.Pow(1f - k, ease);
                int r = Mathf.RoundToInt(Mathf.Lerp(r0, r1, e));
                int t = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(thick0, thick1, k)));
                Sprite s = Art.Ring(r, t, ratio);
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
            };
            return p;
        }
    }
}
