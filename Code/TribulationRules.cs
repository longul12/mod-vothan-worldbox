using System;

namespace Sakuga
{
    internal static class TribulationRules
    {
        public const int Rounds = 9;
        private static readonly float[] Fractions = { .06f, .08f, .10f, .12f, .14f, .16f, .19f, .22f, .30f };
        public static bool NearDeath(float health, float maximum)
        {
            return maximum > 0 && health > 0 && health / maximum < .005f;
        }
        public static bool Qualified(float xp, int xpRequired, float years, int yearsRequired,
            int kills, int killsRequired, bool brink, bool requireBrink)
        {
            return xp >= xpRequired && years >= yearsRequired && kills >= killsRequired && (!requireBrink || brink);
        }
        public static float Damage(int round, float maximum, float strength)
        {
            if (round < 1 || round > Rounds || float.IsNaN(maximum) || float.IsInfinity(maximum)
                || float.IsNaN(strength) || float.IsInfinity(strength)) return 0;
            float basis = Math.Max(3500f, Math.Min(8000f, maximum));
            return basis * Fractions[round - 1] * Math.Max(.1f, Math.Min(5f, strength));
        }
        public static bool Successful(int completed, bool alive, float health)
        {
            return completed == Rounds && alive && health > 0;
        }
        public static bool Survived(bool alive, bool sameLife, float health, float seconds)
        {
            return alive && sameLife && health > 0 && seconds >= 30f;
        }
    }
}
