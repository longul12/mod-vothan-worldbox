using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// How heavy an opponent is. A tier may only use its techniques on opponents up to its
    /// class (Novices fight people and wolves, Experts take on bears, Masters demons, Grandmasters
    /// dragons, Gods anything, Crabzilla included). Ordinary attacks are never limited.
    /// </summary>
    internal enum Weight
    {
        Critter = 0,
        Folk = 1,
        Brute = 2,
        Fiend = 3,
        Titan = 4,
        Kaiju = 5
    }

    internal static class Weights
    {
        // (asset ids are not all lower case: the UFO's is "UFO")
        private static readonly HashSet<string> Fiends = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "demon", "cold_one", "evil_mage", "white_mage", "necromancer", "ghost", "greg", "alien", "angle",
            "fire_elemental", "fire_elemental_blob", "fire_elemental_horse", "fire_elemental_slug", "fire_elemental_snake",
            "fire_skull", "plague_doctor", "druid", "crystal_golem", "assimilator", "tumor_monster_unit", "living_house"
        };

        private static readonly HashSet<string> Titans = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dragon", "zombie_dragon", "UFO", "god_finger" };
        private static readonly HashSet<string> Kaijus = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crabzilla" };

        private static readonly Dictionary<ActorAsset, Weight> _species = new Dictionary<ActorAsset, Weight>();

        /// <summary>The class of the species itself (its base health, a list for the otherworldly), cached per species.</summary>
        public static Weight Species(Actor a)
        {
            if (a == null || a.asset == null)
            {
                return Weight.Folk;
            }
            return Species(a.asset);
        }

        /// <summary>The class of a species (its base health, a list for the otherworldly), cached.</summary>
        public static Weight Species(ActorAsset asset)
        {
            if (asset == null)
            {
                return Weight.Folk;
            }
            if (_species.TryGetValue(asset, out Weight w))
            {
                return w;
            }
            w = SpeciesOf(asset);
            _species[asset] = w;
            return w;
        }

        private static Weight SpeciesOf(ActorAsset asset)
        {
            if (Peoples.Is(asset))
            {
                // a civilized people is Folk whatever its base health: cat folk are no critters, unicorn
                // and crystal golem folk no fiends (every tier may use its techniques on any people)
                return Weight.Folk;
            }
            string id = asset.id ?? "";
            if (id.StartsWith("civ_", StringComparison.Ordinal))
            {
                id = id.Substring(4);
            }
            if (Kaijus.Contains(id))
            {
                return Weight.Kaiju;
            }
            if (Titans.Contains(id))
            {
                return Weight.Titan;
            }
            float hp = SpeciesHealth(asset);
            if (hp > 5000f)
            {
                return Weight.Kaiju;
            }
            if (hp > 900f)
            {
                return Weight.Titan;
            }
            if (Fiends.Contains(id) || hp > 400f)
            {
                return Weight.Fiend;
            }
            if (hp > 180f)
            {
                return Weight.Brute;
            }
            return hp > 80f ? Weight.Folk : Weight.Critter;
        }

        private static readonly Dictionary<ActorAsset, float> _health = new Dictionary<ActorAsset, float>();

        /// <summary>
        /// A species' base health. Most species set it through their genome, not base_stats, so this
        /// adds both (the creature-overview numbers), cached per species.
        /// </summary>
        public static float SpeciesHealth(ActorAsset asset)
        {
            if (asset == null)
            {
                return 100f;
            }
            if (_health.TryGetValue(asset, out float hp))
            {
                return hp;
            }
            try
            {
                hp = asset.getStatsForOverview()["health"];
            }
            catch
            {
                hp = 0f;
            }
            if (hp <= 0f && asset.base_stats != null)
            {
                hp = asset.base_stats["health"];
            }
            if (hp <= 0f)
            {
                hp = 100f;
            }
            _health[asset] = hp;
            return hp;
        }

        /// <summary>The class a tier makes you count as (a Master crab is no critter).</summary>
        public static Weight OfTier(Tier t)
        {
            switch (t)
            {
                case Tier.Novice:
                case Tier.Adept:
                    return Weight.Folk;
                case Tier.Expert:
                    return Weight.Brute;
                case Tier.Master:
                    return Weight.Fiend;
                case Tier.Grandmaster:
                case Tier.God:
                    return Weight.Titan;
                default:
                    return Weight.Critter;
            }
        }

        public static Weight Of(Actor a)
        {
            Weight s = Species(a);
            Weight t = OfTier(Tiers.Of(a));
            return t > s ? t : s;
        }

        /// <summary>The heaviest class this unit may use techniques on.</summary>
        public static Weight Limit(Actor a)
        {
            Tier t = Tiers.Of(a);
            if (t != Tier.None)
            {
                return Tiers.Info(t).MaxClass;
            }
            // bare move traits: one class above your own kind (a dragon with moves can take Crabzilla on)
            Weight s = Species(a);
            return s >= Weight.Kaiju ? Weight.Kaiju : s + 1;
        }

        /// <summary>How much a kill of this class teaches (Sakuga XP weight).</summary>
        public static float Lesson(Weight w)
        {
            switch (w)
            {
                case Weight.Critter: return 0.25f;
                case Weight.Folk: return 1f;
                case Weight.Brute: return 2f;
                case Weight.Fiend: return 4f;
                case Weight.Titan: return 12f;
                default: return 60f;
            }
        }

        /// <summary>Too heavy to be thrown around (it staggers instead).</summary>
        public static bool Heavy(Actor v)
        {
            return v == null || !v.asset.can_be_moved_by_powers || Species(v) >= Weight.Titan;
        }
    }

    /// <summary>What happened when a technique reached its target.</summary>
    internal enum Contact
    {
        Hit,
        /// <summary>It was parried: the attacker's technique breaks.</summary>
        Parried,
        /// <summary>It struck nothing (an afterimage, a blur).</summary>
        Evaded
    }

    /// <summary>The result of one blow.</summary>
    internal struct HitResult
    {
        public float Dealt;
        public bool Crit;
        public bool Killed;
        public bool Landed;
    }

    /// <summary>Shared rules every technique uses: who is a valid target, how a blow lands, how a body flies.</summary>
    internal static class Combat
    {
        /// <summary>Extra reactions to a technique's first contact (parries, evades, deflections). First non-Hit wins.</summary>
        public static readonly List<Func<Move, Actor, bool, Contact>> ContactHandlers = new List<Func<Move, Actor, bool, Contact>>();

        /// <summary>The unit a technique's blow (Hit) is landing on right now: super armour doesn't stop it.</summary>
        internal static Actor Hitting;

        /// <summary>
        /// The unit a blow is landing on right now that must reach it even inside an intangible move
        /// (Primordials.Smite with 'pierce': the two of the World's End, the Gods' joint strike).
        /// </summary>
        internal static Actor Piercing;

        /// <summary>
        /// The move whose blow Combat.Hit is landing right now (its 'by'), for Hooks.BeforeDamage: a wave or
        /// a crack that runs on after its move ended (LineKit.Linger) still counts as that technique.
        /// </summary>
        public static Move Landing;

        /// <summary>An enemy this unit could hit right now (alive, hostile, reachable, not hidden).</summary>
        public static bool IsFoe(Actor a, Actor v)
        {
            // (a body a blow already killed stays alive until the game's death check: it is no foe)
            if (a == null || v == null || v == a || !v.isAlive() || !v.hasHealth() || a.kingdom == null || v.kingdom == null)
            {
                return false;
            }
            if (!a.areFoes(v) || !v.asset.can_be_killed_by_stuff || v.isInsideSomething() || v.isInMagnet())
            {
                return false;
            }
            if (v.ai != null && v.ai.action != null && v.ai.action.special_prevent_can_be_attacked)
            {
                return false;
            }
            if (Moves.Intangible(v))
            {
                return false;
            }
            if (KaijuKiller.ShieldCrab >= 0 && v.Sid() == KaijuKiller.ShieldCrab || KaijuKiller.Awaits(v, a))
            {
                return false;   // the Crabzilla held by (or waiting for) the Kaiju Killer: only the hero touches it
            }
            return true;
        }

        /// <summary>
        /// May a blow of 'a' still land on 'v'? Never on a friend: one of its own kingdom, or of a
        /// kingdom it is no longer at war with (a peace made, a city taken mid-move). What the wild is to
        /// whom never changes, so a creature a technique went for on purpose (Crabzilla for the Kaiju
        /// Killer, whoever its hero serves) stays fair game, as does a unit with no kingdom.
        /// Combat.Hit asks it of every blow; ask it before a raw getHit of your own (an owed kill).
        /// </summary>
        public static bool Hostile(Actor a, Actor v)
        {
            if (a == null || v == null || a.kingdom == null || v.kingdom == null || a.areFoes(v))
            {
                return true;
            }
            return a.kingdom != v.kingdom && !(a.kingdom.isCiv() && v.kingdom.isCiv());
        }

        public static bool IsAlly(Actor a, Actor v)
        {
            return a != null && v != null && v != a && v.isAlive() && a.kingdom != null && v.kingdom != null && !a.areFoes(v) && a.kingdom == v.kingdom;
        }

        /// <summary>
        /// May 'a' use a technique on 'v'? 'mult' is 1, or 0.5 when it is punching above its class
        /// (Valiant: at 30% health or less a unit may take on one class heavier, at half damage).
        /// </summary>
        public static bool Allowed(Actor a, Actor v, out float mult)
        {
            mult = 1f;
            if (v == null)
            {
                return false;
            }
            if (!Settings.ClassLimits)
            {
                return true;
            }
            Weight limit = Weights.Limit(a);
            Weight w = Weights.Of(v);
            if (w <= limit)
            {
                return true;
            }
            if (w == limit + 1 && HpPct(a) <= 0.3f)
            {
                mult = 0.5f;
                return true;
            }
            return false;
        }

        public static bool Allowed(Actor a, Actor v)
        {
            return Allowed(a, v, out _);
        }

        public static float HpPct(Actor a)
        {
            int max = a.getMaxHealth();
            return max > 0 ? Mathf.Clamp01(a.getHealth() / (float)max) : 1f;
        }

        /// <summary>
        /// Alive with health left. After a lethal Combat.Hit isAlive() stays true until the game's own
        /// death check runs: count survivors and pick chain targets with this.
        /// </summary>
        public static bool Living(Actor v)
        {
            return v != null && v.isAlive() && v.hasHealth();
        }

        /// <summary>Does fire leave this unit alone (Masters and up, fire-proof traits)?</summary>
        public static bool Fireproof(Actor a)
        {
            return a != null && a.isImmuneToFire();
        }

        /// <summary>How high a unit really is (a unit in a move: its pose, not its drawn height).</summary>
        public static float HeightOf(Actor v)
        {
            if (v == null)
            {
                return 0f;
            }
            return Moves.TryGet(v, out Move m) ? m.Pose.Height : v.position_height;
        }

        /// <summary>On the ground, or near enough to it for a ground technique to reach.</summary>
        public static bool Grounded(Actor v)
        {
            return v != null && !v.isFlying() && HeightOf(v) <= 0.35f && !v.asset.is_boat;
        }

        /// <summary>Can 'by' pick this unit up and throw it around (juggles, grabs)?</summary>
        public static bool CanPuppet(Actor v, Actor by)
        {
            if (v == null || v == by || !v.isAlive() || Moves.Locked(v) || HeightOf(v) > 0.05f || v.isFlying()
                || v.isInsideSomething() || v.isInMagnet() || v.is_immovable)
            {
                return false;
            }
            ActorAsset s = v.asset;
            if (!s.can_be_moved_by_powers || !s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render)
            {
                return false;
            }
            if (Weights.Heavy(v) || ControllableUnit.isControllingUnit(v))
            {
                return false;
            }
            return v.actor_scale <= Mathf.Max(0.3f, by.actor_scale * 2.2f);
        }

        /// <summary>
        /// CanPuppet, but a unit caught low in a hop (an archer's backstep, a dodge, a shove; never a
        /// flier) can still be grabbed out of the air, up to 'maxHop' world units. Start its Puppet
        /// with Pose.Height = its position_height (Pose.Of does that) and ease it down.
        /// </summary>
        public static bool CanCatch(Actor v, Actor by, float maxHop = 1.5f)
        {
            if (v == null || by == null || !v.isAlive())
            {
                return false;
            }
            float h = v.position_height;
            if (h <= 0.05f)
            {
                return CanPuppet(v, by);
            }
            if (h > maxHop || v.isFlying())
            {
                return false;
            }
            v.position_height = 0f;
            try
            {
                return CanPuppet(v, by);
            }
            finally
            {
                v.position_height = h;
            }
        }

        /// <summary>
        /// The target doesn't act for 'seconds': no attack (so none of the game's combat hops that come
        /// with one), and it stands where it is (no walking off mid-move; the game's own wait, which
        /// runs out by itself: nothing to restore). It can still answer the technique itself
        /// (Combat.Reach). Renew it every tick of a wind-up on one target.
        /// </summary>
        public static void Pin(Actor v, float seconds)
        {
            if (v == null || !v.isAlive() || seconds <= 0f)
            {
                return;
            }
            if (v.attack_timer < seconds)
            {
                v.setActionTimeout(seconds);
            }
            if (v.timer_action < seconds && !Moves.Busy(v) && !v.under_forces)
            {
                v.makeWait(seconds);   // (a body in flight, or played by a move, keeps going: the move or the throw has it)
            }
        }

        /// <summary>Hostile units around a point (on the ground unless 'air'), not mid-move unless 'busy'. A new list.</summary>
        public static List<Actor> FoesAround(Actor a, Vector2 at, float radius, bool air = false, bool busy = false)
        {
            List<Actor> list = new List<Actor>();
            WorldTile tile = TileAt(at);
            if (tile == null || a.kingdom == null)
            {
                return list;
            }
            int chunks = radius > 10f ? 2 : 1;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, chunks))
            {
                if (u == null || u == a || !IsFoe(a, u))
                {
                    continue;
                }
                if (!busy && Moves.Locked(u))
                {
                    continue;
                }
                if (!air && !Grounded(u))
                {
                    continue;
                }
                float r = radius + u.stats["size"] * 0.5f;
                if ((u.current_position - at).sqrMagnitude <= r * r)
                {
                    list.Add(u);
                }
            }
            return list;
        }

        /// <summary>A technique's edge over the tier's plain blows (by the user's tier).</summary>
        private static readonly float[] TechEdge = { 1f, 1.3f, 1.2f, 1.12f, 1.08f, 1.05f, 1.05f };

        /// <summary>
        /// A technique's blow: the attacker's damage times 'mult', through the game's own damage path
        /// (armor applies, the victim turns on its attacker, the kill is credited). A blow that isn't
        /// 'lethal' always leaves the victim at least 1 health, whatever its armor. Only foes are hit:
        /// a blow on a unit that stopped being one since the move picked it (a peace, a city taken)
        /// lands nothing (Hostile). A unit it lands on that is still committed to a move this blow answers
        /// has that move cut short there and then (Hooks.HitDealt: Reactions), so the blow's reaction
        /// can throw it.
        /// </summary>
        public static HitResult Hit(Actor a, Actor v, float mult, bool lethal = true, bool flash = true, float flat = 0f, Move by = null)
        {
            HitResult r = default;
            // (a body already killed this frame is not hit again: no second kill, no stolen kill credit)
            if (a == null || v == null || !v.isAlive() || !v.hasHealth() || a.Sid() == v.Sid())
            {
                return r;
            }
            if (!Hostile(a, v))
            {
                return r;
            }
            float d = Mathf.Max(4f, a.stats["damage"]) * mult + flat;
            TierInfo ti = Tiers.Info(Tiers.Of(a));
            d *= Fighters.DamageMult(a) * (ti != null ? ti.Might : 1f) * Settings.Power;
            // a Primordial's blows land three times as hard, and anything less barely scratches one
            d *= Primordials.DamageMult(a, v);
            if (by != null && by.Tech != null)
            {
                // a technique hits harder than its tier's plain blows, the lower tiers the most (a
                // novice's one trick has to count); and a fighter's own school's techniques grow with its
                // mastery: each tier above the technique's entry adds a sixth again (a God's novice combo
                // nearly doubles)
                d *= TechEdge[Mathf.Clamp((int)Tiers.Of(a), 0, 6)] * (1f + 0.18f * Schools.Mastery(a, by.Tech));
            }
            if (Randy.randomChance(Mathf.Clamp01(a.stats["critical_chance"])))
            {
                r.Crit = true;
                d *= 1.5f + Mathf.Max(0f, a.stats["critical_damage_multiplier"]) * 0.5f;
            }
            float armor = Mathf.Min(v.stats["armor"], 95f);
            float through = Mathf.Max(0.05f, 1f - armor / 100f);
            int hp = v.getHealth();
            if (!lethal)
            {
                d = Mathf.Min(d, (hp - 1) / through - 0.5f);
            }
            if (d < 1f)
            {
                return r;
            }
            bool kills = lethal && d * through >= hp;
            // a technique's blow goes through super armour (only ordinary weapons can't knock a unit out
            // of its move), whichever attack type carries it
            Actor was = Hitting;
            Move wasBy = Landing;
            Hitting = v;
            Landing = by;
            try
            {
                v.getHit(d, flash, kills ? AttackType.Weapon : AttackType.Other, a, false, false, true);
            }
            finally
            {
                Hitting = was;
                Landing = wasBy;
            }
            // the blow is stamped with its school's emblem (once a beat per body)
            SchoolStyle.Stamp(a, v, by, flat > 0f ? Mathf.Max(mult, 0.8f) : mult);
            // what really happened (a parry, Last Light or a shield may have taken the blow)
            r.Dealt = Mathf.Max(0, hp - v.getHealth());
            r.Landed = r.Dealt > 0f;
            r.Killed = !v.hasHealth();
            try
            {
                Hooks.HitDealt?.Invoke(a, v, r, by);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] hit hook: " + e);
            }
            return r;
        }

        /// <summary>
        /// Throws a unit away from 'from': it lands about 'distance' tiles away after rising 'apex'
        /// world units, tumbling. Never off the map. Heavy units only stagger.
        /// </summary>
        public static void Launch(Actor v, Vector2 from, float distance, float apex, float spin = 720f)
        {
            if (v == null || !v.isAlive() || Moves.Locked(v) || v.isInMagnet() || v.isInsideSomething()
                || RealmControl.BlocksLower(v)
                || Primordials.Any && Primordials.Is(v) && !RealmControl.Breached(v, RealmControl.RecentAttacker(v)))
            {
                return;
            }
            Moves.Interrupt(v);   // a flow strike is no shield against a launch
            if (Weights.Heavy(v))
            {
                Stagger(v, 0.4f);
                return;
            }
            Vector2 p = v.current_position;
            Vector2 dir = p - from;
            if (dir.sqrMagnitude < 0.0004f)
            {
                dir = UnityEngine.Random.insideUnitCircle;
            }
            dir.Normalize();
            LaunchDir(v, dir, distance, apex, spin);
        }

        /// <summary>Like Launch, in a given direction.</summary>
        public static void LaunchDir(Actor v, Vector2 dir, float distance, float apex, float spin = 720f)
        {
            if (v == null || !v.isAlive() || Moves.Locked(v) || Weights.Heavy(v) || v.isInMagnet() || v.isInsideSomething()
                || RealmControl.BlocksLower(v))
            {
                return;
            }
            Moves.Interrupt(v);
            apex = Mathf.Max(0.15f, apex);
            distance = Mathf.Max(0f, distance);
            Vector2 p = v.current_position;
            for (int i = 0; i < 8; i++)
            {
                Vector2 land = p + dir * distance;
                if (Toolbox.inMapBorder(ref land) && land.x > 1f && land.y > 1f && land.x < MapBox.width - 2 && land.y < MapBox.height - 2)
                {
                    break;
                }
                distance *= 0.6f;
            }
            float g = Mathf.Min(Mathf.Max(0.2f, v.stats["mass"]) * SimGlobals.m.gravity, 20f);
            float vz = Mathf.Sqrt(0.6f * apex);
            float vxy = 0.25f * distance * g / apex;
            float m = SimGlobals.m.unit_force_multiplier;
            v.addForce(dir.x * vxy * m, dir.y * vxy * m, vz * m, true, true);
            if (spin != 0f)
            {
                Tumble.Add(v, spin);
            }
        }

        /// <summary>A short stun with a lean back: the blow rocked it.</summary>
        public static void Stagger(Actor v, float seconds)
        {
            if (v == null || !v.isAlive() || RealmControl.BlocksLower(v))
            {
                return;
            }
            // dragons take no statuses: a landed one is held down the way Wingbreaker does it
            if (seconds >= 0.3f && !v.isFlying() && v.getActorComponent<Dragon>() != null)
            {
                WingbreakerGround.Down(v, seconds);
                v.startShake(0.25f, 0.12f, true, false);
                return;
            }
            if (!Weights.Heavy(v) || seconds >= 0.3f)
            {
                Daze(v, seconds);
            }
            v.startShake(0.25f, 0.12f, true, false);
        }

        /// <summary>
        /// A stun without the game's white flash (makeStunned flashes the unit white whenever no
        /// colour effect played in the last 0.3 s, which pops on a delayed stun). Use it for every
        /// stun a technique deals after its own hit flash.
        /// </summary>
        public static void Daze(Actor v, float seconds)
        {
            if (v == null || !v.isAlive() || seconds <= 0f || RealmControl.BlocksLower(v))
            {
                return;
            }
            Moves.Interrupt(v);   // a unit rocked mid flow strike stops swinging
            v.cancelAllBeh();
            v.makeWait(seconds);
            if (v.addStatusEffect("stunned", seconds, false))
            {
                v.finishAngryStatus();
            }
        }

        /// <summary>
        /// The first time a technique reaches a target: the target may answer (parry, evade)
        /// instead of being hit. Call once per target, right before the first blow.
        /// 'area': it is an area blow (can be evaded, never parried).
        /// </summary>
        public static Contact Reach(Move m, Actor v, bool area)
        {
            if (!Living(v))
            {
                return Contact.Evaded;   // (gone, or a body another blow just killed: nothing to strike)
            }
            Contact result = Contact.Hit;
            if (Moves.Intangible(v))
            {
                result = Contact.Evaded;
            }
            else
            {
                for (int i = 0; i < ContactHandlers.Count; i++)
                {
                    try
                    {
                        Contact c = ContactHandlers[i](m, v, area);
                        if (c != Contact.Hit)
                        {
                            result = c;
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Sakuga] contact: " + e);
                    }
                }
            }
            try
            {
                Hooks.Reached?.Invoke(m, v, result);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] reached hook: " + e);
            }
            return result;
        }

        // ------------------------------------------------------------- places --

        public static WorldTile TileAt(Vector2 p)
        {
            if (World.world == null)
            {
                return null;
            }
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, MapBox.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, MapBox.height - 1);
            return World.world.GetTile(x, y);
        }

        public static Vector2 Clamp(Vector2 p)
        {
            p.x = Mathf.Clamp(p.x, 0.5f, MapBox.width - 1.5f);
            p.y = Mathf.Clamp(p.y, 0.5f, MapBox.height - 1.5f);
            return p;
        }

        /// <summary>Solid ground to land on: not a mountain or wall, not lava, not water (unless allowed). Burning grass is fine.</summary>
        public static bool SafeTile(WorldTile t, bool water = false)
        {
            if (t == null || t.Type == null)
            {
                return false;
            }
            if (t.Type.block || t.Type.lava)
            {
                return false;
            }
            return water || !t.Type.liquid;
        }

        public static bool SafeSpot(Vector2 p, bool water = false)
        {
            return p.x >= 1f && p.y >= 1f && p.x < MapBox.width - 1 && p.y < MapBox.height - 1 && SafeTile(TileAt(p), water);
        }

        /// <summary>The nearest safe spot to 'want' within 'radius' tiles (spiral search), if any.</summary>
        public static bool FindLanding(Vector2 want, float radius, out Vector2 spot, bool water = false)
        {
            want = Clamp(want);
            if (SafeSpot(want, water))
            {
                spot = want;
                return true;
            }
            int r = Mathf.CeilToInt(radius);
            for (int ring = 1; ring <= r; ring++)
            {
                for (int i = 0; i < ring * 8; i++)
                {
                    float a = i / (ring * 8f) * Mathf.PI * 2f;
                    Vector2 p = want + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                    if (SafeSpot(p, water))
                    {
                        spot = p;
                        return true;
                    }
                }
            }
            spot = want;
            return false;
        }

        /// <summary>Can a unit travel in a straight line along the ground from a to b (no walls, lava, water on the way)?</summary>
        public static bool ClearPath(Vector2 a, Vector2 b, bool water = false)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(d / 0.7f));
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)n);
                // a blur passes over burning grass; it only may not stop in the flames
                if (i == n ? !SafeSpot(p, water) : !Passable(p, water))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Can a dash pass over this spot (not a wall, mountain, lava or water; fire is fine)?</summary>
        public static bool Passable(Vector2 p, bool water = false)
        {
            if (p.x < 1f || p.y < 1f || p.x >= MapBox.width - 1 || p.y >= MapBox.height - 1)
            {
                return false;
            }
            WorldTile t = TileAt(p);
            return t != null && t.Type != null && !t.Type.block && !t.Type.lava && (water || !t.Type.liquid);
        }
    }
}
