using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PHANTOM BLADES (Thousand Blades, Grandmaster). It straightens and lifts its free hand: one by
    /// one, spectral swords flicker into being behind it, each with a glint, until a halo of them
    /// stands round its back like the rays of a sun, points outward, gently bobbing. Then the hand
    /// sweeps forward and they go, one after another: each turns its point on a foe and streaks at it,
    /// curving after it, and skewers it (a small crater where it stabs the ground through it). The
    /// halo empties sword by sword; the main foe gets the first and the last.
    /// The swords are shots: they can be dodged, caught, deflected or cut.
    /// I: 6 swords; II: 9; III: 12, the last one a great sword that bursts round its target.
    /// Damage per sword (x the user's damage): I 0.8, II 0.95, III 1.1 (the great sword x1.8).
    /// Distinct from Blade Rain (from the sky over an area) and Heaven's Gate: a halo at its back,
    /// aimed shots one after another.
    /// </summary>
    internal sealed class PhantomBladesTech : Technique
    {
        public override string Id => "phantom_blades";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Clone | Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 42f, 70f, 108f };
        public override float[] Cooldown => new[] { 15f, 16f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Launched || link == Link.Scattered || link == Link.Thrown;
        }

        public static int Swords(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 9 : 12;
        }

        public static float SwordMult(int level)
        {
            return level <= 1 ? 0.8f : level == 2 ? 0.95f : 1.1f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 10f : 11f;
        }

        private static readonly List<Actor> _pick = new List<Actor>(12);

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * k;
            _pick.Clear();
            Actor focus = c.Focus;
            if (focus != null && Choreo.Fair(c.Self, focus, out _, c.Forced) && c.Dist(focus) <= range + (c.Forced ? 4f : 0f))
            {
                _pick.Add(focus);
            }
            for (int i = 0; i < c.Enemies.Count && i < 16 && _pick.Count < Swords(level); i++)
            {
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                if (v == focus || !Choreo.Fair(c.Self, v, out _, c.Forced))
                {
                    continue;
                }
                _pick.Add(v);
            }
            int n = _pick.Count;
            if (n == 0)
            {
                return null;
            }
            int swords = Swords(level);
            float score = 0f;
            bool worth = false;
            for (int i = 0; i < n; i++)
            {
                Actor v = _pick[i];
                c.Allowed(v, out float cm);
                // the swords share themselves out (the first foe gets the extras)
                int got = swords / n + (i < swords % n ? 1 : 0);
                score += c.Value(v, c.Expected(v, SwordMult(level) * got), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || !Combat.Grounded(v))
                {
                    worth = true;
                }
            }
            if (!c.Forced && n < 2 && !worth && !c.Linked)
            {
                return null;   // a halo of swords is not for one plain foot soldier
            }
            // foes out of reach of the blade (archers, flyers) are what it answers best
            if (c.State(_pick[0]).Ranged || !Combat.Grounded(_pick[0]))
            {
                score *= 1.2f;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Actor first = _pick[0];
            Vector2 dir = first.current_position - c.Pos;
            return new Plan
            {
                Target = first,
                Point = first.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = score,
                Others = new List<Actor>(_pick),
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Halo m = new Halo(a, this, p);
            if (!m.Build(p.Others))
            {
                return false;
            }
            return m.Start();
        }

        /// <summary>The move: the halo forms sword by sword, then they are loosed one after another.</summary>
        private sealed class Halo : ChoreoMove
        {
            private readonly List<Actor> _foes = new List<Actor>(12);
            private readonly List<long> _ids = new List<long>(12);
            private readonly Particle[] _swords = new Particle[12];
            private readonly float[] _born = new float[12];
            private readonly float[] _ang = new float[12];
            private int _n, _shown, _loosed;
            private float _t;

            public Halo(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                // the swords do the reaching: nobody is held still across the field
                SetVictim(null);
                Face(Aim);
            }

            public bool Build(List<Actor> foes)
            {
                if (foes == null)
                {
                    return false;
                }
                for (int i = 0; i < foes.Count; i++)
                {
                    if (foes[i] != null)
                    {
                        _foes.Add(foes[i]);
                        _ids.Add(foes[i].Sid());
                    }
                }
                if (_foes.Count == 0)
                {
                    return false;
                }
                int level = Level;
                _n = Mathf.Min(12, Swords(level));
                // the halo: an arc behind and over its back, wider the more swords
                float span = _n <= 6 ? 150f : _n <= 9 ? 190f : 230f;
                for (int i = 0; i < _n; i++)
                {
                    float u = _n == 1 ? 0.5f : i / (float)(_n - 1);
                    _ang[i] = 90f + (u - 0.5f) * span;
                }
                Form show = Form.Blade | Form.Clone | Form.Projectile;
                // the halo forms, sword by sword
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.42f + 0.02f * _n,
                    To = new Shape(-6f, 0.94f, 1.08f, 0f, -0.02f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Halo)mm).EnterHalo(),
                    Every = (mm, k) => ((Halo)mm).Form_(k)
                });
                // the hand sweeps forward: they go, one after another
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.07f * _n + 0.08f,
                    To = new Shape(12f, 1.12f, 0.92f, 0f, 0.06f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = show,
                    Every = (mm, k) => ((Halo)mm).Launch(k)
                });
                Hold(0.08f, false, Tempo.Follow);
                Recover(0.28f);
                return true;
            }

            private Vector2 Center()
            {
                return BodyPos() + new Vector2(-Fwd * 1.5f, 2.5f) * Px;
            }

            private float RingR()
            {
                return (8f + _n * 0.35f) * Mathf.Lerp(1f, Kk, 0.6f);
            }

            private int SwordLen()
            {
                return Mathf.Clamp(Mathf.RoundToInt(9f * Mathf.Lerp(1f, Kk, 0.6f)), 6, 40);
            }

            /// <summary>Where sword 'i' stands in the halo: its tip, pointing outward; 'rot' its turn.</summary>
            private Vector2 Slot(int i, out float rot, float bob)
            {
                float a = _ang[i];
                // mirrored to its back when it faces left
                float wa = (Fwd > 0f ? 180f - a : a) * Mathf.Deg2Rad;
                Vector2 u = new Vector2(Mathf.Cos(wa), Mathf.Sin(wa));
                float r = RingR() + SwordLen() + bob;
                rot = wa * Mathf.Rad2Deg + 90f;
                return Center() + u * r * Px;
            }

            private void EnterHalo()
            {
                _t = 0f;
                _shown = 0;
                Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            }

            private void Form_(float k)
            {
                _t += Clock.Dt;
                // one sword after another flickers into being
                int want = Mathf.Min(_n, Mathf.CeilToInt(_n * Mathf.Clamp01(k / 0.75f)));
                while (_shown < want)
                {
                    int i = _shown++;
                    _born[i] = _t;
                    Vector2 at = Slot(i, out float rot, 0f);
                    Particle p = Fx.Spawn(ChoreoArt.LightSword(SwordLen()), at, Px, 1f, FxLayer.Behind, 9);
                    if (p != null)
                    {
                        Glue(p);
                        p.Rot = rot;
                        _swords[i] = p;
                    }
                    if (Vis)
                    {
                        Fx.Glint(at, Px, 0.14f, Color.white, 0.8f);
                    }
                    Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.05f);
                }
                Hover(1f);
                // its free hand raised, light gathering in it
                if (Vis && Random.value < 0.4f)
                {
                    ChoreoFx.Motes(Center(), Px, 1, RingR() + 6f, 0.14f, Hue.Core, Hue.Edge, FxLayer.Over);
                }
            }

            /// <summary>The swords still in the halo bob and shimmer, following its back.</summary>
            private void Hover(float alpha)
            {
                for (int i = 0; i < _n; i++)
                {
                    Particle p = _swords[i];
                    if (p == null || !p.Alive)
                    {
                        continue;
                    }
                    float age = _t - _born[i];
                    float grow = Mathf.Clamp01(age / 0.09f);
                    float bob = Mathf.Sin(_t * 6f + i * 0.9f) * 0.8f;
                    p.Pos = Slot(i, out float rot, bob);
                    p.Rot = rot;
                    p.Size0 = p.Size1 = Mathf.Lerp(0.3f, 1f, Beat.Apply(Ease.OutBack, grow));
                    p.Grad = null;
                    float shine = 0.75f + 0.2f * Mathf.Sin(_t * 9f + i * 1.3f);
                    p.R.color = Fx.WithA(Color.Lerp(Hue.Main, Hue.Core, shine), 0.9f * alpha);
                    p.Cine = Cinematic;
                }
            }

            private void Launch(float k)
            {
                _t += Clock.Dt;
                int want = Mathf.Min(_n, Mathf.FloorToInt(_n * Mathf.Clamp01(k / 0.92f)) + 1);
                while (_loosed < want)
                {
                    Loose(_loosed++);
                }
                Hover(1f);
            }

            /// <summary>The foe sword 'i' goes for: the main one first and last, the others in turn.</summary>
            private Actor TargetFor(int i)
            {
                int n = _foes.Count;
                int pick = i == _n - 1 ? 0 : i % n;
                for (int j = 0; j < n; j++)
                {
                    int q = (pick + j) % n;
                    Actor v = _foes[q];
                    if (v != null && v.Sid() == _ids[q] && Choreo.Fair(A, v, out _, true))
                    {
                        return v;
                    }
                }
                return null;
            }

            private void Loose(int i)
            {
                Particle p = _swords[i];
                Vector2 from = p != null && p.Alive ? p.Pos : Slot(i, out _, 0f);
                if (p != null)
                {
                    Unglue(p);
                    _swords[i] = null;
                }
                Actor v = TargetFor(i);
                int level = Level;
                bool great = level >= 3 && i == _n - 1;
                Vector2 dir = v != null ? Choreo.BodyAt(v, 0.45f) - from : Aim;
                ShotSpec s = new ShotSpec
                {
                    Kind = ShotKind.LightSword,
                    Flight = Flight.Homing,
                    Turn = 7f,
                    Speed = 22f + 2f * level,
                    Range = Range(level) * Mathf.Lerp(1f, Kk, 0.5f) + 5f,
                    Size = great ? 1.8f : 1.05f + 0.1f * level,
                    Mult = SwordMult(level) * (great ? 1.8f : 1f),
                    Hurt = great ? Sakuga.Hurt.Launch : Sakuga.Hurt.Fold,
                    Push = great ? 2f : 0.5f,
                    Crater = great ? 0.9f : 0.35f,
                    Area = great ? 1.3f : 0f,
                    AreaMult = 0.5f
                };
                Shots.Fire(A, this, from, dir, v, Vector2.zero, s, Hue);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile, 0.04f);
                if (Vis)
                {
                    Kit.Flash(from, Px, 2, 0.05f, Color.white, FxLayer.Top, 22);
                    if (dir.sqrMagnitude > 1e-4f)
                    {
                        Kit.SpeedLines(from, dir, Px, 2, 2f, 0.1f, Fx.WithA(Hue.Edge, 0.8f));
                    }
                }
            }
        }
    }
}
