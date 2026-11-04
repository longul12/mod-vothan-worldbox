using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// REACTIONS: how a fighter answers a technique that reaches it (Combat.Reach). The old way, the
    /// first reactive technique in the list always had the first say: a fight was a rehearsal. Now:
    ///
    ///   1. the defender READS the blow once: its tier (and the difference with the attacker's), a
    ///      blow from behind is harder, a light flow strike is read like an ordinary blow, its
    ///      persona's reading skill, what it remembers of this attacker (a habit it has seen is read
    ///      better; a feint it has learned fools it less), how squarely the answers it knows meet
    ///      this kind of attack, how many it knows, and how deep in a chain of counters this is;
    ///   2. if it reads it, its ready answers are tried in an order weighed by how well each one's
    ///      Answers meet the incoming Forms, its persona's taste, the attacker's habits, and chance:
    ///      the same blow is met by a slip one time, a parry the next.
    ///
    /// COUNTER THE COUNTER. An answer's move is itself readable (Counter, CounterDepth + 1), so the
    /// original attacker may answer it in turn, even caught in its own broken move (its recovery is
    /// cut short: Free) or a fresh stagger: the boxing match's counter to the counter. Each layer is
    /// harder (x0.6 per layer) and only the strongest read deep: Novices and Adepts answer attacks,
    /// Experts and Masters a counter, Grandmasters and Gods a counter to their counter (a tactician
    /// one layer more, at most three).
    ///
    /// Technique authors: React starts with Reactions.Accept and calls Reactions.Free before it
    /// starts its move (DYNAMICS.md).
    /// </summary>
    internal static class Reactions
    {
        private struct Cand
        {
            public Technique T;
            public int Level;
            public float W;
        }

        private static readonly List<Cand> _cands = new List<Cand>();

        // the read being answered now (the dispatch)
        private static bool _dispatching;
        private static Move _incoming;
        private static Actor _defender;

        /// <summary>Dev staging (Stage): this unit always reads, and answers only with this technique.</summary>
        internal static readonly Dictionary<long, string> DevForce = new Dictionary<long, string>();

        /// <summary>Blows read / let through unread / answered, by counter depth (dev statistics).</summary>
        internal static int ReadsRolled, Unread;
        internal static readonly int[] ByDepth = new int[4];
        internal static readonly Dictionary<string, int> ByTech = new Dictionary<string, int>();

        /// <summary>How many layers of counters this fighter reads (it answers a move of CounterDepth below this).</summary>
        public static int MaxDepth(Fighter f)
        {
            if (f == null)
            {
                return 1;
            }
            int d = f.Tier >= Tier.Grandmaster ? 3 : f.Tier >= Tier.Expert ? 2 : 1;
            return Mathf.Clamp(d + f.Persona.Depth, 1, 3);
        }

        /// <summary>
        /// Can 'v' answer 'incoming' at all right now? Free and on its feet; or, to answer a counter, in
        /// its own move that is recovering (its attack was just answered) or freshly staggered, and only
        /// if it reads that deep.
        /// </summary>
        public static bool Ready(Actor v, Move incoming)
        {
            if (!Combat.Living(v))
            {
                return false;
            }
            int depth = incoming != null ? incoming.CounterDepth : 0;
            Fighters.TryGet(v, out Fighter f);
            if (depth > 0 && depth >= MaxDepth(f))
            {
                return false;
            }
            if (Moves.CanPerform(v))
            {
                return true;
            }
            if (depth <= 0)
            {
                return false;
            }
            // a counter to a counter: from inside its own broken move, or through a fresh stagger
            bool recovering = Reads.Recovering(v);
            if (!recovering)
            {
                return false;
            }
            bool shake = f != null && f.Tier >= Tier.Expert;
            return Moves.CanPerform(v, true, shake);
        }

        /// <summary>
        /// Frees 'v' to start its answer: a recovering move of its own is cut short, a fresh stun shaken
        /// off. Call it right before starting the reactive move (no-op for a unit that is already free).
        /// </summary>
        public static void Free(Actor v)
        {
            if (v == null)
            {
                return;
            }
            // (read before the cut: the recovery beat of its own move is what Ready approved it by, and
            // that move is gone once cancelled)
            bool recovering = Reads.Recovering(v);
            if (Moves.TryGet(v, out Move cur) && cur.A == v && !cur.Interruptible && !(cur is Puppet) && !cur.Charging)
            {
                Moves.Cancel(cur);
            }
            if (recovering && v.hasStatusStunned())
            {
                Unstun(v);
            }
        }

        /// <summary>
        /// Takes a stun off at once. The game's finishStatusEffect only flags it: the unit would stay
        /// stunned (and unconscious) until the next status sweep, so every answer's own Moves.CanPerform
        /// would still refuse it, after its stun was spent anyway.
        /// </summary>
        private static void Unstun(Actor v)
        {
            if (!v.getStatusesDict().TryGetValue("stunned", out Status s) || s == null || World.world == null || World.world.statuses == null)
            {
                v.finishStatusEffect("stunned");
                return;
            }
            s.finish();
            StatusManager sm = World.world.statuses;
            sm.removeObject(s);   // out of the unit (removeFinishedStatusEffect) and the manager
            sm.list.Remove(s);    // and out of this frame's list: the sweep never removes it twice (a new stun keeps its place)
            try
            {
                v.updateStats();   // the stun's 'unconscious' tag goes with it
            }
            catch (Exception e)
            {
                SakugaMod.Report("unstun", e);
            }
        }

        /// <summary>
        /// The first line of a reactive technique's React: true if 'defender' takes this blow on with
        /// 'tech'. Inside the dispatch the blow is already read: it only checks that the defender can
        /// move and that the technique answers this kind of attack. Called any other way it rolls the
        /// read itself.
        /// </summary>
        public static bool Accept(Technique tech, Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (tech == null || defender == null || attacker == null || defender == attacker || !Ready(defender, incoming))
            {
                return false;
            }
            Form inc = Reads.FormsOf(incoming) | (area ? Form.Area : Form.None);
            float match = tech.Answers == Form.None ? 0.6f : FormSet.Match(tech.Answers, inc);
            if (match <= 0f)
            {
                return false;
            }
            if (_dispatching && _defender == defender && _incoming == incoming)
            {
                return true;
            }
            Fighters.TryGet(defender, out Fighter f);
            return Random.value < ReadChance(defender, f, attacker, incoming, area, match, 1);
        }

        /// <summary>
        /// The chance that 'd' reads the blow of 'atk' ('m', null for a plain blow): see the class
        /// summary. 'match' how squarely its best answer meets it, 'known' how many answers it has ready.
        /// </summary>
        public static float ReadChance(Actor d, Fighter f, Actor atk, Move m, bool area, float match, int known)
        {
            Tier td = f != null ? f.Tier : Tiers.Of(d);
            TierInfo info = Tiers.Info(td);
            float c = info != null ? info.React : 0.3f;
            c += Mathf.Clamp(0.1f * ((int)td - (int)Tiers.Of(atk)), -0.3f, 0.3f);
            float dx = atk.current_position.x - d.current_position.x;
            if (Mathf.Abs(dx) > 0.2f && dx > 0f != d.flip)
            {
                c *= 0.5f;   // from behind
            }
            if (m != null && m.Interruptible)
            {
                c *= 0.5f;   // a flow strike: the everyday blows, read like ordinary ones
            }
            Form inc = Reads.FormsOf(m) | (area ? Form.Area : Form.None);
            if (f != null)
            {
                Persona p = f.Persona;
                c *= p.ReadSkill;
                FoeNotes n = f.Recall(atk);
                if (n != null)
                {
                    c *= 1f + 0.35f * n.HabitOf(inc & ~(Form.Fast | Form.Heavy)) * p.Adapt;
                    if ((inc & Form.Feint) != 0)
                    {
                        c *= n.Fooled > 0 && p.Patience > 0.5f ? 0.5f : 1.15f;   // the patient learn a feint; the rest bite
                    }
                }
            }
            c *= 0.85f + 0.25f * Mathf.Clamp01(match);
            if (known > 1)
            {
                c = 1f - Mathf.Pow(1f - Mathf.Clamp01(c), 1f + 0.3f * Mathf.Min(known - 1, 3));
            }
            int depth = m != null ? m.CounterDepth : 0;
            if (depth > 0)
            {
                c *= Mathf.Pow(0.6f, depth);
            }
            return Mathf.Clamp(c, 0.02f, 0.92f);
        }

        /// <summary>
        /// Combat.Reach's contact handler (through Techniques): 'v' is about to be reached by 'm'. Reads
        /// it once, then tries its ready answers in a weighed, shuffled order. The answer that takes it
        /// pays its mana and cooldown here, and its move becomes one layer deeper in the chain of counters.
        /// </summary>
        public static Contact Dispatch(Move m, Actor v, bool area)
        {
            if (m == null || m.A == null || v == null || v == m.A || !Combat.Living(v) || _dispatching)
            {
                return Contact.Hit;   // (an answer's own first contact inside a dispatch is not answered from inside it)
            }
            if (!Fighters.TryGet(v, out Fighter f))
            {
                if (!Techniques.AnyTrait(v))
                {
                    return Contact.Hit;   // a plain unit knows no answers
                }
                f = Fighters.Get(v);
            }
            if (!Ready(v, m))
            {
                if (Dev.On && m.CounterDepth > 0)
                {
                    Dev.Trace("counter of " + m.A.asset.id + "#" + m.A.Sid() + " (depth " + m.CounterDepth + ") reaches " + v.asset.id + "#" + v.Sid()
                        + ": it can't answer (reads " + MaxDepth(f) + " deep, recovering " + Reads.Recovering(v) + ", move "
                        + (Moves.TryGet(v, out Move cur) ? cur.GetType().Name : "-") + ", stunned " + v.hasStatusStunned() + ")");
                }
                return Contact.Hit;
            }
            Form inc = Reads.FormsOf(m) | (area ? Form.Area : Form.None);
            FoeNotes notes = f.Recall(m.A);
            Persona p = f.Persona;
            string forced = null;
            if (Dev.On && DevForce.Count > 0)
            {
                DevForce.TryGetValue(v.Sid(), out forced);
            }
            _cands.Clear();
            float best = 0f;
            IReadOnlyList<Technique> list = Techniques.Reactives;
            for (int i = 0; i < list.Count; i++)
            {
                Technique t = list[i];
                if (forced != null && t.Id != forced)
                {
                    continue;
                }
                int level = Techniques.Level(v, t, f.Tier);
                if (level <= 0 || !f.Ready(t) || !Mana.Has(v, t, level, false))
                {
                    continue;
                }
                float match = t.Answers == Form.None ? 0.6f : FormSet.Match(t.Answers, inc);
                if (match <= 0f)
                {
                    continue;
                }
                best = Mathf.Max(best, match);
                float w = (0.4f + match) * p.Affinity(t.FormsAt(level));
                if (notes != null)
                {
                    // readied: the answer to what this foe keeps doing comes first
                    w *= 1f + 0.6f * notes.HabitOf(t.Answers == Form.None ? Form.Strike : t.Answers) * p.Adapt;
                }
                w *= Random.Range(0.55f, 1.45f);
                _cands.Add(new Cand { T = t, Level = level, W = w });
            }
            if (_cands.Count == 0)
            {
                return Contact.Hit;
            }
            ReadsRolled++;
            if (forced == null && Random.value >= ReadChance(v, f, m.A, m, area, best, _cands.Count))
            {
                Unread++;
                return Contact.Hit;
            }
            _cands.Sort((x, y) => y.W.CompareTo(x.W));
            int depth = m.CounterDepth;
            _dispatching = true;
            _incoming = m;
            _defender = v;
            try
            {
                for (int i = 0; i < _cands.Count; i++)
                {
                    Cand cd = _cands[i];
                    Contact c;
                    try
                    {
                        c = cd.T.React(v, m.A, m, cd.Level, area);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Sakuga] react " + cd.T.Id + ": " + e);
                        continue;
                    }
                    if (c == Contact.Hit)
                    {
                        continue;
                    }
                    Answered(f, v, m, cd.T, cd.Level, depth, c, inc);
                    return c;
                }
            }
            finally
            {
                _dispatching = false;
                _incoming = null;
                _defender = null;
            }
            if (Dev.On && (depth > 0 || forced != null))
            {
                Dev.Trace("read: " + v.asset.id + "#" + v.Sid() + " read " + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + " (depth " + depth
                    + ") but no answer could start (" + _cands.Count + " tried; can perform " + Moves.CanPerform(v) + ", h " + v.position_height.ToString("F2") + ")");
            }
            return Contact.Hit;
        }

        /// <summary>The answer took the blow: it pays, its move goes one layer deeper, and it is remembered.</summary>
        private static void Answered(Fighter f, Actor v, Move m, Technique t, int level, int depth, Contact c, Form inc)
        {
            Mana.Spend(v, t, level, false);
            f.SetCooldown(t, t.CooldownFor(v, level));
            Director.Used(t, v.current_position);
            Brain.Started.TryGetValue(t.Id, out int n);
            Brain.Started[t.Id] = n + 1;
            ByTech.TryGetValue(t.Id, out int k);
            ByTech[t.Id] = k + 1;
            ByDepth[Mathf.Clamp(depth, 0, 3)]++;
            if (Moves.TryGet(v, out Move nm) && nm.A == v && nm != m && !(nm is Puppet))
            {
                nm.CounterDepth = Mathf.Max(nm.CounterDepth, depth + 1);
                if (nm.Tech == null)
                {
                    nm.Tech = t;
                    nm.Level = level;
                }
            }
            if ((inc & Form.Feint) != 0 && (t.Answers & Form.Feint) == 0)
            {
                Reads.Fooled(m.A, v, m);   // it bit on a feint (an answer that sees through one doesn't)
            }
            if (Dev.On)
            {
                Dev.Trace("react " + t.Id + " " + Techniques.Roman(level) + " by " + v.asset.id + "#" + v.Sid() + " (" + f.Persona.Main + ") against "
                    + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + " [" + FormSet.Name(inc) + "] depth " + depth + ": " + c);
            }
            try
            {
                Hooks.Answered?.Invoke(v, m.A, t, depth, c);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] answered hook: " + e);
            }
        }

        // -------------------------------------------------------- plain blows --

        private const string CasualTimer = "sakuga_dyn_casual";
        internal static int OrdinaryAnswers;

        [SakugaInit]
        private static void Init()
        {
            Hooks.BeforeOrdinaryHit.Add(Ordinary);
            Hooks.HitDealt += CutOnHit;
        }

        /// <summary>
        /// Hooks.BeforeOrdinaryHit: a fighter about to take an ordinary melee blow of the game (a plain
        /// sword swing, a punch) may meet it with an answer that knows how (Technique.ReactOrdinary): a
        /// swing at the head ducked and swept, one from above slipped and hooked. A plain blow comes in
        /// one of three ways (High, Overhead or Straight, by chance); the read is 0.4 of the usual,
        /// once a second at most: every tier's fighting gets its answers, not only the masters'.
        /// </summary>
        private static bool Ordinary(AttackData d, Actor t)
        {
            if (d.is_projectile || d.initiator == null || !d.initiator.isActor() || t == null || !Fighters.TryGet(t, out Fighter f))
            {
                return false;
            }
            Actor a = d.initiator.a;
            if (a == null || a == t || !Combat.Living(a) || !f.Elapsed(CasualTimer, 1f) || !Moves.CanPerform(t))
            {
                return false;
            }
            float r = Random.value;
            Form forms = Reads.OrdinaryForms(a, d) | (r < 0.5f ? Form.High : r < 0.75f ? Form.Overhead : Form.Straight);
            Persona p = f.Persona;
            FoeNotes notes = f.Recall(a);
            _cands.Clear();
            float best = 0f;
            IReadOnlyList<Technique> list = Techniques.Reactives;
            for (int i = 0; i < list.Count; i++)
            {
                Technique tech = list[i];
                if (tech.Answers == Form.None || !tech.AnswersOrdinary)
                {
                    continue;   // (the generic answers have their own casual parries; the rest answer techniques only)
                }
                float match = FormSet.Match(tech.Answers, forms);
                if (match <= 0f)
                {
                    continue;
                }
                int level = Techniques.Level(t, tech, f.Tier);
                if (level <= 0 || !f.Ready(tech) || !Mana.Has(t, tech, level, false))
                {
                    continue;
                }
                best = Mathf.Max(best, match);
                float w = (0.4f + match) * p.Affinity(tech.FormsAt(level)) * Random.Range(0.55f, 1.45f);
                if (notes != null)
                {
                    w *= 1f + 0.6f * notes.HabitOf(tech.Answers) * p.Adapt;
                }
                _cands.Add(new Cand { T = tech, Level = level, W = w });
            }
            if (_cands.Count == 0)
            {
                return false;
            }
            f.Mark(CasualTimer);
            if (Random.value >= 0.4f * ReadChance(t, f, a, null, false, best, _cands.Count))
            {
                return false;
            }
            _cands.Sort((x, y) => y.W.CompareTo(x.W));
            for (int i = 0; i < _cands.Count; i++)
            {
                Cand cd = _cands[i];
                Moves.TryGet(t, out Move before);
                bool ok;
                try
                {
                    ok = cd.T.ReactOrdinary(t, a, forms, cd.Level);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] react ordinary " + cd.T.Id + ": " + e);
                    continue;
                }
                if (!ok)
                {
                    continue;
                }
                OrdinaryAnswers++;
                // the answer to a plain blow is a counter (depth 1), as the dispatch marks one: only the
                // strong read a counter to it
                if (Moves.TryGet(t, out Move nm) && nm.A == t && nm != before && !(nm is Puppet))
                {
                    nm.CounterDepth = Mathf.Max(nm.CounterDepth, 1);
                    if (nm.Tech == null)
                    {
                        nm.Tech = cd.T;
                        nm.Level = cd.Level;
                    }
                }
                Mana.Spend(t, cd.T, cd.Level, false);
                f.SetCooldown(cd.T, cd.T.CooldownFor(t, cd.Level));
                Director.Used(cd.T, t.current_position);
                Brain.Started.TryGetValue(cd.T.Id, out int n);
                Brain.Started[cd.T.Id] = n + 1;
                ByTech.TryGetValue(cd.T.Id, out int k);
                ByTech[cd.T.Id] = k + 1;
                if (Dev.On)
                {
                    Dev.Trace("react " + cd.T.Id + " " + Techniques.Roman(cd.Level) + " by " + t.asset.id + "#" + t.Sid() + " (" + p.Main + ") against a plain "
                        + (d.metallic_weapon ? "swing" : "blow") + " of " + a.asset.id + "#" + a.Sid() + " [" + FormSet.Name(forms) + "]");
                }
                try
                {
                    Hooks.Answered?.Invoke(t, a, cd.T, 0, Contact.Evaded);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] answered hook: " + e);
                }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------ answered moves --

        /// <summary>Answered moves a landing blow cut short (dev statistics).</summary>
        internal static int CutsShort;

        /// <summary>
        /// The foe a counter (or a punish) lands on is caught in a committed move of its own: the attack
        /// it answered ('incoming'), a counter of its own, a move it was just caught recovering from, or
        /// one showing 'answers' (the kind of attack this technique punishes). That move is cut short so
        /// the blow throws the body instead of only rocking it (a charge comes apart: Interrupts). Never
        /// someone's puppet, a cinematic, a light (interruptible) move, a move the engine already broke
        /// (it flies), a giant's or one its class may not throw (it only rocks). 'strict': only the
        /// attack it answers or one showing 'answers' (a grab doesn't pull a foe out of its own evasion).
        /// Combat.Hit does this on its own for a counter's blow (CutOnHit); call it before a blow that
        /// doesn't go through Combat.Hit, or to cut first. True if it cut.
        /// </summary>
        public static bool CutShort(Actor foe, Actor by, Move incoming = null, Form answers = Form.None, bool strict = false)
        {
            if (foe == null || !Combat.Living(foe) || !Moves.TryGet(foe, out Move m) || m.A != foe || m is Puppet || m.Ended || m.Finished
                || m.Interruptible || m.Cinematic || m is ChoreoMove cm && cm.Broken)
            {
                return false;
            }
            if (by != null && !Combat.Allowed(by, foe) || !Victims.Movable(foe, by))
            {
                return false;
            }
            if (by != null && Primordials.Any && Primordials.Is(foe) && !Primordials.Is(by))
            {
                return false;   // (nothing less than a Primordial cuts a Primordial's power short)
            }
            bool shows = answers != Form.None && (Reads.FormsOf(m) & answers) != 0;
            bool answered = strict ? m == incoming || shows : m == incoming || m.CounterDepth > 0 || Reads.Recovering(foe) || shows;
            if (!answered)
            {
                return false;
            }
            Cut(m, by);
            return true;
        }

        /// <summary>Ends 'm' where it stands (a charge comes apart instead), leaving no chain behind.</summary>
        private static void Cut(Move m, Actor by)
        {
            CutsShort++;
            if (Dev.On) Dev.Trace("cut short: " + (by != null ? by.asset.id + "#" + by.Sid() : "-") + " cuts " + m.A.asset.id + "#" + m.A.Sid() + "'s "
                + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + " short");
            if (m.Charging)
            {
                Interrupts.Disperse(m, by);
                return;
            }
            m.EndLink = Link.None;
            m.EndTarget = null;
            Moves.Cancel(m);
        }

        /// <summary>
        /// Hooks.HitDealt: a technique's blow landed on a unit still committed to a move of its own that
        /// was just parried or evaded (the attack a counter met, a counter met by a counter to it), or
        /// that is in its recovery. The move is cut short before the blow's reaction plays, so a counter
        /// throws an old hand-made technique's performer like any other body instead of only rocking it.
        /// A flow strike's blow (light) only rocks; charges are Interrupts' business; duels and set
        /// pieces keep their own pace.
        /// </summary>
        private static void CutOnHit(Actor a, Actor v, HitResult r, Move by)
        {
            if (!r.Landed || r.Killed || by == null || a == null || v == null || a == v || by.A != a)
            {
                return;
            }
            try
            {
                CutIfAnswered(a, v, by);
            }
            catch (Exception e)
            {
                SakugaMod.Report("cut short", e);
            }
        }

        private static void CutIfAnswered(Actor a, Actor v, Move by)
        {
            if (!Combat.Living(v))
            {
                return;
            }
            if (!Moves.TryGet(v, out Move m) || m.A != v || m == by || m is Puppet || m.Ended || m.Finished || m.Interruptible || m.Cinematic || m.Charging)
            {
                return;
            }
            if (by.Interruptible && by.CounterDepth <= 0)
            {
                return;
            }
            if (!m.Recovering)
            {
                // (the very move that was parried or evaded a moment ago: a counter's blow, a counter to
                // that counter, an ally's punish; never a fresh move it started since)
                Reads.Trace t = Reads.Of(v, false);
                if (t == null || t.AnsweredMove != m || Fighter.Now - t.AnsweredAt >= 1.5)
                {
                    return;
                }
            }
            if (m is ChoreoMove cm && cm.Broken || Duels.InDuel(v) || Director.InScene(v) || !Combat.Allowed(a, v) || !Victims.Movable(v, a))
            {
                return;
            }
            Cut(m, a);
        }

        /// <summary>The reactive techniques 'v' could answer these forms with right now (dev, Plan helpers).</summary>
        public static bool Knows(Actor v, Form incoming)
        {
            if (!Fighters.TryGet(v, out Fighter f))
            {
                return false;
            }
            return (f.AnswersKnown & incoming) != 0;
        }
    }
}
