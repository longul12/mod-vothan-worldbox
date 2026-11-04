using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BATTLE SITUATIONS: what makes each fight a story. The same two armies never fight the same
    /// battle twice, because what happens depends on who is where, who falls and who is watching:
    ///
    ///   LAST STAND   a tiered fighter cut off and surrounded plants its feet and burns: +30% damage,
    ///                thinks twice as fast, half the pause between techniques       (SituationLastStand)
    ///   BODYGUARD    an Expert+ blinks in front of an ally a single-target technique is about to hit,
    ///                crosses its arms and takes it: the technique is parried         (SituationBodyguard)
    ///   CHAMPIONS    in a big battle each side's strongest becomes its champion; the two seek each
    ///                other through the crowd; when one falls its side wavers         (SituationChampions)
    ///   RESCUE       a Master+ sees a weak ally about to die and flash-steps in       (SituationRescue)
    ///   ROUT         a Grandmaster or God killing three in a second breaks the enemy line (SituationKills)
    ///   UNDERDOG     a nobody fells a Master: the world notices                       (SituationKills)
    ///   VENGEANCE    a fighter whose comrade falls beside it swears vengeance on the killer (SituationKills)
    ///   LESSON       a Novice or Adept who watches its master kill with a technique tries it at once
    ///   CHALLENGE    a lone Master+ steps out of the line and points at the enemy's strongest, who
    ///                answers                                                         (SituationChallenge)
    ///
    /// Tempo: a situation is a beat inside the fight, never a pause in it. Every pose is shorter the
    /// stronger its unit (Pace: the tier's TierInfo.Linger, the same tempo as its techniques'
    /// recoveries: Masters and up live it in about half the time), and when it ends the unit goes
    /// straight back to fighting (Resume: its Brain looks at once, a Master or better without
    /// waiting out the pause between techniques; a bodyguard that is a Master or better opens a
    /// chain window onto the one it just stopped, when it has a technique ready to take it: the
    /// parry flows into its counter).
    ///
    /// Flow: Masters and up spend most of a fight in light moves (flow strikes, blur dashes) that
    /// any technique may cut short. For situations they are free (Moves.Committed, not Moves.Busy):
    /// they are looked at, steered, chosen as avengers, rescuers and guards, and a situation's pose
    /// takes over from the light move at once (CanAct). Only the champion's roar waits for the
    /// move that won it (the finisher plays out first). Nothing starts a pose on a duelist its
    /// duel is waiting for (Duels.Waiting).
    ///
    /// The world: the big moments mark the ground for real (Kit.Ravage, the same on or off screen):
    /// a Master's last stand scorches the ground at its feet, a Grandmaster's or a God's cracks it
    /// open; a Grandmaster's victory roar scorches, a God's craters; a God's rout burns a swath
    /// through the line that breaks, spreading with the front of its pressure ring.
    ///
    /// Cost: nothing at all for fighters that are not fighting. Fighters in a fight are looked at
    /// round-robin (one chunk scan each, at most ~16 a second in total, each at most every 0.6 s);
    /// battles are looked for once every 2 s; everything else answers events (kills, blows) and
    /// works on the next frame, outside of whatever raised the event. Active situations tick once a
    /// frame (there are only ever a handful).
    ///
    /// Safety: a situation only starts a move on a unit that can perform (Moves.CanPerform) and is not
    /// in a Director scene or awaited by its duel (CanAct); it only steers units that are free;
    /// every move ends on its own; glows end
    /// when their unit dies; the slow-motion beat is ended by its own frame clock, not by this tick; and
    /// Hooks.WorldCleared drops everything. A unit a blow has already killed (no health left, not yet
    /// dead to the game) is nobody's ally, foe, guard or rescuer any more (Combat.Living); a unit
    /// sheltering in a building or a boat, or in the god hand, is not on the field (Afield).
    /// </summary>
    internal static class Situations
    {
        /// <summary>Master switch (for a setting the integrator may add).</summary>
        public static bool Enabled = true;

        private static int _frame = -1;
        private static int _mainThread = -1;
        private static bool _contactHooked;
        private static float _refresh;
        private static float _scanBudget;
        private static int _cursor;
        private static double _prune;
        private static double _tipAt = -999;

        /// <summary>Tiered fighters by rank, refreshed twice a second (no map search needed to find a guard or a rescuer).</summary>
        internal static readonly List<Fighter> Tiered = new List<Fighter>();
        internal static readonly List<Fighter> Guards = new List<Fighter>();
        internal static readonly List<Fighter> Masters = new List<Fighter>();

        private static readonly Dictionary<long, double> _nextLook = new Dictionary<long, double>();
        private static readonly List<Actor> _units = new List<Actor>();

        /// <summary>Who is busy with a situation that steers it (champion, challenge, vengeance, rescue).</summary>
        private static readonly Dictionary<long, object> _roles = new Dictionary<long, object>();

        // ------------------------------------------------------------------ init --

        [SakugaInit]
        private static void Init()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            Hooks.FighterTick += Tick;
            Hooks.Killed += SitKills.OnKilled;
            Hooks.HitDealt += SitKills.OnHit;
            Hooks.MoveEnded += OnMoveEnded;
            Hooks.WorldCleared += Clear;
            Hooks.BeforeDamage.Add(SitRescue.OnBeforeDamage);
            Hooks.HoldTechniques.Add(HoldStaged);
        }

        internal static bool MainThread => Thread.CurrentThread.ManagedThreadId == _mainThread;

        private static void Tick(Fighter f, float dt)
        {
            if (Time.frameCount == _frame)
            {
                return;
            }
            _frame = Time.frameCount;
            try
            {
                if (!_contactHooked)
                {
                    // the bodyguard answers last: a unit's own parry, a duel's deflection come first
                    _contactHooked = true;
                    Combat.ContactHandlers.Add(SitBodyguard.Answer);
                }
                if (!Enabled)
                {
                    if (Any)
                    {
                        Clear();
                    }
                    return;
                }
                Frame(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("situations", e);
            }
        }

        private static bool Any => SitLastStand.Count > 0 || SitChampions.Count > 0 || _roles.Count > 0 || SitKills.Pending > 0 || _staging.Count > 0 || _boosts.Count > 0;

        /// <summary>Once a frame (game running), from the first fighter that ticks.</summary>
        private static void Frame(float dt)
        {
            double now = Fighter.Now;
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 0.5f;
                Refresh();
            }
            if (_staging.Count > 0)
            {
                Staged(now);
            }
            SitKills.Process(now);
            SitRescue.Process(now);
            SitLastStand.Update(dt, now);
            SitChampions.Update(dt, now);
            SitRescue.Update(now);
            SitKills.Update(now);
            SitKills.Swaths(dt);
            SitChallenge.Update(now);
            UpdateBoosts(now);
            Scan(now);
            SitChampions.Seed(now);
            if (now >= _prune)
            {
                _prune = now + 10.0;
                Prune(now);
            }
        }

        /// <summary>The tiered fighters, sorted into the lists the event answers use.</summary>
        private static void Refresh()
        {
            Tiered.Clear();
            Guards.Clear();
            Masters.Clear();
            IReadOnlyList<Fighter> all = Fighters.All;
            for (int i = 0; i < all.Count; i++)
            {
                Fighter f = all[i];
                if (f == null || f.Tier == Tier.None || !f.Valid)
                {
                    continue;
                }
                Tiered.Add(f);
                if (f.Tier >= Tier.Expert)
                {
                    Guards.Add(f);
                }
                if (f.Tier >= Tier.Master)
                {
                    Masters.Add(f);
                }
            }
        }

        // ------------------------------------------------------------- scanning --

        /// <summary>What a fighter sees around it, from one chunk scan.</summary>
        internal struct Survey
        {
            /// <summary>Hostile fighting-age units within 4 tiles, their total power and the sum of their positions.</summary>
            public int Foes4;
            public float FoePower4;
            public Vector2 FoeSum4;
            /// <summary>Allies (not babies) on the field within 6 tiles (not sheltering in a house or a boat: Afield).</summary>
            public int Allies6;
            /// <summary>Units fighting within 10 tiles, by side.</summary>
            public int FoesFighting10, AlliesFighting10;
            /// <summary>The strongest tiered enemy (Adept+) within 12 tiles.</summary>
            public Actor Rival;
            public Tier RivalTier;
            public float RivalDist;
        }

        /// <summary>A few fighters a frame, round-robin: those not fighting cost nothing.</summary>
        private static void Scan(double now)
        {
            IReadOnlyList<Fighter> all = Fighters.All;
            int n = all.Count;
            if (n == 0)
            {
                return;
            }
            _scanBudget = Mathf.Min(_scanBudget + Time.unscaledDeltaTime * 16f, 2f);
            int visits = Mathf.Min(n, 12);
            while (visits-- > 0 && _scanBudget >= 1f)
            {
                if (_cursor >= all.Count)
                {
                    _cursor = 0;
                }
                Fighter f = all[_cursor++];
                // a master between blows (a flow strike, a blur dash) is free: only a technique or
                // someone's hands, or its own wind-up (Moves.Committed) keep it from being looked at
                if (f == null || f.Tier == Tier.None || !f.Valid || !f.A.isFighting() || Moves.Committed(f.A) || Director.InScene(f.A))
                {
                    continue;
                }
                if (_nextLook.TryGetValue(f.Id, out double at) && now < at)
                {
                    continue;
                }
                _nextLook[f.Id] = now + Random.Range(0.6f, 0.9f);
                _scanBudget -= 1f;
                Look(f);
            }
        }

        private static void Look(Fighter f)
        {
            Actor a = f.A;
            WorldTile tile = a.current_tile;
            if (tile == null || a.kingdom == null)
            {
                return;
            }
            _units.Clear();
            foreach (Actor u in Finder.getUnitsFromChunk(tile, 1, 13f))
            {
                _units.Add(u);
            }
            Survey s = default;
            Vector2 p = a.current_position;
            float best = -1f;
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u == null || u == a || !Combat.Living(u))
                {
                    continue;
                }
                float d2 = (u.current_position - p).sqrMagnitude;
                if (d2 > 144f)
                {
                    continue;
                }
                if (Combat.IsFoe(a, u))
                {
                    if (!Threat(u))
                    {
                        continue;
                    }
                    if (d2 <= 16f)
                    {
                        s.Foes4++;
                        s.FoePower4 += Tiers.Power(u);
                        s.FoeSum4 += u.current_position;
                    }
                    if (d2 <= 100f && u.isFighting())
                    {
                        s.FoesFighting10++;
                    }
                    Tier t = Tiers.Of(u);
                    if (t >= Tier.Adept)
                    {
                        float rank = (int)t * 10f + Combat.HpPct(u);
                        if (rank > best)
                        {
                            best = rank;
                            s.Rival = u;
                            s.RivalTier = t;
                            s.RivalDist = Mathf.Sqrt(d2);
                        }
                    }
                }
                else if (Combat.IsAlly(a, u) && !u.isBaby() && Afield(u))
                {
                    if (d2 <= 36f)
                    {
                        s.Allies6++;
                    }
                    if (d2 <= 100f && u.isFighting())
                    {
                        s.AlliesFighting10++;
                    }
                }
            }
            _units.Clear();
            SitLastStand.Consider(f, ref s);
            SitChallenge.Consider(f, ref s);
        }

        /// <summary>An enemy worth counting: of fighting weight (or tiered), grown up, not running away.</summary>
        internal static bool Threat(Actor u)
        {
            return !u.isBaby() && Weights.Of(u) >= Weight.Folk && !u.isTask("run_away");
        }

        /// <summary>
        /// On the field: not sheltering in a building or a boat, not in the god hand (the chunk lists
        /// hold those too), not a boat itself. An ally in its house or its boat at the shore relieves
        /// no one; a stun would pull a sheltering one out of its house.
        /// </summary>
        internal static bool Afield(Actor u)
        {
            return !u.isInsideSomething() && !u.isInMagnet() && !u.asset.is_boat;
        }

        /// <summary>Living units around a point (a fresh copy: safe to act on while iterating).</summary>
        internal static List<Actor> Around(Vector2 at, float radius, List<Actor> into)
        {
            into.Clear();
            WorldTile tile = Combat.TileAt(at);
            if (tile == null)
            {
                return into;
            }
            int chunks = radius > 14f ? 2 : 1;
            float r2 = radius * radius;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, chunks, radius + 1f))
            {
                // (Combat.Living: a body a blow has already killed is no one to count, shove or stun)
                if (u != null && Combat.Living(u) && (u.current_position - at).sqrMagnitude <= r2)
                {
                    into.Add(u);
                }
            }
            return into;
        }

        // ------------------------------------------------------------- staging --

        /// <summary>One try at staging a situation: it happened, it waits for its moment, or it can't happen.</summary>
        internal enum Stage
        {
            Done,
            Wait,
            Fail
        }

        /// <summary>A situation asked for (Force) that waits for its moment.</summary>
        private sealed class Staging
        {
            public string Id;
            public Actor A;
            public long AId;
            public Kingdom Side;
            public Vector2 Last;
            public double Since, Until, Next;
            public string Waiting;
            /// <summary>The unit it needs free when its moment comes: its techniques wait meanwhile.</summary>
            public long Hold;
        }

        /// <summary>Set by a try that waits on a unit (Want).</summary>
        private static long _want;

        /// <summary>The staging being tried right now: its own hold doesn't stop the technique its situation starts (a rescuer's Flash Step).</summary>
        private static Staging _trying;

        private static readonly List<Staging> _staging = new List<Staging>();

        /// <summary>How long (world seconds) a staged situation waits for its moment before it settles for less.</summary>
        private const double StageWait = 8.0;

        public const string Ids = "last_stand bodyguard champions rescue rout underdog vengeance lesson challenge";

        /// <summary>
        /// Makes a situation happen around this unit, for dev captures and god powers (the "is it the
        /// moment?" gates are relaxed, never the safety ones). Ids: last_stand, bodyguard (arms the next
        /// guard), champions, rescue, rout, underdog, vengeance, lesson, challenge. The unit stands for
        /// its side: when the situation needs a tiered unit (or a Master) and it isn't one, the
        /// strongest of its side within 30 tiles plays it. If the moment hasn't come yet (the armies
        /// haven't met, the unit is busy, the units were spawned this very frame), it waits for it, up
        /// to 8 s of world time, trying five times a second, and then settles for the closest thing
        /// (the log says what happened). Returns what happened (or that it waits), for the log.
        /// </summary>
        public static string Force(string id, Actor a)
        {
            if (a == null || !a.isAlive())
            {
                return "no unit";
            }
            string key = Key(id);
            if (key == null)
            {
                return "situations: " + Ids;
            }
            if (!Enabled)
            {
                return key + ": battle situations are off (setting)";
            }
            try
            {
                Refresh();
                double now = Fighter.Now;
                _staging.RemoveAll(s => s.Id == key);
                _want = 0;
                Stage st = Try(key, a, false, out string msg);
                if (st != Stage.Wait)
                {
                    return msg;
                }
                _staging.Add(new Staging
                {
                    Id = key,
                    A = a,
                    AId = a.Sid(),
                    Side = a.kingdom,
                    Last = a.current_position,
                    Since = now,
                    Until = now + StageWait,
                    Next = now + 0.2,
                    Waiting = msg,
                    Hold = _want
                });
                return key + ": waiting for the moment (" + msg + "), up to " + StageWait.ToString("F0") + " s";
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] force situation " + id + ": " + e);
                return "failed: " + e.Message;
            }
        }

        private static string Key(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "last_stand":
                case "laststand":
                case "stand":
                    return "last_stand";
                case "bodyguard":
                case "guard":
                    return "bodyguard";
                case "champions":
                case "champion":
                    return "champions";
                case "rescue":
                    return "rescue";
                case "rout":
                    return "rout";
                case "underdog":
                    return "underdog";
                case "vengeance":
                case "revenge":
                    return "vengeance";
                case "lesson":
                    return "lesson";
                case "challenge":
                    return "challenge";
                default:
                    return null;
            }
        }

        /// <summary>One try. 'last': the wait is over, take whatever is closest to the moment.</summary>
        private static Stage Try(string key, Actor a, bool last, out string msg)
        {
            switch (key)
            {
                case "last_stand":
                    return SitLastStand.Stage(a, last, out msg);
                case "bodyguard":
                    SitBodyguard.Armed = Fighter.Now + 20.0;
                    msg = "bodyguard armed for 20 s: the next single-target technique on someone with a free Expert+ ally within 4 tiles is guarded";
                    return Stage.Done;
                case "champions":
                    return SitChampions.Stage(a, last, out msg);
                case "rescue":
                    return SitRescue.Stage(a, last, out msg);
                case "rout":
                    return SitKills.StageRout(a, last, out msg);
                case "underdog":
                    return SitKills.StageUnderdog(a, last, out msg);
                case "vengeance":
                    return SitKills.StageVengeance(a, last, out msg);
                case "lesson":
                    return SitKills.StageLesson(a, last, out msg);
                case "challenge":
                    return SitChallenge.Stage(a, last, out msg);
                default:
                    msg = "situations: " + Ids;
                    return Stage.Fail;
            }
        }

        /// <summary>The waiting stagings get another try (five times a second).</summary>
        private static void Staged(double now)
        {
            for (int i = _staging.Count - 1; i >= 0; i--)
            {
                if (i >= _staging.Count)
                {
                    continue;
                }
                Staging s = _staging[i];
                if (now < s.Next)
                {
                    continue;
                }
                s.Next = now + 0.2;
                Actor a = s.A;
                if (a == null || a.Sid() != s.AId || !a.isAlive())
                {
                    // its unit fell while it waited: another of its side that was there stands in
                    a = StandIn(s.Side, s.Last, s.Since);
                    if (a == null)
                    {
                        _staging.RemoveAt(i);
                        if (Dev.On)
                        {
                            Dev.Trace("situation " + s.Id + ": gave up, nobody of its side is left");
                        }
                        continue;
                    }
                    s.A = a;
                    s.AId = a.Sid();
                }
                s.Last = a.current_position;
                bool last = now >= s.Until;
                Stage st;
                string msg;
                try
                {
                    _want = 0;
                    _trying = s;
                    st = Try(s.Id, a, last, out msg);
                }
                catch (Exception e)
                {
                    st = Stage.Fail;
                    msg = s.Id + ": failed: " + e.Message;
                    Debug.LogError("[Sakuga] staging " + s.Id + ": " + e);
                }
                finally
                {
                    _trying = null;
                }
                if (st == Stage.Wait && !last)
                {
                    s.Waiting = msg;
                    s.Hold = _want;
                    continue;
                }
                _staging.Remove(s);
                if (Dev.On)
                {
                    string after = (now - s.Since).ToString("F1");
                    Dev.Trace("situation staged: " + (st == Stage.Done ? msg + " (after " + after + " s)" : st == Stage.Wait ? s.Id + ": gave up after " + after + " s (" + msg + ")" : msg));
                }
            }
        }

        /// <summary>
        /// The nearest living unit of a side to a point that already lived when the staging began (a
        /// staging whose unit fell; never a unit of the next demo spawned where this one was).
        /// </summary>
        private static Actor StandIn(Kingdom side, Vector2 at, double since)
        {
            if (side == null)
            {
                return null;
            }
            Around(at, 30f, _units);
            Actor best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u.kingdom != side || u.isBaby() || u.data == null || u.data.created_time > since + 0.05)
                {
                    continue;
                }
                float d2 = (u.current_position - at).sqrMagnitude;
                if (d2 < bd)
                {
                    bd = d2;
                    best = u;
                }
            }
            _units.Clear();
            return best;
        }

        /// <summary>
        /// Staging: a try that waits needs this unit free when the moment comes (to plant its feet, to
        /// point, to rush in, to show a technique; or, for a rout or an underdog, not to scatter the
        /// crowd first): the Brain holds its techniques while the staging waits (at most 8 s). It
        /// still fights.
        /// </summary>
        internal static void Want(Actor u)
        {
            _want = u != null ? u.Sid() : 0;
        }

        /// <summary>Hooks.HoldTechniques: is a waiting staging holding this unit?</summary>
        private static bool HoldStaged(Actor a)
        {
            if (_staging.Count == 0 || a == null)
            {
                return false;
            }
            long id = a.Sid();
            for (int i = 0; i < _staging.Count; i++)
            {
                if (_staging[i].Hold == id && _staging[i] != _trying)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Would its Brain hold its techniques right now (a duel's pacing or its stare-down, a God
        /// waiting for its ability, a staging, the move budget of a huge war)? A situation that starts
        /// a technique itself (a lesson tried at once, a rescuer's Flash Step) asks the same first.
        /// </summary>
        internal static bool TechHeld(Actor a)
        {
            List<Func<Actor, bool>> holds = Hooks.HoldTechniques;
            for (int i = 0; i < holds.Count; i++)
            {
                if (holds[i](a))
                {
                    return true;
                }
            }
            // (the Brain's own budget: fighters off screen give way to the ones the player watches)
            int busy = Moves.Count;
            return busy >= 64 || busy >= 28 && !Fx.OnScreen(a.current_position, 0.15f);
        }

        /// <summary>
        /// Staging: who plays a situation for this unit's side. The unit itself if it is at least 'min';
        /// otherwise the strongest tiered unit of its side within 30 tiles (the highest tier, then the
        /// nearest), even if weaker than 'min' (the best its side has); null if its side has none.
        /// </summary>
        internal static Actor Pick(Actor a, Tier min)
        {
            Tier ta = Tiers.Of(a);
            if (ta != Tier.None && ta >= min)
            {
                return a;
            }
            Actor best = ta != Tier.None ? a : null;
            Tier bt = ta;
            float bd = 0f;
            for (int i = 0; i < Tiered.Count; i++)
            {
                Fighter f = Tiered[i];
                if (!f.Valid || f.A == a || a.kingdom == null || f.A.kingdom != a.kingdom)
                {
                    continue;
                }
                float d2 = (f.A.current_position - a.current_position).sqrMagnitude;
                if (d2 > 900f)
                {
                    continue;
                }
                if (best == null || f.Tier > bt || f.Tier == bt && d2 < bd)
                {
                    best = f.A;
                    bt = f.Tier;
                    bd = d2;
                }
            }
            return best;
        }

        /// <summary>
        /// Dev staging only: the camera looks at a staged moment if it is near the edge of the screen
        /// (where the bottom bar of the game hides it), and the capture follows it. The zoom is kept.
        /// </summary>
        internal static void Show(Vector2 at)
        {
            if (!Dev.On)
            {
                return;
            }
            Dev.Watch(at);
            try
            {
                Camera cam = World.world != null ? World.world.camera : null;
                if (cam == null || World.world.move_camera == null)
                {
                    return;
                }
                Vector3 v = cam.WorldToViewportPoint(new Vector3(at.x, at.y, 0f));
                if (v.x > 0.2f && v.x < 0.8f && v.y > 0.35f && v.y < 0.8f)
                {
                    return;
                }
                Transform t = World.world.move_camera.transform;
                t.position = new Vector3(at.x, at.y, t.position.z);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] staging camera: " + e);
            }
        }

        /// <summary>The nearest enemy (or ally) of a unit within 'radius' (for staging).</summary>
        internal static Actor Nearest(Actor a, float radius, bool foe, Func<Actor, bool> filter = null)
        {
            Around(a.current_position, radius, _units);
            Actor best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u == a || (foe ? !Combat.IsFoe(a, u) : !Combat.IsAlly(a, u)) || filter != null && !filter(u))
                {
                    continue;
                }
                float d2 = (u.current_position - a.current_position).sqrMagnitude;
                if (d2 < bestD)
                {
                    bestD = d2;
                    best = u;
                }
            }
            _units.Clear();
            return best;
        }

        // ---------------------------------------------------------------- roles --

        /// <summary>Is this unit being steered by a situation (champion, challenge, vengeance, rescue)?</summary>
        public static bool Engaged(Actor a)
        {
            return a != null && _roles.Count > 0 && _roles.ContainsKey(a.Sid());
        }

        public static object RoleOf(Actor a)
        {
            return a != null && _roles.TryGetValue(a.Sid(), out object r) ? r : null;
        }

        internal static void Claim(Actor a, object role)
        {
            if (a != null)
            {
                _roles[a.Sid()] = role;
            }
        }

        internal static void Unclaim(long id, object role)
        {
            if (_roles.TryGetValue(id, out object r) && r == role)
            {
                _roles.Remove(id);
            }
        }

        /// <summary>
        /// Free to be steered: alive with health left, not performing a technique or held (a master in a flow strike
        /// or a blur dash is between blows: free, and a situation's move takes over from it at
        /// once), not in a scene, not stunned or fleeing, not the player's.
        /// </summary>
        internal static bool Free(Actor a)
        {
            return a != null && Combat.Living(a) && !Moves.Committed(a) && !Director.InScene(a) && !a.hasStatusStunned()
                && !a.isTask("run_away") && !a.isInsideSomething() && !a.isInMagnet() && !Possessed(a);
        }

        /// <summary>
        /// May a situation start its pose on this unit now? It has health left (a blow earlier this
        /// frame may already have killed it), it can perform (a flow strike or a blur dash gives way
        /// at once), it is not in a scene, and its duel isn't waiting for it (the next bout starts
        /// the moment it is free: a pose would only hold the duel up).
        /// </summary>
        internal static bool CanAct(Actor a)
        {
            return Combat.Living(a) && Moves.CanPerform(a) && !Director.InScene(a) && !Duels.Waiting(a);
        }

        /// <summary>
        /// Is the ground there on fire? (Combat.SafeSpot counts burning ground as safe now: Masters
        /// and up don't burn. A situation that sends a weaker unit somewhere checks this too.)
        /// </summary>
        internal static bool Burning(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t != null && t.isOnFire();
        }

        /// <summary>The unit the player is controlling: situations never move, stun or steer it.</summary>
        internal static bool Possessed(Actor a)
        {
            return ControllableUnit.isControllingUnit(a);
        }

        /// <summary>
        /// Is it already fighting a tiered enemy other than 'except' (a duel between bouts, a hunt)?
        /// Situations don't pull such a unit away.
        /// </summary>
        internal static bool OnTieredFoe(Actor a, Actor except = null)
        {
            Actor t = TieredFoe(a);
            return t != null && t != except;
        }

        /// <summary>The tiered enemy it is fighting (its attack target), or null.</summary>
        internal static Actor TieredFoe(Actor a)
        {
            if (!a.has_attack_target || a.attack_target == null || !a.attack_target.isActor())
            {
                return null;
            }
            Actor t = a.attack_target.a;
            return t != null && t.isAlive() && Tiers.Of(t) != Tier.None ? t : null;
        }

        /// <summary>
        /// Points a unit at an enemy (the game's own way of starting a fight), unless it is already on it.
        /// 'unlessTiered': leave it alone if it is already fighting another tiered enemy (a duel, a hunt).
        /// </summary>
        internal static bool Steer(Actor a, Actor target, bool unlessTiered)
        {
            if (!Free(a) || target == null || !target.isAlive() || !Combat.IsFoe(a, target))
            {
                return false;
            }
            if (a.has_attack_target && a.attack_target == target)
            {
                return true;
            }
            if (unlessTiered && a.has_attack_target && a.attack_target != null && a.attack_target.isActor()
                && a.attack_target.isAlive() && Tiers.Of(a.attack_target.a) != Tier.None)
            {
                return false;
            }
            if (!a.canAttackTarget(target, true, false))
            {
                return false;
            }
            a.startFightingWith(target);
            return true;
        }

        // ------------------------------------------------------------------ tempo --

        /// <summary>
        /// How long a situation's pose lasts for this tier (1 = as written in its beat sheet): the
        /// higher the tier, the less a situation stops the fight. It is the tier's own recovery
        /// factor (TierInfo.Linger: Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42), so a
        /// situation's beat and a technique's recovery keep the same tempo.
        /// </summary>
        internal static float Pace(Tier t)
        {
            TierInfo info = t != Tier.None ? Tiers.Info(t) : null;
            return info != null && info.Linger > 0.05f ? Mathf.Min(1f, info.Linger) : 1f;
        }

        /// <summary>A key pose, shortened with the pace but never below 'floor' (the silhouette must still read).</summary>
        internal static float Beat(float seconds, float pace, float floor)
        {
            return pace >= 1f ? seconds : Mathf.Max(Mathf.Min(seconds, floor), seconds * pace);
        }

        /// <summary>
        /// A situation's pose is over: the unit goes straight back to fighting. Its Brain looks at once,
        /// and a Master or better (who fights in one flow) doesn't wait out the pause between
        /// techniques either: the pose was not a technique.
        /// </summary>
        internal static void Resume(Actor a)
        {
            if (a == null || !a.isAlive() || !Fighters.TryGet(a, out Fighter f))
            {
                return;
            }
            f.ThinkIn = 0f;
            if (f.Info != null && f.Info.Flow)
            {
                float gap = f.Info.MinGap / Mathf.Max(0.1f, Settings.Frequency);
                f.LastEnd = Math.Min(f.LastEnd, Fighter.Now - gap - 0.01);
            }
        }

        /// <summary>
        /// A Master or better flows on from a situation's beat into a technique on 'target' (a parry
        /// into its counter): the chain window a technique's EndLink would open, starting a fresh
        /// chain. Like the Brain's own windows, it opens only when a technique it has ready (and can
        /// afford) takes that link, and stays open a little longer while the target is still mid-move
        /// (the counter waits for it); otherwise its Brain simply looks at once (an open window that
        /// nothing can take would only hold its flow strikes and dashes back).
        /// </summary>
        internal static void Open(Actor a, Link link, Actor target)
        {
            if (a == null || target == null || !a.isAlive() || !target.isAlive() || !Fighters.TryGet(a, out Fighter f) || f.Info == null || !f.Info.Flow)
            {
                return;
            }
            f.ThinkIn = 0f;
            if (!Takes(f, link))
            {
                return;
            }
            f.Chain = 0;
            f.Heat = 0f;
            f.Link = link;
            f.LinkTarget = target;
            f.LinkUntil = Fighter.Now + 0.4 + (Moves.Busy(target) ? 0.4 : 0.0);
        }

        /// <summary>Does the fighter have a technique ready and affordable that takes this link?</summary>
        private static bool Takes(Fighter f, Link link)
        {
            IReadOnlyList<Technique> all = Techniques.All;
            for (int i = 0; i < all.Count; i++)
            {
                Technique t = all[i];
                if (t.GodAbility || t.Reactive || t.Team && !Settings.TeamTechniques || !t.Accepts(link))
                {
                    continue;
                }
                int level = Techniques.Level(f.A, t, f.Tier);
                if (level > 0 && f.Ready(t) && Mana.Has(f.A, t, level, true))
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------- damage boosts --

        /// <summary>Hit multipliers read by the damage patch (copied on write: the patch may run off the main thread).</summary>
        private static Dictionary<long, float> _mult = new Dictionary<long, float>();
        private static Dictionary<long, long> _hate = new Dictionary<long, long>();
        internal static volatile int Boosters;
        public const float HateMult = 1.25f;

        internal static void SetMult(Actor a, float mult)
        {
            Dictionary<long, float> d = new Dictionary<long, float>(_mult) { [a.Sid()] = mult };
            _mult = d;
            Boosters = _mult.Count + _hate.Count;
        }

        internal static void ClearMult(long id)
        {
            if (!_mult.ContainsKey(id))
            {
                return;
            }
            Dictionary<long, float> d = new Dictionary<long, float>(_mult);
            d.Remove(id);
            _mult = d;
            Boosters = _mult.Count + _hate.Count;
        }

        internal static void SetHate(Actor a, Actor target)
        {
            Dictionary<long, long> d = new Dictionary<long, long>(_hate) { [a.Sid()] = target.Sid() };
            _hate = d;
            Boosters = _mult.Count + _hate.Count;
        }

        internal static void ClearHate(long id)
        {
            if (!_hate.ContainsKey(id))
            {
                return;
            }
            Dictionary<long, long> d = new Dictionary<long, long>(_hate);
            d.Remove(id);
            _hate = d;
            Boosters = _mult.Count + _hate.Count;
        }

        /// <summary>How much harder this attacker's ordinary blows land on this victim (1 = normal).</summary>
        internal static float DamageFactor(Actor a, Actor v)
        {
            long id = a.Sid();
            float k = 1f;
            Dictionary<long, float> m = _mult;
            if (m.Count > 0 && m.TryGetValue(id, out float x))
            {
                k *= x;
            }
            Dictionary<long, long> h = _hate;
            if (h.Count > 0 && h.TryGetValue(id, out long t) && v.Sid() == t)
            {
                k *= HateMult;
            }
            return k;
        }

        /// <summary>One boost on a fighter: how much, until when (world time).</summary>
        private struct BoostOn
        {
            public float Amount;
            public double Until;
        }

        /// <summary>
        /// The boosts on a fighter while a situation boosts it, each with its own end. The fighter has
        /// one boost slot (shared with second winds, team links, a God's awakening): it holds the
        /// largest boost that is on, and when that one ends the next largest takes over, so a short
        /// large boost (a last stand's) never lasts as long as a long small one (an awakening, an
        /// oath), nor the other way round. Another system's boost found in the slot joins as it is.
        /// </summary>
        private sealed class Boosts
        {
            public Fighter F;
            public readonly List<BoostOn> On = new List<BoostOn>(4);
            /// <summary>What it last put in the slot: anything else there was written by another system.</summary>
            public float Set = float.NaN;
            public double SetUntil;
        }

        private static readonly Dictionary<long, Boosts> _boosts = new Dictionary<long, Boosts>();
        private static readonly List<long> _boostsOver = new List<long>();

        /// <summary>A technique bonus through the fighter's boost (shared with second winds and team links: the larger wins while it lasts).</summary>
        internal static void Boost(Fighter f, float amount, double seconds)
        {
            if (f == null)
            {
                return;
            }
            double now = Fighter.Now;
            if (!_boosts.TryGetValue(f.Id, out Boosts b) || b.F != f)
            {
                b = new Boosts { F = f };
                _boosts[f.Id] = b;
            }
            Adopt(b, now);
            Join(b, amount, now + seconds);
            Apply(b, now);
        }

        /// <summary>Once a frame: each boosted fighter's slot holds the largest boost still on.</summary>
        private static void UpdateBoosts(double now)
        {
            if (_boosts.Count == 0)
            {
                return;
            }
            _boostsOver.Clear();
            foreach (KeyValuePair<long, Boosts> kv in _boosts)
            {
                Boosts b = kv.Value;
                if (!b.F.Valid)
                {
                    _boostsOver.Add(kv.Key);
                    continue;
                }
                Adopt(b, now);
                if (!Apply(b, now))
                {
                    _boostsOver.Add(kv.Key);
                }
            }
            foreach (long id in _boostsOver)
            {
                _boosts.Remove(id);
            }
            _boostsOver.Clear();
        }

        /// <summary>Another system wrote the slot since (an awakening, a second wind, a team link): its boost joins the list.</summary>
        private static void Adopt(Boosts b, double now)
        {
            Fighter f = b.F;
            if (now < f.BoostUntil && (f.BoostDamage != b.Set || f.BoostUntil != b.SetUntil))
            {
                Join(b, f.BoostDamage, f.BoostUntil);
            }
        }

        /// <summary>A boost joins the list (the same amount again only lasts longer).</summary>
        private static void Join(Boosts b, float amount, double until)
        {
            for (int i = 0; i < b.On.Count; i++)
            {
                if (b.On[i].Amount == amount)
                {
                    b.On[i] = new BoostOn { Amount = amount, Until = Math.Max(b.On[i].Until, until) };
                    return;
                }
            }
            b.On.Add(new BoostOn { Amount = amount, Until = until });
        }

        /// <summary>The slot: the largest boost on now, until the last one ends (false: none is left).</summary>
        private static bool Apply(Boosts b, double now)
        {
            float best = 0f;
            double end = now;
            for (int i = b.On.Count - 1; i >= 0; i--)
            {
                BoostOn o = b.On[i];
                if (o.Until <= now)
                {
                    b.On.RemoveAt(i);
                    continue;
                }
                best = Mathf.Max(best, o.Amount);
                end = Math.Max(end, o.Until);
            }
            if (b.On.Count == 0)
            {
                return false;
            }
            b.F.BoostDamage = best;
            b.F.BoostUntil = end;
            b.Set = best;
            b.SetUntil = end;
            return true;
        }

        // ------------------------------------------------------------- messages --

        /// <summary>A line at the top of the screen, if the spot is in view (or 'always'), not more than one every 2.5 s.</summary>
        internal static void Tip(string text, string hex, Vector2 at, bool always = false)
        {
            if (string.IsNullOrEmpty(text) || World.world == null)
            {
                return;
            }
            if (!always && !Fx.OnScreen(at, 0.05f))
            {
                return;
            }
            double now = Fighter.Now;
            if (!always && now - _tipAt < 2.5)
            {
                return;
            }
            _tipAt = now;
            try
            {
                WorldTip.showNow(text, false, "top", 2.8f, hex);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] situation tip: " + e);
            }
        }

        /// <summary>A unit's name for a message (never throws).</summary>
        internal static string Name(Actor a)
        {
            try
            {
                string n = a?.getName();
                return string.IsNullOrEmpty(n) ? a?.asset?.id ?? "?" : n;
            }
            catch
            {
                return "?";
            }
        }

        internal static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        // ---------------------------------------------------------- bookkeeping --

        private static void OnMoveEnded(Move m, bool aborted)
        {
            try
            {
                if (m != null && m.Tech != null)
                {
                    SitLastStand.TechniqueEnded(m.A);
                    SitKills.MentorMoveEnded(m);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] situations move end: " + e);
            }
        }

        private static readonly List<long> _drop = new List<long>();

        private static void Prune(double now)
        {
            _drop.Clear();
            foreach (KeyValuePair<long, double> kv in _nextLook)
            {
                if (now - kv.Value > 5.0)
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (long id in _drop)
            {
                _nextLook.Remove(id);
            }
            SitLastStand.Prune(now);
            SitBodyguard.Prune(now);
            SitChampions.Prune(now);
            SitRescue.Prune(now);
            SitKills.Prune(now);
            SitChallenge.Prune(now);
        }

        /// <summary>A new world (or a load): nothing from the old one may remain.</summary>
        internal static void Clear()
        {
            Hooks.Safe(SitLastStand.Clear, "clear last stands");
            Hooks.Safe(SitChampions.Clear, "clear champions");
            Hooks.Safe(SitRescue.Clear, "clear rescues");
            Hooks.Safe(SitKills.Clear, "clear kill situations");
            Hooks.Safe(SitChallenge.Clear, "clear challenges");
            Hooks.Safe(SitBodyguard.Clear, "clear bodyguards");
            _roles.Clear();
            _staging.Clear();
            _nextLook.Clear();
            _units.Clear();
            Tiered.Clear();
            Guards.Clear();
            Masters.Clear();
            _mult = new Dictionary<long, float>();
            _hate = new Dictionary<long, long>();
            Boosters = 0;
            _boosts.Clear();
            _boostsOver.Clear();
            _trying = null;
            _tipAt = -999;
            _prune = 0;
            _refresh = 0f;
        }
    }

    /// <summary>
    /// Situation damage on ordinary blows (a last stand's +30%, vengeance on the killer): weapon
    /// hits from a boosted attacker land harder. Technique damage takes the bonus through the
    /// fighter's boost instead (Combat.Hit), so a combo's non-lethal blows still never kill.
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.getHit))]
    internal static class SituationDamagePatch
    {
        private static void Prefix(Actor __instance, ref float pDamage, AttackType pAttackType, BaseSimObject pAttacker)
        {
            if (Situations.Boosters == 0 || pAttackType != AttackType.Weapon || pAttacker == null)
            {
                return;
            }
            try
            {
                if (pAttacker.isActor() && pAttacker.a != null)
                {
                    pDamage *= Situations.DamageFactor(pAttacker.a, __instance);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] situation damage: " + e);
            }
        }
    }
}
