using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PILLAR OF HEAVEN (Divine, God; id world_tree_pillar). The God spreads its arms and looks up
    /// (Charging: a fast blow breaks it); a thread of light comes down to it out of the sky, leaves
    /// of light rising round it. Then the column falls: a pillar of white and green-gold light from
    /// the sky straight through the God into the earth. The God is lifted a little inside it, roots
    /// of light race out through the ground in every direction (the ground torn along them, a ring
    /// broken round its feet, every fire near it put out), and a gale bursts out of the column:
    /// every enemy around is swept far away (Scattered). Inside the light the God is healed in
    /// pulses, then the column draws back up into the sky and it lands. The world tree's motif:
    /// roots, leaves and green light, not Last Light's golden rebirth (chosen by the planner when
    /// the God is hurt and pressed, never as a last-breath reflex).
    /// I: 5 tiles, heals 15%; II: 7 tiles, 22%; III: 9 tiles, 30% and the cinematic.
    /// Damage (x the user's damage) I 0.8, II 1.1, III 1.5 (less at the edge).
    /// </summary>
    internal sealed class WorldTreePillarTech : Technique
    {
        public override string Id => "world_tree_pillar";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ki | Form.Area | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 120f, 170f, 220f };
        public override float[] Cooldown => new[] { 150f, 165f, 180f };

        public static float Radius(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 7f : 9f;
        }

        public static float Heal(int level)
        {
            return level <= 1 ? 0.15f : level == 2 ? 0.22f : 0.3f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.8f : level == 2 ? 1.1f : 1.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int n = 0;
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
                score += c.Value(v, c.Expected(v, Mult(level)), cm) + 4f * Mathf.Clamp(Tiers.Power(v), 0.3f, 4f);
            }
            if (main == null)
            {
                return null;
            }
            int max = c.Self.getMaxHealth();
            float heal = Mathf.Min(max * Heal(level), max - c.Self.getHealth());
            if (!c.Forced)
            {
                // the light is for a God that is hurt and pressed (or buried under a horde)
                if (c.HpPct > 0.7f && n < 8)
                {
                    return null;
                }
                if (!DivineArts.Warranted(c, main, n, threat) && !(c.HpPct < 0.5f && threat >= 0.3f))
                {
                    return null;
                }
            }
            // its own life back is worth more the lower it is
            score += heal * (1.25f - c.HpPct) * 0.8f;
            if (c.Surrounded)
            {
                score *= 1.15f;
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
            PillarMove m = new PillarMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Pillar of Heaven's move: the invocation, the column falling, the gale, the healing light, the column rising.</summary>
    internal sealed class PillarMove : ChoreoMove
    {
        private readonly float _r;
        private readonly int _healTotal;
        private int _healed;
        private Particle _thread, _glow, _core, _base;
        private float _lastT, _fxT, _pulseT, _width;
        private bool _fell;
        private Hue _holy;
        private static readonly List<Actor> _scan = new List<Actor>();

        public PillarMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _r = WorldTreePillarTech.Radius(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            int max = a.getMaxHealth();
            _healTotal = Mathf.Max(0, Mathf.RoundToInt(max * WorldTreePillarTech.Heal(Level)));
            _holy = Hue.As(Element.Holy);
            SetVictim(null);
            Face(p.Dir);
        }

        /// <summary>The column's width at full (pixels of the God's sprite).</summary>
        private float Wide => (7f + 3f * Level) * Kk;

        public void Build()
        {
            // the invocation: arms spread, face to the sky, a thread of light coming down
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.6f : 0.48f,
                To = new Shape(-5f, 1.14f, 0.96f, 0f),
                Ease = Ease.OutCubic,
                Charging = true,
                Track = false,
                Forms = Form.Ki | Form.Area | Form.Charge,
                Enter = m => ((PillarMove)m).EnterInvoke(),
                Every = (m, k) => ((PillarMove)m).TickInvoke(k)
            });
            // the column falls: through the God into the earth
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.12f,
                To = new Shape(0f, 0.96f, 1.1f, 0.3f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Ki | Form.Area | Form.Ultimate,
                Every = (m, k) => ((PillarMove)m).TickFall(k),
                Exit = m => ((PillarMove)m).Fall()
            });
            // inside the light: healed in pulses, floating
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = Level <= 1 ? 0.5f : Level == 2 ? 0.7f : 0.9f,
                To = new Shape(0f, 1.02f, 1.02f, 0.38f),
                Ease = Ease.InOutSine,
                Track = false,
                Forms = Form.Ki | Form.Area | Form.Ultimate,
                Every = (m, k) => ((PillarMove)m).TickBathe(k),
                Exit = m => ((PillarMove)m).HealRest()
            });
            // the column draws back up into the sky; it comes down
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.32f,
                To = new Shape(0f, 1f, 1f, 0f),
                Ease = Ease.InQuad,
                Track = false,
                Every = (m, k) => ((PillarMove)m).TickRise(k),
                Exit = m => ((PillarMove)m).Landed()
            });
            Recover(0.28f);
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

        // ------------------------------------------------------------- invocation --

        private void EnterInvoke()
        {
            _lastT = 0f;
            if (Level >= 2)
            {
                DivineArts.Open(this, "world_tree_pillar", 0.35f, 0.6f, 0.4f);
            }
            Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
        }

        private void TickInvoke(float k)
        {
            float dt = Dt();
            if (k > 0.8f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            }
            Column(Mathf.Lerp(1f, 1.6f, k), 0.25f + 0.55f * k, true);
            if (!Vis)
            {
                return;
            }
            _fxT -= dt;
            if (_fxT <= 0f)
            {
                _fxT = 0.05f;
                Leaves(2, 1.2f, true);
            }
        }

        /// <summary>The column at 'width' pixels and 'alpha'; 'thread': still only the thread (no glow).</summary>
        private void Column(float width, float alpha, bool thread)
        {
            if (!Vis)
            {
                return;
            }
            float h = 14f * Kk;
            Vector2 mid = Pose.Ground + new Vector2(0f, h * 0.5f - 0.2f * Kk);
            if (thread)
            {
                if (_thread == null || !_thread.Alive)
                {
                    _thread = Glue(Fx.Spawn(Art.Pixel, mid, Px, 1f, TopLayer, 10));
                }
                if (_thread != null)
                {
                    _thread.Pos = mid;
                    _thread.SX = width;
                    _thread.SY = h / Px;
                    _thread.Grad = null;
                    _thread.R.color = Fx.WithA(Color.Lerp(_holy.Core, DivineArts.Verdant, 0.25f), alpha);
                }
                return;
            }
            if (_thread != null)
            {
                Unglue(_thread);
                _thread = null;
            }
            if (_glow == null || !_glow.Alive)
            {
                _glow = Glue(Fx.Spawn(Art.Pixel, mid, Px, 1f, TopLayer, 9));
                _core = Glue(Fx.Spawn(Art.Pixel, mid, Px, 1f, TopLayer, 11));
                _base = Glue(Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(Wide * 1.6f)), Art.Ground, 0.5f), Pose.Ground, Px, 1f, FxLayer.Ground, 4));
            }
            float shimmer = 1f + 0.08f * Mathf.Sin(T * 24f);
            if (_glow != null)
            {
                _glow.Pos = mid;
                _glow.SX = width * 2.4f * shimmer;
                _glow.SY = h / Px;
                _glow.Grad = null;
                _glow.R.color = Fx.WithA(Color.Lerp(_holy.Edge, DivineArts.Verdant, 0.45f), 0.42f * alpha);
            }
            if (_core != null)
            {
                _core.Pos = mid;
                _core.SX = width;
                _core.SY = h / Px;
                _core.Grad = null;
                _core.R.color = Fx.WithA(Color.Lerp(Color.white, _holy.Core, 0.3f), alpha);
            }
            if (_base != null)
            {
                _base.Pos = Pose.Ground;
                _base.Size0 = _base.Size1 = Mathf.Clamp01(width / Mathf.Max(1f, Wide)) * shimmer;
                _base.Grad = null;
                _base.R.color = Fx.WithA(Color.Lerp(_holy.Core, DivineArts.Verdant, 0.4f), 0.5f * alpha);
            }
        }

        /// <summary>Leaves of light: rising round it ('rise'), or drifting out and down from the column.</summary>
        private void Leaves(int n, float spread, bool rise)
        {
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = rise
                    ? Pose.Ground + new Vector2(Random.Range(-spread, spread), Random.Range(-0.3f, 0.3f)) * Kk
                    : Pose.Ground + new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.5f, 3.5f)) * Kk;
                Particle p = Fx.Spawn(ChoreoArt.Petal(Random.Range(0, 4)), at, Px, Random.Range(0.6f, 1f), TopLayer, 7);
                if (p == null)
                {
                    break;
                }
                Vector2 vel = rise
                    ? new Vector2(Random.Range(-6f, 6f), Random.Range(20f, 45f))
                    : new Vector2(Random.Range(-45f, 45f), Random.Range(-15f, 5f));
                p.Moving(vel * Px * Kk, 0f, 0.8f);
                p.Spin = Random.Range(-300f, 300f);
                Color leaf = Random.value < 0.6f ? DivineArts.Verdant : _holy.Core;
                p.Color(Fx.WithA(leaf, 0f), Fx.WithA(leaf, 0.95f), Fx.WithA(leaf, 0f));
            }
        }

        // ------------------------------------------------------------- the fall --

        private void TickFall(float k)
        {
            Column(Mathf.Lerp(1.6f, Wide, Beat.Apply(Ease.OutCubic, k)), 1f, false);
        }

        private void Fall()
        {
            if (_fell)
            {
                return;
            }
            _fell = true;
            Vector2 at = Pose.Ground;
            WorldTile tile = A.current_tile;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", tile, 0.1f);
            Choreo.Sound("event:/SFX/POWERS/Blessing", tile, 0.2f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", tile, 0.2f);
            if (Cinematic)
            {
                Freeze(0.08f);
            }
            Kit.Stop(at, 0.08f);
            Ruin.Quake(A, at, Scale, 1.5f, Kk);
            if (Vis)
            {
                Kit.ImpactFrame(BodyPos(), Px, 1.1f * Kk, _holy.Pal, false, Cinematic ? FxLayer.Cine : FxLayer.Top);
                Fx.Ring(at, Px, 0.55f, 6f, Mathf.Min(140f, _r / Px), 3, 1, FxLayer.Ground, 7, Art.Ground, 1.5f,
                    Fx.WithA(Color.white, 1f), Fx.WithA(DivineArts.Verdant, 0.7f), Fx.WithA(_holy.Rim, 0f));
                Fx.Ring(at, Px, 0.4f, 4f, Mathf.Min(140f, _r * 0.7f / Px), 2, 1, FxLayer.Ground, 6, Art.Ground, 1.8f,
                    Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0f));
                Kit.DustRing(at, Px, tile, 20, 80f, 0.8f, Kk * 1.5f);
                Leaves(10, 1f, false);
            }
            Roots(at);
            Sweep(at);
            GodRuin.Calm(at, _r * 0.6f);
            Kit.BeginTrench();
            try
            {
                // the ground breaks in a ring round its feet (never under them)
                Ruin.Blast(A, at, 1.9f / Mathf.Max(0.5f, Ruin.Radius(Tier, Scale, 1f)), Scale, at, false);
            }
            finally
            {
                Kit.EndTrench(A);
            }
            _pulseT = 0f;
        }

        /// <summary>Roots of light racing out through the ground, a furrow torn along each.</summary>
        private void Roots(Vector2 at)
        {
            int n = Level <= 1 ? 5 : Level == 2 ? 7 : 9;
            float a0 = Random.value * 360f;
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    float ang = (a0 + i * 360f / n + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float len = _r * Random.Range(0.6f, 0.9f);
                    Vector2 from = at + dir * 1.3f * Kk, to = Combat.Clamp(at + dir * len);
                    Ruin.Furrow(A, from, to, 0.35f, Scale, at, false, -1);
                    if (Kit.Visible(Vector2.Lerp(from, to, 0.5f), 0.3f))
                    {
                        Root(at, ang, len, i, 1f);
                        // a branch off its middle
                        float side = Random.value < 0.5f ? -1f : 1f;
                        Root(at + dir * len * 0.5f, ang + side * Random.Range(0.45f, 0.8f), len * 0.4f, i + 7, 0.8f);
                    }
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
        }

        private void Root(Vector2 from, float ang, float len, int variant, float bright)
        {
            int px = Mathf.Clamp(Mathf.RoundToInt(len / Px), 6, 120);
            float rot = Mathf.Atan2(Mathf.Sin(ang), Mathf.Cos(ang)) * Mathf.Rad2Deg;
            Particle dark = Fx.Spawn(ChoreoArt.Fissure(px, variant), from, Px, 3.2f, FxLayer.Ground, 2);
            if (dark != null)
            {
                dark.Rot = rot;
                dark.Snap = true;
                Color soil = Fx.Darken(Fx.SoilColor(Combat.TileAt(from)), 0.5f);
                dark.Color(Fx.WithA(soil, 0.9f), Fx.WithA(soil, 0.8f), Fx.WithA(soil, 0f));
            }
            Particle glow = Fx.Spawn(ChoreoArt.Fissure(px, variant), from, Px, 1.8f, FxLayer.Ground, 3);
            if (glow != null)
            {
                glow.Rot = rot;
                glow.Snap = true;
                glow.Color(Fx.WithA(Color.white, bright), Fx.WithA(DivineArts.Verdant, 0.85f * bright), Fx.WithA(_holy.Edge, 0f));
            }
        }

        /// <summary>The gale out of the column: every enemy around swept far away.</summary>
        private void Sweep(Vector2 at)
        {
            Choreo.Foes(A, at, _r, _scan, true, true);
            float mult = WorldTreePillarTech.Mult(Level);
            int n = 0;
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                Vector2 rel = u.current_position - at;
                float d = Mathf.Clamp01(rel.magnitude / Mathf.Max(0.1f, _r));
                Impact im = Impact.Of(Hurt.Launch, rel.sqrMagnitude > 1e-4f ? rel : Random.insideUnitCircle, Mathf.Lerp(5.5f, 2.8f, d) * (0.7f + 0.1f * Level), 1.5f);
                im.Lift = Mathf.Lerp(2.2f, 1.2f, d);
                HitResult r = DivineArts.Smite(this, u, mult * Mathf.Lerp(1f, 0.55f, d), im);
                if (r.Landed)
                {
                    n++;
                }
            }
            if (n > 0)
            {
                SetLink(Link.Scattered, null);
            }
        }

        // ------------------------------------------------------------- the bath --

        private void TickBathe(float k)
        {
            float dt = Dt();
            Column(Wide, 1f, false);
            Pose.Height = (0.38f + 0.05f * Mathf.Sin(BeatT * 6f)) * Kk;
            _pulseT -= dt;
            if (_pulseT <= 0f)
            {
                _pulseT = 0.15f;
                Pulse();
            }
            _fxT -= dt;
            if (_fxT <= 0f && Vis)
            {
                _fxT = 0.06f;
                Leaves(1, 0.4f, false);
                ChoreoFx.Motes(BodyPos(), Px, 2, 14f, 0.25f, DivineArts.Verdant, _holy.Edge, TopLayer);
            }
        }

        /// <summary>One pulse of the healing light.</summary>
        private void Pulse()
        {
            int chunk = Mathf.Max(1, _healTotal / 6);
            int left = _healTotal - _healed;
            if (left <= 0 || !A.isAlive())
            {
                return;
            }
            int give = Mathf.Min(chunk, left);
            A.restoreHealth(give);
            _healed += give;
            if (Vis)
            {
                Fx.Ring(Pose.Ground, Px, 0.3f, 2f, 10f * Kk, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                    Fx.WithA(DivineArts.Verdant, 0.9f), Fx.WithA(_holy.Edge, 0f));
                Victims.Flash(A, DivineArts.Verdant, 0.05f);
            }
        }

        private void HealRest()
        {
            int left = _healTotal - _healed;
            if (left > 0 && A.isAlive())
            {
                A.restoreHealth(left);
                _healed = _healTotal;
            }
        }

        private void TickRise(float k)
        {
            float e = Beat.Apply(Ease.InQuad, k);
            Column(Mathf.Lerp(Wide, 1f, e), 1f - e, false);
            if (_glow != null && _glow.Alive)
            {
                // it draws back up: its foot leaves the ground first
                float h = 14f * Kk;
                float lift = h * e;
                Vector2 mid = Pose.Ground + new Vector2(0f, lift + (h - lift) * 0.5f);
                _glow.Pos = mid;
                _glow.SY = Mathf.Max(1f, (h - lift) / Px);
                if (_core != null && _core.Alive)
                {
                    _core.Pos = mid;
                    _core.SY = _glow.SY;
                }
            }
        }

        private void Landed()
        {
            if (Vis)
            {
                Fx.LandingPuff(Pose.Ground, Px, A.current_tile);
                Leaves(6, 1f, false);
            }
        }
    }
}
