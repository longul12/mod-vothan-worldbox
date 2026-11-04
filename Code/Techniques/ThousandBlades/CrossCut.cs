using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CROSS CUT (Thousand Blades, Adept). Two quick cuts that cross: one falling from high on one side,
    /// one rising from low on the other. Each leaves its line glowing on the foe, so an X of light hangs
    /// on its body. The swordsman lowers its blade and turns a hair away; the X burns brighter... and
    /// bursts: the arms of the X fly apart in rays, a ring of light, the foe thrown back.
    ///   I    the X, then the burst (the foe reels back)
    ///   II   faster cuts, a bigger burst on an impact frame; the foe reels further, dazed
    ///   III  the X and then a cross (a level cut and a falling one): an eight-pointed star on the foe
    ///        that bursts and knocks it off its feet
    /// A flurry of cuts (Blade | Flurry): what a clinch or an iron wall answers. Chains from Close.
    /// Damage: I 0.35 x2 + 1.0; II 0.4 x2 + 1.3; III 0.35 x4 + 1.6.
    /// </summary>
    internal sealed class CrossCutTech : Technique
    {
        public override string Id => "cross_cut";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float CutMult(int level)
        {
            return level == 2 ? 0.4f : 0.35f;
        }

        private static float BurstMult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.3f : 1.6f;
        }

        public static float Total(int level)
        {
            return CutMult(level) * (level >= 3 ? 4 : 2) + BurstMult(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2f, Total = Total(1), Worthy = true, Crowd = 0.06f, Alone = 6f, IntoGuard = 0.75f, Worth = 10f },
            new MeleeWant { Reach = 2.2f, Total = Total(2), Worthy = true, Crowd = 0.05f, Alone = 8f, IntoGuard = 0.75f, Worth = 12f },
            new MeleeWant { Reach = 2.4f, Total = Total(3), Worthy = true, Crowd = 0.04f, Alone = 10f, IntoGuard = 0.75f, Worth = 16f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            return Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
        }

        /// <summary>The burst's blow (no blade arc: the X itself bursts).</summary>
        private static readonly BlowShape Burst = new BlowShape
        {
            Limb = Limb.Blade,
            Weight = 1.25f,
            Hurt = Hurt.Reel,
            Push = 0.9f,
            Contact = 0.55f,
            SlashAngle = 50f,
            Forms = Form.Blade | Form.Flurry
        };

        public override bool Begin(Actor a, Plan p)
        {
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Blade | Form.Flurry;
            List<Particle> marks = new List<Particle>(8);
            float quick = lv <= 1 ? 1f : 0.85f;
            float cm = CutMult(lv);
            m.StepIn();
            // the X: a cut falling from high on one side...
            m.Cut(new Hit(Blow.DiagSlash, cm)
            {
                Hurt = Hurt.Flinch, Push = 0.08f, Lethal = false, Shows = forms, ActScale = quick,
                OnLand = (mm, v, r) => Mark(mm, v, 50f, marks)
            });
            // ...and one rising from low on the other
            m.Cut(new Hit(Blow.RisingSlash, cm)
            {
                Hurt = Hurt.Flinch, Push = 0.08f, Lift = 0f, Lethal = false, Shows = forms, WindScale = 0.55f, ActScale = quick,
                OnLand = (mm, v, r) => Mark(mm, v, -50f, marks)
            });
            if (lv >= 3)
            {
                // III: and a cross over it, a level cut and a falling one: an eight-pointed star
                m.Cut(new Hit(Blow.DrawCut, cm)
                {
                    Hurt = Hurt.Flinch, Push = 0.06f, Lethal = false, Shows = forms, WindScale = 0.5f, ActScale = 0.8f,
                    OnLand = (mm, v, r) => Mark(mm, v, 0f, marks)
                });
                m.Cut(new Hit(Blow.Cleave, cm)
                {
                    Hurt = Hurt.Flinch, Push = 0.06f, Lethal = false, Shows = forms, WindScale = 0.45f, ActScale = 0.8f,
                    OnLand = (mm, v, r) => Mark(mm, v, 90f, marks)
                });
            }
            // the blade lowered, a hair turned away: the mark burns brighter
            m.Posture(lv <= 1 ? 0.2f : lv == 2 ? 0.18f : 0.16f, new Shape(-5f, 0.97f, 1.03f, 0f, -0.04f), Tempo.Follow, -48f, forms);
            m.Last.Track = false;
            m.Last.NeedsVictim = false;
            // ...and it bursts
            m.Do(mm => Bursts((TblMove)mm, lv, marks));
            m.Posture(0.1f, new Shape(6f, 1.06f, 0.96f, 0f, 0f), Tempo.Follow, -40f, forms);
            m.Last.Track = false;
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>A cut's line stays glowing on the foe's body (two lines: a tier glow and a white core), pulsing.</summary>
        private static void Mark(ChoreoMove m, Actor v, float ang, List<Particle> marks)
        {
            if (!m.Vis || v == null)
            {
                return;
            }
            float px = m.Px;
            float w = TblFx.Mirror(ang, m.Fwd);
            float half = (4f + m.Level) * px * Mathf.Clamp(m.VHeight / Mathf.Max(0.04f, 10f * px), 0.8f, 2.6f);
            long id = v.Sid();
            for (int layer = 0; layer < 2; layer++)
            {
                bool core = layer == 1;
                Particle p = m.Glue(Fx.Spawn(Art.Pixel, Choreo.BodyAt(v, 0.55f), px, 1f, m.TopLayer, core ? 17 : 16));
                if (p == null)
                {
                    return;
                }
                p.SX = 2f * half / px * (core ? 0.9f : 1f);
                p.SY = core ? 1f : 2f;
                p.Rot = w;
                p.Grad = null;
                Hue hue = m.Hue;
                p.R.color = core ? Color.white : Fx.WithA(hue.Edge, 0.9f);
                p.Step = (q, d) =>
                {
                    if (v.Sid() == id && v.isAlive())
                    {
                        q.Pos = Choreo.BodyAt(v, 0.55f);
                    }
                    // it burns brighter while it waits, flickering
                    float glow = Mathf.Clamp01(q.Age / 0.3f);
                    float flick = 0.8f + 0.2f * Mathf.Sin(q.Age * 55f);
                    q.Grad = null;
                    q.R.color = core
                        ? Fx.WithA(Color.Lerp(hue.Core, Color.white, glow), flick)
                        : Fx.WithA(Color.Lerp(hue.Edge, hue.Core, glow * 0.5f), (0.6f + 0.35f * glow) * flick);
                    q.SY = core ? 1f : 2f + glow;
                };
                marks.Add(p);
            }
        }

        /// <summary>The mark bursts: its arms fly apart in rays, a ring, the blow (the foe thrown back).</summary>
        private static void Bursts(TblMove m, int lv, List<Particle> marks)
        {
            Vector2 at = m.V != null && m.V.Sid() == m.VId ? Choreo.BodyAt(m.V, 0.55f) : m.BodyPos() + m.Dir * m.Stand;
            float px = m.Px;
            if (m.Vis)
            {
                for (int i = 0; i < marks.Count; i++)
                {
                    Particle p = marks[i];
                    if (p == null || !p.Alive || i % 2 == 1)
                    {
                        continue;
                    }
                    // two rays along this arm, flying apart
                    float r = p.Rot * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
                    float half = p.SX * px * 0.5f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Particle ray = Kit.Line(at + d * s * half * 0.2f, at + d * s * half * 1.1f, px, 0.16f, lv >= 2 ? 2 : 1, m.TopLayer, 18,
                            Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                        ray?.Moving(d * s * (60f + 20f * lv) * px, 0f, 6f);
                    }
                }
                Kit.Flash(at, px, 3 + lv, 0.07f, Fx.WithA(Color.white, 0.95f), m.TopLayer, 22);
                Fx.Ring(at, px, 0.22f, 2f, (8f + 3f * lv) * m.Kk, lv >= 3 ? 2 : 1, 1, m.TopLayer, 6, 1f, 2.4f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
                Kit.Sparks(at, px, 5 + 2 * lv, 50f, 110f, m.Hue.Edge, m.TopLayer);
            }
            for (int i = 0; i < marks.Count; i++)
            {
                m.Unglue(marks[i]);
            }
            marks.Clear();
            if (!m.VOk)
            {
                return;
            }
            Hit h = new Hit(Blow.Slash, BurstMult(lv))
            {
                Hurt = lv >= 3 ? Hurt.Knockdown : Hurt.Reel,
                Push = lv <= 1 ? 0.9f : lv == 2 ? 1.2f : 0.8f,
                Daze = lv <= 1 ? 0f : lv == 2 ? 0.4f : 0.6f,
                Power = lv <= 1 ? 1f : lv == 2 ? 1.15f : 1.3f,
                Finisher = lv >= 2,
                Shows = Form.Blade | Form.Flurry
            };
            m.Deliver(m.V, h, Burst, at, m.Dir);
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", m.A.current_tile, 0.05f);
            if (Random.value < 0.5f)
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.1f);
            }
        }
    }
}
