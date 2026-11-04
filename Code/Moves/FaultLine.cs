using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// FAULT LINE. The unit hops, drives its fist (or its blade, or a beast's forepaws) into the
    /// ground, and the earth splits: a fissure races out along a line and erupts under everyone
    /// standing on it, throwing them into the sky as the crack reaches them, like falling dominoes.
    ///
    /// Beat sheet (level II, game seconds at x1; the hop is longer when it repositions):
    ///
    ///   HOP      0.00-0.15  a quick crouch, then a hop of 0.5 tiles, the fist raised (Masters and up hop up
    ///                       to 2 tiles sideways first, trailing afterimages, to put the enemies in a line)
    ///   SLAM     0.15-0.25  the dive, then the punch: squash to 70%, a 0.6-tile flash disk, a tier shockwave,
    ///                       rocks, a small crater; a 0.05 local hold (the pose shakes with the blow)
    ///   RUN      0.25-0.60  the crack decal runs at 20 tiles/s: one continuous jagged fissure (dark rim,
    ///                       light lip, side cracks, an orange core). At each tile 2 rocks burst up with a
    ///                       dust puff and a little ring of rock spikes; each enemy on the line is struck
    ///                       x2.75 as the front reaches it, a spike punches up under it and it flies 2.5 high,
    ///                       tumbling. The unit stays crouched with its fist in the ground, trembling.
    ///   RECOVER  0.60-0.82  it rises out of the crouch, springs upright (Flow tiers: see below). The launched
    ///                       fall until ~1.2; the crack's core cools over 8 s. Leaves the Launched link (Earth
    ///                       and Sky) or Scattered.
    ///
    ///   I   a 4-tile hairline crack, dust only (no spikes, no glow, no hold), knocked up 1 tile, x1.5
    ///   III "Tectonic Rend": 10 tiles, the crack forks into a trident after 3 (centre +-20 degrees), glowing
    ///       white-hot; magma erupts from it 0.4 s after the front passes (fire, embers); launch height 4, x4.5;
    ///       the punch freezes the world (0.08 hit-stop, impact frame, hot crater); the scar stays 20 s
    ///
    /// Variants: a fist (humanoid, unarmed), a blade plunged into the ground (armed: a vertical cut of light
    /// at the impact), a stomp (beasts rear up and slam both forepaws: a wider dust burst). Tiny enemies fly
    /// comically high and far, heavy ones only stagger as the spike breaks under them, enemies too heavy for
    /// the tier take a glancing blow. The crack stops at water (a splash; steam at III) and at mountains,
    /// lava or the map edge (a burst of rock); it never runs under the attacker's feet.
    ///
    /// The land really breaks (Kit.Ravage, as the front passes each tile): I scorches the ground under the fist;
    /// II blasts a trench along the whole fissure (force 2) and a crater under the fist; III tears it open
    /// (force 3) and the magma sets the line itself alight where it bursts out, 0.4 s behind the front (a few
    /// tiles on the crack, which the grass fire then spreads from: the trident reads first, the field burns
    /// after). A God's crack runs 30% further and 25% faster, a force stronger, its trunk half again as wide,
    /// and nearly every magma burst catches. Never fire at the feet of a performer that could burn.
    ///
    /// Flow tiers (Master and up) don't wait for their crack: the fist stays planted a breath (0.13 s at
    /// Master, 0.10 at God; a little longer at III), then they rise at their tier's tempo (TierInfo.Linger)
    /// while the fissure runs on by itself (LineKit.Linger) and the Brain chains straight on: Launched
    /// (Wingbreaker) if one is still in the air, else Scattered.
    /// </summary>
    internal sealed class FaultLine : Move, LineKit.ILinger
    {
        public const float Width = 0.5f;
        public const float Fork = 3f;
        public const float ForkAngle = 20f;
        /// <summary>A God's crack runs this much further.</summary>
        public const float GodReach = 1.3f;

        /// <summary>How far each level's crack runs (tiles).</summary>
        public static float LengthOf(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 7f : 10f;
        }

        /// <summary>How far it runs for this performer (a God's tears 30% further).</summary>
        public static float LengthOf(int level, bool god)
        {
            return LengthOf(level) * (god ? GodReach : 1f);
        }

        /// <summary>Damage multiplier per level (II and III hit harder since the tiers' Might: each level decisively stronger).</summary>
        public static float MultOf(int level)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2.75f : 4.5f;
        }

        private enum Phase
        {
            Hop,
            Slam,
            Run,
            Recover
        }

        private enum Stop
        {
            Range,
            Water,
            Rock
        }

        private sealed class Branch
        {
            public Vector2 From, Dir;
            public float Start, Len, Front, FxAt;
            public Stop Why;
            public bool Ended;
            public LineScar Scar;
            public int Steps;
        }

        private struct Flown
        {
            public Actor V;
            public long Id;
            public float Power;
            public float At;
        }

        private static readonly Color Rock = new Color(0.52f, 0.5f, 0.5f, 1f);

        private readonly Vector2 _from, _to;
        private readonly Vector2 _dir, _side;
        private readonly float _fwd;
        private readonly float _k, _kc;
        private readonly bool _beast, _armed;
        private readonly Palette _pal;
        private readonly float _hopD, _tHop, _hopH, _speed;
        private readonly bool _flow, _god;
        private readonly float _len;
        // the real damage to the land: along the fissure (as the front passes each tile) and under the fist
        private readonly int _tearForce, _punchForce;
        private readonly float _tearR, _prongR, _punchR;
        // the tier's tempo: how long the fist stays planted (Flow tiers) and how fast it rises
        private readonly float _planted, _recoverDur;
        // III: the magma sets the line alight where it bursts out of the crack (a chance per burst, nearly
        // always for a God), when the burst shows, 0.4 s after the front passed (move clock)
        private readonly float _burn;
        private readonly List<KeyValuePair<float, Vector2>> _fires = new List<KeyValuePair<float, Vector2>>();

        private Phase _phase = Phase.Hop;
        private float _pt, _hold, _ghost, _run;
        private bool _entered, _struck, _tookOff, _crackDone, _lingering;
        private Vector2 _impact;
        private readonly List<Branch> _branches = new List<Branch>();
        private readonly HashSet<long> _hit = new HashSet<long>();
        private readonly List<KeyValuePair<float, Actor>> _cand = new List<KeyValuePair<float, Actor>>();
        private readonly List<Flown> _flown = new List<Flown>();
        private Particle _glow;
        private int _hits, _ignited;

        /// <summary>Cracks started / enemies thrown since the game started (dev log).</summary>
        internal static int Started, Thrown;

        private FaultLine(Actor a, Vector2 origin, Vector2 dir, int level) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _from = a.current_position;
            _to = origin;
            _dir = dir.normalized;
            _side = new Vector2(-_dir.y, _dir.x);
            _fwd = Mathf.Abs(_dir.x) > 0.15f ? Mathf.Sign(_dir.x) : (a.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            _k = K;
            _kc = Mathf.Clamp(_k, 1f, 1.6f);
            _beast = !a.asset.is_humanoid;
            _armed = !_beast && CrescentFang.Armed(a);
            _pal = Palette.For(a, Level);
            _hopD = Vector2.Distance(_from, _to);
            float baseHop = Level == 1 ? 0.13f : Level == 2 ? 0.15f : 0.18f;
            _tHop = _hopD > 0.3f ? baseHop + 0.06f * Mathf.Min(2.5f, _hopD) : baseHop;
            _hopH = (Level == 1 ? 0.35f : Level == 2 ? 0.5f : 0.7f) * _k + 0.12f * Mathf.Min(2.5f, _hopD);
            _flow = LineKit.Flow(a);
            _god = LineKit.God(a);
            _len = LengthOf(Level, _god);
            _speed = (Level == 1 ? 16f : 20f) * (_god ? 1.25f : 1f);
            _impact = _to + _dir * 0.35f * _k;
            // I: only the fist scorches the ground; II: a blasted trench (force 2); III: torn open (force 3), the
            // magma lighting the line; a God's one force stronger, the trunk half again as wide (the prongs keep
            // a mortal's width: three 5-tile blasts every tile merged the trident into one wedge)
            _tearForce = Level <= 1 ? 0 : LineKit.Force(Level == 2 ? 2 : 3, _god);
            _tearR = LineKit.Radius(1f, _god) * Mathf.Clamp(_k, 1f, 1.5f);
            _prongR = Mathf.Clamp(_k, 1f, 1.5f);
            _punchForce = Level <= 1 ? 1 : LineKit.Force(2, _god);
            _punchR = LineKit.Radius(Level >= 3 ? 2f : 1f, _god) * Mathf.Clamp(_k, 1f, 1.5f);
            _burn = Level < 3 ? 0f : _god ? 0.9f : 0.5f;
            // the higher the tier, the sooner the fist leaves the ground and the crisper it rises (Master 0.13 s
            // planted, God 0.10; III a little longer)
            float tempo = Mathf.Clamp(Tiers.Linger(a), 0.3f, 1f);
            _planted = _flow ? (Level >= 3 ? 0.16f : 0.12f) * Mathf.Min(1.1f, tempo * 2f) : float.MaxValue;
            _recoverDur = LineKit.Recover(0.22f, a);
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>
        /// How far a crack can run from 'from' along 'dir' (up to 'max') before water, lava, a mountain or the
        /// map edge, and what stopped it.
        /// </summary>
        private static float LandLength(Vector2 from, Vector2 dir, float max, out Stop why)
        {
            why = Stop.Range;
            if (World.world == null)
            {
                return 0f;
            }
            for (float s = 0f; s <= max + 0.001f; s += 0.25f)
            {
                Vector2 q = from + dir * s;
                if (q.x < 1f || q.y < 1f || q.x > MapBox.width - 1f || q.y > MapBox.height - 1f)
                {
                    why = Stop.Rock;
                    return Mathf.Max(0f, s - 0.25f);
                }
                WorldTile t = Combat.TileAt(q);
                if (t == null || t.Type == null || t.Type.block || t.Type.lava)
                {
                    why = Stop.Rock;
                    return Mathf.Max(0f, s - 0.25f);
                }
                if (t.Type.liquid)
                {
                    why = Stop.Water;
                    return Mathf.Max(0f, s - 0.25f);
                }
            }
            return max;
        }

        /// <summary>The same, for the plan (no reason needed).</summary>
        public static float LandLength(Vector2 from, Vector2 dir, float max)
        {
            return LandLength(from, dir, max, out _);
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Starts a Fault Line: hop to 'origin' (if it is a safe spot reachable in a straight hop) and split the ground along 'dir'.</summary>
        public static bool Begin(Actor a, Vector2 origin, Vector2 dir, int level)
        {
            if (!Moves.CanPerform(a) || dir.sqrMagnitude < 1e-6f)
            {
                return false;
            }
            Vector2 p = a.current_position;
            origin = Combat.Clamp(origin);
            if ((origin - p).sqrMagnitude > 0.09f && (!Combat.SafeSpot(origin) || !Combat.ClearPath(p, origin) || (origin - p).sqrMagnitude > 2.6f * 2.6f))
            {
                origin = p;
            }
            FaultLine m = new FaultLine(a, origin, dir, level);
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(m._impact + m._dir * Mathf.Min(3f, m._len * 0.4f) + new Vector2(0f, 1f));
            return true;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (_hold > 0f)
            {
                // the punch's local hold: the unit shakes with the blow, its own clock stopped
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Hop:
                    Hop(dt);
                    break;
                case Phase.Slam:
                    Slam(dt);
                    break;
                case Phase.Run:
                    Run(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
            Fires();
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("fault " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        // ---------------------------------------------------------------- hop --

        private void Hop(float dt)
        {
            float px = Px;
            if (Enter())
            {
                if (Level >= 3 && Fx.HasSilhouette)
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
                if (Level >= 3 && Kit.Visible(_impact))
                {
                    // the ground knows: hairlines of light along where it will split
                    float len = _len;
                    float life = _tHop + 0.12f;
                    float centre = LandLength(_impact, _dir, len);
                    Kit.Line(_impact, _impact + _dir * centre, px, life, 1, FxLayer.Ground, 4, Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.4f), Fx.WithA(_pal.Edge, 0f));
                    if (centre >= Fork + 0.2f)
                    {
                        Vector2 fork = _impact + _dir * Fork;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Vector2 d = Rotate(_dir, s * ForkAngle);
                            Kit.Line(fork, fork + d * LandLength(fork, d, len - Fork), px, life, 1, FxLayer.Ground, 4, Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.3f), Fx.WithA(_pal.Edge, 0f));
                        }
                    }
                }
            }
            const float crouch = 0.04f;
            if (_pt < crouch)
            {
                // anticipation
                float c = Anim.OutQuad(_pt / crouch);
                Pose.Ground = _from;
                Pose.Height = 0f;
                Pose.SX = Mathf.Lerp(1f, 1.12f, c);
                Pose.SY = Mathf.Lerp(1f, 0.84f, c);
                Pose.Rot = -6f * c;
            }
            else
            {
                if (!_tookOff)
                {
                    _tookOff = true;
                    if (Kit.Visible(_from))
                    {
                        Fx.LandingPuff(_from, px, A.current_tile);
                    }
                }
                float k = Mathf.Clamp01((_pt - crouch) / (_tHop - crouch));
                Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
                Pose.Height = _hopH * Mathf.Sin(Mathf.PI * 0.5f * Mathf.Clamp01(k / 0.75f));
                float s = Anim.OutQuad(Mathf.Clamp01(k / 0.5f));
                Pose.SX = Mathf.Lerp(0.86f, 1f, s);
                Pose.SY = Mathf.Lerp(1.2f, 1f, s);
                // the fist (blade, forepaws) raised high
                float raise = _beast ? -34f : _armed ? -28f : -24f;
                Pose.Rot = Mathf.Lerp(-6f, raise, Anim.OutCubic(k));
                if (_hopD > 0.3f && Kit.Visible(BodyPos()))
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.03f;
                        Fx.Afterimage(this, 0.14f, _pal.Ghost, _pal.GhostEnd);
                    }
                }
            }
            if (_pt >= _tHop)
            {
                Next(Phase.Slam);
            }
        }

        // --------------------------------------------------------------- slam --

        private void Slam(float dt)
        {
            const float dive = 0.04f;
            float raise = _beast ? -34f : _armed ? -28f : -24f;
            float down = _beast ? 18f : _armed ? 20f : 32f;
            Pose.Ground = _to;
            if (_pt < dive)
            {
                float k = Anim.InQuad(_pt / dive);
                Pose.Height = _hopH * (1f - k);
                Pose.Rot = Mathf.Lerp(raise, down, k);
                Pose.SX = 0.9f;
                Pose.SY = 1.2f;
                return;
            }
            float squash = Level == 1 ? 0.8f : Level == 2 ? 0.7f : 0.66f;
            if (!_struck)
            {
                _struck = true;
                Pose.Height = 0f;
                Pose.Rot = down;
                Pose.SX = 2f - squash;
                Pose.SY = squash;
                Punch();
                if (Level == 2)
                {
                    _hold = 0.05f;
                }
                return;
            }
            // holding the blow, the fist buried (II already held it locally, III froze the world on it)
            float settle = Level == 1 ? 0.06f : 0.03f;
            float e = Mathf.Clamp01((_pt - dive) / settle);
            Pose.Height = 0f;
            Pose.Rot = down - 6f * e;
            Pose.SX = Mathf.Lerp(2f - squash, 1.16f, e);
            Pose.SY = Mathf.Lerp(squash, 0.82f, e);
            Pose.Nudge = Vector2.zero;
            if (_pt >= dive + settle)
            {
                Next(Phase.Run);
            }
        }

        /// <summary>The fist meets the ground: the impact, and the crack's branches (terrain read now).</summary>
        private void Punch()
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(_impact);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            if (Level >= 2)
            {
                Anim.Sound("event:/SFX/NATURE/EarthQuake", tile);
            }
            if (Kit.Visible(_impact))
            {
                PunchFx(px, tile);
            }
            if (Level >= 3)
            {
                Kit.Stop(_impact, 0.08f);
            }
            // the ground really breaks under the fist, on the impact frame (blasted and dug, never set alight)
            LineKit.Ravage(_impact, _punchR, _punchForce, A);
            BuildBranches(px);
        }

        private void PunchFx(float px, WorldTile tile)
        {
            Vector2 ip = _impact;
            Vector2 fist = ip + new Vector2(0f, 1.5f * px);
            float k = _k;
            // the variant's own touch: a blade's cut of light, a fist's star, a beast's paws
            if (_armed)
            {
                Kit.Line(ip + new Vector2(0f, 7f * px), ip, px, 0.12f, 1, FxLayer.Top, 25, Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
                Fx.Glint(ip + new Vector2(0f, 7f * px), px, 0.16f, _pal.Core, 0.9f);
            }
            else if (_beast)
            {
                Fx.LandingPuff(ip + _side * 2f * px, px, tile);
                Fx.LandingPuff(ip - _side * 2f * px, px, tile);
            }
            else
            {
                Particle star = Fx.Spawn(Art.HitStars[Level == 1 ? 0 : 1], fist, px, 0.08f, FxLayer.Top, 20);
                star?.Sized(1.15f, 0.7f).Color(Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0f));
            }
            Kit.DustRing(ip, px, tile, Level == 1 ? 6 : 10, (Level == 1 ? 40f : 60f) * (_beast ? 1.4f : 1f), 0.5f, k);
            Kit.Debris(ip, px, tile, Level == 1 ? 3 : Level == 2 ? 6 : 8, 0.8f * k);
            if (Level == 1)
            {
                Fx.Ring(ip, px, 0.2f, 2f, 10f * k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f, new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
                return;
            }
            // a 0.6-tile flash disk, the shockwave in the tier colour, a crater
            Kit.Flash(fist, px, Mathf.RoundToInt(3f * k + (Level >= 3 ? 2f : 0f)), 0.08f, Fx.WithA(_pal.Core, 1f), FxLayer.Top, 28);
            // (a God's ring opens wider, around the wider crater it blasts)
            Kit.Shockwave(ip, px, 3f, (Level >= 3 ? 26f : 18f) * k * (_god ? 1.35f : 1f), 0.32f, Fx.WithA(_pal.Edge, 0.9f));
            Kit.Crater(ip, px, tile, Level >= 3, Level >= 3 ? 10f : 5f, true, _pal.Edge);
            if (Level >= 3)
            {
                Kit.ImpactFrame(fist + new Vector2(0f, 1f * px), px, 0.6f, _pal, false);
                Fx.FocusLines(fist, px, Fx.Count(10), 12f * k, 26f * k, 0.14f);
                Kit.Shake(ip, 0.3f, 0.01f, k);
                int embers = Fx.Count(6);
                for (int i = 0; i < embers; i++)
                {
                    Kit.Ember(ip + Random.insideUnitCircle * 4f * px, px, Random.Range(0.6f, 1.1f), new Vector2(Random.Range(-10f, 10f), Random.Range(15f, 30f)) * px);
                }
            }
            else
            {
                Kit.Shake(ip, 0.18f, 0.006f, k);
            }
        }

        private void BuildBranches(float px)
        {
            float len = _len;
            Branch centre = new Branch { From = _impact, Dir = _dir, Start = 0f };
            centre.Len = LandLength(_impact, _dir, len, out centre.Why);
            centre.FxAt = 0.5f;
            _branches.Add(centre);
            if (Level >= 3 && centre.Len >= Fork + 0.2f)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 d = Rotate(_dir, s * ForkAngle);
                    Branch b = new Branch { From = _impact + _dir * Fork, Dir = d, Start = Fork, FxAt = 0.6f };
                    b.Len = LandLength(b.From, d, len - Fork, out b.Why);
                    if (b.Len >= 0.5f)
                    {
                        _branches.Add(b);
                    }
                }
            }
            // the scars, only when someone could see them
            bool vis = Kit.Visible(_impact, 0.5f) || Kit.Visible(_impact + _dir * centre.Len, 0.5f);
            if (!vis)
            {
                return;
            }
            int seed = (int)(Id & 0x7fffffff) ^ Random.Range(0, 99999);
            for (int i = 0; i < _branches.Count; i++)
            {
                Branch b = _branches[i];
                if (b.Start > 0f && centre.Scar != null)
                {
                    // the prongs leave from where the drawn crack really is
                    b.From = centre.Scar.PointAt(b.Start);
                }
                b.Scar = LineScar.Build(b.From, b.Dir, b.Len, px, ScarSpec(i > 0), seed + i * 7919);
            }
        }

        private LineScar.Spec ScarSpec(bool prong)
        {
            switch (Level)
            {
                case 1:
                    return new LineScar.Spec
                    {
                        Width = 1,
                        TipWidth = 1,
                        Jag = 1.5f,
                        BrokenTip = true,
                        Rim = 0.45f,
                        Life = 3f
                    };
                case 2:
                    // the core cools like real heat: hot for a breath, a dull ember line for a few seconds, then
                    // only the dark fissure (evenly spaced stops kept it bright for 8 s: pink stripes on the ground)
                    return new LineScar.Spec
                    {
                        Width = 2,
                        TipWidth = 1,
                        Jag = 2f,
                        Pinch = 0.3f,
                        Branches = true,
                        Rubble = true,
                        BrokenTip = true,
                        Rim = 1f,
                        GlowUntil = 0.85f,
                        GlowWidth = 1,
                        Life = 10f,
                        GlowLife = 5f,
                        GlowColors = LineScar.Cooling(5f, new[] { 0f, 0.15f, 0.6f, 1.6f, 3.2f, 5f }, new[]
                        {
                            new Color(1f, 0.97f, 0.75f, 1f), Fx.WithA(Color.Lerp(Kit.Fire2, _pal.Edge, 0.35f), 1f), new Color(0.85f, 0.3f, 0.08f, 0.85f),
                            new Color(0.5f, 0.1f, 0.04f, 0.55f), new Color(0.3f, 0.05f, 0.03f, 0.22f), new Color(0.2f, 0.03f, 0.02f, 0f)
                        })
                    };
                default:
                    // white-hot while the magma erupts, then orange, then a long dark-red ember tail; the prongs are
                    // a pixel narrower than the trunk and have no side cracks of their own (all three at full width,
                    // all branching, read as a tree drawn on the ground)
                    return new LineScar.Spec
                    {
                        Width = prong ? 2 : 3,
                        TipWidth = 1,
                        Jag = prong ? 2f : 2.5f,
                        Pinch = prong ? 0.35f : 0.3f,
                        Branches = !prong,
                        Rubble = true,
                        BrokenTip = true,
                        Rim = 1f,
                        GlowUntil = 0.9f,
                        GlowWidth = prong ? 1 : 2,
                        Life = 20f,
                        GlowLife = 8f,
                        GlowColors = LineScar.Cooling(8f, new[] { 0f, 0.3f, 0.75f, 1.6f, 3.2f, 5.5f, 8f }, new[]
                        {
                            Color.white, Kit.Fire1, Fx.WithA(Color.Lerp(Kit.Fire2, _pal.Edge, 0.4f), 1f), Fx.WithA(Kit.Fire3, 0.85f),
                            new Color(0.45f, 0.08f, 0.04f, 0.55f), new Color(0.3f, 0.05f, 0.03f, 0.25f), new Color(0.2f, 0.03f, 0.02f, 0f)
                        })
                    };
            }
        }

        // ---------------------------------------------------------------- run --

        private void Run(float dt)
        {
            bool done = AdvanceCrack(dt);
            // crouched over the fist, pushing the power into the ground
            float e = Mathf.Clamp01(_pt / 0.3f);
            float down = _beast ? 18f : _armed ? 20f : 32f;
            Pose.Ground = _to;
            Pose.Height = 0f;
            Pose.Rot = down - 6f - 4f * e;
            Pose.SX = Mathf.Lerp(1.16f, 1.1f, e);
            Pose.SY = Mathf.Lerp(0.82f, 0.86f, e);
            Pose.Nudge = Level >= 2 ? new Vector2(((int)(_pt * 50f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            // a master doesn't kneel over its crack: the fist stays planted a breath, then it rises while the
            // fissure runs on by itself
            if (done || _pt >= _planted)
            {
                Pose.Nudge = Vector2.zero;
                Next(Phase.Recover);
            }
        }

        /// <summary>
        /// The crack runs on: each branch's front advances, striking whoever it passes and breaking the ground
        /// under it. True once every branch has run its course (or there is none).
        /// </summary>
        private bool AdvanceCrack(float dt)
        {
            if (_crackDone)
            {
                return true;
            }
            _run += dt;
            float f = _run * _speed;
            bool done = true;
            foreach (Branch b in _branches)
            {
                if (b.Ended)
                {
                    continue;
                }
                done = false;
                if (f < b.Start)
                {
                    continue;
                }
                float front = Mathf.Min(b.Len, f - b.Start);
                if (front > b.Front)
                {
                    Strike(b, b.Front, front);
                    Steps(b, front);
                    b.Front = front;
                    b.Scar?.RevealTo(front);
                }
                if (f - b.Start >= b.Len)
                {
                    EndBranch(b);
                }
            }
            _crackDone = done;
            return done;
        }

        /// <summary>Everyone the front of this branch passed since the last tick, nearest first: struck and thrown up.</summary>
        private void Strike(Branch b, float f0, float f1)
        {
            float w = Width * _kc;
            Vector2 a0 = b.From + b.Dir * f0, a1 = b.From + b.Dir * f1;
            List<Actor> near = Combat.FoesAround(A, (a0 + a1) * 0.5f, (f1 - f0) * 0.5f + w + 1f);
            _cand.Clear();
            foreach (Actor u in near)
            {
                // (a body cut down earlier this frame is still 'alive' with no health: never thrown, never owed)
                if (_hit.Contains(u.Sid()) || !u.hasHealth())
                {
                    continue;
                }
                Vector2 rel = u.current_position - b.From;
                float along = Vector2.Dot(rel, b.Dir);
                float perp = Mathf.Abs(rel.x * b.Dir.y - rel.y * b.Dir.x);
                float size = u.stats["size"] * 0.5f;
                if (perp > w + size || along < -0.25f || along > f1 + size || along < f0 - w - size)
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
                Erupt(kv.Value, b);
            }
        }

        /// <summary>The crack reaches an enemy: the ground bursts under it.</summary>
        private void Erupt(Actor v, Branch b)
        {
            _hit.Add(v.Sid());
            float px = Px;
            Vector2 at = v.current_position;
            WorldTile tile = v.current_tile;
            bool vis = Kit.Visible(at);
            // (one too heavy for this tier may answer it too, before the spike breaks on it)
            if (Combat.Reach(this, v, true) != Contact.Hit)
            {
                return;   // it got off the line in time
            }
            if (!Combat.Allowed(A, v, out float cm))
            {
                // too heavy for this tier: the spike breaks on it
                Combat.Hit(A, v, 0.5f, true, true, 0f, this);
                Combat.Stagger(v, 0.35f);
                if (vis)
                {
                    VictimSpike(at, px, tile);
                    Kit.Sparks(at + new Vector2(0f, 2f * px), px, 5, 40f, 80f, Rock);
                }
                return;
            }
            _hits++;
            float mult = MultOf(Level);
            // a body that will fly is thrown before it may die: the blow leaves it at 1 HP and the kill is
            // delivered where it lands (a corpse can't be thrown: at II and III most victims died on the
            // spot and just crumpled into the crack, and the line of bodies thrown into the sky was lost)
            bool flies = !Weights.Heavy(v);
            HitResult r = Combat.Hit(A, v, mult * cm, !flies, true, 0f, this);
            bool owed = flies && v.isAlive() && v.getHealth() <= 1;
            Vector2 body = at + new Vector2(0f, Mathf.Clamp(v.actor_scale, 0.04f, 0.4f) * 4f);
            if (vis)
            {
                if (Level == 1)
                {
                    Fx.LandingPuff(at, px, tile);
                }
                else
                {
                    VictimSpike(at, px, tile);
                }
                Fx.HitSpark(body, px, Level == 1 ? 0 : 1, Vector2.up, Level == 1 ? 3 : 5);
                if (r.Crit && Level >= 2)
                {
                    Kit.ImpactFrame(body, px, 0.45f, _pal, true);
                }
            }
            if (!v.isAlive())
            {
                return;
            }
            if (Weights.Heavy(v))
            {
                Combat.Stagger(v, 0.5f);   // it doesn't fly: it reels as the ground breaks under it
                return;
            }
            // up it goes, drifting off the line, tumbling (critters fly comically high and far)
            Vector2 rel = at - b.From;
            float sideSign = Mathf.Sign(rel.x * b.Dir.y - rel.y * b.Dir.x + 1e-4f);
            Vector2 away = (new Vector2(b.Dir.y, -b.Dir.x) * sideSign + b.Dir * 0.35f).normalized;
            bool critter = Weights.Species(v) == Weight.Critter;
            float apex = (Level == 1 ? 1f : Level == 2 ? 2.5f : 4f) * (critter ? 1.25f : 1f);
            float dist = (Level == 1 ? 0.5f : Level == 2 ? 0.9f : 1.3f) * (critter ? 1.6f : 1f);
            float spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(Level == 1 ? 420f : 560f, Level == 3 ? 1000f : 820f);
            Combat.LaunchDir(v, away, dist, apex, spin);
            Thrown++;
            _flown.Add(new Flown { V = v, Id = v.Sid(), Power = Tiers.Power(v), At = T });
            if (owed)
            {
                Owe(A, v, this);
            }
            if (Dev.On) Dev.Trace("fault " + Id + " erupts under " + v.asset.id + " x" + (mult * cm).ToString("F1") + " hp " + v.getHealth() + (owed ? " (thrown, dies where it lands)" : "") + (r.Crit ? " CRIT" : ""));
        }

        // ------------------------------------------------------- owed kills --

        /// <summary>A victim thrown by a killing blow: it flies at 1 HP and dies where it lands.</summary>
        private sealed class Debt
        {
            public Actor A, V;
            public long AId, VId;
            public Move By;
            public float Age, Free;
            public bool Up;
        }

        private static readonly List<Debt> _debts = new List<Debt>();

        [SakugaInit]
        private static void InitDebts()
        {
            Hooks.FrameTick += TickDebts;
            Hooks.WorldCleared += _debts.Clear;
        }

        private static void Owe(Actor a, Actor v, Move by)
        {
            _debts.Add(new Debt { A = a, AId = a.Sid(), V = v, VId = v.Sid(), By = by });
        }

        /// <summary>
        /// Once a frame on the move clock (whether or not a fighter is left to tick: a victim thrown by the last
        /// fighter standing still dies where it lands): a thrown victim that has landed gets its killing blow.
        /// </summary>
        private static void TickDebts(float dt)
        {
            if (_debts.Count == 0 || dt <= 0f)
            {
                return;
            }
            for (int i = _debts.Count - 1; i >= 0; i--)
            {
                Debt d = _debts[i];
                Actor v = d.V;
                d.Age += dt;
                // gone, or picked up by the god hand: no debt
                if (v == null || !v.isAlive() || v.Sid() != d.VId || v.isInMagnet() || d.Age > 10f)
                {
                    _debts.RemoveAt(i);
                    continue;
                }
                if (Moves.Locked(v))
                {
                    // caught by a follow-up technique (Wingbreaker...): never killed in the middle of the
                    // combo; if it survives it, it dies when it is put down (a light move of its own, a flow
                    // strike, is no reprieve: on the fighter's own swings a Master owed a kill lived on at 1 HP)
                    d.Up = true;
                    d.Free = 0f;
                    continue;
                }
                d.Free += dt;
                if (v.position_height > 0.05f)
                {
                    d.Up = true;
                }
                // on the ground again (or it never left it), never in mid-air: a corpse would hang there
                bool landed = v.position_height <= 0.02f;
                if (!landed || !d.Up && d.Free < 0.6f)
                {
                    continue;
                }
                _debts.RemoveAt(i);
                try
                {
                    Settle(d);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Sakuga] fault line kill: " + e);
                }
            }
        }

        private static void Settle(Debt d)
        {
            Actor v = d.V;
            if (d.A != null && d.A.isAlive() && d.A.Sid() == d.AId)
            {
                Combat.Hit(d.A, v, 1f, true, true, 99999f, d.By);
            }
            else
            {
                v.getHit(v.getHealth() * 10f + 100f, true, AttackType.Other, null, false, false, false);
            }
            if (v.is_visible && Kit.Visible(v.current_position))
            {
                // it hits the ground for good: a heavier thud of dust
                float px = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
                Kit.DustRing(v.current_position, px, v.current_tile, 5, 30f, 0.4f, Mathf.Sqrt(Mathf.Clamp(v.actor_scale / 0.1f, 0.6f, 4f)));
            }
            if (!v.isAlive() && v.position_height > 0.02f)
            {
                Tumble.Fall(v);
            }
            if (Dev.On) Dev.Trace("fault: " + v.asset.id + " lands and dies" + (v.isAlive() ? " (still alive?)" : ""));
        }

        /// <summary>
        /// The ground answers along the crack: every tile (half tile at I) a burst of dust, rocks and spikes, and
        /// (II, III) the land really splits there as the front passes: a trench blasted along the fissure, on fire
        /// at III (world damage: on or off screen alike).
        /// </summary>
        private void Steps(Branch b, float front)
        {
            float step = Level == 1 ? 0.5f : 1f;
            while (b.FxAt <= front)
            {
                Vector2 at = b.From + b.Dir * b.FxAt;
                b.FxAt += step;
                b.Steps++;
                if (_tearForce > 0)
                {
                    LineKit.Ravage(at, b.Start > 0f ? _prongR : _tearR, _tearForce, A);
                }
                if (Level >= 3 && (b.Steps & 1) == 1)
                {
                    // where the magma bursts out (StepFx), the line catches fire as the burst shows
                    QueueFire(at, 0.4f);
                }
                if (Kit.Visible(at))
                {
                    StepFx(at, b.Steps, b);
                }
            }
        }

        /// <summary>III: the tile at 'at' may catch fire 'delay' move seconds from now (when the magma there shows).</summary>
        private void QueueFire(Vector2 at, float delay)
        {
            if (_burn > 0f && Random.value < _burn)
            {
                _fires.Add(new KeyValuePair<float, Vector2>(T + delay, at));
            }
        }

        /// <summary>Lights the queued tiles whose moment has come (on or off screen alike).</summary>
        private void Fires()
        {
            for (int i = _fires.Count - 1; i >= 0; i--)
            {
                if (T >= _fires[i].Key)
                {
                    if (LineKit.Ignite(_fires[i].Value, A, _to))
                    {
                        _ignited++;
                    }
                    _fires.RemoveAt(i);
                }
            }
        }

        private void StepFx(Vector2 at, int n, Branch b)
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(at);
            Color soil = Fx.SoilColor(tile);
            Color dust = Fx.Lighten(soil, 0.35f);
            // dust thrown up out of the crack: small, quick puffs (big slow ones merged into one cloud bank that
            // hid the enemies being knocked up)
            {
                Vector2 off = _side * Random.Range(-2f, 2f) * px;
                Particle d = Fx.Spawn(Art.Dust[Random.Range(0, Level == 1 ? 2 : 3)], at + off, px, Random.Range(0.3f, 0.42f), FxLayer.Top, 6);
                d?.Moving(off * 4f + _dir * 5f * px, Random.Range(10f, 16f) * px, 3f)
                    .Sized(0.5f, 0.95f)
                    .Color(Fx.WithA(dust, Level == 1 ? 0.6f : 0.65f), Fx.WithA(dust, 0.35f), Fx.WithA(dust, 0f));
            }
            if (Level == 1)
            {
                // pebbles hop out on both sides of the hairline
                for (int i = 0; i < 2; i++)
                {
                    Particle peb = Fx.Spawn(Art.Chunks[6], at + _side * (i == 0 ? 1f : -1f) * px, px, 0.45f, FxLayer.Top, 4);
                    if (peb == null)
                    {
                        break;
                    }
                    peb.Moving(_side * (i == 0 ? 1f : -1f) * Random.Range(5f, 12f) * px, Random.Range(28f, 42f) * px, 0f, 300f * px);
                    peb.Bounce = 0.3f;
                    peb.Color(soil, soil, Fx.WithA(soil, 0f));
                }
                return;
            }
            // rocks bursting up
            int rocks = Level == 2 ? 2 : 1;
            for (int i = 0; i < rocks; i++)
            {
                float s = i == 0 ? 1f : -1f;
                Color c = Random.value < 0.6f ? StoneColor(soil) : Fx.Darken(StoneColor(soil), 0.25f);
                Particle r = Fx.Spawn(Art.Chunks[Random.Range(0, 6)], at, px, Random.Range(0.7f, 1f), FxLayer.Top, 4);
                if (r == null)
                {
                    break;
                }
                r.Moving(_side * s * Random.Range(10f, 24f) * px + _dir * Random.Range(-4f, 8f) * px, Random.Range(50f, 85f) * px, 0f, 320f * px);
                r.Bounce = 0.3f;
                r.Spin = Random.Range(-600f, 600f);
                r.Color(c, c, c, Fx.WithA(c, 0f));
            }
            // rock spikes jutting out of the crack as the front passes: most steps, alternating sides, now and
            // then a small twin; they punch up and crumble at once (standing half a second, one a tile, three
            // branches at III, they lined the crack like a fallen picket fence). On a crack running up or down
            // the screen they lean away from it.
            float side = (n & 1) == 0 ? 1f : -1f;
            float chance = Level >= 3 && b.Start > 0f ? 0.5f : 0.7f;
            if (Random.value < chance)
            {
                Vector2 off = _side * side;
                Vector2 sp = at + off * Random.Range(1.5f, 3f) * px + _dir * Random.Range(-2f, 2f) * px;
                Spike(sp, px, LeanVariant(off), Random.Range(0.3f, 0.42f), SpikeColor(soil), Random.Range(0f, 0.04f));
                if (Random.value < 0.25f)
                {
                    Vector2 tw = at - off * Random.Range(1f, 2.5f) * px + _dir * Random.Range(-1f, 3f) * px;
                    Spike(tw, px, 0, Random.Range(0.25f, 0.35f), SpikeColor(soil), Random.Range(0.03f, 0.08f));
                }
            }
            if (Level == 2 || (n & 1) == 0)
            {
                Fx.Ring(at, px, 0.25f, 2f, 7f * _k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0f));
            }
            if (Level >= 3 && (n & 1) == 1)
            {
                Magma(at, px, 0.4f);
            }
        }

        /// <summary>Magma bursting out of the crack 'delay' seconds after the front passed: fire, a glow, embers.</summary>
        private void Magma(Vector2 at, float px, float delay)
        {
            // the crack flares where it bursts
            Particle glow = Fx.Spawn(Art.Disk(2, 0.85f, 0.35f), at + new Vector2(0f, 1f * px), px, 0.14f, FxLayer.Top, 28);
            if (glow != null)
            {
                glow.Delay = delay;
                glow.Sized(0.8f, 1.2f).Color(new Color(1f, 0.95f, 0.7f, 1f), Fx.WithA(Kit.Fire1, 0.8f), Fx.WithA(Kit.Fire2, 0f));
            }
            // a jet, not a ball: a small flame tongue spat straight up (fast, then braking) and a column of
            // sparks after it (full-size puffs drifting up read as orange balls rolling along the crack)
            Particle fire = Fx.Spawn(Art.Puffs[0], at + _side * Random.Range(-1f, 1f) * px, px, Random.Range(0.26f, 0.36f), FxLayer.Top, 7);
            if (fire != null)
            {
                fire.Delay = delay + Random.Range(0f, 0.05f);
                fire.Moving(new Vector2(Random.Range(-2f, 2f), 0f) * px, Random.Range(38f, 52f) * px, 5f)
                    .Sized(0.45f, 0.8f)
                    .Color(Fx.WithA(Kit.Core, 1f), Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Kit.Fire2, 0.75f), Fx.WithA(Kit.Fire3, 0.3f), Fx.WithA(Kit.Smoke, 0f));
                fire.SY = 1.35f;
            }
            int embers = Fx.Count(3);
            for (int i = 0; i < embers; i++)
            {
                Particle e = Fx.Spawn(Art.Pixel, at + new Vector2(Random.Range(-1.5f, 1.5f), 0f) * px, px, Random.Range(0.45f, 0.8f), FxLayer.Top, 9);
                if (e == null)
                {
                    break;
                }
                e.Delay = delay + Random.Range(0f, 0.1f);
                e.Moving(new Vector2(Random.Range(-6f, 6f), 0f) * px, Random.Range(45f, 75f) * px, 0.8f, 140f * px)
                    .Color(new Color(1f, 0.95f, 0.6f, 1f), Kit.Fire2, Fx.WithA(Kit.Fire3, 0f));
            }
        }

        /// <summary>A spike's shape: leaning away from a crack that runs up or down the screen; on a flat one, mostly upright.</summary>
        private static int LeanVariant(Vector2 off)
        {
            if (off.x > 0.6f)
            {
                return 1;
            }
            if (off.x < -0.6f)
            {
                return 2;
            }
            float r = Random.value;
            return r < 0.6f ? 0 : r < 0.8f ? 1 : 2;
        }

        /// <summary>Broken rock: the ground's colour pulled towards grey stone.</summary>
        private static Color StoneColor(Color soil)
        {
            return Color.Lerp(soil, Rock, 0.55f);
        }

        /// <summary>Rock spikes: mostly grey stone with a little of the ground's colour (at 45% soil they read as wooden stakes on grass).</summary>
        private static Color SpikeColor(Color soil)
        {
            return Color.Lerp(soil, Rock, 0.72f);
        }

        /// <summary>The spike that punches up under a victim (and a puff of the ground it broke).</summary>
        private void VictimSpike(Vector2 at, float px, WorldTile tile)
        {
            Color soil = Fx.SoilColor(tile);
            Spike(at, px, 3, 0.55f, SpikeColor(soil));
            Kit.DustRing(at, px, tile, 5, 35f, 0.4f, _k);
            if (Level >= 3)
            {
                Magma(at, px, 0.12f);
            }
        }

        /// <summary>A rock spike rising out of the ground ('delay' s later): it shoots up in 0.06 s, stands, then crumbles.</summary>
        private static void Spike(Vector2 at, float px, int variant, float life, Color c, float delay = 0f)
        {
            Sprite[] frames = SpikeArt.Get(variant);
            Particle p = Fx.Spawn(frames[0], at, px, life, FxLayer.Behind, 4);
            if (p == null)
            {
                return;
            }
            p.Snap = true;
            p.Delay = delay;   // (never mirrored: the light stays on the top-left face)
            p.Color(c);
            p.Step = (q, d) =>
            {
                int f;
                if (q.Age < 0.06f)
                {
                    f = Mathf.Min(SpikeArt.Rise - 1, (int)(q.Age / 0.06f * SpikeArt.Rise));
                }
                else if (q.Age < q.Life - 0.18f)
                {
                    f = SpikeArt.Rise;
                }
                else
                {
                    f = SpikeArt.Rise + 1 + Mathf.Min(SpikeArt.Crumble - 1, (int)((q.Age - (q.Life - 0.18f)) / 0.18f * SpikeArt.Crumble));
                }
                Sprite s = frames[Mathf.Clamp(f, 0, frames.Length - 1)];
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
            };
        }

        /// <summary>A branch has run its course: how it ends depends on what stopped it.</summary>
        private void EndBranch(Branch b)
        {
            if (b.Ended)
            {
                return;
            }
            b.Ended = true;
            b.Scar?.RevealAll();
            b.Scar?.Close();
            Vector2 end = b.From + b.Dir * b.Len;
            float px = Px;
            if (b.Why == Stop.Range && _tearForce > 0)
            {
                // the end bursts open (the trench's last step may lie up to a tile short of it), and at III the
                // magma pillar there sets it alight
                LineKit.Ravage(end, b.Start > 0f ? _prongR : _tearR, _tearForce, A);
                QueueFire(end, 0.08f);
            }
            if (!Kit.Visible(end))
            {
                return;
            }
            WorldTile tile = Combat.TileAt(end + b.Dir * 0.3f);
            switch (b.Why)
            {
                case Stop.Water:
                    {
                        // it runs into the shore: a splash (steam where it is hot)
                        Kit.Splash(end + b.Dir * 0.25f, px, tile, Level == 1 ? 0.5f : 0.9f);
                        if (Level >= 3)
                        {
                            for (int i = 0; i < Fx.Count(3); i++)
                            {
                                Particle s = Fx.Spawn(Art.Dust[Random.Range(2, 5)], end + Random.insideUnitCircle * 2f * px, px, Random.Range(0.8f, 1.1f), FxLayer.Top, 6);
                                s?.Moving(new Vector2(Random.Range(-3f, 3f), 0f) * px, Random.Range(10f, 16f) * px, 1.5f).Sized(0.6f, 1.2f)
                                    .Color(new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.6f), new Color(1f, 1f, 1f, 0f));
                            }
                        }
                        break;
                    }
                case Stop.Rock:
                    Kit.Debris(end, px, Combat.TileAt(end), Level == 1 ? 3 : 6, 0.8f * _k);
                    Kit.DustRing(end, px, Combat.TileAt(end), 6, 40f, 0.45f, _k);
                    Anim.Sound("event:/SFX/HIT/HitStone", Combat.TileAt(end));
                    break;
                default:
                    {
                        WorldTile t = Combat.TileAt(end);
                        if (Level == 1)
                        {
                            Kit.DustRing(end, px, t, 5, 30f, 0.4f, _k);
                            break;
                        }
                        // the end bursts: a pillar of rock (and magma at III)
                        Spike(end, px, 3, 0.7f, SpikeColor(Fx.SoilColor(t)));
                        Kit.Debris(end, px, t, Level == 2 ? 5 : 7, 0.9f * _k);
                        Particle burst = Fx.Spawn(Art.HitStars[1], end + new Vector2(0f, 3f * px), px, 0.08f, FxLayer.Top, 20);
                        burst?.Sized(1.1f, 0.8f).Color(Color.white, Fx.WithA(Kit.Fire1, 0.8f), Fx.WithA(Kit.Fire2, 0f));
                        if (Level >= 3)
                        {
                            Magma(end, px, 0.05f);
                            Magma(end + _side * 2f * px, px, 0.15f);
                            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", t);
                        }
                        break;
                    }
            }
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            // (a Flow tier rose before its crack ran out: the fissure keeps running meanwhile)
            AdvanceCrack(dt);
            float px = Px;
            if (Enter())
            {
                if (!_flow)
                {
                    A.startAttackCooldown();   // (Flow tiers never swing like a peasant: no swing timer to stall them)
                }
                if (Level >= 2 && Kit.Visible(BodyPos()))
                {
                    Vector2 off = new Vector2(_fwd * 3f, Pose.PivotUp * 1.2f) * px;
                    // (a Flow tier is on its way before a long glint would fade: it would hang where it rose)
                    Particle g = Fx.Glint(BodyPos() + off, px, _flow ? 0.12f : 0.2f, Level >= 3 ? _pal.Core : Color.white, Level >= 3 ? 1f : 0.8f);
                    if (g != null)
                    {
                        g.Step = (q, d) => q.Pos = BodyPos() + off;
                    }
                }
            }
            // higher tiers spring up faster (the same spring, played at the tier's tempo)
            float dur = _recoverDur;
            float st = _pt * (0.22f / dur);
            float k = Mathf.Clamp01(_pt / dur);
            float down = _beast ? 18f : _armed ? 20f : 32f;
            float amp = Anim.Spring(st, 9f, 18f);
            Pose.Ground = _to;
            Pose.Height = 0f;
            Pose.Rot = Mathf.Lerp(down - 10f, 0f, Anim.OutCubic(k));
            Pose.SX = 1f + 0.12f * amp;
            Pose.SY = 1f - 0.14f * amp;
            Pose.Nudge = Vector2.zero;
            if (_pt >= dur)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                KillGlow();
                Link();
                if (!_crackDone || _fires.Count > 0)
                {
                    // the performer is free; the earth goes on splitting (and the magma burning) by itself
                    _lingering = true;
                    LineKit.Linger(this);
                }
                Finished = true;
            }
        }

        /// <summary>
        /// The chain it leaves, the one the performer has a follow-up for: Launched on the most dangerous enemy
        /// in the air (or just thrown) for an aerial follow-up, otherwise Scattered on the most dangerous one
        /// thrown off the line (a link nothing takes holds the Brain for its whole window). With neither, still
        /// Launched if one flies: a hunting partner may take the hand-off.
        /// </summary>
        private void Link()
        {
            Actor air = null, thrown = null;
            float airP = -1f, thrownP = -1f;
            foreach (Flown f in _flown)
            {
                Actor v = f.V;
                if (v == null || !v.isAlive() || v.Sid() != f.Id || v.isInMagnet())
                {
                    continue;
                }
                if (f.Power > thrownP)
                {
                    thrownP = f.Power;
                    thrown = v;
                }
                // in the air, or thrown a moment ago (its height may not have built up yet)
                bool up = v.position_height >= 0.15f || T - f.At < 0.25f;
                if (up && f.Power > airP)
                {
                    airP = f.Power;
                    air = v;
                }
            }
            if (air != null && LineKit.CanChain(A, Sakuga.Link.Launched))
            {
                EndLink = Sakuga.Link.Launched;
                EndTarget = air;
            }
            else if (thrown != null && LineKit.CanChain(A, Sakuga.Link.Scattered))
            {
                EndLink = Sakuga.Link.Scattered;
                EndTarget = thrown;
            }
            else if (air != null)
            {
                // nothing of its own takes either: Launched still lets a hunting partner take the hand-off
                // (Teamwork), as before
                EndLink = Sakuga.Link.Launched;
                EndTarget = air;
            }
        }

        // ------------------------------------------------------------ linger --

        /// <summary>After the move: the crack runs on (struck enemies, the trench, the scar) until every branch is done.</summary>
        bool LineKit.ILinger.Linger(float dt)
        {
            if (!Valid)
            {
                // the performer is gone (and its unit may be recycled): the crack stops where it is
                CloseScars();
                _fires.Clear();
                if (Dev.On) Dev.Trace("fault " + Id + " crack stops: performer gone");
                return false;
            }
            T += dt;
            bool running = !AdvanceCrack(dt);
            Fires();
            if (running || _fires.Count > 0)
            {
                return true;
            }
            LineScar.Sweep();
            if (Dev.On) Dev.Trace("fault " + Id + " crack done: " + _hits + " thrown, " + _branches.Count + " branch(es), " + _ignited + " tile(s) set alight");
            return false;
        }

        void LineKit.ILinger.Drop()
        {
            CloseScars();
            _fires.Clear();
        }

        private void CloseScars()
        {
            foreach (Branch b in _branches)
            {
                b.Ended = true;
                b.Scar?.Close();
            }
            _crackDone = true;
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Color c;
            switch (_phase)
            {
                case Phase.Hop:
                    {
                        float k = Mathf.Clamp01(_pt / Mathf.Max(0.01f, _tHop));
                        c = Fx.WithA(_pal.Edge, 0.5f * k * (0.75f + 0.25f * Mathf.Sin(_pt * 50f)));
                        break;
                    }
                case Phase.Slam:
                    c = _struck ? Fx.WithA(Color.white, 0.9f) : Fx.WithA(_pal.Edge, 0.6f);
                    break;
                case Phase.Run:
                    c = Fx.WithA(_pal.Edge, Mathf.Lerp(0.5f, 0f, Mathf.Clamp01(_pt / 0.25f)));
                    break;
                default:
                    c = Fx.WithA(_pal.Edge, 0f);
                    break;
            }
            Fx.Glue(_glow, this, c);
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        public override void Abort()
        {
            KillGlow();
            CloseScars();
            _fires.Clear();
        }

        public override void OnEnd(bool aborted)
        {
            KillGlow();
            if (!_lingering || aborted)
            {
                CloseScars();
            }
            LineScar.Sweep();
            if (Dev.On) Dev.Trace("fault " + Id + " ends" + (aborted ? " (aborted)" : "") + ": " + _hits + " thrown, " + _branches.Count + " branch(es)" + (_lingering && !aborted ? ", the crack runs on" : "") + (EndLink != Sakuga.Link.None ? ", link " + EndLink : ""));
        }

        // ------------------------------------------------------------------ art --

        /// <summary>
        /// Rock spikes, pixel by pixel: a leaning triangle lit from the left (light face, body, shadow face),
        /// in frames that rise out of the ground (only the top rows show, sitting on the ground line), then
        /// stand, then crumble away (dithered holes). Four shapes; the last is the big one that throws victims.
        /// </summary>
        private static class SpikeArt
        {
            public const int Rise = 3, Crumble = 3;
            private static Sprite[][] _sets;

            public static Sprite[] Get(int variant)
            {
                if (_sets == null)
                {
                    _sets = new[] { Make(3, 5, 0f, 0), Make(4, 7, 1f, 1), Make(3, 6, -1f, 2), Make(5, 9, 0.5f, 3) };
                }
                return _sets[Mathf.Clamp(variant, 0, _sets.Length - 1)];
            }

            private static Sprite[] Make(int b, int h, float lean, int v)
            {
                int w = b + 3;
                float cx = w * 0.5f - lean * 0.5f;
                // the full spike: value per pixel (0 = empty)
                float[,] full = new float[w, h];
                for (int y = 0; y < h; y++)
                {
                    float k = y / (float)h;
                    float half = b * 0.5f * (1f - k) + 0.3f;
                    float mid = cx + lean * k;
                    int x0 = int.MaxValue, x1 = int.MinValue;
                    for (int x = 0; x < w; x++)
                    {
                        if (Mathf.Abs(x + 0.5f - mid) <= half)
                        {
                            x0 = Mathf.Min(x0, x);
                            x1 = Mathf.Max(x1, x);
                        }
                    }
                    if (x0 > x1)
                    {
                        continue;
                    }
                    for (int x = x0; x <= x1; x++)
                    {
                        full[x, y] = x == x0 ? 1f : x == x1 && x1 > x0 ? 0.6f : 0.82f;
                    }
                }
                Sprite[] frames = new Sprite[Rise + 1 + Crumble];
                for (int f = 0; f < frames.Length; f++)
                {
                    Canvas c = new Canvas(w, h);
                    int rows = f < Rise ? Mathf.Max(1, Mathf.RoundToInt(h * (f + 1f) / (Rise + 1f))) : h;
                    float holes = f > Rise ? (f - Rise) / (float)(Crumble + 1) : 0f;
                    for (int y = 0; y < rows; y++)
                    {
                        int src = h - rows + y;   // rising: the top of the spike shows first, on the ground line
                        for (int x = 0; x < w; x++)
                        {
                            float val = full[x, src];
                            if (val <= 0f)
                            {
                                continue;
                            }
                            if (holes > 0f && Art.Hash(x, y, v * 17 + 3) < holes + (y / (float)h) * holes)
                            {
                                continue;   // crumbling from the tip down
                            }
                            c.Set(x, y, val, 1f);
                        }
                    }
                    frames[f] = c.ToSprite("sakuga_spike_" + v + "_" + f, new Vector2(0.5f, 0f));
                }
                return frames;
            }
        }
    }

    /// <summary>
    /// Fault Line as a technique. Entry: Master. The AI splits the ground under a line of enemies: 3 / 3 / 4
    /// or more within 0.5 tiles of the line (III also when its trident covers one more; one fewer in a chain
    /// after Down or Scattered: it throws them up for Wingbreaker and friends), with the attacker
    /// and the line on land. Masters and up hop up to 2 tiles sideways first to line the enemies up (only onto
    /// a safe spot, in a clear straight hop). The crack stops at water and mountains, so the plan does too.
    /// A Master or better also splits it under one worthy foe 1.5 tiles off or more (its duel opponent, or one
    /// of about a third of its power or more): one on one, the masters tear up the ground between them.
    /// </summary>
    internal sealed class FaultLineTech : Technique
    {
        public override string Id => "fault_line";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Line;

        /// <summary>A fist into the ground: a fissure races along a line and erupts under everyone on it.</summary>
        public override Form Forms => Form.Ground | Form.Line | Form.Punch | Form.Heavy;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => new[] { 20f, 35f, 60f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };

        private const int MaxCand = 10;
        private readonly List<Actor> _cands = new List<Actor>();
        private readonly bool[] _on = new bool[MaxCand];
        private readonly List<Line> _lines = new List<Line>();

        private struct Line
        {
            public Vector2 Origin, Dir;
            public int Raw;
            public float Hop;
        }

        /// <summary>
        /// Enemies knocked down are lying on the ground a fissure can throw into the sky (Down), and a crowd just
        /// blown apart is often strung out along the line it flew (Scattered): Earth and Sky combos start here.
        /// </summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Down || link == Link.Scattered;
        }

        /// <summary>
        /// Enemies needed on the line: 3 / 3 / 4 (one fewer in a chain). It used to want 3 / 4 / 5 within half a tile
        /// of one line, a formation that hardly ever happens: in the test battles the AI never once chose it.
        /// </summary>
        private static int Need(int level, bool linked)
        {
            int need = level <= 2 ? 3 : 4;
            return linked ? need - 1 : need;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float len = FaultLine.LengthOf(level, c.Tier == Tier.God);
            int need = Need(level, c.Linked);
            float k = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
            float w = FaultLine.Width * Mathf.Clamp(k, 1f, 1.6f);
            float reachIn = 0.35f * k;

            // who could be on a line: grounded, allowed, not mid-move, close enough
            _cands.Clear();
            for (int i = 0; i < c.Enemies.Count && _cands.Count < MaxCand; i++)
            {
                Actor v = c.Enemies[i];
                // (a unit in a light move, a flow strike or a blur dash, is fair game: the crack takes it over)
                if (c.EnemyDist[i] > len + 2.5f || !Combat.Grounded(v) || Moves.Locked(v) || !c.Allowed(v))
                {
                    continue;
                }
                _cands.Add(v);
            }
            if (!c.Forced && _cands.Count < need)
            {
                return Single(c, level, len, w, reachIn);
            }

            // candidate lines: straight at each enemy from here, and (Masters and up) through each pair after a sideways hop
            _lines.Clear();
            bool hop = c.Tier >= Tier.Master;
            for (int i = 0; i < _cands.Count; i++)
            {
                Vector2 d = _cands[i].current_position - c.Pos;
                if (d.sqrMagnitude > 0.09f)
                {
                    AddLine(c.Pos, d.normalized, 0f, len, w, reachIn, level);
                }
            }
            if (hop)
            {
                for (int i = 0; i < _cands.Count; i++)
                {
                    for (int j = i + 1; j < _cands.Count; j++)
                    {
                        Vector2 pi = _cands[i].current_position, pj = _cands[j].current_position;
                        Vector2 d = pj - pi;
                        if (d.sqrMagnitude < 0.36f)
                        {
                            continue;
                        }
                        d.Normalize();
                        Vector2 foot = pi + d * Vector2.Dot(c.Pos - pi, d);
                        float ai = Vector2.Dot(pi - foot, d), aj = Vector2.Dot(pj - foot, d);
                        if (ai < 0f && aj < 0f)
                        {
                            d = -d;
                            ai = -ai;
                            aj = -aj;
                        }
                        if (Mathf.Min(ai, aj) < 0.6f)
                        {
                            continue;   // it would have to land among them
                        }
                        float lateral = Vector2.Distance(c.Pos, foot);
                        if (lateral > 2f)
                        {
                            continue;
                        }
                        if (lateral < 0.35f)
                        {
                            AddLine(c.Pos, d, 0f, len, w, reachIn, level);
                        }
                        else
                        {
                            AddLine(foot, d, lateral, len, w, reachIn, level);
                        }
                    }
                }
            }
            if (_lines.Count == 0 && !c.Forced)
            {
                return Single(c, level, len, w, reachIn);
            }

            // the best few by raw count, checked against the terrain (and the hop's safety)
            _lines.Sort((x, y) => x.Raw != y.Raw ? y.Raw.CompareTo(x.Raw) : x.Hop.CompareTo(y.Hop));
            Actor force = c.ForceTarget;
            Plan best = null;
            int tries = 0;
            foreach (Line l in _lines)
            {
                if (tries >= 4 || (!c.Forced && l.Raw < need))
                {
                    break;
                }
                tries++;
                Plan p = Evaluate(c, level, l, len, w, reachIn, need, force);
                if (p != null && (best == null || p.Score > best.Score))
                {
                    best = p;
                }
            }
            if (best == null && c.Forced)
            {
                best = ForcedLine(c, level, len, w, reachIn);
            }
            else if (!c.Forced)
            {
                Plan one = Single(c, level, len, w, reachIn);
                if (one != null && (best == null || one.Score > best.Score))
                {
                    best = one;
                }
            }
            return best;
        }

        /// <summary>
        /// A worthy foe (its duel opponent, or one at least about a third of its power) for a Master or better:
        /// the earth opens under that one alone and throws it into the sky (Earth and Sky: an aerial follow-up
        /// takes the Launched link). Straight at it from where it stands (1.5 tiles off at least), on land the
        /// crack really reaches. So two masters (two Gods) fighting one on one tear the ground between them.
        /// </summary>
        private Plan Single(Ctx c, int level, float len, float w, float reachIn)
        {
            if (c.Forced || c.Tier < Tier.Master)
            {
                return null;
            }
            Actor t = c.Focus;
            if (t == null || !t.isAlive() || !_cands.Contains(t))
            {
                return null;   // (candidates: grounded, allowed, free, within reach)
            }
            bool duel = Duels.Opponent(c.Self) == t;
            if (!duel && Tiers.Power(t) < 0.35f * Tiers.Power(c.Self))
            {
                return null;
            }
            float d = c.Dist(t);
            if (d < 1.5f || d > len - 0.3f)
            {
                return null;
            }
            Line l = new Line { Origin = c.Pos, Dir = (t.current_position - c.Pos) / d, Hop = 0f };
            Plan p = Evaluate(c, level, l, len, w, reachIn, 1, null);
            if (p == null || !_hit.Contains(t))
            {
                return null;
            }
            p.Target = t;
            p.Score += duel ? 20f : 10f;
            return p;
        }

        private void AddLine(Vector2 origin, Vector2 dir, float hop, float len, float w, float reachIn, int level)
        {
            Vector2 impact = origin + dir * reachIn;
            int raw = Count(impact, dir, len, len, w, level, null, out _);
            if (raw > 0)
            {
                _lines.Add(new Line { Origin = origin, Dir = dir, Raw = raw, Hop = hop });
            }
        }

        /// <summary>
        /// Enemies within 'w' of the crack from 'impact' along 'dir' ('centre' tiles of land), plus at III the
        /// trident's prongs (if the crack reaches the fork). 'into' collects them; 'prongs' ignores terrain when
        /// 'centre' is the full length (the raw count).
        /// </summary>
        private int Count(Vector2 impact, Vector2 dir, float len, float centre, float w, int level, List<Actor> into, out int line)
        {
            int n = _cands.Count;
            for (int i = 0; i < n; i++)
            {
                _on[i] = false;
            }
            line = Mark(impact, dir, centre, w);
            int total = line;
            if (level >= 3 && centre >= FaultLine.Fork + 0.2f)
            {
                Vector2 fork = impact + dir * FaultLine.Fork;
                bool raw = into == null;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 d = Rotate(dir, s * FaultLine.ForkAngle);
                    float pl = raw ? len - FaultLine.Fork : FaultLine.LandLength(fork, d, len - FaultLine.Fork);
                    total += Mark(fork, d, pl, w);
                }
            }
            if (into != null)
            {
                for (int i = 0; i < n; i++)
                {
                    if (_on[i])
                    {
                        into.Add(_cands[i]);
                    }
                }
            }
            return total;
        }

        private int Mark(Vector2 from, Vector2 dir, float len, float w)
        {
            int added = 0;
            for (int i = 0; i < _cands.Count; i++)
            {
                if (_on[i])
                {
                    continue;
                }
                Actor v = _cands[i];
                Vector2 rel = v.current_position - from;
                float along = Vector2.Dot(rel, dir);
                if (along < -0.2f || along > len + 0.2f)
                {
                    continue;
                }
                if (Mathf.Abs(rel.x * dir.y - rel.y * dir.x) <= w + v.stats["size"] * 0.5f)
                {
                    _on[i] = true;
                    added++;
                }
            }
            return added;
        }

        private readonly List<Actor> _hit = new List<Actor>();

        private Plan Evaluate(Ctx c, int level, Line l, float len, float w, float reachIn, int need, Actor force)
        {
            if (l.Hop > 0f && (!Combat.SafeSpot(l.Origin) || !Combat.ClearPath(c.Pos, l.Origin)))
            {
                return null;   // never hop onto a wall, water, lava or fire
            }
            Vector2 impact = l.Origin + l.Dir * reachIn;
            float land = FaultLine.LandLength(impact, l.Dir, len);
            if (land < 0.75f)
            {
                return null;
            }
            _hit.Clear();
            int total = Count(impact, l.Dir, len, land, w, level, _hit, out int line);
            if (!c.Forced && line < need && !(level >= 3 && total >= need + 1))
            {
                return null;
            }
            if (c.Forced && force != null && !_hit.Contains(force))
            {
                return null;
            }
            float mult = FaultLine.MultOf(level);
            float score = 0f;
            Actor first = null;
            float firstAlong = float.MaxValue;
            int down = 0;
            foreach (Actor v in _hit)
            {
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, mult), cm);
                float along = Vector2.Dot(v.current_position - impact, l.Dir);
                if (along < firstAlong)
                {
                    firstAlong = along;
                    first = v;
                }
                if (v.is_unconscious || v == c.LinkTarget && c.Link == Link.Down)
                {
                    down++;
                }
            }
            // the right answer to a formation: more so the longer the line; enemies lying stunned on it can't
            // step off it, and the fissure throws them into the sky for an aerial follow-up
            score += 25f + 8f * Mathf.Max(0, total - need) + 15f * Mathf.Min(2, down);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = first,
                Point = impact + l.Dir * land * 0.5f,
                Dir = l.Dir,
                Count = total,
                Score = score,
                AreaRadius = Mathf.Max(1.5f, land * 0.3f),
                Data = l.Origin
            };
        }

        /// <summary>God power or dev command with no good line: straight at the target, the point, or ahead.</summary>
        private Plan ForcedLine(Ctx c, int level, float len, float w, float reachIn)
        {
            Actor t = c.ForceTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            Vector2 dir;
            if (c.ForcePoint.HasValue && (c.ForcePoint.Value - c.Pos).sqrMagnitude > 0.04f)
            {
                dir = (c.ForcePoint.Value - c.Pos).normalized;
            }
            else if (t != null && (t.current_position - c.Pos).sqrMagnitude > 0.04f)
            {
                dir = (t.current_position - c.Pos).normalized;
            }
            else
            {
                dir = c.Self.flip ? Vector2.right : Vector2.left;
            }
            Line l = new Line { Origin = c.Pos, Dir = dir, Hop = 0f };
            Plan p = Evaluate(c, level, l, len, w, reachIn, 0, null);
            if (p == null)
            {
                return null;   // the line would start on water or against a wall: never break safety
            }
            p.Target = p.Target ?? t;
            p.Score = Mathf.Max(p.Score, 1f);
            return p;
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public override bool Begin(Actor a, Plan p)
        {
            Vector2 origin = p.Data is Vector2 o ? o : a.current_position;
            Vector2 dir = p.Dir.sqrMagnitude > 1e-6f ? p.Dir : p.Point - a.current_position;
            return FaultLine.Begin(a, origin, dir, p.Level);
        }

        public override bool Order(Actor a, WorldTile tile, int level)
        {
            if (a == null || tile == null)
            {
                return false;
            }
            Vector2 dir = new Vector2(tile.posV3.x, tile.posV3.y) - a.current_position;
            return FaultLine.Begin(a, a.current_position, dir, level);
        }
    }
}
