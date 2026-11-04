using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// Eight more of the Primordial's own powers (with the first five, thirteen: a Primordial's battle is
    /// never the same few blows). Each ends Gods and leaves the world standing.
    ///   Spectrum Cataclysm   seven beams, one of every colour, fanned from its hands and swept across
    ///                        the field
    ///   Supernova            a star forged in its palm and thrown: it bursts, falls in on itself, and
    ///                        bursts again bigger
    ///   Stillness of the First  time stops; it is beside every great foe in turn, its echoes left frozen
    ///                        mid-blow; time moves and every blow lands at once
    ///   Crushing Heaven      the sky presses down on them: rings of weight falling, the ground sinking
    ///                        into a crater under them
    ///   Chains of Creation   chains of light burst out of the ground, bind the greatest of them, pull
    ///                        taut and shatter them
    ///   Black Sun            a black sun rises over the field and rains black light on them
    ///   Legion of Echoes     it splits into six; each echo strikes a different foe and comes back
    ///   Worldbreaker         it leaps and strikes the ground: the land splits in a line thirty tiles
    ///                        long, lava and rock thrown up all along it
    /// </summary>
    internal static class PrimPow
    {
        private static readonly List<Actor> _scan = new List<Actor>();

        /// <summary>Its enemies within 'r' of 'at' (never another Primordial), the greatest first, at most 'max'.</summary>
        public static List<Actor> Foes(Actor a, Vector2 at, float r, int max)
        {
            List<Actor> l = new List<Actor>();
            Primordials.Around(a, at, r, false, _scan);
            foreach (Actor v in _scan)
            {
                if (v != null && v.isAlive() && !Primordials.Is(v))
                {
                    l.Add(v);
                }
            }
            l.Sort((x, y) => Tiers.Power(y).CompareTo(Tiers.Power(x)));
            if (l.Count > max)
            {
                l.RemoveRange(max, l.Count - max);
            }
            return l;
        }

        /// <summary>The best point for an area of 'r' tiles among its foes within 'reach' (score, count).</summary>
        public static Vector2 Cluster(Ctx c, float reach, float r, out float score, out int n, out int great)
        {
            score = 0f;
            n = great = 0;
            Vector2 best = c.Pos;
            for (int i = 0; i < c.Enemies.Count && i < 14 && c.EnemyDist[i] <= reach; i++)
            {
                Vector2 at = c.Enemies[i].current_position;
                float s = 0f;
                int k = 0, g = 0;
                for (int j = 0; j < c.Enemies.Count && c.EnemyDist[j] <= reach + r; j++)
                {
                    Actor v = c.Enemies[j];
                    if (Primordials.Is(v) || (v.current_position - at).sqrMagnitude > r * r)
                    {
                        continue;
                    }
                    k++;
                    if (Tiers.Of(v) >= Tier.Grandmaster)
                    {
                        g++;
                    }
                    s += Tiers.Of(v) >= Tier.God ? 6000f : Tiers.Of(v) >= Tier.Grandmaster ? 1500f : 60f;
                }
                if (s > score)
                {
                    score = s;
                    n = k;
                    great = g;
                    best = at;
                }
            }
            return best;
        }

        public static Plan Toward(Ctx c, Actor target, Vector2 point, float score, int n, float area = 0f)
        {
            Vector2 d = point - c.Pos;
            return new Plan { Target = target, Point = point, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = n, Score = score, AreaRadius = area };
        }

        /// <summary>A rainbow of the seven colours (0..6).</summary>
        public static Color Band7(int i)
        {
            return Color.HSVToRGB(Mathf.Repeat(i / 7f, 1f), 0.75f, 1f);
        }
    }

    // ======================================================== SPECTRUM CATACLYSM ==

    internal sealed class SpectrumTech : PrimordialTech
    {
        public override string Id => "primordial_spectrum";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Beam | Form.Area | Form.Charge;
        public override float[] ManaCost => new[] { 260f, 260f, 260f };
        public override float[] Cooldown => new[] { 34f, 34f, 34f };
        public const float Len = 24f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            Vector2 at = PrimPow.Cluster(c, Len - 4f, 10f, out float score, out int n, out int great);
            if (n == 0 || !c.Forced && n < 3 && great < 1)
            {
                return null;
            }
            return PrimPow.Toward(c, null, at, score * 1.1f, n);
        }

        public override bool Begin(Actor a, Plan p)
        {
            SpectrumMove m = new SpectrumMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class SpectrumMove : ChoreoMove
    {
        private readonly Vector2 _dir;
        private readonly HashSet<long> _hit = new HashSet<long>();
        private static readonly List<Actor> _scan = new List<Actor>();

        public SpectrumMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            _dir = p.Dir;
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.6f, To = new Shape(-14f, 0.92f, 1.14f, 0.4f), Ease = Ease.OutCubic, Track = false, Charging = true, Every = (m, k) => ((SpectrumMove)m).Gather(k) });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.3f, To = new Shape(10f, 1.1f, 0.94f, 0.4f, 0.1f), Ease = Ease.InOutSine, Track = false, Keep = true, Enter = m => ((SpectrumMove)m).Open(), Every = (m, k) => ((SpectrumMove)m).Sweep(k) });
            Recover(0.3f);
        }

        private void Gather(float k)
        {
            if (!Vis)
            {
                return;
            }
            Vector2 h = Hand(Limb.Palm);
            for (int i = 0; i < 7; i++)
            {
                float a = T * 8f + i * 0.897f;
                Vector2 o = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * (1.6f - k) * Kk;
                Spec.Blob(h + o, 0.28f * Kk, Px, 0.05f, FxLayer.Top, 24, 1f, 0.3f, Color.white, PrimPow.Band7(i));
            }
            PrimFx.Charge(h, (0.3f + 0.9f * k) * Kk, FxLayer.Top, 25, false, Primordials.Prism());
        }

        private void Open()
        {
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.4f), 0.15f);
                Kit.Flash(Hand(Limb.Palm), Px, Mathf.RoundToInt(10f * Kk), 0.12f, Color.white);
            }
            Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile);
        }

        private void Sweep(float k)
        {
            Vector2 from = Hand(Limb.Palm);
            float sweep = Mathf.Lerp(-40f, 40f, Anim.InOutSine(k));
            float fade = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.1f));
            for (int i = 0; i < 7; i++)
            {
                Vector2 d = Choreo.Rotate(_dir, sweep + (i - 3) * 7f);
                Vector2 to = from + d * SpectrumTech.Len;
                Color c = PrimPow.Band7(i);
                if (Vis || Kit.Visible(to, 1f))
                {
                    PrimFx.Beam(from, to, (0.25f + 0.25f * fade) * Kk, 0.05f, FxLayer.Top, 20 + i, false, c, Color.Lerp(c, Color.white, 0.4f), i == 3, i % 2 == 0);
                }
                // a streak burnt into the land under every beam
                if (Random.value < 0.35f)
                {
                    Kit.Ravage(Combat.Clamp(Pose.Ground + d * Random.Range(3f, SpectrumTech.Len)), 1.2f, 3, A, false);
                }
                Primordials.Around(A, Pose.Ground + d * SpectrumTech.Len * 0.5f, SpectrumTech.Len * 0.55f, false, _scan);
                Vector2 side = new Vector2(-d.y, d.x);
                foreach (Actor v in _scan)
                {
                    Vector2 rel = v.current_position - Pose.Ground;
                    float along = Vector2.Dot(rel, d);
                    if (along < 0f || along > SpectrumTech.Len || Mathf.Abs(Vector2.Dot(rel, side)) > 1.1f || !_hit.Add(v.Sid()))
                    {
                        continue;
                    }
                    Primordials.Doom(A, v, 0.35f);
                }
            }
        }
    }

    // ================================================================ SUPERNOVA ==

    internal sealed class SupernovaTech : PrimordialTech
    {
        public override string Id => "primordial_supernova";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Ki | Form.Area | Form.Projectile | Form.Charge;
        public override float[] ManaCost => new[] { 320f, 320f, 320f };
        public override float[] Cooldown => new[] { 45f, 45f, 45f };
        public const float Radius = 10f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            Vector2 at = PrimPow.Cluster(c, 22f, Radius, out float score, out int n, out int great);
            if (n == 0 || (at - c.Pos).sqrMagnitude < 6f * 6f || !c.Forced && n < 4 && great < 1)
            {
                return null;
            }
            return PrimPow.Toward(c, null, at, score * 1.2f, n, Radius);
        }

        public override bool Begin(Actor a, Plan p)
        {
            SupernovaMove m = new SupernovaMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class SupernovaMove : ChoreoMove
    {
        private readonly Vector2 _at;
        private Vector2 _star, _from;
        private bool _burst1, _burst2;
        private readonly HashSet<long> _hit = new HashSet<long>();

        public SupernovaMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            _at = p.Point;
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 1f, To = new Shape(-16f, 0.92f, 1.16f, 0.5f), Ease = Ease.OutCubic, Track = false, Charging = true, Every = (m, k) => ((SupernovaMove)m).Forge(k) });
            Add(new Beat { Tempo = Tempo.Act, Dur = 0.45f, To = new Shape(18f, 1.12f, 0.9f, 0.5f, 0.15f), Ease = Ease.OutExpo, Track = false, Keep = true, Enter = m => ((SupernovaMove)m)._from = ((SupernovaMove)m)._star, Every = (m, k) => ((SupernovaMove)m).Fly(k) });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.4f, To = new Shape(0f, 1f, 1f, 0.5f), Ease = Ease.OutQuad, Track = false, Keep = true, Every = (m, k) => ((SupernovaMove)m).Nova(k) });
            Recover(0.3f);
        }

        private void Forge(float k)
        {
            _star = Hand(Limb.Palm) + new Vector2(0f, (0.6f + 0.8f * k) * Kk);
            if (!Vis)
            {
                return;
            }
            Color gold = new Color(1f, 0.85f, 0.45f, 1f);
            PrimFx.Charge(_star, (0.3f + 1.3f * k) * Kk, FxLayer.Top, 25, false, gold);
            if (Random.value < 0.4f)
            {
                DuelFx.Starburst(_star, Px, (0.8f + 1.4f * k) * Kk, Color.white, gold, Primordials.Prism(), 0.06f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = T * 9f + i * 1.571f;
                Spec.Blob(_star + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f) * (1.6f + k) * Kk, 0.2f * Kk, Px, 0.05f, FxLayer.Top, 26, 1f, 0.2f, Color.white, Primordials.Prism(i * 0.25f));
            }
        }

        private void Fly(float k)
        {
            Vector2 to = _at + new Vector2(0f, 0.6f * Kk);
            Vector2 prev = _star;
            _star = Vector2.Lerp(_from, to, Anim.InQuad(k)) + new Vector2(0f, Mathf.Sin(Mathf.PI * k) * 3f * Kk);
            if (Vis || Kit.Visible(_star, 1f))
            {
                PrimFx.Charge(_star, 1.6f * Kk, FxLayer.Top, 25, false, new Color(1f, 0.85f, 0.45f, 1f));
                DuelFx.Comet(prev - (_star - prev) * 2f, _star, Px * 1.5f, Color.white, new Color(1f, 0.7f, 0.3f, 1f));
            }
        }

        private void Nova(float k)
        {
            Vector2 c = _at + new Vector2(0f, 0.6f * Kk);
            bool vis = Kit.Visible(c, 2f);
            if (k < 0.35f && !_burst1)
            {
                // the first burst
                _burst1 = true;
                if (vis)
                {
                    Primordials.Flash(Fx.WithA(Color.white, 0.7f), 0.3f);
                    Spec.Fireball(c, 4f * Kk, Px, Color.white, new Color(1f, 0.85f, 0.45f, 1f), new Color(1f, 0.5f, 0.15f, 1f), false);
                    Fx.Ring(c, Px, 0.7f, 2f, Mathf.Min(140f, SupernovaTech.Radius / Px), 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Color.white, Fx.WithA(new Color(1f, 0.7f, 0.3f, 1f), 0.7f), Fx.WithA(Primordials.Void, 0f));
                    DuelFx.Starburst(c, Px, 5f * Kk, Color.white, new Color(1f, 0.85f, 0.4f, 1f), Primordials.Prism(), 0.15f);
                }
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", Combat.TileAt(_at));
                Hit(SupernovaTech.Radius * 0.7f, 0.25f);
            }
            if (k >= 0.35f && k < 0.6f && vis)
            {
                // it falls in on itself
                float kk = (k - 0.35f) / 0.25f;
                Spec.Blob(c, Mathf.Lerp(3f, 0.3f, kk) * Kk, Px, 0.05f, FxLayer.Top, 25, 1f, 0.1f, Primordials.Void);
                Fx.Ring(c, Px, 0.12f, Mathf.Lerp(60f, 4f, kk) * Kk, 2f, 2, 1, FxLayer.Top, 24, 1f, 2f, Fx.WithA(Primordials.Prism(kk), 0.9f), Fx.WithA(Color.white, 0f));
            }
            if (k >= 0.6f && !_burst2)
            {
                // and goes off again, far bigger
                _burst2 = true;
                if (vis)
                {
                    Primordials.Flash(Fx.WithA(Color.white, 0.9f), 0.45f);
                    DuelFx.Starburst(c, Px, 7f * Kk, Color.white, Primordials.Prism(), Primordials.Prism(0.5f), 0.2f);
                    Spec.Fireball(c, 6f * Kk, Px, Color.white, Color.Lerp(Primordials.Prism(), Color.white, 0.3f), Primordials.Prism(0.4f));
                    for (int i = 0; i < 3; i++)
                    {
                        Particle r = Fx.Ring(c, Px, 0.8f + 0.2f * i, 2f, Mathf.Min(140f, SupernovaTech.Radius * (1.3f + 0.3f * i) / Px), 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Color.white, Fx.WithA(Primordials.Prism(i * 0.3f), 0.7f), Fx.WithA(Primordials.Void, 0f));
                        if (r != null)
                        {
                            r.Delay = 0.08f * i;
                        }
                    }
                    Spec.Rocks(_at, Px, 30, 150f, Kk);
                }
                if (Settings.Destruction > 0)
                {
                    MapAction.damageWorld(Combat.TileAt(Combat.Clamp(_at)), 8, TerraformLibrary.atomic_bomb, A);
                }
                Kit.Stop(c, 0.2f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionHuge", Combat.TileAt(_at));
                Hit(SupernovaTech.Radius, 0.5f);
            }
        }

        private void Hit(float r, float god)
        {
            foreach (Actor v in PrimPow.Foes(A, _at, r, 60))
            {
                if (_hit.Add(v.Sid()) || god >= 0.5f)
                {
                    Primordials.Doom(A, v, god);
                }
            }
        }
    }

    // ===================================================== STILLNESS OF THE FIRST ==

    internal sealed class StillnessTech : PrimordialTech
    {
        public override string Id => "primordial_stillness";
        public override Role Role => Role.Single;
        public override Form Forms => Form.Ultimate | Form.Teleport;
        public override float[] ManaCost => new[] { 300f, 300f, 300f };
        public override float[] Cooldown => new[] { 60f, 60f, 60f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            int great = 0;
            float score = 0f;
            Actor first = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 22f; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v) || !Great(v))
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
            return PrimPow.Toward(c, first, first.current_position, score * 1.2f, great);
        }

        public override bool Begin(Actor a, Plan p)
        {
            StillnessMove m = new StillnessMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class StillnessMove : ChoreoMove
    {
        private List<Actor> _targets = new List<Actor>();
        private readonly List<float> _angles = new List<float>();
        private int _n;

        public StillnessMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            Face(p.Dir);
        }

        public void Build()
        {
            OpenCinematic(6f, 0.6f, "sakuga_title_primordial_stillness", 0.05f, 1.9f);
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.25f, To = new Shape(0f, 0.96f, 1.08f), Ease = Ease.OutCubic, Track = false, Enter = m => ((StillnessMove)m).Stop() });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.4f, To = new Shape(0f, 1f, 1f), Ease = Ease.Linear, Track = false, Keep = true, Intangible = true, Every = (m, k) => ((StillnessMove)m).Visit(k) });
            Add(new Beat { Tempo = Tempo.Act, Dur = 0.3f, To = new Shape(14f, 1.08f, 0.94f), Ease = Ease.OutExpo, Track = false, Keep = true, Enter = m => ((StillnessMove)m).Release() });
            Recover(0.35f);
        }

        private void Stop()
        {
            _targets = PrimPow.Foes(A, A.current_position, 22f, 8);
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(new Color(0.75f, 0.75f, 0.9f, 1f), 0.5f), 0.3f);
                Fx.Ring(BodyPos(), Px, 1.6f, 60f * Kk, 18f * Kk, 2, 1, FxLayer.Top, 22, 1f, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(new Color(0.7f, 0.75f, 1f, 1f), 0.6f), Fx.WithA(Color.white, 0f));
            }
            Choreo.Sound("event:/SFX/POWERS/Curse", A.current_tile);
        }

        /// <summary>It is beside each of them in turn; an echo of it stays there, frozen mid-blow.</summary>
        private void Visit(float k)
        {
            int want = Mathf.Min(_targets.Count, Mathf.FloorToInt(k * (_targets.Count + 0.5f)));
            while (_n < want)
            {
                Actor v = _targets[_n];
                _n++;
                if (v == null || !v.isAlive())
                {
                    _angles.Add(0f);
                    continue;
                }
                float side = Random.value < 0.5f ? -1f : 1f;
                float ang = Random.Range(20f, 160f);
                _angles.Add(ang);
                Vector2 at = Choreo.BodyAt(v, 0.55f);
                if (!Kit.Visible(at, 0.5f))
                {
                    continue;
                }
                Sprite s = Fx.UnitSprite(A);
                Particle g = s != null ? Fx.Spawn(s, v.current_position + new Vector2(side * 0.7f * Kk, 0f), 1f, 1.8f - k * 1.4f, FxLayer.Top, 22, true) : null;
                if (g != null)
                {
                    g.SX = Mathf.Abs(A.current_scale.x) * -side;
                    g.SY = A.current_scale.y;
                    g.Color(Fx.WithA(new Color(0.88f, 0.88f, 1f, 1f), 0.9f), Fx.WithA(new Color(0.88f, 0.88f, 1f, 1f), 0.9f), Fx.WithA(Primordials.Prism(), 0f));
                }
                PrimFx.Slash(at, ang, 2.4f * Kk, 0.22f * Kk, 1.8f - k * 1.4f, FxLayer.Top, 23, false, Fx.WithA(Primordials.Prism(_n / 8f), 0.45f));
                Kit.Flash(at, Px, Mathf.RoundToInt(4f * Kk), 0.06f, Color.white);
            }
        }

        /// <summary>Time moves: every blow lands at once.</summary>
        private void Release()
        {
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.85f), 0.3f);
            }
            for (int i = 0; i < _targets.Count; i++)
            {
                Actor v = _targets[i];
                if (v == null || !v.isAlive())
                {
                    continue;
                }
                Vector2 at = Choreo.BodyAt(v, 0.55f);
                float ang = i < _angles.Count ? _angles[i] : 45f;
                if (Kit.Visible(at, 0.5f))
                {
                    PrimFx.Slash(at, ang, 5f * Kk, 0.5f * Kk, 0.4f, FxLayer.Top, 25, false, Primordials.Prism(i / 8f));
                    PrimFx.Slash(at, ang + 90f, 4f * Kk, 0.4f * Kk, 0.4f, FxLayer.Top, 25, false, Color.white, 0.05f);
                    DuelFx.Starburst(at, Px, 2f * Kk, Color.white, Primordials.Prism(i / 8f), Primordials.Prism(i / 8f + 0.5f), 0.1f);
                }
                Kit.Ravage(v.current_position, 1.6f, 3, A, false);
                Primordials.Doom(A, v, 0.4f);
            }
            Kit.Stop(BodyPos(), 0.2f);
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
        }
    }

    // ============================================================ CRUSHING HEAVEN ==

    internal sealed class CrushingHeavenTech : PrimordialTech
    {
        public override string Id => "primordial_crushing_heaven";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Area;
        public override float[] ManaCost => new[] { 280f, 280f, 280f };
        public override float[] Cooldown => new[] { 40f, 40f, 40f };
        public const float Radius = 9f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            Vector2 at = PrimPow.Cluster(c, 22f, Radius, out float score, out int n, out int great);
            if (n == 0 || (at - c.Pos).sqrMagnitude < 5f * 5f || !c.Forced && n < 4 && great < 1)
            {
                return null;
            }
            return PrimPow.Toward(c, null, at, score * 1.1f, n, Radius);
        }

        public override bool Begin(Actor a, Plan p)
        {
            CrushingMove m = new CrushingMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class CrushingMove : ChoreoMove
    {
        private readonly Vector2 _at;
        private float _sunk;
        private static readonly Color Weight = new Color(0.55f, 0.4f, 1f, 1f);

        public CrushingMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            _at = p.Point;
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.5f, To = new Shape(-10f, 0.94f, 1.1f, 0.6f), Ease = Ease.OutCubic, Track = false, Charging = true });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.7f, To = new Shape(14f, 1.1f, 0.9f, 0.6f, 0.05f), Ease = Ease.InQuad, Track = false, Keep = true, Every = (m, k) => ((CrushingMove)m).Press(k) });
            Add(new Beat { Tempo = Tempo.Act, Dur = 0.2f, To = new Shape(0f, 1f, 1f), Ease = Ease.OutExpo, Track = false, Keep = true, Enter = m => ((CrushingMove)m).Crush() });
            Recover(0.3f);
        }

        private void Press(float k)
        {
            bool vis = Kit.Visible(_at, 2f);
            if (vis && Random.value < 0.45f)
            {
                // rings of weight falling on them, from the sky to the ground
                float r = CrushingHeavenTech.Radius * Random.Range(1f, 1.4f);
                Fx.Ring(_at, Px, 0.35f, Mathf.Min(140f, r / Px), 2f * Kk / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Weight, 0f), Fx.WithA(Weight, 0.8f), Fx.WithA(Color.white, 0.9f));
                Fx.Ring(_at + new Vector2(0f, (8f - 7f * k) * Kk), Px, 0.2f, Mathf.Min(140f, r * 0.8f / Px), Mathf.Min(140f, r * 0.7f / Px), 2, 1, FxLayer.Top, 20, 0.35f, 2f, Fx.WithA(Weight, 0.6f), Fx.WithA(Primordials.Void, 0f));
            }
            if (vis && Random.value < 0.3f)
            {
                PrimFx.Beam(Hand(Limb.Palm), _at + new Vector2(0f, 6f * Kk), 0.35f * Kk, 0.06f, FxLayer.Top, 19, false, Weight, Primordials.Void, false);
                PrimFx.Crack(_at, Random.insideUnitCircle.normalized, (3f + 6f * k) * Kk, 2.2f * Px, 1f, Weight, FxLayer.Ground, 4, false, true, 1);
            }
            // the ground under them sinks, ring by ring
            float rr = CrushingHeavenTech.Radius * Anim.OutQuad(k);
            if (Settings.Destruction > 0 && rr > _sunk + 0.5f)
            {
                int x0 = Mathf.FloorToInt(_at.x - rr), x1 = Mathf.CeilToInt(_at.x + rr), y0 = Mathf.FloorToInt(_at.y - rr), y1 = Mathf.CeilToInt(_at.y + rr);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        float d2 = (x + 0.5f - _at.x) * (x + 0.5f - _at.x) + (y + 0.5f - _at.y) * (y + 0.5f - _at.y);
                        if (d2 > rr * rr || d2 <= _sunk * _sunk || x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
                        {
                            continue;
                        }
                        WorldTile t = World.world.GetTileSimple(x, y);
                        if (t == null)
                        {
                            continue;
                        }
                        if (t.hasBuilding() && t.building != null && t.building.isAlive())
                        {
                            t.building.startDestroyBuilding();
                        }
                        MapAction.decreaseTile(t, true, TerraformLibrary.nothing);
                    }
                }
                _sunk = rr;
            }
            foreach (Actor v in PrimPow.Foes(A, _at, CrushingHeavenTech.Radius, 40))
            {
                if (Random.value < 0.15f)
                {
                    v.getHit(Mathf.Max(1f, v.getMaxHealth() * 0.01f), true, AttackType.Other, A, false, false, true);
                }
            }
            World.world.startShake(0.15f, 0.01f, 1f + 2f * k);
        }

        private void Crush()
        {
            if (Kit.Visible(_at, 2f))
            {
                Primordials.Flash(Fx.WithA(Weight, 0.45f), 0.2f);
                Spec.Blob(_at, 4f * Kk, Px, 0.5f, FxLayer.Top, 24, Art.Ground, 0.5f, Color.white, Fx.WithA(Weight, 0.7f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.4f, 1.5f);
                Spec.Rocks(_at, Px, 24, 120f, Kk);
            }
            Kit.Ravage(_at, CrushingHeavenTech.Radius * 0.8f, 3, A, false);
            foreach (Actor v in PrimPow.Foes(A, _at, CrushingHeavenTech.Radius, 60))
            {
                Primordials.Doom(A, v, 0.4f);
            }
            Kit.Stop(_at, 0.2f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", Combat.TileAt(_at));
        }
    }

    // ========================================================= CHAINS OF CREATION ==

    internal sealed class ChainsTech : PrimordialTech
    {
        public override string Id => "primordial_chains";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Grab;
        public override float[] ManaCost => new[] { 280f, 280f, 280f };
        public override float[] Cooldown => new[] { 42f, 42f, 42f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            int great = 0;
            float score = 0f;
            Actor first = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 20f; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v) || !Great(v))
                {
                    continue;
                }
                great++;
                first = first ?? v;
                score += Worth(c, v);
            }
            if (first == null || !c.Forced && great < 2 && Tiers.Of(first) < Tier.God)
            {
                return null;
            }
            return PrimPow.Toward(c, first, first.current_position, score, great);
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChainsMove m = new ChainsMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class ChainsMove : ChoreoMove
    {
        private List<Actor> _targets = new List<Actor>();
        private readonly List<Vector2> _anchors = new List<Vector2>();

        public ChainsMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.4f, To = new Shape(8f, 1.12f, 0.88f), Ease = Ease.OutExpo, Track = false, Enter = m => ((ChainsMove)m).Erupt() });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.4f, To = new Shape(-8f, 0.95f, 1.1f, 0.3f), Ease = Ease.InOutSine, Track = false, Keep = true, Every = (m, k) => ((ChainsMove)m).Bind(k) });
            Add(new Beat { Tempo = Tempo.Act, Dur = 0.25f, To = new Shape(16f, 1.12f, 0.9f), Ease = Ease.OutExpo, Track = false, Keep = true, Enter = m => ((ChainsMove)m).Shatter() });
            Recover(0.3f);
        }

        private void Erupt()
        {
            _targets = PrimPow.Foes(A, A.current_position, 20f, 5);
            _anchors.Clear();
            foreach (Actor v in _targets)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.094f + Random.Range(-0.4f, 0.4f);
                    Vector2 g = v.current_position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * 2.6f * Kk;
                    _anchors.Add(g);
                    if (Kit.Visible(g, 0.5f))
                    {
                        Spec.Rocks(g, Px, 6, 80f, Kk);
                        Kit.Ravage(g, 1f, 2, A, false);
                    }
                }
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile);
        }

        /// <summary>Chains of light from the ground to each of them, link by link, tightening.</summary>
        private void Bind(float k)
        {
            for (int t = 0; t < _targets.Count; t++)
            {
                Actor v = _targets[t];
                if (v == null || !v.isAlive())
                {
                    continue;
                }
                Vector2 body = Choreo.BodyAt(v, 0.5f);
                for (int i = 0; i < 3; i++)
                {
                    int ai = t * 3 + i;
                    if (ai >= _anchors.Count)
                    {
                        break;
                    }
                    Vector2 g = _anchors[ai];
                    Vector2 end = Vector2.Lerp(g, body, Mathf.Clamp01(k * 2.5f));
                    Chain(g, end, k, t * 0.2f + i * 0.07f);
                }
                if (k > 0.4f)
                {
                    try
                    {
                        v.addStatusEffect("stunned", 0.4f);
                    }
                    catch
                    {
                    }
                    if (Random.value < 0.2f && Kit.Visible(body, 0.5f))
                    {
                        Kit.Flash(body, Px, Mathf.RoundToInt(3f * Kk), 0.05f, Primordials.Prism(t * 0.2f));
                    }
                }
            }
        }

        private void Chain(Vector2 a, Vector2 b, float k, float hue)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.1f || !Kit.Visible((a + b) * 0.5f, 1f))
            {
                return;
            }
            Color c = Primordials.Prism(hue + T * 0.3f, 0.6f);
            PrimFx.Band(a, b, 0.5f * Kk, 0.05f, FxLayer.Top, 20, false, 0, Fx.WithA(c, 0.4f));
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float step = 0.32f * Kk;
            int n = Mathf.Min(40, Mathf.CeilToInt(len / step));
            float tight = 0.12f * (1f - Mathf.Clamp01(k * 1.5f));
            Vector2 side = new Vector2(-d.y, d.x) / len;
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                Vector2 p = a + d * u + side * Mathf.Sin(u * Mathf.PI) * tight * len;
                // links, each turned a quarter from the last
                Particle l = Fx.Ring(p, Px, 0.05f, 1.8f * Kk / Px * 0.1f + 1.5f, 1.8f * Kk / Px * 0.1f + 1.5f, 1, 1, FxLayer.Top, 21, i % 2 == 0 ? 0.5f : 0.25f, 1f, Color.Lerp(c, Color.white, 0.4f));
                if (l != null)
                {
                    l.Rot = ang;
                }
            }
        }

        private void Shatter()
        {
            for (int t = 0; t < _targets.Count; t++)
            {
                Actor v = _targets[t];
                if (v == null || !v.isAlive())
                {
                    continue;
                }
                Vector2 at = Choreo.BodyAt(v, 0.5f);
                if (Kit.Visible(at, 0.5f))
                {
                    DuelFx.Starburst(at, Px, 2.5f * Kk, Color.white, Primordials.Prism(t * 0.2f), Primordials.Prism(t * 0.2f + 0.5f), 0.12f);
                    Spec.Blob(at, 1.8f * Kk, Px, 0.4f, FxLayer.Top, 24, 1f, 0.5f, Color.white, Fx.WithA(Primordials.Prism(t * 0.2f), 0.6f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.3f, 1.4f);
                    for (int i = 0; i < 6; i++)
                    {
                        Particle s = Fx.Spawn(Art.Sparks[2], at, Px * 1.5f, 0.35f, FxLayer.Top, 24);
                        if (s != null)
                        {
                            s.Moving(Random.insideUnitCircle.normalized * Random.Range(12f, 20f) * Kk, 0f, 2f);
                            s.FaceVel = true;
                            s.Color(Color.white, Fx.WithA(Primordials.Prism(t * 0.2f), 0f));
                        }
                    }
                }
                Kit.Ravage(v.current_position, 1.8f, 3, A, false);
                Primordials.Doom(A, v, 0.45f);
            }
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.5f), 0.15f);
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
        }
    }

    // ================================================================= BLACK SUN ==

    internal sealed class BlackSunTech : PrimordialTech
    {
        public override string Id => "primordial_black_sun";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Beam | Form.Area;
        public override float[] ManaCost => new[] { 340f, 340f, 340f };
        public override float[] Cooldown => new[] { 55f, 55f, 55f };
        public const float Radius = 16f;

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            Vector2 at = PrimPow.Cluster(c, 20f, Radius, out float score, out int n, out int great);
            if (n == 0 || !c.Forced && n < 5 && great < 2)
            {
                return null;
            }
            return PrimPow.Toward(c, null, at, score * 1.15f, n, Radius);
        }

        public override bool Begin(Actor a, Plan p)
        {
            BlackSunMove m = new BlackSunMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class BlackSunMove : ChoreoMove
    {
        private readonly Vector2 _at;
        private float _fire;
        private readonly Dictionary<long, int> _hits = new Dictionary<long, int>();

        public BlackSunMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            _at = p.Point;
            Face(p.Dir);
        }

        private Vector2 Sun => _at + new Vector2(0f, 10f * Kk);

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.7f, To = new Shape(-12f, 0.94f, 1.12f, 1.2f), Ease = Ease.OutCubic, Track = false, Charging = true, Every = (m, k) => ((BlackSunMove)m).Rise(k) });
            Add(new Beat { Tempo = Tempo.Free, Dur = 3f, To = new Shape(-6f, 1f, 1.04f, 1.2f), Ease = Ease.Linear, Track = false, Keep = true, Every = (m, k) => ((BlackSunMove)m).Rain(k) });
            Recover(0.4f);
        }

        private void Rise(float k)
        {
            DrawSun(k);
        }

        private void DrawSun(float k)
        {
            Vector2 s = Sun;
            if (!Kit.Visible(s, 3f) && !Kit.Visible(_at, 2f))
            {
                return;
            }
            float r = (0.5f + 2.5f * Mathf.Clamp01(k)) * Kk;
            // a corona of the first fire round a disk of nothing
            for (int i = 0; i < 12; i++)
            {
                float ang = T * 20f + i * 30f;
                PrimFx.Ray(s, ang, r * Random.Range(1.8f, 2.6f), r * 0.5f, 0.05f, FxLayer.Top, 22, false, Fx.WithA(Primordials.Prism(i / 12f + T * 0.1f), 0.7f));
            }
            Spec.Blob(s, r * 1.45f, Px, 0.05f, FxLayer.Top, 23, 1f, 0.6f, Fx.WithA(Primordials.Prism(T * 0.2f), 0.55f));
            Spec.Blob(s, r * 1.08f, Px, 0.05f, FxLayer.Top, 24, 1f, 0.2f, Color.white);
            Spec.Blob(s, r, Px, 0.05f, FxLayer.Top, 25, 1f, 0.05f, Color.black);
        }

        /// <summary>Black light raining from it on them, every beam a crater.</summary>
        private void Rain(float k)
        {
            DrawSun(1f);
            _fire -= Time.deltaTime;
            if (_fire > 0f)
            {
                return;
            }
            _fire = 0.14f;
            List<Actor> foes = PrimPow.Foes(A, _at, BlackSunTech.Radius, 40);
            Vector2 to;
            Actor hit = null;
            if (foes.Count > 0 && Random.value < 0.75f)
            {
                hit = foes[Random.Range(0, foes.Count)];
                to = hit.current_position;
            }
            else
            {
                to = Combat.Clamp(_at + Random.insideUnitCircle * BlackSunTech.Radius);
            }
            Vector2 s = Sun;
            if (Kit.Visible(to, 1f) || Kit.Visible(s, 2f))
            {
                PrimFx.Beam(s, to, 0.55f * Kk, 0.18f, FxLayer.Top, 20, false, Color.black, Primordials.Prism(Random.value), true, true);
                Fx.Ring(to, Px, 0.4f, 2f, 3f * Kk / Px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f, Color.white, Fx.WithA(Primordials.Prism(), 0.6f), Fx.WithA(Primordials.Void, 0f));
                Spec.Rocks(to, Px, 6, 90f, Kk);
            }
            Kit.Ravage(to, 1.8f, 3, A, false);
            if (Settings.Destruction > 0 && Random.value < 0.3f)
            {
                MapAction.damageWorld(Combat.TileAt(Combat.Clamp(to)), 3, TerraformLibrary.bomb, A);
            }
            if (hit != null)
            {
                long id = hit.Sid();
                _hits.TryGetValue(id, out int n);
                if (n < 2)
                {
                    _hits[id] = n + 1;
                    Primordials.Doom(A, hit, 0.22f);
                }
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(Combat.Clamp(to)));
        }
    }

    // ========================================================== LEGION OF ECHOES ==

    internal sealed class LegionTech : PrimordialTech
    {
        public override string Id => "primordial_legion";
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ultimate | Form.Teleport | Form.Dash;
        public override float[] ManaCost => new[] { 220f, 220f, 220f };
        public override float[] Cooldown => new[] { 28f, 28f, 28f };

        public override Plan Plan(Ctx c, int level)
        {
            if (!Ready(c))
            {
                return null;
            }
            int n = 0;
            float score = 0f;
            Actor first = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 18f; i++)
            {
                Actor v = c.Enemies[i];
                if (Primordials.Is(v))
                {
                    continue;
                }
                n++;
                first = first ?? v;
                score += Worth(c, v);
            }
            if (first == null || !c.Forced && n < 3)
            {
                return null;
            }
            return PrimPow.Toward(c, first, first.current_position, score, n);
        }

        public override bool Begin(Actor a, Plan p)
        {
            LegionMove m = new LegionMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class LegionMove : ChoreoMove
    {
        private List<Actor> _targets = new List<Actor>();
        private readonly List<Vector2> _ends = new List<Vector2>();
        private bool _struck;

        public LegionMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.3f, To = new Shape(-8f, 1.1f, 0.9f), Ease = Ease.OutCubic, Track = false, Enter = m => ((LegionMove)m).Split() });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1.1f, To = new Shape(0f, 1f, 1f), Ease = Ease.Linear, Track = false, Keep = true, Intangible = true, Every = (m, k) => ((LegionMove)m).Dash(k) });
            Recover(0.3f);
        }

        private void Split()
        {
            _targets = PrimPow.Foes(A, A.current_position, 18f, 6);
            _ends.Clear();
            foreach (Actor v in _targets)
            {
                _ends.Add(v.current_position);
            }
            if (Vis)
            {
                Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(8f * Kk), 0.1f, Color.white);
                DuelFx.Starburst(BodyPos(), Px, 1.6f * Kk, Color.white, Primordials.Prism(), Primordials.Prism(0.5f), 0.08f);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
        }

        /// <summary>Six echoes out to six foes and back; at the turn, every one of them strikes.</summary>
        private void Dash(float k)
        {
            Vector2 home = Pose.Ground;
            Sprite sp = Fx.UnitSprite(A);
            for (int i = 0; i < _targets.Count; i++)
            {
                Actor v = _targets[i];
                Vector2 end = v != null && v.isAlive() ? v.current_position : _ends[i];
                float u = k < 0.5f ? Anim.OutCubic(k / 0.5f) : 1f - Anim.InQuad((k - 0.5f) / 0.5f);
                Vector2 p = Vector2.Lerp(home, end, u) + new Vector2(0f, Mathf.Sin(Mathf.PI * u) * 1.2f * Kk);
                if (sp != null && Kit.Visible(p, 0.5f))
                {
                    Particle g = Fx.Spawn(sp, p, 1f, 0.12f, FxLayer.Top, 21, true);
                    if (g != null)
                    {
                        g.SX = Mathf.Abs(A.current_scale.x) * ((end.x >= home.x) == (k < 0.5f) ? 1f : -1f);
                        g.SY = A.current_scale.y;
                        Color c = Primordials.Prism(i / 6f, 0.6f);
                        g.Color(Fx.WithA(c, 0.85f), Fx.WithA(c, 0f));
                    }
                }
            }
            if (!_struck && k >= 0.5f)
            {
                _struck = true;
                for (int i = 0; i < _targets.Count; i++)
                {
                    Actor v = _targets[i];
                    if (v == null || !v.isAlive())
                    {
                        continue;
                    }
                    Vector2 at = Choreo.BodyAt(v, 0.55f);
                    if (Kit.Visible(at, 0.5f))
                    {
                        PrimFx.Slash(at, Random.Range(0f, 180f), 3.4f * Kk, 0.35f * Kk, 0.3f, FxLayer.Top, 24, false, Primordials.Prism(i / 6f));
                        DuelFx.Starburst(at, Px, 1.4f * Kk, Color.white, Primordials.Prism(i / 6f), Primordials.Prism(i / 6f + 0.5f), 0.08f);
                        Kit.Flash(at, Px, Mathf.RoundToInt(5f * Kk), 0.06f, Color.white);
                    }
                    Kit.Ravage(v.current_position, 1.4f, 2, A, false);
                    Primordials.Doom(A, v, 0.3f);
                }
                Choreo.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            }
        }
    }

    // ================================================================ WORLDBREAKER ==

    internal sealed class WorldbreakerTech : PrimordialTech
    {
        public override string Id => "primordial_worldbreaker";
        public override Role Role => Role.Line;
        public override Form Forms => Form.Ultimate | Form.Line | Form.Aerial;
        public override float[] ManaCost => new[] { 240f, 240f, 240f };
        public override float[] Cooldown => new[] { 32f, 32f, 32f };
        public const float Len = 30f, Width = 2.5f;

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
                if (!c.Forced && n < 3 && score < 2000f)
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
            WorldbreakerMove m = new WorldbreakerMove(a, this, p);
            m.Build();
            return m.Start();
        }
    }

    internal sealed class WorldbreakerMove : ChoreoMove
    {
        private readonly Vector2 _dir;
        private Vector2 _from;
        private float _done;
        private readonly HashSet<long> _hit = new HashSet<long>();

        public WorldbreakerMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            SetVictim(null);
            _dir = p.Dir;
            Face(p.Dir);
        }

        public void Build()
        {
            Add(new Beat { Tempo = Tempo.Wind, Dur = 0.45f, To = new Shape(-18f, 0.9f, 1.15f, 2.2f), Ease = Ease.OutCubic, Track = false, Charging = true, Every = (m, k) => ((WorldbreakerMove)m).Fist(k) });
            Add(new Beat { Tempo = Tempo.Act, Dur = 0.12f, To = new Shape(30f, 1.2f, 0.8f, 0f, 0.3f), Ease = Ease.InCubic, Track = false, Keep = true, Enter = m => ((WorldbreakerMove)m).Slam() });
            Add(new Beat { Tempo = Tempo.Free, Dur = 1f, To = new Shape(24f, 1.15f, 0.85f, 0f, 0.3f), Ease = Ease.Linear, Track = false, Keep = true, Every = (m, k) => ((WorldbreakerMove)m).Split(k) });
            Recover(0.35f);
        }

        private void Fist(float k)
        {
            if (Vis)
            {
                PrimFx.Charge(Hand(Limb.Fist), (0.3f + 0.7f * k) * Kk, FxLayer.Top, 25, false, new Color(1f, 0.55f, 0.2f, 1f));
            }
        }

        private void Slam()
        {
            _from = Pose.Ground + _dir * 0.8f;
            if (Vis)
            {
                Primordials.Flash(Fx.WithA(new Color(1f, 0.7f, 0.4f, 1f), 0.35f), 0.15f);
                Spec.Blob(Pose.Ground, 2.4f * Kk, Px, 0.4f, FxLayer.Top, 24, Art.Ground, 0.5f, Color.white, Fx.WithA(new Color(1f, 0.6f, 0.2f, 1f), 0.6f), Fx.WithA(Primordials.Void, 0f))?.Sized(0.3f, 1.5f);
                Spec.Rocks(Pose.Ground, Px, 20, 130f, Kk);
            }
            Kit.Ravage(Pose.Ground, 2.4f, 3, A, false);
            Kit.Stop(BodyPos(), 0.12f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", A.current_tile);
        }

        /// <summary>The land splits along the line, racing ahead: lava and rock thrown up all along it.</summary>
        private void Split(float k)
        {
            float reach = WorldbreakerTech.Len * Anim.OutQuad(k);
            while (_done < reach)
            {
                float s0 = _done;
                _done += 1.5f;
                Vector2 a = _from + _dir * s0, b = Combat.Clamp(_from + _dir * _done);
                Kit.Trench(a, b, 1.8f, 4, A, true);
                if (Kit.Visible(b, 1f))
                {
                    PrimFx.Crack(a, _dir, 1.5f, 3f * Px, 1.4f, new Color(1f, 0.5f, 0.15f, 1f), FxLayer.Ground, 4, false, true, 0);
                    if (Random.value < 0.35f)
                    {
                        // a geyser of lava and rock
                        Spec.Column(b, Px, 6, Kk * 1.3f, new Color(1f, 0.45f, 0.1f, 1f));
                        Spec.Rocks(b, Px, 8, 130f, Kk);
                        Kit.Flash(b, Px, Mathf.RoundToInt(5f * Kk), 0.08f, new Color(1f, 0.75f, 0.4f, 1f));
                    }
                }
                foreach (Actor v in PrimPow.Foes(A, b, WorldbreakerTech.Width, 20))
                {
                    if (_hit.Add(v.Sid()))
                    {
                        Combat.Launch(v, b, Random.Range(1.5f, 2.5f), Random.Range(0.8f, 1.2f), Random.value < 0.5f ? -700f : 700f);
                        Primordials.Doom(A, v, 0.4f);
                    }
                }
            }
        }
    }
}
