using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// STARFALL (god ability: the crowd). The sky turns crimson and the world stops; the God
    /// launches ten tiles into the air, spins once and raises its arm in front of a gold sun. The
    /// world wakes at quarter speed into a rain of stars, each falling on its own enemy cluster,
    /// and the God follows them down as the Sun Meteor into the thickest of the crowd.
    ///
    /// Beat sheet (seconds; full mode: 1.3 s of frozen world, then 2.1 s of 25% slow motion in
    /// which everything but the God and its stars plays slow):
    ///
    ///   TELL     0.00-0.30  gold ring closes on the feet, focus lines converge, the world freezes,
    ///                       the veil fades to 68% blood red: the sky has turned
    ///   ASCEND   0.30-0.90  squash 0.1 s, launch to at most 10 tiles, kept in the upper third of
    ///                       the view and under the title card's band (the apex is always seen):
    ///                       takeoff ring, dust ring, rocks, cracks; stretched smear, gold
    ///                       afterimages, vertical gold speed lines; our shadow stays and shrinks
    ///   APEX     0.90-1.30  one 360 spin (tucked, smeared), then a ting at the raised hand, the
    ///                       sprite stretches 120% tall and a 1.5-tile gold halo blooms behind it;
    ///                       a small sun stays glowing behind the God through the rain
    ///   RAIN     1.30-2.90  the world resumes at 25%; where the Sun Meteor will fall is settled
    ///                       first (the densest knot within 22 tiles: the army the God saw; the
    ///                       ones standing there are the Sun's), then the sky falls on the WHOLE
    ///                       battlefield (a disc around the army, 20-28 tiles across): N = 18 +
    ///                       enemies stars (30 to 48), all from off the top of the view on one
    ///                       30-degree diagonal, landing 0.03-0.16 s apart; the clusters first
    ///                       (at most three stars per foe, each tracks its victim; a Grandmaster
    ///                       or God is worth an army to the sky: up to six, homing hard), the
    ///                       rest spread evenly over the land of the field (a point over open
    ///                       water is tried elsewhere, only now and then kept for the spray);
    ///                       a unit that answers a star (a mirage) dodged that star, not the
    ///                       sky: half a second later the next one asks again; a 3-px white-hot head, a three-layer
    ///                       fire trail, flames and slow smoke; each impact: flash, shockwave, dust
    ///                       ring, embers (a glowing crater decal on the big ones and on hits) and
    ///                       a real BURNING CRATER in the ground (Kit.Ravage force 3, 2 tiles; no
    ///                       flames where our own people stand); 70 + 1.8xD within 1.8 tiles (50%
    ///                       at the edge), launched 1.5 tiles. The Sun's own stay at 1 HP or more
    ///                       and are only knocked down, for the Sun
    ///                       (a god war: the big stars tear 2.6-tile bowls and the Sun's crater is
    ///                       8 tiles: two powers of that scale reshape the map)
    ///   DIVE     2.90-3.40  the densest knot now (bodies still in the air count; the God's equal
    ///                       draws the Sun as an army would) is marked; the
    ///                       sun collapses into the God; wind-up tuck, sound barrier, then the Sun
    ///                       Meteor: a stretched accelerating dive at 1.5x Meteor Slam III scale
    ///   IMPACT   3.40       the red sky flashes white and burns away over 0.45 s; impact frame
    ///                       (black on a crit), 0.25 s hit-stop, a 3-tile crater with gold cracks
    ///                       for 30 s, rings at 2, 4 and 6 tiles, dust wall, rocks, sparks, embers;
    ///                       THE LAND RESHAPED: a deep burning crater of 6 tiles (force 4; its
    ///                       heart, 2.5 tiles where the God lands, blown clear of fire); as the
    ///                       shockwave passes, every star crater flares gold; 180 + 5xD at the
    ///                       centre falling to 40% at 4 tiles
    ///                       (bodies in the air included), knockdown out to 6 tiles (bodies fly
    ///                       when the hit-stop ends; stunned 1.4-0.8 s times the tier's Linger,
    ///                       and a flow strike is taken over, never a shield)
    ///   RISE     3.40-3.95  held superhero landing; from 3.62 it springs up (hopping out if the
    ///                       crater drowned its footing); one gold ember; it chains straight on
    ///                       (Scattered, or Launched on a lone survivor)
    ///
    /// A Combat God caught in it loses at most 40% of its life to one Starfall (an equal is shaken,
    /// not erased). Light mode (off screen, zoomed out, fast, busy cinema, a god war after its
    /// first cinematic): the same timeline at world speed, no freeze, veil or slow motion, fewer
    /// particles. Allies are never hit; heavier-than-class enemies only take a glancing blow.
    /// </summary>
    internal sealed class Starfall : Move
    {
        private const float TTell = 0.30f;
        private const float TLift = 0.40f;
        private const float TApex = 0.90f;
        private const float TPose = 1.12f;
        private const float TRain = 1.30f;
        private const float TFirst = 1.52f;
        private const float TLast = 2.88f;
        private const float TDive = 2.90f;
        private const float TDrop = 3.00f;
        private const float TImpact = 3.40f;
        private const float TFly = 3.65f;
        // a God lands crisply and goes (the landing used to hold to 4.50)
        private const float TRise = 3.62f;
        private const float THop = 0.25f;
        private const float TEmber = 3.80f;
        private const float TEnd = 3.95f;
        /// <summary>Seconds a star takes from the top of the view to the ground.</summary>
        private const float Flight = 0.30f;

        /// <summary>Tiles a star's blow reaches (its crater is a little smaller).</summary>
        public const float MeteorRadius = 1.8f;
        /// <summary>A star's burning crater in the ground (Kit.Ravage radius).</summary>
        private const float StarCrater = 2f;
        public const float DiveRadius = 4f;
        public const float KnockRadius = 6f;
        /// <summary>The Sun Meteor's crater: the land reshaped (Kit.Ravage radius, force 4).</summary>
        private const float SunCrater = 6f;
        /// <summary>The most of a Combat God's life one Starfall may take.</summary>
        private const float GodShare = 0.4f;
        /// <summary>The most stars aimed at one foe (the rest fall on the field).</summary>
        private const int StarsPerFoe = 3;
        /// <summary>The most stars aimed at a Grandmaster or God: the sky hunts the God's equal.</summary>
        private const int StarsPerRival = 6;
        /// <summary>How much more a Grandmaster or God is worth to the rain and the Sun than its weight says.</summary>
        private const float RivalValue = 5f;
        /// <summary>
        /// Move seconds an answer (a mirage, a parry) shelters a unit from the rest of the sky: it
        /// dodged that star, not the whole Starfall (the next star or the Sun asks again).
        /// </summary>
        private const float AnswerWindow = 0.5f;
        /// <summary>A god war: the craters of the big stars and the Sun's are this much wider.</summary>
        private const float WarRuin = 1.3f;

        /// <summary>How many stars fall: the whole sky (30 to 48, more for a bigger army).</summary>
        public static int StarCount(int foes)
        {
            return Mathf.Clamp(18 + foes, 30, 48);
        }

        /// <summary>The turned sky: over grass it reads brick red, over sand blood red.</summary>
        private static readonly Color Sky = new Color(0.42f, 0.01f, 0.05f, 1f);
        private const float SkyAlpha = 0.68f;
        private static readonly Color SkyFlash = new Color(1f, 0.96f, 0.84f, 1f);

        private static readonly Color White = Color.white;
        private static readonly Color Gold = GodFx.Gold;
        private static readonly Color GoldLight = GodFx.GoldLight;
        private static readonly Color Hot = GodFx.WhiteHot;
        private static readonly Color Smoke = new Color(0.34f, 0.3f, 0.3f, 1f);
        // the Sun Meteor burns gold-white instead of orange
        private static readonly Color Sun1 = new Color(1f, 0.93f, 0.6f, 1f);
        private static readonly Color Sun2 = new Color(1f, 0.74f, 0.26f, 1f);
        private static readonly Color Sun3 = new Color(0.86f, 0.4f, 0.08f, 1f);

        private const int BTell = 1, BTell2 = 2, BLift = 4, BPose = 8, BRain = 16, BDive = 32, BDrop = 64,
            BImpact = 128, BFly = 256, BClear = 512, BEmber = 1024, BHop = 2048, BHopLand = 4096;

        private sealed class Meteor
        {
            public Vector2 Land;
            public float At;
            /// <summary>
            /// Move time its flight begins: Flight before it lands, but never before the rain begins
            /// (the first stars fall a little faster, so they still enter from the top of the view).
            /// </summary>
            public float From;
            public Actor Victim;
            public long VictimId = -1;
            public float Fall;
            public bool Spawned, Done, Shown, Big;
            public int Frame;
            public Particle Head, Halo, Trail0, Trail1, Trail2;
        }

        private struct Fly
        {
            public Actor U;
            public long Id;
            public float Distance, Apex, Spin;
        }

        private readonly GodCast _cast;
        private readonly bool _full;
        private bool _cine;
        private readonly Vector2 _from;
        private Vector2 _drift;
        private Vector2 _dive;
        private Vector2 _diveFrom;
        private float _diveH;
        private float _hoverH;
        private readonly float _apex;
        private readonly float _k;
        private readonly Palette _pal;
        private readonly Vector2 _fall;
        private int _beats;
        private float _ghost, _lines, _glint;
        private Vector2 _lastTrail;
        private bool _trail;
        private int _diveHits;
        /// <summary>The Sun's zone, settled when the rain begins: stars there break, the Sun finishes.</summary>
        private Vector2 _sunAt;
        private bool _sunSet;
        /// <summary>The ones standing in the Sun's zone when the rain began: no star may end them, wherever they are thrown.</summary>
        private readonly HashSet<long> _kept = new HashSet<long>();
        /// <summary>The rain's disc: the whole battlefield.</summary>
        private Vector2 _rainAt;
        private float _rainR;
        /// <summary>Life taken from each Combat God caught in it (GodShare at most).</summary>
        private readonly Dictionary<long, float> _dealt = new Dictionary<long, float>();
        /// <summary>The crater drowned its footing: it hops out to here as it rises.</summary>
        private Vector2 _hopTo;
        private bool _hop;

        private Particle _shadow, _clone, _glow, _sun, _marker, _markerRing;
        private readonly List<Meteor> _meteors = new List<Meteor>();
        private readonly List<Actor> _foes = new List<Actor>();
        private readonly List<Actor> _allies = new List<Actor>();
        private readonly HashSet<long> _reached = new HashSet<long>();
        /// <summary>Units that answered a star (move time until which the sky passes them by).</summary>
        private readonly Dictionary<long, float> _answered = new Dictionary<long, float>();
        private readonly List<Fly> _fly = new List<Fly>();
        /// <summary>A god war (a Grandmaster or God stands against it): the land is torn wider.</summary>
        private readonly bool _war;

        private Starfall(Actor a, GodCast cast) : base(a)
        {
            _cast = cast;
            _full = cast.Full;
            _cine = _full;
            _from = a.current_position;
            _drift = _from;
            _k = Mathf.Clamp(K, 0.8f, 1.5f);
            _pal = Palette.For(Tier.God, 3);
            _dive = _from;
            if (cast.Point.HasValue && Combat.SafeSpot(cast.Point.Value))
            {
                _dive = Combat.Clamp(cast.Point.Value);
            }
            // every star falls along the same diagonal, over the God's shoulder
            float fwd = Pose.FaceRight ? 1f : -1f;
            _fall = new Vector2(0.5f * fwd, -0.866f);
            Pose.HideShadow = true;
            _apex = ApexHeight();
            _hoverH = _apex;
            _war = Godhood.AtWar(a);
        }

        public override bool Cinematic => _cine;

        public override bool Intangible => T > TLift && T < TImpact;

        /// <summary>
        /// The tell, played at world speed (light staging): the gold ring closing on its feet is a God
        /// gathering the sky, and an equal can still kick it out of it. (A full cinematic freezes the world.)
        /// </summary>
        public override bool Charging => !_full && T < TTell && !Finished;

        public static bool Start(Actor a, GodCast cast, Technique tech)
        {
            if (!Moves.CanPerform(a))
            {
                return false;
            }
            Starfall m = new Starfall(a, cast) { Tech = tech, Level = 1 };
            if (!Moves.Start(m))
            {
                return false;
            }
            m.Open();
            return true;
        }

        /// <summary>The world stops (in full mode) the moment it is unleashed, not a frame later.</summary>
        private void Open()
        {
            float px = Px;
            if (_full)
            {
                Cinema.Hold(_cast);
                Cinema.Dim(SkyAlpha, Sky, TTell);
            }
            _shadow = Fx.Spawn(Art.Disk(ShadowRx(1f), 0.42f, 0.45f), _from, px, 1f, FxLayer.Ground, 3);
            if (_shadow != null)
            {
                _shadow.Held = true;
                _shadow.Snap = true;
                _shadow.Cine = _full;
                _shadow.Color(new Color(0f, 0f, 0f, 0.36f));
            }
            Sprite s = Fx.UnitSprite(A);
            if (s != null)
            {
                if (_full)
                {
                    // the God itself stays lit above the turned sky
                    _clone = Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Cine, 25);
                    if (_clone != null)
                    {
                        _clone.Held = true;
                        _clone.UseEuler = true;
                        Fx.Glue(_clone, this, White);
                    }
                }
                if (Fx.HasSilhouette)
                {
                    _glow = Fx.Spawn(s, DrawPos(), 1f, 1f, _full ? FxLayer.Cine : FxLayer.Over, _full ? 26 : 0, true);
                    if (_glow != null)
                    {
                        _glow.Held = true;
                        _glow.UseEuler = true;
                        _glow.Cine = _full;
                        Fx.Glue(_glow, this, Fx.WithA(Gold, 0f));
                    }
                }
            }
            Anim.Sound("event:/SFX/POWERS/DivineLight", A.current_tile);
            Dev.Watch(_from + new Vector2(0f, _apex * 0.55f));
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>
        /// At most 10 tiles up, kept in the upper third of the view: the title card (Cinema.Title,
        /// the band from 44% to 80% of the half-height above the centre) must never cover the apex,
        /// so the God's head stays under 38% of it.
        /// </summary>
        private float ApexHeight()
        {
            float h = 10f * _k;
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam != null && Fx.OnScreen(_from, 0.1f))
            {
                float ceiling = cam.transform.position.y + cam.orthographicSize * 0.38f;
                float body = Mathf.Max(0.6f, Pose.PivotUp * 2.4f * A.actor_scale);
                h = Mathf.Clamp(ceiling - _from.y - body, 3.5f * _k, 10f * _k);
            }
            return h;
        }

        private bool Beat(int b)
        {
            if ((_beats & b) != 0)
            {
                return false;
            }
            _beats |= b;
            return true;
        }

        private bool Visible => Kit.Visible(Pose.Ground, 0.4f) || Kit.Visible(BodyPos(), 0.3f);

        /// <summary>Above the veil in full mode, over everything otherwise.</summary>
        private FxLayer Hi => _full ? FxLayer.Cine : FxLayer.Top;

        /// <summary>Behind the God: above the veil but under its clone in full mode, behind units otherwise.</summary>
        private FxLayer Back => _full ? FxLayer.Cine : FxLayer.Behind;

        /// <summary>Particle counts: fewer in light mode (and Fx.Count trims them when zoomed out).</summary>
        private int N(int n)
        {
            return Fx.Count(_full ? n : Mathf.Max(1, (n * 2 + 2) / 3));
        }

        /// <summary>A radius in tiles as texels at this unit's pixel size.</summary>
        private float Tex(float tiles)
        {
            return Mathf.Clamp(tiles * _k / Px, 2f, 140f);
        }

        private int Rx(float tiles)
        {
            return Mathf.RoundToInt(Tex(tiles));
        }

        private Vector2 HandPos()
        {
            float fwd = Pose.FaceRight ? 1f : -1f;
            return HeadPos() + new Vector2(fwd * 1f, 2.5f) * Px;
        }

        private void Ghost(float life, params Color[] colors)
        {
            Particle p = Fx.Afterimage(this, life, colors);
            if (_full)
            {
                GodFx.Lift(p);
            }
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            float t = T;
            if (t < TTell)
            {
                Tell(t);
            }
            else if (t < TApex)
            {
                Ascend(t, dt);
            }
            else if (t < TRain)
            {
                Apex(t, dt);
            }
            else if (t < TDive)
            {
                Rain(t, dt);
            }
            else if (t < TImpact)
            {
                Dive(t, dt);
            }
            else
            {
                Land(t);
            }
            Meteors(t, dt);
            UpdateShadow();
            if (t >= TEnd)
            {
                Finish();
            }
        }

        // --------------------------------------------------------------- tell --

        private void Tell(float t)
        {
            float px = Px;
            if (Beat(BTell) && Visible)
            {
                int mark = GodFx.Mark();
                Fx.Ring(_from, px, TTell, 18f * _k, 3f, 1, 2, FxLayer.Ground, 4, Art.Ground, 1.6f,
                    Fx.WithA(Gold, 0f), Fx.WithA(Gold, 0.85f), Fx.WithA(GoldLight, 1f));
                Fx.FocusLines(BodyPos(), px, N(12), 18f * _k, 34f * _k, 0.22f);
                if (_full)
                {
                    GodFx.Lift(mark);
                }
            }
            if (t >= 0.15f && Beat(BTell2) && Visible)
            {
                int mark = GodFx.Mark();
                Fx.FocusLines(BodyPos(), px, N(10), 14f * _k, 28f * _k, 0.18f);
                if (_full)
                {
                    GodFx.Lift(mark);
                }
            }
            // gathering: the shoulders rise, a one-pixel power tremble at the end
            float k = Anim.Smooth(0f, TTell, t);
            Pose.Ground = _from;
            Pose.Height = 0f;
            Pose.SX = 1f - 0.03f * k;
            Pose.SY = 1f + 0.05f * k;
            Pose.Rot = -3f * k;
            Pose.Nudge = t > 0.18f ? new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
        }

        // ------------------------------------------------------------- ascend --

        private void Ascend(float t, float dt)
        {
            float px = Px;
            Pose.Ground = _from;
            if (t < TLift)
            {
                // the squash before the launch
                float c = Anim.OutCubic(Mathf.Clamp01((t - TTell) / (TLift - TTell)));
                Pose.SX = Mathf.Lerp(0.97f, 1.18f, c);
                Pose.SY = Mathf.Lerp(1.05f, 0.76f, c);
                Pose.Rot = -7f * c;
                Pose.Height = 0f;
                Pose.Nudge = new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            if (Beat(BLift))
            {
                Pose.Nudge = Vector2.zero;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    Takeoff(px);
                }
            }
            float k = Mathf.Clamp01((t - TLift) / (TApex - TLift));
            Pose.Height = _apex * Anim.OutCubic(k);
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
            Pose.SX = Mathf.Lerp(0.64f, 1f, s);
            Pose.SY = Mathf.Lerp(1.62f, 1f, s);
            Pose.Rot = Mathf.Lerp(-4f, 4f, k);
            if (!Visible)
            {
                return;
            }
            _ghost -= dt;
            if (_ghost <= 0f)
            {
                _ghost = 0.035f;
                Ghost(0.18f, new Color(1f, 0.97f, 0.8f, 0.85f), Fx.WithA(Gold, 0.6f), Fx.WithA(GodFx.GoldDeep, 0f));
            }
            _lines -= dt;
            if (_lines <= 0f && k < 0.85f)
            {
                _lines = 0.03f;
                Kit.SpeedLines(BodyPos(), Vector2.down, px, 2, 7f, 0.18f, Fx.WithA(Gold, 0.85f), Hi);
            }
        }

        /// <summary>The ground gives: takeoff ring, a dust ring, rocks thrown up, small cracks, streaks.</summary>
        private void Takeoff(float px)
        {
            int mark = GodFx.Mark();
            WorldTile tile = A.current_tile;
            Fx.Ring(_from, px, 0.26f, 3f, 22f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                new Color(1f, 1f, 1f, 0.95f), Fx.WithA(GoldLight, 0.6f), Fx.WithA(Gold, 0f));
            Kit.DustRing(_from, px, tile, N(12), 62f, 0.6f, _k);
            Kit.Debris(_from, px, tile, N(8), 1.1f * _k);
            Kit.Crater(_from, px, tile, false, 3f, true);
            Kit.SpeedLines(_from + new Vector2(0f, 8f * px), Vector2.up, px, N(6), 5f, 0.2f, new Color(1f, 1f, 1f, 0.85f));
            if (_full)
            {
                GodFx.Cine(mark);   // the world is held: what the God kicks up still moves
            }
        }

        // --------------------------------------------------------------- apex --

        private void Apex(float t, float dt)
        {
            float px = Px;
            Pose.Ground = _from;
            Pose.Height = _apex + 0.2f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - TApex) / (TRain - TApex)));
            if (t < TPose)
            {
                // one full spin, tucked, smeared
                float fk = Mathf.Clamp01((t - TApex) / (TPose - TApex));
                Pose.Rot = 4f + 356f * Anim.InOutCubic(fk);
                float tuck = Mathf.Sin(Mathf.PI * fk);
                Pose.SX = 1f - 0.08f * tuck;
                Pose.SY = 1f - 0.2f * tuck;
                if (Visible)
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.03f;
                        Ghost(0.13f, new Color(1f, 0.96f, 0.8f, 0.6f), Fx.WithA(Gold, 0.4f), Fx.WithA(Gold, 0f));
                    }
                }
                return;
            }
            if (Beat(BPose))
            {
                Raise(px);
            }
            // the arm raised: stretched 120% tall
            float s = Anim.OutCubic(Mathf.Clamp01((t - TPose) / 0.08f));
            Pose.Rot = 0f;
            Pose.SX = Mathf.Lerp(1f, 0.9f, s);
            Pose.SY = Mathf.Lerp(1f, 1.2f, s);
        }

        /// <summary>The ting at the raised hand, the gold halo blooming behind, and the sun it keeps.</summary>
        private void Raise(float px)
        {
            if (!Visible)
            {
                return;
            }
            int mark = GodFx.Mark();
            Vector2 hand = HandPos();
            Particle g = Fx.Glint(hand, px, 0.32f, GoldLight, 1.3f);
            if (g != null)
            {
                g.Step = (q, d) => q.Pos = HandPos();
            }
            Vector2 body = BodyPos();
            Particle halo = Kit.Flash(body, px, Rx(1.5f), 0.55f, Gold, Back, 1);
            halo?.Sized(0.3f, 1.1f).Color(Fx.WithA(GoldLight, 0.95f), Fx.WithA(Gold, 0.7f), Fx.WithA(Gold, 0f));
            Fx.Ring(body, px, 0.45f, 4f, Tex(1.9f), 2, 1, Back, 2, 1f, 2.4f,
                Fx.WithA(GoldLight, 1f), Fx.WithA(Gold, 0.6f), Fx.WithA(Gold, 0f));
            if (_full)
            {
                GodFx.Lift(mark);
            }
            _sun = Fx.Spawn(Art.Disk(Rx(0.8f), 1f, 0.55f), body, px, 1f, Back, 0);
            if (_sun != null)
            {
                _sun.Held = true;
                _sun.Cine = _full;
                _sun.Grad = null;
                _sun.Step = (q, d) =>
                {
                    q.Pos = BodyPos();
                    q.Grad = null;
                    q.R.color = Fx.WithA(Gold, Mathf.Min(1f, q.Age * 4f) * (0.32f + 0.08f * Mathf.Sin(q.Age * 9f)));
                };
                _sun.R.color = Fx.WithA(Gold, 0f);
            }
        }

        // --------------------------------------------------------------- rain --

        private void Rain(float t, float dt)
        {
            if (Beat(BRain))
            {
                BeginRain();
            }
            // it hangs in the sky, arm raised, drifting toward where it will dive
            float k = Mathf.Clamp01((t - TRain) / (TDive - TRain));
            Pose.Ground = Vector2.Lerp(_from, _drift, Anim.InOutSine(k));
            // a slow bob (it starts at the apex height, so there is no pop)
            Pose.Height = _apex + 0.2f * _k * Mathf.Sin((t - TRain) * 4f);
            _hoverH = Pose.Height;
            Pose.Rot = 2f * Mathf.Sin((t - TRain) * 3f);
            Pose.SX = 0.9f;
            Pose.SY = 1.2f + 0.02f * Mathf.Sin(t * 9f);
            if (Visible)
            {
                _glint -= dt;
                if (_glint <= 0f)
                {
                    _glint = 0.4f;
                    Particle g = Fx.Glint(HandPos(), Px, 0.22f, GoldLight, 0.8f);
                    if (_full)
                    {
                        GodFx.Lift(g);
                    }
                }
            }
        }

        /// <summary>1.30: the world wakes at quarter speed and the stars are assigned to the clusters.</summary>
        private void BeginRain()
        {
            if (_full)
            {
                Cinema.Unhold(_cast);
                Cinema.SlowMo(0.25f, TImpact - TRain);
            }
            PlanRain();
            Anim.Sound("event:/SFX/DESTRUCTION/FallMeteorite", A.current_tile);
            Dev.Watch(Vector2.Lerp(_from, _rainAt, 0.5f) + new Vector2(0f, 3f));
            if (Dev.On)
            {
                int aimed = 0;
                foreach (Meteor m in _meteors)
                {
                    if (m.Victim != null)
                    {
                        aimed++;
                    }
                }
                Dev.Trace("starfall " + Id + " rain: " + _meteors.Count + " stars (" + aimed + " aimed) on " + _foes.Count + " foes (" + _kept.Count
                    + " kept for the sun), field " + (_rainR * 2f).ToString("F0") + " tiles across, apex " + _apex.ToString("F1") + ", dive "
                    + Vector2.Distance(_from, _dive).ToString("F1") + " tiles away");
            }
        }

        private void PlanRain()
        {
            GatherField(false);
            // where the Sun Meteor will fall is settled first: the stars that land there break the
            // crowd without finishing it, so the God's own dive always has someone to end
            _dive = DiveSpot(false);
            _sunAt = _dive;
            _sunSet = _foes.Count > 0;
            _kept.Clear();
            if (_sunSet)
            {
                float r = DiveRadius * _k;
                foreach (Actor u in _foes)
                {
                    if ((u.current_position - _sunAt).sqrMagnitude <= r * r)
                    {
                        _kept.Add(u.Sid());
                    }
                }
            }
            RainArea();
            int n = StarCount(_foes.Count);
            List<Vector2> pts = new List<Vector2>(n);
            List<Actor> vics = new List<Actor>(n);
            PlanStrikes(_foes, _allies, n, MeteorRadius * _k, pts, vics, out _);
            // the sky does not run out of stars because the ground ran out of enemies: the rest
            // fall evenly over the whole battlefield (a sunflower spread over the rain's disc)
            // the sky falls on the land: a star over open water is a wasted star (a splash, no
            // crater), so a point over water is tried again elsewhere, and only now and then kept
            // (a few columns of spray off a coast)
            int fill = n - pts.Count;
            float phase = Random.value * Mathf.PI * 2f;
            const int tries = 7;
            for (int i = 0; i < fill; i++)
            {
                for (int k = 0; k < tries; k++)
                {
                    float u = Mathf.Clamp01((i + 0.5f + Random.Range(-0.35f, 0.35f)) / fill);
                    float ang = phase + i * 2.39996f + k * 0.9f;
                    float rr = k < 4 ? Mathf.Sqrt(u) : Random.value;
                    Vector2 p = _rainAt + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (_rainR * rr) + Random.insideUnitCircle * 0.8f;
                    p = Combat.Clamp(p);
                    if (NearAlly(p, StarCrater + 0.5f) || NearPoint(pts, p, 1.4f))
                    {
                        continue;
                    }
                    if (Wet(p) && (k < tries - 1 || Random.value > 0.2f))
                    {
                        continue;
                    }
                    pts.Add(p);
                    vics.Add(null);
                    break;
                }
            }
            // a drum roll, not a sweep: shuffled
            for (int i = pts.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Vector2 tp = pts[i];
                pts[i] = pts[j];
                pts[j] = tp;
                Actor tv = vics[i];
                vics[i] = vics[j];
                vics[j] = tv;
            }
            int count = pts.Count;
            float spacing = count > 1 ? Mathf.Clamp((TLast - TFirst) / (count - 1), 0.028f, 0.16f) : 0f;
            float now = T;
            for (int i = 0; i < count; i++)
            {
                Actor v = vics[i];
                float at = TFirst + i * spacing;
                _meteors.Add(new Meteor
                {
                    Land = pts[i],
                    At = at,
                    From = Mathf.Min(at, Mathf.Max(at - Flight, now)),
                    Victim = v,
                    VictimId = v != null ? v.Sid() : -1,
                    Big = i % 3 == 0
                });
            }
            _drift = Vector2.Lerp(_from, _dive, 0.6f);
        }

        /// <summary>
        /// Is this one the Sun's (it stood in the Sun's zone when the rain began, or stands there
        /// now)? The stars may break it but never finish it, wherever they throw it.
        /// </summary>
        private bool Kept(Actor u)
        {
            if (!_sunSet)
            {
                return false;
            }
            if (_kept.Contains(u.Sid()))
            {
                return true;
            }
            float r = DiveRadius * _k;
            return (u.current_position - _sunAt).sqrMagnitude <= r * r;
        }

        /// <summary>
        /// The battlefield: enemies within the God's reach a falling star can strike (on the ground
        /// unless 'air', not in a boat or a building, not in a god ability of their own; one mid-
        /// technique counts), and our own people a little further out (no star, no flame by them).
        /// </summary>
        private void GatherField(bool air)
        {
            float reach = Godhood.Reach;
            Godhood.Gather(A, _from, reach + 8f, _foes, _allies, false);
            float r2 = reach * reach;
            for (int i = _foes.Count - 1; i >= 0; i--)
            {
                Actor u = _foes[i];
                bool ground = air ? !u.isFlying() : Combat.Grounded(u);
                if ((u.current_position - _from).sqrMagnitude > r2 || !ground || u.asset.is_boat || u.isInsideSomething() || Godhood.InGodMove(u))
                {
                    _foes.RemoveAt(i);
                }
            }
        }

        /// <summary>The rain's disc: around the army (a quarter of the way back to the God), wide enough for all of it.</summary>
        private void RainArea()
        {
            if (_foes.Count == 0)
            {
                float fwd = Pose.FaceRight ? 1f : -1f;
                _rainAt = Combat.Clamp(_from + new Vector2(4f * fwd, 0f));
                _rainR = 10f;
                return;
            }
            Vector2 sum = Vector2.zero;
            foreach (Actor u in _foes)
            {
                sum += u.current_position;
            }
            Vector2 c = Vector2.Lerp(sum / _foes.Count, _from, 0.25f);
            float far = 0f;
            foreach (Actor u in _foes)
            {
                far = Mathf.Max(far, Vector2.Distance(u.current_position, c));
            }
            _rainAt = c;
            _rainR = Mathf.Clamp(far + 2f, 10f, 14f);
        }

        /// <summary>Open water (or lava) under this point: a star there makes no crater.</summary>
        private static bool Wet(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t == null || t.Type == null || t.Type.liquid || t.Type.lava;
        }

        /// <summary>A Grandmaster or God: the God's equal, worth an army to its sky.</summary>
        private static bool Rival(Actor u)
        {
            return u != null && Tiers.Of(u) >= Tier.Grandmaster;
        }

        private static bool NearPoint(List<Vector2> pts, Vector2 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < pts.Count; i++)
            {
                if ((pts[i] - p).sqrMagnitude < r2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Ground the God can land on: on the map, not a wall or mountain, lava or water. Fire does
        /// not matter: its own landing puts the fire out (GodRuin.Calm).
        /// </summary>
        private static bool LandOk(Vector2 p)
        {
            if (p.x < 1f || p.y < 1f || p.x >= MapBox.width - 1 || p.y >= MapBox.height - 1)
            {
                return false;
            }
            WorldTile t = Combat.TileAt(p);
            return t != null && t.Type != null && !t.Type.block && !t.Type.lava && !t.Type.liquid;
        }

        private static bool FindLand(Vector2 want, float radius, out Vector2 spot)
        {
            want = Combat.Clamp(want);
            spot = want;
            if (LandOk(want))
            {
                return true;
            }
            int r = Mathf.CeilToInt(radius);
            for (int ring = 1; ring <= r; ring++)
            {
                for (int i = 0; i < ring * 8; i++)
                {
                    float a = i / (ring * 8f) * Mathf.PI * 2f;
                    Vector2 p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                    if (LandOk(p))
                    {
                        spot = p;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool NearAlly(Vector2 p, float r)
        {
            float r2 = r * r;
            foreach (Actor u in _allies)
            {
                if (u != null && u.isAlive() && (u.current_position - p).sqrMagnitude <= r2)
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly int[] _assigned = new int[64];
        private static readonly int[] _picked = new int[64];
        private static readonly bool[] _blocked = new bool[64];
        private static readonly float[] _value = new float[64];
        private static readonly bool[] _rival = new bool[64];

        /// <summary>
        /// Where n stars should fall: greedy cover of the enemy clusters (each pick is the enemy
        /// whose surroundings are worth the most and have been struck the least; at most three
        /// stars aimed at one foe), never where one of our own stands. alliesIn: allies inside the
        /// chosen strike zones.
        /// </summary>
        internal static void PlanStrikes(List<Actor> foes, List<Actor> allies, int n, float r, List<Vector2> points, List<Actor> victims, out int alliesIn)
        {
            points.Clear();
            victims?.Clear();
            alliesIn = 0;
            int m = Mathf.Min(foes.Count, 64);
            if (m == 0 || n <= 0)
            {
                return;
            }
            float r2 = r * r;
            float block2 = r2 * 0.5f;
            for (int i = 0; i < m; i++)
            {
                _assigned[i] = 0;
                _picked[i] = 0;
                // a Grandmaster or God is worth an army to the sky (it hunts the God's equal)
                _rival[i] = Rival(foes[i]);
                _value[i] = Mathf.Max(0.3f, Godhood.CrowdPoints(Weights.Of(foes[i]))) + (_rival[i] ? RivalValue : 0f);
                Vector2 c = foes[i].current_position;
                bool blocked = false;
                if (allies != null)
                {
                    for (int j = 0; j < allies.Count; j++)
                    {
                        Actor u = allies[j];
                        if (u != null && (u.current_position - c).sqrMagnitude <= block2)
                        {
                            blocked = true;
                            break;
                        }
                    }
                }
                _blocked[i] = blocked;
            }
            for (int s = 0; s < n; s++)
            {
                int best = -1;
                float bestScore = 0.05f;
                for (int i = 0; i < m; i++)
                {
                    if (_blocked[i] || _picked[i] >= (_rival[i] ? StarsPerRival : StarsPerFoe))
                    {
                        continue;
                    }
                    Vector2 c = foes[i].current_position;
                    float score = 0f;
                    for (int j = 0; j < m; j++)
                    {
                        if ((foes[j].current_position - c).sqrMagnitude <= r2)
                        {
                            score += _value[j] / (1f + 1.5f * _assigned[j]);
                        }
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                if (best < 0)
                {
                    break;
                }
                _picked[best]++;
                Vector2 at = foes[best].current_position;
                for (int j = 0; j < m; j++)
                {
                    if ((foes[j].current_position - at).sqrMagnitude <= r2)
                    {
                        _assigned[j]++;
                    }
                }
                // a second star on the same spot lands a little apart, so the craters don't stack
                for (int q = 0; q < points.Count; q++)
                {
                    if ((points[q] - at).sqrMagnitude < 0.09f)
                    {
                        at += Random.insideUnitCircle * 0.45f * r;
                        break;
                    }
                }
                points.Add(Combat.Clamp(at));
                victims?.Add(foes[best]);
            }
            if (allies == null)
            {
                return;
            }
            foreach (Actor u in allies)
            {
                if (u == null)
                {
                    continue;
                }
                for (int q = 0; q < points.Count; q++)
                {
                    if ((points[q] - u.current_position).sqrMagnitude <= r2)
                    {
                        alliesIn++;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The densest knot of enemies within reach, on solid ground, away from our own. 'regather':
        /// look again now, bodies the stars knocked into the air included (they are where they will
        /// come down), keeping a small preference for the Sun's zone settled at the rain.
        /// </summary>
        private Vector2 DiveSpot(bool regather)
        {
            if (regather)
            {
                GatherField(true);
            }
            Vector2 best = _dive;
            float bestScore = 0f;
            float r = DiveRadius * _k * 0.75f;
            float r2 = r * r;
            int m = Mathf.Min(_foes.Count, 64);
            for (int i = 0; i < m; i++)
            {
                // the God's equal draws the Sun as an army would (God against God)
                _value[i] = Mathf.Max(0.3f, Godhood.CrowdPoints(Weights.Of(_foes[i]))) + (Rival(_foes[i]) ? RivalValue : 0f);
            }
            for (int i = 0; i < m; i++)
            {
                Vector2 c = _foes[i].current_position;
                float score = 0f;
                for (int j = 0; j < m; j++)
                {
                    float d2 = (_foes[j].current_position - c).sqrMagnitude;
                    if (d2 <= r2)
                    {
                        score += _value[j] * (1f - 0.5f * Mathf.Sqrt(d2) / r);
                    }
                }
                if (regather && _sunSet)
                {
                    // the ones the stars broke for it
                    if (_kept.Contains(_foes[i].Sid()))
                    {
                        score += 4f;
                    }
                    if ((c - _sunAt).sqrMagnitude <= r2)
                    {
                        score += 2f;
                    }
                }
                foreach (Actor u in _allies)
                {
                    if (u != null && (u.current_position - c).sqrMagnitude <= 2.25f)
                    {
                        score -= 2f;
                    }
                }
                // a little nearer first (the God dives across the field, not off it)
                score /= 1f + Vector2.Distance(c, _from) / 30f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            // (burning ground is fine: the Sun's landing puts its own footing out)
            if (FindLand(best, 2.5f, out Vector2 spot))
            {
                return Combat.Clamp(spot);
            }
            if (FindLand(_from, 3f, out spot))
            {
                return Combat.Clamp(spot);
            }
            return _from;
        }

        // ------------------------------------------------------------- stars --

        private void Meteors(float t, float dt)
        {
            for (int i = 0; i < _meteors.Count; i++)
            {
                Meteor m = _meteors[i];
                if (m.Done)
                {
                    continue;
                }
                float start = m.From;
                if (t < start)
                {
                    continue;
                }
                if (!m.Spawned)
                {
                    m.Spawned = true;
                    Aim(m, true, dt);
                    m.Fall = FallLength(m.Land);
                    if (Kit.Visible(m.Land, 0.6f))
                    {
                        Show(m);
                    }
                }
                else
                {
                    Aim(m, false, dt);
                }
                if (m.Shown)
                {
                    Draw(m, Mathf.Clamp01((t - start) / Mathf.Max(0.01f, m.At - start)), dt);
                }
                if (t >= m.At)
                {
                    m.Done = true;
                    Hide(m);
                    Strike(m, i);
                }
            }
        }

        /// <summary>A star follows its enemy (a re-aim at launch, then a gentle track while it falls).</summary>
        private void Aim(Meteor m, bool first, float dt)
        {
            Actor v = m.Victim;
            // (Living: one an earlier star just killed is still "alive" until the game's death check)
            bool alive = Combat.Living(v) && v.Sid() == m.VictimId && Combat.Grounded(v) && !v.isInMagnet();
            if (!alive)
            {
                if (!first || v == null)
                {
                    return;
                }
                // its enemy fell to an earlier star: the nearest one still standing close by
                m.Victim = null;
                float best = 4f;
                foreach (Actor u in _foes)
                {
                    if (!Combat.Living(u) || !Combat.Grounded(u) || !Combat.IsFoe(A, u))
                    {
                        continue;
                    }
                    float d2 = (u.current_position - m.Land).sqrMagnitude;
                    if (d2 < best)
                    {
                        best = d2;
                        m.Victim = u;
                    }
                }
                m.VictimId = m.Victim != null ? m.Victim.Sid() : -1;
                if (m.Victim == null)
                {
                    return;
                }
                v = m.Victim;
            }
            Vector2 at = v.current_position;
            if (first)
            {
                if ((at - m.Land).sqrMagnitude <= 2.5f * 2.5f)
                {
                    m.Land = at;
                }
            }
            else
            {
                // a star hunting the God's equal bends hard after it (an equal is fast)
                m.Land = Vector2.MoveTowards(m.Land, at, (Rival(v) ? 9f : 4f) * _k * dt);
            }
        }

        /// <summary>How far a star falls: from just above the top of the view, along the diagonal.</summary>
        private float FallLength(Vector2 land)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null)
            {
                return 24f;
            }
            float top = cam.transform.position.y + cam.orthographicSize + 2f;
            return Mathf.Clamp((top - land.y) / 0.866f, 6f, 70f);
        }

        private void Show(Meteor m)
        {
            FxLayer layer = Hi;
            float rot = Mathf.Atan2(_fall.y, _fall.x) * Mathf.Rad2Deg - 90f;
            m.Halo = Held(Art.Disk(2, 1f, 0.5f), layer, 6, Fx.WithA(Kit.Fire1, 0.75f));
            m.Head = Held(GodFx.MeteorHead, layer, 8, Hot);
            m.Trail0 = Streak(layer, 3, rot, 3f, 1.5f, Fx.WithA(Kit.Fire2, 0.55f));
            m.Trail1 = Streak(layer, 4, rot, 2f, 1.15f, Fx.WithA(Kit.Fire1, 0.8f));
            m.Trail2 = Streak(layer, 5, rot, 1f, 0.8f, Fx.WithA(Hot, 0.95f));
            m.Shown = true;
            m.Frame = Random.Range(0, 4);
            Draw(m, 0f, 0f);
        }

        private Particle Held(Sprite s, FxLayer layer, int order, Color c)
        {
            Particle p = Fx.Spawn(s, Vector2.zero, Px, 1f, layer, order);
            if (p == null)
            {
                return null;
            }
            p.Held = true;
            p.Cine = _full;
            p.Color(c);
            return p;
        }

        private Particle Streak(FxLayer layer, int order, float rot, float wide, float longer, Color c)
        {
            Particle p = Held(Art.Lines[2], layer, order, c);
            if (p != null)
            {
                p.Rot = rot;
                p.SX = wide;
                p.SY = longer;
            }
            return p;
        }

        /// <summary>A star in flight at u (0 top of the view .. 1 the ground): head, trail, flames, smoke.</summary>
        private void Draw(Meteor m, float u, float dt)
        {
            float px = Px;
            Vector2 head = m.Land - _fall * (m.Fall * (1f - u));
            Place(m.Head, head);
            Place(m.Halo, head);
            Place(m.Trail0, head - _fall * (18f * px * 1.5f * 0.5f - px));
            Place(m.Trail1, head - _fall * (18f * px * 1.15f * 0.5f - px));
            Place(m.Trail2, head - _fall * (18f * px * 0.8f * 0.5f - px));
            if (dt <= 0f)
            {
                return;
            }
            m.Frame++;
            if ((m.Frame & 1) == 0)
            {
                Particle f = Fx.Spawn(Art.Puffs[Random.Range(0, 2)], head - _fall * Random.Range(1f, 4f) * px + Random.insideUnitCircle * px,
                    px, Random.Range(0.1f, 0.15f), Hi, 2);
                f?.Moving(Random.insideUnitCircle * 6f * px).Sized(0.6f, 0.4f)
                    .Color(Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Kit.Fire2, 0.8f), Fx.WithA(Kit.Fire3, 0f));
            }
            if (m.Big && m.Frame % 5 == 0)
            {
                // smoke hangs in the slowed world (it is not the God's: it plays at world speed, under the veil)
                Particle s = Fx.Spawn(Art.Dust[Random.Range(2, 4)], head - _fall * 4f * px + Random.insideUnitCircle * 1.5f * px,
                    px, Random.Range(0.4f, 0.55f), FxLayer.Top, 0);
                s?.Moving(Random.insideUnitCircle * 3f * px, 4f * px, 1.5f).Sized(0.6f, 1.15f)
                    .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.32f), Fx.WithA(Smoke, 0f));
            }
        }

        private static void Place(Particle p, Vector2 at)
        {
            if (p != null && p.Alive)
            {
                p.Pos = at;
            }
        }

        private static void Hide(Meteor m)
        {
            m.Head?.Kill();
            m.Halo?.Kill();
            m.Trail0?.Kill();
            m.Trail1?.Kill();
            m.Trail2?.Kill();
            m.Head = m.Halo = m.Trail0 = m.Trail1 = m.Trail2 = null;
        }

        /// <summary>A star lands: its burning crater in the ground and its damage.</summary>
        private void Strike(Meteor m, int index)
        {
            Vector2 at = m.Land;
            WorldTile tile = Combat.TileAt(at);
            if (Kit.Visible(at))
            {
                StarFx(m, at, tile);
            }
            if (index % 3 == 0)
            {
                Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile);
            }
            // the ground takes it on the impact frame: a burning crater (blasted without flames
            // where our own people stand)
            // (a god war: the big ones tear a wider bowl)
            GodRuin.Crater(A, at, m.Big && _war ? StarCrater * WarRuin : StarCrater, 3, _allies);
            float radius = MeteorRadius * _k;
            // the sky falls on everyone below it, mid-technique or not (they just can't be thrown)
            List<Actor> hit = Combat.FoesAround(A, at, radius, false, true);
            foreach (Actor u in hit)
            {
                // (no blow on one an earlier star just killed: it is still "alive" until the death check)
                if (!u.hasHealth() || u.asset.is_boat || u.isInsideSomething() || Godhood.InGodMove(u))
                {
                    continue;
                }
                // the Sun's own: a star breaks but never finishes them, the God's dive ends them
                bool kept = Kept(u);
                if (!Combat.Allowed(A, u, out float cm))
                {
                    // heavier than its class: a glancing blow, no launch
                    Blow(u, 0.5f, !kept, 0f);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                if (!Reached(u))
                {
                    continue;
                }
                float e = Mathf.Clamp01(Vector2.Distance(u.current_position, at) / radius);
                float f = Mathf.Lerp(1f, 0.5f, e) * cm;
                Blow(u, 1.8f * f, !kept, 70f * f);
                // (a flow strike is no shield: the throw takes it over; a technique is)
                if (u.isAlive() && u.hasHealth() && !Moves.Locked(u))
                {
                    float spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(480f, 760f);
                    if (kept)
                    {
                        // knocked flat where it stands, still inside the Sun's reach
                        Combat.Launch(u, at, 0.35f * _k, 0.5f * _k, spin * 0.6f);
                    }
                    else
                    {
                        Combat.Launch(u, at, 1.5f * _k, 0.9f * _k, spin);
                    }
                    GodRuin.Shelter(u, at, (kept ? 0.35f : 1.5f) * _k);
                }
            }
        }

        /// <summary>
        /// One blow of the rain or the Sun. A Combat God caught in it loses at most GodShare of its
        /// life to one Starfall (an equal is shaken, never erased in one breath).
        /// </summary>
        private HitResult Blow(Actor u, float mult, bool lethal, float flat)
        {
            if (Tiers.Of(u) < Tier.God)
            {
                return Combat.Hit(A, u, mult, lethal, true, flat, this);
            }
            long id = u.Sid();
            _dealt.TryGetValue(id, out float done);
            float room = GodShare * Mathf.Max(1, u.getMaxHealth()) - done;
            if (room < 1f)
            {
                if (u.is_visible)
                {
                    u.startShake(0.2f, 0.1f, true, false);   // it still feels it
                }
                return default;
            }
            float through = Mathf.Max(0.05f, 1f - Mathf.Min(u.stats["armor"], 95f) / 100f);
            float expect = (Mathf.Max(4f, A.stats["damage"]) * mult + flat) * Fighters.DamageMult(A) * GodRuin.Might(A) * through;
            if (expect > room)
            {
                float s = room / expect;
                mult *= s;
                flat *= s;
            }
            if (u.getHealth() > room)
            {
                lethal = false;   // (a crit can't carry it past its share either)
            }
            HitResult r = Combat.Hit(A, u, mult, lethal, true, flat, this);
            if (r.Landed)
            {
                _dealt[id] = done + r.Dealt;
            }
            return r;
        }

        private void StarFx(Meteor m, Vector2 at, WorldTile tile)
        {
            float px = Px;
            Kit.Flash(at + new Vector2(0f, 2f * px), px, Rx(0.55f), 0.08f, Hot, Hi, 30);
            // (the ground itself is cratered and burning now: a glowing decal only on the big ones and the hits)
            if (m.Big || m.Victim != null)
            {
                Kit.Crater(at, px * 1.05f, tile, true, 9f, true, Gold);
            }
            Kit.Shockwave(at, px, 2f, Tex(MeteorRadius), 0.36f, Fx.WithA(GoldLight, 0.9f), m.Big ? 2 : 1);
            Kit.DustRing(at, px, tile, N(m.Big ? 5 : 3), 55f, 0.6f, _k);
            if (m.Big)
            {
                Kit.Debris(at, px, tile, N(3), _k);
                Kit.Shake(at, 0.14f, 0.004f, _k);
            }
            int embers = N(m.Big ? 2 : 1);
            for (int i = 0; i < embers; i++)
            {
                Kit.Ember(at + Random.insideUnitCircle * 3f * px, px, Random.Range(0.6f, 1.1f),
                    new Vector2(Random.Range(-8f, 8f), Random.Range(14f, 30f)) * px, Gold);
            }
        }

        /// <summary>
        /// Before its first blow on a unit: may it answer (a mirage, a parry)? A blow that lands is
        /// remembered for the whole move; an answer only for AnswerWindow: it dodged that star, not
        /// the sky (the next star, or the Sun, asks again, and a spent reaction can't answer twice).
        /// </summary>
        private bool Reached(Actor u)
        {
            long id = u.Sid();
            if (_reached.Contains(id))
            {
                return true;
            }
            if (_answered.TryGetValue(id, out float until))
            {
                if (T < until)
                {
                    return false;
                }
                _answered.Remove(id);
            }
            if (Combat.Reach(this, u, true) != Contact.Hit)
            {
                _answered[id] = T + AnswerWindow;
                return false;
            }
            _reached.Add(id);
            return true;
        }

        // --------------------------------------------------------------- dive --

        private void Dive(float t, float dt)
        {
            float px = Px;
            if (Beat(BRain))
            {
                BeginRain();
            }
            if (Beat(BDive))
            {
                BeginDive(px);
            }
            if (t < TDrop)
            {
                // the wind-up: a tuck, a little lift
                float c = Anim.OutQuad(Mathf.Clamp01((t - TDive) / (TDrop - TDive)));
                Pose.Ground = _drift;
                Pose.Height = _hoverH + 0.25f * _k * c;
                Pose.Rot = -22f * c;
                Pose.SX = Mathf.Lerp(0.9f, 1.08f, c);
                Pose.SY = Mathf.Lerp(1.2f, 0.84f, c);
                return;
            }
            if (Beat(BDrop))
            {
                _diveFrom = Pose.Ground;
                _diveH = Pose.Height;
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible)
                {
                    // the sound barrier
                    Particle boom = Fx.Ring(BodyPos(), px, 0.22f, 5f, 24f * _k, 2, 1, Hi, 3, 0.35f, 2.5f,
                        new Color(1f, 1f, 1f, 0.95f), Fx.WithA(GoldLight, 0f));
                    if (boom != null)
                    {
                        boom.Cine = _full;
                    }
                }
                _lastTrail = BodyPos();
                _trail = true;
            }
            float k = Mathf.Clamp01((t - TDrop) / (TImpact - TDrop));
            float e = k * k;
            Pose.Height = _diveH * (1f - e);
            Pose.Ground = Vector2.Lerp(_diveFrom, _dive, e);
            float s = Mathf.Clamp01(k * 2.5f);
            Pose.SX = Mathf.Lerp(0.85f, 0.62f, s);
            Pose.SY = Mathf.Lerp(1.3f, 1.66f, s);
            float horiz = Mathf.Abs(_dive.x - _diveFrom.x);
            Pose.Rot = Mathf.Clamp(Mathf.Atan2(horiz, Mathf.Max(0.5f, _diveH)) * Mathf.Rad2Deg * 0.8f, 6f, 35f);
            if (Visible)
            {
                SunTrail(px * 1.5f);
                Ghost(0.12f, new Color(1f, 0.95f, 0.7f, 0.85f), Fx.WithA(Sun2, 0.6f), Fx.WithA(Sun3, 0f));
                _lines -= dt;
                if (_lines <= 0f)
                {
                    _lines = 0.025f;
                    Vector2 body = BodyPos();
                    Vector2 vel = body - _lastTrail;
                    if (vel.sqrMagnitude > 1e-6f)
                    {
                        Kit.SpeedLines(body - vel.normalized * 6f * px, vel, px, 2, 6f, 0.1f, Fx.WithA(GoldLight, 0.8f), Hi);
                    }
                }
            }
            _lastTrail = BodyPos();
        }

        /// <summary>2.90: the densest cluster now, marked; the sun collapses into the God.</summary>
        private void BeginDive(float px)
        {
            _dive = DiveSpot(true);
            if (Mathf.Abs(_dive.x - Pose.Ground.x) > 0.3f)
            {
                Pose.FaceRight = _dive.x > Pose.Ground.x;
            }
            if (_sun != null && Visible)
            {
                int mark = GodFx.Mark();
                Particle f = Kit.Flash(BodyPos(), px, Rx(1f), 0.16f, GoldLight, Back, 1);
                f?.Sized(1.1f, 0.2f);
                if (_full)
                {
                    GodFx.Lift(mark);
                }
            }
            _sun?.Kill();
            _sun = null;
            Marker(px);
            Dev.Watch(_dive + new Vector2(0f, 2f));
        }

        /// <summary>Where it will land: a shadow growing on the ground and a dashed gold ring closing on it.</summary>
        private void Marker(float px)
        {
            float total = TImpact - TDive;
            Vector2 at = _dive;
            _marker = Fx.Spawn(Art.Disk(2, 0.42f, 0.5f), at, px, total, FxLayer.Ground, 2);
            if (_marker != null)
            {
                _marker.Held = true;
                _marker.Snap = true;
                _marker.Cine = _full;
                int rx = ShadowRx(1.6f);
                _marker.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Disk(Mathf.RoundToInt(Mathf.Lerp(2f, rx, k * k)), 0.42f, 0.5f);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.1f, 0.42f, k));
                };
            }
            _markerRing = Fx.Spawn(Art.Ring(24, 1, Art.Ground, 12), at, px, total, _full ? FxLayer.Cine : FxLayer.Ground, 3);
            if (_markerRing != null)
            {
                _markerRing.Held = true;
                _markerRing.Snap = true;
                float r0 = Tex(DiveRadius * 0.9f), r1 = 7f * _k;
                _markerRing.Step = (q, d) =>
                {
                    float k = Mathf.Clamp01(q.Age / total);
                    Sprite s = Art.Ring(Mathf.RoundToInt(Mathf.Lerp(r0, r1, Anim.InOutCubic(k))), 1, Art.Ground, 12);
                    if ((object)q.R.sprite != s)
                    {
                        q.R.sprite = s;
                        q.Frames[0] = s;
                    }
                    q.Grad = null;
                    q.R.color = Fx.WithA(Gold, Mathf.Lerp(0f, 0.85f, Mathf.Clamp01(k * 3f)));
                };
            }
        }

        /// <summary>Gold-white fire along the dive, evenly spaced so it reads as one stream (1.5x scale).</summary>
        private void SunTrail(float px)
        {
            Vector2 now = BodyPos();
            if (!_trail)
            {
                return;
            }
            float step = 1.6f * px / Fx.Detail();
            int n = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(_lastTrail, now) / step), 1, 30);
            FxLayer layer = Hi;
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_lastTrail, now, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle core = Fx.Spawn(Art.Puffs[0], at, px, 0.09f, layer, 3);
                core?.Sized(0.7f, 0.35f).Color(Hot, Fx.WithA(Sun1, 0.7f));
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.15f, 0.22f), layer, 2);
                flame?.Moving(Random.insideUnitCircle * 5f * px, 6f * px, 2f).Sized(0.75f, 0.45f)
                    .Color(Fx.WithA(Sun1, 0.95f), Fx.WithA(Sun2, 0.85f), Fx.WithA(Sun3, 0.45f), Fx.WithA(Sun3, 0f));
                if (Random.value < 0.15f)
                {
                    Particle smoke = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at + Random.insideUnitCircle * 1.5f * px, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0);
                    smoke?.Moving(Random.insideUnitCircle * 4f * px, 8f * px, 1.5f).Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.3f), Fx.WithA(Smoke, 0f));
                    if (smoke != null)
                    {
                        smoke.Delay = 0.06f;
                    }
                }
            }
        }

        // ----------------------------------------------------- impact & rise --

        private void Land(float t)
        {
            if (Beat(BRain))
            {
                BeginRain();
            }
            if (Beat(BDive))
            {
                BeginDive(Px);
            }
            if (Beat(BImpact))
            {
                Impact();
            }
            Pose.Ground = _dive;
            Pose.Height = 0f;
            Pose.HideShadow = false;
            if (_full && t >= TImpact + 0.07f && Beat(BClear))
            {
                // the white flash fades and takes the red sky with it: the impact in its own colours
                Cinema.Undim(0.45f);
            }
            if (t < TRise)
            {
                // the superhero landing, held through the hit-stop
                Pose.SX = 1.34f;
                Pose.SY = 0.6f;
                Pose.Rot = 14f;
                Pose.Nudge = t - TImpact < 0.12f ? new Vector2(0f, ((int)(t * 60f) & 1) == 0 ? 0.5f : 0f) : Vector2.zero;
            }
            else
            {
                float r = t - TRise;
                float amp = Anim.Spring(r, 6f, 14f);
                Pose.SX = 1f + 0.34f * amp;
                Pose.SY = 1f - 0.4f * amp;
                Pose.Rot = 14f * Mathf.Exp(-7f * r);
                Pose.Nudge = Vector2.zero;
                if (Beat(BHop) && !LandOk(_dive) && FindLand(_dive, 3f, out Vector2 spot))
                {
                    // the crater drowned (or walled in) its footing: it springs out to firm ground
                    _hopTo = spot;
                    _hop = true;
                    Pose.FaceRight = Mathf.Abs(spot.x - _dive.x) > 0.05f ? spot.x > _dive.x : Pose.FaceRight;
                }
                if (_hop)
                {
                    float h = Mathf.Clamp01(r / THop);
                    Pose.Ground = Vector2.Lerp(_dive, _hopTo, Anim.InOutSine(h));
                    Pose.Height = 0.4f * _k * Mathf.Sin(Mathf.PI * h);
                    Pose.HideShadow = h > 0f && h < 1f;
                    if (h >= 1f && Beat(BHopLand) && Visible)
                    {
                        Fx.LandingPuff(_hopTo, Px, Combat.TileAt(_hopTo));
                    }
                }
            }
            if (t >= TFly && Beat(BFly))
            {
                FlyVictims();
            }
            if (t >= TEmber && Beat(BEmber) && Visible)
            {
                // one gold ember drifts up: it is over
                float px = Px;
                Particle e = Fx.Spawn(Art.Pixel, BodyPos() + new Vector2(0f, 2f * px), px, 1.6f, FxLayer.Top, 9);
                e?.Moving(new Vector2(Random.Range(-2f, 2f), 9f) * px, 0f, 0.4f).Color(GoldLight, Gold, Fx.WithA(Gold, 0f));
                Fx.Glint(HeadPos() + new Vector2((Pose.FaceRight ? 2f : -2f) * px, 1f * px), px, 0.24f, GoldLight, 0.9f);
            }
        }

        /// <summary>3.40: the Sun Meteor lands.</summary>
        private void Impact()
        {
            Vector2 at = _dive;
            WorldTile tile = Combat.TileAt(at);
            KillHeld();
            Pose.Ground = at;
            Pose.Height = 0f;
            bool crit = HurtDive(at);
            if (_full)
            {
                // the turned sky flashes white for a few frames
                Cinema.Dim(0.5f, SkyFlash, 0f);
            }
            if (Kit.Visible(at))
            {
                SunImpact(at, tile, crit);
            }
            // the land reshaped: a deep burning crater (no flames where our own people stand), and
            // its heart, where the God and the broken stand, blown clear of fire by the landing
            GodRuin.Crater(A, at, _war ? SunCrater * WarRuin : SunCrater, 4, _allies);
            GodRuin.Calm(at, 2.5f);
            StarsAnswer(at);
            Kit.Stop(at, 0.25f);
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", tile);
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            EndLink = _diveHits >= 2 ? Link.Scattered : Link.None;
            if (Dev.On)
            {
                Dev.Trace("starfall " + Id + " sun meteor hits " + _diveHits + (crit ? " (crit)" : ""));
            }
        }

        /// <summary>
        /// 180 + 5xD at the centre falling to 40% at 4 tiles; everyone out to 6 tiles knocked down.
        /// It falls from the sky: bodies the stars threw into the air are caught in it too (fliers
        /// are not: it is not an anti-air blow), and so is anyone mid-technique (not thrown).
        /// </summary>
        private bool HurtDive(Vector2 at)
        {
            float rDmg = DiveRadius * _k, rKnock = KnockRadius * _k;
            List<Actor> hit = Combat.FoesAround(A, at, rKnock, true, true);
            bool crit = false;
            int n = 0;
            foreach (Actor u in hit)
            {
                if (!u.hasHealth() || u.isFlying() || u.asset.is_boat || u.isInsideSomething() || Godhood.InGodMove(u))
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, at);
                bool inner = d <= rDmg + u.stats["size"] * 0.5f;
                if (!Combat.Allowed(A, u, out float cm))
                {
                    if (inner)
                    {
                        Blow(u, 0.5f, true, 0f);
                    }
                    Combat.Stagger(u, 0.4f);
                    continue;
                }
                if (!Reached(u))
                {
                    continue;
                }
                if (inner)
                {
                    float f = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(d / rDmg)) * cm;
                    HitResult r = Blow(u, 5f * f, true, 180f * f);
                    crit |= r.Crit;
                    n++;
                }
                if (!u.isAlive() || !u.hasHealth() || Moves.Locked(u))
                {
                    continue;
                }
                float k = Mathf.Clamp01(d / rKnock);
                // a flow strike ends here (the Sun takes it over); a high tier is down only briefly
                Moves.Interrupt(u);
                u.addStatusEffect("stunned", Mathf.Lerp(1.4f, 0.8f, k) * Mathf.Max(0.5f, Tiers.Linger(u)));
                _fly.Add(new Fly
                {
                    U = u,
                    Id = u.Sid(),
                    Distance = Mathf.Lerp(3.2f, 1.2f, k) * _k,
                    Apex = Mathf.Lerp(2.4f, 0.8f, k) * _k,
                    Spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(540f, 900f)
                });
            }
            _diveHits = n;
            return crit;
        }

        private void SunImpact(Vector2 at, WorldTile tile, bool crit)
        {
            float px = Px;
            float big = px * 1.5f;
            bool water = tile != null && tile.Type != null && tile.Type.liquid;
            Vector2 up = new Vector2(0f, 4f * px);
            // the impact frame (black flash on a crit) and a white-gold flash
            Kit.ImpactFrame(at + up, big, 1.2f, _pal, crit, Hi);
            Kit.Flash(at + up, big, 18, 0.12f, Hot, Hi, 29);
            if (water)
            {
                Kit.Splash(at, big, tile, 2f);
            }
            else
            {
                GoldCrater(at, tile);
            }
            // rings at 2, 4 and 6 tiles
            Fx.Ring(at, px, 0.42f, 6f, Tex(2f), 3, 1, _full ? FxLayer.Cine : FxLayer.Ground, 6, Art.Ground, 2.6f,
                White, Fx.WithA(GoldLight, 0.8f), Fx.WithA(Gold, 0f));
            Particle r4 = Fx.Ring(at, px, 0.6f, 8f, Tex(4f), 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f,
                Fx.WithA(GoldLight, 0.9f), Fx.WithA(Gold, 0.5f), Fx.WithA(Gold, 0f));
            if (r4 != null)
            {
                r4.Delay = 0.06f;
            }
            Particle r6 = Fx.Ring(at, px, 0.78f, 10f, Tex(6f), 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f,
                Fx.WithA(White, 0.7f), Fx.WithA(GoldLight, 0.35f), Fx.WithA(Gold, 0f));
            if (r6 != null)
            {
                r6.Delay = 0.12f;
            }
            // the dust wall, a dust column, rocks, sparks, embers
            Kit.DustRing(at, big, tile, N(22), 95f, 0.95f, _k);
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.45f);
            int column = N(3);
            for (int i = 0; i < column; i++)
            {
                Particle p = Fx.Spawn(Art.Dust[Random.Range(3, 6)], at + new Vector2(Random.Range(-3f, 3f), Random.Range(-1f, 2f)) * big, big,
                    Random.Range(1f, 1.3f), FxLayer.Top, 6);
                p?.Moving(new Vector2(Random.Range(-4f, 4f), 0f) * px, Random.Range(10f, 18f) * px * _k, 1.6f).Sized(0.6f, 1.05f)
                    .Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
            }
            Kit.Debris(at, big, tile, N(16), _k);
            Kit.Sparks(at + new Vector2(0f, 2f * px), px, N(14), 110f, 190f, GoldLight, Hi);
            int embers = N(12);
            for (int i = 0; i < embers; i++)
            {
                Kit.Ember(at + Random.insideUnitCircle * 8f * px, px, Random.Range(0.9f, 1.6f),
                    new Vector2(Random.Range(-10f, 10f), Random.Range(15f, 38f)) * px, Random.value < 0.4f ? GoldLight : Gold);
            }
            Kit.Shake(at, 0.45f, 0.016f, _k);
        }

        /// <summary>
        /// The fallen stars answer the sun: as the shockwave passes each star's crater (in order of
        /// distance; the hit-stop holds them first), it flares gold and sheds an ember, and the big
        /// ones ring. The rain and the dive read as one strike, even when nobody is left to hit.
        /// </summary>
        private void StarsAnswer(Vector2 at)
        {
            float px = Px;
            int shown = 0;
            foreach (Meteor m in _meteors)
            {
                if (!m.Done || shown >= 24)
                {
                    continue;
                }
                float dist = Vector2.Distance(m.Land, at);
                if (dist < 1.2f * _k || !Kit.Visible(m.Land, 0.2f))
                {
                    continue;   // under the Sun's own crater, or off screen
                }
                WorldTile tile = Combat.TileAt(m.Land);
                if (tile != null && tile.Type != null && tile.Type.liquid)
                {
                    continue;
                }
                shown++;
                float delay = 0.05f + dist / (16f * _k);
                Particle f = Kit.Flash(m.Land + new Vector2(0f, 1.5f * px), px, Rx(0.45f), 0.18f, GoldLight, FxLayer.Top, 27);
                if (f != null)
                {
                    f.Sized(0.5f, 1.1f).Color(Fx.WithA(Hot, 0.95f), Fx.WithA(GoldLight, 0.7f), Fx.WithA(Gold, 0f));
                    f.Delay = delay;
                }
                Particle e = Fx.Spawn(Art.Pixel, m.Land + new Vector2(Random.Range(-2f, 2f), Random.Range(0f, 2f)) * px, px,
                    Random.Range(0.8f, 1.2f), FxLayer.Top, 9);
                if (e != null)
                {
                    e.Moving(new Vector2(Random.Range(-3f, 3f), Random.Range(14f, 24f)) * px, 0f, 0.6f)
                        .Color(GoldLight, Gold, Fx.WithA(GodFx.GoldDeep, 0f));
                    e.Delay = delay;
                }
                if (m.Big)
                {
                    Particle r = Fx.Ring(m.Land, px, 0.3f, 2f, Tex(0.8f), 1, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                        Fx.WithA(GoldLight, 0.9f), Fx.WithA(Gold, 0f));
                    if (r != null)
                    {
                        r.Delay = delay;
                    }
                }
            }
        }

        /// <summary>A 3-tile crater whose cracks glow gold for 30 s, cooling slowly.</summary>
        private void GoldCrater(Vector2 at, WorldTile tile)
        {
            Art.CrackSet set = Art.Cracks[Random.Range(0, Art.Cracks.Length)];
            float cpx = Px * 1.8f * _k;
            const float life = 30f;
            Color soil = Fx.SoilColor(tile);
            Color dark = Fx.Darken(soil, 0.6f);
            Color light = Fx.Lighten(soil, 0.35f);
            float flip = Random.value < 0.5f ? -1f : 1f;
            Particle d = Fx.Spawn(set.Dark, at, cpx, life, FxLayer.Ground, 1);
            if (d != null)
            {
                d.Snap = true;
                d.SX = flip;
                d.Color(Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0f));
            }
            Particle l = Fx.Spawn(set.Light, at, cpx, life, FxLayer.Ground, 2);
            if (l != null)
            {
                l.Snap = true;
                l.SX = flip;
                l.Color(Fx.WithA(light, 0.9f), Fx.WithA(light, 0.9f), Fx.WithA(light, 0.85f), Fx.WithA(light, 0.8f), Fx.WithA(light, 0f));
            }
            Particle g = Fx.Spawn(set.Glow, at, cpx, life, FxLayer.Ground, 3);
            if (g != null)
            {
                g.Snap = true;
                g.SX = flip;
                g.Color(Hot, GoldLight, Fx.WithA(Gold, 0.95f), Fx.WithA(Gold, 0.85f), Fx.WithA(Gold, 0.75f),
                    Fx.WithA(GodFx.GoldDeep, 0.65f), Fx.WithA(GodFx.GoldDeep, 0.5f), Fx.WithA(GodFx.GoldDeep, 0.3f), new Color(0.4f, 0.14f, 0.03f, 0f));
            }
        }

        /// <summary>When the hit-stop lets go, the bodies fly (a lone survivor in the air: the God chases it).</summary>
        private void FlyVictims()
        {
            int flown = 0;
            Actor last = null;
            foreach (Fly f in _fly)
            {
                Actor u = f.U;
                if (u == null || !u.isAlive() || !u.hasHealth() || u.Sid() != f.Id)
                {
                    continue;
                }
                Combat.Launch(u, _dive, f.Distance, f.Apex, f.Spin);
                GodRuin.Shelter(u, _dive, f.Distance);
                flown++;
                last = u;
            }
            _fly.Clear();
            if (flown >= 2 || (flown == 1 && _diveHits >= 2))
            {
                EndLink = Link.Scattered;
            }
            else if (flown == 1)
            {
                EndLink = Link.Launched;
                EndTarget = last;
            }
        }

        // ------------------------------------------------------- glued effects --

        public override void Frame()
        {
            if (_clone != null && _clone.Alive)
            {
                Fx.Glue(_clone, this, White);
            }
            if (_glow != null && _glow.Alive)
            {
                Fx.Glue(_glow, this, GlowColor());
            }
        }

        /// <summary>The God's own light: gathering gold, the held sun, the Sun Meteor white-hot, cooling.</summary>
        private Color GlowColor()
        {
            float t = T;
            if (t < TTell)
            {
                return Fx.WithA(Gold, 0.45f * Anim.Smooth(0f, TTell, t) * (0.75f + 0.25f * Mathf.Sin(t * 50f)));
            }
            if (t < TApex)
            {
                return Fx.WithA(Gold, Mathf.Lerp(0.4f, 0.1f, (t - TTell) / (TApex - TTell)));
            }
            if (t < TPose)
            {
                return Fx.WithA(Gold, 0.1f);
            }
            if (t < TRain)
            {
                return Fx.WithA(GoldLight, Mathf.Lerp(0.55f, 0.25f, (t - TPose) / (TRain - TPose)));
            }
            if (t < TDive)
            {
                return Fx.WithA(GoldLight, 0.22f + 0.08f * Mathf.Sin(t * 7f));
            }
            if (t < TImpact)
            {
                return Fx.WithA(Color.Lerp(Sun1, Hot, Mathf.Clamp01((t - TDive) / (TImpact - TDive))), Mathf.Lerp(0.5f, 0.85f, (t - TDive) / (TImpact - TDive)));
            }
            if (t < TImpact + 0.05f)
            {
                return Fx.WithA(White, 0.95f);
            }
            if (t < TRise)
            {
                return Fx.WithA(Hot, 0.6f);
            }
            return Color.Lerp(Fx.WithA(Gold, 0.55f), Fx.WithA(Gold, 0f), Mathf.Clamp01((t - TRise) / 0.35f));
        }

        /// <summary>Our own shadow: it stays on the ground and shrinks and fades with height.</summary>
        private void UpdateShadow()
        {
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            float h = _apex > 0f ? Mathf.Clamp01(Pose.Height / _apex) : 0f;
            Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, 0.35f, h)), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != s)
            {
                _shadow.R.sprite = s;
                _shadow.Frames[0] = s;
            }
            _shadow.Pos = Pose.Ground;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.36f, 0.14f, h));
        }

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        // ------------------------------------------------------------- endings --

        private void Finish()
        {
            if (Finished)
            {
                return;
            }
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            if (_hop)
            {
                Pose.Ground = _hopTo;
            }
            Cleanup();
            // it rises out of its own ruin unburnt and fights straight on
            GodRuin.Settle(A, Pose.Ground);
            GodRuin.AfterMove(A);
            Finished = true;
        }

        private void KillHeld()
        {
            _shadow?.Kill();
            _marker?.Kill();
            _markerRing?.Kill();
            _sun?.Kill();
            _shadow = _marker = _markerRing = _sun = null;
            _trail = false;
        }

        /// <summary>Everything the move holds goes, and the cinematic it began ends (safe to call twice).</summary>
        private void Cleanup()
        {
            KillHeld();
            _clone?.Kill();
            _glow?.Kill();
            _clone = _glow = null;
            foreach (Meteor m in _meteors)
            {
                Hide(m);
            }
            if (_full)
            {
                Cinema.End(_cast);
            }
            _cine = false;
        }

        public override void Abort()
        {
            Cleanup();
            _fly.Clear();
        }

        public override void OnEnd(bool aborted)
        {
            Cleanup();
            _fly.Clear();
            Godhood.Ended(this, _full);
        }
    }

    /// <summary>
    /// Starfall as a technique: a God ability (Entry God, one level). The Brain never plans it;
    /// Godhood unleashes it when the gauge is full and a crowd stands near (10 tiles) or an army
    /// is in sight (16 tiles).
    /// </summary>
    internal sealed class StarfallTech : Technique
    {
        public override string Id => "starfall";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.God;

        /// <summary>The sky turns crimson and the stars fall on the enemy army; the God follows them down.</summary>
        public override Form Forms => Form.Ki | Form.Aerial | Form.Area | Form.Overhead | Form.Ultimate | Form.Charge;

        public override Scale Scale => Scale.Ultimate;

        public override int MaxLevel => 1;
        public override bool GodAbility => true;
        public override float[] ManaCost => new[] { 0f };
        public override float[] Cooldown => new[] { Godhood.CooldownStarfall };

        public override Plan Plan(Ctx c, int level)
        {
            return Godhood.PlanFor(this, GodKind.Starfall, c);
        }

        public override bool Begin(Actor a, Plan p)
        {
            return Godhood.Begin(this, GodKind.Starfall, a, p);
        }
    }
}
