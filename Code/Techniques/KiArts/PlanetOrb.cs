using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PLANET ORB (Ki Arts, Grandmaster). One hand goes up to the sky and light pours into it: motes
    /// stream in from all round, the dust is drawn across the ground to its feet, and over its head
    /// an orb swells until it dwarfs its maker, a world of gold with a white heart and a churning rim
    /// (Charging, and long: the price of it is that anyone quick enough can break it, and then the
    /// orb bursts over its own head). Then it leans back under the weight and hurls it: the orb sails
    /// slowly over the field, its shadow growing under it, and comes down on the enemy: a blinding
    /// flash, a dome of light, the earth blown out in a crater, everyone in it thrown far.
    /// I: radius 4. II: 5. III: 6, and the shock runs on past the crater knocking down those beyond.
    /// Damage at the heart (x the user's damage): I 3.2, II 3.9, III 4.6 (60% at the rim; the shock
    /// ring of III 30%).
    /// </summary>
    internal sealed class PlanetOrbTech : Technique
    {
        public override string Id => "planet_orb";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Charge | Form.Area | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 70f, 110f, 160f };
        public override float[] Cooldown => new[] { 20f, 22f, 24f };

        public static float Radius(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 3.2f : level == 2 ? 3.9f : 4.6f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 1.65f : 1.8f;
        }

        /// <summary>The orb's radius when it is full (sprite pixels of a human-sized thrower).</summary>
        private static float OrbPx(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 9.5f : 11f;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            Vector2 at = c.Cluster(r * 0.7f, c.Forced ? 0f : 3.5f, c.Forced ? 16f : 14f, out int count, out Actor center);
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
            bool worth = false;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                c.Allowed(v, out float cm);
                float fall = Mathf.Lerp(1f, 0.6f, Vector2.Distance(v.current_position, at) / Mathf.Max(0.1f, r));
                score += c.Value(v, c.Expected(v, Mult(level) * fall), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2.5f) * cm)
                {
                    worth = true;
                }
            }
            if (!c.Forced)
            {
                // a planet is not for one peasant
                if (count < 2 && !worth || count < 3 && !worth && score < c.D * 6f)
                {
                    return null;
                }
                score *= count >= 4 ? 1.2f : 1f;
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
                Count = Mathf.Max(count, _near.Count),
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
            Orb orb = new Orb(OrbPx(level) * Mathf.Lerp(1f, m.Kk, 0.7f));
            m.Ended2 = (mm, aborted) => orb.End(mm);
            m.Face(p.Point - m.Stance);
            Form shows = Form.Ki | Form.Charge | Form.Area | Form.Heavy;
            // the hand goes up: a world gathers over its head
            m.Charge(ChargeTime(level), ChargeStyle.Raised, 2f, Element.Ki)
                .Every((mm, k) => orb.Gather(mm, k, level));
            // it leans back under the weight of it
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.13f,
                To = new Shape(-16f, 0.92f, 1.12f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Charging = true,
                Tremble = true,
                Forms = shows,
                Every = (mm, k) => orb.Heave(mm, k)
            });
            // and hurls it
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.09f,
                To = new Shape(26f, 1.22f, 0.86f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Ki | Form.Area | Form.Heavy,
                Enter = mm => orb.Throw(mm, level)
            });
            m.Hold(0.2f, false, Tempo.Follow);
            m.Recover(0.34f);
            return m.Start();
        }

        /// <summary>The orb while it is still in the thrower's hand (its own particles, glued to the move).</summary>
        private sealed class Orb
        {
            private readonly float _rp;
            private Particle _body, _core, _rim;
            private float _grow, _moteT, _dustT, _back;
            private Vector2 _at;
            private bool _thrown;

            public Orb(float rp)
            {
                _rp = rp;
            }

            /// <summary>Over its head (it grows: the higher, the bigger).</summary>
            private Vector2 Place(ChoreoMove m)
            {
                float r = _rp * Mathf.Max(0.15f, _grow);
                return m.BodyPos() + new Vector2(-m.Fwd * _back, m.Hpx * 0.5f + 1.5f + r) * m.Px;
            }

            public void Gather(ChoreoMove m, float k, int level)
            {
                if (_body == null)
                {
                    // (the engine's small orb in the hand gives way to the world over its head)
                    m.SpendOrb();
                    if (m.Vis)
                    {
                        _body = m.Glue(Fx.Spawn(Art.Disk(2, 1f, 0.35f), m.BodyPos(), m.Px, 1f, FxLayer.Over, 8));
                        _core = m.Glue(Fx.Spawn(Art.Disk(1, 1f, 0f), m.BodyPos(), m.Px, 1f, FxLayer.Over, 9));
                        _rim = m.Glue(Fx.Spawn(Art.Ring(2, 1, 1f, 8), m.BodyPos(), m.Px, 1f, FxLayer.Over, 10));
                    }
                }
                _grow = Mathf.Lerp(0.12f, 1f, Beat.Apply(Ease.OutQuad, k));
                Draw(m, k);
                if (!m.Vis)
                {
                    return;
                }
                _moteT -= Clock.Dt;
                if (_moteT <= 0f)
                {
                    _moteT = 0.02f;
                    // motes streaming in from far round it
                    ChoreoFx.Motes(_at, m.Px, level >= 3 ? 4 : 3, (_rp * 2.2f + 24f) * m.Kk, 0.22f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
                }
                _dustT -= Clock.Dt;
                if (_dustT <= 0f)
                {
                    _dustT = 0.12f;
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 6, -34f, 0.5f, m.Kk * (0.6f + 0.6f * k));
                    if (k > 0.5f)
                    {
                        Ruin.Quake(m.A, m.Pose.Ground, Scale.Casual, 0.5f + k, m.Kk);
                    }
                }
            }

            /// <summary>It sways back under the weight, the orb behind its head.</summary>
            public void Heave(ChoreoMove m, float k)
            {
                _grow = 1f;
                _back = 3f * Beat.Apply(Ease.OutQuad, k);
                Draw(m, 1f);
            }

            private void Draw(ChoreoMove m, float k)
            {
                _at = Place(m);
                float pulse = 1f + 0.06f * Mathf.Sin(m.T * 31f);
                int r = Mathf.Clamp(Mathf.RoundToInt(_rp * _grow), 1, 60);
                Shape(_body, Art.Disk(r, 1f, 0.35f), _at, Fx.WithA(Color.Lerp(m.Hue.Main, m.Hue.Edge, 0.25f), 0.92f), pulse, m);
                Shape(_core, Art.Disk(Mathf.Max(1, Mathf.RoundToInt(r * 0.55f)), 1f, 0f), _at + new Vector2(-0.15f, 0.2f) * r * m.Px, Color.Lerp(m.Hue.Core, Color.white, k), 1f, m);
                Shape(_rim, Art.Ring(Mathf.Max(2, r), 1, 1f, 8), _at, Fx.WithA(m.Hue.Rim, 0.85f), pulse, m);
                if (_rim != null && _rim.Alive)
                {
                    _rim.Rot += 120f * Clock.Dt;
                }
            }

            private static void Shape(Particle p, Sprite s, Vector2 at, Color c, float size, ChoreoMove m)
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
                p.Size0 = p.Size1 = size;
                p.Grad = null;
                p.R.color = c;
                p.Cine = m.Cinematic;
            }

            /// <summary>Hurled: it leaves the hand and flies on its own (PlanetOrbHazard).</summary>
            public void Throw(ChoreoMove m, int level)
            {
                _grow = 1f;
                _back = 0f;
                Vector2 from = _body != null && _body.Alive ? _body.Pos : Place(m);
                Kill(m);
                _thrown = true;
                // (it comes down where its target stands now: the enemy may have moved while it gathered)
                Vector2 to = Combat.Clamp(m.V != null && m.VOk ? m.VG : m.Point);
                Areas.Add(new PlanetOrbHazard(m, from, to, Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f), Mult(level), _rp, level));
                Choreo.Sound("event:/SFX/WEAPONS/WeaponStartThrow", m.A.current_tile);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
                if (m.Vis)
                {
                    Kit.SpeedLines(from, to - m.Pose.Ground, m.Px, 4, 6f, 0.16f, Fx.WithA(m.Hue.Edge, 0.85f), m.TopLayer);
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 8, 40f, 0.45f, m.Kk);
                }
                Ruin.Quake(m.A, m.Pose.Ground, Scale.Light, 1f, m.Kk);
            }

            private void Kill(ChoreoMove m)
            {
                if (_body != null)
                {
                    m.Unglue(_body);
                }
                if (_core != null)
                {
                    m.Unglue(_core);
                }
                if (_rim != null)
                {
                    m.Unglue(_rim);
                }
                _body = _core = _rim = null;
            }

            /// <summary>The move ended: broken mid-gather, the orb bursts over its head (its light scattering, no harm done).</summary>
            public void End(ChoreoMove m)
            {
                if (_thrown || _grow <= 0f)
                {
                    return;
                }
                if (m.Broken && Kit.Visible(_at))
                {
                    float px = m.Px;
                    int r = Mathf.Clamp(Mathf.RoundToInt(_rp * _grow), 2, 60);
                    Kit.Flash(_at, px, r + 3, 0.12f, Fx.WithA(m.Hue.Core, 1f));
                    Fx.Ring(_at, px, 0.3f, r, r * 2.4f + 6f, 2, 1, FxLayer.Top, 6, 1f, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                    int n = Fx.Count(18);
                    for (int i = 0; i < n; i++)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Particle q = Fx.Spawn(Art.Pixel, _at + d * r * px * 0.6f, px, Random.Range(0.3f, 0.6f), FxLayer.Top, 9);
                        q?.Moving(d * Random.Range(40f, 120f) * px, Random.Range(0f, 30f) * px, 3f, 60f * px).Sized(1.6f, 1f)
                            .Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                    }
                    Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile);
                }
                _grow = 0f;
            }
        }
    }

    /// <summary>
    /// The thrown planet orb: it sails from the hand to its spot (slowly at first, heavier as it
    /// falls), its shadow growing on the ground, and bursts: a flash and an impact frame, a dome of
    /// light swelling and fading, rings racing over the ground, a crater, everyone in it thrown far.
    /// Only enemies are hurt; nothing is drawn off screen; the thrower's death on the way turns it
    /// into light (no harm).
    /// </summary>
    internal sealed class PlanetOrbHazard : Hazard
    {
        private readonly Vector2 _from, _to;
        private readonly float _z0, _dur, _r, _mult, _rp;
        private readonly int _level;
        private float _t, _boomT, _moteT;
        private bool _boom;
        private Particle _body, _core, _rim, _shadow, _dome, _domeRim;

        /// <summary>'drawn': where the orb is drawn as it leaves the hand; 'to': the ground it falls on; 'r' tiles; 'rp' its radius in pixels.</summary>
        public PlanetOrbHazard(ChoreoMove m, Vector2 drawn, Vector2 to, float r, float mult, float rp, int level) : base(m)
        {
            _from = new Vector2(drawn.x, m.Pose.Ground.y);
            _z0 = Mathf.Max(0f, drawn.y - m.Pose.Ground.y);
            _to = to;
            _r = r;
            _mult = mult;
            _rp = rp;
            _level = level;
            _dur = Mathf.Clamp(Vector2.Distance(_from, _to) / 10f, 0.4f, 1.1f);
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            if (!_boom)
            {
                _t += dt;
                float k = Mathf.Clamp01(_t / _dur);
                float e = Mathf.Lerp(k, k * k, 0.6f);
                Vector2 g = Vector2.Lerp(_from, _to, e);
                float bottom = _rp * Px;
                float z = Mathf.Lerp(_z0, bottom, e) + 0.9f * K * 4f * e * (1f - e);
                Draw(g, z, k);
                if (k >= 1f)
                {
                    _boom = true;
                    Burst();
                }
                return true;
            }
            _boomT += dt;
            Afterglow(_boomT);
            return _boomT < 0.75f;
        }

        private void Draw(Vector2 g, float z, float k)
        {
            Vector2 at = g + new Vector2(0f, z);
            if (!Kit.Visible(at, 0.5f) && !Kit.Visible(g, 0.5f))
            {
                KillOrb();
                return;
            }
            int r = Mathf.Clamp(Mathf.RoundToInt(_rp), 2, 60);
            float pulse = 1f + 0.06f * Mathf.Sin(Age * 31f);
            Held(ref _body, Art.Disk(r, 1f, 0.35f), at, Fx.WithA(Color.Lerp(Hue.Main, Hue.Edge, 0.25f), 0.92f), pulse, FxLayer.Top, 12);
            Held(ref _core, Art.Disk(Mathf.Max(1, Mathf.RoundToInt(r * 0.55f)), 1f, 0f), at + new Vector2(-0.15f, 0.2f) * r * Px, Hue.Core, 1f, FxLayer.Top, 13);
            Held(ref _rim, Art.Ring(r, 1, 1f, 8), at, Fx.WithA(Hue.Rim, 0.85f), pulse, FxLayer.Top, 14);
            if (_rim != null)
            {
                _rim.Rot += 160f * Clock.Dt;
            }
            Held(ref _shadow, Art.Disk(Mathf.Max(2, Mathf.RoundToInt(r * 0.9f)), Art.Ground, 0.5f), g, new Color(0f, 0f, 0f, Mathf.Lerp(0.12f, 0.45f, k)), Mathf.Lerp(0.5f, 1f, k), FxLayer.Ground, 2);
            _moteT -= Clock.Dt;
            if (_moteT <= 0f)
            {
                _moteT = 0.03f;
                Vector2 d = Random.insideUnitCircle.normalized;
                Particle m = Fx.Spawn(Art.Pixel, at + d * r * Px, Px, 0.25f, FxLayer.Top, 11);
                m?.Moving(d * 8f * Px + (_from - _to).normalized * 12f * Px, 0f, 2f).Sized(1.5f, 1f).Color(Color.white, Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        private void Held(ref Particle p, Sprite s, Vector2 at, Color c, float size, FxLayer layer, int order)
        {
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(s, at, Px, 1f, layer, order);
                if (p == null)
                {
                    return;
                }
                p.Held = true;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = at;
            p.Size0 = p.Size1 = size;
            p.Grad = null;
            p.R.color = c;
        }

        /// <summary>It comes down: the burst.</summary>
        private void Burst()
        {
            KillOrb();
            Vector2 at = _to;
            WorldTile tile = Combat.TileAt(at);
            bool vis = Kit.Visible(at, 0.6f);
            bool owner = OwnerOk;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            Choreo.Sound("event:/SFX/POWERS/DivineLight", tile, 0.2f);
            if (vis)
            {
                float rpx = _r / Px;
                Kit.ImpactFrame(at + new Vector2(0f, 0.6f * K), Px, (1f + 0.15f * _level) * K, Hue.Pal, false);
                Kit.Flash(at + new Vector2(0f, 0.5f * K), Px, Mathf.Clamp(Mathf.RoundToInt(rpx * 0.7f), 4, 140), 0.16f, Color.white);
                Fx.Ring(at, Px, 0.45f, _rp, rpx * 1.15f, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 1f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(at, Px, 0.7f, _rp, rpx * 1.4f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.6f, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                Fx.FocusLines(at + new Vector2(0f, 0.6f * K), Px, Fx.Count(14), rpx * 0.5f, rpx * 0.9f, 0.14f);
                Kit.DustRing(at, Px, tile, 20, 80f, 0.7f, K);
                Kit.Debris(at, Px, tile, 18, K * 1.3f);
                Kit.Crater(at, Px * K * 1.4f, tile, true, 8f, false, Hue.Edge);
                ChoreoFx.Smoke(at, Px, 8, rpx * 0.4f, Fx.Darken(Fx.SoilColor(tile), 0.2f), 1.1f);
                _dome = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(_rp), 2, 140), 0.8f, 0.4f), at + new Vector2(0f, 0.2f * K), Px, 1f, FxLayer.Top, 9);
                if (_dome != null)
                {
                    _dome.Held = true;
                }
                _domeRim = Fx.Spawn(Art.Ring(Mathf.Clamp(Mathf.RoundToInt(_rp), 2, 140), 2, 0.8f), at + new Vector2(0f, 0.2f * K), Px, 1f, FxLayer.Top, 10);
                if (_domeRim != null)
                {
                    _domeRim.Held = true;
                }
            }
            if (!owner)
            {
                return;   // (its thrower is gone: only light)
            }
            Kit.Stop(at, 0.07f);
            Ruin.Quake(A, at, Scale, 1.6f, K);
            // the crater: about the blast's own size
            float baseR = _r * 0.8f / Mathf.Max(0.1f, Ruin.Radius(Tiers.Of(A), Scale, 1f));
            Ruin.Blast(A, at, baseR, Scale, A.current_position);
            Choreo.Foes(A, at, _r, Near, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Mathf.Clamp01(Vector2.Distance(u.current_position, at) / Mathf.Max(0.1f, _r));
                Strike(u, _mult * Mathf.Lerp(1f, 0.6f, d), Hurt.Launch, at, Mathf.Lerp(4f, 2.2f, d), Mathf.Lerp(2.6f, 1.4f, d), 1.6f);
            }
            if (_level >= 3 && M != null)
            {
                Areas.Add(new ShockwaveHazard(M, at, _r * 1.5f, 18f, _mult * 0.3f, Hurt.Knockdown, 0.8f));
            }
            if (M != null && !M.Ended)
            {
                M.SetLink(Link.Scattered, null);
            }
        }

        /// <summary>The dome of light swells to the crater's size and fades.</summary>
        private void Afterglow(float t)
        {
            float rpx = Mathf.Clamp(_r / Px, 2f, 140f);
            float grow = Beat.Apply(Ease.OutCubic, Mathf.Clamp01(t / 0.2f));
            float fade = 1f - Mathf.Clamp01((t - 0.15f) / 0.55f);
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(_rp, rpx * 0.9f, grow)), 2, 140);
            if (_dome != null && _dome.Alive)
            {
                Sprite s = Art.Disk(r, 0.8f, 0.4f);
                if ((object)_dome.R.sprite != s)
                {
                    _dome.R.sprite = s;
                    _dome.Frames[0] = s;
                }
                _dome.Grad = null;
                _dome.R.color = Fx.WithA(Color.Lerp(Hue.Core, Hue.Main, grow), 0.55f * fade);
            }
            if (_domeRim != null && _domeRim.Alive)
            {
                Sprite s = Art.Ring(r, 2, 0.8f);
                if ((object)_domeRim.R.sprite != s)
                {
                    _domeRim.R.sprite = s;
                    _domeRim.Frames[0] = s;
                }
                _domeRim.Grad = null;
                _domeRim.R.color = Fx.WithA(Hue.Core, 0.9f * fade);
            }
            if (Random.value < 0.35f * fade && Kit.Visible(_to))
            {
                Kit.Ember(_to + Random.insideUnitCircle * _r * 0.6f, Px, 0.6f, new Vector2(Random.Range(-6f, 6f), Random.Range(10f, 24f)) * Px, Hue.Edge);
            }
        }

        /// <summary>The orb in flight is gone (it burst, or it is off screen).</summary>
        private void KillOrb()
        {
            KillP(ref _body);
            KillP(ref _core);
            KillP(ref _rim);
            KillP(ref _shadow);
        }

        public override void Kill()
        {
            KillOrb();
            KillP(ref _dome);
            KillP(ref _domeRim);
        }

        private static void KillP(ref Particle p)
        {
            if (p != null && p.Alive)
            {
                p.Kill();
            }
            p = null;
        }
    }
}
