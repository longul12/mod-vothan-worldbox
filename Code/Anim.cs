using UnityEngine;

namespace Sakuga
{
    /// <summary>Easing curves and other small things every move uses.</summary>
    internal static class Anim
    {
        public static float OutQuad(float k)
        {
            return 1f - (1f - k) * (1f - k);
        }

        public static float OutCubic(float k)
        {
            float i = 1f - k;
            return 1f - i * i * i;
        }

        public static float InQuad(float k)
        {
            return k * k;
        }

        public static float InOutCubic(float k)
        {
            return k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
        }

        public static float InOutSine(float k)
        {
            return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * k);
        }

        /// <summary>Smoothstep of k between a and b.</summary>
        public static float Smooth(float a, float b, float k)
        {
            float t = Mathf.Clamp01((k - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A damped spring from 1 back to 0: overshoots once, settles.</summary>
        public static float Spring(float t, float damping, float freq)
        {
            return Mathf.Exp(-damping * t) * Mathf.Cos(freq * t);
        }

        public static void Sound(string id, WorldTile tile)
        {
            try
            {
                MusicBox.playSound(id, tile, false, true);
            }
            catch
            {
                // a missing sound never stops a move
            }
        }
    }
}
