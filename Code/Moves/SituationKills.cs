using System;
using System.Collections.Generic;
using ai.behaviours;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The situations a death sets off. Kills (Hooks.Killed) and technique kills (Hooks.HitDealt) are
    /// noted when they happen (a few field reads) and answered on the next frame, outside whatever
    /// code raised them.
    ///
    ///   ROUT       a Grandmaster or God killing three enemies within one second breaks the enemy line:
    ///              each weak Folk-sized enemy (with feelings: not the undead) within 6.5 tiles -
    ///              untiered, or a Novice before a Grandmaster, an Adept too before a God - may (half
    ///              of them, two thirds before a God) break and flee for 3 s: a grey "!" and a sprint
    ///              straight away from it (the game's own run_away task, aimed). A pressure ring of its
    ///              colours rolls out; a Grandmaster's scorches the ground at its feet, a God's tears
    ///              a swath through the line as it goes. No rout where nobody could break (two at least).
    ///   UNDERDOG   a nobody (untiered or a Novice, person-sized) lands the killing blow on a Master or
    ///              better: the world slows for a breath where the player is looking, a pillar of white
    ///              gold rises behind it, it flashes, glitter falls, "An unknown X has felled the Combat
    ///              Master Y!", a great deal of Sakuga XP and a promotion trial (x3).
    ///   VENGEANCE  a tiered comrade falls: the strongest free tiered ally (Adept+) within 8 tiles that may
    ///              fight the killer and is not in a duel of its own or facing its match (65%; 40% if
    ///              it must drop a weaker tiered enemy for it; one between flow strikes is free)
    ///              swears vengeance - it whips round, the anger vein pops, red flashes - and goes
    ///              for the killer for 15 s: its blows on the killer land 25% harder, its techniques
    ///              25% harder. If it kills the killer, vengeance is done (Sakuga XP and a trial);
    ///              otherwise it ends when the killer dies or escapes, or in 15 s.
    ///   LESSON     a Master+ kills with a technique: up to two Novices or Adepts of its side within 6
    ///              tiles who are in the fight learned something (a gold chevron rises over them, Sakuga
    ///              XP); one that knows the same technique tries it at once (if its own judgement finds a
    ///              use for it), the others think at once, inspired (+15% damage for 5 s).
    /// </summary>
    internal static class SitKills
    {
        private struct Note
        {
            public Actor Killer;
            public long KillerId;
            public Tier KillerTier;
            public Weight KillerWeight;
            public long VictimId;
            public Tier VictimTier;
            public Kingdom VictimSide;
            public string VictimName;
            public Vector2 At;
            public bool Rout;
            public double When;
        }

        private struct Lesson
        {
            public Actor Mentor;
            public long MentorId;
            public Technique Tech;
            public double When;
        }

        private sealed class Spree
        {
            public double T1 = -999, T2 = -999, LastRout = -999;
        }

        private struct Flight
        {
            public Actor U;
            public long Id;
            public double Until;
        }

        private sealed class Oath
        {
            public Fighter F;
            public Actor A;
            public long Id;
            public Actor Killer;
            public long KillerId;
            public double Until, NextSteer;
        }

        /// <summary>
        /// A rout spreading: the line breaks as its soldiers find their feet. Those the killing blow
        /// threw into the air (or stunned) are judged when they land, within 1.6 s.
        /// </summary>
        private sealed class Wave
        {
            public Actor K;
            public long KId;
            public Tier Tier;
            public Vector2 At;
            public float Chance;
            public double Until, Next;
            public int Broke;
            public readonly HashSet<long> Judged = new HashSet<long>();
        }

        /// <summary>
        /// A God's rout marks the ground for real: a swath torn through the line that breaks (blasted
        /// open near the God, scorched further out), widening as it goes, spreading outward with the
        /// front of the pressure ring (one Kit.Ravage every 1.4 tiles: five or so in all).
        /// </summary>
        private sealed class Swath
        {
            public Actor By;
            public long ById;
            public Vector2 From, Dir;
            public Color Heat;
            /// <summary>The pressure ring it follows: its pixel size, radii (pixels), how much flatter it is along Dir.</summary>
            public float Px, R0, R1, Stretch;
            /// <summary>Tiles long, tiles burned so far, seconds since the ring left.</summary>
            public float Len, Done, Age;
        }

        private const float RoutRadius = 6.5f;
        /// <summary>The rout's pressure ring (life, start radius in pixels, ease): the swath keeps pace with it.</summary>
        private const float RingLife = 0.6f, RingR0 = 4f, RingEase = 2.2f;
        private const float SwathStep = 1.4f;

        private static readonly List<Note> _notes = new List<Note>();
        private static readonly List<Swath> _swaths = new List<Swath>();
        private static readonly List<Lesson> _lessons = new List<Lesson>();
        private static readonly Dictionary<long, Spree> _sprees = new Dictionary<long, Spree>();
        private static readonly List<Flight> _flights = new List<Flight>();
        private static readonly List<Wave> _waves = new List<Wave>();
        /// <summary>A staged lesson: taught when its mentor's technique ends.</summary>
        private static Lesson _staged;
        private static readonly List<Oath> _oaths = new List<Oath>();
        private static readonly Dictionary<long, double> _oathReady = new Dictionary<long, double>();
        private static readonly Dictionary<long, double> _studentReady = new Dictionary<long, double>();
        private static readonly Dictionary<long, double> _mentorReady = new Dictionary<long, double>();
        private static readonly List<Actor> _units = new List<Actor>();
        private static readonly List<Fighter> _pick = new List<Fighter>();
        private static Ctx _ctx;

        public static int Pending => _notes.Count + _lessons.Count + _flights.Count + _oaths.Count + _waves.Count + _swaths.Count + (_staged.Mentor != null ? 1 : 0);

        // ---------------------------------------------------------------- events --

        /// <summary>
        /// Hooks.Killed (every death in the world): most are none of our business and cost a couple of
        /// trait lookups; a tiered victim, a Grandmaster's spree, a champion or an oath's killer is noted.
        /// </summary>
        public static void OnKilled(Actor killer, Actor victim)
        {
            if (!Situations.Enabled || killer == null || victim == null || _notes.Count >= 48 || !Situations.MainThread)
            {
                return;
            }
            try
            {
                long victimId = victim.Sid();
                Tier vt = Tiers.Of(victim);
                Tier kt = Tiers.Of(killer);
                bool rout = kt >= Tier.Grandmaster && killer.kingdom != victim.kingdom && Count(killer.Sid());
                if (vt == Tier.None && !rout && !Watched(victimId))
                {
                    return;
                }
                if (kt == Tier.Adept && Progression.Years(killer) < 0.01f)
                {
                    kt = Tier.Novice;   // this very kill promoted it: still the nobody who did it
                }
                _notes.Add(new Note
                {
                    Killer = killer,
                    KillerId = killer.Sid(),
                    KillerTier = kt,
                    KillerWeight = Weights.Species(killer),
                    VictimId = victimId,
                    VictimTier = vt,
                    VictimSide = victim.kingdom,
                    VictimName = vt != Tier.None ? Situations.Name(victim) : null,
                    At = victim.current_position,
                    Rout = rout,
                    When = Fighter.Now
                });
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] situation kill: " + e);
            }
        }

        /// <summary>Is this unit a champion, an avenger or an avenger's quarry (its death ends something)?</summary>
        private static bool Watched(long id)
        {
            if (SitChampions.Count > 0 && SitChampions.IsId(id))
            {
                return true;
            }
            for (int i = 0; i < _oaths.Count; i++)
            {
                if (_oaths[i].Id == id || _oaths[i].KillerId == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Three kills within a second? (the last three kill times of this killer)</summary>
        private static bool Count(long id)
        {
            double now = Fighter.Now;
            if (!_sprees.TryGetValue(id, out Spree s))
            {
                s = new Spree();
                _sprees[id] = s;
            }
            bool rout = now - s.T1 <= 1.0 && now - s.LastRout > 8.0;
            s.T1 = s.T2;
            s.T2 = now;
            if (rout)
            {
                s.LastRout = now;
            }
            return rout;
        }

        /// <summary>Hooks.HitDealt: a Master's technique killed someone (a lesson for the young ones watching).</summary>
        public static void OnHit(Actor a, Actor v, HitResult r, Move m)
        {
            if (!r.Killed || m == null || m.Tech == null || a == null || !Situations.Enabled || _lessons.Count >= 8 || !Situations.MainThread)
            {
                return;
            }
            if (Tiers.Of(a) < Tier.Master)
            {
                return;
            }
            _lessons.Add(new Lesson { Mentor = a, MentorId = a.Sid(), Tech = m.Tech, When = Fighter.Now });
        }

        // --------------------------------------------------------------- process --

        public static void Process(double now)
        {
            if (_notes.Count > 0)
            {
                for (int i = 0; i < _notes.Count; i++)
                {
                    Note n = _notes[i];
                    if (now - n.When > 1.5)
                    {
                        continue;   // noted while nobody was ticking: too late to be a moment
                    }
                    // (the killer as it was: a pooled actor recycled since is no one's winner)
                    SitChampions.OnKilled(n.VictimId, n.Killer != null && n.Killer.Sid() == n.KillerId ? n.Killer : null, n.At);
                    Oaths(n, now);
                    if (n.Rout)
                    {
                        Rout(n);
                    }
                    if (n.VictimTier >= Tier.Master && n.KillerTier <= Tier.Novice && n.KillerWeight <= Weight.Folk && n.VictimSide != null)
                    {
                        Underdog(n, true);
                    }
                    if (n.VictimTier != Tier.None && n.VictimSide != null)
                    {
                        Vengeance(n, now);
                    }
                }
                _notes.Clear();
            }
            if (_lessons.Count > 0)
            {
                for (int i = 0; i < _lessons.Count; i++)
                {
                    if (now - _lessons[i].When <= 1.5)
                    {
                        Teach(_lessons[i], now);
                    }
                }
                _lessons.Clear();
            }
        }

        // ------------------------------------------------------------------ rout --

        /// <summary>
        /// The killer's pressure rolls out over the enemy line (a white-hot ring in its colours, a
        /// second one behind it) and the line breaks: each soldier who can break may (half of them for
        /// a Grandmaster, two thirds for a God), judged as soon as it stands on its feet again.
        /// </summary>
        private static void Rout(Note n, bool staged = false)
        {
            Actor k = n.Killer;
            if (k == null || k.Sid() != n.KillerId || !k.isAlive())
            {
                return;
            }
            Vector2 at = k.current_position;
            if (!staged && BreakableAround(k, n.KillerTier, at, RoutRadius) < 2)
            {
                // nobody here who could break (an army of tiered fighters, the undead, beasts, or
                // everyone already fled): no rout, no pressure ring rolling over nothing (round 3
                // logged "breaks 0 of 0" again and again); its next spree may still break a line
                if (_sprees.TryGetValue(n.KillerId, out Spree sp))
                {
                    sp.LastRout = -999;
                }
                return;
            }
            float px = SitFx.PxOf(k);
            float kk = SitFx.KOf(k);
            Color dark = Tiers.DarkColor(n.KillerTier);
            Color main = Tiers.Color(n.KillerTier);
            Color light = Tiers.LightColor(n.KillerTier);
            bool cine = Godhood.InGodMove(k);   // the God is in the sky mid-cinematic: no ring on the ground under it
            if (!cine && Kit.Visible(at) && k.is_visible)
            {
                // the pressure: a heavy ring rolls out over the line, white-hot at its front, then its
                // colour, then its dark colour as it thins; a second, smaller one follows it
                Fx.Ring(at, px, RingLife, RingR0, 62f * kk, 3, 1, FxLayer.Ground, 6, Art.Ground, RingEase,
                    Fx.WithA(Color.Lerp(Color.white, light, 0.4f), 1f), Fx.WithA(main, 0.9f), Fx.WithA(dark, 0.55f), Fx.WithA(dark, 0f));
                Particle second = Fx.Ring(at, px, 0.5f, 3f, 42f * kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                    Fx.WithA(main, 0.85f), Fx.WithA(dark, 0.5f), Fx.WithA(dark, 0f));
                if (second != null)
                {
                    second.Delay = 0.12f;
                }
                bool god = n.KillerTier >= Tier.God;
                Kit.Shake(at, god ? 0.24f : 0.18f, god ? 0.008f : 0.005f, kk);
            }
            if (!cine && n.KillerTier >= Tier.God)
            {
                // a God's pressure tears through the ground it rolls over (on or off screen)
                Scorch(k, at, px, 62f * kk, main);
            }
            else if (!cine)
            {
                // a Grandmaster's scorches the ground at its feet (on or off screen)
                Kit.Ravage(at, 1.6f * Mathf.Max(1f, kk), 1, k);
            }
            Wave w = new Wave
            {
                K = k,
                KId = n.KillerId,
                Tier = n.KillerTier,
                At = at,
                Chance = staged ? 0.75f : n.KillerTier >= Tier.God ? 0.65f : 0.5f,
                Until = Fighter.Now + 1.6,
                Next = Fighter.Now + 0.15
            };
            _waves.Add(w);
            Sweep(w);
            if (!cine)
            {
                Dev.Watch(at + new Vector2(0f, 1.5f));
            }
        }

        /// <summary>
        /// Can this enemy of a rout's killer break (whether it stands on its feet right now or not)?
        /// Folk-sized, with feelings, and weak next to the killer: untiered soldiers before a
        /// Grandmaster, Novices too; before a God, Adepts as well (an army of Novices is still a
        /// line that breaks).
        /// </summary>
        private static bool Breakable(Actor k, Tier kt, Actor u)
        {
            if (u == k || !Combat.IsFoe(k, u) || Weights.Of(u) > Weight.Folk || !u.hasEmotions() || u.isBaby() || Situations.Possessed(u) || u.isTask("run_away"))
            {
                return false;
            }
            Tier tu = Tiers.Of(u);
            return tu == Tier.None || tu == Tier.Novice && kt >= Tier.Grandmaster || tu == Tier.Adept && kt >= Tier.God;
        }

        /// <summary>How many enemies around the killer could break (on their feet or not).</summary>
        private static int BreakableAround(Actor k, Tier kt, Vector2 at, float radius)
        {
            int n = 0;
            Situations.Around(at, radius, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                if (Breakable(k, kt, _units[i]))
                {
                    n++;
                }
            }
            _units.Clear();
            return n;
        }

        /// <summary>On its feet, free to run (not flying from the blow, not stunned, not in someone's hands)?</summary>
        private static bool Steady(Actor u)
        {
            return Situations.Free(u) && Combat.Grounded(u) && !u.under_forces;
        }

        /// <summary>Judges the soldiers of the line that are on their feet (each once).</summary>
        private static void Sweep(Wave w)
        {
            Actor k = w.K;
            Situations.Around(w.At, RoutRadius, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (!Breakable(k, w.Tier, u))
                {
                    continue;
                }
                long id = u.Sid();
                if (w.Judged.Contains(id) || !Steady(u))
                {
                    continue;   // judged already, or still flying from the blow: when it lands
                }
                w.Judged.Add(id);
                if (Random.value > w.Chance || !Flee(u, w.At))
                {
                    continue;
                }
                if (++w.Broke == 1)
                {
                    Anim.Sound("event:/SFX/POWERS/Curse", u.current_tile);
                    Situations.Tip(Locale.Format("sakuga_sit_rout", Situations.Name(k)), Situations.Hex(Tiers.Color(w.Tier)), w.At);
                }
            }
            _units.Clear();
        }

        /// <summary>
        /// The game's run_away task (fast, deaf to the fight), aimed straight away from the threat
        /// instead of at a random far tile; it is called off after 3 s if it is still running.
        /// </summary>
        private static bool Flee(Actor u, Vector2 from)
        {
            WorldTile here = u.current_tile;
            if (here == null || u.ai == null)
            {
                return false;
            }
            Vector2 away = u.current_position - from;
            away = away.sqrMagnitude > 0.0004f ? away.normalized : Random.insideUnitCircle.normalized;
            // straight away from it, or a little to either side: never into the fire (a battle
            // ground burns now, and burning ground counts as a safe landing for those who don't burn)
            Vector2 spot = default;
            bool found = false;
            float dist = Random.Range(5f, 7f);
            for (int i = 0; i < 3 && !found; i++)
            {
                float ang = i == 0 ? 0f : i == 1 ? 35f : -35f;
                Vector2 dir = Quaternion.Euler(0f, 0f, ang) * away;
                Vector2 want = Combat.Clamp(u.current_position + dir * dist);
                found = Combat.FindLanding(want, 3f, out spot) && !Situations.Burning(spot);
            }
            if (!found)
            {
                return false;
            }
            WorldTile t = Combat.TileAt(spot);
            if (t == null || t.region == null || here.region == null || t.region.island != here.region.island)
            {
                return false;
            }
            if (Tiers.Of(u) != Tier.None)
            {
                u.clearAttackTarget();   // a tiered one that breaks runs: its Brain looks for no opening meanwhile
            }
            u.setTask("run_away", true, false, true);
            BehaviourTaskActor task = u.ai.task;
            if (task != null && task.id == "run_away" && task.list.Count > 1 && task.list[1] is BehGoToTileTarget)
            {
                u.ai.action_index = 1;   // skip "find a random far tile": run from it
                u.beh_tile_target = t;
            }
            SitFx.Mark(u, SitFx.Bang, SitFx.Grey, 0.9f, 3f);
            _flights.Add(new Flight { U = u, Id = u.Sid(), Until = Fighter.Now + 3.0 });
            return true;
        }

        /// <summary>
        /// A God's rout: a swath of scorched ground from its feet through the thick of the line it
        /// breaks (or the way it faces, if they stand all around it), as far as its ring rolls.
        /// </summary>
        private static void Scorch(Actor k, Vector2 at, float px, float r1, Color heat)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            Situations.Around(at, RoutRadius, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u == k || u.isBaby() || !Combat.IsFoe(k, u))
                {
                    continue;
                }
                Vector2 d = u.current_position - at;
                float m = d.magnitude;
                if (m > 0.3f)
                {
                    sum += d / m;
                    n++;
                }
            }
            _units.Clear();
            Vector2 dir;
            if (n > 0 && sum.sqrMagnitude > 0.04f * n * n)
            {
                dir = sum.normalized;
            }
            else
            {
                bool right = Moves.TryGet(k, out Move mv) ? mv.Pose.FaceRight : k.flip;
                dir = new Vector2(right ? 1f : -1f, 0f);
            }
            // the ring is an ellipse (flatter in y): along 'dir' its front is this much nearer
            float stretch = Mathf.Sqrt(dir.x * dir.x + dir.y / Art.Ground * (dir.y / Art.Ground));
            if (_swaths.Count >= 6)
            {
                _swaths.RemoveAt(0);
            }
            _swaths.Add(new Swath
            {
                By = k,
                ById = k.Sid(),
                From = at,
                Dir = dir,
                Heat = heat,
                Px = px,
                R0 = RingR0,
                R1 = r1,
                Stretch = stretch,
                Len = Mathf.Min(r1 * px / stretch, 9f),
                Done = 0.6f,
                Age = 0f
            });
        }

        /// <summary>Every frame: each swath burns on as far as its ring has rolled (world damage: on or off screen).</summary>
        public static void Swaths(float dt)
        {
            for (int i = _swaths.Count - 1; i >= 0; i--)
            {
                if (i >= _swaths.Count)
                {
                    continue;
                }
                Swath s = _swaths[i];
                if (s.By == null || s.By.Sid() != s.ById || !s.By.isAlive())
                {
                    _swaths.RemoveAt(i);   // its God is gone: what burned stays burned
                    continue;
                }
                s.Age += Mathf.Max(0f, dt);
                float k = Mathf.Clamp01(s.Age / RingLife);
                float e = 1f - Mathf.Pow(1f - k, RingEase);
                float front = Mathf.Lerp(s.R0, s.R1, e) * s.Px / s.Stretch;
                // 'Done' is the next distance to burn
                while (s.Done <= s.Len && s.Done <= front + 0.01f)
                {
                    if (!Burn(s, s.Done))
                    {
                        s.Done = s.Len + 1f;   // the map ends here
                        break;
                    }
                    s.Done += SwathStep;
                }
                if (s.Done > s.Len || s.Age > 3f)
                {
                    _swaths.RemoveAt(i);
                }
            }
        }

        /// <summary>One bite of the swath, 'd' tiles out: the ground scorched (wider as it goes), embers where it is seen.</summary>
        private static bool Burn(Swath s, float d)
        {
            Vector2 at = s.From + s.Dir * d;
            if (at.x < 0f || at.y < 0f || at.x >= MapBox.width || at.y >= MapBox.height)
            {
                return false;
            }
            float t = Mathf.Clamp01(d / Mathf.Max(1f, s.Len));
            // blasted open near the God (the grass torn off), scorched as the pressure thins
            Kit.Ravage(at, Mathf.Lerp(1.2f, 2.2f, t), t < 0.45f ? 2 : 1, s.By, false, s.From, 0.9f);   // its own footing kept
            if (Kit.Visible(at))
            {
                int n = Fx.Count(3);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = at + new Vector2(Random.Range(-6f, 6f), Random.Range(-3f, 3f)) * s.Px;
                    Kit.Ember(p, s.Px, Random.Range(0.45f, 0.8f), new Vector2(Random.Range(-5f, 5f), Random.Range(16f, 30f)) * s.Px, s.Heat);
                }
            }
            return true;
        }

        // -------------------------------------------------------------- underdog --

        private static void Underdog(Note n, bool rewards)
        {
            Actor k = n.Killer;
            bool here = k != null && k.Sid() == n.KillerId && k.isAlive();
            string victimTier = Tiers.Name(n.VictimTier);
            string name = here ? Situations.Name(k) : "?";
            Situations.Tip(Locale.Format("sakuga_sit_underdog", name, victimTier, n.VictimName ?? "?"), Situations.Hex(SitFx.GoldPale), n.At, true);
            if (!here)
            {
                return;
            }
            Vector2 feet = SitFx.Feet(k);
            float px = SitFx.PxOf(k);
            float kk = SitFx.KOf(k);
            if (Kit.Visible(feet) && k.is_visible)
            {
                SitFx.SlowBeat(feet, 0.3f, 0.7f);
                SitFx.Pillar(feet, px, 42f * kk, 0.7f, Color.white, SitFx.GoldPale, 2);
                Fx.Ring(feet, px, 0.55f, 3f, 26f * kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f,
                    Fx.WithA(Color.white, 1f), Fx.WithA(SitFx.GoldPale, 0.85f), Fx.WithA(SitFx.Gold, 0f));
                SitFx.Glitter(feet, px, 14, 34f * kk, SitFx.GoldPale);
                SitFx.HeadGlint(k, Color.white, 0.4f, 1.4f, new Vector2(0f, 3f));
                new SitAura(k, Fx.WithA(SitFx.GoldPale, 0.9f), Fx.WithA(Color.white, 0.75f), true, true).End(0.8f);
            }
            Anim.Sound("event:/SFX/DROPS/DropBlessing", k.current_tile);
            Dev.Watch(feet + new Vector2(0f, 1.5f));
            if (rewards)
            {
                Progression.AddSxp(k, 60f * (int)n.VictimTier);
                Progression.Trial(k, 3f, false);
            }
            if (Dev.On)
            {
                Dev.Trace("situation underdog: " + k.asset.id + "#" + n.KillerId + " (" + n.KillerTier + ") felled a " + n.VictimTier);
            }
        }

        // ------------------------------------------------------------- vengeance --

        private static void Vengeance(Note n, double now)
        {
            Actor k = n.Killer;
            if (k == null || k.Sid() != n.KillerId || !k.isAlive() || k.kingdom == n.VictimSide)
            {
                return;
            }
            Fighter best = null;
            float bestScore = float.MinValue;
            List<Fighter> tiered = Situations.Tiered;
            for (int i = 0; i < tiered.Count; i++)
            {
                Fighter f = tiered[i];
                Actor a = f.A;
                if (!f.Valid || f.Tier < Tier.Adept || a.kingdom != n.VictimSide || f.Id == n.VictimId)
                {
                    continue;
                }
                float d2 = (a.current_position - n.At).sqrMagnitude;
                if (d2 > 64f || (a.current_position - k.current_position).sqrMagnitude > 196f)
                {
                    continue;
                }
                if (_oathReady.TryGetValue(f.Id, out double at) && now < at)
                {
                    continue;
                }
                if (Situations.Engaged(a) || !Situations.Free(a) || SitLastStand.In(a) || !Combat.IsFoe(a, k) || !Combat.Allowed(a, k))
                {
                    continue;
                }
                Actor rival = Duels.Opponent(a);
                if (rival != null && rival != k)
                {
                    continue;   // its own duel comes first
                }
                // one already at grips with a weaker tiered enemy may drop it for the killer (in a
                // war of tiered fighters everyone is at grips: round 3 never swore an oath), never
                // one facing its match (a God's war with a God, a Master's with a Master); the free
                // ones swear first
                Actor foe = Situations.TieredFoe(a);
                bool turning = foe != null && foe != k;
                if (turning && Tiers.Of(foe) >= f.Tier)
                {
                    continue;
                }
                float score = (int)f.Tier * 100f - d2 - (turning ? 150f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = f;
                }
            }
            if (best == null || Random.value > (Situations.OnTieredFoe(best.A, k) ? 0.4f : 0.65f))
            {
                return;
            }
            Swear(best, k, n.VictimName, now);
        }

        /// <summary>The oath: it turns on the killer (false if it can't be steered onto it).</summary>
        private static bool Swear(Fighter best, Actor k, string fallen, double now)
        {
            Actor av = best.A;
            if (!Situations.Steer(av, k, false))
            {
                return false;
            }
            Oath o = new Oath { F = best, A = av, Id = best.Id, Killer = k, KillerId = k.Sid(), Until = now + 15.0, NextSteer = now + 0.5 };
            _oaths.Add(o);
            _oathReady[best.Id] = now + 30.0;
            Situations.Claim(av, o);
            Situations.SetHate(av, k);
            Situations.Boost(best, Situations.HateMult - 1f, 15.0);
            Situations.Resume(av);   // it goes for the killer now (again when its oath pose ends)
            if (!(Situations.CanAct(av) && SitFuryMove.Begin(av, k)))
            {
                // too busy for the pose: the mark and the flash still tell it
                SitFx.Mark(av, SitFx.Vein, SitFx.Blood, 1.2f, 1f, 2);
                new SitAura(av, Fx.WithA(SitFx.Blood, 0.8f), Fx.WithA(SitFx.Blood, 0.45f), true, true).End(0.5f);
            }
            if (best.Tier >= Tier.Master)
            {
                string line = fallen != null
                    ? Locale.Format("sakuga_sit_vengeance", Situations.Name(av), fallen)
                    : Locale.Format("sakuga_sit_vengeance_on", Situations.Name(av), Situations.Name(k));
                Situations.Tip(line, Situations.Hex(SitFx.Blood), av.current_position);
            }
            Dev.Watch(av.current_position + new Vector2(0f, 1.5f));
            if (Dev.On)
            {
                Dev.Trace("situation vengeance: " + av.asset.id + "#" + best.Id + " (" + best.Tier + ") on " + k.asset.id + "#" + k.Sid());
            }
            return true;
        }

        /// <summary>A kill that ends an oath: the avenger's own (done) or anyone else's.</summary>
        private static void Oaths(Note n, double now)
        {
            for (int i = _oaths.Count - 1; i >= 0; i--)
            {
                Oath o = _oaths[i];
                if (o.KillerId == n.VictimId)
                {
                    EndOath(o, n.KillerId == o.Id);
                }
                else if (o.Id == n.VictimId)
                {
                    EndOath(o, false);
                }
            }
        }

        private static void EndOath(Oath o, bool done)
        {
            _oaths.Remove(o);
            Situations.Unclaim(o.Id, o);
            Situations.ClearHate(o.Id);
            if (!done || o.A == null || o.A.Sid() != o.Id || !o.A.isAlive())
            {
                return;
            }
            // done: the anger leaves it
            Actor a = o.A;
            Vector2 feet = SitFx.Feet(a);
            float px = SitFx.PxOf(a);
            if (Kit.Visible(feet) && a.is_visible)
            {
                SitFx.HeadGlint(a, Color.white, 0.26f, 1f, new Vector2(a.flip ? 3f : -3f, 1f));
                Fx.LandingPuff(feet, px, a.current_tile);
            }
            Progression.AddSxp(a, 20f);
            Progression.Trial(a, 1.5f, false);
        }

        // ---------------------------------------------------------------- lesson --

        private static void Teach(Lesson l, double now)
        {
            Actor m = l.Mentor;
            if (m == null || m.Sid() != l.MentorId || !m.isAlive() || m.kingdom == null)
            {
                return;
            }
            if (_mentorReady.TryGetValue(l.MentorId, out double ready) && now < ready)
            {
                return;
            }
            Tier mt = Tiers.Of(m);
            _pick.Clear();
            List<Fighter> tiered = Situations.Tiered;
            Vector2 mp = m.current_position;
            for (int i = 0; i < tiered.Count; i++)
            {
                Fighter f = tiered[i];
                if (!f.Valid || f.Tier > Tier.Adept || f.A == m || f.A.kingdom != m.kingdom || !f.InCombat)
                {
                    continue;
                }
                if ((f.A.current_position - mp).sqrMagnitude > 36f || !Situations.CanAct(f.A))
                {
                    continue;
                }
                if (_studentReady.TryGetValue(f.Id, out double at) && now < at)
                {
                    continue;
                }
                _pick.Add(f);
            }
            if (_pick.Count == 0)
            {
                return;
            }
            Dev.Watch(mp + new Vector2(0f, 1.5f));
            _pick.Sort((x, y) => (x.A.current_position - mp).sqrMagnitude.CompareTo((y.A.current_position - mp).sqrMagnitude));
            _mentorReady[l.MentorId] = now + 6.0;
            SitFx.HeadGlint(m, SitFx.GoldPale, 0.22f, 0.9f, new Vector2(0f, 2f));
            for (int i = 0; i < _pick.Count && i < 2; i++)
            {
                Learn(_pick[i], l.Tech, mt, now);
            }
            _pick.Clear();
        }

        private static void Learn(Fighter f, Technique tech, Tier mentorTier, double now)
        {
            Actor a = f.A;
            _studentReady[f.Id] = now + 25.0;
            SitFx.Mark(a, SitFx.Chevron, SitFx.Gold, 0.8f, 5f);
            SitFx.HeadGlint(a, SitFx.GoldPale, 0.2f, 0.8f, new Vector2(a.flip ? 3f : -3f, 4f));
            Progression.AddSxp(a, 5f + 3f * Mathf.Max(0, (int)mentorTier - (int)f.Tier));
            bool tried = false;
            int level = Techniques.Level(a, tech, f.Tier);
            // (never past what its Brain would hold: its duel's pacing, a staging, the move budget)
            if (level > 0 && f.Ready(tech) && Mana.Has(a, tech, level, false) && Moves.CanPerform(a) && !Situations.TechHeld(a))
            {
                _ctx ??= new Ctx();
                _ctx.Build(f);
                if (_ctx.Enemies.Count > 0)
                {
                    Plan p = null;
                    try
                    {
                        p = tech.Plan(_ctx, level);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[Sakuga] lesson plan " + tech.Id + ": " + e);
                    }
                    if (p != null && p.Score > 0f)
                    {
                        p.Tech = tech;
                        p.Level = level;
                        p.Linked = false;
                        tried = Brain.Execute(f, p);
                    }
                }
            }
            if (!tried)
            {
                // inspired: it looks for its opening right now
                f.ThinkIn = 0f;
                f.LastEnd = Math.Min(f.LastEnd, now - 30.0);
                Situations.Boost(f, 0.15f, 5.0);
            }
            if (Dev.On)
            {
                Dev.Trace("situation lesson: " + a.asset.id + "#" + f.Id + " (" + f.Tier + ") watched " + tech.Id + (tried ? " and tried it" : ""));
            }
        }

        // --------------------------------------------------------------- staging --

        /// <summary>
        /// Staging (Situations.Force): the strongest of this unit's side breaks the enemy line around it
        /// as if it had just killed three, once three soldiers who could break stand within 6 tiles of
        /// it (at the end of the wait: once any does).
        /// </summary>
        internal static Situations.Stage StageRout(Actor a, bool last, out string msg)
        {
            Actor k = Situations.Pick(a, Tier.Grandmaster);
            if (k == null)
            {
                msg = "rout: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(k);
            int n = 0;
            Situations.Around(k.current_position, 6f, _units);
            Tier kt = Tiers.Of(k);
            for (int i = 0; i < _units.Count; i++)
            {
                if (Breakable(k, kt, _units[i]) && Steady(_units[i]))
                {
                    n++;
                }
            }
            _units.Clear();
            if (n < 3 && !(last && n > 0))
            {
                msg = "rout: " + n + " soldiers who could break within 6 tiles of " + k.asset.id + "#" + k.Sid() + ", waiting for 3";
                return Situations.Stage.Wait;
            }
            if (n == 0)
            {
                msg = "rout: nobody near " + k.asset.id + "#" + k.Sid() + " could break";
                return Situations.Stage.Fail;
            }
            Situations.Show(k.current_position + new Vector2(0f, 1.5f));
            Tier t = Tiers.Of(k);
            Rout(new Note { Killer = k, KillerId = k.Sid(), KillerTier = t, When = Fighter.Now }, true);
            msg = "rout: " + k.asset.id + "#" + k.Sid() + " (" + t + ") with " + n + " who could break within 6 tiles";
            return Situations.Stage.Done;
        }

        /// <summary>
        /// Staging: a real underdog kill. The strongest Master+ enemy of this unit's side within 24
        /// tiles falls to the next nobody of this side (untiered or a Novice, person-sized) that
        /// stands at its side: a killing blow, through the game's own kill bookkeeping, so everything
        /// that follows a kill (the moment, the rewards, a champion's fall, vengeance) happens. At the
        /// end of the wait, if no nobody reached it, only the moment plays on the nobody nearest to it.
        /// </summary>
        internal static Situations.Stage StageUnderdog(Actor a, bool last, out string msg)
        {
            Actor victim = null;
            float rank = -1f;
            Situations.Around(a.current_position, 24f, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                Tier t = Tiers.Of(u);
                if (t < Tier.Master || !Combat.IsFoe(a, u) || u.kingdom == null || u.kingdom == a.kingdom)
                {
                    continue;
                }
                float r = (int)t * 10f + Combat.HpPct(u);
                if (r > rank)
                {
                    rank = r;
                    victim = u;
                }
            }
            _units.Clear();
            if (victim == null)
            {
                if (!last)
                {
                    msg = "underdog: no Master or better among the enemies within 24 tiles yet";
                    return Situations.Stage.Wait;
                }
                if (Tiers.Of(a) > Tier.Novice)
                {
                    msg = "underdog: no Master to fell, and " + a.asset.id + " is no nobody";
                    return Situations.Stage.Fail;
                }
                Situations.Show(a.current_position + new Vector2(0f, 1.5f));
                Underdog(new Note { Killer = a, KillerId = a.Sid(), KillerTier = Tier.None, VictimTier = Tier.Master, VictimName = "?", At = a.current_position, When = Fighter.Now }, false);
                msg = "underdog: no Master to fell; the moment alone on " + a.asset.id + "#" + a.Sid();
                return Situations.Stage.Done;
            }
            Situations.Want(victim);   // its techniques wait: it must not cut the crowd down before one of them reaches it
            // the nobody at its side
            Actor hero = null;
            float best = float.MaxValue;
            Vector2 vp = victim.current_position;
            float reach = 1.7f * Mathf.Max(1f, SitFx.KOf(victim));
            Situations.Around(vp, last ? 12f : reach, _units);
            for (int i = 0; i < _units.Count; i++)
            {
                Actor u = _units[i];
                if (u == victim || u.kingdom != a.kingdom || !Combat.IsFoe(u, victim) || u.isBaby() || Tiers.Of(u) > Tier.Novice
                    || Weights.Species(u) > Weight.Folk || Moves.TryGet(u, out Move um) && um is Puppet)
                {
                    continue;
                }
                float d2 = (u.current_position - vp).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    hero = u;
                }
            }
            _units.Clear();
            string who = victim.asset.id + "#" + victim.Sid() + " (" + Tiers.Of(victim) + ")";
            if (hero == null)
            {
                msg = "underdog: waiting for a nobody to reach " + who;
                return Situations.Stage.Wait;
            }
            if (!last && Moves.Busy(victim))
            {
                msg = "underdog: " + who + " is mid-technique";
                return Situations.Stage.Wait;
            }
            if (best > reach * reach)
            {
                // the wait is over and nobody reached it: the moment alone, on the nobody nearest to it
                Situations.Show(hero.current_position + new Vector2(0f, 1.5f));
                Underdog(new Note { Killer = hero, KillerId = hero.Sid(), KillerTier = Tier.None, VictimTier = Tiers.Of(victim), VictimName = Situations.Name(victim), At = hero.current_position, When = Fighter.Now }, false);
                msg = "underdog: nobody reached " + who + "; the moment alone on " + hero.asset.id + "#" + hero.Sid();
                return Situations.Stage.Done;
            }
            Situations.Show(vp + new Vector2(0f, 1.5f));
            int hp = victim.getHealth();
            Combat.Hit(hero, victim, 1f, true, true, hp * 40f + 400f);
            if (victim.isAlive())
            {
                msg = "underdog: the killing blow of " + hero.asset.id + "#" + hero.Sid() + " on " + who + " was turned aside";
                return Situations.Stage.Fail;
            }
            msg = "underdog: " + hero.asset.id + "#" + hero.Sid() + " (" + Tiers.Of(hero) + ") lands the killing blow on " + who;
            return Situations.Stage.Done;
        }

        /// <summary>
        /// Staging: the strongest of this unit's side (an Adept or better) swears vengeance on the
        /// nearest enemy in the fight within 10 tiles of it (14 at the end of the wait).
        /// </summary>
        internal static Situations.Stage StageVengeance(Actor a, bool last, out string msg)
        {
            Actor av = Situations.Pick(a, Tier.Adept);
            if (av == null)
            {
                msg = "vengeance: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(av);
            string who = av.asset.id + "#" + av.Sid();
            if (Situations.Engaged(av) || !Situations.Free(av))
            {
                msg = "vengeance: " + who + " is busy";
                return Situations.Stage.Wait;
            }
            Actor k = Situations.Nearest(av, last ? 14f : 10f, true, u => Situations.Threat(u) && Combat.Allowed(av, u) && (last || u.isFighting()));
            if (k == null)
            {
                msg = "vengeance: no enemy in the fight within 10 tiles of " + who;
                return Situations.Stage.Wait;
            }
            _oathReady.Remove(av.Sid());
            Situations.Show(Vector2.Lerp(av.current_position, k.current_position, 0.35f) + new Vector2(0f, 1.5f));
            if (!Swear(Fighters.Get(av), k, null, Fighter.Now))
            {
                msg = "vengeance: " + who + " can't be steered onto " + k.asset.id + "#" + k.Sid();
                return Situations.Stage.Wait;
            }
            msg = "vengeance: " + who + " (" + Tiers.Of(av) + ") on " + k.asset.id + "#" + k.Sid();
            return Situations.Stage.Done;
        }

        /// <summary>
        /// Staging: a real lesson. Once a Novice or Adept of its side is in the fight within 6 tiles of
        /// the strongest of this unit's side (a Master if it has one) and an enemy is within 8 tiles,
        /// the master performs a technique (one a student knows first, so the student can try it) and
        /// the lesson is taught when it lands. If it can't perform one, its last technique is the lesson.
        /// </summary>
        internal static Situations.Stage StageLesson(Actor a, bool last, out string msg)
        {
            Actor m = Situations.Pick(a, Tier.Master);
            if (m == null || m.kingdom == null)
            {
                msg = "lesson: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(m);
            string who = m.asset.id + "#" + m.Sid();
            Fighter f = Fighters.Get(m);
            _pick.Clear();
            List<Fighter> tiered = Situations.Tiered;
            for (int i = 0; i < tiered.Count; i++)
            {
                Fighter s = tiered[i];
                if (s.Valid && s.Tier <= Tier.Adept && s.A != m && s.A.kingdom == m.kingdom && (s.InCombat || s.A.isFighting())
                    && (s.A.current_position - m.current_position).sqrMagnitude <= 36f)
                {
                    _pick.Add(s);
                }
            }
            if (_pick.Count == 0)
            {
                msg = "lesson: no Novice or Adept of " + who + "'s side in the fight within 6 tiles of it";
                return last ? Situations.Stage.Fail : Situations.Stage.Wait;
            }
            if (!Situations.CanAct(m))
            {
                _pick.Clear();
                msg = "lesson: " + who + " is busy";
                return Situations.Stage.Wait;
            }
            Actor target = Situations.Nearest(m, 8f, true, u => Situations.Threat(u) && Combat.Allowed(m, u));
            if (target == null && !last)
            {
                _pick.Clear();
                msg = "lesson: no enemy within 8 tiles of " + who + " to show it on";
                return Situations.Stage.Wait;
            }
            foreach (Fighter s in _pick)
            {
                _studentReady.Remove(s.Id);
            }
            _mentorReady.Remove(m.Sid());
            Situations.Show(m.current_position + new Vector2(0f, 1.5f));
            Technique shown = null;
            IReadOnlyList<Technique> all = Techniques.All;
            int from = all.Count > 0 ? Random.Range(0, all.Count) : 0;   // not always the same one
            for (int pass = 0; pass < 2 && shown == null && target != null; pass++)
            {
                for (int j = 0; j < all.Count; j++)
                {
                    Technique t = all[(from + j) % all.Count];
                    if (t.GodAbility || t.Reactive || t.Team)
                    {
                        continue;
                    }
                    int lv = Techniques.Level(m, t, f.Tier);
                    if (lv <= 0 || pass == 0 && !AnyKnows(_pick, t))
                    {
                        continue;
                    }
                    if (Brain.Force(m, t, lv, target))
                    {
                        shown = t;
                        break;
                    }
                }
            }
            _pick.Clear();
            if (shown != null)
            {
                _staged = new Lesson { Mentor = m, MentorId = m.Sid(), Tech = shown, When = Fighter.Now };
                msg = "lesson: " + who + " (" + f.Tier + ") shows " + shown.Id + " on " + target.asset.id + "#" + target.Sid() + " (taught when it ends)";
                return Situations.Stage.Done;
            }
            Technique tech = f.LastTech != null && Techniques.Level(m, f.LastTech, f.Tier) > 0 ? f.LastTech : null;
            if (tech == null)
            {
                foreach (Technique t in Techniques.All)
                {
                    if (!t.GodAbility && !t.Reactive && !t.Team && Techniques.Level(m, t, f.Tier) > 0)
                    {
                        tech = t;
                    }
                }
            }
            if (tech == null)
            {
                msg = "lesson: " + who + " knows no technique";
                return Situations.Stage.Fail;
            }
            Teach(new Lesson { Mentor = m, MentorId = m.Sid(), Tech = tech, When = Fighter.Now }, Fighter.Now);
            msg = "lesson: " + who + " (" + f.Tier + ") couldn't show anything now; it taught " + tech.Id;
            return Situations.Stage.Done;
        }

        private static bool AnyKnows(List<Fighter> students, Technique t)
        {
            for (int i = 0; i < students.Count; i++)
            {
                if (Techniques.Level(students[i].A, t, students[i].Tier) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A move ended (Situations.OnMoveEnded): a staged lesson's demonstration is taught now.</summary>
        internal static void MentorMoveEnded(Move m)
        {
            Lesson l = _staged;
            if (l.Mentor == null || m.Tech != l.Tech || m.A == null || m.A.Sid() != l.MentorId)
            {
                return;
            }
            _staged = default;
            if (_mentorReady.TryGetValue(l.MentorId, out double ready) && Fighter.Now < ready)
            {
                return;   // its technique killed: the lesson was taught as it happened
            }
            l.When = Fighter.Now;
            _lessons.Add(l);   // answered on the next frame, outside the move's end
        }

        // ---------------------------------------------------------------- update --

        public static void Update(double now)
        {
            if (_staged.Mentor != null && now - _staged.When > 15.0)
            {
                _staged = default;   // its demonstration never ended as expected
            }
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                if (i >= _waves.Count)
                {
                    continue;
                }
                Wave w = _waves[i];
                bool killer = w.K != null && w.K.Sid() == w.KId && w.K.isAlive();
                if (killer && now < w.Until)
                {
                    if (now >= w.Next)
                    {
                        w.Next = now + 0.15;
                        Sweep(w);
                    }
                    continue;
                }
                _waves.RemoveAt(i);
                if (Dev.On)
                {
                    Dev.Trace("situation rout: " + (w.K != null ? w.K.asset.id : "?") + "#" + w.KId + " breaks " + w.Broke + " of " + w.Judged.Count);
                }
            }
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                Flight fl = _flights[i];
                if (now < fl.Until)
                {
                    continue;
                }
                _flights.RemoveAt(i);
                Actor u = fl.U;
                if (u != null && u.Sid() == fl.Id && u.isAlive() && u.isTask("run_away") && !Moves.Busy(u))
                {
                    u.cancelAllBeh();   // three seconds of panic: back to it
                }
            }
            for (int i = _oaths.Count - 1; i >= 0; i--)
            {
                if (i >= _oaths.Count)
                {
                    continue;
                }
                Oath o = _oaths[i];
                bool avenger = o.A != null && o.A.Sid() == o.Id && o.A.isAlive();
                bool killer = o.Killer != null && o.Killer.Sid() == o.KillerId && o.Killer.isAlive();
                if (!avenger || !killer || now > o.Until || (o.A.current_position - o.Killer.current_position).sqrMagnitude > 900f)
                {
                    EndOath(o, false);
                    continue;
                }
                if (now >= o.NextSteer)
                {
                    o.NextSteer = now + 0.5;
                    Situations.Steer(o.A, o.Killer, false);
                }
            }
        }

        public static void Prune(double now)
        {
            Drop(_oathReady, now);
            Drop(_studentReady, now);
            Drop(_mentorReady, now);
            List<long> old = null;
            foreach (KeyValuePair<long, Spree> kv in _sprees)
            {
                if (now - kv.Value.T2 > 10.0 && now - kv.Value.LastRout > 10.0)
                {
                    (old ??= new List<long>()).Add(kv.Key);
                }
            }
            if (old != null)
            {
                foreach (long id in old)
                {
                    _sprees.Remove(id);
                }
            }
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
            _notes.Clear();
            _lessons.Clear();
            _sprees.Clear();
            _flights.Clear();
            _waves.Clear();
            _swaths.Clear();
            _staged = default;
            _oaths.Clear();
            _oathReady.Clear();
            _studentReady.Clear();
            _mentorReady.Clear();
            _units.Clear();
            _pick.Clear();
        }
    }

    /// <summary>
    /// A comrade falls beside it: the avenger's oath.
    ///
    /// Beat sheet (game seconds):
    ///
    ///   JOLT    0.08  the news hits it: a jolt, a 1-pixel tremble; it whips round to face the killer
    ///   CLENCH  0.18  it hunches (squash), fists clenched: its silhouette pulses blood red, the anger
    ///                 vein pops over its head (and throbs), a red glint flashes where its eyes are
    ///   BURST   0.12  it springs up (stretch): a dark red ring snaps out from its feet, red embers fly
    ///
    /// Then it charges (its attack target is already the killer) and its Brain looks at once. A
    /// Master or better swears it in about a quarter of a second (Situations.Pace; the clench never
    /// under 0.1 s) and goes straight for the killer.
    /// </summary>
    internal sealed class SitFuryMove : Move
    {
        private readonly float TJolt;
        private readonly float TClench;
        private readonly float TBurst;

        private readonly Vector2 _at;
        private readonly float _k;
        private int _phase;
        private float _pt;
        private bool _entered;
        private Particle _glow;
        private Sprite[] _glowFrames;
        private Color _glowColor = SitFx.Clear;

        private SitFuryMove(Actor a, Actor killer) : base(a)
        {
            _at = a.current_position;
            _k = K;
            float pace = Situations.Pace(Tiers.Of(a));
            TJolt = 0.08f * pace;
            TClench = Situations.Beat(0.18f, pace, 0.1f);
            TBurst = 0.12f * pace;
            float dx = killer.current_position.x - _at.x;
            if (Mathf.Abs(dx) > 0.1f)
            {
                Pose.FaceRight = dx > 0f;
            }
        }

        public static bool Begin(Actor a, Actor killer)
        {
            return Moves.CanPerform(a) && Moves.Start(new SitFuryMove(a, killer));
        }

        private bool Visible => Fx.OnScreen(_at, 0.3f);

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        private void Next()
        {
            _phase++;
            _pt = 0f;
            _entered = false;
        }

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
                        if (Enter())
                        {
                            Anim.Sound("event:/SFX/DROPS/DropCurse", A.current_tile);
                            StartGlow();
                        }
                        Pose.Nudge = new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                        Pose.Rot = -4f;
                        _glowColor = Fx.WithA(SitFx.Blood, 0.5f);
                        if (_pt >= TJolt)
                        {
                            Next();
                        }
                        break;
                    }
                case 1:
                    {
                        if (Enter())
                        {
                            Pose.Nudge = Vector2.zero;
                            SitFx.Mark(A, SitFx.Vein, SitFx.Blood, 1.3f, 1f, 2);
                            if (Visible)
                            {
                                // the eyes flash (riding on the unit: a God's oath is over before it fades)
                                Vector2 eye = new Vector2((Pose.FaceRight ? 1.5f : -1.5f) * px, Pose.PivotUp * 1.45f * px);
                                SitFx.Ride(Fx.Glint(BodyPos() + eye, px, 0.18f, SitFx.Blood, 0.8f), this, eye);
                            }
                        }
                        float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.08f));
                        Pose.SX = Mathf.Lerp(1f, 1.12f, k);
                        Pose.SY = Mathf.Lerp(1f, 0.86f, k);
                        Pose.Rot = Mathf.Lerp(-4f, 8f, k);
                        float pulse = 0.5f + 0.5f * Mathf.Sin(_pt * 50f);
                        _glowColor = Fx.WithA(Color.Lerp(SitFx.BloodDark, SitFx.Blood, pulse), 0.45f + 0.25f * pulse);
                        if (_pt >= TClench)
                        {
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        if (Enter() && Visible)
                        {
                            Fx.Ring(_at, px, 0.3f, 3f, 16f * _k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                                Fx.WithA(SitFx.Blood, 0.95f), Fx.WithA(SitFx.BloodDark, 0.7f), Fx.WithA(SitFx.BloodDark, 0f));
                            for (int i = 0; i < Fx.Count(6); i++)
                            {
                                Kit.Ember(_at + new Vector2(Random.Range(-4f, 4f), Random.Range(0f, 4f)) * px, px, Random.Range(0.4f, 0.7f),
                                    new Vector2(Random.Range(-12f, 12f), Random.Range(16f, 30f)) * px, SitFx.Blood);
                            }
                        }
                        float k = Mathf.Clamp01(_pt / TBurst);
                        float s = Anim.OutQuad(Mathf.Clamp01(k / 0.4f));
                        float settle = Anim.Smooth(0.4f, 1f, k);
                        Pose.SX = Mathf.Lerp(Mathf.Lerp(1.12f, 0.9f, s), 1f, settle);
                        Pose.SY = Mathf.Lerp(Mathf.Lerp(0.86f, 1.14f, s), 1f, settle);
                        Pose.Rot = Mathf.Lerp(8f, 0f, k);
                        _glowColor = Fx.WithA(SitFx.Blood, Mathf.Lerp(0.6f, 0f, k));
                        if (_pt >= TBurst)
                        {
                            Pose.SX = Pose.SY = 1f;
                            Pose.Rot = 0f;
                            KillGlow();
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        private void StartGlow()
        {
            if (_glow != null || !Fx.HasSilhouette || !Visible)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            _glow = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 3, true) : null;
            if (_glow != null)
            {
                _glowFrames = _glow.Frames;
                _glow.Held = true;
                _glow.UseEuler = true;
                Fx.Glue(_glow, this, SitFx.Clear);
            }
        }

        public override void Frame()
        {
            if (SitFx.Mine(_glow, _glowFrames))
            {
                Fx.Glue(_glow, this, _glowColor);
            }
        }

        private void KillGlow()
        {
            if (SitFx.Mine(_glow, _glowFrames))
            {
                _glow.Kill();
            }
            _glow = null;
        }

        public override void Abort()
        {
            KillGlow();
        }

        public override void OnEnd(bool aborted)
        {
            KillGlow();
            if (!aborted)
            {
                Situations.Resume(A);   // and at the killer
            }
        }
    }
}
