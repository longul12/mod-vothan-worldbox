using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// QUAKE FIST (Earth Breaker, Grandmaster). It crouches, leaps (into the middle of the nearest
    /// group, a few strides at most), draws its fist back high at the top, and drives it down into the
    /// earth: an impact frame, the world holding its breath, a crater under the fist, and the land all
    /// around quakes: fissures crack out 6-9 tiles and rolling waves of earth race out one after another,
    /// each one bouncing every enemy it passes off its feet (thrown up and down on its back, again and
    /// again: they bounce), spraying rock along its front and breaking whatever stands on it. The fist
    /// stays buried, trembling, while the land rolls. Radius I 6, II 7.5, III 9 tiles; waves I 2, II 3,
    /// III 4; cracks 8 / 10 / 12. Damage to each foe (x the user's damage, less towards the rim):
    /// waves 0.9, 0.6, 0.5, 0.4.
    /// </summary>
    internal sealed class QuakeFistTech : Technique
    {
        public override string Id => "quake_fist";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Punch | Form.Area | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 42f, 68f, 100f };
        public override float[] Cooldown => new[] { 15f, 17f, 19f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down || link == Link.Scattered;
        }

        public static float Radius(int level)
        {
            return EarthKit.L(level, 6f, 7.5f, 9f);
        }

        public static int Waves(int level)
        {
            return EarthKit.L(level, 2, 3, 4);
        }

        private static readonly float[] WaveMults = { 0.9f, 0.6f, 0.5f, 0.4f };

        public static float WaveMult(int w)
        {
            return WaveMults[Mathf.Clamp(w, 0, WaveMults.Length - 1)];
        }

        public static float Total(int level)
        {
            float t = 0f;
            for (int i = 0; i < Waves(level); i++)
            {
                t += WaveMult(i);
            }
            return t;
        }

        private const float MaxLeap = 3f;
        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            Vector2 at = c.Cluster(r * 0.45f, 0f, r, out int count, out Actor center);
            if (center == null)
            {
                return null;
            }
            // it leaps into the middle of them (a few strides at most), onto solid ground
            Vector2 land = c.Pos;
            Vector2 d = at - c.Pos;
            if (d.magnitude > 0.8f)
            {
                Vector2 q = c.Pos + Vector2.ClampMagnitude(d, MaxLeap);
                if (Combat.SafeSpot(q) || Choreo.Wading(q))
                {
                    land = q;
                }
            }
            _near.Clear();
            c.CountNear(land, r, _near);
            float total = Total(level);
            float score = 0f;
            int n = 0;
            bool worthy = false;
            Actor best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                float dist = Vector2.Distance(v.current_position, land);
                n++;
                score += c.Value(v, c.Expected(v, total * Mathf.Lerp(1f, 0.55f, dist / r)), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 1.5f) * cm)
                {
                    worthy = true;
                }
                if (dist < bestD)
                {
                    bestD = dist;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                int need = c.Linked || c.Surrounded ? 2 : 3;
                if (n < need || !worthy && n < 5)
                {
                    return null;   // a quake for a crowd, not for a few peasants
                }
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = land,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
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
            Vector2 land = p.Point;
            m.Crouch(0.1f, 1.2f);
            // the leap: up (into the middle of them), coming down still high over its landing
            m.Leap(mm => (land - mm.Stance).sqrMagnitude > 0.04f && (Combat.SafeSpot(land) || Choreo.Wading(land)) ? land : mm.Stance,
                EarthKit.L(level, 0.9f, 1.1f, 1.4f), 0.32f, 0f, 0.7f);
            // the fist drawn back high at the top
            m.PoseTo(0.07f, new Shape(-24f, 0.88f, 1.2f, float.NaN), Ease.OutQuad, Tempo.Wind)
                .At(0.5f, mm =>
                {
                    if (mm.Vis && mm.Level >= 2)
                    {
                        Fx.Glint(mm.Hand(Limb.Fist) + new Vector2(0f, 2f * mm.Px), mm.Px, 0.16f, mm.Hue.Core, 1f);
                    }
                });
            // ...and down, fist first, into the earth
            Beat fall = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.09f,
                To = new Shape(42f, 0.9f, 1.16f, 0f, 0.12f),
                Ease = Ease.InQuad,
                Ghosts = 0.025f,
                Track = false,
                Forms = Form.Ground | Form.Punch | Form.Heavy | Form.Overhead
            };
            fall.Every = (mm, k) =>
            {
                if (mm.Vis && k < 0.9f)
                {
                    Kit.SpeedLines(mm.BodyPos() + new Vector2(0f, 3f * mm.Px), Vector2.down, mm.Px, 1, 4f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f));
                }
            };
            fall.Exit = Quake;
            m.Add(fall);
            m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.05f, To = new Shape(46f, 1.34f, 0.68f, 0f, 0.16f), Ease = Ease.OutExpo, Track = false });
            // the fist stays buried, trembling, while the land rolls
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.26f + 0.04f * level, Tremble = true, Track = false });
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>The fist meets the earth: the impact, the crater, and the quake that rolls out from it.</summary>
        private static void Quake(ChoreoMove m)
        {
            m.Pose.Height = 0f;
            int level = m.Level;
            float px = m.Px, k = m.Kk;
            Vector2 at = m.Pose.Ground + m.Dir * 0.32f * k;
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f);
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound(level >= 3 ? "event:/SFX/EXPLOSIONS/ExplosionMiddle" : "event:/SFX/HIT/HitStone", tile, 0.05f);
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 0.6f * px);
                if (level >= 2)
                {
                    Kit.ImpactFrame(hit, px, (0.4f + 0.08f * level) * k, m.Hue.Pal, false, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
                else
                {
                    Kit.Flash(hit, px, Mathf.RoundToInt(6f * k), 0.07f, Fx.WithA(m.Hue.Core, 0.95f));
                }
                ChoreoFx.Impact(hit, Vector2.down, px, k, 2f, m.Hue, m.TopLayer, true);
                Kit.DustRing(at, px, tile, 18, 64f, 0.6f, k);
                Kit.Debris(at, px, tile, 12, k);
                EarthKit.Boulders(at, px, 2 + level, k, EarthKit.Stone(at));
            }
            Kit.Stop(at, (0.04f + 0.02f * level) * m.ImpactK);
            Ruin.Blast(m.A, at, EarthKit.L(level, 1.2f, 1.4f, 1.6f), m.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, at, Scale.Heavy, 1.6f, k);
            m.HoldFor(0.05f * m.ImpactK);
            Areas.Add(new QuakeWaveHazard(m, at, r, level));
            m.SetLink(Link.Down, m.V);
        }
    }

    /// <summary>
    /// Quake Fist's quake: fissures crack out of the impact, and rolling waves of earth race out one
    /// after another (a dark ring with rock spraying along its front), each bouncing every enemy it
    /// passes off its feet onto its back (and again, smaller, as the next one passes), breaking what
    /// stands on the ground it rolls through. Flyers are left alone.
    /// </summary>
    internal sealed class QuakeWaveHazard : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _speed;
        private readonly int _level, _waves;
        private const float Gap = 0.2f;
        private readonly Dictionary<long, int> _waveOf = new Dictionary<long, int>();
        private readonly HashSet<long> _out = new HashSet<long>();
        private float _sprayT;
        private readonly float[] _breakT;
        private readonly bool[] _started;

        public QuakeWaveHazard(ChoreoMove m, Vector2 at, float r, int level) : base(m)
        {
            _at = at;
            _r = r;
            _level = level;
            _waves = QuakeFistTech.Waves(level);
            _speed = 13f * Mathf.Lerp(1f, K, 0.5f);
            _breakT = new float[_waves];
            _started = new bool[_waves];
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(at), 0.3f);
            EarthKit.Fissures(A, at, Px, r * 0.9f, EarthKit.L(level, 8, 10, 12), level >= 2 ? Hue.Edge : (Color?)null, Scale);
            if (Kit.Visible(at, 0.6f))
            {
                Kit.Crater(at, Px * K, Combat.TileAt(at), level >= 2, 8f, false, Hue.Edge);
            }
        }

        private static readonly List<Actor> _foes = new List<Actor>();

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_at, 0.6f);
            bool any = false;
            _sprayT -= dt;
            bool spray = _sprayT <= 0f;
            if (spray)
            {
                _sprayT = 0.04f;
            }
            for (int w = 0; w < _waves; w++)
            {
                float t = Age - w * Gap;
                if (t < 0f)
                {
                    any = true;
                    continue;
                }
                float rf = _speed * t;
                float was = _speed * Mathf.Max(0f, t - dt);
                if (was > _r + 0.4f)
                {
                    continue;
                }
                any = true;
                if (!_started[w])
                {
                    _started[w] = true;
                    StartWave(w, vis);
                }
                float front = Mathf.Min(rf, _r);
                if (vis && spray && rf <= _r)
                {
                    // rock and dust thrown up along the rolling front
                    for (int i = 0; i < 2; i++)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                        Vector2 p = _at + dir * front;
                        WorldTile tile = Combat.TileAt(p);
                        Kit.Debris(p, Px, tile, 1, K * 0.6f);
                        ChoreoFx.HeelDust(p, dir, Px, tile, K * 0.8f);
                    }
                }
                // what stands on it breaks as it rolls through (II and III)
                if (_level >= 2 && OwnerOk && rf <= _r)
                {
                    _breakT[w] -= dt;
                    if (_breakT[w] <= 0f)
                    {
                        _breakT[w] = _level >= 3 ? 0.09f : 0.14f;
                        float a = Random.value * Mathf.PI * 2f;
                        Vector2 p = Combat.Clamp(_at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * front);
                        Ruin.Blast(A, p, 0.6f, Scale, A.current_position, false);
                    }
                }
                if (OwnerOk)
                {
                    Roll(w, was, rf);
                }
            }
            return any;
        }

        private void StartWave(int w, bool vis)
        {
            if (OwnerOk)
            {
                Ruin.Quake(A, _at, Scale.Heavy, 1.2f - 0.15f * w, K);
            }
            if (!vis)
            {
                return;
            }
            float life = _r / _speed;
            Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(_at)), 0.35f);
            Fx.Ring(_at, Px, life, 3f, _r / Px, 3, 2, FxLayer.Ground, 5, Art.Ground, 1f, Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.7f), Fx.WithA(dark, 0f));
            Fx.Ring(_at, Px, life, 2f, _r / Px * 0.97f, 1, 1, FxLayer.Ground, 6, Art.Ground, 1f, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
        }

        /// <summary>The wave's front passes under them: each is bounced off its feet (each wave once).</summary>
        private void Roll(int w, float was, float rf)
        {
            Choreo.Foes(A, _at, Mathf.Min(rf, _r) + 0.3f, _foes, true);
            for (int i = 0; i < _foes.Count; i++)
            {
                Actor u = _foes[i];
                if (u.isFlying() || Combat.HeightOf(u) > 1.4f)
                {
                    continue;
                }
                long id = u.Sid();
                if (_out.Contains(id) || _waveOf.TryGetValue(id, out int last) && last >= w)
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, _at);
                if (d < was - 0.6f || d > rf + 0.3f || d > _r + 0.3f)
                {
                    continue;
                }
                if (!Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                if (!_waveOf.ContainsKey(id) && Combat.Reach(M, u, true) != Contact.Hit)
                {
                    _out.Add(id);   // it rode it out (an evasion): the quake leaves it be
                    continue;
                }
                _waveOf[id] = w;
                float fall = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(d / _r));
                Combat.Hit(A, u, QuakeFistTech.WaveMult(w) * fall * cm, false, false, 0f, M);
                Vector2 away = u.current_position - _at;
                Impact im = Impact.Of(Hurt.Knockdown, away.sqrMagnitude > 1e-4f ? away : Random.insideUnitCircle, 0.25f, 1.1f);
                im.Lift = Mathf.Max(0.25f, 0.85f - 0.15f * w) * (0.7f + 0.3f * fall);
                im.Daze = w == _waves - 1 ? 1f : 0.7f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.05f);
                if (Kit.Visible(u.current_position, 0.3f))
                {
                    Kit.DustRing(u.current_position, Px, u.current_tile, 5, 26f, 0.35f, K * 0.8f);
                }
            }
        }
    }
}
