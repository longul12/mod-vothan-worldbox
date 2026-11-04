using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KNEE SPLITTER (Hunter's Way, Expert). How a hunter fells something too big to fight face to
    /// face: it slides in low under the creature's reach (a blur along the ground, nothing can touch
    /// it), chambers its knee and stamps a side kick straight into the joint of the leg: a crack of
    /// white lines at the knee, dust bursting at the giant's feet, and the creature buckles (a giant
    /// is rocked and stunned a long while; a brute it can move drops to its knees and pitches
    /// forward, dazed). II: a low sweeping kick at the other knee first, then the stamp on an impact
    /// frame. III: the hunter hops and brings its heel down on the bent joint, the ground cracking
    /// under the creature's feet: down the longest. Made for titans, brutes and kaiju (the Brain uses
    /// it on nothing else); everything happens at knee height. Damage (x the user's damage): I 1.6;
    /// II 0.8 + 1.6; III 0.8 + 1.6 + 1.4. Stagger on a giant: I 1.6 s, II 2.2 s, III 3 s.
    /// </summary>
    internal sealed class KneeSplitterTech : Technique
    {
        public override string Id => "knee_splitter";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Kick | Form.Low | Form.Hunt;
        public override Prey Prey => Prey.Titan | Prey.Brute | Prey.Kaiju;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 28f, 45f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float Sweep = 0.8f, Stamp = 1.6f, Crush = 1.4f;

        public static float Total(int level)
        {
            return Stamp + (level >= 2 ? Sweep : 0f) + (level >= 3 ? Crush : 0f);
        }

        private static float Down(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.2f : 3f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 3.2f, Total = Total(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 1f, Worth = 18f },
            new MeleeWant { Reach = 3.6f, Total = Total(2), Crowd = 0.05f, Alone = 8f, IntoGuard = 1f, Worth = 22f },
            new MeleeWant { Reach = 4f, Total = Total(3), Crowd = 0.04f, Alone = 10f, IntoGuard = 1f, Worth = 28f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = HwLoKit.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // the bigger it is, the more a long stagger is worth
            if (Weights.Heavy(v) || Weights.Species(v) >= Weight.Fiend || v.actor_scale > c.Self.actor_scale * 1.4f)
            {
                p.Score += 20f + 6f * level;
            }
            // something about to swing at it: under the swing, at the knee
            if (c.State(v).AimsAtMe)
            {
                p.Score += 8f;
            }
            return p;
        }

        // a low sweeping kick at the near knee
        private static readonly BlowShape LowKick = new BlowShape
        {
            Limb = Limb.Foot,
            Wind = new Shape(-8f, 1.06f, 0.92f, 0f, -0.03f),
            WindDur = 0.07f,
            Hit = new Shape(26f, 1.3f, 0.8f, 0f, 0.14f),
            ActDur = 0.07f,
            FollowDur = 0.06f,
            At = 0.55f,
            Relax = 0.35f,
            Contact = 0.18f,
            ArcR = 8,
            ArcRatio = 0.4f,
            A0 = 200f,
            A1 = 345f,
            ArcThick = 2,
            ArcOff = new Vector2(1f, -2f),
            Weight = 0.95f,
            Hurt = Hurt.SnapBack,
            Push = 0.12f,
            Forms = Form.Kick | Form.Low | Form.Hunt
        };

        // the stamp: a chambered side kick straight into the joint
        private static readonly BlowShape KneeStamp = new BlowShape
        {
            Limb = Limb.Foot,
            Wind = new Shape(-16f, 0.9f, 1.1f, 0.06f, -0.08f),
            WindDur = 0.11f,
            Hit = new Shape(-26f, 1.36f, 0.8f, 0f, 0.24f),
            ActDur = 0.07f,
            FollowDur = 0.1f,
            At = 0.5f,
            Relax = 0.3f,
            Contact = 0.2f,
            Straight = true,
            SmearHead = 2,
            SmearThick = 2,
            Weight = 1.35f,
            Heavy = true,
            Hurt = Hurt.Crumple,
            Forms = Form.Kick | Form.Low | Form.Straight | Form.Hunt
        };

        // III: the heel brought down on the bent joint from a hop
        private static readonly BlowShape HeelCrush = new BlowShape
        {
            Limb = Limb.Foot,
            Wind = new Shape(-18f, 0.9f, 1.18f, 0.45f, 0.06f),
            WindDur = 0.13f,
            Hit = new Shape(32f, 1.18f, 0.82f, 0f, 0.2f),
            ActDur = 0.07f,
            FollowDur = 0.12f,
            At = 0.6f,
            Relax = 0.25f,
            Contact = 0.24f,
            ArcR = 9,
            ArcRatio = 1f,
            A0 = 100f,
            A1 = -70f,
            ArcThick = 3,
            ArcOff = new Vector2(2f, 2f),
            Weight = 1.5f,
            Heavy = true,
            Hurt = Hurt.Crumple,
            Forms = Form.Kick | Form.Low | Form.Overhead | Form.Heavy | Form.Hunt
        };

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            HwLoKit.FitStand(m);
            int level = m.Level;
            // in low under its reach: a blur along the ground
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = 0.03f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = ChoreoMove.ToTarget,
                To = new Shape(26f, 1.24f, 0.76f, 0f),
                Ease = Ease.OutQuad,
                Intangible = true,
                Ghosts = 0.025f,
                Lines = 0.03f,
                Dust = 0.04f,
                NeedsVictim = true,
                Forms = Form.Dash | Form.Low | Form.Hunt,
                If = mm => mm.V != null && Vector2.Distance(mm.Stance, mm.StandSpot(mm.Stand)) > 0.3f * mm.Kk,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Fx.LandingPuff(mm.Stance, mm.Px, mm.A.current_tile);
                    }
                }
            });
            if (level >= 2)
            {
                HwLoKit.Strike(m, LowKick, new Hit(Blow.Sweep, Sweep) { Hurt = Hurt.SnapBack, Push = 0.12f, OnLand = (mm, v, r) => Buckle(mm, v, 0.5f, 0.8f) });
            }
            HwLoKit.Strike(m, KneeStamp, new Hit(Blow.SideKick, Stamp)
            {
                Hurt = Hurt.Crumple,
                Daze = level >= 3 ? 0.6f : Down(level) * 0.75f,
                Power = 1.3f,
                Finisher = level >= 2,
                WindScale = 1.15f,
                OnLand = (mm, v, r) => Buckle(mm, v, 1f, level >= 3 ? 1.2f : Down(level))
            });
            if (level >= 3)
            {
                HwLoKit.Strike(m, HeelCrush, new Hit(Blow.HeelDrop, Crush)
                {
                    Hurt = Hurt.Crumple,
                    Daze = Down(level) * 0.75f,
                    Power = 1.45f,
                    Finisher = true,
                    Crater = 0.5f,
                    OnLand = (mm, v, r) =>
                    {
                        Buckle(mm, v, 1.4f, Down(level));
                        Vector2 g = Victims.GroundOf(v);
                        Ruin.Furrow(mm.A, g - mm.Side * 0.7f * mm.Kk, g + mm.Side * 0.7f * mm.Kk, 0.4f, mm.Scale, mm.Pose.Ground, false);
                    }
                });
            }
            m.Recover(0.26f);
            return m.Start();
        }

        /// <summary>
        /// The leg gives: a crack of white lines at the knee, dust bursting at its feet; a giant (or
        /// anything it can't move) is rocked and stunned 'secs' seconds, a body it can move has already
        /// dropped (Crumple) and only gets the cracks and the dust.
        /// </summary>
        private static void Buckle(ChoreoMove m, Actor v, float power, float secs)
        {
            if (v == null)
            {
                return;
            }
            bool giant = Weights.Heavy(v) || !Victims.Movable(v, m.A);
            if (giant && Combat.Living(v) && !Moves.Locked(v))
            {
                Combat.Stagger(v, secs);
                v.startShake(0.3f + 0.1f * power, 0.08f, true, false);
            }
            Vector2 knee = m.ContactPoint(0.2f);
            Vector2 feet = Victims.GroundOf(v);
            float vk = Mathf.Max(1f, Choreo.KOf(v));
            if (m.Vis)
            {
                // the crack at the joint: short white lines bursting out of it
                int n = Fx.Count(power >= 1f ? 6 : 4);
                for (int i = 0; i < n; i++)
                {
                    float ang = (i / (float)n) * 360f + Random.Range(-15f, 15f);
                    Vector2 d = Choreo.Rotate(Vector2.right, ang);
                    float len = Random.Range(3f, 5.5f) * power * Mathf.Sqrt(m.Kk) * m.Px;
                    Kit.Line(knee + d * 1f * m.Px, knee + d * len, m.Px, 0.12f, 1, m.TopLayer, 15, Color.white, Fx.WithA(m.Hue.Core, 0.7f), Fx.WithA(m.Hue.Edge, 0f));
                }
                Kit.DustRing(feet, Choreo.PxOf(v), Combat.TileAt(feet), giant ? 12 : 8, giant ? 40f : 30f, 0.5f, vk);
                if (giant)
                {
                    Kit.Debris(feet, m.Px, Combat.TileAt(feet), 5, vk);
                }
            }
            if (giant)
            {
                Kit.Shake(feet, 0.2f, 0.004f * power * Mathf.Min(3f, vk));
                Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", Combat.TileAt(feet), 0.1f);
            }
            if (Dev.On) Dev.Trace("knee_splitter " + m.Id + ": " + v.asset.id + "#" + v.Sid() + " buckles (" + (giant ? "giant, " + secs.ToString("F1") + " s" : "crumples") + ")");
        }
    }
}
