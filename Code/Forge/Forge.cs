using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// THE FORGE: techniques written as recipes, not code. Every *.json file in the mod's Forge folder
    /// holds a list of recipes; each one becomes a real technique when the mod loads (its trait, texts,
    /// icon, plan, counters), played on the Choreo engine like the hand-written ones. A recipe is a few
    /// lines: who learns it (school, entry tier), what it is (Forms, Answers, Prey, Scale), how it is
    /// planned (melee, air, line, area, self, counter) and its script, one step per line in a small
    /// language that maps onto the engine's builder:
    ///   step | run | dash [behind] | blink [behind|point] [dur] | leap apex dur [spin] | land crater area mult
    ///   crouch dur | slip side | backhop dist | circle turn | rise h | drop | spin turns dur | airwalk h steps
    ///   guard dur [hex] | hold dur [tremble] | pose rot sx sy h lunge dur | charge dur Style orb [Element]
    ///   strike Blow mult [hurt= push= lift= daze= crater= area= areamult= wind= power=] [finisher air charged nofire]
    ///   flurry n mult BlowA BlowB [dur=]
    ///   grab | lift Grip [dur] | slam mult crater area [headfirst] | suplex mult crater area | toss mult dist crater area
    ///   letgo Hurt push | beam len width life mult [push= trench=] [wins] | shot Kind mult [count= spread= speed= range=
    ///   size= flight= pierce= crater= area= hurt= push= gap=] | shockwave r mult [hurt= push=] | dome r mult
    ///   quake r mult [cracks=] | pillar mult [height= lift=] [spike] | cone mult half len [blades]
    ///   tornado r mult dur fling | rain Kind count r mult [dur=] | clones n Blow mult | legion Blow mult [max=]
    ///   ghost [lines] | untouchable | dust | ascharge | leaves Link | cinematic title | recover [dur]
    /// "L2:" or "L3:" in front of a step plays it only from that level. Damage grows by level (x1, x1.35,
    /// x1.8), sizes (x1, x1.2, x1.45) and counts (x1, x1.3, x1.6) on their own.
    /// tools/sakuga-forge/forge.py writes recipes (and their icons) by the batch.
    /// </summary>
    internal static class Forge
    {
        public static readonly List<ForgedTechnique> All = new List<ForgedTechnique>();
        internal static readonly float[] Dmg = { 1f, 1.35f, 1.8f };
        internal static readonly float[] Size = { 1f, 1.2f, 1.45f };
        internal static readonly float[] Many = { 1f, 1.3f, 1.6f };

        /// <summary>Reads every recipe file (before the traits are created).</summary>
        public static void Load()
        {
            All.Clear();
            string folder = SakugaMod.Instance?.GetDeclaration()?.FolderPath;
            string dir = folder != null ? Path.Combine(folder, "Forge") : null;
            if (dir == null || !Directory.Exists(dir))
            {
                return;
            }
            int bad = 0;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                List<Recipe> list;
                try
                {
                    list = JsonConvert.DeserializeObject<List<Recipe>>(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] forge: " + Path.GetFileName(file) + ": " + e.Message);
                    continue;
                }
                if (list == null)
                {
                    continue;
                }
                foreach (Recipe r in list)
                {
                    try
                    {
                        ForgedTechnique t = new ForgedTechnique(r, folder);
                        if (Techniques.Register(t))
                        {
                            All.Add(t);
                        }
                        else
                        {
                            bad++;
                            Debug.LogError("[Sakuga] forge: id taken: " + r.id);
                        }
                    }
                    catch (Exception e)
                    {
                        bad++;
                        Debug.LogError("[Sakuga] forge: recipe " + (r != null ? r.id : "?") + ": " + e.Message);
                    }
                }
            }
            Debug.Log("[Sakuga] forge: " + All.Count + " techniques from recipes" + (bad > 0 ? ", " + bad + " rejected" : ""));
        }

        /// <summary>Their names and lines (after the language is loaded).</summary>
        [SakugaInit]
        internal static void Texts()
        {
            foreach (ForgedTechnique t in All)
            {
                // Fallback only: never clobber Locales/cz.json (or any loaded translation).
                Traits.PutFallback("trait_" + t.TraitId, t.R.name ?? t.Id);
                Traits.PutFallback("trait_" + t.TraitId + "_info", t.R.info ?? "");
                Traits.PutFallback("sakuga_title_" + t.Id, (t.R.name ?? t.Id).ToUpperInvariant());
            }
        }

        // ---------------------------------------------------------- parsing --

        internal static T Enum<T>(string s, T d) where T : struct
        {
            return !string.IsNullOrEmpty(s) && System.Enum.TryParse(s, true, out T v) ? v : d;
        }

        internal static T EnumStrict<T>(string s) where T : struct
        {
            if (System.Enum.TryParse(s, true, out T v))
            {
                return v;
            }
            throw new FormatException("not a " + typeof(T).Name + ": " + s);
        }

        /// <summary>Was this technique forged from a recipe?</summary>
        internal static bool Is(string id)
        {
            foreach (ForgedTechnique t in All)
            {
                if (t.Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A hold by name: Front, Overhead, Inverted or Hug.</summary>
        internal static Grip GripOf(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "front": return Grip.Front;
                case "overhead": return Grip.Overhead;
                case "inverted": return Grip.Inverted;
                case "hug": return Grip.Hug;
            }
            throw new FormatException("not a Grip: " + s);
        }

        internal static float F(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        internal static Form Forms(string[] names)
        {
            Form f = Form.None;
            if (names != null)
            {
                foreach (string n in names)
                {
                    f |= EnumStrict<Form>(n);
                }
            }
            return f;
        }

        internal static Prey Preys(string[] names)
        {
            Prey p = Prey.None;
            if (names != null)
            {
                foreach (string n in names)
                {
                    p |= EnumStrict<Prey>(n);
                }
            }
            return p;
        }
    }

    /// <summary>One recipe as written in a Forge file.</summary>
    internal sealed class Recipe
    {
        public string id, name, info;
        public string school = "none", tier = "novice", plan = "melee", role = "single", scale = "light", element = "school";
        public string contact = "evaded";
        public string[] forms, answers, prey, accepts, steps;
        public float[] mana, cooldown;
        public float reach = 2.2f, len = 10f, width = 0.6f, radius = 2.5f, range = 6f;
        public int need = 1;
        public bool hold, worthy = true;
    }

    /// <summary>One parsed step of a recipe's script.</summary>
    internal sealed class ForgeStep
    {
        public string Op;
        public string[] Args;
        public readonly Dictionary<string, string> Kw = new Dictionary<string, string>();
        public readonly HashSet<string> Flags = new HashSet<string>();
        public int From = 1;

        public float A(int i, float d)
        {
            return Args.Length > i ? Forge.F(Args[i]) : d;
        }

        public string S(int i, string d)
        {
            return Args.Length > i ? Args[i] : d;
        }

        public float K(string key, float d)
        {
            return Kw.TryGetValue(key, out string v) ? Forge.F(v) : d;
        }

        public string KS(string key, string d)
        {
            return Kw.TryGetValue(key, out string v) ? v : d;
        }

        public bool Has(string flag)
        {
            return Flags.Contains(flag);
        }

        public static ForgeStep Parse(string line)
        {
            ForgeStep s = new ForgeStep();
            string text = line.Trim();
            if (text.Length > 3 && (text[0] == 'L' || text[0] == 'l') && char.IsDigit(text[1]) && text[2] == ':')
            {
                s.From = text[1] - '0';
                text = text.Substring(3).Trim();
            }
            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                throw new FormatException("empty step");
            }
            s.Op = parts[0].ToLowerInvariant();
            List<string> args = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    s.Kw[p.Substring(0, eq).ToLowerInvariant()] = p.Substring(eq + 1);
                }
                else if (char.IsLetter(p[0]) && IsFlag(p))
                {
                    s.Flags.Add(p.ToLowerInvariant());
                }
                else
                {
                    args.Add(p);
                }
            }
            s.Args = args.ToArray();
            return s;
        }

        private static bool IsFlag(string p)
        {
            switch (p.ToLowerInvariant())
            {
                case "finisher": case "air": case "charged": case "nofire": case "headfirst": case "wins": case "spike":
                case "blades": case "hex": case "tremble": case "lines": case "behind": case "point": case "target": case "self": case "cut":
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>A technique made from a recipe.</summary>
    internal sealed class ForgedTechnique : Technique
    {
        public readonly Recipe R;
        private readonly string _id;
        private readonly Tier _entry;
        private readonly Role _role;
        private readonly School _school;
        private readonly Form _forms, _answers;
        private readonly Prey _prey;
        private readonly Scale _scale;
        private readonly Element _element;
        private readonly float[] _mana, _cd;
        private readonly List<ForgeStep> _steps = new List<ForgeStep>();
        private readonly HashSet<Link> _accepts = new HashSet<Link>();
        private readonly string _icon;
        private readonly bool _reactive;
        private readonly float[] _total = new float[3];

        public ForgedTechnique(Recipe r, string folder)
        {
            if (r == null || string.IsNullOrEmpty(r.id))
            {
                throw new FormatException("no id");
            }
            R = r;
            _id = r.id;
            _entry = Forge.EnumStrict<Tier>(r.tier);
            _role = Forge.Enum(r.role, Role.Single);
            _school = SchoolOf(r.school);
            _forms = Forge.Forms(r.forms);
            _answers = Forge.Forms(r.answers);
            _prey = Forge.Preys(r.prey);
            _scale = Forge.Enum(r.scale, Scale.Light);
            _element = Forge.Enum(r.element, Element.School);
            _reactive = r.plan == "counter";
            if (r.accepts != null)
            {
                foreach (string l in r.accepts)
                {
                    _accepts.Add(Forge.EnumStrict<Link>(l));
                }
            }
            if (r.steps == null || r.steps.Length == 0)
            {
                throw new FormatException("no steps");
            }
            foreach (string line in r.steps)
            {
                ForgeStep s = ForgeStep.Parse(line);
                ForgeInterpreter.Check(s);
                _steps.Add(s);
            }
            int ti = (int)_entry;
            _mana = r.mana != null && r.mana.Length == 3 ? r.mana : new[] { 6f * ti, 11f * ti, 18f * ti };
            _cd = r.cooldown != null && r.cooldown.Length == 3 ? r.cooldown : new[] { 4f + ti, 5f + ti, 6f + ti };
            for (int lv = 1; lv <= 3; lv++)
            {
                _total[lv - 1] = Mathf.Max(0.5f, ForgeInterpreter.Total(_steps, lv));
            }
            string pascal = Pascal(_id);
            string png = folder != null ? Path.Combine(folder, "GameResources", "ui", "Icons", "sakuga", "icon" + pascal + ".png") : null;
            _icon = png != null && File.Exists(png) ? "ui/Icons/sakuga/icon" + pascal
                : _school != School.None ? "ui/Icons/sakuga/iconSchool" + Pascal(Schools.Id(_school))
                : Tiers.Info(_entry)?.Icon;
        }

        private static School SchoolOf(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "none")
            {
                return School.None;
            }
            for (int i = 1; i <= Schools.Count; i++)
            {
                if (Schools.Id((School)i) == s)
                {
                    return (School)i;
                }
            }
            throw new FormatException("no school " + s);
        }

        public override string Id => _id;
        public override Tier Entry => _entry;
        public override Role Role => _role;
        public override School School => _school;
        public override Form Forms => _forms;
        public override Form Answers => _answers;
        public override Prey Prey => _prey;
        public override Scale Scale => _scale;
        public override float[] ManaCost => _mana;
        public override float[] Cooldown => _cd;
        public override bool Reactive => _reactive;
        public override string Icon => _icon;
        public Element Element => _element;

        public override bool Accepts(Link link)
        {
            return _accepts.Contains(link);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float total = _total[Mathf.Clamp(level, 1, 3) - 1];
            switch (R.plan)
            {
                case "counter":
                    return null;
                case "air":
                    if (!c.Forced && !(c.Linked && c.Link == Link.Launched))
                    {
                        return null;
                    }
                    return Choreo.PickMelee(c, this, level, new MeleeWant { Reach = R.reach + 1.5f, Total = total, Air = true, Hold = R.hold, Worthy = false });
                case "line":
                    return Choreo.PickLine(c, this, level, R.len * Forge.Size[Mathf.Clamp(level, 1, 3) - 1], R.width, total, R.need, R.worthy);
                case "area":
                    return Choreo.PickArea(c, this, level, R.radius * Forge.Size[Mathf.Clamp(level, 1, 3) - 1], R.range, total, R.need);
                case "self":
                    return Around(c, R.radius * Forge.Size[Mathf.Clamp(level, 1, 3) - 1], total);
                default:
                    return Choreo.PickMelee(c, this, level, new MeleeWant
                    {
                        Reach = R.reach,
                        Total = total,
                        Hold = R.hold,
                        Worthy = R.worthy,
                        IntoGuard = (_answers & Form.Guard) != 0 ? 1.3f : 0.7f
                    });
            }
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>A burst around itself: worth it when enough enemies stand within its reach (the nearest is the target).</summary>
        private Plan Around(Ctx c, float radius, float total)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            _near.Clear();
            int count = c.CountNear(c.Pos, radius, _near);
            int needed = c.Linked ? Mathf.Max(1, R.need - 1) : R.need;
            if (count == 0 || count < needed && !c.Forced)
            {
                return null;
            }
            float score = 0f;
            Actor best = null;
            float bd = float.MaxValue;
            foreach (Actor v in _near)
            {
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, total), cm);
                float d = (v.current_position - c.Pos).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = v;
                }
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = count,
                Score = score,
                AreaRadius = radius,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            ForgeInterpreter.Build(m, this, _steps);
            return m.Start();
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (!_reactive || area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            ChoreoMove m = ChoreoMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            ForgeInterpreter.Build(m, this, _steps);
            if (!m.Start())
            {
                return Contact.Hit;
            }
            return R.contact == "parried" ? Contact.Parried : Contact.Evaded;
        }
    }
}
