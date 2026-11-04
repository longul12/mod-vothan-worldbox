using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The Primordial's own powers (only a Primordial can use them: Technique.PrimordialOnly). They end
    /// Gods, but they leave the world standing; the world-ending ones are only for a battle between two
    /// Primordials (WorldsEnd). Anything below a God that they touch dies; a God loses a great share
    /// of its life (its Last Light may yet save it once).
    ///   Erasure                 a sphere of the void closes on one foe and collapses (a God: 55%)
    ///   Annihilation Lance      a beam of first light 26 tiles long through a line of foes (Gods 45%)
    ///   Genesis Wave            a ring of prismatic light 18 tiles out from it (Gods 35%)
    ///   Moonfall                a moon dragged down onto a crowd (Gods 50%; the land round it a crater)
    ///   Judgement of the First  the world stops; it is beside every God and great foe around it in turn,
    ///                           one strike each (Gods 45%)
    /// </summary>
    internal abstract class PrimordialTech : Technique
    {
        public override Tier Entry => Tier.God;
        public override Scale Scale => Scale.Ultimate;
        public override bool PrimordialOnly => true;

        /// <summary>Is 'v' a foe worth a Primordial's power (a God, a great fighter, a titan of a beast)?</summary>
        protected static bool Great(Actor v)
        {
            return Tiers.Of(v) >= Tier.Master || Weights.Species(v) >= Weight.Titan;
        }

        /// <summary>How much ending 'v' is worth to the Brain.</summary>
        protected static float Worth(Ctx c, Actor v)
        {
            return Tiers.Of(v) >= Tier.God ? 6000f : Tiers.Of(v) >= Tier.Grandmaster ? 1500f : 60f + 300f * Mathf.Clamp(c.Threat(v), 0f, 3f);
        }

        protected static bool Ready(Ctx c)
        {
            return c.Self != null && Primordials.Is(c.Self) && !WorldsEnd.Holds(c.Self);
        }
    }

    // ===================================================================== ERASURE ==

    internal sealed class ErasureTech : PrimordialTech
    {
        public override string Id => "primordial_erasure";
        public override Role Role => Role.Single;
        public override Form Forms => Form.Ultimate | Form.Ki | Form.Charge;
        public override float[] ManaCost => new[] { 200f, 200f, 200f };
        public override float[] Cooldown => new[] { 30f, 30f, 30f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            Actor best = null;
            float bw = 0f;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 16f; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v) || !c.Forced && !Great(v))
                {
                    continue;
                }
                float w = Worth(c, v);
                if (w > bw)
                {
                    bw = w;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 d = best.current_position - c.Pos;
            return new Plan { Target = best, Point = best.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1, Score = bw };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ErasureMove m = new ErasureMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class ErasureMove : ChoreoMove
    {
        public ErasureMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.6f,
                To = new Shape(-10f, 0.95f, 1.12f),
                Ease = Ease.OutCubic,
                Track = false,
                Every = (m, k) => ((ErasureMove)m).Close(k)
            });
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.2f,
                To = new Shape(12f, 1.1f, 0.92f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Track = false,
                Keep = true,
                Enter = m => ((ErasureMove)m).Collapse()
            });
            Recover(0.3f);
        }

        /// <summary>The void closes on it: a black sphere round it, a prismatic ring tightening.</summary>
        private void Close(float k)
        {
            if (V == null || !Vis)
            {
                return;
            }
            Vector2 at = Choreo.BodyAt(V, 0.5f);
            float r = Mathf.Lerp(3.2f, 1.1f, k) * Kk;
            Spec.Blob(at, r, Px, 0.05f, FxLayer.Top, 22, 1f, 0.35f, Fx.WithA(Primordials.Void, 0.55f + 0.35f * k));
            if (Random.value < 0.5f)
            {
                Fx.Ring(at, Px, 0.12f, r / Px * 1.3f, r / Px * 0.9f, 1, 1, FxLayer.Top, 23, 1f, 1.4f, Fx.WithA(Primordials.Prism(), 0.9f), Fx.WithA(Primordials.Prism(0.4f), 0f));
            }
            PrimFx.Beam(Hand(Limb.Palm), at, 2.5f * Px, 0.05f, FxLayer.Top, 21, false, Primordials.Prism(0.2f), Primordials.Prism(0.6f), false);
        }

        /// <summary>...and collapses to nothing with whatever was inside.</summary>
        private void Collapse()
        {
            if (V == null)
            {
                return;
            }
            Vector2 at = Choreo.BodyAt(V, 0.5f);
            if (Vis)
            {
                Kit.Flash(at, Px, Mathf.RoundToInt(10f * Kk), 0.12f, Color.white);
                Spec.Blob(at, 2.4f * Kk, Px, 0.5f, FxLayer.Top, 24, 1f, 0.4f, Color.white, Fx.WithA(Primordials.Prism(), 0.7f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.2f, 1.4f);
                Fx.Ring(at, Px, 0.5f, 2f, 28f * Kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Color.white, Fx.WithA(Primordials.Prism(0.5f), 0f));
                Primordials.Flash(Fx.WithA(Color.white, 0.35f), 0.15f);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", Combat.TileAt(V.current_position));
            Kit.Ravage(V.current_position, 2.2f, 3, A, false);
            Primordials.Doom(A, V, 0.55f);
        }
    }

    // ========================================================== ANNIHILATION LANCE ==

    internal sealed class LanceTech : PrimordialTech
    {
        public override string Id => "primordial_lance";
        public override Role Role => Role.Line;
        public override Form Forms => Form.Ultimate | Form.Beam | Form.Line | Form.Charge;
        public override float[] ManaCost => new[] { 250f, 250f, 250f };
        public override float[] Cooldown => new[] { 26f, 26f, 26f };
        public const float Len = 26f, Width = 1.6f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c) || c.Enemies.Count == 0)
            {
                return null;
            }
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 8 && c.EnemyDist[i] <= Len; i++)
            {
                Vector2 dir = (c.Enemies[i].current_position - c.Pos).normalized;
                float score = 0f;
                int n = 0;
                for (int j = 0; j < c.Enemies.Count && c.EnemyDist[j] <= Len; j++)
                {
                    Actor v = c.Enemies[j];
                    Vector2 rel = v.current_position - c.Pos;
                    float along = Vector2.Dot(rel, dir);
                    if (along < 0f || along > Len || Mathf.Abs(Vector2.Dot(rel, new Vector2(-dir.y, dir.x))) > Width || Primordials.Is(v))
                    {
                        continue;
                    }
                    n++;
                    score += Worth(c, v);
                }
                if (!c.Forced && n < 4 && score < 2000f)
                {
                    continue;
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = c.Enemies[i], Point = c.Pos + dir * Len, Dir = dir, Count = n, Score = score };
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            LanceMove m = new LanceMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class LanceMove : ChoreoMove
    {
        private readonly Vector2 _dir;
        private readonly HashSet<long> _hit = new HashSet<long>();
        private static readonly List<Actor> _scan = new List<Actor>();

        public LanceMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _dir = p.Dir;
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.7f,
                To = new Shape(-12f, 1.08f, 0.94f, 0f, -0.06f),
                Ease = Ease.OutCubic,
                Track = false,
                Charging = true,
                Every = (m, k) => ((LanceMove)m).Gather(k)
            });
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.55f,
                To = new Shape(16f, 1.14f, 0.9f, 0f, 0.12f),
                Ease = Ease.OutExpo,
                Track = false,
                Keep = true,
                Enter = m => ((LanceMove)m).Fire(),
                Every = (m, k) => ((LanceMove)m).Burn(k)
            });
            Recover(0.3f);
        }

        private Vector2 Muzzle => Hand(Limb.Palm);

        private void Gather(float k)
        {
            if (!Vis)
            {
                return;
            }
            Spec.Blob(Muzzle, (0.3f + 1.2f * k) * Kk, Px, 0.05f, FxLayer.Top, 24, 1f, 0.3f, Color.white, Fx.WithA(Primordials.Void, 0.8f));
            Spec.Blob(Muzzle, (0.6f + 2f * k) * Kk, Px, 0.05f, FxLayer.Top, 23, 1f, 0.6f, Fx.WithA(Primordials.Prism(), 0.5f));
            ChoreoFx.Motes(Muzzle, Px, 2, 20f * Kk, 0.15f, Color.white, Primordials.Prism(), FxLayer.Top);
        }

        private void Fire()
        {
            Vector2 from = Muzzle, to = from + _dir * LanceTech.Len;
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.4f), 0.15f);
                Kit.Shake(from, 0.4f, 0.012f);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", A.current_tile);
            Kit.Trench(Pose.Ground + _dir * 0.8f, Combat.Clamp(Pose.Ground + _dir * LanceTech.Len), 1.6f, 4, A, true);
        }

        private void Burn(float k)
        {
            Vector2 from = Muzzle, to = from + _dir * LanceTech.Len;
            if (Vis || Kit.Visible(to, 1f))
            {
                float w = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.3f));
                int t = 1 + Mathf.RoundToInt(5f * w);
                PrimFx.Beam(from, to, (t + 3) * Px * 1.4f, 0.05f, FxLayer.Top, 21, false, Primordials.Prism(), Primordials.Prism(0.4f));
            }
            // everything in its path
            Primordials.Around(A, Pose.Ground + _dir * LanceTech.Len * 0.5f, LanceTech.Len * 0.55f, false, _scan);
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            foreach (Actor v in _scan)
            {
                Vector2 rel = v.current_position - Pose.Ground;
                float along = Vector2.Dot(rel, _dir);
                if (along < 0f || along > LanceTech.Len || Mathf.Abs(Vector2.Dot(rel, side)) > LanceTech.Width || !_hit.Add(v.Sid()))
                {
                    continue;
                }
                Primordials.Doom(A, v, 0.45f);
            }
        }
    }

    // ================================================================ GENESIS WAVE ==

    internal sealed class GenesisWaveTech : PrimordialTech
    {
        public override string Id => "primordial_genesis_wave";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Area | Form.Ki;
        public override float[] ManaCost => new[] { 300f, 300f, 300f };
        public override float[] Cooldown => new[] { 40f, 40f, 40f };
        public const float Radius = 18f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            int n = 0, gods = 0;
            float score = 0f;
            Actor near = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= Radius; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v))
                {
                    continue;
                }
                near = near ?? v;
                n++;
                if (Tiers.Of(v) >= Tier.God)
                {
                    gods++;
                }
                score += Worth(c, v);
            }
            if (near == null || !c.Forced && n < 7 && gods < 2)
            {
                return null;
            }
            Vector2 d = near.current_position - c.Pos;
            return new Plan { Target = near, Point = c.Pos, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = n, Score = score, AreaRadius = Radius };
        }

        public override bool Begin(Actor a, Plan p)
        {
            GenesisMove m = new GenesisMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class GenesisMove : ChoreoMove
    {
        private readonly HashSet<long> _hit = new HashSet<long>();
        private static readonly List<Actor> _scan = new List<Actor>();
        private Vector2 _at;
        private float _ravaged;

        public GenesisMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.5f,
                To = new Shape(-4f, 0.92f, 1.14f, 0.6f),
                Ease = Ease.OutCubic,
                Track = false,
                Every = (m, k) => ((GenesisMove)m).Swell(k)
            });
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.95f,
                To = new Shape(0f, 1.12f, 0.9f, 0.6f),
                Ease = Ease.OutQuad,
                Track = false,
                Keep = true,
                Enter = m => ((GenesisMove)m).Release(),
                Every = (m, k) => ((GenesisMove)m).Wave(k)
            });
            Recover(0.3f);
        }

        private void Swell(float k)
        {
            if (!Vis)
            {
                return;
            }
            Vector2 c = BodyPos();
            Spec.Blob(c, (0.5f + 1.8f * k) * Kk, Px, 0.05f, FxLayer.Top, 24, 1f, 0.5f, Fx.WithA(Color.white, 0.6f + 0.3f * k), Fx.WithA(Primordials.Prism(), 0.5f));
            ChoreoFx.Motes(c, Px, 3, 30f * Kk, 0.2f, Color.white, Primordials.Prism(), FxLayer.Top);
        }

        private void Release()
        {
            _at = Pose.Ground;
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Primordials.Prism(), 0.45f), 0.25f);
                Spec.Blob(BodyPos(), 3f * Kk, Px, 0.4f, FxLayer.Top, 24, 1f, 0.5f, Color.white, Fx.WithA(Primordials.Prism(), 0.5f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.3f, 1.4f);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", A.current_tile);
        }

        private void Wave(float k)
        {
            float r = GenesisWaveTech.Radius * Anim.OutQuad(k);
            if (Vis && Random.value < 0.6f)
            {
                Fx.Ring(_at, Px, 0.14f, r / Px, r / Px, 3, 3, FxLayer.Ground, 6, Art.Ground, 1f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Primordials.Prism(k), 0.6f), Fx.WithA(Primordials.Prism(k + 0.3f), 0f));
            }
            // the land under the front is scorched and broken as it passes
            while (_ravaged < r)
            {
                _ravaged += 2.5f;
                for (int i = 0; i < 6; i++)
                {
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    Kit.Ravage(_at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * _ravaged, 2f, 3, A, false);
                }
            }
            Primordials.Around(A, _at, r, false, _scan);
            foreach (Actor v in _scan)
            {
                if (_hit.Add(v.Sid()))
                {
                    Primordials.Doom(A, v, 0.35f);
                }
            }
        }
    }

    // ==================================================================== MOONFALL ==

    internal sealed class MoonfallTech : PrimordialTech
    {
        public override string Id => "primordial_moonfall";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Area;
        public override float[] ManaCost => new[] { 350f, 350f, 350f };
        public override float[] Cooldown => new[] { 50f, 50f, 50f };
        public const float Radius = 12f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c) || c.Enemies.Count == 0)
            {
                return null;
            }
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 12 && c.EnemyDist[i] <= 24f; i++)
            {
                Vector2 at = c.Enemies[i].current_position;
                if ((at - c.Pos).sqrMagnitude < 8f * 8f)
                {
                    continue;   // (it doesn't drop a moon on its own head)
                }
                float score = 0f;
                int n = 0;
                for (int j = 0; j < c.Enemies.Count; j++)
                {
                    Actor v = c.Enemies[j];
                    if (!Primordials.Is(v) && (v.current_position - at).sqrMagnitude <= Radius * Radius)
                    {
                        n++;
                        score += Worth(c, v);
                    }
                }
                if (!c.Forced && n < 6 && score < 5000f)
                {
                    continue;
                }
                if (best == null || score > best.Score)
                {
                    Vector2 d = at - c.Pos;
                    best = new Plan { Target = c.Enemies[i], Point = at, Dir = d.normalized, Count = n, Score = score, AreaRadius = Radius };
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            MoonfallMove m = new MoonfallMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class MoonfallMove : ChoreoMove
    {
        private readonly Vector2 _at;
        private static readonly List<Actor> _scan = new List<Actor>();

        public MoonfallMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _at = p.Point;
            SetVictim(null);
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 2.1f,
                To = new Shape(-16f, 0.92f, 1.16f),
                Ease = Ease.OutCubic,
                Track = false,
                Every = (m, k) => ((MoonfallMove)m).Fall(k)
            });
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.3f,
                To = new Shape(8f, 1.06f, 0.95f),
                Ease = Ease.OutQuad,
                Track = false,
                Keep = true,
                Enter = m => ((MoonfallMove)m).Land()
            });
            Recover(0.4f);
        }

        /// <summary>A crater on the moon: its floor in shadow, its rim lit from the upper left.</summary>
        private void Crater(Vector2 at, float r)
        {
            Spec.Blob(at + new Vector2(-0.12f, 0.12f) * r, r, Px, 0.05f, FxLayer.Top, 26, 1f, 0.2f, new Color(0.88f, 0.88f, 0.93f, 1f));
            Spec.Blob(at, r * 0.9f, Px, 0.05f, FxLayer.Top, 27, 1f, 0.25f, new Color(0.46f, 0.46f, 0.54f, 1f));
            Spec.Blob(at + new Vector2(0.15f, -0.15f) * r, r * 0.55f, Px, 0.05f, FxLayer.Top, 28, 1f, 0.3f, new Color(0.34f, 0.33f, 0.42f, 1f));
        }

        /// <summary>A moon, dragged out of the night, falling on them: its shadow first, then the moon itself.</summary>
        private void Fall(float k)
        {
            float kk = Anim.InQuad(k);
            Vector2 p = _at + new Vector2(-10f * (1f - kk), 34f * (1f - kk) + 1f) * Kk;
            float r = 6f * Kk;
            bool vis = Kit.Visible(_at, 2f) || Kit.Visible(p, 3f);
            if (!vis)
            {
                return;
            }
            Spec.Blob(_at, MoonfallTech.Radius * 0.7f * k, Px, 0.05f, FxLayer.Ground, 3, Art.Ground, 0.4f, Fx.WithA(Color.black, 0.2f + 0.4f * k));
            // the moon: a lit face, a shadowed side, craters with their rims caught by the light, a halo of the first light
            Spec.Blob(p, r * 1.3f, Px, 0.05f, FxLayer.Top, 24, 1f, 0.8f, Fx.WithA(Primordials.Prism(), 0.35f));
            Spec.Blob(p, r, Px, 0.05f, FxLayer.Top, 25, 1f, 0.08f, new Color(0.74f, 0.74f, 0.8f, 1f));
            Spec.Blob(p + new Vector2(-0.14f, 0.14f) * r, r * 0.82f, Px, 0.05f, FxLayer.Top, 25, 1f, 0.35f, Fx.WithA(new Color(0.95f, 0.95f, 1f, 1f), 0.45f));
            Spec.Blob(p + new Vector2(0.24f, -0.22f) * r, r * 0.72f, Px, 0.05f, FxLayer.Top, 25, 1f, 0.45f, Fx.WithA(new Color(0.2f, 0.18f, 0.32f, 1f), 0.5f));
            Crater(p + new Vector2(-0.3f, 0.25f) * r, r * 0.3f);
            Crater(p + new Vector2(0.35f, -0.2f) * r, r * 0.2f);
            Crater(p + new Vector2(0.1f, 0.42f) * r, r * 0.14f);
            Crater(p + new Vector2(-0.42f, -0.3f) * r, r * 0.12f);
            Crater(p + new Vector2(0.05f, -0.05f) * r, r * 0.09f);
            if (k > 0.25f)
            {
                // it burns as it comes down: a trail of fire behind it, embers and rock shed off it
                Vector2 back = p + new Vector2(-10f, 34f).normalized * r * (1.2f + 1.5f * k);
                DuelFx.Comet(back, p + new Vector2(-10f, 34f).normalized * r * 0.6f, Px * 2.5f, new Color(1f, 0.92f, 0.75f, 1f), new Color(1f, 0.45f, 0.15f, 1f));
                for (int i = 0; i < 2; i++)
                {
                    Kit.Ember(p + Random.insideUnitCircle * r, Px, Random.Range(0.4f, 0.7f), new Vector2(Random.Range(-15f, 5f), Random.Range(15f, 35f)) * Px, Random.value < 0.5f ? new Color(1f, 0.6f, 0.3f, 1f) : Primordials.Prism(Random.value));
                }
                if (Random.value < 0.3f)
                {
                    Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], p + Random.insideUnitCircle * r, Px * Random.Range(1.5f, 3f), Random.Range(0.6f, 1f), FxLayer.Top, 24);
                    c?.Moving(new Vector2(Random.Range(-10f, 10f), Random.Range(10f, 25f)) * Px, 0f, 0.5f).Color(new Color(0.6f, 0.6f, 0.65f, 1f), Fx.WithA(new Color(1f, 0.5f, 0.2f, 1f), 0f));
                }
            }
        }

        private void Land()
        {
            WorldTile t = Combat.TileAt(Combat.Clamp(_at));
            if (Kit.Visible(_at, 2f))
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.75f), 0.35f);
                Spec.Blob(_at, MoonfallTech.Radius * 0.6f, Px, 0.8f, FxLayer.Top, 25, 1f, 0.5f, Color.white, new Color(0.9f, 0.9f, 1f, 1f), Fx.WithA(Primordials.Prism(), 0.5f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.3f, 1.4f);
                Fx.Ring(_at, Px, 0.8f, 2f, MoonfallTech.Radius / Px * 2f, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Color.white, Fx.WithA(Primordials.Prism(0.4f), 0.5f), Fx.WithA(Primordials.Void, 0f));
                Spec.Rocks(_at, Px, 40, 140f, Kk);
                Kit.Shake(_at, 0.7f, 0.02f);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", t);
            if (t != null && Settings.Destruction > 0)
            {
                MapAction.damageWorld(t, (int)MoonfallTech.Radius, TerraformLibrary.atomic_bomb, A);
            }
            Primordials.Around(A, _at, MoonfallTech.Radius, false, _scan);
            foreach (Actor v in _scan)
            {
                Primordials.Doom(A, v, 0.5f);
            }
        }
    }

    // ===================================================== JUDGEMENT OF THE FIRST ==

    internal sealed class JudgementTech : PrimordialTech
    {
        public override string Id => "primordial_judgement";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Teleport | Form.Fast;
        public override float[] ManaCost => new[] { 400f, 400f, 400f };
        public override float[] Cooldown => new[] { 70f, 70f, 70f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            int great = 0;
            float score = 0f;
            Actor first = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 26f; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v) || Tiers.Of(v) < Tier.Grandmaster)
                {
                    continue;
                }
                great++;
                first = first ?? v;
                score += Worth(c, v);
            }
            if (first == null || !c.Forced && great < 2)
            {
                return null;
            }
            Vector2 d = first.current_position - c.Pos;
            return new Plan { Target = first, Point = c.Pos, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = great, Score = score * 1.3f };
        }

        public override bool Begin(Actor a, Plan p)
        {
            JudgementMove m = new JudgementMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class JudgementMove : ChoreoMove
    {
        private readonly List<Actor> _targets = new List<Actor>();
        private int _n;

        public JudgementMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            Face(p.Dir);
        }

        public void Build()
        {
            OpenCinematic(6f, 0.55f, "sakuga_title_primordial_judgement", 0.25f, 0.8f);
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.3f,
                To = new Shape(0f, 0.95f, 1.1f),
                Ease = Ease.OutCubic,
                Track = false,
                Enter = m => ((JudgementMove)m).Choose()
            });
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 1.6f,
                To = new Shape(18f, 1.06f, 0.94f),
                Ease = Ease.OutQuad,
                Track = false,
                Keep = true,
                Intangible = true,
                Every = (m, k) => ((JudgementMove)m).Judge(k)
            });
            Recover(0.35f);
        }

        private void Choose()
        {
            _targets.Clear();
            foreach (Actor v in Primordials.Around(A, A.current_position, 26f, false, new List<Actor>()))
            {
                if (Tiers.Of(v) >= Tier.Grandmaster && _targets.Count < 6)
                {
                    _targets.Add(v);
                }
            }
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.4f), 0.2f);
            }
        }

        /// <summary>It is beside each of them in turn: one strike, a cross of light, and on to the next.</summary>
        private void Judge(float k)
        {
            int want = Mathf.Min(_targets.Count, Mathf.FloorToInt(k * (_targets.Count + 0.5f)));
            while (_n < want)
            {
                Actor v = _targets[_n];
                _n++;
                if (v == null || !v.isAlive())
                {
                    continue;
                }
                Vector2 at = Choreo.BodyAt(v, 0.55f);
                if (Kit.Visible(at, 0.5f))
                {
                    // its shape, there for a blink, then the cross of the strike
                    Sprite s = Fx.UnitSprite(A);
                    Particle g = s != null ? Fx.Spawn(s, v.current_position + new Vector2((Random.value < 0.5f ? -1f : 1f) * 0.6f * Kk, 0f), 1f, 0.35f, FxLayer.Top, 22, true) : null;
                    if (g != null)
                    {
                        g.SX = A.current_scale.x;
                        g.SY = A.current_scale.y;
                        g.Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(Primordials.Prism(), 0f));
                    }
                    float h = 1.2f * Kk;
                    PrimFx.Slash(at, 45f, h * 3.2f, 0.32f * Kk, 0.5f, FxLayer.Top, 24, false, Primordials.Prism());
                    PrimFx.Slash(at, -45f, h * 3.2f, 0.32f * Kk, 0.5f, FxLayer.Top, 24, false, Primordials.Prism(0.5f), 0.06f);
                    Kit.Flash(at, Px, Mathf.RoundToInt(7f * Kk), 0.08f, Color.white);
                }
                Choreo.Sound("event:/SFX/HIT/HitGeneric", Combat.TileAt(v.current_position));
                Kit.Ravage(v.current_position, 1.5f, 2, A, false);
                Primordials.Doom(A, v, 0.45f);
            }
        }
    }
}
