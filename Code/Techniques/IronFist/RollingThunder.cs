using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ROLLING THUNDER (Iron Fist, Grandmaster). Lightning crawls over its body, then it is a bolt:
    /// it zigzags from foe to foe in jagged flashes, one snapping punch at each, and every one it
    /// punched is left crackling with the charge. It stops, raises its fist and closes it: the
    /// lightning leaps between the marked foes in a chain, and they detonate one after another, each
    /// struck by a bolt out of the sky and thrown up in a blast (the ground cratered under them).
    /// A chain of dashes and blows: each punch can be answered; the detonations can be slipped.
    /// I (Grandmaster): 3 foes within 6 tiles. II: 4 within 7. III: 6 within 8, faster.
    /// Damage (x the user's damage): each punch 0.45, each detonation I 1.6, II 2.0, III 2.6.
    /// </summary>
    internal sealed class RollingThunderTech : Technique
    {
        public override string Id => "rolling_thunder";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Chain;
        public override School School => School.IronFist;
        public override Form Forms => Form.Dash | Form.Punch | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 36f, 58f, 88f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Close;
        }

        public const float Punch = 0.45f;

        public static int Targets(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 4 : 6;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7f : 8f;
        }

        public static float Detonation(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2f : 2.6f;
        }

        /// <summary>The longest single dash between two foes of the chain (tiles, x size).</summary>
        public const float Hop = 4.5f;

        private static readonly List<Actor> _chain = new List<Actor>();

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
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 3f : 0f);
            float hop = Hop * k + (c.Forced ? 2f : 0f);
            int max = Targets(level);
            _chain.Clear();
            Vector2 cur = c.Pos;
            // the chain opens on its focus when it can
            Actor first = c.Focus;
            if (first != null && Fits(c, first, cur, hop, range))
            {
                _chain.Add(first);
                cur = first.current_position;
            }
            while (_chain.Count < max)
            {
                Actor next = null;
                float bd = float.MaxValue;
                for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= range; i++)
                {
                    Actor v = c.Enemies[i];
                    if (_chain.Contains(v) || !Fits(c, v, cur, hop, range))
                    {
                        continue;
                    }
                    float d = (v.current_position - cur).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        next = v;
                    }
                }
                if (next == null)
                {
                    break;
                }
                _chain.Add(next);
                cur = next.current_position;
            }
            if (_chain.Count == 0)
            {
                return null;
            }
            float total = Punch + Detonation(level);
            float score = 0f;
            bool worth = false;
            for (int i = 0; i < _chain.Count; i++)
            {
                Actor v = _chain[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, total), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2f) * cm)
                {
                    worth = true;
                }
            }
            if (!c.Forced)
            {
                int need = c.Linked ? 1 : 2;
                if (_chain.Count < need || !worth && _chain.Count < 3)
                {
                    return null;   // a chain is for a crowd (or a worthy pair), not one peasant
                }
            }
            else
            {
                score = Mathf.Max(score, 1f);
            }
            Actor t = _chain[0];
            Vector2 dir = t.current_position - c.Pos;
            return new Plan
            {
                Target = t,
                Point = t.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = _chain.Count,
                Score = score,
                Others = new List<Actor>(_chain),
                Data = c.Forced
            };
        }

        /// <summary>A foe it can dash to from 'from' and strike: fair, on the ground, in range, nothing in the way.</summary>
        private static bool Fits(Ctx c, Actor v, Vector2 from, float hop, float range)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out _, c.Forced) || !Combat.Grounded(v) || Moves.Locked(v))
            {
                return false;
            }
            if (v.isInLiquid() && !Choreo.Wading(v.current_position))
            {
                return false;
            }
            float d = Vector2.Distance(from, v.current_position);
            if (d > hop + v.stats["size"] * 0.5f || c.Dist(v) > range)
            {
                return false;
            }
            return d <= 1.2f || Combat.ClearPath(from, v.current_position);
        }

        public override bool Begin(Actor a, Plan p)
        {
            RollingThunderMove m = RollingThunderMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build(p.Others, p.Target);
            return m.Start();
        }
    }

    /// <summary>The Rolling Thunder's move: the zigzag of dashes and punches, the marks crackling on the foes, the closing fist.</summary>
    internal sealed class RollingThunderMove : ChoreoMove
    {
        private readonly List<Actor> _targets = new List<Actor>(6);
        private readonly List<long> _ids = new List<long>(6);
        private readonly List<Actor> _marked = new List<Actor>(6);
        private readonly List<long> _markIds = new List<long>(6);
        private int _next;
        private bool _detonated;
        private Hue _bolt;
        private Vector2 _trailFrom;
        private float _sparkT, _crackleT;
        private RollingThunderBurst _burst;

        private RollingThunderMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _bolt = Hue.As(Element.Lightning);
        }

        public static RollingThunderMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new RollingThunderMove(a, t, p);
        }

        public void Build(List<Actor> chain, Actor first)
        {
            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    if (chain[i] != null && !_targets.Contains(chain[i]))
                    {
                        _targets.Add(chain[i]);
                        _ids.Add(chain[i].Sid());
                    }
                }
            }
            if (_targets.Count == 0 && first != null)
            {
                _targets.Add(first);
                _ids.Add(first.Sid());
            }
            // (as many bolts as the level allows: a foe of the plan that is gone is replaced by the nearest one still standing)
            int n = Mathf.Max(_targets.Count, RollingThunderTech.Targets(Level));
            // lightning crawls over its body
            PoseTo(0.13f, new Shape(-10f, 1.1f, 0.9f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind)
                .OnEnter(mm => ((RollingThunderMove)mm).Crackle())
                .Every((mm, k) => ((RollingThunderMove)mm).CrackleFx(k));
            for (int i = 0; i < n; i++)
            {
                Do(mm => ((RollingThunderMove)mm).NextTarget());
                // a bolt to it
                Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.03f,
                    PerTile = 0.016f,
                    Route = Route.Line,
                    PathEase = Ease.OutQuad,
                    Dest = ToTarget,
                    Ease = Ease.Linear,
                    To = new Shape(14f, 1.18f, 0.88f, 0f, 0f),
                    Intangible = true,
                    Ghosts = 0.025f,
                    Forms = Form.Dash | Form.Punch | Form.Fast,
                    Enter = mm => ((RollingThunderMove)mm).Leap(),
                    Every = (mm, k) => ((RollingThunderMove)mm).Trail(),
                    Exit = mm => ((RollingThunderMove)mm).Trail()
                });
                // the snapping punch that leaves it crackling
                Strike(new Hit((i & 1) == 0 ? Blow.Cross : Blow.Hook, RollingThunderTech.Punch)
                {
                    Hurt = (i & 1) == 0 ? Hurt.SnapBack : Hurt.Spin,
                    Push = 0.3f,
                    Power = 0.9f,
                    Lethal = false,
                    WindScale = 0.45f,
                    ActScale = 0.85f,
                    Shows = Form.Punch | Form.Fast | Form.Dash,
                    OnLand = (mm, v, r) => ((RollingThunderMove)mm).Mark(v)
                });
            }
            // it stops, raises its fist, and closes it
            Do(mm => mm.SetVictim(null)).Label("det");
            PoseTo(0.12f, new Shape(-6f, 0.92f, 1.14f, 0f, 0f), Ease.OutBack, Tempo.Follow).Shows(Form.Punch | Form.Area)
                .OnEnter(mm => ((RollingThunderMove)mm).Clench());
            Do(mm => ((RollingThunderMove)mm).Detonate());
            WaitUntil(mm => ((RollingThunderMove)mm)._burst == null || ((RollingThunderMove)mm)._burst.Done, 1.2f).Shows(Form.Punch | Form.Area);
            Recover(0.3f);
            Ended2 = (mm, aborted) => ((RollingThunderMove)mm).Late(aborted);
        }

        /// <summary>The next foe of the chain still there to strike (the dead and the taken are skipped); none: the fist closes.</summary>
        private void NextTarget()
        {
            float reach = (RollingThunderTech.Hop + 2f) * Kk;
            while (_next < _targets.Count)
            {
                int i = _next++;
                Actor v = _targets[i];
                if (v != null && v.Sid() == _ids[i] && !_marked.Contains(v) && Strikable(v)
                    && Vector2.Distance(v.current_position, Stance) <= reach + v.stats["size"] * 0.5f)
                {
                    SetVictim(v);
                    return;
                }
            }
            // the plan's foes are spent: the nearest one still standing that it hasn't struck
            Actor best = null;
            float bd = float.MaxValue;
            Choreo.Foes(A, Stance, (RollingThunderTech.Hop + 1.5f) * Kk, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (_marked.Contains(u) || !Strikable(u))
                {
                    continue;
                }
                float d = (u.current_position - Stance).sqrMagnitude;
                if (d < bd && (d < 1.5f || Combat.ClearPath(Stance, u.current_position)))
                {
                    bd = d;
                    best = u;
                }
            }
            if (best != null)
            {
                SetVictim(best);
                return;
            }
            Jump("det");
        }

        private bool Strikable(Actor v)
        {
            return Choreo.Fair(A, v, out _, true) && Combat.Grounded(v) && !Moves.Locked(v) && !(v.isInLiquid() && !Choreo.Wading(v.current_position));
        }

        private static readonly List<Actor> _near = new List<Actor>();

        private void Crackle()
        {
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(3f * Kk), 0.06f, Fx.WithA(_bolt.Core, 0.9f), FxLayer.Over, 9);
            }
        }

        private void CrackleFx(float k)
        {
            if (!Vis)
            {
                return;
            }
            _sparkT -= Clock.Dt;
            if (_sparkT > 0f)
            {
                return;
            }
            _sparkT = 0.04f;
            SmallBolt(BodyPos() + Random.insideUnitCircle * Hpx * 0.4f * Px, Random.Range(3, 7));
            if (k > 0.5f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            }
        }

        /// <summary>A short crackle of lightning at 'at' (random direction).</summary>
        private void SmallBolt(Vector2 at, int len)
        {
            Particle p = Fx.Spawn(ChoreoArt.Bolt(len, Random.Range(0, 8)), at, Px, Random.Range(0.06f, 0.11f), FxLayer.Over, 11);
            if (p != null)
            {
                p.Rot = Random.Range(0f, 360f);
                p.Color(Color.white, Fx.WithA(_bolt.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        private void Leap()
        {
            _trailFrom = BodyPos();
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (Vis)
            {
                Kit.Flash(_trailFrom, Px, Mathf.RoundToInt(3f * Kk), 0.05f, Fx.WithA(Color.white, 0.95f), FxLayer.Over, 10);
                ChoreoFx.HeelDust(Stance, -Dir, Px, A.current_tile, Kk);
            }
        }

        /// <summary>The bolt it leaves behind: a jagged line of lightning from where it was to where it is.</summary>
        private void Trail()
        {
            Vector2 now = BodyPos();
            Vector2 d = now - _trailFrom;
            float px = Px;
            if (d.magnitude < px * 2f)
            {
                return;
            }
            if (Vis)
            {
                int len = Mathf.Clamp(Mathf.RoundToInt(d.magnitude / px), 4, 120);
                Particle p = Fx.Spawn(ChoreoArt.Bolt(len, Random.Range(0, 8)), _trailFrom, px, 0.22f, FxLayer.Top, 13);
                if (p != null)
                {
                    p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    p.Color(Color.white, Fx.WithA(_bolt.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                }
                Particle q = Fx.Spawn(ChoreoArt.Bolt(len, Random.Range(0, 8)), _trailFrom, px, 0.14f, FxLayer.Behind, 4);
                if (q != null)
                {
                    q.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    q.SY = 2f;
                    q.Color(Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0.4f), Fx.WithA(Hue.Rim, 0f));
                }
            }
            _trailFrom = now;
        }

        /// <summary>The punch landed: the foe is left crackling with the charge.</summary>
        private void Mark(Actor v)
        {
            if (v == null || !Combat.Living(v) || _marked.Contains(v))
            {
                return;
            }
            _marked.Add(v);
            _markIds.Add(v.Sid());
            if (Vis)
            {
                Vector2 at = Choreo.BodyAt(v, 0.6f);
                Kit.Flash(at, Px, 3, 0.05f, Fx.WithA(_bolt.Core, 0.95f), TopLayer, 21);
                for (int i = 0; i < 3; i++)
                {
                    SmallBolt(at, Random.Range(4, 8));
                }
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (Finished || !Vis || _detonated)
            {
                return;
            }
            // the marked foes crackle until they go off
            _crackleT -= dt;
            if (_crackleT > 0f || _marked.Count == 0)
            {
                return;
            }
            _crackleT = 0.06f;
            for (int i = 0; i < _marked.Count; i++)
            {
                Actor v = _marked[i];
                if (v != null && v.Sid() == _markIds[i] && v.isAlive() && Random.value < 0.7f)
                {
                    SmallBolt(Choreo.BodyAt(v, Random.Range(0.2f, 0.9f)), Random.Range(3, 6));
                }
            }
        }

        /// <summary>The fist closes: the lightning leaps from foe to foe along the chain.</summary>
        private void Clench()
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.1f);
            if (!Vis || _marked.Count == 0)
            {
                return;
            }
            float px = Px;
            Vector2 prev = Hand(Limb.Fist) + new Vector2(0f, 2f * px);
            Fx.Glint(prev, px, 0.16f, Color.white, 1f);
            for (int i = 0; i < _marked.Count; i++)
            {
                Actor v = _marked[i];
                if (v == null || v.Sid() != _markIds[i] || !v.isAlive())
                {
                    continue;
                }
                Vector2 at = Choreo.BodyAt(v, 0.55f);
                Vector2 d = at - prev;
                int len = Mathf.Clamp(Mathf.RoundToInt(d.magnitude / px), 4, 120);
                Particle p = Fx.Spawn(ChoreoArt.Bolt(len, i), prev, px, 0.2f, FxLayer.Top, 14);
                if (p != null)
                {
                    p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    p.Color(Color.white, Fx.WithA(_bolt.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                prev = at;
            }
        }

        /// <summary>They go off, one after another.</summary>
        private void Detonate()
        {
            if (_detonated)
            {
                return;
            }
            _detonated = true;
            if (_marked.Count > 0)
            {
                _burst = Areas.Add(new RollingThunderBurst(this, _marked, _markIds, RollingThunderTech.Detonation(Level), _bolt));
            }
        }

        /// <summary>Ended before the fist closed (its target lost, a punch answered): the marks still go off.</summary>
        private void Late(bool aborted)
        {
            if (!aborted && !_detonated && Combat.Living(A))
            {
                Detonate();
            }
        }
    }

    /// <summary>The marked foes of a Rolling Thunder detonating in sequence: a bolt out of the sky on each, a blast, thrown up.</summary>
    internal sealed class RollingThunderBurst : Hazard
    {
        private readonly List<Actor> _v = new List<Actor>(6);
        private readonly List<long> _id = new List<long>(6);
        private readonly float _mult, _gap;
        private readonly Hue _bolt;
        private int _i;
        private float _t;

        public RollingThunderBurst(ChoreoMove m, List<Actor> marked, List<long> ids, float mult, Hue bolt) : base(m)
        {
            _v.AddRange(marked);
            _id.AddRange(ids);
            _mult = mult;
            _bolt = bolt;
            int level = m.Level;
            _gap = level >= 3 ? 0.07f : 0.09f;
            _t = 0.02f;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            _t -= dt;
            while (_t <= 0f && _i < _v.Count)
            {
                _t += _gap;
                Go(_i++);
            }
            return _i < _v.Count || Age < 0.25f;
        }

        private void Go(int i)
        {
            Actor u = _v[i];
            if (u == null || u.Sid() != _id[i] || !Combat.Living(u))
            {
                return;
            }
            Vector2 g = Victims.GroundOf(u);
            bool vis = Kit.Visible(g, 0.4f);
            Vector2 lift = new Vector2(Random.Range(-0.15f, 0.15f), 1f);
            Strike(u, _mult, Hurt.Launch, u.current_position - lift * 0.5f, 0.5f, 1.6f + 0.25f * M.Level, 1.5f);
            Ruin.Blast(A, g, 0.6f, Scale, A != null && A.isAlive() ? A.current_position : (Vector2?)null);
            Ruin.Quake(A, g, Scale, 0.9f, K);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(g));
            if (!vis)
            {
                return;
            }
            float px = Px;
            // the bolt out of the sky
            int len = Mathf.Clamp(Mathf.RoundToInt(5f * K / px), 20, 120);
            Vector2 top = g + new Vector2(Random.Range(-0.4f, 0.4f), len * px);
            Particle p = Fx.Spawn(ChoreoArt.Bolt(len, i), top, px, 0.16f, FxLayer.Top, 26);
            if (p != null)
            {
                p.Rot = -90f + Random.Range(-6f, 6f);
                p.SY = 2f;
                p.Color(Color.white, Fx.WithA(_bolt.Core, 0.95f), Fx.WithA(Hue.Edge, 0f));
            }
            Vector2 body = Choreo.BodyAt(u, 0.5f);
            Kit.Flash(body, px, Mathf.RoundToInt(5f * K), 0.08f, Fx.WithA(_bolt.Core, 1f), FxLayer.Top, 25);
            Fx.Ring(g, px, 0.26f, 3f, 14f * K, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            Kit.Sparks(body, px, 6, 60f, 130f, _bolt.Core);
            Kit.DustRing(g, px, Combat.TileAt(g), 8, 36f, 0.45f, K);
            Kit.Crater(g, px * K, Combat.TileAt(g), Ruin.Force(Tiers.Of(A), Scale) >= 3, 4f, true, Hue.Edge);
        }
    }
}
