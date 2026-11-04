using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CHOREO. The choreography engine the expansion's techniques are written on. A technique is a
    /// short script of beats (ChoreoMove: anticipation, action, follow-through, recovery) over shared,
    /// polished primitives that already know how to look right at any size and tier:
    ///   movement     step-ins, dashes, blinks, leaps, slips, back-hops, circling, air-walks, spins
    ///   strikes      every limb and blade cut, with smears, squash and stretch, hit-sparks, impact
    ///                frames and hit-stop scaled to the blow; flurries (Strikes.cs)
    ///   victims      generic reactions that make the one hit FEEL it: flinch, snap-back, fold, reel,
    ///                spin, knockdown, launch, juggle, driven in, pinned, thrown, crumple (Victims.cs)
    ///   grabs        held bodies that follow the hands, lifts, suplexes, slams, piledrivers, tosses
    ///   shots        the mod's own projectiles, deflectable, reflectable, cuttable (Shots.cs)
    ///   beams        charged beams with a bright core, trenches, and head-on beam clashes (Beams.cs)
    ///   areas        shockwaves, domes, quakes, pillars, cones, tornadoes, rains (Areas.cs)
    ///   clones       silhouette copies that move and strike on their own, mirrors (Clones.cs)
    /// plus the palette grammar (Hue), destruction scaling (Ruin), cinematic hooks, generated sprites
    /// (ChoreoArt) and the interrupt entry point Choreo.Break. The guide for technique authors is
    /// CHOREO.md in the expansion folder.
    ///
    /// Everything ticks in move time (paused with the game, held by hit-stops and cinematics) once
    /// per frame after the moves (Hooks.FrameTick), draws nothing off screen, and leaves nothing
    /// behind when a unit dies, is picked up, or the world is cleared.
    /// </summary>
    internal static partial class Choreo
    {
        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += Clear;
            // a move whose blow was parried or evaded reels: open to the counter (Interruptible), but its
            // own Brain starts nothing in the meantime (the punish window stays open)
            Hooks.HoldTechniques.Add(a => Moves.TryGet(a, out Move m) && m.A == a && m is ChoreoMove cm && cm.Reeling);
            ChoreoDev.Register();
        }

        private static void Tick(float dt)
        {
            try
            {
                Victims.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo victims", e);
            }
            try
            {
                Shots.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo shots", e);
            }
            try
            {
                Beams.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo beams", e);
            }
            try
            {
                Areas.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo areas", e);
            }
            try
            {
                Clones.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo clones", e);
            }
            try
            {
                TickPending(dt);
                Trails.Tick(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo pending", e);
            }
        }

        /// <summary>The world is cleared or a save loaded: nothing of the old world may remain.</summary>
        public static void Clear()
        {
            Hooks.Safe(Victims.Clear, "choreo victims clear");
            Hooks.Safe(Shots.Clear, "choreo shots clear");
            Hooks.Safe(Beams.Clear, "choreo beams clear");
            Hooks.Safe(Areas.Clear, "choreo areas clear");
            Hooks.Safe(Clones.Clear, "choreo clones clear");
            Hooks.Safe(Trails.Clear, "choreo trails clear");
            _pending.Clear();
        }

        // ------------------------------------------------------------------ break --

        /// <summary>
        /// INTERRUPT. 'by' breaks whatever 'victim' is doing with a blow along 'dir' ('force': how far
        /// it flies, in tiles: 1 a shove, 3 a kick, 6 and more a huge blow). A Choreo move that is
        /// gathering power (Charging) bursts: the motes it gathered scatter, its ring breaks, its orb
        /// pops, and the performer is blasted away tumbling inside its own move (no charge is ever
        /// half-played). Any other move of the victim's own (a wind-up of the older techniques) is
        /// cut off and the victim is launched. A unit in nobody's move is simply launched. A unit
        /// someone else is holding (a puppet) is left to its holder. Heavy units only stagger.
        /// True if a move was broken. Safe to call from anywhere on the main thread.
        /// </summary>
        public static bool Break(Actor victim, Actor by, Vector2 dir, float force)
        {
            if (victim == null || !Combat.Living(victim) || victim.isInMagnet() || victim.isInsideSomething())
            {
                return false;
            }
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = by != null ? victim.current_position - by.current_position : Random.insideUnitCircle;
                if (dir.sqrMagnitude < 1e-6f)
                {
                    dir = Vector2.right;
                }
            }
            dir.Normalize();
            force = Mathf.Clamp(force, 0.3f, 20f);
            if (Moves.TryGet(victim, out Move m))
            {
                if (m is ChoreoMove cm)
                {
                    return cm.BreakCharge(by, dir, force);
                }
                if (m is Puppet)
                {
                    return false;   // in someone else's hands: its holder decides
                }
                if (!m.Interruptible)
                {
                    BurstAt(m.BodyPos(), m.Px, m.Charging, by);
                    Kill(m);
                    Launch(victim, dir, force);
                    if (Dev.On) Dev.Trace("choreo break: " + m.GetType().Name + " of " + victim.asset.id + "#" + victim.Sid() + " cut off");
                    return true;
                }
            }
            Launch(victim, dir, force);
            return false;
        }

        /// <summary>
        /// Ends any move at once, as aborted (its Abort cleans up; it leaves no chain; Moves.Cancel), so
        /// the unit is free this very frame. Use it only to cut off another unit's move.
        /// </summary>
        public static void Kill(Move m)
        {
            if (m == null || m.Ended)
            {
                return;
            }
            m.EndLink = Link.None;
            m.EndTarget = null;
            Moves.Cancel(m);
        }

        /// <summary>
        /// Knocks a free unit flying along 'dir', 'force' tiles, 'apex' tiles high (-1: a height fitting
        /// the distance), tumbling 'spin' degrees a second. Heavy ones stagger; one still in a move
        /// flies as soon as it is let go.
        /// </summary>
        public static void Launch(Actor v, Vector2 dir, float force, float spin = 720f, float apex = -1f)
        {
            if (v == null || !Combat.Living(v) || RealmControl.BlocksLower(v)
                || Primordials.Any && Primordials.Is(v) && !RealmControl.Breached(v, RealmControl.RecentAttacker(v)))
            {
                return;
            }
            if (Weights.Heavy(v))
            {
                Combat.Stagger(v, Mathf.Clamp(0.25f + force * 0.08f, 0.3f, 1.2f));
                return;
            }
            if (Moves.Locked(v))
            {
                Defer(v, dir, force, spin, apex);
                return;
            }
            Combat.LaunchDir(v, dir, force, apex > 0f ? apex : Apex(force), spin);
            Trails.Follow(v, 0.6f + force * 0.06f);
        }

        /// <summary>
        /// Knocked out (stunned) inside a move: the game only recalculates a unit's stats (and with them
        /// "out cold", which keeps a body lying down) once the move lets it go. This does it now, so a
        /// body laid down by a move stays down when it is let go instead of popping upright.
        /// </summary>
        public static void Daze(Actor v, float seconds)
        {
            if (v == null || !Combat.Living(v))
            {
                return;
            }
            Combat.Daze(v, seconds);
            try
            {
                if (v.isStatsDirty())
                {
                    v.updateStats();
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo daze", e);
            }
        }

        /// <summary>A launch's height for a throw of 'force' tiles.</summary>
        public static float Apex(float force)
        {
            return Mathf.Clamp(0.45f + force * 0.22f, 0.5f, 4f);
        }

        // a launch waiting for the unit's move to let go of it (it ends on the next update)
        private sealed class Pending
        {
            public Actor A;
            public long Id;
            public Vector2 Dir;
            public float Force, Spin, Apex, Age;
        }

        private static readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Launches 'v' as soon as no move holds it any more (within half a second).</summary>
        public static void Defer(Actor v, Vector2 dir, float force, float spin = 720f, float apex = -1f)
        {
            if (v == null)
            {
                return;
            }
            long id = v.Sid();
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].A == v && _pending[i].Id == id)
                {
                    _pending[i].Dir = dir;
                    _pending[i].Force = force;
                    _pending[i].Apex = apex;
                    return;
                }
            }
            _pending.Add(new Pending { A = v, Id = id, Dir = dir, Force = force, Spin = spin, Apex = apex });
        }

        private static void TickPending(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                p.Age += Mathf.Max(dt, Time.unscaledDeltaTime * 0.25f);
                if (p.A == null || p.A.Sid() != p.Id || !Combat.Living(p.A) || p.Age > 0.6f)
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (Moves.Busy(p.A))
                {
                    continue;
                }
                _pending.RemoveAt(i);
                Launch(p.A, p.Dir, p.Force, p.Spin, p.Apex);
            }
        }

        /// <summary>Gathered energy bursting out of a broken charge (motes scattering, a broken ring).</summary>
        public static void BurstAt(Vector2 at, float px, bool charged, Actor by)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Color c = by != null ? Tiers.Color(Tiers.Of(by)) : Color.white;
            Kit.Flash(at, px, 4, 0.08f, Fx.WithA(Color.white, 0.95f));
            Fx.Ring(at, px, 0.25f, 3f, 16f, 2, 1, FxLayer.Top, 5, 1f, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
            int n = Fx.Count(charged ? 14 : 7);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(Art.Pixel, at + d * 2f * px, px, Random.Range(0.25f, 0.45f), FxLayer.Top, 9);
                p?.Moving(d * Random.Range(40f, 110f) * px, 0f, 4f).Sized(1.5f, 1f).Color(Color.white, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            }
        }

        // ------------------------------------------------------------- measures --

        /// <summary>World units per sprite pixel of a unit (effects are drawn at its resolution).</summary>
        public static float PxOf(Actor a)
        {
            return a == null ? 0.1f : Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
        }

        /// <summary>A unit's size relative to a human (1 a human, about 1.4 a bear, 0.8 small).</summary>
        public static float KOf(Actor a)
        {
            return a == null ? 1f : Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        /// <summary>A unit's sprite height in its own pixels (about 10-12 for people).</summary>
        public static float SpritePx(Actor a)
        {
            Sprite s = a != null ? Fx.UnitSprite(a) : null;
            return s != null ? Mathf.Clamp(s.rect.height, 4f, 120f) : 10f;
        }

        /// <summary>Where a unit's feet are drawn right now (its pose while it performs or is held).</summary>
        public static Vector2 Feet(Actor v)
        {
            if (v == null)
            {
                return Vector2.zero;
            }
            if (Moves.TryGet(v, out Move m))
            {
                return m.Pose.Ground + new Vector2(0f, m.Pose.Height);
            }
            return v.current_position + new Vector2(0f, Mathf.Max(0f, v.position_height));
        }

        /// <summary>A point on a unit's body, 'frac' of the way up its sprite (0 feet .. 1 head).</summary>
        public static Vector2 BodyAt(Actor v, float frac)
        {
            return BodyPoint(v, frac, SpritePx(v) * PxOf(v));
        }

        /// <summary>
        /// A point 'frac' of the way from a unit's feet to its head ('height' world units tall) along its
        /// body as it is drawn this frame: straight up while it stands, but along the body while it tumbles,
        /// lies on the ground or hangs upside down (a kick to the head of a body lying in the dirt lands on
        /// its head, not in the air above it). Moves turn their bodies around the middle (Moves.PivotOffset).
        /// </summary>
        public static Vector2 BodyPoint(Actor v, float frac, float height)
        {
            if (v == null)
            {
                return Vector2.zero;
            }
            if (Moves.TryGet(v, out Move m))
            {
                Pose p = m.Pose;
                float along = height * p.SY * frac;
                float rot = Mathf.DeltaAngle(0f, p.Rot);
                if (Mathf.Abs(rot) < 2f)
                {
                    return p.Ground + new Vector2(0f, p.Height + along);
                }
                float hc = p.PivotUp * v.actor_scale * p.SY;
                float th = (p.FaceRight && v.asset.can_flip ? -rot : rot) * Mathf.Deg2Rad;
                Vector2 mid = p.Ground + new Vector2(0f, p.Height + hc);
                return mid + new Vector2(-Mathf.Sin(th), Mathf.Cos(th)) * (along - hc);
            }
            return v.current_position + new Vector2(0f, Mathf.Max(0f, v.position_height) + height * frac);
        }

        /// <summary>One pixel of tremble, left and right, at time t.</summary>
        public static Vector2 Tremble(float t, float amp = 0.5f)
        {
            return new Vector2(((int)(t * 60f) & 1) == 0 ? amp : -amp, 0f);
        }

        /// <summary>A sound, the same one at most ~30 times a second.</summary>
        public static void Sound(string id, WorldTile tile, float gap = 0.035f)
        {
            if (id == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (_sounds.TryGetValue(id, out float at) && now - at < gap)
            {
                return;
            }
            _sounds[id] = now;
            Anim.Sound(id, tile);
        }

        private static readonly Dictionary<string, float> _sounds = new Dictionary<string, float>();

        public static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The perpendicular of a direction (its left side).</summary>
        public static Vector2 Perp(Vector2 d)
        {
            return new Vector2(-d.y, d.x);
        }

        // ---------------------------------------------------------------- foes --

        private static readonly List<Actor> _scan = new List<Actor>();

        /// <summary>
        /// Enemies of 'a' within 'r' of 'at' into 'into' (cleared first; no new list): alive, hostile,
        /// touchable (Combat.IsFoe), on the ground unless 'air', not held by someone else's move unless 'busy'.
        /// </summary>
        public static int Foes(Actor a, Vector2 at, float r, List<Actor> into, bool air = false, bool busy = false)
        {
            into.Clear();
            WorldTile tile = Combat.TileAt(at);
            if (a == null || tile == null || a.kingdom == null)
            {
                return 0;
            }
            int chunks = r > 9f ? 2 : 1;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, chunks))
            {
                if (u == null || u == a || !Combat.IsFoe(a, u))
                {
                    continue;
                }
                if (!busy && !Hittable(u))
                {
                    continue;
                }
                if (!air && !Combat.Grounded(u))
                {
                    continue;
                }
                float rr = r + u.stats["size"] * 0.5f;
                if ((u.current_position - at).sqrMagnitude <= rr * rr)
                {
                    into.Add(u);
                }
            }
            return into.Count;
        }

        /// <summary>Enemies within 'width' of the segment a..b into 'into' (in other units' moves too when 'busy').</summary>
        public static int FoesOnLine(Actor by, Vector2 a, Vector2 b, float width, List<Actor> into, bool air = false, bool busy = false)
        {
            into.Clear();
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f)
            {
                return Foes(by, a, width, into, air, busy);
            }
            d /= len;
            _scan.Clear();
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 14f));
            List<Actor> tmp = _lineTmp;
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                Foes(by, p, Mathf.Min(len, 14f) * 0.5f + width + 2f, tmp, air, true);
                for (int i = 0; i < tmp.Count; i++)
                {
                    Actor u = tmp[i];
                    if (_scan.Contains(u))
                    {
                        continue;
                    }
                    _scan.Add(u);
                    Vector2 rel = u.current_position - a;
                    float along = Vector2.Dot(rel, d);
                    if (along < -0.3f || along > len + 0.3f)
                    {
                        continue;
                    }
                    float off = Mathf.Abs(rel.x * d.y - rel.y * d.x);
                    if (off <= width + u.stats["size"] * 0.5f && (busy || Hittable(u)))
                    {
                        into.Add(u);
                    }
                }
            }
            return into.Count;
        }

        private static readonly List<Actor> _lineTmp = new List<Actor>();

        /// <summary>
        /// Is 'v' free for a technique to take: in no move, in a light one (a flow strike), in one that
        /// has just finished, or in one of the engine's own reactions (the next blow of a combo)?
        /// </summary>
        public static bool Free(Actor v)
        {
            if (v == null || !Moves.TryGet(v, out Move m))
            {
                return true;
            }
            return m.Interruptible || m.Finished || Victims.Ours(v);
        }

        /// <summary>
        /// Can a technique's blow reach 'v' (it may be busy with a move of its own: a wind-up, a beam, a
        /// dash; never one another technique is holding in its hands, unless it is one of the engine's
        /// own reactions)? Intangible units are no foes at all (Combat.IsFoe).
        /// </summary>
        public static bool Hittable(Actor v)
        {
            if (v == null || !Moves.TryGet(v, out Move m))
            {
                return true;
            }
            return !(m is Puppet) || m.Finished || Victims.Ours(v);
        }

        /// <summary>
        /// Is 'v' a fair target for a technique of 'a' right now: alive, hostile and touchable, allowed by
        /// the class limits ('cm': the damage share, 0.5 when punching above its class), not in another
        /// technique's hands (Hittable: busy with a move of its own is fine), not flying to its death,
        /// not in someone else's set piece (unless 'forced')?
        /// </summary>
        public static bool Fair(Actor a, Actor v, out float cm, bool forced = false)
        {
            cm = 1f;
            return FairFoe(a, v, forced) && Combat.Allowed(a, v, out cm);
        }

        /// <summary>
        /// Fair as one of the engine's blows sees it (an area blow, a beam, a shot's burst): the class
        /// limits never make a foe in the way untouchable, they make the blow glance. True for any fair
        /// foe; 'full' false (and 'cm' Glance) for one above the class limits of 'a' (in move 'm'): it
        /// takes a little and only rocks (a Stagger), it is never thrown, launched, caught or pulled.
        /// </summary>
        public static bool FairBlow(Actor a, Actor v, Move m, out float cm, out bool full, bool forced = false)
        {
            cm = 1f;
            full = false;
            if (!FairFoe(a, v, forced))
            {
                return false;
            }
            full = ClassShare(a, v, m, out cm);
            return true;
        }

        /// <summary>Fair without the class limits: alive, hostile, touchable, not held by another, not doomed, not in a set piece (unless 'forced').</summary>
        private static bool FairFoe(Actor a, Actor v, bool forced)
        {
            if (a == null || v == null || !Combat.Living(v) || !Combat.IsFoe(a, v) || MeteorSlam.IsDoomed(v))
            {
                return false;
            }
            if (!Hittable(v))
            {
                return false;
            }
            return forced || !Director.InScene(v);
        }

        /// <summary>
        /// Are 'a' and 'v' still enemies (their kingdoms at war, or wild)? A move picks its target as a
        /// foe, but a city taken, a peace or a rebellion can make it a friend in the middle of the move:
        /// from then on no blow of the move lands on it. Unlike Combat.IsFoe it says nothing about being
        /// touchable (a target mid-blink is still an enemy: it evades instead).
        /// </summary>
        public static bool Hostile(Actor a, Actor v)
        {
            return a != null && v != null && a.kingdom != null && v.kingdom != null && a.areFoes(v);
        }

        /// <summary>
        /// The share of an engine blow's damage a unit takes when the class limits forbid it to its
        /// attacker (a Novice's counter on a demon, a ki bullet that meets a dragon): a glancing blow
        /// that only rocks it, never moves it (the older techniques' rule, CounterMove's 0.35).
        /// </summary>
        public const float Glance = 0.35f;

        /// <summary>
        /// The class share of an engine blow of 'a' (in move 'm') on 'v': true and 1 (0.5 punching above
        /// its class, Combat.Allowed's Valiant rule) when the class limits allow it; false and Glance when
        /// they don't (the blow only glances: its reaction is a Stagger). A team technique's blows keep 1:
        /// they carry the team's own share already (Syncs.Share). A forced move (a god power's order, a
        /// dev demo) goes past the limits at half damage, as a forced hunter's plan does.
        /// One rule for every blow of the engine (strikes, flurries, grabs, slams, shots and their
        /// bursts, beams and their clashes, areas, hazards): a foe above the limits is never hit at full
        /// and never moved, but never passed over either. Planning (Fair, PickMelee, Retarget) still only
        /// takes foes the limits allow.
        /// </summary>
        public static bool ClassShare(Actor a, Actor v, Move m, out float cm)
        {
            if (Combat.Allowed(a, v, out cm))
            {
                return true;
            }
            if (m != null && m.Tech != null && m.Tech.Team)
            {
                cm = 1f;
                return true;
            }
            if (m is ChoreoMove c && c.Forced)
            {
                cm = 0.5f;
                return true;
            }
            cm = Glance;
            return false;
        }
    }

    /// <summary>
    /// Afterimage trails behind units the engine launched with the game's own physics (it flies on
    /// its own, so no move can leave its ghosts): a fading silhouette every few hundredths of a
    /// second while it is airborne.
    /// </summary>
    internal static class Trails
    {
        private sealed class T
        {
            public Actor A;
            public long Id;
            public float Left, Next;
            public Color C0, C1;
        }

        private static readonly List<T> _list = new List<T>();

        public static void Follow(Actor v, float seconds, Color? color = null)
        {
            if (v == null || _list.Count > 64)
            {
                return;
            }
            Color c = color ?? new Color(1f, 1f, 1f, 0.55f);
            _list.Add(new T { A = v, Id = v.Sid(), Left = seconds, C0 = c, C1 = Fx.WithA(c, 0f) });
        }

        public static void Tick(float dt)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                T t = _list[i];
                t.Left -= dt;
                Actor a = t.A;
                if (t.Left <= 0f || a == null || a.Sid() != t.Id || !a.isAlive() || Moves.Busy(a))
                {
                    _list.RemoveAt(i);
                    continue;
                }
                t.Next -= dt;
                if (t.Next > 0f || a.position_height < 0.15f || !a.is_visible)
                {
                    continue;
                }
                t.Next = 0.035f;
                Ghost(a, 0.14f, t.C0, t.C1);
            }
        }

        /// <summary>A fading silhouette of a free unit as the game draws it now.</summary>
        public static Particle Ghost(Actor a, float life, Color c0, Color c1)
        {
            Sprite s = Fx.UnitSprite(a);
            if (s == null)
            {
                return null;
            }
            Vector3 cp = a.cur_transform_position;
            Particle p = Fx.Spawn(s, new Vector2(cp.x, cp.y), 1f, life, FxLayer.Behind, 0, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = a.current_rotation;
            p.SX = a.current_scale.x;
            p.SY = a.current_scale.y;
            p.Color(c0, c1);
            return p;
        }

        public static void Clear()
        {
            _list.Clear();
        }
    }
}
