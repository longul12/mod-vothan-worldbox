using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN'S HAND (Divine, God). The God lifts its open palm to the sky (Charging: a fast blow
    /// breaks it) and, high over the enemy army, a colossal spectral hand takes shape, palm down,
    /// fingers spread away from the God, its shadow spreading over the ground beneath. The God's
    /// arm comes down and the hand falls with it: an impact frame, and every enemy under the palm
    /// and the fingers is pressed into the earth (crushed flat, dazed), the ones at its edge thrown
    /// down and away. The hand holds a heartbeat, trembling, cracks running out from it, then fades
    /// upward into motes and leaves its print in the ground: a pit for the palm, a furrow for each
    /// finger and the thumb (every building under it broken; no fire; allies untouched). Never on
    /// its own feet: the hand falls at least half its length away.
    /// I: a hand 4.2 tiles long; II: 6; III: 8.4, the cinematic. Damage (x the user's damage) under
    /// the hand I 1.8, II 2.4, III 3.2; at its edge 60% of that.
    /// </summary>
    internal sealed class HeavensHandTech : Technique
    {
        public override string Id => "heavens_hand";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Palm | Form.Overhead | Form.Area | Form.Charge | Form.Heavy | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 140f, 200f, 270f };
        public override float[] Cooldown => new[] { 110f, 125f, 140f };

        /// <summary>The hand's length, wrist to fingertip (tiles, x the user's size).</summary>
        public static float Length(int level)
        {
            return level <= 1 ? 4.2f : level == 2 ? 6f : 8.4f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.4f : 3.2f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 11f : 14f;
        }

        private static readonly List<Actor> _in = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float len = Length(level) * k;
            float reach = len * 0.45f;
            Vector2 at;
            Actor center;
            int count;
            if (c.Forced && c.Focus != null)
            {
                center = c.Focus;
                at = center.current_position;
                count = c.CountNear(at, reach);
            }
            else
            {
                at = c.Cluster(reach, len * 0.55f, Range(level) * k, out count, out center);
            }
            if (center == null)
            {
                return null;
            }
            _in.Clear();
            c.CountNear(at, reach * 1.15f, _in);
            float threat = DivineArts.ThreatOf(c, _in);
            if (!c.Forced)
            {
                bool worthy = Tiers.Of(center) >= Tier.Master || c.Threat(center) >= 0.3f;
                if (count < (level <= 1 ? 3 : 4) && !worthy || !DivineArts.Warranted(c, center, count, threat))
                {
                    return null;
                }
                // a hand falling on a melee of our own people is not the moment
                int allies = c.AlliesNear(at, reach);
                if (allies * 2 > count)
                {
                    return null;
                }
            }
            float score = 0f;
            for (int i = 0; i < _in.Count; i++)
            {
                Actor v = _in[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
            }
            score *= DivineArts.Affinity(c.Self, Id);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 d = at - c.Pos;
            return new Plan
            {
                Target = center,
                Point = at,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = Mathf.Max(1, _in.Count),
                Score = score,
                AreaRadius = reach,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            HandMove m = new HandMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Heaven's Hand's move: the palm raised, the hand falling with the arm, the press, the print.</summary>
    internal sealed class HandMove : ChoreoMove
    {
        private readonly Vector2 _at, _dir, _perp;
        private readonly float _len;
        private readonly int _dirIx, _lenPx;
        private Particle _hand, _shade, _rim;
        private Hue _holy;
        private float _lastT, _moteT;
        private bool _pressed;

        public HandMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _len = HeavensHandTech.Length(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            _at = Combat.Clamp(p.Point);
            Vector2 d = _at - Pose.Ground;
            Vector2 want = d.sqrMagnitude > 1e-4f ? d.normalized : (p.Dir.sqrMagnitude > 1e-4f ? p.Dir.normalized : Vector2.right);
            // (the hand is drawn in 16 directions: it presses exactly where it is drawn)
            _dirIx = HandArt.DirIndex(want);
            float ang = _dirIx * 22.5f * Mathf.Deg2Rad;
            _dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _perp = new Vector2(_dir.y, -_dir.x);
            _lenPx = Mathf.Clamp(Mathf.RoundToInt(_len / Px), 16, 130);
            _holy = Hue.As(Element.Holy);
            SetVictim(null);
            Face(d);
        }

        public void Build()
        {
            // the open palm raised to the sky; high above the army the hand takes shape
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.8f : 0.62f,
                To = new Shape(-10f, 0.9f, 1.16f, 0f, -0.04f),
                Ease = Ease.OutCubic,
                Charging = true,
                Track = false,
                Forms = Form.Palm | Form.Area | Form.Charge | Form.Ultimate,
                Enter = m => ((HandMove)m).EnterRaise(),
                Every = (m, k) => ((HandMove)m).TickRaise(k)
            });
            // the arm comes down and the hand falls with it
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.26f,
                To = new Shape(18f, 1.18f, 0.8f, 0f, 0.1f),
                Ease = Ease.InQuad,
                Track = false,
                Forms = Form.Palm | Form.Overhead | Form.Area | Form.Heavy | Form.Ultimate,
                Every = (m, k) => ((HandMove)m).TickFall(k),
                Exit = m => ((HandMove)m).Press()
            });
            // it presses, trembling, the ground cracking out from it
            Add(new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.32f,
                To = new Shape(20f, 1.2f, 0.78f, 0f, 0.1f),
                Tremble = true,
                Track = false,
                Every = (m, k) => ((HandMove)m).TickHold(k)
            });
            // it lifts away into motes
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.34f,
                To = new Shape(4f, 1.02f, 0.98f, 0f, 0.02f),
                Ease = Ease.OutQuad,
                Track = false,
                Every = (m, k) => ((HandMove)m).TickLift(k)
            });
            Recover(0.3f);
        }

        private float Dt()
        {
            float dt = BeatT - _lastT;
            if (dt < 0f)
            {
                dt = BeatT;
            }
            _lastT = BeatT;
            return dt;
        }

        private void EnterRaise()
        {
            _lastT = 0f;
            DivineArts.Open(this, "heavens_hand", 0.45f, 0.6f, 0.5f);
            Choreo.Sound("event:/SFX/POWERS/DivineMagnet", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 20f, 0.5f, Kk);
            }
        }

        /// <summary>How high the hand hangs (world units over the ground).</summary>
        private float SkyH => (5.5f + 0.5f * Level) * Kk;

        private void TickRaise(float k)
        {
            float dt = Dt();
            if (k > 0.8f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.6f);
            }
            float h = SkyH * (1.25f - 0.15f * k) + 0.25f * Mathf.Sin(BeatT * 5f) * Kk;
            Draw(h, Mathf.Lerp(0f, 0.55f, Beat.Apply(Ease.OutQuad, k)), Mathf.Lerp(1.15f, 1.08f, k), Mathf.Lerp(0f, 0.22f, k));
            if (!Kit.Visible(_at, 0.4f))
            {
                return;
            }
            _moteT -= dt;
            if (_moteT <= 0f)
            {
                _moteT = 0.04f;
                // motes drift down out of the hand; the dust under it stirs
                Vector2 off = _perp * Random.Range(-0.4f, 0.4f) * _len + _dir * Random.Range(-0.3f, 0.4f) * _len;
                Particle p = Fx.Spawn(Art.Pixel, _at + off + new Vector2(0f, h), Px, 0.5f, TopLayer, 6);
                p?.Moving(new Vector2(0f, -Random.Range(20f, 40f)) * Px * Kk).Sized(1.4f, 0.7f)
                    .Color(Fx.WithA(_holy.Core, 0f), Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0f));
                if (Random.value < 0.35f)
                {
                    Vector2 g = _at + Random.insideUnitCircle * _len * 0.45f;
                    ChoreoFx.HeelDust(g, g - _at, Px, Combat.TileAt(g), Kk);
                }
            }
        }

        private void TickFall(float k)
        {
            float e = Beat.Apply(Ease.InQuad, k);
            Draw(SkyH * 1.1f * (1f - e), Mathf.Lerp(0.55f, 0.85f, k), Mathf.Lerp(1.08f, 1f, e), Mathf.Lerp(0.22f, 0.5f, k));
            if (k > 0.5f && Kit.Visible(_at, 0.4f))
            {
                Kit.SpeedLines(_at + new Vector2(0f, SkyH * (1f - e) + 1.5f * Kk), Vector2.down, Px, 2, 30f, 0.12f, Fx.WithA(_holy.Core, 0.7f), TopLayer);
            }
        }

        private void TickHold(float k)
        {
            Draw(0f, 0.85f - 0.15f * k, 1f, 0.5f);
            if (_hand != null && _hand.Alive)
            {
                _hand.Pos += Choreo.Tremble(BeatT, 1f) * Px;
            }
        }

        private void TickLift(float k)
        {
            float a = 0.7f * (1f - Beat.Apply(Ease.OutQuad, k));
            Draw(SkyH * 0.5f * Beat.Apply(Ease.InQuad, k), a, 1f + 0.08f * k, 0.5f * (1f - k));
            float dt = Dt();
            _moteT -= dt;
            if (_moteT <= 0f && Kit.Visible(_at, 0.4f))
            {
                _moteT = 0.03f;
                int n = Fx.Count(3);
                for (int i = 0; i < n; i++)
                {
                    Vector2 off = HandArt.RandomPoint(_dir, _perp) * _len;
                    Particle p = Fx.Spawn(Art.Pixel, _at + off + new Vector2(0f, SkyH * 0.5f * k), Px, 0.5f, TopLayer, 6);
                    p?.Moving(new Vector2(Random.Range(-6f, 6f), Random.Range(30f, 70f)) * Px * Kk).Sized(1.5f, 0.6f)
                        .Color(Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(_holy.Edge, 0f));
                }
            }
        }

        /// <summary>The hand at height 'h' (its alpha 'a', its scale 's'), its shadow on the ground ('shade' alpha).</summary>
        private void Draw(float h, float a, float s, float shade)
        {
            if (!Kit.Visible(_at, 0.6f))
            {
                return;
            }
            Sprite fill = HandArt.Hand(_lenPx, _dirIx, false);
            Sprite rim = HandArt.Hand(_lenPx, _dirIx, true);
            if (_shade == null || !_shade.Alive)
            {
                _shade = Glue(Fx.Spawn(fill, _at, Px, 1f, FxLayer.Ground, 2));
            }
            if (_hand == null || !_hand.Alive)
            {
                _hand = Glue(Fx.Spawn(fill, _at, Px, 1f, TopLayer, 8));
                _rim = Glue(Fx.Spawn(rim, _at, Px, 1f, TopLayer, 9));
            }
            if (_shade != null)
            {
                _shade.Pos = _at;
                _shade.Size0 = _shade.Size1 = Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(1f - h / Mathf.Max(0.1f, SkyH * 1.3f)));
                _shade.Grad = null;
                _shade.R.color = new Color(0.05f, 0.03f, 0.08f, shade);
            }
            if (_hand != null)
            {
                _hand.Pos = _at + new Vector2(0f, h);
                _hand.Size0 = _hand.Size1 = s;
                _hand.Grad = null;
                _hand.R.color = Fx.WithA(Color.Lerp(_holy.Core, Color.white, 0.3f), a * 0.55f);
            }
            if (_rim != null)
            {
                _rim.Pos = _at + new Vector2(0f, h);
                _rim.Size0 = _rim.Size1 = s;
                _rim.Grad = null;
                _rim.R.color = Fx.WithA(_holy.Edge, Mathf.Clamp01(a * 1.4f));
            }
        }

        // -------------------------------------------------------------- the press --

        private static readonly List<Actor> _scan = new List<Actor>();

        private void Press()
        {
            if (_pressed)
            {
                return;
            }
            _pressed = true;
            WorldTile tile = Combat.TileAt(_at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile, 0.2f);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.1f);
            if (Cinematic)
            {
                Freeze(0.1f);
            }
            float mult = HeavensHandTech.Mult(Level);
            Choreo.Foes(A, _at, _len * 0.62f, _scan, true, true);
            int crushed = 0;
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                Vector2 rel = u.current_position - _at;
                float lu = Vector2.Dot(rel, _perp) / _len, lv = Vector2.Dot(rel, _dir) / _len;
                bool under = HandArt.Inside(lu, lv, 0.04f + u.stats["size"] * 0.5f / _len);
                if (under)
                {
                    Impact im = Impact.Of(Combat.Grounded(u) ? Hurt.DriveIn : Hurt.Knockdown, Vector2.down, 0.1f, 2f);
                    im.Daze = 1.1f + 0.15f * Level;
                    HitResult r = DivineArts.Smite(this, u, mult, im);
                    if (r.Landed)
                    {
                        crushed++;
                        Victims.Flash(u, _holy.Core, 0.08f);
                    }
                }
                else
                {
                    Vector2 away = rel.sqrMagnitude > 1e-4f ? rel : _dir;
                    Impact im = Impact.Of(Hurt.Knockdown, away, 0.9f, 1.2f);
                    im.Daze = 0.5f;
                    DivineArts.Smite(this, u, mult * 0.6f, im);
                }
            }
            if (Kit.Visible(_at, 0.5f))
            {
                Kit.ImpactFrame(_at + new Vector2(0f, 0.3f * Kk), Px, 1.4f * Kk, _holy.Pal, false, Cinematic ? FxLayer.Cine : FxLayer.Top);
                Fx.Ring(_at, Px, 0.5f, 6f, _len * 0.9f / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1.8f,
                    Fx.WithA(Color.white, 1f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(_holy.Rim, 0f));
                Kit.DustRing(_at, Px, tile, 22, 70f, 0.9f, Kk * 1.6f);
                Kit.Debris(_at, Px, tile, 16, Kk * 1.4f);
                // the print the hand leaves, darker than the ground, fading over seconds
                Particle print = Fx.Spawn(HandArt.Hand(_lenPx, _dirIx, false), _at, Px, 6f, FxLayer.Ground, 1);
                if (print != null)
                {
                    Color dark = Fx.Darken(Fx.SoilColor(tile), 0.55f);
                    print.Color(Fx.WithA(dark, 0.8f), Fx.WithA(dark, 0.65f), Fx.WithA(dark, 0f));
                }
            }
            Kit.Stop(_at, 0.1f);
            Ruin.Quake(A, _at, Scale, 1.6f, Kk);
            Print();
            if (crushed > 0)
            {
                SetLink(Link.Scattered, null);
            }
            if (Dev.On) Dev.Trace("heavens_hand " + Techniques.Roman(Level) + " pressed " + crushed + " of " + _scan.Count + " near (len " + _len.ToString("F1") + ")");
        }

        /// <summary>The ground takes the print: a pit for the palm, a furrow for each finger and the thumb.</summary>
        private void Print()
        {
            Vector2 keep = Pose.Ground;
            float palm = 0.2f * _len / Mathf.Max(0.5f, Ruin.Radius(Tier, Scale, 1f));
            Kit.BeginTrench();
            try
            {
                Ruin.Blast(A, _at, palm, Scale, keep, false);
                Ruin.Blast(A, _at + _perp * 0.12f * _len, palm * 0.8f, Scale, keep, false);
                Ruin.Blast(A, _at - _perp * 0.12f * _len, palm * 0.8f, Scale, keep, false);
                for (int f = 0; f < HandArt.Digits; f++)
                {
                    HandArt.Digit(f, out Vector2 b, out Vector2 tip, out _);
                    Vector2 wb = _at + (_perp * b.x + _dir * b.y) * _len;
                    Vector2 wt = _at + (_perp * tip.x + _dir * tip.y) * _len;
                    Ruin.Furrow(A, Combat.Clamp(wb), Combat.Clamp(wt), 0.35f, Scale, keep, false);
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            if (Kit.Visible(_at, 0.5f))
            {
                for (int f = 0; f < HandArt.Digits; f++)
                {
                    HandArt.Digit(f, out Vector2 b, out Vector2 tip, out _);
                    Vector2 wt = _at + (_perp * tip.x + _dir * tip.y) * _len;
                    Kit.Debris(wt, Px, Combat.TileAt(wt), 3, Kk);
                }
                for (int i = 0; i < 4; i++)
                {
                    Particle c = Fx.Spawn(Art.Cracks[Random.Range(0, Art.Cracks.Length)].Dark, _at, Px * Kk * (1f + 0.3f * Level), 4f, FxLayer.Ground, 3);
                    if (c != null)
                    {
                        c.Rot = Random.Range(0f, 360f);
                        Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(_at)), 0.5f);
                        c.Color(Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.8f), Fx.WithA(dark, 0f));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The spectral hand of Heaven's Hand, palm down, seen from above: a palm, four fingers spread a
    /// little and a thumb, wrist towards the God. Drawn pixel-crisp in 16 directions (fingers along
    /// the direction), cached; one sprite fills it (translucent, a palm crease), one outlines it.
    /// The same shape decides who is under it (Inside, in units of the hand's length).
    /// </summary>
    internal static class HandArt
    {
        public const int Digits = 5;

        private static readonly Dictionary<int, Sprite> _cache = new Dictionary<int, Sprite>();

        /// <summary>A finger (0..3, little to index) or the thumb (4): its base, its tip and its radius, in hand lengths.</summary>
        public static void Digit(int i, out Vector2 b, out Vector2 tip, out float r)
        {
            switch (i)
            {
                case 0:
                    b = new Vector2(-0.18f, 0.15f);
                    tip = b + Dir(-9f) * 0.27f;
                    r = 0.05f;
                    break;
                case 1:
                    b = new Vector2(-0.06f, 0.17f);
                    tip = b + Dir(-3f) * 0.35f;
                    r = 0.056f;
                    break;
                case 2:
                    b = new Vector2(0.06f, 0.17f);
                    tip = b + Dir(3f) * 0.37f;
                    r = 0.056f;
                    break;
                case 3:
                    b = new Vector2(0.18f, 0.15f);
                    tip = b + Dir(9f) * 0.32f;
                    r = 0.053f;
                    break;
                default:
                    b = new Vector2(0.21f, -0.06f);
                    tip = b + Dir(58f) * 0.25f;
                    r = 0.062f;
                    break;
            }
        }

        /// <summary>A unit vector 'deg' degrees off the fingers' direction (+v), towards +u.</summary>
        private static Vector2 Dir(float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>Is the point (u across the hand, v along the fingers; in hand lengths, palm centre at 0) under the hand ('pad' wider)?</summary>
        public static bool Inside(float u, float v, float pad = 0f)
        {
            // the palm
            float pu = u / (0.25f + pad), pv = v / (0.23f + pad);
            if (pu * pu + pv * pv <= 1f)
            {
                return true;
            }
            // the wrist, narrowing away from the palm
            if (v < 0f && v > -0.42f - pad && Mathf.Abs(u) <= 0.15f + pad + 0.05f * (v + 0.42f))
            {
                return true;
            }
            for (int i = 0; i < Digits; i++)
            {
                Digit(i, out Vector2 b, out Vector2 tip, out float r);
                if (SegDist(new Vector2(u, v), b, tip) <= r + pad)
                {
                    return true;
                }
            }
            return false;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>A random point on the hand (world offset per hand length, along 'dir', 'perp').</summary>
        public static Vector2 RandomPoint(Vector2 dir, Vector2 perp)
        {
            for (int i = 0; i < 6; i++)
            {
                float u = Random.Range(-0.3f, 0.3f), v = Random.Range(-0.4f, 0.55f);
                if (Inside(u, v))
                {
                    return perp * u + dir * v;
                }
            }
            return Vector2.zero;
        }

        /// <summary>Which of 16 directions 'd' points in (0 = right, counter-clockwise).</summary>
        public static int DirIndex(Vector2 d)
        {
            float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return ((Mathf.RoundToInt(a / 22.5f) % 16) + 16) % 16;
        }

        /// <summary>The hand 'len' pixels long, fingers along direction 'dir' (0..15); its fill, or ('rim') its outline.</summary>
        public static Sprite Hand(int len, int dir, bool rim)
        {
            len = Mathf.Clamp(len, 16, 130);
            dir = ((dir % 16) + 16) % 16;
            int key = len | (dir << 8) | (rim ? 1 << 12 : 0);
            if (_cache.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            float ang = dir * 22.5f * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 perp = new Vector2(d.y, -d.x);
            int half = Mathf.CeilToInt(len * 0.62f) + 2;
            int n = half * 2 + 1;
            bool[] inside = new bool[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector2 w = new Vector2(x - half, y - half) / len;
                    inside[y * n + x] = Inside(Vector2.Dot(w, perp), Vector2.Dot(w, d));
                }
            }
            Canvas c = new Canvas(n, n);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (!inside[y * n + x])
                    {
                        continue;
                    }
                    bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1 || !inside[y * n + x - 1] || !inside[y * n + x + 1]
                        || !inside[(y - 1) * n + x] || !inside[(y + 1) * n + x];
                    if (rim)
                    {
                        if (edge)
                        {
                            c.Set(x, y, 1f, 1f);
                        }
                        else if (x > 1 && y > 1 && x < n - 2 && y < n - 2 && (!inside[y * n + x - 2] || !inside[y * n + x + 2]
                            || !inside[(y - 2) * n + x] || !inside[(y + 2) * n + x]))
                        {
                            c.Set(x, y, 0.85f, 0.55f);   // a softer second line inside the rim
                        }
                        continue;
                    }
                    Vector2 w = new Vector2(x - half, y - half) / len;
                    float u = Vector2.Dot(w, perp), v = Vector2.Dot(w, d);
                    // a palm crease, a lighter heel of the hand
                    bool crease = Mathf.Abs(v - (0.06f - 1.6f * u * u)) < 0.6f / len && Mathf.Abs(u) < 0.2f;
                    float val = crease ? 0.62f : v < -0.05f ? 0.9f : 0.82f;
                    float alpha = crease ? 0.9f : 0.55f + (Art.Dither(x, y) < 0.3f ? 0.2f : 0f);
                    c.Set(x, y, val, alpha);
                }
            }
            s = c.ToSprite("sakuga_divine_hand_" + len + "_" + dir + (rim ? "_rim" : ""), new Vector2(0.5f, 0.5f));
            _cache[key] = s;
            return s;
        }
    }
}
