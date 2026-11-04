using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// RESCUE. A weak one of its side (untiered, a Novice or an Adept) is hit down to 15% health or
    /// less, and lives: the nearest free Master or better of the same side within 8 tiles that may fight
    /// the attacker (not in a duel, not facing a tiered enemy of its own strength; one between flow
    /// strikes is free, the rescue takes over from them) comes to the rescue. A gold "!" over it, its attack target set on the attacker, and
    /// it Flash-Steps in if it knows Flash Step (the level it knows; ready and affordable: mana and the
    /// cooldown are paid as usual) and the blink can reach the attacker itself, otherwise it charges
    /// (speed lines, a kick of dust) and keeps going
    /// for the attacker for 3 s, its Brain looking for its way in at once (no pause between
    /// techniques to wait out). One rescue per victim every 10 s, each rescuer once every 10 s.
    ///
    /// Noticed in Hooks.BeforeDamage (a few comparisons, main thread only), answered next frame.
    /// </summary>
    internal static class SitRescue
    {
        private const float Reach = 8f;
        private const float RescuerCooldown = 10f;
        private const float VictimCooldown = 10f;

        private struct Call
        {
            public Actor Victim;
            public long VictimId;
            public Actor Attacker;
            public long AttackerId;
            public double When;
        }

        private sealed class Run
        {
            public Actor A;
            public long Id;
            public Actor Target;
            public long TargetId;
            public double Until, NextSteer;
        }

        private static readonly List<Call> _calls = new List<Call>();
        private static readonly List<Run> _runs = new List<Run>();
        private static readonly Dictionary<long, double> _victims = new Dictionary<long, double>();
        private static readonly Dictionary<long, double> _rescuers = new Dictionary<long, double>();
        private static Technique _flash;
        private static bool _flashLooked;
        private static Ctx _ctx;

        /// <summary>Rescues since the game started (dev log).</summary>
        internal static int Started;

        // ---------------------------------------------------------------- notice --

        /// <summary>Hooks.BeforeDamage: never cancels the hit, only notices a weak ally in mortal danger.</summary>
        public static bool OnBeforeDamage(Actor victim, float damage, AttackType type, BaseSimObject attacker)
        {
            if (!Situations.Enabled || Situations.Masters.Count == 0 || attacker == null || _calls.Count >= 6 || victim == null)
            {
                return false;
            }
            try
            {
                if (!Situations.MainThread || !attacker.isActor() || attacker.a == null || !victim.isAlive())
                {
                    return false;
                }
                int max = victim.getMaxHealth();
                float after = victim.getHealth() - damage;
                if (max <= 0 || after <= 0f || after > 0.15f * max)
                {
                    return false;   // not in danger, or beyond saving
                }
                if (Tiers.Of(victim) > Tier.Adept)
                {
                    return false;
                }
                long id = victim.Sid();
                if (_victims.TryGetValue(id, out double at) && Fighter.Now < at)
                {
                    return false;
                }
                _victims[id] = Fighter.Now + 1.0;   // asked; a real rescue extends it
                _calls.Add(new Call { Victim = victim, VictimId = id, Attacker = attacker.a, AttackerId = attacker.a.Sid(), When = Fighter.Now });
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] rescue notice: " + e);
            }
            return false;
        }

        // --------------------------------------------------------------- answer --

        public static void Process(double now)
        {
            if (_calls.Count == 0)
            {
                return;
            }
            for (int i = 0; i < _calls.Count; i++)
            {
                if (now - _calls[i].When <= 1.0)
                {
                    Answer(_calls[i], now);
                }
            }
            _calls.Clear();
        }

        private static void Answer(Call c, double now)
        {
            Actor v = c.Victim, atk = c.Attacker;
            // (Combat.Living: a technique this frame may already have finished one of them)
            if (v == null || v.Sid() != c.VictimId || !Combat.Living(v) || atk == null || atk.Sid() != c.AttackerId || !Combat.Living(atk))
            {
                return;
            }
            if (!Combat.IsFoe(v, atk) && !Combat.IsFoe(atk, v))
            {
                return;
            }
            Fighter best = null;
            float bestD = Reach * Reach;
            List<Fighter> masters = Situations.Masters;
            for (int i = 0; i < masters.Count; i++)
            {
                Fighter f = masters[i];
                Actor r = f.A;
                if (!f.Valid || r == v || r == atk)
                {
                    continue;
                }
                float d2 = (r.current_position - v.current_position).sqrMagnitude;
                if (d2 > bestD)
                {
                    continue;
                }
                if (_rescuers.TryGetValue(f.Id, out double at) && now < at)
                {
                    continue;
                }
                if (!Combat.IsAlly(v, r) || !Combat.IsFoe(r, atk) || Situations.Engaged(r) || SitLastStand.In(r) || !Situations.Free(r))
                {
                    continue;
                }
                // a master between flow strikes is free; one cutting down weaker tiered enemies may
                // break off to save a dying ally (in a war of tiered fighters all of them are at
                // grips with someone); never one in a duel or facing its match
                Actor duel = Duels.Opponent(r);
                Actor foe = Situations.TieredFoe(r);
                if (duel != null && duel != atk || foe != null && foe != atk && Tiers.Of(foe) >= f.Tier)
                {
                    continue;
                }
                best = f;
                bestD = d2;
            }
            if (best == null)
            {
                return;
            }
            Go(best, v, atk, now);
        }

        private static void Go(Fighter f, Actor v, Actor atk, double now)
        {
            Actor r = f.A;
            if (!Situations.Steer(r, atk, false))
            {
                return;
            }
            _rescuers[f.Id] = now + RescuerCooldown;
            _victims[v.Sid()] = now + VictimCooldown;
            Started++;
            SitFx.Mark(r, SitFx.Bang, SitFx.Gold, 1f, 3f);
            Anim.Sound("event:/SFX/UI/HintHighlight", r.current_tile);
            Progression.AddSxp(r, 4f);
            bool stepped = FlashIn(f, atk);
            if (!stepped)
            {
                Charge(r, atk);
                Situations.Resume(r);   // its Brain looks for a way in right now (a Master+ doesn't wait out its pause)
            }
            Run run = new Run { A = r, Id = f.Id, Target = atk, TargetId = atk.Sid(), Until = now + 3.0, NextSteer = now + 0.5 };
            _runs.Add(run);
            Situations.Claim(r, run);
            Dev.Watch(Vector2.Lerp(r.current_position, atk.current_position, 0.5f) + new Vector2(0f, 1.5f));
            if (Dev.On)
            {
                Dev.Trace("situation rescue: " + r.asset.id + "#" + f.Id + " (" + f.Tier + ") for " + v.asset.id + "#" + v.Sid()
                    + " against " + atk.asset.id + "#" + atk.Sid() + (stepped ? " (flash step)" : " (charge)"));
            }
        }

        /// <summary>
        /// Flash Step onto the attacker, if it knows it and can afford it right now, and its Brain
        /// would not hold its techniques (its duel's pacing, a God's hold, the move budget).
        /// </summary>
        private static bool FlashIn(Fighter f, Actor atk)
        {
            if (!_flashLooked)
            {
                _flashLooked = true;
                _flash = Techniques.Get("flash_step");
            }
            Technique t = _flash;
            Actor r = f.A;
            if (t == null || !Moves.CanPerform(r) || Situations.TechHeld(r))
            {
                return false;
            }
            int level = Techniques.Level(r, t, f.Tier);
            if (level <= 0 || !f.Ready(t) || !Mana.Has(r, t, level, false))
            {
                return false;
            }
            if (Vector2.Distance(r.current_position, atk.current_position) < 1.6f)
            {
                return false;   // already on it
            }
            // planned as a forced Flash Step onto the attacker (Brain.Force's way: the "is it worth
            // it" gates relaxed, never the safety ones), but taken only if it goes for the attacker:
            // when it can't reach that one (mid-technique, in the air, a blur, no way through) a
            // forced plan settles for the nearest other enemy, and the rescuer would blink away
            // from the one killing its ally. Then it runs at the attacker instead.
            _ctx ??= new Ctx();
            Ctx c = _ctx;
            c.Build(f);
            c.Forced = true;
            c.ForceTarget = atk;
            c.Target = atk;
            if (!c.Enemies.Contains(atk))
            {
                c.Enemies.Insert(0, atk);
                c.EnemyDist.Insert(0, c.Dist(atk));
            }
            Plan p;
            try
            {
                p = t.Plan(c, level);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] rescue plan " + t.Id + ": " + e);
                p = null;
            }
            if (p == null || p.Target != atk)
            {
                return false;
            }
            p.Tech = t;
            p.Level = level;
            p.Linked = false;
            return Brain.Execute(f, p);   // (mana and the cooldown are paid as usual)
        }

        /// <summary>No blink: it just runs at them, fast (speed lines streaming back, dust at its heels).</summary>
        private static void Charge(Actor r, Actor atk)
        {
            Vector2 feet = r.current_position;
            if (!Kit.Visible(feet) || !r.is_visible)
            {
                return;
            }
            float px = SitFx.PxOf(r);
            Vector2 dir = atk.current_position - feet;
            Fx.LandingPuff(feet, px, r.current_tile);
            Kit.SpeedLines(SitFx.Body(r) - dir.normalized * 3f * px, dir, px, 6, 4f, 0.18f, Fx.WithA(SitFx.GoldPale, 0.8f));
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>
        /// Staging (Situations.Force): the strongest of this unit's side (a Master if it has one) comes
        /// to the rescue of a weaker ally within 8 tiles of it once an enemy is on that ally (within 2
        /// tiles) and the rescuer is at least 2 tiles from that enemy, so the rush reads (at the end of
        /// the wait: any ally with an enemy within 8 tiles).
        /// </summary>
        internal static Situations.Stage Stage(Actor a, bool last, out string msg)
        {
            Actor r = Situations.Pick(a, Tier.Master);
            if (r == null)
            {
                msg = "rescue: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(r);
            string who = r.asset.id + "#" + r.Sid();
            if (Situations.Engaged(r) || !Situations.Free(r))
            {
                msg = "rescue: " + who + " is busy";
                return Situations.Stage.Wait;
            }
            Tier tr = Tiers.Of(r);
            Actor bestV = null, bestAtk = null;
            float bestD = float.MaxValue;
            Situations.Around(r.current_position, Reach, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (v == r || v.isBaby() || !Combat.IsAlly(r, v) || Tiers.Of(v) >= tr || Moves.TryGet(v, out Move vm) && !(vm is Puppet))
                {
                    continue;
                }
                Actor atk = Situations.Nearest(v, last ? 8f : 2f, true, u => Combat.IsFoe(r, u) && Situations.Threat(u));
                if (atk == null)
                {
                    continue;
                }
                float gap = (atk.current_position - r.current_position).sqrMagnitude;
                if (!last && gap < 4f)
                {
                    continue;   // already on it: no rush to see
                }
                float d2 = (v.current_position - r.current_position).sqrMagnitude + Combat.HpPct(v) * 9f;
                if (d2 < bestD)
                {
                    bestD = d2;
                    bestV = v;
                    bestAtk = atk;
                }
            }
            _near.Clear();
            if (bestV == null)
            {
                msg = "rescue: no weaker ally of " + who + " within 8 tiles has an enemy on it yet";
                return Situations.Stage.Wait;
            }
            int before = Started;
            Situations.Show(Vector2.Lerp(r.current_position, bestAtk.current_position, 0.5f) + new Vector2(0f, 1.5f));
            Go(Fighters.Get(r), bestV, bestAtk, Fighter.Now);
            if (Started == before)
            {
                msg = "rescue: " + who + " can't go for " + bestAtk.asset.id + "#" + bestAtk.Sid();
                return Situations.Stage.Wait;
            }
            msg = "rescue: " + who + " (" + tr + ") for " + bestV.asset.id + "#" + bestV.Sid() + " against " + bestAtk.asset.id + "#" + bestAtk.Sid();
            return Situations.Stage.Done;
        }

        // ---------------------------------------------------------------- upkeep --

        public static void Update(double now)
        {
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                Run run = _runs[i];
                bool alive = run.A != null && run.A.Sid() == run.Id && run.A.isAlive();
                bool target = run.Target != null && run.Target.Sid() == run.TargetId && run.Target.isAlive();
                if (!alive || !target || now > run.Until)
                {
                    _runs.RemoveAt(i);
                    Situations.Unclaim(run.Id, run);
                    continue;
                }
                if (now >= run.NextSteer)
                {
                    run.NextSteer = now + 0.5;
                    Situations.Steer(run.A, run.Target, true);
                }
            }
        }

        public static void Prune(double now)
        {
            Drop(_victims, now);
            Drop(_rescuers, now);
        }

        private static void Drop(Dictionary<long, double> d, double now)
        {
            List<long> old = null;
            foreach (KeyValuePair<long, double> kv in d)
            {
                if (kv.Value < now)
                {
                    (old ??= new List<long>()).Add(kv.Key);
                }
            }
            if (old != null)
            {
                foreach (long id in old)
                {
                    d.Remove(id);
                }
            }
        }

        public static void Clear()
        {
            foreach (Run run in _runs)
            {
                Situations.Unclaim(run.Id, run);
            }
            _calls.Clear();
            _runs.Clear();
            _victims.Clear();
            _rescuers.Clear();
            _flashLooked = false;
            _flash = null;
        }
    }
}
