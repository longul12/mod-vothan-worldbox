using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// WINGBREAKER. The ground fighter's answer to whatever rules the sky. It plays out one of three
    /// ways, picked by what it is up against:
    ///
    /// SKYFALL (II/III: a flier, or a body launched into the air, light enough to grab). Level II:
    ///
    ///   CROUCH   0.12  squash and lean back, a dust ring; a tier ring closes on the feet (III: "ting")
    ///   LEAP    ~0.33  stretched launch frame, takeoff ring (III: glowing cracks), speed lines and
    ///                  afterimages; our own shadow stays on the ground, shrinking. The enemy hangs in
    ///                  the air meanwhile (its flight dies out: anime hang time)
    ///   GRAB     0.10  both flash, a held frame with manga focus lines (the first blow)
    ///   SPIN     0.40  the pair cartwheels twice through the air inside a circular swoosh, rising a
    ///                  little; it ends with the enemy upside down under the attacker
    ///   FLING    0.30  the enemy is driven down head first, a dust-trail meteor, the attacker diving
    ///                  0.1 s behind it. III: a fire trail, the attacker rides it down standing on it
    ///                  and stomps once on the way
    ///   IMPACT         II: a 1-tile crater, a 1.5-tile ring, a local hit-stop. III: impact frame,
    ///                  global hit-stop, a 1.5-tile glowing crater, rings out to 2.5 tiles, embers, and
    ///                  everyone within 2 tiles is blown away
    ///   BACKFLIP 0.35  the attacker springs off the enemy and lands beside the crater; the enemy
    ///                  bounces once and stays down: grounded (a flier's wings give out) and stunned
    ///                  3 s (III: 6 s)
    ///
    ///   A god's order on an enemy standing on the ground: the crouch's stomp sends a wave of dust
    ///   racing along the ground to it, the ground erupts under it and throws it up, and the leap
    ///   catches it at the top of its flight. A body only just thrown (the usual link after a
    ///   launch) is carried on up by its launch, tumbling, and caught 2 tiles up.
    ///
    /// SKULL CRACKER (II/III: anything too heavy to grab: dragons, Titans, Crabzilla; also a forced
    /// use on an enemy standing on the ground). A somersault up and over its head, the heel raised,
    /// and an axe kick that chops down onto the crown of its head, the feet landing on it (II); III
    /// bounces off the skull, flips and brings the heel down again on an impact frame. A
    /// flying dragon can't be moved: the attacker meets it at its height, clings to it as it flies,
    /// and falls back down. II rocks it (1.5 s stagger); III knocks it out of the sky: it comes down
    /// (if there is ground under it) and stays down. This is how a Grandmaster fights a dragon.
    ///
    /// RISING UPPERCUT (I). A leap of at most 4 tiles under the enemy (a launched body hangs for it, a
    /// body only just thrown is carried on up by its launch to meet the leap 1.3-1.7 tiles up, a
    /// dragon's belly, a giant's jaw) and two blows: an uppercut, then either a flip kick that knocks
    /// it away (it flies on: Launched), or, when an ally stands near the enemy to finish it (or one
    /// time in three), a forward flip over it and a hammer heel that SPIKES it back into the ground,
    /// where it bounces once and lies stunned (Down). White and grey only, no hit-stop, no decals.
    ///
    /// The enemy can't die before the last blow (every blow before it leaves it at least 1 health).
    /// Anything else that takes it (killed by someone else, the god hand) ends the move early: the
    /// attacker comes down and lands with a glint. A target that answers (parry, evade) is read
    /// before it is caught: an evaded grab closes on empty air, a parried one breaks the leap.
    ///
    /// THE WORLD TAKES THE BLOW (Kit.Ravage, on the impact frame): the Skyfall crash blasts a real
    /// crater (II force 2, III force 3 and the attacker springs clear of the flames), the last heel
    /// on a grounded giant craters the ground under its feet, a dragon knocked out of the sky
    /// ploughs a long trench where it comes down (WingbreakerGround.Gouge); III scorches the ground
    /// it leaps from and a long drop scorches where it lands; I scorches only where it spikes a
    /// body into the earth. A God hits one force harder and half as wide again.
    ///
    /// TEMPO: the backflip, the fall and the landing shrink with the tier's recover beat
    /// (TierInfo.Linger: Expert 0.85 ... God 0.42) and the vault over a giant is quicker too;
    /// Masters and up (TierInfo.Flow) start no attack cooldown (they strike between techniques with
    /// FlowStrikes), and every ending leaves a link when there is something to chain into (a body
    /// that was kicked away and has landed dazed counts as Down).
    ///
    /// AIM: a giant's shape is followed smoothly (a dragon landing mid-leap changes its drawn skull
    /// by several tiles from one frame to the next); a heel that blasts the ground under a giant
    /// sends the attacker down past the crater's rim, on a pocket the blast leaves unlit. Units in
    /// a light move (a flow strike, a blur dash) are fair game: Moves.Locked, not Busy, keeps it off
    /// a target (a Master caught mid leap-strike is grabbed out of the air).
    /// </summary>
    internal sealed class Wingbreaker : Move
    {
        /// <summary>Horizontal reach to a flier, in tiles.</summary>
        public const float Range = 6f;

        internal enum Kind
        {
            Skyfall,
            SkullCracker,
            Uppercut
        }

        private enum Phase
        {
            Crouch,
            Leap,
            Grab,
            Spin,
            Fling,
            Impact,
            Backflip,
            Upper1,
            Upper2,
            Over,
            Drop,
            Rebound,
            Fall,
            Land,
            Recoil
        }

        private enum VState
        {
            Hang,
            Pop,
            Held,
            Slam,
            Bounce,
            Done
        }

        /// <summary>
        /// How a unit is drawn right now: heights of its feet, middle and highest pixels above its ground
        /// point, its half width, and where its skull is: the height of the crown of its head and how far
        /// the head sits ahead of the ground point (signed, world units; a dragon's head is well forward).
        /// </summary>
        internal struct Box
        {
            public float Bottom, Center, Top, Half, Crown, HeadX;
        }

        private const float TCrouch = 0.12f;
        private const float TGrab = 0.02f;      // plus the 0.08 s held frame
        private const float TSpin = 0.4f;
        private const float TFling = 0.3f;
        private const float TFlingLag = 0.1f;
        private const float TImpact = 0.02f;    // plus the hit-stop
        private const float TBackflip = 0.35f;
        private const float TLand = 0.24f;
        private const float TUpper1 = 0.13f;
        private const float TUpper2 = 0.17f;
        private const float TSpike = 0.2f;
        private const float TSlam = 0.12f;
        private const float TDrop = 0.15f;
        private const float TRebound = 0.22f;
        private const float TRecoil = 0.38f;
        private const float TBounce = 0.32f;

        private static readonly Color DustAir = new Color(0.88f, 0.85f, 0.79f, 1f);
        /// <summary>The impact frame's silhouette: a body flat and dark against the flash behind it.</summary>
        private static readonly Color ImpactDark = new Color(0.08f, 0.06f, 0.09f, 1f);
        /// <summary>How long the attacker stays a flat silhouette on an impact frame (move time: it holds through a freeze).</summary>
        private const float SilhouetteTime = 0.06f;

        private readonly Kind _kind;
        private readonly Actor _v;
        private readonly long _vid;
        private Puppet _p;
        private readonly Palette _pal;
        private readonly float _k, _fwd, _maxH, _cm;
        private readonly Vector2 _from;
        private readonly bool _titan;
        /// <summary>A forced Skyfall on an enemy standing on the ground: the stomp pops it up first.</summary>
        private readonly bool _popUp;
        /// <summary>
        /// A Skyfall on a body only just thrown (barely off the ground, the usual link after a launch):
        /// its launch carries it on up into the sky while the attacker leaps, and it is caught up there.
        /// In game the first version caught it where it was, at the grass, and "Skyfall" never left the
        /// ground outside the dev demo.
        /// </summary>
        private readonly bool _carry;
        private readonly float _carryTo;
        /// <summary>How much bigger the heel's shock is against a giant (a dragon is 13 humans tall).</summary>
        private readonly float _big;
        /// <summary>I: the second blow spikes it back into the ground instead of kicking it away.</summary>
        private readonly bool _spike;
        /// <summary>A God performs it: its craters are one force harder and half as wide again.</summary>
        private readonly bool _god;
        /// <summary>Masters and up: no attack cooldown at the end (FlowStrikes fights between techniques).</summary>
        private readonly bool _flow;
        /// <summary>The tail of the move (backflip, fall, landing) is this much of its length: 1 below Master.</summary>
        private readonly float _crisp;
        /// <summary>How far the last fall came down from (a long drop scorches the ground it lands on).</summary>
        private float _dropH;

        private Phase _phase = Phase.Crouch;
        private float _pt, _hold, _tPhase;
        private bool _entered, _hit, _glinted;
        private bool _vGone, _reached, _whiff, _parried, _finisher, _crit;
        private int _drops;

        // the attacker's path in the current phase
        private Vector2 _aFrom, _land;
        private float _hFrom, _rotFrom, _rotTo;
        /// <summary>The leap's extra arc over a low target, and the way it goes (for streaks and speed lines).</summary>
        private float _hop;
        private Vector2 _leapDir = Vector2.up;
        private Vector2 _tg;          // the target's ground point, followed smoothly (fliers move)
        /// <summary>
        /// The target's shape as the attacker aims at it: followed smoothly. In game a dragon that
        /// landed in the middle of the leap changed its measured skull by 5 world units from one
        /// frame to the next (its flying and landed frames), and the attacker, aiming at it,
        /// teleported 130 pixels down in mid-air (r3_wingbreaker_3, frames 16-17).
        /// </summary>
        private Box _box;
        /// <summary>The target's shape this very tick (what is true now: is it in the air?).</summary>
        private Box _raw;
        /// <summary>
        /// The landing chosen when a heel (or the crash) tears up the ground: outside the blast's
        /// burning rim, kept as an unlit pocket (Kit.Ravage keep), so the move's last beat (the
        /// landing, the flare, the glint) is not lost in its own flames (r3_wingbreaker_3: the God
        /// landed inside the fire it had lit and vanished from the shot).
        /// </summary>
        private bool _landKept;
        /// <summary>The box follows the unit itself (false while it is held: it is snapped to it when let go).</summary>
        private bool _tracked = true;
        /// <summary>The unlit pocket kept around a planned landing, in tiles.</summary>
        private const float KeepR = 1.6f;
        private float _headX;         // where its head is (box.HeadX), followed smoothly (a dragon turns around in flight)
        private Vector2 _whiffAim;    // where the enemy stood when it slipped away (ground), and how high it was
        private float _whiffH;

        // the enemy's own little state machine (when it is held)
        private VState _vs = VState.Hang;
        private float _vt, _vh0, _vRise, _vSpin, _vPop0, _vPopH;
        /// <summary>The spiked body: the height and turn it is driven down from, and the turn it bounces from.</summary>
        private float _slamH, _slamRot, _bounceRot = 180f;
        /// <summary>Where the spike's heel came down last tick (kept if the enemy dies to it).</summary>
        private Vector2 _spikeOver;
        private float _spikeTop;
        private bool _spikeSet;
        private Vector2 _vg;

        // the spin and the fling
        private Vector2 _c0, _crater, _vFrom;
        private float _r, _lift, _gy, _phiA0, _alpha, _rotA0, _rotV0, _rotVEnd, _vhFrom;

        // a released enemy is thrown on the next tick (a held unit can't be launched)
        private bool _pend;
        private Vector2 _pendDir;
        private float _pendDist, _pendApex, _pendStagger, _pendMult;

        // effects
        // (only held particles are kept: short-lived ones are pooled and may belong to someone else once they die)
        private Particle _shadow, _vShadow, _glow;
        private float _ghost, _lines, _selfFlash;
        /// <summary>An impact frame is up behind the pair: the attacker is a silhouette against it for this long (white on a Black Flash).</summary>
        private float _framed;
        private bool _framedBlack;
        private Vector2 _lastTrail;
        private bool _trail;
        /// <summary>Which way it was turning last (+1 forward, -1 backward): the fall finishes the turn.</summary>
        private float _turn = 1f;
        private float _vPopT;

        /// <summary>Started / finished since the game started (dev log).</summary>
        internal static int Started, Done;

        private Wingbreaker(Actor a, Actor v, Puppet p, Kind kind, int level, bool forced, bool spike) : base(a)
        {
            Level = level;
            _kind = kind;
            _v = v;
            _vid = v.Sid();
            _p = p;
            _pal = Palette.For(a, level);
            _k = K;
            Tier tier = Tiers.Of(a);
            TierInfo ti = Tiers.Info(tier);
            _god = tier == Tier.God;
            _flow = ti != null && ti.Flow;
            _crisp = Crisp(tier);
            _from = a.current_position;
            float dx = v.current_position.x - _from.x;
            _fwd = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : (a.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            Pose.HideShadow = true;
            _maxH = MaxHeight(level, _k);
            if (!Combat.Allowed(a, v, out _cm))
            {
                _cm = forced ? 0.5f : 0f;   // a god's order on something above its class: a glancing blow
            }
            _titan = Weights.Species(v) >= Weight.Titan;
            _tg = v.current_position;
            _box = Measure(v);
            _raw = _box;
            _headX = _box.HeadX;
            _big = _titan ? Mathf.Clamp(_box.Half / (0.5f * _k), 1f, 2.2f) : 1f;
            // a spike needs something it can drive down, and solid ground under it to drive it into
            _spike = spike && kind == Kind.Uppercut && !_titan && !Weights.Heavy(v) && !v.isFlying() && Combat.SafeSpot(v.current_position);
            if (p != null)
            {
                _vg = v.current_position;
                _vh0 = Mathf.Max(0f, v.position_height);
                // the flight it had: dies out over a few frames instead of stopping dead
                float g = Mathf.Min(Mathf.Max(0.2f, v.stats["mass"]) * SimGlobals.m.gravity, 20f);
                _vRise = v.under_forces ? Mathf.Clamp(v.velocity.z * g, -12f, 18f) : 0f;
                _vSpin = v.position_height > 0.25f ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(240f, 420f) : 0f;
                _popUp = kind == Kind.Skyfall && !v.isFlying() && v.position_height < 0.25f && !Rising(v);
                // I carries a body only just thrown up too, so the Rising Uppercut meets it in the
                // air: in game Masters linked it right after a launch (the body at h 0.02) and the
                // "leap" was a hop along the grass, both blows landing at ground level
                bool low = !v.isFlying() && _vh0 < 1.1f * _k;
                _carry = !_popUp && low && (kind == Kind.Skyfall || kind == Kind.Uppercut && Rising(v));
                _carryTo = kind == Kind.Skyfall
                    ? Mathf.Min(Mathf.Clamp(_vh0 + 1.5f * _k, 1.7f * _k, 2.2f * _k), _maxH * 0.6f)
                    : Mathf.Min(Mathf.Clamp(_vh0 + 1.2f * _k, 1.3f * _k, 1.7f * _k), _maxH * 0.5f);
            }
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>How high it can leap at each level (scaled a little by its size).</summary>
        public static float MaxHeight(int level, float k)
        {
            float h = level <= 1 ? 4f : level == 2 ? 10f : 13.5f;
            return h * Mathf.Clamp(k, 0.85f, 1.4f);
        }

        /// <summary>
        /// How much of the move's tail (backflip, fall, landing) a tier keeps: the tier's recover
        /// beat (TierInfo.Linger: Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42; 1 below).
        /// Masters land crisply and go, gods barely touch the ground.
        /// </summary>
        public static float Crisp(Tier t)
        {
            TierInfo i = Tiers.Info(t);
            return i != null ? Mathf.Clamp(i.Linger, 0.3f, 1f) : 1f;
        }

        /// <summary>
        /// Real damage to the world on one of the big impacts (Kit.Ravage), on the impact frame:
        /// 'radius' in tiles for a human-sized performer (scaled by its size). 'godScale': a God's is
        /// one force harder (max 4) and half as wide again. Mechanics: it runs on or off screen.
        /// </summary>
        private void Ravage(Vector2 at, float radius, int force, bool godScale, string what, bool clear = false)
        {
            if (force <= 0)
            {
                return;
            }
            radius *= Mathf.Clamp(_k, 0.75f, 1.6f);
            if (godScale && _god)
            {
                force = Mathf.Min(4, force + 1);
                radius *= 1.5f;
            }
            if (clear)
            {
                SpringClear(at, radius);
            }
            Kit.Ravage(at, radius, force, A, true, _landKept ? _land : (Vector2?)null, _landKept ? KeepR : 0f);
            if (Dev.On)
            {
                Dev.Trace("wingbreaker " + Id + " ravage " + what + " force " + force + " r " + radius.ToString("F2") + (clear ? " (lands " + Vector2.Distance(at, _land).ToString("F1") + " out)" : ""));
            }
        }

        /// <summary>
        /// The heel is about to blast the ground under a giant: the attacker will spring off its skull
        /// and come down past the crater's rim (the way it was going, else back the way it came), on
        /// a spot the blast leaves unlit.
        /// </summary>
        private void SpringClear(Vector2 at, float radius)
        {
            float rim = Kit.BrushRadius(radius) + 0.7f;
            float away = Mathf.Max(_box.Half + 0.6f * _k, rim);
            _land = SafeLanding(at + new Vector2(_fwd * away, 0f), at - new Vector2(_fwd * away, 0f));
            _landKept = true;
        }

        /// <summary>The Skyfall crash's crater: II blasts the soft ground, III sets it alight (a God's bites deeper and wider).</summary>
        private void CrashRavage(out int force, out float radius)
        {
            force = Level >= 3 ? 3 : 2;
            // a little smaller than the blow's reach (II rocks 1.2 tiles around, III blasts 2)
            radius = (Level >= 3 ? 1.5f : 1f) * Mathf.Clamp(_k, 0.75f, 1.6f);
            if (_god)
            {
                force = Mathf.Min(4, force + 1);
                radius *= 1.5f;
            }
        }

        /// <summary>Up in the air: flying, or thrown at least 'above' world units up.</summary>
        public static bool Airborne(Actor v, float above)
        {
            return v != null && (v.isFlying() || v.position_height > above);
        }

        /// <summary>Thrown and still going up (a launch that has only just left the ground).</summary>
        public static bool Rising(Actor v)
        {
            return v != null && !v.isFlying() && v.under_forces && v.velocity.z > 0f;
        }

        /// <summary>
        /// Can 'by' catch this one in mid-air and throw it around? Like Combat.CanPuppet, but a unit in
        /// the air (or a flier) is exactly what it is for.
        /// </summary>
        public static bool CanGrab(Actor v, Actor by)
        {
            if (v == null || v == by || !v.isAlive() || Moves.Locked(v) || v.isInsideSomething() || v.isInMagnet() || v.is_immovable)
            {
                return false;
            }
            ActorAsset s = v.asset;
            if (!s.can_be_moved_by_powers || !s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render)
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
        /// Which of the three it will be against this enemy. 'forced' (a god's order): a grabbable enemy
        /// standing on the ground is first popped into the sky by a ground wave, then caught.
        /// </summary>
        public static Kind Choose(Actor a, Actor v, int level, bool forced = false)
        {
            if (level <= 1)
            {
                return Kind.Uppercut;
            }
            return CanGrab(v, a) && (Airborne(v, 0.25f) || Rising(v) || forced) ? Kind.Skyfall : Kind.SkullCracker;
        }

        /// <summary>Can it get there at this level (a grab needs the body, a heel the skull, an uppercut the feet)?</summary>
        public static bool Reachable(Kind kind, Box b, float maxH)
        {
            switch (kind)
            {
                case Kind.Skyfall:
                    return b.Center <= maxH + 0.3f;
                case Kind.SkullCracker:
                    return b.Crown <= maxH + 0.6f;
                default:
                    return b.Bottom <= maxH;
            }
        }

        /// <summary>
        /// How a unit is drawn right now: feet, middle and top heights above its ground point, and its
        /// skull. Dragons and other units that draw themselves are measured from the dragon's own frames
        /// (in game captures: a flying dragon's belly is 13 sprite pixels above its shadow, its raised
        /// wing tips 41-44, but its head sits under the wings, crown at 33, 6 pixels ahead of its middle
        /// on the side it faces; a landed one's crown is at 17, 4.5 ahead). The first Skull Cracker aimed
        /// at the highest pixels and both heels landed in the air above the dragon's back.
        /// </summary>
        public static Box Measure(Actor v)
        {
            Box b;
            float s = Mathf.Max(0.02f, v.actor_scale);
            float lift = Mathf.Max(0f, v.position_height);
            float face = v.flip ? 1f : -1f;   // the game: flip = looking right
            if (v.asset.has_avatar_prefab || v.asset.ignore_generic_render || !v.asset.has_sprite_renderer)
            {
                bool fly = v.isFlying();
                b.Bottom = lift + (fly ? 13f : 0f) * s;
                b.Center = lift + (fly ? 27f : 11f) * s;
                b.Top = lift + (fly ? 41f : 22f) * s;
                b.Half = 10f * s;
                if (v.getActorComponent<Dragon>() != null)
                {
                    b.Crown = lift + (fly ? 33f : 17f) * s;
                    b.HeadX = face * (fly ? 6f : 4.5f) * s;
                }
                else
                {
                    b.Crown = b.Top - 0.1f * (b.Top - b.Center);
                    b.HeadX = 0f;
                }
                return b;
            }
            if (v.isHovering() && !Moves.Busy(v))
            {
                lift += Mathf.Max(0f, v.move_jump_offset.y);
            }
            Sprite sp = Fx.UnitSprite(v);
            float h = sp != null ? sp.rect.height : 10f;
            float w = sp != null ? sp.rect.width : 8f;
            b.Bottom = lift;
            b.Center = lift + h * 0.4f * s;
            b.Top = lift + h * s;
            b.Half = Mathf.Max(0.12f, w * 0.3f * s);
            // a beast drawn side on (wider than tall) carries its head forward and a little low
            bool beast = w > h * 1.3f;
            b.Crown = lift + h * (beast ? 0.85f : 0.96f) * s;
            b.HeadX = beast ? face * w * 0.22f * s : 0f;
            return b;
        }

        /// <summary>
        /// Starts a Wingbreaker on 'v'. 'forced': a god's order (class limits only weaken it). 'spike'
        /// (I): the second blow drives it back into the ground instead of kicking it away.
        /// </summary>
        public static bool Begin(Actor a, Actor v, int level, Technique tech, bool forced, bool spike = false)
        {
            if (!Moves.CanPerform(a) || v == null || v == a || !v.isAlive() || Moves.Locked(v) || !Combat.IsFoe(a, v))
            {
                return false;
            }
            Kind kind = Choose(a, v, level, forced);
            bool hold = CanGrab(v, a) && (kind == Kind.Skyfall || (kind == Kind.Uppercut && (Airborne(v, 0.25f) || Rising(v))));
            Puppet p = hold ? new Puppet(v, v.flip) : null;
            Wingbreaker m = new Wingbreaker(a, v, p, kind, level, forced, spike)
            {
                Tech = tech
            };
            if (p != null)
            {
                // it may answer now, before it is caught (once caught, it can't)
                Contact c = Combat.Reach(m, v, false);
                if (c != Contact.Hit)
                {
                    m.Answered(c);
                }
                else
                {
                    // the puppet takes over exactly where the unit is: same height, same tumble
                    p.Pose.Ground = v.current_position;
                    p.Pose.Height = Mathf.Max(0f, v.position_height);
                    p.Pose.Rot = Mathf.DeltaAngle(0f, v.target_angle.z);
                    p.Pose.FaceRight = v.flip;
                    p.Pose.HideShadow = true;
                    if (!Moves.Start(p))
                    {
                        m.Answered(Contact.Evaded);
                    }
                }
            }
            if (!Moves.Start(m))
            {
                if (m._p != null && !m._p.Finished)
                {
                    m._p.Release(m._p.Pose.Height <= 0.02f);
                }
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(a.current_position, v.current_position, 0.5f) + new Vector2(0f, Mathf.Max(2f, m._box.Center * 0.6f)));
            if (Dev.On)
            {
                Dev.Trace("wingbreaker " + m.Id + " " + Techniques.Roman(level) + " " + kind + (m._spike ? " (spike)" : m._carry ? " (carried up)" : "") + " on " + v.asset.id + " h " + v.position_height.ToString("F2")
                    + (v.isFlying() ? " flying" : "") + (m._whiff ? " (evaded)" : m._parried ? " (parried)" : ""));
            }
            return true;
        }

        /// <summary>The enemy answered before the first blow: an evaded move whiffs, a parried one breaks.</summary>
        private void Answered(Contact c)
        {
            if (_p != null && !_p.Finished && Moves.Busy(_p.A) && Moves.TryGet(_p.A, out Move cur) && cur == _p)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            _p = null;
            if (c == Contact.Parried)
            {
                _parried = true;
            }
            else
            {
                _whiff = true;
                _whiffAim = _v != null ? _v.current_position : _from;
                _whiffH = _box.Center;
            }
        }

        private bool Held => _p != null && !_vGone && !_p.Finished;

        private bool Visible => Kit.Visible(Pose.Ground, 0.45f) || Kit.Visible(_tg, 0.45f);

        private Color Ghost0 => _pal.Ghost;

        private Color Ghost1 => _pal.GhostEnd;

        /// <summary>The colour an enemy flashes when it is hit hard: its own tier's, or white.</summary>
        private Color VFlash()
        {
            Tier t = Tiers.Of(_v);
            return t != Tier.None ? Fx.Lighten(Tiers.Color(t), 0.35f) : Color.white;
        }

        /// <summary>Thrown bodies fly by weight: critters comically far, brutes a little, titans not at all.</summary>
        private static float Fly(Actor u)
        {
            switch (Weights.Of(u))
            {
                case Weight.Critter: return 1.7f;
                case Weight.Folk: return 1f;
                case Weight.Brute: return 0.65f;
                case Weight.Fiend: return 0.45f;
                default: return 0.3f;
            }
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (Finished)
            {
                return;
            }
            CheckVictim();
            Pending();
            Track(dt);
            if (_hold > 0f)
            {
                // hit-stop: the two hold their poses for a beat, shaking with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                if (Held)
                {
                    _p.Pose.Nudge = -Pose.Nudge;
                }
                UpdateShadows();
                return;
            }
            Pose.Nudge = Vector2.zero;
            if (Held)
            {
                _p.Pose.Nudge = Vector2.zero;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Crouch:
                    Crouch(dt);
                    break;
                case Phase.Leap:
                    Leap(dt);
                    break;
                case Phase.Grab:
                    Grab(dt);
                    break;
                case Phase.Spin:
                    Spin(dt);
                    break;
                case Phase.Fling:
                    Fling(dt);
                    break;
                case Phase.Impact:
                    Impact(dt);
                    break;
                case Phase.Backflip:
                    Backflip(dt);
                    break;
                case Phase.Upper1:
                    Upper1(dt);
                    break;
                case Phase.Upper2:
                    Upper2(dt);
                    break;
                case Phase.Over:
                    Over(dt);
                    break;
                case Phase.Drop:
                    Drop(dt);
                    break;
                case Phase.Rebound:
                    Rebound(dt);
                    break;
                case Phase.Fall:
                    Fall(dt);
                    break;
                case Phase.Recoil:
                    Recoil(dt);
                    break;
                default:
                    Land(dt);
                    break;
            }
            if (Held)
            {
                Victim(dt);
            }
            UpdateShadows();
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("wingbreaker " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3") + " h=" + Pose.Height.ToString("F2"));
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

        /// <summary>Is the enemy still ours to hit? If something else took it, the attacker comes back down.</summary>
        private void CheckVictim()
        {
            if (_vGone)
            {
                return;
            }
            bool gone = _p != null
                ? _p.Finished || !_v.isAlive() || _v.Sid() != _p.Id
                : !_v.isAlive() || _v.Sid() != _vid || _v.isInMagnet() || _v.isInsideSomething();
            if (!gone)
            {
                return;
            }
            _vGone = true;
            if (_p != null && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            _vShadow?.Kill();
            _vShadow = null;
            _pend = false;
            if (_finisher)
            {
                return;   // it fell to our own last blow: the move plays out
            }
            switch (_phase)
            {
                case Phase.Crouch:
                case Phase.Recoil:
                    break;   // still on the ground: the crouch ends and it lands where it stands
                case Phase.Leap:
                case Phase.Grab:
                case Phase.Spin:
                case Phase.Fling:
                case Phase.Upper1:
                case Phase.Upper2:
                case Phase.Over:
                case Phase.Drop:
                case Phase.Rebound:
                    Next(Pose.Height > 0.05f ? Phase.Fall : Phase.Land);
                    break;
            }
        }

        /// <summary>A released enemy is thrown as soon as it is free.</summary>
        private void Pending()
        {
            if (!_pend)
            {
                return;
            }
            if (_v == null || !_v.isAlive() || _v.Sid() != _vid)
            {
                _pend = false;
                return;
            }
            if (Moves.Locked(_v))
            {
                return;
            }
            _pend = false;
            if (_pendMult > 0f && _cm > 0f)
            {
                // the flip kick's damage, dealt now that it is free (a held body never dies in our hands mid-air)
                HitResult r = Combat.Hit(A, _v, _pendMult * _cm, true, true, 0f, this);
                if (Dev.On)
                {
                    Dev.Trace("wingbreaker " + Id + " blow x" + (_pendMult * _cm).ToString("F2") + " -> " + _v.asset.id + " hp " + _v.getHealth() + " (finisher, let go)" + (r.Crit ? " CRIT" : ""));
                }
                _pendMult = 0f;
                if (!_v.isAlive())
                {
                    return;
                }
            }
            if (_pendStagger > 0f)
            {
                Combat.Stagger(_v, _pendStagger);
            }
            Combat.LaunchDir(_v, _pendDir, _pendDist, _pendApex, (_fwd > 0f ? -1f : 1f) * Random.Range(540f, 800f));
        }

        /// <summary>Follows the target (a dragon keeps flying while it is fought).</summary>
        private void Track(float dt)
        {
            if (_vGone || _v == null)
            {
                return;
            }
            if (_p != null)
            {
                _tg = _p.Pose.Ground;
                _tracked = false;
                return;
            }
            if (!_v.isAlive() || _v.Sid() != _vid)
            {
                return;
            }
            _tg = Vector2.MoveTowards(_tg, _v.current_position, 16f * _k * dt);
            _raw = Measure(_v);
            if (!_tracked)
            {
                // just let go of it (or never held): its shape now, as it is
                _tracked = true;
                _box = _raw;
                return;
            }
            // a dragon landing (or taking off) changes its drawn shape from one frame to the next:
            // the aim glides after it instead of jumping (and the attacker with it)
            _box.Bottom = Ease(_box.Bottom, _raw.Bottom, dt);
            _box.Center = Ease(_box.Center, _raw.Center, dt);
            _box.Top = Ease(_box.Top, _raw.Top, dt);
            _box.Crown = Ease(_box.Crown, _raw.Crown, dt);
            _box.Half = _raw.Half;
            _box.HeadX = _raw.HeadX;
            // a dragon turning in flight swings its head to the other side: the aim follows, it doesn't jump
            _headX = Mathf.MoveTowards(_headX, _box.HeadX, 6f * _k * dt);
        }

        /// <summary>Glides a measured height towards its new value: fast (about a fifth of a second for a dragon landing), never a jump.</summary>
        private float Ease(float from, float to, float dt)
        {
            return Mathf.MoveTowards(from, to, Mathf.Max(4f * _k, Mathf.Abs(to - from) * 12f) * dt);
        }

        /// <summary>Sets the ground point and height that put a body's middle at 'body' over the ground line 'gy'.</summary>
        private static void Place(ref Pose pose, Vector2 body, float gy, float scale)
        {
            pose.Ground = new Vector2(body.x, gy);
            pose.Height = Mathf.Max(0f, body.y - gy - pose.PivotUp * scale * pose.SY);
        }

        private float LeapTime(Vector2 from, Vector2 to, float h)
        {
            return Mathf.Clamp(0.22f + 0.022f * Vector2.Distance(from, to) + 0.02f * h / _k, 0.28f, 0.52f);
        }

        // ------------------------------------------------------------- crouch --

        private void Crouch(float dt)
        {
            float px = Px;
            if (Enter())
            {
                SpawnShadow();
                if (Level >= 2)
                {
                    SpawnGlow();
                }
                if (_popUp)
                {
                    GroundWave(px);
                }
                if (Kit.Visible(_from))
                {
                    WorldTile tile = A.current_tile;
                    Kit.DustRing(_from, px, tile, 7, 22f, 0.36f, _k);
                    if (Level >= 2)
                    {
                        // the ground answers: a tier ring closes on the feet
                        Fx.Ring(_from, px, TCrouch + 0.04f, 14f * _k, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                            Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Core, 0.9f));
                    }
                }
            }
            float c = Anim.OutCubic(Mathf.Clamp01(_pt / 0.08f));
            Pose.Ground = _from;
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1f, 1.14f, c);
            Pose.SY = Mathf.Lerp(1f, 0.8f, c);
            Pose.Rot = -8f * c;
            Pose.Nudge = _pt > 0.07f && Level >= 2 ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            if (!_glinted && Level >= 3 && _pt >= 0.06f && Kit.Visible(_from))
            {
                _glinted = true;
                Vector2 off = new Vector2(_fwd * 3f * px, Pose.PivotUp * 1.5f * px);
                Follow(Fx.Glint(BodyPos() + off, px, 0.18f, _pal.Core, 1f), off);
            }
            if (_pt < TCrouch)
            {
                return;
            }
            Pose.Nudge = Vector2.zero;
            if (_parried)
            {
                Next(Phase.Recoil);
            }
            else if (_vGone)
            {
                Next(Phase.Land);
            }
            else if (_kind == Kind.SkullCracker)
            {
                Next(Phase.Over);
            }
            else
            {
                Next(Phase.Leap);
            }
        }

        /// <summary>The launch: a ring, dust, small cracks (II+), streaks along 'dir' (the way it leaps).</summary>
        private void Takeoff(Vector2 dir)
        {
            float px = Px;
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (Level >= 3)
            {
                // III: the ground it springs from is scorched under its glowing cracks
                Ravage(_from, 1f, 1, false, "takeoff");
            }
            if (!Kit.Visible(_from))
            {
                return;
            }
            WorldTile tile = A.current_tile;
            Color ring0 = Level >= 3 ? _pal.Core : Level == 2 ? _pal.Core : new Color(1f, 1f, 1f, 1f);
            Color ring1 = Level >= 2 ? _pal.Edge : new Color(0.86f, 0.9f, 0.96f, 1f);
            Fx.Ring(_from, px, 0.24f, 3f, 18f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                Fx.WithA(ring0, 0.95f), Fx.WithA(ring1, 0.5f), Fx.WithA(ring1, 0f));
            Kit.DustRing(_from, px, tile, Level >= 2 ? 10 : 7, Level >= 2 ? 50f : 36f, Level >= 2 ? 0.5f : 0.4f, _k);
            if (Level >= 3)
            {
                // only III cracks the ground it leaps from (as Meteor Slam's takeoff): at II the takeoff
                // cracks, the eruption's and the crater's made three 3-tile decals for one move in game
                Kit.Crater(_from, px, tile, true, 1.6f, true, _pal.Edge);
            }
            // streaks shooting off the way it goes (lines are drawn vertical: turned to the direction)
            Vector2 side = new Vector2(-dir.y, dir.x);
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            int lines = Fx.Count(5);
            for (int i = 0; i < lines; i++)
            {
                Vector2 at = _from + new Vector2(0f, 3f * px) + side * Random.Range(-4f, 4f) * px + dir * Random.Range(0f, 9f) * px;
                Particle p = Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, Random.Range(0.12f, 0.2f), FxLayer.Top, 1);
                if (p == null)
                {
                    break;
                }
                p.Rot = ang;
                p.Moving(dir * 30f * px).Color(Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
            }
        }

        /// <summary>A forced Skyfall on a grounded enemy: the stomp sends a wave of dust racing along the ground to it.</summary>
        private void GroundWave(float px)
        {
            Vector2 to = _vg;
            float d = Vector2.Distance(_from, to);
            if (d < 0.3f || (!Kit.Visible(_from) && !Kit.Visible(to)))
            {
                return;
            }
            WorldTile tile = A.current_tile;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            int n = Mathf.Clamp(Mathf.CeilToInt(d / 0.35f), 2, Fx.Count(18));
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                Vector2 at = Vector2.Lerp(_from, to, u) + Random.insideUnitCircle * 1.5f * px;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, Random.Range(0.3f, 0.45f), FxLayer.Ground, 5);
                if (p == null)
                {
                    break;
                }
                p.Delay = TCrouch * 0.9f * u;
                p.Moving(Random.insideUnitCircle * 8f * px, Random.Range(3f, 7f) * px, 4f).Sized(0.5f, 1.1f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>...and erupts under it: rocks, a ring, and the enemy is thrown into the sky to be caught.</summary>
        private void Erupt(float px)
        {
            WorldTile tile = Combat.TileAt(_vg) ?? A.current_tile;
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            // the ground under it bursts (a scorch: the crash will crater this very spot a second later)
            Ravage(_vg, 1f, 1, false, "eruption");
            if (Kit.Visible(_vg))
            {
                Fx.Ring(_vg, px, 0.24f, 2f, 12f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                    Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                Kit.DustRing(_vg, px, tile, 10, 45f, 0.5f, _k);
                Kit.Debris(_vg, px, tile, 6, _k * 0.7f);
                // short-lived: the enemy comes back down onto this very spot a second later, and the
                // crater's own cracks must not land on a tangle of older ones
                Kit.Crater(_vg, px, tile, false, 0.95f, true);
                Kit.SpeedLines(_p.BodyPos(), Vector2.up, px, 4, 4f, 0.16f, Fx.WithA(_pal.Core, 0.8f));
            }
            VPop(Mathf.Min(2.2f * _k, _maxH * 0.6f), 0.34f, (_fwd > 0f ? -1f : 1f) * 520f);
        }

        // --------------------------------------------------------------- leap --

        /// <summary>Where the attacker should be to strike: beside a held body, under a flier's belly, at a giant's jaw.</summary>
        private void Aim(out Vector2 ground, out float height)
        {
            float aPiv = Pose.PivotUp * A.actor_scale;
            if (_whiff)
            {
                ground = Combat.Clamp(_whiffAim - new Vector2(_fwd * 0.3f * _k, 0f));
                height = Mathf.Clamp(_whiffH - aPiv, 0f, _maxH);
                return;
            }
            if (Held)
            {
                Vector2 vb = _p.BodyPos();
                float vHalf = _p.Pose.PivotUp * _v.actor_scale * 0.55f;
                if (_kind == Kind.Skyfall)
                {
                    // side by side, at its height
                    ground = new Vector2(vb.x - _fwd * (0.22f * _k + vHalf), _vg.y);
                    height = Mathf.Max(0f, vb.y - ground.y - aPiv);
                }
                else
                {
                    // under it, fist up
                    ground = new Vector2(vb.x - _fwd * (0.12f * _k + vHalf * 0.5f), _vg.y);
                    height = Mathf.Max(0f, vb.y - ground.y - aPiv - (_p.Pose.PivotUp * _v.actor_scale + aPiv) * 0.85f);
                }
                height = Mathf.Min(height, _maxH);
                return;
            }
            // not held: a flier's belly (under the giant) or a grounded enemy's chin (in front of it:
            // at 0.35 tiles the two sprites merged into one in game)
            float side = _box.Half < 0.5f * _k ? _box.Half * 0.7f + 0.28f * _k : _box.Half * 0.45f + 0.24f * _k;
            ground = new Vector2(_tg.x - _fwd * side, _tg.y);
            float strike = _box.Bottom > 0.3f
                ? _box.Bottom + 0.15f * (_box.Center - _box.Bottom)
                : Mathf.Min(_box.Center, _maxH);
            height = Mathf.Clamp(strike - aPiv * 2f, _box.Bottom > 0.3f ? 0.3f * _k : 0.2f * _k, _maxH);
        }

        private void Leap(float dt)
        {
            float px = Px;
            Aim(out Vector2 g, out float h);
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _tPhase = LeapTime(_aFrom, g, h);
                // a target low down (a body barely off the ground, a forced order on one standing
                // there): a bounding leap over the gap, not a glide along the grass
                float run = Vector2.Distance(_aFrom, g);
                bool lift = (_popUp || _carry) && Held;
                _hop = !lift && h - _hFrom < 0.8f * _k ? Mathf.Clamp(0.16f * run, 0.25f * _k, 0.9f * _k) : 0f;
                float rise = !lift ? h - _hFrom : _popUp ? Mathf.Min(2.2f * _k, _maxH * 0.6f) : _carryTo - _hFrom;
                _leapDir = new Vector2(g.x - _aFrom.x, g.y - _aFrom.y + rise + _hop * 0.5f);
                if (_leapDir.sqrMagnitude < 0.0004f)
                {
                    _leapDir = Vector2.up;
                }
                _leapDir.Normalize();
                Takeoff(_leapDir);
                if (_popUp && Held)
                {
                    Erupt(px);
                    _tPhase = Mathf.Max(_tPhase, 0.36f);
                }
                else if (_carry && Held)
                {
                    // the launch carries it on up, tumbling, and the attacker goes up after it
                    _tPhase = Mathf.Max(_tPhase, 0.34f);
                    float up = _carryTo - _p.Pose.Height;
                    if (up > 0.05f)
                    {
                        VPop(up, _tPhase * 0.85f, (_fwd > 0f ? -1f : 1f) * Random.Range(300f, 480f));
                        if (Visible)
                        {
                            Kit.SpeedLines(_p.BodyPos(), Vector2.up, px, 3, 4f, 0.16f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.7f));
                        }
                    }
                }
            }
            float k = Mathf.Clamp01(_pt / _tPhase);
            float kg = Anim.OutQuad(k);
            Pose.Ground = Vector2.Lerp(_aFrom, g, kg);
            Pose.Height = Mathf.Lerp(_hFrom, h, Anim.OutCubic(k)) + _hop * Mathf.Sin(Mathf.PI * kg);
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
            Pose.SX = Mathf.Lerp(0.7f, 1f, s);
            Pose.SY = Mathf.Lerp(1.5f, 1f, s);
            Pose.Rot = Mathf.Lerp(-6f, _kind == Kind.Uppercut ? 4f : 12f, Anim.OutQuad(k)) + (_hop > 0f ? 14f * Mathf.Sin(Mathf.PI * kg) : 0f);
            if (Visible)
            {
                Trails(dt, 0.03f, 0.16f);
                _lines -= dt;
                if (_lines <= 0f && k < 0.8f)
                {
                    _lines = 0.035f;
                    // streaming back along the way it came (straight down under a vertical leap)
                    Kit.SpeedLines(BodyPos(), -_leapDir, px, 2, 5f, 0.14f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.7f));
                }
            }
            if (k < 1f)
            {
                return;
            }
            if (_kind == Kind.Skyfall)
            {
                Next(Phase.Grab);
            }
            else
            {
                Next(Phase.Upper1);
            }
        }

        // --------------------------------------------------------------- grab --

        private void Grab(float dt)
        {
            float px = Px;
            if (Enter())
            {
                if (_whiff || !Held)
                {
                    _whiff = true;
                    // it slipped away: the hands close on empty air
                    if (Visible)
                    {
                        Swish(Art.Arc(9, 0.8f, 160f, -20f, 2), 0.14f, new Vector2(_fwd * 2f, 1f));
                    }
                    Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                    Next(Phase.Fall);
                    return;
                }
                _vs = VState.Held;
                _p.Pose.FaceRight = !Pose.FaceRight;   // turned to face its captor, hidden in the flash
                Vector2 mid = Vector2.Lerp(BodyPos(), _p.BodyPos(), 0.5f);
                Strike(0.6f, false, 1, mid, new Vector2(_fwd, 0.3f), 5);
                _p.Flash(0.1f, VFlash());
                FlashSelf(0.1f);
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                if (Visible)
                {
                    Fx.FocusLines(mid, px, Fx.Count(10), 13f * _k, 24f * _k, 0.12f);
                    if (Level >= 2)
                    {
                        Fx.Ring(mid, px, 0.14f, 3f, 10f * _k, 1, 1, FxLayer.Top, 3, 1f, 2.5f, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    }
                }
                _hold = 0.08f;   // the held frame
            }
            // the grip: face to face, the enemy turns to the attacker
            Pose.SX = 1.08f;
            Pose.SY = 0.94f;
            if (_pt >= TGrab)
            {
                Next(Phase.Spin);
            }
        }

        // --------------------------------------------------------------- spin --

        private Vector2 SpinCenter(float k)
        {
            return _c0 + new Vector2(0f, _lift * Anim.OutQuad(Mathf.Clamp01(k)));
        }

        /// <summary>The pair phases need the enemy in hand; without it the attacker just comes down.</summary>
        private bool Lost()
        {
            if (Held)
            {
                return false;
            }
            Next(Pose.Height > 0.05f ? Phase.Fall : Phase.Land);
            return true;
        }

        private void Spin(float dt)
        {
            float px = Px;
            if (Lost())
            {
                return;
            }
            _turn = 1f;
            if (Enter())
            {
                Vector2 a0 = BodyPos(), v0 = _p.BodyPos();
                _c0 = (a0 + v0) * 0.5f;
                // arm's length: the two bodies swing around each other, clearly two silhouettes
                // (closer, they merged into one tangled blob at this zoom)
                _r = Mathf.Clamp(Vector2.Distance(a0, v0) * 0.5f, 0.36f * _k, 0.5f * _k);
                _gy = _vg.y;
                float vPiv = _p.Pose.PivotUp * _v.actor_scale;
                float aPiv = Pose.PivotUp * A.actor_scale;
                // the lower body never scrapes the ground, and the pair rises a little
                float need = _gy + Mathf.Max(vPiv, aPiv) + _r + 0.3f * _k;
                _lift = Mathf.Max(0.3f * _k, need - _c0.y);
                _phiA0 = Mathf.Atan2(a0.y - _c0.y, a0.x - _c0.x) * Mathf.Rad2Deg;
                float phiV0 = _phiA0 + 180f;
                // two full turns, plus whatever brings the enemy straight under the attacker
                _alpha = 720f + Mathf.Repeat(_fwd * (phiV0 + 90f), 360f);
                _rotA0 = Pose.Rot;
                _rotV0 = _p.Pose.Rot;
                _rotVEnd = _rotV0 - 900f + Mathf.DeltaAngle(_rotV0 - 900f, 180f);
                _p.Pose.FaceRight = !Pose.FaceRight;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    SpinSwooshes(px);
                }
            }
            float k = Mathf.Clamp01(_pt / TSpin);
            float e = Mathf.Lerp(k, Anim.InOutCubic(k), 0.6f);
            Vector2 c = SpinCenter(k);
            float phi = (_phiA0 - _fwd * _alpha * e) * Mathf.Deg2Rad;
            Vector2 off = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi)) * _r;
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.06f * tuck;
            Pose.SY = 1f - 0.12f * tuck;
            Pose.Rot = Mathf.Lerp(_rotA0, 900f, e);
            Place(ref Pose, c + off, _gy, A.actor_scale);
            Pose vp = _p.Pose;
            vp.SX = 1f - 0.05f * tuck;
            vp.SY = 1f - 0.1f * tuck;
            vp.Rot = Mathf.Lerp(_rotV0, _rotVEnd, e);
            Place(ref vp, c - off, _gy, _v.actor_scale);
            _p.Pose = vp;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    // sparse ghosts: one or two per body at a time, so the circle reads, not a smear
                    _ghost = 0.05f;
                    Fx.Afterimage(this, 0.1f, Fx.WithA(Ghost0, Ghost0.a * 0.6f), Ghost1);
                    Fx.Afterimage(_p, 0.08f, new Color(1f, 1f, 1f, 0.3f), Fx.WithA(Ghost1, 0f));
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Fling);
            }
        }

        /// <summary>The circular swoosh around the spinning pair: one per turn, glued to the pair.</summary>
        private void SpinSwooshes(float px)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt((_r + Pose.PivotUp * A.actor_scale * 1.15f) / px), 5, 22);
            float a0 = Mathf.Round(_phiA0 / 45f) * 45f;
            Sprite[] arc = Art.Arc(r, 1f, a0, a0 - _fwd * 360f, Level >= 3 ? 3 : 2);
            for (int i = 0; i < 2; i++)
            {
                Particle p = Fx.Spawn(arc, SpinCenter(0f), px, 0.21f, FxLayer.Over, 6);
                if (p == null)
                {
                    break;
                }
                p.Delay = i * 0.19f;
                if (Level >= 3)
                {
                    p.Color(Color.white, _pal.Core, Fx.WithA(_pal.Edge, 0.5f));
                }
                else
                {
                    p.Color(_pal.Core, _pal.Edge, Fx.WithA(_pal.Edge, 0.4f));
                }
                p.Step = (q, d) => q.Pos = SpinCenter(_phase == Phase.Spin ? _pt / TSpin : 1f);
            }
        }

        // -------------------------------------------------------------- fling --

        private void Fling(float dt)
        {
            float px = Px;
            if (Lost())
            {
                return;
            }
            Pose vp = _p.Pose;
            float vPiv = vp.PivotUp * _v.actor_scale;
            if (Enter())
            {
                _crater = CraterSpot();
                // it springs clear of its own crater: out of the flames of a burning one (III)
                CrashRavage(out int force, out float radius);
                float back = force >= 3 ? Mathf.Max(1f * _k, Mathf.RoundToInt(radius) + 0.7f) : 1f * _k;
                _land = SafeLanding(_crater - new Vector2(_fwd * back, 0f), _crater);
                _vFrom = vp.Ground;
                _vhFrom = vp.Height;
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                _lastTrail = _p.BodyPos();
                _trail = true;
                Anim.Sound("event:/SFX/WEAPONS/WeaponStartThrow", A.current_tile);
                if (Visible)
                {
                    // the throw cracks the air
                    Fx.Ring(_p.BodyPos(), px, 0.18f, 3f, 13f * _k, 2, 1, FxLayer.Top, 3, 0.4f, 2.5f,
                        Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.95f), Fx.WithA(_pal.Edge, 0f));
                }
                if (Level >= 3)
                {
                    KillGlow();
                    SpawnGlow();
                }
            }
            float k = Mathf.Clamp01(_pt / TFling);
            float e = k * k;
            // the enemy: head first into the ground, faster and faster
            vp.Ground = Vector2.Lerp(_vFrom, _crater, e);
            vp.Height = _vhFrom * (1f - e);
            vp.Rot = Mathf.Lerp(vp.Rot, _rotVEnd, Mathf.Clamp01(dt * 20f));
            vp.SX = 0.82f;
            vp.SY = 1.28f;
            _p.Pose = vp;
            float onTop = vPiv * 2f;
            if (Level >= 3)
            {
                // riding it down, standing on it
                Vector2 vb = _p.BodyPos();
                Pose.Rot = Mathf.Lerp(_rotFrom, 1080f, Anim.OutCubic(Mathf.Clamp01(k / 0.3f)));
                Pose.SX = 1.12f;
                Pose.SY = 0.86f;
                Place(ref Pose, vb + new Vector2(-_fwd * 0.06f * _k, vPiv + Pose.PivotUp * A.actor_scale * 0.9f), vp.Ground.y, A.actor_scale);
                if (!_hit && k >= 0.5f)
                {
                    _hit = true;
                    Strike(1f, false, 1, vb + new Vector2(0f, vPiv * 0.8f), Vector2.down, 4);
                    _p.Flash(0.05f, VFlash());
                    _hold = 0.03f;
                    Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                }
            }
            else
            {
                // a beat behind it, then diving after it
                float kd = Mathf.Clamp01((_pt - TFlingLag) / (TFling - TFlingLag));
                float ed = kd * kd;
                Vector2 top = _crater - new Vector2(_fwd * 0.08f * _k, 0f);
                float recoil = 0.12f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_pt / TFlingLag)) * (1f - kd);
                Pose.Ground = Vector2.Lerp(_aFrom, top, ed);
                Pose.Height = Mathf.Lerp(_hFrom, onTop, ed) + recoil;
                Pose.Rot = _rotFrom;
                Pose.SX = kd > 0f ? 0.82f : 1f;
                Pose.SY = kd > 0f ? 1.25f : 1f;
            }
            if (Visible)
            {
                FlingTrail(px);
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.028f;
                    Fx.Afterimage(_p, 0.13f, new Color(1f, 1f, 1f, 0.6f), Fx.WithA(Level >= 3 ? Kit.Fire2 : Ghost1, 0f));
                    if (Level >= 3 || _pt >= TFlingLag)
                    {
                        Fx.Afterimage(this, 0.12f, Ghost0, Ghost1);
                    }
                }
                _lines -= dt;
                if (_lines <= 0f)
                {
                    _lines = 0.03f;
                    Vector2 dir = (_crater + new Vector2(0f, vPiv)) - (_vFrom + new Vector2(0f, _vhFrom + vPiv));
                    Kit.SpeedLines(_p.BodyPos(), dir, px, 2, 4f, 0.1f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.75f));
                }
            }
            _lastTrail = _p.BodyPos();
            if (k >= 1f)
            {
                Next(Phase.Impact);
            }
        }

        /// <summary>Where the enemy comes down: a little ahead, on solid ground if there is any near.</summary>
        private Vector2 CraterSpot()
        {
            Vector2 want = Combat.Clamp(_vg + new Vector2(_fwd * 0.5f * _k, 0f));
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            if (Combat.FindLanding(want, 2f, out Vector2 spot))
            {
                return spot;
            }
            return want;   // over water it splashes
        }

        /// <summary>Somewhere the attacker may land: 'want', else near it, else 'second', else where it jumped from.</summary>
        private Vector2 SafeLanding(Vector2 want, Vector2 second)
        {
            want = Combat.Clamp(want);
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            if (Combat.FindLanding(want, 2.5f, out Vector2 spot))
            {
                return spot;
            }
            second = Combat.Clamp(second);
            if (Combat.SafeSpot(second))
            {
                return second;
            }
            return _from;
        }

        /// <summary>Dust (II) or fire (III) streaming off the falling body, evenly spaced so it reads as one trail.</summary>
        private void FlingTrail(float px)
        {
            if (!_trail || !Held)
            {
                return;
            }
            Vector2 now = _p.BodyPos();
            float step = 2.2f * px / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 10);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * px;
                if (Level >= 3)
                {
                    Particle core = Fx.Spawn(Art.Puffs[0], at, px, 0.09f, FxLayer.Behind, 3);
                    core?.Sized(0.7f, 0.35f).Color(_pal.Core, Fx.WithA(Kit.Fire1, 0.7f));
                    Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.15f, 0.22f), FxLayer.Behind, 2);
                    flame?.Moving(Random.insideUnitCircle * 5f * px, 6f * px, 2f).Sized(0.75f, 0.45f)
                        .Color(Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Color.Lerp(Kit.Fire2, _pal.Edge, 0.45f), 0.85f), Fx.WithA(Kit.Fire3, 0.4f), Fx.WithA(Kit.Fire3, 0f));
                }
                else if (Random.value < 0.7f)
                {
                    Particle dust = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, Random.Range(0.25f, 0.4f), FxLayer.Behind, 2);
                    dust?.Moving(Random.insideUnitCircle * 6f * px, 4f * px, 2f).Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(DustAir, 0.8f), Fx.WithA(DustAir, 0.45f), Fx.WithA(DustAir, 0f));
                }
            }
        }

        // ------------------------------------------------------------- impact --

        private void Impact(float dt)
        {
            if (!_entered && Lost())
            {
                return;
            }
            if (Enter())
            {
                Pose vp = _p.Pose;
                float vPiv = vp.PivotUp * _v.actor_scale;
                vp.Ground = _crater;
                vp.Height = 0f;
                vp.SX = 1.25f;
                vp.SY = 0.72f;
                vp.Rot = _rotVEnd;
                _p.Pose = vp;
                Pose.Ground = _crater - new Vector2(_fwd * 0.08f * _k, 0f);
                Pose.Height = vPiv * 1.7f;
                Pose.SX = 1.2f;
                Pose.SY = 0.8f;
                _trail = false;
                Crash();
                _vs = VState.Bounce;
                _vt = 0f;
            }
            if (_pt >= TImpact)
            {
                Next(Phase.Backflip);
            }
        }

        /// <summary>The enemy hits the ground: the last (and only lethal) blow, crater, rings, and (III) the blast.</summary>
        private void Crash()
        {
            float px = Px;
            Vector2 at = _crater;
            WorldTile tile = Combat.TileAt(at) ?? A.current_tile;
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            if (Level >= 3)
            {
                Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
            }
            _finisher = true;
            Strike(Level >= 3 ? 6f : 3.6f, true, -1, at + new Vector2(0f, 2f * px), Vector2.up, 0);
            // the earth gives way under it: a real crater (III: burning), on this very frame
            CrashRavage(out int force, out float radius);
            // (the spot the attacker springs back to is left unlit: its landing is not lost in the flames)
            Kit.Ravage(at, radius, force, A, true, _land != Vector2.zero ? _land : (Vector2?)null, KeepR);
            if (Dev.On)
            {
                Dev.Trace("wingbreaker " + Id + " ravage crash force " + force + " r " + radius.ToString("F2"));
            }
            bool black = _crit && Level >= 2;
            if (Kit.Visible(at))
            {
                // the flash BEHIND the pair, the two flat shapes against it (Framed): drawn over them,
                // it hid the crash, the key pose, for the whole freeze (as Meteor Slam's and Rising
                // Tempest's did, r2_meteor_slam_2 0035-0037)
                if (Level >= 3 || black)
                {
                    Kit.ImpactFrame(at + new Vector2(0f, 3f * px), px * _k, Level >= 3 ? 1f : 0.7f, _pal, black, FxLayer.Behind);
                }
                else
                {
                    // II: a white flash and a starburst (a flat disk of the tier's pale core read as a
                    // pink balloon in game); the tier colour lives in the rim and the rings
                    Particle flash = Fx.Spawn(Art.Disk(11, 0.85f, 0.35f), at + new Vector2(0f, 3f * px), px * _k, 0.08f, FxLayer.Behind, 8);
                    flash?.Sized(0.8f, 1.2f).Color(Color.white, Fx.WithA(Color.Lerp(Color.white, _pal.Edge, 0.35f), 0.8f), Fx.WithA(_pal.Edge, 0f));
                    Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at + new Vector2(0f, 4f * px), px * _k * 0.75f, 0.07f, FxLayer.Behind, 9);
                    if (burst != null)
                    {
                        burst.Rot = Random.Range(0f, 360f);
                        burst.Sized(1f, 1.2f).Color(Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    }
                }
                Framed(black);
                if (Level >= 3)
                {
                    // (a God's crash rings out as wide as its blast: half as far again)
                    float g = _god ? 1.4f : 1f;
                    Kit.Crater(at, px * _k, tile, true, 6f, false, _pal.Edge);
                    Fx.Ring(at, px, 0.45f * Mathf.Sqrt(g), 6f, 26f * _k * g, 3, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                        Color.white, Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
                    Particle ring2 = Fx.Ring(at, px, 0.4f, 8f, 17f * _k * g, 1, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                        Fx.WithA(_pal.Core, 0.7f), Fx.WithA(_pal.Edge, 0f));
                    if (ring2 != null)
                    {
                        ring2.Delay = 0.07f;
                    }
                    Kit.DustRing(at, px, tile, 22, 95f * g, 0.85f, _k);
                    Kit.Debris(at, px, tile, 14, _k);
                    Kit.Sparks(at + new Vector2(0f, 2f * px), px, 10, 90f * _k, 160f * _k, _pal.Edge);
                    int embers = Fx.Count(10);
                    for (int i = 0; i < embers; i++)
                    {
                        Kit.Ember(at + Random.insideUnitCircle * 6f * px, px, Random.Range(0.8f, 1.5f),
                            new Vector2(Random.Range(-8f, 8f), Random.Range(15f, 35f)) * px, _pal.Edge);
                    }
                    Kit.Shake(at, 0.32f, 0.012f, _k);
                }
                else
                {
                    Kit.Crater(at, px * _k, tile, false, 4f, true);
                    Fx.Ring(at, px, 0.35f, 4f, 15f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                        Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                    Kit.DustRing(at, px, tile, 14, 70f, 0.7f, _k);
                    Kit.Debris(at, px, tile, 7, _k * 0.8f);
                    Kit.Shake(at, 0.2f, 0.008f, _k);
                }
                Fx.FocusLines(at + new Vector2(0f, 3f * px), px, Fx.Count(8), 14f * _k, 26f * _k, 0.12f);
            }
            if (Level >= 3)
            {
                Kit.Stop(at, 0.1f);
                _hold = 0.04f;
                Blast(at);
            }
            else
            {
                _hold = 0.06f;
                // the ring rocks anyone standing too close
                foreach (Actor u in Combat.FoesAround(A, at, 1.2f * _k))
                {
                    if (u != _v)
                    {
                        u.startShake(0.2f, 0.1f, true, false);
                    }
                }
            }
        }

        /// <summary>III: everyone hostile within 2 tiles of the crater (a God's: 3) is hit and thrown.</summary>
        private void Blast(Vector2 at)
        {
            float radius = (_god ? 3f : 2f) * _k;
            foreach (Actor u in Combat.FoesAround(A, at, radius))
            {
                // (asked first, the too-heavy too: anyone may answer an area blow, an evade or a deflection)
                if (u == _v || Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    // too heavy for this tier: a glancing blow, no throw
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                float near = 1f - Mathf.Clamp01(Vector2.Distance(u.current_position, at) / radius);
                Combat.Hit(A, u, Mathf.Lerp(2f, 2.6f, near) * cm, true, true, 0f, this);
                Combat.Launch(u, at, Mathf.Lerp(1.4f, 2.6f, near) * _k * Fly(u), Mathf.Lerp(0.9f, 1.8f, near) * _k,
                    (Random.value < 0.5f ? -1f : 1f) * Random.Range(500f, 800f));
            }
        }

        // ----------------------------------------------------------- backflip --

        private void Backflip(float dt)
        {
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                // II springs off upside down (one and a half flips back), III off its feet (one flip back)
                _rotTo = Level >= 3 ? _rotFrom - 360f : _rotFrom - 540f;
                _rotTo = Mathf.Round(_rotTo / 360f) * 360f;
                if (_land == Vector2.zero)
                {
                    _land = SafeLanding(_crater - new Vector2(_fwd * _k, 0f), _crater);
                }
                else if (!Combat.SafeSpot(_land))
                {
                    // the crash has just torn up (or set fire to) the spot it meant to land on
                    _land = SafeLanding(_land, _from);
                }
                // masters spring off and are down at once (the tail of the move, not its beat)
                _tPhase = TBackflip * Mathf.Lerp(1f, _crisp, 0.6f);
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / _tPhase);
            float d = Vector2.Distance(_aFrom, _land);
            Pose.Ground = Vector2.Lerp(_aFrom, _land, Anim.OutQuad(k));
            Pose.Height = Mathf.Max(0f, Mathf.Lerp(_hFrom, 0f, k) + (0.6f * _k + 0.08f * d) * Mathf.Sin(Mathf.PI * k));
            Pose.Rot = Mathf.Lerp(_rotFrom, _rotTo, Anim.OutCubic(k));
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.07f * tuck;
            Pose.SY = 1f - 0.16f * tuck;
            if (Visible && k < 0.7f)
            {
                Trails(dt, 0.035f, 0.12f);
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Land);
            }
        }

        // ---------------------------------------------------------- uppercut --

        private Vector2 StrikePoint()
        {
            if (Held)
            {
                return _p.BodyPos() + new Vector2(-_fwd * 1f * Px, -1.5f * Px);
            }
            if (_vGone)
            {
                return HeadPos();
            }
            float h = _box.Bottom > 0.3f ? _box.Bottom + 0.15f * (_box.Center - _box.Bottom) : Mathf.Min(_box.Center, Pose.Height + Pose.PivotUp * 2.4f * A.actor_scale);
            return new Vector2(_tg.x - _fwd * _box.Half * 0.3f, _tg.y + h);
        }

        private void Upper1(float dt)
        {
            float px = Px;
            Aim(out Vector2 g, out float h);
            if (Enter())
            {
                _hFrom = Pose.Height;
                if (Visible)
                {
                    Swish(Art.Arc(10, 1f, -70f, 115f, 3), 0.15f, new Vector2(_fwd * 1f, 0f));
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / TUpper1);
            Pose.Ground = Vector2.Lerp(Pose.Ground, g, Mathf.Clamp01(dt * 14f));
            Pose.Height = _hFrom + 0.2f * _k * Anim.OutQuad(k);
            Pose.Rot = Mathf.Lerp(4f, -24f, Anim.OutCubic(Mathf.Clamp01(k * 1.6f)));
            Pose.SX = Mathf.Lerp(0.86f, 1f, k);
            Pose.SY = Mathf.Lerp(1.22f, 1f, k);
            if (!_hit && k >= 0.4f)
            {
                _hit = true;
                if (Strike(0.8f, false, 1, StrikePoint(), new Vector2(_fwd * 0.3f, 1f), 5))
                {
                    if (Held)
                    {
                        VPop(0.3f * _k, 0.12f, -420f * _fwd);
                    }
                    else if (_v != null && !_vGone)
                    {
                        _v.startShake(0.15f, 0.1f, true, true);
                    }
                    Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                }
                else if (_parried)
                {
                    Next(Phase.Fall);
                    return;
                }
            }
            if (Visible)
            {
                Trails(dt, 0.04f, 0.1f);
            }
            if (k >= 1f)
            {
                Next(Phase.Upper2);
            }
        }

        private void Upper2(float dt)
        {
            if (_spike)
            {
                Spike(dt);
                return;
            }
            float px = Px;
            Aim(out Vector2 g, out float h);
            if (Enter())
            {
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                if (Visible)
                {
                    Swish(Art.Arc(11, 1f, -90f, 250f, 3), 0.18f, Vector2.zero);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            _turn = -1f;
            float k = Mathf.Clamp01(_pt / TUpper2);
            Pose.Ground = Vector2.Lerp(Pose.Ground, g, Mathf.Clamp01(dt * 10f));
            Pose.Height = _hFrom + 0.3f * _k * Anim.OutQuad(k);
            // the flip kick: a whole turn backwards, the foot sweeping up through the enemy
            Pose.Rot = _rotFrom - 360f * Anim.OutCubic(k);
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.08f * tuck;
            Pose.SY = 1f - 0.16f * tuck;
            if (!_hit && k >= 0.35f)
            {
                _hit = true;
                _finisher = true;
                Vector2 at = StrikePoint();
                if (Held && !_whiff)
                {
                    // the kick lands as it lets go: the blow itself is dealt once the body is free
                    if (Kit.Visible(at))
                    {
                        Fx.HitSpark(at, px, 2, new Vector2(_fwd * 0.4f, 1f), Fx.Count(7));
                        Fx.FocusLines(at, px, Fx.Count(8), 12f * _k, 22f * _k, 0.1f);
                    }
                    _p.Flash(0.08f, VFlash());
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                    _pendMult = 1.2f;
                    Knock();
                }
                else if (Strike(1.2f, true, 2, at, new Vector2(_fwd * 0.4f, 1f), 7))
                {
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                    Knock();
                    if (Visible)
                    {
                        Fx.FocusLines(at, px, Fx.Count(8), 12f * _k, 22f * _k, 0.1f);
                    }
                }
            }
            if (Visible)
            {
                Trails(dt, 0.03f, 0.11f);
            }
            if (k >= 1f)
            {
                Next(Phase.Fall);
            }
        }

        /// <summary>The flip kick's knock: a held body is let go and thrown, a grounded one launched, a heavy one rocked.</summary>
        private void Knock()
        {
            if (_vGone || !_v.isAlive())
            {
                return;
            }
            Vector2 dir = new Vector2(_fwd, Random.Range(-0.25f, 0.25f)).normalized;
            if (Held)
            {
                // let go in mid-air; from here on it is an ordinary unit we track by id (no longer ours to pose)
                _p.Release(false);
                _p = null;
                _vShadow?.Kill();
                _vShadow = null;
                _pend = true;
                _pendDir = dir;
                _pendDist = 1.1f * _k * Fly(_v);
                _pendApex = 0.9f * _k;
                // still dazed when it lands: the move's end hands it on as Down (Finish)
                _pendStagger = 1f;
                if (_v.isAlive())
                {
                    EndLink = Link.Launched;
                    EndTarget = _v;
                }
                return;
            }
            if (Weights.Heavy(_v))
            {
                Combat.Stagger(_v, 0.6f);
                if (!_v.isFlying())
                {
                    EndLink = Link.Down;
                    EndTarget = _v;
                }
                return;
            }
            Combat.Stagger(_v, 0.9f);
            Combat.LaunchDir(_v, dir, 1.0f * _k * Fly(_v), 1.3f * _k, (_fwd > 0f ? -1f : 1f) * 600f);
            EndLink = Link.Launched;
            EndTarget = _v;
        }

        // ------------------------------------------------------------- spike --

        /// <summary>Where a spiked body comes down: right under it, on solid ground.</summary>
        private Vector2 SlamSpot()
        {
            Vector2 want = Combat.Clamp(_vg + new Vector2(_fwd * 0.15f * _k, 0f));
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            if (Combat.SafeSpot(_vg))
            {
                return _vg;
            }
            return Combat.FindLanding(_vg, 1.5f, out Vector2 spot) ? spot : _vg;
        }

        /// <summary>
        /// I, the other finish: a forward flip up and over the enemy and a hammer heel that drives it
        /// back into the ground (a held body is slammed down and bounces once; one on its feet is
        /// stamped into the earth). It lies stunned: Down, for an ally to finish.
        /// </summary>
        private void Spike(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                _aFrom = Pose.Ground;
                if (Visible)
                {
                    // the forward flip: the foot sweeps from behind, up and over the top
                    Swish(Art.Arc(11, 1f, 220f, 50f, 3), 0.14f, new Vector2(_fwd * 0.5f, 1f));
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            _turn = 1f;
            float k = Mathf.Clamp01(_pt / TSpike);
            // over it: the feet just above a held body's head, or on the crown of one on its feet
            Vector2 over;
            float top;
            if (_hit && _spikeSet)
            {
                // after the blow it springs off the spot where the heel came down (it doesn't ride
                // the spiked body down, nor drop if the enemy fell to it)
                over = _spikeOver;
                top = _spikeTop;
            }
            else if (Held)
            {
                Vector2 vb = _p.BodyPos();
                float vPiv = _p.Pose.PivotUp * _v.actor_scale;
                over = new Vector2(vb.x + _fwd * 0.04f * _k, _vg.y);
                top = Mathf.Clamp(vb.y + vPiv * 0.9f - _vg.y, 0f, _maxH);
            }
            else if (_whiff || _vGone)
            {
                // nothing there any more: the heel comes down on the air where it was
                over = _whiff ? _whiffAim : _aFrom;
                top = Mathf.Clamp(_whiff ? _whiffH : _hFrom, 0f, _maxH);
            }
            else
            {
                over = SkullAim(-0.02f);
                top = Mathf.Clamp(_box.Crown + 0.04f * _k, 0f, _maxH);
            }
            _spikeOver = over;
            _spikeTop = top;
            _spikeSet = true;
            float k1 = Mathf.Clamp01(k / 0.5f);
            float up = Anim.OutCubic(k1);
            Pose.Ground = Vector2.Lerp(_aFrom, over, up);
            if (k < 0.5f)
            {
                // up and over in a forward flip, the heel coming down from above onto it
                Pose.Height = Mathf.Lerp(_hFrom, top, up) + 0.25f * _k * Mathf.Sin(Mathf.PI * k1);
                Pose.Rot = Mathf.Lerp(_rotFrom, 360f, Anim.InOutCubic(k1));
                float tuck = Mathf.Sin(Mathf.PI * k1);
                Pose.SX = 1f - 0.08f * tuck;
                Pose.SY = 1f - 0.18f * tuck;
            }
            else
            {
                // bounced off the blow, squashed onto it for a beat
                float after = Mathf.Clamp01((k - 0.5f) / 0.5f);
                Pose.Height = top + 0.14f * _k * Mathf.Sin(Mathf.PI * after);
                Pose.Rot = 360f;
                float squash = 1f - Mathf.Clamp01(after / 0.6f);
                Pose.SX = 1f + 0.14f * squash;
                Pose.SY = 1f - 0.14f * squash;
            }
            if (!_hit && k >= 0.5f)
            {
                _hit = true;
                _finisher = true;
                SpikeHit(px);
            }
            if (Visible)
            {
                Trails(dt, 0.03f, 0.11f);
            }
            if (k >= 1f)
            {
                Next(Phase.Fall);
            }
        }

        /// <summary>The hammer heel lands: a held body is driven down (its damage lands with it), one on its feet is stamped.</summary>
        private void SpikeHit(float px)
        {
            if (_vGone || _v == null || !_v.isAlive())
            {
                return;
            }
            if (Visible)
            {
                Swish(Art.Arc(11, 1f, 130f, -65f, 3), 0.15f, new Vector2(_fwd * 1.5f, -1f));
            }
            if (Held)
            {
                Vector2 hit = _p.BodyPos() + new Vector2(0f, _p.Pose.PivotUp * _v.actor_scale * 0.5f);
                if (Kit.Visible(hit))
                {
                    Fx.HitSpark(hit, px, 1, Vector2.down, Fx.Count(5));
                    Fx.FocusLines(hit, px, Fx.Count(8), 12f * _k, 22f * _k, 0.1f);
                }
                _p.Flash(0.06f, VFlash());
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                _crater = SlamSpot();
                _slamH = _p.Pose.Height;
                _slamRot = _p.Pose.Rot;
                _vs = VState.Slam;
                _vt = 0f;
                return;
            }
            Vector2 at = new Vector2(Pose.Ground.x + _fwd * 1f * px, _tg.y + Mathf.Min(_box.Top, Pose.Height + 0.05f));
            if (!Strike(1.2f, true, 2, at, new Vector2(_fwd * 0.3f, -1f), 7))
            {
                return;
            }
            Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            if (Kit.Visible(at))
            {
                Fx.FocusLines(at, px, Fx.Count(8), 12f * _k, 22f * _k, 0.1f);
            }
            if (_vGone || !_v.isAlive())
            {
                return;
            }
            // stamped into the earth where it stands (the ground under it is scorched)
            WorldTile tile = Combat.TileAt(_tg) ?? A.current_tile;
            Ravage(_tg, 1f, 1, false, "stamp");
            if (Kit.Visible(_tg))
            {
                Kit.DustRing(_tg, px, tile, 9, 38f, 0.45f, _k);
                Fx.LandingPuff(_tg, px, tile);
            }
            Combat.Stagger(_v, Weights.Heavy(_v) ? 0.6f : 1.1f);
            if (!_v.isFlying())
            {
                EndLink = Link.Down;
                EndTarget = _v;
            }
        }

        /// <summary>The spiked body hits the ground: the blow's damage, dust, and it bounces once.</summary>
        private void SpikeLand(float lying)
        {
            float px = Px;
            _p.Pose.Ground = _crater;
            _p.Pose.Height = 0f;
            _vs = VState.Bounce;
            _vt = 0f;
            _bounceRot = lying;
            WorldTile tile = Combat.TileAt(_crater) ?? A.current_tile;
            Vector2 at = _crater + new Vector2(0f, 2f * px);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Strike(1.2f, true, 1, at, Vector2.up, 5);
            // a heavy landing: the ground it is driven into is scorched (I's only mark on the world)
            Ravage(_crater, 1f, 1, false, "spike");
            if (Kit.Visible(_crater))
            {
                Kit.DustRing(_crater, px, tile, 10, 42f, 0.45f, _k);
                Fx.LandingPuff(_crater, px, tile);
                Fx.FocusLines(at, px, Fx.Count(6), 10f * _k, 18f * _k, 0.09f);
            }
        }

        // ------------------------------------------------------ skull cracker --

        private float HeelApex()
        {
            return Mathf.Clamp(_box.Top + 0.65f * _k, 0.8f * _k, _maxH);
        }

        /// <summary>
        /// Where the heel lands: the crown of the head (Box.Crown). The heel goes INTO it: in game the
        /// first version stopped above the top and every blow landed in thin air, and the second aimed
        /// at a flying dragon's highest pixels (its raised wing tips) and chopped the air over its back.
        /// </summary>
        private float HeadHeight()
        {
            return Mathf.Clamp(_box.Crown, 0f, _maxH);
        }

        /// <summary>The ground point under the skull (a dragon's head is well ahead of its middle).</summary>
        private Vector2 SkullAim(float past)
        {
            return new Vector2(_tg.x + _headX + _fwd * past * _k, _tg.y);
        }

        private void Over(float dt)
        {
            float px = Px;
            Vector2 aim = SkullAim(0.12f);
            float apex = HeelApex();
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                // (higher tiers vault faster: less hang at the top before the chop)
                _tPhase = Mathf.Clamp(0.26f + 0.02f * Vector2.Distance(_aFrom, aim) + 0.018f * apex / _k, 0.32f, 0.56f) * Mathf.Lerp(1f, _crisp, 0.3f);
                Takeoff(Vector2.up);
            }
            float k = Mathf.Clamp01(_pt / _tPhase);
            Pose.Ground = Vector2.Lerp(_aFrom, aim, Anim.InOutSine(k));
            // a vault never sinks: if the target comes down under it (a dragon landing), it keeps the
            // height it has reached and the heel chops down from there (below)
            Pose.Height = Mathf.Max(Mathf.Lerp(_hFrom, apex, Anim.OutCubic(k)), Pose.Height);
            // up, a somersault over the head, and the heel raised high at the top
            float fk = Mathf.Clamp01((k - 0.12f) / 0.68f);
            Pose.Rot = 360f * Anim.InOutCubic(fk) - 38f * Anim.Smooth(0.8f, 1f, k);
            float tuck = Mathf.Sin(Mathf.PI * fk);
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.3f));
            Pose.SX = Mathf.Lerp(0.72f, 1f, s) - 0.08f * tuck;
            Pose.SY = Mathf.Lerp(1.45f, 1f, s) - 0.18f * tuck;
            if (Visible)
            {
                Trails(dt, fk > 0f && fk < 1f ? 0.03f : 0.045f, 0.14f);
                _lines -= dt;
                if (_lines <= 0f && k < 0.55f)
                {
                    _lines = 0.035f;
                    Kit.SpeedLines(BodyPos(), Vector2.down, px, 2, 5f, 0.14f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.7f));
                }
            }
            // the somersault done, and the target has come down under it (a dragon landing in the
            // middle of the leap): the heel comes down now, from up there, instead of the attacker
            // sinking through the air to the new apex first
            if (k >= 1f || k >= 0.85f && Pose.Height > apex + 0.4f * _k)
            {
                Next(Phase.Drop);
            }
        }

        private void Drop(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                // contact leaning a little past upright, whatever turn the somersault ended on
                _rotTo = Mathf.Round(_rotFrom / 360f) * 360f + 24f;
                // a longer chop from higher up (the heel still lands in under a quarter second)
                _tPhase = Mathf.Clamp(TDrop + 0.01f * Mathf.Max(0f, _hFrom - HeadHeight()) / _k, TDrop, 0.22f);
                if (Visible)
                {
                    Swish(Art.Arc(11, 1f, 130f, -65f, 3), 0.16f, new Vector2(_fwd * 1.5f, -1f));
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / _tPhase);
            Vector2 aim = SkullAim(0.08f);
            Pose.Ground = Vector2.Lerp(Pose.Ground, aim, Mathf.Clamp01(dt * 14f));
            // an axe kick: the raised heel (leaning back) chops down until the body stands upright on
            // the skull. The heel leads; the feet are what touch the head.
            Pose.Rot = Mathf.Lerp(_rotFrom, _rotTo, Anim.OutCubic(Mathf.Clamp01(k / 0.8f)));
            float head = Mathf.Min(HeadHeight(), _hFrom);
            Pose.Height = Mathf.Lerp(_hFrom, head, Anim.InQuad(Mathf.Clamp01(k / 0.8f)));
            float stretch = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.8f));
            Pose.SX = 1f - 0.1f * stretch;
            Pose.SY = 1f + 0.16f * stretch;
            if (!_hit && k >= 0.8f)
            {
                // contact: squashed onto the head for the hit-stop
                Pose.SX = 1.14f;
                Pose.SY = 0.86f;
                _hit = true;
                _drops++;
                Heel();
                if (_parried)
                {
                    Next(Phase.Fall);
                    return;
                }
            }
            if (k >= 1f)
            {
                Next(Level >= 3 && _drops == 1 && !_vGone && !_whiff ? Phase.Rebound : Phase.Fall);
            }
        }

        /// <summary>A heel lands on the head: II one blow and a stagger; III two, the second on an impact frame.</summary>
        private void Heel()
        {
            float px = Px;
            Vector2 at = new Vector2(Pose.Ground.x + _fwd * 1f * px, _tg.y + Mathf.Min(_box.Top, Pose.Height + 0.05f));
            bool last = Level < 3 || _drops >= 2;
            if (last)
            {
                _finisher = true;
            }
            // a dragon in flight that this heel brings down (or kills) ploughs a trench where it falls
            bool dragonAir = last && Level >= 3 && !_vGone && _v != null && _v.isAlive() && _v.isFlying() && _v.getActorComponent<Dragon>() != null;
            float mult = Level < 3 ? 4.2f : _drops == 1 ? 2.7f : 4.9f;
            if (!Strike(mult, last, 2, at, new Vector2(_fwd * 0.4f, -1f), last ? 9 : 6))
            {
                if (_whiff && Visible)
                {
                    Fx.FocusLines(at, px, Fx.Count(4), 10f * _k, 18f * _k, 0.08f);
                }
                return;
            }
            Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            // (what it is now, not the glide the aim follows: a dragon that has just landed is on the ground)
            bool air = _raw.Bottom > 0.3f;
            bool vis = Kit.Visible(at);
            WorldTile tile = Combat.TileAt(_tg) ?? A.current_tile;
            if (!last)
            {
                // III, first heel: rock it in place for the second
                _hold = 0.05f;
                Combat.Stagger(_v, 0.5f);
                if (vis)
                {
                    // (against a giant the shock is drawn to its scale, not to the attacker's)
                    Fx.FocusLines(at, px, Fx.Count(_big > 1.2f ? 14 : 10), 14f * _k * _big, 26f * _k * _big, 0.12f);
                    Fx.Ring(at, px, 0.2f, 3f, 12f * _k * _big, 2, 1, FxLayer.Top, 3, 0.45f, 2.5f, Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0f));
                    Kit.Shake(at, 0.15f, 0.006f, _k);
                }
                return;
            }
            bool black = _crit && Level >= 2;
            if (vis)
            {
                Fx.FocusLines(at, px, Fx.Count(_big > 1.2f ? 20 : 14), 16f * _k * _big, 30f * _k * _big, 0.14f);
                if (Level >= 3 || black)
                {
                    // (behind: the heel on the skull is the key pose, a silhouette against it)
                    Kit.ImpactFrame(at, px * _k, (Level >= 3 ? 1f : 0.7f) * Mathf.Sqrt(_big), _pal, black, FxLayer.Behind);
                    Framed(black);
                }
                // the shock rings out around the head (in the air, a flattened ring; on the ground, through the body into the earth)
                Fx.Ring(at, px, 0.3f, 3f, (Level >= 3 ? 26f : 15f) * _k * _big * (_god ? 1.3f : 1f), Level >= 3 ? 3 : 2, 1, FxLayer.Top, 3, 0.45f, 2.6f,
                    Fx.WithA(Level >= 3 ? Color.white : _pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                if (!air)
                {
                    Vector2 feet = _tg;
                    if (Level >= 3)
                    {
                        Kit.Crater(feet, px * _k, tile, true, 6f, false, _pal.Edge);
                        Fx.Ring(feet, px, 0.45f, 6f, 26f * _k, 3, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                            Color.white, Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
                        Kit.DustRing(feet, px, tile, 20, 90f, 0.8f, _k);
                        Kit.Debris(feet, px, tile, 12, _k);
                    }
                    else
                    {
                        Kit.Crater(feet, px * _k, tile, false, 3.5f, true);
                        Kit.DustRing(feet, px, tile, 12, 60f, 0.6f, _k);
                    }
                }
                if (Level >= 3)
                {
                    Kit.Sparks(at, px, _big > 1.2f ? 14 : 10, 90f * _k * _big, 160f * _k * _big, _pal.Edge);
                    int embers = Fx.Count(8);
                    for (int i = 0; i < embers; i++)
                    {
                        Kit.Ember(at + Random.insideUnitCircle * 5f * px, px, Random.Range(0.6f, 1.2f), new Vector2(Random.Range(-10f, 10f), Random.Range(-6f, 20f)) * px, _pal.Edge);
                    }
                }
                Kit.Shake(at, Level >= 3 ? 0.3f : 0.2f, Level >= 3 ? 0.011f : 0.008f, _k);
                if (Level >= 3 && _titan && Cinema.FullAt(at) && !Cinema.Playing)
                {
                    Cinema.Title(Locale.Get("sakuga_wingbreaker_skull_cracker"), _pal.Edge, 1.4f, at, 0.8f);
                }
            }
            if (!air)
            {
                // the blow goes through the body into the earth: a real crater under its feet, a
                // little smaller than its shock (II blasts the soft ground, III sets it alight)
                // (a giant's is as wide as the giant: a God's heel on a landed dragon, 4 tiles)
                // and the attacker comes down past its rim, out of its flames
                float big = Mathf.Min(_big, 1.9f);
                Ravage(_tg, (Level >= 3 ? 1.4f : 1f) * big, Level >= 3 ? 3 : 2, true, "heel", true);
            }
            // a dragon knocked out of the sky ploughs a long trench where it comes down: when it
            // touches down (WingbreakerGround), or at once if this heel killed it in the air
            int gouge = 0;
            float gougeW = 0f;
            if (dragonAir)
            {
                gouge = _god ? 4 : 3;
                gougeW = _god ? 2.6f : 1.6f;
                if (!_v.isAlive())
                {
                    WingbreakerGround.Gouge(_v, _v.current_position, _v.current_position, _v.flip ? 1f : -1f, gouge, gougeW, A);
                }
            }
            if (Level >= 3)
            {
                Kit.Stop(at, 0.1f);
                _hold = 0.04f;
            }
            else
            {
                _hold = 0.06f;
            }
            if (_v.isAlive())
            {
                if (Level >= 3)
                {
                    // knocked out of the sky (if the game lets it land there), or rocked where it
                    // stands (a dragon on the ground is held down by hand: it can't be stunned)
                    if (_v.isFlying() || _v.getActorComponent<Dragon>() != null)
                    {
                        WingbreakerGround.Down(_v, 3f, A, gouge, gougeW);
                    }
                    else
                    {
                        Combat.Stagger(_v, 3f);
                    }
                    EndLink = Link.Down;
                    EndTarget = _v;
                }
                else
                {
                    Combat.Stagger(_v, 1.5f);
                    if (!_v.isFlying())
                    {
                        EndLink = Link.Down;
                        EndTarget = _v;
                    }
                }
            }
        }

        private void Rebound(float dt)
        {
            if (Enter())
            {
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / TRebound);
            Vector2 aim = SkullAim(0.1f);
            Pose.Ground = Vector2.Lerp(Pose.Ground, aim, Mathf.Clamp01(dt * 12f));
            // off a giant's skull it bounces higher (in game, off a landed dragon's head, the 0.9 tile
            // hop barely cleared its horns and the second chop read as a twitch)
            float bounce = 0.9f * _k + 0.12f * Mathf.Max(0f, _box.Crown - 1f * _k);
            Pose.Height = Mathf.Min(_maxH, _hFrom + bounce * Anim.OutQuad(k));
            // bounce off the skull, a forward flip, and the heel raised again (leaning back) for the second chop
            Pose.Rot = Mathf.Lerp(_rotFrom, _rotFrom + 298f, Anim.InOutCubic(k));
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.08f * tuck;
            Pose.SY = 1f - 0.18f * tuck;
            if (Visible)
            {
                Trails(dt, 0.03f, 0.12f);
            }
            if (k >= 1f)
            {
                Next(Phase.Drop);
            }
        }

        // --------------------------------------------------------------- fall --

        /// <summary>Back down to solid ground: a little recoil off the blow, then falling faster, one last flip.</summary>
        private void Fall(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _aFrom = Pose.Ground;
                _hFrom = Pose.Height;
                _rotFrom = Pose.Rot;
                if (!_landKept || _parried || _whiff || !Combat.SafeSpot(_land))
                {
                    _land = SafeLanding(FallSpot(), Pose.Ground);
                }
                // masters drop out of the sky faster (it reads as weight, and the next move comes
                // sooner); a long spring clear of a crater takes a little longer
                float run = Vector2.Distance(_aFrom, _land) / _k;
                _tPhase = Mathf.Clamp(0.22f + 0.04f * _hFrom / _k + 0.025f * run, 0.26f, 0.7f) * Mathf.Lerp(1f, _crisp, 0.4f);
                _dropH = _parried || _whiff ? 0f : _hFrom;
                if (_parried)
                {
                    // knocked back off the blow: a tumble backwards
                    _rotTo = Mathf.Floor(_rotFrom / 360f) * 360f - 360f;
                    if (Kit.Visible(BodyPos()))
                    {
                        Fx.HitSpark(BodyPos() + new Vector2(_fwd * 3f * px, 1f * px), px, 1, new Vector2(-_fwd, 0.4f), 6);
                    }
                }
                else if (_hFrom > 1.2f * _k)
                {
                    // one more turn on the way down, the way it was already turning
                    float to = _turn >= 0f ? Mathf.Ceil(_rotFrom / 360f) * 360f : Mathf.Floor(_rotFrom / 360f) * 360f;
                    if (Mathf.Abs(to - _rotFrom) < 140f)
                    {
                        to += _turn >= 0f ? 360f : -360f;
                    }
                    _rotTo = to;
                }
                else
                {
                    _rotTo = Mathf.Round(_rotFrom / 360f) * 360f;   // a short hop: just upright again
                }
            }
            float k = Mathf.Clamp01(_pt / _tPhase);
            float up01 = Anim.OutQuad(Mathf.Clamp01(k / 0.2f));
            float down = Anim.InQuad(Mathf.Clamp01((k - 0.2f) / 0.8f));
            float recoil = Mathf.Min(0.3f * _k, Mathf.Max(0f, _maxH - _hFrom));
            Pose.Height = Mathf.Max(0f, Mathf.Lerp(Mathf.Lerp(_hFrom, _hFrom + recoil, up01), 0f, down));
            Pose.Ground = Vector2.Lerp(_aFrom, _land, Anim.InOutSine(k));
            Pose.Rot = Mathf.Lerp(_rotFrom, _rotTo, Anim.OutCubic(Mathf.Clamp01(k / 0.75f)));
            float tuck = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.75f));
            Pose.SX = 1f - 0.06f * tuck - 0.08f * down * (1f - tuck);
            Pose.SY = 1f - 0.14f * tuck + 0.18f * down * (1f - tuck);
            if (Visible && k < 0.8f)
            {
                Trails(dt, 0.04f, 0.12f);
                _lines -= dt;
                if (_lines <= 0f && down > 0.3f && _hFrom > 2f * _k)
                {
                    _lines = 0.04f;
                    Kit.SpeedLines(BodyPos(), Vector2.up, px, 2, 5f, 0.12f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.55f));
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Land);
            }
        }

        /// <summary>Where it wants to come down: past a giant it vaulted, beside an enemy it struck, or straight down.</summary>
        private Vector2 FallSpot()
        {
            if (_vGone || _whiff || _parried)
            {
                return Pose.Ground - new Vector2(_fwd * 0.3f * _k, 0f);
            }
            if (_kind == Kind.SkullCracker)
            {
                return _tg + new Vector2(_fwd * (_box.Half + 0.6f * _k), 0f);
            }
            if (_spike)
            {
                // it flipped over the enemy: it comes down on the far side
                return Pose.Ground + new Vector2(_fwd * (_box.Half * 0.5f + 0.45f * _k), 0f);
            }
            return Pose.Ground - new Vector2(_fwd * (_box.Half * 0.5f + 0.35f * _k), 0f);
        }

        // --------------------------------------------------------------- land --

        private void Land(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Height = 0f;
                WorldTile tile = Combat.TileAt(Pose.Ground) ?? A.current_tile;
                if (Level >= 2 && _dropH >= 4f * _k)
                {
                    // a long drop (II/III come down from the height of a dragon's head) scorches where it lands
                    Ravage(Pose.Ground, 1f, 1, false, "landing");
                }
                if (Kit.Visible(Pose.Ground))
                {
                    Fx.LandingPuff(Pose.Ground, px, tile);
                    if (Level >= 2 && !_parried && !_whiff)
                    {
                        Kit.Flare(Pose.Ground, px, Fx.WithA(_pal.Edge, 0.8f), 10f * _k, 0.3f);
                    }
                    Vector2 off = new Vector2(_fwd * 3f * px, Pose.PivotUp * 1.4f * px);
                    Follow(Fx.Glint(BodyPos() + off, px, 0.24f, _parried ? new Color(0.8f, 0.8f, 0.85f, 1f) : _pal.Core, 1f), off);
                }
            }
            float amp = Anim.Spring(_pt, 9f, 18f);
            Pose.SX = 1f + 0.16f * amp;
            Pose.SY = 1f - 0.18f * amp;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.HideShadow = false;
            KillShadow();
            // masters land crisply and go: the spring is the same, only shorter
            if (_pt >= TLand * _crisp)
            {
                Finish();
            }
        }

        private void Finish()
        {
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Nudge = Vector2.zero;
            if (Held)
            {
                if (_vs == VState.Bounce)
                {
                    // a crisp landing can beat the body's last bounce by a few frames: it comes to
                    // rest now, grounded and stunned, so the chain (Down) is never lost
                    Settle();
                }
                else
                {
                    // never leave it hanging: whatever happened, it is let go
                    _p.Release(_p.Pose.Height <= 0.02f);
                }
            }
            if (_parried)
            {
                Combat.Stagger(A, 0.5f);
                EndLink = Link.None;
                EndTarget = null;
            }
            if (EndTarget != null && (!EndTarget.isAlive() || EndTarget.Sid() != _vid))
            {
                EndLink = Link.None;
                EndTarget = null;
            }
            if (EndLink == Link.Launched && EndTarget != null && !Airborne(EndTarget, 0.2f) && !Rising(EndTarget) && !EndTarget.under_forces)
            {
                // the body it kicked away has already landed, still dazed: only Wingbreaker itself
                // follows a launch, so hand it on as Down (Hundred Fists, Severing Flash, Cross Fang...)
                if (EndTarget.hasStatus("stunned"))
                {
                    EndLink = Link.Down;
                }
                else
                {
                    EndLink = Link.None;
                    EndTarget = null;
                }
            }
            if (!_flow)
            {
                // (a master never stands and swings between techniques: FlowStrikes takes over at once)
                A.startAttackCooldown();
            }
            KillAll();
            Done++;
            Finished = true;
        }

        // ------------------------------------------------------------- recoil --

        /// <summary>Parried before it could leave the ground: it is read and thrown back on its heels.</summary>
        private void Recoil(float dt)
        {
            float px = Px;
            Vector2 back = Combat.Clamp(_from - new Vector2(_fwd * 0.6f * _k, 0f));
            if (!Combat.SafeSpot(back))
            {
                back = _from;
            }
            if (Enter())
            {
                Anim.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
                if (Kit.Visible(_from))
                {
                    Fx.HitSpark(BodyPos() + new Vector2(_fwd * 4f * px, 1f * px), px, 1, new Vector2(-_fwd, 0.5f), 6);
                    Kit.DustRing(_from, px, A.current_tile, 6, 20f, 0.35f, _k);
                }
            }
            float k = Mathf.Clamp01(_pt / TRecoil);
            Pose.Ground = Vector2.Lerp(_from, back, Anim.OutQuad(k));
            Pose.Height = 0.3f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.7f));
            Pose.Rot = Mathf.Lerp(-28f, 0f, Anim.OutCubic(k));
            Pose.SX = Mathf.Lerp(0.9f, 1f, k);
            Pose.SY = Mathf.Lerp(1.1f, 1f, k);
            if (k >= 1f)
            {
                _land = Pose.Ground;
                Next(Phase.Land);
            }
        }

        // ------------------------------------------------------------- victim --

        /// <summary>A short upward pop (the first uppercut of level I).</summary>
        private void VPop(float up, float duration, float spin)
        {
            if (!Held)
            {
                return;
            }
            _vs = VState.Pop;
            _vt = 0f;
            _vPop0 = _p.Pose.Height;
            _vPopH = Mathf.Min(up, Mathf.Max(0f, _maxH - _vPop0));
            _vSpin = spin;
            _vPopT = duration;
        }

        /// <summary>The held enemy between the attacker's blows: hanging, popped up, bouncing out of the crater.</summary>
        private void Victim(float dt)
        {
            _vt += dt;
            switch (_vs)
            {
                case VState.Hang:
                    {
                        if (_popUp)
                        {
                            // still on its feet: the ground wave is on its way
                            _p.Pose.Ground = _vg;
                            _p.Pose.Height = 0f;
                            _p.Pose.Rot = Mathf.MoveTowardsAngle(_p.Pose.Rot, 0f, 360f * dt);
                            break;
                        }
                        // anime hang time: what was left of its flight dies out, it floats, dazed
                        float tau = 0.06f;
                        float rise = Mathf.Clamp(_vRise * tau * (1f - Mathf.Exp(-_vt / tau)), -0.25f * _k, 0.7f * _k);
                        float h = _vh0 + rise + 0.04f * _k * Mathf.Sin(_vt * 9f);
                        _p.Pose.Height = Mathf.Clamp(h, 0f, Mathf.Max(_vh0, _maxH - 0.2f * _k));
                        _p.Pose.Ground = _vg;
                        _vSpin = Mathf.MoveTowards(_vSpin, 0f, 1100f * dt);
                        float rot = _p.Pose.Rot + _vSpin * dt;
                        if (Mathf.Abs(_vSpin) < 60f)
                        {
                            rot = Mathf.MoveTowardsAngle(rot, 6f * Mathf.Sin(_vt * 7f), 260f * dt);
                        }
                        _p.Pose.Rot = rot;
                        _p.Pose.SX = _p.Pose.SY = 1f;
                        break;
                    }
                case VState.Pop:
                    {
                        float k = Mathf.Clamp01(_vt / Mathf.Max(0.05f, _vPopT));
                        _p.Pose.Height = _vPop0 + _vPopH * Anim.OutCubic(k);
                        _p.Pose.Ground = _vg;
                        _p.Pose.Rot += _vSpin * dt * (1f - 0.6f * k);
                        _p.Pose.SX = Mathf.Lerp(0.85f, 1f, k);
                        _p.Pose.SY = Mathf.Lerp(1.2f, 1f, k);
                        break;
                    }
                case VState.Slam:
                    {
                        // spiked: driven straight down, faster and faster, turning to lie flat
                        float k = Mathf.Clamp01(_vt / TSlam);
                        float e = k * k;
                        float lying = _fwd > 0f ? 90f : -90f;
                        _p.Pose.Ground = Vector2.Lerp(_vg, _crater, e);
                        _p.Pose.Height = _slamH * (1f - e);
                        _p.Pose.Rot = Mathf.LerpAngle(_slamRot, lying, Anim.OutQuad(k));
                        _p.Pose.SX = 0.84f;
                        _p.Pose.SY = 1.22f;
                        if (k >= 1f)
                        {
                            SpikeLand(lying);
                        }
                        break;
                    }
                case VState.Bounce:
                    {
                        float k = Mathf.Clamp01(_vt / TBounce);
                        float lying = _fwd > 0f ? 90f : -90f;
                        _p.Pose.Ground = _crater;
                        _p.Pose.Height = 0.3f * _k * Mathf.Sin(Mathf.PI * k) * (1f - 0.3f * k);
                        _p.Pose.Rot = Mathf.LerpAngle(_bounceRot, lying, Anim.OutQuad(k));
                        float amp = Anim.Spring(_vt, 10f, 20f);
                        _p.Pose.SX = 1f + 0.2f * amp;
                        _p.Pose.SY = 1f - 0.25f * amp;
                        if (k >= 1f)
                        {
                            Settle();
                        }
                        break;
                    }
            }
        }

        /// <summary>The thrown (or spiked) body comes to rest where it hit the ground: let go, grounded and stunned (Down).</summary>
        private void Settle()
        {
            _p.Pose.Ground = _crater;
            _p.Pose.Height = 0f;
            _p.Pose.SX = _p.Pose.SY = 1f;
            _p.Pose.Rot = _fwd > 0f ? 90f : -90f;
            _vs = VState.Done;
            _p.Release(true);
            _p = null;
            _vShadow?.Kill();
            _vShadow = null;
            if (_v.isAlive())
            {
                // it stays down: grounded (a flier's wings give out) and stunned; a
                // spiked one (I) only for a moment, long enough for an ally to strike
                if (_spike)
                {
                    Combat.Stagger(_v, 1.1f);
                }
                else
                {
                    WingbreakerGround.Down(_v, Level >= 3 ? 6f : 3f);
                }
                EndLink = Link.Down;
                EndTarget = _v;
            }
        }

        // ------------------------------------------------------------- blows --

        /// <summary>
        /// A blow lands: the first one asks the enemy whether it answers (parry, evade); damage through
        /// Combat.Hit; hit star and sparks; a held enemy flashes. False if nothing was hit.
        /// size -1: no hit star (the crash has its own).
        /// </summary>
        private bool Strike(float mult, bool lethal, int size, Vector2 at, Vector2 away, int sparks)
        {
            if (_vGone || _v == null || !_v.isAlive() || _whiff || _parried)
            {
                return false;
            }
            if (!_reached)
            {
                _reached = true;
                if (_p == null)
                {
                    Contact c = Combat.Reach(this, _v, false);
                    if (c != Contact.Hit)
                    {
                        Answered(c);
                        if (_whiff && Kit.Visible(at))
                        {
                            Fx.FocusLines(at, Px, Fx.Count(4), 10f * _k, 18f * _k, 0.08f);
                        }
                        return false;
                    }
                }
            }
            if (_cm <= 0f)
            {
                return false;
            }
            HitResult r = Combat.Hit(A, _v, mult * _cm, lethal, true, 0f, this);
            if (lethal && r.Crit)
            {
                _crit = true;
            }
            if (size >= 0 && Kit.Visible(at))
            {
                Fx.HitSpark(at, Px, size, away, Fx.Count(sparks));
            }
            if (Held)
            {
                _p.Flash(size >= 2 ? 0.08f : 0.05f, VFlash());
            }
            if (Dev.On)
            {
                Dev.Trace("wingbreaker " + Id + " blow x" + (mult * _cm).ToString("F2") + " -> " + _v.asset.id + " hp " + _v.getHealth() + (lethal ? " (finisher)" : "") + (r.Crit ? " CRIT" : ""));
            }
            return true;
        }

        // ------------------------------------------------------------ effects --

        /// <summary>A crescent swoosh glued to the attacker's body, mirrored to the way it faces.</summary>
        private void Swish(Sprite[] arc, float life, Vector2 offsetPx)
        {
            Color core = Level >= 3 ? Color.white : _pal.Core;
            Color edge = Level >= 2 ? _pal.Edge : new Color(0.9f, 0.95f, 1f, 1f);
            Kit.Swish(this, arc, life, offsetPx, _fwd, core, edge);
        }

        /// <summary>Afterimages on fast motion (tier-tinted from II).</summary>
        private void Trails(float dt, float every, float life)
        {
            _ghost -= dt;
            if (_ghost > 0f)
            {
                return;
            }
            _ghost = every;
            Fx.Afterimage(this, life, Ghost0, Ghost1);
        }

        /// <summary>Keeps an effect on the unit's body as it flies.</summary>
        private void Follow(Particle p, Vector2 offset)
        {
            if (p == null)
            {
                return;
            }
            p.Step = (q, d) => q.Pos = BodyPos() + offset;
        }

        private void SpawnShadow()
        {
            float px = Px;
            if (_shadow == null)
            {
                _shadow = Fx.Spawn(Art.Disk(ShadowRx(Pose.PivotUp, 1f), 0.42f, 0.45f), Pose.Ground, px, 1f, FxLayer.Ground, 3);
                if (_shadow != null)
                {
                    _shadow.Held = true;
                    _shadow.Snap = true;
                    _shadow.Color(new Color(0f, 0f, 0f, 0.36f));
                }
            }
            if (_vShadow == null && Held)
            {
                float vpx = Mathf.Clamp(_v.actor_scale, 0.04f, 0.4f);
                _vShadow = Fx.Spawn(Art.Disk(ShadowRx(_p.Pose.PivotUp, 1f), 0.42f, 0.45f), _p.Pose.Ground, vpx, 1f, FxLayer.Ground, 3);
                if (_vShadow != null)
                {
                    _vShadow.Held = true;
                    _vShadow.Snap = true;
                    _vShadow.Color(new Color(0f, 0f, 0f, 0.32f));
                }
            }
        }

        /// <summary>Shadows stay on the ground and shrink and fade with height.</summary>
        private void UpdateShadows()
        {
            Shadow(_shadow, Pose, _maxH);
            if (Held)
            {
                Shadow(_vShadow, _p.Pose, _maxH);
            }
        }

        private static void Shadow(Particle s, Pose pose, float maxH)
        {
            if (s == null || !s.Alive)
            {
                return;
            }
            float h = Mathf.Clamp01(pose.Height / Mathf.Max(1f, maxH * 0.6f));
            Sprite sp = Art.Disk(ShadowRx(pose.PivotUp, Mathf.Lerp(1f, 0.45f, h)), 0.42f, 0.45f);
            if ((object)s.R.sprite != sp)
            {
                s.R.sprite = sp;
                s.Frames[0] = sp;
            }
            s.Pos = pose.Ground;
            s.Grad = null;
            s.R.color = new Color(0f, 0f, 0f, pose.Hidden ? 0f : Mathf.Lerp(0.36f, 0.12f, h));
        }

        private static int ShadowRx(float pivotUp, float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (pivotUp / 3.5f)));
        }

        private void SpawnGlow()
        {
            if (_glow != null || !Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            _glow = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 0, true) : null;
            if (_glow != null)
            {
                _glow.Held = true;
                _glow.UseEuler = true;
                Fx.Glue(_glow, this, Fx.WithA(_pal.Edge, 0f));
            }
        }

        private void FlashSelf(float seconds)
        {
            _selfFlash = Mathf.Max(_selfFlash, seconds);
            if (_glow == null)
            {
                SpawnGlow();
            }
        }

        /// <summary>
        /// An impact frame went up behind the pair: for that instant they are flat shapes against it,
        /// dark (white against a Black Flash), the attacker through its glow and a held enemy through
        /// its hit flash (a tiered one keeps its own colour's). White on the white flash, they vanished.
        /// </summary>
        private void Framed(bool black)
        {
            _framed = SilhouetteTime;
            _framedBlack = black;
            if (_glow == null)
            {
                SpawnGlow();
            }
            if (Held && (black || Tiers.Of(_v) == Tier.None))
            {
                _p.Flash(0.07f, black ? Color.white : ImpactDark);
            }
        }

        public override void Frame()
        {
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, GlowColor());
        }

        /// <summary>The attacker's glow: gathering power, flaring at the grab, white-hot riding down (III), cooling.</summary>
        private Color GlowColor()
        {
            if (_framed > 0f)
            {
                _framed -= Clock.Dt;
                return _framedBlack ? Fx.WithA(Color.white, 0.95f) : Fx.WithA(Color.Lerp(ImpactDark, _pal.Dark, 0.3f), 0.92f);
            }
            if (_selfFlash > 0f)
            {
                _selfFlash -= Clock.Dt;
                return Fx.WithA(Color.white, 0.85f);
            }
            switch (_phase)
            {
                case Phase.Crouch:
                    {
                        float k = Mathf.Clamp01(_pt / TCrouch);
                        return Fx.WithA(_pal.Edge, 0.45f * k * (0.75f + 0.25f * Mathf.Sin(_pt * 50f)));
                    }
                case Phase.Leap:
                case Phase.Over:
                    return Fx.WithA(_pal.Edge, Mathf.Lerp(0.35f, 0.04f, Mathf.Clamp01(_pt / Mathf.Max(0.1f, _tPhase))));
                case Phase.Spin:
                    return Fx.WithA(_pal.Edge, 0.12f);
                case Phase.Fling:
                    return Level >= 3 ? Fx.WithA(Color.Lerp(_pal.Edge, _pal.Core, Mathf.Clamp01(_pt / TFling)), 0.7f) : Fx.WithA(_pal.Edge, 0.1f);
                case Phase.Impact:
                    return Fx.WithA(Color.white, 0.9f);
                case Phase.Drop:
                    return _hit ? Fx.WithA(_pal.Core, 0.7f) : Fx.WithA(_pal.Edge, 0.2f);
                case Phase.Backflip:
                case Phase.Rebound:
                case Phase.Fall:
                    // cooling off the blow (at 0.5 it painted the whole body in the tier colour)
                    return Fx.WithA(_pal.Edge, Mathf.Lerp(0.32f, 0f, Mathf.Clamp01(_pt / 0.22f)));
                default:
                    return Fx.WithA(_pal.Edge, 0f);
            }
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        private void KillShadow()
        {
            _shadow?.Kill();
            _shadow = null;
        }

        private void KillAll()
        {
            KillShadow();
            KillGlow();
            _vShadow?.Kill();
            _vShadow = null;
            _trail = false;
        }

        public override void Abort()
        {
            if (Held)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            _pend = false;
            KillAll();
        }

        public override void OnEnd(bool aborted)
        {
            KillAll();
        }
    }

    /// <summary>
    /// Brings a flier down. A dragon is sent into its own landing (if there is ground under it) and
    /// kept down once it touches down; an ordinary flier loses its wings and is stunned at once (the
    /// stun keeps its AI from taking off again); anything that can't land is only stunned.
    ///
    /// A dragon can't carry the game's "stunned" status (its status tier is too low), and being hit
    /// makes it breathe fire or take off again: in game the first version's dragon was back on its
    /// feet and burning the attacker a second and a half after it was knocked down. So a downed
    /// dragon is held by hand for its whole stun: its attacks and take-offs are cancelled, it is
    /// kept waiting, and stars circle its head (the game draws no status effects on dragons).
    ///
    /// A dragon brought down by a blow that carries a trench (Wingbreaker III) ploughs one into the
    /// ground the moment it touches down: real world damage along the way it came in (Gouge).
    /// </summary>
    internal static class WingbreakerGround
    {
        private sealed class Entry
        {
            public Actor A;
            public long Id;
            public float Stun;
            /// <summary>Gives up waiting for the landing after this.</summary>
            public double Until;
            /// <summary>On the ground and held there until DownUntil.</summary>
            public bool Down;
            public double DownUntil;
            public float Wait, Daze, Spin;
            /// <summary>The trench it ploughs when it touches down (0: none), who brought it down, and where it was struck.</summary>
            public int Force;
            public float Width;
            public Actor By;
            public long ById;
            public Vector2 From;
        }

        /// <summary>Who brought it down, if that unit is still the same living one.</summary>
        private static Actor Attacker(Entry e)
        {
            return e.By != null && e.By.Sid() == e.ById && e.By.isAlive() ? e.By : null;
        }

        private static readonly List<Entry> _list = new List<Entry>();
        private static int _frame = -1;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FighterTick += Tick;
            Hooks.WorldCleared += _list.Clear;
        }

        /// <summary>Grounds 'v' (if the game allows it) and stuns it for 'seconds'.</summary>
        public static void Down(Actor v, float seconds)
        {
            Down(v, seconds, null, 0, 0f);
        }

        /// <summary>
        /// Grounds 'v' and stuns it for 'seconds'; a dragon sent down from the sky ploughs a trench
        /// ('force', 'width' tiles: Kit.Ravage) where it touches down. 'by': who brought it down.
        /// </summary>
        public static void Down(Actor v, float seconds, Actor by, int force, float width)
        {
            if (v == null || !v.isAlive())
            {
                return;
            }
            bool dragon = v.getActorComponent<Dragon>() != null;
            if (!v.isFlying())
            {
                if (dragon)
                {
                    v.makeStunned(seconds);
                    Track(v, seconds, true, null, 0, 0f);
                }
                else
                {
                    // (without the game's white status flash: makeStunned lights the unit up white
                    // when no blow has flashed it for 0.3 s, and a Skyfall's body is downed when it
                    // settles in its crater, a third of a second after the crash: it popped like a
                    // second blow. A dragon takes no statuses, so makeStunned never flashes it)
                    Combat.Daze(v, seconds);
                }
                return;
            }
            if (dragon)
            {
                if (v.ai != null && v.current_tile != null && Dragon.canLand(v) && AssetManager.tasks_actor != null && AssetManager.tasks_actor.dict.ContainsKey("dragon_land"))
                {
                    v.cancelAllBeh();
                    v.setTask("dragon_land");
                    Track(v, seconds, false, by, force, width);
                    return;
                }
                v.makeStunned(seconds);
                return;
            }
            if (v.asset.flying)
            {
                Combat.Daze(v, seconds);   // born to fly (a UFO): it can only be rocked
                return;
            }
            v.setFlying(false);
            Combat.Daze(v, seconds);
        }

        /// <summary>
        /// Holds a dragon down (or waits for it to land). Every stagger on a landed dragon comes here
        /// now (Combat.Stagger), so a lighter blow on one already held down never cuts its stun short
        /// (a Skull Cracker's 3 s down must survive the next 0.4 s knock), and its daze stars keep turning.
        /// </summary>
        private static void Track(Actor v, float seconds, bool landed, Actor by, int force, float width)
        {
            long id = v.Sid();
            double now = Fighter.Now;
            float spin = Random.value * 360f;
            Vector2 from = v.current_position;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                Entry e = _list[i];
                if (e.A != v && e.Id != id)
                {
                    continue;
                }
                if (e.A == v && e.Id == id)
                {
                    float left = e.Down ? (float)(e.DownUntil - now) : e.Stun;
                    seconds = Mathf.Max(seconds, left);
                    spin = e.Spin;
                    // a trench still owed (it was knocked down and has not touched down yet) is kept
                    if (!e.Down && e.Force > force)
                    {
                        force = e.Force;
                        width = e.Width;
                        by = Attacker(e);
                        from = e.From;
                    }
                }
                _list.RemoveAt(i);
            }
            if (landed)
            {
                v.makeStunned(seconds);
                if (force > 0)
                {
                    // it touched down before we saw it land (a blow held it down first): plough now
                    Gouge(v, v.current_position, from, v.flip ? 1f : -1f, force, width, by);
                    force = 0;
                }
            }
            _list.Add(new Entry
            {
                A = v,
                Id = id,
                Stun = seconds,
                Until = now + 4.0,
                Down = landed,
                DownUntil = landed ? now + seconds : 0.0,
                Spin = spin,
                Force = force,
                Width = width,
                By = by,
                ById = by != null ? by.Sid() : 0L,
                From = from
            });
        }

        /// <summary>
        /// A dragon brought down ploughs a long trench: from where it was struck (or a body's length
        /// and more behind it, the way it faces) up to where it lies, real world damage along the
        /// line (Kit.Ravage: the game's own explosion pixels, lowered ground; force 3+ sets it alight)
        /// and a furrow of dust racing along it. Never within reach of its attacker's feet: a burning
        /// trench is not where a fighter should be standing when its move ends.
        /// </summary>
        public static void Gouge(Actor v, Vector2 end, Vector2 from, float face, int force, float width, Actor by)
        {
            if (v == null || force <= 0 || Combat.TileAt(end) == null)
            {
                return;
            }
            Wingbreaker.Box b = Wingbreaker.Measure(v);
            float len = Mathf.Clamp(b.Half * 2f + 2f, 4f, 9f);
            Vector2 dir = end - from;
            if (dir.sqrMagnitude > 2.25f)
            {
                // it carried on a little before it came down: the trench follows the way it went
                len = Mathf.Clamp(dir.magnitude, len, 12f);
                dir.Normalize();
            }
            else
            {
                dir = new Vector2(face, 0f);
            }
            Vector2 start = Combat.Clamp(end - dir * len);
            bool spare = by != null && by.isAlive();
            Vector2 feet = spare ? by.current_position : Vector2.zero;
            float keep = Mathf.RoundToInt(width) + 1.3f;
            // (Kit.Trench: the trench itself doesn't burn, only a few tiles along its banks; blast after
            // blast with its own burning rim lit a carpet of fire along and inside it. Around the
            // attacker's feet nothing is dug or lit, the rims of the blasts beyond included)
            Kit.Trench(start, end, width, force, by, true, spare ? feet : (Vector2?)null, spare ? keep : 0f);
            if (Dev.On)
            {
                Dev.Trace("wingbreaker trench under " + v.asset.id + " force " + force + " w " + width.ToString("F1") + " len " + len.ToString("F1"));
            }
            WorldTile tile = Combat.TileAt(end);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            if (!Kit.Visible(end) && !Kit.Visible(start))
            {
                return;
            }
            // the furrow: dust thrown up along it, racing from the far end to the body
            float px = Mathf.Clamp(v.actor_scale * 0.5f, 0.1f, 0.16f);
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
            int puffs = Mathf.Clamp(Mathf.CeilToInt(len / 0.4f), 4, Fx.Count(22));
            Vector2 side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < puffs; i++)
            {
                float u = (i + 0.5f) / puffs;
                Vector2 at = Vector2.Lerp(start, end, u) + side * Random.Range(-0.35f, 0.35f) * width;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, Random.Range(0.45f, 0.7f), FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Delay = 0.22f * u;
                p.Moving(side * Random.Range(-14f, 14f) * px + dir * 10f * px, Random.Range(6f, 12f) * px, 3f).Sized(0.7f, 1.5f)
                    .Color(Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.55f), Fx.WithA(dust, 0f));
            }
            Kit.DustRing(end, px, tile, 14, 70f, 0.7f, 1.6f);
            Kit.Debris(end, px, tile, 10, 1.4f);
            Kit.Shake(end, 0.3f, 0.012f, 1.5f);
        }

        /// <summary>Once a frame: a dragon that has touched down is held down, dazed, until its stun is over.</summary>
        private static void Tick(Fighter f, float dt)
        {
            if (_list.Count == 0 || Time.frameCount == _frame)
            {
                return;
            }
            _frame = Time.frameCount;
            double now = Fighter.Now;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                Entry e = _list[i];
                Actor a = e.A;
                if (a == null || a.Sid() != e.Id || !a.isAlive())
                {
                    if (a != null && a.Sid() == e.Id && !e.Down && e.Force > 0)
                    {
                        // killed on its way down: it still comes down hard
                        Gouge(a, a.current_position, e.From, a.flip ? 1f : -1f, e.Force, e.Width, Attacker(e));
                    }
                    _list.RemoveAt(i);
                    continue;
                }
                if (!e.Down)
                {
                    if (now > e.Until)
                    {
                        _list.RemoveAt(i);
                    }
                    else if (!a.isFlying())
                    {
                        e.Down = true;
                        e.DownUntil = now + e.Stun;
                        a.makeStunned(e.Stun);
                        e.Wait = 0.25f;
                        if (e.Force > 0)
                        {
                            // it hits the ground: the trench, on the frame it touches down
                            Gouge(a, a.current_position, e.From, a.flip ? 1f : -1f, e.Force, e.Width, Attacker(e));
                            e.Force = 0;
                            e.By = null;
                        }
                    }
                    continue;
                }
                double left = e.DownUntil - now;
                if (left <= 0.0 || a.isFlying() || a.isInMagnet())
                {
                    _list.RemoveAt(i);   // over, or something else took it (the god hand)
                    continue;
                }
                // no fire breath, no take-off while it is down
                string task = a.ai != null && a.ai.task != null ? a.ai.task.id : null;
                if (task == "dragon_land_attack" || task == "dragon_up" || task == "dragon_slide" || task == "dragon_fly")
                {
                    a.cancelAllBeh();
                    e.Wait = 0f;
                }
                e.Wait -= dt;
                if (e.Wait <= 0f)
                {
                    e.Wait = 0.25f;
                    a.makeWait((float)left);
                }
                Daze(e, dt);
            }
        }

        /// <summary>Little stars circling the downed giant's head.</summary>
        private static void Daze(Entry e, float dt)
        {
            Actor a = e.A;
            e.Spin += 300f * dt;
            e.Daze -= dt;
            if (e.Daze > 0f)
            {
                return;
            }
            e.Daze = 0.16f;
            // a halo just over its horns: the head is well ahead of the body's middle (in game the
            // first stars circled the wing root, over its back)
            Wingbreaker.Box b = Wingbreaker.Measure(a);
            Vector2 head = a.current_position + new Vector2(b.HeadX, b.Crown + 0.12f * b.Half);
            if (!Kit.Visible(head))
            {
                return;
            }
            float rad = e.Spin * Mathf.Deg2Rad;
            Vector2 at = head + new Vector2(Mathf.Cos(rad) * b.Half * 0.36f, Mathf.Sin(rad) * b.Half * 0.12f);
            float px = Mathf.Clamp(a.actor_scale * 0.4f, 0.08f, 0.14f);
            Fx.Glint(at, px, 0.22f, new Color(1f, 0.95f, 0.6f, 1f), 0.8f);
        }
    }

    /// <summary>
    /// Wingbreaker as a technique. Entry: Master (I), Grandmaster II, Gods III. The AI uses it on a
    /// flier or a body in the air within 6 tiles (horizontally), on a giant standing on the ground
    /// (Skull Cracker), and as a link when a technique has just launched its target (even while the
    /// body is still leaving the ground: the link window is a single look, and in game the first
    /// version let it pass because the launched body had not risen yet). Any launch counts, an
    /// ally's blast included: a body thrown 0.6 tiles up is worth the leap (it was 0.8: never seen).
    /// </summary>
    internal sealed class WingbreakerTech : Technique
    {
        public override string Id => "wingbreaker";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Air;

        /// <summary>The ground fighter's answer to whatever rules the sky: leap, grab, spin, drive it down.</summary>
        public override Form Forms => Form.Grab | Form.Aerial | Form.AntiAir | Form.Throw | Form.Overhead | Form.Hunt;

        public override Form Answers => Form.Aerial;

        public override Prey Prey => Prey.Flyer | Prey.Dragon;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => new[] { 18f, 30f, 50f };
        public override float[] Cooldown => new[] { 10f, 11f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Launched;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor self = c.Self;
            float k = Mathf.Sqrt(Mathf.Clamp(self.actor_scale / 0.1f, 0.6f, 4f));
            float maxH = Wingbreaker.MaxHeight(level, k);
            bool linked = c.Linked && c.Link == Link.Launched;
            Actor v;
            if (c.Forced)
            {
                v = c.ForceTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            }
            else if (linked && c.LinkTarget != null && (Wingbreaker.Airborne(c.LinkTarget, 0.25f) || Wingbreaker.Rising(c.LinkTarget)))
            {
                // the body our last blow just threw: it may still be leaving the ground
                v = c.LinkTarget;
            }
            else
            {
                v = Pick(c, linked);
            }
            if (v == null || v == self || !v.isAlive() || Moves.Locked(v) || v.asset.is_boat || !Combat.IsFoe(self, v))
            {
                return null;
            }
            float range = c.Forced ? 14f : Wingbreaker.Range + (linked ? 1f : 0f) + Mathf.Min(1f, v.stats["size"] * 0.25f);
            if (c.Dist(v) > range)
            {
                return null;
            }
            // in the air: a flier, a body thrown high enough to be worth the leap (anyone's launch:
            // a blast, a throw), or the one our own blow has just sent up
            bool air = Wingbreaker.Airborne(v, linked || c.Forced ? 0.25f : 0.6f) || (linked && Wingbreaker.Rising(v));
            bool giant = !air && Weights.Species(v) >= Weight.Titan;
            if (!c.Forced && !air && !giant)
            {
                return null;   // a grounded enemy is not its job
            }
            if (!c.Allowed(v, out float cm))
            {
                if (!c.Forced)
                {
                    return null;
                }
                cm = 0.5f;
            }
            Wingbreaker.Kind kind = Wingbreaker.Choose(self, v, level, c.Forced);
            if (!Wingbreaker.Reachable(kind, Wingbreaker.Measure(v), maxH))
            {
                return null;   // out of reach at this level
            }
            // what its blows add up to (I 0.8 + 1.2; II grab 0.6 + crash 3.6, or heel 4.2;
            // III grab 0.6 + stomp 1 + crash 6, or heels 2.7 + 4.9): each level decisively harder
            float total = level <= 1 ? 2f : level == 2 ? 4.2f : 7.6f;
            // the right answer: nothing else touches a flier; a giant is what the heel is for
            float style = v.isFlying() ? 45f : air ? 25f : 30f;
            float score = c.Value(v, c.Expected(v, total), cm) + style;
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            // I finishes one of two ways: kicked away (Launched) or spiked back into the ground (Down),
            // the spike when an ally is there to finish the job, or one time in three; a god's orders
            // alternate the two, so the dev demos show both
            bool spike = level <= 1 && (c.Forced ? (Wingbreaker.Started & 1) == 1 : c.AlliesNear(v.current_position, 2.5f) > 0 || Random.value < 0.33f);
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = score, Data = (c.Forced ? 1 : 0) | (spike ? 2 : 0) };
        }

        /// <summary>The best thing in the sky (or the biggest giant) within reach: its focus first, fliers before giants, near before far.</summary>
        private static Actor Pick(Ctx c, bool linked)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            Actor focus = c.Focus;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                float d = c.EnemyDist[i];
                if (d > Wingbreaker.Range + 1f)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                bool air = Wingbreaker.Airborne(v, linked ? 0.25f : 0.6f) || (linked && Wingbreaker.Rising(v));
                bool giant = !air && Weights.Species(v) >= Weight.Titan;
                if (!air && !giant)
                {
                    continue;
                }
                if (Moves.Locked(v) || v.asset.is_boat || !c.Allowed(v))
                {
                    continue;
                }
                float s = (v == focus ? 3f : 0f) + (v.isFlying() ? 2f : air ? 1.5f : 1f) - d * 0.15f;
                if (s > bestScore)
                {
                    bestScore = s;
                    best = v;
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            int flags = p.Data is int f ? f : 0;
            return p.Target != null && Wingbreaker.Begin(a, p.Target, p.Level, this, (flags & 1) != 0, (flags & 2) != 0);
        }
    }
}
