using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// FLOW: how a master fights between its techniques. Masters, Grandmasters and Gods
    /// (TierInfo.Flow) never stand and trade the game's own weapon swings: every blow they would
    /// have swung, every arrow they would have loosed, every few steps they would have walked
    /// towards an enemy becomes part of the mod's motion vocabulary, so a fight between them is
    /// one continuous flow from technique to strikes to technique.
    ///
    ///   STRIKE CHAINS   the game asks a Flow fighter to swing at an enemy in reach
    ///                   (Actor.tryToAttack): instead a FlowStrike plays, a 0.3-0.6 s chain of
    ///                   2-4 blows picked from a pool by circumstance (armed or not, a much bigger
    ///                   or a smaller enemy), the fighter darting round its enemy between blows.
    ///                   The game's attack cooldown starts as it would have for a swing. It ends
    ///                   leaving a chain hook (Close, or Launched after a launching kick) and the
    ///                   Brain looks at once, so a technique follows without a breath.
    ///   EXCHANGES       when two Flow fighters go at each other at the same moment, the chains
    ///                   meet: an exchange of blows clashing mid-way (FlowStrike's exchange).
    ///   KI SHOTS        a Flow fighter with a ranged attack fires a bolt of its tier's light (an
    ///                   archer a glowing arrow) instead of the game's projectile, on the game's
    ///                   own cadence (KiShot, KiBolts). Mages still cast their spells.
    ///   BLUR DASHES     a Flow fighter walking at an enemy a few tiles away blurs there in short
    ///                   hops (BlurDash: 3-9 tiles for a Master, 11 a Grandmaster, 14 a God);
    ///                   Experts do it now and then, in a single hop.
    ///   LUNGES          a melee master walking the last 1-3 tiles at its enemy doesn't walk them:
    ///                   the chain opens with a dash into reach (TryReach).
    ///   STANDOFFS       a melee master whose enemy it can't get to (it swims, flies, stands across
    ///                   water or behind a wall, or the master itself is stuck or swimming) fights
    ///                   on with ki shots on its own cadence instead of standing there (TryReach).
    ///
    /// Limits: at most 40 of these moves at once (12 off screen, where the game's own swings
    /// stand in), and never while the Brain still needs room (Moves.Count). Nothing costs mana;
    /// nothing is a trait. Every flow move can be cut short by a technique between its blows
    /// (never mid-blow, mid-flip or mid-blink: Move.Interruptible is false for those beats), so
    /// a technique flows straight out of a landed blow.
    ///
    /// Duels: a duelist fights in flow through all the free fighting between bouts; the next bout
    /// takes it over at the next blow (FreeFor trims a chain to the time left). It only stands
    /// ready when the bout begins this very moment (both free and near), never while the bout
    /// can't begin (the other is down, airborne, swimming or far). A unit a set piece holds is
    /// never struck (a master turns to another enemy, or waits its turn without a plain swing).
    ///
    /// Never stuck, never a lock: "standing ready" always runs out (Wait: 0.35-1 s of move time,
    /// shorter the higher the tier), then it acts whatever it waited for; it faces its enemy
    /// while it waits. A master whose last chain was on another master owes it its turn: they
    /// meet in an exchange, or it stands ready a moment for the answer, so the first to strike
    /// can't pin the other down for good. A melee master whose blows can't reach (water, a wall
    /// between them) or whose enemy is knocked up out of reach, flies, or rides a boat throws a
    /// ki shot instead of cutting the air (or swinging like a peasant).
    /// </summary>
    internal static class FlowStrikes
    {
        public const int MaxLive = 40;
        public const int MaxLiveOffScreen = 12;
        /// <summary>Total moves above which no flow move starts (the Brain stops thinking at 64).</summary>
        public const int MovesCeiling = 56;
        /// <summary>Off screen, total moves above which the game's own swing stands in (the Brain's off-screen cap is 28).</summary>
        public const int OffScreenCeiling = 20;

        private const string BlurTimer = "sakuga_flow_blur";
        private const string TurnTimer = "sakuga_flow_turn";

        /// <summary>Flow moves alive right now (strikes, partners, shots, dashes).</summary>
        internal static int Live;

        /// <summary>Dev statistics since the game started (Holds: runs of standing ready; HoldSec: their total move time).</summary>
        internal static int Chains, Exchanges, Joins, Shots, Spells, Dashes, Fallbacks, Holds, Lunges, Standoffs, Beats;
        internal static float HoldSec;

        /// <summary>The shortest chain (one blow and its recovery, move time): less free time than this and it stands ready.</summary>
        internal const float MinChain = 0.22f;

        internal enum Verdict
        {
            /// <summary>Let the game swing.</summary>
            Vanilla,
            /// <summary>A flow move took the swing's place.</summary>
            Done,
            /// <summary>Stand ready: no swing at all this time (a bout is due, the Brain looks first...).</summary>
            Hold
        }

        /// <summary>What a fighter last did, so it never plays the same chain twice running.</summary>
        internal sealed class Mem
        {
            public Actor A;
            public long Id;
            public string LastChain, PrevChain;
            public double LastExchange = -99;
            public double Seen;
            /// <summary>
            /// The Flow enemy its last plain chain was on (-1: none, or it ended in an exchange), when
            /// that chain began (world time) and ended (move time): that enemy gets its turn next.
            /// </summary>
            public long LastVictim = -1;
            public double ChainStart = -99, ChainEnd = -99;
            /// <summary>A run of "stand ready": since when (move time), the last one, and its frame.</summary>
            public double HoldSince = -99, HoldLast = -99;
            public int HoldFrame = -99;
            /// <summary>Since when (move time) it has stood still with its enemy out of reach; its last ki trace.</summary>
            public double StillSince = -99, StillSeen = -99, KiTrace = -99;
        }

        /// <summary>Move time since the game started (paused with the game, capped at high speeds, like the moves).</summary>
        internal static double MoveNow;

        private static readonly Dictionary<long, Mem> _mem = new Dictionary<long, Mem>();
        private static readonly Dictionary<string, float> _sound = new Dictionary<string, float>();
        private static readonly List<long> _drop = new List<long>();
        private static FieldInfo _duelFree, _duelWindow;
        private static bool _duelFreeLooked;
        private static int _frame = -1;
        private static float _prune = 10f;
        /// <summary>When (real time) a flow move last shook the camera.</summary>
        private static float _shakeAt = -99f;
        /// <summary>A wait ran out during this attack: it acts.</summary>
        private static bool _expired;
        /// <summary>This attacker's patience (1 Master, less for Grandmasters and Gods: the stronger, the fewer pauses).</summary>
        private static float _pace = 1f;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FighterTick += Tick;
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            Live = 0;
            _mem.Clear();
            _sound.Clear();
            _frame = -1;
            KiBolts.Clear();
        }

        // ------------------------------------------------------------ the swing --

        /// <summary>
        /// The game wants 'a' to attack 'target' (Actor.tryToAttack). A Flow fighter answers with a
        /// strike chain, an exchange, a ki shot (or its spell), or stands ready; everyone else swings.
        /// A Flow fighter never swings like a peasant: whatever can't be a chain becomes ki, or a
        /// moment of standing ready (facing its enemy, the attack's cooldown started).
        /// </summary>
        internal static Verdict Attack(Actor a, BaseSimObject target, bool checks)
        {
            _expired = false;
            if (a == null || target == null || !target.isActor())
            {
                return Verdict.Vanilla;
            }
            if (!Fighters.TryGet(a, out Fighter f) || f.Info == null || !f.Info.Flow)
            {
                return Verdict.Vanilla;
            }
            // the stronger, the fewer and shorter the pauses
            _pace = f.Tier >= Tier.God ? 0.55f : f.Tier == Tier.Grandmaster ? 0.75f : 0.9f;
            Actor v = target.a;
            if (v == null || !v.isAlive())
            {
                return Verdict.Vanilla;
            }
            bool ranged = a.hasRangeAttack();
            if (checks && (a.isInWaterAndCantAttack() || !a.isAttackPossible() || !a.isInAttackRange(v) || !ranged && v.position_height > 0f))
            {
                return Verdict.Vanilla;   // the game's own checks say no: let it say so
            }
            if (!v.hasHealth())
            {
                // a body already falling (a blow brought it to nothing, the game's death check comes
                // next): nothing to strike or shoot, it stands ready for its next enemy
                return Wait(a, 0.5f) ? Stand(a, v) : Beat(a, v);
            }
            // (a foe mid-blink is still the foe: Combat.IsFoe only says nothing can touch it this moment)
            bool blur = Moves.Intangible(v);
            if (blur ? !BlurFoe(a, v) : !Combat.IsFoe(a, v))
            {
                return Verdict.Vanilla;
            }
            if (!Moves.CanPerform(a) || Moves.Busy(a))
            {
                // caught in the water, it fights on with the ki of its palm (no drawing pose)
                if (!Swimming(a) || FreeFor(a) < 0.15f)
                {
                    return Verdict.Vanilla;
                }
                return !blur ? Bolt(a, v, "swims") : Wait(a, 0.5f) ? Stand(a, v) : Beat(a, v);
            }
            if (blur)
            {
                // a blur for a heartbeat: nothing to hit, it waits for it to land (one a set piece
                // holds that long: another enemy, if there is one)
                if (Director.InScene(v) && TurnAway(a, v, 6f))
                {
                    return Stand(a, v);
                }
                return Wait(a, 0.5f) ? Stand(a, v) : Beat(a, v);
            }
            if (Director.InScene(a) && Duels.InDuel(a))
            {
                // a duelist claimed for its duel's opening or a bout stands ready (a moment, never
                // for good), never a plain swing at its opponent
                return Wait(a, 1f) ? Stand(a, v) : Beat(a, v);
            }
            if (f.Linked && f.ThinkIn <= 0f && Wait(a, 0.5f))
            {
                return Stand(a, v);   // a chain window is open and the Brain hasn't looked yet: it goes first
            }
            return ranged ? Shoot(a, v) : Strike(a, v);
        }

        private static Verdict Strike(Actor a, Actor v)
        {
            bool held = Moves.TryGet(v, out Move vm);
            if (vm is Duelist || Director.InScene(v))
            {
                // never a blow on a unit a set piece holds (a duel's bout, a team technique, a God's
                // cinematic): another enemy, or it waits its turn (no swing at all)
                if (TurnAway(a, v, 6f))
                {
                    return Stand(a, v);
                }
                return Wait(a, 1f) ? Stand(a, v) : Beat(a, v);
            }
            if (held)
            {
                if (vm is FlowStrike fs && fs.Victim == a)
                {
                    // it is working on me: if it only just started, the two chains meet
                    if (fs.TryJoin(a))
                    {
                        Joins++;
                        return Verdict.Done;
                    }
                    // too late to answer it: take it, then answer
                    return Wait(a, 1f) ? Stand(a, v) : Beat(a, v);
                }
                if (vm is Puppet || vm is FlowPartner)
                {
                    // in someone's hands (an ally's combo, another master's exchange): it turns on
                    // another enemy near it if there is one; if not, it doesn't stand by: its ki joins in
                    if (TurnAway(a, v, 3f))
                    {
                        return Stand(a, v);
                    }
                    return Wait(a, 0.6f) ? Stand(a, v) : Bolt(a, v, "joins in");
                }
                if (vm is BlurDash || vm.Intangible)
                {
                    TurnAway(a, v, 3f);
                    return Wait(a, 0.5f) ? Stand(a, v) : Beat(a, v);
                }
            }
            // knocked up (it comes down in a moment), flying, or on a boat: out of a ground chain's
            // reach, a bolt of ki meets it
            bool airborne = !Combat.Grounded(v);
            float room = FreeFor(a);
            if (room < (airborne ? 0.15f : MinChain) && Wait(a, 0.8f))
            {
                // the duel's next bout (or its Final) begins this moment: it stands ready for it.
                // Only so long: a bout that can't begin (nothing to play) never freezes the pair
                return Stand(a, v);
            }
            if (airborne)
            {
                return Bolt(a, v, v.isFlying() ? "flier" : v.asset.is_boat ? "boat" : "airborne");
            }
            Mem mem = MemOf(a);
            bool meet = false;
            if (mem.LastVictim == v.Sid() && MoveNow - mem.ChainEnd < 0.6 && IsFlow(v) && !Answered(v, mem.ChainStart))
            {
                // its last chain was on this master, who hasn't had its turn since (it was pinned, then
                // knocked back): never a lock. They meet blade to blade if they can; otherwise it
                // stands ready a moment for the answer (a parry, a technique, a chain of its own)
                if (FlowStrike.CanMeet(a, v))
                {
                    meet = true;
                }
                else if (Wait(a, 0.35f))
                {
                    return Stand(a, v);
                }
            }
            if (!Budget(a.current_position))
            {
                Fallbacks++;
                return Verdict.Vanilla;
            }
            if (!FlowStrike.Begin(a, v, mem, meet, Mathf.Max(room, MinChain), out bool unreachable))
            {
                // across water, a wall or a cliff: its blows can't get there, its ki can
                return unreachable ? Bolt(a, v, "unreachable") : Beat(a, v);
            }
            a.startAttackCooldown();
            a.makeSoundAttack();   // its own battle cry, as with every swing
            Chains++;
            return Verdict.Done;
        }

        private static Verdict Shoot(Actor a, Actor v)
        {
            if (Director.InScene(v) || Moves.TryGet(v, out Move vm) && vm is Duelist)
            {
                // a set piece holds it: another enemy, or it waits its turn (no shot into a set piece)
                if (TurnAway(a, v, 6f))
                {
                    return Stand(a, v);
                }
                return Wait(a, 1f) ? Stand(a, v) : Beat(a, v);
            }
            if (FreeFor(a) < 0.15f && Wait(a, 0.8f))
            {
                return Stand(a, v);   // the duel's next bout (or its Final) begins this moment (only so long)
            }
            // a mage still casts its own spells now and then (the game would try one most times;
            // a master mostly fights with its ki)
            if (Random.value < 0.34f && KiShot.TryCast(a, v))
            {
                a.startAttackCooldown();
                Spells++;
                return Verdict.Done;
            }
            return Bolt(a, v, null);
        }

        /// <summary>A ki shot at 'v': drawn on screen (within the budget), else fired at once. 'why': the dev trace (null: none).</summary>
        private static Verdict Bolt(Actor a, Actor v, string why)
        {
            bool drawn = Moves.CanPerform(a) && !Moves.Busy(a) && Fx.OnScreen(a.current_position, 0.2f) && Budget(a.current_position) && KiShot.Begin(a, v);
            if (!drawn)
            {
                KiShot.FireNow(a, v);   // off screen, over budget or in the water: the same bolt, without the drawing pose
            }
            a.startAttackCooldown();
            a.makeSoundAttack();
            Shots++;
            if (why != null && Dev.On)
            {
                Mem m = MemOf(a);
                if (MoveNow - m.KiTrace > 1.5)
                {
                    m.KiTrace = MoveNow;
                    Dev.Trace("ki " + a.Sid() + " (" + why + ") at " + v.asset.id + "#" + v.Sid() + " "
                        + Vector2.Distance(a.current_position, v.current_position).ToString("F1") + " tiles");
                }
            }
            return Verdict.Done;
        }

        /// <summary>Swimming (a land unit in the water, free otherwise): its blows can't, its ki can.</summary>
        private static bool Swimming(Actor a)
        {
            try
            {
                return a.isAlive() && a.isInLiquid() && !a.isWaterCreature() && !a.isFlying() && !a.isInsideSomething() && !a.isInMagnet()
                    && !a.is_unconscious && !Moves.Busy(a) && !ControllableUnit.isControllingUnit(a);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Combat.IsFoe for a unit mid-blink: an enemy all the same, only out of touch for a heartbeat.</summary>
        private static bool BlurFoe(Actor a, Actor v)
        {
            if (a == null || v == null || v == a || !v.isAlive() || a.kingdom == null || v.kingdom == null || !a.areFoes(v))
            {
                return false;
            }
            if (!v.asset.can_be_killed_by_stuff || v.isInsideSomething() || v.isInMagnet())
            {
                return false;
            }
            return v.ai == null || v.ai.action == null || !v.ai.action.special_prevent_can_be_attacked;
        }

        /// <summary>Has 'v' acted since 'since' (world time): an attack of any kind, or a technique?</summary>
        private static bool Answered(Actor v, double since)
        {
            if (v.last_attack_timestamp >= since)
            {
                return true;
            }
            return Fighters.TryGet(v, out Fighter f) && f.LastStart >= since;
        }

        /// <summary>
        /// Stand ready this time? Yes, unless it has already stood 'max' seconds (move time, shorter
        /// the higher the tier) running: then it acts after all (and the count starts over).
        /// Nothing ever stands for good.
        /// </summary>
        private static bool Wait(Actor a, float max)
        {
            Mem m = MemOf(a);
            int frame = Time.frameCount;
            if (frame - m.HoldFrame > 3 && MoveNow - m.HoldLast > 0.25)
            {
                m.HoldSince = MoveNow;   // a new run
                Holds++;
            }
            else if (m.HoldLast > 0)
            {
                HoldSec += (float)(MoveNow - m.HoldLast);
            }
            if (_expired || MoveNow - m.HoldSince >= max * _pace)
            {
                // (once a wait has run out, nothing else makes it stand this time)
                _expired = true;
                m.HoldLast = -99;
                m.HoldFrame = -99;
                return false;
            }
            m.HoldLast = MoveNow;
            m.HoldFrame = frame;
            return true;
        }

        /// <summary>
        /// Its target is out of play for a moment: the nearest other free enemy within 'radius'
        /// tiles becomes its target. True if it found one.
        /// </summary>
        private static bool TurnAway(Actor a, Actor v, float radius)
        {
            if (!Fighters.TryGet(a, out Fighter f) || !f.Elapsed(TurnTimer, 0.3f))
            {
                return false;
            }
            f.Mark(TurnTimer);
            Actor best = null;
            float bd = radius * radius;
            foreach (Actor u in Combat.FoesAround(a, a.current_position, radius))
            {
                if (u == v || !Combat.Living(u) || Director.InScene(u) || Moves.Locked(u))
                {
                    continue;
                }
                float d = (u.current_position - a.current_position).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
            if (best == null)
            {
                return false;
            }
            a.setAttackTarget(best);
            return true;
        }

        /// <summary>Stands ready (no swing this time), facing its enemy.</summary>
        private static Verdict Stand(Actor a, Actor v)
        {
            Face(a, v);
            return Verdict.Hold;
        }

        /// <summary>
        /// Its wait ran out and there is still nothing a master would do to that enemy: no plain swing
        /// either. It stays ready for one attack's length (the cooldown starts), facing it.
        /// </summary>
        private static Verdict Beat(Actor a, Actor v)
        {
            a.startAttackCooldown();
            Beats++;
            Face(a, v);
            return Verdict.Hold;
        }

        private static void Face(Actor a, Actor v)
        {
            if (v != null && !Moves.Busy(a))
            {
                a.checkFlipAgainstTargetPosition(v.current_position);
            }
        }

        // ------------------------------------------------------------ queries --

        /// <summary>A Flow fighter (Master and up) that fights with the mod's moves only.</summary>
        internal static bool IsFlow(Actor a)
        {
            if (a == null)
            {
                return false;
            }
            if (Fighters.TryGet(a, out Fighter f))
            {
                return f.Info != null && f.Info.Flow;
            }
            TierInfo info = Tiers.Info(Tiers.Of(a));
            return info != null && info.Flow;
        }

        /// <summary>Is any system holding this fighter's techniques (a duel about to begin, the Final due)?</summary>
        internal static bool Held(Actor a)
        {
            List<Func<Actor, bool>> h = Hooks.HoldTechniques;
            for (int i = 0; i < h.Count; i++)
            {
                try
                {
                    if (h[i](a))
                    {
                        return true;
                    }
                }
                catch (Exception e)
                {
                    SakugaMod.Report("flow hold", e);
                }
            }
            return false;
        }

        /// <summary>Room for one more flow move here (on screen 40 at most, off screen 12, and the Brain keeps its room).</summary>
        internal static bool Budget(Vector2 at)
        {
            if (Live >= MaxLive || Moves.Count >= MovesCeiling)
            {
                return false;
            }
            if ((Live >= MaxLiveOffScreen || Moves.Count >= OffScreenCeiling) && !Fx.OnScreen(at, 0.15f))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// May this unit start a flow move lasting about 'need' seconds? Always, unless it is a
        /// duelist whose next bout begins sooner than that (FreeFor).
        /// </summary>
        internal static bool DuelFree(Actor a, float need)
        {
            return FreeFor(a) >= need;
        }

        /// <summary>
        /// Free for a flow move of about 'need' seconds: a duelist as its duel allows (FreeFor),
        /// anyone else unless a system holds it for a set piece about to begin (a duel's meeting,
        /// a staged situation). A duel's rationing of techniques is no reason to stand still.
        /// </summary>
        internal static bool Free(Actor a, float need)
        {
            return Duels.InDuel(a) ? FreeFor(a) >= need : !Held(a);
        }

        /// <summary>
        /// Seconds of flow a duelist has before its duel's next bout takes it (99: no duel, or the
        /// bout can't begin yet: the other is down, airborne, swimming, busy or far). Flow moves
        /// give way to the bout at their next blow; this is so a chain can end on its own first,
        /// and so a duelist only stands ready when the bout begins this very moment.
        /// </summary>
        internal static float FreeFor(Actor a)
        {
            Duel d = Duels.Of(a);
            if (d == null)
            {
                return 99f;
            }
            if (Director.InScene(a) || !d.Fought)
            {
                return 0f;   // its bout, or the opening: the duel has it
            }
            Actor o = d.OpponentOf(a);
            if (o == null || !o.isAlive() || !BoutReady(d, a) || !BoutReady(d, o))
            {
                return 99f;
            }
            float reach = d.Gods ? 13f : d.Flow ? 10f : 7.5f;
            if ((a.current_position - o.current_position).sqrMagnitude > reach * reach)
            {
                return 99f;   // the bout waits for them to close in (a blur dash does it)
            }
            return Mathf.Max(0f, FreeLeft(d));
        }

        /// <summary>Could the duel take this one into a bout now (Duel.Ready: free, or in a light move, not stunned, in no other set piece)?</summary>
        private static bool BoutReady(Duel d, Actor x)
        {
            if (!Moves.CanPerform(x) || x.hasStatusStunned())
            {
                return false;
            }
            object s = Director.SceneOf(x);
            return s == null || s == d;
        }

        /// <summary>
        /// Seconds of free fighting the duel has left before it wants its next bout (Duel._free).
        /// Once the free fighting is over and the bout keeps failing to begin (nothing to play just
        /// there, a claim refused), the duel opens a new window of free fighting (Duel._window)
        /// while it keeps trying: that window is the time there is, not the retry's beat.
        /// </summary>
        private static float FreeLeft(Duel d)
        {
            if (!_duelFreeLooked)
            {
                _duelFreeLooked = true;
                try
                {
                    _duelFree = AccessTools.Field(typeof(Duel), "_free");
                    _duelWindow = AccessTools.Field(typeof(Duel), "_window");
                }
                catch
                {
                    _duelFree = null;
                    _duelWindow = null;
                }
            }
            if (_duelFree == null)
            {
                return 99f;
            }
            try
            {
                float free = (float)_duelFree.GetValue(d);
                float window = _duelWindow != null ? (float)_duelWindow.GetValue(d) : free;
                if (window > 0f && free < window - 0.05f)
                {
                    return window;
                }
                // the bout is due but its last try came to nothing (no exchange fits just there, a
                // claim refused): the retry is no reason to stand still, a breath of flow it is
                return window <= 0f && free > 0.1f ? 0.5f : free;
            }
            catch
            {
                return 99f;
            }
        }

        /// <summary>
        /// Between two duelists, the duel's pacing takes a move's blows for a technique's (Duel.TechScale);
        /// a flow strike stands in for a plain swing (Duel.SwingScale): this evens it out.
        /// </summary>
        internal static float Comp(Actor a, Actor v)
        {
            Duel d = Duels.Of(v);
            if (d == null || a == null)
            {
                return 1f;
            }
            Actor o = d.OpponentOf(v);
            if (o == null || o.Sid() != a.Sid())
            {
                return 1f;
            }
            return Mathf.Clamp(d.SwingScale / Mathf.Max(0.05f, d.TechScale), 1f, 3f);
        }

        /// <summary>
        /// The damage (a multiple of the attacker's damage stat) of 'seconds' (move time) of flow:
        /// what its own swings would have dealt in that time (attack speed, the game's average roll),
        /// in game time (Speed). The tier's Might comes on top in Combat.Hit, so masters hit harder
        /// than their swings did.
        /// </summary>
        internal static float Total(Actor a, float seconds)
        {
            float cd = 0.5f;
            try
            {
                cd = a.getAttackCooldown();
            }
            catch
            {
                // a unit being recycled: the default stands
            }
            float aps = 1f / Mathf.Max(0.08f, cd);
            float r = Speed();
            return Mathf.Clamp(seconds * r * aps * 0.8f, 0.8f, 3.6f * r);
        }

        /// <summary>
        /// Game seconds per second of move time at this game speed: 1 up to x3, then the move clock
        /// (capped at three frames' worth a frame) falls behind the world: x5 1.7, x10 3.3, x20 6.7,
        /// x40 13.3. A flow move stands in for swings that come in game time, so what it holds in
        /// game time (its enemy's pin, the damage of the swings it replaces) is scaled by this.
        /// </summary>
        internal static float Speed()
        {
            MapBox w = World.world;
            if (w == null || Config.time_scale_asset == null)
            {
                return 1f;
            }
            float e = w.elapsed;
            float step = Mathf.Min(e, Time.fixedDeltaTime * 3f);
            if (e <= 0f || step <= 0f)
            {
                return 1f;
            }
            return Mathf.Max(1f, e * Mathf.Max(1, Config.time_scale_asset.ticks) / step);
        }

        /// <summary>
        /// A flow move's camera shake (a heavy finisher, a clash's end): never over a shake already
        /// running (a technique's big one is never cut short by it), and at most one every half
        /// second across all flow moves, so a war on screen doesn't keep the camera trembling.
        /// </summary>
        internal static void Shake(Vector2 at, float duration, float strength, float k)
        {
            if (World.world == null || World.world._shake_timer > 0f || !Fx.OnScreen(at, 0.05f))
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now - _shakeAt < 0.5f)
            {
                return;
            }
            _shakeAt = now;
            Kit.Shake(at, duration, strength, k);
        }

        internal static bool Armed(Actor a)
        {
            try
            {
                return a.hasWeapon() && !a.hasRangeAttack();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A unit's drawn height in world units (its sprite).</summary>
        internal static float SpriteH(Actor a)
        {
            Sprite s = Fx.UnitSprite(a);
            return Mathf.Max(0.2f, (s != null ? s.rect.height : 10f) * a.actor_scale);
        }

        /// <summary>A unit's size relative to a human.</summary>
        internal static float KOf(Actor a)
        {
            return Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        internal static float PxOf(Actor a)
        {
            return Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
        }

        /// <summary>A point on another unit's body, 'frac' of the way up its sprite (where it is drawn right now).</summary>
        internal static Vector2 Body(Actor v, float frac)
        {
            return Feet(v) + new Vector2(0f, SpriteH(v) * frac);
        }

        /// <summary>Where another unit's feet are drawn right now (its pose while it performs).</summary>
        internal static Vector2 Feet(Actor v)
        {
            if (Moves.TryGet(v, out Move m))
            {
                return m.Pose.Ground + new Vector2(0f, m.Pose.Height);
            }
            return v.is_visible ? (Vector2)v.cur_transform_position : v.current_position + new Vector2(0f, v.position_height);
        }

        /// <summary>A sound, but the same one not more than ~30 times a second across all flow moves.</summary>
        internal static void Sound(string id, WorldTile tile, float gap = 0.035f)
        {
            float now = Time.unscaledTime;
            if (_sound.TryGetValue(id, out float at) && now - at < gap)
            {
                return;
            }
            _sound[id] = now;
            Anim.Sound(id, tile);
        }

        internal static Mem MemOf(Actor a)
        {
            long id = a.Sid();
            if (!_mem.TryGetValue(id, out Mem m) || m.A != a)
            {
                m = new Mem { A = a, Id = id };
                _mem[id] = m;
            }
            m.Seen = Fighter.Now;
            return m;
        }

        // -------------------------------------------------------------- after --

        /// <summary>
        /// A flow move ended well: the Brain looks right away (ThinkIn 0), and the chain hook it
        /// leaves is offered when the fighter has a technique ready that takes it. Not always: an
        /// open link narrows the Brain to the techniques that accept it, and a crowd may call for
        /// something else.
        /// </summary>
        internal static void After(Actor a, Link link, Actor target)
        {
            if (a == null || !a.isAlive() || !Fighters.TryGet(a, out Fighter f))
            {
                return;
            }
            f.ThinkIn = 0f;
            if (link == Link.None || target == null || !target.isAlive() || f.Linked)
            {
                return;
            }
            if (Random.value >= 0.6f || !Chainable(f, link))
            {
                return;
            }
            int max = f.Info != null ? f.Info.MaxChain : 2;
            if (f.Chain >= max)
            {
                // the combo it was in is spent: these strikes open a new one
                f.Chain = 0;
                f.Heat = 0f;
            }
            f.Link = link;
            f.LinkTarget = target;
            f.LinkUntil = Fighter.Now + 0.4;
        }

        /// <summary>Does the fighter know a technique that is ready, affordable, and takes this link?</summary>
        private static bool Chainable(Fighter f, Link link)
        {
            IReadOnlyList<Technique> all = Techniques.All;
            for (int i = 0; i < all.Count; i++)
            {
                Technique t = all[i];
                if (t.GodAbility || t.Reactive || t.Team && !Settings.TeamTechniques || !t.Accepts(link))
                {
                    continue;
                }
                int level = Techniques.Level(f.A, t, f.Tier);
                if (level > 0 && f.Ready(t) && Mana.Has(f.A, t, level, true))
                {
                    return true;
                }
            }
            return false;
        }

        // --------------------------------------------------- blur dashes, reach --

        private static void Tick(Fighter f, float dt)
        {
            try
            {
                if (Time.frameCount != _frame)
                {
                    _frame = Time.frameCount;
                    Prune(dt);
                }
                if (dt <= 0f)
                {
                    return;
                }
                if (f.Tier >= Tier.Expert)
                {
                    TryBlur(f, dt);
                }
                if (f.Info != null && f.Info.Flow)
                {
                    TryReach(f, dt);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("flow tick", e);
            }
        }

        /// <summary>How far a blur dash takes it (Master 9 tiles, Grandmaster 11, God 14; Experts 9 in one hop now and then).</summary>
        private static float BlurRange(Tier t)
        {
            return t >= Tier.God ? 14f : t == Tier.Grandmaster ? 11f : 9f;
        }

        /// <summary>
        /// A fighter walking at an enemy a few tiles away, the Brain having just looked and found
        /// nothing better: it blurs the distance instead of walking it.
        /// </summary>
        private static void TryBlur(Fighter f, float dt)
        {
            Actor a = f.A;
            if (!a.is_moving || !a.isFighting() || !a.has_attack_target || a.attack_target == null || !a.attack_target.isActor())
            {
                return;
            }
            if (f.ThinkIn - dt <= 0f)
            {
                return;   // the Brain looks this frame (or a chain is pending): its call
            }
            float cooldown = f.Tier >= Tier.God ? 0.6f : f.Tier == Tier.Grandmaster ? 0.7f : f.Tier == Tier.Master ? 0.8f : 1.3f;
            if (!f.Elapsed(BlurTimer, cooldown))
            {
                return;
            }
            Actor v = a.attack_target.a;
            if (v == null || !Combat.Living(v) || !Combat.IsFoe(a, v) || !Combat.Grounded(v) || v.isFlying())
            {
                return;
            }
            Vector2 p = a.current_position;
            Vector2 to = v.current_position - p;
            float d = to.magnitude;
            if (d < 3f || d > BlurRange(f.Tier) || !a.hasMeleeAttack())
            {
                return;
            }
            Vector2 step = a.next_step_position - p;
            if (step.sqrMagnitude < 1e-6f || Vector2.Dot(step.normalized, to / d) < 0.55f)
            {
                return;   // not walking at it (going round something, or away)
            }
            if (!Moves.CanPerform(a) || Moves.Busy(a) || Director.InScene(a) || !Free(a, 0.5f) || !Budget(p))
            {
                return;
            }
            f.Mark(BlurTimer);
            if (f.Tier == Tier.Expert && Random.value >= 0.3f)
            {
                return;   // an Expert only now and then
            }
            if (BlurDash.Begin(a, v, f.Tier))
            {
                Dashes++;
            }
        }

        /// <summary>
        /// A melee master whose enemy is out of its reach. Walking the last 1-3 tiles at it: it
        /// doesn't walk them, its chain opens with a dash into reach (a lunge). Unable to get to it
        /// (it swims, flies or rides a boat, water or a wall lies between them, the master is stuck
        /// or swimming itself): it doesn't stand there, it fights on with ki shots on the game's
        /// own cadence (a standoff), up to 6 tiles (a Grandmaster 7, a God 9).
        /// </summary>
        private static void TryReach(Fighter f, float dt)
        {
            Actor a = f.A;
            if (!a.has_attack_target || a.attack_target == null || !a.attack_target.isActor())
            {
                return;
            }
            Actor v = a.attack_target.a;
            if (v == null || v == a || !Combat.Living(v) || !a.isFighting() || !a.hasMeleeAttack() || a.hasRangeAttack())
            {
                return;   // (a shooter walks into its range and shoots: the game's own loop)
            }
            // how long it has stood still with its enemy out of reach (any step, or the enemy in
            // reach, or a gap in watching it, starts the count over)
            bool inReach = a.isInAttackRange(v);
            Mem mem = MemOf(a);
            if (a.is_moving || inReach || MoveNow - mem.StillSeen > 0.15)
            {
                mem.StillSince = MoveNow;
            }
            mem.StillSeen = MoveNow;
            if (inReach || a.attack_timer > 0f || f.ThinkIn - dt <= 0f)
            {
                return;   // in reach (the game asks for the swing: Attack), pinned, cooling down, or the Brain looks first
            }
            if (!Combat.IsFoe(a, v) || Director.InScene(v) || a.hasStatusStunned() || ControllableUnit.isControllingUnit(a))
            {
                return;
            }
            bool free = Moves.CanPerform(a) && !Moves.Busy(a);
            bool swims = !free && Swimming(a);
            if (!free && !swims || Director.InScene(a))
            {
                return;
            }
            Vector2 p = a.current_position;
            Vector2 vp = v.current_position;
            float d = Vector2.Distance(p, vp);
            float k = KOf(a);
            float range = (f.Tier >= Tier.God ? 9f : f.Tier == Tier.Grandmaster ? 7f : 6f) * Mathf.Lerp(1f, k, 0.5f);
            if (d > Mathf.Max(range, 3.2f * k))
            {
                return;   // too far for a lunge or a standoff: it walks on (no path probed for nothing)
            }
            bool blocked = !Combat.Grounded(v) || v.isInLiquid() || !Combat.ClearPath(p, vp);
            if (free && !blocked && a.is_moving && d <= 3.2f * k)
            {
                // the last steps at it: a lunge (the chain's first beat dashes into reach)
                Vector2 step = a.next_step_position - p;
                if (step.sqrMagnitude > 1e-6f && Vector2.Dot(step.normalized, (vp - p) / Mathf.Max(0.01f, d)) >= 0.55f)
                {
                    _pace = f.Tier >= Tier.God ? 0.55f : f.Tier == Tier.Grandmaster ? 0.75f : 0.9f;
                    _expired = false;
                    if (Strike(a, v) == Verdict.Done)
                    {
                        Lunges++;
                    }
                    _expired = false;
                }
                return;
            }
            bool stuck = !a.is_moving && MoveNow - mem.StillSince > 0.35;
            if (d > range || !(blocked || stuck || swims))
            {
                return;
            }
            if (Moves.TryGet(v, out Move vm) && (vm is Duelist || vm is Puppet || vm is FlowPartner || vm.Intangible) || !Free(a, 0.2f))
            {
                return;   // (in someone's hands, a set piece's or a blur: not a target for ki from afar)
            }
            Standoffs++;
            Bolt(a, v, swims ? "swims" : v.isFlying() ? "flier" : v.isInLiquid() ? "in the water" : blocked ? "cut off" : "stuck");
        }

        // -------------------------------------------------------------- frame --

        /// <summary>Every frame after the world's step: ki bolts in flight.</summary>
        internal static void Frame()
        {
            if (World.world == null)
            {
                return;
            }
            MoveNow += Clock.Dt;
            KiBolts.Tick(Clock.Dt);
        }

        private static void Prune(float dt)
        {
            _prune -= dt;
            if (_prune > 0f)
            {
                return;
            }
            _prune = 10f;
            double now = Fighter.Now;
            _drop.Clear();
            foreach (KeyValuePair<long, Mem> kv in _mem)
            {
                Mem m = kv.Value;
                if (m.A == null || m.A.Sid() != m.Id || !m.A.isAlive() || now - m.Seen > 60.0)
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (long id in _drop)
            {
                _mem.Remove(id);
            }
            _drop.Clear();
            if (Dev.On)
            {
                Dev.Trace("flow: chains " + Chains + " exchanges " + Exchanges + " joins " + Joins + " lunges " + Lunges + " shots " + Shots
                    + " standoffs " + Standoffs + " spells " + Spells + " dashes " + Dashes + " fallbacks " + Fallbacks + " holds " + Holds
                    + " (" + HoldSec.ToString("F1") + " s) beats " + Beats + " live " + Live + " bolts " + KiBolts.Count);
            }
        }
    }

    // ------------------------------------------------------------------ patches --

    /// <summary>A Flow fighter's swing becomes a strike chain, an exchange or a ki shot.</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.tryToAttack))]
    internal static class FlowAttackPatch
    {
        private static bool Prefix(Actor __instance, BaseSimObject pTarget, bool pDoChecks, ref bool __result)
        {
            try
            {
                switch (FlowStrikes.Attack(__instance, pTarget, pDoChecks))
                {
                    case FlowStrikes.Verdict.Done:
                    case FlowStrikes.Verdict.Hold:
                        __result = true;
                        return false;
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("flow attack", e);
            }
            return true;
        }
    }

    /// <summary>After the world's step and the mod's own frame: ki bolts fly on.</summary>
    [HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    internal static class FlowFramePatch
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix()
        {
            try
            {
                FlowStrikes.Frame();
            }
            catch (Exception e)
            {
                SakugaMod.Report("flow frame", e);
            }
        }
    }
}
