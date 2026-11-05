using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>What a shot looks like.</summary>
    internal enum ShotKind
    {
        /// <summary>A finger-flick of ki: a bright bead and a streak.</summary>
        Bullet,
        /// <summary>A ball of ki, sparkling.</summary>
        Orb,
        /// <summary>A spinning disc (it cuts).</summary>
        Disc,
        /// <summary>A rock torn from the ground (thrown in an arc; it craters where it lands).</summary>
        Boulder,
        /// <summary>A thin crescent of wind.</summary>
        WindBlade,
        /// <summary>A crescent of light from a blade.</summary>
        SwordWave,
        /// <summary>A harpoon on a chain from the hand (it can drag what it hits back).</summary>
        Harpoon,
        /// <summary>A sword of light.</summary>
        LightSword,
        /// <summary>A long spear of light.</summary>
        Spear,
        /// <summary>A small stone (a sling).</summary>
        Stone,
        /// <summary>A fist of pressure punched out across the air (rings peel off it as it flies).</summary>
        Fist,
        /// <summary>A hunter's arrow or dart: a thin shaft, fast and straight.</summary>
        Arrow,
        /// <summary>A hunter's javelin: a wooden shaft and a steel point (no glow: a thrown weapon, not ki).</summary>
        Javelin
    }

    /// <summary>
    /// How a shot meets a body, so the picture always follows from the thing thrown and the thing hit:
    /// a boulder carries a light body off with it until they crash together and it smashes, but breaks
    /// apart on something too heavy to move; a spear runs a body through and pins it where it lands; a
    /// ball of ki goes off like a shell, throwing the bodies round it; a blade's wave cuts.
    /// </summary>
    internal enum ShotImpact
    {
        /// <summary>By its kind and its target (see ShotImpact).</summary>
        Auto,
        /// <summary>It bursts on the body (its reaction, a flash): the default for the light ones.</summary>
        Burst,
        /// <summary>It breaks apart on the body (fragments, dust; the body barely moves).</summary>
        Shatter,
        /// <summary>The body rides it until they come down together; then it smashes (a crater, fragments).</summary>
        Carry,
        /// <summary>It runs the body through and nails it to the ground where it lands (it stays a moment).</summary>
        Pin,
        /// <summary>It goes off on the body: a blast that throws the bodies round it too.</summary>
        Explode
    }

    /// <summary>How a shot flies.</summary>
    internal enum Flight
    {
        Straight,
        /// <summary>An arc through the air to the target's spot (it lands there).</summary>
        Lob,
        /// <summary>Curving after its target.</summary>
        Homing,
        /// <summary>Out to its range and back to the thrower's hand.</summary>
        Boomerang,
        /// <summary>Weaving side to side as it goes.</summary>
        Wave
    }

    /// <summary>What a shot is (ChoreoMove.Shoot / Shots.Fire).</summary>
    internal sealed class ShotSpec
    {
        public ShotKind Kind = ShotKind.Orb;
        public Flight Flight = Flight.Straight;
        /// <summary>Tiles a second.</summary>
        public float Speed = 18f;
        /// <summary>Tiles it flies at most (then fizzles; a boomerang turns back).</summary>
        public float Range = 12f;
        /// <summary>Its size (1 as designed; scales the sprite and its hit radius).</summary>
        public float Size = 1f;
        public float Mult = 1f;
        // Opt-in falloff for a piercing projectile. Existing shots retain full damage.
        public float PierceDecay = 1f, PierceFloor = 1f;
        public bool LunarCrescent;
        public Hurt Hurt = Hurt.Reel;
        public float Push = 0.8f;
        /// <summary>How many enemies it passes through before it is spent (0: the first stops it).</summary>
        public int Pierce;
        /// <summary>Homing: how fast it turns (radians a second).</summary>
        public float Turn = 6f;
        /// <summary>Lob: the arc's height (tiles).</summary>
        public float Apex = 2f;
        /// <summary>Where it bursts the ground breaks (base radius, tiles; 0: no crater) at the move's Scale.</summary>
        public float Crater;
        /// <summary>Its crater may set the grass alight (at a Master's force and up); false: it only breaks the ground.</summary>
        public bool Fire = true;
        /// <summary>Its burst hurts enemies within this many tiles too (x AreaMult).</summary>
        public float Area, AreaMult = 0.5f;
        public bool Deflectable = true, Reflectable = true, Cuttable = true;
        public Element Element = Element.School;
        /// <summary>What it does to a body it hits (Auto: by what it is and what it hits).</summary>
        public ShotImpact Impact = ShotImpact.Auto;
        /// <summary>Seconds after it is loosed before it flies (a volley's later shots).</summary>
        public float Delay;
    }

    /// <summary>
    /// One of the mod's own projectiles in flight: it flies its course (straight, an arc, homing,
    /// a boomerang, weaving), hits the first enemy in its path (or several: Pierce), which may answer
    /// it once (Combat.Reach: an evade lets it through, a parry stops it), deals its damage and its
    /// reaction, and bursts (a heavy one breaks the ground). Only enemies are hurt. Reactive techniques
    /// can turn it: Reflect (back at its thrower, theirs now), Deflect (away, harmless), Cut (split in
    /// two, spent); during its Combat.Reach it is Shots.Reaching, and Shots.Interceptors are asked
    /// before it touches anyone.
    /// </summary>
    internal sealed class Shot
    {
        public Actor A;
        public long AId;
        public Move By;
        public ShotSpec Spec;
        public Hue Hue;
        /// <summary>Where it is over the ground (Pos), how high (Z), which way it flies over the ground (Dir).</summary>
        public Vector2 Pos, Dir, Start;
        public float Z, VZ, Age, Traveled, Px, K;
        private float _z0, _zGoal, _dist0;
        public Actor Target;
        public long TId;
        public Vector2 Goal;
        /// <summary>It can still hurt (false once deflected: it flies off harmlessly).</summary>
        public bool Hostile = true;
        /// <summary>Spent (burst, cut, fizzled at its range, landed, caught): it is gone.</summary>
        public bool Done;
        public bool Returning;
        public int Hits;

        private readonly HashSet<long> _hit = new HashSet<long>();
        private float _scanT, _trailT, _wave;
        private Particle _head, _glow, _shadow;
        private Particle[] _chain;
        private float _lobDur, _lobT;
        private Vector2 _lobFrom;

        public Shot(Actor a, Move by, Vector2 from, Vector2 dir, Actor target, Vector2 goal, ShotSpec spec, Hue hue)
        {
            A = a;
            AId = a.Sid();
            By = by;
            Spec = spec;
            Hue = hue;
            // 'from' is where it is drawn (the hand): its ground point and its height above it
            _z0 = Mathf.Max(0f, from.y - Choreo.Feet(a).y);
            Pos = Start = from - new Vector2(0f, _z0);
            Z = _z0;
            Target = target;
            TId = target != null ? target.Sid() : -1;
            if (target != null)
            {
                Vector2 g = target.current_position - Pos;
                dir = g.sqrMagnitude > 1e-4f ? g : dir;
                _zGoal = Mathf.Max(0f, target.position_height) + Choreo.SpritePx(target) * Choreo.PxOf(target) * 0.45f;
                _dist0 = Mathf.Max(0.3f, g.magnitude);
            }
            else
            {
                _zGoal = _z0;
                _dist0 = 1f;
            }
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            Goal = goal;
            Px = Choreo.PxOf(a);
            K = Choreo.KOf(a);
            Age = -Mathf.Max(0f, spec.Delay);
            if (spec.Flight == Flight.Lob)
            {
                _lobFrom = Pos;
                float d = Vector2.Distance(Pos, goal);
                _lobDur = Mathf.Max(0.25f, d / Mathf.Max(2f, spec.Speed));
            }
        }

        private bool OwnerOk => A != null && A.Sid() == AId && A.isAlive();

        /// <summary>Its hit radius in tiles.</summary>
        public float Radius => (Spec.LunarCrescent ? 2.2f : 0.3f + 0.12f * Spec.Size) * Mathf.Lerp(1f, K, 0.5f);

        // ------------------------------------------------------------ answers --

        /// <summary>Sent back at its thrower by 'by' (it is theirs now, a little faster).</summary>
        public void Reflect(Actor by, float boost = 1.2f)
        {
            if (by == null || !Spec.Reflectable)
            {
                Deflect(Random.insideUnitCircle);
                return;
            }
            // (back at its thrower: the same unit still, never a new one its pooled actor became since)
            Actor was = A != null && A.Sid() == AId ? A : null;
            A = by;
            AId = by.Sid();
            By = null;
            Hue = Hue.For(by, School.None, 2, Spec.Element == Element.School ? Element.Ki : Spec.Element);
            Target = was;
            TId = was != null ? was.Sid() : -1;
            Dir = was != null && (was.current_position - Pos).sqrMagnitude > 1e-4f ? (was.current_position - Pos).normalized : -Dir;
            _z0 = Z;
            _zGoal = was != null ? Choreo.SpritePx(was) * Choreo.PxOf(was) * 0.45f : Z;
            _dist0 = was != null ? Mathf.Max(0.3f, Vector2.Distance(was.current_position, Pos)) : 1f;
            Hostile = true;
            Returning = false;
            Traveled = 0f;
            Start = Pos;
            _hit.Clear();
            Spec = Clone(Spec);   // (never the thrower's own spec: it may cast it again)
            if (Spec.Flight == Flight.Lob || Spec.Flight == Flight.Boomerang)
            {
                Spec.Flight = Flight.Homing;
            }
            Spec.Speed *= boost;
            Spec.Mult *= boost;
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(Pos));
            Vector2 at = Pos + new Vector2(0f, Z);
            if (Kit.Visible(at))
            {
                Kit.Flash(at, Px, 4, 0.07f, Color.white);
                Fx.Ring(at, Px, 0.18f, 2f, 10f, 1, 1, FxLayer.Top, 5, 1f, 2.4f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        /// <summary>Knocked away along 'dir': it flies off harmlessly and fades.</summary>
        public void Deflect(Vector2 dir)
        {
            if (!Spec.Deflectable)
            {
                return;
            }
            Hostile = false;
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : -Dir;
            Spec = Clone(Spec);
            Spec.Flight = Flight.Straight;
            Spec.Range = Traveled + 3f;
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(Pos));
            Vector2 at = Pos + new Vector2(0f, Z);
            if (Kit.Visible(at))
            {
                Kit.Sparks(at, Px, 5, 40f, 90f, Hue.Edge);
            }
        }

        /// <summary>Cut in two: the halves fly apart and fade; it is spent.</summary>
        public void Cut()
        {
            if (!Spec.Cuttable)
            {
                Deflect(-Dir);
                return;
            }
            Done = true;
            Vector2 at = Pos + new Vector2(0f, Z);
            if (Kit.Visible(at))
            {
                Vector2 side = Choreo.Perp(Dir);
                for (int s = -1; s <= 1; s += 2)
                {
                    Particle p = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(2f * Spec.Size)), 1f, 0.3f), at, Px, 0.25f, FxLayer.Top, 12);
                    p?.Moving((Dir * 0.5f + side * s) * 30f * Px, 0f, 3f).Sized(1f, 0.3f).Color(Hue.Core, Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                }
                Kit.Line(at - side * 4f * Px, at + side * 4f * Px, Px, 0.08f, 1, FxLayer.Top, 14, Color.white, Fx.WithA(Color.white, 0f));
            }
            Kill();
        }

        private static ShotSpec Clone(ShotSpec s)
        {
            return (ShotSpec)s.MemberwiseCloneShot();
        }

        // --------------------------------------------------------------- tick --

        public bool Tick(float dt)
        {
            if (Done)
            {
                Kill();
                return false;
            }
            if (dt <= 0f)
            {
                return true;
            }
            Age += dt;
            if (Age < 0f)
            {
                return true;   // a volley's later shot waits its turn
            }
            Vector2 last = Pos;
            if (_ride != null || _stuckT > 0f)
            {
                if (!RideTick(dt))
                {
                    Done = true;
                    Kill();
                    return false;
                }
                Render(last, dt);
                return true;
            }
            if (!Fly(dt))
            {
                Done = true;
                Kill();
                return false;
            }
            Traveled += Vector2.Distance(last, Pos);
            if (Spec.Flight != Flight.Lob)
            {
                // it flies from the hand's height to the middle of its target's body
                Z = Mathf.Lerp(_z0, _zGoal, Mathf.Clamp01(Traveled / _dist0));
            }
            if (Hostile && OwnerOk)
            {
                _scanT -= dt;
                if (_scanT <= 0f || Spec.Speed > 25f)
                {
                    _scanT = 0.04f;
                    if (Collide())
                    {
                        Done = true;
                        Kill();
                        return false;
                    }
                }
            }
            Render(last, dt);
            if (Traveled >= Spec.Range && Spec.Flight != Flight.Lob)
            {
                if (Spec.Flight == Flight.Boomerang && !Returning)
                {
                    Returning = true;
                    _hit.Clear();
                }
                else if (Spec.Flight != Flight.Boomerang)
                {
                    Fizzle();
                    Done = true;
                    Kill();
                    return false;
                }
            }
            if (Age > 8f)
            {
                Done = true;
                Kill();
                return false;
            }
            return true;
        }

        /// <summary>Moves it one frame along its course. False when its course ends (a lob landed, a boomerang caught).</summary>
        private bool Fly(float dt)
        {
            float speed = Spec.Speed * Mathf.Lerp(1f, K, 0.4f);
            switch (Spec.Flight)
            {
                case Flight.Lob:
                    {
                        _lobT += dt;
                        float k = Mathf.Clamp01(_lobT / _lobDur);
                        Vector2 g = Vector2.Lerp(_lobFrom, Goal, k);
                        Dir = (g - Pos).sqrMagnitude > 1e-6f ? (g - Pos).normalized : Dir;
                        Pos = g;
                        Z = Spec.Apex * 4f * k * (1f - k) + Mathf.Lerp(_z0, 0f, k);
                        if (k >= 1f)
                        {
                            Z = 0f;
                            Burst(null, true);
                            return false;
                        }
                        return true;
                    }
                case Flight.Homing:
                    {
                        if (TargetOk())
                        {
                            Vector2 want = Target.current_position - Pos;
                            if (want.sqrMagnitude > 1e-4f)
                            {
                                float ang = Vector2.SignedAngle(Dir, want) * Mathf.Deg2Rad;
                                float turn = Spec.Turn * dt;
                                Dir = Choreo.Rotate(Dir, Mathf.Clamp(ang, -turn, turn) * Mathf.Rad2Deg).normalized;
                            }
                        }
                        Pos += Dir * speed * dt;
                        return true;
                    }
                case Flight.Boomerang:
                    {
                        if (Returning)
                        {
                            if (!OwnerOk)
                            {
                                return false;
                            }
                            Vector2 hand = A.current_position;
                            Vector2 want = hand - Pos;
                            if (want.magnitude < speed * dt + 0.3f)
                            {
                                return false;   // caught
                            }
                            Dir = Vector2.Lerp(Dir, want.normalized, Mathf.Clamp01(dt * 6f)).normalized;
                        }
                        else
                        {
                            Dir = Choreo.Rotate(Dir, 40f * dt);   // a slight curve outwards
                        }
                        Pos += Dir * speed * dt;
                        return true;
                    }
                case Flight.Wave:
                    {
                        _wave += dt;
                        Vector2 side = Choreo.Perp(Dir);
                        float off = Mathf.Cos(_wave * 9f) * 9f * Px * dt * 9f;
                        Pos += Dir * speed * dt + side * off;
                        return true;
                    }
                default:
                    Pos += Dir * speed * dt;
                    return true;
            }
        }

        private bool TargetOk()
        {
            return Target != null && Target.Sid() == TId && Combat.Living(Target) && !Target.isInMagnet() && !Target.isInsideSomething();
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>Touches the enemies in its way. True when it is spent.</summary>
        private bool Collide()
        {
            if (Spec.Flight == Flight.Lob)
            {
                return false;   // a lob lands where it lands
            }
            Choreo.Foes(A, Pos, Radius, _near, true, true);
            long owner = AId;
            for (int i = 0; i < _near.Count; i++)
            {
                if (AId != owner)
                {
                    return false;   // an answer sent it back: the rest were its old thrower's enemies, not its new one's (next scan)
                }
                Actor u = _near[i];
                long id = u.Sid();
                float uh = Mathf.Max(0f, Combat.HeightOf(u));
                float top = uh + Choreo.SpritePx(u) * Choreo.PxOf(u) + 0.3f;
                if (_hit.Contains(id) || Z > top || Z < uh - 0.4f)
                {
                    continue;
                }
                _hit.Add(id);
                // an answer that turns the shot itself (a reflecting barrier, a catch)
                bool turned = false;
                for (int h = 0; h < Shots.Interceptors.Count; h++)
                {
                    try
                    {
                        if (Shots.Interceptors[h](this, u))
                        {
                            turned = true;
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        SakugaMod.Report("choreo shot interceptor", e);
                    }
                }
                if (turned || !Hostile || Done)
                {
                    return Done;
                }
                Shot was = Shots.Reaching;
                Shots.Reaching = this;
                Contact c;
                try
                {
                    c = Combat.Reach(By, u, false);
                }
                finally
                {
                    Shots.Reaching = was;
                }
                if (Done || !Hostile || AId != owner)
                {
                    return Done;   // the answer turned it (or sent it back: it flies on, its new thrower's)
                }
                if (c == Contact.Evaded)
                {
                    continue;   // it flies on through the afterimage
                }
                if (c == Contact.Parried)
                {
                    Burst(null, false);
                    return true;
                }
                ShotImpact how = ImpactOn(u);
                if (how == ShotImpact.Carry || how == ShotImpact.Pin)
                {
                    if (Ride(u, how))
                    {
                        return false;   // it travels with the body from now on (Tick)
                    }
                    how = ShotImpact.Shatter;
                }
                Burst(u, false);
                if (how == ShotImpact.Shatter)
                {
                    Shatter(Pos + new Vector2(0f, Z), 1f);
                    return true;
                }
                if (how == ShotImpact.Explode)
                {
                    Explode(u);
                }
                Hits++;
                if (Hits > Spec.Pierce)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>It bursts: on 'u' (its damage and reaction), and/or on the ground (a crater, an area blow).</summary>
        private void Burst(Actor u, bool ground)
        {
            Vector2 at = Pos + new Vector2(0f, Z);
            WorldTile tile = Combat.TileAt(Pos);
            bool vis = Kit.Visible(at);
            ChoreoMove cm = By as ChoreoMove;
            Scale scale = cm != null ? cm.Scale : Scale.Light;
            if (u != null && OwnerOk && Choreo.Hostile(A, u))
            {
                // (a class above the thrower's limits: it breaks on it, a glancing blow that only rocks it)
                bool full = Choreo.ClassShare(A, u, By, out float cmul);
                float decay=Mathf.Max(Spec.PierceFloor,Mathf.Pow(Spec.PierceDecay,Mathf.Max(0,Hits)));
                HitResult r = Combat.Hit(A, u, Spec.Mult * cmul * decay, false, false, 0f, By);
                Impact im = Impact.Of(full ? Spec.Hurt : Hurt.Stagger, Dir, Spec.Push * Mathf.Lerp(1f, K, 0.5f), 0.6f + 0.4f * Spec.Size);
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, By, im);
                Victims.Flash(u, Color.white, 0.05f);
                OpenLink(u, full ? Spec.Hurt : Hurt.Stagger);
                if (vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), Dir, Px, K, 0.7f + 0.4f * Spec.Size, Hue);
                }
            }
            if (vis)
            {
                Kit.Flash(at, Px, Mathf.RoundToInt((3f + 2f * Spec.Size) * Mathf.Sqrt(K)), 0.07f, Fx.WithA(Hue.Core, 0.95f));
                Fx.Ring(at, Px, 0.2f, 2f, (7f + 5f * Spec.Size) * K, 1, 1, FxLayer.Top, 4, 0.6f, 2.4f, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(at, Px, 4 + Mathf.RoundToInt(2f * Spec.Size), 40f, 100f, Hue.Edge);
                if (Spec.Kind == ShotKind.Boulder || Spec.Kind == ShotKind.Stone)
                {
                    Kit.Debris(Pos, Px, tile, Spec.Kind == ShotKind.Boulder ? 9 : 3, K);
                    Kit.DustRing(Pos, Px, tile, 8, 32f, 0.45f, K);
                }
            }
            Choreo.Sound(Spec.Kind == ShotKind.Boulder ? "event:/SFX/DESTRUCTION/DropSimpleImpact" : "event:/SFX/HIT/HitGeneric", tile);
            if (Spec.Crater > 0f && OwnerOk && (ground || Z < 0.6f))
            {
                if (vis)
                {
                    Kit.Crater(Pos, Px * K, tile, Ruin.Force(Tiers.Of(A), scale) >= 3, 5f, Spec.Crater < 1.2f, Hue.Edge);
                }
                Ruin.Blast(A, Pos, Spec.Crater, scale, A.current_position, Spec.Fire);
                Ruin.Quake(A, Pos, scale, 0.8f, K);
            }
            if (Spec.Area > 0f && OwnerOk)
            {
                Choreo.Foes(A, Pos, Spec.Area, _near);
                for (int i = 0; i < _near.Count; i++)
                {
                    Actor v = _near[i];
                    if (v == u || _hit.Contains(v.Sid()) || !Choreo.FairBlow(A, v, By, out float cmul, out bool vfull, true))
                    {
                        continue;
                    }
                    _hit.Add(v.Sid());
                    if (Combat.Reach(By, v, true) != Contact.Hit)
                    {
                        continue;
                    }
                    HitResult r = Combat.Hit(A, v, Spec.Mult * Spec.AreaMult * cmul, true, false, 0f, By);
                    if (Combat.Living(v))
                    {
                        // (one above the class limits only rocks)
                        Victims.Hurt(v, A, By, Impact.Of(vfull ? Hurt.Knockdown : Hurt.Stagger, v.current_position - Pos, 0.5f, 0.8f));
                    }
                }
            }
        }

        // ------------------------------------------------------------ impacts --

        private Actor _ride;
        private long _rideId;
        private float _rideT, _stuckT;
        private ShotImpact _rideHow;

        /// <summary>What it does to 'u' (its own setting, else by its kind and how heavy 'u' is).</summary>
        private ShotImpact ImpactOn(Actor u)
        {
            if (Spec.Impact != ShotImpact.Auto)
            {
                return Spec.Impact;
            }
            switch (Spec.Kind)
            {
                case ShotKind.Boulder:
                    {
                        // a rock carries off what it outweighs; on anything big it breaks
                        bool light = Victims.Movable(u, A) && Choreo.KOf(u) <= K * (0.9f + 0.25f * Spec.Size);
                        return light ? ShotImpact.Carry : ShotImpact.Shatter;
                    }
                case ShotKind.Stone:
                    return ShotImpact.Shatter;
                case ShotKind.Spear:
                case ShotKind.LightSword:
                case ShotKind.Arrow:
                case ShotKind.Javelin:
                    return Victims.Movable(u, A) && Choreo.KOf(u) <= K * 1.2f ? ShotImpact.Pin : ShotImpact.Burst;
                case ShotKind.Orb:
                    return Spec.Size >= 1f ? ShotImpact.Explode : ShotImpact.Burst;
                default:
                    return ShotImpact.Burst;
            }
        }

        /// <summary>
        /// It hits 'u' and takes the body with it: the blow's damage, the body thrown along its line (a low
        /// arc), and the shot pressed against it all the way. False if the body can't be moved after all.
        /// </summary>
        private bool Ride(Actor u, ShotImpact how)
        {
            if (!OwnerOk || !Choreo.Hostile(A, u))
            {
                return false;
            }
            bool full = Choreo.ClassShare(A, u, By, out float cmul);
            if (!full)
            {
                return false;
            }
            Combat.Hit(A, u, Spec.Mult * cmul, false, false, 0f, By);
            float left = Mathf.Max(0f, Spec.Range - Traveled);
            float dist = Mathf.Clamp(left, how == ShotImpact.Pin ? 1.4f : 2.2f, how == ShotImpact.Pin ? 2.6f : 4.5f) * Mathf.Lerp(1f, K, 0.5f) * (0.8f + 0.2f * Spec.Size);
            Impact im = Sakuga.Impact.Of(Hurt.Thrown, Dir, dist, 1.1f + 0.3f * Spec.Size);
            im.Lift = how == ShotImpact.Pin ? 0.35f : 0.55f;
            im.Spin = how == ShotImpact.Pin ? 0f : 200f;
            im.Owed = MeteorSlam.AtDeathsDoor(u);
            Victim b = Victims.Hurt(u, A, By, im);
            if (b == null)
            {
                return false;
            }
            OpenLink(u, Hurt.Thrown);
            _ride = u;
            _rideId = u.Sid();
            _rideT = 0f;
            _rideHow = how;
            Hits++;
            Vector2 at = Choreo.BodyAt(u, 0.5f);
            if (Kit.Visible(at))
            {
                ChoreoFx.Impact(at, Dir, Px, K, 0.9f + 0.4f * Spec.Size, Hue);
                Kit.Flash(at, Px, Mathf.RoundToInt(4f * Mathf.Sqrt(K)), 0.06f, Color.white);
            }
            Choreo.Sound(how == ShotImpact.Pin ? "event:/SFX/HIT/HitMetal" : "event:/SFX/DESTRUCTION/DropSimpleImpact", Combat.TileAt(Pos), 0.04f);
            return true;
        }

        /// <summary>
        /// A shot that knocks its target off its feet opens a chain for its thrower (as a blow would): a downed
        /// body takes a dive or a stomp, a launched one an air chase, a thrown one a pursuit. Only once the
        /// thrower is free of its own move (its move's own chain logic covers it while it still plays).
        /// </summary>
        private void OpenLink(Actor u, Hurt hurt)
        {
            if (!OwnerOk || !Combat.Living(u) || Moves.Busy(A))
            {
                return;
            }
            Link l = hurt == Hurt.Launch || hurt == Hurt.Juggle ? Link.Launched
                : hurt == Hurt.Knockdown || hurt == Hurt.DriveIn || hurt == Hurt.Pin ? Link.Down
                : hurt == Hurt.Thrown ? Link.Thrown : Link.None;
            if (l != Link.None)
            {
                Crew.Handoff(A, l, u, 0.5f);
            }
        }

        /// <summary>Riding the body: pressed against it until it comes down. False when it is over.</summary>
        private bool RideTick(float dt)
        {
            if (_stuckT > 0f)
            {
                // a spear left standing in the ground a moment, then gone
                _stuckT -= dt;
                return _stuckT > 0f;
            }
            _rideT += dt;
            Victim b = _ride != null && _ride.Sid() == _rideId ? Victims.Get(_ride) : null;
            bool down = b == null || b.Done || (!b.Aloft && _rideT > 0.12f) || _rideT > 2.5f;
            if (b != null && !b.Done)
            {
                Pose p = b.P.Pose;
                float mid = Choreo.SpritePx(_ride) * Choreo.PxOf(_ride) * 0.45f;
                Pos = p.Ground - Dir * (_rideHow == ShotImpact.Pin ? 0.05f : 0.3f) * K;
                Z = p.Height + mid;
            }
            if (!down)
            {
                return true;
            }
            Hostile = false;
            if (_rideHow == ShotImpact.Pin)
            {
                // nailed: it stands in the ground, slanting down through where the body lies
                Dir = (new Vector2(Dir.x * 0.45f, -0.9f)).normalized;
                Z = 0.25f * K;
                _stuckT = 1.1f;
                Vector2 at = Pos + new Vector2(0f, Z);
                if (Kit.Visible(at))
                {
                    Kit.Sparks(at, Px, 4, 30f, 70f, Hue.Edge);
                    Kit.DustRing(Pos, Px, Combat.TileAt(Pos), 5, 20f, 0.35f, K);
                }
                return true;
            }
            // they crash together: the rock smashes
            Z = 0.2f;
            Burst(null, true);
            Shatter(Pos + new Vector2(0f, 0.3f * K), 1.3f);
            return false;
        }

        /// <summary>It breaks apart: fragments thrown out (its own colour), dust, a crack of sound.</summary>
        private void Shatter(Vector2 at, float k)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            WorldTile tile = Combat.TileAt(Pos);
            bool rock = Spec.Kind == ShotKind.Boulder || Spec.Kind == ShotKind.Stone;
            Color c = rock ? Color.Lerp(Fx.SoilColor(Combat.TileAt(Start)), Color.white, 0.15f) : Hue.Main;
            int n = Mathf.RoundToInt((rock ? 10f : 6f) * Spec.Size * k);
            for (int i = 0; i < n; i++)
            {
                Vector2 v = Random.insideUnitCircle.normalized * Random.Range(25f, 70f) * Px * Mathf.Sqrt(K) + Dir * 20f * Px;
                Particle f = Fx.Spawn(Art.Disk(Random.Range(1, Mathf.Max(2, Mathf.RoundToInt(2f * Spec.Size))), 1f, 0f), at, Px, Random.Range(0.35f, 0.6f), FxLayer.Top, 13);
                f?.Moving(v, -160f * Px, 2.5f).Sized(1f, 0.6f).Color(c, Color.Lerp(c, Color.black, 0.3f), Fx.WithA(c, 0f));
            }
            if (rock)
            {
                Kit.Debris(Pos, Px, tile, Mathf.RoundToInt(6f * Spec.Size * k), K, Z);
                Kit.DustRing(Pos, Px, tile, 9, 34f, 0.5f, K * k);
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.04f);
            }
            else
            {
                Kit.Sparks(at, Px, 6, 40f, 110f, Hue.Edge);
            }
        }

        /// <summary>It goes off like a shell on 'u': the bodies round it are thrown out from the blast.</summary>
        private void Explode(Actor u)
        {
            if (!OwnerOk)
            {
                return;
            }
            float r = (0.9f + 0.5f * Spec.Size) * Mathf.Lerp(1f, K, 0.5f);
            Vector2 at = Pos + new Vector2(0f, Z);
            Choreo.Foes(A, Pos, r, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (v == u || _hit.Contains(v.Sid()) || !Choreo.FairBlow(A, v, By, out float cmul, out bool vfull, true))
                {
                    continue;
                }
                _hit.Add(v.Sid());
                Combat.Hit(A, v, Spec.Mult * 0.35f * cmul, true, false, 0f, By);
                if (Combat.Living(v))
                {
                    Victims.Hurt(v, A, By, Sakuga.Impact.Of(vfull ? Hurt.Knockdown : Hurt.Stagger, v.current_position - Pos, 0.7f * K, 0.8f));
                }
            }
            if (Kit.Visible(at))
            {
                Kit.Flash(at, Px, Mathf.RoundToInt(6f * Spec.Size * Mathf.Sqrt(K)), 0.09f, Fx.WithA(Hue.Core, 1f));
                Fx.Ring(at, Px, 0.28f, 2f, r / Px * 0.9f, 2, 1, FxLayer.Top, 5, 0.6f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                Kit.Sparks(at, Px, 8, 50f, 130f, Hue.Edge);
                Kit.DustRing(Pos, Px, Combat.TileAt(Pos), 6, 28f, 0.4f, K);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(Pos), 0.05f);
        }

        /// <summary>Spent in the air: a small puff.</summary>
        private void Fizzle()
        {
            if (Kit.Visible(Pos))
            {
                Kit.Flash(Pos, Px, 2, 0.05f, Fx.WithA(Hue.Core, 0.8f));
            }
        }

        // ------------------------------------------------------------- render --

        private void Render(Vector2 last, float dt)
        {
            Vector2 at = Pos + new Vector2(0f, Z);
            if (!Kit.Visible(at, 0.2f))
            {
                Kill();
                return;
            }
            float px = Px;
            float s = Spec.Size * Mathf.Lerp(1f, K, 0.6f);
            float ang = Mathf.Atan2(Dir.y, Dir.x) * Mathf.Rad2Deg;
            if (_head == null || !_head.Alive)
            {
                _head = Fx.Spawn(HeadSprite(s), at, px, 1f, FxLayer.Top, 12);
                if (_head != null)
                {
                    _head.Held = true;
                    if (Spec.Kind == ShotKind.Disc)
                    {
                        _head.Fps = 24f;
                        _head.Loop = true;
                    }
                }
            }
            if (_head != null)
            {
                _head.Pos = at;
                switch (Spec.Kind)
                {
                    case ShotKind.Boulder:
                    case ShotKind.Stone:
                        _head.Rot += 400f * dt;
                        break;
                    case ShotKind.LightSword:
                        _head.Rot = ang + 90f;
                        break;
                    case ShotKind.Disc:
                        _head.Rot = 0f;
                        break;
                    case ShotKind.Orb:
                    case ShotKind.Bullet:
                        _head.Rot = 0f;
                        break;
                    default:
                        _head.Rot = ang;
                        break;
                }
                _head.Grad = null;
                _head.R.color = Spec.Kind == ShotKind.Boulder || Spec.Kind == ShotKind.Stone ? Color.Lerp(Fx.SoilColor(Combat.TileAt(Start)), Color.white, 0.15f)
                    : Spec.Kind == ShotKind.Arrow ? Color.Lerp(new Color(0.86f, 0.8f, 0.68f, 1f), Hue.Core, 0.3f)
                    : Spec.Kind == ShotKind.Javelin ? new Color(0.66f, 0.48f, 0.3f, 1f)
                    : Spec.Kind == ShotKind.Fist ? Color.Lerp(Hue.Main, Color.white, 0.2f)
                    : Hostile ? Color.Lerp(Hue.Core, Hue.Main, Spec.Kind == ShotKind.Bullet ? 0f : 0.25f) : Fx.WithA(Hue.Main, 0.6f);
            }
            if (Spec.Kind == ShotKind.Orb || Spec.Kind == ShotKind.Bullet || Spec.Kind == ShotKind.Spear || Spec.Kind == ShotKind.Fist || Spec.Kind == ShotKind.Javelin)
            {
                if (_glow == null || !_glow.Alive)
                {
                    // (a fist of air: the white front of pressure it pushes ahead of it, not a glow)
                    // (a javelin: the steel point at the front of its wooden shaft)
                    _glow = Spec.Kind == ShotKind.Fist
                        ? Fx.Spawn(ChoreoArt.Crescent(Mathf.Max(3, Mathf.RoundToInt(4.5f * s)), 1), at, px, 1f, FxLayer.Top, 13)
                        : Spec.Kind == ShotKind.Javelin
                        ? Fx.Spawn(ChoreoArt.Spike(2, Mathf.Max(3, Mathf.RoundToInt(3f * s))), at, px, 1f, FxLayer.Top, 13)
                        : Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(2f * s)), 1f, 0.4f), at, px, 1f, FxLayer.Top, 11);
                    if (_glow != null)
                    {
                        _glow.Held = true;
                    }
                }
                if (_glow != null)
                {
                    _glow.Pos = at;
                    _glow.Grad = null;
                    float pulse = 1f + 0.15f * Mathf.Sin(Age * 40f);
                    _glow.Size0 = _glow.Size1 = pulse;
                    _glow.R.color = Fx.WithA(Hue.Edge, 0.75f);
                    if (Spec.Kind == ShotKind.Fist)
                    {
                        _glow.Pos = at + Dir * 2.6f * px * s;
                        _glow.Rot = ang;
                        _glow.R.color = Fx.WithA(Color.white, 0.85f);
                    }
                    else if (Spec.Kind == ShotKind.Javelin)
                    {
                        _glow.Size0 = _glow.Size1 = 1f;
                        _glow.Rot = ang - 90f;
                        _glow.R.color = new Color(0.86f, 0.9f, 0.95f, 1f);
                    }
                }
            }
            if (Z > 0.25f)
            {
                if (_shadow == null || !_shadow.Alive)
                {
                    _shadow = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(2f * s)), Art.Ground, 0.3f), Pos, px, 1f, FxLayer.Ground, 2);
                    if (_shadow != null)
                    {
                        _shadow.Held = true;
                    }
                }
                if (_shadow != null)
                {
                    _shadow.Pos = Pos;
                    _shadow.Grad = null;
                    _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.35f, 0.12f, Z / Mathf.Max(1f, Spec.Apex)));
                }
            }
            else if (_shadow != null)
            {
                _shadow.Kill();
                _shadow = null;
            }
            if (Spec.Kind == ShotKind.Harpoon)
            {
                Chain(at);
            }
            // its trail
            _trailT -= dt;
            Vector2 lastAt = last + new Vector2(0f, Z);
            if (_trailT <= 0f && (at - lastAt).sqrMagnitude > px * px * 0.25f)
            {
                _trailT = Spec.LunarCrescent ? 0.07f : Shots.Count > 60 ? 0.05f : 0f;
                if(Spec.LunarCrescent)
                {
                    Particle ghost=Fx.Spawn(ChoreoArt.Crescent(26,9),at,px,.24f,FxLayer.Top,9);
                    if(ghost!=null){ghost.Rot=ang;ghost.Color(Fx.WithA(Color.white,.22f),Fx.WithA(Hue.Edge,0f));}
                }
                lastAt = Vector2.Lerp(lastAt, at, 0f);
                switch (Spec.Kind)
                {
                    case ShotKind.Boulder:
                    case ShotKind.Stone:
                        if (Random.value < 0.4f)
                        {
                            Particle d = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, 0.3f, FxLayer.Top, 8);
                            d?.Sized(0.5f, 0.9f).Color(Fx.WithA(Fx.SoilColor(Combat.TileAt(Pos)), 0.6f), Fx.WithA(Fx.SoilColor(Combat.TileAt(Pos)), 0f));
                        }
                        break;
                    case ShotKind.Disc:
                    case ShotKind.WindBlade:
                    case ShotKind.SwordWave:
                        Kit.Line(lastAt, at, px, 0.12f, 1, FxLayer.Top, 9, Fx.WithA(Hue.Core, 0.6f), Fx.WithA(Hue.Edge, 0f));
                        break;
                    case ShotKind.Javelin:
                        Kit.Line(lastAt, at, px, 0.06f, 1, FxLayer.Top, 9, Fx.WithA(Color.white, 0.45f), Fx.WithA(Color.white, 0f));
                        break;
                    case ShotKind.Fist:
                        // a fist of pressure: a thick streak and rings of air peeling off it
                        Kit.Line(lastAt, at, px, 0.1f, 2, FxLayer.Top, 10, Fx.WithA(Hue.Core, 0.75f), Fx.WithA(Hue.Edge, 0f));
                        if (Random.value < 0.35f)
                        {
                            Fx.Ring(at - Dir * 3f * px, px, 0.22f, 2f, 7f * Spec.Size, 1, 1, FxLayer.Top, 9, 1f, 2f, Fx.WithA(Color.white, 0.7f), Fx.WithA(Hue.Edge, 0f));
                        }
                        break;
                    default:
                        Kit.Line(lastAt, at, px, Spec.Kind == ShotKind.Bullet ? 0.07f : 0.11f, Spec.Kind == ShotKind.Spear ? 2 : 1, FxLayer.Top, 10,
                            Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                        break;
                }
                if (Spec.Kind == ShotKind.Orb && Shots.Count <= 40 && Random.value < 0.5f)
                {
                    Particle m = Fx.Spawn(Art.Pixel, at + Random.insideUnitCircle * 1.5f * px, px, 0.14f, FxLayer.Top, 9);
                    m?.Moving(-Dir * Random.Range(6f, 14f) * px + Random.insideUnitCircle * 6f * px, 0f, 3f)
                        .Color(Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                }
            }
        }

        private Sprite[] HeadSprite(float s)
        {
            switch (Spec.Kind)
            {
                case ShotKind.Bullet:
                    return new[] { Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1f * s)), 1f, 0f) };
                case ShotKind.Disc:
                    return ChoreoArt.Disc(Mathf.RoundToInt(4f * s));
                case ShotKind.Boulder:
                    return new[] { ChoreoArt.Boulder(Mathf.RoundToInt(4f * s), Random.Range(0, 4)) };
                case ShotKind.Stone:
                    return new[] { ChoreoArt.Boulder(Mathf.Max(2, Mathf.RoundToInt(1.5f * s)), Random.Range(0, 4)) };
                case ShotKind.WindBlade:
                    return new[] { ChoreoArt.Crescent(Mathf.RoundToInt(5f * s), 1) };
                case ShotKind.SwordWave:
                    if(Spec.LunarCrescent)return new[]{ChoreoArt.Crescent(26,9)};
                    return new[] { ChoreoArt.Crescent(Mathf.RoundToInt(8f * s), 2) };
                case ShotKind.LightSword:
                    return new[] { ChoreoArt.LightSword(Mathf.RoundToInt(9f * s)) };
                case ShotKind.Harpoon:
                    return new[] { ChoreoArt.Spike(3, 4) };
                case ShotKind.Spear:
                    return new[] { ChoreoArt.Smear(Mathf.RoundToInt(14f * s), 1, 2)[0] };
                case ShotKind.Fist:
                    return new[] { ChoreoArt.Fist(Mathf.RoundToInt(3.5f * s)) };
                case ShotKind.Arrow:
                    return new[] { ChoreoArt.Smear(Mathf.RoundToInt(8f * s), 0, 1)[0] };
                case ShotKind.Javelin:
                    return new[] { ChoreoArt.Smear(Mathf.RoundToInt(12f * s), 0, 1)[0] };
                default:
                    return new[] { Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.5f * s)), 1f, 0f) };
            }
        }

        /// <summary>A harpoon's chain from the thrower's hand to its head.</summary>
        private void Chain(Vector2 head)
        {
            if (!OwnerOk)
            {
                return;
            }
            Vector2 hand = Choreo.BodyAt(A, 0.55f);
            Vector2 d = head - hand;
            float len = d.magnitude;
            int n = Mathf.Clamp(Mathf.RoundToInt(len / (Px * 4f)), 1, 40);
            _chain ??= new Particle[40];
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < 40; i++)
            {
                Particle p = _chain[i];
                if (i >= n)
                {
                    if (p != null && p.Alive)
                    {
                        p.Kill();
                    }
                    _chain[i] = null;
                    continue;
                }
                if (p == null || !p.Alive)
                {
                    p = Fx.Spawn(ChoreoArt.ChainLink(), hand, Px, 1f, FxLayer.Top, 10);
                    if (p == null)
                    {
                        break;
                    }
                    p.Held = true;
                    _chain[i] = p;
                }
                p.Pos = Vector2.Lerp(hand, head, (i + 0.5f) / n);
                p.Rot = ang;
                p.Grad = null;
                p.R.color = Color.Lerp(Hue.Edge, Hue.Core, (i & 1) == 0 ? 0.3f : 0f);
            }
        }

        private bool _audited;

        public void Kill()
        {
            if (!_audited && Dev.On && By != null && By.Tech != null)
            {
                _audited = true;
                Dev.Trace("audit shot " + By.Tech.Id + " " + Spec.Kind + " hits=" + Hits + " traveled=" + Traveled.ToString("F1") + (Hostile ? "" : " turned"));
            }
            if (_head != null && _head.Alive)
            {
                _head.Kill();
            }
            if (_glow != null && _glow.Alive)
            {
                _glow.Kill();
            }
            if (_shadow != null && _shadow.Alive)
            {
                _shadow.Kill();
            }
            _head = _glow = _shadow = null;
            if (_chain != null)
            {
                for (int i = 0; i < _chain.Length; i++)
                {
                    if (_chain[i] != null && _chain[i].Alive)
                    {
                        _chain[i].Kill();
                    }
                    _chain[i] = null;
                }
            }
        }
    }

    /// <summary>The engine's projectiles in flight (ticked once per frame after the moves).</summary>
    internal static class Shots
    {
        private static readonly List<Shot> _list = new List<Shot>();

        /// <summary>
        /// Answers that turn a shot before it touches 'target' (reflect barriers, catches): return true
        /// if the shot was handled (Reflect, Deflect or Cut it). Register from a [SakugaInit].
        /// </summary>
        public static readonly List<Func<Shot, Actor, bool>> Interceptors = new List<Func<Shot, Actor, bool>>();

        /// <summary>The shot whose Combat.Reach is being answered right now (a reactive technique may Reflect it).</summary>
        public static Shot Reaching;

        public static int Count => _list.Count;

        public static IReadOnlyList<Shot> All => _list;

        /// <summary>The most shots in flight at once (past it a shot lands at once).</summary>
        private const int Max = 300;

        /// <summary>
        /// Looses a shot of 'a' (from move 'by') from 'from' along 'dir' (or at 'target'; a lob at
        /// 'goal' or the target's spot). Colours from 'hue'.
        /// </summary>
        public static Shot Fire(Actor a, Move by, Vector2 from, Vector2 dir, Actor target, Vector2 goal, ShotSpec spec, Hue hue)
        {
            if (a == null || spec == null)
            {
                return null;
            }
            if (target != null && dir.sqrMagnitude < 1e-6f)
            {
                dir = Choreo.BodyAt(target, 0.45f) - from;
            }
            if (spec.Flight == Flight.Lob && goal.sqrMagnitude < 0.01f)
            {
                goal = target != null ? target.current_position : from + dir.normalized * spec.Range;
            }
            Shot s = new Shot(a, by, from, dir, target, goal, spec, hue);
            if (_list.Count >= Max)
            {
                s.Done = true;
                s.Kill();
                return null;
            }
            _list.Add(s);
            return s;
        }

        /// <summary>The nearest hostile shot flying at 'v' within 'within' tiles (for answers: deflect, catch, dodge).</summary>
        public static Shot Incoming(Actor v, float within = 3f)
        {
            Shot best = null;
            float bd = within * within;
            for (int i = 0; i < _list.Count; i++)
            {
                Shot s = _list[i];
                if (s.Done || !s.Hostile || s.A == v || s.A == null || !Combat.IsFoe(s.A, v))
                {
                    continue;
                }
                Vector2 rel = v.current_position - s.Pos;
                if (Vector2.Dot(rel, s.Dir) < 0f)
                {
                    continue;   // flying away from it
                }
                float d = rel.sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = s;
                }
            }
            return best;
        }

        public static void Tick(float dt)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Shot s = _list[i];
                bool alive;
                try
                {
                    alive = s.Tick(dt);
                }
                catch (Exception e)
                {
                    SakugaMod.Report("choreo shot", e);
                    s.Done = true;
                    s.Kill();
                    alive = false;
                }
                if (!alive)
                {
                    _list.Remove(s);
                }
            }
        }

        public static void Clear()
        {
            foreach (Shot s in _list)
            {
                s.Done = true;
                s.Kill();
            }
            _list.Clear();
            Reaching = null;
        }
    }

    internal static class ShotSpecExt
    {
        public static object MemberwiseCloneShot(this ShotSpec s)
        {
            return new ShotSpec
            {
                Kind = s.Kind,
                Flight = s.Flight,
                Speed = s.Speed,
                Range = s.Range,
                Size = s.Size,
                Mult = s.Mult,
                PierceDecay = s.PierceDecay,
                PierceFloor = s.PierceFloor,
                LunarCrescent = s.LunarCrescent,
                Hurt = s.Hurt,
                Push = s.Push,
                Pierce = s.Pierce,
                Turn = s.Turn,
                Apex = s.Apex,
                Crater = s.Crater,
                Fire = s.Fire,
                Area = s.Area,
                AreaMult = s.AreaMult,
                Deflectable = s.Deflectable,
                Reflectable = s.Reflectable,
                Cuttable = s.Cuttable,
                Element = s.Element,
                Delay = s.Delay
            };
        }
    }

    internal partial class ChoreoMove
    {
        /// <summary>
        /// Looses a shot (or a volley: 'count' of them, 'spread' degrees apart, 'gap' seconds apart) from
        /// its hands at the target (or along the plan's direction): a flick of the hand, a muzzle flash.
        /// The shot flies on its own (the move does not wait for it).
        /// </summary>
        public ChoreoMove Shoot(ShotSpec spec, int count = 1, float spread = 0f, float gap = 0.06f, Limb from = Limb.Palm)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f + gap * (count - 1),
                To = new Shape(10f, 1.18f, 0.9f, 0f, 0.06f),
                Ease = Ease.OutExpo,
                Forms = Form.Projectile | (spec.Kind == ShotKind.Orb || spec.Kind == ShotKind.Bullet ? Form.Ki : Form.None)
            };
            b.Enter = m =>
            {
                if (m.V != null && m.VOk)
                {
                    m.Face(m.VG - m.Stance);
                }
                Vector2 at = from == Limb.Palm && m._orb != null ? m.Muzzle() : m.Hand(from);
                m.SpendOrb();
                Vector2 aim = m.V != null && m.VOk ? m.VPoint(0.45f) - at : m.Aim;
                Hue hue = spec.Element == Element.School ? m.Hue : m.Hue.As(spec.Element);
                for (int i = 0; i < count; i++)
                {
                    float off = count == 1 ? 0f : (i - (count - 1) * 0.5f) * spread;
                    ShotSpec s = count == 1 ? spec : (ShotSpec)spec.MemberwiseCloneShot();
                    s.Delay = spec.Delay + i * gap;
                    Actor tgt = m.V != null && m.VOk ? m.V : null;
                    Shot sh = Shots.Fire(m.A, m, at, Choreo.Rotate(aim, off), tgt, Vector2.zero, s, hue);
                    if (sh != null && tgt != null && off != 0f)
                    {
                        sh.Dir = Choreo.Rotate(sh.Dir, off).normalized;   // (a shot at a target is aimed at its feet: the fan is turned from there)
                    }
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.Flash(at, m.Px, Mathf.Max(2, Mathf.RoundToInt(3f * m.Kk)), 0.06f, Fx.WithA(hue.Core, 0.95f), m.TopLayer, 22);
                    Kit.Sparks(at, m.Px, 2, 50f, 80f, hue.Edge);
                }
            };
            return Add(b);
        }
    }
}
