using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BIG BANG (Ki Arts, God). It holds out one open palm and the world goes quiet: in the palm a
    /// single point of white light, no bigger than a star (the world dims and stops a heartbeat; a
    /// ting). It flicks it away. The point drifts out over the enemy and hangs there, rings of light
    /// closing on it, motes sucked into it... and the universe begins: a white-out flash, an impact
    /// frame that swallows the field, a sphere of light swelling out to 10-14 tiles, shock rings
    /// racing over the land, the earth under it blown into a vast crater (every building in it
    /// broken), everyone inside hurled away; then the light rises in a column and fades, and embers
    /// fall. The God stands braced at the edge of it as the light washes past. Ultimate: a cinematic
    /// (veil, freeze, title, slow motion at the blast) when the settings allow; it plays without
    /// one otherwise.
    /// I (a bare trait): radius 10. II: 12. III (Gods): 14.
    /// Damage at the heart (x the user's damage): I 5, II 6, III 7 (40% at the rim).
    /// </summary>
    internal sealed class BigBangTech : Technique
    {
        public override string Id => "big_bang";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Area | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 260f, 340f, 420f };
        public override float[] Cooldown => new[] { 80f, 85f, 90f };

        public static float Radius(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 12f : 14f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 6f : 7f;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level);
            Vector2 at = c.Cluster(r * 0.5f, c.Forced ? 0f : 4f, 18f, out int count, out Actor center);
            if (center == null)
            {
                if (!c.Forced || c.Focus == null)
                {
                    return null;
                }
                center = c.Focus;
                at = center.current_position;
            }
            _near.Clear();
            c.CountNear(at, r, _near);
            float score = 0f;
            bool mighty = false;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                c.Allowed(v, out float cm);
                float fall = Mathf.Lerp(1f, 0.4f, Vector2.Distance(v.current_position, at) / r);
                score += c.Value(v, c.Expected(v, Mult(level) * fall), cm);
                if (Tiers.Of(v) >= Tier.Grandmaster || Weights.Species(v) >= Weight.Titan)
                {
                    mighty = true;
                }
            }
            if (!c.Forced)
            {
                // the birth of a universe is for an army, a peer, or the fight of its life
                if (!(_near.Count >= 6 || mighty || c.ThreatAround >= 1.2f || c.Desperate && _near.Count >= 2))
                {
                    return null;
                }
                score *= 1f + 0.03f * Mathf.Min(20, _near.Count);
            }
            else
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = at - c.Pos;
            return new Plan
            {
                Target = center,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = _near.Count,
                Score = score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Spark sp = new Spark();
            m.Ended2 = (mm, aborted) => sp.Clear();
            m.Face(p.Point - m.Stance);
            Form shows = Form.Ki | Form.Area | Form.Ultimate;
            m.OpenCinematic(8f, 0.55f, "sakuga_title_big_bang");
            // the palm opens: a single point of light in it; the world stops
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.24f,
                To = new Shape(4f, 1.02f, 1f, 0f, 0.05f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm => sp.Light(mm),
                Every = (mm, k) => sp.Palm(mm, k)
            });
            // the quiet
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.3f,
                Forms = shows,
                Every = (mm, k) => sp.Palm(mm, 1f)
            }.On(0.5f, mm =>
            {
                Choreo.Sound("event:/SFX/UI/HintHighlight", mm.A.current_tile);
                if (mm.Vis)
                {
                    Fx.Glint(sp.At, mm.Px, 0.2f, Color.white, 1.2f);
                }
            }));
            // it flicks it away
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(16f, 1.14f, 0.9f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = shows,
                Enter = mm => sp.Flick(mm, level)
            });
            // it watches it go, until the light is born
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.3f,
                To = new Shape(6f, 1.04f, 0.98f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = shows,
                Until = mm => sp.Hazard == null || sp.Hazard.Phase >= 2,
                MaxDur = 2.2f
            });
            // braced as the light washes past
            m.Add(new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.42f,
                To = new Shape(-8f, 1.06f, 0.96f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Tremble = true,
                Forms = shows,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 12, 50f, 0.6f, mm.Kk);
                        ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                    }
                }
            });
            m.Recover(0.4f);
            return m.Start();
        }

        /// <summary>The point of light while it is in the palm.</summary>
        private sealed class Spark
        {
            public BigBangHazard Hazard;
            public Vector2 At;
            private Particle _dot, _halo;

            public void Light(ChoreoMove m)
            {
                At = m.Hand(Limb.Palm) + new Vector2(0f, 1f * m.Px);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile, 0.2f);
                m.Freeze(0.45f);
                if (m.Vis)
                {
                    _dot = m.Glue(Fx.Spawn(Art.Disk(1, 1f, 0f), At, m.Px, 1f, m.TopLayer, 16));
                    _halo = m.Glue(Fx.Spawn(Art.Disk(3, 1f, 0.6f), At, m.Px, 1f, m.TopLayer, 15));
                    Fx.Ring(At, m.Px, 0.3f, 18f * m.Kk, 2f, 1, 1, m.TopLayer, 14, 1f, 1.5f, Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(Color.white, 1f));
                }
            }

            public void Palm(ChoreoMove m, float k)
            {
                At = m.Hand(Limb.Palm) + new Vector2(0f, 1f * m.Px);
                float pulse = 0.7f + 0.3f * Mathf.Sin(m.T * 26f);
                if (_dot != null && _dot.Alive)
                {
                    _dot.Pos = At;
                    _dot.Grad = null;
                    _dot.R.color = Color.white;
                }
                if (_halo != null && _halo.Alive)
                {
                    _halo.Pos = At;
                    _halo.Size0 = _halo.Size1 = 0.6f + 0.5f * k * pulse;
                    _halo.Grad = null;
                    _halo.R.color = Fx.WithA(m.Hue.Core, 0.5f * pulse);
                }
            }

            public void Flick(ChoreoMove m, int level)
            {
                Clear();
                Vector2 to = Combat.Clamp(m.Point);
                Hazard = Areas.Add(new BigBangHazard(m, At, to, Radius(level) * Mathf.Lerp(1f, m.Kk, 0.4f), Mult(level), level));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.Flash(At, m.Px, 2, 0.05f, Color.white, m.TopLayer);
                }
            }

            public void Clear()
            {
                KiHi.Kill(ref _dot);
                KiHi.Kill(ref _halo);
            }
        }
    }

    /// <summary>
    /// The Big Bang in the world: the point of light drifting from the God's palm out over the enemy
    /// (Phase 0), hanging there while the light is drawn into it (1), the birth: a sphere of light
    /// swelling out to 'R', everyone it passes hurled away, the ground blown into a crater (2), and the
    /// afterglow: the light rising in a column, embers falling (3). Only enemies are hurt.
    /// </summary>
    internal sealed class BigBangHazard : Hazard
    {
        public int Phase;
        private readonly Vector2 _from, _at;
        private readonly float _r, _mult, _hang;
        private readonly int _level;
        private float _t, _ringT, _moteT;
        private bool _rimDone;
        private Particle _dot, _halo, _sphere, _core, _rim, _column;

        public BigBangHazard(ChoreoMove m, Vector2 drawn, Vector2 at, float r, float mult, int level) : base(m)
        {
            _from = drawn;
            _at = at;
            _r = r;
            _mult = mult;
            _level = level;
            _hang = 1.4f * K;
        }

        private FxLayer Layer => M != null && M.Cinematic ? FxLayer.Cine : FxLayer.Top;

        /// <summary>Where the point hangs (drawn).</summary>
        private Vector2 Over => _at + new Vector2(0f, _hang);

        public override bool Tick(float dt)
        {
            Age += dt;
            _t += dt;
            switch (Phase)
            {
                case 0:
                    {
                        float dur = Mathf.Clamp(Vector2.Distance(_from, Over) / 22f, 0.22f, 0.5f);
                        float k = Mathf.Clamp01(_t / dur);
                        Vector2 at = Vector2.Lerp(_from, Over, Beat.Apply(Ease.OutCubic, k));
                        Point(at, 0.5f);
                        if (Kit.Visible(at))
                        {
                            Kit.Line(at, Vector2.Lerp(_from, Over, Beat.Apply(Ease.OutCubic, Mathf.Max(0f, k - 0.08f))), Px, 0.12f, 1, Layer, 12, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                        }
                        if (k >= 1f)
                        {
                            Next();
                        }
                        return true;
                    }
                case 1:
                    {
                        float k = Mathf.Clamp01(_t / 0.42f);
                        Point(Over, 0.5f + k);
                        Inhale(dt, k);
                        if (k >= 1f)
                        {
                            Next();
                            Birth();
                        }
                        return true;
                    }
                case 2:
                    {
                        float k = Mathf.Clamp01(_t / 0.5f);
                        Grow(k);
                        if (k >= 1f)
                        {
                            Next();
                        }
                        return true;
                    }
                default:
                    {
                        float k = Mathf.Clamp01(_t / 0.9f);
                        Fade(k);
                        return k < 1f;
                    }
            }
        }

        private void Next()
        {
            Phase++;
            _t = 0f;
        }

        /// <summary>The point of light (and its halo, 'glow' times the size).</summary>
        private void Point(Vector2 at, float glow)
        {
            if (!Kit.Visible(at, 0.3f))
            {
                KiHi.Kill(ref _dot);
                KiHi.Kill(ref _halo);
                return;
            }
            float pulse = 0.75f + 0.25f * Mathf.Sin(Age * 30f);
            KiHi.Blob(ref _halo, at, Px, 3f * glow * pulse * Mathf.Lerp(1f, K, 0.5f), Fx.WithA(Hue.Core, 0.5f), Layer, 15, 0.6f);
            KiHi.Blob(ref _dot, at, Px, 1f, Color.white, Layer, 16, 0f);
        }

        /// <summary>It draws the light in: rings closing on it, motes rushing in, the dust drawn across the ground.</summary>
        private void Inhale(float dt, float k)
        {
            if (!Kit.Visible(Over, 0.4f))
            {
                return;
            }
            _ringT -= dt;
            if (_ringT <= 0f)
            {
                _ringT = 0.12f;
                Fx.Ring(Over, Px, 0.22f, (30f - 10f * k) * K, 2f, 1, 1, Layer, 13, 1f, 1.4f, Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Color.white, 1f));
                Kit.DustRing(_at, Px, Combat.TileAt(_at), 8, -40f, 0.45f, K);
            }
            _moteT -= dt;
            if (_moteT <= 0f)
            {
                _moteT = 0.02f;
                ChoreoFx.Motes(Over, Px, 4, 40f * K, 0.16f, Hue.Core, Hue.Edge, Layer);
            }
        }

        /// <summary>The birth: white-out, the impact frame, the shock, the crater.</summary>
        private void Birth()
        {
            KiHi.Kill(ref _dot);
            KiHi.Kill(ref _halo);
            WorldTile tile = Combat.TileAt(_at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.2f);
            Choreo.Sound("event:/SFX/POWERS/DivineLight", tile, 0.2f);
            float rpx = Mathf.Clamp(_r / Px, 4f, 140f);
            if (Kit.Visible(_at, 0.8f))
            {
                Kit.ImpactFrame(Over, Px, 2.4f * K, Hue.Pal, false, Layer);
                Kit.Flash(Over, Px, Mathf.RoundToInt(rpx), 0.22f, Color.white, Layer);
                Fx.FocusLines(Over, Px, Fx.Count(20), rpx * 0.4f, rpx * 0.9f, 0.2f);
                Fx.Ring(_at, Px, 0.5f, 6f, rpx * 1.25f, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 1f), Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(_at, Px, 0.8f, 6f, rpx * 1.5f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.7f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Fx.Ring(_at, Px, 1.1f, 6f, rpx * 1.8f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.3f, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
                _sphere = Fx.Spawn(Art.Disk(4, 0.85f, 0.4f), Over, Px, 1f, Layer, 9);
                _core = Fx.Spawn(Art.Disk(3, 0.85f, 0.2f), Over, Px, 1f, Layer, 10);
                _rim = Fx.Spawn(Art.Ring(4, 2, 0.85f), Over, Px, 1f, Layer, 11);
                Hold(_sphere);
                Hold(_core);
                Hold(_rim);
                Kit.Crater(_at, Px * K * 2.2f, tile, true, 10f, false, Hue.Edge);
                Kit.Debris(_at, Px, tile, 24, K * 1.8f);
                Kit.DustRing(_at, Px, tile, 24, 120f, 0.9f, K * 1.4f);
            }
            if (!OwnerOk)
            {
                return;
            }
            Kit.Stop(_at, 0.12f);
            Ruin.Quake(A, _at, Scale.Ultimate, 1.6f, K);
            Kit.Shake(_at, 1f, 0.03f, K);
            if (M != null && M.Cinematic && Settings.Cinematics > 0)
            {
                Cinema.SlowMo(0.4f, 0.55f);   // (its own cinematic: the world holds its breath)
            }
            float baseR = _r * 0.6f / Mathf.Max(0.1f, Ruin.Radius(Tiers.Of(A), Scale.Ultimate, 1f));
            Ruin.Blast(A, _at, baseR, Scale.Ultimate, A.current_position);
        }

        private static void Hold(Particle p)
        {
            if (p != null)
            {
                p.Held = true;
            }
        }

        /// <summary>The sphere swells out; everyone it passes is hurled away; at the rim the land breaks again.</summary>
        private void Grow(float k)
        {
            float e = Beat.Apply(Ease.OutCubic, k);
            float cur = Mathf.Max(0.3f, _r * e);
            int rp = Mathf.Clamp(Mathf.RoundToInt(cur / Px), 2, 140);
            Vector2 c = _at + new Vector2(0f, cur * 0.25f);
            Shape(_sphere, Art.Disk(rp, 0.85f, 0.4f), c, Fx.WithA(Color.Lerp(Color.white, Hue.Main, e), 0.85f - 0.3f * e));
            Shape(_core, Art.Disk(Mathf.Max(2, Mathf.RoundToInt(rp * 0.6f)), 0.85f, 0.2f), c, Fx.WithA(Color.white, 0.95f - 0.4f * e));
            Shape(_rim, Art.Ring(rp, 2, 0.85f), c, Fx.WithA(Hue.Rim, 0.95f));
            if (!OwnerOk)
            {
                return;
            }
            Choreo.Foes(A, _at, cur + 0.4f, Near, true, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Mathf.Clamp01(Vector2.Distance(u.current_position, _at) / _r);
                Strike(u, _mult * Mathf.Lerp(1f, 0.4f, d), Hurt.Launch, _at, Mathf.Lerp(6f, 3f, d) * Mathf.Lerp(1f, K, 0.3f), Mathf.Lerp(3.4f, 1.8f, d), 2f);
            }
            if (!_rimDone && e >= 0.7f)
            {
                // the land breaks again where the shock reaches
                _rimDone = true;
                int n = 4 + _level;
                float a0 = Random.value * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + i * Mathf.PI * 2f / n;
                    Vector2 p = Combat.Clamp(_at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _r * 0.7f);
                    Ruin.Blast(A, p, 1.1f, Scale.Heavy, A.current_position);
                    if (Kit.Visible(p))
                    {
                        Kit.Debris(p, Px, Combat.TileAt(p), 6, K);
                    }
                }
            }
        }

        /// <summary>The light rises in a column and fades; embers fall; smoke.</summary>
        private void Fade(float k)
        {
            float rp = Mathf.Clamp(_r / Px, 2f, 140f);
            float a = 1f - k;
            Vector2 c = _at + new Vector2(0f, _r * 0.25f);
            Shape(_sphere, Art.Disk(Mathf.RoundToInt(rp), 0.85f, 0.4f), c, Fx.WithA(Hue.Main, 0.55f * a * a));
            Shape(_core, Art.Disk(Mathf.Max(2, Mathf.RoundToInt(rp * 0.6f * a)), 0.85f, 0.2f), c, Fx.WithA(Color.white, 0.55f * a));
            Shape(_rim, Art.Ring(Mathf.RoundToInt(rp), 1, 0.85f), c, Fx.WithA(Hue.Edge, 0.6f * a));
            if (!Kit.Visible(_at, 0.8f))
            {
                return;
            }
            float colH = _r * 1.2f * Beat.Apply(Ease.OutCubic, k);
            KiHi.Bar(ref _column, _at, _at + new Vector2(0f, Mathf.Max(0.1f, colH)), Px, Mathf.Lerp(rp * 0.35f, 2f, k), Fx.WithA(Color.Lerp(Color.white, Hue.Core, k), 0.7f * a), Layer, 8);
            if (Random.value < 0.6f * a)
            {
                Kit.Ember(_at + Random.insideUnitCircle * _r * 0.7f + new Vector2(0f, Random.Range(1f, 3f) * K), Px, 0.9f, new Vector2(Random.Range(-4f, 4f), Random.Range(-12f, -4f)) * Px, Hue.Edge);
            }
            if (Random.value < 0.15f * a)
            {
                ChoreoFx.Smoke(_at + Random.insideUnitCircle * _r * 0.5f, Px, 2, 6f, Fx.Darken(Fx.SoilColor(Combat.TileAt(_at)), 0.25f), 1.4f);
            }
        }

        private static void Shape(Particle p, Sprite s, Vector2 at, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = at;
            p.Grad = null;
            p.R.color = c;
            p.Cine = Cinema.Playing;
        }

        public override void Kill()
        {
            KiHi.Kill(ref _dot);
            KiHi.Kill(ref _halo);
            KiHi.Kill(ref _sphere);
            KiHi.Kill(ref _core);
            KiHi.Kill(ref _rim);
            KiHi.Kill(ref _column);
        }
    }
}
