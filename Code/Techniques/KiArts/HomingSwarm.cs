using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HOMING SWARM (Ki Arts, Expert). One hand raised high: orbs of gold light blink into being one by
    /// one and wheel in a halo over its head; the arm sweeps forward and the swarm is loosed. Each orb
    /// curves away first (out and up, a comet tail of motes behind it), weaves, then turns hard onto
    /// a foe of its own: the swarm spreads over different foes (flyers included), and an orb whose
    /// foe falls on the way seeks another. I: 5 orbs; II: 7; III: 9, each bursting in a small ring
    /// that rocks those beside it. Range 10 / 11 / 12 tiles. Damage x0.55 per orb (III x0.6).
    /// </summary>
    internal sealed class HomingSwarmTech : Technique
    {
        public override string Id => "homing_swarm";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 24f, 40f, 62f };
        public override float[] Cooldown => new[] { 10f, 11f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Launched;
        }

        private static int Orbs(int level)
        {
            return level <= 1 ? 5 : level == 2 ? 7 : 9;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 11f : 12f;
        }

        private static float Each(int level)
        {
            return level >= 3 ? 0.6f : 0.55f;
        }

        private static readonly List<Actor> _cand = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float range = Range(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int orbs = Orbs(level);
            _cand.Clear();
            bool worthy = false;
            for (int i = 0; i < c.Enemies.Count && _cand.Count < orbs; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out _, c.Forced))
                {
                    continue;
                }
                _cand.Add(v);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || !Combat.Grounded(v))
                {
                    worthy = true;
                }
            }
            int n = _cand.Count;
            if (n == 0 || !c.Forced && n < 2 && !worthy)
            {
                return null;
            }
            float score = 0f, top = -1f;
            Actor lead = null;
            for (int i = 0; i < n; i++)
            {
                Actor v = _cand[i];
                int on = orbs / n + (i < orbs % n ? 1 : 0);
                c.Allowed(v, out float cm);
                float s = c.Value(v, c.Expected(v, on * Each(level)), cm);
                if (!Combat.Grounded(v))
                {
                    s += 8f;   // it follows flyers up into the sky
                }
                score += s;
                if (s > top)
                {
                    top = s;
                    lead = v;
                }
            }
            if (c.Linked && c.LinkTarget != null && _cand.Contains(c.LinkTarget))
            {
                lead = c.LinkTarget;
                score += 8f;
            }
            if (c.ForceTarget != null && _cand.Contains(c.ForceTarget))
            {
                lead = c.ForceTarget;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = lead.current_position - c.Pos;
            return new Plan
            {
                Target = lead,
                Point = lead.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = score,
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
            Swarm sw = new Swarm(Orbs(level));
            m.Face(p.Dir);
            // RAISE: one hand high; the orbs blink into being one by one and wheel in a halo over its head
            m.PoseTo(0.34f, new Shape(-6f, 0.92f, 1.1f, 0f, -0.02f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm => Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.15f))
                .Every((mm, k) => sw.Gather(mm, k));
            // SWEEP: the arm sweeps forward and the swarm is loosed
            m.PoseTo(0.07f, new Shape(14f, 1.16f, 0.9f, 0f, 0.07f), Ease.OutExpo, Tempo.Act).Shows(Forms)
                .OnEnter(mm => sw.Loose(mm));
            m.Hold(0.12f, false, Tempo.Follow).Shows(Forms);
            m.Recover(0.25f).OnEnter(mm => sw.Out(mm));
            return m.Start();
        }

        /// <summary>The halo of orbs over its head, and their release.</summary>
        private sealed class Swarm
        {
            private readonly Particle[] _orbs;
            private readonly Particle[] _glows;
            private float _turn;
            private int _made;

            public Swarm(int n)
            {
                _orbs = new Particle[n];
                _glows = new Particle[n];
            }

            private static Vector2 Center(ChoreoMove m)
            {
                return m.BodyPos() + new Vector2(m.Fwd * m.Hpx * 0.06f, m.Hpx * 0.95f) * m.Px;
            }

            private Vector2 Slot(ChoreoMove m, int i)
            {
                float a = (_turn + 360f * i / _orbs.Length) * Mathf.Deg2Rad;
                return Center(m) + new Vector2(Mathf.Cos(a) * m.Hpx * 0.95f, Mathf.Sin(a) * m.Hpx * 0.32f) * m.Px;
            }

            public void Gather(ChoreoMove m, float k)
            {
                int n = _orbs.Length;
                _turn += Clock.Dt * Mathf.Lerp(160f, 320f, k);
                // one more blinks into being every so often
                while (_made < n && k >= _made / (float)n * 0.8f)
                {
                    int i = _made++;
                    Vector2 at = Slot(m, i);
                    int r = Mathf.Max(1, Mathf.RoundToInt((1.1f + 0.15f * m.Level) * m.Kk));
                    _glows[i] = m.Glue(Fx.Spawn(Art.Disk(r + 1, 1f, 0.6f), at, m.Px, 1f, FxLayer.Over, 9));
                    _orbs[i] = m.Glue(Fx.Spawn(Art.Disk(r, 1f, 0.15f), at, m.Px, 1f, FxLayer.Over, 10));
                    if (m.Vis)
                    {
                        Kit.Flash(at, m.Px, 2, 0.05f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer, 22);
                        Fx.Glint(at, m.Px, 0.12f, m.Hue.Core, 0.6f);
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.06f);
                }
                for (int i = 0; i < _made; i++)
                {
                    Vector2 at = Slot(m, i);
                    float pulse = 1f + 0.15f * Mathf.Sin(m.T * 30f + i);
                    // (the ones behind the head are dimmer: the halo turns in depth)
                    float front = 0.75f + 0.25f * Mathf.Sin((_turn + 360f * i / n) * Mathf.Deg2Rad);
                    Set(_glows[i], at, pulse, Fx.WithA(m.Hue.Main, 0.7f * front));
                    Set(_orbs[i], at, 0.8f + 0.2f * pulse, Fx.WithA(Color.Lerp(m.Hue.Core, Color.white, 0.4f), front));
                }
                if (m.Vis && Random.value < 0.4f)
                {
                    ChoreoFx.Motes(Center(m), m.Px, 1, 10f, 0.1f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
                }
            }

            private static void Set(Particle p, Vector2 at, float size, Color c)
            {
                if (p == null || !p.Alive)
                {
                    return;
                }
                p.Pos = at;
                p.Size0 = p.Size1 = size;
                p.Grad = null;
                p.R.color = c;
            }

            private static readonly List<Actor> _scan = new List<Actor>();

            /// <summary>The arm sweeps: each orb flies out of the halo, swinging wide, then homes on a foe of its own.</summary>
            public void Loose(ChoreoMove m)
            {
                int level = m.Level;
                float range = Range(level) * Mathf.Lerp(1f, m.Kk, 0.5f);
                // its foes now: the main one first, then the nearest
                List<Actor> pool = new List<Actor>(8);
                if (m.VOk)
                {
                    pool.Add(m.V);
                }
                Choreo.Foes(m.A, m.Stance, range, _scan, true);
                Vector2 from0 = m.Stance;
                _scan.Sort((x, y) => (x.current_position - from0).sqrMagnitude.CompareTo((y.current_position - from0).sqrMagnitude));
                for (int i = 0; i < _scan.Count && pool.Count < _orbs.Length; i++)
                {
                    Actor v = _scan[i];
                    if (!pool.Contains(v) && Choreo.Fair(m.A, v, out _, true))
                    {
                        pool.Add(v);
                    }
                }
                Vector2 c = Center(m);
                for (int i = 0; i < _made; i++)
                {
                    Vector2 at = Slot(m, i);
                    Actor v = pool.Count > 0 ? pool[i % pool.Count] : null;
                    Vector2 outward = at - c;
                    outward = (outward.sqrMagnitude > 1e-6f ? outward.normalized : new Vector2(m.Fwd, 0f)) + Vector2.up * 0.6f + m.Dir * 0.3f;
                    ShotSpec s = new ShotSpec
                    {
                        Kind = ShotKind.Orb,
                        Flight = Flight.Homing,
                        Size = 0.85f + 0.1f * level,
                        Speed = 10f + level,
                        Turn = 6f + level,
                        Mult = Each(level),
                        Hurt = Hurt.Reel,
                        Push = 0.6f,
                        Range = (v != null ? Vector2.Distance(m.Stance, v.current_position) : range) * 2.5f + 6f,
                        Area = level >= 3 ? 0.7f : 0f,
                        AreaMult = 0.5f
                    };
                    Shot shot = KiFlight.Fire(m, at, outward, v, s, KiFlight.Style.Swarm, default, pool, m.Tier >= Tier.Master ? 0.3f : 0f);
                    if (shot != null)
                    {
                        shot.Dir = outward.normalized;   // (it swings out first; its turn ramps up onto the foe)
                    }
                }
                Out(m);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile, 0.2f);
                if (m.Vis)
                {
                    Kit.Flash(c, m.Px, Mathf.RoundToInt(4f * m.Kk), 0.07f, Fx.WithA(m.Hue.Core, 1f), m.TopLayer, 24);
                    Fx.Ring(c, m.Px, 0.22f, 3f, m.Hpx * 0.8f, 2, 1, m.TopLayer, 22, 0.45f, 2.2f, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.5f), Fx.WithA(m.Hue.Rim, 0f));
                    Kit.Sparks(c, m.Px, 5, 40f, 100f, m.Hue.Edge, m.TopLayer);
                }
            }

            public void Out(ChoreoMove m)
            {
                for (int i = 0; i < _orbs.Length; i++)
                {
                    m.Unglue(_orbs[i]);
                    m.Unglue(_glows[i]);
                    _orbs[i] = _glows[i] = null;
                }
            }
        }
    }
}
