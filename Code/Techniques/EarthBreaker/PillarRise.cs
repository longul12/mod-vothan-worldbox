using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// STONE PILLAR (Earth Breaker, Expert). It lifts a knee high and stomps: a glowing crack races
    /// away from its foot along the ground to the foe, the ground under the foe glows, and a stone
    /// pillar erupts under it and throws it high into the air (Launched: an air follow-up can take
    /// it). Fast and long-reaching: it breaks a charge from afar (Interrupt), and the pillar reaches a
    /// low flyer. II raises a taller pillar with two spikes flanking it that catch whoever stands
    /// beside the foe; III a towering column ringed by spikes erupting under everyone near it, one
    /// after another. It needs ground under the foe (never water). Range 6 / 7.5 / 9 tiles. Damage
    /// (x the user's damage): I 1.4, II 1.9 (+ spikes x0.8), III 2.6 (+ spikes x0.9).
    /// </summary>
    internal sealed class PillarRiseTech : Technique
    {
        public override string Id => "pillar_rise";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Launcher;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Rising | Form.Interrupt | Form.AntiAir;

        /// <summary>The interrupt: a wind-up anywhere in reach.</summary>
        public override Form Answers => Form.Charge;

        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 24f, 38f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Down || link == Link.Thrown || link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.6f;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7.5f : 9f;
        }

        private static float Height(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.2f : 3.1f;
        }

        private static float Lift(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.6f : 3.3f;
        }

        /// <summary>How fast the crack runs through the ground (tiles a second).</summary>
        private const float CrackSpeed = 26f;

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f);
            float top = Height(level) * k + 0.4f;
            float mult = Mult(level);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 12; i++)
            {
                Actor v = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > (c.Forced ? Mathf.Max(range * 1.5f, 10f) : range))
                {
                    break;
                }
                if (c.ForceTarget != null && v != c.ForceTarget)
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || !OverGround(v.current_position) || !c.Forced && MeteorSlam.AtDeathsDoor(v))
                {
                    continue;
                }
                // a low flyer is in reach of the column; anything higher is not
                if (Combat.HeightOf(v) > top)
                {
                    continue;
                }
                bool charging = Moves.Charging(v);
                bool linkTarget = c.Linked && v == c.LinkTarget;
                if (!c.Forced && !charging && !linkTarget && v.getHealth() <= c.Expected(v, 1.2f) * cm)
                {
                    continue;   // a pillar is not for what an ordinary blow ends
                }
                float score = c.Value(v, c.Expected(v, mult), cm);
                if (charging)
                {
                    score += c.D * 3f + 30f + 10f * Tiers.Power(v);   // the interrupt from afar
                }
                if (!Combat.Grounded(v))
                {
                    score += 12f;   // brought out of the air
                }
                if (d < 1.2f)
                {
                    score *= 0.7f;   // (point blank: a fist is quicker)
                }
                if (level >= 2)
                {
                    int near = c.CountNear(v.current_position, 1.2f);
                    score += Mathf.Max(0, near - 1) * c.D * 0.5f;
                }
                if (linkTarget)
                {
                    score += 8f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                Data = c.Forced
            };
        }

        /// <summary>Solid ground (no water, no lava) for a pillar to come out of.</summary>
        private static bool OverGround(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t != null && t.Type != null && !t.Type.liquid && !t.Type.lava && !t.Type.block;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Erupt erupt = new Erupt();
            // the knee comes up high (a quick hop of weight onto the standing leg)
            m.PoseTo(level >= 3 ? 0.13f : 0.11f, new Shape(-8f, 0.9f, 1.16f, 0.06f, -0.03f), Ease.OutCubic, Tempo.Wind);
            if (level >= 3)
            {
                // (III: the ground under it glows as it gathers the weight)
                m.Last.Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Kit.Flare(mm.Pose.Ground, mm.Px, Fx.WithA(mm.Hue.Edge, 0.9f), 10f * mm.Kk, 0.25f);
                    }
                };
            }
            // the stomp
            Beat stomp = new Beat { Tempo = Tempo.Act, Dur = 0.06f, To = new Shape(10f, 1.26f, 0.76f, 0f, 0.02f), Ease = Ease.OutExpo };
            stomp.On(0.5f, mm => Stomp(mm, erupt));
            m.Add(stomp);
            // it holds the stomp while the crack runs, then throws an open hand up as the stone comes
            m.Add(new Beat { Tempo = Tempo.Free, Dur = 0.08f, Until = mm => erupt.Risen(), MaxDur = 1.2f, To = new Shape(8f, 1.18f, 0.84f, 0f), Ease = Ease.OutQuad });
            Beat summon = new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(-14f, 0.86f, 1.18f, 0f), Ease = Ease.OutBack, Track = false };
            summon.Enter = mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(mm.Hand(Limb.Palm) + new Vector2(0f, 2f * mm.Px), mm.Px, 0.16f, mm.Hue.Core, 0.8f);
                }
                // (the foe stood on it: it is in the air now, for whoever follows up)
                PillarHazard main = erupt.Main;
                if (main != null && mm.VOk && Vector2.Distance(mm.VG, main.At) <= main.Stand + 0.35f)
                {
                    mm.SetLink(Link.Launched, mm.V);
                }
            };
            m.Add(summon);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.1f, To = new Shape(-6f, 0.94f, 1.06f, 0f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>What the stomp set off: the main pillar once the crack reaches the foe (the move waits for it to rise).</summary>
        private sealed class Erupt
        {
            public PillarHazard Main;
            public bool Fizzled;

            public bool Risen()
            {
                if (Main != null)
                {
                    return Main.Done || Main.Age >= Main.Delay + 0.03f;
                }
                return Fizzled;
            }
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>The foot comes down: the crack sets off through the ground after the foe.</summary>
        private static void Stomp(ChoreoMove m, Erupt erupt)
        {
            int level = m.Level;
            WorldTile tile = m.A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            if (m.Vis)
            {
                Fx.LandingPuff(m.Pose.Ground, m.Px, tile);
                Kit.DustRing(m.Pose.Ground, m.Px, tile, level >= 3 ? 12 : 8, level >= 3 ? 40f : 30f, 0.45f, m.Kk);
                Kit.Crater(m.Pose.Ground, m.Px * m.Kk, tile, false, 3f, true, m.Hue.Edge);
                EarthFx.Cracks(m.Pose.Ground, m.Px, 3, 0.35f * m.Kk, m.Hue, tile, 1f);
            }
            Ruin.Quake(m.A, m.Pose.Ground, m.Scale, 0.8f, m.Kk);
            m.HoldFor(0.03f * m.ImpactK);
            Vector2 from = m.Pose.Ground + m.Dir * 0.3f * m.Kk;
            Areas.Add(new CrackRun(m, from, m.VOk ? m.V : null, m.VOk ? m.VG : m.Point, erupt));
        }

        /// <summary>
        /// The crack racing through the ground from the stomp after the foe (it bends after a foe on
        /// the move: it runs far faster than anyone can): a jagged line opening a step at a time, its
        /// fresh end glowing, dust spitting out of it. Where it catches the foe the pillar erupts
        /// (with II's flanking spikes, III's ring of spikes under everyone near); with the foe gone
        /// it erupts at the crack's end. It gives up past half again the first distance.
        /// </summary>
        private sealed class CrackRun : Hazard
        {
            private readonly Actor _v;
            private readonly long _vid;
            private readonly Erupt _erupt;
            private readonly int _level;
            private readonly float _max;
            private Vector2 _head, _seg, _goal, _dir;
            private float _run;
            private int _n;
            private readonly Color _dark;

            public CrackRun(ChoreoMove m, Vector2 from, Actor v, Vector2 goal, Erupt erupt) : base(m)
            {
                _v = v;
                _vid = v != null ? v.Sid() : -1;
                _erupt = erupt;
                _level = m.Level;
                _head = _seg = from;
                _goal = goal;
                Vector2 d = goal - from;
                _dir = d.sqrMagnitude > 1e-4f ? d.normalized : m.Dir;
                _max = d.magnitude * 1.5f + 2f;
                _dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(from)), 0.55f);
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(from), 0.4f);
            }

            public override bool Tick(float dt)
            {
                Age += dt;
                if (!OwnerOk)
                {
                    _erupt.Fizzled = true;
                    return false;
                }
                if (_v != null && _v.Sid() == _vid && Combat.Living(_v) && !_v.isInMagnet() && !_v.isInsideSomething())
                {
                    _goal = Victims.GroundOf(_v);
                }
                Vector2 d = _goal - _head;
                float len = d.magnitude;
                float step = CrackSpeed * dt;
                if (len > 1e-4f)
                {
                    _dir = d / len;
                }
                if (len <= step + 0.05f || _run >= _max || Age > 1.1f)
                {
                    Vector2 at = len <= step + 0.05f ? _goal : _head;
                    Lay(_seg, at);
                    Rise(at);
                    return false;
                }
                _head += _dir * step;
                _run += step;
                if ((_head - _seg).sqrMagnitude >= 0.55f * 0.55f)
                {
                    Lay(_seg, _head);
                    _seg = _head;
                }
                return true;
            }

            private void Lay(Vector2 a, Vector2 b)
            {
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 0.05f || !Kit.Visible(a, 0.3f))
                {
                    return;
                }
                WorldTile t = Combat.TileAt(a);
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    return;
                }
                float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                int lp = Mathf.Clamp(Mathf.RoundToInt(len / Px) + 2, 4, 120);
                Particle f = Fx.Spawn(ChoreoArt.Fissure(lp, _n++ & 7), a, Px, 1.4f, FxLayer.Ground, 2);
                if (f != null)
                {
                    f.Rot = rot;
                    f.Snap = true;
                    f.Color(Fx.WithA(_dark, 0.95f), Fx.WithA(_dark, 0.85f), Fx.WithA(_dark, 0f));
                }
                Particle g = Fx.Spawn(ChoreoArt.Fissure(lp, (_n + 3) & 7), a, Px, 0.35f, FxLayer.Ground, 3);
                if (g != null)
                {
                    g.Rot = rot;
                    g.Snap = true;
                    g.Color(Fx.WithA(Hue.Core, 1f), Fx.WithA(Hue.Edge, 0.75f), Fx.WithA(Hue.Edge, 0f));
                }
                if ((_n & 1) == 0)
                {
                    ChoreoFx.HeelDust(a, Choreo.Perp(d) * (Random.value < 0.5f ? -1f : 1f), Px, t, K * 0.7f);
                }
            }

            /// <summary>The crack has caught it: the stone erupts (and the spikes round it).</summary>
            private void Rise(Vector2 at)
            {
                if (!OverGround(at))
                {
                    _erupt.Fizzled = true;   // nothing to raise a pillar from: the crack dies out
                    return;
                }
                float kk = Mathf.Lerp(1f, K, 0.5f);
                PillarHazard main = new PillarHazard(M, at, 0.07f, Height(_level) * K, Mult(_level), Lift(_level), false)
                {
                    Stand = (_level >= 3 ? 1f : 0.85f) * kk
                };
                _erupt.Main = Areas.Add(main);
                if (_level <= 1)
                {
                    return;
                }
                // spikes: II flanks the pillar with two; III rings it, one under each foe near
                float spikeH = (_level >= 3 ? 1.5f : 1.1f) * K;
                float spikeMult = _level >= 3 ? 0.9f : 0.8f;
                if (_level == 2)
                {
                    Vector2 side = Choreo.Perp(_dir);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 q = Combat.Clamp(at + side * s * K);
                        if (OverGround(q))
                        {
                            Areas.Add(new PillarHazard(M, q, 0.14f, spikeH, spikeMult, 1.4f, true) { Stand = 0.7f });
                        }
                    }
                    return;
                }
                int made = 0;
                Choreo.Foes(A, at, 2.2f * kk, _near, true);
                for (int i = 0; i < _near.Count && made < 6; i++)
                {
                    Actor u = _near[i];
                    if (u == _v || (u.current_position - at).sqrMagnitude < 0.6f || !OverGround(u.current_position))
                    {
                        continue;
                    }
                    Areas.Add(new PillarHazard(M, u.current_position, 0.13f + 0.05f * made, spikeH, spikeMult, 1.8f, true) { Stand = 0.7f });
                    made++;
                }
                // the rest of the ring: bare spikes round the column
                for (int i = made; i < 5; i++)
                {
                    float a = (i / 5f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                    Vector2 q = Combat.Clamp(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * 1.35f * K);
                    if (OverGround(q))
                    {
                        Areas.Add(new PillarHazard(M, q, 0.13f + 0.05f * i, spikeH * 0.8f, spikeMult, 1.6f, true) { Stand = 0.6f });
                    }
                }
            }
        }
    }
}
