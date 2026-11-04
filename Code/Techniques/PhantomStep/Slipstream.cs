using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SLIPSTREAM (Phantom Step, Adept). A dash or a line attack comes at it, and it rides the wake:
    /// it turns side-on, thins into a violet streak and is drawn along beside the attack's path,
    /// past the attacker, and out at its back, skidding round to face it while the attack spends
    /// itself on empty air; a violet ribbon hangs where it went. A line from afar (a beam, a thrown
    /// blade) carries it up to 6/8/10 tiles, to the shooter's back.
    /// I: it ends at the attacker's back (Close: the counter-attack follows at once). II: an elbow
    /// into the back as it turns (the attacker snaps forward). III: the elbow, then a knee that
    /// launches it (Launched).
    /// Reactive (Answers Dash | Line). Damage (x the user's damage): II 0.9; III 0.8 + 1.2.
    /// </summary>
    internal sealed class SlipstreamTech : Technique
    {
        public override string Id => "slipstream";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Evasive | Form.Dash;
        public override Form FormsAt(int level) => level >= 2 ? Form.Evasive | Form.Dash | Form.Elbow | Form.Counter : Form.Evasive | Form.Dash;
        public override Form Answers => Form.Dash | Form.Line;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 7f, 6f, 5f };

        /// <summary>How far it rides a line to the shooter's back (tiles x size).</summary>
        public static float Ride(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 8f : 10f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            float d = Vector2.Distance(defender.current_position, attacker.current_position);
            if (d > Ride(level) * Choreo.KOf(defender) + attacker.stats["size"] * 0.5f || !Combat.Grounded(attacker))
            {
                return Contact.Hit;   // too far to ride, or it comes from the sky
            }
            Reactions.Free(defender);
            return Slipstream.Begin(defender, attacker, level, this) ? Contact.Evaded : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > Ride(level) + 2f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && Slipstream.Begin(a, p.Target, p.Level, this);
        }
    }

    /// <summary>Slipstream's choreography.</summary>
    internal static class Slipstream
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
            Vector2 from = m.Stance, ag = m.VG;
            float stand = m.Stand + 0.08f * k;
            float bulge = 0.55f * k + atk.stats["size"] * 0.3f;
            // its back: behind the way it faces (a dash that went through it faces away from it already)
            Vector2 back = Back(m, stand);
            if (!Combat.SafeSpot(back))
            {
                Vector2 rel = ag - from;
                back = Combat.Clamp(ag + (rel.sqrMagnitude > 1e-4f ? rel.normalized : m.Dir) * stand);
            }
            bool ok = Combat.SafeSpot(back) && Clear(from, Control(from, back, ag, bulge), back);
            if (!ok)
            {
                // no way round to its back: out to its side instead
                float first = Random.value < 0.5f ? 1f : -1f;
                Vector2 rel = ag - from;
                Vector2 dir = rel.sqrMagnitude > 1e-4f ? rel.normalized : m.Dir;
                for (int i = 0; i < 2 && !ok; i++)
                {
                    Vector2 q = Combat.Clamp(ag + Choreo.Perp(dir) * (i == 0 ? first : -first) * stand);
                    if (Combat.SafeSpot(q) && Clear(from, Control(from, q, ag, bulge), q))
                    {
                        back = q;
                        ok = true;
                    }
                }
            }
            if (!ok)
            {
                return false;
            }
            m.Spot = back;
            m.Home = from;
            // what stood there: an afterimage stretched along the attack, torn by it
            m.Do(_ => m.Decoy = PhantomKit.Decoy(m, 0.24f, false));
            // the ride: side-on, a streak along the wake, untouchable
            Beat ride = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.06f,
                To = new Shape(20f, 1.34f, 0.72f, 0f),
                Ease = Ease.OutQuad,
                Intangible = true,
                Ghosts = 0.016f,
                Lines = 0.03f,
                Forms = Form.Evasive | Form.Dash
            };
            Vector2 last = from;
            ride.Enter = mm =>
            {
                m.Home = m.Stance;
                last = m.Stance;
                Vector2 control = Control(m.Home, m.Spot, m.VG, bulge);
                float path = Vector2.Distance(m.Home, control) + Vector2.Distance(control, m.Spot);
                mm.BeatDur += 0.02f * path * mm.TempoOf(Tempo.Travel);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    ChoreoFx.HeelDust(m.Stance, m.Stance - control, m.Px, m.A.current_tile, m.Kk);
                }
            };
            ride.Every = (mm, t) =>
            {
                // (an attacker still running on is followed: its back moves with it)
                if (m.VOk && t < 0.85f)
                {
                    Vector2 want = Back(m, stand);
                    if (Combat.SafeSpot(want) && Combat.Passable(Vector2.Lerp(m.Stance, want, 0.5f)))
                    {
                        m.Spot = want;
                    }
                }
                Vector2 q = ShadowFeintTech.Curve(m.Home, Control(m.Home, m.Spot, m.VG, bulge), m.Spot, Beat.Apply(Ease.InOutSine, t));
                Vector2 step = q - last;
                if (step.sqrMagnitude > 1e-6f)
                {
                    m.Face(step);
                }
                if (m.Vis && step.sqrMagnitude > 0.0025f)
                {
                    // the ribbon it leaves
                    Vector2 lift = new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale);
                    Kit.Line(last + lift, q + lift, m.Px, 0.32f, 2, FxLayer.Behind, 2, PhantomKit.Illusion(m, 0.9f), PhantomKit.Illusion(m, 0.5f), PhantomKit.Illusion(m, 0f));
                    Kit.Line(last + lift, q + lift, m.Px, 0.18f, 1, FxLayer.Behind, 3, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Core, 0f));
                    last = q;
                }
                m.Stance = q;
                m.Pose.Ground = q;
            };
            ride.Track = false;
            ride.On(0.35f, _ => PhantomKit.Pierce(m, m.Decoy, m.Hue));
            m.Add(ride);
            // out at its back: it skids round to face it
            m.PoseTo(0.08f, new Shape(-10f, 1.1f, 0.9f, 0f), Ease.OutQuad, Tempo.Follow).Dusting(0.03f)
                .OnEnter(_ =>
                {
                    if (m.V != null)
                    {
                        m.Face(m.VG - m.Stance);
                    }
                    if (m.Vis)
                    {
                        Vector2 v = m.Spot - m.Home;
                        ChoreoFx.Skid(m.Stance - (v.sqrMagnitude > 1e-4f ? v.normalized : -m.Dir) * 0.35f * m.Kk, m.Stance, m.Px, m.A.current_tile);
                    }
                });
            if (level >= 2)
            {
                m.Strike(new Hit(Blow.Elbow, level == 2 ? 0.9f : 0.8f) { Hurt = Hurt.SnapBack, Push = 0.6f, WindScale = 0.5f, Finisher = level == 2, Shows = Form.Elbow | Form.Counter | Form.Fast });
            }
            if (level >= 3)
            {
                m.Strike(PhantomKit.Crack(new Hit(Blow.Knee, 1.2f) { Hurt = Hurt.Launch, Push = 1.2f, Lift = 1.9f, Finisher = true }, m.Tier >= Tier.Master ? 0.4f : 0f));
            }
            if (level <= 1)
            {
                m.Leaves(Link.Close);
            }
            m.Recover(0.16f);
            return m.Start();
        }

        /// <summary>The attacker's back: 'stand' tiles behind the way it faces (its move's pose while it performs).</summary>
        private static Vector2 Back(ChoreoMove m, float stand)
        {
            Actor v = m.V;
            bool right = v != null && Moves.TryGet(v, out Move mv) ? mv.Pose.FaceRight : v != null && v.flip;
            return Combat.Clamp(m.VG + new Vector2(right ? -stand : stand, 0f));
        }

        /// <summary>The curve's control point: beside the line from a to b, on the side away from the attacker at 'ag' (it goes round it, never through it).</summary>
        private static Vector2 Control(Vector2 a, Vector2 b, Vector2 ag, float bulge)
        {
            Vector2 mid = Vector2.Lerp(a, b, 0.5f);
            Vector2 d = b - a;
            Vector2 perp = d.sqrMagnitude > 1e-4f ? Choreo.Perp(d.normalized) : Vector2.up;
            float side = Vector2.Dot(perp, ag - mid) > 0f ? -1f : 1f;
            return Combat.Clamp(mid + perp * side * bulge);
        }

        /// <summary>Can it glide from a through c to b (the curve's ground passable, b safe)?</summary>
        private static bool Clear(Vector2 a, Vector2 c, Vector2 b)
        {
            for (int i = 1; i <= 8; i++)
            {
                if (!Combat.Passable(ShadowFeintTech.Curve(a, c, b, i / 8f)))
                {
                    return false;
                }
            }
            return Combat.SafeSpot(b);
        }
    }
}
