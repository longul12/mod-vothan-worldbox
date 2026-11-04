using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI PALM (Ki Arts, Novice). It steps in and draws the palm back to the hip, gold light swelling
    /// in it, then drives the open hand into the foe's chest: the ki bursts at the contact (a flash,
    /// a ring of light, motes flung out), and the foe is shoved back 1.5 / 2 / 2.5 tiles inside a ring
    /// of ki that slides with it. II: a second ring right behind the first; III: the burst throws back
    /// whoever stands beside it too, scorches the ground (breaks it, from a Master's hand), the frame
    /// flashes, and the ring sheds embers all the way.
    /// Damage (x the user's damage): I 1.3, II 1.7, III 2.2 (+ x0.4 on those beside it at III).
    /// </summary>
    internal sealed class KiPalmTech : Technique
    {
        public override string Id => "ki_palm";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.KiArts;
        public override Form Forms => Form.Palm | Form.Ki;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 15f };
        public override float[] Cooldown => new[] { 4.5f, 5f, 5.5f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.7f : 2.2f;
        }

        private static float Push(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2f : 2.5f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2f, Total = Total(1), Worthy = false, Crowd = 0.04f, Alone = 4f, IntoGuard = 0.8f, Worth = 5f },
            new MeleeWant { Reach = 2.2f, Total = Total(2), Worthy = false, Crowd = 0.03f, Alone = 5f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2.4f, Total = Total(3), Worthy = false, Crowd = 0f, Alone = 6f, IntoGuard = 0.8f, Worth = 8f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Combat.Allowed(c.Self, p.Target, out float cm);
            if (p.Target.getHealth() <= c.Expected(p.Target, 0.7f) * cm)
            {
                return null;   // an ordinary blow ends it
            }
            // the shove makes room: worth a little more when they press in on it
            if (c.Near2 >= 2)
            {
                p.Score += 4f * Mathf.Min(3, c.Near2 - 1);
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Particle glow = null;
            bool atHip = true;
            m.StepIn()
                // the palm drawn back to the hip, light swelling in it
                .PoseTo(0.1f, new Shape(-12f, 0.92f, 1.06f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm =>
                {
                    glow = mm.Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt((1.5f + 0.5f * mm.Level) * mm.Kk)), 1f, 0.4f), Back(mm), mm.Px, 1f, FxLayer.Over, 9));
                    if (glow != null)
                    {
                        // (it rides in the palm: at the hip while it gathers, then driven forward with the strike)
                        glow.Step = (q, dt) => q.Pos = atHip ? Back(mm) : mm.Hand(Limb.Palm);
                    }
                })
                .Every((mm, k) =>
                {
                    if (glow == null || !glow.Alive)
                    {
                        return;
                    }
                    glow.Grad = null;
                    glow.Size0 = glow.Size1 = Mathf.Lerp(0.4f, 1.1f, k) * (1f + 0.12f * Mathf.Sin(mm.BeatT * 50f));
                    glow.R.color = Fx.WithA(Color.Lerp(mm.Hue.Edge, mm.Hue.Core, k), 0.85f);
                    if (mm.Vis && Random.value < 0.35f)
                    {
                        ChoreoFx.Motes(Back(mm), mm.Px, 1, 6f, 0.08f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                });
            m.Last.Exit = mm => atHip = false;
            m.Strike(new Hit(Blow.Palm, Total(level))
                {
                    Hurt = Hurt.Reel,
                    Push = Push(level),
                    WindScale = 0.45f,
                    Shows = Forms,
                    Power = 1.05f + 0.1f * level,
                    Finisher = level >= 3,
                    Area = level >= 3 ? 1.2f : 0f,
                    AreaMult = 0.4f / Total(3),
                    Crater = level >= 3 ? 0.4f : 0f,
                    OnLand = (mm, v, r) =>
                    {
                        if (glow != null)
                        {
                            mm.Unglue(glow);
                            glow = null;
                        }
                        Burst(mm, v);
                    }
                })
                .Recover(0.2f)
                .OnEnter(mm =>
                {
                    // (a palm that never landed: the light in it goes out)
                    if (glow != null)
                    {
                        mm.Unglue(glow);
                        glow = null;
                    }
                });
            return m.Start();
        }

        /// <summary>The palm drawn back at the hip (where the light gathers).</summary>
        private static Vector2 Back(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.18f, -m.Hpx * 0.06f) * m.Px;
        }

        /// <summary>The ki bursts at the contact: a flash, a ring of light, motes flung out, a ring round the foe's feet that slides with it.</summary>
        private static void Burst(ChoreoMove m, Actor v)
        {
            Choreo.Sound("event:/SFX/POWERS/Powerup", v.current_tile, 0.15f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px, k = m.Kk;
            int level = m.Level;
            Vector2 at = m.ContactPoint(0.55f);
            Kit.Flash(at, px, Mathf.RoundToInt((3f + level) * k), 0.07f, Fx.WithA(m.Hue.Core, 1f), m.TopLayer, 25);
            Fx.Ring(at, px, 0.2f, 2f, (7f + 3f * level) * k, 2, 1, m.TopLayer, 22, 1f, 2.2f,
                Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
            int n = Fx.Count(6 + 3 * level);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = (d + m.Dir * 0.8f).normalized;
                Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.18f, 0.32f), m.TopLayer, 21);
                p?.Moving(d * Random.Range(40f, 100f) * px * k, 0f, 5f).Sized(1.6f, 0.8f).Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
            KiFlight.FollowRing(v, m.Hue, px, 0.36f + 0.06f * level, 3f, (8f + 3f * level) * k, level >= 2 ? 2 : 1, level >= 3);
            if (level >= 2)
            {
                // a second ring right behind the first
                Particle second = Fx.Ring(at, px, 0.24f, 1f, (5f + 2f * level) * k, 1, 1, m.TopLayer, 21, 1f, 1.6f,
                    Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                if (second != null)
                {
                    second.Delay = 0.05f;
                }
                KiFlight.FollowRing(v, m.Hue, px, 0.3f, 2f, (6f + 2f * level) * k, 1, false);
            }
        }
    }
}
