using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN'S FIST (Iron Fist, God). The world dims and holds its breath (a cinematic, when the
    /// settings allow one): the God draws one fist back while light pours into it, the air rushing in
    /// from all around, golden cracks opening in the ground under its feet (Charging: a fast enough
    /// blow can still break it). Then a single straight thrown at the horizon. A frame of white: the
    /// air in front of the fist is driven out so hard that a corridor opens 25-40 tiles long, racing
    /// away from the fist in a streak of light: the clouds over it part, walls of earth and dust are
    /// thrown up on both banks, everyone in it (fliers too) is blasted the length of it, the buildings
    /// in its way collapse, and the ground under it is torn out into a canyon that glows as it cools.
    /// Unlike the Earthshaker's rolling column it is not stopped by mountains: it is the air itself.
    /// I: 25 tiles. II: 32. III (a God's): 40 tiles, a 2-tile canyon. Damage to each one in it (x the
    /// user's damage): I 4, II 5, III 6.5.
    /// </summary>
    internal sealed class HeavensFistTech : Technique
    {
        public override string Id => "heavens_fist";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Line;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Line | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 90f, 120f, 150f };
        public override float[] Cooldown => new[] { 90f, 100f, 110f };

        public static float Length(int level)
        {
            return level <= 1 ? 25f : level == 2 ? 32f : 40f;
        }

        public static float Width(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.6f : 2f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6.5f;
        }

        public static float ChargeTime(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.1f : 1.2f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float len = Mathf.Min(Length(level), Mathf.Max(16f, c != null ? c.Awareness : 16f));
            Plan p = Choreo.PickLine(c, this, level, len, Width(level) * 0.7f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (c.Dist(p.Target) < 2.5f)
            {
                return null;   // the gathering would be broken before the fist came round
            }
            // never a canyon through its own people
            int allies = 0;
            for (int i = 0; i < c.Allies.Count; i++)
            {
                Vector2 rel = c.Allies[i].current_position - c.Pos;
                float along = Vector2.Dot(rel, p.Dir);
                if (along > 0.5f && along < Length(level) && Mathf.Abs(rel.x * p.Dir.y - rel.y * p.Dir.x) < Width(level) + 0.6f)
                {
                    allies++;
                }
            }
            if (allies >= 2)
            {
                return null;
            }
            p.Score *= allies > 0 ? 0.6f : p.Count >= 4 ? 1.3f : 1f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            HeavensFistMove m = HeavensFistMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Heaven's Fist's move: the cinematic, the gathering, the straight, the corridor it opens.</summary>
    internal sealed class HeavensFistMove : ChoreoMove
    {
        private HeavensCorridor _corridor;
        private Hue _light;
        private float _inT, _crackT, _ringT;
        private int _cracks;

        private HeavensFistMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _light = Hue.As(Element.Holy);
        }

        public static HeavensFistMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new HeavensFistMove(a, t, p);
        }

        private const Form Thrown = Form.Punch | Form.Line | Form.Ultimate | Form.Straight;

        public void Build()
        {
            int level = Level;
            Face(Aim);
            // the world holds its breath (only one cinematic at a time, on screen, if the settings allow it)
            OpenCinematic(8f, 0.5f, "sakuga_title_heavens_fist");
            Charge(HeavensFistTech.ChargeTime(level), ChargeStyle.Fist, 2.8f + 0.5f * level, Element.Holy)
                .Every((mm, k) => ((HeavensFistMove)mm).Gather(k));
            // the last draw: a glint on the fist
            PoseTo(0.07f, new Shape(-20f, 0.88f, 1.1f, 0f, -0.12f), Ease.OutQuad, Tempo.Wind).AsCharge()
                .At(0.5f, mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.Hand(Limb.Fist) + new Vector2(0f, 2f * mm.Px), mm.Px, 0.2f, Color.white, 1.4f);
                    }
                });
            // the straight
            PoseTo(0.08f, new Shape(26f, 1.38f, 0.78f, 0f, 0.28f), Ease.OutExpo, Tempo.Act).Shows(Thrown)
                .OnEnter(mm => ((HeavensFistMove)mm).Throw());
            Hold(0.2f, true, Tempo.Impact).Shows(Thrown);
            // the fist stays out while the corridor opens to the horizon
            WaitUntil(mm => ((HeavensFistMove)mm)._corridor == null || ((HeavensFistMove)mm)._corridor.Done, 1.6f).Shows(Thrown);
            Do(mm => mm.EndCinematic());
            Recover(0.4f).Shows(Form.Punch | Form.Ultimate);
        }

        /// <summary>The gathering: the air rushing in to the fist, the ground cracking gold under its feet.</summary>
        private void Gather(float k)
        {
            float dt = Clock.Dt;
            _crackT -= dt;
            if (_crackT <= 0f)
            {
                _crackT = 0.12f;
                _cracks++;
                if (_cracks % 2 == 0)
                {
                    Ruin.Quake(A, Pose.Ground, Scale.Heavy, 0.4f + 0.6f * k, Kk);
                }
                if (Vis)
                {
                    float px = Px;
                    int len = Mathf.RoundToInt((6f + 14f * k) * Kk);
                    float a = _cracks * 137.5f;
                    Particle f = Fx.Spawn(ChoreoArt.Fissure(len, _cracks), Pose.Ground, px, 2.8f, FxLayer.Ground, 2);
                    if (f != null)
                    {
                        f.Rot = a;
                        f.Snap = true;
                        Color dark = Fx.Darken(Fx.SoilColor(A.current_tile), 0.55f);
                        f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                    }
                    Particle g = Fx.Spawn(ChoreoArt.Fissure(len, _cracks), Pose.Ground, px, 1.2f, TopLayer, 3);
                    if (g != null)
                    {
                        g.Rot = a;
                        g.Snap = true;
                        g.Cine = Cinematic;
                        g.Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Core, 0.8f), Fx.WithA(_light.Edge, 0f));
                    }
                }
            }
            if (!Vis)
            {
                return;
            }
            float p = Px;
            Vector2 orb = OrbAt();
            // the air rushing in from every side
            _inT -= dt;
            if (_inT <= 0f)
            {
                _inT = Mathf.Lerp(0.05f, 0.02f, k);
                float ang = Random.value * Mathf.PI * 2f;
                Vector2 off = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1.5f, 3f) * Kk;
                Kit.SpeedLines(orb + off, -off, p, 1, 2f, 0.14f, Fx.WithA(_light.Core, 0.8f), TopLayer);
                if (Random.value < 0.4f)
                {
                    Vector2 at = Pose.Ground + off;
                    Particle d = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at, p, 0.45f, FxLayer.Top, 6);
                    Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(at)), 0.35f);
                    d?.Moving(-off * 3f, Random.Range(4f, 10f) * p, 1f).Sized(1f, 0.4f).Color(Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0f));
                }
            }
            // a ring of light closing on its feet
            _ringT -= dt;
            if (_ringT <= 0f)
            {
                _ringT = 0.22f;
                Fx.Ring(Pose.Ground, p, 0.26f, (26f + 10f * k) * Kk, 4f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.5f,
                    Fx.WithA(_light.Edge, 0f), Fx.WithA(_light.Edge, 0.7f), Fx.WithA(_light.Core, 0.95f));
            }
        }

        /// <summary>The straight: a frame of white, and the corridor opens.</summary>
        private void Throw()
        {
            int level = Level;
            Vector2 dir = Aim;
            if (V != null && VOk)
            {
                Vector2 d = VG - Stance;
                if (d.sqrMagnitude > 0.04f && Vector2.Angle(d, Aim) < 30f)
                {
                    dir = d.normalized;
                }
            }
            Face(dir);
            SpendOrb();
            Vector2 fist = Hand(Limb.Fist);
            Vector2 from = Stance + dir * 0.5f * Kk;
            float len = HeavensFistTech.Length(level) * Mathf.Lerp(1f, Kk, 0.4f);
            _corridor = Areas.Add(new HeavensCorridor(this, from, dir, len, HeavensFistTech.Width(level) * Mathf.Lerp(1f, Kk, 0.4f),
                HeavensFistTech.Mult(level), level, _light));
            if (Cinematic)
            {
                Freeze(0.14f);
            }
            else
            {
                Kit.Stop(fist, 0.1f * ImpactK);
            }
            Ruin.Quake(A, from, Scale, 1.6f, Kk);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", A.current_tile);
            ChoreoFx.BlowSound(Limb.Fist, 2f, false, A.current_tile);
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Kit.ImpactFrame(fist, px, 0.8f * Kk, Hue.Pal, false, TopLayer);
            Kit.Flash(fist, px, Mathf.RoundToInt(9f * Kk), 0.14f, Fx.WithA(Color.white, 1f), TopLayer, 31);
            Fx.Ring(fist, px, 0.3f, 4f, 30f * Kk, 3, 1, TopLayer, 6, 1.6f, 2.6f, Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            Fx.Ring(Pose.Ground, px, 0.4f, 6f, 40f * Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            ChoreoFx.Smear(Root(Limb.Fist), fist + dir * 4f * px, px, 0.16f, 3, 4, _light.Core, Hue.Edge, TopLayer, 8);
            Kit.DustRing(Pose.Ground, px, A.current_tile, 18, 70f, 0.7f, Kk);
            Kit.SpeedLines(fist + dir * 6f * px, dir, px, 10, 8f * Kk, 0.2f, Fx.WithA(_light.Core, 0.9f), TopLayer);
            Fx.FocusLines(fist, px, Fx.Count(10), 10f * Kk, 18f * Kk, 0.14f);
        }
    }

    /// <summary>
    /// The corridor a Heaven's Fist opens: a front racing away from the fist at 80 tiles a second, a
    /// streak of light behind it, the clouds over it parting, its enemies (on the ground and in the
    /// air) blasted the length of it, a canyon torn out of the ground (Ruin, at the Ultimate scale:
    /// everything built in its way collapses), and a towering burst where it ends.
    /// </summary>
    internal sealed class HeavensCorridor : Hazard
    {
        private readonly Vector2 _from, _dir, _side;
        private readonly float _len, _width, _mult, _push, _lift;
        private readonly int _level;
        private readonly Hue _light;
        private float _d, _furrowAt, _lineT, _ringT, _fxT, _shakeT, _glowAt;
        private bool _ended;
        private static readonly List<Actor> _hit = new List<Actor>();

        public HeavensCorridor(ChoreoMove m, Vector2 from, Vector2 dir, float len, float width, float mult, int level, Hue light) : base(m)
        {
            _from = from;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _side = Choreo.Perp(_dir);
            _len = len;
            _width = width;
            _mult = mult;
            _level = level;
            _light = light;
            _push = level <= 1 ? 6f : level == 2 ? 8f : 10f;
            _lift = level <= 1 ? 2.4f : level == 2 ? 2.9f : 3.4f;
            // (never past the map's edge)
            Vector2 end = Combat.Clamp(_from + _dir * _len);
            _len = Mathf.Min(_len, Vector2.Distance(_from, end));
        }

        private const float Speed = 80f;

        private FxLayer Layer => M != null && M.Cinematic ? FxLayer.Cine : FxLayer.Top;

        private bool Cine => M != null && M.Cinematic;

        public override bool Tick(float dt)
        {
            Age += dt;
            if (dt <= 0f)
            {
                return true;
            }
            if (_ended)
            {
                return Age < 0.3f;
            }
            float was = _d;
            _d = Mathf.Min(_len, _d + Speed * dt);
            Vector2 a = _from + _dir * Mathf.Max(0f, was - 0.5f);
            Vector2 b = _from + _dir * _d;
            if (OwnerOk)
            {
                Choreo.FoesOnLine(A, a, b, _width, _hit, true);
                for (int i = 0; i < _hit.Count; i++)
                {
                    Actor u = _hit[i];
                    float side = Vector2.Dot(u.current_position - _from, _side) >= 0f ? 1f : -1f;
                    Vector2 from = u.current_position - (_dir * 1.2f + _side * side * 0.15f);
                    Strike(u, _mult, Hurt.Launch, from, _push, _lift, 2f);
                }
            }
            // the canyon, a stretch at a time
            if (_d - _furrowAt >= 2f || _d >= _len)
            {
                Vector2? keep = A != null && A.isAlive() ? A.current_position : (Vector2?)null;
                Ruin.Furrow(A, _from + _dir * _furrowAt, b, _width * 0.75f, Scale, keep, true);
                _furrowAt = _d;
            }
            _shakeT -= dt;
            if (_shakeT <= 0f)
            {
                _shakeT = 0.1f;
                Ruin.Quake(A, b, Scale, 1f, K);
            }
            if (Kit.Visible(b, 0.8f) || Kit.Visible(_from, 0.3f))
            {
                Render(dt, b);
            }
            if (_d >= _len)
            {
                _ended = true;
                Age = 0f;
                End(b);
            }
            return true;
        }

        private void Render(float dt, Vector2 front)
        {
            float px = Px;
            Vector2 up = new Vector2(0f, 0.55f * K);
            FxLayer layer = Layer;
            // the streak of light from the fist to the front
            _lineT -= dt;
            if (_lineT <= 0f)
            {
                _lineT = 0.03f;
                Particle o = Kit.Line(_from + up, front + up, px, 0.1f, 7 + _level, FxLayer.Behind, 5, Fx.WithA(_light.Edge, 0.6f), Fx.WithA(Hue.Rim, 0.35f), Fx.WithA(Hue.Rim, 0f));
                Particle c = Kit.Line(_from + up, front + up, px, 0.08f, 2 + _level, layer, 15, Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Core, 0.7f), Fx.WithA(_light.Core, 0f));
                if (o != null)
                {
                    o.Cine = Cine;
                }
                if (c != null)
                {
                    c.Cine = Cine;
                }
            }
            _ringT -= dt;
            if (_ringT <= 0f)
            {
                _ringT = 0.035f;
                float ratio = Mathf.Lerp(0.4f, 1.7f, Mathf.Abs(_dir.x));
                Particle r = Fx.Ring(front + up, px, 0.2f, 4f, (12f + 3f * _level) * K, 3, 1, layer, 6, ratio, 2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                if (r != null)
                {
                    r.Cine = Cine;
                }
                Particle f = Kit.Flash(front + up, px, Mathf.RoundToInt(5f * K + _level), 0.06f, Fx.WithA(_light.Core, 0.9f), layer, 14);
                if (f != null)
                {
                    f.Cine = Cine;
                }
            }
            _fxT -= dt;
            if (_fxT <= 0f)
            {
                _fxT = 0.022f;
                WorldTile tile = Combat.TileAt(front);
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
                for (int s = -1; s <= 1; s += 2)
                {
                    // walls of earth and dust thrown up on both banks
                    Vector2 at = front + _side * s * _width * 0.7f;
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at, px, Random.Range(0.6f, 1f), FxLayer.Top, 7);
                    p?.Moving((_side * s * Random.Range(24f, 44f) + _dir * 8f) * px * K, Random.Range(12f, 26f) * px, 2.5f).Sized(1f, 2.4f)
                        .Color(Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.55f), Fx.WithA(dust, 0f));
                    // the clouds over it parting
                    Vector2 sky = front + _side * s * Random.Range(0.4f, 1.2f) * _width;
                    Particle cl = Fx.Spawn(Art.Dust[Random.Range(0, 4)], sky, px, Random.Range(0.7f, 1.1f), layer, 8);
                    if (cl != null)
                    {
                        cl.Z = Random.Range(2.5f, 4f) * K;
                        cl.Moving(_side * s * Random.Range(30f, 60f) * px * K, 0f, 1.5f).Sized(2f, 3.4f)
                            .Color(new Color(1f, 1f, 1f, 0.85f), new Color(0.95f, 0.95f, 1f, 0.5f), new Color(0.9f, 0.9f, 1f, 0f));
                        cl.Cine = Cine;
                    }
                }
                if (Random.value < 0.6f)
                {
                    Kit.Debris(front, px, tile, 3, K * 1.2f);
                }
                Kit.SpeedLines(front, _dir, px, 2, 6f * K, 0.12f, Fx.WithA(_light.Core, 0.85f), layer);
            }
            // the canyon's floor glowing as it cools
            while (_glowAt + 1.6f <= _d)
            {
                Vector2 at = _from + _dir * _glowAt;
                _glowAt += 1.6f;
                int len = Mathf.Clamp(Mathf.RoundToInt(1.8f / px), 6, 80);
                float rot = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
                Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(at)), 0.6f);
                Particle f = Fx.Spawn(ChoreoArt.Fissure(len, Mathf.RoundToInt(_glowAt)), at, px, 4f, FxLayer.Ground, 2);
                if (f != null)
                {
                    f.Rot = rot;
                    f.Snap = true;
                    f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                }
                Particle g = Fx.Spawn(ChoreoArt.Fissure(len, Mathf.RoundToInt(_glowAt) + 3), at, px, 2.2f, FxLayer.Ground, 3);
                if (g != null)
                {
                    g.Rot = rot;
                    g.Snap = true;
                    g.Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Core, 0.85f), Fx.WithA(Kit.Fire2, 0.5f), Fx.WithA(Kit.Fire3, 0f));
                }
            }
        }

        /// <summary>Where the corridor ends: a towering burst, a crater, rings racing out.</summary>
        private void End(Vector2 at)
        {
            Vector2? keep = A != null && A.isAlive() ? A.current_position : (Vector2?)null;
            Ruin.Blast(A, at, 1.4f, Scale, keep);
            Ruin.Quake(A, at, Scale, 1.3f, K);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", Combat.TileAt(at));
            if (!Kit.Visible(at, 0.5f))
            {
                return;
            }
            float px = Px;
            FxLayer layer = Layer;
            WorldTile tile = Combat.TileAt(at);
            Particle f = Kit.Flash(at + new Vector2(0f, 0.6f * K), px, Mathf.RoundToInt(10f * K), 0.14f, Fx.WithA(Color.white, 1f), layer, 30);
            if (f != null)
            {
                f.Cine = Cine;
            }
            Fx.Ring(at, px, 0.4f, 4f, 34f * K, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(_light.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
            Kit.DustRing(at, px, tile, 18, 70f, 0.8f, K);
            Kit.Debris(at, px, tile, 14, K * 1.3f);
            Kit.Crater(at, px * K, tile, true, 6f, false, _light.Edge);
        }
    }
}
