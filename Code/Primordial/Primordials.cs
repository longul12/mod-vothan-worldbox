using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PRIMORDIAL: the power above the Gods. A Primordial is the first of all things: a planetary
    /// threat. It never rises on its own (no natural growth reaches it, it is never born or inherited):
    /// only a player makes one, by giving the trait.
    ///
    /// For every other system a Primordial is a God (Tiers.Of answers God: it keeps the God's gauge,
    /// abilities, techniques and duels against other Gods are beneath it), and above that:
    ///   * its blows land three times as hard, and the blows of anything less than a Primordial land at
    ///     a third (its hide: a God alone barely scratches it) - only the Gods' joined strike goes through
    ///   * it has powers no one else can use (PrimordialPowers: Erasure, the Annihilation Lance, the
    ///     Genesis Wave, Moonfall, the Judgement of the First), which kill Gods but leave the world standing
    ///   * two Primordials who are enemies cannot share a world: they find each other wherever they are,
    ///     and their battle ends the world (WorldsEnd) unless the "World's End" setting is off
    ///   * wherever it walks the world shows it: void motes and prismatic light round it, the ground cracking
    ///
    /// Gods against a Primordial (Pantheon): one or two Gods die. Three or more Gods fighting the same
    /// Primordial join their power every so often into one strike (each God a beam of its school's
    /// colour, converging on it) that goes through its hide: each God in it takes 9% of the Primordial's
    /// life. Three or four Gods wound it and usually fall; five or more, if they last, bring it down.
    /// </summary>
    internal static class Primordials
    {
        public const string TraitId = "sakuga_tier_primordial";
        /// <summary>The trait (Traits.Register makes it).</summary>
        public static ActorTrait Trait;

        private static readonly List<Actor> _living = new List<Actor>();
        private static float _scan, _auraT;

        public static IReadOnlyList<Actor> Living => _living;

        public static bool Any => _living.Count > 0;

        public static bool Is(Actor a)
        {
            return Trait != null && a != null && a.traits != null && a.traits.Contains(Trait);
        }

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += Frame;
            Hooks.WorldCleared += () =>
            {
                _living.Clear();
                WorldsEnd.Clear();
                Pantheon.Clear();
            };
        }

        /// <summary>The trait was given (Traits): no other tier is kept, and the world is told.</summary>
        public static void Given(Actor a)
        {
            if (a == null)
            {
                return;
            }
            for (int i = 1; i <= Tiers.Count; i++)
            {
                ActorTrait t = Tiers.Trait((Tier)i);
                if (t != null && a.traits.Contains(t))
                {
                    a.removeTrait(t);
                }
            }
            if (!_living.Contains(a))
            {
                _living.Add(a);
            }
            // it walks no school and carries no school's weapon: its powers are its own
            Schools.Fill(a);
            Schools.Arm(a, true);
            if (Dev.On) Dev.Trace("primordial: " + a.asset.id + "#" + a.Sid() + " is Primordial");
        }

        private static void Frame(float dt)
        {
            if (dt <= 0f || World.world == null)
            {
                return;
            }
            _scan -= dt;
            if (_scan <= 0f)
            {
                _scan = 0.5f;
                Rescan();
            }
            WorldsEnd.Tick(dt);
            if (_living.Count == 0)
            {
                return;
            }
            Pantheon.Tick(dt);
            _auraT -= dt;
            if (_auraT <= 0f)
            {
                _auraT = 0.05f;
                for (int i = 0; i < _living.Count; i++)
                {
                    Actor a = _living[i];
                    if (a != null && a.isAlive())
                    {
                        Aura(a);
                    }
                }
            }
        }

        private static void Rescan()
        {
            _living.Clear();
            if (Trait == null)
            {
                return;
            }
            foreach (Actor a in World.world.units.getSimpleList())
            {
                if (a != null && a.isAlive() && a.traits != null && a.traits.Contains(Trait))
                {
                    _living.Add(a);
                }
            }
            for (int i = 0; i < _living.Count; i++)
            {
                StrikeBack(_living[i]);
                if (Fighters.TryGet(_living[i], out Fighter f) && f.InCombat)
                {
                    _fightUntil = Time.unscaledTime + 2.5f;
                }
            }
        }

        /// <summary>
        /// A Primordial turns on whoever attacks it. (Nearly every blow on one is parried or shrugged off,
        /// so the game never counts it as attacked: without this it would stand in an army parrying, and
        /// never fight, and never use a power.)
        /// </summary>
        private static void StrikeBack(Actor p)
        {
            if (p == null || !p.isAlive() || p.isFighting() || WorldsEnd.Holds(p) || Moves.Busy(p) || Awakenings.Waiting(p))
            {
                return;
            }
            Actor foe = null;
            float best = 16f * 16f;
            foreach (Actor u in World.world.units.getSimpleList())
            {
                if (u == null || u == p || !u.isAlive() || u.attack_target != p)
                {
                    continue;
                }
                float d = (u.current_position - p.current_position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    foe = u;
                }
            }
            if (foe != null)
            {
                p.addAggro(foe);
                p.startFightingWith(foe);
                if (Dev.On) Dev.Trace("primordial: " + p.asset.id + "#" + p.Sid() + " turns on " + foe.asset.id + "#" + foe.Sid());
            }
        }

        // ------------------------------------------------------------------------------ colours --

        public static readonly Color Void = new Color(0.04f, 0.01f, 0.08f, 1f);

        // ----------------------------------------------------------- screen comfort --

        private static float _fightUntil = -1f;

        /// <summary>Is a Primordial fighting or awakening (the "Primordial screen shake" setting applies)?</summary>
        public static bool ShakeScope => WorldsEnd.Active || Awakening.Live > 0 || Time.unscaledTime < _fightUntil;

        /// <summary>Camera shake while Primordials fight: 0 none, 1 a little, 2 full.</summary>
        public static float ShakeK => Settings.PrimordialShake <= 0 ? 0f : Settings.PrimordialShake == 1 ? 0.3f : 1f;

        /// <summary>A full-screen flash, softer with less shake.</summary>
        public static void Flash(Color c, float life)
        {
            float k = Settings.PrimordialShake <= 0 ? 0.35f : Settings.PrimordialShake == 1 ? 0.65f : 1f;
            CineKit.ViewFlash(Fx.WithA(c, c.a * k), life);
        }

        /// <summary>The prismatic light of the first things (cycling through every colour).</summary>
        public static Color Prism(float offset = 0f, float sat = 0.55f)
        {
            return Color.HSVToRGB(Mathf.Repeat(Time.time * 0.35f + offset, 1f), sat, 1f);
        }

        /// <summary>Its aura (every 0.05 s while visible): void motes orbiting it, prismatic light, the ground crazing under it.</summary>
        private static void Aura(Actor a)
        {
            Vector2 c = Choreo.BodyAt(a, 0.5f);
            if (!Kit.Visible(c, 0.6f))
            {
                return;
            }
            float px = Choreo.PxOf(a), k = Choreo.KOf(a);
            float half = Choreo.SpritePx(a) * px * 0.6f;
            // void motes orbiting, black at the heart, a prismatic rim
            for (int i = 0; i < 2; i++)
            {
                float ang = Time.time * 3f + i * Mathf.PI + Random.Range(-0.4f, 0.4f);
                Vector2 at = c + new Vector2(Mathf.Cos(ang) * half * 1.6f, Mathf.Sin(ang) * half * 0.8f);
                Particle m = Fx.Spawn(Art.Disk(Random.Range(1, 3), 1f, 0.2f), at, px, 0.4f, FxLayer.Top, 15);
                m?.Moving(new Vector2(0f, Random.Range(4f, 10f)) * px, 0f, 1f).Color(Fx.WithA(Void, 0.95f), Fx.WithA(Prism(i * 0.3f), 0.6f), Fx.WithA(Prism(i * 0.3f), 0f));
            }
            if (Random.value < 0.35f)
            {
                Trails.Ghost(a, 0.18f, Fx.WithA(Prism(), 0.55f), Fx.WithA(Prism(0.3f), 0f));
            }
            if (Random.value < 0.08f)
            {
                // a crackle of power round it
                Vector2 p0 = c + Random.insideUnitCircle * half;
                PrimFx.Bolt(p0, p0 + Random.insideUnitCircle * half * 2f, 2f * px, 0.1f, Prism(0.5f), FxLayer.Top, 16, false, 1);
            }
            if (Random.value < 0.06f)
            {
                Vector2 feet = Choreo.BodyAt(a, 0f);
                Vector2 dir = Random.insideUnitCircle.normalized;
                PrimFx.Crack(feet, dir, Random.Range(4f, 9f) * px * k, 1.4f * px, 0.8f, Prism(), FxLayer.Ground, 5, false, true, 0);
            }
        }

        // ------------------------------------------------------------------------------- damage --

        /// <summary>
        /// Combat.Hit's factor for a blow from 'a' on 'v': a Primordial hits three times as hard; a blow on a
        /// Primordial from anything less lands at a third (the Gods' joined strike excepted).
        /// </summary>
        public static float DamageMult(Actor a, Actor v)
        {
            if (_living.Count == 0)
            {
                return 1f;
            }
            bool pa = Is(a), pv = Is(v);
            if (pa && !pv)
            {
                return 3f;
            }
            if (pv && !pa)
            {
                return Pantheon.Striking ? 1f : 0.3f;
            }
            return 1f;
        }

        /// <summary>
        /// A Primordial power lands on 'v': a God loses 'godFrac' of its life (its Last Light may yet
        /// save it), a Grandmaster or anything less is simply ended. Armour is nothing to it.
        /// </summary>
        public static void Doom(Actor a, Actor v, float godFrac)
        {
            if (v == null || !v.isAlive() || v == a || Is(v))
            {
                return;
            }
            float frac = Tiers.Of(v) >= Tier.God ? godFrac : 5f;
            Smite(a, v, frac);
        }

        /// <summary>
        /// 'v' takes 'frac' of its max health through any armour (it may die). 'pierce': it also lands on a
        /// unit in an intangible move (the World's End holds, a Primordial between the beats of its powers),
        /// which every other blow passes through.
        /// </summary>
        public static void Smite(Actor a, Actor v, float frac, bool pierce = false)
        {
            if (v == null || !v.isAlive() || !v.hasHealth())
            {
                return;
            }
            float armor = Mathf.Min(v.stats["armor"], 95f);
            float through = Mathf.Max(0.05f, 1f - armor / 100f);
            float d = frac * v.getMaxHealth() / through + 1f;
            Actor was = Combat.Piercing;
            if (pierce)
            {
                Combat.Piercing = v;
            }
            Duels.OwnBlow++;
            try
            {
                v.getHit(d, true, AttackType.Other, a, false, false, true);
            }
            finally
            {
                Duels.OwnBlow--;
                Combat.Piercing = was;
            }
        }

        /// <summary>The enemies of 'a' (or everyone, 'all') within 'r' tiles of 'at', nearest first.</summary>
        public static List<Actor> Around(Actor a, Vector2 at, float r, bool all, List<Actor> into)
        {
            into.Clear();
            float r2 = r * r;
            foreach (Actor u in World.world.units.getSimpleList())
            {
                if (u == null || u == a || !u.isAlive() || Is(u))
                {
                    continue;
                }
                if ((u.current_position - at).sqrMagnitude > r2)
                {
                    continue;
                }
                if (!all && (a == null || !Combat.IsFoe(a, u)))
                {
                    continue;
                }
                into.Add(u);
            }
            into.Sort((x, y) => (x.current_position - at).sqrMagnitude.CompareTo((y.current_position - at).sqrMagnitude));
            return into;
        }
    }

    // ===================================================================== PANTHEON ==

    /// <summary>
    /// Gods against a Primordial: when three or more Gods are fighting the same Primordial (its enemies,
    /// within 22 tiles of it), every 16 seconds they join their power: each God's light runs to it as a
    /// beam of its school's colour, the beams meet on the Primordial, and the strike goes through its
    /// hide: 9% of its life for every God in it, and the land round it blasted.
    /// </summary>
    internal static class Pantheon
    {
        /// <summary>The joined strike is landing (Primordials.DamageMult lets it through the hide).</summary>
        public static bool Striking;

        private sealed class Strike
        {
            public Actor P;
            public List<Actor> Gods = new List<Actor>();
            public float T;
            public bool Struck;
        }

        private static readonly List<Strike> _on = new List<Strike>();
        private static readonly Dictionary<long, double> _cool = new Dictionary<long, double>();
        private static readonly List<Actor> _gods = new List<Actor>();
        private static float _look;
        private const float Gather = 1.5f, Cooldown = 16f;

        public static void Clear()
        {
            _on.Clear();
            _cool.Clear();
        }

        /// <summary>
        /// One God's power running into the joined strike, in its school's own form: a ki stream, a file
        /// of flying swords, fists of pressure, a spiral of wind with crescents in it, rocks torn up and
        /// drawn along, ribbons of shadow, a flight of spears (never one plain beam for all of them).
        /// </summary>
        public static void Stream(School sc, Vector2 from, Vector2 to, Hue h, float px, float k, float charge)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                return;
            }
            Vector2 dir = d / len;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float w = (0.25f + 0.35f * charge) * k;
            float life = Mathf.Clamp(len / 30f, 0.2f, 0.6f);
            switch (sc)
            {
                case School.KiArts:
                    PrimFx.Beam(from, to, w * 1.4f, 0.05f, FxLayer.Top, 20, false, h.Main, Color.Lerp(h.Main, Color.white, 0.5f), false);
                    break;
                case School.ThousandBlades:
                case School.HuntersWay:
                case School.IronFist:
                case School.EarthBreaker:
                    {
                        if (Random.value < 0.5f)
                        {
                            Sprite s = sc == School.ThousandBlades ? ChoreoArt.LightSword(Mathf.RoundToInt(9f * k))
                                : sc == School.HuntersWay ? ChoreoArt.Smear(Mathf.RoundToInt(11f * k), 1, 2)[0]
                                : sc == School.IronFist ? ChoreoArt.Fist(Mathf.RoundToInt(10f * k))
                                : Art.Chunks[Random.Range(0, Art.Chunks.Length)];
                            Particle p = Fx.Spawn(s, from + new Vector2(0f, Random.Range(-0.3f, 0.3f) * k), sc == School.EarthBreaker ? px * 2.5f : px, life, FxLayer.Top, 21);
                            if (p != null)
                            {
                                p.Moving(d / life);
                                p.Rot = sc == School.EarthBreaker ? Random.Range(0f, 360f) : ang;
                                if (sc == School.EarthBreaker)
                                {
                                    p.Spin = Random.Range(-300f, 300f);
                                }
                                Color body = sc == School.EarthBreaker ? new Color(0.6f, 0.52f, 0.45f, 1f) : Color.Lerp(h.Core, Color.white, 0.5f);
                                p.Color(body, body, Fx.WithA(h.Edge, 0.6f));
                            }
                        }
                        // the trail of it: a smear of the school's light, pointed at both ends
                        if (Random.value < 0.35f)
                        {
                            Vector2 a0 = Vector2.Lerp(from, to, Random.Range(0f, 0.6f));
                            PrimFx.Streak(a0, a0 + dir * Random.Range(2f, 4f) * k, w * 0.6f, 0.15f, FxLayer.Top, 19, false, h.Main);
                        }
                        break;
                    }
                case School.TempestLeg:
                    {
                        // a spiral of wind, crescents of air riding it
                        PrimFx.Flow(from, to, w * 1.6f, 0.05f, FxLayer.Top, 19, false, Color.Lerp(h.Main, Color.white, 0.4f), Fx.WithA(Color.white, 0.8f));
                        if (Random.value < 0.4f)
                        {
                            Particle c = Fx.Spawn(ChoreoArt.Crescent(Mathf.RoundToInt(5f * k), 1), from, px, life, FxLayer.Top, 21);
                            if (c != null)
                            {
                                c.Moving(d / life);
                                c.Spin = 720f;
                                c.Color(Color.white, Fx.WithA(h.Main, 0.7f));
                            }
                        }
                        break;
                    }
                case School.PhantomStep:
                    {
                        // ribbons of shadow sliding to it
                        if (Random.value < 0.5f)
                        {
                            Vector2 side = new Vector2(-dir.y, dir.x) * Random.Range(-0.6f, 0.6f) * k;
                            PrimFx.Streak(from + side, Vector2.Lerp(from, to, Random.Range(0.5f, 1f)) + side, w, 0.2f, FxLayer.Top, 20, false, Color.Lerp(h.Main, Color.black, 0.4f));
                        }
                        break;
                    }
                default:
                    PrimFx.Flow(from, to, w * 1.4f, 0.05f, FxLayer.Top, 19, false, h.Main, Color.Lerp(h.Main, Color.white, 0.5f));
                    break;
            }
        }

        public static void Tick(float dt)
        {
            for (int i = _on.Count - 1; i >= 0; i--)
            {
                if (!Step(_on[i], dt))
                {
                    _on.RemoveAt(i);
                }
            }
            _look -= dt;
            if (_look > 0f)
            {
                return;
            }
            _look = 1f;
            foreach (Actor p in Primordials.Living)
            {
                // (not one still awakening: the joint strike pierces, and its awakening is no fight yet)
                if (p == null || !p.isAlive() || WorldsEnd.Holds(p) || Awakenings.Waiting(p))
                {
                    continue;
                }
                long id = p.Sid();
                if (_cool.TryGetValue(id, out double at) && Fighter.Now < at)
                {
                    continue;
                }
                _gods.Clear();
                foreach (Actor g in World.world.units.getSimpleList())
                {
                    if (g != null && g.isAlive() && !Primordials.Is(g) && Tiers.Of(g) == Tier.God && Combat.IsFoe(g, p)
                        && (g.current_position - p.current_position).sqrMagnitude <= 22f * 22f)
                    {
                        _gods.Add(g);
                    }
                }
                if (_gods.Count < 3)
                {
                    continue;
                }
                _cool[id] = Fighter.Now + Cooldown;
                Strike s = new Strike { P = p };
                s.Gods.AddRange(_gods);
                _on.Add(s);
                Spotlight(p, _gods.Count);
            }
        }

        private static void Spotlight(Actor p, int n)
        {
            if (Dev.On) Dev.Trace("pantheon: " + n + " Gods join their power against " + p.asset.id + "#" + p.Sid());
            Vector2 at = Choreo.BodyAt(p, 0.5f);
            if (Kit.Visible(at, 0.5f))
            {
                Cinema.Title(n + " GODS AS ONE", new Color(1f, 0.85f, 0.4f, 1f), 1.6f, at + new Vector2(0f, 3f), 0.9f);
            }
        }

        private static bool Step(Strike s, float dt)
        {
            s.T += dt;
            Actor p = s.P;
            if (p == null || !p.isAlive())
            {
                return false;
            }
            Vector2 target = Choreo.BodyAt(p, 0.5f);
            float px = Choreo.PxOf(p);
            bool vis = Kit.Visible(target, 0.8f);
            if (!s.Struck)
            {
                float k = Mathf.Clamp01(s.T / Gather);
                foreach (Actor g in s.Gods)
                {
                    if (g == null || !g.isAlive())
                    {
                        continue;
                    }
                    if (!vis && !Kit.Visible(g.current_position, 0.5f))
                    {
                        continue;
                    }
                    School sc = Schools.PrimaryOf(g);
                    Hue h = Hue.For(g, sc, 3);
                    Vector2 from = Choreo.BodyAt(g, 0.6f);
                    // each God gives its power in its own school's form, all of it running to the Primordial
                    Pantheon.Stream(sc, from, target, h, px, Choreo.KOf(g), k);
                    Trails.Ghost(g, 0.08f, Fx.WithA(h.Main, 0.7f), Fx.WithA(h.Edge, 0f));
                    if (Random.value < 0.3f)
                    {
                        ChoreoFx.Motes(from, px, 2, 8f, 0.12f, h.Core, h.Edge, FxLayer.Top);
                    }
                }
                if (vis && Random.value < 0.5f)
                {
                    Kit.Flash(target, px, Mathf.RoundToInt((3f + 8f * k) * Choreo.KOf(p)), 0.05f, Fx.WithA(Color.white, 0.8f));
                }
                if (s.T >= Gather)
                {
                    s.Struck = true;
                    Land(s, target, px, vis);
                }
                return true;
            }
            return s.T < Gather + 0.6f;
        }

        private static void Land(Strike s, Vector2 target, float px, bool vis)
        {
            Actor p = s.P;
            int n = 0;
            Actor by = null;
            foreach (Actor g in s.Gods)
            {
                if (g != null && g.isAlive())
                {
                    n++;
                    by = by ?? g;
                }
            }
            if (n == 0)
            {
                return;
            }
            if (vis)
            {
                Primordials.Flash(Fx.WithA(Color.white, 0.6f), 0.3f);
                Spec.Blob(target, 3f * Choreo.KOf(p), px, 0.6f, FxLayer.Top, 24, 1f, 0.5f, Color.white, new Color(1f, 0.9f, 0.6f, 1f), Fx.WithA(Primordials.Prism(), 0.6f), Fx.WithA(Primordials.Prism(0.4f), 0f))?.Sized(0.3f, 1.6f);
                Fx.Ring(target, px, 0.6f, 2f, 60f * Choreo.KOf(p), 3, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Color.white, Fx.WithA(new Color(1f, 0.85f, 0.4f, 1f), 0.6f), Fx.WithA(Color.white, 0f));
                Kit.Shake(target, 0.5f, 0.012f);
            }
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", Combat.TileAt(target));
            Kit.Ravage(p.current_position, 3f, 3, by, false);
            Striking = true;
            try
            {
                // (through its hide, and through the intangible beats of its own powers)
                Primordials.Smite(by, p, 0.09f * n, true);
            }
            finally
            {
                Striking = false;
            }
        }
    }
}
