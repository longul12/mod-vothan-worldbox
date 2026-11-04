using System;
using UnityEngine;

namespace Sakuga
{
    /// <summary>The fighting personalities. Every fighter has a main one and a second that colours it.</summary>
    internal enum Style
    {
        /// <summary>Toe to toe: fists, elbows, body blows, grabs; little patience for dodging.</summary>
        Brawler,
        /// <summary>Waits for the swing and punishes it: counters, guards, quick straights; reads the best.</summary>
        CounterPuncher,
        /// <summary>Keeps its range: shots, beams, lines, kicks; side-steps the rush.</summary>
        OutFighter,
        /// <summary>Pressure: flurries, dashes, fast blows, never a breath for the other.</summary>
        Swarmer,
        /// <summary>Feints, clones, blinks and blows from where nobody looked.</summary>
        Trickster,
        /// <summary>Reads, adapts fast, answers deeper; no favourite, the right tool.</summary>
        Tactician,
        /// <summary>Heavy, charged, ruinous; ignores danger and fights hardest when hurt.</summary>
        Berserker,
        /// <summary>Stands between the enemy and its own: areas, guards, the ground.</summary>
        Guardian,
        /// <summary>Picks the monsters and the flyers, and has a tool for each.</summary>
        Hunter
    }

    /// <summary>
    /// A fighter's personality: which kinds of attack it likes (a weight per Form, unique to the unit),
    /// how readily it commits, how much risk it takes winding up, how well it reads, how fast it
    /// learns an opponent, how whimsical it is, the range it likes to fight at, how deep it counters.
    /// Two Gods with the same arsenal still fight nothing alike.
    /// </summary>
    internal sealed class Persona
    {
        public Style Main, Second;
        /// <summary>0..1: how readily it commits (shorter gaps between techniques).</summary>
        public float Aggression;
        /// <summary>0..1: winding up with a quick enemy close (1 ignores the danger).</summary>
        public float Risk;
        /// <summary>0..1: waits for an opening rather than forcing one.</summary>
        public float Patience;
        /// <summary>Multiplies its chance to read and answer an incoming attack.</summary>
        public float ReadSkill;
        /// <summary>How strongly what it remembers of an opponent shapes its choices (0.4 .. 1.6).</summary>
        public float Adapt;
        /// <summary>Extra randomness in its choices (variety of its own).</summary>
        public float Whim;
        /// <summary>The distance it likes to fight at (tiles; 0 in your face).</summary>
        public float Range;
        /// <summary>Extra counter depth it reads (a tactician reads one layer deeper).</summary>
        public int Depth;
        public int Seed;

        /// <summary>Per Form bit: how much it likes that kind of attack (0.6 .. 1.6).</summary>
        internal readonly float[] W = new float[FormSet.Bits];

        /// <summary>
        /// How much it likes an attack of these forms (0.6 .. 1.6): its likes and dislikes over the
        /// forms set, the strongest feelings counting most.
        /// </summary>
        public float Affinity(Form f)
        {
            if (f == Form.None)
            {
                return 1f;
            }
            ulong x = (ulong)f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < FormSet.Bits && x != 0; i++)
            {
                ulong b = 1UL << i;
                if ((x & b) != 0)
                {
                    x &= ~b;
                    sum += Mathf.Log(W[i]);
                    n++;
                }
            }
            if (n == 0)
            {
                return 1f;
            }
            return Mathf.Clamp(Mathf.Exp(sum / Mathf.Sqrt(n)), 0.6f, 1.6f);
        }

        public bool Is(Style s)
        {
            return Main == s || Second == s;
        }

        public override string ToString()
        {
            return Main + "/" + Second + " aggr " + Aggression.ToString("F2") + " risk " + Risk.ToString("F2") + " read " + ReadSkill.ToString("F2")
                + " adapt " + Adapt.ToString("F2") + " whim " + Whim.ToString("F2") + " range " + Range.ToString("F1") + " depth+" + Depth;
        }
    }

    /// <summary>Makes personas: weighted by people, traits, schools and weapons, picked with the unit's own seed.</summary>
    internal static class Personas
    {
        private const int N = 9;

        private struct Profile
        {
            public Form Likes, Dislikes;
            public float Aggression, Risk, Patience, ReadSkill, Adapt, Whim, Range;
            public int Depth;
        }

        private static readonly Profile[] _p = new Profile[N];
        private static readonly Persona _neutral = new Persona { Main = Style.Tactician, Second = Style.Brawler, Aggression = 0.5f, Risk = 0.5f, Patience = 0.5f, ReadSkill = 1f, Adapt = 1f, Whim = 0.1f };

        static Personas()
        {
            Def(Style.Brawler, Form.Punch | Form.Hook | Form.Body | Form.Elbow | Form.Knee | Form.Head | Form.Grab | Form.Heavy,
                Form.Evasive | Form.Projectile | Form.Beam | Form.Clone, 0.8f, 0.7f, 0.3f, 0.92f, 0.8f, 0.12f, 0f, 0);
            Def(Style.CounterPuncher, Form.Counter | Form.Guard | Form.Straight | Form.Fast | Form.Evasive | Form.Interrupt,
                Form.Charge | Form.Ultimate | Form.Flurry, 0.35f, 0.3f, 0.85f, 1.35f, 1.15f, 0.08f, 0.5f, 0);
            Def(Style.OutFighter, Form.Projectile | Form.Ki | Form.Beam | Form.Line | Form.Kick | Form.Evasive | Form.Dash,
                Form.Grab | Form.Throw | Form.Body | Form.Knee | Form.Elbow, 0.5f, 0.45f, 0.6f, 1.05f, 1f, 0.12f, 4f, 0);
            Def(Style.Swarmer, Form.Flurry | Form.Fast | Form.Dash | Form.Punch | Form.Kick | Form.Hook | Form.Body,
                Form.Charge | Form.Beam | Form.Guard | Form.Ultimate, 0.95f, 0.6f, 0.15f, 0.9f, 0.9f, 0.15f, 0f, 0);
            Def(Style.Trickster, Form.Feint | Form.Clone | Form.Teleport | Form.Evasive | Form.Aerial | Form.Spin,
                Form.Guard | Form.Heavy | Form.Straight, 0.6f, 0.5f, 0.5f, 1.1f, 1f, 0.35f, 1.5f, 0);
            Def(Style.Tactician, Form.Counter | Form.Interrupt | Form.Line | Form.AntiAir,
                Form.None, 0.5f, 0.35f, 0.7f, 1.2f, 1.6f, 0.1f, 2f, 1);
            Def(Style.Berserker, Form.Heavy | Form.Charge | Form.Ultimate | Form.Area | Form.Overhead | Form.Spin | Form.Ground,
                Form.Guard | Form.Evasive | Form.Counter, 1f, 0.95f, 0.05f, 0.72f, 0.45f, 0.22f, 0f, 0);
            Def(Style.Guardian, Form.Area | Form.Guard | Form.Team | Form.Ground | Form.Counter | Form.Grab,
                Form.Dash | Form.Teleport, 0.45f, 0.3f, 0.65f, 1.1f, 1f, 0.1f, 1f, 0);
            Def(Style.Hunter, Form.Hunt | Form.AntiAir | Form.Projectile | Form.Grab | Form.Throw | Form.Aerial,
                Form.Guard | Form.Flurry, 0.65f, 0.55f, 0.5f, 1.05f, 1.15f, 0.12f, 2.5f, 0);
        }

        private static void Def(Style s, Form likes, Form dislikes, float aggression, float risk, float patience, float read, float adapt, float whim, float range, int depth)
        {
            _p[(int)s] = new Profile
            {
                Likes = likes,
                Dislikes = dislikes,
                Aggression = aggression,
                Risk = risk,
                Patience = patience,
                ReadSkill = read,
                Adapt = adapt,
                Whim = whim,
                Range = range,
                Depth = depth
            };
        }

        /// <summary>A plain persona for units the mod keeps no fighter for.</summary>
        public static Persona Neutral => _neutral;

        /// <summary>The persona of any unit (a fighter's own; the plain one for everyone else).</summary>
        public static Persona Of(Actor a)
        {
            return a != null && Fighters.TryGet(a, out Fighter f) ? f.Persona : _neutral;
        }

        /// <summary>What a persona is built from (the tier, the schools, how many traits): rebuilt when it changes.</summary>
        public static int Signature(Actor a, Tier t)
        {
            if (a == null || a.traits == null)
            {
                return 0;
            }
            int mask = 0;
            for (int i = 1; i <= Schools.Count; i++)
            {
                if (Schools.Knows(a, (School)i))
                {
                    mask |= 1 << i;
                }
            }
            return ((int)t << 24) | (mask << 12) | Mathf.Min(a.traits.Count, 4095);
        }

        /// <summary>Builds the unit's persona (deterministic for a unit: its id seeds every draw).</summary>
        public static Persona Make(Actor a, Tier tier)
        {
            if (a == null)
            {
                return _neutral;
            }
            int seed = Hash(a.Sid());
            System.Random rng = new System.Random(seed);
            float[] w = new float[N];
            for (int i = 0; i < N; i++)
            {
                w[i] = 1f;
            }
            // its people
            string id = a.asset != null ? a.asset.id ?? "" : "";
            if (id.StartsWith("civ_", StringComparison.Ordinal))
            {
                id = id.Substring(4);
            }
            switch (id)
            {
                case "orc":
                    Lean(w, Style.Brawler, 2f); Lean(w, Style.Berserker, 2f); Lean(w, Style.Swarmer, 1.3f);
                    break;
                case "elf":
                    Lean(w, Style.OutFighter, 1.8f); Lean(w, Style.Trickster, 1.6f); Lean(w, Style.CounterPuncher, 1.3f);
                    break;
                case "dwarf":
                    Lean(w, Style.Guardian, 2f); Lean(w, Style.Brawler, 1.5f); Lean(w, Style.Berserker, 1.2f);
                    break;
                case "human":
                    Lean(w, Style.Tactician, 1.4f); Lean(w, Style.CounterPuncher, 1.3f); Lean(w, Style.Swarmer, 1.15f);
                    break;
                case "alien":
                    Lean(w, Style.Trickster, 1.6f); Lean(w, Style.OutFighter, 1.4f);
                    break;
                default:
                    if (Peoples.LeanStyles(a.asset, w))
                    {
                        break;   // an animal people fights with its kind's temper
                    }
                    if (a.asset != null && !a.asset.civ && !a.asset.is_humanoid)
                    {
                        // a beast fights like a beast
                        Lean(w, Style.Brawler, 1.5f); Lean(w, Style.Berserker, 1.5f); Lean(w, Style.Hunter, 1.6f); Lean(w, Style.Swarmer, 1.4f);
                        w[(int)Style.Tactician] *= 0.5f;
                    }
                    break;
            }
            // its nature (the game's traits)
            if (Has(a, "strong") || Has(a, "tough") || Has(a, "fat") || Has(a, "giant")) { Lean(w, Style.Brawler, 1.6f); }
            if (Has(a, "savage") || Has(a, "hotheaded") || Has(a, "bloodlust") || Has(a, "madness") || Has(a, "pyromaniac")) { Lean(w, Style.Berserker, 1.8f); }
            if (Has(a, "fast") || Has(a, "agile") || Has(a, "dash") || Has(a, "ambitious")) { Lean(w, Style.Swarmer, 1.6f); }
            if (Has(a, "deceitful") || Has(a, "thief") || Has(a, "lucky") || Has(a, "psychopath")) { Lean(w, Style.Trickster, 1.7f); }
            if (Has(a, "genius") || Has(a, "wise") || Has(a, "strong_minded") || Has(a, "arcane_reflexes")) { Lean(w, Style.Tactician, 1.8f); }
            if (Has(a, "battle_reflexes") || Has(a, "block") || Has(a, "dodge") || Has(a, "deflect_projectile")) { Lean(w, Style.CounterPuncher, 1.7f); }
            if (Has(a, "eagle_eyed") || Has(a, "paranoid") || Has(a, "backstep") || Has(a, "weightless")) { Lean(w, Style.OutFighter, 1.6f); }
            if (Has(a, "peaceful") || Has(a, "pacifist") || Has(a, "honest") || Has(a, "healing_aura") || Has(a, "blessed")) { Lean(w, Style.Guardian, 1.7f); }
            if (Has(a, "dragonslayer") || Has(a, "mageslayer") || Has(a, "veteran") || Has(a, "flesh_eater")) { Lean(w, Style.Hunter, 1.7f); }
            // its schools
            if (Schools.Knows(a, School.IronFist)) { Lean(w, Style.Brawler, 1.6f); Lean(w, Style.CounterPuncher, 1.5f); Lean(w, Style.Swarmer, 1.3f); }
            if (Schools.Knows(a, School.TempestLeg)) { Lean(w, Style.Swarmer, 1.5f); Lean(w, Style.OutFighter, 1.4f); Lean(w, Style.Trickster, 1.2f); }
            if (Schools.Knows(a, School.ThousandBlades)) { Lean(w, Style.CounterPuncher, 1.6f); Lean(w, Style.Tactician, 1.4f); }
            if (Schools.Knows(a, School.KiArts)) { Lean(w, Style.OutFighter, 1.8f); Lean(w, Style.Tactician, 1.2f); }
            if (Schools.Knows(a, School.EarthBreaker)) { Lean(w, Style.Brawler, 1.5f); Lean(w, Style.Guardian, 1.6f); Lean(w, Style.Berserker, 1.3f); }
            if (Schools.Knows(a, School.PhantomStep)) { Lean(w, Style.Trickster, 2f); Lean(w, Style.Swarmer, 1.3f); }
            if (Schools.Knows(a, School.HuntersWay)) { Lean(w, Style.Hunter, 2.2f); }
            try
            {
                if (a.hasRangeAttack()) { Lean(w, Style.OutFighter, 1.6f); }
            }
            catch
            {
                // (a unit whose stats are being rebuilt)
            }
            Style main = Pick(w, rng, -1);
            Style second = Pick(w, rng, (int)main);
            Profile pm = _p[(int)main], ps = _p[(int)second];
            Persona p = new Persona
            {
                Main = main,
                Second = second,
                Seed = seed,
                Aggression = Mathf.Clamp01(Mix(pm.Aggression, ps.Aggression) + Jit(rng, 0.08f)),
                Risk = Mathf.Clamp01(Mix(pm.Risk, ps.Risk) + Jit(rng, 0.08f)),
                Patience = Mathf.Clamp01(Mix(pm.Patience, ps.Patience) + Jit(rng, 0.08f)),
                ReadSkill = Mathf.Clamp(Mix(pm.ReadSkill, ps.ReadSkill) + Jit(rng, 0.06f), 0.6f, 1.45f),
                Adapt = Mathf.Clamp(Mix(pm.Adapt, ps.Adapt) + Jit(rng, 0.1f), 0.35f, 1.7f),
                Whim = Mathf.Clamp(Mix(pm.Whim, ps.Whim) + Jit(rng, 0.04f), 0.03f, 0.45f),
                Range = Mathf.Max(0f, Mix(pm.Range, ps.Range) + Jit(rng, 0.5f)),
                Depth = Mathf.Max(pm.Depth, main == Style.Tactician || second == Style.Tactician ? 1 : 0)
            };
            for (int i = 0; i < FormSet.Bits; i++)
            {
                Form b = FormSet.Bit(i);
                float k = 1f;
                if ((pm.Likes & b) != 0) k *= 1.35f;
                if ((pm.Dislikes & b) != 0) k *= 0.72f;
                if ((ps.Likes & b) != 0) k *= 1.15f;
                if ((ps.Dislikes & b) != 0) k *= 0.88f;
                // and its own quirks: no two fighters weigh the same blow alike
                k *= 0.86f + 0.3f * (float)rng.NextDouble();
                p.W[i] = Mathf.Clamp(k, 0.6f, 1.6f);
            }
            return p;
        }

        private static float Mix(float main, float second)
        {
            return main * 0.7f + second * 0.3f;
        }

        private static float Jit(System.Random rng, float amp)
        {
            return ((float)rng.NextDouble() * 2f - 1f) * amp;
        }

        private static Style Pick(float[] w, System.Random rng, int skip)
        {
            float sum = 0f;
            for (int i = 0; i < N; i++)
            {
                if (i != skip)
                {
                    sum += w[i];
                }
            }
            float r = (float)rng.NextDouble() * sum;
            for (int i = 0; i < N; i++)
            {
                if (i == skip)
                {
                    continue;
                }
                r -= w[i];
                if (r <= 0f)
                {
                    return (Style)i;
                }
            }
            return skip == N - 1 ? (Style)0 : (Style)(N - 1);
        }

        private static void Lean(float[] w, Style s, float k)
        {
            w[(int)s] *= k;
        }

        private static bool Has(Actor a, string trait)
        {
            try
            {
                return a.hasTrait(trait);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A well-mixed 32-bit hash of a unit id (stable across sessions for the same id).</summary>
        public static int Hash(long id)
        {
            ulong x = (ulong)id + 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (int)(x & 0x7FFFFFFF);
        }
    }
}
