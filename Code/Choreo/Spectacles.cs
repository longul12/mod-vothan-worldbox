using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THE SPECTACLES: the engine's big set pieces, for God powers, team techniques and the Forge's
    /// high tiers. Each is a hazard (it plays on by itself while the performer holds its pose), and
    /// every one of them is built so cause and effect read clearly: a telegraph first (a shadow, a
    /// ring, a light in the sky), then the thing itself, then what it did (the ground broken where it
    /// struck, bodies thrown away from it, never toward it unless something pulls them).
    ///   Meteor      a burning rock out of the sky: its shadow grows, it streaks down, the ground
    ///               blows open (a hot crater, fire, rocks); everything round it is thrown outward
    ///   BlackHole   a sphere of dark: the foes round it are dragged in (flying, tumbling) and held,
    ///               then it collapses to a point and goes off, flinging them far
    ///   Trench      the earth split open along a line: a fissure racing out, rocks bursting from it,
    ///               the bodies on either side thrown off it to both sides
    ///   SkyBeam     a column of light from the sky onto a point: a ring marks it, the column falls
    ///               and burns, the ground scorched and blasted under it
    ///   Lightning   bolts out of the sky onto the foes one after another: a jagged line, a flash,
    ///               a scorch; each struck body knocked flat
    ///   SwordGate   blades of light appear in the air behind the performer (a ripple at each) and are
    ///               loosed one by one: each runs a foe through and pins it where it lands
    ///   SlashDomain a ring drawn on the ground: inside it, cuts appear out of nowhere faster and
    ///               faster, everything inside is cut again and again, then it all bursts at once
    /// </summary>
    /// <summary>
    /// What falls out of the sky in a Meteor: each school calls down its own. Earth breakers tear a
    /// burning rock out of it, ki masters a sun of ki, iron fists a colossal fist of pressure, sword
    /// saints a sword the size of a tower, hunters a spear thrown from the heavens.
    /// </summary>
    internal enum SkyFall
    {
        Rock,
        Sun,
        Fist,
        Sword,
        Spear
    }

    internal partial class ChoreoMove
    {
        private Vector2 SpotOr(Func<ChoreoMove, Vector2> at)
        {
            return at != null ? at(this) : (V != null && VOk ? VG : Point);
        }

        public ChoreoMove Meteor(float r, float mult, float size = 1f, Func<ChoreoMove, Vector2> at = null, SkyFall kind = SkyFall.Rock)
        {
            return KeepDo(m => Areas.Add(new MeteorHazard(m, m.SpotOr(at), r, mult, size, kind)));
        }

        public ChoreoMove BlackHole(float r, float mult, float dur = 1.2f, Func<ChoreoMove, Vector2> at = null, bool terrain = true)
        {
            return KeepDo(m => Areas.Add(new BlackHoleHazard(m, m.SpotOr(at), r, mult, dur, terrain)));
        }

        /// <summary>The earth split along its facing; 'cut': a blade's cut instead (a line of light that opens the ground).</summary>
        public ChoreoMove Trench(float len, float width, float mult, float speed = 16f, bool cut = false)
        {
            return KeepDo(m => Areas.Add(new TrenchHazard(m, m.Pose.Ground + m.Dir * 0.6f * m.Kk, m.Dir, len, width, mult, speed, cut)));
        }

        public ChoreoMove SkyBeam(float r, float mult, float dur = 0.9f, Func<ChoreoMove, Vector2> at = null, bool terrain = true)
        {
            return KeepDo(m => Areas.Add(new SkyBeamHazard(m, m.SpotOr(at), r, mult, dur, terrain)));
        }

        public ChoreoMove Lightning(int count, float r, float mult, float dur = 0.9f, Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new LightningHazard(m, m.SpotOr(at), r, count, mult, dur)));
        }

        /// <summary>Gates open behind it and loose 'kind' (swords of light, or a hunter's spears) one by one.</summary>
        public ChoreoMove SwordGate(int count, float mult, float range = 10f, ShotKind kind = ShotKind.LightSword)
        {
            return KeepDo(m => Areas.Add(new SwordGateHazard(m, count, mult, range, kind)));
        }

        public ChoreoMove SlashDomain(float r, float mult, float dur = 1.2f, Func<ChoreoMove, Vector2> at = null)
        {
            return KeepDo(m => Areas.Add(new SlashDomainHazard(m, at != null ? at(m) : m.Pose.Ground, r, mult, dur)));
        }
    }

    /// <summary>Shared bits of the spectacles.</summary>
    internal static class Spec
    {
        public static readonly Color Molten = new Color(1f, 0.55f, 0.15f, 1f);

        /// <summary>
        /// A fireball: a white flash, then flames (the game's fire sprites) bursting out and rolling up,
        /// smoke climbing after them. 'r' is its size in world units. Colours: the heart, the flame, the
        /// embers (defaults: fire).
        /// </summary>
        public static void Fireball(Vector2 at, float r, float px, Color? heart = null, Color? flame = null, Color? ember = null, bool smoke = true)
        {
            if (!Kit.Visible(at, 1f))
            {
                return;
            }
            Color h = heart ?? Hot, f = flame ?? Molten, e = ember ?? Ember;
            Kit.Flash(at, px, Mathf.Clamp(Mathf.RoundToInt(r / px * 0.45f), 2, 60), 0.08f, Color.white);
            float fpx = Mathf.Max(px, r / 6f);
            int n = Fx.Count(12);
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = Random.insideUnitCircle;
                Particle p = Fx.Spawn(Art.Puffs[Random.Range(0, 3)], at + dir * r * 0.3f, fpx * Random.Range(0.8f, 1.4f), Random.Range(0.35f, 0.6f), FxLayer.Top, 19);
                if (p == null)
                {
                    break;
                }
                p.Delay = Random.Range(0f, 0.06f);
                p.Moving(dir * r * Random.Range(1.4f, 2.8f) + new Vector2(0f, r * Random.Range(0.4f, 1.4f)), 0f, 2f)
                    .Color(Color.white, h, f, Fx.WithA(e, 0.8f), Fx.WithA(e, 0f));
            }
            if (!smoke)
            {
                return;
            }
            int m = Fx.Count(7);
            for (int i = 0; i < m; i++)
            {
                Particle s = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at + Random.insideUnitCircle * r * 0.5f, fpx * 1.4f, Random.Range(0.8f, 1.3f), FxLayer.Top, 18);
                if (s == null)
                {
                    break;
                }
                s.Delay = 0.12f + Random.Range(0f, 0.15f);
                s.Moving(new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(1f, 2f)) * r, 0f, 1f).Sized(0.8f, 1.8f)
                    .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.6f), Fx.WithA(Smoke, 0f));
            }
        }

        /// <summary>
        /// What a meteor leaves as it falls, one beat of it: flames (the game's fire sprites) streaming
        /// back off it, a plume of smoke behind them, sparks shed, now and then a bow of heat across its front.
        /// </summary>
        public static void MeteorTrail(Vector2 p, Vector2 back, float headR, float px, Color hot, Color molten, Color ember, Color smoke, FxLayer layer, int order, bool cine)
        {
            if (!Kit.Visible(p, 1f))
            {
                return;
            }
            float fpx = Mathf.Max(px, headR / 5f);
            Particle f = CineKit.C(Fx.Spawn(Art.Puffs[Random.Range(0, 3)], p + back * headR * 0.7f + Random.insideUnitCircle * headR * 0.35f, fpx, Random.Range(0.22f, 0.36f), layer, order), cine);
            f?.Moving(back * headR * Random.Range(2.5f, 4.5f) + Random.insideUnitCircle * headR * 0.8f, 0f, 1.5f).Color(hot, molten, Fx.WithA(ember, 0.8f), Fx.WithA(ember, 0f));
            if (Random.value < 0.5f)
            {
                Particle s = CineKit.C(Fx.Spawn(Art.Dust[Random.Range(0, 3)], p + back * headR * Random.Range(1.5f, 2.5f) + Random.insideUnitCircle * headR * 0.5f, fpx * 1.3f, Random.Range(0.5f, 0.8f), layer, order - 1), cine);
                s?.Moving(back * headR * 1.2f + Random.insideUnitCircle * headR * 0.6f, 0f, 1.2f).Sized(0.8f, 1.6f).Color(Fx.WithA(smoke, 0f), Fx.WithA(smoke, 0.55f), Fx.WithA(smoke, 0f));
            }
            if (Random.value < 0.6f)
            {
                Particle e = CineKit.Ember(p + Random.insideUnitCircle * headR, px, Random.Range(0.3f, 0.5f), (back * Random.Range(1f, 3f) + Random.insideUnitCircle) * headR * 2f, molten, layer, cine, 1.2f);
                if (e != null)
                {
                    e.Delay = 0f;
                }
            }
            if (Random.value < 0.3f)
            {
                PrimFx.Slash(p - back * headR * 0.85f, Mathf.Atan2(back.y, back.x) * Mathf.Rad2Deg + 90f, headR * 2.4f, headR * 0.3f, 0.07f, layer, order + 3, cine, hot);
            }
        }
        public static readonly Color Hot = new Color(1f, 0.92f, 0.6f, 1f);
        public static readonly Color Ember = new Color(0.85f, 0.2f, 0.05f, 1f);
        public static readonly Color Smoke = new Color(0.25f, 0.22f, 0.22f, 1f);

        /// <summary>A soft blob at 'at': 'r' tiles across, held (null if off screen or out of particles).</summary>
        public static Particle Blob(Vector2 at, float r, float px, float life, FxLayer layer, int order, float ratio, float soft, params Color[] colors)
        {
            Particle b = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(r / px), 2, 140), ratio, soft), at, px, life, layer, order);
            b?.Color(colors);
            return b;
        }

        /// <summary>A jagged bolt from 'a' to 'b' (and a branch or two), bright core and coloured edge.</summary>
        public static void Bolt(Vector2 a, Vector2 b, float px, float life, Color edge, FxLayer layer, int order)
        {
            int n = 7;
            Vector2 last = a;
            Vector2 side = Choreo.Perp((b - a).normalized);
            float len = Vector2.Distance(a, b);
            for (int i = 1; i <= n; i++)
            {
                float k = i / (float)n;
                Vector2 p = Vector2.Lerp(a, b, k) + (i < n ? side * Random.Range(-1f, 1f) * len * 0.07f : Vector2.zero);
                Kit.Line(last, p, px, life * 1.2f, 5, layer, order - 1, Fx.WithA(edge, 0.45f), Fx.WithA(edge, 0f));
                Kit.Line(last, p, px, life, 3, layer, order, Fx.WithA(edge, 0.95f), Fx.WithA(edge, 0f));
                Kit.Line(last, p, px, life * 0.8f, 1, layer, order + 1, Color.white, Fx.WithA(Color.white, 0f));
                if (i == 3 || i == 5)
                {
                    Vector2 br = p + Choreo.Rotate((b - a).normalized, Random.Range(-50f, 50f)) * len * 0.2f;
                    Kit.Line(p, br, px, life * 0.7f, 1, layer, order, Fx.WithA(edge, 0.8f), Fx.WithA(edge, 0f));
                }
                last = p;
            }
        }

        /// <summary>Rocks thrown up out of the ground at 'at' and falling back.</summary>
        public static void Rocks(Vector2 at, float px, int n, float speed, float k)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color soil = Fx.SoilColor(Combat.TileAt(at));
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at + Random.insideUnitCircle * 2f * px, px * Random.Range(1f, 1.8f), Random.Range(0.5f, 0.9f), FxLayer.Top, 14);
                if (c == null)
                {
                    break;
                }
                c.Moving(new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.6f, 1.2f)) * speed * px * k, 0f, 0.4f, 300f * px);
                c.Spin = Random.Range(-500f, 500f);
                c.Color(Color.Lerp(soil, Color.white, 0.25f), soil, Fx.WithA(Color.Lerp(soil, Color.black, 0.3f), 0f));
            }
        }

        /// <summary>A rolling column of smoke rising off 'at'.</summary>
        public static void Column(Vector2 at, float px, int n, float k, Color c)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                // (the game's soft puff shapes, rising and spreading, not flat discs)
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at + new Vector2(Random.Range(-2f, 2f) * px * k, 0f), px * k * 1.2f, Random.Range(1f, 1.6f), FxLayer.Top, 9);
                p?.Moving(new Vector2(Random.Range(-3f, 3f), Random.Range(10f, 22f)) * px * k, 0f, 0.6f).Sized(0.6f, 1.8f)
                    .Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.7f), Fx.WithA(c, 0.45f), Fx.WithA(c, 0f));
                if (p != null)
                {
                    p.Delay = i * 0.06f;
                }
            }
        }
    }

    // ================================================================= METEOR ==

    internal sealed class MeteorHazard : Hazard
    {
        private readonly Vector2 _at, _sky;
        private readonly float _r, _mult, _size, _warn, _fall;
        private readonly SkyFall _kind;
        private Particle _shadow, _head, _glow;
        private Vector2 _glowOff;
        private bool _struck;
        private float _trailT;

        public MeteorHazard(ChoreoMove m, Vector2 at, float r, float mult, float size, SkyFall kind = SkyFall.Rock) : base(m)
        {
            _kind = kind;
            Init(at, r, mult, size, out _at, out _sky, out _r, out _mult, out _size, out _warn, out _fall);
        }

        public MeteorHazard(Actor a, Move by, Hue hue, Scale scale, Vector2 at, float r, float mult, float size, SkyFall kind = SkyFall.Rock) : base(a, by, hue, scale)
        {
            _kind = kind;
            Init(at, r, mult, size, out _at, out _sky, out _r, out _mult, out _size, out _warn, out _fall);
        }

        /// <summary>The pixel scale of a falling sword or spear (chunky: it is the size of a tower).</summary>
        private const float Big = 1.8f;

        /// <summary>A fist or a sword falls straight onto its mark; a rock, a sun or a spear comes in on a slant.</summary>
        private bool Plumb => _kind == SkyFall.Fist || _kind == SkyFall.Sword;

        private void Init(Vector2 at, float r, float mult, float size, out Vector2 oat, out Vector2 osky, out float or, out float omult, out float osize, out float owarn, out float ofall)
        {
            oat = at;
            or = r * Mathf.Lerp(1f, K, 0.5f);
            omult = mult;
            osize = Mathf.Clamp(size, 0.5f, 4f);
            owarn = 0.55f + 0.15f * osize;
            ofall = 0.35f + 0.08f * osize;
            float side = Random.value < 0.5f ? -1f : 1f;
            float slant = Plumb ? 0.12f : _kind == SkyFall.Spear ? 0.7f : 1f;
            osky = at + new Vector2(side * (4f + 2f * osize) * slant, 14f + 4f * osize) * K;
            Choreo.Sound(_kind == SkyFall.Rock || _kind == SkyFall.Sun ? "event:/SFX/DESTRUCTION/FallMeteorite" : "event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(at));
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_at, 0.6f);
            float headR = (0.45f + 0.3f * _size) * K;
            if (Age < _warn + _fall)
            {
                float w = Mathf.Clamp01(Age / (_warn + _fall));
                if (vis)
                {
                    // its shadow on the ground grows darker and wider as it comes
                    if (_shadow == null || !_shadow.Alive)
                    {
                        _shadow = Spec.Blob(_at, _r * 0.6f, Px, 5f, FxLayer.Ground, 3, Art.Ground, 0.4f, Fx.WithA(Color.black, 0.5f));
                        if (_shadow != null)
                        {
                            _shadow.Held = true;
                        }
                    }
                    if (_shadow != null)
                    {
                        _shadow.Pos = _at;
                        _shadow.SX = _shadow.SY = Mathf.Lerp(0.2f, 1f, w);
                        _shadow.R.color = Fx.WithA(Color.black, 0.15f + 0.4f * w);
                    }
                    if (((int)(Age * 6f) & 1) == 0 && Age < _warn)
                    {
                        Fx.Ring(_at, Px, 0.16f, _r / Px * 1.1f, _r / Px * 0.9f, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                    }
                }
                if (Age >= _warn)
                {
                    float k = (Age - _warn) / _fall;
                    // (a sword or a spear is drawn from its point: the point is what lands on the mark)
                    bool pointed = _kind == SkyFall.Sword || _kind == SkyFall.Spear;
                    Vector2 p = Vector2.Lerp(_sky, _at + new Vector2(0f, pointed ? 0f : headR * 0.5f), k * k);
                    if (vis || Kit.Visible(p, 0.6f))
                    {
                        if (_head == null || !_head.Alive)
                        {
                            MakeHead(p, headR);
                        }
                        if (_head != null)
                        {
                            _head.Pos = p;
                            if (_kind == SkyFall.Rock)
                            {
                                _head.Rot += 300f * dt;
                            }
                            else if (_kind != SkyFall.Sun)
                            {
                                _head.Grad = null;
                                // (a fist and a spear in the school's own colour; a sword of light white-hot)
                                _head.R.color = _kind == SkyFall.Sword ? Color.Lerp(Hue.Core, Color.white, 0.45f) : Color.Lerp(Hue.Main, Color.white, _kind == SkyFall.Fist ? 0.2f : 0.35f);
                            }
                        }
                        if (_glow != null)
                        {
                            _glow.Pos = p + _glowOff;
                            if (_kind == SkyFall.Sun || _kind == SkyFall.Rock)
                            {
                                _glow.Rot += 90f * dt;
                            }
                        }
                        _trailT -= dt;
                        if (_trailT <= 0f)
                        {
                            Trail(p, headR);
                        }
                    }
                }
                return true;
            }
            if (!_struck)
            {
                _struck = true;
                Strike();
            }
            return Age < _warn + _fall + 0.1f;
        }

        /// <summary>The falling thing itself, in its school's shape (held: Tick moves it).</summary>
        private void MakeHead(Vector2 p, float headR)
        {
            _glowOff = Vector2.zero;
            switch (_kind)
            {
                case SkyFall.Sun:
                    // a sun of ki: a face of pixel-art light in a corona of the school's colour
                    _head = Fx.Spawn(PrimFx.SunSprite(), p, headR * 1.15f / PrimFx.BodyR, 5f, FxLayer.Top, 22);
                    _head?.Color(Color.Lerp(Hue.Core, Color.white, 0.5f));
                    _glow = Fx.Spawn(PrimFx.CoronaSprite(), p, headR * 1.6f / PrimFx.BodyR, 5f, FxLayer.Top, 21);
                    _glow?.Color(Fx.WithA(Hue.Main, 0.75f));
                    break;
                case SkyFall.Fist:
                    _head = Fx.Spawn(ChoreoArt.Fist(Mathf.RoundToInt(headR * 2.6f / Px)), p, Px, 5f, FxLayer.Top, 22);
                    if (_head != null)
                    {
                        _head.Rot = -90f;   // knuckles down
                    }
                    _glow = Spec.Blob(p, headR * 2.1f, Px, 5f, FxLayer.Top, 21, 1f, 0.7f, Fx.WithA(Hue.Main, 0.6f));
                    break;
                case SkyFall.Sword:
                    {
                        // (drawn with chunky pixels: a tower of a blade, not a long needle)
                        int len = Mathf.RoundToInt(headR * 4.6f / (Px * Big));
                        _head = Fx.Spawn(ChoreoArt.LightSword(len), p, Px * Big, 5f, FxLayer.Top, 22);
                        _glowOff = new Vector2(0f, len * Px * Big * 0.55f);
                        _glow = Spec.Blob(p + _glowOff, headR * 2.2f, Px, 5f, FxLayer.Top, 21, 0.45f, 0.8f, Fx.WithA(Hue.Edge, 0.55f));
                    }
                    break;
                case SkyFall.Spear:
                    {
                        _head = Fx.Spawn(ChoreoArt.Smear(Mathf.RoundToInt(headR * 5.5f / (Px * Big)), 1, 2)[0], p, Px * Big, 5f, FxLayer.Top, 22);
                        Vector2 d = (_at - _sky).normalized;
                        if (_head != null)
                        {
                            _head.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        }
                        _glow = Spec.Blob(p, headR * 1.3f, Px, 5f, FxLayer.Top, 21, 1f, 0.7f, Fx.WithA(Hue.Edge, 0.7f));
                    }
                    break;
                default:
                    {
                        // a boulder (shaded, cracked) glowing hot right through, in a thin halo of heat
                        int rpx = Mathf.Clamp(Mathf.RoundToInt(headR / Px), 3, 40);
                        _head = Fx.Spawn(ChoreoArt.Boulder(rpx, Random.Range(0, 4)), p, Px, 5f, FxLayer.Top, 22);
                        _head?.Color(Color.Lerp(Spec.Molten, Spec.Hot, 0.3f));
                        // a ragged corona of heat round it (pixel art), not a soft disc
                        _glow = Fx.Spawn(PrimFx.CoronaSprite(), p, headR * 1.05f / PrimFx.BodyR, 5f, FxLayer.Top, 21);
                        _glow?.Color(Fx.WithA(Spec.Molten, 0.6f));
                    }
                    break;
            }
            if (_head != null)
            {
                _head.Held = true;
            }
            if (_glow != null)
            {
                _glow.Held = true;
            }
        }

        /// <summary>What it leaves behind it as it falls: fire, light, rings of pressure, a streak.</summary>
        private void Trail(Vector2 p, float headR)
        {
            Vector2 back = (_sky - _at).normalized;
            switch (_kind)
            {
                case SkyFall.Sun:
                    {
                        _trailT = 0.02f;
                        // tongues of its light streaming off behind it, and a corona of rays round it
                        float fpx = Mathf.Max(Px, headR / 5f);
                        Particle f = Fx.Spawn(Art.Puffs[Random.Range(0, 3)], p + back * headR * 0.7f + Random.insideUnitCircle * headR * 0.4f, fpx, Random.Range(0.25f, 0.4f), FxLayer.Top, 20);
                        f?.Moving(back * headR * Random.Range(2f, 4f) + Random.insideUnitCircle * headR, 0f, 1.5f).Color(Color.white, Hue.Core, Fx.WithA(Hue.Main, 0.7f), Fx.WithA(Hue.Edge, 0f));
                        float a0 = Age * 90f;
                        for (int i = 0; i < 3; i++)
                        {
                            PrimFx.Ray(p, a0 + Random.Range(0f, 360f), headR * Random.Range(1.6f, 2.5f), headR * 0.45f, 0.06f, FxLayer.Top, 21, false, Fx.WithA(Color.Lerp(Hue.Core, Color.white, 0.4f), 0.6f));
                        }
                    }
                    break;
                case SkyFall.Fist:
                    _trailT = 0.06f;
                    // rings of pressure peel off the knuckles as it comes down
                    Fx.Ring(p, Px, 0.3f, headR / Px * 0.8f, headR / Px * 2.3f, 2, 1, FxLayer.Top, 20, 0.45f, 2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Line(p + back * headR, p + back * headR * 5f, Px, 0.12f, 2, FxLayer.Top, 19, Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
                    break;
                case SkyFall.Sword:
                    {
                        _trailT = 0.03f;
                        Vector2 top = p + _glowOff * 2f + new Vector2(Random.Range(-0.5f, 0.5f) * headR, 0f);
                        Kit.Line(top, top + new Vector2(0f, headR * 6f), Px, 0.1f, 1, FxLayer.Top, 19, Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    }
                    break;
                case SkyFall.Spear:
                    _trailT = 0.02f;
                    Kit.Line(p, p + back * headR * 8f, Px, 0.1f, 2, FxLayer.Top, 19, Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0f));
                    break;
                default:
                    {
                        _trailT = 0.018f;
                        Spec.MeteorTrail(p, back, headR, Px, Spec.Hot, Spec.Molten, Spec.Ember, Spec.Smoke, FxLayer.Top, 20, false);
                    }
                    break;
            }
        }

        /// <summary>A sun of ki lands: a dome of light swells and bursts, the ground glows.</summary>
        private void SunFx(WorldTile tile)
        {
            if (Scale >= Scale.Ultimate || _size >= 2f)
            {
                CineKit.ViewFlash(Fx.WithA(Color.Lerp(Hue.Core, Color.white, 0.5f), 0.55f), 0.28f);
            }
            Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.8f), 0.14f, Color.white);
            Spec.Blob(_at + new Vector2(0f, _r * 0.25f), _r * 1.1f, Px, 0.8f, FxLayer.Top, 19, 1f, 0.5f, Color.white, Color.Lerp(Hue.Core, Color.white, 0.5f), Hue.Main, Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f))?.Sized(0.3f, 1.4f);
            Fx.Ring(_at, Px, 0.5f, 2f, _r / Px * 1.8f, 3, 1, FxLayer.Top, 8, Art.Ground, 2.6f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Main, 0.6f), Fx.WithA(Hue.Edge, 0f));
            Fx.Ring(_at, Px, 0.8f, 2f, _r / Px * 2.6f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            Kit.Sparks(_at, Px, 18, 60f, 180f, Hue.Edge);
            Spec.Rocks(_at, Px, Mathf.RoundToInt(6 + 3 * _size), 80f, K);
            Kit.Crater(_at, Px * K * (0.8f + 0.4f * _size), tile, false, 8f, false, Hue.Main);
        }

        /// <summary>A fist of pressure lands: its print in the ground, then ring after ring of force.</summary>
        private void FistFx(WorldTile tile)
        {
            if (Scale >= Scale.Ultimate || _size >= 2f)
            {
                CineKit.ViewFlash(Fx.WithA(Color.white, 0.5f), 0.22f);
            }
            float headR = (0.45f + 0.3f * _size) * K;
            Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.7f), 0.12f, Color.white);
            Particle print = Fx.Spawn(ChoreoArt.Fist(Mathf.RoundToInt(headR * 2.6f / Px)), _at + new Vector2(0f, headR * 0.4f), Px, 0.45f, FxLayer.Top, 22);
            if (print != null)
            {
                print.Rot = -90f;
                print.Sized(1f, 1.25f).Color(Color.Lerp(Hue.Main, Color.white, 0.45f), Fx.WithA(Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            for (int i = 0; i < 3; i++)
            {
                Fx.Ring(_at, Px, 0.35f + 0.2f * i, 2f, _r / Px * (1.1f + 0.75f * i), 3 - i, 1, FxLayer.Ground, 6 - i, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
            }
            Spec.Rocks(_at, Px, Mathf.RoundToInt(16 + 6 * _size), 100f, K);
            Kit.DustRing(_at, Px, tile, 14, 50f, 0.7f, K);
            Kit.Crater(_at, Px * K * (1f + 0.4f * _size), tile, false, 8f, false, Hue.Main);
        }

        /// <summary>A sword lands point first: it stands planted in a great cross cut into the land, then fades.</summary>
        private void SwordFx(WorldTile tile)
        {
            if (Scale >= Scale.Ultimate || _size >= 2f)
            {
                CineKit.ViewFlash(Fx.WithA(Color.white, 0.5f), 0.2f);
            }
            float headR = (0.45f + 0.3f * _size) * K;
            Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.6f), 0.12f, Color.white);
            Fx.Spawn(ChoreoArt.LightSword(Mathf.RoundToInt(headR * 4.6f / (Px * Big))), _at, Px * Big, 1.4f, FxLayer.Top, 22)?
                .Color(Color.white, Color.Lerp(Hue.Core, Color.white, 0.45f), Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            for (int i = 0; i < 2; i++)
            {
                Vector2 dir = Choreo.Rotate(Vector2.right, i == 0 ? 33f : -33f);
                Vector2 a = _at - dir * _r * 1.3f, b = _at + dir * _r * 1.3f;
                Kit.Line(a, b, Px, 1.2f, 4, FxLayer.Ground, 4, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(a, b, Px, 1f, 2, FxLayer.Ground, 5, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            Fx.Ring(_at, Px, 0.4f, 2f, _r / Px * 1.8f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0f));
            Kit.DustRing(_at, Px, tile, 10, 40f, 0.6f, K);
            Spec.Rocks(_at, Px, 8, 80f, K);
            Kit.Sparks(_at, Px, 14, 80f, 170f, Hue.Edge);
        }

        /// <summary>A spear lands on its slant: it stands in the ground, which bursts in cracks round the shaft.</summary>
        private void SpearFx(WorldTile tile)
        {
            if (Scale >= Scale.Ultimate || _size >= 2f)
            {
                CineKit.ViewFlash(Fx.WithA(Color.white, 0.45f), 0.18f);
            }
            float headR = (0.45f + 0.3f * _size) * K;
            Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.55f), 0.1f, Color.white);
            Particle sp = Fx.Spawn(ChoreoArt.Smear(Mathf.RoundToInt(headR * 5.5f / (Px * Big)), 1, 2)[0], _at, Px * Big, 1.6f, FxLayer.Top, 22);
            if (sp != null)
            {
                Vector2 d = (_at - _sky).normalized;
                sp.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                sp.Color(Color.white, Color.Lerp(Hue.Main, Color.white, 0.35f), Fx.WithA(Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            for (int i = 0; i < 8; i++)
            {
                Vector2 d = Choreo.Rotate(Vector2.right, i * 45f + Random.Range(-15f, 15f));
                d.y *= 0.5f;
                Kit.Line(_at, _at + d * _r * Random.Range(0.6f, 1.15f), Px, 0.8f, 2, FxLayer.Ground, 5, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
            }
            Fx.Ring(_at, Px, 0.35f, 2f, _r / Px * 1.6f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
            Spec.Rocks(_at, Px, 14, 95f, K);
            Kit.DustRing(_at, Px, tile, 10, 40f, 0.55f, K);
            Kit.Crater(_at, Px * K * (0.7f + 0.3f * _size), tile, false, 6f, false, Hue.Main);
        }

        private void Strike()
        {
            Kill();
            WorldTile tile = Combat.TileAt(_at);
            bool vis = Kit.Visible(_at, 0.6f);
            if (vis && _kind != SkyFall.Rock)
            {
                switch (_kind)
                {
                    case SkyFall.Sun: SunFx(tile); break;
                    case SkyFall.Fist: FistFx(tile); break;
                    case SkyFall.Sword: SwordFx(tile); break;
                    default: SpearFx(tile); break;
                }
            }
            else if (vis)
            {
                if (Scale >= Scale.Ultimate || _size >= 2f)
                {
                    CineKit.ViewFlash(Fx.WithA(Spec.Hot, 0.55f), 0.25f);
                }
                Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.7f), 0.12f, Fx.WithA(Color.white, 1f));
                Spec.Fireball(_at + new Vector2(0f, _r * 0.3f), _r * 0.9f, Px);
                Fx.Ring(_at, Px, 0.45f, 2f, _r / Px * 1.6f, 3, 1, FxLayer.Top, 8, Art.Ground, 2.6f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Spec.Molten, 0.6f), Fx.WithA(Spec.Ember, 0f));
                Fx.Ring(_at, Px, 0.7f, 2f, _r / Px * 2.4f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Spec.Smoke, 0.6f), Fx.WithA(Spec.Smoke, 0f));
                Spec.Rocks(_at, Px, Mathf.RoundToInt(14 + 6 * _size), 90f, K);
                Spec.Column(_at, Px, 10, K * (0.8f + 0.3f * _size), Spec.Smoke);
                Kit.Crater(_at, Px * K * (0.8f + 0.4f * _size), tile, true, 8f, false, Spec.Molten);
            }
            if (_kind == SkyFall.Sword || _kind == SkyFall.Spear)
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", tile);
            }
            Choreo.Sound(_size >= 2f ? "event:/SFX/EXPLOSIONS/ExplosionBig" : "event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
            Ruin.Blast(A, _at, _r * 0.9f, Scale, null, _kind == SkyFall.Rock, _size >= 2f || _kind == SkyFall.Fist ? 1 : 0);
            Ruin.Quake(A, _at, Scale, 1.6f + 0.3f * _size, K);
            if (_kind == SkyFall.Sword)
            {
                // the cross is cut into the land itself
                Vector2? keep = A != null && A.isAlive() ? A.current_position : (Vector2?)null;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 dir = Choreo.Rotate(Vector2.right, i == 0 ? 33f : -33f);
                    Ruin.Furrow(A, _at - dir * _r * 1.3f, _at + dir * _r * 1.3f, 0.45f, Scale, keep, false);
                }
            }
            Choreo.Foes(A, _at, _r, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Vector2.Distance(u.current_position, _at) / Mathf.Max(0.1f, _r);
                // a fist drives the ones under it into the ground; a spear or a sword runs the one under it through
                Hurt h = _kind == SkyFall.Fist && d < 0.4f ? Hurt.DriveIn : (_kind == SkyFall.Sword || _kind == SkyFall.Spear) && d < 0.3f ? Hurt.Crumple : Hurt.Launch;
                Strike(u, _mult * Mathf.Lerp(1f, 0.45f, d), h, _at, (1.2f + 1.6f * (1f - d)) * K, (1.2f + 1.8f * (1f - d)) * K, 1.4f);
            }
        }

        public override void Kill()
        {
            if (_shadow != null)
            {
                _shadow.Held = false;
                _shadow.Life = _shadow.Age + 0.15f;
            }
            if (_head != null)
            {
                _head.Held = false;
                _head.Life = _head.Age;
            }
            if (_glow != null)
            {
                _glow.Held = false;
                _glow.Life = _glow.Age;
            }
        }
    }

    // ============================================================= BLACK HOLE ==

    internal sealed class BlackHoleHazard : Hazard
    {
        private readonly bool _terrain;
        private readonly Vector2 _at, _core;
        private readonly float _r, _mult, _dur;
        private Particle _ball, _rim;
        private bool _pulled, _blown;
        private float _moteT;

        public BlackHoleHazard(ChoreoMove m, Vector2 at, float r, float mult, float dur, bool terrain = true) : base(m)
        {
            _terrain=terrain;
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.6f, 3f);
            _core = at + new Vector2(0f, 1.1f * K);
            Choreo.Sound("event:/SFX/POWERS/DivineMagnet", Combat.TileAt(at));
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_core, 0.6f);
            float grow = Mathf.Clamp01(Age / 0.3f);
            float collapse = Mathf.Clamp01((Age - _dur) / 0.18f);
            float size = (0.6f + 0.35f * Mathf.Min(1f, _r / 4f)) * K * grow * (1f - collapse);
            if (vis && !_blown)
            {
                if (_ball == null || !_ball.Alive)
                {
                    _ball = Spec.Blob(_core, Mathf.Max(0.3f, size), Px, 9f, FxLayer.Top, 24, 1f, 0.1f, Color.black);
                    _rim = Spec.Blob(_core, Mathf.Max(0.3f, size) * 1.7f, Px, 9f, FxLayer.Top, 23, 1f, 0.8f, Fx.WithA(Hue.Main, 0.85f));
                    if (_ball != null)
                    {
                        _ball.Held = true;
                    }
                    if (_rim != null)
                    {
                        _rim.Held = true;
                    }
                }
                float pulse = 1f + 0.08f * Mathf.Sin(Age * 30f);
                if (_ball != null)
                {
                    _ball.Pos = _core;
                    _ball.SX = _ball.SY = Mathf.Max(0.01f, grow * (1f - collapse));
                }
                if (_rim != null)
                {
                    _rim.Pos = _core;
                    _rim.SX = _rim.SY = Mathf.Max(0.01f, grow * (1f - collapse) * pulse);
                }
                if (collapse <= 0f && ((int)(Age * 40f) & 1) == 0)
                {
                    // the accretion ring: a flat ellipse of light wheeling round it
                    Particle ring = Fx.Ring(_core, Px, 0.06f, size / Px * 1.9f, size / Px * 2.1f, 2, 1, FxLayer.Top, 25, 0.35f, 1f,
                        Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
                    if (ring != null)
                    {
                        ring.Rot = Age * 120f;
                    }
                }
                // light, dust and debris spiral in
                _moteT -= dt;
                if (_moteT <= 0f && collapse <= 0f)
                {
                    _moteT = 0.03f;
                    ChoreoFx.Motes(_core, Px, 3, _r / Px * 0.8f, 0.35f, Hue.Core, Hue.Edge, FxLayer.Top);
                    Vector2 from = _core + Random.insideUnitCircle.normalized * _r * Random.Range(0.6f, 1f);
                    Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], from, Px, 0.4f, FxLayer.Top, 22);
                    if (c != null)
                    {
                        c.Moving((_core - from) / 0.4f + Choreo.Perp((_core - from).normalized) * 8f * Px);
                        c.Spin = 700f;
                        c.Color(Fx.SoilColor(Combat.TileAt(from)), Fx.WithA(Color.black, 0.6f));
                    }
                }
            }
            if (!_pulled && Age >= 0.15f)
            {
                _pulled = true;
                Pull();
            }
            if (!_blown && Age >= _dur + 0.18f)
            {
                _blown = true;
                Blow();
            }
            return !_blown || Age < _dur + 0.4f;
        }

        /// <summary>Every foe within reach is torn off its feet and dragged into the dark.</summary>
        private void Pull()
        {
            Choreo.Foes(A, _at, _r, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                if (!Choreo.FairBlow(A, u, By, out float cm, out bool full, true) || !full)
                {
                    continue;
                }
                Impact im = Impact.Of(Hurt.Thrown, _at - u.current_position, 0f, 1.2f);
                im.To = _at + Random.insideUnitCircle * 0.5f * K;
                im.Lift = 1.1f * K;
                im.Spin = 720f;
                im.Daze = 0.2f;
                Victims.Hurt(u, A, By, im);
            }
        }

        /// <summary>It collapses to a point and goes off: everything near it flung far.</summary>
        private void Blow()
        {
            Kill();
            bool vis = Kit.Visible(_core, 0.6f);
            if (vis)
            {
                Kit.Flash(_core, Px, Mathf.RoundToInt(_r / Px * 0.5f), 0.1f, Color.white);
                Fx.Ring(_core, Px, 0.35f, 2f, _r / Px * 1.4f, 3, 1, FxLayer.Top, 10, 1f, 2.6f, Fx.WithA(Color.white, 1f), Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
                Fx.Ring(_at, Px, 0.5f, 2f, _r / Px * 1.8f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Hue.Main, 0.7f), Fx.WithA(Hue.Edge, 0f));
                Spec.Rocks(_at, Px, 12, 80f, K);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", Combat.TileAt(_at));
            if(_terrain)
            {
                Ruin.Blast(A, _at, _r * 0.55f, Scale, null, false);
                Ruin.Quake(A, _at, Scale, 1.4f, K);
            }
            HitIds.Clear();
            Choreo.Foes(A, _at, _r * 0.9f, Near, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Strike(Near[i], _mult, Hurt.Launch, _at, 2.4f * K, 1.8f * K, 1.5f);
            }
        }

        public override void Kill()
        {
            if (_ball != null)
            {
                _ball.Held = false;
                _ball.Life = _ball.Age;
            }
            if (_rim != null)
            {
                _rim.Held = false;
                _rim.Life = _rim.Age;
            }
        }
    }

    // ================================================================= TRENCH ==

    internal sealed class TrenchHazard : Hazard
    {
        private readonly Vector2 _from, _dir, _side;
        private readonly float _len, _w, _mult, _speed;
        private readonly bool _cut;
        private float _done;

        public TrenchHazard(ChoreoMove m, Vector2 from, Vector2 dir, float len, float width, float mult, float speed, bool cut = false) : base(m)
        {
            _cut = cut;
            _from = from;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _side = Choreo.Perp(_dir);
            _len = len * Mathf.Lerp(1f, K, 0.5f);
            _w = width * Mathf.Lerp(1f, K, 0.5f);
            _mult = mult;
            _speed = speed;
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(from));
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float reach = Mathf.Min(_len, Age * _speed);
            while (_done + 0.5f <= reach)
            {
                Vector2 a = _from + _dir * _done;
                Vector2 b = _from + _dir * (_done + 0.5f);
                _done += 0.5f;
                Split(a, b, _done >= _len - 0.01f);
            }
            if (_done >= _len - 0.01f && Age > _len / _speed + 0.2f)
            {
                return false;
            }
            return true;
        }

        private void Split(Vector2 a, Vector2 b, bool end)
        {
            WorldTile tile = Combat.TileAt(b);
            if (tile == null || tile.Type != null && tile.Type.liquid)
            {
                return;
            }
            Ruin.Furrow(A, a, b, _w * (_cut ? 0.3f : 0.6f), Scale, By.Pose.Ground, !_cut);
            if (_cut && Kit.Visible(b))
            {
                // a blade's cut: a line of white light drawn across the land, the ground opening under it
                int cw = Mathf.Max(1, Mathf.RoundToInt(_w / Px * 0.2f));
                Kit.Line(a, b, Px, 1.3f, cw + 3, FxLayer.Ground, 4, Fx.WithA(Hue.Edge, 0.75f), Fx.WithA(Hue.Edge, 0.3f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(a, b, Px, 1.1f, cw, FxLayer.Ground, 5, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.Flash(b, Px, Mathf.RoundToInt(_w / Px * 0.5f), 0.06f, Fx.WithA(Color.white, 0.9f), FxLayer.Top, 12);
                Kit.Sparks(b, Px, 2, 60f, 120f, Hue.Edge);
                if (((int)(_done * 2f) % 2) == 0)
                {
                    Spec.Rocks(b, Px, 3, 70f, K);
                }
                if (end)
                {
                    Kit.Flash(b, Px, Mathf.RoundToInt(_w / Px), 0.1f, Color.white);
                    Kit.Sparks(b, Px, 12, 80f, 160f, Hue.Edge);
                }
            }
            else if (Kit.Visible(b))
            {
                // the crack's glowing heart, its dark lips, rocks bursting up out of it
                int w = Mathf.Max(2, Mathf.RoundToInt(_w / Px * 0.35f));
                Kit.Line(a, b, Px, 1.6f, w + 2, FxLayer.Ground, 4, Fx.WithA(Color.black, 0.7f), Fx.WithA(Color.black, 0.4f), Fx.WithA(Color.black, 0f));
                Kit.Line(a, b, Px, 1.4f, w, FxLayer.Ground, 5, Fx.WithA(Color.white, 0.95f), Fx.WithA(Spec.Hot, 0.9f), Fx.WithA(Spec.Molten, 0.6f), Fx.WithA(Spec.Ember, 0f));
                Kit.Flash(b, Px, Mathf.RoundToInt(_w / Px * 0.7f), 0.08f, Fx.WithA(Spec.Hot, 0.8f), FxLayer.Top, 12);
                Spec.Rocks(b, Px, 6, 85f, K);
                if (((int)(_done * 2f) % 3) == 0)
                {
                    Kit.Crater(b, Px * K * 0.7f, tile, true, 5f, true, Spec.Molten);
                }
                Kit.DustRing(b, Px, tile, 4, 26f, 0.4f, K);
                if (end)
                {
                    Kit.Flash(b, Px, Mathf.RoundToInt(_w / Px), 0.1f, Spec.Hot);
                    Spec.Rocks(b, Px, 12, 100f, K);
                    Kit.Crater(b, Px * K, tile, true, 5f, false, Spec.Molten);
                }
            }
            Choreo.Foes(A, Vector2.Lerp(a, b, 0.5f), _w + 0.3f, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float s = Vector2.Dot(u.current_position - a, _side) >= 0f ? 1f : -1f;
                Strike(u, _mult, Hurt.Launch, u.current_position - _side * s, 1.6f * K, 1.5f * K, 1.2f);
            }
            if (end)
            {
                Ruin.Blast(A, b, 1.4f, Scale, By.Pose.Ground, true);
            }
        }
    }

    // ================================================================ SKY BEAM ==

    internal sealed class SkyBeamHazard : Hazard
    {
        private readonly bool _terrain;
        private readonly Vector2 _at;
        private readonly float _r, _mult, _dur, _warn;
        private int _waves;

        public SkyBeamHazard(Actor a, Move by, Hue hue, Scale scale, Vector2 at, float r, float mult, float dur) : base(a, by, hue, scale)
        {
            _terrain=true;
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.4f, 3f);
            _warn = 0.45f;
            Choreo.Sound("event:/SFX/POWERS/DivineLight", Combat.TileAt(at));
        }

        public SkyBeamHazard(ChoreoMove m, Vector2 at, float r, float mult, float dur, bool terrain = true) : base(m)
        {
            _terrain=terrain;
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.4f, 3f);
            _warn = 0.45f;
            Choreo.Sound("event:/SFX/POWERS/DivineLight", Combat.TileAt(at));
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_at, 0.6f);
            Vector2 top = _at + new Vector2(0f, 22f * K);
            if (Age < _warn)
            {
                if (vis && ((int)(Age * 20f) % 3) == 0)
                {
                    // the mark on the ground and a thread of light coming down to it
                    Fx.Ring(_at, Px, 0.12f, _r / Px * 1.2f, _r / Px * 0.8f, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Line(top, _at, Px, 0.06f, 1, FxLayer.Top, 9, Fx.WithA(Hue.Core, 0.5f), Fx.WithA(Hue.Edge, 0f));
                }
                return true;
            }
            float t = Age - _warn;
            if (vis && t < _dur)
            {
                // the column: a wide glow, a bright core, flickering
                int w = Mathf.Max(2, Mathf.RoundToInt(_r / Px * (0.9f + 0.1f * Mathf.Sin(t * 50f))));
                Kit.Line(top, _at, Px, 0.05f, w, FxLayer.Top, 18, Fx.WithA(Hue.Main, 0.55f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(top, _at, Px, 0.05f, Mathf.Max(1, w / 2), FxLayer.Top, 19, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Core, 0f));
                if (Random.value < 0.5f)
                {
                    Spec.Rocks(_at + Random.insideUnitCircle * _r * 0.5f, Px, 1, 60f, K);
                    Kit.Sparks(_at, Px, 2, 40f, 90f, Hue.Edge);
                }
            }
            // it strikes three times while it burns: as it falls, through it, and as it goes
            int wave = t < 0.02f ? 1 : t < _dur * 0.5f ? 2 : t < _dur ? 3 : 4;
            while (_waves < Mathf.Min(wave, 3))
            {
                _waves++;
                HitIds.Clear();
                if (_waves == 1)
                {
                    if (vis)
                    {
                        Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px), 0.1f, Color.white);
                        Fx.Ring(_at, Px, 0.4f, 2f, _r / Px * 2f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                        Kit.Crater(_at, Px * K, Combat.TileAt(_at), true, 7f, false, Hue.Edge);
                    }
                    Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", Combat.TileAt(_at));
                    if(_terrain)
                    {
                        Ruin.Blast(A, _at, _r, Scale, null, true);
                        Ruin.Quake(A, _at, Scale, 1.3f, K);
                    }
                }
                Choreo.Foes(A, _at, _r, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Strike(Near[i], _mult / 3f, _waves == 3 ? Hurt.Launch : Hurt.Knockdown, _at, (_waves == 3 ? 1.6f : 0.5f) * K, 1.4f * K, 1.1f);
                }
            }
            if (t >= _dur && _waves < 4)
            {
                _waves = 4;
                if (vis)
                {
                    Spec.Column(_at, Px, 6, K, Spec.Smoke);
                }
            }
            return t < _dur + 0.3f;
        }
    }

    // =============================================================== LIGHTNING ==

    internal sealed class LightningHazard : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _mult, _dur;
        private readonly int _count;
        private int _fired;
        private readonly List<Actor> _marks = new List<Actor>();

        public LightningHazard(Actor a, Move by, Hue hue, Scale scale, Vector2 at, float r, int count, float mult, float dur) : base(a, by, hue, scale)
        {
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _count = Mathf.Clamp(count, 1, 40);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.2f, 3f);
            Choreo.Foes(A, at, _r, Near);
            _marks.AddRange(Near);
        }

        public LightningHazard(ChoreoMove m, Vector2 at, float r, int count, float mult, float dur) : base(m)
        {
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _count = Mathf.Clamp(count, 1, 40);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.2f, 3f);
            Choreo.Foes(A, at, _r, Near);
            _marks.AddRange(Near);
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            int due = Mathf.Min(_count, 1 + (int)(Age / Mathf.Max(0.01f, _dur / _count)));
            while (_fired < due)
            {
                Actor u = null;
                for (int i = 0; i < _marks.Count; i++)
                {
                    Actor c = _marks[(_fired + i) % _marks.Count];
                    if (Combat.Living(c))
                    {
                        u = c;
                        break;
                    }
                }
                Vector2 spot = u != null ? u.current_position : _at + Random.insideUnitCircle * _r;
                _fired++;
                Fire(spot, u);
            }
            return _fired < _count || Age < _dur + 0.2f;
        }

        private void Fire(Vector2 spot, Actor u)
        {
            Vector2 top = spot + new Vector2(Random.Range(-3f, 3f), 16f) * K;
            if (Kit.Visible(spot, 0.4f))
            {
                Spec.Bolt(top, spot + new Vector2(0f, 0.3f * K), Px, 0.12f, Hue.Edge, FxLayer.Top, 20);
                Kit.Flash(spot, Px, Mathf.RoundToInt(10f * K), 0.08f, Color.white);
                Fx.Ring(spot, Px, 0.2f, 2f, 14f * K, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(spot, Px, 5, 50f, 110f, Hue.Edge);
                Kit.Crater(spot, Px * K * 0.6f, Combat.TileAt(spot), false, 3f, true, Hue.Edge);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", Combat.TileAt(spot), 0.05f);
            Ruin.Blast(A, spot, 0.6f, Scale, By.Pose.Ground, true);
            if (u != null)
            {
                HitIds.Remove(u.Sid());
                Strike(u, _mult, Hurt.Knockdown, spot + Random.insideUnitCircle * 0.2f, 0.4f * K, -1f, 1.1f);
            }
            Choreo.Foes(A, spot, 0.8f * K, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                if (Near[i] != u)
                {
                    HitIds.Remove(Near[i].Sid());
                    Strike(Near[i], _mult * 0.4f, Hurt.Reel, spot, 0.6f * K);
                }
            }
        }
    }

    // ============================================================== SWORD GATE ==

    internal sealed class SwordGateHazard : Hazard
    {
        private readonly int _count;
        private readonly float _mult, _range;
        private readonly ShotKind _kind;
        private readonly List<Vector2> _spots = new List<Vector2>();
        private readonly List<Particle> _blades = new List<Particle>();
        private int _fired;
        private const float Open = 0.45f;

        public SwordGateHazard(ChoreoMove m, int count, float mult, float range, ShotKind kind = ShotKind.LightSword) : base(m)
        {
            _kind = kind;
            _count = Mathf.Clamp(count, 1, 30);
            _mult = mult;
            _range = range;
            Vector2 body = m.BodyPos();
            for (int i = 0; i < _count; i++)
            {
                float k = _count == 1 ? 0.5f : i / (float)(_count - 1);
                // an arc of gates behind and above the performer
                Vector2 o = -m.Dir * Mathf.Lerp(0.6f, 1.6f, Mathf.Abs(k - 0.5f) * 2f) * K + m.Side * Mathf.Lerp(-2.2f, 2.2f, k) * K + new Vector2(0f, Random.Range(1.2f, 2.6f) * K);
                _spots.Add(body + o);
            }
            Choreo.Sound("event:/SFX/POWERS/Powerup", m.A.current_tile);
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(M.Pose.Ground, 0.6f);
            // each gate opens with a ripple, its blade sliding out, point to the enemy
            int open = Mathf.Min(_count, Mathf.FloorToInt(Age / (Open / _count)) + 1);
            while (_blades.Count < open)
            {
                Vector2 s = _spots[_blades.Count];
                Particle b = null;
                if (vis)
                {
                    Fx.Ring(s, Px, 0.3f, 1f, 6f * K, 1, 1, FxLayer.Top, 12, 1f, 2f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                    b = Kit.Line(s, s + Aim(s) * 0.7f * K, Px, 3f, 1, FxLayer.Top, 14, Fx.WithA(Color.white, 0.95f));
                    if (b != null)
                    {
                        b.Held = true;
                    }
                }
                _blades.Add(b);
            }
            if (Age < Open + 0.2f)
            {
                return true;
            }
            int due = Mathf.Min(_count, 1 + (int)((Age - Open - 0.2f) / 0.06f));
            while (_fired < due)
            {
                Vector2 s = _spots[_fired];
                Particle b = _blades[_fired];
                if (b != null)
                {
                    b.Held = false;
                    b.Life = b.Age;
                }
                _fired++;
                if (!OwnerOk)
                {
                    continue;
                }
                Actor t = Pick(s);
                ShotSpec sp = new ShotSpec
                {
                    Kind = _kind, Speed = 30f, Range = _range, Mult = _mult, Hurt = Hurt.Reel, Push = 1f, Size = _kind == ShotKind.Spear ? 1.15f : 0.9f,
                    Impact = ShotImpact.Pin, Element = Element.School
                };
                Shots.Fire(A, M, s, t != null ? t.current_position - (s - new Vector2(0f, s.y - Choreo.Feet(A).y)) : M.Dir, t, Vector2.zero, sp, Hue);
            }
            return _fired < _count;
        }

        private Vector2 Aim(Vector2 s)
        {
            Actor t = M.V != null && M.VOk ? M.V : null;
            Vector2 d = t != null ? Choreo.BodyAt(t, 0.5f) - s : M.Dir;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.right;
        }

        private Actor Pick(Vector2 s)
        {
            Choreo.Foes(A, M.Pose.Ground, _range, Near);
            if (Near.Count == 0)
            {
                return null;
            }
            return Near[_fired % Near.Count];
        }

        public override void Kill()
        {
            foreach (Particle b in _blades)
            {
                if (b != null)
                {
                    b.Held = false;
                    b.Life = b.Age;
                }
            }
        }
    }

    // ============================================================ SLASH DOMAIN ==

    internal sealed class SlashDomainHazard : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _mult, _dur;
        private float _cutT, _ringT;
        private bool _burst;
        private readonly Dictionary<long, int> _cuts = new Dictionary<long, int>();

        public SlashDomainHazard(ChoreoMove m, Vector2 at, float r, float mult, float dur) : base(m)
        {
            _at = at;
            _r = r * Mathf.Lerp(1f, K, 0.5f);
            _mult = mult;
            _dur = Mathf.Clamp(dur, 0.5f, 3f);
            Choreo.Sound("event:/SFX/POWERS/Curse", Combat.TileAt(at));
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_at, 0.6f);
            _ringT -= dt;
            if (vis && _ringT <= 0f && Age < _dur)
            {
                // the domain's edge drawn on the ground, shimmering
                _ringT = 0.12f;
                Fx.Ring(_at, Px, 0.16f, _r / Px, _r / Px, 2, 2, FxLayer.Ground, 4, Art.Ground, 1f, Fx.WithA(Hue.Edge, 0.95f), Fx.WithA(Hue.Edge, 0f));
                Spec.Blob(_at, _r, Px, 0.16f, FxLayer.Ground, 2, Art.Ground, 0.15f, Fx.WithA(Hue.Dark, 0.35f), Fx.WithA(Hue.Dark, 0f));
            }
            if (Age < _dur)
            {
                // the cuts come out of nowhere, faster and faster
                _cutT -= dt;
                float gap = Mathf.Lerp(0.09f, 0.025f, Age / _dur);
                while (_cutT <= 0f)
                {
                    _cutT += gap;
                    Cut(vis);
                }
                return true;
            }
            if (!_burst)
            {
                _burst = true;
                HitIds.Clear();
                if (vis)
                {
                    Kit.Flash(_at, Px, Mathf.RoundToInt(_r / Px * 0.8f), 0.1f, Color.white);
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 d = Choreo.Rotate(Vector2.right, i * 30f + Random.Range(-8f, 8f));
                        Kit.Line(_at - d * _r, _at + d * _r, Px, 0.18f, 2, FxLayer.Top, 20, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                    }
                }
                Choreo.Sound("event:/SFX/HIT/HitSwordSword", Combat.TileAt(_at));
                Ruin.Blast(A, _at, _r * 0.6f, Scale, By.Pose.Ground, false);
                Choreo.Foes(A, _at, _r, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Strike(Near[i], _mult * 0.5f, Hurt.Launch, _at, 1.4f * K, 1.4f * K, 1.3f);
                }
            }
            return Age < _dur + 0.3f;
        }

        private void Cut(bool vis)
        {
            Vector2 c = _at + Random.insideUnitCircle * _r * 0.9f;
            Vector2 d = Choreo.Rotate(Vector2.right, Random.Range(0f, 180f));
            float half = Random.Range(0.9f, 2.2f) * K;
            if (vis)
            {
                Kit.Line(c - d * half, c + d * half, Px, 0.12f, 2, FxLayer.Top, 19, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(c - d * half * 0.9f, c + d * half * 0.9f, Px, 0.08f, 4, FxLayer.Top, 18, Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(c - d * half * 0.8f, c + d * half * 0.8f, Px, 0.6f, 1, FxLayer.Ground, 5, Fx.WithA(Hue.Dark, 0.7f), Fx.WithA(Hue.Dark, 0f));
            }
            if (!OwnerOk)
            {
                return;
            }
            Choreo.Foes(A, c, half, Near);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                long id = u.Sid();
                _cuts.TryGetValue(id, out int n);
                if (n >= 6 || !Choreo.FairBlow(A, u, By, out float cm, out bool full, true))
                {
                    continue;
                }
                _cuts[id] = n + 1;
                Combat.Hit(A, u, _mult * 0.1f * cm, false, false, 0f, By);
                Victims.Hurt(u, A, By, Impact.Of(full ? (n % 2 == 0 ? Hurt.Flinch : Hurt.Spin) : Hurt.Stagger, Choreo.Perp(d), 0.15f, 0.6f));
                if (vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), Choreo.Perp(d), Px, K, 0.5f, Hue);
                }
            }
        }
    }
}
