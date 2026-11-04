using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GENESIS FIST (Divine, God). A punch that creates. A golden blur to the foe, then the God
    /// plants and draws its fist back to the hip while the earth gives up its power: pebbles and
    /// motes of green-gold light rise out of the ground and stream into the fist (Charging: a fast
    /// blow now breaks it, and the world dims around it at III). The straight lands on an impact
    /// frame; the foe is blasted away, and where it stood the land erupts upward: spires of rock
    /// burst out of the ground in a ring round a towering peak, throwing everyone near up and out,
    /// and when the dust falls a new hill stands there (Environment damage at full: a mountain
    /// with a summit at III, rising even out of shallow water; every building on it is broken; no
    /// ally or the God itself is ever left standing on a peak). I: a mound and the hills, II: a
    /// mountain core, III: a summit, a wider ring of spires, the cinematic.
    /// Damage (x the user's damage): the punch I 2.8, II 3.6, III 4.6; the eruption x0.9 / 1.2 /
    /// 1.6 on everyone else in it. Planner-driven (not the gauge): a single, worthy target.
    /// Shared helpers of the eight divine techniques: DivineArts (below).
    /// </summary>
    internal sealed class GenesisFistTech : Technique
    {
        public override string Id => "genesis_fist";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Single;
        public override Form Forms => Form.Punch | Form.Straight | Form.Ground | Form.Charge | Form.Heavy | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 120f, 170f, 230f };
        public override float[] Cooldown => new[] { 80f, 90f, 100f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Punch(int level)
        {
            return level <= 1 ? 2.8f : level == 2 ? 3.6f : 4.6f;
        }

        public static float Eruption(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.2f : 1.6f;
        }

        /// <summary>The eruption's reach (tiles, x the user's size).</summary>
        public static float Radius(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 2.8f : 3.4f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 4.5f, Total = Punch(1), Crowd = 0f, Alone = 6f, IntoGuard = 0.9f, Worth = 40f },
            new MeleeWant { Reach = 5.5f, Total = Punch(2), Crowd = 0f, Alone = 8f, IntoGuard = 0.9f, Worth = 60f },
            new MeleeWant { Reach = 6.5f, Total = Punch(3), Crowd = 0f, Alone = 10f, IntoGuard = 0.9f, Worth = 80f }
        };

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self))
            {
                return null;
            }
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            float r = Radius(level) * Choreo.KOf(c.Self);
            _near.Clear();
            int n = c.CountNear(v.current_position, r, _near);
            if (!DivineArts.Warranted(c, v, n, DivineArts.ThreatOf(c, _near)))
            {
                return null;
            }
            // the eruption throws everyone round it too
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == v)
                {
                    continue;
                }
                c.Allowed(u, out float cm);
                p.Score += c.Value(u, c.Expected(u, Eruption(level)), cm);
            }
            // a peak rising under our own people's feet is not the moment
            if (c.AlliesNear(v.current_position, r * 0.6f) > 0)
            {
                p.Score *= 0.5f;
            }
            p.Score *= DivineArts.Affinity(c.Self, Id);
            p.Count = Mathf.Max(1, n);
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            GenesisGather st = new GenesisGather();
            if (Vector2.Distance(m.Stance, m.VG) > 2.4f * m.Kk)
            {
                m.Dash();
            }
            else
            {
                m.StepIn();
            }
            // the earth's power drawn into the fist (Charging all along)
            m.Charge(level <= 1 ? 0.45f : level == 2 ? 0.55f : 0.7f, ChargeStyle.Fist, 1.6f + 0.5f * level, Element.Holy)
                .OnEnter(mm =>
                {
                    if (mm.Level >= 2)
                    {
                        DivineArts.Open(mm, "genesis_fist", 0.35f, 0.55f, 0.4f);
                    }
                })
                .Every((mm, k) => st.Tick(mm, k));
            m.Strike(new Hit(Blow.Cross, Punch(level))
            {
                Hurt = Hurt.Launch,
                Push = 3f + 1.2f * level,
                Lift = 1.4f + 0.5f * level,
                Power = 1.5f + 0.2f * level,
                Finisher = true,
                Charged = true,
                WindScale = 1.6f,
                OnLand = (mm, v, r) => Erupt(mm, v)
            });
            // the fist held out while the land rises
            m.Hold(level >= 3 ? 0.3f : 0.2f, false, Tempo.Impact);
            m.Recover(0.32f);
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }

        /// <summary>
        /// The blow landed: the foe flies, and the land rises where it comes down (a giant that can't be
        /// thrown: under it, a step beyond, the God's own ground kept low).
        /// </summary>
        private static void Erupt(ChoreoMove m, Actor v)
        {
            m.SpendOrb();
            if (m.Cinematic)
            {
                m.Freeze(0.1f);
                m.EndCinematic();
            }
            int level = m.Level;
            bool flies = Victims.Movable(v, m.A) && !Weights.Heavy(v);
            float push = (3f + 1.2f * level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            float r = Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            Vector2 from = Victims.GroundOf(v);
            Vector2 at = flies ? from + m.Dir * push * 0.9f : from + m.Dir * r * 0.5f;
            at = Combat.Clamp(at);
            // (never round the God's own feet)
            Vector2 off = at - m.Pose.Ground;
            if (off.magnitude < r + 0.6f)
            {
                at = Combat.Clamp(m.Pose.Ground + (off.sqrMagnitude > 1e-4f ? off.normalized : m.Dir) * (r + 0.6f));
            }
            Areas.Add(new GenesisHazard(m, at, level, v, flies ? 0.95f : 0.1f));
        }

        /// <summary>The gathering: pebbles and motes rising out of the ground into the cocked fist.</summary>
        private sealed class GenesisGather
        {
            private float _last, _t, _crack;

            public void Tick(ChoreoMove m, float k)
            {
                float dt = m.BeatT - _last;
                if (dt < 0f)
                {
                    dt = m.BeatT;
                }
                _last = m.BeatT;
                if (!m.Vis)
                {
                    return;
                }
                float px = m.Px;
                Vector2 fist = m.OrbAt();
                Hue holy = m.Hue.As(Element.Holy);
                _t -= dt;
                if (_t <= 0f)
                {
                    _t = Mathf.Lerp(0.05f, 0.022f, k);
                    int n = Fx.Count(m.Level >= 3 ? 3 : 2);
                    for (int i = 0; i < n; i++)
                    {
                        float ang = Random.value * Mathf.PI * 2f;
                        float d = Random.Range(1.2f, 2.6f) * m.Kk;
                        Vector2 from = m.Pose.Ground + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * d;
                        float life = Random.Range(0.22f, 0.34f);
                        bool pebble = Random.value < 0.45f;
                        Particle p = pebble
                            ? Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], from, px, life, m.TopLayer, 6)
                            : Fx.Spawn(Art.Pixel, from, px, life, m.TopLayer, 7);
                        if (p == null)
                        {
                            break;
                        }
                        p.Moving((fist - from) / life);
                        if (pebble)
                        {
                            Color soil = Fx.SoilColor(Combat.TileAt(from));
                            p.Spin = Random.Range(-400f, 400f);
                            p.Color(soil, Color.Lerp(soil, holy.Core, 0.6f), Fx.WithA(holy.Core, 0f));
                        }
                        else
                        {
                            Color life2 = Random.value < 0.5f ? DivineArts.Verdant : holy.Core;
                            p.Sized(1.6f, 0.7f).Color(Fx.WithA(life2, 0f), Fx.WithA(life2, 1f), Fx.WithA(holy.Edge, 0.5f));
                        }
                    }
                }
                _crack -= dt;
                if (_crack <= 0f && m.Level >= 2)
                {
                    _crack = 0.2f;
                    Particle c = Fx.Spawn(Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)].Glow, m.Pose.Ground, px * m.Kk, 0.35f, FxLayer.Ground, 4);
                    c?.Color(Fx.WithA(holy.Core, 0.9f), Fx.WithA(DivineArts.Verdant, 0.5f), Fx.WithA(holy.Edge, 0f));
                }
            }
        }
    }

    /// <summary>
    /// Genesis Fist's eruption: spires of rock bursting out of the ground round a towering peak,
    /// everyone of the God's enemies in it thrown up and out, and (Environment damage at full) the
    /// land itself raised: a mountain core, hills round it, soil lifted round those.
    /// </summary>
    internal sealed class GenesisHazard : Hazard
    {
        private struct Spire
        {
            public Vector2 Off;
            public float H, Delay;
            public int W;
            public Particle P, Glow;
        }

        private readonly Vector2 _at, _keep;
        private readonly int _level;
        private readonly float _r, _rHill, _rCore;
        private readonly long _skip;
        private readonly Spire[] _spires;
        private readonly Color _stone;
        private bool _hit, _raised, _drawn, _started;
        private const float Stand = 1.35f;
        // the wait for the thrown foe to come down (at most _wait), the glow that warns of the rising
        private readonly Actor _victim;
        private readonly float _wait;
        private float _pre, _warnT;

        public GenesisHazard(ChoreoMove m, Vector2 at, int level, Actor skip, float wait) : base(m)
        {
            _victim = skip;
            _wait = wait;
            _at = at;
            _keep = m.A.current_position;
            _level = Mathf.Clamp(level, 1, 3);
            float k = Mathf.Lerp(1f, m.Kk, 0.5f);
            _r = GenesisFistTech.Radius(_level) * k;
            _rHill = (_level <= 1 ? 1.2f : _level == 2 ? 1.8f : 2.4f) * k;
            _rCore = (_level <= 1 ? 0f : _level == 2 ? 0.9f : 1.35f) * k;
            _skip = skip != null ? skip.Sid() : -1;
            _stone = Color.Lerp(Fx.SoilColor(Combat.TileAt(at)), new Color(0.6f, 0.56f, 0.52f, 1f), 0.45f);
            int n = _level <= 1 ? 5 : _level == 2 ? 8 : 12;
            _spires = new Spire[n];
            float peak = (_level <= 1 ? 1.3f : _level == 2 ? 2f : 2.7f) * k;
            float a0 = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                Spire s = new Spire();
                if (i == 0)
                {
                    s.Off = Vector2.zero;
                    s.H = peak;
                    s.W = Mathf.RoundToInt((_level >= 3 ? 16f : 12f) * k);
                    s.Delay = 0f;
                }
                else
                {
                    float ang = (a0 + i * 360f / (n - 1) + Random.Range(-14f, 14f)) * Mathf.Deg2Rad;
                    float d = _r * Random.Range(i % 2 == 0 ? 0.35f : 0.55f, i % 2 == 0 ? 0.55f : 0.85f);
                    s.Off = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * d;
                    s.H = peak * Mathf.Lerp(0.75f, 0.3f, d / _r) * Random.Range(0.85f, 1.1f);
                    s.W = Mathf.RoundToInt(Random.Range(5f, 9f) * k);
                    s.Delay = 0.03f + 0.11f * d / _r;
                }
                _spires[i] = s;
            }
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(at), 0.3f);
        }

        /// <summary>Has the thrown foe come down (or is it gone)?</summary>
        private bool Landed()
        {
            Actor v = _victim;
            return v == null || v.Sid() != _skip || !v.isAlive() || _pre > 0.15f && Combat.HeightOf(v) < 0.3f && !v.under_forces;
        }

        public override bool Tick(float dt)
        {
            bool vis = Kit.Visible(_at, 0.6f);
            if (!_started)
            {
                // the ground glows and trembles where the foe will come down
                _pre += dt;
                _warnT -= dt;
                if (vis && _warnT <= 0f)
                {
                    _warnT = 0.12f;
                    Particle c = Fx.Spawn(Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)].Glow, _at + Random.insideUnitCircle * _r * 0.3f, Px * K * 1.4f, 0.3f, FxLayer.Ground, 4);
                    if (c != null)
                    {
                        c.Rot = Random.Range(0f, 360f);
                        c.Color(Fx.WithA(Hue.Core, 0.9f), Fx.WithA(DivineArts.Verdant, 0.6f), Fx.WithA(Hue.Edge, 0f));
                    }
                    ChoreoFx.HeelDust(_at + Random.insideUnitCircle * _r * 0.5f, Random.insideUnitCircle, Px, Combat.TileAt(_at), K);
                }
                if (_pre < _wait && !Landed())
                {
                    return true;
                }
                _started = true;
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", Combat.TileAt(_at), 0.2f);
            }
            Age += dt;
            if (!_hit && Age >= 0.03f)
            {
                _hit = true;
                Burst(vis);
                if (OwnerOk)
                {
                    HitAll();
                }
            }
            if (!_raised && Age >= 0.24f)
            {
                _raised = true;
                if (A != null)
                {
                    // the shock breaks what stands round it (no fire: this blow makes, it doesn't burn)
                    Ruin.Blast(A, _at, _r / Ruin.Radius(Tiers.Of(A), Scale.Ultimate, 1f), Scale, _keep, false, -2);
                    Raise();
                    Ruin.Quake(A, _at, Scale, 1.3f, K);
                }
                if (vis)
                {
                    Kit.DustRing(_at, Px, Combat.TileAt(_at), 18, 60f, 0.8f, K * 1.4f);
                    Kit.Debris(_at, Px, Combat.TileAt(_at), 14, K * 1.3f);
                }
            }
            if (vis)
            {
                DrawSpires();
            }
            else if (_drawn)
            {
                Kill();
            }
            return Age < Stand + 0.5f;
        }

        private void Burst(bool vis)
        {
            if (!vis)
            {
                return;
            }
            WorldTile t = Combat.TileAt(_at);
            Kit.Flash(_at + new Vector2(0f, 0.4f * K), Px, Mathf.RoundToInt(10f * K), 0.12f, Fx.WithA(Hue.Core, 0.95f));
            Fx.Ring(_at, Px, 0.45f, 4f, _r * 1.3f / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1.8f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(DivineArts.Verdant, 0.7f), Fx.WithA(Hue.Rim, 0f));
            for (int i = 0; i < 3; i++)
            {
                Particle c = Fx.Spawn(Art.Cracks[Random.Range(0, Art.Cracks.Length)].Glow, _at, Px * K * (1f + 0.25f * _level), 1.1f, FxLayer.Ground, 4);
                if (c != null)
                {
                    c.Rot = Random.Range(0f, 360f);
                    c.Color(Fx.WithA(Hue.Core, 1f), Fx.WithA(DivineArts.Verdant, 0.7f), Fx.WithA(Hue.Edge, 0f));
                }
            }
            Kit.Debris(_at, Px, t, 10, K * 1.6f);
            Kit.DustRing(_at, Px, t, 12, 46f, 0.6f, K);
            Kit.SpeedLines(_at + new Vector2(0f, 1.2f * K), Vector2.up, Px, 6, 10f, 0.25f, Fx.WithA(Hue.Core, 0.85f));
        }

        private void HitAll()
        {
            Choreo.Foes(A, _at, _r * 1.1f, Near, true);
            float mult = GenesisFistTech.Eruption(_level);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Mathf.Clamp01(Vector2.Distance(u.current_position, _at) / Mathf.Max(0.1f, _r));
                // (the foe the fist threw is thrown up again by the land it lands on, at half)
                float k = u.Sid() == _skip ? 0.5f : 1f;
                Strike(u, k * mult * Mathf.Lerp(1f, 0.65f, d), Hurt.Launch, _at, Mathf.Lerp(2.6f, 1.4f, d), Mathf.Lerp(3.2f, 1.8f, d), 1.4f);
            }
        }

        private void DrawSpires()
        {
            _drawn = true;
            for (int i = 0; i < _spires.Length; i++)
            {
                Spire s = _spires[i];
                float t = Age - s.Delay;
                if (t < 0f)
                {
                    continue;
                }
                float rise = Beat.Apply(Ease.OutBack, Mathf.Clamp01(t / 0.2f));
                float sink = Mathf.Clamp01((Age - Stand - s.Delay * 0.5f) / 0.4f);
                float sy = Mathf.Max(0f, rise * (1f - sink));
                Vector2 pos = _at + s.Off;
                int h = Mathf.Clamp(Mathf.RoundToInt(s.H / Px), 6, 120);
                if (s.P == null || !s.P.Alive)
                {
                    s.P = Fx.Spawn(ChoreoArt.Spike(s.W, h), pos, Px, 1f, FxLayer.Behind, 30);
                    if (s.P != null)
                    {
                        s.P.Held = true;
                        if (i > 0 || _level >= 2)
                        {
                            Kit.Debris(pos, Px, Combat.TileAt(pos), 3, K * 0.8f);
                        }
                    }
                    s.Glow = Fx.Spawn(ChoreoArt.Spike(s.W, h), pos, Px, 1f, FxLayer.Behind, 31, true);
                    if (s.Glow != null)
                    {
                        s.Glow.Held = true;
                    }
                }
                int order = 20 + Mathf.RoundToInt(-pos.y * 0.1f);
                if (s.P != null)
                {
                    s.P.Pos = pos + new Vector2(0f, -0.5f * Px);
                    s.P.SY = sy;
                    s.P.Grad = null;
                    s.P.R.color = _stone;
                    s.P.R.sortingOrder = order;
                }
                if (s.Glow != null)
                {
                    float g = Mathf.Clamp01(1f - t / 0.6f);
                    s.Glow.Pos = s.P != null ? s.P.Pos : pos;
                    s.Glow.SY = sy;
                    s.Glow.Grad = null;
                    s.Glow.R.color = Fx.WithA(Color.Lerp(Hue.Core, DivineArts.Verdant, 0.3f), 0.85f * g);
                    s.Glow.R.sortingOrder = order + 1;
                }
                if (sink > 0f && sink < 1f && Random.value < 0.35f)
                {
                    Kit.Debris(pos, Px, Combat.TileAt(pos), 1, K * 0.6f, s.H * sy * 0.6f);
                }
                _spires[i] = s;
            }
        }

        // ------------------------------------------------------------ the land --

        /// <summary>Raises the ground (never lowers it): a mountain core, hills round it, the soil round those lifted a step.</summary>
        private void Raise()
        {
            if (Settings.Destruction < 2 || World.world == null)
            {
                return;
            }
            WorldTile c = Combat.TileAt(_at);
            if (c == null)
            {
                return;
            }
            int ri = Mathf.CeilToInt(_r);
            // (below Master the land rises round its own people's buildings, never under them: Kit.Unsparing)
            bool all = Kit.Unsparing(A);
            Kingdom own = A != null ? A.kingdom : null;
            try
            {
                for (int dx = -ri; dx <= ri; dx++)
                {
                    for (int dy = -ri; dy <= ri; dy++)
                    {
                        int x = c.x + dx, y = c.y + dy;
                        if (x < 1 || y < 1 || x >= MapBox.width - 1 || y >= MapBox.height - 1)
                        {
                            continue;
                        }
                        Vector2 mid = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Vector2.Distance(mid, _at);
                        if (d > _r)
                        {
                            continue;
                        }
                        WorldTile w = World.world.GetTileSimple(x, y);
                        if (w == null || w.main_type == null || !all && own != null && w.building != null && w.building.kingdom == own)
                        {
                            continue;
                        }
                        int now = Rank(w.main_type);
                        if (now < 0)
                        {
                            continue;   // lava, goo, anything strange: left as it is
                        }
                        int want = d <= _rCore ? (_level >= 3 && d <= 0.55f ? 8 : 7) : d <= _rHill ? 6 : Mathf.Min(5, now + 1);
                        if (want >= 6 && Occupied(mid))
                        {
                            want = 5;   // never a peak under the God or one of its own
                        }
                        if (want <= now || !MapAction.checkTileDamageGaiaCovenant(w, true))
                        {
                            continue;
                        }
                        TileType to = OfRank(want);
                        if (to != null)
                        {
                            MapAction.terraformMain(w, to);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("genesis raise", e);
            }
        }

        private static readonly List<Actor> _friends = new List<Actor>();

        /// <summary>Does the God or one of its own stand on (or right next to) this tile?</summary>
        private bool Occupied(Vector2 mid)
        {
            if ((mid - _keep).sqrMagnitude <= 1.6f * 1.6f || A != null && A.isAlive() && (mid - A.current_position).sqrMagnitude <= 1.6f * 1.6f)
            {
                return true;
            }
            WorldTile t = Combat.TileAt(mid);
            if (t == null || A == null || A.kingdom == null)
            {
                return false;
            }
            foreach (Actor u in Finder.getUnitsFromChunk(t, 1))
            {
                if (u != null && u != A && u.isAlive() && !Combat.IsFoe(A, u) && (u.current_position - mid).sqrMagnitude <= 1.1f * 1.1f)
                {
                    return true;
                }
            }
            return false;
        }

        private static int Rank(TileType t)
        {
            if (t == TileLibrary.summit) return 8;
            if (t == TileLibrary.mountains) return 7;
            if (t == TileLibrary.hills) return 6;
            if (t == TileLibrary.soil_high) return 5;
            if (t == TileLibrary.soil_low) return 4;
            if (t == TileLibrary.sand) return 3;
            if (t == TileLibrary.shallow_waters || t == TileLibrary.pit_shallow_waters) return 2;
            if (t == TileLibrary.close_ocean || t == TileLibrary.pit_close_ocean) return 1;
            if (t == TileLibrary.deep_ocean || t == TileLibrary.pit_deep_ocean) return 0;
            return -1;
        }

        private static TileType OfRank(int r)
        {
            switch (r)
            {
                case 8: return TileLibrary.summit;
                case 7: return TileLibrary.mountains;
                case 6: return TileLibrary.hills;
                case 5: return TileLibrary.soil_high;
                case 4: return TileLibrary.soil_low;
                case 3: return TileLibrary.sand;
                default: return null;
            }
        }

        public override void Kill()
        {
            for (int i = 0; i < _spires.Length; i++)
            {
                _spires[i].P?.Kill();
                _spires[i].Glow?.Kill();
                _spires[i].P = null;
                _spires[i].Glow = null;
            }
            _drawn = false;
        }
    }

    /// <summary>
    /// DIVINE ARTS: what the eight God-only techniques share (Genesis Fist, Judgment Rain, Heaven's
    /// Hand, Avatar Ascendant, Primordial Roar, Eclipse, Pillar of Heaven, Heaven's Gate).
    ///   ONE AT A TIME   a God that has just unleashed one waits Lockout seconds before the next
    ///                   (its other techniques are free): the set pieces breathe between them.
    ///   ATTUNEMENT      every God leans to some of the eight and away from others (stable per
    ///                   unit): two Gods never reach for the same ones in the same way.
    ///   WARRANTED       only against a worthy foe (Master and up, a giant, a real threat), an army
    ///                   that adds up to one, or at its last stand: never on a lone goblin (the
    ///                   Brain's Scale fit shapes the rest).
    ///   CINEMATIC       one at a time, only where the player looks (Cinema), rarer in a god war;
    ///                   a title card the first time each is seen in a while (title cards on).
    /// </summary>
    internal static class DivineArts
    {
        /// <summary>World seconds a God waits after one divine technique before the next.</summary>
        public const float Lockout = 10f;

        /// <summary>The green-gold of life (creation, the world tree).</summary>
        public static readonly Color Verdant = new Color(0.72f, 1f, 0.55f, 1f);

        /// <summary>The white-gold of holy light.</summary>
        public static readonly Color Radiant = new Color(1f, 0.95f, 0.72f, 1f);

        private static readonly Dictionary<long, double> _last = new Dictionary<long, double>();
        private static readonly Dictionary<string, double> _titled = new Dictionary<string, double>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.WorldCleared += () => _last.Clear();
        }

        /// <summary>May this God unleash a divine technique now (its last one was Lockout seconds ago)?</summary>
        public static bool Ready(Actor a)
        {
            return a == null || !_last.TryGetValue(a.Sid(), out double at) || Fighter.Now - at >= Lockout || Fighter.Now < at;
        }

        /// <summary>It just unleashed one.</summary>
        public static void Mark(Actor a)
        {
            if (a == null)
            {
                return;
            }
            if (_last.Count > 256)
            {
                _last.Clear();
            }
            _last[a.Sid()] = Fighter.Now;
        }

        /// <summary>How much this unit is drawn to this technique (0.6 .. 1.4, stable per unit and technique).</summary>
        public static float Affinity(Actor a, string id)
        {
            if (a == null || id == null)
            {
                return 1f;
            }
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < id.Length; i++)
                {
                    h = (h ^ id[i]) * 16777619u;
                }
                long sid = a.Sid();
                h ^= (uint)sid * 2654435761u;
                h ^= (uint)(sid >> 32) * 40503u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                float u = (h & 0xFFFF) / 65535f;
                // a third of them it favours, a fifth it rarely reaches for
                return u < 0.2f ? 0.6f : u > 0.67f ? 1.35f : 1f;
            }
        }

        /// <summary>The threat of all of them together (Ctx.Threat).</summary>
        public static float ThreatOf(Ctx c, List<Actor> list)
        {
            float sum = 0f;
            for (int i = 0; i < list.Count && i < 28; i++)
            {
                sum += c.Threat(list[i]);
            }
            return sum;
        }

        /// <summary>
        /// Does the moment warrant a divine technique: a worthy 'main' foe (Master and up, a giant, a
        /// real threat), 'caught' foes that add up to a threat ('threat') or a horde, or its last stand?
        /// </summary>
        public static bool Warranted(Ctx c, Actor main, int caught, float threat)
        {
            if (c.Forced)
            {
                return true;
            }
            if (main != null && (Tiers.Of(main) >= Tier.Master || Weights.Species(main) >= Weight.Titan || c.Threat(main) >= 0.3f))
            {
                return true;
            }
            if (caught >= 5 && threat >= 0.4f || caught >= 16)
            {
                return true;
            }
            return c.Desperate && c.ThreatAround >= 0.25f;
        }

        /// <summary>
        /// Opens the move's cinematic (full only where the player looks, one at a time, at a sane
        /// speed, cinematics on; rarer in a god war) with a title card the first time this technique
        /// is seen in a while. False: it plays at world speed.
        /// </summary>
        public static bool Open(ChoreoMove m, string id, float dim, float slow = 0f, float slowFor = 0f)
        {
            if (m == null || Settings.Cinematics < 2)
            {
                return false;
            }
            float gap = Godhood.AtWar(m.A) ? 20f : 8f;
            string title = null;
            if (Settings.TitleCards && (!_titled.TryGetValue(id, out double at) || Fighter.Now - at > 90.0 || Fighter.Now < at))
            {
                title = "sakuga_title_" + id;
            }
            if (!m.BeginCinematic(gap, dim, title, slow, slowFor))
            {
                return false;
            }
            if (title != null)
            {
                _titled[id] = Fighter.Now;
            }
            return true;
        }

        /// <summary>
        /// A divine blow on 'u' from move 'm' (its first contact: 'u' may evade an area blow, never parry
        /// it): x 'mult' (the class limits' share applied), then the reaction 'im'. A blow that would kill
        /// leaves it at death's door and it dies where it comes to rest. Only the God's enemies.
        /// </summary>
        public static HitResult Smite(ChoreoMove m, Actor u, float mult, Impact im, bool area = true)
        {
            HitResult r = default;
            if (m == null || u == null || !Combat.Living(u) || !Choreo.Fair(m.A, u, out float cm, true))
            {
                return r;
            }
            if (!m.Touch(u, area))
            {
                return r;
            }
            r = Combat.Hit(m.A, u, mult * cm, false, false, 0f, m);
            im.Owed = MeteorSlam.AtDeathsDoor(u);
            if (Combat.Living(u) || im.Owed)
            {
                Victims.Hurt(u, m.A, m, im);
            }
            return r;
        }
    }
}
