using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GROUND POUND (Earth Breaker, Novice). Both fists go up over its head (it rises on its toes,
    /// stretched, a pixel's tremble at the top) and come down together into the ground in front of
    /// it: a flash at the knuckles, a star of fresh cracks, dust and clods thrown up, and a small
    /// shockwave racing out over the ground that sweeps everyone it passes off their feet (Down).
    /// II hops up first and drops its whole weight into the pound (a wider wave, a deeper dent);
    /// III pounds twice, left then right, the second blow a heavy one that cracks the ground wide
    /// open. Radius 1.5 / 2 / 2.5 tiles (x the user's size). Damage on each foe the wave passes
    /// (x the user's damage, less towards its edge): I 0.8, II 1.0, III 0.55 + 0.9.
    /// </summary>
    internal sealed class GroundPoundTech : Technique
    {
        public override string Id => "ground_pound";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Punch | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 9f, 16f, 26f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        /// <summary>The (last) wave's radius in tiles.</summary>
        public static float Radius(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 2.5f;
        }

        /// <summary>The (last) wave's damage.</summary>
        public static float Mult(int level)
        {
            return level <= 1 ? 0.8f : level == 2 ? 1f : 0.9f;
        }

        private const float FirstMult = 0.55f, FirstRadius = 1.7f;

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f);
            if (!c.Forced && !Combat.SafeSpot(c.Pos) && !Choreo.Wading(c.Pos))
            {
                return null;   // fists into water make a splash, not a wave
            }
            _near.Clear();
            int n = c.CountNear(c.Pos, r + 0.2f, _near);
            int need = c.Linked ? 1 : 2;
            if (n < need && !c.Forced)
            {
                return null;
            }
            float mult = Mult(level) + (level >= 3 ? FirstMult : 0f);
            float score = 0f;
            Actor nearest = null;
            float nd = float.MaxValue;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                if (MeteorSlam.AtDeathsDoor(v))
                {
                    continue;   // (already falling)
                }
                score += c.Value(v, c.Expected(v, mult * 0.85f), cm);
                if (Moves.Charging(v))
                {
                    score += 12f;   // tripped mid wind-up: the charge comes apart
                }
                float d = c.Dist(v);
                if (d < nd)
                {
                    nd = d;
                    nearest = v;
                }
            }
            if (nearest == null)
            {
                if (!c.Forced)
                {
                    return null;
                }
                nearest = c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = nearest != null ? nearest.current_position - c.Pos : Vector2.right;
            return new Plan
            {
                Target = nearest,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = Mathf.Max(1, n),
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
            int level = m.Level;
            if (level >= 3)
            {
                // the first pound: the left fist, a quick one
                Raise(m, 0.11f, false);
                Slam(m, FirstRadius, FirstMult, false, 0.6f, 0.12f);
                m.Hold(0.05f, false, Tempo.Follow);
            }
            if (level == 2)
            {
                // a hop: it springs up with both fists over its head and drops its whole weight into the pound
                m.Crouch(0.08f, 0.8f);
                m.Add(new Beat { Tempo = Tempo.Travel, Dur = 0.13f, To = new Shape(-14f, 0.86f, 1.2f, 0.32f), Ease = Ease.OutCubic, Ghosts = 0.04f, Forms = Form.Ground | Form.Punch | Form.Aerial });
                m.Hold(0.04f, true, Tempo.Wind);
                m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(40f, 1.34f, 0.68f, 0f, 0.08f), Ease = Ease.InQuad, Ghosts = 0.025f });
                m.Last.Exit = mm => Pound(mm, Radius(2), Mult(2), true);
            }
            else
            {
                Raise(m, level >= 3 ? 0.12f : 0.15f, level >= 3);
                Slam(m, Radius(level), Mult(level), level >= 3, level >= 3 ? 1.25f : 1f, level >= 3 ? -0.12f : 0f);
            }
            // the impact pose: crouched over its fists, a beat
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = level >= 3 ? 0.16f : 0.12f, To = new Shape(34f, 1.26f, 0.74f, 0f, 0.06f), Ease = Ease.OutQuad, Tremble = level >= 3 });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>Both fists go up over its head: stretched tall on its toes, leaning back, trembling at the top.</summary>
        private static void Raise(ChoreoMove m, float dur, bool heavy)
        {
            m.PoseTo(dur, new Shape(heavy ? -16f : -12f, 0.86f, 1.2f, 0f, -0.04f), Ease.OutCubic, Tempo.Wind);
            m.Last.Enter = mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                }
            };
            m.Hold(heavy ? 0.06f : 0.04f, true, Tempo.Wind);
        }

        /// <summary>The fists come down: the body snaps over them; at the bottom the ground takes it.</summary>
        private static void Slam(ChoreoMove m, float r, float mult, bool big, float ease, float side)
        {
            Beat b = new Beat { Tempo = Tempo.Act, Dur = 0.07f * ease, To = new Shape(40f, 1.3f, 0.7f, 0f, 0.08f, side), Ease = Ease.OutExpo };
            b.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                if (mm.Vis)
                {
                    Fx.Ghost(mm, new Vector2(0f, 2f * mm.Px), -14f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
                }
            };
            b.On(0.55f, mm => Pound(mm, r, mult, big));
            m.Add(b);
        }

        /// <summary>The fists hit the ground in front of it: cracks, dust, a dent, and the tripping wave.</summary>
        private static void Pound(ChoreoMove m, float r, float mult, bool big)
        {
            Vector2 at = m.Pose.Ground + m.Dir * 0.3f * m.Kk;
            if (!Combat.SafeSpot(at) && !Choreo.Wading(at))
            {
                at = m.Pose.Ground;
            }
            WorldTile tile = Combat.TileAt(at);
            float rr = r * Mathf.Lerp(1f, m.Kk, 0.5f);
            Areas.Add(new ShockwaveHazard(m, at, rr, big ? 12f : 10f, mult, Hurt.Knockdown, big ? 0.45f : 0.3f));
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.06f);
            if (m.Vis)
            {
                float px = m.Px;
                Kit.Flash(at + new Vector2(0f, px), px, Mathf.RoundToInt((big ? 4f : 3f) * m.Kk), 0.06f, Fx.WithA(m.Hue.Core, 0.95f));
                Kit.DustRing(at, px, tile, big ? 14 : 9, big ? 48f : 34f, 0.5f, m.Kk);
                Kit.Debris(at, px, tile, big ? 9 : 4, m.Kk);
                Kit.Crater(at, px * m.Kk, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 5f, !big || m.Level < 3, m.Hue.Edge);
                EarthFx.Cracks(at, px, big ? 6 : 4, rr * (big ? 0.75f : 0.55f), m.Hue, tile, big ? 2.2f : 1.4f);
                Fx.LandingPuff(m.Pose.Ground, px, tile);
                if (big)
                {
                    Kit.Shockwave(at, px, 3f, rr / px * 0.6f, 0.25f, Fx.WithA(m.Hue.Core, 0.9f), 2);
                }
            }
            Ruin.Blast(m.A, at, big ? 0.9f : 0.6f, m.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, at, m.Scale, big ? 1.3f : 0.9f, m.Kk);
            m.HoldFor((big ? 0.05f : 0.03f) * m.ImpactK);
            if (big && m.Tier >= Tier.Master)
            {
                Kit.Stop(at, 0.04f * m.ImpactK);
            }
            if (m.V != null && m.VOk)
            {
                // (the one it aimed at is swept off its feet with the rest, unless it stood clear)
                m.SetLink(Vector2.Distance(m.VG, at) <= rr + 0.3f ? Link.Down : Link.Close, m.V);
            }
        }
    }

    /// <summary>
    /// The Earth Breaker school's shared effects: the ground cracking open, stone flaking off, the
    /// stars that circle a rattled head, the colour of rock. Every call draws nothing off screen.
    /// </summary>
    internal static class EarthFx
    {
        private static readonly Color Grey = new Color(0.62f, 0.58f, 0.54f, 1f);

        /// <summary>Rock: the ground's own colour greyed (what the school tears up and wears).</summary>
        public static Color Stone(WorldTile t)
        {
            return Color.Lerp(Fx.SoilColor(t), Grey, 0.5f);
        }

        /// <summary>
        /// A star of fresh cracks round 'at' (a pound, a slam): 'n' fissures about 'len' tiles long, dark
        /// in the soil with a core glowing in the move's colours that cools off.
        /// </summary>
        public static void Cracks(Vector2 at, float px, int n, float len, Hue hue, WorldTile tile, float life = 1.6f)
        {
            if (!Kit.Visible(at) || tile == null || tile.Type == null || tile.Type.liquid)
            {
                return;
            }
            Color dark = Fx.Darken(Fx.SoilColor(tile), 0.55f);
            float a0 = Random.value * 360f;
            n = Mathf.Clamp(n, 1, 12);
            for (int i = 0; i < n; i++)
            {
                float a = (a0 + i * 360f / n + Random.Range(-22f, 22f)) * Mathf.Deg2Rad;
                int lp = Mathf.Clamp(Mathf.RoundToInt(len / px * Random.Range(0.65f, 1f)), 4, 120);
                float rot = Mathf.Atan2(Mathf.Sin(a) * Art.Ground, Mathf.Cos(a)) * Mathf.Rad2Deg;
                Particle f = Fx.Spawn(ChoreoArt.Fissure(lp, i), at, px, life, FxLayer.Ground, 2);
                if (f == null)
                {
                    return;
                }
                f.Rot = rot;
                f.Snap = true;
                f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                Particle g = Fx.Spawn(ChoreoArt.Fissure(lp, i), at, px, Mathf.Min(0.6f, life * 0.4f), FxLayer.Ground, 3);
                if (g != null)
                {
                    g.Rot = rot;
                    g.Snap = true;
                    g.Color(Fx.WithA(hue.Core, 0.9f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(hue.Edge, 0f));
                }
            }
        }

        /// <summary>
        /// Flakes of stone chipped off: 'n' chunks of colour 'c' thrown from 'z' world units above the
        /// ground point 'ground', away from 'away' (they fall, bounce and fade).
        /// </summary>
        public static void Flakes(Vector2 ground, float z, float px, int n, float k, Color c, Vector2 away)
        {
            if (!Kit.Visible(ground + new Vector2(0f, z)))
            {
                return;
            }
            n = Fx.Count(n);
            float ba = away.sqrMagnitude > 1e-4f ? Mathf.Atan2(away.y, away.x) : Mathf.PI * 0.5f;
            Color dark = Fx.Darken(c, 0.25f);
            for (int i = 0; i < n; i++)
            {
                float a = ba + Random.Range(-1.3f, 1.3f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], ground, px, Random.Range(0.45f, 0.75f), FxLayer.Top, 6);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(16f, 44f) * px * k, Random.Range(14f, 40f) * px * k, 0f, 260f * px * k);
                p.Z = z;
                p.Bounce = 0.25f;
                p.Spin = Random.Range(-600f, 600f);
                Color col = (i & 1) == 0 ? c : dark;
                p.Color(col, col, Fx.WithA(col, 0f));
            }
        }

        /// <summary>Over its head (a body lying flat: over its middle, low).</summary>
        private static Vector2 Head(Actor v)
        {
            float z = Moves.TryGet(v, out Move m) ? Mathf.DeltaAngle(0f, m.Pose.Rot) : Mathf.DeltaAngle(0f, v.current_rotation.z);
            return Mathf.Abs(z) > 50f ? Choreo.BodyAt(v, 0.1f) + new Vector2(0f, Choreo.SpritePx(v) * Choreo.PxOf(v) * 0.3f) : Choreo.BodyAt(v, 1.08f);
        }

        /// <summary>Little stars circling a rattled head for 'seconds' (a headbutt, a crushing slam).</summary>
        public static void Dizzy(Actor v, float seconds, Hue hue, int n)
        {
            if (v == null || !v.isAlive() || !Kit.Visible(v.current_position) || seconds <= 0.05f)
            {
                return;
            }
            float px = Choreo.PxOf(v);
            float r = Mathf.Max(3f, Choreo.SpritePx(v) * 0.32f) * px;
            long id = v.Sid();
            n = Mathf.Clamp(n, 1, 5);
            for (int i = 0; i < n; i++)
            {
                float ph = i * Mathf.PI * 2f / n;
                Particle p = Fx.Spawn(Art.Glint, Head(v), px, seconds, FxLayer.Top, 11);
                if (p == null)
                {
                    break;
                }
                p.Fps = 10f;
                p.Loop = true;
                p.Sized(0.6f, 0.5f);
                p.Color(Color.white, Fx.WithA(hue.Core, 0.95f), Fx.WithA(hue.Edge, 0.9f), Fx.WithA(hue.Edge, 0f));
                p.Step = (q, dt) =>
                {
                    if (v.Sid() != id || !v.isAlive())
                    {
                        q.Life = Mathf.Min(q.Life, q.Age + 0.05f);
                        return;
                    }
                    float t = q.Age * 7.5f + ph;
                    q.Pos = Head(v) + new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r * 0.35f);
                };
            }
        }
    }
}
