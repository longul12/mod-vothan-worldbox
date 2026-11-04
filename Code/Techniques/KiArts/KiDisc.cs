using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI DISC (Ki Arts, Expert). A hand raised over the shoulder: a disc of ki forms above the palm,
    /// spinning faster and faster with a rising whine, a whirr of light round its rim. The arm draws
    /// back across the body and whips through sidearm: the disc flies out in a long curve, slicing
    /// through everyone on its line (cut lines flash across them, they spin away), turns at the end
    /// of its flight and comes back like a boomerang, cutting them again on its way home, into the
    /// thrower's hand with a flash. The thrower is free while it flies. I: 7 tiles; II: 8.5, bigger;
    /// III: 10, the biggest, cutting through up to 14 bodies. Damage per cut (x the user's damage):
    /// I 0.9, II 1.0, III 1.15.
    /// </summary>
    internal sealed class KiDiscTech : Technique
    {
        public override string Id => "ki_disc";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Line;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Blade;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 22f, 38f, 60f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8.5f : 10f;
        }

        private static float Each(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1f : 1.15f;
        }

        private static float Size(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.25f : 1.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            // (out and back: most of what it passes is cut twice)
            Plan p = Choreo.PickLine(c, this, level, Range(level), 0.5f, Each(level) * 1.6f, 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (c.Dist(p.Target) < 1.5f)
            {
                return null;   // point blank: no room to throw
            }
            if (c.Linked && p.Target == c.LinkTarget)
            {
                p.Score += 8f;
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
            Particle disc = null;
            m.Face(p.Dir);
            // CONJURE: a hand over the shoulder; the disc forms above the palm, spinning up
            m.PoseTo(0.3f, new Shape(-10f, 0.92f, 1.1f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm =>
                {
                    disc = mm.Glue(Fx.Spawn(ChoreoArt.Disc(Mathf.RoundToInt(4f * Size(mm.Level) * Mathf.Lerp(1f, mm.Kk, 0.6f))), Over(mm), mm.Px, 1f, FxLayer.Over, 10));
                    if (disc != null)
                    {
                        disc.Loop = true;
                        disc.Fps = 8f;
                    }
                    Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.15f);
                })
                .Every((mm, k) =>
                {
                    if (disc != null && disc.Alive)
                    {
                        disc.Pos = Over(mm);
                        disc.Fps = Mathf.Lerp(8f, 30f, k);
                        disc.Size0 = disc.Size1 = Mathf.Lerp(0.2f, 1f, Beat.Apply(Ease.OutQuad, k));
                        disc.Grad = null;
                        disc.R.color = Color.Lerp(mm.Hue.Edge, Color.Lerp(mm.Hue.Core, mm.Hue.Main, 0.25f), k);
                    }
                    if (mm.Vis && Random.value < 0.3f + 0.4f * k)
                    {
                        Vector2 at = Over(mm);
                        Vector2 side = new Vector2(Random.value < 0.5f ? -1f : 1f, Random.Range(-0.2f, 0.2f));
                        Particle sp = Fx.Spawn(Art.Sparks[0], at + side * 3f * mm.Px * k, mm.Px, 0.1f, mm.TopLayer, 11);
                        if (sp != null)
                        {
                            sp.Moving(side * Random.Range(30f, 60f) * mm.Px, 0f, 6f);
                            sp.FaceVel = true;
                            sp.Color(Color.white, mm.Hue.Edge, Fx.WithA(mm.Hue.Edge, 0f));
                        }
                    }
                    if (k > 0.8f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.4f);
                    }
                });
            // WIND: the arm draws back across the body, the disc with it
            m.PoseTo(0.08f, new Shape(-18f, 1.06f, 0.94f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .Every((mm, k) =>
                {
                    if (disc != null && disc.Alive)
                    {
                        disc.Pos = Vector2.Lerp(Over(mm), Cocked(mm), Beat.Apply(Ease.OutQuad, k));
                    }
                });
            // THROW: a sidearm whip, the body twisting through; the disc is away
            m.PoseTo(0.07f, new Shape(18f, 1.2f, 0.9f, 0f, 0.08f), Ease.OutExpo, Tempo.Act).Shows(Forms)
                .OnEnter(mm =>
                {
                    if (disc != null)
                    {
                        mm.Unglue(disc);
                        disc = null;
                    }
                    Throw(mm);
                });
            // the follow-through: it watches the disc go
            m.PoseTo(0.16f, new Shape(8f, 1.06f, 0.96f, 0f, 0.04f), Ease.OutQuad, Tempo.Follow).Shows(Forms);
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>Above the raised palm, over the shoulder.</summary>
        private static Vector2 Over(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.1f, m.Hpx * 0.85f) * m.Px;
        }

        /// <summary>The hand drawn back across the body for the throw.</summary>
        private static Vector2 Cocked(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.3f, m.Hpx * 0.12f) * m.Px;
        }

        /// <summary>The disc flies: out along a curve that passes over the target, back to the hand.</summary>
        private static void Throw(ChoreoMove m)
        {
            int level = m.Level;
            float sk = Mathf.Lerp(1f, m.Kk, 0.5f);
            Vector2 from = m.Hand(Limb.Palm) + new Vector2(0f, m.Hpx * 0.08f * m.Px);
            Vector2 aim = m.Aim;
            float d = Range(level) * sk;
            float speed = (14f + level) * Mathf.Lerp(1f, Choreo.KOf(m.A), 0.4f);
            if (m.VOk)
            {
                Vector2 to = m.VG - m.Stance;
                d = Mathf.Max(1f, to.magnitude);
                // (it curls 40 degrees a second: thrown a little to the side, its curve comes back across the target)
                float turn = 40f * d / speed;
                aim = Choreo.Rotate(to / d, -0.5f * turn);
                m.Face(to);
            }
            ShotSpec s = new ShotSpec
            {
                Kind = ShotKind.Disc,
                Flight = Flight.Boomerang,
                Speed = 14f + level,
                Range = Mathf.Max(Range(level) * sk, d + 1.5f),
                Size = Size(level),
                Mult = Each(level),
                Hurt = Hurt.Spin,
                Push = 0.5f,
                Pierce = level <= 1 ? 6 : level == 2 ? 9 : 14
            };
            KiFlight.Fire(m, from, aim, null, s, KiFlight.Style.Disc);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Kit.Swish(m, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(9f * Mathf.Lerp(1f, m.Kk, 0.6f)), 4, 40), 0.45f, 200f, 370f, 2), 0.12f,
                new Vector2(m.Fwd * 1f, 2f), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
            Kit.Flash(from, px, Mathf.RoundToInt(3f * m.Kk), 0.05f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer, 22);
            Kit.SpeedLines(from, aim, px, 3, 2.5f, 0.1f, Fx.WithA(m.Hue.Edge, 0.85f), m.TopLayer);
            Fx.Ghost(m, -m.Dir * 2f * px, -8f, 0.08f, Fx.WithA(m.Hue.Ghost, 0.55f), m.Hue.GhostEnd);
        }
    }
}
