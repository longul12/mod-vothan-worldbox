using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sakuga
{
    /// <summary>What a finished technique leaves behind, which the next one can chain from.</summary>
    internal enum Link
    {
        None,
        /// <summary>Right next to the target, on the ground.</summary>
        Close,
        /// <summary>The target is in the air.</summary>
        Launched,
        /// <summary>The target is stunned on the ground.</summary>
        Down,
        /// <summary>A crowd was blown apart.</summary>
        Scattered,
        /// <summary>The attacker itself is airborne (aerial follow-ups).</summary>
        SelfAir,
        /// <summary>The victim was thrown into a pile of enemies.</summary>
        Thrown
    }

    /// <summary>What a technique is for (the AI and the UI group them by this).</summary>
    internal enum Role
    {
        Mobility,
        Area,
        Single,
        Launcher,
        Defense,
        Line,
        Execute,
        Air,
        Chain,
        Team,
        God
    }

    /// <summary>One way a fighter could use a technique right now, and how good it is.</summary>
    internal sealed class Plan
    {
        public Technique Tech;
        public int Level;
        /// <summary>Main target (may be null for ground-targeted techniques).</summary>
        public Actor Target;
        /// <summary>Where (a landing point, the centre of an area...).</summary>
        public Vector2 Point;
        /// <summary>Which way (lines, dashes).</summary>
        public Vector2 Dir;
        /// <summary>Expected value; must beat an ordinary attack's to be chosen.</summary>
        public float Score;
        /// <summary>How many enemies it should catch.</summary>
        public int Count;
        /// <summary>Other units it involves (allies of a team technique, a chain of targets).</summary>
        public List<Actor> Others;
        /// <summary>Started as a link from the previous technique.</summary>
        public bool Linked;
        /// <summary>Anything else the technique wants to remember between Plan and Begin.</summary>
        public object Data;
        /// <summary>
        /// For area techniques: the radius it will strike around Point. Allies then avoid striking
        /// the same spot at the same time (no two Meteor Slams on one crowd).
        /// </summary>
        public float AreaRadius;
        /// <summary>What the Brain's tactics multiplied the plan's own score by (Tactics.Shape; dev log).</summary>
        public float Shape = 1f;
    }

    /// <summary>
    /// A special move: a trait you can give, a level (I-III) that comes from the unit's tier,
    /// a plan (when is it worth using) and a beginning (start its Move). Every concrete subclass
    /// in the mod is found and registered automatically, together with its trait.
    /// </summary>
    internal abstract class Technique
    {
        /// <summary>snake_case id; the trait is "sakuga_" + Id.</summary>
        public abstract string Id { get; }

        public string TraitId => "sakuga_" + Id;

        /// <summary>The tier that learns it at level I (one tier later II, two tiers later III; Gods know all at III).</summary>
        public abstract Tier Entry { get; }

        public abstract Role Role { get; }

        public virtual int MaxLevel => 3;

        /// <summary>
        /// Mana per level (index 0 = level I). Ordinary units with the bare trait pay nothing. Constant:
        /// it is read once and kept (Mana(level)).
        /// </summary>
        public abstract float[] ManaCost { get; }

        /// <summary>
        /// Seconds per level before it can be used again (times the tier's cooldown multiplier). Constant:
        /// it is read once and kept (CooldownFor).
        /// </summary>
        public abstract float[] Cooldown { get; }

        // (ManaCost and Cooldown are mostly written "=> new[] { ... }": a fresh array at every read, and
        // the Brain asks for them hundreds of times a think)
        private float[] _mana, _cooldown;

        /// <summary>Answers to someone else's technique instead of being planned (parries, evades).</summary>
        public virtual bool Reactive => false;

        /// <summary>One of the three God abilities (driven by the Sakuga gauge, not the planner).</summary>
        public virtual bool GodAbility => false;

        /// <summary>Needs allies (a team technique).</summary>
        public virtual bool Team => Role == Role.Team;

        /// <summary>
        /// The combat school it belongs to (None: every fighter's). A tiered fighter learns a school's
        /// techniques only if it walks that school (Schools); a bare trait still teaches it.
        /// </summary>
        public virtual School School => School.None;

        /// <summary>
        /// For a technique of no school: the schools whose fighters learn it with their tier (null: all of
        /// them). A sword saint does not call down a meteor of rock; a fighter of another school only
        /// uses such a technique when a player gives it the trait.
        /// </summary>
        public virtual School[] Affinity => Schools.AffinityOf(Id);

        /// <summary>A Primordial's power: no one else can use it, not even with the trait.</summary>
        public virtual bool PrimordialOnly => false;

        /// <summary>What kind of attack it is (others read it and answer it: see Form).</summary>
        public virtual Form Forms => Form.None;

        /// <summary>For counters and reactive techniques: the kinds of attack it answers.</summary>
        public virtual Form Answers => Form.None;

        /// <summary>The creatures it is made for (hunters' techniques; None: anyone).</summary>
        public virtual Prey Prey => Prey.None;

        /// <summary>How big an answer it is (a God flicks a dragon away with a Casual one).</summary>
        public virtual Scale Scale => Scale.Light;

        /// <summary>
        /// What it is at a given level, when the levels differ (a slash that only gathers a charge at
        /// III shows Charge there). Forms by default. The Brain and Move.Forms read this one.
        /// </summary>
        public virtual Form FormsAt(int level)
        {
            return Forms;
        }

        /// <summary>How big an answer it is at a given level (a quick hop at I, a cataclysm at III). Scale by default.</summary>
        public virtual Scale ScaleAt(int level)
        {
            return Scale;
        }

        /// <summary>Icon path under Resources ("ui/Icons/sakuga/iconFlashStep").</summary>
        public virtual string Icon => "ui/Icons/sakuga/icon" + Pascal(Id);

        /// <summary>Sorting in lists (lower first).</summary>
        public virtual int SortOrder => (int)Entry * 100;

        /// <summary>Can it start as a chain from this link?</summary>
        public virtual bool Accepts(Link link)
        {
            return false;
        }

        /// <summary>
        /// The best way to use it right now, or null. Called by the AI a few times a second per
        /// fighter, so keep it cheap: use the context's lists, never search the map yourself.
        /// </summary>
        public abstract Plan Plan(Ctx c, int level);

        /// <summary>Starts the move (Moves.Start). False if it couldn't (nothing is spent then).</summary>
        public abstract bool Begin(Actor a, Plan p);

        /// <summary>
        /// For reactive techniques: 'defender' (who knows this at 'level') is about to be reached by
        /// 'incoming' (a move of 'attacker'). Return Parried/Evaded if it answered (and started its own
        /// move), Hit to let the blow land. Reactions.Dispatch calls it (ready and affordable, its
        /// Answers matching the incoming Forms, the read already rolled): start with
        /// Reactions.Accept(this, ...) and Reactions.Free(defender) before starting the move (see
        /// DYNAMICS.md). Cooldown and mana are paid by the dispatcher.
        /// </summary>
        public virtual Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            return Contact.Hit;
        }

        /// <summary>
        /// For reactive techniques that can also meet an ordinary blow of the game (a plain sword swing,
        /// a punch; never a shot): start the answer on 'attacker' and return true (the blow is turned
        /// aside). 'forms' is what the blow looks like (Blade or Punch, Fast, and High, Overhead or
        /// Straight). Reactions reads it (a share of the usual chance, once a second at most), checks
        /// Answers and pays mana and cooldown. Default: it doesn't.
        /// </summary>
        public virtual bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            return false;
        }

        private int _ordinary = -1;

        /// <summary>
        /// Does it answer ordinary blows at all (overrides ReactOrdinary)? Reactions.Ordinary counts only
        /// these: the others' answers to a plain swing are always "no", and counting them raised the read
        /// chance and spent the casual timer for nothing.
        /// </summary>
        public bool AnswersOrdinary
        {
            get
            {
                if (_ordinary < 0)
                {
                    System.Reflection.MethodInfo m = GetType().GetMethod(nameof(ReactOrdinary), new[] { typeof(Actor), typeof(Actor), typeof(Form), typeof(int) });
                    _ordinary = m != null && m.DeclaringType != typeof(Technique) ? 1 : 0;
                }
                return _ordinary == 1;
            }
        }

        /// <summary>A god power click: perform it at this tile (ignores cooldown and mana).</summary>
        public virtual bool Order(Actor a, WorldTile tile, int level)
        {
            return false;
        }

        public float Mana(int level)
        {
            float[] m = _mana ??= ManaCost;
            return m == null || m.Length == 0 ? 0f : m[Mathf.Clamp(level - 1, 0, m.Length - 1)];
        }

        public float CooldownFor(Actor a, int level)
        {
            float[] cd = _cooldown ??= Cooldown;
            float s = cd == null || cd.Length == 0 ? 8f : cd[Mathf.Clamp(level - 1, 0, cd.Length - 1)];
            TierInfo info = Tiers.Info(Tiers.Of(a));
            float mult = info != null ? info.CooldownMult : 1f;
            if (Reactive)
            {
                mult = Mathf.Max(mult, 0.6f);   // answers stay rare enough to be special
            }
            // mastery: its own school's techniques come round sooner the higher it has risen above them
            mult *= Mathf.Max(0.55f, 1f - 0.07f * Schools.Mastery(a, this));
            return s * mult * Settings.Cooldowns;
        }

        public static string Pascal(string id)
        {
            StringBuilder sb = new StringBuilder();
            bool up = true;
            foreach (char ch in id)
            {
                if (ch == '_')
                {
                    up = true;
                    continue;
                }
                sb.Append(up ? char.ToUpperInvariant(ch) : ch);
                up = false;
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return Id;
        }
    }

    internal static class Techniques
    {
        private static readonly List<Technique> _all = new List<Technique>();
        private static readonly Dictionary<string, Technique> _byId = new Dictionary<string, Technique>();
        private static readonly Dictionary<Technique, ActorTrait> _trait = new Dictionary<Technique, ActorTrait>();
        private static readonly HashSet<ActorTrait> _sakugaTraits = new HashSet<ActorTrait>();

        public static IReadOnlyList<Technique> All => _all;

        /// <summary>Finds every Technique in the mod.</summary>
        public static void Discover()
        {
            _all.Clear();
            _byId.Clear();
            _reactive.Clear();
            foreach (Type t in typeof(Technique).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(Technique).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }
                try
                {
                    Technique tech = (Technique)Activator.CreateInstance(t);
                    if (_byId.ContainsKey(tech.Id))
                    {
                        Debug.LogError("[Sakuga] two techniques named " + tech.Id);
                        continue;
                    }
                    _all.Add(tech);
                    _byId[tech.Id] = tech;
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] technique " + t.Name + ": " + e);
                }
            }
            _all.Sort((x, y) => x.SortOrder != y.SortOrder ? x.SortOrder.CompareTo(y.SortOrder) : string.CompareOrdinal(x.Id, y.Id));
        }

        /// <summary>
        /// Adds a technique made at runtime (the Forge's recipes) before the traits are created. False if
        /// its id is taken (a duplicate is rejected, like a second class with the same id).
        /// </summary>
        public static bool Register(Technique tech)
        {
            if (tech == null || string.IsNullOrEmpty(tech.Id) || _byId.ContainsKey(tech.Id))
            {
                return false;
            }
            _all.Add(tech);
            _byId[tech.Id] = tech;
            _all.Sort((x, y) => x.SortOrder != y.SortOrder ? x.SortOrder.CompareTo(y.SortOrder) : string.CompareOrdinal(x.Id, y.Id));
            _reactive.Clear();   // (Reactives is built lazily: a reactive one registered late is picked up)
            return true;
        }

        /// <summary>After the traits exist.</summary>
        public static void LinkTraits()
        {
            _trait.Clear();
            _sakugaTraits.Clear();
            foreach (Technique t in _all)
            {
                ActorTrait tr = AssetManager.traits.get(t.TraitId);
                if (tr != null)
                {
                    _trait[t] = tr;
                    _sakugaTraits.Add(tr);
                }
            }
            for (int i = 1; i <= Tiers.Count; i++)
            {
                ActorTrait tr = Tiers.Trait((Tier)i);
                if (tr != null)
                {
                    _sakugaTraits.Add(tr);
                }
            }
            // (the Primordial is a fighter too: without it the Brain never sees one)
            if (Primordials.Trait != null)
            {
                _sakugaTraits.Add(Primordials.Trait);
            }
        }

        public static Technique Get(string id)
        {
            return id != null && _byId.TryGetValue(id, out Technique t) ? t : null;
        }

        public static ActorTrait Trait(Technique t)
        {
            return t != null && _trait.TryGetValue(t, out ActorTrait tr) ? tr : null;
        }

        /// <summary>Is this one of the mod's traits (a tier or a technique)?</summary>
        public static bool IsSakuga(ActorTrait t)
        {
            return t != null && _sakugaTraits.Contains(t);
        }

        /// <summary>Does this unit have any Sakuga trait at all?</summary>
        public static bool AnyTrait(Actor a)
        {
            if (a == null || a.traits == null)
            {
                return false;
            }
            foreach (ActorTrait t in a.traits)
            {
                if (_sakugaTraits.Contains(t))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The level (0 = doesn't know it) a unit uses a technique at: from its tier, or its bare trait.</summary>
        public static int Level(Actor a, Technique t)
        {
            return Level(a, t, Tiers.Of(a));
        }

        public static int Level(Actor a, Technique t, Tier tier)
        {
            if (t.PrimordialOnly && !Primordials.Is(a))
            {
                return 0;
            }
            int level = 0;
            if (t.School != School.None && tier != Tier.None && Settings.Schools && !Schools.Knows(a, t.School))
            {
                // another school's technique: only a bare trait teaches it
            }
            else if (t.School == School.None && tier != Tier.None && Settings.Schools && !Schools.Fits(a, t))
            {
                // a technique in another school's style: only a bare trait teaches it
            }
            else if (tier == Tier.God)
            {
                level = t.MaxLevel;
            }
            else if (tier != Tier.None && tier >= t.Entry)
            {
                level = Mathf.Min(t.MaxLevel, tier - t.Entry + 1);
            }
            ActorTrait tr = Trait(t);
            if (tr != null && a.traits.Contains(tr))
            {
                level = Mathf.Max(level, Mathf.Min(t.MaxLevel, Settings.BareLevel));
            }
            return level;
        }

        /// <summary>Reactive techniques answer a technique's first contact (Combat.Reach).</summary>
        [SakugaInit]
        private static void InitReactions()
        {
            Combat.ContactHandlers.Add(React);
        }

        /// <summary>
        /// 'v' is about to be reached by move 'm': its reactive techniques may answer. Reactions.Dispatch
        /// reads the blow once (tier, persona, memory, how deep in a chain of counters it is), then
        /// tries the answers it knows in an order weighed by how well each one's Answers meet the
        /// incoming Forms, with chance: never the same fixed first-in-list answer.
        /// </summary>
        private static Contact React(Move m, Actor v, bool area)
        {
            return Reactions.Dispatch(m, v, area);
        }

        private static readonly List<Technique> _reactive = new List<Technique>();

        /// <summary>The reactive techniques (Reactions.Dispatch walks these).</summary>
        public static IReadOnlyList<Technique> Reactives
        {
            get
            {
                if (_reactive.Count == 0)
                {
                    foreach (Technique t in _all)
                    {
                        if (t.Reactive)
                        {
                            _reactive.Add(t);
                        }
                    }
                }
                return _reactive;
            }
        }

        /// <summary>Roman numerals for levels.</summary>
        public static string Roman(int level)
        {
            return level <= 1 ? "I" : level == 2 ? "II" : "III";
        }
    }

    /// <summary>Mana: tiers pay for techniques and regenerate fast; ordinary units with bare traits pay nothing.</summary>
    internal static class Mana
    {
        public static float Cost(Actor a, Technique t, int level, bool linked)
        {
            if (Tiers.Of(a) == Tier.None || Fighters.Awakened(a))
            {
                return 0f;
            }
            float c = t.Mana(level) * Settings.ManaCost * Mathf.Max(0.6f, 1f - 0.06f * Schools.Mastery(a, t));
            if (linked)
            {
                Tier tier = Tiers.Of(a);
                c *= tier >= Tier.God ? 0.65f : tier >= Tier.Grandmaster ? 0.7f : 0.75f;
            }
            return c;
        }

        public static bool Has(Actor a, Technique t, int level, bool linked)
        {
            float c = Cost(a, t, level, linked);
            return c <= 0f || a.getMana() >= c;
        }

        public static void Spend(Actor a, Technique t, int level, bool linked)
        {
            int c = Mathf.CeilToInt(Cost(a, t, level, linked));
            if (c > 0)
            {
                a.spendMana(c);
            }
        }

        public static void Regen(Fighter f, float dt)
        {
            TierInfo info = Tiers.Info(f.Tier);
            if (info == null || f.A.hasMaxMana())
            {
                f.ManaAcc = 0f;
                return;
            }
            f.ManaAcc += info.ManaRegen * dt * (f.A.isFighting() ? 1f : 1.5f);
            if (f.ManaAcc >= 1f)
            {
                int n = (int)f.ManaAcc;
                f.ManaAcc -= n;
                f.A.restoreMana(n);
            }
        }
    }
}
