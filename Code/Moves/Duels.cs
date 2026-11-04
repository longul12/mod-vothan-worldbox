using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DUELS: dynamic battles between strong fighters. When two tiered enemies (Novice and up)
    /// meet, one of them going for the other, not so far apart in power that one would simply
    /// crush the other (power ratio under 3), they may lock into a duel (a Duel controller drives
    /// it: openers, exchanges, free fighting, the Final Exchange).
    ///
    /// THE MEETING. The first time two worthy enemies come within 10 tiles of each other, fate
    /// decides once whether they duel (by the lower of the two: Novices 35%, Adepts 50%, Experts 70%,
    /// Masters 90%, Grandmasters, Gods and rivals always). If they will, neither throws a technique
    /// while they close in (the Brain is held through Hooks.HoldTechniques, at most 1.8 s, never
    /// while it is mobbed by lesser enemies), and the duel begins as soon as both are free within
    /// 8.5 tiles, with an opening ceremony (Openers: a stare-down, a salvo, an aura clash, a blitz,
    /// a first clash; two Gods most often the Clash of Heavens), never with a stray technique. A
    /// pair that fate passed over is left alone for 15 s.
    /// Two who meet with nobody else about always duel. In a battle, a Master or better cutting
    /// through lesser enemies now and then senses a worthy one within 9 tiles and turns to it.
    ///
    /// PACING. A duel is meant to be drawn out and to end on the Final Exchange: while two
    /// fighters duel, the duel rations the techniques of the free fighting (Duel.HoldTech, through
    /// Hooks.HoldTechniques), a technique once shown is "read" by the other and not used again for
    /// a while (14 s for Adepts, 7 for Gods: Duel.ReadFor, so a duel draws out the whole
    /// repertoire), and the blows they trade outside the exchanges (techniques, plain swings) are
    /// softened (Duel.TechScale, Duel.SwingScale), one technique never costs more than its share
    /// of the other's health (Duel.TechCap; a God's own ability a quarter), nothing takes a duelist
    /// under its floor (Duel.Floor: the duel's course spends its health over time) and nothing is
    /// lethal; the exchanges themselves cost a share of the loser's health. A blow the floor takes
    /// still flashes. Anyone else may still kill a duelist (a war goes on around them).
    ///
    /// At most three duels on screen at once (eight in the whole world), and every fighter rests
    /// ten seconds after a duel.
    ///
    /// Rivals: two who both walked away from a duel (8 s after it ended with both alive, or as
    /// soon as they are 15 tiles apart; one cutting the other down before that simply wins the
    /// duel late). Stored on the units ("sakuga_rival", so it survives saving) and in
    /// Fighter.Rival; they seek each other within 30 tiles, always stare each other down, and when
    /// one finally kills the other the world hears of it.
    ///
    /// A Master or better challenging a Master or better is announced (on screen or a favourite).
    ///
    /// Other systems ask Duels.InDuel(unit) / Duels.Opponent(unit) (team techniques should leave
    /// duelists alone; a duelist's Brain may favour its opponent).
    /// </summary>
    internal static class Duels
    {
        /// <summary>Tiles within which two fighters fated to duel lock into it.</summary>
        public const float Range = 8.5f;
        /// <summary>Tiles within which two worthy enemies have "met" (fate decides, techniques are held).</summary>
        public const float MeetRange = 10f;
        /// <summary>Actor data key holding the rival's id (a string: ids don't fit a float).</summary>
        public const string KeyRival = "sakuga_rival";

        private const int MaxOnScreen = 3;
        private const int MaxTotal = 8;
        private const float RestSeconds = 10f;
        private const float SeekRange = 30f;
        /// <summary>World seconds a fated pair may be held from their techniques while they close in.</summary>
        private const float HoldMax = 1.8f;
        /// <summary>How long fate's word stands: a duel to be (it may still begin), or a pair passed over.</summary>
        private const float FatedFor = 8f, PassedFor = 15f;

        private static readonly List<Duel> _duels = new List<Duel>();
        private static readonly List<Meeting> _meetings = new List<Meeting>();
        private static readonly List<Parting> _parted = new List<Parting>();
        /// <summary>Seconds after a duel that ended with both alive before they are sworn rivals.</summary>
        private const float PartFor = 8f;
        private static readonly Dictionary<long, double> _rest = new Dictionary<long, double>();
        private static readonly Dictionary<long, Watch> _watch = new Dictionary<long, Watch>();
        private static int _frame = -1;
        private static float _prune;
        private static string _forced;
        private static bool _reissue;

        /// <summary>Above zero while a duel deals its own blows (exchange chips, the finishing blow): pacing leaves them alone.</summary>
        internal static int OwnBlow;

        /// <summary>Two worthy enemies within sight of each other, and what fate decided for them.</summary>
        private sealed class Meeting
        {
            public Actor A, B;
            public long IdA, IdB;
            /// <summary>They will duel.</summary>
            public bool Go;
            /// <summary>World time it was decided, and until when it stands.</summary>
            public double Since, Until;
            public bool Logged;

            public bool Has(long id)
            {
                return id == IdA || id == IdB;
            }

            public Actor Other(long id)
            {
                return id == IdA ? B : A;
            }
        }

        /// <summary>Two who walked away from a duel alive: rivals once they have really parted.</summary>
        private sealed class Parting
        {
            public Actor A, B;
            public long IdA, IdB;
            public double At;
        }

        /// <summary>Per-fighter timers (move time).</summary>
        private sealed class Watch
        {
            public Fighter F;
            public float Scan = Random.Range(0f, 0.5f);
            public float Seek = Random.Range(0f, 1f);
            public bool Loaded;
        }

        /// <summary>Duels started since the game started (dev log).</summary>
        internal static int Started;

        /// <summary>
        /// Hooks for the duel's choices (the later duel rework): each multiplies an exchange's weight
        /// in the pool (1: no opinion; 0: never). The personas' taste is built in (Taste).
        /// </summary>
        internal static readonly List<Func<Duel, Exchange, float>> Weighers = new List<Func<Duel, Exchange, float>>();

        /// <summary>
        /// Hooks for how a duel opens: fill the queue with its opening exchanges and return true to
        /// take over (the first that does wins); false leaves it to Duel.Opening.
        /// </summary>
        internal static readonly List<Func<Duel, List<Exchange>, bool>> Openings = new List<Func<Duel, List<Exchange>, bool>>();

        /// <summary>
        /// How much the two duelists want this exchange: both personas' taste for its Forms (a brawler
        /// leans into a blade lock, a trickster a feint, an out-fighter the chase), and the hooks.
        /// </summary>
        public static float Taste(Duel d, Exchange ex)
        {
            float k = 1f;
            Form f = ex.Forms;
            if (f != Form.None)
            {
                float a = Personas.Of(d.A).Affinity(f), b = Personas.Of(d.B).Affinity(f);
                k = Mathf.Pow(Mathf.Sqrt(a * b), 1.6f);
            }
            for (int i = 0; i < Weighers.Count; i++)
            {
                try
                {
                    k *= Mathf.Max(0f, Weighers[i](d, ex));
                }
                catch (Exception e)
                {
                    SakugaMod.Report("duel weigher", e);
                }
            }
            return k;
        }

        public static IReadOnlyList<Duel> All => _duels;

        /// <summary>The duel dev captures follow (the latest one begun).</summary>
        internal static Duel Watched;

        /// <summary>Is this unit in a duel right now (exchanging blows or in the free fighting between)?</summary>
        public static bool InDuel(Actor a)
        {
            return Of(a) != null;
        }

        /// <summary>Its duel opponent, or null.</summary>
        public static Actor Opponent(Actor a)
        {
            Duel d = Of(a);
            return d?.OpponentOf(a);
        }

        /// <summary>
        /// Is its duel waiting for it (the free fighting is over and the next exchange starts the
        /// moment both are free)? Other systems that start moves of their own on a unit (strike
        /// chains) should leave it be while this is true, so the duel flows straight on.
        /// </summary>
        public static bool Waiting(Actor a)
        {
            Duel d = Of(a);
            return d != null && d.Waiting;
        }

        /// <summary>
        /// The share of a technique's damage that reaches 'v' from 'a' right now: softened between
        /// two duelists (Duel.TechScale), whole otherwise. For scoring (Ctx.Expected) and for
        /// techniques that want to know a blow can't end a duel (an execution won't kill).
        /// </summary>
        public static float TechShare(Actor a, Actor v)
        {
            Duel d = Of(v);
            Actor o = d?.OpponentOf(v);
            return o != null && a != null && o.Sid() == a.Sid() ? d.TechScale : 1f;
        }

        /// <summary>The techniques each fighter threw in its last duel (its next duel leans away from them).</summary>
        private static readonly Dictionary<long, HashSet<Technique>> _lastDuel = new Dictionary<long, HashSet<Technique>>();

        /// <summary>A duel is over: what each of them threw is remembered for their next one.</summary>
        internal static void Remember(Duel d)
        {
            foreach (long id in new[] { d.IdA, d.IdB })
            {
                HashSet<Technique> s = new HashSet<Technique>(d.UsedBy(id));
                if (s.Count > 0)
                {
                    _lastDuel[id] = s;
                }
            }
            if (_lastDuel.Count > 2000)
            {
                _lastDuel.Clear();
            }
        }

        /// <summary>
        /// How fresh a technique is for a duelist (the Brain's score is multiplied by it): every time it
        /// was already thrown in this duel it counts for a third as much, one it hasn't shown yet gets a
        /// little lift, one it leaned on in its last duel counts for less, and a duel adds its own chance;
        /// so a duel draws out a fighter's whole repertoire and no two duels of the same two play alike.
        /// 1 outside a duel.
        /// </summary>
        internal static float Freshness(Actor a, Technique t)
        {
            Duel d = a != null && _duels.Count > 0 ? Of(a) : null;
            if (d == null || t == null)
            {
                return 1f;
            }
            int n = d.UsedCount(a, t);
            float k = n == 0 ? Random.Range(1f, 1.4f) : Mathf.Pow(0.3f, n) * Random.Range(0.75f, 1.25f);
            if (_lastDuel.TryGetValue(a.Sid(), out HashSet<Technique> last) && last.Contains(t))
            {
                k *= 0.6f;
            }
            return k;
        }

        internal static Duel Of(Actor a)
        {
            if (a == null || _duels.Count == 0)
            {
                return null;
            }
            long id = a.Sid();
            for (int i = 0; i < _duels.Count; i++)
            {
                Duel d = _duels[i];
                if (!d.Over && d.Has(id))
                {
                    return d;
                }
            }
            return null;
        }

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += OnFrame;
            Hooks.FighterTick += Tick;
            Hooks.Killed += OnKilled;
            Hooks.WorldCleared += Clear;
            Hooks.HoldTechniques.Add(Hold);
            Hooks.BeforeDamage.Add(Pace);
            Hooks.TechniqueStarted += OnTechnique;
        }

        /// <summary>
        /// A duelist started a technique in the free fighting: it counts against what the duel allows
        /// it, and the other has now seen it: the same trick won't come again for a while (ReadFor),
        /// so a duel draws out a fighter's whole repertoire instead of one favourite chain.
        /// </summary>
        private static void OnTechnique(Fighter f, Plan p)
        {
            try
            {
                if (f == null || _duels.Count == 0)
                {
                    return;
                }
                Duel d = Of(f.A);
                if (d == null)
                {
                    return;
                }
                d.NoteTechnique(f.A);
                if (p != null && p.Tech != null)
                {
                    d.NoteUsed(f.A, p.Tech);
                }
                if (p != null && p.Tech != null && !p.Tech.GodAbility && !p.Tech.Reactive)
                {
                    f.SetCooldown(p.Tech, Mathf.Max(f.CooldownLeft(p.Tech), d.ReadFor));
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("duel technique", e);
            }
        }

        // -------------------------------------------------------------- tick --

        /// <summary>
        /// Every duel ticks once per frame (move time, stopped with the world), whether or not any
        /// fighter is left to tick: two duelists killed together still end their duel (and its cinematic).
        /// </summary>
        private static void OnFrame(float dt)
        {
            if (dt <= 0f || Time.frameCount == _frame)
            {
                return;
            }
            _frame = Time.frameCount;
            try
            {
                Frame(dt);
            }
            catch (Exception e)
            {
                SakugaMod.Report("duels", e);
            }
        }

        private static void Tick(Fighter f, float dt)
        {
            try
            {
                Watch w = WatchOf(f);
                if (!w.Loaded)
                {
                    w.Loaded = true;
                    LoadRival(f);
                }
                w.Scan -= dt;
                if (w.Scan <= 0f)
                {
                    w.Scan = Random.Range(0.35f, 0.55f);
                    Look(f);
                }
                w.Seek -= dt;
                if (w.Seek <= 0f)
                {
                    w.Seek = Random.Range(0.9f, 1.2f);
                    Seek(f);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("duels", e);
            }
        }

        private static void Frame(float dt)
        {
            for (int i = _duels.Count - 1; i >= 0; i--)
            {
                if (i >= _duels.Count)
                {
                    continue;
                }
                Duel d = _duels[i];
                try
                {
                    d.Tick(dt);
                }
                catch (Exception e)
                {
                    SakugaMod.Report("duel", e);
                    d.Abandon();
                }
                if (d.Over)
                {
                    _duels.Remove(d);
                }
            }
            Meetings();
            Partings();
            _prune -= dt;
            if (_prune <= 0f)
            {
                _prune = 10f;
                Prune();
            }
        }

        private static Watch WatchOf(Fighter f)
        {
            if (_watch.TryGetValue(f.Id, out Watch w) && w.F == f)
            {
                return w;
            }
            w = new Watch { F = f };
            _watch[f.Id] = w;
            return w;
        }

        private static void Prune()
        {
            List<long> drop = null;
            foreach (KeyValuePair<long, Watch> kv in _watch)
            {
                if (!kv.Value.F.Valid)
                {
                    (drop ??= new List<long>()).Add(kv.Key);
                }
            }
            foreach (KeyValuePair<long, double> kv in _rest)
            {
                if (kv.Value < Fighter.Now)
                {
                    (drop ??= new List<long>()).Add(kv.Key);
                }
            }
            if (drop == null)
            {
                return;
            }
            foreach (long id in drop)
            {
                if (_watch.TryGetValue(id, out Watch w) && !w.F.Valid)
                {
                    _watch.Remove(id);
                }
                if (_rest.TryGetValue(id, out double until) && until < Fighter.Now)
                {
                    _rest.Remove(id);
                }
            }
        }

        // ---------------------------------------------------------- detection --

        /// <summary>A fighter going for a worthy enemy within sight: they have met (fate decides once).</summary>
        private static void Look(Fighter f)
        {
            if (!Settings.Duels || _duels.Count >= MaxTotal || f.Tier < Tier.Novice)
            {
                return;
            }
            Actor b = TargetOf(f.A);
            if (b != null && Worthy(f.A, b))
            {
                if (Dist2(f.A, b) <= MeetRange * MeetRange)
                {
                    Meet(f.A, b);
                }
                return;
            }
            if (f.Tier >= Tier.Master)
            {
                Sense(f);
            }
        }

        /// <summary>
        /// A Master or better busy with lesser enemies feels a worthy one near (an Expert or better
        /// within 9 tiles, free) and now and then turns to it: strong fighters find each other in a
        /// battle. Never while it is mobbed.
        /// </summary>
        private static void Sense(Fighter f)
        {
            Actor a = f.A;
            if (Random.value > 0.3f || Moves.Busy(a) || Director.InScene(a) || a.isTask("run_away"))
            {
                return;
            }
            Actor best = null;
            float bd = 9f * 9f;
            IReadOnlyList<Fighter> all = Fighters.All;
            for (int i = 0; i < all.Count; i++)
            {
                Fighter g = all[i];
                if (g == f || g.Tier < Tier.Expert || !g.Valid)
                {
                    continue;
                }
                float d2 = Dist2(a, g.A);
                if (d2 >= bd || Director.InScene(g.A) || !Worthy(a, g.A) || !a.isTargetOkToAttack(g.A))
                {
                    continue;   // (the game's own rule: one it could fight at all, on its own island)
                }
                best = g.A;
                bd = d2;
            }
            if (best == null || Crowded(a, null))
            {
                return;
            }
            a.setAttackTarget(best);
            Trace("sense: " + a.asset.id + "#" + a.Sid() + " (" + f.Tier + ") turns to " + best.asset.id + "#" + best.Sid() + " (" + Tiers.Of(best) + ") at " + Mathf.Sqrt(bd).ToString("F1") + " tiles");
        }

        private static Actor TargetOf(Actor a)
        {
            if (!a.has_attack_target || a.attack_target == null || !a.attack_target.isActor())
            {
                return null;
            }
            Actor b = a.attack_target.a;
            return b != null && b.isAlive() ? b : null;
        }

        private static float Dist2(Actor a, Actor b)
        {
            return (a.current_position - b.current_position).sqrMagnitude;
        }

        /// <summary>
        /// Two enemies who could duel: both tiered (Adept and up), not so far apart in power that the
        /// stronger simply wins (ratio under 3), neither resting from a duel or in another one, neither
        /// flying (a duel is fought on the ground: one in the air could never begin it).
        /// </summary>
        private static bool Worthy(Actor a, Actor b)
        {
            if (a == null || b == null || a == b || !a.isAlive() || !b.isAlive() || !Combat.IsFoe(a, b) || a.isFlying() || b.isFlying())
            {
                return false;
            }
            Tier ta = Tiers.Of(a), tb = Tiers.Of(b);
            if (ta < Tier.Novice || tb < Tier.Novice)
            {
                return false;
            }
            if (Primordials.Is(a) || Primordials.Is(b))
            {
                return false;   // a Primordial doesn't duel: against a God it is a slaughter, against its equal the world ends
            }
            float ra = Tiers.Power(a), rb = Tiers.Power(b);
            if (Mathf.Max(ra, rb) / Mathf.Max(0.01f, Mathf.Min(ra, rb)) >= 3f)
            {
                return false;
            }
            return !Resting(a) && !Resting(b) && Of(a) == null && Of(b) == null;
        }

        /// <summary>
        /// Their meeting: what fate decided for this pair, deciding it now if nothing stands. The chance
        /// grows with the weaker one's tier; rivals always duel.
        /// </summary>
        private static Meeting Meet(Actor a, Actor b)
        {
            long ia = a.Sid(), ib = b.Sid();
            double now = Fighter.Now;
            for (int i = 0; i < _meetings.Count; i++)
            {
                Meeting m = _meetings[i];
                if (m.Has(ia) && m.Has(ib) && now <= m.Until)
                {
                    return m;
                }
            }
            if (_duels.Count >= MaxTotal)
            {
                return null;
            }
            bool rivals = Rivals(a, b) || Rivals(b, a);
            Tier ta = Tiers.Of(a), tb = Tiers.Of(b);
            Tier low = ta < tb ? ta : tb;
            float chance = rivals || low >= Tier.Grandmaster ? 1f : low == Tier.Master ? 0.9f : low == Tier.Expert ? 0.7f : low == Tier.Adept ? 0.5f : 0.35f;
            if (chance < 1f && Alone(a, b))
            {
                chance = 1f;   // two who meet with nobody else about: that is a duel, always
            }
            Meeting n = new Meeting { A = a, B = b, IdA = ia, IdB = ib, Since = now };
            n.Go = Random.value < chance;
            n.Until = now + (n.Go ? FatedFor : PassedFor);
            _meetings.Add(n);
            Trace("meet " + a.asset.id + "#" + ia + " (" + ta + ") and " + b.asset.id + "#" + ib + " (" + tb + ") at "
                + Mathf.Sqrt(Dist2(a, b)).ToString("F1") + " tiles: " + (n.Go ? "they will duel" : "not this time"));
            return n;
        }

        /// <summary>No other enemy of either within 8 tiles of their middle: a one-on-one in the open.</summary>
        private static bool Alone(Actor a, Actor b)
        {
            Vector2 mid = (a.current_position + b.current_position) * 0.5f;
            foreach (Actor u in Combat.FoesAround(a, mid, 8f, true, true))
            {
                if (u != b)
                {
                    return false;
                }
            }
            foreach (Actor u in Combat.FoesAround(b, mid, 8f, true, true))
            {
                if (u != a)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The fated meeting this unit is part of, still in its hold window (or null).</summary>
        private static Meeting FatedOf(long id, double now)
        {
            for (int i = 0; i < _meetings.Count; i++)
            {
                Meeting m = _meetings[i];
                if (m.Go && m.Has(id) && now <= m.Until && now - m.Since <= HoldMax)
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>
        /// Hooks.HoldTechniques: a fighter about to duel keeps its techniques while its opponent
        /// closes in (at most HoldMax, only while the other is coming within 10 tiles, never while
        /// it is mobbed by others); a duelist uses only the techniques its duel allows it in the free
        /// fighting, and stands ready for the Final when one of them is a blow from the end.
        /// </summary>
        private static bool Hold(Actor a)
        {
            try
            {
                if (!Settings.Duels || a == null)
                {
                    return false;
                }
                if (_meetings.Count == 0 && _duels.Count == 0 && Tiers.Of(a) < Tier.Novice)
                {
                    return false;   // nothing to hold it for (an Adept or better may still meet its first worthy foe below)
                }
                Duel d = Of(a);
                if (d != null)
                {
                    return d.HoldTech(a);
                }
                long id = a.Sid();
                double now = Fighter.Now;
                Meeting m = FatedOf(id, now);
                if (m == null)
                {
                    // its first look at a worthy enemy may come before the duels' own scan
                    Actor t = TargetOf(a);
                    if (t == null || !Worthy(a, t) || Dist2(a, t) > MeetRange * MeetRange)
                    {
                        return false;
                    }
                    m = Meet(a, t);
                    if (m == null || !m.Go || now - m.Since > HoldMax)
                    {
                        return false;
                    }
                }
                Actor o = m.Other(id);
                if (!Worthy(a, o) || Dist2(a, o) > MeetRange * MeetRange || a.isTask("run_away") || o.isTask("run_away") || Crowded(a, o))
                {
                    return false;
                }
                if (!m.Logged)
                {
                    m.Logged = true;
                    Trace("hold " + a.asset.id + "#" + id + "'s techniques: " + o.asset.id + "#" + o.Sid() + " is coming (" + Mathf.Sqrt(Dist2(a, o)).ToString("F1") + " tiles)");
                }
                return true;
            }
            catch (Exception e)
            {
                SakugaMod.Report("duel hold", e);
                return false;
            }
        }

        /// <summary>Two or more other enemies right on it: it fights them, it doesn't wait for a duel.</summary>
        private static bool Crowded(Actor a, Actor o)
        {
            int n = 0;
            foreach (Actor u in Combat.FoesAround(a, a.current_position, 2.5f, false, true))
            {
                if (u != o && ++n >= 2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Every frame: fated pairs that are both free and close enough lock into their duel.</summary>
        private static void Meetings()
        {
            if (_meetings.Count == 0)
            {
                return;
            }
            double now = Fighter.Now;
            for (int i = _meetings.Count - 1; i >= 0; i--)
            {
                if (i >= _meetings.Count)
                {
                    continue;
                }
                Meeting m = _meetings[i];
                bool gone = m.A == null || m.B == null || m.A.Sid() != m.IdA || m.B.Sid() != m.IdB || !m.A.isAlive() || !m.B.isAlive();
                if (gone || now > m.Until || !Settings.Duels)
                {
                    _meetings.RemoveAt(i);
                    continue;
                }
                if (!m.Go || Of(m.A) != null || Of(m.B) != null || Resting(m.A) || Resting(m.B))
                {
                    continue;
                }
                if (Dist2(m.A, m.B) > Range * Range || !Free(m.A) || !Free(m.B) || !Combat.IsFoe(m.A, m.B))
                {
                    continue;
                }
                Vector2 mid = (m.A.current_position + m.B.current_position) * 0.5f;
                if (Fx.OnScreen(mid, 0.1f) && OnScreen() >= MaxOnScreen)
                {
                    m.Go = false;   // the screen is full of duels: this one isn't to be
                    continue;
                }
                if (Begin(m.A, m.B, false))
                {
                    _meetings.RemoveAt(i);
                }
            }
        }

        /// <summary>Free to start a duel: on its feet, not in a move or a set piece, not fleeing, not stunned.</summary>
        private static bool Free(Actor a)
        {
            return Moves.CanPerform(a) && !Director.InScene(a) && Of(a) == null && !a.isTask("run_away") && !a.hasStatusStunned();
        }

        /// <summary>
        /// Starts a duel between a (the challenger) and b. 'force' skips the chance, the rest and the
        /// on-screen budget (dev tools, god powers), never the rules (tiered, hostile, free).
        /// </summary>
        public static bool Begin(Actor a, Actor b, bool force)
        {
            if (a == null || b == null || a == b || !Combat.IsFoe(a, b) || Tiers.Of(a) == Tier.None || Tiers.Of(b) == Tier.None)
            {
                return false;
            }
            if (!Free(a) || !Free(b))
            {
                return false;
            }
            if (!force && _duels.Count >= MaxTotal)
            {
                return false;   // eight in the whole world (fated meetings wait for room)
            }
            Vector2 mid = (a.current_position + b.current_position) * 0.5f;
            bool onScreen = Fx.OnScreen(mid, 0.1f);
            if (!force && onScreen && OnScreen() >= MaxOnScreen)
            {
                return false;
            }
            Duel d = new Duel(a, b, Rivals(a, b) || Rivals(b, a));
            if (!d.Begin())
            {
                return false;
            }
            _duels.Add(d);
            Started++;
            Watched = d;
            Dev.Watch(mid + new Vector2(0f, 2f));
            Trace("duel begins: " + a.asset.id + "#" + a.Sid() + " (" + d.TA + ") vs " + b.asset.id + "#" + b.Sid() + " (" + d.TB + ")" + (d.Rivals ? " RIVALS" : ""));
            Announce(a, b, d, onScreen);
            Spotlight.Fight(a, b, d.TA > d.TB ? d.TA : d.TB);
            return true;
        }

        private static int OnScreen()
        {
            int n = 0;
            foreach (Duel d in _duels)
            {
                if (!d.Over && Fx.OnScreen(d.Mid, 0.1f))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>"A challenges B" for a Master+ against a Master+, if the player can see it or cares.</summary>
        private static void Announce(Actor a, Actor b, Duel d, bool onScreen)
        {
            if (d.TA < Tier.Master || d.TB < Tier.Master || !(onScreen || a.isFavorite() || b.isFavorite()))
            {
                return;
            }
            try
            {
                string text = Locale.Format(d.Rivals ? "sakuga_duel_rematch" : "sakuga_duel_challenge", a.getName(), b.getName());
                WorldTip.showNow(text, false, "top", 3f, Tiers.Info(d.TA).Hex);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] duel announce: " + e);
            }
        }

        // -------------------------------------------------------------- rest --

        /// <summary>The unit with this id rests from duels (by the id a duel stored: its actor may already be someone else).</summary>
        internal static void Rest(long id)
        {
            if (id >= 0)
            {
                _rest[id] = Fighter.Now + RestSeconds;
            }
        }

        private static bool Resting(Actor a)
        {
            return _rest.TryGetValue(a.Sid(), out double until) && Fighter.Now < until;
        }

        // ------------------------------------------------------------ rivals --

        /// <summary>The id of a unit's sworn rival (0: none).</summary>
        public static long RivalOf(Actor a)
        {
            if (a == null || a.data == null)
            {
                return 0;
            }
            if (Fighters.TryGet(a, out Fighter f) && f.Rival != 0)
            {
                return f.Rival;
            }
            a.data.get(KeyRival, out string s, null);
            return s != null && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0;
        }

        public static bool Rivals(Actor a, Actor b)
        {
            return a != null && b != null && b.data != null && RivalOf(a) == b.Sid();
        }

        /// <summary>A duel ended with both alive: they become rivals once they have really parted.</summary>
        internal static void Parted(Actor a, Actor b)
        {
            if (a == null || b == null || a.data == null || b.data == null)
            {
                return;
            }
            _parted.Add(new Parting { A = a, B = b, IdA = a.Sid(), IdB = b.Sid(), At = Fighter.Now });
        }

        private static void Partings()
        {
            if (_parted.Count == 0)
            {
                return;
            }
            double now = Fighter.Now;
            for (int i = _parted.Count - 1; i >= 0; i--)
            {
                if (i >= _parted.Count)
                {
                    continue;
                }
                Parting p = _parted[i];
                bool alive = p.A != null && p.B != null && p.A.Sid() == p.IdA && p.B.Sid() == p.IdB && p.A.isAlive() && p.B.isAlive();
                if (!alive)
                {
                    _parted.RemoveAt(i);   // one fell (by the other's hand, OnKilled settled it)
                    continue;
                }
                if (now - p.At >= PartFor || (p.A.current_position - p.B.current_position).sqrMagnitude > 15f * 15f)
                {
                    _parted.RemoveAt(i);
                    MakeRivals(p.A, p.B);
                }
            }
        }

        /// <summary>One of two who just walked away from a duel cut the other down: that was the duel's end.</summary>
        private static bool SettleParting(Actor killer, Actor victim)
        {
            long k = killer.Sid(), v = victim.Sid();
            for (int i = 0; i < _parted.Count; i++)
            {
                Parting p = _parted[i];
                if ((p.IdA == k && p.IdB == v) || (p.IdA == v && p.IdB == k))
                {
                    _parted.RemoveAt(i);
                    Progression.DuelWon(killer, victim);
                    Trace("duel settled late: " + killer.asset.id + "#" + k + " cut down " + victim.asset.id + "#" + v + " (" + (Fighter.Now - p.At).ToString("F1") + " s after)");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Both lived through a duel: rivals from now on (the latest rivalry replaces an older one).</summary>
        internal static void MakeRivals(Actor a, Actor b)
        {
            bool already = Rivals(a, b) && Rivals(b, a);
            SetRival(a, b);
            SetRival(b, a);
            if (already)
            {
                return;
            }
            Trace("rivals: " + a.Sid() + " and " + b.Sid());
            Vector2 mid = (a.current_position + b.current_position) * 0.5f;
            Tier ta = Tiers.Of(a), tb = Tiers.Of(b);
            if (ta < Tier.Expert && tb < Tier.Expert && !a.isFavorite() && !b.isFavorite())
            {
                return;
            }
            if (!Fx.OnScreen(mid, 0.1f) && !a.isFavorite() && !b.isFavorite())
            {
                return;
            }
            try
            {
                Tier top = ta > tb ? ta : tb;
                WorldTip.showNow(Locale.Format("sakuga_duel_rivals", a.getName(), b.getName()), false, "top", 2.5f, Tiers.Info(top)?.Hex ?? "#FFFFFF");
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] rivals tip: " + e);
            }
        }

        private static void SetRival(Actor a, Actor b)
        {
            if (a == null || a.data == null || b == null || b.data == null)
            {
                return;
            }
            long id = b.Sid();
            a.data.set(KeyRival, id.ToString(CultureInfo.InvariantCulture));
            if (Fighters.TryGet(a, out Fighter f))
            {
                f.Rival = id;
            }
        }

        private static void ForgetRival(Actor a)
        {
            if (a == null || a.data == null)
            {
                return;
            }
            a.data.removeString(KeyRival);
            if (Fighters.TryGet(a, out Fighter f))
            {
                f.Rival = 0;
            }
        }

        /// <summary>A fighter seen for the first time (a loaded world): its rival comes back from the unit's data.</summary>
        private static void LoadRival(Fighter f)
        {
            if (f.Rival != 0 || f.A.data == null)
            {
                return;
            }
            f.A.data.get(KeyRival, out string s, null);
            if (s != null && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            {
                f.Rival = id;
            }
        }

        /// <summary>A fighter with a rival goes for it when it's an enemy within 30 tiles.</summary>
        private static void Seek(Fighter f)
        {
            if (f.Rival == 0 || !Settings.Duels)
            {
                return;
            }
            Actor a = f.A;
            if (Moves.Busy(a) || Director.InScene(a) || Of(a) != null || a.isTask("run_away"))
            {
                return;
            }
            Actor r = World.world.units.get(f.Rival);
            if (r == null || !r.isAlive())
            {
                ForgetRival(a);   // gone from the world: the rivalry ends with it
                return;
            }
            if (!Combat.IsFoe(a, r))
            {
                return;
            }
            Vector2 p = a.current_position;
            float d2 = (r.current_position - p).sqrMagnitude;
            if (d2 > SeekRange * SeekRange || (a.has_attack_target && a.attack_target == r))
            {
                return;
            }
            // it won't turn its back on someone at arm's length for it, unless the rival is close too
            if (a.has_attack_target && a.attack_target != null && !a.attack_target.isRekt()
                && (a.attack_target.current_position - p).sqrMagnitude < 4f && d2 > 16f)
            {
                return;
            }
            a.setAttackTarget(r);
            Trace("rival seek: " + a.Sid() + " -> " + r.Sid() + " (" + Mathf.Sqrt(d2).ToString("F1") + " tiles)");
        }

        // ------------------------------------------------------------- kills --

        private static void OnKilled(Actor killer, Actor victim)
        {
            try
            {
                if (victim == null)
                {
                    return;
                }
                Of(victim)?.Died(victim, killer);
                if (killer == null || killer.data == null)
                {
                    return;
                }
                if (_parted.Count > 0)
                {
                    SettleParting(killer, victim);
                }
                long rid = RivalOf(killer);
                if (rid == 0 || rid != victim.Sid())
                {
                    return;
                }
                ForgetRival(killer);
                Tier kt = Tiers.Of(killer);
                bool show = kt >= Tier.Master || killer.isFavorite() || victim.isFavorite() || Fx.OnScreen(victim.current_position, 0.1f);
                if (show)
                {
                    WorldTip.showNow(Locale.Format("sakuga_duel_rival_end", killer.getName(), victim.getName()), false, "top", 3.5f,
                        kt != Tier.None ? Tiers.Info(kt).Hex : "#FFFFFF");
                }
                Trace("rival's end: " + killer.Sid() + " killed " + victim.Sid());
            }
            catch (Exception e)
            {
                SakugaMod.Report("duel kill", e);
            }
        }

        // ------------------------------------------------------------ pacing --

        /// <summary>
        /// Hooks.BeforeDamage: a blow between two duelists outside their exchanges (a technique in the
        /// free fighting, a plain swing, an arrow) is softened, and never lethal: only the Final
        /// Exchange ends a duel. The blow is replaced by the softened one (same attacker, same kind);
        /// a duelist already at the brink shrugs it off. Blows from anyone else are untouched.
        /// </summary>
        private static bool Pace(Actor v, float dmg, AttackType type, BaseSimObject by)
        {
            if (_reissue || OwnBlow > 0 || _duels.Count == 0 || v == null || by == null || dmg <= 0f)
            {
                return false;
            }
            if (type != AttackType.Weapon && type != AttackType.Other)
            {
                return false;   // fire, lightning, divine powers: the world's own business
            }
            try
            {
                if (!by.isActor() || by.a == null)
                {
                    return false;
                }
                Duel d = Of(v);
                if (d == null)
                {
                    return false;
                }
                Actor x = by.a;
                Actor o = d.OpponentOf(v);
                if (o == null || o.Sid() != x.Sid())
                {
                    return false;
                }
                int hp = v.getHealth();
                if (hp <= 0)
                {
                    return false;
                }
                // a technique of the other's (the attacker is performing a move that isn't the duel's own)
                // (a wave or crack still running after its move ended counts as that technique)
                Move m = Combat.Landing != null && Combat.Landing.Id == x.Sid() ? Combat.Landing : null;
                if (m == null)
                {
                    Moves.TryGet(x, out m);
                }
                bool tech = m != null && !(m is Duelist);
                float scale = tech ? d.TechScale : d.SwingScale;
                float armor = Mathf.Min(v.stats["armor"], 95f);
                float through = Mathf.Max(0.05f, 1f - armor / 100f);
                const float keep = 3f;   // health a duelist keeps, whatever it takes outside the exchanges
                // the duel's course: nothing the other deals takes it under its floor before its time
                bool deep = tech && m.Tech != null && m.Tech.GodAbility;
                float floor = hp - d.FloorHp(v, deep);
                if (!tech && v._shake_active)
                {
                    // a reeling unit: the game's own rule decides whether a plain blow lands at all
                    // (swings skip it, arrows don't), so it passes untouched while it stays within what
                    // the duel allows; beyond that it glances off
                    if (Mathf.Max(1f, dmg * through) <= Mathf.Min(hp - keep, floor))
                    {
                        return false;
                    }
                    Graze(v);
                    return true;
                }
                // what may still get through: down to its floor, never to its death, and of a technique
                // at most its share (Duel.TechCap)
                float room = Mathf.Min(floor, hp - keep);
                bool share = false;
                if (tech)
                {
                    float r = d.TechRoom(x, m, v);
                    share = r < room;
                    room = Mathf.Min(room, r);
                }
                if (room < 1f)
                {
                    Graze(v);   // it has had all it may take for now: the blow glances off (it still flashes)
                    return true;
                }
                float want = dmg * scale;
                float take = Mathf.Max(1f, want * through);
                bool cut = take > room;
                if (cut)
                {
                    want = room / through - 0.01f;
                    take = room;
                    if (Dev.On)
                    {
                        Trace("pace: " + v.asset.id + "#" + v.Sid() + " takes only part of " + x.asset.id + "'s " + (tech ? m.GetType().Name : "blow")
                            + (share ? " (it has had its share: " + d.TechCap.ToString("P0") + ")" : " (hp " + hp + ", floor " + d.Floor(v, deep).ToString("P0") + ")"));
                    }
                }
                if (tech)
                {
                    d.TechDealt(x, take);
                }
                if (!cut && scale >= 0.999f)
                {
                    return false;
                }
                _reissue = true;
                try
                {
                    v.getHit(want, true, type, by, false, false, true);
                }
                finally
                {
                    _reissue = false;
                }
                return true;
            }
            catch (Exception e)
            {
                SakugaMod.Report("duel pace", e);
                return false;
            }
        }

        /// <summary>A blow the duel's pacing takes whole still lands to the eye: the victim flashes red.</summary>
        private static void Graze(Actor v)
        {
            if (v.is_visible)
            {
                v.startColorEffect(ActorColorEffect.Red);
            }
        }

        // --------------------------------------------------------------- dev --

        /// <summary>Dev: the next exchange picked from the pool is this one (blade_lock, flurry, aerial, chase, ground_shaker, feint, circle, sky; Grandmasters and Gods also skyfall, tectonic, sky_battle, mountain_breaker, relentless), once.</summary>
        internal static void Force(string id)
        {
            _forced = string.IsNullOrEmpty(id) ? null : id;
        }

        internal static string TakeForced()
        {
            string s = _forced;
            _forced = null;
            return s;
        }

        internal static void Trace(string s)
        {
            if (Dev.On)
            {
                Dev.Trace(s);
            }
        }

        private static void Clear()
        {
            foreach (Duel d in _duels)
            {
                d.Over = true;   // the moves, scenes and cinematics are cleared by the world clear itself
            }
            _duels.Clear();
            _meetings.Clear();
            _parted.Clear();
            Watched = null;
            _rest.Clear();
            _watch.Clear();
            _frame = -1;
            _forced = null;
            _reissue = false;
            OwnBlow = 0;
            // (what the old world's fighters last showed: unit ids start over in the new one)
            _lastDuel.Clear();
            DuelAmbience.Clear();
        }
    }
}
