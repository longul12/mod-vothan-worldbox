using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HUNDRED FISTS. The unit plants its feet in front of an enemy and buries it under a storm
    /// of blows too fast to count, driving it back across the ground, then ends it with one
    /// clean finisher.
    ///
    /// Beat sheet (level II, game seconds at x1):
    ///
    ///   PLANT    0.10  squash onto the front foot, a dust puff. The first blow is the contact:
    ///                  the enemy may parry it (the flurry breaks and the unit staggers back) or
    ///                  evade it (the unit punches an afterimage and stumbles)
    ///   FLURRY   0.84  14 blows (x0.16, never lethal). The unit jitters a pixel every 0.03 s; a
    ///                  white 3-px spark pops on a random pixel of the enemy per blow; 4 fist-blur
    ///                  speed lines (tier tint) refresh every 0.06 s; a tinted ghost thrusts out on
    ///                  every blow; the enemy recoils on every blow and shakes and flashes on every
    ///                  3rd; both slide back 0.1 tile per blow (1.4 tiles) leaving a dust trail
    ///   WINDUP   0.04  the arm comes back
    ///   STRIKE   0.10  the lunge (x0.8): a small white pop, a tier ring racing out, a big hit
    ///                  star, focus lines, a 0.06 s local hit-stop, then the enemy is launched 2
    ///                  tiles, tumbling (and it can't swing back for half a second)
    ///   RECOVER  0.22  the follow-through springs back
    ///
    /// Level I: 6 blows in 0.36 s, everything white, then a palm shove (no hit-stop).
    /// Level III: 30 blows (x0.1) in 1.5 s with tier-tinted sparks, two flickering clones of the
    /// unit ("many arms"), a ring pulse at 0.8 s that blows back anyone crowding the pair; the arm
    /// draws back with a glint, then (x1.9) an impact frame and a global hit-stop: the enemy
    /// becomes a projectile trailing speed lines and afterimages, flies up to 6 tiles (a Master
    /// 7, a Grandmaster 8, a God 9.6) and bowls over (x1, knocked down) everyone it passes. With
    /// nobody on the line and no wall to hit, now and then (more often when the unit has an air
    /// follow-up ready) it is a rising fist instead, the enemy sent twice as high as a II uppercut.
    /// Totals: I x1.67, II x3.04, III x4.9 (+ the bowling), all times the tier's Might.
    ///
    /// The world takes the blows too (Kit.Ravage, on the impact frame): II scorches the ground
    /// under the finisher; III blasts a crater there (two tiles out), the projectile ploughs a
    /// trench along its whole line and craters where it comes down (or into the wall it hits).
    /// From Master the blasts dig deeper and their rims catch fire; a God's are wider and deeper
    /// still. Level I leaves the ground alone. The unit's own tile is only ever scorched.
    ///
    /// Its pace follows the tier (TierInfo.Linger): a God's flurry is about a quarter shorter, its
    /// recover, parried reel and whiffed stumble under half as long, and Masters and up leave the
    /// game's attack cooldown alone (their ordinary swings are flow strikes). Higher tiers throw
    /// harder and further (the cannonball, the bodies it bowls over, the III uppercut). Every
    /// ending leaves a chain: Close, Down, Launched (an uppercut, when the unit has a follow-up
    /// for an airborne enemy ready; a low skimming throw mostly leaves Down for the ground
    /// follow-ups), or Scattered when the projectile bowled two or more enemies over.
    ///
    /// The enemy is caught even mid-hop (an archer backstepping out of reach, a dodge): a target
    /// that slipped back gets one quick pursuit step, and is brought back down to the ground. One
    /// lying stunned is taken as it lies and hauled up by the first blows (no snap upright). One
    /// in the middle of a flow strike or a blur dash is taken out of it.
    /// A finisher that kills still throws the body: the blow leaves it at 1 HP, it sails along
    /// the line (spinning, landing on its back), and it dies where it lands.
    ///
    /// Circumstances change it:
    ///   - a bigger enemy takes a body blow that slides it a tile and staggers it instead of flying;
    ///   - a small one, or one pinned against a wall or the water during the flurry, is uppercut
    ///     into the air; critters fly comically far;
    ///   - at III a wall on the line means the enemy is slammed into it (a glowing crater);
    ///     a heavy unit on the line stops the projectile dead;
    ///   - an armed unit stabs (thin line sparks and blade thrusts), a bare-handed one punches
    ///     (popping sparks and hit stars);
    ///   - a Novice's rhythm stumbles, a God's is a metronome;
    ///   - a heavy enemy that can't be moved is hit where it stands and only staggers;
    ///   - tiered enemies flash their own tier colour on the finisher.
    /// If the enemy is taken mid-flurry (killed by someone else, picked up by the god hand) the
    /// unit turns on another enemy of the same class within 2 tiles, or ends in a pose with a glint.
    /// </summary>
    internal sealed class HundredFists : Move
    {
        /// <summary>How far (tiles) a forced Hundred Fists may step in to reach its target.</summary>
        public const float StepRange = 3.5f;

        /// <summary>A unit this high (world units) and not flying is only hopping (a backstep, a dodge): it can still be caught.</summary>
        internal const float HopMax = 0.9f;

        private enum Phase
        {
            StepIn,
            Plant,
            Flurry,
            Windup,
            Strike,
            Recover,
            Broken,
            Whiff
        }

        private enum Finish
        {
            Shove,
            Launch,
            Uppercut,
            Body,
            Projectile,
            Stagger
        }

        private const float TBroken = 0.38f;
        private const float TWhiff = 0.32f;
        private const float TBody = 0.24f;
        private const float TCrumple = 0.16f;

        private const string SndPunch = "event:/SFX/OTHER/Punch";
        private const string SndHit = "event:/SFX/HIT/HitGeneric";
        private const string SndWhoosh = "event:/SFX/UNIQUE/SpawnWhoosh";

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Steel = new Color(0.86f, 0.92f, 1f, 1f);

        // ---- per level
        private readonly int _hits;
        private readonly float _tPlant, _tFlurry, _tWindup, _tStrike, _tRecover;
        private readonly float _mult, _finMult, _slide;
        private readonly float[] _times;
        private readonly Palette _pal;
        private readonly bool _armed;
        private readonly float _k, _lk, _ah;

        // ---- the tier: tempo (dead time, throws) and world damage
        private readonly Tier _tier;
        private readonly float _snap, _tBroken, _tWhiff, _quick;

        // ---- the enemy
        private Actor _v;
        private long _vid;
        private Weight _class;
        private float _cm = 1f;
        private Puppet _p;
        private bool _released, _vGone;
        private Vector2 _vGround;
        private float _vpx, _vPivot, _stand;
        private bool _big, _small, _critter;

        private Vector2 _dir;
        private float _fwd;
        private bool _pinned;
        private int _retargets, _chases;

        /// <summary>A downed enemy is hauled up by the first blows: 0 (as it lay) .. 1 (in the barrage).</summary>
        private float _rise = 1f;
        private float _fallRot;
        private Vector2 _fallNudge;
        private const float TRise = 0.16f;

        /// <summary>The plan saw enemies lined up behind the target (the III cannonball bowls them).</summary>
        private bool _lined;

        // ---- the clock
        private Phase _phase;
        private Phase _afterStep = Phase.Plant;
        private float _pt, _ft, _hold;
        private bool _entered;
        private Vector2 _from, _to, _lungeTo;
        private float _tStep, _rotFrom;
        private bool _lungeOk, _stepLong;

        // ---- the flurry
        private int _hit;
        private float _punch, _recoil, _shake, _jx = 1f;
        private int _arm;
        private float _jitterT, _ghostT, _lineT, _dustT, _focusT, _lastSound = -1f;
        private bool _ring, _glinted, _glintEnd, _dustSide;

        // ---- the finisher
        private Finish _finish;
        private bool _vDone, _vStarted;
        private float _vt;
        private Vector2 _vFrom, _vTo;
        private float _flyDur, _flyH, _crumple = -1f;
        private bool _flyWall;
        private Vector2 _lastBody;
        /// <summary>How far along its line the projectile has ploughed the ground (tiles).</summary>
        private float _plough;
        private readonly HashSet<long> _bowled = new HashSet<long>();
        /// <summary>Enemies the cannonball actually knocked over.</summary>
        private int _knocked;
        /// <summary>The finisher's flash on the enemy (when, how long, what colour): what is left of it goes on with a body let go to fly.</summary>
        private float _flashAt, _flashLen;
        private Color _flashTint = Color.white;

        // a finisher that kills: the body is thrown first (at 1 HP) and dies where it lands
        private bool _owed, _arc, _landed;
        private float _arcDist, _arcApex, _arcH0, _arcRot0, _arcRot1;
        private int _arcTurns;

        // a launch waits a tick for the released puppet to be handed back to the game
        private bool _launch;
        private Vector2 _launchDir;
        private float _launchDist, _launchApex, _launchSpin;
        private int _launchTries;

        private static Sprite[] _pop;

        /// <summary>Flurries started since the game started (dev log).</summary>
        internal static int Started;

        private HundredFists(Actor a, Actor v, int level, float cm) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            Tier tier = Tiers.Of(a);
            _pal = Palette.For(tier, Level);
            _armed = Armed(a);
            _k = K;
            _lk = Mathf.Lerp(1f, _k, 0.5f);
            Sprite sa = Fx.UnitSprite(a);
            _ah = Mathf.Max(0.2f, (sa != null ? sa.rect.height : 10f) * a.actor_scale);
            switch (Level)
            {
                case 1:
                    _hits = 6;
                    _tPlant = 0.07f;
                    _tFlurry = 0.36f;
                    _tWindup = 0.03f;
                    _tStrike = 0.1f;
                    _tRecover = 0.2f;
                    _mult = HundredFistsTech.Blow(1);
                    _finMult = HundredFistsTech.Finisher(1);
                    _slide = 0.05f;
                    break;
                case 2:
                    _hits = 14;
                    _tPlant = 0.1f;
                    _tFlurry = 0.84f;
                    _tWindup = 0.04f;
                    _tStrike = 0.1f;
                    _tRecover = 0.22f;
                    _mult = HundredFistsTech.Blow(2);
                    _finMult = HundredFistsTech.Finisher(2);
                    _slide = 0.1f;
                    break;
                default:
                    _hits = 30;
                    _tPlant = 0.12f;
                    _tFlurry = 1.5f;
                    _tWindup = 0.1f;
                    _tStrike = 0.12f;
                    _tRecover = 0.22f;
                    _mult = HundredFistsTech.Blow(3);
                    _finMult = HundredFistsTech.Finisher(3);
                    _slide = 0.045f;
                    break;
            }
            // Masters and up: faster hands, harder throws, and no dead time after
            _tier = tier;
            _snap = SingleKit.Snap(tier);
            _tFlurry *= Mathf.Lerp(1f, _snap, 0.4f);
            _tRecover *= _snap;
            _tBroken = TBroken * _snap;
            _tWhiff = TWhiff * _snap;
            _quick = Mathf.Lerp(1f, _snap, 0.5f);
            _times = Rhythm(_hits, _tFlurry, tier);
            SetVictim(v, cm);
            _phase = Vector2.Distance(Pose.Ground, StandSpot()) > 0.3f ? Phase.StepIn : Phase.Plant;
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>'lined': the plan saw enemies lined up behind 'v' (at III the cannonball is the answer: it bowls them).</summary>
        public static bool Begin(Actor a, Actor v, int level, Technique tech, bool lined = false)
        {
            if (v == null || !Moves.CanPerform(a) || !Combat.IsFoe(a, v) || Moves.Locked(v) || !Hopping(v))
            {
                return false;
            }
            if (!Combat.Allowed(a, v, out float cm))
            {
                return false;
            }
            if (Vector2.Distance(a.current_position, v.current_position) > StepRange + v.stats["size"] + 0.5f)
            {
                return false;
            }
            HundredFists m = new HundredFists(a, v, level, cm) { Tech = tech, _lined = lined };
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(a.current_position, v.current_position, 0.5f) + new Vector2(0f, 1.2f * m._k));
            return true;
        }

        private static bool Armed(Actor a)
        {
            try
            {
                return a.hasWeapon();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>On the ground, or only hopping off it (a backstep, a dodge): not flying, not in a boat.</summary>
        internal static bool Hopping(Actor v)
        {
            return v != null && !v.isFlying() && !v.asset.is_boat && v.position_height <= HopMax;
        }

        /// <summary>
        /// Can 'by' take hold of this unit? Combat.CanPuppet, except that a unit caught in a little
        /// hop (an archer's backstep, a dodge, a stumble) can be caught too: Take brings it back down.
        /// </summary>
        internal static bool Catchable(Actor v, Actor by)
        {
            if (!Hopping(v))
            {
                return false;
            }
            if (v.position_height <= 0.05f)
            {
                return Combat.CanPuppet(v, by);
            }
            if (v == by || !v.isAlive() || Moves.Locked(v) || v.isInsideSomething() || v.isInMagnet() || v.is_immovable)
            {
                return false;
            }
            ActorAsset s = v.asset;
            if (!s.can_be_moved_by_powers || !s.has_sprite_renderer || s.has_avatar_prefab || s.ignore_generic_render)
            {
                return false;
            }
            if (Weights.Heavy(v) || ControllableUnit.isControllingUnit(v))
            {
                return false;
            }
            return v.actor_scale <= Mathf.Max(0.3f, by.actor_scale * 2.2f);
        }

        /// <summary>
        /// Takes hold of a (Catchable) unit as a puppet, keeping the height it was caught at (no pop):
        /// the owner eases it down. Null if it could not be taken.
        /// </summary>
        internal static Puppet Take(Actor v, bool faceRight, Move owner)
        {
            float h = Mathf.Clamp(v.position_height, 0f, HopMax);
            Puppet p = new Puppet(v, faceRight, owner);
            p.Pose.Height = h;
            if (Fallen(v, p, out float rot, out Vector2 nudge))
            {
                p.Pose.Rot = rot;
                p.Pose.Nudge = nudge;
            }
            return Moves.Start(p) ? p : null;
        }

        /// <summary>
        /// A unit knocked flat (stunned, out cold) or caught mid-tumble is drawn by the game turned
        /// about its feet. This is the pose that draws it exactly so: the same angle on screen in
        /// the puppet's facing, and a nudge (sprite pixels) that cancels the pose's turn about the
        /// middle of the body. Taken with it, a downed enemy doesn't snap upright in one frame (in a
        /// duel a Hundred Fists on a stunned orc stood it up between two frames); the owner eases it
        /// up (or keeps it down). False, and zeros, if it is standing.
        /// </summary>
        internal static bool Fallen(Actor v, Puppet p, out float rot, out Vector2 nudge)
        {
            rot = 0f;
            nudge = Vector2.zero;
            if (v == null || p == null)
            {
                return false;
            }
            float z = Mathf.DeltaAngle(0f, v.current_rotation.z);
            if (Mathf.Abs(z) < 12f)
            {
                return false;
            }
            bool canFlip = v.asset.can_flip;
            bool mirrored = canFlip && Mathf.Abs(Mathf.DeltaAngle(v.current_rotation.y, 180f)) < 1f;
            float phi = mirrored ? -z : z;   // on screen, counter-clockwise
            rot = p.Pose.FaceRight && canFlip ? -phi : phi;
            float r = phi * Mathf.Deg2Rad;
            nudge = new Vector2(-Mathf.Sin(r), Mathf.Cos(r) - 1f) * p.Pose.PivotUp;
            return true;
        }

        /// <summary>
        /// When each blow lands (seconds into the flurry). A Novice's rhythm stumbles (blows bunch up
        /// and stall), a God's is perfectly even. The first blow is the contact itself.
        /// </summary>
        private static float[] Rhythm(int n, float window, Tier tier)
        {
            float amp;
            switch (tier)
            {
                case Tier.Novice: amp = 0.42f; break;
                case Tier.Adept: amp = 0.3f; break;
                case Tier.Expert: amp = 0.18f; break;
                case Tier.Master: amp = 0.1f; break;
                case Tier.Grandmaster: amp = 0.05f; break;
                case Tier.God: amp = 0f; break;
                default: amp = 0.3f; break;
            }
            float gap = window / n;
            float[] t = new float[n];
            float prev = 0f;
            for (int i = 1; i < n; i++)
            {
                float x = i * gap + Random.Range(-amp, amp) * gap;
                x = Mathf.Clamp(x, prev + gap * 0.3f, window - gap * 0.3f);
                t[i] = x;
                prev = x;
            }
            return t;
        }

        private void SetVictim(Actor v, float cm)
        {
            _v = v;
            _vid = v.Sid();
            _class = Weights.Of(v);
            _cm = cm;
            _p = null;
            _released = false;
            _vGone = false;
            _pinned = false;
            _rise = 1f;
            _vGround = v.current_position;
            _vpx = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
            Sprite s = Fx.UnitSprite(v);
            float h = s != null ? s.rect.height : 10f;
            _vPivot = Mathf.Clamp(h * 0.36f, 1.5f, 40f);
            float ratio = Mathf.Max(0.2f, h * v.actor_scale) / _ah;
            Weight species = Weights.Species(v);
            _critter = species == Weight.Critter;
            _big = ratio > 1.35f || species >= Weight.Brute && species > Weights.Species(A);
            _small = !_big && (_critter || ratio < 0.75f);
            // a fist's length apart: close enough to touch, far enough that the two bodies (and the
            // blur of blows between them) read as two, not one blob
            _stand = 0.58f * _k + 0.3f * Mathf.Max(0f, ratio - 1f) * _k;
            Aim();
        }

        /// <summary>Turn to the enemy (until we have hold of it the line of the slide follows it).</summary>
        private void Aim()
        {
            Vector2 d = _vGround - Pose.Ground;
            if (d.sqrMagnitude > 0.0025f)
            {
                _dir = d.normalized;
            }
            else if (_dir == Vector2.zero)
            {
                _dir = new Vector2(A.flip ? 1f : -1f, 0f);
            }
            if (Mathf.Abs(_dir.x) > 0.12f)
            {
                _fwd = Mathf.Sign(_dir.x);
            }
            else if (_fwd == 0f)
            {
                _fwd = A.flip ? 1f : -1f;
            }
            Pose.FaceRight = _fwd > 0f;
        }

        private bool Held => _p != null && !_released && !_p.Finished;

        /// <summary>The enemy is being thrown around by someone else's move.</summary>
        private bool HeldByOther()
        {
            return Moves.TryGet(_v, out Move m) && m is Puppet && m != _p;
        }

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        /// <summary>Where to stand: right in the enemy's face (or where we are, if that spot is bad ground).</summary>
        private Vector2 StandSpot()
        {
            Vector2 s = Combat.Clamp(_vGround - _dir * _stand);
            return Combat.SafeSpot(s) ? s : Pose.Ground;
        }

        /// <summary>The enemy's body (where blows land).</summary>
        private Vector2 VBody()
        {
            if (Held)
            {
                return _p.BodyPos();
            }
            if (_v == null || _v.Sid() != _vid)
            {
                return BodyPos() + _dir * _stand;
            }
            return new Vector2(_v.current_position.x, _v.current_position.y + _v.position_height + _vPivot * _v.actor_scale);
        }

        private Vector2 Hand()
        {
            return BodyPos() + new Vector2(_fwd * 2.5f, -0.5f) * Px;
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("fists " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3") + " hit " + _hit + "/" + _hits);
            _phase = p;
            _pt = 0f;
            _entered = false;
        }

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        /// <summary>The force of the world damage a blow does: 'lo' up to Expert, 'hi' from Master, one more for a God.</summary>
        private int Force(int lo, int hi)
        {
            return SingleKit.Force(_tier, lo, hi);
        }

        /// <summary>Real damage to the world at 'at' (a God's is half as wide again), never under the unit's own feet.</summary>
        private void Ruin(Vector2 at, int force)
        {
            SingleKit.Ruin(A, Pose.Ground, at, SingleKit.Radius(_tier, _lk), force);
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            CheckVictim();
            if (Finished)
            {
                return;
            }
            if (_hold > 0f)
            {
                // hit-stop: both hold the pose, trembling with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                if (Held)
                {
                    _p.Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? -0.5f : 0.5f, 0f);
                }
                return;
            }
            if (_launch)
            {
                DoLaunch();
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.StepIn:
                    StepIn(dt);
                    break;
                case Phase.Plant:
                    Plant(dt);
                    break;
                case Phase.Flurry:
                    Flurry(dt);
                    break;
                case Phase.Windup:
                    Windup(dt);
                    break;
                case Phase.Strike:
                    Strike(dt);
                    break;
                case Phase.Broken:
                    Broken(dt);
                    break;
                case Phase.Whiff:
                    Whiff(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
        }

        /// <summary>
        /// Is the enemy still ours? Killed by someone else, picked up by the god hand, recycled, or
        /// gone into a building: let go, and turn on another enemy or end in a pose.
        /// </summary>
        private void CheckVictim()
        {
            if (_vGone || _v == null)
            {
                return;
            }
            bool same = _v.Sid() == _vid && _v.isAlive();
            bool held = _p != null && !_released;
            bool gone = !same || (held ? _p.Finished : _v.isInMagnet() || _v.isInsideSomething());
            if (!gone)
            {
                return;
            }
            _vGone = true;
            _launch = false;
            if (held && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            _released = true;
            if (EndTarget == _v)
            {
                EndLink = Link.None;
                EndTarget = null;
            }
            switch (_phase)
            {
                case Phase.StepIn:
                case Phase.Plant:
                case Phase.Flurry:
                    if (!Retarget())
                    {
                        Lose();
                    }
                    break;
                case Phase.Windup:
                    Lose();
                    break;
                case Phase.Strike:
                    _vDone = true;
                    break;
            }
        }

        /// <summary>The enemy is gone mid-flurry: another one of the same class within 2 tiles takes its place.</summary>
        private bool Retarget()
        {
            if (_retargets >= 2 || _hits - _hit < Mathf.Max(3, _hits / 4))
            {
                return false;
            }
            Actor best = null;
            float bestD = 3f;
            foreach (Actor u in Combat.FoesAround(A, _vGround, 2f))
            {
                if (u == _v || Weights.Of(u) != _class || !Combat.Allowed(A, u) || !Catchable(u, A))
                {
                    continue;
                }
                float d = Vector2.Distance(Pose.Ground, u.current_position);
                if (d < bestD && Combat.ClearPath(Pose.Ground, u.current_position))
                {
                    bestD = d;
                    best = u;
                }
            }
            if (best == null)
            {
                return false;
            }
            _retargets++;
            Combat.Allowed(A, best, out float cm);
            SetVictim(best, cm);
            _afterStep = Phase.Flurry;
            if (Dev.On) Dev.Trace("fists " + Id + " retargets " + best.asset.id + "#" + best.Sid());
            Next(Phase.StepIn);
            return true;
        }

        /// <summary>Nothing left to hit: end in a pose, with a glint.</summary>
        private void Lose()
        {
            _glintEnd = true;
            LetGo(true);
            Next(Phase.Recover);
        }

        // ------------------------------------------------------------ step in --

        private void StepIn(float dt)
        {
            if (!Held && !_vGone)
            {
                _vGround = _v.current_position;
                Aim();
                // it doesn't get a free swing while we close in (it can still answer the first blow)
                Combat.Pin(_v, 0.3f);
            }
            Vector2 to = StandSpot();
            if (Enter())
            {
                _from = Pose.Ground;
                float d0 = Vector2.Distance(_from, to);
                _tStep = Mathf.Clamp(0.06f + 0.035f * d0, 0.08f, 0.24f);
                _stepLong = d0 > 0.35f;
                _ghostT = 0f;
                if (Visible && d0 > 0.6f)
                {
                    Fx.LandingPuff(_from, Px, A.current_tile);
                }
                if (d0 > 1.6f)
                {
                    Anim.Sound(SndWhoosh, A.current_tile);
                }
            }
            float k = Mathf.Clamp01(_pt / _tStep);
            Pose.Ground = Vector2.Lerp(_from, to, Anim.OutQuad(k));
            float lean = Mathf.Sin(Mathf.PI * k);
            Pose.Height = 0f;
            Pose.Rot = 14f * lean;
            Pose.SX = 1f + 0.12f * lean;
            Pose.SY = 1f - 0.1f * lean;
            Pose.Nudge = Vector2.zero;
            if (Visible && _stepLong && k < 0.9f)
            {
                // a few spaced smears, not a stack: at one every 0.022 s (alpha 0.7) six of them
                // overlapped into an opaque white blob trailing the unit
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = Level <= 1 ? 0.045f : 0.035f;
                    Fx.Afterimage(this, 0.11f, Level <= 1 ? new Color(1f, 1f, 1f, 0.42f) : Fx.WithA(_pal.Ghost, 0.55f), _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                if (_afterStep == Phase.Flurry)
                {
                    Engage();
                }
                else
                {
                    Next(Phase.Plant);
                }
            }
        }

        // -------------------------------------------------------------- plant --

        private void Plant(float dt)
        {
            float px = Px;
            if (!Held && !_vGone)
            {
                _vGround = _v.current_position;
                Aim();
                Combat.Pin(_v, 0.3f);
            }
            if (Enter() && Visible)
            {
                Fx.LandingPuff(Pose.Ground + _dir * 1.5f * px, px, A.current_tile);
                if (Level >= 3)
                {
                    Kit.Flare(Pose.Ground, px, _pal.Edge, 12f * _k, 0.3f);
                    Kit.DustRing(Pose.Ground, px, A.current_tile, 8, 36f, 0.45f, _k);
                }
            }
            float k = Mathf.Clamp01(_pt / _tPlant);
            float c = Anim.OutQuad(k);
            Pose.Ground = Vector2.Lerp(Pose.Ground, StandSpot(), Mathf.Clamp01(dt * 20f));
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1f, 1.12f, c);
            Pose.SY = Mathf.Lerp(1f, 0.86f, c);
            Pose.Rot = 10f * c;
            Pose.Nudge = Vector2.zero;
            if (Level >= 3 && !_glinted && k >= 0.45f)
            {
                _glinted = true;
                if (Visible)
                {
                    Fx.Glint(Hand() + new Vector2(0f, 1.5f * px), px, 0.18f, _pal.Core, 0.9f);
                }
            }
            if (k >= 1f)
            {
                Engage();
            }
        }

        /// <summary>
        /// The first blow on this enemy: it may answer (parry, evade). If it lands we take hold of it.
        /// </summary>
        private void Engage()
        {
            if (_vGone || _v == null || !_v.isAlive() || _v.Sid() != _vid)
            {
                if (!Retarget())
                {
                    Lose();
                }
                return;
            }
            if (!Hopping(_v) || HeldByOther())
            {
                Lose();   // it took off (flying, thrown), or someone else has it
                return;
            }
            float reach = _stand + 0.8f * _k + _v.stats["size"] * 0.5f + 0.35f;
            float d = Vector2.Distance(Pose.Ground, _v.current_position);
            if (d > _stand + 0.5f * _k && _chases < 1 && d <= reach + 1.5f && Combat.ClearPath(Pose.Ground, _v.current_position))
            {
                // it slipped back (an archer's backstep, a dodge): one quick step after it
                _chases++;
                _vGround = _v.current_position;
                Aim();
                _afterStep = Phase.Flurry;
                if (Dev.On) Dev.Trace("fists " + Id + " pursues " + _v.asset.id + "#" + _vid + " (" + d.ToString("F2") + " tiles, h " + _v.position_height.ToString("F2") + ")");
                Next(Phase.StepIn);
                return;
            }
            if (d > reach)
            {
                Lose();   // it got away before the first blow
                return;
            }
            _vGround = _v.current_position;
            Aim();
            Contact answer = Combat.Reach(this, _v, false);
            if (answer == Contact.Parried)
            {
                Next(Phase.Broken);
                return;
            }
            if (answer == Contact.Evaded)
            {
                Next(Phase.Whiff);
                return;
            }
            if (Catchable(_v, A))
            {
                Puppet p = Take(_v, _fwd < 0f, this);
                if (p != null)
                {
                    _p = p;
                    _released = false;
                    // knocked flat (stunned): taken as it lies, and hauled up into the barrage
                    _fallRot = p.Pose.Rot;
                    _fallNudge = p.Pose.Nudge;
                    _rise = Mathf.Abs(_fallRot) > 1f ? 0f : 1f;
                }
            }
            if (Dev.On && !Held) Dev.Trace("fists " + Id + " can't take hold of " + _v.asset.id + "#" + _vid + " (h " + _v.position_height.ToString("F2") + ", locked " + Moves.Locked(_v) + ", heavy " + Weights.Heavy(_v) + "): hits it where it stands");
            Next(Phase.Flurry);
        }

        // ------------------------------------------------------------- flurry --

        private void Flurry(float dt)
        {
            _ft += dt;
            if (!Held)
            {
                // a heavy enemy is hit where it stands; if it walks off (or is caught in someone
                // else's combo) the flurry is over
                _vGround = _v.current_position;
                if (Vector2.Distance(Pose.Ground, _vGround) > _stand + 1.2f * _k + _v.stats["size"] * 0.5f || HeldByOther())
                {
                    Lose();
                    return;
                }
            }
            int fired = 0;
            while (_hit < _hits && _ft >= _times[_hit])
            {
                Blow(fired < 2);
                _hit++;
                fired++;
                if (_vGone || _phase != Phase.Flurry)
                {
                    return;
                }
            }

            // the unit: leaning in, an arm snapping out on every blow, a pixel of jitter
            _punch = Mathf.MoveTowards(_punch, 0f, dt * 16f);
            Pose.Ground = Vector2.Lerp(Pose.Ground, StandSpot(), Mathf.Clamp01(dt * 25f));
            Pose.Height = 0f;
            float lean = Level >= 3 ? 12f : 9f;
            Pose.Rot = lean + (_arm == 0 ? 6f : -2f) * _punch;
            Pose.SX = 1f + 0.1f * _punch;
            Pose.SY = 1f - 0.07f * _punch;
            _jitterT -= dt;
            if (_jitterT <= 0f)
            {
                _jitterT = 0.03f;
                _jx = -_jx;
                Pose.Nudge = new Vector2(_jx, Random.value < 0.35f ? 1f : 0f);
            }
            VictimPose(dt);
            FlurryFx(dt);

            if (Level >= 3 && !_ring && _ft >= 0.52f * _tFlurry)
            {
                _ring = true;
                RingPulse();
            }
            if (_hit >= _hits && _ft >= _tFlurry)
            {
                Next(Phase.Windup);
            }
        }

        /// <summary>One blow of the flurry: never lethal, so the finisher always gets to land.</summary>
        private void Blow(bool fx)
        {
            if (_vGone || _v == null || !_v.isAlive())
            {
                return;
            }
            int i = _hit;
            bool third = i % 3 == 2;
            Combat.Hit(A, _v, _mult * _cm, false, !Held && third, 0f, this);
            _recoil = 1f;
            _punch = 1f;
            _arm ^= 1;
            if (Held && third)
            {
                _p.Flash(0.045f);
                _shake = 0.06f;
            }
            Slide();
            if (fx && Visible)
            {
                BlowFx(i);
            }
            if ((i & 1) == 0 && T - _lastSound >= 0.07f)
            {
                _lastSound = T;
                Anim.Sound(_armed ? SndHit : SndPunch, A.current_tile);
            }
        }

        /// <summary>Each blow drives the enemy back a little (heavier ones less), unless it is pinned.</summary>
        private void Slide()
        {
            if (!Held || _pinned)
            {
                return;
            }
            float step = _slide * (_big ? 0.5f : _critter ? 1.25f : 1f);
            Vector2 nv = _vGround + _dir * step;
            Vector2 na = nv - _dir * _stand;
            if (!Combat.SafeSpot(nv) || !Combat.SafeSpot(na))
            {
                _pinned = true;   // against a wall, the water, the edge of the world
                return;
            }
            _vGround = nv;
        }

        /// <summary>The enemy under the barrage: pressed back, rocked by every blow, shaking on every third.</summary>
        private void VictimPose(float dt)
        {
            if (!Held)
            {
                return;
            }
            Pose vp = _p.Pose;
            vp.Ground = Vector2.Lerp(vp.Ground, _vGround, Mathf.Clamp01(dt * 30f));
            vp.Height = Mathf.MoveTowards(vp.Height, 0f, dt * 6f);   // caught mid-hop: beaten back down
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 10f);
            vp.Rot = -(5f + 9f * _recoil);
            vp.SX = 1f - 0.06f * _recoil;
            vp.SY = 1f + 0.04f * _recoil;
            if (_shake > 0f)
            {
                _shake -= dt;
                vp.Nudge = new Vector2(Random.Range(-1, 2), Random.Range(0, 2));
            }
            else
            {
                vp.Nudge = new Vector2(_fwd * Mathf.Round(_recoil), 0f);
            }
            if (_rise < 1f)
            {
                // taken lying down: the first blows haul it up off the ground
                _rise = Mathf.Min(1f, _rise + dt / TRise);
                float f = 1f - Anim.OutQuad(_rise);
                vp.Rot = Mathf.Lerp(vp.Rot, _fallRot, f);
                vp.Nudge += _fallNudge * f;
            }
            vp.FaceRight = _fwd < 0f;
            _p.Pose = vp;
        }

        private void BlowFx(int i)
        {
            float px = Px;
            Vector2 vb = VBody();
            float spread = _vpx * Mathf.Max(1f, _vPivot / 3.6f);
            Vector2 at = vb + new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(-3f, 3.5f)) * spread;
            Color tip = Level >= 3 ? _pal.Edge : White;
            if (_armed)
            {
                // a stab: a thin line spark along the thrust, and (II+) the blade's streak into the enemy
                float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg + Random.Range(-16f, 16f);
                Particle s = Fx.Spawn(Art.Sparks[1], at, px, 0.07f, FxLayer.Top, 21);
                if (s != null)
                {
                    s.Rot = ang - 90f;
                    Color c = Level >= 3 ? _pal.Edge : Steel;
                    s.Color(White, c, Fx.WithA(c, 0f));
                }
                if (Level >= 2)
                {
                    Kit.Line(Hand(), at, px, 0.04f, 1, FxLayer.Over, 6, Fx.WithA(Steel, 0.85f), Fx.WithA(Steel, 0f));
                }
                if (i % 4 == 3)
                {
                    Fx.Glint(at, px, 0.1f, White, 0.6f);
                }
            }
            else
            {
                // a punch: a spark pops on the enemy, and every fifth blow a hit star
                Particle s = Fx.Spawn(PopFrames(), at, px, 0.08f, FxLayer.Top, 21);
                s?.Color(White, tip, Fx.WithA(tip, 0f));
                if (i % 5 == 4)
                {
                    Particle star = Fx.Spawn(Art.HitStars[0], at, px, 0.06f, FxLayer.Top, 20);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 90f);
                        star.Sized(1.1f, 0.7f).Color(White, Fx.WithA(tip, 0.9f), Fx.WithA(tip, 0f));
                    }
                }
            }
            if (Level >= 2 && (i & 1) == 0)
            {
                // a spark flying off the blow
                float a = Mathf.Atan2(_dir.y, _dir.x) + Random.Range(-1f, 1f);
                Particle sp = Fx.Spawn(Art.Sparks[0], at, px, Random.Range(0.08f, 0.14f), FxLayer.Top, 19);
                if (sp != null)
                {
                    sp.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(50f, 90f) * px, 0f, 8f);
                    sp.FaceVel = true;
                    sp.Color(White, tip, Fx.WithA(tip, 0f));
                }
            }
            if (Level <= 2 && (Level == 2 || (i & 1) == 0))
            {
                // the arm is a blur: a ghost of the unit thrusting forward
                Fx.Ghost(this, _dir * Random.Range(1.5f, 3f) * px, Random.Range(-22f, 28f), 0.06f,
                    Level == 2 ? Fx.WithA(_pal.Ghost, 0.5f) : new Color(1f, 1f, 1f, 0.35f), _pal.GhostEnd);
            }
        }

        private void FlurryFx(float dt)
        {
            if (!Visible)
            {
                return;
            }
            float px = Px;
            Vector2 body = BodyPos(), vb = VBody();
            Vector2 perp = new Vector2(-_dir.y, _dir.x);
            float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;

            // fist-blur speed lines between the two
            _lineT -= dt;
            if (_lineT <= 0f)
            {
                _lineT = 0.06f;
                Color c0, c1;
                if (Level <= 1)
                {
                    c0 = new Color(1f, 1f, 1f, 0.75f);
                    c1 = new Color(1f, 1f, 1f, 0f);
                }
                else if (Level == 2)
                {
                    c0 = Fx.WithA(Color.Lerp(White, _pal.Edge, 0.5f), 0.8f);
                    c1 = Fx.WithA(_pal.Edge, 0f);
                }
                else
                {
                    c0 = Fx.WithA(_pal.Core, 0.9f);
                    c1 = Fx.WithA(_pal.Edge, 0f);
                }
                int n = Fx.Count(Level >= 3 ? 5 : 4);
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = Vector2.Lerp(body, vb, Random.Range(0.25f, 0.8f)) + perp * Random.Range(-3f, 3f) * px + new Vector2(0f, Random.Range(-1f, 2f) * px);
                    Particle l = Fx.Spawn(Art.Lines[0], at, px, 0.07f, FxLayer.Over, 4);
                    if (l == null)
                    {
                        break;
                    }
                    l.Rot = ang - 90f;
                    l.Moving(_dir * 30f * px);
                    l.Color(c0, c1);
                }
            }

            // III: two flickering clones of the unit lunging a pixel or two ahead of it, one a pixel
            // above, one below: many blows at once. Tinted and faint (white ones at 0.42 stacked
            // into a white mass over the pair), and only tilted forward, so they read as jabs
            if (Level >= 3)
            {
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = 0.045f;
                    Color clone = Fx.WithA(Color.Lerp(_pal.Core, _pal.Edge, 0.45f), 0.32f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Fx.Ghost(this, new Vector2(_fwd * Random.Range(1f, 2.5f) * px, s * px), Random.Range(-4f, 16f), 0.05f,
                            clone, _pal.GhostEnd);
                    }
                }
            }

            // short impact ticks snapping in on the enemy (the big manga focus lines are kept for
            // the ring pulse and the finisher: all through the flurry they were only noise)
            if (Level >= 2)
            {
                _focusT -= dt;
                if (_focusT <= 0f)
                {
                    _focusT = Level >= 3 ? 0.1f : 0.13f;
                    Ticks(vb, px, Fx.Count(Level >= 3 ? 4 : 3));
                }
            }

            // the dust trail of the slide: one puff at a time, under one of the two
            if (Held && !_pinned)
            {
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = 0.07f;
                    Color dust = Fx.Lighten(Fx.SoilColor(_v.current_tile), 0.35f);
                    _dustSide = !_dustSide;
                    TrailDust(_dustSide ? _p.Pose.Ground - _dir * px : Pose.Ground - _dir * 2f * px, dust, px);
                }
            }
        }

        /// <summary>A few short streaks darting in at the enemy's body from 0.7-1.2 tiles: the eye stays on the blows.</summary>
        private void Ticks(Vector2 at, float px, int n)
        {
            Color c = Level >= 3 ? Fx.WithA(_pal.Core, 0.6f) : new Color(1f, 1f, 1f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle l = Fx.Spawn(Art.Lines[0], at + d * Random.Range(7f, 12f) * _k * px, px, 0.08f, FxLayer.Top, 2);
                if (l == null)
                {
                    break;
                }
                l.Rot = a * Mathf.Rad2Deg - 90f;
                l.Moving(-d * 50f * px);
                l.Color(c, Fx.WithA(c, 0f));
            }
        }

        private void TrailDust(Vector2 at, Color dust, float px)
        {
            Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], at + new Vector2(Random.Range(-1f, 1f) * px, 0f), px, Random.Range(0.3f, 0.45f), FxLayer.Behind, 2);
            p?.Moving(-_dir * Random.Range(6f, 12f) * px, Random.Range(2f, 5f) * px, 3f)
                .Sized(0.6f, 1f)
                .Color(Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
        }

        /// <summary>
        /// III, 0.8 s: the barrage peaks; a ring of force pulses out from the pair and blows back a
        /// step whoever is crowding it (in game, the target's friends walked into a 1.5-second
        /// flurry and the three of them, the pair and the blows were one unreadable heap).
        /// </summary>
        private void RingPulse()
        {
            float px = Px;
            Vector2 mid = Vector2.Lerp(Pose.Ground, Held ? _p.Pose.Ground : _vGround, 0.5f);
            Vector2 perp = new Vector2(-_dir.y, _dir.x);
            int blown = 0;
            foreach (Actor u in Combat.FoesAround(A, mid, 1.3f * _lk))
            {
                if (u == _v || u.Sid() == _vid)
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    Combat.Stagger(u, 0.3f);   // too heavy to blow away: it only rocks
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                Vector2 away = u.current_position - mid;
                away = away.sqrMagnitude > 0.0004f ? away.normalized : (Random.value < 0.5f ? perp : -perp);
                Combat.Hit(A, u, 0.3f * cm, false, true, 0f, this);
                Combat.Pin(u, 0.5f);
                Combat.LaunchDir(u, away, 0.9f * _lk, 0.35f, 0f);
                blown++;
            }
            if (Dev.On && blown > 0) Dev.Trace("fists " + Id + " ring pulse blows back " + blown);
            // the ground under the barrage is scorched by it
            Ruin(mid, 1);
            if (!Visible)
            {
                return;
            }
            Fx.Ring(mid, px, 0.34f, 3f, 22f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
            Kit.DustRing(mid, px, A.current_tile, 8, 45f, 0.45f, _k);
            Fx.FocusLines(VBody(), px, Fx.Count(10), 14f * _k, 26f * _k, 0.12f);
        }

        /// <summary>A spark that pops on one pixel: a 3-px cross, a dotted cross, a single pixel.</summary>
        private static Sprite[] PopFrames()
        {
            if (_pop != null)
            {
                return _pop;
            }
            Canvas a = new Canvas(3, 3);
            a.Set(1, 1, 1f, 1f);
            a.Set(0, 1, 1f, 0.9f);
            a.Set(2, 1, 1f, 0.9f);
            a.Set(1, 0, 1f, 0.9f);
            a.Set(1, 2, 1f, 0.9f);
            Canvas b = new Canvas(5, 5);
            b.Set(2, 2, 1f, 1f);
            b.Set(0, 2, 1f, 0.7f);
            b.Set(4, 2, 1f, 0.7f);
            b.Set(2, 0, 1f, 0.7f);
            b.Set(2, 4, 1f, 0.7f);
            Canvas c = new Canvas(1, 1);
            c.Set(0, 0, 1f, 1f);
            _pop = new[]
            {
                a.ToSprite("sakuga_fists_pop_0", new Vector2(0.5f, 0.5f)),
                b.ToSprite("sakuga_fists_pop_1", new Vector2(0.5f, 0.5f)),
                c.ToSprite("sakuga_fists_pop_2", new Vector2(0.5f, 0.5f))
            };
            return _pop;
        }

        // ------------------------------------------------------------- windup --

        private void Windup(float dt)
        {
            if (Enter())
            {
                ChooseFinish();
                _from = Pose.Ground;
                _rotFrom = Pose.Rot;
                Pose.Nudge = Vector2.zero;
                if (Level >= 3 && Visible)
                {
                    // the "ting" before the last blow
                    Fx.Glint(Hand() + new Vector2(-_fwd * 2f * Px, 1.5f * Px), Px, 0.2f, _pal.Core, 1.1f);
                }
            }
            float k = Mathf.Clamp01(_pt / _tWindup);
            float e = Anim.OutQuad(k);
            bool up = _finish == Finish.Uppercut;
            Vector2 back = _from - _dir * 0.06f * _k;
            Pose.Ground = Combat.SafeSpot(back) ? Vector2.Lerp(_from, back, e) : _from;
            Pose.Height = 0f;
            Pose.Rot = Mathf.Lerp(_rotFrom, up ? -6f : -10f, e);
            Pose.SX = Mathf.Lerp(1f, up ? 1.12f : 0.9f, e);
            Pose.SY = Mathf.Lerp(1f, up ? 0.8f : 1.06f, e);
            Pose.Nudge = Vector2.zero;
            VictimPose(dt);
            if (k >= 1f)
            {
                Next(Phase.Strike);
            }
        }

        /// <summary>How this one ends depends on who is being hit and where.</summary>
        private void ChooseFinish()
        {
            if (!Held)
            {
                _finish = Finish.Stagger;
            }
            else if (Level <= 1)
            {
                _finish = Finish.Shove;
            }
            else if (_big)
            {
                _finish = Finish.Body;
            }
            else if (Level >= 3)
            {
                PlanFlight();
                bool room = Vector2.Distance(_vFrom, _vTo) >= 0.5f;
                if (!room && !_flyWall)
                {
                    _finish = Finish.Uppercut;      // no line to fly down: straight up
                }
                else if (_flyWall || _lined || _pinned)
                {
                    _finish = Finish.Projectile;    // a wall to slam it into, a crowd to bowl over
                }
                else
                {
                    // nothing on the line: the cannonball, or now and then (more often when an air
                    // follow-up is ready) the rising fist that sends it into the sky for the next
                    // technique; III ended the same way every time
                    _finish = Random.value < (SingleKit.AirReady(A) ? 0.45f : 0.25f) ? Finish.Uppercut : Finish.Projectile;
                }
            }
            else
            {
                _finish = _pinned || _small ? Finish.Uppercut : Finish.Launch;
            }
        }

        /// <summary>The projectile's line: up to 6 tiles, stopped by a wall or mountain (it crashes) or the map's edge.</summary>
        private void PlanFlight()
        {
            _vFrom = Held ? _p.Pose.Ground : _vGround;
            float max = 6f * _lk * SingleKit.Throw(_tier);   // a God's cannonball flies half as far again
            const float step = 0.25f;
            float d = 0f;
            _flyWall = false;
            while (d + step <= max)
            {
                Vector2 q = _vFrom + _dir * (d + step);
                if (q.x < 1.5f || q.y < 1.5f || q.x > MapBox.width - 2.5f || q.y > MapBox.height - 2.5f)
                {
                    break;
                }
                WorldTile t = Combat.TileAt(q);
                if (t == null || t.Type == null)
                {
                    break;
                }
                if (t.Type.block)
                {
                    _flyWall = true;
                    break;
                }
                d += step;
            }
            _vTo = _vFrom + _dir * d;
            _flyDur = (0.05f + d / (14f * Mathf.Sqrt(SingleKit.Throw(_tier)))) * _quick;   // a Master's cannonball flies faster
            _plough = 0f;
            _flyH = 0.3f * _k + 0.08f;
        }

        // ------------------------------------------------------------- strike --

        private void Strike(float dt)
        {
            if (Enter())
            {
                _from = Pose.Ground;
                Vector2 lunge = _from + _dir * (_finish == Finish.Body ? 0.3f : 0.2f) * _k;
                _lungeOk = Combat.SafeSpot(lunge);
                _lungeTo = _lungeOk ? lunge : _from;
                Impact();
                if (_phase != Phase.Strike)
                {
                    return;
                }
            }
            float e = Anim.OutCubic(Mathf.Clamp01(_pt / 0.06f));
            if (_lungeOk)
            {
                Pose.Ground = Vector2.Lerp(_from, _from + _dir * (_finish == Finish.Body ? 0.3f : 0.2f) * _k, e);
            }
            Pose.Nudge = Vector2.zero;
            if (_finish == Finish.Uppercut)
            {
                // the rising fist: stretched tall, a little hop (III: it rises with the blow)
                Pose.Rot = Mathf.Lerp(-6f, -24f, e);
                Pose.SX = Mathf.Lerp(1.12f, 0.86f, e);
                Pose.SY = Mathf.Lerp(0.8f, 1.22f, e);
                float hop = Level >= 3 ? 0.3f : 0.12f, span = Level >= 3 ? 0.3f : 0.22f;
                Pose.Height = hop * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_pt / span));
            }
            else
            {
                // the straight: stretched along the punch, then held
                Pose.Rot = Mathf.Lerp(-10f, 18f, e);
                Pose.SX = Mathf.Lerp(0.9f, 1.2f, e) - 0.12f * Mathf.Clamp01((_pt - 0.06f) / 0.1f);
                Pose.SY = Mathf.Lerp(1.06f, 0.86f, e) + 0.08f * Mathf.Clamp01((_pt - 0.06f) / 0.1f);
                Pose.Height = 0f;
            }
            // a thrown body still in the air: the follow-through is held (zanshin), relaxing a little
            float relax = 0.5f * Anim.OutQuad(Mathf.Clamp01((_pt - 0.18f) / 0.35f));
            if (relax > 0f)
            {
                Pose.Rot *= 1f - relax;
                Pose.SX = Mathf.Lerp(Pose.SX, 1f, relax);
                Pose.SY = Mathf.Lerp(Pose.SY, 1f, relax);
            }
            if (!_vDone)
            {
                VictimFinish(dt);
            }
            if (_pt >= _tStrike && _vDone)
            {
                Next(Phase.Recover);
            }
        }

        /// <summary>The last blow lands (the only lethal one).</summary>
        private void Impact()
        {
            if (_vGone || _v == null || !_v.isAlive())
            {
                _vDone = true;
                return;
            }
            float px = Px;
            Vector2 at = VBody();
            // a body we hold is thrown before it may die: the blow leaves it at 1 HP and the kill
            // is delivered where it lands (a corpse can't be thrown, and a killing blow that just
            // drops the enemy on the spot is the weakest possible ending)
            bool throws = Held && _finish != Finish.Stagger;
            HitResult r = Combat.Hit(A, _v, _finMult * _cm, !throws, !Held, 0f, this);
            _owed = throws && _v.isAlive() && _v.getHealth() <= 2;
            if (Held)
            {
                Tier vt = Tiers.Of(_v);
                _flashAt = T;
                _flashLen = Level >= 2 ? 0.08f : 0.05f;
                _flashTint = Level >= 2 && vt != Tier.None ? Tiers.Color(vt) : Color.white;
                _p.Flash(_flashLen, _flashTint);
            }
            Anim.Sound(SndPunch, A.current_tile);
            if (Level >= 2)
            {
                Anim.Sound(SndHit, A.current_tile);
            }
            if (Visible)
            {
                ImpactFx(at, px, r.Crit);
            }
            if (Level >= 2)
            {
                // the ground under the blow gives way on the same frame: II scorched, III a crater
                // (a blast two tiles out, a God's three and deeper), just beyond the enemy's feet
                // (the spot the unit's lunge ends on stays whole)
                Vector2 feet = Held ? _p.Pose.Ground : _vGround;
                SingleKit.Ruin(A, _lungeTo, feet + _dir * 0.5f * _lk, SingleKit.Radius(_tier, (Level >= 3 ? 1.7f : 1f) * _lk),
                    Level >= 3 ? Force(2, 3) : Force(1, 2));
            }
            if (Level == 2)
            {
                _hold = 0.06f;
            }
            else if (Level >= 3)
            {
                Kit.Stop(at, 0.1f);
                _hold = 0.03f;
            }
            if (Dev.On) Dev.Trace("fists " + Id + " finisher " + _finish + " x" + (_finMult * _cm).ToString("F2") + " -> " + _v.asset.id + " hp " + _v.getHealth() + (r.Crit ? " CRIT" : "") + (_owed ? " (thrown, dies where it lands)" : "") + (Held ? "" : " (not held)"));
        }

        private void ImpactFx(Vector2 at, float px, bool crit)
        {
            switch (Level)
            {
                case 1:
                    {
                        // the palm shove: a white push ring a tile wide under the enemy's feet
                        Vector2 feet = Held ? _p.Pose.Ground : _vGround;
                        Fx.HitSpark(at, px, 0, _dir, 3);
                        Kit.Swish(this, Art.Arc(6, 0.7f, 60f, -60f, 2), 0.1f, new Vector2(_fwd * 2f, 0f), _fwd, White, new Color(0.86f, 0.9f, 0.96f, 1f));
                        Fx.Ring(feet, px, 0.26f, 2f, 10f * _k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                        Fx.LandingPuff(feet, px, _v.current_tile);
                        break;
                    }
                case 2:
                    {
                        // a small white pop and a tier-tinted ring racing out of it (a 10-px disk
                        // that turned tier blue within two frames read as a flat blue blob over the enemy)
                        Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(3f * _k), 0.85f, 0.35f), at, px, 0.05f, FxLayer.Top, 30);
                        flash?.Sized(0.9f, 1.2f).Color(White, Fx.WithA(White, 0.9f), Fx.WithA(_pal.Core, 0f));
                        Fx.Ring(at, px, 0.16f, 3f, 12f * _k, 2, 1, FxLayer.Top, 29, 1f, 2.5f,
                            Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
                        Fx.HitSpark(at, px, 2, _finish == Finish.Uppercut ? Vector2.up : _dir, 6);
                        Fx.FocusLines(at, px, Fx.Count(8), 12f * _k, 22f * _k, 0.12f);
                        if (_finish == Finish.Uppercut)
                        {
                            Kit.Swish(this, Art.Arc(10, 1f, -70f, 115f, 3), 0.16f, new Vector2(_fwd, -1f), _fwd, _pal.Core, _pal.Edge);
                        }
                        else
                        {
                            Kit.SpeedLines(at - _dir * 3f * px, _dir, px, 4, 3f, 0.12f, Fx.WithA(_pal.Edge, 0.85f));
                        }
                        if (crit)
                        {
                            Kit.ImpactFrame(at, px, 0.45f * _k, _pal, true);
                        }
                        break;
                    }
                default:
                    Kit.ImpactFrame(at, px, 0.65f * _k, _pal, crit);
                    if (_finish == Finish.Uppercut)
                    {
                        // the rising fist: the spark and a column of speed lines go up, with the enemy
                        Fx.HitSpark(at, px, 2, Vector2.up, 8);
                        Kit.SpeedLines(at + new Vector2(0f, 4f * px), Vector2.up, px, 6, 5f * _k, 0.2f, Fx.WithA(_pal.Edge, 0.9f));
                        Kit.Swish(this, Art.Arc(Mathf.RoundToInt(12f * _k), 1f, -70f, 115f, 3), 0.18f, new Vector2(_fwd, -1f), _fwd, _pal.Core, _pal.Edge);
                    }
                    else
                    {
                        Fx.HitSpark(at, px, 2, _dir, 8);
                    }
                    Fx.Ring(at, px, 0.22f, 3f, 16f * _k, 2, 1, FxLayer.Top, 3, 1f, 2.5f,
                        Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0f));
                    Fx.FocusLines(at, px, Fx.Count(12), 14f * _k, 28f * _k, 0.14f);
                    Kit.Shake(at, 0.22f, 0.009f, _k);
                    break;
            }
        }

        /// <summary>The enemy's part of the finisher: shoved, launched, uppercut, body-blown, or thrown down the line.</summary>
        private void VictimFinish(float dt)
        {
            if (_vGone)
            {
                _vDone = true;
                return;
            }
            if (_arc)
            {
                ArcVictim(dt);
                return;
            }
            if (_hold > 0f && !_owed && (_finish == Finish.Shove || _finish == Finish.Launch || _finish == Finish.Uppercut))
            {
                // the impact's hold (and III's hit-stop) holds the enemy too, reeling and flashing
                // in the blow: it is let go when the hold is over (let go on the impact frame, it
                // stood upright and unlit beside the hit star through the whole hold, then popped
                // into its flight)
                return;
            }
            float far = _critter ? 1.6f : 1f;
            float high = _critter ? 1.3f : 1f;
            switch (_finish)
            {
                case Finish.Stagger:
                    Combat.Stagger(_v, Level >= 3 ? 1f : 0.6f);
                    SetLink(Link.Down);
                    _vDone = true;
                    break;
                case Finish.Shove:
                    if (_owed)
                    {
                        Arc(0.7f * _lk * far, 0.3f, 0);
                        ArcVictim(dt);
                        break;
                    }
                    CarryFlash();
                    LetGo(true);
                    Schedule(_dir, 0.7f * _lk * far, 0.3f, 0f);
                    SetLink(Link.Close);
                    _vDone = true;
                    break;
                case Finish.Launch:
                    if (_owed)
                    {
                        Arc(2f * _lk * far, 0.9f * high, 1);
                        ArcVictim(dt);
                        break;
                    }
                    CarryFlash();
                    LetGo(true);
                    Schedule(_dir, 2f * _lk * far, 1f * high, Spin(720f));
                    SetLink(SingleKit.AirLink(A, 0.6f));
                    _vDone = true;
                    break;
                case Finish.Uppercut:
                    if (_owed)
                    {
                        Arc((_pinned ? 0.25f : 0.8f) * _lk * far, (Level >= 3 ? 2.2f : 1.6f) * high, 1);
                        ArcVictim(dt);
                        break;
                    }
                    CarryFlash();
                    LetGo(true);
                    // III sends it twice as high (and a stronger tier higher still): a sky for the next technique
                    Schedule(_dir, (_pinned ? 0.25f : 0.8f) * _lk * far, (Level >= 3 ? 3f * Mathf.Sqrt(SingleKit.Throw(_tier)) : 2.2f) * high, Spin(540f));
                    SetLink(SingleKit.AirLink(A));
                    _vDone = true;
                    break;
                case Finish.Body:
                    BodyBlow(dt);
                    break;
                default:
                    FlyVictim(dt);
                    break;
            }
        }

        private float Spin(float s)
        {
            return (Random.value < 0.5f ? -1f : 1f) * s * (_critter ? 1.6f : 1f);
        }

        /// <summary>A big one doesn't fly: the blow slides it back a tile and leaves it reeling.</summary>
        private void BodyBlow(float dt)
        {
            if (!Held)
            {
                _vDone = true;
                return;
            }
            Pose vp = _p.Pose;
            if (!_vStarted)
            {
                _vStarted = true;
                _vFrom = vp.Ground;
                _vTo = _vFrom;
                for (float d = 0.25f; d <= 1f * _lk + 0.001f; d += 0.25f)
                {
                    Vector2 q = _vFrom + _dir * d;
                    if (!Combat.SafeSpot(q))
                    {
                        break;
                    }
                    _vTo = q;
                }
            }
            _vt += dt;
            float k = Mathf.Clamp01(_vt / (TBody * _quick));
            vp.Ground = Vector2.Lerp(_vFrom, _vTo, Anim.OutCubic(k));
            vp.Height = 0f;
            vp.Rot = -18f * (1f - 0.6f * k);
            vp.SX = 1.1f - 0.1f * k;
            vp.SY = 0.9f + 0.1f * k;
            vp.Nudge = Vector2.zero;
            _p.Pose = vp;
            if (Visible)
            {
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = 0.04f;
                    TrailDust(vp.Ground, Fx.Lighten(Fx.SoilColor(_v.current_tile), 0.35f), Px);
                }
            }
            if (k >= 1f)
            {
                LetGo(true);
                // its heels gouge the ground where the slide stops
                Ruin(_vTo, Level >= 3 ? Force(2, 2) : Force(1, 1));
                if (_owed)
                {
                    OwedKill();   // the big one goes down at the end of its slide
                }
                else
                {
                    Combat.Stagger(_v, Level >= 3 ? 1f : 0.6f);
                    SetLink(Link.Down);
                }
                _vDone = true;
            }
        }

        /// <summary>III: the enemy is a cannonball. Everyone on its line is bowled over.</summary>
        private void FlyVictim(float dt)
        {
            if (!Held)
            {
                _vDone = true;
                return;
            }
            Pose vp = _p.Pose;
            if (!_vStarted)
            {
                _vStarted = true;
                _lastBody = _p.BodyPos();
                Anim.Sound(SndWhoosh, A.current_tile);
                if (Visible)
                {
                    Fx.Ring(_p.BodyPos(), Px, 0.18f, 3f, 12f * _k, 2, 1, FxLayer.Top, 3, 0.4f, 2.5f,
                        Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                }
            }
            if (_crumple >= 0f)
            {
                Crumple(dt, ref vp);
                if (!_vDone)
                {
                    _p.Pose = vp;
                }
                return;
            }
            _vt += dt;
            float k = _flyDur > 0f ? Mathf.Clamp01(_vt / _flyDur) : 1f;
            vp.Ground = Vector2.Lerp(_vFrom, _vTo, Mathf.Lerp(k, Anim.OutQuad(k), 0.3f));
            vp.Height = _flyH * Anim.OutQuad(Mathf.Clamp01(_vt / 0.06f));
            vp.Rot -= 1100f * dt;
            vp.SX = 1f;
            vp.SY = 1f;
            vp.Nudge = Vector2.zero;
            _p.Pose = vp;
            Trail(dt, vp.Ground);
            Bowl(vp.Ground);
            Plough(Vector2.Distance(_vFrom, vp.Ground));
            if (k >= 1f)
            {
                if (_flyWall)
                {
                    _crumple = 0f;
                    Crash(_p.Pose.Ground);
                }
                else if (_owed)
                {
                    // the cannonball comes down, rolls on along the line and stays down
                    Touchdown(_p.Pose.Ground);
                    Arc(1.1f * _lk * (_critter ? 1.6f : 1f), 0.45f, 0);
                }
                else
                {
                    Touchdown(_p.Pose.Ground);
                    LetGo(false);
                    Schedule(_dir, 1.1f * _lk * (_critter ? 1.6f : 1f), 0.75f * (_critter ? 1.3f : 1f), Spin(800f));
                    // a low skipping bounce far down the line: mostly the ground follow-ups take it
                    SetLink(SingleKit.AirLink(A, 0.35f));
                    _vDone = true;
                }
            }
        }

        /// <summary>
        /// The cannonball skims the ground and ploughs it open: a trench bite every tile or so of its
        /// line (evenly spaced whatever the frame rate), from a tile out to just short of where it
        /// comes down (that spot craters). A God's bites are wider and burn.
        /// </summary>
        private void Plough(float along)
        {
            float step = 1.1f * _lk;
            float stop = Vector2.Distance(_vFrom, _vTo) - 0.7f * _lk;
            while (_plough + step <= along && _plough + step <= stop)
            {
                _plough += step;
                Vector2 at = _vFrom + _dir * _plough;
                Ruin(at, Force(2, 2));
                WorldTile t = Combat.TileAt(at);
                if (t != null && Kit.Visible(at))
                {
                    Kit.Debris(at, Px, t, 3, _k);
                }
            }
        }

        /// <summary>Where the cannonball comes down, at the end of its trench: a glowing crater, clods, a ring of dust.</summary>
        private void Touchdown(Vector2 at)
        {
            Ruin(at, Force(2, 3));
            WorldTile t = Combat.TileAt(at);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", t);
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = Px;
            Kit.Crater(at, px, t, true, 3f, true, _pal.Edge);   // (on water: a splash)
            if (t == null || t.Type == null || t.Type.liquid)
            {
                return;
            }
            Kit.Debris(at, px, t, 5, _k);
            Kit.DustRing(at, px, t, 6, 36f, 0.45f, _k);
        }

        private void Trail(float dt, Vector2 ground)
        {
            if (!Visible)
            {
                _lastBody = _p.BodyPos();
                return;
            }
            float px = Px;
            Vector2 body = _p.BodyPos();
            _ghostT -= dt;
            if (_ghostT <= 0f)
            {
                _ghostT = 0.028f;
                Fx.Afterimage(_p, 0.14f, _pal.Ghost, _pal.GhostEnd);
            }
            Kit.Line(_lastBody, body, px, 0.16f, 1, FxLayer.Behind, 2, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
            _lineT -= dt;
            if (_lineT <= 0f)
            {
                _lineT = 0.04f;
                Kit.SpeedLines(body - _dir * 3f * px, _dir, px, 2, 4f, 0.12f, Fx.WithA(_pal.Edge, 0.8f));
            }
            _dustT -= dt;
            if (_dustT <= 0f && _v != null)
            {
                _dustT = 0.045f;
                WorldTile t = Combat.TileAt(ground);
                if (t != null && t.Type != null && !t.Type.liquid)
                {
                    Color dust = Fx.Lighten(Fx.SoilColor(t), 0.35f);
                    Vector2 side = new Vector2(-_dir.y, _dir.x) * (Random.value < 0.5f ? -1f : 1f);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], ground, px, 0.4f, FxLayer.Behind, 2);
                    p?.Moving(side * Random.Range(10f, 20f) * px, Random.Range(3f, 6f) * px, 3f)
                        .Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
                }
            }
            _lastBody = body;
        }

        /// <summary>Everyone the flying body passes is knocked down (x1); a heavy one stops it dead.</summary>
        private void Bowl(Vector2 at)
        {
            foreach (Actor u in Combat.FoesAround(A, at, 0.55f * _lk))
            {
                long id = u.Sid();
                if (id == _vid || _bowled.Contains(id))
                {
                    continue;
                }
                _bowled.Add(id);
                if (!Combat.Allowed(A, u, out float cm))
                {
                    // too heavy for this tier: a glancing blow; a giant stops the cannonball
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    // (the finished body's last arc rolls on past it: stopped there, it snapped from
                    // mid-air and mid-spin to its landing pose in one frame)
                    if (Weights.Heavy(u) && !_arc)
                    {
                        StopFlight();
                    }
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                Combat.Hit(A, u, 1f * cm, true, true, 0f, this);
                _knocked++;
                if (Kit.Visible(at))
                {
                    Vector2 ub = new Vector2(u.current_position.x, u.current_position.y + 3.5f * u.actor_scale);
                    Fx.HitSpark(ub, Px, 1, _dir, 4);
                    Fx.LandingPuff(u.current_position, Px, u.current_tile);
                }
                if (T - _lastSound >= 0.06f)
                {
                    _lastSound = T;
                    Anim.Sound(SndPunch, u.current_tile);
                }
                if (u.isAlive())
                {
                    u.addStatusEffect("stunned", 0.8f);
                    Vector2 perp = new Vector2(-_dir.y, _dir.x);
                    Vector2 side = Vector2.Dot(u.current_position - at, perp) >= 0f ? perp : -perp;
                    Combat.LaunchDir(u, (side * 0.8f + _dir * 0.6f).normalized, 1.2f * _lk * SingleKit.Throw(_tier), 0.8f, Random.value < 0.5f ? -600f : 600f);
                }
            }
        }

        private void StopFlight()
        {
            _vTo = _p.Pose.Ground;
            _flyWall = true;
            _flyDur = _vt;
        }

        /// <summary>The cannonball meets a wall: a glowing crater at its foot, debris, dust.</summary>
        private void Crash(Vector2 at)
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(at);
            Combat.Hit(A, _v, 0.5f * _cm, !_owed, false, 0f, this);   // an owed kill lands when it drops
            Ruin(at, Force(2, 3));   // the foot of the wall caves in (a God's bites into the rock itself)
            _p.Flash(0.06f);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            if (Kit.Visible(at))
            {
                Kit.Crater(at + _dir * 2f * px, px, tile, true, 4f, true, _pal.Edge);
                Kit.Debris(at + _dir * 3f * px, px, tile, 8, _k);
                Kit.DustRing(at, px, tile, 10, 50f, 0.6f, _k);
                Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(7f * _k), 0.8f, 0.4f), _p.BodyPos(), px, 0.08f, FxLayer.Top, 30);
                flash?.Sized(0.8f, 1.2f).Color(White, Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
                Kit.Shake(at, 0.25f, 0.01f, _k);
            }
        }

        /// <summary>Flattened against the wall for a beat, then it drops.</summary>
        private void Crumple(float dt, ref Pose vp)
        {
            _crumple += dt;
            float k = Mathf.Clamp01(_crumple / TCrumple);
            vp.Height = _flyH * (1f - k) * (1f - k);
            vp.Rot = Mathf.LerpAngle(vp.Rot, 0f, Mathf.Clamp01(dt * 20f));
            float amp = Anim.Spring(_crumple, 10f, 22f);
            vp.SX = 1f - 0.28f * amp;
            vp.SY = 1f + 0.18f * amp;
            vp.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                vp.Height = 0f;
                vp.SX = vp.SY = 1f;
                vp.Rot = 0f;
                _p.Pose = vp;
                LetGo(true);
                if (_owed)
                {
                    OwedKill();   // slid down the wall, and that was all
                }
                else
                {
                    Combat.Stagger(_v, 1.2f);
                    SetLink(Link.Down);
                }
                _vDone = true;
            }
        }

        // ------------------------------------------------------- thrown bodies --

        /// <summary>The finished enemy's last flight: 'dist' tiles along the line, 'apex' high, 'turns' back-flips.</summary>
        private void Arc(float dist, float apex, int turns)
        {
            _arc = true;
            _vStarted = false;
            _landed = false;
            _vt = 0f;
            _arcDist = dist;
            _arcApex = apex;
            _arcTurns = turns;
        }

        /// <summary>
        /// Thrown along the line (stopping short of walls and water), spinning backwards, it lands
        /// on its back with a thud; on the next frame (once that pose is on the ground) it is let
        /// go and dies there.
        /// </summary>
        private void ArcVictim(float dt)
        {
            if (!Held)
            {
                _vDone = true;
                return;
            }
            Pose vp = _p.Pose;
            if (!_vStarted)
            {
                _vStarted = true;
                _vFrom = vp.Ground;
                _vTo = _vFrom;
                for (float d = 0.25f; d <= _arcDist + 0.001f; d += 0.25f)
                {
                    Vector2 q = _vFrom + _dir * d;
                    if (!Combat.SafeSpot(q))
                    {
                        break;
                    }
                    _vTo = q;
                }
                float span = Vector2.Distance(_vFrom, _vTo);
                _flyDur = Mathf.Clamp(0.16f + 0.12f * span + 0.25f * Mathf.Sqrt(Mathf.Max(0f, _arcApex)), 0.24f, 0.75f) * _quick;
                _arcH0 = vp.Height;
                _arcRot0 = vp.Rot;
                // keep spinning the way it was going (backwards) and land at -80: on its back
                _arcRot1 = _arcRot0 - 360f * _arcTurns - Mathf.Repeat(_arcRot0 + 80f, 360f);
                _lastBody = _p.BodyPos();
            }
            if (_landed)
            {
                LetGo(true);
                OwedKill();
                _vDone = true;
                return;
            }
            _vt += dt;
            float k = _flyDur > 0f ? Mathf.Clamp01(_vt / _flyDur) : 1f;
            vp.Ground = Vector2.Lerp(_vFrom, _vTo, Mathf.Lerp(k, Anim.OutQuad(k), 0.4f));
            vp.Height = Mathf.Lerp(_arcH0, 0f, k) + _arcApex * 4f * k * (1f - k);
            vp.Rot = Mathf.Lerp(_arcRot0, _arcRot1, Anim.OutQuad(k));
            vp.SX = 1f;
            vp.SY = 1f;
            vp.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                vp.Height = 0f;
                _landed = true;
            }
            _p.Pose = vp;
            if (Level >= 3)
            {
                Trail(dt, vp.Ground);
                if (vp.Height < 0.6f * _lk)
                {
                    Bowl(vp.Ground);
                }
            }
            if (_landed)
            {
                Thud(vp.Ground);
            }
        }

        /// <summary>The body hits the ground: a puff and (II+) a ring of dust, and the ground marked (II scorched, III cratered).</summary>
        private void Thud(Vector2 at)
        {
            WorldTile tile = Combat.TileAt(at);
            Anim.Sound(SndHit, tile);
            if (Level >= 2)
            {
                Ruin(at, Level >= 3 ? Force(2, 2) : Force(1, 1));
            }
            if (!Kit.Visible(at))
            {
                return;
            }
            Fx.LandingPuff(at, _vpx, tile);
            if (Level >= 2)
            {
                Kit.DustRing(at, _vpx, tile, 6, 30f, 0.4f, Mathf.Sqrt(Mathf.Clamp(_vpx / 0.1f, 0.6f, 4f)));
            }
        }

        /// <summary>The kill the finisher owed: enough to end it through any armor, at any Power setting.</summary>
        private void OwedKill()
        {
            if (!_owed)
            {
                return;
            }
            _owed = false;
            if (_v == null || !_v.isAlive() || _v.Sid() != _vid || !_v.hasHealth())
            {
                return;
            }
            // (Combat.Hit scales a flat blow by the Power setting too: one sized to the health and
            // armor left the body at 1 HP below Power 1, to get up off its back and fight on)
            Combat.Hit(A, _v, 0f, true, false, 99999f, this);
            if (_v.isAlive() && _v.hasHealth() && Combat.Hostile(A, _v))
            {
                // armor of 100 or more: the game still deals 1 through it (never on a friend: a peace was made mid-flight)
                _v.getHit(_v.getHealth() * 10f + 100f, false, AttackType.Weapon, A, false, false, false);
            }
            if (Dev.On) Dev.Trace("fists " + Id + " " + _v.asset.id + "#" + _vid + " lands and stays down");
        }

        /// <summary>
        /// Hand the enemy back to the game. It is still reeling from the barrage: no swing for half a
        /// second (released a tick before its launch, it used to hit back at the very frame it was
        /// punched away).
        /// </summary>
        private void LetGo(bool onGround)
        {
            if (_p == null || _released)
            {
                return;
            }
            if (!_p.Finished)
            {
                _p.Release(onGround);
            }
            _released = true;
            if (_v != null && _v.Sid() == _vid && _v.isAlive())
            {
                Combat.Pin(_v, 0.5f);
            }
        }

        /// <summary>
        /// The enemy is let go to fly: what is left of the finisher's flash goes on with it (a
        /// puppet's flash ends with the puppet: a level I shove, let go on the impact frame, never
        /// flashed at all). A flat-colour copy of the unit that follows it wherever the game draws
        /// it, fading out as the puppet's flash would have.
        /// </summary>
        private void CarryFlash()
        {
            float left = _flashLen - (T - _flashAt);
            Actor v = _v;
            if (left <= 0.01f || !Held || !Fx.HasSilhouette || v == null || !v.is_visible)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, v.current_position, 1f, left, FxLayer.Over, 1, true);
            if (p == null)
            {
                return;
            }
            long id = v.Sid();
            p.UseEuler = true;
            p.Color(Fx.WithA(_flashTint, Mathf.Clamp01(left / 0.05f)), Fx.WithA(_flashTint, 0f));
            Place(p, v);
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    Place(q, v);
                }
                else
                {
                    q.Kill();
                }
            };
        }

        /// <summary>Puts a silhouette exactly where the game draws the unit.</summary>
        private static void Place(Particle q, Actor v)
        {
            q.Pos = v.current_position + v.move_jump_offset + new Vector2(0f, v.position_height);
            q.Z = 0f;
            q.Euler = v.current_rotation;
            q.SX = v.current_scale.x;
            q.SY = v.current_scale.y;
        }

        private void Schedule(Vector2 dir, float distance, float apex, float spin)
        {
            _launch = true;
            _launchDir = dir;
            _launchDist = distance;
            _launchApex = apex;
            _launchSpin = spin;
            _launchTries = 0;
        }

        /// <summary>The released enemy flies (Combat.Launch skips units still held by a move, so this waits a tick).</summary>
        private void DoLaunch()
        {
            if (_v == null || !_v.isAlive() || _v.Sid() != _vid || _v.isInMagnet() || _v.isInsideSomething())
            {
                _launch = false;
                return;
            }
            if (Moves.Locked(_v))
            {
                if (++_launchTries > 5)
                {
                    _launch = false;
                }
                return;
            }
            _launch = false;
            Combat.LaunchDir(_v, _launchDir, _launchDist, _launchApex, _launchSpin);
        }

        private void SetLink(Link link)
        {
            if (_v != null && _v.isAlive() && _v.hasHealth() && _v.Sid() == _vid)
            {
                EndLink = link;
                EndTarget = _v;
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            if (Enter())
            {
                SingleKit.Cooldown(A, _tier);
                _rotFrom = Pose.Rot;
                Pose.Nudge = Vector2.zero;
                if (_glintEnd && Visible)
                {
                    Fx.Glint(Hand() + new Vector2(0f, 1.5f * Px), Px, 0.22f, _pal.Core, 0.9f);
                }
            }
            // (a Master's spring plays in about half the time: it lands crisply and goes)
            float st = _pt / _snap;
            float amp = Anim.Spring(st, 9f, 18f);
            Pose.SX = 1f + 0.1f * amp;
            Pose.SY = 1f - 0.12f * amp;
            Pose.Rot = _rotFrom * Mathf.Exp(-10f * st);
            Pose.Height = Mathf.MoveTowards(Pose.Height, 0f, dt * 2f / _snap);
            Pose.Nudge = Vector2.zero;
            if (_pt >= _tRecover && !_launch)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                LetGo(true);
                if (_knocked >= 2)
                {
                    // the cannonball went through a crowd and blew it apart: whatever follows a
                    // scattered crowd (a wave, a dash, a slam) comes straight after
                    EndLink = Link.Scattered;
                    EndTarget = _v != null && _v.isAlive() && _v.Sid() == _vid ? _v : null;
                }
                Finished = true;
            }
        }

        // ------------------------------------------------- answered: parried --

        /// <summary>The first blow was parried: the flurry breaks and the unit reels back.</summary>
        private void Broken(float dt)
        {
            if (Enter())
            {
                _from = Pose.Ground;
                _to = _from - _dir * 0.38f * _k;
                if (!Combat.SafeSpot(_to))
                {
                    _to = _from;
                }
                if (Visible)
                {
                    Fx.LandingPuff(_from, Px, A.current_tile);
                    Fx.Afterimage(this, 0.12f, new Color(1f, 1f, 1f, 0.7f), _pal.GhostEnd);
                }
            }
            float k = Mathf.Clamp01(_pt / _tBroken);
            Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
            float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.25f));
            Pose.Rot = -20f * lean;
            Pose.SX = 1f - 0.06f * lean;
            Pose.SY = 1f + 0.06f * lean;
            Pose.Height = 0f;
            Pose.Nudge = k < 0.55f ? new Vector2(((int)(_pt * 50f) & 1) == 0 ? 1f : -1f, 0f) : Vector2.zero;
            if (k >= 1f)
            {
                SingleKit.Cooldown(A, _tier);
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Nudge = Vector2.zero;
                Finished = true;
            }
        }

        // ------------------------------------------------- answered: evaded --

        /// <summary>The enemy wasn't there: a few blows into an afterimage, then an overextended stumble.</summary>
        private void Whiff(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _from = Pose.Ground;
                _to = _from + _dir * 0.18f * _k;
                if (!Combat.SafeSpot(_to))
                {
                    _to = _from;
                }
                Anim.Sound(SndWhoosh, A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / _tWhiff);
            Pose.Height = 0f;
            if (k < 0.55f)
            {
                float local = (_pt % 0.06f) / 0.06f;
                float punch = 1f - local;
                Pose.Ground = _from;
                Pose.Rot = 10f + 6f * punch;
                Pose.SX = 1f + 0.1f * punch;
                Pose.SY = 1f - 0.07f * punch;
                Pose.Nudge = new Vector2(((int)(_pt * 33f) & 1) == 0 ? 1f : -1f, 0f);
                if (Visible)
                {
                    _lineT -= dt;
                    if (_lineT <= 0f)
                    {
                        _lineT = 0.06f;
                        float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
                        for (int i = 0; i < Fx.Count(3); i++)
                        {
                            Vector2 at = Hand() + _dir * Random.Range(1f, 5f) * px + new Vector2(0f, Random.Range(-2f, 2f) * px);
                            Particle l = Fx.Spawn(Art.Lines[0], at, px, 0.07f, FxLayer.Over, 4);
                            if (l == null)
                            {
                                break;
                            }
                            l.Rot = ang - 90f;
                            l.Moving(_dir * 30f * px);
                            l.Color(new Color(1f, 1f, 1f, 0.6f), new Color(1f, 1f, 1f, 0f));
                        }
                    }
                }
            }
            else
            {
                float s = Anim.OutQuad((k - 0.55f) / 0.45f);
                Pose.Ground = Vector2.Lerp(_from, _to, s);
                Pose.Rot = Mathf.Lerp(20f, 4f, s);
                Pose.SX = Mathf.Lerp(1.1f, 1f, s);
                Pose.SY = Mathf.Lerp(0.9f, 1f, s);
                Pose.Nudge = Vector2.zero;
            }
            if (k >= 1f)
            {
                SingleKit.Cooldown(A, _tier);
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Nudge = Vector2.zero;
                Finished = true;
            }
        }

        // -------------------------------------------------------------- abort --

        public override void Abort()
        {
            if (_p != null && !_released && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            _released = true;
            _launch = false;
            // cut off with the kill still owed (the finisher left it at death's door, mid-flight): it
            // still dies where it lands, as MeteorSlam's do
            if (_owed && _v != null && _v.Sid() == _vid && _v.isAlive() && _v.hasHealth())
            {
                _owed = false;
                MeteorSlam.Doom(A, _v, this);
            }
        }
    }

    /// <summary>
    /// Hundred Fists as a technique. Entry: Novice (the first technique most fighters learn).
    /// The AI uses it on an enemy right next to it, on the ground; preferred on an enemy someone
    /// else has stunned, against Brutes, and on an archer that got too close (pinned, it can't
    /// back off and shoot). At III it likes enemies
    /// lined up behind its target (they get bowled over). It is less keen on a long flurry with
    /// several enemies around it, and never wastes a flurry on an enemy one ordinary blow would end.
    ///
    /// Damage (multiples of the user's damage): I 6 x0.22 + x0.35 = x1.67; II 14 x0.16 + x0.8 = x3.04;
    /// III 30 x0.1 + x1.9 = x4.9. (It was x5 / x12: the cheapest technique out-damaged Rising
    /// Tempest III by half again and ended every duel in one go; then x3.6 / x5.7 with a +35 bonus
    /// on every Down link on top of the Brain's chain bonus, and it was the AI's pick for nearly a
    /// third of all techniques.) A chain into it is worth what a chain into anything is worth.
    /// Combat.Hit multiplies all of it by the tier's Might, and the tiers' health grew with it: a
    /// III takes about 45% of an equal Expert, 35% of a Master, 20% of a God (a duel takes its
    /// own share), and wipes out anything far below it.
    /// </summary>
    internal sealed class HundredFistsTech : Technique
    {
        private static readonly List<Actor> _ray = new List<Actor>();

        public override string Id => "hundred_fists";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;

        /// <summary>A storm of blows too fast to count, then a finisher.</summary>
        public override Form Forms => Form.Punch | Form.Flurry | Form.Fast | Form.Body;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Light : Scale.Heavy;

        public override float[] ManaCost => new[] { 10f, 20f, 35f };
        public override float[] Cooldown => new[] { 6f, 8f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        /// <summary>One blow of the flurry (multiple of the user's damage).</summary>
        public static float Blow(int level)
        {
            return level <= 1 ? 0.22f : level == 2 ? 0.16f : 0.1f;
        }

        /// <summary>The finisher.</summary>
        public static float Finisher(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.8f : 1.9f;
        }

        /// <summary>Total damage multiple of the whole move.</summary>
        public static float Total(int level)
        {
            int hits = level <= 1 ? 6 : level == 2 ? 14 : 30;
            return hits * Blow(level) + Finisher(level);
        }

        private static bool Ranged(Actor v)
        {
            try
            {
                return v.hasRangeAttack();
            }
            catch
            {
                return false;
            }
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;   // never a flurry straight into another flurry (seen after a forced one)
            }
            Actor best = null;
            float bestScore = 0f;
            int bestCount = 1;
            Consider(c, c.Focus, level, ref best, ref bestScore, ref bestCount);
            if (!c.Forced)
            {
                for (int i = 0; i < c.Enemies.Count && i < 4; i++)
                {
                    if (c.EnemyDist[i] > 3f)
                    {
                        break;
                    }
                    if (c.Enemies[i] != c.Focus)
                    {
                        Consider(c, c.Enemies[i], level, ref best, ref bestScore, ref bestCount);
                    }
                }
            }
            else if (best == null && c.ForceTarget == null && c.Enemies.Count > 0)
            {
                Consider(c, c.Enemies[0], level, ref best, ref bestScore, ref bestCount);
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right,
                Count = bestCount,
                Score = bestScore
            };
        }

        private static void Consider(Ctx c, Actor v, int level, ref Actor best, ref float bestScore, ref int bestCount)
        {
            if (v == null || !v.isAlive() || Moves.Locked(v) || !HundredFists.Hopping(v) || !c.Allowed(v, out float cm))
            {
                return;
            }
            bool linkTarget = c.Linked && v == c.LinkTarget;
            if (c.Forced)
            {
                if (c.Dist(v) > HundredFists.StepRange + v.stats["size"] || !Combat.ClearPath(c.Pos, v.current_position))
                {
                    return;
                }
            }
            else if (!c.Adjacent(v, linkTarget ? 0.9f : 0.15f) || c.Dist(v) > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
            {
                return;   // out of reach, or a river / wall between them
            }
            if (!c.Forced && v.getHealth() <= c.Expected(v, 1.2f) * cm)
            {
                return;   // one ordinary blow ends it: a two-second flurry of non-lethal blows would be silly
            }
            // the bread and butter of a Novice; above that, one choice among many (round 2: 28 of
            // 93 techniques the AI chose were this, 24 of them as a chain)
            float score = c.Value(v, c.Expected(v, Total(level)), cm) + (level <= 1 ? 15f : 10f);
            if (v.is_unconscious && !linkTarget)
            {
                // someone else's stun: an open invitation. (A Down link from our own last move is
                // already worth the Brain's chain bonus to every technique that accepts it; adding
                // 35 more here made this the answer to every Down link.)
                score += 20f;
            }
            if (Weights.Species(v) >= Weight.Brute)
            {
                score += 15f;   // a brute that shrugs off single blows is buried under a hundred
            }
            bool takes = HundredFists.Catchable(v, c.Self);
            if (!takes)
            {
                score *= 0.75f;   // it can't be driven back: no slide, no finisher flight
            }
            else if (Ranged(v))
            {
                score += 15f;     // an archer in arm's reach: pin it before it backs off and shoots
            }
            int count = 1;
            if (level >= 3)
            {
                // the bowling finisher: enemies lined up behind the target
                Vector2 dir = v.current_position - c.Pos;
                float d = dir.magnitude;
                if (d > 0.05f)
                {
                    dir /= d;
                    _ray.Clear();
                    c.OnRay(dir, d + 6.5f * SingleKit.Throw(c.Tier), 0.6f, _ray);
                    foreach (Actor u in _ray)
                    {
                        if (u == v || Vector2.Dot(u.current_position - c.Pos, dir) < d + 0.3f)
                        {
                            continue;
                        }
                        c.Allowed(u, out float cmu);
                        score += c.Value(u, c.Expected(u, 1f), cmu);
                        count++;
                    }
                    _ray.Clear();
                }
            }
            if (level >= 2 && !c.Forced)
            {
                // a long flurry in a crowd gets interrupted
                int around = c.Surrounding(1.6f);
                if (around > 2)
                {
                    score *= Mathf.Max(0.5f, 1f - 0.12f * (around - 2));
                }
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
                bestCount = count;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && HundredFists.Begin(a, p.Target, p.Level, this, p.Count > 1);
        }
    }

    /// <summary>
    /// What Hundred Fists, Severing Flash and Still Water share about the performer's tier: how
    /// little dead time it has after a move (Masters and up land crisply and go), the force of the
    /// world damage its blows do, and the chain a launch leaves.
    /// </summary>
    internal static class SingleKit
    {
        /// <summary>Fights with techniques and flow strikes only (TierInfo.Flow): Masters and above.</summary>
        public static bool Flow(Tier t)
        {
            TierInfo i = Tiers.Info(t);
            return i != null && i.Flow;
        }

        /// <summary>
        /// Dead time (recovering, sheathing, reeling, stumbling) is multiplied by this: the tier's
        /// TierInfo.Linger, the framework's one measure of it (Expert 0.85, Master 0.55, Grandmaster
        /// 0.48, God 0.42; 1 below and for untiered units), so these moves keep the same pace as
        /// every other technique.
        /// </summary>
        public static float Snap(Tier t)
        {
            TierInfo i = Tiers.Info(t);
            return i != null && i.Linger > 0.05f ? Mathf.Min(1f, i.Linger) : 1f;
        }

        /// <summary>
        /// How hard the tier throws (distances of launches and of the cannonball): 1 up to Expert,
        /// then 1.15 / 1.3 / 1.6 for Master / Grandmaster / God.
        /// </summary>
        public static float Throw(Tier t)
        {
            switch (t)
            {
                case Tier.Master: return 1.15f;
                case Tier.Grandmaster: return 1.3f;
                case Tier.God: return 1.6f;
                default: return 1f;
            }
        }

        /// <summary>World damage force (Kit.Ravage): 'lo' up to Expert, 'hi' from Master; a God's is one more (at most 4).</summary>
        public static int Force(Tier t, int lo, int hi)
        {
            int f = t >= Tier.Master ? hi : lo;
            if (t == Tier.God)
            {
                f++;
            }
            return Mathf.Clamp(f, 0, 4);
        }

        /// <summary>A God's craters are half as wide again.</summary>
        public static float Radius(Tier t, float r)
        {
            return t == Tier.God ? r * 1.5f : r;
        }

        /// <summary>
        /// The pocket around the performer's own feet that a Ruin only scorches (tiles): its own
        /// tile and the nearest of its neighbours. (Burning ground no longer counts as bad footing,
        /// and only Masters and up, who don't burn, light fires at all.)
        /// </summary>
        public const float KeepR = 0.9f;

        /// <summary>
        /// Kit.Ravage at full size, with a pocket around the performer's own feet ('feet', where it
        /// stands) where nothing is dug or lit, only scorched: it never ends its move in a pit or on
        /// fire. (It used to shrink the whole blast until the performer's tile was outside it, which
        /// left a Hundred Fists III finisher, landed a tile away, a one-tile scorch.) Water and lava
        /// are left alone (a body skimming a river ploughs nothing).
        /// </summary>
        public static void Ruin(Actor by, Vector2 feet, Vector2 at, float radius, int force)
        {
            if (force <= 0 || Settings.Destruction <= 0 || by == null)
            {
                return;
            }
            WorldTile c = Combat.TileAt(at);
            if (c == null || c.Type == null || c.Type.liquid || c.Type.lava)
            {
                return;
            }
            Kit.Ravage(at, Mathf.Clamp(radius, 0.75f, 16f), Mathf.Clamp(force, 1, 4), by, true, feet, KeepR);
        }

        /// <summary>
        /// The game's attack cooldown after a move, for those that still swing. A Flow tier's swings
        /// are flow strikes: starting the cooldown would only leave it standing between techniques.
        /// </summary>
        public static void Cooldown(Actor a, Tier t)
        {
            if (a != null && !Flow(t))
            {
                a.startAttackCooldown();
            }
        }

        /// <summary>Does 'a' know a ready follow-up for an airborne enemy (Wingbreaker)?</summary>
        public static bool AirReady(Actor a)
        {
            if (a != null && Fighters.TryGet(a, out Fighter f))
            {
                IReadOnlyList<Technique> all = Techniques.All;
                for (int i = 0; i < all.Count; i++)
                {
                    Technique t = all[i];
                    if (!t.Reactive && !t.GodAbility && t.Accepts(Link.Launched) && f.Ready(t) && Techniques.Level(a, t, f.Tier) > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The chain an enemy knocked into the air leaves: Launched (an air follow-up: Wingbreaker,
        /// the only one) or Down (it lands reeling a moment later and the ground follow-ups take
        /// it: a dash, a fissure, a tempest...), so a launch never ends the flow. 'air' is how
        /// strongly the throw invites the air follow-up: 1 for a launch straight up (an uppercut),
        /// less for one that skims along the ground. (Always Launched when Wingbreaker was ready
        /// turned every Hundred Fists III and every Still Water riposte into the same Wingbreaker:
        /// five times in one round of duels.)
        /// </summary>
        public static Link AirLink(Actor a, float air = 1f)
        {
            return Random.value < air && AirReady(a) ? Link.Launched : Link.Down;
        }
    }
}
