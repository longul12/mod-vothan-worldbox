using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI STORM (Ki Arts, Grandmaster). It springs into the air and hangs there, arms working like a
    /// drummer's: from each hand in turn a burst of ki bolts streaks up into the sky. A heartbeat
    /// later the sky answers: dozens of bolts come down over the enemy in shifting patterns, a
    /// spiral winding out from the heart of them, then rows sweeping across the field one after
    /// another, (III) then a ring closing in on the centre; each bolt a gold streak, a warning ring
    /// on the ground, and a small blast where it lands (enemies only, the ground pocked). The storm
    /// drifts with the enemy between patterns and leans on the units it finds near its marks. It
    /// lands while the sky is still falling.
    /// I: 30 bolts over 8 tiles in two patterns; II: 40 over 10; III: 54 over 12, three patterns.
    /// Damage per bolt (x the user's damage): I 0.36, II 0.42, III 0.5; at most 5 / 6 / 7 bolts on
    /// any one foe.
    /// </summary>
    internal sealed class KiStormTech : Technique
    {
        public override string Id => "ki_storm";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 50f, 80f, 120f };
        public override float[] Cooldown => new[] { 16f, 18f, 20f };

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 10f : 12f;
        }

        public static int Bolts(int level)
        {
            return level <= 1 ? 30 : level == 2 ? 40 : 54;
        }

        public static float BoltMult(int level)
        {
            return level <= 1 ? 0.36f : level == 2 ? 0.42f : 0.5f;
        }

        public static int MaxPerFoe(int level)
        {
            return 4 + level;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level);
            Vector2 at = c.Cluster(r * 0.55f, 0f, 9f + r * 0.4f, out int count, out Actor center);
            if (center == null)
            {
                if (!c.Forced || c.Focus == null)
                {
                    return null;
                }
                center = c.Focus;
                at = center.current_position;
            }
            if (!c.Forced && count < 3)
            {
                return null;   // a storm is for a field of foes
            }
            _near.Clear();
            int n = c.CountNear(at, r * 0.85f, _near);
            // the bolts are shared out: each foe takes its share of them, never more than the cap
            float each = Mathf.Min(MaxPerFoe(level), Bolts(level) * 0.75f / Mathf.Max(1, n)) * BoltMult(level);
            float score = 0f;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, each), cm);
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            else
            {
                score *= n >= 6 ? 1.25f : 1f;
            }
            Vector2 dir = at - c.Pos;
            return new Plan
            {
                Target = center,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
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
            float rise = 1.2f + 0.15f * level;
            int throws = 8 + 2 * level;
            float stormDur = level <= 1 ? 1.1f : level == 2 ? 1.3f : 1.6f;
            Form shows = Form.Ki | Form.Projectile | Form.Area;
            m.Face(p.Point - m.Stance);
            m.Crouch(0.1f, 0.9f);
            // it springs up and hangs there
            m.Rise(rise, 0.22f)
                .Shows(shows | Form.Aerial)
                .OnEnter(mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                        Fx.Ring(mm.Pose.Ground, mm.Px, 0.25f, 3f, 14f * mm.Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(mm.Hue.Core, 0.9f), Fx.WithA(mm.Hue.Edge, 0f));
                    }
                });
            // the hands work like a drummer's: a burst of bolts up into the sky from each in turn
            for (int i = 0; i < throws; i++)
            {
                int j = i;
                float side = (i & 1) == 0 ? 1f : -1f;
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.08f,
                    To = new Shape(-10f + 8f * side, 0.9f + 0.04f * side, 1.16f, rise + 0.04f * side),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = shows | Form.Aerial,
                    Enter = mm =>
                    {
                        if (j == 0)
                        {
                            Areas.Add(new KiStormHazard(mm, Combat.Clamp(mm.Point), Radius(level) * Mathf.Lerp(1f, mm.Kk, 0.4f), Bolts(level), stormDur,
                                BoltMult(level), level >= 3 ? 3 : 2, MaxPerFoe(level), 0.24f));
                        }
                        Volley(mm, side);
                    }
                });
            }
            // it comes down while the sky is still falling
            m.Drop(0.2f).Shows(shows);
            m.Land();
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>A burst of bolts leaving a hand straight up into the sky.</summary>
        private static void Volley(ChoreoMove m, float side)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.07f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 hand = m.BodyPos() + new Vector2(side * m.Hpx * 0.28f, m.Hpx * 0.35f) * px;
            Kit.Flash(hand, px, Mathf.Max(2, Mathf.RoundToInt(2.5f * m.Kk)), 0.05f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer, 22);
            int n = Fx.Count(3);
            for (int i = 0; i < n; i++)
            {
                Vector2 v = new Vector2(Random.Range(-0.25f, 0.25f) + side * 0.15f, 1f).normalized;
                Particle b = Fx.Spawn(Art.Pixel, hand, px, 0.3f, m.TopLayer, 14);
                if (b == null)
                {
                    break;
                }
                b.Moving(v * Random.Range(28f, 40f) * m.Kk, 0f, 0f);
                b.FaceVel = true;
                b.Sized(1.6f, 1.2f);
                b.SY = 4f;
                b.Color(Color.white, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.Line(hand, hand + v * 1.4f * m.Kk, px, 0.07f, 1, m.TopLayer, 13, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
        }
    }

    /// <summary>
    /// The storm the sky gives back: 'Count' bolts over 'Dur' seconds within 'R' tiles of 'At', in
    /// patterns (a spiral winding out, rows sweeping across, a ring closing in), re-centred on the
    /// enemy between patterns; each bolt a streak falling out of the sky onto a warning ring, a small
    /// blast (enemies only, each bolt hitting anew, at most 'Cap' on one foe), the ground pocked.
    /// </summary>
    internal sealed class KiStormHazard : Hazard
    {
        private sealed class Bolt
        {
            public Vector2 At;
            public float T, Fall;
            public bool Live;
            public Particle Streak, Head, Mark;
        }

        private Vector2 _at;
        private readonly float _r, _dur, _mult, _delay;
        private readonly int _count, _patterns, _cap;
        private int _made, _pattern = -1;
        private float _ang0;
        private Vector2 _rowDir = Vector2.right;
        private float _slant;
        private readonly List<Bolt> _bolts = new List<Bolt>(16);
        private readonly Dictionary<long, int> _hits = new Dictionary<long, int>();

        public KiStormHazard(ChoreoMove m, Vector2 at, float r, int count, float dur, float mult, int patterns, int cap, float delay) : base(m)
        {
            _at = at;
            _r = r;
            _count = Mathf.Clamp(count, 1, 120);
            _dur = Mathf.Max(0.2f, dur);
            _mult = mult;
            _patterns = Mathf.Clamp(patterns, 1, 3);
            _cap = cap;
            _delay = delay;
            _slant = Random.value < 0.5f ? -1f : 1f;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float gap = _dur / _count;
            while (_made < _count && Age - _delay >= _made * gap)
            {
                Spawn();
            }
            bool any = false;
            for (int i = 0; i < _bolts.Count; i++)
            {
                Bolt b = _bolts[i];
                if (!b.Live)
                {
                    continue;
                }
                any = true;
                b.T += dt;
                float k = Mathf.Clamp01(b.T / b.Fall);
                Draw(b, k);
                if (k >= 1f)
                {
                    Land(b);
                }
            }
            return _made < _count || any;
        }

        private void Spawn()
        {
            int per = Mathf.CeilToInt(_count / (float)_patterns);
            int pattern = Mathf.Min(_patterns - 1, _made / per);
            int j = _made - pattern * per;
            int n = Mathf.Min(per, _count - pattern * per);
            if (pattern != _pattern)
            {
                _pattern = pattern;
                Shift();
            }
            float u = n > 1 ? j / (float)(n - 1) : 0f;
            Vector2 p;
            switch (pattern)
            {
                case 0:
                    {
                        // a spiral winding out from the heart of them
                        float a = _ang0 + u * Mathf.PI * 2f * 2.3f;
                        float rad = _r * (0.08f + 0.85f * u);
                        p = _at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                        break;
                    }
                case 1:
                    {
                        // rows sweeping across the field, one after another
                        int rows = 4;
                        int perRow = Mathf.Max(1, Mathf.CeilToInt(n / (float)rows));
                        int row = j / perRow, col = j % perRow;
                        float along = -_r * 0.8f + 1.6f * _r * (row + 0.5f) / rows;
                        float across = -_r * 0.75f + 1.5f * _r * (col + 0.5f) / perRow + Random.Range(-0.3f, 0.3f);
                        p = _at + _rowDir * along + Choreo.Perp(_rowDir) * across;
                        break;
                    }
                default:
                    {
                        // a ring closing in on the centre
                        float a = _ang0 + j * 2.4f;
                        float rad = _r * Mathf.Lerp(0.95f, 0.1f, u);
                        p = _at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                        break;
                    }
            }
            // a third of them lean onto a foe standing near the mark
            if (OwnerOk && Random.value < 0.35f)
            {
                Choreo.Foes(A, p, 1.6f, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u2 = Near[i];
                    _hits.TryGetValue(u2.Sid(), out int h);
                    if (h < _cap)
                    {
                        p = u2.current_position + Random.insideUnitCircle * 0.3f;
                        break;
                    }
                }
            }
            p = Combat.Clamp(p);
            Bolt b = null;
            for (int i = 0; i < _bolts.Count; i++)
            {
                if (!_bolts[i].Live)
                {
                    b = _bolts[i];
                    break;
                }
            }
            if (b == null)
            {
                b = new Bolt();
                _bolts.Add(b);
            }
            b.At = p;
            b.T = 0f;
            b.Fall = 0.2f;
            b.Live = true;
            _made++;
        }

        /// <summary>A new pattern: it drifts towards where the enemy stands now, and turns.</summary>
        private void Shift()
        {
            _ang0 = Random.value * Mathf.PI * 2f;
            float a = Random.value * Mathf.PI * 2f;
            _rowDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (!OwnerOk)
            {
                return;
            }
            Choreo.Foes(A, _at, _r * 1.3f, Near);
            if (Near.Count == 0)
            {
                return;
            }
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < Near.Count; i++)
            {
                sum += Near[i].current_position;
            }
            _at = Combat.Clamp(Vector2.Lerp(_at, sum / Near.Count, 0.6f));
        }

        private void Draw(Bolt b, float k)
        {
            if (!Kit.Visible(b.At, 0.3f))
            {
                KillBolt(b);
                return;
            }
            float h = (1f - k) * 7f * K;
            Vector2 slant = new Vector2(_slant * 0.35f, 1f).normalized;
            Vector2 head = b.At + slant * h;
            Vector2 tail = head + slant * 1.3f * K;
            int level = M != null ? M.Level : 2;
            KiHi.Bar(ref b.Streak, head, tail, Px, level >= 2 ? 2f : 1f, Fx.WithA(Hue.Edge, 0.85f), FxLayer.Top, 11);
            KiHi.Blob(ref b.Head, head, Px, (1.2f + 0.3f * level) * Mathf.Lerp(1f, K, 0.5f), Hue.Core, FxLayer.Top, 12);
            // the warning ring on the ground, closing as it falls
            if (b.Mark == null || !b.Mark.Alive)
            {
                b.Mark = Fx.Spawn(Art.Ring(Mathf.Clamp(Mathf.RoundToInt(6f * K), 2, 30), 1, Art.Ground), b.At, Px, 1f, FxLayer.Ground, 4);
                if (b.Mark != null)
                {
                    b.Mark.Held = true;
                }
            }
            if (b.Mark != null)
            {
                b.Mark.Pos = b.At;
                b.Mark.Size0 = b.Mark.Size1 = Mathf.Lerp(1.3f, 0.5f, k);
                b.Mark.Grad = null;
                b.Mark.R.color = Fx.WithA(Hue.Edge, 0.35f + 0.5f * k);
            }
        }

        private void Land(Bolt b)
        {
            b.Live = false;
            KillBolt(b);
            WorldTile t = Combat.TileAt(b.At);
            if (Kit.Visible(b.At, 0.3f))
            {
                Kit.Flash(b.At + new Vector2(0f, 0.2f * K), Px, Mathf.RoundToInt(3.5f * K), 0.07f, Fx.WithA(Hue.Core, 0.95f));
                Fx.Ring(b.At, Px, 0.2f, 2f, 9f * K, 1, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(b.At + new Vector2(0f, 0.2f * K), Px, 3, 30f, 70f, Hue.Edge);
                Kit.DustRing(b.At, Px, t, 4, 24f, 0.35f, K);
            }
            Choreo.Sound("event:/SFX/HIT/HitGeneric", t, 0.06f);
            if (!OwnerOk)
            {
                return;
            }
            if ((_made & 1) == 0)
            {
                Ruin.Blast(A, b.At, 0.55f, Scale, A.current_position, false, -1);
            }
            Choreo.Foes(A, b.At, 0.8f * Mathf.Lerp(1f, K, 0.5f), Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                long id = u.Sid();
                _hits.TryGetValue(id, out int h);
                if (h >= _cap)
                {
                    continue;
                }
                HitIds.Remove(id);   // (each bolt hits anew)
                if (Strike(u, _mult, (h % 3) == 2 ? Hurt.Knockdown : Hurt.Flinch, b.At, 0.25f, -1f, 0.8f))
                {
                    _hits[id] = h + 1;
                }
            }
        }

        private static void KillBolt(Bolt b)
        {
            KiHi.Kill(ref b.Streak);
            KiHi.Kill(ref b.Head);
            KiHi.Kill(ref b.Mark);
        }

        public override void Kill()
        {
            for (int i = 0; i < _bolts.Count; i++)
            {
                KillBolt(_bolts[i]);
                _bolts[i].Live = false;
            }
        }
    }
}
