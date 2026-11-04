using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THE KAIJU KILLER (the easter egg). When the player summons Crabzilla while a Combat God walks
    /// the world, the God lets the player stomp around for a moment, then looks to the sky, flies
    /// across the map, takes the kaiju apart in sixteen blinks, drives it into the ground as a meteor,
    /// kicks its death-nuke into the sky where it goes off in a nuclear airburst, and floats down beside the
    /// crater. Off (setting) or with no hero: nothing special, tiers fight Crabzilla like anyone else.
    ///
    /// This class is the director: it arms every Crabzilla when it spawns (the game possesses it for
    /// the player: ControllableUnit.setControllableCreatureCrabzilla), waits for the grace period,
    /// picks the hero (the God with the most kills; a Grandmaster if the setting allows and no God
    /// exists; a second God joins as a wingman), and starts the KaijuKillerMove. With no hero it
    /// stays armed: a unit that becomes a God while Crabzilla lives answers. A tier given to
    /// Crabzilla disarms it for good ("The Gods hesitate").
    ///
    /// What it does to the game, only for the Crabzilla in the cinematic and only while it runs:
    ///   - holds it still (its Crabzilla.update is skipped: no walking, no laser, no stale input),
    ///   - shields it from everyone else's damage (Hooks.BeforeDamage), its corpse too until the game
    ///     has processed its death (a stray hit would clear its attackedBy and steal the hero's kill),
    ///   - takes over its death nuke (ActionLibrary.startCrabzillaNuke is skipped for it).
    /// If it dies any other way (or is deleted) before the hero's blow, everything is let go at once
    /// and the vanilla nuke goes off; the hero blinks clear of the blast.
    /// </summary>
    internal static class KaijuKiller
    {
        public const string SlayerId = "sakuga_kaiju_slayer";

        /// <summary>The hero stops and looks to the sky at most this long before the grace period ends.</summary>
        public const float GazeLead = 4f;

        private const int MaxTries = 3;

        private sealed class Armed
        {
            public Actor Crab;
            public long Id;
            public double Since;
            public bool Trained;
            public bool Done;
            public int Tries;
            public double WakeAt;
            public long Summoned = -1;
        }

        private static readonly List<Armed> _armed = new List<Armed>();
        private static KaijuKillerMove _move;
        private static float _scan;
        private static bool _trainedSaid;
        private static Actor _repossess;
        private static long _repossessId = -1;

        /// <summary>The first cinematic of the session shows "???" instead of its title.</summary>
        internal static bool TitleShown;

        internal static ActorTrait Slayer;

        // ---- the crab under the cinematic (ids, -1 = none); read by the patches
        /// <summary>Its Crabzilla.update is skipped: it stands still, its laser off, deaf to input.</summary>
        internal static long HeldCrab = -1;
        /// <summary>Damage from anyone but the cinematic is ignored (kept on the corpse: the kill stays the hero's).</summary>
        internal static long ShieldCrab = -1;
        /// <summary>Its death nuke belongs to the cinematic.</summary>
        internal static long ContainCrab = -1;

        /// <summary>
        /// A Crabzilla waiting for its Kaiju Killer (armed, not answered yet): the heroes who may answer
        /// leave it to the cinematic (a God fighting a war right there would kill it with plain
        /// techniques first, and its nuke would go off uncontained).
        /// </summary>
        internal static bool Awaits(Actor crab, Actor by)
        {
            if (_armed.Count == 0 || crab == null || by == null || !Settings.CrabzillaCinematic)
            {
                return false;
            }
            if (Tiers.Of(by) < (Settings.CrabzillaGrandmasters ? Tier.Grandmaster : Tier.God) || !Suitable(by, crab))
            {
                return false;   // (one that could never answer, a God dragon, fights it like anyone)
            }
            long id = crab.Sid();
            for (int i = 0; i < _armed.Count; i++)
            {
                Armed a = _armed[i];
                if (a.Id == id && !a.Done && !a.Trained)
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>The cinematic's own blows pass the shield.</summary>
        internal static bool OurHit;

        // ------------------------------------------------------------ setup --

        [SakugaInit]
        private static void Init()
        {
            RegisterTrait();
            Hooks.TierChanged += OnTierChanged;
            Hooks.WorldCleared += Clear;
            Hooks.BeforeDamage.Add(Shield);
        }

        /// <summary>Kaiju Slayer: a legendary merit trait, +10% damage.</summary>
        private static void RegisterTrait()
        {
            Slayer = AssetManager.traits.get(SlayerId);
            if (Slayer != null)
            {
                return;
            }
            Slayer = AssetManager.traits.add(new ActorTrait
            {
                id = SlayerId,
                group_id = Traits.GroupDivine,   // (a God's deed, not a tier)
                path_icon = "ui/Icons/sakuga/iconKaijuSlayer",
                type = TraitType.Positive,
                rarity = Rarity.R3_Legendary,
                can_be_given = true,
                can_be_removed = true,
                needs_to_be_explored = false,
                rate_birth = 0,
                rate_inherit = 0,
                rate_acquire_grow_up = 0,
                spawn_random_trait_allowed = false,
                is_mutation_box_allowed = false,
                can_be_in_book = false,
                affects_mind = false,
                can_be_cured = false,
                can_be_removed_by_divine_light = false,
                can_be_removed_by_accelerated_healing = false,
                remove_for_zombie_actor_asset = false,
                priority = 108,
                base_stats = new BaseStats()
            });
            Slayer.base_stats["multiplier_damage"] = 0.1f;
        }

        private static void Clear()
        {
            _armed.Clear();
            _move = null;
            _scan = 0f;
            _repossess = null;
            _repossessId = -1;
            HeldCrab = ShieldCrab = ContainCrab = -1;
            OurHit = false;
        }

        // ----------------------------------------------------------- events --

        /// <summary>A Crabzilla was born (and possessed for the player).</summary>
        internal static void OnSpawn(Actor crab)
        {
            if (crab == null || crab.asset == null || crab.asset.id != "crabzilla")
            {
                return;
            }
            long id = crab.Sid();
            if (id < 0)
            {
                return;
            }
            foreach (Armed a in _armed)
            {
                if (a.Id == id)
                {
                    return;   // the cinematic handing it back to the player, not a new one
                }
            }
            _armed.Add(new Armed { Crab = crab, Id = id, Since = Fighter.Now });
            _scan = 0f;
            if (Dev.On) Dev.Trace("kaiju: Crabzilla #" + id + " armed (grace " + Settings.CrabzillaGrace + " s)");
        }

        private static void OnTierChanged(Actor a, Tier old, Tier now, bool natural)
        {
            if (a == null || a.asset == null)
            {
                return;
            }
            if (a.asset.id == "crabzilla")
            {
                if (now == Tier.None)
                {
                    return;
                }
                long id = a.Sid();
                foreach (Armed x in _armed)
                {
                    if (x.Id == id)
                    {
                        x.Trained = true;
                    }
                }
                if (!_trainedSaid)
                {
                    _trainedSaid = true;
                    WorldTip.showNow(Locale.Get("sakuga_crab_trained"), false, "top", 3.5f, "#FF8A92");
                }
                return;
            }
            if (now >= Tier.Grandmaster)
            {
                _scan = 0f;   // a new hero may answer now
            }
        }

        /// <summary>Hooks.BeforeDamage: nobody but the hero touches the held Crabzilla.</summary>
        private static bool Shield(Actor victim, float damage, AttackType type, BaseSimObject attacker)
        {
            if (ShieldCrab >= 0 && !OurHit && victim != null && victim.Sid() == ShieldCrab)
            {
                return true;
            }
            // (a God's plain blows wait for the cinematic too)
            bool block = _armed.Count > 0 && !OurHit && attacker != null && attacker.isActor() && Awaits(victim, attacker.a);
            if (Dev.On && _armed.Count > 0 && victim != null && victim.asset != null && victim.asset.id == "crabzilla")
            {
                Actor x = attacker != null && attacker.isActor() ? attacker.a : null;
                Moves.TryGet(x, out Move xm);
                Dev.Trace("kaiju: Crabzilla #" + victim.Sid() + " hit " + damage.ToString("F0") + " " + type + " by " + (x != null ? x.asset.id + "#" + x.Sid() + " (" + Tiers.Of(x) + ")" : attacker != null ? attacker.GetType().Name : "nobody")
                    + (xm != null ? " in " + xm.GetType().Name : "") + (block ? ": blocked" : "") + " hp " + victim.getHealth()
                    + (Combat.Landing != null ? " landing " + Combat.Landing.GetType().Name : ""));
            }
            return block;
        }

        internal static bool IsTrained(long crabId)
        {
            foreach (Armed a in _armed)
            {
                if (a.Id == crabId)
                {
                    return a.Trained;
                }
            }
            return false;
        }

        // ------------------------------------------------------- crab state --

        internal static void HoldCrab(long id)
        {
            HeldCrab = id;
            ShieldCrab = id;
        }

        /// <summary>Lets a still-living Crabzilla go (the cinematic gave up before its blow).</summary>
        internal static void ReleaseCrab(long id, Actor giveBack)
        {
            if (HeldCrab == id)
            {
                HeldCrab = -1;
            }
            if (ShieldCrab == id)
            {
                ShieldCrab = -1;
            }
            if (ContainCrab == id)
            {
                ContainCrab = -1;
            }
            if (giveBack != null)
            {
                // next frame (never inside a world clear): back into the player's hands, inputs fresh
                _repossess = giveBack;
                _repossessId = id;
            }
        }

        /// <summary>The move ended (any way).</summary>
        internal static void Ended(KaijuKillerMove m, bool killed)
        {
            if (_move == m)
            {
                _move = null;
            }
            for (int i = _armed.Count - 1; i >= 0; i--)
            {
                Armed a = _armed[i];
                if (a.Id != m.CrabId)
                {
                    continue;
                }
                bool standing = a.Crab != null && a.Crab.Sid() == a.Id && a.Crab.isAlive() && a.Crab.hasHealth();
                if (killed || !standing || a.Tries >= MaxTries)
                {
                    a.Done = true;
                }
                else
                {
                    // someone else may answer in a moment
                    a.Since = Fighter.Now - Settings.CrabzillaGrace + 2.5;
                }
            }
            _scan = 0.5f;
        }

        // ------------------------------------------------------------ frame --

        /// <summary>Every frame (cheap: nothing unless a Crabzilla is armed).</summary>
        internal static void Frame()
        {
            if (World.world == null)
            {
                return;
            }
            if (_repossess != null)
            {
                Repossess();
            }
            if (_armed.Count == 0)
            {
                return;
            }
            _scan -= Time.unscaledDeltaTime;
            if (_scan > 0f)
            {
                return;
            }
            _scan = 0.25f;
            for (int i = _armed.Count - 1; i >= 0; i--)
            {
                Armed a = _armed[i];
                bool alive = a.Crab != null && a.Crab.Sid() == a.Id && a.Crab.isAlive() && a.Crab.hasHealth();
                if ((!alive || a.Done) && (_move == null || _move.CrabId != a.Id))
                {
                    _armed.RemoveAt(i);
                }
            }
            if (_move != null && _move.Ended)
            {
                _move = null;
            }
            if (_move != null || World.world.isPaused() || !Settings.CrabzillaCinematic)
            {
                return;
            }
            double now = Fighter.Now;
            foreach (Armed a in _armed)
            {
                if (a.Done || a.Trained || Tiers.Of(a.Crab) != Tier.None)
                {
                    continue;
                }
                double graceEnd = a.Since + Settings.CrabzillaGrace;
                if (now < graceEnd - GazeLead)
                {
                    continue;
                }
                Actor hero = PickHero(a.Crab, null, false, out Tier tier);
                if (hero == null)
                {
                    if (now >= graceEnd && Summon(a))
                    {
                        continue;   // a God is being called off its fight: it answers when its move ends
                    }
                    if (now >= a.WakeAt)
                    {
                        // a God asleep while a kaiju walks the world: its eyes snap open (it answers next scan)
                        a.WakeAt = now + 5.0;
                        WakeHeroes(a.Crab);
                    }
                    continue;   // stays armed: a God may still rise
                }
                KaijuKillerMove m = KaijuKillerMove.Begin(hero, tier, a.Crab, a.Since, graceEnd);
                if (m != null)
                {
                    _move = m;
                    a.Tries++;
                    if (Dev.On) Dev.Trace("kaiju: " + hero.getName() + " (" + tier + ", " + hero.data.kills + " kills) answers Crabzilla #" + a.Id);
                    return;
                }
                if (KaijuKillerMove.RigFailed)
                {
                    // a Crabzilla the cinematic can't take hold of (its parts not found) is never answered:
                    // it stops waiting, so the heroes fight it like anyone (armed, Awaits and the shield
                    // kept every God off it for good)
                    a.Done = true;
                    if (Dev.On) Dev.Trace("kaiju: Crabzilla #" + a.Id + " can't be rigged: no cinematic");
                }
            }
        }

        private static void Repossess()
        {
            Actor c = _repossess;
            long id = _repossessId;
            _repossess = null;
            _repossessId = -1;
            try
            {
                if (c != null && c.Sid() == id && c.isAlive() && c.hasHealth() && !ControllableUnit.isControllingUnit())
                {
                    // the game's possession also sets the world to x1 and unpauses it: the player's own
                    // speed and pause stay (a hero picked up by the god hand while paused)
                    bool paused = Config.paused;
                    WorldTimeScaleAsset speed = Config.time_scale_asset;
                    bool sonic = speed != null && speed.sonic && DebugConfig.isOn(DebugOption.SonicSpeed);
                    ControllableUnit.setControllableCreatureCrabzilla(c);
                    Config.paused = paused;
                    if (speed != null && Config.time_scale_asset != speed)
                    {
                        Config.setWorldSpeed(speed, false);
                    }
                    if (sonic)
                    {
                        DebugConfig.setOption(DebugOption.SonicSpeed, true, false);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] kaiju repossess: " + e);
            }
        }

        // ------------------------------------------------------------- heroes --

        /// <summary>Could this unit ever fly up to Crabzilla (a sprite unit that walks)?</summary>
        private static bool Suitable(Actor a, Actor crab)
        {
            if (a == null || a == crab || !Combat.Living(a) || a.asset == null)
            {
                return false;   // (a God a lethal blow already took dies at the next step: not a hero)
            }
            ActorAsset s = a.asset;
            return s.has_sprite_renderer && !s.is_boat && !s.has_avatar_prefab && !s.special && !s.ignore_generic_render
                && s.can_be_moved_by_powers && !s.flying && s.id != "crabzilla";
        }

        /// <summary>
        /// The God with the most kills that can go right now (favourites break ties). If no God exists
        /// at all and the setting allows it, a Grandmaster. 'godsOnly': the wingman.
        /// </summary>
        internal static Actor PickHero(Actor crab, Actor exclude, bool godsOnly, out Tier tier)
        {
            tier = Tier.None;
            bool anyGod;
            Actor best = Best(Tier.God, crab, exclude, out anyGod);
            if (best != null)
            {
                tier = Tier.God;
                return best;
            }
            if (godsOnly || anyGod || !Settings.CrabzillaGrandmasters)
            {
                return null;   // a God exists but is busy: wait for it
            }
            best = Best(Tier.Grandmaster, crab, exclude, out _);
            if (best != null)
            {
                tier = Tier.Grandmaster;
            }
            return best;
        }

        /// <summary>
        /// Every God is caught up in a fight (a god war, a duel) at each look: the kaiju comes first. The
        /// strongest is called off it: its duel ends, and it starts nothing new (a short scene, renewed
        /// at every look) until its current move ends and it answers.
        /// </summary>
        private static bool Summon(Armed a)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            Vector2 at = a.Crab.current_position;
            IReadOnlyList<Fighter> list = Fighters.All;
            for (int i = 0; i < list.Count; i++)
            {
                Fighter f = list[i];
                if (!f.Valid || Tiers.Of(f.A) != Tier.God || !Suitable(f.A, a.Crab) || ControllableUnit.isControllingUnit(f.A))
                {
                    continue;
                }
                float score = f.A.data.kills + (f.A.isFavorite() ? 0.5f : 0f) + (f.A.Sid() == a.Summoned ? 1000f : 0f)
                    - Vector2.Distance(f.A.current_position, at) * 0.0001f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = f.A;
                }
            }
            if (best == null)
            {
                return false;
            }
            Duel d = Duels.Of(best);
            if (d != null)
            {
                d.End("a kaiju walks", false);
            }
            // (world time, renewed every 0.25 s of real time: 0.6 s lapsed between looks from x3 speed up,
            // and the God started a new fight in the gap)
            Director.Claim(a, 2.5f, best);
            if (a.Summoned != best.Sid())
            {
                a.Summoned = best.Sid();
                if (Dev.On) Dev.Trace("kaiju: " + best.getName() + " is called off its fight for Crabzilla #" + a.Id);
            }
            return true;
        }

        /// <summary>Wakes the Gods (and Grandmasters, when they may answer) sleeping through a kaiju's rampage.</summary>
        private static void WakeHeroes(Actor crab)
        {
            IReadOnlyList<Fighter> list = Fighters.All;
            for (int i = 0; i < list.Count; i++)
            {
                Fighter f = list[i];
                if (!f.Valid)
                {
                    continue;
                }
                Tier t = Tiers.Of(f.A);
                if ((t != Tier.God && !(t == Tier.Grandmaster && Settings.CrabzillaGrandmasters)) || !Suitable(f.A, crab))
                {
                    continue;
                }
                try
                {
                    if (f.A.hasStatus("sleeping"))
                    {
                        f.A.stopSleeping();
                        if (Dev.On) Dev.Trace("kaiju: " + f.A.getName() + " (" + t + ") wakes");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] kaiju wake: " + e);
                }
            }
        }

        private static Actor Best(Tier want, Actor crab, Actor exclude, out bool any)
        {
            any = false;
            Actor best = null;
            float bestScore = float.MinValue;
            Vector2 at = crab != null ? crab.current_position : Vector2.zero;
            IReadOnlyList<Fighter> list = Fighters.All;
            for (int i = 0; i < list.Count; i++)
            {
                Fighter f = list[i];
                if (!f.Valid || Tiers.Of(f.A) != want || !Suitable(f.A, crab))
                {
                    continue;
                }
                any = true;
                Actor a = f.A;
                if (a == exclude || !Moves.CanPerform(a))
                {
                    continue;
                }
                float score = a.data.kills + (a.isFavorite() ? 0.5f : 0f) - Vector2.Distance(a.current_position, at) * 0.0001f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- dev --

        /// <summary>
        /// Dev: a Combat God 'far' tiles left of the camera (the strongest God if one exists, else a
        /// new 'race' promoted to God) and a Crabzilla at the camera. The grace period is the setting's.
        /// </summary>
        internal static string DevDemo(string race, float far)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null)
            {
                return "no camera";
            }
            Vector3 cp = cam.transform.position;
            WorldTile center = Combat.TileAt(new Vector2(cp.x, cp.y));
            if (center == null)
            {
                return "no tile under the camera";
            }
            Actor god = PickHero(null, null, true, out _);
            if (god == null)
            {
                Vector2 at = Combat.Clamp(new Vector2(cp.x - far, cp.y));
                if (!Combat.FindLanding(at, 12f, out at))
                {
                    return "no land for the God";
                }
                god = World.world.units.spawnNewUnit(string.IsNullOrEmpty(race) ? "human" : race, Combat.TileAt(at), false, false, 0f, null, false, true);
                if (god == null)
                {
                    return "could not spawn a " + race;
                }
                Progression.Promote(god, Tier.God, false);
            }
            Actor crab = World.world.units.createNewUnit("crabzilla", center, false, 0f);
            return "kaiju demo: " + god.getName() + " (" + Tiers.Of(god) + ") vs Crabzilla " + (crab != null ? "#" + crab.Sid() : "failed");
        }
    }

    /// <summary>
    /// The cinematic itself, played by the hero (real seconds: it keeps playing while the world is held).
    ///
    /// Beat sheet (seconds from the moment the hero answers; ~16.5 s):
    ///
    ///   GAZE      grace  the hero stops, faces Crabzilla, looks up; a gold ring pulses at its feet
    ///                    every second; "X looks to the sky..." (once)
    ///   SUMMONS   0.0    power crouch, an aura pulse pushes nearby enemies 2 tiles, a gold ring,
    ///                    focus lines, a glint at the eye. Title "A COMBAT GOD ANSWERS" ("???" the
    ///                    first time in a session; "A GRANDMASTER ANSWERS" in red for a Grandmaster)
    ///   LIFTOFF   1.2    squash 0.15, then straight up to 16 tiles: takeoff ring, dust ring, rocks,
    ///                    vertical gold speed lines, the shadow shrinking to a dot; a God's launch
    ///                    scorches the ground it leaves (Kit.Ravage force 1)
    ///   FLIGHT    2.0    a high gold streak across the map in 1.2 s whatever the distance (an
    ///                    afterimage every 1.5 tiles), entering the view from its real direction,
    ///                    braking beside Crabzilla's face
    ///   STARE     3.2    the world freezes (full mode), the laser cuts out, the vignette closes to
    ///                    50%, focus lines on the hero; Crabzilla flashes red (it has noticed); "ting"
    ///   HEAVENS   4.4    16 blink-strikes on claws, legs, shell edge and face: a 2-tile crescent,
    ///                    a hit star, sparks (in twice the hero's pixel grain, near the kaiju's, so
    ///                    they read in a view that takes it all in), red-orange shell debris and a
    ///                    0.03 hit-stop each; the
    ///                    rhythm tightens from 0.30 to 0.12 s; gold lines zig-zag between the strike
    ///                    points; HP 100% -> 70% (strike 8) -> 35% (strike 16) with the game's own
    ///                    limb-damage sprites flickering red. Every blow scorches the ground in front
    ///                    of its feet where its shell falls (force 1); the 8th and 16th tear the turf
    ///                    open (force 2)
    ///   ASCENT    7.6    up to the top of the view, one spin, hold: a gold disk swells behind it
    ///                    (2 -> 4 tiles) around a white-hot sun, embers drawn in, vignette 60%, a red-gold target ring
    ///                    under Crabzilla
    ///   DIVE      9.4    recoil and "ting", sonic boom, then STARBREAKER: a white-gold meteor with a
    ///                    crimson trail (Meteor Slam III at 2x), 0.35 s
    ///   IMPACT   10.0    2 white frames, then a 6-tile black flash held on a frozen frame: the 0.6 s
    ///                    hit-stop, the longest in the mod; Crabzilla dies (the kill is the hero's);
    ///                    a crater with gold fissures glowing for a minute, and the ground torn
    ///                    open for real under the black frame: the game's own explosion 6 tiles
    ///                    round (Kit.Ravage force 4; a Grandmaster's 4 tiles, force 3), turf torn
    ///                    to bare earth, fires, trees and other kingdoms' buildings smashed
    ///           10.42    the frame lets go: rings at 3, 6 and 9 tiles, the dust wall, a storm of
    ///                    shell debris, the ground scorched out to the last ring (force 1, 9 tiles);
    ///                    everyone else within 9 tiles is knocked flat, unhurt by the blow
    ///                    (scorch marks and explosion pixels show through the freeze: the
    ///                    framework draws them while the world is held)
    ///   CORE     10.6    the nuke is ours: a white orb swells at the husk's core (0.5 -> 2 tiles,
    ///                    wobbling), sparks and focus lines; the burnt shell crumbles away
    ///   BLOOM    11.4    the hero blinks under the orb: a rising kick, a 3-tile crescent; the orb
    ///                    rockets out of the top of the view in 0.9 s on a fire trail and a smoke column
    ///   HOLD     12.3    the kick pose, stretched upward. Silence (1 s)
    ///   BURST    13.3    high in the sky: a starburst and a 30% view flash, an 8-tile ring, then a
    ///                    peony: a shell of gold streaks flung out 5 tiles, an orange one inside it,
    ///                    slowing, drooping and falling for 2.5 s, white glitter crackling where they
    ///                    fade (above the vignette). No terrain damage
    ///   DESCENT  13.3    floats down through the embers (2.5+ tiles/s), one flip
    ///   LAND    ~15.3    lands beside the crater, clear of the fires it lit (the wingman on the other side),
    ///                    a small puff, no crater; the vignette fades (0.8); back to the crater, one
    ///                    glint (a crab hero turns and bows twice instead); 0.6 s after touching
    ///                    down the world moves again
    ///
    /// Light mode (cinematics set to light, x5+ speed, Crabzilla off screen, another cinematic
    /// playing): the same, without the freeze and the vignette; bystanders are thrown instead of
    /// knocked flat. Every exit lifts the freeze, the vignette, the hold on Crabzilla and its tint.
    /// </summary>
    internal sealed class KaijuKillerMove : Move
    {
        /// <summary>The last Begin failed because the Crabzilla could not be rigged (no hero could ever answer it).</summary>
        internal static bool RigFailed;

        /// <summary>A God hunting the colossus: from the air, the heaviest blow there is.</summary>
        public override Form Forms => Form.Hunt | Form.Aerial | Form.Heavy | Form.Ultimate;

        private enum Phase
        {
            Gaze,
            Summons,
            Liftoff,
            Flight,
            Stare,
            Heavens,
            Ascent,
            Dive,
            Impact,
            Core,
            Bloom,
            Hold,
            Descent,
            Land,
            Exit
        }

        private const float TSummons = 1.2f;
        private const float TLiftoff = 0.8f;
        private const float TFlight = 1.2f;
        private const float TStare = 1.2f;
        /// <summary>The flurry's own clock; its 16 hit-stops bring it to 3.2 s.</summary>
        private const float THeavens = 2.76f;
        private const float TAscent = 1.8f;
        private const float TDive = 0.6f;
        private const float TImpact = 0.6f;
        /// <summary>When the impact's frozen frame lets go and the blast flies.</summary>
        private const float BlastAt = 0.42f;
        private const float TCore = 0.8f;
        private const float TBloom = 0.84f;
        private const float THold = 1.0f;
        private const float Cruise = 16f;
        /// <summary>How far under its feet an airborne performer is anchored (see SetFeet).</summary>
        private const float Anchor = 1.2f;
        private const int Strikes = 16;
        private const float StrikeStop = 0.03f;

        private static readonly Color Hot = new Color(1f, 0.98f, 0.86f, 1f);
        private static readonly Color Orange = new Color(1f, 0.55f, 0.15f, 1f);
        private static readonly Color Notice = new Color(1f, 0.1f, 0.06f, 1f);
        private static readonly Color Husk = new Color(0.2f, 0.035f, 0.045f, 1f);
        private static readonly Color Smoke = new Color(0.4f, 0.37f, 0.35f, 1f);
        private static readonly Color[] Shell =
        {
            Tiers.Hex("#E75B51"), Tiers.Hex("#A5413A"), Tiers.Hex("#FF816B"), Tiers.Hex("#FFAF69"), Tiers.Hex("#6B3A3C")
        };

        private readonly Tier _tier;
        private readonly Actor _crab;
        public readonly long CrabId;
        private readonly CrabRig _rig;
        private readonly double _graceEnd, _tipAt;
        private readonly Vector2 _origin;
        private readonly bool _crabHero;
        private readonly Color _main, _light;

        private Phase _phase = Phase.Gaze;
        private float _pt;
        private bool _entered;
        private int _cues;
        private float _hold;
        private float _wait = -1f;
        private bool _owns, _full, _tipped, _killed, _cleaned, _fleeing, _homeBlink, _standUp;
        /// <summary>
        /// Set once the move is over, whichever way (by Cleanup, also from Abort: it must not be the
        /// framework's Move.Ended, which Moves.End checks before it hands the unit back).
        /// </summary>
        public new bool Ended;

        private float _pulse, _ghost, _streak, _ember, _lines, _flashT, _acc, _dur;
        /// <summary>The Starbreaker's crater in tiles (a God's tears 6 tiles of ground, a Grandmaster's 4).</summary>
        private readonly float _craterR;
        private int _side = 1;
        private float _groundY;
        /// <summary>The real ground under the hero (its shadow), wherever it is anchored.</summary>
        private Vector2 _shadowAt;
        private Vector2 _crabRoot;
        private Vector2 _bodyC, _bodyExt, _impact;
        private float _impactH;
        private readonly Vector2[] _points = new Vector2[Strikes];
        private readonly int[] _limbs = new int[Strikes];
        private readonly float[] _times = new float[Strikes];
        private readonly float[] _leads = new float[Strikes];
        private int _next;
        private Vector2 _lastHit;
        private Vector2 _p0, _top, _g0, _lastBody, _land;
        private Vector2? _burstAt;

        private Particle _shadow, _glow, _disk, _diskCore, _target;
        /// <summary>Lit copies of the hero and the wingman drawn above the Ascent's disk.</summary>
        private Particle _lit, _litW;
        private bool _glowTop;

        // the core (the nuke the cinematic takes over)
        private enum Orb
        {
            None,
            Swell,
            Rise,
            Gone
        }

        private Orb _orb;
        private float _orbT, _orbR, _smoke;
        private Vector2 _orbPos, _orbFrom, _orbTo, _orbLast;
        private Particle _orbRim, _orbCore;

        // blink-strikes (the hero, and the wingman taking every other one)
        private sealed class Striker
        {
            public Move M;
            public Vector2 Body, From, Hit, Rest;
            public float AppearAt, HitAt;
            /// <summary>The blow's lean, wind-up to strike: a lunge, an axe kick (down) or a rising kick (up).</summary>
            public float Rot0, Rot1;
            public int Index = -1;
            public bool Struck = true;
            public bool FaceRight;
        }

        private Striker _sh, _sw;

        // the wingman (a second God)
        private KaijuWing _wing;
        private Actor _wingActor;
        private long _wingId = -1;
        private Vector2 _wOrigin, _wF0, _wG0, _wLast, _wLand, _wL0, _wLG0, _wShadow;
        private float _wT;
        private bool _wLanded;

        // bystanders knocked flat by the impact (full mode: held as puppets until the world moves)
        private sealed class Downed
        {
            public Puppet P;
            public Actor U;
            public Vector2 From, To;
            public float Delay, T, Apex, UpAt = -1f;
        }

        private readonly List<Downed> _downed = new List<Downed>();

        private KaijuKillerMove(Actor a, Tier tier, Actor crab, double since, double graceEnd) : base(a)
        {
            _tier = tier;
            _crab = crab;
            CrabId = crab.Sid();
            _rig = new CrabRig(crab);
            _graceEnd = graceEnd;
            _origin = a.current_position;
            _shadowAt = _origin;
            _crabRoot = crab.current_position;
            string id = a.asset.id;
            _crabHero = id == "crab" || id == "civ_crab";
            _main = Tiers.Color(tier);
            _light = Tiers.LightColor(tier);
            _craterR = tier == Tier.God ? 6f : 4f;
            // the tip comes a few seconds after the spawn (the game shows its own controls tip first)
            double now = Fighter.Now;
            _tipAt = Math.Min(Math.Max(since + 3.0, now + 0.3), Math.Max(graceEnd - 0.6, now + 0.3));
            // the flurry's rhythm: gaps from 0.30 to 0.12 s, hit-stops included
            float t = 0.04f;
            for (int i = 0; i < Strikes; i++)
            {
                float gapBefore = i == 0 ? 0.3f : Mathf.Lerp(0.27f, 0.09f, (i - 1) / 14f);
                if (i > 0)
                {
                    t += gapBefore;
                }
                _times[i] = t;
                _leads[i] = Mathf.Clamp(gapBefore * 0.42f, 0.04f, 0.1f);
            }
        }

        public override bool Cinematic => true;

        public override bool Intangible => true;

        public static KaijuKillerMove Begin(Actor hero, Tier tier, Actor crab, double since, double graceEnd)
        {
            RigFailed = false;
            if (!Moves.CanPerform(hero) || crab == null || !crab.isAlive() || !crab.hasHealth())
            {
                return null;
            }
            KaijuKillerMove m = new KaijuKillerMove(hero, tier, crab, since, graceEnd);
            if (m._rig.Cz == null)
            {
                RigFailed = true;
                return null;
            }
            if (!Moves.Start(m))
            {
                return null;
            }
            return m;
        }

        // ------------------------------------------------------------ helpers --

        private float BodyH => Pose.PivotUp * A.actor_scale;

        /// <summary>
        /// Puts a performer's feet at a point on screen; 'shadow' is the real ground under it (returned,
        /// clamped to the map). The game only draws a unit whose ground point is in view (and the view
        /// has no margin below), so a performer high in the air is anchored at most Anchor tiles under
        /// its feet, and its real shadow is drawn by the move.
        /// </summary>
        private static Vector2 SetFeet(Move m, Vector2 feet, Vector2 shadow)
        {
            shadow = Combat.Clamp(shadow);
            float lift = Mathf.Clamp(feet.y - shadow.y, 0f, Anchor);
            Vector2 anchor = Combat.Clamp(new Vector2(feet.x, feet.y - lift));
            m.Pose.Ground = anchor;
            m.Pose.Height = Mathf.Max(0f, feet.y - anchor.y);
            return shadow;
        }

        /// <summary>Puts a performer's body at a point on screen over the ground line 'groundY' (returns its shadow point).</summary>
        private static Vector2 Place(Move m, Vector2 body, float groundY)
        {
            float bh = m.Pose.PivotUp * m.A.actor_scale * m.Pose.SY;
            Vector2 feet = new Vector2(body.x, body.y - bh);
            return SetFeet(m, feet, new Vector2(feet.x, Mathf.Min(groundY, feet.y)));
        }

        private bool Seen(Vector2 at, float margin = 0.35f)
        {
            return Kit.Visible(at, margin);
        }

        private void Sound(string id, Vector2 at)
        {
            Anim.Sound(id, Combat.TileAt(at));
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("kaiju " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F2"));
            _phase = p;
            _pt = 0f;
            _entered = false;
            _cues = 0;
        }

        /// <summary>A one-shot cue within the current phase (bits 0-15 the hero's, 16+ the wingman's).</summary>
        private bool Once(int bit)
        {
            int m = 1 << bit;
            if ((_cues & m) != 0)
            {
                return false;
            }
            _cues |= m;
            return true;
        }

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        /// <summary>The live shell (centre, half size) and the hover spot beside its face.</summary>
        private void Track()
        {
            if (!_rig.Present)
            {
                return;
            }
            _crabRoot = _rig.Root;
            if (_phase < Phase.Stare)
            {
                _rig.Shell(out _bodyC, out _bodyExt);
                _groundY = Mathf.Max(0.5f, _crabRoot.y - 1f);
            }
        }

        private Vector2 StareBody(int side)
        {
            float x = _bodyC.x + side * (_bodyExt.x + 2.2f * K);
            x = Mathf.Clamp(x, 1f, MapBox.width - 2f);
            return new Vector2(x, _bodyC.y + _bodyExt.y * 0.45f);
        }

        private Vector2 Eye()
        {
            Vector2 head = HeadPos();
            return head + new Vector2((Pose.FaceRight ? 1.5f : -1.5f) * Px, -Pose.PivotUp * 0.35f * Px);
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (!CheckWorld())
            {
                return;
            }
            if (_flashT > 0f)
            {
                _flashT -= dt;
            }
            _rig.TickFlicker(_phase >= Phase.Stare && _phase < Phase.Impact ? dt : 0f);
            if (_hold > 0f)
            {
                // a hit-stop: the performers shake on the blow, the clock waits
                _hold -= dt;
                Vector2 n = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                Pose.Nudge = n;
                if (_wing != null)
                {
                    _wing.Pose.Nudge = -n;
                }
                UpdateShadow();
                return;
            }
            Pose.Nudge = Vector2.zero;
            if (_wing != null)
            {
                _wing.Pose.Nudge = Vector2.zero;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Gaze:
                    Gaze(dt);
                    break;
                case Phase.Summons:
                    Summons(dt);
                    break;
                case Phase.Liftoff:
                    Liftoff(dt);
                    break;
                case Phase.Flight:
                    Flight(dt);
                    break;
                case Phase.Stare:
                    Stare(dt);
                    break;
                case Phase.Heavens:
                    Heavens(dt);
                    break;
                case Phase.Ascent:
                    Ascent(dt);
                    break;
                case Phase.Dive:
                    Dive(dt);
                    break;
                case Phase.Impact:
                    Impact(dt);
                    break;
                case Phase.Core:
                    Core(dt);
                    break;
                case Phase.Bloom:
                    Bloom(dt);
                    break;
                case Phase.Hold:
                    HoldPose(dt);
                    break;
                case Phase.Descent:
                    Descent(dt);
                    break;
                case Phase.Land:
                    Land(dt);
                    break;
                default:
                    Exit(dt);
                    break;
            }
            if (_phase != Phase.Exit && _phase != Phase.Gaze)
            {
                Wing(dt);
            }
            UpdateOrb(dt);
            UpdateDowned(dt);
            UpdateShadow();
            if (_wing != null)
            {
                _wing.Shadow = _wShadow;   // where it falls if it is killed up there
            }
        }

        /// <summary>Is everything still there? Crabzilla gone before the blow, a trained crab, the setting off: wind down.</summary>
        private bool CheckWorld()
        {
            if (_wing != null && (_wing.Finished || !_wing.Valid))
            {
                if (!_wing.Finished)
                {
                    _wing.Done(_wing.Pose.Height <= 0.05f);
                }
                _wing = null;
                _sw = null;
            }
            if (_phase == Phase.Exit || _killed)
            {
                return true;
            }
            Track();
            if (_phase == Phase.Gaze)
            {
                if (!_rig.Standing || KaijuKiller.IsTrained(CrabId) || Tiers.Of(_crab) != Tier.None || !Settings.CrabzillaCinematic)
                {
                    Finished = true;
                    return false;
                }
                return true;
            }
            if (!_rig.Standing)
            {
                // killed or deleted by someone else before the blow: its own nuke is coming
                bool nuke = _rig.Dying;
                if (Dev.On) Dev.Trace("kaiju " + Id + ": Crabzilla lost before the blow (" + (nuke ? "killed" : "removed") + ")");
                WindDown(nuke);
                return true;
            }
            return true;
        }

        // --------------------------------------------------------------- gaze --

        private void Gaze(float dt)
        {
            if (Enter())
            {
                _pulse = 0.15f;
                Pose.Ground = _origin;
            }
            if (_rig.Present)
            {
                float dx = _crabRoot.x - Pose.Ground.x;
                if (Mathf.Abs(dx) > 0.3f)
                {
                    Pose.FaceRight = dx > 0f;
                }
            }
            // it looks up at the sky: a lean back, a slow breath
            float look = Anim.OutCubic(Mathf.Clamp01(_pt / 0.45f));
            Pose.Rot = -9f * look;
            Pose.SX = 1f - 0.015f * Mathf.Sin(_pt * 4f);
            Pose.SY = 1f + 0.025f * Mathf.Sin(_pt * 4f);
            Pose.Height = 0f;
            Vector2 feet = Pose.Ground;
            _pulse -= dt;
            if (_pulse <= 0f)
            {
                _pulse = 1.05f;
                if (Seen(feet))
                {
                    CineKit.Ring(feet, Px, 0.55f, 3f, 13f * K, 2, 1, Art.Ground, FxLayer.Ground, 6, true, 0f,
                        Fx.WithA(_light, 0.95f), Fx.WithA(_main, 0.6f), Fx.WithA(_main, 0f));
                    CineKit.Glint(Eye(), Px, 0.18f, _light, 0.7f, FxLayer.Top, true);
                }
            }
            if (!_tipped && Fighter.Now >= _tipAt)
            {
                _tipped = true;
                WorldTip.showNow(Locale.Format("sakuga_crab_gaze", A.getName()), false, "top", 2.6f, Tiers.Info(_tier).Hex);
            }
            if (Fighter.Now < _graceEnd || _pt < 1f)
            {
                return;
            }
            if (_wait < 0f)
            {
                _wait = _pt;
            }
            if (!_owns)
            {
                _owns = Cinema.TryBegin(this, 1f);
            }
            // another cinematic is on: wait for it a while, then go anyway, lightly
            if (_owns || _pt - _wait > 8f)
            {
                if (!_tipped)
                {
                    _tipped = true;
                    WorldTip.showNow(Locale.Format("sakuga_crab_gaze", A.getName()), false, "top", 2.4f, Tiers.Info(_tier).Hex);
                }
                Next(Phase.Summons);
            }
        }

        // ------------------------------------------------------------ summons --

        private void Summons(float dt)
        {
            float px = Px;
            Vector2 feet = Pose.Ground;
            if (Enter())
            {
                Recruit();
                Dev.Watch(BodyPos() + new Vector2(0f, 1.5f));
                _glow = CineKit.Copy(this, FxLayer.Over, 0, true);
                if (_owns)
                {
                    Title();
                }
                Sound("event:/SFX/POWERS/Powerup", feet);
                if (Seen(feet))
                {
                    // the ground answers: dust drawn in
                    Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                    int n = Fx.Count(8);
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i + Random.value * 0.6f) / n * Mathf.PI * 2f;
                        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                        Particle p = CineKit.C(Fx.Spawn(Art.Dust[0], feet + dir * 12f * px * K, px, 0.3f, dir.y < 0f ? FxLayer.Top : FxLayer.Ground, 5), true);
                        p?.Moving(-dir * 34f * px * K).Sized(0.7f, 0.3f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
                    }
                }
            }
            if (_pt < 0.25f)
            {
                // the power crouch, trembling
                float c = Anim.OutCubic(Mathf.Clamp01(_pt / 0.16f));
                Pose.SX = Mathf.Lerp(1f, 1.13f, c);
                Pose.SY = Mathf.Lerp(1f, 0.84f, c);
                Pose.Rot = Mathf.Lerp(-9f, 7f, c);
                Pose.Nudge = _pt > 0.12f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            else
            {
                if (Once(0))
                {
                    Aura(feet, px);
                }
                // stands tall, chin up to the sky: an overshooting spring out of the crouch
                float s = Anim.Spring(_pt - 0.25f, 7f, 16f);
                Pose.SX = 1f + 0.13f * s;
                Pose.SY = 1f - 0.16f * s;
                Pose.Rot = Mathf.Lerp(7f, -12f, Anim.OutCubic(Mathf.Clamp01((_pt - 0.25f) / 0.3f)));
                _lines -= dt;
                if (_lines <= 0f && _pt < 1f)
                {
                    _lines = 0.22f;
                    CineKit.FocusLines(BodyPos(), px, 9, 14f * K, 26f * K, 0.12f, Fx.WithA(_light, 0.75f), FxLayer.Top, true);
                }
                if (_pt >= 0.9f && Once(1))
                {
                    CineKit.Glint(Eye(), px, 0.24f, Hot, 1f, FxLayer.Top, true);
                }
            }
            Pose.Ground = _origin;
            Pose.Height = 0f;
            if (_pt >= TSummons)
            {
                Next(Phase.Liftoff);
            }
        }

        /// <summary>The aura pulse: a gold ring and a white flash; enemies close by are pushed away.</summary>
        private void Aura(Vector2 feet, float px)
        {
            _flashT = 0.12f;
            if (Seen(feet))
            {
                CineKit.Ring(feet, px, 0.45f, 4f, 26f * K, 3, 1, Art.Ground, FxLayer.Ground, 6, true, 0f,
                    Color.white, Fx.WithA(_main, 0.8f), Fx.WithA(_main, 0f));
                CineKit.Ring(feet, px, 0.6f, 3f, 18f * K, 1, 1, Art.Ground, FxLayer.Ground, 5, true, 0.07f,
                    Fx.WithA(_light, 0.8f), Fx.WithA(_light, 0f));
            }
            foreach (Actor u in Combat.FoesAround(A, feet, 2.6f * K))
            {
                Combat.Launch(u, feet, 2f, 0.7f, Random.value < 0.5f ? -540f : 540f);
            }
        }

        private void Title()
        {
            if (!Settings.TitleCards)
            {
                return;
            }
            Vector2 at = Fx.OnScreen(BodyPos(), 0.1f) ? BodyPos() : _bodyC;
            if (!Fx.OnScreen(at, 0.1f))
            {
                at = _crabRoot;
                if (!Fx.OnScreen(at, 0.1f))
                {
                    return;
                }
            }
            string key = !KaijuKiller.TitleShown ? "sakuga_title_crab_first"
                : _tier == Tier.God ? "sakuga_title_crab_god" : "sakuga_title_crab_gm";
            KaijuKiller.TitleShown = true;
            string text = Locale.Get(key);
            Cinema.Title(text, _main, 2.6f, at, CineKit.TitleSize(text, 1f));
        }

        // ------------------------------------------------------------ liftoff --

        private void Liftoff(float dt)
        {
            float px = Px;
            Vector2 feet = _origin;
            if (_pt < 0.15f)
            {
                // the squash before the jump
                float c = Anim.OutQuad(_pt / 0.15f);
                Pose.SX = Mathf.Lerp(1f, 1.24f, c);
                Pose.SY = Mathf.Lerp(1f, 0.72f, c);
                Pose.Rot = Mathf.Lerp(-12f, 0f, c);
                Pose.Ground = feet;
                Pose.Height = 0f;
                return;
            }
            if (Enter())
            {
                Pose.HideShadow = true;
                SpawnShadow();
                Sound("event:/SFX/UNIQUE/SpawnWhoosh", feet);
                if (Seen(feet))
                {
                    Takeoff(feet, px, A.current_tile);
                }
                if (_tier == Tier.God)
                {
                    Kit.Ravage(feet, 1f, 1, A);   // a God's launch scorches the ground it leaves
                }
            }
            float k = Mathf.Clamp01((_pt - 0.15f) / (TLiftoff - 0.15f));
            _shadowAt = SetFeet(this, feet + new Vector2(0f, Cruise * Anim.OutCubic(k)), feet);
            float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
            Pose.SX = Mathf.Lerp(0.64f, 1f, s);
            Pose.SY = Mathf.Lerp(1.6f, 1f, s);
            Pose.Rot = 0f;
            if (Seen(BodyPos()))
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.035f;
                    CineKit.C(Fx.Afterimage(this, 0.16f, Fx.WithA(_light, 0.85f), Fx.WithA(_main, 0.55f), Fx.WithA(_main, 0f)), true);
                }
                _streak -= dt;
                if (_streak <= 0f && k < 0.85f)
                {
                    // vertical gold speed lines streaming past
                    _streak = 0.025f;
                    Vector2 at = BodyPos() + new Vector2(Random.Range(-5f, 5f) * px, -Random.Range(2f, 12f) * px);
                    Particle p = CineKit.C(Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, 0.16f, FxLayer.Top, 1), true);
                    p?.Moving(new Vector2(0f, -30f * px)).Color(Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0f));
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Flight);
            }
        }

        private void Takeoff(Vector2 feet, float px, WorldTile tile)
        {
            CineKit.Ring(feet, px, 0.3f, 3f, 24f * K, 2, 1, Art.Ground, FxLayer.Ground, 6, true, 0f, Color.white, Fx.WithA(_main, 0.7f), Fx.WithA(_main, 0f));
            CineKit.DustRing(feet, px, tile, 12, 55f, 0.55f, K, true);
            Color soil = Fx.SoilColor(tile);
            CineKit.Debris(feet, 0f, px, 7, 1.6f * K, 5f * K, true, soil, Fx.Darken(soil, 0.3f), Fx.TopColor(tile));
            Kit.Crater(feet, px, tile, false, 3f, true);
            int lines = Fx.Count(7);
            for (int i = 0; i < lines; i++)
            {
                Vector2 at = feet + new Vector2(Random.Range(-4f, 4f), Random.Range(3f, 14f)) * px;
                Particle p = CineKit.C(Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, Random.Range(0.12f, 0.2f), FxLayer.Top, 1), true);
                p?.Moving(new Vector2(0f, 36f * px)).Color(Fx.WithA(_light, 0.9f), Fx.WithA(_main, 0f));
            }
        }

        // ------------------------------------------------------------- flight --

        private void Flight(float dt)
        {
            if (Enter())
            {
                _p0 = BodyPos();
                _g0 = _shadowAt;
                _lastBody = _p0;
                _acc = 0f;
                float dx = _bodyC.x - _origin.x;
                _side = Mathf.Abs(dx) > 0.5f ? (dx > 0f ? -1 : 1) : (Random.value < 0.5f ? -1 : 1);
                Sound("event:/SFX/UNIQUE/SpawnWhoosh", _bodyC);
            }
            float k = Mathf.Clamp01(_pt / TFlight);
            float e = Anim.InOutCubic(k);
            Vector2 target = StareBody(_side);
            Vector2 tg = Combat.Clamp(new Vector2(target.x, _groundY));
            Vector2 body = Vector2.LerpUnclamped(_p0, target, e) + new Vector2(0f, 3.5f * Mathf.Sin(Mathf.PI * k));
            Vector2 ground = Combat.Clamp(Vector2.Lerp(_g0, tg, e));
            float dirX = target.x - _p0.x;
            if (Mathf.Abs(dirX) > 0.3f)
            {
                Pose.FaceRight = dirX > 0f;
            }
            // the superman lean: flat out in the middle, a brake at the end
            float lean = 72f * Anim.Smooth(0f, 0.18f, k) * (1f - Anim.Smooth(0.78f, 0.97f, k));
            float brake = -18f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((k - 0.8f) / 0.2f));
            Pose.Rot = lean + brake;
            Pose.SX = 1f - 0.24f * lean / 72f;
            Pose.SY = 1f + 0.34f * lean / 72f;
            _shadowAt = SetFeet(this, new Vector2(body.x, Mathf.Max(ground.y, body.y - BodyH * Pose.SY)), ground);
            Vector2 now = BodyPos();
            Trail(now, dt);
            _lastBody = now;
            if (k >= 1f)
            {
                Next(Phase.Stare);
            }
        }

        /// <summary>The high gold streak: one continuous line, an afterimage every 1.5 tiles, speed lines.</summary>
        private void Trail(Vector2 now, float dt)
        {
            float px = Px;
            Vector2 d = now - _lastBody;
            float len = d.magnitude;
            if (len < 0.0001f)
            {
                return;
            }
            if (Seen(now, 0.6f) || Seen(_lastBody, 0.6f))
            {
                CineKit.Line(_lastBody, now, px, 0.32f, 2, FxLayer.Behind, 2, true, Fx.WithA(Color.white, 0.95f), Fx.WithA(_main, 0.85f), Fx.WithA(_main, 0f));
            }
            _acc += len;
            while (_acc >= 1.5f)
            {
                _acc -= 1.5f;
                Vector2 at = Vector2.Lerp(now, _lastBody, Mathf.Clamp01(_acc / len));
                if (Seen(at, 0.4f))
                {
                    Particle p = CineKit.C(Fx.Afterimage(this, 0.22f, Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0.45f), Fx.WithA(_main, 0f)), true);
                    if (p != null)
                    {
                        p.Pos += at - now;
                    }
                }
            }
            _lines -= dt;
            if (_lines <= 0f && Seen(now))
            {
                _lines = 0.05f;
                CineKit.SpeedLines(now, d, px, 3, 6f, 0.12f, Fx.WithA(Color.white, 0.6f), FxLayer.Top, true, 10f);
            }
        }

        // -------------------------------------------------------------- stare --

        private void Stare(float dt)
        {
            float px = Px;
            Vector2 s = StareBody(_side);
            if (Enter())
            {
                // from here on Crabzilla is ours: frozen in place, its laser off, untouchable by others
                _rig.Shell(out _bodyC, out _bodyExt);
                _groundY = Mathf.Max(0.5f, _crabRoot.y - 1f);
                s = StareBody(_side);
                _full = _owns && FullGate();
                if (_full)
                {
                    Cinema.Hold(this);
                    Cinema.Dim(0.5f, Color.black, 0.35f, true);
                }
                KaijuKiller.HoldCrab(CrabId);
                _rig.CutLaser();
                _rig.Outline(_bodyC, _bodyExt, _points, _limbs);
                _impact = Combat.Clamp(_crabRoot);
                WorldTile it = Combat.TileAt(_impact);
                _impactH = it != null && it.Type != null && (it.Type.liquid || it.Type.lava) ? 0.5f * K : 0f;
                Dev.Watch(_bodyC);
                CineKit.FocusLines(s, px, 16, 16f * K, 34f * K, 0.18f, Fx.WithA(Color.white, 0.8f), FxLayer.Top, true);
                _lines = 0.3f;
            }
            Pose.FaceRight = _bodyC.x > s.x;
            Pose.Rot = 5f + 2f * Mathf.Sin(_pt * 3f);
            Pose.SX = 1f;
            Pose.SY = 1f + 0.02f * Mathf.Sin(_pt * 3.2f);
            _shadowAt = Place(this, s + new Vector2(0f, 0.07f * K * Mathf.Sin(_pt * 3.2f)), _groundY);
            _lines -= dt;
            if (_lines <= 0f && _pt < 0.9f)
            {
                _lines = 0.3f;
                CineKit.FocusLines(BodyPos(), px, 10, 16f * K, 30f * K, 0.14f, Fx.WithA(Color.white, 0.65f), FxLayer.Top, true);
            }
            // it has noticed: one red flash through the whole shell
            if (_pt >= 0.3f && _pt < 0.68f)
            {
                float t = _pt - 0.3f;
                float amt = t < 0.06f ? t / 0.06f : t < 0.14f ? 1f : 1f - (t - 0.14f) / 0.24f;
                _rig.Tint(Notice, 0.75f * Mathf.Clamp01(amt), 1f);
                if (Once(0))
                {
                    Vector2 face = _bodyC + new Vector2(0f, _bodyExt.y * 0.55f);
                    CineKit.Flash(face, _rig.Px, 7, 0.22f, FxLayer.Top, 28, true, Fx.WithA(Notice, 0.55f), Fx.WithA(Notice, 0.3f), Fx.WithA(Notice, 0f));
                }
            }
            else if (_pt >= 0.68f)
            {
                _rig.Untint();
            }
            if (_pt >= 1.0f && Once(1))
            {
                CineKit.Glint(Eye(), px, 0.24f, Hot, 1.1f, FxLayer.Top, true);
            }
            if (_pt >= TStare)
            {
                _rig.Untint();
                Next(Phase.Heavens);
            }
        }

        /// <summary>A full cinematic only where it reads: setting on full, a sensible speed, Crabzilla in view.</summary>
        private bool FullGate()
        {
            if (Settings.Cinematics < 2 || World.world == null || Config.time_scale_asset == null || Config.time_scale_asset.multiplier > 5f)
            {
                return false;
            }
            Camera cam = World.world.camera;
            return cam != null && cam.orthographicSize <= 160f && (Fx.OnScreen(_bodyC, 0.15f) || Fx.OnScreen(BodyPos(), 0.1f));
        }

        // ------------------------------------------------------------ heavens --

        private void Heavens(float dt)
        {
            if (Enter())
            {
                _sh = new Striker { M = this, Body = BodyPos(), Rest = BodyPos(), FaceRight = Pose.FaceRight };
                if (_wing != null)
                {
                    _sw = new Striker { M = _wing, Body = _wing.BodyPos(), Rest = _wing.BodyPos(), FaceRight = _wing.Pose.FaceRight };
                }
                _next = 0;
                _lastHit = BodyPos();
            }
            while (_next < Strikes)
            {
                Striker st = StrikerFor(_next);
                if (st.Index != _next && _pt >= _times[_next] - _leads[_next])
                {
                    Appear(st, _next);
                }
                if (st.Index == _next && !st.Struck && _pt >= _times[_next])
                {
                    Contact(st, _next);
                    _next++;
                    continue;
                }
                break;
            }
            Strike(_sh);
            if (_sw != null)
            {
                Strike(_sw);
            }
            if (_pt >= THeavens && _next >= Strikes)
            {
                Next(Phase.Ascent);
            }
        }

        private Striker StrikerFor(int j)
        {
            return _sw != null && (j & 1) == 1 ? _sw : _sh;
        }

        /// <summary>A blink: gone from where it was, there at the next strike point in a wind-up.</summary>
        private void Appear(Striker st, int j)
        {
            Vector2 p = _points[j];
            Vector2 o = p - _bodyC;
            o = o.sqrMagnitude > 0.01f ? o.normalized : Vector2.up;
            Move m = st.M;
            float k = m.K;
            if (st.Index >= 0 && Seen(st.Body))
            {
                CineKit.C(Fx.Afterimage(m, 0.12f, Fx.WithA(_light, 0.7f), Fx.WithA(_main, 0f)), true);
            }
            st.From = p + o * 1.1f * k;
            st.Hit = p + o * 0.3f * k;
            st.Rest = st.Hit + o * 0.55f * k + new Vector2(0f, 0.2f * k);
            st.AppearAt = _pt;
            st.HitAt = _times[j];
            st.Index = j;
            st.Struck = false;
            float dx = p.x - st.From.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                st.FaceRight = dx > 0f;
            }
            // the shape of the blow follows its direction (o points from the kaiju to the striker)
            if (o.y > 0.7f)
            {
                st.Rot0 = -40f;   // from above: the heel comes down
                st.Rot1 = 105f;
            }
            else if (o.y < -0.7f)
            {
                st.Rot0 = 18f;    // from below: a rising kick
                st.Rot1 = -46f;
            }
            else
            {
                st.Rot0 = -28f;   // from the side: a lunge
                st.Rot1 = 34f;
            }
            if (Seen(st.From))
            {
                CineKit.Ring(st.From, KaijuGrain(m.Px), 0.1f, 2f, 7f * k, 1, 1, 1f, FxLayer.Top, 3, true, 0f, Fx.WithA(Color.white, 0.9f), Fx.WithA(_light, 0f));
            }
        }

        /// <summary>
        /// The pixel size the flurry's effects are drawn in: twice the striker's, up to the kaiju's own
        /// grain (a human's 0.1 -> 0.2 against Crabzilla's 0.25).
        /// </summary>
        private float KaijuGrain(float px)
        {
            return Mathf.Max(px, Mathf.Min(px * 2f, _rig.Px));
        }

        /// <summary>A blow lands on the kaiju.</summary>
        private void Contact(Striker st, int j)
        {
            st.Struck = true;
            Move m = st.M;
            float px = m.Px;
            // the blows land on a kaiju, in a view pulled back to take it all in: their stars, crescents
            // and sparks are drawn in a coarser grain, near the shell's (in the hero's own pixels they
            // were specks under the favourite star)
            float fpx = KaijuGrain(px);
            Vector2 p = _points[j];
            Vector2 o = p - _bodyC;
            o = o.sqrMagnitude > 0.01f ? o.normalized : Vector2.up;
            bool last = j == Strikes - 1;
            bool big = last || j == 7;
            // the wingman strikes in its own, paler light
            Color tint = m == this ? _main : _light;
            if (Seen(p))
            {
                CineKit.HitSpark(p, fpx, big ? 2 : 1 + (j & 1), o, big ? 10 : 6, tint, FxLayer.Top, true);
                // the crescent of the blow, swept across the strike line (about two tiles)
                Vector2 d = p - st.Hit;
                float th = Mathf.Round(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg / 22.5f) * 22.5f;
                int r = Mathf.Clamp(Mathf.RoundToInt((big ? 2.8f : 2f) / fpx), 6, 24);
                Sprite[] arc = (j & 1) == 0 ? Art.Arc(r, 1f, th + 80f, th - 80f, 3) : Art.Arc(r, 1f, th - 80f, th + 80f, 3);
                CineKit.Arc(arc, st.Hit, fpx, 0.14f, false, Color.white, tint, FxLayer.Top, 6, true);
                // red-orange shell, knocked off and falling to the ground
                float gy = Mathf.Min(_groundY, p.y);
                CineKit.Debris(new Vector2(p.x, gy), p.y - gy, _rig.Px, big ? 6 : 3, 2.5f, 4f, true, Shell);
                CineKit.Sparks(p, fpx, big ? 8 : 4, 60f, 120f, Orange, FxLayer.Top, true, 0.3f);
                if (j > 0)
                {
                    // the strike points joined by gold lines: a zig-zag drawn across the kaiju
                    CineKit.Line(_lastHit, p, px, 0.6f, 2, FxLayer.Top, 3, true, Fx.WithA(Color.white, 0.95f), Fx.WithA(tint, 0.9f), Fx.WithA(tint, 0.6f), Fx.WithA(tint, 0f));
                }
                if (big)
                {
                    CineKit.Ring(p, fpx, 0.22f, 4f, 22f, 2, 1, 1f, FxLayer.Top, 4, true, 0f, Color.white, Fx.WithA(_main, 0f));
                    Kit.Shake(p, 0.18f, 0.006f);
                }
            }
            if (st.M == this)
            {
                _flashT = 0.06f;
            }
            else if (Seen(st.Body))
            {
                CineKit.C(Fx.Afterimage(m, 0.08f, Fx.WithA(Color.white, 0.9f), Fx.WithA(_light, 0f)), true);
            }
            Sound((j & 3) == 3 ? "event:/SFX/HIT/HitStone" : "event:/SFX/OTHER/Punch", p);
            ScriptHp(j);
            StrikeScar(p, big);
            _lastHit = p;
            _hold = last ? 0.07f : StrikeStop;
        }

        /// <summary>
        /// Every blow on the kaiju shakes the ground under it: a scorch where its shell debris falls
        /// (in front of its feet, where it shows), and the two big blows (the face, the 8th and the
        /// 16th) tear the turf open.
        /// </summary>
        private void StrikeScar(Vector2 p, bool big)
        {
            float gx = Mathf.Clamp(p.x, _crabRoot.x - 7f, _crabRoot.x + 7f);
            Vector2 g = Combat.Clamp(new Vector2(gx, _groundY - Random.Range(-0.4f, 1.2f)));
            if (big)
            {
                Kit.Ravage(g, _tier == Tier.God ? 2f : 1f, 2, A);
            }
            else
            {
                Kit.Ravage(g, 1f, 1, A);
            }
        }

        /// <summary>Crabzilla's health, scripted: 70% at the 8th blow, 35% at the 16th, and the game's own limb damage.</summary>
        private void ScriptHp(int j)
        {
            if (!_rig.Standing)
            {
                return;
            }
            float pct = j < 8 ? 1f - 0.3f * (j + 1) / 8f : 0.7f - 0.35f * (j - 7) / 8f;
            int max = _crab.getMaxHealth();
            int target = Mathf.Max(2, Mathf.RoundToInt(max * pct));
            if (_crab.getHealth() > target)
            {
                KaijuKiller.OurHit = true;
                try
                {
                    // one point of real damage on top: the game's own hit runs (Crabzilla.getHit and its limbs)
                    _crab.setHealth(target + 1);
                    _crab.getHit(1f, false, AttackType.Other, A, false, false, false);
                }
                finally
                {
                    KaijuKiller.OurHit = false;
                }
            }
            if (_limbs[j] >= 0)
            {
                _rig.ShowDamage(_limbs[j]);
            }
            if (j == 7 || j == Strikes - 1)
            {
                _rig.ShowDamage(-1);
            }
            if (_limbs[j] >= 0)
            {
                _rig.HitFlicker(_limbs[j]);   // no-op if the line above already set it flickering
            }
        }

        /// <summary>A striker's pose: wind-up, the lunge into the blow, the recoil out of it.</summary>
        private void Strike(Striker st)
        {
            Move m = st.M;
            Vector2 body;
            float rot, sx, sy;
            if (st.Index < 0)
            {
                body = st.Rest + new Vector2(0f, 0.07f * m.K * Mathf.Sin(T * 3.2f));
                rot = 5f;
                sx = 1f;
                sy = 1f;
            }
            else if (!st.Struck)
            {
                float k = Mathf.Clamp01((_pt - st.AppearAt) / Mathf.Max(0.01f, st.HitAt - st.AppearAt));
                float e = Anim.OutQuad(k);
                body = Vector2.Lerp(st.From, st.Hit, e);
                rot = Mathf.Lerp(st.Rot0, st.Rot1, e);
                sx = Mathf.Lerp(0.92f, 0.8f, e);
                sy = Mathf.Lerp(1.1f, 1.28f, e);
            }
            else
            {
                float t = _pt - st.HitAt;
                float e = Anim.OutCubic(Mathf.Clamp01(t / 0.14f));
                body = Vector2.Lerp(st.Hit, st.Rest, e);
                rot = Mathf.Lerp(st.Rot1, 6f, e);
                float amp = Anim.Spring(t, 9f, 18f);
                sx = 1f - 0.2f * amp;
                sy = 1f + 0.28f * amp;
            }
            m.Pose.FaceRight = st.FaceRight;
            m.Pose.Rot = rot;
            m.Pose.SX = sx;
            m.Pose.SY = sy;
            m.Pose.Hidden = false;
            Vector2 shadow = Place(m, body, _groundY);
            if (m == this)
            {
                _shadowAt = shadow;
            }
            else
            {
                _wShadow = shadow;
            }
            st.Body = body;
        }

        // ------------------------------------------------------------- ascent --

        private void Ascent(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _p0 = BodyPos();
                _top = TopPoint();
                if (_full)
                {
                    Cinema.Dim(0.6f, Color.black, 0.5f, true);
                }
                Sound("event:/SFX/POWERS/Powerup", _top);
                Dev.Watch(Vector2.Lerp(_top, _impact, 0.5f));
            }
            float k = Mathf.Clamp01(_pt / 0.7f);
            Vector2 body;
            if (k < 1f)
            {
                float e = Anim.OutCubic(k);
                body = Vector2.Lerp(_p0, _top, e);
                Pose.Rot = 360f * Anim.InOutCubic(k);
                float tuck = Mathf.Sin(Mathf.PI * k);
                Pose.SX = 1f - 0.1f * tuck;
                Pose.SY = 1f + 0.12f * (1f - k) - 0.14f * tuck;
                if (Seen(BodyPos()))
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.035f;
                        CineKit.C(Fx.Afterimage(this, 0.14f, Fx.WithA(_light, 0.7f), Fx.WithA(_main, 0f)), true);
                    }
                }
            }
            else
            {
                body = _top + new Vector2(0f, 0.06f * K * Mathf.Sin(_pt * 4f));
                Pose.Rot = 0f;
                Pose.SX = 1f;
                Pose.SY = 1.04f;
                Pose.Nudge = _pt > 1.25f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            _shadowAt = Place(this, body, _groundY);
            if (_pt >= 0.3f && _target == null)
            {
                SpawnTarget();
            }
            if (_pt >= 0.45f && _disk == null)
            {
                SpawnDisk();
            }
            if (_pt >= 0.6f)
            {
                // embers drawn in from all around
                _ember -= dt;
                if (_ember <= 0f && Seen(body))
                {
                    _ember = 0.03f;
                    float a = Random.value * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    float r = Random.Range(3.5f, 5.5f) * K;
                    float life = Random.Range(0.35f, 0.5f);
                    Particle p = CineKit.Ember(body + dir * r, px, life, -dir * r / life, Random.value < 0.5f ? _main : Orange, FxLayer.Top, true, 0f);
                    p?.Color(Fx.WithA(_light, 0f), _main, Fx.WithA(Color.white, 0.9f));
                }
            }
            if (_pt >= 1.35f && Once(0))
            {
                CineKit.Glint(Eye(), px, 0.24f, Color.white, 1.2f, FxLayer.Top, true);
            }
            if (_pt >= TAscent)
            {
                Next(Phase.Dive);
            }
        }

        /// <summary>The top 15% of the view, above the kaiju (or high over it when the player looks elsewhere).</summary>
        private Vector2 TopPoint()
        {
            float lo = _bodyC.y + _bodyExt.y + 3.5f;
            float y = lo + 6f;
            Camera cam = World.world.camera;
            if (cam != null && Fx.OnScreen(_bodyC, 0.1f))
            {
                y = Mathf.Clamp(cam.transform.position.y + cam.orthographicSize * 0.7f, lo, lo + 24f);
            }
            return new Vector2(Mathf.Clamp(_bodyC.x, 1f, MapBox.width - 2f), y);
        }

        /// <summary>The gold disk swelling behind the hero (2 -> 4 tiles), a light in the sky over the trees.</summary>
        private void SpawnDisk()
        {
            float px = Px;
            _disk = CineKit.C(Fx.Spawn(Art.Disk(Mathf.RoundToInt(2f / px), 1f, 0.35f), BodyPos(), px, 1f, FxLayer.Top, -2), true);
            if (_disk == null)
            {
                return;
            }
            LiftAboveDisk();
            _disk.Held = true;
            float born = T;
            float span = Mathf.Max(0.1f, TAscent - _pt);
            _disk.Step = (q, d) =>
            {
                float k = Mathf.Clamp01((T - born) / span);
                float tiles = Mathf.Lerp(2f, 4f, Anim.OutQuad(k)) * (1f + 0.04f * Mathf.Sin(T * 22f));
                Sprite s = Art.Disk(Mathf.Clamp(Mathf.RoundToInt(tiles / q.Px), 1, 140), 1f, 0.35f);
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
                q.Pos = BodyPos();
                q.Grad = null;
                float a = Mathf.Lerp(0.4f, 0.78f, k) * (0.88f + 0.12f * Mathf.Sin(T * 30f));
                q.R.color = Fx.WithA(Color.Lerp(_main, _light, 0.3f + 0.2f * Mathf.Sin(T * 9f)), a);
            };
            // a white-hot sun at its heart: the hero hangs against it at the top of the sky
            _diskCore = CineKit.C(Fx.Spawn(Art.Disk(Mathf.RoundToInt(0.5f / px), 1f, 0.2f), BodyPos(), px, 1f, FxLayer.Top, -1), true);
            if (_diskCore == null)
            {
                return;
            }
            _diskCore.Held = true;
            _diskCore.Step = (q, d) =>
            {
                float k = Mathf.Clamp01((T - born) / span);
                float tiles = Mathf.Lerp(0.5f, 1.25f, Anim.OutQuad(k)) * (1f + 0.06f * Mathf.Sin(T * 31f));
                Sprite s = Art.Disk(Mathf.Clamp(Mathf.RoundToInt(tiles / q.Px), 1, 140), 1f, 0.2f);
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
                q.Pos = BodyPos();
                q.Grad = null;
                q.R.color = Fx.WithA(Color.Lerp(Hot, Color.white, 0.5f + 0.5f * Mathf.Sin(T * 17f)), Mathf.Lerp(0.3f, 0.75f, k));
            };
        }

        /// <summary>A red-gold target ring under Crabzilla, closing to 4 tiles.</summary>
        private void SpawnTarget()
        {
            float px = Px;
            _target = CineKit.C(Fx.Spawn(Art.Ring(Mathf.RoundToInt(6f / px), 1, Art.Ground, 12), _impact, px, 1f, FxLayer.Ground, 4), true);
            if (_target == null)
            {
                return;
            }
            _target.Held = true;
            _target.Snap = true;
            float born = T;
            _target.Step = (q, d) =>
            {
                float k = Anim.OutCubic(Mathf.Clamp01((T - born) / 0.45f));
                // it closes on the spot as the meteor comes down
                float tiles = _phase == Phase.Dive ? Mathf.Lerp(4f, 1.2f, Mathf.Clamp01((_pt - 0.25f) / (TDive - 0.25f))) : Mathf.Lerp(6f, 4f, k);
                Sprite s = Art.Ring(Mathf.Clamp(Mathf.RoundToInt(tiles / q.Px), 2, 140), 2, Art.Ground, 12);
                if ((object)q.R.sprite != s)
                {
                    q.R.sprite = s;
                    q.Frames[0] = s;
                }
                q.Pos = _impact;
                q.Grad = null;
                bool gold = ((int)(T * 8f) & 1) == 0;
                q.R.color = Fx.WithA(gold ? _main : Tiers.Crimson, 0.9f * Mathf.Clamp01(k * 2f));
            };
        }

        // --------------------------------------------------------------- dive --

        private void Dive(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _p0 = BodyPos();
                CineKit.Glint(Eye(), px, 0.2f, Hot, 1.3f, FxLayer.Top, true);
                StartWingDescent();
            }
            if (_pt < 0.25f)
            {
                // the recoil before the dive: up a little, gathered tight
                float c = Anim.OutQuad(_pt / 0.25f);
                Pose.SX = Mathf.Lerp(1f, 1.16f, c);
                Pose.SY = Mathf.Lerp(1.04f, 0.84f, c);
                Pose.Rot = Mathf.Lerp(0f, -18f, c);
                _shadowAt = Place(this, _p0 + new Vector2(0f, 0.6f * K * c), _groundY);
                return;
            }
            if (Once(0))
            {
                _top = BodyPos();
                _lastBody = _top;
                Sound("event:/SFX/DESTRUCTION/FallMeteorite", _impact);
                if (Seen(_top))
                {
                    // the sound barrier, and the disk bursting behind it
                    CineKit.Ring(_top, px, 0.22f, 4f, 22f * K, 2, 1, 0.35f, FxLayer.Top, 3, true, 0f, Color.white, Fx.WithA(_light, 0f));
                    CineKit.Ring(_top, px, 0.3f, Mathf.Round(3.6f / px), Mathf.Round(5.2f / px), 2, 1, 1f, FxLayer.Top, 3, true, 0f,
                        Fx.WithA(_light, 0.9f), Fx.WithA(_main, 0f));
                }
                _disk?.Kill();
                _diskCore?.Kill();
                _disk = _diskCore = null;
                // the wingman glides down to the ground on its own: drawn by the game again
                _litW?.Kill();
                _litW = null;
            }
            float k = Mathf.Clamp01((_pt - 0.25f) / (TDive - 0.25f));
            float e = k * k;
            float s = Mathf.Clamp01(k * 2.5f);
            Pose.SX = Mathf.Lerp(0.82f, 0.6f, s);
            Pose.SY = Mathf.Lerp(1.3f, 1.72f, s);
            // feet on the impact point at the end of the dive
            Vector2 target = new Vector2(_impact.x, _impact.y + _impactH + BodyH * Pose.SY);
            Vector2 body = Vector2.Lerp(_top, target, e);
            float dx = _impact.x - _top.x;
            Pose.Rot = Mathf.Clamp(Mathf.Abs(dx) * 4f, 6f, 28f);
            if (Mathf.Abs(dx) > 0.2f)
            {
                Pose.FaceRight = dx > 0f;
            }
            _shadowAt = Place(this, body, _impact.y);
            Vector2 now = BodyPos();
            if (Seen(now, 0.5f))
            {
                FireTrail(_lastBody, now, px * 2f);
                CineKit.C(Fx.Afterimage(this, 0.12f, Fx.WithA(Hot, 0.85f), Fx.WithA(_main, 0.6f), Fx.WithA(Tiers.Crimson, 0f)), true);
                CineKit.SpeedLines(now, now - _lastBody, px, 2, 6f, 0.1f, Fx.WithA(Hot, 0.75f), FxLayer.Top, true, 10f);
            }
            _lastBody = now;
            if (k >= 1f)
            {
                Next(Phase.Impact);
            }
        }

        /// <summary>A white-gold meteor's trail with a crimson tail, evenly spaced along the path.</summary>
        private void FireTrail(Vector2 from, Vector2 to, float px)
        {
            float step = 1.6f * px / Fx.Detail();
            float dist = Vector2.Distance(from, to);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 40);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle core = CineKit.C(Fx.Spawn(Art.Puffs[0], at, px, 0.09f, FxLayer.Behind, 3), true);
                core?.Sized(0.7f, 0.35f).Color(Hot, Fx.WithA(_light, 0.7f));
                Particle flame = CineKit.C(Fx.Spawn(Art.Puffs[Random.Range(1, 3)], at + Random.insideUnitCircle * 1.2f * px, px, Random.Range(0.16f, 0.24f), FxLayer.Behind, 2), true);
                flame?.Moving(Random.insideUnitCircle * 5f * px, 6f * px, 2f)
                    .Sized(0.75f, 0.45f)
                    .Color(Fx.WithA(_light, 0.95f), Fx.WithA(_main, 0.85f), Fx.WithA(Tiers.Crimson, 0.6f), Fx.WithA(Tiers.Crimson, 0f));
                if (Random.value < 0.15f)
                {
                    Particle smoke = CineKit.C(Fx.Spawn(Art.Dust[Random.Range(2, 5)], at, px, Random.Range(0.35f, 0.5f), FxLayer.Behind, 0), true);
                    smoke?.Moving(Random.insideUnitCircle * 4f * px, 8f * px, 1.5f).Sized(0.6f, 1.1f)
                        .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.3f), Fx.WithA(Smoke, 0f));
                }
            }
        }

        // ------------------------------------------------------------- impact --

        private void Impact(float dt)
        {
            if (Enter())
            {
                Pose.Ground = _impact;
                Pose.Height = _impactH;
                Pose.SX = 1.42f;
                Pose.SY = 0.6f;
                Pose.Rot = 6f;
                _target?.Kill();
                _target = null;
                DropLit();
                Crash();
            }
            // the longest hit-stop in the mod: crushed into the crater, trembling on the black frame;
            // then everything the blow did bursts out at once
            Pose.Ground = _impact;
            Pose.Height = _impactH;
            _shadowAt = _impact;
            Pose.Nudge = _pt < BlastAt ? new Vector2(0f, ((int)(_pt * 60f) & 1) == 0 ? 0.5f : 0f) : Vector2.zero;
            if (_pt >= BlastAt && Once(1))
            {
                Blast();
            }
            if (_pt >= TImpact)
            {
                Pose.Nudge = Vector2.zero;
                Next(Phase.Core);
            }
        }

        /// <summary>The held part of the impact: white frames, then the black flash on a frozen frame. The blow lands.</summary>
        private void Crash()
        {
            float px = Px;
            Vector2 at = _impact;
            WorldTile tile = Combat.TileAt(at) ?? A.current_tile;
            if (Seen(at, 0.4f))
            {
                CineKit.ViewFlash(Fx.WithA(Color.white, 0.85f), 0.04f);
                CineKit.ImpactFrame(at + new Vector2(0f, 0.5f), px, Mathf.Clamp(5.5f / (26f * px), 1f, 4f), _main, true, FxLayer.Top, true, BlastAt - 0.08f);
                // Crabzilla's grave, in its own pixel grain: gold fissures glowing for a minute
                CineKit.Crater(at, _rig.Px, tile, 60f, 60f, _main);
            }
            Tear(at);
            Kit.Stop(at, 0.6f);
            Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", at);
            KillCrab();
            _rig.Untint();
        }

        /// <summary>
        /// The Starbreaker tears the ground open for real, on the black frame: the game's own explosion
        /// (Kit.Ravage force 4 for a God, 3 for a Grandmaster), the turf torn down to bare earth, the
        /// ground set alight, trees and other kingdoms' buildings smashed. Whatever the hero now stands
        /// on (the ground may have given way to water), it stays above it; the wingman's landing spot
        /// moves off any fire.
        /// </summary>
        private void Tear(Vector2 at)
        {
            Kit.Ravage(at, _craterR, _tier == Tier.God ? 4 : 3, A);
            WorldTile now = Combat.TileAt(at);
            if (now != null && now.Type != null && (now.Type.liquid || now.Type.lava))
            {
                _impactH = Mathf.Max(_impactH, 0.5f * K);
            }
            if (_wing != null && !_wLanded && !Unburnt(_wLand, 1)
                && (ClearLanding(_wLand, _impact, _craterR + 3f, out Vector2 spot) || Combat.FindLanding(_impact, _craterR + 4f, out spot)))
            {
                _wLand = spot;
            }
        }

        /// <summary>When the hit-stop lets go: rings at 3, 6 and 9 tiles, the dust wall, a storm of shell, bystanders thrown flat.</summary>
        private void Blast()
        {
            float px = Px;
            Vector2 at = _impact;
            WorldTile tile = Combat.TileAt(at) ?? A.current_tile;
            Sound("event:/SFX/EXPLOSIONS/ExplosionBig", at);
            // the shock sears the ground out to the last ring: a scorched ring beyond the crater
            Kit.Ravage(at, _craterR * 1.5f, 1, A);
            if (Seen(at, 0.4f))
            {
                // the shock rings sweep over the husk (on the ground they would pass unseen under its shell)
                CineKit.Ring(at, px, 0.5f, 6f, Mathf.Round(3f / px), 3, 1, Art.Ground, FxLayer.Top, 6, true, 0f, Color.white, Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0f));
                CineKit.Ring(at, px, 0.62f, 8f, Mathf.Round(6f / px), 3, 1, Art.Ground, FxLayer.Top, 6, true, 0.08f, Color.white, Fx.WithA(_main, 0.7f), Fx.WithA(_main, 0f));
                CineKit.Ring(at, px, 0.78f, 10f, Mathf.Round(9f / px), 2, 1, Art.Ground, FxLayer.Top, 5, true, 0.16f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Tiers.Crimson, 0.6f), Fx.WithA(Tiers.Crimson, 0f));
                CineKit.DustRing(at, px, tile, 26, 110f, 0.9f, 1.6f, true);
                Color soil = Fx.SoilColor(tile);
                CineKit.Debris(at, 0f, px, 18, 4f, 7f, true, soil, Fx.Darken(soil, 0.3f), Fx.TopColor(tile));
                CineKit.Sparks(at + new Vector2(0f, 0.3f), px, 16, 110f, 180f, _main, FxLayer.Top, true, 0.4f);
                // a storm of shell: pieces of the kaiju thrown from all over its body
                float cpx = _rig.Px;
                int n = Fx.Count(28);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = _bodyC + new Vector2(Random.Range(-1f, 1f) * _bodyExt.x, Random.Range(-1f, 1f) * _bodyExt.y);
                    float gy = Mathf.Min(_groundY, p.y);
                    CineKit.Debris(new Vector2(p.x, gy), p.y - gy, cpx, 1, 5f, 6f, true, Shell);
                }
                int embers = Fx.Count(12);
                for (int i = 0; i < embers; i++)
                {
                    CineKit.Ember(at + Random.insideUnitCircle * 0.8f, px, Random.Range(0.8f, 1.5f), new Vector2(Random.Range(-1f, 1f), Random.Range(1.5f, 3.5f)), _main, FxLayer.Top, true);
                }
                Kit.Shake(at, 0.5f, 0.016f);
            }
            KnockDown(at);
        }

        /// <summary>The blow that ends it: through the game's own damage, so the kill is the hero's, and its nuke is ours.</summary>
        private void KillCrab()
        {
            if (!_rig.Standing)
            {
                return;
            }
            KaijuKiller.ContainCrab = CrabId;
            KaijuKiller.OurHit = true;
            try
            {
                int hp = _crab.getHealth();
                Combat.Hit(A, _crab, 0f, true, false, hp * 3f + 100f, this);
                if (_crab.hasHealth() && _crab.isAlive())
                {
                    _crab.getHit(_crab.getHealth() + 10f, false, AttackType.Weapon, A, false, false, false);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] kaiju blow: " + e);
            }
            finally
            {
                KaijuKiller.OurHit = false;
            }
            _killed = !_crab.hasHealth();
            // the shield stays on the corpse: any later hit (Esc's ControllableUnit.clear, a power, a soldier)
            // would clear its attackedBy before the game processes the death, and the kill would be no one's
            if (!_killed)
            {
                // something kept it alive (debug "ignore damage"): let it go, skip the nuke
                KaijuKiller.ReleaseCrab(CrabId, null);
            }
            if (Dev.On) Dev.Trace("kaiju " + Id + ": the blow " + (_killed ? "killed" : "did not kill") + " Crabzilla (hp " + _crab.getHealth() + ")");
        }

        /// <summary>Everyone within 9 tiles is knocked flat by the shockwave, unhurt.</summary>
        private void KnockDown(Vector2 at)
        {
            WorldTile tile = Combat.TileAt(at);
            if (tile == null)
            {
                return;
            }
            List<Actor> near = new List<Actor>(Finder.getUnitsFromChunk(tile, 2));
            near.Sort((x, y) => (x.current_position - at).sqrMagnitude.CompareTo((y.current_position - at).sqrMagnitude));
            Actor wing = _wing != null ? _wing.A : null;
            int held = 0;
            foreach (Actor u in near)
            {
                // (a unit in a light move, a flow strike, is knocked flat too: the puppet takes it over; one a
                // lethal blow already took is not: it would get back up at the landing, then drop dead)
                if (u == null || u == A || u == wing || u == _crab || !Combat.Living(u) || Moves.Locked(u) || u.isInsideSomething() || u.isInMagnet()
                    || u.asset.is_boat || u.isFlying() || u.position_height > 0.5f || u.is_immovable || ControllableUnit.isControllingUnit(u))
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, at);
                if (d > 9f + u.stats["size"] * 0.5f)
                {
                    break;   // sorted: the rest are further
                }
                float k = Mathf.Clamp01(d / 9f);
                if (!_full)
                {
                    Combat.Launch(u, at, Mathf.Lerp(1.6f, 0.5f, k), Mathf.Lerp(0.8f, 0.3f, k), Random.value < 0.5f ? -500f : 500f);
                    continue;
                }
                if (held >= 24 || !Combat.CanPuppet(u, A))
                {
                    continue;
                }
                Puppet p = new Puppet(u, at.x > u.current_position.x, this);
                if (!Moves.Start(p))
                {
                    continue;
                }
                held++;
                Vector2 away = (u.current_position - at).sqrMagnitude > 0.0001f ? (u.current_position - at).normalized : Random.insideUnitCircle.normalized;
                Vector2 to = u.current_position + away * Mathf.Lerp(0.9f, 0.25f, k);
                if (!Combat.SafeSpot(to))
                {
                    to = u.current_position;
                }
                _downed.Add(new Downed
                {
                    P = p,
                    U = u,
                    From = u.current_position,
                    To = Combat.Clamp(to),
                    Delay = d / 30f,
                    Apex = Mathf.Lerp(0.5f, 0.15f, k) * Mathf.Sqrt(Mathf.Clamp(u.actor_scale / 0.1f, 0.6f, 4f))
                });
            }
        }

        private void UpdateDowned(float dt)
        {
            for (int i = _downed.Count - 1; i >= 0; i--)
            {
                Downed d = _downed[i];
                if (d.P.Finished || d.U == null || !d.U.isAlive() || d.U.Sid() != d.P.Id)
                {
                    if (!d.P.Finished)
                    {
                        d.P.Release(d.P.Pose.Height <= 0.02f);
                    }
                    _downed.RemoveAt(i);
                    continue;
                }
                d.T += dt;
                if (_standUp && d.UpAt < 0f)
                {
                    d.UpAt = d.T + Random.Range(0f, 0.25f);
                }
                Pose p = d.P.Pose;
                float t = d.T - d.Delay;
                float lie = 0f;
                if (t < 0f)
                {
                    p.Ground = d.From;
                    p.Height = 0f;
                }
                else if (t < 0.32f)
                {
                    float k = t / 0.32f;
                    p.Ground = Vector2.Lerp(d.From, d.To, Anim.OutQuad(k));
                    p.Height = d.Apex * Mathf.Sin(Mathf.PI * k);
                    lie = Anim.OutQuad(k);
                }
                else
                {
                    float b = t - 0.32f;
                    p.Ground = d.To;
                    p.Height = d.Apex * 0.2f * Mathf.Abs(Mathf.Sin(b * 14f)) * Mathf.Exp(-9f * b);
                    lie = 1f;
                }
                if (d.UpAt >= 0f && d.T >= d.UpAt)
                {
                    float u = Mathf.Clamp01((d.T - d.UpAt) / 0.35f);
                    lie *= 1f - Anim.OutCubic(u);
                    p.Height = 0.12f * Mathf.Sin(Mathf.PI * u);
                }
                // flat on its back, away from the blast (lowered so it lies on the ground)
                p.Rot = -86f * lie;
                p.Nudge = new Vector2(0f, -Mathf.Max(0f, p.PivotUp - 2.5f) * lie);
                p.HideShadow = lie > 0.5f;
                d.P.Pose = p;
            }
        }

        private void ReleaseDowned()
        {
            foreach (Downed d in _downed)
            {
                if (d.P != null && !d.P.Finished)
                {
                    Pose p = d.P.Pose;
                    p.Rot = 0f;
                    p.Height = 0f;
                    p.Nudge = Vector2.zero;
                    d.P.Pose = p;
                    d.P.Release(true);
                }
            }
            _downed.Clear();
        }

        // --------------------------------------------------------------- core --

        private void Core(float dt)
        {
            float px = Px;
            if (Enter())
            {
                if (_killed)
                {
                    StartOrb();
                }
                Dev.Watch(_bodyC);
            }
            // up out of the crater, eyes on the core
            float amp = Anim.Spring(_pt, 7f, 15f);
            Pose.SX = 1f + 0.42f * amp;
            Pose.SY = 1f - 0.4f * amp;
            Pose.Rot = -14f * Anim.Smooth(0.15f, 0.45f, _pt);
            Pose.Ground = _impact;
            Pose.Height = _impactH;
            _shadowAt = _impact;
            if (_killed && _rig.Present)
            {
                // the burnt husk crumbles away from around its core
                float k = Mathf.Clamp01((_pt - 0.1f) / 0.65f);
                _rig.Tint(Husk, Mathf.Lerp(0.55f, 0.8f, k), 1f - k);
                if (k >= 1f)
                {
                    // nothing of it is left: its shadow and anything else the tint can't fade goes too
                    // (the corpse waits for the world to move again before the game removes it)
                    _rig.Hide();
                }
                _ember -= dt;
                if (_ember <= 0f && k < 1f)
                {
                    _ember = 0.05f;
                    Vector2 p = _points[Random.Range(0, Strikes)];
                    float gy = Mathf.Min(_groundY, p.y);
                    CineKit.Debris(new Vector2(p.x, gy), p.y - gy, _rig.Px, 1, 1.5f, 1.5f, true, Fx.Darken(Shell[0], 0.45f), Fx.Darken(Shell[1], 0.5f), Husk);
                }
            }
            if (_pt >= TCore)
            {
                if (_killed)
                {
                    _rig.Hide();
                }
                Next(_killed ? Phase.Bloom : Phase.Descent);
            }
        }

        private void StartOrb()
        {
            float px = Px;
            _orb = Orb.Swell;
            _orbT = 0f;
            _orbPos = _bodyC + new Vector2(0f, _bodyExt.y * 0.12f);
            _orbR = 0.5f;
            _orbRim = CineKit.C(Fx.Spawn(Art.Disk(Mathf.RoundToInt(0.7f / px), 1f, 0.3f), _orbPos, px, 1f, FxLayer.Top, 26), true);
            _orbCore = CineKit.C(Fx.Spawn(Art.Disk(Mathf.RoundToInt(0.4f / px), 1f, 0.2f), _orbPos, px, 1f, FxLayer.Top, 27), true);
            if (_orbRim != null)
            {
                _orbRim.Held = true;
            }
            if (_orbCore != null)
            {
                _orbCore.Held = true;
            }
            if (Seen(_orbPos))
            {
                CineKit.Flash(_orbPos, px, Mathf.RoundToInt(1.4f / px), 0.16f, FxLayer.Top, 28, true, Color.white, Fx.WithA(Hot, 0.6f), Fx.WithA(Hot, 0f));
            }
        }

        /// <summary>The core: swelling and wobbling where the husk was, then rocketing up, then gone.</summary>
        private void UpdateOrb(float dt)
        {
            if (_orb == Orb.None || _orb == Orb.Gone)
            {
                return;
            }
            float px = Px;
            _orbT += dt;
            if (_orb == Orb.Swell)
            {
                float k = Mathf.Clamp01(_orbT / 0.8f);
                float wob = 1f + 0.1f * (0.6f * Mathf.Sin(_orbT * 26f) + 0.4f * Mathf.Sin(_orbT * 41f));
                _orbR = Mathf.Lerp(0.5f, 2f, Anim.OutQuad(k)) * wob;
                _smoke -= dt;
                if (_smoke <= 0f && Seen(_orbPos))
                {
                    _smoke = 0.06f;
                    CineKit.Sparks(_orbPos, px, 3, 50f, 110f, Orange, FxLayer.Top, true);
                    _lines -= 0.06f;
                    if (_lines <= 0f)
                    {
                        _lines = 0.18f;
                        CineKit.FocusLines(_orbPos, px, 10, _orbR / px + 8f, _orbR / px + 22f, 0.14f, Fx.WithA(Color.white, 0.7f), FxLayer.Top, true);
                    }
                }
            }
            else
            {
                float k = Mathf.Clamp01(_orbT / 0.78f);
                _orbPos = Vector2.Lerp(_orbFrom, _orbTo, k * k);
                _orbR = Mathf.Lerp(2f, 0.5f, k);
                if (Seen(_orbPos, 0.6f))
                {
                    OrbTrail(_orbLast, _orbPos, px, dt);
                }
                _orbLast = _orbPos;
                if (k >= 1f)
                {
                    _orb = Orb.Gone;
                    _orbRim?.Kill();
                    _orbCore?.Kill();
                    _orbRim = _orbCore = null;
                    return;
                }
            }
            SetOrb(_orbRim, _orbR + 1.5f * px, Color.Lerp(_main, Orange, 0.5f + 0.5f * Mathf.Sin(T * 40f)), 0.9f);
            SetOrb(_orbCore, _orbR * 0.72f, Color.Lerp(Color.white, Hot, 0.5f + 0.5f * Mathf.Sin(T * 33f)), 1f);
        }

        private void SetOrb(Particle p, float tiles, Color c, float a)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Sprite s = Art.Disk(Mathf.Clamp(Mathf.RoundToInt(tiles / p.Px), 1, 140), 1f, 0.3f);
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = _orbPos;
            p.Grad = null;
            p.R.color = Fx.WithA(c, a);
        }

        /// <summary>Fire behind the rising core and a column of smoke that stays.</summary>
        private void OrbTrail(Vector2 from, Vector2 to, float px, float dt)
        {
            float fpx = px * 1.6f;
            float step = 1.6f * fpx / Fx.Detail();
            int n = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(from, to) / step), 1, 30);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(from, to, (i + 1f) / n) + Random.insideUnitCircle * _orbR * 0.4f;
                Particle f = CineKit.C(Fx.Spawn(Art.Puffs[Random.Range(1, 4)], at, fpx, Random.Range(0.2f, 0.3f), FxLayer.Behind, 2), true);
                f?.Moving(Random.insideUnitCircle * 4f * px, 0f, 2f).Sized(0.8f, 0.5f)
                    .Color(Hot, Fx.WithA(_main, 0.9f), Fx.WithA(Orange, 0.7f), Fx.WithA(Tiers.Crimson, 0f));
            }
            _smoke -= dt;
            if (_smoke <= 0f)
            {
                _smoke = 0.03f;
                Particle s = CineKit.C(Fx.Spawn(Art.Dust[Random.Range(3, 6)], to + Random.insideUnitCircle * 0.3f, fpx, Random.Range(2f, 2.6f), FxLayer.Behind, 0), true);
                s?.Moving(new Vector2(Random.Range(-0.15f, 0.15f), 0.25f), 0f, 0.4f).Sized(0.7f, 1.7f)
                    .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.34f), Fx.WithA(Smoke, 0.22f), Fx.WithA(Smoke, 0f));
            }
        }

        // -------------------------------------------------------------- bloom --

        private void Bloom(float dt)
        {
            float px = Px;
            Vector2 under = _orbPos - new Vector2(0f, _orbR + BodyH * 0.9f + 0.4f * K);
            if (Enter())
            {
                // the blink: gone from the crater, crouched under the core
                if (Seen(BodyPos()))
                {
                    CineKit.C(Fx.Afterimage(this, 0.14f, Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0f)), true);
                }
                _p0 = under;
                Sound("event:/SFX/UNIQUE/SpawnWhoosh", _orbPos);
                if (Seen(_p0))
                {
                    CineKit.Ring(_p0, px, 0.12f, 2f, 8f * K, 1, 1, 1f, FxLayer.Top, 3, true, 0f, Color.white, Fx.WithA(_light, 0f));
                }
            }
            if (_pt < 0.08f)
            {
                Pose.SX = 1.16f;
                Pose.SY = 0.82f;
                Pose.Rot = 12f;
                _shadowAt = Place(this, _p0, _groundY);
            }
            else
            {
                float k = Mathf.Clamp01((_pt - 0.08f) / 0.08f);
                float e = Anim.OutCubic(k);
                // the rising kick, then the follow-through drifting up a little
                float drift = 0.25f * K * Anim.OutQuad(Mathf.Clamp01((_pt - 0.16f) / 0.6f));
                _shadowAt = Place(this, _p0 + new Vector2(0f, 0.5f * K * e + drift), _groundY);
                Pose.Rot = Mathf.Lerp(12f, -48f, e) + 10f * Anim.Smooth(0.2f, 0.8f, _pt);
                Pose.SX = Mathf.Lerp(1.16f, 0.84f, e);
                Pose.SY = Mathf.Lerp(0.82f, 1.3f, e) - 0.08f * Anim.Smooth(0.2f, 0.8f, _pt);
                if (Once(0))
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(3f / px), 10, 60);
                    CineKit.Arc(Art.Arc(r, 0.9f, -70f, 100f, 4), BodyPos() + new Vector2((Pose.FaceRight ? 0.4f : -0.4f) * K, 0.6f * K), px, 0.2f,
                        !Pose.FaceRight, Color.white, _main, FxLayer.Top, 6, true);
                }
                if (_orb == Orb.Swell && _pt >= 0.14f)
                {
                    Kick();
                }
            }
            if (_pt >= TBloom)
            {
                _p0 = BodyPos();
                Next(Phase.Hold);
            }
        }

        /// <summary>The kick lands: the core leaves for the sky.</summary>
        private void Kick()
        {
            float px = Px;
            _orb = Orb.Rise;
            _orbT = 0f;
            _orbFrom = _orbPos;
            _orbLast = _orbPos;
            float exit = _orbFrom.y + 16f;
            Camera cam = World.world.camera;
            if (cam != null && Fx.OnScreen(_orbFrom, 0.1f))
            {
                exit = cam.transform.position.y + cam.orthographicSize + 3f;
            }
            _orbTo = new Vector2(_orbFrom.x, Mathf.Max(exit, _orbFrom.y + 8f));
            _flashT = 0.08f;
            _hold = 0.06f;
            if (Seen(_orbPos))
            {
                CineKit.ImpactFrame(_orbPos, px, 1.3f, _main, false, FxLayer.Top, true);
                CineKit.Ring(_orbPos, px, 0.24f, Mathf.Round(_orbR / px), Mathf.Round(_orbR / px) + 26f, 2, 1, 1f, FxLayer.Top, 4, true, 0f,
                    Color.white, Fx.WithA(_main, 0f));
                CineKit.Sparks(_orbPos, px, 14, 90f, 170f, _main, FxLayer.Top, true, 0.5f);
                Kit.Shake(_orbPos, 0.2f, 0.008f);
            }
            Sound("event:/SFX/OTHER/Punch", _orbPos);
            Sound("event:/SFX/HIT/HitGeneric", _orbPos);
        }

        // --------------------------------------------------------------- hold --

        private void HoldPose(float dt)
        {
            // the kick held, stretched to the sky. Silence
            Pose.Rot = -38f + 2f * Mathf.Sin(_pt * 2.5f);
            Pose.SX = 0.9f;
            Pose.SY = 1.16f;
            _shadowAt = Place(this, _p0 + new Vector2(0f, 0.05f * K * Mathf.Sin(_pt * 2.2f)), _groundY);
            if (_pt >= THold)
            {
                Burst();
                Next(Phase.Descent);
            }
        }

        /// <summary>
        /// High in the sky the core goes off: a nuclear airburst. The whole view whites out, a white-hot
        /// fireball swells and cools through gold, orange and crimson into smoke, shockwave rings race out
        /// across the sky (one flat, like a condensation ring), a column of fire and smoke climbs into a
        /// spreading cap and embers rain down. No terrain damage, no fire crabs: it all stays in the sky.
        /// </summary>
        private void Burst()
        {
            float px = Mathf.Max(Px, _rig.Px * 0.8f);
            Vector2 sky = new Vector2(_orbTo.x, _orbFrom.y + 12f);
            Camera cam = World.world.camera;
            if (cam != null && Fx.OnScreen(_orbFrom, 0.15f))
            {
                sky.y = cam.transform.position.y + cam.orthographicSize * 0.62f;
            }
            sky.y = Mathf.Max(sky.y, _orbFrom.y + 6f);
            _burstAt = sky;
            Sound("event:/SFX/EXPLOSIONS/ExplosionHuge", _impact);
            Sound("event:/SFX/EXPLOSIONS/ExplosionAntimatterBomb", _impact);
            Kit.Shake(sky, 1.4f, 1.8f);
            if (!Seen(sky, 0.6f))
            {
                return;
            }
            Airburst(sky, px, _full ? FxLayer.Cine : FxLayer.Top, _light);
        }

        /// <summary>The airburst itself (also the dev "airburst" command): at 'sky', drawn at pixel size 'px'.</summary>
        internal static void Airburst(Vector2 sky, float px, FxLayer layer, Color light)
        {
            // the flash: the whole view goes white, then the light of it lingers
            CineKit.ViewFlash(Fx.WithA(Color.white, 0.85f), 0.28f);
            CineKit.ViewFlash(Fx.WithA(Hot, 0.4f), 1.4f);
            // the core: a white-hot star, a blinding disk around it
            Blob(sky, 2.2f, px, 0.7f, 0f, Vector2.zero, 0.6f, 1.6f, layer, 40, Color.white, Color.white, Hot, Fx.WithA(Hot, 0f));
            CineKit.Burst(sky, px * 2.4f, 0.4f, layer, 41, true, Color.white, Color.white, Fx.WithA(Hot, 0.9f), Fx.WithA(light, 0f));
            // the fireball: overlapping soft clouds swelling and cooling, white -> gold -> orange -> crimson -> smoke,
            // drifting up as they cool
            for (int i = 0; i < 9; i++)
            {
                Vector2 at = sky + Random.insideUnitCircle * 1.1f;
                Vector2 rise = new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(0.35f, 0.8f));
                Blob(at, Random.Range(1.4f, 2.1f), px, Random.Range(2.6f, 3.6f), i * 0.04f, rise, 0.5f, Random.Range(2.4f, 3.1f), layer, 30,
                    Color.white, Hot, Color.Lerp(Hot, Orange, 0.6f), Orange, Fx.WithA(Tiers.Crimson, 0.9f), Fx.WithA(Smoke, 0.8f), Fx.WithA(Smoke, 0f));
            }
            // the shockwaves: a white ring racing out across the sky, a hot one behind it, a flat condensation ring
            CineKit.Ring(sky, px, 1.2f, 2f, Mathf.Round(22f / px), 7, 2, 1f, layer, 8, true, 0f, Color.white, Fx.WithA(Hot, 0.95f), Fx.WithA(light, 0.6f), Fx.WithA(light, 0f));
            CineKit.Ring(sky, px, 1.6f, 2f, Mathf.Round(14f / px), 6, 2, 1f, layer, 8, true, 0.12f, Fx.WithA(Hot, 0.95f), Fx.WithA(Orange, 0.7f), Fx.WithA(Orange, 0f));
            CineKit.Ring(sky, px, 2.2f, 3f, Mathf.Round(18f / px), 5, 1, 0.28f, layer, 7, true, 0.35f, Fx.WithA(Color.white, 0.85f), Fx.WithA(Color.white, 0.35f), Fx.WithA(Color.white, 0f));
            // the mushroom: a stem of fire and smoke climbing out of the fireball, a cap rolling out at its top
            int stem = Fx.Count(16);
            for (int i = 0; i < stem; i++)
            {
                float k = i / (float)Mathf.Max(1, stem - 1);
                Vector2 at = sky + new Vector2(Random.Range(-0.5f, 0.5f), -0.6f);
                Blob(at, Random.Range(0.9f, 1.3f), px, Random.Range(3.2f, 4.2f), 0.4f + k * 1.1f, new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(2.2f, 3f)),
                    0.7f, 1.6f, layer, 24, Hot, Orange, Fx.WithA(Tiers.Crimson, 0.9f), Fx.WithA(Smoke, 0.85f), Fx.WithA(Smoke, 0.5f), Fx.WithA(Smoke, 0f));
            }
            int cap = Fx.Count(18);
            for (int i = 0; i < cap; i++)
            {
                float a = i / (float)Mathf.Max(1, cap) * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.4f);
                Vector2 at = sky + new Vector2(0f, 4.8f) + dir * 0.6f;
                Blob(at, Random.Range(1.2f, 1.7f), px, Random.Range(3.4f, 4.4f), 1.2f + Random.Range(0f, 0.5f), dir * Random.Range(1.1f, 1.8f) + new Vector2(0f, 0.6f),
                    0.6f, 2.2f, layer, 26, Orange, Fx.WithA(Tiers.Crimson, 0.95f), Fx.WithA(Smoke, 0.9f), Fx.WithA(Smoke, 0.6f), Fx.WithA(Smoke, 0f));
            }
            // embers: burning fragments flung out and drifting down
            Peony(sky, px * 1.6f, Fx.Count(36), 8f, 0.05f, Orange, layer);
            Peony(sky, px * 1.6f, Fx.Count(20), 4.6f, 0.2f, Hot, layer);
        }

        /// <summary>One soft cloud of the airburst: 'r' tiles across at size 1, drawn in this unit's pixel grain.</summary>
        private static void Blob(Vector2 at, float r, float px, float life, float delay, Vector2 vel, float s0, float s1, FxLayer layer, int order, params Color[] colors)
        {
            Particle b = CineKit.C(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(r / px), 3, 140), 1f, 0.18f), at, px, life, layer, order), true);
            if (b == null)
            {
                return;
            }
            b.Delay = delay;
            b.Moving(vel, 0f, 0.35f, 0f);
            b.Sized(s0, s1);
            b.Color(colors);
        }

        /// <summary>One shell of the fireworks: 'n' streaks flung out at 'speed' tiles/s in an even ring.</summary>
        private static void Peony(Vector2 sky, float px, int n, float speed, float delay, Color c, FxLayer layer)
        {
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + (i + Random.Range(-0.3f, 0.3f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = CineKit.C(Fx.Spawn(Art.Sparks[Random.Range(0, 2)], sky + dir * 0.3f, px, Random.Range(1.9f, 2.7f), layer, 9), true);
                if (p == null)
                {
                    break;
                }
                p.Delay = delay + Random.Range(0f, 0.04f);
                p.Moving(dir * speed * Random.Range(0.88f, 1.08f), 0f, 1.7f, 1.3f);
                p.FaceVel = true;
                p.Color(Color.white, Color.Lerp(c, Color.white, 0.35f), c, Fx.WithA(c, 0.85f), Fx.WithA(Color.Lerp(c, Tiers.Crimson, 0.6f), 0.6f), Fx.WithA(Tiers.Crimson, 0f));
            }
        }

        // ------------------------------------------------------------ descent --

        private void Descent(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _p0 = BodyPos();
                _g0 = _shadowAt;
                ChooseLanding();
                if (_homeBlink)
                {
                    if (Seen(_p0))
                    {
                        CineKit.C(Fx.Afterimage(this, 0.14f, Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0f)), true);
                    }
                    _g0 = _land;
                    _p0 = _land + new Vector2(0f, 3f * K + BodyH);
                }
                // a float at 2.5 tiles/s, kept within the beat (a very high kick floats down faster)
                float fall = Mathf.Max(0f, _p0.y - BodyH - _land.y);
                _dur = Mathf.Clamp(fall / 2.5f, 1.2f, 2.4f);
                Dev.Watch(_burstAt.HasValue ? Vector2.Lerp(_land + new Vector2(0f, 2f), _burstAt.Value, 0.4f) : _land + new Vector2(0f, 2f));
            }
            Pose.Hidden = _homeBlink && _pt < 0.1f;
            float k = Mathf.Clamp01(_pt / _dur);
            float e = Anim.InOutSine(k);
            Vector2 feet0 = _p0 - new Vector2(0f, BodyH);
            float x = Mathf.Lerp(feet0.x, _land.x, e);
            _shadowAt = SetFeet(this, new Vector2(x, Mathf.Lerp(feet0.y, _land.y, e)), new Vector2(x, Mathf.Lerp(_g0.y, _land.y, e)));
            // one flip on the way down, a sway as it floats
            float fk = Mathf.Clamp01((k - 0.3f) / 0.4f);
            Pose.Rot = Mathf.Lerp(-38f, 0f, Anim.OutCubic(Mathf.Clamp01(k / 0.25f))) + 360f * Anim.InOutCubic(fk)
                + 4f * Mathf.Sin(_pt * 3.2f) * (1f - k);
            float tuck = Mathf.Sin(Mathf.PI * fk);
            Pose.SX = 1f - 0.06f * tuck;
            Pose.SY = Mathf.Lerp(1.16f, 1f, Mathf.Clamp01(k / 0.25f)) - 0.12f * tuck;
            if (fk > 0f && fk < 1f && Seen(BodyPos()))
            {
                _ghost -= dt;
                if (_ghost <= 0f)
                {
                    _ghost = 0.05f;
                    CineKit.C(Fx.Afterimage(this, 0.12f, Fx.WithA(_light, 0.45f), Fx.WithA(_main, 0f)), true);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Land);
            }
        }

        /// <summary>
        /// The crater's rim on the hero's side (solid ground, off the fires; the other side when the
        /// wingman took this one), or home if there is none.
        /// </summary>
        private void ChooseLanding()
        {
            _homeBlink = false;
            int side = _p0.x >= _impact.x ? 1 : -1;
            if (_wing != null && (_wLand.x >= _impact.x ? 1 : -1) == side)
            {
                side = -side;
            }
            Vector2 want = Combat.Clamp(_impact + new Vector2(side * Rim(K), -0.4f));
            if (ClearLanding(want, _impact, _craterR + 3f, out _land) || Combat.FindLanding(_impact, 8f, out _land))
            {
                return;
            }
            _land = _origin;
            _homeBlink = true;
        }

        /// <summary>
        /// A landing spot clear of the flames the Starbreaker lit around its crater (burning ground is
        /// safe to stand on, but a hero landing among the fire sprites can't be seen): solid ground
        /// with no fire on it or beside it, looked for outward from 'want' (away from 'from'), then
        /// around it; else any unburnt safe tile; else any safe tile (Combat.FindLanding).
        /// </summary>
        private static bool ClearLanding(Vector2 want, Vector2 from, float reach, out Vector2 spot)
        {
            want = Combat.Clamp(want);
            Vector2 dir = want - from;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
            for (int pass = 0; pass < 2; pass++)
            {
                int margin = pass == 0 ? 1 : 0;
                for (float d = 0f; d <= reach; d += 0.75f)
                {
                    for (int f = 0; f < 3; f++)
                    {
                        float a = f == 0 ? 0f : f == 1 ? 0.3f : -0.3f;
                        Vector2 r = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
                        Vector2 p = Combat.Clamp(want + r * d);
                        if (Unburnt(p, margin))
                        {
                            spot = p;
                            return true;
                        }
                    }
                }
            }
            return Combat.FindLanding(want, reach, out spot);
        }

        /// <summary>Safe ground with no fire within 'margin' tiles.</summary>
        private static bool Unburnt(Vector2 p, int margin)
        {
            if (!Combat.SafeSpot(p))
            {
                return false;
            }
            WorldTile t = Combat.TileAt(p);
            if (t == null)
            {
                return false;
            }
            for (int dx = -margin; dx <= margin; dx++)
            {
                for (int dy = -margin; dy <= margin; dy++)
                {
                    int x = t.x + dx, y = t.y + dy;
                    if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height)
                    {
                        continue;
                    }
                    WorldTile w = World.world.GetTileSimple(x, y);
                    if (w != null && w.isOnFire())
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>How far from the impact the crater's rim is (where its torn ground thins out).</summary>
        private float Rim(float k)
        {
            return Mathf.Max(1.8f * k, _craterR * 0.7f);
        }

        // --------------------------------------------------------------- land --

        private void Land(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Ground = _land;
                Pose.Height = 0f;
                _shadowAt = _land;
                Pose.Rot = 0f;
                Pose.HideShadow = false;
                _shadow?.Kill();
                _shadow = null;
                if (_owns)
                {
                    Cinema.Undim(0.8f);
                }
                CineKit.LandingPuff(_land, px, Combat.TileAt(_land), true);
                // the strong land gently: back to the crater (a crab turns to it and bows)
                bool right = _land.x >= _impact.x;
                Pose.FaceRight = _crabHero ? !right : right;
                _standUp = true;
            }
            Pose.Ground = _land;
            Pose.Height = 0f;
            float amp = Anim.Spring(_pt, 8f, 16f);
            Pose.SX = 1f + 0.16f * amp;
            Pose.SY = 1f - 0.18f * amp;
            Pose.Rot = 0f;
            if (_crabHero)
            {
                // two slow bows, squashed to 70%
                float b = 0f;
                if (_pt >= 0.25f && _pt < 0.7f)
                {
                    b = Mathf.Sin(Mathf.PI * (_pt - 0.25f) / 0.45f);
                }
                else if (_pt >= 0.78f && _pt < 1.23f)
                {
                    b = Mathf.Sin(Mathf.PI * (_pt - 0.78f) / 0.45f);
                }
                Pose.SY *= 1f - 0.3f * b;
                Pose.SX *= 1f + 0.12f * b;
                Pose.Rot = 10f * b;
                if (_pt >= 1.35f)
                {
                    Finished = true;
                }
                return;
            }
            // a crisp landing, one glint, and the world moves again (no standing about)
            if (_pt >= 0.26f && Once(0))
            {
                CineKit.Glint(HeadPos() + new Vector2((Pose.FaceRight ? 2f : -2f) * px, 0f), px, 0.26f, Hot, 1f, FxLayer.Top, true);
            }
            if (_pt >= 0.6f)
            {
                Finished = true;
            }
        }

        // --------------------------------------------------------------- exit --

        /// <summary>Crabzilla was lost before the blow: let go of the world at once and get down (clear of its nuke).</summary>
        private void WindDown(bool nuke)
        {
            if (_phase == Phase.Exit)
            {
                return;
            }
            if (_phase == Phase.Gaze)
            {
                Finished = true;
                return;
            }
            ReleaseWorld();
            KillHeld();
            ReleaseDowned();
            _hold = 0f;
            _fleeing = nuke && Vector2.Distance(_shadowAt, _crabRoot) < 36f;
            Next(Phase.Exit);
        }

        private void Exit(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _p0 = BodyPos();
                _g0 = _shadowAt;
                _homeBlink = false;
                if (_fleeing && FleeSpot(out _land))
                {
                    _homeBlink = true;
                }
                else if (!Combat.FindLanding(_shadowAt, 8f, out _land))
                {
                    _land = _origin;
                    _homeBlink = true;
                }
                if (_homeBlink)
                {
                    if (Seen(_p0))
                    {
                        CineKit.C(Fx.Afterimage(this, 0.14f, Fx.WithA(_light, 0.8f), Fx.WithA(_main, 0f)), true);
                    }
                    _p0 = _land + new Vector2(0f, 2.5f * K + BodyH);
                    _g0 = _land;
                }
                _dur = _homeBlink ? 0.5f : Mathf.Clamp((_p0.y - BodyH - _land.y) / 6f, 0.35f, 1.2f);
                LandWingNow();
                Pose.HideShadow = true;
            }
            float t = _pt - (_homeBlink ? 0.12f : 0f);
            Pose.Hidden = t < 0f;
            if (t < 0f)
            {
                return;
            }
            float k = Mathf.Clamp01(t / _dur);
            float e = Anim.InOutSine(k);
            Vector2 feet0 = _p0 - new Vector2(0f, BodyH);
            float x = Mathf.Lerp(feet0.x, _land.x, e);
            _shadowAt = SetFeet(this, new Vector2(x, Mathf.Lerp(feet0.y, _land.y, e)), new Vector2(x, Mathf.Lerp(_g0.y, _land.y, e)));
            Pose.Rot = Mathf.Lerp(Pose.Rot, 0f, Mathf.Clamp01(dt * 10f));
            Pose.SX = Pose.SY = 1f;
            if (k >= 1f)
            {
                Pose.Ground = _land;
                Pose.Height = 0f;
                Pose.HideShadow = false;
                CineKit.LandingPuff(_land, px, Combat.TileAt(_land), true);
                Finished = true;
            }
        }

        /// <summary>A safe spot 36+ tiles from Crabzilla (its nuke reaches 30), away from it first.</summary>
        private bool FleeSpot(out Vector2 spot)
        {
            Vector2 away = _shadowAt - _crabRoot;
            float a0 = away.sqrMagnitude > 0.01f ? Mathf.Atan2(away.y, away.x) : Random.value * Mathf.PI * 2f;
            for (int i = 0; i < 12; i++)
            {
                float a = a0 + ((i & 1) == 0 ? 1f : -1f) * ((i + 1) / 2) * Mathf.PI / 6f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float d = 36f; d <= 48f; d += 6f)
                {
                    Vector2 p = _crabRoot + dir * d;
                    if (p.x < 2f || p.y < 2f || p.x > MapBox.width - 3 || p.y > MapBox.height - 3)
                    {
                        continue;
                    }
                    if (Combat.FindLanding(p, 4f, out spot))
                    {
                        return true;
                    }
                }
            }
            spot = _shadowAt;
            return false;
        }

        // --------------------------------------------------------------- wing --

        /// <summary>A second God joins (if one is free right now): it flies with the hero and takes every other blow.</summary>
        private void Recruit()
        {
            if (_tier != Tier.God)
            {
                return;
            }
            Actor w = KaijuKiller.PickHero(_crab, A, true, out _);
            if (w == null || !Moves.CanPerform(w))
            {
                return;
            }
            KaijuWing m = new KaijuWing(w);
            if (!Moves.Start(m))
            {
                return;
            }
            _wing = m;
            _wingActor = w;
            _wingId = m.Id;
            _wOrigin = w.current_position;
            _wShadow = _wOrigin;
            float dx = _crabRoot.x - _wOrigin.x;
            if (Mathf.Abs(dx) > 0.3f)
            {
                m.Pose.FaceRight = dx > 0f;
            }
            if (Dev.On) Dev.Trace("kaiju " + Id + ": " + w.getName() + " joins as the wingman");
        }

        /// <summary>The wingman's choreography, phase by phase alongside the hero (cues 16+).</summary>
        private void Wing(float dt)
        {
            KaijuWing w = _wing;
            if (w == null)
            {
                return;
            }
            float px = w.Px;
            switch (_phase)
            {
                case Phase.Summons:
                    {
                        // the same power crouch, a beat behind
                        if (_pt < 0.25f)
                        {
                            float c = Anim.OutCubic(Mathf.Clamp01(_pt / 0.16f));
                            w.Pose.SX = Mathf.Lerp(1f, 1.12f, c);
                            w.Pose.SY = Mathf.Lerp(1f, 0.85f, c);
                            w.Pose.Rot = 6f * c;
                        }
                        else
                        {
                            float s = Anim.Spring(_pt - 0.25f, 7f, 16f);
                            w.Pose.SX = 1f + 0.12f * s;
                            w.Pose.SY = 1f - 0.15f * s;
                            w.Pose.Rot = Mathf.Lerp(6f, -10f, Anim.OutCubic(Mathf.Clamp01((_pt - 0.25f) / 0.3f)));
                            if (Once(16) && Seen(_wOrigin))
                            {
                                CineKit.Ring(_wOrigin, px, 0.45f, 4f, 22f * w.K, 2, 1, Art.Ground, FxLayer.Ground, 6, true, 0f, Color.white, Fx.WithA(_light, 0f));
                            }
                        }
                        w.Pose.Ground = _wOrigin;
                        w.Pose.Height = 0f;
                        break;
                    }
                case Phase.Liftoff:
                    {
                        float t = _pt - 0.08f;
                        w.Pose.Ground = _wOrigin;
                        if (t < 0.15f)
                        {
                            float c = Anim.OutQuad(Mathf.Clamp01(t / 0.15f));
                            w.Pose.SX = Mathf.Lerp(1f, 1.22f, c);
                            w.Pose.SY = Mathf.Lerp(1f, 0.74f, c);
                            w.Pose.Rot = 0f;
                            w.Pose.Height = 0f;
                            break;
                        }
                        if (Once(16))
                        {
                            w.Pose.HideShadow = true;
                            if (Seen(_wOrigin))
                            {
                                CineKit.Ring(_wOrigin, px, 0.3f, 3f, 20f * w.K, 2, 1, Art.Ground, FxLayer.Ground, 6, true, 0f, Color.white, Fx.WithA(_light, 0f));
                                CineKit.DustRing(_wOrigin, px, w.A.current_tile, 10, 50f, 0.5f, w.K, true);
                            }
                            Kit.Ravage(_wOrigin, 1f, 1, w.A);
                        }
                        float k = Mathf.Clamp01((t - 0.15f) / (TLiftoff - 0.23f));
                        _wShadow = SetFeet(w, _wOrigin + new Vector2(0f, Cruise * Anim.OutCubic(k)), _wOrigin);
                        float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
                        w.Pose.SX = Mathf.Lerp(0.66f, 1f, s);
                        w.Pose.SY = Mathf.Lerp(1.55f, 1f, s);
                        w.Pose.Rot = 0f;
                        if (Seen(w.BodyPos()) && ((int)(_pt * 30f) & 1) == 0)
                        {
                            CineKit.C(Fx.Afterimage(w, 0.15f, Fx.WithA(Color.white, 0.75f), Fx.WithA(_light, 0f)), true);
                        }
                        break;
                    }
                case Phase.Flight:
                    {
                        if (Once(16))
                        {
                            _wF0 = w.BodyPos();
                            _wG0 = _wShadow;
                            _wLast = _wF0;
                        }
                        float k = Mathf.Clamp01(_pt / TFlight);
                        float e = Anim.InOutCubic(k);
                        Vector2 target = StareBody(-_side) + new Vector2(0f, -0.6f * w.K);
                        Vector2 body = Vector2.LerpUnclamped(_wF0, target, e) + new Vector2(0f, 2.5f * Mathf.Sin(Mathf.PI * k));
                        Vector2 ground = Combat.Clamp(Vector2.Lerp(_wG0, new Vector2(target.x, _groundY), e));
                        if (Mathf.Abs(target.x - _wF0.x) > 0.3f)
                        {
                            w.Pose.FaceRight = target.x > _wF0.x;
                        }
                        float lean = 70f * Anim.Smooth(0f, 0.2f, k) * (1f - Anim.Smooth(0.78f, 0.97f, k));
                        w.Pose.Rot = lean;
                        w.Pose.SX = 1f - 0.22f * lean / 70f;
                        w.Pose.SY = 1f + 0.3f * lean / 70f;
                        _wShadow = SetFeet(w, new Vector2(body.x, Mathf.Max(ground.y, body.y - w.Pose.PivotUp * w.A.actor_scale * w.Pose.SY)), ground);
                        Vector2 now = w.BodyPos();
                        if ((now - _wLast).sqrMagnitude > 0.0001f && (Seen(now, 0.6f) || Seen(_wLast, 0.6f)))
                        {
                            // its own streak, a paler gold beside the hero's
                            CineKit.Line(_wLast, now, px, 0.28f, 1, FxLayer.Behind, 2, true, Fx.WithA(Color.white, 0.9f), Fx.WithA(_light, 0.7f), Fx.WithA(_light, 0f));
                        }
                        _wLast = now;
                        break;
                    }
                case Phase.Stare:
                    {
                        Vector2 s = StareBody(-_side) + new Vector2(0f, -0.6f * w.K + 0.07f * w.K * Mathf.Sin(_pt * 3.4f + 1f));
                        w.Pose.FaceRight = _bodyC.x > s.x;
                        w.Pose.Rot = 5f;
                        w.Pose.SX = w.Pose.SY = 1f;
                        _wShadow = Place(w, s, _groundY);
                        break;
                    }
                case Phase.Heavens:
                    break;   // the strikers drive it
                case Phase.Ascent:
                    {
                        if (Once(16))
                        {
                            _wF0 = w.BodyPos();
                        }
                        float k = Mathf.Clamp01(_pt / 0.75f);
                        Vector2 spot = _top + new Vector2(-_side * 3.2f * w.K, -1.4f * w.K);
                        Vector2 body = k < 1f ? Vector2.Lerp(_wF0, spot, Anim.OutCubic(k)) : spot + new Vector2(0f, 0.06f * w.K * Mathf.Sin(_pt * 4f + 2f));
                        w.Pose.Rot = k < 1f ? -360f * Anim.InOutCubic(k) : 0f;
                        w.Pose.SX = 1f;
                        w.Pose.SY = 1f;
                        w.Pose.FaceRight = _top.x > spot.x;
                        _wShadow = Place(w, body, _groundY);
                        break;
                    }
                default:
                    WingDescent(dt);
                    break;
            }
        }

        private void StartWingDescent()
        {
            if (_wing == null)
            {
                return;
            }
            _wT = 0f;
            _wLanded = false;
            _wL0 = _wing.BodyPos();
            _wLG0 = _wShadow;
            int side = _wL0.x >= _impact.x ? 1 : -1;
            Vector2 want = Combat.Clamp(_impact + new Vector2(side * Rim(_wing.K), 0.5f));
            if (!Combat.FindLanding(want, 3f, out _wLand) && !Combat.FindLanding(_impact, 8f, out _wLand))
            {
                _wLand = _wOrigin;
            }
        }

        /// <summary>The wingman glides down to the far rim while the hero dives, and waits there, back to the crater.</summary>
        private void WingDescent(float dt)
        {
            KaijuWing w = _wing;
            if (_wLanded)
            {
                w.Pose.Ground = _wLand;
                w.Pose.Height = 0f;
                float amp = Anim.Spring(_wT, 8f, 16f);
                _wT += dt;
                w.Pose.SX = 1f + 0.14f * amp;
                w.Pose.SY = 1f - 0.16f * amp;
                w.Pose.Rot = 0f;
                return;
            }
            _wT += dt;
            float k = Mathf.Clamp01(_wT / 2.4f);
            float e = Anim.InOutSine(k);
            Vector2 feet0 = _wL0 - new Vector2(0f, w.Pose.PivotUp * w.A.actor_scale);
            float x = Mathf.Lerp(feet0.x, _wLand.x, e);
            _wShadow = SetFeet(w, new Vector2(x, Mathf.Lerp(feet0.y, _wLand.y, e)), new Vector2(x, Mathf.Lerp(_wLG0.y, _wLand.y, e)));
            float fk = Mathf.Clamp01((k - 0.35f) / 0.35f);
            w.Pose.Rot = 360f * Anim.InOutCubic(fk);
            w.Pose.SX = 1f;
            w.Pose.SY = 1f - 0.1f * Mathf.Sin(Mathf.PI * fk);
            if (k >= 1f)
            {
                _wLanded = true;
                _wT = 0f;
                w.Pose.Rot = 0f;
                w.Pose.HideShadow = false;
                w.Pose.FaceRight = _wLand.x >= _impact.x;
                CineKit.LandingPuff(_wLand, w.Px, Combat.TileAt(_wLand), true);
            }
        }

        /// <summary>Puts the wingman down at once (the cinematic is ending early).</summary>
        private void LandWingNow()
        {
            KaijuWing w = _wing;
            if (w == null || w.Finished)
            {
                _wing = null;
                return;
            }
            if (w.Pose.Height > 0.05f)
            {
                if (Seen(w.BodyPos()))
                {
                    CineKit.C(Fx.Afterimage(w, 0.14f, Fx.WithA(Color.white, 0.8f), Fx.WithA(_light, 0f)), true);
                }
                Vector2 near = _phase == Phase.Exit ? _land + new Vector2(-1.2f, 0f) : _wShadow;
                if (!Combat.FindLanding(near, 4f, out Vector2 spot) && !Combat.FindLanding(_wOrigin, 4f, out spot))
                {
                    spot = _wOrigin;
                }
                w.Pose.Ground = spot;
                w.Pose.Height = 0f;
            }
            w.Pose.Rot = 0f;
            w.Pose.SX = w.Pose.SY = 1f;
            w.Pose.Hidden = false;
            w.Pose.HideShadow = false;
            w.Done(true);
            _wing = null;
            _sw = null;
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            CineKit.StandStill(this);
            if (_lit != null && _lit.Alive)
            {
                Fx.Glue(_lit, this, Color.white);
            }
            if (_litW != null)
            {
                if (_wing != null && !_wing.Finished && _wing.Valid)
                {
                    Fx.Glue(_litW, _wing, Color.white);
                }
                else
                {
                    _litW.Kill();   // the wingman was let go: no copy left hanging in the sky
                    _litW = null;
                }
            }
            if (_glow == null || !_glow.Alive)
            {
                return;
            }
            Fx.Glue(_glow, this, GlowColor());
        }

        /// <summary>
        /// The Ascent's gold disk is a light in the sky, drawn over everything on the ground (trees,
        /// buildings): the hero (and the wingman beside it) are drawn again above it, lit, and the
        /// hero's glow above that.
        /// </summary>
        private void LiftAboveDisk()
        {
            if (_lit == null)
            {
                _lit = CineKit.Copy(this, FxLayer.Top, 1, false);
            }
            if (_litW == null && _wing != null && !_wing.Finished)
            {
                _litW = CineKit.Copy(_wing, FxLayer.Top, 1, false);
            }
            if (_glow != null && !_glowTop)
            {
                _glow.Kill();
                _glow = CineKit.Copy(this, FxLayer.Top, 2, true);
                _glowTop = true;
            }
        }

        private void DropLit()
        {
            _lit?.Kill();
            _litW?.Kill();
            _lit = _litW = null;
        }

        /// <summary>The hero's own light through the beats.</summary>
        private Color GlowColor()
        {
            Color c;
            switch (_phase)
            {
                case Phase.Summons:
                    c = Fx.WithA(_main, _pt < 0.25f ? 0.3f * _pt / 0.25f : 0.22f + 0.18f * Mathf.Sin(_pt * 18f));
                    break;
                case Phase.Liftoff:
                case Phase.Flight:
                    c = Fx.WithA(_light, 0.3f);
                    break;
                case Phase.Stare:
                case Phase.Heavens:
                    c = Fx.WithA(_main, 0.12f + 0.05f * Mathf.Sin(T * 6f));
                    break;
                case Phase.Ascent:
                    c = Fx.WithA(_main, Mathf.Lerp(0.15f, 0.6f, Mathf.Clamp01(_pt / TAscent)) * (0.85f + 0.15f * Mathf.Sin(_pt * 40f)));
                    break;
                case Phase.Dive:
                    c = Fx.WithA(Color.Lerp(_light, Hot, Mathf.Clamp01(_pt / TDive)), 0.85f);
                    break;
                case Phase.Impact:
                    c = _pt < 0.06f ? Fx.WithA(Color.white, 0.95f) : Fx.WithA(Hot, 0.7f);
                    break;
                case Phase.Core:
                    c = Color.Lerp(Fx.WithA(_main, 0.6f), Fx.WithA(_main, 0f), Mathf.Clamp01(_pt / 0.5f));
                    break;
                case Phase.Bloom:
                case Phase.Hold:
                    c = Fx.WithA(_main, _phase == Phase.Bloom ? 0.25f : Mathf.Lerp(0.25f, 0f, Mathf.Clamp01(_pt / 0.8f)));
                    break;
                default:
                    c = Fx.WithA(_main, 0f);
                    break;
            }
            if (_flashT > 0f)
            {
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_flashT / 0.06f));
                c.a = Mathf.Max(c.a, 0.85f * Mathf.Clamp01(_flashT / 0.06f));
            }
            return c;
        }

        /// <summary>Our own shadow while airborne: it stays on the ground and shrinks to a dot at cruise height.</summary>
        private void SpawnShadow()
        {
            if (_shadow != null)
            {
                return;
            }
            _shadow = CineKit.C(Fx.Spawn(Art.Disk(ShadowRx(1f), 0.42f, 0.45f), Pose.Ground, Px, 1f, FxLayer.Ground, 3), true);
            if (_shadow != null)
            {
                _shadow.Held = true;
                _shadow.Snap = true;
                _shadow.Color(new Color(0f, 0f, 0f, 0.36f));
            }
        }

        private void UpdateShadow()
        {
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            // on the real ground, however high the hero is anchored
            float h = Mathf.Clamp01((Pose.Ground.y + Pose.Height - _shadowAt.y) / Cruise);
            Sprite s = Art.Disk(ShadowRx(Mathf.Lerp(1f, 0.1f, Mathf.Sqrt(h))), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != s)
            {
                _shadow.R.sprite = s;
                _shadow.Frames[0] = s;
            }
            _shadow.Pos = _shadowAt;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, Pose.Hidden ? 0f : Mathf.Lerp(0.36f, 0.2f, h));
        }

        private int ShadowRx(float k)
        {
            return Mathf.Max(1, Mathf.RoundToInt(3.2f * k * (Pose.PivotUp / 3.5f)));
        }

        // ------------------------------------------------------------ cleanup --

        /// <summary>
        /// Lifts the freeze and the vignette, and lets Crabzilla go if it still stands (back into the
        /// player's hands, inputs fresh, if the player had let go of it meanwhile).
        /// </summary>
        private void ReleaseWorld()
        {
            Cinema.End(this);
            _owns = false;
            if (!_killed)
            {
                _rig.Untint();
                _rig.FinishFlicker();
                KaijuKiller.ReleaseCrab(CrabId, _rig.Standing && _phase >= Phase.Stare ? _crab : null);
            }
            // killed: it stays held and shielded until the game removes it (its update would read the
            // player's input for a frame; a stray hit would steal the hero's kill)
        }

        private void KillHeld()
        {
            _disk?.Kill();
            _diskCore?.Kill();
            _target?.Kill();
            _disk = _diskCore = _target = null;
            DropLit();
            if (_orb == Orb.Swell || _orb == Orb.Rise)
            {
                // the core never reached the sky: it fizzles out harmlessly
                if (Seen(_orbPos))
                {
                    CineKit.Ring(_orbPos, Px, 0.4f, Mathf.Round(_orbR / Px), 2f, 2, 1, 1f, FxLayer.Top, 4, true, 0f, Fx.WithA(_light, 0.9f), Fx.WithA(_main, 0f));
                    CineKit.Sparks(_orbPos, Px, 10, 40f, 90f, _main, FxLayer.Top, true);
                }
                _orb = Orb.Gone;
            }
            _orbRim?.Kill();
            _orbCore?.Kill();
            _orbRim = _orbCore = null;
        }

        private void Cleanup()
        {
            if (_cleaned)
            {
                return;
            }
            _cleaned = true;
            Ended = true;
            ReleaseWorld();
            KillHeld();
            _shadow?.Kill();
            _glow?.Kill();
            _shadow = _glow = null;
            ReleaseDowned();
            LandWingNow();
        }

        public override void Abort()
        {
            LetGo(this, _shadowAt, _origin);
            Cleanup();
        }

        /// <summary>
        /// A performer let go in mid-air (killed, picked up, its move failed) is over its real ground,
        /// not over its anchor (SetFeet: up to Cruise tiles north of it): a body drops onto its shadow
        /// from where it was drawn; a living one the framework sets down stands on safe ground near
        /// its shadow ('home' if there is none).
        /// </summary>
        internal static void LetGo(Move m, Vector2 shadow, Vector2 home)
        {
            Actor a = m.A;
            if (a == null || a.Sid() != m.Id)
            {
                return;
            }
            shadow = Combat.Clamp(shadow);
            if (!a.isAlive())
            {
                float h = a.current_position.y + a.position_height - shadow.y;
                if (h > 0.02f)
                {
                    a.current_position = shadow;
                    a.position_height = h;   // (the framework's Tumble.Fall drops it from there)
                    a.dirty_current_tile = true;
                }
                return;
            }
            if (m.Pose.Height > 0.05f || (m.Pose.Ground - shadow).sqrMagnitude > 0.01f)
            {
                m.Pose.Ground = Combat.FindLanding(shadow, 8f, out Vector2 spot) ? spot : home;
                m.Pose.Height = 0f;
            }
        }

        /// <summary>
        /// Is this a Kaiju Killer performer (the hero or its wingman), and where is its real ground? It
        /// is drawn over an anchor under its feet, far from its shadow while it is high in the sky.
        /// </summary>
        internal static bool Anchored(Move m, out Vector2 shadow)
        {
            switch (m)
            {
                case KaijuKillerMove k:
                    shadow = k._shadowAt;
                    return true;
                case KaijuWing w:
                    shadow = w.Shadow;
                    return true;
                default:
                    shadow = default;
                    return false;
            }
        }

        public override void OnEnd(bool aborted)
        {
            bool done = !aborted && _killed && _phase == Phase.Land;
            Cleanup();
            if (done)
            {
                Aftermath();
            }
            KaijuKiller.Ended(this, _killed);
        }

        /// <summary>The world hears of it; the hero (and its wingman) earn Kaiju Slayer and a star.</summary>
        private void Aftermath()
        {
            try
            {
                WorldTip.showNow(Locale.Format("sakuga_crab_slain", A.getName(), Tiers.Name(_tier)), false, "top", 4f, Tiers.Info(_tier).Hex);
                Honour(A);
                if (_wingActor != null && _wingActor.Sid() == _wingId && Combat.Living(_wingActor))
                {
                    Honour(_wingActor);   // the wingman shares the honour (the same unit: actors are pooled)
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] kaiju aftermath: " + e);
            }
        }

        private static void Honour(Actor a)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            ActorTrait t = KaijuKiller.Slayer;
            if (t != null && !a.hasTrait(t))
            {
                a.addTrait(t);
            }
            a.data.favorite = true;
        }
    }

    /// <summary>The second God, flown by the hero's move (it only holds the pose it is given).</summary>
    internal sealed class KaijuWing : Move
    {
        /// <summary>The real ground under it (kept by the hero's move), wherever it is anchored.</summary>
        internal Vector2 Shadow;

        public KaijuWing(Actor a) : base(a)
        {
            Shadow = a.current_position;
        }

        public override bool Cinematic => true;

        public override bool Intangible => true;

        public override void Tick(float dt)
        {
        }

        public override void Abort()
        {
            KaijuKillerMove.LetGo(this, Shadow, Shadow);
        }

        public override void Frame()
        {
            CineKit.StandStill(this);
        }

        /// <summary>Hands the unit back ('onGround': it stands where it was put).</summary>
        public void Done(bool onGround)
        {
            LandOnFinish = onGround;
            Finished = true;
        }
    }

    /// <summary>
    /// A live Crabzilla's parts. It is a prefab of many sprites driven by its own component, not a unit
    /// sprite, so silhouettes can't copy it: this reaches the parts directly to read its outline (for
    /// the strike points), cut its laser, tint it, and run its own limb-damage flicker while it is held
    /// (the flicker normally advances inside Crabzilla.update, which the hold skips).
    /// </summary>
    internal sealed class CrabRig
    {
        public readonly Actor Crab;
        public readonly long Id;
        public readonly Crabzilla Cz;
        /// <summary>World units per pixel of Crabzilla's own sprites (its shell debris is drawn at this size).</summary>
        public readonly float Px;

        private readonly CrabLimbItem[] _items;
        private SpriteRenderer[] _parts;
        private Color[] _colors;
        private bool _tinted;

        public CrabRig(Actor crab)
        {
            Crab = crab;
            Id = crab.Sid();
            Px = Mathf.Clamp(crab.actor_scale, 0.04f, 0.4f);
            try
            {
                Cz = crab.getActorComponent<Crabzilla>();
                _items = Cz != null ? Cz.GetComponentsInChildren<CrabLimbItem>(true) : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab rig: " + e);
                Cz = null;
            }
        }

        /// <summary>The same Crabzilla, not removed.</summary>
        public bool Present => Crab != null && Crab.Sid() == Id && Crab.isAlive() && Cz != null;

        /// <summary>Present and not dead.</summary>
        public bool Standing => Present && Crab.hasHealth();

        /// <summary>Killed (no health) but its death not yet processed: its nuke is on the way.</summary>
        public bool Dying => Crab != null && Crab.Sid() == Id && !Crab.hasHealth();

        public Vector2 Root => Crab.current_position;

        /// <summary>The shell on screen: centre and half size (from its body sprites, else a guess).</summary>
        public void Shell(out Vector2 center, out Vector2 ext)
        {
            center = Root + new Vector2(0f, 7f);
            ext = new Vector2(6.5f, 6.4f);
            if (Cz == null)
            {
                return;
            }
            bool any = false;
            Bounds b = default;
            if (_items != null)
            {
                foreach (CrabLimbItem item in _items)
                {
                    SpriteRenderer r = Renderer(item);
                    if (r == null || item.crabLimb != CrabLimb.MainBody)
                    {
                        continue;
                    }
                    if (!any)
                    {
                        b = r.bounds;
                        any = true;
                    }
                    else
                    {
                        b.Encapsulate(r.bounds);
                    }
                }
            }
            if (any && b.extents.x > 0.5f && b.extents.y > 0.5f)
            {
                center = b.center;
                ext = b.extents;
            }
            else if (Cz.mainBody != null)
            {
                center = Cz.mainBody.transform.position;
            }
        }

        private static SpriteRenderer Renderer(CrabLimbItem item)
        {
            if (item == null || !item.gameObject.activeInHierarchy)
            {
                return null;
            }
            SpriteRenderer r = item._sprite_renderer != null ? item._sprite_renderer : item.GetComponent<SpriteRenderer>();
            return r != null && r.enabled && r.sprite != null ? r : null;
        }

        /// <summary>
        /// Sixteen strike points around the outline, zig-zagging left and right, high and low: claws,
        /// legs, the shell's edge and the face twice (the 8th and the 16th blows). 'limbs' gets the
        /// limb group each point belongs to (its damage sprites change when struck).
        /// </summary>
        public void Outline(Vector2 c, Vector2 ext, Vector2[] pts, int[] limbs)
        {
            const int groups = 9;
            Vector2[] sum = new Vector2[groups];
            int[] n = new int[groups];
            Vector2[] far = new Vector2[groups];
            Vector2[] near = new Vector2[groups];
            float[] farD = new float[groups];
            float[] nearD = new float[groups];
            for (int g = 0; g < groups; g++)
            {
                farD[g] = -1f;
                nearD[g] = float.MaxValue;
            }
            if (_items != null)
            {
                foreach (CrabLimbItem item in _items)
                {
                    SpriteRenderer r = Renderer(item);
                    int g = (int)item.crabLimb;
                    if (r == null || g < 0 || g >= groups)
                    {
                        continue;
                    }
                    Vector2 p = r.bounds.center;
                    sum[g] += p;
                    n[g]++;
                    float d = (p - c).sqrMagnitude;
                    if (d > farD[g])
                    {
                        farD[g] = d;
                        far[g] = p;
                    }
                    if (d < nearD[g])
                    {
                        nearD[g] = d;
                        near[g] = p;
                    }
                }
            }
            // (limb group, 0 outer 1 mid 2 inner) or a shell point (group -1, x, y in half sizes)
            float[,] plan =
            {
                { (int)CrabLimb.ArmRight, 0, 0 },
                { (int)CrabLimb.LegLeft1, 0, 0 },
                { -1, -0.72f, 0.7f },
                { (int)CrabLimb.LegRight2, 1, 0 },
                { (int)CrabLimb.ArmLeft, 0, 0 },
                { -1, 0.95f, 0.1f },
                { (int)CrabLimb.LegLeft3, 0, 0 },
                { -1, 0f, 0.55f },
                { (int)CrabLimb.LegRight3, 0, 0 },
                { -1, -0.95f, 0.05f },
                { (int)CrabLimb.ArmRight, 2, 0 },
                { (int)CrabLimb.LegLeft2, 1, 0 },
                { -1, 0.72f, 0.72f },
                { (int)CrabLimb.ArmLeft, 2, 0 },
                { (int)CrabLimb.LegRight1, 0, 0 },
                { -1, 0f, 0.4f }
            };
            for (int i = 0; i < pts.Length && i < plan.GetLength(0); i++)
            {
                int g = (int)plan[i, 0];
                if (g >= 0 && n[g] > 0)
                {
                    int how = (int)plan[i, 1];
                    pts[i] = how == 0 ? far[g] : how == 1 ? sum[g] / n[g] : near[g];
                    limbs[i] = g;
                    continue;
                }
                if (g >= 0)
                {
                    // that limb isn't there: a point on the shell's rim on the same side instead
                    bool right = g == (int)CrabLimb.ArmRight || g == (int)CrabLimb.LegRight1 || g == (int)CrabLimb.LegRight2 || g == (int)CrabLimb.LegRight3;
                    float a = (right ? 0f : 180f) + (i * 37f % 80f - 40f);
                    pts[i] = c + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad) * ext.x * 1.1f, Mathf.Sin(a * Mathf.Deg2Rad) * ext.y * 0.9f);
                    limbs[i] = (int)CrabLimb.MainBody;
                    continue;
                }
                pts[i] = c + new Vector2(plan[i, 1] * ext.x, plan[i, 2] * ext.y);
                limbs[i] = (int)CrabLimb.MainBody;
            }
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i].x = Mathf.Clamp(pts[i].x, 0.5f, MapBox.width - 1.5f);
                pts[i].y = Mathf.Max(0.5f, pts[i].y);
            }
        }

        /// <summary>The beam goes dark and silent at once.</summary>
        public void CutLaser()
        {
            if (Cz == null)
            {
                return;
            }
            try
            {
                Cz._beam_enabled = false;
                if (Cz.mouthSprite != null)
                {
                    Cz.mouthSprite.SetActive(false);
                }
                Cut(Cz.arm1);
                Cut(Cz.arm2);
                if (MusicBox.inst != null)
                {
                    MusicBox.inst.stopDrawingSound("event:/SFX/UNIQUE/Crabzilla/CrabzillaLazer");
                    MusicBox.inst.stopDrawingSound("event:/SFX/UNIQUE/Crabzilla/CrabzillaVoice");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab laser: " + e);
            }
        }

        private static void Cut(CrabArm arm)
        {
            if (arm == null)
            {
                return;
            }
            arm._laser_frame_index = 0;
            if (arm.laser != null)
            {
                arm.laser.enabled = false;
            }
        }

        /// <summary>Every part tinted towards 'c' by 'amount', its opacity times 'alpha' (from its own colours).</summary>
        public void Tint(Color c, float amount, float alpha)
        {
            if (Cz == null)
            {
                return;
            }
            if (_parts == null)
            {
                _parts = Cz.GetComponentsInChildren<SpriteRenderer>(true);
                _colors = new Color[_parts.Length];
                for (int i = 0; i < _parts.Length; i++)
                {
                    _colors[i] = _parts[i] != null ? _parts[i].color : Color.white;
                }
            }
            for (int i = 0; i < _parts.Length; i++)
            {
                SpriteRenderer r = _parts[i];
                if (r == null)
                {
                    continue;
                }
                Color o = _colors[i];
                Color t = Color.Lerp(o, c, amount);
                t.a = o.a * alpha;
                r.color = t;
            }
            _tinted = true;
        }

        /// <summary>
        /// The dead kaiju's last pixels off screen: every renderer of its prefab (shell, legs, mouth,
        /// and its shadow, which the tint doesn't fade: it stayed under the orb, the kick and the
        /// landing), until the game destroys it once the world moves again. Only for a Crabzilla that
        /// is dead (the cinematic killed it).
        /// </summary>
        public void Hide()
        {
            if (_hidden || Cz == null || Crab == null || Crab.Sid() != Id || Crab.hasHealth())
            {
                return;
            }
            _hidden = true;
            try
            {
                foreach (Renderer r in Cz.GetComponentsInChildren<Renderer>(true))
                {
                    if (r != null)
                    {
                        r.enabled = false;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab hide: " + e);
            }
        }

        private bool _hidden;

        public void Untint()
        {
            if (!_tinted || _parts == null)
            {
                return;
            }
            _tinted = false;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] != null)
                {
                    _parts[i].color = _colors[i];
                }
            }
        }

        /// <summary>The game's own limb damage on one limb group (-1: all of them): new sprites and a red flicker.</summary>
        public void ShowDamage(int limb)
        {
            CrabLimbGroup[] list = Cz != null ? Cz.list_limbs : null;
            if (list == null || !Standing && !Dying)
            {
                return;
            }
            try
            {
                foreach (CrabLimbGroup g in list)
                {
                    if (g != null && (limb < 0 || (int)g.crabLimb == limb))
                    {
                        g.showDamage();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab limbs: " + e);
            }
        }

        /// <summary>
        /// The game's own red hit flicker on one limb group, its sprites unchanged (between the HP
        /// thresholds the game shows nothing, but every blow should land visibly on the kaiju).
        /// </summary>
        public void HitFlicker(int limb)
        {
            CrabLimbGroup[] list = Cz != null ? Cz.list_limbs : null;
            if (list == null || _tinted)
            {
                return;
            }
            try
            {
                foreach (CrabLimbGroup g in list)
                {
                    if (g == null || (int)g.crabLimb != limb || g.IsFlickering() || g._list == null)
                    {
                        continue;
                    }
                    foreach (CrabLimbItem item in g._list)
                    {
                        if (item != null && item._sprite_renderer != null)
                        {
                            item._sprite_renderer.color = item._dmg;
                        }
                    }
                    g._flicker_timer = 0.15f;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab flicker: " + e);
            }
        }

        /// <summary>
        /// Ends every limb flicker at once (a living kaiju let go mid-flurry): its own update only runs
        /// the flicker of the limb group it last hit, so any other would stay red, its damage sprites
        /// stuck (CrabLimbGroup.showDamage waits for the flicker to end).
        /// </summary>
        public void FinishFlicker()
        {
            if (!Present || Cz.list_limbs == null)
            {
                return;
            }
            try
            {
                foreach (CrabLimbGroup g in Cz.list_limbs)
                {
                    if (g != null && g.IsFlickering())
                    {
                        g.update(1f);   // the timer runs out: back to the limb's own shade
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] crab flicker: " + e);
            }
        }

        /// <summary>Runs the limb flicker that Crabzilla.update would (it is skipped while held).</summary>
        public void TickFlicker(float dt)
        {
            if (dt <= 0f || _tinted || Cz == null || Cz.list_limbs == null)
            {
                return;
            }
            foreach (CrabLimbGroup g in Cz.list_limbs)
            {
                if (g != null && g.IsFlickering())
                {
                    g.update(dt);
                }
            }
        }
    }

    // ------------------------------------------------------------------ patches --

    /// <summary>A Crabzilla is born (the game hands it to the player): arm the cinematic.</summary>
    [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreatureCrabzilla))]
    internal static class KaijuSpawnPatch
    {
        private static void Postfix(Actor pActor)
        {
            try
            {
                KaijuKiller.OnSpawn(pActor);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] kaiju spawn: " + e);
            }
        }
    }

    /// <summary>The death nuke of the Crabzilla the hero killed belongs to the cinematic (it goes off in the sky).</summary>
    [HarmonyPatch(typeof(ActionLibrary), nameof(ActionLibrary.startCrabzillaNuke))]
    internal static class KaijuNukePatch
    {
        private static bool Prefix(BaseSimObject pTarget, ref bool __result)
        {
            long id = KaijuKiller.ContainCrab;
            if (id < 0 || pTarget == null || pTarget.Sid() != id)
            {
                return true;
            }
            if (Dev.On) Dev.Trace("kaiju: Crabzilla #" + id + "'s nuke is contained");
            __result = true;
            return false;
        }
    }

    /// <summary>The Crabzilla in the cinematic stands still (no walking, no laser, no stale input), even after it dies.</summary>
    [HarmonyPatch(typeof(Crabzilla), nameof(Crabzilla.update))]
    internal static class KaijuHoldPatch
    {
        private static bool Prefix(Crabzilla __instance)
        {
            long id = KaijuKiller.HeldCrab;
            if (id < 0)
            {
                return true;
            }
            Actor a = __instance.actor;
            return a == null || a.Sid() != id;
        }
    }

    /// <summary>The director's frame (every frame, after the simulation step or its skip).</summary>
    [HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    internal static class KaijuFramePatch
    {
        private static void Postfix()
        {
            try
            {
                KaijuKiller.Frame();
            }
            catch (Exception e)
            {
                SakugaMod.Report("kaiju", e);
            }
        }
    }
}
