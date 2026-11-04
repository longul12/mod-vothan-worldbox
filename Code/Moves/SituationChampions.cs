using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CHAMPIONS. A battle with six or more units fighting on each of two sides within 20 tiles
    /// crowns each side's strongest tiered unit its champion (highest tier, then health; at least one
    /// of the two an Adept or better: two Novices are not a story yet): a pillar
    /// of gold light behind each, a gold ring at their feet, glitter, a "CHAMPIONS" card when on
    /// screen, and from then on a gold glint over their heads every 2 s. The two seek each other
    /// (their attack target is set on each other twice a second unless they are already busy with
    /// another tiered foe or a scene), shouldering aside the small fry in their way, so their fight
    /// happens - the duel system takes over when they meet. When a champion falls, the untiered
    /// soldiers of its side within 8 tiles waver (30% each: a 1 s stun and a grey "!") and the
    /// winner roars. The battle ends quietly when the champions part (40+ tiles), stop fighting for
    /// 10 s, or after 90 s; then those two sides wait 30 s before crowning again around there.
    /// </summary>
    internal static class SitChampions
    {
        private const float Radius = 20f;
        private const int MinSide = 6;
        private const float WaverRadius = 8f;

        internal sealed class Battle
        {
            public Kingdom KA, KB;
            public Actor A, B;
            public long IdA, IdB;
            public Tier TA, TB;
            public string NameA, NameB;
            public Vector2 PosA, PosB;
            public double Since, Until, NextSeek, NextGlint, LastFightA, LastFightB;
            public double ShoveA, ShoveB;
            public bool Over;
        }

        private struct Rest
        {
            public Kingdom KA, KB;
            public Vector2 At;
            public double Until;
        }

        private static readonly List<Battle> _battles = new List<Battle>();
        private static readonly List<Rest> _rests = new List<Rest>();
        private static readonly List<Actor> _units = new List<Actor>();
        private static readonly Dictionary<Kingdom, int> _count = new Dictionary<Kingdom, int>();
        private static readonly Dictionary<Kingdom, Actor> _best = new Dictionary<Kingdom, Actor>();
        private static double _nextSeed;
        private static float _nextSeedReal;
        private static int _cursor;

        public static int Count => _battles.Count;

        /// <summary>Is this unit a champion of a battle right now?</summary>
        public static bool Is(Actor a)
        {
            return Situations.RoleOf(a) is Battle;
        }

        /// <summary>Is this unit id a champion of a battle that is still on?</summary>
        public static bool IsId(long id)
        {
            for (int i = 0; i < _battles.Count; i++)
            {
                Battle b = _battles[i];
                if (!b.Over && (b.IdA == id || b.IdB == id))
                {
                    return true;
                }
            }
            return false;
        }

        // -------------------------------------------------------------- seeding --

        /// <summary>Every 1.5 s: one fighter in a fight is the seed of a look for a big battle around it.</summary>
        public static void Seed(double now)
        {
            if (now < _nextSeed || Time.unscaledTime < _nextSeedReal)
            {
                return;
            }
            _nextSeed = now + 1.5;
            _nextSeedReal = Time.unscaledTime + 0.5f;   // at x20 1.5 world seconds pass in a few frames
            List<Fighter> tiered = Situations.Tiered;
            int n = tiered.Count;
            for (int tries = 0; tries < n && tries < 24; tries++)
            {
                if (_cursor >= n)
                {
                    _cursor = 0;
                }
                Fighter f = tiered[_cursor++];
                if (!f.Valid || !f.A.isFighting() || f.A.kingdom == null || Is(f.A) || f.A.current_tile == null)
                {
                    continue;
                }
                if (Covered(f.A, now))
                {
                    continue;
                }
                Look(f.A, now);
                return;   // one look per 1.5 s
            }
        }

        /// <summary>
        /// Already a champions battle (or a recent one) on this battlefield? One pair of champions per
        /// battlefield, whatever the kingdoms: a side's kingdom can change in the middle of a battle
        /// (its soldiers found a village), and a second pair crowned in the same melee (an Adept
        /// "champion" against a Grandmaster who already is one in all but name) reads as noise.
        /// </summary>
        private static bool Covered(Actor a, double now)
        {
            Vector2 p = a.current_position;
            foreach (Battle b in _battles)
            {
                if (!b.Over && ((b.PosA - p).sqrMagnitude < 900f || (b.PosB - p).sqrMagnitude < 900f))
                {
                    return true;
                }
            }
            foreach (Rest r in _rests)
            {
                if (r.Until > now && (r.At - p).sqrMagnitude < 900f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Is this unit in the battle? Fighting (the game's attack target), performing (a technique
        /// or a flow move clears the target between blows), in combat these 3 s, or close to the
        /// seed (a side's strongest walking up to the clash is in it too).
        /// </summary>
        private static bool InBattle(Actor u, Vector2 seed)
        {
            if (u.isFighting() || Moves.Busy(u))
            {
                return true;
            }
            if (Fighters.TryGet(u, out Fighter f) && f.InCombat)
            {
                return true;
            }
            return (u.current_position - seed).sqrMagnitude <= 144f;
        }

        private static void Look(Actor seed, double now)
        {
            Vector2 sp = seed.current_position;
            Situations.Around(sp, Radius, _units);
            _count.Clear();
            _best.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                Kingdom k = u.kingdom;
                if (k == null || u.asset.is_boat || u.isInsideSomething() || u.isBaby() || u.isTask("run_away"))
                {
                    continue;
                }
                bool fighting = u.isFighting() || Moves.Busy(u);
                if (fighting)
                {
                    _count.TryGetValue(k, out int c);
                    _count[k] = c + 1;
                }
                // the champion is the strongest of its side on the field, whether or not it has a
                // target this very instant (round 3: the orcs' Grandmaster, between two moves,
                // lost the crown to one of its Experts)
                if (Tiers.Of(u) == Tier.None || !(fighting || InBattle(u, sp)))
                {
                    continue;
                }
                if (!_best.TryGetValue(k, out Actor cur) || Better(u, cur))
                {
                    _best[k] = u;
                }
            }
            _units.Clear();
            Kingdom ka = seed.kingdom;
            if (!_count.TryGetValue(ka, out int na) || na < MinSide)
            {
                return;
            }
            // the enemy side with the most fighters here
            Kingdom kb = null;
            int nb = 0;
            foreach (KeyValuePair<Kingdom, int> kv in _count)
            {
                if (kv.Key == ka || kv.Value < MinSide || kv.Value <= nb || !_best.ContainsKey(kv.Key))
                {
                    continue;
                }
                Actor rep = _best[kv.Key];
                if (!seed.areFoes(rep))
                {
                    continue;
                }
                kb = kv.Key;
                nb = kv.Value;
            }
            if (kb == null || !_best.TryGetValue(ka, out Actor a) || !_best.TryGetValue(kb, out Actor b))
            {
                return;
            }
            // the fight is already on: when one side's best is at grips with a tiered enemy of the
            // other side's best tier (its duel, its target), those two are the champions, so the
            // card names the clash that is happening rather than two who will never meet
            Actor fa = AtGrips(b, ka);
            if (fa != null && fa != a && Tiers.Of(fa) >= Tiers.Of(a))
            {
                a = fa;
            }
            else
            {
                Actor fb = AtGrips(a, kb);
                if (fb != null && fb != b && Tiers.Of(fb) >= Tiers.Of(b))
                {
                    b = fb;
                }
            }
            if (Situations.Engaged(a) || Situations.Engaged(b) || !Combat.IsFoe(a, b) || Tiers.Of(a) < Tier.Adept && Tiers.Of(b) < Tier.Adept)
            {
                return;
            }
            Crown(a, b, now);
        }

        /// <summary>The tiered enemy of 'side' this unit is at grips with (its duel opponent, else its target), or null.</summary>
        private static Actor AtGrips(Actor x, Kingdom side)
        {
            Actor o = Duels.Opponent(x);
            if (o == null && x.has_attack_target && x.attack_target != null && x.attack_target.isActor())
            {
                o = x.attack_target.a;
            }
            if (o == null || o == x || !o.isAlive() || o.kingdom != side || Tiers.Of(o) == Tier.None || Situations.Engaged(o) || !Combat.IsFoe(x, o))
            {
                return null;
            }
            return o;
        }

        /// <summary>Who leads a side: the highest tier, then the healthiest, then the most experienced.</summary>
        private static bool Better(Actor u, Actor cur)
        {
            Tier tu = Tiers.Of(u), tc = Tiers.Of(cur);
            if (tu != tc)
            {
                return tu > tc;
            }
            float hu = Combat.HpPct(u), hc = Combat.HpPct(cur);
            if (Mathf.Abs(hu - hc) > 0.05f)
            {
                return hu > hc;
            }
            return u.data.kills > cur.data.kills;
        }

        // -------------------------------------------------------------- crowning --

        private static void Crown(Actor a, Actor b, double now)
        {
            Battle bt = new Battle
            {
                KA = a.kingdom,
                KB = b.kingdom,
                A = a,
                B = b,
                IdA = a.Sid(),
                IdB = b.Sid(),
                TA = Tiers.Of(a),
                TB = Tiers.Of(b),
                NameA = Situations.Name(a),
                NameB = Situations.Name(b),
                PosA = a.current_position,
                PosB = b.current_position,
                Since = now,
                Until = now + 90.0,
                NextSeek = now + 0.6,
                NextGlint = now + 2.0,
                LastFightA = now,
                LastFightB = now
            };
            _battles.Add(bt);
            Situations.Claim(a, bt);
            Situations.Claim(b, bt);
            Dev.Watch(Vector2.Lerp(a.current_position, b.current_position, 0.5f) + new Vector2(0f, 2f));
            Flare(a);
            Flare(b);
            Anim.Sound("event:/SFX/POWERS/Inspiration", a.current_tile);
            Vector2 mid = Vector2.Lerp(a.current_position, b.current_position, 0.5f);
            bool seenA = Fx.OnScreen(a.current_position, 0.05f);
            bool seen = seenA || Fx.OnScreen(b.current_position, 0.05f);
            if (seen && Settings.TitleCards && !Cinema.Playing)
            {
                // on screen: the card says it (a tip would sit on top of it). The card asks for its
                // spot on screen: the one in view (the middle between them may be well off it)
                Cinema.Title(Locale.Get("sakuga_sit_title_champions"), SitFx.Gold, 1.7f, seenA ? a.current_position : b.current_position, 0.8f);
            }
            else if (seen || bt.TA >= Tier.Master && bt.TB >= Tier.Master)
            {
                // no card: a line at the top (two Masters are news even off screen)
                Situations.Tip(Locale.Format("sakuga_sit_champions", bt.NameA, bt.NameB), Situations.Hex(SitFx.Gold), mid, !seen);
            }
            if (Dev.On)
            {
                Dev.Trace("situation champions: " + a.asset.id + "#" + bt.IdA + " (" + bt.TA + ") vs " + b.asset.id + "#" + bt.IdB + " (" + bt.TB + ")");
            }
        }

        /// <summary>The champion's flare: a pillar of gold light behind it, a gold ring, glitter, a big glint.</summary>
        private static void Flare(Actor a)
        {
            if (!a.is_visible)
            {
                return;
            }
            Vector2 feet = SitFx.Feet(a);
            if (!Kit.Visible(feet))
            {
                return;
            }
            float px = SitFx.PxOf(a);
            float k = SitFx.KOf(a);
            SitFx.Pillar(feet, px, 34f * k, 0.5f, SitFx.GoldPale, SitFx.Gold, 2);
            Fx.Ring(feet, px, 0.5f, 3f, 20f * k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f,
                Fx.WithA(SitFx.GoldPale, 1f), Fx.WithA(SitFx.Gold, 0.85f), Fx.WithA(SitFx.Gold, 0f));
            SitFx.Glitter(feet, px, 9, 26f * k, SitFx.Gold);
            SitFx.HeadGlint(a, SitFx.GoldPale, 0.32f, 1.3f, new Vector2(0f, 3f));
            new SitAura(a, SitFx.Clear, Fx.WithA(SitFx.GoldPale, 0.55f), false, true).End(0.4f);
        }

        /// <summary>
        /// Staging (Situations.Force): the strongest of this unit's side and the strongest tiered enemy
        /// within 30 tiles of it are crowned, crowd or not (it waits until the enemy is in sight).
        /// </summary>
        internal static Situations.Stage Stage(Actor a, bool last, out string msg)
        {
            Actor c = a.kingdom != null ? Situations.Pick(a, Tier.God) : null;
            if (c == null)
            {
                msg = "champions: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            if (Is(c))
            {
                msg = "champions: " + c.asset.id + "#" + c.Sid() + " already is one";
                return Situations.Stage.Done;
            }
            if (Situations.Engaged(c))
            {
                msg = "champions: " + c.asset.id + "#" + c.Sid() + " is busy with another situation";
                return Situations.Stage.Wait;
            }
            Situations.Around(c.current_position, 30f, _units);
            Actor best = null;
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u != c && Tiers.Of(u) != Tier.None && u.kingdom != null && Combat.IsFoe(c, u) && !Situations.Engaged(u) && (best == null || Better(u, best)))
                {
                    best = u;
                }
            }
            _units.Clear();
            if (best == null)
            {
                msg = "champions: no tiered enemy within 30 tiles of " + c.asset.id + "#" + c.Sid();
                return last ? Situations.Stage.Fail : Situations.Stage.Wait;
            }
            if (!last && (best.current_position - c.current_position).sqrMagnitude > 14f * 14f)
            {
                // the moment: the two armies are closing in (it reads best just before they clash)
                msg = "champions: waiting for the armies to close in";
                return Situations.Stage.Wait;
            }
            Situations.Show(Vector2.Lerp(c.current_position, best.current_position, 0.5f) + new Vector2(0f, 2f));
            Crown(c, best, Fighter.Now);
            msg = "champions: " + c.asset.id + "#" + c.Sid() + " (" + Tiers.Of(c) + ") vs " + best.asset.id + "#" + best.Sid() + " (" + Tiers.Of(best) + ")";
            return Situations.Stage.Done;
        }

        // --------------------------------------------------------------- update --

        public static void Update(float dt, double now)
        {
            if (_roars.Count > 0)
            {
                Roars(now);
            }
            for (int i = _battles.Count - 1; i >= 0; i--)
            {
                if (i >= _battles.Count)
                {
                    continue;
                }
                Battle b = _battles[i];
                if (b.Over)
                {
                    _battles.RemoveAt(i);
                    continue;
                }
                bool aliveA = b.A != null && b.A.Sid() == b.IdA && b.A.isAlive();
                bool aliveB = b.B != null && b.B.Sid() == b.IdB && b.B.isAlive();
                // positions, and sides: a side's kingdom may change mid-battle (a village founded)
                if (aliveA)
                {
                    b.PosA = b.A.current_position;
                    b.KA = b.A.kingdom ?? b.KA;
                }
                if (aliveB)
                {
                    b.PosB = b.B.current_position;
                    b.KB = b.B.kingdom ?? b.KB;
                }
                if (!aliveA || !aliveB)
                {
                    // fell without a kill being credited (fire, a god power): no killer, the rival is the winner
                    Fall(b, null, !aliveA);
                    continue;
                }
                // fighting: a target, or a move (a technique or a flow move clears the target between blows)
                if (b.A.isFighting() || Moves.Busy(b.A))
                {
                    b.LastFightA = now;
                }
                if (b.B.isFighting() || Moves.Busy(b.B))
                {
                    b.LastFightB = now;
                }
                if (now > b.Until || (b.PosA - b.PosB).sqrMagnitude > 1600f || now - b.LastFightA > 10.0 || now - b.LastFightB > 10.0)
                {
                    End(b, now);
                    continue;
                }
                if (now >= b.NextGlint)
                {
                    b.NextGlint = now + 2.0;
                    SitFx.HeadGlint(b.A, SitFx.Gold, 0.36f, 1.05f, new Vector2(0f, 3f));
                    SitFx.HeadGlint(b.B, SitFx.Gold, 0.36f, 1.05f, new Vector2(0f, 3f));
                }
                if (now >= b.NextSeek)
                {
                    b.NextSeek = now + 0.5;
                    Seek(b.A, b.B, ref b.ShoveA, now);
                    Seek(b.B, b.A, ref b.ShoveB, now);
                }
            }
        }

        /// <summary>A champion goes for the other: it turns on it and shoulders through the crowd between them.</summary>
        private static void Seek(Actor c, Actor rival, ref double shoveAt, double now)
        {
            if (!Situations.Free(c))
            {
                return;
            }
            float d = Vector2.Distance(c.current_position, rival.current_position);
            if (d > 32f)
            {
                return;
            }
            Situations.Steer(c, rival, true);
            if (d > 2.5f && now >= shoveAt && c.has_attack_target && c.attack_target == rival)
            {
                if (Shove(c, rival))
                {
                    shoveAt = now + 0.7;
                }
            }
        }

        /// <summary>The nearest small enemy right in its path is shoved aside (no damage, a stumble).</summary>
        private static bool Shove(Actor c, Actor rival)
        {
            Vector2 p = c.current_position;
            Vector2 dir = (rival.current_position - p).normalized;
            Tier tc = Tiers.Of(c);
            Situations.Around(p, 1.3f, _units);
            Actor best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u == c || u == rival || !Combat.IsFoe(c, u) || Moves.Busy(u) || !Combat.Grounded(u) || Weights.Heavy(u) || Situations.Possessed(u))
                {
                    continue;
                }
                if (Tiers.Of(u) >= tc || Weights.Of(u) > Weight.Brute)
                {
                    continue;
                }
                Vector2 rel = u.current_position - p;
                float along = Vector2.Dot(rel, dir);
                if (along < 0.1f || along / Mathf.Max(0.01f, rel.magnitude) < 0.55f)
                {
                    continue;
                }
                if (along < bestD)
                {
                    bestD = along;
                    best = u;
                }
            }
            _units.Clear();
            if (best == null)
            {
                return false;
            }
            Vector2 rel2 = best.current_position - p;
            float side = rel2.x * dir.y - rel2.y * dir.x >= 0f ? 1f : -1f;
            Vector2 push = (new Vector2(dir.y, -dir.x) * side * 0.85f + dir * 0.3f).normalized;
            if (!Combat.SafeSpot(best.current_position + push * 0.9f))
            {
                // not into the river: the other way, or not at all
                push = (new Vector2(-dir.y, dir.x) * side * 0.85f + dir * 0.3f).normalized;
                if (!Combat.SafeSpot(best.current_position + push * 0.9f))
                {
                    return false;
                }
            }
            Combat.LaunchDir(best, push, 0.9f, 0.25f, 0f);
            float px = SitFx.PxOf(c);
            Vector2 at = Vector2.Lerp(p, best.current_position, 0.6f) + new Vector2(0f, 3f * px);
            if (Kit.Visible(at) && c.is_visible)
            {
                Fx.HitSpark(at, px, 0, push, 2);
                Fx.LandingPuff(best.current_position, px, best.current_tile);
            }
            return true;
        }

        // ------------------------------------------------------------- the fall --

        /// <summary>Hooks.Killed (through SitKills): was it a champion?</summary>
        public static void OnKilled(long victimId, Actor killer, Vector2 at)
        {
            for (int i = 0; i < _battles.Count; i++)
            {
                Battle b = _battles[i];
                if (b.Over)
                {
                    continue;
                }
                if (b.IdA == victimId)
                {
                    b.PosA = at;
                    Fall(b, killer, true);
                    return;
                }
                if (b.IdB == victimId)
                {
                    b.PosB = at;
                    Fall(b, killer, false);
                    return;
                }
            }
        }

        private static void Fall(Battle b, Actor killer, bool sideA)
        {
            if (b.Over)
            {
                return;
            }
            b.Over = true;
            Situations.Unclaim(b.IdA, b);
            Situations.Unclaim(b.IdB, b);
            Rested(b);
            Kingdom side = sideA ? b.KA : b.KB;
            Vector2 at = sideA ? b.PosA : b.PosB;
            Actor rival = sideA ? b.B : b.A;
            long rivalId = sideA ? b.IdB : b.IdA;
            Tier fallenTier = sideA ? b.TA : b.TB;
            // the winner: whoever struck it down, or the other champion if nobody did
            Actor winner = killer != null && killer.isAlive() && killer.kingdom != side ? killer : null;
            if (winner == null && rival != null && rival.Sid() == rivalId && rival.isAlive() && (rival.current_position - at).sqrMagnitude < 144f)
            {
                winner = rival;
            }
            Waver(side, at);
            if (winner != null)
            {
                Roar(winner);
                if (Fighters.TryGet(winner, out Fighter wf))
                {
                    Situations.Boost(wf, 0.2f, 6.0);
                }
                Progression.AddSxp(winner, 25f * Mathf.Max(1, (int)fallenTier));
                Progression.Trial(winner, 2f, false);
            }
            Situations.Tip(Locale.Format("sakuga_sit_champion_fell", sideA ? b.NameA : b.NameB), Situations.Hex(SitFx.Grey), at, fallenTier >= Tier.Master);
            if (Dev.On)
            {
                Dev.Trace("situation champion fell: #" + (sideA ? b.IdA : b.IdB) + (winner != null ? " winner " + winner.asset.id + "#" + winner.Sid() : ""));
            }
        }

        /// <summary>The fallen champion's untiered soldiers near it lose heart for a moment.</summary>
        private static void Waver(Kingdom side, Vector2 at)
        {
            if (side == null)
            {
                return;
            }
            Situations.Around(at, WaverRadius, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                // (never one sheltering in a house or a boat: the stun would pull it out onto the
                // field; nor one in the god hand)
                if (u.kingdom != side || Tiers.Of(u) != Tier.None || Moves.Busy(u) || Director.InScene(u) || !u.hasEmotions() || u.isBaby()
                    || !Situations.Afield(u) || Situations.Possessed(u) || Random.value > 0.3f)
                {
                    continue;
                }
                u.makeStunned(1f);
                SitFx.Mark(u, SitFx.Bang, SitFx.Grey, 1.1f, 2f);
            }
            _units.Clear();
        }

        /// <summary>A winner still in the move that felled its rival: it roars when that move is done.</summary>
        private struct PendingRoar
        {
            public Actor A;
            public long Id;
            public Move Was;
            public double Until;
        }

        private static readonly List<PendingRoar> _roars = new List<PendingRoar>();

        /// <summary>
        /// The winner's roar: a pose if it is free, the rings either way. A winner still in the move
        /// that felled its rival (the flow strike's finisher, a technique) is not cut short: it roars
        /// the moment that move is done (a fresh flow strike started meanwhile gives way to the roar),
        /// within 1.2 s; past that it shouts on the move (the rings alone).
        /// </summary>
        private static void Roar(Actor w)
        {
            if (Moves.TryGet(w, out Move cur))
            {
                if (_roars.Count < 8)
                {
                    _roars.Add(new PendingRoar { A = w, Id = w.Sid(), Was = cur, Until = Fighter.Now + 1.2 });
                    return;
                }
            }
            else if (Situations.CanAct(w) && SitRoarMove.Begin(w))
            {
                return;
            }
            Shout(w);
        }

        private static void Shout(Actor w)
        {
            SitRoarMove.Rings(w, SitFx.Head(w, SitFx.HeadUp(w)), SitFx.PxOf(w), SitFx.KOf(w), Tiers.Of(w));
        }

        private static void Roars(double now)
        {
            for (int i = _roars.Count - 1; i >= 0; i--)
            {
                PendingRoar r = _roars[i];
                Actor w = r.A;
                if (w == null || w.Sid() != r.Id || !w.isAlive())
                {
                    _roars.RemoveAt(i);
                    continue;
                }
                if (now > r.Until)
                {
                    // still busy when the moment passed: it shouts on the move (a stale one, after
                    // the situations were switched off a while, is simply dropped)
                    _roars.RemoveAt(i);
                    if (now - r.Until < 0.25)
                    {
                        Shout(w);
                    }
                    continue;
                }
                bool busy = Moves.TryGet(w, out Move cur);
                // free again, or only just into a new light move (a flow strike, a blur dash): now
                bool ready = !busy || cur != r.Was && cur.Interruptible;
                if (ready && Situations.CanAct(w))
                {
                    _roars.RemoveAt(i);
                    if (!SitRoarMove.Begin(w))
                    {
                        Shout(w);
                    }
                    continue;
                }
                if (!busy)
                {
                    // free but can't pose (a scene, its duel waiting for it): the shout alone
                    _roars.RemoveAt(i);
                    Shout(w);
                }
            }
        }

        // ------------------------------------------------------------ the end --

        private static void End(Battle b, double now)
        {
            if (b.Over)
            {
                return;
            }
            b.Over = true;
            Situations.Unclaim(b.IdA, b);
            Situations.Unclaim(b.IdB, b);
            Rested(b);
            if (Dev.On)
            {
                Dev.Trace("situation champions part: #" + b.IdA + " #" + b.IdB + " after " + (now - b.Since).ToString("F1") + " s");
            }
        }

        private static void Rested(Battle b)
        {
            _rests.Add(new Rest { KA = b.KA, KB = b.KB, At = Vector2.Lerp(b.PosA, b.PosB, 0.5f), Until = Fighter.Now + 30.0 });
        }

        public static void Prune(double now)
        {
            _rests.RemoveAll(r => r.Until < now);
        }

        public static void Clear()
        {
            _roars.Clear();
            _battles.Clear();
            _rests.Clear();
            _units.Clear();
            _count.Clear();
            _best.Clear();
            _nextSeed = 0;
            _nextSeedReal = 0f;
            _cursor = 0;
        }
    }

    /// <summary>
    /// A fallen champion's conqueror roars.
    ///
    /// Beat sheet (game seconds):
    ///
    ///   REAR    0.12  it leans back, chest out (stretch tall, head tipped back)
    ///   ROAR    0.30  three rings burst from its head one after another, white then its tier colour,
    ///                 a ring of dust at its feet, it trembles with the shout
    ///   SETTLE  0.15  springs back into its stance
    ///
    /// A Master or better roars in a third of a second or less (Situations.Pace; the shout never
    /// under 0.15 s) and is fighting again at once. The shout marks the ground for real: a
    /// Grandmaster's scorches it around its feet, a God's blasts a crater half again as wide.
    /// </summary>
    internal sealed class SitRoarMove : Move
    {
        /// <summary>A champion's roar: rings bursting from it (no blow, but readers see the area it claims).</summary>
        public override Form Forms => Form.Area;

        private readonly float TRear;
        private readonly float TRoar;
        private readonly float TSettle;

        private readonly Vector2 _at;
        private readonly float _k;
        private readonly Tier _tier;
        private int _phase;
        private float _pt;
        private bool _entered;

        private SitRoarMove(Actor a) : base(a)
        {
            _at = a.current_position;
            _k = K;
            _tier = Tiers.Of(a);
            float pace = Situations.Pace(_tier);
            TRear = 0.12f * pace;
            TRoar = Situations.Beat(0.3f, pace, 0.15f);
            TSettle = 0.15f * pace;
        }

        public static bool Begin(Actor a)
        {
            return Moves.CanPerform(a) && Moves.Start(new SitRoarMove(a));
        }

        /// <summary>The shout itself: rings from the head (also used when the winner is busy).</summary>
        public static void Rings(Actor a, Vector2 head, float px, float k, Tier tier)
        {
            if (!Kit.Visible(head) || !a.is_visible || SitFx.Hidden(a))
            {
                return;
            }
            Color c = Tiers.Color(tier);
            for (int i = 0; i < 3; i++)
            {
                Particle r = Fx.Ring(head, px, 0.32f, 3f, (12f + 7f * i) * k, 1, 1, FxLayer.Top, 4, 0.8f, 2f,
                    Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.7f), Fx.WithA(c, 0f));
                if (r != null)
                {
                    r.Delay = 0.08f * i;
                }
            }
            Fx.FocusLines(head, px, Fx.Count(8), 10f * k, 18f * k, 0.12f);
        }

        private bool Visible => Fx.OnScreen(_at, 0.3f);

        public override void Tick(float dt)
        {
            _pt += dt;
            float px = Px;
            Pose.Ground = _at;
            Pose.Height = 0f;
            switch (_phase)
            {
                case 0:
                    {
                        float k = Anim.OutQuad(Mathf.Clamp01(_pt / TRear));
                        Pose.SX = Mathf.Lerp(1f, 0.92f, k);
                        Pose.SY = Mathf.Lerp(1f, 1.12f, k);
                        Pose.Rot = -12f * k;
                        if (_pt >= TRear)
                        {
                            _phase = 1;
                            _pt = 0f;
                            _entered = false;
                        }
                        break;
                    }
                case 1:
                    {
                        if (!_entered)
                        {
                            _entered = true;
                            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                            if (Visible)
                            {
                                Rings(A, HeadPos(), px, _k, _tier);
                                Kit.DustRing(_at, px, A.current_tile, 8, 36f, 0.5f, _k);
                                Kit.Shake(_at, _tier >= Tier.God ? 0.22f : 0.15f, _tier >= Tier.God ? 0.007f : 0.004f, _k);
                            }
                            if (_tier >= Tier.Grandmaster)
                            {
                                // the shout itself marks the ground (on or off screen)
                                Kit.Ravage(_at, (_tier >= Tier.God ? 3f : 2f) * Mathf.Max(1f, _k), _tier >= Tier.God ? 2 : 1, A);
                            }
                        }
                        Pose.SX = 0.94f;
                        Pose.SY = 1.1f;
                        Pose.Rot = -10f;
                        Pose.Nudge = new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                        if (_pt >= TRoar)
                        {
                            Pose.Nudge = Vector2.zero;
                            _phase = 2;
                            _pt = 0f;
                        }
                        break;
                    }
                default:
                    {
                        float amp = Anim.Spring(_pt, 10f, 20f);
                        Pose.SX = 1f - 0.06f * amp;
                        Pose.SY = 1f + 0.1f * amp;
                        Pose.Rot = -10f * amp;
                        if (_pt >= TSettle)
                        {
                            Pose.SX = Pose.SY = 1f;
                            Pose.Rot = 0f;
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        public override void OnEnd(bool aborted)
        {
            if (!aborted)
            {
                Situations.Resume(A);   // the battle goes on: so does it
            }
        }
    }
}
