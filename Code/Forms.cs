using System;

namespace Sakuga
{
    /// <summary>
    /// What kind of attack a technique (or a move, right now) is. Fighters read it and answer it:
    /// an uppercut (Rising) leaves the side open for a slip and a liver shot, a charge (Charge) can be
    /// broken by a fast blow (Interrupt), a dash (Dash) can be side-stepped, a beam (Beam) reflected or
    /// cut through, a grab (Grab) broken and reversed. Flags: a move is usually several at once.
    /// </summary>
    [Flags]
    internal enum Form : ulong
    {
        None = 0,
        Punch = 1UL << 0,
        Kick = 1UL << 1,
        Knee = 1UL << 2,
        Elbow = 1UL << 3,
        Head = 1UL << 4,
        Palm = 1UL << 5,
        Grab = 1UL << 6,
        Throw = 1UL << 7,
        Blade = 1UL << 8,
        Ki = 1UL << 9,
        Beam = 1UL << 10,
        Projectile = 1UL << 11,
        /// <summary>Gathers power before it strikes: a fast blow in the window breaks it.</summary>
        Charge = 1UL << 12,
        Dash = 1UL << 13,
        Teleport = 1UL << 14,
        Aerial = 1UL << 15,
        AntiAir = 1UL << 16,
        /// <summary>Rises from below (uppercuts, rising cuts): opens the body's side.</summary>
        Rising = 1UL << 17,
        /// <summary>Comes down from above (axe kicks, hammer blows, dives).</summary>
        Overhead = 1UL << 18,
        /// <summary>Low (sweeps, trips): hop over it.</summary>
        Low = 1UL << 19,
        /// <summary>High (round kicks, hooks to the head): duck under it.</summary>
        High = 1UL << 20,
        Area = 1UL << 21,
        Line = 1UL << 22,
        Flurry = 1UL << 23,
        /// <summary>A bait: looks like something else, punishes the answer.</summary>
        Feint = 1UL << 24,
        /// <summary>An answer to someone else's attack.</summary>
        Counter = 1UL << 25,
        Guard = 1UL << 26,
        /// <summary>Fast enough to break a charge.</summary>
        Interrupt = 1UL << 27,
        Evasive = 1UL << 28,
        Clone = 1UL << 29,
        Ground = 1UL << 30,
        Drill = 1UL << 31,
        Heavy = 1UL << 32,
        Fast = 1UL << 33,
        Ultimate = 1UL << 34,
        Team = 1UL << 35,
        /// <summary>Meant for a kind of creature (see Technique.Prey).</summary>
        Hunt = 1UL << 36,
        /// <summary>To the body (liver, ribs).</summary>
        Body = 1UL << 37,
        Spin = 1UL << 38,
        Straight = 1UL << 39,
        Hook = 1UL << 40,

        /// <summary>Blows of the limbs (what a parry or a block meets).</summary>
        Strike = Punch | Kick | Knee | Elbow | Head | Palm | Blade,
        /// <summary>Things that fly at you (deflect, reflect, cut, dodge).</summary>
        Shot = Ki | Beam | Projectile
    }

    /// <summary>Kinds of creature a technique is made for (hunters' techniques, threat-proportional answers).</summary>
    [Flags]
    internal enum Prey : uint
    {
        None = 0,
        Humanoid = 1 << 0,
        Beast = 1 << 1,
        Brute = 1 << 2,
        Undead = 1 << 3,
        Fiend = 1 << 4,
        Caster = 1 << 5,
        Ranged = 1 << 6,
        Flyer = 1 << 7,
        Dragon = 1 << 8,
        Titan = 1 << 9,
        Kaiju = 1 << 10,
        Swarm = 1 << 11,
        Construct = 1 << 12,
        Boat = 1 << 13,
        Building = 1 << 14,
        Any = 0xFFFF
    }

    /// <summary>
    /// How big an answer a technique is: a fighter far above its enemy answers with a Casual one
    /// (a God flicks a dragon away), an equal with Heavy ones, an Ultimate is for the fight of its life.
    /// </summary>
    internal enum Scale
    {
        Casual,
        Light,
        Heavy,
        Ultimate
    }

    /// <summary>The combat schools: each fighter walks one, at every tier (Schools.Want); their techniques are its own.</summary>
    internal enum School
    {
        None,
        IronFist,
        TempestLeg,
        ThousandBlades,
        KiArts,
        EarthBreaker,
        PhantomStep,
        HuntersWay,
        HuyenNguyet
    }
}
