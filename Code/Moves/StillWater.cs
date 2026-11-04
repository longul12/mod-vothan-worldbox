using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// STILL WATER. The answer to a technique: at the instant a single-target blow arrives the
    /// unit meets it, barely moving, and the blow is simply not there any more. Effortless.
    ///
    /// Beat sheet (game seconds at x1, from the attacker's contact frame):
    ///
    ///   0.00        the unit flashes white for a frame; a "ting" glint at the contact pixel, a
    ///               short bright bar where the blades meet, 6 sparks; the attacker flashes and is
    ///               thrown off its stroke (staggered 0.4 s, at III stunned 1.0 s). The unit gives
    ///               one pixel and a 4-degree lean, nothing more
    ///   (III)       a clash starburst, two thin "water ripple" ellipses (0.8 and 1.4 tiles) spread
    ///               over 0.4 s from its feet (and a last faint ring out to 2.6 tiles), and a 0.1 s
    ///               global hit-stop
    ///   (II, III)   0.15-0.30 the riposte: one crescent (0.8 tile, 160 degrees; at III a single
    ///               upward cut) x1.5 with a 1.5-tile knockback (III: x2.5, launched 2 tiles). If the
    ///               attacker is still caught in its own (broken) move the unit waits, perfectly
    ///               still, until it is free (at most 1 s) - a parry, a beat, the riposte
    ///   end         a calm glint at the hand
    ///
    /// Level I negates the blow and staggers; II adds the riposte; III negates the whole technique
    /// (the attacker's move can't hurt it again, even if it ignores the parry), stuns, and launches
    /// the attacker for a chain (Launched). I and II leave the attacker reeling (Down).
    ///
    /// Area attacks can't be parried. The chance is the defender's read (Reactions.ReadChance: the
    /// tier's reading of a fight, Expert .25 ... God .75, untiered .3, +-0.1 per tier of difference
    /// with the attacker, halved from behind, shaped by its persona and what it remembers of the
    /// attacker); against a counter (the attacker's own answer) it may parry from inside its own
    /// broken move: two masters trading answers.
    ///
    /// Still Water users also turn aside ordinary sword blows now and then (half the chance): a
    /// small ting, a few sparks, the attacker rocks back; once a second at most (any time at III).
    ///
    /// The riposte reaches: an attacker that reeled out of arm's reach is followed by one blink of
    /// a step (at most 0.9 tile), so the cut never lands from across the field.
    ///
    /// Forced (dev command, god power) there is no incoming technique to meet, so the attacker is
    /// driven through a blow first (0.2 s: it winds back, lunges in with a white swish) and the
    /// parry meets that - otherwise the demonstration is a flash at nothing.
    ///
    /// It flows straight into the defender's next action. Its pace follows the tier
    /// (TierInfo.Linger): from Master the parry's beat is shorter (0.1 s), it waits at most 0.3 s
    /// for a broken attacker (then cuts it where it is; an attacker that has started a flow strike
    /// is cut out of it at once), and settles in about half the time; the move always ends on a
    /// chain: Down on an attacker reeling beside it, and after the III launch Launched (more often
    /// than not, if it knows a follow-up for an airborne enemy, Wingbreaker) or Down (the ground
    /// follow-ups take it as it lands).
    ///
    /// Power grows with the tier: a stronger defender throws the attacker further (a God half as
    /// far again) and stills a wider pond. The ground under the defender is never touched, but at
    /// III from a Grandmaster up the force of the blow it turned aside runs back out along the
    /// ground, under and past the attacker (a Grandmaster's a scorch, a God's a blasted furrow).
    /// </summary>
    internal sealed class StillWater : Move
    {
        private enum Phase
        {
            Incoming,
            Parry,
            Wait,
            Riposte,
            Settle
        }

        private const float TParryI = 0.34f;
        private const float TParry = 0.15f;
        private const float TRiposte = 0.15f;
        private const float RiposteAt = 0.05f;
        private const float TWaitMax = 1f;
        private const float TIncoming = 0.2f;
        private const string CasualTimer = "still_water_casual";

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Pale = new Color(0.85f, 0.88f, 0.95f, 1f);
        private static readonly Color Water = new Color(0.72f, 0.9f, 1f, 1f);

        private readonly Actor _atk;
        private readonly long _atkId;
        private readonly Move _incoming;
        private readonly Palette _pal;
        private readonly bool _armed;
        private readonly float _k;
        private readonly float _fwd;
        private Vector2 _home;

        // ---- the tier: the beats shrink from Master up (the parry flows into what comes next)
        private readonly Tier _tier;
        private readonly float _snap, _tParryI, _tParry, _tWaitMax, _tSettle;

        private Phase _phase = Phase.Parry;
        private float _pt, _waitT, _rotFrom;
        private bool _entered, _struck, _launched, _glinted, _clashed;
        private Particle _flash;
        private float _flashAge;

        // the riposte's step in
        private Vector2 _stepFrom, _stepTo;

        // forced: the attacker driven through its blow
        private Puppet _ap;
        private Vector2 _aFrom, _aTo;
        private bool _swung;

        /// <summary>Technique parries / casual parries since the game started (dev log).</summary>
        internal static int Parries, Casuals;

        private StillWater(Actor d, Actor atk, Move incoming, int level) : base(d)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _atk = atk;
            _atkId = atk.Sid();
            _incoming = incoming;
            _pal = Palette.For(d, Level);
            _armed = Armed(d);
            _k = K;
            _home = d.current_position;
            _tier = Tiers.Of(d);
            _snap = SingleKit.Snap(_tier);
            _tParryI = TParryI * _snap;
            _tParry = TParry * Mathf.Lerp(1f, _snap, 0.6f);
            _tWaitMax = SingleKit.Flow(_tier) ? 0.3f : TWaitMax;
            _tSettle = (Level >= 3 ? 0.24f : 0.16f) * _snap;
            float dx = AttackerBody().x - d.current_position.x;
            _fwd = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : (d.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
        }

        /// <summary>
        /// The defender 'd' meets the blow of 'atk' ('incoming' is its move, or null for a forced
        /// demonstration): starts the parry and plays the contact right now, on the attacker's frame.
        /// </summary>
        public static bool Begin(Actor d, Actor atk, Move incoming, int level, Technique tech)
        {
            if (d == null || atk == null || d == atk || !atk.isAlive() || atk.Sid() < 0 || !Moves.CanPerform(d))
            {
                return false;
            }
            StillWater m = new StillWater(d, atk, incoming, level) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            if (incoming != null || !m.DriveBlow())
            {
                m.Clash();
            }
            Parries++;
            return true;
        }

        /// <summary>
        /// Forced, with nothing incoming: the attacker is made to strike (a puppet for 0.2 s), so
        /// there is a blow to meet. False if it can't be driven (heavy, busy, too far): then the
        /// parry plays at once, as before.
        /// </summary>
        private bool DriveBlow()
        {
            if (!AtkOk() || _atk.is_unconscious || !HundredFists.Catchable(_atk, A))
            {
                return false;   // (a stunned attacker lying on the ground swings at nothing)
            }
            Vector2 ap = _atk.current_position;
            float d = Vector2.Distance(ap, _home);
            if (d > 3.5f)
            {
                return false;
            }
            Vector2 dir = d > 0.01f ? (_home - ap) / d : new Vector2(-_fwd, 0f);
            // a blade's length short of the defender: at 0.55 the lunge's forward lean put the
            // attacker's head inside the defender's sprite at the moment of the clash
            float stop = 0.72f * _k + _atk.stats["size"] * 0.3f;
            Vector2 to = d > stop ? ap + dir * (d - stop) : ap;
            if (!Combat.SafeSpot(to) || !Combat.ClearPath(ap, to))
            {
                to = ap;   // no way in: it swings from where it stands
            }
            bool faceRight = Mathf.Abs(dir.x) > 0.05f ? dir.x > 0f : _atk.flip;
            Puppet p = HundredFists.Take(_atk, faceRight, this);
            if (p == null)
            {
                return false;
            }
            _ap = p;
            _aFrom = ap;
            _aTo = to;
            _phase = Phase.Incoming;
            _pt = 0f;
            _entered = false;
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

        private static float PivotOf(Actor u)
        {
            Sprite s = Fx.UnitSprite(u);
            return s != null ? Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) : 3.5f;
        }

        /// <summary>Middle of a unit's body as drawn right now (its move's pose if it is performing).</summary>
        private static Vector2 BodyOf(Actor u)
        {
            if (Moves.TryGet(u, out Move m) && m.A == u)
            {
                return m.BodyPos();
            }
            return new Vector2(u.current_position.x, u.current_position.y + u.position_height + PivotOf(u) * u.actor_scale);
        }

        private Vector2 AttackerBody()
        {
            return BodyOf(_atk);
        }

        /// <summary>The same live attacker, not picked up by the god hand or gone inside a boat or building.</summary>
        private bool AtkOk()
        {
            return _atk != null && _atk.Sid() == _atkId && _atk.isAlive() && !_atk.isInMagnet() && !_atk.isInsideSomething();
        }

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("still water " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        // -------------------------------------------------------------- clash --

        /// <summary>The contact frame: ting, sparks, flashes, the attacker thrown off, the guard raised.</summary>
        private void Clash()
        {
            _clashed = true;
            float px = Px;
            Vector2 body = BodyPos();
            Vector2 to = AttackerBody() - body;
            float dist = to.magnitude;
            Vector2 n = dist > 0.001f ? to / dist : new Vector2(_fwd, 0f);
            Vector2 contact = body + n * Mathf.Min(dist * 0.5f, 2.5f * px);

            Combat.Stagger(_atk, Level >= 3 ? 1f : 0.4f);
            float window = Level <= 1 ? _tParryI + 0.15f : _tParry + TRiposte + 0.15f;
            AddGuard(A, _atk, _incoming, window, Level >= 3);
            Anim.Sound(_armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitGeneric", A.current_tile);
            if (Level >= 3)
            {
                Kit.Stop(contact, 0.1f);
                Redirect(n, px);
            }
            if (!Kit.Visible(contact))
            {
                return;
            }
            FlashSelf();
            Color spark = Level <= 1 ? Pale : _pal.Edge;
            Fx.Glint(contact, px, Level >= 3 ? 0.26f : 0.2f, Level <= 1 ? White : _pal.Core, Level >= 3 ? 1.3f : Level == 2 ? 1.1f : 1f);
            Kit.Sparks(contact, px, 6, 60f, 120f, spark);
            // where the blades meet: a short bright bar across the line of the blow
            Vector2 across = new Vector2(-n.y, n.x);
            Kit.Line(contact - across * 2f * px, contact + across * 2f * px, px, 0.08f, 1, FxLayer.Top, 25, White, Fx.WithA(spark, 0f));
            // the force goes straight back into the attacker
            FlashUnit(_atk, 0.06f, new Color(1f, 1f, 1f, 0.85f));
            if (Level >= 3)
            {
                // a starburst the size of a torso at the meeting point (at 0.32 it was 1.7 units
                // tall and buried the defender, who should be the calm centre of the frame)
                Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], contact, px * 0.15f * _k, 0.07f, FxLayer.Top, 31);
                if (burst != null)
                {
                    burst.Rot = Random.Range(0f, 360f);
                    burst.Sized(1f, 1.15f).Color(White, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                }
                Ripples(px);
            }
        }

        /// <summary>
        /// III, from a Grandmaster up: the force of the blow it met doesn't vanish, it is turned
        /// back the way it came and runs out along the ground from a tile in front of the defender,
        /// under and past the attacker (a Grandmaster's a scorch 2 tiles long, a God's a blasted
        /// furrow of 3). The pond itself, the ground under the defender, is left as it was.
        /// (Measured from the defender, not the attacker: a drawn blade is met before the attacker
        /// has crossed the ground, and it lands in front of the defender.)
        /// </summary>
        private void Redirect(Vector2 n, float px)
        {
            int force = SingleKit.Force(_tier, 0, 1);
            if (force <= 0 || !AtkOk())
            {
                return;
            }
            Vector2 dir = _atk.current_position - _home;
            dir = dir.sqrMagnitude > 0.0004f ? dir.normalized : (n.sqrMagnitude > 0.0001f ? n.normalized : new Vector2(_fwd, 0f));
            float lk = Mathf.Lerp(1f, _k, 0.5f);
            float from = 1f * lk, to = from + (_tier == Tier.God ? 3.2f : 2.2f) * lk;
            float r = SingleKit.Radius(_tier, 0.8f);
            for (float d = from; d <= to + 0.01f; d += 0.8f * lk)
            {
                Vector2 at = Combat.Clamp(_home + dir * d);
                SingleKit.Ruin(A, _home, at, r, force);
                WorldTile t = Combat.TileAt(at);
                if (t != null && Kit.Visible(at))
                {
                    Kit.Debris(at, px, t, 2, _k);
                }
            }
            Vector2 start = _home + dir * from;
            if (Kit.Visible(start))
            {
                Color c = Fx.WithA(Color.Lerp(Water, _pal.Edge, 0.5f), 0.8f);
                Kit.SpeedLines(start + new Vector2(0f, 3f * px), dir, px, 5, 4f * _k, 0.2f, c);
            }
            if (Dev.On) Dev.Trace("still water " + Id + " turns the blow aside: the ground scored " + from.ToString("F1") + "-" + to.ToString("F1") + " tiles out toward " + _atk.asset.id + " (force " + force + ")");
        }

        /// <summary>Two thin rings spreading over still water from its feet (0.8 and 1.4 tiles).</summary>
        private void Ripples(float px)
        {
            Color w = Color.Lerp(Water, _pal.Edge, 0.35f);
            // a stronger tier stills a wider pond (a God's last ring runs out to 4 tiles)
            float far = Mathf.Lerp(1f, SingleKit.Throw(_tier), 0.85f);
            Fx.Ring(Pose.Ground, px, 0.4f, 2f, 8f * _k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                Fx.WithA(White, 0.9f), Fx.WithA(w, 0.6f), Fx.WithA(w, 0f));
            Particle r2 = Fx.Ring(Pose.Ground, px, 0.4f, 3f, 14f * _k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                Fx.WithA(White, 0.8f), Fx.WithA(w, 0.5f), Fx.WithA(w, 0f));
            if (r2 != null)
            {
                r2.Delay = 0.06f;
            }
            // and the pond's last, faint ring far out
            Particle r3 = Fx.Ring(Pose.Ground, px, 0.45f * far, 6f, 26f * _k * far, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f,
                Fx.WithA(w, 0.35f), Fx.WithA(w, 0.18f), Fx.WithA(w, 0f));
            if (r3 != null)
            {
                r3.Delay = 0.14f;
            }
        }

        /// <summary>One white frame over the defender (glued to it in Frame).</summary>
        private void FlashSelf()
        {
            if (!Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            if (s == null)
            {
                return;
            }
            _flash = Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 1, true);
            if (_flash != null)
            {
                _flash.Held = true;
                _flash.UseEuler = true;
                _flashAge = 0f;
                Fx.Glue(_flash, this, Fx.WithA(White, 0.75f));
            }
        }

        /// <summary>A white silhouette over any unit as it is drawn right now (in a move or not), fading.</summary>
        internal static void FlashUnit(Actor u, float life, Color c)
        {
            if (u == null || !u.isAlive() || !Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(u);
            if (s == null)
            {
                return;
            }
            Vector2 pos;
            Vector3 euler;
            Vector2 scale;
            if (Moves.TryGet(u, out Move m) && m.A == u)
            {
                if (m.Pose.Hidden)
                {
                    return;
                }
                pos = m.DrawPos();   // (a hovering unit's float included)
                euler = Fx.UnitEuler(m);
                scale = new Vector2(u.actor_scale * m.Pose.SX, u.actor_scale * m.Pose.SY);
            }
            else
            {
                pos = new Vector2(u.current_position.x + u.move_jump_offset.x, u.current_position.y + u.move_jump_offset.y + u.position_height);
                euler = u.current_rotation;
                scale = new Vector2(u.current_scale.x, u.current_scale.y);
            }
            Particle p = Fx.Spawn(s, pos, 1f, life, FxLayer.Over, 1, true);
            if (p == null)
            {
                return;
            }
            p.UseEuler = true;
            p.Euler = euler;
            p.SX = scale.x;
            p.SY = scale.y;
            p.Color(c, Fx.WithA(c, 0f));
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _flashAge += dt;
            _pt += dt;
            switch (_phase)
            {
                case Phase.Incoming:
                    Incoming(dt);
                    break;
                case Phase.Parry:
                    Parry(dt);
                    break;
                case Phase.Wait:
                    Wait(dt);
                    break;
                case Phase.Riposte:
                    Riposte(dt);
                    break;
                default:
                    Settle(dt);
                    break;
            }
        }

        /// <summary>
        /// Forced: the attacker winds back (0-0.08 s), then lunges in with a swish; the defender
        /// does not move at all. At the end of the lunge the blades meet.
        /// </summary>
        private void Incoming(float dt)
        {
            Pose.Ground = _home;
            Pose.Height = 0f;
            Pose.FaceRight = _fwd > 0f;
            Pose.Rot = 0f;
            Pose.SX = 1f;
            Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            if (_ap == null || _ap.Finished || !AtkOk())
            {
                LetAttackerGo();
                Next(Phase.Settle);   // the attacker was taken away before its blow: nothing to meet
                return;
            }
            float k = Mathf.Clamp01(_pt / TIncoming);
            const float back = 0.4f;
            Pose ap = _ap.Pose;
            ap.Height = Mathf.MoveTowards(ap.Height, 0f, dt * 6f);
            ap.Nudge = Vector2.zero;
            if (k < back)
            {
                float w = Anim.OutQuad(k / back);
                ap.Ground = _aFrom;
                ap.Rot = -12f * w;
                ap.SX = 1f - 0.06f * w;
                ap.SY = 1f + 0.05f * w;
            }
            else
            {
                float l = (k - back) / (1f - back);
                ap.Ground = Vector2.Lerp(_aFrom, _aTo, Anim.InQuad(l));
                ap.Rot = Mathf.Lerp(-12f, 22f, Anim.OutQuad(l));
                ap.SX = Mathf.Lerp(0.94f, 1.14f, l);
                ap.SY = Mathf.Lerp(1.05f, 0.9f, l);
            }
            _ap.Pose = ap;
            if (!_swung && k >= back)
            {
                _swung = true;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", _atk.current_tile);
                if (Visible)
                {
                    float face = ap.FaceRight ? 1f : -1f;
                    float ak = Mathf.Sqrt(Mathf.Clamp(_atk.actor_scale / 0.1f, 0.6f, 4f));
                    Kit.Swish(_ap, Art.Arc(Mathf.RoundToInt(7f * ak), 0.7f, 75f, -60f, 1), 0.14f, new Vector2(face * 2f, 0f), face,
                        new Color(1f, 1f, 1f, 0.85f), new Color(0.86f, 0.9f, 0.96f, 0.6f));
                    if (Vector2.Distance(_aFrom, _aTo) > 0.3f)
                    {
                        Fx.Afterimage(_ap, 0.14f, new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f));
                    }
                }
            }
            if (k >= 1f)
            {
                LetAttackerGo();
                Clash();
                Next(Phase.Parry);
            }
        }

        private void LetAttackerGo()
        {
            if (_ap != null && !_ap.Finished)
            {
                _ap.Release(true);
            }
            _ap = null;
        }

        private void Parry(float dt)
        {
            // it gives one pixel and a few degrees, and that is all
            Pose.Ground = _home;
            Pose.Height = 0f;
            Pose.FaceRight = _fwd > 0f;
            Pose.Rot = -4f * Anim.Spring(_pt, 12f, 20f);
            Pose.SX = 1f;
            Pose.SY = 1f;
            Pose.Nudge = _pt < 0.05f ? new Vector2(-_fwd, 0f) : Vector2.zero;
            if (Level <= 1)
            {
                if (!_glinted && _pt >= 0.6f * _tParryI)
                {
                    _glinted = true;
                    CalmGlint();
                }
                if (_pt >= _tParryI)
                {
                    End();
                }
                return;
            }
            if (_pt >= _tParry)
            {
                if (!AtkOk())
                {
                    Next(Phase.Settle);
                }
                else
                {
                    Next(Moves.Locked(_atk) ? Phase.Wait : Phase.Riposte);
                }
            }
        }

        /// <summary>
        /// The attacker is still caught in its broken move: perfectly still, the unit waits for it
        /// (at most 1 s; from Master 0.3 s, then it cuts the attacker where it stands).
        /// </summary>
        private void Wait(float dt)
        {
            _waitT += dt;
            Pose.Ground = _home;
            Pose.Rot = 0f;
            Pose.SX = 1f;
            Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            if (!AtkOk())
            {
                Next(Phase.Settle);
                return;
            }
            float dx = _atk.current_position.x - _home.x;
            if (Mathf.Abs(dx) > 0.1f)
            {
                Pose.FaceRight = dx > 0f;
            }
            if (!Moves.Locked(_atk) || _waitT >= _tWaitMax)
            {
                Next(Phase.Riposte);
            }
        }

        private void Riposte(float dt)
        {
            float px = Px;
            if (Enter())
            {
                StepIn();
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    float facing = Pose.FaceRight ? 1f : -1f;
                    if (_stepTo != _stepFrom)
                    {
                        Fx.Afterimage(this, 0.16f, Level <= 1 ? new Color(1f, 1f, 1f, 0.6f) : _pal.Ghost, _pal.GhostEnd);
                    }
                    if (Level >= 3)
                    {
                        // one upward cut
                        Kit.Swish(this, Art.Arc(Mathf.RoundToInt(9f * _k), 0.85f, -80f, 80f, 3), 0.16f, new Vector2(facing * 2f, 0f), facing, _pal.Core, _pal.Edge);
                    }
                    else
                    {
                        // a 160-degree crescent, 0.8 tile
                        Kit.Swish(this, Art.Arc(Mathf.RoundToInt(8f * _k), 0.65f, 80f, -80f, 2), 0.15f, new Vector2(facing * 2f, 0f), facing, _pal.Core, _pal.Edge);
                    }
                }
            }
            if (!_struck && _pt >= RiposteAt)
            {
                _struck = true;
                Strike();
            }
            float k = Mathf.Clamp01(_pt / TRiposte);
            float flick = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.4f));
            Pose.Ground = Vector2.Lerp(_stepFrom, _stepTo, Anim.OutCubic(Mathf.Clamp01(_pt / RiposteAt)));
            Pose.Rot = (Level >= 3 ? -6f : 9f) * flick;
            Pose.SX = 1f + 0.06f * flick;
            Pose.SY = 1f - 0.04f * flick;
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Next(Phase.Settle);
            }
        }

        /// <summary>
        /// An attacker out of arm's reach (it reeled back, or a forced demonstration from afar) is
        /// followed by one blink of a step, at most 0.9 tile, on safe ground; the cut lands as it arrives.
        /// </summary>
        private void StepIn()
        {
            _stepFrom = _home;
            _stepTo = _home;
            if (!AtkOk())
            {
                return;
            }
            Vector2 ap = _atk.current_position;
            float d = Vector2.Distance(_home, ap);
            float arm = 0.7f * _k + _atk.stats["size"] * 0.5f;
            if (d <= arm + 0.1f || d > arm + 0.9f * _k + 0.6f)
            {
                return;   // already in reach, or too far to follow: it cuts from where it stands
            }
            Vector2 to = _home + (ap - _home) / d * Mathf.Min(d - arm, 0.9f * _k);
            if (Combat.SafeSpot(to) && Combat.ClearPath(_home, to))
            {
                _stepTo = to;
                _home = to;   // it stays where it stepped (the settle, the chain, the reach check)
            }
        }

        /// <summary>The riposte lands. A unit still held by its own move takes the cut but can't be thrown.</summary>
        private void Strike()
        {
            if (!AtkOk())
            {
                return;
            }
            float px = Px;
            // measured from where it stands after its step in (StepIn): a blade's length, no more
            float reach = 0.7f * _k + _atk.stats["size"] * 0.5f + 0.55f + (Level >= 3 ? 0.3f : 0f);
            if (Vector2.Distance(_home, _atk.current_position) > reach)
            {
                return;   // it reeled out of reach: the crescent cuts the air
            }
            if (Combat.Reach(this, _atk, false) != Contact.Hit)
            {
                return;   // the riposte itself was met (two masters trading answers)
            }
            Vector2 ab = AttackerBody();
            bool allowed = Combat.Allowed(A, _atk, out float cm);
            HitResult r = Combat.Hit(A, _atk, allowed ? (Level >= 3 ? 2.5f : 1.5f) * cm : 0.5f, true, true, 0f, this);
            Anim.Sound("event:/SFX/HIT/HitGeneric", _atk.current_tile);
            if (Kit.Visible(ab))
            {
                Vector2 away = ab - BodyPos();
                away = away.sqrMagnitude > 0.0001f ? away.normalized : new Vector2(_fwd, 0f);
                Fx.HitSpark(ab, px, Level >= 3 ? 2 : 1, Level >= 3 ? (away + Vector2.up).normalized : away, Level >= 3 ? 7 : 5);
                if (Level >= 3)
                {
                    Fx.Ring(ab, px, 0.2f, 3f, 14f * _k, 2, 1, FxLayer.Top, 3, 1f, 2.5f, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    Fx.FocusLines(ab, px, Fx.Count(10), 12f * _k, 24f * _k, 0.12f);
                    Kit.Shake(ab, 0.18f, 0.007f, _k);
                }
                if (r.Crit)
                {
                    Kit.ImpactFrame(ab, px, 0.4f * _k, _pal, true);
                }
            }
            if (!allowed)
            {
                Combat.Stagger(_atk, 0.3f);   // too heavy to throw: it only rocks
                return;
            }
            if (!_atk.isAlive() || !_atk.hasHealth() || Moves.Locked(_atk))
            {
                return;   // (a flow strike is no shield: Launch cuts it short)
            }
            // a stronger tier sends it further (a God's answer throws it half as far again)
            float lk = Mathf.Lerp(1f, _k, 0.5f) * SingleKit.Throw(_tier);
            if (Level >= 3)
            {
                _atk.addStatusEffect("stunned", 1f);
                Combat.Launch(_atk, _home, 2f * lk, 1.3f * Mathf.Sqrt(lk), Random.value < 0.5f ? -720f : 720f);
                _launched = !Weights.Heavy(_atk);
            }
            else
            {
                _atk.addStatusEffect("stunned", 0.5f);
                Combat.Launch(_atk, _home, 1.5f * lk, 0.5f, 0f);
            }
        }

        private void Settle(float dt)
        {
            if (Enter())
            {
                _rotFrom = Pose.Rot;
            }
            float amp = Anim.Spring(_pt / _snap, 10f, 16f);
            Pose.Ground = _home;
            Pose.Rot = _rotFrom * amp;
            Pose.SX = 1f;
            Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            if (!_glinted && _pt >= 0.5f * _tSettle)
            {
                _glinted = true;
                CalmGlint();
            }
            if (_pt >= _tSettle)
            {
                End();
            }
        }

        private void CalmGlint()
        {
            if (Visible)
            {
                Fx.Glint(BodyPos() + new Vector2((Pose.FaceRight ? 1f : -1f) * 2f, 0.5f) * Px, Px, 0.16f, _pal.Core, 0.6f);
            }
        }

        /// <summary>
        /// The chain it leaves, so the defender's next action follows at once (the Brain looks the
        /// moment a move ends on a chain): an attacker launched (III: Launched for a unit that can
        /// follow it into the air, otherwise Down as it lands) or reeling beside it (I, II: Down).
        /// </summary>
        private void End()
        {
            if (_clashed && AtkOk() && _atk.hasHealth())
            {
                if (_launched)
                {
                    EndLink = SingleKit.AirLink(A, 0.6f);
                    EndTarget = _atk;
                }
                else if (Vector2.Distance(_home, _atk.current_position) <= 3f)
                {
                    EndLink = Link.Down;
                    EndTarget = _atk;
                }
            }
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            KillFlash();
            Finished = true;
        }

        public override void Frame()
        {
            if (_flash == null || !_flash.Alive)
            {
                return;
            }
            // a white sheen, not a white cut-out: the unit stays readable through it
            float a = 0.75f * (_flashAge < 0.035f ? 1f : 1f - (_flashAge - 0.035f) / 0.05f);
            if (a <= 0f)
            {
                KillFlash();
                return;
            }
            Fx.Glue(_flash, this, Fx.WithA(White, a));
        }

        private void KillFlash()
        {
            _flash?.Kill();
            _flash = null;
        }

        public override void Abort()
        {
            KillFlash();
            LetAttackerGo();
        }

        public override void OnEnd(bool aborted)
        {
            KillFlash();
            LetAttackerGo();
        }

        // ------------------------------------------------------------- guards --

        /// <summary>
        /// While a guard stands, nothing the attacker does can hurt the defender: for the parry itself
        /// (I, II), or (III) for as long as the parried technique's move still runs (at most 4 s) -
        /// so a technique that ignores being parried still can't land.
        /// </summary>
        private struct Guard
        {
            public long Def, Atk;
            public Move Incoming;
            public double Base, Until;
        }

        private static readonly List<Guard> _guards = new List<Guard>();
        private static volatile int _guardCount;

        private static bool Active(Guard g, double now)
        {
            if (now <= g.Base)
            {
                return true;
            }
            return g.Incoming != null && now <= g.Until && !g.Incoming.Finished;
        }

        private static void AddGuard(Actor d, Actor atk, Move incoming, float seconds, bool whole)
        {
            double now = Fighter.Now;
            for (int i = _guards.Count - 1; i >= 0; i--)
            {
                if (!Active(_guards[i], now))
                {
                    _guards.RemoveAt(i);
                }
            }
            if (_guards.Count >= 32)
            {
                _guards.RemoveAt(0);
            }
            _guards.Add(new Guard
            {
                Def = d.Sid(),
                Atk = atk.Sid(),
                Incoming = whole ? incoming : null,
                Base = now + seconds,
                Until = now + 4.0
            });
            _guardCount = _guards.Count;
        }

        /// <summary>Hooks.BeforeDamage: a guarded defender takes nothing from its attacker. Cheap when no guard stands.</summary>
        internal static bool Shield(Actor victim, float damage, AttackType type, BaseSimObject attacker)
        {
            if (_guardCount == 0 || victim == null || attacker == null)
            {
                return false;
            }
            long vid = victim.Sid();
            long aid = attacker.Sid();
            if (vid < 0 || aid < 0)
            {
                return false;
            }
            double now = Fighter.Now;
            for (int i = _guards.Count - 1; i >= 0; i--)
            {
                if (i >= _guards.Count)
                {
                    continue;
                }
                Guard g = _guards[i];
                if (g.Def == vid && g.Atk == aid && Active(g, now))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ClearAll()
        {
            _guards.Clear();
            _guardCount = 0;
        }

        // ------------------------------------------------------------ chances --

        /// <summary>
        /// The chance to read and meet a blow: the tier's React (untiered .3), +-0.1 per tier of
        /// difference with the attacker, halved for a blow from behind.
        /// </summary>
        internal static float Chance(Actor d, Actor atk, Tier tier)
        {
            TierInfo info = Tiers.Info(tier);
            float c = info != null ? info.React : 0.3f;
            c += 0.1f * ((int)tier - (int)Tiers.Of(atk));
            float dx = atk.current_position.x - d.current_position.x;
            if (Mathf.Abs(dx) > 0.2f && dx > 0f != d.flip)
            {
                c *= 0.5f;
            }
            c *= Personas.Of(d).ReadSkill;   // a counter-puncher reads the blow; a berserker barely looks
            return Mathf.Clamp(c, 0.03f, 0.95f);
        }

        // ------------------------------------------------------ casual parries --

        private static Technique _tech;

        /// <summary>
        /// Hooks.BeforeOrdinaryHit: a Still Water user (free, on its feet) turns aside an ordinary
        /// melee blow with half its reaction chance, once a second at most (no limit at III).
        /// </summary>
        internal static bool CasualParry(AttackData data, Actor t)
        {
            if (data.is_projectile || data.initiator == null || t == null || !data.initiator.isActor())
            {
                return false;
            }
            Technique tech = _tech ??= Techniques.Get("still_water");
            if (tech == null || !Fighters.TryGet(t, out Fighter f))
            {
                return false;
            }
            int level = Techniques.Level(t, tech, f.Tier);
            if (level <= 0)
            {
                return false;
            }
            Actor atk = data.initiator.a;
            if (atk == null || atk == t || !atk.isAlive() || !Moves.CanPerform(t))
            {
                return false;
            }
            if (level < 3 && !f.Elapsed(CasualTimer, 1f))
            {
                return false;
            }
            if (Random.value >= 0.5f * Chance(t, atk, f.Tier))
            {
                return false;
            }
            f.Mark(CasualTimer);
            Casual(t, atk, level);
            return true;
        }

        /// <summary>A small ting, a few sparks, a flash; the attacker rocks back.</summary>
        private static void Casual(Actor d, Actor atk, int level)
        {
            Casuals++;
            Combat.Stagger(atk, 0.2f);
            Anim.Sound(Armed(d) ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitGeneric", d.current_tile);
            float px = Mathf.Clamp(d.actor_scale, 0.04f, 0.4f);
            Vector2 body = BodyOf(d);
            Vector2 to = BodyOf(atk) - body;
            float dist = to.magnitude;
            Vector2 n = dist > 0.001f ? to / dist : new Vector2(d.flip ? 1f : -1f, 0f);
            Vector2 contact = body + n * Mathf.Min(dist * 0.5f, 2.5f * px);
            if (!Kit.Visible(contact))
            {
                return;
            }
            Palette pal = Palette.For(d, level);
            Fx.Glint(contact, px, 0.14f, level >= 2 ? pal.Core : White, 0.7f);
            Kit.Sparks(contact, px, 4, 40f, 90f, level >= 2 ? pal.Edge : Pale);
            FlashUnit(d, 0.05f, new Color(1f, 1f, 1f, 0.9f));
        }
    }

    /// <summary>
    /// Still Water as a technique. Entry: Expert. Reactive: it is never planned in a fight; it
    /// answers the first contact of someone else's single-target technique (Combat.Reach), and
    /// turns aside ordinary melee blows now and then. Forced (dev command, god power) it demonstrates
    /// the parry on the nearest enemy.
    /// </summary>
    internal sealed class StillWaterTech : Technique
    {
        public override string Id => "still_water";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;

        /// <summary>The blow is met and simply not there; the riposte comes before the attacker recovers.</summary>
        public override Form Forms => Form.Blade | Form.Counter | Form.Guard | Form.Fast;

        /// <summary>Any single blow of a limb or a blade, a dash, a counter (two masters trading answers); never an area.</summary>
        public override Form Answers => Form.Strike | Form.Dash | Form.Counter;

        public override bool Reactive => true;
        public override float[] ManaCost => new[] { 6f, 10f, 16f };
        public override float[] Cooldown => new[] { 5f, 4f, 3f };

        [SakugaInit]
        private static void Init()
        {
            Hooks.BeforeOrdinaryHit.Add(StillWater.CasualParry);
            Hooks.BeforeDamage.Add(StillWater.Shield);
            Hooks.WorldCleared += StillWater.ClearAll;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || defender == null || attacker == null || incoming == null || defender == attacker)
            {
                return Contact.Hit;   // area blows can't be parried
            }
            // read (Reactions: tier, persona, memory, a counter's depth), on its feet (or, against a
            // counter, out of its own broken move)
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return StillWater.Begin(defender, attacker, incoming, level, this) ? Contact.Parried : Contact.Hit;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // a reaction, never a plan
            }
            Actor v = c.ForceTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || !v.isAlive() || c.Dist(v) > 3f)
            {
                return null;
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = 1f };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && StillWater.Begin(a, p.Target, null, p.Level, this);
        }
    }
}
