using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI VOLLEY (Ki Arts, Adept). Feet planted, both palms drawn back with light kindling in them,
    /// then the palms thrust out in turn, faster and faster, a blur of arms: a stream of small gold
    /// orbs spread over every foe in front of it (a 64° fan, 9 / 10 / 11 tiles), each one rocking
    /// what it strikes, the near misses bursting on the ground in puffs of dust and light. I: 6
    /// shots; II: 10; III: 14, then both palms together throw one heavy orb that bursts on the main
    /// target, knocking down those around it and breaking the ground. A shower, not a sniper's shot:
    /// best on a crowd. Damage x0.3 per orb that lands (the heavy one x0.9, x0.45 on those near it).
    /// </summary>
    internal sealed class KiVolleyTech : Technique
    {
        public override string Id => "ki_volley";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 28f, 46f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Thrown;
        }

        private const float Each = 0.3f;
        private const float Half = 32f;

        private static int ShotsOf(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 10 : 14;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 10f : 11f;
        }

        private static float Dur(int level)
        {
            return level <= 1 ? 0.5f : level == 2 ? 0.7f : 0.85f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float range = Range(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float cos = Mathf.Cos(Half * Mathf.Deg2Rad);
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 10; i++)
            {
                float d = c.EnemyDist[i];
                if (d > range)
                {
                    break;
                }
                Actor aim = c.Enemies[i];
                if (d < 1.2f || c.ForceTarget != null && aim != c.ForceTarget || !Choreo.Fair(c.Self, aim, out _, c.Forced))
                {
                    continue;
                }
                Vector2 dir = (aim.current_position - c.Pos) / d;
                // the fan: everyone in it shares the shots
                int n = 0;
                bool worthy = false;
                for (int j = 0; j < c.Enemies.Count && j < 20; j++)
                {
                    float dj = c.EnemyDist[j];
                    if (dj > range)
                    {
                        break;
                    }
                    Actor v = c.Enemies[j];
                    if (dj < 0.5f || Vector2.Dot((v.current_position - c.Pos) / dj, dir) < cos || Moves.Locked(v) || !c.Allowed(v))
                    {
                        continue;
                    }
                    n++;
                    if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute)
                    {
                        worthy = true;
                    }
                }
                n = Mathf.Max(1, n);
                if (!c.Forced && n < 2 && !(worthy && d >= 2f))
                {
                    continue;   // a single plain foot soldier is no target for a shower of orbs
                }
                float share = ShotsOf(level) * 0.75f / n * Each;
                float score = 0f;
                for (int j = 0; j < c.Enemies.Count && j < 20; j++)
                {
                    float dj = c.EnemyDist[j];
                    if (dj > range)
                    {
                        break;
                    }
                    Actor v = c.Enemies[j];
                    if (dj < 0.5f || Vector2.Dot((v.current_position - c.Pos) / dj, dir) < cos || Moves.Locked(v) || !c.Allowed(v, out float cm))
                    {
                        continue;
                    }
                    score += c.Value(v, c.Expected(v, share + (level >= 3 && v == aim ? 0.9f : 0f)), cm);
                }
                if (c.Linked && aim == c.LinkTarget)
                {
                    score += 8f;
                }
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = aim, Point = aim.current_position, Dir = dir, Count = n, Score = score, Data = c.Forced };
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Barrage b = new Barrage(ShotsOf(level));
            m.Face(p.Dir);
            // PLANT: feet set, both palms drawn back, light kindling in them
            m.PoseTo(0.12f, new Shape(-8f, 1.1f, 0.9f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm => b.Kindle(mm))
                .Every((mm, k) => b.Glow(mm, k));
            // BARRAGE: the palms thrust out in turn, faster and faster, orbs streaming over the fan
            Beat barrage = new Beat
            {
                Tempo = Tempo.Act,
                Dur = Dur(level),
                To = new Shape(8f, 1.08f, 0.94f, 0f, 0.06f),
                Ease = Ease.OutQuad,
                Forms = Forms
            };
            barrage.Enter = mm => b.Gather(mm);
            barrage.Every = (mm, k) => b.Fire(mm, k);
            m.Add(barrage);
            if (level >= 3)
            {
                // both palms drawn back together...
                m.PoseTo(0.08f, new Shape(-8f, 0.94f, 1.06f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                    .Every((mm, k) => b.Glow(mm, 0.5f + 0.5f * k));
                // ...and the heavy orb is thrown
                m.PoseTo(0.06f, new Shape(14f, 1.22f, 0.88f, 0f, 0.12f), Ease.OutExpo, Tempo.Act).Shows(Forms)
                    .OnEnter(mm => b.Final(mm));
            }
            // smoke curling off the palms
            m.Hold(0.1f, false, Tempo.Follow).Shows(Forms).OnEnter(mm =>
            {
                b.Out(mm);
                if (mm.Vis)
                {
                    ChoreoFx.Smoke(mm.Hand(Limb.Palm), mm.Px, 2, 1.5f, Fx.WithA(mm.Hue.Edge, 0.45f), 0.5f);
                }
            });
            m.Recover(0.22f).OnEnter(mm => b.Out(mm));
            return m.Start();
        }

        /// <summary>The volley as it plays: its targets, the shots fired so far, the palms' glow, the body's jolts.</summary>
        private sealed class Barrage
        {
            private readonly int _n;
            private int _i, _pick;
            private readonly List<Actor> _foes = new List<Actor>(8);
            private Particle _l, _r;
            private float _kickT, _kRot, _kSX, _kSY;

            private static readonly List<Actor> _scan = new List<Actor>();

            public Barrage(int n)
            {
                _n = n;
            }

            private static Vector2 PalmOf(ChoreoMove m, int side)
            {
                return m.Hand(Limb.Palm) + m.Side * side * 1.5f * m.Px;
            }

            public void Kindle(ChoreoMove m)
            {
                Sprite s = Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.4f * m.Kk)), 1f, 0.4f);
                _l = m.Glue(Fx.Spawn(s, m.BodyPos(), m.Px, 1f, FxLayer.Over, 9));
                _r = m.Glue(Fx.Spawn(s, m.BodyPos(), m.Px, 1f, FxLayer.Over, 9));
                Choreo.Sound("event:/SFX/POWERS/Powerup", m.A.current_tile, 0.15f);
            }

            public void Glow(ChoreoMove m, float k)
            {
                Vector2 back = m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.14f, -m.Hpx * 0.04f) * m.Px;
                Place(_l, back + m.Side * 1.2f * m.Px, m, k);
                Place(_r, back - m.Side * 1.2f * m.Px, m, k);
            }

            private static void Place(Particle p, Vector2 at, ChoreoMove m, float k)
            {
                if (p == null || !p.Alive)
                {
                    return;
                }
                p.Pos = at;
                p.Grad = null;
                p.Size0 = p.Size1 = Mathf.Lerp(0.5f, 1.2f, k) * (1f + 0.15f * Mathf.Sin(m.T * 60f));
                p.R.color = Fx.WithA(Color.Lerp(m.Hue.Edge, m.Hue.Core, k), 0.9f);
            }

            public void Out(ChoreoMove m)
            {
                m.Unglue(_l);
                m.Unglue(_r);
                _l = _r = null;
            }

            /// <summary>Everyone in the fan right now, nearest first.</summary>
            public void Gather(ChoreoMove m)
            {
                Out(m);
                _foes.Clear();
                float range = Range(m.Level) * Mathf.Lerp(1f, m.Kk, 0.5f);
                float cos = Mathf.Cos((Half + 4f) * Mathf.Deg2Rad);
                Vector2 dir = m.Aim;
                Choreo.Foes(m.A, m.Stance + dir * range * 0.5f, range * 0.5f + 1f, _scan, true);
                for (int i = 0; i < _scan.Count; i++)
                {
                    Actor v = _scan[i];
                    Vector2 rel = v.current_position - m.Stance;
                    float d = rel.magnitude;
                    if (d < 0.3f || d > range || Vector2.Dot(rel / d, dir) < cos || !Choreo.Fair(m.A, v, out _, true))
                    {
                        continue;
                    }
                    _foes.Add(v);
                }
                Vector2 from = m.Stance;
                _foes.Sort((x, y) => (x.current_position - from).sqrMagnitude.CompareTo((y.current_position - from).sqrMagnitude));
                // the main target leads the list
                if (m.V != null && m.VOk && _foes.Remove(m.V))
                {
                    _foes.Insert(0, m.V);
                }
            }

            /// <summary>The next foe in turn that is still there (null: none left).</summary>
            private Actor Next(ChoreoMove m)
            {
                for (int t = 0; t < _foes.Count; t++)
                {
                    Actor v = _foes[_pick++ % _foes.Count];
                    if (Choreo.Fair(m.A, v, out _, true))
                    {
                        return v;
                    }
                }
                return null;
            }

            public void Fire(ChoreoMove m, float k)
            {
                _kickT -= Clock.Dt;
                if (_kickT > 0f)
                {
                    m.Pose.Rot = _kRot;
                    m.Pose.SX = _kSX;
                    m.Pose.SY = _kSY;
                }
                // shots come faster and faster: the i-th at (i/n)^0.85 of the beat
                while (_i < _n && k >= Mathf.Pow(_i / (float)_n, 0.85f))
                {
                    Shoot(m, _i++);
                }
            }

            private void Shoot(ChoreoMove m, int i)
            {
                int side = (i & 1) == 0 ? 1 : -1;
                Vector2 from = PalmOf(m, side);
                Actor v = Next(m);
                // a quarter of them land at the foe's feet (near misses: dust and light), the rest go for the body
                bool miss = i % 4 == 3 || v == null;
                ShotSpec s = new ShotSpec
                {
                    Kind = ShotKind.Orb,
                    Size = 0.6f + 0.05f * m.Level,
                    Mult = Each,
                    Hurt = Hurt.SnapBack,
                    Push = 0.2f,
                    Speed = 20f + 2f * m.Level
                };
                Vector2 dir;
                if (miss)
                {
                    Vector2 goal = v != null
                        ? (Vector2)v.current_position + Random.insideUnitCircle * 0.9f
                        : m.Stance + Choreo.Rotate(m.Aim, Random.Range(-Half, Half)) * Random.Range(3f, Range(m.Level));
                    goal = Combat.Clamp(goal);
                    s.Flight = Flight.Lob;
                    s.Apex = 0.5f;
                    s.Speed = 16f;
                    s.Area = m.Level >= 3 ? 0.5f : 0f;
                    s.AreaMult = 0.5f;
                    dir = goal - from;
                    KiFlight.Fire(m, from, dir, null, s, KiFlight.Style.Volley, goal, null, m.Tier >= Tier.Master ? 0.25f : 0f);
                }
                else
                {
                    s.Range = Vector2.Distance(m.Stance, v.current_position) + 3f;
                    dir = Choreo.BodyAt(v, 0.45f) - from;
                    KiFlight.Fire(m, from, dir, v, s, KiFlight.Style.Volley);
                }
                if (v != null)
                {
                    m.Face(v.current_position - m.Stance);
                }
                // the body jolts with each thrust; the other arm a blur
                _kickT = 0.035f;
                _kRot = m.Pose.Rot = Random.Range(6f, 14f);
                _kSX = m.Pose.SX = Random.Range(1.08f, 1.16f);
                _kSY = m.Pose.SY = Random.Range(0.88f, 0.94f);
                if ((i & 1) == 0)
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.03f);
                }
                if (!m.Vis)
                {
                    return;
                }
                Kit.Flash(from, m.Px, Mathf.Max(2, Mathf.RoundToInt(2f * m.Kk)), 0.04f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer, 22);
                Fx.Ghost(m, m.Side * side * 2f * m.Px - m.Dir * m.Px, side * 8f, 0.07f, Fx.WithA(m.Hue.Ghost, 0.5f), m.Hue.GhostEnd);
                ChoreoFx.Smear(m.Root(Limb.Palm) + m.Side * side * m.Px, from + dir.normalized * 2f * m.Px, m.Px, 0.06f, 1, 1, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
                if (i % 3 == 0)
                {
                    Kit.Sparks(from, m.Px, 1, 40f, 70f, m.Hue.Edge, m.TopLayer);
                }
            }

            /// <summary>III: both palms together throw one heavy orb that bursts on the main target.</summary>
            public void Final(ChoreoMove m)
            {
                Actor v = m.VOk ? m.V : Next(m);
                Vector2 from = m.Hand(Limb.Palm) + new Vector2(m.Fwd * 1.5f * m.Px, 0f);
                ShotSpec s = new ShotSpec
                {
                    Kind = ShotKind.Orb,
                    Size = 1.6f,
                    Mult = 0.9f,
                    Hurt = Hurt.Reel,
                    Push = 1.2f,
                    Speed = 18f,
                    Area = 1.3f,
                    AreaMult = 0.5f,
                    Crater = 0.5f
                };
                Vector2 dir;
                if (v != null)
                {
                    s.Range = Vector2.Distance(m.Stance, v.current_position) + 3f;
                    dir = Choreo.BodyAt(v, 0.45f) - from;
                    m.Face(v.current_position - m.Stance);
                }
                else
                {
                    s.Range = Range(m.Level);
                    dir = m.Aim;
                }
                KiFlight.Fire(m, from, dir, v, s, KiFlight.Style.Volley);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile, 0.1f);
                if (!m.Vis)
                {
                    return;
                }
                Kit.Flash(from, m.Px, Mathf.RoundToInt(5f * m.Kk), 0.07f, Color.white, m.TopLayer, 24);
                Fx.Ring(from, m.Px, 0.2f, 2f, 10f * m.Kk, 2, 1, m.TopLayer, 22, 1f, 2.2f, Fx.WithA(m.Hue.Core, 1f), Fx.WithA(m.Hue.Edge, 0.5f), Fx.WithA(m.Hue.Rim, 0f));
                Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 8, 30f, 0.4f, m.Kk);
                Kit.SpeedLines(from, dir, m.Px, 3, 3f, 0.1f, Fx.WithA(m.Hue.Edge, 0.85f), m.TopLayer);
            }
        }
    }
}
