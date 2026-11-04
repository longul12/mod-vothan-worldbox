using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SCISSOR TAKEDOWN (Tempest Leg, Adept). The answer to a foe curled up behind its guard (it answers
    /// Guard; a grab: guards don't stop it, a fast blow or a grab break does). It steps in, crouches and
    /// jumps at the foe's chest, tipping over backwards in the air so its legs go first; the legs close
    /// round the foe like the blades of a pair of scissors (two crescents crossing, focus lines), the pair
    /// locks a beat with the foe struggling, then it twists over its own length and the foe is dragged
    /// off its feet and over, slammed down on its back in a burst of dust (Down). It rolls out of the fall
    /// and springs up. II: the slam cracks the ground (a small crater, a ring of rock). III: the twist is a
    /// corkscrew (the foe spun right over in the air before the slam), an impact frame, and the shock
    /// knocks down everyone within 1.4 tiles. It needs a body it can take (no giants, no flyers).
    /// Damage (x the user's damage): the clamp 0.2, then the slam I 1.5; II 2.1; III 2.9 (+ x0.4 around).
    /// </summary>
    internal sealed class ScissorTakedownTech : Technique
    {
        public override string Id => "scissor_takedown";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Grab | Form.Aerial | Form.Kick;
        public override Form Answers => Form.Guard;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 15f, 27f, 44f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public const float Clamp = 0.2f;

        public static float Slam(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2.1f : 2.9f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Clamp + Slam(1), Hold = true, Crowd = 0.1f, Alone = 10f, IntoGuard = 0.9f, Worth = 12f },
            new MeleeWant { Reach = 2.6f, Total = Clamp + Slam(2), Hold = true, Crowd = 0.08f, Alone = 10f, IntoGuard = 0.9f, Worth = 14f },
            new MeleeWant { Reach = 2.8f, Total = Clamp + Slam(3), Hold = true, Crowd = 0.04f, Alone = 12f, IntoGuard = 0.9f, Worth = 18f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // the jump and the fall need solid ground under the foe
            Vector2 at = p.Target.current_position;
            if (!Combat.SafeSpot(at) && !Choreo.Wading(at))
            {
                return null;
            }
            if (level >= 3)
            {
                p.Score += Mathf.Max(0, c.CountNear(at, 1.4f) - 1) * 6f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            return ScissorMove.Begin(a, p, this);
        }
    }

    /// <summary>The scissor takedown's choreography: the jump, the scissors, the twist and the slam (the foe held between its legs).</summary>
    internal sealed class ScissorMove : LegMove
    {
        private const Form Shows = Form.Grab | Form.Aerial | Form.Kick;

        private ScissorMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static bool Begin(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || p.Target == null || !Moves.CanPerform(a))
            {
                return false;
            }
            ScissorMove m = new ScissorMove(a, t, p);
            m.Build();
            return m.Start();
        }

        private void Build()
        {
            int level = Level;
            StepIn();
            Crouch(0.07f, 0.85f).Shows(Shows);
            // the jump: at the foe's chest, tipping over backwards so the legs go first
            Beat jump = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.15f,
                PerTile = 0.05f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.StandSpot(0.42f * mm.Kk),
                Apex = 0.22f,
                To = new Shape(-90f, 1.04f, 0.96f, 0.2f),
                Ease = Ease.OutQuad,
                Ghosts = 0.03f,
                NeedsVictim = true,
                Forms = Shows
            };
            jump.Enter = mm =>
            {
                // its middle comes level with the foe's chest
                float mid = mm.Pose.PivotUp * mm.A.actor_scale;
                mm.Current.To = new Shape(-90f, 1.04f, 0.96f, Mathf.Clamp((0.55f * mm.VHeight - mid) / mm.Kk, 0.05f, 0.9f));
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 6, 30f, 0.4f, mm.Kk);
                }
            };
            Add(jump);
            // the legs close round it
            Beat clamp = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(-96f, 1.1f, 0.92f, float.NaN, 0.06f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Shows
            };
            clamp.Enter = mm => ((ScissorMove)mm).Close();
            Add(clamp);
            // locked a beat, the foe struggling between its legs
            Add(new Beat { Tempo = Tempo.Follow, Dur = 0.07f, Tremble = true, Track = false, Forms = Shows, If = mm => ((ScissorMove)mm).HasBody });
            // the twist: it rolls over its own length, dragging the foe off its feet and over
            Beat twist = new Beat
            {
                Tempo = Tempo.Act,
                Dur = level >= 3 ? 0.3f : 0.2f,
                To = new Shape(level >= 3 ? 268f : 88f, 1.02f, 0.98f, 0.04f, 0f),
                Ease = Ease.InOutCubic,
                Track = false,
                Ghosts = 0.03f,
                Forms = Shows | Form.Throw,
                If = mm => ((ScissorMove)mm).HasBody
            };
            twist.Enter = mm => ((ScissorMove)mm).Twist();
            twist.Exit = mm => ((ScissorMove)mm).Down();
            Add(twist);
            // lying across where it threw it, squashed into the fall
            Add(new Beat { Tempo = Tempo.Impact, Dur = 0.1f, To = new Shape(float.NaN, 1.16f, 0.86f, 0f, 0f), Ease = Ease.OutExpo, Track = false });
            // and it springs up out of the roll
            // (on round the way it rolled: III's roll went right over, so upright is a full turn on)
            PoseTo(0.1f, new Shape(level >= 3 ? 360f : 0f, 0.9f, 1.12f, 0.12f, 0f), Ease.OutQuad, Tempo.Follow);
            Recover(0.2f);
        }

        /// <summary>The legs close round the foe (the target answers here: a parry or a grab break throws it off).</summary>
        private void Close()
        {
            if (!VOk)
            {
                Lose();
                return;
            }
            float reach = Stand + 0.6f * Kk + V.stats["size"] * 0.5f + 0.4f;
            if (Vector2.Distance(Pose.Ground, VG) > reach)
            {
                Lose();
                return;
            }
            if (!Touch(V))
            {
                return;   // answered: the engine plays the recoil or the whiff
            }
            Actor v = V;
            if (!TakeBody(v))
            {
                // it can't be taken off its feet after all: the legs only kick it away
                Deliver(v, new Hit(Blow.SideKick, 0.5f) { Lethal = false, Shows = Shows }, ShapeOf(Blow.SideKick), VPoint(0.5f), Dir);
                Lose();
                return;
            }
            Struggle = 5f;
            Deliver(v, new Hit(Blow.Knee, ScissorTakedownTech.Clamp) { Hurt = Hurt.None, Lethal = false, Power = 0.8f, Shows = Shows }, ShapeOf(Blow.Knee), VPoint(0.55f), Dir);
            // rocked back by the legs as they close
            MoveBody(VG, 0f, -10f, Ease.OutQuad);
            Choreo.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            if (Vis)
            {
                // the two legs: crescents crossing like the blades of scissors, round the foe's middle
                Vector2 off = (VPoint(0.55f) - BodyPos()) / Px;
                Vector2 rel = new Vector2(off.x * Fwd, off.y);
                TempestKicks.Arc(this, 7, 0.55f, 160f, 20f, 2, rel + new Vector2(0f, 1.5f), 0.14f, 7);
                TempestKicks.Arc(this, 7, 0.55f, 200f, 340f, 2, rel - new Vector2(0f, 1.5f), 0.14f, 7);
                Fx.FocusLines(VPoint(0.55f), Px, Fx.Count(6), 8f * Kk, 14f * Kk, 0.1f);
            }
        }

        /// <summary>The twist begins: the foe is dragged off its feet and over (III: spun right round first).</summary>
        private void Twist()
        {
            if (!HasBody)
            {
                return;
            }
            Struggle = 0f;
            Vector2 g0 = BodyGround;
            Vector2 land = Combat.Clamp(g0 + Dir * 0.35f * Kk);
            if (!Combat.SafeSpot(land) && !Choreo.Wading(land))
            {
                land = g0;
            }
            bool corkscrew = Level >= 3;
            MoveBody(land, 0f, corkscrew ? -455f : -95f, Ease.InOutCubic, (corkscrew ? 0.55f : 0.22f) * Kk);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            // the roll winds the air round the pair
            TempestKicks.Arc(this, corkscrew ? 11 : 9, 0.6f, 90f, 450f, 2, Vector2.zero, BeatDur * 1.4f, 5);
        }

        /// <summary>The foe hits the ground on its back: the slam.</summary>
        private void Down()
        {
            if (!HasBody)
            {
                return;
            }
            Actor v = Body.A;
            Vector2 at = BodyGround;
            WorldTile tile = Combat.TileAt(at);
            int level = Level;
            float mult = ScissorTakedownTech.Slam(level);
            HitResult r = Combat.Hit(A, v, mult * Cm, false, false, 0f, this);
            LastHit = r;
            Impact im = Impact.Of(Hurt.Knockdown, Dir, 0.15f * Kk, 1.4f);
            im.Lift = 0.12f;
            im.Daze = level <= 1 ? 1f : level == 2 ? 1.3f : 1.6f;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            im.Stop = 0.05f * ImpactK;
            ReleaseBody(im);
            Victims.Flash(v, Color.white, 0.07f);
            HoldFor(0.05f * ImpactK);
            float weight = 1.3f + 0.2f * level;
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            if (Vis)
            {
                Vector2 hit = at + new Vector2(0f, 1.5f * Px);
                ChoreoFx.Impact(hit, Vector2.down, Px, Kk, weight, Hue, TopLayer, level >= 2);
                Fx.LandingPuff(at, Px, tile);
                Kit.DustRing(at, Px, tile, 8 + 3 * level, 36f + 5f * level, 0.5f, Kk);
                if (level >= 2)
                {
                    Kit.Debris(at, Px, tile, 3 + 3 * level, Kk);
                    Kit.Crater(at, Px * Kk, tile, Ruin.Force(Tier, Scale) >= 3, 5f, true, Hue.Edge);
                    Kit.Shockwave(at, Px, 3f, (0.9f + 0.4f * level) / Px, 0.3f, Fx.WithA(Hue.Edge, 0.85f), 2);
                }
                if (level >= 3 || r.Crit && level >= 2)
                {
                    Kit.ImpactFrame(hit, Px, (0.22f + 0.06f * level) * Kk, Hue.Pal, r.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
            }
            if (level >= 2)
            {
                Choreo.Sound("event:/SFX/HIT/HitStone", tile);
                Ruin.Blast(A, at, level >= 3 ? 0.9f : 0.6f, Scale, Pose.Ground);
            }
            if (level >= 3)
            {
                AreaBlow(at, 1.4f * Mathf.Lerp(1f, Kk, 0.5f), mult * 0.4f, v, Hurt.Knockdown, 0.6f, 1.1f);
                if (Tier >= Tier.Master)
                {
                    Kit.Stop(at, (0.03f + 0.02f * weight) * ImpactK);
                }
            }
            Ruin.Quake(A, at, Scale, weight, Kk);
            SetLink(Link.Down, v);
            if (Dev.On) Dev.Trace("tempest scissor slams " + v.asset.id + "#" + v.Sid() + " x" + (mult * Cm).ToString("F2") + " hp " + v.getHealth());
        }
    }
}
