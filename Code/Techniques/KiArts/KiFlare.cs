using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI FLARE (Ki Arts, Novice). The hands come together before the face, a point of light caught
    /// between them, and are thrown open: a blinding flash, rays of light, a bright ring racing over
    /// the ground. Every foe around flinches, white, its eyes full of light, and stands dazed a
    /// moment (I 0.6 s, II 0.9 s, III 1.2 s); one gathering power in reach has its charge broken.
    /// Farther off (5 / 6 / 7 tiles), any foe charging in plain sight and facing it is blinded too:
    /// its gathered power comes apart (an interrupt from afar). A light sting, not a blow (x0.3 /
    /// 0.4 / 0.5); radius 2.2 / 2.8 / 3.4 tiles.
    /// </summary>
    internal sealed class KiFlareTech : Technique
    {
        public override string Id => "ki_flare";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Area;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Interrupt | Form.Area;
        public override Form Answers => Form.Charge;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 7f, 11f, 16f };
        public override float[] Cooldown => new[] { 6f, 6.5f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float Radius(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 2.8f : 3.4f;
        }

        private static float Far(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 6f : 7f;
        }

        private static float DazeOf(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.2f;
        }

        private static float Mult(int level)
        {
            return level <= 1 ? 0.3f : level == 2 ? 0.4f : 0.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float sk = Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float r = Radius(level) * sk, far = Far(level) * sk;
            float score = 0f, charge = 0f;
            int n = 0;
            Actor first = null, charger = null;
            for (int i = 0; i < c.Enemies.Count && i < 20; i++)
            {
                float d = c.EnemyDist[i];
                if (d > far)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                bool charging = Moves.Charging(v);
                if (d <= r)
                {
                    n++;
                    first ??= v;
                    // a sting, and a foe blinded for a moment: it is open
                    score += c.Value(v, c.Expected(v, Mult(level)), cm) + 5f * Mathf.Clamp(Tiers.Power(v), 0.3f, 3f);
                }
                if (charging && (d <= r || Combat.ClearPath(c.Pos, v.current_position, true) && Faces(v, c.Pos)))
                {
                    float w = c.D * 3f + 30f + 10f * Tiers.Power(v);
                    if (w > charge)
                    {
                        charge = w;
                        charger = v;
                    }
                }
            }
            Actor target = charger ?? first;
            if (c.Forced)
            {
                target ??= c.ForceTarget ?? c.Focus;
                if (target == null)
                {
                    return null;
                }
                score = Mathf.Max(score + charge, 1f);
            }
            else
            {
                if (charger == null)
                {
                    if (n == 0)
                    {
                        return null;
                    }
                    // a single plain foe: not worth the light (a strong one, dazed, is wide open)
                    if (n == 1 && Tiers.Of(first) == Tier.None && Weights.Species(first) < Weight.Brute)
                    {
                        return null;
                    }
                }
                score += charge;
            }
            Vector2 dir = target.current_position - c.Pos;
            return new Plan
            {
                Target = target,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = Mathf.Max(1, n),
                Score = score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        /// <summary>Does 'v' face the point 'at' (it sees the flare)? Right in line with it: yes.</summary>
        private static bool Faces(Actor v, Vector2 at)
        {
            float dx = at.x - v.current_position.x;
            if (Mathf.Abs(dx) < 0.3f)
            {
                return true;
            }
            bool right = Moves.TryGet(v, out Move m) ? m.Pose.FaceRight : v.flip;
            return dx > 0f == right;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Particle spark = null;
            m.Face(p.Dir);
            // CUP: the hands come together before the face; a point of light caught between them
            m.PoseTo(0.11f, new Shape(-4f, 0.9f, 1.08f, 0f, -0.03f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm =>
                {
                    spark = mm.Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.4f * mm.Kk)), 1f, 0.2f), Cup(mm), mm.Px, 1f, FxLayer.Over, 10));
                    Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.15f);
                })
                .Every((mm, k) =>
                {
                    if (spark != null && spark.Alive)
                    {
                        spark.Pos = Cup(mm);
                        spark.Grad = null;
                        spark.Size0 = spark.Size1 = Mathf.Lerp(0.5f, 1.3f, k) * (1f + 0.25f * Mathf.Sin(mm.BeatT * 70f));
                        spark.R.color = Color.Lerp(mm.Hue.Edge, Color.white, k);
                    }
                    if (mm.Vis && Random.value < 0.5f)
                    {
                        ChoreoFx.Motes(Cup(mm), mm.Px, 1, 8f, 0.08f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                    if (k > 0.7f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.4f);
                    }
                });
            // FLARE: the hands thrown open, the light bursts out of them
            m.PoseTo(0.05f, new Shape(-12f, 1.18f, 0.88f, 0f, -0.06f), Ease.OutExpo, Tempo.Act).Shows(Forms)
                .OnEnter(mm =>
                {
                    if (spark != null)
                    {
                        mm.Unglue(spark);
                        spark = null;
                    }
                    Flare(mm);
                });
            // the arms stay flung open while the light dies
            m.Hold(0.13f, false, Tempo.Follow).Shows(Forms);
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>Between the cupped hands, before the face.</summary>
        private static Vector2 Cup(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(m.Fwd * m.Hpx * 0.2f, m.Hpx * 0.2f) * m.Px;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>The flash: everyone near is blinded (dazed, charges broken); chargers farther off who see it lose their power.</summary>
        private static void Flare(ChoreoMove m)
        {
            int level = m.Level;
            Actor a = m.A;
            float sk = Mathf.Lerp(1f, m.Kk, 0.5f);
            float r = Radius(level) * sk, far = Far(level) * sk;
            float daze = DazeOf(level);
            Vector2 at = Cup(m);
            Vector2 feet = m.Pose.Ground;
            Choreo.Sound("event:/SFX/POWERS/DivineLight", a.current_tile, 0.2f);
            if (m.Vis)
            {
                FlareFx(m, at, r);
            }
            Actor close = null;
            float cd = float.MaxValue;
            Choreo.Foes(a, feet, r, _near, true, true);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (!Choreo.Fair(a, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                // (a charging foe's charge breaks under the blow itself: the flare is an interrupt)
                Combat.Hit(a, u, Mult(level) * cm, true, false, 0f, m);
                if (!Combat.Living(u))
                {
                    continue;
                }
                Vector2 away = u.current_position - feet;
                Blind(m, u, away, daze);
                float d = away.sqrMagnitude;
                if (d < cd)
                {
                    cd = d;
                    close = u;
                }
            }
            // farther off: a charge gathered in plain sight of the flare comes apart
            Choreo.Foes(a, feet, far, _near, true, true);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if ((u.current_position - feet).sqrMagnitude <= r * r || !Moves.Charging(u) || !Faces(u, feet)
                    || !Combat.ClearPath(feet, u.current_position, true) || !Choreo.Fair(a, u, out _, true) || !m.Touch(u, true))
                {
                    continue;
                }
                if (Moves.TryGet(u, out Move cm) && cm.A == u && cm.Charging)
                {
                    if (Dev.On) Dev.Trace("ki_flare of " + a.asset.id + "#" + a.Sid() + " blinds the charging " + u.asset.id + "#" + u.Sid() + " from afar");
                    Interrupts.Disperse(cm, a);
                }
                if (Combat.Living(u))
                {
                    Victims.Flash(u, Color.white, 0.12f);
                    Combat.Daze(u, daze * 0.6f);
                    Kit.Mark(u, m.Hue.Core, daze * 0.6f, 0.8f);
                }
            }
            if (close != null && cd <= 2.2f * 2.2f * m.Kk * m.Kk)
            {
                m.SetLink(Link.Close, close);
            }
        }

        /// <summary>One foe in the light: it flinches away, white, and stands dazed with light in its eyes.</summary>
        private static void Blind(ChoreoMove m, Actor u, Vector2 away, float daze)
        {
            if (Moves.Charging(u))
            {
                return;   // (its charge is broken by the blow: it is thrown, not dazed)
            }
            Impact im = Impact.Of(Hurt.Flinch, away, 0.15f, 0.7f);
            im.Daze = daze;
            Victims.Hurt(u, m.A, m, im);
            if (Victims.Get(u) == null && !Moves.Locked(u))
            {
                Combat.Stagger(u, daze);   // (a giant, a flyer: it reels where it is)
            }
            Victims.Flash(u, Color.white, 0.14f);
            Kit.Mark(u, m.Hue.Core, daze, 0.8f);
        }

        private static void FlareFx(ChoreoMove m, Vector2 at, float r)
        {
            float px = m.Px, k = m.Kk;
            int level = m.Level;
            FxLayer top = m.TopLayer;
            Kit.Flash(at, px, Mathf.RoundToInt((6f + 2f * level) * k), 0.1f, Color.white, top, 31);
            // the bloom: a soft disk of light over everything near
            Particle bloom = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(r / px * 0.55f), 4, 120), 1f, 0.7f), at, px, 0.26f + 0.04f * level, top, 29);
            bloom?.Sized(0.45f, 1.3f).Color(Fx.WithA(Color.white, 0.7f), Fx.WithA(m.Hue.Core, 0.35f), Fx.WithA(m.Hue.Edge, 0f));
            // rays
            int rays = Fx.Count(8 + 3 * level);
            float spin = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < rays; i++)
            {
                float ang = spin + (i + Random.Range(-0.3f, 0.3f)) / rays * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float len = r * Random.Range(0.6f, 1.1f) * (i % 2 == 0 ? 1f : 0.7f);
                Kit.Line(at + d * 2f * px, at + d * len, px, 0.1f + 0.02f * level, 1, top, 28, Color.white, Fx.WithA(m.Hue.Core, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            }
            // the ring of light racing out over the ground
            Fx.Ring(m.Pose.Ground, px, 0.3f, 3f, r / px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Edge, 0.5f), Fx.WithA(m.Hue.Rim, 0f));
            Fx.FocusLines(at, px, Fx.Count(6 + 2 * level), 6f * k, 12f * k, 0.08f);
            if (level >= 3)
            {
                Kit.ImpactFrame(at, px, 0.45f * k, m.Hue.Pal, false, top);
                // the light lingers: motes drifting down where it burst
                int n = Fx.Count(10);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = at + Random.insideUnitCircle * r * 0.6f;
                    Particle mote = Fx.Spawn(Art.Pixel, p, px, Random.Range(0.4f, 0.7f), top, 9);
                    mote?.Moving(new Vector2(Random.Range(-3f, 3f), -Random.Range(4f, 9f)) * px, 0f, 1f).Sized(1.5f, 0.6f)
                        .Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Core, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
                }
            }
        }
    }
}
