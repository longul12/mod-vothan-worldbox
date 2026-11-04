using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Hit-stop: on a big impact the whole world holds still for a few frames
    /// while the impact flash stays on screen (the anime "impact frame"), then
    /// everything snaps back into motion. Only for impacts the player is
    /// actually looking at up close, at normal speeds, and never more than
    /// once every couple of seconds, so a big battle doesn't stutter.
    /// </summary>
    internal static class HitStop
    {
        private const float Cooldown = 1.8f;

        private static float _left;
        private static float _next;

        public static bool Active => _left > 0f;

        public static void Request(Vector2 at, float seconds)
        {
            if (World.world == null || Config.time_scale_asset == null || Config.time_scale_asset.multiplier > 2f)
            {
                return;
            }
            Camera cam = World.world.camera;
            if (cam == null || cam.orthographicSize > 45f || !Fx.OnScreen(at, 0f))
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now < _next)
            {
                return;
            }
            _next = now + Cooldown;
            _left = Mathf.Max(_left, seconds);
        }

        /// <summary>Counts down in real time (the frozen world has no game time).</summary>
        public static void Tick()
        {
            if (_left > 0f)
            {
                _left -= Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            }
        }

        public static void Clear()
        {
            _left = 0f;
        }
    }
}
