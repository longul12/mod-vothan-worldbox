using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// TEAMWORK TACTICS: how fighters of one side gang up on an enemy none of them could take
    /// alone, and pass combos to each other.
    ///
    ///   ASSAULT PARTY   An enemy whose power rating is at least 1.75x that of the strongest of its
    ///                   foes near it (the one who noticed included) is hunted: up to four tiered
    ///                   Adepts and better within 8 tiles join. The toughest takes the front, the
    ///                   others come in from +-120 degrees and from behind (a quick flank dash when
    ///                   they are on the wrong side), all set on it; the Brain does the fighting. A
    ///                   red glint marks the hunted enemy every second. The party breaks up when it
    ///                   dies, flees out of reach, or fewer than two hunters remain. Duels are left
    ///                   alone: nobody in a duel hunts or is hunted, and a party whose quarry gets
    ///                   locked in one stands back until it walks out of it. A fighter a battle
    ///                   situation is steering (Situations.Engaged: a champion, a challenger, an
    ///                   avenger, a rescuer) never joins, and a hunter it takes over leaves the
    ///                   party: one unit is never pulled two ways at once.
    ///   CROSS-UNIT LINK When a hunter's move leaves the hunted enemy Launched or Down, one other free
    ///                   hunter within 3 tiles of it gets the hand-off: a link window on it (0.5 s,
    ///                   it thinks at once) and +20% damage for 2 s, so combos chain across units
    ///                   (a flickering line in its tier colour shows the pass).
    ///
    /// Parity goals this serves: 2 Experts ~ 1 Master, 4 Adepts ~ 1 Expert, 3 Grandmasters ~ 1 God.
    ///
    /// Cheap by design: each fighter looks around at most once a second (randomised), with one
    /// chunk scan; the marks and the parties' upkeep run once a frame over the few parties alive.
    /// Off with the team techniques setting.
    /// </summary>
    internal static class Teamwork
    {
        private sealed class Hunt
        {
            public Actor Target;
            public long TargetId;
            public Kingdom Side;
            public readonly List<Fighter> Members = new List<Fighter>();
            public readonly List<float> Slots = new List<float>();
            public float Front;
            public double Formed, Until, NextMark;
        }

        private static readonly float[] SlotAngles = { 0f, 120f, -120f, 180f };
        private static readonly Color Red = new Color(1f, 0.2f, 0.18f, 1f);

        private static readonly List<Hunt> _hunts = new List<Hunt>();
        private static readonly Dictionary<long, Hunt> _byMember = new Dictionary<long, Hunt>();
        private static readonly Dictionary<long, double> _next = new Dictionary<long, double>();
        private static readonly List<long> _stale = new List<long>();
        private static int _pruneAt = 1024;
        private static readonly List<Actor> _scan = new List<Actor>();
        private static readonly List<KeyValuePair<float, Fighter>> _cands = new List<KeyValuePair<float, Fighter>>();
        private static int _frame = -1;

        /// <summary>Parties formed / hand-offs given since the game started (dev log).</summary>
        internal static int Parties, Handoffs;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FighterTick += Tick;
            Hooks.MoveEnded += MoveEnded;
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            Crew.ClearFailures();
            _hunts.Clear();
            _byMember.Clear();
            _next.Clear();
            _pruneAt = 1024;
            _scan.Clear();
            _frame = -1;
        }

        // -------------------------------------------------------------- tick --

        private static void Tick(Fighter f, float dt)
        {
            if (dt <= 0f || !Settings.TeamTechniques || World.world == null)
            {
                return;
            }
            double now = Fighter.Now;
            if (_hunts.Count > 0 && Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                Upkeep(now);
            }
            if (_next.TryGetValue(f.Id, out double at) && now < at)
            {
                return;
            }
            if (!f.Valid)
            {
                return;
            }
            if (_next.Count >= _pruneAt)
            {
                Prune(now);
            }
            _next[f.Id] = now + Random.Range(0.8f, 1.2f);
            if (_byMember.TryGetValue(f.Id, out Hunt h))
            {
                Keep(h, f, now);
                return;
            }
            if (f.Tier < Tier.Adept || !f.A.isFighting() || Director.InScene(f.A) || f.A.current_tile == null || Duels.InDuel(f.A) || Situations.Engaged(f.A)
                || f.A.isInMagnet() || ControllableUnit.isControllingUnit(f.A))
            {
                return;   // (a champion, a challenger, an avenger, a rescuer: a situation steers it)
            }
            // facing a big threat: look around every second; otherwise every ~3 s (huge battles stay cheap)
            BaseSimObject t = f.A.attack_target;
            bool threat = t != null && t.isActor() && t.a != null && Tiers.Power(t.a) >= 1.75f * Tiers.Power(f.A);
            if (!threat)
            {
                _next[f.Id] = now + Random.Range(2.5f, 3.5f);
            }
            Consider(f, now);
        }

        /// <summary>
        /// Forgets the look-around times of fighters that stopped ticking (dead, recycled, no longer
        /// fighters): a live fighter's time is never more than a frame behind the clock. Runs when
        /// the table doubles, so it never grows without end and is never wiped wholesale (every
        /// fighter looking around on the same frame).
        /// </summary>
        private static void Prune(double now)
        {
            _stale.Clear();
            foreach (KeyValuePair<long, double> e in _next)
            {
                if (e.Value < now - 5.0)
                {
                    _stale.Add(e.Key);
                }
            }
            foreach (long id in _stale)
            {
                _next.Remove(id);
            }
            _stale.Clear();
            _pruneAt = Mathf.Max(1024, 2 * _next.Count);
        }

        /// <summary>Once a frame: drop finished parties and lost hunters; the red glint every second.</summary>
        private static void Upkeep(double now)
        {
            for (int i = _hunts.Count - 1; i >= 0; i--)
            {
                Hunt h = _hunts[i];
                for (int j = h.Members.Count - 1; j >= 0; j--)
                {
                    if (!h.Members[j].Valid)
                    {
                        _byMember.Remove(h.Members[j].Id);
                        h.Members.RemoveAt(j);
                        h.Slots.RemoveAt(j);
                    }
                }
                Actor t = h.Target;
                if (h.Members.Count < 2 || t == null || t.Sid() != h.TargetId || !Combat.Living(t) || t.isInsideSomething() || now > h.Until)
                {
                    Dissolve(h);
                    continue;
                }
                if (now >= h.NextMark)
                {
                    h.NextMark = now + 1.0;
                    if (t.is_visible && !Duels.InDuel(t))
                    {
                        Kit.Mark(t, Red, 0.45f, 1f);
                    }
                }
            }
        }

        // ------------------------------------------------------------ parties --

        /// <summary>A tiered fighter in a fight looks for an enemy nobody here can take alone.</summary>
        private static void Consider(Fighter f, double now)
        {
            Actor a = f.A;
            Vector2 p = a.current_position;
            _scan.Clear();
            foreach (Actor u in Finder.getUnitsFromChunk(a.current_tile, 1))
            {
                _scan.Add(u);
            }
            // the strongest enemy within 8 tiles (one locked in a duel is left to its duel)
            Actor e = null;
            float ep = 0f;
            foreach (Actor u in _scan)
            {
                if (u == null || (u.current_position - p).sqrMagnitude > 64f || !Combat.IsFoe(a, u) || Duels.InDuel(u))
                {
                    continue;
                }
                float pw = Tiers.Power(u);
                if (pw > ep)
                {
                    ep = pw;
                    e = u;
                }
            }
            if (e == null)
            {
                return;
            }
            Hunt h = Find(e, a.kingdom);
            if (h != null)
            {
                if (h.Members.Count < 4)
                {
                    Join(h, f);
                }
                return;
            }
            // much stronger than the strongest of its foes near it (this one included)?
            float best = Tiers.Power(a);
            Vector2 ePos = e.current_position;
            foreach (Actor u in _scan)
            {
                if (u == null || u == e || !u.isAlive() || (u.current_position - ePos).sqrMagnitude > 36f || !Combat.IsFoe(e, u))
                {
                    continue;
                }
                best = Mathf.Max(best, Tiers.Power(u));
            }
            if (ep < 1.75f * best)
            {
                return;
            }
            // the party: tiered Adepts and better within 8 tiles, nearest first, not hunting already
            _cands.Clear();
            foreach (Actor u in _scan)
            {
                if (u == null || u == a || !Combat.IsAlly(a, u) || Tiers.Of(u) < Tier.Adept || _byMember.ContainsKey(u.Sid()) || Duels.InDuel(u)
                    || Situations.Engaged(u) || u.isInMagnet() || ControllableUnit.isControllingUnit(u))
                {
                    continue;
                }
                float d2 = (u.current_position - p).sqrMagnitude;
                if (d2 > 64f || !Fighters.TryGet(u, out Fighter g) || !g.Valid)
                {
                    continue;
                }
                _cands.Add(new KeyValuePair<float, Fighter>(d2, g));
            }
            if (_cands.Count == 0)
            {
                return;
            }
            _cands.Sort((x, y) => x.Key.CompareTo(y.Key));
            List<Fighter> party = new List<Fighter> { f };
            for (int i = 0; i < _cands.Count && party.Count < 4; i++)
            {
                party.Add(_cands[i].Value);
            }
            Form(e, party, now);
        }

        /// <summary>The party of this side already hunting e, if any (another kingdom hunts on its own).</summary>
        private static Hunt Find(Actor e, Kingdom side)
        {
            long id = e.Sid();
            foreach (Hunt h in _hunts)
            {
                if (h.TargetId == id && h.Target == e && h.Side == side)
                {
                    return h;
                }
            }
            return null;
        }

        private static void Form(Actor e, List<Fighter> party, double now)
        {
            Hunt h = new Hunt { Target = e, TargetId = e.Sid(), Side = party[0].A.kingdom, Formed = now, Until = now + 25.0, NextMark = now + 1.0 };
            // the toughest takes the front: the side of the hunted enemy that faces the party
            Vector2 mid = Vector2.zero;
            Fighter anchor = party[0];
            foreach (Fighter m in party)
            {
                mid += m.A.current_position;
                if (m.A.getHealth() > anchor.A.getHealth())
                {
                    anchor = m;
                }
            }
            mid /= party.Count;
            Vector2 to = mid - e.current_position;
            h.Front = to.sqrMagnitude > 0.0004f ? Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);
            party.Remove(anchor);
            party.Insert(0, anchor);
            _hunts.Add(h);
            Parties++;
            foreach (Fighter m in party)
            {
                Join(h, m);
            }
            if (e.is_visible)
            {
                Kit.Mark(e, Red, 0.5f, 1.2f);
                foreach (Fighter m in h.Members)
                {
                    float px = Crew.PxOf(m.A);
                    Color c = Tiers.Color(m.Tier);
                    Kit.Flare(m.A.current_position, px, c, 12f * Crew.KOf(m.A), 0.3f);
                    Crew.Link(m.A.current_position + new Vector2(0f, 4f * px), e.current_position + new Vector2(0f, 4f * Crew.PxOf(e)), px, c, 0.22f);
                }
            }
            if (Dev.On)
            {
                Dev.Trace("party of " + h.Members.Count + " hunts " + e.asset.id + "#" + e.Sid() + " (power " + Tiers.Power(e) + ")");
            }
        }

        private static void Join(Hunt h, Fighter f)
        {
            if (_byMember.ContainsKey(f.Id) || h.Members.Count >= 4 || Situations.Engaged(f.A) || f.A.isInMagnet() || ControllableUnit.isControllingUnit(f.A))
            {
                return;   // (held by the god hand or played by the player: not one of the party)
            }
            // the first free slot
            float slot = 0f;
            foreach (float s in SlotAngles)
            {
                if (!h.Slots.Contains(s))
                {
                    slot = s;
                    break;
                }
            }
            h.Members.Add(f);
            h.Slots.Add(slot);
            _byMember[f.Id] = h;
            Engage(h, f);
        }

        private static void Leave(Hunt h, Fighter f)
        {
            int i = h.Members.IndexOf(f);
            if (i >= 0)
            {
                h.Members.RemoveAt(i);
                h.Slots.RemoveAt(i);
            }
            _byMember.Remove(f.Id);
            if (h.Members.Count < 2)
            {
                Dissolve(h);
            }
        }

        private static void Dissolve(Hunt h)
        {
            foreach (Fighter m in h.Members)
            {
                _byMember.Remove(m.Id);
            }
            h.Members.Clear();
            h.Slots.Clear();
            _hunts.Remove(h);
        }

        /// <summary>A hunter keeps at it: on the hunted enemy, from its own side.</summary>
        private static void Keep(Hunt h, Fighter f, double now)
        {
            Actor t = h.Target;
            if (t == null || t.Sid() != h.TargetId || !Combat.Living(t))
            {
                Dissolve(h);
                return;
            }
            float d = Vector2.Distance(f.A.current_position, t.current_position);
            // (still enemies: a hunted master that is a blur for a moment, mid-blink or mid-dash,
            // isn't hittable right now, but the party doesn't break up over it)
            bool hostile = f.A.kingdom != null && t.kingdom != null && f.A.areFoes(t) && !t.isInMagnet();
            if (d > 14f || !hostile || Situations.Engaged(f.A) || ControllableUnit.isControllingUnit(f.A))
            {
                // out of reach, no longer an enemy (or held by the god hand), played by the player,
                // or a situation took it over (champion, challenge, vengeance, rescue): it leaves
                // the party rather than be pulled two ways at once
                if (Dev.On && Situations.Engaged(f.A))
                {
                    Dev.Trace("party: " + f.A.asset.id + "#" + f.Id + " leaves the hunt (a situation steers it)");
                }
                Leave(h, f);
                return;
            }
            if (d <= 4f)
            {
                h.Until = System.Math.Max(h.Until, now + 8.0);
            }
            if (Moves.Busy(f.A) || Director.InScene(f.A) || Duels.InDuel(f.A) || f.A.isInMagnet())
            {
                return;   // mid-move, fighting its own duel (never pulled out of it), or in the god hand
            }
            if (Duels.InDuel(t))
            {
                // the hunted enemy is locked in a duel: the party stands back (it isn't theirs to
                // break) and closes in again if it walks out of it alive
                BaseSimObject on = f.A.attack_target;
                if (on != null && on.isActor() && on.a == t)
                {
                    f.A.clearAttackTarget();
                }
                h.Until = System.Math.Max(h.Until, now + 3.0);
                return;
            }
            if (!Combat.IsFoe(f.A, t))
            {
                return;   // a blur right now (or the like): the party waits it out
            }
            Engage(h, f);
            Flank(h, f, d);
        }

        /// <summary>
        /// Sets the hunter on the hunted enemy (the Brain then fights it). Never mid-move (a master
        /// mid-way through a flowing strike is set on it when that strike is over).
        /// </summary>
        private static void Engage(Hunt h, Fighter f)
        {
            Actor a = f.A;
            Actor t = h.Target;
            if (Moves.Busy(a) || a.isInsideSomething() || a.isInMagnet() || ControllableUnit.isControllingUnit(a))
            {
                return;
            }
            bool on = a.has_attack_target && a.attack_target != null && a.attack_target.isActor() && a.attack_target.a == t;
            if (!on && a.canAttackTarget(t))
            {
                a.startFightingWith(t);
            }
        }

        /// <summary>On the wrong side of it, 2-8 tiles away: a quick dash to its own slot around the enemy.</summary>
        private static void Flank(Hunt h, Fighter f, float d)
        {
            Actor a = f.A;
            // (never cutting its own swing short: only from its feet)
            if (d < 2.2f || d > 8f || a.hasRangeAttack() || Moves.Busy(a) || !Moves.CanPerform(a) || !f.Elapsed("sakuga_flank", 3f))
            {
                return;
            }
            int i = h.Members.IndexOf(f);
            if (i < 0)
            {
                return;
            }
            Actor t = h.Target;
            Vector2 tp = t.current_position;
            float slot = h.Front + h.Slots[i];
            Vector2 rel = a.current_position - tp;
            float cur = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(cur, slot)) < 40f)
            {
                return;   // already coming in from its side
            }
            float r = 1.3f + 0.5f * t.stats["size"];
            foreach (float turn in new[] { 0f, 25f, -25f })
            {
                Vector2 want = tp + Crew.Dir(slot + turn) * r;
                if (Combat.SafeSpot(want) && Vector2.Distance(want, a.current_position) <= 7f && Combat.ClearPath(a.current_position, want))
                {
                    if (FlankStep.Begin(a, want, t))
                    {
                        f.Mark("sakuga_flank");
                    }
                    return;
                }
            }
        }

        // -------------------------------------------------------------- links --

        /// <summary>A hunter left the hunted enemy in the air or on the ground: the nearest free hunter takes over.</summary>
        private static void MoveEnded(Move m, bool aborted)
        {
            if (aborted || m == null || m.EndTarget == null || (m.EndLink != Link.Launched && m.EndLink != Link.Down) || !Settings.TeamTechniques)
            {
                return;
            }
            if (m.A == null || !_byMember.TryGetValue(m.A.Sid(), out Hunt h))
            {
                return;
            }
            Actor t = h.Target;
            if (m.EndTarget != t || t.Sid() != h.TargetId || !Combat.Living(t) || Duels.InDuel(t))
            {
                return;
            }
            // the nearest free hunter with a ready technique that takes the link (a window nobody
            // can use is never opened: Brain's rule)
            Fighter best = null;
            float bd = 9f;
            foreach (Fighter g in h.Members)
            {
                if (g.A == m.A || !g.Valid || !Moves.CanPerform(g.A) || Director.InScene(g.A) || Situations.Engaged(g.A) || Duels.InDuel(g.A))
                {
                    continue;
                }
                float d2 = (g.A.current_position - t.current_position).sqrMagnitude;
                if (d2 <= bd && Crew.CanTake(g, m.EndLink))
                {
                    bd = d2;
                    best = g;
                }
            }
            if (best == null)
            {
                return;
            }
            double now = Fighter.Now;
            best.Link = m.EndLink;
            best.LinkTarget = t;
            best.LinkUntil = now + 0.5;
            best.ThinkIn = 0f;
            Situations.Boost(best, 0.2f, 2.0);
            Handoffs++;
            if (best.A.is_visible)
            {
                float px = Crew.PxOf(best.A);
                Color c = Tiers.Color(best.Tier);
                Crew.Link(m.A.current_position + new Vector2(0f, 4f * Crew.PxOf(m.A)), best.A.current_position + new Vector2(0f, 4f * px), px, c, 0.2f);
                Kit.Mark(best.A, c, 0.3f, 0.8f);
            }
            if (Dev.On)
            {
                Dev.Trace("hand-off " + m.EndLink + " on " + t.asset.id + " from " + m.A.asset.id + "#" + m.A.Sid() + " to " + best.A.asset.id + "#" + best.Id);
            }
        }
    }

    /// <summary>
    /// A hunter's quick dash to its own side of the hunted enemy (an Assault Party flanking).
    ///
    /// Beat sheet: a dust puff and a low lean, a skimming dash (0.14-0.34 s) on a trail of
    /// afterimages in its tier colour, a skid and a springy stop facing the enemy (0.18 s; a
    /// master's 0.08-0.11 s), and it picks its next move at once.
    /// </summary>
    internal sealed class FlankStep : Move
    {
        private readonly Vector2 _from, _to;
        private readonly Actor _t;
        private readonly long _tid;
        private readonly float _dur;
        private readonly Palette _pal;
        private float _ghost;
        private bool _landed;
        private float _lt;

        private FlankStep(Actor a, Vector2 to, Actor target) : base(a)
        {
            _from = a.current_position;
            _to = to;
            _t = target;
            _tid = target.Sid();
            float d = Vector2.Distance(_from, _to);
            _dur = Mathf.Clamp(0.1f + 0.035f * d, 0.14f, 0.34f);
            _pal = Palette.For(a, 2);
            Pose.FaceRight = Crew.Face(_from, _to, a.flip);
        }

        public static bool Begin(Actor a, Vector2 to, Actor target)
        {
            if (!Moves.CanPerform(a) || target == null)
            {
                return false;
            }
            FlankStep m = new FlankStep(a, to, target);
            if (!Moves.Start(m))
            {
                return false;
            }
            if (Fx.OnScreen(m._from, 0.3f))
            {
                Fx.LandingPuff(m._from, m.Px, a.current_tile);
            }
            return true;
        }

        public override void Tick(float dt)
        {
            bool vis = Fx.OnScreen(Pose.Ground, 0.3f);
            if (!_landed)
            {
                float k = Mathf.Clamp01(T / _dur);
                Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
                float lean = Mathf.Sin(Mathf.PI * k);
                Pose.Height = 0.1f * K * lean;
                Pose.Rot = 18f * lean;
                Pose.SX = 1f + 0.14f * lean;
                Pose.SY = 1f - 0.12f * lean;
                if (vis)
                {
                    _ghost -= dt;
                    if (_ghost <= 0f)
                    {
                        _ghost = 0.025f;
                        Fx.Afterimage(this, 0.14f, _pal.Ghost, _pal.GhostEnd);
                    }
                }
                if (k >= 1f)
                {
                    _landed = true;
                    Pose.Height = 0f;
                    if (_t != null && _t.Sid() == _tid && _t.isAlive())
                    {
                        Pose.FaceRight = Crew.Face(_to, _t.current_position, Pose.FaceRight);
                    }
                    if (vis)
                    {
                        Fx.LandingPuff(_to, Px, Combat.TileAt(_to));
                    }
                }
                return;
            }
            // the skid: a springy stop, crisper for a master (Crew.Pace)
            _lt += dt;
            float pace = Crew.Pace(A);
            float amp = Anim.Spring(_lt / pace, 10f, 18f);
            Pose.Rot = -6f * amp;
            Pose.SX = 1f + 0.12f * amp;
            Pose.SY = 1f - 0.14f * amp;
            if (_lt >= 0.18f * pace)
            {
                Pose.Rot = 0f;
                Pose.SX = Pose.SY = 1f;
                Finished = true;
                // in its slot beside the hunted enemy: it picks its next move at once
                if (Fighters.TryGet(A, out Fighter f))
                {
                    f.ThinkIn = 0f;
                }
            }
        }
    }
}
