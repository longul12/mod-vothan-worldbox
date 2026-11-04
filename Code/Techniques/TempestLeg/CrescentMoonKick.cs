using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// CRESCENT MOON (Tempest Leg, Expert). The anti-air answer: whatever comes down on it from above (a leap,
    /// a dive, an overhead blow, a dash at it), it drops its weight onto the back foot and goes over in a
    /// backflip, the leg sweeping up in front of it and over its head: the foot carves a crescent of light
    /// through the air, meets the attacker on its way in and launches it (Launched: an air follow-up takes
    /// it). The attacker's move breaks on it (Parried). It also hangs a crescent moon of light where the
    /// leg passed, fading. Planned too: a launcher on a foe standing in reach (the air game opens), and an
    /// answer to a body already in the air within reach (kicked back up). II: a higher flip, a bigger
    /// moon, the foe launched higher on a heavier blow. III: two crescents (the moon and a wider one round
    /// it), an impact frame on the kick, the foe launched highest.
    /// Damage (x the user's damage): I 1.3; II 1.8; III 2.5.
    /// </summary>
    internal sealed class CrescentMoonKickTech : Technique
    {
        public override string Id => "crescent_moon_kick";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Launcher;
        public override bool Reactive => true;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Rising | Form.AntiAir;
        public override Form Answers => Form.Aerial | Form.Overhead | Form.Dash;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 7f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.8f : 2.5f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.9f, Total = Mult(1), Crowd = 0.04f, Alone = 6f, IntoGuard = 0.8f, Worth = 10f },
            new MeleeWant { Reach = 2.1f, Total = Mult(2), Crowd = 0.04f, Alone = 7f, IntoGuard = 0.8f, Worth = 12f },
            new MeleeWant { Reach = 2.3f, Total = Mult(3), Crowd = 0.04f, Alone = 8f, IntoGuard = 0.8f, Worth = 15f }
        };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || incoming == null || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            // the attacker must be coming in within the leg's sweep, and not a colossus it can't meet
            float k = Choreo.KOf(defender);
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.4f * k + attacker.stats["size"] * 0.5f
                || Combat.HeightOf(attacker) > 2.2f * k || attacker.stats["size"] > defender.stats["size"] * 3f)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            ChoreoMove m = ChoreoMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            CrescentMoon.Build(m, true);
            return m.Start() ? Contact.Parried : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                Plan air = AirTarget(c, level);
                if (air != null)
                {
                    return air;
                }
            }
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // a launcher: it opens the air game. A giant only staggers under it
            if (Weights.Heavy(p.Target))
            {
                p.Score *= 0.5f;
            }
            return p;
        }

        /// <summary>A foe in the air within the leg's sweep (falling on it, leaping at it, juggled by an ally): kick it back up.</summary>
        private Plan AirTarget(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 2.6f * k + 1f; i++)
            {
                Actor v = c.Enemies[i];
                if (!TempestKicks.Up(v, 0.35f) || Combat.HeightOf(v) > 2f * k || !Choreo.Fair(c.Self, v, out float cm))
                {
                    continue;
                }
                if (c.EnemyDist[i] > 2.4f * k + v.stats["size"] * 0.5f || !Combat.ClearPath(c.Pos, v.current_position))
                {
                    continue;
                }
                float s = c.Value(v, c.Expected(v, Mult(level)), cm) + 25f;
                if (s > bestScore)
                {
                    bestScore = s;
                    best = v;
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
                Data = false
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            if (p.Target != null && Combat.Grounded(p.Target))
            {
                m.StepIn();
            }
            CrescentMoon.Build(m, false);
            return m.Start();
        }
    }

    /// <summary>The crescent moon kick's choreography (shared by the answer and the planned launcher).</summary>
    internal static class CrescentMoon
    {
        private const Form Shows = Form.Kick | Form.Rising | Form.AntiAir;

        /// <summary>The foot's blow (for the engine's impact: weight, sound, the reaction's defaults).</summary>
        private static readonly BlowShape Foot = new BlowShape
        {
            Limb = Limb.Foot,
            Weight = 1.3f,
            Hurt = Hurt.Launch,
            Push = 0.8f,
            Lift = 1.8f,
            Contact = 0.6f,
            Heavy = true,
            Forms = Shows
        };

        public static void Build(ChoreoMove m, bool answer)
        {
            int level = m.Level;
            float mult = CrescentMoonKickTech.Mult(level);
            Hit hit = new Hit(Blow.Uppercut, mult)
            {
                Hurt = Hurt.Launch,
                Lift = level <= 1 ? 1.8f : level == 2 ? 2.3f : 2.9f,
                Push = level <= 1 ? 0.8f : level == 2 ? 1f : 1.2f,
                Power = level <= 1 ? 1.1f : level == 2 ? 1.25f : 1.45f,
                Finisher = level >= 2,
                Shows = Shows
            };
            // the read: the weight drops onto the back foot
            m.PoseTo(answer ? 0.03f : 0.07f, new Shape(-12f, 1.12f, 0.86f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind).Shows(Shows);
            // the backflip: over backwards, the leg sweeping up in front and over its head
            Beat flip = new Beat
            {
                Tempo = Tempo.Act,
                Dur = level >= 3 ? 0.34f : 0.3f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.3f * mm.Kk);
                    return Combat.SafeSpot(q) && Combat.ClearPath(mm.Stance, q) ? q : mm.Stance;
                },
                Apex = level <= 1 ? 0.5f : level == 2 ? 0.65f : 0.8f,
                Spin = -360f,
                To = new Shape(float.NaN, 1f, 1f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.025f,
                Forms = Shows,
                Tag = hit
            };
            flip.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                // the crescent the foot carves: from low in front, up and over to behind its head
                float life = mm.BeatDur * 1.5f;
                TempestKicks.Arc(mm, 11, 1f, -70f, 200f, 3, new Vector2(1f, 2f), life, 7);
                if (mm.Level >= 3)
                {
                    TempestKicks.Arc(mm, 16, 1f, -60f, 190f, 2, new Vector2(1f, 3f), life * 1.2f, 6);
                }
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 6, 30f, 0.4f, mm.Kk);
                }
            };
            flip.On(0.3f, mm => Kick(mm, hit));
            m.Add(flip);
            m.Land();
            m.Recover(answer ? 0.18f : 0.22f);
        }

        /// <summary>The foot meets the foe on its way up: it is launched (from the air too).</summary>
        private static void Kick(ChoreoMove m, Hit hit)
        {
            Vector2 tip = m.BodyPos() + new Vector2(m.Fwd * 4f, 5f) * m.Px;
            if (!m.VOk)
            {
                Moon(m, tip);
                return;   // it meets only air
            }
            Actor v = m.V;
            float reach = m.Stand + 0.9f * m.Kk + v.stats["size"] * 0.5f + 0.4f;
            float h = Combat.HeightOf(v);
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach || h > 2.4f * m.Kk)
            {
                Moon(m, tip);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                return;
            }
            if (!m.Touch(v))
            {
                return;   // answered: the engine plays it
            }
            Vector2 at = Choreo.BodyAt(v, 0.55f);
            m.Deliver(v, hit, Foot, at, m.Dir);
            if (Combat.Living(v))
            {
                Impact im = Impact.Of(Hurt.Launch, m.Dir, hit.Push, 1.3f);
                im.Lift = hit.Lift;
                im.Owed = MeteorSlam.AtDeathsDoor(v);
                TempestKicks.Aloft(m, v, im);
            }
            TempestKicks.Burst(m, at, Vector2.up, m.Level >= 3 ? 1.6f : 1.1f);
            Moon(m, Vector2.Lerp(tip, at, 0.5f));
            m.SetLink(Link.Launched, v);
        }

        /// <summary>The crescent moon the leg leaves hanging in the air where it passed, fading.</summary>
        private static void Moon(ChoreoMove m, Vector2 at)
        {
            if (!m.Vis)
            {
                return;
            }
            int r = Mathf.RoundToInt((m.Level <= 1 ? 10f : m.Level == 2 ? 12f : 14f) * Mathf.Lerp(1f, m.Kk, 0.6f));
            Particle p = Fx.Spawn(ChoreoArt.Crescent(r, m.Level >= 3 ? 4 : 3), at, m.Px, 0.45f + 0.12f * m.Level, m.TopLayer, 8);
            if (p == null)
            {
                return;
            }
            p.SX = m.Fwd;
            p.Rot = 25f * m.Fwd;
            p.Moving(new Vector2(0f, 6f * m.Px), 0f, 2f).Sized(0.9f, 1.15f);
            p.Color(Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            p.Cine = m.Cinematic;
            Fx.Glint(at + new Vector2(m.Fwd * r * 0.5f, r * 0.8f) * m.Px, m.Px, 0.2f, m.Hue.Core, 0.8f);
        }
    }
}
