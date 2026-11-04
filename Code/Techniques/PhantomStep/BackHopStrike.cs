using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// BACK HOP (Phantom Step, Novice). Too close, a swing coming: it hops back out of reach, light
    /// as a cat (untouchable in the air, violet afterimages hanging where it was), lands coiled on
    /// the back foot, trembling for a heartbeat, and springs back in with a long side kick that
    /// sends the foe skidding. II: a longer hop, and the spring is a flying side kick (a low leap
    /// into it, harder). III: a double hop, the second a back flip, then a spinning back kick that
    /// sends it reeling (a finisher).
    /// Planned against a foe in reach, with room behind to hop to; worth more when that foe is
    /// swinging at it or crowding it. Damage (x the user's damage): I 1.1; II 1.5; III 2.0.
    /// </summary>
    internal sealed class BackHopStrikeTech : Technique
    {
        public override string Id => "back_hop_strike";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Mobility;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Evasive | Form.Kick;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 16f };
        public override float[] Cooldown => new[] { 5f, 6f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.5f : 2f;
        }

        private static float Hop(int level)
        {
            return level == 2 ? 1.3f : 1f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.5f, Total = Mult(1), Worthy = false, Crowd = 0f, Alone = 4f, IntoGuard = 1f, Worth = 6f },
            new MeleeWant { Reach = 1.6f, Total = Mult(2), Worthy = false, Crowd = 0f, Alone = 5f, IntoGuard = 1f, Worth = 8f },
            new MeleeWant { Reach = 1.7f, Total = Mult(3), Worthy = false, Crowd = 0f, Alone = 6f, IntoGuard = 1f, Worth = 10f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            Combat.Allowed(c.Self, v, out float cm);
            if (v.getHealth() <= c.Expected(v, 0.9f) * cm)
            {
                return null;   // a plain blow does it
            }
            // room to hop back: safe ground behind it, away from the foe
            Vector2 away = c.Pos - (Vector2)v.current_position;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -p.Dir;
            float k = Choreo.KOf(c.Self);
            float hop = (Hop(level) + (level >= 3 ? 0.8f : 0f)) * k;
            Vector2 back = Combat.Clamp(c.Pos + away * hop);
            if (!Combat.SafeSpot(back) || !Combat.ClearPath(c.Pos, back) || c.WaterNear && !Combat.SafeSpot(c.Pos + away * hop * 1.2f))
            {
                return null;
            }
            FoeState s = c.State(v);
            if (s.AimsAtMe || s.Busy && !s.Charging)
            {
                p.Score += 10f;   // a swing is coming: out of it, and back in
            }
            if (c.Near2 >= 2)
            {
                p.Score += 5f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            PhantomMove m = PhantomMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            // out of reach: a light hop back, untouchable in the air
            m.BackHop(Hop(level), level == 2 ? 0.24f : 0.2f).Untouchable().Ghosting(0.025f)
                .OnEnter(_ => HopFx(m));
            if (level >= 3)
            {
                // and again, a back flip
                m.BackHop(0.8f, 0.26f).Untouchable().Ghosting(0.02f).OnEnter(_ => HopFx(m));
                m.Last.Spin = -360f;
                m.Last.Apex = 0.5f;
            }
            // it lands coiled on the back foot, trembling
            m.PoseTo(0.08f, new Shape(-12f, 1.14f, 0.84f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind).Shows(Form.Evasive | Form.Kick);
            m.Hold(0.05f, true, Tempo.Wind);
            // the spring back in
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.06f,
                PerTile = 0.035f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = ChoreoMove.ToTarget,
                Apex = level <= 1 ? 0.12f : level == 2 ? 0.45f : 0.22f,
                To = new Shape(16f, 1.18f, 0.86f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.022f,
                Lines = 0.03f,
                NeedsVictim = true,
                Forms = Form.Dash | Form.Kick | Form.Straight
            }).OnEnter(_ =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.DustRing(m.Stance, m.Px, m.A.current_tile, 6, 30f, 0.4f, m.Kk);
                }
            });
            Blow blow = level >= 3 ? Blow.SpinKick : Blow.SideKick;
            m.Strike(PhantomKit.Crack(new Hit(blow, Mult(level))
            {
                Hurt = Hurt.Reel,
                Push = level <= 1 ? 1f : level == 2 ? 1.4f : 1.9f,
                WindScale = 0.5f,
                Power = level == 2 ? 1.2f : 1f,
                Finisher = level >= 3
            }, m.Tier >= Tier.Master ? 0.4f : 0f));
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>A puff where it leaves the ground and a violet afterimage of it hanging in the air it left.</summary>
        private static void HopFx(PhantomMove m)
        {
            if (!m.Vis)
            {
                return;
            }
            Particle p = Fx.Afterimage(m, 0.26f, PhantomKit.Illusion(m, 0.7f), PhantomKit.Illusion(m, 0.45f), Fx.WithA(m.Hue.Edge, 0f));
            if (p != null)
            {
                p.R.sortingOrder += 1;
            }
        }
    }
}
