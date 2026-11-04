using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// LUNGE THRUST (Thousand Blades, Novice). The gap closer of the sword. It coils (the weight back on
    /// the rear leg, the blade drawn back level along the arm, a glint running to its point), then
    /// throws its whole body forward in one long low stride, stretched out like a spear, a streak of
    /// light from where it stood to the point, and the thrust lands at the end of it: the foe is driven
    /// back on its heels. The thrust is held out a beat before it straightens.
    ///   I    a lunge of up to 2.6 tiles
    ///   II   up to 3.4 tiles, the foe driven further back
    ///   III  up to 4.2 tiles, and the thrust goes THROUGH: the point bursts out of its back and the
    ///        next foe behind it (within 1.9 tiles) is run through too
    /// A committed straight line: Iai Counter and every answer to a dash or a straight can meet it.
    /// Never from point blank (it needs room for the stride), never through walls or into water.
    /// Chains from Thrown and Scattered (it chases the body). Damage: I 1.2, II 1.5, III 1.9 (+1.2 on
    /// the one behind).
    /// </summary>
    internal sealed class LungeThrustTech : Technique
    {
        public override string Id => "lunge_thrust";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Mobility;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Dash | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 7f, 12f, 18f };
        public override float[] Cooldown => new[] { 5f, 5.5f, 6f };

        public override bool Accepts(Link link)
        {
            return link == Link.Thrown || link == Link.Scattered;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.9f;
        }

        private const float Pierce = 1.2f;

        /// <summary>How far the stride carries it (tiles, a human's size).</summary>
        public static float Lunge(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.4f : 4.2f;
        }

        private static readonly List<Actor> _ray = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, k, ref best, ref bestScore);
            float far = Lunge(level) * k + 1.6f;
            for (int i = 0; i < c.Enemies.Count && i < 8 && !(c.Forced && best != null && best == c.ForceTarget); i++)
            {
                if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(far, 7f) : far))
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], level, k, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        private void Consider(Ctx c, Actor v, int level, float k, ref Actor best, ref float bestScore)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v))
            {
                return;
            }
            if (v.isInLiquid() && !Choreo.Wading(v.current_position))
            {
                return;
            }
            float d = c.Dist(v);
            float reach = Lunge(level) * k + 0.7f * k + v.stats["size"] * 0.5f;
            if (c.Forced)
            {
                reach = Mathf.Max(reach, 7f);
            }
            // a lunge needs room for its stride: at point blank other blows do this
            if (d > reach || d < 1.3f * k && !c.Forced)
            {
                return;
            }
            if (!Combat.ClearPath(c.Pos, v.current_position))
            {
                return;
            }
            bool link = c.Linked && v == c.LinkTarget;
            if (!c.Forced && !link && v.getHealth() <= c.Expected(v, 0.8f) * cm)
            {
                return;   // an ordinary blow ends it
            }
            float score = c.Value(v, c.Expected(v, Mult(level)), cm);
            if (level >= 3)
            {
                // the one behind it is run through too
                Vector2 dir = (v.current_position - c.Pos).normalized;
                _ray.Clear();
                c.OnRay(dir, d + 1.9f * k, 0.45f, _ray);
                for (int i = 0; i < _ray.Count; i++)
                {
                    Actor u = _ray[i];
                    if (u != v && c.Dist(u) > d)
                    {
                        c.Allowed(u, out float cu);
                        score += c.Value(u, c.Expected(u, Pierce), cu);
                        break;
                    }
                }
            }
            if (d > 2.2f * k)
            {
                score += 8f;   // it closes the gap: what it is for
            }
            if (c.State(v).Ranged)
            {
                score += 10f;   // onto the archer, the mage
            }
            if (link)
            {
                score += 6f;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Blade | Form.Dash | Form.Straight;
            Vector2 from = m.Stance;
            // COIL: back on the rear leg, the blade drawn back level along the arm; a glint runs to its point
            m.Posture(lv <= 1 ? 0.12f : lv == 2 ? 0.13f : 0.15f, new Shape(-12f, 0.88f, 1.09f, 0f, -0.15f), Tempo.Wind, 3f, forms, true);
            m.At(0.7f, mm =>
            {
                if (mm.Vis)
                {
                    TblFx.Ting(((TblMove)mm).BladeTip, mm.Px, mm.Hue, lv >= 2 ? 0.9f : 0.7f);
                }
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
            });
            m.Dusting(0.06f);
            // LUNGE: one long low stride, stretched out like a spear; the thrust lands at its end
            Beat lunge = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = 0.032f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm => mm.StandSpot(mm.Stand * 0.92f),
                Chase = true,
                To = new Shape(12f, 1.36f, 0.78f, 0f, 0.24f),
                Ease = Ease.OutExpo,
                Ghosts = 0.024f,
                Lines = 0.03f,
                Dust = 0.04f,
                NeedsVictim = true,
                Forms = forms
            };
            lunge.Enter = mm =>
            {
                from = mm.Stance;
                TblMove t = (TblMove)mm;
                t.HoldBlade(-3f);
                t.Reach = 1.15f;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                    ChoreoFx.HeelDust(mm.Stance, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk * 1.3f);
                }
            };
            lunge.On(0.86f, mm => Thrust((TblMove)mm, lv, from));
            m.Add(lunge);
            // HELD: the thrust held out, long and still, then it straightens
            m.Posture(lv >= 3 ? 0.14f : 0.11f, new Shape(8f, 1.18f, 0.9f, 0f, 0.18f), Tempo.Follow, -5f, forms);
            m.Last.Track = false;
            m.Recover(0.22f);
            return m.Start();
        }

        private static readonly List<Actor> _behind = new List<Actor>();

        /// <summary>The point arrives: the foe is driven back; at III the one behind it is run through too.</summary>
        private static void Thrust(TblMove m, int lv, Vector2 from)
        {
            if (!m.VOk && !m.Retarget())
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            Vector2 tip = m.ContactPoint(0.55f);
            float px = m.Px;
            if (m.Vis)
            {
                // the line of the lunge: from where it stood to the point
                Vector2 a = from + new Vector2(0f, tip.y - m.Pose.Ground.y);
                TblFx.Streak(a, tip, px, 0.16f, lv >= 2 ? 3 : 2, m.Hue, FxLayer.Over);
                Kit.SpeedLines(Vector2.Lerp(a, tip, 0.6f), m.Dir, px, 3, 2.5f, 0.1f, Fx.WithA(m.Hue.Edge, 0.8f), FxLayer.Over);
            }
            float reach = m.Stand + 0.6f * m.Kk + v.stats["size"] * 0.5f + 0.35f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
            {
                // it slid out of reach: the point stabs the air
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    TblFx.Ting(m.BladeTip, px, m.Hue, 0.6f);
                }
                return;
            }
            if (!m.Touch(v))
            {
                return;   // answered: the engine plays the parry or the whiff
            }
            Hit h = new Hit(Blow.Thrust, Mult(lv))
            {
                Hurt = Hurt.Reel,
                Push = lv <= 1 ? 0.9f : lv == 2 ? 1.2f : 1.5f,
                Power = lv <= 1 ? 1f : lv == 2 ? 1.12f : 1.25f,
                Daze = lv >= 2 ? 0.3f : 0f,
                Finisher = lv >= 3,
                Shows = Form.Blade | Form.Dash | Form.Straight
            };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Thrust), tip, m.Dir);
            if (m.Vis)
            {
                TblFx.CutOn(v, 0.55f, m.Fwd > 0f ? 0f : 180f, 3.5f + lv, px, 0.05f, 0.14f, m.Hue, m.TopLayer);
            }
            if (lv < 3)
            {
                return;
            }
            // III: through the body and into the one behind it
            Vector2 back = m.VG + m.Dir * 2.3f * m.Kk;
            Choreo.FoesOnLine(m.A, m.VG, back, 0.65f, _behind);
            Actor next = null;
            float nd = float.MaxValue;
            for (int i = 0; i < _behind.Count; i++)
            {
                Actor u = _behind[i];
                if (u == v || !Choreo.Fair(m.A, u, out _, true))
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, m.VG);
                if (d < nd && Vector2.Dot(u.current_position - m.VG, m.Dir) > 0.1f)
                {
                    nd = d;
                    next = u;
                }
            }
            Vector2 exit = Choreo.BodyAt(v, 0.55f) + m.Dir * 0.6f * m.Kk;
            if (m.Vis)
            {
                // the point bursts out of its back
                TblFx.Streak(tip, next != null ? Choreo.BodyAt(next, 0.55f) : exit, px, 0.14f, 2, m.Hue, m.TopLayer, 0.02f);
                Kit.Flash(exit, px, 3, 0.06f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer, 22);
                Kit.Sparks(exit, px, 4, 50f, 100f, m.Hue.Edge, m.TopLayer);
            }
            if (Dev.On && next == null) Dev.Trace("lunge_thrust III: no one behind " + v.asset.id + "#" + v.Sid() + " (" + _behind.Count + " on the line)");
            if (next != null && m.Touch(next))
            {
                Hit h2 = new Hit(Blow.Thrust, Pierce) { Hurt = Hurt.Reel, Push = 1.1f, Power = 1.1f, Shows = Form.Blade | Form.Straight };
                m.Deliver(next, h2, ChoreoMove.ShapeOf(Blow.Thrust), Choreo.BodyAt(next, 0.55f), m.Dir);
                m.SetLink(Link.Close, Combat.Living(v) ? v : next);
            }
        }
    }
}
