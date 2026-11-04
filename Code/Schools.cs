using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// COMBAT SCHOOLS. Every tiered fighter walks a way of fighting, and that school's techniques
    /// are its own (the mod's first twenty techniques stay everyone's):
    ///   Iron Fist        boxing and brawling: jabs, hooks, liver shots, counters, guard
    ///   Tempest Leg      kicks and the air: sweeps, axe kicks, flying knees, aerial juggles
    ///   Thousand Blades  the sword (a blade of ki when it carries none): draws, cuts, sword waves
    ///   Ki Arts          energy: bullets, orbs, beams, barriers, reflections
    ///   Earth Breaker    grapples and the ground: slams, suplexes, pillars, quakes
    ///   Phantom Step     speed and illusion: feints, blinks, afterimages, clones
    ///   Hunter's Way     the craft of fighting what is not a person: beasts, undead, fiends,
    ///                    flyers, giants, dragons, swarms, mages, towers
    /// A fighter gets its first school when it first becomes a Novice (weighted by its people and its
    /// nature: an orc leans to Earth Breaker and Iron Fist, an elf to Thousand Blades and Phantom
    /// Step, a strong one to the heavy schools, a quick one to the fast), a second one when it becomes
    /// a Master, and a third as a God, so no two Gods carry the same arsenal. Schools are traits (the
    /// player can give or take them). Fighters from before schools existed get theirs the first time
    /// the mod looks at them. The "Combat schools" setting off: every fighter knows every school.
    /// </summary>
    internal static class Schools
    {
        public const string Group = "sakuga_schools";
        public const int Count = 7;
        /// <summary>Actor data: how many schools the mod has given the unit so far.</summary>
        private const string KeyGiven = "sakuga_schools_given";

        private static readonly ActorTrait[] _traits = new ActorTrait[Count + 1];
        private static readonly HashSet<long> _checked = new HashSet<long>();
        private static readonly List<Actor> _pending = new List<Actor>();
        private static readonly List<long> _pendingId = new List<long>();

        /// <summary>The one school a fighter walks (the first, when a player gave it several; None: none).</summary>
        public static School PrimaryOf(Actor a)
        {
            if (a == null)
            {
                return School.None;
            }
            for (int i = 1; i <= Count; i++)
            {
                if (Knows(a, (School)i))
                {
                    return (School)i;
                }
            }
            return School.None;
        }

        public static string Id(School s)
        {
            switch (s)
            {
                case School.IronFist: return "iron_fist";
                case School.TempestLeg: return "tempest_leg";
                case School.ThousandBlades: return "thousand_blades";
                case School.KiArts: return "ki_arts";
                case School.EarthBreaker: return "earth_breaker";
                case School.PhantomStep: return "phantom_step";
                case School.HuntersWay: return "hunters_way";
                default: return "none";
            }
        }

        public static string TraitId(School s)
        {
            return "sakuga_school_" + Id(s);
        }

        /// <summary>The school's colour (its trait group, its icon frame).</summary>
        public static Color Color(School s)
        {
            switch (s)
            {
                case School.IronFist: return new Color(0.86f, 0.3f, 0.22f, 1f);
                case School.TempestLeg: return new Color(0.42f, 0.84f, 0.92f, 1f);
                case School.ThousandBlades: return new Color(0.82f, 0.86f, 0.95f, 1f);
                case School.KiArts: return new Color(0.98f, 0.82f, 0.32f, 1f);
                case School.EarthBreaker: return new Color(0.68f, 0.5f, 0.3f, 1f);
                case School.PhantomStep: return new Color(0.62f, 0.45f, 0.92f, 1f);
                case School.HuntersWay: return new Color(0.45f, 0.72f, 0.34f, 1f);
                default: return UnityEngine.Color.white;
            }
        }

        public static ActorTrait Trait(School s)
        {
            int i = (int)s;
            return i > 0 && i <= Count ? _traits[i] : null;
        }

        /// <summary>Does this unit walk this school?</summary>
        public static bool Knows(Actor a, School s)
        {
            if (s == School.None)
            {
                return true;
            }
            ActorTrait t = Trait(s);
            return t != null && a != null && a.traits != null && a.traits.Contains(t);
        }

        /// <summary>The schools this unit walks (a new list).</summary>
        public static List<School> Of(Actor a)
        {
            List<School> list = new List<School>(3);
            for (int i = 1; i <= Count; i++)
            {
                if (Knows(a, (School)i))
                {
                    list.Add((School)i);
                }
            }
            return list;
        }

        /// <summary>
        /// How many schools a tier walks: one, from Novice to God. A fighter's growth is its mastery of
        /// that one school (Mastery), not more schools; the player may still give a unit any others.
        /// </summary>
        public static int Want(Tier t)
        {
            return t == Tier.None ? 0 : 1;
        }

        /// <summary>
        /// A fighter's mastery of its school's technique 't': how far its tier stands above the technique's
        /// entry (0: just learned it; 5: a God throwing a Novice's move). 0 for techniques of no school or
        /// of a school it does not walk.
        /// </summary>
        public static int Mastery(Actor a, Technique t)
        {
            if (a == null || t == null || t.School == School.None || !Knows(a, t.School))
            {
                return 0;
            }
            return Mathf.Max(0, (int)Tiers.Of(a) - (int)t.Entry);
        }

        // ------------------------------------------------------------- traits --

        /// <summary>Called by Traits.Register (the group and one trait per school).</summary>
        public static void Register()
        {
            if (AssetManager.trait_groups.get(Group) == null)
            {
                AssetManager.trait_groups.add(new ActorTraitGroupAsset { id = Group, name = "trait_group_" + Group, color = "#E8B24A" });
            }
            for (int i = 1; i <= Count; i++)
            {
                School s = (School)i;
                ActorTrait t = AssetManager.traits.get(TraitId(s));
                if (t == null)
                {
                    t = AssetManager.traits.add(new ActorTrait
                    {
                        id = TraitId(s),
                        group_id = Group,
                        path_icon = "ui/Icons/sakuga/iconSchool" + Technique.Pascal(Id(s)),
                        type = TraitType.Positive,
                        rarity = Rarity.R2_Epic,
                        can_be_given = true,
                        can_be_removed = true,
                        needs_to_be_explored = false,
                        rate_birth = 0,
                        rate_inherit = 0,
                        spawn_random_trait_allowed = false,
                        is_mutation_box_allowed = false,
                        affects_mind = false,
                        priority = 150 + i,
                        base_stats = new BaseStats()
                    });
                    t.action_on_augmentation_add = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_add, new WorldActionTrait(OnChanged));
                    t.action_on_augmentation_remove = (WorldActionTrait)Delegate.Combine(t.action_on_augmentation_remove, new WorldActionTrait(OnChanged));
                }
                _traits[i] = t;
            }
        }

        private static bool OnChanged(NanoObject target, BaseAugmentationAsset trait)
        {
            try
            {
                if (target is Actor a && World.world != null)
                {
                    Fighters.Touch(a);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("school", e);
            }
            return true;
        }

        // --------------------------------------------------------- assignment --

        [SakugaInit]
        private static void Init()
        {
            Hooks.TierChanged += (a, old, now, natural) => Queue(a);
            Hooks.FighterTick += (f, dt) =>
            {
                if (!_checked.Contains(f.Id))
                {
                    Queue(f.A);
                }
                else if (!_armAt.TryGetValue(f.Id, out float next) || Time.unscaledTime >= next)
                {
                    if (_armAt.Count > 4096)
                    {
                        _armAt.Clear();
                    }
                    _armAt[f.Id] = Time.unscaledTime + 5f + UnityEngine.Random.value * 3f;
                    try
                    {
                        Arm(f.A, false);
                    }
                    catch (Exception e)
                    {
                        SakugaMod.Report("school sword", e);
                    }
                }
            };
            Hooks.FrameTick += dt => Assign();
            // dev: "school" lists the performer's schools; "school ki_arts" gives it one
            Dev.Commands["school"] = args =>
            {
                Actor p = Dev.Performer;
                if (p == null)
                {
                    Dev.Log("school: no performer");
                    return;
                }
                string want = Dev.Arg(args, 1, "");
                for (int i = 1; i <= Count && want != ""; i++)
                {
                    if (Id((School)i) == want)
                    {
                        p.addTrait(Trait((School)i), true);
                    }
                }
                List<string> names = new List<string>();
                foreach (School s in Of(p))
                {
                    names.Add(Id(s));
                }
                Dev.Log("school: " + p.asset.id + "#" + p.Sid() + " (" + Tiers.Of(p) + ") walks " + string.Join(", ", names));
            };
            Hooks.WorldCleared += () =>
            {
                _checked.Clear();
                _pending.Clear();
                _pendingId.Clear();
            };
        }

        /// <summary>Looked at on the next frame (never from inside a trait callback: the game may be enumerating traits).</summary>
        private static void Queue(Actor a)
        {
            if (a == null)
            {
                return;
            }
            long id = a.Sid();
            _checked.Add(id);
            if (!_pendingId.Contains(id))
            {
                _pending.Add(a);
                _pendingId.Add(id);
            }
        }

        private static void Assign()
        {
            if (_pending.Count == 0)
            {
                return;
            }
            for (int i = 0; i < _pending.Count; i++)
            {
                Actor a = _pending[i];
                try
                {
                    if (a != null && a.Sid() == _pendingId[i] && a.isAlive())
                    {
                        Fill(a);
                        Arm(a, true);
                    }
                }
                catch (Exception e)
                {
                    SakugaMod.Report("school", e);
                }
            }
            _pending.Clear();
            _pendingId.Clear();
            if (_checked.Count > 8192)
            {
                _checked.Clear();
            }
        }

        /// <summary>
        /// The old rule gave Masters two schools and Gods three: such a unit keeps one (the one most of its
        /// own kind would walk, so a forge-dwarf stays an earth breaker), and the mod's count is set to it.
        /// </summary>
        private static void Trim(Actor a, int keep)
        {
            List<School> mine = Of(a);
            School best = mine[0];
            float bw = -1f;
            float[] w = Weights(a, true);
            foreach (School s in mine)
            {
                if (w[(int)s] > bw)
                {
                    bw = w[(int)s];
                    best = s;
                }
            }
            a.data.set(KeyGiven, keep);
            foreach (School s in mine)
            {
                if (s != best)
                {
                    a.removeTrait(Trait(s));
                }
            }
            if (Dev.On) Dev.Trace("school: " + a.asset.id + "#" + a.Sid() + " keeps only the " + Id(best));
        }

        // the techniques of no school, by the schools whose style they are (the rest fit everyone)
        private static readonly Dictionary<string, School[]> _affinity = new Dictionary<string, School[]>
        {
            // Novice
            ["cyclone_heel"] = new[] { School.TempestLeg },
            ["flash_step"] = new[] { School.PhantomStep, School.ThousandBlades },
            ["hundred_fists"] = new[] { School.IronFist },
            ["interrupt_jab"] = new[] { School.IronFist, School.PhantomStep },
            ["sky_relay"] = new[] { School.TempestLeg, School.IronFist, School.PhantomStep },
            // Adept
            ["cross_fang"] = new[] { School.ThousandBlades, School.PhantomStep, School.HuntersWay },
            ["duck_sweep"] = new[] { School.TempestLeg, School.PhantomStep },
            ["earth_and_sky"] = new[] { School.TempestLeg, School.EarthBreaker, School.IronFist },
            ["heaven_splitter"] = new[] { School.IronFist },
            ["hop_strike"] = new[] { School.TempestLeg },
            ["hundred_volley"] = new[] { School.HuntersWay, School.KiArts },
            ["iron_wedge"] = new[] { School.IronFist, School.EarthBreaker, School.HuntersWay },
            ["meteor_slam"] = new[] { School.IronFist, School.EarthBreaker },
            ["rising_tempest"] = new[] { School.TempestLeg, School.IronFist },
            ["slip_counter"] = new[] { School.IronFist, School.PhantomStep },
            ["twin_meteor"] = new[] { School.EarthBreaker, School.IronFist, School.TempestLeg },
            // Expert
            ["air_intercept"] = new[] { School.TempestLeg, School.HuntersWay },
            ["crescent_fang"] = new[] { School.ThousandBlades },
            ["grab_break"] = new[] { School.EarthBreaker, School.KiArts, School.IronFist },
            ["hurricane_swing"] = new[] { School.EarthBreaker, School.HuntersWay },
            ["mirage_step"] = new[] { School.PhantomStep },
            ["pincer_beams"] = new[] { School.KiArts },
            ["shadow_formation"] = new[] { School.PhantomStep, School.ThousandBlades },
            ["still_water"] = new[] { School.ThousandBlades, School.KiArts },
            ["unison_burst"] = new[] { School.KiArts, School.IronFist },
            // Master
            ["beam_cleave"] = new[] { School.ThousandBlades, School.KiArts },
            ["fault_line"] = new[] { School.EarthBreaker, School.IronFist },
            ["hammer_anvil"] = new[] { School.EarthBreaker, School.IronFist },
            ["heaven_seal"] = new[] { School.KiArts },
            ["orb_relay"] = new[] { School.KiArts },
            ["severing_flash"] = new[] { School.ThousandBlades },
            ["star_choir"] = new[] { School.KiArts },
            ["wingbreaker"] = new[] { School.EarthBreaker, School.HuntersWay, School.TempestLeg },
            // Grandmaster
            ["phantom_rush"] = new[] { School.ThousandBlades, School.PhantomStep },
            ["reversal"] = new[] { School.IronFist, School.PhantomStep, School.ThousandBlades, School.TempestLeg },
            ["spiral_assault"] = new[] { School.TempestLeg, School.PhantomStep, School.ThousandBlades, School.HuntersWay },
            ["vanguard_charge"] = new[] { School.IronFist, School.EarthBreaker, School.HuntersWay },
            // God
            ["avatar_ascendant"] = new[] { School.KiArts },
            ["eclipse"] = new[] { School.PhantomStep },
            ["genesis_fist"] = new[] { School.IronFist, School.EarthBreaker },
            ["heavens_gate"] = new[] { School.ThousandBlades, School.HuntersWay },
            ["heavens_hand"] = new[] { School.IronFist, School.KiArts },
            ["judgment_rain"] = new[] { School.KiArts, School.HuntersWay },
            ["primordial_roar"] = new[] { School.IronFist, School.EarthBreaker },
            ["world_tree_pillar"] = new[] { School.KiArts, School.EarthBreaker },
            ["starfall"] = new[] { School.KiArts, School.IronFist, School.EarthBreaker },
            ["zero_moment"] = new[] { School.ThousandBlades, School.PhantomStep },
        };

        /// <summary>The schools a technique of no school belongs to (null: every school).</summary>
        public static School[] AffinityOf(string id)
        {
            return id != null && _affinity.TryGetValue(id, out School[] s) ? s : null;
        }

        /// <summary>Does a technique of no school suit this fighter's school (or has it none yet)?</summary>
        public static bool Fits(Actor a, Technique t)
        {
            School[] aff = t.Affinity;
            if (aff == null)
            {
                return true;
            }
            bool any = false;
            for (int i = (int)School.IronFist; i <= (int)School.HuntersWay; i++)
            {
                if (Knows(a, (School)i))
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return true;
            }
            foreach (School s in aff)
            {
                if (Knows(a, s))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Gives a tiered unit the schools its tier calls for (it keeps the ones it has).</summary>
        public static void Fill(Actor a)
        {
            if (Primordials.Any && Primordials.Is(a))
            {
                // a Primordial walks no school: it is older than all of them (its powers are its own)
                for (int i = 1; i <= Count; i++)
                {
                    ActorTrait st = Trait((School)i);
                    if (st != null && a.traits.Contains(st))
                    {
                        a.removeTrait(st);
                    }
                }
                return;
            }
            Tier t = Tiers.Of(a);
            int want = Want(t);
            int have = Of(a).Count;
            // how many the mod already gave it (saved with the unit): a school the player took away
            // stays away; a new one comes only when its tier calls for more than it was ever given
            a.data.get(KeyGiven, out int given, 0);
            if (have > want && want > 0 && given > want && have <= given)
            {
                Trim(a, want);
                return;
            }
            if (Mathf.Max(have, given) >= want)
            {
                if (given < have)
                {
                    a.data.set(KeyGiven, Mathf.Min(have, want));
                }
                return;
            }
            have = Mathf.Max(have, given);
            a.data.set(KeyGiven, want);
            int guard = 0;
            while (have < want && guard++ < Count)
            {
                School s = Pick(a);
                if (s == School.None)
                {
                    break;
                }
                a.addTrait(Trait(s), true);
                have++;
                if (Dev.On) Dev.Trace("school: " + a.asset.id + "#" + a.Sid() + " (" + t + ") walks the " + Id(s));
            }
        }

        // ------------------------------------------------------------- swords --

        /// <summary>The sword a walker of Thousand Blades is handed, by tier (None .. God).</summary>
        private static readonly string[] SwordOf = { "sword_iron", "sword_bronze", "sword_iron", "sword_steel", "sword_steel", "sword_mythril", "sword_adamantine" };
        /// <summary>The bow a walker of Hunter's Way is handed, by tier (None .. God): an archer's school.</summary>
        private static readonly string[] BowOf = { "bow_wood", "bow_bronze", "bow_iron", "bow_steel", "bow_steel", "bow_mythril", "bow_adamantine" };
        private const string KeySword = "sakuga_sword_at";
        private static readonly Dictionary<long, float> _armAt = new Dictionary<long, float>();

        /// <summary>
        /// Every school fights with what it is: THOUSAND BLADES with swords, HUNTER'S WAY with bows, and the
        /// schools of the body (fist, leg, ki, earth, phantom) bare-handed. A walker of a sword or bow
        /// school that holds anything else (a spear, a hammer, a staff) or nothing takes up one of its tier's
        /// metal (bronze for a Novice up to adamantine for a God); a walker of a bare-handed school puts
        /// its weapon down. A weapon put down goes back to its city's storage; the right one already held
        /// is kept, whatever its metal. Units that cannot use items (beasts) fight as they are. 'now':
        /// right away (a new walker); otherwise at most once a year per unit (the game's cities and smiths
        /// may hand it another weapon in between). (A unit given several schools by a player takes the
        /// sword if it walks the blades, else the bow if it hunts, else its hands.)
        /// </summary>
        public static void Arm(Actor a, bool now)
        {
            if (a == null || !a.isAlive() || a.asset == null || !a.understandsHowToUseItems() || !Settings.Schools)
            {
                return;
            }
            bool blades = Knows(a, School.ThousandBlades), hunt = !blades && Knows(a, School.HuntersWay);
            bool any = false;
            for (int i = 1; i <= Count; i++)
            {
                any |= Knows(a, (School)i);
            }
            bool primordial = Primordials.Any && Primordials.Is(a);
            if (!any && !primordial)
            {
                return;
            }
            string want = blades ? "sword" : hunt ? "bow" : null;
            ActorEquipmentSlot slot = a.equipment != null ? a.equipment.getSlot(EquipmentType.Weapon) : null;
            if (slot == null)
            {
                return;
            }
            Item held = slot.isEmpty() ? null : slot.getItem();
            if (want == null ? held == null : held != null && held.getAsset() != null && held.getAsset().equipment_subtype == want)
            {
                return;
            }
            float worldNow = (float)World.world.getCurWorldTime();
            if (!now)
            {
                a.data.get(KeySword, out float at, -999f);
                if (worldNow - at < 60f && worldNow >= at)
                {
                    return;
                }
            }
            a.data.set(KeySword, worldNow);
            string[] table = blades ? SwordOf : BowOf;
            EquipmentAsset sword = want == null ? null : AssetManager.items.get(table[Mathf.Clamp((int)Tiers.Of(a), 0, table.Length - 1)]);
            if (want != null && sword == null)
            {
                return;
            }
            if (held != null)
            {
                slot.takeAwayItem();
                if (a.city != null && a.city.isAlive())
                {
                    a.city.tryToPutItem(held);
                }
                else
                {
                    World.world.items.removeObject(held);
                }
            }
            if (sword == null)
            {
                a.setStatsDirty();
                if (Dev.On) Dev.Trace("school: " + a.asset.id + "#" + a.Sid() + " (" + Tiers.Of(a) + ") puts down its " + held.getAsset().id + " and fights bare-handed");
                return;
            }
            Item item = World.world.items.generateItem(sword, a.kingdom, a.getName(), 1, a);
            a.equipment.setItem(item, a);
            a.setStatsDirty();
            if (Dev.On) Dev.Trace("school: " + a.asset.id + "#" + a.Sid() + " (" + Tiers.Of(a) + ") takes up a " + sword.id + (held != null ? " instead of its " + held.getAsset().id : ""));
        }

        /// <summary>A school it doesn't walk yet, weighted by its people and its nature.</summary>
        /// <summary>How fitting each school is for 'a' (its kind, its nature, how rare among the great); 'known': count the ones it walks too.</summary>
        internal static float[] Weights(Actor a, bool known)
        {
            float[] w = new float[Count + 1];
            for (int i = 1; i <= Count; i++)
            {
                w[i] = known || !Knows(a, (School)i) ? 1f : 0f;
            }
            string id = a.asset != null ? a.asset.id : "";
            if (id.StartsWith("civ_", StringComparison.Ordinal))
            {
                id = id.Substring(4);
            }
            bool civ = a.isKingdomCiv();
            switch (id)
            {
                case "orc":
                    Lean(w, School.EarthBreaker, 2.5f); Lean(w, School.IronFist, 2.2f); Lean(w, School.HuntersWay, 1.4f);
                    break;
                case "elf":
                    Lean(w, School.ThousandBlades, 2.4f); Lean(w, School.PhantomStep, 2.4f); Lean(w, School.KiArts, 1.6f);
                    break;
                case "dwarf":
                    Lean(w, School.EarthBreaker, 3f); Lean(w, School.IronFist, 1.6f); Lean(w, School.HuntersWay, 1.5f);
                    break;
                case "human":
                    Lean(w, School.ThousandBlades, 1.8f); Lean(w, School.IronFist, 1.6f); Lean(w, School.TempestLeg, 1.3f);
                    break;
                case "alien":
                    Lean(w, School.KiArts, 3f); Lean(w, School.PhantomStep, 1.8f);
                    break;
                default:
                    if (Peoples.LeanSchools(a.asset, w))
                    {
                        break;   // an animal people walks the schools that suit its kind
                    }
                    if (!civ)
                    {
                        // a beast that awakens fights with what it has
                        Lean(w, School.HuntersWay, 2f); Lean(w, School.TempestLeg, 1.5f); Lean(w, School.EarthBreaker, 1.5f);
                        w[(int)School.ThousandBlades] *= 0.3f;
                    }
                    break;
            }
            // its nature (the game's traits)
            if (Has(a, "strong") || Has(a, "giant") || Has(a, "fat")) { Lean(w, School.EarthBreaker, 1.6f); Lean(w, School.IronFist, 1.3f); }
            if (Has(a, "fast") || Has(a, "agile") || Has(a, "light")) { Lean(w, School.TempestLeg, 1.6f); Lean(w, School.PhantomStep, 1.5f); }
            if (Has(a, "genius") || Has(a, "wise") || Has(a, "clever")) { Lean(w, School.KiArts, 1.6f); Lean(w, School.PhantomStep, 1.2f); }
            if (Has(a, "bloodlust") || Has(a, "aggressive")) { Lean(w, School.IronFist, 1.4f); Lean(w, School.ThousandBlades, 1.3f); }
            if (Has(a, "eagle_eyed") || Has(a, "hunter") || Has(a, "veteran")) { Lean(w, School.HuntersWay, 1.5f); }
            if (a.hasRangeAttack()) { Lean(w, School.KiArts, 1.5f); }
            if (a.hasWeapon()) { Lean(w, School.ThousandBlades, 1.4f); }
            // the strongest in the world fight unlike each other: a school other Masters and up already
            // walk is a less likely pick (a God's three schools rarely match another God's)
            Tier mine = Tiers.Of(a);
            if (mine >= Tier.Master)
            {
                IReadOnlyList<Fighter> all = Fighters.All;
                for (int f = 0; f < all.Count; f++)
                {
                    Actor o = all[f].A;
                    if (o == null || o == a || !o.isAlive() || all[f].Tier < Tier.Master)
                    {
                        continue;
                    }
                    float k = all[f].Tier >= Tier.God ? 0.3f : all[f].Tier >= Tier.Grandmaster ? 0.55f : 0.8f;
                    for (int i = 1; i <= Count; i++)
                    {
                        if (Knows(o, (School)i))
                        {
                            w[i] *= k;
                        }
                    }
                }
            }
            return w;
        }

        private static School Pick(Actor a)
        {
            float[] w = Weights(a, false);
            float sum = 0f;
            for (int i = 1; i <= Count; i++)
            {
                sum += w[i];
            }
            if (sum <= 0f)
            {
                return School.None;
            }
            float r = UnityEngine.Random.value * sum;
            for (int i = 1; i <= Count; i++)
            {
                r -= w[i];
                if (r <= 0f && w[i] > 0f)
                {
                    return (School)i;
                }
            }
            for (int i = Count; i >= 1; i--)
            {
                if (w[i] > 0f)
                {
                    return (School)i;
                }
            }
            return School.None;
        }

        private static void Lean(float[] w, School s, float k)
        {
            w[(int)s] *= k;
        }

        private static bool Has(Actor a, string trait)
        {
            try
            {
                return a.hasTrait(trait);
            }
            catch
            {
                return false;
            }
        }
    }
}
