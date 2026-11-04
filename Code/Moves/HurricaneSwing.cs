using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HURRICANE SWING: "the human bowling ball". The unit grabs the enemy beside it, swings it
    /// round and round like a hammer thrower, and lets go toward a crowd: the body ploughs through
    /// its own friends. At level III the last turn hurls it straight up instead; the unit leaps
    /// after it, axe-kicks it at the top, and it comes down on the crowd as a burning meteor.
    ///
    /// Beat sheet (game seconds at x1; a big performer swings small victims 20% faster, a heavy
    /// victim makes a lighter performer strain and swing 15% slower):
    ///
    ///   STEP IN   ~0.1-0.3  only if the victim is not already within reach: slide in beside it
    ///   GRAB       0.15     a crouch, then the lunge (0.07): the victim answers or is caught (a crab
    ///                       pinches it in a claw), yanked off its feet to arm's length
    ///
    ///   I   SWING  0.40     one turn: both sprites turn (narrow edge-on, flip, widen), the victim
    ///                       held level, head out; one white crescent, dust scuffs
    ///       THROW  ~0.3     let go toward the pin (a single enemy is enough): a 3-tile lob; anyone
    ///                       it hits takes x0.5 and is knocked down
    ///       LAND   0.30     it hits the ground (x1), bounces once, rolls, sits up dazed
    ///                                                                              ~1.2 s in all
    ///   II  SWING  0.88     three accelerating turns at 0.6 tiles: a ring of tier-tinted crescents,
    ///                       tinted afterimages of the victim, a 1-tile dust ring
    ///       THROW  ~0.45    let go toward the biggest crowd 3-8 tiles away: the body flies low
    ///                       (0.5 tiles up) for about 6 tiles, spinning, trailing speed lines and a
    ///                       furrow of dust, ploughing a real trench into the ground behind it;
    ///                       every enemy in the way is bowled over (x1, a hit beat, knocked
    ///                       tumbling); a wall stops it short with a crunch
    ///       LAND   0.42     it hits (x2): a small crack and a real crater, a dust burst, a short
    ///                       shake; it bounces twice, slides, sits up dazed         ~1.9 s in all
    ///       PURSUE          a Master does not stand and watch: as the body comes down it blinks
    ///                       after it down the trench (a tinted blur) and skids in beside it as it
    ///                       sits up, the dazed victim at its feet for the next technique (Down)
    ///   III SWING  0.95     five turns inside a tornado: a helix of dust, stacked crescents, embers;
    ///                       a dashed ring and a growing shadow mark where it will come down
    ///       TOSS   0.32     the last turn hurls the victim 6 tiles straight up, tumbling; the
    ///                       tornado's footprint is scorched into the ground
    ///       CHASE  0.13+0.30  a crouch, a leap after it on a trail of afterimages
    ///       AXE    0.08+0.12  heel raised over it, then down: impact frame, the pair hangs for a beat
    ///       METEOR 0.30     it falls on the crowd trailing fire and smoke
    ///       CRASH           a glowing 1-tile crater, a 2-tile double shockwave, a dust wall, rocks,
    ///                       embers, a global hit-stop: x2-2.6 to everyone within 2 tiles (harder
    ///                       at the middle, launched), x4.2 to the victim in all (x1.2 kick + x3
    ///                       crash). The ground is blasted into a real crater with a burning core
    ///                       (never where the unit itself comes down, nor on its own people); a
    ///                       Combat God's is half again as wide, bites deeper, and its ejecta rain
    ///                       down in smaller craters all round
    ///       DESCENT 0.42 + LAND 0.24  the unit flips and floats down, lands, "ting"  ~2.45 s in all
    ///
    /// Power scales it: the recover beats (follow-through, tumble, descent, landing, a whiff) play
    /// TierInfo.Linger of their time (an Expert's 0.85, a God's 0.42); a stronger thrower whirls
    /// faster (a Master's swing in 0.87 of the time, a God's in 0.83), throws further and knocks
    /// harder (SpinWreck.Scale: a Grandmaster 1.15x, a God 1.3x: the lane, the knocks, the meteor's
    /// blast and its marker, a God's ejecta). The meteor's burning crater leaves only a pocket
    /// unburnt where the unit itself comes down.
    ///
    /// A mob pressing in on the thrower changes it: the whirling body bats aside every enemy that
    /// stands in the circle as it comes round (x0.35 / x0.5 / x0.6, knocked 0.9-1.5 tiles out the
    /// way the swing turns, a hit spark; the first one jolts the swing at II and III), except the
    /// pins standing in the lane it will be thrown down, or where the meteor will fall. At III the
    /// meteor is aimed again just before its marker shows, at the biggest group standing 1.5-7
    /// tiles away by then (the crowd moves, or was just batted aside).
    ///
    /// The victim only takes non-lethal blows until it hits the ground, so the throw always plays
    /// out; if it is killed by someone else or picked up by the god hand, the unit lets go and
    /// recovers. A victim lost before the grab is replaced by another enemy of its weight within
    /// 2 tiles, or the unit grabs air. Every flight ends on solid ground.
    /// </summary>
    internal sealed class HurricaneSwing : Move
    {
        /// <summary>What the planner found: who stands where the victim will be thrown.</summary>
        internal sealed class AimData
        {
            public Actor Center;
            public float Len;
        }

        private enum Phase
        {
            StepIn,
            Grab,
            Swing,
            Throw,
            Wait,
            Crouch,
            Chase,
            Overhead,
            Axe,
            Descent,
            Land,
            Whiff,
            Recoil,
            Pursue
        }

        private enum VState
        {
            Free,
            Held,
            Fly,
            Tumble,
            Up,
            Hang,
            Meteor,
            Bounce,
            Done
        }

        private const float TGrab = 0.15f;
        private const float TContact = 0.07f;
        private const float TFollow = 0.14f;
        private const float TToss = 0.08f;
        private const float TCrouch = 0.05f;
        private const float TChase = 0.30f;
        private const float TOverhead = 0.08f;
        private const float TAxe = 0.12f;
        private const float TDescent = 0.42f;
        private const float TLand = 0.24f;
        private const float TUp = 0.32f;
        private const float TMeteor = 0.30f;
        private const float TBounce = 0.32f;
        private const float TWhiff = 0.32f;
        private const float TRecoil = 0.3f;
        /// <summary>The swing is a circle on the ground plane, seen flattened.</summary>
        private const float Flat = 0.55f;

        private static readonly Color Pale = new Color(0.86f, 0.9f, 0.96f, 1f);
        private static readonly Color Ting = new Color(1f, 0.98f, 0.85f, 1f);
        private static readonly Color GhostCool = new Color(0.7f, 0.85f, 1f, 0f);

        private Actor _v;
        private long _vid;
        private Weight _vWeight;
        private Puppet _p;
        private bool _vGone;
        private float _vcm = 1f;

        private readonly Vector2 _start;
        private Vector2 _stand, _c;
        private float _fwd;
        private readonly float _k;
        private readonly Palette _pal;
        private readonly bool _big, _crab;
        private bool _strain, _critter, _heavy;
        private float _tStep;
        /// <summary>Where the current step-in starts (a second lunge after a victim that hopped back starts mid-way).</summary>
        private Vector2 _stepFrom;
        private int _lunges;
        /// <summary>How high the victim was when the hand closed on it (snatched mid-hop).</summary>
        private float _aFromH;
        private float _tSwing, _sweep, _sign, _phi0, _phi, _R;
        private int _turns;

        private Vector2 _aim;
        private readonly Actor _aimActor;
        private readonly long _aimId;
        private float _len;
        private Vector2 _dirThrow;

        private Phase _phase = Phase.StepIn;
        private float _pt;
        private bool _entered, _hit;
        private float _hold;
        private int _holds;
        private float _ghost, _lines, _puff, _helix, _nextTurn;
        private Vector2 _aFrom;
        private float _hFrom, _rotFrom;
        private Vector2 _landA;

        // the victim's own little state machine
        private VState _vs = VState.Free;
        private float _vt;
        private Vector2 _fFrom, _fTo, _slideTo, _lastFly, _lastTrail;
        private float _fDur, _fH0, _vh0;
        private bool _blocked, _trail;
        private Vector2 _upFrom, _upTo;
        private float _apexH;

        private readonly HashSet<long> _bowled = new HashSet<long>();
        private int _pins;

        /// <summary>Who the whirling body has already batted aside during the swing.</summary>
        private readonly HashSet<long> _clipped = new HashSet<long>();
        private int _clips;
        private float _phiLast;
        private const int MaxClips = 6;
        private bool _reAimed;

        private Particle _vShadow, _aShadow, _marker, _markerRing;

        /// <summary>A Master or above: one continuous flow (it pursues its throw, lands crisply, never waits on a weapon swing).</summary>
        private readonly bool _flow;
        /// <summary>Recover phases (the follow-through, the descent, the landing) play this much of their time (TierInfo.Linger: Expert 0.85 .. God 0.42).</summary>
        private readonly float _pace;
        /// <summary>How much bigger a stronger thrower's swing is (SpinWreck.Scale: the knocks, the meteor's blast and marker).</summary>
        private readonly float _scale;
        private readonly bool _god;
        /// <summary>How hard it tears up the ground (SpinWreck.Force) and how far (SpinWreck.Reach).</summary>
        private readonly int _wreck;
        private readonly float _wreckW;
        /// <summary>Where the flying body last tore the trench (II).</summary>
        private Vector2 _lastWreck;

        // the pursuit (Flow tiers, I and II): a blur down the lane to the body as it sits up
        private bool _dashPlanned, _dashOk, _dashing;
        private Vector2 _dashFrom, _dashTo;
        private float _dashDur;

        /// <summary>A God's meteor throws up rock that comes down in smaller craters round it.</summary>
        private struct Ejecta
        {
            public Vector2 At;
            public float When;
        }

        private readonly List<Ejecta> _ejecta = new List<Ejecta>();

        /// <summary>Swings started / throws landed since the game started (dev log).</summary>
        internal static int Started, Landed;

        private HurricaneSwing(Actor a, Actor v, Vector2 aim, Actor aimActor, float len, int level) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _v = v;
            _vid = v.Sid();
            _vWeight = Weights.Of(v);
            _start = a.current_position;
            _k = K;
            _pal = Palette.For(a, Level);
            string id = a.asset != null ? a.asset.id : "";
            _crab = id.Contains("crab");
            _big = !_crab && BigBody(a);
            _aim = aim;
            _aimActor = aimActor;
            _aimId = aimActor != null ? aimActor.Sid() : -1;
            _len = len;
            Vector2 vp = v.current_position;
            float dx = vp.x - _start.x;
            _fwd = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : (a.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            _stand = _start;
            _stepFrom = _start;
            float dist = Vector2.Distance(_start, vp);
            if (dist > GrabReach(v) && StandBeside(vp, out Vector2 beside))
            {
                _stand = beside;
            }
            float step = Vector2.Distance(_start, _stand);
            _tStep = step > 0.05f ? Mathf.Clamp(0.08f + 0.03f * step, 0.1f, 0.32f) : 0f;
            Tier tier = Tiers.Of(a);
            TierInfo ti = Tiers.Info(tier);
            _flow = ti != null && ti.Flow;
            _pace = ti != null ? Mathf.Clamp(ti.Linger, 0.3f, 1f) : 1f;
            _scale = SpinWreck.Scale(tier);
            _god = tier == Tier.God;
            _wreck = SpinWreck.Force(a, Level);
            _wreckW = SpinWreck.Reach(a);
        }

        /// <summary>Nothing can touch it while it is a blur down the lane after its throw.</summary>
        public override bool Intangible => _phase == Phase.Pursue && _dashing;

        /// <summary>How high a hopping victim (an archer's backstep, a dodge, a knock) the hand still snatches out of the air.</summary>
        private float HopGrab => 1.5f * _k;

        /// <summary>
        /// How big a creature this is by nature: its species' health with its genome (many species set
        /// their health only through the genome, so the asset's base health alone reads as zero).
        /// Critters are up to 80, people up to 180, brutes (bears) above. Cached per species
        /// (Weights.SpeciesHealth): the overview builds a new stat table on every call, and the
        /// planner asks this of every enemy beside the unit several times a second.
        /// </summary>
        internal static float Bulk(Actor u)
        {
            return u == null || u.asset == null ? 100f : Weights.SpeciesHealth(u.asset);
        }

        /// <summary>A big performer (a bear, a giant): heavier, slower, more dust.</summary>
        private bool BigBody(Actor a)
        {
            float mass = a.asset != null && a.asset.base_stats != null ? a.asset.base_stats["mass_2"] : 0f;
            return Bulk(a) > 180f || mass >= 140f || _k >= 1.3f || Pose.PivotUp >= 5.2f;
        }

        /// <summary>How far away it can still take hold of 'v' without stepping in.</summary>
        private float GrabReach(Actor v)
        {
            return 0.75f * _k + (A.stats["size"] + v.stats["size"]) * 0.5f;
        }

        /// <summary>Where to stand to grab it: right beside it, on solid ground (either side). False if there is none.</summary>
        private bool StandBeside(Vector2 vp, out Vector2 at)
        {
            at = Combat.Clamp(vp - new Vector2(_fwd * 0.5f * _k, 0f));
            if (Combat.SafeSpot(at))
            {
                return true;
            }
            Vector2 toward = vp - Pose.Ground;
            at = Combat.Clamp(vp - (toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector2.right * _fwd) * 0.5f * _k);
            return Combat.SafeSpot(at);
        }

        public static bool Begin(Actor a, Actor v, Vector2 aim, Actor aimActor, float len, int level, Technique tech)
        {
            if (!Moves.CanPerform(a) || v == null || !Combat.CanPuppet(v, a))
            {
                return false;
            }
            HurricaneSwing m = new HurricaneSwing(a, v, aim, aimActor, len, level) { Tech = tech };
            if (m._tStep > 0f && !Combat.ClearPath(a.current_position, m._stand))
            {
                return false;   // a wall or water between them: it can't get there
            }
            if (m._tStep <= 0f && Vector2.Distance(a.current_position, v.current_position) > m.GrabReach(v) + 0.3f)
            {
                return false;   // out of reach, and nowhere solid to step in beside it
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            // frame the whole flight: a low throw lands 'len' tiles out, past the crowd it aims at
            // (in game the landing of a level II throw fell off the edge of the shot)
            Vector2 from = a.current_position, end = aim;
            if (level <= 2 && len > 0f && (aim - from).sqrMagnitude > 0.01f)
            {
                end = from + (aim - from).normalized * len;
            }
            Dev.Watch(Vector2.Lerp(from, end, 0.5f) + new Vector2(0f, level >= 3 ? 2.5f * m._k : 0.8f));
            return true;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            CheckVictim();
            Rockfall(false);
            if (_hold > 0f)
            {
                // a hit beat: the pair holds still, trembling with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                if (Held)
                {
                    _p.Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? -0.5f : 0.5f, 0f);
                }
                return;
            }
            Pose.Nudge = Vector2.zero;
            if (Held)
            {
                _p.Pose.Nudge = Vector2.zero;
            }
            _pt += dt;
            _vt += dt;
            switch (_phase)
            {
                case Phase.StepIn:
                    StepIn(dt);
                    break;
                case Phase.Grab:
                    Grab();
                    break;
                case Phase.Swing:
                    Swing(dt);
                    break;
                case Phase.Throw:
                    Throw();
                    break;
                case Phase.Wait:
                    Wait();
                    break;
                case Phase.Crouch:
                    Crouch();
                    break;
                case Phase.Chase:
                    Chase(dt);
                    break;
                case Phase.Overhead:
                    Overhead(dt);
                    break;
                case Phase.Axe:
                    Axe();
                    break;
                case Phase.Descent:
                    Descent(dt);
                    break;
                case Phase.Land:
                    Land();
                    break;
                case Phase.Whiff:
                    Whiff();
                    break;
                case Phase.Pursue:
                    Pursue(dt);
                    break;
                default:
                    Recoil();
                    break;
            }
            if (Held && !Finished)
            {
                Victim(dt);
            }
            UpdateVShadow();
            UpdateAShadow();
            if (!Finished && T > 7f)
            {
                Finish();   // never hang on to anyone forever
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("hurricane " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3") + " h=" + Pose.Height.ToString("F2") + " v=" + _vs);
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

        /// <summary>Is the victim ours right now (grabbed, alive, still the same unit)?</summary>
        private bool Held => _p != null && !_vGone && _vs != VState.Done;

        private bool Visible => Kit.Visible(Pose.Ground, 0.45f);

        private Vector2 VBody => _p.BodyPos();

        /// <summary>The victim is killed by someone else, taken by the god hand, recycled: let go, wind down.</summary>
        private void CheckVictim()
        {
            if (_p == null || _vGone || _vs == VState.Done)
            {
                return;
            }
            if (!_p.Finished && _v.isAlive() && _v.Sid() == _vid)
            {
                return;
            }
            _vGone = true;
            if (!_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            KillVShadow();
            KillMarker();
            _trail = false;
            if (EndTarget == _v)
            {
                // the chain the crash left was about it (a crowd it scattered is still there to chase)
                EndTarget = null;
                if (EndLink != Link.Scattered)
                {
                    EndLink = Link.None;
                }
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " lost its victim in " + _phase);
            switch (_phase)
            {
                case Phase.Grab:
                case Phase.Swing:
                    Next(Phase.Whiff);
                    break;
                case Phase.Crouch:
                case Phase.Chase:
                case Phase.Overhead:
                    Next(Pose.Height > 0.05f ? Phase.Descent : Phase.Land);
                    break;
            }
        }

        // ------------------------------------------------------------ step in --

        private void StepIn(float dt)
        {
            if (_tStep <= 0f)
            {
                Next(Phase.Grab);
                Grab();
                return;
            }
            float px = Px;
            if (!Free())
            {
                return;
            }
            // it is reaching for this one: no attack (and none of the game's hops that come with one)
            // until the hand closes; it may still answer the technique itself
            Combat.Pin(_v, 0.2f);
            if (Enter() && Visible)
            {
                Fx.LandingPuff(_stepFrom, px, Combat.TileAt(_stepFrom));
                if (_lunges > 0 || Vector2.Distance(_stepFrom, _stand) > 2f)
                {
                    Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                }
            }
            // keep beside it while it moves (never onto bad ground)
            if (StandBeside(_v.current_position, out Vector2 want) && Vector2.Distance(want, _stand) < 1.2f
                && Combat.ClearPath(_stepFrom, want))
            {
                _stand = want;
            }
            float k = Mathf.Clamp01(_pt / _tStep);
            Pose.Ground = Vector2.Lerp(_stepFrom, _stand, Anim.OutQuad(k));
            float lean = Mathf.Sin(Mathf.PI * k);
            Pose.FaceRight = _fwd > 0f;
            Pose.Rot = 14f * lean;
            Pose.SX = 1f + 0.1f * lean;
            Pose.SY = 1f - 0.1f * lean;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.025f;
                    Fx.Afterimage(this, 0.14f, Level >= 2 ? _pal.Ghost : new Color(1f, 1f, 1f, 0.6f), Level >= 2 ? _pal.GhostEnd : GhostCool);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Grab);
            }
        }

        /// <summary>
        /// Before the grab the victim is still free: if it is gone, take another of its weight within
        /// 2 tiles, or grab air. False if the move went elsewhere.
        /// </summary>
        private bool Free()
        {
            // a victim in a low hop (backing off, dodging, knocked) is still the one to grab
            // (one in a light move, a flow strike, is still there to grab: the grab cuts it short)
            if (_v != null && _v.isAlive() && _v.Sid() == _vid && !Moves.Locked(_v) && SpinGrip.Low(_v, HopGrab) && !_v.isInMagnet())
            {
                return true;
            }
            Actor best = null;
            float bestD = float.MaxValue;
            foreach (Actor u in SpinGrip.LowFoes(A, Pose.Ground, 2f, HopGrab))
            {
                if (!SpinGrip.Snatchable(u, A, HopGrab) || !Combat.Allowed(A, u) || Weights.Of(u) > _vWeight)
                {
                    continue;
                }
                float d = (u.current_position - Pose.Ground).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = u;
                }
            }
            if (best == null)
            {
                if (Dev.On) Dev.Trace("hurricane " + Id + " whiffs: its victim is gone and no one of its weight is near");
                Next(Phase.Whiff);
                return false;
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " retargets to " + best.asset.id);
            _v = best;
            _vid = best.Sid();
            _vWeight = Weights.Of(best);
            float dx = best.current_position.x - Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                _fwd = Mathf.Sign(dx);
            }
            if (_phase == Phase.StepIn && StandBeside(best.current_position, out Vector2 to) && Combat.ClearPath(Pose.Ground, to))
            {
                // restart the step from here toward the new one (no jump along the old lerp)
                _stand = to;
                _stepFrom = Pose.Ground;
                _tStep = Mathf.Clamp(0.06f + 0.035f * Vector2.Distance(Pose.Ground, to), 0.08f, 0.25f);
                _pt = 0f;
            }
            return true;
        }

        // ---------------------------------------------------------------- grab --

        private void Grab()
        {
            float px = Px;
            if (_p == null)
            {
                if (!Free())
                {
                    return;
                }
                Combat.Pin(_v, 0.2f);
                // the anticipation: a crouch facing it
                float c = Anim.OutQuad(Mathf.Clamp01(_pt / TContact));
                Pose.Ground = _stand;
                Pose.FaceRight = _fwd > 0f;
                Pose.SX = 1f + 0.1f * c;
                Pose.SY = 1f - 0.13f * c;
                Pose.Rot = -8f * c;
                Pose.Height = 0f;
                if (_pt >= TContact)
                {
                    TakeHold();
                }
                return;
            }
            // the hoist: lean in, then rock back, the victim yanked off its feet to arm's length
            float k = Mathf.Clamp01((_pt - TContact) / (TGrab - TContact));
            float e = Anim.OutCubic(k);
            Pose.Ground = _c;
            Pose.FaceRight = Mathf.Cos(_phi0) >= 0f;
            Pose.SX = Mathf.Lerp(1.12f, 1f, e);
            Pose.SY = Mathf.Lerp(0.9f, 1f, e);
            Pose.Rot = Mathf.Lerp(16f, -8f, e);
            Pose.Nudge = _strain ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            Pose vp = _p.Pose;
            Vector2 ring = Orbit(_phi0);
            vp.Ground = Vector2.Lerp(_aFrom, ring, e);
            vp.Height = Mathf.Lerp(_aFromH, 0.12f * _k, e);
            vp.FaceRight = Mathf.Cos(_phi0) >= 0f;
            vp.Rot = 72f * e;
            vp.SX = 1f;
            vp.SY = Mathf.Lerp(1f, Mathf.Lerp(0.5f, 1f, Mathf.Abs(Mathf.Cos(_phi0))), e);
            vp.HideShadow = true;
            _p.Pose = vp;
            if (k >= 1f)
            {
                Next(Phase.Swing);
            }
        }

        /// <summary>The hand reaches it: it may answer (a parry, a sidestep), or it is caught.</summary>
        private void TakeHold()
        {
            float px = Px;
            float reach = GrabReach(_v) + 0.6f;
            if ((_v.current_position - Pose.Ground).sqrMagnitude > reach * reach)
            {
                // it hopped back out of reach: one more lunge after it, then it got away
                if (Relunge())
                {
                    return;
                }
                if (Dev.On) Dev.Trace("hurricane " + Id + " whiffs: " + _v.asset.id + " got away (" + Vector2.Distance(_v.current_position, Pose.Ground).ToString("F2") + " tiles)");
                Next(Phase.Whiff);
                return;
            }
            Contact answer = Combat.Reach(this, _v, false);
            if (answer == Contact.Parried)
            {
                Next(Phase.Recoil);
                return;
            }
            if (answer != Contact.Hit || !SpinGrip.Snatchable(_v, A, HopGrab) || !Combat.Allowed(A, _v, out _vcm))
            {
                if (Dev.On) Dev.Trace("hurricane " + Id + " whiffs: " + (answer != Contact.Hit ? "answered (" + answer + ")" : "cannot take hold of " + _v.asset.id + " h=" + _v.position_height.ToString("F2")));
                Next(Phase.Whiff);
                return;
            }
            Vector2 vpos = _v.current_position;
            _aFromH = Mathf.Max(0f, _v.position_height);
            _p = new Puppet(_v, vpos.x < Pose.Ground.x, this);
            _p.Pose.Height = _aFromH;   // snatched out of a hop: taken from where it is, not snapped down
            if (!Moves.Start(_p))
            {
                _p = null;
                if (Dev.On) Dev.Trace("hurricane " + Id + " whiffs: the victim could not be taken");
                Next(Phase.Whiff);
                return;
            }
            _vs = VState.Held;
            _vt = 0f;
            _c = Pose.Ground;
            _aFrom = vpos;
            _critter = Bulk(_v) <= 80f;
            _heavy = Bulk(_v) > 180f || _vWeight >= Weight.Brute;
            _strain = !_big && _heavy;
            bool small = _p.Pose.PivotUp < Pose.PivotUp * 0.8f;
            // arm's length plus the victim's own half body: it must clearly circle the thrower, not
            // overlap it (at 0.6 tiles the pair read as one blob in game)
            _R = 0.8f * _k * (_big ? 1.2f : 1f);

            // how it swings: a big one whirls a small one fast, a heavy one makes it strain
            _turns = Level <= 1 ? 1 : Level == 2 ? 3 : 5;
            _tSwing = Level <= 1 ? 0.4f : Level == 2 ? 0.88f : 0.95f;
            if (_big && small)
            {
                _tSwing *= 0.8f;
            }
            else if (_strain)
            {
                _tSwing *= 1.15f;
            }
            // a stronger thrower whirls its catch faster (a God about a sixth faster)
            _tSwing *= SpinWreck.Whirl(A);
            Aim();
            Vector2 rel = vpos - _c;
            if (rel.sqrMagnitude < 1e-4f)
            {
                rel = new Vector2(_fwd, 0f);
            }
            _phi0 = Mathf.Atan2(rel.y / Flat, rel.x);
            float phiAim = Mathf.Atan2(_dirThrow.y / Flat, _dirThrow.x);
            _sign = Random.value < 0.5f ? 1f : -1f;
            float delta = Mathf.Repeat((phiAim - _phi0) * _sign, Mathf.PI * 2f);
            if (delta > Mathf.PI)
            {
                delta -= Mathf.PI * 2f;   // end within half a turn of the whole turns
            }
            _sweep = Mathf.PI * 2f * _turns + delta;
            _phi = _phi0;

            // the catch
            Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            _p.Flash(0.05f);
            Vector2 vb = vpos + new Vector2(0f, _p.Pose.PivotUp * _v.actor_scale);
            if (Kit.Visible(vb))
            {
                if (_crab)
                {
                    Pinch(vb, px);
                }
                Fx.HitSpark(vb, px, 0, new Vector2(-_fwd, 0.4f), 2);
                if (_strain)
                {
                    Kit.Sparks(_c, px, 3, 30f, 60f, Pale);
                }
            }
            if (Kit.Visible(_c))
            {
                SpawnVShadow();
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " grabs " + _v.asset.id + " -> aim " + _aim + " len " + _len.ToString("F1") + " turns " + _turns + (_aFromH > 0.05f ? " (snatched mid-hop h=" + _aFromH.ToString("F2") + ")" : "") + (_lunges > 0 ? " after a second lunge" : ""));
        }

        /// <summary>
        /// The victim hopped back as the hand came (archers back off from melee): one more short
        /// lunge after it, if it is still close and the way is clear. False: it got away.
        /// </summary>
        private bool Relunge()
        {
            if (_lunges >= 1 || !SpinGrip.Low(_v, HopGrab) || !StandBeside(_v.current_position, out Vector2 to))
            {
                return false;
            }
            float d = Vector2.Distance(Pose.Ground, to);
            if (d > 2.6f * _k || d < 0.05f || !Combat.ClearPath(Pose.Ground, to))
            {
                return false;
            }
            _lunges++;
            _stepFrom = Pose.Ground;
            _stand = to;
            _tStep = Mathf.Clamp(0.06f + 0.035f * d, 0.08f, 0.2f);
            float dx = _v.current_position.x - Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                _fwd = Mathf.Sign(dx);
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " lunges again after " + _v.asset.id + " (" + d.ToString("F2") + " tiles)");
            Next(Phase.StepIn);
            return true;
        }

        /// <summary>
        /// Where to throw it, decided at the grab: the crowd the planner saw, wherever it has moved
        /// to (its middle, now); straight on if there is none.
        /// </summary>
        private void Aim()
        {
            Vector2 aim = _aim;
            if (_aimActor != null && _aimActor.isAlive() && _aimActor.Sid() == _aimId)
            {
                aim = Vector2.Lerp(aim, _aimActor.current_position, 0.7f);
            }
            List<Actor> crowd = Combat.FoesAround(A, aim, 1.6f * _k);
            if (crowd.Count > 0)
            {
                Vector2 sum = Vector2.zero;
                int n = 0;
                foreach (Actor u in crowd)
                {
                    if (u != _v)
                    {
                        sum += u.current_position;
                        n++;
                    }
                }
                if (n > 0)
                {
                    aim = Vector2.Lerp(aim, sum / n, 0.6f);
                }
            }
            Vector2 d = aim - _c;
            if (d.sqrMagnitude < 0.25f)
            {
                d = new Vector2(_fwd, 0f);
                aim = _c + d * 3f;
            }
            _dirThrow = d.normalized;
            float dist = Vector2.Distance(_c, aim);
            if (Level >= 3)
            {
                if (!Combat.FindLanding(Combat.Clamp(aim), 1.5f, out aim))
                {
                    aim = FlightEnd(_c, _dirThrow, dist, out _);
                }
                _dirThrow = (aim - _c).sqrMagnitude > 0.04f ? (aim - _c).normalized : new Vector2(_fwd, 0f);
            }
            else if (_len <= 0f)
            {
                _len = Level <= 1 ? Mathf.Clamp(dist + 0.4f, 2.4f, 3.6f * _scale) : Mathf.Clamp(dist + 1.2f, 4.5f, 7f * _scale);
            }
            if (_critter && Level <= 2)
            {
                _len *= 1.25f;   // critters fly comically far
            }
            _aim = aim;
        }

        /// <summary>A point on the swing circle.</summary>
        private Vector2 Orbit(float phi)
        {
            return _c + new Vector2(Mathf.Cos(phi), Mathf.Sin(phi) * Flat) * _R;
        }

        // --------------------------------------------------------------- swing --

        private void Swing(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _nextTurn = 0f;
                _phiLast = _phi0;
                if (Visible)
                {
                    if (Level >= 2)
                    {
                        // the feet bite: a ring of dust a tile wide
                        Kit.DustRing(_c, px, A.current_tile, _big ? 14 : 10, 4.5f * _k / px, 0.55f);
                    }
                    else
                    {
                        Scuff(px, 2);
                    }
                }
            }
            float u = Mathf.Clamp01(_pt / _tSwing);
            float e = Mathf.Pow(u, 1.6f);
            float speed = Mathf.Pow(u, 0.6f);
            _phi = _phi0 + _sign * _sweep * e;
            float cos = Mathf.Cos(_phi), sin = Mathf.Sin(_phi);

            // the thrower: planted, turning with its catch, leaning back against the pull
            Pose.Ground = _c;
            Pose.FaceRight = cos >= 0f;
            Pose.SX = Mathf.Lerp(0.55f, _big ? 1.08f : 1f, Mathf.Abs(cos));
            Pose.SY = 1f;
            Pose.Rot = -(8f + 14f * speed);
            Pose.Height = Level >= 3 ? 0.12f * _k * speed : 0f;
            Pose.Nudge = _strain && u < 0.5f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;

            // the victim: at arm's length, nearly level, head out, foreshortened front and back
            if (Held)
            {
                Pose vp = _p.Pose;
                vp.Ground = Orbit(_phi);
                vp.Height = Mathf.Lerp(0.12f, Level >= 2 ? 0.42f : 0.3f, speed) * _k;
                vp.FaceRight = cos >= 0f;
                vp.Rot = 72f + 14f * speed;
                vp.SX = 1f;
                vp.SY = Mathf.Lerp(0.5f, 1f, Mathf.Abs(cos));
                vp.HideShadow = true;
                _p.Pose = vp;
                // whoever crowds the swing is batted aside by the body as it comes round
                if (u >= 0.06f)
                {
                    Clip(_phiLast, _phi);
                }
            }
            _phiLast = _phi;

            // one swoosh (and a whoosh) per turn
            float turned = _sweep * e / (Mathf.PI * 2f);
            if (turned >= _nextTurn && u < 0.97f)
            {
                _nextTurn += 1f;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    SwingArcs(u);
                }
            }
            if (Level >= 3 && !_reAimed && u >= 0.68f)
            {
                _reAimed = true;
                ReAim();   // before the marker shows where it will fall
            }
            if (Visible)
            {
                SwingFx(px, dt, u, speed);
            }
            if (u >= 1f)
            {
                Release();
            }
        }

        /// <summary>
        /// The body sweeps the arc between two swing angles: every enemy standing in its way is
        /// batted aside (once each). Enemies in the lane the body will be thrown down (the pins), or
        /// where the meteor will fall, are left standing for that.
        /// </summary>
        private void Clip(float from, float to)
        {
            if (_clips >= MaxClips || !Held)
            {
                return;
            }
            float r = 0.38f * _k;
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(to - from) / 0.45f), 1, 8);
            // one look around the circle, then each enemy against the arc swept since last tick
            foreach (Actor u in Combat.FoesAround(A, _c, _R + r))
            {
                if (u == _v || _clipped.Contains(u.Sid()) || Spared(u))
                {
                    continue;
                }
                float reach = r + u.stats["size"] * 0.5f;
                for (int i = 1; i <= n; i++)
                {
                    float phi = Mathf.Lerp(from, to, i / (float)n);
                    if ((u.current_position - Orbit(phi)).sqrMagnitude > reach * reach)
                    {
                        continue;
                    }
                    _clipped.Add(u.Sid());
                    ClipOne(u, phi);
                    break;
                }
                if (_clips >= MaxClips || !Held)
                {
                    return;
                }
            }
        }

        /// <summary>Left for the throw: standing in the lane ahead (I, II) or where the meteor will fall (III).</summary>
        private bool Spared(Actor u)
        {
            Vector2 p = u.current_position;
            if (Level >= 3)
            {
                float r = 1.6f * _k * _scale;
                return (p - _aim).sqrMagnitude <= r * r;
            }
            Vector2 rel = p - _c;
            float along = Vector2.Dot(rel, _dirThrow);
            float off = Mathf.Abs(rel.x * _dirThrow.y - rel.y * _dirThrow.x);
            return along > 0.15f * _k && along < _len + 0.5f && off <= 0.55f * _k + u.stats["size"] * 0.5f;
        }

        /// <summary>One enemy struck by the whirling body: knocked out of the circle, the way the swing turns.</summary>
        private void ClipOne(Actor u, float phi)
        {
            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
            Vector2 body = u.current_position + new Vector2(0f, u.position_height + 4f * vpx);
            Vector2 radial = u.current_position - _c;
            if (radial.sqrMagnitude < 1e-4f)
            {
                radial = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
            }
            radial.Normalize();
            // the swing's own direction at that point of the circle
            Vector2 tangent = new Vector2(-Mathf.Sin(phi), Mathf.Cos(phi) * Flat) * _sign;
            tangent = tangent.sqrMagnitude > 1e-4f ? tangent.normalized : radial;
            Vector2 away = (radial * 0.75f + tangent * 0.65f).normalized;
            float mult = Level <= 1 ? 0.35f : Level == 2 ? 0.5f : 0.6f;
            if (!Combat.Allowed(A, u, out float cm))
            {
                // too heavy to bat aside: it takes the knock and rocks
                Combat.Hit(A, u, Mathf.Min(0.5f, mult), true, true, 0f, this);
                Combat.Stagger(u, 0.35f);
                if (Kit.Visible(body))
                {
                    Fx.HitSpark(body, Px, 0, away, 2);
                }
                return;
            }
            if (Combat.Reach(this, u, true) != Contact.Hit)
            {
                return;
            }
            Combat.Hit(A, u, mult * cm, true, true, 0f, this);
            _clips++;
            float dist = (Level <= 1 ? 0.9f : Level == 2 ? 1.2f : 1.5f) * _k * _scale;
            float apex = (Level <= 1 ? 0.3f : 0.5f) * _k;
            float bulk = Bulk(u);
            if (bulk <= 80f)
            {
                dist *= 1.6f;
                apex *= 1.3f;
            }
            else if (bulk > 180f || Weights.Of(u) >= Weight.Brute)
            {
                dist *= 0.6f;
                apex *= 0.7f;
            }
            Combat.LaunchDir(u, away, dist, apex, Level <= 1 ? 0f : (_sign > 0f ? 540f : -540f));
            if (Held)
            {
                _p.Flash(0.04f);
            }
            Anim.Sound("event:/SFX/OTHER/Punch", u.current_tile);
            if (Kit.Visible(body))
            {
                Fx.HitSpark(body, Px, Level <= 1 ? 0 : 1, away + Vector2.up * 0.3f, Level <= 1 ? 3 : 5);
                if (Level >= 2)
                {
                    TierFlash(u, 0.1f);
                }
            }
            if (Level >= 2 && _clips == 1)
            {
                _hold = 0.04f;   // the first one jolts the swing (a local hit-stop)
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " swing clips " + u.asset.id + " (" + _clips + ")");
        }

        /// <summary>
        /// Level III: the crowd may have moved, or been batted aside by the swing, since the grab.
        /// Just before the marker shows where the meteor will fall, it is aimed again at the biggest
        /// group standing 1.5-7 tiles away now, if that beats the group still at the old spot.
        /// </summary>
        private void ReAim()
        {
            float r = 2f * _k * _scale, g = 1.6f * _k;
            List<Actor> foes = Combat.FoesAround(A, _c, 7.5f * _k);
            for (int i = foes.Count - 1; i >= 0; i--)
            {
                if (foes[i] == _v || !Combat.Allowed(A, foes[i]))
                {
                    foes.RemoveAt(i);
                }
            }
            int here = 0;
            foreach (Actor u in foes)
            {
                if ((u.current_position - _aim).sqrMagnitude <= r * r)
                {
                    here++;
                }
            }
            int best = 0;
            Vector2 at = _aim;
            foreach (Actor u in foes)
            {
                float d = Vector2.Distance(u.current_position, _c);
                if (d < 1.5f * _k || d > 7f * _k)
                {
                    continue;
                }
                Vector2 sum = Vector2.zero;
                int n = 0;
                foreach (Actor w in foes)
                {
                    if ((w.current_position - u.current_position).sqrMagnitude <= g * g)
                    {
                        sum += w.current_position;
                        n++;
                    }
                }
                if (n > best)
                {
                    best = n;
                    at = sum / n;
                }
            }
            if (best <= here || !Combat.FindLanding(Combat.Clamp(at), 1.5f, out Vector2 spot))
            {
                return;
            }
            if (Dev.On) Dev.Trace("hurricane " + Id + " re-aims the meteor: " + here + " -> " + best + " at " + spot);
            _aim = spot;
            _dirThrow = (_aim - _c).sqrMagnitude > 0.04f ? (_aim - _c).normalized : new Vector2(_fwd, 0f);
        }

        /// <summary>The swooshes of one turn: white at I, tier-tinted at II, a stacked funnel at III.</summary>
        private void SwingArcs(float u)
        {
            // how long the next turn takes at the current speed: the crescent keeps pace with it
            float omega = _sweep * 1.6f * Mathf.Pow(Mathf.Max(u, 0.15f), 0.6f) / _tSwing;
            // (its head goes all the way round in 2 x HalfGap of its life: two half-sweeps)
            float life = Mathf.Clamp(2f * Mathf.PI / Mathf.Max(0.1f, omega) / (2f * HalfGap), 0.14f, 0.45f);
            float start = Mathf.Repeat(Mathf.Round(_phi * Mathf.Rad2Deg / 90f) * 90f, 360f);
            // traced by the victim's head: at its body's height, on the same flattened circle
            float lift = Held ? _p.Pose.Height + _p.Pose.PivotUp * _v.actor_scale * 0.75f : 0.35f * _k;
            float r = _R + 0.45f * _k;
            if (Level <= 1)
            {
                Crescent(r, Flat, 2, life, lift, start, Color.white, Pale);
                return;
            }
            Crescent(r, Flat, 3, life, lift, start, _pal.Core, _pal.Edge);
            if (Level >= 3)
            {
                Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                Crescent(r * 0.75f, 0.42f, 2, life, 0.06f * _k, Mathf.Repeat(start + 90f * _sign, 360f), Fx.WithA(Fx.Lighten(dust, 0.3f), 0.9f), Fx.WithA(dust, 0.75f), true);
                Crescent(r * 1.45f, 0.48f, 2, life, 0.95f * _k, Mathf.Repeat(start + 180f, 360f), Color.Lerp(_pal.Core, _pal.Edge, 0.3f), Fx.WithA(_pal.Edge, 0.8f));
            }
        }

        private void SwingFx(float px, float dt, float u, float speed)
        {
            // dust scuffed up by the turning feet
            _puff -= dt;
            if (_puff <= 0f)
            {
                _puff = Mathf.Lerp(0.14f, 0.06f, speed);
                Scuff(px, 1);
            }
            if (Held && Level >= 2 && speed > 0.35f)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    // tinted and see-through: in game the near-white ghosts of a body turned on
                    // its side piled into white lumps beside the thrower
                    _ghost = 0.04f;
                    Fx.Afterimage(_p, 0.09f, Fx.WithA(Color.Lerp(_pal.Ghost, _pal.Edge, 0.45f), 0.55f), _pal.GhostEnd);
                }
            }
            if (Level >= 3)
            {
                // the tornado: dust climbing a helix round the pair, embers inside
                _helix -= dt;
                if (_helix <= 0f)
                {
                    _helix = 0.035f / Fx.Detail();
                    float a = _phi * 0.5f;
                    Helix(px, a, 1.25f * _k, 1.6f * _k);
                    Helix(px, a + Mathf.PI, 1.25f * _k, 1.6f * _k);
                }
                _lines -= dt;
                if (_lines <= 0f && u > 0.25f)
                {
                    _lines = 0.06f;
                    Kit.Ember(_c + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(0.1f, 0.6f)) * _k, px, Random.Range(0.4f, 0.7f),
                        new Vector2(Random.Range(-6f, 6f), Random.Range(12f, 24f)) * px, _pal.Edge);
                }
                if (u > 0.7f && _marker == null)
                {
                    Marker(px);   // the crowd sees it coming
                }
            }
        }

        /// <summary>Let go: a low throw (I, II), or straight up (III).</summary>
        private void Release()
        {
            float px = Px;
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (!Held)
            {
                Next(Phase.Throw);
                return;
            }
            Vector2 at = VBody;
            if (Kit.Visible(at))
            {
                if (Level >= 2)
                {
                    Fx.Ring(at, px, 0.2f, 2f, 9f * _k, 2, 1, FxLayer.Top, 3, 1f, 2.5f, Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0f));
                }
                else
                {
                    Fx.Ring(at, px, 0.16f, 2f, 6f * _k, 1, 1, FxLayer.Top, 3, 1f, 2.5f, new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
                }
            }
            if (Level >= 3)
            {
                // the heave: the tornado's footprint is scorched into the ground (a God's blasted),
                // under its own feet, so never fire
                SpinWreck.At(A, _c, 1.8f * _k * _wreckW, _wreck >= 4 ? 2 : 1, _c, 0f, false);
                VToss();
            }
            else
            {
                VFly();
            }
            Next(Phase.Throw);
        }

        // --------------------------------------------------- throw (I, II) --

        private void Throw()
        {
            float t = Level >= 3 ? TToss : TFollow * _pace;
            float k = Mathf.Clamp01(_pt / t);
            if (_dirThrow.sqrMagnitude > 0f && Mathf.Abs(_dirThrow.x) > 0.15f)
            {
                Pose.FaceRight = _dirThrow.x > 0f;
            }
            Pose.Ground = _c;
            if (Level >= 3)
            {
                // the heave upward: stretched tall, leaning back
                Pose.Height = Mathf.Lerp(Pose.Height, 0f, k);
                Pose.Rot = Mathf.Lerp(-22f, -14f, k);
                Pose.SX = Mathf.Lerp(0.86f, 0.95f, k);
                Pose.SY = Mathf.Lerp(1.18f, 1.06f, k);
                if (_pt >= t)
                {
                    Next(_vGone ? Phase.Land : Phase.Crouch);
                }
                return;
            }
            // the follow-through: leaning into the throw, arm out
            float e = Anim.OutCubic(k);
            Pose.Height = 0f;
            Pose.Rot = Mathf.Lerp(24f, 10f, e);
            Pose.SX = Mathf.Lerp(1.16f, 1.04f, e);
            Pose.SY = Mathf.Lerp(0.88f, 0.97f, e);
            if (_pt >= t)
            {
                Next(Phase.Wait);
            }
        }

        /// <summary>
        /// It watches the body fly, then settles; done when the victim has landed. A Flow tier
        /// (Master and up) does not stand and watch: once the body is down it blinks after it
        /// (Pursue), timed to arrive as it sits up.
        /// </summary>
        private void Wait()
        {
            float amp = Anim.Spring(_pt, 8f, 16f);
            Pose.Ground = _c;
            Pose.Height = 0f;
            Pose.Rot = 10f * amp;
            Pose.SX = 1f + 0.05f * amp;
            Pose.SY = 1f - 0.04f * amp;
            bool down = _vs == VState.Tumble || _vs == VState.Done;
            if (_flow && Level <= 2 && !_vGone && _p != null && down)
            {
                if (!_dashPlanned)
                {
                    _dashPlanned = true;
                    _dashOk = PlanDash();
                }
                if (_dashOk && (_vs == VState.Done || TumbleT - _vt <= _dashDur + 0.04f))
                {
                    Next(Phase.Pursue);
                    return;
                }
            }
            if ((_vGone || _vs == VState.Done || _p == null) && _pt >= (_flow ? 0.04f : 0.12f * _pace))
            {
                Strike3();
                if (_flow)
                {
                    LinkIfClose();
                }
                Finish();
            }
        }

        /// <summary>Three or more bowled over: a strike! (a glint at the head)</summary>
        private void Strike3()
        {
            if (_pins >= 3 && Visible)
            {
                Fx.Glint(HeadPos() + new Vector2((Pose.FaceRight ? 3f : -3f) * Px, 2f * Px), Px, 0.26f, Ting, 1.1f);
            }
        }

        /// <summary>The victim lies dazed within reach: the next technique takes it from the ground (Down).</summary>
        private void LinkIfClose()
        {
            if (_vGone || _v == null || !_v.isAlive() || _v.Sid() != _vid)
            {
                return;
            }
            float r = 1.8f * _k;
            if ((_v.current_position - Pose.Ground).sqrMagnitude <= r * r)
            {
                EndLink = Link.Down;
                EndTarget = _v;
            }
        }

        /// <summary>How long the landed body tumbles before it sits up (I, II): a stronger thrower's lands harder and shorter (a Master's in 0.78 of the time).</summary>
        private float TumbleT => (Level <= 1 ? 0.3f : 0.42f) * (0.5f + 0.5f * _pace);

        // ------------------------------------------- the pursuit (Flow, I-II) --

        /// <summary>
        /// Where to arrive beside the body: on the near side of where it ends its slide, on solid
        /// ground, with a clear run down the lane (no wall, water or fire on the way). False: it
        /// stays where it threw from.
        /// </summary>
        private bool PlanDash()
        {
            Vector2 lie = _slideTo;
            Vector2 back = _c - lie;
            back = back.sqrMagnitude > 1e-4f ? back.normalized : -_dirThrow;
            Vector2 to = Combat.Clamp(lie + back * (0.65f * _k));
            if (!Combat.SafeSpot(to) && !Combat.FindLanding(to, 1f, out to))
            {
                return false;
            }
            float d = Vector2.Distance(Pose.Ground, to);
            if (d < 0.8f * _k || !Combat.ClearPath(Pose.Ground, to))
            {
                return false;
            }
            _dashTo = to;
            _dashDur = Mathf.Clamp(d / 26f, 0.1f, 0.22f);
            return true;
        }

        /// <summary>
        /// The blink after its throw: a tinted blur down the lane (untouchable), a skid in beside
        /// the body, a ready stance until it sits up; then the next technique takes it (Down).
        /// </summary>
        private void Pursue(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _dashFrom = Pose.Ground;
                _dashing = true;
                _ghost = 0f;
                _lines = 0f;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    Fx.LandingPuff(_dashFrom, px, Combat.TileAt(_dashFrom));
                }
                if (Dev.On) Dev.Trace("hurricane " + Id + " pursues its throw (" + Vector2.Distance(_dashFrom, _dashTo).ToString("F1") + " tiles in " + _dashDur.ToString("F2") + " s)");
            }
            Vector2 run = _dashTo - _dashFrom;
            if (_dashing)
            {
                float k = Mathf.Clamp01(_pt / _dashDur);
                Pose.Ground = Vector2.Lerp(_dashFrom, _dashTo, Anim.OutQuad(k));
                Pose.Height = 0f;
                if (Mathf.Abs(run.x) > 0.05f)
                {
                    Pose.FaceRight = run.x > 0f;
                }
                // a smear: stretched along the run, leaning into it
                Pose.Rot = 18f;
                Pose.SX = 1.3f;
                Pose.SY = 0.8f;
                if (Visible)
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.02f;
                        Fx.Afterimage(this, 0.14f, _pal.Ghost, _pal.GhostEnd);
                    }
                    _lines -= dt;
                    if (_lines <= 0f && run.sqrMagnitude > 1e-4f)
                    {
                        _lines = 0.05f;
                        Kit.SpeedLines(BodyPos(), run.normalized, px, 3, 4f, 0.12f, Fx.WithA(_pal.Core, 0.8f));
                    }
                }
                if (k >= 1f)
                {
                    _dashing = false;
                    _pt = 0f;
                    if (Kit.Visible(_dashTo))
                    {
                        Fx.LandingPuff(_dashTo, px, Combat.TileAt(_dashTo));
                        Scuff(px, 2);
                    }
                }
                return;
            }
            // beside it: the skid settles into a ready stance, facing the body
            float amp = Anim.Spring(_pt, 12f, 26f);
            Pose.Ground = _dashTo;
            Pose.Height = 0f;
            Vector2 at = Held ? _p.Pose.Ground : !_vGone && _v != null && _v.isAlive() ? _v.current_position : _dashTo;
            if (Mathf.Abs(at.x - _dashTo.x) > 0.05f)
            {
                Pose.FaceRight = at.x > _dashTo.x;
            }
            Pose.Rot = 14f * amp;
            Pose.SX = 1f + 0.14f * amp;
            Pose.SY = 1f - 0.14f * amp;
            if ((_vGone || _vs == VState.Done || _p == null) && _pt >= 0.06f)
            {
                Strike3();
                if (!_vGone && _v != null && _v.isAlive() && _v.Sid() == _vid)
                {
                    EndLink = Link.Down;
                    EndTarget = _v;
                }
                else
                {
                    // its passenger is gone (killed by the landing, or by someone else): the pins
                    // it bowled over are what is left to chase
                    EndLink = _pins >= 1 ? Link.Scattered : Link.None;
                    EndTarget = null;
                }
                Finish();
            }
        }

        // -------------------------------------------------- level III: chase --

        private void Crouch()
        {
            float k = Anim.OutQuad(Mathf.Clamp01(_pt / TCrouch));
            Pose.Ground = _c;
            Pose.Height = 0f;
            Pose.Rot = Mathf.Lerp(-14f, 0f, k);
            Pose.SX = Mathf.Lerp(0.95f, 1.15f, k);
            Pose.SY = Mathf.Lerp(1.06f, 0.82f, k);
            if (_pt >= TCrouch)
            {
                Next(Phase.Chase);
            }
        }

        private void Chase(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _aFrom = Pose.Ground;
                Pose.HideShadow = true;
                SpawnAShadow(px);
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    Fx.Ring(_c, px, 0.22f, 3f, 14f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f, Color.white, Fx.WithA(_pal.Edge, 0f));
                    Kit.DustRing(_c, px, A.current_tile, 8, 4.5f * 0.7f * _k / px, 0.45f);
                }
            }
            float k = Mathf.Clamp01(_pt / TChase);
            Vector2 under = Held ? _p.Pose.Ground - _dirThrow * 0.2f * _k : _aFrom;
            Pose.Ground = Vector2.Lerp(_aFrom, under, Anim.OutQuad(k));
            float target = (Held ? _p.Pose.Height : _apexH) + 0.85f * _k;
            Pose.Height = target * Anim.OutCubic(k);
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
            if (k >= 1f)
            {
                Next(Phase.Overhead);
            }
        }

        private void Overhead(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
            }
            float k = Mathf.Clamp01(_pt / TOverhead);
            float e = Anim.OutCubic(k);
            Vector2 over = Held ? _p.Pose.Ground - _dirThrow * 0.12f * _k : _aFrom;
            Pose.Ground = Vector2.Lerp(_aFrom, over, e);
            float top = Held ? _p.Pose.Height + 0.95f * _k : _hFrom;
            Pose.Height = Mathf.Lerp(_hFrom, top, e);
            Pose.Rot = -38f * e;
            Pose.SX = Mathf.Lerp(0.8f, 1f, e);
            Pose.SY = Mathf.Lerp(1.3f, 1f, e);
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.03f;
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Axe);
            }
        }

        private void Axe()
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                if (Visible)
                {
                    // the heel comes down in a falling crescent
                    float f = Pose.FaceRight ? 1f : -1f;
                    Kit.Swish(this, Art.Arc(11, 1f, 130f, -65f, 3), 0.16f, new Vector2(f * 1.5f, -1f), f, _pal.Core, _pal.Edge);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / TAxe);
            Pose.Rot = Mathf.Lerp(-38f, 110f, Anim.OutCubic(k));
            Pose.Height = _hFrom - 0.2f * _k * Anim.OutQuad(k);
            Pose.SX = 1f;
            Pose.SY = 1f;
            if (!_hit && k >= 0.4f)
            {
                _hit = true;
                if (Held)
                {
                    Vector2 at = VBody;
                    HitResult r = Combat.Hit(A, _v, 1.2f * _vcm, false, true, 0f, this);
                    _p.Flash(0.08f, TierTint(_v));
                    Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                    if (Kit.Visible(at))
                    {
                        Kit.ImpactFrame(at, px, 0.55f * _k, _pal, r.Crit);
                        Fx.FocusLines(at, px, Fx.Count(14), 16f * _k, 30f * _k, 0.14f);
                        Fx.HitSpark(at, px, 2, new Vector2(Pose.FaceRight ? 0.4f : -0.4f, -1f), 8);
                        Kit.Shake(at, 0.18f, 0.007f, _k);
                    }
                    _hold = 0.06f;
                    VMeteor();
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Descent);
            }
        }

        private void Descent(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                _landA = LandingSpot();
            }
            // (a stronger tier drops out of the sky after its kick faster, a God's in 0.57 of the time: it lands crisply and goes)
            float k = Mathf.Clamp01(_pt / (TDescent * (0.25f + 0.75f * _pace)));
            // a little recoil up off the kick, then a calm float down
            float up = Anim.OutQuad(Mathf.Clamp01(k / 0.2f));
            float down = Anim.InOutSine(Mathf.Clamp01((k - 0.2f) / 0.8f));
            Pose.Height = Mathf.Max(0f, Mathf.Lerp(Mathf.Lerp(_hFrom, _hFrom + 0.25f * _k, up), 0f, down));
            Pose.Ground = Vector2.Lerp(_aFrom, _landA, Anim.InOutSine(k));
            Pose.Rot = Mathf.Lerp(_rotFrom, 360f, Anim.OutCubic(Mathf.Clamp01(k / 0.55f)));
            float tuck = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.55f));
            Pose.SX = 1f - 0.05f * tuck;
            Pose.SY = 1f - 0.12f * tuck;
            if (Visible && k < 0.55f)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.045f;
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Land);
            }
        }

        /// <summary>Where the unit comes down after the kick: under it if that is solid ground, else where it stood.</summary>
        private Vector2 LandingSpot()
        {
            Vector2 under = Combat.Clamp(Pose.Ground);
            if (Combat.SafeSpot(under))
            {
                return under;
            }
            if (Combat.SafeSpot(_c))
            {
                return _c;
            }
            return Combat.FindLanding(_c, 2f, out Vector2 spot) ? spot : _start;
        }

        private void Land()
        {
            float px = Px;
            if (Enter())
            {
                Pose.HideShadow = false;
                KillAShadow();
                if (Pose.Height > 0.02f || _hFrom > 0.3f)
                {
                    if (Visible)
                    {
                        Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                        Fx.Glint(HeadPos() + new Vector2((Pose.FaceRight ? 3f : -3f) * px, 2f * px), px, 0.24f, Ting, 1f);
                    }
                }
                Pose.Height = 0f;
            }
            // the same squash and spring, played faster for a Flow tier
            float amp = Anim.Spring(_pt / _pace, 9f, 18f);
            Pose.SX = 1f + 0.16f * amp;
            Pose.SY = 1f - 0.18f * amp;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            if (_pt >= TLand * _pace && (_vGone || _vs == VState.Done || _p == null))
            {
                Finish();
            }
        }

        // ------------------------------------------------------ misfortunes --

        /// <summary>It grabbed air (the victim slipped away, sidestepped, or was gone): a half turn on momentum, a glint.</summary>
        private void Whiff()
        {
            float px = Px;
            if (Enter())
            {
                _rotFrom = Pose.Rot;
                if (Visible)
                {
                    Crescent(0.7f * _k, 0.5f, 2, 0.24f, 0.25f * _k, Pose.FaceRight ? 0f : 180f, new Color(1f, 1f, 1f, 0.8f), Pale);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / (TWhiff * _pace));
            float turn = 180f * Anim.OutCubic(k);
            float c = Mathf.Cos(turn * Mathf.Deg2Rad);
            Pose.Height = 0f;
            Pose.FaceRight = c >= 0f ? _fwd > 0f : _fwd < 0f;
            Pose.SX = Mathf.Lerp(0.55f, 1f, Mathf.Abs(c));
            Pose.SY = 1f;
            Pose.Rot = Mathf.Lerp(_rotFrom + 12f, 0f, Anim.OutQuad(k));
            if (k >= 1f)
            {
                if (Visible)
                {
                    Fx.Glint(HeadPos() + new Vector2(2f * px, 2f * px), px, 0.2f, Ting, 0.8f);
                }
                Finish();
            }
        }

        /// <summary>Parried: the unit reels back off the deflection and staggers.</summary>
        private void Recoil()
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                Vector2 back = Combat.Clamp(_aFrom - new Vector2(_fwd * 0.3f * _k, 0f));
                _landA = Combat.SafeSpot(back) ? back : _aFrom;
                if (Visible)
                {
                    Fx.HitSpark(BodyPos() + new Vector2(_fwd * 3f * Px, 0f), Px, 0, new Vector2(-_fwd, 0.3f), 3);
                }
            }
            float k = Mathf.Clamp01(_pt / TRecoil);
            Pose.Ground = Vector2.Lerp(_aFrom, _landA, Anim.OutCubic(k));
            Pose.Height = 0f;
            Pose.FaceRight = _fwd > 0f;
            float amp = Anim.Spring(_pt, 8f, 14f);
            Pose.Rot = -26f * amp;
            Pose.SX = 1f - 0.08f * amp;
            Pose.SY = 1f + 0.06f * amp;
            if (k >= 1f)
            {
                Finish();
                Combat.Stagger(A, 0.35f);
            }
        }

        private void Finish()
        {
            if (Held && !_p.Finished)
            {
                if (!Combat.SafeSpot(_p.Pose.Ground) && Combat.FindLanding(_p.Pose.Ground, 2f, out Vector2 spot))
                {
                    _p.Pose.Ground = spot;
                }
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            if (!_flow)
            {
                // (a Master and up never waits on an ordinary weapon swing between techniques)
                A.startAttackCooldown();
            }
            KillHeld();
            Finished = true;
        }

        // ============================================================= victim ==

        /// <summary>Moves the victim through its flight, landing, toss and fall.</summary>
        private void Victim(float dt)
        {
            switch (_vs)
            {
                case VState.Fly:
                    Fly(dt);
                    break;
                case VState.Tumble:
                    Tumble(dt);
                    break;
                case VState.Up:
                    Up(dt);
                    break;
                case VState.Hang:
                    Hang(dt);
                    break;
                case VState.Meteor:
                    Meteor(dt);
                    break;
                case VState.Bounce:
                    Bounce(dt);
                    break;
            }
        }

        // ---------------------------------------------- the flight (I, II) --

        private void VFly()
        {
            _fFrom = _p.Pose.Ground;
            _fH0 = _p.Pose.Height;
            _fTo = FlightEnd(_fFrom, _dirThrow, _len, out _blocked);
            float d = Vector2.Distance(_fFrom, _fTo);
            // a master throws harder: the body covers the same lane a little faster
            float fast = _flow ? 1.15f : 1f;
            _fDur = Level >= 2 ? Mathf.Clamp(d / (13f * fast), 0.16f, 0.55f) : Mathf.Clamp(d / (9.5f * fast), 0.14f, 0.4f);
            _lastFly = _fFrom;
            _lastTrail = _fFrom;
            _lastWreck = _fFrom;
            _vs = VState.Fly;
            _vt = 0f;
            if (Level >= 2 && Kit.Visible(_fFrom))
            {
                Kit.SpeedLines(VBody, _dirThrow, _p.Px, 5, 4f, 0.16f, Fx.WithA(_pal.Core, 0.85f));
            }
        }

        private void Fly(float dt)
        {
            float vpx = _p.Px;
            float k = Mathf.Clamp01(_vt / _fDur);
            Pose vp = _p.Pose;
            vp.Ground = Vector2.Lerp(_fFrom, _fTo, k);
            float h;
            if (Level >= 2)
            {
                // low and flat, wobbling, dropping only at the very end
                h = Mathf.Lerp(_fH0, 0.5f * _k, Anim.OutQuad(Mathf.Clamp01(k / 0.2f))) * (1f - Anim.Smooth(0.8f, 1f, k))
                    + 0.04f * _k * Mathf.Sin(_vt * 38f) * (1f - k);
            }
            else
            {
                // a lob
                h = Mathf.Lerp(_fH0, 0f, k) + 0.55f * _k * Mathf.Sin(Mathf.PI * k);
            }
            vp.Height = Mathf.Max(0f, h);
            if (Mathf.Abs(_dirThrow.x) > 0.15f)
            {
                vp.FaceRight = _dirThrow.x > 0f;
            }
            vp.Rot += (Level >= 2 ? 1080f : 720f) * dt;
            vp.SX = 1f;
            vp.SY = 1.08f;
            vp.HideShadow = true;
            _p.Pose = vp;

            Bowling(_lastFly, vp.Ground);
            _lastFly = vp.Ground;
            Plough(vp.Ground);

            if (Kit.Visible(vp.Ground))
            {
                FlyFx(dt, vpx);
            }
            if (k >= 1f)
            {
                VTumble();
            }
        }

        private void FlyFx(float dt, float vpx)
        {
            _lines -= dt;
            if (_lines <= 0f)
            {
                _lines = Level >= 2 ? 0.04f : 0.07f;
                Kit.SpeedLines(VBody, _dirThrow, vpx, Level >= 2 ? 2 : 1, 4f, 0.14f,
                    Level >= 2 ? Fx.WithA(_pal.Core, 0.8f) : new Color(1f, 1f, 1f, 0.7f));
            }
            if (Level >= 2)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.025f;
                    Fx.Afterimage(_p, 0.11f, _pal.Ghost, _pal.GhostEnd);
                }
                // the furrow it ploughs in the air above the ground: dust kicked up below it
                _puff -= dt;
                if (_puff <= 0f)
                {
                    _puff = 0.05f;
                    Vector2 g = _p.Pose.Ground;
                    Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(g)), 0.35f);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], g + Random.insideUnitCircle * 1.5f * vpx, vpx, Random.Range(0.35f, 0.5f), FxLayer.Behind, 2);
                    p?.Moving(-_dirThrow * Random.Range(10f, 20f) * vpx, Random.Range(3f, 6f) * vpx, 4f)
                        .Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
                }
            }
        }

        /// <summary>A tile of the trench every this many tiles of the body's flight.</summary>
        private const float TrenchStep = 1f;

        /// <summary>
        /// Level II: the body ploughs a trench behind it as it flies (Kit.Ravage one tile at a time
        /// as it passes: a blasted furrow with scorched shoulders, a few calls over the flight,
        /// never every frame). No fire: the unit may run down this lane after its throw.
        /// </summary>
        private void Plough(Vector2 g)
        {
            int force = Level >= 2 ? Mathf.Min(_wreck, 2) : 0;
            if (force <= 0)
            {
                return;
            }
            Vector2 run = g - _lastWreck;
            int guard = 0;
            while (run.sqrMagnitude >= TrenchStep * TrenchStep && guard++ < 12)
            {
                _lastWreck += run.normalized * TrenchStep;
                SpinWreck.At(A, _lastWreck, 1f, force, Pose.Ground, 0f, false);
                run = g - _lastWreck;
            }
        }

        /// <summary>Every enemy the flying body passes through is bowled over (each one once).</summary>
        private void Bowling(Vector2 from, Vector2 to)
        {
            // half a tile either side of the path (FoesAround adds each pin's own half-size)
            float r = 0.3f * _k;
            float d = Vector2.Distance(from, to);
            int n = Mathf.Clamp(Mathf.CeilToInt(d / 0.35f), 1, 12);
            for (int i = 1; i <= n; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, i / (float)n);
                foreach (Actor u in Combat.FoesAround(A, at, r))
                {
                    Bowl(u, at);
                }
            }
        }

        private void Bowl(Actor u, Vector2 at)
        {
            long id = u.Sid();
            if (u == _v || !_bowled.Add(id))
            {
                return;
            }
            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
            Vector2 body = u.current_position + new Vector2(0f, u.position_height + 4f * vpx);
            if (!Combat.Allowed(A, u, out float cm))
            {
                // too heavy to bowl over: it takes a knock and rocks
                Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                Combat.Stagger(u, 0.35f);
                if (Kit.Visible(body))
                {
                    Fx.HitSpark(body, Px, 0, -_dirThrow, 2);
                }
                return;
            }
            if (Combat.Reach(this, u, true) != Contact.Hit)
            {
                return;
            }
            Combat.Hit(A, u, (Level <= 1 ? 0.5f : 1f) * cm, true, true, 0f, this);
            _pins++;
            // knocked off the path, mostly sideways, a little onward: bowling pins
            Vector2 side = new Vector2(-_dirThrow.y, _dirThrow.x);
            if (Vector2.Dot(u.current_position - at, side) < 0f)
            {
                side = -side;
            }
            Vector2 away = (side * 0.8f + _dirThrow * 0.6f).normalized;
            float dist = (Level <= 1 ? 0.6f : 1.3f) * _k * _scale;
            float apex = (Level <= 1 ? 0.3f : 0.75f) * _k;
            if (Bulk(u) <= 80f)
            {
                dist *= 1.6f;
                apex *= 1.3f;
            }
            Combat.LaunchDir(u, away, dist, apex, Level <= 1 ? 0f : (Random.value < 0.5f ? -720f : 720f));
            // (Combat.Daze: makeStunned flashes the unit white again whenever its hit flash is over)
            Combat.Daze(u, Level <= 1 ? 0.8f : 1.2f);
            _p.Flash(0.04f);
            Anim.Sound("event:/SFX/OTHER/Punch", u.current_tile);
            if (Kit.Visible(body))
            {
                Fx.HitSpark(body, Px, Level <= 1 ? 0 : 1, away + Vector2.up * 0.3f, Level <= 1 ? 3 : 5);
                if (Level >= 2)
                {
                    TierFlash(u, 0.12f);
                }
            }
            if (Level >= 2 && _holds < 3)
            {
                _holds++;
                _hold = 0.04f;   // the body jolts on each pin (a local hit-stop)
            }
        }

        // --------------------------------------------- the landing (I, II) --

        private void VTumble()
        {
            Vector2 at = _fTo;
            WorldTile tile = Combat.TileAt(at);
            float vpx = _p.Px;
            float slide = (Level <= 1 ? 0.35f : 0.8f) * _k * (_critter ? 1.5f : _heavy ? 0.5f : 1f);
            _slideTo = Combat.Clamp(at + _dirThrow * slide);
            if (_blocked || !Combat.ClearPath(at, _slideTo))
            {
                _slideTo = at;
            }
            _vs = VState.Tumble;
            _vt = 0f;
            _vh0 = 0f;
            Landed++;

            // the ground hits back: the only lethal blow, harder against a wall (the Thrown link is
            // left when the body is handed back alive: LetGo; one this landing kills leaves none)
            float mult = (Level <= 1 ? 1f : 2f) * _vcm;
            HitResult r = Combat.Hit(A, _v, mult, true, true, 0f, this);
            if (_blocked && _v.isAlive())
            {
                Combat.Hit(A, _v, 0.3f * _vcm, true, true, 0f, this);
            }
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound(Level >= 2 ? "event:/SFX/HIT/HitGeneric" : "event:/SFX/HIT/HitFlesh", tile);
            // the ground takes the body for real: a crater at II (wider for a stronger thrower; no
            // fire, the unit may pursue down here), only a scorch at I and only under a heavy body
            if (Level >= 2)
            {
                SpinWreck.At(A, at, 1.8f * _k * _wreckW, Mathf.Min(_wreck, 2), Pose.Ground, 0f, false);
            }
            else if (_heavy || _wreck > 0)
            {
                SpinWreck.At(A, at, 1f, Mathf.Max(_wreck, 1), Pose.Ground, 0f, false);
            }
            if (Kit.Visible(at))
            {
                Fx.LandingPuff(at, vpx, tile);
                Kit.DustRing(at, vpx, tile, Level <= 1 ? 6 : 10, 4.5f * (Level <= 1 ? 0.5f : 0.9f) * _k / vpx, 0.5f);
                if (Level >= 2)
                {
                    Kit.Crater(at, vpx * 0.8f * _k, tile, false, 3f, true);
                    Kit.Shake(at, 0.16f, 0.006f, _k);
                    if (r.Crit)
                    {
                        Kit.ImpactFrame(_p.BodyPos(), vpx, 0.4f, _pal, true);
                    }
                }
                if (_blocked)
                {
                    Kit.Debris(at, vpx, tile, 6, _k);
                }
            }
            // whoever it lands on goes down too
            foreach (Actor u in Combat.FoesAround(A, at, 0.6f * _k))
            {
                Bowl(u, at);
            }
            // the landing's own flash, after the pins (each pin's white jolt would turn it white)
            _p.Flash(0.08f, Level >= 2 ? TierTint(_v) : Color.white);
        }

        private void Tumble(float dt)
        {
            float t = TumbleT;
            float k = Mathf.Clamp01(_vt / t);
            Pose vp = _p.Pose;
            vp.Ground = Vector2.Lerp(_fTo, _slideTo, Anim.OutQuad(Mathf.Clamp01(k / 0.75f)));
            // bounces: one at I, two at II
            float h;
            if (Level <= 1)
            {
                h = 0.2f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.55f));
            }
            else
            {
                h = k < 0.42f ? 0.3f * _k * Mathf.Sin(Mathf.PI * k / 0.42f)
                    : k < 0.7f ? 0.11f * _k * Mathf.Sin(Mathf.PI * (k - 0.42f) / 0.28f) : 0f;
            }
            vp.Height = h;
            if (k < 0.7f)
            {
                vp.Rot += (Level <= 1 ? 540f : 900f) * (1f - k) * dt;   // rolling, slowing
                vp.SX = vp.SY = 1f;
            }
            else if (k < 0.86f)
            {
                vp.Rot = Mathf.LerpAngle(vp.Rot, 90f, Mathf.Clamp01(dt * 25f));   // flat out
                vp.SX = 1.08f;
                vp.SY = 0.92f;
            }
            else
            {
                // sits up, dazed
                vp.Rot = Mathf.LerpAngle(vp.Rot, 0f, Mathf.Clamp01(dt * 20f));
                vp.SX = 1f;
                vp.SY = 0.94f;
            }
            vp.HideShadow = false;
            _p.Pose = vp;
            if (Kit.Visible(vp.Ground) && k < 0.7f && vp.Height < 0.05f)
            {
                _puff -= dt;
                if (_puff <= 0f)
                {
                    _puff = 0.06f;
                    Fx.LandingPuff(vp.Ground, _p.Px, Combat.TileAt(vp.Ground));
                }
            }
            if (k >= 1f)
            {
                LetGo(Level <= 1 ? 1f : 1.5f);
            }
        }

        /// <summary>Hands the victim back on the ground, dazed, and leaves the chain hook.</summary>
        private void LetGo(float stun)
        {
            Pose vp = _p.Pose;
            vp.Height = 0f;
            vp.Rot = 0f;
            vp.SX = vp.SY = 1f;
            vp.Nudge = Vector2.zero;
            _p.Pose = vp;
            _p.Release(true);
            _vs = VState.Done;
            KillVShadow();
            if (Combat.Living(_v))
            {
                // (no makeStunned: its white flash popped on the body as it sat up, long after the landing's)
                Combat.Daze(_v, stun);
                EndTarget = _v;
                if (Level <= 2)
                {
                    EndLink = Link.Thrown;   // (III: the crash decided it)
                }
            }
        }

        // ------------------------------------------- level III: up and down --

        private void VToss()
        {
            _upFrom = _p.Pose.Ground;
            float d = Vector2.Distance(_upFrom, _aim);
            _upTo = Combat.Clamp(_upFrom + _dirThrow * Mathf.Min(0.25f * d, 1.2f * _k));
            _vh0 = _p.Pose.Height;
            _apexH = 6f * _k;
            _vs = VState.Up;
            _vt = 0f;
            if (Kit.Visible(_upFrom))
            {
                Vector2 b = VBody;
                for (int i = 0; i < Fx.Count(6); i++)
                {
                    Particle l = Fx.Spawn(Art.Lines[Random.Range(1, 3)], b + new Vector2(Random.Range(-3f, 3f), Random.Range(-2f, 6f)) * Px, Px, 0.18f, FxLayer.Top, 1);
                    l?.Moving(new Vector2(0f, 45f * Px)).Color(Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
                }
            }
        }

        private void Up(float dt)
        {
            float k = Mathf.Clamp01(_vt / TUp);
            Pose vp = _p.Pose;
            vp.Height = Mathf.Lerp(_vh0, _apexH, Anim.OutCubic(k));
            vp.Ground = Vector2.Lerp(_upFrom, _upTo, Anim.OutQuad(k));
            float s = Mathf.Clamp01(k * 2.5f);
            vp.SX = Mathf.Lerp(0.85f, 1f, s);
            vp.SY = Mathf.Lerp(1.2f, 1f, s);
            vp.Rot += 540f * dt * (1f - 0.8f * k);
            vp.HideShadow = true;
            _p.Pose = vp;
            if (Kit.Visible(vp.Ground) && k < 0.6f)
            {
                _lines -= dt;
                if (_lines <= 0f)
                {
                    _lines = 0.03f;
                    Vector2 at = VBody + new Vector2(Random.Range(-3f, 3f) * _p.Px, -Random.Range(3f, 9f) * _p.Px);
                    Particle l = Fx.Spawn(Art.Lines[Random.Range(0, 2)], at, _p.Px, 0.14f, FxLayer.Top, 1);
                    l?.Color(Fx.WithA(_pal.Core, 0.6f), Fx.WithA(_pal.Edge, 0f));
                }
            }
            if (k >= 1f)
            {
                _vs = VState.Hang;
                _vt = 0f;
            }
        }

        private void Hang(float dt)
        {
            Pose vp = _p.Pose;
            vp.Height += 0.3f * _k * dt;
            vp.Rot += 140f * dt;
            vp.SX = vp.SY = 1f;
            _p.Pose = vp;
        }

        private void VMeteor()
        {
            _upTo = _p.Pose.Ground;
            _vh0 = _p.Pose.Height;
            _vs = VState.Meteor;
            _vt = 0f;
            _lastTrail = VBody;
            _trail = true;
        }

        private void Meteor(float dt)
        {
            float k = Mathf.Clamp01(_vt / TMeteor);
            float e = k * k;
            Vector2 before = VBody;
            Pose vp = _p.Pose;
            vp.Ground = Vector2.Lerp(_upTo, _aim, e);
            vp.Height = _vh0 * (1f - e);
            // head first along the fall
            Vector2 vel = new Vector2(_aim.x - _upTo.x, (_aim.y - _upTo.y) - _vh0);
            if (Mathf.Abs(vel.x) > 0.05f)
            {
                vp.FaceRight = vel.x > 0f;
            }
            vp.Rot = 90f - Mathf.Atan2(vel.y, Mathf.Abs(vel.x)) * Mathf.Rad2Deg;
            vp.SX = 0.82f;
            vp.SY = 1.3f;
            vp.HideShadow = true;
            _p.Pose = vp;
            if (Kit.Visible(vp.Ground, 0.5f) || Kit.Visible(before, 0.5f))
            {
                FireTrail(_p.Px);
                Fx.Afterimage(_p, 0.1f, new Color(1f, 0.9f, 0.55f, 0.85f), new Color(1f, 0.6f, 0.2f, 0.6f), Fx.WithA(_pal.Edge, 0f));
                if (Random.value < 0.5f)
                {
                    Kit.Ember(VBody + Random.insideUnitCircle * 2f * _p.Px, _p.Px, Random.Range(0.4f, 0.7f), Random.insideUnitCircle * 12f * _p.Px);
                }
            }
            _lastTrail = VBody;
            if (k >= 1f)
            {
                Crash();
                _vs = VState.Bounce;
                _vt = 0f;
            }
        }

        /// <summary>Fire and smoke along the path since last frame, evenly spaced so it reads as one stream.</summary>
        private void FireTrail(float vpx)
        {
            if (!_trail)
            {
                return;
            }
            Vector2 now = VBody;
            float step = 1.6f * vpx / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 30);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * vpx;
                Particle core = Fx.Spawn(Art.Puffs[0], at, vpx, 0.09f, FxLayer.Behind, 3);
                core?.Sized(0.7f, 0.35f).Color(Kit.Core, Fx.WithA(Kit.Fire1, 0.7f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * vpx, vpx, Random.Range(0.15f, 0.22f), FxLayer.Behind, 2);
                flame?.Moving(Random.insideUnitCircle * 5f * vpx, 6f * vpx, 2f)
                    .Sized(0.75f, 0.45f)
                    .Color(Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Kit.Fire2, 0.85f), Fx.WithA(Kit.Fire3, 0.45f), Fx.WithA(Kit.Fire3, 0f));
                if (Random.value < 0.18f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at + Random.insideUnitCircle * 1.5f * vpx, vpx, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0);
                    smoke?.Moving(Random.insideUnitCircle * 4f * vpx, 8f * vpx, 1.5f)
                        .Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(Kit.Smoke, 0f), Fx.WithA(Kit.Smoke, 0.32f), Fx.WithA(Kit.Smoke, 0f));
                    if (smoke != null)
                    {
                        smoke.Delay = 0.06f;
                    }
                }
            }
        }

        /// <summary>The meteor lands on the crowd: a glowing crater, a shockwave, everyone thrown.</summary>
        private void Crash()
        {
            float px = Px;
            Vector2 at = _aim;
            WorldTile tile = Combat.TileAt(at);
            _trail = false;
            KillMarker();
            Landed++;
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);

            HitResult r = Combat.Hit(A, _v, 3f * _vcm, true, true, 0f, this);
            _p.Flash(0.1f, TierTint(_v));
            int hits = 0;
            // (a stronger thrower's meteor blasts a wider crowd: a God's 2.6 tiles out)
            float blast = 2f * _k * _scale;
            foreach (Actor u in Combat.FoesAround(A, at, blast))
            {
                if (u == _v)
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.35f);
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                float near = 1f - Mathf.Clamp01(Vector2.Distance(u.current_position, at) / blast);
                Combat.Hit(A, u, Mathf.Lerp(2f, 2.6f, near) * cm, true, true, 0f, this);
                hits++;
                TierFlash(u, 0.12f);
                float dist = Mathf.Lerp(1.6f, 2.8f, near) * _k * _scale * (Bulk(u) <= 80f ? 1.4f : 1f);
                Combat.Launch(u, at, dist, Mathf.Lerp(1f, 1.8f, near) * _k, Random.value < 0.5f ? -720f : 720f);
            }
            // a crowd blown apart is something to chase (a dash, a wave, a leap); a lone passenger
            // lies stunned in its crater for the next technique to take
            // (Combat.Living: one the crash just killed is still isAlive() until the game's death check)
            EndLink = hits >= 1 ? Link.Scattered : r.Killed || !Combat.Living(_v) ? Link.None : Link.Down;
            EndTarget = Combat.Living(_v) ? _v : null;
            Crater(at);
            if (Dev.On)
            {
                Dev.Trace("hurricane " + Id + " meteor hits " + hits + (r.Crit ? " (crit)" : ""));
            }
            if (Kit.Visible(at))
            {
                Kit.ImpactFrame(at + new Vector2(0f, 3f * px), px, 0.85f * _k, _pal, r.Crit);
                Kit.Crater(at, px * _k, tile, true, 5f, false, _pal.Edge);
                Color c = _pal.Edge;
                Fx.Ring(at, px, 0.42f, 6f, blast / px, 3, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                    Color.white, Fx.WithA(_pal.Core, 0.8f), Fx.WithA(c, 0f));
                Particle ring2 = Fx.Ring(at, px, 0.4f, 8f, 1.3f * _k * _scale / px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
                if (ring2 != null)
                {
                    ring2.Delay = 0.07f;
                }
                DustWall(at, px, tile);
                Kit.Debris(at, px, tile, 12, _k);
                Kit.Sparks(at + new Vector2(0f, 2f * px), px, 10, 110f, 170f, Kit.Fire1);
                for (int i = 0; i < Fx.Count(8); i++)
                {
                    Kit.Ember(at + Random.insideUnitCircle * 6f * px, px, Random.Range(0.8f, 1.4f),
                        new Vector2(Random.Range(-8f, 8f), Random.Range(15f, 32f)) * px, _pal.Edge);
                }
                // (a stronger thrower's meteor lands heavier: a God's shakes half again as hard)
                Kit.Shake(at, 0.3f, 0.011f * (1f + 1.5f * (_scale - 1f)), _k);
            }
            Kit.Stop(at, Mathf.Min(0.12f, 0.1f + 0.07f * (_scale - 1f)));   // 0.1 s, a God's 0.12
        }

        /// <summary>
        /// The meteor's crater, torn into the world on the impact frame: a blasted bowl about as wide
        /// as the blast, a burning core (Grandmaster: fire; God: fire and a deeper bite), and a God's
        /// ejecta coming down in smaller craters all round over the next few tenths of a second.
        /// Only the pocket where the unit itself comes down is left unburnt and undug (Kit.Ravage's
        /// keep): in game the whole core stepped down to a plain blast whenever the unit landed
        /// within 3.5 tiles (it always does), and the fire burned on the far side only.
        /// </summary>
        private void Crater(Vector2 at)
        {
            Vector2 keep = _phase == Phase.Descent || _phase == Phase.Land ? _landA : Pose.Ground;
            float keepR = KeepR;
            SpinWreck.At(A, at, 2.6f * _k * _wreckW, Mathf.Min(_wreck, 2), keep, keepR, false);
            if (_wreck >= 3)
            {
                float core = 1.6f * _k * _wreckW;
                SpinWreck.At(A, at, core, _wreck, keep, keepR);
                // the rim catches: fire round the bowl that spreads into the grass
                SpinWreck.Ignite(A, at, core, _god ? 8 : 5, keep, keepR);
            }
            if (!_god)
            {
                return;
            }
            int n = 6;
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + (i + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / n;
                float d = Random.Range(3.2f, 4.8f) * _k * _scale;
                _ejecta.Add(new Ejecta
                {
                    At = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * d,
                    When = T + Random.Range(0.12f, 0.4f)
                });
            }
        }

        /// <summary>How wide a pocket round where the unit comes down the crater leaves undug and unburnt.</summary>
        private float KeepR => 1.3f * _k;

        /// <summary>A God's ejecta landing (all that is left when the move ends, with 'all').</summary>
        private void Rockfall(bool all)
        {
            for (int i = _ejecta.Count - 1; i >= 0; i--)
            {
                Ejecta e = _ejecta[i];
                if (!all && T < e.When)
                {
                    continue;
                }
                _ejecta.RemoveAt(i);
                Vector2 keep = _phase == Phase.Descent || _phase == Phase.Land ? _landA : Pose.Ground;
                SpinWreck.At(A, e.At, 1f, 2, keep, KeepR, false);
                if (Kit.Visible(e.At))
                {
                    WorldTile tile = Combat.TileAt(e.At);
                    float px = Px;
                    Kit.Crater(e.At, px * 0.8f * _k, tile, true, 3f, true, _pal.Edge);
                    Fx.LandingPuff(e.At, px, tile);
                    Kit.Debris(e.At, px, tile, 3, _k);
                }
            }
        }

        private void DustWall(Vector2 at, float px, WorldTile tile)
        {
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.45f);
            int n = Fx.Count(18);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.35f, 0.35f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + dir * 3f * px, px, Random.Range(0.7f, 1f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * Random.Range(4.5f, 6f) * _k, Random.Range(0.4f, 1f) * px * 10f, 2.4f)
                    .Sized(0.45f, 1.3f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.55f), Fx.WithA(dust, 0.2f), Fx.WithA(dust, 0f));
            }
        }

        private void Bounce(float dt)
        {
            float k = Mathf.Clamp01(_vt / TBounce);
            Pose vp = _p.Pose;
            vp.Ground = _aim;
            vp.Height = 0.35f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.75f)) * (1f - 0.3f * k);
            // spiked head first, it flops flat, then sits up dazed
            vp.Rot = k < 0.75f ? Mathf.LerpAngle(180f, 90f, Anim.OutQuad(k / 0.75f)) : Mathf.LerpAngle(90f, 0f, Anim.OutQuad((k - 0.75f) / 0.25f));
            float amp = Anim.Spring(_vt, 10f, 20f);
            vp.SX = 1f + 0.2f * amp;
            vp.SY = 1f - 0.25f * amp;
            vp.HideShadow = false;
            _p.Pose = vp;
            if (k >= 1f)
            {
                LetGo(2f);
            }
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>
        /// Where a body thrown from 'from' along 'dir' for up to 'len' tiles comes down: the furthest
        /// solid ground before anything it can't fly through ('blocked': a wall or mountain stopped
        /// it short). It may cross water on the way, never land in it.
        /// </summary>
        private static Vector2 FlightEnd(Vector2 from, Vector2 dir, float len, out bool blocked)
        {
            blocked = false;
            Vector2 best = from;
            bool found = Combat.SafeSpot(from);
            const float step = 0.25f;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = from + dir * Mathf.Min(len, i * step);
                if (p.x < 1f || p.y < 1f || p.x >= MapBox.width - 1 || p.y >= MapBox.height - 1)
                {
                    blocked = true;
                    break;
                }
                WorldTile t = Combat.TileAt(p);
                if (t == null || t.Type == null)
                {
                    break;
                }
                if (t.Type.block)
                {
                    blocked = true;
                    break;
                }
                if (Combat.SafeTile(t))
                {
                    best = p;
                    found = true;
                }
            }
            if (!found && Combat.FindLanding(from, 2f, out Vector2 spot))
            {
                best = spot;
            }
            return best;
        }

        /// <summary>Each half of a crescent lives this share of the whole crescent's life...</summary>
        private const float HalfLife = 0.6f;

        /// <summary>...and the front half starts this share of it after the back half.</summary>
        private const float HalfGap = 0.3f;

        /// <summary>
        /// A crescent round the swing at 'lift' above the ground, one turn in the swing's direction.
        /// Drawn as two half-sweeps one after the other: the half passing behind the pair (the top of
        /// the ellipse on screen) under the units, the half in front over them, so the swoosh wraps
        /// round the thrower instead of being drawn across its body. 'behind' keeps it all under the
        /// units (the dust ring at the feet).
        /// </summary>
        private void Crescent(float radiusTiles, float ratio, int thick, float life, float lift, float startDeg, Color core, Color edge, bool behind = false)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(radiusTiles / Px), 3, 130);
            float sign = _sign != 0f ? _sign : 1f;
            Vector2 c = _p != null ? _c : Pose.Ground;
            if (behind)
            {
                CrescentPart(Art.Arc(r, ratio, startDeg, startDeg + 360f * sign, thick), FxLayer.Behind, life, 0f, c, lift, core, edge);
                return;
            }
            float half = 180f * sign;
            for (int i = 0; i < 2; i++)
            {
                float a0 = Mathf.Repeat(startDeg + half * i, 360f);   // (few distinct arcs to draw and cache)
                bool back = Mathf.Sin((a0 + 90f * sign) * Mathf.Deg2Rad) > 0f;
                CrescentPart(Art.Arc(r, ratio, a0, a0 + half, thick), back ? FxLayer.Behind : FxLayer.Over,
                    HalfLife * life, HalfGap * life * i, c, lift, core, edge);
            }
        }

        private void CrescentPart(Sprite[] arc, FxLayer layer, float life, float delay, Vector2 c, float lift, Color core, Color edge)
        {
            Particle p = Fx.Spawn(arc, c + new Vector2(0f, lift + Pose.Height), Px, life, layer, 5);
            if (p == null)
            {
                return;
            }
            p.Delay = delay;
            p.Color(core, edge, Fx.WithA(edge, 0.6f));
            p.Step = (q, d) => q.Pos = c + new Vector2(0f, lift + Pose.Height);
        }

        /// <summary>A puff of dust climbing the tornado on a helix.</summary>
        private void Helix(float px, float angle, float radius, float rise)
        {
            float life = Random.Range(0.42f, 0.55f);
            Vector2 c = _c;
            float dir = _sign;
            Vector2 at = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * Flat) * radius * 0.75f;
            Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at, px, life, Mathf.Sin(angle) < 0f ? FxLayer.Top : FxLayer.Behind, 4);
            if (p == null)
            {
                return;
            }
            Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(c)), 0.35f);
            p.Sized(0.7f, 1.15f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
            p.Step = (q, d) =>
            {
                float k = q.K;
                float a = angle + dir * 7f * q.Age;
                float r = radius * (0.75f + 0.45f * k);
                q.Pos = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Flat) * r;
                q.Z = rise * k;
            };
        }

        /// <summary>A scuff of dust under the turning feet.</summary>
        private void Scuff(float px, int n)
        {
            Vector2 at = Pose.Ground;
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            for (int i = 0; i < n; i++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], at + new Vector2(side * Random.Range(1f, 3f), Random.Range(-1f, 0.5f)) * px,
                    px, Random.Range(0.3f, 0.45f), FxLayer.Behind, 2);
                p?.Moving(new Vector2(side * Random.Range(8f, 16f), Random.Range(-2f, 2f)) * px * (_big ? 1.4f : 1f), 3f * px, 5f)
                    .Sized(_big ? 0.8f : 0.6f, _big ? 1.3f : 1f)
                    .Color(Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>A crab takes hold with a claw: two pincer tips snap shut on the victim.</summary>
        private static void Pinch(Vector2 at, float px)
        {
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                Particle p = Fx.Spawn(Art.Chunks[7], at + new Vector2(-2f * px, s * 3f * px), px, 0.12f, FxLayer.Top, 21);
                if (p == null)
                {
                    return;
                }
                p.Rot = s > 0f ? 180f : 0f;
                p.Moving(new Vector2(0f, -s * 40f * px), 0f, 12f);
                p.Color(Color.white, Pale, Fx.WithA(Pale, 0f));
            }
            Fx.Glint(at + new Vector2(0f, 2f * px), px, 0.16f, Color.white, 0.7f);
        }

        /// <summary>A hard blow on a tiered enemy: it flashes its own tier colour.</summary>
        private static void TierFlash(Actor v, float life)
        {
            Tier t = Tiers.Of(v);
            if (t == Tier.None || !Fx.HasSilhouette || v == null || !v.is_visible)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, v.current_position, 1f, life, FxLayer.Over, 1, true);
            if (p == null)
            {
                return;
            }
            long id = v.Sid();
            Color c = Tiers.Color(t);
            p.UseEuler = true;
            p.Color(Fx.WithA(c, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
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

        private static void Place(Particle q, Actor v)
        {
            q.Pos = v.current_position + v.move_jump_offset + new Vector2(0f, v.position_height);
            q.Z = 0f;
            q.Euler = v.current_rotation;
            q.SX = v.current_scale.x;
            q.SY = v.current_scale.y;
        }

        private static Color TierTint(Actor v)
        {
            Tier t = Tiers.Of(v);
            return t != Tier.None ? Tiers.Color(t) : Color.white;
        }

        // ------------------------------------------------------- held effects --

        /// <summary>The victim's own shadow while it is off its feet (the game hides its own when it tilts).</summary>
        private void SpawnVShadow()
        {
            int rx = Mathf.Max(1, Mathf.RoundToInt(3.2f * (_p.Pose.PivotUp / 3.5f)));
            _vShadow = Fx.Spawn(Art.Disk(rx, 0.42f, 0.45f), _p.Pose.Ground, _p.Px, 1f, FxLayer.Ground, 3);
            if (_vShadow == null)
            {
                return;
            }
            _vShadow.Held = true;
            _vShadow.Snap = true;
            _vShadow.Color(new Color(0f, 0f, 0f, 0.3f));
        }

        private void UpdateVShadow()
        {
            if (_vShadow == null || !_vShadow.Alive)
            {
                return;
            }
            if (!Held || _vs == VState.Done || _vs == VState.Bounce || _vs == VState.Tumble)
            {
                KillVShadow();
                return;
            }
            float h = Mathf.Clamp01(_p.Pose.Height / (6f * _k));
            _vShadow.Pos = _p.Pose.Ground;
            _vShadow.Grad = null;
            _vShadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.32f, 0.08f, h));
        }

        /// <summary>Where the meteor will come down: a shadow growing on the ground and a dashed ring closing on it.</summary>
        private void Marker(float px)
        {
            Vector2 at = _aim;
            float total = 1.1f;
            Color edge = _pal.Edge;
            // (the shadow and the closing ring match the blast: wider for a stronger thrower)
            float k0 = _k * _scale;
            _marker = Fx.Spawn(Art.Disk(2, 0.42f, 0.5f), at, px, total, FxLayer.Ground, 2);
            if (_marker != null)
            {
                _marker.Held = true;
                _marker.Snap = true;
                _marker.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Disk(Mathf.RoundToInt(Mathf.Lerp(2f, 7f * k0, k * k)), 0.42f, 0.5f);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.08f, 0.4f, k));
                };
            }
            _markerRing = Fx.Spawn(Art.Ring(20, 1, Art.Ground, 10), at, px, total, FxLayer.Ground, 3);
            if (_markerRing != null)
            {
                _markerRing.Held = true;
                _markerRing.Snap = true;
                _markerRing.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Ring(Mathf.RoundToInt(Mathf.Lerp(20f * k0, 7f * k0, Anim.InOutCubic(k))), 1, Art.Ground, 10);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = Fx.WithA(edge, Mathf.Lerp(0f, 0.85f, Mathf.Clamp01(k * 3f)));
                };
            }
        }

        /// <summary>The unit's own shadow while it leaps after the victim (it shrinks and fades with height).</summary>
        private void SpawnAShadow(float px)
        {
            if (_aShadow != null)
            {
                return;
            }
            _aShadow = Fx.Spawn(Art.Disk(AShadowRx(1f), 0.42f, 0.45f), Pose.Ground, px, 1f, FxLayer.Ground, 3);
            if (_aShadow == null)
            {
                return;
            }
            _aShadow.Held = true;
            _aShadow.Snap = true;
            _aShadow.Color(new Color(0f, 0f, 0f, 0.36f));
        }

        private void UpdateAShadow()
        {
            if (_aShadow == null || !_aShadow.Alive)
            {
                return;
            }
            float h = Mathf.Clamp01(Pose.Height / Mathf.Max(1f, _apexH + _k));
            Sprite s = Art.Disk(AShadowRx(Mathf.Lerp(1f, 0.45f, h)), 0.42f, 0.45f);
            if ((object)_aShadow.R.sprite != s)
            {
                _aShadow.R.sprite = s;
                _aShadow.Frames[0] = s;
            }
            _aShadow.Pos = Pose.Ground;
            _aShadow.Grad = null;
            _aShadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.36f, 0.14f, h));
        }

        private int AShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        private void KillAShadow()
        {
            _aShadow?.Kill();
            _aShadow = null;
        }

        private void KillVShadow()
        {
            _vShadow?.Kill();
            _vShadow = null;
        }

        private void KillMarker()
        {
            _marker?.Kill();
            _markerRing?.Kill();
            _marker = _markerRing = null;
        }

        private void KillHeld()
        {
            KillVShadow();
            KillAShadow();
            KillMarker();
            _trail = false;
        }

        public override void Abort()
        {
            if (Held && !_p.Finished)
            {
                // let go over solid ground, never over a mountain or the sea the swing carried it across
                if (!Combat.SafeSpot(_p.Pose.Ground) && Combat.FindLanding(_p.Pose.Ground, 2f, out Vector2 spot))
                {
                    _p.Pose.Ground = spot;
                }
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            KillHeld();
        }

        public override void OnEnd(bool aborted)
        {
            if (Held && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            KillHeld();
            if (!aborted)
            {
                Rockfall(true);   // the last of a God's ejecta still comes down
            }
            _ejecta.Clear();
        }
    }

    /// <summary>
    /// Hurricane Swing as a technique. Entry: Expert. The AI uses it when an enemy it can lift is
    /// right beside it and there is somewhere worth throwing it: a crowd of two or more 3-8 tiles
    /// away (level I settles for a single enemy as the pin; a Master or above for one, or for none
    /// at all: it hurls a lone opponent down the field and goes after it). It prefers light
    /// victims; critters are the funniest.
    /// </summary>
    internal sealed class HurricaneSwingTech : Technique
    {
        public override string Id => "hurricane_swing";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;

        /// <summary>Grabs the enemy, swings it round like a hammer and throws it into its friends.</summary>
        public override Form Forms => Form.Grab | Form.Throw | Form.Spin | Form.Heavy;

        public override Form FormsAt(int level) => level >= 3 ? Forms | Form.Aerial | Form.Overhead | Form.Area : Forms;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => new[] { 18f, 30f, 50f };
        public override float[] Cooldown => new[] { 9f, 10f, 12f };

        /// <summary>Who the throw would land on (reused: the planner runs several times a second per fighter).</summary>
        private static readonly List<Actor> _pins = new List<Actor>();

        public override bool Accepts(Link link)
        {
            // it is standing right there, or lying dazed: pick it up
            return link == Link.Close || link == Link.Down;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor self = c.Self;
            float k = Mathf.Sqrt(Mathf.Clamp(self.actor_scale / 0.1f, 0.6f, 4f));
            Actor v = Victim(c, out float vcm);
            if (v == null)
            {
                return null;
            }
            // (a stronger thrower throws further and its meteor blasts wider: SpinWreck.Scale)
            float scale = SpinWreck.Scale(c.Tier);
            float clusterR = (level >= 3 ? 2f * scale : 1.6f) * k;
            Vector2 aim;
            int count;
            Actor center = null;
            if (c.Forced && c.ForcePoint.HasValue)
            {
                aim = c.ForcePoint.Value;
                count = Others(c, aim, clusterR, v);
            }
            else if (level <= 1)
            {
                // a crowd close by if there is one, else any single enemy will do as the pin
                aim = Crowd(c, v, 1.4f * k, 2.4f, 4.5f, out count, out center);
                if (count < 2 && Pin(c, v, out Vector2 pinAt, out Actor pin))
                {
                    aim = pinAt;
                    center = pin;
                    count = Mathf.Max(1, Others(c, aim, 1.1f * k, v));
                }
            }
            else
            {
                aim = Crowd(c, v, clusterR, 3f, 8f, out count, out center);
            }
            // a pair is already worth bowling into (in game, waiting for three in a cluster meant it was
            // almost never thrown); a full alley earns the bonus below. A master fights with
            // techniques only: one to bowl into will do, and with no one at all (a one-on-one) it
            // still hurls its opponent down the field and goes after it
            bool flow = c.Info != null && c.Info.Flow;
            int need = level <= 1 || c.Linked || flow ? 1 : 2;
            if (count < need)
            {
                if (!c.Forced && !flow)
                {
                    return null;
                }
                if (count <= 0 && !(c.ForcePoint.HasValue))
                {
                    // nothing to aim at: throw it onward, the way it stands from us
                    Vector2 on = v.current_position - c.Pos;
                    aim = c.Pos + (on.sqrMagnitude > 0.01f ? on.normalized : (self.flip ? Vector2.right : Vector2.left)) * (level <= 1 ? 3f : 4.5f) * scale;
                    center = null;
                }
            }
            aim = Combat.Clamp(aim);
            Vector2 dir = aim - c.Pos;
            if (dir.sqrMagnitude < 0.04f)
            {
                return null;
            }
            float dist = dir.magnitude;
            dir /= dist;
            float len = level <= 1 ? Mathf.Clamp(dist + 0.4f, 2.4f, 3.6f * scale) : Mathf.Clamp(dist + 1.2f, 4.5f, 7f * scale);
            // it must come down on solid ground
            if (level >= 3)
            {
                if (!Combat.FindLanding(aim, 1.5f, out aim))
                {
                    return null;
                }
            }
            else
            {
                Vector2 end = Combat.Clamp(c.Pos + dir * len);
                if (!Combat.SafeSpot(end) && !Combat.FindLanding(end, 2f, out _) && !Combat.SafeSpot(c.Pos + dir * dist))
                {
                    return null;
                }
            }

            // what it is worth: the victim, plus whoever it lands on
            float vm = level <= 1 ? 1f : level == 2 ? 2f : 4.2f;
            float score = c.Value(v, c.Expected(v, vm), vcm);
            List<Actor> pins = _pins;
            pins.Clear();
            if (level <= 1)
            {
                c.CountNear(c.Pos + dir * Mathf.Min(dist, len), 1.1f * k, pins);
            }
            else if (level == 2)
            {
                c.OnRay(dir, len, 0.55f * k, pins);
            }
            else
            {
                c.CountNear(aim, clusterR, pins);
            }
            float pm = level <= 1 ? 0.5f : level == 2 ? 1f : 2.3f;
            foreach (Actor u in pins)
            {
                if (u == v)
                {
                    continue;
                }
                c.Allowed(u, out float cm);
                score += c.Value(u, c.Expected(u, pm), cm);
            }
            // a mob pressing in on it: the whirling body bats aside whoever crowds the swing (the
            // pins in the lane are counted above)
            float clip = level <= 1 ? 0.35f : level == 2 ? 0.5f : 0.6f;
            int mob = 0;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 1.4f * k; i++)
            {
                Actor u = c.Enemies[i];
                if (u == v || pins.Contains(u) || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u, out float mcm))
                {
                    continue;
                }
                score += c.Value(u, c.Expected(u, clip), mcm);
                mob++;
            }
            pins.Clear();
            if (mob >= 2)
            {
                score += 10f;
            }
            if (level >= 2 && count >= 3 || level <= 1 && count >= 1)
            {
                score += level <= 1 ? 15f : 25f;   // the right answer: a bowling alley
            }
            else if (level >= 2 && count == 2)
            {
                score += 10f;
            }
            float bulk = HurricaneSwing.Bulk(v);
            score += bulk <= 80f ? 12f : bulk <= 180f ? 5f : 0f;
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = v,
                Point = aim,
                Dir = dir,
                Count = count,
                Score = score,
                AreaRadius = level >= 3 ? clusterR : 0f,
                Data = new HurricaneSwing.AimData { Center = center, Len = level >= 3 ? 0f : len }
            };
        }

        /// <summary>Who to grab: an enemy right beside it that it may lift, the lighter the better.</summary>
        private static Actor Victim(Ctx c, out float vcm)
        {
            vcm = 1f;
            if (c.Forced)
            {
                Actor t = c.ForceTarget ?? c.LinkTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (t == null || !Combat.CanPuppet(t, c.Self) || !c.Allowed(t, out vcm) || c.Dist(t) > 8f)
                {
                    return null;
                }
                if (!c.Adjacent(t) && !Combat.ClearPath(c.Pos, t.current_position))
                {
                    return null;
                }
                return t;
            }
            Actor best = null;
            float bestScore = float.MinValue;
            Actor focus = c.Focus;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 3f; i++)
            {
                Actor u = c.Enemies[i];
                if (!c.Adjacent(u) || !Combat.Grounded(u) || !Combat.CanPuppet(u, c.Self) || !c.Allowed(u, out float cm))
                {
                    continue;
                }
                float bulk = HurricaneSwing.Bulk(u);
                float s = (bulk <= 80f ? 30f : bulk <= 180f ? 15f : 0f) - Tiers.Power(u) * 2f
                    + (u == focus ? 10f : 0f) - c.EnemyDist[i];
                if (s > bestScore)
                {
                    bestScore = s;
                    best = u;
                    vcm = cm;
                }
            }
            return best;
        }

        /// <summary>
        /// Like Ctx.Cluster (the middle of the biggest group of allowed, grounded enemies between
        /// minRange and maxRange away), but the victim in our hands is never part of the crowd.
        /// </summary>
        private static Vector2 Crowd(Ctx c, Actor v, float r, float minRange, float maxRange, out int count, out Actor center)
        {
            count = 0;
            center = null;
            Vector2 best = c.Pos;
            float r2 = r * r;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                Actor u = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (u == v || d < minRange || d > maxRange || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u))
                {
                    continue;
                }
                Vector2 sum = Vector2.zero;
                int k = 0;
                foreach (Actor w in c.Enemies)
                {
                    if (w != v && Combat.Grounded(w) && (w.current_position - u.current_position).sqrMagnitude <= r2)
                    {
                        sum += w.current_position;
                        k++;
                    }
                }
                Vector2 at = k > 0 ? sum / k : u.current_position;
                int n = Others(c, at, r, v);
                if (n > count)
                {
                    count = n;
                    best = at;
                    center = u;
                }
            }
            return best;
        }

        /// <summary>Allowed, grounded enemies within r of 'at', not counting the victim itself.</summary>
        private static int Others(Ctx c, Vector2 at, float r, Actor v)
        {
            int n = c.CountNear(at, r);
            if (n > 0 && (v.current_position - at).sqrMagnitude <= r * r && Combat.Grounded(v) && !Moves.Locked(v) && c.Allowed(v))
            {
                n--;
            }
            return n;
        }

        /// <summary>Level I with no crowd: the enemy 2-4.5 tiles away with the most friends around it is the pin.</summary>
        private static bool Pin(Ctx c, Actor v, out Vector2 at, out Actor pin)
        {
            at = c.Pos;
            pin = null;
            int best = -1;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                Actor u = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (u == v || d < 2f || d > 4.5f || !Combat.Grounded(u) || !c.Allowed(u))
                {
                    continue;
                }
                int n = c.CountNear(u.current_position, 1f);
                if (n > best)
                {
                    best = n;
                    pin = u;
                    at = u.current_position;
                }
            }
            return pin != null;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (p.Target == null || !p.Target.isAlive())
            {
                return false;
            }
            HurricaneSwing.AimData aim = p.Data as HurricaneSwing.AimData;
            return HurricaneSwing.Begin(a, p.Target, p.Point, aim?.Center, aim != null ? aim.Len : 0f, p.Level, this);
        }
    }
}
