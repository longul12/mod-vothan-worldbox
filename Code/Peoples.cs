using System;
using System.Collections.Generic;

namespace Sakuga
{
    /// <summary>
    /// The civilized peoples: every species that builds kingdoms (humans, elves, orcs, dwarves and the
    /// game's animal civilizations: cat folk, bear folk, crab folk, crystal golem folk, unicorn folk...;
    /// the game marks them all with ActorAsset.civ through its civilization templates). A people is
    /// people whatever its kind or its base health: it counts as Folk, is never taken for a beast, a
    /// fiend or a construct, and walks the schools that suit its kind.
    /// </summary>
    internal static class Peoples
    {
        /// <summary>Does this species build kingdoms (people, not a beast)?</summary>
        public static bool Is(ActorAsset asset)
        {
            return asset != null && asset.civ;
        }

        /// <summary>The species id without the game's "civ_" prefix (civ_cat: "cat").</summary>
        public static string Kind(ActorAsset asset)
        {
            string id = asset != null ? asset.id ?? "" : "";
            return id.StartsWith("civ_", StringComparison.Ordinal) ? id.Substring(4) : id;
        }

        private struct Lean
        {
            public readonly int I;
            public readonly float K;

            public Lean(int i, float k)
            {
                I = i;
                K = k;
            }
        }

        private static Lean S(School s, float k) => new Lean((int)s, k);
        private static Lean P(Style s, float k) => new Lean((int)s, k);

        // the schools that suit each animal people (humans, elves, orcs and dwarves lean in Schools.Weights)
        private static readonly Dictionary<string, Lean[]> SchoolLeans = new Dictionary<string, Lean[]>
        {
            ["cat"] = new[] { S(School.PhantomStep, 2.2f), S(School.TempestLeg, 1.8f), S(School.ThousandBlades, 1.3f) },
            ["dog"] = new[] { S(School.HuntersWay, 2f), S(School.IronFist, 1.5f), S(School.TempestLeg, 1.3f) },
            ["chicken"] = new[] { S(School.TempestLeg, 2.2f), S(School.KiArts, 1.3f) },
            ["rabbit"] = new[] { S(School.TempestLeg, 2.6f), S(School.PhantomStep, 1.5f) },
            ["monkey"] = new[] { S(School.TempestLeg, 1.8f), S(School.IronFist, 1.6f), S(School.KiArts, 1.4f) },
            ["fox"] = new[] { S(School.PhantomStep, 2.4f), S(School.KiArts, 1.6f), S(School.HuntersWay, 1.3f) },
            ["sheep"] = new[] { S(School.EarthBreaker, 1.6f), S(School.IronFist, 1.3f), S(School.KiArts, 1.2f) },
            ["cow"] = new[] { S(School.EarthBreaker, 2.2f), S(School.IronFist, 1.6f) },
            ["armadillo"] = new[] { S(School.EarthBreaker, 2.4f), S(School.IronFist, 1.5f) },
            ["wolf"] = new[] { S(School.HuntersWay, 2.4f), S(School.TempestLeg, 1.4f), S(School.PhantomStep, 1.3f) },
            ["bear"] = new[] { S(School.IronFist, 2.6f), S(School.EarthBreaker, 2f) },
            ["rhino"] = new[] { S(School.EarthBreaker, 2.6f), S(School.IronFist, 1.8f) },
            ["buffalo"] = new[] { S(School.EarthBreaker, 2.4f), S(School.IronFist, 1.6f) },
            ["hyena"] = new[] { S(School.HuntersWay, 2f), S(School.PhantomStep, 1.5f), S(School.IronFist, 1.3f) },
            ["rat"] = new[] { S(School.PhantomStep, 2.2f), S(School.HuntersWay, 1.5f), S(School.ThousandBlades, 1.3f) },
            ["alpaca"] = new[] { S(School.TempestLeg, 1.8f), S(School.KiArts, 1.4f) },
            ["capybara"] = new[] { S(School.KiArts, 2f), S(School.EarthBreaker, 1.4f) },
            ["goat"] = new[] { S(School.TempestLeg, 1.8f), S(School.EarthBreaker, 1.6f) },
            ["scorpion"] = new[] { S(School.ThousandBlades, 1.8f), S(School.HuntersWay, 1.6f), S(School.PhantomStep, 1.3f) },
            ["crab"] = new[] { S(School.IronFist, 2.4f), S(School.EarthBreaker, 1.6f) },
            ["penguin"] = new[] { S(School.TempestLeg, 1.6f), S(School.KiArts, 1.4f), S(School.IronFist, 1.3f) },
            ["turtle"] = new[] { S(School.EarthBreaker, 2f), S(School.KiArts, 1.8f), S(School.IronFist, 1.3f) },
            ["crocodile"] = new[] { S(School.EarthBreaker, 2f), S(School.IronFist, 1.8f), S(School.HuntersWay, 1.3f) },
            ["snake"] = new[] { S(School.PhantomStep, 2f), S(School.ThousandBlades, 1.6f), S(School.HuntersWay, 1.3f) },
            ["frog"] = new[] { S(School.TempestLeg, 2.4f), S(School.KiArts, 1.4f) },
            ["piranha"] = new[] { S(School.IronFist, 2f), S(School.HuntersWay, 1.6f) },
            ["liliar"] = new[] { S(School.KiArts, 2f), S(School.PhantomStep, 1.4f) },
            ["garlic_man"] = new[] { S(School.KiArts, 1.6f), S(School.EarthBreaker, 1.5f) },
            ["lemon_man"] = new[] { S(School.KiArts, 1.8f), S(School.TempestLeg, 1.4f) },
            ["acid_gentleman"] = new[] { S(School.KiArts, 2.2f), S(School.ThousandBlades, 1.4f) },
            ["crystal_golem"] = new[] { S(School.EarthBreaker, 2.6f), S(School.KiArts, 1.6f), S(School.IronFist, 1.4f) },
            ["candy_man"] = new[] { S(School.TempestLeg, 1.6f), S(School.KiArts, 1.5f) },
            ["beetle"] = new[] { S(School.IronFist, 1.8f), S(School.EarthBreaker, 1.8f) },
            ["seal"] = new[] { S(School.IronFist, 1.5f), S(School.TempestLeg, 1.4f), S(School.KiArts, 1.3f) },
            ["unicorn"] = new[] { S(School.KiArts, 2.6f), S(School.PhantomStep, 1.6f) },
        };

        // the fighting temper of each animal people (Personas)
        private static readonly Dictionary<string, Lean[]> StyleLeans = new Dictionary<string, Lean[]>
        {
            ["cat"] = new[] { P(Style.Trickster, 1.6f), P(Style.CounterPuncher, 1.5f) },
            ["dog"] = new[] { P(Style.Hunter, 1.6f), P(Style.Guardian, 1.4f) },
            ["chicken"] = new[] { P(Style.Swarmer, 1.6f), P(Style.Berserker, 1.3f) },
            ["rabbit"] = new[] { P(Style.Swarmer, 1.7f), P(Style.OutFighter, 1.3f) },
            ["monkey"] = new[] { P(Style.Trickster, 1.6f), P(Style.Swarmer, 1.4f) },
            ["fox"] = new[] { P(Style.Trickster, 1.8f), P(Style.Tactician, 1.3f) },
            ["sheep"] = new[] { P(Style.Guardian, 1.5f), P(Style.CounterPuncher, 1.2f) },
            ["cow"] = new[] { P(Style.Brawler, 1.5f), P(Style.Guardian, 1.4f) },
            ["armadillo"] = new[] { P(Style.Guardian, 1.8f), P(Style.CounterPuncher, 1.3f) },
            ["wolf"] = new[] { P(Style.Hunter, 1.7f), P(Style.Swarmer, 1.4f) },
            ["bear"] = new[] { P(Style.Brawler, 1.8f), P(Style.Berserker, 1.5f) },
            ["rhino"] = new[] { P(Style.Berserker, 1.7f), P(Style.Brawler, 1.5f) },
            ["buffalo"] = new[] { P(Style.Brawler, 1.6f), P(Style.Berserker, 1.4f) },
            ["hyena"] = new[] { P(Style.Swarmer, 1.6f), P(Style.Hunter, 1.4f) },
            ["rat"] = new[] { P(Style.Trickster, 1.6f), P(Style.Swarmer, 1.5f) },
            ["alpaca"] = new[] { P(Style.OutFighter, 1.5f), P(Style.CounterPuncher, 1.2f) },
            ["capybara"] = new[] { P(Style.Tactician, 1.6f), P(Style.CounterPuncher, 1.4f) },
            ["goat"] = new[] { P(Style.Berserker, 1.4f), P(Style.Swarmer, 1.3f) },
            ["scorpion"] = new[] { P(Style.Hunter, 1.5f), P(Style.CounterPuncher, 1.5f) },
            ["crab"] = new[] { P(Style.Brawler, 1.6f), P(Style.Guardian, 1.5f) },
            ["penguin"] = new[] { P(Style.Swarmer, 1.4f), P(Style.Guardian, 1.3f) },
            ["turtle"] = new[] { P(Style.Guardian, 1.8f), P(Style.Tactician, 1.4f) },
            ["crocodile"] = new[] { P(Style.Berserker, 1.5f), P(Style.CounterPuncher, 1.4f) },
            ["snake"] = new[] { P(Style.Trickster, 1.6f), P(Style.CounterPuncher, 1.5f) },
            ["frog"] = new[] { P(Style.OutFighter, 1.5f), P(Style.Swarmer, 1.3f) },
            ["piranha"] = new[] { P(Style.Berserker, 1.7f), P(Style.Swarmer, 1.5f) },
            ["liliar"] = new[] { P(Style.OutFighter, 1.6f), P(Style.Tactician, 1.3f) },
            ["garlic_man"] = new[] { P(Style.Guardian, 1.4f), P(Style.Brawler, 1.3f) },
            ["lemon_man"] = new[] { P(Style.OutFighter, 1.4f), P(Style.Swarmer, 1.3f) },
            ["acid_gentleman"] = new[] { P(Style.Tactician, 1.7f), P(Style.OutFighter, 1.4f) },
            ["crystal_golem"] = new[] { P(Style.Guardian, 1.7f), P(Style.Brawler, 1.4f) },
            ["candy_man"] = new[] { P(Style.Trickster, 1.4f), P(Style.Swarmer, 1.3f) },
            ["beetle"] = new[] { P(Style.Guardian, 1.5f), P(Style.Brawler, 1.4f) },
            ["seal"] = new[] { P(Style.CounterPuncher, 1.4f), P(Style.Swarmer, 1.2f) },
            ["unicorn"] = new[] { P(Style.OutFighter, 1.7f), P(Style.Tactician, 1.4f) },
        };

        /// <summary>Leans 'w' (one weight per School) toward the schools of an animal people; false if it has none.</summary>
        public static bool LeanSchools(ActorAsset asset, float[] w)
        {
            return Is(asset) && Apply(SchoolLeans, Kind(asset), w);
        }

        /// <summary>Leans 'w' (one weight per Style) toward the temper of an animal people; false if it has none.</summary>
        public static bool LeanStyles(ActorAsset asset, float[] w)
        {
            return Is(asset) && Apply(StyleLeans, Kind(asset), w);
        }

        [SakugaInit]
        private static void Init()
        {
            // dev: every species that builds kingdoms, how the mod sees it, and whether its kind has leanings
            Dev.Commands["peoples"] = args =>
            {
                int n = 0, missing = 0;
                foreach (ActorAsset s in AssetManager.actor_library.list)
                {
                    if (s == null || s.id == null || s.id.StartsWith("$", StringComparison.Ordinal) || !Is(s))
                    {
                        continue;
                    }
                    n++;
                    string kind = Kind(s);
                    bool classic = kind == "human" || kind == "elf" || kind == "orc" || kind == "dwarf";
                    bool leans = classic || SchoolLeans.ContainsKey(kind) && StyleLeans.ContainsKey(kind);
                    if (!leans)
                    {
                        missing++;
                    }
                    Dev.Log("people " + s.id + ": weight " + Weights.Species(s) + ", " + Preys.Name(Preys.OfSpecies(s)) + ", hp "
                        + Weights.SpeciesHealth(s).ToString("F0") + ", size " + s.actor_size + ", humanoid " + s.is_humanoid
                        + ", hands " + s.body_separate_part_hands + ", items " + s.use_items + (leans ? "" : "  (no leanings: plain schools)"));
                }
                Dev.Log("peoples: " + n + " civilized species, " + missing + " without leanings");
            };
            // dev: per civilized species, its living adults, fighters by tier and the techniques it has used
            Hooks.TechniqueStarted += (f, plan) =>
            {
                if (Dev.On && f != null && f.A != null && f.A.asset != null)
                {
                    _techniques.TryGetValue(f.A.asset.id, out int c);
                    _techniques[f.A.asset.id] = c + 1;
                }
            };
            Dev.Commands["peoplestats"] = args =>
            {
                Dictionary<string, int[]> by = new Dictionary<string, int[]>();
                foreach (Actor a in World.world.units)
                {
                    if (a == null || !a.isAlive() || !Is(a.asset) || !a.isAdult())
                    {
                        continue;
                    }
                    if (!by.TryGetValue(a.asset.id, out int[] row))
                    {
                        row = new int[Tiers.Count + 2];
                        by[a.asset.id] = row;
                    }
                    row[0]++;
                    row[(int)Tiers.Of(a) + 1]++;
                }
                foreach (ActorAsset s in AssetManager.actor_library.list)
                {
                    if (s == null || s.id == null || s.id.StartsWith("$", StringComparison.Ordinal) || !Is(s))
                    {
                        continue;
                    }
                    by.TryGetValue(s.id, out int[] row);
                    _techniques.TryGetValue(s.id, out int used);
                    string tiers = "";
                    for (int t = 1; t <= Tiers.Count && row != null; t++)
                    {
                        tiers += (t > 1 ? "/" : "") + row[t + 1];
                    }
                    Dev.Log("peoplestats " + s.id + ": adults " + (row != null ? row[0] : 0) + ", fighters N/A/E/M/GM/G "
                        + (row != null ? tiers : "-") + ", techniques used " + used);
                }
            };
        }

        [SakugaInit]
        private static void InitGrowthProbe()
        {
            // dev: why units do or don't grow (a sample of civilized adults, and the totals)
            Dev.Commands["growth"] = args =>
            {
                double now = World.world.getCurWorldTime();
                int adults = 0, awaken = 0, stamped = 0, warriors = 0;
                float sum = 0f;
                int shown = 0;
                foreach (Actor a in World.world.units)
                {
                    if (a == null || !a.isAlive() || !Is(a.asset) || !a.isAdult())
                    {
                        continue;
                    }
                    adults++;
                    bool can = Progression.CanAwaken(a);
                    if (can) awaken++;
                    a.data.get("sakuga_year", out float last, -1f);
                    if (last >= 0f) stamped++;
                    if (a.isWarrior() || a.hasArmy()) warriors++;
                    float sxp = Progression.Sxp(a);
                    sum += sxp;
                    if (shown < 12)
                    {
                        shown++;
                        Dev.Log("growth " + a.asset.id + "#" + a.Sid() + ": tier " + Tiers.Of(a) + ", sxp " + sxp.ToString("F1") + ", kills " + a.data.kills
                            + ", age " + a.getAge() + ", stamp " + last.ToString("F0") + " (now " + now.ToString("F0") + "), canAwaken " + can
                            + ", warrior " + (a.isWarrior() || a.hasArmy()) + ", kingdomCiv " + a.isKingdomCiv());
                    }
                }
                Dev.Log("growth: " + adults + " civilized adults, " + awaken + " may awaken, " + stamped + " year-stamped, " + warriors
                    + " warriors, mean sxp " + (adults > 0 ? sum / adults : 0f).ToString("F1") + ", now " + now.ToString("F0")
                    + ", speed " + (Config.time_scale_asset != null ? Config.time_scale_asset.id + " ticks " + Config.time_scale_asset.ticks : "?"));
            };
        }

        private static readonly Dictionary<string, int> _techniques = new Dictionary<string, int>();

        private static bool Apply(Dictionary<string, Lean[]> table, string kind, float[] w)
        {
            if (!table.TryGetValue(kind, out Lean[] leans))
            {
                return false;
            }
            for (int i = 0; i < leans.Length; i++)
            {
                if (leans[i].I >= 0 && leans[i].I < w.Length)
                {
                    w[leans[i].I] *= leans[i].K;
                }
            }
            return true;
        }
    }
}
