using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// PILEDRIVER (Earth Breaker, Expert). It closes in and seizes the foe (one lying down is hauled
    /// up off the ground), flips it upside down in front of it, sinks under the weight, trembling,
    /// and leaps with it (the ground cracking under the take-off), hangs a heartbeat at the top, and
    /// comes down hard, driving it head first into the earth: an impact frame, a crater, a ring of
    /// dust and rock; the foe is left crushed upside down, then keels over (Down) with stars round
    /// its head. II leaps higher and the landing's shockwave knocks everyone near down; III flips the
    /// pair over at the top of a higher leap and the crater is wider. A grab: it beats a guard (and
    /// loses to a fast blow), it needs a body it can lift (no giants, no flyers). Damage (x the
    /// user's damage): I 2.4, II 3.2, III 4.4 (+ the shockwave x0.6 on the others).
    /// </summary>
    internal sealed class PiledriverTech : Technique
    {
        public override string Id => "piledriver";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Aerial | Form.Throw | Form.Overhead | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 20f, 36f, 60f };
        public override float[] Cooldown => new[] { 9f, 11f, 13f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3.2f : 4.4f;
        }

        private const float GrabMult = 0.15f;

        /// <summary>A body lying on its back in front of it (taken up off the ground).</summary>
        private static readonly Grip Low = new Grip(3.6f, -1.6f, -84f);

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Total(1), Hold = true, Crowd = 0.1f, Alone = 10f, IntoGuard = 1.2f, Worth = 14f },
            new MeleeWant { Reach = 2.6f, Total = Total(2), Hold = true, Crowd = 0.06f, Alone = 10f, IntoGuard = 1.2f, Worth = 18f },
            new MeleeWant { Reach = 3f, Total = Total(3), Hold = true, Crowd = 0.04f, Alone = 12f, IntoGuard = 1.2f, Worth = 24f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (MeteorSlam.AtDeathsDoor(p.Target))
            {
                return null;   // (already falling: nothing to spend it on)
            }
            // the leap needs open sky and solid ground: not from the water or into it
            if (!Combat.SafeSpot(p.Target.current_position) && !Choreo.Wading(p.Target.current_position)
                || !Combat.SafeSpot(c.Pos) && !Choreo.Wading(c.Pos))
            {
                return null;
            }
            // (II and III: the landing's shockwave makes a crowd round it worth more)
            if (level >= 2)
            {
                int near = c.CountNear(p.Target.current_position, 1.6f);
                p.Score += Mathf.Max(0, near - 1) * 6f;
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
            float rise = level <= 1 ? 1.5f : level == 2 ? 2.2f : 3f;
            bool down = m.V != null && (m.V.is_unconscious || m.V.hasStatusStunned());
            m.StepIn()
                .Grab(GrabMult, down ? Low : (Grip?)null)
                // up and over: the body is flipped upside down in front of it
                .Lift(Grip.Inverted, down ? 0.24f : 0.2f, new Shape(-8f, 0.96f, 1.06f, 0f))
                // it sinks under the weight (anticipation), trembling, then leaps with it
                .Crouch(0.12f, 1.2f);
            m.Last.Tremble = level >= 2;
            m.Last.If = mm => mm.Held != null;
            m.Rise(rise, level <= 1 ? 0.26f : 0.32f);
            m.Last.If = mm => mm.Held != null;
            m.Last.Enter = mm => TakeOff(mm);
            if (level >= 3)
            {
                // the pair turns over at the top of the leap
                m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.24f, Spin = 360f, To = new Shape(float.NaN, 0.94f, 1.06f, rise + 0.3f), Ease = Ease.OutQuad, Track = false, Ghosts = 0.03f, Forms = Forms, If = mm => mm.Held != null });
            }
            m.Hold(0.06f)
                .Slam(Total(level) - GrabMult, level <= 1 ? 0.9f : level == 2 ? 1.2f : 1.6f, level >= 2 ? (level == 2 ? 1.5f : 2.1f) : 0f, true, 0.13f, 0.6f);
            // (the landing: stars round the head it was driven in on)
            m.Do(mm =>
            {
                Actor v = mm.V;
                if (v != null && v.Sid() == mm.VId && Combat.Living(v) && !MeteorSlam.AtDeathsDoor(v))
                {
                    EarthFx.Dizzy(v, 1.2f + 0.3f * mm.Level, mm.Hue, mm.Level >= 2 ? 3 : 2);
                }
            });
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>The leap: a ring of dust, the ground cracking under it (II, III), a gust up past the bodies.</summary>
        private static void TakeOff(ChoreoMove m)
        {
            WorldTile tile = m.A.current_tile;
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", tile);
            if (m.Level >= 2)
            {
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.08f);
            }
            if (!m.Vis)
            {
                return;
            }
            Kit.DustRing(m.Stance, m.Px, tile, m.Level >= 2 ? 12 : 8, m.Level >= 2 ? 42f : 32f, 0.5f, m.Kk);
            Kit.SpeedLines(m.BodyPos(), Vector2.up, m.Px, 3, 3f, 0.16f, Fx.WithA(m.Hue.Edge, 0.75f));
            if (m.Level >= 2)
            {
                Kit.Crater(m.Stance, m.Px * m.Kk, tile, false, 4f, true, m.Hue.Edge);
                EarthFx.Cracks(m.Stance, m.Px, m.Level >= 3 ? 5 : 3, (m.Level >= 3 ? 0.8f : 0.55f) * m.Kk, m.Hue, tile, 1.4f);
            }
        }
    }
}
