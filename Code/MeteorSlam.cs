using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// METEOR SLAM. The unit crouches and gathers power, launches into the sky,
    /// flips at the top of its jump, ignites and dives onto its target like a
    /// meteor, cratering the ground and throwing everyone around it into the air.
    /// Three levels that read differently at a glance (and an aerial variant):
    ///
    /// LEVEL II, "Meteor Slam" (game seconds at x1; rise and dive stretch a little with distance):
    ///
    ///   WINDUP   0.32  crouch (squash), lean back, ground ring closes in, pebbles float,
    ///                  1-pixel power tremble, glow builds, "ting" glint
    ///   RISE    ~0.42  launch frame stretched tall, takeoff ring + dust + small crater,
    ///                  pebbles thrown, launch streaks; ease-out to the apex with afterimages
    ///                  in the tier's colour, the shadow on the ground shrinking with height
    ///   HANG     0.34  hang time: a full forward flip (tucked, smeared with afterimages),
    ///                  big glint, ignites; the landing spot is marked by a tier-coloured ring
    ///   DIVE    ~0.20  tier-tinted sonic-boom ring, then an accelerating stretched dive trailing
    ///                  fire, smoke, speed lines and afterimages; the marker closes on the spot
    ///   IMPACT   0.11  local hit-stop (the pose and the victims held; no world freeze at II):
    ///                  crushed squash, a flat silhouette for an instant against the white flash
    ///                  and starburst behind it (white on a Black Flash on a critical blow),
    ///                  crater + glowing fissures, double shockwave, dust wall, rocks from the
    ///                  rim, sparks, embers, camera shake; victims flash, then fly tumbling
    ///   RECOVER  0.36  springs back up through an overshoot, the glow cools
    ///
    /// LEVEL I, "Comet Hop" (~1.1 s): clean, quick, white and grey.
    ///
    ///   WINDUP   0.24  a shallower crouch, a grey ring and a little dust drawn in, a white glint
    ///   RISE    ~0.30  a low arc (apex ~4), white afterimages, a puff of dust at the takeoff
    ///   HANG     0.16  no flip: knees tucked, it leans back at the top (anticipation)
    ///   DIVE    ~0.17  a dust trail and white streaks instead of fire
    ///   IMPACT   0.06  no hit-stop and no decal: a small flash, a puff, a 1.4-tile dust ring,
    ///                  a few rocks; victims are knocked down (<= 1 tile)
    ///   RECOVER  0.30  a quick spring back up
    ///
    /// LEVEL III, "Cataclysm" (~2.2 s plus a 0.12 s world freeze):
    ///
    ///   WINDUP   0.42  a deep crouch; the ground cracks and glows under its feet, a tier ring
    ///                  and a white ring close in, pebbles float high, tier sparks rise
    ///   RISE    ~0.55  a launch blast (white-hot ring, dust, a speed-line pillar) and a leap
    ///                  twice as high: it leaves the top of the view (the camera stays put)
    ///   HANG     0.40  up there: a double flip with a glint, then it ignites white-hot. On the
    ///                  ground only its shadow (a dot) and a red warning ring remain (0.6 s)
    ///   DIVE    ~0.24  it comes back down as a white-hot meteor (white core, then yellow,
    ///                  orange and red trail, then smoke), breaking the sound barrier twice
    ///   IMPACT   0.14  impact frame behind the landing, the slammer a silhouette against it (a
    ///                  Black Flash on a critical blow) + 0.12 s world freeze; the ground lit up;
    ///                  a 1.5-tile crater with glowing magma cracks and broken ground around it,
    ///                  shockwave rings at 2 and 3.5 tiles, a dust wall rolling out to 5 tiles;
    ///                  inside 3.5 tiles everyone is launched high, out to 5 tiles the wave
    ///                  knocks everyone down as it passes; the crater smoulders for 20 s
    ///   RECOVER  0.50  a superhero landing held for a beat, then it rises, the heat cooling
    ///
    /// AIR METEOR (any level): a unit still in the air after another move skips the windup
    /// and the leap: a short hang (a flip at II and III) and the dive from where it is.
    ///
    /// THE WORLD TAKES IT (Kit.Ravage, on the impact frame): I leaves the ground alone (a heavy
    /// performer scorches it); II blasts a real crater (force 2, r 2); III a burning one (force 3,
    /// r 3.5, the launch spot scorched too). A God hits one force harder over 1.5x the radius (a
    /// Cataclysm of force 4, r ~5) and its fissures run out with the shockwave, breaking the land
    /// out to ~9 tiles (a Grandmaster's scorch 3 cracks). Higher tiers spread the blast itself
    /// (Grandmaster 1.15x, God 1.3x) and leap further. The fire never takes the landing spot.
    ///
    /// TEMPO: the landing recovers in the tier's Linger share of the time (Expert 0.85, Master 0.55,
    /// Grandmaster 0.48, God 0.42) and the crouch and the hang in its square root: the higher the
    /// tier, the sooner it hits and the sooner it goes on.
    ///
    /// THE FALL FINISHES THEM: a blow that would kill leaves its victim at the edge of death instead,
    /// and the ground finishes it where it comes down: the crowd a Cataclysm wipes out is thrown
    /// high and falls dead all around the crater, instead of dropping dead in a heap on the landing.
    ///
    /// FLOW (Masters and up): no
    /// attack cooldown (their ordinary swings are FlowStrikes), and the slam always leaves a
    /// chain hook when something is left to chain onto: Scattered (2+ hit), Launched / Down
    /// (one hit), Close (a foe right there). Against a worthy lone foe (a Master or stronger, a
    /// monster) a Flow tier will drop a meteor on it alone.
    ///
    /// Everything happens in game time, so it pauses with the game.
    /// Where it lands, it lands: the target is tracked until the dive starts (if it falls first,
    /// the foe nearest the spot is tracked instead, once). From the second half of the hang the
    /// foes under it freeze, looking up (no swings at its shadow); they can still answer the blast.
    /// </summary>
    internal sealed class MeteorSlam : Move
    {
        public const float MinRange = 3.5f;
        public const float MaxRange = 14f;

        /// <summary>Cataclysm (III): the shockwave knocks everyone down out to this many tiles.</summary>
        public const float OuterRadius = 5f;

        private enum Phase
        {
            Windup,
            Rise,
            Hang,
            Dive,
            Impact,
            Recover
        }

        // the meteor's palette (fire, smoke, dust and rock keep their natural colours at every level)
        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Core = new Color(1f, 0.98f, 0.82f, 1f);
        private static readonly Color Fire1 = new Color(1f, 0.84f, 0.32f, 1f);
        private static readonly Color Fire2 = new Color(1f, 0.48f, 0.12f, 1f);
        private static readonly Color Fire3 = new Color(0.78f, 0.18f, 0.07f, 1f);
        private static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);
        private static readonly Color Charge = new Color(1f, 0.88f, 0.55f, 1f);
        private static readonly Color Warning = new Color(1f, 0.14f, 0.1f, 1f);

        private Actor _target;
        private long _targetId;
        /// <summary>The target fell before the dive and another was picked (once per slam).</summary>
        private bool _retargeted;
        private readonly Vector2 _from;
        private Vector2 _to;
        private readonly float _k;
        private readonly bool _air;
        private readonly Palette _pal;
        private readonly Tier _tier;
        /// <summary>A Master or above: lands crisply and goes (TierInfo.Flow).</summary>
        private readonly bool _flow;
        /// <summary>How far the blast spreads for this tier (MeteorSlamTech.AreaScale).</summary>
        private readonly float _area;
        /// <summary>How fast the landing recovery plays (1 / TierInfo.Linger: the same springs, less time).</summary>
        private readonly float _recoverSpeed = 1f;

        // the shape of this slam (its level, its distance, the air variant)
        private readonly float _tWindup, _tRise, _tHang, _tDive, _tImpact, _tRecover, _apex;
        /// <summary>How far along the ground (0 = takeoff, 1 = landing) the hang starts and ends.</summary>
        private readonly float _hang0, _hang1;
        private readonly int _flips;

        private Phase _phase = Phase.Windup;
        private float _pt;
        private bool _entered;
        private bool _glinted, _shadowMade, _marked, _boomed, _wasSeen;
        private bool _crit;
        /// <summary>The impact frame was drawn (the slammer turns into its silhouette against it).</summary>
        private bool _framed;
        private float _ghost, _ember, _streak, _pin;
        private float _since;
        private Vector2 _lastTrail;
        private bool _trail;
        private Color _dust;

        private Particle _shadow, _glow, _marker, _markerRing, _markerFill;
        private readonly List<Particle> _pebbles = new List<Particle>();
        private readonly List<Launch> _launches = new List<Launch>();
        private readonly List<Wave> _wave = new List<Wave>();
        private readonly List<Rift> _rifts = new List<Rift>();

        /// <summary>Who the blast caught (the main blast, then the outer ring), and the one to chain onto.</summary>
        private int _landed, _waveHits;
        private Actor _primary;
        private long _primaryId = -1;
        private float _primaryD = float.MaxValue;

        /// <summary>A point of a Grandmaster's or God's fissure: the land breaks there as the wave passes.</summary>
        private struct Rift
        {
            public Vector2 At;
            public float When;
            public int Force;
            public bool Hot;
        }

        private struct Launch
        {
            public Actor U;
            public long Id;
            public float Distance, Apex, Spin;
            /// <summary>The blow would have killed it: it flies at the edge of death and the fall finishes it.</summary>
            public bool Doomed;
        }

        /// <summary>Cataclysm's outer ring: knocked down when the shockwave reaches it.</summary>
        private struct Wave
        {
            public Actor U;
            public long Id;
            public float At;
        }

        /// <summary>Slams that have started / landed since the game started (dev log; air slams included).</summary>
        internal static int Started, Landed;

        private MeteorSlam(Actor a, Vector2 to, Actor target, int level, bool air) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _air = air;
            _pal = Palette.For(a, Level);
            _from = a.current_position;
            _to = to;
            _target = target;
            _targetId = target != null ? target.Sid() : -1;
            float d = Vector2.Distance(_from, _to);
            _k = Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
            _tier = Tiers.Of(a);
            TierInfo info = Tiers.Info(_tier);
            _flow = info != null && info.Flow;
            _area = MeteorSlamTech.AreaScale(a);
            switch (Level)
            {
                case 1:
                    _tWindup = 0.24f;
                    _tRise = 0.26f + 0.01f * d;
                    _tHang = 0.16f;
                    _tDive = 0.14f + 0.005f * d;
                    _apex = Mathf.Clamp(3.4f + 0.1f * d, 3.6f, 4.2f) * _k;
                    _tImpact = 0.06f;
                    _tRecover = 0.3f;
                    _flips = 0;
                    _hang0 = 0.55f;
                    _hang1 = 0.62f;
                    break;
                case 2:
                    _tWindup = 0.32f;
                    _tRise = 0.34f + 0.012f * d;
                    _tHang = 0.34f;
                    _tDive = 0.15f + 0.006f * d;
                    _apex = Mathf.Clamp(4.2f + 0.22f * d, 4.5f, 7.5f) * _k;
                    _tImpact = 0.11f;
                    _tRecover = 0.36f;
                    _flips = 1;
                    _hang0 = 0.55f;
                    _hang1 = 0.66f;
                    break;
                default:
                    _tWindup = 0.42f;
                    _tRise = 0.46f + 0.012f * d;
                    _tHang = 0.4f;
                    _tDive = 0.2f + 0.004f * d;
                    _apex = Mathf.Clamp(8.4f + 0.44f * d, 9f, 15f) * _k;
                    _tImpact = 0.14f;
                    _tRecover = 0.5f;
                    _flips = 2;
                    _hang0 = 0.55f;
                    _hang1 = 0.62f;
                    break;
            }
            if (air)
            {
                // already up there: a short hang (a flip at II and III) and the dive from this height
                float h = Mathf.Max(0.3f, a.position_height);
                // (the dive starts where it really is: with the 0.3 floor on it too, a unit let go
                // lower than that snapped a few pixels up on the dive's first frame)
                _apex = Mathf.Max(0.05f, a.position_height);
                _tHang = Level <= 1 ? 0.12f : Level == 2 ? 0.24f : 0.3f;
                _flips = Level <= 1 ? 0 : 1;
                _tDive = 0.12f + 0.006f * d + 0.02f * h;
                bool hang = h >= 1f * _k;
                _phase = hang ? Phase.Hang : Phase.Dive;
                _hang0 = 0f;
                _hang1 = hang ? 0.1f : 0f;
                Pose.Height = a.position_height;
            }
            // the tier's tempo (TierInfo.Linger: Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42):
            // the landing plays the same springs and the same superhero beat in that share of the time
            // (a God's Cataclysm: 0.21 s instead of 0.5); the crouch and the hang, the anticipation
            // that sells the leap, tighten by its square root (a God's: 0.27 s and 0.26 s, not 0.42
            // and 0.4) so a higher tier hits sooner without losing the beat
            float linger = Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
            _recoverSpeed = 1f / linger;
            _tRecover *= linger;
            float tempo = Mathf.Sqrt(linger);
            _tWindup *= tempo;
            _tHang *= tempo;
            if (Mathf.Abs(_to.x - _from.x) > 0.2f)
            {
                Pose.FaceRight = _to.x > _from.x;
            }
            Sprite s = Fx.UnitSprite(a);
            Pose.PivotUp = s != null ? Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) : 3.5f;
            Pose.HideShadow = true;
        }

        /// <summary>
        /// The crouch gathers power (a ground slam's wind-up, never the air variant's): a quick blow
        /// now breaks it and throws the slammer off its leap.
        /// </summary>
        public override bool Charging => _phase == Phase.Windup && !_air && !Finished;

        /// <summary>Whom the meteor falls on (its target, while it still follows one).</summary>
        public override Actor Aim => _target;

        // ------------------------------------------------------------ helpers --

        public static bool CanPerform(Actor a)
        {
            return Moves.CanPerform(a);
        }

        /// <summary>
        /// Free in the air on its own (let go mid-air by its previous move): above the ground, not
        /// flying, not thrown (under forces), not locked in a move; everything else as Moves.CanPerform.
        /// </summary>
        public static bool CanPerformAir(Actor a)
        {
            if (a == null || !a.isAlive() || a.position_height <= 0.05f || Moves.Locked(a) || a.under_forces
                || a.isInLiquid() || a.isInsideSomething() || a.isInMagnet() || a.isFlying() || a.is_immovable
                || a.is_unconscious)
            {
                return false;
            }
            ActorAsset s = a.asset;
            if (!s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render || s.special
                || s.skip_fight_logic || !s.can_be_moved_by_powers)
            {
                return false;
            }
            return !ControllableUnit.isControllingUnit(a);
        }

        /// <summary>How far an Air Meteor reaches from this height (higher up, further).</summary>
        public static float AirRange(Actor a, int level)
        {
            float h = a != null ? Mathf.Max(0f, a.position_height) : 0f;
            return Mathf.Clamp(3f + 1.5f * h, 3f, level <= 1 ? 6f : level == 2 ? 9f : 12f);
        }

        /// <summary>Somewhere to land: solid ground (not a wall or mountain, lava, water or fire).</summary>
        public static bool GoodLanding(WorldTile t)
        {
            return Combat.SafeTile(t);
        }

        /// <summary>Starts a Meteor Slam onto a point (and tracks 'target' until the dive, if given).</summary>
        public static bool Begin(Actor a, Vector2 to, Actor target, int level = 2)
        {
            if (!CanPerform(a))
            {
                return false;
            }
            to = Combat.Clamp(to);
            if (!GoodLanding(Combat.TileAt(to)) && !Combat.FindLanding(to, 2f, out to))
            {
                return false;
            }
            MeteorSlam m = new MeteorSlam(a, to, target, level, false);
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(m._from, to, 0.5f) + new Vector2(0f, Mathf.Min(m._apex, 7f) * 0.42f));
            return true;
        }

        /// <summary>
        /// AIR METEOR: a unit still in the air after another move (Link.SelfAir) skips the leap and
        /// dives onto 'to' from its current height. Only for a unit that is free up there on its own
        /// (CanPerformAir), and only within reach of that height (AirRange).
        /// </summary>
        public static bool BeginFromAir(Actor a, Vector2 to, Actor target, int level)
        {
            if (!CanPerformAir(a))
            {
                return false;
            }
            to = Combat.Clamp(to);
            if (!GoodLanding(Combat.TileAt(to)) && !Combat.FindLanding(to, 2f, out to))
            {
                return false;
            }
            if (Vector2.Distance(a.current_position, to) > AirRange(a, level) + 2f)
            {
                return false;
            }
            MeteorSlam m = new MeteorSlam(a, to, target, level, true);
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(m._from, to, 0.5f) + new Vector2(0f, m._apex * 0.5f));
            return true;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _pt += dt;
            if (!_shadowMade)
            {
                _shadowMade = true;
                MakeShadow();
            }
            Track(dt);
            switch (_phase)
            {
                case Phase.Windup:
                    Windup(dt);
                    break;
                case Phase.Rise:
                    Rise(dt);
                    break;
                case Phase.Hang:
                    Hang(dt);
                    break;
                case Phase.Dive:
                    Dive(dt);
                    break;
                case Phase.Impact:
                    Impact(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
            UpdateShadow();
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("slam " + Id + " " + Techniques.Roman(Level) + (_air ? " air " : " ") + _phase + " -> " + p + " at t=" + T.ToString("F3") + " h=" + Pose.Height.ToString("F2"));
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

        /// <summary>
        /// Follow the target until the dive commits to a spot. If it falls first (killed by someone
        /// else, taken), the meteor picks the foe nearest the spot within 3 tiles and follows that one
        /// instead, once: in a melee the crowd it leapt at was often cut down under it (r3 war: a
        /// slam that hit no one). One the god hand picks up (or that goes inside something) is taken
        /// too: the landing spot never chases the player's cursor.
        /// </summary>
        private void Track(float dt)
        {
            if (_phase >= Phase.Dive || _target == null)
            {
                return;
            }
            if (!_target.isAlive() || _target.Sid() != _targetId || _target.isInMagnet() || _target.isInsideSomething())
            {
                _target = null;
                if (_retargeted)
                {
                    return;
                }
                _retargeted = true;
                Actor best = null;
                float bestD = float.MaxValue;
                foreach (Actor u in Combat.FoesAround(A, _to, 3f * _k))
                {
                    float d = (u.current_position - _to).sqrMagnitude;
                    if (d < bestD && Combat.Allowed(A, u) && !IsDoomed(u))
                    {
                        bestD = d;
                        best = u;
                    }
                }
                if (best != null)
                {
                    _target = best;
                    _targetId = best.Sid();
                    if (Dev.On) Dev.Trace("slam " + Id + " retargets " + best.asset.id + "#" + _targetId);
                }
                return;
            }
            Vector2 next = Vector2.MoveTowards(_to, _target.current_position, 6f * _k * dt);
            // never drift onto a wall, lava or water on the way
            if (Combat.SafeSpot(next))
            {
                _to = next;
            }
        }

        /// <summary>
        /// Any of the leap near the view: its takeoff, its middle or its landing (what is drawn along
        /// the way). On the middle alone, a long leap that started or landed near the edge of the
        /// view lost its takeoff or its whole impact while that end was on screen.
        /// </summary>
        private bool Visible => Fx.OnScreen(Vector2.Lerp(_from, _to, 0.5f), 0.45f) || TakeoffVisible || LandingVisible;

        /// <summary>The takeoff spot near the view (the crouch and the launch are drawn there).</summary>
        private bool TakeoffVisible => Fx.OnScreen(_from, 0.45f);

        /// <summary>The landing spot near the view (the impact and the recovery are drawn there).</summary>
        private bool LandingVisible => Fx.OnScreen(_to, 0.45f);

        /// <summary>Is the unit's body itself on screen (Cataclysm leaves the view at the top)?</summary>
        private bool BodyVisible => Fx.OnScreen(BodyPos(), 0.1f);

        private Vector2 GroundAt(float u)
        {
            return Vector2.LerpUnclamped(_from, _to, u);
        }

        /// <summary>A radius in tiles (scaled by the unit's size) as texels of this unit's pixel art.</summary>
        private float RingPx(float tiles)
        {
            return Mathf.Max(2f, tiles * _k / Px);
        }

        // ------------------------------------------------------------- windup --

        private void Windup(float dt)
        {
            float px = Px;
            if (Enter() && TakeoffVisible)
            {
                if (Level <= 1)
                {
                    CometGather(px);
                }
                else if (Level == 2)
                {
                    Gather(px);
                }
                else
                {
                    CataclysmGather(px);
                }
                MakeGlow();
            }

            // a crouch, not a pancake: at 9 pixels tall more squash than this reads as lying down
            float sx = Level <= 1 ? 1.1f : Level == 2 ? 1.14f : 1.18f;
            float sy = Level <= 1 ? 0.85f : Level == 2 ? 0.8f : 0.76f;
            float ks = Anim.OutCubic(Mathf.Clamp01(_pt / (_tWindup * 0.625f)));
            Pose.SX = Mathf.Lerp(1f, sx, ks);
            Pose.SY = Mathf.Lerp(1f, sy, ks);
            Pose.Rot = (Level >= 3 ? -11f : -9f) * ks;
            Pose.Ground = _from;
            Pose.Height = 0f;
            // the power tremble: one pixel left, one pixel right
            Pose.Nudge = _pt > _tWindup * 0.53125f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;

            if (Level >= 3 && TakeoffVisible)
            {
                // power leaking out: tier-coloured sparks drift up around it
                _ember -= dt;
                if (_ember <= 0f && _pt > 0.08f)
                {
                    _ember = 0.04f;
                    Vector2 at = _from + new Vector2(Random.Range(-5f, 5f), Random.Range(0f, 3f)) * px;
                    Kit.Ember(at, px, Random.Range(0.35f, 0.6f), new Vector2(Random.Range(-3f, 3f), Random.Range(14f, 26f)) * px, _pal.Edge);
                }
            }

            if (!_glinted && _pt >= _tWindup * 0.6875f)
            {
                _glinted = true;
                if (TakeoffVisible)
                {
                    Color c = Level == 2 ? Core : _pal.Core;
                    float size = Level <= 1 ? 0.8f : Level == 2 ? 1f : 1.25f;
                    // the "ting" beside the head, in front: on the head it hid the face at the
                    // last beat of the crouch (r2_meteor_slam_3 0010-0013)
                    Vector2 off = new Vector2((Pose.FaceRight ? 3.5f : -3.5f) * px, Pose.PivotUp * 1.45f * px);
                    Follow(Fx.Glint(BodyPos() + off, px, 0.2f, c, size), off);
                }
            }
            if (_pt >= _tWindup)
            {
                Next(Phase.Rise);
            }
        }

        /// <summary>II: the ground answers: a ring closes in on the feet, dust is drawn in, pebbles lift.</summary>
        private void Gather(float px)
        {
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            Fx.Ring(_from, px, _tWindup, 14f, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                Fx.WithA(Charge, 0f), Fx.WithA(Charge, 0.75f), Fx.WithA(Charge, 0.9f));
            DrawDustIn(px, dust, 6, 10f);
            Pebbles(px, 5, 2f, 5f, 3f, 8f);
        }

        /// <summary>I: a grey ring and a little dust.</summary>
        private void CometGather(float px)
        {
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            Fx.Ring(_from, px, _tWindup, 10f, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(White, 0.8f));
            DrawDustIn(px, dust, 4, 8f);
            Pebbles(px, 3, 1.5f, 3f, 3f, 6f);
        }

        /// <summary>III: the ground gives under the gathered power: glowing cracks, two rings, high pebbles.</summary>
        private void CataclysmGather(float px)
        {
            WorldTile tile = A.current_tile;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            Kit.Crater(_from, px * _k, tile, true, 3.5f, true, _pal.Edge);
            Fx.Ring(_from, px, _tWindup, 20f * _k, 3f, 2, 1, FxLayer.Ground, 4, Art.Ground, 1.8f,
                Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.8f), Fx.WithA(_pal.Core, 0.95f));
            Particle inner = Fx.Ring(_from, px, _tWindup * 0.7f, 12f * _k, 2f, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.6f,
                Fx.WithA(White, 0f), Fx.WithA(White, 0.85f));
            if (inner != null)
            {
                inner.Delay = _tWindup * 0.3f;
            }
            DrawDustIn(px, dust, 8, 13f);
            Pebbles(px, 8, 3f, 7f, 3f, 10f);
        }

        private void DrawDustIn(float px, Color dust, int count, float from)
        {
            int n = Fx.Count(count);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.6f) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[0], _from + dir * from * px, px, 0.28f, dir.y < 0f ? FxLayer.Top : FxLayer.Ground, 5);
                p?.Moving(-dir * from * 3f * px).Sized(0.7f, 0.3f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>Pebbles lift off the ground around the feet (anime power-up) until the takeoff throws them.</summary>
        private void Pebbles(float px, int count, float liftMin, float liftMax, float rMin, float rMax)
        {
            int pebbles = Fx.Count(count);
            float windup = _tWindup;
            for (int i = 0; i < pebbles; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float r = Random.Range(rMin, rMax);
                Vector2 at = _from + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * Art.Ground) * px;
                Sprite chunk = Art.Chunks[Random.value < 0.5f ? 6 : Random.value < 0.5f ? 7 : 0];
                // the ones behind the unit float up behind it: drawn over it they speckled its face
                Particle p = Fx.Spawn(chunk, at, px, 10f, Mathf.Sin(a) > 0f ? FxLayer.Behind : FxLayer.Top, 2);
                if (p == null)
                {
                    continue;
                }
                p.Held = true;
                p.Snap = true;
                Color c = Random.value < 0.75f ? Fx.SoilColor(A.current_tile) : Fx.Darken(Fx.SoilColor(A.current_tile), 0.3f);
                p.Color(c);
                float lift = Random.Range(liftMin, liftMax) * px, delay = Random.Range(0f, 0.12f), wob = Random.Range(0f, 6f);
                p.Step = (q, d) =>
                {
                    if (!q.Held)
                    {
                        return;
                    }
                    float k = Mathf.Clamp01((q.Age - delay) / Mathf.Max(0.05f, windup - delay));
                    q.Z = lift * (1f - (1f - k) * (1f - k)) + Mathf.Sin(q.Age * 40f + wob) * 0.3f * q.Px * k;
                };
                _pebbles.Add(p);
            }
        }

        // --------------------------------------------------------------- rise --

        private void Rise(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Nudge = Vector2.zero;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Level >= 3)
                {
                    Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
                }
                if (TakeoffVisible)
                {
                    if (Level <= 1)
                    {
                        CometTakeoff(px);
                    }
                    else if (Level == 2)
                    {
                        Takeoff(px);
                    }
                    else
                    {
                        CataclysmTakeoff(px);
                    }
                }
                if (Level >= 3)
                {
                    // the launch blast scorches the ground it leaves (a God's blasts a crater)
                    Ravage(_from, _k * RavageScale(A), RavageForce(1, A), A);
                }
                ReleasePebbles(px);
            }
            float k = Mathf.Clamp01(_pt / _tRise);
            Pose.Height = _apex * Anim.OutCubic(k);
            Pose.Ground = GroundAt(_hang0 * Anim.OutQuad(k));
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
            float sx0 = Level <= 1 ? 0.76f : Level == 2 ? 0.68f : 0.62f;
            float sy0 = Level <= 1 ? 1.4f : Level == 2 ? 1.55f : 1.7f;
            Pose.SX = Mathf.Lerp(sx0, 1f, s);
            Pose.SY = Mathf.Lerp(sy0, 1f, s);
            Pose.Rot = Mathf.Lerp(-6f, 10f, Anim.OutQuad(k));

            if (Visible && (Level < 3 || BodyVisible))
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.034f;
                    if (Level == 2)
                    {
                        Fx.Afterimage(this, 0.16f, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.6f), _pal.GhostEnd);
                    }
                    else if (Level <= 1)
                    {
                        Fx.Afterimage(this, 0.14f, _pal.Ghost, _pal.GhostEnd);
                    }
                    else
                    {
                        Fx.Afterimage(this, 0.18f, Fx.WithA(White, 0.9f), Fx.WithA(_pal.Edge, 0.7f), _pal.GhostEnd);
                    }
                }
                _streak -= dt;
                if (_streak <= 0f && k < 0.7f)
                {
                    _streak = Level >= 3 ? 0.02f : 0.03f;
                    Vector2 at = BodyPos() + new Vector2(Random.Range(-4f, 4f) * px, -Random.Range(2f, 8f) * px);
                    Particle p = Fx.Spawn(Art.Lines[Random.Range(0, Level >= 3 ? 3 : 2)], at, px, 0.14f, FxLayer.Top, 1);
                    p?.Color(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0f));
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Hang);
            }
        }

        /// <summary>II: takeoff ring, dust, small cracks, launch streaks.</summary>
        private void Takeoff(float px)
        {
            WorldTile tile = A.current_tile;
            Color soil = Fx.SoilColor(tile);
            Color dust = Fx.Lighten(soil, 0.35f);
            Fx.Ring(_from, px, 0.24f, 3f, 20f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
            TakeoffDust(px, dust, 10, 40f, 60f);
            Decal(Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)], _from, px, soil, false, 2.6f);
            LaunchStreaks(px, 6, 30f);
        }

        /// <summary>I: a puff of dust and a thin white ring, no decal.</summary>
        private void CometTakeoff(float px)
        {
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            Fx.Ring(_from, px, 0.2f, 3f, 13f * _k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                Fx.WithA(White, 0.85f), Fx.WithA(_pal.Edge, 0f));
            TakeoffDust(px, dust, 6, 30f, 45f);
            LaunchStreaks(px, 3, 26f);
        }

        /// <summary>III: a launch blast: a white-hot ring with a tier ring behind it, a crater, a pillar of speed lines.</summary>
        private void CataclysmTakeoff(float px)
        {
            WorldTile tile = A.current_tile;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            Fx.Ring(_from, px, 0.28f, 3f, 26f * _k, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.8f,
                Fx.WithA(White, 1f), Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
            Particle ring2 = Fx.Ring(_from, px, 0.34f, 4f, 18f * _k, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f,
                Fx.WithA(_pal.Edge, 0.9f), Fx.WithA(_pal.Edge, 0f));
            if (ring2 != null)
            {
                ring2.Delay = 0.06f;
            }
            TakeoffDust(px, dust, 14, 50f, 75f);
            Kit.Crater(_from, px * _k, tile, true, 4f, true, _pal.Edge);
            LaunchStreaks(px, 10, 40f);
            Kit.SpeedLines(_from + new Vector2(0f, 6f * px), Vector2.up, px, 8, 5f, 0.22f, Fx.WithA(_pal.Core, 0.9f));
        }

        private void TakeoffDust(float px, Color dust, int count, float vMin, float vMax)
        {
            int n = Fx.Count(count);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], _from + dir * 2f * px, px, Random.Range(0.4f, 0.6f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * Random.Range(vMin, vMax) * px * _k, Random.Range(3f, 7f) * px, 4.5f)
                    .Sized(0.5f, 1.15f)
                    .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>Streaks left behind by the launch.</summary>
        private void LaunchStreaks(float px, int count, float speed)
        {
            int lines = Fx.Count(count);
            for (int i = 0; i < lines; i++)
            {
                Vector2 at = _from + new Vector2(Random.Range(-4f, 4f), Random.Range(3f, 12f)) * px;
                Particle p = Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, Random.Range(0.12f, 0.2f), FxLayer.Top, 1);
                p?.Moving(new Vector2(0f, speed * px)).Color(new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
            }
        }

        private void ReleasePebbles(float px)
        {
            foreach (Particle p in _pebbles)
            {
                if (p == null || !p.Alive)
                {
                    continue;
                }
                p.Held = false;
                p.Step = null;
                p.Life = p.Age + Random.Range(0.7f, 1f);
                Vector2 dir = (p.Pos - _from).normalized;
                if (dir == Vector2.zero)
                {
                    dir = Random.insideUnitCircle.normalized;
                }
                p.Moving(dir * Random.Range(10f, 28f) * px, Random.Range(35f, 70f) * px, 0f, 260f * px);
                p.Bounce = 0.3f;
                p.Spin = Random.Range(-600f, 600f);
                p.Snap = false;
                Color c = p.Grad != null && p.Grad.Length > 0 ? p.Grad[0] : Color.white;
                p.Color(c, c, c, Fx.WithA(c, 0f));
            }
            _pebbles.Clear();
        }

        // --------------------------------------------------------------- hang --

        private void Hang(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Nudge = Vector2.zero;
                if (Visible)
                {
                    MakeGlow();
                    if (BodyVisible || Level < 3)
                    {
                        // the "ting!" sits up and in front of the unit, so it never hides the flip
                        Vector2 off = new Vector2((Pose.FaceRight ? 4f : -4f) * px, Pose.PivotUp * 1.6f * px);
                        Color c = Level == 2 ? Core : _pal.Core;
                        float size = Level <= 1 ? 0.8f : Level == 2 ? 1.1f : 1.35f;
                        Follow(Fx.Glint(BodyPos() + off, px, Level >= 3 ? 0.3f : 0.24f, c, size), off);
                        if (Level >= 3)
                        {
                            Fx.FocusLines(BodyPos(), px, Fx.Count(8), 10f * _k, 20f * _k, 0.16f);
                        }
                    }
                    Marker(px, _tHang + _tDive);
                }
            }
            float k = Mathf.Clamp01(_pt / _tHang);
            if (k > 0.4f)
            {
                PinUnder(dt);
            }
            float bob = Level <= 1 ? 0.12f : Level == 2 ? 0.25f : 0.35f;
            if (_air)
            {
                bob = 0.3f;
            }
            Pose.Height = _apex + bob * _k * Mathf.Sin(Mathf.PI * k);
            Pose.Ground = GroundAt(Mathf.Lerp(_hang0, _hang1, k));
            if (_flips <= 0)
            {
                // Comet Hop: no flip; knees tucked, it leans back at the top, ready to drop
                float e = Anim.Smooth(0f, 1f, k);
                Pose.Rot = Mathf.Lerp(10f, -8f, e);
                Pose.SX = Mathf.Lerp(1f, 1.05f, e);
                Pose.SY = Mathf.Lerp(1f, 0.88f, e);
            }
            else
            {
                float fk = Mathf.Clamp01((k - 0.05f) / 0.85f);
                Pose.Rot = 10f + (360f * _flips - 10f) * Anim.InOutCubic(fk);
                float tuck = Mathf.Sin(Mathf.PI * fk);
                Pose.SX = 1f - (_flips >= 2 ? 0.1f : 0.08f) * tuck;
                Pose.SY = 1f - (_flips >= 2 ? 0.26f : 0.2f) * tuck;
            }

            if (Visible && (Level < 3 || BodyVisible))
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    bool spinning = _flips > 0 && k > 0.05f && k < 0.9f;
                    _ghost = spinning ? 0.03f : 0.06f;
                    if (Level == 2)
                    {
                        Fx.Afterimage(this, 0.13f, Fx.WithA(_pal.Core, 0.6f), Fx.WithA(_pal.Edge, 0.4f), _pal.GhostEnd);
                    }
                    else if (Level <= 1)
                    {
                        Fx.Afterimage(this, 0.11f, Fx.WithA(_pal.Ghost, 0.55f), _pal.GhostEnd);
                    }
                    else
                    {
                        Fx.Afterimage(this, 0.14f, Fx.WithA(White, 0.7f), Fx.WithA(_pal.Edge, 0.5f), _pal.GhostEnd);
                    }
                }
                if (Level >= 2)
                {
                    _ember -= dt;
                    if (_ember <= 0f && k > (Level >= 3 ? 0.5f : 0.3f))
                    {
                        // it ignites: sparks of fire start to leave it
                        _ember = Level >= 3 ? 0.025f : 0.03f;
                        Ember(BodyPos() + Random.insideUnitCircle * 3f * px, px, 0.45f, Random.insideUnitCircle * 12f * px);
                    }
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Next(Phase.Dive);
            }
        }

        /// <summary>
        /// The foes under the falling meteor freeze, looking up at it (Combat.Pin: no swing of their
        /// own): the unit's place on the ground is its shadow, so they swung their weapons at the
        /// empty ground under it, the game's white slashes flashing beside the crowd while the meteor
        /// was still high above (r3_meteor_slam_1 0023-0026). They can still answer the blast itself.
        /// </summary>
        private void PinUnder(float dt)
        {
            _pin -= dt;
            if (_pin > 0f)
            {
                return;
            }
            _pin = 0.1f;
            float r = MeteorSlamTech.BlastRadius(Level) * _k * _area + 1f;
            foreach (Actor u in Combat.FoesAround(A, _to, r))
            {
                Combat.Pin(u, 0.25f);
            }
        }

        /// <summary>Keeps an effect on the unit's body as it flies.</summary>
        private void Follow(Particle p, Vector2 offset)
        {
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = BodyPos() + offset;
            }
        }

        /// <summary>
        /// Where it will land: a shadow growing on the ground and a dashed ring closing on the spot
        /// (II in the tier's colour, I grey, III a flickering red warning the size of the blast).
        /// </summary>
        private void Marker(float px, float total)
        {
            if (_marked)
            {
                return;
            }
            _marked = true;
            total = Mathf.Max(0.05f, total);
            int shadowMax = ShadowRx(Level >= 3 ? 1.4f : 1.25f);
            _marker = Fx.Spawn(Art.Disk(2, 0.42f, 0.5f), _to, px, total, FxLayer.Ground, 2);
            if (_marker != null)
            {
                _marker.Held = true;
                _marker.Snap = true;
                _marker.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    q.Pos = _to;
                    Sprite s = Art.Disk(Mathf.RoundToInt(Mathf.Lerp(2f, shadowMax, k * k)), 0.42f, 0.5f);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.1f, 0.42f, k));
                };
            }
            float r0, r1;
            int dashes;
            switch (Level)
            {
                case 1:
                    r0 = Mathf.Max(8f, RingPx(MeteorSlamTech.Radius(1) * _area));
                    r1 = Mathf.Max(3f, r0 * 0.35f);
                    dashes = 8;
                    break;
                case 2:
                    // (the blast's own size, as I and III: texels already scale with the unit, so
                    // 18 * K texels drew a bear's ring half as wide again as its blast, a crab's half)
                    r0 = RingPx(MeteorSlamTech.Radius(2) * _area);
                    r1 = Mathf.Max(4f, r0 * 0.25f);
                    dashes = 10;
                    break;
                default:
                    r0 = Mathf.Min(120f, RingPx(MeteorSlamTech.Radius(3) * _area));
                    r1 = Mathf.Max(6f, r0 * 0.25f);
                    dashes = 14;
                    break;
            }
            int level = Level;
            Color tint = level >= 3 ? Warning : _pal.Edge;
            // III: the meteor is out of sight up there, so the ground has to scream: a 2-pixel ring
            // (1 pixel at 3.5 tiles read as a few faint dashes) and the whole blast zone tinted red
            int thick = level >= 3 ? 2 : 1;
            _markerRing = Fx.Spawn(Art.Ring(Mathf.RoundToInt(r0), thick, Art.Ground, dashes), _to, px, total, FxLayer.Ground, 3);
            if (_markerRing != null)
            {
                _markerRing.Held = true;
                _markerRing.Snap = true;
                _markerRing.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    q.Pos = _to;
                    Sprite s = Art.Ring(Mathf.RoundToInt(Mathf.Lerp(r0, r1, Anim.InOutCubic(k))), thick, Art.Ground, dashes);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    float alpha = Mathf.Lerp(0f, level <= 1 ? 0.6f : level == 2 ? 0.8f : 0.95f, Mathf.Clamp01(k * 3f));
                    if (level >= 3)
                    {
                        // a warning flicker, faster as it closes
                        alpha *= Flicker(q.Age, k) ? 1f : 0.5f;
                    }
                    q.R.color = Fx.WithA(tint, alpha);
                };
            }
            if (level < 3)
            {
                return;
            }
            // the danger zone: the blast's whole area, dithered at its edge, pulsing with the ring.
            // It stays the size of the blast while the ring closes on the spot (it used to shrink
            // with the ring: at the end it showed a quarter of the zone about to be hit)
            int rf = Mathf.RoundToInt(r0);
            _markerFill = Fx.Spawn(Art.Disk(rf, Art.Ground, 0.5f), _to, px, total, FxLayer.Ground, 1);
            if (_markerFill != null)
            {
                _markerFill.Held = true;
                _markerFill.Snap = true;
                _markerFill.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    q.Pos = _to;
                    q.Grad = null;
                    float alpha = Mathf.Lerp(0f, 0.2f, Mathf.Clamp01(k * 2.5f)) * (Flicker(q.Age, k) ? 1f : 0.55f);
                    q.R.color = Fx.WithA(Warning, alpha);
                };
            }
        }

        /// <summary>III's warning flicker: on/off, faster as the meteor closes in.</summary>
        private static bool Flicker(float age, float k)
        {
            return ((int)(age * Mathf.Lerp(10f, 26f, k)) & 1) == 0;
        }

        // --------------------------------------------------------------- dive --

        private void Dive(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Nudge = Vector2.zero;
                Dev.Watch(Vector2.Lerp(_from, _to, 0.5f) + new Vector2(0f, Mathf.Min(_apex, 7f) * 0.42f));
                Anim.Sound(Level >= 3 ? "event:/SFX/DESTRUCTION/FallMeteorite" : "event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                _wasSeen = Visible && BodyVisible;
                if (Visible)
                {
                    MakeGlow();
                    Marker(px, _tDive);   // an Air Meteor from low down skips the hang
                    if (_wasSeen)
                    {
                        SoundBarrier(px);
                    }
                }
                _dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(_to)), 0.4f);
                _lastTrail = BodyPos();
                _trail = true;
            }
            PinUnder(dt);
            float k = Mathf.Clamp01(_pt / _tDive);
            float e = k * k;
            Pose.Height = _apex * (1f - e);
            Pose.Ground = GroundAt(Mathf.Lerp(_hang1, 1f, e));
            float s = Mathf.Clamp01(k * 2.5f);
            float sx = Level <= 1 ? 0.76f : Level == 2 ? 0.66f : 0.6f;
            float sy = Level <= 1 ? 1.4f : Level == 2 ? 1.6f : 1.75f;
            Pose.SX = Mathf.Lerp(0.85f, sx, s);
            Pose.SY = Mathf.Lerp(1.25f, sy, s);
            float horiz = Mathf.Abs(_to.x - _from.x) * 0.34f;
            float vert = Mathf.Max(0.3f, _apex);
            Pose.Rot = Mathf.Clamp(Mathf.Atan2(horiz, vert) * Mathf.Rad2Deg * 0.8f, 8f, 35f);

            if (Level >= 3)
            {
                // the second boom, as it tears into view (or halfway down, if it never left it)
                bool seen = Visible && BodyVisible;
                if (!_boomed && seen && (!_wasSeen || k >= 0.55f))
                {
                    _boomed = true;
                    SoundBarrier(px);
                }
                _wasSeen = seen;
            }
            if (Visible)
            {
                if (Level <= 1)
                {
                    DustTrail(px);
                }
                else if (Level == 2)
                {
                    FireTrail(px);
                }
                else
                {
                    WhiteHotTrail(px);
                }
                if (BodyVisible || Level < 3)
                {
                    if (Level == 2)
                    {
                        Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(Fire3, 0f));
                    }
                    else if (Level <= 1)
                    {
                        Fx.Afterimage(this, 0.1f, Fx.WithA(_pal.Ghost, 0.7f), _pal.GhostEnd);
                    }
                    else
                    {
                        Fx.Afterimage(this, 0.13f, Fx.WithA(White, 0.95f), Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(Fire3, 0f));
                    }
                    int lines = Fx.Count(Level >= 3 ? 3 : 2);
                    Vector2 body = BodyPos();
                    Vector2 vel = body - _lastTrail;
                    if (vel.sqrMagnitude > 1e-8f)
                    {
                        float ang = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg - 90f;
                        Color c0 = Level <= 1 ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 0.97f, 0.85f, 0.75f);
                        Color c1 = Level <= 1 ? new Color(1f, 1f, 1f, 0f) : new Color(1f, 0.9f, 0.6f, 0f);
                        for (int i = 0; i < lines; i++)
                        {
                            Vector2 side = new Vector2(-vel.y, vel.x).normalized * Random.Range(-5f, 5f) * px;
                            Particle p = Fx.Spawn(Art.Lines[2], body + side - vel.normalized * Random.Range(4f, 10f) * px, px, 0.1f, FxLayer.Top, 1);
                            if (p != null)
                            {
                                p.Rot = ang;
                                p.Color(c0, c1);
                            }
                        }
                    }
                }
            }
            _lastTrail = BodyPos();
            if (k >= 1f)
            {
                Next(Phase.Impact);
            }
        }

        /// <summary>The sound barrier: a flat ring around the body (tier-tinted from II).</summary>
        private void SoundBarrier(float px)
        {
            switch (Level)
            {
                case 1:
                    Fx.Ring(BodyPos(), px, 0.16f, 3f, 10f * _k, 1, 1, FxLayer.Top, 3, 0.35f, 2.5f,
                        Fx.WithA(White, 0.8f), Fx.WithA(_pal.Edge, 0f));
                    break;
                case 2:
                    Fx.Ring(BodyPos(), px, 0.2f, 4f, 16f * _k, 2, 1, FxLayer.Top, 3, 0.35f, 2.5f,
                        Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0f));
                    break;
                default:
                    Fx.Ring(BodyPos(), px, 0.22f, 4f, 22f * _k, 3, 1, FxLayer.Top, 3, 0.35f, 2.6f,
                        Fx.WithA(White, 1f), Fx.WithA(_pal.Core, 0.8f), Fx.WithA(_pal.Edge, 0f));
                    break;
            }
        }

        /// <summary>II: fire and smoke along the path since last frame, evenly spaced so it reads as one stream.</summary>
        private void FireTrail(float px)
        {
            Vector2 now = BodyPos();
            if (!_trail)
            {
                return;
            }
            float step = 1.6f * px / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 40);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle core = Fx.Spawn(Art.Puffs[0], at, px, 0.09f, FxLayer.Behind, 3);
                core?.Sized(0.7f, 0.35f).Color(Core, Fx.WithA(Fire1, 0.7f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.15f, 0.22f), FxLayer.Behind, 2);
                flame?.Moving(Random.insideUnitCircle * 5f * px, 6f * px, 2f)
                    .Sized(0.75f, 0.45f)
                    .Color(Fx.WithA(Fire1, 0.95f), Fx.WithA(Fire2, 0.85f), Fx.WithA(Fire3, 0.45f), Fx.WithA(Fire3, 0f));
                if (Random.value < 0.18f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at + Random.insideUnitCircle * 1.5f * px, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0);
                    smoke?.Moving(Random.insideUnitCircle * 4f * px, 8f * px, 1.5f)
                        .Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.32f), Fx.WithA(Smoke, 0f));
                    if (smoke != null)
                    {
                        smoke.Delay = 0.06f;
                    }
                }
            }
        }

        /// <summary>
        /// III: a white-hot head and a wide trail that cools as it ages: white core, then yellow,
        /// orange and red, then smoke that lingers. Only the part of the path on screen is drawn.
        /// </summary>
        private void WhiteHotTrail(float px)
        {
            Vector2 now = BodyPos();
            if (!_trail)
            {
                return;
            }
            float step = 2.2f * px / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 40);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                if (!Fx.OnScreen(at, 0.05f))
                {
                    continue;
                }
                Particle core = Fx.Spawn(Art.Puffs[1], at, px, 0.1f, FxLayer.Behind, 4);
                core?.Sized(0.8f, 0.4f).Color(White, Fx.WithA(Core, 0.9f), Fx.WithA(Fire1, 0.5f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(2, 4)], at + Random.insideUnitCircle * 1.8f * px, px, Random.Range(0.26f, 0.34f), FxLayer.Behind, 2);
                flame?.Moving(Random.insideUnitCircle * 6f * px, 7f * px, 2f)
                    .Sized(0.85f, 0.5f)
                    .Color(Fx.WithA(White, 0.95f), Fx.WithA(Fire1, 0.95f), Fx.WithA(Fire2, 0.85f), Fx.WithA(Fire3, 0.6f), Fx.WithA(Smoke, 0f));
                if (Random.value < 0.25f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(3, 6)], at + Random.insideUnitCircle * 2f * px, px, Random.Range(0.7f, 1f), FxLayer.Behind, 0);
                    smoke?.Moving(Random.insideUnitCircle * 4f * px, 10f * px, 1.2f)
                        .Sized(0.6f, 1.25f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.38f), Fx.WithA(Smoke, 0.2f), Fx.WithA(Smoke, 0f));
                    if (smoke != null)
                    {
                        smoke.Delay = 0.08f;
                    }
                }
            }
        }

        /// <summary>I: no fire: a stream of dust puffs and white streaks behind the falling unit.</summary>
        private void DustTrail(float px)
        {
            Vector2 now = BodyPos();
            if (!_trail)
            {
                return;
            }
            float step = 2.6f * px / Fx.Detail();
            float dist = Vector2.Distance(_lastTrail, now);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 24);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle puff = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, Random.Range(0.25f, 0.4f), FxLayer.Behind, 2);
                puff?.Moving(Random.insideUnitCircle * 4f * px, 3f * px, 2f)
                    .Sized(0.5f, 1f)
                    .Color(Fx.WithA(White, 0.7f), Fx.WithA(_dust, 0.5f), Fx.WithA(_dust, 0f));
            }
        }

        // ------------------------------------------------------------- impact --

        private void Impact(float dt)
        {
            if (Enter())
            {
                Landed++;
                Pose.Ground = _to;
                Pose.Height = 0f;
                Pose.SX = Level <= 1 ? 1.24f : Level == 2 ? 1.32f : 1.38f;
                Pose.SY = Level <= 1 ? 0.74f : Level == 2 ? 0.66f : 0.62f;
                Pose.Rot = Level <= 1 ? 6f : 8f;
                KillHeld();
                Crash();
            }
            _since += dt;
            ProcessWave(false);
            ProcessRifts(false);
            Pose.Nudge = new Vector2(0f, ((int)(_pt * 60f) & 1) == 0 ? 0.5f : 0f);
            if (_pt >= _tImpact)
            {
                Pose.Nudge = Vector2.zero;
                LaunchVictims();
                Next(Phase.Recover);
            }
        }

        private void Crash()
        {
            float px = Px;
            Vector2 at = _to;
            WorldTile tile = Combat.TileAt(at) ?? A.current_tile;
            // the blows first: the impact frame shows whether one was critical
            Hurt(at, tile);
            bool visible = LandingVisible;
            switch (Level)
            {
                case 1:
                    if (visible)
                    {
                        CometFx(at, px, tile);
                    }
                    Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
                    Anim.Sound("event:/SFX/HIT/HitStone", tile);
                    break;
                case 2:
                    if (visible)
                    {
                        // (no world freeze at II: that is III's. The Impact phase is II's own
                        // local hit-stop: the crushed pose held, the victims held until they fly)
                        ImpactFx(at, px, tile);
                        Shake(at);
                    }
                    Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
                    break;
                default:
                    if (visible)
                    {
                        CataclysmFx(at, px, tile);
                        Kit.Shake(at, 0.5f, 0.02f, _k);
                        Kit.Stop(at, 0.12f);
                    }
                    Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
                    Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile);
                    if (tile != null && tile.Type != null && !tile.Type.liquid)
                    {
                        Smoulder(at, px, _k, _pal.Edge, 20f);
                    }
                    break;
            }
            // and the world takes it, on the same frame (after the effects: their rocks and clods
            // take the colours of the ground as it was)
            Devastate(at);
        }

        // ---------------------------------------------------- the world takes it --

        /// <summary>
        /// Real damage to the ground where it lands (Kit.Ravage): I none (a heavy performer
        /// scorches it), II a crater (force 2, r 2), III a burning one (force 3, r 3.5); a God one
        /// force harder over 1.5x the radius. Grandmasters and Gods break the land beyond it too.
        /// Mechanics: it happens on or off screen.
        /// </summary>
        private void Devastate(Vector2 at)
        {
            int force;
            float r;
            switch (Level)
            {
                case 1:
                    force = _k >= 1.4f ? 1 : 0;
                    r = 1.2f;
                    break;
                case 2:
                    force = 2;
                    r = 2f;
                    break;
                default:
                    force = 3;
                    r = 3.5f;
                    break;
            }
            force = RavageForce(force, A);
            r *= _k * RavageScale(A);
            // (the fire takes only the blast's rim, never the crater's middle where the slammer lands)
            Ravage(at, r, force, A);
            if (Level >= 3)
            {
                PlanRifts(at, r);
            }
            if (Dev.On && force > 0)
            {
                Dev.Trace("slam " + Id + " ravage force " + force + " r " + r.ToString("F1") + (_rifts.Count > 0 ? " + " + _rifts.Count + " rift points" : ""));
            }
        }

        /// <summary>The world damage a performer of this tier does: a God hits one force harder (max 4).</summary>
        internal static int RavageForce(int force, Actor a)
        {
            return Tiers.Of(a) == Tier.God ? Mathf.Min(4, force + 1) : force;
        }

        /// <summary>...over a wider area: a Grandmaster 1.2x, a God 1.5x.</summary>
        internal static float RavageScale(Actor a)
        {
            Tier t = Tiers.Of(a);
            return t == Tier.God ? 1.5f : t == Tier.Grandmaster ? 1.2f : 1f;
        }

        /// <summary>
        /// Kit.Ravage (its fire takes only the rim of the blast, so a slammer never burns in its own
        /// crater). 'keep' / 'keepR': a spot the performer is about to stand on, left whole: neither
        /// dug nor set alight (never the middle of a crater that has to be dug).
        /// </summary>
        internal static void Ravage(Vector2 at, float radius, int force, Actor by, Vector2? keep = null, float keepR = 0f)
        {
            if (force <= 0)
            {
                return;
            }
            Kit.Ravage(at, radius, force, by, true, keepR > 0f ? keep : null, keepR);
        }

        /// <summary>
        /// A Grandmaster's or a God's Cataclysm breaks the land itself: fissures run out from the
        /// crater as the ground wave passes (God: 4, real craters along them, glowing near the
        /// crater; Grandmaster: 3 scorched cracks), ending at water, a cliff or the map's edge.
        /// </summary>
        private void PlanRifts(Vector2 at, float craterR)
        {
            int rays, force;
            float reach;
            if (_tier == Tier.God)
            {
                rays = 4;
                force = 2;
                reach = 1.4f;
            }
            else if (_tier == Tier.Grandmaster)
            {
                rays = 3;
                force = 1;
                reach = 1.15f;
            }
            else
            {
                return;
            }
            float d0 = Mathf.Max(1.5f, craterR * 0.7f);
            float d1 = OuterRadius * _area * _k * reach;
            if (d1 < d0 + 1f)
            {
                return;
            }
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < rays; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / rays + Random.Range(-0.35f, 0.35f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 side = new Vector2(-dir.y, dir.x);
                float wob = 0f;
                for (float d = d0; d <= d1; d += 1.1f)
                {
                    // a crack, not a ruler line: it wanders a little from side to side
                    wob = Mathf.Clamp(wob + Random.Range(-0.4f, 0.4f), -0.9f, 0.9f);
                    Vector2 p = at + dir * d + side * wob;
                    WorldTile t = Combat.TileAt(p);
                    if (t == null || t.Type == null || t.Type.liquid || t.Type.block || p.x < 1f || p.y < 1f
                        || p.x > MapBox.width - 2 || p.y > MapBox.height - 2)
                    {
                        break;
                    }
                    float f = (d - d0) / (d1 - d0);
                    _rifts.Add(new Rift
                    {
                        At = p,
                        When = 0.03f + 0.3f * f,
                        Force = f > 0.7f ? Mathf.Max(1, force - 1) : force,
                        Hot = f < 0.45f
                    });
                }
            }
        }

        /// <summary>The land breaks along the fissures as the wave reaches each point (all of it when 'all').</summary>
        private void ProcessRifts(bool all)
        {
            if (_rifts.Count == 0)
            {
                return;
            }
            float px = Px;
            for (int i = _rifts.Count - 1; i >= 0; i--)
            {
                Rift r = _rifts[i];
                if (!all && r.When > _since)
                {
                    continue;
                }
                _rifts.RemoveAt(i);
                Kit.Ravage(r.At, 1f, r.Force, A);
                if (Kit.Visible(r.At))
                {
                    WorldTile t = Combat.TileAt(r.At);
                    Kit.Crater(r.At, px * _k, t, r.Hot, r.Hot ? 9f : 6f, true, _pal.Edge);
                    if (r.Hot || (i & 1) == 0)
                    {
                        // (a puff where the ground splits; every other one further out: ~24 points)
                        Fx.LandingPuff(r.At, px, t);
                    }
                }
            }
        }

        /// <summary>II: the full meteor impact.</summary>
        private void ImpactFx(Vector2 at, float px, WorldTile tile)
        {
            bool water = tile != null && tile.Type.liquid;
            Color soil = Fx.SoilColor(tile);
            Color dust = Fx.Lighten(soil, 0.45f);
            Color top = Fx.TopColor(tile);
            float k = _k * _area;

            // flash + starburst (the "impact frame"; inverted into a Black Flash on a critical blow),
            // BEHIND the bodies: the crushed landing (a flat silhouette for that instant, see
            // GlowColor) and the victims read against it. Drawn on top, it swallowed the key pose
            // for the whole freeze (r2_meteor_slam_2 0035-0037)
            _framed = ImpactFrame(at + new Vector2(0f, 3f * px), px * k, 1f, _pal, _crit);

            // crater and fissures, glowing hot and cooling
            if (!water)
            {
                Decal(Art.Cracks[Random.Range(0, Art.Cracks.Length)], at, px * k, soil, true, 5.5f);
            }
            else
            {
                Kit.Splash(at, px, tile, 1.2f * k);
            }

            // double shockwave: a fast bright ring, a slower one behind it, and the dust wall
            Fx.Ring(at, px, 0.42f, 6f, 62f * k, 3, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                new Color(1f, 1f, 1f, 1f), new Color(0.92f, 0.96f, 1f, 0.7f), new Color(1f, 1f, 1f, 0f));
            Particle ring2 = Fx.Ring(at, px, 0.4f, 10f, 36f * k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                new Color(1f, 1f, 1f, 0.7f), new Color(1f, 1f, 1f, 0f));
            if (ring2 != null)
            {
                ring2.Delay = 0.07f;
            }
            Fx.Ring(at, px, 0.75f, 4f, 44f * k, 3, 2, FxLayer.Ground, 5, Art.Ground, 2f,
                Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));

            // the dust wall rolling out
            int puffs = Fx.Count(24);
            for (int i = 0; i < puffs; i++)
            {
                float a = (i + Random.Range(-0.35f, 0.35f)) / puffs * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + dir * 3f * px, px, Random.Range(0.7f, 1f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * Random.Range(70f, 110f) * px * k, Random.Range(4f, 10f) * px, 2.4f)
                    .Sized(0.45f, 1.3f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.55f), Fx.WithA(Fx.Lighten(dust, 0.15f), 0.25f), Fx.WithA(Fx.Lighten(dust, 0.2f), 0f));
            }
            // a column of dust over the crater, rising behind the unit (in front of it, it hid the landing)
            int column = Fx.Count(2);
            for (int i = 0; i < column; i++)
            {
                Particle p = Fx.Spawn(Art.Dust[Random.Range(3, 6)], at + new Vector2(Random.Range(-3f, 3f), Random.Range(0f, 2f)) * px, px, Random.Range(1f, 1.3f), FxLayer.Behind, 6);
                p?.Moving(new Vector2(Random.Range(-4f, 4f), 0f) * px, Random.Range(10f, 18f) * px * k, 1.6f)
                    .Sized(0.6f, 1.05f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }

            // rocks (and clods of grass) thrown out of the crater's rim
            if (!water)
            {
                Rocks(at, px, soil, top, 12, k, 1f);
            }

            // sparks
            int sparks = Fx.Count(12);
            for (int i = 0; i < sparks; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at + new Vector2(0f, 2f * px), px, Random.Range(0.2f, 0.3f), FxLayer.Top, 8);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(110f, 170f) * px * k, Random.Range(10f, 70f) * px, 6f, 60f * px);
                p.FaceVel = true;
                p.Color(White, new Color(1f, 0.9f, 0.4f, 1f), new Color(1f, 0.5f, 0.1f, 0f));
            }

            // embers drifting up from the crater's rim (not out of the unit standing in it)
            int embers = Fx.Count(10);
            for (int i = 0; i < embers; i++)
            {
                Ember(at + RimOffset(3f * k, 7f * k) * px, px, Random.Range(0.8f, 1.5f), new Vector2(Random.Range(-8f, 8f), Random.Range(15f, 35f)) * px);
            }
        }

        /// <summary>A point around the crater's rim, left or right of the unit that stands in its middle (texels).</summary>
        private static Vector2 RimOffset(float min, float max)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            return new Vector2(side * Random.Range(min, max), Random.Range(-2f, 2.5f));
        }

        /// <summary>
        /// The impact frame (flash disk + starburst; a Black Flash on a critical blow) behind the
        /// bodies: the units in it read as silhouettes against it instead of vanishing under it.
        /// Kit.ImpactFrame's own orders (29-31) would sort it in front of the units on that layer.
        /// </summary>
        internal static bool ImpactFrame(Vector2 at, float px, float size, Palette pal, bool black)
        {
            if (!Kit.Visible(at))
            {
                return false;
            }
            Color core = black ? new Color(0.05f, 0.02f, 0.04f, 1f) : Color.white;
            Color rim = black ? pal.Edge : new Color(1f, 0.96f, 0.75f, 0.9f);
            Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(15 * size), 0.85f, 0.35f), at, px, 0.09f, FxLayer.Behind, 8);
            flash?.Sized(0.8f, 1.25f).Color(black ? Fx.WithA(pal.Edge, 1f) : Color.white, Fx.WithA(rim, 0.8f), Fx.WithA(rim, 0f));
            if (black)
            {
                Particle ring = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * Mathf.Max(0.3f, size) * 1.25f, 0.09f, FxLayer.Behind, 9);
                if (ring != null)
                {
                    ring.Rot = Random.Range(0f, 360f);
                    ring.Sized(1f, 1.3f).Color(pal.Edge, Fx.WithA(pal.Edge, 0.7f), Fx.WithA(pal.Edge, 0f));
                }
            }
            Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * Mathf.Max(0.3f, size), 0.07f + 0.02f * size, FxLayer.Behind, 10);
            if (burst != null)
            {
                burst.Rot = Random.Range(0f, 360f);
                burst.Sized(1f, 1.2f).Color(core, Fx.WithA(core, 0.9f), Fx.WithA(rim, 0f));
            }
            return true;
        }

        /// <summary>
        /// Rocks (and clods of grass) thrown out of the crater's rim, never up through the unit
        /// standing in it (thrown from its feet, they flew across the landing pose): the far ones
        /// behind the units, the near ones in front, none straight at the camera over the feet.
        /// </summary>
        internal static void Rocks(Vector2 at, float px, Color soil, Color top, int count, float k, float speed)
        {
            int n = Fx.Count(count);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                if (Mathf.Sin(a) < -0.8f)
                {
                    // not straight down-screen: that one rose right over the feet
                    a = Mathf.PI * (Random.value < 0.5f ? -0.15f : -0.85f);
                }
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                float roll = Random.value;
                Color c = roll < 0.6f ? soil : roll < 0.85f ? Fx.Darken(soil, 0.3f) : top;
                Vector2 from = at + dir * Random.Range(5f, 7f) * px * k;
                Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], from, px, Random.Range(0.8f, 1.3f),
                    dir.y > 0.05f ? FxLayer.Behind : FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(20f, 55f) * px * k * speed, Random.Range(50f, 100f) * px * k * speed, 0f, 320f * px * k);
                p.Bounce = 0.3f;
                p.Spin = Random.Range(-720f, 720f);
                p.Color(c, c, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>
        /// I: a small flash, a 1.4-tile dust ring, a puff, a few rocks. No fire and no decal (the
        /// level grammar: a crater is a II, a glowing one a III), so nothing is left on the ground.
        /// </summary>
        private void CometFx(Vector2 at, float px, WorldTile tile)
        {
            bool water = tile != null && tile.Type != null && tile.Type.liquid;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.4f);
            // the little flash behind the landing, not over it
            Particle flash = Fx.Spawn(Art.Disk(8, 0.85f, 0.35f), at + new Vector2(0f, 2f * px), px * _k, 0.07f, FxLayer.Behind, 8);
            flash?.Sized(0.8f, 1.15f).Color(White, Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
            if (water)
            {
                Kit.Splash(at, px, tile, 0.6f * _k);
                return;
            }
            Fx.LandingPuff(at, px, tile);
            float r = RingPx(MeteorSlamTech.Radius(1) * _area);
            Fx.Ring(at, px, 0.28f, 4f, r, 1, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                Fx.WithA(White, 0.9f), Fx.WithA(_pal.Edge, 0f));
            Fx.Ring(at, px, 0.55f, 3f, r, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f,
                Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            // dust kicked out to the edge of the blast (drag 4: it travels speed / 4)
            Kit.DustRing(at, px, tile, 14, MeteorSlamTech.Radius(1) * _area * 4.2f / px, 0.5f, _k);
            Kit.Debris(at, px, tile, 6, _k * 0.7f);
        }

        /// <summary>
        /// III: the impact frame, a 1.5-tile crater glowing with magma in the tier's colour and the
        /// ground broken around it, shockwave rings at 2 and 3.5 tiles, a dust wall out to 5 tiles,
        /// a column of smoke, rocks, sparks and embers.
        /// </summary>
        private void CataclysmFx(Vector2 at, float px, WorldTile tile)
        {
            bool water = tile != null && tile.Type != null && tile.Type.liquid;
            Color soil = Fx.SoilColor(tile);
            Color dust = Fx.Lighten(soil, 0.45f);
            float k = _k;

            // the impact frame behind the bodies: the superhero landing is a silhouette against it
            _framed = ImpactFrame(at + new Vector2(0f, 3f * px), px * k, 1.35f, _pal, _crit);
            // the blast lights the ground up for a frame: white, then the tier's colour (on the
            // ground, under the units: over them it washed the landing out for the whole freeze)
            Particle glare = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(RingPx(1.6f * _area)), 6, 140), Art.Ground, 0.45f), at, px, 0.13f, FxLayer.Ground, 4);
            glare?.Sized(0.9f, 1.1f).Color(Fx.WithA(White, 0.8f), Fx.WithA(_pal.Edge, 0.5f), Fx.WithA(_pal.Edge, 0f));

            // the crater, its magma cracks glowing in the tier's colour, and the ground broken around it
            Kit.Crater(at, px * k, tile, true, 20f, false, _pal.Edge);
            if (!water)
            {
                float a0 = Random.value * Mathf.PI * 2f;
                for (int i = 0; i < 3; i++)
                {
                    float a = a0 + i * Mathf.PI * 2f / 3f + Random.Range(-0.4f, 0.4f);
                    Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Random.Range(1.6f, 2.2f) * k;
                    WorldTile t = Combat.TileAt(at + off);
                    if (t != null && t.Type != null && !t.Type.liquid && !t.Type.block)
                    {
                        Kit.Crater(at + off, px * k, t, false, 20f, true);
                    }
                }
            }
            else
            {
                Kit.Splash(at, px, tile, 1.6f * k);
            }

            // shockwave rings: a white-hot one to 2 tiles, the tier's one to 3.5 (the launch zone)
            Fx.Ring(at, px, 0.3f, 6f, RingPx(2f * _area), 3, 1, FxLayer.Ground, 7, Art.Ground, 3f,
                Fx.WithA(White, 1f), Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
            Particle ring2 = Fx.Ring(at, px, 0.42f, 8f, RingPx(MeteorSlamTech.Radius(3) * _area), 2, 1, FxLayer.Ground, 6, Art.Ground, 2.6f,
                Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
            if (ring2 != null)
            {
                ring2.Delay = 0.08f;
            }
            // and the ground wave that knocks the outer ring down, out to 5 tiles
            Fx.Ring(at, px, WaveLife, WaveR0, RingPx(OuterRadius * _area), 3, 2, FxLayer.Ground, 5, Art.Ground, WaveEase,
                Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));

            // the dust wall rolling out to the edge of the blast (drag 2.2 over ~1 s: it travels ~0.4 x its speed)
            int puffs = Fx.Count(28);
            float reach = OuterRadius * _area * k / 0.4f;
            for (int i = 0; i < puffs; i++)
            {
                float a = (i + Random.Range(-0.35f, 0.35f)) / puffs * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + dir * 4f * px, px, Random.Range(0.85f, 1.1f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * reach * Random.Range(0.85f, 1.1f), Random.Range(4f, 10f) * px, 2.2f)
                    .Sized(0.5f, 1.45f)
                    .Color(Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.6f), Fx.WithA(Fx.Lighten(dust, 0.15f), 0.25f), Fx.WithA(Fx.Lighten(dust, 0.2f), 0f));
            }
            // a column of smoke and dust over the crater, behind the unit: the superhero landing
            // in front of it is the payoff and must read (drawn on top, the smoke swallowed it)
            int column = Fx.Count(4);
            for (int i = 0; i < column; i++)
            {
                Color c = i % 2 == 0 ? dust : Smoke;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(3, 6)], at + new Vector2(Random.Range(-4f, 4f), Random.Range(0f, 2f)) * px, px, Random.Range(1.2f, 1.6f), FxLayer.Behind, 6);
                p?.Moving(new Vector2(Random.Range(-4f, 4f), 0f) * px, Random.Range(14f, 24f) * px * k, 1.4f)
                    .Sized(0.7f, 1.25f)
                    .Color(Fx.WithA(c, 0.85f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
            }

            if (!water)
            {
                Rocks(at, px, soil, Fx.TopColor(tile), 16, k, 1.2f);
            }

            // sparks: white-hot, cooling to fire
            int sparks = Fx.Count(16);
            for (int i = 0; i < sparks; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at + new Vector2(0f, 2f * px), px, Random.Range(0.24f, 0.36f), FxLayer.Top, 8);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(130f, 200f) * px * k, Random.Range(20f, 90f) * px, 5f, 60f * px);
                p.FaceVel = true;
                p.Color(White, Fx.WithA(Fire1, 1f), Fx.WithA(Fire2, 0f));
            }

            // embers in the tier's colour, from the rim around the unit
            int embers = Fx.Count(16);
            for (int i = 0; i < embers; i++)
            {
                Kit.Ember(at + RimOffset(3.5f * k, 10f * k) * px, px, Random.Range(0.9f, 1.8f), new Vector2(Random.Range(-10f, 10f), Random.Range(18f, 40f)) * px, _pal.Edge);
            }
        }

        /// <summary>
        /// The crater smoulders after the slam is over: embers keep rising from it for 'seconds',
        /// fewer and fewer as it cools, with a wisp of smoke now and then. An invisible emitter
        /// particle (game time: it pauses with the game; cleared with the world).
        /// </summary>
        private static void Smoulder(Vector2 at, float px, float k, Color tint, float seconds)
        {
            Particle e = Fx.Spawn(Art.Pixel, at, px, seconds, FxLayer.Ground, 0);
            if (e == null)
            {
                return;
            }
            e.Color(new Color(0f, 0f, 0f, 0f));
            float next = 0.3f;
            e.Step = (q, d) =>
            {
                next -= d;
                if (next > 0f)
                {
                    return;
                }
                float age = q.Age;
                float heat = 1f - Mathf.Clamp01(age / seconds);
                next = 0.06f + 0.06f * age;
                if (!Kit.Visible(at, 0.1f))
                {
                    return;
                }
                Vector2 off = Random.insideUnitCircle * 7f * px * k;
                off.y *= Art.Ground;
                Color c = Color.Lerp(Fire3, tint, 0.3f + 0.5f * heat);
                Kit.Ember(at + off, px, Random.Range(0.7f, 1.3f), new Vector2(Random.Range(-3f, 3f), Random.Range(8f, 18f)) * px, c);
                if (Random.value < 0.18f)
                {
                    // behind whoever stands in the crater (the slammer's landing pose, a later fight)
                    Particle w = Fx.Spawn(Art.Dust[Random.Range(1, 4)], at + off, px, Random.Range(1f, 1.4f), FxLayer.Behind, 5);
                    w?.Moving(new Vector2(Random.Range(-2f, 2f), 0f) * px, Random.Range(6f, 10f) * px, 0.8f)
                        .Sized(0.5f, 1.2f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.22f * (0.4f + 0.6f * heat)), Fx.WithA(Smoke, 0f));
                }
            };
        }

        /// <summary>Crater/fissure decal in three layers: dark, light, and (big ones) a glowing core that cools.</summary>
        private static void Decal(Art.CrackSet set, Vector2 at, float px, Color soil, bool hot, float life)
        {
            float flip = Random.value < 0.5f ? -1f : 1f;
            Color dark = Fx.Darken(soil, 0.6f);
            Color light = Fx.Lighten(soil, 0.35f);
            Particle d = Fx.Spawn(set.Dark, at, px, life, FxLayer.Ground, 1);
            if (d != null)
            {
                d.Snap = true;
                d.SX = flip;
                d.Color(Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0f));
            }
            Particle l = Fx.Spawn(set.Light, at, px, life, FxLayer.Ground, 2);
            if (l != null)
            {
                l.Snap = true;
                l.SX = flip;
                l.Color(Fx.WithA(light, 0.9f), Fx.WithA(light, 0.9f), Fx.WithA(light, 0.8f), Fx.WithA(light, 0f));
            }
            if (!hot || set.Glow == null)
            {
                return;
            }
            Particle g = Fx.Spawn(set.Glow, at, px, 1.9f, FxLayer.Ground, 3);
            if (g != null)
            {
                g.Snap = true;
                g.SX = flip;
                g.Color(new Color(1f, 0.97f, 0.7f, 1f), new Color(1f, 0.62f, 0.18f, 1f), new Color(0.85f, 0.22f, 0.06f, 0.8f),
                    new Color(0.4f, 0.06f, 0.03f, 0.35f), new Color(0.2f, 0.03f, 0.02f, 0f));
            }
        }

        private static void Ember(Vector2 at, float px, float life, Vector2 vel)
        {
            Particle p = Fx.Spawn(Art.Pixel, at, px, life, FxLayer.Top, 9);
            p?.Moving(vel, 0f, 1.2f).Color(new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.5f, 0.12f, 1f), new Color(0.6f, 0.1f, 0.05f, 0f));
        }

        private void Shake(Vector2 at)
        {
            Camera cam = World.world.camera;
            if (cam == null || !Fx.OnScreen(at, 0.05f))
            {
                return;
            }
            float size = cam.orthographicSize;
            if (size > 90f)
            {
                return;
            }
            World.world.startShake(0.32f, 0.02f, size * 0.012f * _k, true, true);
        }

        /// <summary>
        /// Everyone hostile in the blast takes the hit now and flies when the hit-stop ends; at III the
        /// outer ring (3.5-5 tiles) is knocked down as the shockwave reaches it. An area blow can't
        /// be parried: a target that answers it in any way (an evade, a duel deflection) is skipped.
        /// </summary>
        private void Hurt(Vector2 at, WorldTile tile)
        {
            Actor a = A;
            if (tile == null || a.kingdom == null)
            {
                return;
            }
            float radius = MeteorSlamTech.Radius(Level) * _k * _area;
            float blast = MeteorSlamTech.BlastRadius(Level) * _k * _area;
            List<Actor> hit = Combat.FoesAround(a, at, blast);
            int landed = 0;
            foreach (Actor u in hit)
            {
                float d = Vector2.Distance(u.current_position, at);
                if (d > radius + u.stats["size"] * 0.5f)
                {
                    // Cataclysm's outer ring: the wave gets there a moment later
                    _wave.Add(new Wave { U = u, Id = u.Sid(), At = WaveTime(d) });
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (!Combat.Allowed(a, u, out float classMult))
                {
                    // too heavy for this tier: a glancing blow, no launch
                    Combat.Hit(a, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    landed++;
                    Primary(u, d);
                    continue;
                }
                float k = 1f - Mathf.Clamp01(d / radius);
                HitResult r;
                Launch l = new Launch { U = u, Id = u.Sid() };
                // (the stuns below never ask for the game's white status flash: the blow's own
                // red flash is the hit; a white one could pop up on its own a beat later.)
                // The blows never kill on the spot (lethal: false): one that would have leaves its
                // victim at the edge of death, thrown like the rest, and the fall finishes it where
                // it lands. Killed outright, a crowd dropped dead in a heap on the landing spot, over
                // the superhero landing, and nothing flew (r3_meteor_slam_3 0049-0061: 8 of 9 dead
                // where they stood; the God's 3g: a charred pile in the crater)
                switch (Level)
                {
                    case 1:
                        // Comet Hop: knocked off their feet, not thrown
                        r = Combat.Hit(a, u, Mathf.Lerp(1.1f, 1.5f, k) * classMult, false, true, 2f, this);
                        if (k > 0.5f && u.isAlive())
                        {
                            u.addStatusEffect("stunned", 0.6f, false);
                        }
                        l.Distance = Mathf.Min(1f, Mathf.Lerp(0.5f, 1f, k) * _k);
                        l.Apex = Mathf.Lerp(0.4f, 0.6f, k);
                        l.Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(260f, 420f);
                        break;
                    case 2:
                        // (decisively harder than Comet Hop's 1.1-1.5, well short of Cataclysm's 4-5)
                        r = Combat.Hit(a, u, Mathf.Lerp(1.5f, 2.6f, k) * classMult, false, true, 4f, this);
                        if (k > 0.45f && u.isAlive())
                        {
                            u.addStatusEffect("stunned", 1.2f, false);
                        }
                        l.Distance = Mathf.Lerp(1.5f, 3.4f, k) * _k;
                        l.Apex = Mathf.Lerp(1.2f, 2.6f, k) * _k;
                        l.Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(540f, 900f);
                        break;
                    default:
                        // Cataclysm: the inner blast throws them high
                        r = Combat.Hit(a, u, Mathf.Lerp(4f, 5f, k) * classMult, false, true, 6f, this);
                        if (k > 0.35f && u.isAlive())
                        {
                            u.addStatusEffect("stunned", 1.6f, false);
                        }
                        l.Distance = Mathf.Min(4f, Mathf.Lerp(2.5f, 4f, k) * _k);
                        l.Apex = Mathf.Min(3f, Mathf.Lerp(2.2f, 3f, k) * _k);
                        l.Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(720f, 1080f);
                        break;
                }
                l.Doomed = AtDeathsDoor(u);
                landed++;
                Primary(u, d);
                if (r.Crit)
                {
                    _crit = true;
                }
                // a hit star on each one caught by the blast, in the mod's own pixel language (the
                // game's big yellow "!" read as a critical hit on everyone near the middle, and sat
                // right on the landing pose); the ones under the meteor are inside the impact flash
                if (u.is_visible && d > 0.6f * _k && Fx.OnScreen(u.current_position, 0.05f))
                {
                    float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
                    Vector2 hp = u.current_position + new Vector2(0f, u.position_height + 4f * vpx);
                    Vector2 away = (u.current_position - at).normalized + Vector2.up * 0.5f;
                    Fx.HitSpark(hp, vpx, r.Crit ? 1 : 0, away, r.Crit ? 4 : 2);
                }
                _launches.Add(l);
            }
            if (Dev.On)
            {
                int doomed = 0;
                foreach (Launch l in _launches)
                {
                    if (l.Doomed)
                    {
                        doomed++;
                    }
                }
                Dev.Trace("slam " + Id + " " + Techniques.Roman(Level) + " hits " + landed + " (radius " + radius.ToString("F1") + ")"
                    + (doomed > 0 ? ", " + doomed + " thrown to their deaths" : "")
                    + (_wave.Count > 0 ? " + " + _wave.Count + " in the outer ring" : "") + (_crit ? " CRIT" : ""));
            }
            _landed = landed;
        }

        /// <summary>The one to chain onto when only one was caught: the target it went for, else the one nearest the middle.</summary>
        private void Primary(Actor u, float d)
        {
            if (_primary != null && _primary == _target)
            {
                return;
            }
            if (u == _target || d < _primaryD)
            {
                _primary = u;
                _primaryId = u.Sid();
                _primaryD = d;
            }
        }

        /// <summary>
        /// The chain hook the slam leaves (read by the Brain when it ends), so the next technique
        /// follows straight on instead of a pause: a crowd blown apart (Scattered), the one it
        /// caught still in the air (Launched) or stunned on the ground (Down), or at least a foe
        /// standing right there (Close).
        /// </summary>
        private void Chain()
        {
            EndLink = Link.None;
            EndTarget = null;
            Actor a = A;
            // (not one flying to its death: the fall finishes it, nothing need chase it)
            Actor p = _primary != null && _primary.isAlive() && _primary.Sid() == _primaryId && Combat.IsFoe(a, _primary)
                && !IsDoomed(_primary) ? _primary : null;
            if (_landed + _waveHits >= 2)
            {
                EndLink = Link.Scattered;
                EndTarget = p;
            }
            else if (p != null && !p.isFlying() && Combat.HeightOf(p) > 0.25f)
            {
                EndLink = Link.Launched;
                EndTarget = p;
            }
            else if (p != null && Combat.Grounded(p) && p.hasStatus("stunned"))
            {
                EndLink = Link.Down;
                EndTarget = p;
            }
            else
            {
                // nothing caught (it was dodged, or it killed): whoever is right there
                Actor near = null;
                float best = float.MaxValue;
                foreach (Actor u in Combat.FoesAround(a, _to, 2.5f * _k))
                {
                    float d = (u.current_position - _to).sqrMagnitude;
                    if (d < best && Combat.Allowed(a, u) && !IsDoomed(u))
                    {
                        best = d;
                        near = u;
                    }
                }
                if (near != null)
                {
                    EndLink = Link.Close;
                    EndTarget = near;
                }
            }
            if (Dev.On && EndLink != Link.None)
            {
                Dev.Trace("slam " + Id + " leaves " + EndLink + (EndTarget != null ? " -> " + EndTarget.asset.id : ""));
            }
        }

        // Cataclysm's ground wave: the ring that rolls out to the edge of the blast, slowing as it spreads
        private const float WaveLife = 0.34f, WaveR0 = 6f, WaveEase = 1.6f;

        /// <summary>When the ground wave reaches 'dist' world units from the crater (the outer ring falls then).</summary>
        private float WaveTime(float dist)
        {
            float r1 = RingPx(OuterRadius * _area);
            float e = Mathf.Clamp01((dist / Px - WaveR0) / Mathf.Max(1f, r1 - WaveR0));
            return WaveLife * (1f - Mathf.Pow(1f - e, 1f / WaveEase));
        }

        private void LaunchVictims()
        {
            Vector2 at = _to;
            foreach (Launch l in _launches)
            {
                Actor u = l.U;
                if (u == null || u.isRekt() || u.Sid() != l.Id)
                {
                    continue;
                }
                Combat.Launch(u, at, l.Distance, l.Apex, l.Spin);
                if (l.Doomed)
                {
                    Doom(A, u, this);
                }
            }
            _launches.Clear();
        }

        // ------------------------------------------------- the fall finishes them --

        /// <summary>A unit a blow left at the edge of death and threw: the ground finishes it where it lands.</summary>
        private sealed class Doomed
        {
            public Actor U, By;
            public long Id, ById;
            public Move Move;
            public float Age;
            public bool Up;
        }

        private static readonly List<Doomed> _doomed = new List<Doomed>();

        /// <summary>Doomed units still in the air (dev log).</summary>
        internal static int Falling => _doomed.Count;

        [SakugaInit]
        private static void InitFall()
        {
            Hooks.FrameTick += TickDoomed;
            Hooks.WorldCleared += _doomed.Clear;
        }

        /// <summary>
        /// After a blow dealt with lethal: false, was it one that would have killed? The cap leaves
        /// such a victim at 1-2 health (the game truncates the damage), any other blow far above it.
        /// </summary>
        internal static bool AtDeathsDoor(Actor u)
        {
            return u != null && u.isAlive() && u.getHealth() <= 2;
        }

        /// <summary>
        /// 'u' was thrown by a blow that would have killed it: it dies when it lands (or at once if it
        /// never took off: too heavy to throw, held by something). 'move' is credited with the kill.
        /// </summary>
        internal static void Doom(Actor by, Actor u, Move move)
        {
            if (by == null || u == null || !u.isAlive())
            {
                return;
            }
            long id = u.Sid();
            foreach (Doomed d in _doomed)
            {
                if (d.U == u && d.Id == id)
                {
                    d.Age = 0f;
                    d.Up = false;
                    return;
                }
            }
            _doomed.Add(new Doomed { U = u, Id = id, By = by, ById = by.Sid(), Move = move });
        }

        /// <summary>Is 'u' flying to its death (a technique need not chase it)?</summary>
        internal static bool IsDoomed(Actor u)
        {
            if (u == null || _doomed.Count == 0)
            {
                return false;
            }
            long id = u.Sid();
            foreach (Doomed d in _doomed)
            {
                if (d.U == u && d.Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        private static void TickDoomed(float dt)
        {
            if (_doomed.Count == 0 || dt <= 0f)
            {
                return;
            }
            for (int i = _doomed.Count - 1; i >= 0; i--)
            {
                Doomed d = _doomed[i];
                Actor u = d.U;
                if (u == null || !u.isAlive() || u.Sid() != d.Id)
                {
                    // someone else finished it (an arrow, a technique) or it is gone; a body killed
                    // in mid-air comes down (the game doesn't move corpses)
                    if (u != null && u.Sid() == d.Id && !u.isAlive() && u.position_height > 0.02f && !Moves.Busy(u))
                    {
                        Tumble.Fall(u);
                    }
                    _doomed.RemoveAt(i);
                    continue;
                }
                if (u.isInMagnet() || u.isInsideSomething())
                {
                    // the god hand took it (or it got inside something): spared, never killed in the hand
                    _doomed.RemoveAt(i);
                    continue;
                }
                d.Age += dt;
                bool held = Moves.Locked(u);
                if (!held && u.position_height > 0.15f)
                {
                    d.Up = true;
                }
                bool down = !held && !u.under_forces && u.position_height <= 0.02f;
                // it lands; or it never took off (a heavy one staggers, a blow can't throw it);
                // or, held by another move all along, that move has the last word after all
                bool due = d.Up ? down : (d.Age > 0.3f && !held);
                if (!due && d.Age < 3.5f)
                {
                    continue;
                }
                _doomed.RemoveAt(i);
                Actor by = d.By;
                if (held || u.getHealth() > 2 || by == null || by.Sid() != d.ById)
                {
                    // taken by another move (its blows decide), healed meanwhile, or the slammer's
                    // body recycled (no one to credit): it lives, barely
                    continue;
                }
                // (any blow kills at 1-2 health: the game deals at least 1 after armour; the flat part
                // makes sure of 2 through the heaviest armour)
                HitResult r = Combat.Hit(by, u, 1f, true, true, 2000f, d.Move);
                if (Dev.On)
                {
                    Dev.Trace("fall: " + u.asset.id + "#" + d.Id + (r.Killed ? " dies where it lands" : " survives the fall") + " after " + d.Age.ToString("F2") + " s");
                }
            }
        }

        /// <summary>III: the shockwave reaches the outer ring and knocks it down (all of it when 'all').</summary>
        private void ProcessWave(bool all)
        {
            Actor a = A;
            for (int i = _wave.Count - 1; i >= 0; i--)
            {
                Wave w = _wave[i];
                if (!all && w.At > _since)
                {
                    continue;
                }
                _wave.RemoveAt(i);
                Actor u = w.U;
                if (u == null || u.Sid() != w.Id || !Combat.IsFoe(a, u) || !Combat.Grounded(u) || Moves.Locked(u))
                {
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (!Combat.Allowed(a, u, out float classMult))
                {
                    Combat.Hit(a, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                // (knocked off its feet; one the wave would have killed dies as it hits the ground)
                Combat.Hit(a, u, 1.3f * classMult, false, true, 0f, this);
                _waveHits++;
                if (u.isAlive())
                {
                    bool doomed = AtDeathsDoor(u);
                    Combat.Launch(u, _to, Mathf.Min(1f, 0.8f * _k), 0.5f, (Random.value < 0.5f ? -1f : 1f) * Random.Range(280f, 420f));
                    if (doomed)
                    {
                        Doom(a, u, this);
                    }
                }
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            if (Enter() && !_flow)
            {
                // (a Master and up never swings like a peasant: its ordinary blows are FlowStrikes,
                // so no attack cooldown to stall them)
                A.startAttackCooldown();
            }
            _since += dt;
            ProcessWave(false);
            ProcessRifts(false);
            // the landing's own clock: faster the higher the tier (the same springs, less time)
            float t = _pt * _recoverSpeed;
            switch (Level)
            {
                case 1:
                    {
                        float amp = Mathf.Exp(-8f * t) * Mathf.Cos(18f * t);
                        Pose.SX = 1f + 0.24f * amp;
                        Pose.SY = 1f - 0.26f * amp;
                        Pose.Rot = 6f * Mathf.Exp(-9f * t);
                        break;
                    }
                case 2:
                    {
                        float amp = Mathf.Exp(-7f * t) * Mathf.Cos(16f * t);
                        Pose.SX = 1f + 0.32f * amp;
                        Pose.SY = 1f - 0.34f * amp;
                        Pose.Rot = 8f * Mathf.Exp(-8f * t);
                        break;
                    }
                default:
                    {
                        // the superhero landing: held for a beat, trembling with heat, then it rises
                        const float hold = 0.16f;
                        if (t < hold)
                        {
                            Pose.SX = 1.24f;
                            Pose.SY = 0.74f;
                            Pose.Rot = 7f;
                            Pose.Nudge = new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                        }
                        else
                        {
                            float u = t - hold;
                            float amp = Mathf.Exp(-6f * u) * Mathf.Cos(14f * u);
                            Pose.SX = 1f + 0.24f * amp;
                            Pose.SY = 1f - 0.26f * amp;
                            Pose.Rot = 7f * Mathf.Exp(-7f * u);
                            Pose.Nudge = Vector2.zero;
                        }
                        if (LandingVisible)
                        {
                            // heat coming off it: from its sides, so the pose itself stays clean
                            _ember -= dt;
                            if (_ember <= 0f)
                            {
                                _ember = 0.07f;
                                float side = Random.value < 0.5f ? -1f : 1f;
                                Vector2 off = new Vector2(side * Random.Range(2.5f, 4.5f), Random.Range(-3f, 3f)) * Px * _k;
                                Kit.Ember(BodyPos() + off, Px, Random.Range(0.5f, 0.8f),
                                    new Vector2(side * Random.Range(1f, 5f), Random.Range(10f, 20f)) * Px, _pal.Edge);
                            }
                        }
                        break;
                    }
            }
            Pose.Ground = _to;
            Pose.Height = 0f;
            Pose.HideShadow = false;
            if (_pt >= _tRecover)
            {
                ProcessWave(true);
                ProcessRifts(true);
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Nudge = Vector2.zero;
                KillGlow();
                Chain();
                Finished = true;
            }
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            if (Dev.On && Dev.Verbose && (Time.frameCount & 3) == 0)
            {
                Dev.Trace("pose " + _phase + " sx " + Pose.SX.ToString("F2") + " sy " + Pose.SY.ToString("F2") + " rot " + Pose.Rot.ToString("F1")
                    + " | actor_scale " + A.actor_scale + " cur_scale " + A.current_scale + " target_angle " + A.target_angle
                    + " cur_rot " + A.current_rotation + " flip " + A.flip + " can_flip " + A.asset.can_flip + " jump " + A.move_jump_offset);
            }
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, GlowColor());
        }

        /// <summary>The unit's own glow, held over its sprite (a silhouette) for the whole move.</summary>
        private void MakeGlow()
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
                Fx.Glue(_glow, this, Fx.WithA(Charge, 0f));
            }
        }

        /// <summary>
        /// The unit's own glow: gathering power, igniting, white-hot on impact, cooling.
        /// I never burns (a pale shimmer); III burns white-hot and cools through the tier's colour.
        /// </summary>
        private Color GlowColor()
        {
            if (Level <= 1)
            {
                switch (_phase)
                {
                    case Phase.Windup:
                        {
                            float k = Mathf.Clamp01(_pt / _tWindup);
                            return Fx.WithA(_pal.Core, 0.35f * Anim.Smooth(0.1f, 1f, k));
                        }
                    case Phase.Rise:
                        return Fx.WithA(_pal.Core, Mathf.Lerp(0.25f, 0f, Mathf.Clamp01(_pt / _tRise)));
                    case Phase.Hang:
                        return Fx.WithA(_pal.Core, 0f);
                    case Phase.Dive:
                        return Fx.WithA(_pal.Core, 0.3f * Mathf.Clamp01(_pt / _tDive));
                    case Phase.Impact:
                        // a touch of light only: it lands in front of its little flash
                        return Fx.WithA(White, 0.25f);
                    default:
                        return Fx.WithA(_pal.Core, Mathf.Lerp(0.25f, 0f, Mathf.Clamp01(_pt * _recoverSpeed / 0.15f)));
                }
            }
            if (Level >= 3)
            {
                switch (_phase)
                {
                    case Phase.Windup:
                        {
                            float k = Mathf.Clamp01(_pt / _tWindup);
                            float pulse = 0.75f + 0.25f * Mathf.Sin(_pt * 50f);
                            return Fx.WithA(Color.Lerp(_pal.Edge, _pal.Core, 0.3f), 0.6f * Anim.Smooth(0.1f, 1f, k) * pulse);
                        }
                    case Phase.Rise:
                        return Fx.WithA(_pal.Edge, Mathf.Lerp(0.5f, 0.1f, Mathf.Clamp01(_pt / _tRise)));
                    case Phase.Hang:
                        {
                            float k = Anim.Smooth(0.5f, 1f, Mathf.Clamp01(_pt / _tHang));
                            return Color.Lerp(Fx.WithA(_pal.Edge, 0.1f), Fx.WithA(White, 0.85f), k);
                        }
                    case Phase.Dive:
                        return Fx.WithA(White, 0.9f);
                    case Phase.Impact:
                        return _pt < SilhouetteTime && _framed ? Silhouette() : Fx.WithA(White, 0.8f);
                    default:
                        {
                            // white-hot, then yellow fire, then the tier's colour, then gone; light
                            // enough over the held superhero landing that the pose itself reads
                            float k = Mathf.Clamp01(_pt / (_tRecover * 0.8f));
                            Color c = k < 0.35f ? Color.Lerp(White, Fire1, k / 0.35f) : Color.Lerp(Fire1, _pal.Edge, (k - 0.35f) / 0.65f);
                            return Fx.WithA(c, 0.6f * (1f - k) * (1f - k));
                        }
                }
            }
            switch (_phase)
            {
                case Phase.Windup:
                    {
                        float k = Mathf.Clamp01(_pt / _tWindup);
                        float pulse = 0.75f + 0.25f * Mathf.Sin(_pt * 50f);
                        return Fx.WithA(Charge, 0.5f * Anim.Smooth(0.1f, 1f, k) * pulse);
                    }
                case Phase.Rise:
                    return Fx.WithA(Charge, Mathf.Lerp(0.4f, 0.08f, Mathf.Clamp01(_pt / _tRise)));
                case Phase.Hang:
                    {
                        // the flip stays readable; the meteor ignites right before the dive
                        float k = Anim.Smooth(0.55f, 1f, Mathf.Clamp01(_pt / _tHang));
                        return Color.Lerp(Fx.WithA(Charge, 0.08f), Fx.WithA(Fire1, 0.6f), k);
                    }
                case Phase.Dive:
                    return Fx.WithA(Color.Lerp(Fire1, Core, Mathf.Clamp01(_pt / _tDive)), 0.8f);
                case Phase.Impact:
                    return _pt < SilhouetteTime && _framed ? Silhouette() : Fx.WithA(Core, 0.6f);
                default:
                    {
                        // cooling fast: the spring back up has to read (an orange blob at 0.7, it didn't)
                        float k = Mathf.Clamp01(_pt * _recoverSpeed / 0.22f);
                        return Color.Lerp(Fx.WithA(Fire1, 0.45f), Fx.WithA(Fire2, 0f), k);
                    }
            }
        }

        /// <summary>How long the slammer stays a flat silhouette on the impact frame (move time: it holds through a freeze).</summary>
        private const float SilhouetteTime = 0.06f;

        /// <summary>
        /// The impact frame's silhouette: dark against the bright starburst behind it, white against
        /// a Black Flash (the anime impact frame: the pose as a flat shape for an instant).
        /// </summary>
        private Color Silhouette()
        {
            return _crit ? Fx.WithA(White, 0.95f) : Fx.WithA(Color.Lerp(new Color(0.07f, 0.05f, 0.08f, 1f), _pal.Dark, 0.3f), 0.92f);
        }

        /// <summary>Our own shadow, even off screen (the game's is hidden while airborne).</summary>
        private void MakeShadow()
        {
            _shadow = Fx.Spawn(Art.Disk(ShadowRx(_air ? 0.6f : 1f), 0.42f, 0.45f), Pose.Ground, Px, 1f, FxLayer.Ground, 3);
            if (_shadow != null)
            {
                _shadow.Held = true;
                _shadow.Snap = true;
                _shadow.Color(new Color(0f, 0f, 0f, 0.36f));
            }
        }

        /// <summary>Our own shadow: it stays on the ground and shrinks and fades with height.</summary>
        private void UpdateShadow()
        {
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            float h = _apex > 0f ? Mathf.Clamp01(Pose.Height / _apex) : 0f;
            // up where Cataclysm goes, only a dot is left of it
            float smallest = Level >= 3 && !_air ? 0.3f : 0.45f;
            Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, smallest, h)), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != s)
            {
                _shadow.R.sprite = s;
                _shadow.Frames[0] = s;
            }
            _shadow.Pos = Pose.Ground;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.36f, Level >= 3 ? 0.22f : 0.16f, h));
        }

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        private void KillHeld()
        {
            _shadow?.Kill();
            _marker?.Kill();
            _markerRing?.Kill();
            _markerFill?.Kill();
            _shadow = _marker = _markerRing = _markerFill = null;
            _trail = false;
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        public override void Abort()
        {
            KillHeld();
            KillGlow();
            foreach (Particle p in _pebbles)
            {
                p?.Kill();
            }
            _pebbles.Clear();
            // cut off between the blow and the throw: those the blow would have killed still die
            // (they never took off: at once)
            foreach (Launch l in _launches)
            {
                if (l.Doomed && l.U != null && l.U.Sid() == l.Id)
                {
                    Doom(A, l.U, this);
                }
            }
            _launches.Clear();
            _wave.Clear();
            _rifts.Clear();
        }
    }

    /// <summary>
    /// Meteor Slam as a technique. Entry: Adept (I Adept, II Expert, III Master and up). The AI uses
    /// it on a crowd it can reach (I: 2+ enemies, II: 3+, III: 4+; 2 when chaining), never onto a
    /// wall, lava or water, and (Masters and up) never where it would land among its own side.
    /// Masters and up also drop it on a worthy lone foe (a Master or stronger, a monster) from
    /// range; Grandmasters and Gods leap further and blast wider (ReachScale, AreaScale).
    /// Chained from Thrown and Scattered, and from SelfAir as an Air Meteor (a dive from where the
    /// unit already is, onto the crowd or the one it just knocked down).
    /// </summary>
    internal sealed class MeteorSlamTech : Technique
    {
        public override string Id => "meteor_slam";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Area;

        /// <summary>A crouch that gathers power (Charge: a quick blow breaks it), a leap, a dive onto a crowd.</summary>
        public override Form Forms => Form.Charge | Form.Aerial | Form.Overhead | Form.Area | Form.Heavy | Form.Ground;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => new[] { 25f, 45f, 80f };
        public override float[] Cooldown => new[] { 12f, 15f, 18f };
        public override string Icon => "ui/Icons/sakuga/iconMeteorSlam";

        /// <summary>The main blast (full damage and launch) at this level.</summary>
        public static float Radius(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 2.4f : 3.5f;
        }

        /// <summary>Everything the slam touches: III's shockwave knocks down a ring out to 5 tiles.</summary>
        public static float BlastRadius(int level)
        {
            return level >= 3 ? MeteorSlam.OuterRadius : Radius(level);
        }

        /// <summary>The farthest it leaps at this level.</summary>
        public static float MaxReach(int level)
        {
            return level <= 1 ? 8f : level == 2 ? MeteorSlam.MaxRange : 18f;
        }

        /// <summary>How far a higher tier's blast spreads (hits and visuals): a Grandmaster 1.15x, a God 1.3x.</summary>
        public static float AreaScale(Actor a)
        {
            Tier t = Tiers.Of(a);
            return t == Tier.God ? 1.3f : t == Tier.Grandmaster ? 1.15f : 1f;
        }

        /// <summary>How much further a higher tier leaps: a Grandmaster 1.15x, a God 1.35x (its awareness still bounds it).</summary>
        public static float ReachScale(Actor a)
        {
            Tier t = Tiers.Of(a);
            return t == Tier.God ? 1.35f : t == Tier.Grandmaster ? 1.15f : 1f;
        }

        /// <summary>A foe worth a meteor on its own: a Master or stronger, or a monster of that weight.</summary>
        private static bool Worthy(Actor v)
        {
            return v != null && Tiers.Power(v) >= 8f;
        }

        public override bool Accepts(Link link)
        {
            return link == Link.Thrown || link == Link.Scattered || link == Link.SelfAir;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor self = c.Self;
            float k = Mathf.Sqrt(Mathf.Clamp(self.actor_scale / 0.1f, 0.6f, 4f));
            bool air = self.position_height > 0.05f;
            if (air && (c.Link != Link.SelfAir || !MeteorSlam.CanPerformAir(self)))
            {
                return null;
            }
            float area = AreaScale(self);
            float radius = Radius(level) * k * area;
            float blast = BlastRadius(level) * k * area;
            float min = air ? 0f : MeteorSlam.MinRange;
            float max = air ? MeteorSlam.AirRange(self, level) : MaxReach(level) * ReachScale(self);
            Vector2 at;
            int count;
            Actor center;
            if (c.Forced && (c.ForcePoint.HasValue || c.ForceTarget != null))
            {
                at = c.ForcePoint ?? c.ForceTarget.current_position;
                center = c.ForceTarget;
                count = Mathf.Max(1, c.CountNear(at, radius));
            }
            else
            {
                at = c.Cluster(radius, min, max, out count, out center);
                int need = level <= 1 ? 2 : level == 2 ? 3 : 4;
                if (c.Linked)
                {
                    need = 2;
                }
                if (air && c.LinkTarget != null && Combat.Grounded(c.LinkTarget)
                    && (c.LinkTarget.current_position - at).sqrMagnitude <= radius * radius)
                {
                    need = 1;   // the aerial finisher onto the one it just knocked down
                }
                if (count < need && !air && c.Info != null && c.Info.Flow)
                {
                    // a master fights a worthy foe with everything it has: a meteor dropped on that
                    // one alone, from across the field (gods duelling over the whole map)
                    Actor f = c.Focus;
                    if (Worthy(f) && Combat.Grounded(f) && !Moves.Locked(f) && c.Allowed(f))
                    {
                        float df = c.Dist(f);
                        if (df >= min && df <= max)
                        {
                            at = f.current_position;
                            center = f;
                            count = Mathf.Max(1, c.CountNear(at, radius));
                            need = 1;
                        }
                    }
                }
                if (count < need)
                {
                    return null;
                }
            }
            if (!Combat.SafeSpot(at))
            {
                if (!Combat.FindLanding(at, 1.5f, out at))
                {
                    return null;
                }
            }
            if (air && Vector2.Distance(c.Pos, at) > max + 1f)
            {
                return null;
            }
            if (!c.Forced)
            {
                int allies = c.AlliesNear(at, radius);
                if (allies > 0 && (c.Tier >= Tier.Master || allies >= count))
                {
                    return null;   // a master never lands on its own people
                }
            }
            // what it is worth: everyone in the main blast, and (III) the outer ring knocked down
            float mult = level <= 1 ? 1.3f : level == 2 ? 2.1f : 4.5f;
            float outer = level >= 3 ? 1.3f : 1f;
            float score = 0f;
            float r2 = radius * radius, b2 = blast * blast;
            int ranged = 0;
            foreach (Actor v in c.Enemies)
            {
                if (!Combat.Grounded(v) || Moves.Locked(v))
                {
                    continue;
                }
                float d2 = (v.current_position - at).sqrMagnitude;
                if (d2 > b2 || !c.Allowed(v, out float cm))
                {
                    continue;
                }
                score += c.Value(v, c.Expected(v, d2 <= r2 ? mult : outer), cm);
                if (d2 <= r2 && RisingTempest.Ranged(v))
                {
                    ranged++;
                }
            }
            // the right answer to a group it can't otherwise reach: a gap closer from the sky onto a
            // crowd standing off at a distance, above all archers who keep backing away
            if (!air && Vector2.Distance(c.Pos, at) >= 6f * k)
            {
                score += 15f;
            }
            score += Mathf.Min(ranged, 3) * 8f;
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan { Target = center, Point = at, Count = count, Score = score, AreaRadius = blast };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a != null && a.position_height > 0.05f)
            {
                return MeteorSlam.BeginFromAir(a, p.Point, p.Target, p.Level);
            }
            return MeteorSlam.Begin(a, p.Point, p.Target, p.Level);
        }

        public override bool Order(Actor a, WorldTile tile, int level)
        {
            return MeteorSlam.Begin(a, new Vector2(tile.posV3.x, tile.posV3.y), null, level);
        }
    }
}
