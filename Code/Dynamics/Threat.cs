using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// How dangerous one unit is to another, and how big an answer that calls for. A unit's fight
    /// value is the square root of what it can take (health through its armour) times what it deals
    /// (damage with its tier's Might): the threat of a foe is its value over one's own. A dragon is a
    /// mortal danger to an Expert (about 2.4) and a nuisance to a God (about 0.2): the God flicks it
    /// away (a Casual technique) and keeps its ultimate. Even foes call for Heavy answers; a stronger
    /// foe, an army, or one's own last stand for the Ultimate ones.
    /// </summary>
    internal static class Threat
    {
        /// <summary>What a unit is worth in a fight (its current health, armour, damage, Might).</summary>
        public static float Value(Actor a)
        {
            if (a == null || a.stats == null)
            {
                return 1f;
            }
            float hp = Mathf.Max(1f, a.getHealth());
            float armor = Mathf.Clamp(a.stats["armor"], 0f, 90f) / 100f;
            TierInfo info = Tiers.Info(Tiers.Of(a));
            float dmg = Mathf.Max(4f, a.stats["damage"]) * (info != null ? info.Might : 1f) * Fighters.DamageMult(a);
            return Mathf.Sqrt(hp / (1f - armor) * dmg);
        }

        /// <summary>The threat 'foe' poses to 'self' (1 = an equal; 0.2 a nuisance; 3 a mortal danger).</summary>
        public static float Ratio(Actor self, Actor foe)
        {
            return Value(foe) / Mathf.Max(1f, Value(self));
        }

        /// <summary>
        /// The Scale (as 0 Casual .. 3 Ultimate, in between too) that answers a threat in proportion:
        /// 2 + 0.75 log2(threat): an equal Heavy, a fifth of one Casual, two of one Ultimate. At its
        /// last stand (a quarter of its health, against a real threat) a fighter reaches higher.
        /// </summary>
        public static float Ideal(float threat, float hpPct)
        {
            float s = 2f + 0.75f * Mathf.Log(Mathf.Max(0.01f, threat), 2f);
            if (hpPct < 0.3f && threat >= 0.3f)
            {
                s += 0.75f;
            }
            return Mathf.Clamp(s, -0.5f, 3.5f);
        }

        /// <summary>
        /// How fitting a technique of Scale 's' is for that ideal (0.04 .. 1): bigger than called for
        /// is wasted and heavily discouraged (no ultimate on a chicken); smaller is a little weaker
        /// but always acceptable (a flick is never wrong).
        /// </summary>
        public static float Fit(Scale s, float ideal)
        {
            float d = (int)s - ideal;
            if (d <= 0f)
            {
                return Mathf.Clamp(1f + 0.1f * d, 0.65f, 1f);
            }
            return Mathf.Max(0.04f, Mathf.Pow(0.4f, d));
        }
    }
}
