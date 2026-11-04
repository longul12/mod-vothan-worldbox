using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SEVERING FLASH. "Already behind you." An iaido draw: the unit settles into a stance,
    /// vanishes, and is standing back to back with its enemy before anyone saw the blade.
    /// A beat of silence. Then the cut lands, and a lone enemy that was already hurt falls in
    /// two (no gore: its sprite parts at the waist, the upper half slides along the cut and
    /// topples, and both fade as the body drops).
    ///
    /// Beat sheet (level II, game seconds at x1):
    ///
    ///   STANCE   0.35  squash to 85%, a lean over the hilt; focus lines converge from 3 tiles, the
    ///                  dust around the feet settles; a glint at the hand at 0.30
    ///   DRAW     0.05  the unit vanishes (intangible); an afterimage stays at the start; a line
    ///                  runs from the start through the enemy to 1.5 tiles past it, speed lines
    ///                  along it: a 3-px tier-tinted flash for a few frames, then a 1-px white razor
    ///                  cooling to the tier colour. The enemy's one chance to answer is here
    ///   HOLD     0.40  back to back: the unit in its follow-through, the enemy frozen (a puppet);
    ///                  the line fades; nothing moves
    ///   CUT      0.80  a small white pop on the enemy, a slash mark across the waist, sparks, a
    ///                  0.05 s hit-stop. x5; if that leaves it at 30% health or less it is
    ///                  executed: its sprite is cut at the waist into two real halves; the upper
    ///                  one slides a texel, then two, along the cut (0.3 s), then topples forward
    ///                  as both fade and the body drops (0.3 s). A survivor reels and staggers
    ///   SHEATHE  0.30  the unit straightens with a spring; a tiny sheathe glint at the hip
    ///
    /// Level I: a 0.5 s stance, a plain white line, a shorter hold; x3, executes at 15%.
    /// Level III: a 0.2 s stance (just the glint), a 6-tile line (white core, tier edge, outer
    /// glow) that leaves a glowing cut in the ground, a local black flash disk, an impact frame and
    /// a global hit-stop at the cut; x8, executes at 50%, and always kills an enemy two or more
    /// weight classes below what the unit may fight. On the frame the cut lands the ground splits
    /// open along the whole line (Kit.Ravage, force 1-2; a God's line is 9 tiles, wider, and burns),
    /// sparing only the unit's own feet. (All of it times the tier's Might.)
    ///
    /// An enemy of the unit's own tier or above is executed at half those thresholds: one already
    /// on its last legs falls in two, a fresh equal does not (with Might, the III cut alone took a
    /// God from 82% to the execution line).
    ///
    /// Its pace follows the tier (TierInfo.Linger): from Master the stance, the hold and the split
    /// play about a quarter faster, the sheathe (and a parried reel, a lost stance) about half as
    /// long, and the game's attack cooldown is left alone (their ordinary swings are flow strikes).
    ///
    /// The enemy is pinned through the stance (no swing, no hop away; it may still answer the draw).
    /// An enemy caught mid-hop at the draw (an archer backstepping) is frozen too, set back down
    /// where it was: it must not keep acting (shooting, walking into the unit's landing spot)
    /// through the hold. One too heavy or too big to freeze is held where it stands; if it gets
    /// away all the same (takes off, dashes off in a move of its own), the blade cuts its
    /// afterimage. A duel opponent is never executed (only the duel's Final ends a duel).
    ///
    /// Circumstances: a parried draw stops at the enemy (the unit reappears in front of it and
    /// reels back); an evaded one cuts an afterimage (a thin slash across the empty spot, no
    /// damage) after only a breath of a hold, and the unit snaps round to where the enemy really
    /// is and is gone into its next action. An enemy lying stunned is frozen as it lies (and, if
    /// it survives, struggles up through its reel). An enemy in the middle of a flow strike or a
    /// blur dash may be drawn on (the draw takes it out of it). An immortal enemy is split, then
    /// the halves slide back together and it gets back up. If the enemy dies during the stance the
    /// unit turns on another enemy of the same class within 2 tiles, or relaxes with a glint. A
    /// bare-handed unit cuts with the edge of its hand and flicks it clean instead of sheathing.
    /// Never ends on a wall, water, lava or off the map; never draws through a mountain.
    /// </summary>
    internal sealed class SeveringFlash : Move
    {
        public const float MinRange = 1.5f;
        public const float MaxRange = 4f;

        /// <summary>The nearest a chained draw may start from (a reeling enemy right beside the unit).</summary>
        public const float MinLinkRange = 0.9f;

        private enum Phase
        {
            Stance,
            Draw,
            Hold,
            Sheathe,
            Broken,
            Lost
        }

        private const float TDraw = 0.05f;
        private const float TSplit = 0.3f;
        private const float TSplitEnd = 0.14f;
        /// <summary>Killed, real halves: the upper half topples and both fade (after TSplit).</summary>
        private const float TFall = 0.3f;
        /// <summary>Where the body is cut: this far up the sprite (the waist, at the draw's height).</summary>
        private const float CutK = 0.38f;
        /// <summary>The halves are not quite solid: no gore, a body that is already a memory.</summary>
        private const float HalfAlpha = 0.85f;
        private const float TBroken = 0.4f;
        private const float TLost = 0.3f;
        private const float TReel = 0.2f;
        /// <summary>The hold after a draw that cut only an afterimage.</summary>
        private const float TEmpty = 0.12f;

        private const string SndWhoosh = "event:/SFX/UNIQUE/SpawnWhoosh";

        private static readonly float[] EndTries = { 1f, 0.7f, 1.45f };
        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Ink = new Color(0.05f, 0.02f, 0.04f, 1f);

        private readonly Palette _pal;
        private readonly bool _armed;
        private readonly float _k, _ah;
        private readonly float _tStance, _tHold, _tSheathe;
        private readonly float _mult;
        private readonly Vector2 _start;

        // ---- the tier: dead time (snap) and the beats of the draw (beat) shrink from Master up
        private readonly Tier _tier;
        private readonly float _snap, _beat, _tBroken, _tLost;

        /// <summary>Where the line of the draw ends (III: the ground splits open along it at the cut).</summary>
        private Vector2 _lineEnd;
        private bool _lineSet;

        // ---- the enemy
        private Actor _v;
        private long _vid;
        private Weight _class;
        private float _cm = 1f;
        private float _vpx, _vPivot, _vRatio;
        private Puppet _p;
        private bool _released;
        private Vector2 _lastSeen;
        private int _retargets;
        /// <summary>Frozen as it lay (knocked flat when the blade came): its pose, kept through the hold.</summary>
        private bool _fallen;
        private float _fallRot;
        private Vector2 _fallNudge;

        private Vector2 _dir, _end, _drawTo, _from, _to;
        private float _fwd;
        private Contact _contact = Contact.Hit;

        // ---- the clock
        private Phase _phase = Phase.Stance;
        private float _pt, _hold;
        private bool _entered;
        private bool _glinted, _sheathed, _glintEnd;
        private float _focusT, _windT;
        /// <summary>How long the sheathe takes (it spans a split still playing) and when the blade clicks home.</summary>
        private float _sheatheFor, _clickAt;
        private float _rotFrom, _sxFrom, _syFrom;

        // ---- the cut
        private bool _killed;
        private float _reelT = -1f;
        private float _splitT = -1f;
        private Particle _halfA, _halfB;
        private Vector2 _splitAt, _splitStep, _splitScale;
        private Vector3 _splitEuler;
        private bool _corpseHidden;
        // the real halves (made for this cut, destroyed with it)
        private bool _realHalves;
        private Sprite _topS, _botS;
        private float _cutLift;

        /// <summary>Draws / executions since the game started (dev log).</summary>
        internal static int Started, Executed;

        public static float Mult(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 5f : 8f;
        }

        public static float Threshold(int level)
        {
            return level <= 1 ? 0.15f : level == 2 ? 0.3f : 0.5f;
        }

        /// <summary>
        /// The execution line against this enemy: half of it against one of the unit's own tier or
        /// above (only one already on its last legs falls in two, never a fresh equal).
        /// </summary>
        public static float Threshold(int level, Actor a, Actor v)
        {
            float t = Threshold(level);
            Tier vt = Tiers.Of(v);
            return vt != Tier.None && vt >= Tiers.Of(a) ? t * 0.5f : t;
        }

        /// <summary>Two or more weight classes below what 'a' may fight (Severing Flash III always kills these).</summary>
        public static bool FarBelow(Actor a, Actor v)
        {
            return (int)Weights.Limit(a) - (int)Weights.Of(v) >= 2;
        }

        private SeveringFlash(Actor a, Actor v, int level, float cm) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _pal = Palette.For(a, Level);
            _armed = Armed(a);
            _k = K;
            Sprite sa = Fx.UnitSprite(a);
            _ah = Mathf.Max(0.2f, (sa != null ? sa.rect.height : 10f) * a.actor_scale);
            switch (Level)
            {
                case 1:
                    _tStance = 0.5f;
                    _tHold = 0.3f;
                    _tSheathe = 0.3f;
                    break;
                case 2:
                    _tStance = 0.35f;
                    _tHold = 0.4f;
                    _tSheathe = 0.3f;
                    break;
                default:
                    _tStance = 0.2f;
                    _tHold = 0.45f;
                    _tSheathe = 0.35f;
                    break;
            }
            _mult = Mult(Level);
            // Masters and up: a crisper draw (stance, hold, split) and almost no dead time after it
            _tier = Tiers.Of(a);
            _snap = SingleKit.Snap(_tier);
            _beat = Mathf.Lerp(1f, _snap, 0.5f);
            _tStance = Mathf.Max(0.16f, _tStance * _beat);
            _tHold *= _beat;
            _tSheathe *= _snap;
            _tBroken = TBroken * _snap;
            _tLost = TLost * _snap;
            _start = a.current_position;
            SetVictim(v, cm);
        }

        // ------------------------------------------------------------ helpers --

        public static bool Begin(Actor a, Actor v, int level, Technique tech)
        {
            if (v == null || !Moves.CanPerform(a) || !Combat.IsFoe(a, v) || !HundredFists.Hopping(v))
            {
                return false;
            }
            if (!Combat.Allowed(a, v, out float cm))
            {
                return false;
            }
            if (Vector2.Distance(a.current_position, v.current_position) > MaxRange + 2.5f)
            {
                return false;
            }
            SeveringFlash m = new SeveringFlash(a, v, level, cm) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(Vector2.Lerp(a.current_position, v.current_position, 0.5f) + new Vector2(0f, 1f));
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

        /// <summary>
        /// Where the unit ends: just past the enemy on the line of the draw (back to back), on safe
        /// ground, with no wall or mountain between the start and there. Tries a little nearer and further.
        /// </summary>
        internal static bool FindEnd(Vector2 start, Vector2 vpos, Vector2 dir, float standoff, out Vector2 end)
        {
            for (int i = 0; i < EndTries.Length; i++)
            {
                Vector2 e = vpos + dir * standoff * EndTries[i];
                if (Combat.SafeSpot(e) && PathOpen(start, e))
                {
                    end = e;
                    return true;
                }
            }
            end = vpos;
            return false;
        }

        /// <summary>No wall or mountain on the straight line from a to b (water and lava are crossed in a flash).</summary>
        internal static bool PathOpen(Vector2 a, Vector2 b)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(d / 0.5f));
            for (int i = 1; i <= n; i++)
            {
                WorldTile t = Combat.TileAt(Vector2.Lerp(a, b, i / (float)n));
                if (t == null || t.Type == null || t.Type.block)
                {
                    return false;
                }
            }
            return true;
        }

        private void SetVictim(Actor v, float cm)
        {
            _v = v;
            _vid = v.Sid();
            _class = Weights.Of(v);
            _cm = cm;
            _lastSeen = v.current_position;
            _vpx = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
            Sprite s = Fx.UnitSprite(v);
            float h = s != null ? s.rect.height : 10f;
            _vPivot = Mathf.Clamp(h * 0.36f, 1.5f, 40f);
            _vRatio = Mathf.Max(0.2f, h * v.actor_scale) / _ah;
            Aim(v.current_position);
        }

        private void Aim(Vector2 vpos)
        {
            Vector2 d = vpos - _start;
            _dir = d.sqrMagnitude > 0.0004f ? d.normalized : new Vector2(A.flip ? 1f : -1f, 0f);
            _fwd = Mathf.Abs(_dir.x) > 0.12f ? Mathf.Sign(_dir.x) : (A.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
        }

        /// <summary>
        /// How far past the enemy it ends: back to back, but two silhouettes with air between them
        /// (at 0.55 tile the two sprites overlapped and the split halves were drawn over the unit).
        /// </summary>
        internal const float StandoffK = 0.75f;

        private float Standoff()
        {
            return StandoffK * _k + 0.3f * Mathf.Max(0f, _vRatio - 1f) * _k;
        }

        private bool VictimOk()
        {
            return _v != null && _v.Sid() == _vid && _v.isAlive() && _v.hasHealth() && !_v.isInMagnet() && !_v.isInsideSomething();
        }

        private bool HeldOk()
        {
            return _p != null && !_released && !_p.Finished && _v != null && _v.Sid() == _vid && _v.isAlive();
        }

        /// <summary>
        /// An enemy the draw could not freeze (too heavy or too big to hold, the player's own unit,
        /// busy with a move of its own) acted through the hold: the cut lands only if it is still
        /// on the ground where the blade passed it, free and touchable.
        /// </summary>
        private bool StillThere()
        {
            if (Moves.Locked(_v) || Moves.Intangible(_v) || !Combat.Grounded(_v))
            {
                return false;
            }
            float r = Standoff();
            return (_v.current_position - _lastSeen).sqrMagnitude <= r * r;
        }

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.45f);

        private Vector2 VBody()
        {
            if (HeldOk())
            {
                return _p.BodyPos();
            }
            Vector2 at = _v != null && _v.Sid() == _vid ? _v.current_position : _lastSeen;
            float h = _v != null && _v.Sid() == _vid ? _v.position_height + _vPivot * _v.actor_scale : 0.35f;
            return new Vector2(at.x, at.y + h);
        }

        private Vector2 HiltPos()
        {
            return BodyPos() + new Vector2(_fwd * 1.5f, -1f) * Px;
        }

        private Vector2 SheathPos()
        {
            return BodyPos() + new Vector2(-_fwd * 1.5f, -1.5f) * Px;
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("sever " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        /// <summary>Mid-blink, nothing can touch it.</summary>
        public override bool Intangible => _phase == Phase.Draw;

        /// <summary>The stance gathers the draw: a quick blow now breaks it (the enemy's one chance before the blade).</summary>
        public override bool Charging => _phase == Phase.Stance && !Finished;

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (_hold > 0f)
            {
                _hold -= dt;   // the local hit-stop of the cut: everything holds a beat
                return;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Stance:
                    Stance(dt);
                    break;
                case Phase.Draw:
                    Draw(dt);
                    break;
                case Phase.Hold:
                    Hold(dt);
                    break;
                case Phase.Sheathe:
                    Sheathe(dt);
                    break;
                case Phase.Broken:
                    Broken(dt);
                    break;
                default:
                    Lost(dt);
                    break;
            }
            if (_splitT >= 0f)
            {
                Split(dt);
            }
        }

        /// <summary>The enemy died before the draw: another of the same class within 2 tiles of it.</summary>
        private bool Retarget()
        {
            if (_retargets >= 1)
            {
                return false;
            }
            Actor best = null;
            float bestD = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, _lastSeen, 2f))
            {
                if (u == _v || Weights.Of(u) != _class || u.isFlying() || !Combat.Allowed(A, u))
                {
                    continue;
                }
                float d = Vector2.Distance(_start, u.current_position);
                if (d < 0.6f || d > MaxRange + 1f)
                {
                    continue;
                }
                if (d < bestD && FindEnd(_start, u.current_position, (u.current_position - _start) / d, Standoff(), out _))
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
            if (Dev.On) Dev.Trace("sever " + Id + " retargets " + best.asset.id + "#" + best.Sid());
            return true;
        }

        // ------------------------------------------------------------- stance --

        private void Stance(float dt)
        {
            float px = Px;
            if (Enter() && Level <= 2 && Visible)
            {
                SettleDust(px);
            }
            if (!VictimOk() && !Retarget())
            {
                Next(Phase.Lost);
                return;
            }
            _lastSeen = _v.current_position;
            Aim(_lastSeen);
            // the world narrows to the draw: the enemy doesn't swing or hop away through the stance
            // (it may still answer the draw itself)
            Combat.Pin(_v, 0.3f);
            float k = Mathf.Clamp01(_pt / _tStance);
            float c = Anim.OutCubic(Mathf.Clamp01(_pt / Mathf.Min(0.15f, _tStance * 0.5f)));
            Pose.Ground = _start;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.SY = Mathf.Lerp(1f, Level >= 3 ? 0.9f : 0.85f, c);
            Pose.SX = Mathf.Lerp(1f, 1.07f, c);
            Pose.Rot = 6f * c;
            if (Level <= 2 && Visible)
            {
                _focusT -= dt;
                if (_focusT <= 0f)
                {
                    _focusT = 0.07f;
                    Converge(BodyPos(), px);
                }
            }
            float glintAt = Level >= 3 ? 0.06f : _tStance - 0.05f;
            if (!_glinted && _pt >= glintAt)
            {
                _glinted = true;
                if (Visible)
                {
                    Fx.Glint(HiltPos(), px, Level >= 3 ? 0.16f : 0.2f, _pal.Core, Level >= 3 ? 1.1f : 0.9f);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Draw);
            }
        }

        /// <summary>Manga focus lines rushing in from 3 tiles away onto the unit: the world narrows to the draw.</summary>
        private void Converge(Vector2 center, float px)
        {
            int n = Fx.Count(Level <= 1 ? 4 : 6);
            float r = 30f * _k;
            Color c = Level >= 2 ? Color.Lerp(White, _pal.Edge, 0.35f) : White;
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f).normalized;
                Particle p = Fx.Spawn(Art.Lines[Random.Range(1, 3)], center + dir * Random.Range(0.75f, 1f) * r * px, px, 0.18f, FxLayer.Top, 2);
                if (p == null)
                {
                    break;
                }
                p.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                p.Moving(-dir * 90f * px);
                p.Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.55f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>The air goes still: dust motes around the feet sink and settle.</summary>
        private void SettleDust(float px)
        {
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            int n = Fx.Count(5);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Vector2 at = _start + dir * Random.Range(6f, 14f) * px;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], at, px, _tStance + 0.15f, dir.y < 0f ? FxLayer.Top : FxLayer.Ground, 5);
                if (p == null)
                {
                    break;
                }
                p.Z = Random.Range(3f, 7f) * px;
                p.Moving(-dir * 5f * px, -12f * px, 1.5f);
                p.Sized(0.6f, 0.4f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
            }
        }

        // --------------------------------------------------------------- draw --

        private void Draw(float dt)
        {
            if (Enter() && !Unsheathe())
            {
                return;
            }
            float k = Mathf.Clamp01(_pt / TDraw);
            Pose.Hidden = true;
            Pose.Ground = Vector2.Lerp(_start, _drawTo, k);
            if (k >= 1f)
            {
                Pose.Hidden = false;
                Pose.Ground = _drawTo;
                Next(_contact == Contact.Parried ? Phase.Broken : Phase.Hold);
            }
        }

        /// <summary>The blade comes out: last checks, the enemy's one chance to answer, the line.</summary>
        private bool Unsheathe()
        {
            if (!VictimOk() && !Retarget())
            {
                Next(Phase.Lost);
                return false;
            }
            Vector2 vpos = _v.current_position;
            if (!HundredFists.Hopping(_v) || Vector2.Distance(_start, vpos) > MaxRange + 1.5f)
            {
                Next(Phase.Lost);   // it took off (flying, thrown) or got too far
                return false;
            }
            Aim(vpos);
            if (!FindEnd(_start, vpos, _dir, Standoff(), out _end))
            {
                Next(Phase.Lost);
                return false;
            }
            _lastSeen = vpos;   // where the blade passes it
            _contact = Combat.Reach(this, _v, false);
            float px = Px;
            bool vis = Visible;
            if (vis)
            {
                // what's left where it stood
                Fx.Afterimage(this, Level >= 3 ? 0.55f : 0.45f, Level <= 1 ? new Color(1f, 1f, 1f, 0.7f) : _pal.Ghost, _pal.GhostEnd);
                Fx.LandingPuff(_start, px, A.current_tile);
            }
            Anim.Sound(SndWhoosh, A.current_tile);
            if (_contact == Contact.Parried)
            {
                // the blade is met: the line stops at the enemy and the unit lands in front of it
                Vector2 front = vpos - _dir * (0.5f * _k + 0.1f);
                _drawTo = Combat.SafeSpot(front) && PathOpen(_start, front) ? front : _start;
                if (vis)
                {
                    DrawLine(_start, vpos - _dir * 0.15f, px, 0.6f);
                }
                return true;
            }
            _drawTo = _end;
            if (_contact == Contact.Hit && HundredFists.Catchable(_v, A))
            {
                // frozen where it stands, facing where the unit was (caught mid-backstep, it is
                // set down where it was: an archer hopping away is frozen like anyone else)
                Puppet p = HundredFists.Take(_v, _fwd < 0f, this);
                if (p != null)
                {
                    _p = p;
                    _released = false;
                    // knocked flat: frozen as it lies (taking it upright snapped it up in one frame)
                    _fallen = Mathf.Abs(p.Pose.Rot) > 1f;
                    _fallRot = p.Pose.Rot;
                    _fallNudge = p.Pose.Nudge;
                }
            }
            if (Dev.On && _contact == Contact.Hit && !HeldOk()) Dev.Trace("sever " + Id + " can't freeze " + _v.asset.id + "#" + _vid + " (h " + _v.position_height.ToString("F2") + ", locked " + Moves.Locked(_v) + ", heavy " + Weights.Heavy(_v) + ")");
            _lineEnd = LineEnd(vpos);
            _lineSet = true;
            if (vis)
            {
                Vector2 lineEnd = _lineEnd;
                DrawLine(_start, lineEnd, px, 1f);
                Color sl = Level <= 1 ? new Color(1f, 1f, 1f, 0.8f) : Fx.WithA(_pal.Edge, 0.85f);
                Kit.SpeedLines(BodyPos() + _dir * 3f * px, _dir, px, 5, 4f, 0.16f, sl);
                Kit.SpeedLines(VBody(), _dir, px, 3, 3f, 0.14f, sl);
                if (Level >= 3)
                {
                    // the ground itself is cut, glowing and cooling
                    // (white-hot, then the tier colour, then a dark ember line that fades)
                    Kit.Line(_start, lineEnd, px, 1.8f, 1, FxLayer.Ground, 4,
                        Fx.WithA(_pal.Core, 1f), Fx.WithA(_pal.Edge, 0.85f), Fx.WithA(_pal.Dark, 0.5f), Fx.WithA(_pal.Dark, 0f));
                }
            }
            return true;
        }

        /// <summary>How far the line of the draw runs: just past (I), 1.5 tiles past the enemy (II), 6 tiles (III; a God's 9).</summary>
        private Vector2 LineEnd(Vector2 vpos)
        {
            Vector2 l;
            if (Level <= 1)
            {
                l = _end + _dir * 0.4f;
            }
            else if (Level == 2)
            {
                l = vpos + _dir * Mathf.Max(1.5f, Vector2.Distance(vpos, _end) + 0.4f);
            }
            else
            {
                l = _start + _dir * Mathf.Max(_tier == Tier.God ? 9f : 6f, Vector2.Distance(_start, vpos) + 1.5f);
            }
            return Inside(_start, l);
        }

        private static Vector2 Inside(Vector2 from, Vector2 to)
        {
            for (int i = 0; i < 12; i++)
            {
                if (to.x >= 0.5f && to.y >= 0.5f && to.x <= MapBox.width - 1.5f && to.y <= MapBox.height - 1.5f)
                {
                    return to;
                }
                to = Vector2.Lerp(from, to, 0.8f);
            }
            return Combat.Clamp(to);
        }

        /// <summary>
        /// The draw's line at body height: I plain white; II a white core on a tier edge; III white-hot,
        /// edge and glow. Only the first instant is a band of light: the 3-px edge (and the III glow)
        /// is a bloom of a few frames, and what the hold shows is a razor, one pixel of white cooling
        /// to the tier colour. (As a 3-px band lasting half a second it lay across both bodies through
        /// the whole hold, a laser beam hiding the back-to-back pose, and faded to a muddy stripe.)
        /// </summary>
        private void DrawLine(Vector2 from, Vector2 to, float px, float lifeK)
        {
            float h = Pose.PivotUp * A.actor_scale * 0.9f;
            Vector2 a = from + new Vector2(0f, h), b = to + new Vector2(0f, h);
            switch (Level)
            {
                case 1:
                    GrowLine(a, b, px, TDraw, 0.28f * lifeK, 1, 12, White, Fx.WithA(White, 0.8f), Fx.WithA(White, 0f));
                    break;
                case 2:
                    GrowLine(a, b, px, TDraw, 0.12f * lifeK, 3, 11, Fx.WithA(_pal.Edge, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    GrowLine(a, b, px, TDraw, 0.36f * lifeK, 1, 12, White, Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Edge, 0f));
                    break;
                default:
                    GrowLine(a, b, px, TDraw, 0.08f * lifeK, 5, 10, Fx.WithA(_pal.Edge, 0.35f), Fx.WithA(_pal.Edge, 0f));
                    GrowLine(a, b, px, TDraw, 0.18f * lifeK, 3, 11, _pal.Edge, Fx.WithA(_pal.Edge, 0.5f), Fx.WithA(_pal.Edge, 0f));
                    GrowLine(a, b, px, TDraw, 0.42f * lifeK, 1, 12, White, _pal.Core, Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
                    break;
            }
        }

        /// <summary>A straight line that shoots from a to b over 'grow' seconds, then fades: 'thick' pixels wide.</summary>
        private static Particle GrowLine(Vector2 a, Vector2 b, float px, float grow, float life, int thick, int order, params Color[] colors)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < px)
            {
                return null;
            }
            Particle p = Fx.Spawn(Art.Pixel, a, px, life, FxLayer.Top, order);
            if (p == null)
            {
                return null;
            }
            Vector2 u = d / len;
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.SX = 1f;
            p.SY = Mathf.Max(1, thick);
            p.Color(colors);
            p.Step = (q, dt) =>
            {
                float k = grow > 0f ? Mathf.Clamp01(q.Age / grow) : 1f;
                float l = Mathf.Max(px, len * k);
                q.SX = l / px;
                q.Pos = a + u * (l * 0.5f);
            };
            return p;
        }

        // --------------------------------------------------------------- hold --

        private void Hold(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Hidden = false;
                if (Visible)
                {
                    // it skids to a stop
                    Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                    for (int i = 0; i < Fx.Count(3); i++)
                    {
                        Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], _drawTo - _dir * Random.Range(0f, 2f) * px, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 2);
                        p?.Moving(_dir * Random.Range(12f, 22f) * px, Random.Range(2f, 5f) * px, 4f)
                            .Sized(0.6f, 1.1f)
                            .Color(Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
                    }
                }
            }
            // back to back, perfectly still: the follow-through after the draw
            float s = Anim.OutCubic(Mathf.Clamp01(_pt / 0.08f));
            Pose.Ground = _drawTo;
            Pose.FaceRight = _fwd > 0f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Rot = Mathf.Lerp(16f, 10f, s);
            Pose.SX = Mathf.Lerp(1.15f, 1.05f, s);
            Pose.SY = Mathf.Lerp(0.82f, 0.9f, s);
            if (HeldOk())
            {
                Pose vp = _p.Pose;
                vp.Nudge = _fallen ? _fallNudge : Vector2.zero;
                vp.Height = Mathf.MoveTowards(vp.Height, 0f, dt * 6f);
                vp.FaceRight = _fwd < 0f;
                _p.Pose = vp;
            }
            else if (_contact == Contact.Hit && VictimOk() && !Moves.Busy(_v))
            {
                // one that couldn't be frozen (too heavy or too big to hold) is held where it stands
                // for the beat: no swing, no walking off (the player's own unit only can't swing)
                Combat.Pin(_v, 0.3f);
                if (!ControllableUnit.isControllingUnit(_v))
                {
                    _v.makeWait(0.3f);
                }
            }
            if (Level >= 3 && Visible)
            {
                // a breath of wind along the cut
                _windT -= dt;
                if (_windT <= 0f)
                {
                    _windT = 0.1f;
                    Vector2 mid = Vector2.Lerp(BodyPos(), VBody(), 0.5f) + new Vector2(0f, Random.Range(-3f, 4f) * px);
                    Kit.SpeedLines(mid, _dir, px, 2, 8f, 0.25f, new Color(1f, 1f, 1f, 0.35f));
                }
            }
            // an evaded draw cut an afterimage: there is no back-to-back silence to hold, the enemy is
            // already somewhere else (the God duel held 0.34 s of empty air, then a full sheathe)
            if (_pt >= (_contact == Contact.Evaded ? Mathf.Min(_tHold, TEmpty) : _tHold))
            {
                if (_contact == Contact.Hit && VictimOk() && !HeldOk() && !StillThere())
                {
                    // never frozen, it got away through the hold (took off, dashed, was walked off):
                    // the blade cut where it stood, an afterimage
                    _contact = Contact.Evaded;
                }
                if (_contact == Contact.Hit && VictimOk())
                {
                    Cut();
                }
                else
                {
                    _glintEnd = true;   // nothing there to cut: an afterimage, or it was taken
                    if (_contact == Contact.Evaded)
                    {
                        CutAir(px);
                    }
                    if (Dev.On) Dev.Trace("sever " + Id + " nothing to cut (" + (_contact == Contact.Evaded ? "evaded" : _v == null ? "no target" : _v.Sid() != _vid ? "recycled" : !_v.isAlive() || !_v.hasHealth() ? "already dead" : "taken from it") + ")");
                }
                if (Level >= 3 && _lineSet)
                {
                    Scar();   // the blade went on into the ground: it splits on the same frame
                }
                Next(Phase.Sheathe);
            }
        }

        // ---------------------------------------------------------------- cut --

        /// <summary>The cut lands. Executes a weakened enemy (and, at III, anything far below the unit's class).</summary>
        private void Cut()
        {
            float px = Px;
            bool held = HeldOk();
            Vector2 body = VBody();
            if (held)
            {
                _p.Flash(Level >= 2 ? 0.07f : 0.05f);
            }
            HitResult r = Combat.Hit(A, _v, _mult * _cm, true, !held, 0f, this);
            bool exec = false;
            if (_v.isAlive() && _v.hasHealth() && Duels.TechShare(A, _v) >= 1f
                && (Combat.HpPct(_v) <= Threshold(Level, A, _v) || Level >= 3 && FarBelow(A, _v)))
            {
                exec = true;
                Execute();
            }
            _killed = !_v.hasHealth();
            if (_killed)
            {
                Executed++;
            }
            Anim.Sound(_armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch", _v.current_tile);
            if (Level >= 3)
            {
                Anim.Sound("event:/SFX/HIT/HitGeneric", _v.current_tile);
            }
            if (Kit.Visible(body))
            {
                CutFx(body, px, r.Crit);
            }
            if (Level == 2)
            {
                _hold = 0.05f;
            }
            else if (Level >= 3)
            {
                Kit.Stop(body, 0.1f);
            }
            if (_killed || exec)
            {
                StartSplit(held, body);
            }
            else
            {
                Reel(held);
            }
            if (Dev.On) Dev.Trace("sever " + Id + " cut x" + (_mult * _cm).ToString("F1") + " -> " + _v.asset.id + " hp " + _v.getHealth() + (exec ? " EXECUTE" : "") + (_killed ? " killed" : "") + (r.Crit ? " CRIT" : ""));
        }

        /// <summary>
        /// The execution: enough to end it through any armor. An invincible unit shrugs it off
        /// (the game ignores the damage): it is split, then gets back up.
        /// </summary>
        private void Execute()
        {
            float armor = Mathf.Min(_v.stats["armor"], 95f);
            float through = Mathf.Max(0.05f, 1f - armor / 100f);
            // Combat.Hit multiplies it by the unit's boosts and Might and the Power setting: a low
            // Power setting must not let the execution fall short (cut in two, it got back up)
            TierInfo ti = Tiers.Info(Tiers.Of(A));
            float boost = Mathf.Max(0.05f, Fighters.DamageMult(A) * (ti != null ? ti.Might : 1f) * Settings.Power);
            float flat = ((_v.getHealth() + 2f) / through + 4f) / Mathf.Min(1f, boost);
            Combat.Hit(A, _v, 0f, true, false, flat, this);
        }

        private void CutFx(Vector2 body, float px, bool crit)
        {
            Color edge = Level <= 1 ? new Color(0.86f, 0.9f, 0.96f, 1f) : _pal.Edge;
            // the slash mark across the waist, rising a little the way the unit went: the line the
            // body parts along (an iaido draw cuts flat, not from the shoulder)
            Vector2 cd = CutDir();
            float len = 5f * _vpx * Mathf.Max(1f, _vPivot / 3.6f);
            Kit.Line(body - cd * len, body + cd * len, px, 0.18f, 1, FxLayer.Top, 32, White, Fx.WithA(edge, 0.8f), Fx.WithA(edge, 0f));
            switch (Level)
            {
                case 1:
                    Kit.Sparks(body, px, 6, 50f, 110f, edge);
                    break;
                case 2:
                    // a short white pop (a 6-px disk hid the whole enemy for the cut's frame)
                    Kit.Flash(body, px, Mathf.RoundToInt(4f * _k), 0.05f, White);
                    Kit.Sparks(body, px, 8, 60f, 130f, edge);
                    if (crit)
                    {
                        Kit.ImpactFrame(body, px, 0.4f * _k, _pal, true);
                    }
                    break;
                default:
                    {
                        // a local black flash around the impact frame
                        Particle ink = Fx.Spawn(Art.Disk(Mathf.RoundToInt(10f * _k), 0.85f, 0.35f), body, px, 0.1f, FxLayer.Top, 29);
                        ink?.Sized(0.8f, 1.3f).Color(Ink, Fx.WithA(Ink, 0.85f), Fx.WithA(_pal.Edge, 0f));
                        Kit.ImpactFrame(body, px, 0.55f * _k, _pal, crit);
                        Kit.Sparks(body, px, 12, 70f, 160f, edge);
                        Kit.Shake(body, 0.22f, 0.008f, _k);
                        Vector2 feet = _v != null && _v.Sid() == _vid ? _v.current_position : _lastSeen;
                        Kit.Line(feet - _dir * 0.6f, feet + _dir * 0.6f, px, 0.9f, 1, FxLayer.Ground, 5, White, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                        break;
                    }
            }
        }

        /// <summary>
        /// Evaded: the blade cuts where the enemy stood (its afterimage): a thin slash across the
        /// empty spot and a breath of dust, no sparks (nothing was there to hit).
        /// </summary>
        private void CutAir(float px)
        {
            if (!Kit.Visible(_lastSeen))
            {
                return;
            }
            Vector2 at = new Vector2(_lastSeen.x, _lastSeen.y + _vPivot * _vpx);
            Vector2 cd = CutDir();
            float len = 5f * _vpx * Mathf.Max(1f, _vPivot / 3.6f);
            Color edge = Level <= 1 ? new Color(0.86f, 0.9f, 0.96f, 1f) : _pal.Edge;
            Kit.Line(at - cd * len, at + cd * len, px, 0.14f, 1, FxLayer.Top, 32, Fx.WithA(White, 0.8f), Fx.WithA(edge, 0.5f), Fx.WithA(edge, 0f));
            Fx.LandingPuff(_lastSeen, _vpx, Combat.TileAt(_lastSeen));
        }

        /// <summary>
        /// III: the cut goes on into the ground. On the frame it lands the earth splits open along the
        /// whole line of the draw (6 tiles; a God's 9, wider): force 1, 2 from Master, a God's burns.
        /// One bite a tile, clods thrown up along it; the unit's own feet are spared (it stands on the
        /// line, back to back with its enemy), and the stretch across water is left alone.
        /// </summary>
        private void Scar()
        {
            float len = Vector2.Distance(_start, _lineEnd);
            if (len < 0.5f)
            {
                return;
            }
            int force = SingleKit.Force(_tier, 1, 2);
            float r = SingleKit.Radius(_tier, 1f);
            int n = Mathf.Clamp(Mathf.CeilToInt(len), 1, 14);
            for (int i = 0; i <= n; i++)
            {
                Vector2 at = Vector2.Lerp(_start, _lineEnd, i / (float)n);
                SingleKit.Ruin(A, Pose.Ground, at, r, force);
                if ((i & 1) == 1)
                {
                    WorldTile t = Combat.TileAt(at);
                    if (t != null && Kit.Visible(at))
                    {
                        Kit.Debris(at, Px, t, 2, _k);
                    }
                }
            }
            if (Dev.On) Dev.Trace("sever " + Id + " splits the ground for " + len.ToString("F1") + " tiles (force " + force + ")");
        }

        /// <summary>It survived the cut: it reels (a puppet for a beat), then staggers.</summary>
        private void Reel(bool held)
        {
            if (held)
            {
                _reelT = 0f;
                return;
            }
            if (_v.isAlive())
            {
                Combat.Stagger(_v, 0.5f);
                SetLink(Link.Down);
            }
        }

        private void ReelTick(float dt)
        {
            if (_reelT < 0f)
            {
                return;
            }
            _reelT += dt;
            if (!HeldOk())
            {
                _reelT = -1f;
                return;
            }
            float k = Mathf.Clamp01(_reelT / (TReel * _beat));
            float amp = 1f - Anim.OutQuad(k);
            Pose vp = _p.Pose;
            vp.Hidden = false;
            vp.Rot = -16f * amp;
            vp.SX = 1f + 0.08f * amp;
            vp.SY = 1f - 0.18f * amp;
            vp.Nudge = Vector2.zero;
            if (_fallen)
            {
                // cut where it lay: it struggles up through the reel (the game stands a unit it is
                // handed back upright, so it must be up by then, not snap there)
                float f = 1f - Anim.OutQuad(k);
                vp.Rot = Mathf.Lerp(vp.Rot, _fallRot, f);
                vp.Nudge = _fallNudge * f;
            }
            _p.Pose = vp;
            if (k >= 1f)
            {
                _reelT = -1f;
                LetGo(true);
                if (_v.isAlive())
                {
                    Combat.Stagger(_v, 0.5f);
                    SetLink(Link.Down);
                }
            }
        }

        // -------------------------------------------------------------- split --

        /// <summary>
        /// The enemy falls in two: its sprite is cut at the waist into a real upper and lower half
        /// (two sprites over the same texture), drawn where the body was. (Two whole copies offset
        /// by a pixel read as a ghostly double image, not as a body cut in two.) A sprite that can't
        /// be cut (packed in an atlas) falls back to the two half-alpha copies.
        /// </summary>
        private void StartSplit(bool held, Vector2 body)
        {
            _splitT = 0f;
            _splitStep = new Vector2(_fwd >= 0f ? 1f : -1f, 1f) * _vpx;
            if (held)
            {
                _splitAt = _p.DrawPos();   // where the sprite really is (DrawPos carries a hovering unit's float)
                _splitEuler = Fx.UnitEuler(_p);
                _splitScale = new Vector2(_v.actor_scale * _p.Pose.SX, _v.actor_scale * _p.Pose.SY);
                _p.Pose.Hidden = true;
            }
            else
            {
                _splitAt = new Vector2(_v.current_position.x + _v.move_jump_offset.x, _v.current_position.y + _v.move_jump_offset.y + _v.position_height);
                _splitEuler = _v.current_rotation;
                _splitScale = new Vector2(_v.current_scale.x, _v.current_scale.y);
            }
            // a corpse stays hidden while its halves slide apart
            _corpseHidden = _killed;
            _realHalves = false;
            Sprite s = Fx.UnitSprite(_v);
            if (s != null && Kit.Visible(body))
            {
                // one white frame of the whole body, then the halves
                if (Fx.HasSilhouette)
                {
                    Particle flash = Fx.Spawn(s, _splitAt, 1f, 0.07f, FxLayer.Over, 4, true);
                    if (flash != null)
                    {
                        flash.UseEuler = true;
                        flash.Euler = _splitEuler;
                        flash.SX = _splitScale.x;
                        flash.SY = _splitScale.y;
                        flash.Color(White, White, Fx.WithA(White, 0f));
                    }
                }
                _realHalves = MakeHalves(s, out _topS, out _botS, out float lift);
                if (_realHalves)
                {
                    _cutLift = lift * _splitScale.y;
                    _halfA = Half(_topS);
                    _halfB = Half(_botS);
                }
                else
                {
                    _halfA = Half(s);
                    _halfB = Half(s);
                }
                // a burst of sparks along the cut
                Vector2 cd = CutDir();
                Color edge = Level <= 1 ? new Color(0.86f, 0.9f, 0.96f, 1f) : _pal.Edge;
                for (int i = 0; i < Fx.Count(6); i++)
                {
                    float side = (i & 1) == 0 ? 1f : -1f;
                    Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, 2)], body + cd * Random.Range(-2f, 2f) * Px, Px, Random.Range(0.12f, 0.2f), FxLayer.Top, 8);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving(cd * side * Random.Range(50f, 100f) * Px, 0f, 6f);
                    p.FaceVel = true;
                    p.Color(White, edge, Fx.WithA(edge, 0f));
                }
            }
            if (Dev.On) Dev.Trace("sever " + Id + " splits " + _v.asset.id + "#" + _vid + (_realHalves ? " (halves)" : " (copies)") + (_killed ? "" : ", it gets back up"));
        }

        /// <summary>Where the body parts: across the waist, rising a little the way the unit went.</summary>
        private Vector2 CutDir()
        {
            return new Vector2(_fwd >= 0f ? 1f : -1f, 0.3f).normalized;
        }

        /// <summary>
        /// Cuts a unit sprite at the waist into two sprites over the same texture: 'top' pivots on the
        /// cut line, 'bottom' keeps the original pivot (both line up with the whole sprite when drawn
        /// with its transform). 'lift': sprite units from the original pivot up to the cut. False if
        /// it can't be cut; whatever was made is destroyed.
        /// </summary>
        private static bool MakeHalves(Sprite s, out Sprite top, out Sprite bottom, out float lift)
        {
            top = bottom = null;
            lift = 0f;
            try
            {
                if (s == null || s.texture == null || s.packed)
                {
                    return false;
                }
                Rect r = s.rect;
                int h = Mathf.RoundToInt(r.height);
                if (h < 5 || r.width < 2f)
                {
                    return false;
                }
                int cut = Mathf.Clamp(Mathf.RoundToInt(h * CutK), 2, h - 2);
                Vector2 pv = s.pivot;   // texels from the rect's bottom-left
                float ppu = s.pixelsPerUnit > 0f ? s.pixelsPerUnit : 1f;
                bottom = Sprite.Create(s.texture, new Rect(r.x, r.y, r.width, cut), new Vector2(pv.x / r.width, pv.y / cut), ppu, 0u, SpriteMeshType.FullRect);
                top = Sprite.Create(s.texture, new Rect(r.x, r.y + cut, r.width, h - cut), new Vector2(pv.x / r.width, 0f), ppu, 0u, SpriteMeshType.FullRect);
                lift = (cut - pv.y) / ppu;
                if (bottom == null || top == null)
                {
                    Discard(ref top);
                    Discard(ref bottom);
                    return false;
                }
                return true;
            }
            catch
            {
                Discard(ref top);
                Discard(ref bottom);
                return false;
            }
        }

        private static void Discard(ref Sprite s)
        {
            if (s != null)
            {
                Object.Destroy(s);
            }
            s = null;
        }

        private Particle Half(Sprite s)
        {
            Particle p = Fx.Spawn(s, _splitAt, 1f, 1f, FxLayer.Over, 3, false);
            if (p == null)
            {
                return null;
            }
            p.Held = true;
            p.UseEuler = true;
            p.Euler = _splitEuler;
            p.SX = _splitScale.x;
            p.SY = _splitScale.y;
            p.Color(new Color(1f, 1f, 1f, _realHalves ? HalfAlpha : 0.6f));
            return p;
        }

        /// <summary>
        /// Real halves: at 0.1 / 0.2 s the upper half slides a texel, then two, along the cut (crisp
        /// steps, no sub-pixel blur). Killed: a third texel, and it topples forward off the legs, both
        /// halves fading as the body drops behind them. Immortal: it slides back and the unit gets
        /// back up. The fallback copies keep the old 2-px diagonal slide and quick fade.
        /// </summary>
        private void Split(float dt)
        {
            _splitT += dt;
            float t = _splitT / _beat;   // the same beats, a little quicker from Master up
            int slide = t >= 0.2f ? 2 : t >= 0.1f ? 1 : 0;
            float alpha = _realHalves ? HalfAlpha : 0.6f;
            float tip = 0f, drop = 0f;
            float end = TSplit + (_killed && _realHalves ? TFall : TSplitEnd);
            if (t >= TSplit)
            {
                if (_killed)
                {
                    float f = Mathf.Clamp01((t - TSplit) / (end - TSplit));
                    if (_realHalves)
                    {
                        slide = f >= 0.25f ? 3 : 2;
                        float fall = Anim.InQuad(Mathf.Clamp01(f / 0.7f));
                        tip = 80f * fall;
                        drop = _cutLift * 0.85f * fall;
                        alpha = HalfAlpha * (1f - Mathf.Clamp01((f - 0.55f) / 0.45f));
                        if (f >= 0.55f && _corpseHidden)
                        {
                            UnhideCorpse();   // the body takes over as the halves fade
                        }
                    }
                    else
                    {
                        if (_corpseHidden)
                        {
                            UnhideCorpse();
                        }
                        slide = f >= 0.4f ? 3 : 2;
                        alpha = 0.6f * (1f - f);
                    }
                }
                else
                {
                    slide = t - TSplit < 0.07f ? 1 : 0;
                }
            }
            if (_realHalves)
            {
                PlaceTop(slide, tip, drop, alpha);
                SetHalf(_halfB, _splitAt, alpha);
            }
            else
            {
                int a = slide >= 3 ? 2 : Mathf.Min(slide, 1);
                int b = slide >= 2 ? 1 : 0;
                SetHalf(_halfA, _splitAt + _splitStep * a, alpha);
                SetHalf(_halfB, _splitAt - _splitStep * b, alpha);
            }
            if (t >= end)
            {
                KillHalves();
                _splitT = -1f;
                if (_corpseHidden)
                {
                    UnhideCorpse();
                }
                if (!_killed)
                {
                    // immortal: whole again, it reels and gets back up
                    if (HeldOk())
                    {
                        _p.Pose.Hidden = false;
                        _p.Flash(0.06f);
                    }
                    Reel(HeldOk());
                }
            }
        }

        /// <summary>Seconds left of the split (0 if none is playing).</summary>
        private float SplitLeft()
        {
            if (_splitT < 0f)
            {
                return 0f;
            }
            float end = TSplit + (_killed && _realHalves ? TFall : TSplitEnd);
            return Mathf.Max(0f, end * _beat - _splitT);
        }

        /// <summary>The upper half: on the cut line, 'slide' texels along it, toppling 'tip' degrees forward and 'drop' lower.</summary>
        private void PlaceTop(int slide, float tip, float drop, float alpha)
        {
            Particle p = _halfA;
            if (p == null || !p.Alive)
            {
                return;
            }
            // the sprite is mirrored (y 180) when it faces right: its z rotation turns the other way on screen
            bool mirror = Mathf.Abs(Mathf.DeltaAngle(_splitEuler.y, 180f)) < 1f;
            float z = _splitEuler.z * Mathf.Deg2Rad;
            Vector2 up = new Vector2(mirror ? Mathf.Sin(z) : -Mathf.Sin(z), Mathf.Cos(z));
            float fwd = _fwd >= 0f ? 1f : -1f;
            p.Pos = _splitAt + up * _cutLift + new Vector2(fwd * slide * _vpx, -drop);
            p.Z = 0f;
            p.Euler = new Vector3(_splitEuler.x, _splitEuler.y, _splitEuler.z + (mirror ? fwd : -fwd) * tip);
            p.Grad = null;
            p.R.color = new Color(1f, 1f, 1f, alpha);
        }

        private static void SetHalf(Particle p, Vector2 pos, float alpha)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = pos;
            p.Grad = null;
            p.R.color = new Color(1f, 1f, 1f, alpha);
        }

        private void KillHalves()
        {
            _halfA?.Kill();
            _halfB?.Kill();
            _halfA = _halfB = null;
            Discard(ref _topS);
            Discard(ref _botS);
        }

        /// <summary>The body shows again (it falls the game's way). A body still in our puppet is shown when it is let go.</summary>
        private void UnhideCorpse()
        {
            if (!_corpseHidden)
            {
                return;
            }
            _corpseHidden = false;
            if (_v != null && _v.Sid() == _vid && !HeldOk())
            {
                _v.current_scale = new Vector3(_v.actor_scale, _v.actor_scale, 1f);
            }
        }

        /// <summary>
        /// Right before drawing: a dead (or dying: the game buries its dead a frame later) body stays
        /// hidden while its halves are on screen.
        /// </summary>
        public override void Frame()
        {
            if (_corpseHidden && _v != null && _v.Sid() == _vid && (!_v.isAlive() || !_v.hasHealth()) && !HeldOk())
            {
                _v.current_scale = Vector3.zero;
            }
        }

        // ------------------------------------------------------------ sheathe --

        private void Sheathe(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _rotFrom = Pose.Rot;
                _sxFrom = Pose.SX;
                _syFrom = Pose.SY;
                // halves still parting: the unit doesn't stand and wait for them, it straightens
                // through the whole fall and the blade clicks home as the upper half drops
                float left = SplitLeft();
                _sheatheFor = Mathf.Max(_tSheathe, left);
                _clickAt = left > _tSheathe ? Mathf.Max(0.12f * _snap, left - 0.08f) : 0.12f * _snap;
                if (_contact == Contact.Evaded)
                {
                    // it cut an afterimage: it snaps round to where the enemy really is, blade
                    // still out, and is gone into its next action (no ceremony for a miss)
                    _sheatheFor = 0.6f * _tSheathe;
                    _clickAt = 0.35f * _sheatheFor;
                    if (_v != null && _v.Sid() == _vid && _v.isAlive())
                    {
                        float dx = _v.current_position.x - Pose.Ground.x;
                        if (Mathf.Abs(dx) > 0.1f && dx > 0f != _fwd > 0f)
                        {
                            _fwd = -_fwd;
                            Pose.FaceRight = _fwd > 0f;
                            _rotFrom = -4f;   // caught leaning the wrong way: a small recoil as it turns
                        }
                    }
                }
            }
            float amp = Anim.Spring(_pt / _snap * (_tSheathe / Mathf.Max(0.01f, _sheatheFor)), 8f, 14f);
            Pose.Ground = _drawTo;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.Rot = _rotFrom * amp;
            Pose.SX = 1f + (_sxFrom - 1f) * amp;
            Pose.SY = 1f + (_syFrom - 1f) * amp;
            if (!_sheathed && _pt >= _clickAt)
            {
                _sheathed = true;
                if (Visible)
                {
                    if (_armed || _glintEnd)
                    {
                        // click: the blade is home
                        Fx.Glint(_glintEnd ? HiltPos() + new Vector2(0f, 1.5f * px) : SheathPos(), px, 0.18f, _pal.Core, _glintEnd ? 0.9f : 0.6f);
                    }
                    else
                    {
                        // a bare hand flicked clean
                        Kit.Swish(this, Art.Arc(5, 0.6f, 40f, -60f, 1), 0.1f, new Vector2(_fwd * 1.5f, -1f), _fwd, new Color(1f, 1f, 1f, 0.7f), new Color(0.86f, 0.9f, 0.96f, 0.5f));
                    }
                }
            }
            ReelTick(dt);
            if (_pt >= _sheatheFor && _splitT < 0f && _reelT < 0f)
            {
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                LetGo(true);
                SingleKit.Cooldown(A, _tier);
                Finished = true;
            }
        }

        // ------------------------------------------------- answered: parried --

        /// <summary>The draw was met: the unit lands in front of the enemy and reels back, open.</summary>
        private void Broken(float dt)
        {
            if (Enter())
            {
                _from = Pose.Ground;
                _to = _from - _dir * 0.35f * _k;
                if (!Combat.SafeSpot(_to))
                {
                    _to = _from;
                }
                if (Visible)
                {
                    Fx.LandingPuff(_from, Px, A.current_tile);
                }
            }
            float k = Mathf.Clamp01(_pt / _tBroken);
            Pose.Hidden = false;
            Pose.FaceRight = _fwd > 0f;
            Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
            float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.25f));
            Pose.Rot = -18f * lean;
            Pose.SX = 1f - 0.06f * lean;
            Pose.SY = 1f + 0.06f * lean;
            Pose.Height = 0f;
            Pose.Nudge = k < 0.5f ? new Vector2(((int)(_pt * 50f) & 1) == 0 ? 1f : -1f, 0f) : Vector2.zero;
            if (k >= 1f)
            {
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Pose.Nudge = Vector2.zero;
                SingleKit.Cooldown(A, _tier);
                Finished = true;
            }
        }

        // --------------------------------------------------------------- lost --

        /// <summary>Nothing to draw on: the unit lets the stance go, with a glint.</summary>
        private void Lost(float dt)
        {
            if (Enter())
            {
                _rotFrom = Pose.Rot;
                _sxFrom = Pose.SX;
                _syFrom = Pose.SY;
                Pose.Hidden = false;
                if (Visible)
                {
                    Fx.Glint(HiltPos() + new Vector2(0f, 1.5f * Px), Px, 0.2f, _pal.Core, 0.8f);
                }
            }
            float amp = Anim.Spring(_pt / _snap, 9f, 16f);
            Pose.Rot = _rotFrom * amp;
            Pose.SX = 1f + (_sxFrom - 1f) * amp;
            Pose.SY = 1f + (_syFrom - 1f) * amp;
            Pose.Nudge = Vector2.zero;
            Pose.Height = 0f;
            if (_pt >= _tLost)
            {
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                LetGo(true);
                Finished = true;
            }
        }

        // ------------------------------------------------------------ helpers --

        private void LetGo(bool onGround)
        {
            if (_p == null || _released)
            {
                return;
            }
            if (!_p.Finished)
            {
                _p.Pose.Hidden = false;
                _p.Release(onGround);
            }
            _released = true;
        }

        private void SetLink(Link link)
        {
            if (_v != null && _v.isAlive() && _v.hasHealth() && _v.Sid() == _vid)
            {
                EndLink = link;
                EndTarget = _v;
            }
        }

        public override void Abort()
        {
            LetGo(true);
            KillHalves();
            UnhideCorpse();
        }

        public override void OnEnd(bool aborted)
        {
            LetGo(true);
            KillHalves();
            UnhideCorpse();
        }
    }

    /// <summary>
    /// Severing Flash as a technique. Entry: Master. The AI's "isolated enemy, instant kill": an
    /// enemy with no friends within 3 tiles, 1.5-4 tiles away (or the target of a link), on the
    /// ground, already low enough that the cut executes it (or the cut alone would kill it), with
    /// safe ground just past it and no wall in the way. Invincible units are not worth it.
    /// </summary>
    internal sealed class SeveringFlashTech : Technique
    {
        public override string Id => "severing_flash";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Execute;

        /// <summary>A stance (Charge: the moment to break it), then the draw: already behind you.</summary>
        public override Form Forms => Form.Blade | Form.Charge | Form.Dash | Form.Fast | Form.Straight;

        public override Scale Scale => Scale.Heavy;

        public override float[] ManaCost => new[] { 18f, 30f, 45f };
        public override float[] Cooldown => new[] { 10f, 9f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
            if (c.Forced)
            {
                Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                return Consider(c, v, level, k);
            }
            Plan best = null;
            if (c.Linked && c.LinkTarget != null)
            {
                best = Consider(c, c.LinkTarget, level, k);
            }
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > SeveringFlash.MaxRange)
                {
                    break;
                }
                if (c.EnemyDist[i] < SeveringFlash.MinRange || c.Enemies[i] == c.LinkTarget)
                {
                    continue;
                }
                Plan p = Consider(c, c.Enemies[i], level, k);
                if (p != null && (best == null || p.Score > best.Score))
                {
                    best = p;
                }
            }
            return best;
        }

        private static Plan Consider(Ctx c, Actor v, int level, float k)
        {
            if (v == null || !v.isAlive() || Moves.Locked(v) || !HundredFists.Hopping(v) || !c.Allowed(v, out float cm))
            {
                return null;
            }
            float d = c.Dist(v);
            bool link = c.Linked && v == c.LinkTarget;
            if (c.Forced)
            {
                if (d > SeveringFlash.MaxRange + 2f)
                {
                    return null;
                }
            }
            else
            {
                // a link may draw from closer (the enemy is reeling right there), but not from arm's
                // length: a "dash" of half a tile through someone reads as a stub of light between two
                // bodies (war_team: a draw from 0.5 tile, parried, was a red bar between them)
                if (link ? d < SeveringFlash.MinLinkRange || d > SeveringFlash.MaxRange + 0.5f : d < SeveringFlash.MinRange || d > SeveringFlash.MaxRange)
                {
                    return null;
                }
                if (v.is_invincible || !c.Isolated(v, 3f))
                {
                    return null;
                }
            }
            float hp = v.getHealth();
            float max = Mathf.Max(1f, v.getMaxHealth());
            float e = c.Expected(v, SeveringFlash.Mult(level)) * cm;
            // a duel opponent is never executed (the cut skips it, and only the duel's Final can end
            // a duel): it is worth what the cut does, not a kill (an execution it could not perform
            // scored 2377 in the god duel and won the plan every time it was ready)
            bool duel = Duels.TechShare(c.Self, v) < 1f;
            bool kills = !duel && (e >= hp || (hp - e) / max <= SeveringFlash.Threshold(level, c.Self, v) || level >= 3 && SeveringFlash.FarBelow(c.Self, v));
            if (!kills && !c.Forced && !duel)
            {
                return null;
            }
            Vector2 dir = v.current_position - c.Pos;
            dir = dir.sqrMagnitude > 0.0004f ? dir.normalized : new Vector2(c.Self.flip ? 1f : -1f, 0f);
            if (!SeveringFlash.FindEnd(c.Pos, v.current_position, dir, SeveringFlash.StandoffK * k, out Vector2 end))
            {
                return null;
            }
            float score = c.Value(v, kills ? hp / Mathf.Max(0.05f, cm) + 1f : c.Expected(v, SeveringFlash.Mult(level)), cm);
            if (kills || c.Forced)
            {
                score += 30f + (Tiers.Of(v) != Tier.None ? 15f : 0f);   // the lone straggler: an execution is the answer
            }
            else
            {
                score += 20f;   // a duel: one draw, back to back, a beat of silence (it can't end it)
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan { Target = v, Point = end, Dir = dir, Count = 1, Score = score };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && SeveringFlash.Begin(a, p.Target, p.Level, this);
        }
    }
}
