using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// REVERSAL. The counter to the counter. Its own attack was just answered (parried, slipped) and
    /// the answer is coming back at it: a Grandmaster has already read that too. It catches the
    /// countering limb, the world flashes black and white for a frame, and it is behind the counterer,
    /// driving an elbow into its back: the counter is broken and the counterer is sent flying.
    /// It also sees through feints (Answers Feint): the bait is taken at its own price.
    ///
    /// Beat sheet (game seconds at x1, from the counter's contact frame):
    ///
    ///   CATCH    0.06  both flash white, a "ting" glint where the blows meet, a short bright bar
    ///                  across them; (III) an impact frame in black and white and a beat of hit-stop
    ///   TURN     0.07  gone (a frame of afterimage where it stood, a streak to where it is going),
    ///                  then behind the counterer, low, facing its back (nothing can touch it between)
    ///   ELBOW    0.07  the elbow into the back: stretched and leaning in, a short crescent, the
    ///                  contact (Combat.Reach: a God may answer even this), a big hit star; the
    ///                  counter is cut short and the counterer is launched 2-3 tiles, dazed
    ///   RECOVER  0.18  springs back upright. Chain: Launched (or Down for a giant)
    ///
    /// Reactive (Answers Counter | Feint): it can only meet an answer, so it only ever plays in the
    /// middle of an exchange of counters, from inside its own broken move (Reactions.Free).
    /// </summary>
    internal sealed class Reversal : CounterMove
    {
        private const int Catch = 0, Turn = 1, Elbow = 2, Back = 3;

        private Vector2 _from, _to;
        private bool _hit;
        private float _r0, _sx0 = 1f, _sy0 = 1f;

        private Reversal(Actor a, Actor v, Move incoming, int level, Technique tech) : base(a, v, incoming, level, tech)
        {
        }

        public override Form Forms => Form.Counter | Form.Fast | Form.Elbow | Form.Teleport;

        public override Actor Aim => Foe;

        public override bool Intangible => Ph == Turn;

        public static bool Begin(Actor d, Actor atk, Move incoming, int level, Technique tech)
        {
            if (d == null || atk == null || d == atk || !Combat.Living(atk) || !Moves.CanPerform(d))
            {
                return false;
            }
            Reversal m = new Reversal(d, atk, incoming, level, tech);
            m.PlanTurn();
            if (!Moves.Start(m))
            {
                return false;
            }
            m.CatchFx();
            Dev.Watch(Vector2.Lerp(m._from, atk.current_position, 0.5f) + new Vector2(0f, 1f));
            return true;
        }

        /// <summary>Behind the counterer (its back to it), on safe ground; failing that, where it stands.</summary>
        private void PlanTurn()
        {
            _from = Pose.Ground;
            Vector2 f = Foe.current_position;
            Vector2 d = f - _from;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
            float dist = Mathf.Max(Reach, 0.6f * Kk);
            Vector2 want = f + d * dist;
            _to = Combat.SafeSpot(want) ? want : _from;
            Home = _to;
            Next(Catch);
        }

        /// <summary>The counterer may have moved since the catch: behind it as it stands now.</summary>
        private void Replan()
        {
            if (!FoeOk())
            {
                return;
            }
            Vector2 f = Foe.current_position;
            Vector2 d = f - _from;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
            float dist = Mathf.Max(Reach, 0.6f * Kk);
            Vector2 want = f + d * dist;
            if (Combat.SafeSpot(want) && Vector2.Distance(_from, want) <= 4f * Kk)
            {
                _to = want;
            }
            else
            {
                _to = Close(_from, 3f * Kk);
            }
            Home = _to;
        }

        private void CatchFx()
        {
            Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
            if (!Visible)
            {
                return;
            }
            float px = Px;
            Vector2 body = BodyPos();
            Vector2 fb = FoeBody();
            Vector2 n = fb - body;
            float dist = n.magnitude;
            n = dist > 0.001f ? n / dist : new Vector2(Fwd, 0f);
            Vector2 contact = body + n * Mathf.Min(dist * 0.5f, 2.5f * px);
            StillWater.FlashUnit(A, 0.07f, new Color(1f, 1f, 1f, 0.9f));
            StillWater.FlashUnit(Foe, 0.07f, new Color(1f, 1f, 1f, 0.9f));
            Fx.Glint(contact, px, 0.18f, Color.white, 1.1f);
            Vector2 across = new Vector2(-n.y, n.x);
            Kit.Line(contact - across * 2.5f * px, contact + across * 2.5f * px, px, 0.08f, 1, FxLayer.Top, 25, Color.white, Fx.WithA(Pal.Edge, 0f));
            if (Level >= 3)
            {
                Kit.ImpactFrame(contact, px, 0.3f * Kk, Pal, true);
                Kit.Stop(contact, 0.07f);
            }
        }

        public override void Tick(float dt)
        {
            Pt += dt;
            if (!FoeOk() && Ph < Back)
            {
                Pose.Hidden = false;
                Next(Back);
            }
            float px = Px;
            switch (Ph)
            {
                case Catch:
                    {
                        float k = Mathf.Clamp01(Pt / (0.06f * Tempo));
                        Pose.Ground = _from;
                        Pose.Rot = -15f * Anim.OutQuad(k);
                        Pose.SX = 1f - 0.06f * k;
                        Pose.SY = 1f + 0.05f * k;
                        Face(Foe.current_position);
                        if (k >= 1f)
                        {
                            Next(Turn);
                        }
                        break;
                    }
                case Turn:
                    {
                        float t = 0.07f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        if (Enter())
                        {
                            Replan();
                        }
                        if (Pt <= dt && Visible)
                        {
                            Fx.Afterimage(this, 0.22f, Pal.Ghost, Pal.GhostEnd);
                            Vector2 dir = _to - _from;
                            if (dir.sqrMagnitude > 0.01f)
                            {
                                Kit.SpeedLines(BodyPos(), dir.normalized, px, 3, 3f * Kk, 0.1f, Fx.WithA(Pal.Edge, 0.8f));
                            }
                        }
                        Pose.Hidden = k < 0.7f;
                        Pose.Ground = Vector2.Lerp(_from, _to, Anim.InOutCubic(k));
                        Pose.Rot = 8f;
                        Pose.SX = 1.1f;
                        Pose.SY = 0.88f;
                        if (FoeOk())
                        {
                            Face(Foe.current_position);
                        }
                        if (k >= 1f)
                        {
                            Pose.Hidden = false;
                            if (Visible)
                            {
                                Fx.LandingPuff(_to, px, A.current_tile);
                            }
                            Next(Elbow);
                        }
                        break;
                    }
                case Elbow:
                    {
                        float t = 0.07f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        float e = Anim.OutCubic(k);
                        Pose.Ground = Home;
                        Pose.Rot = Mathf.Lerp(8f, 22f, e);
                        Pose.SX = Mathf.Lerp(1.1f, 1.18f, e);
                        Pose.SY = Mathf.Lerp(0.88f, 0.9f, e);
                        if (Enter())
                        {
                            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                            if (Visible)
                            {
                                Kit.Swish(this, Art.Arc(Mathf.RoundToInt(6f * Kk), 0.6f, 60f, -30f, 2), 0.09f, new Vector2(Fwd * 2f, 1f), Fwd, Pal.Core, Pal.Edge);
                            }
                        }
                        if (!_hit && k >= 0.5f)
                        {
                            _hit = true;
                            Smash();
                        }
                        if (k >= 1f)
                        {
                            Next(Back);
                        }
                        break;
                    }
                default:
                    {
                        if (Enter())
                        {
                            Pose.Hidden = false;
                            _r0 = Pose.Rot;
                            _sx0 = Pose.SX;
                            _sy0 = Pose.SY;
                        }
                        if (Recover(0.18f * Snap, _r0, _sx0, _sy0))
                        {
                            Done(Launched ? SingleKit.AirLink(A, 0.8f) : Landed ? Link.Down : Link.None);
                        }
                        break;
                    }
            }
        }

        /// <summary>The elbow into the counterer's back: its counter is broken, it flies.</summary>
        private void Smash()
        {
            Vector2 d = FoeOk() ? Foe.current_position - Pose.Ground : new Vector2(Fwd, 0f);
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
            float mult = Level <= 1 ? 2f : Level == 2 ? 2.6f : 3.2f;
            if (!Strike(mult, d, Level >= 3 ? 0.09f : 0.06f, 2, 1f))
            {
                return;
            }
            if (Level >= 2)
            {
                Kit.Shake(FoeBody(), 0.12f, 0.004f, Kk);
            }
            Throw(Pose.Ground, Level >= 3 ? 3f : 2.2f, Level >= 3 ? 1.6f : 1.1f, 0.8f, 720f);
        }

        public override void Abort()
        {
            Pose.Hidden = false;
        }
    }

    /// <summary>
    /// Reversal as a technique. Entry: Grandmaster (universal). Reactive: it answers answers (Answers
    /// Counter: a parry's riposte, a slip's hook, a duck's sweep, even a counter-attack chained off
    /// an evasion) and feints. Never planned; forced (dev) it plays on the nearest enemy.
    /// </summary>
    internal sealed class ReversalTech : Technique
    {
        public override string Id => "reversal";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;

        public override Form Forms => Form.Counter | Form.Fast | Form.Elbow | Form.Teleport;

        public override Form Answers => Form.Counter | Form.Feint;

        public override Scale Scale => Scale.Heavy;

        public override float[] ManaCost => new[] { 18f, 26f, 34f };
        public override float[] Cooldown => new[] { 8f, 7f, 6f };

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return Reversal.Begin(defender, attacker, incoming, level, this) ? Contact.Parried : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;
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
            return p.Target != null && Reversal.Begin(a, p.Target, null, p.Level, this);
        }
    }
}
