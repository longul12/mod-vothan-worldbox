using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// EARTHSHAKER (Iron Fist, Grandmaster). It plants its feet and cocks its fist back, gathering:
    /// the ground cracks open under its feet, stones lift off the earth around it, the fist glows
    /// (Charging: a fast blow now breaks it and sends it flying). Then one straight: the air in front
    /// of the fist is driven out as a pressure column that rolls along the ground 12-16 tiles, tearing
    /// a trench, throwing up walls of dust and earth on both sides, crumbling the buildings it passes,
    /// and blasting everyone in its path along the line. A mountain or a wall stops it (it bursts
    /// against it). It is no beam (nothing clashes with it); a line attack: it can be side-stepped.
    /// I (Grandmaster): 12 tiles, a 0.9-tile column. II: 14 tiles, 1.05. III: 16 tiles, 1.2, the
    /// trench's banks catch fire. Damage to each one in its path (x the user's damage): I 2.6, II 3.4,
    /// III 4.4.
    /// </summary>
    internal sealed class EarthshakerStraightTech : Technique
    {
        public override string Id => "earthshaker_straight";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Line;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Straight | Form.Charge | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 40f, 66f, 100f };
        public override float[] Cooldown => new[] { 14f, 15f, 16f };

        public static float Length(int level)
        {
            return level <= 1 ? 12f : level == 2 ? 14f : 16f;
        }

        public static float Width(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.05f : 1.2f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.4f : 4.4f;
        }

        public static float ChargeTime(int level)
        {
            return level <= 1 ? 0.75f : level == 2 ? 0.85f : 0.95f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickLine(c, this, level, Length(level), Width(level) * 0.8f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            // never point blank: the gathering would be broken before the fist came round
            if (c.Dist(p.Target) < 2f)
            {
                return null;
            }
            // a crowd standing in a line is what it is for
            p.Score *= p.Count >= 3 ? 1.25f : 1f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            EarthshakerMove m = EarthshakerMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Earthshaker's move: the gathering (the ground cracking under it), the straight, the column it looses.</summary>
    internal sealed class EarthshakerMove : ChoreoMove
    {
        private EarthshakerColumn _column;
        private float _crackT, _stoneT;
        private int _cracks;

        private EarthshakerMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static EarthshakerMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new EarthshakerMove(a, t, p);
        }

        private const Form Thrown = Form.Punch | Form.Straight | Form.Heavy | Form.Line;

        public void Build()
        {
            int level = Level;
            Face(Aim);
            // the gathering: the fist cocked back and glowing, the ground giving way under its feet
            Charge(EarthshakerStraightTech.ChargeTime(level), ChargeStyle.Fist, 2.2f + 0.4f * level)
                .Every((mm, k) => ((EarthshakerMove)mm).Gather(k));
            // the last draw: lower, the weight on the back foot (still gathering)
            PoseTo(0.06f, new Shape(-18f, 0.9f, 1.08f, 0f, -0.1f), Ease.OutQuad, Tempo.Wind).AsCharge();
            // the straight
            PoseTo(0.07f, new Shape(24f, 1.34f, 0.8f, 0f, 0.24f), Ease.OutExpo, Tempo.Act).Shows(Thrown)
                .OnEnter(mm => ((EarthshakerMove)mm).Fire());
            Hold(0.14f, true, Tempo.Impact).Shows(Thrown);
            // the fist stays out while the column rolls on
            WaitUntil(mm => ((EarthshakerMove)mm)._column == null || ((EarthshakerMove)mm)._column.Done, 0.45f).Shows(Thrown);
            Recover(0.34f).Shows(Form.Punch | Form.Heavy);
        }

        /// <summary>The ground cracks under its feet as it gathers; stones lift off the earth around it.</summary>
        private void Gather(float k)
        {
            float dt = Clock.Dt;
            _crackT -= dt;
            if (_crackT <= 0f)
            {
                _crackT = 0.14f;
                _cracks++;
                if (_cracks % 3 == 0)
                {
                    Ruin.Quake(A, Pose.Ground, Scale.Light, 0.5f + 0.5f * k, Kk);
                    Choreo.Sound("event:/SFX/HIT/HitStone", A.current_tile, 0.1f);
                }
                if (Vis)
                {
                    float px = Px;
                    WorldTile tile = A.current_tile;
                    int len = Mathf.RoundToInt((5f + 9f * k) * Kk * (0.8f + 0.2f * Level));
                    Particle f = Fx.Spawn(ChoreoArt.Fissure(len, _cracks), Pose.Ground, px, 2.4f, FxLayer.Ground, 2);
                    if (f != null)
                    {
                        float a = Random.value * 360f;
                        f.Rot = a;
                        f.Snap = true;
                        Color dark = Fx.Darken(Fx.SoilColor(tile), 0.55f);
                        f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                        Particle g = Fx.Spawn(ChoreoArt.Fissure(len, _cracks), Pose.Ground, px, 0.8f, FxLayer.Ground, 3);
                        if (g != null)
                        {
                            g.Rot = a;
                            g.Snap = true;
                            g.Color(Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                        }
                    }
                }
            }
            if (!Vis)
            {
                return;
            }
            _stoneT -= dt;
            if (_stoneT <= 0f && k > 0.25f)
            {
                _stoneT = 0.07f;
                // pebbles lifted off the ground by the pressure gathering round it
                float px = Px;
                Vector2 at = Pose.Ground + Random.insideUnitCircle * 1.4f * Kk;
                Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px, Random.Range(0.4f, 0.7f), FxLayer.Top, 4);
                if (p != null)
                {
                    Color soil = Fx.SoilColor(Combat.TileAt(at));
                    p.Moving((Pose.Ground - at) * 0.4f, Random.Range(14f, 30f) * px, 1f, 20f * px);
                    p.Bounce = 0.2f;
                    p.Spin = Random.Range(-200f, 200f);
                    p.Color(soil, soil, Fx.WithA(soil, 0f));
                }
            }
        }

        /// <summary>The straight lands on the air itself: the column is loosed along the line.</summary>
        private void Fire()
        {
            int level = Level;
            Vector2 dir = Dir;
            if (V != null && VOk)
            {
                Vector2 d = VG - Stance;
                if (d.sqrMagnitude > 0.04f && Vector2.Angle(d, Aim) < 30f)
                {
                    dir = d.normalized;
                }
            }
            else
            {
                dir = Aim;
            }
            Face(dir);
            SpendOrb();
            Vector2 from = Stance + dir * 0.45f * Kk;
            float len = EarthshakerStraightTech.Length(level) * Mathf.Lerp(1f, Kk, 0.5f);
            _column = Areas.Add(new EarthshakerColumn(this, from, dir, len, EarthshakerStraightTech.Width(level) * Mathf.Lerp(1f, Kk, 0.5f),
                EarthshakerStraightTech.Mult(level), level));
            Kit.Stop(Hand(Limb.Fist), (0.05f + 0.01f * level) * ImpactK);
            Ruin.Quake(A, from, Scale, 1.4f, Kk);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", A.current_tile);
            ChoreoFx.BlowSound(Limb.Fist, 1.8f, false, A.current_tile);
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Vector2 fist = Hand(Limb.Fist);
            ChoreoFx.Smear(Root(Limb.Fist), fist + dir * 3f * px, px, 0.12f, 3, 3, Hue.Core, Hue.Edge, FxLayer.Over, 8);
            Kit.Flash(fist, px, Mathf.RoundToInt((5f + level) * Kk), 0.09f, Fx.WithA(Hue.Core, 1f), TopLayer);
            Fx.Ring(fist, px, 0.22f, 3f, (12f + 3f * level) * Kk, 2, 1, TopLayer, 5, 1.4f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            Kit.DustRing(Pose.Ground, px, A.current_tile, 12, 48f, 0.55f, Kk);
            Kit.SpeedLines(fist + dir * 4f * px, dir, px, 6, 5f * Kk, 0.16f, Fx.WithA(Hue.Edge, 0.85f));
        }
    }

    /// <summary>
    /// The Earthshaker's pressure column: it rolls along the ground from the fist at about 34 tiles a
    /// second, blasting its enemies along the line (an area blow: they may slip it), trenching the
    /// ground (Ruin, at the move's Scale: buildings crumble), throwing walls of dust to both sides, and
    /// bursts where it ends (or against a mountain).
    /// </summary>
    internal sealed class EarthshakerColumn : Hazard
    {
        private readonly Vector2 _from, _dir, _side;
        private readonly float _len, _width, _mult, _push, _lift;
        private readonly int _level;
        private float _d, _furrowAt, _fxT, _ringT, _lineT, _shakeT, _fissureAt;
        private bool _stopped;
        private static readonly List<Actor> _hit = new List<Actor>();

        public EarthshakerColumn(ChoreoMove m, Vector2 from, Vector2 dir, float len, float width, float mult, int level) : base(m)
        {
            _from = from;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            _side = Choreo.Perp(_dir);
            _len = len;
            _width = width;
            _mult = mult;
            _level = level;
            _push = level <= 1 ? 3f : level == 2 ? 4f : 5f;
            _lift = level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.2f;
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(from), 0.3f);
        }

        private const float Speed = 34f;

        public override bool Tick(float dt)
        {
            Age += dt;
            if (dt <= 0f)
            {
                return true;
            }
            if (_stopped)
            {
                return false;
            }
            float was = _d;
            _d = Mathf.Min(_len, _d + Speed * dt);
            // a mountain or a wall stops it: it bursts against it
            for (float s = was + 0.5f; s <= _d; s += 0.5f)
            {
                if (!Combat.Passable(Combat.Clamp(_from + _dir * s), true))
                {
                    _d = Mathf.Max(was, s - 0.3f);
                    _stopped = true;
                    break;
                }
            }
            Vector2 a = _from + _dir * Mathf.Max(0f, was - 0.4f);
            Vector2 b = _from + _dir * _d;
            if (OwnerOk)
            {
                Choreo.FoesOnLine(A, a, b, _width, _hit);
                for (int i = 0; i < _hit.Count; i++)
                {
                    Actor u = _hit[i];
                    float side = Vector2.Dot(u.current_position - _from, _side) >= 0f ? 1f : -1f;
                    // thrown along the line, a little outward
                    Vector2 push = u.current_position - (_dir * 1.2f + _side * side * 0.25f);
                    Strike(u, _mult, Hurt.Launch, push, _push, _lift, 1.6f);
                }
            }
            // the trench, a stretch at a time
            if (_d - _furrowAt >= 1.5f || _d >= _len || _stopped)
            {
                Vector2? keep = A != null && A.isAlive() ? A.current_position : (Vector2?)null;
                Ruin.Furrow(A, _from + _dir * _furrowAt, b, _width * 0.85f, Scale, keep, _level >= 3);
                _furrowAt = _d;
            }
            _shakeT -= dt;
            if (_shakeT <= 0f)
            {
                _shakeT = 0.15f;
                Ruin.Quake(A, b, Scale, 0.9f, K);
            }
            if (Kit.Visible(b, 0.6f))
            {
                Render(dt, b);
            }
            if (_d >= _len || _stopped)
            {
                Burst(b);
                return false;
            }
            return true;
        }

        private void Render(float dt, Vector2 front)
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(front);
            Vector2 up = new Vector2(0f, 0.45f * K);
            // the pressure tunnel from the fist to the front, a white core in the school's colour
            _lineT -= dt;
            if (_lineT <= 0f)
            {
                _lineT = 0.035f;
                Kit.Line(_from + up, front + up, px, 0.1f, 4 + _level, FxLayer.Behind, 5, Fx.WithA(Hue.Edge, 0.55f), Fx.WithA(Hue.Rim, 0.3f), Fx.WithA(Hue.Rim, 0f));
                Kit.Line(_from + up, front + up, px, 0.08f, 1 + (_level >= 2 ? 1 : 0), FxLayer.Top, 14, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Core, 0.6f), Fx.WithA(Hue.Core, 0f));
            }
            // the front: a ring of compressed air, a flash, rocks and turf thrown up
            _ringT -= dt;
            if (_ringT <= 0f)
            {
                _ringT = 0.045f;
                float ratio = Mathf.Lerp(0.4f, 1.7f, Mathf.Abs(_dir.x));
                Fx.Ring(front + up, px, 0.16f, 3f, (7f + 1.5f * _level) * K, 2, 1, FxLayer.Top, 6, ratio, 2f,
                    Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0.55f), Fx.WithA(Hue.Edge, 0f));
                Kit.Flash(front + up, px, Mathf.RoundToInt(3f * K + _level), 0.05f, Fx.WithA(Hue.Core, 0.8f), FxLayer.Top, 13);
            }
            _fxT -= dt;
            if (_fxT <= 0f)
            {
                _fxT = 0.03f;
                // walls of dust to both sides
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 at = front + _side * s * _width * 0.6f;
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at, px, Random.Range(0.5f, 0.8f), s < 0 && _side.y > 0f || s > 0 && _side.y < 0f ? FxLayer.Top : FxLayer.Ground, 7);
                    p?.Moving((_side * s * Random.Range(18f, 34f) + _dir * 6f) * px * K, Random.Range(8f, 18f) * px, 3f).Sized(0.8f, 1.8f)
                        .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
                }
                if (Random.value < 0.45f)
                {
                    Kit.Debris(front, px, tile, 2, K);
                }
                Kit.SpeedLines(front, _dir, px, 1, 4f * K, 0.1f, Fx.WithA(Hue.Edge, 0.8f));
            }
            // the trench's cracks along the line behind it
            while (_fissureAt + 1.4f <= _d)
            {
                Vector2 at = _from + _dir * _fissureAt;
                _fissureAt += 1.4f;
                int len = Mathf.Clamp(Mathf.RoundToInt(1.5f / px), 6, 60);
                Particle f = Fx.Spawn(ChoreoArt.Fissure(len, Mathf.RoundToInt(_fissureAt)), at, px, 3f, FxLayer.Ground, 2);
                if (f != null)
                {
                    f.Rot = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
                    f.Snap = true;
                    Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(at)), 0.55f);
                    f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                }
            }
        }

        /// <summary>The column's end: it bursts (against the mountain that stopped it, or in the open).</summary>
        private void Burst(Vector2 at)
        {
            Vector2? keep = A != null && A.isAlive() ? A.current_position : (Vector2?)null;
            Ruin.Blast(A, at, 1f + 0.15f * _level, Scale, keep);
            Ruin.Quake(A, at, Scale, 1.2f, K);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", Combat.TileAt(at));
            if (!Kit.Visible(at, 0.4f))
            {
                return;
            }
            WorldTile tile = Combat.TileAt(at);
            Kit.Flash(at + new Vector2(0f, 0.4f * K), Px, Mathf.RoundToInt((6f + _level) * K), 0.1f, Fx.WithA(Hue.Core, 0.95f));
            Fx.Ring(at, Px, 0.3f, 3f, (16f + 4f * _level) * K, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
            Kit.DustRing(at, Px, tile, 14, 56f, 0.6f, K);
            Kit.Debris(at, Px, tile, 8 + 2 * _level, K);
            Kit.Crater(at, Px * K, tile, _level >= 2, 5f, false, Hue.Edge);
        }
    }
}
