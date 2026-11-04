using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HORDE REAPER (Hunter's Way, Grandmaster). Made for swarms: rats, ants, bees, chickens, any crowd
    /// of small things. It dashes into the heart of the swarm and drops low, one leg sweeping out,
    /// and whirls: a reaping crescent of green wind trails its leg round the full circle, once, twice,
    /// faster each turn, the grass mown flat in a ring and leaves and blades of grass flung out; every
    /// turn reaps everything small in the circle (they tumble away spinning), and the last turn
    /// lifts off the ground and flings the survivors far out of the ring. I: two turns (2.6 tiles);
    /// II: three (3.2 tiles), the ring of wind racing out after the last turn; III: four turns (4
    /// tiles), the last a rising spiral that throws them all into the air. Damage per turn (x the
    /// user's damage, to everyone in the ring): I 0.6, II 0.65, III 0.7; the last turn x1.5.
    /// Chains from Close and Scattered; leaves Scattered.
    /// </summary>
    internal sealed class HordeReaperTech : Technique
    {
        public override string Id => "horde_reaper";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Area | Form.Flurry;
        public override Prey Prey => Prey.Swarm;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 38f, 60f, 90f };
        public override float[] Cooldown => new[] { 14f, 15f, 16f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.2f : 4f;
        }

        public static int Turns(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        public static float PerTurn(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.65f : 0.7f;
        }

        /// <summary>What one caught in every turn takes in all.</summary>
        public static float Total(int level)
        {
            return PerTurn(level) * (Turns(level) - 1) + PerTurn(level) * 1.5f;
        }

        private static readonly List<Actor> _swarm = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level);
            float range = level <= 1 ? 5f : level == 2 ? 6f : 7f;
            // the heart of the swarm: the small things it would reap, counted round each of them
            Vector2 best = c.Pos;
            Actor center = null;
            int bestN = 0;
            for (int i = 0; i < c.Enemies.Count && i < 20; i++)
            {
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                if (!Combat.Grounded(v) || !Preys.Is(v, Prey.Swarm) || !c.Allowed(v) || Moves.Locked(v))
                {
                    continue;
                }
                _swarm.Clear();
                int n = BigGame.CountPrey(c, v.current_position, r * 0.8f, Prey.Swarm, _swarm);
                if (n > bestN)
                {
                    Vector2 sum = Vector2.zero;
                    for (int j = 0; j < _swarm.Count; j++)
                    {
                        sum += _swarm[j].current_position;
                    }
                    best = _swarm.Count > 0 ? sum / _swarm.Count : v.current_position;
                    bestN = n;
                    center = v;
                }
            }
            int need = c.Linked ? 3 : level <= 1 ? 5 : 4;
            if (center == null || bestN < need && !c.Forced)
            {
                if (!c.Forced || c.Enemies.Count == 0)
                {
                    return null;
                }
                center = c.ForceTarget ?? c.Enemies[0];
                best = center.current_position;
            }
            // the dash in must be clear, the heart of the swarm dry land
            if (!c.Forced && (!Combat.SafeSpot(best) || !Combat.ClearPath(c.Pos, best)))
            {
                return null;
            }
            // value: everyone the ring will catch (swarm or not)
            _swarm.Clear();
            c.CountNear(best, r, _swarm);
            float total = Total(level);
            float score = 0f;
            int swarm = 0;
            for (int j = 0; j < _swarm.Count; j++)
            {
                Actor v = _swarm[j];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, total), cm);
                if (Preys.Is(v, Prey.Swarm))
                {
                    swarm++;
                }
            }
            // it is made for crowds: the more it reaps, the better
            score += swarm * 6f;
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = best - c.Pos;
            return new Plan
            {
                Target = center,
                Point = best,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = Mathf.Max(1, _swarm.Count),
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
            Reap reap = new Reap(m, p.Point);
            int level = m.Level;
            // into the heart of the swarm
            m.Dash(mm => reap.Heart(mm), 0.024f).OnlyIf(mm => Vector2.Distance(mm.Stance, reap.Heart(mm)) > 0.6f * mm.Kk);
            // down low, the leg out
            m.PoseTo(0.1f, new Shape(6f, 1.26f, 0.72f, 0f), Ease.OutQuad, Tempo.Wind).OnEnter(mm => reap.Drop(mm));
            int turns = Turns(level);
            for (int i = 0; i < turns; i++)
            {
                reap.AddTurn(i, turns);
            }
            m.Recover(0.3f);
            return m.Start();
        }
    }

    /// <summary>
    /// Dev: "hw_hostile [kingdom]" turns the latest demo's foes (everyone not of the performer's
    /// kingdom) into members of a hostile wild kingdom (default "undead"), so peaceful creatures
    /// (rats, chickens, bees) can stand in for a hostile swarm or flier in a demo.
    /// </summary>
    internal static class HunterDev
    {
        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["hw_hostile"] = Hostile;
        }

        private static void Hostile(string[] args)
        {
            Actor perf = Dev.Performer;
            if (perf == null || World.world == null)
            {
                Dev.Log("hw_hostile: no performer");
                return;
            }
            Kingdom k = World.world.kingdoms_wild.get(Dev.Arg(args, 1, "undead"));
            if (k == null)
            {
                Dev.Log("hw_hostile: no such kingdom");
                return;
            }
            int n = 0;
            foreach (Actor u in Dev.Units)
            {
                if (u == null || u == perf || !u.isAlive() || u.kingdom == perf.kingdom)
                {
                    continue;
                }
                u.setKingdom(k);
                n++;
            }
            Dev.Log("hw_hostile: " + n + " units now of " + k.name + " (enemy of the performer: " + (perf.kingdom != null && perf.kingdom.isEnemy(k)) + ")");
        }
    }

    /// <summary>One Horde Reaper: the whirling turns, the crescent of wind, the reaped swarm.</summary>
    internal sealed class Reap
    {
        private readonly ChoreoMove _m;
        private readonly Vector2 _point;
        private static readonly List<Actor> _caught = new List<Actor>();
        private float _blades;

        public Reap(ChoreoMove m, Vector2 point)
        {
            _m = m;
            _point = point;
        }

        /// <summary>The heart of the swarm (on solid ground).</summary>
        public Vector2 Heart(ChoreoMove m)
        {
            Vector2 p = Combat.Clamp(_point);
            if (Combat.SafeSpot(p) && Combat.ClearPath(m.Stance, p))
            {
                return p;
            }
            return m.Stance;
        }

        public void Drop(ChoreoMove m)
        {
            m.SetVictim(null);   // it reaps the ring, not one of them: no facing, no chasing
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.08f);
            if (m.Vis)
            {
                Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 36f, 0.45f, m.Kk);
            }
        }

        /// <summary>One whirl of the reaping leg: faster each time; the last one lifts and flings.</summary>
        public void AddTurn(int i, int turns)
        {
            ChoreoMove m = _m;
            bool last = i == turns - 1;
            int level = m.Level;
            float dur = Mathf.Lerp(0.3f, 0.18f, turns > 1 ? i / (float)(turns - 1) : 1f);
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = last && level >= 3 ? new Shape(0f, 0.9f, 1.14f, 0.5f) : new Shape(0f, 1.26f, 0.72f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.03f,
                Dust = i == 0 ? 0.1f : 0f,
                Forms = Form.Hunt | Form.Area | Form.Flurry | Form.Kick | Form.Spin | Form.Low
            };
            float face = 1f;
            b.Enter = mm =>
            {
                face = mm.Fwd;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                if (mm.Vis)
                {
                    float r = HordeReaperTech.Radius(level);
                    // the reaping crescent: a ring of green wind round it, the grass mown in its track
                    int rr = Mathf.Clamp(Mathf.RoundToInt(r / mm.Px * 0.9f), 8, 70);
                    Particle ring = Fx.Spawn(Art.Arc(rr, Art.Ground, 0f, 360f, level >= 3 ? 3 : 2), mm.Pose.Ground, mm.Px, mm.BeatDur * 1.6f + 0.06f, FxLayer.Ground, 6);
                    if (ring != null)
                    {
                        ring.SX = face > 0f ? -1f : 1f;   // (it sweeps the way the leg turns)
                        ring.Color(Fx.WithA(mm.Hue.Core, 0.95f), Fx.WithA(mm.Hue.Main, 0.8f), Fx.WithA(mm.Hue.Edge, 0f));
                    }
                    int sr = Mathf.Clamp(Mathf.RoundToInt(10f * mm.Kk), 6, 30);
                    Kit.Swish(mm, Art.Arc(sr, 0.42f, 0f, 360f, 3), mm.BeatDur * 1.4f, new Vector2(0f, -mm.Pose.PivotUp + 1.5f), face, mm.Hue.Core, mm.Hue.Edge, 6);
                }
            };
            b.Every = (mm, k) =>
            {
                // a full turn on the spot: the sprite narrows, flips, widens
                float t = Beat.Apply(Ease.InOutSine, k);
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                mm.Pose.SX *= Mathf.Max(0.25f, Mathf.Abs(c));
                mm.Pose.FaceRight = t > 0.25f && t < 0.75f ? face < 0f : face > 0f;
                Blades(mm);
            };
            b.On(0.5f, mm => Sweep(mm, last));
            b.Exit = mm => mm.Pose.FaceRight = face > 0f;
            m.Add(b);
            if (last)
            {
                m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, To = new Shape(-6f, 1.12f, 0.9f, 0f), Ease = Ease.OutExpo, Track = false });
            }
        }

        /// <summary>Blades of grass and leaves flung out of its track.</summary>
        private void Blades(ChoreoMove m)
        {
            _blades -= Clock.Dt;
            if (_blades > 0f || !m.Vis)
            {
                return;
            }
            _blades = 0.03f;
            float r = HordeReaperTech.Radius(m.Level);
            WorldTile tile = m.A.current_tile;
            Color grass = Fx.TopColor(tile);
            for (int i = 0; i < 2; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 at = m.Pose.Ground + d * Random.Range(0.3f, 0.9f) * r;
                Particle p = Fx.Spawn(Art.Pixel, at, m.Px, Random.Range(0.3f, 0.5f), FxLayer.Top, 6);
                p?.Moving(d * Random.Range(20f, 40f) * m.Px + new Vector2(-d.y, d.x) * 20f * m.Px, Random.Range(8f, 16f) * m.Px, 2f, 30f * m.Px)
                    .Sized(1.6f, 1f).Color(Fx.WithA(Color.Lerp(grass, m.Hue.Main, 0.3f), 0.95f), Fx.WithA(grass, 0.7f), Fx.WithA(grass, 0f));
            }
        }

        /// <summary>The turn reaps everything small in the ring (the last flings them out).</summary>
        private void Sweep(ChoreoMove m, bool last)
        {
            int level = m.Level;
            float r = HordeReaperTech.Radius(level);
            Vector2 at = m.Pose.Ground;
            float mult = HordeReaperTech.PerTurn(level) * (last ? 1.5f : 1f);
            Choreo.Foes(m.A, at, r, _caught);
            int n = 0;
            for (int i = 0; i < _caught.Count; i++)
            {
                Actor u = _caught[i];
                if (!Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                float near = 1f - Mathf.Clamp01(Vector2.Distance(u.current_position, at) / Mathf.Max(0.1f, r));
                HitResult res = Combat.Hit(m.A, u, mult * cm * Mathf.Lerp(0.75f, 1f, near), last, false, 0f, m);
                n++;
                if (Combat.Living(u))
                {
                    Vector2 away = u.current_position - at;
                    Vector2 swirl = Choreo.Perp(away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.right) * (m.Fwd > 0f ? -1f : 1f);
                    Impact im;
                    if (last)
                    {
                        im = Impact.Of(Hurt.Launch, away.normalized * 0.8f + swirl * 0.4f, 1.6f + 0.4f * level, 1.2f);
                        im.Lift = level >= 3 ? 2.4f : 1.4f;
                    }
                    else
                    {
                        im = Impact.Of(Hurt.Spin, swirl * 0.7f + away.normalized * 0.3f, 0.5f, 0.9f);
                    }
                    im.Owed = MeteorSlam.AtDeathsDoor(u);
                    Victims.Hurt(u, m.A, m, im);
                }
                else
                {
                    Victims.Flash(u, Color.white, 0.05f);
                }
                if (m.Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.4f), u.current_position - at, m.Px, m.Kk, 0.7f, m.Hue, m.TopLayer);
                }
            }
            // the grass mown flat in a ring (III: torn up)
            if (level >= 3 && last)
            {
                Ruin.Blast(m.A, at, r * 0.5f, Scale.Light, m.Pose.Ground, false);
            }
            if (last)
            {
                if (m.Vis)
                {
                    Kit.Shockwave(at, m.Px, 3f, r / m.Px * (level >= 2 ? 1.4f : 1f), 0.4f, Fx.WithA(m.Hue.Edge, 0.85f), 2);
                    Kit.DustRing(at, m.Px, m.A.current_tile, Fx.Count(8), 60f, 0.5f, m.Kk);
                }
                if (level >= 2)
                {
                    // the ring of wind races on out after the last turn
                    m.AreaBlow(at, r * 1.4f, 0.3f, null, Hurt.Knockdown, 0.8f);
                }
                Ruin.Quake(m.A, at, m.Scale, 0.9f, m.Kk);
                m.SetLink(Link.Scattered, null);
            }
            if (Dev.On) Dev.Trace("horde_reaper turn" + (last ? " (last)" : "") + ": " + n + " reaped");
        }
    }
}
