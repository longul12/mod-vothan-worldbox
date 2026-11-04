using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>The three God abilities.</summary>
    internal enum GodKind
    {
        Starfall,
        ZeroMoment,
        LastLight
    }

    /// <summary>
    /// One use of a god ability: which, on whom, and how it is staged. It is also the Cinema owner
    /// of a full cinematic, so the move that plays it ends exactly the cinematic it began.
    /// </summary>
    internal sealed class GodCast
    {
        public GodKind Kind;
        /// <summary>Zero Moment's target.</summary>
        public Actor Target;
        /// <summary>Starfall: where to dive if no crowd is left to dive into.</summary>
        public Vector2? Point;
        /// <summary>Ordered by a dev command or a god power (no gauge, no cooldown, no 25 s gap).</summary>
        public bool Forced;
        /// <summary>The staging is settled (Full, or light).</summary>
        public bool Decided;
        /// <summary>Full cinematic: the world freezes and darkens. Otherwise light: world speed, no veil.</summary>
        public bool Full;
    }

    /// <summary>
    /// GODHOOD. The three God abilities are not planned by the Brain like the other techniques:
    /// the Sakuga gauge and the situation choose them, and this plays them.
    ///
    ///   GAUGE (Fighter.Gauge 0..100, every fighter that knows a god ability)
    ///     +1    per technique blow dealt (at most +5 per move)
    ///     +2    per hit taken worth 2% of max HP or more
    ///     +4    per kill, +10 for a tiered victim
    ///     +8    when an ally dies within 10 tiles
    ///     +1.5  per second while 8+ enemies (critters don't count) stand within 10 tiles
    ///     +20   when a duel with a Master or better starts (if the Duels feature is there)
    ///     -2    per second after 15 s out of combat (and 15 s since it last rose)
    ///     (nothing fills it while a god ability plays: its own kills don't refill it)
    ///   GOD WAR: while a Grandmaster or God stands against it within 22 tiles, everything above
    ///     counts three times, +1 more per second of fighting (x3 too), and the god abilities cool
    ///     down three times faster: God abilities fly when Gods fight.
    ///
    ///   SELECTION (gauge 100, free to act, in combat; looked at 4 times a second, and again the
    ///   moment the Brain is about to choose a technique: a full gauge has the first say, so a
    ///   God never spends the moment on a lesser technique)
    ///     ZERO MOMENT  a Kaiju, a Titan that is a real threat to it (Threat.Ratio 0.45+, or the God
    ///                  under half its health: a dragon is no threat to a God), a Grandmaster or
    ///                  God, its sworn rival, or an enemy that killed 3+ of our side in the last
    ///                  20 s, within 8 tiles (a Grandmaster or God: 12)              cooldown 120 s
    ///     STARFALL     crowd points (Folk 1, Brute 2, Fiend 3, Titan 6) >= 12 within 10 tiles, or
    ///                  >= 15 within 22 tiles (an army in sight), or a god war (the rain falls on
    ///                  the whole battlefield), and allies in the strike zones <= 50% of the
    ///                  enemies; once the gauge has been full for 30 s of fighting, 6 points will
    ///                  do if they are a real threat together (the guarantee)        cooldown 150 s
    ///                  (both possible: Starfall when the army is worth more than the one foe; in
    ///                  a god war the two take turns)
    ///     LAST LIGHT   on its own: a hit that would kill, or leave 15% HP or less, is cancelled and
    ///                  the God rises; no gauge, once per battle                     cooldown 300 s
    ///     Never while another Hooks.HoldTechniques handler holds the God (a duel's stare-down, a
    ///     duel's exchange, a free fighting whose techniques are spent). In a duel's free fighting
    ///     a god ability counts as one of its techniques (Zero Moment only on the opponent), and
    ///     the duel's own pacing caps what it takes (a quarter of a life).
    ///
    ///   STAGING
    ///     full    freeze, veil, slow motion: only where the player is looking (Cinema.FullAt) or
    ///             with Camera Follow on, one cinematic at a time: 10 s after the last god
    ///             ability's, 3 s after anyone else's. While it waits for its turn (at most 5 s)
    ///             the God's techniques are held; after that (or at once, when the 10 s can't
    ///             run out within the wait) it plays light. In a god war one
    ///             full cinematic, then light for 90 s (and never a wait): the war never stops.
    ///     light   the same mechanics at world speed: no freeze, no veil, fewer particles.
    ///     The first time each ability is seen this session, a gold title card names it.
    ///     Camera Follow: the camera jumps to the God when it unleashes one (and never otherwise;
    ///     the zoom stays the player's, and a unit the player follows keeps the camera).
    ///
    ///   RUIN (Kit.Ravage, the "Environment damage" setting): Starfall's stars are burning
    ///     craters, the Sun Meteor reshapes the land; Zero Moment's cuts open trenches and the snap
    ///     a crater; Last Light's ring scorches and flattens. No flames where the God's own people
    ///     stand (a crater without fire there) and the God never ends a god ability in its own fire
    ///     (GodRuin); Masters and up are fire-immune, so the flames stay where they stand. In a god
    ///     war (Godhood.AtWar: its equal stands against it) every ability tears the land wider.
    ///
    ///   PRESENCE (Settings.Auras)
    ///     a Combat God sheds a gold ember every 1.5 s; a full gauge pulses a gold outline every 2 s.
    ///   AWAKENED (after Last Light, 12 s): techniques cost nothing, cooldowns halve, +25% damage
    ///     (Fighters.DamageMult), gold afterimages while moving, an ember aura, a gold puff at the end.
    ///   FALLEN: a God that dies anyway after its Last Light is announced, and its tiered allies
    ///     within 20 tiles fight on with +25% damage for 30 s.
    /// </summary>
    internal static class Godhood
    {
        public const float CooldownStarfall = 150f;
        public const float CooldownZero = 120f;
        public const float CooldownLast = 300f;
        /// <summary>World seconds between two god abilities' full cinematics.</summary>
        public const float GodGap = 10f;
        /// <summary>World seconds after any other cinematic before a god ability may freeze the world.</summary>
        public const float AnyGap = 3f;
        /// <summary>A request that waited this long for its cinematic turn plays light.</summary>
        public const float QueueWait = 5f;

        public const string TimerStarfall = "sakuga_starfall";
        public const string TimerZero = "sakuga_zero_moment";
        public const string TimerLast = "sakuga_last_light";

        /// <summary>The near crowd (the gauge's pressure, Starfall's 12 points).</summary>
        private const float Sight = 10f;
        /// <summary>How far a God looks for an army (the battlefield): Starfall's reach.</summary>
        public const float Reach = 22f;
        private const float ZeroRange = 8f;
        /// <summary>Zero Moment's reach for a Grandmaster or God (a god war spans the field).</summary>
        private const float ZeroRangeWar = 12f;
        /// <summary>A god war: every gauge gain counts this many times.</summary>
        public const float WarGauge = 3f;
        /// <summary>A god war: the god abilities' cooldowns are multiplied by this.</summary>
        public const float WarCooldown = 0.35f;
        /// <summary>A god war: after one full cinematic, god abilities play light for this long (world seconds).</summary>
        public const float WarFullGap = 90f;

        /// <summary>What the mod remembers about one fighter for its god abilities.</summary>
        private sealed class Divine
        {
            public Fighter F;
            public Actor A;
            public long Id;
            public bool Starfall, Zero, Last;
            public float Recheck;
            public bool Listed;

            // the battlefield around it (refreshed twice a second while it fights): Foes and
            // Allies out to Reach; Crowd and Points within Sight, CrowdAll and PointsAll out to Reach
            public float ScanIn;
            public readonly List<Actor> Foes = new List<Actor>();
            public readonly List<Actor> Allies = new List<Actor>();
            public int Crowd, CrowdAll;
            public float Points, PointsAll;
            /// <summary>A god war: a Grandmaster or God stands against it (WarFoe: the nearest one).</summary>
            public bool War;
            public Actor WarFoe;
            /// <summary>The last god ability it unleashed (-1 none): in a god war the two take turns.</summary>
            public int LastKind = -1;

            // gauge bookkeeping
            public Move HitMove;
            public int HitCount;
            /// <summary>
            /// Units its god ability's blows left dying: their deaths (processed later, perhaps after
            /// the ability ended) don't refill the gauge.
            /// </summary>
            public readonly HashSet<long> AbilityKills = new HashSet<long>();
            public double LooseAt = -999;
            public int LooseCount;
            public float FullCombat;
            public bool InDuel;
            public float DuelIn;
            public float SelectIn;
            public int SelectFrame = -1;
            public double WaitSince = -1;
            /// <summary>World time the gauge last rose (any way: a blow, a god power, the dev tools).</summary>
            public double GainAt = -999;
            public float SeenGauge;
            public double ZonesAt = -999;
            public bool ZonesOk;

            // abilities
            public bool Active;
            public double PendingSince = -1;
            public double LastLightBattle = -2;
            public double SpentAt = -999;
            public string Name;
            public bool WasGod;
            public Kingdom Side;
            public Vector2 Pos, PrevPos;
            public bool Fallen;

            // Last Light's healing (world time)
            public double HealAt = -1;
            public int HealFrom, HealTo;

            /// <summary>Until then (world time) its own fire parts for it: the ground under it is put out as it walks.</summary>
            public double FireproofUntil = -1;

            // presence
            public float EmberIn, PulseIn, AfterIn, AuraIn;
            public bool AwakeFx;

            public bool Knows => Starfall || Zero || Last;

            public bool Valid => A != null && A.Sid() == Id && A.isAlive();
        }

        /// <summary>A unit that recently killed: who it killed, when (Zero Moment hunts butchers).</summary>
        private sealed class Slayer
        {
            public readonly List<double> At = new List<double>(4);
            public readonly List<Kingdom> Of = new List<Kingdom>(4);
        }

        private static readonly Dictionary<long, Divine> _all = new Dictionary<long, Divine>();
        private static readonly List<Divine> _holders = new List<Divine>();
        private static readonly Dictionary<long, Slayer> _slayers = new Dictionary<long, Slayer>();
        private static readonly List<long> _drop = new List<long>();
        private static readonly bool[] _seen = new bool[3];
        private static readonly List<Vector2> _points = new List<Vector2>();

        private static Technique _starfall, _zero, _last;
        private static Func<Actor, bool> _inDuel;
        private static Func<Actor, Actor> _opponent;
        private static Func<Actor, bool> _hold;
        private static int _mainThread = -1;
        private static int _frame = -1;
        private static float _pruneIn;
        /// <summary>World time the last full god-ability cinematic ended.</summary>
        private static double _lastGodEnd = -999;
        /// <summary>World time a god war last had a full cinematic (after it, the war plays light).</summary>
        private static double _warFullAt = -999;

        /// <summary>God abilities started / Last Lights fired since the game started (dev log).</summary>
        internal static int Casts, Rises;

        // ------------------------------------------------------------ wiring --

        [SakugaInit]
        private static void Init()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            _starfall = Techniques.Get("starfall");
            _zero = Techniques.Get("zero_moment");
            _last = Techniques.Get("last_light");
            Hooks.FighterTick += Tick;
            Hooks.HitDealt += OnHit;
            Hooks.Killed += OnKilled;
            Hooks.TechniqueStarted += OnTechnique;
            Hooks.TierChanged += (a, from, to, natural) => Recheck(a);
            Hooks.WorldCleared += Clear;
            Hooks.BeforeDamage.Add(BeforeDamage);
            _hold = HoldForGod;
            Hooks.HoldTechniques.Add(_hold);
            FindDuels();
        }

        /// <summary>The Duels feature is optional: its two questions are looked up by name, once.</summary>
        private static void FindDuels()
        {
            try
            {
                Type t = typeof(Godhood).Assembly.GetType("Sakuga.Duels");
                if (t == null)
                {
                    return;
                }
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                MethodInfo inDuel = t.GetMethod("InDuel", any, null, new[] { typeof(Actor) }, null);
                MethodInfo opponent = t.GetMethod("Opponent", any, null, new[] { typeof(Actor) }, null);
                if (inDuel != null && inDuel.ReturnType == typeof(bool))
                {
                    _inDuel = (Func<Actor, bool>)Delegate.CreateDelegate(typeof(Func<Actor, bool>), inDuel);
                }
                if (opponent != null && opponent.ReturnType == typeof(Actor))
                {
                    _opponent = (Func<Actor, Actor>)Delegate.CreateDelegate(typeof(Func<Actor, Actor>), opponent);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god gauge: duels not linked: " + e.Message);
                _inDuel = null;
                _opponent = null;
            }
        }

        private static void Clear()
        {
            _all.Clear();
            _holders.Clear();
            _slayers.Clear();
            _points.Clear();
            _frame = -1;
            _pruneIn = 0f;
            _lastGodEnd = -999;
            _warFullAt = -999;
        }

        private static GodKind KindOf(Technique t)
        {
            return t == _zero ? GodKind.ZeroMoment : t == _last ? GodKind.LastLight : GodKind.Starfall;
        }

        private static Technique TechOf(GodKind k)
        {
            return k == GodKind.ZeroMoment ? _zero : k == GodKind.LastLight ? _last : _starfall;
        }

        private static string TimerOf(GodKind k)
        {
            return k == GodKind.ZeroMoment ? TimerZero : k == GodKind.LastLight ? TimerLast : TimerStarfall;
        }

        private static float CooldownOf(GodKind k)
        {
            return k == GodKind.ZeroMoment ? CooldownZero : k == GodKind.LastLight ? CooldownLast : CooldownStarfall;
        }

        /// <summary>Has this ability cooled down (three times faster in a god war; Last Light never faster)?</summary>
        private static bool Ready(Divine d, GodKind k)
        {
            // a God ability is a duel's turning point, once: never the same one twice in one duel
            if (k != GodKind.LastLight && d.F != null && d.F.A != null)
            {
                Duel du = Duels.Of(d.F.A);
                if (du != null && d.F.MarkedAt(TimerOf(k)) >= du.Began)
                {
                    return false;
                }
            }
            float cd = CooldownOf(k);
            if (d.War && k != GodKind.LastLight)
            {
                cd *= WarCooldown;
            }
            return d.F.Elapsed(TimerOf(k), cd);
        }

        // ------------------------------------------------------------ who --

        private static Divine Of(Fighter f)
        {
            if (f == null)
            {
                return null;
            }
            if (!_all.TryGetValue(f.Id, out Divine d) || d.F != f)
            {
                if (d != null && d.Listed)
                {
                    _holders.Remove(d);
                }
                d = new Divine { F = f, A = f.A, Id = f.Id };
                _all[f.Id] = d;
                Refresh(d);
            }
            return d;
        }

        private static Divine Of(Actor a)
        {
            return a != null && _all.TryGetValue(a.Sid(), out Divine d) && d.A == a ? d : null;
        }

        /// <summary>Which god abilities it knows (from its tier, or the bare traits).</summary>
        private static void Refresh(Divine d)
        {
            d.Recheck = 2f;
            Actor a = d.A;
            bool alive = a != null && a.isAlive();
            d.Starfall = alive && _starfall != null && Techniques.Level(a, _starfall) > 0;
            d.Zero = alive && _zero != null && Techniques.Level(a, _zero) > 0;
            d.Last = alive && _last != null && Techniques.Level(a, _last) > 0;
            if (d.Knows && !d.Listed)
            {
                d.Listed = true;
                _holders.Add(d);
            }
            else if (!d.Knows && d.Listed)
            {
                d.Listed = false;
                _holders.Remove(d);
            }
        }

        private static void Recheck(Actor a)
        {
            Divine d = Of(a);
            if (d != null)
            {
                d.Recheck = 0f;
            }
        }

        // ------------------------------------------------------------ tick --

        private static void Tick(Fighter f, float dt)
        {
            if (World.world == null)
            {
                return;
            }
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                Sweep(dt);
            }
            Divine d = Of(f);
            d.Recheck -= dt;
            if (d.Recheck <= 0f)
            {
                Refresh(d);
            }
            if (!d.Knows)
            {
                return;
            }
            Actor a = f.A;
            double now = Fighter.Now;
            d.PrevPos = d.Pos;
            d.Pos = a.current_position;
            d.Side = a.kingdom ?? d.Side;
            if (d.Active && (!Moves.TryGet(a, out Move cur) || cur.Tech == null || !cur.Tech.GodAbility))
            {
                d.Active = false;   // never left untouchable by a move that ended some other way
            }

            if (d.PendingSince >= 0)
            {
                // Last Light is waiting for the God to be free of whatever holds it (a throw, a juggle)
                if (now - d.PendingSince > 3.0)
                {
                    d.PendingSince = -1;
                }
                else if (LastLight.CanRise(a))
                {
                    d.PendingSince = -1;
                    LaunchLastLight(d, false);
                }
            }
            Heal(d, now);
            Awakened(d, dt, now);
            if (now < d.FireproofUntil && !Moves.Busy(a))
            {
                // the God walks out of its own ruin: its fire parts for it (no burning, no balking
                // at a burning tile, no running for water in the middle of the fight)
                GodRuin.Calm(a.current_position, 1.2f);
                if (a.hasStatus("burning"))
                {
                    a.finishStatusEffect("burning");
                }
            }

            bool on = Settings.Cinematics > 0;
            bool fighting = f.InCombat || a.isFighting();
            d.ScanIn -= dt;
            if (d.ScanIn <= 0f)
            {
                d.ScanIn = 0.5f;
                if (on && (fighting || f.Gauge > 0f))
                {
                    Scan(d);
                }
                else
                {
                    d.Foes.Clear();
                    d.Allies.Clear();
                    d.Crowd = d.CrowdAll = 0;
                    d.Points = d.PointsAll = 0f;
                    d.War = false;
                    d.WarFoe = null;
                }
            }
            if (f.Gauge > d.SeenGauge + 0.001f)
            {
                d.GainAt = now;   // it rose since the last look (a blow, a kill, a god power, the dev tools)
            }
            if (on && !d.Active)
            {
                if (fighting && d.Crowd >= 8)
                {
                    Add(d, 1.5f * dt);
                }
                if (fighting && d.War)
                {
                    Add(d, 1f * dt);   // two powers of that scale in one battle: the air itself charges
                }
                // it fades only after 15 s with neither fighting nor any gain (a fresh fighter's
                // LastCombat is long ago: a gauge filled before the battle must not leak away)
                if (now - Math.Max(f.LastCombat, d.GainAt) > 15.0 && f.Gauge > 0f)
                {
                    f.Gauge = Mathf.Max(0f, f.Gauge - 2f * dt);
                }
                Duel(d, dt);
            }
            d.SeenGauge = f.Gauge;
            if (f.Gauge >= 100f)
            {
                if (fighting)
                {
                    d.FullCombat += dt;
                }
            }
            else
            {
                d.FullCombat = 0f;
                d.WaitSince = -1;
            }
            Presence(d, dt, on);
            if (on && f.Gauge >= 100f && !d.Active && d.PendingSince < 0)
            {
                d.SelectIn -= dt;
                if (d.SelectIn <= 0f)
                {
                    d.SelectIn = 0.25f;
                    Select(d);
                }
            }
            else
            {
                d.WaitSince = -1;
            }
        }

        /// <summary>Once a frame: forget the gone (and announce a God that fell after its Last Light).</summary>
        private static void Sweep(float dt)
        {
            for (int i = _holders.Count - 1; i >= 0; i--)
            {
                Divine d = _holders[i];
                bool fighter = d.A != null && Fighters.TryGet(d.A, out Fighter f) && f == d.F;
                if (d.Valid && fighter)
                {
                    continue;
                }
                if (!d.Valid)
                {
                    Fall(d);
                }
                d.Listed = false;
                _holders.RemoveAt(i);
                if (_all.TryGetValue(d.Id, out Divine cur) && cur == d)
                {
                    _all.Remove(d.Id);
                }
            }
            _pruneIn -= dt;
            if (_pruneIn > 0f)
            {
                return;
            }
            _pruneIn = 5f;
            _drop.Clear();
            foreach (KeyValuePair<long, Divine> kv in _all)
            {
                if (!kv.Value.Valid)
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (long id in _drop)
            {
                _all.Remove(id);
            }
            _drop.Clear();
            double now = Fighter.Now;
            foreach (KeyValuePair<long, Slayer> kv in _slayers)
            {
                List<double> at = kv.Value.At;
                if (at.Count == 0 || now - at[at.Count - 1] > 20.0)
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (long id in _drop)
            {
                _slayers.Remove(id);
            }
        }

        /// <summary>
        /// Who stands within 22 tiles (enemies, allies), the crowd's weight near (10) and in all, and
        /// whether a Grandmaster or God stands against it (a god war).
        /// </summary>
        private static void Scan(Divine d)
        {
            Actor a = d.A;
            Vector2 at = a.current_position;
            Gather(a, at, Reach, d.Foes, d.Allies, false);
            int crowd = 0, all = 0;
            float points = 0f, pointsAll = 0f;
            const float near2 = Sight * Sight;
            Actor warFoe = null;
            float warD = float.MaxValue;
            foreach (Actor u in d.Foes)
            {
                // (a body a blow already took, or one a meteor is carrying to its death, is no crowd)
                if (u.asset.is_boat || !Combat.Living(u) || MeteorSlam.IsDoomed(u))
                {
                    continue;
                }
                if (Tiers.Of(u) >= Tier.Grandmaster)
                {
                    float d2 = (u.current_position - at).sqrMagnitude;
                    if (d2 < warD)
                    {
                        warD = d2;
                        warFoe = u;
                    }
                }
                Weight w = Weights.Of(u);
                if (w == Weight.Critter)
                {
                    continue;
                }
                bool near = (u.current_position - at).sqrMagnitude <= near2;
                all++;
                if (near)
                {
                    crowd++;
                }
                if (Combat.Grounded(u))
                {
                    float p = CrowdPoints(w);
                    pointsAll += p;
                    if (near)
                    {
                        points += p;
                    }
                }
            }
            d.Crowd = crowd;
            d.CrowdAll = all;
            d.Points = points;
            d.PointsAll = pointsAll;
            bool war = warFoe != null;
            if (war && !d.War && Dev.On)
            {
                Dev.Trace("god war: " + a.asset.id + "#" + d.Id + " faces " + warFoe.asset.id + "#" + warFoe.Sid() + " (" + Tiers.Of(warFoe) + ") at "
                    + Mathf.Sqrt(warD).ToString("F1") + " tiles: gauge x" + WarGauge.ToString("F0"));
            }
            d.War = war;
            d.WarFoe = warFoe;
        }

        /// <summary>How much of a crowd one unit makes (Folk 1, Brute 2, Fiend 3, Titan 6).</summary>
        public static float CrowdPoints(Weight w)
        {
            switch (w)
            {
                case Weight.Critter: return 0f;
                case Weight.Folk: return 1f;
                case Weight.Brute: return 2f;
                case Weight.Fiend: return 3f;
                default: return 6f;
            }
        }

        /// <summary>
        /// Hostile units (and, if asked, allies) within r tiles of 'at', in one pass over the map
        /// chunks. groundOnly: only grounded enemies not busy in a move (what a falling star can hit).
        /// </summary>
        public static void Gather(Actor a, Vector2 at, float r, List<Actor> foes, List<Actor> allies, bool groundOnly)
        {
            foes.Clear();
            allies?.Clear();
            WorldTile tile = Combat.TileAt(at);
            if (tile == null || a == null || a.kingdom == null)
            {
                return;
            }
            float r2 = r * r;
            int chunks = r > 14f ? 2 : 1;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, chunks))
            {
                // (Living: one a blow just killed is still "alive" until the game's death check)
                if (u == null || u == a || !Combat.Living(u))
                {
                    continue;
                }
                if ((u.current_position - at).sqrMagnitude > r2)
                {
                    continue;
                }
                if (Combat.IsFoe(a, u))
                {
                    if (groundOnly && (!Combat.Grounded(u) || Moves.Busy(u)))
                    {
                        continue;
                    }
                    foes.Add(u);
                }
                else if (allies != null && Combat.IsAlly(a, u))
                {
                    allies.Add(u);
                }
            }
        }

        // ----------------------------------------------------------- gauge --

        private static void Add(Divine d, float amount)
        {
            if (d.Active || Settings.Cinematics <= 0 || amount <= 0f)
            {
                return;
            }
            if (d.War)
            {
                amount *= WarGauge;
            }
            Fighter f = d.F;
            float was = f.Gauge;
            f.Gauge = Mathf.Clamp(f.Gauge + amount, 0f, 100f);
            if (f.Gauge > was)
            {
                d.GainAt = Fighter.Now;
            }
            if (was < 100f && f.Gauge >= 100f)
            {
                f.GaugeFullSince = Fighter.Now;
                d.FullCombat = 0f;
                d.PulseIn = 0f;
                d.SelectIn = 0f;
                if (Dev.On)
                {
                    Dev.Trace("god gauge full: " + d.A.asset.id + "#" + d.Id + " (crowd " + d.Points.ToString("F0") + "/" + d.PointsAll.ToString("F0") + ")");
                }
            }
        }

        /// <summary>A technique's blow landed: +1, at most 5 from one move.</summary>
        private static void OnHit(Actor a, Actor v, HitResult r, Move by)
        {
            if (_holders.Count == 0 || a == null || !r.Landed)
            {
                return;
            }
            Divine d = Of(a);
            if (d == null || !d.Knows)
            {
                return;
            }
            if (by != null && by.Tech != null && by.Tech.GodAbility)
            {
                if (r.Killed && v != null)
                {
                    d.AbilityKills.Add(v.Sid());
                }
                return;
            }
            if (d.Active)
            {
                return;
            }
            if (by != null)
            {
                if (by != d.HitMove)
                {
                    d.HitMove = by;
                    d.HitCount = 0;
                }
                if (d.HitCount >= 5)
                {
                    return;
                }
                d.HitCount++;
            }
            else
            {
                double now = Fighter.Now;
                if (now - d.LooseAt > 2.0)
                {
                    d.LooseCount = 0;
                }
                d.LooseAt = now;
                if (d.LooseCount >= 5)
                {
                    return;
                }
                d.LooseCount++;
            }
            Add(d, 1f);
        }

        private static void OnKilled(Actor killer, Actor victim)
        {
            if (victim == null)
            {
                return;
            }
            Divine dv = Of(victim);
            if (dv != null && dv.Listed)
            {
                Fall(dv);
            }
            if (_holders.Count == 0)
            {
                return;
            }
            Kingdom side = victim.kingdom;
            if (killer != null && side != null)
            {
                Record(killer, side);
            }
            Vector2 at = victim.current_position;
            for (int i = 0; i < _holders.Count; i++)
            {
                Divine d = _holders[i];
                if (!d.Valid || d.A == victim)
                {
                    continue;
                }
                if (d.A == killer)
                {
                    // (a death its god ability dealt is processed after the blow, maybe after the
                    // ability ended: its own kills don't refill it)
                    if (!d.AbilityKills.Remove(victim.Sid()))
                    {
                        Add(d, Tiers.Of(victim) != Tier.None ? 10f : 4f);
                    }
                }
                else if (side != null && d.A.kingdom == side && (d.A.current_position - at).sqrMagnitude <= Sight * Sight)
                {
                    Add(d, 8f);
                }
            }
        }

        private static void Record(Actor killer, Kingdom victims)
        {
            long id = killer.Sid();
            if (id < 0)
            {
                return;
            }
            if (!_slayers.TryGetValue(id, out Slayer s))
            {
                s = new Slayer();
                _slayers[id] = s;
            }
            s.At.Add(Fighter.Now);
            s.Of.Add(victims);
            if (s.At.Count > 12)
            {
                s.At.RemoveAt(0);
                s.Of.RemoveAt(0);
            }
        }

        /// <summary>Has v killed 3 or more of 'ours' in the last 20 s?</summary>
        private static bool Butcher(Actor v, Kingdom ours)
        {
            if (ours == null || !_slayers.TryGetValue(v.Sid(), out Slayer s))
            {
                return false;
            }
            double now = Fighter.Now;
            int n = 0;
            for (int i = 0; i < s.At.Count; i++)
            {
                if (now - s.At[i] <= 20.0 && s.Of[i] == ours)
                {
                    n++;
                }
            }
            return n >= 3;
        }

        /// <summary>A duel with a Master or better just began: +20.</summary>
        private static void Duel(Divine d, float dt)
        {
            if (_inDuel == null)
            {
                return;
            }
            d.DuelIn -= dt;
            if (d.DuelIn > 0f)
            {
                return;
            }
            d.DuelIn = 0.25f;
            bool now;
            Actor rival = null;
            try
            {
                now = _inDuel(d.A);
                if (now && !d.InDuel && _opponent != null)
                {
                    rival = _opponent(d.A);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god gauge: duels: " + e.Message);
                _inDuel = null;
                return;
            }
            if (now && !d.InDuel && rival != null && Tiers.Of(rival) >= Tier.Master)
            {
                Add(d, 20f);
            }
            d.InDuel = now;
        }

        /// <summary>
        /// Every hit on anyone passes here (Actor.getHit): a God mid-ability can't be hurt, a hit
        /// taken fills the gauge, and the hit that would end a God is cancelled by Last Light.
        /// </summary>
        private static bool BeforeDamage(Actor v, float damage, AttackType type, BaseSimObject attacker)
        {
            if (_holders.Count == 0 || v == null || Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                return false;
            }
            Divine d = Of(v);
            if (d == null || !d.Listed)
            {
                return false;
            }
            if (d.Active)
            {
                return true;   // untouchable while it performs
            }
            if (Settings.Cinematics <= 0 || World.world == null || !v.isAlive())
            {
                return false;
            }
            int hp = v.getHealth();
            int max = Mathf.Max(1, v.getMaxHealth());
            float dealt = damage;
            if (type == AttackType.Other || type == AttackType.Weapon)
            {
                dealt *= 1f - Mathf.Min(v.stats["armor"], 100f) / 100f;
            }
            dealt = Mathf.Max(1f, dealt);
            if (dealt >= max * 0.02f)
            {
                Add(d, 2f);
            }
            bool lethal = dealt >= hp;
            bool crisis = hp - dealt <= max * 0.15f;
            if (d.PendingSince >= 0)
            {
                return lethal || crisis;   // it is already rising: nothing may finish it first
            }
            if ((!lethal && !crisis) || !LastLightReady(d, type, lethal, attacker))
            {
                return false;
            }
            TriggerLastLight(d);
            return true;
        }

        private static bool LastLightReady(Divine d, AttackType type, bool lethal, BaseSimObject attacker)
        {
            if (!d.Last || !LastLight.CouldRise(d.A))
            {
                return false;
            }
            switch (type)
            {
                case AttackType.Age:
                case AttackType.Starvation:
                case AttackType.Metamorphosis:
                case AttackType.Smile:
                case AttackType.None:
                case AttackType.Drowning:
                case AttackType.Water:
                case AttackType.Eaten:
                    return false;
            }
            Fighter f = d.F;
            if (!f.Elapsed(TimerLast, CooldownLast))
            {
                return false;
            }
            if (f.CombatSince >= 0 && d.LastLightBattle == f.CombatSince)
            {
                return false;   // once per battle
            }
            // a crisis is a fight; a killing blow from anything (fire, lightning) counts on its own
            return lethal || f.InCombat || (attacker != null && attacker.isActor());
        }

        private static void TriggerLastLight(Divine d)
        {
            Fighter f = d.F;
            // the cooldown and the battle are spent here (the killing blow is cancelled now, and a
            // God that can't rise in time must not be shielded again by every blow after); what
            // the world remembers of a Last Light (Spend: the FALLEN tale) only once it has risen
            f.Mark(TimerLast);
            d.LastLightBattle = f.CombatSince >= 0 ? f.CombatSince : -2;
            if (!LastLight.CanRise(d.A) || !LaunchLastLight(d, false))
            {
                d.PendingSince = Fighter.Now;
            }
        }

        /// <summary>Remember what the world needs if this God falls after its Last Light.</summary>
        private static void Spend(Divine d)
        {
            d.SpentAt = Fighter.Now;
            d.Fallen = false;
            d.WasGod = d.F.Tier == Tier.God;
            d.Side = d.A.kingdom;
            d.Pos = d.A.current_position;
            try
            {
                d.Name = d.A.getName();
            }
            catch
            {
                d.Name = d.A.asset != null ? d.A.asset.id : "?";
            }
        }

        private static bool LaunchLastLight(Divine d, bool forced)
        {
            if (_last == null)
            {
                return false;
            }
            GodCast cast = new GodCast { Kind = GodKind.LastLight, Forced = forced };
            cast.Full = WantFullFor(d, d.A.current_position, forced) && TrySlot(cast);
            cast.Decided = true;
            Plan p = new Plan { Tech = _last, Level = 1, Score = 1e4f, Data = cast, Point = d.A.current_position };
            if (Brain.Execute(d.F, p))
            {
                Spend(d);
                Rises++;
                return true;
            }
            if (cast.Full)
            {
                Cinema.End(cast);
            }
            return false;
        }

        // ------------------------------------------------------- selection --

        /// <summary>
        /// May a god ability's full cinematic begin now? 10 s after the last god ability's, 3 s
        /// after anyone else's (another feature's cinematic doesn't hold the God's moment back for
        /// long); ordered ones (dev, god powers) whenever no cinematic is playing.
        /// </summary>
        private static bool TrySlot(GodCast cast)
        {
            if (!cast.Forced && Fighter.Now - _lastGodEnd < GodGap)
            {
                return false;
            }
            return Cinema.TryBegin(cast, cast.Forced ? 0f : AnyGap);
        }

        /// <summary>
        /// Hooks.HoldTechniques: the moment the Brain is about to choose a technique for a God with
        /// a full gauge, the god ability has the first say. It is unleashed right here when the
        /// situation calls for it, and while it waits for its cinematic turn (at most QueueWait)
        /// the God's lesser techniques are held, so the moment is never spent on them.
        /// </summary>
        private static bool HoldForGod(Actor a)
        {
            try
            {
                if (_holders.Count == 0 || a == null || Settings.Cinematics <= 0 || World.world == null)
                {
                    return false;
                }
                Divine d = Of(a);
                if (d == null || !d.Listed || !d.Valid || d.Active || d.PendingSince >= 0 || d.F.Gauge < 100f)
                {
                    return false;
                }
                if (!d.Starfall && !d.Zero)
                {
                    return false;
                }
                if (d.Foes.Count == 0 && d.F.Gauge >= 100f)
                {
                    Scan(d);
                }
                Select(d);
                return d.Active || d.WaitSince >= 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god hold: " + e.Message);
                return false;
            }
        }

        /// <summary>Does any other Hooks.HoldTechniques handler hold this unit (a duel's stare-down)?</summary>
        private static bool HeldByOthers(Actor a)
        {
            List<Func<Actor, bool>> hs = Hooks.HoldTechniques;
            for (int i = 0; i < hs.Count; i++)
            {
                Func<Actor, bool> h = hs[i];
                if (h == null || (object)h == _hold)
                {
                    continue;
                }
                try
                {
                    if (h(a))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Sakuga] god hold check: " + e.Message);
                }
            }
            return false;
        }

        private static bool Dueling(Actor a)
        {
            if (_inDuel == null)
            {
                return false;
            }
            try
            {
                return _inDuel(a);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god gauge: duels: " + e.Message);
                _inDuel = null;
                return false;
            }
        }

        /// <summary>The gauge is full: is there a reason to unleash something, and may the world stop for it?</summary>
        private static void Select(Divine d)
        {
            if (d.SelectFrame == Time.frameCount)
            {
                return;   // already asked this frame (the tick and the Brain's hold both ask)
            }
            d.SelectFrame = Time.frameCount;
            Plan p = Qualify(d);
            if (p == null)
            {
                d.WaitSince = -1;
                return;
            }
            GodCast cast = (GodCast)p.Data;
            if (WantFullFor(d, d.A.current_position, false))
            {
                if (TrySlot(cast))
                {
                    cast.Full = true;
                }
                else if (!d.War)   // a god war never waits for the cinematic slot: it plays light now
                {
                    double now = Fighter.Now;
                    // (no cinematic playing, but the last god ability's ended too recently for the slot
                    // to open within the wait: no point holding the God's techniques, it plays light now)
                    double waited = d.WaitSince < 0 ? 0.0 : now - d.WaitSince;
                    bool hopeless = !Cinema.Playing && GodGap - (now - _lastGodEnd) > QueueWait - waited;
                    if (!hopeless)
                    {
                        if (d.WaitSince < 0)
                        {
                            d.WaitSince = now;
                            if (Dev.On)
                            {
                                Dev.Trace("god " + cast.Kind + " by " + d.A.asset.id + "#" + d.Id + " waits for the cinematic slot (techniques held)");
                            }
                        }
                        if (now - d.WaitSince <= QueueWait)
                        {
                            return;   // wait for the world to be ready
                        }
                    }
                }
            }
            cast.Decided = true;
            d.WaitSince = -1;
            if (!Brain.Execute(d.F, p) && cast.Full)
            {
                Cinema.End(cast);
            }
        }

        /// <summary>What the situation calls for now (null: nothing).</summary>
        private static Plan Qualify(Divine d)
        {
            Fighter f = d.F;
            Actor a = d.A;
            if (f.Gauge < 100f || d.Active || !d.Valid || !Moves.CanPerform(a) || Director.InScene(a))
            {
                return null;
            }
            if (!f.InCombat && !a.isFighting())
            {
                return null;
            }
            // a stare-down, a duel's exchange, a free fighting whose techniques are spent (or any
            // other hold) is never pre-empted; in a duel's free fighting the duel allows it as one
            // of its techniques, and its pacing caps what it takes
            if (HeldByOthers(a))
            {
                return null;
            }
            Actor rival = Dueling(a) ? DuelOpponent(a) : null;
            Actor v = null;
            if (d.Zero && _zero != null && Ready(d, GodKind.ZeroMoment))
            {
                v = ZeroTarget(d);
                if (rival != null && v != rival)
                {
                    v = ZeroWorthy(d, rival) ? rival : null;   // a duelist cuts its opponent, nobody else
                }
            }
            bool star = false;
            if (d.Starfall && _starfall != null && Ready(d, GodKind.Starfall))
            {
                // the crowd as it stands right now (the last look may be half a second old: a crowd
                // the God's own technique just swept away doesn't call for the sky)
                if (d.ScanIn < 0.4f)
                {
                    d.ScanIn = 0.5f;
                    Scan(d);
                }
                // (the guarantee after 30 s of fighting only against a real threat: one dragon is no reason)
                star = d.Points >= 12f || d.PointsAll >= 15f || (d.FullCombat >= 30f && d.PointsAll >= 6f && FoesThreat(d) >= 0.5f)
                    || (d.War && Combat.Living(d.WarFoe) && Combat.Grounded(d.WarFoe));
            }
            bool starOk = star && ZonesClear(d);
            if (v != null && starOk)
            {
                // both called for: a whole army outweighs one foe that is not a colossus; in a god
                // war the two take turns (the duel of the age shows both)
                bool army = d.PointsAll >= 20f && Weights.Of(v) < Weight.Titan;
                // in a god war the two take turns; which comes first is the God's own nature (a
                // swift or cunning God cuts first, a ruinous or protective one calls the sky first)
                bool turn = d.War && (d.LastKind == (int)GodKind.ZeroMoment || d.LastKind < 0 && SkyFirst(d.F));
                return army || turn ? MakePlan(GodKind.Starfall, null, null, false) : MakePlan(GodKind.ZeroMoment, v, null, false);
            }
            if (v != null)
            {
                return MakePlan(GodKind.ZeroMoment, v, null, false);
            }
            return starOk ? MakePlan(GodKind.Starfall, null, null, false) : null;
        }

        /// <summary>How dangerous everyone it sees is to it, together (Threat: 1 an equal).</summary>
        private static float FoesThreat(Divine d)
        {
            float sum = 0f;
            for (int i = 0; i < d.Foes.Count && i < 24; i++)
            {
                if (Combat.Living(d.Foes[i]))
                {
                    sum += Threat.Ratio(d.A, d.Foes[i]);
                }
            }
            return sum;
        }

        /// <summary>A God's first ability in a god war: Starfall first for a ruinous or protective nature (seeded, stable).</summary>
        private static bool SkyFirst(Fighter f)
        {
            Persona p = f.Persona;
            float sky = p.Is(Style.Berserker) || p.Is(Style.Guardian) || p.Is(Style.OutFighter) ? 0.75f
                : p.Is(Style.Trickster) || p.Is(Style.Swarmer) || p.Is(Style.CounterPuncher) ? 0.25f : 0.5f;
            return (p.Seed % 1000) / 1000f < sky;
        }

        private static Actor DuelOpponent(Actor a)
        {
            if (_opponent == null)
            {
                return null;
            }
            try
            {
                return _opponent(a);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god gauge: duels: " + e.Message);
                _opponent = null;
                return null;
            }
        }

        /// <summary>
        /// The colossus or rival Zero Moment answers: a Kaiju or a threatening Titan, a Grandmaster or God, its
        /// sworn rival, or a butcher of our side, within 8 tiles (the strongest, nearer ones first on
        /// a tie).
        /// </summary>
        private static Actor ZeroTarget(Divine d)
        {
            Actor best = null;
            float bestScore = 0f;
            foreach (Actor v in d.Foes)
            {
                float score = ZeroScore(d, v);
                if (score > 0f && (best == null || score > bestScore))
                {
                    best = v;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool ZeroWorthy(Divine d, Actor v)
        {
            return ZeroScore(d, v) > 0f;
        }

        /// <summary>How much Zero Moment wants this foe (0: not a target now).</summary>
        private static float ZeroScore(Divine d, Actor v)
        {
            Actor a = d.A;
            // (Living: a foe another blow just killed is still "alive" until the game's death check)
            if (!Combat.Living(v) || v.asset.is_boat || !Combat.IsFoe(a, v) || !Combat.Allowed(a, v))
            {
                return 0f;
            }
            Tier tier = Tiers.Of(v);
            float dist = Vector2.Distance(a.current_position, v.current_position);
            float range = tier >= Tier.Grandmaster ? ZeroRangeWar : ZeroRange;
            // one in the middle of a technique, or held in someone's juggle, is left alone (it is
            // looked at again a quarter of a second later)
            if (dist > range + v.stats["size"] * 0.5f || InGodMove(v) || Moves.Locked(v) || Director.InScene(v) || v.isInsideSomething())
            {
                return 0f;
            }
            float score = 0f;
            // in proportion to the threat: a colossus is worth the God's ultimate only when it really
            // threatens it (a dragon is a nuisance to a God in health: a flick, not Zero Moment)
            if (Weights.Of(v) >= Weight.Titan && (Weights.Of(v) >= Weight.Kaiju || Threat.Ratio(a, v) >= 0.45f || Combat.HpPct(a) < 0.5f))
            {
                score = 10f + Tiers.Power(v);
            }
            if (tier >= Tier.Grandmaster || (d.F.Rival != 0 && v.Sid() == d.F.Rival))
            {
                score = Mathf.Max(score, 9f + Tiers.Power(v));
            }
            if (Butcher(v, a.kingdom))
            {
                score = Mathf.Max(score, 8f + Tiers.Power(v));
            }
            if (score <= 0f)
            {
                return 0f;
            }
            return Mathf.Max(0.01f, score - dist * 0.2f);
        }

        /// <summary>
        /// A god war: a Grandmaster or God stands against this God within 22 tiles (two powers of that
        /// scale in one battle: its abilities tear the land wider).
        /// </summary>
        public static bool AtWar(Actor a)
        {
            Divine d = Of(a);
            return d != null && d.War && Combat.Living(d.WarFoe);
        }

        /// <summary>Is v in the middle of a cinematic or a god ability of its own (leave it alone)?</summary>
        public static bool InGodMove(Actor v)
        {
            return Moves.TryGet(v, out Move m) && (m.Cinematic || (m.Tech != null && m.Tech.GodAbility));
        }

        /// <summary>
        /// Starfall's rule: where its stars would fall, our own people are at most half the enemies
        /// (no star is ever aimed at them and no flame lit by them, but a sky that only falls on a
        /// melee of friends is not the moment).
        /// </summary>
        private static bool ZonesClear(Divine d)
        {
            // asked several times a second while a God waits for its turn: the answer holds 0.25 s
            double now = Fighter.Now;
            if (now - d.ZonesAt < 0.25 && now >= d.ZonesAt)
            {
                return d.ZonesOk;
            }
            d.ZonesAt = now;
            d.ZonesOk = ZonesClearNow(d);
            return d.ZonesOk;
        }

        private static bool ZonesClearNow(Divine d)
        {
            _ground.Clear();
            for (int i = 0; i < d.Foes.Count; i++)
            {
                Actor u = d.Foes[i];
                // (one mid-technique counts too: the sky falls on it all the same)
                if (u != null && u.isAlive() && Combat.Grounded(u) && !Godhood.InGodMove(u))
                {
                    _ground.Add(u);
                }
            }
            if (_ground.Count == 0)
            {
                return false;
            }
            int n = Sakuga.Starfall.StarCount(_ground.Count);
            float k = Mathf.Clamp(Mathf.Sqrt(Mathf.Clamp(d.A.actor_scale / 0.1f, 0.6f, 4f)), 0.8f, 1.5f);
            Sakuga.Starfall.PlanStrikes(_ground, d.Allies, n, Sakuga.Starfall.MeteorRadius * k, _points, null, out int alliesIn);
            return _points.Count > 0 && alliesIn <= 0.5f * Mathf.Max(1, d.CrowdAll);
        }

        private static readonly List<Actor> _ground = new List<Actor>();

        private static Plan MakePlan(GodKind kind, Actor target, Vector2? point, bool forced)
        {
            Technique t = TechOf(kind);
            if (t == null)
            {
                return null;
            }
            GodCast cast = new GodCast { Kind = kind, Target = target, Point = point, Forced = forced };
            return new Plan
            {
                Tech = t,
                Level = 1,
                Target = target,
                Point = point ?? (target != null ? target.current_position : Vector2.zero),
                Score = 1e4f,
                Count = 1,
                Data = cast
            };
        }

        // ------------------------------------------------ technique entry --

        /// <summary>
        /// Technique.Plan for the god abilities. The Brain never asks (it skips them); god powers do
        /// (Unleash: only when the gauge has chosen this ability), and so do dev commands (Forced:
        /// any time, on the forced target).
        /// </summary>
        public static Plan PlanFor(Technique t, GodKind kind, Ctx c)
        {
            if (c == null || c.F == null || c.Self == null || Settings.Cinematics <= 0)
            {
                return null;
            }
            if (c.Forced)
            {
                return Forced(kind, c);
            }
            if (kind == GodKind.LastLight)
            {
                return null;   // it answers death, nothing else
            }
            Divine d = Of(c.F);
            if (d == null)
            {
                return null;
            }
            if (d.Foes.Count == 0)
            {
                Scan(d);
            }
            Plan p = Qualify(d);
            return p != null && p.Tech == t ? p : null;
        }

        private static Plan Forced(GodKind kind, Ctx c)
        {
            Actor a = c.Self;
            switch (kind)
            {
                case GodKind.ZeroMoment:
                    {
                        Actor v = c.ForceTarget ?? c.LinkTarget ?? c.Target;
                        if (v == null || !Combat.IsFoe(a, v) || v.asset.is_boat)
                        {
                            v = null;
                            for (int i = 0; i < c.Enemies.Count; i++)
                            {
                                Actor u = c.Enemies[i];
                                if (c.EnemyDist[i] <= 14f && !u.asset.is_boat && Combat.IsFoe(a, u) && Combat.Allowed(a, u) && !InGodMove(u) && !Moves.Locked(u))
                                {
                                    v = u;
                                    break;
                                }
                            }
                        }
                        if (!Combat.Living(v) || v.asset.is_boat || !Combat.Allowed(a, v) || InGodMove(v) || Moves.Locked(v))
                        {
                            return null;
                        }
                        return MakePlan(kind, v, null, true);
                    }
                case GodKind.LastLight:
                    return MakePlan(kind, null, null, true);
                default:
                    {
                        Vector2? at = c.ForcePoint;
                        if (!at.HasValue && c.ForceTarget != null)
                        {
                            at = c.ForceTarget.current_position;
                        }
                        return MakePlan(kind, null, at, true);
                    }
            }
        }

        /// <summary>
        /// Technique.Begin for the god abilities: settles the staging (if Select didn't), starts the
        /// ability's move and does the bookkeeping (gauge, cooldown, the title card, the camera).
        /// </summary>
        public static bool Begin(Technique t, GodKind kind, Actor a, Plan p)
        {
            if (a == null || p == null || Settings.Cinematics <= 0)
            {
                return false;
            }
            // Last Light rises from anything a God may be in when the blow comes (even stunned)
            if (kind == GodKind.LastLight ? !LastLight.CanRise(a) : !Moves.CanPerform(a))
            {
                return false;
            }
            GodCast cast = p.Data as GodCast;
            if (cast == null)
            {
                cast = new GodCast { Kind = kind, Target = p.Target };
                p.Data = cast;
            }
            cast.Kind = kind;
            if (kind == GodKind.ZeroMoment && !Combat.Living(cast.Target))
            {
                return false;
            }
            Fighter f = Fighters.Get(a);
            Divine d = Of(f);
            if (Settings.CameraFollow)
            {
                Focus(a, cast, d);
            }
            if (!cast.Decided)
            {
                cast.Full = WantFullFor(d, a.current_position, cast.Forced) && TrySlot(cast);
                cast.Decided = true;
            }
            bool ok;
            try
            {
                switch (kind)
                {
                    case GodKind.ZeroMoment:
                        ok = ZeroMoment.Start(a, cast, t);
                        break;
                    case GodKind.LastLight:
                        ok = LastLight.Start(a, cast, t);
                        break;
                    default:
                        ok = Sakuga.Starfall.Start(a, cast, t);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] god ability " + kind + ": " + e);
                ok = false;
            }
            if (!ok)
            {
                if (cast.Full)
                {
                    Cinema.End(cast);
                }
                return false;
            }
            d.Active = true;
            d.WaitSince = -1;
            d.AbilityKills.Clear();
            if (cast.Full && d.War)
            {
                _warFullAt = Fighter.Now;   // the war has had its cinematic: from now on it plays on
            }
            if (kind != GodKind.LastLight)
            {
                f.Gauge = 0f;
                f.GaugeFullSince = -1;
                d.FullCombat = 0f;
                d.LastKind = (int)kind;
            }
            else if (cast.Forced)
            {
                Spend(d);
            }
            f.Mark(TimerOf(kind));
            Casts++;
            Witness(kind, cast, a);
            if (Dev.On)
            {
                Dev.Trace("god " + kind + " by " + a.asset.id + "#" + a.Sid() + (cast.Full ? " FULL" : " light")
                    + (cast.Target != null ? " -> " + cast.Target.asset.id + "#" + cast.Target.Sid() : "") + (cast.Forced ? " (forced)" : "")
                    + (d.War ? " (god war)" : "") + (Dueling(a) ? " (in a duel)" : ""));
            }
            return true;
        }

        /// <summary>The ability's move ended (any way): the God is an ordinary fighter again.</summary>
        public static void Ended(Move m, bool full)
        {
            if (full)
            {
                _lastGodEnd = Fighter.Now;   // the next god ability's freeze keeps its distance
            }
            if (m == null || !_all.TryGetValue(m.Id, out Divine d) || d.A != m.A)
            {
                return;
            }
            d.Active = false;
            d.HitMove = null;
            d.FireproofUntil = Fighter.Now + FireproofSeconds;
        }

        /// <summary>World seconds after a god ability during which its own fire parts for the God.</summary>
        private const double FireproofSeconds = 8.0;

        // ---------------------------------------------------------- staging --

        /// <summary>May a full cinematic (freeze, veil) play for a God standing here?</summary>
        public static bool WantFull(Vector2 at)
        {
            if (Settings.Cinematics < 2 || World.world == null || Config.time_scale_asset == null || Config.time_scale_asset.multiplier > 5f)
            {
                return false;
            }
            return Follows() || Cinema.FullAt(at);
        }

        /// <summary>
        /// Will Camera Follow bring the God into view? Not while the player follows a unit of its own
        /// (that is its camera), and not zoomed far out (the zoom is the player's: Cinema.FullAt's limit).
        /// </summary>
        private static bool Follows()
        {
            if (!Settings.CameraFollow || MoveCamera.hasFocusUnit())
            {
                return false;
            }
            Camera cam = World.world.camera;
            return cam != null && cam.orthographicSize <= 90f;
        }

        /// <summary>
        /// WantFull for this God: in a god war, once the war has had one full cinematic, the rest play
        /// light for WarFullGap (the war never stops for long). Ordered ones (dev, god powers) are exempt.
        /// </summary>
        private static bool WantFullFor(Divine d, Vector2 at, bool forced)
        {
            if (!WantFull(at))
            {
                return false;
            }
            return forced || d == null || !d.War || Fighter.Now - _warFullAt >= WarFullGap;
        }

        /// <summary>
        /// Camera Follow: the camera jumps to the God (only its position: the zoom stays the player's,
        /// and a unit the player follows keeps its camera).
        /// </summary>
        private static void Focus(Actor a, GodCast cast, Divine d)
        {
            try
            {
                MoveCamera mc = World.world != null ? World.world.move_camera : null;
                if (mc == null || MoveCamera.hasFocusUnit())
                {
                    return;
                }
                Vector2 at = a.current_position;
                if (cast.Kind == GodKind.ZeroMoment && cast.Target != null)
                {
                    at = Vector2.Lerp(at, cast.Target.current_position, 0.5f);
                }
                else if (cast.Kind == GodKind.Starfall)
                {
                    // the rain falls on the army it saw: frame the God and the near side of the field
                    if (d != null && d.Foes.Count > 0)
                    {
                        Vector2 sum = Vector2.zero;
                        int n = 0;
                        foreach (Actor u in d.Foes)
                        {
                            if (u != null && u.isAlive())
                            {
                                sum += u.current_position;
                                n++;
                            }
                        }
                        if (n > 0)
                        {
                            at = Vector2.Lerp(at, sum / n, 0.4f);
                        }
                    }
                    at.y += 3.5f;   // it rises: keep the sky in view
                }
                // (not MoveCamera.focusOn: it zooms to 15 and drops the player's follow-unit mode)
                Transform tr = mc.transform;
                tr.position = new Vector3(at.x, at.y + 1f, tr.position.z);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god camera: " + e.Message);
            }
        }

        /// <summary>The first time each ability is seen this session, its name in big gold letters.</summary>
        private static void Witness(GodKind kind, GodCast cast, Actor a)
        {
            int i = (int)kind;
            Vector2 at = a.current_position;
            if (cast.Target != null)
            {
                at = Vector2.Lerp(at, cast.Target.current_position, 0.5f);
            }
            if (_seen[i] || !Fx.OnScreen(at, 0.1f))
            {
                return;
            }
            _seen[i] = true;
            string title = kind == GodKind.ZeroMoment ? "ZERO MOMENT" : kind == GodKind.LastLight ? "LAST LIGHT" : "STARFALL";
            Cinema.Title(title, GodFx.Gold, 1.5f, at, 1f);
        }

        // ------------------------------------------------- Last Light aftermath --

        /// <summary>Last Light's burst: 12 s Awakened (cooldowns reset), and HP climbs to 60% over a second.</summary>
        public static void Awaken(Actor a, float seconds, float heal)
        {
            if (a == null || !a.isAlive() || !Fighters.TryGet(a, out Fighter f))
            {
                return;
            }
            f.AwakenedUntil = Fighter.Now + seconds;
            f.ResetCooldowns();
            Divine d = Of(f);
            int max = Mathf.Max(1, a.getMaxHealth());
            int to = Mathf.RoundToInt(max * heal);
            if (a.getHealth() < to)
            {
                d.HealAt = Fighter.Now;
                d.HealFrom = a.getHealth();
                d.HealTo = to;
            }
        }

        private static void Heal(Divine d, double now)
        {
            if (d.HealAt < 0)
            {
                return;
            }
            float k = Mathf.Clamp01((float)(now - d.HealAt) / 1f);
            int want = Mathf.RoundToInt(Mathf.Lerp(d.HealFrom, d.HealTo, k));
            int hp = d.A.getHealth();
            if (hp < want && hp > 0)
            {
                d.A.restoreHealth(want - hp);
            }
            if (k >= 1f)
            {
                d.HealAt = -1;
            }
        }

        /// <summary>Awakened techniques cool down twice as fast.</summary>
        private static void OnTechnique(Fighter f, Plan p)
        {
            if (f == null || p == null || p.Tech == null || p.Tech.GodAbility || Fighter.Now >= f.AwakenedUntil)
            {
                return;
            }
            f.SetCooldown(p.Tech, p.Tech.CooldownFor(f.A, Mathf.Max(1, p.Level)) * 0.5f);
        }

        /// <summary>A God that used its Last Light died anyway: the world hears of it, and its allies fight on harder.</summary>
        private static void Fall(Divine d)
        {
            if (d.Fallen || d.SpentAt < 0 || Fighter.Now - d.SpentAt > CooldownLast)
            {
                return;
            }
            d.Fallen = true;
            string name = string.IsNullOrEmpty(d.Name) ? "?" : d.Name;
            try
            {
                string text = d.WasGod ? Locale.Format("sakuga_god_fallen", name) : Locale.Format("sakuga_fallen", name);
                WorldTip.showNow(text, false, "top", 4f, "#FFD35A");
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] fallen: " + e);
            }
            if (d.Side == null)
            {
                return;
            }
            double now = Fighter.Now;
            foreach (Fighter g in Fighters.All)
            {
                if (!g.Valid || g.Tier == Tier.None || g.A == d.A || g.A.kingdom != d.Side)
                {
                    continue;
                }
                if ((g.A.current_position - d.Pos).sqrMagnitude > 20f * 20f)
                {
                    continue;
                }
                Situations.Boost(g, 0.25f, 30.0);
                if (g.A.is_visible)
                {
                    float px = Mathf.Clamp(g.A.actor_scale, 0.04f, 0.4f);
                    Kit.Flare(g.A.current_position, px, Tiers.Crimson, 13f, 0.45f);
                    Kit.Mark(g.A, GodFx.Gold, 0.4f, 0.9f);
                }
            }
        }

        // -------------------------------------------------------- presence --

        private static void Presence(Divine d, float dt, bool on)
        {
            Actor a = d.A;
            if (!Settings.Auras || !a.is_visible || d.Active || !MapBox.isRenderGameplay())
            {
                return;
            }
            float px = Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
            if (d.F.Tier == Tier.God && !Settings.Aura[(int)Tier.God])
            {
                // (with the God aura on, its own embers already rise off the God)
                d.EmberIn -= dt;
                if (d.EmberIn <= 0f)
                {
                    d.EmberIn = 1.5f;
                    Vector2 body = GodFx.Body(a);
                    Kit.Ember(body + new Vector2(Random.Range(-3f, 3f), Random.Range(-1f, 4f)) * px, px, Random.Range(1.2f, 1.6f),
                        new Vector2(Random.Range(-2f, 2f), Random.Range(8f, 12f)) * px, GodFx.Gold);
                }
            }
            if (on && d.F.Gauge >= 100f)
            {
                d.PulseIn -= dt;
                if (d.PulseIn <= 0f)
                {
                    d.PulseIn = 2f;
                    // only when something can actually come (an ability off cooldown)
                    if ((d.Starfall && Ready(d, GodKind.Starfall)) || (d.Zero && Ready(d, GodKind.ZeroMoment)))
                    {
                        GodFx.Outline(a, GodFx.Gold, 0.45f);
                    }
                }
            }
        }

        /// <summary>Awakened: gold afterimages while it moves, an ember aura, a gold puff when it ends.</summary>
        private static void Awakened(Divine d, float dt, double now)
        {
            Actor a = d.A;
            bool awake = now < d.F.AwakenedUntil;
            if (!awake)
            {
                if (d.AwakeFx)
                {
                    d.AwakeFx = false;
                    if (a.is_visible)
                    {
                        Puff(a);
                    }
                }
                return;
            }
            d.AwakeFx = true;
            if (!a.is_visible || !MapBox.isRenderGameplay())
            {
                return;
            }
            float px = Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
            bool moving = (d.Pos - d.PrevPos).sqrMagnitude > 0.0004f || a.position_height > 0.05f;
            d.AfterIn -= dt;
            if (moving && d.AfterIn <= 0f)
            {
                d.AfterIn = 0.15f;
                GodFx.Clone(a, FxLayer.Behind, 0, true, 0.32f, Vector2.zero, false)?
                    .Color(new Color(1f, 0.9f, 0.5f, 0.7f), new Color(1f, 0.7f, 0.2f, 0.35f), new Color(1f, 0.55f, 0.1f, 0f));
            }
            d.AuraIn -= dt;
            if (d.AuraIn <= 0f)
            {
                d.AuraIn = 0.09f;
                Vector2 body = GodFx.Body(a);
                Kit.Ember(body + new Vector2(Random.Range(-4f, 4f), Random.Range(-4f, 3f)) * px, px, Random.Range(0.4f, 0.65f),
                    new Vector2(Random.Range(-3f, 3f), Random.Range(12f, 22f)) * px, Random.value < 0.3f ? GodFx.GoldLight : GodFx.Gold);
            }
        }

        private static void Puff(Actor a)
        {
            float px = Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
            Vector2 feet = a.current_position;
            Vector2 body = GodFx.Body(a);
            Fx.Ring(feet, px, 0.35f, 3f, 14f, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(GodFx.GoldLight, 0.9f), Fx.WithA(GodFx.Gold, 0f));
            int n = Fx.Count(6);
            for (int i = 0; i < n; i++)
            {
                float ang = (i + Random.value * 0.5f) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], body + dir * 2f * px, px, Random.Range(0.4f, 0.55f), FxLayer.Top, 6);
                p?.Moving(dir * Random.Range(14f, 24f) * px, 4f * px, 3.5f).Sized(0.5f, 1.1f)
                    .Color(Fx.WithA(GodFx.GoldLight, 0.85f), Fx.WithA(GodFx.Gold, 0.5f), Fx.WithA(GodFx.Gold, 0f));
            }
            Fx.Glint(body + new Vector2(0f, 3f * px), px, 0.25f, GodFx.GoldLight, 0.9f);
        }
    }

    /// <summary>
    /// What the god abilities do to the world (Kit.Ravage, the "Environment damage" setting):
    /// craters, trenches and fire, sparing the God's own people (where one stands, a crater
    /// without flames) and its own footing (it never ends a god ability standing in its own fire).
    /// </summary>
    internal static class GodRuin
    {
        private static readonly List<Actor> _near = new List<Actor>();
        private static readonly List<Actor> _foes = new List<Actor>();

        /// <summary>
        /// A crater of 'r' tiles at 'at' with 'force' (Kit.Ravage). Flames (force 3 and up) only
        /// away from the God's people ('allies', or gathered here when null) and from 'keep': near
        /// them only an inner crater burns and the rest is blasted without fire (force 2).
        /// </summary>
        public static void Crater(Actor god, Vector2 at, float r, int force, List<Actor> allies, Vector2? keep = null)
        {
            if (force <= 0 || Settings.Destruction <= 0 || World.world == null)
            {
                return;
            }
            if (force <= 2)
            {
                Kit.Ravage(at, r, force, god);
                return;
            }
            float near = Nearest(god, at, r + 2f, allies, keep);
            if (near > r + 1.5f)
            {
                Kit.Ravage(at, r, force, god);
                return;
            }
            float inner = near - 2f;
            if (inner >= 1.5f)
            {
                Kit.Ravage(at, inner, force, god);
            }
            Kit.Ravage(at, r, 2, god);
        }

        /// <summary>
        /// A trench from a to b: craters of 'width' tiles every 'width' along it. No flames by the
        /// God's people or 'keep', and nothing at all within 'keepR' of 'keep' (the God's footing).
        /// </summary>
        public static void Trench(Actor god, Vector2 a, Vector2 b, float width, int force, List<Actor> allies, Vector2 keep, float keepR)
        {
            if (force <= 0 || Settings.Destruction <= 0 || World.world == null)
            {
                return;
            }
            float len = Vector2.Distance(a, b);
            int n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(0.8f, width)), 1, 24);
            float keep2 = keepR * keepR;
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Combat.Clamp(Vector2.Lerp(a, b, i / (float)n));
                    if ((p - keep).sqrMagnitude < keep2)
                    {
                        continue;
                    }
                    int f = force;
                    if (f >= 3 && Nearest(god, p, width + 1.5f, allies, keep) <= width + 1.5f)
                    {
                        f = 2;
                    }
                    Kit.Ravage(p, width, f, god);
                }
            }
            finally
            {
                Kit.EndTrench(god);
            }
        }

        /// <summary>The nearest of the God's people (or 'keep') to 'at' within 'r' tiles (MaxValue: none).</summary>
        private static float Nearest(Actor god, Vector2 at, float r, List<Actor> allies, Vector2? keep)
        {
            List<Actor> list = allies;
            if (list == null)
            {
                Godhood.Gather(god, at, r, _foes, _near, false);
                list = _near;
            }
            float best = float.MaxValue;
            float r2 = r * r;
            for (int i = 0; i < list.Count; i++)
            {
                Actor u = list[i];
                if (u == null || !u.isAlive())
                {
                    continue;
                }
                float d2 = (u.current_position - at).sqrMagnitude;
                if (d2 <= r2 && d2 < best * best)
                {
                    best = Mathf.Sqrt(d2);
                }
            }
            if (keep.HasValue)
            {
                float dk = Vector2.Distance(keep.Value, at);
                if (dk <= r && dk < best)
                {
                    best = dk;
                }
            }
            if (list == _near)
            {
                _near.Clear();
                _foes.Clear();
            }
            return best;
        }

        /// <summary>Puts out the fire within 'r' tiles of 'at'.</summary>
        public static void Calm(Vector2 at, float r)
        {
            WorldTile c = Combat.TileAt(at);
            if (c == null || World.world == null)
            {
                return;
            }
            int ri = Mathf.CeilToInt(r);
            float r2 = r * r;
            for (int dx = -ri; dx <= ri; dx++)
            {
                for (int dy = -ri; dy <= ri; dy++)
                {
                    if (dx * dx + dy * dy > r2)
                    {
                        continue;
                    }
                    int x = c.x + dx, y = c.y + dy;
                    if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
                    {
                        continue;
                    }
                    WorldTile t = World.world.GetTileSimple(x, y);
                    if (t != null && t.isOnFire())
                    {
                        t.stopFire();
                    }
                }
            }
        }

        /// <summary>
        /// A Master or better thrown across the God's burning ground lands where the flames are put
        /// out: a fighter of that class is shaken by the ruin, never left running from its fire.
        /// (Masters and up are fire-immune now: for them the ruin keeps burning; this only shelters a
        /// tiered unit that somehow is not.)
        /// </summary>
        public static void Shelter(Actor u, Vector2 from, float distance)
        {
            if (u == null || !u.isAlive() || Tiers.Of(u) < Tier.Master || World.world == null || u.isImmuneToFire())
            {
                return;
            }
            Vector2 p = u.current_position;
            Calm(p, 1f);
            Vector2 dir = p - from;
            if (distance > 0f && dir.sqrMagnitude > 0.0004f)
            {
                Calm(p + dir.normalized * distance, 1.3f);
            }
        }

        /// <summary>The God stands up out of its own ruin: no fire at its feet, and no flame clings to it.</summary>
        public static void Settle(Actor god, Vector2 feet)
        {
            if (god == null || World.world == null)
            {
                return;
            }
            try
            {
                Calm(feet, 1.6f);
                if (god.isAlive() && god.hasStatus("burning"))
                {
                    god.finishStatusEffect("burning");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sakuga] god ruin: " + e.Message);
            }
        }

        /// <summary>The attacker's tier Might times Settings.Power (what Combat.Hit multiplies every blow by).</summary>
        public static float Might(Actor a)
        {
            TierInfo ti = Tiers.Info(Tiers.Of(a));
            return Mathf.Max(0.1f, (ti != null ? ti.Might : 1f) * Settings.Power);
        }

        /// <summary>
        /// The end of a god ability: it looks at the battlefield at once (no dead air). A Flow tier
        /// (Master and up) fights on with techniques: no ordinary attack cooldown to stall it.
        /// </summary>
        public static void AfterMove(Actor a, bool swingCooldown = true)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            if (swingCooldown && Tiers.Of(a) < Tier.Master)
            {
                a.startAttackCooldown();
            }
            if (Fighters.TryGet(a, out Fighter f))
            {
                f.ThinkIn = 0f;   // it looks at the battlefield the moment it lands, no dead air
            }
        }
    }

    /// <summary>
    /// Drawing helpers the god abilities share: the God's gold, effects that keep playing while the
    /// world is held (Particle.Cine), effects lifted above the cinematic veil, and copies of a
    /// unit's sprite (clones above the veil, outline pulses, flashes) glued to where it is drawn.
    /// </summary>
    internal static class GodFx
    {
        /// <summary>#FFD35A, the Combat God's own colour.</summary>
        public static readonly Color Gold = new Color(1f, 0.827f, 0.353f, 1f);
        /// <summary>#FFF4C2.</summary>
        public static readonly Color GoldLight = new Color(1f, 0.957f, 0.761f, 1f);
        public static readonly Color GoldDeep = new Color(0.86f, 0.52f, 0.1f, 1f);
        public static readonly Color WhiteHot = new Color(1f, 0.99f, 0.94f, 1f);
        /// <summary>#2A0006, deep crimson.</summary>
        public static readonly Color Crimson = new Color(0.165f, 0f, 0.024f, 1f);
        public static readonly Color DeepRed = new Color(0.6f, 0.03f, 0.07f, 1f);

        private static List<Particle> _live;
        private static bool _looked;
        private static int _top = int.MinValue;
        private static Sprite _head;

        /// <summary>The particle engine's live list (Fx keeps it private): read once, by reflection.</summary>
        private static List<Particle> Live()
        {
            if (!_looked)
            {
                _looked = true;
                try
                {
                    FieldInfo fi = typeof(Fx).GetField("_live", BindingFlags.NonPublic | BindingFlags.Static);
                    _live = fi != null ? fi.GetValue(null) as List<Particle> : null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Sakuga] god fx: no live list: " + e.Message);
                    _live = null;
                }
            }
            return _live;
        }

        /// <summary>Marks the particles spawned from now on (Kit and Fx helpers don't hand them back).</summary>
        public static int Mark()
        {
            List<Particle> l = Live();
            return l != null ? l.Count : 0;
        }

        /// <summary>Everything spawned since 'mark' keeps moving while the world is held.</summary>
        public static void Cine(int mark)
        {
            List<Particle> l = Live();
            if (l == null)
            {
                return;
            }
            for (int i = Mathf.Max(0, mark); i < l.Count; i++)
            {
                l[i].Cine = true;
            }
        }

        /// <summary>Everything spawned since 'mark' is drawn above the veil (and keeps moving while the world is held).</summary>
        public static void Lift(int mark)
        {
            List<Particle> l = Live();
            if (l == null)
            {
                return;
            }
            for (int i = Mathf.Max(0, mark); i < l.Count; i++)
            {
                Lift(l[i]);
            }
        }

        /// <summary>Draws one effect above the veil, keeping its order among the other lifted ones.</summary>
        public static Particle Lift(Particle p)
        {
            if (p == null || p.R == null)
            {
                return p;
            }
            if (_top == int.MinValue)
            {
                _top = SortingLayer.NameToID("EffectsTop");
            }
            p.Cine = true;
            if (p.R.sortingLayerID == _top && p.R.sortingOrder >= 1000)
            {
                return p;   // already there
            }
            int o = p.R.sortingOrder;
            p.R.sortingLayerID = _top;
            p.R.sortingOrder = 1000 + Mathf.Clamp(o, -60, 150);
            return p;
        }

        /// <summary>Where a unit's body is (the middle of its sprite), posed or not.</summary>
        public static Vector2 Body(Actor a)
        {
            Sprite s = Fx.UnitSprite(a);
            float up = s != null ? Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) * a.actor_scale : Mathf.Max(0.3f, a.stats["size"] * 0.45f);
            return a.current_position + new Vector2(0f, a.position_height + up);
        }

        /// <summary>
        /// A copy of the unit exactly as the game draws it now (sprite, flip, rotation, squash):
        /// flat colour ('silhouette') or its real colours. 'glue': it follows the unit every frame.
        /// </summary>
        public static Particle Clone(Actor a, FxLayer layer, int order, bool silhouette, float life, Vector2 offset, bool glue)
        {
            if (a == null)
            {
                return null;
            }
            Sprite s = Fx.UnitSprite(a);
            if (s == null)
            {
                return null;
            }
            Particle p = Fx.Spawn(s, Vector2.zero, 1f, life, layer, order, silhouette);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            Place(p, a, offset);
            if (glue)
            {
                long id = a.Sid();
                p.Step = (q, d) =>
                {
                    if (a.Sid() == id)
                    {
                        Place(q, a, offset);
                    }
                    else
                    {
                        q.Kill();
                    }
                };
            }
            return p;
        }

        /// <summary>Puts a clone where the game draws the unit (ActorManager: position + jump offset + shake + height).</summary>
        public static void Place(Particle p, Actor a, Vector2 offset)
        {
            Vector2 pos = a.current_position + a.move_jump_offset + a.shake_offset;
            p.Pos = new Vector2(pos.x + offset.x, pos.y + a.position_height + offset.y);
            p.Z = 0f;
            p.Euler = a.current_rotation;
            p.SX = a.current_scale.x;
            p.SY = a.current_scale.y;
            Sprite s = Fx.UnitSprite(a);
            if (s != null && p.R != null && (object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
        }

        /// <summary>Fx.Glue with an offset in world units (outline copies one pixel out).</summary>
        public static void GlueOff(Particle p, Move m, Color c, Vector2 offset)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Fx.Glue(p, m, c);
            if (offset != Vector2.zero)
            {
                p.Pos += offset;
                p.Tr.position = new Vector3(p.Pos.x, p.Pos.y, -0.02f);
            }
        }

        /// <summary>A one-pixel outline flash around a unit (four silhouettes behind it, one pixel out each way).</summary>
        public static void Outline(Actor a, Color c, float life)
        {
            float px = a.actor_scale;
            for (int i = 0; i < 4; i++)
            {
                Vector2 off = i == 0 ? new Vector2(px, 0f) : i == 1 ? new Vector2(-px, 0f) : i == 2 ? new Vector2(0f, px) : new Vector2(0f, -px);
                Clone(a, FxLayer.Behind, -2, true, life, off, true)?.Color(Fx.WithA(c, 0.95f), Fx.WithA(c, 0.7f), Fx.WithA(c, 0f));
            }
        }

        /// <summary>A falling star's head: a 3x3 white-hot core with a soft one-pixel rim.</summary>
        public static Sprite MeteorHead
        {
            get
            {
                if (_head == null)
                {
                    Canvas c = new Canvas(5, 5);
                    for (int y = 1; y <= 3; y++)
                    {
                        for (int x = 1; x <= 3; x++)
                        {
                            c.Set(x, y, 1f, 1f);
                        }
                    }
                    c.Set(2, 0, 1f, 0.5f);
                    c.Set(2, 4, 1f, 0.5f);
                    c.Set(0, 2, 1f, 0.5f);
                    c.Set(4, 2, 1f, 0.5f);
                    _head = c.ToSprite("sakuga_meteor_head", new Vector2(0.5f, 0.5f));
                }
                return _head;
            }
        }
    }
}
