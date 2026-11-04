using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>The combat ladder. None = an ordinary unit (it may still know bare move traits).</summary>
    internal enum Tier
    {
        None = 0,
        Novice = 1,
        Adept = 2,
        Expert = 3,
        Master = 4,
        Grandmaster = 5,
        God = 6
    }

    /// <summary>Everything that differs between tiers, in one row each.</summary>
    internal sealed class TierInfo
    {
        public Tier Tier;
        public string TraitId;
        public string Icon;
        public Rarity Rarity;

        /// <summary>The tier's colour (icon frame, trails, rings, aura), a lighter and a darker shade.</summary>
        public Color Main, Light, Dark;
        public string Hex;

        // ---- trait stats (added to the unit)
        public float Health, MultHealth, Damage, MultDamage, Armor, Speed, AttackSpeed, Crit, CritDamage, Mana, Stamina, Skill, Lifespan;

        // ---- tempo
        /// <summary>Mana regenerated per second (on top of the game's slow regen).</summary>
        public float ManaRegen;
        /// <summary>Technique cooldowns are multiplied by this.</summary>
        public float CooldownMult;
        /// <summary>Seconds between two techniques that are not chained (readability: low tiers never strobe).</summary>
        public float MinGap;
        /// <summary>Seconds between two looks at the battlefield.</summary>
        public float Think;
        /// <summary>Tiles it looks around itself.</summary>
        public float Awareness;
        /// <summary>Scores are multiplied by 1 +- this (bad judgement at low tiers).</summary>
        public float Noise;
        /// <summary>Chance to ignore a good opening and just swing (Novices are eager but clumsy).</summary>
        public float JustAttack;
        public int MaxChain;
        /// <summary>Power rating: how much one of these is worth in a fight.</summary>
        public float PR;
        /// <summary>The heaviest class of enemy it may use techniques on.</summary>
        public Weight MaxClass;
        /// <summary>Base chance to read and answer an incoming technique (parry, evade).</summary>
        public float React;

        // ---- natural promotion into this tier
        public float Sxp;
        public int Kills;
        public int Level;
        public float Seasoning;
        public float Trial;

        public float Pr => PR;

        /// <summary>Technique damage multiplier: higher tiers hit far harder with the same move.</summary>
        public float Might;

        /// <summary>
        /// How long recover / landing / sheathe beats last at this tier (1 = as authored): high tiers
        /// land crisply and go. Novice 1, Adept 1, Expert 0.85, Master 0.55, Grandmaster 0.48, God 0.42.
        /// </summary>
        public float Linger;

        /// <summary>Fights with techniques only (no plain weapon swings): Masters and above.</summary>
        public bool Flow => Tier >= Tier.Master;
    }

    internal static class Tiers
    {
        public const int Count = 6;
        private static readonly TierInfo[] _info = new TierInfo[Count + 1];
        private static readonly ActorTrait[] _traits = new ActorTrait[Count + 1];

        public static readonly UnityEngine.Color Crimson = new UnityEngine.Color(0.78f, 0.06f, 0.16f, 1f);

        static Tiers()
        {
            Def(Tier.Novice, "sakuga_tier_novice", "iconTierNovice", Rarity.R0_Normal, "#D7DBE0", "#FFFFFF", "#6E737A",
                health: 40, multHealth: 0f, damage: 3, multDamage: 0f, armor: 2, speed: 2, attackSpeed: 0.5f, crit: 0.02f, critDamage: 0f,
                mana: 60, stamina: 20, skill: 0.05f, lifespan: 0,
                regen: 3f, cd: 1f, gap: 1.3f, think: 0.65f, aware: 5f, noise: 0.45f, justAttack: 0.08f, chain: 2, pr: 1f, max: Weight.Folk, react: 0.1f,
                sxp: 10, kills: 1, level: 0, season: 0f, trial: 0.08f);
            Def(Tier.Adept, "sakuga_tier_adept", "iconTierAdept", Rarity.R1_Rare, "#66AFFF", "#CFE8FF", "#2F5E9E",
                health: 110, multHealth: 0f, damage: 7, multDamage: 0f, armor: 5, speed: 3, attackSpeed: 1f, crit: 0.04f, critDamage: 0f,
                mana: 120, stamina: 40, skill: 0.10f, lifespan: 0,
                regen: 5f, cd: 0.9f, gap: 1.1f, think: 0.55f, aware: 6f, noise: 0.35f, justAttack: 0.04f, chain: 3, pr: 2f, max: Weight.Folk, react: 0.18f,
                sxp: 60, kills: 1, level: 0, season: 2f, trial: 0.10f);
            Def(Tier.Expert, "sakuga_tier_expert", "iconTierExpert", Rarity.R2_Epic, "#FFE45E", "#FFF7B0", "#A8892A",
                health: 240, multHealth: 0.05f, damage: 12, multDamage: 0f, armor: 9, speed: 5, attackSpeed: 2f, crit: 0.06f, critDamage: 0.2f,
                mana: 220, stamina: 70, skill: 0.18f, lifespan: 125,
                regen: 9f, cd: 0.8f, gap: 0.9f, think: 0.5f, aware: 8f, noise: 0.2f, justAttack: 0.03f, chain: 4, pr: 4f, max: Weight.Brute, react: 0.25f,
                sxp: 110, kills: 2, level: 0, season: 1f, trial: 0.08f);
            Def(Tier.Master, "sakuga_tier_master", "iconTierMaster", Rarity.R3_Legendary, "#FF7028", "#FFB27A", "#9A3A12",
                health: 520, multHealth: 0.15f, damage: 22, multDamage: 0.10f, armor: 15, speed: 9, attackSpeed: 3f, crit: 0.10f, critDamage: 0.4f,
                mana: 400, stamina: 110, skill: 0.28f, lifespan: 250,
                regen: 16f, cd: 0.6f, gap: 0.35f, think: 0.3f, aware: 11f, noise: 0.1f, justAttack: 0f, chain: 5, pr: 8f, max: Weight.Fiend, react: 0.45f,
                sxp: 300, kills: 3, level: 0, season: 2f, trial: 0.06f);
            Def(Tier.Grandmaster, "sakuga_tier_grandmaster", "iconTierGrandmaster", Rarity.R3_Legendary, "#FF2A3A", "#FF8A92", "#5A0A12",
                health: 1200, multHealth: 0.30f, damage: 40, multDamage: 0.20f, armor: 22, speed: 13, attackSpeed: 4f, crit: 0.14f, critDamage: 0.6f,
                mana: 700, stamina: 160, skill: 0.40f, lifespan: 500,
                regen: 26f, cd: 0.45f, gap: 0.15f, think: 0.22f, aware: 14f, noise: 0.05f, justAttack: 0f, chain: 6, pr: 16f, max: Weight.Titan, react: 0.6f,
                sxp: 700, kills: 5, level: 0, season: 2f, trial: 0.04f);
            Def(Tier.God, "sakuga_tier_god", "iconTierGod", Rarity.R3_Legendary, "#FFD35A", "#FFF4C2", "#2A0006",
                health: 3000, multHealth: 0.50f, damage: 70, multDamage: 0.30f, armor: 32, speed: 18, attackSpeed: 5f, crit: 0.20f, critDamage: 1.0f,
                mana: 1200, stamina: 250, skill: 0.55f, lifespan: 1000,
                regen: 45f, cd: 0.3f, gap: 0.05f, think: 0.15f, aware: 20f, noise: 0.03f, justAttack: 0f, chain: 8, pr: 40f, max: Weight.Kaiju, react: 0.75f,
                sxp: 1200, kills: 8, level: 3, season: 3f, trial: 0f);
        }

        private static void Def(Tier t, string trait, string icon, Rarity rarity, string main, string light, string dark,
            float health, float multHealth, float damage, float multDamage, float armor, float speed, float attackSpeed, float crit, float critDamage,
            float mana, float stamina, float skill, float lifespan,
            float regen, float cd, float gap, float think, float aware, float noise, float justAttack, int chain, float pr, Weight max, float react,
            float sxp, int kills, int level, float season, float trial)
        {
            _info[(int)t] = new TierInfo
            {
                Tier = t,
                TraitId = trait,
                Icon = "ui/Icons/sakuga/" + icon,
                Rarity = rarity,
                Main = Hex(main),
                Light = Hex(light),
                Dark = Hex(dark),
                Hex = main,
                Health = health,
                MultHealth = multHealth,
                Damage = damage,
                MultDamage = multDamage,
                Armor = armor,
                Speed = speed,
                AttackSpeed = attackSpeed,
                Crit = crit,
                CritDamage = critDamage,
                Mana = mana,
                Stamina = stamina,
                Skill = skill,
                Lifespan = lifespan,
                ManaRegen = regen,
                CooldownMult = cd,
                MinGap = gap,
                Think = think,
                Awareness = aware,
                Noise = noise,
                JustAttack = justAttack,
                MaxChain = chain,
                PR = pr,
                MaxClass = max,
                React = react,
                Sxp = sxp,
                Kills = kills,
                Level = level,
                Seasoning = season,
                Trial = trial,
                Linger = t switch { Tier.Expert => 0.85f, Tier.Master => 0.55f, Tier.Grandmaster => 0.48f, Tier.God => 0.42f, _ => 1f },
                Might = t switch { Tier.Novice => 1f, Tier.Adept => 1.12f, Tier.Expert => 1.3f, Tier.Master => 1.55f, Tier.Grandmaster => 1.9f, Tier.God => 2.5f, _ => 1f }
            };
        }

        public static UnityEngine.Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out UnityEngine.Color c) ? c : UnityEngine.Color.white;
        }

        public static TierInfo Info(Tier t)
        {
            return t == Tier.None ? null : _info[(int)t];
        }

        public static IEnumerable<TierInfo> All
        {
            get
            {
                for (int i = 1; i <= Count; i++)
                {
                    yield return _info[i];
                }
            }
        }

        /// <summary>Called once the tier traits exist (Traits.Register).</summary>
        internal static void Link()
        {
            for (int i = 1; i <= Count; i++)
            {
                _traits[i] = AssetManager.traits.get(_info[i].TraitId);
            }
        }

        public static ActorTrait Trait(Tier t)
        {
            return t == Tier.None ? null : _traits[(int)t];
        }

        /// <summary>The unit's tier (the highest one if a save or another mod stacked several).</summary>
        public static Tier Of(Actor a)
        {
            if (a == null || a.traits == null || a.traits.Count == 0)
            {
                return Tier.None;
            }
            // (a Primordial is a God and more to every system: Primordials.Is tells it apart)
            if (Primordials.Trait != null && a.traits.Contains(Primordials.Trait))
            {
                return Tier.God;
            }
            for (int i = Count; i >= 1; i--)
            {
                ActorTrait t = _traits[i];
                if (t != null && a.traits.Contains(t))
                {
                    return (Tier)i;
                }
            }
            return Tier.None;
        }

        public static bool IsTierTrait(ActorTrait t)
        {
            if (t != null && t == Primordials.Trait)
            {
                return true;
            }
            for (int i = 1; i <= Count; i++)
            {
                if (_traits[i] == t)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The tier's colour; ordinary units (bare move traits) use a warm white.</summary>
        public static UnityEngine.Color Color(Tier t)
        {
            return t == Tier.None ? new UnityEngine.Color(1f, 0.96f, 0.86f, 1f) : _info[(int)t].Main;
        }

        public static UnityEngine.Color LightColor(Tier t)
        {
            return t == Tier.None ? UnityEngine.Color.white : _info[(int)t].Light;
        }

        public static UnityEngine.Color DarkColor(Tier t)
        {
            return t == Tier.None ? new UnityEngine.Color(0.45f, 0.45f, 0.5f, 1f) : _info[(int)t].Dark;
        }

        /// <summary>The tier's name as the player sees it ("Combat Master").</summary>
        public static string Name(Tier t)
        {
            return t == Tier.None ? "" : Locale.Get("trait_" + _info[(int)t].TraitId);
        }

        /// <summary>The unit's recovery factor (TierInfo.Linger; 1 for ordinary units).</summary>
        public static float Linger(Actor a)
        {
            TierInfo i = Info(Of(a));
            return i != null ? i.Linger : 1f;
        }

        /// <summary>Power rating of a unit: its tier's, or a plain unit's by its size class.</summary>
        public static float Power(Actor a)
        {
            Tier t = Of(a);
            if (t != Tier.None)
            {
                return Primordials.Is(a) ? _info[(int)t].PR * 12f : _info[(int)t].PR;
            }
            switch (Weights.Species(a))
            {
                case Weight.Critter: return 0.3f;
                case Weight.Folk: return 0.6f;
                case Weight.Brute: return 1.5f;
                case Weight.Fiend: return 3f;
                case Weight.Titan: return 20f;
                default: return 60f;
            }
        }
    }

    /// <summary>
    /// The colours a technique is drawn in. Level I is plain white and grey (clean, quick, weak);
    /// level II takes the tier's colour on trails, swooshes and rings; level III burns white-hot
    /// at the core with the tier's colour at the edge. Fire, dust and rocks keep their natural colours.
    /// </summary>
    internal struct Palette
    {
        public Color Core;    // brightest (flashes, the heart of a slash)
        public Color Edge;    // the tier tint (rims, trails)
        public Color Ghost;   // afterimage start
        public Color GhostEnd;// afterimage end (alpha 0)
        public Color Dark;    // deep shade (outlines, black-flash rims)
        public Tier Tier;
        public int Level;

        public static Palette For(Actor a, int level)
        {
            return For(Tiers.Of(a), level);
        }

        public static Palette For(Tier tier, int level)
        {
            Color main = Tiers.Color(tier);
            Color light = Tiers.LightColor(tier);
            Palette p = new Palette { Tier = tier, Level = level, Dark = Tiers.DarkColor(tier) };
            if (level <= 1 || tier == Tier.None && level <= 1)
            {
                p.Core = Color.white;
                p.Edge = new Color(0.86f, 0.9f, 0.96f, 1f);
                p.Ghost = new Color(1f, 1f, 1f, 0.7f);
                p.GhostEnd = new Color(0.75f, 0.82f, 0.95f, 0f);
            }
            else if (level == 2)
            {
                p.Core = Color.Lerp(Color.white, light, 0.5f);
                p.Edge = main;
                p.Ghost = Fx.WithA(Color.Lerp(light, Color.white, 0.3f), 0.8f);
                p.GhostEnd = Fx.WithA(main, 0f);
            }
            else
            {
                p.Core = new Color(1f, 0.99f, 0.94f, 1f);
                p.Edge = main;
                p.Ghost = new Color(1f, 1f, 1f, 0.9f);
                p.GhostEnd = Fx.WithA(main, 0f);
            }
            if (tier == Tier.None)
            {
                // ordinary units with bare move traits: a warm white at every level
                p.Edge = level >= 2 ? new Color(1f, 0.9f, 0.7f, 1f) : p.Edge;
                p.GhostEnd = new Color(1f, 0.85f, 0.6f, 0f);
            }
            return p;
        }

        /// <summary>A colour ramp from the core out to the tier edge and transparent (for gradients).</summary>
        public Color[] Ramp(float alpha = 1f)
        {
            return new[] { Fx.WithA(Core, alpha), Fx.WithA(Edge, alpha * 0.85f), Fx.WithA(Edge, 0f) };
        }
    }
}
