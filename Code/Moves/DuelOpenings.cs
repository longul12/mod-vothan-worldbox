using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// More ways for a duel to begin (and new exchanges for its middle), each in the duelists' own schools:
    ///   Salvo      they open from range with their school's own projectiles (fists of air, wind crescents,
    ///              sword waves, orbs of ki, boulders, shadow discs, javelins): volley after volley meets in
    ///              the middle and bursts, until one side's overpowers the other's and hits home
    ///   AuraClash  both flare up and let their power out at each other: two pressures, each in its
    ///              school's colours, push against each other across the ground between them, which cracks
    ///              and splits (a God's splits the land) until both are thrown back from the seam
    ///   Blitz      both vanish; only the meetings are seen, one here, one there, faster and faster, each a
    ///              burst of both their schools' marks (a God's leave a field of craters), until they stand
    ///              where they began
    /// Duel.Opening weighs them with the old ceremonies (stare-down, first clash, the Clash of Heavens) and
    /// never opens two duels of the same fighter the same way twice running.
    /// </summary>
    internal static class Openers
    {
        private static readonly Dictionary<long, string> _last = new Dictionary<long, string>();

        /// <summary>Fills the opening (true: it did). 'gods': two Gods; 'expert': both Expert or better.</summary>
        public static bool Fill(Duel d, List<Exchange> q, bool gods, bool expert, bool rivals)
        {
            _last.TryGetValue(d.IdA, out string la);
            _last.TryGetValue(d.IdB, out string lb);
            List<KeyValuePair<string, float>> w = new List<KeyValuePair<string, float>>
            {
                new KeyValuePair<string, float>("salvo", 1.2f),
                new KeyValuePair<string, float>("aura", expert ? 1.2f : 0.6f),
                new KeyValuePair<string, float>("blitz", expert ? 1f : 0.4f),
                new KeyValuePair<string, float>("stare", expert || rivals ? 1f : 0.3f),
                new KeyValuePair<string, float>("clash", 1f),
                new KeyValuePair<string, float>("none", 0.4f),
            };
            if (gods)
            {
                w.Add(new KeyValuePair<string, float>("heavens", 1.3f));
            }
            float total = 0f;
            for (int i = 0; i < w.Count; i++)
            {
                float v = w[i].Value * (w[i].Key == la || w[i].Key == lb ? 0.2f : 1f) * Random.Range(0.8f, 1.2f);
                w[i] = new KeyValuePair<string, float>(w[i].Key, v);
                total += v;
            }
            float r = Random.value * total;
            string pick = "clash";
            foreach (KeyValuePair<string, float> kv in w)
            {
                r -= kv.Value;
                if (r <= 0f)
                {
                    pick = kv.Key;
                    break;
                }
            }
            _last[d.IdA] = pick;
            _last[d.IdB] = pick;
            if (_last.Count > 2000)
            {
                _last.Clear();
            }
            switch (pick)
            {
                case "salvo":
                    q.Add(new Salvo());
                    q.Add(new FirstClash());
                    break;
                case "aura":
                    if (rivals || expert && Random.value < 0.4f)
                    {
                        q.Add(new StareDown());
                    }
                    q.Add(new AuraClash());
                    q.Add(new FirstClash());
                    break;
                case "blitz":
                    q.Add(new Blitz());
                    break;
                case "stare":
                    q.Add(new StareDown());
                    q.Add(gods ? (Exchange)new Heavens() : new FirstClash());
                    break;
                case "heavens":
                    q.Add(new Heavens());
                    break;
                case "clash":
                    q.Add(new FirstClash());
                    break;
            }
            Duels.Trace("duel " + d.IdA + "/" + d.IdB + " opens: " + pick);
            return true;
        }

        /// <summary>A school's thrown thing, drawn small at 'k' size: its sprite frames and whether it spins.</summary>
        public static Sprite[] Missile(School s, float k, out bool spin)
        {
            spin = false;
            switch (s)
            {
                case School.IronFist: return new[] { ChoreoArt.Fist(Mathf.RoundToInt(4f * k)) };
                case School.TempestLeg: return new[] { ChoreoArt.Crescent(Mathf.RoundToInt(5f * k), 1) };
                case School.ThousandBlades: return new[] { ChoreoArt.Crescent(Mathf.RoundToInt(7f * k), 2) };
                case School.EarthBreaker:
                    spin = true;
                    return new[] { ChoreoArt.Boulder(Mathf.RoundToInt(3f * k), Random.Range(0, 4)) };
                case School.PhantomStep: return ChoreoArt.Disc(Mathf.RoundToInt(3.5f * k));
                case School.HuntersWay: return new[] { ChoreoArt.Smear(Mathf.RoundToInt(10f * k), 0, 1)[0] };
                default: return new[] { Art.Disk(Mathf.Max(1, Mathf.RoundToInt(2.5f * k)), 1f, 0.3f) };
            }
        }

        /// <summary>Fires one school missile from 'from' to 'to' in 'dur' seconds (a particle: it only shows).</summary>
        public static void Fly(School s, Hue h, Vector2 from, Vector2 to, float dur, float px, float k)
        {
            Sprite[] sp = Missile(s, k, out bool spin);
            Particle p = Fx.Spawn(sp, from, px, dur, FxLayer.Top, 21);
            if (p == null)
            {
                return;
            }
            Vector2 d = to - from;
            p.Moving(d / Mathf.Max(0.05f, dur));
            if (sp.Length > 1)
            {
                p.Fps = 24f;
                p.Loop = true;
            }
            else if (spin)
            {
                p.Spin = 700f;
            }
            else
            {
                p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }
            Color c = s == School.EarthBreaker ? Fx.SoilColor(Combat.TileAt(from)) : s == School.HuntersWay ? new Color(0.66f, 0.48f, 0.3f, 1f) : Color.Lerp(h.Main, Color.white, 0.25f);
            p.Color(c, c);
            // its streak
            Particle t = Kit.Line(from, from + d * 0.3f, px, dur * 0.8f, 1, FxLayer.Top, 20, Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
            t?.Moving(d / Mathf.Max(0.05f, dur));
        }
    }

    // ===================================================================== SALVO ==

    internal sealed class Salvo : Exchange
    {
        private const int Set = 0, Volley = 1, Last = 2, Hit = 3, Settle_ = 4;
        private Vector2 _sa, _sb, _mid;
        private int _volleys, _shot, _burst;
        private float _flight;
        private Clash _c;
        private bool _hitDone;
        private Knock _kb;
        private School _schA, _schB;
        private Hue _ha, _hb;

        public override string Id => "salvo";

        public override Form Forms => Form.Projectile | Form.Line;

        public override float Weight()
        {
            return 0.8f;
        }

        public override bool Plan()
        {
            float gap = (D.Gods ? 7f : D.Grand ? 6f : 5f);
            if (!DuelGeo.Stance(A0, B0, gap * K, out _sa, out _sb) && !DuelGeo.Stance(A0, B0, 3.5f * K, out _sa, out _sb))
            {
                return false;
            }
            _mid = (_sa + _sb) * 0.5f;
            _volleys = D.Gods ? 4 : D.Grand ? 3 : 2;
            _flight = 0.22f;
            return true;
        }

        public override void Start()
        {
            _schA = SchoolStyle.Of(P);
            _schB = SchoolStyle.Of(Q);
            _ha = Hue.For(P.A, _schA, Mathf.Clamp(L, 1, 3));
            _hb = Hue.For(Q.A, _schB, Mathf.Clamp(L, 1, 3));
        }

        private float Size => D.Gods ? 2f : D.Grand ? 1.6f : L >= 3 ? 1.3f : 1f;

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / 0.22f);
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(Volley);
                        }
                        break;
                    }
                case Volley:
                    {
                        // both throw on every beat; each pair meets in the middle and bursts
                        float beat = Rest(0.42f) + 0.12f;
                        while (_shot < _volleys && Pt >= beat * _shot)
                        {
                            _shot++;
                            Throw(P, _schA, _ha, _sa, _mid);
                            Throw(Q, _schB, _hb, _sb, _mid);
                        }
                        while (_burst < _shot && Pt >= beat * _burst + _flight)
                        {
                            _burst++;
                            Burst(_mid, 1f);
                        }
                        Pose(P, Pt, beat);
                        Pose(Q, Pt, beat);
                        if (_burst >= _volleys && Pt >= beat * (_volleys - 1) + _flight + 0.12f)
                        {
                            Next(Last);
                        }
                        break;
                    }
                case Last:
                    {
                        // the last volley: one side's overpowers the other's and goes on through
                        if (Enter())
                        {
                            _c = D.Roll();
                            Throw(P, _schA, _ha, _sa, _mid);
                            Throw(Q, _schB, _hb, _sb, _mid);
                        }
                        if (Pt >= _flight && !_hitDone)
                        {
                            _hitDone = true;
                            Burst(_mid, 1.4f);
                            if (!_c.Even)
                            {
                                Duelist w = Winner(_c), l = Loser(_c);
                                Vector2 lfeet = l == P ? _sa : _sb;
                                School ws = w == P ? _schA : _schB;
                                Hue wh = w == P ? _ha : _hb;
                                Openers.Fly(ws, wh, _mid, l.BodyPos(), _flight * 0.8f, Px, K * Size);
                            }
                            Next(Hit);
                        }
                        Pose(P, Pt, 1f);
                        Pose(Q, Pt, 1f);
                        break;
                    }
                case Hit:
                    {
                        if (Pt >= _flight * 0.8f && Enter())
                        {
                            if (_c.Even)
                            {
                                Opens(null, Link.None);
                            }
                            else
                            {
                                Duelist w = Winner(_c), l = Loser(_c);
                                Strike(w, l, _c.Kind == ClashKind.Overwhelm ? 0.8f : 0.5f, true);
                                Vector2 lf = l.Pose.Ground;
                                Vector2 away = (lf - w.Pose.Ground).normalized;
                                _kb = Knock.Skid(lf, Combat.Clamp(lf + away * 0.8f * K), 0.3f, -18f, 0.25f);
                                Wreck(lf, 0.8f, L >= 3 ? 2 : 1, w);
                                Opens(w, Link.Close);
                            }
                        }
                        if (_kb.Active)
                        {
                            Duelist l = Loser(_c);
                            _kb.Step(l, dt);
                        }
                        if (Pt >= 0.6f)
                        {
                            Next(Settle_);
                        }
                        break;
                    }
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1f);
                    if (Pt >= Rest(0.3f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void Throw(Duelist m, School s, Hue h, Vector2 from, Vector2 to)
        {
            if (Vis)
            {
                Openers.Fly(s, h, m.BodyPos() + new Vector2(m.Fwd * 2f * m.Px, 0f), (P.BodyPos() + Q.BodyPos()) * 0.5f, _flight, Px, K * Size);
            }
            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
        }

        private static void Pose(Duelist m, float t, float beat)
        {
            // the throw: a lunge on each beat, back to guard in between
            float k = Mathf.Repeat(t, Mathf.Max(0.1f, beat)) / Mathf.Max(0.1f, beat);
            float s = k < 0.3f ? k / 0.3f : 1f - (k - 0.3f) / 0.7f;
            m.Pose.Rot = 16f * s;
            m.Pose.SX = 1f + 0.12f * s;
            m.Pose.SY = 1f - 0.1f * s;
            m.Pose.Height = 0f;
        }

        private void Burst(Vector2 ground, float size)
        {
            Vector2 at = (P.BodyPos() + Q.BodyPos()) * 0.5f;
            if (Vis)
            {
                DuelFx.Starburst(at, Px, (0.8f + 0.5f * size) * K * Size * 0.6f, Color.white, _ha.Main, _hb.Main, 0.12f);
                SchoolStyle.Emblem(_schA, at, ground, Axis(), Px, K * Size * 0.8f, size, _ha);
                SchoolStyle.Emblem(_schB, at, ground, -Axis(), Px, K * Size * 0.8f, size, _hb);
            }
            D.Sound("event:/SFX/HIT/HitGeneric");
            if (L >= 2)
            {
                Wreck(ground, 0.6f * size * Size, L >= 3 ? 2 : 1, null);
            }
            Stop(0.05f);
        }
    }

    // ================================================================= AURA CLASH ==

    internal sealed class AuraClash : Exchange
    {
        private const int Set = 0, Flare = 1, Push = 2, Break = 3, Settle_ = 4;
        private Vector2 _sa, _sb, _mid;
        private Knock _ka, _kb;
        private School _schA, _schB;
        private Hue _ha, _hb;
        private float _fx;

        public override string Id => "aura_clash";

        public override Form Forms => Form.Charge | Form.Area;

        public override float Weight()
        {
            return D.Low >= Tier.Expert ? 0.7f : 0f;
        }

        public override bool Plan()
        {
            float gap = D.Gods ? 5f : 4f;
            return DuelGeo.Stance(A0, B0, gap * K, out _sa, out _sb) || DuelGeo.Stance(A0, B0, 2.8f * K, out _sa, out _sb);
        }

        public override void Start()
        {
            _mid = (_sa + _sb) * 0.5f;
            _schA = SchoolStyle.Of(P);
            _schB = SchoolStyle.Of(Q);
            _ha = Hue.For(P.A, _schA, Mathf.Clamp(L, 1, 3));
            _hb = Hue.For(Q.A, _schB, Mathf.Clamp(L, 1, 3));
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / 0.22f);
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/POWERS/Powerup");
                            Next(Flare);
                        }
                        break;
                    }
                case Flare:
                    {
                        float dur = Rest(0.7f) + 0.25f;
                        float k = Mathf.Clamp01(Pt / dur);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            float e = Anim.OutQuad(k);
                            m.Pose.Rot = -6f * e;
                            m.Pose.SX = 1f - 0.05f * e;
                            m.Pose.SY = 1f + 0.12f * e;
                        }
                        _fx -= dt;
                        if (Vis && _fx <= 0f)
                        {
                            _fx = 0.05f;
                            DuelFx.Rising(P.BodyPos(), Px, 2, 1.2f * K, _ha.Main);
                            DuelFx.Rising(Q.BodyPos(), Px, 2, 1.2f * K, _hb.Main);
                            Trails.Ghost(P.A, 0.15f, Fx.WithA(_ha.Main, 0.6f), Fx.WithA(_ha.Edge, 0f));
                            Trails.Ghost(Q.A, 0.15f, Fx.WithA(_hb.Main, 0.6f), Fx.WithA(_hb.Edge, 0f));
                            if (L >= 2)
                            {
                                Spec.Rocks(P.Pose.Ground, Px, 1, 50f, K);
                                Spec.Rocks(Q.Pose.Ground, Px, 1, 50f, K);
                            }
                        }
                        if (k >= 1f)
                        {
                            Next(Push);
                        }
                        break;
                    }
                case Push:
                    {
                        // two pressures meeting: a seam of sparks in the middle, the ground cracking under it
                        float dur = Rest(0.8f) + 0.35f;
                        float k = Mathf.Clamp01(Pt / dur);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 14f;
                            m.Pose.SX = 1.1f;
                            m.Pose.SY = 0.92f;
                            m.Pose.Nudge = new Vector2(((int)(Pt * 70f) & 1) == 0 ? 0.4f : -0.4f, 0f);
                        }
                        _fx -= dt;
                        if (Vis && _fx <= 0f)
                        {
                            _fx = 0.04f;
                            Vector2 seam = _mid + new Vector2(0f, Random.Range(0.1f, 1.4f) * K) + Random.insideUnitCircle * 0.2f * K;
                            DuelFx.Wind(P.BodyPos(), seam, Px, Combat.TileAt(_mid), Fx.WithA(_ha.Main, 0.7f));
                            DuelFx.Wind(Q.BodyPos(), seam, Px, Combat.TileAt(_mid), Fx.WithA(_hb.Main, 0.7f));
                            DuelFx.Spray(seam, Px, 3, Vector2.up, false, Random.value < 0.5f ? _ha.Main : _hb.Main);
                            if (Random.value < 0.3f + 0.4f * k)
                            {
                                Vector2 dir = new Vector2(Random.Range(-0.4f, 0.4f), Random.value < 0.5f ? 1f : -1f).normalized;
                                DuelFx.Fissure(_mid, _mid + dir * Random.Range(0.6f, 2f + (D.Gods ? 3f : 0f)) * K, Px, Combat.TileAt(_mid), Color.Lerp(_ha.Main, _hb.Main, Random.value), 1);
                            }
                        }
                        if (k >= 1f)
                        {
                            Next(Break);
                        }
                        break;
                    }
                case Break:
                    {
                        if (Enter())
                        {
                            Vector2 at = (P.BodyPos() + Q.BodyPos()) * 0.5f;
                            if (Vis)
                            {
                                DuelFx.Starburst(at, Px, (D.Gods ? 3f : 1.6f) * K, Color.white, _ha.Main, _hb.Main, 0.16f);
                                DuelFx.GroundRing(_mid, Px, 2f, (D.Gods ? 70f : 34f) * K, 0.45f, 3, 1, Color.white, Fx.WithA(_ha.Main, 0.6f), Fx.WithA(_hb.Main, 0f));
                                if (D.Gods)
                                {
                                    CineKit.ViewFlash(Fx.WithA(Color.white, 0.4f), 0.2f);
                                }
                            }
                            D.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall");
                            Wreck(_mid, D.Gods ? 2.5f : D.Grand ? 1.6f : 1f, L >= 3 ? 3 : 2, null);
                            if (D.Grand)
                            {
                                // the seam splits the land across the field
                                Vector2 ax = (_sb - _sa).normalized;
                                Vector2 side = new Vector2(-ax.y, ax.x);
                                float len = (D.Gods ? 7f : 4f) * K;
                                D.Furrow(Combat.Clamp(_mid - side * len), Combat.Clamp(_mid + side * len), 0.8f, 2, null);
                            }
                            Stop(0.1f);
                            Vector2 a0 = P.Pose.Ground, b0 = Q.Pose.Ground;
                            Vector2 axis = (b0 - a0).normalized;
                            _ka = Knock.Skid(a0, Combat.Clamp(a0 - axis * 0.9f * K), 0.35f, -16f, 0.2f);
                            _kb = Knock.Skid(b0, Combat.Clamp(b0 + axis * 0.9f * K), 0.35f, -16f, 0.2f);
                            Opens(null, Link.None);
                        }
                        bool a = _ka.Step(P, dt), b = _kb.Step(Q, dt);
                        if (a && b || Pt > 0.8f)
                        {
                            Next(Settle_);
                        }
                        break;
                    }
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1f);
                    if (Pt >= Rest(0.25f))
                    {
                        Done = true;
                    }
                    break;
            }
        }
    }

    // ===================================================================== BLITZ ==

    internal sealed class Blitz : Exchange
    {
        private const int Vanish = 0, Meet = 1, Return = 2, Settle_ = 3;
        private readonly List<Vector2> _spots = new List<Vector2>();
        private Vector2 _sa, _sb;
        private int _n;
        private School _schA, _schB;
        private Hue _ha, _hb;
        private Clash _c;

        public override string Id => "blitz";

        public override Form Forms => Form.Fast | Form.Dash | Form.Teleport;

        public override float Weight()
        {
            return D.Low >= Tier.Expert ? 0.7f : 0f;
        }

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 2.2f * K, out _sa, out _sb))
            {
                _sa = A0;
                _sb = B0;
            }
            Vector2 mid = (_sa + _sb) * 0.5f;
            int n = D.Gods ? 7 : D.Grand ? 6 : D.Flow ? 5 : 4;
            float r = (D.Gods ? 5f : D.Grand ? 4f : 3f) * K;
            for (int i = 0; i < n * 3 && _spots.Count < n; i++)
            {
                Vector2 p = Combat.Clamp(mid + Random.insideUnitCircle * r);
                if (Combat.SafeSpot(p))
                {
                    _spots.Add(p);
                }
            }
            return _spots.Count >= 3;
        }

        public override void Start()
        {
            _schA = SchoolStyle.Of(P);
            _schB = SchoolStyle.Of(Q);
            _ha = Hue.For(P.A, _schA, Mathf.Clamp(L, 1, 3));
            _hb = Hue.For(Q.A, _schB, Mathf.Clamp(L, 1, 3));
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Vanish:
                    {
                        if (Enter())
                        {
                            if (Vis)
                            {
                                DuelFx.Blink(P.Pose.Ground, Px, K, Color.white, _ha.Main);
                                DuelFx.Blink(Q.Pose.Ground, Px, K, Color.white, _hb.Main);
                            }
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                        }
                        P.Pose.Hidden = Q.Pose.Hidden = true;
                        if (Pt >= 0.12f)
                        {
                            Next(Meet);
                        }
                        break;
                    }
                case Meet:
                    {
                        // a meeting here, then there, faster and faster
                        float beat = Mathf.Lerp(0.2f, 0.09f, _n / (float)_spots.Count) * Mathf.Max(0.6f, Brisk * 1.6f);
                        if (Pt >= beat)
                        {
                            Pt = 0f;
                            if (_n < _spots.Count)
                            {
                                Vector2 s = _spots[_n];
                                _n++;
                                Vector2 dir = Random.insideUnitCircle.normalized;
                                dir.y *= 0.4f;
                                P.Pose.Hidden = Q.Pose.Hidden = false;
                                P.Pose.Ground = Combat.Clamp(s - dir * 0.35f * K);
                                Q.Pose.Ground = Combat.Clamp(s + dir * 0.35f * K);
                                P.Pose.Height = Q.Pose.Height = _n % 3 == 0 ? 0.8f * K : 0f;
                                FaceEach();
                                P.Pose.Rot = Q.Pose.Rot = 20f;
                                Vector2 at = (P.BodyPos() + Q.BodyPos()) * 0.5f;
                                if (Vis)
                                {
                                    Fx.Afterimage(P, 0.22f, Fx.WithA(_ha.Main, 0.8f), Fx.WithA(_ha.Edge, 0f));
                                    Fx.Afterimage(Q, 0.22f, Fx.WithA(_hb.Main, 0.8f), Fx.WithA(_hb.Edge, 0f));
                                    DuelFx.Starburst(at, Px, 0.7f * K, Color.white, _ha.Main, _hb.Main, 0.08f);
                                    SchoolStyle.Emblem(_schA, at, s, dir, Px, K, 1f, _ha);
                                    SchoolStyle.Emblem(_schB, at, s, -dir, Px, K, 1f, _hb);
                                }
                                D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
                                if (L >= 2)
                                {
                                    Wreck(s, D.Gods ? 1.4f : 0.7f, D.Gods ? 3 : L >= 3 ? 2 : 1, null);
                                }
                                Stop(0.03f);
                            }
                            else
                            {
                                Next(Return);
                            }
                        }
                        else if (Pt > 0.05f)
                        {
                            P.Pose.Hidden = Q.Pose.Hidden = true;
                        }
                        break;
                    }
                case Return:
                    {
                        if (Enter())
                        {
                            P.Pose.Hidden = Q.Pose.Hidden = false;
                            P.Pose.Ground = _sa;
                            Q.Pose.Ground = _sb;
                            P.Pose.Height = Q.Pose.Height = 0f;
                            FaceEach();
                            if (Vis)
                            {
                                DuelFx.Blink(_sa, Px, K, Color.white, _ha.Main);
                                DuelFx.Blink(_sb, Px, K, Color.white, _hb.Main);
                            }
                            // the last meeting told: one of them took more of it
                            _c = D.Roll();
                            if (!_c.Even)
                            {
                                Strike(Winner(_c), Loser(_c), 0.35f, false);
                                Opens(Winner(_c), Link.Close);
                            }
                        }
                        Settle(P, Pt, 0f);
                        Settle(Q, Pt, 1f);
                        if (Pt >= Rest(0.3f))
                        {
                            Done = true;
                        }
                        break;
                    }
            }
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Pose.Hidden = false;
            }
            if (Q != null)
            {
                Q.Pose.Hidden = false;
            }
        }
    }
}
