using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// MIRROR RETURN (Ki Arts, Master). The energy that comes for it goes home bigger. A beam reaching
    /// it splashes on its two open palms: it is driven back skidding, the beam's head held in a
    /// knot of both colours between its hands, sparks spraying, trembling... then it shoves, the
    /// enemy's beam dies, and its own roars back down the same line, wider and stronger, blasting
    /// the shooter out of its stance. A ki orb is caught in its palms, swells there, and is thrown
    /// back larger; a Celestial Lance is caught by its head, turned round in its hands and hurled
    /// home; the ki of a palm strike is caught between its own palms and pushed back into the one
    /// who threw it (launched). Seeing a foe gather a beam or a shot aimed at it, it may also open
    /// its palms and wait for it (a stance that shows Guard: a kick or any blow that isn't ki breaks
    /// it). It cannot hold a Final Radiance, and a far mightier foe's energy is too much for it
    /// (I: up to one tier above its own; II: two; III: any).
    /// I: x1.3 the power sent back; II: x1.5, an impact frame; III: x1.8, a crater where an orb lands.
    /// A palm pushed back: I 1.5, II 2.1, III 2.8 (x the user's damage).
    /// </summary>
    internal sealed class MirrorReturnTech : Technique
    {
        public override string Id => "mirror_return";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Counter | Form.Guard;
        public override Form Answers => Form.Beam | Form.Ki;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 20f, 30f, 42f };
        public override float[] Cooldown => new[] { 14f, 13f, 12f };

        public override Scale ScaleAt(int level)
        {
            return level >= 2 ? Scale.Heavy : Scale.Light;
        }

        /// <summary>How much more power goes back than came.</summary>
        public static float Boost(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.5f : 1.8f;
        }

        public static float MeleeMult(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2.1f : 2.8f;
        }

        /// <summary>Can it hold this foe's energy (a tier gap of at most 'level')?</summary>
        public static bool Fits(Actor defender, Actor attacker, int level)
        {
            return (int)Tiers.Of(attacker) - (int)Tiers.Of(defender) <= level;
        }

        [SakugaInit]
        private static void Init()
        {
            Combat.ContactHandlers.Add(StanceContact);
            Hooks.HitDealt += StanceStruck;
            MirrorDev.Register();
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Forced)
            {
                Actor f = c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                return f == null ? null : Stance(c, f, 1f);
            }
            // a beam already on its way: palms open for it
            Beam b = Beams.Toward(c.Self, 1f);
            if (b != null && b.Firing && b.A != null && Combat.IsFoe(c.Self, b.A) && !b.Spec.AlwaysWins && b.Clash == null && Fits(c.Self, b.A, level))
            {
                return Stance(c, b.A, 160f + c.Value(b.A, c.Expected(b.A, b.Spec.Mult * Boost(level) * 0.6f)));
            }
            // a foe gathering a beam or a shot aimed at it: it waits for it, palms open
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                if (c.EnemyDist[i] > 20f)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] < 2.5f || !Fits(c.Self, v, level) || !c.Allowed(v))
                {
                    continue;   // (one that close is better broken than waited for)
                }
                FoeState s = c.State(v);
                if (!s.Charging || !s.AimsAtMe || (s.Doing & (Form.Beam | Form.Projectile)) == 0 || (s.Doing & (Form.Ki | Form.Beam)) == 0)
                {
                    continue;
                }
                float score = c.Value(v, c.Expected(v, 2.5f * Boost(level))) + 40f;
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
                }
            }
            return best == null ? null : Stance(c, best, bestScore);
        }

        private static Plan Stance(Ctx c, Actor v, float score)
        {
            Vector2 dir = v.current_position - c.Pos;
            return new Plan
            {
                Target = v,
                Point = v.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = score,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            MirrorMove m = MirrorMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!Fits(defender, attacker, level))
            {
                return Contact.Hit;   // too much for its palms
            }
            MirrorMode mode = MirrorMove.Read(defender, attacker, incoming, out Beam beam, out Shot shot, out LanceHazard lance);
            if (mode == MirrorMode.None)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            MirrorMove m = MirrorMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            m.Set(mode, beam, shot, lance);
            m.Build();
            if (!m.Start())
            {
                return Contact.Hit;
            }
            m.Catch(attacker);
            return Contact.Parried;
        }

        /// <summary>A unit in the waiting stance is reached: ki it can hold is caught; anything else breaks the stance.</summary>
        private static Contact StanceContact(Move m, Actor v, bool area)
        {
            if (m == null || m.A == null || v == null || !Moves.TryGet(v, out Move mv) || !(mv is MirrorMove mm) || !mm.InStance || mm.Mode != MirrorMode.None)
            {
                return Contact.Hit;
            }
            if (area || !Combat.IsFoe(v, m.A) || !Fits(v, m.A, mm.Level))
            {
                mm.Struck = true;
                return Contact.Hit;
            }
            MirrorMode mode = MirrorMove.Read(v, m.A, m, out Beam b, out Shot s, out LanceHazard l);
            if (mode == MirrorMode.None)
            {
                mm.Struck = true;
                return Contact.Hit;
            }
            mm.Set(mode, b, s, l);
            mm.Catch(m.A);
            mm.CounterDepth = Mathf.Max(mm.CounterDepth, m.CounterDepth + 1);
            try
            {
                Hooks.Answered?.Invoke(v, m.A, mm.Tech, m.CounterDepth, Contact.Parried);
            }
            catch (Exception e)
            {
                SakugaMod.Report("mirror stance answered", e);
            }
            if (Dev.On) Dev.Trace("mirror_return stance of " + v.asset.id + "#" + v.Sid() + " catches " + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + " (" + mode + ")");
            return Contact.Parried;
        }

        /// <summary>A blow landed on a unit in the waiting stance: the stance is broken.</summary>
        private static void StanceStruck(Actor a, Actor v, HitResult r, Move by)
        {
            if (v != null && r.Landed && Moves.TryGet(v, out Move mv) && mv is MirrorMove mm && mm.InStance)
            {
                mm.Struck = true;
            }
        }
    }

    internal enum MirrorMode
    {
        None,
        Beam,
        Shot,
        Lance,
        Melee
    }

    /// <summary>
    /// Mirror Return's move: the waiting stance (planned), then whichever catch fits what came (a
    /// beam, a ki shot, a lance, the ki of a palm), each a hold (its palms full of the enemy's
    /// light, driven back skidding) and a return (bigger), then the follow-through. Every branch is
    /// in the script; only the one that fits plays.
    /// </summary>
    internal sealed class MirrorMove : ChoreoMove
    {
        public MirrorMode Mode;
        public bool InStance, Struck;

        private Beam _beam, _returned;
        private Hue _theirs;
        private bool _theirsSet;
        private ShotKind _kind;
        private float _size = 1f, _speed = 18f, _smult = 1f, _spush = 0.8f, _scrater;
        private LanceHazard _lance;
        private float _lmult = 3f;
        private float _bWidth = 6f, _bSpeed = 45f, _bMult = 2f, _bPush = 2f, _bTrench = 0.8f, _bPower = 1f;
        private Particle _mirror, _knot, _star, _held;
        private float _fxT, _grace;
        private bool _broke;

        private MirrorMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static MirrorMove Make(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new MirrorMove(a, t, p);
        }

        /// <summary>An answer: 'defender' meets 'attacker' (its target) with Mirror Return at 'level'.</summary>
        public static MirrorMove Answer(Actor defender, Actor attacker, Technique t, int level)
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
            return new MirrorMove(defender, t, p);
        }

        // ------------------------------------------------------------- reading --

        /// <summary>What of 'attacker's is reaching 'defender' right now that its palms can hold.</summary>
        public static MirrorMode Read(Actor defender, Actor attacker, Move incoming, out Beam beam, out Shot shot, out LanceHazard lance)
        {
            beam = null;
            shot = null;
            lance = null;
            Shot s = Shots.Reaching;
            if (s != null && s.A == attacker && s.Hostile && s.Spec.Reflectable && KiShot(s.Spec.Kind))
            {
                shot = s;
                return MirrorMode.Shot;
            }
            LanceHazard l = LanceHazard.Reaching;
            if (l != null && l.A == attacker)
            {
                lance = l;
                return MirrorMode.Lance;
            }
            Beam b = BeamOn(attacker, defender);
            if (b != null)
            {
                if (b.Spec.AlwaysWins || b.Clash != null)
                {
                    return MirrorMode.None;   // (the beam of beams can't be held; nor one locked in a clash)
                }
                beam = b;
                return MirrorMode.Beam;
            }
            if (s != null || incoming == null)
            {
                return MirrorMode.None;
            }
            Form f = Reads.FormsOf(incoming);
            if ((f & Form.Ki) != 0 && (f & (Form.Beam | Form.Projectile | Form.Area)) == 0 && Combat.Grounded(attacker)
                && Vector2.Distance(attacker.current_position, defender.current_position) <= 2.6f * Choreo.KOf(defender))
            {
                return MirrorMode.Melee;
            }
            return MirrorMode.None;
        }

        private static bool KiShot(ShotKind k)
        {
            return k == ShotKind.Orb || k == ShotKind.Bullet || k == ShotKind.Spear || k == ShotKind.LightSword || k == ShotKind.Disc;
        }

        /// <summary>The firing beam of 'a' whose line covers 'v' (its head already there; 'aimed': anywhere on its way), if any.</summary>
        public static Beam BeamOn(Actor a, Actor v, bool aimed = false)
        {
            if (a == null || v == null)
            {
                return null;
            }
            IReadOnlyList<Beam> all = Beams.All;
            for (int i = 0; i < all.Count; i++)
            {
                Beam b = all[i];
                if (b.A != a || !b.Firing)
                {
                    continue;
                }
                Vector2 rel = v.current_position - b.GroundFrom;
                float along = Vector2.Dot(rel, b.Dir);
                if (along < -0.5f || along > (aimed ? b.MaxLen : b.Len) + 1.5f)
                {
                    continue;
                }
                if (Mathf.Abs(rel.x * b.Dir.y - rel.y * b.Dir.x) <= b.Reach + 0.8f)
                {
                    return b;
                }
            }
            return null;
        }

        /// <summary>What it is about to catch (copied: the original may be gone by the time it is sent back).</summary>
        public void Set(MirrorMode mode, Beam beam, Shot shot, LanceHazard lance)
        {
            Mode = mode;
            _beam = beam;
            _lance = lance;
            if (beam != null)
            {
                _theirs = beam.Hue;
                _theirsSet = true;
                BeamSpec s = beam.Spec;
                _bWidth = s.Width;
                _bSpeed = s.Speed;
                _bMult = s.Mult;
                _bPush = s.Push;
                _bTrench = s.Trench;
                _bPower = s.Power;
            }
            if (shot != null)
            {
                _theirs = shot.Hue;
                _theirsSet = true;
                ShotSpec s = shot.Spec;
                _kind = s.Kind == ShotKind.Bullet ? ShotKind.Orb : s.Kind;
                _size = s.Size;
                _speed = s.Speed;
                _smult = s.Mult;
                _spush = s.Push;
                _scrater = s.Crater;
            }
            if (lance != null)
            {
                _theirs = lance.Hue;
                _theirsSet = true;
                _lmult = lance.Mult;
            }
            if (!_theirsSet && V != null)
            {
                _theirs = Hue.For(V, School.KiArts, Level, Element.Ki);
                _theirsSet = true;
            }
        }

        /// <summary>The moment of the catch: what came is stopped in its palms.</summary>
        public void Catch(Actor attacker)
        {
            if (attacker != null && attacker != V)
            {
                SetVictim(attacker);
            }
            if (_beam != null && _beam.Firing)
            {
                _beam.StopAt(PalmPoint(), 0.3f);
            }
            if (_lance != null)
            {
                _lance.Caught = true;
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Vector2 at = PalmPoint();
                Kit.Flash(at, Px, Mathf.RoundToInt(5f * Kk), 0.08f, Fx.WithA(_theirs.Core, 1f), TopLayer);
                Fx.Ring(at, Px, 0.2f, 2f, 12f * Kk, 2, 1, TopLayer, 6, 1f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(at, Px, 6, 40f, 100f, _theirs.Edge, TopLayer);
            }
            Kit.Stop(PalmPoint(), 0.03f);
            if (Dev.On) Dev.Trace("mirror_return " + Techniques.Roman(Level) + " by " + A.asset.id + "#" + A.Sid() + " catches a " + Mode + " of " + (attacker != null ? attacker.asset.id : "?"));
        }

        /// <summary>Between its two open palms.</summary>
        public Vector2 PalmPoint()
        {
            return BodyPos() + new Vector2(Fwd * Hpx * 0.38f, Hpx * 0.05f) * Px;
        }

        // ------------------------------------------------------------- script --

        public void Build()
        {
            Form guard = Form.Ki | Form.Guard | Form.Counter;
            Form back = Form.Ki | Form.Counter;
            float hold = Level <= 1 ? 0.42f : Level == 2 ? 0.36f : 0.3f;
            // the waiting stance (planned): palms open towards the foe, a mirror of light between them
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.16f,
                To = new Shape(-6f, 1.06f, 0.94f, 0f, -0.02f),
                Ease = Ease.OutQuad,
                Forms = guard,
                If = m => Mode == MirrorMode.None,
                Until = m => Mode != MirrorMode.None || Abandon(),
                MaxDur = 2f,
                Enter = m => EnterStance(),
                Every = (m, k) => TickStance(),
                Exit = m => EndStance()
            });
            // BEAM: held in its palms, driven back skidding...
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = hold,
                To = new Shape(10f, 1.12f, 0.9f, 0f, 0.04f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = guard,
                If = m => Mode == MirrorMode.Beam,
                Enter = m => EnterHold(),
                Every = (m, k) => HoldBeam(k)
            });
            // ...shoved back, bigger
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(24f, 1.28f, 0.84f, 0f, 0.16f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = back | Form.Beam,
                If = m => Mode == MirrorMode.Beam,
                Enter = m => PushBeam()
            });
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.2f,
                To = new Shape(14f, 1.18f, 0.88f, 0f, 0.08f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = back | Form.Beam,
                If = m => Mode == MirrorMode.Beam && _returned != null,
                Until = m => _returned == null || _returned.Done || !_returned.Firing,
                MaxDur = 3f,
                Every = (m, k) => Pour()
            });
            // SHOT: the orb caught in its palms swells...
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.24f,
                To = new Shape(-8f, 1.08f, 0.94f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                Forms = guard,
                If = m => Mode == MirrorMode.Shot,
                Enter = m => EnterHold(),
                Every = (m, k) => HoldShot(k)
            });
            // ...and goes back
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(20f, 1.24f, 0.86f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Forms = back | Form.Projectile,
                If = m => Mode == MirrorMode.Shot,
                Enter = m => ThrowShot()
            });
            // LANCE: caught by its head, turned round in its hands...
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.26f,
                To = new Shape(-10f, 1.06f, 0.96f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Forms = guard,
                If = m => Mode == MirrorMode.Lance,
                Enter = m => EnterHold(),
                Every = (m, k) => HoldLance(k)
            });
            // ...and hurled home
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(26f, 1.26f, 0.85f, 0f, 0.16f),
                Ease = Ease.OutExpo,
                Forms = back | Form.Projectile,
                If = m => Mode == MirrorMode.Lance,
                Enter = m => ThrowLance()
            });
            // MELEE: the ki of its palm caught between its own palms...
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.12f,
                To = new Shape(-10f, 1.08f, 0.94f, 0f, -0.08f),
                Ease = Ease.OutQuad,
                Forms = guard,
                If = m => Mode == MirrorMode.Melee,
                Enter = m => EnterHold(),
                Every = (m, k) => HoldMelee(k)
            });
            // ...and pushed back into it
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(18f, 1.24f, 0.88f, 0f, 0.2f),
                Ease = Ease.OutExpo,
                Forms = back | Form.Palm,
                If = m => Mode == MirrorMode.Melee
            }.On(0.45f, m => PushMelee()));
            // the follow-through of any catch
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.14f,
                To = new Shape(12f, 1.1f, 0.94f, 0f, 0.1f),
                Ease = Ease.OutQuad,
                Track = false,
                If = m => Mode != MirrorMode.None,
                Enter = m => ClearFx()
            });
            Recover(0.26f);
        }

        // -------------------------------------------------------------- stance --

        private void EnterStance()
        {
            InStance = true;
            _grace = 0f;
            Choreo.Sound("event:/SFX/HIT/HitMetal", A.current_tile, 0.2f);
            if (Vis)
            {
                _mirror = Glue(Fx.Spawn(ChoreoArt.Hex(Mathf.Clamp(Mathf.RoundToInt(Hpx * 0.55f), 3, 40), 1), PalmPoint(), Px, 1f, FxLayer.Over, 9));
                Fx.Ring(Pose.Ground, Px, 0.25f, 3f, 12f * Kk, 1, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        private void TickStance()
        {
            _grace += Clock.Dt;
            if (_mirror != null && _mirror.Alive)
            {
                _mirror.Pos = PalmPoint() + new Vector2(Fwd * 1.5f * Px, 0f);
                _mirror.SX = 0.55f;   // (seen edge-on: a thin mirror held out in front)
                _mirror.Grad = null;
                _mirror.R.color = Fx.WithA(Color.Lerp(Hue.Core, Hue.Edge, 0.3f), 0.55f + 0.25f * Mathf.Sin(T * 11f));
            }
            _fxT -= Clock.Dt;
            if (Vis && _fxT <= 0f)
            {
                _fxT = 0.08f;
                ChoreoFx.Motes(PalmPoint(), Px, 2, 8f * Kk, 0.12f, Hue.Core, Hue.Edge, TopLayer);
            }
        }

        private void EndStance()
        {
            InStance = false;
            if (_mirror != null)
            {
                Unglue(_mirror);
                _mirror = null;
            }
        }

        /// <summary>The stance is given up: it was struck, its foe is gone, or nothing is coming any more.</summary>
        private bool Abandon()
        {
            if (Struck || V == null || !Combat.Living(V))
            {
                return true;
            }
            if (_grace < 0.5f)
            {
                return false;
            }
            // still gathering, or its beam (however far its head is yet) or its throw is still coming
            if (Moves.Charging(V) || BeamOn(V, A, true) != null || (Reads.Doing(V) & (Form.Beam | Form.Projectile)) != 0)
            {
                return false;
            }
            Shot s = Shots.Incoming(A, 6f);
            return s == null || s.A != V;
        }

        // --------------------------------------------------------------- holds --

        private void EnterHold()
        {
            InStance = false;
            if (_mirror != null)
            {
                Unglue(_mirror);
                _mirror = null;
            }
            if (Mode == MirrorMode.Beam && _beam != null && _beam.Dir.sqrMagnitude > 0.5f)
            {
                Face(-_beam.Dir);
            }
            else if (V != null && VOk)
            {
                Face(VG - Stance);
            }
            if (!Vis)
            {
                return;
            }
            int r = Mathf.Clamp(Mathf.RoundToInt((Mode == MirrorMode.Shot ? 2f * _size + 1f : 3f) * Mathf.Lerp(1f, Kk, 0.6f)), 1, 30);
            _knot = Glue(Fx.Spawn(Art.Disk(r, 1f, 0.35f), PalmPoint(), Px, 1f, FxLayer.Over, 10));
            _star = Glue(Fx.Spawn(Art.Burst(Mathf.Clamp(r * 2 + 3, 4, 40)), PalmPoint(), Px, 1f, FxLayer.Over, 9));
        }

        /// <summary>The knot of both colours between its palms ('k' how full it is), sparks spraying.</summary>
        private void Knot(float k, float size)
        {
            Vector2 at = PalmPoint();
            bool flick = ((int)(T * 30f) & 1) == 0;
            if (_knot != null && _knot.Alive)
            {
                _knot.Pos = at;
                _knot.Size0 = _knot.Size1 = size * (1f + 0.12f * Mathf.Sin(T * 47f));
                _knot.Grad = null;
                _knot.R.color = Color.Lerp(flick ? _theirs.Core : Hue.Core, Color.white, 0.3f + 0.4f * k);
            }
            if (_star != null && _star.Alive)
            {
                _star.Pos = at;
                _star.Rot += 300f * Clock.Dt;
                _star.Size0 = _star.Size1 = size * (0.7f + 0.3f * k);
                _star.Grad = null;
                _star.R.color = Fx.WithA(flick ? Hue.Edge : _theirs.Edge, 0.75f);
            }
            _fxT -= Clock.Dt;
            if (Vis && _fxT <= 0f)
            {
                _fxT = 0.035f;
                Vector2 side = Choreo.Perp(Dir);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, Px, Random.Range(0.1f, 0.2f), TopLayer, 19);
                if (p != null)
                {
                    Vector2 d = (side * (Random.value < 0.5f ? -1f : 1f) - Dir * Random.Range(0.2f, 0.8f)).normalized;
                    p.Moving(d * Random.Range(60f, 120f) * Px * Kk, Random.Range(0f, 20f) * Px, 5f, 140f * Px);
                    p.FaceVel = true;
                    p.Color(Color.white, flick ? _theirs.Edge : Hue.Edge, Fx.WithA(Hue.Edge, 0f));
                }
            }
        }

        /// <summary>Driven back along the ground (dust at its heels, skid marks), 'speed' tiles a second.</summary>
        private void Skid(Vector2 along, float speed)
        {
            Vector2 was = Stance;
            Vector2 to = Stance + along.normalized * speed * Clock.Dt * Kk;
            if (Combat.SafeSpot(to) || Choreo.Wading(to))
            {
                Stance = to;
            }
            Pose.Nudge = Choreo.Tremble(BeatT, 0.8f);
            if (Vis && Random.value < 0.35f)
            {
                ChoreoFx.HeelDust(Pose.Ground, along, Px, A.current_tile, Kk);
                ChoreoFx.Skid(was, Stance, Px, A.current_tile);
            }
        }

        private void HoldBeam(float k)
        {
            if (_beam != null && _beam.Firing)
            {
                _beam.StopAt(PalmPoint(), 0.15f);
                Skid(_beam.Dir, 0.9f);
            }
            else if (BeatT > 0.06f && BeatT < BeatDur)
            {
                BeatDur = BeatT;   // their beam is spent: send it home now
            }
            Knot(k, 1f + 0.8f * k);
            if (k > 0.3f)
            {
                Ruin.Quake(A, Pose.Ground, Scale.Casual, 0.6f, Kk);
            }
        }

        private void PushBeam()
        {
            Vector2 dir = V != null && VOk ? VG - Stance : _beam != null ? -_beam.Dir : Dir;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Dir;
            float dist = V != null && VOk ? Vector2.Distance(Stance, VG) : 10f;
            if (_beam != null)
            {
                _beam.Lost = true;   // (it lost to the mirror: its own end-of-beam bursts don't go off)
                _beam.Collapse();
            }
            Face(dir);
            float boost = MirrorReturnTech.Boost(Level);
            _returned = Beams.Fire(A, this, Muzzle(), dir, new BeamSpec
            {
                Length = Mathf.Clamp(dist + 3f, 6f, 30f),
                Width = _bWidth * (1.15f + 0.1f * Level),
                Speed = Mathf.Max(40f, _bSpeed) * 1.3f,
                Life = 0.3f + 0.1f * Level,
                Mult = _bMult * boost,
                Push = _bPush + 0.8f,
                Trench = Mathf.Max(0.6f, _bTrench),
                Power = _bPower * boost,
                Clashes = false,
                Element = Element.Ki
            });
            ReturnFx(1.2f);
        }

        /// <summary>Its beam pours back down the line; when it reaches the shooter, the shooter is blasted out of its stance.</summary>
        private void Pour()
        {
            Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            if (_broke || _returned == null || V == null || !Combat.Living(V))
            {
                return;
            }
            float reach = Vector2.Distance(_returned.GroundFrom, V.current_position);
            if (_returned.Len < reach - 0.6f)
            {
                return;
            }
            _broke = true;
            if (Moves.TryGet(V, out Move vm) && vm is MirrorMove)
            {
                return;   // (it holds it in turn: a mirror against a mirror)
            }
            Choreo.Break(V, A, _returned.Dir, 2.5f + _bPush);
        }

        private void HoldShot(float k)
        {
            Skid(-Dir, 0.6f * (1f - k));
            Knot(k, 1f + 0.5f * k);
        }

        private void ThrowShot()
        {
            Vector2 at = PalmPoint();
            ClearFx();
            Vector2 dir = V != null && VOk ? VPoint(0.45f) - at : Dir;
            float dist = V != null && VOk ? Vector2.Distance(Stance, VG) : 8f;
            float boost = MirrorReturnTech.Boost(Level);
            ShotSpec spec = new ShotSpec
            {
                Kind = _kind,
                Flight = Flight.Straight,
                Speed = Mathf.Max(14f, _speed) * 1.25f,
                Range = Mathf.Clamp(dist + 5f, 6f, 24f),
                Size = _size * 1.5f + 0.3f,
                Mult = _smult * boost,
                Hurt = Hurt.Launch,
                Push = _spush + 1.2f,
                Crater = Level >= 3 ? Mathf.Max(0.8f, _scrater) : _scrater,
                Area = Level >= 2 ? 1.2f : 0f,
                AreaMult = 0.5f,
                Element = Element.Ki
            };
            Shots.Fire(A, this, at, dir, V != null && VOk ? V : null, Vector2.zero, spec, Hue.As(Element.Ki));
            ReturnFx(0.9f);
        }

        private void HoldLance(float k)
        {
            Skid(-Dir, 0.8f * (1f - k));
            Knot(k, 1.2f);
            if (!Vis)
            {
                return;
            }
            // the spear turned round in its hands: from pointing at it to pointing home
            float a = Mathf.Lerp(180f, 0f, Beat.Apply(Ease.InOutCubic, k));
            Vector2 d = Choreo.Rotate(Dir, a * (Fwd > 0f ? 1f : -1f));
            Vector2 c = PalmPoint();
            KiHi.Bar(ref _held, c - d * 0.7f * Kk, c + d * 1f * Kk, Px, 1f, Color.Lerp(_theirs.Core, Hue.Core, k), TopLayer, 13);
        }

        private void ThrowLance()
        {
            Vector2 at = PalmPoint();
            ClearFx();
            Vector2 dir = V != null && VOk ? VG - Stance : Dir;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Dir;
            float dist = V != null && VOk ? Vector2.Distance(Stance, VG) : 12f;
            Areas.Add(new LanceHazard(this, at, dir, Mathf.Clamp(dist + 6f, 8f, 30f), _lmult * MirrorReturnTech.Boost(Level), 2f, false));
            Choreo.Sound("event:/SFX/WEAPONS/WeaponStartThrow", A.current_tile);
            ReturnFx(1f);
        }

        private void HoldMelee(float k)
        {
            Skid(-Dir, 0.7f * (1f - k));
            Knot(k, 0.8f + 0.6f * k);
            if (V != null && VOk)
            {
                Combat.Pin(V, 0.4f);
            }
        }

        private void PushMelee()
        {
            if (!VOk)
            {
                Lose();
                return;
            }
            float reach = Stand + 0.7f * Kk + V.stats["size"] * 0.5f + 0.5f;
            if (Vector2.Distance(Pose.Ground, VG) > reach)
            {
                ClearFx();
                Lose();
                return;
            }
            Vector2 at = PalmPoint();
            ClearFx();
            if (!Touch(V))
            {
                return;
            }
            Hit h = new Hit(Blow.Palm, MirrorReturnTech.MeleeMult(Level))
            {
                Hurt = Hurt.Launch,
                Push = 2f + 0.6f * Level,
                Lift = 1f + 0.3f * Level,
                Finisher = true,
                Power = 1.3f
            };
            Deliver(V, h, ShapeOf(Blow.Palm), ContactPoint(0.55f), Dir);
            if (Vis)
            {
                Kit.Flash(at, Px, Mathf.RoundToInt(6f * Kk), 0.08f, Fx.WithA(_theirs.Core, 1f), TopLayer);
                Fx.Ring(at, Px, 0.22f, 2f, 14f * Kk, 2, 1, TopLayer, 6, 0.6f, 2.4f, Fx.WithA(Color.white, 1f), Fx.WithA(_theirs.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        /// <summary>The return leaves its palms: a flash, a ring, (II+) an impact frame, a jolt.</summary>
        private void ReturnFx(float weight)
        {
            Vector2 at = Muzzle();
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
            Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.Flash(at, Px, Mathf.RoundToInt(6f * Kk * weight), 0.08f, Color.white, TopLayer);
                Fx.Ring(at, Px, 0.25f, 3f, 18f * Kk * weight, 2, 1, TopLayer, 6, 1.4f, 2.4f, Fx.WithA(Hue.Core, 1f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 10, 40f, 0.5f, Kk);
                if (Level >= 2)
                {
                    Kit.ImpactFrame(at, Px, 0.5f * Kk * weight, Hue.Pal, false, FxLayer.Behind);
                }
            }
            Kit.Stop(at, 0.04f + 0.015f * Level);
            Ruin.Quake(A, Pose.Ground, Level >= 2 ? Scale.Heavy : Scale.Light, weight, Kk);
        }

        private void ClearFx()
        {
            if (_knot != null)
            {
                Unglue(_knot);
                _knot = null;
            }
            if (_star != null)
            {
                Unglue(_star);
                _star = null;
            }
            KiHi.Kill(ref _held);
        }

        public override void OnEnd(bool aborted)
        {
            InStance = false;
            KiHi.Kill(ref _held);
            base.OnEnd(aborted);
        }
    }

    /// <summary>
    /// Dev staging for the Ki Arts beams and the mirror (MirrorReturn.cs, Dev only):
    ///   kiduel techA techB [tierA] [tierB] [dist] [levelA] [levelB] [raceA] [raceB] [zoom]
    /// Two Ki Arts fighters 'dist' tiles apart on a clean meadow, held a moment; then A uses techA on
    /// B and B uses techB on A a beat later (two beams: a clash). techB "mirror" makes B answer A's
    /// attack with Mirror Return (it always reads it); "stance" makes B open its waiting stance just
    /// before A attacks; "none" leaves B to stand there. Logs "kiduel done" after 6 s.
    /// </summary>
    internal static class MirrorDev
    {
        private static Actor _a, _b;
        private static string _ta, _tb;
        private static int _la, _lb;
        private static float _t = -1f;
        private static bool _wentA, _wentB;
        private static readonly object _scene = new object();
        private static readonly List<long> _staged = new List<long>();

        public static void Register()
        {
            Dev.Commands["kiduel"] = Command;
            // kischool: every tiered demo unit (a 'war', a 'battle') walks Ki Arts too, so the AI fights with its techniques
            Dev.Commands["kischool"] = args =>
            {
                ActorTrait s = Schools.Trait(School.KiArts);
                int n = 0;
                foreach (Actor u in Dev.Units)
                {
                    if (s != null && u != null && u.isAlive() && Tiers.Of(u) != Tier.None)
                    {
                        u.addTrait(s, true);
                        n++;
                    }
                }
                Dev.Log("kischool: " + n + " fighters walk Ki Arts");
            };
            Hooks.FrameTick += dt =>
            {
                if (_t < 0f)
                {
                    return;
                }
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    SakugaMod.Report("kiduel", e);
                    End();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _ta = Dev.Arg(args, 1, "dragon_wave");
            _tb = Dev.Arg(args, 2, "dragon_wave");
            Tier ta = Dev.ParseTier(Dev.Arg(args, 3, "grandmaster"));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 4, "grandmaster"));
            float dist = Dev.ArgF(args, 5, 10f);
            _la = (int)Dev.ArgF(args, 6, 3f);
            _lb = (int)Dev.ArgF(args, 7, 3f);
            string ra = Dev.Arg(args, 8, "human"), rb = Dev.Arg(args, 9, "orc");
            float zoom = Dev.ArgF(args, 10, 16f);
            if (Techniques.Get(_ta) == null)
            {
                Dev.Log("kiduel: no technique " + _ta);
                return;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), Mathf.CeilToInt(dist * 0.5f) + 6);
            if (c == null)
            {
                Dev.Log("kiduel: no ground");
                return;
            }
            int half = Mathf.RoundToInt(dist * 0.5f);
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - half, c.y) ?? c, true);
            _b = Dev.Spawn(rb, World.world.GetTile(c.x + half, c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("kiduel: could not spawn");
                return;
            }
            Progression.Promote(_a, ta == Tier.None ? Tier.Grandmaster : ta, false);
            Progression.Promote(_b, tb == Tier.None ? Tier.Grandmaster : tb, false);
            foreach (Actor u in new[] { _a, _b })
            {
                if (u.data != null)
                {
                    u.data.favorite = false;
                }
                ActorTrait s = Schools.Trait(School.KiArts);
                if (s != null)
                {
                    u.addTrait(s, true);
                }
                _staged.Add(u.Sid());
            }
            Director.Claim(_scene, 9f, _a, _b);
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _wentA = _wentB = false;
            Dev.Log("kiduel: " + _ta + " " + Techniques.Roman(_la) + " (" + Tiers.Of(_a) + " " + ra + " #" + _a.Sid() + ") vs " + _tb + " " + Techniques.Roman(_lb)
                + " (" + Tiers.Of(_b) + " " + rb + " #" + _b.Sid() + "), " + dist + " tiles");
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime * Clock.Scale;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > 1.5f)
                {
                    Dev.Log("kiduel done (one fell)");
                    End();
                }
                return;
            }
            bool stance = _tb == "stance";
            bool mirror = _tb == "mirror";
            if (stance && !_wentB && _t >= 1.1f)
            {
                Ready(_b);
                Moves.ReleaseHeld();
                Moves.Hold(_a);
                Technique m = Techniques.Get("mirror_return");
                bool ok = Brain.Force(_b, m, Mathf.Max(1, _lb), _a);
                if (ok || _t >= 2f)
                {
                    _wentB = true;
                    Dev.Log("kiduel: B opens its mirror stance: " + ok);
                }
            }
            if (!_wentA && _t >= 1.2f && (!stance || _wentB))
            {
                Ready(_a);
                Ready(_b);
                if (mirror)
                {
                    Reactions.DevForce[_b.Sid()] = "mirror_return";
                }
                Moves.ReleaseHeld();
                if (mirror || _tb == "none" || stance)
                {
                    Moves.Hold(_b);   // the defender stands its ground until it answers
                }
                bool ok = Brain.Force(_a, Techniques.Get(_ta), Mathf.Max(1, _la), _b);
                // (a fresh unit may still be settling: it tries again for a second)
                if (ok || _t >= 2.2f)
                {
                    _wentA = true;
                    Dev.Log("kiduel: A uses " + _ta + ": " + ok + (ok ? "" : " (can perform " + Moves.CanPerform(_a) + ", move "
                        + (Moves.TryGet(_a, out Move am) ? am.GetType().Name : "-") + ", h " + _a.position_height.ToString("F2") + ", forces " + _a.under_forces
                        + ", foes " + Combat.IsFoe(_a, _b) + ", stunned " + _a.hasStatusStunned() + ")"));
                }
            }
            if (!_wentB && _wentA && !stance && !mirror && _tb != "none" && _t >= 1.32f)
            {
                Technique tb = Techniques.Get(_tb);
                bool ok = tb != null && Brain.Force(_b, tb, Mathf.Max(1, _lb), _a);
                if (ok || tb == null || _t >= 2.4f)
                {
                    _wentB = true;
                    Dev.Log("kiduel: B uses " + _tb + ": " + ok);
                }
            }
            if (_t >= 7f)
            {
                Dev.Log("kiduel done");
                End();
            }
        }

        private static void Ready(Actor u)
        {
            Fighter f = Fighters.Get(u);
            f.ResetCooldowns();
            u.setMana(u.getMaxMana());
        }

        private static void End()
        {
            if (_a != null)
            {
                Reactions.DevForce.Remove(_a.Sid());
            }
            if (_b != null)
            {
                Reactions.DevForce.Remove(_b.Sid());
            }
            _staged.Clear();
            Director.Release(_scene);
            _t = -1f;
            _a = _b = null;
            Moves.ReleaseHeld();
        }
    }

    /// <summary>Small drawing helpers the Ki Arts master techniques share (held sprites steered every frame).</summary>
    internal static class KiHi
    {
        /// <summary>A bar of light from 'a' to 'b', 'thick' pixels thick (one stretched pixel, held until killed).</summary>
        public static Particle Bar(ref Particle p, Vector2 a, Vector2 b, float px, float thick, Color c, FxLayer layer, int order)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(Art.Pixel, (a + b) * 0.5f, px, 1f, layer, order);
                if (p == null)
                {
                    return null;
                }
                p.Held = true;
            }
            p.Pos = (a + b) * 0.5f;
            p.SX = Mathf.Max(0.5f, len / px);
            p.SY = Mathf.Max(1f, Mathf.Round(thick));
            p.Rot = len > 1e-5f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;
            p.Grad = null;
            p.R.color = c;
            p.Cine = Cinema.Playing;
            return p;
        }

        /// <summary>A round glow 'r' pixels in radius at 'at' (held until killed).</summary>
        public static Particle Blob(ref Particle p, Vector2 at, float px, float r, Color c, FxLayer layer, int order, float soft = 0.3f)
        {
            Sprite s = Art.Disk(Mathf.Clamp(Mathf.RoundToInt(r), 1, 140), 1f, soft);
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(s, at, px, 1f, layer, order);
                if (p == null)
                {
                    return null;
                }
                p.Held = true;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = at;
            p.Grad = null;
            p.R.color = c;
            p.Cine = Cinema.Playing;
            return p;
        }

        public static void Kill(ref Particle p)
        {
            if (p != null && p.Alive)
            {
                p.Kill();
            }
            p = null;
        }
    }
}
