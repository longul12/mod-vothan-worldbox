using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BLADE RAIN (Thousand Blades, God). The God raises its blade to the sky and a shaft of light
    /// runs up from it; a ring of light opens over the field. Then the swords come: hundreds of
    /// swords of light falling slantwise like rain in a gale, the downpour sweeping across the field
    /// from the God's side to the far side, each one a shadow growing, a streak, and a sword standing
    /// in the ground where it struck: the foes it finds are skewered where they stand. When the last
    /// has fallen the whole field of standing swords flashes white at once and shatters into light,
    /// knocking down whoever is still among them. The ground is pocked by every sword, and a building
    /// they rain on is cut to pieces.
    /// I: 80 swords over 10 tiles; II: 120 over 12; III: 160 over 14 (Gods use it at III).
    /// Damage (x the user's damage): each sword I 0.35, II 0.42, III 0.5, at most 5/6/8 per foe;
    /// the shattering x0.6 / 0.8 / 1.
    /// Distinct from Heaven's Gate (a gate of every school's weapons) and Judgment Rain (pillars):
    /// slanting swords sweeping across like a squall, a field of blades that stays, then shatters.
    /// </summary>
    internal sealed class BladeRainTech : Technique
    {
        public override string Id => "blade_rain";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Area | Form.Projectile;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 150f, 190f, 240f };
        public override float[] Cooldown => new[] { 40f, 45f, 50f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Thrown;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 6f : 7f;
        }

        public static int Swords(int level)
        {
            return level <= 1 ? 80 : level == 2 ? 120 : 160;
        }

        public static float SwordMult(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.42f : 0.5f;
        }

        public static int MaxHits(int level)
        {
            return level <= 1 ? 5 : level == 2 ? 6 : 8;
        }

        public static float Shatter(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.8f : 1f;
        }

        private static float Total(int level)
        {
            return SwordMult(level) * MaxHits(level) * 0.7f + Shatter(level);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float r = Radius(level);
            Plan p = Choreo.PickArea(c, this, level, r * 0.8f, 14f * k, Total(level), 3);
            if (p == null)
            {
                // one great foe worth a downpour: a titan, a kaiju, a Grandmaster or a God
                Actor v = c.Focus;
                if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || c.Dist(v) > 14f * k || !Combat.Grounded(v))
                {
                    return null;
                }
                if (!c.Forced && Tiers.Of(v) < Tier.Grandmaster && Weights.Species(v) < Weight.Titan)
                {
                    return null;
                }
                Vector2 d = v.current_position - c.Pos;
                p = new Plan
                {
                    Target = v,
                    Point = v.current_position,
                    Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                    Count = 1,
                    Score = c.Value(v, c.Expected(v, Total(level)), cm),
                    Data = c.Forced
                };
            }
            p.AreaRadius = r;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Squall m = new Squall(a, this, p);
            m.Build();
            return m.Start();
        }

        /// <summary>The move: the blade raised, the shaft of light, the squall called down, the downbeat.</summary>
        private sealed class Squall : ChoreoMove
        {
            private Vector2 _at;
            private Hue _blade;

            public Squall(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                // the rain comes down on a field, not on one foe held still
                SetVictim(null);
                _at = p.Point;
                Face(_at - Stance);
            }

            public void Build()
            {
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Area | Form.Projectile;
                // the blade raised to the sky
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.24f,
                    To = new Shape(-10f, 0.9f, 1.16f, 0f),
                    Ease = Ease.OutCubic,
                    Track = false,
                    Forms = show,
                    Every = (mm, k) => ((Squall)mm).Raised(k)
                });
                // a shaft of light runs up from it; the ring opens over the field; the squall begins
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.1f,
                    To = new Shape(-14f, 0.86f, 1.22f, 0.05f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Squall)mm).Call()
                });
                // it holds the blade up while the first swords fall
                Add(new Beat { Tempo = Tempo.Free, Dur = 0.55f, Track = false, Forms = show, Every = (mm, k) => ((Squall)mm).Raised(1f) });
                // the conductor's downbeat
                Add(new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(26f, 1.14f, 0.88f, 0f, 0.06f), Ease = Ease.OutExpo, Track = false, Forms = show });
                Hold(0.12f, false, Tempo.Follow);
                Recover(0.3f);
            }

            private void Raised(float k)
            {
                Vector2 hand = BodyPos() + new Vector2(Fwd * 0.5f, Hpx * 0.3f) * Px;
                TbHiArt.Blade(this, hand, new Vector2(Fwd * 0.12f, 1f), Hpx * (0.6f + 0.2f * k), _blade);
                if (Vis && k >= 1f && Random.value < 0.3f)
                {
                    Fx.Glint(hand + new Vector2(Fwd * 0.12f, 1f) * Hpx * 0.8f * Px, Px, 0.12f, Hue.Core, 0.7f);
                }
            }

            private void Call()
            {
                int level = Level;
                Vector2 hand = BodyPos() + new Vector2(Fwd * 0.5f, Hpx * 0.3f) * Px;
                Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                float r = Radius(level);
                Vector2 sky = _at + new Vector2(0f, 5.5f * Kk);
                if (Vis || Kit.Visible(_at, 0.4f))
                {
                    // the shaft of light up from the blade
                    Kit.Line(hand, hand + new Vector2(0f, 7f * Kk), Px, 0.35f, 3, FxLayer.Top, 14, Color.white, Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Flash(hand, Px, Mathf.RoundToInt(5f * Kk), 0.08f, Color.white, TopLayer);
                    // the ring over the field and its shadow on the ground
                    int rr = Mathf.Clamp(Mathf.RoundToInt(r / Px), 6, 90);
                    Particle ring = Fx.Spawn(ChoreoArt.Sigil(rr), sky, Px, 2.6f, FxLayer.Top, 3);
                    ring?.Sized(0.6f, 1f).Color(Fx.WithA(Hue.Core, 0f), Fx.WithA(Hue.Core, 0.55f), Fx.WithA(Hue.Edge, 0.45f), Fx.WithA(Hue.Edge, 0f));
                    Particle mark = Fx.Spawn(ChoreoArt.Sigil(rr), _at, Px, 2.8f, FxLayer.Ground, 3);
                    mark?.Sized(0.9f, 1f).Color(Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.4f), Fx.WithA(Hue.Edge, 0.3f), Fx.WithA(Hue.Edge, 0f));
                }
                Vector2 wind = _at - Stance;
                Areas.Add(new Downpour(this, _at, r, Swords(level), level <= 1 ? 1.6f : level == 2 ? 1.9f : 2.2f, wind, level));
                Ruin.Quake(A, _at, Scale.Light, 0.6f, Kk);
            }
        }

        /// <summary>
        /// The downpour: swords of light falling slantwise, the front of the squall sweeping across the
        /// field; each skewers the foe it finds and stays standing in the ground; at the end the whole
        /// field of swords flashes and shatters.
        /// </summary>
        private sealed class Downpour : Hazard
        {
            private sealed class Sword
            {
                public Vector2 At;
                public float T, Fall;
                public bool Down;
                public Particle Shadow, Body;
            }

            private readonly Vector2 _at, _fwd, _side, _slant;
            private readonly float _r, _dur, _mult, _shatter;
            private readonly int _count, _maxHits, _level;
            private readonly List<Sword> _swords = new List<Sword>(160);
            private readonly Dictionary<long, int> _hits = new Dictionary<long, int>();
            private readonly HashSet<long> _read = new HashSet<long>();
            private readonly HashSet<long> _spared = new HashSet<long>();
            private readonly HashSet<Building> _cut = new HashSet<Building>();
            private int _made;
            private float _next, _endT = -1f;
            private bool _shattered;
            private readonly int _len;

            public Downpour(ChoreoMove m, Vector2 at, float r, int count, float dur, Vector2 wind, int level) : base(m)
            {
                _at = at;
                _r = r * Mathf.Lerp(1f, K, 0.4f);
                _count = Mathf.Clamp(count, 1, 200);
                _dur = dur;
                _level = level;
                _mult = SwordMult(level);
                _maxHits = MaxHits(level);
                _shatter = Shatter(level);
                _fwd = wind.sqrMagnitude > 0.01f ? wind.normalized : Vector2.right;
                _side = Choreo.Perp(_fwd);
                // the swords fall slanting with the squall, driven from the God's side
                _slant = new Vector2(Mathf.Sign(_fwd.x == 0f ? 1f : _fwd.x) * 0.38f, -1f).normalized;
                _len = Mathf.Clamp(Mathf.RoundToInt(8f * Mathf.Lerp(1f, K, 0.6f)), 6, 40);
            }

            public override bool Tick(float dt)
            {
                if (dt <= 0f)
                {
                    return true;
                }
                Age += dt;
                _next -= dt;
                while (_made < _count && _next <= 0f)
                {
                    _next += _dur / _count;
                    _swords.Add(new Sword { At = Pick(_made / (float)_count), Fall = 0.2f });
                    _made++;
                }
                for (int i = 0; i < _swords.Count; i++)
                {
                    Sword s = _swords[i];
                    if (s.Down)
                    {
                        continue;
                    }
                    s.T += dt;
                    float k = Mathf.Clamp01(s.T / s.Fall);
                    bool vis = Kit.Visible(s.At, 0.3f);
                    if (vis)
                    {
                        Render(s, k);
                    }
                    if (k >= 1f)
                    {
                        s.Down = true;
                        Strike(s, vis);
                    }
                }
                if (_made >= _count && _endT < 0f)
                {
                    _endT = Age + 0.3f;
                }
                if (_endT >= 0f && Age >= _endT && !_shattered)
                {
                    _shattered = true;
                    ShatterAll();
                }
                return !_shattered || Age < _endT + 0.15f;
            }

            /// <summary>Where sword 'u' (0..1 of the squall) comes down: never on the God itself.</summary>
            private Vector2 Pick(float u)
            {
                Vector2 p = Pick0(u);
                if (A != null && A.isAlive())
                {
                    Vector2 me = A.current_position;
                    float keep = 1.6f * K;
                    for (int i = 0; i < 3 && (p - me).sqrMagnitude < keep * keep; i++)
                    {
                        p = Pick0(Mathf.Clamp01(u + 0.15f * (i + 1)));
                    }
                    if ((p - me).sqrMagnitude < keep * keep)
                    {
                        Vector2 away = p - me;
                        p = Combat.Clamp(me + (away.sqrMagnitude > 1e-4f ? away.normalized : _fwd) * keep);
                    }
                }
                return p;
            }

            /// <summary>On the sweeping front, onto foes first.</summary>
            private Vector2 Pick0(float u)
            {
                float front = Mathf.Lerp(-_r, _r, u);
                if (OwnerOk && Random.value < 0.62f)
                {
                    Choreo.Foes(A, _at, _r, Near);
                    // a foe near the front that has not had its share
                    for (int tries = 0; tries < 3 && Near.Count > 0; tries++)
                    {
                        Actor v = Near[Random.Range(0, Near.Count)];
                        float along = Vector2.Dot(v.current_position - _at, _fwd);
                        _hits.TryGetValue(v.Sid(), out int n);
                        if (n < _maxHits && Mathf.Abs(along - front) < _r * 0.45f)
                        {
                            return v.current_position + Random.insideUnitCircle * 0.25f;
                        }
                    }
                }
                float half = Mathf.Sqrt(Mathf.Max(0f, _r * _r - front * front));
                Vector2 p = _at + _fwd * (front + Random.Range(-0.6f, 0.6f)) + _side * Random.Range(-half, half);
                return Combat.Clamp(p);
            }

            private void Render(Sword s, float k)
            {
                float px = Px;
                if (s.Shadow == null)
                {
                    s.Shadow = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(2f * K)), Art.Ground, 0.4f), s.At, px, s.Fall + 0.05f, FxLayer.Ground, 3);
                    if (s.Shadow != null)
                    {
                        s.Shadow.Held = true;   // (it goes when its sword lands, never on its own)
                        s.Shadow.Sized(0.3f, 1f).Color(new Color(0f, 0f, 0f, 0.08f), new Color(0f, 0f, 0f, 0.35f));
                    }
                }
                float h = (1f - k * k) * 6f * K;
                Vector2 pos = s.At - _slant * h / Mathf.Max(0.2f, -_slant.y);
                if (s.Body == null || !s.Body.Alive)
                {
                    s.Body = Fx.Spawn(ChoreoArt.LightSword(_len), pos, px, 3f, FxLayer.Top, 12);
                    if (s.Body == null)
                    {
                        return;
                    }
                    s.Body.Held = true;
                    s.Body.Rot = Mathf.Atan2(_slant.y, _slant.x) * Mathf.Rad2Deg + 90f;
                }
                s.Body.Pos = pos;
                s.Body.Grad = null;
                s.Body.R.color = Color.Lerp(Hue.Core, Hue.Main, 0.3f);
                Kit.Line(pos, pos - _slant * (1.2f + 2f * (1f - k)) * K, px, 0.05f, 1, FxLayer.Top, 11, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
            }

            /// <summary>A sword comes down: it skewers the foe it finds, pocks the ground, stays standing.</summary>
            private void Strike(Sword s, bool vis)
            {
                WorldTile t = Combat.TileAt(s.At);
                if (vis)
                {
                    if (s.Body != null && s.Body.Alive)
                    {
                        // it stands in the ground where it struck, among the units
                        s.Body.Pos = s.At;
                        s.Body.R.sortingLayerID = Fx.UnitsLayer;
                        s.Body.R.sortingOrder = -8;
                    }
                    Kit.Flash(s.At, Px, Mathf.RoundToInt(3f * K), 0.06f, Fx.WithA(Hue.Core, 0.95f));
                    Kit.DustRing(s.At, Px, t, 3, 20f, 0.3f, K);
                }
                else if (s.Body != null)
                {
                    s.Body.Kill();
                    s.Body = null;
                }
                s.Shadow?.Kill();
                s.Shadow = null;
                Choreo.Sound("event:/SFX/HIT/HitGeneric", t, 0.05f);
                if (!OwnerOk)
                {
                    return;
                }
                Vector2 feet = A.current_position;
                if (t != null && t.hasBuilding() && t.building != null && t.building.isAlive())
                {
                    if (!_cut.Contains(t.building) || Random.value < 0.25f)
                    {
                        _cut.Add(t.building);
                        Ruin.Blast(A, s.At, 0.5f, Scale.Light, feet, false);
                    }
                }
                else if ((s.At - feet).sqrMagnitude > 2f)
                {
                    Ruin.Blast(A, s.At, 0.35f, Scale.Light, feet, false, -2);
                }
                Choreo.Foes(A, s.At, 0.55f * Mathf.Lerp(1f, K, 0.4f), Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Skewer(Near[i], s.At);
                }
            }

            private void Skewer(Actor u, Vector2 at)
            {
                long id = u.Sid();
                if (_spared.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                {
                    return;
                }
                _hits.TryGetValue(id, out int n);
                if (n >= _maxHits)
                {
                    return;
                }
                if (_read.Add(id) && Combat.Reach(M, u, true) != Contact.Hit)
                {
                    _spared.Add(id);   // it read the squall and got out from under it
                    return;
                }
                _hits[id] = n + 1;
                Combat.Hit(A, u, _mult * cm, false, false, 0f, M);
                Impact im = Impact.Of(Sakuga.Hurt.Fold, u.current_position - at, 0.12f, 0.75f);
                im.Hold = 0.12f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.04f);
                if (Kit.Visible(at))
                {
                    Kit.Sparks(Choreo.BodyAt(u, 0.5f), Px, 2, 40f, 80f, Hue.Edge);
                }
            }

            /// <summary>The field of standing swords flashes white at once and shatters into light.</summary>
            private void ShatterAll()
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(_at));
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(_at), 0.1f);
                bool any = false;
                for (int i = 0; i < _swords.Count; i++)
                {
                    Sword s = _swords[i];
                    if (s.Body != null && s.Body.Alive)
                    {
                        any = true;
                        Vector2 at = s.Body.Pos;
                        Kit.Flash(at + new Vector2(0f, 0.3f * K), Px, 2, 0.08f, Color.white, FxLayer.Top, 25);
                        if (i % 3 == 0)
                        {
                            for (int j = 0; j < 2; j++)
                            {
                                Particle m = Fx.Spawn(Art.Pixel, at + new Vector2(0f, Random.Range(0.1f, 0.6f) * K), Px, Random.Range(0.3f, 0.5f), FxLayer.Top, 12);
                                m?.Moving(Random.insideUnitCircle * 20f * Px, Random.Range(10f, 30f) * Px, 1.5f).Sized(1.5f, 0.6f)
                                    .Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                            }
                        }
                        s.Body.Kill();
                        s.Body = null;
                    }
                    s.Shadow?.Kill();
                    s.Shadow = null;
                }
                if (any && Kit.Visible(_at, 0.4f))
                {
                    Fx.Ring(_at, Px, 0.4f, 4f, _r / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                }
                if (!OwnerOk)
                {
                    return;
                }
                Choreo.Foes(A, _at, _r, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    long id = u.Sid();
                    if (_spared.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                    {
                        continue;
                    }
                    if (_read.Add(id) && Combat.Reach(M, u, true) != Contact.Hit)
                    {
                        continue;
                    }
                    Combat.Hit(A, u, _shatter * cm, false, false, 0f, M);
                    Impact im = Impact.Of(Sakuga.Hurt.Knockdown, u.current_position - _at, 0.5f, 1.1f);
                    im.Daze = 0.6f;
                    im.Owed = MeteorSlam.AtDeathsDoor(u);
                    Victims.Hurt(u, A, M, im);
                }
                Ruin.Quake(A, _at, Scale, 1.1f, K);
            }

            public override void Kill()
            {
                for (int i = 0; i < _swords.Count; i++)
                {
                    _swords[i].Shadow?.Kill();
                    _swords[i].Body?.Kill();
                }
                _swords.Clear();
            }
        }
    }
}
