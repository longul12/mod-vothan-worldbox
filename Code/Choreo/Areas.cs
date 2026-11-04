using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// Something a technique leaves in the world that plays on by itself (a shockwave racing out, a
    /// pillar rising, a rain of swords): ticked once per frame after the moves, it hurts only the
    /// enemies of the one who made it (each may answer it once, Combat.Reach as an area blow), and
    /// draws nothing off screen.
    /// </summary>
    internal abstract class Hazard
    {
        public Actor A;
        public long AId;
        public ChoreoMove M;
        /// <summary>The move it is credited to (M, or a team technique's move that has no performer script).</summary>
        public Move By;
        public Hue Hue;
        public Scale Scale = Scale.Light;
        public float Age, Px, K;
        public bool Done;
        protected readonly HashSet<long> HitIds = new HashSet<long>();

        protected Hazard(ChoreoMove m)
        {
            M = m;
            By = m;
            A = m.A;
            AId = A.Sid();
            Hue = m.Hue;
            Scale = m.Scale;
            Px = m.Px;
            K = m.Kk;
        }

        /// <summary>A hazard of a move that is not a performer's script (a team technique's): credited to 'by'.</summary>
        protected Hazard(Actor a, Move by, Hue hue, Scale scale)
        {
            By = by;
            A = a;
            AId = a.Sid();
            Hue = hue;
            Scale = scale;
            Px = Choreo.PxOf(a);
            K = Choreo.KOf(a);
        }

        protected bool OwnerOk => A != null && A.Sid() == AId && A.isAlive();

        /// <summary>
        /// The owner, or its corpse, still has its own kingdom and tier (its blasts spare its own people's
        /// roofs and hit as hard as it did): false once the corpse is destroyed or its actor reused.
        /// </summary>
        protected bool OwnerHere => A != null && A.Sid() == AId && A.kingdom != null;

        /// <summary>One frame (move time). False once it is over.</summary>
        public abstract bool Tick(float dt);

        public virtual void Kill()
        {
        }

        protected static readonly List<Actor> Near = new List<Actor>();

        /// <summary>
        /// Hits 'u' once (an area blow: it may evade, never parry): x 'mult', and 'hurt' pushed 'push' tiles
        /// away from 'from' (Launch: 'lift' tiles high; one above the class limits only takes a glancing
        /// blow and rocks). True if it landed.
        /// </summary>
        protected bool Strike(Actor u, float mult, Hurt hurt, Vector2 from, float push, float lift = -1f, float power = 1f)
        {
            if (u == null || !OwnerOk || HitIds.Contains(u.Sid()) || !Choreo.FairBlow(A, u, By, out float cm, out bool full, true))
            {
                return false;
            }
            HitIds.Add(u.Sid());
            if (Combat.Reach(By, u, true) != Contact.Hit)
            {
                return false;
            }
            HitResult r = Combat.Hit(A, u, mult * cm, false, false, 0f, By);
            if (!full)
            {
                hurt = Hurt.Stagger;
            }
            Vector2 away = u.current_position - from;
            Impact im = Impact.Of(hurt, away, push, power);
            im.Lift = lift;
            im.Owed = MeteorSlam.AtDeathsDoor(u);
            im.Daze = hurt == Hurt.Knockdown ? 0.7f : 0f;
            Victims.Hurt(u, A, By, im);
            Victims.Flash(u, Color.white, 0.05f);
            if (Kit.Visible(u.current_position))
            {
                ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.up, Px, K, 0.8f * power, Hue);
            }
            return true;
        }
    }

    /// <summary>The hazards in the world.</summary>
    internal static class Areas
    {
        private static readonly List<Hazard> _list = new List<Hazard>();

        public static int Count => _list.Count;

        public static T Add<T>(T h) where T : Hazard
        {
            if (h != null)
            {
                _list.Add(h);
            }
            return h;
        }

        public static void Tick(float dt)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Hazard h = _list[i];
                bool alive;
                try
                {
                    alive = !h.Done && h.Tick(dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("choreo hazard " + h.GetType().Name, e);
                    alive = false;
                }
                if (!alive)
                {
                    h.Done = true;
                    h.Kill();
                    _list.Remove(h);
                }
            }
        }

        public static void Clear()
        {
            foreach (Hazard h in _list)
            {
                h.Kill();
            }
            _list.Clear();
        }
    }

    // ------------------------------------------------------------- shockwave --

    /// <summary>A ring racing out over the ground from 'At' to 'R' tiles: everyone it passes is hit and thrown outward.</summary>
    internal sealed class ShockwaveHazard : Hazard
    {
        public Vector2 At;
        public float R, Speed, Mult, Push, Lift = -1f;
        public Hurt Hurt = Hurt.Knockdown;
        private float _r, _dustT;

        public ShockwaveHazard(ChoreoMove m, Vector2 at, float r, float speed, float mult, Hurt hurt, float push) : base(m)
        {
            At = at;
            R = r;
            Speed = Mathf.Max(2f, speed);
            Mult = mult;
            Hurt = hurt;
            Push = push;
            if (Kit.Visible(at, 0.5f))
            {
                float life = r / Speed;
                Fx.Ring(at, Px, life + 0.1f, 3f, r / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1.3f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(at, Px, life * 0.8f + 0.1f, 2f, r / Px * 0.8f, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.5f, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float was = _r;
            _r = Mathf.Min(R, _r + Speed * dt);
            Choreo.Foes(A, At, _r + 0.3f, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Vector2.Distance(u.current_position, At);
                if (d >= was - 0.4f)
                {
                    Strike(u, Mult * Mathf.Lerp(1f, 0.6f, d / Mathf.Max(0.1f, R)), Hurt, At, Push * Mathf.Lerp(1f, 0.6f, d / Mathf.Max(0.1f, R)), Lift);
                }
            }
            _dustT -= dt;
            if (_dustT <= 0f && Kit.Visible(At, 0.5f))
            {
                _dustT = 0.05f;
                float a = Random.value * Mathf.PI * 2f;
                Vector2 p = At + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * _r;
                ChoreoFx.HeelDust(p, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), Px, Combat.TileAt(p), K);
            }
            return _r < R;
        }
    }

    // ------------------------------------------------------------------ dome --

    /// <summary>A dome of light swelling at 'At' to 'R' tiles over 'Grow' seconds, then bursting: everyone inside is flung out (Scattered).</summary>
    internal sealed class DomeHazard : Hazard
    {
        public Vector2 At;
        public float R, Grow, Mult, Push;
        private Particle _disk, _rim;
        private bool _burst;

        public DomeHazard(ChoreoMove m, Vector2 at, float r, float grow, float mult, float push) : base(m)
        {
            At = at;
            R = r;
            Grow = Mathf.Max(0.05f, grow);
            Mult = mult;
            Push = push;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float k = Mathf.Clamp01(Age / Grow);
            float rp = Mathf.Max(2f, R / Px * Beat.Apply(Ease.OutCubic, k));
            if (Kit.Visible(At, 0.5f) && !_burst)
            {
                _disk = Shape(_disk, Art.Disk(Mathf.Clamp(Mathf.RoundToInt(rp), 1, 140), 0.8f, 0.4f), Fx.WithA(Hue.Main, 0.28f + 0.2f * k), 3);
                _rim = Shape(_rim, Art.Ring(Mathf.Clamp(Mathf.RoundToInt(rp), 1, 140), 2, 0.8f), Fx.WithA(Hue.Core, 0.9f), 4);
            }
            if (!_burst && k >= 1f)
            {
                _burst = true;
                Kill();
                Choreo.Foes(A, At, R, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Strike(Near[i], Mult, Hurt.Launch, At, Push, 1.2f + 0.2f * Push, 1.2f);
                }
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", Combat.TileAt(At));
                if (Kit.Visible(At, 0.5f))
                {
                    Kit.ImpactFrame(At + new Vector2(0f, 0.5f * K), Px, 0.9f * K, Hue.Pal, false);
                    Fx.Ring(At, Px, 0.35f, rp * 0.6f, rp * 1.5f, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Color.white, 1f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                    Kit.DustRing(At, Px, Combat.TileAt(At), 16, 70f, 0.6f, K);
                }
                if (OwnerHere)
                {
                    Ruin.Blast(A, At, R * 0.6f, Scale, A.isAlive() ? A.current_position : (Vector2?)null);
                }
                Ruin.Quake(A, At, Scale, 1.4f, K);
                if (M != null && !M.Ended)
                {
                    M.SetLink(Link.Scattered, null);
                }
                return false;
            }
            return true;
        }

        private Particle Shape(Particle p, Sprite s, Color c, int order)
        {
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(s, At + new Vector2(0f, 0.2f * K), Px, 1f, FxLayer.Top, order);
                if (p == null)
                {
                    return null;
                }
                p.Held = true;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Grad = null;
            p.R.color = c;
            return p;
        }

        public override void Kill()
        {
            _disk?.Kill();
            _rim?.Kill();
            _disk = _rim = null;
        }
    }

    // ----------------------------------------------------------------- quake --

    /// <summary>The ground quakes at 'At': fissures race out to 'R' tiles, rings ripple, everyone standing in it bounces and falls.</summary>
    internal sealed class QuakeHazard : Hazard
    {
        public Vector2 At;
        public float R, Mult;
        public int Cracks;
        private float _ringT;
        private readonly float[] _ang;
        private bool _hit;

        public QuakeHazard(ChoreoMove m, Vector2 at, float r, float mult, int cracks) : base(m)
        {
            At = at;
            R = r;
            Mult = mult;
            Cracks = Mathf.Clamp(cracks, 0, 12);
            _ang = new float[Cracks];
            float a0 = Random.value * 360f;
            for (int i = 0; i < Cracks; i++)
            {
                _ang[i] = a0 + i * 360f / Mathf.Max(1, Cracks) + Random.Range(-18f, 18f);
            }
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(at), 0.3f);
            if (Kit.Visible(at, 0.6f))
            {
                WorldTile t = Combat.TileAt(at);
                Color dark = Fx.Darken(Fx.SoilColor(t), 0.55f);
                for (int i = 0; i < Cracks; i++)
                {
                    float a = _ang[i] * Mathf.Deg2Rad;
                    int len = Mathf.Clamp(Mathf.RoundToInt(R / Px * Random.Range(0.7f, 1f)), 6, 120);
                    Particle f = Fx.Spawn(ChoreoArt.Fissure(len, i), at, Px, 3f, FxLayer.Ground, 2);
                    if (f != null)
                    {
                        f.Rot = Mathf.Atan2(Mathf.Sin(a) * Art.Ground, Mathf.Cos(a)) * Mathf.Rad2Deg;
                        f.Snap = true;
                        f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                    }
                    Particle g = Fx.Spawn(ChoreoArt.Fissure(len, i), at, Px, 1.2f, FxLayer.Ground, 3);
                    if (g != null)
                    {
                        g.Rot = f != null ? f.Rot : 0f;
                        g.Snap = true;
                        g.Color(Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                    }
                }
                Kit.Crater(at, Px * K, t, Ruin.Force(Tiers.Of(A), Scale) >= 3, 6f, false, Hue.Edge);
            }
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i < Cracks; i++)
                {
                    float a = _ang[i] * Mathf.Deg2Rad;
                    Vector2 end = Combat.Clamp(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R * 0.85f);
                    Ruin.Furrow(A, at, end, 0.6f, Scale, A.current_position, false);
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            Ruin.Quake(A, at, Scale, 1.5f, K);
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            _ringT -= dt;
            if (_ringT <= 0f && Age < 0.5f && Kit.Visible(At, 0.6f))
            {
                _ringT = 0.12f;
                Fx.Ring(At, Px, 0.4f, 4f, R / Px, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.6f, Fx.WithA(Fx.Darken(Fx.SoilColor(Combat.TileAt(At)), 0.3f), 0.8f), Fx.WithA(Hue.Edge, 0f));
            }
            if (!_hit && Age >= 0.06f)
            {
                _hit = true;
                Choreo.Foes(A, At, R, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    float d = Vector2.Distance(u.current_position, At) / Mathf.Max(0.1f, R);
                    Strike(u, Mult * Mathf.Lerp(1f, 0.6f, d), Hurt.Knockdown, At, 0.3f, 0.35f + 0.3f * (1f - d), 1.1f);
                }
            }
            return Age < 0.6f;
        }
    }

    // ---------------------------------------------------------------- pillar --

    /// <summary>
    /// A stone pillar erupting at 'At' after 'Delay' (glowing cracks warn first): whoever stands on it
    /// is launched 'Lift' tiles up; it stands a moment and crumbles. A row of them makes a wall,
    /// a ring of them a cage (Spikes for pointed ones).
    /// </summary>
    internal sealed class PillarHazard : Hazard
    {
        public Vector2 At;
        public float Delay, Height, Mult, Lift, Stand = 0.8f;
        public bool Spike;
        private Particle _p, _warn;
        private int _warnGen;
        private bool _risen;
        private readonly int _w, _h;
        private readonly Color _stone;

        public PillarHazard(ChoreoMove m, Vector2 at, float delay, float height, float mult, float lift, bool spike) : base(m)
        {
            At = at;
            Delay = delay;
            Height = height;
            Mult = mult;
            Lift = lift;
            Spike = spike;
            _w = Mathf.Clamp(Mathf.RoundToInt((spike ? 4f : 6f) * Mathf.Lerp(1f, K, 0.6f)), 3, 30);
            _h = Mathf.Clamp(Mathf.RoundToInt(height / Px), 6, 120);
            _stone = Color.Lerp(Fx.SoilColor(Combat.TileAt(at)), new Color(0.62f, 0.58f, 0.54f, 1f), 0.5f);
            if (Kit.Visible(at, 0.4f) && delay > 0.05f)
            {
                _warn = Fx.Spawn(Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)].Glow, at, Px, delay + 0.05f, FxLayer.Ground, 4);
                if (_warn != null)
                {
                    _warnGen = _warn.Gen;   // (it fades by itself: the handle is only good while it is that same particle)
                    _warn.Color(Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.9f), Fx.WithA(Hue.Core, 1f));
                }
            }
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float t = Age - Delay;
            if (t < 0f)
            {
                return true;
            }
            bool vis = Kit.Visible(At, 0.4f);
            if (!_risen)
            {
                _risen = true;
                WorldTile tile = Combat.TileAt(At);
                Choreo.Sound("event:/SFX/HIT/HitStone", tile);
                if (vis)
                {
                    Kit.Debris(At, Px, tile, 7, K);
                    Kit.DustRing(At, Px, tile, 10, 40f, 0.5f, K);
                    Kit.Crater(At, Px, tile, false, 4f, true);
                }
                Choreo.Foes(A, At, Stand * Mathf.Lerp(1f, K, 0.5f), Near, true);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    Vector2 away = u.current_position - At;
                    Strike(u, Mult, Hurt.Launch, At - away * 0.2f, 0.4f, Lift, 1.2f);
                }
                if (OwnerHere)
                {
                    Ruin.Blast(A, At, 0.7f, Scale, A.isAlive() ? A.current_position : (Vector2?)null, false);
                }
            }
            // up fast, stands, crumbles back into the ground
            float rise = Beat.Apply(Ease.OutBack, Mathf.Clamp01(t / 0.1f));
            float sink = Mathf.Clamp01((t - 0.9f) / 0.35f);
            float sy = Mathf.Max(0f, rise * (1f - sink));
            if (vis)
            {
                if (_p == null || !_p.Alive)
                {
                    _p = Fx.Spawn(Spike ? ChoreoArt.Spike(_w, _h) : ChoreoArt.Pillar(_w, _h, Random.Range(0, 4)), At, Px, 1f, FxLayer.Behind, 30);
                    if (_p != null)
                    {
                        _p.Held = true;
                    }
                }
                if (_p != null)
                {
                    _p.Pos = At + new Vector2(0f, -0.5f * Px);
                    _p.SY = sy;
                    _p.Grad = null;
                    _p.R.color = _stone;
                    _p.R.sortingOrder = 20 + Mathf.RoundToInt(-At.y * 0.1f);
                }
                if (sink > 0f && sink < 1f && Random.value < 0.4f)
                {
                    Kit.Debris(At, Px, Combat.TileAt(At), 1, K * 0.6f, _h * Px * sy * 0.6f);
                }
            }
            else
            {
                Kill();
            }
            return sink < 1f;
        }

        public override void Kill()
        {
            _p?.Kill();
            _p = null;
            if (_warn != null && _warn.Alive && _warn.Gen == _warnGen)
            {
                _warn.Kill();
            }
            _warn = null;
        }
    }

    // ------------------------------------------------------------------ cone --

    /// <summary>A fan of fists (or blades) flying out of 'From' along 'Dir' (half-angle 'Half', 'Len' tiles): everyone in the cone is hit and driven back.</summary>
    internal sealed class ConeHazard : Hazard
    {
        public Vector2 From, Dir;
        public float Half, Len, Dur, Mult, Push;
        public bool Blades;
        private float _spawnT, _hitT;

        public ConeHazard(ChoreoMove m, Vector2 from, Vector2 dir, float half, float len, float dur, float mult, float push, bool blades) : base(m)
        {
            From = from;
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            Half = half;
            Len = len;
            Dur = dur;
            Mult = mult;
            Push = push;
            Blades = blades;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            if (Age > Dur)
            {
                return false;
            }
            float px = Px;
            _spawnT -= dt;
            if (_spawnT <= 0f && Kit.Visible(From, 0.5f))
            {
                _spawnT = 0.012f;
                float a = Random.Range(-Half, Half);
                Vector2 d = Choreo.Rotate(Dir, a);
                Vector2 at = From + d * Random.Range(0.2f, 0.8f) + new Vector2(0f, Random.Range(-0.15f, 0.35f) * K);
                Particle p = Fx.Spawn(Blades ? ChoreoArt.Crescent(Mathf.RoundToInt(4f * K), 1) : ChoreoArt.Fist(Mathf.RoundToInt(9f * K)), at, px, Len / 26f, FxLayer.Top, 12);
                if (p != null)
                {
                    p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    p.Moving(d * 26f);
                    p.Color(Hue.Core, Fx.WithA(Hue.Main, 0.85f), Fx.WithA(Hue.Edge, 0f));
                }
                Kit.Line(at, at - d * 6f * px, px, 0.06f, 1, FxLayer.Top, 11, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
            }
            _hitT -= dt;
            if (_hitT <= 0f)
            {
                _hitT = 0.08f;
                Choreo.Foes(A, From + Dir * Len * 0.5f, Len * 0.6f, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    Vector2 rel = u.current_position - From;
                    float along = Vector2.Dot(rel, Dir);
                    if (along < 0f || along > Len || Mathf.Abs(Vector2.Angle(Dir, rel)) > Half + 8f)
                    {
                        continue;
                    }
                    Strike(u, Mult, Hurt.Reel, From, Push, -1f, 1f);
                }
            }
            return true;
        }
    }

    // --------------------------------------------------------------- tornado --

    /// <summary>A spinning column of wind at 'At' ('R' tiles): it drags its enemies in, hurts them as they whirl, and at the end flings them high.</summary>
    internal sealed class TornadoHazard : Hazard
    {
        public Vector2 At;
        public float R, Dur, Mult, Fling;
        private float _ringT, _pullT;
        private readonly HashSet<long> _caught = new HashSet<long>();
        private readonly List<Actor> _in = new List<Actor>();
        private readonly List<long> _inIds = new List<long>();

        public TornadoHazard(ChoreoMove m, Vector2 at, float r, float dur, float mult, float fling) : base(m)
        {
            At = at;
            R = r;
            Dur = dur;
            Mult = mult;
            Fling = fling;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float px = Px;
            bool vis = Kit.Visible(At, 0.5f);
            _ringT -= dt;
            if (_ringT <= 0f && vis && Age < Dur)
            {
                _ringT = 0.015f;
                // the funnel, sized to the storm: rings whirling up it, narrow at the ground and wide at the top,
                // the whole column swaying
                float top = (2.4f + R * 0.7f) * K;
                float u = Random.value;
                float rw = Mathf.Lerp(R * 0.16f, R * 0.85f, u);
                int rr = Mathf.Clamp(Mathf.RoundToInt(rw / px), 2, 120);
                Vector2 sway = new Vector2(Mathf.Sin(Age * 3f + u * 2.5f) * R * 0.12f * u, 0f);
                Particle p = Fx.Spawn(Art.Ring(rr, R > 2.5f ? 2 : 1, 0.32f), At + sway + new Vector2(Mathf.Sin(Age * 9f + u * 6f) * rw * 0.12f, u * top), px, 0.22f, FxLayer.Top, 10);
                if (p != null)
                {
                    p.Moving(new Vector2(0f, 3f));
                    p.Color(Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Main, 0.55f), Fx.WithA(Hue.Edge, 0f));
                }
                // earth and debris torn up and swept round it, rising
                if (Random.value < 0.55f)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    float hy = Random.Range(0f, 0.75f) * top;
                    float rx = Mathf.Lerp(R * 0.2f, R * 0.85f, hy / top);
                    Vector2 at = At + sway + new Vector2(Mathf.Cos(a) * rx, hy + Mathf.Sin(a) * rx * 0.32f);
                    Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px, 0.35f, FxLayer.Top, 11);
                    if (c != null)
                    {
                        Color soil = Fx.SoilColor(Combat.TileAt(At));
                        c.Moving(new Vector2(-Mathf.Sin(a), Mathf.Cos(a) * 0.32f) * rx * 6f + new Vector2(0f, 2.5f * K));
                        c.Spin = Random.Range(-600f, 600f);
                        c.Color(Color.Lerp(soil, Color.white, 0.2f), soil, Fx.WithA(soil, 0f));
                    }
                }
                if (Random.value < 0.4f)
                {
                    Vector2 g = At + Random.insideUnitCircle * R * 0.6f;
                    ChoreoFx.HeelDust(g, (g - At).sqrMagnitude > 1e-4f ? Choreo.Perp(g - At) : Vector2.up, px, Combat.TileAt(g), K);
                }
            }
            _pullT -= dt;
            // (its owner dead or its actor reused: it pulls and flings no one any more)
            bool ok = OwnerOk;
            if (_pullT <= 0f && Age < Dur && ok)
            {
                _pullT = 0.1f;
                Choreo.Foes(A, At, R, Near, false, true);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    long id = u.Sid();
                    if (HitIds.Contains(id))
                    {
                        continue;   // it answered the tornado once: spared for the rest of it
                    }
                    if (!Choreo.FairBlow(A, u, M, out float cm, out bool full, true))
                    {
                        continue;
                    }
                    if (!_caught.Contains(id))
                    {
                        if (Combat.Reach(M, u, true) != Contact.Hit)
                        {
                            HitIds.Add(id);
                            continue;
                        }
                        _caught.Add(id);
                        if (full)
                        {
                            _in.Add(u);
                            _inIds.Add(id);
                        }
                    }
                    Combat.Hit(A, u, Mult * 0.1f * cm, false, false, 0f, M);
                    if (!full)
                    {
                        // above the class limits: the wind only buffets it where it stands (never pulled, never flung)
                        Victims.Hurt(u, A, M, Impact.Of(Hurt.Stagger, u.current_position - At, 0f, 0.5f));
                        continue;
                    }
                    Vector2 inward = At - u.current_position;
                    Vector2 swirl = Choreo.Perp(inward.sqrMagnitude > 1e-4f ? inward.normalized : Vector2.right);
                    Victims.Hurt(u, A, M, Impact.Of(Hurt.Reel, inward.normalized * 0.7f + swirl * 0.7f, Mathf.Min(0.6f, inward.magnitude * 0.5f), 0.6f));
                }
            }
            if (Age >= Dur)
            {
                for (int i = 0; ok && i < _in.Count; i++)
                {
                    Actor u = _in[i];
                    // the same unit (pooled actors are reused), still a fair foe (not in the god's hand, not a
                    // friend now, within the class limits: one above them is never flung)
                    if (u == null || u.Sid() != _inIds[i] || !Choreo.FairBlow(A, u, M, out float cm, out bool full, true) || !full)
                    {
                        continue;
                    }
                    Combat.Hit(A, u, Mult * 0.4f * cm, false, false, 0f, M);
                    Vector2 away = u.current_position - At;
                    Impact im = Impact.Of(Hurt.Launch, away.sqrMagnitude > 1e-4f ? away : Random.insideUnitCircle, Fling, 1.4f);
                    im.Lift = 2.5f + Fling * 0.4f;
                    im.Owed = MeteorSlam.AtDeathsDoor(u);
                    Victims.Hurt(u, A, M, im);
                }
                _in.Clear();
                _inIds.Clear();
                if (M != null && !M.Ended)
                {
                    M.SetLink(Link.Scattered, null);
                }
                return false;
            }
            return true;
        }
    }

    // ------------------------------------------------------------------ rain --

    /// <summary>What falls from the sky in a Rain.</summary>
    internal enum RainKind
    {
        Swords,
        Light,
        Ki,
        Meteors,
        /// <summary>A hunter's arrows: thin shafts on a slant, left standing where they struck.</summary>
        Arrows,
        /// <summary>Javelins: long shafts on a slant, left standing.</summary>
        Spears,
        /// <summary>Fists of pressure punched down out of the sky: each a print and a ring of force.</summary>
        Fists,
        /// <summary>Blades of shadow out of the dark: they stand a moment where they struck.</summary>
        Shadows
    }

    /// <summary>
    /// 'Count' things fall over 'Dur' seconds within 'R' tiles of 'At' (onto its enemies first, then
    /// anywhere): each a shadow growing, a streak falling, an impact (a small blast of 'Blast' tiles
    /// hurting its enemies x 'Mult', the ground broken at the move's Scale).
    /// </summary>
    internal sealed class RainHazard : Hazard
    {
        public Vector2 At;
        public float R, Dur, Mult, Blast;
        public int Count;
        public RainKind Kind;

        private sealed class Drop
        {
            public Vector2 At;
            public float T, Fall;
            public bool Down;
            public Particle Shadow, Body;
            /// <summary>The particles' generations when spawned: a handle is only good while it is that same particle.</summary>
            public int ShadowGen, BodyGen;
        }

        /// <summary>Kills 'p' only if it is still the particle it was ('gen'): a faded one may belong to another effect now.</summary>
        private static void KillIf(Particle p, int gen)
        {
            if (p != null && p.Alive && p.Gen == gen)
            {
                p.Kill();
            }
        }

        private readonly List<Drop> _drops = new List<Drop>();
        private int _made;
        private float _next;
        /// <summary>Arrows and javelins come in on a slant, all from one side.</summary>
        private readonly float _slant;

        /// <summary>Does what fell stay standing in the ground a moment (a sword, an arrow, a javelin, a shadow blade)?</summary>
        private bool Planted => Kind == RainKind.Swords || Kind == RainKind.Arrows || Kind == RainKind.Spears || Kind == RainKind.Shadows;

        public RainHazard(ChoreoMove m, Vector2 at, float r, int count, float dur, RainKind kind, float mult, float blast) : base(m)
        {
            At = at;
            R = r;
            Count = Mathf.Clamp(count, 1, 300);
            Dur = Mathf.Max(0.1f, dur);
            Kind = kind;
            Mult = mult;
            Blast = blast;
            _slant = kind == RainKind.Arrows || kind == RainKind.Spears ? (Random.value < 0.5f ? -0.45f : 0.45f) : 0f;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            _next -= dt;
            while (_made < Count && _next <= 0f && Age <= Dur)
            {
                _next += Dur / Count;
                _made++;
                Vector2 p = Pick();
                _drops.Add(new Drop { At = p, Fall = Kind == RainKind.Meteors ? 0.4f : Kind == RainKind.Arrows ? 0.15f : 0.22f });
            }
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                Drop d = _drops[i];
                d.T += dt;
                float k = Mathf.Clamp01(d.T / d.Fall);
                bool vis = Kit.Visible(d.At, 0.3f);
                if (vis)
                {
                    RenderDrop(d, k);
                }
                if (k >= 1f && !d.Down)
                {
                    d.Down = true;
                    Impact(d, vis);
                }
                if (d.Down && d.T > d.Fall + (Planted ? 0.6f : 0.05f))
                {
                    KillIf(d.Shadow, d.ShadowGen);
                    KillIf(d.Body, d.BodyGen);
                    _drops.RemoveAt(i);
                }
            }
            return _made < Count || _drops.Count > 0;
        }

        private Vector2 Pick()
        {
            Choreo.Foes(A, At, R, Near);
            if (Near.Count > 0 && Random.value < 0.7f)
            {
                Actor u = Near[Random.Range(0, Near.Count)];
                return u.current_position + Random.insideUnitCircle * 0.3f;
            }
            Vector2 p = At + Random.insideUnitCircle * R;
            return Combat.Clamp(p);
        }

        private void RenderDrop(Drop d, float k)
        {
            float px = Px;
            if (d.Shadow == null)
            {
                d.Shadow = Fx.Spawn(Art.Disk(Mathf.RoundToInt(3f * K), Art.Ground, 0.4f), d.At, px, d.Fall + 0.1f, FxLayer.Ground, 3);
                if (d.Shadow != null)
                {
                    d.ShadowGen = d.Shadow.Gen;
                    d.Shadow.Sized(0.3f, 1f).Color(new Color(0f, 0f, 0f, 0.1f), new Color(0f, 0f, 0f, 0.4f));
                }
            }
            float h = (1f - k * k) * 6f * K;
            if (d.Down)
            {
                if (!Planted)
                {
                    return;   // the body died in the blast (Impact): only a planted blade or shaft stays
                }
                h = 0f;
            }
            if (d.Body == null || !d.Body.Alive || d.Body.Gen != d.BodyGen)
            {
                Sprite s;
                switch (Kind)
                {
                    case RainKind.Swords:
                        s = ChoreoArt.LightSword(Mathf.RoundToInt(8f * K));
                        break;
                    case RainKind.Meteors:
                        s = ChoreoArt.Boulder(Mathf.RoundToInt(3f * K), Random.Range(0, 4));
                        break;
                    case RainKind.Light:
                        s = Art.Pixel;
                        break;
                    case RainKind.Arrows:
                        s = ChoreoArt.Smear(Mathf.RoundToInt(6f * K), 0, 1)[0];
                        break;
                    case RainKind.Spears:
                        s = ChoreoArt.Smear(Mathf.RoundToInt(11f * K), 1, 2)[0];
                        break;
                    case RainKind.Fists:
                        // big enough to read as a fist (curled fingers, knuckles, thumb), not a pebble
                        s = ChoreoArt.Fist(Mathf.RoundToInt(13f * K));
                        break;
                    case RainKind.Shadows:
                        s = ChoreoArt.Spike(3, Mathf.RoundToInt(7f * K));
                        break;
                    default:
                        s = Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.5f * K)), 1f, 0.2f);
                        break;
                }
                d.Body = Fx.Spawn(s, d.At + new Vector2(0f, h), px, 2f, FxLayer.Top, 12);
                if (d.Body != null)
                {
                    d.Body.Held = true;
                    d.BodyGen = d.Body.Gen;
                }
            }
            if (d.Body == null)
            {
                return;
            }
            d.Body.Grad = null;
            switch (Kind)
            {
                case RainKind.Light:
                    // a pillar of light from the sky down to the spot, thinning as it lands
                    d.Body.Pos = d.At + new Vector2(0f, 3.5f * K);
                    d.Body.SX = Mathf.Lerp(1f, 4f, k) * K;
                    d.Body.SY = 7f * K / px;
                    d.Body.R.color = Fx.WithA(Hue.Core, Mathf.Lerp(0.2f, 0.9f, k));
                    break;
                case RainKind.Meteors:
                    d.Body.Pos = d.At + new Vector2(-h * 0.3f, h);
                    d.Body.Rot += 6f;
                    d.Body.R.color = Color.Lerp(Kit.Fire1, Kit.Fire2, k);
                    if (!d.Down && Random.value < 0.6f)
                    {
                        Kit.Ember(d.Body.Pos, px, 0.3f, new Vector2(0f, 6f * px), Kit.Fire2);
                    }
                    break;
                case RainKind.Arrows:
                case RainKind.Spears:
                    // (drawn from the point: the point is what reaches the ground)
                    d.Body.Pos = d.At + new Vector2(h * _slant, h);
                    d.Body.Rot = Mathf.Atan2(-1f, -_slant) * Mathf.Rad2Deg;
                    d.Body.R.color = Kind == RainKind.Arrows ? Color.Lerp(new Color(0.86f, 0.8f, 0.68f, 1f), Hue.Core, 0.25f) : Color.Lerp(new Color(0.66f, 0.48f, 0.3f, 1f), Hue.Main, 0.15f);
                    break;
                case RainKind.Fists:
                    // a fist of pressure driven down out of the sky, knuckles first, glowing with the force
                    // behind it, its afterimages trailing up behind it
                    d.Body.Pos = d.At + new Vector2(0f, h + 6.5f * K * px);
                    d.Body.Rot = -90f;
                    d.Body.R.color = Color.Lerp(Hue.Core, Color.white, 0.55f);
                    if (!d.Down && Kit.Visible(d.Body.Pos, 0.3f))
                    {
                        Spec.Blob(d.Body.Pos, 1.05f * K, px, 0.05f, FxLayer.Top, 11, 1f, 0.6f, Fx.WithA(Hue.Edge, 0.5f));
                        for (int g = 1; g <= 3; g++)
                        {
                            Particle e = Fx.Spawn(d.Body.R.sprite, d.Body.Pos + new Vector2(0f, g * 1.2f * K * (0.4f + 0.6f * (1f - k))), px, 0.05f, FxLayer.Top, 10);
                            if (e != null)
                            {
                                e.Rot = -90f;
                                e.Color(Fx.WithA(Color.Lerp(Hue.Edge, Hue.Core, 0.3f), 0.55f / g));
                            }
                        }
                    }
                    break;
                case RainKind.Shadows:
                    d.Body.Pos = d.At + new Vector2(0f, h + 7f * K * px);
                    d.Body.Rot = 180f;
                    // (the school's own colour, deepened: violet-black blades with a violet streak)
                    d.Body.R.color = Color.Lerp(Hue.Main, Color.black, 0.55f);
                    if (!d.Down)
                    {
                        Kit.Line(d.Body.Pos, d.Body.Pos + new Vector2(0f, 3f * K * (1f - k) + 0.3f), px, 0.07f, 1, FxLayer.Top, 11, Fx.WithA(Hue.Main, 0.85f), Fx.WithA(Hue.Edge, 0f));
                    }
                    break;
                default:
                    d.Body.Pos = d.At + new Vector2(0f, h);
                    d.Body.R.color = Kind == RainKind.Swords ? Color.Lerp(Hue.Core, Hue.Main, 0.3f) : Hue.Core;
                    if (!d.Down)
                    {
                        Kit.Line(d.Body.Pos, d.Body.Pos + new Vector2(0f, 3f * K * (1f - k) + 0.3f), px, 0.05f, 1, FxLayer.Top, 11, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                    }
                    break;
            }
        }

        private void Impact(Drop d, bool vis)
        {
            WorldTile t = Combat.TileAt(d.At);
            if (vis)
            {
                Kit.Flash(d.At, Px, Mathf.RoundToInt(4f * K), 0.07f, Fx.WithA(Hue.Core, 0.95f));
                Fx.Ring(d.At, Px, 0.22f, 2f, Blast / Px, 1, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.DustRing(d.At, Px, t, 5, 26f, 0.35f, K);
                if (Kind == RainKind.Meteors)
                {
                    Kit.Debris(d.At, Px, t, 6, K);
                    Kit.Crater(d.At, Px * K, t, true, 5f, true, Kit.Fire2);
                }
                else if (Kind == RainKind.Fists)
                {
                    Fx.Ring(d.At, Px, 0.3f, 2f, Blast / Px * 1.5f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Debris(d.At, Px, t, 4, K);
                    Kit.Crater(d.At, Px * K * 0.8f, t, false, 4f, true, Hue.Main);
                }
                else if (Kind == RainKind.Shadows)
                {
                    Color shade = Color.Lerp(Hue.Main, Color.black, 0.5f);
                    Spec.Blob(d.At + new Vector2(0f, 0.3f * K), 0.6f * K, Px, 0.45f, FxLayer.Top, 13, 1f, 0.5f, Fx.WithA(shade, 0.85f), Fx.WithA(Hue.Main, 0.45f), Fx.WithA(shade, 0f));
                }
            }
            if (d.Body != null && !Planted)
            {
                KillIf(d.Body, d.BodyGen);
                d.Body = null;
            }
            Choreo.Sound(Kind == RainKind.Meteors ? "event:/SFX/EXPLOSIONS/ExplosionSmall" : "event:/SFX/HIT/HitGeneric", t, 0.06f);
            Choreo.Foes(A, d.At, Blast, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                HitIds.Remove(Near[i].Sid());   // (each drop hits anew)
                Hurt hurt = Kind == RainKind.Swords || Kind == RainKind.Arrows || Kind == RainKind.Spears ? Hurt.Fold : Kind == RainKind.Shadows ? Hurt.Crumple : Hurt.Knockdown;
                Strike(Near[i], Mult, hurt, d.At, 0.3f, -1f, 0.9f);
            }
            if ((Kind == RainKind.Meteors || Kind == RainKind.Light || Kind == RainKind.Fists) && OwnerHere)
            {
                Ruin.Blast(A, d.At, Blast, Scale, A.isAlive() ? A.current_position : (Vector2?)null, Kind == RainKind.Meteors);
            }
        }

        public override void Kill()
        {
            foreach (Drop d in _drops)
            {
                KillIf(d.Shadow, d.ShadowGen);
                KillIf(d.Body, d.BodyGen);
            }
            _drops.Clear();
        }
    }

    internal partial class ChoreoMove
    {
        /// <summary>A shockwave racing out from its feet (or 'at') to 'r' tiles at 'speed' tiles/s, throwing everyone it passes ('hurt', 'push').</summary>
        public ChoreoMove Shockwave(float r, float mult, Hurt hurt = Hurt.Knockdown, float push = 0.8f, float speed = 14f, System.Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new ShockwaveHazard(m, at != null ? at(m) : m.Pose.Ground, r * Mathf.Lerp(1f, m.Kk, 0.5f), speed, mult, hurt, push)));
        }

        /// <summary>A dome of light swelling round it to 'r' tiles over 'grow' seconds and bursting: everyone inside flung away.</summary>
        public ChoreoMove Dome(float r, float mult, float grow = 0.35f, float push = 3f, System.Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new DomeHazard(m, at != null ? at(m) : m.Pose.Ground, r * Mathf.Lerp(1f, m.Kk, 0.5f), grow, mult, push)));
        }

        /// <summary>The ground quakes ('r' tiles, 'cracks' fissures running out): everyone standing in it bounces and falls.</summary>
        public ChoreoMove Quake(float r, float mult, int cracks = 6, System.Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new QuakeHazard(m, at != null ? at(m) : m.Pose.Ground, r * Mathf.Lerp(1f, m.Kk, 0.5f), mult, cracks)));
        }

        /// <summary>A stone pillar (or a spike) erupts under 'at' (default: the target) after 'delay', launching whoever stands there 'lift' tiles up.</summary>
        public ChoreoMove Pillar(float mult, float height = 1.6f, float lift = 2.2f, float delay = 0.18f, bool spike = false, System.Func<ChoreoMove, Vector2> at = null)
        {
            return Do(m => Areas.Add(new PillarHazard(m, at != null ? at(m) : (m.V != null ? m.VG : m.Point), delay, height * m.Kk, mult, lift, spike)));
        }

        /// <summary>A fan of fists (or blades) out of it along its facing: 'half' degrees each side, 'len' tiles, for 'dur' seconds.</summary>
        public ChoreoMove Cone(float mult, float half = 45f, float len = 4f, float dur = 0.5f, float push = 1.2f, bool blades = false)
        {
            return Do(m => Areas.Add(new ConeHazard(m, m.Hand(Limb.Fist), m.Dir, half, len * Mathf.Lerp(1f, m.Kk, 0.5f), dur, mult, push, blades)));
        }

        /// <summary>A tornado at its feet (or 'at'): 'r' tiles, 'dur' seconds, flinging those it caught 'fling' tiles at the end.</summary>
        public ChoreoMove Tornado(float r, float mult, float dur = 1f, float fling = 3f, System.Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new TornadoHazard(m, at != null ? at(m) : m.Pose.Ground, r * Mathf.Lerp(1f, m.Kk, 0.5f), dur, mult, fling)));
        }

        /// <summary>A rain from the sky over 'r' tiles round the target (or 'at'): 'count' drops in 'dur' seconds, each a blast of 'blast' tiles.</summary>
        public ChoreoMove Rain(RainKind kind, int count, float r, float mult, float dur = 1f, float blast = 0.8f, System.Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new RainHazard(m, at != null ? at(m) : (m.V != null ? m.VG : m.Point), r, count, dur, kind, mult, blast)));
        }
    }
}
