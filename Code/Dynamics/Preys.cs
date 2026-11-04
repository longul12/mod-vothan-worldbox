using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// What kind of creature a unit is, for techniques made for some (Technique.Prey: a hunter's
    /// harpoon for flyers, a blessed blow for the undead, a knee-splitter for giants): people,
    /// beasts, brutes, the undead, fiends, casters, archers, flyers, dragons, titans, kaiju, swarms
    /// of small things, constructs, boats. Read from the game's own assets (cached per species) and,
    /// for what a unit carries or does right now (a bow, spells, flight), from the unit itself.
    /// </summary>
    internal static class Preys
    {
        private static readonly Dictionary<ActorAsset, Prey> _species = new Dictionary<ActorAsset, Prey>();

        // (asset ids are not all lower case: the UFO's is "UFO")
        private static readonly HashSet<string> Undead = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "skeleton", "zombie", "ghost", "necromancer", "jumpy_skull", "zombie_dragon", "lich", "mummy"
        };

        private static readonly HashSet<string> Fiends = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "demon", "cold_one", "evil_mage", "greg", "fire_elemental", "fire_elemental_blob", "fire_elemental_horse",
            "fire_elemental_slug", "fire_elemental_snake", "fire_skull", "tumor_monster_unit", "tumor_monster_animal",
            "assimilator", "bioblob", "alien", "angle"
        };

        private static readonly HashSet<string> Casters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "evil_mage", "white_mage", "necromancer", "druid", "plague_doctor", "witch"
        };

        private static readonly HashSet<string> Constructs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "UFO", "god_finger", "crystal_golem", "assimilator", "living_house", "snowman", "robot", "smore", "super_pumpkin"
        };

        private static readonly HashSet<string> Dragons = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dragon", "zombie_dragon" };

        /// <summary>Every kind of creature this unit is (several at once: a dragon is a Flyer, a Dragon and a Titan's weight).</summary>
        public static Prey Of(Actor a)
        {
            if (a == null || a.asset == null)
            {
                return Prey.None;
            }
            Prey p = Species(a.asset);
            // what this one does right now
            try
            {
                if (a.isFlying())
                {
                    p |= Prey.Flyer;
                }
                if (a.hasRangeAttack())
                {
                    p |= Prey.Ranged;
                }
                if (a.hasSpells())
                {
                    p |= Prey.Caster;
                }
            }
            catch
            {
                // (a unit mid-rebuild of its stats)
            }
            if (Weights.Species(a) == Weight.Critter && (p & (Prey.Humanoid | Prey.Undead | Prey.Fiend | Prey.Brute | Prey.Titan | Prey.Kaiju)) == 0)
            {
                p |= Prey.Swarm;   // (small things that come many: never a big body however frail)
            }
            return p;
        }

        /// <summary>
        /// Is there a building of people standing at 'p' (on its tile or one next to it), one of an enemy
        /// of 'by' if given? Never a tree, a bush, a field, a rock or a ruin: those are buildings to the
        /// world too. A plan of a technique made for buildings (Prey.Building) aimed at such a point is
        /// aimed at a building.
        /// </summary>
        public static bool BuildingAt(Vector2 p, Actor by = null)
        {
            WorldTile t = Combat.TileAt(p);
            if (t == null || World.world == null)
            {
                return false;
            }
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    WorldTile n = dx == 0 && dy == 0 ? t : World.world.GetTile(t.x + dx, t.y + dy);
                    Building b = n != null ? n.building : null;
                    if (b != null && b.isAlive() && !b.isRuin() && b.asset != null && !b.asset.flora && b.isCiv() && b.kingdom != null
                        && (by == null || by.kingdom == null || b.kingdom != by.kingdom && by.kingdom.isEnemy(b.kingdom)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>What a species is, before what one of its units carries or does (dev: "preys").</summary>
        public static Prey OfSpecies(ActorAsset asset)
        {
            return asset != null ? Species(asset) : Prey.None;
        }

        /// <summary>Does the unit count as any of these?</summary>
        public static bool Is(Actor a, Prey kinds)
        {
            return (Of(a) & kinds) != 0;
        }

        /// <summary>A monster: anything but people (the God's flick, the hunters' calamity).</summary>
        public static bool Monster(Actor a)
        {
            Prey p = Of(a);
            return (p & Prey.Humanoid) == 0 && p != Prey.None && (p & Prey.Boat) == 0;
        }

        private static Prey Species(ActorAsset asset)
        {
            if (_species.TryGetValue(asset, out Prey p))
            {
                return p;
            }
            p = Prey.None;
            if (Peoples.Is(asset))
            {
                // a civilized people is people (crystal golem folk are no construct, unicorn folk no brute)
                p = Prey.Humanoid;
                if (asset.flying || asset.very_high_flyer)
                {
                    p |= Prey.Flyer;
                }
                if (asset.spell_ids != null && asset.spell_ids.Count > 0)
                {
                    p |= Prey.Caster;
                }
                _species[asset] = p;
                return p;
            }
            string id = asset.id ?? "";
            if (id.StartsWith("civ_", StringComparison.Ordinal))
            {
                id = id.Substring(4);
            }
            if (asset.is_boat)
            {
                p |= Prey.Boat;
            }
            if (asset.flying || asset.very_high_flyer)
            {
                p |= Prey.Flyer;
            }
            if (Dragons.Contains(id))
            {
                p |= Prey.Dragon | Prey.Flyer;
            }
            if (Undead.Contains(id) || asset.unit_zombie || asset.kingdom_id_wild == "undead" || id.Contains("skeleton") || id.Contains("zombie"))
            {
                p |= Prey.Undead;
            }
            if (Fiends.Contains(id) || id.StartsWith("fire_elemental", StringComparison.Ordinal) || asset.kingdom_id_wild == "demon")
            {
                p |= Prey.Fiend;
            }
            if (Casters.Contains(id) || asset.spell_ids != null && asset.spell_ids.Count > 0)
            {
                p |= Prey.Caster;
            }
            if (Constructs.Contains(id) || asset.flag_ufo || asset.flag_finger)
            {
                p |= Prey.Construct;
            }
            ActorAsset a = asset;
            Weight w = SpeciesWeight(a);
            ActorSize size = asset.actor_size;
            if (w == Weight.Kaiju)
            {
                p |= Prey.Kaiju;
            }
            // a giant: a titan's or a kaiju's might, or a body of the biggest size (a lil pumpkin, Crabzilla;
            // the dragons are Dragons, the machines Constructs, a ship a Boat)
            if ((w >= Weight.Titan || size >= ActorSize.S17_Dragon) && (p & (Prey.Construct | Prey.Dragon | Prey.Boat)) == 0)
            {
                p |= Prey.Titan;
            }
            bool people = (asset.is_humanoid || asset.civ && (id == "human" || id == "orc" || id == "elf" || id == "dwarf"))
                && (p & (Prey.Undead | Prey.Fiend | Prey.Construct | Prey.Boat)) == 0;
            if (people)
            {
                p |= Prey.Humanoid;
            }
            else if ((p & (Prey.Undead | Prey.Fiend | Prey.Construct | Prey.Boat | Prey.Dragon | Prey.Kaiju)) == 0)
            {
                p |= Prey.Beast;
            }
            // a brute: a brute's or a fiend's might, or a big body (a bear's, a buffalo's: a rhino is no critter)
            if ((w == Weight.Brute || w == Weight.Fiend || w < Weight.Titan && size >= ActorSize.S15_Bear) && (p & (Prey.Construct | Prey.Boat)) == 0)
            {
                p |= Prey.Brute;
            }
            _species[asset] = p;
            return p;
        }

        private static Weight SpeciesWeight(ActorAsset asset)
        {
            float hp = Weights.SpeciesHealth(asset);
            string id = asset.id ?? "";
            if (id == "crabzilla" || hp > 5000f)
            {
                return Weight.Kaiju;
            }
            if (Dragons.Contains(id) || string.Equals(id, "UFO", StringComparison.OrdinalIgnoreCase) || id == "god_finger" || hp > 900f)
            {
                return Weight.Titan;
            }
            if (hp > 400f)
            {
                return Weight.Fiend;
            }
            if (hp > 180f)
            {
                return Weight.Brute;
            }
            return hp > 80f ? Weight.Folk : Weight.Critter;
        }

        /// <summary>"Beast|Brute" (dev log).</summary>
        public static string Name(Prey p)
        {
            if (p == Prey.None)
            {
                return "-";
            }
            List<string> parts = new List<string>();
            foreach (Prey k in (Prey[])Enum.GetValues(typeof(Prey)))
            {
                if (k != Prey.None && k != Prey.Any && (p & k) == k)
                {
                    parts.Add(k.ToString());
                }
            }
            return string.Join("|", parts);
        }

        public static void Clear()
        {
            _species.Clear();
        }
    }
}
