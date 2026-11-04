using System;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Sakuga: anime-style combat for WorldBox units.
    ///
    /// WorldBox units are ~10 pixel sprites with no attack frames, so a move is animated the
    /// way a 2D animator would do it at that size: the unit's own sprite is squashed, stretched,
    /// flipped and flown along an arc (Moves), and everything around it is drawn in the game's
    /// own pixel density from procedural pixel art (Art, Fx, Kit): dust, cracks, shockwaves,
    /// afterimages, hit-stop.
    ///
    /// On top of that: a combat ladder (Tiers: Novice to Combat God) whose fighters know
    /// techniques at levels I-III (Technique, one file per technique), a tactical mind that picks
    /// them by the situation and chains them into combos (Brain), mana, natural progression
    /// (Progression), duels and team techniques, God abilities, cinematics (Cinema), settings.
    /// </summary>
    public class SakugaMod : BasicMod<SakugaMod>
    {
        protected override void OnModLoad()
        {
            Step("settings", Settings.Reload);
            Step("dev", Dev.Init);
            Step("art", Art.Init);
            Step("techniques", Techniques.Discover);
            Step("forge", Forge.Load);
            Step("traits", Traits.Register);
            Step("powers", Powers.Register);
            Step("init", Hooks.RunInits);
            Step("tab", Powers.AddTab);
            Step("settings ready", Settings.Init);
            PatchEach(new Harmony("turbo.sakuga"));
            LogInfo("Combat Gods ready on WorldBox " + Application.version + ": " + Techniques.All.Count + " techniques");
        }

        private void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                LogError(what + " failed: " + e);
            }
        }

        /// <summary>One patch class at a time, so a game update that breaks one patch only disables that feature.</summary>
        private void PatchEach(Harmony harmony)
        {
            foreach (Type t in AccessTools.GetTypesFromAssembly(typeof(SakugaMod).Assembly))
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    LogError("patch " + t.Name + " failed: " + e);
                }
            }
        }

        private void Update()
        {
            try
            {
                Dev.Poll();
                Settings.Poll();
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] update: " + e);
            }
        }

        /// <summary>
        /// Once per game frame, right after the world's simulation step (and only on real game
        /// frames: not while a world is loading).
        /// </summary>
        internal static void Frame()
        {
            if (World.world == null)
            {
                return;
            }
            // (stuns dealt from here on are the mod's own choreography: no stun resistance applies)
            KiDeflect.Own++;
            try
            {
                FrameParts();
            }
            finally
            {
                KiDeflect.Own--;
            }
        }

        private static void FrameParts()
        {
            float dt = 0f;
            try
            {
                HitStop.Tick();
                Cinema.Tick();
                dt = Clock.Tick();
            }
            catch (Exception e)
            {
                Report("clock", e);
            }
            // each part on its own: one failing never stops the others (or the game)
            try
            {
                Moves.Update(dt, Cinema.Dt);
            }
            catch (Exception e)
            {
                Report("moves", e);
            }
            try
            {
                Hooks.FrameTick?.Invoke(dt);
            }
            catch (Exception e)
            {
                Report("frame tick", e);
            }
            try
            {
                Brain.Update(dt);
            }
            catch (Exception e)
            {
                Report("brain", e);
            }
            try
            {
                Progression.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Report("progression", e);
            }
            try
            {
                Fx.Update(dt, Cinema.Dt);
            }
            catch (Exception e)
            {
                Report("fx", e);
            }
            try
            {
                Dev.Update();
            }
            catch (Exception e)
            {
                Report("dev", e);
            }
        }

        private static float _lastReport;
        private static string _lastWhat;

        /// <summary>Logs a failure, but not the same one every frame.</summary>
        internal static void Report(string what, Exception e)
        {
            if (what == _lastWhat && Time.realtimeSinceStartup - _lastReport < 5f)
            {
                return;
            }
            _lastWhat = what;
            _lastReport = Time.realtimeSinceStartup;
            Debug.LogError("[Sakuga] " + what + ": " + e);
        }

        /// <summary>The world is cleared or a save is loaded: nothing from the old world may remain.</summary>
        internal static void ClearAll()
        {
            NinefoldTribulation.ForgetWorld();
            Hooks.Safe(Moves.Clear, "clear moves");
            Hooks.Safe(Tumble.Clear, "clear tumble");
            Hooks.Safe(Fx.Clear, "clear fx");
            Hooks.Safe(HitStop.Clear, "clear hit-stop");
            Hooks.Safe(Cinema.Clear, "clear cinema");
            Hooks.Safe(Fighters.Clear, "clear fighters");
            Hooks.Safe(Director.Clear, "clear director");
            Hooks.Safe(Progression.Clear, "clear progression");
            Hooks.Safe(Dev.WorldCleared, "clear dev");
            Hooks.Safe(() => Hooks.WorldCleared?.Invoke(), "clear hooks");
        }
    }

    /// <summary>
    /// Move time. Moves follow game time (they pause with the game and speed up with it) but
    /// never play faster than x3, so a slam stays readable at x20. Hit-stops and cinematics hold it.
    /// </summary>
    internal static class Clock
    {
        /// <summary>Dev slow motion (1 = normal).</summary>
        public static float Scale = 1f;

        /// <summary>This frame's move time step.</summary>
        public static float Dt;

        public static float Tick()
        {
            if (World.world == null || World.world.isPaused() || HitStop.Active || Cinema.Frozen)
            {
                Dt = 0f;
                return 0f;
            }
            float cap = Time.fixedDeltaTime * 3f;
            Dt = Mathf.Min(World.world.elapsed, cap) * Scale * Cinema.Rate;
            return Dt;
        }
    }

    /// <summary>
    /// The world's simulation step: skipped while a hit-stop or a cinematic holds the world
    /// (and on slow-motion frames); afterwards, the mod's own frame.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    internal static class SimulationPatch
    {
        /// <summary>Whether this frame's simulation was skipped (held world).</summary>
        internal static bool Skipped;

        private static bool Prefix()
        {
            try
            {
                Skipped = HitStop.Active || Cinema.SkipFrame();
                return !Skipped;
            }
            catch
            {
                Skipped = false;
                return true;
            }
        }

        private static void Postfix()
        {
            SakugaMod.Frame();
            if (Skipped)
            {
                Kit.DrawScars(UnityEngine.Time.deltaTime);
            }
        }
    }
}
