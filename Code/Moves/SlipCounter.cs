using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SLIP COUNTER. "If someone throws an uppercut, you go to the side and hit them on the liver."
    /// The big swing (an uppercut, a hook, a blow from above) comes in; the fighter dips and slips to
    /// its outside, the swing tears through an afterimage where it stood, and a short hook digs into
    /// the attacker's side before it can recover: it folds.
    ///
    /// Beat sheet (game seconds at x1, from the attacker's contact frame):
    ///
    ///   SLIP     0.09  a dip (squash 112% x 84%, leaning away) and a slide 0.5 tiles to the side the
    ///                  swing leaves open; an afterimage stays where it stood and takes the blow (a
    ///                  white flicker), a puff of dust at its heels
    ///   COIL     0.05  the weight sinks onto the lead leg, the fist low, a glint
    ///   HOOK     0.07  the body turns into it: a low curved smear in the tier's colours, the contact
    ///                  (Combat.Reach: the attacker may answer the answer), a hit star at the ribs, the
    ///                  attacker flashes and folds (dazed 0.6 / 0.9 / 1.3 s), its broken swing cut short
    ///   (II)     0.08  the uppercut follow-through: a rising smear, the attacker launched (Launched)
    ///   (III)          the liver shot on an impact frame with a hit-stop, then the launch
    ///   RECOVER  0.16  springs back upright. Chain: Down (I) or Launched (II, III): the
    ///                  counter-attack follows at once
    ///
    /// Reactive (Answers Rising | Hook | Overhead): never planned; forced (dev, god power) it slips
    /// beside the nearest enemy and hooks it.
    /// </summary>
    internal sealed class SlipCounter : CounterMove
    {
        private const int Slip = 0, Coil = 1, Hook = 2, Upper = 3, Back = 4;

        private Vector2 _from, _to;
        private float _side;
        private bool _hit, _up, _flicked;
        private Particle _decoy;

        private SlipCounter(Actor a, Actor v, Move incoming, int level, Technique tech) : base(a, v, incoming, level, tech)
        {
        }

        public override Form Forms => Form.Evasive | Form.Counter | Form.Punch | Form.Hook | Form.Body;

        public override Actor Aim => Foe;

        public static bool Begin(Actor d, Actor atk, Move incoming, int level, Technique tech)
        {
            if (d == null || atk == null || d == atk || !Combat.Living(atk) || !Moves.CanPerform(d))
            {
                return false;
            }
            SlipCounter m = new SlipCounter(d, atk, incoming, level, tech);
            if (!m.PlanSlip())
            {
                return false;
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            m.Decoy();
            Dev.Watch(Vector2.Lerp(m._from, atk.current_position, 0.5f) + new Vector2(0f, 1f));
            return true;
        }

        /// <summary>To the outside of the swing: the side with safe ground, at a hook's distance from the attacker.</summary>
        private bool PlanSlip()
        {
            _from = Pose.Ground;
            float first = Random.value < 0.5f ? 1f : -1f;
            float dist = Mathf.Max(Reach, 0.55f * Kk);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? first : -first;
                Vector2 want = Beside(side, dist);
                Vector2 to = StepTo(_from, want);
                if (Vector2.Distance(to, _from) > 0.15f || i == 1)
                {
                    _to = to;
                    _side = side;
                    break;
                }
            }
            if (Vector2.Distance(_to, Foe.current_position) > dist + 0.9f)
            {
                _to = _from;   // nowhere to slip to: it sways in place
            }
            Home = _to;
            Next(Slip);
            return true;
        }

        /// <summary>The afterimage that takes the blow where it stood.</summary>
        private void Decoy()
        {
            if (!Visible)
            {
                return;
            }
            _decoy = Fx.Afterimage(this, 0.26f, new Color(1f, 1f, 1f, 0.75f), Fx.WithA(Pal.Edge, 0.35f), Fx.WithA(Pal.Edge, 0f));
            Fx.LandingPuff(_from, Px, A.current_tile);
        }

        public override void Tick(float dt)
        {
            Pt += dt;
            if (!FoeOk() && Ph < Back)
            {
                Next(Back);
            }
            else if (Ph < Back)
            {
                Face(Foe.current_position);
            }
            float px = Px;
            switch (Ph)
            {
                case Slip:
                    {
                        float t = 0.09f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        float e = Anim.OutCubic(k);
                        Pose.Ground = Vector2.Lerp(_from, _to, e);
                        float dip = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.2f));
                        Pose.Rot = -10f * dip;
                        Pose.SX = 1f + 0.12f * dip;
                        Pose.SY = 1f - 0.16f * dip;
                        if (!_flicked && k > 0.5f && _decoy != null && _decoy.Alive && Visible)
                        {
                            // the swing tears through the afterimage
                            _flicked = true;
                            Vector2 at = _decoy.Pos + new Vector2(0f, 4f * px * Kk);
                            Fx.Glint(at, px, 0.1f, Color.white, 0.8f);
                            Kit.Sparks(at, px, 3, 30f, 70f, new Color(0.9f, 0.95f, 1f, 1f));
                        }
                        if (k >= 1f)
                        {
                            Next(Coil);
                        }
                        break;
                    }
                case Coil:
                    {
                        float k = Mathf.Clamp01(Pt / (0.05f * Tempo));
                        Pose.Ground = Home;
                        Pose.Rot = Mathf.Lerp(-10f, -14f, k);
                        Pose.SX = Mathf.Lerp(1.12f, 0.92f, k);
                        Pose.SY = Mathf.Lerp(0.84f, 0.9f, k);
                        if (Enter() && Visible)
                        {
                            Fx.Glint(BodyPos() + new Vector2(Fwd * 2f, -1f) * px, px, 0.12f, Pal.Core, 0.7f);
                        }
                        if (k >= 1f)
                        {
                            Next(Hook);
                        }
                        break;
                    }
                case Hook:
                    {
                        float t = 0.07f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        float e = Anim.OutCubic(k);
                        if (Pt <= dt)
                        {
                            // the swing carried it past: one step after it into the hook
                            _stepFrom = Home;
                            Home = Close(Home, 1f * Kk);
                        }
                        Pose.Ground = Vector2.Lerp(_stepFrom, Home, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
                        Pose.Rot = Mathf.Lerp(-14f, 20f, e);
                        Pose.SX = Mathf.Lerp(0.92f, 1.16f, e);
                        Pose.SY = Mathf.Lerp(0.9f, 0.94f, e);
                        if (Enter())
                        {
                            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                            if (Visible)
                            {
                                // a low curved smear: the hook to the body
                                Kit.Swish(this, Art.Arc(Mathf.RoundToInt(7f * Kk), 0.55f, -70f, 40f, 2), 0.1f, new Vector2(Fwd * 2f, -1.5f), Fwd, Pal.Core, Pal.Edge);
                            }
                        }
                        if (!_hit && k >= 0.55f)
                        {
                            _hit = true;
                            Liver();
                        }
                        if (k >= 1f)
                        {
                            Next(Level >= 2 && Landed && FoeOk() ? Upper : Back);
                        }
                        break;
                    }
                case Upper:
                    {
                        float t = 0.08f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        float e = Anim.OutCubic(k);
                        Pose.Ground = Home;
                        Pose.Rot = Mathf.Lerp(20f, -8f, e);
                        Pose.SX = Mathf.Lerp(1.16f, 0.9f, e);
                        Pose.SY = Mathf.Lerp(0.94f, 1.18f, e);
                        Pose.Height = 0.15f * Kk * Mathf.Sin(Mathf.PI * k);
                        if (Enter() && Visible)
                        {
                            Kit.Swish(this, Art.Arc(Mathf.RoundToInt(8f * Kk), 0.75f, -80f, 80f, 2), 0.1f, new Vector2(Fwd * 2f, 1f), Fwd, Pal.Core, Pal.Edge);
                        }
                        if (!_up && k >= 0.5f)
                        {
                            _up = true;
                            Vector2 up = (Dir() + Vector2.up * 1.6f).normalized;
                            if (Strike(Level >= 3 ? 1.2f : 0.8f, up, 0.04f, 1, 0.9f))
                            {
                                Throw(Pose.Ground, 1.4f, Level >= 3 ? 1.9f : 1.4f, 0.5f, 720f);
                            }
                        }
                        if (k >= 1f)
                        {
                            Pose.Height = 0f;
                            Next(Back);
                        }
                        break;
                    }
                default:
                    {
                        float rot0 = Pose.Rot, sx0 = Pose.SX, sy0 = Pose.SY;
                        if (Enter())
                        {
                            _r0 = rot0;
                            _sx0 = sx0;
                            _sy0 = sy0;
                        }
                        if (Recover(0.16f * Snap, _r0, _sx0, _sy0))
                        {
                            Done(Launched ? SingleKit.AirLink(A, 0.8f) : Link.Down);
                        }
                        break;
                    }
            }
        }

        private float _r0, _sx0 = 1f, _sy0 = 1f;
        private Vector2 _stepFrom;

        private Vector2 Dir()
        {
            if (!FoeOk())
            {
                return new Vector2(Fwd, 0f);
            }
            Vector2 d = Foe.current_position - Pose.Ground;
            return d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
        }

        /// <summary>The hook to the liver: it folds.</summary>
        private void Liver()
        {
            float mult = Level <= 1 ? 1.3f : Level == 2 ? 1.6f : 2.2f;
            if (!Strike(mult, Dir(), Level >= 3 ? 0.08f : 0.04f, Level >= 3 ? 2 : 1, 0.9f))
            {
                if (Dev.On) Dev.Trace("slip " + Id + ": the hook " + (FoeOk() ? "cuts the air (" + Vector2.Distance(Pose.Ground, Foe.current_position).ToString("F2") + " tiles)" : "has no one to land on") + " or was answered");
                return;
            }
            if (Level >= 3 && Visible)
            {
                Kit.ImpactFrame(FoeBody(), Px, 0.34f * Kk, Pal, false);
            }
            // (a foe above its class only rocks: its move plays on, no long fold)
            bool allowed = FoeOk() && Combat.Allowed(A, Foe);
            if (allowed)
            {
                BreakFoeMove();
            }
            if (FoeOk() && Foe.hasHealth())
            {
                // it folds where it stands (I) or a beat before the uppercut takes it (II, III)
                Combat.Stagger(Foe, !allowed ? 0.3f : Level <= 1 ? 0.6f : Level == 2 ? 0.9f : 1.3f);
            }
        }

        public override void Abort()
        {
            Pose.Height = 0f;
        }
    }

    /// <summary>
    /// Slip Counter as a technique. Entry: Adept (universal). Reactive: it answers the big swings
    /// (Rising | Hook | Overhead: uppercuts, hooks, blows from above, the flow strikes' hooks and
    /// rising cuts too); the attack whiffs (Evaded) and the counter lands.
    /// </summary>
    internal sealed class SlipCounterTech : Technique
    {
        public override string Id => "slip_counter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;

        public override Form Forms => Form.Evasive | Form.Counter | Form.Punch | Form.Hook | Form.Body;

        public override Form Answers => Form.Rising | Form.Hook | Form.Overhead;

        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 6f, 5f, 4f };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return SlipCounter.Begin(defender, attacker, incoming, level, this) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A plain blow of the same kind is met the same way (Reactions reads it, at a share of the chance).</summary>
        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            return SlipCounter.Begin(defender, attacker, null, level, this);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 3f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && SlipCounter.Begin(a, p.Target, null, p.Level, this);
        }
    }
}
