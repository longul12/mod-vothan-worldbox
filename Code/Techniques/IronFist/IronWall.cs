using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// IRON WALL (Iron Fist, Master). The guard closes: it sinks behind crossed forearms and a crimson
    /// shield of force glows in front of them. Punches, elbows, blades and flurries clang off it in
    /// sparks (each turned aside: the attacker's technique recoils, an ordinary blow bounces off), the
    /// wall heating whiter with every blow it takes. Once it has taken enough (or the guard has been up
    /// long enough) the arms fling open: the shield bursts outward in a ring that shoves everyone round
    /// it back, and a palm drives the last attacker away (the shove-counter, the harder the more it
    /// absorbed). A stance: it shows Guard to everyone reading it, and a guard is what grabs and kicks
    /// are for: a kick, a grab, a throw, an area blow, an ultimate, or a blow from behind goes through
    /// (a kick or a grab breaks the guard and takes the fighter out of it).
    /// Reactive (the first blow of a strike or a flurry, or an ordinary swing, raises it) and planned
    /// (raised when several foes press it). I (Master): 0.85 s, bursts after 2 blows, a 1.6-tile
    /// shove. II: 1.05 s, 3 blows, 2 tiles, the burst scars the ground. III: 1.25 s, 4 blows, 2.5
    /// tiles, the palm launches, an impact frame. Damage (x the user's damage): the ring I 0.7, II
    /// 0.9, III 1.1, the palm I 1.2, II 1.5, III 1.9, each +0.2 per blow absorbed (at most +0.8).
    /// </summary>
    internal sealed class IronWallTech : Technique
    {
        public override string Id => "iron_wall";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.IronFist;
        public override Form Forms => Form.Guard | Form.Counter;

        /// <summary>Strikes and flurries; never kicks (a guard is what a kick is for).</summary>
        public override Form Answers => Form.Flurry | Form.Punch | Form.Elbow | Form.Palm | Form.Head | Form.Knee | Form.Blade;

        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 22f, 32f };
        public override float[] Cooldown => new[] { 12f, 11f, 10f };

        public static float GuardTime(int level)
        {
            return level <= 1 ? 0.85f : level == 2 ? 1.05f : 1.25f;
        }

        public static int Holds(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        public static float RingMult(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.9f : 1.1f;
        }

        public static float PalmMult(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.9f;
        }

        public static float BurstR(int level)
        {
            return level <= 1 ? 1.6f : level == 2 ? 2f : 2.5f;
        }

        [SakugaInit]
        private static void Init()
        {
            // first in line: a raised guard turns the blow aside before anyone else answers it
            Combat.ContactHandlers.Insert(0, IronWallMove.OnContact);
            Hooks.BeforeOrdinaryHit.Add(IronWallMove.Ordinary);
            Hooks.HoldTechniques.Add(IronWallMove.Holding);
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || incoming == null || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!IronWallMove.Stops(Reads.FormsOf(incoming), defender, attacker))
            {
                return Contact.Hit;   // a kick, a grab, an ultimate, a blow from behind: the guard is no answer
            }
            Reactions.Free(defender);
            IronWallMove m = IronWallMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            m.Build(attacker);
            if (!m.Start())
            {
                return Contact.Hit;
            }
            m.Absorb(attacker);
            return Contact.Parried;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (!IronWallMove.Stops(forms, defender, attacker))
            {
                return false;
            }
            IronWallMove m = IronWallMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return false;
            }
            m.Build(attacker);
            if (!m.Start())
            {
                return false;
            }
            m.Absorb(attacker);
            return true;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            if (c.Forced)
            {
                Actor f = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                Vector2 d = f != null ? f.current_position - c.Pos : Vector2.right;
                return new Plan
                {
                    Target = f,
                    Point = f != null ? f.current_position : c.Pos,
                    Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                    Count = 1,
                    Score = 1f,
                    Data = true
                };
            }
            // raised when it is pressed: foes in reach throwing blows at it (not kicks or grabs: a guard
            // is what those are for)
            int threats = 0;
            float value = 0f;
            Actor main = null;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 2.6f; i++)
            {
                Actor v = c.Enemies[i];
                if (!Combat.Grounded(v) || !c.Allowed(v, out float cm))
                {
                    continue;
                }
                FoeState s = c.State(v);
                if ((s.Doing & (Form.Kick | Form.Grab | Form.Throw)) != 0 || s.Stunned)
                {
                    continue;
                }
                bool pressing = s.AimsAtMe || (s.Doing & (Form.Strike | Form.Flurry)) != 0 && c.EnemyDist[i] <= 1.9f;
                if (!pressing)
                {
                    continue;
                }
                threats++;
                main ??= v;
                value += c.Value(v, c.Expected(v, RingMult(level)), cm);
            }
            if (main == null)
            {
                return null;
            }
            if (threats == 1 && c.Threat(main) < 0.5f && !c.Surrounded)
            {
                return null;   // one weak foe: it just fights it
            }
            Vector2 dir = main.current_position - c.Pos;
            return new Plan
            {
                Target = main,
                Point = main.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = threats,
                Score = value + c.Value(main, c.Expected(main, PalmMult(level))) * 0.5f + c.D * (0.6f + 0.4f * threats) + 12f * threats
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            IronWallMove m = IronWallMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build(p.Target);
            return m.Start();
        }
    }

    /// <summary>
    /// The Iron Wall's move: the guard (an open stance: grabs and kicks may take it out of it), the
    /// blows it turns aside, and the burst. While the guard is up the fighter's Brain is held.
    /// </summary>
    internal sealed class IronWallMove : ChoreoMove
    {
        /// <summary>The guard is up (blows are turned aside; the stance can be taken).</summary>
        public bool Guarding;

        private int _absorbed;
        private bool _burstNow;
        private float _idleT, _scanT, _shieldFlash;
        private Actor _last;
        private long _lastId = -1;
        private Actor _first;
        private long _firstId = -1;
        private Particle _shield, _barA, _barB;
        private Hit _palm;
        private static readonly List<Actor> _near = new List<Actor>();

        private IronWallMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static IronWallMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new IronWallMove(a, t, p);
        }

        /// <summary>The guard raised in answer to 'attacker' (its blow is about to land).</summary>
        public static IronWallMove Answer(Actor defender, Actor attacker, Technique t, int level)
        {
            if (defender == null || attacker == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true
            };
            return new IronWallMove(defender, t, p);
        }

        /// <summary>
        /// While the guard is up the stance is open to whatever takes hold of it (a grab, a kick, a
        /// launch take it out of it; grabs may be planned on it), but not in the frame a blow it turns
        /// aside lands: then it is committed, so the blow clangs off the guard and no other answer of
        /// its own takes the fighter out of it (IronWallBracePatch, OnContact).
        /// </summary>
        public override bool Interruptible => base.Interruptible || Guarding && _hitFrame != Time.frameCount;

        private int _hitFrame = -1;

        /// <summary>An ordinary blow from in front is about to land on a raised guard: it is braced this frame.</summary>
        public static void Brace(Actor t, BaseSimObject by)
        {
            IronWallMove w = Of(t);
            if (w != null && by != null && by.isActor() && by.a != null && by.a != t && w.Facing(by.a))
            {
                w._hitFrame = Time.frameCount;
            }
        }

        // --------------------------------------------------------------- rules --

        /// <summary>
        /// Does a guard turn this blow aside: a punch, an elbow, a palm, a blade, a flurry from in front?
        /// Kicks, grabs, throws, ultimates, and anything from behind go through.
        /// </summary>
        public static bool Stops(Form f, Actor holder, Actor attacker)
        {
            if ((f & (Form.Grab | Form.Throw | Form.Kick | Form.Ultimate)) != 0)
            {
                return false;
            }
            if ((f & (Form.Strike | Form.Flurry)) == 0)
            {
                return false;
            }
            return InFront(holder, attacker);
        }

        /// <summary>Is 'attacker' in front of 'holder' (the side it faces, or level with it)?</summary>
        public static bool InFront(Actor holder, Actor attacker)
        {
            if (holder == null || attacker == null)
            {
                return false;
            }
            float dx = attacker.current_position.x - holder.current_position.x;
            if (Mathf.Abs(dx) < 0.3f)
            {
                return true;
            }
            bool right = Moves.TryGet(holder, out Move m) ? m.Pose.FaceRight : holder.flip;
            return dx > 0f == right;
        }

        private static IronWallMove Of(Actor v)
        {
            return v != null && Moves.TryGet(v, out Move m) && m is IronWallMove w && w.Guarding && w.A == v ? w : null;
        }

        /// <summary>Combat.Reach: a technique's blow reaching a raised guard clangs off it (the attacker recoils).</summary>
        public static Contact OnContact(Move m, Actor v, bool area)
        {
            if (area || m == null || m.A == null || m.A == v)
            {
                return Contact.Hit;
            }
            IronWallMove w = Of(v);
            if (w == null || !Combat.IsFoe(v, m.A) || !w.Facing(m.A) || !Stops(Reads.FormsOf(m), v, m.A))
            {
                return Contact.Hit;   // (a kick, a grab, a blow from behind: the guard stays open to it)
            }
            w._hitFrame = Time.frameCount;
            w.Absorb(m.A);
            return Contact.Parried;
        }

        /// <summary>Hooks.BeforeOrdinaryHit: an ordinary swing or punch on a raised guard bounces off (arrows still fly in).</summary>
        public static bool Ordinary(AttackData d, Actor t)
        {
            if (d.is_projectile || d.initiator == null || !d.initiator.isActor())
            {
                return false;
            }
            IronWallMove w = Of(t);
            Actor a = d.initiator.a;
            if (w == null || a == null || a == t || !w.Facing(a))
            {
                return false;
            }
            w.Absorb(a);
            return true;
        }

        /// <summary>Hooks.HoldTechniques: the guard is held until it bursts (its own Brain doesn't drop it for something else).</summary>
        public static bool Holding(Actor a)
        {
            return Of(a) != null;
        }

        private bool Facing(Actor a)
        {
            float dx = a.current_position.x - Pose.Ground.x;
            return Mathf.Abs(dx) < 0.3f || dx > 0f == Pose.FaceRight;
        }

        // -------------------------------------------------------------- script --

        public void Build(Actor first)
        {
            int level = Level;
            _first = first;
            _firstId = first != null ? first.Sid() : -1;
            if (first != null)
            {
                Face(first.current_position - Stance);
            }
            // (nobody is pinned by a guard: the foes are free to throw their blows at it)
            SetVictim(null);
            // the guard: low behind crossed forearms, the shield of force in front
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = IronWallTech.GuardTime(level),
                To = new Shape(-5f, 1.1f, 0.88f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Guard | Form.Counter,
                Enter = mm => ((IronWallMove)mm).RaiseGuard(),
                Every = (mm, k) => ((IronWallMove)mm).TickGuard(k),
                Exit = mm => ((IronWallMove)mm).LowerGuard()
            });
            // the arms cross tight (the coil before the burst)
            Do(mm => ((IronWallMove)mm).PickTarget());
            PoseTo(0.07f, new Shape(-12f, 0.9f, 1.12f, 0f, -0.08f), Ease.OutQuad, Tempo.Wind).Shows(Form.Counter | Form.Palm | Form.Area);
            // they fling open: the ring bursts outward
            Do(mm => ((IronWallMove)mm).Burst());
            PoseTo(0.05f, new Shape(8f, 1.24f, 0.88f, 0f, 0.04f), Ease.OutExpo, Tempo.Act).Shows(Form.Counter | Form.Palm | Form.Area);
            Do(mm =>
            {
                if (mm.V == null)
                {
                    mm.JumpToRecovery();   // nobody left in reach to drive away
                }
            });
            // the shove-counter: a palm that drives the last attacker away
            _palm = new Hit(Blow.Palm, IronWallTech.PalmMult(level))
            {
                Hurt = level >= 3 ? Hurt.Launch : Hurt.Reel,
                Push = level <= 1 ? 2f : level == 2 ? 2.6f : 3.2f,
                Lift = 1.1f,
                Finisher = true,
                Power = 1.25f,
                WindScale = 0.6f,
                Shows = Form.Palm | Form.Counter | Form.Straight
            };
            Strike(_palm);
            Recover(0.25f);
        }

        private void RaiseGuard()
        {
            Guarding = true;
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.15f);
            float px = Px;
            Sprite s = ChoreoArt.Crescent(Mathf.Max(4, Mathf.RoundToInt(Hpx * 0.6f)), Level >= 3 ? 3 : 2);
            _shield = Glue(Fx.Spawn(s, BodyPos(), px, 1f, FxLayer.Over, 9));
            // the forearms: two bars crossed in front of the face
            _barA = Glue(Fx.Spawn(Art.Pixel, BodyPos(), px, 1f, FxLayer.Over, 10));
            _barB = Glue(Fx.Spawn(Art.Pixel, BodyPos(), px, 1f, FxLayer.Over, 10));
            if (Vis)
            {
                Kit.Flash(BodyPos() + new Vector2(Fwd * Hpx * 0.3f * px, 0f), px, Mathf.RoundToInt(3f * Kk), 0.06f, Fx.WithA(Hue.Core, 0.9f), FxLayer.Over, 11);
                Fx.Ring(Pose.Ground, px, 0.2f, 8f * Kk, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.8f, Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.8f));
                ChoreoFx.HeelDust(Pose.Ground, -Dir, px, A.current_tile, Kk);
            }
        }

        private void TickGuard(float k)
        {
            if (_burstNow)
            {
                BeatDur = Mathf.Min(BeatDur, BeatT);   // (taken enough: the burst comes now)
            }
            // nothing left in reach to guard against (after a first moment): it bursts early
            _scanT -= Clock.Dt;
            if (_scanT <= 0f)
            {
                _scanT = 0.1f;
                int n = Choreo.Foes(A, Pose.Ground, 2.6f * Mathf.Lerp(1f, Kk, 0.5f), _near);
                _idleT = n > 0 ? 0f : _idleT + 0.1f;
                if (_idleT >= 0.35f && BeatT > 0.3f)
                {
                    BeatDur = Mathf.Min(BeatDur, BeatT);
                }
            }
            _shieldFlash = Mathf.Max(0f, _shieldFlash - Clock.Dt * 6f);
            Shields(k);
        }

        /// <summary>The shield and the crossed forearms in front of it, heating whiter with every blow taken.</summary>
        private void Shields(float k)
        {
            float px = Px;
            float heat = Mathf.Clamp01(_absorbed / (float)IronWallTech.Holds(Level));
            float fade = Mathf.Clamp01(Mathf.Min(k * 10f, (1f - k) * 14f + 0.2f));
            Vector2 body = BodyPos();
            // (crimson at every level: the wall reads as the Iron Fist's; it heats towards white as it takes blows)
            Color hot = Color.Lerp(Color.Lerp(Schools.Color(School.IronFist), Hue.Edge, 0.35f), Hue.Core, 0.15f + 0.65f * heat);
            hot = Color.Lerp(hot, Color.white, _shieldFlash);
            if (_shield != null && _shield.Alive)
            {
                _shield.Pos = body + new Vector2(Fwd * Hpx * 0.52f, Hpx * 0.06f) * px;
                _shield.SX = Fwd;
                _shield.Grad = null;
                float pulse = 0.6f + 0.2f * Mathf.Sin(BeatT * 14f) + 0.25f * _shieldFlash;
                _shield.R.color = Fx.WithA(hot, Mathf.Clamp01(pulse) * fade);
                _shield.Size0 = _shield.Size1 = 1f + 0.12f * _shieldFlash;
            }
            Vector2 arms = body + new Vector2(Fwd * Hpx * 0.32f, Hpx * 0.14f) * px;
            Bar(_barA, arms, 38f, hot, fade);
            Bar(_barB, arms, -38f, hot, fade);
        }

        private void Bar(Particle p, Vector2 at, float ang, Color c, float fade)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = at;
            p.SX = Mathf.Max(2f, Hpx * 0.42f);
            p.SY = Level >= 2 ? 2f : 1f;
            p.Rot = Fwd > 0f ? 90f + ang : 90f - ang;
            p.Grad = null;
            p.R.color = Fx.WithA(Color.Lerp(c, Hue.Rim, 0.35f), 0.85f * fade);
        }

        private void LowerGuard()
        {
            Guarding = false;
            KillShields();
        }

        private void KillShields()
        {
            if (_shield != null)
            {
                Unglue(_shield);
                _shield = null;
            }
            if (_barA != null)
            {
                Unglue(_barA);
                _barA = null;
            }
            if (_barB != null)
            {
                Unglue(_barB);
                _barB = null;
            }
        }

        /// <summary>
        /// A blow turned aside: a clang, sparks where it met the shield, the fighter rocked back a step
        /// behind its guard, the wall a shade hotter. Enough of them and it bursts.
        /// </summary>
        public void Absorb(Actor by)
        {
            if (!Guarding && _absorbed > 0)
            {
                return;
            }
            _absorbed++;
            _shieldFlash = 1f;
            if (by != null)
            {
                _last = by;
                _lastId = by.Sid();
                Vector2 d = by.current_position - Stance;
                if (Mathf.Abs(d.x) > 0.12f)
                {
                    Face(d);
                }
                // an ordinary arm bounces off (a technique's own move recoils by itself)
                if (!Moves.Busy(by) && !Weights.Heavy(by) && Combat.Living(by))
                {
                    by.startShake(0.15f, 0.08f, true, false);
                }
            }
            // rocked back behind the guard
            Vector2 back = Combat.Clamp(Stance - Dir * 0.07f * Kk);
            if (Combat.SafeSpot(back))
            {
                Stance = back;
            }
            if (Guarding)
            {
                HoldFor(0.035f + 0.01f * Level);   // (the first blow lands as the guard closes: no pause before it)
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.04f);
            if (_absorbed >= IronWallTech.Holds(Level))
            {
                _burstNow = true;
            }
            if (Vis)
            {
                float px = Px;
                Vector2 at = BodyPos() + new Vector2(Fwd * Hpx * 0.54f, Hpx * 0.1f) * px;
                Fx.Glint(at, px, 0.14f, Color.white, 0.9f + 0.1f * Level);
                Kit.Sparks(at, px, 4 + Level, 50f, 110f, Color.Lerp(Hue.Core, new Color(1f, 0.9f, 0.6f, 1f), 0.5f));
                Kit.Flash(at, px, Mathf.RoundToInt(2f + Level), 0.05f, Fx.WithA(Color.white, 0.9f), FxLayer.Over, 12);
                Vector2 across = new Vector2(0f, 1f);
                Kit.Line(at - across * 2.5f * px, at + across * 2.5f * px, px, 0.06f, 1, FxLayer.Top, 22, Color.white, Fx.WithA(Hue.Edge, 0f));
                ChoreoFx.HeelDust(Pose.Ground, -Dir, px, A.current_tile, Kk * 0.7f);
            }
            if (Dev.On) Dev.Trace("choreo " + Id + " iron wall absorbs a blow of " + (by != null ? by.asset.id + "#" + by.Sid() : "?") + " (" + _absorbed + ")");
        }

        // --------------------------------------------------------------- burst --

        /// <summary>Whom the palm drives away: the last attacker if it is still in reach, else the nearest foe in front.</summary>
        private void PickTarget()
        {
            Actor pick = null;
            float reach = 2.4f * Kk;
            if (Ok(_last, _lastId, reach))
            {
                pick = _last;
            }
            else if (Ok(_first, _firstId, reach))
            {
                pick = _first;
            }
            else
            {
                Choreo.Foes(A, Stance, reach, _near);
                float best = float.MaxValue;
                for (int i = 0; i < _near.Count; i++)
                {
                    Actor u = _near[i];
                    if (!Choreo.Fair(A, u, out _, Forced) || !Facing(u))
                    {
                        continue;
                    }
                    float d = (u.current_position - Stance).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        pick = u;
                    }
                }
            }
            SetVictim(pick);
            if (pick != null)
            {
                Face(VG - Stance);
            }
        }

        private bool Ok(Actor u, long id, float reach)
        {
            return u != null && u.Sid() == id && Choreo.Fair(A, u, out _, Forced) && Combat.Grounded(u)
                && Vector2.Distance(u.current_position, Stance) <= reach + u.stats["size"] * 0.5f;
        }

        /// <summary>The shield bursts outward: a ring that shoves everyone round it back; the more it took, the harder.</summary>
        private void Burst()
        {
            int level = Level;
            int bonus = Mathf.Min(_absorbed, 4);
            float r = IronWallTech.BurstR(level) * Mathf.Lerp(1f, Kk, 0.5f);
            float push = (level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.5f) + 0.15f * bonus;
            AreaBlow(Pose.Ground, r, IronWallTech.RingMult(level) + 0.1f * bonus, V, level >= 2 ? Hurt.Knockdown : Hurt.Reel, push, 1.1f);
            if (_palm != null)
            {
                _palm.Mult = IronWallTech.PalmMult(level) + 0.2f * bonus;
                _palm.Power = 1.2f + 0.1f * bonus;
            }
            if (level >= 2)
            {
                Ruin.Blast(A, Pose.Ground, 1f + 0.2f * level, Scale, Pose.Ground, false);
            }
            Ruin.Quake(A, Pose.Ground, Scale, 0.9f + 0.1f * bonus, Kk);
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile, 0.1f);
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Vector2 body = BodyPos();
            Kit.Flash(body, px, Mathf.RoundToInt((4f + level) * Kk), 0.08f, Fx.WithA(Hue.Core, 1f), TopLayer);
            Fx.Ring(Pose.Ground, px, 0.3f, 4f, r / px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.75f), Fx.WithA(Hue.Rim, 0f));
            Fx.Ring(body, px, 0.22f, 3f, (10f + 3f * level) * Kk, 2, 1, TopLayer, 5, 0.9f, 2.4f,
                Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            Kit.DustRing(Pose.Ground, px, A.current_tile, 12, 52f, 0.5f, Kk);
            // the shield breaks into shards flung outward
            int n = Fx.Count(10 + 3 * level);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), body + d * 2f * px, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 12);
                if (p == null)
                {
                    break;
                }
                p.Moving(d * Random.Range(50f, 110f) * px, Random.Range(10f, 30f) * px, 2.5f, 160f * px);
                p.Spin = Random.Range(-500f, 500f);
                p.Color(Color.white, Fx.WithA(Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            if (level >= 3)
            {
                Kit.ImpactFrame(body, px, 0.35f * Kk, Hue.Pal, false, Cinematic ? FxLayer.Cine : FxLayer.Behind);
            }
        }

        /// <summary>Taken out of its guard (a kick, a grab, a launch): the shield cracks apart.</summary>
        public override void Abort()
        {
            if (Guarding && Vis)
            {
                float px = Px;
                Vector2 at = BodyPos() + new Vector2(Fwd * Hpx * 0.52f, 0f) * px;
                Kit.Sparks(at, px, 6, 40f, 90f, Hue.Edge);
                Fx.Glint(at, px, 0.12f, Hue.Core, 0.8f);
                Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.05f);
            }
            Guarding = false;
            base.Abort();
            _shield = _barA = _barB = null;
        }

        public override void OnEnd(bool aborted)
        {
            Guarding = false;
            base.OnEnd(aborted);
        }
    }

    /// <summary>
    /// An ordinary blow of the game is about to land (MapBox.applyAttack): before any answer looks at
    /// it (they take their turns in a shuffled order), a fighter behind a raised Iron Wall facing the
    /// blow is braced: the guard takes it, and no other answer of its own drops the guard for it.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), "applyAttack")]
    internal static class IronWallBracePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(AttackData pData, BaseSimObject pTargetToCheck)
        {
            try
            {
                if (pTargetToCheck != null && pTargetToCheck.isActor() && !pData.is_projectile)
                {
                    IronWallMove.Brace(pTargetToCheck.a, pData.initiator);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("iron wall brace", e);
            }
        }
    }
}
