using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SPINNING CUT (Thousand Blades, Novice). Crowded in, the swordsman twists low with the blade held
    /// back behind it, then turns right round on its heel: the blade sweeps a full circle at waist
    /// height, a ring of steel light around it and a ring racing out over the ground, and everyone
    /// within reach is cut and thrown outward (area blows: each may slip it, none can parry it).
    ///   I    one turn (1.35 tiles): everyone reels back
    ///   II   two turns (1.55 tiles): the first nicks them, the second throws them
    ///   III  two turns (1.8 tiles), the second a wide low sweep that knocks everyone off their feet;
    ///        a heartbeat later the cuts flash on every body at once
    /// For crowds: two foes at least within reach (one when linked or forced). Chains from Close.
    /// Damage to each (x the user's damage): I 1.1, II 0.45 + 0.95, III 0.5 + 1.25.
    /// </summary>
    internal sealed class SpinningCutTech : Technique
    {
        public override string Id => "spinning_cut";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Area;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Spin | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 9f, 16f, 26f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 1.35f : level == 2 ? 1.55f : 1.8f;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.4f : 1.75f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float r = Radius(level) * k;
            int n = 0;
            float score = 0f;
            Actor nearest = null;
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] > r + 1.5f)
                {
                    break;
                }
                if (c.EnemyDist[i] > r + v.stats["size"] * 0.5f || !Combat.Grounded(v) || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                n++;
                nearest ??= v;
                score += c.Value(v, c.Expected(v, Total(level)), cm);
            }
            if (nearest == null)
            {
                return null;
            }
            int need = c.Forced || c.Linked ? 1 : 2;
            if (n < need)
            {
                return null;
            }
            if (c.Surrounded)
            {
                score *= 1.15f;
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
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            float r = Radius(lv) * m.Kk;
            Form forms = Form.Blade | Form.Spin | Form.Area;
            // COIL: twisted low, the blade held back behind it; a glint along its edge
            m.Posture(lv <= 1 ? 0.1f : 0.12f, new Shape(-16f, 1.14f, 0.84f, 0f, -0.04f), Tempo.Wind, 168f, forms);
            m.OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 6, 18f, 0.35f, mm.Kk);
                }
            });
            m.At(0.75f, mm =>
            {
                if (mm.Vis)
                {
                    TblFx.Ting(((TblMove)mm).BladeTip, mm.Px, mm.Hue, 0.7f);
                }
            });
            if (lv <= 1)
            {
                Turn(m, 0.2f, r, 1.1f, Hurt.Reel, 0.8f, 1f, false);
            }
            else if (lv == 2)
            {
                Turn(m, 0.18f, r, 0.45f, Hurt.Flinch, 0.12f, 0.8f, false);
                Turn(m, 0.16f, r, 0.95f, Hurt.Reel, 1f, 1.15f, true);
            }
            else
            {
                Turn(m, 0.17f, r, 0.5f, Hurt.Flinch, 0.15f, 0.85f, false);
                Turn(m, 0.16f, r * 1.08f, 1.25f, Hurt.Knockdown, 0.9f, 1.35f, true);   // (the cuts flash a heartbeat later)
            }
            // the follow-through: the blade swept out level, the body still turning to a stop
            m.Posture(0.12f, new Shape(10f, 1.1f, 0.94f, 0f, 0.04f), Tempo.Follow, -10f, forms);
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>
        /// One turn on its heel: the sprite narrows, flips and widens (a 2D turn), the blade sweeps a full
        /// circle, a ring of steel light round the body and one racing out over the ground; at half way
        /// everyone within 'r' is cut ('mult', 'hurt', pushed 'push' out).
        /// </summary>
        private static void Turn(TblMove m, float dur, float r, float mult, Hurt hurt, float push, float power, bool last)
        {
            int lv = m.Level;
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = new Shape(4f, 1.08f, 0.96f, 0f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Blade | Form.Spin | Form.Area
            };
            float face = 1f;
            b.Enter = mm =>
            {
                face = mm.Fwd;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (!mm.Vis)
                {
                    return;
                }
                float px = mm.Px;
                int rad = Mathf.Clamp(Mathf.RoundToInt((10f + 1.5f * lv) * mm.Kk), 6, 50);
                Kit.Swish(mm, Art.Arc(rad, 0.42f, 0f, 360f, last ? 3 : 2), dur * 1.7f + 0.04f, new Vector2(0f, -1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                Fx.Ring(mm.Pose.Ground, px, 0.26f + 0.04f * lv, 3f * mm.Kk, r / px, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.2f,
                    Fx.WithA(mm.Hue.Core, 0.95f), Fx.WithA(mm.Hue.Edge, 0.6f), Fx.WithA(mm.Hue.Edge, 0f));
                Kit.DustRing(mm.Pose.Ground, px, mm.A.current_tile, last ? 10 : 6, last ? 36f : 26f, 0.45f, mm.Kk);
            };
            b.Every = (mm, k) =>
            {
                // it turns right round: the sprite narrows, flips, widens; the blade sweeps the circle
                float t = Beat.Apply(Ease.InOutSine, k);
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                mm.Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
                mm.Pose.FaceRight = t > 0.25f && t < 0.75f ? face < 0f : face > 0f;
                ((TblMove)mm).SwingBlade(-10f + 360f * t);
            };
            b.On(0.5f, mm =>
            {
                TblMove t = (TblMove)mm;
                float ang = TblFx.Mirror(lv >= 3 && last ? -8f : 6f, mm.Fwd);
                t.Sweep(mm.Pose.Ground, r, mult, hurt, push, power, ang, last && lv >= 3 ? 0.14f : 0.05f);
            });
            b.Exit = mm =>
            {
                mm.Pose.FaceRight = mm.Fwd > 0f;
                ((TblMove)mm).FreeBlade();
            };
            m.Add(b);
        }
    }
}
