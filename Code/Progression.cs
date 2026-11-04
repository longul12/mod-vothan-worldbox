using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Natural growth. Fighting teaches: every blow landed on a foe (stronger foes teach more), kills,
    /// battles survived, duels won, and above all surviving at the brink of death earn Sakuga XP (saved
    /// with the unit); so do the years themselves (a warrior drills, anyone learns a little). At those
    /// moments (a "trial"), and once a year for anyone who is ready, a unit that meets its next tier's
    /// gates (XP, kills, level, years served at its tier, a proof of worth) may break through. Caps keep
    /// the pyramid: many Novices, a few Masters, one or two Gods.
    /// Measured with the dev "lab" (a continent, six civilizations of ten, sonic speed).
    /// </summary>
    internal static class Progression
    {
        public const string KeySxp = "sakuga_sxp";
        public const string KeySince = "sakuga_since";
        public const string KeyProof = "sakuga_proof";
        public const string KeyStarred = "sakuga_starred";

        // proofs of worth (bits)
        public const int ProofDuel = 1;
        public const int ProofBrute = 2;
        public const int ProofFiend = 4;
        public const int ProofTitan = 8;
        public const int ProofMasterDuel = 16;
        /// <summary>Survived a battle at the brink of death.</summary>
        public const int ProofBrink = 32;
        /// <summary>Killed a Master or stronger.</summary>
        public const int ProofSlayer = 64;

        /// <summary>The chance, once a year, that a unit ready for the next tier breaks through (index = that tier).</summary>
        private static readonly float[] Yearly = { 0f, 0.12f, 0.25f, 0.2f, 0.15f, 0.1f, 0.05f };
        private const string KeyYear = "sakuga_year";

        private static readonly int[] _census = new int[Tiers.Count + 1];
        private static float _censusIn;
        private static int _population;

        // ------------------------------------------------------------ data --

        public static float Sxp(Actor a)
        {
            a.data.get(KeySxp, out float v, 0f);
            return v;
        }

        public static void AddSxp(Actor a, float amount)
        {
            if (a == null || amount <= 0f || Settings.ProgressSpeed == 0)
            {
                return;
            }
            float mult = Settings.SxpMult;
            // a student near a stronger ally learns faster
            if (Fighters.TryGet(a, out Fighter f) && f.Tier < Tier.God)
            {
                mult *= Mentor(a, f.Tier) ? 1.5f : 1f;
            }
            a.data.set(KeySxp, Sxp(a) + amount * mult);
        }

        /// <summary>XP from frequent sources (every blow): no mentor search.</summary>
        private static void AddSxpFast(Actor a, float amount)
        {
            if (a == null || amount <= 0f || Settings.ProgressSpeed == 0)
            {
                return;
            }
            a.data.set(KeySxp, Sxp(a) + amount * Settings.SxpMult);
        }

        /// <summary>A blow landed: the striker learns by the foe's weight and tier, the struck a little by surviving it.</summary>
        public static void OnHit(Actor victim, Actor by)
        {
            if (Settings.ProgressSpeed == 0 || victim == null || by == null || by == victim || !by.isAlive() || by.asset == null)
            {
                return;
            }
            if (by.kingdom == null || victim.kingdom == null || !by.areFoes(victim))
            {
                return;   // (a friend caught in the swing teaches nothing)
            }
            float xp = 0.3f * Weights.Lesson(Weights.Of(victim)) * (1 + (int)Tiers.Of(victim));
            AddSxpFast(by, Mathf.Min(xp, 8f));
            if (victim.isAlive())
            {
                AddSxpFast(victim, 0.08f);
            }
        }

        // ---------------------------------------------------------- the years --

        private static int _cursor;

        /// <summary>
        /// Once a year for every adult that may grow: a fighter of a tier trains hard (+3, +3 more per tier),
        /// a warrior drills (+3 XP), anyone else learns a little (+1), and a unit that meets its next tier's
        /// gates may break through. A few units per frame.
        /// </summary>
        private static void YearlyGrowth()
        {
            if (Settings.ProgressSpeed == 0 || World.world == null || World.world.units == null)
            {
                return;
            }
            List<Actor> list = World.world.units.getSimpleList();
            int n = list.Count;
            if (n == 0)
            {
                return;
            }
            double now = World.world.getCurWorldTime();
            // (more world per frame at faster speeds: more units looked at per frame)
            int ticks = Config.time_scale_asset != null ? Mathf.Max(1, Config.time_scale_asset.ticks) : 1;
            int budget = Mathf.Min(n, 80 * Mathf.Max(1, ticks / 2));
            for (int i = 0; i < budget; i++)
            {
                if (_cursor >= n)
                {
                    _cursor = 0;
                }
                Actor a = list[_cursor++];
                if (a == null || !a.isAlive() || a.asset == null || !a.isAdult())
                {
                    continue;
                }
                Tier cur = Tiers.Of(a);
                if (cur == Tier.None && !CanAwaken(a))
                {
                    continue;
                }
                a.data.get(KeyYear, out float last, -1f);
                if (last < 0f || last > now)
                {
                    a.data.set(KeyYear, (float)now);
                    continue;
                }
                int years = (int)((now - last) / 60.0);
                if (years <= 0)
                {
                    continue;
                }
                a.data.set(KeyYear, last + years * 60f);
                years = Mathf.Min(years, 3);
                float drill = cur != Tier.None ? 3f + 3f * (int)cur : a.isWarrior() || a.hasArmy() ? 3f : 1f;
                AddSxpFast(a, drill * years);
                TrueGodAscension.Try(a, cur);
                for (int y = 0; y < years && cur < Tier.God; y++)
                {
                    Tier next = cur + 1;
                    if (!Eligible(a, cur, next))
                    {
                        break;
                    }
                    if (UnityEngine.Random.value < Yearly[(int)next] * Mathf.Min(2f, Settings.TrialMult))
                    {
                        Promote(a, next, true);
                        break;
                    }
                }
            }
        }

        private static bool Mentor(Actor a, Tier t)
        {
            foreach (Fighter f in Fighters.All)
            {
                if (f.Tier > t && f.A != a && f.Valid && f.A.kingdom == a.kingdom
                    && (f.A.current_position - a.current_position).sqrMagnitude < 100f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The proofs of worth (bits). They live in the int store (each type has its own store).</summary>
        public static int Proof(Actor a)
        {
            a.data.get(KeyProof, out int bits, 0);
            a.data.get(KeyProof, out float old, 0f);   // in case a save holds them as a float
            return bits | (int)old;
        }

        public static void AddProof(Actor a, int bits)
        {
            a.data.set(KeyProof, Proof(a) | bits);
        }

        /// <summary>Years served at the current tier.</summary>
        public static float Years(Actor a)
        {
            a.data.get(KeySince, out float since, -1f);
            if (since < 0f)
            {
                return 999f;
            }
            return (float)((World.world.getCurWorldTime() - since) / 60.0);
        }

        // ----------------------------------------------------------- events --

        /// <summary>A kill (from the game's kill bookkeeping).</summary>
        public static void OnKill(Actor killer, Actor victim)
        {
            if (killer == null || victim == null || Settings.ProgressSpeed == 0)
            {
                return;
            }
            TrueGodAscension.EnemyKilled(killer, victim);
            Weight w = Weights.Species(victim);
            Tier vt = Tiers.Of(victim);
            Weight wv = Weights.Of(victim);
            float xp = 4f * Weights.Lesson(wv) * (1 + (int)vt);
            bool rival = Fighters.TryGet(killer, out Fighter kf) && kf.Rival != 0 && kf.Rival == victim.Sid();
            if (rival)
            {
                xp *= 2f;
            }
            AddSxp(killer, xp);
            if (wv >= Weight.Brute)
            {
                AddProof(killer, ProofBrute);
            }
            if (wv >= Weight.Fiend || vt >= Tier.Expert)
            {
                AddProof(killer, ProofFiend);
            }
            if (wv >= Weight.Titan || w >= Weight.Titan)
            {
                AddProof(killer, ProofTitan);
            }
            if (vt >= Tier.Master)
            {
                AddProof(killer, ProofSlayer);
            }
            Tier kt = Tiers.Of(killer);
            if (kt == Tier.Grandmaster && (w >= Weight.Titan || wv >= Weight.Kaiju))
            {
                // the final blow on a titan: ascension
                Trial(killer, 100f, true);
                return;
            }
            Trial(killer, rival ? 2f : 1f, false);
        }

        /// <summary>A fighter's battle ended and it lived. 'since': when it began (if CombatSince is already reset).</summary>
        public static void BattleOver(Fighter f, double since = -1)
        {
            Actor a = f.A;
            if (!f.Valid || Settings.ProgressSpeed == 0)
            {
                return;
            }
            // time spent fighting; a battle whose start is unknown is short (never the world's age)
            double start = since >= 0 ? since : f.CombatSince;
            double length = start >= 0 ? f.LastCombat - start : 0.0;
            if (length >= 15.0)
            {
                AddSxp(a, 6f);
            }
            if (f.LowestHp < 0.15f)
            {
                AddSxp(a, 15f);
                AddProof(a, ProofBrink);
                Trial(a, 3f, f.Tier == Tier.Grandmaster);
            }
            else if (length >= 15.0)
            {
                Trial(a, 1f, false);
            }
        }

        /// <summary>A duel ended with this unit the winner.</summary>
        public static void DuelWon(Actor winner, Actor loser)
        {
            if (winner == null || loser == null)
            {
                return;
            }
            Tier lt = Tiers.Of(loser);
            AddSxp(winner, 20f * Mathf.Max(1, (int)lt));
            AddProof(winner, ProofDuel);
            if (lt >= Tier.Master)
            {
                AddProof(winner, ProofMasterDuel);
            }
            Trial(winner, 2f, false);
        }

        // ------------------------------------------------------------ trial --

        /// <summary>A moment that may make a unit break through to its next tier.</summary>
        public static void Trial(Actor a, float mult, bool ascension)
        {
            if (a == null || !a.isAlive() || Settings.ProgressSpeed == 0)
            {
                return;
            }
            Tier cur = Tiers.Of(a);
            if (cur == Tier.God)
            {
                return;
            }
            Tier next = cur + 1;
            if (!Eligible(a, cur, next))
            {
                return;
            }
            TierInfo info = Tiers.Info(next);
            float chance;
            if (next == Tier.God)
            {
                // ascension: only at the brink or over a fallen titan
                if (!ascension)
                {
                    return;
                }
                chance = mult >= 100f ? 1f : 0.35f;
                chance *= Mathf.Min(1f, Settings.TrialMult);
            }
            else
            {
                chance = info.Trial * mult * Settings.TrialMult;
            }
            if (UnityEngine.Random.value < chance)
            {
                Promote(a, next, true);
            }
        }

        public static bool Eligible(Actor a, Tier cur, Tier next)
        {
            TierInfo info = Tiers.Info(next);
            if (info == null)
            {
                return false;
            }
            if ((int)next > Settings.MaxTier)
            {
                return false;   // (the "highest tier by growth" setting: nobody grows past it by themselves)
            }
            if (cur == Tier.None && !CanAwaken(a))
            {
                return false;
            }
            if (Sxp(a) < info.Sxp || Record(a) < info.Kills || a.data.level < info.Level)
            {
                return false;
            }
            if (cur != Tier.None && Years(a) < info.Seasoning * Settings.SeasonMult)
            {
                return false;
            }
            int proof = Proof(a);
            switch (next)
            {
                case Tier.Grandmaster:
                    // a deed (a won duel, a slain titan or demon, a Master+ felled, a survived brink), or long
                    // service: a peaceful realm's Masters still rise in time
                    if ((proof & (ProofMasterDuel | ProofTitan | ProofSlayer | ProofFiend | ProofDuel | ProofBrink)) == 0
                        && Years(a) < 6f * Settings.SeasonMult)
                    {
                        return false;
                    }
                    break;
            }
            return Census(next) < Cap(next);
        }

        /// <summary>
        /// The combat record the kill gates read: kills, plus one for every 60 XP (long years of drill and
        /// sparring count for something: a soldier of a long peace still grows, only slower).
        /// </summary>
        public static int Record(Actor a)
        {
            return a.data.kills + (int)(Sxp(a) / 60f);
        }

        /// <summary>Who may awaken as a Novice by themselves: adults of civilized peoples (and beasts, if allowed).</summary>
        public static bool CanAwaken(Actor a)
        {
            if (!a.isAdult() || a.asset.is_boat || !a.asset.has_sprite_renderer || a.asset.special || a.asset.skip_fight_logic
                || !a.asset.can_be_moved_by_powers || a.asset.has_avatar_prefab)
            {
                return false;
            }
            return Settings.BeastsAwaken || a.isKingdomCiv();
        }

        // ---------------------------------------------------------- census --

        public static void Tick(float realDt)
        {
            try
            {
                YearlyGrowth();
            }
            catch (Exception e)
            {
                SakugaMod.Report("progression years", e);
            }
            _censusIn -= realDt;
            if (_censusIn > 0f || World.world == null)
            {
                return;
            }
            _censusIn = 5f;
            Array.Clear(_census, 0, _census.Length);
            foreach (Fighter f in Fighters.All)
            {
                if (f.Valid && f.Tier != Tier.None)
                {
                    _census[(int)f.Tier]++;
                }
            }
            // the caps follow the civilized population (animals and monsters don't count)
            int civ = 0;
            foreach (Actor u in World.world.units)
            {
                if (u != null && u.isAlive() && u.isKingdomCiv())
                {
                    civ++;
                }
            }
            _population = civ;
        }

        public static int Census(Tier t)
        {
            return t == Tier.None ? 0 : _census[(int)t];
        }

        public static int Cap(Tier t)
        {
            float m = Settings.CapMult;
            switch (t)
            {
                // the pyramid, per civilized people: one Novice in 8, one Adept in 25, one Expert in 70,
                // one Master in 250, a Grandmaster in 800 (no fixed ceiling: they grow with the world, like
                // the others), the Gods by the setting
                case Tier.Novice:
                    return Mathf.Max(4, Mathf.RoundToInt(_population / 8f * m));
                case Tier.Adept:
                    return Mathf.Max(2, Mathf.RoundToInt(_population / 25f * m));
                case Tier.Expert:
                    return Mathf.Max(2, Mathf.RoundToInt(_population / 70f * m));
                case Tier.Master:
                    return Mathf.Max(1, Mathf.RoundToInt(_population / 250f * m));
                case Tier.Grandmaster:
                    return Mathf.Max(1, Mathf.RoundToInt(_population / 800f * m));
                default:
                    return Settings.GodCap;   // (exactly the setting, at every speed)
            }
        }

        // ---------------------------------------------------------- promote --

        /// <summary>Makes a unit this tier (replacing any other), with its ceremony, star and announcement.</summary>
        public static void Promote(Actor a, Tier tier, bool natural)
        {
            if (a == null || !a.isAlive())
            {
                return;
            }
            Tier old = Tiers.Of(a);
            if (old == tier)
            {
                return;
            }
            _promoting = true;
            try
            {
                for (int i = 1; i <= Tiers.Count; i++)
                {
                    ActorTrait t = Tiers.Trait((Tier)i);
                    if (t != null && a.hasTrait(t) && (Tier)i != tier)
                    {
                        a.removeTrait(t);
                    }
                }
                if (tier != Tier.None)
                {
                    a.addTrait(Tiers.Trait(tier), true);
                }
            }
            finally
            {
                _promoting = false;
            }
            Changed(a, old, tier, natural);
        }

        private static bool _promoting;

        /// <summary>A unit holding several tiers keeps only the highest (stats never stack).</summary>
        public static void Normalize(Actor a)
        {
            // not again from inside our own removals (each removal's callback would merge the rest
            // itself, and the rings would play once per removed tier)
            if (a == null || !a.isAlive() || _promoting)
            {
                return;
            }
            Tier keep = Tiers.Of(a);
            if (keep == Tier.None)
            {
                return;
            }
            List<Tier> removed = null;
            for (int i = 1; i < (int)keep; i++)
            {
                ActorTrait t = Tiers.Trait((Tier)i);
                if (t != null && a.traits.Contains(t))
                {
                    (removed ??= new List<Tier>()).Add((Tier)i);
                }
            }
            if (removed == null)
            {
                return;
            }
            _promoting = true;
            try
            {
                foreach (Tier t in removed)
                {
                    a.removeTrait(Tiers.Trait(t));
                }
            }
            finally
            {
                _promoting = false;
            }
            Hooks.TiersMerged?.Invoke(a, removed, keep);
        }

        /// <summary>A tier trait was added or removed, by us, the player or anything else.</summary>
        public static void Changed(Actor a, Tier old, Tier now, bool natural)
        {
            if (_promoting)
            {
                return;
            }
            Tier was = Fighters.TryGet(a, out Fighter pf) ? pf.Tier : old;
            Fighters.Touch(a);
            if (now != Tier.None && now != was)
            {
                // counted at once: two trials in the same census window can't both take the last
                // place under a cap (the recount every 5 s catches up with deaths and demotions)
                _census[(int)now]++;
            }
            if (now == Tier.None)
            {
                Hooks.TierChanged?.Invoke(a, old, now, natural);
                return;
            }
            a.data.set(KeySince, (float)World.world.getCurWorldTime());
            TierInfo info = Tiers.Info(now);
            if (!natural && Sxp(a) < info.Sxp)
            {
                a.data.set(KeySxp, info.Sxp);   // a given tier counts as earned; it grows on from there
            }
            if (now > old)
            {
                a.setStatsDirty();
                a.event_full_stats = true;
                a.data.mana = Mathf.Max(a.data.mana, (int)info.Mana);
                Star(a, now);
                if (natural)
                {
                    Announce(a, now);
                }
            }
            Hooks.TierChanged?.Invoke(a, old, now, natural);
        }

        /// <summary>Auto-favourite by tier (setting), once: a unit the player un-starred stays un-starred.</summary>
        /// <summary>What a Primordial's star is remembered as (one above a God's).</summary>
        private const int PrimordialMark = 7;

        private static void Star(Actor a, Tier t)
        {
            // (a Primordial counts as a God to every system, but has its own switch)
            bool prim = Primordials.Is(a);
            int mark = prim ? PrimordialMark : (int)t;
            if (!(prim ? Settings.StarPrimordial : Settings.Star[(int)t]) || a.isFavorite())
            {
                return;
            }
            a.data.get(KeyStarred, out int starredAt, 0);   // the int store (each type has its own)
            a.data.get(KeyStarred, out float oldAt, 0f);
            if (Mathf.Max(starredAt, (int)oldAt) >= mark)
            {
                return;
            }
            a.data.favorite = true;   // (hidden while the hero plays a cinematic: Moves' CinematicStarPatch)
            a.data.set(KeyStarred, mark);
        }

        /// <summary>
        /// A tier's auto-favourite setting changed. Off: its units the mod starred lose their star (the
        /// player's own stars stay). On: its units are starred (except ones the player un-starred after
        /// the mod starred them).
        /// </summary>
        public static void Restar(Tier t, bool on)
        {
            if (World.world == null || World.world.units == null || t == Tier.None)
            {
                return;
            }
            foreach (Actor a in World.world.units)
            {
                // (the Primordials have their own switch: RestarPrimordials)
                if (a == null || !a.isAlive() || Tiers.Of(a) != t || Primordials.Is(a))
                {
                    continue;
                }
                Restar(a, t, on);
            }
        }

        /// <summary>The Primordials' auto-favourite setting changed (as Restar, for them).</summary>
        public static void RestarPrimordials(bool on)
        {
            if (World.world == null || World.world.units == null)
            {
                return;
            }
            foreach (Actor a in World.world.units)
            {
                if (a != null && a.isAlive() && Primordials.Is(a))
                {
                    Restar(a, Tier.God, on);
                }
            }
        }

        /// <summary>One unit: starred (on), or its mod-given star taken back (off; the player's own stays).</summary>
        private static void Restar(Actor a, Tier t, bool on)
        {
            if (on)
            {
                Star(a, t);
                return;
            }
            a.data.get(KeyStarred, out int at, 0);
            a.data.get(KeyStarred, out float atOld, 0f);
            if (Mathf.Max(at, (int)atOld) <= 0)
            {
                return;   // never starred by the mod: the player's own star
            }
            if (a.isFavorite())
            {
                a.data.favorite = false;
            }
            a.data.set(KeyStarred, 0);
            if (atOld > 0f)
            {
                a.data.set(KeyStarred, 0f);
            }
        }

        private static void Announce(Actor a, Tier t)
        {
            if ((int)t < Settings.AnnounceFrom)
            {
                return;
            }
            try
            {
                string text = Locale.Format("sakuga_promoted", a.getName(), Tiers.Name(t));
                WorldTip.showNow(text, false, "top", t >= Tier.Grandmaster ? 4f : 3f, Tiers.Info(t).Hex);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] announce: " + e);
            }
        }

        public static void Clear()
        {
            Array.Clear(_census, 0, _census.Length);
            _censusIn = 0f;
        }
    }

    /// <summary>Blows teach (Sakuga XP for every landed blow on a foe).</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.getHit))]
    internal static class HitLessonPatch
    {
        private static void Postfix(Actor __instance, BaseSimObject pAttacker)
        {
            try
            {
                if (pAttacker != null && pAttacker.isActor())
                {
                    Progression.OnHit(__instance, pAttacker.a);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("hit lesson", e);
            }
        }
    }

    /// <summary>Kills teach (Sakuga XP and promotion trials).</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.newKillAction))]
    internal static class KillPatch
    {
        private static void Postfix(Actor __instance, Actor pDeadUnit)
        {
            try
            {
                Progression.OnKill(__instance, pDeadUnit);
                Hooks.Killed?.Invoke(__instance, pDeadUnit);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] kill: " + e);
            }
        }
    }
}
