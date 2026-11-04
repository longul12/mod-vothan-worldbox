using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DUCK AND SWEEP. A blow at head height (a hook, a high kick, a jab, a cross) comes in: the
    /// fighter drops under it, and from down there spins one leg out along the ground and takes the
    /// attacker's legs: its feet fly up and it lands on its back.
    ///
    /// Beat sheet (game seconds at x1, from the attacker's contact frame):
    ///
    ///   DUCK     0.07  dropped low (squash 128% x 58%), the blow whistles over its head (a white
    ///                  streak just above it), a puff of dust
    ///   SWEEP    0.14  a full low spin: the body turns (narrowing, flipping, widening), a flat
    ///                  crescent skims the ground round its feet in the tier's colours, a ring of
    ///                  dust; at mid-spin the leg reaches the attacker's (Combat.Reach first): a hit
    ///                  star at the ankles, the attacker's feet fly up, it falls and is dazed
    ///                  (0.8 / 1.1 / 1.5 s); its swing is cut short
    ///   (III)          the sweep carries a shockwave: the ground under it cracks (a scorch),
    ///                  and anyone else within 1.2 tiles stumbles
    ///   RISE     0.16  back up through a spring. Chain: Down
    ///
    /// Reactive (Answers High): never planned; forced (dev, god power) it sweeps the nearest enemy.
    /// </summary>
    internal sealed class DuckSweep : CounterMove
    {
        private const int Duck = 0, Sweep = 1, Rise = 2;

        private bool _swept, _turned;
        private float _r0, _sx0 = 1f, _sy0 = 1f;
        private readonly bool _startRight;

        private DuckSweep(Actor a, Actor v, Move incoming, int level, Technique tech) : base(a, v, incoming, level, tech)
        {
            _startRight = Pose.FaceRight;
        }

        public override Form Forms => Form.Low | Form.Kick | Form.Counter | Form.Spin;

        public override Actor Aim => Foe;

        public static bool Begin(Actor d, Actor atk, Move incoming, int level, Technique tech)
        {
            if (d == null || atk == null || d == atk || !Combat.Living(atk) || !Moves.CanPerform(d))
            {
                return false;
            }
            DuckSweep m = new DuckSweep(d, atk, incoming, level, tech);
            if (!Moves.Start(m))
            {
                return false;
            }
            Dev.Watch(Vector2.Lerp(m.Home, atk.current_position, 0.5f) + new Vector2(0f, 1f));
            return true;
        }

        public override void Tick(float dt)
        {
            Pt += dt;
            if (!FoeOk() && Ph < Rise)
            {
                Next(Rise);
            }
            float px = Px;
            switch (Ph)
            {
                case Duck:
                    {
                        float k = Mathf.Clamp01(Pt / (0.07f * Tempo));
                        float e = Anim.OutCubic(k);
                        Pose.Ground = Home;
                        Pose.Rot = -4f * e;
                        Pose.SX = Mathf.Lerp(1f, 1.28f, e);
                        Pose.SY = Mathf.Lerp(1f, 0.58f, e);
                        if (Enter() && Visible)
                        {
                            // the blow whistles over its head
                            Vector2 head = HeadPos() + new Vector2(0f, 2.5f * px * Kk);
                            Kit.Line(head - new Vector2(Fwd * 6f * px * Kk, 0f), head + new Vector2(Fwd * 4f * px * Kk, 0f), px, 0.1f, 1, FxLayer.Top, 14,
                                Fx.WithA(Color.white, 0.85f), Fx.WithA(Color.white, 0f));
                            Fx.LandingPuff(Home, px, A.current_tile);
                        }
                        if (k >= 1f)
                        {
                            Next(Sweep);
                        }
                        break;
                    }
                case Sweep:
                    {
                        float t = 0.14f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        Pose.Ground = Home;
                        // a full turn low: narrow edge-on, flip, widen
                        float turn = Mathf.Abs(Mathf.Cos(Mathf.PI * k));
                        Pose.SX = Mathf.Lerp(0.55f, 1.28f, turn);
                        Pose.SY = 0.6f;
                        Pose.Rot = 0f;
                        if (!_turned && k >= 0.5f)
                        {
                            _turned = true;
                            Pose.FaceRight = !_startRight;
                        }
                        if (Enter())
                        {
                            Home = Close(Home, 0.8f * Kk);   // (low, it shuffles in to reach the legs)
                            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                            if (Visible)
                            {
                                // a flat crescent skims the ground round its feet
                                Particle arc = Fx.Spawn(Art.Arc(Mathf.RoundToInt(9f * Kk), Art.Ground, 200f, -20f, 2), Home, px, 0.16f, FxLayer.Ground, 6);
                                if (arc != null)
                                {
                                    arc.SX = Fwd;
                                    arc.Color(Pal.Core, Pal.Edge, Fx.WithA(Pal.Edge, 0f));
                                }
                                Kit.DustRing(Home, px, A.current_tile, Fx.Count(8), 22f * Kk, 0.4f, Kk);
                            }
                        }
                        if (!_swept && k >= 0.45f)
                        {
                            _swept = true;
                            Take();
                        }
                        if (k >= 1f)
                        {
                            Pose.FaceRight = _startRight;
                            Next(Rise);
                        }
                        break;
                    }
                default:
                    {
                        if (Enter())
                        {
                            _r0 = Pose.Rot;
                            _sx0 = Pose.SX;
                            _sy0 = Pose.SY;
                            if (FoeOk())
                            {
                                Face(Foe.current_position);
                            }
                        }
                        if (Recover(0.16f * Snap, _r0, _sx0, _sy0))
                        {
                            Done(Landed ? Link.Down : Link.None);
                        }
                        break;
                    }
            }
        }

        /// <summary>The leg takes the attacker's legs: its feet fly up and it lands on its back.</summary>
        private void Take()
        {
            Vector2 low = FoeOk() ? Foe.current_position + new Vector2(0f, 1.5f * Px) : Home;
            Vector2 d = FoeOk() ? (Foe.current_position - Home) : new Vector2(Fwd, 0f);
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
            float mult = Level <= 1 ? 1f : Level == 2 ? 1.4f : 1.9f;
            if (Strike(mult, (d + Vector2.up).normalized, Level >= 3 ? 0.06f : 0.03f, Level >= 3 ? 2 : 1, 1f))
            {
                if (Kit.Visible(low))
                {
                    Fx.HitSpark(low, Px, 1, Vector2.up, 3);
                }
                // its feet fly up: a short tumble onto its back, then it lies dazed
                Throw(Home, 0.5f, 0.7f, Level <= 1 ? 0.8f : Level == 2 ? 1.1f : 1.5f, 360f);
            }
            if (Level >= 3)
            {
                Shock();
            }
        }

        /// <summary>III: the sweep's shockwave scorches the ground and trips whoever else is close.</summary>
        private void Shock()
        {
            float px = Px;
            if (Visible)
            {
                Kit.Shockwave(Home, px, 3f, 14f * Kk, 0.3f, Fx.WithA(Pal.Edge, 0.85f));
            }
            int force = SingleKit.Force(Tr, 0, 1);
            if (force > 0)
            {
                SingleKit.Ruin(A, Home, Home + new Vector2(Fwd * 0.8f, 0f), SingleKit.Radius(Tr, 0.9f), force);
            }
            foreach (Actor u in Combat.FoesAround(A, Home, 1.2f * Kk))
            {
                if (u == Foe || !Combat.Allowed(A, u))
                {
                    continue;
                }
                Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                Combat.Stagger(u, 0.5f);
            }
        }
    }

    /// <summary>
    /// Duck and Sweep as a technique. Entry: Adept (universal). Reactive: it answers blows at head
    /// height (Answers High: hooks, high kicks, the flow strikes' jabs and crosses); the blow whiffs
    /// over its head (Evaded) and the sweep lands.
    /// </summary>
    internal sealed class DuckSweepTech : Technique
    {
        public override string Id => "duck_sweep";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;

        public override Form Forms => Form.Low | Form.Kick | Form.Counter | Form.Spin;

        public override Form Answers => Form.High;

        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 6f, 5f, 4f };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return Contact.Hit;   // a blow from afar: no legs to sweep
            }
            Reactions.Free(defender);
            return DuckSweep.Begin(defender, attacker, incoming, level, this) ? Contact.Evaded : Contact.Hit;
        }

        /// <summary>A plain blow of the same kind is met the same way (Reactions reads it, at a share of the chance).</summary>
        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            return DuckSweep.Begin(defender, attacker, null, level, this);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 2.5f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && DuckSweep.Begin(a, p.Target, null, p.Level, this);
        }
    }
}
