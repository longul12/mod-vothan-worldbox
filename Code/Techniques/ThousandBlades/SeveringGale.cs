using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SEVERING GALE (Thousand Blades, Grandmaster). It coils, the blade swept far back and the air
    /// drawn in round it, then whirls on the spot faster and faster, lifted off its feet by its own
    /// wind: every sweep of the blade throws off a crescent of cutting wind, and the crescents fly out
    /// in a spiral in every direction across 6-8 tiles, a storm of slashing wind scoring the ground
    /// (and the walls in its way) as they go. The last turn throws a whole ring of them at once.
    /// The blades are shots: they can be dodged, caught, deflected or cut.
    /// I: 16 blades + a ring of 8 over 6 tiles; II: 24 + 12 over 7; III: 36 + 16 over 8, piercing more.
    /// Damage per blade (x the user's damage): I 0.55, II 0.65, III 0.75 (a foe close by takes several).
    /// Distinct from Tornado Pillar (it pulls in) and Spinning Cut (adjacent only): it throws out.
    /// </summary>
    internal sealed class SeveringGaleTech : Technique
    {
        public override string Id => "severing_gale";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Area | Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 40f, 68f, 105f };
        public override float[] Cooldown => new[] { 15f, 16f, 17f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7f : 8f;
        }

        public static float BladeMult(int level)
        {
            return level <= 1 ? 0.55f : level == 2 ? 0.65f : 0.75f;
        }

        public static int Blades(int level)
        {
            return level <= 1 ? 16 : level == 2 ? 24 : 36;
        }

        public static int Ring(int level)
        {
            return level <= 1 ? 8 : level == 2 ? 12 : 16;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float r = Range(level) * k;
            float score = 0f;
            int n = 0;
            Actor nearest = null;
            for (int i = 0; i < c.Enemies.Count && i < 24; i++)
            {
                float d = c.EnemyDist[i];
                if (d > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!c.Allowed(v, out float cm) || !Combat.Grounded(v) || Moves.Locked(v))
                {
                    continue;
                }
                // the closer it stands the more blades cross it
                float hits = d < 2f ? 2.4f : d < 4f ? 1.6f : 1.1f;
                score += c.Value(v, c.Expected(v, BladeMult(level) * hits), cm);
                n++;
                nearest ??= v;
            }
            if (nearest == null)
            {
                return null;
            }
            int need = c.Linked || c.Surrounded ? 2 : 3;
            if (!c.Forced && n < need)
            {
                return null;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = nearest.current_position - c.Pos;
            return new Plan
            {
                Target = nearest,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
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
            Gale m = new Gale(a, this, p);
            m.Build();
            return m.Start();
        }

        /// <summary>The move: the coil, the whirling storm (blades loosed in a spiral), the last ring.</summary>
        private sealed class Gale : ChoreoMove
        {
            private Scars _scars;
            private Hue _blade;
            private int _loosed, _count;
            private float _turns, _baseFace, _ringT;

            public Gale(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                // it whirls where it stands: nobody is held still across the field
                SetVictim(null);
                Face(Aim);
            }

            public void Build()
            {
                int level = Level;
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                _count = Blades(level);
                _turns = level <= 1 ? 3f : level == 2 ? 4f : 5f;
                Form show = Form.Blade | Form.Area | Form.Projectile;
                // the coil: blade swept far back, the air drawn in
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.18f,
                    To = new Shape(-18f, 1.14f, 0.86f, 0f, -0.05f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = show,
                    Every = (mm, k) => ((Gale)mm).Coil(k)
                });
                // the storm
                float dur = level <= 1 ? 0.6f : level == 2 ? 0.72f : 0.86f;
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = dur,
                    To = new Shape(0f, 1f, 1.05f, level >= 2 ? 0.16f : 0.06f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = show | Form.Spin,
                    Enter = mm => ((Gale)mm).EnterStorm(),
                    Every = (mm, k) => ((Gale)mm).Storm(k),
                    Exit = mm => mm.Pose.FaceRight = mm.Fwd > 0f
                });
                // the last turn: a whole ring at once
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.08f,
                    To = new Shape(12f, 1.22f, 0.84f, 0f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Gale)mm).LastRing()
                });
                Hold(0.1f, false, Tempo.Follow);
                Recover(0.3f);
            }

            private void Coil(float k)
            {
                Vector2 back = BodyPos() + new Vector2(-Fwd * Hpx * 0.2f, 0f) * Px;
                TbHiArt.Blade(this, back, new Vector2(-Fwd, -0.2f), Hpx * 0.6f, _blade);
                if (Vis && Random.value < 0.5f)
                {
                    ChoreoFx.Motes(BodyPos(), Px, 2, 22f * Kk, 0.12f, Hue.Core, Hue.Edge, TopLayer);
                }
                if (k > 0.7f)
                {
                    Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
                }
            }

            private void EnterStorm()
            {
                _loosed = 0;
                _baseFace = Fwd;
                _ringT = 0f;
                _scars = Areas.Add(new Scars(this));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Vis)
                {
                    Kit.DustRing(Pose.Ground, Px, A.current_tile, 12, 40f, 0.5f, Kk);
                }
            }

            private float SpinAngle(float k)
            {
                // facing angle of the sweep (radians), steady and fast
                return Mathf.PI * 2f * _turns * k + (_baseFace > 0f ? 0f : Mathf.PI);
            }

            private void Storm(float k)
            {
                // it turns (a 2D turn: the sprite narrows, flips, widens)
                float t = _turns * k;
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                Pose.SX *= Mathf.Max(0.22f, Mathf.Abs(c));
                float frac = t - Mathf.Floor(t);
                bool back = frac > 0.25f && frac < 0.75f;
                Pose.FaceRight = back ? _baseFace < 0f : _baseFace > 0f;
                float ang = SpinAngle(k);
                Vector2 arm = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground);
                Vector2 body = BodyPos();
                TbHiArt.Blade(this, body, new Vector2(arm.x, arm.y * 0.6f), Hpx * 0.7f, _blade);
                // the blades come off the sweep, faster and faster
                int want = Mathf.Min(_count, Mathf.CeilToInt(_count * Mathf.Pow(k, 0.9f)));
                while (_loosed < want)
                {
                    int i = _loosed++;
                    float a = SpinAngle(i / (float)_count) + (i % 3 - 1) * 0.21f;
                    Loose(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), 1f, 0f);
                }
                if (!Vis)
                {
                    return;
                }
                _ringT -= Clock.Dt;
                if (_ringT <= 0f)
                {
                    _ringT = 0.07f;
                    // the whirlwind round it: rings spinning out, dust swirling
                    Fx.Ring(Pose.Ground, Px, 0.22f, 4f * Kk, (16f + 6f * Level) * Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.8f,
                        Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0.4f), Fx.WithA(Hue.Edge, 0f));
                    Particle r = Fx.Spawn(Art.Ring(Mathf.RoundToInt((7f + 3f * Mathf.Sin(BeatT * 20f)) * Kk), 1, 0.32f), body + new Vector2(0f, Random.Range(-2f, 3f) * Px), Px, 0.14f, FxLayer.Top, 9);
                    r?.Color(Fx.WithA(Hue.Core, 0.7f), Fx.WithA(Hue.Edge, 0f));
                    Vector2 g = Pose.Ground + Random.insideUnitCircle * 0.8f * Kk;
                    ChoreoFx.HeelDust(g, Choreo.Perp(g - Pose.Ground), Px, A.current_tile, Kk);
                }
            }

            /// <summary>One crescent of cutting wind off the blade along 'dir' (on the ground plane).</summary>
            private void Loose(Vector2 dir, float speedK, float delay)
            {
                int level = Level;
                Vector2 from = BodyPos() + new Vector2(dir.x * Hpx * 0.35f, 0f) * Px;
                ShotSpec s = new ShotSpec
                {
                    Kind = ShotKind.WindBlade,
                    Flight = level >= 3 && (_loosed & 1) == 0 ? Flight.Wave : Flight.Straight,
                    Speed = (15f + 2f * level) * speedK,
                    Range = Range(level),
                    Size = 1.1f + 0.2f * level,
                    Mult = BladeMult(level),
                    Hurt = Sakuga.Hurt.Reel,
                    Push = 0.55f,
                    Pierce = level,
                    Delay = delay
                };
                Shot shot = Shots.Fire(A, this, from, dir, null, Vector2.zero, s, Hue);
                _scars?.Follow(shot);
                if (Vis && (_loosed & 1) == 0)
                {
                    Kit.Line(from, from + dir * 5f * Px, Px, 0.06f, 1, FxLayer.Top, 10, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                if ((_loosed & 3) == 0)
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile, 0.04f);
                }
            }

            private void LastRing()
            {
                int n = Ring(Level);
                float a0 = Random.value * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + i * Mathf.PI * 2f / n;
                    Loose(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), 1.25f, 0f);
                }
                Choreo.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile, 0.05f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
                if (Vis)
                {
                    Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(6f * Kk), 0.07f, Color.white, TopLayer);
                    Kit.Shockwave(Pose.Ground, Px, 4f, Range(Level) * 0.7f / Px, 0.35f, Fx.WithA(Hue.Edge, 0.85f), 2);
                    Kit.DustRing(Pose.Ground, Px, A.current_tile, 16, 70f, 0.55f, Kk);
                }
                Ruin.Quake(A, Pose.Ground, Scale.Heavy, 0.9f, Kk);
                // the ground round its feet is scoured (its own feet kept)
                Ruin.Blast(A, Pose.Ground, 1.2f, Scale, Pose.Ground, false, -1);
                SetLink(Link.Scattered, null);
            }
        }

        /// <summary>
        /// The marks the gale's blades leave: a scratch in the ground every stride or so along each
        /// blade's path, and a real cut into any building a blade crosses (Grandmasters spare none).
        /// </summary>
        private sealed class Scars : Hazard
        {
            private readonly List<Shot> _shots = new List<Shot>(56);
            private readonly List<float> _at = new List<float>(56), _was = new List<float>(56);
            private readonly HashSet<Building> _cut = new HashSet<Building>();

            public Scars(ChoreoMove m) : base(m)
            {
            }

            public void Follow(Shot s)
            {
                if (s == null || _shots.Count >= 64)
                {
                    return;
                }
                _shots.Add(s);
                _at.Add(0.6f);
                _was.Add(-1f);
            }

            public override bool Tick(float dt)
            {
                if (dt <= 0f)
                {
                    return true;
                }
                Age += dt;
                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    Shot s = _shots[i];
                    bool gone = s == null || s.Done || s.Age > 0.05f && s.Traveled <= _was[i] || s.Age > 3f;
                    if (!gone && s.Age >= 0f)
                    {
                        _was[i] = s.Traveled;
                        if (s.Traveled >= _at[i] && OwnerOk)
                        {
                            _at[i] = s.Traveled + 1.4f;
                            Mark(s.Pos);
                        }
                    }
                    if (gone)
                    {
                        _shots.RemoveAt(i);
                        _at.RemoveAt(i);
                        _was.RemoveAt(i);
                    }
                }
                return _shots.Count > 0 && Age < 4f;
            }

            private void Mark(Vector2 p)
            {
                WorldTile t = Combat.TileAt(p);
                if (t == null || t.Type == null || t.Type.liquid)
                {
                    return;
                }
                Vector2 feet = A.current_position;
                if (t.hasBuilding() && t.building != null && t.building.isAlive() && !_cut.Contains(t.building))
                {
                    // a blade crossing a wall cuts into it
                    _cut.Add(t.building);
                    Ruin.Blast(A, p, 0.6f, Scale, feet, false);
                    return;
                }
                if ((p - feet).sqrMagnitude > 1.5f)
                {
                    Ruin.Blast(A, p, 0.35f, Scale, feet, false, -2);
                }
            }
        }
    }
}
