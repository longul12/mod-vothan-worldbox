using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THE AWAKENING: a unit given the Primordial trait is not a God ascending; the first of all things
    /// wakes, and the whole world knows it. (Ceremonies hands a new Primordial here instead of playing
    /// the God's ascension.) The player is usually in the trait editor: it waits for the window to close,
    /// then the camera goes to the unit, the world slows to normal speed, and:
    ///
    ///   0.0        THE WORLD STOPS: frozen, the sky goes violet-black, the whole map quakes;
    ///              "THE WORLD TREMBLES"
    ///   0.0-2.6    everything on screen is pulled into it (motes of every colour from the edges of the
    ///              view); it rises inside a growing sphere of the void; cracks of light race out across
    ///              the ground from its feet; rocks float up; three heartbeats shake the screen
    ///   2.6-3.0    the sphere swallows it and collapses to a point; an EYE opens in the sky
    ///   3.0        THE AWAKENING: a white flash over the whole view, a pillar of prismatic light
    ///              from the ground through the sky, seven rings of every colour sweeping out past the
    ///              edges of the screen, the ground under it blasted; "A PRIMORDIAL HAS AWAKENED"
    ///   3.0-6.6    it hangs in the pillar: a halo of light orbiting it, rays turning round it out to the
    ///              edges of the view, lightning all over the sky, the land rising toward it; then its
    ///              name: "{NAME}, THE FIRST"
    ///   6.6        the world moves again: everything near it is thrown down, and every living thing in
    ///              the world turns to look ("!"); a message for the whole world and a world-log entry
    ///   6.6-9.0    the veil lifts; it comes down; a prismatic ring where it lands
    /// Off screen, above x5 speed or with light cinematics the same plays without the freeze and veil.
    /// </summary>
    internal static class Awakenings
    {
        private sealed class Pending
        {
            public Actor A;
            public long Id;
            public float Waited;
            public float PannedAt = -1f;
        }

        private static readonly List<Pending> _pending = new List<Pending>();
        private static readonly Dictionary<long, float> _recent = new Dictionary<long, float>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += dt => Tick();
            Hooks.WorldCleared += () =>
            {
                _pending.Clear();
                _recent.Clear();
                Awakening.Live = 0;   // (nothing awakens in a fresh world)
            };
        }

        /// <summary>Is this unit's awakening still to play (or playing)? It does nothing else until then.</summary>
        public static bool Waiting(Actor a)
        {
            if (a == null)
            {
                return false;
            }
            if (Moves.TryGet(a, out Move m) && m is Awakening)
            {
                return true;
            }
            long id = a.Sid();
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A unit has just become a Primordial: its awakening plays as soon as the player can see it.</summary>
        public static void Request(Actor a)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            long id = a.Sid();
            float now = Time.unscaledTime;
            if (_recent.TryGetValue(id, out float at) && now - at < 20f)
            {
                return;   // (the trait editor can report one change twice)
            }
            _recent[id] = now;
            _pending.RemoveAll(p => p.Id == id);
            _pending.Add(new Pending { A = a, Id = id });
            if (Dev.On) Dev.Trace("awakening requested for " + a.asset.id + "#" + id);
        }

        private static void Tick()
        {
            if (_pending.Count == 0 || World.world == null)
            {
                return;
            }
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                Actor a = p.A;
                if (a == null || a.Sid() != p.Id || !a.isAlive() || !Primordials.Is(a) || WorldsEnd.Holds(a))
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                p.Waited += dt;
                // the player is still in a window (the trait editor): it waits for them (a minute at most)
                if (ScrollWindow.isWindowActive() && p.Waited < 60f)
                {
                    continue;
                }
                if (p.PannedAt < 0f)
                {
                    Spotlight.Primordial(a);
                    p.PannedAt = now;
                    continue;
                }
                float since = now - p.PannedAt;
                if (since < 0.9f)
                {
                    continue;   // the camera is on its way
                }
                bool full = Cinema.FullAt(a.current_position);
                if (!full && since < 3f)
                {
                    continue;
                }
                if (Awakening.Begin(a, full, since > 8f))
                {
                    _pending.RemoveAt(i);
                }
                else if (since > 12f)
                {
                    _pending.RemoveAt(i);
                }
            }
        }
    }

    internal sealed class Awakening : Move
    {
        private const float Erupt = 3f, Release = 6.6f, Land = 8.7f, Length = 9.3f;

        /// <summary>
        /// Where its titles sit: the usual title band near the top (0.51-0.73 of the half-view). The eye
        /// keeps under it (EyeAt) and the game's own messages above it, so nothing ever overlaps.
        /// </summary>
        private const float TitleY = 0.62f;

        /// <summary>Awakenings playing now (the screen-shake setting applies to them).</summary>
        internal static int Live;

        /// <summary>Whether this one is on the Live count (Uncount takes it off once).</summary>
        private bool _counted;

        private bool _full;
        private int _cues;
        private float _moteT, _crackT, _boltT, _rockT, _fxT;
        private Particle _lit, _flash;
        private float _flashLeft;
        private Color _flashColor = Color.white;
        private float _spin;

        private Awakening(Actor a) : base(a)
        {
            Level = 3;
        }

        public override bool Cinematic => _full;

        public override bool Intangible => true;

        public override bool SuperArmor => true;

        private FxLayer L => _full ? FxLayer.Cine : FxLayer.Top;

        private Particle C(Particle p)
        {
            return CineKit.C(p, _full);
        }

        public static bool Begin(Actor a, bool full, bool force)
        {
            if (WorldsEnd.Holds(a))
            {
                return false;
            }
            if (!Moves.CanPerform(a))
            {
                // a world event outranks whatever it was doing (a technique, a walk, a nap)
                if (Moves.TryGet(a, out Move cur))
                {
                    Moves.Cancel(cur);
                }
                if (!Moves.CanPerform(a) && !force)
                {
                    return false;
                }
            }
            Awakening m = new Awakening(a);
            if (full && Cinema.TryBegin(m, 0f))
            {
                m._full = true;
            }
            else if (full && !force)
            {
                return false;   // another cinematic is playing: wait for it to end
            }
            if (!Moves.Start(m))
            {
                if (m._full)
                {
                    Cinema.End(m);
                }
                return false;
            }
            if (m._full)
            {
                Cinema.Hold(m);
                Cinema.Dim(0.84f, new Color(0.05f, 0.01f, 0.1f, 1f), 1f);
                m._lit = CineKit.Copy(m, FxLayer.Cine, 20, false);
            }
            Live++;
            m._counted = true;
            Dev.Watch(m.BodyPos() + new Vector2(0f, 1f));
            if (Dev.On) Dev.Trace("awakening of " + a.asset.id + "#" + a.Sid() + (m._full ? " (full)" : " (light)"));
            return true;
        }

        // ------------------------------------------------------------------ view --

        private static bool View(out Vector2 c, out float hw, out float hh)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null)
            {
                c = Vector2.zero;
                hw = hh = 0f;
                return false;
            }
            c = cam.transform.position;
            hh = cam.orthographicSize;
            hw = hh * cam.aspect;
            return true;
        }

        private static Vector2 EdgePoint(Vector2 c, float hw, float hh)
        {
            float x = Random.Range(-1f, 1f), y = Random.Range(-1f, 1f);
            if (Random.value < 0.5f)
            {
                x = x < 0f ? -1.05f : 1.05f;
            }
            else
            {
                y = y < 0f ? -1.05f : 1.05f;
            }
            return c + new Vector2(x * hw, y * hh);
        }

        // ------------------------------------------------------------------ tick --

        public override void Tick(float dt)
        {
            float t = T;
            if (_flashLeft > -0.3f)
            {
                _flashLeft -= dt;
            }
            Pose.Nudge = Vector2.zero;
            PoseAt(t);
            Beat(t, dt);
            if (t >= Length)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Finished = true;
            }
        }

        private void PoseAt(float t)
        {
            float K = this.K;
            if (t < 0.4f)
            {
                // it bows under something vast arriving
                float k = Anim.OutCubic(t / 0.4f);
                Pose.SX = 1f + 0.12f * k;
                Pose.SY = 1f - 0.2f * k;
                Pose.Rot = 9f * k;
                Pose.Height = 0f;
            }
            else if (t < 2.6f)
            {
                // it rises, trembling harder and harder
                float k = Anim.InOutSine((t - 0.4f) / 2.2f);
                Pose.SX = Mathf.Lerp(1.12f, 0.96f, k);
                Pose.SY = Mathf.Lerp(0.8f, 1.06f, k);
                Pose.Rot = Mathf.Lerp(9f, 0f, k);
                Pose.Height = 1.6f * K * k;
                float shake = 0.3f + 1.2f * k;
                Pose.Nudge = new Vector2(((int)(t * 60f) & 1) == 0 ? shake : -shake, 0f);
            }
            else if (t < Erupt)
            {
                // curled up inside the collapsing void
                float k = (t - 2.6f) / (Erupt - 2.6f);
                Pose.SX = Mathf.Lerp(0.96f, 1.15f, k);
                Pose.SY = Mathf.Lerp(1.06f, 0.72f, k);
                Pose.Height = 1.6f * K;
                Pose.Rot = 0f;
            }
            else if (t < Release)
            {
                // awake: tall, high in the pillar
                float k = Anim.OutCubic(Mathf.Clamp01((t - Erupt) / 0.25f));
                float amp = Anim.Spring(t - Erupt, 4f, 12f);
                Pose.SX = 1f - 0.12f * amp;
                Pose.SY = 1.08f + 0.2f * amp;
                Pose.Rot = 0f;
                Pose.Height = Mathf.Lerp(1.6f * K, 2.4f * K, k) + 0.08f * K * Mathf.Sin(t * 3f);
            }
            else if (t < Land)
            {
                // it comes down into the world
                float k = Anim.InOutSine((t - Release) / (Land - Release));
                Pose.Height = Mathf.Lerp(2.4f * K, 0f, k);
                Pose.SX = 1f;
                Pose.SY = 1.06f;
            }
            else
            {
                float amp = Anim.Spring(t - Land, 9f, 18f);
                Pose.Height = 0f;
                Pose.SX = 1f + 0.12f * amp;
                Pose.SY = 1f - 0.14f * amp;
            }
        }

        private bool Cue(int bit, float t, float at)
        {
            int m = 1 << bit;
            if (t < at || (_cues & m) != 0)
            {
                return false;
            }
            _cues |= m;
            return true;
        }

        private void Beat(float t, float dt)
        {
            float px = Px, K = this.K;
            Vector2 ground = Pose.Ground, body = BodyPos();
            WorldTile tile = Combat.TileAt(ground);
            View(out Vector2 vc, out float hw, out float hh);

            // ------------------------------------------------------- the world stops
            if (Cue(0, t, 0f))
            {
                World.world.startShake(Release, 0.01f, 2.2f);
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile);
                Primordials.Flash(Fx.WithA(new Color(0.55f, 0.2f, 1f, 1f), 0.35f), 0.4f);
            }
            if (Cue(1, t, 0.35f) && Settings.TitleCards)
            {
                string tt = Locale.Get("sakuga_title_world_trembles");
                Cinema.Title(tt, new Color(0.75f, 0.6f, 1f, 1f), 2f, body, CineKit.TitleSize(tt, 0.8f), TitleY);
            }
            // everything is drawn into it
            if (t < 2.8f)
            {
                _moteT -= dt;
                if (_moteT <= 0f && hw > 0f)
                {
                    _moteT = 0.025f;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 from = Random.value < 0.6f ? EdgePoint(vc, hw, hh) : vc + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh));
                        float life = Random.Range(0.45f, 0.8f);
                        float sz = Mathf.Max(px, hh / 90f);
                        Particle p = C(Fx.Spawn(Art.Sparks[0], from, sz, life, L, 22));
                        if (p == null)
                        {
                            break;
                        }
                        p.Moving((body - from) / life);
                        p.FaceVel = true;
                        Color c = Primordials.Prism(Random.value, 0.7f);
                        p.Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.9f), Fx.WithA(Color.white, 1f));
                    }
                }
                // cracks of light racing out across the ground
                _crackT -= dt;
                if (_crackT <= 0f && t > 0.3f)
                {
                    _crackT = 0.22f;
                    Crack(ground, Random.insideUnitCircle.normalized, (4f + 10f * t) * K, px);
                }
                // the land rises toward it
                _rockT -= dt;
                if (_rockT <= 0f && hw > 0f)
                {
                    _rockT = 0.06f;
                    Vector2 at = ground + Random.insideUnitCircle * Mathf.Min(hw, 14f * K);
                    Color soil = Fx.SoilColor(Combat.TileAt(at));
                    Particle r = C(Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px * Random.Range(1.2f, 2.4f), Random.Range(1.6f, 2.4f), L, 9));
                    if (r != null)
                    {
                        r.Moving(new Vector2(Random.Range(-2f, 2f), Random.Range(10f, 24f)) * px * K, 0f, 0.6f);
                        r.Spin = Random.Range(-90f, 90f);
                        r.Color(Fx.WithA(soil, 0f), Color.Lerp(soil, Color.white, 0.15f), Fx.WithA(soil, 0f));
                    }
                }
            }
            // the void gathers round it
            if (t >= 0.4f && t < Erupt)
            {
                float k = t < 2.6f ? Anim.OutCubic((t - 0.4f) / 2.2f) : 1f - Anim.InQuad((t - 2.6f) / (Erupt - 2.6f));
                float r = Mathf.Max(0.15f, (0.4f + 2.4f * k) * K);
                bool swallow = t >= 2.6f;
                if (Kit.Visible(body, 2f))
                {
                    C(Spec.Blob(body, r * 1.25f, px, 0.05f, L, swallow ? 23 : 17, 1f, 0.5f, Fx.WithA(Primordials.Prism(), 0.55f)));
                    C(Spec.Blob(body, r, px, 0.05f, L, swallow ? 24 : 18, 1f, 0.08f, Fx.WithA(Primordials.Void, 0.97f)));
                    if (Random.value < 0.35f)
                    {
                        C(Fx.Ring(body, px, 0.25f, r / px * 2.2f, r / px * 1.05f, 2, 1, L, 19, 1f, 2f, Fx.WithA(Primordials.Prism(Random.value), 0f), Fx.WithA(Color.white, 0.85f)));
                    }
                }
            }
            // three heartbeats
            for (int i = 0; i < 3; i++)
            {
                if (Cue(2 + i, t, 0.75f + 0.65f * i))
                {
                    float s = 0.12f + 0.08f * i;
                    Primordials.Flash(Fx.WithA(Primordials.Prism(i * 0.3f), s), 0.16f);
                    FlashUnit(i == 2 ? Color.white : Primordials.Void, 0.08f);
                    if (hw > 0f)
                    {
                        float pr = hw / 140f;
                        C(Fx.Ring(ground, pr, 0.35f, 130f, 6f, 2, 4, L, 16, Art.Ground, 2.2f, Fx.WithA(Primordials.Prism(i * 0.3f), 0f), Fx.WithA(Color.white, 0.8f)));
                    }
                    World.world.startShake(0.3f, 0.01f, 2f + i);
                    Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
                }
            }
            // the eye opens in the sky
            if (Cue(5, t, 2.7f))
            {
                OpenEye(vc, hw, hh, body);
                Choreo.Sound("event:/SFX/POWERS/Curse", tile);
            }

            // ------------------------------------------------------- the awakening
            if (Cue(6, t, Erupt))
            {
                Primordials.Flash(Fx.WithA(Color.white, 1f), 0.7f);
                if (_full)
                {
                    Cinema.Dim(0.66f, new Color(0.05f, 0.01f, 0.1f, 1f), 0.4f);
                }
                FlashUnit(Color.white, 0.15f);
                Pillar(ground, vc, hh, K, px);
                for (int i = 0; i < 7; i++)
                {
                    float pr = hw / 140f;
                    Particle ring = C(Fx.Ring(ground, pr, 1.1f + 0.12f * i, 3f, 140f, 5, 1, L, 15, Art.Ground, 2.2f,
                        Color.white, Fx.WithA(Primordials.Prism(i / 7f, 0.75f), 0.9f), Fx.WithA(Primordials.Prism(i / 7f + 0.1f), 0f)));
                    if (ring != null)
                    {
                        ring.Delay = 0.09f * i;
                    }
                }
                C(Spec.Blob(body, 3.5f * K, px, 0.6f, L, 25, 1f, 0.5f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f), Fx.WithA(Primordials.Void, 0f)))?.Sized(0.3f, 1.6f);
                if (Settings.Destruction > 0)
                {
                    Kit.Ravage(ground, Mathf.Clamp(3f * K, 2f, 6f), 2, A, false);
                }
                Kit.Crater(ground, px, tile, true, 12f, false, Primordials.Prism());
                Spec.Rocks(ground, px, 24, 140f, K);
                World.world.startShake(3.6f, 0.01f, 4.5f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionHuge", tile);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", tile);
                if (Settings.TitleCards)
                {
                    string tt = Locale.Get("sakuga_title_primordial_awakened");
                    Cinema.Title(tt, Color.Lerp(Primordials.Prism(), Color.white, 0.35f), 1.8f, body, CineKit.TitleSize(tt, 1.1f), TitleY);
                }
            }
            if (t >= Erupt && t < Release + 0.6f)
            {
                _spin += dt * 25f;
                _fxT -= dt;
                if (_fxT <= 0f)
                {
                    _fxT = 0.03f;
                    Halo(body, px, K, t);
                    Rays(body, vc, hw, hh, px, t);
                    if (t < Release)
                    {
                        PrimFx.Flow(ground, new Vector2(ground.x, vc.y + hh * 1.1f), 2.2f * K, 0.04f, L, 13, _full, Primordials.Prism(t * 0.2f), Color.white);
                    }
                }
                // lightning all over the sky
                _boltT -= dt;
                if (_boltT <= 0f && hw > 0f && t < Release)
                {
                    _boltT = Random.Range(0.08f, 0.16f);
                    Vector2 to = vc + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh * 0.5f));
                    Vector2 from = new Vector2(to.x + Random.Range(-0.2f, 0.2f) * hw, vc.y + hh * 1.1f);
                    Bolt(from, to, Mathf.Max(px, hh / 120f), 0.18f, Random.value < 0.5f ? Primordials.Prism(Random.value) : new Color(1f, 0.3f, 0.35f, 1f));
                    if (Random.value < 0.3f)
                    {
                        Primordials.Flash(Fx.WithA(Color.white, 0.12f), 0.06f);
                    }
                }
                // the land keeps rising toward it
                _rockT -= dt;
                if (_rockT <= 0f && hw > 0f && t < Release)
                {
                    _rockT = 0.05f;
                    Vector2 at = vc + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh));
                    Color soil = Fx.SoilColor(Combat.TileAt(at));
                    Particle r = C(Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, Mathf.Max(px, hh / 100f) * Random.Range(1f, 2f), Random.Range(1.4f, 2.2f), L, 9));
                    if (r != null)
                    {
                        r.Moving(new Vector2(0f, Random.Range(0.08f, 0.2f) * hh), 0f, 0.5f);
                        r.Spin = Random.Range(-120f, 120f);
                        r.Color(Fx.WithA(soil, 0f), soil, Fx.WithA(soil, 0f));
                    }
                }
            }
            if (Cue(7, t, Erupt + 1.9f) && Settings.TitleCards)
            {
                string name = A != null ? A.getName() : "";
                string tt = Locale.Format("sakuga_title_primordial_name", name).ToUpperInvariant();
                Cinema.Title(tt, new Color(0.85f, 0.8f, 1f, 1f), 1.6f, body, CineKit.TitleSize(tt, 1f), TitleY);
            }

            // ------------------------------------------------------- the world moves again
            if (Cue(8, t, Release))
            {
                if (_full)
                {
                    Cinema.Unhold(this);
                    Cinema.Undim(2.2f);
                }
                World.world.startShake(1.5f, 0.01f, 3f);
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile);
                // everything near it thrown down
                List<Actor> near = new List<Actor>();
                Primordials.Around(null, ground, 9f * K, true, near);
                foreach (Actor u in near)
                {
                    if (u != A && !Primordials.Is(u))
                    {
                        Combat.Launch(u, ground, Random.Range(2f, 3.5f), Random.Range(0.6f, 1f), Random.value < 0.5f ? -600f : 600f);
                    }
                }
                CineKit.DustRing(ground, px, tile, 20, 110f, 0.8f, K, false);
                // and every living thing in the world turns to look
                int n = 0;
                foreach (Actor u in World.world.units.getSimpleList())
                {
                    if (u == null || u == A || !u.isAlive() || u.asset == null || !u.asset.has_sprite_renderer)
                    {
                        continue;
                    }
                    try
                    {
                        u.addStatusEffect("surprised", 0f, false);
                    }
                    catch
                    {
                    }
                    if (++n > 6000)
                    {
                        break;
                    }
                }
                try
                {
                    string tip = Locale.Format("sakuga_primordial_tip", A != null ? A.getName() : "");
                    WorldTip.showNow(tip, false, "top", 6f, "#C8B4FF");
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Sakuga] awakening tip: " + e);
                }
                if (Dev.On) Dev.Trace("awakening: the world moves again; " + n + " units turned to look");
            }
            if (Cue(9, t, Land))
            {
                C(Fx.Ring(ground, px, 0.7f, 3f, 60f * K, 3, 1, L, 6, Art.Ground, 2.2f, Color.white, Fx.WithA(Primordials.Prism(), 0.8f), Fx.WithA(Primordials.Prism(0.5f), 0f)));
                CineKit.LandingPuff(ground, px, tile, _full);
                Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            }
        }

        // --------------------------------------------------------------- pieces --

        /// <summary>A fissure of light racing out over the ground.</summary>
        private void Crack(Vector2 from, Vector2 dir, float len, float px)
        {
            PrimFx.Crack(from, dir, len, 2.6f * px, 2.6f, Primordials.Prism(Random.value, 0.65f), L, 8, _full, true, 1);
        }

        /// <summary>Branching lightning (drawn on the cinematic clock).</summary>
        private void Bolt(Vector2 a, Vector2 b, float px, float life, Color edge)
        {
            PrimFx.Bolt(a, b, 3f * px, life, edge, L, 23, _full);
        }

        /// <summary>The eye of the first, opening in the sky over it, watching it, closing as the world moves again.</summary>
        private void OpenEye(Vector2 vc, float hw, float hh, Vector2 body)
        {
            if (hw <= 0f)
            {
                return;
            }
            const float life = 3.5f;
            Eye(Lens(1.25f), false, life, 16, 0f, true, 0.55f);
            Eye(Lens(1f), false, life, 17, 0f, false, 1f, new Color(0.96f, 0.94f, 1f, 1f));
            Eye(Art.Disk(10, 1f, 0.1f), true, life, 18, 0.2f, true, 1f);
            Eye(Art.Disk(5, 1f, 0.05f), true, life, 19, 0.65f, true, 1f);
            Eye(Art.Disk(2, 4f, 0.02f), true, life, 20, 0f, false, 1f, new Color(0.02f, 0f, 0.05f, 1f));
            Primordials.Flash(Fx.WithA(Primordials.Prism(), 0.25f), 0.15f);
        }

        /// <summary>Where the eye is this frame (held in the sky of the view, whatever the camera does) and its pixel size.</summary>
        private bool EyeAt(out Vector2 at, out float epx, out Vector2 look)
        {
            at = look = Vector2.zero;
            epx = 0f;
            if (!View(out Vector2 vc, out float hw, out float hh))
            {
                return false;
            }
            Vector2 body = BodyPos();
            // under the title band: its glow reaches at most 0.48 of the half-view up (titles start at 0.51)
            float w = Mathf.Min(hw * 0.36f, Mathf.Min(20f * K, hh * 0.68f));
            epx = w / LensW;
            at = new Vector2(Mathf.Lerp(vc.x, body.x, 0.6f), vc.y + hh * 0.29f);
            // it looks down at the one who woke
            look = new Vector2(Mathf.Clamp((body.x - at.x) / Mathf.Max(1f, w), -1f, 1f) * 10f, -5f) * epx;
            return true;
        }

        private const int LensW = 96, LensH = 34;
        private static readonly Dictionary<int, Sprite> _lens = new Dictionary<int, Sprite>();

        /// <summary>An almond: an eye's white, pointed at both ends ('grow' makes the glowing rim round it).</summary>
        private static Sprite Lens(float grow)
        {
            int key = Mathf.RoundToInt(grow * 100f);
            if (_lens.TryGetValue(key, out Sprite sp))
            {
                return sp;
            }
            int w = Mathf.RoundToInt(LensW * grow), h = Mathf.RoundToInt(LensH * (grow > 1f ? grow * 1.25f : 1f));
            Canvas c = new Canvas(w + 2, h + 2);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float half = h * 0.5f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.8f);
                for (int y = 0; y < h; y++)
                {
                    float v = y + 0.5f - h * 0.5f;
                    if (Mathf.Abs(v) <= half)
                    {
                        c.Set(x + 1, y + 1, 1f, 1f);
                    }
                }
            }
            sp = c.ToSprite("sakuga_primordial_lens_" + key, new Vector2(0.5f, 0.5f));
            _lens[key] = sp;
            return sp;
        }

        private void Eye(Sprite s, bool iris, float life, int order, float hue, bool prism, float alpha, Color? fixedColor = null)
        {
            if (!EyeAt(out Vector2 at0, out float e0, out Vector2 look0))
            {
                return;
            }
            Particle p = C(Fx.Spawn(s, iris ? at0 + look0 : at0, e0, life, L, order));
            if (p == null)
            {
                return;
            }
            Color fixedC = fixedColor ?? Color.white;
            p.Step = (q, dt) =>
            {
                if (EyeAt(out Vector2 at, out float epx, out Vector2 look))
                {
                    q.Pos = iris ? at + look : at;
                    q.Px = epx;
                }
                float a = q.Age;
                float open = a < 0.35f ? Anim.OutCubic(a / 0.35f) : a > life - 0.4f ? 1f - Anim.InQuad((a - (life - 0.4f)) / 0.4f) : 1f;
                // a blink half way through
                if (a > 1.9f && a < 2.1f)
                {
                    open *= Mathf.Abs(a - 2f) / 0.1f;
                }
                q.SX = 1f;
                q.SY = Mathf.Max(0.02f, open);
                q.Grad = null;
                Color c = prism ? Primordials.Prism(hue + a * 0.15f, 0.75f) : fixedC;
                q.R.color = Fx.WithA(c, alpha * Mathf.Clamp01(open * 1.5f));
            };
        }

        /// <summary>The pillar of the first light: from the ground up through the top of the view, every colour, a white core.</summary>
        private void Pillar(Vector2 foot, Vector2 vc, float hh, float K, float px)
        {
            float top = Mathf.Max(foot.y + 8f, vc.y + hh * 1.1f);
            Vector2 up = new Vector2(foot.x, top);
            const float life = Release - Erupt + 0.8f;
            for (int i = 0; i < 4; i++)
            {
                int layer = i;
                float w0 = (i == 0 ? 6f : i == 1 ? 4.2f : i == 2 ? 2.4f : 0.9f) * K;
                Particle p = PrimFx.Band(foot, up, w0, life, L, 9 + i, _full, i <= 1 ? 0 : i == 2 ? 1 : 2, Color.white);
                if (p == null)
                {
                    return;
                }
                p.Step = (q, dt) =>
                {
                    float a = q.Age;
                    float grow = Anim.OutCubic(Mathf.Clamp01(a / 0.15f));
                    float fade = a > life - 0.9f ? 1f - Anim.InQuad((a - (life - 0.9f)) / 0.9f) : 1f;
                    float shimmer = 1f + 0.08f * Mathf.Sin(a * 50f + layer * 2f);
                    float burst = a < 0.25f ? 1.6f - 0.6f * (a / 0.25f) : 1f;
                    q.SY = Mathf.Max(0.001f, grow * shimmer * fade * burst);
                    q.Grad = null;
                    Color c = layer == 3 ? Color.white : layer == 0 ? Primordials.Void : Primordials.Prism(layer * 0.33f + a * 0.2f, layer == 1 ? 0.8f : 0.55f);
                    q.R.color = Fx.WithA(c, (layer == 0 ? 0.45f : layer == 1 ? 0.45f : layer == 2 ? 0.75f : 0.95f) * Mathf.Clamp01(fade * 1.5f));
                };
            }
        }

        /// <summary>A halo of light orbiting it on two tilted rings.</summary>
        private void Halo(Vector2 body, float px, float K, float t)
        {
            if (!Kit.Visible(body, 1f))
            {
                return;
            }
            for (int ring = 0; ring < 2; ring++)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = t * (ring == 0 ? 5f : -4f) + i * 2.094f;
                    float tilt = ring == 0 ? 25f : -35f;
                    Vector2 o = new Vector2(Mathf.Cos(a) * 1.6f * K, Mathf.Sin(a) * 0.45f * K);
                    o = Choreo.Rotate(o, tilt);
                    bool front = Mathf.Sin(a) < 0f;
                    Particle p = C(Fx.Spawn(Art.Sparks[0], body + o, px * 1.4f, 0.22f, L, front ? 26 : 15));
                    if (p == null)
                    {
                        return;
                    }
                    Color c = Primordials.Prism(i / 3f + ring * 0.5f, 0.6f);
                    p.Color(Color.white, Fx.WithA(c, 0.9f), Fx.WithA(c, 0f));
                }
            }
            if (Random.value < 0.5f)
            {
                C(CineKit.Ember(body + Random.insideUnitCircle * 1.2f * K, px, Random.Range(0.4f, 0.7f), new Vector2(0f, Random.Range(1.5f, 3f) * K), Primordials.Prism(Random.value), L, _full, 0.8f));
            }
        }

        /// <summary>Rays of light turning round it out to the edges of the view.</summary>
        private void Rays(Vector2 body, Vector2 vc, float hw, float hh, float px, float t)
        {
            if (hw <= 0f)
            {
                return;
            }
            float reach = Mathf.Sqrt(hw * hw + hh * hh) * 1.3f;
            float grow = Anim.OutCubic(Mathf.Clamp01((t - Erupt) / 0.4f));
            float fade = t > Release ? 1f - Mathf.Clamp01((t - Release) / 0.6f) : 1f;
            float thick = Mathf.Max(px, hh / 140f);
            for (int i = 0; i < 10; i++)
            {
                float ang = _spin + i * 36f;
                Color c = Primordials.Prism(i / 10f, 0.6f);
                float baseW = (i % 2 == 0 ? 1.4f : 0.8f) * K;
                PrimFx.Ray(body, ang, reach * grow, baseW, 0.04f, L, 12, _full, Fx.WithA(Color.Lerp(c, Color.white, 0.4f), 0.75f * fade));
            }
        }

        // --------------------------------------------------------------- glue --

        private void FlashUnit(Color c, float seconds)
        {
            if (_flash == null || !_flash.Alive)
            {
                _flash = CineKit.Copy(this, _full ? FxLayer.Cine : FxLayer.Over, _full ? 21 : 1, true);
            }
            _flashColor = c;
            _flashLeft = seconds;
        }

        public override void Frame()
        {
            CineKit.StandStill(this);
            if (_lit != null && _lit.Alive)
            {
                Fx.Glue(_lit, this, Fx.WithA(Color.white, 1f - Anim.Smooth(Release + 1.6f, Release + 2.3f, T)));
            }
            if (_flash != null && _flash.Alive)
            {
                float a = _flashLeft > 0f ? 0.9f : Mathf.Clamp01(1f + _flashLeft / 0.08f) * 0.9f;
                Fx.Glue(_flash, this, Fx.WithA(_flashColor, a));
            }
        }

        private void Cleanup()
        {
            _lit?.Kill();
            _lit = null;
            _flash?.Kill();
            _flash = null;
            Cinema.End(this);
        }

        /// <summary>
        /// Takes this awakening off the Live count, once. A world cleared mid-awakening only aborts it
        /// (Moves.Clear never calls OnEnd), and the count left behind kept every camera shake and tile
        /// flash of the session under the Primordial screen-shake setting.
        /// </summary>
        private void Uncount()
        {
            if (_counted)
            {
                _counted = false;
                Live = Mathf.Max(0, Live - 1);
            }
        }

        public override void Abort()
        {
            Uncount();
            Cleanup();
        }

        public override void OnEnd(bool aborted)
        {
            Uncount();
            Cleanup();
            if (!aborted && A.isAlive())
            {
                A.setHealth(A.getMaxHealth());
            }
        }
    }
}
