using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GALE LANCE (Tempest Leg, Master). The knee is drawn up to the chest while the wind spirals into
    /// the foot, then the leg snaps straight out in a side kick and a lance of wind leaves the sole: a
    /// thin white-cored streak wound round with spiralling strands of air, cuffs of wind ringing it as it
    /// goes. It runs straight down the line through everyone in it (each is driven back along it) and
    /// cuts a thin trench in the ground behind it, through whatever stands there (a Master's blow
    /// breaks every building it lands on). It is a real projectile: a barrier can send it back, a blade
    /// can cut it. I: 10 tiles, one strand; II: 12 tiles, two strands, a deeper cut; III: 14 tiles, three
    /// strands and a white-hot core, the foes it passes are carried off their feet, and it bursts at
    /// the end of its run. Damage (x the user's damage) to each it passes: I 1.8, II 2.4, III 3.2.
    /// </summary>
    internal sealed class GaleLanceTech : Technique
    {
        public override string Id => "gale_lance";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Line;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Projectile | Form.Line;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 32f, 52f };
        public override float[] Cooldown => new[] { 9f, 10f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Length(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 12f : 14f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.4f : 3.2f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickLine(c, this, level, Length(level), 0.5f, Mult(level), 1, true);
            if (p == null || p.Target == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                // a lance through a row of them is what it is for; a far archer or mage is a fine mark too
                p.Score *= p.Count >= 3 ? 1.25f : 1f;
                if (c.Dist(p.Target) > 3f && c.FoeRange(p.Target) > 2.5f)
                {
                    p.Score += 12f;
                }
            }
            return p;
        }

        private sealed class State
        {
            public float T;
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
            m.Face(p.Dir);

            // the chamber: the knee drawn up, the wind spiralling into the foot
            Beat chamber = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = level <= 1 ? 0.14f : level == 2 ? 0.16f : 0.18f,
                To = new Shape(-14f, 0.9f, 1.1f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Forms = Form.Kick | Form.Projectile
            };
            chamber.Every = (mm, kk) =>
            {
                st.T -= Clock.Dt;
                if (st.T > 0f || !mm.Vis)
                {
                    return;
                }
                st.T = 0.035f;
                Vector2 foot = mm.Hand(Limb.Foot) + new Vector2(0f, 3f * mm.Px);
                ChoreoFx.Motes(foot, mm.Px, 2 + mm.Level, 9f * mm.Kk, 0.12f, wind.Core, wind.Edge);
                if (kk > 0.4f)
                {
                    Fx.Ring(foot, mm.Px, 0.12f, 6f * mm.Kk, 2f, 1, 1, FxLayer.Over, 6, 0.5f, 1.5f, Fx.WithA(wind.Edge, 0f), Fx.WithA(wind.Core, 0.85f));
                }
            };
            chamber.On(0.75f, mm =>
            {
                if (mm.Vis && mm.Level >= 2)
                {
                    Fx.Glint(mm.Hand(Limb.Foot) + new Vector2(0f, 3f * mm.Px), mm.Px, 0.14f, wind.Core, 0.9f);
                }
            });
            m.Add(chamber);

            // the kick: the leg snaps straight out and the lance leaves the sole
            Beat kick = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(-26f, 1.34f, 0.82f, 0f, 0.2f),
                Ease = Ease.OutExpo,
                Forms = Form.Kick | Form.Projectile | Form.Line
            };
            kick.On(0.4f, mm => Loose(mm, wind));
            m.Add(kick);

            // the leg held out a beat (the kick reads), then down
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.16f, To = new Shape(-20f, 1.2f, 0.9f, 0f, 0.16f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The lance leaves the foot (a shot of the engine: answerable, reflectable, cuttable) with its wake.</summary>
        private static void Loose(ChoreoMove m, Hue wind)
        {
            int level = m.Level;
            Vector2 foot = m.Hand(Limb.Foot);
            Actor target = m.V != null && m.VOk ? m.V : null;
            Vector2 aim = target != null ? m.VPoint(0.45f) - foot : m.Aim;
            ShotSpec spec = new ShotSpec
            {
                Kind = ShotKind.Spear,
                Flight = Flight.Straight,
                Speed = level <= 1 ? 26f : level == 2 ? 29f : 33f,
                Range = Length(level) * Mathf.Lerp(1f, m.Kk, 0.5f),
                Size = level <= 1 ? 0.8f : level == 2 ? 1f : 1.2f,
                Mult = Mult(level),
                Hurt = level >= 3 ? Hurt.Launch : Hurt.Reel,
                Push = level <= 1 ? 1.4f : level == 2 ? 2f : 3f,
                Pierce = 99,
                Element = Element.Wind
            };
            Shot s = Shots.Fire(m.A, m, foot, aim, target, Vector2.zero, spec, wind);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                float px = m.Px;
                Vector2 d = aim.sqrMagnitude > 1e-6f ? aim.normalized : m.Dir;
                ChoreoFx.Smear(m.Root(Limb.Foot), foot + d * 3f * px, px, 0.1f, 2, 2, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 8);
                Kit.Flash(foot, px, Mathf.Max(2, Mathf.RoundToInt(3f * m.Kk)), 0.07f, Fx.WithA(wind.Core, 0.95f), m.TopLayer, 24);
                Particle cuff = Fx.Ring(foot, px, 0.18f, 2f, (4f + 1.5f * level) * m.Kk, 2, 1, FxLayer.Top, 7, 1.8f, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(wind.Main, 0.7f), Fx.WithA(wind.Edge, 0f));
                if (cuff != null)
                {
                    cuff.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                }
                Kit.SpeedLines(foot + d * 4f * px, d, px, 3, 3f, 0.14f, Fx.WithA(wind.Edge, 0.85f));
                ChoreoFx.HeelDust(m.Pose.Ground, -d, px, m.A.current_tile, m.Kk);
            }
            Ruin.Quake(m.A, foot, m.Scale, 0.8f, m.Kk);
            if (s != null)
            {
                Areas.Add(new GaleLanceWake(m, s, level));
            }
        }
    }

    /// <summary>
    /// What follows a Gale Lance in flight: the strands of air spiralling round it, cuffs of wind ringing
    /// it every few tiles, and the thin trench it cuts in the ground behind it (Ruin.Furrow, at the
    /// force the tier calls for). It stops with the lance: spent, cut, turned back or knocked away.
    /// </summary>
    internal sealed class GaleLanceWake : Hazard
    {
        private readonly Shot _s;
        private readonly long _owner;
        private readonly int _level, _strands;
        private readonly float _width, _ringEvery;
        private readonly Vector2[] _prev = new Vector2[3];
        private Vector2 _last, _cutFrom;
        private float _phase, _ringAcc, _cutAcc, _dustT;
        private bool _first = true;

        public GaleLanceWake(ChoreoMove m, Shot s, int level) : base(m)
        {
            _s = s;
            _owner = m.A.Sid();
            _level = level;
            _strands = Mathf.Clamp(level, 1, 3);
            _width = level <= 1 ? 0.45f : level == 2 ? 0.55f : 0.65f;
            _ringEvery = level <= 1 ? 2.6f : level == 2 ? 2f : 1.5f;
            _last = _cutFrom = s.Pos;
            Hue = m.Hue.As(Element.Wind);
        }

        /// <summary>Does the lance still fly as ours (not spent, cut, turned back or knocked away)?</summary>
        private bool Flying()
        {
            if (_s.Done || !_s.Hostile || _s.A == null || _s.A.Sid() != _owner || _s.Traveled >= _s.Spec.Range - 0.01f)
            {
                return false;
            }
            IReadOnlyList<Shot> all = Shots.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == _s)
                {
                    return true;
                }
            }
            return false;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            bool flying = Flying();
            Vector2 pos = flying ? _s.Pos : _last;
            float step = Vector2.Distance(_last, pos);
            _cutAcc += step;
            if (OwnerOk && (_cutAcc >= 1.2f || !flying && _cutAcc > 0.05f))
            {
                // the cut behind it (never under the kicker's own feet)
                Ruin.Furrow(A, _cutFrom, pos, _width, Scale, A.current_position, false);
                if (Kit.Visible(pos, 0.4f))
                {
                    Color soil = Fx.Darken(Fx.SoilColor(Combat.TileAt(pos)), 0.5f);
                    Kit.Line(_cutFrom, pos, Px, 2.5f, 1, FxLayer.Ground, 2, Fx.WithA(soil, 0.9f), Fx.WithA(soil, 0.8f), Fx.WithA(soil, 0f));
                    Kit.Line(_cutFrom, pos, Px, 0.5f, 1, FxLayer.Ground, 3, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                }
                _cutFrom = pos;
                _cutAcc = 0f;
            }
            if (!flying)
            {
                End(pos);
                return false;
            }
            Draw(pos, step, dt);
            _last = pos;
            return true;
        }

        private void Draw(Vector2 pos, float step, float dt)
        {
            Vector2 at = pos + new Vector2(0f, _s.Z);
            if (!Kit.Visible(at, 0.3f))
            {
                _first = true;
                return;
            }
            float px = Px;
            Vector2 dir = _s.Dir;
            Vector2 side = new Vector2(-dir.y, dir.x);
            // the strands wind round it (a helix seen from the side: they cross over and under)
            // the lance itself: a long thin streak with a white core
            float len = (7f + 3f * _level) * px * K;
            Kit.Line(at - dir * len, at, px, 0.05f, _level >= 3 ? 3 : 2, FxLayer.Top, 12, Fx.WithA(Hue.Main, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
            Kit.Line(at - dir * len * 0.7f, at + dir * px, px, 0.04f, 1, FxLayer.Top, 13, Color.white, Fx.WithA(Hue.Core, 0.8f));
            _phase += dt * (24f + 6f * _level);
            float r = (2.2f + 0.5f * _level) * px * Mathf.Lerp(1f, K, 0.5f);
            for (int j = 0; j < _strands; j++)
            {
                float a = _phase + j * Mathf.PI * 2f / _strands;
                Vector2 p = at + new Vector2(side.x * 0.4f, 1f) * Mathf.Sin(a) * r;
                bool front = Mathf.Cos(a) > 0f;
                if (!_first)
                {
                    Kit.Line(_prev[j], p, px, 0.12f, 1, front ? FxLayer.Top : FxLayer.Behind, front ? 13 : 2,
                        Fx.WithA(front ? Hue.Core : Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                }
                _prev[j] = p;
            }
            _first = false;
            // a cuff of wind every few tiles, a ring standing across its flight
            _ringAcc += step;
            if (_ringAcc >= _ringEvery)
            {
                _ringAcc = 0f;
                Particle ring = Fx.Ring(at, px, 0.26f, 2f, (4f + 1.2f * _level) * K, 1, 1, FxLayer.Top, 9, 2.2f, 1.8f,
                    Fx.WithA(Color.white, 0.85f), Fx.WithA(Hue.Main, 0.6f), Fx.WithA(Hue.Edge, 0f));
                if (ring != null)
                {
                    ring.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(pos), 0.12f);
            }
            _dustT -= dt;
            if (_dustT <= 0f)
            {
                _dustT = 0.05f;
                ChoreoFx.HeelDust(pos, -dir, px, Combat.TileAt(pos), K * 0.8f);
            }
        }

        /// <summary>The lance is spent: at III it bursts where it stops.</summary>
        private void End(Vector2 pos)
        {
            if (_level < 3 || !OwnerOk || _s.Done || !_s.Hostile || _s.A == null || _s.A.Sid() != _owner)
            {
                return;
            }
            WorldTile tile = Combat.TileAt(pos);
            if (Kit.Visible(pos, 0.4f))
            {
                Vector2 at = pos + new Vector2(0f, _s.Z);
                Kit.Flash(at, Px, Mathf.RoundToInt(5f * K), 0.08f, Fx.WithA(Hue.Core, 0.95f));
                Fx.Ring(pos, Px, 0.3f, 3f, 16f * K, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                Kit.DustRing(pos, Px, tile, 10, 40f, 0.5f, K);
                Kit.Debris(pos, Px, tile, 5, K);
            }
            Ruin.Blast(A, pos, 0.7f, Scale, A.current_position, false);
        }
    }
}
