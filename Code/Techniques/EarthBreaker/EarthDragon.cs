using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// EARTH DRAGON (Earth Breaker, Grandmaster). It rears up and slams both palms into the earth, and a
    /// dragon of rock wakes under the ground: a ridge of stone spines races away through the earth on a
    /// winding path that seeks out the enemies in front of it one after another (the ground bulging and
    /// spraying rock over its back, a crack left behind, the land torn along it). Under each foe it
    /// passes, its head bursts up out of the ground, jaws open, and hurls the foe into the air
    /// (Launched), then dives back under; at the end of its run it rears up whole under the last one,
    /// and shatters into boulders. The Earth Breaker stays in its guiding stance, arm flung along the
    /// dragon's way. Unlike a fault line it winds: around, between, through a scattered group. It stops
    /// at water and walls. I 11 tiles and 3 foes; II 13 and 4, glowing veins; III 15 and 6, the run
    /// tears the land deeper and the last rearing is an impact frame. Damage to each foe it erupts
    /// under (x the user's damage): I 1.8, II 2.3, III 3; the last rearing x0.8 / 1 / 1.3 on top.
    /// </summary>
    internal sealed class EarthDragonTech : Technique
    {
        public override string Id => "earth_dragon";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Line;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Line;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 38f, 62f, 92f };
        public override float[] Cooldown => new[] { 14f, 15f, 17f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Length(int level)
        {
            return EarthKit.L(level, 11f, 13f, 15f);
        }

        public static int MaxFoes(int level)
        {
            return EarthKit.L(level, 3, 4, 6);
        }

        public static float Mult(int level)
        {
            return EarthKit.L(level, 1.8f, 2.3f, 3f);
        }

        public static float FinalMult(int level)
        {
            return EarthKit.L(level, 0.8f, 1f, 1.3f);
        }

        private static readonly List<Actor> _way = new List<Actor>();
        private static readonly List<float> _along = new List<float>();

        /// <summary>
        /// Into '_way': the enemies the dragon winds through on its way to 'aim' (grounded, fair, in a
        /// cone round the line, nearer than the aim), nearest first, the aim last; at most 'max'.
        /// </summary>
        private static void Gather(Ctx c, Actor aim, Vector2 dir, float d, int max)
        {
            _way.Clear();
            _along.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 24; i++)
            {
                Actor u = c.Enemies[i];
                if (u == aim || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u) || u.isInLiquid() && !Choreo.Wading(u.current_position))
                {
                    continue;
                }
                Vector2 rel = u.current_position - c.Pos;
                float along = Vector2.Dot(rel, dir);
                if (along < 1f || along > d - 0.6f)
                {
                    continue;
                }
                float off = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                if (off > along * 0.7f + 0.8f)
                {
                    continue;
                }
                int at = _along.Count;
                while (at > 0 && _along[at - 1] > along)
                {
                    at--;
                }
                _along.Insert(at, along);
                _way.Insert(at, u);
            }
            if (_way.Count > max - 1)
            {
                _way.RemoveRange(Mathf.Max(0, max - 1), _way.Count - Mathf.Max(0, max - 1));
                _along.RemoveRange(Mathf.Max(0, max - 1), _along.Count - Mathf.Max(0, max - 1));
            }
            _way.Add(aim);
            _along.Add(d);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float len = Length(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int max = MaxFoes(level);
            float mult = Mult(level);
            Actor bestAim = null;
            float bestScore = 0f;
            int bestN = 0;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                Actor aim = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > len || d < (c.Forced ? 0.8f : 2f))
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, aim, out _, c.Forced) || !Combat.Grounded(aim) || aim.isInLiquid() && !Choreo.Wading(aim.current_position))
                {
                    continue;
                }
                Vector2 dir = (aim.current_position - c.Pos).normalized;
                // it needs ground to wake in: no water or wall right in front
                if (!Combat.ClearPath(c.Pos, c.Pos + dir * Mathf.Min(2f, d)))
                {
                    continue;
                }
                Gather(c, aim, dir, d, max);
                float score = 0f;
                int n = 0;
                bool worth = false;
                Vector2 from = c.Pos;
                for (int j = 0; j < _way.Count; j++)
                {
                    Actor u = _way[j];
                    // it can't cross water or a wall to get there: the run ends before it
                    if (!Combat.ClearPath(from, u.current_position))
                    {
                        break;
                    }
                    from = u.current_position;
                    c.Allowed(u, out float cm);
                    float m = u == aim ? mult + FinalMult(level) : mult;
                    score += c.Value(u, c.Expected(u, m), cm);
                    n++;
                    if (Tiers.Of(u) != Tier.None || Weights.Species(u) >= Weight.Brute || u.getHealth() > c.Expected(u, 2.5f) * cm)
                    {
                        worth = true;
                    }
                }
                if (n == 0 || !c.Forced && !worth && n < 3)
                {
                    continue;   // not for one or two peasants
                }
                if (score > bestScore || bestAim == null)
                {
                    bestScore = score;
                    bestAim = aim;
                    bestN = n;
                }
            }
            if (bestAim == null)
            {
                return null;
            }
            Vector2 bd = (bestAim.current_position - c.Pos).normalized;
            Gather(c, bestAim, bd, Vector2.Distance(c.Pos, bestAim.current_position), max);
            return new Plan
            {
                Target = bestAim,
                Point = bestAim.current_position,
                Dir = bd,
                Count = bestN,
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                Others = new List<Actor>(_way),
                Data = c.Forced
            };
        }

        /// <summary>The dragon the move summoned (its guiding pose follows the head).</summary>
        private sealed class Run
        {
            public EarthDragonHazard H;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            List<Actor> way = p.Others;
            Run run = new Run();
            m.Face(p.Dir);
            // it rears up, arms high...
            m.PoseTo(0.15f, new Shape(-16f, 0.9f, 1.16f, 0.1f, -0.04f), Ease.OutQuad, Tempo.Wind)
                .At(0.7f, mm =>
                {
                    if (mm.Vis && mm.Level >= 2)
                    {
                        Fx.Glint(mm.HeadPos() + new Vector2(0f, 2f * mm.Px), mm.Px, 0.16f, mm.Hue.Core, 0.9f);
                    }
                });
            // ...and slams both palms into the earth: the dragon wakes
            m.PoseTo(0.07f, new Shape(36f, 1.3f, 0.72f, 0f, 0.16f), Ease.InQuad, Tempo.Act).Ghosting(0.03f);
            m.Do(mm => Summon(mm, way, run));
            m.Hold(0.1f, true, Tempo.Impact);
            // the guiding stance: one arm flung along the dragon's way, turning to follow it
            m.PoseTo(0.12f, new Shape(16f, 1.08f, 0.94f, 0f, 0.12f), Ease.OutCubic, Tempo.Follow);
            Beat guide = new Beat { Tempo = Tempo.Free, Dur = 0.22f, MaxDur = 0.75f, Track = false, To = new Shape(18f, 1.1f, 0.92f, 0f, 0.14f), Ease = Ease.InOutSine };
            guide.Every = (mm, k) =>
            {
                EarthDragonHazard h = run.H;
                if (h != null && !h.Done)
                {
                    Vector2 d = h.Head - mm.Stance;
                    if (d.sqrMagnitude > 0.25f)
                    {
                        mm.Face(d);
                    }
                }
            };
            guide.Until = mm => run.H == null || run.H.Done || run.H.Progress > 0.75f;
            m.Add(guide);
            m.Recover(0.3f);
            return m.Start();
        }

        private static readonly List<Vector2> _pts = new List<Vector2>();

        /// <summary>The palms hit the earth: the winding way is laid out through the foes, and the dragon starts its run.</summary>
        private static void Summon(ChoreoMove m, List<Actor> way, Run run)
        {
            int level = m.Level;
            float px = m.Px, k = m.Kk;
            Vector2 g = m.Pose.Ground + m.Dir * 0.4f * k;
            WorldTile tile = Combat.TileAt(g);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            if (m.Vis)
            {
                Kit.Flash(g + new Vector2(0f, 1.2f * px), px, Mathf.RoundToInt(5f * k), 0.07f, Fx.WithA(m.Hue.Core, 0.95f));
                Kit.DustRing(g, px, tile, 12, 42f, 0.5f, k);
                Kit.Debris(g, px, tile, 7, k);
                Kit.Crater(g, px * k, tile, level >= 3, 5f, true, m.Hue.Edge);
            }
            Ruin.Blast(m.A, g, 0.7f, m.Scale, m.Pose.Ground, false);
            Ruin.Quake(m.A, g, m.Scale, 1.3f, k);
            m.HoldFor(0.05f * m.ImpactK);
            // the way: from its palms through each foe in turn, weaving between them
            _pts.Clear();
            Vector2 start = m.Pose.Ground + m.Dir * 0.8f * k;
            if (!Combat.Passable(start))
            {
                start = m.Pose.Ground;
            }
            _pts.Add(start);
            Vector2 last = start;
            float len = Length(level) * Mathf.Lerp(1f, k, 0.5f);
            float sign = Random.value < 0.5f ? -1f : 1f;
            bool cut = false;
            int stops = 0;
            if (way != null)
            {
                for (int i = 0; i < way.Count && !cut; i++)
                {
                    Actor u = way[i];
                    if (u == null || !Combat.Living(u))
                    {
                        continue;
                    }
                    Vector2 to = Victims.GroundOf(u);
                    cut = !Weave(last, to, sign, k);
                    sign = -sign;
                    last = _pts[_pts.Count - 1];
                    stops++;
                    Combat.Pin(u, 1.6f);
                }
            }
            if (stops == 0 && !cut)
            {
                // nobody left to seek: it runs straight out along its aim
                Weave(last, Combat.Clamp(last + m.Dir * len * 0.6f), sign, k);
            }
            if (_pts.Count < 2)
            {
                return;   // no ground to run through
            }
            EarthDragonHazard h = new EarthDragonHazard(m, _pts, level, Mult(level), FinalMult(level));
            Areas.Add(h);
            run.H = h;
            if (Dev.On) Dev.Trace("earth_dragon " + Techniques.Roman(level) + ": a way of " + _pts.Count + " points through " + stops + " foes" + (cut ? " (cut short by water or a wall)" : ""));
        }

        /// <summary>Adds a weaving stretch from 'a' to 'b' to the way (it swings 'sign' to one side). False if the ground ended on the way.</summary>
        private static bool Weave(Vector2 a, Vector2 b, float sign, float k)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.2f)
            {
                return true;
            }
            Vector2 perp = new Vector2(-d.y, d.x) / len;
            float amp = Mathf.Min(0.8f, len * 0.18f) * Mathf.Sqrt(k);
            int waves = Mathf.Max(1, Mathf.RoundToInt(len / 3.4f));
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 0.35f));
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                Vector2 straight = Vector2.Lerp(a, b, u);
                Vector2 q = straight + perp * sign * amp * Mathf.Sin(Mathf.PI * waves * u);
                if (!Combat.Passable(q))
                {
                    q = straight;
                    if (!Combat.Passable(q))
                    {
                        return false;
                    }
                }
                _pts.Add(q);
            }
            return true;
        }
    }

    /// <summary>
    /// The Earth Dragon's run: a ridge of stone spines racing through the ground along a winding way,
    /// the earth bulging over its back; it bursts up head first under every enemy it passes (launching
    /// it), and at the end rears up whole and shatters into boulders. Tears the ground along its way.
    /// </summary>
    internal sealed class EarthDragonHazard : Hazard
    {
        private readonly List<Vector2> _pts = new List<Vector2>();
        private readonly float[] _cum;
        private readonly float _total, _speed, _mult, _final, _lift;
        private readonly int _level;
        private float _s, _breachT, _endT, _furrowAt, _crackAt, _spineAt, _dustT;
        private bool _end, _quiet, _finalDone;
        private Vector2 _head, _tan = Vector2.right;
        private Particle _headP, _eye, _mound;
        private readonly Color _stone;
        private readonly List<Particle> _spines = new List<Particle>();
        private readonly List<float> _spineT = new List<float>();
        private readonly HashSet<long> _finalIds = new HashSet<long>();
        private static readonly List<Actor> _hit = new List<Actor>();

        public EarthDragonHazard(ChoreoMove m, List<Vector2> pts, int level, float mult, float final) : base(m)
        {
            _pts.AddRange(pts);
            _cum = new float[_pts.Count];
            for (int i = 1; i < _pts.Count; i++)
            {
                _cum[i] = _cum[i - 1] + Vector2.Distance(_pts[i - 1], _pts[i]);
            }
            _total = _cum[_cum.Length - 1];
            _level = level;
            _mult = mult;
            _final = final;
            _lift = EarthKit.L(level, 2f, 2.4f, 2.8f);
            _speed = EarthKit.L(level, 14f, 16.5f, 19f) * Mathf.Lerp(1f, K, 0.5f);
            _head = _pts[0];
            _stone = Color.Lerp(EarthKit.Stone(_pts[0]), Hue.Main, level >= 2 ? 0.12f : 0.05f);
        }

        /// <summary>Where its head is now.</summary>
        public Vector2 Head => _head;

        /// <summary>How far along its way it is (0..1).</summary>
        public float Progress => _total > 0f ? _s / _total : 1f;

        private Vector2 At(float s, out Vector2 tan)
        {
            int n = _pts.Count;
            if (s <= 0f || n < 2)
            {
                tan = n >= 2 ? (_pts[1] - _pts[0]).normalized : Vector2.right;
                return _pts[0];
            }
            for (int i = 1; i < n; i++)
            {
                if (_cum[i] >= s)
                {
                    float seg = Mathf.Max(1e-4f, _cum[i] - _cum[i - 1]);
                    Vector2 d = _pts[i] - _pts[i - 1];
                    tan = d.sqrMagnitude > 1e-6f ? d.normalized : _tan;
                    return Vector2.Lerp(_pts[i - 1], _pts[i], (s - _cum[i - 1]) / seg);
                }
            }
            Vector2 e = _pts[n - 1] - _pts[n - 2];
            tan = e.sqrMagnitude > 1e-6f ? e.normalized : _tan;
            return _pts[n - 1];
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            if (!_end && !OwnerOk)
            {
                _end = true;
                _quiet = true;   // the one who woke it is gone: it sinks back without its last blow
            }
            bool vis = Kit.Visible(_head, 0.5f);
            if (!_end)
            {
                float sp = _speed * (_breachT > 0f ? 0.55f : 1f);
                _s = Mathf.Min(_total, _s + sp * dt);
                _head = At(_s, out _tan);
                Spines(vis);
                Tear();
                Strike();
                if (vis)
                {
                    Bulge(dt);
                }
                if (_s >= _total)
                {
                    _end = true;
                }
            }
            else
            {
                _endT += dt;
                if (!_finalDone && !_quiet && _endT >= 0.1f)
                {
                    _finalDone = true;
                    FinalBlow();
                }
            }
            _breachT = Mathf.Max(0f, _breachT - dt);
            TickSpines(dt);
            DrawHead(vis);
            bool over = _end && (_quiet || _endT >= 0.36f);
            if (over && _mound != null)
            {
                _mound.Kill();
                _mound = null;
            }
            return !over || _spines.Count > 0;
        }

        // ------------------------------------------------------------ the run --

        /// <summary>A spine of rock rises over its back every half tile, behind the head.</summary>
        private void Spines(bool vis)
        {
            float gap = 0.42f * Mathf.Lerp(1f, K, 0.5f);
            while (_spineAt <= _s - 0.25f)
            {
                float s = _spineAt;
                _spineAt += gap;
                if (!vis || _spines.Count > 40)
                {
                    continue;
                }
                Vector2 p = At(s, out Vector2 tan);
                Vector2 side = new Vector2(-tan.y, tan.x) * (((int)(s / gap) & 1) == 0 ? 0.1f : -0.1f) * K;
                int w = Mathf.RoundToInt((3f + _level) * Mathf.Sqrt(K));
                int h = Mathf.RoundToInt((4f + 1.1f * _level) * K * Random.Range(0.8f, 1.15f));
                Particle sp = Fx.Spawn(ChoreoArt.Spike(w, h), p + side, Px, 1f, FxLayer.Behind, 6);
                if (sp == null)
                {
                    continue;
                }
                sp.Held = true;
                sp.Rot = -tan.x * 14f;
                sp.SY = 0f;
                sp.Grad = null;
                sp.R.color = Fx.Darken(_stone, Random.Range(0f, 0.15f));
                _spines.Add(sp);
                _spineT.Add(0f);
            }
        }

        private void TickSpines(float dt)
        {
            for (int i = _spines.Count - 1; i >= 0; i--)
            {
                Particle p = _spines[i];
                float t = _spineT[i] + dt;
                _spineT[i] = t;
                if (p == null || !p.Alive || t > 0.6f)
                {
                    if (p != null && p.Alive)
                    {
                        p.Kill();
                    }
                    _spines.RemoveAt(i);
                    _spineT.RemoveAt(i);
                    continue;
                }
                // up fast, stands a moment, sinks back into the ground
                float rise = Beat.Apply(Ease.OutBack, Mathf.Clamp01(t / 0.08f));
                float sink = Mathf.Clamp01((t - 0.3f) / 0.3f);
                p.SY = Mathf.Max(0.01f, rise * (1f - Beat.Apply(Ease.InQuad, sink)));
            }
        }

        /// <summary>The ground torn along its way, and a crack left behind it.</summary>
        private void Tear()
        {
            while (_crackAt + 1.2f <= _s)
            {
                Vector2 a = At(_crackAt, out _), b = At(_crackAt + 1.2f, out _);
                EarthKit.Crack(a, b, Px, 2.4f, _level >= 2 ? Hue.Edge : (Color?)null);
                _crackAt += 1.2f;
            }
            if (_s - _furrowAt >= 1.6f || _s >= _total && _s > _furrowAt + 0.2f)
            {
                if (OwnerOk)
                {
                    Ruin.Furrow(A, At(_furrowAt, out _), _head, 0.55f, Scale, A.current_position, false, _level >= 3 ? 1 : 0);
                }
                _furrowAt = _s;
            }
        }

        /// <summary>The earth bulges over its back: rock spraying up at the head, a ridge of dust.</summary>
        private void Bulge(float dt)
        {
            _dustT -= dt;
            if (_dustT > 0f)
            {
                return;
            }
            _dustT = 0.045f;
            WorldTile tile = Combat.TileAt(_head);
            ChoreoFx.HeelDust(_head, -_tan, Px, tile, K);
            ChoreoFx.HeelDust(_head, new Vector2(-_tan.y, _tan.x) * (Random.value < 0.5f ? 1f : -1f), Px, tile, K * 0.8f);
            if (Random.value < 0.45f)
            {
                Kit.Debris(_head, Px, tile, 1, K * 0.7f);
            }
        }

        /// <summary>Every enemy it passes under: the head bursts up out of the ground and hurls it into the air.</summary>
        private void Strike()
        {
            if (!OwnerOk || _total - _s < 1.1f)
            {
                return;   // (the last stretch: the rearing at the end takes whoever stands there)
            }
            float r = 0.75f * Mathf.Lerp(1f, K, 0.5f);
            Choreo.Foes(A, _head, r, _hit);
            for (int i = 0; i < _hit.Count; i++)
            {
                Actor u = _hit[i];
                long id = u.Sid();
                if (HitIds.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                HitIds.Add(id);
                if (Combat.Reach(M, u, true) != Contact.Hit)
                {
                    continue;
                }
                Combat.Hit(A, u, _mult * cm, false, false, 0f, M);
                Vector2 rel = u.current_position - _head;
                Vector2 side = new Vector2(-_tan.y, _tan.x);
                float sgn = Vector2.Dot(rel, side) >= 0f ? 1f : -1f;
                Impact im = Impact.Of(Hurt.Launch, side * sgn * 0.6f + _tan * 0.4f, 0.8f, 1.4f);
                im.Lift = _lift;
                im.Daze = 0.8f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.06f);
                if (!M.Ended)
                {
                    M.SetLink(Link.Launched, u);
                }
                Breach(u.current_position);
            }
        }

        /// <summary>The head bursts up out of the ground (and dives back): rock, a flash, a crater.</summary>
        private void Breach(Vector2 at)
        {
            _breachT = 0.26f;
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.04f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.08f);
            if (OwnerOk)
            {
                Ruin.Blast(A, at, 0.7f, Scale, A.current_position, false);
                Ruin.Quake(A, at, Scale, 1f, K);
            }
            if (!Kit.Visible(at, 0.4f))
            {
                return;
            }
            Kit.Flash(at + new Vector2(0f, 0.4f * K), Px, Mathf.RoundToInt(4f * K), 0.06f, Fx.WithA(Hue.Core, 0.9f));
            ChoreoFx.Impact(at + new Vector2(0f, 0.5f * K), Vector2.up, Px, K, 1.3f, Hue, FxLayer.Top, false);
            Kit.Debris(at, Px, tile, 8, K);
            Kit.DustRing(at, Px, tile, 10, 40f, 0.5f, K);
            Kit.Crater(at, Px * K, tile, _level >= 3, 5f, true, Hue.Edge);
            EarthKit.Boulders(at, Px, 2, K, _stone);
            // a fan of spikes bursts up round it
            for (int i = 0; i < 3; i++)
            {
                float side = i - 1f;
                Particle p = Fx.Spawn(ChoreoArt.Spike(Mathf.RoundToInt(4f * K), Mathf.RoundToInt((7f + 2f * _level) * K)), at + new Vector2(side * 0.32f * K, -0.04f * Mathf.Abs(side)), Px, 0.5f,
                    side == 0f ? FxLayer.Behind : FxLayer.Over, 5);
                if (p == null)
                {
                    break;
                }
                p.Rot = -side * 24f;
                p.Sized(0.3f, 1f).Color(_stone, _stone, Fx.WithA(_stone, 0f));
            }
        }

        /// <summary>The end of the run: it rears up whole under the last foe and shatters.</summary>
        private void FinalBlow()
        {
            if (!OwnerOk)
            {
                return;
            }
            Vector2 at = _head;
            float r = (1.1f + 0.2f * _level) * Mathf.Lerp(1f, K, 0.5f);
            Choreo.Foes(A, at, r, _hit);
            for (int i = 0; i < _hit.Count; i++)
            {
                Actor u = _hit[i];
                long id = u.Sid();
                if (_finalIds.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                _finalIds.Add(id);
                float mult = _final;
                if (!HitIds.Contains(id))
                {
                    HitIds.Add(id);
                    if (Combat.Reach(M, u, true) != Contact.Hit)
                    {
                        continue;
                    }
                    mult += _mult;   // (not hit on the way: the whole of it now)
                }
                Combat.Hit(A, u, mult * cm, false, false, 0f, M);
                Vector2 away = u.current_position - at;
                Impact im = Impact.Of(Hurt.Launch, away.sqrMagnitude > 1e-4f ? away : _tan, 1.2f, 1.6f);
                im.Lift = _lift + 0.8f;
                im.Daze = 1f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.07f);
                if (!M.Ended)
                {
                    M.SetLink(Link.Launched, u);
                }
            }
            if (Dev.On) Dev.Trace("earth_dragon rears at the end of its way: " + _finalIds.Count + " caught");
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile, 0.1f);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Ruin.Blast(A, at, EarthKit.L(_level, 1.1f, 1.3f, 1.6f), _level >= 3 ? Scale.Heavy : Scale, A.current_position);
            Ruin.Quake(A, at, Scale.Heavy, 1.3f, K);
            if (_level >= 3)
            {
                Kit.Stop(at, 0.05f);
            }
            if (!Kit.Visible(at, 0.4f))
            {
                return;
            }
            Vector2 up = at + new Vector2(0f, 0.8f * K);
            if (_level >= 3)
            {
                Kit.ImpactFrame(up, Px, 0.45f * K, Hue.Pal, false, FxLayer.Behind);
            }
            ChoreoFx.Impact(up, Vector2.up, Px, K, 1.8f, Hue, FxLayer.Top, true);
            Kit.DustRing(at, Px, tile, 16, 56f, 0.6f, K);
            Kit.Debris(at, Px, tile, 12, K);
            Kit.Shockwave(at, Px, 4f, r / Px * 1.3f, 0.4f, Fx.WithA(Hue.Edge, 0.9f), 2);
            Kit.Crater(at, Px * K, tile, _level >= 2, 7f, false, Hue.Edge);
        }

        // ------------------------------------------------------------- drawing --

        private void DrawHead(bool vis)
        {
            float emerge;
            float pitch;
            if (_end)
            {
                // it rears up whole, holds a heartbeat, and shatters
                emerge = _quiet ? 0f : Beat.Apply(Ease.OutBack, Mathf.Clamp01(_endT / 0.12f));
                pitch = _quiet ? 20f : 70f;
                if (!_quiet && _endT >= 0.3f && _headP != null)
                {
                    Shatter();
                }
            }
            else
            {
                emerge = _breachT > 0f ? Mathf.Sin(Mathf.PI * (1f - _breachT / 0.26f)) : 0f;
                pitch = 55f * emerge;
            }
            if (!vis)
            {
                KillHead();
                return;
            }
            // the earth heaped over its back (a hump that runs with it)
            if (!_end)
            {
                if (_mound == null || !_mound.Alive)
                {
                    _mound = Fx.Spawn(ChoreoArt.Boulder(Mathf.RoundToInt((3f + 0.5f * _level) * K), 2), _head, Px, 1f, FxLayer.Behind, 7);
                    if (_mound != null)
                    {
                        _mound.Held = true;
                    }
                }
                if (_mound != null)
                {
                    _mound.Pos = _head;
                    _mound.SY = 0.45f + 0.1f * Mathf.Sin(Age * 30f);
                    _mound.Rot = Mathf.Sin(Age * 17f) * 6f;
                    _mound.Grad = null;
                    _mound.R.color = Fx.Darken(_stone, 0.08f);
                }
            }
            if (emerge <= 0.02f || _headP == null && _end && _endT >= 0.3f)
            {
                if (_headP != null)
                {
                    _headP.Kill();
                    _headP = null;
                }
                if (_eye != null)
                {
                    _eye.Kill();
                    _eye = null;
                }
                return;
            }
            int len = Mathf.RoundToInt((11f + 3f * _level) * K * (_end ? 1.3f : 1f));
            Sprite s = EarthKit.DragonHead(len);
            if (_headP == null || !_headP.Alive)
            {
                _headP = Fx.Spawn(s, _head, Px, 1f, FxLayer.Over, 8);
                if (_headP == null)
                {
                    return;
                }
                _headP.Held = true;
            }
            if ((object)_headP.R.sprite != s)
            {
                _headP.R.sprite = s;
                _headP.Frames[0] = s;
            }
            float face = _tan.x >= 0f ? 1f : -1f;
            float rot = face * pitch;
            _headP.Pos = _head - _tan * 0.15f * K;
            _headP.SX = face;
            _headP.SY = Mathf.Max(0.01f, emerge);
            _headP.Rot = rot;
            _headP.Grad = null;
            _headP.R.color = _stone;
            // the eye burns in the school's colour
            Rect rc = s.rect;
            Vector2 local = new Vector2((EarthKit.HeadEye.x - EarthKit.HeadPivot.x) * rc.width * face, (EarthKit.HeadEye.y - EarthKit.HeadPivot.y) * rc.height * _headP.SY) * Px;
            Vector2 eyeAt = _headP.Pos + Choreo.Rotate(local, rot);
            if (_eye == null || !_eye.Alive)
            {
                _eye = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(K)), 1f, 0.2f), eyeAt, Px, 1f, FxLayer.Over, 9);
                if (_eye != null)
                {
                    _eye.Held = true;
                }
            }
            if (_eye != null)
            {
                _eye.Pos = eyeAt;
                _eye.Grad = null;
                _eye.R.color = Fx.WithA(_level >= 2 ? Hue.Core : Color.Lerp(Hue.Core, Hue.Edge, 0.5f), Mathf.Clamp01(emerge * 1.5f));
            }
        }

        /// <summary>The reared head breaks into boulders.</summary>
        private void Shatter()
        {
            Vector2 at = _headP != null ? _headP.Pos : _head;
            KillHead();
            if (Kit.Visible(at, 0.4f))
            {
                EarthKit.Boulders(at + new Vector2(0f, 0.3f * K), Px, 6 + 2 * _level, K, _stone, 1.2f);
                Kit.Debris(at, Px, Combat.TileAt(at), 6, K, 0.6f * K);
            }
        }

        private void KillHead()
        {
            if (_headP != null && _headP.Alive)
            {
                _headP.Kill();
            }
            _headP = null;
            if (_eye != null && _eye.Alive)
            {
                _eye.Kill();
            }
            _eye = null;
            if (_mound != null && _mound.Alive)
            {
                _mound.Kill();
            }
            _mound = null;
        }

        public override void Kill()
        {
            KillHead();
            for (int i = 0; i < _spines.Count; i++)
            {
                if (_spines[i] != null && _spines[i].Alive)
                {
                    _spines[i].Kill();
                }
            }
            _spines.Clear();
            _spineT.Clear();
        }
    }
}
