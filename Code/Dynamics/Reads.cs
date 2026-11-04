using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// READS: what every unit in a fight is doing now and did lately, so others can read it. When a
    /// technique starts (or any move: a flow strike, a ki shot, a wind-up) the fighters it threatens
    /// see its Forms and where it will land (its plan's target, point and radius); a unit that
    /// begins to gather power (Move.Charging) is announced to the quick enemies around it, who look
    /// at once (their reaction time is their tier's: a God's is a few hundredths). Every attack a
    /// fighter sees goes into its notes on the attacker (FoeNotes: habits), every answer into the
    /// attacker's notes (what got countered), every landed technique too. Ordinary blows and the
    /// game's shots and spells are read as well (a sword swing is Blade|Fast, an arrow Projectile, a
    /// mage's bolt Ki|Projectile).
    /// </summary>
    internal static class Reads
    {
        /// <summary>What one unit is doing and did lately.</summary>
        internal sealed class Trace
        {
            public long Id;
            /// <summary>Its last attack's forms, when, on whom (-1: nobody in particular), and what it was.</summary>
            public Form Last;
            public double LastAt = -99;
            public long LastOn = -1;
            public string LastWhat;
            /// <summary>When its own attack was last parried or evaded (it is caught recovering for a moment), and which move it was.</summary>
            public double AnsweredAt = -99;
            public Move AnsweredMove;
            /// <summary>When its charge was last broken.</summary>
            public double BrokenAt = -99;
            /// <summary>The technique move it is playing now and what it is aimed at (its plan).</summary>
            public Move AimMove;
            public long AimOn = -1;
            public Vector2 AimAt;
            public float AimR;
            /// <summary>The move whose blow last landed (a technique's landing is counted once).</summary>
            public Move LandedMove;
            /// <summary>Its last few attacks (forms and when).</summary>
            public readonly Form[] RecentF = new Form[6];
            public readonly double[] RecentT = new double[6];
            public int Head;

            public void Note(Form f, long on, string what)
            {
                Last = f;
                LastAt = Fighter.Now;
                LastOn = on;
                LastWhat = what;
                RecentF[Head] = f;
                RecentT[Head] = LastAt;
                Head = (Head + 1) % RecentF.Length;
            }
        }

        private static readonly Dictionary<long, Trace> _t = new Dictionary<long, Trace>();
        private static readonly List<long> _drop = new List<long>();
        private static float _prune;

        /// <summary>Attacks seen, answers recorded, alerts given (dev statistics).</summary>
        internal static int Seen, Answers, Alerts, OrdinarySeen;

        [SakugaInit]
        private static void Init()
        {
            Hooks.MoveStarted += MoveStarted;
            Hooks.TechniqueStarted += TechniqueStarted;
            Hooks.Reached += Reached;
            Hooks.HitDealt += HitDealt;
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += Clear;
        }

        public static void Clear()
        {
            _t.Clear();
        }

        /// <summary>The trace of a unit (made if 'make').</summary>
        public static Trace Of(Actor a, bool make)
        {
            if (a == null)
            {
                return null;
            }
            long id = a.Sid();
            if (id < 0)
            {
                return null;
            }
            if (_t.TryGetValue(id, out Trace t))
            {
                return t;
            }
            if (!make)
            {
                return null;
            }
            t = new Trace { Id = id };
            _t[id] = t;
            return t;
        }

        /// <summary>What a move shows to whoever reads it right now (its Forms, Counter for an answer).</summary>
        public static Form FormsOf(Move m)
        {
            if (m == null)
            {
                return Form.None;
            }
            Form f = m.Forms;
            if (m.CounterDepth > 0)
            {
                f |= Form.Counter;
            }
            if (m.Charging)
            {
                f |= Form.Charge;
            }
            return f;
        }

        /// <summary>
        /// What 'v' is doing right now: its move's forms, or its last plain blow's within a third of a
        /// second (None: nothing).
        /// </summary>
        public static Form Doing(Actor v)
        {
            if (v == null)
            {
                return Form.None;
            }
            if (Moves.TryGet(v, out Move m) && m.A == v && !(m is Puppet))
            {
                Form f = FormsOf(m);
                if (f != Form.None)
                {
                    return f;
                }
            }
            Trace t = Of(v, false);
            return t != null && Fighter.Now - t.LastAt < 0.35 ? t.Last : Form.None;
        }

        /// <summary>
        /// Caught recovering: its own attack was just answered (parried, evaded), its charge was just
        /// broken, or its move says so (Move.Recovering). A punish window, and the moment a counter
        /// to its counter may cut its own move short.
        /// </summary>
        public static bool Recovering(Actor v)
        {
            if (v == null)
            {
                return false;
            }
            if (Moves.TryGet(v, out Move m) && m.A == v && m.Recovering)
            {
                return true;
            }
            Trace t = Of(v, false);
            if (t == null)
            {
                return false;
            }
            double now = Fighter.Now;
            return now - t.AnsweredAt < 1.0 || now - t.BrokenAt < 0.8;
        }

        /// <summary>Is a technique of 'v' aimed at 'at' right now (its target, or 'at' inside its area)?</summary>
        /// <summary>Whom a move is aimed at (a Choreo move keeps its target in V: its own Vector2 Aim hides Move.Aim).</summary>
        internal static Actor AimOf(Move m)
        {
            if (m is ChoreoMove c)
            {
                return c.VOk ? c.V : null;
            }
            return m.Aim;
        }

        public static bool AimsAt(Actor v, Actor at)
        {
            if (v == null || at == null || !Moves.TryGet(v, out Move m) || m.A != v)
            {
                return false;
            }
            Actor aim = AimOf(m);
            if (aim != null)
            {
                return aim == at;
            }
            Trace t = Of(v, false);
            if (t == null || t.AimMove != m)
            {
                return false;
            }
            if (t.AimOn >= 0)
            {
                return t.AimOn == at.Sid();
            }
            return t.AimR > 0f && (at.current_position - t.AimAt).sqrMagnitude <= (t.AimR + 1f) * (t.AimR + 1f);
        }

        // ------------------------------------------------------------- events --

        private static void MoveStarted(Move m)
        {
            if (m == null || m is Puppet || m.A == null)
            {
                return;
            }
            Form f = FormsOf(m);
            if (f == Form.None)
            {
                return;
            }
            Actor on = AimOf(m);
            Trace t = Of(m.A, true);
            t.Note(f, on != null ? on.Sid() : -1, m.Tech != null ? m.Tech.Id : m.GetType().Name);
            Seen++;
            if (on != null && Fighters.TryGet(on, out Fighter vf))
            {
                // a light move (a flow strike) teaches less than a technique
                vf.NotesOn(m.A).Saw(f, m.Interruptible ? 0.35f : 1f);
            }
        }

        private static void TechniqueStarted(Fighter f, Plan p)
        {
            if (f == null || p == null || p.Tech == null || !Moves.TryGet(f.A, out Move m) || m.A != f.A)
            {
                return;
            }
            Trace t = Of(f.A, true);
            t.AimMove = m;
            t.AimOn = p.Target != null ? p.Target.Sid() : -1;
            t.AimAt = p.Target != null ? p.Target.current_position : p.Point;
            t.AimR = p.AreaRadius;
            Form forms = FormsOf(m);
            if (p.Target != null)
            {
                t.LastOn = t.AimOn;
                f.NotesOn(p.Target).Used(p.Tech);
                if (Fighters.TryGet(p.Target, out Fighter vf))
                {
                    vf.NotesOn(f.A).Saw(forms, 1f);
                }
            }
            // whoever it threatens sees it coming (the target, or everyone in the area it will strike)
            float r = p.Target != null ? 0f : Mathf.Max(p.AreaRadius, 1.5f);
            Alert(f.A, p.Target, p.Target != null ? p.Target.current_position : p.Point, r, forms);
        }

        private static void Reached(Move m, Actor v, Contact c)
        {
            if (m == null || m.A == null || v == null)
            {
                return;
            }
            Form f = FormsOf(m);
            Trace t = Of(m.A, true);
            bool aimed = AimOf(m) == v || t.AimMove == m && t.AimOn == v.Sid();
            if (!aimed && Fighters.TryGet(v, out Fighter vf))
            {
                vf.NotesOn(m.A).Saw(f, m.Interruptible ? 0.35f : 1f);   // (an aimed move was noted when it started)
            }
            if (c == Contact.Hit)
            {
                return;
            }
            Answers++;
            t.AnsweredAt = Fighter.Now;
            t.AnsweredMove = m;
            if (Fighters.TryGet(m.A, out Fighter af))
            {
                af.NotesOn(v).Answered(f, m.Tech);
            }
        }

        private static void HitDealt(Actor a, Actor v, HitResult r, Move by)
        {
            if (!r.Landed || by == null || by.Tech == null || a == null || v == null)
            {
                return;
            }
            Trace t = Of(a, true);
            if (t.LandedMove == by)
            {
                return;
            }
            t.LandedMove = by;
            if (Fighters.TryGet(a, out Fighter f))
            {
                f.NotesOn(v).Landed(by.Tech);
            }
        }

        /// <summary>Feints bitten (dev statistics).</summary>
        internal static int Bitten;

        /// <summary>
        /// 'victim' bit on a feint of 'feinter' (it answered the fake, or took the bait): it remembers
        /// (FoeNotes.Fooled), and a patient fighter bites less on this foe's feints from now on
        /// (Reactions.ReadChance; feint techniques' plans ask Fooled too). 'feint': the feint's move, so
        /// one feint is counted once however many times it is reported. The dispatch reports a bite on
        /// its own (an answer to a move showing Feint, by a technique whose Answers don't see through
        /// feints); call this for a bite it can't see.
        /// </summary>
        public static void Fooled(Actor feinter, Actor victim, Move feint = null)
        {
            if (feinter == null || victim == null || feinter == victim || !Fighters.TryGet(victim, out Fighter f))
            {
                return;
            }
            FoeNotes n = f.NotesOn(feinter);
            if (n != null && n.Fool(feint))
            {
                Bitten++;
                if (Dev.On) Dev.Trace("feint: " + victim.asset.id + "#" + victim.Sid() + " bit on a feint of " + feinter.asset.id + "#" + feinter.Sid()
                    + " (" + (feint != null && feint.Tech != null ? feint.Tech.Id : feint != null ? feint.GetType().Name : "-") + "), fooled " + n.Fooled + "x");
            }
        }

        /// <summary>A duel exchange began between 'a' and 'b': each reads the other's way of fighting in it.</summary>
        public static void Exchanged(Actor a, Actor b, Form f, string id)
        {
            if (a == null || b == null || f == Form.None)
            {
                return;
            }
            Of(a, true).Note(f, b.Sid(), id);
            Of(b, true).Note(f, a.Sid(), id);
            if (Fighters.TryGet(a, out Fighter fa))
            {
                fa.NotesOn(b).Saw(f, 0.5f);
            }
            if (Fighters.TryGet(b, out Fighter fb))
            {
                fb.NotesOn(a).Saw(f, 0.5f);
            }
        }

        /// <summary>
        /// An ordinary blow or a shot of the game is about to land on 't' (MapBox.applyAttack):
        /// read it (its kind goes into t's notes on the attacker); if it lands on a unit gathering
        /// power, a quick enough blow breaks the charge.
        /// </summary>
        public static void Ordinary(AttackData d, Actor t, bool deflected)
        {
            if (d.initiator == null || !d.initiator.isActor() || t == null)
            {
                return;
            }
            Actor a = d.initiator.a;
            if (a == null || a == t)
            {
                return;
            }
            // (called for every blow in the world: only what a fighter could read is kept)
            bool reader = Fighters.TryGet(t, out Fighter vf);
            bool charging = !deflected && Moves.Charging(t);
            if (!reader && !charging && !Fighters.TryGet(a, out _))
            {
                return;
            }
            Form f = OrdinaryForms(a, d);
            OrdinarySeen++;
            Trace tr = Of(a, true);
            tr.Note(f, t.Sid(), d.is_projectile ? "shot" : "swing");
            if (reader)
            {
                vf.NotesOn(a).Saw(f, 0.3f);
            }
            if (charging)
            {
                Interrupts.Ordinary(a, t, d.is_projectile);
            }
        }

        /// <summary>What a plain attack of the game looks like: a sword swing, a punch, an arrow, a spell.</summary>
        public static Form OrdinaryForms(Actor a, AttackData d)
        {
            Form f = Form.Fast;
            if (d.is_projectile)
            {
                f |= Form.Projectile;
                string p = d.projectile_id ?? "";
                if (p.Length > 0 && !p.Contains("arrow") && !p.Contains("rock") && !p.Contains("snowball") && !p.Contains("bullet") && !p.Contains("shotgun"))
                {
                    f |= Form.Ki;   // a fireball, a plasma bolt, a mage's spell
                }
            }
            else
            {
                f |= d.metallic_weapon ? Form.Blade : Form.Punch;
            }
            if (d.area_of_effect > 0.5f)
            {
                f |= Form.Area;
            }
            return f;
        }

        // ------------------------------------------------------------- alerts --

        /// <summary>
        /// The fighters an attack threatens see it coming: 'target' (if any) and every fighter hostile
        /// to 'by' within 'radius' of 'at' look again after their reaction time (God 0.03-0.08 s ..
        /// Novice 0.3-0.6 s) and may break their pause to answer it (Fighter.AlertUntil).
        /// </summary>
        public static void Alert(Actor by, Actor target, Vector2 at, float radius, Form forms)
        {
            if (by == null)
            {
                return;
            }
            if (target != null && Fighters.TryGet(target, out Fighter tf) && tf.Valid)
            {
                Wake(tf);
            }
            if (radius <= 0f)
            {
                return;
            }
            float r2 = radius * radius;
            IReadOnlyList<Fighter> all = Fighters.All;
            for (int i = 0; i < all.Count; i++)
            {
                Fighter f = all[i];
                if (f.A == target || !f.Valid || (f.A.current_position - at).sqrMagnitude > r2 || !Combat.IsFoe(by, f.A))
                {
                    continue;
                }
                Wake(f);
            }
        }

        /// <summary>A charge began: every quick enemy within reach of it is told (the interrupters look).</summary>
        private static void ChargeBegan(Move m)
        {
            Actor a = m.A;
            Trace t = Of(a, true);
            t.Note(FormsOf(m), AimOf(m) != null ? AimOf(m).Sid() : t.AimOn, m.Tech != null ? m.Tech.Id : m.GetType().Name);
            Vector2 at = a.current_position;
            IReadOnlyList<Fighter> all = Fighters.All;
            for (int i = 0; i < all.Count; i++)
            {
                Fighter f = all[i];
                if (f.A == a || !f.Valid)
                {
                    continue;
                }
                float reach = f.Info != null ? Mathf.Min(f.Info.Awareness, 12f) : 5f;
                if ((f.A.current_position - at).sqrMagnitude > reach * reach || !Combat.IsFoe(f.A, a))
                {
                    continue;
                }
                Wake(f);
            }
        }

        private static void Wake(Fighter f)
        {
            float delay;
            switch (f.Tier)
            {
                case Tier.God: delay = 0.05f; break;
                case Tier.Grandmaster: delay = 0.09f; break;
                case Tier.Master: delay = 0.14f; break;
                case Tier.Expert: delay = 0.22f; break;
                case Tier.Adept: delay = 0.32f; break;
                default: delay = 0.42f; break;
            }
            Persona p = f.Persona;
            delay *= Random.Range(0.7f, 1.3f) / Mathf.Max(0.6f, p.ReadSkill);
            f.ThinkIn = Mathf.Min(f.ThinkIn, delay);
            f.LinkThinkIn = Mathf.Min(f.LinkThinkIn, delay);
            f.AlertUntil = Fighter.Now + delay + 0.6;
            Alerts++;
        }

        /// <summary>Has 'f' been alerted to something it should answer right now?</summary>
        public static bool Alerted(Fighter f)
        {
            return f != null && Fighter.Now <= f.AlertUntil;
        }

        // --------------------------------------------------------------- tick --

        private static void Tick(float dt)
        {
            IReadOnlyList<Move> all = Moves.All;
            for (int i = 0; i < all.Count; i++)
            {
                Move m = all[i];
                if (m.Ended || m is Puppet)
                {
                    continue;
                }
                bool ch;
                try
                {
                    ch = m.Charging;
                }
                catch
                {
                    ch = false;
                }
                if (ch && !m.ChargeSeen)
                {
                    m.ChargeSeen = true;
                    try
                    {
                        ChargeBegan(m);
                    }
                    catch (System.Exception e)
                    {
                        SakugaMod.Report("reads charge", e);
                    }
                }
                else if (!ch && m.ChargeSeen)
                {
                    m.ChargeSeen = false;   // (a move may gather again later: a second wind-up is announced too)
                }
            }
            _prune -= Time.unscaledDeltaTime;
            if (_prune > 0f)
            {
                return;
            }
            _prune = 10f;
            double now = Fighter.Now;
            _drop.Clear();
            foreach (KeyValuePair<long, Trace> kv in _t)
            {
                Trace t = kv.Value;
                if (now - t.LastAt > 30.0 && now - t.AnsweredAt > 30.0 && now - t.BrokenAt > 30.0)
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (long id in _drop)
            {
                _t.Remove(id);
            }
        }
    }
}
