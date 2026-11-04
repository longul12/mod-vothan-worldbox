using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TACTICS: what the Brain adds to every technique's own plan score, so a fighter answers what
    /// is in front of it instead of playing its favourite move. Shape() multiplies the plan's score by:
    ///
    ///   PERSONA     its taste for the technique's Forms (a brawler likes hooks and grabs, an
    ///               out-fighter shots and lines; every unit has quirks of its own), its patience
    ///               (a counter-puncher waits for the foe to commit), its whim
    ///   PREY        a technique made for this creature (x1.5, a hunter x1.8); Hunter's Way
    ///               techniques are never used on what they are not made for (a Building plan aimed
    ///               at a point where one stands is made for it)
    ///   SCALE       in proportion to the threat (Threat.Fit): no ultimate on a chicken, a flick
    ///               for a dragon when one is a God, the big guns for an equal or an army
    ///   ANSWERS     the foe is doing what this technique answers (x2.2); it keeps doing it (its
    ///               habits in the fighter's notes: readied)
    ///   FOE STATE   charging: interrupts x3.2, blink-ins and dashes x1.7, a wind-up of its own
    ///               x0.5; guarding: grabs and throws x2, kicks x1.5, feints x1.4, plain blows x0.6;
    ///               airborne: anti-air and aerials x1.8, ground blows x0.4; down: overheads and
    ///               grabs x1.35; recovering: punish (fast blows x1.6); a shooter at range: close
    ///               in (dash, blink x1.5) or out-shoot it; its attack aimed at us: evade/counter x1.4
    ///   MEMORY      avoid what this foe answered before, lean on what landed, show it less of what
    ///               it has already seen, never the same technique three times running on one foe
    ///               (unless it is the answer right now)
    ///   DANGER      winding up (Charge) with quick enemies close, the ones who broke a charge
    ///               before, Phantom Step walkers and interrupters, weighed by its risk-taking
    ///   SITUATION   surrounded: areas; outnumbered and hurt: a way out; its last stand: the big
    ///               ones (a berserker's fury); allies close: team techniques; water near: no cracks
    ///   ECONOMY     the heavy and ultimate ones keep a mana reserve unless the threat is real
    /// </summary>
    internal static class Tactics
    {
        /// <summary>Plans shaped, and of those, shaped up by an answer to what the foe is doing (dev statistics).</summary>
        internal static int Shaped, Answering, Interrupting;

        public static float Shape(Fighter f, Ctx c, Technique t, int level, Plan p)
        {
            Shaped++;
            Form forms = t.FormsAt(level);
            Persona per = c.Persona ?? f.Persona;
            // a plan for a building (Prey Building, aimed at a point with an enemy's house standing there) is
            // made for it; with no unit for a target it is aimed at no unit (not at the fighter's foe)
            bool building = (t.Prey & Prey.Building) != 0 && Preys.BuildingAt(p.Point, c.Self);
            Actor v = building && p.Target == null ? null : p.Target ?? c.Focus;
            float k = per.Affinity(forms);

            // prey: made for this creature?
            if (t.Prey != Prey.None)
            {
                Prey of = (v != null ? Preys.Of(v) : Prey.None) | (building ? Prey.Building : Prey.None);
                if ((of & t.Prey) != 0)
                {
                    k *= per.Is(Style.Hunter) ? 1.8f : 1.5f;
                }
                else if (t.School == School.HuntersWay)
                {
                    return 0f;
                }
                else
                {
                    k *= 0.7f;
                }
            }

            // scale: in proportion to the threat (an area plan against everyone it will catch; a tower or
            // a town alone is worth a full blow, never only a flick; aimed at a unit, the unit's threat)
            float threat = PlanThreat(c, p, v);
            if (building && v == null)
            {
                threat = Mathf.Max(threat, 1f);
            }
            k *= Threat.Fit(t.ScaleAt(level), Threat.Ideal(threat, c.HpPct));

            if (v != null)
            {
                FoeState s = c.State(v);
                bool answering = t.Answers != Form.None && (t.Answers & s.Doing) != 0;
                if (answering)
                {
                    k *= 2.2f;
                    Answering++;
                }
                if (s.Charging)
                {
                    if ((forms & Form.Interrupt) != 0)
                    {
                        k *= 3.2f;
                        Interrupting++;
                    }
                    else if ((forms & (Form.Dash | Form.Teleport)) != 0)
                    {
                        k *= 1.7f;
                    }
                    else if ((forms & Form.Fast) != 0)
                    {
                        k *= 1.4f;
                    }
                    if ((forms & Form.Charge) != 0 && (forms & Form.Beam) == 0)
                    {
                        k *= 0.5f;   // never wind up into a wind-up (a beam may answer a beam: they clash)
                    }
                }
                if (s.Guarding)
                {
                    if ((forms & (Form.Grab | Form.Throw)) != 0)
                    {
                        k *= 2f;
                    }
                    else if ((forms & Form.Kick) != 0)
                    {
                        k *= 1.5f;   // they curl up: kick them
                    }
                    else if ((forms & Form.Feint) != 0)
                    {
                        k *= 1.4f;
                    }
                    else if ((forms & (Form.Strike | Form.Shot)) != 0)
                    {
                        k *= 0.6f;
                    }
                }
                else if (!s.Busy && !s.Down && (forms & (Form.Feint | Form.Area | Form.Grab)) != 0 && c.Guard(v) == 1)
                {
                    k *= 1.2f;   // a foe on guard: bait it, or hit it with what it can't parry
                }
                if (s.Airborne)
                {
                    if ((forms & (Form.AntiAir | Form.Aerial)) != 0)
                    {
                        k *= 1.8f;
                    }
                    else if ((forms & (Form.Low | Form.Ground)) != 0)
                    {
                        k *= 0.4f;
                    }
                }
                if (s.Down && (forms & (Form.Overhead | Form.Ground | Form.Grab | Form.Throw)) != 0)
                {
                    k *= 1.35f;
                }
                if (s.Recovering)
                {
                    k *= (forms & Form.Fast) != 0 ? 1.6f : 1.25f;   // punish
                }
                if (s.Ranged && c.Dist(v) > 3f)
                {
                    if ((forms & (Form.Dash | Form.Teleport)) != 0)
                    {
                        k *= 1.5f;
                    }
                    else if ((forms & (Form.Projectile | Form.Beam | Form.Line)) != 0)
                    {
                        k *= 1.3f;
                    }
                }
                if (s.AimsAtMe && (forms & (Form.Evasive | Form.Counter | Form.Interrupt)) != 0)
                {
                    k *= 1.4f;
                }
                // patience: a counter-puncher waits for the foe to commit
                if (!answering && s.Doing == Form.None && !s.Recovering && !s.Down && !s.Charging)
                {
                    k *= 1f - 0.3f * per.Patience;
                }

                // memory of this foe
                FoeNotes n = f.Recall(v);
                if (n != null)
                {
                    float adapt = per.Adapt;
                    if (t.Answers != Form.None)
                    {
                        k *= 1f + 0.5f * n.HabitOf(t.Answers) * adapt;
                    }
                    k *= Mathf.Max(0.35f, 1f - 0.35f * n.BurnedOf(forms & ~(Form.Fast | Form.Heavy | Form.Ultimate)) * adapt);
                    n.Record(t, out float ctr, out float landed, out float used);
                    if (used >= 1f)
                    {
                        k *= Mathf.Max(0.3f, 1f - 0.45f * ctr * adapt);
                        k *= 1f + 0.15f * landed;
                        k /= 1f + 0.12f * (used - 1f);   // and vary: the more it was shown to this foe, the less
                    }
                    if (n.Last1 == t)
                    {
                        k *= n.Last2 == t ? (answering ? 0.5f : 0.1f) : 0.65f;   // never three times running
                    }
                    if ((forms & Form.Charge) != 0 && n.Interrupted > 0)
                    {
                        k /= 1f + 0.5f * n.Interrupted * adapt;   // it broke our charges before
                    }
                }
            }

            // danger: winding up next to quick enemies
            if ((forms & Form.Charge) != 0)
            {
                k *= ChargeDanger(c, per);
            }

            // the situation
            bool area = (forms & Form.Area) != 0 || p.Count >= 3;
            if (c.Surrounded)
            {
                if (area)
                {
                    k *= 1f + 0.15f * Mathf.Min(c.Near2 + 1, 5);
                }
                else if ((forms & (Form.Evasive | Form.Teleport)) != 0 && c.HpPct < 0.5f)
                {
                    k *= 1.4f;
                }
                else if (p.Count <= 1)
                {
                    k *= 0.85f;
                }
            }
            if (c.Outnumbered && c.HpPct < 0.4f && (forms & Form.Evasive) != 0)
            {
                k *= 1.5f;
            }
            if (c.Desperate)
            {
                if ((forms & (Form.Heavy | Form.Ultimate)) != 0)
                {
                    k *= per.Is(Style.Berserker) ? 1.7f : 1.3f;
                }
                if ((forms & Form.Evasive) != 0)
                {
                    k *= per.Is(Style.OutFighter) || per.Is(Style.CounterPuncher) ? 1.5f : 1.2f;
                }
            }
            else if (per.Main == Style.Berserker && c.HpPct < 0.6f && (forms & Form.Heavy) != 0)
            {
                k *= 1.6f - c.HpPct;   // the fury grows with the wounds
            }
            if (t.Team && c.AlliesClose >= 2)
            {
                k *= 1.2f;
            }
            if ((forms & Form.Ground) != 0 && c.WaterNear)
            {
                k *= 0.7f;
            }
            // the range it likes to fight at
            if (v != null && per.Range >= 2f)
            {
                float d = c.Dist(v);
                bool keeps = (forms & (Form.Projectile | Form.Beam | Form.Line | Form.Evasive)) != 0;
                if (d < 1.6f)
                {
                    k *= keeps ? 1.2f : (forms & (Form.Grab | Form.Body | Form.Flurry)) != 0 ? 0.8f : 1f;
                }
            }
            // economy: the big ones keep a reserve unless the threat is real, and a long cooldown is
            // not spent on a nuisance (the ultimate waits for a foe worth it)
            Scale sc = t.ScaleAt(level);
            if (sc >= Scale.Heavy && threat < 0.8f && c.Self.getMaxMana() > 0)
            {
                float after = (c.Self.getMana() - Mana.Cost(c.Self, t, level, p.Linked)) / Mathf.Max(1f, c.Self.getMaxMana());
                if (after < 0.25f)
                {
                    k *= 0.7f;
                }
            }
            if (threat < 0.5f && t.CooldownFor(c.Self, level) >= 20f)
            {
                k *= 0.7f;
            }
            // and its whim
            k *= 1f + Random.Range(-per.Whim, per.Whim);
            return Mathf.Clamp(k, 0.03f, 8f);
        }

        /// <summary>The threat a plan answers: its target's, or (an area plan) the share of everyone around it will catch.</summary>
        private static float PlanThreat(Ctx c, Plan p, Actor v)
        {
            float single = v != null ? c.Threat(v) : 0f;
            if (p.Count >= 2 || v == null)
            {
                int near = 0;
                for (int i = 0; i < c.Enemies.Count && i < 16 && c.EnemyDist[i] <= 8f; i++)
                {
                    near++;
                }
                float share = near > 0 ? Mathf.Clamp01(Mathf.Max(1, p.Count) / (float)near) : 1f;
                return Mathf.Max(single, c.ThreatAround * share);
            }
            return single;
        }

        /// <summary>
        /// 1 (no danger) down to about 0.3: quick enemies within reach of a wind-up, the more so if
        /// they know interrupts, walk the Phantom Step, are of its tier or above, or broke its charges
        /// before; a risk-taker cares less.
        /// </summary>
        public static float ChargeDanger(Ctx c, Persona per)
        {
            float danger = 0f;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 4.5f; i++)
            {
                Actor e = c.Enemies[i];
                float q = 0.12f;
                if (Fighters.TryGet(e, out Fighter ef))
                {
                    q = 0.25f;
                    if (ef.Tier >= c.Tier)
                    {
                        q += 0.25f;
                    }
                    if ((ef.Arsenal & Form.Interrupt) != 0)
                    {
                        q += 0.4f;
                    }
                    if (Schools.Knows(e, School.PhantomStep))
                    {
                        q += 0.3f;
                    }
                }
                FoeNotes n = c.F.Recall(e);
                if (n != null && n.Interrupted > 0)
                {
                    q += 0.3f * Mathf.Min(n.Interrupted, 3);
                }
                danger += q * (1f - c.EnemyDist[i] / 4.5f);
            }
            return 1f / (1f + danger * Mathf.Max(0.05f, 1.2f - per.Risk));
        }
    }
}
