using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PRIMORDIAL ROAR (Divine, God; Scale Heavy). The God throws its head back and draws in the
    /// air (dust and motes rushing in to it), then roars: it thrusts forward trembling, and ring
    /// after ring of force bursts from its head and races out over the land. The front flattens
    /// everything it passes (grass torn flat in rings, trees and buildings battered: no fire) and
    /// every enemy it reaches is knocked flat and stunned where it falls (flyers rocked in the
    /// air); whoever was winding up a technique has it torn apart. Little damage: it is the answer
    /// to being mobbed and to a crowd of wind-ups (Answers Charge, Team), a God's way to clear the
    /// field and take a breath. Not a cinematic (Heavy): it plays at world speed.
    /// I: 7 tiles, stunned 1.1 s; II: 10 tiles, 1.6 s; III: 14 tiles, 2.2 s.
    /// Damage (x the user's damage) I 0.35, II 0.5, III 0.65, less at the edge.
    /// </summary>
    internal sealed class PrimordialRoarTech : Technique
    {
        public override string Id => "primordial_roar";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Head | Form.Area | Form.Heavy;
        public override Form Answers => Form.Charge | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 80f, 120f, 160f };
        public override float[] Cooldown => new[] { 60f, 70f, 80f };

        public static float Radius(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 10f : 14f;
        }

        public static float Stun(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.6f : 2.2f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.5f : 0.65f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int n = 0, charging = 0;
            float threat = 0f, score = 0f, best = -1f;
            Actor main = null;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                n++;
                float t = c.Threat(v);
                threat += t;
                if (t > best)
                {
                    best = t;
                    main = v;
                }
                // a little damage, and the breath it buys: everyone of them out of the fight for a while
                score += c.Value(v, c.Expected(v, Mult(level)), cm) + Stun(level) * 6f * Mathf.Clamp(Tiers.Power(v), 0.3f, 4f);
                if (Moves.Charging(v))
                {
                    charging++;
                    score += c.D * 2f + 10f * Tiers.Power(v);
                }
            }
            if (main == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                // mobbed, a crowd, or wind-ups to tear apart; never one goblin
                bool reason = n >= (level <= 1 ? 4 : 5) || c.Surrounded || charging > 0 && (Tiers.Of(main) >= Tier.Master || best >= 0.3f)
                    || (Tiers.Of(main) >= Tier.Master || best >= 0.4f) && n >= 2;
                if (!reason)
                {
                    return null;
                }
                if (c.Surrounded)
                {
                    score *= 1.2f;
                }
            }
            score *= DivineArts.Affinity(c.Self, Id);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 d = main.current_position - c.Pos;
            return new Plan
            {
                Target = main,
                Point = c.Pos,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = n,
                Score = score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            RoarMove m = new RoarMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Primordial Roar's move: the breath drawn in, the roar and its racing front, the settle.</summary>
    internal sealed class RoarMove : ChoreoMove
    {
        private readonly float _r, _speed;
        private float _lastT, _ringT, _front, _dustT;
        private int _waves;
        private readonly HashSet<long> _struck = new HashSet<long>();
        private static readonly List<Actor> _scan = new List<Actor>();

        public RoarMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _r = PrimordialRoarTech.Radius(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            _speed = _r / 0.45f;
            SetVictim(null);
            Face(p.Dir);
        }

        /// <summary>The mouth: high on the head, a little forward.</summary>
        private Vector2 Mouth => HeadPos() + new Vector2(Fwd * Hpx * 0.12f, -Hpx * 0.12f) * Px;

        public void Build()
        {
            // the breath drawn in: head thrown back, the chest swelling
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.34f,
                To = new Shape(-14f, 1.08f, 1.12f, 0f, -0.1f),
                Ease = Ease.OutCubic,
                Track = false,
                Forms = Form.Head | Form.Area,
                Enter = m => ((RoarMove)m).EnterInhale(),
                Every = (m, k) => ((RoarMove)m).TickInhale(k)
            });
            // the roar
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = Level <= 1 ? 0.6f : Level == 2 ? 0.75f : 0.95f,
                To = new Shape(16f, 1.16f, 0.9f, 0f, 0.12f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Head | Form.Area | Form.Heavy,
                Enter = m => ((RoarMove)m).EnterRoar(),
                Every = (m, k) => ((RoarMove)m).TickRoar(k)
            });
            // the last of it dies away
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.18f,
                To = new Shape(6f, 1.04f, 0.97f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Track = false
            });
            Recover(0.3f);
        }

        private float Dt()
        {
            float dt = BeatT - _lastT;
            if (dt < 0f)
            {
                dt = BeatT;
            }
            _lastT = BeatT;
            return dt;
        }

        private void EnterInhale()
        {
            _lastT = 0f;
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
        }

        private void TickInhale(float k)
        {
            float dt = Dt();
            if (!Vis)
            {
                return;
            }
            _dustT -= dt;
            if (_dustT <= 0f)
            {
                _dustT = 0.025f;
                Vector2 mouth = Mouth;
                int n = Fx.Count(3);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    float d = Random.Range(0.5f, 1f) * Mathf.Min(_r * 0.4f, 5f);
                    Vector2 from = mouth + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    float life = Random.Range(0.18f, 0.28f);
                    Particle p = Random.value < 0.5f
                        ? Fx.Spawn(Art.Dust[Random.Range(0, Art.Dust.Length)], from, Px, life, FxLayer.Top, 5)
                        : Fx.Spawn(Art.Lines[Random.Range(0, Art.Lines.Length)], from, Px, life, FxLayer.Top, 5);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving((mouth - from) / life);
                    p.Rot = a * Mathf.Rad2Deg - 90f;
                    Color c = Fx.Lighten(Fx.SoilColor(Combat.TileAt(from)), 0.4f);
                    p.Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.7f), Fx.WithA(Hue.Edge, 0.3f));
                }
            }
        }

        private void EnterRoar()
        {
            _lastT = 0f;
            _front = 0f;
            _ringT = 0f;
            _waves = 0;
            WorldTile t = A.current_tile;
            Choreo.Sound("event:/SFX/UNIQUE/Crabzilla/CrabzillaVoice", t, 0.3f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", t, 0.2f);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", t, 0.3f);
            Ruin.Quake(A, Pose.Ground, Scale, 1.6f, Kk);
            if (Vis)
            {
                Kit.Flash(Mouth, Px, Mathf.RoundToInt(6f * Kk), 0.1f, Fx.WithA(Color.white, 0.9f));
                Kit.DustRing(Pose.Ground, Px, t, 16, 70f, 0.7f, Kk * 1.3f);
            }
        }

        private void TickRoar(float k)
        {
            float dt = Dt();
            Pose.Nudge = Choreo.Tremble(BeatT, 1f);
            float was = _front;
            _front = Mathf.Min(_r, _front + _speed * dt);
            // the front: everyone it reaches is knocked flat and stunned
            if (_front > was || _front >= _r)
            {
                Reach(was);
            }
            // the land flattened in rings as the front passes
            while (_waves < 3 && _front >= _r * (_waves + 1) / 3f)
            {
                _waves++;
                float rad = _r * _waves / 3f;
                Ruin.Blast(A, Pose.Ground, rad / Mathf.Max(0.5f, Ruin.Radius(Tier, Scale, 1f)), Scale, Pose.Ground, false, -2);
            }
            if (!Vis && !Kit.Visible(Pose.Ground, 0.6f))
            {
                return;
            }
            _ringT -= dt;
            if (_ringT <= 0f && k < 0.85f)
            {
                _ringT = 0.07f;
                Vector2 mouth = Mouth;
                // rings of force bursting from its head, and the ground ring racing out
                Fx.Ring(mouth, Px, 0.3f, 3f, 26f * Kk + 6f * Level, 2, 1, FxLayer.Top, 8, 0.9f, 1.8f,
                    Fx.WithA(Color.white, 0.85f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(Pose.Ground, Px, 0.45f, 6f, Mathf.Min(140f, _r / Px), 3, 1, FxLayer.Ground, 6, Art.Ground, 1.4f,
                    Fx.WithA(Color.white, 0.75f), Fx.WithA(Hue.Edge, 0.45f), Fx.WithA(Hue.Rim, 0f));
                Kit.SpeedLines(mouth + Dir * 1.5f * Kk, Dir, Px, 3, 12f, 0.15f, Fx.WithA(Color.white, 0.8f));
            }
            _dustT -= dt;
            if (_dustT <= 0f && _front < _r)
            {
                _dustT = 0.03f;
                int n = Fx.Count(4);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 g = Pose.Ground + dir * _front;
                    if (Kit.Visible(g, 0.1f))
                    {
                        ChoreoFx.HeelDust(g, dir, Px, Combat.TileAt(g), Kk * 1.3f);
                    }
                }
            }
        }

        private void Reach(float from)
        {
            Choreo.Foes(A, Pose.Ground, _front + 0.4f, _scan, true, true);
            float mult = PrimordialRoarTech.Mult(Level);
            float stun = PrimordialRoarTech.Stun(Level);
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                long id = u.Sid();
                if (_struck.Contains(id))
                {
                    continue;
                }
                Vector2 rel = u.current_position - Pose.Ground;
                float d = rel.magnitude;
                if (d < from - 0.6f && from > 0.5f)
                {
                    continue;   // (inside the front already: it was taken care of)
                }
                _struck.Add(id);
                if (!Choreo.Fair(A, u, out _, true))
                {
                    continue;
                }
                float edge = Mathf.Clamp01(d / Mathf.Max(0.1f, _r));
                Vector2 away = d > 1e-3f ? rel / d : Dir;
                // a wind-up in its reach is torn apart
                if (Moves.Charging(u))
                {
                    Interrupts.Break(u, A, away, 2.2f);
                }
                Impact im = Impact.Of(Hurt.Knockdown, away, Mathf.Lerp(1.4f, 0.4f, edge), 1.2f);
                im.Daze = stun * Mathf.Lerp(1f, 0.7f, edge);
                HitResult r = DivineArts.Smite(this, u, mult * Mathf.Lerp(1f, 0.5f, edge), im);
                if (r.Landed)
                {
                    if (Kit.Visible(u.current_position, 0.2f))
                    {
                        Fx.Ring(Choreo.BodyAt(u, 0.6f), Choreo.PxOf(u), 0.2f, 2f, 9f, 1, 1, FxLayer.Top, 6, 1f, 2f,
                            Fx.WithA(Color.white, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    }
                }
            }
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            _struck.Clear();
        }
    }
}
