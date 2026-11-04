using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SHOULDER THROW (Earth Breaker, Adept; reactive). The attacker comes charging in (a dash, a
    /// straight, a punch): it dips under the blow (the blow tears through its afterimage), takes the
    /// arm, and the attacker's own momentum carries it up onto its shoulder and right over its head,
    /// flung far behind it along the line it came in on. The body tumbles through the air and
    /// crashes (Thrown): dust, a dent in the ground. A dash's momentum throws it further. II flings
    /// it further and the crash knocks down those around the landing; III whips it over with an
    /// impact frame, the crash a crater with a ring of shock. Answers Dash, Straight and Punch
    /// (techniques, and now and then a plain punch or thrust); only a body it could lift (no giants,
    /// no flyers). Damage (x the user's damage) at the throw: I 1.6, II 2.2, III 3.0 (+ x0.5 on those
    /// near the crash). Distance 3.5 / 5 / 6.5 tiles (+1.5 riding a dash).
    /// </summary>
    internal sealed class ShoulderThrowTech : Technique
    {
        public override string Id => "shoulder_throw";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Throw | Form.Counter;
        public override Form Answers => Form.Dash | Form.Straight | Form.Punch;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 10f, 15f, 22f };
        public override float[] Cooldown => new[] { 7f, 6.5f, 6f };

        public static float Mult(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2.2f : 3f;
        }

        public static float Dist(int level)
        {
            return level <= 1 ? 3.5f : level == 2 ? 5f : 6.5f;
        }

        private const float GrabMult = 0.08f;

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            // only a body it can lift (and may fight: the class limits), arriving at arm's length
            if (!Throwable(attacker, defender) || !AtHand(defender, attacker, 2.5f) || !Combat.Allowed(defender, attacker))
            {
                return Contact.Hit;
            }
            bool dash = incoming != null && (Reads.FormsOf(incoming) & Form.Dash) != 0;
            Reactions.Free(defender);
            ChoreoMove m = ChoreoMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            Build(m, attacker, dash);
            return m.Start() ? Contact.Evaded : Contact.Hit;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (!Throwable(attacker, defender) || !AtHand(defender, attacker, 2f) || !Combat.Allowed(defender, attacker))
            {
                return false;
            }
            ChoreoMove m = ChoreoMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return false;
            }
            Build(m, attacker, false);
            return m.Start();
        }

        /// <summary>Never planned in a fight (it answers); forced (a dev command, a god power) it seizes the nearest foe and throws it.</summary>
        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || !c.Forced)
            {
                return null;
            }
            return Choreo.PickMelee(c, this, level, new MeleeWant { Reach = 2.4f, Total = Mult(level), Hold = true, Worthy = false });
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || p.Target == null)
            {
                return false;
            }
            m.StepIn();
            Build(m, p.Target, false);
            return m.Start();
        }

        /// <summary>Could it lift this body (whatever it is doing right now: its move is cut short when it is thrown)?</summary>
        private static bool Throwable(Actor v, Actor by)
        {
            return v != null && by != null && Combat.Living(v) && Victims.Movable(v, by) && !Weights.Heavy(v) && !v.isFlying()
                && Combat.HeightOf(v) <= HundredFists.HopMax && !(Victims.Get(v) is Victim b && b.Holder != null);
        }

        private static bool AtHand(Actor d, Actor atk, float tiles)
        {
            float k = Choreo.KOf(d);
            return Vector2.Distance(d.current_position, atk.current_position) <= tiles * k + atk.stats["size"] * 0.5f;
        }

        /// <summary>
        /// The answer: the dip under the blow, the catch, the body carried up over its head by its own
        /// momentum and flung far behind it along the line it came in on.
        /// </summary>
        private static void Build(ChoreoMove m, Actor atk, bool dash)
        {
            int level = m.Level;
            Vector2 line = (Vector2)m.A.current_position - (Vector2)atk.current_position;
            Vector2 way = line.sqrMagnitude > 1e-4f ? line.normalized : -m.Dir;
            float dist = Dist(level) + (dash ? 1.5f : 0f);
            // it dips in under the blow: a low pivot into the line, the blow tearing through an afterimage
            Beat dip = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.07f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = Pivot,
                To = new Shape(16f, 1.16f, 0.84f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Forms = Form.Grab | Form.Throw | Form.Counter | Form.Evasive
            };
            dip.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Fx.Afterimage(mm, 0.22f, Fx.WithA(mm.Hue.Ghost, 0.75f), mm.Hue.GhostEnd);
                    ChoreoFx.HeelDust(mm.Stance, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                }
            };
            m.Add(dip);
            // the attacker's own move ends here: it is being thrown
            m.Do(CutShort);
            m.Grab(GrabMult, new Grip(3f, 1.6f, -6f));
            // its momentum carries it up onto the shoulder: hoisted over the head, the body level
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = level >= 3 ? 0.1f : 0.12f,
                To = new Shape(-10f, 0.9f, 1.12f, 0f),
                Ease = Ease.OutCubic,
                Grip = new Grip(0.6f, 8.5f, -100f),
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Grab | Form.Throw | Form.Counter,
                If = mm => mm.Held != null
            });
            // and right over: it leans back into the whip, the body flung down behind it
            Beat whip = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(-30f, 0.92f, 1.08f, 0f, -0.06f),
                Ease = Ease.InQuad,
                Grip = new Grip(-3.5f, 6f, -190f),
                Track = false,
                Ghosts = 0.025f,
                Forms = Form.Grab | Form.Throw | Form.Counter,
                If = mm => mm.Held != null
            };
            whip.Exit = mm => Fling(mm, way, dist);
            m.Add(whip);
            // the follow-through: still leaning back, arms out
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = level >= 3 ? 0.16f : 0.12f, To = new Shape(-14f, 1.04f, 0.96f, 0f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.22f);
        }

        /// <summary>A short step in under the attacker (it came to it: a hair towards it, a little to the side).</summary>
        private static Vector2 Pivot(ChoreoMove m)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 q = Combat.Clamp(m.Stance + m.Dir * 0.15f * m.Kk + m.Side * 0.08f * m.Kk);
            return Combat.SafeSpot(q) && Combat.ClearPath(m.Stance, q) ? q : m.Stance;
        }

        /// <summary>The attacker's own move (not one of the engine's reactions) is cut short: it is about to be thrown.</summary>
        private static void CutShort(ChoreoMove m)
        {
            Actor v = m.V;
            if (v == null || v.Sid() != m.VId || !Moves.TryGet(v, out Move vm) || vm.A != v || vm is Puppet || vm.Ended)
            {
                return;
            }
            if (!vm.Interruptible)
            {
                Choreo.Kill(vm);
            }
        }

        /// <summary>The release: the body flies tumbling along 'way', 'dist' tiles, and crashes there.</summary>
        private static void Fling(ChoreoMove m, Vector2 way, float dist)
        {
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim h = m.Held;
            Actor v = h.A;
            Vector2 from = h.P.Pose.Ground;
            Vector2 want = Combat.Clamp(m.Stance + way * dist * Mathf.Lerp(1f, m.Kk, 0.5f));
            Vector2 dest = Combat.FindLanding(want, 3f, out Vector2 spot) ? spot : m.Stance + way * 1.2f;
            HitResult r = Combat.Hit(m.A, v, (Mult(level) - GrabMult) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            m.Held = null;
            Impact im = Impact.Of(Hurt.Thrown, dest - from, 0f, 1.2f + 0.15f * level);
            im.To = dest;
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            im.Crater = level <= 1 ? 0.5f : level == 2 ? 0.8f : 1.2f;
            im.AreaR = level <= 1 ? 0f : level == 2 ? 1.2f : 1.6f;
            im.AreaMult = 0.5f * Mult(level);
            im.Daze = level >= 3 ? 1.2f : 0.8f;
            im.Spin = level >= 3 ? -900f : -620f;
            im.Lift = Mathf.Clamp(0.9f + 0.16f * Vector2.Distance(from, dest), 1f, 2.6f);
            h.LetGo(im, m.A, m);
            Victims.Flash(v, Color.white, 0.06f);
            m.HoldFor((level >= 3 ? 0.06f : 0.035f) * m.ImpactK);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            ChoreoFx.BlowSound(Limb.Body, 1.2f, false, m.A.current_tile);
            if (m.Vis)
            {
                Vector2 at = Choreo.BodyAt(v, 0.5f);
                Kit.SpeedLines(at, dest - from, m.Px, 4, 4f, 0.16f, Fx.WithA(m.Hue.Edge, 0.85f));
                ChoreoFx.Impact(at, dest - from, m.Px, m.Kk, 1f + 0.25f * level, m.Hue, m.TopLayer, level >= 3);
                if (level >= 3)
                {
                    Kit.ImpactFrame(at, m.Px, 0.4f * m.Kk, m.Hue.Pal, r.Crit, FxLayer.Behind);
                }
            }
            m.SetLink(Link.Thrown, v);
        }
    }
}
