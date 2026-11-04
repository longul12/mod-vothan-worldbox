using System.Text;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Arithmetic on Forms (the kinds of attack fighters read and answer): counting them, how well a
    /// set of answers meets an attack, short names for the dev log. (Named FormSet, not Forms: inside
    /// a technique or a move "Forms" is the property.)
    /// </summary>
    internal static class FormSet
    {
        /// <summary>How many Form flags exist (bits 0..Bits-1).</summary>
        public const int Bits = 41;

        /// <summary>Do the two share any form?</summary>
        public static bool Any(Form a, Form b)
        {
            return (a & b) != 0;
        }

        /// <summary>How many forms are set.</summary>
        public static int Count(Form f)
        {
            ulong x = (ulong)f;
            int n = 0;
            while (x != 0)
            {
                x &= x - 1;
                n++;
            }
            return n;
        }

        /// <summary>
        /// How well 'answers' meets 'incoming' (0 = not at all, 1 = squarely): one shared form is a
        /// match already; each further shared form makes it a surer one.
        /// </summary>
        public static float Match(Form answers, Form incoming)
        {
            if (answers == Form.None || incoming == Form.None)
            {
                return 0f;
            }
            Form both = answers & incoming;
            if (both == Form.None)
            {
                return 0f;
            }
            int shared = Count(both);
            return Mathf.Clamp01(0.6f + 0.2f * (shared - 1));
        }

        /// <summary>The Form of bit i.</summary>
        public static Form Bit(int i)
        {
            return (Form)(1UL << i);
        }

        private static readonly string[] _names =
        {
            "Punch", "Kick", "Knee", "Elbow", "Head", "Palm", "Grab", "Throw", "Blade", "Ki", "Beam", "Projectile", "Charge",
            "Dash", "Teleport", "Aerial", "AntiAir", "Rising", "Overhead", "Low", "High", "Area", "Line", "Flurry", "Feint",
            "Counter", "Guard", "Interrupt", "Evasive", "Clone", "Ground", "Drill", "Heavy", "Fast", "Ultimate", "Team", "Hunt",
            "Body", "Spin", "Straight", "Hook"
        };

        /// <summary>"Punch|Rising|Fast" (dev log).</summary>
        public static string Name(Form f)
        {
            if (f == Form.None)
            {
                return "-";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Bits; i++)
            {
                if (((ulong)f & (1UL << i)) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('|');
                    }
                    sb.Append(_names[i]);
                }
            }
            return sb.ToString();
        }
    }
}
