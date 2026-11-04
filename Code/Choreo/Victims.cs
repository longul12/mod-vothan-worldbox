using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>How a body takes a blow (Victims.Hurt).</summary>
    internal enum Hurt
    {
        /// <summary>Nothing but the hit flash.</summary>
        None,
        /// <summary>A jab's twitch: a few degrees back, a pixel's shove, springs straight back.</summary>
        Flinch,
        /// <summary>The head snaps back, it slides back a little on its heels, and rocks forward again.</summary>
        SnapBack,
        /// <summary>A body blow: it folds over the fist (squashed, bent forward), holds it trembling, straightens.</summary>
        Fold,
        /// <summary>A heavy push: it skids back 'Push' tiles leaning back (dust, skid marks), then stumbles.</summary>
        Reel,
        /// <summary>A hook: it spins round on the spot and staggers sideways.</summary>
        Spin,
        /// <summary>Feet swept up: it falls on its back and lies there dazed (Link.Down).</summary>
        Knockdown,
        /// <summary>
        /// Sent flying: the head snaps back, it is lifted 'Lift' tiles in a backflip and comes down 'Push'
        /// tiles away (on its back when the blow was heavy). Airborne it is Link.Launched: the engine's air
        /// follow-ups take it out of the air (Victims.Grab / Hurt).
        /// </summary>
        Launch,
        /// <summary>Popped up and hanging in the air for the performer's own follow-ups (anime hang time), then it falls.</summary>
        Juggle,
        /// <summary>Hammered down: crushed into the ground (squashed, sunk), then it lies (Link.Down).</summary>
        DriveIn,
        /// <summary>Held flat on its back, trembling, for 'Hold' seconds (Link.Down).</summary>
        Pin,
        /// <summary>Tossed along an arc to 'To' (or 'Push' tiles along Dir), tumbling, and it crashes there (Link.Thrown).</summary>
        Thrown,
        /// <summary>Its knees buckle and it falls forward (a finisher; a doomed body dies as it lands).</summary>
        Crumple,
        /// <summary>Rocked where it stands (no puppet: giants, the heavy, anything that can't be moved).</summary>
        Stagger
    }

    /// <summary>
    /// What a blow does to a body: the reaction and its numbers. 'Dir' is the push (from the attacker
    /// through the victim); 'Power' (0.3 light .. 2 huge) scales how far its head snaps, how hard it
    /// folds, how long the impact holds.
    /// </summary>
    internal struct Impact
    {
        public Hurt Hurt;
        public Vector2 Dir;
        /// <summary>Tiles it is pushed (Reel, SnapBack, Thrown without a To, Launch's distance).</summary>
        public float Push;
        /// <summary>Tiles of height (Launch's apex, Juggle's hang height, Knockdown's hop, Thrown's arc).</summary>
        public float Lift;
        /// <summary>Seconds dazed when it is let go (0: on its feet at once).</summary>
        public float Daze;
        public float Power;
        /// <summary>Seconds a Fold or a Pin holds, a Juggle hangs.</summary>
        public float Hold;
        /// <summary>Degrees it turns in the air (Thrown, Launch's tumble).</summary>
        public float Spin;
        /// <summary>Thrown: where it lands (Vector2.zero: Push tiles along Dir).</summary>
        public Vector2 To;
        /// <summary>The blow would have killed it: it dies where it comes to rest.</summary>
        public bool Owed;
        /// <summary>Thrown/DriveIn: the landing breaks the ground (base radius in tiles, 0: no crater) at the move's Scale.</summary>
        public float Crater;
        /// <summary>Its crater never sets the grass alight (it only breaks the ground).</summary>
        public bool NoFire;
        /// <summary>Thrown: the crash hurts the thrower's enemies within AreaR tiles (x AreaMult of its damage).</summary>
        public float AreaR, AreaMult;
        /// <summary>Seconds of local hit-stop the body holds its first pose (the blow's impact frame).</summary>
        public float Stop;

        public static Impact Of(Hurt h, Vector2 dir, float push = 0f, float power = 1f)
        {
            return new Impact { Hurt = h, Dir = dir, Push = push, Power = power, Lift = -1f, Spin = float.NaN };
        }
    }

    /// <summary>
    /// The bodies the engine moves: a blow's reaction (it plays on the victim on its own, so the
    /// attacker is free to wind up the next blow) or a hold (a grab: the holder's script moves it).
    /// Each is a Puppet (the framework's victim move: everything else in the mod knows it is "in
    /// someone's hands"), driven from here every frame. Reactions blend from whatever pose the body
    /// is in, so a jab, a cross and a hook flow into one another. Too heavy or too big to move
    /// (Titans, Crabzilla, a dragon), flying, or held in someone else's move: it only flashes,
    /// shakes and staggers. Killed, picked up by the god hand, recycled: the framework lets it go and
    /// it is forgotten here (a body killed in the air falls on its own).
    /// </summary>
    internal static class Victims
    {
        private static readonly List<Victim> _list = new List<Victim>();

        public static int Count => _list.Count;

        /// <summary>The engine's body for this unit, if it has one right now.</summary>
        public static Victim Get(Actor v)
        {
            if (v == null || _list.Count == 0)
            {
                return null;
            }
            long id = v.Sid();
            for (int i = 0; i < _list.Count; i++)
            {
                Victim b = _list[i];
                if (b.A == v && b.Id == id && !b.Done && b.P != null && !b.P.Finished)
                {
                    return b;
                }
            }
            return null;
        }

        /// <summary>Is this unit's move one of the engine's reactions (fair game for the next blow)?</summary>
        public static bool Ours(Actor v)
        {
            Victim b = Get(v);
            return b != null && b.Holder == null && Moves.TryGet(v, out Move m) && m == b.P;
        }

        /// <summary>Where a unit stands (its pose's ground while a move moves it).</summary>
        public static Vector2 GroundOf(Actor v)
        {
            if (v == null)
            {
                return Vector2.zero;
            }
            return Moves.TryGet(v, out Move m) ? m.Pose.Ground : v.current_position;
        }

        /// <summary>
        /// 'v' takes a blow of 'by' (in move 'move'): it reacts as 'im' says (see Hurt). Returns its body
        /// (null if it only rocked: too heavy, flying, held by someone else, or launched free).
        /// </summary>
        public static Victim Hurt(Actor v, Actor by, Move move, Impact im)
        {
            if (v == null || !v.isAlive())
            {
                return null;
            }
            if (im.Hurt != Sakuga.Hurt.None && RealmControl.BlocksLower(v, by))
            {
                Flash(v, Color.white, 0.05f);
                return Get(v);   // damage landed; only the control reaction is denied
            }
            if (im.Dir.sqrMagnitude < 1e-6f)
            {
                im.Dir = by != null ? v.current_position - by.current_position : Vector2.right;
                if (im.Dir.sqrMagnitude < 1e-6f)
                {
                    im.Dir = Vector2.right;
                }
            }
            im.Dir.Normalize();
            if (im.Power <= 0f)
            {
                im.Power = 1f;
            }
            Victim b = Get(v);
            if (b != null && b.Holder != null)
            {
                b.P.Flash(0.06f);
                return b;   // in someone's hands: the holder's script moves it
            }
            if (im.Hurt == Sakuga.Hurt.None)
            {
                Flash(v, Color.white, 0.05f);
                return b;
            }
            // (one of the engine's own bodies is ours to move at any height: a launch, a juggle's hang)
            if (im.Hurt == Sakuga.Hurt.Stagger || !Movable(v, by, b != null))
            {
                Rock(v, im, by, move);
                return null;
            }
            if (b != null && b.Aloft && Light(im.Hurt))
            {
                // a light blow on a body the engine holds up in the air: it keeps flying (a flinch would drop it)
                b.KeepUp(im);
                if (im.Owed && by != null)
                {
                    MeteorSlam.Doom(by, v, move);
                }
                return b;
            }
            if (b == null)
            {
                // (a unit caught in a little hop, its own attack's or a dodge, is taken out of the air: Movable)
                if (!Choreo.Free(v))
                {
                    Rock(v, im, by, move);
                    return null;
                }
                b = Take(v, by, move);
                if (b == null)
                {
                    if (im.Hurt == Sakuga.Hurt.Launch)
                    {
                        LaunchFree(v, null, im, by, move);
                    }
                    else
                    {
                        Rock(v, im, by, move);
                    }
                    return null;
                }
            }
            b.Start(im, by, move);
            return b;
        }

        /// <summary>
        /// Takes hold of 'v' for 'holder' (a grab): its pose follows the holder's hands (Grip) until it
        /// is let go. Null if it can't be held (too heavy, flying, in someone else's move).
        /// </summary>
        public static Victim Grab(Actor v, ChoreoMove holder)
        {
            if (v == null || holder == null || !v.isAlive())
            {
                return null;
            }
            if (RealmControl.BlocksLower(v, holder.A))
            {
                return null;
            }
            Victim b = Get(v);
            // (one of the engine's own bodies is taken out of the air too: an air follow-up's grab)
            if (!Movable(v, holder.A, b != null))
            {
                return null;
            }
            if (b != null && b.Holder != null && b.Holder != holder)
            {
                return null;
            }
            if (b == null)
            {
                // (a unit gathering power is taken out of its charge: the hold disperses it, Moves.Start)
                if (!Choreo.Free(v) && !Moves.Charging(v))
                {
                    return null;
                }
                b = Take(v, holder.A, holder);
                if (b == null)
                {
                    return null;
                }
            }
            b.HoldBy(holder);
            return b;
        }

        /// <summary>Can the engine move this body at all (not too heavy, too big for 'by', flying, a boat, up in the air...)?</summary>
        public static bool Movable(Actor v, Actor by)
        {
            return Movable(v, by, false);
        }

        /// <summary>
        /// Movable; 'ours': the engine already moves this body (one of its reactions, Victims.Ours): its
        /// height is its pose's (a launch, a juggle's hang), not a hop of its own, so any height will do.
        /// </summary>
        public static bool Movable(Actor v, Actor by, bool ours)
        {
            if (v == null || v.isFlying() || Weights.Heavy(v) || v.is_immovable || v.isInsideSomething() || v.isInMagnet())
            {
                return false;
            }
            ActorAsset s = v.asset;
            if (s == null || !s.can_be_moved_by_powers || !s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render)
            {
                return false;
            }
            if (ControllableUnit.isControllingUnit(v))
            {
                return false;
            }
            float big = by != null ? Mathf.Max(0.3f, by.actor_scale * 2.4f) : 0.3f;
            return v.actor_scale <= big && (ours || v.position_height <= HundredFists.HopMax);
        }

        /// <summary>A reaction that brings a body back to its feet on the ground (a flying one ignores it).</summary>
        private static bool Light(Hurt h)
        {
            return h == Sakuga.Hurt.Flinch || h == Sakuga.Hurt.SnapBack || h == Sakuga.Hurt.Fold || h == Sakuga.Hurt.Reel || h == Sakuga.Hurt.Spin;
        }

        private static Victim Take(Actor v, Actor by, Move owner)
        {
            if (v.position_height > HundredFists.HopMax)
            {
                return null;
            }
            float dx = by != null ? by.current_position.x - v.current_position.x : 0f;
            Puppet p = HundredFists.Take(v, Mathf.Abs(dx) > 0.05f ? dx > 0f : v.flip, owner);
            if (p == null)
            {
                return null;
            }
            Victim b = new Victim(v, p);
            _list.Add(b);
            return b;
        }

        /// <summary>Launched free (the game's physics): others' air follow-ups can take it. A doomed body dies where it lands.</summary>
        private static void LaunchFree(Actor v, Victim b, Impact im, Actor by, Move move)
        {
            float dist = im.Push > 0f ? im.Push : 1.2f;
            float apex = im.Lift > 0f ? im.Lift : Choreo.Apex(dist);
            float spin = float.IsNaN(im.Spin) ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(540f, 900f) : im.Spin;
            Flash(v, Color.white, 0.06f);
            if (b != null)
            {
                b.Let(false);   // its puppet ends on the next update; the launch waits for it
                Choreo.Defer(v, im.Dir, dist, spin, apex);
            }
            else
            {
                Choreo.Launch(v, im.Dir, dist, spin, apex);
            }
            if (im.Owed && by != null)
            {
                MeteorSlam.Doom(by, v, move);
            }
        }

        /// <summary>A body that can't be moved takes the blow where it stands: a flash, a shake, a stagger for a hard one.</summary>
        private static void Rock(Actor v, Impact im, Actor by, Move move)
        {
            Flash(v, Color.white, 0.06f);
            float hard = im.Power;
            switch (im.Hurt)
            {
                case Sakuga.Hurt.Flinch:
                case Sakuga.Hurt.None:
                    hard *= 0.3f;
                    break;
                case Sakuga.Hurt.Knockdown:
                case Sakuga.Hurt.DriveIn:
                case Sakuga.Hurt.Thrown:
                case Sakuga.Hurt.Launch:
                case Sakuga.Hurt.Pin:
                    hard *= 1.6f;
                    break;
            }
            if (hard >= 0.9f && !Moves.Locked(v))
            {
                Combat.Stagger(v, Mathf.Clamp(0.2f + 0.25f * hard + im.Daze * 0.5f, 0.25f, 1.6f));
            }
            else
            {
                v.startShake(0.2f, 0.1f, true, false);
            }
            if (hard >= 1.4f && Kit.Visible(v.current_position))
            {
                Kit.DustRing(v.current_position, Choreo.PxOf(v), v.current_tile, 8, 30f, 0.45f, Choreo.KOf(v));
            }
            if (im.Owed && by != null && Combat.Living(v))
            {
                MeteorSlam.Doom(by, v, move);
            }
        }

        /// <summary>A white (or coloured) flash over the whole unit, following it.</summary>
        public static void Flash(Actor v, Color c, float life)
        {
            Victim b = Get(v);
            if (b != null)
            {
                b.P.Flash(life, c);
                return;
            }
            ChoreoFx.FlashUnit(v, c, life);
        }

        public static void Tick(float dt)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Victim b = _list[i];
                try
                {
                    b.Tick(dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("choreo victim", e);
                    b.Fail();
                }
                if (b.Done)
                {
                    _list.Remove(b);
                }
            }
        }

        public static void Clear()
        {
            _list.Clear();
        }
    }

    /// <summary>One body the engine moves (see Victims).</summary>
    internal sealed class Victim : IOwned
    {
        private enum Stage
        {
            /// <summary>The reaction is over, but the combo that hurt it goes on: it stands where it is, ours, for the next blow.</summary>
            Idle,
            React,
            Lie,
            Held,
            Fall
        }

        public readonly Actor A;
        public readonly long Id;
        public readonly Puppet P;
        /// <summary>The move holding it (a grab), or null.</summary>
        public ChoreoMove Holder;
        public bool Done;

        private readonly float _k, _px, _pivot;
        private Stage _stage;
        private Impact _im;
        private Actor _by;
        private long _byId;
        private Move _move;
        private float _t, _dur, _stop;
        private Vector2 _g0, _to;
        private float _h0, _rot0, _sx0, _sy0;
        private bool _face0;
        private float _dustT, _ghostT, _flipBase;
        private bool _crashed, _dazed, _skidded;
        private Color _trail = new Color(1f, 1f, 1f, 0.5f);
        /// <summary>Lying down: which way it fell (-1 on its back away from the blow, +1 forward).</summary>
        private float _lieSign = -1f;
        private float _lieT;
        private float _apex, _holdT;

        public Victim(Actor a, Puppet p)
        {
            A = a;
            Id = a.Sid();
            P = p;
            _k = Choreo.KOf(a);
            _px = Choreo.PxOf(a);
            _pivot = p.Pose.PivotUp;
        }

        private bool Alive => !P.Finished && A != null && A.Sid() == Id && A.isAlive();

        /// <summary>Up in the air in one of its reactions (a launch, a juggle's hang, a throw, a fall), in nobody's hands.</summary>
        public bool Aloft => Holder == null && !Done && (_stage == Stage.React || _stage == Stage.Fall) && P.Pose.Height > 0.25f * _k;

        // how much longer light blows have kept a juggle hanging
        private float _hung;

        /// <summary>
        /// A light blow on it while it flies: it flashes and holds the blow a moment, and keeps flying (a
        /// juggle hangs a little longer under every one, up to about half a second more).
        /// </summary>
        public void KeepUp(Impact im)
        {
            P.Flash(0.05f);
            _stop = Mathf.Max(_stop, Mathf.Min(0.04f, Mathf.Max(0f, im.Stop) * 0.5f));
            if (_stage == Stage.React && _im.Hurt == Hurt.Juggle && _hung < 0.5f)
            {
                float add = Mathf.Min(0.08f, 0.5f - _hung);
                _hung += add;
                _dur += add;
            }
        }

        /// <summary>The unit's height in world units.</summary>
        public float HeightW => _pivot / 0.36f * A.actor_scale;

        // -------------------------------------------------------------- start --

        /// <summary>Plays a reaction from the pose it is in now.</summary>
        public void Start(Impact im, Actor by, Move move)
        {
            Holder = null;
            _im = im;
            _by = by;
            _byId = by != null ? by.Sid() : -1;
            _move = move;
            _t = 0f;
            _stop = Mathf.Max(0f, im.Stop);
            Pose p = P.Pose;
            _g0 = p.Ground;
            _h0 = p.Height;
            _rot0 = Mathf.DeltaAngle(0f, p.Rot);
            _sx0 = p.SX;
            _sy0 = p.SY;
            _crashed = false;
            _dazed = false;
            _skidded = false;
            _hung = 0f;
            _dustT = _ghostT = 0f;
            _stage = Stage.React;
            if (move is ChoreoMove cm)
            {
                _trail = Fx.WithA(cm.Hue.Ghost, 0.55f);
            }
            // it faces the one who hit it
            if (Mathf.Abs(im.Dir.x) > 0.08f)
            {
                p.FaceRight = im.Dir.x < 0f;
            }
            _face0 = p.FaceRight;
            P.Pose = p;
            float pw = Mathf.Clamp(im.Power, 0.3f, 2.2f);
            switch (im.Hurt)
            {
                case Hurt.Flinch:
                    _to = Along(0.08f * pw * _k);
                    _dur = 0.22f;
                    break;
                case Hurt.SnapBack:
                    {
                        float d = im.Push > 0f ? im.Push : 0.4f;
                        _to = Along(d);
                        _dur = 0.42f + 0.07f * d;
                        break;
                    }
                case Hurt.Fold:
                    _to = Along(im.Push > 0f ? im.Push : 0.14f * _k);
                    _dur = 0.1f + Mathf.Max(0.12f, im.Hold > 0f ? im.Hold : 0.22f) + 0.26f;
                    break;
                case Hurt.Reel:
                    {
                        float d = im.Push > 0f ? im.Push : 1f;
                        _to = Along(d);
                        _dur = 0.36f + 0.11f * d;
                        break;
                    }
                case Hurt.Spin:
                    {
                        float d = im.Push > 0f ? im.Push : 0.45f;
                        _to = Along(d);
                        _dur = 0.52f;
                        _flipBase = p.FaceRight ? 1f : -1f;
                        break;
                    }
                case Hurt.Knockdown:
                    _to = Along(im.Push > 0f ? im.Push : 0.3f * _k);
                    _dur = 0.3f;
                    _lieSign = -1f;
                    break;
                case Hurt.Launch:
                    {
                        Vector2 want = _g0 + im.Dir * (im.Push > 0f ? im.Push : 1.2f);
                        _to = Landing(want);
                        float d = Vector2.Distance(_g0, _to);
                        _apex = (im.Lift > 0f ? im.Lift : 0.7f + 0.3f * d) * Mathf.Sqrt(_k);
                        _dur = 0.32f + 0.1f * _apex / Mathf.Sqrt(_k) + 0.035f * d;
                        _lieSign = -1f;
                        break;
                    }
                case Hurt.Juggle:
                    _to = Along(im.Push > 0f ? im.Push : 0.25f);
                    _dur = 0.24f + Mathf.Max(0.2f, im.Hold > 0f ? im.Hold : 0.45f);
                    break;
                case Hurt.DriveIn:
                    _to = _g0;
                    _dur = 0.26f;
                    _lieSign = -1f;
                    break;
                case Hurt.Pin:
                    _to = _g0;
                    _dur = Mathf.Max(0.3f, im.Hold > 0f ? im.Hold : 0.6f);
                    _lieSign = -1f;
                    break;
                case Hurt.Thrown:
                    {
                        Vector2 want = im.To.sqrMagnitude > 0.01f ? im.To : _g0 + im.Dir * (im.Push > 0f ? im.Push : 3f);
                        _to = Landing(want);
                        float d = Vector2.Distance(_g0, _to);
                        _dur = 0.32f + 0.055f * d;
                        _lieSign = -1f;
                        break;
                    }
                case Hurt.Crumple:
                    _to = _g0;
                    _dur = 0.5f;
                    _lieSign = 1f;
                    break;
                default:
                    _to = _g0;
                    _dur = 0.2f;
                    break;
            }
            if (Dev.On) Dev.Trace("choreo victim " + A.asset.id + "#" + Id + " " + im.Hurt + " push " + im.Push.ToString("F2") + (im.Owed ? " (owed)" : ""));
        }

        /// <summary>The farthest safe point along the push (a wall or water stops the slide).</summary>
        private Vector2 Along(float d)
        {
            if (d <= 0.001f)
            {
                return _g0;
            }
            Vector2 dir = _im.Dir;
            for (int i = 0; i < 6; i++)
            {
                Vector2 q = Combat.Clamp(_g0 + dir * d);
                if (Combat.ClearPath(_g0, q))
                {
                    return q;
                }
                d *= 0.6f;
                if (d < 0.05f)
                {
                    break;
                }
            }
            return _g0;
        }

        /// <summary>Solid ground near where it should come down (never water, lava, a cliff, the map's edge).</summary>
        private Vector2 Landing(Vector2 want)
        {
            if (Combat.FindLanding(want, 4f, out Vector2 q))
            {
                return q;
            }
            return _g0;
        }

        /// <summary>Held by 'm' (a grab): the holder's script moves it from now on.</summary>
        public void HoldBy(ChoreoMove m)
        {
            Holder = m;
            _stage = Stage.Held;
            m.Own(this);
        }

        /// <summary>The holder's grip, applied this frame (called by the holder).</summary>
        public void Attach(ChoreoMove m, Grip g)
        {
            if (!Alive || Holder != m)
            {
                return;
            }
            Pose p = P.Pose;
            // the grip is in the holder's own frame: it turns with the holder's body (a lean, a flip)
            Vector2 body = m.BodyPos();
            float phi = m.Pose.FaceRight && m.A.asset.can_flip ? -m.Pose.Rot : m.Pose.Rot;
            // the two bodies read as two: the held one sits half its own size further out (upright or
            // upside down: forward; lying across: up)
            float vsz = HeightW / Mathf.Max(0.01f, m.Px);
            float across = Mathf.Abs(Mathf.Cos(g.Rot * Mathf.Deg2Rad));
            float fwd = g.Fwd + vsz * 0.3f * across;
            float up = g.Up + vsz * 0.25f * (1f - across);
            if (vsz > m.Hpx * 1.15f)
            {
                // a body bigger than the holder is carried above its head (else it would hide it); a grip
                // at the ground (a slam: Up below 0) brings it down in front
                float over = Mathf.Clamp01((vsz / m.Hpx - 1.15f) / 0.3f) * Anim.Smooth(-1f, 1.5f, g.Up);
                fwd = Mathf.Lerp(fwd, g.Fwd * 0.3f + 1f, over);
                up = Mathf.Lerp(up, Mathf.Max(up, m.Hpx * 0.5f + vsz * 0.45f * across + vsz * 0.25f * (1f - across)), over);
            }
            Vector2 anchor = body + Choreo.Rotate(new Vector2(m.Fwd * fwd * m.Px, up * m.Px), phi);
            float hc = _pivot * A.actor_scale * g.SY;
            p.Ground = new Vector2(anchor.x, m.Pose.Ground.y - 0.04f);
            p.Height = Mathf.Max(0f, anchor.y - p.Ground.y - hc);
            p.FaceRight = g.Same ? m.Pose.FaceRight : !m.Pose.FaceRight;
            bool flipV = p.FaceRight && A.asset.can_flip;
            // (it struggles in the grip: a kick of the legs, a twist)
            float struggle = 5f * Mathf.Sin(_holdT * 23f) + 2.5f * Mathf.Sin(_holdT * 41f + 1f);
            float vphi = (flipV ? -g.Rot : g.Rot) + phi + struggle;
            p.Rot = flipV ? -vphi : vphi;
            p.SX = g.SX * (1f + 0.03f * Mathf.Sin(_holdT * 31f));
            p.SY = g.SY;
            p.Nudge = Vector2.zero;
            p.Hidden = m.Pose.Hidden;
            p.HideShadow = p.Height > 0.08f || p.Hidden;
            P.Pose = p;
        }

        /// <summary>The holder lets go: it plays 'im' from the pose it is in (a slam's crumple, a toss).</summary>
        public void LetGo(Impact im, Actor by, Move move)
        {
            ChoreoMove h = Holder;
            if (h != null)
            {
                h.Disown(this);
            }
            Holder = null;
            if (!Alive)
            {
                Done = true;
                return;
            }
            if (im.Hurt != Hurt.Thrown && im.Hurt != Hurt.Launch)
            {
                Footing(h);   // (a toss and a launch pick their own landing)
            }
            Start(im, by, move);
        }

        /// <summary>Dropped where it is (the holder ended): on its feet, or it falls if it is in the air.</summary>
        public void Drop(bool inAir)
        {
            ChoreoMove h = Holder;
            Holder = null;
            if (!Alive)
            {
                Done = true;
                return;
            }
            Footing(h);
            Pose p = P.Pose;
            if (p.Height > 0.06f)
            {
                // it falls the rest of the way, tumbling, and lands on its back
                Start(new Impact { Hurt = Hurt.Knockdown, Dir = Vector2.right * (p.FaceRight ? -1f : 1f), Power = 1f, Lift = 0f, Daze = 0.6f }, _by, _move);
                _stage = Stage.Fall;
                return;
            }
            Let(true);
        }

        /// <summary>
        /// A held body is set down where the hands put it, but never in water, lava or a mountain: on the
        /// nearest footing back towards the holder's own feet (which the holder keeps on solid ground).
        /// </summary>
        private void Footing(ChoreoMove holder)
        {
            Pose p = P.Pose;
            if (holder == null || Combat.SafeSpot(p.Ground) || Choreo.Wading(p.Ground))
            {
                return;
            }
            Vector2 home = holder.Pose.Ground;
            for (int i = 1; i <= 4; i++)
            {
                Vector2 q = Vector2.Lerp(p.Ground, home, i * 0.25f);
                if (Combat.SafeSpot(q) || Choreo.Wading(q))
                {
                    p.Ground = q;
                    P.Pose = p;
                    return;
                }
            }
        }

        public void OwnerGone(ChoreoMove m, bool aborted)
        {
            if (Holder == m)
            {
                Drop(aborted);
            }
        }

        /// <summary>Hands it back to the game now.</summary>
        public void Let(bool onGround)
        {
            if (!P.Finished)
            {
                Pose p = P.Pose;
                p.Hidden = false;
                P.Pose = p;
                P.Release(onGround && p.Height <= 0.05f);
            }
            Holder = null;
            Done = true;
        }

        /// <summary>Its tick threw: let it go as it is.</summary>
        public void Fail()
        {
            try
            {
                Let(P.Pose.Height <= 0.02f);
            }
            catch
            {
                Done = true;
            }
        }

        // --------------------------------------------------------------- tick --

        public void Tick(float dt)
        {
            if (Done)
            {
                return;
            }
            if (!Alive)
            {
                // killed (a body in the air falls on its own), picked up by the god hand, recycled
                Done = true;
                if (Holder != null)
                {
                    ChoreoMove h = Holder;
                    Holder = null;
                    h.Disown(this);
                    h.HeldLost(this);
                }
                return;
            }
            if (Holder != null)
            {
                if (Holder.Ended || Holder.Finished)
                {
                    Drop(true);
                }
                _holdT += dt;
                return;   // the holder moves it (Attach)
            }
            bool cine = _move != null && _move.Cinematic;
            float step = cine ? Cinema.Dt : dt;
            if (step <= 0f)
            {
                return;
            }
            if (_stop > 0f)
            {
                _stop -= step;
                Pose q = P.Pose;
                q.Nudge = Choreo.Tremble(_stop);
                P.Pose = q;
                return;
            }
            _t += step;
            switch (_stage)
            {
                case Stage.React:
                case Stage.Fall:
                    React(step);
                    break;
                case Stage.Lie:
                    Lie();
                    break;
                case Stage.Idle:
                    Idle();
                    break;
                default:
                    Let(true);
                    break;
            }
        }

        private void React(float dt)
        {
            Pose p = P.Pose;
            float pw = Mathf.Clamp(_im.Power, 0.3f, 2.2f);
            float t = _t;
            bool vis = Kit.Visible(p.Ground);
            p.Nudge = Vector2.zero;
            p.Hidden = false;
            switch (_im.Hurt)
            {
                case Hurt.Flinch:
                    {
                        float amp = Snap(t, 0.03f, 14f, 24f);
                        float fade = 1f - Mathf.Clamp01(t / 0.05f);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.OutCubic, t / 0.08f));
                        p.Rot = _rot0 * fade + (-11f * pw) * amp;
                        p.SX = 1f - 0.07f * pw * amp;
                        p.SY = 1f + 0.06f * pw * amp;
                        p.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(t / 0.08f));
                        if (t < 0.06f)
                        {
                            p.Nudge = Choreo.Tremble(t);
                        }
                        break;
                    }
                case Hurt.SnapBack:
                    {
                        float ts = 0.18f + 0.06f * Vector2.Distance(_g0, _to);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.OutCubic, t / ts));
                        float peak = -(24f + 10f * pw);
                        float amp = t < 0.1f ? Beat.Apply(Ease.OutExpo, t / 0.035f) : Anim.Spring(t - 0.1f, 7f, 13f);
                        p.Rot = Mathf.Lerp(_rot0, 0f, Mathf.Clamp01(t / 0.035f)) + peak * amp;
                        float sq = t < 0.1f ? 1f : Anim.Spring(t - 0.1f, 9f, 16f);
                        p.SX = 1f - 0.11f * pw * sq;
                        p.SY = 1f + 0.1f * pw * sq;
                        p.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(t / 0.1f)) + 0.05f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.16f));
                        Skid(dt, t < ts * 0.9f, vis);
                        if (t < 0.05f)
                        {
                            p.Nudge = Choreo.Tremble(t);
                        }
                        break;
                    }
                case Hurt.Fold:
                    {
                        float hold = Mathf.Max(0.12f, _im.Hold > 0f ? _im.Hold : 0.22f);
                        float into = Beat.Apply(Ease.OutExpo, t / 0.04f);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.OutCubic, t / 0.09f));
                        float rec = Mathf.Clamp01((t - 0.1f - hold) / 0.26f);
                        float back = Beat.Apply(Ease.OutBack, rec);
                        float fold = 24f + 8f * pw;
                        p.Rot = Mathf.Lerp(Mathf.Lerp(_rot0, fold, into), 0f, back);
                        p.SX = Mathf.Lerp(Mathf.Lerp(_sx0, 1.18f + 0.06f * pw, into), 1f, back);
                        p.SY = Mathf.Lerp(Mathf.Lerp(_sy0, 0.78f - 0.04f * pw, into), 1f, back);
                        p.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(t / 0.08f));
                        if (t < 0.1f + hold && t > 0.02f)
                        {
                            p.Nudge = Choreo.Tremble(t, t < 0.12f ? 0.8f : 0.5f);
                        }
                        if (!_dazed && t > 0.1f && _im.Daze > 0f)
                        {
                            _dazed = true;
                            Combat.Daze(A, _im.Daze + hold);
                        }
                        break;
                    }
                case Hurt.Reel:
                    {
                        float ts = 0.22f + 0.1f * Vector2.Distance(_g0, _to);
                        float u = Beat.Apply(Ease.OutCubic, t / ts);
                        p.Ground = Vector2.Lerp(_g0, _to, u);
                        float lean = -(18f + 8f * pw);
                        float amp = t < ts ? Beat.Apply(Ease.OutExpo, t / 0.04f) : Anim.Spring(t - ts, 8f, 15f);
                        p.Rot = Mathf.Lerp(_rot0, 0f, Mathf.Clamp01(t / 0.04f)) + lean * amp;
                        p.SX = 1f - 0.1f * amp;
                        p.SY = 1f + 0.08f * amp;
                        p.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(t / 0.1f)) + (pw >= 1f ? 0.1f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.16f)) : 0f);
                        Skid(dt, t < ts * 0.92f, vis);
                        if (t < 0.05f)
                        {
                            p.Nudge = Choreo.Tremble(t);
                        }
                        if (vis && _ghostT <= 0f && t < ts * 0.6f && pw >= 1f)
                        {
                            _ghostT = 0.04f;
                            Fx.Afterimage(P, 0.12f, _trail, Fx.WithA(_trail, 0f));
                        }
                        _ghostT -= dt;
                        break;
                    }
                case Hurt.Spin:
                    {
                        float u = Beat.Apply(Ease.OutCubic, t / 0.36f);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.OutCubic, t / 0.3f));
                        float turn = Mathf.Cos(Mathf.PI * 2f * u);
                        p.FaceRight = turn >= 0f ? _face0 : !_face0;
                        p.SX = Mathf.Max(0.2f, Mathf.Abs(turn)) * (1f + 0.06f * (1f - u));
                        float wob = Anim.Spring(Mathf.Max(0f, t - 0.3f), 8f, 16f);
                        p.Rot = Mathf.Lerp(_rot0, 0f, Mathf.Clamp01(t / 0.05f)) + (t < 0.3f ? -14f * pw * (1f - u * 0.5f) : -7f * pw * wob);
                        p.SY = 1f - 0.05f * (1f - u);
                        p.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(t / 0.1f)) + 0.07f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.24f));
                        Skid(dt, t < 0.26f, vis);
                        if (t < 0.04f)
                        {
                            p.Nudge = Choreo.Tremble(t);
                        }
                        if (vis && _ghostT <= 0f && t < 0.3f)
                        {
                            _ghostT = 0.05f;
                            Fx.Afterimage(P, 0.1f, _trail, Fx.WithA(_trail, 0f));
                        }
                        _ghostT -= dt;
                        break;
                    }
                case Hurt.Knockdown:
                    {
                        // feet swept up: it goes over backwards, hits the ground, bounces once, lies
                        float u = Mathf.Clamp01(t / _dur);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.OutQuad, u));
                        float lift = (_im.Lift > 0f ? _im.Lift : 0.22f) * _k;
                        float fall = Beat.Apply(Ease.InQuad, u);
                        // (over backwards; one standing on its head topples on over the same way)
                        float rot = Mathf.Lerp(_rot0, _rot0 > 90f ? 265f : -95f, Beat.Apply(Ease.InOutSine, u));
                        Lying(ref p, rot, Anim.Smooth(0.35f, 1f, u));
                        p.Height = Mathf.Lerp(_h0, 0f, fall) + lift * Mathf.Sin(Mathf.PI * u) * (1f - 0.4f * u);
                        p.SX = 1f;
                        p.SY = 1f + 0.06f * Mathf.Sin(Mathf.PI * u);
                        p.HideShadow = p.Height > 0.08f;
                        if (u >= 1f)
                        {
                            Crash(ref p, vis, false);
                            Lie(ref p, 0.06f);
                        }
                        else if (vis && pw >= 1f)
                        {
                            Ghosts(dt, 0.05f);
                        }
                        break;
                    }
                case Hurt.DriveIn:
                    {
                        // crushed into the ground: flat and sunk for a beat, then it keels over
                        float into = Beat.Apply(Ease.OutExpo, t / 0.03f);
                        p.Ground = _g0;
                        p.Height = Mathf.Lerp(_h0, 0f, into);
                        p.SX = Mathf.Lerp(_sx0, 1.34f, into);
                        p.SY = Mathf.Lerp(_sy0, 0.6f, into);
                        // (driven in head first it stays upside down, crushed, before it keels over)
                        bool inverted = Mathf.Abs(_rot0) > 100f;
                        p.Rot = inverted ? _rot0 : Mathf.Lerp(_rot0, 0f, into);
                        p.Nudge = new Vector2(Choreo.Tremble(t, 0.6f).x, (inverted ? -2f : -1.5f) * into);
                        if (!_crashed)
                        {
                            Crash(ref p, vis, true);
                        }
                        if (t >= _dur)
                        {
                            _im.Hurt = Hurt.Knockdown;
                            _g0 = p.Ground;
                            _to = _g0;
                            _h0 = 0f;
                            _rot0 = Mathf.Abs(_rot0) > 100f ? 180f : 0f;
                            _sx0 = p.SX;
                            _sy0 = p.SY;
                            _im.Lift = 0.05f;
                            _dur = 0.22f;
                            _t = 0f;
                            _crashed = true;
                        }
                        break;
                    }
                case Hurt.Pin:
                    {
                        float into = Beat.Apply(Ease.OutCubic, t / 0.08f);
                        Lying(ref p, Mathf.Lerp(_rot0, -90f, into), into);
                        p.Ground = _g0;
                        p.Height = Mathf.Lerp(_h0, 0f, into);
                        p.SX = Mathf.Lerp(_sx0, 1.08f, into);
                        p.SY = Mathf.Lerp(_sy0, 0.9f, into);
                        p.Nudge += Choreo.Tremble(t, 0.4f);
                        if (!_dazed)
                        {
                            _dazed = true;
                            Choreo.Daze(A, _dur + Mathf.Max(0.4f, _im.Daze));
                        }
                        if (t >= _dur)
                        {
                            Lie(ref p, 0f);
                        }
                        break;
                    }
                case Hurt.Launch:
                    {
                        // the head snaps back on the blow, then up it goes, over in a backflip, and down
                        float u = Mathf.Clamp01(t / _dur);
                        p.Ground = Vector2.Lerp(_g0, _to, Anim.Smooth(0.04f, 1f, u));
                        p.Height = Mathf.Lerp(_h0, 0f, u) + _apex * Lob(u);
                        float snap = Beat.Apply(Ease.OutExpo, t / 0.04f);
                        float flip = Beat.Apply(Ease.InOutSine, Anim.Smooth(0.08f, 0.96f, u));
                        float spin = float.IsNaN(_im.Spin) ? -360f : _im.Spin;
                        float rot = Mathf.Lerp(_rot0, -28f, snap) + (spin + 28f) * flip;
                        p.Rot = rot;
                        float fast = Mathf.Clamp01(Mathf.Abs(LobSlope(u)));
                        p.SX = 1f - 0.12f * fast;
                        p.SY = 1f + 0.22f * fast;
                        if (t < 0.05f)
                        {
                            p.Nudge = Choreo.Tremble(t);
                        }
                        p.HideShadow = p.Height > 0.08f;
                        Ghosts(dt, 0.03f);
                        if (u >= 1f)
                        {
                            p.Height = 0f;
                            Crash(ref p, vis, false);
                            _g0 = p.Ground;
                            _h0 = 0f;
                            _t = 0f;
                            if (_im.Power >= 1.3f || _im.Daze >= 0.6f)
                            {
                                // a heavy blow: it comes down on its back
                                _im.Hurt = Hurt.Knockdown;
                                _to = Along(0.2f * _k);
                                _rot0 = Mathf.DeltaAngle(0f, rot);
                                _im.Lift = 0.12f;
                                _dur = 0.2f;
                            }
                            else
                            {
                                // it lands on its feet, buckling, and staggers a step
                                _im.Hurt = Hurt.Fold;
                                _im.Hold = 0.12f;
                                _im.Push = 0.12f * _k;
                                _to = Along(_im.Push);
                                _rot0 = Mathf.DeltaAngle(0f, rot);
                                _sx0 = 1.2f;
                                _sy0 = 0.78f;
                                _dur = 0.1f + 0.12f + 0.26f;
                            }
                        }
                        break;
                    }
                case Hurt.Juggle:
                    {
                        // popped up, hangs (drifting, turning slowly), then drops onto its back
                        float rise = 0.22f;
                        float top = (_im.Lift > 0f ? _im.Lift : 1.4f) * _k;
                        if (t < rise)
                        {
                            float u = Beat.Apply(Ease.OutCubic, t / rise);
                            p.Height = Mathf.Lerp(_h0, top, u);
                            p.Ground = Vector2.Lerp(_g0, _to, u * 0.7f);
                            p.Rot = Mathf.Lerp(_rot0, -60f, u);
                            p.SX = 0.88f;
                            p.SY = 1.14f;
                            Ghosts(dt, 0.04f);
                        }
                        else
                        {
                            float u = Mathf.Clamp01((t - rise) / Mathf.Max(0.05f, _dur - rise));
                            p.Height = top * (1f - 0.18f * u * u);
                            p.Ground = Vector2.Lerp(_g0, _to, 0.7f + 0.3f * u);
                            p.Rot = -60f - 70f * u;
                            p.SX = p.SY = 1f;
                            if (u >= 1f)
                            {
                                // the hang is over: it drops
                                Start(new Impact { Hurt = Hurt.Knockdown, Dir = _im.Dir, Power = pw, Lift = 0f, Daze = Mathf.Max(0.4f, _im.Daze), Owed = _im.Owed }, _by, _move);
                                _stage = Stage.Fall;
                                return;
                            }
                        }
                        p.HideShadow = true;
                        break;
                    }
                case Hurt.Thrown:
                    {
                        float u = Mathf.Clamp01(t / _dur);
                        p.Ground = Vector2.Lerp(_g0, _to, Beat.Apply(Ease.InOutSine, u));
                        float apex = (_im.Lift > 0f ? _im.Lift : 0.8f + 0.18f * Vector2.Distance(_g0, _to)) * Mathf.Sqrt(_k);
                        p.Height = Mathf.Lerp(_h0, 0f, u) + apex * 4f * u * (1f - u);
                        float spin = float.IsNaN(_im.Spin) ? -540f : _im.Spin;
                        float rot = _rot0 + spin * Beat.Apply(Ease.OutQuad, u);
                        p.Rot = rot;
                        float fast = Mathf.Abs(1f - 2f * u);
                        p.SX = 1f - 0.1f * fast;
                        p.SY = 1f + 0.16f * fast;
                        p.HideShadow = true;
                        Ghosts(dt, 0.035f);
                        if (u >= 1f)
                        {
                            Crash(ref p, vis, true);
                            // it comes to rest on its back
                            _im.Hurt = Hurt.Knockdown;
                            _g0 = p.Ground;
                            _to = Along(0.25f * _k);
                            _h0 = 0f;
                            _rot0 = -90f + Mathf.DeltaAngle(-90f, rot) * 0.15f;
                            _im.Lift = 0.32f;
                            _dur = 0.24f;
                            _t = 0f;
                        }
                        break;
                    }
                case Hurt.Crumple:
                    {
                        float u = Mathf.Clamp01(t / _dur);
                        float knees = Beat.Apply(Ease.OutCubic, Mathf.Clamp01(t / 0.16f));
                        float over = Beat.Apply(Ease.InQuad, Anim.Smooth(0.3f, 1f, u));
                        p.Ground = _g0;
                        p.Height = Mathf.Lerp(_h0, 0f, knees);
                        p.SX = Mathf.Lerp(_sx0, 1.1f, knees);
                        p.SY = Mathf.Lerp(_sy0, 0.82f, knees);
                        Lying(ref p, Mathf.Lerp(Mathf.Lerp(_rot0, 18f, knees), 92f, over), over);
                        if (u >= 1f)
                        {
                            Crash(ref p, vis, false);
                            Lie(ref p, 0f);
                        }
                        break;
                    }
                default:
                    if (t >= _dur)
                    {
                        Settle();
                        return;
                    }
                    break;
            }
            P.Pose = p;
            Collide(p, dt, vis);
            // (Stage.Fall plays a Knockdown to the end; the reactions that end themselves are left out)
            if (_stage == Stage.React && _t >= _dur && _im.Hurt != Hurt.Knockdown && _im.Hurt != Hurt.Pin && _im.Hurt != Hurt.Crumple
                && _im.Hurt != Hurt.Juggle && _im.Hurt != Hurt.DriveIn && _im.Hurt != Hurt.Thrown && _im.Hurt != Hurt.Launch)
            {
                Settle();
            }
        }

        // ----------------------------------------------------------- collisions --

        private Vector2 _lastG;
        private bool _lastSet;
        private int _bowled;
        private readonly List<long> _hitIds = new List<long>(4);
        private static readonly List<Actor> _bowl = new List<Actor>();

        /// <summary>
        /// A body flung fast and low ploughs into the foes in its way: each it meets is knocked down
        /// along its line (or rocked, if too heavy to move), takes a share of the blow, and the crash
        /// shows (an impact burst at the bodies' meeting, dust off the ground). At most four a flight.
        /// </summary>
        private void Collide(Pose p, float dt, bool vis)
        {
            Vector2 g = p.Ground;
            if (!_lastSet || dt <= 0f)
            {
                _lastG = g;
                _lastSet = true;
                return;
            }
            Vector2 d = g - _lastG;
            _lastG = g;
            Hurt h = _im.Hurt;
            if (_bowled >= 4 || !(h == Hurt.Reel || h == Hurt.Thrown || h == Hurt.Launch || h == Hurt.Spin || h == Hurt.Knockdown || h == Hurt.SnapBack))
            {
                return;
            }
            float speed = d.magnitude / dt;
            if (speed < 2.2f * _k || p.Height > 1.3f * _k)
            {
                return;
            }
            Actor by = _by != null && _by.Sid() == _byId && _by.isAlive() ? _by : null;
            if (by == null)
            {
                return;
            }
            _bowl.Clear();
            Choreo.Foes(by, g, 0.55f * _k, _bowl);
            Vector2 dir = d / Mathf.Max(1e-4f, d.magnitude);
            for (int i = 0; i < _bowl.Count && _bowled < 4; i++)
            {
                Actor u = _bowl[i];
                if (u == null || u == A || !u.isAlive() || _hitIds.Contains(u.Sid()))
                {
                    continue;
                }
                Victim ub = Victims.Get(u);
                if (ub != null && (ub.Holder != null || ub.Aloft))
                {
                    continue;
                }
                _hitIds.Add(u.Sid());
                _bowled++;
                float pw = Mathf.Clamp(_im.Power, 0.4f, 2.2f) * Mathf.Clamp(speed / (5f * _k), 0.5f, 1.6f);
                Combat.Hit(by, u, 0.18f * pw, false, true, 0f, _move);
                Impact im = Impact.Of(pw >= 0.9f ? Sakuga.Hurt.Knockdown : Sakuga.Hurt.Reel, dir + Random.insideUnitCircle * 0.35f, Mathf.Clamp(0.5f + 0.35f * pw, 0.5f, 1.5f) * _k, pw);
                im.Daze = 0.3f;
                Victims.Hurt(u, by, _move, im);
                if (vis)
                {
                    Vector2 at = Vector2.Lerp(Choreo.BodyAt(u, 0.45f), Choreo.BodyAt(A, 0.5f), 0.5f);
                    Hue hue = Hue.For(by, _move != null && _move.Tech != null ? _move.Tech.School : School.None, _move != null ? _move.Level : 1);
                    ChoreoFx.Impact(at, dir, _px, _k, Mathf.Clamp(0.55f * pw, 0.4f, 1.2f), hue);
                    Kit.DustRing(u.current_position, _px, u.current_tile, 5, 24f, 0.4f, Mathf.Sqrt(_k));
                    Choreo.Sound("event:/SFX/HIT/HitGeneric", u.current_tile, 0.04f);
                }
            }
        }

        /// <summary>Height over a lob (0..1): fast up, the top at 42%, falling faster and faster.</summary>
        private static float Lob(float k)
        {
            const float top = 0.42f;
            if (k < top)
            {
                return Beat.Apply(Ease.OutQuad, k / top);
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

        /// <summary>The impact pose comes at once and springs back: 0 at t=0 to 1 within 'rise', then a damped spring.</summary>
        private static float Snap(float t, float rise, float damping, float freq)
        {
            return t < rise ? Beat.Apply(Ease.OutExpo, t / rise) : Anim.Spring(t - rise, damping, freq);
        }

        /// <summary>
        /// Turns the pose to 'rot' (its own frame) and, by 'w' (0..1), from turning about its middle to
        /// turning about its feet, the way the game draws a body knocked flat (no pop when it is let go).
        /// </summary>
        private void Lying(ref Pose p, float rot, float w)
        {
            p.Rot = rot;
            bool canFlip = A.asset.can_flip;
            float phi = (p.FaceRight && canFlip ? -rot : rot) * Mathf.Deg2Rad;
            Vector2 feet = new Vector2(-Mathf.Sin(phi), Mathf.Cos(phi) - 1f) * _pivot;
            p.Nudge = feet * Mathf.Clamp01(w);
            _lieSign = rot < 0f ? -1f : 1f;
        }

        /// <summary>Dust, a puff, a crater: the body meets the ground.</summary>
        private void Crash(ref Pose p, bool vis, bool hard)
        {
            if (_crashed)
            {
                return;
            }
            _crashed = true;
            WorldTile tile = Combat.TileAt(p.Ground);
            Choreo.Sound(hard ? "event:/SFX/DESTRUCTION/DropSimpleImpact" : "event:/SFX/HIT/HitGeneric", tile);
            if (vis)
            {
                Fx.LandingPuff(p.Ground, _px, tile);
                Kit.DustRing(p.Ground, _px, tile, hard ? 10 : 6, hard ? 40f : 26f, 0.45f, _k);
                if (hard)
                {
                    Kit.Debris(p.Ground, _px, tile, 6, _k);
                }
            }
            Actor by = _by != null && _by.Sid() == _byId ? _by : null;
            ChoreoMove cm = _move as ChoreoMove;
            if (hard && _im.Crater > 0f && by != null)
            {
                Scale s = cm != null ? cm.Scale : Scale.Heavy;
                if (vis)
                {
                    Kit.Crater(p.Ground, _px * _k, tile, Ruin.Force(Tiers.Of(by), s) >= 3, 6f, _im.Crater < 1.2f, cm != null ? cm.Hue.Edge : (Color?)null);
                }
                Ruin.Blast(by, p.Ground, _im.Crater, s, by.isAlive() ? by.current_position : (Vector2?)null, !_im.NoFire);
                Ruin.Quake(by, p.Ground, s, 1f, _k);
            }
            if (hard && _im.AreaR > 0f && by != null && by.isAlive())
            {
                List<Actor> near = _near;
                Choreo.Foes(by, p.Ground, _im.AreaR, near);
                for (int i = 0; i < near.Count; i++)
                {
                    Actor u = near[i];
                    if (u == A || !Choreo.FairBlow(by, u, _move, out float cmul, out bool full, true))
                    {
                        continue;
                    }
                    if (Combat.Reach(_move, u, true) != Contact.Hit)
                    {
                        continue;
                    }
                    Combat.Hit(by, u, Mathf.Max(0.2f, _im.AreaMult) * cmul, true, true, 0f, _move);
                    if (Combat.Living(u))
                    {
                        // (one above the class limits only rocks)
                        Vector2 away = u.current_position - p.Ground;
                        Victims.Hurt(u, by, _move, Impact.Of(full ? Hurt.Knockdown : Hurt.Stagger, away, 0.5f + 0.4f * _im.Power, 1f));
                    }
                }
                if (vis)
                {
                    Color c = cm != null ? cm.Hue.Edge : Color.white;
                    Fx.Ring(p.Ground, _px, 0.3f, 3f, _im.AreaR / _px * 0.8f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
                }
            }
            if (_im.Owed)
            {
                OwedKill();
            }
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>It comes to rest lying down: dazed (the game keeps it down), then let go.</summary>
        private void Lie(ref Pose p, float bounce)
        {
            _stage = Stage.Lie;
            _lieT = 0f;
            _t = 0f;
            P.Pose = p;
            if (!_dazed && Combat.Living(A))
            {
                _dazed = true;
                Choreo.Daze(A, Mathf.Max(0.5f, _im.Daze > 0f ? _im.Daze : 0.9f));
            }
            _bounce = bounce * _k;
        }

        private float _bounce;

        private void Lie()
        {
            Pose p = P.Pose;
            _lieT += Clock.Dt;
            float b = _bounce > 0f ? _bounce * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_t / 0.14f)) : 0f;
            p.Height = Mathf.Max(0f, b);
            float amp = Anim.Spring(_t, 12f, 22f);
            p.SX = 1f + 0.12f * amp;
            p.SY = 1f - 0.1f * amp;
            p.HideShadow = false;
            P.Pose = p;
            // once the game counts it as out cold it keeps it down itself: let go then (or after a while)
            if (_t > 0.16f && (A.is_unconscious || _t > 0.5f) || !Combat.Living(A))
            {
                if (_im.Owed)
                {
                    OwedKill();
                }
                float z = Mathf.DeltaAngle(0f, p.Rot);
                try
                {
                    A._rotation_direction = z < 0f ? RotationDirection.Left : RotationDirection.Right;
                }
                catch
                {
                    // an odd unit: the game picks its side
                }
                p.SX = p.SY = 1f;
                P.Pose = p;
                Let(true);
            }
        }

        /// <summary>
        /// The reaction is over: upright. While the move that hurt it is still on it (the combo goes on)
        /// it stays in hand a moment for the next blow; else it is let go (dazed if the blow said so).
        /// </summary>
        private void Settle()
        {
            Pose p = P.Pose;
            p.Rot = 0f;
            p.SX = p.SY = 1f;
            p.Height = 0f;
            p.Nudge = Vector2.zero;
            p.HideShadow = false;
            P.Pose = p;
            if (_im.Owed)
            {
                OwedKill();
            }
            if (ComboGoesOn() && Combat.Living(A))
            {
                _stage = Stage.Idle;
                _t = 0f;
                return;
            }
            Release2();
        }

        /// <summary>The move that hurt it is still playing, on it.</summary>
        private bool ComboGoesOn()
        {
            return _move is ChoreoMove cm && !cm.Ended && !cm.Finished && !cm.Broken && cm.V == A && !cm.Lost
                && !(cm.Current != null && cm.Current.Recovery);
        }

        private void Idle()
        {
            if (!ComboGoesOn() || _t > 0.8f || !Combat.Living(A))
            {
                Release2();
            }
        }

        private void Release2()
        {
            Let(true);
            if (!_dazed && _im.Daze > 0f && Combat.Living(A))
            {
                Combat.Daze(A, _im.Daze);
            }
        }

        /// <summary>The kill the blow owed: enough to end it through any armour, credited to the blow.</summary>
        private void OwedKill()
        {
            _im.Owed = false;
            // (never on one that is no longer an enemy: its city taken, a peace)
            if (!Combat.Living(A) || _by == null || _by.Sid() != _byId || !Choreo.Hostile(_by, A))
            {
                return;
            }
            Combat.Hit(_by, A, 0f, true, false, 99999f, _move);
            if (Combat.Living(A) && Duels.TechShare(_by, A) >= 1f)
            {
                A.getHit(A.getHealth() * 10f + 100f, false, AttackType.Weapon, _by, false, false, false);
            }
        }

        // ---------------------------------------------------------------- fx --

        private void Skid(float dt, bool sliding, bool vis)
        {
            if (!vis || !sliding)
            {
                return;
            }
            _dustT -= dt;
            if (_dustT > 0f)
            {
                return;
            }
            _dustT = 0.035f;
            Pose p = P.Pose;
            ChoreoFx.HeelDust(p.Ground, -_im.Dir, _px, Combat.TileAt(p.Ground), _k);
            if (!_skidded && Vector2.Distance(_g0, _to) > 0.35f)
            {
                _skidded = true;
                ChoreoFx.Skid(_g0, _to, _px, Combat.TileAt(_g0));
            }
        }

        private void Ghosts(float dt, float every)
        {
            _ghostT -= dt;
            if (_ghostT > 0f || !Kit.Visible(P.Pose.Ground))
            {
                return;
            }
            _ghostT = every;
            Fx.Afterimage(P, 0.12f, _trail, Fx.WithA(_trail, 0f));
        }
    }
}
