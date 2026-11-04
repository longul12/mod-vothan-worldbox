using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// FLASH STEP. The anime "shunpo": the unit coils, vanishes, and is somewhere else, with a
    /// trail of afterimages to show the way it went. Level I is an honest dash and a jab; level II
    /// passes clean through its target and everyone in the way, and the cut lands a beat after it
    /// has gone by; level III zig-zags in three blinks, and the afterimages it leaves at the corners
    /// strike again on their own a beat after the cut.
    ///
    /// Beat sheet, level II (game seconds at x1):
    ///
    ///   CROUCH   0.00-0.08  squash 120% x 75%, lean back; two ground-tinted dust puffs kicked back
    ///                       from the heels
    ///   VANISH   0.08       one frame stretched 160% wide under a white flash; an afterimage stays
    ///                       on the spot and fades over 0.3 s. Intangible from here until it lands
    ///   BLUR     0.08-0.16  gone: three crisp afterimages along the path (white-hot heart, tier
    ///                       edge, alpha .5/.65/.8: the strongest nearest the end), five tier speed
    ///                       lines, a few low dust specks and a dark scuff line on the ground; a
    ///                       white hairline flicks through every enemy as the blur goes through it
    ///   SKID     0.16-0.26  reappears past the target, stretched and leaning back, skidding 0.3
    ///                       tiles on a trail of dust, its back to them. Never among them: in
    ///                       a cluster or a file it comes out past the last one, in the open
    ///   ZANSHIN  0.26-0.37  still. At 0.31 the delayed cut lands on everyone it passed: white
    ///                       flashes (tiered victims flash their own tier colour), slash lines
    ///                       (armed) or hit stars (unarmed), the whole trail lights up, a 0.05 s
    ///                       local hit-stop (the unit holds; the world goes on), then a 0.5-tile
    ///                       knockback (critters fly, giants stagger).
    ///                       If the target slipped the cut (Mirage Step) it whips round to face it
    ///                       instead of keeping its back turned
    ///   RECOVER  0.37-0.59  turns to face its target, springs upright; chain window (Close)
    ///
    ///   Level I    0.07 crouch, a visible 0.12 s slide (one afterimage, two white speed lines),
    ///              brake, lunge jab x0.8 with a single spark. No hit-stop, no decals. A Novice
    ///              now and then overshoots and goes head over heels instead (harmless).
    ///   Level III  three 0.05 s blinks in a +-35 deg zig-zag (wider, or straight, to get around
    ///              walls and water), an afterimage left standing at every corner; arrival "ting"
    ///              and a ring a tile wide; the main cut gets an impact frame and burns the path
    ///              into the ground (one white-hot line, no second one at body height), the big
    ///              hit star only on the target; the main cut holds a 0.1 s hit-stop. 0.16 s after
    ///              the cut each corner afterimage in turn fires a crescent at the nearest victim
    ///              (x0.5), 0.08 s apart, while the unit keeps its back turned. If the cut left no
    ///              one standing, the afterimages flash out at once in a quick ripple instead of
    ///              keeping it standing there for nothing. A target that mirage-stepped the cut is
    ///              hunted by them. About 1 s in all (0.75 s when the cut felled them).
    ///   Retreat    at 25% health or less with two enemies on it: the same dash, no blow, away
    ///              towards its allies, ending turned towards the danger.
    ///
    /// Power and tempo by tier: the cut is x1.7 at II and x2.4 at III (I's jab x0.9), on top of
    /// the tier's Might. The higher the tier, the less it lingers (TierInfo.Linger): the coil,
    /// the skid, the delayed cut, the echoes and the recovery all shrink. From Master up it never
    /// stands about: it turns away as soon as its cut has thrown them, its afterimages strike
    /// behind it during the recovery (a Master's III is over in ~0.55 s, a God's in ~0.5 s), it
    /// never swings after it (no attack cooldown: FlowStrikes takes over), and it always leaves
    /// a chain: Close on the one it reached or, if its cut felled them, Scattered towards the
    /// nearest enemy still standing. Victims in a light move (a master's flow of blows) freeze
    /// the moment the blur passes them: the flow is cut short, never a shield.
    ///
    /// The ground (the "Environment damage" setting), on the impact frame of the cut, grows with
    /// the one who made it: II, when it cut through a line of them, scorches the tile each one
    /// stood on (a single passing cut, a jab: nothing); III burns its path: an Expert a thin
    /// scorched line, a Master a band three tiles wide, a Grandmaster blasts it (the grass torn
    /// off the middle), a God gouges a trench (dug a step deeper, never into water, no fire);
    /// never under its own feet.
    ///
    /// Variants by circumstance: armed units leave slash lines, unarmed ones hit stars; when the
    /// far side of the target is unsafe the dash stops in front of it instead of passing through;
    /// crabs scuttle sideways (they never turn or lean into it); a target that dies on the way is
    /// swapped for another one within 2 tiles, or the move ends on a pose and a glint.
    /// Everything happens in move time (paused with the game, capped at high speeds): the effects
    /// along the path are scheduled at the moment the blur passes, so they read at any frame rate.
    /// </summary>
    internal sealed class FlashStep : Move
    {
        public const float MinRange = 2.5f;

        internal enum Kind
        {
            Strike,
            Retreat,
            Fumble
        }

        /// <summary>
        /// A planned dash: the blur's corners (Pts[0] the start, the last one where it reappears) and
        /// where the skid stops. Through: it ends past the target (II/III), not in front of it.
        /// </summary>
        internal sealed class Route
        {
            public Kind Kind;
            public Vector2[] Pts;
            public Vector2 Final;
            public bool Through;
            public Vector2 Face;
            public bool HasFace;
        }

        private sealed class Victim
        {
            public Actor U;
            public long Id;
            /// <summary>When the blur passes it (0..1 of the dash).</summary>
            public float F;
            /// <summary>Which way the cut throws it.</summary>
            public Vector2 Dir;
            public bool Main;
            public bool Cut;
            /// <summary>Gone, evaded, parried or too heavy: no more blows.</summary>
            public bool Out;
        }

        private struct EchoHit
        {
            public Actor U;
            public long Id;
            public float At;
            public bool Reached;
            public Vector2 Dir;
        }

        private enum Phase
        {
            Crouch,
            Dash,
            Arrive,
            Jab,
            Zanshin,
            Stumble,
            Recoil,
            Recover
        }

        private const float TCrouchI = 0.07f;
        private const float TCrouch = 0.08f;
        private const float TCrouchRetreat = 0.06f;
        private const float TSlide = 0.12f;
        private const float TBlink = 0.08f;
        private const float TSegment = 0.05f;
        private const float TBrake = 0.06f;
        private const float TSkid = 0.1f;
        private const float TJab = 0.15f;
        private const float TJabHit = 0.045f;
        private const float CutDelay = 0.15f;
        /// <summary>From the cut to the first echo (the afterimages strike once the cut has thrown them).</summary>
        private const float EchoLead = 0.16f;
        private const float EchoBeat = 0.08f;
        private const float EchoFly = 0.06f;
        private const float TRecover = 0.22f;
        private const float TRecoil = 0.3f;
        private const float TStumble = 0.34f;
        private const int MaxVictims = 12;

        /// <summary>Damage (x the attacker's damage, before Might): I's jab, II's and III's cut, III's echoes.</summary>
        internal const float JabMult = 0.9f;
        internal const float EchoMult = 0.5f;

        internal static float CutMult(int level)
        {
            return level >= 3 ? 2.4f : 1.7f;
        }

        /// <summary>
        /// How long a tier lingers between beats (1 = the full beat sheet): the more powerful, the
        /// fewer pauses (TierInfo.Linger: Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42).
        /// </summary>
        internal static float Tempo(Tier t)
        {
            TierInfo info = Tiers.Info(t);
            return info != null ? info.Linger : 1f;
        }

        /// <summary>Masters and up fight in flow: no swing of their own after a technique (FlowStrikes).</summary>
        internal static bool Flows(Tier t)
        {
            TierInfo info = Tiers.Info(t);
            return info != null && info.Flow;
        }

        private readonly Route _r;
        private readonly Actor _target;
        private readonly long _targetId;
        private readonly Weight _targetClass;
        private readonly Palette _pal;
        private readonly bool _crab, _armed, _blink;
        private readonly float _k, _tCrouch, _tDash;
        private readonly Vector2 _u, _mid;
        private readonly bool _baseFace;
        private readonly Color _ghostIdle;

        // the performer's tier sets the tempo of the settling beats and the weight of the blow
        private readonly Tier _tier;
        private readonly bool _flow;
        private readonly float _tSkid, _tBrake, _cutDelay, _echoLead, _echoBeat, _settle, _tRecover, _tRecoil;
        /// <summary>How hard its cut throws them (Grandmasters and Gods throw harder).</summary>
        private readonly float _push;
        private int _cutHits;

        private Phase _phase = Phase.Crouch;
        private float _pt;
        private bool _entered;
        private float _cutT = -1f;
        /// <summary>II: what is left of the cut's local hit-stop (the move holds; the world goes on).</summary>
        private float _hold;
        private bool _cutDone, _knockDone;
        private float _knockT = -1f;
        /// <summary>III: the cut left no one for the afterimages: they flash out at once instead of waiting.</summary>
        private bool _echoRush;
        private float _lastBeat;
        /// <summary>The victims the blur has gone by so far (they are sorted by when it passes them).</summary>
        private int _passed;
        private bool _parried;
        private Vector2 _parryFrom;
        /// <summary>Its target read the cut and slipped it (Mirage Step): it turns to find it.</summary>
        private bool _slipped, _whirled;
        private bool _slideGhost;
        private bool _turned;
        private float _rot0, _sx0 = 1f, _sy0 = 1f;
        private Vector2 _recoilFrom, _recoilTo;
        private bool _stumbleDust;
        private Vector2 _mainPos;

        // level I: the jab
        private Actor _jabV;
        private long _jabId = -1;
        private Vector2 _jabFrom, _jabDir;
        private bool _jabMove, _jabDone, _jabLanded;
        private int _jabStyle;

        private readonly List<Victim> _victims = new List<Victim>();
        private readonly List<EchoHit> _echoHits = new List<EchoHit>();

        // level III: the afterimages left standing at the corners, and their echo-strikes
        private readonly Particle[] _ghosts = new Particle[3];
        private readonly Vector2[] _ghostPos = new Vector2[3];
        private readonly float[] _ghostBorn = { -1f, -1f, -1f };
        private readonly float[] _ghostFire = { -1f, -1f, -1f };
        private readonly Vector2[] _ghostHome = new Vector2[3];
        private readonly Vector2[] _ghostLunge = new Vector2[3];
        private int _corners;
        private int _echoes;

        /// <summary>Dashes started since the game started (dev log).</summary>
        internal static int Started;

        private FlashStep(Actor a, Route r, Actor target, int level) : base(a)
        {
            Level = level;
            _r = r;
            _target = target;
            _targetId = target != null ? target.Sid() : -1L;
            _targetClass = target != null ? Weights.Of(target) : Weight.Folk;
            _k = K;
            _pal = Palette.For(a, level);
            _crab = IsCrab(a);
            _armed = Armed(a);
            _blink = level >= 2;
            Vector2 toward = r.Kind == Kind.Strike && target != null ? target.current_position : r.Final;
            Vector2 d = toward - r.Pts[0];
            _u = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(a.flip ? 1f : -1f, 0f);
            _mid = Vector2.Lerp(r.Pts[0], r.Final, 0.5f);
            _tier = Tiers.Of(a);
            _flow = Flows(_tier);
            float q = Tempo(_tier);
            // the higher the tier, the less it lingers: a Master's coil is two or three frames, its
            // blur a touch quicker, and the beats that only settle (skid, delayed cut, echoes,
            // recovery) shrink the most; the beats that read (the zanshin, the cut) keep their order
            float qh = 0.4f + 0.6f * q;
            _tCrouch = (r.Kind == Kind.Retreat ? TCrouchRetreat : level <= 1 ? TCrouchI : TCrouch) * q;
            _tCrouch = Mathf.Max(0.035f, _tCrouch);
            _tDash = (!_blink ? TSlide : r.Pts.Length >= 4 ? TSegment * (r.Pts.Length - 1) : TBlink) * Mathf.Lerp(1f, q, 0.5f);
            _tSkid = Mathf.Max(0.05f, TSkid * q);
            _tBrake = Mathf.Max(0.04f, TBrake * q);
            _cutDelay = CutDelay * qh;
            _echoLead = Mathf.Max(0.07f, EchoLead * q);
            _echoBeat = EchoBeat * qh;
            _settle = Mathf.Max(0.04f, 0.08f * q);
            _tRecover = Mathf.Max(0.08f, TRecover * q);
            _tRecoil = TRecoil * qh;
            _push = _tier >= Tier.God ? 1.5f : _tier >= Tier.Grandmaster ? 1.25f : 1f;
            // a crab never turns into its dash: it scuttles sideways
            if (!_crab && Mathf.Abs(_u.x) > 0.12f)
            {
                Pose.FaceRight = _u.x > 0f;
            }
            _baseFace = Pose.FaceRight;
            _ghostIdle = Fx.WithA(Color.Lerp(_pal.Edge, Color.white, 0.35f), 0.6f);
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>A unit's size relative to a human (same as Move.K, for units not performing).</summary>
        internal static float KOf(Actor a)
        {
            return Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        /// <summary>Crabs walk sideways, and dash sideways too.</summary>
        internal static bool IsCrab(Actor a)
        {
            string id = a != null && a.asset != null ? a.asset.id : null;
            return id == "crab" || id == "civ_crab";
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

        internal static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>How far past the target a pass-through ends (before the skid).</summary>
        internal static float Over(float ka, float kv)
        {
            return 0.5f + 0.2f * (ka + kv);
        }

        /// <summary>How far in front of the target a dash stops when it doesn't pass through.</summary>
        internal static float Front(float ka, float kv)
        {
            return 0.45f + 0.25f * (ka + kv);
        }

        internal static float SkidLength(float ka)
        {
            return 0.3f * ka;
        }

        /// <summary>Enemies this close to the blur's path get cut.</summary>
        internal static float PathWidth(float ka)
        {
            return 0.45f + 0.15f * ka;
        }

        /// <summary>A straight dash at v: through it and out the other side, or stopping in front of it.</summary>
        internal static Route Straight(Vector2 s, Actor v, float ka, bool through)
        {
            Vector2 t = v.current_position;
            Vector2 d = t - s;
            float dist = d.magnitude;
            if (dist < 0.3f)
            {
                return null;
            }
            Vector2 u = d / dist;
            float kv = KOf(v);
            if (through)
            {
                Vector2 end = t + u * Over(ka, kv);
                Vector2 fin = end + u * SkidLength(ka);
                if (!Combat.ClearPath(s, fin))
                {
                    return null;
                }
                return new Route { Kind = Kind.Strike, Pts = new[] { s, end }, Final = fin, Through = true };
            }
            float front = Front(ka, kv);
            if (dist - front < 0.8f)
            {
                return null;   // nothing to cross
            }
            Vector2 stop = t - u * front;
            if (!Combat.ClearPath(s, stop))
            {
                return null;
            }
            return new Route { Kind = Kind.Strike, Pts = new[] { s, stop }, Final = stop };
        }

        /// <summary>
        /// The level III zig-zag: three legs at +-deg to the line, the last one running through the
        /// target and out past it. The middle leg crosses back over the line, so the legs are about
        /// 1 : 1.7 : 1.1 long (2.1 / 3.7 / 2.4 tiles for a target 6 tiles away). 'side' picks the first turn.
        /// </summary>
        internal static Route Zig(Vector2 s, Actor v, float ka, int side, float deg)
        {
            Vector2 t = v.current_position;
            Vector2 d = t - s;
            float dist = d.magnitude;
            if (dist < 0.6f)
            {
                return null;
            }
            Vector2 u = d / dist;
            Vector2 n = new Vector2(-u.y, u.x) * side;
            float rad = deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            Vector2 d1 = u * cs + n * sn;    // the first and the last leg
            Vector2 d2 = u * cs - n * sn;    // the middle one
            float l2 = dist / (2f * cs);
            float l1 = 0.58f * l2;
            Vector2 c1 = s + d1 * l1;
            Vector2 c2 = c1 + d2 * l2;       // from here the last leg runs straight through the target
            Vector2 end = t + d1 * Over(ka, KOf(v));
            Vector2 fin = end + d1 * SkidLength(ka);
            if (!Combat.ClearPath(s, c1) || !Combat.ClearPath(c1, c2) || !Combat.ClearPath(c2, fin))
            {
                return null;
            }
            return new Route { Kind = Kind.Strike, Pts = new[] { s, c1, c2, end }, Final = fin, Through = true };
        }

        /// <summary>A dash that hits nothing (a retreat, a reposition) 'len' tiles along 'dir'.</summary>
        internal static Route Away(Vector2 s, Vector2 dir, float len, float ka)
        {
            if (dir.sqrMagnitude < 1e-6f)
            {
                return null;
            }
            Vector2 fin = Combat.Clamp(s + dir.normalized * len);
            Vector2 d = fin - s;
            float dist = d.magnitude;
            if (dist < 1.2f || !Combat.ClearPath(s, fin))
            {
                return null;
            }
            Vector2 end = fin - d / dist * SkidLength(ka);
            return new Route { Kind = Kind.Retreat, Pts = new[] { s, end }, Final = fin };
        }

        /// <summary>A Novice's slip: it overshoots the target and tumbles head over heels past it.</summary>
        internal static Route Fumble(Vector2 s, Actor v, float ka)
        {
            Vector2 t = v.current_position;
            Vector2 d = t - s;
            float dist = d.magnitude;
            if (dist < 1.5f)
            {
                return null;
            }
            Vector2 u = d / dist;
            Vector2 end = t + u * Over(ka, KOf(v));
            Vector2 fin = end + u * 0.7f * ka;
            if (!Combat.ClearPath(s, fin))
            {
                return null;
            }
            return new Route { Kind = Kind.Fumble, Pts = new[] { s, end }, Final = fin, Through = true };
        }

        /// <summary>How much further a pass-through may run to come out past a file of them (tiles).</summary>
        internal const float MaxExit = 4.5f;

        /// <summary>
        /// Moves the exit of a pass-through on past everyone standing where it would come out
        /// (the first 'na' of 'a', then the first 'nb' of 'b'; 'b' may be null), up to MaxExit
        /// tiles further, if there is clear ground there. The blur then cuts them on its way.
        /// </summary>
        internal static void PushExit(Route r, Actor v, float ka, List<Actor> a, int na, List<Actor> b, int nb)
        {
            int n = r != null && r.Pts != null ? r.Pts.Length : 0;
            if (n < 2 || !r.Through || r.Kind != Kind.Strike || v == null)
            {
                return;
            }
            Vector2 end = r.Pts[n - 1];
            Vector2 leg = end - r.Pts[n - 2];
            if (leg.sqrMagnitude < 1e-4f)
            {
                return;
            }
            Vector2 d = leg.normalized;
            Vector2 t = v.current_position;
            float skid = SkidLength(ka);
            float over = Vector2.Dot(end - t, d);
            float need = over;
            bool clear = false;
            for (int pass = 0; pass < 6 && need <= over + MaxExit; pass++)
            {
                // enemies, and its own side too (coming out on top of a friend buries it as well)
                bool moved = Crowd(a, na, v, t, d, ka, skid, ref need);
                moved |= Crowd(b, nb, v, t, d, ka, skid, ref need);
                if (!moved)
                {
                    clear = true;
                    break;
                }
            }
            if (!clear || need <= over + 0.01f || need > over + MaxExit)
            {
                return;   // nobody in the way, or a whole crowd: no clear ground within reach
            }
            Vector2 e2 = t + d * need;
            Vector2 f2 = e2 + d * skid;
            if (!Combat.ClearPath(end, f2))
            {
                return;
            }
            r.Pts[n - 1] = e2;
            r.Final = f2;
        }

        /// <summary>
        /// Someone in 'list' stands where it would come out ('need' tiles past the target 't' along
        /// 'd', then the skid): move the exit on past them. True if it moved.
        /// </summary>
        private static bool Crowd(List<Actor> list, int max, Actor v, Vector2 t, Vector2 d, float ka, float skid, ref float need)
        {
            bool moved = false;
            for (int i = 0; list != null && i < list.Count && i < max; i++)
            {
                Actor u = list[i];
                if (u == v || u == null || !u.isAlive() || !Combat.Grounded(u))
                {
                    continue;
                }
                float ku = KOf(u);
                // two sprites side by side without touching
                float room = 0.45f + 0.15f * (ka + ku);
                Vector2 rel = u.current_position - t;
                float along = Vector2.Dot(rel, d);
                if (Mathf.Abs(rel.x * d.y - rel.y * d.x) > room || along < need - room || along > need + skid + room)
                {
                    continue;
                }
                need = along + Over(ka, ku);
                moved = true;
            }
            return moved;
        }

        private static readonly List<Actor> _crowd = new List<Actor>();

        /// <summary>
        /// As it goes, the exit is checked against the real ground: the plan saw only the units the
        /// fighter is aware of (an Adept 6 tiles), and a target at the edge of that may have a file
        /// of them standing behind it. The round-3 II demo came out on top of the second of three.
        /// </summary>
        private static void Reexit(Actor a, Route r, Actor v)
        {
            if (r == null || !r.Through || r.Kind != Kind.Strike || v == null)
            {
                return;
            }
            WorldTile tile = Combat.TileAt(r.Final);
            if (tile == null)
            {
                return;
            }
            _crowd.Clear();
            float reach = MaxExit + 2.5f;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, 1))
            {
                if (u == null || u == a || u == v || !u.isAlive() || u.isInsideSomething() || !Combat.Grounded(u)
                    || (u.current_position - r.Final).sqrMagnitude > reach * reach)
                {
                    continue;
                }
                _crowd.Add(u);
                if (_crowd.Count >= 48)
                {
                    break;
                }
            }
            if (_crowd.Count > 0)
            {
                // nearest first, as the plan's lists are
                Vector2 o = r.Final;
                _crowd.Sort((x, y) => (x.current_position - o).sqrMagnitude.CompareTo((y.current_position - o).sqrMagnitude));
                PushExit(r, v, KOf(a), _crowd, _crowd.Count, null, 0);
            }
            _crowd.Clear();
        }

        // two blinks side by side read as a strobe, not as speed: the AI remembers the last few
        private static readonly double[] _recentAt = { -99.0, -99.0, -99.0, -99.0, -99.0, -99.0, -99.0, -99.0 };
        private static readonly Vector2[] _recentPos = new Vector2[8];
        private static int _recentNext;

        /// <summary>Has a Flash Step started within 'within' world seconds and 'r' tiles of 'at'?</summary>
        internal static bool RecentNear(Vector2 at, double within, float r)
        {
            double now = Fighter.Now;
            float r2 = r * r;
            for (int i = 0; i < _recentAt.Length; i++)
            {
                double since = now - _recentAt[i];
                if (since >= 0.0 && since < within && (_recentPos[i] - at).sqrMagnitude < r2)
                {
                    return true;
                }
            }
            return false;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance from p to the route (the blur and the skid).</summary>
        internal static float DistTo(Route r, Vector2 p)
        {
            float best = SegDist(p, r.Pts[r.Pts.Length - 1], r.Final, out _);
            for (int i = 0; i < r.Pts.Length - 1; i++)
            {
                best = Mathf.Min(best, SegDist(p, r.Pts[i], r.Pts[i + 1], out _));
            }
            return best;
        }

        /// <summary>Where the blur is at 'k' (0..1 of the dash): every leg takes the same time.</summary>
        private Vector2 Along(float k)
        {
            int segs = _r.Pts.Length - 1;
            float s = Mathf.Clamp01(k) * segs;
            int i = Mathf.Min((int)s, segs - 1);
            return Vector2.Lerp(_r.Pts[i], _r.Pts[i + 1], s - i);
        }

        /// <summary>The way the blur is heading at 'k'.</summary>
        private Vector2 DirAt(float k)
        {
            int segs = _r.Pts.Length - 1;
            int i = Mathf.Clamp((int)(Mathf.Clamp01(k) * segs), 0, segs - 1);
            Vector2 d = _r.Pts[i + 1] - _r.Pts[i];
            return d.sqrMagnitude > 1e-6f ? d.normalized : _u;
        }

        /// <summary>The point of the route nearest p: how far it is, when the blur passes it, on which leg.</summary>
        private float Project(Vector2 p, out float f, out int seg)
        {
            int segs = _r.Pts.Length - 1;
            float best = float.MaxValue;
            f = 1f;
            seg = segs - 1;
            for (int i = 0; i < segs; i++)
            {
                float d = SegDist(p, _r.Pts[i], _r.Pts[i + 1], out float t);
                if (d < best)
                {
                    best = d;
                    f = (i + t) / segs;
                    seg = i;
                }
            }
            float ds = SegDist(p, _r.Pts[segs], _r.Final, out _);
            if (ds < best)
            {
                best = ds;
                f = 1f;
                seg = segs - 1;
            }
            return best;
        }

        private Vector2 BodyAt(Vector2 ground)
        {
            return new Vector2(ground.x, ground.y + Pose.PivotUp * A.actor_scale);
        }

        private Vector2 HeadAt(Vector2 ground)
        {
            return new Vector2(ground.x, ground.y + Pose.PivotUp * 2.2f * A.actor_scale);
        }

        /// <summary>The middle of another unit's body, where blows land.</summary>
        private static Vector2 Body(Actor v)
        {
            Sprite s = Fx.UnitSprite(v);
            float h = s != null ? Mathf.Clamp(s.rect.height * 0.4f, 1.5f, 40f) : 3.5f;
            return new Vector2(v.current_position.x, v.current_position.y + v.position_height + h * v.actor_scale);
        }

        /// <summary>Killed, recycled, picked up by the god hand or gone inside: never touch it again.</summary>
        private static bool Gone(Actor v, long id)
        {
            return v == null || !v.isAlive() || v.Sid() != id || v.isInMagnet() || v.isInsideSomething();
        }

        private bool Visible => Fx.OnScreen(_mid, 0.45f);

        /// <summary>One pixel of tremble, left and right.</summary>
        private Vector2 Tremble()
        {
            return new Vector2(((int)(T * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
        }

        /// <summary>A crab's legs skittering under it.</summary>
        private Vector2 Scuttle()
        {
            return new Vector2(0f, ((int)(T * 50f) & 1) == 0 ? 0.5f : 0f);
        }

        // ------------------------------------------------------------- start --

        public static bool Begin(Actor a, Route r, Actor target, int level, Technique tech)
        {
            if (r == null || r.Pts == null || r.Pts.Length < 2 || !Moves.CanPerform(a))
            {
                return false;
            }
            if ((a.current_position - r.Pts[0]).sqrMagnitude > 0.25f)
            {
                return false;   // it moved since the plan was made
            }
            if (r.Kind == Kind.Strike && (target == null || !target.isAlive()))
            {
                return false;
            }
            if (level >= 2)
            {
                Reexit(a, r, target);
            }
            FlashStep m = new FlashStep(a, r, target, level) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            _recentAt[_recentNext] = Fighter.Now;
            _recentPos[_recentNext] = m._mid;
            _recentNext = (_recentNext + 1) % _recentAt.Length;
            Dev.Watch(m._mid + new Vector2(0f, 1.2f * m._k));
            return true;
        }

        // --------------------------------------------------------------- tick --

        /// <summary>Nothing can touch the unit while it is a blur.</summary>
        public override bool Intangible => _phase == Phase.Dash;

        public override void Tick(float dt)
        {
            if (_hold > 0f)
            {
                // II: the cut's local hit-stop: the move, its clock too, holds a beat, trembling
                // with the blow (the cut, the knockback and the pose all wait)
                _hold -= dt;
                T -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Crouch:
                    Crouch();
                    break;
                case Phase.Dash:
                    Dash();
                    break;
                case Phase.Arrive:
                    Arrive();
                    break;
                case Phase.Jab:
                    Jab();
                    break;
                case Phase.Zanshin:
                    Zanshin();
                    break;
                case Phase.Stumble:
                    Stumble();
                    break;
                case Phase.Recoil:
                    Recoil();
                    break;
                default:
                    Recover();
                    break;
            }
            Beats();
            Ghosts();
            if (T > 3f && !Finished)
            {
                Finished = true;   // never hang on to the unit, whatever happened
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("flash " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        // ------------------------------------------------------------- crouch --

        private void Crouch()
        {
            float px = Px;
            Vector2 at = _r.Pts[0];
            bool first = Enter();
            if (first)
            {
                // dev captures frame the whole dash, not just where it starts
                Dev.Watch(_mid + new Vector2(0f, 1.2f * _k));
            }
            if (first && Visible)
            {
                // the heels dig in: ground-tinted dust kicked back
                Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                Vector2 side = new Vector2(-_u.y, _u.x);
                int n = _blink ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    Vector2 off = side * (n == 1 ? 0f : i == 0 ? 1.5f : -1.5f) * px;
                    Particle p = Fx.Spawn(Art.Dust[i], at - _u * 2.5f * px + off, px, 0.32f, i == 0 ? FxLayer.Behind : FxLayer.Top, 2);
                    p?.Moving(-_u * Random.Range(10f, 16f) * px + off * 3f, Random.Range(2f, 4f) * px, 4f)
                        .Sized(0.6f, 1f)
                        .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
                }
            }
            float c = Anim.OutCubic(Mathf.Clamp01(_pt / (_tCrouch * 0.75f)));
            float sx = _blink ? 1.2f : 1.12f, sy = _blink ? 0.75f : 0.85f;
            if (_crab)
            {
                sx += 0.08f;
            }
            Pose.Ground = at;
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1f, sx, c);
            Pose.SY = Mathf.Lerp(1f, sy, c);
            Pose.Rot = _crab ? 0f : -8f * c;
            Pose.Nudge = _crab ? Scuttle() : Level >= 3 && _pt > 0.04f ? Tremble() : Vector2.zero;
            if (_pt >= _tCrouch)
            {
                Next(Phase.Dash);
            }
        }

        // --------------------------------------------------------------- dash --

        private void Dash()
        {
            float px = Px;
            if (Enter())
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (_r.Kind == Kind.Strike && _blink)
                {
                    FindVictims();
                }
                if (Level >= 3 && _r.Kind == Kind.Strike)
                {
                    Corner(0);
                }
                bool vis = Visible;
                if (vis)
                {
                    PathFx(px);
                }
                if (_blink)
                {
                    // the vanish frame: stretched wide under a white flash, then gone
                    Pose.Ground = _r.Pts[0];
                    Pose.Height = 0f;
                    Pose.Rot = 0f;
                    Pose.SX = _crab ? 1.7f : 1.6f;
                    Pose.SY = 0.8f;
                    Pose.Nudge = Vector2.zero;
                    if (vis)
                    {
                        Particle flash = Silhouette(0.07f, FxLayer.Over, 3);
                        flash?.Color(new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
                        if (Level < 3 || _r.Kind != Kind.Strike)
                        {
                            // the afterimage left where it stood (at III the corner afterimage is it)
                            GhostAt(_r.Pts[0], _crab ? 0f : -8f, 1.2f, 0.75f, Pose.FaceRight, 0.3f, Fx.WithA(_pal.Ghost, 0.75f), _pal.GhostEnd);
                        }
                    }
                    return;
                }
            }
            float k = Mathf.Clamp01(_pt / _tDash);
            if (_blink)
            {
                Pose.Hidden = true;
                Pose.HideShadow = true;
                Pose.Ground = Along(k);
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                if (Level >= 3 && _r.Kind == Kind.Strike)
                {
                    while (_corners < 3 && k >= _corners / 3f)
                    {
                        Corner(_corners);
                    }
                }
                // the moment the blur goes by, they freeze: a master's flow of blows is cut short
                // (Moves.Interrupt only ends light moves; a technique in progress is never touched,
                // nor is one too heavy for this tier: it only takes a glancing cut)
                while (_passed < _victims.Count && _victims[_passed].F <= k)
                {
                    Victim w = _victims[_passed++];
                    if (!Gone(w.U, w.Id) && !Moves.Intangible(w.U) && Combat.Allowed(A, w.U))
                    {
                        Moves.Interrupt(w.U);
                    }
                }
            }
            else
            {
                // level I: a visible slide, smeared along its motion
                float e = Anim.OutQuad(k);
                Pose.Ground = Along(e);
                float h = Mathf.Abs(_u.x);
                float s = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.15f));
                Pose.SX = 1f + (_crab ? 0.35f : 0.28f * h + 0.06f) * s;
                Pose.SY = 1f - 0.1f * s;
                Pose.Rot = _crab ? 0f : 12f * s;
                Pose.Nudge = _crab ? Scuttle() : Vector2.zero;
                if (!_slideGhost && k >= 0.45f)
                {
                    _slideGhost = true;
                    if (Visible)
                    {
                        GhostAt(Along(0.5f), Pose.Rot, Pose.SX, Pose.SY, Pose.FaceRight, 0.16f, _pal.Ghost, _pal.GhostEnd);
                    }
                }
            }
            if (k >= 1f)
            {
                Pose.Ground = _r.Pts[_r.Pts.Length - 1];
                if (_r.Kind == Kind.Fumble)
                {
                    Next(Phase.Stumble);
                }
                else
                {
                    Next(Phase.Arrive);
                }
            }
        }

        /// <summary>The flat-colour copy of the unit as it is drawn right now (flashes).</summary>
        private Particle Silhouette(float life, FxLayer layer, int order)
        {
            Sprite s = Fx.UnitSprite(A);
            if (s == null || !Fx.HasSilhouette)
            {
                return null;
            }
            Particle p = Fx.Spawn(s, DrawPos(), 1f, life, layer, order, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = Fx.UnitEuler(this);
            p.SX = A.actor_scale * Pose.SX;
            p.SY = A.actor_scale * Pose.SY;
            return p;
        }

        /// <summary>An afterimage of the unit somewhere along its path, in a given pose.</summary>
        private Particle GhostAt(Vector2 ground, float rot, float sx, float sy, bool faceRight, float life, params Color[] colors)
        {
            Pose keep = Pose;
            Pose.Ground = ground;
            Pose.Height = 0f;
            Pose.Rot = rot;
            Pose.SX = sx;
            Pose.SY = sy;
            Pose.FaceRight = faceRight;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Particle p = Fx.Afterimage(this, life, colors);
            Pose = keep;
            return p;
        }

        /// <summary>A speed line along 'dir' (Art.Lines point along +y).</summary>
        private void Streak(Vector2 at, Vector2 dir, float life, Color c, float delay, int len)
        {
            Particle p = Fx.Spawn(Art.Lines[Mathf.Clamp(len, 0, Art.Lines.Length - 1)], at, Px, life, FxLayer.Top, 1);
            if (p == null)
            {
                return;
            }
            p.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            p.Moving(dir * 30f * Px);
            p.Delay = delay;
            p.Color(c, Fx.WithA(c, 0f));
        }

        /// <summary>
        /// Everything the blur leaves along the way, spawned now and timed (Particle.Delay) to appear
        /// as it passes each point: speed lines, afterimages, a dust streak, the "x" on every enemy.
        /// </summary>
        private void PathFx(float px)
        {
            bool zig = _r.Pts.Length >= 4;
            // speed lines, each appearing just behind the unit as it goes by
            int lines = !_blink ? 2 : zig ? 9 : 5;
            for (int i = 0; i < lines; i++)
            {
                float f = _blink ? (i + 0.5f) / lines : 0.3f + 0.35f * i;
                Vector2 dir = DirAt(f);
                Vector2 side = new Vector2(-dir.y, dir.x);
                Vector2 at = BodyAt(Along(f)) + side * Random.Range(-5f, 5f) * px - dir * Random.Range(2f, 6f) * px;
                Color c = !_blink ? new Color(1f, 1f, 1f, 0.8f)
                    : Level >= 3 && (i & 1) == 0 ? Fx.WithA(_pal.Core, 0.9f) : Fx.WithA(_pal.Edge, 0.85f);
                // the slide eases out (OutQuad): it reaches f at 1 - sqrt(1 - f) of its time
                float when = _blink ? f * _tDash : (1f - Mathf.Sqrt(1f - f)) * _tDash;
                Streak(at, dir, Random.Range(0.12f, 0.18f), c, when, Random.Range(1, 3));
            }
            if (!_blink)
            {
                return;
            }
            // afterimages along the blur: crisp, white at the heart and tier-tinted, each one
            // stronger than the last, so the eye follows them to where it reappears
            if (zig)
            {
                for (int s = 0; s < 3; s++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        float f = (s + 0.35f + 0.35f * j) / 3f;
                        float a = j == 0 ? 0.55f : 0.8f;
                        Vector2 dir = DirAt(f);
                        bool face = _crab || Mathf.Abs(dir.x) < 0.1f ? _baseFace : dir.x > 0f;
                        Particle g = GhostAt(Along(f), _crab ? 0f : 12f, 1.2f, 0.92f, face, 0.15f,
                            Fx.WithA(_pal.Core, a), Fx.WithA(_pal.Edge, a * 0.7f), _pal.GhostEnd);
                        if (g != null)
                        {
                            g.Delay = f * _tDash;
                        }
                    }
                }
            }
            else
            {
                for (int j = 0; j < 3; j++)
                {
                    float f = 0.3f + 0.25f * j;
                    float a = 0.5f + 0.15f * j;
                    Vector2 dir = DirAt(f);
                    bool face = _crab || Mathf.Abs(dir.x) < 0.1f ? _baseFace : dir.x > 0f;
                    Particle g = GhostAt(Along(f), _crab ? 0f : 12f, 1.22f, 0.9f, face, 0.16f,
                        Fx.WithA(_pal.Core, a), Fx.WithA(_pal.Edge, a * 0.7f), _pal.GhostEnd);
                    if (g != null)
                    {
                        g.Delay = f * _tDash;
                    }
                }
            }
            // a low, quick dust streak (a few specks kicked up, not a trail of clouds)
            float len = 0f;
            for (int i = 0; i < _r.Pts.Length - 1; i++)
            {
                len += Vector2.Distance(_r.Pts[i], _r.Pts[i + 1]);
            }
            int n = Fx.Count(Mathf.Clamp(Mathf.RoundToInt(len / 1.1f), 2, 6));
            for (int i = 0; i < n; i++)
            {
                float f = (i + 0.5f) / n;
                Vector2 g = Along(f);
                Vector2 dir = DirAt(f);
                Vector2 side = new Vector2(-dir.y, dir.x);
                WorldTile t = Combat.TileAt(g);
                Color dust = Fx.Lighten(Fx.SoilColor(t), 0.35f);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], g + side * Random.Range(-1f, 1f) * px, px, Random.Range(0.24f, 0.32f), FxLayer.Ground, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(-dir * Random.Range(8f, 14f) * px + side * Random.Range(-3f, 3f) * px, Random.Range(1f, 3f) * px, 5f)
                    .Sized(0.45f, 0.8f)
                    .Color(Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0.3f), Fx.WithA(dust, 0f));
                p.Delay = f * _tDash;
            }
            if (Level == 2)
            {
                // II: the dash scuffs the ground (a dark, unlit line, drawn as the blur goes by;
                // III burns it instead)
                const int pieces = 3;
                for (int i = 0; i < pieces; i++)
                {
                    float f0 = (float)i / pieces, f1 = (i + 1f) / pieces;
                    Color scuff = Fx.Darken(Fx.SoilColor(Combat.TileAt(Along(f1))), 0.2f);
                    Particle l = Kit.Line(Along(f0), Along(f1), px, 0.9f, 1, FxLayer.Ground, 2,
                        Fx.WithA(scuff, 0.55f), Fx.WithA(scuff, 0.4f), Fx.WithA(scuff, 0f));
                    if (l != null)
                    {
                        l.Delay = f0 * _tDash;
                    }
                }
            }
            // the blade goes through everyone on the way: a hairline through each body as it passes
            foreach (Victim w in _victims)
            {
                Vector2 at = Body(w.U);
                Vector2 d = DirAt(w.F);
                float half = 4.5f * Mathf.Clamp(KOf(w.U), 0.8f, 2f) * px;
                Particle p = Kit.Line(at - d * half, at + d * half, px, 0.09f, 1, FxLayer.Top, 15,
                    Color.white, Fx.WithA(_pal.Core, 0.8f), Fx.WithA(_pal.Edge, 0f));
                if (p == null)
                {
                    break;
                }
                p.Delay = w.F * _tDash;
            }
        }

        /// <summary>Who the blur will go through: every foe near the path, in the order it passes them.</summary>
        private void FindVictims()
        {
            float width = PathWidth(_k);
            float r = Vector2.Distance(_mid, _r.Final);
            foreach (Vector2 p in _r.Pts)
            {
                r = Mathf.Max(r, Vector2.Distance(_mid, p));
            }
            List<Actor> near = Combat.FoesAround(A, _mid, r + width + 0.6f);
            bool mainIn = false;
            foreach (Actor u in near)
            {
                bool main = u == _target && u.Sid() == _targetId;
                float d = Project(u.current_position, out float f, out int seg);
                if (!main && (d > width + u.stats["size"] * 0.5f || _victims.Count >= MaxVictims))
                {
                    continue;
                }
                mainIn |= main;
                _victims.Add(MakeVictim(u, f, seg, main));
            }
            // the target, even if it has stepped a little off the line since the plan
            if (!mainIn && !Gone(_target, _targetId) && !Moves.Locked(_target) && Combat.IsFoe(A, _target) && Combat.Grounded(_target)
                && Project(_target.current_position, out float mf, out int ms) <= width + 1.2f)
            {
                _victims.Add(MakeVictim(_target, mf, ms, true));
            }
            _victims.Sort((x, y) => x.F.CompareTo(y.F));
            // the moment it passes they freeze, until the cut lands
            float wait = _tDash + _cutDelay + 0.08f;
            foreach (Victim w in _victims)
            {
                if (Combat.Allowed(A, w.U))
                {
                    w.U.makeWait(wait);   // (one too heavy for this tier doesn't stop for it)
                }
            }
            if (Dev.On) Dev.Trace("flash " + Id + " passes " + _victims.Count + " foes");
        }

        private Victim MakeVictim(Actor u, float f, int seg, bool main)
        {
            Vector2 a = _r.Pts[seg], b = _r.Pts[seg + 1];
            Vector2 sd = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : _u;
            Vector2 dir;
            if (_r.Through)
            {
                // thrown off to the side it stood on, a little along the cut
                Vector2 n = new Vector2(-sd.y, sd.x);
                float side = Vector2.Dot(u.current_position - a, n);
                float sign = Mathf.Abs(side) < 0.06f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(side);
                dir = (n * sign + sd * 0.35f).normalized;
            }
            else
            {
                dir = sd;
            }
            return new Victim { U = u, Id = u.Sid(), F = f, Dir = dir, Main = main };
        }

        /// <summary>Level III: an afterimage left standing where the blur turned (it strikes again later).</summary>
        private void Corner(int i)
        {
            _corners = i + 1;
            float f = i / 3f;
            Vector2 at = Along(f);
            _ghostPos[i] = BodyAt(at);
            _ghostBorn[i] = T;
            if (!Visible)
            {
                return;
            }
            Vector2 dir = DirAt(f + 0.01f);
            bool face = _crab || Mathf.Abs(dir.x) < 0.1f ? _baseFace : dir.x > 0f;
            Particle g = GhostAt(at, _crab ? 0f : 22f, 1.15f, 0.92f, face, 1f, Color.white);
            if (g != null)
            {
                g.Held = true;
                g.Grad = null;
                g.R.color = new Color(1f, 1f, 1f, 0.95f);
                _ghostHome[i] = g.Pos;
            }
            _ghosts[i] = g;
            if (i > 0)
            {
                // it kicked off the ground here
                Fx.Ring(at, Px, 0.22f, 2f, 7f * _k, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f,
                    Fx.WithA(_pal.Edge, 0.8f), Fx.WithA(_pal.Edge, 0f));
            }
        }

        // ------------------------------------------------------------- arrive --

        private void Arrive()
        {
            float px = Px;
            Vector2 end = _r.Pts[_r.Pts.Length - 1];
            bool skid = (_r.Final - end).sqrMagnitude > 0.0004f;
            Vector2 dir = DirAt(1f);
            if (Enter())
            {
                Pose.Hidden = false;
                Pose.HideShadow = false;
                if (_blink && _r.Kind == Kind.Strike)
                {
                    _cutT = T + _cutDelay;   // the cut lands a beat after the blur has gone by
                }
                if (!_crab && Mathf.Abs(dir.x) > 0.12f)
                {
                    Pose.FaceRight = dir.x > 0f;
                }
                if (Visible)
                {
                    ArrivalFx(px, end, dir, skid);
                }
            }
            float dur = skid ? _tSkid : _tBrake;
            float k = Mathf.Clamp01(_pt / dur);
            Pose.Height = 0f;
            if (skid)
            {
                // out of the blur, stretched, leaning back against the skid
                float s = Anim.OutQuad(k);
                Pose.Ground = Vector2.Lerp(end, _r.Final, Anim.OutCubic(k));
                Pose.SX = Mathf.Lerp(_crab ? 1.4f : 1.3f, 1f, s);
                Pose.SY = Mathf.Lerp(0.8f, 0.95f, s);
                Pose.Rot = _crab ? 0f : Mathf.Lerp(-14f, -4f, s);
                Pose.Nudge = _crab ? Scuttle() : Vector2.zero;
            }
            else
            {
                // a brake: it plants its feet
                float amp = Anim.Spring(_pt, 12f, 26f);
                Pose.Ground = end;
                Pose.SX = 1f + 0.14f * amp;
                Pose.SY = 1f - 0.12f * amp;
                Pose.Rot = _crab ? 0f : -6f * amp;
                Pose.Nudge = Vector2.zero;
            }
            if (k >= 1f)
            {
                if (_r.Kind == Kind.Retreat)
                {
                    Next(Phase.Recover);
                }
                else if (!_blink)
                {
                    Next(Phase.Jab);
                }
                else
                {
                    Next(Phase.Zanshin);
                }
            }
        }

        private void ArrivalFx(float px, Vector2 end, Vector2 dir, bool skid)
        {
            WorldTile tile = Combat.TileAt(end);
            if (_blink)
            {
                // the blur's last afterimage, right behind it
                GhostAt(end - dir * 0.35f * _k, _crab ? 0f : 10f, 1.25f, 0.88f, Pose.FaceRight, 0.14f,
                    Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.6f), _pal.GhostEnd);
            }
            HeelDust(end, -dir, px, tile, _k);
            if (skid)
            {
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
                int n = Fx.Count(4);
                for (int i = 0; i < n; i++)
                {
                    float f = (i + 0.5f) / n;
                    Vector2 at = Vector2.Lerp(end, _r.Final, Anim.OutCubic(f)) + new Vector2(0f, Random.Range(-1f, 1f) * px);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], at, px, 0.36f, FxLayer.Behind, 3);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving(-dir * Random.Range(8f, 16f) * px, Random.Range(2f, 5f) * px, 4f)
                        .Sized(0.5f, 1f)
                        .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
                    p.Delay = f * _tSkid;
                }
            }
            if (Level >= 3 && _r.Kind == Kind.Strike)
            {
                // "ting": it's done before anyone knows it
                Vector2 head = HeadAt(_r.Final) + new Vector2((Pose.FaceRight ? 3f : -3f) * px, 2f * px);
                Fx.Glint(head, px, 0.26f, _pal.Core, 1.1f);
                Fx.Ring(_r.Final, px, 0.32f, 3f, RingPx(1f, px), 2, 1, FxLayer.Ground, 5, Art.Ground, 2.5f,
                    Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
            }
        }

        /// <summary>
        /// Two small puffs kicked out at its heels, towards 'back': lighter than a landing puff,
        /// and drawn behind the unit, so the pose it arrives in stays readable.
        /// </summary>
        internal static void HeelDust(Vector2 at, Vector2 back, float px, WorldTile tile, float k)
        {
            if (!Fx.OnScreen(at))
            {
                return;
            }
            back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector2.left;
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            Vector2 side = new Vector2(-back.y, back.x);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                Particle p = Fx.Spawn(Art.Dust[i], at + (back * 1.5f + side * s * 2f) * px, px, 0.3f, FxLayer.Behind, 2);
                if (p == null)
                {
                    return;
                }
                p.Moving((back * 10f + side * s * 6f) * px * k, 2f * px, 6f)
                    .Sized(0.5f, 0.85f)
                    .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
        }

        /// <summary>A radius of 'tiles' in this unit's pixels (bigger units, bigger rings).</summary>
        private float RingPx(float tiles, float px)
        {
            return Mathf.Clamp(Mathf.Round(tiles / px * Mathf.Clamp(_k, 0.8f, 1.6f)), 4f, 120f);
        }

        // ---------------------------------------------------------------- jab --

        private void Jab()
        {
            float px = Px;
            if (Enter())
            {
                _jabV = _target;
                _jabId = _targetId;
                if (Gone(_jabV, _jabId) || !_jabV.hasHealth() || Moves.Locked(_jabV) || !Combat.IsFoe(A, _jabV))
                {
                    _jabV = Retarget(Pose.Ground);
                    _jabId = _jabV != null ? _jabV.Sid() : -1L;
                }
                _jabFrom = Pose.Ground;
                Vector2 d = _jabV != null ? _jabV.current_position - _jabFrom : DirAt(1f);
                _jabDir = d.sqrMagnitude > 1e-6f ? d.normalized : _u;
                if (!_crab && Mathf.Abs(_jabDir.x) > 0.12f)
                {
                    Pose.FaceRight = _jabDir.x > 0f;
                }
                float room = _jabV != null ? Vector2.Distance(_jabFrom, _jabV.current_position) : 2f;
                _jabMove = room > 0.55f * _k + 0.22f * _k && Combat.SafeSpot(_jabFrom + _jabDir * 0.22f * _k);
                _jabStyle = Random.value < 0.5f ? 0 : 1;
            }
            float f = _pt < TJabHit ? Anim.OutCubic(_pt / TJabHit) : 1f - 0.55f * Anim.Smooth(0.08f, TJab, _pt);
            Pose.Ground = _jabMove ? _jabFrom + _jabDir * 0.22f * _k * f : _jabFrom;
            Pose.Height = 0f;
            if (_jabStyle == 0)
            {
                // a straight lunge
                Pose.Rot = _crab ? 0f : 16f * f;
                Pose.SX = 1f + 0.16f * f;
                Pose.SY = 1f - 0.1f * f;
            }
            else
            {
                // low and long
                Pose.Rot = _crab ? 0f : 24f * f;
                Pose.SX = 1f + 0.1f * f;
                Pose.SY = 1f - 0.16f * f;
            }
            Pose.Nudge = Vector2.zero;
            if (!_jabDone && _pt >= TJabHit)
            {
                _jabDone = true;
                JabContact(px);
            }
            if (_pt >= TJab)
            {
                Next(_parried ? Phase.Recoil : Phase.Recover);
            }
        }

        private void JabContact(float px)
        {
            Actor v = _jabV;
            Vector2 tip = BodyPos() + _jabDir * 4f * px;
            if (v == null || Gone(v, _jabId) || !v.hasHealth() || Moves.Locked(v))
            {
                Whiff(px, tip);
                return;
            }
            if (!Combat.Allowed(A, v, out float cm))
            {
                Whiff(px, tip);
                return;
            }
            if (!Moves.Intangible(v))
            {
                Moves.Interrupt(v);   // a flow of blows is no shield (and it may still read the jab)
            }
            Contact c = Combat.Reach(this, v, false);
            if (c == Contact.Parried)
            {
                _parried = true;
                _parryFrom = v.current_position;
                Clash(px, tip);
                return;
            }
            if (c == Contact.Evaded)
            {
                _slipped = v == _target;   // it turns to find it afterwards, no victory glint
                Whiff(px, tip);
                return;
            }
            HitResult r = Combat.Hit(A, v, JabMult * cm, true, true, 0f, this);
            _jabLanded = r.Landed;
            Vector2 at = Body(v);
            if (Visible)
            {
                if (_armed)
                {
                    // a thrust: one bright line and one spark
                    Kit.Line(at - _jabDir * 3f * px, at + _jabDir * 3f * px, px, 0.07f, 1, FxLayer.Top, 21, Color.white, Fx.WithA(Color.white, 0f));
                    Spark(at, _jabDir, px, Color.white);
                }
                else
                {
                    Fx.HitSpark(at, px, 0, _jabDir, 1);
                }
                FlashUnit(v, Tiers.Of(v) != Tier.None && !r.Killed ? Tiers.Color(Tiers.Of(v)) : Color.white, 0.06f);
            }
            Anim.Sound(_armed ? "event:/SFX/HIT/HitFlesh" : "event:/SFX/OTHER/Punch", v.current_tile);
            if (Dev.On) Dev.Trace("flash " + Id + " jab -> " + v.asset.id + " hp " + v.getHealth());
        }

        /// <summary>The blow meets nothing: a little swish through the air.</summary>
        private void Whiff(float px, Vector2 at)
        {
            if (!Visible)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), at, px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = _jabDir.x >= 0f ? 1f : -1f;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        /// <summary>The blow is turned aside: a clash star, sparks, the ring of metal.</summary>
        private void Clash(float px, Vector2 at)
        {
            Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
            if (!Visible)
            {
                return;
            }
            Fx.Glint(at, px, 0.18f, Color.white, 1f);
            Kit.Sparks(at, px, 5, 60f, 120f, new Color(1f, 0.92f, 0.6f, 1f));
        }

        /// <summary>One spark flying away from a blow.</summary>
        private static void Spark(Vector2 at, Vector2 away, float px, Color c)
        {
            float a = Mathf.Atan2(away.y, away.x) + Random.Range(-0.6f, 0.6f);
            Particle p = Fx.Spawn(Art.Sparks[1], at, px, 0.14f, FxLayer.Top, 19);
            if (p == null)
            {
                return;
            }
            p.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(70f, 110f) * px, 0f, 9f);
            p.FaceVel = true;
            p.Color(Color.white, c, Fx.WithA(c, 0f));
        }

        /// <summary>
        /// A white (or tier-coloured) flash over another unit's whole sprite, following it as it flies.
        /// </summary>
        internal static Particle FlashUnit(Actor v, Color c, float life)
        {
            if (v == null || !v.is_visible || !Fx.HasSilhouette)
            {
                return null;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return null;
            }
            Vector3 cp = v.cur_transform_position;
            Particle p = Fx.Spawn(s, new Vector2(cp.x, cp.y), 1f, life, FxLayer.Over, 2, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = v.current_rotation;
            p.SX = v.current_scale.x;
            p.SY = v.current_scale.y;
            p.Color(Fx.WithA(c, 0.95f), Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            long id = v.Sid();
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    Vector3 at = v.cur_transform_position;
                    q.Pos = new Vector2(at.x, at.y);
                    q.Euler = v.current_rotation;
                }
            };
            return p;
        }

        /// <summary>Its target is gone: the nearest foe within 2 tiles, of the same class (or lighter) if there is one.</summary>
        private Actor Retarget(Vector2 at)
        {
            Actor same = null, any = null;
            float ds = float.MaxValue, da = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, at, 2f))
            {
                if (!u.hasHealth() || !Combat.Allowed(A, u))
                {
                    continue;
                }
                float d = (u.current_position - at).sqrMagnitude;
                if (Weights.Of(u) <= _targetClass && d < ds)
                {
                    ds = d;
                    same = u;
                }
                if (d < da)
                {
                    da = d;
                    any = u;
                }
            }
            return same ?? any;
        }

        // ------------------------------------------------------------ zanshin --

        /// <summary>Back turned, still, while the cut lands behind it (and, at III, the echoes).</summary>
        private void Zanshin()
        {
            Pose.Ground = _r.Final;
            Pose.Height = 0f;
            if (_slipped && !_whirled && !_crab)
            {
                // the cut met a mirage: no back turned on an enemy that is suddenly elsewhere,
                // it whips round to face it (the echoes, at III, still go looking for it)
                _whirled = true;
                if (!Gone(_target, _targetId))
                {
                    float dx = _target.current_position.x - _r.Final.x;
                    if (Mathf.Abs(dx) > 0.05f)
                    {
                        Pose.FaceRight = dx > 0f;
                    }
                }
            }
            float since = _cutDone ? T - _cutT : -1f;
            float flick = since < 0f ? 0f : since < 0.05f ? Anim.OutCubic(since / 0.05f) : Mathf.Exp(-(since - 0.05f) * 10f);
            // armed: a flick of the blade as the cut lands; unarmed: the fist tightens
            Pose.SX = 1.02f + (_armed ? 0f : 0.04f * flick);
            Pose.SY = 0.95f - (_armed ? 0.02f : 0.04f) * flick;
            Pose.Rot = _crab ? 0f : -3f + (_armed ? 9f : 4f) * flick;
            float hold = Level >= 3 ? 0.06f : 0.04f;
            Pose.Nudge = since >= 0f && since < hold ? Tremble() : Vector2.zero;
            // below Master it keeps its back turned until the last afterimage has struck; a master
            // turns away as soon as the cut has thrown them, and its afterimages finish behind it
            // (Recover waits for them): no standing about
            bool done = _cutDone && _knockDone && (_flow && !_parried ? T >= _knockT + _settle : !EchoesPending && T >= _lastBeat + _settle);
            if (done)
            {
                Next(_parried ? Phase.Recoil : Phase.Recover);
            }
        }

        // ---------------------------------------------------------------- cut --

        /// <summary>The cut, the knockback and the echoes, on the move's clock whatever the phase.</summary>
        private void Beats()
        {
            if (_cutT >= 0f && !_cutDone && T >= _cutT)
            {
                Cut();
            }
            if (_cutDone && !_knockDone && T >= _cutT + (Level >= 3 ? 0.06f : 0.04f))
            {
                Knock();
            }
            if (_cutDone && Level >= 3)
            {
                while (_echoes < _corners && T >= EchoTime(_echoes))
                {
                    Echo(_echoes++);
                }
            }
            for (int i = _echoHits.Count - 1; i >= 0; i--)
            {
                if (T >= _echoHits[i].At)
                {
                    EchoHit h = _echoHits[i];
                    _echoHits.RemoveAt(i);
                    EchoLands(h);
                }
            }
        }

        /// <summary>
        /// When corner afterimage 'i' strikes: a beat after the cut, one after the other (the higher
        /// the tier, the tighter); at once, in a quick ripple, when the cut left no one to strike.
        /// </summary>
        private float EchoTime(int i)
        {
            if (_echoRush)
            {
                return _cutT + 0.03f + 0.035f * i;
            }
            return Mathf.Max(_ghostBorn[i] + 0.2f, _cutT + _echoLead + _echoBeat * i);
        }

        /// <summary>Level III: afterimages that have yet to strike, or crescents still in flight.</summary>
        private bool EchoesPending => Level >= 3 && _r.Kind == Kind.Strike && _cutDone && (_echoes < _corners || _echoHits.Count > 0);

        private void Cut()
        {
            _cutDone = true;
            _lastBeat = T;
            float px = Px;
            bool vis = Visible;
            float mult = CutMult(Level);
            int hits = 0;
            bool crit = false, main = false;
            Vector2 mainAt = Vector2.zero;
            foreach (Victim w in _victims)
            {
                Actor u = w.U;
                // taken over by another technique since the blur went by (a light move, a flow
                // strike, is no shield: it was cut short as the blur passed)
                if (Gone(u, w.Id) || Moves.Locked(u))
                {
                    w.Out = true;
                    continue;
                }
                if (!u.hasHealth())
                {
                    // already felled by another blow (alive until the game buries it): no cut on a
                    // body; the afterimages look around where it fell
                    if (w.Main)
                    {
                        _mainPos = u.current_position;
                    }
                    w.Out = true;
                    continue;
                }
                // thrown clear of the line by someone else's blow since the blur went by: the cut
                // that lands a beat later can't reach it there
                if (!Combat.Grounded(u) || Project(u.current_position, out _, out _) > PathWidth(_k) + 1.5f)
                {
                    w.Out = true;
                    continue;
                }
                Vector2 at = Body(u);
                if (w.Main)
                {
                    _mainPos = u.current_position;
                }
                // every one it reaches may answer, the heavy ones too (before even a glancing cut)
                Contact c = Combat.Reach(this, u, !w.Main);
                if (c != Contact.Hit)
                {
                    // it read the blow: an afterimage takes the cut (Mirage Step), or it turns it aside
                    w.Out = true;
                    if (c == Contact.Parried && w.Main)
                    {
                        _parried = true;
                        _parryFrom = u.current_position;
                        Clash(px, at);
                    }
                    else if (w.Main)
                    {
                        _slipped = true;
                    }
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    // too heavy for this tier: a glancing cut and a stagger, never thrown
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    if (vis)
                    {
                        Kit.Sparks(at, px, 3, 50f, 90f, _pal.Edge);
                    }
                    w.Out = true;
                    continue;
                }
                Moves.Interrupt(u);
                HitResult r = Combat.Hit(A, u, mult * cm, true, true, 0f, this);
                w.Cut = true;
                hits++;
                if (w.Main)
                {
                    crit = r.Crit;
                    main = true;
                    mainAt = at;
                }
                else if (!main && hits == 1)
                {
                    mainAt = at;   // the target slipped the cut: the frame goes on the first one it caught
                }
                if (vis)
                {
                    CutMark(w, at, r, px);
                }
                if (Dev.On) Dev.Trace("flash " + Id + " cut x" + (mult * cm).ToString("F2") + " -> " + u.asset.id + " hp " + u.getHealth() + (r.Crit ? " CRIT" : ""));
            }
            _cutHits = hits;
            if (Level >= 3 && _r.Kind == Kind.Strike && EchoTarget(_ghostPos[0], out _) == null)
            {
                // nobody left for the afterimages: they flash out right after the cut instead of
                // keeping it standing there for nothing
                _echoRush = true;
            }
            if (hits > 0)
            {
                Anim.Sound(_armed ? "event:/SFX/HIT/HitFlesh" : "event:/SFX/OTHER/Punch", A.current_tile);
                if (Level >= 3)
                {
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                }
                // the real ground takes the cut on the same frame (on or off screen alike)
                Burn(hits);
                if (Level == 2)
                {
                    // II: a local hit-stop (the world goes on; III freezes it): the move holds a
                    // beat, longer on a critical cut on the target, and those it cut stand where
                    // they are until the knockback
                    _hold = main && crit ? 0.07f : 0.05f;
                    foreach (Victim w in _victims)
                    {
                        if (w.Cut && !Gone(w.U, w.Id))
                        {
                            w.U.makeWait(_hold + 0.08f);
                        }
                    }
                }
            }
            if (vis && hits > 0)
            {
                if (Level >= 3)
                {
                    // the whole cut burns into the ground, white-hot (one line: a second one at
                    // body height over it read as a double stroke)
                    Scar(px);
                }
                else
                {
                    PathLine(px);   // the whole cut lights up along the dash
                }
                if (Level >= 3 || main && crit)
                {
                    // III: the impact frame; a critical cut on the target at II+: the black flash
                    Kit.ImpactFrame(mainAt, px, Level >= 3 ? 0.42f : 0.32f, _pal, main && crit);
                }
                if (Level >= 3)
                {
                    // a God's cut gouges the ground along its whole path: the camera feels it
                    Kit.Shake(mainAt, _tier >= Tier.God ? 0.22f : 0.16f, _tier >= Tier.God ? 0.006f : 0.004f, _k);
                }
                if (Level >= 3)
                {
                    // the held beat: III the full, global one (II holds only itself, above)
                    Kit.Stop(mainAt, 0.1f);
                }
            }
        }

        /// <summary>The cut on one victim: a slash line through it (armed) or a hit star (unarmed), and its flash.</summary>
        private void CutMark(Victim w, Vector2 at, HitResult r, float px)
        {
            Actor u = w.U;
            Vector2 sd = DirAt(w.F);
            if (_armed)
            {
                Vector2 d = Rotate(sd, Random.Range(-28f, 28f));
                float half = (Level >= 3 ? 8f : 6f) * _k * px;
                Kit.Line(at - d * half, at + d * half, px, 0.14f, 2, FxLayer.Top, 22,
                    Fx.WithA(_pal.Edge, 0.9f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                Kit.Line(at - d * half * 0.85f, at + d * half * 0.85f, px, 0.1f, 1, FxLayer.Top, 23,
                    _pal.Core, Fx.WithA(_pal.Core, 0f));
            }
            else
            {
                // the big star only on the one it came for: three heavy stars side by side
                // merged into one white blob that hid the whole group through the hit-stop
                int size = (Level >= 3 ? 2 : 1) - (w.Main ? 0 : 1);
                Fx.HitSpark(at, px, size, w.Dir, w.Main ? 3 : 2);
            }
            Kit.Sparks(at, px, 2, 50f, 100f, _pal.Edge);
            // white, or the victim's own tier colour if it has one
            Tier vt = Tiers.Of(u);
            FlashUnit(u, vt != Tier.None && !r.Killed ? Tiers.Color(vt) : Color.white, 0.09f);
        }

        /// <summary>A one-pixel line along the whole dash, flashing as the cut lands.</summary>
        private void PathLine(float px)
        {
            for (int i = 0; i < _r.Pts.Length - 1; i++)
            {
                Kit.Line(BodyAt(_r.Pts[i]), BodyAt(_r.Pts[i + 1]), px, 0.12f, 1, FxLayer.Top, 3,
                    Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
            }
        }

        /// <summary>Level III: the path burns into the ground, white-hot cooling to the tier colour, with embers.</summary>
        private void Scar(float px)
        {
            for (int i = 0; i < _r.Pts.Length - 1; i++)
            {
                Kit.Line(_r.Pts[i], _r.Pts[i + 1], px, 1.4f, 1, FxLayer.Ground, 3,
                    _pal.Core, _pal.Edge, Fx.WithA(_pal.Dark, 0.6f), Fx.WithA(_pal.Dark, 0f));
            }
            int n = Fx.Count(7);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Along(Random.value) + new Vector2(0f, Random.Range(0f, 2f) * px);
                Kit.Ember(at, px, Random.Range(0.5f, 0.9f), new Vector2(Random.Range(-5f, 5f), Random.Range(10f, 22f)) * px, _pal.Edge);
            }
        }

        /// <summary>
        /// The cut marks the real ground (Kit.Ravage, the "Environment damage" setting), and the
        /// mark grows with the one who made it. II, when it cut through a line of them, scorches
        /// the tile each one was cut on (a single passing cut leaves nothing). III burns its whole
        /// path: an Expert a thin scorched line, a Master a band three tiles wide, a Grandmaster
        /// blasts it (the grass torn off down the middle), a God gouges a trench (the middle dug a
        /// step deeper, never into water, no fire: a blink is not a firestorm). Never where it
        /// stands: its own feet are a pocket that is only scorched (Kit.Ravage keep). Along the
        /// path the points are spaced by the real length of the legs (1 : 1.7 : 1.1), corners once.
        /// </summary>
        private void Burn(int hits)
        {
            if (_r.Kind != Kind.Strike || !_blink)
            {
                return;
            }
            int force;
            float width;
            if (Level >= 3)
            {
                force = _tier >= Tier.God ? 3 : _tier >= Tier.Grandmaster ? 2 : 1;
                width = _tier >= Tier.God ? 1.5f : _tier >= Tier.Master ? 1f : 0.6f;
            }
            else
            {
                if (hits < 2)
                {
                    return;
                }
                force = 1;
                width = _tier >= Tier.Master ? 1f : 0.6f;
            }
            Vector2 feet = _r.Final;
            float keepR = 1.3f * _k;
            int calls = 0;
            Kit.BeginTrench();
            try
            {
            if (Level < 3)
            {
                foreach (Victim w in _victims)
                {
                    if (w.Cut && calls < 6)
                    {
                        Kit.Ravage(w.U.current_position, width, force, A, false, feet, keepR);
                        calls++;
                    }
                }
            }
            else
            {
                float step = Mathf.Max(0.9f, width);
                for (int i = 0; i < _r.Pts.Length - 1 && calls < 24; i++)
                {
                    Vector2 a = _r.Pts[i], b = _r.Pts[i + 1];
                    int m = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / step));
                    for (int j = i == 0 ? 0 : 1; j <= m && calls < 24; j++)
                    {
                        Kit.Ravage(Vector2.Lerp(a, b, j / (float)m), width, force, A, false, feet, keepR);
                        calls++;
                    }
                }
            }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            if (Dev.On) Dev.Trace("flash " + Id + " ravage force " + force + " r " + width.ToString("F1") + " x" + calls);
        }

        /// <summary>After the held beat, everyone cut is thrown off the line.</summary>
        private void Knock()
        {
            _knockDone = true;
            _knockT = T;
            _lastBeat = Mathf.Max(_lastBeat, T);
            foreach (Victim w in _victims)
            {
                // a unit in a light move (a flow strike) is thrown out of it (Launch interrupts it)
                if (!w.Cut || w.Out || Gone(w.U, w.Id) || Moves.Locked(w.U))
                {
                    continue;
                }
                Actor u = w.U;
                // critters fly comically far; giants only stagger (Combat.Launch)
                bool critter = Weights.Of(u) == Weight.Critter;
                float dist = (critter ? 1.3f : 0.5f) * (Level >= 3 ? 1.2f : 1f) * _push;
                float apex = critter ? 0.7f : 0.22f;
                float spin = critter ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(500f, 800f) : 0f;
                Combat.Launch(u, u.current_position - w.Dir * 0.5f, dist, apex, spin);
            }
        }

        // -------------------------------------------------------------- echoes --

        /// <summary>A corner afterimage fires a crescent at the nearest victim.</summary>
        private void Echo(int i)
        {
            float px = Px;
            _ghostFire[i] = T;
            _lastBeat = Mathf.Max(_lastBeat, T);
            Vector2 from = _ghostPos[i];
            Actor v = EchoTarget(from, out bool reached);
            if (v == null)
            {
                // nothing left to strike: it swings at the air and fades with a glint
                if (Visible)
                {
                    Fx.Glint(from + new Vector2(0f, 3f * px), px, 0.2f, _pal.Core, 0.8f);
                }
                return;
            }
            Vector2 to = Body(v);
            Vector2 d = to - from;
            Vector2 dir = d.sqrMagnitude > 1e-6f ? d.normalized : _u;
            _ghostLunge[i] = dir * 2f * px;
            if (Visible)
            {
                // a crescent flies from the afterimage, cutting a bright line through the air
                Particle p = Fx.Spawn(Art.Arc(7, 0.8f, 10f, 170f, 2)[2], from, px, EchoFly, FxLayer.Top, 12);
                if (p != null)
                {
                    p.Moving(d / EchoFly);
                    p.FaceVel = true;
                    p.Color(_pal.Core, _pal.Edge);
                }
                Kit.Line(from, to, px, EchoFly + 0.06f, 1, FxLayer.Top, 2, Fx.WithA(_pal.Core, 0.75f), Fx.WithA(_pal.Edge, 0.5f), Fx.WithA(_pal.Edge, 0f));
            }
            _echoHits.Add(new EchoHit { U = v, Id = v.Sid(), At = T + EchoFly, Reached = reached, Dir = dir });
        }

        /// <summary>The nearest victim still standing, or (it died) another foe within 2 tiles of where it fell.</summary>
        private Actor EchoTarget(Vector2 from, out bool reached)
        {
            Actor best = null;
            float bd = float.MaxValue;
            foreach (Victim w in _victims)
            {
                // a victim the cut has just felled still counts as alive until the game buries it
                if (!w.Cut || w.Out || Gone(w.U, w.Id) || !w.U.hasHealth() || Moves.Locked(w.U))
                {
                    continue;
                }
                float d = (w.U.current_position - from).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = w.U;
                }
            }
            reached = best != null;
            if (best != null)
            {
                return best;
            }
            // everyone it cut is down: a fresh strike at whoever stands nearest where they fell
            // (it is reached anew, so one that slipped the cut may read this one too)
            Vector2 c = _mainPos != Vector2.zero ? _mainPos : Pose.Ground;
            return Retarget(c);
        }

        private void EchoLands(EchoHit h)
        {
            _lastBeat = Mathf.Max(_lastBeat, T);
            Actor v = h.U;
            if (Gone(v, h.Id) || !v.hasHealth() || Moves.Locked(v) || Moves.Intangible(v) || !Combat.Allowed(A, v, out float cm))
            {
                return;
            }
            if (!h.Reached)
            {
                if (Combat.Reach(this, v, true) != Contact.Hit)
                {
                    return;
                }
                // from now on it counts as cut (no second chance to read it)
                _victims.Add(new Victim { U = v, Id = h.Id, F = 1f, Dir = h.Dir, Cut = true });
            }
            Moves.Interrupt(v);   // the crescent cuts a flow of blows short
            HitResult r = Combat.Hit(A, v, EchoMult * cm, true, true, 0f, this);
            Vector2 at = Body(v);
            float px = Px;
            if (Fx.OnScreen(at, 0.3f))
            {
                Particle sw = Fx.Spawn(Art.Arc(7, 0.8f, 150f, -30f, 2), at, px, 0.14f, FxLayer.Over, 6);
                if (sw != null)
                {
                    sw.SX = h.Dir.x >= 0f ? 1f : -1f;
                    sw.Color(_pal.Core, _pal.Edge, Fx.WithA(_pal.Edge, 0f));
                }
                Kit.Sparks(at, px, 4, 60f, 110f, _pal.Edge);
                FlashUnit(v, Tiers.Of(v) != Tier.None && !r.Killed ? Tiers.Color(Tiers.Of(v)) : Color.white, 0.06f);
            }
            Kit.Stop(at, 0.05f);   // only when the main cut had none (HitStop allows one every 1.8 s)
            Anim.Sound(_armed ? "event:/SFX/HIT/HitFlesh" : "event:/SFX/OTHER/Punch", v.current_tile);
        }

        /// <summary>The corner afterimages: white when left, settling into the tier colour and shimmering; a flash as each strikes.</summary>
        private void Ghosts()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                Particle g = _ghosts[i];
                if (g == null)
                {
                    continue;
                }
                if (!g.Alive)
                {
                    _ghosts[i] = null;
                    continue;
                }
                Color c;
                if (_ghostFire[i] < 0f)
                {
                    float s = Mathf.Clamp01((T - _ghostBorn[i]) / 0.12f);
                    c = Color.Lerp(new Color(1f, 1f, 1f, 0.95f), _ghostIdle, s);
                    c.a *= 0.88f + 0.12f * ((((int)(T * 40f)) + i) & 1);
                }
                else
                {
                    float f = T - _ghostFire[i];
                    g.Pos = _ghostHome[i] + _ghostLunge[i] * Anim.OutCubic(Mathf.Clamp01(f / 0.05f));
                    if (f < 0.04f)
                    {
                        c = Color.white;
                    }
                    else
                    {
                        float k = Mathf.Clamp01((f - 0.04f) / 0.14f);
                        c = Fx.WithA(_ghostIdle, _ghostIdle.a * (1f - k));
                        if (k >= 1f)
                        {
                            g.Kill();
                            _ghosts[i] = null;
                            continue;
                        }
                    }
                }
                g.Grad = null;
                g.R.color = c;
            }
        }

        // ------------------------------------------------------------ stumble --

        /// <summary>A Novice's overshoot: head over heels past its target, and back up.</summary>
        private void Stumble()
        {
            float px = Px;
            Vector2 end = _r.Pts[_r.Pts.Length - 1];
            if (Enter() && Visible)
            {
                Fx.LandingPuff(end, px, Combat.TileAt(end));
            }
            float k = Mathf.Clamp01(_pt / TStumble);
            Pose.Ground = Vector2.Lerp(end, _r.Final, Anim.OutQuad(k));
            Pose.Rot = _crab ? 0f : 360f * Anim.InOutCubic(k);
            Pose.Height = 0.15f * _k * Mathf.Sin(Mathf.PI * k);
            float tuck = Mathf.Sin(Mathf.PI * k);
            Pose.SX = 1f - 0.08f * tuck;
            Pose.SY = 1f - 0.2f * tuck;
            Pose.Nudge = _crab ? Scuttle() : Vector2.zero;
            if (!_stumbleDust && k >= 0.55f)
            {
                _stumbleDust = true;
                if (Visible)
                {
                    Fx.LandingPuff(Pose.Ground, px, Combat.TileAt(Pose.Ground));
                }
            }
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Next(Phase.Recover);
            }
        }

        // ------------------------------------------------------------- recoil --

        /// <summary>Its blow was turned aside: rocked back, sliding away from the one who parried.</summary>
        private void Recoil()
        {
            if (Enter())
            {
                _recoilFrom = Pose.Ground;
                Vector2 away = _recoilFrom - _parryFrom;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : -_u;
                _recoilTo = _recoilFrom + away * 0.3f * _k;
                if (!Combat.SafeSpot(_recoilTo))
                {
                    _recoilTo = _recoilFrom;
                }
                if (!_crab && Mathf.Abs(away.x) > 0.12f)
                {
                    Pose.FaceRight = away.x < 0f;   // it faces the one who parried as it slides back
                }
                if (Visible)
                {
                    HeelDust(_recoilFrom, away, Px, A.current_tile, _k);   // the heels plough back
                }
            }
            float k = Mathf.Clamp01(_pt / _tRecoil);
            Pose.Ground = Vector2.Lerp(_recoilFrom, _recoilTo, Anim.OutCubic(k));
            Pose.Height = 0f;
            Pose.Rot = _crab ? 0f : -15f * (1f - Anim.Smooth(0.45f, 1f, k));
            Pose.SX = 1f;
            Pose.SY = 1f - 0.05f * (1f - k);
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Pose.SY = 1f;
                EndLink = Link.None;
                Finished = true;
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover()
        {
            if (Enter())
            {
                Pose.Nudge = Vector2.zero;
                if (_r.Kind != Kind.Retreat && !_flow)
                {
                    // below Master it catches its breath before swinging again; a Master never
                    // stands and swings after it (FlowStrikes takes over between techniques)
                    A.startAttackCooldown();
                }
                Actor link = LinkUp();
                Actor next = link == null ? Onward() : null;
                Vector2? look = null;
                if (link != null)
                {
                    look = link.current_position;
                }
                else if (next != null)
                {
                    look = next.current_position;   // its cut felled them: already turning to the next one
                }
                else if ((_r.Kind == Kind.Fumble || _slipped) && !Gone(_target, _targetId))
                {
                    look = _target.current_position;   // the one it tumbled past, or the one that slipped it
                }
                else if (_r.HasFace)
                {
                    look = _r.Face;
                }
                if (look.HasValue && !_crab)
                {
                    float dx = look.Value.x - Pose.Ground.x;
                    if (Mathf.Abs(dx) > 0.1f && dx > 0f != Pose.FaceRight)
                    {
                        Pose.FaceRight = dx > 0f;
                        _turned = true;
                    }
                }
                if (_r.Kind == Kind.Strike && link == null && !_slipped && Visible)
                {
                    // nothing left to fight here: a pose and a glint (not when its cut met a mirage)
                    float px = Px;
                    Fx.Glint(HeadAt(Pose.Ground) + new Vector2((Pose.FaceRight ? 3f : -3f) * px, 1f * px), px, 0.22f, _pal.Core, 0.9f);
                }
                _rot0 = Pose.Rot;
                _sx0 = Pose.SX;
                _sy0 = Pose.SY;
            }
            float t = _pt;
            float amp = Anim.Spring(t, 9f, 17f);
            Pose.SX = 1f + (_sx0 - 1f) * amp;
            Pose.SY = 1f + (_sy0 - 1f) * amp;
            Pose.Rot = _rot0 * Mathf.Exp(-10f * t);
            // a little hop as it turns around
            Pose.Height = _turned ? 0.05f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / Mathf.Min(0.12f, _tRecover))) : 0f;
            Pose.Nudge = _crab && t < 0.1f ? Scuttle() : Vector2.zero;
            // a master's afterimages may still be striking behind it: it holds its stance until
            // the last one has (a few hundredths of a second at most)
            if (_pt >= _tRecover && !EchoesPending)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Pose.Nudge = Vector2.zero;
                if (EndTarget != null && (!EndTarget.isAlive() || !EndTarget.hasHealth()))
                {
                    // an afterimage felled the one it meant to chain on: the chain goes on to the next
                    if (LinkUp() == null)
                    {
                        Onward();
                    }
                }
                Finished = true;
            }
        }

        /// <summary>The chain it leaves: Close on the target it reached (or another one it cut, right beside it).</summary>
        private Actor LinkUp()
        {
            EndLink = Link.None;
            EndTarget = null;
            if (_r.Kind != Kind.Strike || _parried)
            {
                return null;
            }
            Actor best = null;
            if (!_blink)
            {
                if (_jabLanded && !Gone(_jabV, _jabId) && _jabV.hasHealth() && Combat.IsFoe(A, _jabV))
                {
                    best = _jabV;
                }
            }
            else
            {
                // the nearest one it cut (it came out past the whole line, so the one it came for
                // is often the furthest away now); the target wins a near tie
                float bd = float.MaxValue;
                foreach (Victim w in _victims)
                {
                    if (!w.Cut || w.Out || Gone(w.U, w.Id) || !w.U.hasHealth() || !Combat.IsFoe(A, w.U) || !Combat.Allowed(A, w.U))
                    {
                        continue;
                    }
                    float d = Vector2.Distance(w.U.current_position, Pose.Ground) - (w.Main ? 0.6f : 0f);
                    if (d < bd && d <= 3.5f)
                    {
                        bd = d;
                        best = w.U;
                    }
                }
            }
            if (best != null)
            {
                EndLink = Link.Close;
                EndTarget = best;
            }
            return best;
        }

        /// <summary>
        /// Its blow landed but no one it struck is left standing beside it: the chain goes on
        /// (Scattered) towards the nearest enemy still standing within 6 tiles, so the next
        /// technique follows straight on instead of the fight stalling on a pose.
        /// </summary>
        private Actor Onward()
        {
            if (_r.Kind != Kind.Strike || _parried || _slipped || _cutHits <= 0 && !_jabLanded)
            {
                return null;
            }
            Actor best = null;
            float bd = float.MaxValue;
            Vector2 at = Pose.Ground;
            foreach (Actor u in Combat.FoesAround(A, at, 6f))
            {
                if (!u.hasHealth() || Moves.Locked(u) || !Combat.Allowed(A, u))
                {
                    continue;
                }
                float d = (u.current_position - at).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
            if (best != null)
            {
                EndLink = Link.Scattered;
                EndTarget = best;
            }
            return best;
        }

        // ------------------------------------------------------------- cleanup --

        private void Drop()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                _ghosts[i]?.Kill();
                _ghosts[i] = null;
            }
            _echoHits.Clear();
        }

        public override void Abort()
        {
            Drop();
        }

        /// <summary>
        /// A master's move ends as its last afterimages strike: the ones that have fired finish
        /// their flash and fade on their own (killed mid-fade they popped out at half strength).
        /// </summary>
        private void FadeOut()
        {
            for (int i = 0; i < _ghosts.Length; i++)
            {
                Particle g = _ghosts[i];
                if (g == null || !g.Alive || _ghostFire[i] < 0f)
                {
                    continue;
                }
                float f0 = T - _ghostFire[i];
                Vector2 home = _ghostHome[i], lunge = _ghostLunge[i];
                Color idle = _ghostIdle;
                g.Held = false;
                g.Grad = null;
                g.Age = 0f;
                g.Life = Mathf.Max(0.01f, 0.18f - f0);
                g.Step = (q, d) =>
                {
                    // the same flash and fade as Ghosts()
                    float f = f0 + q.Age;
                    q.Pos = home + lunge * Anim.OutCubic(Mathf.Clamp01(f / 0.05f));
                    q.R.color = f < 0.04f ? Color.white : Fx.WithA(idle, idle.a * (1f - Mathf.Clamp01((f - 0.04f) / 0.14f)));
                };
                _ghosts[i] = null;
            }
        }

        public override void OnEnd(bool aborted)
        {
            if (!aborted)
            {
                FadeOut();
            }
            Drop();
            if (aborted)
            {
                _victims.Clear();
            }
        }
    }

    /// <summary>
    /// Flash Step as a technique. Entry: Novice. A gap-closer, and only for a gap that will not
    /// close by itself: a target on the ground 2.5 to 4 / 6 / 8 tiles away (a III: 9 for a Master,
    /// 10 for a Grandmaster, 12 for a God) with a clear way there that shoots, is hounding one of
    /// its allies, is wounded and getting away, or is the one it is fighting standing 4+ tiles
    /// off (a melee enemy coming for it, or walking its way, is met on foot: two lines charging
    /// each other meet by themselves); or, at II and III, any line of 2+
    /// enemies it can cut through on the way. One passing cut is valued at 70% (the combo comes
    /// after); a lone chase from a link at 75%, so other follow-ups get their turn. A lone chase
    /// right after someone beside it has blinked (1.2 s, 6 tiles) waits: two blinks at one enemy
    /// read as a strobe. At 25% health or less with two enemies on it, it gets away towards its
    /// allies. Masters and up never walk up to the one they fight: a gap of 3 tiles or more is a
    /// blink, or, by the mood of the moment (about half the time), a blur dash (FlowStrikes) and
    /// whatever technique fits in reach. Chains from a scattered crowd, a downed enemy or one
    /// thrown into a pile; leaves Close on the target it reached (Scattered towards the next
    /// enemy if its cut felled them). Targets in a light move (a flow strike) are fair game.
    /// </summary>
    internal sealed class FlashStepTech : Technique
    {
        public override string Id => "flash_step";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Mobility;

        /// <summary>A blur dash through the target and everyone in the way; the cut lands a beat later.</summary>
        public override Form Forms => Form.Dash | Form.Teleport | Form.Fast | Form.Straight | Form.Blade;

        public override float[] ManaCost => new[] { 8f, 12f, 18f };
        public override float[] Cooldown => new[] { 6f, 6f, 7f };
        public override string Icon => "ui/Icons/sakuga/iconFlashStep";

        private static readonly float[] Fan = { 0f, 30f, -30f, 60f, -60f };

        /// <summary>Scratch list for planning (the Brain plans on the main thread, one fighter at a time).</summary>
        private static readonly List<Actor> _on = new List<Actor>();

        /// <summary>
        /// How far it blinks: 4 / 6 / 8 tiles by level, and at III further the mightier it is
        /// (Master 9, Grandmaster 10, God 12: two gods cross a battlefield in a blink).
        /// </summary>
        public static float MaxRange(int level, Tier tier = Tier.None)
        {
            if (level <= 1)
            {
                return 4f;
            }
            if (level == 2)
            {
                return 6f;
            }
            return 8f + (tier >= Tier.God ? 4f : tier >= Tier.Grandmaster ? 2f : tier >= Tier.Master ? 1f : 0f);
        }

        /// <summary>
        /// A scattered crowd or a downed enemy to reach, or a victim thrown into a pile of them (a
        /// line to cut through).
        /// </summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Down || link == Link.Thrown;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float ka = FlashStep.KOf(c.Self);
            float max = MaxRange(level, c.Tier);
            // in trouble: get out first
            if (!c.Forced && c.HpPct <= 0.25f)
            {
                Plan away = Retreat(c, ka, max);
                if (away != null)
                {
                    return away;
                }
            }
            // a god power or dev command pointing at the ground: just go there
            if (c.Forced && c.ForceTarget == null && c.ForcePoint.HasValue)
            {
                return Reposition(c, c.ForcePoint.Value, ka, max);
            }
            Actor v = c.Focus;
            Plan best = Strike(c, v, level, ka, max);
            if (best == null && c.Forced)
            {
                best = Strike(c, Nearest(c, max, v), level, ka, max);
            }
            if (!c.Forced)
            {
                // a line of them is worth a detour (II, III)
                if (level >= 2)
                {
                    best = Better(best, Strike(c, LineTarget(c, ka, max, v), level, ka, max));
                }
                // an archer picking at its side, or an enemy hounding one of its allies: worth
                // going for even when it is not the one it was fighting
                if (!c.Linked)
                {
                    best = Better(best, Strike(c, Calling(c, max, v), level, ka, max));
                }
            }
            return best;
        }

        private static Plan Better(Plan a, Plan b)
        {
            return b != null && (a == null || b.Score > a.Score) ? b : a;
        }

        /// <summary>Shoots (bows, staffs, spit): closing the gap is the whole answer to it.</summary>
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

        /// <summary>A melee enemy already coming for it: the gap closes by itself, no need to dash.</summary>
        private static bool Coming(Actor v, Actor self)
        {
            try
            {
                return v.has_attack_target && v.attack_target == self;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Walking its way: the gap is closing by itself.</summary>
        private static bool Closing(Actor v, Vector2 self)
        {
            try
            {
                if (!v.is_moving)
                {
                    return false;
                }
                Vector2 p = v.current_position;
                Vector2 step = v.next_step_position - p;
                Vector2 to = self - p;
                if (step.sqrMagnitude < 1e-6f || step.sqrMagnitude > 9f || to.sqrMagnitude < 1e-4f)
                {
                    return false;   // standing, or no real next step
                }
                return Vector2.Dot(step.normalized, to.normalized) > 0.5f;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// A fighter's mood for the next few seconds (0..1, steady for 3 s, then a new one): some
        /// moments a master blinks in, others it lets a blur dash take it there.
        /// </summary>
        private static float Mood(Actor a)
        {
            unchecked
            {
                uint h = (uint)a.Sid() * 2654435761u ^ (uint)(long)(Fighter.Now / 3.0) * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFu) / 65535f;
            }
        }

        /// <summary>It is attacking one of our allies (cut it down before it finishes them).</summary>
        private static bool Hounding(Ctx c, Actor v)
        {
            try
            {
                if (!v.has_attack_target || v.attack_target == null || !v.attack_target.isActor())
                {
                    return false;
                }
                Actor t = v.attack_target.a;
                return t != null && t != c.Self && t.isAlive() && Combat.IsAlly(c.Self, t);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The nearest enemy in dash range that calls for one even if it is not its target: an
        /// archer (or any shooter), or one hounding an ally.
        /// </summary>
        private static Actor Calling(Ctx c, float max, Actor exclude)
        {
            for (int i = 0; i < c.Enemies.Count && i < 12; i++)
            {
                float d = c.EnemyDist[i];
                if (d > max)
                {
                    break;
                }
                Actor u = c.Enemies[i];
                if (d < FlashStep.MinRange || u == exclude || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u))
                {
                    continue;
                }
                if (Ranged(u) || Hounding(c, u))
                {
                    return u;
                }
            }
            return null;
        }

        private static Plan Strike(Ctx c, Actor v, int level, float ka, float max)
        {
            if (v == null || !v.isAlive() || !Combat.IsFoe(c.Self, v) || !Combat.Grounded(v) || Moves.Locked(v) || !c.Allowed(v, out float cm))
            {
                return null;
            }
            if (Moves.TryGet(v, out Move vm) && vm is BlurDash)
            {
                return null;   // a master blurring across the field: it won't be where the dash ends
            }
            float d = c.Dist(v);
            float lo = c.Forced ? (level >= 2 ? 0.6f : 1.6f) : FlashStep.MinRange;
            float hi = c.Forced ? max * 1.5f : max;
            if (d < lo || d > hi)
            {
                return null;
            }
            // Why dash at all? A gap-closer is the answer to a gap that will not close by itself:
            // a shooter, an enemy on one of its allies, a wounded one getting away, the one it is
            // fighting standing well off (or flung away by its last blow: a chain). A melee enemy
            // already coming for it is met on foot. Cheap questions first: the route is the costly part.
            bool focus = v == c.Focus;
            bool ranged = Ranged(v);
            bool rescue = !ranged && Hounding(c, v);
            // coming for it, or walking its way (two lines charging each other meet by themselves:
            // a whole army blinking at once at the first clash read as one strobe)
            bool coming = !ranged && !rescue && (Coming(v, c.Self) || Closing(v, c.Pos));
            bool runner = !coming && Combat.HpPct(v) <= 0.3f;
            bool flow = FlashStep.Flows(c.Tier);
            // a Master does not walk up to the one it is fighting: any real gap is a blink, or
            // (now and then, by its mood of the moment) a blur dash and another technique in reach
            float far = flow ? FlashStep.MinRange + 0.5f : 4f;
            bool plain = focus && !c.Linked && !coming && d >= far && (c.Forced || !flow || Mood(c.Self) < 0.55f);
            bool gap = ranged || rescue || runner || focus && c.Linked || plain;
            float width = FlashStep.PathWidth(ka);
            if (!c.Forced && !gap && (level <= 1 || c.OnRay((v.current_position - c.Pos) / d, d + 1f, width) < 2))
            {
                return null;   // nothing to chase, and no line of them to cut through
            }
            FlashStep.Route r = null;
            if (level <= 1)
            {
                // Novices are eager and a little clumsy
                if (!c.Forced && c.Tier == Tier.Novice && Random.value < 0.1f)
                {
                    r = FlashStep.Fumble(c.Pos, v, ka);
                }
                r ??= FlashStep.Straight(c.Pos, v, ka, false);
            }
            else
            {
                if (level >= 3)
                {
                    r = BestZig(c, v, ka);
                }
                r ??= FlashStep.Straight(c.Pos, v, ka, true) ?? FlashStep.Straight(c.Pos, v, ka, false);
            }
            if (r == null)
            {
                return null;
            }
            ClearExit(c, r, v, ka);
            int count = 1;
            float score;
            if (level <= 1)
            {
                score = c.Value(v, c.Expected(v, FlashStep.JabMult), cm);
            }
            else
            {
                List<Actor> on = _on;
                on.Clear();
                if (r.Pts.Length >= 4)
                {
                    CountOnPath(c, r, width, on);
                }
                else
                {
                    Vector2 to = r.Final - c.Pos;
                    c.OnRay(to, to.magnitude + 0.3f, width, on);
                }
                if (!on.Contains(v))
                {
                    on.Add(v);
                }
                count = on.Count;
                float mult = FlashStep.CutMult(level);
                score = 0f;
                foreach (Actor u in on)
                {
                    c.Allowed(u, out float m);
                    score += c.Value(u, c.Expected(u, mult), m);
                }
                on.Clear();
                if (level >= 3)
                {
                    score += 0.4f * 3f * c.Value(v, c.Expected(v, FlashStep.EchoMult), cm);   // the echoes (they may find no one)
                }
            }
            if (!c.Forced && !gap && count < 2)
            {
                return null;   // the line it hoped for isn't on the route after all
            }
            if (!c.Forced && !c.Linked && count < 2 && !ranged && !rescue && FlashStep.RecentNear(c.Pos, 1.2, 6f))
            {
                return null;   // someone beside it has just blinked: a second blink for one enemy is a strobe
            }
            if (count >= 2)
            {
                score += 14f * (count - 1);   // a line of them: the right answer
            }
            else
            {
                score *= 0.7f;   // one passing cut: the dash is mostly travel, the combo comes after
            }
            // the gap it closes: worth the most against a shooter, then to save an ally, to catch a runner
            float why = ranged ? 0.55f : rescue ? 0.45f : runner ? 0.35f : gap ? 0.2f : 0f;
            score += c.D * why * Mathf.Clamp(d / 4f, 0.6f, 1.6f) + (ranged ? 12f : rescue ? 10f : runner ? 6f : 0f);
            if (c.Linked && count < 2)
            {
                score *= 0.75f;   // chasing one flung enemy: the other follow-ups get their turn first
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = v.current_position - c.Pos;
            return new Plan
            {
                Target = v,
                Point = r.Final,
                Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right,
                Count = count,
                Score = score,
                Data = r
            };
        }

        /// <summary>
        /// A pass-through comes out in the open. In a cluster or a file of enemies the end of the
        /// line fell among the ones standing behind the target: the back-turned zanshin was lost
        /// among their sprites (and it got stabbed while it stood there). The end of the last leg
        /// moves on past everyone standing on or beside it, up to 4.5 tiles further, and the blur
        /// cuts them too; with no clear ground within reach the route stays as it was. The plan
        /// sees only the units the fighter is aware of: Begin looks again at the real ground.
        /// </summary>
        private static void ClearExit(Ctx c, FlashStep.Route r, Actor v, float ka)
        {
            FlashStep.PushExit(r, v, ka, c.Enemies, 24, c.Allies, 12);
        }

        /// <summary>Level III: the zig-zag that cuts the most, trying the wider one (or none) if walls or water are in the way.</summary>
        private static FlashStep.Route BestZig(Ctx c, Actor v, float ka)
        {
            float width = FlashStep.PathWidth(ka);
            int first = Random.value < 0.5f ? 1 : -1;
            FlashStep.Route best = null;
            int bestN = -1;
            for (int pass = 0; pass < 2 && best == null; pass++)
            {
                float deg = pass == 0 ? 35f : 55f;
                for (int s = 0; s < 2; s++)
                {
                    FlashStep.Route r = FlashStep.Zig(c.Pos, v, ka, s == 0 ? first : -first, deg);
                    if (r == null)
                    {
                        continue;
                    }
                    int n = CountOnPath(c, r, width, null);
                    if (n > bestN)
                    {
                        best = r;
                        bestN = n;
                    }
                }
            }
            return best;
        }

        /// <summary>Allowed, grounded enemies the route would cut.</summary>
        private static int CountOnPath(Ctx c, FlashStep.Route r, float width, List<Actor> into)
        {
            int n = 0;
            foreach (Actor u in c.Enemies)
            {
                if (!Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u))
                {
                    continue;
                }
                if (FlashStep.DistTo(r, u.current_position) <= width + u.stats["size"] * 0.5f)
                {
                    n++;
                    into?.Add(u);
                }
            }
            return n;
        }

        /// <summary>The enemy in range that lines up the most others behind or before it.</summary>
        private static Actor LineTarget(Ctx c, float ka, float max, Actor exclude)
        {
            Actor best = null;
            int bestN = 1;
            float width = FlashStep.PathWidth(ka);
            for (int i = 0; i < c.Enemies.Count && i < 16; i++)
            {
                float d = c.EnemyDist[i];
                if (d < FlashStep.MinRange || d > max)
                {
                    continue;
                }
                Actor u = c.Enemies[i];
                if (u == exclude || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u))
                {
                    continue;
                }
                int n = c.OnRay((u.current_position - c.Pos) / d, d + 1f, width);
                if (n > bestN)
                {
                    bestN = n;
                    best = u;
                }
            }
            return bestN >= 2 ? best : null;
        }

        /// <summary>The nearest enemy it could dash at (chains and forced plays whose target is out of reach).</summary>
        private static Actor Nearest(Ctx c, float max, Actor exclude)
        {
            float lo = c.Forced ? 1.6f : FlashStep.MinRange;
            float hi = c.Forced ? max * 1.5f : max;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                float d = c.EnemyDist[i];
                if (d > hi)
                {
                    break;
                }
                Actor u = c.Enemies[i];
                if (d < lo || u == exclude || !Combat.Grounded(u) || Moves.Locked(u) || !c.Allowed(u))
                {
                    continue;
                }
                return u;
            }
            return null;
        }

        /// <summary>Badly hurt with two enemies on it: dash away towards its allies (or just away).</summary>
        private static Plan Retreat(Ctx c, float ka, float max)
        {
            int adj = c.Surrounding(1.75f);
            if (adj < 2)
            {
                return null;
            }
            Vector2 threat = Threat(c);
            Vector2 want = Vector2.zero;
            bool allies = false;
            float allyD = 0f;
            foreach (Actor al in c.Allies)
            {
                float d = c.Dist(al);
                if (d >= 2.5f && d <= 16f)
                {
                    want = (al.current_position - c.Pos) / d;
                    allies = true;
                    allyD = d;
                    break;
                }
            }
            if (!allies)
            {
                want = c.Pos - threat;
                if (want.sqrMagnitude < 1e-4f)
                {
                    want = new Vector2(c.Self.flip ? -1f : 1f, 0f);
                }
                want.Normalize();
            }
            float len = allies ? Mathf.Clamp(allyD - 1f, FlashStep.MinRange, max) : max;
            for (int i = 0; i < Fan.Length; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    FlashStep.Route r = FlashStep.Away(c.Pos, FlashStep.Rotate(want, Fan[i]), j == 0 ? len : len * 0.65f, ka);
                    if (r == null || EnemiesNear(c, r.Final, 1.75f) >= 2)
                    {
                        continue;
                    }
                    r.Face = threat;
                    r.HasFace = true;
                    return new Plan
                    {
                        Point = r.Final,
                        Dir = (r.Final - c.Pos).normalized,
                        Score = c.D * 2.2f + 14f * adj + (allies ? 12f : 0f),
                        Data = r
                    };
                }
            }
            return null;
        }

        private static Plan Reposition(Ctx c, Vector2 point, float ka, float max)
        {
            Vector2 d = point - c.Pos;
            float len = Mathf.Min(d.magnitude, max * 1.5f);
            FlashStep.Route r = FlashStep.Away(c.Pos, d, len, ka);
            if (r == null)
            {
                return null;
            }
            r.Face = point + d.normalized;
            r.HasFace = true;
            return new Plan { Point = r.Final, Dir = d.normalized, Score = 1f, Data = r };
        }

        /// <summary>The middle of the enemies right on top of it.</summary>
        private static Vector2 Threat(Ctx c)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 3f; i++)
            {
                sum += c.Enemies[i].current_position;
                n++;
            }
            return n > 0 ? sum / n : c.Pos;
        }

        private static int EnemiesNear(Ctx c, Vector2 p, float r)
        {
            int n = 0;
            float r2 = r * r;
            foreach (Actor u in c.Enemies)
            {
                if ((u.current_position - p).sqrMagnitude <= r2)
                {
                    n++;
                }
            }
            return n;
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Data is FlashStep.Route r && FlashStep.Begin(a, r, p.Target, p.Level, this);
        }
    }
}
