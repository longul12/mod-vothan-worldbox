using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// A fighter held by its duel while an exchange plays. It is Busy (the game's AI and the Brain
    /// leave it alone) and its pose is set every tick by the duel's current Exchange; the duelist
    /// itself only keeps its glued effects (hit flashes, a tier glow, its own shadow in the air) and
    /// guards against being orphaned: if the duel stops driving it, it stands up and lets go.
    /// </summary>
    internal sealed class Duelist : Move
    {
        public readonly Duel Duel;
        /// <summary>Seconds since the duel last drove it.</summary>
        internal float Idle;
        /// <summary>Vanished mid-feint: nothing can touch it.</summary>
        public bool Blur;
        /// <summary>A steady glow over the sprite (alpha 0: none), set by the exchange.</summary>
        public Color Glow = DuelFx.Clear;
        /// <summary>Its colours (tier and level).</summary>
        public Palette Pal;
        public int Lv;

        private Particle _overlay, _shadow;
        private float _flashLeft = -1f;
        private Color _flashColor = Color.white;

        public Duelist(Actor a, Duel d, int lv) : base(a)
        {
            Duel = d;
            Lv = lv;
            Level = lv;
            Pal = Palette.For(a, lv);
        }

        public override bool SuperArmor => true;

        public override bool Intangible => Blur;

        /// <summary>The exchange playing (a blade lock, a chase, a feint...): what the duelists read of each other.</summary>
        public override Form Forms => Duel != null && Duel.Current != null ? Duel.Current.Forms : Form.None;

        /// <summary>Its opponent in the duel.</summary>
        public override Actor Aim => Duel != null ? Duel.OpponentOf(A) : null;

        public bool Armed => A.hasWeapon();

        public float Fwd => Pose.FaceRight ? 1f : -1f;

        public override void Tick(float dt)
        {
            Idle += dt;
            _flashLeft -= dt;
            if (!Finished && (Idle > 0.6f || Duel == null || Duel.Over))
            {
                // the duel stopped driving it (it ended or broke): stand up and let go
                Settle();
                Finished = true;
            }
        }

        /// <summary>Back to a plain standing pose, ready to be handed back to the game.</summary>
        public void Settle()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Blur = false;
            Glow = DuelFx.Clear;
            LandOnFinish = Pose.Height <= 0.03f;
            if (LandOnFinish)
            {
                Pose.Height = 0f;
                Pose.HideShadow = false;
            }
        }

        /// <summary>Lets it go now (the bout is over).</summary>
        public void Let()
        {
            if (Finished)
            {
                return;
            }
            Settle();
            Finished = true;
        }

        /// <summary>A white (or coloured) hit flash over the whole sprite.</summary>
        public void Flash(float seconds, Color c)
        {
            _flashColor = c;
            _flashLeft = Mathf.Max(_flashLeft, seconds);
        }

        /// <summary>
        /// The game's shadow is hidden while it is up in the air (it draws its own) or vanished. Set
        /// with the pose (Exchange.Step), before the pose is applied: set in Frame it would lag a
        /// frame (two shadows on the takeoff frame, none on the landing one).
        /// </summary>
        public void Shade()
        {
            Pose.HideShadow = Pose.Height > 0.04f || Pose.Hidden;
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            float px = Px;
            // the overlay: a hit flash wins over the glow
            Color c = Glow;
            if (_flashLeft > -0.05f)
            {
                float fa = Mathf.Clamp01((_flashLeft + 0.05f) / 0.06f) * _flashColor.a;
                if (fa > c.a)
                {
                    c = Fx.WithA(_flashColor, fa);
                }
            }
            if (c.a > 0.003f && (_overlay == null || !_overlay.Alive) && Fx.HasSilhouette && Kit.Visible(Pose.Ground, 0.3f))
            {
                Sprite s = Fx.UnitSprite(A);
                _overlay = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 1, true) : null;
                if (_overlay != null)
                {
                    _overlay.Held = true;
                    _overlay.UseEuler = true;
                }
            }
            if (_overlay != null && _overlay.Alive)
            {
                Fx.Glue(_overlay, this, c);
            }

            // our own shadow while in the air: it stays on the ground and shrinks with height
            bool air = Pose.Height > 0.04f && !Pose.Hidden;
            Shade();
            if (air && _shadow == null && Kit.Visible(Pose.Ground, 0.3f))
            {
                _shadow = Fx.Spawn(Art.Disk(ShadowRx(1f), 0.42f, 0.45f), Pose.Ground, px, 1f, FxLayer.Ground, 3);
                if (_shadow != null)
                {
                    _shadow.Held = true;
                    _shadow.Snap = true;
                }
            }
            if (_shadow != null)
            {
                if (!air || !_shadow.Alive)
                {
                    _shadow.Kill();
                    _shadow = null;
                    return;
                }
                float h = Mathf.Clamp01(Pose.Height / (3.5f * K));
                Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, 0.45f, h)), 0.42f, 0.45f);
                if ((object)_shadow.R.sprite != s)
                {
                    _shadow.R.sprite = s;
                    _shadow.Frames[0] = s;
                }
                _shadow.Pos = Pose.Ground;
                _shadow.Grad = null;
                _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.34f, 0.14f, h));
            }
        }

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        private void KillHeld()
        {
            _overlay?.Kill();
            _shadow?.Kill();
            _overlay = _shadow = null;
        }

        public override void Abort()
        {
            KillHeld();
            Duel?.MoveLost(this);
        }

        public override void OnEnd(bool aborted)
        {
            KillHeld();
        }
    }

    /// <summary>
    /// A bystander stepping back out of a stare-down: the crowd parts. A little backward hop of
    /// one tile, then it turns to watch.
    /// </summary>
    internal sealed class Sidestep : Move
    {
        private const float Dur = 0.38f;
        private readonly Vector2 _from, _to;
        private readonly bool _face;
        private bool _puffed;

        private Sidestep(Actor a, Vector2 to, bool faceRight) : base(a)
        {
            _from = a.current_position;
            _to = to;
            _face = faceRight;
            Pose.FaceRight = faceRight;
        }

        public override bool SuperArmor => false;

        /// <summary>Steps 'a' one tile away from 'from' (if it is free, untiered and has room), facing 'watch'.</summary>
        public static bool Begin(Actor a, Vector2 from, Vector2 watch)
        {
            if (!Moves.CanPerform(a) || Tiers.Of(a) != Tier.None || a.hasStatusStunned())
            {
                return false;
            }
            Vector2 p = a.current_position;
            Vector2 dir = p - from;
            if (dir.sqrMagnitude < 0.0004f)
            {
                dir = Random.insideUnitCircle;
            }
            Vector2 to = DuelGeo.Slide(p, dir, 1f);
            if ((to - p).sqrMagnitude < 0.09f)
            {
                return false;
            }
            return Moves.Start(new Sidestep(a, to, watch.x > to.x));
        }

        public override void Tick(float dt)
        {
            float k = Mathf.Clamp01(T / Dur);
            if (!_puffed && k > 0.85f)
            {
                _puffed = true;
                DuelFx.Puff(_to, Px, Combat.TileAt(_to), 2, 8f);
            }
            Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
            float hop = Mathf.Sin(Mathf.PI * k);
            Pose.Height = 0.09f * K * hop;
            Pose.Rot = -8f * hop;
            Pose.SX = 1f - 0.04f * hop;
            Pose.SY = 1f + 0.05f * hop;
            Pose.FaceRight = _face;
            if (k >= 1f)
            {
                Pose.Height = 0f;
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Finished = true;
            }
        }
    }

    /// <summary>
    /// A duelist just let go in a fresh crater that burns (or wherever it may not stand): one quick
    /// hop out to the nearest safe ground, still facing its opponent, a puff where it lands.
    /// </summary>
    internal sealed class Vault : Move
    {
        private const float Dur = 0.22f;
        private readonly Vector2 _from, _to;
        private readonly bool _face;
        private bool _puffed;

        private Vault(Actor a, Vector2 from, Vector2 to, bool faceRight) : base(a)
        {
            _from = from;
            _to = to;
            _face = faceRight;
            Pose.Ground = from;
            Pose.FaceRight = faceRight;
        }

        /// <summary>
        /// Hops 'a' off unsafe ground (if it stands on it and safe ground is near), facing 'look'.
        /// Burning grass is unsafe for one that can burn (below Master: no fire immunity).
        /// </summary>
        public static bool Begin(Actor a, Vector2 look)
        {
            if (a == null || !a.isAlive() || !Moves.CanPerform(a))
            {
                return false;
            }
            Vector2 p = a.current_position;
            bool burns = !Combat.Fireproof(a);
            if (DuelGeo.Footed(p, burns) || !DuelGeo.FindFooting(p, 2.5f, burns, out Vector2 to) || (to - p).sqrMagnitude < 0.04f)
            {
                return false;
            }
            bool face = Mathf.Abs(look.x - to.x) > 0.03f ? look.x > to.x : a.flip;
            return Moves.Start(new Vault(a, p, to, face));
        }

        public override void Tick(float dt)
        {
            float k = Mathf.Clamp01(T / Dur);
            float hop = Mathf.Sin(Mathf.PI * k);
            Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
            Pose.Height = 0.22f * K * hop;
            Pose.Rot = -10f * hop;
            Pose.SX = 1f - 0.05f * hop;
            Pose.SY = 1f + 0.07f * hop;
            Pose.FaceRight = _face;
            if (!_puffed && k > 0.9f)
            {
                _puffed = true;
                DuelFx.Puff(_to, Px, Combat.TileAt(_to), 2, 10f);
            }
            if (k >= 1f)
            {
                Pose.Height = 0f;
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Finished = true;
            }
        }
    }

    /// <summary>Where fighters may stand, slide and land.</summary>
    internal static class DuelGeo
    {
        private static readonly float[] Tries = { 1f, 0.65f, 0.35f };

        /// <summary>Well inside the map ('margin' tiles from every edge).</summary>
        public static bool Inside(Vector2 p, float margin)
        {
            return p.x >= margin && p.y >= margin && p.x <= MapBox.width - 1 - margin && p.y <= MapBox.height - 1 - margin;
        }

        /// <summary>Safe ground with room around it: at least three of its four neighbours a tile away are safe too.</summary>
        public static bool Roomy(Vector2 p)
        {
            if (!Combat.SafeSpot(p))
            {
                return false;
            }
            int n = 0;
            if (Combat.SafeSpot(p + Vector2.right))
            {
                n++;
            }
            if (Combat.SafeSpot(p + Vector2.left))
            {
                n++;
            }
            if (Combat.SafeSpot(p + Vector2.up))
            {
                n++;
            }
            if (Combat.SafeSpot(p + Vector2.down))
            {
                n++;
            }
            return n >= 3;
        }

        /// <summary>
        /// A place 'min'-'max' tiles from 'from' to land a body thrown across the world: roomy safe
        /// ground well inside the map. Tried along 'pref' first (from the far end in), then fanning
        /// out to either side up to 'fan' radians, then the other way round. False: nowhere fits.
        /// </summary>
        public static bool Far(Vector2 from, Vector2 pref, float min, float max, float fan, out Vector2 to)
        {
            if (pref.sqrMagnitude < 1e-6f)
            {
                pref = Vector2.right;
            }
            float a0 = Mathf.Atan2(pref.y, pref.x);
            for (int back = 0; back < 2; back++)
            {
                for (int i = 0; i < 7; i++)
                {
                    int j = (i + 1) / 2 * ((i & 1) == 1 ? 1 : -1);   // 0, 1, -1, 2, -2, 3, -3
                    float a = a0 + (back == 1 ? Mathf.PI : 0f) + j * fan / 3f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    for (int s = 0; s < 4; s++)
                    {
                        Vector2 p = from + dir * Mathf.Lerp(max, min, s / 3f);
                        if (Inside(p, 3f) && Roomy(p))
                        {
                            to = p;
                            return true;
                        }
                    }
                }
            }
            to = from;
            return false;
        }

        /// <summary>
        /// What lies along a straight line from a to b: tiles of mountain or rock, trees and buildings
        /// (anything standing), the longest run of water, and whether it crosses lava.
        /// </summary>
        public static void Scan(Vector2 a, Vector2 b, out int rock, out int wood, out int wet, out bool lava)
        {
            rock = wood = wet = 0;
            lava = false;
            int run = 0;
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.8f));
            WorldTile last = null;
            for (int i = 1; i <= n; i++)
            {
                WorldTile t = Combat.TileAt(Vector2.Lerp(a, b, i / (float)n));
                if (t == null || t == last || t.Type == null)
                {
                    continue;
                }
                last = t;
                if (t.Type.lava)
                {
                    lava = true;
                }
                if (t.Type.liquid)
                {
                    run++;
                    wet = Mathf.Max(wet, run);
                }
                else
                {
                    run = 0;
                }
                if (t.Type.mountains || t.Type.rocks)
                {
                    rock++;
                }
                if (Standing(t))
                {
                    wood++;
                }
            }
        }

        /// <summary>Something standing on the tile a body can be smashed through (a tree, a rock, a house).</summary>
        public static bool Standing(WorldTile t)
        {
            return t != null && t.hasBuilding() && t.building != null && t.building.isAlive();
        }

        /// <summary>
        /// A God driven through a mountain carves a pass: mountain tiles within 'r' tiles of 'at'
        /// drop a step (a summit to a mountain, a mountain to hills). Only with full environment
        /// damage, at most 'budget' tiles; returns how many gave way.
        /// </summary>
        public static int Carve(Vector2 at, float r, int budget)
        {
            if (Settings.Destruction < 2 || budget <= 0 || World.world == null)
            {
                return 0;
            }
            WorldTile c = Combat.TileAt(at);
            if (c == null)
            {
                return 0;
            }
            int n = 0;
            int ri = Mathf.CeilToInt(r);
            float r2 = r * r + 0.3f;
            try
            {
                for (int dx = -ri; dx <= ri && n < budget; dx++)
                {
                    for (int dy = -ri; dy <= ri && n < budget; dy++)
                    {
                        if (dx * dx + dy * dy > r2)
                        {
                            continue;
                        }
                        int x = c.x + dx, y = c.y + dy;
                        if (x < 1 || y < 1 || x >= MapBox.width - 1 || y >= MapBox.height - 1)
                        {
                            continue;
                        }
                        WorldTile t = World.world.GetTileSimple(x, y);
                        if (t == null || t.Type == null || !t.Type.mountains)
                        {
                            continue;
                        }
                        MapAction.decreaseTile(t, true, "sakuga_dig");   // (the game's "flash" blinks every tile white)
                        n++;
                    }
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("duel carve", e);
            }
            return n;
        }

        /// <summary>Ground a fighter may stand on: safe (Combat.SafeSpot), and not burning under one that can burn.</summary>
        public static bool Footed(Vector2 p, bool burns)
        {
            if (!Combat.SafeSpot(p))
            {
                return false;
            }
            if (!burns)
            {
                return true;
            }
            WorldTile t = Combat.TileAt(p);
            return t != null && !t.isOnFire();
        }

        /// <summary>The nearest ground within 'radius' tiles a fighter may stand on (Footed; Combat.FindLanding's spiral), if any.</summary>
        public static bool FindFooting(Vector2 want, float radius, bool burns, out Vector2 spot)
        {
            if (!burns)
            {
                return Combat.FindLanding(want, radius, out spot);
            }
            want = Combat.Clamp(want);
            if (Footed(want, true))
            {
                spot = want;
                return true;
            }
            int r = Mathf.CeilToInt(radius);
            for (int ring = 1; ring <= r; ring++)
            {
                for (int i = 0; i < ring * 8; i++)
                {
                    float a = i / (ring * 8f) * Mathf.PI * 2f;
                    Vector2 p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                    if (Footed(p, true))
                    {
                        spot = p;
                        return true;
                    }
                }
            }
            spot = want;
            return false;
        }

        /// <summary>Where a fighter left in a fresh crater gets up: right there if the ground holds, else the nearest safe ground (it climbs out).</summary>
        public static Vector2 Footing(Vector2 at)
        {
            if (Combat.SafeSpot(at))
            {
                return at;
            }
            return Combat.FindLanding(at, 2.5f, out Vector2 s) ? s : at;
        }

        /// <summary>Slides along the ground up to 'dist' tiles (less if the ground ends): never onto water, lava or a wall.</summary>
        public static Vector2 Slide(Vector2 from, Vector2 dir, float dist)
        {
            if (dist <= 0.01f || dir.sqrMagnitude < 1e-6f)
            {
                return from;
            }
            dir.Normalize();
            foreach (float f in Tries)
            {
                Vector2 to = Combat.Clamp(from + dir * dist * f);
                if (Combat.SafeSpot(to) && Combat.ClearPath(from, to))
                {
                    return to;
                }
            }
            return from;
        }

        /// <summary>Flies up to 'dist' tiles (over anything) and lands on safe ground, or stays.</summary>
        public static Vector2 Land(Vector2 from, Vector2 dir, float dist)
        {
            if (dist <= 0.01f || dir.sqrMagnitude < 1e-6f)
            {
                return from;
            }
            dir.Normalize();
            foreach (float f in Tries)
            {
                Vector2 to = Combat.Clamp(from + dir * dist * f);
                if (Combat.SafeSpot(to))
                {
                    return to;
                }
            }
            if (Combat.FindLanding(from + dir * dist, 1f, out Vector2 s) && (s - from).sqrMagnitude <= (dist + 1.2f) * (dist + 1.2f))
            {
                return s;
            }
            return Combat.SafeSpot(from) ? from : Combat.Clamp(from);
        }

        /// <summary>
        /// Face-to-face spots 'gap' tiles apart for two fighters standing at a and b: on a
        /// horizontal line through their middle if the ground allows (fights read best side-on),
        /// otherwise along the line between them, otherwise shifted toward one of them. Both
        /// spots are safe and reachable by a straight dash. False: nowhere fits.
        /// </summary>
        public static bool Stance(Vector2 a, Vector2 b, float gap, out Vector2 sa, out Vector2 sb)
        {
            Vector2 mid = (a + b) * 0.5f;
            Vector2 hor = new Vector2(a.x <= b.x ? 1f : -1f, 0f);
            Vector2 real = b - a;
            real = real.sqrMagnitude > 1e-4f ? real.normalized : hor;
            for (int i = 0; i < 4; i++)
            {
                Vector2 dir = i == 1 ? real : hor;
                Vector2 m = i == 2 ? a + dir * gap * 0.5f : i == 3 ? b - dir * gap * 0.5f : mid;
                sa = Combat.Clamp(m - dir * gap * 0.5f);
                sb = Combat.Clamp(m + dir * gap * 0.5f);
                if (Combat.SafeSpot(sa) && Combat.SafeSpot(sb) && Combat.ClearPath(a, sa) && Combat.ClearPath(b, sb))
                {
                    return true;
                }
            }
            sa = a;
            sb = b;
            return false;
        }
    }
}
