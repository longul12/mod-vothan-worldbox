using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TORNADO PILLAR (Tempest Leg, Grandmaster). It blurs into the thick of them (when they are not
    /// already all around it), lifts one knee, and starts to turn on one leg, faster and faster, dust
    /// swirling in, until it is spinning too fast to see inside a column of wind that climbs from the
    /// earth to the sky, a funnel of rings widening as it rises, debris whirling up it, rising a little
    /// off the ground at its heart. Every foe within the pull is dragged in across the ground, whirled
    /// round it and battered, and at the end one last roundhouse out of the spin and the pillar flings
    /// them all high and far (Scattered). The ground under the pillar is scoured and whatever stands
    /// close round it is torn apart (a Grandmaster's blow breaks every building it lands on).
    /// I: pulls from 4 tiles, a 6-tile pillar; II: 5 tiles, 8; III: 6 tiles, a 10-tile pillar burning
    /// white at its core, flinging them farther. Damage (x the user's damage) on each caught: I 1.6,
    /// II 2.1, III 2.8 (a tenth of it every whirl, the rest on the fling).
    /// </summary>
    internal sealed class TornadoPillarTech : Technique
    {
        public override string Id => "tornado_pillar";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Spin | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 45f, 70f, 105f };
        public override float[] Cooldown => new[] { 15f, 16f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Pull(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.1f : 2.8f;
        }

        private static float Fling(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 4f : 5f;
        }

        private static float Spin(int level)
        {
            return level <= 1 ? 1.25f : level == 2 ? 1.45f : 1.65f;
        }

        private static float Column(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 8f : 10f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Pull(level) * Mathf.Lerp(1f, k, 0.5f);
            // where it spins: right here, or a blur dash into the thick of them
            Vector2 at = c.Pos;
            Actor center = null;
            List<Actor> list = TempestHigh.Tmp;
            list.Clear();
            int here = c.CountNear(c.Pos, r, list);
            Plan area = Choreo.PickArea(c, this, level, r * 0.6f, 4f * k, Mult(level), 2);
            if (area != null && Vector2.Distance(area.Point, c.Pos) > 1.2f * k)
            {
                Vector2 q = Combat.Clamp(area.Point);
                if ((Combat.SafeSpot(q) || Choreo.Wading(q)) && Combat.ClearPath(c.Pos, q))
                {
                    list.Clear();
                    int there = c.CountNear(q, r, list);
                    if (there > here)
                    {
                        at = q;
                        center = area.Target;
                    }
                    else
                    {
                        list.Clear();
                        c.CountNear(c.Pos, r, list);
                    }
                }
            }
            int n = list.Count;
            if (n == 0 || n < 3 && !c.Forced && !(c.Linked && n >= 2) && !(c.Surrounded && n >= 2))
            {
                return null;
            }
            if (c.InWater && !c.Forced)
            {
                return null;
            }
            float score = 0f;
            Actor nearest = null;
            float nd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
                float d = (v.current_position - at).sqrMagnitude;
                if (d < nd)
                {
                    nd = d;
                    nearest = v;
                }
            }
            Actor t = center ?? nearest;
            Vector2 dir = t != null ? t.current_position - c.Pos : Vector2.right;
            return new Plan
            {
                Target = t,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(1f, score) : score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        private sealed class State
        {
            public float T, Turn, Dust;
            public float Face;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            State st = new State();
            Hue wind = m.Hue.As(Element.Wind);
            if (Vector2.Distance(a.current_position, p.Point) > 1.2f * m.Kk)
            {
                m.Dash(ChoreoMove.ToPoint, 0.02f);
            }

            // one knee up: the coil
            Beat coil = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.14f,
                To = new Shape(-10f, 0.92f, 1.1f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Kick | Form.Spin
            };
            coil.Enter = mm =>
            {
                st.Face = mm.Fwd;
                if (mm.Vis)
                {
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.2f, 18f * mm.Kk, 4f, 1, 2, FxLayer.Ground, 5, Art.Ground, 1.6f,
                        Fx.WithA(wind.Edge, 0f), Fx.WithA(wind.Edge, 0.9f), Fx.WithA(wind.Core, 1f));
                    ChoreoFx.Motes(mm.BodyPos(), mm.Px, 8, 16f * mm.Kk, 0.16f, wind.Core, wind.Edge);
                }
            };
            m.Add(coil);

            // the spin-up: faster and faster on one leg, dust drawn in
            Beat up = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.32f,
                To = new Shape(0f, 1f, 1.06f, 0.08f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Kick | Form.Spin | Form.Area
            };
            up.Enter = mm =>
            {
                st.Turn = 0f;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            };
            up.Every = (mm, kk) =>
            {
                // the turn rate climbs from one to six turns a second
                st.Turn += Clock.Dt * Mathf.Lerp(1f, 6f, kk);
                Turn(mm, st);
                st.Dust -= Clock.Dt;
                if (st.Dust <= 0f && mm.Vis)
                {
                    st.Dust = 0.05f;
                    float ang = Random.value * Mathf.PI * 2f;
                    Vector2 g = mm.Pose.Ground + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * Random.Range(0.6f, 1.6f) * mm.Kk;
                    ChoreoFx.HeelDust(g, Choreo.Perp(g - mm.Pose.Ground), mm.Px, mm.A.current_tile, mm.Kk);
                    TempestHigh.WindRing(mm.Pose.Ground, mm.Px, mm.Kk, wind, 8f + 8f * kk, 0.2f, FxLayer.Ground);
                }
            };
            m.Add(up);

            // the pillar: a column of wind from the earth to the sky; it spins at its heart
            Beat pillar = new Beat
            {
                Tempo = Tempo.Act,
                Dur = Spin(level),
                To = new Shape(0f, 1f, 1.08f, 0.55f),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.04f,
                Forms = Form.Kick | Form.Spin | Form.Area
            };
            pillar.Enter = mm =>
            {
                float r = Pull(mm.Level) * Mathf.Lerp(1f, mm.Kk, 0.5f);
                Vector2 at = mm.Stance;
                // (the engine's tornado drags in, whirls and flings; the pillar is drawn and scours on top of it)
                Areas.Add(new TornadoHazard(mm, at, r, mm.BeatDur, Mult(mm.Level), Fling(mm.Level)));
                Areas.Add(new TornadoPillarColumn(mm, at, r, mm.BeatDur, Column(mm.Level) * mm.Kk, wind));
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", mm.A.current_tile, 0.3f);
                Ruin.Quake(mm.A, at, Scale.Heavy, 1f, mm.Kk);
            };
            pillar.Every = (mm, kk) =>
            {
                st.Turn += Clock.Dt * 7f;
                Turn(mm, st);
            };
            m.Add(pillar);

            // the release: one roundhouse out of the spin as the pillar flings them
            Beat release = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.1f,
                To = new Shape(16f, 1.22f, 0.88f, 0.2f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Kick | Form.Spin | Form.Area
            };
            release.Enter = mm =>
            {
                mm.Pose.FaceRight = st.Face > 0f;
                if (mm.Vis)
                {
                    Kit.Swish(mm, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(12f * mm.Kk), 6, 40), 0.42f, 190f, 550f, 3), 0.2f, new Vector2(0f, 1f), st.Face,
                        wind.Core, wind.Edge, 7);
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.35f, 6f, Pull(mm.Level) / mm.Px * 1.1f, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                        Fx.WithA(Color.white, 0.95f), Fx.WithA(wind.Edge, 0.6f), Fx.WithA(mm.Hue.Rim, 0f));
                    Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 16, 70f, 0.6f, mm.Kk);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                Ruin.Quake(mm.A, mm.Pose.Ground, Scale.Heavy, 1.3f, mm.Kk);
            };
            m.Add(release);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.14f, To = new Shape(10f, 1.1f, 0.94f, 0f), Ease = Ease.InQuad, Track = false });
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>A 2D turn on the spot: the sprite narrows, flips, widens, round and round.</summary>
        private static void Turn(ChoreoMove m, State st)
        {
            float c = Mathf.Cos(Mathf.PI * 2f * st.Turn);
            m.Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
            float frac = st.Turn - Mathf.Floor(st.Turn);
            bool back = frac > 0.25f && frac < 0.75f;
            m.Pose.FaceRight = back ? st.Face < 0f : st.Face > 0f;
        }
    }

    /// <summary>
    /// Tornado Pillar's column: a funnel of wind rings from the ground to the sky (wider as it climbs),
    /// a bright core, debris whirling up it, dust scoured off the ground at its foot. The ground under
    /// it is torn (a crater at its foot as it forms) and whatever stands close round it is broken as
    /// it dies (Ruin). It draws nothing off screen.
    /// </summary>
    internal sealed class TornadoPillarColumn : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _dur, _height;
        private readonly Hue _wind;
        private float _ringT, _streakT, _coreT, _rockT, _dustT, _shakeT;
        private bool _scoured, _torn;

        public TornadoPillarColumn(ChoreoMove m, Vector2 at, float r, float dur, float height, Hue wind) : base(m)
        {
            _at = at;
            _r = r;
            _dur = Mathf.Max(0.3f, dur);
            _height = Mathf.Max(2f, height);
            _wind = wind;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float k = Age / _dur;
            if (!_scoured && Age >= 0.12f)
            {
                _scoured = true;
                if (OwnerOk)
                {
                    Ruin.Blast(A, _at, 1f + 0.25f * M.Level, Scale, A.current_position, false);
                }
            }
            if (k >= 1f)
            {
                if (!_torn)
                {
                    _torn = true;
                    if (OwnerOk)
                    {
                        // the fling tears up whatever stands close round the pillar
                        Ruin.Blast(A, _at, _r * 0.45f, Scale, A.current_position, false);
                    }
                }
                return Age < _dur + 0.2f && Draw(dt, 1f - Mathf.Clamp01((Age - _dur) / 0.2f));
            }
            Draw(dt, 1f);
            _shakeT -= dt;
            if (_shakeT <= 0f)
            {
                _shakeT = 0.25f;
                Ruin.Quake(A, _at, Scale, 0.7f, K);
            }
            return true;
        }

        private bool Draw(float dt, float fade)
        {
            if (!Kit.Visible(_at, 0.6f) && !Kit.Visible(_at + new Vector2(0f, _height * 0.5f), 0.6f))
            {
                return true;
            }
            float px = Px;
            float grow = Mathf.Clamp01(Age / 0.25f);
            float top = _height * grow;
            int level = M != null ? M.Level : 2;
            // rings of the funnel: wider the higher, swaying, rising
            _ringT -= dt;
            while (_ringT <= 0f)
            {
                _ringT += 0.009f;
                float h = Random.Range(0f, top);
                float u = h / _height;
                int rr = Mathf.Clamp(Mathf.RoundToInt((5f + u * (12f + 3f * level)) * K), 2, 120);
                Vector2 c = _at + new Vector2(Mathf.Sin(Age * 7f + h * 1.3f) * 0.18f * K * (0.4f + u), h);
                Particle p = Fx.Spawn(Art.Ring(rr, Random.value < 0.35f ? 2 : 1, 0.3f), c, px, 0.22f, h < 0.6f ? FxLayer.Over : FxLayer.Top, 10);
                if (p != null)
                {
                    p.Moving(new Vector2(0f, 4f * K));
                    Color body = Random.value < 0.5f ? _wind.Main : Color.Lerp(_wind.Main, _wind.Edge, 0.5f);
                    p.Color(Fx.WithA(_wind.Core, 0.8f * fade), Fx.WithA(body, 0.65f * fade), Fx.WithA(_wind.Edge, 0f));
                }
            }
            // streaks of wind whipping round it (the near side in front of the bodies, the far side behind)
            _streakT -= dt;
            while (_streakT <= 0f)
            {
                _streakT += 0.012f;
                float h = Random.Range(0f, top);
                float u = h / _height;
                float w = (5f + u * (12f + 3f * level)) * K * px;
                float ph = Random.value * Mathf.PI * 2f;
                bool front = Mathf.Sin(ph) < 0f;
                Vector2 c = _at + new Vector2(Mathf.Cos(ph) * w, h + Mathf.Sin(ph) * w * 0.3f);
                Vector2 d = new Vector2(-Mathf.Sin(ph), Mathf.Cos(ph) * 0.3f).normalized * w * 0.6f;
                Kit.Line(c - d, c + d, px, 0.08f, 1, front ? FxLayer.Top : FxLayer.Behind, front ? 11 : 1,
                    Fx.WithA(Color.white, (front ? 0.85f : 0.5f) * fade), Fx.WithA(_wind.Main, 0.4f * fade), Fx.WithA(_wind.Edge, 0f));
            }
            // its bright core
            _coreT -= dt;
            if (_coreT <= 0f)
            {
                _coreT = 0.05f;
                Vector2 b = _at + new Vector2(0f, 0.2f * K);
                Kit.Line(b, b + new Vector2(Mathf.Sin(Age * 5f) * 0.1f, top), px, 0.07f, level >= 3 ? 3 : 2, FxLayer.Top, 9,
                    Fx.WithA(level >= 3 ? Color.white : _wind.Core, 0.55f * fade), Fx.WithA(_wind.Edge, 0f));
            }
            // debris whirled up it
            _rockT -= dt;
            if (_rockT <= 0f)
            {
                _rockT = 0.05f;
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 from = _at + dir * Random.Range(0.4f, 1f) * _r * 0.5f;
                WorldTile t = Combat.TileAt(from);
                Color soil = Fx.SoilColor(t);
                Particle q = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], from, px, Random.Range(0.5f, 0.9f), FxLayer.Top, 8);
                if (q != null)
                {
                    Vector2 swirl = Choreo.Perp(dir);
                    q.Moving((swirl * 3.5f - dir * 1.5f) * K, Random.Range(3f, 6f) * K, 1.2f);
                    q.Spin = Random.Range(-720f, 720f);
                    q.Color(soil, soil, Fx.WithA(soil, 0f));
                }
            }
            // dust scoured off the ground at its foot
            _dustT -= dt;
            if (_dustT <= 0f)
            {
                _dustT = 0.04f;
                float a = Random.value * Mathf.PI * 2f;
                Vector2 g = _at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Random.Range(0.3f, 1f) * _r * 0.6f;
                ChoreoFx.HeelDust(g, Choreo.Perp(g - _at), px, Combat.TileAt(g), K);
            }
            return true;
        }
    }
}
