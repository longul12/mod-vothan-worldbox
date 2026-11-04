using System;
using System.Collections.Generic;
using System.Reflection;
using NeoModLoader.General;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Events other parts of the mod listen to, so each feature lives in its own file.
    /// Register from a static method marked [SakugaInit] (called once at mod load, after
    /// assets and traits exist).
    /// </summary>
    internal static class Hooks
    {
        /// <summary>Once per game frame, after moves tick (dt = move time, 0 while paused or held).</summary>
        public static Action<float> FrameTick;

        /// <summary>Every frame for every fighter (dt = game time, 0 while paused).</summary>
        public static Action<Fighter, float> FighterTick;

        /// <summary>A technique's blow landed (attacker, victim, result, the move or null).</summary>
        public static Action<Actor, Actor, HitResult, Move> HitDealt;

        /// <summary>Any unit killed another (from the game's own kill bookkeeping).</summary>
        public static Action<Actor, Actor> Killed;

        /// <summary>A technique started (fighter, plan).</summary>
        public static Action<Fighter, Plan> TechniqueStarted;

        /// <summary>A move ended (move, aborted).</summary>
        public static Action<Move, bool> MoveEnded;

        /// <summary>A move started (any: a technique's, a flow strike, a puppet...), right after Moves.Start took it.</summary>
        public static Action<Move> MoveStarted;

        /// <summary>
        /// A technique's contact was answered or not (Combat.Reach): the move, the unit it reached, what
        /// came of it. Reads and the fighters' memories listen.
        /// </summary>
        public static Action<Move, Actor, Contact> Reached;

        /// <summary>A charging move was broken (victim, the one who broke it or null, the move that was broken).</summary>
        public static Action<Actor, Actor, Move> ChargeBroken;

        /// <summary>A reactive technique answered an attack (defender, attacker, technique, counter depth of the attack, result).</summary>
        public static Action<Actor, Actor, Technique, int, Contact> Answered;

        /// <summary>A duel exchange began (the duel, the exchange).</summary>
        public static Action<Duel, Exchange> ExchangeStarted;

        /// <summary>The world was cleared or a save loaded.</summary>
        public static Action WorldCleared;

        /// <summary>A unit's tier changed (unit, old, new, natural).</summary>
        public static Action<Actor, Tier, Tier, bool> TierChanged;

        /// <summary>A unit held several tiers at once (a save, another mod): the lower ones were removed (unit, removed tiers, kept tier).</summary>
        public static Action<Actor, List<Tier>, Tier> TiersMerged;

        /// <summary>
        /// About to take damage (victim, damage, type, attacker). Return true to cancel the hit
        /// entirely (Last Light, a perfect parry). Runs before the game applies it.
        /// </summary>
        public static readonly List<Func<Actor, float, AttackType, BaseSimObject, bool>> BeforeDamage = new List<Func<Actor, float, AttackType, BaseSimObject, bool>>();

        /// <summary>
        /// An ordinary (non-technique) attack is about to land on 'target' (MapBox.applyAttack).
        /// Return true to deflect it: the game then treats it as deflected (no damage).
        /// </summary>
        public static readonly List<Func<AttackData, Actor, bool>> BeforeOrdinaryHit = new List<Func<AttackData, Actor, bool>>();

        /// <summary>
        /// Should the Brain hold this fighter's techniques for now (e.g. a duel is about to claim it, so
        /// the first meeting of two worthy opponents opens with a stare-down, not a random technique)?
        /// Any handler returning true holds it.
        /// </summary>
        public static readonly List<Func<Actor, bool>> HoldTechniques = new List<Func<Actor, bool>>();

        /// <summary>Calls every [SakugaInit] method in the mod.</summary>
        public static void RunInits()
        {
            foreach (Type t in typeof(Hooks).Assembly.GetTypes())
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.GetCustomAttributes(typeof(SakugaInitAttribute), false).Length == 0 || m.GetParameters().Length != 0)
                    {
                        continue;
                    }
                    try
                    {
                        m.Invoke(null, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Sakuga] init " + t.Name + "." + m.Name + ": " + e);
                    }
                }
            }
        }

        public static void Safe(Action a, string what)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] " + what + ": " + e);
            }
        }
    }

    /// <summary>Marks a static, parameterless method to run once when the mod loads (after traits exist).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class SakugaInitAttribute : Attribute
    {
    }

    /// <summary>Localized text.</summary>
    internal static class Locale
    {
        public static string Get(string key)
        {
            try
            {
                return LM.Get(key);
            }
            catch
            {
                return key;
            }
        }

        /// <summary>Text with {0}, {1}... filled in.</summary>
        public static string Format(string key, params object[] args)
        {
            string s = Get(key);
            try
            {
                return string.Format(s, args);
            }
            catch
            {
                return s;
            }
        }
    }
}

namespace Sakuga
{
    internal static class IdExtensions
    {
        /// <summary>
        /// A unit's id that never throws: a unit the game has disposed (its data gone) answers -1.
        /// Use it to check that a pooled actor is still the same unit.
        /// </summary>
        public static long Sid(this Actor a)
        {
            return a != null && a.data != null ? a.data.id : -1L;
        }

        public static long Sid(this BaseSimObject o)
        {
            if (o == null)
            {
                return -1L;
            }
            if (o.isActor())
            {
                return Sid(o.a);
            }
            try
            {
                return o.getID();
            }
            catch
            {
                return -1L;
            }
        }
    }
}
