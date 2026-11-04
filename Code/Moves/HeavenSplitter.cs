using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN SPLITTER. One rising fist under the jaw that sends the enemy straight out of the
    /// scene: up past the top of the sky, and down again somewhere far away, into a crater or a
    /// wall of water. (After a scene of the show Invincible: two blurs in a whirlwind of cream wind
    /// ribbons, the plant, the uppercut, the head snapping back, the body rocketing up out of
    /// frame while the wind whips round the launch point; then a cut to a river far away, a thin
    /// line falling out of the sky, and a huge splash.)
    ///
    /// LEVEL I, "Rising Fist" (Adept, ~0.75 s; white and grey, no hit-stop, no decals):
    ///   STEP IN  0.08-0.22  a quick step in front of the enemy (pinned: no swing of its own)
    ///   PLANT    0.12       it drops into a crouch, leaning in, fist low
    ///   STRIKE   0.20       the fist comes up (the contact: a parry breaks it, an evade whiffs
    ///                       it): stretched tall in one frame, a white rising swoosh, a hit star
    ///                       flying up; a little hop. The enemy's head snaps back and it is
    ///                       popped 2.6 tiles up in a backflip, landing ~2 tiles behind on its
    ///                       feet in a ring of dust, reeling (stunned 0.6 s)
    ///   RECOVER  0.22       a spring back down
    ///
    /// LEVEL II, "Heaven Splitter" (Expert, ~0.8 s for the striker, ~1.2 s for the flight):
    ///   DASH     0.07-0.18  a blur dash in through bands of wind in the tier's colour coiling
    ///                       round its body (nothing can touch it), speed lines, afterimages
    ///   PLANT    0.13       the crouch, a tier ring closing on its feet, two bands of wind
    ///                       wrapping round the pair, a glint on the fist
    ///   STRIKE   0.26       the uppercut: a white pop and a tier ring at the chin, a column of
    ///                       speed lines, focus lines, a local hit-stop (both hold 0.06 s), a
    ///                       hop; the enemy is launched high (out of view at a normal zoom) and
    ///                       far (6-8 tiles), tumbling, and comes down head first: a crater
    ///                       (Kit.Ravage force 2), a ring and a wall of dust, anyone within 1.4
    ///                       tiles knocked flying; the enemy bounces out onto its feet, stunned 1 s
    ///
    /// LEVEL III, "Heaven Splitter" (Master and up; a God's is a scale bigger again):
    ///   WHIRL    0.44*      a blur dash to it, then one full lap around it (1.2 tiles out) on a
    ///                       ring of afterimages (nothing can touch it) while bands of cream wind
    ///                       wrap round the pair, climbing and falling through its height (each
    ///                       band's near half in front of the bodies, its far half behind), speed
    ///                       lines, dust (no room to circle it, by water or a wall: it closes in
    ///                       and the wind does the circling)
    ///   PLANT    0.16*      a deep crouch, glowing cracks under the front foot, a ring closing
    ///   STRIKE   0.32*      the uppercut on an impact frame: both bodies flat silhouettes
    ///                       against the starburst (a Black Flash on a crit), a global hit-stop
    ///                       (0.12 s; a God's 0.15), focus lines, a pillar of speed lines, the
    ///                       ground ringed white-hot; the striker rises with its fist (a hop of
    ///                       0.4 tiles). The enemy is shot straight up off the top of the screen
    ///                       on a cream streak, and as it passes, a funnel of flat wind rings
    ///                       bursts round the column (wider the higher) while bands of wind whip
    ///                       round the launch point and the striker standing in it (a
    ///                       Grandmaster's launch point scorched, a God's torn up and cratered;
    ///                       a Master's left be). The striker lands and goes on fighting at once.
    ///   GONE     0.4-0.8    a twinkle where it vanished into the sky, then nothing: a beat.
    ///                       Far away (Master 12-16 tiles, Grandmaster 15-20, a God 30-50,
    ///                       preferring open water when there is any in reach) its shadow
    ///                       appears and a ring closes on the spot (a God's with "camera
    ///                       follows god abilities" on: the camera glides there, the cut)
    ///   FALL     0.3        it falls out of the sky head first on a thin line of light
    ///   CRASH               land: an impact frame, a glowing crater (Kit.Ravage force 3, a
    ///                       God's force 4 with fissures running out of it), shockwave rings,
    ///                       a dust wall, rocks, sparks and embers, a camera shake; everyone
    ///                       within 2.2 tiles (a God's 3.3) is thrown and hurt. Water: a giant
    ///                       splash (a geyser 3 tiles high, a crown of jets, a dome of white
    ///                       water, rings racing out over the surface, mist; a God's twice that)
    ///                       and the wave throws whoever stands in it. The enemy bounces out
    ///                       of its crater onto its feet and reels, stunned 1.4 s (a God's 1.8)
    ///   (* times the tier's tempo: the square root of TierInfo.Linger; recoveries its Linger)
    ///
    /// Heavy or giant enemies (dragons, Crabzilla, anything too big to lift) just take the blow:
    /// the same impact, the wind bursting up around them, and a stagger. In a duel the flight is
    /// short (5 tiles, 9 between Grandmasters and Gods) and stays on dry land: the duel goes on.
    /// The landing is never off the map, on a cliff or in lava. A blow that would kill
    /// leaves the enemy at the edge of death; it flies, and dies where it comes down (no heap of
    /// bodies at the striker's feet). Damage (times the user's damage and its tier's Might):
    /// I x1.4 + a x0.25 landing; II x2.3 + a x0.7 crash; III x4 + a x1.3 crash; the crash also
    /// catches others (II x0.7, III x1.3). Chains: I and II leave Down on the enemy (it is back
    /// on the ground, reeling, as the window opens); III leaves Close on whoever is next to the
    /// striker (its enemy is far away by then).
    /// </summary>
    internal sealed class HeavenSplitter : Move
    {
        /// <summary>How far (tiles, times the unit's size) the AI starts one from at each level.</summary>
        public static float Reach(int level)
        {
            return level <= 1 ? 2.8f : level == 2 ? 4.5f : 6f;
        }

        /// <summary>A forced one (a god power, a dev command) closes in from up to this far.</summary>
        public const float ForcedReach = 6f;

        private enum Phase
        {
            StepIn,
            Dash,
            Whirl,
            Plant,
            Strike,
            Recover,
            Broken,
            Whiff
        }

        private const string SndPunch = "event:/SFX/OTHER/Punch";
        private const string SndHit = "event:/SFX/HIT/HitGeneric";
        private const string SndWhoosh = "event:/SFX/UNIQUE/SpawnWhoosh";

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Steel = new Color(0.86f, 0.92f, 1f, 1f);
        private static readonly Color ImpactDark = new Color(0.07f, 0.05f, 0.08f, 1f);

        /// <summary>Strikes that started (dev log).</summary>
        internal static int Started;

        private readonly Palette _pal;
        private readonly Tier _tier;
        private readonly float _k, _linger, _tempo;
        private readonly bool _armed;
        private readonly float _tPlant, _tStrike, _tRecover, _tBroken, _tWhiff, _tWhirl, _hop;
        private readonly float _mult;

        // ---- the enemy (until the blow lands; then the flight has it)
        private Actor _v;
        private long _vid;
        private Weight _class;
        private float _cm = 1f;
        private Vector2 _vGround;
        private float _stand, _vPivot, _vScale;
        private int _retargets, _chases;
        private Vector2 _dir = Vector2.right;
        private float _fwd = 1f;

        private HeavenFlight _flight;
        private bool _contact, _heavy, _lost, _crit;

        // ---- the clock
        private Phase _phase;
        private Phase _afterDash = Phase.Plant;
        private Vector2? _dashTo;
        private bool _dashed;
        private float _pt, _hold;
        private bool _entered;
        private Vector2 _from;
        private float _tMove, _rotFrom, _sxFrom = 1f, _syFrom = 1f;
        private float _ghostT, _ribbonT, _lineT, _dustT;
        private bool _glinted;

        // ---- the whirl
        private bool _orbit;
        private Vector2 _center, _tangent;
        private float _theta0, _rx, _ry;
        private int _spin = 1, _band;
        private static readonly float[] BandHeights = { 0.15f, 0.75f, 1.35f, 0.45f, 1.05f, 0.25f, 1.55f, 0.6f };

        // ---- the impact frame: the striker as a flat silhouette for an instant
        private Particle _glow;
        private float _silLeft;

        private HeavenSplitter(Actor a, Actor v, int level, float cm, Technique tech) : base(a)
        {
            Tech = tech;
            Level = Mathf.Clamp(level, 1, 3);
            _tier = Tiers.Of(a);
            _pal = Palette.For(_tier, Level);
            _k = K;
            _armed = Armed(a);
            _linger = Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
            _tempo = Mathf.Sqrt(_linger);
            switch (Level)
            {
                case 1:
                    _tPlant = 0.12f;
                    _tStrike = 0.2f;
                    _tRecover = 0.22f;
                    _hop = 0.1f;
                    break;
                case 2:
                    _tPlant = 0.13f;
                    _tStrike = 0.26f;
                    _tRecover = 0.22f;
                    _hop = 0.24f;
                    break;
                default:
                    _tPlant = 0.16f;
                    _tStrike = 0.32f;
                    _tRecover = 0.26f;
                    _hop = 0.4f;
                    break;
            }
            // the tier's tempo: the anticipation tightens by the square root of its Linger, the
            // dead time after it by the Linger itself (a God lands crisply and goes)
            _tPlant *= _tempo;
            _tStrike *= _tempo;
            _tWhirl = 0.44f * _tempo;
            _tRecover *= _linger;
            _tBroken = 0.38f * _linger;
            _tWhiff = 0.36f * _linger;
            _mult = HeavenSplitterTech.Uppercut(Level);
            SetVictim(v, cm);
            if (Level >= 3)
            {
                _phase = Phase.Whirl;   // (it decides there whether to dash in first)
            }
            else
            {
                bool far = Vector2.Distance(Pose.Ground, StandSpot()) > 0.3f;
                _phase = !far ? Phase.Plant : Level == 2 ? Phase.Dash : Phase.StepIn;
            }
        }

        // ------------------------------------------------------------- outside --

        internal Actor Striker => A;
        internal Tier TierOf => _tier;
        internal Palette Pal => _pal;
        internal float Cm => _cm;
        internal Vector2 Dir => _dir;
        internal bool Crit => _crit;

        /// <summary>Starts a Heaven Splitter on 'v' ('forced': a god power or a dev command, from further away).</summary>
        public static bool Begin(Actor a, Actor v, int level, Technique tech, bool forced)
        {
            if (a == null || v == null || !Moves.CanPerform(a) || !Combat.IsFoe(a, v) || Moves.Locked(v) || !HundredFists.Hopping(v)
                || v.isInLiquid() || MeteorSlam.IsDoomed(v))
            {
                return false;
            }
            if (!Combat.Allowed(a, v, out float cm))
            {
                return false;
            }
            float k = Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
            float reach = forced ? Mathf.Max(ForcedReach, Reach(level) * k) : Reach(level) * k + 1f;
            if (Vector2.Distance(a.current_position, v.current_position) > reach + v.stats["size"] * 0.5f)
            {
                return false;
            }
            HeavenSplitter m = new HeavenSplitter(a, v, level, cm, tech);
            if (!m.CanStand())
            {
                return false;   // nowhere solid beside it to strike from, or a river or a wall in the way
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(a.current_position, v.current_position, 0.5f) + new Vector2(0f, 1.8f * m._k));
            if (Dev.On) Dev.Trace("splitter " + m.Id + " " + Techniques.Roman(m.Level) + " (" + m._tier + ") on " + v.asset.id + "#" + v.Sid() + " hp " + v.getHealth()
                + " from " + Vector2.Distance(a.current_position, v.current_position).ToString("F1") + " tiles");
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

        private void SetVictim(Actor v, float cm)
        {
            _v = v;
            _vid = v.Sid();
            _class = Weights.Of(v);
            _cm = cm;
            _vGround = v.current_position;
            _vScale = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
            Sprite s = Fx.UnitSprite(v);
            float h = s != null ? s.rect.height : 10f;
            _vPivot = Mathf.Clamp(h * 0.36f, 1.5f, 40f);
            Sprite sa = Fx.UnitSprite(A);
            float ah = Mathf.Max(0.2f, (sa != null ? sa.rect.height : 10f) * A.actor_scale);
            float ratio = Mathf.Max(0.2f, h * v.actor_scale) / ah;
            // a fist's length from it: close enough to drive up under the jaw, far enough that the
            // two bodies read as two
            _stand = 0.6f * _k + 0.3f * Mathf.Max(0f, ratio - 1f) * _k;
            _dashed = false;
            Aim();
        }

        /// <summary>Turn to the enemy.</summary>
        private void Aim()
        {
            Vector2 d = _vGround - Pose.Ground;
            if (d.sqrMagnitude > 0.0025f)
            {
                _dir = d.normalized;
            }
            if (Mathf.Abs(_dir.x) > 0.12f)
            {
                _fwd = Mathf.Sign(_dir.x);
            }
            Pose.FaceRight = _fwd > 0f;
        }

        /// <summary>Where to strike from: in front of it (or the nearest solid side of it).</summary>
        private Vector2 StandSpot()
        {
            Vector2 s = Combat.Clamp(_vGround - _dir * _stand);
            if (Combat.SafeSpot(s))
            {
                return s;
            }
            float baseAng = Mathf.Atan2(-_dir.y, -_dir.x);
            for (int i = 1; i <= 4; i++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float a = baseAng + sgn * i * 0.6f;
                    Vector2 q = Combat.Clamp(_vGround + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _stand);
                    if (Combat.SafeSpot(q))
                    {
                        return q;
                    }
                }
            }
            return Pose.Ground;
        }

        private bool CanStand()
        {
            Vector2 s = StandSpot();
            if ((s - Pose.Ground).sqrMagnitude < 0.0001f)
            {
                // no solid spot beside it: only if it is already in reach where it stands
                return Vector2.Distance(Pose.Ground, _vGround) <= _stand + 0.8f * _k;
            }
            return Combat.ClearPath(Pose.Ground, s);
        }

        /// <summary>The enemy's chin: where the fist lands.</summary>
        private Vector2 Chin()
        {
            if (_v == null || _v.Sid() != _vid)
            {
                return BodyPos() + _dir * _stand + new Vector2(0f, 2f * Px);
            }
            return new Vector2(_v.current_position.x, _v.current_position.y + _v.position_height + _vPivot * 1.7f * _v.actor_scale);
        }

        private Vector2 Hand()
        {
            return BodyPos() + new Vector2(_fwd * 2.5f, -1f) * Px;
        }

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        /// <summary>The enemy is in someone else's hands or its own technique (a counter it started, a combo it is caught in).</summary>
        private bool HeldByOther()
        {
            return Moves.TryGet(_v, out Move m) && !m.Interruptible;
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("splitter " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        public override bool Intangible => _phase == Phase.Whirl || _phase == Phase.Dash && Level >= 2;

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            CheckVictim();
            if (Finished)
            {
                return;
            }
            _silLeft -= dt;
            if (_hold > 0f)
            {
                // the local hit-stop: the pose holds, trembling with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.StepIn:
                    StepIn(dt);
                    break;
                case Phase.Dash:
                    Dash(dt);
                    break;
                case Phase.Whirl:
                    Whirl(dt);
                    break;
                case Phase.Plant:
                    Plant(dt);
                    break;
                case Phase.Strike:
                    Strike();
                    break;
                case Phase.Broken:
                    Broken();
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
        /// Before the blow: is the enemy still there to hit? Killed by someone else, picked up by the
        /// god hand, gone into a building, taken by another combo: another enemy of its weight
        /// within 2 tiles takes its place (once), or the strike ends in a pose with a glint.
        /// </summary>
        private void CheckVictim()
        {
            if (_contact || _lost || _v == null || _phase >= Phase.Recover)
            {
                return;
            }
            bool ok = Combat.Living(_v) && _v.Sid() == _vid && !_v.isInMagnet() && !_v.isInsideSomething() && !HeldByOther();
            if (ok)
            {
                return;
            }
            if (!Retarget())
            {
                Lose();
            }
        }

        private bool Retarget()
        {
            if (_retargets >= 1)
            {
                return false;
            }
            Actor best = null;
            float bestD = 3f;
            foreach (Actor u in Combat.FoesAround(A, _vGround, 2f))
            {
                if (u == _v || Weights.Of(u) > _class || !Combat.Allowed(A, u) || !HundredFists.Catchable(u, A) || u.isInLiquid()
                    || MeteorSlam.IsDoomed(u) || Director.InScene(u))
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
            if (Dev.On) Dev.Trace("splitter " + Id + " retargets " + best.asset.id + "#" + best.Sid());
            Next(Level >= 3 ? Phase.Whirl : Level == 2 ? Phase.Dash : Phase.StepIn);
            return true;
        }

        /// <summary>Nothing left to hit: it ends in a pose, with a glint.</summary>
        private void Lose()
        {
            _lost = true;
            Next(Phase.Recover);
        }

        /// <summary>Follows the enemy until the blow (it can't swing while the striker closes in; it can still answer the blow itself).</summary>
        private void TrackVictim()
        {
            if (_contact || _v == null || _v.Sid() != _vid)
            {
                return;
            }
            _vGround = _v.current_position;
            Aim();
            Combat.Pin(_v, 0.35f);
        }

        // ------------------------------------------------------------ step in --

        /// <summary>I: a quick step in front of the enemy (and any level's one step after an enemy that slipped back).</summary>
        private void StepIn(float dt)
        {
            TrackVictim();
            Vector2 to = StandSpot();
            if (Enter())
            {
                _from = Pose.Ground;
                float d0 = Vector2.Distance(_from, to);
                _tMove = Mathf.Clamp(0.06f + 0.035f * d0, 0.08f, 0.22f) * _tempo;
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
            float k = Mathf.Clamp01(_pt / _tMove);
            Pose.Ground = Vector2.Lerp(_from, to, Anim.OutQuad(k));
            float lean = Mathf.Sin(Mathf.PI * k);
            Pose.Height = 0f;
            Pose.Rot = 14f * lean;
            Pose.SX = 1f + 0.12f * lean;
            Pose.SY = 1f - 0.1f * lean;
            Pose.Nudge = Vector2.zero;
            if (Visible && k < 0.9f && Vector2.Distance(_from, to) > 0.4f)
            {
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = Level <= 1 ? 0.045f : 0.032f;
                    Fx.Afterimage(this, 0.11f, Level <= 1 ? new Color(1f, 1f, 1f, 0.42f) : Fx.WithA(_pal.Ghost, 0.55f), _pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Plant);
            }
        }

        // --------------------------------------------------------------- dash --

        /// <summary>II: a blur dash in through wind ribbons coiling round its body. III: the dash to the edge of the whirl.</summary>
        private void Dash(float dt)
        {
            TrackVictim();
            Vector2 to = _dashTo ?? StandSpot();
            float px = Px;
            if (Enter())
            {
                _from = Pose.Ground;
                float d0 = Vector2.Distance(_from, to);
                _tMove = Mathf.Clamp(0.05f + 0.028f * d0, 0.07f, 0.18f) * _tempo;
                _ghostT = 0f;
                _lineT = 0f;
                Anim.Sound(SndWhoosh, A.current_tile);
                if (Visible)
                {
                    Fx.LandingPuff(_from, px, A.current_tile);
                    BodyRibbons(_tMove + 0.14f);
                }
            }
            float k = Mathf.Clamp01(_pt / _tMove);
            Pose.Ground = Vector2.Lerp(_from, to, Anim.OutQuad(k));
            Pose.Height = 0f;
            float lean = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.15f));
            Pose.Rot = 16f * lean;
            Pose.SX = 1f + 0.18f * (1f - k);
            Pose.SY = 1f - 0.12f * (1f - k);
            Pose.Nudge = Vector2.zero;
            if (Visible && k < 0.95f)
            {
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = 0.024f;
                    Fx.Afterimage(this, 0.12f, Level >= 3 ? Fx.WithA(White, 0.8f) : Fx.WithA(_pal.Ghost, 0.7f), Fx.WithA(_pal.Edge, 0.5f), _pal.GhostEnd);
                }
                _lineT -= dt;
                Vector2 d = to - _from;
                if (_lineT <= 0f && d.sqrMagnitude > 0.01f)
                {
                    _lineT = 0.03f;
                    Kit.SpeedLines(BodyPos() - d.normalized * 3f * px, d, px, 2, 3f, 0.12f, Fx.WithA(_pal.Edge, 0.85f));
                }
            }
            if (k >= 1f)
            {
                if (Visible)
                {
                    Fx.LandingPuff(Pose.Ground + _dir * px, px, A.current_tile);
                }
                _dashTo = null;
                Phase next = _afterDash;
                _afterDash = Phase.Plant;
                Next(next);
            }
        }

        /// <summary>Wind bands coiling round the striker's body as it dashes (glued to it): one at the waist, one at the shoulders.</summary>
        private void BodyRibbons(float life)
        {
            HeavenArt.Wind(Level, _pal, out Color head, out Color body, out Color tail);
            float px = Px;
            for (int i = 0; i < 2; i++)
            {
                Vector2 off = new Vector2(0f, (i == 0 ? -1.5f : 2.5f) * _k) * px;
                Particle[] band = HeavenArt.Band(BodyPos() + off, px, (i == 0 ? 11f : 9f) * _k, _fwd > 0f, 2, life, head, body, tail, i * 0.04f);
                foreach (Particle p in band)
                {
                    if (p != null)
                    {
                        p.Step = (q, d) => q.Pos = BodyPos() + off;
                    }
                }
            }
        }

        // -------------------------------------------------------------- whirl --

        /// <summary>
        /// III: one full lap around the enemy on a ring of afterimages, cream ribbons whipping round
        /// the pair at every height; it ends where it started, in front of the enemy. No room to
        /// circle (water, a wall on the ring): it closes in and the wind does the circling.
        /// </summary>
        private void Whirl(float dt)
        {
            TrackVictim();
            if (Enter() && !BeginWhirl())
            {
                return;
            }
            float k = Mathf.Clamp01(_pt / _tWhirl);
            // the eye of the whirlwind follows the enemy if it stumbles
            _center = Vector2.Lerp(_center, _vGround, Mathf.Clamp01(dt * 12f));
            Vector2 stand = StandSpot();
            if (_orbit)
            {
                float e = Mathf.Lerp(k, Anim.InOutSine(k), 0.45f);
                float th = _theta0 + _spin * Mathf.PI * 2f * e;
                Pose.Ground = Vector2.Lerp(Orbit(th), stand, Anim.Smooth(0.84f, 1f, k));
                _tangent = new Vector2(-Mathf.Sin(th) * _rx, Mathf.Cos(th) * _ry) * _spin;
                float settle = Anim.Smooth(0.85f, 1f, k);
                Pose.Rot = Mathf.Lerp(15f, 9f, settle);
                Pose.SX = Mathf.Lerp(1.14f, 1.08f, settle);
                Pose.SY = Mathf.Lerp(0.9f, 0.92f, settle);
                Pose.Nudge = Vector2.zero;
            }
            else
            {
                float m = Anim.OutQuad(Mathf.Clamp01(k / 0.3f));
                Pose.Ground = Vector2.Lerp(_from, stand, m);
                _tangent = stand - _from;
                float c = Anim.Smooth(0.3f, 0.6f, k);
                Pose.Rot = Mathf.Lerp(14f, 8f, c);
                Pose.SX = Mathf.Lerp(1.12f, 1.08f, c);
                Pose.SY = Mathf.Lerp(0.9f, 0.92f, c);
                Pose.Nudge = k > 0.45f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            Pose.Height = 0f;
            WhirlFx(dt, k);
            if (k >= 1f)
            {
                Next(Phase.Plant);
            }
        }

        /// <summary>Sets up the lap (or the dash to its edge first). False if it went to dash.</summary>
        private bool BeginWhirl()
        {
            _center = _vGround;
            _rx = _stand + 0.62f * _k;
            _ry = _rx * 0.62f;
            Vector2 rel = Pose.Ground - _center;
            if (rel.sqrMagnitude < 0.0004f)
            {
                rel = -_dir;
            }
            _theta0 = Mathf.Atan2(rel.y / _ry, rel.x / _rx);
            _orbit = OrbitClear();
            Vector2 entry = _orbit ? Orbit(_theta0) : StandSpot();
            if (!_dashed && Vector2.Distance(Pose.Ground, entry) > 0.5f * _k)
            {
                // too far to start circling: a blur dash to the edge of the whirl first
                _dashed = true;
                _dashTo = entry;
                _afterDash = Phase.Whirl;
                Next(Phase.Dash);
                return false;
            }
            _spin = Random.value < 0.5f ? 1 : -1;
            _from = Pose.Ground;
            _ghostT = _ribbonT = _lineT = _dustT = 0f;
            Anim.Sound(SndWhoosh, A.current_tile);
            _band = 0;
            if (Visible)
            {
                Kit.DustRing(_center, Px, Combat.TileAt(_center), 10, 40f, 0.5f, _k);
                // the whirlwind rises: two wide bands sweeping round the pair at once
                HeavenArt.Wind(Level, _pal, out Color head, out Color body, out Color tail);
                float vh = _vPivot * 2f * _vScale;
                HeavenArt.Band(_center + new Vector2(0f, 0.35f * vh), Px, _rx / Px * 1.9f, _spin > 0, 2, 0.34f, head, body, tail);
                HeavenArt.Band(_center + new Vector2(0f, 0.95f * vh), Px, _rx / Px * 1.6f, _spin > 0, 2, 0.3f, head, body, tail, 0.06f);
            }
            if (Dev.On) Dev.Trace("splitter " + Id + (_orbit ? " circles" : " can't circle (water or a wall): the wind does") + " r " + _rx.ToString("F2"));
            return true;
        }

        private Vector2 Orbit(float th)
        {
            return _center + new Vector2(Mathf.Cos(th) * _rx, Mathf.Sin(th) * _ry);
        }

        private bool OrbitClear()
        {
            for (int i = 0; i < 12; i++)
            {
                if (!Combat.Passable(Orbit(i / 12f * Mathf.PI * 2f)))
                {
                    return false;
                }
            }
            return true;
        }

        private void WhirlFx(float dt, float k)
        {
            if (!Visible)
            {
                return;
            }
            float px = Px;
            if (_orbit || k < 0.3f)
            {
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = 0.02f;
                    Fx.Afterimage(this, 0.13f, Fx.WithA(White, 0.78f), Fx.WithA(_pal.Edge, 0.55f), _pal.GhostEnd);
                }
            }
            _ribbonT -= dt;
            if (_ribbonT <= 0f)
            {
                // bands of wind wrapping round the pair, climbing and falling through its height
                _ribbonT = _orbit ? 0.05f : 0.04f;
                float vh = _vPivot * 2f * _vScale;   // the enemy's height (world units)
                float h = BandHeights[_band++ % BandHeights.Length] * vh;
                float r = _rx / px * Random.Range(1f, 1.45f);
                HeavenArt.Wind(Level, _pal, out Color head, out Color body, out Color tail);
                HeavenArt.Band(_center + new Vector2(0f, h), px, r, _spin > 0, Random.value < 0.4f ? 3 : 2, Random.Range(0.24f, 0.32f), head, body, tail);
            }
            if (!_orbit)
            {
                return;
            }
            _lineT -= dt;
            if (_lineT <= 0f && _tangent.sqrMagnitude > 1e-6f)
            {
                _lineT = 0.05f;
                Kit.SpeedLines(BodyPos(), _tangent, px, 2, 3f, 0.1f, Fx.WithA(_pal.Core, 0.75f));
            }
            _dustT -= dt;
            if (_dustT <= 0f && _tangent.sqrMagnitude > 1e-6f)
            {
                _dustT = 0.05f;
                Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], Pose.Ground, px, Random.Range(0.3f, 0.45f), FxLayer.Behind, 2);
                p?.Moving(-_tangent.normalized * Random.Range(6f, 12f) * px, Random.Range(2f, 5f) * px, 3f)
                    .Sized(0.6f, 1.05f)
                    .Color(Fx.WithA(dust, 0.65f), Fx.WithA(dust, 0.35f), Fx.WithA(dust, 0f));
            }
        }

        // -------------------------------------------------------------- plant --

        /// <summary>The anticipation: it drops into a crouch, leaning in, fist low.</summary>
        private void Plant(float dt)
        {
            TrackVictim();
            float px = Px;
            if (Enter())
            {
                _rotFrom = Pose.Rot;
                _sxFrom = Pose.SX;
                _syFrom = Pose.SY;
                Pose.Nudge = Vector2.zero;
                if (Visible)
                {
                    WorldTile tile = A.current_tile;
                    Fx.LandingPuff(Pose.Ground + _dir * 1.5f * px, px, tile);
                    if (Level == 2)
                    {
                        Fx.Ring(Pose.Ground, px, _tPlant + 0.05f, 12f * _k, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                            Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.8f), Fx.WithA(_pal.Core, 0.9f));
                        // the wind catches up with it: a pair of ribbons round the two of them
                        HeavenArt.Wind(Level, _pal, out Color head, out Color body, out Color tail);
                        Vector2 mid = Vector2.Lerp(Pose.Ground, _vGround, 0.5f) + new Vector2(0f, 3f * px);
                        HeavenArt.Band(mid, px, 15f * _k, _fwd > 0f, 2, 0.26f, head, body, tail);
                        HeavenArt.Band(mid + new Vector2(0f, 5f * px * _k), px, 12f * _k, _fwd > 0f, 2, 0.24f, head, body, tail, 0.05f);
                    }
                    else if (Level >= 3)
                    {
                        // the ground gives under the planted foot: glowing cracks, a ring closing in
                        Kit.Crater(Pose.Ground + _dir * 1.5f * px, px * _k, tile, true, 3.5f, true, _pal.Edge);
                        Fx.Ring(Pose.Ground, px, _tPlant + 0.05f, 16f * _k, 3f, 2, 1, FxLayer.Ground, 4, Art.Ground, 1.8f,
                            Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.85f), Fx.WithA(_pal.Core, 0.95f));
                    }
                }
            }
            float k = Mathf.Clamp01(_pt / _tPlant);
            float c = Anim.OutQuad(k);
            Pose.Ground = Vector2.Lerp(Pose.Ground, StandSpot(), Mathf.Clamp01(dt * 20f));
            Pose.Height = 0f;
            // (from whatever the approach left: a whirl ends already leaning in)
            Pose.SX = Mathf.Lerp(_sxFrom, 1.16f, c);
            Pose.SY = Mathf.Lerp(_syFrom, 0.8f, c);
            Pose.Rot = Mathf.Lerp(_rotFrom, 12f, c);
            Pose.Nudge = Level >= 3 && k > 0.5f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            if (Level >= 2 && !_glinted && k >= 0.5f)
            {
                _glinted = true;
                if (Visible)
                {
                    Fx.Glint(Hand() + new Vector2(0f, -0.5f * px), px, 0.16f, Level >= 3 ? _pal.Core : HeavenArt.CreamHi, 0.9f);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Strike);
            }
        }

        // ------------------------------------------------------------- strike --

        /// <summary>The fist comes up: the contact on the first frame, then the rising follow-through (a hop at II and III).</summary>
        private void Strike()
        {
            if (Enter())
            {
                Connect();
                if (_phase != Phase.Strike)
                {
                    return;   // parried, evaded, or it got away
                }
            }
            float k = Mathf.Clamp01(_pt / _tStrike);
            // stretched tall at once (the smear of the blow), relaxing as the hop comes down
            float up = Anim.OutCubic(Mathf.Clamp01(0.55f + k / 0.25f));
            float relax = Anim.Smooth(0.45f, 1f, k);
            Pose.SX = Mathf.Lerp(Mathf.Lerp(1.16f, 0.8f, up), 1f, relax * 0.85f);
            Pose.SY = Mathf.Lerp(Mathf.Lerp(0.8f, 1.32f, up), 1f, relax * 0.85f);
            Pose.Rot = Mathf.Lerp(Mathf.Lerp(12f, -16f, up), -4f, relax);
            Pose.Height = _hop * _k * (_heavy ? 0.6f : 1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.92f));
            Vector2 lunge = _from + _dir * 0.14f * _k;
            if (Combat.SafeSpot(lunge))
            {
                Pose.Ground = Vector2.Lerp(_from, lunge, Anim.OutCubic(Mathf.Clamp01(_pt / 0.06f)));
            }
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Next(Phase.Recover);
            }
        }

        /// <summary>The blow lands (or is answered).</summary>
        private void Connect()
        {
            _from = Pose.Ground;
            if (_v == null || !Combat.Living(_v) || _v.Sid() != _vid || !HundredFists.Hopping(_v) || HeldByOther())
            {
                Lose();
                return;
            }
            float reach = _stand + 0.8f * _k + _v.stats["size"] * 0.5f + 0.35f;
            float d = Vector2.Distance(Pose.Ground, _v.current_position);
            if (d > reach)
            {
                if (_chases < 1 && d <= reach + 1.6f && Combat.ClearPath(Pose.Ground, _v.current_position))
                {
                    // it slipped back (a backstep, a dodge): one quick step after it, then the blow
                    _chases++;
                    _vGround = _v.current_position;
                    Aim();
                    if (Dev.On) Dev.Trace("splitter " + Id + " steps after " + _v.asset.id + " (" + d.ToString("F2") + " tiles)");
                    Next(Phase.StepIn);
                    return;
                }
                Lose();
                return;
            }
            _contact = true;
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
            Vector2 chin = Chin();
            Tier vt = Tiers.Of(_v);
            Color tint = Level >= 2 && vt != Tier.None ? Tiers.Color(vt) : White;
            Puppet p = null;
            if (HundredFists.Catchable(_v, A) && !Weights.Heavy(_v))
            {
                float dx = A.current_position.x - _v.current_position.x;
                p = HundredFists.Take(_v, Mathf.Abs(dx) > 0.05f ? dx > 0f : _v.flip, this);
            }
            HitResult r;
            if (p != null)
            {
                // never lethal here: one it would kill flies anyway and dies where it comes down
                r = Combat.Hit(A, _v, _mult * _cm, false, false, 0f, this);
                bool owed = MeteorSlam.AtDeathsDoor(_v);
                _crit = r.Crit;
                if (Level >= 3)
                {
                    p.Flash(0.07f, r.Crit ? White : ImpactDark);   // a flat silhouette against the impact frame
                }
                else
                {
                    p.Flash(Level >= 2 ? 0.07f : 0.05f, tint);
                }
                _flight = HeavenFlight.Launch(this, p, tint, owed);
                if (Dev.On) Dev.Trace("splitter " + Id + " uppercut x" + (_mult * _cm).ToString("F2") + " -> " + _v.asset.id + " hp " + _v.getHealth()
                    + (r.Crit ? " CRIT" : "") + (owed ? " (flies to its death)" : ""));
            }
            else
            {
                // too heavy to lift: it takes the whole blow where it stands, and reels
                _heavy = true;
                r = Combat.Hit(A, _v, (_mult + 0.5f * HeavenSplitterTech.Crash(Level)) * _cm, true, true, 0f, this);
                _crit = r.Crit;
                if (Combat.Living(_v))
                {
                    Combat.Stagger(_v, Level <= 1 ? 0.5f : Level == 2 ? 0.8f : 1.1f);
                }
                if (Level >= 2)
                {
                    SingleKit.Ruin(A, Pose.Ground, _v.current_position, SingleKit.Radius(_tier, Level >= 3 ? 1.8f : 1.6f) * _k,
                        Level >= 3 ? SingleKit.Force(_tier, 1, 2) : 1);
                }
                if (Dev.On) Dev.Trace("splitter " + Id + " " + _v.asset.id + " too heavy to lift: takes it and staggers (hp " + _v.getHealth() + ")");
            }
            Anim.Sound(_armed ? "event:/SFX/HIT/HitSwordSword" : SndPunch, A.current_tile);
            if (Level >= 2)
            {
                Anim.Sound(SndHit, A.current_tile);
            }
            ImpactFx(chin, r.Crit);
            if (Level == 2)
            {
                _hold = 0.06f;
            }
            else if (Level >= 3)
            {
                Kit.Stop(chin, _tier == Tier.God ? 0.15f : 0.12f);
                _hold = 0.03f;
            }
            Dev.Watch(chin + new Vector2(0f, 1.2f * _k));
        }

        private void ImpactFx(Vector2 at, bool crit)
        {
            if (!Visible)
            {
                return;
            }
            float px = Px;
            Vector2 feet = _heavy ? _v.current_position : Vector2.Lerp(Pose.Ground, _vGround, 0.5f);
            WorldTile tile = Combat.TileAt(feet);
            switch (Level)
            {
                case 1:
                    {
                        Particle pop = Fx.Spawn(Art.Disk(3, 0.85f, 0.35f), at, px, 0.05f, FxLayer.Top, 30);
                        pop?.Sized(0.9f, 1.2f).Color(White, Fx.WithA(White, 0.8f), Fx.WithA(White, 0f));
                        Fx.HitSpark(at, px, 1, Vector2.up, 4);
                        Kit.Swish(this, Art.Arc(9, 1f, -70f, 115f, 2), 0.14f, new Vector2(_fwd, -1f), _fwd, White, _pal.Edge);
                        if (_armed)
                        {
                            Kit.Line(Hand(), at + new Vector2(0f, 4f * px), px, 0.06f, 1, FxLayer.Over, 6, Fx.WithA(Steel, 0.9f), Fx.WithA(Steel, 0f));
                        }
                        Fx.Ring(feet, px, 0.24f, 2f, 9f * _k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f, new Color(1f, 1f, 1f, 0.75f), new Color(1f, 1f, 1f, 0f));
                        break;
                    }
                case 2:
                    {
                        Particle pop = Fx.Spawn(Art.Disk(Mathf.RoundToInt(4f * _k), 0.85f, 0.35f), at, px, 0.06f, FxLayer.Top, 30);
                        pop?.Sized(0.9f, 1.25f).Color(White, Fx.WithA(White, 0.9f), Fx.WithA(_pal.Core, 0f));
                        Fx.Ring(at, px, 0.18f, 3f, 14f * _k, 2, 1, FxLayer.Top, 29, 1f, 2.5f,
                            Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
                        Fx.HitSpark(at, px, 2, Vector2.up, 6);
                        Fx.FocusLines(at, px, Fx.Count(8), 12f * _k, 22f * _k, 0.12f);
                        Kit.Swish(this, Art.Arc(11, 1f, -70f, 115f, 3), 0.16f, new Vector2(_fwd, -1f), _fwd, _pal.Core, _pal.Edge);
                        Kit.SpeedLines(at + new Vector2(0f, 4f * px), Vector2.up, px, 5, 3f * _k, 0.16f, Fx.WithA(_pal.Edge, 0.85f));
                        Fx.Ring(feet, px, 0.3f, 3f, 18f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                            Fx.WithA(White, 0.9f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                        if (_armed)
                        {
                            Kit.Line(Hand(), at + new Vector2(0f, 5f * px), px, 0.07f, 1, FxLayer.Over, 6, Fx.WithA(Steel, 0.95f), Fx.WithA(_pal.Edge, 0f));
                        }
                        if (crit)
                        {
                            Kit.ImpactFrame(at, px, 0.45f * _k, _pal, true);
                        }
                        Kit.Shake(at, 0.16f, 0.006f, _k);
                        break;
                    }
                default:
                    {
                        bool god = _tier == Tier.God;
                        bool grand = _tier >= Tier.Grandmaster;
                        // the impact frame behind the two bodies: they read as flat silhouettes against it
                        MeteorSlam.ImpactFrame(at, px * _k, god ? 1.05f : grand ? 0.95f : 0.85f, _pal, crit);
                        MakeGlow();
                        _silLeft = 0.06f;
                        Fx.HitSpark(at, px, 2, Vector2.up, 8);
                        Kit.SpeedLines(at + new Vector2(0f, 4f * px), Vector2.up, px, 8, 5f * _k, 0.22f, Fx.WithA(_pal.Core, 0.9f));
                        Kit.Swish(this, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(13f * _k), 8, 40), 1f, -70f, 115f, 3), 0.2f, new Vector2(_fwd, -1f), _fwd, _pal.Core, _pal.Edge);
                        Fx.Ring(at, px, 0.22f, 3f, 18f * _k, 2, 1, FxLayer.Top, 3, 1f, 2.5f, Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0f));
                        Fx.FocusLines(at, px, Fx.Count(12), 14f * _k, 28f * _k, 0.14f);
                        // the ground under the pair rings white-hot
                        Fx.Ring(feet, px, 0.32f, 4f, (grand ? 30f : 24f) * _k, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.8f,
                            Fx.WithA(White, 1f), Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
                        if (_armed)
                        {
                            Kit.Line(Hand(), at + new Vector2(0f, 6f * px), px, 0.08f, 2, FxLayer.Over, 6, Fx.WithA(White, 1f), Fx.WithA(_pal.Edge, 0f));
                        }
                        if (_heavy)
                        {
                            // the force goes up through the giant that can't be lifted
                            HeavenArt.LaunchColumn(_v.current_position, px, _k, 0.75f, _pal, tile, 0.42f, 0f, 9f, false);
                        }
                        Kit.Shake(at, god ? 0.4f : grand ? 0.3f : 0.25f, god ? 0.022f : grand ? 0.016f : 0.012f, _k);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            float px = Px;
            if (Enter())
            {
                SingleKit.Cooldown(A, _tier);
                _rotFrom = Pose.Rot;
                Pose.Nudge = Vector2.zero;
                if (Visible)
                {
                    if (_lost)
                    {
                        Fx.Glint(Hand() + new Vector2(0f, 2f * px), px, 0.22f, _pal.Core, 0.9f);
                    }
                    else if (_contact && Level >= 2)
                    {
                        Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                    }
                }
            }
            // (the same spring at the tier's pace: a Master's plays in about half the time)
            float st = _pt / _linger;
            float amp = Anim.Spring(st, 9f, 18f);
            Pose.SX = 1f + 0.1f * amp;
            Pose.SY = 1f - 0.12f * amp;
            Pose.Rot = _rotFrom * Mathf.Exp(-10f * st);
            Pose.Height = Mathf.MoveTowards(Pose.Height, 0f, dt * 3f);
            Pose.Nudge = Vector2.zero;
            if (_pt >= _tRecover)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                KillGlow();
                Chain();
                Finished = true;
            }
        }

        /// <summary>
        /// The chain it leaves: I and II leave Down on the enemy (back on the ground and reeling by
        /// the time the window opens); III, whose enemy is far away by then, leaves Close on whoever
        /// stands right here; a giant that only staggered, Down or Close on it.
        /// </summary>
        private void Chain()
        {
            EndLink = Link.None;
            EndTarget = null;
            if (_lost || !_contact)
            {
                return;
            }
            bool same = _v != null && _v.Sid() == _vid && Combat.Living(_v);
            if (_heavy)
            {
                if (same && Combat.Grounded(_v) && Combat.IsFoe(A, _v))
                {
                    EndLink = _v.hasStatus("stunned") ? Link.Down : Link.Close;
                    EndTarget = _v;
                }
                return;
            }
            if (_flight != null && Level <= 2 && same && !_flight.Owed && !_flight.Lost)
            {
                EndLink = Link.Down;
                EndTarget = _v;
                return;
            }
            Actor near = null;
            float best = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, Pose.Ground, 2.5f * _k))
            {
                if (u == _v || !Combat.Allowed(A, u) || MeteorSlam.IsDoomed(u))
                {
                    continue;
                }
                float d = (u.current_position - Pose.Ground).sqrMagnitude;
                if (d < best)
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

        // ---------------------------------------------------- answered: parried --

        /// <summary>The fist met a guard: the striker is rocked back and reels.</summary>
        private void Broken()
        {
            if (Enter())
            {
                _from = Pose.Ground;
                Vector2 to = _from - _dir * 0.38f * _k;
                _dashTo = Combat.SafeSpot(to) ? to : _from;
                if (Visible)
                {
                    Fx.Glint(Hand() + new Vector2(_fwd * Px, 2f * Px), Px, 0.16f, new Color(1f, 0.92f, 0.55f, 1f), 0.9f);
                    Fx.Afterimage(this, 0.12f, new Color(1f, 1f, 1f, 0.7f), _pal.GhostEnd);
                }
            }
            Vector2 back = _dashTo ?? _from;
            float k = Mathf.Clamp01(_pt / _tBroken);
            Pose.Ground = Vector2.Lerp(_from, back, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
            float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.25f));
            Pose.Rot = -20f * lean;
            Pose.SX = 1f - 0.06f * lean;
            Pose.SY = 1f + 0.06f * lean;
            Pose.Height = 0f;
            Pose.Nudge = k < 0.55f ? new Vector2(((int)(_pt * 50f) & 1) == 0 ? 1f : -1f, 0f) : Vector2.zero;
            if (k >= 1f)
            {
                _dashTo = null;
                SingleKit.Cooldown(A, _tier);
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Nudge = Vector2.zero;
                Finished = true;
            }
        }

        // ---------------------------------------------------- answered: evaded --

        /// <summary>The enemy wasn't there: the fist rises through empty air, and it overbalances a step.</summary>
        private void Whiff(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Anim.Sound(SndWhoosh, A.current_tile);
                if (Visible)
                {
                    Kit.Swish(this, Art.Arc(10, 1f, -70f, 115f, 2), 0.16f, new Vector2(_fwd, -1f), _fwd, new Color(1f, 1f, 1f, 0.8f), _pal.Edge);
                }
                Vector2 to = _from + _dir * 0.2f * _k;
                _dashTo = Combat.SafeSpot(to) ? to : _from;
            }
            float k = Mathf.Clamp01(_pt / _tWhiff);
            if (k < 0.5f)
            {
                float up = Anim.OutCubic(Mathf.Clamp01(0.55f + k / 0.2f));
                Pose.SX = Mathf.Lerp(1.16f, 0.84f, up);
                Pose.SY = Mathf.Lerp(0.8f, 1.26f, up);
                Pose.Rot = Mathf.Lerp(12f, -12f, up);
                Pose.Height = 0.5f * _hop * _k * Mathf.Sin(Mathf.PI * k / 0.5f);
                Pose.Ground = _from;
            }
            else
            {
                float s = Anim.OutQuad((k - 0.5f) / 0.5f);
                Pose.Ground = Vector2.Lerp(_from, _dashTo ?? _from, s);
                Pose.Rot = Mathf.Lerp(18f, 3f, s);
                Pose.SX = Mathf.Lerp(1.08f, 1f, s);
                Pose.SY = Mathf.Lerp(0.92f, 1f, s);
                Pose.Height = 0f;
            }
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                _dashTo = null;
                SingleKit.Cooldown(A, _tier);
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Height = 0f;
                // the one that slipped it is right there: whatever follows a close-in exchange comes next
                if (_v != null && _v.Sid() == _vid && Combat.Living(_v) && Combat.IsFoe(A, _v) && Combat.Grounded(_v)
                    && Vector2.Distance(Pose.Ground, _v.current_position) <= 2.2f * _k)
                {
                    EndLink = Link.Close;
                    EndTarget = _v;
                }
                Finished = true;
            }
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, _silLeft > 0f ? (_crit ? Fx.WithA(White, 0.95f) : Fx.WithA(Color.Lerp(ImpactDark, _pal.Dark, 0.3f), 0.92f)) : Fx.WithA(White, 0f));
            if (_silLeft < -0.3f)
            {
                KillGlow();
            }
        }

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
                Fx.Glue(_glow, this, Fx.WithA(White, 0f));
            }
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        public override void Abort()
        {
            KillGlow();
        }

        public override void OnEnd(bool aborted)
        {
            KillGlow();
        }
    }

    /// <summary>
    /// The enemy's flight after a Heaven Splitter: its Puppet (taken by the striker at the blow), driven
    /// from here every frame so the striker can go on fighting while it flies. I: a backflip lob two
    /// tiles back. II: a high arc out of view and down head first 6-8 tiles away. III: straight up out
    /// of the scene, gone for a beat, and straight down far away. Then the crash (a crater or a splash),
    /// a bounce out onto its feet, and it is let go reeling (or, if the blow was a killing one, it dies
    /// where it came down). Whatever happens to it on the way (killed, picked up by the god hand) ends
    /// the flight: the framework lets it go.
    /// </summary>
    internal sealed class HeavenFlight
    {
        private static readonly List<HeavenFlight> _all = new List<HeavenFlight>();

        /// <summary>Flights in the air right now (dev log).</summary>
        internal static int Count => _all.Count;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += TickAll;
            Hooks.WorldCleared += ClearAll;
        }

        private static void TickAll(float dt)
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (i >= _all.Count)
                {
                    continue;
                }
                HeavenFlight f = _all[i];
                try
                {
                    f.Tick(dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("heaven flight", e);
                    f.Fail();
                }
                if (f.Over)
                {
                    _all.Remove(f);
                }
            }
        }

        private static void ClearAll()
        {
            foreach (HeavenFlight f in _all)
            {
                f.KillFx();
            }
            _all.Clear();
        }

        private enum Stage
        {
            Snap,
            Arc,
            Rise,
            Gone,
            Fall,
            Crash,
            Plunge,
            Bounce,
            Settle,
            Dying
        }

        private const string SndWhoosh = "event:/SFX/UNIQUE/SpawnWhoosh";
        private static readonly float[] Offsets = { 0f, 22f, -22f, 45f, -45f, 70f, -70f, 100f, -100f, 135f, -135f, 180f };

        private readonly Puppet _p;
        private readonly Actor _v;
        private readonly long _vid;
        private readonly Actor _by;
        private readonly long _byId;
        private readonly Move _move;
        private readonly int _level;
        private readonly Tier _tier;
        private readonly Palette _pal;
        private readonly Color _tint;
        /// <summary>The striker's pixel size and size (the technique's art is drawn at its resolution), the enemy's size.</summary>
        private readonly float _px, _k, _vk, _vpx;
        private readonly float _cm;
        private readonly bool _rocket, _god, _grand;
        private readonly Vector2 _from;
        private Vector2 _to;
        private bool _water;
        private readonly float _apex, _h0;
        private readonly float _tSnap, _tFly, _tRise, _tGone, _tFall, _tCrash, _tBounce, _tSettle, _bounceH, _rotEnd, _daze;
        private readonly float _areaR;

        private Stage _stage;
        private float _pt;
        private bool _entered;
        private float _rot, _rotFrom, _rotTo, _hFrom;
        private Vector2 _nudge0;
        private float _ghostT, _follow = -1f, _settleFrom;
        private bool _seen, _marked;
        private Particle _shadow, _mark, _markRing;
        private int _hits;

        /// <summary>The blow left it at the edge of death: it dies where it comes down.</summary>
        public bool Owed;
        /// <summary>Taken out of the flight before it landed (killed by something else, the god hand).</summary>
        public bool Lost;
        public bool Over;

        private HeavenFlight(HeavenSplitter m, Puppet p, Color tint, bool owed)
        {
            _p = p;
            _v = p.A;
            _vid = _v.Sid();
            _by = m.Striker;
            _byId = _by.Sid();
            _move = m;
            _level = m.Level;
            _tier = m.TierOf;
            _god = _tier == Tier.God;
            _grand = _tier == Tier.Grandmaster;
            _pal = m.Pal;
            _tint = tint;
            _px = m.Px;
            _k = m.K;
            _vk = p.K;
            _vpx = p.Px;
            _cm = m.Cm;
            Owed = owed;
            _rocket = _level >= 3;
            _from = p.Pose.Ground;
            _h0 = Mathf.Max(0f, p.Pose.Height);
            Weight species = Weights.Species(_v);
            bool critter = species == Weight.Critter;
            bool big = species >= Weight.Brute && species > Weights.Species(_by);
            float far = critter ? 1.35f : big ? 0.75f : 1f;
            float dMin, dMax;
            switch (_level)
            {
                case 1:
                    dMin = 1.7f;
                    dMax = 2.3f;
                    break;
                case 2:
                    dMin = 6f;
                    dMax = 8f;
                    break;
                default:
                    if (_god)
                    {
                        dMin = 30f;
                        dMax = 50f;
                    }
                    else if (_grand)
                    {
                        dMin = 15f;
                        dMax = 20f;
                    }
                    else
                    {
                        dMin = 12f;
                        dMax = 16f;
                    }
                    break;
            }
            dMin *= far;
            dMax *= far;
            // in a duel the two must stay within reach of each other: the duel goes on
            Duel d = Duels.Of(_v) ?? Duels.Of(_by);
            if (d != null && _level >= 2)
            {
                dMax = Mathf.Min(dMax, d.Grand ? 9f : 5f);
                dMin = Mathf.Min(dMin, dMax * 0.6f);
            }
            // (III loves a lake for its splash; a duel keeps its fight on dry land)
            _to = ChooseLanding(_from, m.Dir, dMin, dMax, _level >= 3 && d == null, out _water);
            float dist = Vector2.Distance(_from, _to);
            float kv = Mathf.Sqrt(_vk);
            switch (_level)
            {
                case 1:
                    _tSnap = 0.03f;
                    _tFly = 0.46f + 0.02f * dist;
                    _apex = 2.6f * (critter ? 1.25f : big ? 0.8f : 1f);
                    _rotEnd = -360f;   // one backflip, onto its feet
                    _tCrash = 0.05f;
                    _tBounce = 0.14f;
                    _bounceH = 0.12f * _vk;
                    _tSettle = 0.12f;
                    _daze = 0.6f;
                    _areaR = 0f;
                    break;
                case 2:
                    _tSnap = 0.06f;
                    _tFly = 0.9f + 0.025f * dist;
                    _apex = (big ? 12f : 15f) * kv;
                    _rotEnd = -540f;   // a backflip and a half: it comes down head first
                    _tCrash = 0.06f;
                    _tBounce = 0.24f;
                    _bounceH = 0.35f * _vk;
                    _tSettle = 0.14f;
                    _daze = 1f;
                    _areaR = 1.4f * kv;
                    break;
                default:
                    _tSnap = 0.04f;
                    _tRise = 0.42f;
                    _apex = (_god ? 34f : _grand ? 26f : 22f) * kv;
                    _tGone = 0.25f + 0.011f * dist;
                    _tFall = 0.24f + 0.003f * _apex;
                    _tCrash = 0.07f;
                    _tBounce = 0.3f;
                    _bounceH = 0.55f * _vk;
                    _tSettle = 0.16f;
                    _daze = _god ? 1.8f : 1.4f;
                    _areaR = 2.2f * kv * (_god ? 1.5f : _grand ? 1.15f : 1f);
                    break;
            }
            _stage = Stage.Snap;
            if (Dev.On) Dev.Trace("splitter flight " + Techniques.Roman(_level) + ": " + _v.asset.id + "#" + _vid + " to " + dist.ToString("F1") + " tiles"
                + (_water ? " INTO WATER" : "") + " apex " + _apex.ToString("F1") + (d != null ? " (duel)" : "") + (owed ? " doomed" : ""));
        }

        internal static HeavenFlight Launch(HeavenSplitter m, Puppet p, Color tint, bool owed)
        {
            HeavenFlight f = new HeavenFlight(m, p, tint, owed);
            _all.Add(f);
            return f;
        }

        /// <summary>
        /// Where it comes down: along the throw's line (fanning out to the sides, then behind)
        /// at about the distance it was thrown, never off the map, on a wall or mountain, or in
        /// lava. 'preferWater': III loves a lake (the giant splash); I and II avoid one. Nowhere at
        /// all: straight back down where it went up.
        /// </summary>
        private static Vector2 ChooseLanding(Vector2 from, Vector2 away, float dMin, float dMax, bool preferWater, out bool water)
        {
            water = false;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = Random.insideUnitCircle;
                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector2.right;
                }
            }
            away.Normalize();
            float baseAng = Mathf.Atan2(away.y, away.x);
            float ideal = Random.Range(dMin, dMax);
            float step = Mathf.Max(0.4f, (dMax - dMin) / 5f);
            float best = float.MinValue;
            Vector2 spot = from;
            bool found = false;
            for (int i = 0; i < Offsets.Length; i++)
            {
                float off = Offsets[i];
                float a = baseAng + off * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int j = 0; j <= 10; j++)
                {
                    // the ideal distance first, then alternately farther and nearer
                    float dist = ideal + ((j & 1) == 0 ? 1f : -1f) * ((j + 1) / 2) * step;
                    if (dist < dMin - 0.01f || dist > dMax + 0.01f)
                    {
                        continue;
                    }
                    Vector2 q = from + dir * dist;
                    if (q.x < 2f || q.y < 2f || q.x > MapBox.width - 3f || q.y > MapBox.height - 3f)
                    {
                        continue;
                    }
                    WorldTile t = Combat.TileAt(q);
                    if (t == null || t.Type == null || t.Type.block || t.Type.lava)
                    {
                        continue;
                    }
                    bool w = t.Type.liquid;
                    float s = 100f - Mathf.Abs(off) * 0.35f - Mathf.Abs(dist - ideal) * 2f;
                    if (w)
                    {
                        // (out in the open water, not on the shore: the splash has room)
                        s += preferWater ? 70f + 5f * Open(q) : -25f;
                    }
                    if (s > best)
                    {
                        best = s;
                        spot = q;
                        water = w;
                        found = true;
                    }
                }
            }
            if (!found)
            {
                WorldTile t = Combat.TileAt(from);
                water = t != null && t.Type != null && t.Type.liquid;
                return from;
            }
            return spot;
        }

        /// <summary>How much water is round a spot (0..8: the liquid tiles 1.6 tiles out round it).</summary>
        private static int Open(Vector2 at)
        {
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                WorldTile t = Combat.TileAt(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.6f);
                if (t != null && t.Type != null && t.Type.liquid)
                {
                    n++;
                }
            }
            return n;
        }

        // --------------------------------------------------------------- tick --

        private bool Visible(Vector2 at)
        {
            return Fx.OnScreen(at, 0.35f);
        }

        private Vector2 Body => _p.BodyPos();

        private void Tick(float dt)
        {
            if (Over)
            {
                return;
            }
            // still ours? (killed by something else, picked up by the god hand, recycled: the
            // framework let it go; a body killed in the air drops on its own)
            if (_p.Finished || _v == null || _v.Sid() != _vid)
            {
                if (_stage != Stage.Dying)
                {
                    Lost = true;
                    if (Dev.On) Dev.Trace("splitter flight of " + _vid + " cut short in " + _stage);
                }
                End();
                return;
            }
            FollowCamera();
            if (dt <= 0f)
            {
                return;
            }
            _pt += dt;
            switch (_stage)
            {
                case Stage.Snap:
                    Snap();
                    break;
                case Stage.Arc:
                    Arc(dt);
                    break;
                case Stage.Rise:
                    Rise(dt);
                    break;
                case Stage.Gone:
                    Gone();
                    break;
                case Stage.Fall:
                    Fall(dt);
                    break;
                case Stage.Crash:
                    Crash();
                    break;
                case Stage.Plunge:
                    Plunge();
                    break;
                case Stage.Bounce:
                    Bounce();
                    break;
                case Stage.Settle:
                    Settle();
                    break;
                default:
                    Dying();
                    break;
            }
            UpdateShadow();
        }

        private void Next(Stage s)
        {
            if (Dev.On) Dev.Trace("splitter flight " + _vid + " " + _stage + " -> " + s + " (h " + _p.Pose.Height.ToString("F1") + ")");
            _stage = s;
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

        private float Tremble => ((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f;

        /// <summary>The striker is still there to credit its blows to (a dead one too; not one whose body became another unit).</summary>
        private bool Credit => _by != null && _by.Sid() == _byId;

        // ------------------------------------------------------------- snap --

        /// <summary>The head snaps back under the fist (held through the hit-stop), a pixel's tremble.</summary>
        private void Snap()
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                _rotFrom = vp.Rot;   // (one taken lying down is hauled up by the blow)
                _nudge0 = vp.Nudge;
                MakeShadow();
            }
            float k = Mathf.Clamp01(_pt / _tSnap);
            vp.Ground = _from;
            vp.Height = _h0 + 0.1f * _vk * Anim.OutQuad(k);
            vp.Rot = Mathf.Lerp(_rotFrom, -34f, Anim.OutCubic(Mathf.Min(1f, 0.4f + k * 3f)));
            vp.SX = 0.9f;
            vp.SY = 1.12f;
            vp.Nudge = _nudge0 * (1f - k) + new Vector2(Tremble, 0f);
            vp.HideShadow = true;
            _p.Pose = vp;
            if (k >= 1f)
            {
                _rot = vp.Rot;
                Next(_rocket ? Stage.Rise : Stage.Arc);
            }
        }

        // -------------------------------------------------------------- arc --

        /// <summary>I and II: a lob (I, a backflip two tiles back) or a high arc out of view and down head first (II).</summary>
        private void Arc(float dt)
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                _hFrom = vp.Height;
                _rotFrom = _rot;
                if (_level >= 2)
                {
                    Anim.Sound(SndWhoosh, _v.current_tile);
                    if (Visible(_from))
                    {
                        WorldTile t = Combat.TileAt(_from);
                        Kit.DustRing(_from, _px, t, 10, 45f, 0.5f, _k);
                        Kit.SpeedLines(_from + new Vector2(0f, 6f * _px), Vector2.up, _px, 6, 4f * _k, 0.2f, Fx.WithA(_pal.Edge, 0.85f));
                        HeavenArt.Wind(_level, _pal, out Color head, out Color body, out Color tail);
                        for (int i = 0; i < 3; i++)
                        {
                            HeavenArt.Band(_from + new Vector2(0f, (2f + i * 5f) * _px * _k), _px, (12f + 4f * i) * _k, true, 2,
                                0.28f, head, body, tail, i * 0.06f);
                        }
                    }
                }
            }
            float k = Mathf.Clamp01(_pt / _tFly);
            // I lobs it back; II sends it mostly up, across at the top (out of view), and down
            float u = _level <= 1 ? Mathf.Lerp(k, Anim.OutQuad(k), 0.3f) : Anim.Smooth(0.15f, 0.88f, k);
            vp.Ground = Vector2.Lerp(_from, _to, u);
            vp.Height = Mathf.Lerp(_hFrom, 0f, k) + _apex * Lob(k);
            _rot = Mathf.Lerp(_rotFrom, _rotEnd, Mathf.Lerp(k, Anim.OutQuad(k), 0.5f));
            vp.Rot = _rot;
            // smeared along the motion while it is fast (the launch, the drop), round at the top
            float fast = Mathf.Clamp01(Mathf.Abs(LobSlope(k)));
            vp.SX = 1f - 0.12f * fast;
            vp.SY = 1f + 0.22f * fast;
            vp.Nudge = Vector2.zero;
            vp.HideShadow = true;
            _p.Pose = vp;
            Ghosts(dt, _level <= 1 ? 0.045f : 0.03f);
            if (_level >= 2 && !_marked && k >= 0.5f)
            {
                // up there, out of sight: make sure of the spot it comes down on, and mark it
                Validate();
                Marker(_tFly * (1f - k) + 0.02f);
                Dev.Watch(_to + new Vector2(0f, 2f));
            }
            if (k >= 1f)
            {
                Next(Stage.Crash);
            }
        }

        /// <summary>Height over the lob (0..1): fast up, the top at 42%, falling faster and faster.</summary>
        private static float Lob(float k)
        {
            const float top = 0.42f;
            if (k < top)
            {
                return Anim.OutQuad(k / top);
            }
            float f = (k - top) / (1f - top);
            return 1f - f * f;
        }

        /// <summary>How fast the lob climbs or drops at k (1 at its fastest).</summary>
        private static float LobSlope(float k)
        {
            const float top = 0.42f;
            return k < top ? 1f - k / top : (k - top) / (1f - top);
        }

        // ------------------------------------------------------------- rise --

        /// <summary>III: shot straight up off the top of the screen on a cream streak; the wind bursts round the launch point.</summary>
        private void Rise(float dt)
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                _p.Flash(0.04f, _tint);
                _hFrom = vp.Height;
                _rotFrom = _rot;
                _seen = Visible(_from);
                WorldTile tile = Combat.TileAt(_from);
                Anim.Sound(SndWhoosh, tile);
                Anim.Sound(_god ? "event:/SFX/EXPLOSIONS/ExplosionMiddle" : "event:/SFX/EXPLOSIONS/ExplosionSmall", tile);
                if (_seen)
                {
                    HeavenArt.LaunchColumn(_from, _px, _k, _god ? 1.45f : _grand ? 1.2f : 1f, _pal, tile, _tRise, _hFrom, _apex, _god);
                }
                // a Grandmaster's launch point is scorched, a God's torn up (the striker's own footing
                // only scorched); a Master's wind leaves the ground be
                if (_god || _grand)
                {
                    Vector2 feet = Credit && _by.isAlive() ? _by.current_position : _from;
                    SingleKit.Ruin(_by, feet, _from, (_god ? 2.4f : 1.6f) * _k, _god ? 3 : 1);
                }
            }
            float k = Mathf.Clamp01(_pt / _tRise);
            float e = Anim.OutQuad(k);
            vp.Ground = _from;
            vp.Height = _hFrom + (_apex - _hFrom) * e;
            _rot = _rotFrom - 300f * e;
            vp.Rot = _rot;
            vp.SX = Mathf.Lerp(0.66f, 0.92f, k);
            vp.SY = Mathf.Lerp(1.6f, 1.12f, k);
            vp.Nudge = Vector2.zero;
            vp.HideShadow = true;
            _p.Pose = vp;
            Streak(_from + new Vector2(0f, _hFrom + 4f * _vpx), Body, 0.16f);
            Ghosts(dt, 0.025f);
            if (k >= 1f)
            {
                Next(Stage.Gone);
            }
        }

        /// <summary>A line of light from 'a' to the body: the column it shot up, the line it falls down.</summary>
        private void Streak(Vector2 a, Vector2 b, float life)
        {
            if (!Visible(a) && !Visible(b) && !Visible(Vector2.Lerp(a, b, 0.5f)))
            {
                return;
            }
            HeavenArt.Wind(3, _pal, out Color head, out Color body, out Color tail);
            int thick = _god ? 2 : 1;
            Kit.Line(a, b, _px, life, thick, FxLayer.Behind, 2, Fx.WithA(head, 0.95f), Fx.WithA(body, 0.7f), tail);
        }

        private void Ghosts(float dt, float every)
        {
            if (!Fx.OnScreen(Body, 0.1f))
            {
                return;
            }
            _ghostT -= dt;
            if (_ghostT > 0f)
            {
                return;
            }
            _ghostT = every;
            if (_level <= 1)
            {
                Fx.Afterimage(_p, 0.1f, new Color(1f, 1f, 1f, 0.42f), _pal.GhostEnd);
            }
            else if (_level == 2)
            {
                Fx.Afterimage(_p, 0.12f, Fx.WithA(_pal.Ghost, 0.6f), _pal.GhostEnd);
            }
            else
            {
                Fx.Afterimage(_p, 0.13f, Fx.WithA(HeavenArt.CreamHi, 0.8f), Fx.WithA(HeavenArt.Cream, 0.55f), _pal.GhostEnd);
            }
        }

        // ------------------------------------------------------------- gone --

        /// <summary>III: a twinkle where it vanished into the sky, then nothing for a beat while it crosses the world up there.</summary>
        private void Gone()
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                Vector2 top = new Vector2(_from.x, _from.y + _apex + 1f);
                if (Fx.OnScreen(top, 0.05f))
                {
                    Fx.Glint(top, _px, 0.4f, HeavenArt.CreamHi, 1.6f);
                }
                Validate();
                Marker(_tGone + _tFall);
                Dev.Watch(_to + new Vector2(0f, 2.5f));
                _follow = Settings.CameraFollow && _god && _seen && !MoveCamera.hasFocusUnit() ? 0f : -1f;
            }
            float k = Mathf.Clamp01(_pt / _tGone);
            vp.Ground = Vector2.Lerp(_from, _to, Anim.InOutSine(k));
            vp.Height = _apex;
            vp.Hidden = true;
            vp.HideShadow = true;
            _p.Pose = vp;
            if (k >= 1f)
            {
                Next(Stage.Fall);
            }
        }

        /// <summary>The spot it comes down on is still somewhere to come down (a blast elsewhere may have changed it).</summary>
        private void Validate()
        {
            WorldTile t = Combat.TileAt(_to);
            if (t != null && t.Type != null && !t.Type.block && !t.Type.lava)
            {
                _water = t.Type.liquid;
                return;
            }
            if (Combat.FindLanding(_to, 4f, out Vector2 q, true))
            {
                _to = q;
            }
            else
            {
                _to = _from;
            }
            WorldTile n = Combat.TileAt(_to);
            _water = n != null && n.Type != null && n.Type.liquid;
        }

        // ------------------------------------------------------------- fall --

        /// <summary>III: it falls out of the sky head first on a thin line of light.</summary>
        private void Fall(float dt)
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                WorldTile t = Combat.TileAt(_to);
                Anim.Sound("event:/SFX/DESTRUCTION/FallMeteorite", t);
                _rot = 180f;
                vp.Hidden = false;
            }
            float k = Mathf.Clamp01(_pt / _tFall);
            float e = k * k;
            vp.Ground = _to;
            vp.Height = _apex * (1f - e);
            vp.Hidden = false;
            vp.Rot = _rot;
            vp.SX = 0.7f;
            vp.SY = 1.45f;
            vp.Nudge = Vector2.zero;
            vp.HideShadow = true;
            _p.Pose = vp;
            // the line of light it falls down (as far up as the screen shows of it)
            Vector2 b = Body;
            Streak(b + new Vector2(0f, Mathf.Min(_apex * e + 1f, 9f * _k)), b, 0.1f);
            Ghosts(dt, 0.025f);
            if (k >= 1f)
            {
                Next(Stage.Crash);
            }
        }

        // ------------------------------------------------------------ crash --

        private void Crash()
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                KillMarker();
                WorldTile tile = Combat.TileAt(_to);
                _water = tile != null && tile.Type != null && tile.Type.liquid;
                Hurt();
                Effects(tile);
                Devastate(tile);
                if (Dev.On) Dev.Trace("splitter flight " + _vid + " comes down " + (_water ? "in water" : "on land") + " at " + _to + ", hp " + _v.getHealth()
                    + (Owed ? " (doomed)" : "") + (_hits > 0 ? ", " + _hits + " caught around it" : ""));
            }
            float k = Mathf.Clamp01(_pt / _tCrash);
            vp.Ground = _to;
            vp.Height = 0f;
            vp.Rot = _rot;
            vp.Nudge = Vector2.zero;
            vp.HideShadow = true;
            if (_water)
            {
                // plunged under the splash
                vp.Hidden = _pt > 0.03f;
                vp.SX = 0.8f;
                vp.SY = 1.3f;
            }
            else if (_level <= 1)
            {
                vp.SX = 1.25f;
                vp.SY = 0.75f;
            }
            else
            {
                // head first into the ground, crushed for an instant
                vp.SX = 1.3f;
                vp.SY = 0.65f;
                vp.Nudge = new Vector2(Tremble, 0f);
            }
            _p.Pose = vp;
            if (k >= 1f)
            {
                Next(_water ? Stage.Plunge : Stage.Bounce);
            }
        }

        /// <summary>
        /// The ground hits back: the enemy takes the crash (never lethal here: a killing one is paid
        /// when it lies still), and at II and III everyone around the spot is hurt and thrown (one
        /// the crash would kill flies and dies where it lands).
        /// </summary>
        private void Hurt()
        {
            if (!Credit)
            {
                return;   // no one left to credit (the striker's body became another unit): only the fall
            }
            if (Combat.Living(_v))
            {
                Combat.Hit(_by, _v, HeavenSplitterTech.Crash(_level) * _cm, false, false, 0f, _move);
                if (MeteorSlam.AtDeathsDoor(_v))
                {
                    Owed = true;
                }
                _p.Flash(_level >= 2 ? 0.07f : 0.05f, _tint);
            }
            if (_level < 2 || _by.kingdom == null)
            {
                return;
            }
            float mult = HeavenSplitterTech.Area(_level);
            foreach (Actor u in Combat.FoesAround(_by, _to, _areaR))
            {
                if (u == _v || u.Sid() == _vid || MeteorSlam.IsDoomed(u))
                {
                    continue;
                }
                if (Combat.Reach(_move, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (!Combat.Allowed(_by, u, out float cm))
                {
                    // too heavy for this tier: a glancing blow, no throw
                    Combat.Hit(_by, u, 0.5f, true, true, 0f, _move);
                    Combat.Stagger(u, 0.3f);
                    _hits++;
                    continue;
                }
                float d = Vector2.Distance(u.current_position, _to);
                float near = 1f - Mathf.Clamp01(d / Mathf.Max(0.1f, _areaR));
                Combat.Hit(_by, u, Mathf.Lerp(0.6f, 1f, near) * mult * cm, false, true, 0f, _move);
                _hits++;
                if (!u.isAlive())
                {
                    continue;
                }
                bool doomed = MeteorSlam.AtDeathsDoor(u);
                if (near > 0.4f)
                {
                    u.addStatusEffect("stunned", _level >= 3 ? 1.2f : 0.8f, false);
                }
                float dist = (_level >= 3 ? Mathf.Lerp(2f, 3.5f, near) : Mathf.Lerp(1.2f, 2f, near)) * (_god ? 1.3f : 1f);
                float apex = _level >= 3 ? Mathf.Lerp(1.5f, 2.2f, near) : Mathf.Lerp(0.8f, 1.2f, near);
                Combat.Launch(u, _to, dist, apex, (Random.value < 0.5f ? -1f : 1f) * Random.Range(540f, 900f));
                if (doomed)
                {
                    MeteorSlam.Doom(_by, u, _move);
                }
            }
        }

        private void Effects(WorldTile tile)
        {
            Vector2 at = _to;
            if (_water)
            {
                Anim.Sound("event:/SFX/DROPS/DropWaterBomb", tile);
                if (_level >= 3)
                {
                    Anim.Sound("event:/SFX/POWERS/WaterBomb", tile);
                }
                HeavenArt.Splash(at, _px, _k, tile, _level, _god, _pal);
                if (_level >= 3)
                {
                    Kit.Shake(at, _god ? 0.6f : 0.4f, _god ? 0.026f : 0.014f, _k);
                }
                return;
            }
            switch (_level)
            {
                case 1:
                    Anim.Sound("event:/SFX/HIT/HitGeneric", tile);
                    break;
                case 2:
                    Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
                    Anim.Sound("event:/SFX/HIT/HitStone", tile);
                    break;
                default:
                    Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
                    if (_god)
                    {
                        Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
                    }
                    break;
            }
            HeavenArt.CrashLand(at, _px, _k, tile, _pal, _level, _tier, _move is HeavenSplitter s && s.Crit, _areaR);
            if (_level == 2)
            {
                Kit.Shake(at, 0.25f, 0.01f, _k);
            }
            else if (_level >= 3)
            {
                Kit.Shake(at, _god ? 0.7f : _grand ? 0.5f : 0.45f, _god ? 0.032f : _grand ? 0.022f : 0.018f, _k);
                Kit.Stop(at, _god ? 0.12f : 0.08f);
            }
        }

        /// <summary>
        /// The world takes it (Kit.Ravage, on the crash frame): I leaves the ground alone; II a crater
        /// (force 2); III a burning one (force 3; a Grandmaster's wider); a God's force 4 and half as
        /// wide again, with fissures running out of it. Water only splashes.
        /// </summary>
        private void Devastate(WorldTile tile)
        {
            if (_water || _level <= 1 || tile == null)
            {
                return;
            }
            int force = _level == 2 ? 2 : _god ? 4 : 3;
            float r = (_level == 2 ? 1.6f : 2.2f) * MeteorSlam.RavageScale(_by) * Mathf.Sqrt(_vk);
            // (never the striker's own feet, if the flight came straight back down beside it)
            Vector2? keep = null;
            if (Credit && _by.isAlive() && Vector2.Distance(_by.current_position, _to) < r + 1.5f)
            {
                keep = _by.current_position;
            }
            Kit.Ravage(_to, r, force, _by, _level >= 3, keep, keep.HasValue ? SingleKit.KeepR : 0f);
            if (_god)
            {
                Fissures(r);
            }
            if (Dev.On) Dev.Trace("splitter flight " + _vid + " ravage force " + force + " r " + r.ToString("F1"));
        }

        /// <summary>A God's crash breaks the land: four fissures run out of the crater, ending at water, a cliff or the map's edge.</summary>
        private void Fissures(float craterR)
        {
            float a0 = Random.value * Mathf.PI * 2f;
            float d0 = craterR * 0.7f, d1 = craterR * 2.6f;
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = a0 + i * Mathf.PI * 0.5f + Random.Range(-0.35f, 0.35f);
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    float wob = 0f;
                    for (float d = d0; d <= d1; d += 1.1f)
                    {
                        wob = Mathf.Clamp(wob + Random.Range(-0.4f, 0.4f), -0.9f, 0.9f);
                        Vector2 p = _to + dir * d + side * wob;
                        WorldTile t = Combat.TileAt(p);
                        if (t == null || t.Type == null || t.Type.liquid || t.Type.block || p.x < 1f || p.y < 1f
                            || p.x > MapBox.width - 2 || p.y > MapBox.height - 2)
                        {
                            break;
                        }
                        float f = (d - d0) / Mathf.Max(0.1f, d1 - d0);
                        Kit.Ravage(p, 1f, f > 0.6f ? 1 : 2, _by, false);
                        if (Kit.Visible(p))
                        {
                            Kit.Crater(p, _px * _k, t, f < 0.45f, f < 0.45f ? 9f : 6f, true, _pal.Edge);
                        }
                    }
                }
            }
            finally
            {
                Kit.EndTrench(_by);
            }
        }

        // ----------------------------------------------------------- water --

        /// <summary>Under the splash for a moment; then it surfaces (and swims for it, reeling).</summary>
        private void Plunge()
        {
            Pose vp = _p.Pose;
            float k = Mathf.Clamp01(_pt / 0.26f);
            vp.Ground = _to;
            vp.Height = 0f;
            vp.Hidden = k < 0.85f;
            vp.Rot = 0f;
            _rot = 0f;
            vp.SX = 1f;
            vp.SY = Mathf.Lerp(0.8f, 1f, k);
            vp.Nudge = Vector2.zero;
            _p.Pose = vp;
            if (k >= 1f)
            {
                Next(Stage.Settle);
            }
        }

        // ----------------------------------------------------------- bounce --

        /// <summary>It bounces out of its crater, turning over: onto its feet (or, doomed, onto its back).</summary>
        private void Bounce()
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                _rotFrom = _rot;
                // backwards to the next upright angle; a doomed one lands on its back
                float upright = _rotFrom - Mathf.Repeat(_rotFrom, 360f);
                _rotTo = upright - (Owed ? 85f : 10f);
                if (_level >= 2 && Visible(_to))
                {
                    Fx.LandingPuff(_to, _px, Combat.TileAt(_to));
                }
            }
            float k = Mathf.Clamp01(_pt / _tBounce);
            vp.Ground = _to;
            vp.Height = _bounceH * Mathf.Sin(Mathf.PI * k);
            _rot = Mathf.Lerp(_rotFrom, _rotTo, Anim.OutQuad(k));
            vp.Rot = _rot;
            float amp = Anim.Spring(_pt, 10f, 20f);
            vp.SX = 1f + 0.18f * amp;
            vp.SY = 1f - 0.2f * amp;
            vp.Nudge = Vector2.zero;
            vp.Hidden = false;
            vp.HideShadow = vp.Height > 0.05f;
            _p.Pose = vp;
            if (k >= 1f)
            {
                if (Visible(_to))
                {
                    Fx.LandingPuff(_to, _vpx, Combat.TileAt(_to));
                }
                Next(Stage.Settle);
            }
        }

        // ----------------------------------------------------------- settle --

        /// <summary>
        /// Back on its feet, crumpled and swaying; then it is let go, stunned (the game tips it over).
        /// A doomed one is finished as it lies still; it is let go by its own death.
        /// </summary>
        private void Settle()
        {
            Pose vp = _p.Pose;
            if (Enter())
            {
                // (it bounced out leaning back 10 degrees: the settle starts there)
                _settleFrom = _water ? 0f : -10f;
                if (Owed)
                {
                    if (_water)
                    {
                        _rot = -85f;   // a body floats on its back
                    }
                    OwedKill();
                    if (!Combat.Living(_v))
                    {
                        Next(Stage.Dying);
                        return;
                    }
                    Owed = false;   // (it lived through it after all: a duel's floor, a shield): it gets up
                    _settleFrom = _water ? 0f : -85f;
                }
            }
            float k = Mathf.Clamp01(_pt / _tSettle);
            vp.Ground = _to;
            vp.Height = 0f;
            vp.Rot = Mathf.Lerp(_settleFrom, 0f, Anim.OutQuad(k));
            vp.SX = Mathf.Lerp(_water ? 1f : 1.18f, 1f, Anim.OutQuad(k));
            vp.SY = Mathf.Lerp(_water ? 0.9f : 0.78f, 1f, Anim.OutQuad(k));
            vp.Nudge = k < 0.7f ? new Vector2(Tremble, 0f) : Vector2.zero;
            vp.Hidden = false;
            vp.HideShadow = false;
            _p.Pose = vp;
            if (k >= 1f)
            {
                vp.Rot = 0f;
                vp.SX = vp.SY = 1f;
                vp.Nudge = Vector2.zero;
                _p.Pose = vp;
                Release();
            }
        }

        /// <summary>A doomed one lies still on its back until its death lets it go (or, after a while, it is let go anyway).</summary>
        private void Dying()
        {
            Pose vp = _p.Pose;
            vp.Ground = _to;
            vp.Height = 0f;
            vp.Rot = _rot;
            vp.SX = vp.SY = 1f;
            vp.Nudge = Vector2.zero;
            vp.Hidden = false;
            vp.HideShadow = false;
            _p.Pose = vp;
            if (_pt > 1.5f)
            {
                Release();
            }
        }

        /// <summary>The kill the blow owed: enough to end it through any armor, at any Power setting.</summary>
        private void OwedKill()
        {
            if (!Combat.Living(_v) || !Credit)
            {
                return;   // (no one left to credit: it lives, barely)
            }
            Combat.Hit(_by, _v, 0f, true, false, 99999f, _move);
            if (Combat.Living(_v) && Duels.TechShare(_by, _v) >= 1f && Combat.Hostile(_by, _v))
            {
                // armor of 100 or more: the game still deals 1 through it (never on a friend: a peace was made mid-flight)
                _v.getHit(_v.getHealth() * 10f + 100f, false, AttackType.Weapon, _by, false, false, false);
            }
            if (Dev.On) Dev.Trace("splitter flight " + _vid + (Combat.Living(_v) ? " survives its fall" : " dies where it came down"));
        }

        /// <summary>Hands it back to the game, on its feet and reeling (a short daze in the water: it swims for it).</summary>
        private void Release()
        {
            if (!_p.Finished)
            {
                _p.Release(true);
            }
            if (Combat.Living(_v) && _v.Sid() == _vid)
            {
                Combat.Daze(_v, _water ? 0.3f : _daze);
            }
            End();
        }

        private void End()
        {
            Over = true;
            KillFx();
        }

        /// <summary>The flight threw: let it go (in the air it falls on its own).</summary>
        private void Fail()
        {
            try
            {
                if (_p != null && !_p.Finished)
                {
                    Pose vp = _p.Pose;
                    vp.Hidden = false;
                    _p.Pose = vp;
                    _p.Release(vp.Height <= 0.02f);
                }
            }
            catch
            {
                // nothing more to do
            }
            Lost = true;
            End();
        }

        // ------------------------------------------------------------ camera --

        /// <summary>A God's cross-map launch with "camera follows god abilities" on: the camera glides to where it comes down.</summary>
        private void FollowCamera()
        {
            if (_follow < 0f)
            {
                return;
            }
            _follow += Time.unscaledDeltaTime;
            if (_follow > 2.5f || MoveCamera.hasFocusUnit())
            {
                _follow = -1f;
                return;
            }
            try
            {
                MoveCamera mc = World.world != null ? World.world.move_camera : null;
                if (mc == null)
                {
                    return;
                }
                Transform t = mc.transform;
                Vector3 p = t.position;
                Vector2 want = _to + new Vector2(0f, 1.5f);
                float f = 1f - Mathf.Exp(-4f * Mathf.Min(0.1f, Time.unscaledDeltaTime));
                t.position = new Vector3(Mathf.Lerp(p.x, want.x, f), Mathf.Lerp(p.y, want.y, f), p.z);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Sakuga] splitter camera: " + e.Message);
                _follow = -1f;
            }
        }

        // ---------------------------------------------------- shadow, marker --

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (_p.Pose.PivotUp / 3.5f)));
        }

        private void MakeShadow()
        {
            if (_shadow != null)
            {
                return;
            }
            _shadow = Fx.Spawn(Art.Disk(ShadowRx(1f), 0.42f, 0.45f), _p.Pose.Ground, _vpx, 1f, FxLayer.Ground, 3);
            if (_shadow != null)
            {
                _shadow.Held = true;
                _shadow.Snap = true;
                _shadow.Color(new Color(0f, 0f, 0f, 0.34f));
            }
        }

        /// <summary>Its own shadow on the ground while it flies (the game's is hidden): shrinking and fading with height.</summary>
        private void UpdateShadow()
        {
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            Pose vp = _p.Pose;
            bool show = !vp.Hidden && _stage != Stage.Fall && _stage <= Stage.Rise;
            float h = Mathf.Clamp01(vp.Height / Mathf.Max(1f, _rocket ? 8f : _apex));
            Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, 0.35f, h)), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != s)
            {
                _shadow.R.sprite = s;
                _shadow.Frames[0] = s;
            }
            _shadow.Pos = vp.Ground;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, show ? Mathf.Lerp(0.34f, 0.08f, h) : 0f);
            if (_stage >= Stage.Crash)
            {
                _shadow.Kill();
                _shadow = null;
            }
        }

        /// <summary>
        /// Where it will come down: a shadow growing on the ground and a ring closing on the spot,
        /// the size of the crash (II in the tier's colour; III flickering white and tier).
        /// </summary>
        private void Marker(float total)
        {
            if (_marked)
            {
                return;
            }
            _marked = true;
            total = Mathf.Max(0.05f, total);
            int shadowMax = Mathf.Max(2, Mathf.RoundToInt(3.6f * (_p.Pose.PivotUp / 3.5f)));
            Vector2 to = _to;
            float vpx = _vpx;
            _mark = Fx.Spawn(Art.Disk(1, 0.42f, 0.5f), to, vpx, total, FxLayer.Ground, 2);
            if (_mark != null)
            {
                _mark.Held = true;
                _mark.Snap = true;
                _mark.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Disk(Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, shadowMax, k * k))), 0.42f, 0.5f);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.08f, 0.42f, k));
                };
            }
            if (_areaR <= 0f)
            {
                return;
            }
            float r0 = Mathf.Clamp(_areaR / _px, 6f, 120f);
            float r1 = Mathf.Max(3f, r0 * 0.25f);
            int level = _level;
            Color tint = _pal.Edge;
            int thick = level >= 3 ? 2 : 1;
            int dashes = level >= 3 ? 14 : 10;
            _markRing = Fx.Spawn(Art.Ring(Mathf.RoundToInt(r0), thick, Art.Ground, dashes), to, _px, total, FxLayer.Ground, 3);
            if (_markRing != null)
            {
                _markRing.Held = true;
                _markRing.Snap = true;
                _markRing.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Ring(Mathf.RoundToInt(Mathf.Lerp(r0, r1, Anim.InOutCubic(k))), thick, Art.Ground, dashes);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    float alpha = Mathf.Lerp(0f, level >= 3 ? 0.95f : 0.8f, Mathf.Clamp01(k * 3f));
                    Color c = tint;
                    if (level >= 3 && ((int)(q.Age * Mathf.Lerp(10f, 26f, k)) & 1) == 0)
                    {
                        c = Color.Lerp(tint, Color.white, 0.6f);   // a flicker, faster as it comes
                    }
                    q.R.color = Fx.WithA(c, alpha);
                };
            }
        }

        private void KillMarker()
        {
            _mark?.Kill();
            _markRing?.Kill();
            _mark = _markRing = null;
        }

        private void KillFx()
        {
            KillMarker();
            _shadow?.Kill();
            _shadow = null;
        }
    }

    /// <summary>Heaven Splitter's own pixel art: the wind ribbons, the launch column, the crash, the splash.</summary>
    internal static class HeavenArt
    {
        /// <summary>The wind of the clip: cream ribbons with a white highlight.</summary>
        public static readonly Color Cream = new Color(0.97f, 0.91f, 0.78f, 1f);
        public static readonly Color CreamHi = new Color(1f, 0.98f, 0.93f, 1f);
        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);
        private static readonly Color Fire1 = new Color(1f, 0.84f, 0.32f, 1f);
        private static readonly Color Fire2 = new Color(1f, 0.48f, 0.12f, 1f);

        /// <summary>How flat a ribbon's ellipse is (a ring round a body, seen from above and in front).</summary>
        public const float RibbonRatio = 0.5f;

        /// <summary>The wind's colours over a ribbon's life: I white cooling to grey, II the tier's, III cream cooling into the tier's.</summary>
        public static void Wind(int level, Palette pal, out Color head, out Color body, out Color tail)
        {
            if (level <= 1)
            {
                head = new Color(1f, 1f, 1f, 0.9f);
                body = Fx.WithA(pal.Edge, 0.7f);
                tail = Fx.WithA(pal.Edge, 0f);
            }
            else if (level == 2)
            {
                head = Fx.WithA(pal.Core, 0.95f);
                body = Fx.WithA(pal.Edge, 0.85f);
                tail = Fx.WithA(pal.Edge, 0f);
            }
            else
            {
                head = CreamHi;
                body = Fx.WithA(Cream, 0.9f);
                tail = Fx.WithA(pal.Edge, 0f);
            }
        }

        /// <summary>
        /// A wind ribbon: a tapered band whipping almost half way round an axis at 'center' (a
        /// crescent on a flat ellipse, Art.Arc), 'r' texels out; its near (front) half drawn over
        /// the bodies, its far half behind them. 'ccw': the sense the wind turns in.
        /// </summary>
        public static Particle Ribbon(Vector2 center, float px, float r, bool front, bool ccw, int thick, float life, Color head, Color body, Color tail, float delay = 0f)
        {
            // (radii in steps of four texels, four sweeps, three widths: a few dozen cached arcs in all)
            int ri = Mathf.Clamp(Mathf.RoundToInt(r * 0.25f) * 4, 8, 64);
            float a0, a1;
            if (front)
            {
                a0 = ccw ? 196f : 344f;
                a1 = ccw ? 344f : 196f;
            }
            else
            {
                a0 = ccw ? 16f : 164f;
                a1 = ccw ? 164f : 16f;
            }
            Particle p = Fx.Spawn(Art.Arc(ri, RibbonRatio, a0, a1, Mathf.Clamp(thick, 1, 3)), center, px, life, front ? FxLayer.Over : FxLayer.Behind, front ? 6 : 2);
            if (p == null)
            {
                return null;
            }
            p.Delay = delay;
            p.Color(head, body, tail);
            return p;
        }

        private static readonly Particle[] _band = new Particle[2];

        /// <summary>
        /// A whole turn of wind round an axis: the near half sweeps across in front of the bodies, and
        /// as its head gets round, the far half carries on behind them. (The two particles are returned
        /// in a shared array: use them at once.)
        /// </summary>
        public static Particle[] Band(Vector2 center, float px, float r, bool ccw, int thick, float life, Color head, Color body, Color tail, float delay = 0f)
        {
            _band[0] = Ribbon(center, px, r, true, ccw, thick, life, head, body, tail, delay);
            _band[1] = Ribbon(center, px, r, false, ccw, Mathf.Max(1, thick - 1), life * 0.9f, head, body, tail, delay + life * 0.42f);
            return _band;
        }

        /// <summary>
        /// III: the launch point as the body leaves it: a white-hot ring on the ground and the tier's
        /// behind it, dust blown flat, a funnel of flat wind rings bursting round the column as the
        /// body passes each height, bands of cream wind whipping round the column (and the striker
        /// standing in it) for half a second after it, and a pillar of speed lines. 'scale' grows it.
        /// </summary>
        public static void LaunchColumn(Vector2 at, float px, float k, float scale, Palette pal, WorldTile tile, float riseTime, float h0, float apex, bool tall)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Wind(3, pal, out Color head, out Color body, out Color tail);
            float s = k * scale;
            float up = k * (tall ? 1.3f : 1f);
            Fx.Ring(at, px, 0.3f, 3f, 24f * s, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.8f,
                Fx.WithA(White, 1f), Fx.WithA(pal.Core, 0.85f), Fx.WithA(pal.Edge, 0f));
            Particle r2 = Fx.Ring(at, px, 0.45f, 4f, 36f * s, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f,
                Fx.WithA(pal.Edge, 0.9f), Fx.WithA(pal.Edge, 0f));
            if (r2 != null)
            {
                r2.Delay = 0.06f;
            }
            Kit.DustRing(at, px, tile, 14, 55f * scale, 0.6f, k);
            // the funnel: each ring bursts as the body passes its height, wider the higher it is
            float[] hs = { 1.2f, 3f, 5.4f, 8.4f };
            for (int i = 0; i < hs.Length; i++)
            {
                float h = hs[i] * up;
                if (h >= apex)
                {
                    break;
                }
                // when the body passes this height (the rise eases out: OutQuad inverted)
                float frac = Mathf.Clamp01((h - h0) / Mathf.Max(0.1f, apex - h0));
                float t = (1f - Mathf.Sqrt(1f - frac)) * riseTime;
                Vector2 c = at + new Vector2(0f, h);
                if (!Fx.OnScreen(c, 0.15f))
                {
                    continue;
                }
                Particle ring = Fx.Ring(c, px, 0.34f + 0.05f * i, 3f, (12f + 6f * i) * s, i == 0 ? 2 : 1, 1, FxLayer.Top, 4, 0.3f, 2.4f,
                    Fx.WithA(head, 1f), Fx.WithA(body, 0.8f), Fx.WithA(pal.Edge, 0f));
                if (ring != null)
                {
                    ring.Delay = t;
                }
            }
            // the wind whips round the launch point after it, climbing the column
            float[] bh = { 0.35f, 1.4f, 2.8f, 0.9f, 4.2f, 2f, 5.6f };
            int n = Fx.Count(bh.Length);
            for (int i = 0; i < n; i++)
            {
                float r = (14f + 2.2f * bh[i] * 3f) * s;
                Band(at + new Vector2(0f, bh[i] * up), px, r, true, i % 3 == 0 ? 3 : 2, Random.Range(0.28f, 0.36f), head, body, tail, 0.02f + i * 0.07f);
            }
            Kit.SpeedLines(at + new Vector2(0f, 6f * px), Vector2.up, px, 10, 5f * s, 0.24f, Fx.WithA(head, 0.9f));
        }

        /// <summary>
        /// Where it hits the ground. I: a puff and a ring of dust (no decal). II: a flash, a crater,
        /// a ring and a wall of dust, rocks. III: the impact frame behind it, a glowing crater with
        /// broken ground round it, a white-hot ring and the tier's, a ground wave, a dust wall, a
        /// column of smoke, rocks, sparks, embers. A God's half as big again.
        /// </summary>
        public static void CrashLand(Vector2 at, float px, float k, WorldTile tile, Palette pal, int level, Tier tier, bool crit, float areaR)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color soil = Fx.SoilColor(tile);
            Color dust = Fx.Lighten(soil, 0.45f);
            Color top = Fx.TopColor(tile);
            if (level <= 1)
            {
                Fx.LandingPuff(at, px, tile);
                Fx.Ring(at, px, 0.28f, 3f, 12f * k, 1, 1, FxLayer.Ground, 6, Art.Ground, 3f, Fx.WithA(White, 0.85f), Fx.WithA(pal.Edge, 0f));
                Kit.DustRing(at, px, tile, 12, 42f, 0.5f, k);
                return;
            }
            bool god = tier == Tier.God;
            float ring = Mathf.Max(8f, areaR / px);
            if (level == 2)
            {
                Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(7f * k), 0.8f, 0.4f), at + new Vector2(0f, 2f * px), px, 0.08f, FxLayer.Behind, 8);
                flash?.Sized(0.8f, 1.2f).Color(White, Fx.WithA(pal.Edge, 0.7f), Fx.WithA(pal.Edge, 0f));
                Kit.Crater(at, px * k, tile, false, 8f, true);
                Fx.Ring(at, px, 0.36f, 4f, ring, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.8f,
                    Fx.WithA(White, 0.95f), Fx.WithA(pal.Core, 0.7f), Fx.WithA(pal.Edge, 0f));
                Particle r2 = Fx.Ring(at, px, 0.6f, 3f, ring * 1.3f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                    Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
                if (r2 != null)
                {
                    r2.Delay = 0.05f;
                }
                Kit.DustRing(at, px, tile, 16, 60f, 0.6f, k);
                MeteorSlam.Rocks(at, px, soil, top, 8, k, 0.9f);
                Column(at, px, k, dust, 2, 1.1f);
                return;
            }
            float s = k * (god ? 1.5f : tier == Tier.Grandmaster ? 1.15f : 1f);
            // the impact frame behind the body planted head first in it
            MeteorSlam.ImpactFrame(at + new Vector2(0f, 3f * px), px * s, god ? 1.5f : 1.1f, pal, crit);
            Particle glare = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(ring * 0.6f), 6, 140), Art.Ground, 0.45f), at, px, 0.13f, FxLayer.Ground, 4);
            glare?.Sized(0.9f, 1.1f).Color(Fx.WithA(White, 0.8f), Fx.WithA(pal.Edge, 0.5f), Fx.WithA(pal.Edge, 0f));
            Kit.Crater(at, px * s, tile, true, 20f, false, pal.Edge);
            int side = god ? 5 : 3;
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < side; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / side + Random.Range(-0.4f, 0.4f);
                Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Random.Range(1.4f, 2.1f) * s;
                WorldTile t = Combat.TileAt(at + off);
                if (t != null && t.Type != null && !t.Type.liquid && !t.Type.block)
                {
                    Kit.Crater(at + off, px * s, t, false, 18f, true);
                }
            }
            Fx.Ring(at, px, 0.3f, 6f, ring * 0.7f, 3, 1, FxLayer.Ground, 7, Art.Ground, 3f,
                Fx.WithA(White, 1f), Fx.WithA(pal.Core, 0.85f), Fx.WithA(pal.Edge, 0f));
            Particle t2 = Fx.Ring(at, px, 0.42f, 8f, ring, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.6f,
                Fx.WithA(pal.Core, 0.95f), Fx.WithA(pal.Edge, 0.75f), Fx.WithA(pal.Edge, 0f));
            if (t2 != null)
            {
                t2.Delay = 0.08f;
            }
            Fx.Ring(at, px, 0.5f, 6f, Mathf.Min(140f, ring * 1.6f), 3, 2, FxLayer.Ground, 5, Art.Ground, 1.6f,
                Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            int puffs = Fx.Count(god ? 36 : 24);
            float reach = areaR * 1.5f / 0.4f;
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
            Column(at, px, s, dust, god ? 6 : 4, 1.4f);
            MeteorSlam.Rocks(at, px, soil, top, god ? 22 : 14, s, 1.2f);
            int sparks = Fx.Count(god ? 18 : 12);
            for (int i = 0; i < sparks; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at + new Vector2(0f, 2f * px), px, Random.Range(0.24f, 0.36f), FxLayer.Top, 8);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(130f, 200f) * px * s, Random.Range(20f, 90f) * px, 5f, 60f * px);
                p.FaceVel = true;
                p.Color(White, Fx.WithA(Fire1, 1f), Fx.WithA(Fire2, 0f));
            }
            int embers = Fx.Count(god ? 18 : 12);
            for (int i = 0; i < embers; i++)
            {
                float sideX = Random.value < 0.5f ? -1f : 1f;
                Vector2 off = new Vector2(sideX * Random.Range(3.5f, 10f) * s, Random.Range(-2f, 2.5f));
                Kit.Ember(at + off * px, px, Random.Range(0.9f, 1.8f), new Vector2(Random.Range(-10f, 10f), Random.Range(18f, 40f)) * px, pal.Edge);
            }
        }

        /// <summary>A column of dust and smoke rising behind the crater (behind the body: the landing in front of it reads).</summary>
        private static void Column(Vector2 at, float px, float k, Color dust, int count, float lift)
        {
            int n = Fx.Count(count);
            for (int i = 0; i < n; i++)
            {
                Color c = i % 2 == 0 ? dust : Smoke;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(3, 6)], at + new Vector2(Random.Range(-4f, 4f), Random.Range(0f, 2f)) * px, px, Random.Range(1f, 1.5f), FxLayer.Behind, 6);
                p?.Moving(new Vector2(Random.Range(-4f, 4f), 0f) * px, Random.Range(12f, 22f) * px * k * lift, 1.4f)
                    .Sized(0.7f, 1.25f)
                    .Color(Fx.WithA(c, 0.85f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>
        /// The giant splash: a flash on the water, a dome of white water swelling out, a geyser
        /// thrown straight up and falling back, a crown of jets all round, droplets flung far, rings
        /// racing out over the surface, mist hanging after. I a small one, II a big one, III a giant
        /// one (a geyser three tiles high), a God's twice that.
        /// </summary>
        public static void Splash(Vector2 at, float px, float k, WorldTile tile, int level, bool god, Palette pal)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color water = tile != null && tile.main_type != null ? tile.main_type.color : new Color(0.2f, 0.45f, 0.8f, 1f);
            water.a = 1f;
            Color spray = Color.Lerp(water, White, 0.55f);
            Color foam = Color.Lerp(water, White, 0.85f);
            float s = k * (level <= 1 ? 0.6f : level == 2 ? 1f : god ? 2.1f : 1.45f);
            // the geyser's height (tiles)
            float tall = level <= 1 ? 0.7f : level == 2 ? 1.6f : god ? 6f : 3f;
            Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(8f * s), Art.Ground, 0.4f), at, px, 0.1f, FxLayer.Ground, 8);
            flash?.Sized(0.7f, 1.2f).Color(White, Fx.WithA(foam, 0.8f), Fx.WithA(spray, 0f));
            if (level >= 2)
            {
                // the dome: white water swelling out of the impact and sinking back
                Particle dome = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(11f * s), 4, 140), Art.Ground, 0.35f), at, px, 0.7f, FxLayer.Ground, 5);
                dome?.Sized(0.35f, 1.6f).Color(Fx.WithA(foam, 0.95f), Fx.WithA(spray, 0.75f), Fx.WithA(water, 0f));
            }
            Fx.Ring(at, px, 0.5f, 3f, 26f * s, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(foam, 1f), Fx.WithA(spray, 0.7f), Fx.WithA(water, 0f));
            Particle r2 = Fx.Ring(at, px, 0.75f, 3f, 40f * s, 1, 1, FxLayer.Ground, 6, Art.Ground, 1.8f, Fx.WithA(foam, 0.9f), Fx.WithA(spray, 0.5f), Fx.WithA(water, 0f));
            if (r2 != null)
            {
                r2.Delay = 0.12f;
            }
            if (level >= 3)
            {
                Particle r3 = Fx.Ring(at, px, 0.95f, 4f, Mathf.Min(140f, 56f * s), 1, 1, FxLayer.Ground, 6, Art.Ground, 1.6f, Fx.WithA(foam, 0.8f), Fx.WithA(spray, 0.4f), Fx.WithA(water, 0f));
                if (r3 != null)
                {
                    r3.Delay = 0.28f;
                }
            }
            // the wave: a wall of white water rolling out over the surface (the dust wall of a crater, in foam)
            int wall = Fx.Count(level <= 1 ? 6 : level == 2 ? 14 : god ? 34 : 24);
            float roll = 26f * s * px * 2.2f;
            for (int i = 0; i < wall; i++)
            {
                float a = (i + Random.Range(-0.35f, 0.35f)) / wall * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + dir * 4f * px * s, px, Random.Range(0.7f, 0.95f), dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * roll * Random.Range(0.85f, 1.1f), Random.Range(3f, 8f) * px, 2.2f)
                    .Sized(0.5f, 1.4f)
                    .Color(Fx.WithA(foam, 0.95f), Fx.WithA(spray, 0.7f), Fx.WithA(spray, 0.3f), Fx.WithA(spray, 0f));
            }
            // the geyser: a column of white water thrown straight up, falling back (soft blobs, not
            // the shaded puffs: shaded, the column read as a heap of grey rocks)
            float g = 26f * Mathf.Max(1f, Mathf.Sqrt(tall));
            int col = Fx.Count(level <= 1 ? 4 : level == 2 ? 8 : god ? 22 : 15);
            for (int i = 0; i < col; i++)
            {
                float h = tall * k * Mathf.Lerp(0.25f, 1f, (i + Random.value) / col);
                float vz = Mathf.Sqrt(2f * g * h);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + new Vector2(Random.Range(-2f, 2f) * s, Random.Range(-1f, 1f)) * px, px, 2f * vz / g + 0.12f,
                    i % 3 == 0 ? FxLayer.Behind : FxLayer.Over, 5);
                if (p == null)
                {
                    break;
                }
                p.Delay = Random.Range(0f, 0.04f);
                p.Moving(new Vector2(Random.Range(-4f, 4f) * px * s, 0f), vz, 0f, g);
                p.Bounce = 0f;
                p.Sized(0.6f + 0.1f * s, 1.05f + 0.25f * s);
                p.Color(Fx.WithA(White, 1f), Fx.WithA(White, 0.95f), Fx.WithA(foam, 0.75f), Fx.WithA(foam, 0f));
            }
            // the crown: jets of water thrown up and out all round
            int jets = Fx.Count(level <= 1 ? 6 : level == 2 ? 12 : god ? 30 : 20);
            for (int i = 0; i < jets; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / jets * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                float h = tall * 0.5f * k * Random.Range(0.6f, 1f);
                float vz = Mathf.Sqrt(2f * g * h);
                Particle p = Fx.Spawn(Art.Puffs[Random.Range(0, 3)], at + dir * 4f * px * s, px, 2f * vz / g + 0.05f, dir.y < 0f ? FxLayer.Top : FxLayer.Behind, 5);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(1.2f, 2.4f) * tall * k * 0.5f, vz, 0.4f, g);
                p.Bounce = 0f;
                p.Sized(0.7f, 1.1f);
                p.Color(Fx.WithA(White, 1f), Fx.WithA(foam, 0.9f), Fx.WithA(foam, 0f));
            }
            // droplets flung far
            int drops = Fx.Count(level <= 1 ? 8 : level == 2 ? 20 : god ? 60 : 40);
            for (int i = 0; i < drops; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                float h = tall * k * Random.Range(0.3f, 0.9f);
                float vz = Mathf.Sqrt(2f * g * h);
                Particle p = Fx.Spawn(Art.Pixel, at + dir * 3f * px * s, px, 2f * vz / g + 0.05f, FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(1.5f, 3.5f) * tall * k * 0.5f, vz, 0f, g);
                p.Bounce = 0f;
                p.Sized(1.5f, 1f);
                p.Color(White, Fx.WithA(foam, 0.9f), Fx.WithA(spray, 0f));
            }
            // mist hanging over the water afterwards
            int mist = Fx.Count(level >= 3 ? (god ? 12 : 8) : 3);
            for (int i = 0; i < mist; i++)
            {
                Vector2 off = new Vector2(Random.Range(-8f, 8f), Random.Range(-2f, 4f)) * px * s;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], at + off, px, Random.Range(1.2f, 1.8f), FxLayer.Behind, 3);
                if (p == null)
                {
                    break;
                }
                p.Delay = Random.Range(0.1f, 0.35f);
                p.Moving(new Vector2(Random.Range(-4f, 4f), 0f) * px, Random.Range(4f, 9f) * px * s, 1f);
                p.Sized(0.8f, 1.5f);
                p.Color(Fx.WithA(foam, 0f), Fx.WithA(foam, 0.5f), Fx.WithA(foam, 0.25f), Fx.WithA(foam, 0f));
            }
            if (level >= 3)
            {
                Kit.Flare(at, px, pal.Edge, Mathf.Min(120f, 20f * s), 0.4f);
            }
        }
    }

    /// <summary>
    /// Heaven Splitter as a technique. Entry: Adept (I Adept, II Expert, III Master and up). The AI's
    /// single-target finisher for a strong enemy it can reach, best a lone one: never wasted on one
    /// an ordinary blow would end, less keen when the target stands in a crowd (it gets mobbed), into
    /// a ready guard, or on a giant it can only stagger; never (unless it is the chain's own target)
    /// on a plain foot soldier a couple of blows would end; keener on a tiered or dangerous one, on
    /// one caught mid-hop, right after its guard was spent. Chained from Close and Down (an uppercut
    /// that hauls a downed enemy up off the ground). Mana 22 / 42 / 72, cooldown 10 / 13 / 16 s.
    /// </summary>
    internal sealed class HeavenSplitterTech : Technique
    {
        public override string Id => "heaven_splitter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;

        /// <summary>A rising fist under the jaw that sends the enemy out of the scene.</summary>
        public override Form Forms => Form.Punch | Form.Rising | Form.Heavy | Form.High;

        public override Form FormsAt(int level) => level >= 2 ? Forms | Form.Dash : Forms;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => new[] { 22f, 42f, 72f };
        public override float[] Cooldown => new[] { 8f, 10f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        /// <summary>The uppercut (multiple of the user's damage).</summary>
        public static float Uppercut(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 2.3f : 4f;
        }

        /// <summary>Where it comes down: the landing (I), the crash (II, III).</summary>
        public static float Crash(int level)
        {
            return level <= 1 ? 0.25f : level == 2 ? 0.7f : 1.3f;
        }

        /// <summary>The crash catches those around it too (II, III).</summary>
        public static float Area(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 0.7f : 1.3f;
        }

        public static float Total(int level)
        {
            return Uppercut(level) + Crash(level);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;   // never one straight into another
            }
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, ref best, ref bestScore);
            if (!c.Forced)
            {
                float k = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
                float reach = HeavenSplitter.Reach(level) * k + 1f;
                for (int i = 0; i < c.Enemies.Count && i < 4; i++)
                {
                    if (c.EnemyDist[i] > reach)
                    {
                        break;
                    }
                    if (c.Enemies[i] != c.Focus)
                    {
                        Consider(c, c.Enemies[i], level, ref best, ref bestScore);
                    }
                }
            }
            else if (best == null && c.ForceTarget == null && c.Enemies.Count > 0)
            {
                Consider(c, c.Enemies[0], level, ref best, ref bestScore);
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
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        /// <summary>Dev: why candidates were turned down, by tier of the user.</summary>
        internal static readonly Dictionary<string, int> Probe = new Dictionary<string, int>();

        private static void Why(Ctx c, string why)
        {
            if (Dev.On)
            {
                string k = c.Tier + " " + why;
                Probe.TryGetValue(k, out int n);
                Probe[k] = n + 1;
            }
        }

        // (by the user's tier: none, Novice, Adept, Expert, Master, Grandmaster, God)
        private static readonly float[] WorthyBonus = { 20f, 20f, 60f, 70f, 150f, 500f, 850f };

        private void Consider(Ctx c, Actor v, int level, ref Actor best, ref float bestScore)
        {
            if (v == null || !Combat.Living(v))
            {
                return;
            }
            if (Moves.Locked(v))
            {
                Why(c, "locked");
                return;
            }
            if (!HundredFists.Hopping(v))
            {
                Why(c, "airborne");
                return;
            }
            if (v.isInLiquid() || MeteorSlam.IsDoomed(v))
            {
                Why(c, "water/doomed");
                return;
            }
            if (!c.Allowed(v, out float cm))
            {
                Why(c, "class");
                return;
            }
            if (!c.Forced && Director.InScene(v))
            {
                return;   // someone else's set piece
            }
            float k = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
            bool linkTarget = c.Linked && v == c.LinkTarget;
            float reach = HeavenSplitter.Reach(level) * k + v.stats["size"] * 0.5f + (linkTarget ? 0.8f : 0f);
            if (c.Forced)
            {
                reach = Mathf.Max(reach, HeavenSplitter.ForcedReach);
            }
            float d = c.Dist(v);
            if (d > reach || d > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
            {
                Why(c, "reach");
                return;   // out of reach, or a river or a wall between them
            }
            float hp = v.getHealth();
            if (!c.Forced && hp <= c.Expected(v, 1.2f) * cm)
            {
                Why(c, "one blow");
                return;   // one ordinary blow ends it: a finisher would be wasted on it
            }
            if (!c.Forced && !linkTarget && Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute && hp <= c.Expected(v, 2.2f) * cm)
            {
                Why(c, "foot soldier");
                return;   // a plain foot soldier a couple of blows will end: the finisher is for a strong foe
            }
            Why(c, "candidate");
            float score = c.Value(v, c.Expected(v, Total(level)), cm);
            // the right answer to a strong enemy on its own
            if (c.Isolated(v, 2f))
            {
                score += 12f + Mathf.Min(Tiers.Power(v), 8f) * 3f;
            }
            else
            {
                // in a crowd the whirl and the plant get mobbed: something else is the better answer
                int near = c.CountNear(v.current_position, 2f);
                score *= near >= 3 ? Mathf.Max(0.5f, 0.92f - 0.1f * (near - 2)) : 0.92f;
            }
            if (Tiers.Of(v) != Tier.None)
            {
                score += 10f;
            }
            // the finisher for a worthy foe: the stronger the enemy against the user, the more it calls
            // for the uppercut (a God's for a Grandmaster or a God, a Master's for an Expert or a
            // Master); scaled to the scores the user's other techniques reach at its tier
            if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Fiend)
            {
                float worth = Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0f, 1.5f);
                score += worth * WorthyBonus[Mathf.Clamp((int)c.Tier, 0, WorthyBonus.Length - 1)];
            }
            if (v.position_height > 0.05f)
            {
                score += 6f;   // caught mid-hop: the moment it can't slip away
            }
            if (!HundredFists.Catchable(v, c.Self) || Weights.Heavy(v))
            {
                score *= 0.7f;   // it can't be lifted: only a stagger
            }
            if (!c.Forced)
            {
                // the whole thing hangs on one blow: into a guard that is up it is a gamble; right
                // after that answer is spent, it is the punish
                int guard = c.Guard(v, FormsAt(level));
                if (guard > 0)
                {
                    score *= 0.65f;
                }
                else if (guard < 0)
                {
                    score += 20f;
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
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && HeavenSplitter.Begin(a, p.Target, p.Level, this, p.Data is bool forced && forced);
        }
    }
}
