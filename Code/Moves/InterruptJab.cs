using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// INTERRUPT JAB. An enemy plants its feet and starts gathering power (a slam's crouch, a
    /// stance, a beam in its hands): the fighter is already there, and one snapping jab to the face
    /// breaks it. The gathered power bursts apart and the charger is sent flying (Interrupts.Break).
    ///
    /// Beat sheet (game seconds at x1; the tier's tempo shortens the action beats by the square
    /// root of its Linger, the recovery by its Linger):
    ///
    ///   DART     0.06-0.16  a low dash into arm's reach (only if it isn't there): leaning in,
    ///                       stretched, an afterimage every few hundredths, heel dust kicked back
    ///                       (III, from 3 tiles: a blink, gone for a frame, nothing can touch it)
    ///   COCK     0.04       the lead hand drawn back: a squash, the weight on the back foot
    ///   SNAP     0.05       the jab: stretched 120% and leaning in, a straight streak from the
    ///                       shoulder to the fist, a short white arc; at the end the contact
    ///                       (Combat.Reach: the charger may still answer it): a hit star, a flash,
    ///                       and the charge breaks (or a plain foe is rocked back)
    ///   II         a second jab a beat later (the one-two), the breaking one
    ///   III        the jab looses a ring of pressure and throws the charger hardest
    ///   HOLD       0.06       the arm stays out, trembling a pixel
    ///   RECOVER    0.18       springs back upright. Chain: Launched (the charger is in the air)
    ///                         or Close
    ///
    /// Planned against a charging enemy within reach of the dart (2.5 tiles, Grandmasters 4, Gods 5):
    /// the Brain's tactics push it hardest exactly then (Interrupt x3.2, and it answers Charge).
    /// Reactive too: a charged blow arriving is met by the jab first.
    /// </summary>
    internal sealed class InterruptJab : CounterMove
    {
        private const int Dart = 0, Cock = 1, SnapIn = 2, Hold = 3, Back = 4;

        private Vector2 _from, _to;
        private float _tDart;
        private bool _struck, _blink, _second, _secondStruck;
        private float _ghost;
        private bool _broke;

        private InterruptJab(Actor a, Actor v, Move incoming, int level, Technique tech) : base(a, v, incoming, level, tech)
        {
        }

        public override bool Intangible => _blink && Ph == Dart && Pose.Hidden;

        public override Form Forms => Form.Punch | Form.Fast | Form.Interrupt | Form.Straight;

        public override Actor Aim => Foe;

        /// <summary>Starts the jab on 'v'. 'react': met as an answer (no dart: it is already there).</summary>
        public static bool Begin(Actor a, Actor v, int level, Technique tech, Move incoming, bool react)
        {
            if (a == null || v == null || a == v || !Combat.Living(v) || !Moves.CanPerform(a))
            {
                return false;
            }
            InterruptJab m = new InterruptJab(a, v, incoming, level, tech);
            if (!m.Plan(react))
            {
                return false;
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            if (m._tDart > 0f && m.Visible)
            {
                Fx.LandingPuff(m._from, m.Px, a.current_tile);
            }
            Dev.Watch(Vector2.Lerp(m._from, v.current_position, 0.5f) + new Vector2(0f, 1f));
            return true;
        }

        private bool Plan(bool react)
        {
            _from = Pose.Ground;
            Vector2 v = Foe.current_position;
            float d = Vector2.Distance(_from, v);
            float stand = Reach;
            if (d <= stand + 0.25f || react)
            {
                _to = _from;
                _tDart = 0f;
            }
            else
            {
                Vector2 want = v + (_from - v) / d * stand;
                _to = StepTo(_from, want);
                if (Vector2.Distance(_to, v) > stand + 0.6f)
                {
                    return false;   // no way in (water, a wall)
                }
                float len = Vector2.Distance(_from, _to);
                _tDart = Mathf.Clamp(0.05f + 0.028f * len, 0.06f, 0.16f) * Tempo;
                _blink = Level >= 3 && len > 3f;
            }
            _second = Level == 2;
            Home = _to;
            Next(_tDart > 0f ? Dart : Cock);
            Face(v);
            return true;
        }

        public override void Tick(float dt)
        {
            Pt += dt;
            if (!FoeOk())
            {
                if (Ph < Hold)
                {
                    Next(Back);
                }
            }
            else if (Ph < Hold)
            {
                Face(Foe.current_position);
            }
            float px = Px;
            switch (Ph)
            {
                case Dart:
                    {
                        float k = Mathf.Clamp01(Pt / Mathf.Max(0.01f, _tDart));
                        Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
                        Pose.Rot = 12f;
                        Pose.SX = 1.16f;
                        Pose.SY = 0.9f;
                        Pose.Hidden = _blink && k > 0.2f && k < 0.75f;
                        _ghost -= dt;
                        if (_ghost <= 0f && Visible && !Pose.Hidden)
                        {
                            _ghost = 0.03f;
                            Fx.Afterimage(this, 0.14f, Pal.Ghost, Pal.GhostEnd);
                        }
                        if (k >= 1f)
                        {
                            Pose.Hidden = false;
                            Next(Cock);
                        }
                        break;
                    }
                case Cock:
                    {
                        float k = Mathf.Clamp01(Pt / (0.04f * Tempo));
                        Pose.Ground = Home;
                        Pose.Rot = Mathf.Lerp(12f, -6f, Anim.OutQuad(k));
                        Pose.SX = Mathf.Lerp(1.16f, 0.94f, k);
                        Pose.SY = Mathf.Lerp(0.9f, 1.04f, k);
                        if (k >= 1f)
                        {
                            Next(SnapIn);
                        }
                        break;
                    }
                case SnapIn:
                    {
                        float t = 0.05f * Tempo;
                        float k = Mathf.Clamp01(Pt / t);
                        float e = Anim.OutCubic(k);
                        Vector2 dir = Dir();
                        Pose.Ground = Home + dir * 0.14f * Kk * e;
                        Pose.Rot = Mathf.Lerp(-6f, 16f, e);
                        Pose.SX = Mathf.Lerp(0.94f, 1.2f, e);
                        Pose.SY = Mathf.Lerp(1.04f, 0.9f, e);
                        if (Enter() && Visible)
                        {
                            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                            Streak(dir, px);
                        }
                        if (k >= 1f)
                        {
                            if (!_struck)
                            {
                                _struck = true;
                                Jab(dir, !_second);
                            }
                            else if (_second && !_secondStruck)
                            {
                                _secondStruck = true;
                                Jab(dir, true);
                            }
                            Next(Hold);
                        }
                        break;
                    }
                case Hold:
                    {
                        Pose.Nudge = ((int)(Pt * 120f) & 1) == 0 ? new Vector2(0.5f, 0f) : Vector2.zero;
                        if (Pt >= 0.06f * Tempo)
                        {
                            Pose.Nudge = Vector2.zero;
                            if (_second && !_secondStruck && FoeOk() && !_broke)
                            {
                                // the one-two: back to the cock for the breaking jab
                                Next(Cock);
                            }
                            else
                            {
                                Next(Back);
                            }
                        }
                        break;
                    }
                default:
                    {
                        if (Recover(0.18f * Snap, 16f, 1.2f, 0.9f))
                        {
                            Done(Launched ? SingleKit.AirLink(A, 0.7f) : Link.Close);
                        }
                        break;
                    }
            }
        }

        private Vector2 Dir()
        {
            if (!FoeOk())
            {
                return new Vector2(Fwd, 0f);
            }
            Vector2 d = Foe.current_position - Pose.Ground;
            return d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
        }

        /// <summary>The jab's streak: a straight line from the shoulder to the fist, a short white arc at the end.</summary>
        private void Streak(Vector2 dir, float px)
        {
            Vector2 shoulder = BodyPos() + new Vector2(0f, 1f * px);
            Vector2 fist = shoulder + dir * (5f * Kk) * px;
            Kit.Line(shoulder, fist, px, 0.07f, 1, FxLayer.Over, 12, Fx.WithA(Color.white, 0.9f), Fx.WithA(Pal.Edge, 0.6f), Fx.WithA(Pal.Edge, 0f));
            Kit.Swish(this, Art.Arc(Mathf.RoundToInt(5f * Kk), 0.7f, 25f, -25f, 1), 0.07f, new Vector2(Fwd * 3f, 1f), Fwd, Pal.Core, Pal.Edge);
        }

        /// <summary>The contact: breaks a charge (the main point), else rocks the foe back.</summary>
        private void Jab(Vector2 dir, bool breaking)
        {
            bool charging = FoeOk() && Moves.Charging(Foe);
            float mult = Level <= 1 ? 0.7f : Level == 2 ? 0.55f : 1.3f;
            if (!Strike(mult, dir, breaking ? 0.05f : 0.03f, breaking ? 2 : 1))
            {
                return;
            }
            if (Level >= 3 && Visible)
            {
                // a ring of pressure off the fist
                Vector2 at = FoeBody();
                Fx.Ring(at, Px, 0.2f, 3f, 16f * Kk, 2, 1, FxLayer.Top, 4, 0.6f, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Pal.Edge, 0.5f), Fx.WithA(Pal.Edge, 0f));
                Kit.SpeedLines(at, dir, Px, 4, 4f * Kk, 0.12f, Fx.WithA(Pal.Edge, 0.8f));
            }
            if (charging)
            {
                // the blow itself broke it (an Interrupt always does, a far stronger charger shrugs off
                // half): Interrupts.OnHit threw it; III throws harder still
                _broke = FoeOk() && !Moves.Charging(Foe);
                Launched = _broke && !Weights.Heavy(Foe);
                if (_broke && Level >= 3 && !Weights.Heavy(Foe))
                {
                    Throw(Pose.Ground, 2.6f, 1.2f, 0.6f);
                }
                return;
            }
            if (!breaking)
            {
                return;   // (II's lead jab only sets it up)
            }
            // nothing to break: a plain jab rocks it back a step
            Throw(Pose.Ground, Level >= 3 ? 1.2f : 0.6f, 0.25f, Level >= 3 ? 0.5f : 0.3f, 0f);
        }

        public override void Abort()
        {
            Pose.Hidden = false;
        }
    }

    /// <summary>
    /// Interrupt Jab as a technique. Entry: Novice (universal: every school). Reactive and planned:
    /// the Brain throws it at an enemy gathering power within reach of its dart (its tactics weigh it
    /// x3.2 against a charging foe), and a charged blow that reaches the fighter may be met by it
    /// first. Forced (dev command, god power) it darts at the nearest enemy and jabs.
    /// </summary>
    internal sealed class InterruptJabTech : Technique
    {
        public override string Id => "interrupt_jab";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override bool Reactive => true;

        public override Form Forms => Form.Punch | Form.Fast | Form.Interrupt | Form.Straight;

        public override Form Answers => Form.Charge;

        public override Scale Scale => Scale.Casual;

        public override float[] ManaCost => new[] { 6f, 10f, 14f };
        public override float[] Cooldown => new[] { 5f, 4.5f, 4f };

        /// <summary>How far its dart reaches (tiles): further the higher the tier.</summary>
        private static float Dart(Tier t)
        {
            return t >= Tier.God ? 5f : t >= Tier.Grandmaster ? 4f : t >= Tier.Master ? 3.2f : 2.5f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || incoming == null || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return InterruptJab.Begin(defender, attacker, level, this, incoming, true) ? Contact.Parried : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float reach = Dart(c.Tier) + (level >= 3 ? 1f : 0f);
            Actor best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= reach + 1f; i++)
            {
                Actor v = c.Enemies[i];
                if (!c.Allowed(v) || !Combat.Grounded(v))
                {
                    continue;
                }
                bool charging = Moves.Charging(v);
                if (!charging && !c.Forced)
                {
                    continue;
                }
                float d = c.EnemyDist[i];
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            if (c.Forced)
            {
                best = c.ForceTarget ?? best ?? c.Focus;
                if (best == null || c.Dist(best) > reach + 1f)
                {
                    return null;
                }
                return new Plan { Target = best, Point = best.current_position, Count = 1, Score = 1f };
            }
            if (best == null)
            {
                return null;
            }
            // breaking a charge is worth the blow it stops: the charger's power and then some
            float worth = c.D * 3f + 30f + 10f * Tiers.Power(best);
            return new Plan { Target = best, Point = best.current_position, Count = 1, Score = worth };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && InterruptJab.Begin(a, p.Target, p.Level, this, null, false);
        }
    }
}
