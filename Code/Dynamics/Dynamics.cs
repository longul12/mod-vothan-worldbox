using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// DYNAMICS: the layer that makes every battle its own. Fighters read what their opponents are
    /// doing (Reads: the Forms of every attack), answer it before it lands (Tactics: the Brain's
    /// proactive answers, threat-proportional scale, prey, personas, memory) and as it lands
    /// (Reactions: a scored, shuffled choice of answers; counters to counters), break wind-ups
    /// (Interrupts), and remember each opponent (FoeNotes). This file holds the dev tools:
    ///
    ///   dyn                 statistics: reads, answers by depth and technique, interrupts, tactics
    ///   persona [all]       the performer's persona (all: every fighter near it)
    ///   threat              how dangerous each enemy near the performer is to it, and the scale it calls for
    ///   reads               what the performer remembers of each opponent (habits, burned forms, last techniques)
    ///   preys [filter]      what kind of creature every species is (Prey), its weight class, health and size
    ///   variety ...         the variety test (Variety.cs)
    /// </summary>
    internal static class Dynamics
    {
        [SakugaInit]
        private static void Init()
        {
            Hooks.WorldCleared += () =>
            {
                Preys.Clear();
            };
            Dev.Commands["dyn"] = args => Dev.Log(Stats());
            Dev.Commands["persona"] = args =>
            {
                Actor p = Dev.Performer;
                bool all = Dev.Arg(args, 1, "") == "all";
                if (p == null && !all)
                {
                    Dev.Log("persona: no performer");
                    return;
                }
                foreach (Fighter f in Fighters.All)
                {
                    if (!f.Valid || (!all && f.A != p) || (all && p != null && Vector2.Distance(f.A.current_position, p.current_position) > 30f))
                    {
                        continue;
                    }
                    Dev.Log("persona " + f.A.asset.id + "#" + f.Id + " (" + f.Tier + ", " + SchoolList(f.A) + "): " + f.Persona);
                }
            };
            Dev.Commands["threat"] = args =>
            {
                Actor p = Dev.Performer;
                if (p == null)
                {
                    Dev.Log("threat: no performer");
                    return;
                }
                Dev.Log("threat: " + p.asset.id + "#" + p.Sid() + " value " + Threat.Value(p).ToString("F0"));
                foreach (Actor u in Finder.getUnitsFromChunk(p.current_tile, 2))
                {
                    if (u == null || u == p || !Combat.IsFoe(p, u))
                    {
                        continue;
                    }
                    float r = Threat.Ratio(p, u);
                    float ideal = Threat.Ideal(r, Combat.HpPct(p));
                    Dev.Log("  " + u.asset.id + "#" + u.Sid() + " (" + Tiers.Of(u) + ", " + Preys.Name(Preys.Of(u)) + ") value " + Threat.Value(u).ToString("F0")
                        + " threat " + r.ToString("F2") + " ideal scale " + ideal.ToString("F2") + " fit casual/light/heavy/ultimate "
                        + Threat.Fit(Scale.Casual, ideal).ToString("F2") + "/" + Threat.Fit(Scale.Light, ideal).ToString("F2") + "/"
                        + Threat.Fit(Scale.Heavy, ideal).ToString("F2") + "/" + Threat.Fit(Scale.Ultimate, ideal).ToString("F2"));
                }
            };
            Dev.Commands["preys"] = args =>
            {
                string filter = Dev.Arg(args, 1, "");
                foreach (ActorAsset s in AssetManager.actor_library.list)
                {
                    if (s == null || s.id == null || s.id.StartsWith("$", System.StringComparison.Ordinal)
                        || filter.Length > 0 && s.id.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    Dev.Log("prey " + s.id + ": " + Preys.Name(Preys.OfSpecies(s)) + " (weight " + Weights.Species(s) + ", hp "
                        + Weights.SpeciesHealth(s).ToString("F0") + ", size " + s.actor_size + ")");
                }
            };
            Dev.Commands["reads"] = args =>
            {
                Actor p = Dev.Performer;
                if (p == null || !Fighters.TryGet(p, out Fighter f))
                {
                    Dev.Log("reads: no performer");
                    return;
                }
                foreach (FoeNotes n in f.Notes)
                {
                    StringBuilder sb = new StringBuilder("reads: on #" + n.Id + " habits");
                    for (int i = 0; i < FormSet.Bits; i++)
                    {
                        if (n.Habit[i] >= 0.5f)
                        {
                            sb.Append(' ').Append(FormSet.Name(FormSet.Bit(i))).Append('=').Append(n.Habit[i].ToString("F1"));
                        }
                    }
                    sb.Append(" | burned");
                    for (int i = 0; i < FormSet.Bits; i++)
                    {
                        if (n.Burned[i] >= 0.3f)
                        {
                            sb.Append(' ').Append(FormSet.Name(FormSet.Bit(i))).Append('=').Append(n.Burned[i].ToString("F1"));
                        }
                    }
                    sb.Append(" | last ").Append(n.Last1 != null ? n.Last1.Id : "-").Append(", ").Append(n.Last2 != null ? n.Last2.Id : "-");
                    sb.Append(" | broke our charges ").Append(n.Interrupted).Append(" | fooled us ").Append(n.Fooled);
                    Dev.Log(sb.ToString());
                }
            };
        }

        /// <summary>The dynamics statistics since the game started (dev log).</summary>
        public static string Stats()
        {
            StringBuilder sb = new StringBuilder("dynamics:");
            sb.Append(" attacks seen ").Append(Reads.Seen).Append(", plain blows read ").Append(Reads.OrdinarySeen)
                .Append(", answered contacts ").Append(Reads.Answers).Append(", alerts ").Append(Reads.Alerts);
            sb.Append(" | reads rolled ").Append(Reactions.ReadsRolled).Append(" (unread ").Append(Reactions.Unread).Append(")");
            sb.Append(" answers by depth ").Append(Reactions.ByDepth[0]).Append('/').Append(Reactions.ByDepth[1]).Append('/')
                .Append(Reactions.ByDepth[2]).Append('/').Append(Reactions.ByDepth[3]);
            sb.Append(", plain blows answered ").Append(Reactions.OrdinaryAnswers).Append(", answered moves cut short ").Append(Reactions.CutsShort)
                .Append(", feints bitten ").Append(Reads.Bitten);
            sb.Append(" by technique");
            foreach (KeyValuePair<string, int> kv in Reactions.ByTech)
            {
                sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            }
            sb.Append(" | charges broken ").Append(Interrupts.Broken).Append(" (by plain blows ").Append(Interrupts.ByOrdinary)
                .Append("), dispersed ").Append(Interrupts.Dispersed);
            sb.Append(" | plans shaped ").Append(Tactics.Shaped).Append(", answering ").Append(Tactics.Answering).Append(", interrupting ").Append(Tactics.Interrupting);
            return sb.ToString();
        }

        public static string SchoolList(Actor a)
        {
            List<string> names = new List<string>();
            foreach (School s in Schools.Of(a))
            {
                names.Add(Schools.Id(s));
            }
            return names.Count > 0 ? string.Join("+", names) : "no school";
        }
    }
}
