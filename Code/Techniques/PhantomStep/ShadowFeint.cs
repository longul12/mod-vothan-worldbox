using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SHADOW FEINT (Phantom Step, Adept). Its shadow goes first: a violet silhouette of it peels
    /// off and lunges straight at the foe (readers see a dash and a straight punch coming), while
    /// the real one, a low blur nothing can touch, swings out wide and round to the foe's flank.
    /// Whatever the foe throws at the lunge cuts a shadow that bursts into violet smoke: it bit, its
    /// answer is spent and cut short, and it stands flat-footed; then the hook comes from the side it
    /// wasn't watching and spins it round. A foe that doesn't bite simply takes the hook from the side
    /// (the shadow fades). An answer made for feints sees through it (caught mid-run). II: the hook and a roundhouse.
    /// III: two shadows lunge, from the front and the far side, and after the hook and the
    /// roundhouse a spinning back kick knocks it off its feet (a finisher).
    /// Damage (x the user's damage): I 1.5; II 0.9 + 1.3; III 0.9 + 1.0 + 1.3.
    /// </summary>
    internal sealed class ShadowFeintTech : Technique
    {
        public override string Id => "shadow_feint";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Feint | Form.Clone;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 24f, 40f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        /// <summary>What readers see while the shadow lunges: a dash and a straight punch (and the bait it is).</summary>
        public const Form Lunge = Form.Dash | Form.Straight | Form.Punch | Form.Feint | Form.Clone;

        public static float Total(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2.2f : 3.2f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 3.2f, Total = Total(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 1.2f, Worth = 12f },
            new MeleeWant { Reach = 3.4f, Total = Total(2), Crowd = 0.06f, Alone = 9f, IntoGuard = 1.2f, Worth = 14f },
            new MeleeWant { Reach = 3.6f, Total = Total(3), Crowd = 0.05f, Alone = 10f, IntoGuard = 1.2f, Worth = 18f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            float k = Choreo.KOf(c.Self);
            float stand = 0.6f * k + v.stats["size"] * 0.3f;
            if (!Flank(c.Pos, v.current_position, stand, k, 1f, out _, out _) && !Flank(c.Pos, v.current_position, stand, k, -1f, out _, out _))
            {
                return null;   // no way round it
            }
            if (c.Guard(v, Form.Dash | Form.Straight | Form.Punch) > 0)
            {
                p.Score += 10f;   // an answer waiting for a lunge: give it one
            }
            if (Fighters.TryGet(v, out Fighter fv))
            {
                FoeNotes seen = fv.Recall(c.Self);
                if (seen != null && seen.Fooled > 0 && fv.Persona.Patience > 0.5f)
                {
                    p.Score *= 0.7f;
                }
            }
            return p;
        }

        /// <summary>
        /// The run round to the flank on side 's' of the target: out wide (the control point) and in
        /// beside it. False if the ground on the way or the spot itself won't take it.
        /// </summary>
        public static bool Flank(Vector2 from, Vector2 vg, float stand, float k, float s, out Vector2 spot, out Vector2 control)
        {
            Vector2 d = vg - from;
            float dist = d.magnitude;
            Vector2 dir = dist > 1e-3f ? d / dist : Vector2.right;
            Vector2 perp = Choreo.Perp(dir) * s;
            spot = Combat.Clamp(vg + perp * (stand + 0.05f * k) - dir * 0.08f * k);
            control = Combat.Clamp(from + dir * (dist * 0.4f) + perp * (stand + 1.1f * k));
            if (!Combat.SafeSpot(spot))
            {
                return false;
            }
            for (int i = 1; i <= 6; i++)
            {
                if (!Combat.Passable(Curve(from, control, spot, i / 6f)))
                {
                    return false;
                }
            }
            return true;
        }

        public static Vector2 Curve(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        public override bool Begin(Actor a, Plan p)
        {
            PhantomMove m = PhantomMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            float k = m.Kk;
            Vector2 home = m.Stance, vg = m.VG;
            float s = Random.value < 0.5f ? 1f : -1f;
            Vector2 spot, control;
            if (!Flank(home, vg, m.Stand, k, s, out spot, out control))
            {
                s = -s;
                if (!Flank(home, vg, m.Stand, k, s, out spot, out control))
                {
                    // nowhere round it: the shadow lunges, and it comes in right behind its shadow
                    spot = m.StandSpot(m.Stand);
                    control = Vector2.Lerp(home, spot, 0.5f);
                }
            }
            m.Side2 = s;
            m.Spot = spot;
            m.Home = home;
            // a coil: it sinks, the shadow peels off it
            m.PoseTo(0.08f, new Shape(-8f, 1.1f, 0.88f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind).Shows(Lunge);
            m.Do(_ => SendShadows(m, level));
            // the run round to its flank: a low blur nothing can touch; readers see the shadow's lunge
            Beat run = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.24f,
                To = new Shape(16f, 1.14f, 0.88f, 0f),
                Ease = Ease.OutQuad,
                Intangible = true,
                Ghosts = 0.022f,
                Dust = 0.05f,
                Forms = Lunge,
                NeedsVictim = true
            };
            run.Enter = mm =>
            {
                m.Home = m.Stance;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    ChoreoFx.HeelDust(m.Stance, m.Stance - control, m.Px, m.A.current_tile, m.Kk);
                }
            };
            run.Every = (mm, t) =>
            {
                Vector2 q = Curve(m.Home, control, m.Spot, Beat.Apply(Ease.InOutSine, t));
                m.Stance = q;
                m.Pose.Ground = q;
            };
            run.On(0.62f, _ =>
            {
                if (PhantomKit.Bait(m) == 1)
                {
                    CutShadow(m, m.Shadow);
                }
            });
            m.Add(run);
            // it bit: a beat untouchable at its flank while the answer finishes cutting the shadow
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.06f, Intangible = true, To = new Shape(-6f, 1.06f, 0.94f, 0f), Forms = Form.Evasive | Form.Feint })
                .OnlyIf(_ => m.Bit);
            m.Do(_ => PhantomKit.Punish(m, 0.45f));
            // from the side it wasn't watching
            bool master = m.Tier >= Tier.Master;
            m.Strike(new Hit(Blow.Hook, level <= 1 ? 1.5f : 0.9f) { Push = 0.5f, WindScale = 0.6f, Finisher = level == 1, Power = level == 1 ? 1.2f : 1f });
            if (level == 2)
            {
                m.Strike(PhantomKit.Crack(new Hit(Blow.Roundhouse, 1.3f) { Push = 0.9f, Finisher = true }, master ? 0.35f : 0f));
            }
            else if (level >= 3)
            {
                m.Strike(new Hit(Blow.Roundhouse, 1f) { Push = 0.6f });
                m.Strike(PhantomKit.Crack(new Hit(Blow.SpinKick, 1.3f) { Hurt = Hurt.Knockdown, Push = 1.2f, Finisher = true }, master ? 0.45f : 0f));
            }
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>The shadow (III: two) peels off and lunges at the foe: straight in front, and (III) from the far side.</summary>
        private static void SendShadows(PhantomMove m, int level)
        {
            if (m.V == null)
            {
                return;
            }
            Clone c = Clones.Spawn(m, m.Stance, true);
            if (c != null)
            {
                c.DashTo(() => Front(m, 0f), 0.16f).Wait(0.08f).Vanish(0.12f);
                m.Shadow = c;
            }
            if (level >= 3)
            {
                Clone c2 = Clones.Spawn(m, m.Stance, true);
                if (c2 != null)
                {
                    c2.Wait(0.03f).DashTo(() => Front(m, -m.Side2), 0.17f).Wait(0.06f).Vanish(0.12f);
                    m.Shadow2 = c2;
                }
            }
            if (m.Vis)
            {
                Fx.Afterimage(m, 0.16f, Fx.WithA(m.Hue.Core, 0.8f), PhantomKit.Illusion(m, 0f));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            }
        }

        /// <summary>Where a shadow's lunge ends: right in the foe's face (side 0), or at its side 's'.</summary>
        private static Vector2 Front(PhantomMove m, float s)
        {
            Vector2 vg = m.VOk ? m.VG : m.Spot;
            Vector2 d = vg - m.Home;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : m.Dir;
            Vector2 at = s == 0f ? vg - dir * m.Stand * 0.55f : vg + Choreo.Perp(dir) * s * m.Stand * 0.7f;
            at = Combat.Clamp(at);
            return Combat.SafeSpot(at) ? at : Combat.Clamp(vg - dir * m.Stand);
        }

        /// <summary>The answer cut the shadow: it bursts into violet smoke where it stood.</summary>
        private static void CutShadow(PhantomMove m, Clone c)
        {
            if (c == null || c.Done)
            {
                return;
            }
            Vector2 g = c.Pose.Ground;
            c.Kill();
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(g), 0.05f);
            if (!Kit.Visible(g))
            {
                return;
            }
            float px = m.Px;
            Vector2 chest = g + new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale);
            PhantomKit.Smoke(m, g, 0.6f * m.Kk, 8, 0.5f, 0.7f);
            Fx.Glint(chest, px, 0.12f, Color.white, 0.8f);
            PhantomKit.Motes(chest, px, 8, 6f * m.Kk, 0.3f, m.Hue.Core, PhantomKit.Illusion(m, 1f));
        }
    }
}
