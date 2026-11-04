using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// AVATAR ASCENDANT (Divine, God). The God crouches and its aura erupts in a column of light
    /// (Charging: a fast blow breaks it) and it grows: in half a breath it stands two and a half
    /// times its height (III), wrapped in a spectral giant of its own colour, the ground cracking
    /// under its weight. Then it fights as a giant: it turns on the thickest knot of its enemies,
    /// takes one heavy step, and sweeps its arm through them: a huge crescent tears through the air
    /// and everyone in front of it is hurled away, a furrow ploughed along the arc. The sweeps come
    /// from alternating sides, and at III a two-handed hammer blow ends it (a crater, a ring of
    /// fallen foes). Then the avatar breaks apart into motes and the God is its own size again.
    /// I: x1.7, two sweeps; II: x2.1, three; III: x2.5, four and the hammer. Damage (x the user's
    /// damage) per sweep I 0.9, II 1.1, III 1.3; the hammer 1.8. Only enemies are struck; buildings
    /// under the furrows and the crater break.
    /// </summary>
    internal sealed class AvatarAscendantTech : Technique
    {
        public override string Id => "avatar_ascendant";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Punch | Form.Hook | Form.Area | Form.Charge | Form.Heavy | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 150f, 210f, 280f };
        public override float[] Cooldown => new[] { 120f, 140f, 160f };

        public static float Giant(int level)
        {
            return level <= 1 ? 1.7f : level == 2 ? 2.1f : 2.5f;
        }

        public static int Sweeps(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.1f : 1.3f;
        }

        /// <summary>How far a giant sweep reaches (tiles, before the user's size).</summary>
        public static float Reach(int level)
        {
            return 1.6f + 1.3f * Giant(level);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Reach(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int n = 0;
            float threat = 0f, score = 0f, best = -1f;
            Actor main = null;
            float total = Mult(level) * Mathf.Min(Sweeps(level), 2.2f) * 0.7f + (level >= 3 ? 0.6f : 0f);
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > r + 1.5f)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Combat.Grounded(v) || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
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
                score += c.Value(v, c.Expected(v, total), cm);
            }
            if (main == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                bool worthy = Tiers.Of(main) >= Tier.Master || best >= 0.3f;
                if (n < (level <= 1 ? 3 : 4) && !worthy || !DivineArts.Warranted(c, main, n, threat))
                {
                    return null;
                }
                if (c.WaterNear && n < 6)
                {
                    score *= 0.8f;   // (a giant's footing)
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
                Point = main.current_position,
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
            AvatarMove m = new AvatarMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Avatar Ascendant's move: the ascension, the giant's sweeps, the hammer, the return.</summary>
    internal sealed class AvatarMove : ChoreoMove
    {
        private readonly float _g, _r;
        private Particle _aura, _aura2;
        private float _lastT, _fxT;
        private int _sweep;
        private Vector2 _goal;
        private static readonly List<Actor> _scan = new List<Actor>();

        public AvatarMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _g = AvatarAscendantTech.Giant(Level);
            _r = AvatarAscendantTech.Reach(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            SetVictim(null);
            Face(p.Point - Pose.Ground);
        }

        /// <summary>A pose of the giant: the shape's squash and stretch on top of its giant size.</summary>
        private Shape G(float rot, float sx, float sy, float lunge = 0f)
        {
            return new Shape(rot, sx * _g, sy * _g, 0f, lunge);
        }

        public void Build()
        {
            // the crouch: the aura gathers
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.16f,
                To = new Shape(8f, 1.14f, 0.84f, 0f),
                Ease = Ease.OutQuad,
                Charging = true,
                Track = false,
                Forms = Form.Area | Form.Charge | Form.Heavy,
                Enter = m => ((AvatarMove)m).EnterAscend()
            });
            // the ascension: it grows into the giant
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.5f : 0.4f,
                To = G(-4f, 1f, 1f),
                Ease = Ease.OutBack,
                Charging = true,
                Track = false,
                Forms = Form.Area | Form.Charge | Form.Heavy | Form.Ultimate,
                Every = (m, k) => ((AvatarMove)m).TickAscend(k),
                Exit = m => ((AvatarMove)m).Ascended()
            });
            int n = AvatarAscendantTech.Sweeps(Level);
            for (int i = 0; i < n; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                // a heavy step towards the thickest knot of them
                Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.14f,
                    PerTile = 0.05f,
                    Route = Route.Line,
                    PathEase = Ease.InOutSine,
                    Dest = m => ((AvatarMove)m).StepTo(),
                    To = G(6f, 1.04f, 0.97f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear(),
                    Forms = Form.Punch | Form.Hook | Form.Area | Form.Heavy,
                    Exit = m => ((AvatarMove)m).Footfall()
                });
                // the wind: twisted away, the arm drawn back
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.15f,
                    To = G(-22f * side, 0.95f, 1.04f, -0.05f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear(),
                    Forms = Form.Punch | Form.Hook | Form.Area | Form.Heavy
                });
                // the sweep
                Beat sweep = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.14f,
                    To = G(22f * side, 1.12f, 0.92f, 0.12f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear(),
                    Forms = Form.Punch | Form.Hook | Form.Area | Form.Heavy | Form.Ultimate
                };
                float s = side;
                sweep.On(0.3f, m => ((AvatarMove)m).Sweep(s));
                Add(sweep);
                Add(new Beat
                {
                    Tempo = Tempo.Follow,
                    Dur = 0.12f,
                    To = G(10f * side, 1.04f, 0.97f, 0.06f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear()
                });
            }
            if (Level >= 3)
            {
                // the hammer: both fists raised high and brought down
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.2f,
                    To = G(-14f, 0.9f, 1.14f, -0.06f),
                    Ease = Ease.OutCubic,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear(),
                    Forms = Form.Punch | Form.Overhead | Form.Area | Form.Heavy | Form.Ultimate,
                    Enter = m => m.Face(((AvatarMove)m).Densest() - m.Stance)
                });
                Beat slam = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.13f,
                    To = G(26f, 1.16f, 0.82f, 0.15f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    If = m => ((AvatarMove)m).AnyNear(),
                    Forms = Form.Punch | Form.Overhead | Form.Area | Form.Heavy | Form.Ultimate
                };
                slam.On(0.4f, m => ((AvatarMove)m).Hammer());
                Add(slam);
                Hold(0.12f, true, Tempo.Impact);
            }
            // the avatar breaks apart: its own size again
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.3f,
                To = new Shape(0f, 0.94f, 1.08f, 0f, 0f),
                Ease = Ease.OutCubic,
                Track = false,
                Enter = m => ((AvatarMove)m).EnterDescend(),
                Every = (m, k) => ((AvatarMove)m).TickDescend(k)
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

        // ------------------------------------------------------------ ascension --

        private void EnterAscend()
        {
            _lastT = 0f;
            DivineArts.Open(this, "avatar_ascendant", 0.3f, 0.5f, 0.5f);
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 10, 26f, 0.5f, Kk);
            }
        }

        private void TickAscend(float k)
        {
            float dt = Dt();
            if (_aura == null && k >= 0.25f)
            {
                MakeAura();
            }
            if (!Vis)
            {
                return;
            }
            _fxT -= dt;
            if (_fxT <= 0f)
            {
                _fxT = 0.035f;
                // the column of its aura: flames of light rising round it
                int n = Fx.Count(3);
                float w = 4f * _g * Kk;
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = Pose.Ground + new Vector2(Random.Range(-w, w), Random.Range(-1f, 1f)) * Px;
                    Particle p = Fx.Spawn(Art.Puffs[Random.Range(0, Art.Puffs.Length)], at, Px, Random.Range(0.3f, 0.5f), TopLayer, 4);
                    p?.Moving(new Vector2(0f, Random.Range(50f, 110f) * Px * _g), 0f, 1.5f).Sized(0.8f, 0.3f)
                        .Color(Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                }
            }
            if (k >= 0.5f && k - 0.5f < 0.08f)
            {
                Fx.Ring(Pose.Ground, Px, 0.4f, 4f, 30f * _g * Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            }
        }

        private void Ascended()
        {
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.Crater(Pose.Ground, Px * Kk * _g * 0.7f, A.current_tile, false, 5f, false, Hue.Edge);
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 16, 50f, 0.7f, Kk * _g * 0.6f);
                Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(8f * _g), 0.12f, Fx.WithA(Hue.Core, 0.9f), TopLayer, 12);
            }
            Ruin.Quake(A, Pose.Ground, Scale, 1f, Kk);
            Ruin.Blast(A, Pose.Ground, 0.45f, Scale, Pose.Ground, false, -1);
            // the giant brawls in the light
            EndCinematic();
        }

        private void MakeAura()
        {
            Sprite s = Fx.UnitSprite(A);
            if (s == null)
            {
                return;
            }
            _aura = Glue(Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Behind, 0, true));
            if (_aura != null)
            {
                _aura.UseEuler = true;
            }
            if (Level >= 2)
            {
                _aura2 = Glue(Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Behind, -1, true));
                if (_aura2 != null)
                {
                    _aura2.UseEuler = true;
                }
            }
        }

        /// <summary>The spectral giant round it, drawn this frame (a silhouette a size bigger, pulsing).</summary>
        public override void Frame()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(T * 9f);
            float fade = _fade;
            GlueAura(_aura, 1.16f + 0.03f * pulse, Fx.WithA(Color.Lerp(Hue.Edge, Hue.Core, 0.3f), (0.55f + 0.2f * pulse) * fade));
            GlueAura(_aura2, 1.34f + 0.04f * (1f - pulse), Fx.WithA(Hue.Rim, (0.28f + 0.12f * (1f - pulse)) * fade));
            // flames of its aura licking up round the giant
            if (_aura != null && fade > 0.05f && Vis && T >= _flameAt)
            {
                _flameAt = T + 0.06f;
                float w = 3.5f * _g * Px;
                Vector2 at = Pose.Ground + new Vector2(Random.Range(-w, w), Random.Range(0f, Hpx * 0.5f * _g) * Px);
                Particle p = Fx.Spawn(Art.Puffs[Random.Range(0, Art.Puffs.Length)], at, Px, Random.Range(0.25f, 0.4f), FxLayer.Behind, 1);
                p?.Moving(new Vector2(0f, Random.Range(30f, 60f) * Px * _g), 0f, 1.5f).Sized(0.7f, 0.2f)
                    .Color(Fx.WithA(Hue.Core, 0.85f * fade), Fx.WithA(Hue.Edge, 0.55f * fade), Fx.WithA(Hue.Rim, 0f));
            }
        }

        private float _fade = 1f;
        private float _flameAt;

        private void GlueAura(Particle p, float scale, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            if (s != null && (object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = DrawPos();
            p.Z = 0f;
            p.Euler = Fx.UnitEuler(this);
            p.SX = A.actor_scale * Pose.SX * scale;
            p.SY = A.actor_scale * Pose.SY * scale;
            p.Grad = null;
            p.R.color = c;
            p.Tr.position = new Vector3(p.Pos.x, p.Pos.y, -0.02f);
            p.Tr.localScale = new Vector3(p.SX, p.SY, 1f);
            p.Tr.eulerAngles = p.Euler;
            p.R.enabled = c.a > 0.003f && !Pose.Hidden && (A.is_visible || Fx.OnScreen(Pose.Ground, 0.1f));
        }

        // ------------------------------------------------------------- the sweeps --

        /// <summary>How far one giant's stride carries it (tiles).</summary>
        private float Stride => (0.7f + 0.75f * _g) * Kk;

        /// <summary>Any of its enemies within reach of a giant's stride and a sweep?</summary>
        public bool AnyNear()
        {
            Choreo.Foes(A, Pose.Ground, _r + Stride + 0.4f, _scan);
            for (int i = 0; i < _scan.Count; i++)
            {
                if (Choreo.Fair(A, _scan[i], out _, true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The middle of the thickest knot of its enemies within a step and a sweep.</summary>
        public Vector2 Densest()
        {
            Choreo.Foes(A, Pose.Ground, _r + Stride + 0.4f, _scan);
            Vector2 best = Pose.Ground + Dir;
            int bestN = 0;
            float bestD = float.MaxValue;
            float rr = _r * 0.6f * _r * 0.6f;
            for (int i = 0; i < _scan.Count; i++)
            {
                Vector2 p = _scan[i].current_position;
                int n = 0;
                for (int j = 0; j < _scan.Count; j++)
                {
                    if ((_scan[j].current_position - p).sqrMagnitude <= rr)
                    {
                        n++;
                    }
                }
                float d = (p - Pose.Ground).sqrMagnitude;
                if (n > bestN || n == bestN && d < bestD)
                {
                    bestN = n;
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }

        public Vector2 StepTo()
        {
            _goal = Densest();
            Vector2 d = _goal - Stance;
            float dist = d.magnitude;
            if (dist > 1e-3f)
            {
                Face(d);
            }
            float step = Mathf.Min(Stride, dist - _r * 0.55f);
            if (step < 0.25f)
            {
                return Stance;
            }
            Vector2 to = Combat.Clamp(Stance + d / dist * step);
            return Combat.SafeSpot(to) && Combat.ClearPath(Stance, to) ? to : Stance;
        }

        public void Footfall()
        {
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 34f, 0.5f, Kk * _g * 0.5f);
                Fx.LandingPuff(Pose.Ground, Px * _g * 0.6f, A.current_tile);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile, 0.1f);
            Ruin.Quake(A, Pose.Ground, Scale.Casual, 1f, Kk);
        }

        /// <summary>The sweep lands: everyone in front of the giant within its reach is hurled away along the swing.</summary>
        public void Sweep(float side)
        {
            _sweep++;
            Vector2 at = Stance;
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            Choreo.Sound("event:/SFX/HIT/HitGeneric", A.current_tile, 0.05f);
            if (Vis)
            {
                // the crescent of the swing round the giant's body
                float a0 = 115f * side, a1 = -115f * side;
                int rpx = Mathf.Clamp(Mathf.RoundToInt(_r * 0.85f / Px), 8, 140);
                Kit.Swish(this, Art.Arc(rpx, 0.5f, a0, a1, Mathf.Clamp(Mathf.RoundToInt(2f * _g), 2, 6)), 0.2f,
                    new Vector2(0f, -Hpx * 0.1f * _g), Fwd, Fx.WithA(Hue.Core, 1f), Fx.WithA(Hue.Edge, 0.85f), 9);
                Kit.Swish(this, Art.Arc(Mathf.Max(6, rpx - 6), 0.45f, a0, a1, 1), 0.16f,
                    new Vector2(0f, -Hpx * 0.1f * _g), Fwd, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Core, 0.6f), 10);
                Kit.SpeedLines(BodyPos() + Dir * _r * 0.5f, Choreo.Perp(Dir) * -side, Px, 4, 18f, 0.14f, Fx.WithA(Hue.Edge, 0.8f));
            }
            Choreo.Foes(A, at, _r, _scan);
            float mult = AvatarAscendantTech.Mult(Level);
            int hit = 0;
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                Vector2 rel = u.current_position - at;
                if (rel.sqrMagnitude > 0.04f && Vector2.Angle(Dir, rel) > 105f)
                {
                    continue;   // behind the giant
                }
                Vector2 radial = rel.sqrMagnitude > 1e-4f ? rel.normalized : Dir;
                Vector2 tangent = Choreo.Perp(Dir) * -side;
                Impact im = Impact.Of(Hurt.Launch, radial + tangent * 0.8f, 2.4f + 0.4f * Level, 1.6f);
                im.Lift = 1.4f + 0.3f * Level;
                HitResult r = DivineArts.Smite(this, u, mult, im);
                if (r.Landed)
                {
                    hit++;
                    if (Vis)
                    {
                        ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), radial, Px, Kk, 1.3f, Hue, TopLayer);
                    }
                }
            }
            // the furrow along the arc
            float fr = _r * 0.7f;
            Vector2 prev = at + Choreo.Rotate(Dir, -50f) * fr;
            Kit.BeginTrench();
            try
            {
                for (int s = 1; s <= 3; s++)
                {
                    Vector2 next = at + Choreo.Rotate(Dir, -50f + 100f * s / 3f) * fr;
                    Ruin.Furrow(A, Combat.Clamp(prev), Combat.Clamp(next), 0.5f, Scale, at, false, -1);
                    prev = next;
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            if (Vis)
            {
                for (int s = 0; s < 5; s++)
                {
                    Vector2 g = at + Choreo.Rotate(Dir, -60f + 30f * s) * fr;
                    ChoreoFx.HeelDust(g, Choreo.Perp(Dir) * -side, Px, Combat.TileAt(g), Kk * 1.4f);
                }
            }
            Ruin.Quake(A, at, Scale, 1.1f, Kk);
            if (hit > 0)
            {
                Kit.Stop(at, 0.05f);
                SetLink(Link.Scattered, null);
            }
            if (Dev.On) Dev.Trace("avatar_ascendant sweep " + _sweep + ": " + hit + " of " + _scan.Count + " in reach " + _r.ToString("F1"));
        }

        /// <summary>III: both fists brought down ahead of it: a crater, everyone near it crushed down.</summary>
        public void Hammer()
        {
            Vector2 at = Combat.Clamp(Stance + Dir * _r * 0.45f);
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile, 0.2f);
            float mult = AvatarAscendantTech.Mult(Level) * 1.4f;
            Choreo.Foes(A, at, _r * 0.8f, _scan);
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                Vector2 rel = u.current_position - at;
                float d = Mathf.Clamp01(rel.magnitude / Mathf.Max(0.1f, _r * 0.8f));
                Impact im = Impact.Of(d < 0.4f ? Hurt.DriveIn : Hurt.Knockdown, rel.sqrMagnitude > 1e-4f ? rel : Dir, Mathf.Lerp(0.2f, 1.2f, d), 1.8f);
                im.Daze = 0.9f;
                DivineArts.Smite(this, u, mult * Mathf.Lerp(1f, 0.6f, d), im);
            }
            if (Kit.Visible(at))
            {
                Kit.ImpactFrame(at + new Vector2(0f, 0.3f * Kk), Px, 1.1f * Kk, Hue.Pal, false, TopLayer);
                Kit.Crater(at, Px * Kk * 1.6f, tile, true, 6f, false, Hue.Edge);
                Kit.Shockwave(at, Px, 4f, _r * 0.9f / Px, 0.45f, Fx.WithA(Hue.Core, 0.95f), 3);
                Kit.Debris(at, Px, tile, 14, Kk * 1.4f);
                Kit.DustRing(at, Px, tile, 18, 60f, 0.8f, Kk * 1.4f);
            }
            Kit.Stop(at, 0.09f);
            Ruin.Blast(A, at, 0.75f, Scale, Pose.Ground, true);
            Ruin.Quake(A, at, Scale, 1.5f, Kk);
            SetLink(Link.Down, null);
        }

        // -------------------------------------------------------------- descent --

        private void EnterDescend()
        {
            _lastT = 0f;
            if (Vis)
            {
                Vector2 body = BodyPos();
                int n = Fx.Count(18);
                for (int i = 0; i < n; i++)
                {
                    Vector2 off = new Vector2(Random.Range(-4f, 4f), Random.Range(-6f, 8f)) * Px * _g;
                    Particle p = Fx.Spawn(Art.Pixel, body + off, Px, Random.Range(0.4f, 0.7f), TopLayer, 7);
                    p?.Moving(off.normalized * Random.Range(20f, 50f) * Px + new Vector2(0f, 30f * Px), 0f, 2f).Sized(1.6f, 0.6f)
                        .Color(Fx.WithA(Hue.Core, 1f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                }
                Kit.Flash(body, Px, Mathf.RoundToInt(7f * _g), 0.1f, Fx.WithA(Hue.Core, 0.8f), TopLayer, 12);
            }
            Choreo.Sound("event:/SFX/POWERS/Blessing", A.current_tile, 0.2f);
        }

        private void TickDescend(float k)
        {
            _fade = 1f - k;
        }
    }
}
