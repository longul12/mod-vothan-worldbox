using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// MOON SPLITTER (Thousand Blades, Master). The blade rises straight up and a crescent moon forms
    /// above it, growing out of a sliver while motes of light stream into it and the ground hums
    /// (Charging: a fast blow now breaks it, the moon shatters). Then the blade comes down like a
    /// falling guillotine, the moon drops with it into the ground in front, and a single vertical cut
    /// races away along the line: the earth splits open in a glowing fissure 12-18 tiles long, walls
    /// of dust burst up on both banks, and whatever stands on the line is thrown aside as the ground
    /// parts under it. Buildings on the line break (a Master's blow spares none).
    /// I: 12 tiles; II: 15, wider, debris along it; III: 18, a white-hot seam, deeper.
    /// Damage to each one on the line (x the user's damage): I 2.6, II 3.4, III 4.6.
    /// Distinct from Crescent Fang (a wave skimming the ground, no wind-up): a raised moon, a
    /// committed overhead cut, and the ground itself splitting.
    /// </summary>
    internal sealed class MoonSplitterTech : Technique
    {
        public override string Id => "moon_splitter";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Line;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Line | Form.Charge | Form.Overhead;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 26f, 45f, 72f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Down;
        }

        public static float Length(int level)
        {
            return level <= 1 ? 12f : level == 2 ? 15f : 18f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.4f : 4.6f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 0.75f : level == 2 ? 0.85f : 0.95f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickLine(c, this, level, Length(level), 0.7f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            // never point blank: the wind-up would be broken before the blade came down
            if (c.Dist(p.Target) < 1.8f)
            {
                return null;
            }
            // the cut needs ground to run along at its feet
            if (!Combat.ClearPath(c.Pos, c.Pos + p.Dir * Mathf.Min(2f, c.Dist(p.Target))))
            {
                return null;
            }
            // more value the more it catches: a line of foes is what it is for
            p.Score *= p.Count >= 3 ? 1.3f : 1f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Moon m = new Moon(a, this, p);
            m.Build();
            return m.Start();
        }

        /// <summary>The move: the blade rises and the moon forms (Charging), the cut comes down, the ground splits.</summary>
        private sealed class Moon : ChoreoMove
        {
            private Particle _moon, _halo;
            private Actor _aim;
            private long _aimId = -1;
            private Vector2 _moonAt;
            private Hue _blade;
            private bool _glinted;

            public Moon(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                // a line technique: it does not hold its target still from across the field (a charge
                // that can be broken must leave the foe free to come and break it)
                if (V != null)
                {
                    _aim = V;
                    _aimId = VId;
                    SetVictim(null);
                }
                Face(Aim);
            }

            public void Build()
            {
                int level = Level;
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Line | Form.Overhead;
                Crouch(0.08f, 0.6f);
                // the blade rises; the moon forms above it
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = ChargeTime(level),
                    To = new Shape(-14f, 0.9f, 1.16f, 0f, -0.04f),
                    Ease = Ease.OutCubic,
                    Charging = true,
                    Track = false,
                    Forms = show | Form.Charge,
                    Enter = mm => ((Moon)mm).EnterRaise(),
                    Every = (mm, k) => ((Moon)mm).TickRaise(k)
                });
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.05f,
                    Tremble = true,
                    Charging = true,
                    Track = false,
                    Forms = show | Form.Charge,
                    Every = (mm, k) => ((Moon)mm).TickRaise(1f)
                });
                // the cut comes down
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.08f,
                    To = new Shape(36f, 1.16f, 0.84f, 0f, 0.12f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = show | Form.Heavy,
                    Enter = mm => ((Moon)mm).EnterCut(),
                    Every = (mm, k) => ((Moon)mm).TickCut(k)
                });
                At(0.5f, mm => ((Moon)mm).Split());
                // held: the blade buried at the end of its arc while the split races away
                Add(new Beat { Tempo = Tempo.Impact, Dur = 0.18f, To = new Shape(30f, 1.1f, 0.88f, 0f, 0.1f), Ease = Ease.OutQuad, Track = false, Forms = show });
                Recover(0.32f);
            }

            private Vector2 MoonSpot()
            {
                return HeadPos() + new Vector2(-Fwd * 1.5f, 4f + MoonR() * 0.7f) * Px;
            }

            private float MoonR()
            {
                return (Level <= 1 ? 7f : Level == 2 ? 8f : 9.5f) * Mathf.Lerp(1f, Kk, 0.6f);
            }

            /// <summary>The moon's turn: a fat crescent bulging back over its shoulder, its horns forward.</summary>
            private float MoonRot => Fwd > 0f ? 165f : 15f;

            private void EnterRaise()
            {
                Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                int r = Mathf.Clamp(Mathf.RoundToInt(MoonR()), 3, 40);
                _moonAt = MoonSpot();
                _halo = Glue(Fx.Spawn(Art.Disk(Mathf.RoundToInt(r * 1.25f), 1f, 0.5f), _moonAt, Px, 1f, FxLayer.Over, 7));
                _moon = Glue(Fx.Spawn(ChoreoArt.Crescent(r, Mathf.Max(2, r / 2)), _moonAt, Px, 1f, FxLayer.Over, 9));
                if (Vis)
                {
                    Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 18f, 0.5f, Kk);
                }
                TickRaise(0f);   // (sized and coloured before its first frame is drawn)
            }

            private void TickRaise(float k)
            {
                _moonAt = MoonSpot();
                float grow = Mathf.Lerp(0.2f, 1f, Beat.Apply(Ease.OutQuad, k));
                if (_moon != null && _moon.Alive)
                {
                    _moon.Pos = _moonAt;
                    _moon.Rot = MoonRot;
                    _moon.Size0 = _moon.Size1 = grow;
                    _moon.Grad = null;
                    _moon.R.color = Color.Lerp(Fx.WithA(Hue.Edge, 0.5f), Hue.Core, k);
                    _moon.Cine = Cinematic;
                }
                if (_halo != null && _halo.Alive)
                {
                    _halo.Pos = _moonAt;
                    _halo.Size0 = _halo.Size1 = grow * (1f + 0.06f * Mathf.Sin(BeatT * 23f));
                    _halo.Grad = null;
                    _halo.R.color = Fx.WithA(Hue.Main, 0.12f + 0.2f * k);
                }
                // the raised blade: from its hand straight up into the moon
                Vector2 hand = BodyPos() + new Vector2(-Fwd * 0.5f, Hpx * 0.3f) * Px;
                TbHiArt.Blade(this, hand, _moonAt - hand, Mathf.Min((_moonAt - hand).magnitude / Px, Hpx * 0.9f), _blade);
                if (k > 0.8f)
                {
                    Pose.Nudge = Choreo.Tremble(BeatT, k > 0.93f ? 0.8f : 0.5f);
                }
                if (!Vis)
                {
                    return;
                }
                if (Random.value < 0.45f)
                {
                    ChoreoFx.Motes(_moonAt, Px, Level >= 3 ? 2 : 1, 10f + MoonR() * 1.6f, 0.14f, Hue.Core, Hue.Edge, TopLayer);
                }
                if (Random.value < 0.08f)
                {
                    Kit.DustRing(Pose.Ground, Px, A.current_tile, 4, -14f, 0.35f, Kk);
                }
                if (Level >= 2 && k > 0.4f && Random.value < 0.06f)
                {
                    Ruin.Quake(A, Pose.Ground, Scale.Casual, 0.6f + 0.6f * k, Kk);
                }
                if (k >= 0.88f && !_glinted)
                {
                    _glinted = true;
                    Fx.Glint(_moonAt + new Vector2(0f, MoonR() * Px), Px, 0.16f, Color.white, 1f);
                }
            }

            private Vector2 _cutFrom;
            private static readonly List<Actor> _scan = new List<Actor>();

            private Vector2 BestLine(Vector2 aim, float len)
            {
                Vector2 from = Stance;
                Choreo.Foes(A, from + aim * len * 0.5f, len * 0.6f, _scan, false, true);
                Vector2 best = aim;
                float bestN = OnLine(from, aim, len) + 0.01f;
                for (int i = 0; i < _scan.Count; i++)
                {
                    Actor u = _scan[i];
                    Vector2 d = u.current_position - from;
                    if (d.sqrMagnitude < 0.25f || Vector2.Angle(d, aim) > 30f)
                    {
                        continue;
                    }
                    Vector2 dir = d.normalized;
                    // (its own foe, still in the cone, weighs a little more)
                    float n = OnLine(from, dir, len) + (u == _aim && u.Sid() == _aimId ? 0.5f : 0f);
                    if (n > bestN)
                    {
                        bestN = n;
                        best = dir;
                    }
                }
                return best;
            }

            private int OnLine(Vector2 from, Vector2 dir, float len)
            {
                int n = 0;
                for (int i = 0; i < _scan.Count; i++)
                {
                    Actor u = _scan[i];
                    Vector2 rel = u.current_position - from;
                    float along = Vector2.Dot(rel, dir);
                    if (along < 0.2f || along > len)
                    {
                        continue;
                    }
                    if (Mathf.Abs(rel.x * dir.y - rel.y * dir.x) <= 0.7f + u.stats["size"] * 0.5f)
                    {
                        n++;
                    }
                }
                return n;
            }

            private void EnterCut()
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                _cutFrom = _moonAt;
                if (!Vis)
                {
                    return;
                }
                // the great vertical arc of the blade coming down
                int r = Mathf.Clamp(Mathf.RoundToInt(13f * Mathf.Lerp(1f, Kk, 0.6f) * (Level >= 3 ? 1.15f : 1f)), 6, 60);
                Kit.Swish(this, Art.Arc(r, 1f, 100f, -75f, Level >= 3 ? 4 : 3), BeatDur * 2f + 0.06f, new Vector2(2f * Fwd, 2f), Fwd, Hue.Core, Hue.Edge, 7);
                Fx.Ghost(this, -Dir * 2f * Px, -10f, 0.08f, Fx.WithA(Hue.Ghost, 0.6f), Hue.GhostEnd);
            }

            private void TickCut(float k)
            {
                // the moon falls with the blade into the ground in front of it
                Vector2 to = Pose.Ground + Dir * 0.7f * Kk;
                Vector2 at = Vector2.Lerp(_cutFrom, to, Beat.Apply(Ease.InQuad, Mathf.Clamp01(k * 2f)));
                if (_moon != null && _moon.Alive)
                {
                    _moon.Pos = at;
                    _moon.Rot = MoonRot - 100f * Fwd * k;
                }
                if (_halo != null && _halo.Alive)
                {
                    _halo.Pos = at;
                }
            }

            /// <summary>The blade strikes the ground: the split races away along the line.</summary>
            private void Split()
            {
                int level = Level;
                // the foes moved while it gathered: the cut comes down along the line that holds the
                // most of them now, no more than 30 degrees off where it faced
                float len = Length(level) * Mathf.Lerp(1f, Kk, 0.5f);
                Vector2 dir = BestLine(Aim, len);
                Face(dir);
                Vector2 from = Stance + dir * 0.55f * Kk;
                if (_moon != null)
                {
                    Unglue(_moon);
                    _moon = null;
                }
                if (_halo != null)
                {
                    Unglue(_halo);
                    _halo = null;
                }
                Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile);
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitStone", A.current_tile, 0.05f);
                if (Vis)
                {
                    Kit.Flash(from, Px, Mathf.RoundToInt(7f * Kk), 0.08f, Color.white, TopLayer);
                    if (level >= 2)
                    {
                        Kit.ImpactFrame(from + new Vector2(0f, 0.3f * Kk), Px, (0.4f + 0.1f * level) * Kk, Hue.Pal, false, FxLayer.Behind);
                    }
                    Kit.Crater(from, Px * Kk, Combat.TileAt(from), level >= 3, 4f, true, Hue.Edge);
                    Kit.Debris(from, Px, Combat.TileAt(from), 6, Kk);
                    Kit.DustRing(from, Px, Combat.TileAt(from), 10, 40f, 0.5f, Kk);
                }
                Kit.Stop(from, 0.05f * ImpactK);
                Ruin.Quake(A, from, Scale, 1.2f, Kk);
                Areas.Add(new Fissure(this, from, dir, len, Mult(level), 0.55f + 0.1f * level, level));
            }
        }

        /// <summary>
        /// The split: its head races out along the line, the ground opening behind it in a glowing
        /// fissure; everyone it passes is thrown to the side it stood on.
        /// </summary>
        private sealed class Fissure : Hazard
        {
            private readonly Vector2 _from, _dir, _side;
            private readonly float _len, _mult, _width, _speed;
            private readonly int _level;
            private float _head, _done, _flareT;

            public Fissure(ChoreoMove m, Vector2 from, Vector2 dir, float len, float mult, float width, int level) : base(m)
            {
                _from = from;
                _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
                _side = Choreo.Perp(_dir);
                _len = len;
                _mult = mult;
                _width = width;
                _level = level;
                _speed = (34f + 6f * level) * Mathf.Lerp(1f, K, 0.4f);
            }

            public override bool Tick(float dt)
            {
                if (dt <= 0f)
                {
                    return true;
                }
                Age += dt;
                float was = _head;
                _head = Mathf.Min(_len, _head + _speed * dt);
                // the ground opens behind the head, a segment at a time
                const float seg = 1.2f;
                while (_done + 0.01f < _head && (_head - _done >= seg || _head >= _len))
                {
                    float b = Mathf.Min(_len, _done + seg);
                    Open(_from + _dir * _done, _from + _dir * b);
                    _done = b;
                }
                // whatever it passes is thrown aside
                if (OwnerOk && _head > was)
                {
                    Vector2 a = _from + _dir * Mathf.Max(0f, was - 0.3f);
                    Vector2 bb = _from + _dir * _head;
                    Choreo.FoesOnLine(A, a, bb, _width, Near, false, true);
                    for (int i = 0; i < Near.Count; i++)
                    {
                        Actor u = Near[i];
                        Vector2 rel = u.current_position - _from;
                        float s = Vector2.Dot(rel, _side);
                        float sign = Mathf.Abs(s) < 0.05f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(s);
                        Vector2 on = _from + _dir * Vector2.Dot(rel, _dir);
                        Strike(u, _mult, Sakuga.Hurt.Launch, on - _side * sign * 0.3f, 1.2f + 0.35f * _level, 0.9f + 0.25f * _level, 1.2f + 0.15f * _level);
                    }
                }
                Head();
                return _done < _len - 0.01f;
            }

            /// <summary>The head of the split: light flaring up out of the ground as it runs.</summary>
            private void Head()
            {
                Vector2 h = _from + _dir * _head;
                if (!Kit.Visible(h, 0.3f))
                {
                    return;
                }
                _flareT -= Clock.Dt;
                if (_flareT > 0f)
                {
                    return;
                }
                _flareT = 0.03f;
                WorldTile t = Combat.TileAt(h);
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    return;
                }
                Kit.Line(h, h + new Vector2(0f, (0.9f + 0.3f * _level) * K), Px, 0.1f, _level >= 3 ? 2 : 1, FxLayer.Top, 12, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(h + new Vector2(0f, 0.2f * K), Px, 2, 40f, 90f, Hue.Edge);
            }

            /// <summary>One length of the fissure: torn into the ground, glowing and cooling, dust bursting up on both banks.</summary>
            private void Open(Vector2 a, Vector2 b)
            {
                if (OwnerOk)
                {
                    Ruin.Furrow(A, a, b, _width, Scale, A.current_position, false);
                }
                Vector2 mid = (a + b) * 0.5f;
                if (!Kit.Visible(mid, 0.3f))
                {
                    return;
                }
                WorldTile t = Combat.TileAt(mid);
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    return;
                }
                Color dark = Fx.WithA(Fx.Darken(Fx.SoilColor(t), 0.6f), 0.95f);
                Particle crack = Kit.Line(a, b, Px, 2.6f, _level >= 3 ? 4 : 3, FxLayer.Ground, 4, dark, dark, Fx.WithA(dark, 0f));
                if (crack != null)
                {
                    crack.Snap = true;
                }
                Kit.Line(a, b, Px, 1.1f + 0.3f * _level, _level >= 3 ? 2 : 1, FxLayer.Ground, 5, Color.white, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                // walls of dust thrown up on both banks
                ChoreoFx.HeelDust(mid + _side * 0.35f * K, _side, Px, t, K);
                ChoreoFx.HeelDust(mid - _side * 0.35f * K, -_side, Px, t, K);
                if (_level >= 2)
                {
                    Kit.Debris(mid, Px, t, _level >= 3 ? 3 : 2, K);
                }
            }
        }
    }
}
