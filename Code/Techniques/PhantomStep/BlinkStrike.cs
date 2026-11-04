using System;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// BLINK STRIKE (Phantom Step, Adept). It draws itself up, violet motes rushing into it, its
    /// outline pinches to a sliver and pops in a ring of violet light: gone. In the same heartbeat a
    /// ring opens on the ground behind the foe and it unfolds out of it, already striking: a palm
    /// into the back drives the foe stumbling forward (a blow from behind is read half as well).
    /// II: a second pop, round to its front as it stumbles in, and a knee meets it (it folds).
    /// III: a third pop, to its side, and a spinning backfist that launches it (Launched).
    /// Closes 4-5 tiles in an instant: good against archers and casters.
    /// Damage (x the user's damage): I 1.4; II 0.9 + 1.2; III 0.8 + 1.0 + 1.3.
    /// </summary>
    internal sealed class BlinkStrikeTech : Technique
    {
        public override string Id => "blink_strike";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Punch;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 2.1f : 3.1f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 4.5f, Total = Total(1), Crowd = 0.05f, Alone = 6f, IntoGuard = 1.05f, Worth = 10f },
            new MeleeWant { Reach = 5f, Total = Total(2), Crowd = 0.05f, Alone = 8f, IntoGuard = 1.05f, Worth = 12f },
            new MeleeWant { Reach = 5.5f, Total = Total(3), Crowd = 0.04f, Alone = 10f, IntoGuard = 1.05f, Worth = 16f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // there must be ground at its back to appear on
            Vector2 vp = v.current_position;
            Vector2 d = vp - c.Pos;
            Vector2 back = Combat.Clamp(vp + (d.sqrMagnitude > 1e-4f ? d.normalized : p.Dir) * (0.6f * Choreo.KOf(c.Self) + v.stats["size"] * 0.3f));
            if (!Combat.SafeSpot(back))
            {
                return null;
            }
            float dist = c.Dist(v);
            FoeState s = c.State(v);
            if (s.Ranged && dist > 2.5f)
            {
                p.Score += 12f;   // an archer or a caster keeping its distance: it is simply behind it
            }
            if (dist < 1.2f)
            {
                p.Score *= 0.75f;   // (it is already in reach: a blink is for getting there)
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
            bool master = m.Tier >= Tier.Master;
            // drawn up tall, motes rushing in
            m.PoseTo(0.07f, new Shape(-3f, 0.86f, 1.14f, 0f), Ease.OutQuad, Tempo.Wind).Shows(Form.Teleport | Form.Punch)
                .OnEnter(_ => Gather(m));
            // gone, and behind it: a palm into its back
            m.Add(Pop(ChoreoMove.BehindTarget));
            m.Strike(new Hit(Blow.Palm, level <= 1 ? 1.4f : level == 2 ? 0.9f : 0.8f)
            {
                Hurt = Hurt.Reel,
                Push = level <= 1 ? 1.1f : 0.7f,
                WindScale = 0.45f,
                Finisher = level == 1,
                Shows = Form.Palm | Form.Straight | Form.Teleport
            });
            if (level >= 2)
            {
                // round to its front as it stumbles in: a knee
                m.Add(Pop(ChoreoMove.BehindTarget));
                m.Strike(PhantomKit.Crack(new Hit(Blow.Knee, level == 2 ? 1.2f : 1f) { Hurt = Hurt.Fold, Daze = 0.3f, WindScale = 0.5f, Finisher = level == 2 }, level == 2 && master ? 0.35f : 0f));
            }
            if (level >= 3)
            {
                // to its side: the spinning backfist
                m.Add(Pop(SideOf));
                m.Strike(PhantomKit.Crack(new Hit(Blow.Backfist, 1.3f) { Hurt = Hurt.Launch, Push = 1.6f, Lift = 1.8f, WindScale = 0.6f, Finisher = true }, master ? 0.45f : 0f));
            }
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>Destination: beside the target (its left as seen from where it stands now), or its back if that is blocked.</summary>
        private static Vector2 SideOf(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 d = m.VG - m.Stance;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : m.Dir;
            Vector2 q = Combat.Clamp(m.VG + Choreo.Perp(dir) * m.Stand);
            return Combat.SafeSpot(q) ? q : ChoreoMove.BehindTarget(m);
        }

        /// <summary>
        /// The pop: the outline pinches to a sliver and is gone in a ring of violet light (nothing can
        /// touch it); a ring opens on the ground at 'to' and it unfolds there, facing the target.
        /// </summary>
        private static Beat Pop(Func<ChoreoMove, Vector2> to)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.09f,
                Route = Route.Blink,
                Dest = to,
                To = new Shape(0f, 0.35f, 1.35f, 0f),
                Ease = Ease.OutQuad,
                Intangible = true,
                Hidden = true,
                NeedsVictim = true,
                Forms = Form.Teleport
            };
            b.Enter = m =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (!m.Vis)
                {
                    return;
                }
                float px = m.Px;
                Vector2 body = m.BodyPos();
                // the sliver it pinches into, and the ring snapping shut on it
                Particle sliver = Fx.Afterimage(m, 0.1f, Fx.WithA(m.Hue.Core, 0.95f), PhantomKit.Illusion(m, 0.8f), PhantomKit.Illusion(m, 0f));
                if (sliver != null)
                {
                    sliver.SX *= 0.35f;
                    sliver.SY *= 1.3f;
                    sliver.R.sortingOrder += 3;
                }
                Fx.Ring(body, px, 0.12f, 9f * m.Kk, 1f, 1, 1, FxLayer.Over, 9, 1f, 1.6f, PhantomKit.Illusion(m, 0.9f), Fx.WithA(m.Hue.Core, 0.6f));
                Fx.Glint(body, px, 0.12f, m.Hue.Core, 0.9f);
                Fx.Ring(m.Stance, px, 0.2f, 7f * m.Kk, 2f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            };
            b.Exit = m =>
            {
                if (m.V != null && (m.VG - m.Stance).sqrMagnitude > 1e-4f)
                {
                    m.Face(m.VG - m.Stance);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
                if (!m.Vis)
                {
                    return;
                }
                float px = m.Px;
                Vector2 body = m.BodyPos();
                Fx.Ring(m.Stance, px, 0.24f, 2f, 10f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.4f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                Fx.Ring(body, px, 0.14f, 2f, 8f * m.Kk, 1, 1, FxLayer.Over, 9, 1f, 2.2f, Fx.WithA(Color.white, 0.85f), PhantomKit.Illusion(m, 0f));
                PhantomKit.Motes(body, px, 6, 5f * m.Kk, 0.25f, m.Hue.Core, PhantomKit.Illusion(m, 1f));
            };
            return b;
        }

        /// <summary>Violet motes rushing into it as it draws itself up for the blink.</summary>
        private static void Gather(PhantomMove m)
        {
            if (!m.Vis)
            {
                return;
            }
            ChoreoFx.Motes(m.BodyPos(), m.Px, 8, 10f * m.Kk, 0.12f, m.Hue.Core, PhantomKit.Illusion(m, 1f));
        }
    }
}
