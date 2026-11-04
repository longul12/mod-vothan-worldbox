using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// CRESCENT FANG. A blade swung (or a leg kicked) so fast that the cut leaves the body and
    /// travels: a crescent wave skims the ground along a line and cuts whatever stands on it.
    /// Armed units throw a thin, sharp blade wave; unarmed ones (and beasts) a rounder, airy kick wave.
    ///
    /// Beat sheets (game seconds at x1):
    ///
    ///   I  "Crescent Fang" (one small white crescent, 0.6 tile, 120 degrees, 4 tiles, stops on the first enemy, x1)
    ///     WIND     0.10  twist back (-18), a little squash; blade: a white "ting" on the edge
    ///     SWING    0.08  snap forward through a smear (+26, stretched), a short lunge, white swoosh,
    ///                    the crescent leaves at 0.03
    ///     TRAVEL  ~0.3   14 tiles/s, white streaks, a puff of dust every half tile, its shadow under it;
    ///                    on the first enemy it shatters into a hit star: x1 and a flinch
    ///     RECOVER  0.16  follow-through settles, springs upright
    ///
    ///   II "Crescent Cross" (an X of two crescents, piercing, 8 tiles at 16 tiles/s, x2 to everyone on it)
    ///     0.00-0.12  twist (-25) + glint in the tier colour
    ///     0.12-0.18  swing 1 (blade: a falling diagonal slash / kick: a roundhouse) releases A (+45 tilt),
    ///                which drifts forward, waiting for its twin
    ///     0.18-0.22  recoil (blade) / the spin of a back kick (kick)
    ///     0.22-0.28  swing 2 releases B (-45 tilt) right onto A: the X locks with a flash
    ///     0.28-0.78  the X flies 16 tiles/s: tier afterimages, speed lines, a dust streak under it
    ///                (a spray wake over water). Each enemy: it bites (0.04 s local hold), spark,
    ///                flash, a second cut 0.05 s later and a 0.5-tile knockback
    ///     at range   both fade with a small spark burst; a wall: they crash into it in a shower of grit;
    ///                a foe too heavy for this tier: the X breaks on it with a clang
    ///
    ///   III "Moonfall Fang" (one 1.5-tile vertical crescent, 14 tiles at 20 tiles/s (Grandmaster 24, God 27
    ///       and 18 tiles), everyone it passes is "already cut", then bursts at once x4.5)
    ///     CHARGE   0.30  a deep stance; focus lines converge, the unit's sprite pulses in the tier colour,
    ///                    a tier ring closes in on the feet, the path lights up (telegraph), power tremble
    ///     RELEASE  0.12  the vertical slash (blade) or axe kick (kick): stretch, smear, a big vertical
    ///                    swoosh, flash, shockwave, shake; the moon rises off the blade
    ///     TRAVEL  ~0.7   white core, tier edge; it scores a thin glowing cut (cools in ~3 s, groove 9 s) as it goes; everyone it
    ///                    passes freezes in place (a Puppet) with a white outline flash and a cut mark
    ///     STILLNESS      the attacker holds its follow-through, perfectly still, until the moon is past the
    ///                    last enemy on its line (seen at the release) and the tier's beat is over: 0.8 s
    ///                    untiered, 0.38 Grandmaster, 0.34 God (the moon itself may still be flying on)
    ///     SHEATHE  0.12  it straightens, the sheathe glint ("click"), the frozen flash their outline in the
    ///                    tier colour
    ///     BURST   +0.05  every cut enemy bursts at once: starbursts, x4.5, the furrow flashes, impact
    ///                    frame (black on a crit), global hit-stop 0.1, 1-tile knockback off the line
    ///     RECOVER  0.30  springs back upright (at the tier's tempo: a God's in 0.13)
    ///     A whiff (no one cut: they slipped it) skips the stillness: a breath, the click, and on.
    ///     The moon that flies on after the burst is spent: whoever it still passes is cut and bursts at once
    ///     (no freeze), and it tears the ground open right behind itself.
    ///
    /// Waves stop at walls and mountains (they crash into them) and skim over water and lava. The wave
    /// is simulated by the move itself (mechanics run the same on or off screen, with or without
    /// particles); its sprites only follow it. Hits are swept between ticks, so nothing is skipped at x20.
    ///
    /// The land takes the cut (Kit.Ravage): I leaves it be; the X scorches a line along its whole path
    /// (force 1) and blasts a crater where it crashes into a wall (2); Moonfall's furrow opens into a real
    /// trench the instant everyone bursts (force 2, a God's 3 and half again as wide), on along the moon's
    /// path behind it, and its wall crash craters too. A God's cut burns: tiles along the line itself catch
    /// fire (LineKit.Ignite; never a blanket of flames over the whole width).
    ///
    /// Flow tiers (Master and up) never stand and watch their wave: after the swing's follow-through they
    /// spring back at their tier's tempo and the cut flies on by itself (LineKit.Linger), so the Brain chains
    /// on at once (Scattered when the X was thrown down a line of two or more). Moonfall keeps its stillness and
    /// sheathe (the iai beat) but the higher the tier the faster the moon and the tighter the stillness.
    /// </summary>
    internal sealed class CrescentFang : Move, LineKit.ILinger
    {
        public const float Width = 0.6f;
        /// <summary>A God's Moonfall flies this much further.</summary>
        public const float GodReach = 1.3f;

        /// <summary>How far each level's wave travels (tiles).</summary>
        public static float RangeOf(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 8f : 14f;
        }

        /// <summary>How far it travels for this performer (a God's Moonfall 30% further).</summary>
        public static float RangeOf(int level, bool god)
        {
            return RangeOf(level) * (god && level >= 3 ? GodReach : 1f);
        }

        /// <summary>Moonfall's burst (x per enemy cut).</summary>
        public const float MoonMult = 4.5f;

        private enum Phase
        {
            Wind,
            Swing,
            Follow,
            Sheathe,
            Recover
        }

        private enum End
        {
            Range,
            Wall,
            Shatter
        }

        private const float SpeedI = 14f, SpeedII = 16f, SpeedIII = 20f, Drift = 2.4f;
        private const float BurstDelay = 0.85f, SheatheDelay = 0.8f;
        private const int MaxPuppets = 12;

        private static readonly Color AirEdge = new Color(0.8f, 0.92f, 1f, 1f);

        private readonly Vector2 _from;
        private Vector2 _dir;
        private float _fwd;
        private readonly bool _kick;
        private readonly float _k;
        private readonly Palette _pal;
        private readonly bool _lone;
        private Actor _target;
        private long _targetId;
        private Weight _targetClass;
        private Vector2 _targetLast;

        private Phase _phase = Phase.Wind;

        /// <summary>Moonfall Fang's deep stance (III) gathers the moon: a quick blow now breaks it.</summary>
        public override bool Charging => Level >= 3 && _phase == Phase.Wind && !Finished;
        private float _pt;
        private bool _entered;
        private readonly float _tWind, _tSwing, _tRecover, _recoverDur;
        private readonly bool _flow, _god;
        private readonly float _range, _speedK, _sheatheDelay;
        private readonly int _count;
        // the real damage to the land (Moonfall's trench, a wall crash)
        private readonly int _tearForce;
        private readonly float _tearR;
        private float _wRavAt, _wEndT = -1f;
        // Moonfall: how far along the line the last enemy stood at the release (the stillness lasts until the
        // moon is past it), whether the cut is spent (the burst came: whatever it touches now bursts at once),
        // and how far the trench has been torn open
        private float _lastAlong;
        private bool _late, _opened;
        private float _trenchTo;
        private bool _detached;
        private int _released;
        private float _releaseT = -1f;
        private float _ghost, _focus;
        private bool _glinted, _burst, _outlined, _parried, _blocked;
        private float _rotFrom, _hFrom;
        private Vector2 _groundFrom;

        // the wave
        private float _wS, _wStop, _wSpeed;
        private bool _wLive, _wFlying;
        private float _wBite;
        private int _wBites;
        private float _wTrail, _wLines, _wDustAt;
        private readonly float _hitR, _z;
        private readonly HashSet<long> _touched = new HashSet<long>();
        private readonly List<Particle> _parts = new List<Particle>();
        private readonly List<Blade> _blades = new List<Blade>();
        private readonly List<KeyValuePair<float, Actor>> _cand = new List<KeyValuePair<float, Actor>>();
        private readonly List<Pending> _pending = new List<Pending>();
        private float _r, _cosHalf;   // the crescent's radius (pixels) and the cosine of its half sweep: where its chord sits
        private float _anchor;        // flying crescents: pixels from the sprite's centre back to the wave's point
        private float _cutSound;

        /// <summary>One crescent of the wave: its art and the way its bulge points (the X's two are tilted +-45).</summary>
        private struct Blade
        {
            public Sprite[] Art;
            public Vector2 Heading;
        }

        private struct Pending
        {
            public Actor V;
            public long Id;
            public float At;
            public float Mult;
        }

        // Moonfall
        private sealed class Cut
        {
            public Actor V;
            public long Id;
            public Puppet P;
            public float CM;
            public Vector2 At;
            public float Side;
            public float Lean;
            public float Age;
            public Particle Mark;
        }

        private readonly List<Cut> _cuts = new List<Cut>();
        private readonly List<Cut> _fly = new List<Cut>();
        private int _puppets;
        private LineScar _scar;
        private Particle _glow;
        private int _hits, _survivors;
        private Actor _strongest;
        private long _strongestId;
        private float _strongestP = -1f;

        /// <summary>Waves thrown / enemies cut since the game started (dev log).</summary>
        internal static int Started, Cuts;

        private CrescentFang(Actor a, Vector2 dir, Actor target, int level, bool lone, int count) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _flow = LineKit.Flow(a);
            _god = LineKit.God(a);
            _range = RangeOf(Level, _god);
            _count = count;
            // the higher the tier, the faster the moon flies (the stillness that waits for it is shorter)
            _speedK = Level >= 3 ? (_god ? 1.35f : _flow && Tiers.Of(a) >= Tier.Grandmaster ? 1.2f : 1f) : 1f;
            // the iai stillness at the tier's tempo (0.8 s untiered, 0.44 Master, 0.38 Grandmaster, 0.34 God)
            _sheatheDelay = SheatheDelay * Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
            _tearForce = LineKit.Force(2, _god);
            _tearR = LineKit.Radius(1f, _god) * Mathf.Clamp(K, 1f, 1.5f);
            _from = a.current_position;
            _groundFrom = _from;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : (a.flip ? Vector2.right : Vector2.left);
            _fwd = Mathf.Abs(_dir.x) > 0.15f ? Mathf.Sign(_dir.x) : (a.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            _k = K;
            _kick = !Armed(a);
            _pal = Palette.For(a, Level);
            _lone = lone;
            _target = target;
            _targetId = target != null ? target.Sid() : -1;
            _targetClass = target != null ? Weights.Of(target) : Weight.Folk;
            _targetLast = target != null ? target.current_position : _from;
            _tWind = Level == 1 ? 0.1f : Level == 2 ? 0.12f : 0.3f;
            _tSwing = Level == 1 ? 0.08f : Level == 2 ? 0.16f : 0.12f;
            _tRecover = Level == 1 ? 0.16f : Level == 2 ? 0.18f : 0.3f;
            _recoverDur = LineKit.Recover(_tRecover, a);
            _hitR = (Level == 1 ? 0.45f : Level == 2 ? Width : 0.8f) * Mathf.Clamp(_k, 0.8f, 1.8f);
            // flying crescents at body height (the X, a whole body tall, a little higher so it never scrapes the ground)
            _z = Mathf.Max(Pose.PivotUp * 1.05f, Level == 2 ? 5.5f : 0f) * Px;
            _wStop = ClearLength(_from, _dir, _range);
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>Does it fight with a blade (a blade wave) or with its body (a kick wave)?</summary>
        public static bool Armed(Actor a)
        {
            try
            {
                return a.asset.is_humanoid && a.hasWeapon();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>How far a wave can travel from 'from' along 'dir' before a wall, a mountain or the map edge stops it.</summary>
        public static float ClearLength(Vector2 from, Vector2 dir, float max, float step = 0.25f)
        {
            if (World.world == null)
            {
                return 0f;
            }
            for (float s = step; s <= max + 0.001f; s += step)
            {
                Vector2 q = from + dir * s;
                if (q.x < 1f || q.y < 1f || q.x > MapBox.width - 1f || q.y > MapBox.height - 1f)
                {
                    return Mathf.Max(0f, s - step);
                }
                WorldTile t = Combat.TileAt(q);
                if (t == null || t.Type == null || t.Type.block)
                {
                    return Mathf.Max(0f, s - step);
                }
            }
            return max;
        }

        /// <summary>Is there water (or lava) on the line from 'from' along 'dir' within 'len' tiles? (A wave skims it; melee can't cross.)</summary>
        public static bool CrossesWater(Vector2 from, Vector2 dir, float len)
        {
            if (World.world == null)
            {
                return false;
            }
            for (float s = 0.5f; s < len - 0.25f; s += 0.5f)
            {
                WorldTile t = Combat.TileAt(from + dir * s);
                if (t != null && t.Type != null && (t.Type.liquid || t.Type.lava))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Starts a wave along 'dir' ('count': enemies the plan saw on the line, for the chain it leaves).</summary>
        public static bool Begin(Actor a, Vector2 dir, Actor target, int level, bool lone, int count = 0)
        {
            if (!Moves.CanPerform(a) || dir.sqrMagnitude < 1e-6f)
            {
                return false;
            }
            CrescentFang m = new CrescentFang(a, dir, target, level, lone, count);
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(m._from + m._dir * Mathf.Min(3f, m._range * 0.4f) + new Vector2(0f, 1f));
            return true;
        }

        /// <summary>The wave's point on the ground: the middle of its chord (what cuts).</summary>
        private Vector2 WavePoint => _from + _dir * _wS;

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _pt += dt;
            LaunchCut();
            Track();
            switch (_phase)
            {
                case Phase.Wind:
                    Wind(dt);
                    break;
                case Phase.Swing:
                    Swing(dt);
                    break;
                case Phase.Follow:
                    Follow(dt);
                    break;
                case Phase.Sheathe:
                    Sheathe(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
            if (_wLive)
            {
                Fly(dt);
            }
            Hits();
            if (_cuts.Count > 0)
            {
                HoldCuts(dt);
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("fang " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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
        /// Before the release, a lone target (the archer) is followed with the aim; if it dies or vanishes,
        /// the aim moves to another enemy of the same class within 2 tiles, if there is one (otherwise the cut
        /// still flies where it was aimed, and the move ends in its pose and glint). A line keeps its line, and
        /// Moonfall's stance commits to its path.
        /// </summary>
        private void Track()
        {
            if (_released > 0 || _target == null || !_lone || Level >= 3)
            {
                return;
            }
            if (_target.isAlive() && _target.Sid() == _targetId && !_target.isInMagnet())
            {
                _targetLast = _target.current_position;
                Aim(_targetLast);
                return;
            }
            Actor next = null;
            float best = 4.01f;
            foreach (Actor u in Combat.FoesAround(A, _targetLast, 2f))
            {
                float d2 = (u.current_position - _targetLast).sqrMagnitude;
                if (d2 < best && Weights.Of(u) == _targetClass && Combat.Allowed(A, u))
                {
                    best = d2;
                    next = u;
                }
            }
            _target = next;
            _targetId = next != null ? next.Sid() : -1;
            if (next != null)
            {
                Aim(next.current_position);
            }
        }

        private void Aim(Vector2 at)
        {
            Vector2 d = at - _from;
            if (d.sqrMagnitude < 0.09f)
            {
                return;
            }
            _dir = d.normalized;
            if (Mathf.Abs(_dir.x) > 0.15f)
            {
                _fwd = Mathf.Sign(_dir.x);
                Pose.FaceRight = _fwd > 0f;
            }
            _wStop = ClearLength(_from, _dir, _range);
        }

        // --------------------------------------------------------------- wind --

        private void Wind(float dt)
        {
            float px = Px;
            if (Enter())
            {
                if (Level >= 3)
                {
                    ChargeStart(px);
                }
                else if (Level == 2 && Kit.Visible(_from))
                {
                    Fx.LandingPuff(_from, px, A.current_tile);
                }
            }
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / Mathf.Min(_tWind, 0.18f)));
            Pose.Ground = _from;
            Pose.Height = 0f;
            if (Level == 1)
            {
                Pose.Rot = (_kick ? -12f : -18f) * k;
                Pose.SX = 1f + (_kick ? -0.05f : 0.08f) * k;
                Pose.SY = 1f + (_kick ? 0.06f : -0.08f) * k;
            }
            else if (Level == 2)
            {
                Pose.Rot = (_kick ? -20f : -25f) * k;
                Pose.SX = 1f + (_kick ? -0.05f : 0.08f) * k;
                Pose.SY = 1f + (_kick ? 0.06f : -0.08f) * k;
            }
            else
            {
                // the deep stance: blade drawn back low (iai), or the knee chambered high
                Pose.Rot = (_kick ? -28f : -22f) * k;
                Pose.SX = 1f + (_kick ? -0.04f : 0.1f) * k;
                Pose.SY = 1f + (_kick ? 0.08f : -0.14f) * k;
                Pose.Height = _kick ? 0.06f * _k * Anim.Smooth(0.6f, 1f, _pt / _tWind) : 0f;
                Pose.Nudge = _pt > 0.14f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                Charge(dt, px);
            }
            float glintAt = Level == 1 ? 0.06f : Level == 2 ? 0.08f : 0.22f;
            if (!_glinted && _pt >= glintAt)
            {
                _glinted = true;
                if (Kit.Visible(BodyPos()) && (!_kick || Level >= 2))
                {
                    // the edge catches the light (a kicker's glint sits on the raised foot)
                    Vector2 off = _kick ? new Vector2(_fwd * 2f, -2f) * px : new Vector2(-_fwd * 3f, 4f) * px;
                    Color c = Level == 1 ? Color.white : Fx.WithA(Color.Lerp(_pal.Core, _pal.Edge, 0.35f), 1f);
                    Particle g = Fx.Glint(BodyPos() + off, px, 0.18f, c, Level >= 3 ? 1.1f : 0.85f);
                    Follow(g, off);
                }
            }
            if (_pt >= _tWind)
            {
                Pose.Nudge = Vector2.zero;
                _rotFrom = Pose.Rot;
                Next(Phase.Swing);
            }
        }

        /// <summary>Moonfall's charge: the ring closing in, the glow, the path lighting up, dust drawn in.</summary>
        private void ChargeStart(float px)
        {
            if (Fx.HasSilhouette)
            {
                Sprite s = Fx.UnitSprite(A);
                _glow = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 0, true) : null;
                if (_glow != null)
                {
                    _glow.Held = true;
                    _glow.UseEuler = true;
                    Fx.Glue(_glow, this, Fx.WithA(_pal.Edge, 0f));
                }
            }
            if (!Kit.Visible(_from))
            {
                return;
            }
            Anim.Sound("event:/SFX/POWERS/Powerup", A.current_tile);
            // the ring closes in on the feet and goes out as it arrives (held to the end, its last 3-pixel
            // radius showed as a little white box under the unit)
            Fx.Ring(_from, px, _tWind, 18f * _k, 4f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Core, 0f));
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
            int n = Fx.Count(6);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.6f) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[0], _from + dir * 11f * px, px, 0.3f, dir.y < 0f ? FxLayer.Top : FxLayer.Ground, 5);
                p?.Moving(-dir * 32f * px).Sized(0.7f, 0.3f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
            }
            // the telegraph: the path lights up for a breath, a glint racing along it
            Vector2 a0 = _from + _dir * 0.3f * _k, a1 = _from + _dir * Mathf.Max(0.4f, _wStop);
            Kit.Line(a0, a1, px, 0.36f, 1, FxLayer.Ground, 4, Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.5f), Fx.WithA(_pal.Core, 0.35f), Fx.WithA(_pal.Edge, 0f));
            Particle run = Fx.Glint(a0, px, 0.26f, _pal.Core, 0.9f);
            if (run != null)
            {
                run.Step = (q, d) => q.Pos = Vector2.Lerp(a0, a1, Anim.OutQuad(q.K));
            }
        }

        private void Charge(float dt, float px)
        {
            if (!Kit.Visible(BodyPos()))
            {
                return;
            }
            _focus -= dt;
            if (_focus <= 0f)
            {
                _focus = 0.06f;
                Fx.FocusLines(BodyPos(), px, Fx.Count(6), 12f * _k, 24f * _k, 0.1f);
            }
        }

        // -------------------------------------------------------------- swing --

        private void Swing(float dt)
        {
            float px = Px;
            if (Level == 1)
            {
                SwingI(px);
            }
            else if (Level == 2)
            {
                if (_kick)
                {
                    SwingIIKick(px, dt);
                }
                else
                {
                    SwingIIBlade(px);
                }
            }
            else
            {
                SwingIII(px);
            }
            if (_pt >= _tSwing)
            {
                if (Pose.Rot > 180f)
                {
                    Pose.Rot -= 360f;   // after a spin: the same lean, without unwinding it
                }
                _rotFrom = Pose.Rot;
                _hFrom = Pose.Height;
                Next(Phase.Follow);
            }
        }

        private void SwingI(float px)
        {
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.06f));
            float smear = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_pt / 0.07f));
            Pose.Rot = _kick ? Mathf.Lerp(_rotFrom, -32f, k) : Mathf.Lerp(_rotFrom, 26f, k);
            Pose.SX = 1f + 0.16f * smear;
            Pose.SY = 1f - 0.1f * smear;
            Pose.Height = _kick ? 0.08f * _k * smear : 0f;
            Lunge(0.1f * _k * k);
            if (_released == 0 && _pt >= 0.03f)
            {
                Release(px);
                if (Kit.Visible(BodyPos()))
                {
                    Sprite[] arc = _kick ? Art.Arc(8, 0.6f, -120f, 30f, 2) : Art.Arc(8, 0.75f, 140f, -40f, 2);
                    Kit.Swish(this, arc, 0.14f, new Vector2(_fwd * 2f, _kick ? -2f : 0f), _fwd, Color.white, _kick ? AirEdge : _pal.Edge);
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
        }

        private void SwingIIBlade(float px)
        {
            if (_pt < 0.06f)
            {
                // swing 1: a falling diagonal slash
                float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.05f));
                float smear = Mathf.Sin(Mathf.PI * k);
                Pose.Rot = Mathf.Lerp(_rotFrom, 30f, k);
                Pose.SX = 1f + 0.18f * smear;
                Pose.SY = 1f - 0.1f * smear;
                Lunge(0.08f * _k * k);
            }
            else if (_pt < 0.1f)
            {
                // recoil, blade back up
                float k = Anim.InOutCubic((_pt - 0.06f) / 0.04f);
                Pose.Rot = Mathf.Lerp(30f, -12f, k);
                Pose.SX = Mathf.Lerp(1.02f, 1f, k);
                Pose.SY = 1f;
            }
            else
            {
                // swing 2: a rising slash across the first
                float k = Anim.OutCubic(Mathf.Clamp01((_pt - 0.1f) / 0.05f));
                float smear = Mathf.Sin(Mathf.PI * k);
                Pose.Rot = Mathf.Lerp(-12f, 34f, k);
                Pose.SX = 1f + 0.18f * smear;
                Pose.SY = 1f - 0.1f * smear;
                Lunge(0.08f * _k + 0.06f * _k * k);
            }
            Pose.Height = 0f;
            ReleaseII(px, new Sprite[][] { Art.Arc(9, 0.7f, 140f, -40f, 2), Art.Arc(9, 0.7f, -140f, 40f, 2) }, Vector2.zero);
        }

        private void SwingIIKick(float px, float dt)
        {
            if (_pt < 0.06f)
            {
                // a roundhouse, off the ground
                float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.05f));
                Pose.Rot = Mathf.Lerp(_rotFrom, 38f, k);
                Pose.SX = 1f + 0.12f * Mathf.Sin(Mathf.PI * k);
                Pose.SY = 1f - 0.08f * Mathf.Sin(Mathf.PI * k);
            }
            else
            {
                // and straight into a spinning back kick
                float k = Anim.InOutCubic(Mathf.Clamp01((_pt - 0.06f) / 0.09f));
                Pose.Rot = 38f + 360f * k;
                float tuck = Mathf.Sin(Mathf.PI * k);
                Pose.SX = 1f - 0.06f * tuck;
                Pose.SY = 1f - 0.12f * tuck;
                if (Kit.Visible(BodyPos()))
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.025f;
                        Fx.Afterimage(this, 0.1f, _pal.Ghost, _pal.GhostEnd);
                    }
                }
            }
            Pose.Height = 0.13f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_pt / _tSwing));
            Lunge(0.06f * _k * Mathf.Clamp01(_pt / 0.06f));
            ReleaseII(px, new Sprite[][] { Art.Arc(10, 0.55f, -160f, 25f, 3), Art.Arc(10, 0.42f, 180f, 540f, 2) }, new Vector2(0f, -1f));
        }

        /// <summary>Level II: A leaves on the first swing, B on the second, right onto A.</summary>
        private void ReleaseII(float px, Sprite[][] arcs, Vector2 off)
        {
            if (_released == 0 && _pt >= 0.03f)
            {
                Release(px);
                if (Kit.Visible(BodyPos()))
                {
                    Kit.Swish(this, arcs[0], 0.15f, new Vector2(_fwd * 2f, off.y), _fwd, _pal.Core, _pal.Edge);
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (_released == 1 && _pt >= 0.13f)
            {
                Release(px);
                if (Kit.Visible(BodyPos()))
                {
                    Kit.Swish(this, arcs[1], 0.15f, new Vector2(_fwd * 2f, off.y), _fwd, _pal.Core, _pal.Edge);
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
        }

        private void SwingIII(float px)
        {
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.05f));
            float stretch = Mathf.Sin(Mathf.PI * Mathf.Clamp01(_pt / 0.06f));
            float settle = Anim.Smooth(0.05f, _tSwing, _pt);
            Pose.Rot = Mathf.Lerp(_rotFrom, _kick ? 50f : 46f, k);
            Pose.SX = Mathf.Lerp(1f - 0.12f * stretch, _kick ? 1.16f : 1.12f, settle);
            Pose.SY = Mathf.Lerp(1f + 0.25f * stretch, _kick ? 0.84f : 0.86f, settle);
            Pose.Height = _kick ? Mathf.Lerp(0.06f * _k, 0f, k) : 0f;
            Pose.Nudge = Vector2.zero;
            if (_released == 0)
            {
                Release(px);
                Vector2 body = BodyPos();
                if (Kit.Visible(body))
                {
                    Sprite[] arc = _kick ? Art.Arc(12, 1f, 100f, -80f, 3) : Art.Arc(13, 1f, 120f, -75f, 3);
                    Kit.Swish(this, arc, 0.2f, new Vector2(_fwd * 3f, 2f), _fwd, _pal.Core, _pal.Edge);
                    Fx.Afterimage(this, 0.16f, _pal.Ghost, _pal.GhostEnd);
                    Kit.Flash(body + _dir * 0.5f * _k, px, 6, 0.08f, Fx.WithA(_pal.Core, 1f), FxLayer.Top, 28);
                    Kit.Shockwave(_from, px, 3f, 22f * _k, 0.3f, Fx.WithA(_pal.Edge, 0.9f));
                    Kit.DustRing(_from, px, A.current_tile, 10, 55f, 0.5f, _k);
                    Kit.Shake(body, 0.2f, 0.006f, _k);
                }
                Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
            }
        }

        /// <summary>A small step into the swing (never onto a bad tile).</summary>
        private void Lunge(float dist)
        {
            Vector2 to = Combat.Clamp(_from + _dir * dist);
            if (Combat.SafeSpot(to))
            {
                Pose.Ground = to;
                _groundFrom = to;
            }
        }

        // ------------------------------------------------------------ release --

        private void Release(float px)
        {
            _released++;
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (_released == 1)
            {
                _releaseT = T;
                _wS = Mathf.Min(0.3f * _k, _wStop);
                _wLive = true;
                _wFlying = Level != 2;
                _wSpeed = (Level == 1 ? SpeedI : Level == 2 ? SpeedII : SpeedIII) * _speedK;
                _wDustAt = _wS;
                _wRavAt = _wS + 1f;   // the X's scorch line starts clear of the thrower's feet
                if (Level >= 3)
                {
                    MakeFurrow(px);
                    _lastAlong = LastFoeAlong();
                }
            }
            else
            {
                // B lands on A: the X locks and flies
                _wFlying = true;
                if (Kit.Visible(WavePoint))
                {
                    Vector2 c = WavePoint + new Vector2(0f, _z);
                    Fx.Glint(c, px, 0.14f, _pal.Core, 0.9f);
                    Kit.Flash(c, px, 3, 0.06f, Fx.WithA(_pal.Core, 0.9f), FxLayer.Top, 26);
                }
            }
            Spawn(px);
        }

        /// <summary>
        /// Moonfall, at the release: how far along its path the furthest enemy it could cut stands (its far
        /// edge), 0 when there is none. One search along the whole path, once.
        /// </summary>
        private float LastFoeAlong()
        {
            if (_wStop <= 0.2f)
            {
                return 0f;
            }
            float last = 0f;
            Vector2 mid = _from + _dir * (_wStop * 0.5f);
            foreach (Actor u in Combat.FoesAround(A, mid, _wStop * 0.5f + _hitR + 1f))
            {
                if (!u.hasHealth())
                {
                    continue;   // (a body cut down this frame: the moon never cuts it, nothing to wait for)
                }
                Vector2 rel = u.current_position - _from;
                float along = Vector2.Dot(rel, _dir);
                float size = u.stats["size"] * 0.5f;
                if (along < 0.2f || along > _wStop + size || Mathf.Abs(rel.x * _dir.y - rel.y * _dir.x) > _hitR + size + 0.3f)
                {
                    continue;
                }
                last = Mathf.Max(last, along + size);
            }
            return Mathf.Min(last, _wStop);
        }

        /// <summary>The crescent's sprites, glued to the simulated wave.</summary>
        private void Spawn(float px)
        {
            float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            int style = _kick ? 1 : 0;
            Sprite[] body, core;
            Vector2 heading = _dir;
            if (Level >= 3)
            {
                int bulge = Mathf.Clamp(Mathf.RoundToInt((Mathf.Abs(_dir.x) - 0.55f) / 0.15f), 0, 3);
                body = FangArt.Moon(style, bulge, 0);
                core = FangArt.Moon(style, bulge, 1);
                _r = FangArt.MoonR * FangArt.Bulge(bulge);
                _cosHalf = FangArt.MoonCos;
            }
            else
            {
                int size = Level == 1 ? 0 : 1;
                float drawn;
                if (Level == 1)
                {
                    drawn = FangArt.Bucket(ang);
                }
                else
                {
                    // the X is drawn square to the screen (an X reads as an X whichever way it flies): the nearest
                    // of the four axes, its two blades at +45 / -45 degrees off it, crossing at their middles
                    float axis = Mathf.Round(ang / 90f) * 90f;
                    drawn = FangArt.Bucket(axis + (_released == 1 ? 45f : -45f));
                }
                body = FangArt.Flat(style, size, drawn, 0);
                core = Level >= 2 ? FangArt.Flat(style, size, drawn, 1) : null;
                _r = FangArt.FlatR(style, size);
                _cosHalf = Mathf.Cos(FangArt.Half(style, size) * Mathf.Deg2Rad);
                // level I sits on its chord (the cut's edge leads the wave), the X's blades on their arc's middle
                _anchor = Level == 1 ? _r * _cosHalf : _r;
                heading = new Vector2(Mathf.Cos(drawn * Mathf.Deg2Rad), Mathf.Sin(drawn * Mathf.Deg2Rad));
            }
            _blades.Add(new Blade { Art = body, Heading = heading });
            Color edge = Level == 1 ? (_kick ? AirEdge : _pal.Edge) : _pal.Edge;
            Hold(body, edge, 6, heading);
            if (core != null)
            {
                Hold(core, _pal.Core, 7, heading);
            }
            if (_released == 1 && Level <= 2)
            {
                // its shadow skims the ground under it
                Particle sh = Fx.Spawn(Art.Disk(Level == 1 ? 2 : 3, 0.42f, 0.45f), WavePoint, px, 10f, FxLayer.Ground, 3);
                if (sh != null)
                {
                    sh.Held = true;
                    sh.Snap = true;
                    sh.Color(new Color(0f, 0f, 0f, 0.22f));
                    sh.Step = (q, d) => q.Pos = WavePoint;
                    _parts.Add(sh);
                }
            }
        }

        private void Hold(Sprite[] frames, Color c, int order, Vector2 heading)
        {
            Particle p = Fx.Spawn(frames, BladePos(heading), Px, 10f, FxLayer.Over, order);
            if (p == null)
            {
                return;
            }
            p.Held = true;
            p.Snap = true;
            p.Fps = 18f;
            p.Loop = true;
            p.SX = Level >= 3 ? _fwd : 1f;
            p.Color(c);
            p.Step = (q, d) => q.Pos = BladePos(heading);
            _parts.Add(p);
        }

        /// <summary>
        /// Where a crescent's sprite (drawn around its circle's centre) sits: level I with its chord on the wave's
        /// point, the X's two blades with their arcs' middles on it (so they cross in the middle), both at body
        /// height; the moon standing on the ground with its bulge ahead. The offset is in whole pixels from a
        /// snapped point, so the X's two sprites always land on the same pixel grid and cross cleanly.
        /// </summary>
        private Vector2 BladePos(Vector2 heading)
        {
            float px = Px;
            Vector2 g = WavePoint;
            if (Level >= 3)
            {
                return new Vector2(g.x - _fwd * _r * _cosHalf * px, g.y + (FangArt.MoonR * FangArt.MoonSin + 0.5f) * px);
            }
            Vector2 c = g + new Vector2(0f, _z);
            c = new Vector2(Mathf.Round(c.x / px) * px, Mathf.Round(c.y / px) * px);
            return c - new Vector2(Mathf.Round(heading.x * _anchor), Mathf.Round(heading.y * _anchor)) * px;
        }

        /// <summary>The middle of the wave as seen (sparks, flashes, end bursts).</summary>
        private Vector2 Middle()
        {
            Vector2 g = WavePoint;
            if (Level >= 3)
            {
                return new Vector2(g.x + _fwd * _r * (1f - _cosHalf) * 0.4f * Px, g.y + (FangArt.MoonR * FangArt.MoonSin + 0.5f) * Px);
            }
            if (Level == 2)
            {
                return g + new Vector2(0f, _z);   // where the X's blades cross
            }
            return g + new Vector2(0f, _z) + _dir * _r * (1f - _cosHalf) * 0.5f * Px;
        }

        private void MakeFurrow(float px)
        {
            if (!Kit.Visible(_from + _dir * _wStop * 0.5f, 0.6f) && !Kit.Visible(_from, 0.3f))
            {
                return;
            }
            // a blade's cut, not a road: one pixel scored into the ground with a broken lip, white-hot for
            // a blink, the tier colour for a moment, then a dull ember line that dies out; the groove
            // itself stays a while (it was a 2-pixel body with full rims glowing for 15 s: a rail; and with
            // the tier colour held 1.3 s, a red tier's furrow read as a laser line lying across the trench)
            Color edge = _pal.Edge;
            Color dark = Color.Lerp(edge, _pal.Dark, 0.55f);
            LineScar.Spec sp = new LineScar.Spec
            {
                Width = 1,
                TipWidth = 1,
                TaperStart = true,
                Jag = 0f,
                Rubble = true,
                Rim = 0.55f,
                GlowUntil = 1f,
                GlowWidth = 1,
                Life = 9f,
                GlowLife = 3f,
                GlowColors = LineScar.Cooling(3f, new[] { 0f, 0.1f, 0.35f, 0.9f, 1.9f, 3f }, new[]
                {
                    Color.white, Fx.WithA(Color.Lerp(_pal.Core, edge, 0.35f), 1f), Fx.WithA(edge, 0.85f),
                    Fx.WithA(dark, 0.6f), Fx.WithA(_pal.Dark, 0.25f), Fx.WithA(_pal.Dark, 0f)
                })
            };
            float start = Mathf.Min(0.3f * _k, _wStop);
            _scar = LineScar.Build(_from + _dir * start, _dir, Mathf.Max(0f, _wStop - start), px, sp, (int)(Id & 0x7fffffff) ^ Random.Range(0, 99999));
        }

        // ---------------------------------------------------------------- fly --

        private void Fly(float dt)
        {
            if (_wBite > 0f)
            {
                // the X bites into an enemy: a local hit-stop, the wave holds for a beat
                _wBite -= dt;
                return;
            }
            float s0 = _wS;
            float speed = _wFlying ? _wSpeed : Drift;
            float s1 = Mathf.Min(_wStop, s0 + speed * dt);
            // a wall that rose since the plan (a mountain power...) still stops it
            float block = FirstBlock(s0, s1);
            if (block >= 0f)
            {
                s1 = Mathf.Max(s0, block - 0.1f);
                _wStop = s1;
            }
            _wS = s1;
            if (_wFlying)
            {
                Sweep(s0, s1);
                if (!_wLive)
                {
                    return;
                }
                Scorch();
            }
            WaveFx(dt);
            _scar?.RevealTo(_wS - 0.3f * _k);
            if (_wFlying && _wS >= _wStop - 0.0001f && _wLive)
            {
                bool wall = _wStop < _range - 0.3f;
                EndWave(wall ? End.Wall : End.Range);
            }
        }

        /// <summary>
        /// The wave tears the ground it skims (world damage, on or off screen): the X a scorched line along its
        /// whole path, a tile at a time as it passes; the moon, once the burst has opened its trench, tears it on
        /// right behind itself. Level I leaves the land be.
        /// </summary>
        private void Scorch()
        {
            if (Level == 2)
            {
                while (_wS >= _wRavAt)
                {
                    LineKit.Ravage(_from + _dir * _wRavAt, 1f, 1, A);
                    _wRavAt += 1f;
                }
                return;
            }
            if (Level < 3 || !_opened)
            {
                return;
            }
            float step = Mathf.Max(0.8f, _tearR);
            while (_wS - _trenchTo >= step)
            {
                _trenchTo += step;
                Vector2 p = _from + _dir * _trenchTo;
                LineKit.Ravage(p, _tearR, _tearForce, A);
                Burn(p);
            }
        }

        /// <summary>A God's cut burns: the line itself catches fire here and there (the grass fire spreads from it).</summary>
        private void Burn(Vector2 p)
        {
            if (_god && Random.value < 0.6f)
            {
                LineKit.Ignite(p, A, _groundFrom);
            }
        }

        /// <summary>The first wall, mountain or map edge between s0 and s1 along the wave (every quarter tile and at s1), or -1.</summary>
        private float FirstBlock(float s0, float s1)
        {
            for (float s = s0 + 0.25f; ; s += 0.25f)
            {
                if (s > s1)
                {
                    s = s1;
                }
                Vector2 q = _from + _dir * s;
                if (q.x < 1f || q.y < 1f || q.x > MapBox.width - 1f || q.y > MapBox.height - 1f)
                {
                    return s;
                }
                WorldTile t = Combat.TileAt(q);
                if (t == null || t.Type == null || t.Type.block)
                {
                    return s;
                }
                if (s >= s1)
                {
                    return -1f;
                }
            }
        }

        /// <summary>Everyone the leading edge passed between two ticks, nearest first.</summary>
        private void Sweep(float s0, float s1)
        {
            Vector2 a0 = _from + _dir * s0, a1 = _from + _dir * s1;
            float reach = (s1 - s0) * 0.5f + _hitR + 1f;
            List<Actor> near = Combat.FoesAround(A, (a0 + a1) * 0.5f, reach);
            _cand.Clear();
            foreach (Actor u in near)
            {
                long id = u.Sid();
                // (a body cut down earlier this frame is still 'alive' with no health: a crescent never breaks on it)
                if (_touched.Contains(id) || !u.hasHealth())
                {
                    continue;
                }
                Vector2 rel = u.current_position - _from;
                float along = Vector2.Dot(rel, _dir);
                float perp = Mathf.Abs(rel.x * _dir.y - rel.y * _dir.x);
                float size = u.stats["size"] * 0.5f;
                if (perp > _hitR + size || along < 0.2f || along > s1 + size || along < s0 - _hitR - size)
                {
                    continue;
                }
                _cand.Add(new KeyValuePair<float, Actor>(along, u));
            }
            if (_cand.Count == 0)
            {
                return;
            }
            _cand.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (KeyValuePair<float, Actor> kv in _cand)
            {
                if (!_wLive)
                {
                    break;
                }
                Touch(kv.Value, kv.Key);
            }
        }

        /// <summary>The wave reaches one enemy.</summary>
        private void Touch(Actor u, float along)
        {
            _touched.Add(u.Sid());
            float px = Px;
            Vector2 body = u.current_position + new Vector2(0f, Mathf.Clamp(u.actor_scale, 0.04f, 0.4f) * 4f);
            // (it may answer before any blow, a foe too heavy for this tier too: a Master's Mirage Step slips an
            // Expert's cut instead of breaking it)
            Contact c = Combat.Reach(this, u, Level >= 2);
            if (c == Contact.Evaded)
            {
                return;   // it slipped the cut: the wave flies on
            }
            if (c == Contact.Parried)
            {
                if (Level == 1)
                {
                    _parried = true;
                    Anim.Sound("event:/SFX/HIT/HitSwordSword", u.current_tile);
                    Stop(along - u.stats["size"] * 0.5f, End.Shatter);
                }
                return;
            }
            if (!Combat.Allowed(A, u, out float cm))
            {
                // too heavy for this tier: a glancing cut; I and II break on it with a clang
                Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                Combat.Stagger(u, 0.3f);
                if (Kit.Visible(body))
                {
                    Fx.HitSpark(body, px, 1, -_dir, 5);
                }
                if (Level <= 2)
                {
                    _blocked = true;
                    Anim.Sound("event:/SFX/HIT/HitMetal", u.current_tile);
                    Stop(along - u.stats["size"] * 0.5f, End.Shatter);
                }
                return;
            }
            _hits++;
            switch (Level)
            {
                case 1:
                    {
                        HitResult r = Combat.Hit(A, u, 1f * cm, true, true, 0f, this);
                        if (Kit.Visible(body))
                        {
                            Fx.HitSpark(body, px, 0, _dir, 4);
                            Kit.Flash(body, px, 2, 0.05f, Color.white, FxLayer.Top, 26);
                        }
                        if (!r.Killed && u.isAlive())
                        {
                            Combat.Stagger(u, 0.2f);   // a flinch
                        }
                        Anim.Sound(_kick ? "event:/SFX/OTHER/Punch" : "event:/SFX/HIT/HitFlesh", u.current_tile);
                        Stop(along - u.stats["size"] * 0.3f, End.Shatter);
                        break;
                    }
                case 2:
                    {
                        // the first blade of the X, then the second a beat later (that one may kill, and shoves)
                        Combat.Hit(A, u, 1f * cm, false, true, 0f, this);
                        _pending.Add(new Pending { V = u, Id = u.Sid(), At = T + 0.05f, Mult = 1f * cm });
                        if (Kit.Visible(body))
                        {
                            // the first blade: a small star (two big ones 0.05 s apart merged into one pale blob
                            // covering the enemy); the second cut, the one that shoves, gets the big one
                            Fx.HitSpark(body, px, 0, Rotate(_dir, 30f), 4);
                            Kit.Flash(body, px, 2, 0.05f, Fx.WithA(_pal.Core, 1f), FxLayer.Top, 26);
                            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
                            Kit.Line(body + new Vector2(-_fwd * 3f, 3f) * vpx, body + new Vector2(_fwd * 3f, -3f) * vpx, vpx, 0.08f, 1, FxLayer.Top, 27,
                                Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                        }
                        u.startShake(0.12f, 0.08f, true, false);
                        Anim.Sound(_kick ? "event:/SFX/OTHER/Punch" : "event:/SFX/HIT/HitFlesh", u.current_tile);
                        if (_wBites < 3)
                        {
                            _wBites++;
                            _wBite = 0.04f;
                        }
                        break;
                    }
                default:
                    if (_late)
                    {
                        LateCut(u, cm, px);
                    }
                    else
                    {
                        CutIt(u, cm, px);
                    }
                    break;
            }
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The second cut of the X lands, with the shove.</summary>
        private void Hits()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (T < p.At)
                {
                    continue;
                }
                _pending.RemoveAt(i);
                Actor v = p.V;
                // (cut down by someone else since the first blade: no second cut on the body, no kill credit)
                if (v == null || !Combat.Living(v) || v.Sid() != p.Id || v.isInMagnet())
                {
                    continue;
                }
                HitResult r = Combat.Hit(A, v, p.Mult, true, true, 0f, this);
                float px = Px;
                Vector2 body = v.current_position + new Vector2(0f, Mathf.Clamp(v.actor_scale, 0.04f, 0.4f) * 4f);
                if (Kit.Visible(body))
                {
                    // the second blade crosses the first: an X cut into the enemy
                    Fx.HitSpark(body, px, 1, Rotate(_dir, -30f), 4);
                    float vpx = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
                    Kit.Line(body + new Vector2(-_fwd * 3f, -3f) * vpx, body + new Vector2(_fwd * 3f, 3f) * vpx, vpx, 0.08f, 1, FxLayer.Top, 27,
                        Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                    if (r.Crit)
                    {
                        Kit.ImpactFrame(body, px, 0.4f, _pal, true);
                    }
                }
                if (v.isAlive())
                {
                    // 0.5 tile along the cut (heavy ones only stagger)
                    Combat.Launch(v, v.current_position - _dir, 0.5f, 0.25f, 0f);
                }
            }
        }

        /// <summary>The wave stops at 'along' (it hit something solid or someone).</summary>
        private void Stop(float along, End how)
        {
            _wS = Mathf.Clamp(along, 0f, _wS);
            _wStop = _wS;
            EndWave(how);
        }

        private void EndWave(End how)
        {
            if (!_wLive)
            {
                return;
            }
            _wLive = false;
            _wEndT = T;
            _scar?.RevealTo(_wS);
            _scar?.Close();
            float px = Px;
            Vector2 at = Middle();
            bool vis = Kit.Visible(at);
            if (how == End.Wall && Level >= 2 && Valid)
            {
                // it smashes into the cliff: the ground at its foot is blasted (a God's moon sets it alight)
                LineKit.Ravage(WavePoint, Level >= 3 ? _tearR : 1f, Level >= 3 ? _tearForce : 2, A);
                if (Level >= 3)
                {
                    Burn(WavePoint);
                }
            }
            else if (Level >= 3 && _opened && Valid && _wS - _trenchTo > 0.3f)
            {
                // the spent moon's trench runs out where it vanishes
                LineKit.Ravage(WavePoint, _tearR, _tearForce, A);
                Burn(WavePoint);
            }
            if (_opened)
            {
                _trenchTo = _wS;
            }
            if (vis)
            {
                switch (how)
                {
                    case End.Wall:
                        {
                            Kit.Sparks(at, px, 8, 50f, 110f, _pal.Edge);
                            Kit.Debris(WavePoint, px, Combat.TileAt(WavePoint), 5, 0.8f);
                            Kit.Flash(at, px, 4, 0.06f, Fx.WithA(_pal.Core, 0.9f), FxLayer.Top, 26);
                            Anim.Sound("event:/SFX/HIT/HitStone", Combat.TileAt(WavePoint));
                            break;
                        }
                    case End.Shatter:
                        {
                            Kit.Sparks(at, px, Level == 1 ? 4 : 7, 40f, 90f, _pal.Edge);
                            Particle star = Fx.Spawn(Art.HitStars[Level == 1 ? 0 : 1], at, px, 0.08f, FxLayer.Top, 21);
                            star?.Sized(1.1f, 0.7f).Color(Color.white, Fx.WithA(_pal.Edge, 0f));
                            break;
                        }
                    default:
                        {
                            if (Level >= 3)
                            {
                                // the moon folds into a vertical line of light and is gone, glints drifting off
                                // (a tall flash disk on the grass read as a grey puff of smoke)
                                float hh = FangArt.MoonR * FangArt.MoonSin * px;
                                Kit.Line(at - new Vector2(0f, hh), at + new Vector2(0f, hh), px, 0.12f, 1, FxLayer.Top, 26,
                                    Color.white, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
                                for (int i = 0; i < 3; i++)
                                {
                                    Particle g = Fx.Glint(at + new Vector2(Random.Range(-2f, 2f), (i - 1) * 5f) * px, px, 0.22f, _pal.Core, 0.7f);
                                    g?.Moving(new Vector2(_dir.x * 10f, 8f) * px);
                                }
                            }
                            else
                            {
                                Kit.Sparks(at, px, Level == 1 ? 3 : 6, 25f, 60f, _pal.Edge);
                                if (Level == 2)
                                {
                                    Fx.Glint(at, px, 0.14f, _pal.Core, 0.7f);
                                }
                            }
                            break;
                        }
                }
            }
            float fade = how == End.Range ? (Level >= 3 ? 0.18f : 0.1f) : 0.05f;
            foreach (Particle p in _parts)
            {
                Fade(p, fade);
            }
            _parts.Clear();
            if (Dev.On) Dev.Trace("fang " + Id + " wave ends (" + how + ") at " + _wS.ToString("F1") + " tiles, " + _hits + " hit");
        }

        private static void Fade(Particle p, float t)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Color c = p.R != null ? p.R.color : Color.white;
            p.Held = false;
            p.Age = 0f;
            p.Life = t;
            p.Sized(1f, 1.15f);
            p.Color(c, Fx.WithA(c, 0f));
        }

        // ------------------------------------------------------------ wave fx --

        private void WaveFx(float dt)
        {
            Vector2 g = WavePoint;
            float every = Level == 1 ? 0.6f : Level == 2 ? 0.35f : 0.45f;
            if (!Kit.Visible(g, 0.4f))
            {
                // the dust streak keeps pace unseen (a wave flying into view spawned every puff it had missed
                // along the whole path behind it, at once)
                while (_wS - _wDustAt >= every)
                {
                    _wDustAt += every;
                }
                return;
            }
            float px = Px;
            Vector2 at = Middle();
            // afterimages of the crescent itself
            _wTrail -= dt;
            if (_wTrail <= 0f && _wFlying)
            {
                // (the X's copies every 0.4 tile overlapped into a lattice: now one copy a tile behind it)
                _wTrail = Level == 1 ? 0.05f : Level == 2 ? 0.06f : 0.03f;
                foreach (Blade b in _blades)
                {
                    Particle t = Fx.Spawn(b.Art[0], BladePos(b.Heading), px, Level >= 3 ? 0.16f : Level == 2 ? 0.1f : 0.12f, FxLayer.Behind, 3);
                    if (t == null)
                    {
                        break;
                    }
                    t.Snap = true;
                    t.SX = Level >= 3 ? _fwd : 1f;
                    if (Level == 1)
                    {
                        t.Color(new Color(1f, 1f, 1f, 0.4f), new Color(1f, 1f, 1f, 0f));
                    }
                    else if (Level == 2)
                    {
                        t.Color(Fx.WithA(_pal.Ghost, 0.45f), _pal.GhostEnd);
                    }
                    else
                    {
                        t.Color(_pal.Ghost, _pal.GhostEnd);
                    }
                }
            }
            // speed lines
            _wLines -= dt;
            if (_wLines <= 0f && _wFlying)
            {
                _wLines = Level == 1 ? 0.07f : 0.045f;
                Kit.SpeedLines(at, _dir, px, Level == 1 ? 1 : 2, Level >= 3 ? 7f : 4f, 0.12f, Level == 1 ? new Color(1f, 1f, 1f, 0.6f) : Fx.WithA(_pal.Edge, 0.75f));
            }
            // the ground answers: a dust streak (spray over water)
            while (_wS - _wDustAt >= every)
            {
                _wDustAt += every;
                Vector2 p = _from + _dir * _wDustAt;
                WorldTile t = Combat.TileAt(p);
                if (t != null && t.Type != null && t.Type.liquid)
                {
                    Wake(p, px, t);
                }
                else if (Level >= 2 || Random.value < 0.7f)
                {
                    Color dust = Fx.Lighten(Fx.SoilColor(t), 0.35f);
                    // one small puff a step: two big ones at III merged into a beige bank along the whole line
                    // that hid the enemies standing frozen in it
                    Vector2 side = new Vector2(-_dir.y, _dir.x) * Random.Range(-3f, 3f) * px;
                    Particle d = Fx.Spawn(Art.Dust[Random.Range(0, 2)], p + side, px, Random.Range(0.3f, 0.45f), FxLayer.Ground, 5);
                    d?.Moving(_dir * Random.Range(15f, 30f) * px + side * 3f, Random.Range(3f, 7f) * px, 3f)
                        .Sized(0.6f, Level >= 3 ? 1f : 1.1f)
                        .Color(Fx.WithA(dust, Level == 1 ? 0.45f : Level == 2 ? 0.65f : 0.5f), Fx.WithA(dust, 0.3f), Fx.WithA(dust, 0f));
                }
            }
        }

        /// <summary>Skimming water: a little spray and a ripple.</summary>
        private void Wake(Vector2 p, float px, WorldTile t)
        {
            Color water = Fx.SoilColor(t);
            for (int i = 0; i < 2; i++)
            {
                Particle d = Fx.Spawn(Art.Pixel, p + new Vector2(Random.Range(-2f, 2f), 0f) * px, px, Random.Range(0.3f, 0.45f), FxLayer.Top, 4);
                d?.Moving(_dir * Random.Range(10f, 25f) * px + Random.insideUnitCircle * 8f * px, Random.Range(30f, 55f) * px, 0f, 300f * px)
                    .Color(Color.white, Fx.WithA(water, 0.8f), Fx.WithA(water, 0f));
            }
            Fx.Ring(p, px, 0.3f, 1f, 5f, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(Color.white, 0.6f), Fx.WithA(water, 0f));
        }

        // ------------------------------------------------------------- follow --

        private void Follow(float dt)
        {
            Pose.Ground = _groundFrom;
            if (Level >= 3)
            {
                // the follow-through, held perfectly still while the moon travels
                float k = Anim.OutQuad(Mathf.Clamp01(_pt / 0.3f));
                Pose.Rot = Mathf.Lerp(_rotFrom, _kick ? 26f : 30f, k);
                Pose.SX = Mathf.Lerp(Pose.SX, 1.08f, Mathf.Clamp01(dt * 8f));
                Pose.SY = Mathf.Lerp(Pose.SY, 0.9f, Mathf.Clamp01(dt * 8f));
                Pose.Height = 0f;
                Stillness(dt);
                // the stillness lasts until the moon is past the last enemy on its line and the tier's beat is
                // over (it used to wait for the moon to fly its whole path: a God's 18 tiles kept it standing
                // 0.64 s, longer than anyone, even when its one duel opponent had slipped the cut). A whiff
                // gets only a breath, then the click.
                bool passed = !_wLive || _wS >= _lastAlong + 0.35f * _k;
                float still = _hits > 0 ? _sheatheDelay : 0.15f;
                if (passed && _releaseT >= 0f && T - _releaseT >= still && (_wLive || T - _wEndT >= 0.08f))
                {
                    if (Dev.On) Dev.Trace("fang " + Id + " stillness over: " + _hits + " cut, moon at " + _wS.ToString("F1") + "/" + _wStop.ToString("F1")
                        + " tiles (last foe " + _lastAlong.ToString("F1") + ")" + (_wLive ? ", flying on" : ""));
                    _rotFrom = Pose.Rot;
                    Next(Phase.Sheathe);
                }
                return;
            }
            float e = Anim.OutQuad(Mathf.Clamp01(_pt / 0.22f));
            float rest = _kick && Level == 1 ? -8f : 10f;
            if (_parried)
            {
                // parried: knocked back off balance
                Pose.Rot = Mathf.Lerp(_rotFrom, -16f, Anim.OutCubic(Mathf.Clamp01(_pt / 0.08f)));
                Pose.Nudge = _pt < 0.15f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            else
            {
                Pose.Rot = Mathf.Lerp(_rotFrom, rest, e);
            }
            Pose.Height = Mathf.Lerp(_hFrom, 0f, Anim.OutQuad(Mathf.Clamp01(_pt / 0.08f)));
            Pose.SX = Mathf.Lerp(Pose.SX, 1f, Mathf.Clamp01(dt * 14f));
            Pose.SY = Mathf.Lerp(Pose.SY, 1f, Mathf.Clamp01(dt * 14f));
            // a Flow tier doesn't stand and watch its wave: once the follow-through has read, it springs back
            // and the cut flies on by itself (Recover hands it over); a parried crescent has already stopped
            bool waveDone = !_wLive && _pending.Count == 0;
            if (waveDone && _pt >= 0.1f || _flow && !_parried && _pt >= 0.08f)
            {
                Pose.Nudge = Vector2.zero;
                _rotFrom = Pose.Rot;
                Next(Phase.Recover);
            }
        }

        /// <summary>The silence of the stance: a few dust grains drifting past, nothing else moves.</summary>
        private void Stillness(float dt)
        {
            _ghost -= dt;
            if (_ghost > 0f || !Kit.Visible(BodyPos()))
            {
                return;
            }
            _ghost = 0.12f;
            float px = Px;
            Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.5f);
            Vector2 at = _groundFrom + new Vector2(Random.Range(-8f, 8f), Random.Range(0f, 6f)) * px;
            Particle p = Fx.Spawn(Art.Pixel, at, px, 0.6f, FxLayer.Top, 3);
            p?.Moving(new Vector2(-_fwd * Random.Range(4f, 9f), Random.Range(1f, 3f)) * px).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0f));
        }

        // ------------------------------------------------------------ sheathe --

        private void Sheathe(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Vector2 body = BodyPos();
                if (Kit.Visible(body))
                {
                    // "click": the blade slides home (a glint along the scabbard), or the fist closes
                    Vector2 off = _kick ? new Vector2(_fwd * 3f, 2f) * px : new Vector2(-_fwd * 1f, -1f) * px;
                    Particle g = Fx.Glint(body + off, px, 0.22f, _pal.Core, 1.1f);
                    if (g != null)
                    {
                        Vector2 slide = _kick ? Vector2.zero : new Vector2(-_fwd * 4f, 0f) * px;
                        g.Step = (q, d) => q.Pos = BodyPos() + off + slide * Anim.OutQuad(q.K);
                    }
                }
                Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
                if (!_outlined)
                {
                    _outlined = true;
                    foreach (Cut c in _cuts)
                    {
                        if (c.P != null && !c.P.Finished && Kit.Visible(c.At))
                        {
                            Outline(c.P, _pal.Edge, 0.08f);
                        }
                    }
                }
            }
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.1f));
            Pose.Ground = _groundFrom;
            Pose.Rot = Mathf.Lerp(_rotFrom, -4f, k);
            Pose.SX = Mathf.Lerp(1.08f, 1f, k);
            Pose.SY = Mathf.Lerp(0.9f, 1.05f, k);
            if (!_burst && _pt >= BurstDelay - SheatheDelay)
            {
                _burst = true;
                bool cut = Burst();
                OpenFurrow(cut);
            }
            if (_pt >= 0.12f && _fly.Count == 0)
            {
                _rotFrom = Pose.Rot;
                Next(Phase.Recover);
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            if (Enter() && !_flow)
            {
                A.startAttackCooldown();   // (Flow tiers never swing like a peasant: no swing timer to stall them)
            }
            // Flow tiers spring back in half the time (the same spring, played twice as fast)
            float st = _pt * (_tRecover / _recoverDur);
            float amp = Anim.Spring(st, 9f, 18f);
            Pose.Ground = _groundFrom;
            Pose.Height = 0f;
            Pose.Rot = _rotFrom * Mathf.Exp(-10f * st);
            Pose.SX = 1f + 0.08f * amp * (Level >= 3 ? 1.5f : 1f);
            Pose.SY = 1f - 0.09f * amp * (Level >= 3 ? 1.5f : 1f);
            Pose.Nudge = Vector2.zero;
            if (_pt >= _recoverDur && _fly.Count == 0)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                KillGlow();
                LeaveLink();
                if (_wLive || _pending.Count > 0)
                {
                    // the performer is free; its cut flies on by itself
                    _detached = true;
                    LineKit.Linger(this);
                }
                Finished = true;
            }
        }

        /// <summary>
        /// The chain it leaves, only when the performer has a follow-up that takes it: Scattered after a Moonfall
        /// that threw survivors off the line (on the most dangerous one), or after an X down a line of two or
        /// more (cut and shoved apart, or about to be: a Flow tier's X may still be flying).
        /// </summary>
        private void LeaveLink()
        {
            if (Level >= 3)
            {
                if (_survivors >= 1 && LineKit.CanChain(A, Link.Scattered))
                {
                    EndLink = Link.Scattered;
                    EndTarget = _strongest != null && _strongest.isAlive() && _strongest.hasHealth() && _strongest.Sid() == _strongestId ? _strongest : null;
                }
                return;
            }
            if (Level != 2 || _lone || _blocked)
            {
                return;
            }
            int n = _wLive || _pending.Count > 0 ? Mathf.Max(_count, _hits) : _hits;
            if (n >= 2 && LineKit.CanChain(A, Link.Scattered))
            {
                EndLink = Link.Scattered;
                EndTarget = _target != null && _target.isAlive() && _target.Sid() == _targetId ? _target : null;
            }
        }

        // ------------------------------------------------------------ linger --

        /// <summary>After the move (Flow tiers): the wave flies on, cutting whoever it passes, until it is spent.</summary>
        bool LineKit.ILinger.Linger(float dt)
        {
            if (!Valid)
            {
                // the thrower is gone (its unit may be recycled): the cut dissipates where it is
                _pending.Clear();
                EndWave(End.Range);
                return false;
            }
            T += dt;
            if (_wLive)
            {
                Fly(dt);
            }
            Hits();
            if (_wLive || _pending.Count > 0)
            {
                return true;
            }
            KillWave();   // (nothing should be left held by now; never leave a held sprite behind)
            LineScar.Sweep();
            if (Dev.On) Dev.Trace("fang " + Id + " wave spent after the move: " + _hits + " hit");
            return false;
        }

        void LineKit.ILinger.Drop()
        {
            _pending.Clear();
            KillWave();
        }

        // ------------------------------------------------------------ moonfall --

        /// <summary>The moon passes through an enemy: it is already cut. Frozen in place until the burst.</summary>
        private void CutIt(Actor u, float cm, float px)
        {
            Cut c = new Cut
            {
                V = u,
                Id = u.Sid(),
                CM = cm,
                At = u.current_position,
                Side = Mathf.Sign((u.current_position.x - _from.x) * _dir.y - (u.current_position.y - _from.y) * _dir.x + 1e-4f)
            };
            if (_puppets < MaxPuppets && Combat.CanPuppet(u, A))
            {
                Puppet p = new Puppet(u, u.flip, this);
                if (Moves.Start(p))
                {
                    c.P = p;
                    _puppets++;
                    bool faceAlong = p.Pose.FaceRight == (_fwd > 0f);
                    c.Lean = faceAlong ? 7f : -7f;
                    p.Flash(0.05f, Color.white);
                }
            }
            _cuts.Add(c);
            Cuts++;
            Vector2 body = c.P != null ? c.P.BodyPos() : FreeBody(u);
            if (Kit.Visible(body))
            {
                if (c.P != null)
                {
                    Outline(c.P, Color.white, 0.2f);
                }
                else
                {
                    Fx.Glint(body, px, 0.16f, Color.white, 0.8f);
                }
                // the cut mark across the body, which stays until the burst: a thin white slash that only
                // starts taking the tier colour as the burst nears (it was tier-pink at once, and longer than
                // the body: a stick lying across the unit)
                float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
                Vector2 a = body + new Vector2(-_fwd * 2f, 2.5f) * vpx, b = body + new Vector2(_fwd * 2f, -2.5f) * vpx;
                c.Mark = Kit.Line(a, b, vpx, 1.6f, 1, FxLayer.Over, 25, Color.white, Color.white, Fx.WithA(_pal.Core, 1f), Fx.WithA(_pal.Edge, 1f));
                if (c.Mark != null)
                {
                    c.Mark.Held = true;
                    if (c.P == null)
                    {
                        // one that couldn't be frozen (mid-hop, too heavy, possessed) walks on: its mark goes
                        // with it (it hung in the air where it was cut while the unit walked away)
                        long id = c.Id;
                        c.Mark.Step = (q, d) =>
                        {
                            if (u.Sid() == id && u.isAlive())
                            {
                                q.Pos = FreeBody(u);
                            }
                        };
                    }
                }
                Particle s = Fx.Spawn(Art.Sparks[0], body, px, 0.1f, FxLayer.Top, 8);
                s?.Moving(_dir * 60f * px).Color(Color.white, Fx.WithA(_pal.Edge, 0f));
            }
            if (T - _cutSound > 0.08f)
            {
                _cutSound = T;
                Anim.Sound("event:/SFX/HIT/HitFlesh", u.current_tile);
            }
        }

        /// <summary>A 1-pixel outline flash around a puppet (4 copies of its silhouette behind it, 1 pixel off).</summary>
        private static void Outline(Puppet p, Color c, float life)
        {
            float px = p.Px;
            for (int i = 0; i < 4; i++)
            {
                Vector2 off = i == 0 ? new Vector2(px, 0f) : i == 1 ? new Vector2(-px, 0f) : i == 2 ? new Vector2(0f, px) : new Vector2(0f, -px);
                Fx.Ghost(p, off, 0f, life, c, Fx.WithA(c, 0.85f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>The middle of a cut enemy it couldn't freeze, as drawn (one caught mid-hop is drawn that high).</summary>
        private static Vector2 FreeBody(Actor u)
        {
            return u.current_position + new Vector2(0f, Mathf.Max(0f, u.position_height) + Mathf.Clamp(u.actor_scale, 0.04f, 0.4f) * 4f);
        }

        /// <summary>
        /// A cut enemy that is no longer there to burst: dead (or cut down by someone else, no health left),
        /// recycled, let go, or (one it couldn't freeze) picked up by the god hand or gone into a boat or a
        /// building, where the burst's blow and throw would still have found it.
        /// </summary>
        private static bool Gone(Cut c)
        {
            Actor v = c.V;
            return v == null || !Combat.Living(v) || v.Sid() != c.Id || v.isInMagnet() || v.isInsideSomething() || c.P != null && c.P.Finished;
        }

        /// <summary>Lets go of a cut enemy that is gone: its puppet, if still held, and its mark.</summary>
        private static void LetGo(Cut c)
        {
            if (c.P != null && !c.P.Finished)
            {
                c.P.Release(false);
            }
            c.Mark?.Kill();
            c.Mark = null;
        }

        /// <summary>Keeps the cut enemies frozen in the instant of the cut; lets go of any that are gone.</summary>
        private void HoldCuts(float dt)
        {
            for (int i = _cuts.Count - 1; i >= 0; i--)
            {
                Cut c = _cuts[i];
                c.Age += dt;
                if (Gone(c))
                {
                    LetGo(c);
                    _cuts.RemoveAt(i);
                    continue;
                }
                if (c.P == null)
                {
                    continue;
                }
                Pose vp = c.P.Pose;
                float jolt = Mathf.Clamp01(1f - c.Age / 0.1f);
                vp.Ground = c.At;
                vp.Height = 0f;
                vp.Rot = c.Lean * (0.4f + 0.6f * jolt);
                vp.SX = 1f;
                vp.SY = 1f;
                vp.Nudge = c.Age < 0.07f ? new Vector2(((int)(c.Age * 90f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                c.P.Pose = vp;
            }
        }

        /// <summary>Every cut enemy bursts at once. False when there was no one left to burst.</summary>
        private bool Burst()
        {
            float px = Px;
            _late = true;   // the cut is spent: whoever the moon still passes bursts at once (LateCut)
            if (_cuts.Count == 0)
            {
                return false;
            }
            bool crit = false;
            Vector2 sum = Vector2.zero;
            int n = 0;
            WorldTile tile = null;
            foreach (Cut c in _cuts)
            {
                Actor v = c.V;
                if (Gone(c))
                {
                    // gone since the last hold (this very frame): its held mark goes too (cleared with the rest
                    // and never killed, it hung in the air for good)
                    LetGo(c);
                    continue;
                }
                Vector2 body = c.P != null ? c.P.BodyPos() : FreeBody(v);
                bool vis = Kit.Visible(body);
                if (vis && c.P != null)
                {
                    // a tiered enemy flashes its own tier colour, the others white
                    Tier vt = Tiers.Of(v);
                    Overlay(c.P, vt != Tier.None ? Tiers.Color(vt) : Color.white, 0.12f);
                }
                HitResult r = Combat.Hit(A, v, MoonMult * c.CM, true, true, 0f, this);
                crit |= r.Crit;
                if (vis)
                {
                    OpenCut(body, Mathf.Clamp(v.actor_scale, 0.04f, 0.4f), px);
                }
                c.Mark?.Kill();
                c.Mark = null;
                if (c.P != null && !c.P.Finished)
                {
                    c.P.Release(true);
                }
                if (v.isAlive())
                {
                    _fly.Add(c);   // (a body just cut down flies off the line too; only a survivor counts for the chain)
                }
                sum += body;
                n++;
                tile = v.current_tile;
            }
            _cuts.Clear();
            if (n == 0)
            {
                return false;
            }
            Vector2 center = sum / n;
            if (Kit.Visible(center, 0.5f))
            {
                Kit.ImpactFrame(center, px, 0.6f, _pal, crit && Level >= 2);
                Kit.Shake(center, 0.28f, 0.01f, _k);
                // the whole cut flashes once more: a one-pixel line of light along the groove, gone at once
                // (a 2-pixel bar for 0.3 s read as a pink rail lying on the ground)
                Kit.Line(_from + _dir * 0.3f * _k, _from + _dir * _wS, px, 0.2f, 1, FxLayer.Ground, 6,
                    Color.white, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
            }
            Kit.Stop(center, 0.1f);
            Anim.Sound("event:/SFX/HIT/HitFlesh", tile ?? A.current_tile);
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile ?? A.current_tile);
            if (Dev.On) Dev.Trace("fang " + Id + " moonfall burst: " + n + " cut" + (crit ? " (crit)" : ""));
            return true;
        }

        /// <summary>
        /// On the burst, the furrow the moon scored opens for real: the whole cut is blasted into a trench at once
        /// (a God's burns). With no one left to burst, the groove still flashes as it opens, and it cracks.
        /// </summary>
        private void OpenFurrow(bool burst)
        {
            // (a blast's radius out: the performer's own tile is singed by the first blast, never blown out)
            float start = Mathf.Max(0.3f * _k, Mathf.Max(1f, Mathf.Round(_tearR)));
            // a moon still flying tears the rest of its path open right behind itself (Scorch)
            _opened = true;
            _trenchTo = Mathf.Max(_wS, start);
            if (_wS - start < 0.5f)
            {
                return;
            }
            Vector2 a = _from + _dir * start, b = _from + _dir * _wS;
            LineKit.Trench(a, b, _tearR, _tearForce, A);
            if (_god)
            {
                for (float s = start + 0.6f; s <= _wS; s += 1.2f)
                {
                    Burn(_from + _dir * s);
                }
            }
            if (burst)
            {
                return;   // the burst already flashed the groove, froze the world and shook it
            }
            float px = Px;
            Vector2 mid = (a + b) * 0.5f;
            if (Kit.Visible(mid, 0.6f) || Kit.Visible(a))
            {
                Kit.Line(a, b, px, 0.2f, 1, FxLayer.Ground, 6, Color.white, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
                Kit.Shake(mid, 0.18f, 0.006f, _k);
            }
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(mid));
        }

        /// <summary>The tick after the burst (the puppets are back in the game's hands): off the line they go.</summary>
        private void LaunchCut()
        {
            if (_fly.Count == 0)
            {
                return;
            }
            foreach (Cut c in _fly)
            {
                Actor v = c.V;
                // (one picked up by the god hand or gone into a boat since the burst is never thrown: LaunchDir
                // would set it flying from the hand, or off the deck)
                if (v == null || !v.isAlive() || v.Sid() != c.Id || Moves.Locked(v) || v.isInMagnet() || v.isInsideSomething())
                {
                    continue;
                }
                Fling(v, c.Side);
                Survived(v, c.Id);
            }
            _fly.Clear();
        }

        /// <summary>Thrown off the line, to the side it stood on (a heavy one only reels).</summary>
        private void Fling(Actor v, float sideSign)
        {
            Vector2 side = new Vector2(-_dir.y, _dir.x) * sideSign;
            float dist = Weights.Species(v) == Weight.Critter ? 1.5f : 1f;
            Combat.LaunchDir(v, (side * 0.8f + _dir * 0.6f).normalized, dist, 0.6f, sideSign * _fwd > 0f ? 560f : -560f);
            if (Weights.Heavy(v))
            {
                Combat.Stagger(v, 0.4f);
            }
        }

        /// <summary>
        /// One that lived through its cut, for the chain (the most dangerous is its target). A blow that killed
        /// leaves the body 'alive' with no health until the game buries it: the whole line of corpses counted as
        /// survivors, and the chain was left on a dead skeleton.
        /// </summary>
        private void Survived(Actor v, long id)
        {
            if (!v.hasHealth())
            {
                return;
            }
            _survivors++;
            float power = Tiers.Power(v);
            if (power > _strongestP)
            {
                _strongestP = power;
                _strongest = v;
                _strongestId = id;
            }
        }

        /// <summary>
        /// After the burst the moon is spent: whoever it still passes (one who stepped onto the line after the
        /// stillness began) is cut and bursts at once, no freeze, and flies off the line.
        /// </summary>
        private void LateCut(Actor u, float cm, float px)
        {
            Cuts++;
            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
            Vector2 body = u.current_position + new Vector2(0f, vpx * 4f);
            float side = Mathf.Sign((u.current_position.x - _from.x) * _dir.y - (u.current_position.y - _from.y) * _dir.x + 1e-4f);
            HitResult r = Combat.Hit(A, u, MoonMult * cm, true, true, 0f, this);
            if (Kit.Visible(body))
            {
                OpenCut(body, vpx, px);
                if (r.Crit)
                {
                    Kit.ImpactFrame(body, px, 0.4f, _pal, true);
                }
            }
            if (T - _cutSound > 0.08f)
            {
                _cutSound = T;
                Anim.Sound("event:/SFX/HIT/HitFlesh", u.current_tile);
            }
            if (u.isAlive())
            {
                Fling(u, side);
                Survived(u, u.Sid());
            }
            if (Dev.On) Dev.Trace("fang " + Id + " late cut on " + u.asset.id + " at " + _wS.ToString("F1") + " tiles" + (r.Killed ? " (killed)" : ""));
        }

        /// <summary>
        /// A cut opens: the mark flares into a white slash right through the body, longer than it, with a small
        /// burst behind it and sparks along the stroke (2.4-tile starbursts on every enemy, under a 4-tile impact
        /// frame, merged into one white wall over the whole line: the eye read an explosion, not six enemies cut
        /// at once).
        /// </summary>
        private void OpenCut(Vector2 body, float vpx, float px)
        {
            Vector2 s0 = body + new Vector2(-_fwd * 4f, 5f) * vpx, s1 = body + new Vector2(_fwd * 4f, -5f) * vpx;
            Kit.Line(s0, s1, vpx, 0.16f, 1, FxLayer.Top, 33, Color.white, Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
            Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], body, px * 0.24f * _k, 0.09f, FxLayer.Top, 31);
            if (burst != null)
            {
                burst.Rot = Random.Range(0f, 360f);
                burst.Sized(0.9f, 1.15f).Color(Fx.WithA(_pal.Core, 1f), Fx.WithA(_pal.Edge, 0.85f), Fx.WithA(_pal.Edge, 0f));
            }
            Fx.HitSpark(body, px, 1, (s1 - s0).normalized, 5);
        }

        /// <summary>A flat-colour copy of a unit drawn over it (a hit flash in any colour).</summary>
        private static void Overlay(Move m, Color c, float life)
        {
            Sprite s = Fx.UnitSprite(m.A);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, m.DrawPos(), 1f, life, FxLayer.Over, 3, true);
            if (p == null)
            {
                return;
            }
            p.UseEuler = true;
            p.Euler = Fx.UnitEuler(m);
            p.SX = m.A.actor_scale * m.Pose.SX;
            p.SY = m.A.actor_scale * m.Pose.SY;
            p.Color(c, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, GlowColor());
        }

        /// <summary>Moonfall: the sprite pulses in the tier colour as it charges, flashes white on the release, cools.</summary>
        private Color GlowColor()
        {
            switch (_phase)
            {
                case Phase.Wind:
                    {
                        // power gathering: the tint flickers between the white core and the tier edge (a flat
                        // pulse of the tier colour alone read as the game's red damage flash on red tiers)
                        float k = Mathf.Clamp01(_pt / _tWind);
                        float s = Mathf.Sin(_pt * 46f);
                        Color c = Color.Lerp(_pal.Edge, _pal.Core, 0.5f + 0.5f * s);
                        return Fx.WithA(c, (0.32f + 0.14f * s) * Anim.Smooth(0.05f, 1f, k));
                    }
                case Phase.Swing:
                    return _pt < 0.04f ? Fx.WithA(Color.white, 0.9f) : Fx.WithA(_pal.Edge, Mathf.Lerp(0.5f, 0f, Mathf.Clamp01((_pt - 0.04f) / 0.08f)));
                case Phase.Sheathe:
                    return Fx.WithA(_pal.Edge, Mathf.Lerp(0.45f, 0f, Mathf.Clamp01(_pt / 0.12f)));
                default:
                    return Fx.WithA(_pal.Edge, 0f);
            }
        }

        /// <summary>Keeps an effect on the unit's body.</summary>
        private void Follow(Particle p, Vector2 offset)
        {
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = BodyPos() + offset;
            }
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        private void KillWave()
        {
            foreach (Particle p in _parts)
            {
                p?.Kill();
            }
            _parts.Clear();
            _wLive = false;
            _scar?.Close();
        }

        public override void Abort()
        {
            KillWave();
            KillGlow();
            foreach (Cut c in _cuts)
            {
                c.Mark?.Kill();
                if (c.P != null && !c.P.Finished)
                {
                    c.P.Release(c.P.Pose.Height <= 0.02f);
                }
            }
            _cuts.Clear();
            _fly.Clear();
            _pending.Clear();
        }

        public override void OnEnd(bool aborted)
        {
            if (!_detached || aborted)
            {
                KillWave();
            }
            KillGlow();
            if (!aborted && _parried && A.isAlive())
            {
                Combat.Stagger(A, 0.35f);
            }
            LineScar.Sweep();
            if (Dev.On) Dev.Trace("fang " + Id + " ends" + (aborted ? " (aborted)" : "") + ": " + _hits + " hit" + (_blocked ? ", blocked" : "")
                + (_detached && !aborted ? ", the wave flies on" : "") + (EndLink != Link.None ? ", link " + EndLink : ""));
        }

        // ------------------------------------------------------------------ art --

        /// <summary>
        /// The crescents, drawn pixel by pixel at the unit's resolution, as strokes: the leading rim is an
        /// 8-connected, pixel-perfect curve (no doubled corners), the body's thickness lies behind it in whole
        /// pixels and tapers to the tips. A flying crescent is rasterized at its own heading (32 headings), never
        /// rotated as a sprite, so it stays crisp. The moon stands upright and comes in four "bulges" (seen from
        /// the side .. nearly edge-on when it travels up or down the screen). Each is a body (tinted the edge
        /// colour) and a core (the white leading rim), with a few frames of shimmer (specks of air trailing it).
        /// Blade waves are slim with sharp tips; kick waves are rounder, blunt and airy inside.
        /// </summary>
        private static class FangArt
        {
            public const float MoonR = 8.2f;
            public const float MoonSin = 0.9135f;   // sin 66: the moon is 15 pixels tall
            public const float MoonCos = 0.4067f;   // cos 66
            private const float MoonHalf = 66f;
            private static readonly float[] Bulges = { 0.55f, 0.7f, 0.85f, 1f };
            private static readonly Dictionary<int, Sprite[]> _cache = new Dictionary<int, Sprite[]>();

            /// <summary>
            /// Radius (pixels) of a flying crescent: size 0 the small one of level I, size 1 a blade of the X. The X's
            /// blades are long, shallow and thin so the cross stays open: at radius 8, 2 pixels thick, tilted 34
            /// degrees and joined at their chords, the two arms crossed ahead of the middle and merged into a 5x9
            /// blob ("K" or "|>" on screen). Now each is a near-straight slash of 12 pixels, a body tall.
            /// </summary>
            public static float FlatR(int style, int size)
            {
                return size == 0 ? 4.4f : 14f;
            }

            /// <summary>Half its sweep (degrees): level I's are short deep arcs, the X's long shallow ones.</summary>
            public static float Half(int style, int size)
            {
                return size == 0 ? (style == 0 ? 58f : 72f) : 25f;
            }

            /// <summary>
            /// Thickness at the middle (pixels). The X's blades are one clean pixel: at 45 degrees a 1.5-pixel
            /// body behind a white rim alternated colours pixel by pixel, and with the trail the X read as a
            /// checkered "#" smear.
            /// </summary>
            private static float Thick(int style, int size)
            {
                return size == 0 ? (style == 0 ? 1.8f : 2.4f) : 1f;
            }

            public static float Bulge(int i)
            {
                return Bulges[Mathf.Clamp(i, 0, 3)];
            }

            /// <summary>The heading (degrees) a crescent is actually drawn at: the nearest of 32.</summary>
            public static float Bucket(float heading)
            {
                return (Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / 11.25f) % 32) * 11.25f;
            }

            /// <summary>A flying crescent: style 0 blade / 1 kick, size 0 (I) / 1 (II), heading in degrees, layer 0 body / 1 core.</summary>
            public static Sprite[] Flat(int style, int size, float heading, int layer)
            {
                int bucket = Mathf.RoundToInt(Bucket(heading) / 11.25f);
                int key = 1 << 20 | style << 16 | size << 12 | bucket << 4 | layer;
                if (_cache.TryGetValue(key, out Sprite[] s))
                {
                    return s;
                }
                s = Draw(FlatR(style, size), Half(style, size), Thick(style, size), style, bucket * 11.25f, 1f, false, layer, 2, "sakuga_fang_" + key);
                _cache[key] = s;
                return s;
            }

            /// <summary>The standing moon (facing right; mirror the particle for left).</summary>
            public static Sprite[] Moon(int style, int bulge, int layer)
            {
                int key = 2 << 20 | style << 16 | bulge << 4 | layer;
                if (_cache.TryGetValue(key, out Sprite[] s))
                {
                    return s;
                }
                s = Draw(MoonR, MoonHalf, style == 0 ? 3.4f : 4f, style, 0f, Bulge(bulge), true, layer, 3, "sakuga_moon_" + key);
                _cache[key] = s;
                return s;
            }

            /// <summary>
            /// A crescent of radius r (pixels) around the sprite's centre, spanning +-half degrees around 'heading',
            /// 'thick' pixels at its middle; 'squash' compresses its curve along x (the moon seen at an angle) but
            /// never its thickness. The moon's core is the whole leading half of its thickness, a flying one's only
            /// the middle of its rim.
            /// </summary>
            private static Sprite[] Draw(float r, float half, float thick, int style, float heading, float squash, bool moon, int layer, int frames, string name)
            {
                int hw = Mathf.CeilToInt(r * squash) + 3, hh = Mathf.CeilToInt(r) + 3;
                if (squash >= 1f)
                {
                    hw = hh = Mathf.Max(hw, hh);
                }
                int w = hw * 2 + 1, h = hh * 2 + 1;
                float ca = Mathf.Cos(heading * Mathf.Deg2Rad), sa = Mathf.Sin(heading * Mathf.Deg2Rad);
                float hr = half * Mathf.Deg2Rad;
                int n = Mathf.Max(8, Mathf.CeilToInt(2f * hr * r / 0.25f));

                Vector2Int P(float lx, float ly)
                {
                    float x = lx * ca - ly * sa, y = lx * sa + ly * ca;
                    return new Vector2Int(hw + Mathf.FloorToInt(x + 0.5f), hh + Mathf.FloorToInt(y + 0.5f));
                }

                List<Vector2Int> rim = new List<Vector2Int>();
                List<float> rimA = new List<float>();
                Dictionary<int, Vector2> inner = new Dictionary<int, Vector2>();   // pixel -> (depth, |angle| / half)
                Dictionary<int, float> wisp = new Dictionary<int, float>();
                for (int i = 0; i <= n; i++)
                {
                    float t = -hr + 2f * hr * i / n;
                    float a = Mathf.Abs(t) / hr;
                    float shape = style == 0 ? Mathf.Pow(Mathf.Cos(a * Mathf.PI * 0.5f), 0.8f) : Mathf.Sqrt(Mathf.Max(0f, 1f - a * a * a * a));
                    float wpx = Mathf.Max(1f, thick * shape);
                    float ct = Mathf.Cos(t), st = Mathf.Sin(t);
                    Vector2Int p = P(r * ct * squash, r * st);
                    if (rim.Count == 0 || rim[rim.Count - 1] != p)
                    {
                        if (rim.Count >= 2)
                        {
                            Vector2Int pa = rim[rim.Count - 2], pb = rim[rim.Count - 1];
                            if (Mathf.Abs(pa.x - p.x) == 1 && Mathf.Abs(pa.y - p.y) == 1 && (pb.x == pa.x || pb.y == pa.y) && (pb.x == p.x || pb.y == p.y))
                            {
                                rim.RemoveAt(rim.Count - 1);   // an L corner: the line stays one pixel wide
                                rimA.RemoveAt(rimA.Count - 1);
                            }
                        }
                        rim.Add(p);
                        rimA.Add(a);
                    }
                    for (float d = 0.5f; d < wpx - 0.25f; d += 0.5f)
                    {
                        Vector2Int q = P(r * ct * squash - d * ct, r * st - d * st);
                        int key = q.y * w + q.x;
                        if (!inner.TryGetValue(key, out Vector2 old) || d < old.x)
                        {
                            inner[key] = new Vector2(d, a);
                        }
                    }
                    if (a <= 0.8f)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            float d2 = wpx + 0.6f + 0.8f * k;
                            Vector2Int q = P(r * ct * squash - d2 * ct, r * st - d2 * st);
                            wisp[q.y * w + q.x] = a;
                        }
                    }
                }
                HashSet<int> rimSet = new HashSet<int>();
                for (int i = 0; i < rim.Count; i++)
                {
                    rimSet.Add(rim[i].y * w + rim[i].x);
                }

                Sprite[] result = new Sprite[frames];
                for (int f = 0; f < frames; f++)
                {
                    Canvas c = new Canvas(w, h);
                    if (layer == 1)
                    {
                        for (int i = 0; i < rim.Count; i++)
                        {
                            if (rimA[i] <= (moon ? 0.85f : 0.6f))
                            {
                                c.Set(rim[i].x, rim[i].y, 1f, 1f);
                            }
                        }
                        if (moon)
                        {
                            foreach (KeyValuePair<int, Vector2> kv in inner)
                            {
                                if (!rimSet.Contains(kv.Key) && kv.Value.y <= 0.8f && kv.Value.x <= thick * 0.5f)
                                {
                                    c.Set(kv.Key % w, kv.Key / w, 1f, 1f);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (KeyValuePair<int, Vector2> kv in inner)
                        {
                            int x = kv.Key % w, y = kv.Key / w;
                            if (rimSet.Contains(kv.Key) || style == 1 && Art.Dither(x, y) > 0.7f)
                            {
                                continue;   // a kick wave is air: its inside is broken up
                            }
                            c.Set(x, y, 0.82f, style == 0 ? 1f : 0.75f);
                        }
                        for (int i = 0; i < rim.Count; i++)
                        {
                            c.Set(rim[i].x, rim[i].y, 1f, 1f);
                        }
                        foreach (KeyValuePair<int, float> kv in wisp)
                        {
                            int x = kv.Key % w, y = kv.Key / w;
                            if (!rimSet.Contains(kv.Key) && !inner.ContainsKey(kv.Key) && Art.Hash(x, y, f * 31 + 7) > 0.78f)
                            {
                                c.Set(x, y, 0.8f, 0.45f);   // specks of air trailing it (they flicker frame to frame)
                            }
                        }
                    }
                    result[f] = c.ToSprite(name + "_" + f, new Vector2(0.5f, 0.5f));
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Crescent Fang as a technique. Entry: Expert. The AI throws it down a line of enemies (I and II: 2 or
    /// more within 0.6 tiles of a ray in range, III: 3 or more), or at a lone archer or a fleeing enemy that
    /// melee can't reach ("kill the archer"), and (Masters and up) at a worthy foe 2.5 tiles off or more. Waves
    /// stop at walls and mountains, so the plan does too; a level I or II wave breaks on an enemy too heavy for
    /// the tier, so nothing behind one is counted.
    /// </summary>
    internal sealed class CrescentFangTech : Technique
    {
        public override string Id => "crescent_fang";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Line;

        /// <summary>A cut (or a kick) that leaves the body and flies along a line; Moonfall Fang (III) gathers first.</summary>
        public override Form Forms => Form.Blade | Form.Projectile | Form.Line;

        public override Form FormsAt(int level) => level >= 3 ? Form.Blade | Form.Projectile | Form.Line | Form.Charge | Form.Heavy : Forms;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Light : Scale.Heavy;

        public override float[] ManaCost => new[] { 15f, 30f, 55f };
        public override float[] Cooldown => new[] { 8f, 10f, 12f };

        private readonly List<Actor> _list = new List<Actor>();

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        private static float Mult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 2f : CrescentFang.MoonMult;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float range = CrescentFang.RangeOf(level, c.Tier == Tier.God);
            if (c.Forced)
            {
                return Forced(c, level, range);
            }
            int need = level >= 3 ? 3 : 2;
            if (c.Linked)
            {
                need = Mathf.Max(1, need - 1);
            }
            Plan best = null;
            Vector2 dir = c.BestRay(range, CrescentFang.Width, out int count);
            if (count >= need)
            {
                best = Line(c, level, dir, range, need);
            }
            Plan lone = Lone(c, level, range);
            if (lone != null && (best == null || lone.Score > best.Score))
            {
                best = lone;
            }
            return best;
        }

        /// <summary>How far the wave really goes along 'dir': walls, mountains, and (I, II) the first enemy too heavy to cut.</summary>
        private static float Reach(Ctx c, Vector2 dir, float range, int level)
        {
            float clear = CrescentFang.ClearLength(c.Pos, dir, range, 0.5f);
            if (level >= 3)
            {
                return clear;
            }
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                Actor v = c.Enemies[i];
                if (c.EnemyDist[i] > clear + 1f || !Combat.Grounded(v) || c.Allowed(v))
                {
                    continue;
                }
                Vector2 rel = v.current_position - c.Pos;
                float along = Vector2.Dot(rel, dir);
                if (along > 0.2f && along < clear && Mathf.Abs(rel.x * dir.y - rel.y * dir.x) <= CrescentFang.Width + v.stats["size"] * 0.5f)
                {
                    clear = along - 0.3f;
                }
            }
            return clear;
        }

        private Plan Line(Ctx c, int level, Vector2 dir, float range, int need)
        {
            float len = Reach(c, dir, range, level);
            _list.Clear();
            int n = c.OnRay(dir, len, CrescentFang.Width, _list);
            if (n < need || n == 0)
            {
                return null;
            }
            float mult = Mult(level);
            float score = 0f;
            Actor first = null;
            float firstAlong = float.MaxValue;
            int archers = 0;
            foreach (Actor v in _list)
            {
                float along = Vector2.Dot(v.current_position - c.Pos, dir);
                if (along < firstAlong)
                {
                    firstAlong = along;
                    first = v;
                }
                if (level >= 2)
                {
                    c.Allowed(v, out float cm);
                    score += c.Value(v, c.Expected(v, mult), cm);
                    if (Ranged(v))
                    {
                        archers++;
                    }
                }
            }
            if (level == 1)
            {
                // a single crescent cuts the first in line only; it's the right answer when that one is out of reach
                // (thrown in a chain at one standing 0.4 tiles off, it shattered as it left the blade: a swing)
                if (!c.Forced && firstAlong < 1.2f)
                {
                    return null;
                }
                c.Allowed(first, out float cm);
                score = c.Value(first, c.Expected(first, mult), cm) + 15f + (firstAlong >= 2.5f ? 15f : 0f) + (Ranged(first) ? 10f : 0f);
            }
            else
            {
                // the right answer to a line: more so the longer it is, the further it starts (melee would have to
                // walk there) and the more archers stand on it
                score += (level >= 3 ? 35f : 20f) + 8f * Mathf.Max(0, n - Mathf.Max(need, 2)) + (firstAlong >= 2.5f ? 12f : 0f)
                    + 10f * Mathf.Min(3, archers);
            }
            if (first != null && CrescentFang.CrossesWater(c.Pos, dir, firstAlong))
            {
                score += 25f;   // across a river: a cut that skims the water is the only way to reach them
            }
            return new Plan { Target = first, Point = c.Pos + dir * len, Dir = dir, Count = n, Score = score, Data = false };
        }

        /// <summary>
        /// An archer or a runner a few tiles off, with a clear line to it: cut it down from here. A Master or
        /// better also throws it at a worthy foe from 2.5 tiles (its duel opponent, or one at least about a
        /// third of its power): masters trade cuts across the field instead of walking up to swing.
        /// </summary>
        private Plan Lone(Ctx c, int level, float range)
        {
            // 4 to 8 tiles off (a level I crescent only flies 4: 3 to 3.8)
            float far = level <= 1 ? 3f : 4f, hi = Mathf.Min(8f, range - 0.2f);
            bool flow = c.Tier >= Tier.Master;
            float lo = flow ? Mathf.Min(far, 2.5f) : far;
            Actor opp = flow ? Duels.Opponent(c.Self) : null;
            float bar = flow ? 0.35f * Tiers.Power(c.Self) : float.MaxValue;
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                float d = c.EnemyDist[i];
                if (d < lo)
                {
                    continue;
                }
                if (d > hi)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                // (a unit in a light move, a flow strike or a blur dash, is fair game: the cut takes it over)
                if (!Combat.Grounded(v) || Moves.Locked(v) || !c.Allowed(v, out float cm))
                {
                    continue;
                }
                bool ranged = Ranged(v);
                bool reach = d >= far && (ranged || Fleeing(v));
                bool duel = opp != null && v == opp;
                bool worthy = flow && (duel || Tiers.Power(v) >= bar);
                if (!reach && !worthy)
                {
                    continue;
                }
                Vector2 dir = (v.current_position - c.Pos) / Mathf.Max(0.01f, d);
                float len = Reach(c, dir, range, level);
                if (len < d - 0.3f)
                {
                    continue;   // a wall or a giant in the way
                }
                _list.Clear();
                c.OnRay(dir, level >= 2 ? len : d + 0.3f, CrescentFang.Width, _list);
                // "kill the archer": the cut that reaches what melee can't (more so across water)
                float score = c.Value(v, c.Expected(v, Mult(level)), cm) + (reach ? (ranged ? 42f : 28f) : duel ? 30f : 20f)
                    + (CrescentFang.CrossesWater(c.Pos, dir, d) ? 25f : 0f);
                bool blocked = false;
                foreach (Actor o in _list)
                {
                    if (o == v)
                    {
                        continue;
                    }
                    float along = Vector2.Dot(o.current_position - c.Pos, dir);
                    if (level == 1 && along < d)
                    {
                        blocked = true;   // the single crescent would cut that one instead
                        break;
                    }
                    if (level >= 2)
                    {
                        c.Allowed(o, out float ocm);
                        score += c.Value(o, c.Expected(o, Mult(level)), ocm);
                    }
                }
                if (blocked)
                {
                    continue;
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = v, Point = v.current_position, Dir = dir, Count = 1 + (level >= 2 ? _list.Count - 1 : 0), Score = score, Data = true };
                }
            }
            return best;
        }

        /// <summary>God power or dev command: down the best line through the target, or straight at it, or ahead.</summary>
        private Plan Forced(Ctx c, int level, float range)
        {
            Actor t = c.ForceTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            Vector2 dir;
            if (c.ForcePoint.HasValue && (c.ForcePoint.Value - c.Pos).sqrMagnitude > 0.04f)
            {
                dir = (c.ForcePoint.Value - c.Pos).normalized;
            }
            else
            {
                Vector2 ray = c.BestRay(range, CrescentFang.Width, out int count);
                _list.Clear();
                if (count > 0)
                {
                    c.OnRay(ray, range, CrescentFang.Width, _list);
                }
                if (count > 0 && (t == null || _list.Contains(t)))
                {
                    dir = ray;
                }
                else if (t != null && (t.current_position - c.Pos).sqrMagnitude > 0.04f)
                {
                    dir = (t.current_position - c.Pos).normalized;
                }
                else
                {
                    dir = c.Self.flip ? Vector2.right : Vector2.left;
                }
            }
            Plan p = Line(c, level, dir, range, 0) ?? new Plan { Target = t, Point = c.Pos + dir * range, Dir = dir, Count = 0, Score = 1f, Data = false };
            p.Score = Mathf.Max(p.Score, 1f);
            return p;
        }

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

        private static bool Fleeing(Actor v)
        {
            try
            {
                return v.ai != null && v.isTask("run_away");
            }
            catch
            {
                return false;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            Vector2 dir = p.Dir.sqrMagnitude > 1e-6f ? p.Dir : p.Point - a.current_position;
            return CrescentFang.Begin(a, dir, p.Target, p.Level, p.Data is bool lone && lone, p.Count);
        }

        public override bool Order(Actor a, WorldTile tile, int level)
        {
            if (a == null || tile == null)
            {
                return false;
            }
            Vector2 dir = new Vector2(tile.posV3.x, tile.posV3.y) - a.current_position;
            return CrescentFang.Begin(a, dir, null, level, false);
        }
    }
}
