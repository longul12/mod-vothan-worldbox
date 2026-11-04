using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// VANISHING COUNTER (Phantom Step, Expert). The blow lands clean... on an afterimage: the body
    /// it struck freezes, flashes white and shatters like glass into violet shards, while the real
    /// one is already in the air above the attacker, tucked, a ring of violet light under its feet.
    /// It hangs there a heartbeat and drops heel first onto the attacker's head, hammering it into
    /// the ground (driven in, Down). II: the ground cracks under it (a small crater), an impact
    /// frame. III: higher, a wider crater, and the shock knocks down everyone around (x0.5).
    /// Unlike Mirage Step (the copy bursts into dust, the real one is behind), the copy shatters
    /// and the answer comes from above.
    /// Reactive (Answers Strike | Dash | Straight): a blow or a charge from within 3 tiles, a
    /// grounded attacker. Damage (x the user's damage): I 1.4; II 1.9; III 2.6 (+ x0.5 around).
    /// </summary>
    internal sealed class VanishingCounterTech : Technique
    {
        public override string Id => "vanishing_counter";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Counter | Form.Kick;
        public override Form Answers => Form.Strike | Form.Dash | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 18f, 26f };
        public override float[] Cooldown => new[] { 8f, 7f, 6f };

        public static float Mult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.6f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;   // (an afterimage takes one blow, not a blast)
            }
            if (!Reach(defender, attacker))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return VanishingCounter.Begin(defender, attacker, level, this) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A grounded attacker within 3 tiles (it must be there to drop on).</summary>
        private static bool Reach(Actor d, Actor atk)
        {
            return Combat.Grounded(atk) && !atk.isFlying()
                && Vector2.Distance(d.current_position, atk.current_position) <= 3f * Choreo.KOf(d) + atk.stats["size"] * 0.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 6f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && VanishingCounter.Begin(a, p.Target, p.Level, this);
        }
    }

    /// <summary>Vanishing Counter's choreography.</summary>
    internal static class VanishingCounter
    {
        public static bool Begin(Actor d, Actor atk, int level, Technique tech)
        {
            if (d == null || atk == null || d == atk || !Combat.Living(atk))
            {
                return false;
            }
            PhantomMove m = PhantomMove.Answer(d, atk, tech, level);
            if (m == null || m.V == null)
            {
                return false;
            }
            level = m.Level;
            float k = m.Kk;
            // over a giant it hangs above its shoulders, not its knees
            float high = (level <= 1 ? 1.5f : level == 2 ? 1.7f : 1.95f) + Mathf.Max(0f, m.VHeight / Mathf.Max(0.3f, k) - 1.2f) * 0.6f;
            float mult = VanishingCounterTech.Mult(level);
            Form shows = Form.Teleport | Form.Counter | Form.Kick | Form.Aerial | Form.Overhead;
            m.Home = m.Stance;
            // the afterimage it struck
            m.Do(_ => m.Decoy = PhantomKit.Decoy(m, 0.2f, true));
            // gone, and up above the attacker
            Beat up = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.08f,
                Route = Route.Blink,
                Dest = mm => mm.StandSpot(mm.Stand * 0.7f),
                To = new Shape(-28f, 1.04f, 0.96f, high),
                Ease = Ease.OutQuad,
                Intangible = true,
                Hidden = true,
                NeedsVictim = true,
                Forms = shows
            };
            up.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Fx.Glint(m.BodyPos(), m.Px, 0.1f, Color.white, 0.9f);
                }
            };
            up.On(0.55f, _ =>
            {
                // the struck copy freezes white and shatters like glass
                if (m.Decoy != null && m.Decoy.Alive)
                {
                    m.Decoy.Kill();
                }
                m.Decoy = null;
                PhantomKit.Shatter(m, m.Home, level >= 3 ? 1.2f : 1f);
            });
            up.Exit = mm =>
            {
                if (m.V != null && (m.VG - m.Stance).sqrMagnitude > 1e-4f)
                {
                    m.Face(m.VG - m.Stance);
                }
                if (!m.Vis)
                {
                    return;
                }
                float px = m.Px;
                Vector2 feet = m.Pose.Ground + new Vector2(0f, m.Pose.Height);
                Fx.Ring(feet, px, 0.24f, 2f, 9f * m.Kk, 1, 1, FxLayer.Top, 6, Art.Ground, 2.4f, Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                Fx.Afterimage(m, 0.12f, Fx.WithA(Color.white, 0.85f), PhantomKit.Illusion(m, 0.5f), PhantomKit.Illusion(m, 0f));
                PhantomKit.Motes(m.BodyPos(), px, 6, 5f * m.Kk, 0.25f, m.Hue.Core, PhantomKit.Illusion(m, 1f));
                // the attacker looks up a beat too late
                Kit.Mark(m.V, m.Hue.Core, 0.25f, 0.8f);
            };
            m.Add(up);
            // it hangs a heartbeat at the top, tucked, the heel cocked
            m.PoseTo(0.07f, new Shape(-22f, 1.08f, 0.94f, high + 0.12f), Ease.OutQuad, Tempo.Follow).Shows(shows);
            // and comes down heel first on the head
            m.Strike(PhantomKit.Crack(new Hit(Blow.HeelDrop, mult)
            {
                Hurt = Hurt.DriveIn,
                Finisher = level >= 2,
                Power = level >= 3 ? 1.2f : 1f,
                WindScale = 1.15f,
                Shows = shows,
                OnLand = (mm, v, r) =>
                {
                    if (level >= 3)
                    {
                        Vector2 g = Victims.GroundOf(v);
                        mm.AreaBlow(g, 1.6f * mm.Kk, 0.5f * mult, v, Hurt.Knockdown, 0.5f);
                        if (mm.Vis)
                        {
                            Kit.Shockwave(g, mm.Px, 4f, 16f * mm.Kk, 0.35f, Fx.WithA(mm.Hue.Edge, 0.9f), 2);
                        }
                    }
                }
            }, level <= 1 ? 0f : level == 2 ? 0.6f : 0.9f));
            m.Recover(0.22f);
            return m.Start();
        }
    }
}
