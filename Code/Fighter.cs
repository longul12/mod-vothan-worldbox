using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// What the mod remembers about a unit that knows techniques (a tier or a move trait) while
    /// the world runs: cooldowns, combo state, the God gauge... Not saved: a loaded world starts
    /// fresh (progress that matters, Sakuga XP, lives in the unit's own saved data).
    /// </summary>
    internal sealed class Fighter
    {
        public readonly Actor A;
        public readonly long Id;
        public Tier Tier;
        public TierInfo Info;

        /// <summary>Seconds (game time) until it next looks at the battlefield.</summary>
        public float ThinkIn;
        private readonly Dictionary<Technique, double> _ready = new Dictionary<Technique, double>();

        /// <summary>World time the last technique started / ended.</summary>
        public double LastStart = -999, LastEnd = -999;
        public Technique LastTech, PrevTech;

        // ---- chains
        public Link Link;
        private Actor _linkTarget;
        private long _linkTargetId = -1;

        /// <summary>Whom the open chain is on (remembered with its id: pooled actors get reused).</summary>
        public Actor LinkTarget
        {
            get => _linkTarget;
            set
            {
                _linkTarget = value;
                _linkTargetId = value.Sid();
            }
        }

        /// <summary>The link target, if it is still the same unit.</summary>
        public Actor LinkTargetNow => _linkTarget != null && _linkTargetId >= 0 && _linkTarget.Sid() == _linkTargetId ? _linkTarget : null;

        public double LinkUntil;
        /// <summary>Seconds until a fighter in a chain window looks again (it looks every few frames, not every one).</summary>
        public float LinkThinkIn;
        public int Chain;
        /// <summary>Extra damage from chaining (+10% per link).</summary>
        public float Heat;

        public float ManaAcc;

        // ---- battle state
        public double LastCombat = -999;
        public double CombatSince = -1;
        public float LowestHp = 1f;
        public bool SecondWindUsed;
        public double BoostUntil;
        public float BoostDamage;
        /// <summary>Last Light: awakened until (moves are free, faster, stronger).</summary>
        public double AwakenedUntil;

        // ---- god gauge (0..100)
        public float Gauge;
        public double GaugeFullSince = -1;
        private readonly Dictionary<string, double> _timers = new Dictionary<string, double>();

        /// <summary>Scene the unit is part of (a duel, a team technique...), so two scenes never claim it.</summary>
        public object Scene;
        public double SceneUntil;

        /// <summary>Its sworn rival (duels that both survived).</summary>
        public long Rival;

        // ---- dynamics (Code/Dynamics)
        private Persona _persona;
        private int _personaSig = -1;

        /// <summary>
        /// Its fighting personality (brawler, counter-puncher, out-fighter...): stable for this unit
        /// (seeded by its id), shaped by its traits, its people and the schools it walks. Built when
        /// first asked and again only when what shapes it changes (a new school, a new tier).
        /// </summary>
        public Persona Persona
        {
            get
            {
                int sig = Personas.Signature(A, Tier);
                if (_persona == null || sig != _personaSig)
                {
                    _persona = Personas.Make(A, Tier);
                    _personaSig = sig;
                }
                return _persona;
            }
        }

        /// <summary>What it remembers of each opponent it fought lately (their habits, what worked on them).</summary>
        public readonly List<FoeNotes> Notes = new List<FoeNotes>(4);

        /// <summary>The notes on this opponent (made if it has none yet; the oldest are forgotten).</summary>
        public FoeNotes NotesOn(Actor v)
        {
            return FoeNotes.Of(this, v, true);
        }

        /// <summary>The notes on this opponent if it has any (null otherwise).</summary>
        public FoeNotes Recall(Actor v)
        {
            return FoeNotes.Of(this, v, false);
        }

        /// <summary>The counter depth of the chain window it has open (a counter-attack chained off a reactive answer).</summary>
        public int LinkDepth;

        /// <summary>
        /// Until when (world time) it has something to answer right now (a foe near it began to charge
        /// or aimed a technique at it, Reads.Alert): it looks at once and may cut its pause short.
        /// </summary>
        public double AlertUntil = -99;

        private Form _arsenal, _answers;
        private double _arsenalAt = -99;

        /// <summary>Every Form of the techniques it knows (refreshed every few seconds): what it could throw.</summary>
        public Form Arsenal
        {
            get
            {
                RefreshArsenal();
                return _arsenal;
            }
        }

        /// <summary>Every Form its reactive techniques answer (refreshed every few seconds).</summary>
        public Form AnswersKnown
        {
            get
            {
                RefreshArsenal();
                return _answers;
            }
        }

        private void RefreshArsenal()
        {
            double now = Now;
            if (now - _arsenalAt < 4.0 && _arsenalAt <= now)
            {
                return;
            }
            _arsenalAt = now;
            Form all = Form.None, ans = Form.None;
            IReadOnlyList<Technique> list = Techniques.All;
            for (int i = 0; i < list.Count; i++)
            {
                Technique t = list[i];
                int level = Techniques.Level(A, t, Tier);
                if (level <= 0)
                {
                    continue;
                }
                all |= t.FormsAt(level);
                if (t.Reactive)
                {
                    ans |= t.Answers == Form.None ? Form.Strike : t.Answers;
                }
            }
            _arsenal = all;
            _answers = ans;
        }

        private readonly Technique[] _chainA = new Technique[4], _chainB = new Technique[4];
        private int _chainI;

        /// <summary>Remembers the last few chains (opener, follow-up) so the Brain doesn't replay the same combo.</summary>
        public void RememberChain(Technique a, Technique b)
        {
            _chainA[_chainI] = a;
            _chainB[_chainI] = b;
            _chainI = (_chainI + 1) % _chainA.Length;
        }

        public bool ChainSeen(Technique a, Technique b)
        {
            for (int i = 0; i < _chainA.Length; i++)
            {
                if (_chainA[i] == a && _chainB[i] == b)
                {
                    return true;
                }
            }
            return false;
        }

        public Fighter(Actor a)
        {
            A = a;
            Id = a.Sid();
            Refresh();
            ThinkIn = Random.Range(0f, 0.6f);
        }

        public void Refresh()
        {
            Tier t = Tiers.Of(A);
            if (t != Tier)
            {
                _arsenalAt = -99;   // a new tier knows other levels: look again
            }
            Tier = t;
            Info = Tiers.Info(Tier);
        }

        public static double Now => World.world != null ? World.world.getCurWorldTime() : 0;

        public bool Ready(Technique t)
        {
            return !_ready.TryGetValue(t, out double at) || Now >= at;
        }

        public float CooldownLeft(Technique t)
        {
            return _ready.TryGetValue(t, out double at) ? Mathf.Max(0f, (float)(at - Now)) : 0f;
        }

        public void SetCooldown(Technique t, float seconds)
        {
            _ready[t] = Now + seconds;
        }

        public void ResetCooldowns()
        {
            _ready.Clear();
        }

        /// <summary>When a timer was last marked (-1: never).</summary>
        public double MarkedAt(string name)
        {
            return _timers.TryGetValue(name, out double at) ? at : -1;
        }

        /// <summary>A named timer (god abilities, auras...): has 'seconds' passed since Mark(name)?</summary>
        public bool Elapsed(string name, float seconds)
        {
            return !_timers.TryGetValue(name, out double at) || Now - at >= seconds;
        }

        public void Mark(string name)
        {
            _timers[name] = Now;
        }

        public bool InCombat => Now - LastCombat < 3.0;

        public bool Linked => Link != Link.None && Now <= LinkUntil;

        public bool InScene => Scene != null && Now <= SceneUntil;

        public bool Valid => A != null && A.Sid() == Id && A.isAlive();
    }

    internal static class Fighters
    {
        private static readonly Dictionary<long, Fighter> _map = new Dictionary<long, Fighter>();
        private static readonly List<Fighter> _list = new List<Fighter>();
        private static float _scan;

        public static IReadOnlyList<Fighter> All => _list;

        public static int Count => _list.Count;

        public static Fighter Get(Actor a)
        {
            if (a == null)
            {
                return null;
            }
            long id = a.Sid();
            if (_map.TryGetValue(id, out Fighter f))
            {
                if (f.A == a)
                {
                    return f;
                }
                Remove(f);
            }
            f = new Fighter(a);
            _map[id] = f;
            _list.Add(f);
            return f;
        }

        public static bool TryGet(Actor a, out Fighter f)
        {
            f = null;
            return a != null && _map.TryGetValue(a.Sid(), out f) && f.A == a;
        }

        public static void Remove(Fighter f)
        {
            _map.Remove(f.Id);
            _list.Remove(f);
        }

        public static void Clear()
        {
            _map.Clear();
            _list.Clear();
            _normalize.Clear();
            _scan = 0f;
        }

        /// <summary>Damage multiplier from chains, boosts and awakening.</summary>
        public static float DamageMult(Actor a)
        {
            if (!TryGet(a, out Fighter f))
            {
                return 1f;
            }
            float m = 1f + f.Heat;
            double now = Fighter.Now;
            if (now < f.BoostUntil)
            {
                m += f.BoostDamage;
            }
            if (now < f.AwakenedUntil)
            {
                m += 0.25f;
            }
            return m;
        }

        public static bool Awakened(Actor a)
        {
            return TryGet(a, out Fighter f) && Fighter.Now < f.AwakenedUntil;
        }

        /// <summary>A unit gained or lost a Sakuga trait.</summary>
        public static void Touch(Actor a)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            if (Techniques.AnyTrait(a))
            {
                Get(a).Refresh();
                // stacked tiers are merged on the next frame, never here: this runs from the trait
                // callbacks, and a loading unit's traits are being enumerated right now (removing one
                // would break the game's loop and the whole load)
                _normalize.Add(new KeyValuePair<Actor, long>(a, a.Sid()));
            }
            else if (TryGet(a, out Fighter f))
            {
                Remove(f);
            }
        }

        private static List<KeyValuePair<Actor, long>> _normalize = new List<KeyValuePair<Actor, long>>();
        private static List<KeyValuePair<Actor, long>> _normalizing = new List<KeyValuePair<Actor, long>>();

        /// <summary>Merges the stacked tiers of the units touched since the last frame.</summary>
        private static void NormalizeTouched()
        {
            // (merging removes traits, whose callbacks touch the unit again: work on a swapped list)
            List<KeyValuePair<Actor, long>> list = _normalize;
            _normalize = _normalizing;
            _normalizing = list;
            for (int i = 0; i < list.Count; i++)
            {
                Actor a = list[i].Key;
                if (a == null || a.Sid() != list[i].Value || !a.isAlive())
                {
                    continue;
                }
                try
                {
                    Progression.Normalize(a);
                    if (TryGet(a, out Fighter f))
                    {
                        f.Refresh();
                    }
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("normalize", e);
                }
            }
            list.Clear();
        }

        /// <summary>
        /// Every couple of seconds: picks up units that got Sakuga traits some other way
        /// (a loaded save, another mod) and forgets the dead.
        /// </summary>
        public static void Maintain(float realDt)
        {
            if (_normalize.Count > 0 && World.world != null)
            {
                NormalizeTouched();
            }
            _scan -= realDt;
            if (_scan > 0f || World.world == null)
            {
                return;
            }
            _scan = 2f;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                Fighter f = _list[i];
                if (!f.Valid || !Techniques.AnyTrait(f.A))
                {
                    _map.Remove(f.Id);
                    _list.RemoveAt(i);
                }
                else
                {
                    Progression.Normalize(f.A);
                    f.Refresh();
                }
            }
            foreach (Actor a in World.world.units.getSimpleList())
            {
                if (a != null && a.isAlive() && !_map.ContainsKey(a.Sid()) && Techniques.AnyTrait(a))
                {
                    Get(a);
                }
            }
        }
    }
}
