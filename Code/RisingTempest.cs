using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// RISING TEMPEST. Instead of a normal blow, the unit kicks its enemy into the sky,
    /// jumps after it, hammers it with a flurry of blows in mid-air, spins into a kick that
    /// throws it higher, appears above it and axe-kicks it straight down into the ground,
    /// then floats down and lands calmly beside the crater.
    ///
    /// LEVEL II, "Rising Tempest" (game seconds at x1, ~2.4 s; swooshes and afterimages in the tier's colour):
    ///
    ///   STEP IN   ~0.1   slide in beside the enemy (from further away: a blur of afterimages);
    ///                    the enemy is caught flat-footed (no swing of its own, Combat.Pin) but
    ///                    can still answer the kick
    ///   LAUNCH    0.20   crouch, rising kick with an upward crescent swoosh; at the moment of
    ///                    contact the enemy may answer (parry, evade); if it doesn't, it flies up.
    ///                    An enemy caught mid-hop (an archer's backstep, a dash, a dodge) is met in
    ///                    the air by a jumping kick (a smear of afterimages) and stays caught
    ///   CHASE     0.22   crouch, leap after it on a trail of afterimages (after a catch up in
    ///                    the air: an air-step, a flat ring under its feet, and on up)
    ///   FLURRY    0.50   six blows in mid-air: jabs and blurred ghost fists (an armed unit adds
    ///                    little crescent slashes), sparks, hit flashes, manga focus lines, each blow
    ///                    freezing the pair for a beat (local hit-stop)
    ///   SPIN      0.26   a full spinning kick inside a ring-shaped swoosh; the enemy is knocked
    ///                    higher, spinning
    ///   OVERHEAD  0.12   a smear up and over the enemy, heel raised
    ///   AXE       0.14   the heel comes down in a falling crescent: impact frame (the pair holds
    ///                    on a starburst behind them, the enemy a flat dark shape against it; the
    ///                    world freeze is III's), the enemy is spiked into the ground
    ///   DESCENT   ~0.9   the enemy craters and bounces (dust, cracks, shockwave, debris; a Black
    ///                    Flash if the finishing blow is critical); the unit flips once and floats
    ///                    down, lands, springs up, "ting!"
    ///
    /// LEVEL I, "Rising Kick" (~1.6 s, white only, no hit-stop): launcher, chase, a three-blow
    /// flurry and the spin kick, which is the finisher: the enemy is let go up there and falls on
    /// its own, tumbling (the game's physics); the unit floats down after it. No crater.
    ///
    /// LEVEL III, "Tempest Drop" (~3.2 s, white-hot core and the tier's colour): level II up to the
    /// spike, with an impact frame and a 0.1 s world freeze on the axe kick, then
    ///
    ///   REBOUND   0.34   it bounces off the blow, flips once more at the apex, glint, heel up
    ///   DROP      0.15   a heel drop straight down into the crater, trailing fire
    ///   QUAKE     0.10   the crater grows to 1.5 tiles with a glowing core, a shockwave knocks
    ///                    down everyone within 1.5 tiles; the enemy takes the last blow
    ///   HOP OUT   0.30   a back-flip out of the crater
    ///   LAND      0.34   it lands with its back to the crater while embers rise from it
    ///
    /// WHEN THE KICK DOESN'T CONNECT (any level):
    ///   PARRIED   0.30   a "ting" where the kick met the guard; the unit is rocked back, skids
    ///                    0.4 tiles and staggers (0.4 s daze); the enemy is untouched and free
    ///   EVADED    0.36   the kick cuts through empty air; it overbalances and stumbles a step
    ///   GONE      0.24   the enemy is no longer there to kick (dead, taken, busy, airborne): it
    ///                    settles into a stance with a glint. During the step-in it first looks
    ///                    for another enemy of the same weight (or lighter) within 2 tiles.
    ///
    /// THE WORLD TAKES IT (Kit.Ravage, on the impact frame): II's spike blasts a real crater
    /// where the enemy hits the ground (force 2, r 1); III's Tempest Drop then grows it into a
    /// burning one (force 3, r ~1.7), never setting fire to the ground the unit lands on. A God
    /// hits one force harder over 1.5x the radius, and its shockwave reaches 1.3x further.
    ///
    /// TEMPO: the float down and the landing play in the tier's Linger share of the time (Expert
    /// 0.85, Master 0.55, Grandmaster 0.48, God 0.42), the hop out and the Tempest Drop's flip in
    /// its square root, and a whiffed kick or a vanished enemy costs less time the higher the tier.
    /// The shockwave's victims fly; one it would have killed dies as it hits the ground.
    ///
    /// FLOW (Masters and up): no attack
    /// cooldown is started (their ordinary swings are FlowStrikes), and the combo always leaves
    /// a chain hook when there is something to chain onto (Down on the enemy, Scattered after
    /// the shockwave, Close on whoever stands right there, or on the one that just evaded it).
    ///
    /// The enemy becomes a puppet only when the launcher connects. It can't die before the last
    /// blow (every hit before it leaves it at least 1 health), so the combo always plays out;
    /// anything else that kills it, or the god hand picking either unit up, ends the combo early
    /// and lets both fall naturally.
    /// </summary>
    internal sealed class RisingTempest : Move
    {
        public const float Range = 2.4f;

        private enum Phase
        {
            StepIn,
            Launch,
            Chase,
            Flurry,
            Spin,
            Overhead,
            Axe,
            Rebound,
            Drop,
            Quake,
            HopOut,
            Descent,
            Land,
            Recoil,
            Whiff,
            Settle
        }

        /// <summary>What happened when the launcher reached the enemy.</summary>
        private enum Outcome
        {
            Hit,
            Parried,
            Whiffed,
            Gone
        }

        private const float TLaunch = 0.2f;
        private const float TKick = 0.085f;
        private const float TSpin = 0.26f;
        private const float TOverhead = 0.12f;
        private const float TAxe = 0.14f;
        private const float TRebound = 0.34f;
        private const float TDrop = 0.15f;
        private const float TQuake = 0.1f;
        private const float THopOut = 0.3f;
        private const float TRecoil = 0.3f;
        private const float TWhiff = 0.36f;
        private const float TSettle = 0.24f;

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Fire1 = new Color(1f, 0.84f, 0.32f, 1f);
        private static readonly Color Fire2 = new Color(1f, 0.48f, 0.12f, 1f);
        private static readonly Color Fire3 = new Color(0.78f, 0.18f, 0.07f, 1f);
        private static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);
        private static readonly Color Ting = new Color(1f, 0.92f, 0.55f, 1f);
        /// <summary>The impact frame's silhouette: a body flat and dark against the starburst behind it.</summary>
        private static readonly Color ImpactDark = new Color(0.08f, 0.06f, 0.09f, 1f);
        private static readonly float[] JabRot = { 18f, -8f, 22f, -10f, 16f, 26f };

        private Actor _v;
        private long _vId;
        private Weight _vClass;
        /// <summary>The class rule on every blow: 0.5 when it punches above its class (Valiant), else 1.</summary>
        private float _cm = 1f;
        /// <summary>The enemy, driven by this move: null until the launcher connects.</summary>
        private Puppet _p;
        private float _fwd;
        private readonly float _k;
        private readonly Palette _pal;
        private readonly bool _armed;
        private Vector2 _aStart;
        private float _tStep;
        private Vector2 _stand;
        private Vector2 _vGround;
        private Vector2 _aFrom, _aTo, _over, _contactAt;
        private float _hFrom, _hTo;
        private float _rotFrom;
        /// <summary>How high the rising kick hops: a little, or up to an enemy caught mid-hop.</summary>
        private float _kickHop;
        /// <summary>The kick caught it high in its hop: the unit stays up there and chases from the air.</summary>
        private bool _airCatch;
        private float _contactH;

        /// <summary>A Master or above: lands crisply and goes (TierInfo.Flow).</summary>
        private readonly bool _flow;
        /// <summary>How far the shockwave and the jolt reach for this tier (MeteorSlamTech.AreaScale).</summary>
        private readonly float _area;
        /// <summary>How fast the landing spring plays (1 / TierInfo.Linger: the same motion, less time).</summary>
        private readonly float _landSpeed = 1f;
        private readonly float _tHopOut = THopOut, _tWhiff = TWhiff, _tSettle = TSettle, _tRebound = TRebound;
        /// <summary>How many the Tempest Drop's shockwave knocked down (a crowd to chain onto).</summary>
        private int _quaked;
        /// <summary>Where the hop out of the crater lands (chosen on the quake's frame).</summary>
        private Vector2? _hopTo;

        // this level's rhythm and weight
        private readonly int _hits;
        private readonly float _tChase, _tFlurry, _gap, _tDescent, _tLand;
        private readonly float _mLaunch, _mJab, _mSpin, _mAxe, _mCrash, _mDrop;
        private readonly float _holdLaunch, _holdJab, _holdSpin, _holdAxe;

        private Phase _phase = Phase.StepIn;
        private float _pt;
        private bool _entered;
        private float _hold;
        private float _ghost, _lines, _ember;
        private int _flurry;
        private bool _hit;
        private bool _vGone;
        private bool _contact, _retargeted, _finisher, _dropped, _glinted, _puffed;
        private bool _tumble;
        private int _tumbleTries;
        private Vector2 _lastTrail;

        // the enemy's own little state machine, driven by the blows
        private enum VState
        {
            Stand,
            Rise,
            Float,
            Spike,
            Bounce,
            Lie,
            Final,
            Done
        }

        private VState _vs = VState.Stand;
        private float _vt, _vDur;
        private float _vh0, _vh1;
        private Vector2 _vg0, _vg1;
        private float _vSpin;

        /// <summary>Combos started / finished (the finishing blow landed) since the game started (dev log).</summary>
        internal static int Started, Finished_;

        /// <summary>Launchers that were parried / that kicked empty air (dev log).</summary>
        internal static int Parried, Whiffed;

        private RisingTempest(Actor a, Actor v, int level, float fwd) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _v = v;
            _vId = v.Sid();
            _vClass = Weights.Of(v);
            // (a god's order on something above its class: a glancing blow, as Wingbreaker's)
            _cm = Combat.Allowed(a, v, out float cm) ? cm : 0.5f;
            _aStart = a.current_position;
            _vGround = v.current_position;
            _fwd = fwd;
            Pose.FaceRight = _fwd > 0f;
            _k = Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
            _kickHop = 0.3f * _k;
            _pal = Palette.For(a, Level);
            _armed = Armed(a);
            _stand = StandBeside();
            float dist = Vector2.Distance(_aStart, _stand);
            _tStep = StepTime(dist);
            TierInfo info = Tiers.Info(Tiers.Of(a));
            _flow = info != null && info.Flow;
            _area = MeteorSlamTech.AreaScale(a);
            // (with the tier's Might on every blow: I ~2.0 x damage in all, II ~3.65, III ~6.8 plus
            // the shockwave; each level decisively above the last, none a one-shot of an equal)
            switch (Level)
            {
                case 1:
                    _hits = 3;
                    _tChase = 0.2f;
                    _tFlurry = 0.28f;
                    _gap = 0.08f;
                    _tDescent = 0.34f;
                    _tLand = 0.22f;
                    _mLaunch = 0.4f;
                    _mJab = 0.2f;
                    _mSpin = 1f;
                    break;
                case 2:
                    _hits = 6;
                    _tChase = 0.22f;
                    _tFlurry = 0.5f;
                    _gap = 0.075f;
                    _tDescent = 0.52f;
                    _tLand = 0.3f;
                    _mLaunch = 0.4f;
                    _mJab = 0.15f;
                    _mSpin = 0.55f;
                    _mAxe = 0.7f;
                    _mCrash = 1.1f;
                    _holdLaunch = 0.05f;
                    _holdJab = 0.03f;
                    _holdSpin = 0.06f;
                    _holdAxe = 0.06f;   // II's axe kick: its own hit-stop is the impact frame (no world freeze)
                    break;
                default:
                    _hits = 6;
                    _tChase = 0.22f;
                    _tFlurry = 0.5f;
                    _gap = 0.075f;
                    _tDescent = 0.52f;
                    _tLand = 0.34f;
                    _mLaunch = 0.45f;
                    _mJab = 0.18f;
                    _mSpin = 0.6f;
                    _mAxe = 0.8f;
                    _mCrash = 1.07f;
                    _mDrop = 2.8f;
                    _holdLaunch = 0.05f;
                    _holdJab = 0.03f;
                    _holdSpin = 0.06f;
                    _holdAxe = 0.05f;
                    break;
            }
            // the tier's tempo (TierInfo.Linger: Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42):
            // the float down and the landing spring play in that share of the time (land crisply and
            // go), a missed kick or a vanished enemy costs less; the moves that are motion, not rest
            // (the hop out of the crater, the Tempest Drop's flip at the apex), by its square root
            float linger = Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
            float tempo = Mathf.Sqrt(linger);
            _tDescent *= linger;
            _landSpeed = 1f / linger;
            _tLand *= linger;
            _tHopOut = THopOut * tempo;
            _tRebound = TRebound * tempo;
            _tWhiff = TWhiff * Mathf.Max(0.6f, linger);
            _tSettle = TSettle * Mathf.Max(0.55f, linger);
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>Can this unit be kicked around (not a boat, a flyer, a giant, or busy)? Also mid-hop.</summary>
        public static bool CanJuggle(Actor v, Actor by)
        {
            return Kickable(v, by);
        }

        /// <summary>The highest hop the launcher still catches (world units, times the kicker's size).</summary>
        private const float MaxCatch = 2.6f;

        /// <summary>
        /// Can it be kicked up right now? Everything Combat.CanPuppet asks, except that an enemy
        /// caught in one of the game's own little hops (an archer's backstep away from whoever steps
        /// in, a melee unit's dash, a dodge) still counts: the kick catches it in the air. CanPuppet
        /// alone refuses anyone above the ground, and archers hop back from anyone who closes in,
        /// so without this the launcher found "no one to kick" against every archer.
        /// (Combat.CanCatch, with the highest hop scaled by the kicker's size.)
        /// </summary>
        private static bool Kickable(Actor v, Actor by)
        {
            return by != null && Combat.CanCatch(v, by, MaxCatch * Mathf.Sqrt(Mathf.Clamp(by.actor_scale / 0.1f, 0.6f, 4f)));
        }

        /// <summary>Does it fight from a distance (a bow, a staff)? Those hop away from a melee fighter.</summary>
        internal static bool Ranged(Actor v)
        {
            try
            {
                return v != null && v.hasRangeAttack();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Somewhere solid to stand beside 'v' and a clear way there from 'a' (no water, wall or lava
        /// in between). Cheap enough for a plan: a few tile lookups.
        /// </summary>
        public static bool CanReach(Actor a, Actor v)
        {
            if (a == null || v == null)
            {
                return false;
            }
            float k = Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
            float side = SideFor(a.current_position, v.current_position, a.flip, k);
            if (side == 0f)
            {
                return false;
            }
            Vector2 stand = Combat.Clamp(v.current_position - new Vector2(side * 0.5f * k, 0f));
            return Vector2.Distance(a.current_position, stand) < 0.3f || Combat.ClearPath(a.current_position, stand);
        }

        public static bool Begin(Actor a, Actor v, int level = 2)
        {
            if (!Moves.CanPerform(a) || !CanJuggle(v, a) || v.isInLiquid() || !CanReach(a, v))
            {
                return false;
            }
            float k = Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
            float side = SideFor(a.current_position, v.current_position, a.flip, k);
            RisingTempest m = new RisingTempest(a, v, level, side);
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(a.current_position, v.current_position, 0.5f) + new Vector2(0f, 2.2f * m._k));
            return true;
        }

        /// <summary>
        /// The side to kick it from (+1: stand on its left, facing right): our own side if the ground
        /// beside it is solid, else the far side, else 0 (nowhere to stand).
        /// </summary>
        private static float SideFor(Vector2 from, Vector2 victim, bool flip, float k)
        {
            float dx = victim.x - from.x;
            float fwd = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : (flip ? 1f : -1f);
            if (Combat.SafeSpot(Combat.Clamp(victim - new Vector2(fwd * 0.5f * k, 0f))))
            {
                return fwd;
            }
            if (Combat.SafeSpot(Combat.Clamp(victim + new Vector2(fwd * 0.5f * k, 0f))))
            {
                return -fwd;
            }
            return 0f;
        }

        private static float StepTime(float dist)
        {
            return dist > 0.6f ? Mathf.Clamp(0.08f + 0.03f * dist, 0.1f, 0.32f) : 0.06f;
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

        /// <summary>From 'from' along x towards 'dir' for 'dist' tiles, stopping short of anything unsafe.</summary>
        private static Vector2 Slide(Vector2 from, float dir, float dist)
        {
            Vector2 last = from;
            int n = Mathf.Max(1, Mathf.CeilToInt(dist / 0.1f));
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = from + new Vector2(dir * dist * i / n, 0f);
                if (!Combat.SafeSpot(p))
                {
                    break;
                }
                last = p;
            }
            return last;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (_p != null && !_vGone && (_p.Finished || !_v.isAlive() || _v.Sid() != _p.Id))
            {
                // killed by someone else or taken by the god hand: wind down
                _vGone = true;
                if (!_p.Finished)
                {
                    _p.Release(false);
                }
                // (once the Tempest Drop has begun it lands anyway: the crater and the shockwave don't need it)
                if (_phase >= Phase.Launch && _phase <= Phase.Axe)
                {
                    Next(Pose.Height > 0.05f ? Phase.Descent : Phase.Land);
                }
            }
            if (_tumble)
            {
                TumbleAway();
            }
            if (_hold > 0f)
            {
                // hit-stop: the two hold their poses for a beat, shaking with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                if (_p != null && !_vGone)
                {
                    _p.Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? -0.5f : 0.5f, 0f);
                }
                return;
            }
            Pose.Nudge = Vector2.zero;
            if (_p != null && !_vGone)
            {
                _p.Pose.Nudge = Vector2.zero;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.StepIn:
                    StepIn(dt);
                    break;
                case Phase.Launch:
                    Launch(dt);
                    break;
                case Phase.Chase:
                    Chase(dt);
                    break;
                case Phase.Flurry:
                    Flurry(dt);
                    break;
                case Phase.Spin:
                    Spin(dt);
                    break;
                case Phase.Overhead:
                    Overhead(dt);
                    break;
                case Phase.Axe:
                    Axe(dt);
                    break;
                case Phase.Rebound:
                    Rebound(dt);
                    break;
                case Phase.Drop:
                    Drop(dt);
                    break;
                case Phase.Quake:
                    Quake(dt);
                    break;
                case Phase.HopOut:
                    HopOut(dt);
                    break;
                case Phase.Descent:
                    Descent(dt);
                    break;
                case Phase.Recoil:
                    Recoil(dt);
                    break;
                case Phase.Whiff:
                    Whiff(dt);
                    break;
                case Phase.Settle:
                    Settle(dt);
                    break;
                default:
                    Land(dt);
                    break;
            }
            if (_p != null && !_vGone)
            {
                Victim(dt);
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("tempest " + Id + " " + Techniques.Roman(Level) + " " + _phase + " -> " + p + " at t=" + T.ToString("F3") + " h=" + Pose.Height.ToString("F2"));
            _phase = p;
            _pt = 0f;
            _entered = false;
            _hit = false;
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

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        /// <summary>Where to stand to fight it: right beside it, facing it.</summary>
        private Vector2 StandBeside()
        {
            return Combat.Clamp(_vGround - new Vector2(_fwd * 0.5f * _k, 0f));
        }

        /// <summary>Follows the enemy while it is still free, but never onto water, a wall or lava.</summary>
        private void UpdateStand()
        {
            Vector2 s = StandBeside();
            if (Combat.SafeSpot(s))
            {
                _stand = s;
            }
        }

        /// <summary>The enemy is still there to be kicked (before the launcher connects).</summary>
        private bool VictimHere()
        {
            return _v != null && _v.isAlive() && _v.Sid() == _vId && !_v.isInMagnet() && !_v.isInsideSomething();
        }

        /// <summary>The enemy's body in the air (where blows land).</summary>
        private Vector2 VBody => _p.BodyPos();

        /// <summary>A lighter enemy goes higher, a heavier one less.</summary>
        private float Lift()
        {
            float lift = _vClass <= Weight.Critter ? 1.2f : _vClass >= Weight.Brute ? 0.85f : 1f;
            return Level <= 1 ? lift * 0.75f : lift;
        }

        // ------------------------------------------------------------ step in --

        private void StepIn(float dt)
        {
            float px = Px;
            if (!VictimHere() && !Retarget())
            {
                Next(Phase.Settle);
                return;
            }
            _vGround = _v.current_position;
            // caught flat-footed: no swing of its own across the launcher's wind-up (its weapon's
            // arc sat over the step-in, r2_rising_tempest_2 0004-0009); it can still answer the kick
            Combat.Pin(_v, 0.25f);
            UpdateStand();
            Vector2 to = _stand;
            float dist = Vector2.Distance(_aStart, to);
            if (Enter() && Visible && dist > 0.6f)
            {
                Fx.LandingPuff(_aStart, px, A.current_tile);
                if (dist > 2f)
                {
                    Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                }
            }
            float k = Mathf.Clamp01(_pt / _tStep);
            Pose.Ground = Vector2.Lerp(_aStart, to, Anim.OutQuad(k));
            float lean = Mathf.Sin(Mathf.PI * k);
            Pose.Rot = 14f * lean;
            Pose.SX = 1f + 0.1f * lean;
            Pose.SY = 1f - 0.1f * lean;
            if (dist > 0.6f && Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.02f;
                    Fx.Afterimage(this, 0.14f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Launch);
            }
        }

        /// <summary>
        /// The enemy it was going for is gone before the kick: another one of the same weight (or
        /// lighter) within 2 tiles, if there is one it can kick from solid ground and slide to without
        /// crossing water, a wall or lava (CanReach's rule). Once per combo.
        /// </summary>
        private bool Retarget()
        {
            if (_retargeted || _contact)
            {
                return false;
            }
            _retargeted = true;
            Actor best = null;
            float bestD = float.MaxValue, side = 0f, bestCm = 1f;
            foreach (Actor u in Combat.FoesAround(A, Pose.Ground, 2f * _k))
            {
                if (u == _v || !Kickable(u, A) || u.isInLiquid() || !Combat.Allowed(A, u, out float cm) || Weights.Of(u) > _vClass)
                {
                    continue;
                }
                float d = (u.current_position - Pose.Ground).sqrMagnitude;
                if (d >= bestD)
                {
                    continue;
                }
                float s = SideFor(Pose.Ground, u.current_position, _fwd > 0f, _k);
                if (s == 0f)
                {
                    continue;
                }
                Vector2 stand = Combat.Clamp(u.current_position - new Vector2(s * 0.5f * _k, 0f));
                if (Vector2.Distance(Pose.Ground, stand) >= 0.3f && !Combat.ClearPath(Pose.Ground, stand))
                {
                    continue;
                }
                bestD = d;
                best = u;
                side = s;
                bestCm = cm;
            }
            if (best == null)
            {
                return false;
            }
            if (Dev.On) Dev.Trace("tempest " + Id + " retargets " + best.asset.id + "#" + best.Sid());
            _v = best;
            _vId = best.Sid();
            _vClass = Weights.Of(best);
            _cm = bestCm;
            _vGround = best.current_position;
            _fwd = side;
            Pose.FaceRight = _fwd > 0f;
            _aStart = Pose.Ground;
            _stand = StandBeside();
            _tStep = StepTime(Vector2.Distance(_aStart, _stand));
            _pt = 0f;
            _entered = false;
            return true;
        }

        // ------------------------------------------------------------- launch --

        private void Launch(float dt)
        {
            float px = Px;
            if (_p == null && VictimHere())
            {
                _vGround = _v.current_position;
                if (!_contact)
                {
                    Combat.Pin(_v, 0.25f);
                }
            }
            UpdateStand();
            Pose.Ground = _stand;
            if (_pt < 0.05f)
            {
                // wind-up crouch
                float c = Anim.OutQuad(_pt / 0.05f);
                Pose.SX = Mathf.Lerp(1f, 1.12f, c);
                Pose.SY = Mathf.Lerp(1f, 0.84f, c);
                Pose.Rot = -6f * c;
                Pose.Height = 0f;
            }
            else
            {
                // the rising kick: lean back, stretch up, a little hop; an enemy caught mid-hop
                // (an archer's backstep, a dash) is met in the air by a jumping kick
                float k = Mathf.Clamp01((_pt - 0.05f) / (TLaunch - 0.05f));
                if (!_contact && _p == null && VictimHere())
                {
                    _kickHop = Mathf.Clamp(_v.position_height - 0.15f * _k, 0.3f * _k, 2.2f * _k);
                }
                Pose.SX = Mathf.Lerp(0.86f, 1f, k);
                Pose.SY = Mathf.Lerp(1.24f, 1f, k);
                Pose.Rot = Mathf.Lerp(-6f, -30f, Anim.OutCubic(Mathf.Clamp01(k * 2f))) * (1f - k * 0.4f);
                Pose.Height = _airCatch
                    // caught up there: it stays up with it, drifting up a touch, ready to chase
                    ? _contactH + 0.12f * _k * Anim.OutQuad(Mathf.Clamp01((_pt - TKick) / (TLaunch - TKick)))
                    : _kickHop * Mathf.Sin(Mathf.PI * k);
                if (Enter() && Visible)
                {
                    Kit.Swish(this, Art.Arc(10, 1f, -70f, 115f, 3), 0.16f, new Vector2(_fwd * 1f, -1f), _fwd, _pal.Core, _pal.Edge);
                    Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                }
                if (_kickHop > 0.5f * _k && !_contact && Visible)
                {
                    // the jumping kick is a smear: it leaves itself behind on the way up
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.02f;
                        Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                    }
                }
                if (!_contact && _pt >= TKick)
                {
                    _contact = true;
                    switch (Connect())
                    {
                        case Outcome.Parried:
                            Next(Phase.Recoil);
                            return;
                        case Outcome.Whiffed:
                            Next(Phase.Whiff);
                            return;
                        case Outcome.Gone:
                            Next(Phase.Settle);
                            return;
                    }
                    Blow(_mLaunch, false, 1, Vector2.up + new Vector2(_fwd * 0.3f, 0f), 5);
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                    _hold = _holdLaunch;
                    _contactH = Pose.Height;
                    _airCatch = _p.Pose.Height > 0.5f * _k;
                    // up it goes (from wherever its hop had taken it: always well above that)
                    VRise(Mathf.Max(2.4f * Lift() * _k, _p.Pose.Height + 1f * _k), 0.36f, _fwd * 0.12f * _k, -420f);
                    if (Visible)
                    {
                        // kicked off the ground: a puff where it stood; caught mid-hop: just the streaks
                        float h = _p.Pose.Height;
                        Vector2 feet = _vGround + new Vector2(0f, h);
                        if (h <= 0.3f * _k)
                        {
                            Fx.LandingPuff(_vGround, px, _v.current_tile);
                        }
                        int n = Fx.Count(4);
                        for (int i = 0; i < n; i++)
                        {
                            Vector2 at = feet + new Vector2(Random.Range(-3f, 3f), Random.Range(1f, 6f)) * px;
                            Particle l = Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, 0.16f, FxLayer.Top, 1);
                            l?.Moving(new Vector2(0f, 45f * px)).Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                        }
                    }
                }
            }
            if (_pt >= TLaunch)
            {
                Next(Phase.Chase);
            }
        }

        /// <summary>
        /// The launcher reaches the enemy: is it still there and within reach, does it answer
        /// (parry, evade), and can it be taken? Only then does it become a puppet.
        /// </summary>
        private Outcome Connect()
        {
            if (!VictimHere())
            {
                return Outcome.Gone;
            }
            Vector2 vp = _v.current_position;
            float vh = Mathf.Max(0f, _v.position_height);
            float vpx = Mathf.Clamp(_v.actor_scale, 0.04f, 0.4f);
            _contactAt = Vector2.Lerp(BodyPos() + new Vector2(_fwd * 3f * Px, 0f), vp + new Vector2(0f, vh + 3.5f * vpx), 0.5f);
            float reach = 0.85f * _k + _v.stats["size"] * 0.5f + 0.3f;
            if (Vector2.Distance(Pose.Ground, vp) > reach || vh - Pose.Height > 1.3f * _k)
            {
                return Outcome.Whiffed;   // it stepped (or hopped) out of reach
            }
            if (!Kickable(_v, A) || _v.isInLiquid())
            {
                return Outcome.Gone;
            }
            Contact c = Combat.Reach(this, _v, false);
            if (c == Contact.Parried)
            {
                return Outcome.Parried;
            }
            if (c == Contact.Evaded)
            {
                return Outcome.Whiffed;
            }
            if (!VictimHere() || !Kickable(_v, A))
            {
                return Outcome.Gone;
            }
            _vGround = _v.current_position;
            vh = Mathf.Max(0f, _v.position_height);
            Puppet p = new Puppet(_v, _fwd < 0f, this);
            // caught mid-hop: it goes on up from where it is, never snapped down to the ground first
            p.Pose.Height = vh > 0.05f ? vh : 0f;
            if (!Moves.Start(p))
            {
                return Outcome.Gone;
            }
            _p = p;
            if (Dev.On && vh > 0.05f)
            {
                Dev.Trace("tempest " + Id + " catches " + _v.asset.id + " mid-hop at h=" + vh.ToString("F2"));
            }
            return Outcome.Hit;
        }

        // ------------------------------------------------- the kick that missed --

        /// <summary>Parried: a "ting" at the contact point; rocked back, a skid, a stagger. The enemy is untouched.</summary>
        private void Recoil(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Parried++;
                _aFrom = Pose.Ground;
                _aTo = Slide(_aFrom, -_fwd, 0.4f * _k);
                _hFrom = Pose.Height;
                Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
                if (Visible)
                {
                    Fx.Glint(_contactAt, px, 0.24f, White, 1.2f);
                    Kit.Sparks(_contactAt, px, 7, 60f, 130f, Ting);
                    Fx.Ring(_contactAt, px, 0.14f, 2f, 9f * _k, 1, 1, FxLayer.Top, 4, 1f, 2.5f, Fx.WithA(White, 0.9f), Fx.WithA(White, 0f));
                }
                if (Dev.On) Dev.Trace("tempest " + Id + " parried by " + (_v != null ? _v.asset.id : "?"));
            }
            float k = Mathf.Clamp01(_pt / TRecoil);
            float e = Anim.OutCubic(k);
            Pose.Ground = Vector2.Lerp(_aFrom, _aTo, e);
            Pose.Height = Mathf.Lerp(_hFrom, 0f, Anim.OutQuad(Mathf.Clamp01(k * 3f)));
            // rocked back by the guard, then a wobble as it catches itself
            float back = 1f - Anim.Smooth(0.3f, 1f, k);
            Pose.Rot = -32f * back + 7f * Mathf.Sin(k * 20f) * (1f - k) * Anim.Smooth(0.25f, 0.4f, k);
            Pose.SX = Mathf.Lerp(0.9f, 1f, e);
            Pose.SY = Mathf.Lerp(1.1f, 1f, e);
            if (Visible && k < 0.7f)
            {
                // the feet dig in as it skids
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.05f;
                    Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], Pose.Ground + new Vector2(_fwd * 1.5f * px, 0f), px, 0.3f, FxLayer.Behind, 2);
                    p?.Moving(new Vector2(_fwd * 10f * px, 2f * px), 3f * px, 5f).Sized(0.5f, 0.9f).Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0f));
                }
            }
            if (k >= 1f)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Cooldown();
                // dazed by the guard (Combat.Stagger's stun would flash it white now, 0.3 s after
                // the "ting", like a second blow from nowhere)
                if (A.getActorComponent<Dragon>() != null)
                {
                    Combat.Stagger(A, 0.4f);   // (a dragon takes no statuses: the framework holds it down)
                }
                else
                {
                    Daze(A, 0.4f);
                    A.startShake(0.25f, 0.12f, true, false);
                }
                Finished = true;
            }
        }

        /// <summary>Evaded: the kick cuts through empty air, it overbalances and stumbles a step.</summary>
        private void Whiff(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Whiffed++;
                _aFrom = Pose.Ground;
                _aTo = Slide(_aFrom, _fwd, 0.3f * _k);
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                if (Visible)
                {
                    // a second, emptier crescent through the air where the enemy was
                    Kit.Swish(this, Art.Arc(12, 0.9f, -40f, 140f, 2), 0.2f, new Vector2(_fwd * 2f, 0f), _fwd,
                        Fx.WithA(_pal.Core, 0.75f), Fx.WithA(_pal.Edge, 0.6f));
                }
                if (Dev.On) Dev.Trace("tempest " + Id + " whiffs");
            }
            float k = Mathf.Clamp01(_pt / _tWhiff);
            if (k < 0.38f)
            {
                // the follow-through: nothing there, the leg keeps going
                float u = k / 0.38f;
                Pose.Ground = _aFrom;
                Pose.Rot = Mathf.Lerp(_rotFrom, -46f, Anim.OutQuad(u));
                Pose.Height = Mathf.Lerp(_hFrom, 0.22f * _k, Mathf.Sin(Mathf.PI * 0.5f * u));
                Pose.SX = 0.92f;
                Pose.SY = 1.1f;
            }
            else if (k < 0.72f)
            {
                // overbalanced: a stumbling step forward
                float u = (k - 0.38f) / 0.34f;
                Pose.Ground = Vector2.Lerp(_aFrom, _aTo, Anim.InOutSine(u));
                Pose.Rot = Mathf.Lerp(-46f, 18f, Anim.InOutSine(u));
                Pose.Height = 0.22f * _k * (1f - u * u);
                Pose.SX = Mathf.Lerp(0.92f, 1.08f, u);
                Pose.SY = Mathf.Lerp(1.1f, 0.88f, u);
                if (!_puffed && u > 0.85f)
                {
                    _puffed = true;
                    if (Visible)
                    {
                        Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                    }
                }
            }
            else
            {
                // and catches itself
                float u = (k - 0.72f) / 0.28f;
                float amp = Anim.Spring(u * 0.3f, 9f, 20f);
                Pose.Ground = _aTo;
                Pose.Height = 0f;
                Pose.Rot = 18f * (1f - u) * (1f - u);
                Pose.SX = 1f + 0.08f * amp;
                Pose.SY = 1f - 0.12f * amp;
            }
            if (k >= 1f)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Cooldown();
                if (_flow)
                {
                    // a master doesn't stand there wondering: it turns on the one that slipped it
                    // (a Mirage Step lands right behind) or whoever is right there
                    LinkClose(VictimHere() ? _v : null);
                }
                Finished = true;
            }
        }

        /// <summary>Nothing left to kick: it settles into a stance, with a glint.</summary>
        private void Settle(float dt)
        {
            if (Enter())
            {
                _hFrom = Pose.Height;
                _rotFrom = Mathf.DeltaAngle(0f, Pose.Rot);
                if (Visible)
                {
                    Fx.Glint(HeadPos() + new Vector2(_fwd * 2f * Px, 2f * Px), Px, 0.24f, _pal.Core, 0.9f);
                }
                if (Dev.On) Dev.Trace("tempest " + Id + " has no one to kick");
            }
            float k = Mathf.Clamp01(_pt / _tSettle);
            float e = Anim.OutCubic(k);
            Pose.Height = Mathf.Lerp(_hFrom, 0f, e);
            Pose.Rot = Mathf.Lerp(_rotFrom, 0f, e);
            float amp = Anim.Spring(_pt * (TSettle / _tSettle), 10f, 20f);
            Pose.SX = 1f + 0.08f * amp;
            Pose.SY = 1f - 0.1f * amp;
            if (k >= 1f)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Cooldown();
                if (_flow)
                {
                    LinkClose(null);
                }
                Finished = true;
            }
        }

        /// <summary>
        /// The attack cooldown the game would start after a blow, so the unit doesn't swing the
        /// moment the move ends. Never for a Flow tier: its ordinary blows are FlowStrikes, and a
        /// cooldown would only leave it standing there.
        /// </summary>
        private void Cooldown()
        {
            if (!_flow)
            {
                A.startAttackCooldown();
            }
        }

        /// <summary>
        /// Leaves a Close chain hook (the Brain chains straight on) onto 'prefer' if it is still a
        /// foe within reach, else onto the nearest foe within 2.5 tiles. Nothing if there is none.
        /// </summary>
        private void LinkClose(Actor prefer)
        {
            Actor a = A;
            Vector2 at = Pose.Ground;
            float reach = 2.5f * _k;
            Actor pick = null;
            // (the one that slipped the kick may still be finishing its step: the follow-up's own
            // plan judges whether it can be touched yet, within the link's window)
            if (prefer != null && Combat.IsFoe(a, prefer) && (prefer.current_position - at).sqrMagnitude <= reach * reach
                && Combat.Allowed(a, prefer))
            {
                pick = prefer;
            }
            else
            {
                float best = float.MaxValue;
                foreach (Actor u in Combat.FoesAround(a, at, reach))
                {
                    float d = (u.current_position - at).sqrMagnitude;
                    if (d < best && Combat.Allowed(a, u) && !MeteorSlam.IsDoomed(u))
                    {
                        best = d;
                        pick = u;
                    }
                }
            }
            if (pick == null)
            {
                return;
            }
            EndLink = Link.Close;
            EndTarget = pick;
            if (Dev.On) Dev.Trace("tempest " + Id + " leaves Close -> " + pick.asset.id);
        }

        // -------------------------------------------------------------- chase --

        private void Chase(float dt)
        {
            float px = Px;
            Pose.Ground = Vector2.Lerp(Pose.Ground, _vGround - new Vector2(_fwd * 0.42f * _k, 0f), Mathf.Clamp01(dt * 12f));
            // from the ground: a crouch and a leap; caught up in the air: it kicks off the air itself
            float t0 = _airCatch ? 0f : 0.04f;
            if (_pt < t0)
            {
                float c = Anim.OutQuad(_pt / t0);
                Pose.Height = Mathf.Lerp(Pose.Height, 0f, c);
                Pose.SX = 1.1f;
                Pose.SY = 0.86f;
                Pose.Rot = 0f;
                return;
            }
            if (Enter())
            {
                _hFrom = _airCatch ? Pose.Height : 0f;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    if (_airCatch)
                    {
                        // an air-step: a flat ring under its feet, nothing on the ground
                        Fx.Ring(Pose.Ground + new Vector2(0f, Pose.Height), px, 0.18f, 2f, 9f * _k, 1, 1, FxLayer.Top, 3, 0.35f, 2.5f,
                            new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
                    }
                    else
                    {
                        Fx.Ring(Pose.Ground, px, 0.2f, 2f, 12f * _k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                            new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
                        Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                    }
                }
            }
            float k = Mathf.Clamp01((_pt - t0) / (_tChase - t0));
            float target = _p.Pose.Height - 0.14f * _k;
            Pose.Height = Mathf.Lerp(_hFrom, target, Anim.OutCubic(k));
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.7f));
            Pose.SX = Mathf.Lerp(0.78f, 1f, s);
            Pose.SY = Mathf.Lerp(1.35f, 1f, s);
            Pose.Rot = 8f * s;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.025f;
                    Fx.Afterimage(this, 0.15f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (_pt >= _tChase)
            {
                Next(Phase.Flurry);
            }
        }

        // ------------------------------------------------------------- flurry --

        private void Flurry(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _flurry = 0;
                VFloat(-80f);
            }
            float k = Mathf.Clamp01(_pt / _tFlurry);
            Vector2 home = _vGround - new Vector2(_fwd * 0.42f * _k, 0f);
            float baseH = _p.Pose.Height - 0.12f * _k + 0.04f * _k * Mathf.Sin(_pt * 28f);

            // which blow are we in, and how far into it
            const float first = 0.03f;
            float gap = _gap;
            float local = (_pt - first) % gap;
            int index = Mathf.FloorToInt((_pt - first) / gap);
            bool inside = index >= 0 && index < _hits;
            float jab = inside ? Mathf.Clamp01(1f - Mathf.Abs(local - 0.02f) / 0.035f) : 0f;
            float rot = inside ? JabRot[index % JabRot.Length] : 0f;
            Pose.Ground = home + new Vector2(_fwd * 0.11f * _k * jab, 0f);
            Pose.Height = baseH;
            Pose.Rot = rot * jab;
            Pose.SX = 1f + 0.12f * jab;
            Pose.SY = 1f - 0.08f * jab;

            // every blow whose moment has come lands, even when a slow frame skipped past it
            int due = _pt >= first + 0.02f ? Mathf.Min(_hits, Mathf.FloorToInt((_pt - first - 0.02f) / gap) + 1) : 0;
            if (k >= 1f)
            {
                due = _hits;
            }
            bool sounded = false;
            while (_flurry < due && !_vGone)
            {
                int i = _flurry++;
                if (Visible)
                {
                    Fx.Afterimage(this, 0.09f, Fx.WithA(_pal.Ghost, 0.8f), _pal.GhostEnd);
                    if (_armed)
                    {
                        // a blade in hand: each blow is a little crescent slash
                        bool up = (i & 1) == 0;
                        Kit.Swish(this, Art.Arc(7, 0.8f, up ? -60f : 60f, up ? 60f : -60f, 2), 0.1f, new Vector2(_fwd * 4f, 0f), _fwd,
                            _pal.Core, _pal.Edge, 6);
                    }
                }
                Vector2 away = new Vector2(_fwd, Random.Range(-0.4f, 0.6f));
                Blow(_mJab, false, 0, away, 3);
                if (_p != null)
                {
                    _p.Pose.Rot += Random.Range(-10f, 10f);
                }
                if ((i & 1) == 0 && !sounded)
                {
                    sounded = true;
                    Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                }
                _hold = _holdJab;
            }

            if (Visible)
            {
                // the blur of fists between the blows
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.025f;
                    Fx.Ghost(this, new Vector2(_fwd * Random.Range(0f, 0.12f) * _k, Random.Range(-0.05f, 0.05f) * _k),
                        Random.Range(-20f, 38f), 0.07f, Fx.WithA(_pal.Ghost, 0.45f), _pal.GhostEnd);
                }
                _lines -= dt;
                if (_lines <= 0f && !_vGone)
                {
                    _lines = 0.045f;
                    Fx.FocusLines(Vector2.Lerp(BodyPos(), VBody, 0.6f), px, Fx.Count(5), 14f * _k, 24f * _k, 0.09f);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Spin);
            }
        }

        // --------------------------------------------------------------- spin --

        private void Spin(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                if (Visible)
                {
                    Kit.Swish(this, Art.Arc(12, 0.42f, 180f, 540f, 3), 0.22f, Vector2.zero, _fwd, _pal.Core, _pal.Edge);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / TSpin);
            Pose.Ground = Vector2.Lerp(Pose.Ground, _vGround - new Vector2(_fwd * 0.42f * _k, 0f), Mathf.Clamp01(dt * 10f));
            Pose.Height = _hFrom + 0.15f * _k * Anim.OutQuad(k);
            Pose.Rot = 360f * Anim.InOutCubic(k);
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.06f * tuck;
            Pose.SY = 1f - 0.14f * tuck;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.03f;
                    Fx.Afterimage(this, 0.1f, Fx.WithA(_pal.Ghost, 0.55f), _pal.GhostEnd);
                }
            }
            if (!_hit && k >= 0.55f && !_vGone)
            {
                _hit = true;
                Vector2 at = VBody;
                bool last = Level <= 1;
                Blow(_mSpin, last, 2, new Vector2(_fwd, 0.5f), 7);
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                _hold = _holdSpin;
                if (Visible)
                {
                    Fx.Ring(at, px, 0.16f, 3f, 12f * _k, 2, 1, FxLayer.Top, 3, 1f, 2.5f,
                        Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    Fx.FocusLines(at, px, Fx.Count(10), 14f * _k, 26f * _k, 0.12f);
                    if (Level >= 2)
                    {
                        // (Rising Kick stays light: no camera shake at I)
                        Shake(0.15f, 0.006f);
                    }
                }
                if (last)
                {
                    // Rising Kick: the spin kick ends it; the enemy is let go up there and falls on its own
                    LetGo();
                }
                else
                {
                    VRise(_p.Pose.Height + 1.0f * _k, 0.3f, _fwd * 0.4f * _k, -900f);
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Level <= 1 ? Phase.Descent : Phase.Overhead);
            }
        }

        // ----------------------------------------------------------- overhead --

        private void Overhead(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _glinted = false;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / TOverhead);
            float e = Anim.OutCubic(k);
            Vector2 over = _vGround - new Vector2(_fwd * 0.14f * _k, 0f);
            Pose.Ground = Vector2.Lerp(_aFrom, over, e);
            Pose.Height = Mathf.Lerp(_hFrom, _p.Pose.Height + 0.95f * _k, e);
            Pose.Rot = -38f * e;
            Pose.SX = Mathf.Lerp(0.8f, 1f, e);
            Pose.SY = Mathf.Lerp(1.3f, 1f, e);
            if (Visible)
            {
                Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Ghost, 0.6f), _pal.GhostEnd);
                if (Level >= 3 && !_glinted && k >= 0.7f)
                {
                    // above it, heel raised: "ting!"
                    _glinted = true;
                    Fx.Glint(HeadPos() + new Vector2(_fwd * 3f * Px, 2f * Px), Px, 0.18f, _pal.Core, 1.1f);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Axe);
            }
        }

        // ---------------------------------------------------------------- axe --

        private void Axe(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                if (Visible)
                {
                    Kit.Swish(this, Art.Arc(11, 1f, 130f, -65f, 3), 0.16f, new Vector2(_fwd * 1.5f, -1f), _fwd, _pal.Core, _pal.Edge);
                }
            }
            float k = Mathf.Clamp01(_pt / TAxe);
            Pose.Rot = Mathf.Lerp(-38f, 110f, Anim.OutCubic(k));
            Pose.Height = _hFrom - 0.2f * _k * Anim.OutQuad(k);
            Pose.SX = 1f;
            Pose.SY = 1f;
            if (!_hit && k >= 0.4f && !_vGone)
            {
                _hit = true;
                Vector2 at = VBody;
                HitResult r = Blow(_mAxe, false, 2, new Vector2(_fwd * 0.4f, -1f), 10);
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                if (Visible)
                {
                    // the starburst BEHIND the pair: the heel coming down on the enemy is the key
                    // pose, and over them the burst hid it for the whole beat (r2_rising_tempest_2
                    // 0051-0053, _3 0051-0053); the enemy is a flat shape against it for that instant
                    bool black = false;
                    if (Level >= 3)
                    {
                        // III: an impact frame the world freezes on (a Black Flash on a critical blow)
                        black = r.Crit;
                        MeteorSlam.ImpactFrame(at, px * _k, 0.6f, _pal, black);
                        Fx.FocusLines(at, px, Fx.Count(18), 16f * _k, 34f * _k, 0.16f);
                        Shake(0.24f, 0.01f);
                        Kit.Stop(at, 0.1f);
                    }
                    else
                    {
                        // II: the starburst, held by the pair's own hit-stop (a world freeze is III's)
                        Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * 0.55f * _k, 0.09f, FxLayer.Behind, 10);
                        if (burst != null)
                        {
                            burst.Rot = Random.Range(0f, 360f);
                            burst.Sized(0.9f, 1.15f).Color(Color.white, new Color(1f, 0.96f, 0.7f, 0.85f), new Color(1f, 0.85f, 0.5f, 0f));
                        }
                        Fx.FocusLines(at, px, Fx.Count(14), 16f * _k, 30f * _k, 0.14f);
                        Shake(0.2f, 0.008f);
                    }
                    if (_p != null && !_vGone && (black || Tiers.Of(_v) == Tier.None))
                    {
                        // (a tiered enemy keeps its own colour's flash from Blow)
                        _p.Flash(0.08f, black ? Color.white : ImpactDark);
                    }
                }
                _hold = _holdAxe;
                VSpike(0.16f);
            }
            if (k >= 1f)
            {
                Next(Level >= 3 && !_vGone ? Phase.Rebound : Phase.Descent);
            }
        }

        // ------------------------------------------------- III: the Tempest Drop --

        /// <summary>Bounces off the blow, flips once more at the apex (glint), and ends heel-up over the crater.</summary>
        private void Rebound(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                _over = Combat.Clamp(_vGround - new Vector2(_fwd * 0.12f * _k, 0f));
                _glinted = false;
            }
            float k = Mathf.Clamp01(_pt / _tRebound);
            Pose.Height = _hFrom + 0.6f * _k * Mathf.Sin(Mathf.PI * 0.5f * Anim.OutQuad(k));
            Pose.Ground = Vector2.Lerp(_aFrom, _over, Anim.InOutSine(k));
            // one more flip, ending with the heel raised (-30)
            Pose.Rot = Mathf.Lerp(_rotFrom, 690f, Anim.InOutCubic(k));
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.08f * tuck;
            Pose.SY = 1f - 0.2f * tuck;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.03f;
                    Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Ghost, 0.7f), _pal.GhostEnd);
                }
                if (!_glinted && k >= 0.55f)
                {
                    _glinted = true;
                    Fx.Glint(HeadPos() + new Vector2(_fwd * 3f * px, 2f * px), px, 0.22f, _pal.Core, 1.25f);
                }
                _ember -= dt;
                if (_ember <= 0f && k > 0.6f)
                {
                    // it catches fire for the drop
                    _ember = 0.03f;
                    Kit.Ember(BodyPos() + Random.insideUnitCircle * 3f * px, px, 0.35f, Random.insideUnitCircle * 10f * px, Fire2);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Drop);
            }
        }

        /// <summary>The heel drop, straight down into the crater, trailing fire.</summary>
        private void Drop(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                _lastTrail = BodyPos();
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    Fx.Ring(BodyPos(), px, 0.16f, 3f, 12f * _k, 2, 1, FxLayer.Top, 3, 0.35f, 2.5f,
                        Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                }
            }
            float k = Mathf.Clamp01(_pt / TDrop);
            float e = k * k;
            Pose.Height = _hFrom * (1f - e);
            Pose.Ground = _over;
            Pose.Rot = Mathf.Lerp(-30f, 10f, Anim.OutQuad(k));
            float s = Mathf.Clamp01(k * 2.5f);
            Pose.SX = Mathf.Lerp(0.85f, 0.7f, s);
            Pose.SY = Mathf.Lerp(1.25f, 1.55f, s);
            if (Visible)
            {
                DropTrail(px);
                Fx.Afterimage(this, 0.1f, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(Fire3, 0f));
            }
            _lastTrail = BodyPos();
            if (k >= 1f)
            {
                Next(Phase.Quake);
            }
        }

        /// <summary>Fire along the drop since last frame: a hot core, flames cooling to red, a little smoke.</summary>
        private void DropTrail(float px)
        {
            Vector2 now = BodyPos();
            float step = 1.5f * px / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 24);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.7f * px;
                Particle core = Fx.Spawn(Art.Puffs[0], at, px, 0.08f, FxLayer.Behind, 3);
                core?.Sized(0.7f, 0.35f).Color(White, Fx.WithA(Fire1, 0.7f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.14f, 0.2f), FxLayer.Behind, 2);
                flame?.Moving(Random.insideUnitCircle * 5f * px, 6f * px, 2f)
                    .Sized(0.7f, 0.45f)
                    .Color(Fx.WithA(Fire1, 0.95f), Fx.WithA(Fire2, 0.85f), Fx.WithA(Fire3, 0.45f), Fx.WithA(Fire3, 0f));
                if (Random.value < 0.15f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0);
                    smoke?.Moving(Random.insideUnitCircle * 4f * px, 8f * px, 1.5f)
                        .Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.3f), Fx.WithA(Smoke, 0f));
                }
            }
        }

        /// <summary>The drop lands: the last blow, the crater grows and glows, the shockwave knocks the others down.</summary>
        private void Quake(float dt)
        {
            if (Enter())
            {
                _dropped = true;
                Pose.Ground = _over;
                Pose.Height = 0f;
                Pose.SX = 1.36f;
                Pose.SY = 0.64f;
                Pose.Rot = 6f;
                Vector2 at = _vGround;
                WorldTile tile = Combat.TileAt(at) ?? A.current_tile;
                bool crit = false;
                if (!_vGone && _p != null)
                {
                    if (_vs == VState.Spike || _vs == VState.Rise || _vs == VState.Float)
                    {
                        Crash();   // a slow frame: it reaches the ground right now
                    }
                    if (!_vGone && _v.isAlive())
                    {
                        crit = Blow(_mDrop, true, -1, Vector2.up, 0).Crit;
                        _vs = VState.Final;
                        _vt = 0f;
                        if (!_p.Finished && Tiers.Of(_v) == Tier.None && Fx.OnScreen(at))
                        {
                            // under the heel, a flat shape against the flash behind (white on a Black Flash)
                            _p.Flash(0.07f, crit ? Color.white : ImpactDark);
                        }
                    }
                }
                Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile);
                Anim.Sound("event:/SFX/HIT/HitStone", tile);
                if (Fx.OnScreen(at))
                {
                    QuakeFx(at, tile, crit);
                }
                Shockwave(at);
                // the world takes it on the same frame: the spike's crater grows into a burning one
                // (its fire takes only the rim; the spot it hops out to is kept whole: neither dug nor
                // set alight. A kept spot is never dug, so it is the spot beside the crater, never
                // the ground it stands on in the crater's middle)
                _hopTo = HopTarget();
                int force = MeteorSlam.RavageForce(3, A);
                float r = 1.7f * _k * MeteorSlam.RavageScale(A);
                MeteorSlam.Ravage(at, r, force, A, _hopTo, 0.8f);
                if (Dev.On) Dev.Trace("tempest " + Id + " ravage force " + force + " r " + r.ToString("F1") + ", shockwave " + _quaked);
            }
            Pose.Nudge = new Vector2(0f, ((int)(_pt * 60f) & 1) == 0 ? 0.5f : 0f);
            if (_pt >= TQuake)
            {
                Pose.Nudge = Vector2.zero;
                Next(Phase.HopOut);
            }
        }

        private void QuakeFx(Vector2 at, WorldTile tile, bool crit)
        {
            float px = Px;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.45f);
            // the flash behind the drop's landing (over it, it hid the squash on the enemy for the
            // whole beat, r2_rising_tempest_3 0070-0071)
            Kit.Flash(at + new Vector2(0f, 2f * px), px * _k, 11, 0.09f, White, FxLayer.Behind, 8);
            if (crit)
            {
                MeteorSlam.ImpactFrame(at + new Vector2(0f, 3f * px), px * _k, 0.8f, _pal, true);
            }
            // the crater grows to 1.5 tiles, glowing in the tier's colour (on water: a splash)
            Kit.Crater(at, px * _k, tile, true, 8f, false, _pal.Edge);
            float r = Mathf.Max(4f, 1.5f * _area * _k / px);
            Fx.Ring(at, px, 0.34f, 4f, r, 3, 1, FxLayer.Ground, 7, Art.Ground, 3f,
                Fx.WithA(White, 1f), Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
            Particle ring2 = Fx.Ring(at, px, 0.6f, 4f, r * 1.25f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            if (ring2 != null)
            {
                ring2.Delay = 0.05f;
            }
            // dust out to the edge of the shockwave (drag 4: it travels speed / 4)
            Kit.DustRing(at, px, tile, 16, 1.5f * _area * 4.2f / px, 0.6f, _k);
            if (tile == null || tile.Type == null || !tile.Type.liquid)
            {
                MeteorSlam.Rocks(at, px, Fx.SoilColor(tile), Fx.TopColor(tile), 10, _k, 1f);
            }
            Kit.Sparks(at + new Vector2(0f, 2f * px), px, 10, 90f, 160f, Fire1);
            int embers = Fx.Count(8);
            for (int i = 0; i < embers; i++)
            {
                Kit.Ember(at + Random.insideUnitCircle * 5f * px, px, Random.Range(0.8f, 1.4f), new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 28f)) * px, _pal.Edge);
            }
            Shake(0.3f, 0.012f);
        }

        /// <summary>
        /// Everyone else within 1.5 tiles (a God's 1.95) is knocked down (x1.8 and a small throw). An
        /// area blow can't be parried: whoever answers it in any way is skipped; the too-heavy only stagger.
        /// </summary>
        private void Shockwave(Vector2 at)
        {
            Actor a = A;
            foreach (Actor u in Combat.FoesAround(a, at, 1.5f * _k * _area))
            {
                if (u == _v || Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                _quaked++;
                if (!Combat.Allowed(a, u, out float classMult))
                {
                    Combat.Hit(a, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                // (thrown off its feet; one the shockwave would have killed dies as it hits the ground,
                // a body flung from the crater rather than one dropping dead beside it)
                Combat.Hit(a, u, 1.8f * classMult, false, true, 0f, this);
                if (u.isAlive())
                {
                    bool doomed = MeteorSlam.AtDeathsDoor(u);
                    Combat.Launch(u, at, 1f, 0.6f, (Random.value < 0.5f ? -1f : 1f) * Random.Range(300f, 500f));
                    if (doomed)
                    {
                        MeteorSlam.Doom(a, u, this);
                    }
                }
            }
        }

        /// <summary>Where the back-flip out of the crater lands: beside it, on solid ground.</summary>
        private Vector2 HopTarget()
        {
            Vector2 want = Combat.Clamp(_vGround - new Vector2(_fwd * 0.8f * _k, 0f));
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            if (Combat.SafeSpot(Pose.Ground))
            {
                return Pose.Ground;
            }
            return Combat.FindLanding(want, 2f, out Vector2 spot) ? spot : _aStart;
        }

        /// <summary>A back-flip out of the crater, to land beside it.</summary>
        private void HopOut(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                // (chosen on the quake's frame, before the ground broke, and kept whole by it)
                _aTo = _hopTo ?? HopTarget();
            }
            float k = Mathf.Clamp01(_pt / _tHopOut);
            Pose.Height = 0.45f * _k * Mathf.Sin(Mathf.PI * k);
            Pose.Ground = Vector2.Lerp(_aFrom, _aTo, Anim.InOutSine(k));
            Pose.Rot = -360f * Anim.InOutCubic(k);
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.06f * tuck;
            Pose.SY = 1f - 0.14f * tuck;
            if (Visible && k < 0.8f)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.035f;
                    Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Ghost, 0.5f), _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Land);
            }
        }

        // ------------------------------------------------------------ descent --

        private void Descent(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                Vector2 want;
                if (Level <= 1 && _finisher)
                {
                    // Rising Kick: it drifts down after the enemy it kicked away
                    want = Slide(Pose.Ground, _fwd, 0.25f * _k);
                }
                else
                {
                    want = Combat.Clamp((_vGone ? Pose.Ground : _vGround) - new Vector2(_fwd * 0.7f * _k, 0f));
                }
                _aTo = want;
                if (!Combat.SafeSpot(_aTo))
                {
                    if (Combat.SafeSpot(_vGround))
                    {
                        _aTo = _vGround;
                    }
                    else
                    {
                        _aTo = Combat.FindLanding(want, 2f, out Vector2 spot) ? spot : _aStart;
                    }
                }
                _hFrom = Pose.Height;
                _hTo = Pose.Height + 0.3f * _k;
                _rotFrom = Pose.Rot;
            }
            float k = Mathf.Clamp01(_pt / _tDescent);
            // a little recoil up off the blow, then a calm float down
            float up = Anim.OutQuad(Mathf.Clamp01(k / 0.18f));
            float down = Anim.InOutSine(Mathf.Clamp01((k - 0.18f) / 0.82f));
            Pose.Height = Mathf.Max(0f, Mathf.Lerp(Mathf.Lerp(_hFrom, _hTo, up), 0f, down));
            Pose.Ground = Vector2.Lerp(_aFrom, _aTo, Anim.InOutSine(k));
            // one more flip on the way down (Rising Kick just tucks), then upright
            float flipTo = Level <= 1 ? 0f : 360f;
            Pose.Rot = Mathf.Lerp(_rotFrom, flipTo, Anim.OutCubic(Mathf.Clamp01(k / 0.5f)))
                + 4f * Mathf.Sin(_pt * 14f) * Anim.Smooth(0.5f, 0.7f, k) * (1f - k);   // a little sway as it floats
            float tuck = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.5f));
            Pose.SX = 1f - 0.05f * tuck;
            Pose.SY = 1f - 0.12f * tuck;
            if (Visible && k < 0.5f)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.045f;
                    Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Ghost, 0.45f), _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Land);
            }
        }

        private void Land(float dt)
        {
            float px = Px;
            bool drop = Level >= 3 && _dropped;
            if (Enter())
            {
                Pose.Height = 0f;
                if (drop)
                {
                    // the Tempest Drop's last frame: its back to the crater
                    Pose.FaceRight = _fwd < 0f;
                }
                if (Visible)
                {
                    Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                    Color glint = Level == 2 ? new Color(1f, 0.98f, 0.85f, 1f) : _pal.Core;
                    float side = Pose.FaceRight ? 1f : -1f;
                    Fx.Glint(BodyPos() + new Vector2(side * 3f * px, Pose.PivotUp * 1.4f * px), px, 0.24f, glint, 1f);
                }
            }
            float amp = Anim.Spring(_pt * _landSpeed, 9f, 18f);
            Pose.SX = 1f + 0.16f * amp;
            Pose.SY = 1f - 0.18f * amp;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            if (drop && Visible)
            {
                // embers rise from the crater behind it
                _ember -= dt;
                if (_ember <= 0f)
                {
                    _ember = 0.06f;
                    Vector2 at = _vGround + new Vector2(Random.Range(-4f, 4f), Random.Range(-1.5f, 1.5f)) * px * _k;
                    Kit.Ember(at, px, Random.Range(0.7f, 1.2f), new Vector2(Random.Range(-3f, 3f), Random.Range(10f, 20f)) * px, _pal.Edge);
                }
            }
            if (_pt >= _tLand)
            {
                Pose.SX = Pose.SY = 1f;
                if (_p != null && !_vGone && !_p.Finished)
                {
                    if (_vs == VState.Bounce || _vs == VState.Lie || _vs == VState.Final)
                    {
                        // (a crisp Flow landing can beat the enemy's last bounce): it is down, so
                        // it is handed back down, dazed, and the chain goes on from there
                        Pose vp = _p.Pose;
                        vp.Height = 0f;
                        vp.SX = vp.SY = 1f;
                        Release(vp);
                    }
                    else
                    {
                        _p.Release(true);
                    }
                }
                if (Level <= 1 && _finisher && _v != null && _v.isAlive() && _v.Sid() == _vId)
                {
                    // Rising Kick leaves it in the air or just landed, dazed
                    EndLink = _v.position_height > 0.25f ? Link.Launched : Link.Down;
                    EndTarget = _v;
                }
                if (EndLink == Link.None)
                {
                    // the enemy is finished (or gone): the shockwave's crowd, or whoever is right
                    // there, so the next technique follows straight on
                    if (_quaked >= 2)
                    {
                        EndLink = Link.Scattered;
                        EndTarget = null;
                        if (Dev.On) Dev.Trace("tempest " + Id + " leaves Scattered");
                    }
                    else
                    {
                        LinkClose(null);
                    }
                }
                Cooldown();
                if (_finisher)
                {
                    Finished_++;
                }
                Finished = true;
            }
        }

        // ------------------------------------------------------------- victim --

        private void VRise(float toHeight, float duration, float drift, float spin)
        {
            if (_vGone)
            {
                return;
            }
            _vs = VState.Rise;
            _vt = 0f;
            _vDur = duration;
            _vh0 = _p.Pose.Height;
            _vh1 = toHeight;
            _vg0 = _vGround;
            _vg1 = _vGround + new Vector2(drift, 0f);
            _vSpin = spin;
        }

        private void VFloat(float spin)
        {
            if (_vGone || _vs == VState.Rise)
            {
                _vSpin = spin;
                return;
            }
            _vs = VState.Float;
            _vSpin = spin;
        }

        private void VSpike(float duration)
        {
            if (_vGone)
            {
                return;
            }
            _vs = VState.Spike;
            _vt = 0f;
            _vDur = duration;
            _vh0 = _p.Pose.Height;
            _vSpin = 0f;
        }

        /// <summary>Rising Kick: the enemy is let go in mid-air, to fall and tumble on the game's own physics.</summary>
        private void LetGo()
        {
            if (_vGone || _p == null)
            {
                return;
            }
            _vGone = true;
            _vs = VState.Done;
            _p.Release(false);
            _tumble = _v.isAlive();
            _tumbleTries = 0;
        }

        /// <summary>Once the puppet is handed back (a frame later), the kick's push and a tumble.</summary>
        private void TumbleAway()
        {
            if (_v == null || !_v.isAlive() || _v.Sid() != _vId)
            {
                _tumble = false;
                return;
            }
            if (Moves.Locked(_v))
            {
                if (++_tumbleTries > 4)
                {
                    _tumble = false;
                }
                return;
            }
            _tumble = false;
            if (_v.position_height <= 0.05f)
            {
                return;
            }
            Combat.LaunchDir(_v, new Vector2(_fwd, 0f), 0.8f * _k, 0.35f * _k, _fwd > 0f ? -720f : 720f);
            if (_v.isAlive())
            {
                Daze(_v, 1.2f);
            }
        }

        /// <summary>
        /// Stunned, without the game's white status flash: makeStunned lights the whole unit up
        /// white whenever no blow has flashed it for 0.3 s, so a downed enemy handed back after its
        /// bounce turned into a white silhouette half a beat after the last hit (r2_rising_tempest_2
        /// 0066-0070), a pop that read as one more blow.
        /// </summary>
        private static void Daze(Actor v, float seconds)
        {
            if (v == null || !v.isAlive())
            {
                return;
            }
            // (makeStunned, less the colour effect)
            v.cancelAllBeh();
            v.makeWait(seconds);
            if (v.addStatusEffect("stunned", seconds, false))
            {
                v.finishAngryStatus();
            }
        }

        /// <summary>Moves the enemy: launched, hanging, spiked, bouncing off the crater, lying in it.</summary>
        private void Victim(float dt)
        {
            Pose vp = _p.Pose;
            _vt += dt;
            float lying = _fwd > 0f ? 90f : -90f;
            switch (_vs)
            {
                case VState.Stand:
                    vp.Ground = _vGround;
                    vp.Height = 0f;
                    break;
                case VState.Rise:
                    {
                        float k = Mathf.Clamp01(_vt / _vDur);
                        vp.Height = Mathf.Lerp(_vh0, _vh1, Anim.OutCubic(k));
                        Vector2 drift = Vector2.Lerp(_vg0, _vg1, Anim.OutQuad(k));
                        if (Combat.SafeSpot(drift))
                        {
                            _vGround = drift;
                        }
                        vp.Ground = _vGround;
                        float s = Mathf.Clamp01(k * 2.5f);
                        vp.SX = Mathf.Lerp(0.85f, 1f, s);
                        vp.SY = Mathf.Lerp(1.2f, 1f, s);
                        vp.Rot += _vSpin * dt * (1f - 0.7f * k);
                        if (k >= 1f)
                        {
                            _vs = VState.Float;
                        }
                        break;
                    }
                case VState.Float:
                    vp.Height += 0.55f * _k * dt;
                    vp.Ground = _vGround;
                    _vSpin = Mathf.MoveTowards(_vSpin, 0f, 600f * dt);
                    vp.Rot += _vSpin * dt;
                    vp.SX = vp.SY = 1f;
                    break;
                case VState.Spike:
                    {
                        float k = Mathf.Clamp01(_vt / _vDur);
                        vp.Height = _vh0 * (1f - k * k);
                        vp.Ground = _vGround;
                        vp.Rot = Mathf.MoveTowardsAngle(vp.Rot, 180f, 1500f * dt);
                        vp.SX = 0.82f;
                        vp.SY = 1.3f;
                        if (Visible)
                        {
                            Fx.Afterimage(_p, 0.1f, new Color(1f, 1f, 1f, 0.6f), new Color(1f, 0.8f, 0.6f, 0f));
                            Vector2 top = _p.BodyPos() + new Vector2(Random.Range(-3f, 3f) * Px, Random.Range(4f, 9f) * Px);
                            Particle l = Fx.Spawn(Art.Lines[2], top, Px, 0.08f, FxLayer.Top, 1);
                            l?.Color(new Color(1f, 1f, 1f, 0.75f), new Color(1f, 1f, 1f, 0f));
                        }
                        if (k >= 1f)
                        {
                            _p.Pose = vp;
                            Crash();
                            vp = _p.Pose;
                        }
                        break;
                    }
                case VState.Bounce:
                    {
                        float k = Mathf.Clamp01(_vt / 0.3f);
                        vp.Height = 0.3f * _k * Mathf.Sin(Mathf.PI * k) * (1f - 0.3f * k);
                        vp.Rot = Mathf.LerpAngle(180f, lying, Anim.OutQuad(k));
                        float amp = Anim.Spring(_vt, 10f, 20f);
                        vp.SX = 1f + 0.2f * amp;
                        vp.SY = 1f - 0.25f * amp;
                        if (k >= 1f)
                        {
                            vp.Height = 0f;
                            vp.SX = vp.SY = 1f;
                            if (Level >= 3 && !_dropped)
                            {
                                // Tempest Drop: it stays down in the crater for what comes next
                                _vs = VState.Lie;
                                _vt = 0f;
                                break;
                            }
                            Release(vp);
                            return;
                        }
                        break;
                    }
                case VState.Lie:
                    vp.Ground = _vGround;
                    vp.Height = 0f;
                    vp.Rot = lying;
                    vp.SX = 1f;
                    vp.SY = 1f - 0.05f * Mathf.Sin(_vt * 30f) * Mathf.Exp(-3f * _vt);   // a twitch
                    break;
                case VState.Final:
                    {
                        // the heel drop lands on it: one last small bounce
                        float k = Mathf.Clamp01(_vt / 0.24f);
                        vp.Ground = _vGround;
                        vp.Height = 0.18f * _k * Mathf.Sin(Mathf.PI * k);
                        vp.Rot = lying;
                        float amp = Anim.Spring(_vt, 10f, 22f);
                        vp.SX = 1f + 0.22f * amp;
                        vp.SY = 1f - 0.28f * amp;
                        if (k >= 1f)
                        {
                            vp.Height = 0f;
                            vp.SX = vp.SY = 1f;
                            Release(vp);
                            return;
                        }
                        break;
                    }
            }
            _p.Pose = vp;
        }

        /// <summary>Hands the enemy back on the ground, dazed (the chain goes on from Down).</summary>
        private void Release(Pose vp)
        {
            _vs = VState.Done;
            _p.Pose = vp;
            _p.Release(true);
            _vGone = true;
            if (_v.isAlive())
            {
                Daze(_v, 2.2f);
                EndLink = Link.Down;
                EndTarget = _v;
            }
        }

        /// <summary>
        /// The enemy hits the ground: a small crater, the finishing damage (at III not yet: the
        /// Tempest Drop is still to come) and a jolt for any of its friends standing too close.
        /// </summary>
        private void Crash()
        {
            float px = Px;
            Vector2 at = _vGround;
            WorldTile tile = Combat.TileAt(at) ?? _v.current_tile;
            _vs = VState.Bounce;
            _vt = 0f;
            bool last = Level < 3;
            HitResult r = Blow(_mCrash, last, -1, Vector2.up, 0);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            if (Fx.OnScreen(at))
            {
                Color soil = Fx.SoilColor(tile);
                Color dust = Fx.Lighten(soil, 0.45f);
                // (behind the enemy hitting the ground, so it reads against the flash)
                if (last && r.Crit)
                {
                    MeteorSlam.ImpactFrame(at + new Vector2(0f, 3f * px), px * _k, 0.7f, _pal, true);
                }
                else
                {
                    Particle flash = Fx.Spawn(Art.Disk(9, 0.8f, 0.4f), at + new Vector2(0f, 2f * px), px * _k, 0.08f, FxLayer.Behind, 8);
                    flash?.Sized(0.8f, 1.2f).Color(Color.white, new Color(1f, 0.95f, 0.75f, 0f));
                }
                if (_p != null && !_p.Finished && Tiers.Of(_v) == Tier.None)
                {
                    // a white hit flash vanished into the white flash behind it: a flat dark shape
                    // instead (white against a Black Flash); a tiered enemy keeps its colour's flash
                    _p.Flash(0.07f, last && r.Crit ? Color.white : ImpactDark);
                }
                if (tile == null || tile.Type == null || !tile.Type.liquid)
                {
                    Art.CrackSet set = Art.Cracks[Random.Range(0, Art.Cracks.Length)];
                    Particle d = Fx.Spawn(set.Dark, at, px * 0.55f * _k, 4f, FxLayer.Ground, 1);
                    if (d != null)
                    {
                        d.Snap = true;
                        Color dark = Fx.Darken(soil, 0.6f);
                        d.Color(Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0.8f), Fx.WithA(dark, 0f));
                    }
                    Particle l = Fx.Spawn(set.Light, at, px * 0.55f * _k, 4f, FxLayer.Ground, 2);
                    if (l != null)
                    {
                        l.Snap = true;
                        Color light = Fx.Lighten(soil, 0.35f);
                        l.Color(Fx.WithA(light, 0.85f), Fx.WithA(light, 0.85f), Fx.WithA(light, 0.75f), Fx.WithA(light, 0f));
                    }
                }
                else
                {
                    Kit.Splash(at, px, tile, 0.8f * _k);
                }
                Fx.Ring(at, px, 0.32f, 4f, 32f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                    new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
                int puffs = Fx.Count(14);
                for (int i = 0; i < puffs; i++)
                {
                    float a = (i + Random.Range(-0.3f, 0.3f)) / puffs * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(1, 5)], at + dir * 2f * px, px, Random.Range(0.55f, 0.85f),
                        dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                    p?.Moving(dir * Random.Range(50f, 85f) * px * _k, Random.Range(3f, 8f) * px, 2.6f)
                        .Sized(0.45f, 1.15f)
                        .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.55f), Fx.WithA(dust, 0.2f), Fx.WithA(dust, 0f));
                }
                if (tile == null || tile.Type == null || !tile.Type.liquid)
                {
                    // rocks from the rim of the little crater, not up through the enemy lying in it
                    MeteorSlam.Rocks(at, px, soil, Fx.TopColor(tile), 9, _k, 0.8f);
                }
                Shake(0.25f, 0.01f);
            }
            // a jolt for any of its friends standing too close (an area blow: it can't be parried)
            float splash = 1.2f * _k * _area;
            foreach (Actor u in Combat.FoesAround(A, at, splash))
            {
                if (u == _v || Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float classMult))
                {
                    Combat.Hit(A, u, 0.3f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                Combat.Hit(A, u, 0.3f * classMult, true, true, 0f, this);
                Combat.Launch(u, at, 1.6f * _k, 1.1f * _k, Random.value < 0.5f ? -500f : 500f);
            }
            // and the world takes it on the same frame: a real crater where it was spiked (a God's
            // burns at the rim); the spot beside it where the unit comes down (II) or hops out to
            // (III) is kept whole (a kept spot is never dug: never the crater's middle)
            int force = MeteorSlam.RavageForce(2, A);
            float crater = _k * MeteorSlam.RavageScale(A);
            MeteorSlam.Ravage(at, crater, force, A, Combat.Clamp(at - new Vector2(_fwd * 0.75f * _k, 0f)), 0.8f);
            if (Dev.On) Dev.Trace("tempest " + Id + " spike ravage force " + force + " r " + crater.ToString("F1"));
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>
        /// A blow lands on the enemy: hit star, sparks, a flash (a tiered enemy flashes its own tier's
        /// colour on the heavy ones), damage. Only a 'lethal' blow may finish it; the others always
        /// leave it standing (well, floating) for the next. size -1: no hit star (the crash has its own effects).
        /// </summary>
        private HitResult Blow(float mult, bool lethal, int size, Vector2 away, int sparks)
        {
            if (_vGone || _p == null || !_v.isAlive())
            {
                return default;
            }
            Vector2 at = VBody + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1.5f)) * Px;
            if (size >= 0 && Visible)
            {
                Fx.HitSpark(at, Px, size, away, sparks);
            }
            bool heavy = size >= 2 || size < 0;
            Tier tier = Tiers.Of(_v);
            _p.Flash(heavy ? 0.07f : 0.04f, heavy && tier != Tier.None ? Tiers.Color(tier) : (Color?)null);
            HitResult r = Combat.Hit(A, _v, mult * _cm, lethal, true, 0f, this);
            if (lethal)
            {
                _finisher = true;
            }
            if (Dev.On)
            {
                Dev.Trace("tempest " + Id + " blow x" + (mult * _cm) + " -> " + _v.asset.id + " hp " + _v.getHealth() + (lethal ? " (finisher)" : "") + (r.Crit ? " CRIT" : ""));
            }
            return r;
        }

        private void Shake(float duration, float strength)
        {
            Camera cam = World.world.camera;
            if (cam == null || cam.orthographicSize > 60f || !Fx.OnScreen(Pose.Ground, 0.05f))
            {
                return;
            }
            World.world.startShake(duration, 0.02f, cam.orthographicSize * strength * _k, true, true);
        }

        public override void Abort()
        {
            _tumble = false;
            if (_p != null && !_vGone && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
        }
    }

    /// <summary>
    /// Rising Tempest as a technique. Entry: Adept (I Adept, II Expert, III Master and up). The AI
    /// opens with it on an enemy right next to it that it can throw around, that still has some
    /// fight in it, that it can reach over solid ground, and that isn't in the middle of a crowd
    /// (a juggle in a crowd gets interrupted; III, whose Tempest Drop knocks down whoever stands
    /// close, minds a crowd less and likes a few of the enemy's friends around it). It reads the
    /// enemy's guard: into a ready Mirage Step or Still Water it is a gamble it seldom takes, right
    /// after the enemy has spent that answer it is the punish.
    /// </summary>
    internal sealed class RisingTempestTech : Technique
    {
        public override string Id => "rising_tempest";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Launcher;

        /// <summary>Kicked into the sky, chased, a flurry in the air, then an axe kick straight down.</summary>
        public override Form Forms => Form.Kick | Form.Rising | Form.Aerial | Form.Flurry | Form.Overhead;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Light : Scale.Heavy;

        public override float[] ManaCost => new[] { 20f, 35f, 55f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };
        public override string Icon => "ui/Icons/sakuga/iconRisingTempest";

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor v = c.Focus;
            if (v == null || !RisingTempest.CanJuggle(v, c.Self) || !c.Allowed(v, out float cm) || v.isInLiquid())
            {
                return null;
            }
            float kSelf = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
            if (v.position_height > 1.2f * kSelf)
            {
                return null;   // high in the air (thrown, mid-dash at its peak): not for a rising kick
            }
            float reach = RisingTempest.Range + v.stats["size"] + (c.Linked ? 1f : 0f);
            if (c.Dist(v) > reach && !c.Forced)
            {
                return null;
            }
            int near = 0;
            if (!c.Forced || level >= 3)
            {
                near = c.CountNear(v.current_position, 2f);
            }
            if (!c.Forced)
            {
                if (Combat.HpPct(v) < 0.35f && !c.Linked)
                {
                    return null;   // not worth a whole combo
                }
                if (near > (level == 2 ? 3 : 4))
                {
                    return null;
                }
            }
            if (!RisingTempest.CanReach(c.Self, v))
            {
                return null;   // nowhere solid to stand beside it, or water in the way
            }
            float total = level <= 1 ? 2f : level == 2 ? 3.7f : 6.8f;
            float score = c.Value(v, c.Expected(v, total), cm) + (Tiers.Of(v) != Tier.None ? 25f : 10f);
            if (level >= 3)
            {
                score += Mathf.Clamp(near - 1, 0, 3) * 10f;   // the shockwave's share
            }
            if (RisingTempest.Ranged(v))
            {
                // an archer that keeps hopping back out of reach: the launcher catches it and it can't
                // shoot from up there (the right answer to a skirmisher in your face)
                score += 15f;
            }
            if (v.position_height > 0.05f)
            {
                score += 10f;   // caught mid-hop: the moment it can't dodge
            }
            if (!c.Forced)
            {
                // the whole combo hangs on the launcher landing: into a guard that is up (a ready
                // Mirage Step or Still Water) it is a gamble (round 2: 10 of 22 launchers were
                // slipped or parried), right after that answer is spent it is the punish
                int guard = Guard(v);
                if (guard > 0)
                {
                    score *= 0.6f;
                }
                else if (guard < 0)
                {
                    score += 20f;
                }
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = score };
        }

        /// <summary>
        /// Its guard: 1 up (a reactive technique it knows is ready and paid for), -1 down (it knows
        /// one, but has just used it), 0 none. Cheap: a few lookups per reactive technique.
        /// </summary>
        private static int Guard(Actor v)
        {
            IReadOnlyList<Technique> all = Techniques.All;
            if (all == null)
            {
                return 0;
            }
            Fighters.TryGet(v, out Fighter f);
            bool knows = false;
            for (int i = 0; i < all.Count; i++)
            {
                Technique t = all[i];
                if (t == null || !t.Reactive)
                {
                    continue;
                }
                int level = Techniques.Level(v, t);
                if (level <= 0)
                {
                    continue;
                }
                knows = true;
                if ((f == null || f.Ready(t)) && Sakuga.Mana.Has(v, t, level, false))
                {
                    return 1;
                }
            }
            return knows ? -1 : 0;
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && RisingTempest.Begin(a, p.Target, p.Level);
        }
    }
}
