using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// How a performing unit is drawn this frame. Everything a move animates
    /// goes through these few numbers, which map onto the fields the game's
    /// renderer reads (ActorManager.precalculateRenderDataParallel):
    /// position = current_position + move_jump_offset + (0, position_height),
    /// rotation = target_angle, scale = current_scale.
    /// </summary>
    internal struct Pose
    {
        /// <summary>The point on the ground under the unit (its shadow).</summary>
        public Vector2 Ground;
        /// <summary>World units above the ground.</summary>
        public float Height;
        /// <summary>Degrees; positive leans the unit forward, whichever way it faces. Any value (360 = one flip).</summary>
        public float Rot;
        /// <summary>Squash and stretch on top of the unit's own scale.</summary>
        public float SX, SY;
        public bool FaceRight;
        /// <summary>Extra offset in sprite pixels (tremble).</summary>
        public Vector2 Nudge;
        /// <summary>Height of the rotation pivot in sprite pixels: units turn around their middle, not their feet.</summary>
        public float PivotUp;
        public bool HideShadow;
        /// <summary>Not drawn at all this frame (a unit that has "vanished" mid-blink).</summary>
        public bool Hidden;

        public static Pose Of(Actor a)
        {
            return new Pose
            {
                Ground = a.current_position,
                Height = Mathf.Max(0f, a.position_height),
                SX = 1f,
                SY = 1f,
                FaceRight = a.flip,
                PivotUp = 3.5f
            };
        }
    }

    /// <summary>A move a unit is performing. The unit's AI and physics stand still until it ends.</summary>
    internal abstract class Move
    {
        public readonly Actor A;
        public readonly long Id;
        public float T;
        public bool Finished;
        /// <summary>When it finishes, is the unit on the ground? (A puppet let go in mid-air falls.)</summary>
        public bool LandOnFinish = true;
        public Pose Pose;
        internal bool ShadowWas;
        internal bool Ended;

        /// <summary>The game's big favourite star is hidden over this unit while the move plays (cinematics always).</summary>
        public bool HideStar;
        /// <summary>Its Tick or Frame threw: it is ended as aborted.</summary>
        internal bool Threw;
        internal float HoverWas;
        internal bool ScaleWas;

        /// <summary>The technique this move belongs to and the level it is played at (1..3), if any.</summary>
        public Technique Tech;
        public int Level = 2;

        /// <summary>The chain this move leaves when it ends well (set it during the move), and on whom.</summary>
        public Link EndLink;
        public Actor EndTarget;

        protected Move(Actor a)
        {
            A = a;
            Id = a.Sid();
            Pose = Pose.Of(a);
            Sprite s = Fx.UnitSprite(a);
            Pose.PivotUp = s != null ? Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) : 3.5f;
        }

        /// <summary>Can't be knocked out of the move by weapons while airborne.</summary>
        public virtual bool SuperArmor => true;

        /// <summary>Nothing can touch the unit during this move (it is a blur, or somewhere else).</summary>
        public virtual bool Intangible => false;

        /// <summary>Keeps playing while a cinematic holds the rest of the world still.</summary>
        public virtual bool Cinematic => false;

        /// <summary>
        /// A light move (a flow strike, a blur dash) that any technique may cut short: other units'
        /// techniques may target the unit, and starting a technique or a puppet on it ends this one.
        /// </summary>
        public virtual bool Interruptible => false;

        /// <summary>
        /// What kind of attack this is right now (others read it and answer it): its technique's by
        /// default, showing Charge only while it is really gathering power (Charging).
        /// </summary>
        public virtual Form Forms => Tech != null ? (Charging ? Tech.FormsAt(Level) : Tech.FormsAt(Level) & ~Form.Charge) : Form.None;

        /// <summary>
        /// Gathering power (a wind-up, a charging beam): a fast blow now breaks it and sends the unit
        /// flying. While it charges the unit is open (Moves.Locked is false): other techniques may
        /// target it, and one that takes hold of it (a grab, a throw) breaks the charge first.
        /// </summary>
        public virtual bool Charging => false;

        /// <summary>
        /// In its recovery (the blow is spent, the body settles): an answer to a counter may cut it
        /// short (counter the counter). A move whose own contact was parried or evaded counts as
        /// recovering for a moment anyway (Reads.Recovering).
        /// </summary>
        public virtual bool Recovering => false;

        /// <summary>
        /// How deep in a chain of answers this move is: 0 an attack, 1 a counter to it, 2 a counter to
        /// that counter... (set by Reactions when a reactive technique answers). Each layer is harder
        /// to answer again; a God reads three deep, a Novice one.
        /// </summary>
        public int CounterDepth;

        /// <summary>Reads saw it charging (the moment it began to gather was announced once).</summary>
        internal bool ChargeSeen;

        /// <summary>
        /// Whom this move threatens right now, if one unit (others read it: the one it is aimed at
        /// sees it coming). Null: Reads uses the target of the technique's plan.
        /// </summary>
        public virtual Actor Aim => null;

        /// <summary>World units per sprite pixel of this unit: its effects are drawn at the same resolution.</summary>
        public float Px => Mathf.Clamp(A.actor_scale, 0.04f, 0.4f);

        /// <summary>The unit's size relative to a human (1 for a human, 2-ish for a bear, 0.6 tiny).</summary>
        public float K => Mathf.Sqrt(Mathf.Clamp(A.actor_scale / 0.1f, 0.6f, 4f));

        /// <summary>Advances the move (main thread, once per frame, dt = move time).</summary>
        public abstract void Tick(float dt);

        /// <summary>After the game's simulation and right before it draws: effects glued to the unit.</summary>
        public virtual void Frame()
        {
        }

        /// <summary>The unit died, was picked up, or the world was cleared: drop whatever the move holds.</summary>
        public virtual void Abort()
        {
        }

        /// <summary>Called once when the move ends for any reason (after Tick or Abort).</summary>
        public virtual void OnEnd(bool aborted)
        {
        }

        /// <summary>Where the unit's sprite is drawn this frame (its feet).</summary>
        public Vector2 DrawPos()
        {
            Vector2 off = Moves.PivotOffset(Pose, A.actor_scale, A.asset.can_flip);
            return new Vector2(Pose.Ground.x + off.x, Pose.Ground.y + off.y + Pose.Height + HoverWas);
        }

        /// <summary>Middle of the unit's body this frame.</summary>
        public Vector2 BodyPos()
        {
            return new Vector2(Pose.Ground.x, Pose.Ground.y + Pose.Height + Pose.PivotUp * A.actor_scale * Pose.SY);
        }

        /// <summary>Top of the unit's head this frame (glints, marks).</summary>
        public Vector2 HeadPos()
        {
            return new Vector2(Pose.Ground.x, Pose.Ground.y + Pose.Height + Pose.PivotUp * 2.2f * A.actor_scale * Pose.SY);
        }

        /// <summary>Is the actor still the same unit this move started on (pooled actors get reused)?</summary>
        public bool Valid => A != null && A.Sid() == Id && A.isAlive();
    }

    /// <summary>
    /// A unit that is being moved by another unit's move (thrown, juggled). Its pose is
    /// set by that move every tick; it ends when that move lets go.
    /// </summary>
    internal sealed class Puppet : Move
    {
        private Particle _flash;
        private float _flashLeft;
        private Color _flashColor = Color.white;
        public readonly Move Owner;

        public Puppet(Actor a, bool faceRight, Move owner = null) : base(a)
        {
            Pose.FaceRight = faceRight;
            Owner = owner;
        }

        public override bool SuperArmor => false;

        public override bool Cinematic => Owner != null && Owner.Cinematic;

        /// <summary>Hands the unit back to the game. In mid-air it falls on its own.</summary>
        public void Release(bool onGround)
        {
            LandOnFinish = onGround;
            Finished = true;
            _flash?.Kill();
            _flash = null;
        }

        /// <summary>A white (or coloured) hit flash over the whole unit.</summary>
        public void Flash(float seconds, Color? color = null)
        {
            if (Finished)
            {
                return;
            }
            _flashColor = color ?? Color.white;
            _flashLeft = Mathf.Max(_flashLeft, seconds);
            if ((_flash == null || !_flash.Alive) && Fx.HasSilhouette)
            {
                Sprite s = Fx.UnitSprite(A);
                _flash = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 1, true) : null;
                if (_flash != null)
                {
                    _flash.Held = true;
                    _flash.UseEuler = true;
                    _flash.Cine = Cinematic;
                    Fx.Glue(_flash, this, new Color(1f, 1f, 1f, 0f));
                }
            }
        }

        public override void Tick(float dt)
        {
            _flashLeft -= dt;
        }

        public override void Frame()
        {
            if (_flash == null || !_flash.Alive)
            {
                return;
            }
            Fx.Glue(_flash, this, Fx.WithA(_flashColor, Mathf.Clamp01(_flashLeft / 0.05f)));
            if (_flashLeft <= -0.2f)
            {
                _flash.Kill();
                _flash = null;
            }
        }

        public override void Abort()
        {
            _flash?.Kill();
            _flash = null;
        }
    }

    internal static class Moves
    {
        private static readonly ConcurrentDictionary<long, Move> _byId = new ConcurrentDictionary<long, Move>();
        private static readonly List<Move> _list = new List<Move>();
        private static volatile int _active;

        public static int Count => _list.Count;

        /// <summary>Units held still (dev demos: everyone waits for the cue).</summary>
        internal static readonly ConcurrentDictionary<long, bool> Held = new ConcurrentDictionary<long, bool>();
        internal static volatile bool HeldAny;

        public static void Hold(Actor a)
        {
            Held[a.Sid()] = true;
            HeldAny = true;
        }

        public static void ReleaseHeld()
        {
            Held.Clear();
            HeldAny = false;
        }

        /// <summary>Is this unit free and able to start a move (on its feet, on land, not busy, an ordinary unit)?</summary>
        public static bool CanPerform(Actor a)
        {
            // (a stun only counts as unconscious once the game recalculates the unit's stats, and a unit a
            // blow just killed stays alive until the game's death check: both are asked for directly)
            if (a == null || !a.isAlive() || !a.hasHealth() || Committed(a) || !TryGet(a, out _) && a.position_height > 0f || a.under_forces
                || a.isInLiquid() && !Wades(a) || a.isInsideSomething() || a.isInMagnet() || a.isFlying() || a.is_immovable
                || a.is_unconscious || a.hasStatusStunned())
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

        /// <summary>
        /// CanPerform, but 'evenCommitted' lets a unit in a move of its own through (an answer to a
        /// counter cuts its recovering move short) and 'evenStunned' one in a fresh stun (it shakes
        /// it off). The caller must free it first (Reactions.Free).
        /// </summary>
        public static bool CanPerform(Actor a, bool evenCommitted, bool evenStunned)
        {
            if (a == null || !a.isAlive() || !a.hasHealth())
            {
                return false;
            }
            bool moving = TryGet(a, out Move m);
            if (moving && !m.Interruptible && (!evenCommitted || m is Puppet || m.Charging))
            {
                return false;
            }
            float h = moving ? m.Pose.Height : a.position_height;
            // a committed move cut short (Moves.Cancel) only sets its unit down from a hair off the
            // ground; a light one gives way to the answer from a little higher (Moves.Start)
            float lim = !moving ? 0f : m.Interruptible ? 0.3f : 0.05f;
            if (h > lim || a.under_forces || a.isInLiquid() && !Wades(a) || a.isInsideSomething() || a.isInMagnet() || a.isFlying()
                || a.is_immovable || (!evenStunned && (a.is_unconscious || a.hasStatusStunned())))
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

        /// <summary>A Master or higher in shallow water still fights (never deep water, never lava).</summary>
        public static bool Wades(Actor a)
        {
            TierInfo i = Tiers.Info(Tiers.Of(a));
            WorldTile t = a.current_tile;
            return i != null && i.Flow && t != null && t.Type != null && !t.Type.lava && t.Type.id != null && t.Type.id.Contains("shallow");
        }

        public static bool Busy(Actor a)
        {
            return a != null && _active > 0 && _byId.ContainsKey(a.Sid());
        }

        /// <summary>Thread-safe and lock-free: also asked from the game's worker threads.</summary>
        public static bool Busy(long id)
        {
            return _active > 0 && _byId.ContainsKey(id);
        }

        public static bool TryGet(Actor a, out Move m)
        {
            m = null;
            return a != null && _active > 0 && _byId.TryGetValue(a.Sid(), out m);
        }

        /// <summary>
        /// In a move that can't be cut short (a technique, a puppet): techniques must leave it alone.
        /// A unit gathering power (Move.Charging) is not locked: it is open to attack, and whatever
        /// takes hold of it breaks the charge (Moves.Start, Interrupt).
        /// </summary>
        public static bool Locked(Actor a)
        {
            return TryGet(a, out Move m) && !m.Interruptible && !m.Charging;
        }

        /// <summary>In any move that isn't a light one (a technique, a puppet, a wind-up): it can't start anything of its own.</summary>
        public static bool Committed(Actor a)
        {
            return TryGet(a, out Move m) && !m.Interruptible;
        }

        /// <summary>Gathering power right now (Move.Charging): a fast blow breaks it.</summary>
        public static bool Charging(Actor a)
        {
            return TryGet(a, out Move m) && m.Charging;
        }

        /// <summary>
        /// Ends a light (interruptible) move the unit is in, so a technique can take it over. A
        /// charging move is broken too (its gathered power disperses, Interrupts.Disperse).
        /// </summary>
        public static void Interrupt(Actor a)
        {
            if (!TryGet(a, out Move m))
            {
                return;
            }
            if (m.Interruptible)
            {
                SafeAbort(m);
                m.Finished = true;
                m.LandOnFinish = true;
                End(m, true);
            }
            else if (m.Charging)
            {
                Interrupts.Disperse(m, null);
            }
        }

        /// <summary>
        /// Ends a move right now as aborted (it lets go of everything it holds; the unit lands where it
        /// is). For breaking a charge, or a counter to a counter cutting its own recovery short.
        /// </summary>
        public static bool Cancel(Move m)
        {
            if (m == null || m.Ended)
            {
                return false;
            }
            bool grounded = m.Pose.Height <= 0.05f;
            SafeAbort(m);
            m.Finished = true;
            m.LandOnFinish = grounded;
            End(m, true);
            Actor a = m.A;
            if (grounded && a != null && a.Sid() == m.Id && a.isAlive())
            {
                // it was on its feet: it stays there (an aborted end leaves the drawn lift of a tilted pose)
                Vector2 g = m.Pose.Ground;
                Toolbox.clampToMap(ref g);
                a.current_position = g;
                a.position_height = 0f;
                a.dirty_current_tile = true;
            }
            return true;
        }

        /// <summary>
        /// Ends a move that has already finished (Finished set, as Duelist.Let does) right now instead of
        /// at the next Moves.Update, cleanly, exactly as that would: its unit is free for a new move at
        /// once (until then the finished move still holds it: Committed, so CanPerform says no).
        /// </summary>
        public static void EndFinished(Move m)
        {
            if (m != null && m.Finished && !m.Ended)
            {
                End(m, m.Threw);
            }
        }

        /// <summary>Nothing can touch this unit right now (it is mid-blink).</summary>
        public static bool Intangible(Actor a)
        {
            return TryGet(a, out Move m) && m.Intangible;
        }

        public static bool Start(Move m)
        {
            if (m.A == null)
            {
                return false;
            }
            // nothing less than a Primordial can take hold of one: no knock-back, no throw, no juggle
            if (m is Puppet pp && Primordials.Any && Primordials.Is(m.A) && (pp.Owner == null || !Primordials.Is(pp.Owner.A)))
            {
                return false;
            }
            // a technique or a puppet takes over a unit in a light move (a flow strike) at once; one
            // that takes hold of a unit gathering power breaks its charge
            if (!m.Interruptible && _byId.TryGetValue(m.Id, out Move cur) && cur != m)
            {
                if (cur.Interruptible)
                {
                    Interrupt(m.A);
                }
                else if (cur.Charging)
                {
                    Interrupts.Disperse(cur, m is Puppet p && p.Owner != null ? p.Owner.A : null);
                }
            }
            if (!_byId.TryAdd(m.Id, m))
            {
                return false;
            }
            _active = _byId.Count;
            _list.Add(m);
            Actor a = m.A;
            // (a favourite's big star is hidden while it plays a cinematic: CinematicStarPatch)
            m.ShadowWas = a.show_shadow;
            m.HoverWas = a.isHovering() ? a.move_jump_offset.y : 0f;
            a.stopMovement();
            a.under_forces = false;
            a.velocity = Vector3.zero;
            a.skipUpdates();
            try
            {
                Hooks.MoveStarted?.Invoke(m);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] move start hook: " + e);
            }
            return true;
        }

        private static Move[] _tick = new Move[64];

        public static void Update(float dt, float cineDt)
        {
            // a tick may end other moves (a launch interrupts its victim's flow strike) or start new
            // ones: walk a snapshot, so every move is ticked exactly once and an ended one not at all
            int n = _list.Count;
            if (_tick.Length < n)
            {
                _tick = new Move[Mathf.NextPowerOfTwo(n)];
            }
            _list.CopyTo(_tick);
            for (int i = n - 1; i >= 0; i--)
            {
                Move m = _tick[i];
                _tick[i] = null;
                if (m == null || m.Ended)
                {
                    continue;
                }
                Actor a = m.A;
                bool valid = a != null && a.Sid() == m.Id && a.isAlive();
                // died, recycled, or picked up by the god hand: let go, but don't leave it squashed or upside down
                // (a move whose frame threw is broken too: it ends aborted)
                if (!valid || a.isInMagnet() || a.isInsideSomething() || m.Threw || ControllableUnit.isControllingUnit(a))
                {
                    SafeAbort(m);
                    m.Finished = true;
                    m.LandOnFinish = false;
                    End(m, true);
                    continue;
                }
                float step = m.Cinematic ? cineDt : dt;
                try
                {
                    if (step > 0f)
                    {
                        m.T += step;
                        m.Tick(step);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] move " + m.GetType().Name + ": " + e);
                    SafeAbort(m);
                    m.Finished = true;
                    m.LandOnFinish = false;
                    m.Threw = true;
                }
                if (!m.Finished && !m.Threw && step > 0f && (m.Tech != null || m is Duelist))
                {
                    // the technique's school shows on the fighter all through it (its aura, its trail)
                    try
                    {
                        SchoolStyle.Tick(m, step);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Sakuga] school style: " + e);
                    }
                }
                if (m.Finished)
                {
                    End(m, m.Threw);   // a move that threw is aborted, never a clean finish
                }
            }
        }

        /// <summary>Right before the game computes what to draw: performers take their pose.</summary>
        public static void ApplyAll()
        {
            for (int i = 0; i < _list.Count; i++)
            {
                Move m = _list[i];
                Actor a = m.A;
                if (a == null || a.Sid() != m.Id || a.isRekt() || a.isInMagnet() || a.isInsideSomething())
                {
                    continue;
                }
                if (m.Threw)
                {
                    continue;   // ends (aborted) on the next update
                }
                try
                {
                    Apply(a, m);
                    m.Frame();
                }
                catch (Exception e)
                {
                    SakugaMod.Report("frame " + m.GetType().Name, e);
                    m.Threw = true;
                }
            }
        }

        public static void Clear()
        {
            foreach (Move m in _list.ToArray())
            {
                SafeAbort(m);
                Restore(m, true);
            }
            _list.Clear();
            _byId.Clear();
            _active = 0;
            ReleaseHeld();
        }

        /// <summary>All moves right now (read only; for the AI and reactions).</summary>
        public static IReadOnlyList<Move> All => _list;

        /// <summary>
        /// Offset that keeps the body's middle in place when the sprite turns around its feet
        /// (unit sprites pivot at the bottom), plus the tremble.
        /// </summary>
        public static Vector2 PivotOffset(Pose p, float scale, bool canFlip = true)
        {
            // y = 180 mirrors the sprite, which turns its z rotation the other way on screen
            float phi = (p.FaceRight && canFlip ? -p.Rot : p.Rot) * Mathf.Deg2Rad;
            float hc = p.PivotUp * scale * p.SY;
            return new Vector2(Mathf.Sin(phi) * hc + p.Nudge.x * scale, hc * (1f - Mathf.Cos(phi)) + p.Nudge.y * scale);
        }

        private static void Apply(Actor a, Move m)
        {
            Pose p = m.Pose;
            float s = a.actor_scale;
            Vector2 ground = p.Ground;
            Toolbox.clampToMap(ref ground);
            a.current_position = ground;
            a.dirty_current_tile = true;
            a.position_height = p.Height;
            a.under_forces = false;
            a.velocity = Vector3.zero;
            float rot = Mathf.DeltaAngle(0f, p.Rot);
            Pose q = p;
            q.Rot = rot;
            Vector2 off = PivotOffset(q, s, a.asset.can_flip);
            // the lift of a tilted pose rides in the height (drawn the same), so the game doesn't take
            // a raised sprite for a walking one and run its legs in place
            float lift = Mathf.Max(0f, off.y);
            off.y = Mathf.Min(0f, off.y) + m.HoverWas;
            a.position_height = p.Height + lift;
            a.move_jump_offset = off;
            if (m.HoverWas <= 0f)
            {
                a.timer_jump_animation = 0f;
            }
            float flipAngle = 0f;
            if (a.asset.can_flip)
            {
                a.flip = p.FaceRight;
                a.flip_angle = p.FaceRight ? 180f : 0f;
                flipAngle = a.flip_angle;
            }
            a.target_angle = new Vector3(0f, flipAngle, rot);
            a.current_rotation = a.target_angle;
            a.current_scale = p.Hidden ? Vector3.zero : new Vector3(s * p.SX, s * p.SY, 1f);
            // the game slides its own shadow sideways with the rotation: hide it while upside down
            a.show_shadow = m.ShadowWas && !p.HideShadow && !p.Hidden && Mathf.Abs(rot) <= 45f;
        }

        /// <summary>Hands the unit back to the game. 'landed': the move put it down, so it is on the ground too.</summary>
        private static void Restore(Move m, bool landed)
        {
            Actor a = m.A;
            if (a == null || a.Sid() != m.Id)
            {
                return;   // the pooled actor already belongs to someone else
            }
            bool alive = a.isAlive();
            a.move_jump_offset = new Vector2(0f, m.HoverWas);
            if (landed && alive)
            {
                Vector2 g = m.Pose.Ground;
                Toolbox.clampToMap(ref g);
                a.current_position = g;
                a.position_height = 0f;
                a.stopMovement();
            }
            if (!alive && a.position_height > 0.02f)
            {
                Tumble.Fall(a);
            }
            // a corpse, or a unit knocked out cold, keeps a gentle tilt (the game tips it over),
            // never upside down or stretched
            float z = alive && !a.is_unconscious ? 0f : Mathf.Clamp(Mathf.DeltaAngle(0f, a.target_angle.z), -89f, 89f);
            a.target_angle = new Vector3(0f, a.flip_angle, z);
            a.current_rotation = a.target_angle;
            a.current_scale = new Vector3(a.actor_scale, a.actor_scale, 1f);
            a.show_shadow = m.ShadowWas;
            a.dirty_current_tile = true;
        }

        private static void End(Move m, bool aborted)
        {
            if (m.Ended)
            {
                return;
            }
            m.Ended = true;
            SchoolStyle.End(m);
            int i = _list.IndexOf(m);
            if (i >= 0)
            {
                _list.RemoveAt(i);
            }
            if (_byId.TryGetValue(m.Id, out Move cur) && cur == m)
            {
                _byId.TryRemove(m.Id, out _);
            }
            _active = _byId.Count;
            try
            {
                Restore(m, !aborted && m.LandOnFinish);
            }
            catch (Exception e)
            {
                // (a failed restore must not skip the move's own ending: puppets, cinematics, counts)
                Debug.LogError("[Sakuga] restore: " + e);
            }
            try
            {
                m.OnEnd(aborted);
                Brain.MoveEnded(m, aborted);
                Hooks.MoveEnded?.Invoke(m, aborted);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] end: " + e);
            }
        }

        private static void SafeAbort(Move m)
        {
            try
            {
                m.Abort();
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] abort: " + e);
            }
        }
    }

    /// <summary>
    /// Units knocked into the air by a move tumble head over heels until they
    /// land, and kick up a little dust when they do. The flight itself is the
    /// game's own throw physics (Actor.addForce).
    /// </summary>
    internal static class Tumble
    {
        private sealed class Entry
        {
            public Actor A;
            public long Id;
            public float Spin;
            public float Age;
            public bool Airborne;
        }

        private static readonly List<Entry> _list = new List<Entry>();

        public static int Count => _list.Count;

        public static void Add(Actor a, float spin)
        {
            if (a == null || a.isRekt() || Moves.Busy(a))
            {
                return;
            }
            long id = a.Sid();
            foreach (Entry e in _list)
            {
                if (e.A == a && e.Id == id)
                {
                    e.Spin = spin;
                    e.Age = 0f;
                    return;
                }
            }
            _list.Add(new Entry { A = a, Id = id, Spin = spin });
        }

        public static void Apply()
        {
            float dt = Clock.Dt;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                Entry e = _list[i];
                Actor a = e.A;
                if (a == null || a.isRekt() || a.Sid() != e.Id || a.isInMagnet() || Moves.Busy(a))
                {
                    if (a != null && !a.isRekt() && a.Sid() == e.Id && !Moves.Busy(a))
                    {
                        Settle(a);
                    }
                    _list.RemoveAt(i);
                    continue;
                }
                e.Age += dt;
                if (a.position_height > 0.02f)
                {
                    e.Airborne = true;
                    float z = a.target_angle.z + e.Spin * dt;
                    a.target_angle.z = Mathf.Repeat(z + 180f, 360f) - 180f;
                }
                else if (e.Airborne || e.Age > 0.4f)
                {
                    if (e.Airborne && a.is_visible)
                    {
                        Fx.LandingPuff(a.current_position, Mathf.Clamp(a.actor_scale, 0.04f, 0.4f), a.current_tile);
                    }
                    Settle(a);
                    _list.RemoveAt(i);
                    continue;
                }
                if (e.Age > 8f)
                {
                    Settle(a);
                    _list.RemoveAt(i);
                }
            }
        }

        /// <summary>Upright again (the living), so the game lets it attack.</summary>
        private static void Settle(Actor a)
        {
            if (a.isAlive())
            {
                a.target_angle = new Vector3(0f, a.flip_angle, 0f);
                a.current_rotation = a.target_angle;
            }
        }

        // ------------------------------------------------------- falling bodies --

        private sealed class Body
        {
            public Actor A;
            public long Id;
            public float VZ;
            public float Spin;
        }

        private static readonly List<Body> _bodies = new List<Body>();

        /// <summary>A body let go in mid-air by a move: it drops to the ground (the game doesn't move corpses).</summary>
        public static void Fall(Actor a)
        {
            if (a == null || a.data == null)
            {
                return;
            }
            long id = a.Sid();
            foreach (Body b in _bodies)
            {
                if (b.A == a && b.Id == id)
                {
                    return;
                }
            }
            _bodies.Add(new Body { A = a, Id = id, VZ = 0f, Spin = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * 220f });
        }

        /// <summary>Called with the poses, every frame.</summary>
        public static void ApplyBodies()
        {
            float dt = Clock.Dt;
            for (int i = _bodies.Count - 1; i >= 0; i--)
            {
                Body b = _bodies[i];
                Actor a = b.A;
                if (a == null || a.data == null || a.Sid() != b.Id || a.isAlive() || a.isInMagnet())
                {
                    _bodies.RemoveAt(i);
                    continue;
                }
                if (dt <= 0f)
                {
                    continue;
                }
                b.VZ += 14f * dt;
                a.position_height = Mathf.Max(0f, a.position_height - b.VZ * dt);
                float z = Mathf.Clamp(Mathf.DeltaAngle(0f, a.target_angle.z + b.Spin * dt), -89f, 89f);
                a.target_angle.z = z;
                a.current_rotation.z = z;
                if (a.position_height <= 0f)
                {
                    if (a.is_visible)
                    {
                        Fx.LandingPuff(a.current_position, Mathf.Clamp(a.actor_scale, 0.04f, 0.4f), a.current_tile);
                    }
                    _bodies.RemoveAt(i);
                }
            }
        }

        public static int Bodies => _bodies.Count;

        public static void Clear()
        {
            _list.Clear();
            _bodies.Clear();
        }
    }

    // ------------------------------------------------------------------ patches --

    /// <summary>A performing unit's brain, movement and tile effects wait until the move ends.</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.u4_deadCheck))]
    internal static class PerformerSkipPatch
    {
        private static void Postfix(Actor __instance)
        {
            long id = __instance.Sid();
            if (Moves.TryGet(__instance, out Move move) && move is NinefoldTribulation)
            {
                // Keep native statuses, regeneration and passive traits ticking during the trial.
                __instance.skipBehaviour();
                return;
            }
            if (Moves.Busy(id) || (Moves.HeldAny && Moves.Held.ContainsKey(id)))
            {
                __instance.skipUpdates();
            }
        }
    }

    /// <summary>A performer flies on its move, not on the game's gravity.</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.updateFall))]
    internal static class PerformerFallPatch
    {
        private static bool Prefix(Actor __instance)
        {
            return !Moves.Busy(__instance.Sid());
        }
    }

    /// <summary>
    /// Super armour: a unit in the air during a move can't be knocked out of it by
    /// weapons (fire, lightning, divine powers still hurt). A unit mid-blink can't be touched at all.
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.getHit))]
    internal static class PerformerArmourPatch
    {
        private static bool Prefix(Actor __instance, float pDamage, AttackType pAttackType, BaseSimObject pAttacker)
        {
            try
            {
                RealmControl.RecordHit(__instance, pAttacker);
                if (Moves.TryGet(__instance, out Move m))
                {
                    if (m.Intangible && __instance != Combat.Piercing
                        && (pAttackType == AttackType.Weapon || pAttackType == AttackType.Other || pAttackType == AttackType.Explosion))
                    {
                        return false;
                    }
                    // (a technique's own blow, Combat.Hit, lands whatever its attack type)
                    if (pAttackType == AttackType.Weapon && m.SuperArmor && m.Pose.Height > 0.05f && __instance != Combat.Hitting)
                    {
                        return false;
                    }
                }
                var handlers = Hooks.BeforeDamage;
                for (int i = 0; i < handlers.Count; i++)
                {
                    if (handlers[i](__instance, pDamage, pAttackType, pAttacker))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] armour: " + e);
                return true;
            }
        }
    }

    /// <summary>The last moment before the game works out what to draw: performers take their pose.</summary>
    [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.calculateVisibleActors))]
    internal static class PoseBeforeRenderPatch
    {
        private static void Prefix()
        {
            try
            {
                if (Moves.Count > 0)
                {
                    Moves.ApplyAll();
                }
                if (Tumble.Count > 0)
                {
                    Tumble.Apply();
                }
                if (Tumble.Bodies > 0)
                {
                    Tumble.ApplyBodies();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] pose: " + e);
            }
        }
    }

    /// <summary>
    /// The game's big favourite star would cover the hero for a whole cinematic: a unit playing a
    /// cinematic move is left out of this frame's starred units (its saved favourite flag is untouched).
    /// </summary>
    [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.calculateVisibleActors))]
    internal static class CinematicStarPatch
    {
        private static void Postfix(ActorManager __instance)
        {
            try
            {
                if (Moves.Count == 0)
                {
                    return;
                }
                ActorVisibleDataArray stars = __instance.visible_units_with_favorite;
                int n = 0;
                for (int i = 0; i < stars.count; i++)
                {
                    Actor a = stars.array[i];
                    if (!Moves.TryGet(a, out Move m) || !m.Cinematic && !m.HideStar)
                    {
                        stars.array[n++] = a;
                    }
                }
                stars.count = n;
            }
            catch (Exception e)
            {
                SakugaMod.Report("cinematic star", e);
            }
        }
    }

    /// <summary>Nothing from the old world may land in a new or loaded one.</summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    internal static class ClearWorldPatch
    {
        private static void Prefix()
        {
            SakugaMod.ClearAll();
        }
    }

    /// <summary>
    /// An ordinary attack (a sword swing, an arrow) is about to land: parries, deflections and
    /// other answers registered in Hooks.BeforeOrdinaryHit may turn it aside.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), "applyAttack")]
    internal static class OrdinaryHitPatch
    {
        private static bool Prefix(AttackData pData, BaseSimObject pTargetToCheck, ref AttackDataResult __result)
        {
            try
            {
                var handlers = Hooks.BeforeOrdinaryHit;
                if (pTargetToCheck == null || !pTargetToCheck.isActor() || !pTargetToCheck.isAlive())
                {
                    return true;
                }
                Actor t = pTargetToCheck.a;
                if (handlers.Count > 0)
                {
                    KiDeflect.Own++;   // (a parry's stagger on the attacker is the mod's own)
                    try
                    {
                        // every answer gets its turn first now and then (a fixed order is a rehearsed fight)
                        int n = handlers.Count;
                        int start = n > 1 ? UnityEngine.Random.Range(0, n) : 0;
                        for (int k = 0; k < n; k++)
                        {
                            int i = (start + k) % n;
                            if (handlers[i](pData, t))
                            {
                                __result = new AttackDataResult(ApplyAttackState.Deflect, t.data.id);
                                Reads.Ordinary(pData, t, true);
                                return false;
                            }
                        }
                    }
                    finally
                    {
                        KiDeflect.Own--;
                    }
                }
                // it lands: seen (read), and a quick enough blow breaks a wind-up
                Reads.Ordinary(pData, t, false);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] ordinary hit: " + e);
            }
            return true;
        }
    }
}
