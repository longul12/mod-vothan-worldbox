using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// Shared by the spins (Cyclone Heel, Hurricane Swing): who a spin can still reach or take hold
    /// of. Archers hop back from melee (the game's backstep and dodge) and a knock lifts a unit a
    /// little: a spin that only took units standing flat on the ground missed most of them (in game
    /// the swing grabbed air against every archer, and the vortex caught 2 of 6). A unit in a low
    /// hop is snatched out of the air; flying units, units high in someone else's launch, boats and
    /// everything Combat.CanPuppet forbids stay out.
    /// </summary>
    internal static class SpinGrip
    {
        /// <summary>On the ground or in a low hop (not flying, not a boat).</summary>
        internal static bool Low(Actor v, float maxHeight)
        {
            return v != null && v.asset != null && !v.isFlying() && !v.asset.is_boat && v.position_height <= maxHeight;
        }

        /// <summary>Combat.CanPuppet, except that a unit up to 'maxHeight' off the ground may be snatched mid-hop (Combat.CanCatch).</summary>
        internal static bool Snatchable(Actor v, Actor by, float maxHeight)
        {
            return Combat.CanCatch(v, by, maxHeight);
        }

        /// <summary>Hostile units around a point that are on the ground or in a low hop (a new list).</summary>
        internal static List<Actor> LowFoes(Actor a, Vector2 at, float radius, float maxHeight)
        {
            List<Actor> list = Combat.FoesAround(a, at, radius, true);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!Low(list[i], maxHeight))
                {
                    list.RemoveAt(i);
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Shared by the spins: how hard a spin tears up the world at its impacts (Kit.Ravage: scorched
    /// ground, real craters, fire). The level sets the force (I none, II 1, III 2), the performer's
    /// power raises it (II: 2 from Master up, III: 3 from Grandmaster up; a Combat God one more,
    /// up to 4) and its reach (x1.1 Master, x1.25 Grandmaster, x1.5 God). Where the performer
    /// stands or will land (a pocket of 'keepR' tiles round 'keep') nothing is dug or lit, only
    /// scorched (Kit.Ravage's own pocket), so a master never comes down in its own pit of flames;
    /// near its own people the blast steps down to one without fire. Never on water.
    /// </summary>
    internal static class SpinWreck
    {
        internal static int Force(Actor a, int level)
        {
            Tier t = Tiers.Of(a);
            int f = level <= 1 ? 0 : level == 2 ? (t >= Tier.Master ? 2 : 1) : (t >= Tier.Grandmaster ? 3 : 2);
            if (t == Tier.God)
            {
                f++;
            }
            return Mathf.Clamp(f, 0, 4);
        }

        /// <summary>How far the wreckage reaches for this performer's tier.</summary>
        internal static float Reach(Actor a)
        {
            switch (Tiers.Of(a))
            {
                case Tier.Master:
                    return 1.1f;
                case Tier.Grandmaster:
                    return 1.25f;
                case Tier.God:
                    return 1.5f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// How much bigger a stronger fighter's spin is (its reach, its storm, how far it flings and
        /// throws, the meteor's blast): a Master 1.07x, a Grandmaster 1.15x, a God 1.3x (the same
        /// steps as the reference moves' blast areas). Everything else stays as authored.
        /// </summary>
        internal static float Scale(Tier t)
        {
            switch (t)
            {
                case Tier.Master:
                    return 1.07f;
                case Tier.Grandmaster:
                    return 1.15f;
                case Tier.God:
                    return 1.3f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// How much faster a stronger fighter whirls (the swing, never the recover beats, which
        /// TierInfo.Linger already shortens): about 13% at a Master, 17% at a God.
        /// </summary>
        internal static float Whirl(Actor a)
        {
            return Mathf.Lerp(1f, Tiers.Linger(a), 0.3f);
        }

        /// <summary>
        /// Tears up the ground at 'at' ('radius' tiles: Kit.Ravage blasts the inner part and scorches
        /// the rim). Nothing is dug or set alight within 'keepR' tiles of 'keep' (where the performer
        /// stands or will land); 'fire' false for a blast that never burns (a whirlwind's).
        /// </summary>
        internal static void At(Actor a, Vector2 at, float radius, int force, Vector2 keep, float keepR = 1.2f, bool fire = true)
        {
            if (force <= 0 || a == null || Settings.Destruction <= 0 || World.world == null)
            {
                return;
            }
            Vector2 p = Combat.Clamp(at);
            WorldTile t = Combat.TileAt(p);
            if (t == null || t.Type == null || t.Type.liquid)
            {
                return;
            }
            if (force >= 3 && fire && AlliesNear(a, p, Mathf.Clamp(Mathf.RoundToInt(radius), 1, 16) + 1.5f))
            {
                fire = false;   // its own people stand near: a blast, no flames
            }
            Kit.Ravage(p, radius, force, a, fire, keep, keepR);
        }

        /// <summary>
        /// Sets about 'n' tiles alight on a ring 'r' tiles round 'at': the burning rim of a crater
        /// (Kit.Ravage lights only a share of the rim; this makes sure it reads as a ring of fire).
        /// Only with full environment damage; never within 'keepR' tiles of 'keep', never if the
        /// performer's own people are near, never on water, rock, its own kingdom's buildings or
        /// what already burns.
        /// </summary>
        internal static void Ignite(Actor a, Vector2 at, float r, int n, Vector2 keep, float keepR = 1.2f)
        {
            if (a == null || n <= 0 || Settings.Destruction < 2 || World.world == null)
            {
                return;
            }
            float clear = Mathf.Max(0.5f, keepR);
            if (AlliesNear(a, at, r + 2f))
            {
                return;
            }
            Kingdom own = a.kingdom;
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float ang = a0 + (i + Random.Range(-0.25f, 0.25f)) * Mathf.PI * 2f / n;
                Vector2 p = Combat.Clamp(at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                if ((keep - p).sqrMagnitude <= clear * clear)
                {
                    continue;
                }
                WorldTile t = Combat.TileAt(p);
                if (t == null || t.Type == null || t.Type.liquid || t.Type.block || t.isOnFire())
                {
                    continue;
                }
                if (own != null && t.hasBuilding() && t.building != null && t.building.kingdom == own)
                {
                    continue;
                }
                try
                {
                    t.startFire(true);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("spin fire", e);
                    return;
                }
            }
        }

        /// <summary>Any of the performer's own people within 'r' tiles of a point (no fire there).</summary>
        private static bool AlliesNear(Actor a, Vector2 at, float r)
        {
            try
            {
                WorldTile tile = Combat.TileAt(at);
                if (tile == null || a.kingdom == null)
                {
                    return false;
                }
                float r2 = r * r;
                foreach (Actor u in Finder.getUnitsFromChunk(tile, r > 6f ? 2 : 1))
                {
                    if (u != null && u != a && Combat.IsAlly(a, u) && (u.current_position - at).sqrMagnitude <= r2)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return true;   // unsure: no fire
            }
        }
    }

    /// <summary>
    /// CYCLONE HEEL. The unit drops into a twisting crouch, hops, and whips round on one heel
    /// inside a crescent of wind; everything around it is kicked away. The move a fighter uses to
    /// clear space when it is crowded, or to break out when it is surrounded.
    ///
    /// Beat sheet (game seconds at x1; a big performer plays it 1.2x slower and heavier):
    ///
    ///   I "Cyclone Heel"      radius 1 tile, x0.7, knockback 1 tile                      0.55 s
    ///     CROUCH   0.00-0.10  squash and twist back (Rot -20)
    ///     SPIN     0.10-0.40  a 3-px hop; the sprite turns on its heel: it narrows edge-on, flips,
    ///                         widens (4 flips, one every 0.075 s); one full white crescent
    ///                         ellipse (1 tile, aspect 0.5) sweeps round the hips
    ///     HIT      0.40       everyone in reach is knocked back 1 tile; a 1-tile dust ring
    ///     LAND     0.40-0.55  landing squash, spring back
    ///
    ///   II                    radius 1.4, x1.3, knockback 2 tiles tumbling               0.90 s
    ///     CROUCH   0.00-0.10
    ///     CHAMBER  0.10-0.40  a tight, low first spin: a small pale crescent round the ankles,
    ///                         dust scuffing under the feet (the wind-up)
    ///     SPIN     0.40-0.70  the real kick: a 1.4-tile tier-tinted crescent, tinted afterimages;
    ///                         each enemy is struck as the heel sweeps past it (a ripple of hits
    ///                         round the circle); the first contact holds the pose for a beat
    ///     LAND     0.70-0.90  a tinted ring and a dust ring, a small crack under the heel (the
    ///                         ground really scorched round it; blasted from Master up), a short
    ///                         shake, spring back
    ///
    ///   III "Tempest Spiral"  radius 2, x2.8, everyone flung 3 tiles                     1.50 s
    ///     GATHER   0.00-0.15  a deep crouch and a power tremble; dust spirals in from 2 tiles; a
    ///                         dashed ring marks the eye wall on the ground; a tier glow gathers
    ///     VORTEX   0.15-1.05  rises 2.5 tiles over 3 accelerating turns inside a tornado: three
    ///                         stacked crescents (wider as they rise) every half turn, a helix of
    ///                         dust, embers; enemies within 2 tiles are drawn into a wide orbit
    ///                         (1-1.6 tiles out, so the bodies circle the eye apart) and lifted
    ///                         1-2 tiles as puppets, tumbling (two grazing ticks rippling round
    ///                         the funnel the way it turns: a short white or tier pop on each body
    ///                         as the wall passes it, never all at once, no red damage tint); heavy
    ///                         ones only reel and stagger. The eye wall scorches the ground as it
    ///                         sweeps its first turn
    ///     BURST    1.05       impact frame and a global hit-stop, a 2.5-tile double ring, a dust
    ///                         wall; everyone is flung 3 tiles out, tumbling; the eye wall rips a
    ///                         continuous groove of torn earth round the eye (a Grandmaster's digs
    ///                         it deeper and tears a second groove further out; a God's grooves are
    ///                         bands three tiles wide). Never fire: a whirlwind tears, it does not burn
    ///     DESCENT  1.05-1.30  a twirling fall trailing afterimages
    ///     LAND     1.30-1.50  a small glowing crater (a real one blasted under the heel), dust and
    ///                         embers, spring back, "ting"
    ///
    /// Power scales it: the recover beats (descent, landing) play TierInfo.Linger of their time (an
    /// Expert's 0.85, a Master's 0.55, a God's 0.42), and a stronger fighter's storm is bigger
    /// (SpinWreck.Scale: reach, funnel, burst ring, how far they fly: a Grandmaster 1.15x, a God
    /// 1.3x; its funnel lifts 10 or 12 bodies, and it rises higher). A Master or above goes
    /// straight on: whoever was flung leaves a Scattered link for the next technique, and it never
    /// waits on an ordinary weapon swing afterwards.
    ///
    /// Circumstances change it: the dust takes the ground's colour (sand, snow; grass bits fly on
    /// grass); a big performer (a bear) stomp-spins heavier, slower, with more dust and a shake; a
    /// crab spins with its claws out (a wider swoosh, two claws whirling round); the spin turns
    /// either way; critters fly far, heavy enemies only stagger, tiered victims flash their own tier
    /// colour. The performer never leaves its spot, so it always lands where it stood.
    /// </summary>
    internal sealed class CycloneHeel : Move
    {
        private enum Phase
        {
            Crouch,
            Chamber,
            Spin,
            Gather,
            Vortex,
            Descent,
            Land
        }

        private const float TCrouch = 0.10f;
        private const float TChamber = 0.30f;
        private const float TSpin = 0.30f;
        private const float TLand1 = 0.15f;
        private const float TLand2 = 0.20f;
        private const float TGather = 0.15f;
        private const float TVortex = 0.90f;
        private const float TDescent = 0.25f;
        private const float TLand3 = 0.20f;
        private const int MaxOrbit = 8;
        /// <summary>Orbits and helices are circles on the ground plane, seen flattened.</summary>
        private const float Flat = 0.55f;

        private static readonly Color Pale = new Color(0.86f, 0.9f, 0.96f, 1f);
        private static readonly Color Ting = new Color(1f, 0.98f, 0.85f, 1f);

        private readonly Vector2 _c;
        private readonly float _k, _slow, _radius, _reach, _rise;
        private readonly bool _right0, _big, _crab;
        private readonly float _dir;
        private readonly Palette _pal;
        private readonly WorldTile _tile;
        private readonly Color _dust;
        private readonly bool _grass;

        /// <summary>A Master or above: fights in one continuous flow (crisper landings, no weapon-swing pause after).</summary>
        private readonly bool _flow;
        /// <summary>Recover phases (the descent, the landing) play this much of their time (TierInfo.Linger: Expert 0.85 .. God 0.42).</summary>
        private readonly float _pace;
        /// <summary>How much bigger a stronger fighter's storm is (SpinWreck.Scale: reach, funnel, burst, fling).</summary>
        private readonly float _scale;
        /// <summary>How many bodies the funnel can lift (more for a stronger fighter).</summary>
        private readonly int _maxOrbit;
        /// <summary>How hard it tears up the ground (SpinWreck.Force) and how far (SpinWreck.Reach).</summary>
        private readonly int _wreck;
        private readonly float _wreckW;
        /// <summary>Points of the eye wall the vortex has scorched so far (its first turn).</summary>
        private int _scoured;
        /// <summary>The last enemy it struck that still stands (who the chain it leaves is about).</summary>
        private Actor _struck;
        private long _struckId;
        /// <summary>When the burst struck the whirling bodies (move time): how much of their flash is left when they are let go.</summary>
        private float _burstAt;

        private Phase _phase;
        private float _pt;
        private bool _entered;
        private float _hold;
        private float _spin, _spinFrom, _hFrom;
        private float _ghost, _puff, _helix, _ember, _nextArc;
        private int _ticks;
        /// <summary>When the last graze punch sounded (the vortex clock): one sound per beat, not per body.</summary>
        private float _punchAt = -1f;
        private int _hits;

        /// <summary>II: the spin's one hit-stop has played (on its first contact, a glancing one included).</summary>
        private bool _spinStopped;
        private bool _crit;

        private Particle _shadow, _marker, _glow;
        private readonly List<Claw> _claws = new List<Claw>();
        private readonly HashSet<long> _touched = new HashSet<long>();
        private readonly HashSet<long> _rim = new HashSet<long>();
        private readonly List<Sweep> _sweep = new List<Sweep>();
        private readonly List<Orb> _orbit = new List<Orb>();
        private readonly List<Fling> _pending = new List<Fling>();
        private float _orbitAngle, _vt;

        /// <summary>An enemy the level II heel will strike when it sweeps past (degrees along the sweep).</summary>
        private struct Sweep
        {
            public Actor U;
            public long Id;
            public float At;
            public bool Done;
        }

        /// <summary>An enemy caught in the level III vortex.</summary>
        private sealed class Orb
        {
            public Actor V;
            public long Id;
            public Puppet P;
            public Vector2 From;
            public float A0, R1, H0, H1, Spin, Phase, Cm;
            /// <summary>How far round the funnel it hangs from the front (0..1): when its grazes come.</summary>
            public float Lag;
            public int Grazed;
            public bool Gone;
            public Particle Shadow;
        }

        /// <summary>
        /// A crab's whirling claw and the step it was given. A claw expires on its own with the
        /// spin and its particle goes back to the pool: only one still running that step is ours.
        /// </summary>
        private struct Claw
        {
            public Particle P;
            public System.Action<Particle, float> Step;
        }

        /// <summary>A released puppet, flung as soon as the game has it back (next tick).</summary>
        private struct Fling
        {
            public Actor U;
            public long Id;
            public Vector2 Dir;
            public float Dist, Apex, Spin, H0;
            public int Waits;
        }

        private CycloneHeel(Actor a, int level, Actor face) : base(a)
        {
            Level = Mathf.Clamp(level, 1, 3);
            _c = a.current_position;
            _tile = a.current_tile;
            _k = K;
            string id = a.asset != null ? a.asset.id : "";
            _crab = id.Contains("crab");
            _big = !_crab && BigBody(a);
            _slow = _big ? 1.2f : 1f;
            Tier tier = Tiers.Of(a);
            _scale = SpinWreck.Scale(tier);
            _maxOrbit = tier == Tier.God ? 12 : tier == Tier.Grandmaster ? 10 : MaxOrbit;
            _radius = CycloneHeelTech.Radius(Level) * _k * _scale;
            _reach = _radius + (_crab ? 0.3f : 0.15f);
            _rise = 2.5f * _k * (1f + 0.6f * (_scale - 1f));
            _pal = Palette.For(a, Level);
            _dir = Random.value < 0.5f ? 1f : -1f;
            if (face != null && Mathf.Abs(face.current_position.x - _c.x) > 0.1f)
            {
                _right0 = face.current_position.x > _c.x;
            }
            else
            {
                _right0 = a.flip;
            }
            Pose.FaceRight = _right0;
            Color soil = Fx.SoilColor(_tile);
            _dust = Fx.Lighten(soil, 0.35f);
            _grass = _tile != null && _tile.Type != null && _tile.Type.grass;
            TierInfo ti = Tiers.Info(tier);
            _flow = ti != null && ti.Flow;
            _pace = ti != null ? Mathf.Clamp(ti.Linger, 0.3f, 1f) : 1f;
            _wreck = SpinWreck.Force(a, Level);
            _wreckW = SpinWreck.Reach(a);
            _phase = Level >= 3 ? Phase.Gather : Phase.Crouch;
        }

        public static bool Begin(Actor a, int level, Technique tech, Actor face = null)
        {
            if (!Moves.CanPerform(a))
            {
                return false;
            }
            CycloneHeel m = new CycloneHeel(a, level, face) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            Dev.Watch(m._c + new Vector2(0f, level >= 3 ? 1.6f * m._k : 0.6f));
            return true;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            Flings();
            if (_hold > 0f)
            {
                // a local hit-stop: the heel stays out, trembling with the blow
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            Pose.Nudge = Vector2.zero;
            _pt += dt;
            switch (_phase)
            {
                case Phase.Crouch:
                    Crouch();
                    break;
                case Phase.Chamber:
                    Chamber(dt);
                    break;
                case Phase.Spin:
                    if (Level >= 2)
                    {
                        Spin2(dt);
                    }
                    else
                    {
                        Spin1();
                    }
                    break;
                case Phase.Gather:
                    Gather();
                    break;
                case Phase.Vortex:
                    Vortex(dt);
                    break;
                case Phase.Descent:
                    Descent(dt);
                    break;
                default:
                    Land();
                    break;
            }
            if (Level >= 3)
            {
                UpdateShadow();
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("cyclone " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3") + " h=" + Pose.Height.ToString("F2"));
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

        private bool Visible => Kit.Visible(_c, 0.4f);

        /// <summary>
        /// How big a creature this is by nature: its species' health with its genome (many species set
        /// their health only through the genome, so the asset's base health alone reads as zero).
        /// Critters are up to 80, people up to 180, brutes (bears) above. Cached per species
        /// (Weights.SpeciesHealth): the overview builds a new stat table on every call, and the
        /// planners ask this of every enemy in reach several times a second.
        /// </summary>
        internal static float Bulk(Actor u)
        {
            return u == null || u.asset == null ? 100f : Weights.SpeciesHealth(u.asset);
        }

        /// <summary>A big performer (a bear, a giant): heavier, slower, more dust.</summary>
        private bool BigBody(Actor a)
        {
            float mass = a.asset != null && a.asset.base_stats != null ? a.asset.base_stats["mass_2"] : 0f;
            return Bulk(a) > 180f || mass >= 140f || _k >= 1.3f || Pose.PivotUp >= 5.2f;
        }

        /// <summary>Hip height above the unit's feet: where the heel travels.</summary>
        private float Hip => Pose.PivotUp * A.actor_scale * 0.7f;

        /// <summary>How high a hopping enemy (an archer's backstep, a dodge) the heel still kicks.</summary>
        private float HopKick => 0.8f * _k;

        /// <summary>How high a hopping enemy the vortex still sucks in.</summary>
        private float HopCatch => 1.5f * _k;

        /// <summary>
        /// A flat sprite turning on its heel: it narrows to a sliver edge-on, flips, widens again.
        /// Returns how side-on it is (1 = full width, the leg out).
        /// </summary>
        private float Turn(float deg)
        {
            float c = Mathf.Cos(deg * Mathf.Deg2Rad);
            Pose.FaceRight = c >= 0f ? _right0 : !_right0;
            return Mathf.Abs(c);
        }

        /// <summary>Where the heel starts its sweep: on the side the unit faces.</summary>
        private float FrontDeg => _right0 ? 0f : 180f;

        // ------------------------------------------------------------- crouch --

        private void Crouch()
        {
            float t = TCrouch * _slow;
            if (Enter() && Visible)
            {
                Scuff(Px, 2);
            }
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / t));
            float sq = _big ? 1.45f : 1f;
            Pose.Ground = _c;
            Pose.Height = 0f;
            Pose.FaceRight = _right0;
            Pose.SX = 1f + 0.12f * sq * k;
            Pose.SY = 1f - 0.16f * sq * k;
            Pose.Rot = -20f * k;
            if (_pt >= t)
            {
                Next(Level >= 2 ? Phase.Chamber : Phase.Spin);
            }
        }

        // ------------------------------------------------------- level I spin --

        private void Spin1()
        {
            float px = Px;
            float t = TSpin * _slow;
            if (Enter())
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    // a bolder white swoosh (in game the 3-px one melted into the grass), its tail
                    // still fading as the heel lands
                    Crescent(_radius * (_crab ? 1.25f : 1f), 0.5f, 4, t + 0.1f, Hip, FrontDeg, Color.white, Pale);
                    Claws(_radius * 1.2f, t, Pale);
                }
            }
            float u = Mathf.Clamp01(_pt / t);
            _spin = 720f * u;
            float side = Turn(_spin);
            Pose.Ground = _c;
            Pose.Height = (_big ? 2f : 3f) * px * Mathf.Sin(Mathf.PI * u);
            Pose.SX = Mathf.Lerp(0.5f, _big ? 1.08f : 1.12f, side);
            Pose.SY = 1f;
            Pose.Rot = Mathf.Lerp(-20f, -8f, Anim.OutQuad(Mathf.Clamp01(u * 3f)));
            if (u >= 1f)
            {
                Strike1();
                Next(Phase.Land);
            }
        }

        /// <summary>Level I: one blow on everyone in reach at once, a shove back.</summary>
        private void Strike1()
        {
            float px = Px;
            foreach (Actor u in SpinGrip.LowFoes(A, _c, _reach, HopKick))
            {
                Strike(u, 0.7f, 1f, 0.35f, 0f, 0);
            }
            if (_hits > 0)
            {
                Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            }
            if (!Visible)
            {
                return;
            }
            // white and grey only: one crisp ring and the ground's own dust
            Fx.Ring(_c, px, 0.3f, 3f, _radius / px, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.6f,
                new Color(1f, 1f, 1f, 0.85f), new Color(0.9f, 0.92f, 0.96f, 0f));
            DustRing(_radius, _big ? 14 : 9, 0.5f);
        }

        // ------------------------------------------------- level II chamber --

        private void Chamber(float dt)
        {
            float px = Px;
            float t = TChamber * _slow;
            if (Enter())
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    // the wind-up: a small, low, pale crescent round the ankles
                    Crescent(0.6f * _k * (_crab ? 1.25f : 1f), 0.45f, 2, t, Hip * 0.35f, FrontDeg, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.7f));
                }
            }
            float u = Mathf.Clamp01(_pt / t);
            _spin = 360f * u;
            float side = Turn(_spin);
            Pose.Ground = _c;
            Pose.Height = (_big ? 1.5f : 2f) * px * Mathf.Sin(Mathf.PI * u);
            Pose.SX = Mathf.Lerp(0.5f, 1.05f, side);
            Pose.SY = Mathf.Lerp(0.9f, 0.96f, u);
            Pose.Rot = Mathf.Lerp(-20f, -12f, u);
            if (Visible)
            {
                _puff -= dt;
                if (_puff <= 0f)
                {
                    _puff = 0.1f;
                    Scuff(px, 1);
                }
            }
            if (u >= 1f)
            {
                Next(Phase.Spin);
            }
        }

        // ---------------------------------------------------- level II spin --

        private void Spin2(float dt)
        {
            float px = Px;
            float t = TSpin * _slow;
            float life = t + 0.04f;
            if (Enter())
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    Crescent(_radius * (_crab ? 1.25f : 1f), 0.5f, 3, life, Hip, FrontDeg, _pal.Core, _pal.Edge);
                    Claws(_radius * 1.2f, t, _pal.Edge);
                }
                // who the heel will meet, and where along its sweep
                _sweep.Clear();
                foreach (Actor u in SpinGrip.LowFoes(A, _c, _reach, HopKick))
                {
                    Vector2 rel = u.current_position - _c;
                    float ang = rel.sqrMagnitude > 1e-4f ? Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg : FrontDeg;
                    _sweep.Add(new Sweep { U = u, Id = u.Sid(), At = Mathf.Repeat((ang - FrontDeg) * _dir, 360f) });
                }
            }
            float u01 = Mathf.Clamp01(_pt / t);
            _spin = 360f + 360f * u01;
            float side = Turn(_spin);
            Pose.Ground = _c;
            Pose.Height = (_big ? 3f : 4f) * px * Mathf.Sin(Mathf.PI * u01);
            Pose.SX = Mathf.Lerp(0.5f, _big ? 1.12f : 1.18f, side);
            Pose.SY = 0.97f;
            Pose.Rot = -10f;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.035f;
                    Fx.Afterimage(this, 0.13f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            // the head of the crescent races round (Art.Arc): strike each enemy as it passes
            float head = SweepHead(_pt, life);
            for (int i = 0; i < _sweep.Count; i++)
            {
                Sweep s = _sweep[i];
                if (s.Done || (s.At > head && u01 < 1f))
                {
                    continue;
                }
                s.Done = true;
                _sweep[i] = s;
                // (one in a light move, a flow strike, is still there to kick: the launch cuts it short;
                // one picked up by the god hand, gone into a boat or a building, made peace or thrown
                // high since the spin began is not)
                if (s.U == null || s.U.Sid() != s.Id || !Combat.IsFoe(A, s.U) || Moves.Locked(s.U) || !SpinGrip.Low(s.U, HopKick))
                {
                    continue;
                }
                float far = _reach + 0.6f + s.U.stats["size"] * 0.5f;
                if ((s.U.current_position - _c).sqrMagnitude > far * far)
                {
                    continue;   // it got away in time
                }
                // (once per spin: a glancing blow on a heavy foe counts no hit, and each one before the
                // first real hit used to stop the spin again)
                if (Strike(s.U, 1.3f, 2f, 0.9f, Random.value < 0.5f ? -720f : 720f, 1) && !_spinStopped)
                {
                    _spinStopped = true;
                    _hold = 0.05f;
                    Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                }
            }
            if (u01 >= 1f && _hold <= 0f)
            {
                Finale2(px);
                Next(Phase.Land);
            }
        }

        private void Finale2(float px)
        {
            // the heel's circle really scorches the ground (blasts it, from Master up); on the
            // landing beat, under its own feet, so never fire
            SpinWreck.At(A, _c, 1.8f * _k * _wreckW, Mathf.Min(_wreck, 2), _c, 0f, false);
            if (!Visible)
            {
                return;
            }
            Fx.Ring(_c, px, 0.36f, 4f, _radius / px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.6f,
                Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
            DustRing(_radius, _big ? 18 : 12, 0.6f);
            // a small crack under the heel (no glow below level III)
            Kit.Crater(_c, px * 0.8f * _k, _tile, false, 3f, true);
            Kit.Shake(_c, _big ? 0.22f : 0.15f, _big ? 0.009f : 0.006f, _k);
        }

        // --------------------------------------------- level III gather --

        private void Gather()
        {
            float px = Px;
            float t = TGather * _slow;
            if (Enter())
            {
                Pose.HideShadow = true;
                SpawnShadow(px);
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    SpiralIn(px);
                    Marker(px);
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
                }
            }
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / (t * 0.7f)));
            float sq = _big ? 1.4f : 1f;
            Pose.Ground = _c;
            Pose.Height = 0f;
            Pose.FaceRight = _right0;
            Pose.SX = 1f + 0.14f * sq * k;
            Pose.SY = 1f - 0.2f * sq * k;
            Pose.Rot = -22f * k;
            Pose.Nudge = _pt > t * 0.5f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            if (_pt >= t)
            {
                Pose.Nudge = Vector2.zero;
                Next(Phase.Vortex);
            }
        }

        /// <summary>Dust drawn in from the edge of the reach, spiralling to the feet.</summary>
        private void SpiralIn(float px)
        {
            int n = Fx.Count(12);
            float r0 = _radius, life = 0.32f * _slow, dir = _dir;
            Vector2 c = _c;
            Color dust = _dust;
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector2 start = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0) * Flat) * r0;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(1, 4)], start, px, life, Mathf.Sin(a0) < 0f ? FxLayer.Top : FxLayer.Behind, 5);
                if (p == null)
                {
                    break;
                }
                p.Sized(1.1f, 0.5f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
                p.Step = (q, d) =>
                {
                    float k = q.K;
                    float a = a0 + dir * 2.2f * k;
                    float r = r0 * (1f - 0.85f * k * k);
                    q.Pos = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Flat) * r;
                };
            }
        }

        /// <summary>The telegraph: a dashed ring on the eye wall, brightening until the burst.</summary>
        private void Marker(float px)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(_radius / px), 4, 130);
            _marker = Fx.Spawn(Art.Ring(r, 1, Art.Ground, 14), _c, px, 3f, FxLayer.Ground, 3);
            if (_marker == null)
            {
                return;
            }
            _marker.Held = true;
            _marker.Snap = true;
            Color edge = _pal.Edge;
            float total = (TGather + TVortex) * _slow;
            _marker.Step = (q, d) =>
            {
                float k = Mathf.Clamp01(q.Age / total);
                q.Grad = null;
                q.R.color = Fx.WithA(edge, Mathf.Lerp(0f, 0.75f, Mathf.Clamp01(k * 2.5f)) * (0.8f + 0.2f * Mathf.Sin(q.Age * 30f)));
            };
        }

        // --------------------------------------------- level III vortex --

        private void Vortex(float dt)
        {
            float px = Px;
            float t = TVortex * _slow;
            if (Enter())
            {
                _vt = 0f;
                _spin = 0f;
                _nextArc = 0f;
                _ticks = 0;
                Catch();
                SetLags();
                if (Visible)
                {
                    Kit.DustRing(_c, px, _tile, _big ? 12 : 8, 4.5f * 0.8f * _k * _scale / px, 0.5f);
                }
            }
            _vt += dt;
            float u = Mathf.Clamp01(_pt / t);
            _spin = 1080f * Mathf.Pow(u, 1.25f);
            _orbitAngle = _dir * Mathf.PI * 2f * 1.5f * Mathf.Pow(u, 1.3f);
            float side = Turn(_spin);
            Pose.Ground = _c;
            Pose.Height = _rise * Anim.Smooth(0f, 1f, u);
            Pose.SX = Mathf.Lerp(0.5f, 1.02f, side);
            Pose.SY = 1.06f;
            Pose.Rot = -6f + 4f * Mathf.Sin(_pt * 11f);

            if (Visible)
            {
                VortexFx(px, dt, u);
            }
            Orbit(dt);
            Scour();
            // two grazing ticks while they whirl, rippling round the funnel
            Graze(u);
            if (u >= 1f)
            {
                Burst();
                Next(Phase.Descent);
            }
        }

        private void VortexFx(float px, float dt, float u)
        {
            // three stacked crescents every half turn: a funnel widening as it rises
            if (_spin >= _nextArc)
            {
                _nextArc += 180f;
                float life = 0.26f * _slow;
                // (a stronger fighter's funnel is as much wider as its reach)
                float wide = (_crab ? 1.15f : 1f) * _scale;
                // each layer starts a quarter turn after the one below it: the funnel twists
                // (starts snap to quarter turns so only a handful of arcs are ever drawn and cached)
                float start = Mathf.Repeat(Mathf.Round((FrontDeg + _spin * _dir) / 90f) * 90f, 360f);
                Crescent(0.85f * _k * wide, 0.42f, 2, life, 0.08f * _k, start, Fx.WithA(Fx.Lighten(_dust, 0.3f), 0.9f), Fx.WithA(_dust, 0.75f), true);
                CrescentMid(1.3f * _k * wide, 0.45f, 2, life, Mathf.Repeat(start + 90f * _dir, 360f));
                Crescent(1.8f * _k * wide, 0.48f, 3, life, Hip, Mathf.Repeat(start + 180f, 360f), _pal.Core, _pal.Edge);
            }
            // a helix of dust climbing the funnel
            _helix -= dt;
            if (_helix <= 0f)
            {
                _helix = 0.03f / Fx.Detail();
                float a = _spin * Mathf.Deg2Rad * 0.5f * _dir;
                float hr = 1.45f * _k * _scale;
                Helix(px, a, hr, Mathf.Max(0.8f, Pose.Height + 0.6f * _k));
                Helix(px, a + Mathf.PI, hr, Mathf.Max(0.8f, Pose.Height + 0.6f * _k));
            }
            _ember -= dt;
            if (_ember <= 0f && u > 0.2f)
            {
                _ember = 0.05f;
                Vector2 at = _c + new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.1f, 0.4f) + Pose.Height * Random.value) * _k;
                Kit.Ember(at, px, Random.Range(0.4f, 0.7f), new Vector2(Random.Range(-6f, 6f), Random.Range(12f, 26f)) * px, _pal.Edge);
            }
            _ghost -= dt;
            if (_ghost <= 0f)
            {
                _ghost = 0.04f;
                Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
            }
        }

        /// <summary>Points round the eye wall the ground is torn at (scorched in the vortex, blasted at the burst).</summary>
        private const int WallPoints = 8;

        /// <summary>Where the eye wall bites the ground: just inside the marker ring, wider for a stronger tier.</summary>
        private float WallR => 1.5f * _k * _wreckW;

        /// <summary>A point of a ground-plane ring round the eye (the same flattening as the marker ring).</summary>
        private Vector2 WallAt(float radius, float angle)
        {
            return _c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * Art.Ground) * radius;
        }

        /// <summary>
        /// The eye wall scorches the ground as it sweeps its first turn: one point every eighth of a
        /// turn, as the orbit passes it (a few calls spread over half a second, never every frame).
        /// </summary>
        private void Scour()
        {
            if (_wreck <= 0 || _scoured >= WallPoints)
            {
                return;
            }
            float swept = Mathf.Abs(_orbitAngle);
            float step = Mathf.PI * 2f / WallPoints;
            float a0 = FrontDeg * Mathf.Deg2Rad;
            while (_scoured < WallPoints && swept >= (_scoured + 0.5f) * step)
            {
                SpinWreck.At(A, WallAt(WallR, a0 + _dir * _scoured * step), 1f, 1, _c, 0f, false);
                _scoured++;
            }
        }

        /// <summary>
        /// The burst rips the eye wall into a groove of torn earth all the way round (on the impact
        /// frame); a Grandmaster's digs it a step deeper, a God's tears a band three tiles wide; a
        /// Grandmaster's or a God's tears a second groove out where the dust wall goes. Never fire:
        /// a whirlwind tears the ground, it does not burn it.
        /// </summary>
        private void RipWall()
        {
            int force = Mathf.Min(_wreck, 3);
            if (force <= 0)
            {
                return;
            }
            Groove(WallR, force, _wreck >= 4 ? 1f : 0.5f, FrontDeg * Mathf.Deg2Rad);
            if (_wreck >= 3)
            {
                Groove(2.7f * _k * _wreckW, 2, _wreck >= 4 ? 1f : 0.5f, Random.value * Mathf.PI);
            }
        }

        /// <summary>
        /// A continuous ring of torn ground round the eye: blasts under a tile apart along the
        /// ground ellipse ('blast' 0.5: one tile each, a clean groove; 1: a band). In game eight
        /// separate one-tile blasts round the wall read as a dotted checkerboard of white flashes.
        /// </summary>
        private void Groove(float radius, int force, float blast, float a0)
        {
            float ry = radius * Art.Ground;
            float len = 2f * Mathf.PI * Mathf.Sqrt((radius * radius + ry * ry) * 0.5f);
            int n = Mathf.Clamp(Mathf.CeilToInt(len / 0.75f), 8, 48);
            for (int i = 0; i < n; i++)
            {
                SpinWreck.At(A, WallAt(radius, a0 + _dir * i * Mathf.PI * 2f / n), blast, force, _c, 0f, false);
            }
        }

        /// <summary>The middle crescent rides halfway up the funnel, whatever the height.</summary>
        private void CrescentMid(float radiusTiles, float ratio, int thick, float life, float startDeg)
        {
            Crescent(radiusTiles, ratio, thick, life, 0f, startDeg, Color.Lerp(_pal.Core, _pal.Edge, 0.3f), Fx.WithA(_pal.Edge, 0.85f), false, true);
        }

        /// <summary>A puff of dust riding up the tornado on a helix.</summary>
        private void Helix(float px, float angle, float radius, float rise)
        {
            float life = Random.Range(0.42f, 0.55f);
            Vector2 c = _c;
            float dir = _dir;
            Vector2 at = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * Flat) * radius * 0.75f;
            Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 4)], at, px, life, Mathf.Sin(angle) < 0f ? FxLayer.Top : FxLayer.Behind, 4);
            if (p == null)
            {
                return;
            }
            Color dust = _dust;
            p.Sized(0.7f, 1.15f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.7f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
            p.Step = (q, d) =>
            {
                float k = q.K;
                float a = angle + dir * 7f * q.Age;
                float r = radius * (0.75f + 0.45f * k);
                q.Pos = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Flat) * r;
                q.Z = rise * k;
            };
        }

        /// <summary>The vortex takes hold: who is pulled in, who only reels.</summary>
        private void Catch()
        {
            List<Actor> near = SpinGrip.LowFoes(A, _c, _reach, HopCatch);
            Vector2 c = _c;
            near.Sort((x, y) => (x.current_position - c).sqrMagnitude.CompareTo((y.current_position - c).sqrMagnitude));
            foreach (Actor u in near)
            {
                long id = u.Sid();
                if (!_touched.Add(id))
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    Glance(u);
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                if (_orbit.Count < _maxOrbit && SpinGrip.Snatchable(u, A, HopCatch))
                {
                    // one caught mid-hop is taken from where it is in the air, not snapped to the ground
                    Puppet p = new Puppet(u, u.flip, this);
                    p.Pose.Height = Mathf.Max(0f, u.position_height);
                    if (Moves.Start(p))
                    {
                        _orbit.Add(NewOrb(u, id, p, cm));
                        continue;
                    }
                }
                // too big to lift (or the funnel is full): it reels in the wind until the burst
                _rim.Add(id);
                Combat.Stagger(u, 0.6f);
                u.startShake(0.5f, 0.1f, true, false);
            }
            if (Dev.On)
            {
                Dev.Trace("cyclone " + Id + " caught " + _orbit.Count + " of " + near.Count);
            }
        }

        private Orb NewOrb(Actor u, long id, Puppet p, float cm)
        {
            Vector2 rel = u.current_position - _c;
            if (rel.sqrMagnitude < 1e-4f)
            {
                rel = Random.insideUnitCircle.normalized * 0.3f;
            }
            // drawn in only a little: the bodies must circle the eye wide apart, around the
            // spinning unit (pulled half a tile in, they clumped into one heap round it in game)
            float r1 = Mathf.Clamp(rel.magnitude * 0.85f, 1f * _k, _radius * 0.8f);
            float lift = Mathf.InverseLerp(1f * _k, _radius * 0.8f, r1);
            bool critter = Bulk(u) <= 80f;
            Orb o = new Orb
            {
                V = u,
                Id = id,
                P = p,
                From = u.current_position,
                A0 = Mathf.Atan2(rel.y / Flat, rel.x),
                R1 = r1,
                H0 = Mathf.Max(0f, u.position_height),
                H1 = ((critter ? 1.3f : 1f) + lift + Random.Range(-0.12f, 0.12f)) * _k,
                Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(420f, 820f),
                Phase = Random.Range(0f, 6.3f),
                Cm = cm
            };
            p.Pose.HideShadow = true;
            if (Kit.Visible(o.From))
            {
                int rx = Mathf.Max(1, Mathf.RoundToInt(3.2f * (p.Pose.PivotUp / 3.5f)));
                o.Shadow = Fx.Spawn(Art.Disk(rx, 0.42f, 0.45f), o.From, p.Px, 1f, FxLayer.Ground, 3);
                if (o.Shadow != null)
                {
                    o.Shadow.Held = true;
                    o.Shadow.Snap = true;
                    o.Shadow.Step = (q, d) =>
                    {
                        q.Pos = p.Pose.Ground;
                        q.Grad = null;
                        q.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.32f, 0.1f, Mathf.Clamp01(p.Pose.Height / (2.5f * _k))));
                    };
                }
            }
            if (Kit.Visible(o.From))
            {
                Fx.LandingPuff(o.From, p.Px, u.current_tile);
            }
            return o;
        }

        /// <summary>The caught enemies whirl round the eye, tumbling, sucked in and up.</summary>
        private void Orbit(float dt)
        {
            for (int i = 0; i < _orbit.Count; i++)
            {
                Orb o = _orbit[i];
                if (o.Gone)
                {
                    continue;
                }
                if (Lost(o))
                {
                    continue;
                }
                float pull = Anim.OutCubic(Mathf.Clamp01(_vt / 0.3f));
                float lift = Anim.OutQuad(Mathf.Clamp01(_vt / 0.42f));
                float a = o.A0 + _orbitAngle;
                Vector2 ring = _c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Flat) * o.R1;
                Pose vp = o.P.Pose;
                vp.Ground = Combat.Clamp(Vector2.Lerp(o.From, ring, pull));
                vp.Height = Mathf.Lerp(o.H0, o.H1, lift) + 0.08f * _k * Mathf.Sin(_vt * 9f + o.Phase) * lift;
                float tx = -Mathf.Sin(a) * _dir;
                if (Mathf.Abs(tx) > 0.25f)
                {
                    vp.FaceRight = tx > 0f;
                }
                vp.Rot += o.Spin * dt * (0.3f + 0.7f * lift);
                vp.SX = vp.SY = 1f;
                vp.HideShadow = true;
                o.P.Pose = vp;
            }
        }

        /// <summary>Is this orbiting enemy still ours? (Killed by someone else, picked up by the god hand...)</summary>
        private bool Lost(Orb o)
        {
            if (!o.P.Finished && o.V.isAlive() && o.V.Sid() == o.Id)
            {
                return false;
            }
            o.Gone = true;
            if (!o.P.Finished)
            {
                o.P.Release(false);
            }
            o.Shadow?.Kill();
            o.Shadow = null;
            return true;
        }

        /// <summary>When the two grazes come (share of the vortex), before each body's own lag round the funnel.</summary>
        private const float Graze1 = 0.33f, Graze2 = 0.64f, GrazeSpread = 0.18f;

        /// <summary>
        /// Two grazing blows on every whirling body, rippling round the funnel the way it turns
        /// (each body as the wall sweeps past it, a short white or tier pop and sparks). In game
        /// every body flashed solid white on the same frame, twice: six white lumps in the funnel.
        /// </summary>
        private void Graze(float u)
        {
            float px = Px;
            bool any = false;
            foreach (Orb o in _orbit)
            {
                if (o.Gone || o.Grazed >= 2 || u < (o.Grazed == 0 ? Graze1 : Graze2) + o.Lag * GrazeSpread)
                {
                    continue;
                }
                o.Grazed++;
                if (Lost(o))
                {
                    continue;
                }
                // no game damage flash: its red tint on whirling bodies smeared the funnel red
                Combat.Hit(A, o.V, 0.2f * o.Cm, false, false, 0f, this);
                o.P.Flash(0.035f, TierTint(o.V));
                any = true;
                if (Kit.Visible(o.P.BodyPos()))
                {
                    Kit.Sparks(o.P.BodyPos(), o.P.Px, 3, 40f, 80f, _pal.Edge);
                }
            }
            if (any && _vt - _punchAt > 0.07f)
            {
                _punchAt = _vt;
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            }
            // the eye itself crackles as each wave starts
            if (_ticks == 0 && u >= Graze1 || _ticks == 1 && u >= Graze2)
            {
                _ticks++;
                if (Visible)
                {
                    Kit.Sparks(BodyPos(), px, 4, 40f, 90f, _pal.Edge);
                }
            }
        }

        /// <summary>Spreads the grazes round the funnel: each body's lag is how far round from the front it hangs, the way the funnel turns.</summary>
        private void SetLags()
        {
            float front = FrontDeg * Mathf.Deg2Rad;
            foreach (Orb o in _orbit)
            {
                o.Lag = Mathf.Repeat((o.A0 - front) * _dir, Mathf.PI * 2f) / (Mathf.PI * 2f);
            }
        }

        // ---------------------------------------------- level III burst --

        private void Burst()
        {
            float px = Px;
            Vector2 body = BodyPos();
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
            Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);

            // the orbit's last blow, one each: never a kill on the spot (one it would have killed is
            // flung all the same and dies where it lands); the bodies hang flashing in the burst's
            // own hit-stop and fly on the next beat (Descent). Let go on this frame, they stood
            // upright and unlit in mid-air through the impact frame, and the ones it killed only
            // dropped out of the funnel
            _burstAt = T;
            foreach (Orb o in _orbit)
            {
                if (o.Gone || Lost(o))
                {
                    continue;
                }
                HitResult r = Combat.Hit(A, o.V, 2.4f * o.Cm, false, true, 0f, this);
                _crit |= r.Crit;
                _hits++;
                if (MeteorSlam.AtDeathsDoor(o.V))
                {
                    MeteorSlam.Doom(A, o.V, this);
                }
                Struck(o.V);
                o.P.Flash(BurstFlash, TierTint(o.V));
            }
            // everyone else the blast reaches
            foreach (Actor u in SpinGrip.LowFoes(A, _c, 2.5f * _k * _scale + 0.15f, HopCatch))
            {
                long id = u.Sid();
                if (_rim.Contains(id))
                {
                    // allowed, but too big to lift: it took the whole storm standing
                    HitBlast(u, 2.8f);
                    continue;
                }
                if (_touched.Contains(id))
                {
                    // a heavy one the storm already rocked reels again; one that answered the
                    // technique (a parry, a sidestep) is left alone
                    if (!Combat.Allowed(A, u))
                    {
                        Combat.Stagger(u, 0.3f);
                    }
                    continue;
                }
                Strike(u, 2.8f, 3f, 1.2f, Random.value < 0.5f ? -800f : 800f, 2);
            }
            // the ground tears on the same frame as the blast
            RipWall();

            if (Visible)
            {
                Kit.ImpactFrame(body, px, 0.75f * _k, _pal, _crit);
                Fx.Ring(_c, px, 0.45f, 6f, 2.5f * _k * _scale / px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.8f,
                    Color.white, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0f));
                Particle ring2 = Fx.Ring(_c, px, 0.4f, 4f, 1.7f * _k * _scale / px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                    Fx.WithA(_pal.Edge, 0.85f), Fx.WithA(_pal.Edge, 0f));
                if (ring2 != null)
                {
                    ring2.Delay = 0.07f;
                }
                DustWall(px);
                Kit.Sparks(body, px, 14, 80f, 160f, _pal.Edge);
                // (a stronger fighter's burst lands heavier: a God's shakes half again as hard)
                Kit.Shake(_c, 0.28f, 0.01f * (1f + 1.5f * (_scale - 1f)), _k);
            }
            Kit.Stop(body, 0.08f + 0.1f * (_scale - 1f));   // 0.08 s, a God's 0.11
            _marker?.Kill();
            _marker = null;
            // anyone flung is something to chase: the next technique picks it up (even a lone
            // enemy thrown 3 tiles is a gap to close, not a pause)
            LeaveLink();
            if (Dev.On)
            {
                Dev.Trace("cyclone " + Id + " burst: " + _hits + " hit" + (_crit ? " (crit)" : ""));
            }
        }

        /// <summary>The storm's dust thrown outward along the ground, the colour of the ground.</summary>
        private void DustWall(float px)
        {
            int n = Fx.Count(_big ? 26 : 20);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.35f, 0.35f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 6)], _c + dir * 0.4f * _k, px, Random.Range(0.7f, 0.95f),
                    dir.y < -0.05f ? FxLayer.Top : FxLayer.Ground, 7);
                p?.Moving(dir * Random.Range(5.5f, 7f) * _k * _scale, Random.Range(0.3f, 0.8f) * _k, 2.4f)
                    .Sized(0.5f, 1.3f)
                    .Color(Fx.WithA(_dust, 0.85f), Fx.WithA(_dust, 0.55f), Fx.WithA(_dust, 0.2f), Fx.WithA(_dust, 0f));
            }
            Grass(px, _radius * 1.2f, 8);
        }

        /// <summary>How long the burst's flash on each whirling body lasts.</summary>
        private const float BurstFlash = 0.09f;

        /// <summary>
        /// The burst is over (its hit-stop too): the bodies it struck are let go, and fly out the
        /// tick after (once the game has them back). What is left of each one's flash goes on with
        /// it (a puppet's flash ends with the puppet).
        /// </summary>
        private void Scatter()
        {
            float flash = BurstFlash - (T - _burstAt);
            foreach (Orb o in _orbit)
            {
                if (o.Gone || Lost(o))
                {
                    continue;
                }
                Vector2 outDir = o.P.Pose.Ground - _c;
                if (outDir.sqrMagnitude < 1e-4f)
                {
                    outDir = new Vector2(Mathf.Cos(o.A0), Mathf.Sin(o.A0));
                }
                outDir.Normalize();
                float h0 = o.P.Pose.Height;
                o.P.Release(false);
                o.Gone = true;
                o.Shadow?.Kill();
                o.Shadow = null;
                if (flash > 0.01f)
                {
                    Color c = TierTint(o.V);
                    BodyFlash(o.V, flash, Fx.WithA(c, Mathf.Clamp01(flash / 0.05f)), Fx.WithA(c, 0f));
                }
                bool critter = Bulk(o.V) <= 80f;
                _pending.Add(new Fling
                {
                    U = o.V,
                    Id = o.Id,
                    Dir = outDir,
                    // from the wider orbit: they still come down about 4 tiles from the eye
                    // (further from a stronger fighter's storm)
                    Dist = 2.6f * _k * _scale * (critter ? 1.4f : 1f),
                    Apex = 1.1f * _k,
                    Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(700f, 1000f),
                    H0 = h0
                });
            }
        }

        // ---------------------------------------------- level III descent --

        /// <summary>The fall out of the storm: a stronger tier drops harder and faster (still a readable twirl; Linger 0.42 plays it in 0.62).</summary>
        private float DescentT => TDescent * _slow * (0.35f + 0.65f * _pace);

        private void Descent(float dt)
        {
            float t = DescentT;
            if (Enter())
            {
                _hFrom = Pose.Height;
                _spinFrom = _spin;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                Scatter();
            }
            float u = Mathf.Clamp01(_pt / t);
            _spin = _spinFrom + 360f * Anim.OutQuad(u);
            float side = Turn(_spin);
            Pose.Ground = _c;
            Pose.Height = _hFrom * (1f - u * u);
            Pose.SX = Mathf.Lerp(0.55f, 0.95f, side);
            Pose.SY = Mathf.Lerp(1.05f, 1.15f, u);
            Pose.Rot = 0f;
            if (Visible)
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.035f;
                    Fx.Afterimage(this, 0.12f, _pal.Ghost, _pal.GhostEnd);
                }
            }
            if (u >= 1f)
            {
                Next(Phase.Land);
            }
        }

        // ------------------------------------------------------------- land --

        private void Land()
        {
            float px = Px;
            // a stronger tier lands crisply and goes: the same squash and spring, played faster
            // (TierInfo.Linger: an Expert's in 0.85 of the time, a God's in 0.42)
            float t = (Level >= 3 ? TLand3 : Level == 2 ? TLand2 : TLand1) * _slow * _pace;
            if (Enter())
            {
                Pose.Height = 0f;
                if (Level >= 3)
                {
                    Pose.HideShadow = false;
                    KillShadow();
                    // the landing crater, blasted for real under the heel (a God's bites a step
                    // deeper; never fire under its own feet)
                    SpinWreck.At(A, _c, 1.8f * _k * _wreckW, Mathf.Min(_wreck, _wreck >= 4 ? 3 : 2), _c, 0f, false);
                    if (Visible)
                    {
                        // if it leaves a glowing crater, it's a III
                        Kit.Crater(_c, px * 0.8f * _k, _tile, true, 4f, true, _pal.Edge);
                        DustRing(1.1f * _k, _big ? 14 : 10, 0.55f);
                        for (int i = 0; i < Fx.Count(6); i++)
                        {
                            Kit.Ember(_c + Random.insideUnitCircle * 0.3f * _k, px, Random.Range(0.6f, 1.1f),
                                new Vector2(Random.Range(-8f, 8f), Random.Range(14f, 30f)) * px, _pal.Edge);
                        }
                        Fx.Glint(HeadPos() + new Vector2((_right0 ? 3f : -3f) * px, 2f * px), px, 0.24f, Ting, 1f);
                    }
                    Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile);
                    Kit.Shake(_c, 0.14f, 0.005f, _k);
                }
                else if (_big)
                {
                    // a bear lands its spin like a stomp (at level I the stomp is all that marks the
                    // ground; level II already tore it at the ring)
                    if (Level <= 1)
                    {
                        SpinWreck.At(A, _c, 1f, Mathf.Max(_wreck, 1), _c, 0f, false);
                    }
                    if (Visible)
                    {
                        Fx.LandingPuff(_c, px, _tile);
                        if (Level >= 2)
                        {
                            Kit.Shake(_c, 0.12f, 0.005f, _k);
                        }
                    }
                }
                else if (Level <= 1)
                {
                    // (a Combat God's level I still scorches the ground; anyone else's is clean)
                    SpinWreck.At(A, _c, 1f, _wreck, _c, 0f, false);
                }
            }
            float amp = Anim.Spring(_pt / _pace, 9f, 18f);
            float sq = (_big ? 1.45f : 1f) * (Level >= 3 ? 1.3f : 1f);
            Pose.Ground = _c;
            Pose.Height = 0f;
            Pose.FaceRight = _right0;
            Pose.SX = 1f + 0.16f * sq * amp;
            Pose.SY = 1f - 0.2f * sq * amp;
            Pose.Rot = 0f;
            if (_pt >= t)
            {
                Pose.SX = Pose.SY = 1f;
                KillGlow();
                if (!_flow)
                {
                    // (a Master and up never stands and swings like a peasant between techniques:
                    // its strike chains take over at once, so no weapon cooldown to wait out)
                    A.startAttackCooldown();
                }
                if (Level <= 2)
                {
                    LeaveLink();
                }
                Finished = true;
            }
        }

        /// <summary>
        /// The chain this spin leaves: anyone it flung is a crowd blown apart (Scattered: a dash, a
        /// wave, a leap picks it up), aimed at the last one it struck that still stands.
        /// </summary>
        private void LeaveLink()
        {
            EndLink = _hits >= 1 ? Link.Scattered : Link.None;
            // (Combat.Living: a unit a blow just killed is still isAlive() until the game's death check)
            EndTarget = _struck != null && _struck.Sid() == _struckId && Combat.Living(_struck) && !MeteorSlam.IsDoomed(_struck) ? _struck : null;
        }

        /// <summary>Remembers the enemy just struck, if it still stands (not killed by the blow, not flung to its death).</summary>
        private void Struck(Actor u)
        {
            if (Combat.Living(u) && !MeteorSlam.IsDoomed(u))
            {
                _struck = u;
                _struckId = u.Sid();
            }
        }

        // ------------------------------------------------------------ blows --

        /// <summary>
        /// One heel blow on an enemy, thrown 'knock' tiles from the centre: the class rules (heavy ones
        /// take a glancing blow and stagger), the answer check, damage, the flight, the flash.
        /// 'size' is the hit star (0 jab .. 2 heavy).
        /// </summary>
        private bool Strike(Actor u, float mult, float knock, float apex, float spin, int size)
        {
            long id = u.Sid();
            if (!_touched.Add(id))
            {
                return false;
            }
            if (!Combat.Allowed(A, u, out float cm))
            {
                Glance(u);
                return true;
            }
            if (Combat.Reach(this, u, true) != Contact.Hit)
            {
                return false;
            }
            HitResult r = Combat.Hit(A, u, mult * cm, true, true, 0f, this);
            _crit |= r.Crit && Level >= 2;
            _hits++;
            Struck(u);
            Vector2 away = Away(u);
            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
            Vector2 body = u.current_position + new Vector2(0f, u.position_height + 4f * vpx);
            if (Kit.Visible(body))
            {
                Fx.HitSpark(body, Px, size, away + Vector2.up * 0.3f, 3 + size * 2);
                if (Level >= 2)
                {
                    TierFlash(u, 0.12f);
                    if (r.Crit)
                    {
                        Kit.ImpactFrame(body, Px, 0.35f, _pal, true);
                    }
                }
            }
            Throw(u, knock, apex, spin);
            return true;
        }

        /// <summary>A blow on an enemy that was already reached (it stood its ground in the storm).</summary>
        private void HitBlast(Actor u, float mult)
        {
            if (!Combat.Allowed(A, u, out float cm))
            {
                return;
            }
            HitResult r = Combat.Hit(A, u, mult * cm, true, true, 0f, this);
            _crit |= r.Crit;
            _hits++;
            Struck(u);
            TierFlash(u, 0.12f);
            Throw(u, 3f, 1.2f, Random.value < 0.5f ? -800f : 800f);
        }

        /// <summary>Too heavy for this tier: a glancing blow and a stagger, never a launch.</summary>
        private void Glance(Actor u)
        {
            Combat.Hit(A, u, 0.5f, true, true, 0f, this);
            Combat.Stagger(u, 0.35f);
            float vpx = Mathf.Clamp(u.actor_scale, 0.04f, 0.4f);
            Vector2 body = u.current_position + new Vector2(0f, u.position_height + 5f * vpx);
            if (Kit.Visible(body))
            {
                Fx.HitSpark(body, Px, 0, Away(u), 2);
            }
        }

        /// <summary>Knocks a unit away from the centre: critters fly far, brutes barely budge (heavy ones stagger).</summary>
        private void Throw(Actor u, float knock, float apex, float spin)
        {
            if (!u.isAlive())
            {
                return;
            }
            // (a stronger fighter's kick throws them further)
            float d = knock * _k * _scale;
            float bulk = Bulk(u);
            if (bulk <= 80f)
            {
                d *= 1.6f;
                apex *= 1.3f;
            }
            else if (bulk > 180f || Weights.Of(u) >= Weight.Brute)
            {
                d *= 0.65f;
                apex *= 0.7f;
            }
            Combat.Launch(u, _c, d, apex * _k, spin);
        }

        private Vector2 Away(Actor u)
        {
            Vector2 away = u.current_position - _c;
            return away.sqrMagnitude > 1e-4f ? away.normalized : (_right0 ? Vector2.right : Vector2.left);
        }

        /// <summary>The released puppets fly once the game has them back (the tick after letting go).</summary>
        private void Flings()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Fling f = _pending[i];
                Actor u = f.U;
                if (u == null || u.Sid() != f.Id || !u.isAlive())
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (Moves.Locked(u))
                {
                    f.Waits++;
                    if (f.Waits > 4)
                    {
                        _pending.RemoveAt(i);   // someone else has it now
                    }
                    else
                    {
                        _pending[i] = f;
                    }
                    continue;
                }
                FlingFrom(u, f.Dir, f.Dist, f.Apex, f.Spin, Mathf.Max(0f, u.position_height));
                _pending.RemoveAt(i);
            }
        }

        /// <summary>
        /// Launches a unit that is already up in the air: the same push carries it further from up
        /// there, so the push is shortened to land about 'dist' tiles out. Never off the map.
        /// </summary>
        private static void FlingFrom(Actor u, Vector2 dir, float dist, float apex, float spin, float h0)
        {
            Vector2 p = u.current_position;
            for (int i = 0; i < 8; i++)
            {
                Vector2 land = p + dir * dist;
                if (land.x > 2f && land.y > 2f && land.x < MapBox.width - 3 && land.y < MapBox.height - 3)
                {
                    break;
                }
                dist *= 0.6f;
            }
            float longer = (1f + Mathf.Sqrt(1f + h0 / Mathf.Max(0.15f, apex))) * 0.5f;
            Combat.LaunchDir(u, dir, dist / longer, apex, spin);
        }

        // -------------------------------------------------------- the look --

        /// <summary>Each half of a crescent lives this share of the whole crescent's life...</summary>
        private const float HalfLife = 0.6f;

        /// <summary>...and the front half starts this share of it after the back half.</summary>
        private const float HalfGap = 0.3f;

        /// <summary>
        /// A crescent ellipse round the unit at 'lift' above its feet, sweeping a full turn in the
        /// spin's direction from 'startDeg', following the unit as it hops and rises ('mid': riding
        /// halfway up the funnel instead). It is drawn as two half-sweeps one after the other: the
        /// half passing behind the body (the top of the ellipse on screen) under the units, the
        /// half in front over them, so the swoosh wraps round the body instead of cutting across
        /// its chest (in game the whole ring was drawn over the unit). 'behind' keeps it all under
        /// the units (the dust ring at the feet).
        /// </summary>
        private void Crescent(float radiusTiles, float ratio, int thick, float life, float lift, float startDeg, Color core, Color edge, bool behind = false, bool mid = false)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(radiusTiles / Px), 3, 130);
            if (behind)
            {
                CrescentPart(Art.Arc(r, ratio, startDeg, startDeg + 360f * _dir, thick), FxLayer.Behind, life, 0f, lift, mid, core, edge);
                return;
            }
            float half = 180f * _dir;
            for (int i = 0; i < 2; i++)
            {
                float a0 = Mathf.Repeat(startDeg + half * i, 360f);   // (few distinct arcs to draw and cache)
                bool back = Mathf.Sin((a0 + 90f * _dir) * Mathf.Deg2Rad) > 0f;
                CrescentPart(Art.Arc(r, ratio, a0, a0 + half, thick), back ? FxLayer.Behind : FxLayer.Over,
                    HalfLife * life, HalfGap * life * i, lift, mid, core, edge);
            }
        }

        private void CrescentPart(Sprite[] arc, FxLayer layer, float life, float delay, float lift, bool mid, Color core, Color edge)
        {
            Particle p = Fx.Spawn(arc, CrescentAt(lift, mid), Px, life, layer, 5);
            if (p == null)
            {
                return;
            }
            p.Delay = delay;
            p.Color(core, edge, Fx.WithA(edge, 0.6f));
            p.Step = (q, d) => q.Pos = CrescentAt(lift, mid);
        }

        private Vector2 CrescentAt(float lift, bool mid)
        {
            return mid ? Pose.Ground + new Vector2(0f, Pose.Height * 0.5f + 0.2f * _k) : Pose.Ground + new Vector2(0f, Pose.Height + lift);
        }

        /// <summary>
        /// How far round (degrees) the head of a crescent of this life has swept 't' seconds after
        /// it was spawned: each half's head runs its 180 degrees in the first half of its own life
        /// (Art.Arc), the front half starting when the back half is done.
        /// </summary>
        private static float SweepHead(float t, float life)
        {
            float g = Mathf.Max(0.01f, HalfGap * life);
            return t < g ? 180f * Anim.OutCubic(t / g) : 180f + 180f * Anim.OutCubic(Mathf.Clamp01((t - g) / g));
        }

        /// <summary>A crab spins with its claws out: two claws whirl round at the edge of the swoosh.</summary>
        private void Claws(float radius, float life, Color c)
        {
            if (!_crab)
            {
                return;
            }
            float px = Px;
            for (int i = 0; i < 2; i++)
            {
                float off = i * Mathf.PI;
                Particle p = Fx.Spawn(Art.Chunks[7], BodyPos(), px, life, FxLayer.Over, 6);
                if (p == null)
                {
                    return;
                }
                p.Color(Color.white, c, Fx.WithA(c, 0f));
                System.Action<Particle, float> step = (q, d) =>
                {
                    float a = _spin * Mathf.Deg2Rad * _dir + off + (_right0 ? 0f : Mathf.PI);
                    q.Pos = Pose.Ground + new Vector2(Mathf.Cos(a) * radius * 0.9f, Pose.Height + Hip + Mathf.Sin(a) * radius * 0.45f);
                    q.Rot = a * Mathf.Rad2Deg;
                };
                p.Step = step;
                _claws.Add(new Claw { P = p, Step = step });
            }
        }

        /// <summary>Dust kicked out along the ground to about 'radius' tiles (soil-coloured), and grass bits on grass.</summary>
        private void DustRing(float radius, int n, float life)
        {
            float px = Px;
            // Kit.DustRing slides 'speed' texels/s with drag 4: it travels about speed * px / 4.5
            Kit.DustRing(_c, px, _tile, n, 4.5f * radius / px, life);
            Grass(px, radius, 5);
        }

        private void Grass(float px, float radius, int n)
        {
            if (!_grass || _tile == null)
            {
                return;
            }
            Color top = Fx.TopColor(_tile);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(Art.Pixel, _c + dir * 2f * px, px, Random.Range(0.5f, 0.8f), FxLayer.Top, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(0.6f, 1.1f) * radius * 2.2f, Random.Range(1.5f, 3f) * _k, 1.5f, 14f * _k);
                p.Bounce = 0.2f;
                p.Sized(1f, 1f).Color(top, Fx.Darken(top, 0.2f), Fx.WithA(top, 0f));
            }
        }

        /// <summary>A scuff of dust under the turning feet.</summary>
        private void Scuff(float px, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], _c + new Vector2(side * Random.Range(1f, 3f), Random.Range(-1f, 0.5f)) * px,
                    px, Random.Range(0.3f, 0.45f), FxLayer.Behind, 2);
                p?.Moving(new Vector2(side * Random.Range(8f, 16f), Random.Range(-2f, 2f)) * px * (_big ? 1.4f : 1f), 3f * px, 5f)
                    .Sized(_big ? 0.8f : 0.6f, _big ? 1.3f : 1f)
                    .Color(Fx.WithA(_dust, 0.75f), Fx.WithA(_dust, 0.4f), Fx.WithA(_dust, 0f));
            }
        }

        /// <summary>A hard blow on a tiered enemy: it flashes its own tier colour.</summary>
        private static void TierFlash(Actor v, float life)
        {
            Tier t = Tiers.Of(v);
            if (t == Tier.None)
            {
                return;
            }
            Color c = Tiers.Color(t);
            BodyFlash(v, life, Fx.WithA(c, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
        }

        /// <summary>A flat-colour copy of the unit that follows it wherever the game draws it (flying, tumbling), fading through 'colors'.</summary>
        private static void BodyFlash(Actor v, float life, params Color[] colors)
        {
            if (!Fx.HasSilhouette || v == null || !v.is_visible)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, v.current_position, 1f, life, FxLayer.Over, 1, true);
            if (p == null)
            {
                return;
            }
            long id = v.Sid();
            p.UseEuler = true;
            p.Color(colors);
            Place(p, v);
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    Place(q, v);
                }
                else
                {
                    q.Kill();
                }
            };
        }

        /// <summary>Puts a silhouette exactly where the game draws the unit.</summary>
        private static void Place(Particle q, Actor v)
        {
            q.Pos = v.current_position + v.move_jump_offset + new Vector2(0f, v.position_height);
            q.Z = 0f;
            q.Euler = v.current_rotation;
            q.SX = v.current_scale.x;
            q.SY = v.current_scale.y;
        }

        private static Color TierTint(Actor v)
        {
            Tier t = Tiers.Of(v);
            return t != Tier.None ? Tiers.Color(t) : Color.white;
        }

        // ---------------------------------------------------- held effects --

        private void SpawnShadow(float px)
        {
            _shadow = Fx.Spawn(Art.Disk(ShadowRx(1f), 0.42f, 0.45f), _c, px, 1f, FxLayer.Ground, 3);
            if (_shadow != null)
            {
                _shadow.Held = true;
                _shadow.Snap = true;
                _shadow.Color(new Color(0f, 0f, 0f, 0.36f));
            }
        }

        private void UpdateShadow()
        {
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            float h = _rise > 0f ? Mathf.Clamp01(Pose.Height / _rise) : 0f;
            Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, 0.5f, h)), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != s)
            {
                _shadow.R.sprite = s;
                _shadow.Frames[0] = s;
            }
            _shadow.Pos = Pose.Ground;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.36f, 0.16f, h));
        }

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        public override void Frame()
        {
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, GlowColor());
        }

        /// <summary>The level III glow: gathering, burning through the vortex, white at the burst, cooling.</summary>
        private Color GlowColor()
        {
            switch (_phase)
            {
                case Phase.Gather:
                    {
                        float k = Mathf.Clamp01(_pt / (TGather * _slow));
                        return Fx.WithA(_pal.Edge, 0.45f * k * (0.75f + 0.25f * Mathf.Sin(_pt * 50f)));
                    }
                case Phase.Vortex:
                    {
                        float k = Mathf.Clamp01(_pt / (TVortex * _slow));
                        // kept under half strength: the spinning body must still read through it
                        return Fx.WithA(Color.Lerp(_pal.Edge, _pal.Core, k * k), Mathf.Lerp(0.22f, 0.46f, k));
                    }
                case Phase.Descent:
                    return Fx.WithA(Color.Lerp(_pal.Core, _pal.Edge, Mathf.Clamp01(_pt / 0.15f)), Mathf.Lerp(0.7f, 0.3f, Mathf.Clamp01(_pt / DescentT)));
                default:
                    return Fx.WithA(_pal.Edge, Mathf.Lerp(0.3f, 0f, Mathf.Clamp01(_pt / (0.15f * _pace))));
            }
        }

        private void KillShadow()
        {
            _shadow?.Kill();
            _shadow = null;
        }

        private void KillGlow()
        {
            _glow?.Kill();
            _glow = null;
        }

        private void KillHeld()
        {
            KillShadow();
            KillGlow();
            _marker?.Kill();
            _marker = null;
            foreach (Claw c in _claws)
            {
                // (only one still whirling: an expired claw's particle may be someone else's effect by now)
                if (c.P != null && c.P.Alive && (object)c.P.Step == c.Step)
                {
                    c.P.Kill();
                }
            }
            _claws.Clear();
        }

        /// <summary>Lets go of everything still whirling (falling from where they are, over solid ground).</summary>
        private void ReleaseAll()
        {
            foreach (Orb o in _orbit)
            {
                if (!o.Gone && !o.P.Finished)
                {
                    if (!Combat.SafeSpot(o.P.Pose.Ground) && Combat.FindLanding(o.P.Pose.Ground, 2f, out Vector2 spot))
                    {
                        o.P.Pose.Ground = spot;
                    }
                    o.P.Release(o.P.Pose.Height <= 0.02f);
                }
                o.Gone = true;
                o.Shadow?.Kill();
                o.Shadow = null;
            }
        }

        public override void Abort()
        {
            ReleaseAll();
            KillHeld();
            _pending.Clear();
            _sweep.Clear();
        }

        public override void OnEnd(bool aborted)
        {
            ReleaseAll();
            KillHeld();
            if (!aborted)
            {
                // a released puppet the game has not handed back yet still gets its flight
                Flings();
            }
            _pending.Clear();
            _sweep.Clear();
            _orbit.Clear();
        }
    }

    /// <summary>
    /// Cyclone Heel as a technique. Entry: Novice. The AI spins when two or more enemies are in
    /// reach (three for the Tempest Spiral below Master), or at any level when it is surrounded,
    /// to break out.
    /// </summary>
    internal sealed class CycloneHeelTech : Technique
    {
        public override string Id => "cyclone_heel";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Area;

        /// <summary>A spinning heel: everyone in reach swept up and flung (III: a vortex and a heel from above).</summary>
        public override Form Forms => Form.Kick | Form.Spin | Form.Area;

        public override Form FormsAt(int level) => level >= 3 ? Form.Kick | Form.Spin | Form.Area | Form.Aerial | Form.Overhead : Forms;

        public override float[] ManaCost => new[] { 10f, 18f, 30f };
        public override float[] Cooldown => new[] { 6f, 7f, 9f };

        /// <summary>Who would be in reach (reused: the planner runs several times a second for nearly every fighter).</summary>
        private static readonly List<Actor> _hit = new List<Actor>();

        public static float Radius(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.4f : 2f;
        }

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Mathf.Sqrt(Mathf.Clamp(c.Self.actor_scale / 0.1f, 0.6f, 4f));
            // (the same reach the move will have: a stronger fighter's storm is wider)
            float radius = Radius(level) * k * SpinWreck.Scale(c.Tier);
            List<Actor> hit = _hit;
            hit.Clear();
            int n = c.CountNear(c.Pos, radius + 0.2f, hit);
            bool surrounded = c.Surrounding() >= 3;
            if (!c.Forced)
            {
                // a master fights with techniques only: two in reach is already worth its storm
                int need = c.Linked ? 1 : level >= 3 && c.Tier < Tier.Master ? 3 : 2;
                if (n == 0 || n < need && !surrounded)
                {
                    return null;
                }
            }
            float mult = level <= 1 ? 0.7f : level == 2 ? 1.3f : 2.8f;
            float score = 0f;
            foreach (Actor v in hit)
            {
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, mult), cm);
            }
            hit.Clear();
            // it is the answer to a crowd: every extra body it kicks away is space won (in game it
            // was never chosen to open, only as a chain, because a weak kick on two enemies never
            // beat an ordinary attack; round 2: level I was still never chosen in a battle). The
            // space is worth about half a blow per body at I and II, so two in reach is enough.
            float space = level >= 3 ? 12f : Mathf.Max(12f, 0.6f * c.D);
            score += space * Mathf.Clamp(n - 1, 0, 4);
            if (level <= 2 && n >= 2)
            {
                score += 0.4f * c.D;
            }
            if (surrounded)
            {
                score += 25f;   // the answer to an encirclement
            }
            if (n >= 2 && c.HpPct < 0.5f)
            {
                score += 15f;   // hurt and hemmed in: break out
            }
            if (n <= 1 && level >= 3)
            {
                score *= 0.7f;  // the Tempest Spiral on a lone enemy is a waste: a finisher fits better
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Actor face = c.Focus;
            return new Plan { Target = face, Point = c.Pos, Count = n, Score = score, AreaRadius = radius };
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor face = p.Target != null && p.Target.isAlive() ? p.Target : null;
            return CycloneHeel.Begin(a, p.Level, this, face);
        }
    }
}
