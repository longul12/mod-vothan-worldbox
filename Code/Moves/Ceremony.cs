using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PROMOTION CEREMONIES: a promotion is a moment seen in the world, bigger at every tier, whether
    /// the unit earned it (a trial) or was given it (the Train power, the trait editor, dev tools).
    /// The ceremony is a short move when the unit is free to stop (Moves.CanPerform); a unit that is
    /// busy, flying, swimming or in a crowd of ceremonies gets the same effects without stopping (a
    /// timeline that follows it), and a unit spawned and promoted in the same moment (a script, the
    /// dev demos) only flares. A sleeping unit wakes with a start and plays its ceremony a moment
    /// later; a new Grandmaster or God that is busy (a technique, mid-air, asleep) waits up to 2.5 /
    /// 4.5 s for the moment it is free, so its ceremony plays in full, before falling back to effects.
    ///
    ///   NOVICE       0.5 s  a crouch and a pop: two white flashes 0.1 s apart, a 0.8-tile dust ring,
    ///                       three white sparks rising
    ///   ADEPT        0.6 s  leans back, a blue flash, a 1-tile blue ring, a "ting" at the head
    ///   EXPERT       0.8 s  a trembling crouch, then flexes up: a yellow flash and a hit-stop, a
    ///                       1.5-tile ring, six yellow zig-zag sparks crackling outward
    ///   MASTER       1.2 s  lifts a tile into the air inside a spiral of orange embers, a flash disk,
    ///                       drops into a superhero landing: a 2.5-tile ring, dust, small cracks, the
    ///                       ground scorched under it
    ///   GRANDMASTER  1.8 s  the world slows (0.5 s, when nothing else plays); dust and red sparks
    ///                       drawn in, then released: a red flash, a 4-tile ring, glowing cracks and
    ///                       scorched ground, pebbles thrown up; a red aura of flame tongues and a
    ///                       pillar of vertical speed lines while it hovers, embers, a glint
    ///   COMBAT GOD   3.5 s  the ascension (below): a beam of light from the sky; its shockwave tears
    ///                       the turf at its feet and sears a ring into the ground
    ///
    /// Demotions (the Humble power, a removed tier): a short grey puff drifting down, no ceremony.
    /// A unit that held several tiers (a save, another mod) keeps the highest: each removed tier's
    /// colour closes in on it as a ring, 0.1 s apart.
    /// </summary>
    internal static class Ceremonies
    {
        /// <summary>Ceremony moves and timelines playing at once before new ones only flare.</summary>
        private const int MaxLive = 6;

        internal static int Live;

        private static readonly List<Rite> _rites = new List<Rite>();

        /// <summary>
        /// The tier each unit last reached (the trait editor reports a swap as "lost its tier" then
        /// "gained one" with no old tier, so the old one is remembered here).
        /// </summary>
        private static readonly Dictionary<long, Tier> _known = new Dictionary<long, Tier>();

        // a unit that just lost its last tier: a demotion, unless a new tier follows in the same frame
        private static Actor _lost;
        private static long _lostId = -1;
        private static Tier _lostTier;
        private static int _lostFrame;

        /// <summary>Where a ceremony happens this frame: the unit's ground point, body, head and pixel size.</summary>
        internal struct Spot
        {
            public Actor A;
            public Ceremony M;
            public Vector2 Ground, Body, Head;
            public float Px, K;
            /// <summary>Drawn above the dark veil and playing through the freeze (the full ascension).</summary>
            public bool Cine;

            public FxLayer Top => Cine ? FxLayer.Cine : FxLayer.Top;
            public FxLayer Low => Cine ? FxLayer.Cine : FxLayer.Ground;
            public WorldTile Tile => Combat.TileAt(Ground);
        }

        /// <summary>A ceremony played as effects only, following a unit that can't stop for it.</summary>
        private sealed class Rite
        {
            public Actor A;
            public long Id;
            public Tier Tier;
            public float T;
            public int Cues;
            public float Stream;
        }

        /// <summary>
        /// A ceremony waiting for its unit to be free (asleep and being woken, finishing a technique,
        /// landing): it starts the moment the unit can stop, or plays as effects only when 'Left' runs out.
        /// </summary>
        private sealed class Pending
        {
            public Actor A;
            public long Id;
            public Tier Tier;
            public float Left;
            public string Why;
            /// <summary>Not before this (unscaled) time: the camera is still on its way (Spotlight).</summary>
            public float NotBefore;
        }

        private static readonly List<Pending> _pending = new List<Pending>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.TierChanged += OnTierChanged;
            Hooks.TiersMerged += OnMerged;
            Hooks.MoveEnded += OnMoveEnded;
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            _rites.Clear();
            _pending.Clear();
            _known.Clear();
            _lost = null;
            _lostId = -1;
            Live = 0;
        }

        // ------------------------------------------------------------ events --

        private static void OnTierChanged(Actor a, Tier old, Tier now, bool natural)
        {
            if (a == null || !a.isAlive() || a.asset == null)
            {
                return;
            }
            ActorAsset s = a.asset;
            if (s.has_avatar_prefab || !s.has_sprite_renderer || s.ignore_generic_render || s.id == "crabzilla")
            {
                return;   // no sprite to celebrate (Crabzilla's own story is the Kaiju Killer's)
            }
            long id = a.Sid();
            if (now != Tier.None && Primordials.Is(a))
            {
                // not a God ascending: the first of all things wakes (the tiers it gave up go quietly)
                if (_lostId == id)
                {
                    _lost = null;
                    _lostId = -1;
                }
                Unqueue(id);
                DropRites(id, Tier.None);
                _known[id] = now;
                if (!Fresh(a))
                {
                    Awakenings.Request(a);
                }
                return;
            }
            Tier before = old != Tier.None ? old : _known.TryGetValue(id, out Tier k) ? k : Tier.None;
            if (now == Tier.None)
            {
                _known.Remove(id);
                Unqueue(id);
                if (old == Tier.None)
                {
                    // its last tier trait was removed: wait a frame, the trait editor may be swapping it
                    FlushLost();
                    _lost = a;
                    _lostId = id;
                    _lostTier = before;
                    _lostFrame = Time.frameCount;
                    return;
                }
                Revoke(a, id, now);
                Demotion(a);
                return;
            }
            if (_lostId == id)
            {
                before = _lostTier;   // a swap: compare with the tier it just gave up
                _lost = null;
                _lostId = -1;
            }
            if (_known.Count > 8192)
            {
                _known.Clear();
            }
            _known[id] = now;
            if (now <= before)
            {
                if (now < before)
                {
                    Unqueue(id);
                    Revoke(a, id, now);
                    Demotion(a);
                }
                return;
            }
            if (Fresh(a) || (Moves.HeldAny && Moves.Held.ContainsKey(id)))
            {
                // spawned and promoted in one breath (a script, a dev demo): no time for a ceremony
                Flare(a, now, 12f, 0.4f, "fresh");
                return;
            }
            if (Live >= MaxLive && now != Tier.God)
            {
                Flare(a, now, 14f, 0.45f, "crowd");
                return;
            }
            Unqueue(id);
            if (now == Tier.God && !Cinema.FullAt(a.current_position) && Spotlight.Ascension(a))
            {
                // the camera is on its way and the world slows to normal speed: the ceremony plays in full
                // once it arrives
                _pending.Add(new Pending { A = a, Id = id, Tier = now, Left = 6f, Why = "spotlight", NotBefore = Time.unscaledTime + 0.9f });
                return;
            }
            if (Moves.CanPerform(a) && Ceremony.Begin(a, now))
            {
                return;
            }
            Queue(a, now);
        }

        private static void Flare(Actor a, Tier t, float r, float life, string why)
        {
            Spot sp = Of(a);
            Kit.Flare(sp.Ground, sp.Px, Tiers.Color(t), r * sp.K, life);
            if (Dev.On)
            {
                Dev.Watch(sp.Body + new Vector2(0f, 1f));
                Dev.Trace("ceremony " + t + " for " + a.getName() + ": flare only (" + why + ")");
            }
        }

        // ---------------------------------------------------------- waiting --

        /// <summary>
        /// Why a unit can't stop for its ceremony right now (Moves.CanPerform's reasons, worded for the
        /// dev log), and whether that will pass soon.
        /// </summary>
        private static string Why(Actor a, out bool passes)
        {
            passes = true;
            if (Moves.TryGet(a, out Move m))
            {
                return "busy: " + m.GetType().Name;
            }
            if (a.is_unconscious)
            {
                return a.hasStatus("sleeping") ? "asleep" : "unconscious";
            }
            if (a.position_height > 0f || a.under_forces)
            {
                return "in the air";
            }
            if (a.isInMagnet())
            {
                return "held by the god hand";
            }
            if (a.isInsideSomething())
            {
                return "inside";
            }
            if (a.isInLiquid())
            {
                return "in water";
            }
            passes = false;
            if (a.isFlying())
            {
                return "flying";
            }
            if (a.is_immovable)
            {
                return "immovable";
            }
            return ControllableUnit.isControllingUnit(a) ? "possessed" : "can't perform";
        }

        /// <summary>
        /// The unit can't stop for its ceremony right now. A sleeping unit wakes with a start (the
        /// power floods in). A Grandmaster or a God waits for the moment it is free (its technique
        /// ends, it lands, it wakes): their ceremonies are worth the wait. Anyone else, busy in a
        /// fight, gets the effects without stopping.
        /// </summary>
        private static void Queue(Actor a, Tier tier)
        {
            string why = Why(a, out bool passes);
            bool asleep = why == "asleep";
            if (asleep)
            {
                Wake(a);
            }
            float wait = !passes ? 0f
                : tier == Tier.God ? 4.5f
                : tier == Tier.Grandmaster ? 2.5f
                : asleep ? 1f
                : why == "busy: " + nameof(Ceremony) ? 1.3f   // promoted again mid-ceremony: the next one follows
                : 0f;
            if (wait <= 0f && !InKaijuKiller(a))
            {
                StartRite(a, tier, why);
                return;
            }
            _pending.Add(new Pending { A = a, Id = a.Sid(), Tier = tier, Left = wait, Why = why });
            if (Dev.On)
            {
                Dev.Watch(Of(a).Body + new Vector2(0f, 1f));
                Dev.Trace("ceremony " + tier + " for " + a.getName() + " waits (" + why + (asleep ? ", woken" : "") + ")");
            }
        }

        /// <summary>
        /// Flying with the Kaiju Killer (high in the sky, its real ground far below, the finale playing):
        /// its ceremony waits for the landing however long that takes, then plays in full.
        /// </summary>
        private static bool InKaijuKiller(Actor a)
        {
            return Moves.TryGet(a, out Move m) && KaijuKillerMove.Anchored(m, out _);
        }

        private static void Wake(Actor a)
        {
            try
            {
                if (a.hasStatus("sleeping"))
                {
                    a.stopSleeping();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] ceremony wake: " + e);
            }
        }

        private static void Unqueue(long id)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Id == id)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        /// <summary>Stops the unit's effects-only ceremonies for tiers above 'above' (Tier.None: all of them).</summary>
        internal static void DropRites(long id, Tier above)
        {
            for (int i = _rites.Count - 1; i >= 0; i--)
            {
                if (_rites[i].Id == id && _rites[i].Tier > above)
                {
                    _rites.RemoveAt(i);
                    Live = Mathf.Max(0, Live - 1);
                }
            }
        }

        /// <summary>
        /// A demotion: whatever still celebrates a tier above 'now' stops (an effects-only ceremony, the
        /// ceremony move itself: a God's ascension would still throw, tear the ground and heal at 2 s).
        /// </summary>
        private static void Revoke(Actor a, long id, Tier now)
        {
            DropRites(id, now);
            if (Moves.TryGet(a, out Move m) && m is Ceremony c && c.Tier > now)
            {
                c.Cancel();
            }
        }

        /// <summary>A move ended: a unit waiting for its ceremony may be free this very moment (before its AI picks a new one).</summary>
        private static void OnMoveEnded(Move m, bool aborted)
        {
            if (_pending.Count == 0 || m == null || m.A == null)
            {
                return;
            }
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (p.Id == m.Id && TryPending(p))
                {
                    _pending.Remove(p);
                }
            }
        }

        /// <summary>Starts a waiting ceremony if its unit is free now.</summary>
        private static bool TryPending(Pending p)
        {
            Actor a = p.A;
            if (Time.unscaledTime < p.NotBefore)
            {
                return false;
            }
            if (p.Why == "spotlight" && p.Left > 1.5f && p.A != null && !Cinema.FullAt(p.A.current_position))
            {
                return false;   // the camera has not reached it yet (or the world is still fast): a little longer
            }
            if (a == null || a.Sid() != p.Id || !a.isAlive() || !Moves.CanPerform(a))
            {
                return false;
            }
            if (Live >= MaxLive && p.Tier != Tier.God)
            {
                Flare(a, p.Tier, 14f, 0.45f, "crowd");
                return true;
            }
            return Ceremony.Begin(a, p.Tier, p.Why);
        }

        private static void StartRite(Actor a, Tier tier, string why)
        {
            long id = a.Sid();
            DropRites(id, Tier.None);   // a newer tier supersedes the one still playing
            _rites.Add(new Rite { A = a, Id = id, Tier = tier });
            Live++;
            if (Dev.On)
            {
                Dev.Watch(Of(a).Body + new Vector2(0f, 1f));
                Dev.Trace("ceremony " + tier + " for " + a.getName() + ": effects only (" + why + ")");
            }
        }

        /// <summary>Several tiers at once: the highest is kept, each removed one closes in on the unit as a ring.</summary>
        private static void OnMerged(Actor a, List<Tier> removed, Tier keep)
        {
            if (a == null || !a.isAlive() || removed == null || removed.Count == 0 || a.asset == null || !a.asset.has_sprite_renderer)
            {
                return;
            }
            Spot s = Of(a);
            if (Dev.On)
            {
                Dev.Watch(s.Body + new Vector2(0f, 1f));
                Dev.Trace("tiers merged on " + a.getName() + ": " + removed.Count + " ring(s), keeps " + keep);
            }
            if (!Kit.Visible(s.Ground))
            {
                return;
            }
            for (int i = 0; i < removed.Count; i++)
            {
                Color c = Tiers.Color(removed[i]);
                CineKit.Ring(s.Ground, s.Px, 0.24f, 16f * s.K, 2f, 1, 2, Art.Ground, FxLayer.Ground, 6, false, i * 0.1f,
                    Fx.WithA(c, 0f), Fx.WithA(c, 0.9f), Fx.WithA(Color.white, 1f));
            }
            Particle f = Kit.Visible(s.Body) ? CineKit.Flash(s.Body, s.Px, Mathf.RoundToInt(6f * s.K), 0.2f, FxLayer.Top, 28, false,
                Color.white, Fx.WithA(Tiers.Color(keep), 0.7f), Fx.WithA(Tiers.Color(keep), 0f)) : null;
            if (f != null)
            {
                f.Delay = removed.Count * 0.1f + 0.12f;
            }
        }

        /// <summary>A short grey puff drifting down: the unit lost a tier. No ceremony.</summary>
        private static void Demotion(Actor a)
        {
            Spot s = Of(a);
            if (Dev.On)
            {
                Dev.Watch(s.Body + new Vector2(0f, 1f));
                Dev.Trace("demotion of " + a.getName() + " to " + Tiers.Of(a));
            }
            if (!Kit.Visible(s.Body))
            {
                return;
            }
            Color grey = new Color(0.6f, 0.6f, 0.64f, 1f);
            float px = s.Px;
            int n = Fx.Count(6);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = s.Ground + new Vector2(Random.Range(-3f, 3f) * px * s.K, 0f);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at, px, Random.Range(0.4f, 0.55f), i % 2 == 0 ? FxLayer.Top : FxLayer.Behind, 6);
                if (p == null)
                {
                    break;
                }
                p.Z = (s.Head.y - s.Ground.y) * Random.Range(0.6f, 1f);
                p.Moving(new Vector2(Random.Range(-6f, 6f) * px, 0f), -Random.Range(12f, 20f) * px * s.K, 1.5f);
                p.Bounce = 0f;
                p.Sized(0.9f, 0.55f).Color(Fx.WithA(grey, 0f), Fx.WithA(grey, 0.7f), Fx.WithA(grey, 0f));
            }
            CineKit.Ring(s.Ground, px, 0.3f, 9f * s.K, 3f, 1, 1, Art.Ground, FxLayer.Ground, 5, false, 0f, Fx.WithA(grey, 0.8f), Fx.WithA(grey, 0f));
        }

        private static bool Fresh(Actor a)
        {
            return Time.time - a.created_time_unscaled < 0.75;
        }

        // ------------------------------------------------------------ helpers --

        internal static Spot Of(Actor a)
        {
            float scale = a.actor_scale;
            Sprite sp = Fx.UnitSprite(a);
            float pivot = sp != null ? Mathf.Clamp(sp.rect.height * 0.36f, 1.5f, 40f) : 3.5f;
            Vector2 g = a.current_position;
            float h = Mathf.Max(0f, a.position_height);
            if (Moves.TryGet(a, out Move m) && KaijuKillerMove.Anchored(m, out Vector2 shadow))
            {
                // a Kaiju Killer performer stands on an anchor under its feet: its real ground is its shadow
                h = Mathf.Max(0f, g.y + h - shadow.y);
                g = shadow;
            }
            return new Spot
            {
                A = a,
                Ground = g,
                Body = g + new Vector2(0f, h + pivot * scale),
                Head = g + new Vector2(0f, h + pivot * 2.2f * scale),
                Px = Mathf.Clamp(scale, 0.04f, 0.4f),
                K = Mathf.Sqrt(Mathf.Clamp(scale / 0.1f, 0.6f, 4f))
            };
        }

        /// <summary>The whole unit flashes a colour (a silhouette when it is a ceremony move, a flash disk otherwise).</summary>
        internal static void UnitFlash(Spot s, Color c, float seconds)
        {
            if (s.M != null && s.M.FlashUnit(c, seconds))
            {
                return;
            }
            CineKit.Flash(s.Body, s.Px, Mathf.RoundToInt(5f * s.K), seconds + 0.06f, s.Top, 28, s.Cine, Fx.WithA(c, 0.9f), Fx.WithA(c, 0.5f), Fx.WithA(c, 0f));
        }

        /// <summary>A ring on the ground, sized in tiles (times the unit's size).</summary>
        internal static Particle Ring(Spot s, float life, float r0, float r1, int t0, int t1, float delay, params Color[] colors)
        {
            float px = s.Px;
            return CineKit.Ring(s.Ground, px, life, r0 * s.K / px, r1 * s.K / px, t0, t1, Art.Ground, s.Low, 6, s.Cine, delay, colors);
        }

        /// <summary>Sparks rising from the unit (white for a Novice).</summary>
        private static void Rising(Spot s, int n, Color c)
        {
            if (!Kit.Visible(s.Body))
            {
                return;
            }
            float px = s.Px;
            for (int i = 0; i < n; i++)
            {
                // quick sparks popping off the shoulders and the head, fanning out, gone in a blink
                // (slow long streaks hung over the head like antennae)
                float side = i - (n - 1) * 0.5f;
                Vector2 at = s.Body + new Vector2(side * 2.5f * px * s.K, (1.5f - Mathf.Abs(side)) * px * s.K);
                Particle p = CineKit.C(Fx.Spawn(Art.Sparks[0], at, px, Random.Range(0.2f, 0.26f), s.Top, 8), s.Cine);
                if (p == null)
                {
                    break;
                }
                p.Delay = Mathf.Abs(side) * 0.035f;
                p.Moving(new Vector2(side * 22f * px, Random.Range(58f, 70f) * px * s.K), 0f, 5f);
                p.FaceVel = true;
                p.Color(Color.white, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>Little lightning sparks crackling out of the body: three-segment zig-zags.</summary>
        private static void ZigZags(Spot s, int n, Color c)
        {
            if (!Kit.Visible(s.Body))
            {
                return;
            }
            float px = s.Px;
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(-0.25f, 0.25f)) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 side = new Vector2(-dir.y, dir.x);
                Vector2 p0 = s.Body + dir * 3f * px * s.K;
                float seg = Random.Range(2.5f, 3.5f) * px * s.K;
                float jit = 1.5f * px * s.K * (Random.value < 0.5f ? 1f : -1f);
                Vector2 p1 = p0 + dir * seg + side * jit;
                Vector2 p2 = p1 + dir * seg - side * jit;
                Vector2 p3 = p2 + dir * seg * 0.8f + side * jit * 0.5f;
                Seg(p0, p1, px, 0f, c, s);
                Seg(p1, p2, px, 0.025f, c, s);
                Seg(p2, p3, px, 0.05f, c, s);
            }
        }

        private static void Seg(Vector2 a, Vector2 b, float px, float delay, Color c, Spot s)
        {
            Particle p = CineKit.Line(a, b, px, 0.16f, 1, s.Top, 9, s.Cine, Color.white, c, Fx.WithA(c, 0f));
            if (p != null)
            {
                p.Delay = delay;
            }
        }

        /// <summary>Embers spiralling up around the unit.</summary>
        private static void Spiral(Spot s, float t, Color c, float radius, float rise)
        {
            float px = s.Px;
            for (int i = 0; i < 2; i++)
            {
                float a = t * 11f + i * Mathf.PI;
                Vector2 off = new Vector2(Mathf.Cos(a) * radius * s.K, Mathf.Sin(a) * radius * s.K * 0.35f);
                bool front = Mathf.Sin(a) < 0f;
                Particle p = CineKit.Ember(s.Ground + off + new Vector2(0f, Random.Range(0f, 3f) * px), px, Random.Range(0.5f, 0.75f),
                    new Vector2(-Mathf.Sin(a) * 1.4f * radius * s.K, rise * s.K), c, front ? s.Top : (s.Cine ? FxLayer.Cine : FxLayer.Behind), s.Cine, 0.8f);
                if (p == null)
                {
                    break;
                }
                if (front && s.Cine)
                {
                    p.R.sortingOrder = 1025;   // in front of the lit copy of the unit (Cine 20) and its flash (21)
                }
            }
        }

        /// <summary>The power gathering: dust and red sparks drawn in to the unit's feet and body.</summary>
        private static void Gather(Spot s, Color c)
        {
            if (!Kit.Visible(s.Body))
            {
                return;
            }
            float px = s.Px;
            Color dust = Fx.Lighten(Fx.SoilColor(s.Tile), 0.35f);
            int n = Fx.Count(8);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.6f) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = CineKit.C(Fx.Spawn(Art.Dust[0], s.Ground + dir * 14f * px * s.K, px, 0.3f, dir.y < 0f ? s.Top : s.Low, 5), s.Cine);
                p?.Moving(-dir * 40f * px * s.K).Sized(0.7f, 0.3f).Color(Fx.WithA(dust, 0f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
            }
            int m = Fx.Count(6);
            for (int i = 0; i < m; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float r = Random.Range(9f, 13f) * px * s.K;
                float life = Random.Range(0.24f, 0.3f);
                Particle p = CineKit.Ember(s.Body + dir * r, px, life, -dir * r / life, c, s.Top, s.Cine, 0f);
                p?.Color(Fx.WithA(c, 0f), c, Fx.WithA(Color.white, 0.9f));
            }
        }

        /// <summary>
        /// A power-up aura: flame tongues licking up around the unit (behind it, and at its sides in
        /// front), white at the root, its tier colour, gone in a third of a second.
        /// </summary>
        private static void AuraFlames(Spot s, Color c, Color pale, int n)
        {
            float px = s.Px;
            float tall = Mathf.Max(2f * px, (s.Head.y - s.Ground.y) * 0.85f);
            for (int i = 0; i < n; i++)
            {
                bool front = Random.value < 0.45f;
                float side = Random.value < 0.5f ? -1f : 1f;
                float x = front ? side * Random.Range(2.4f, 4f) : Random.Range(-3.5f, 3.5f);
                Vector2 at = s.Ground + new Vector2(x * px * s.K, Random.Range(0f, tall));
                FxLayer layer = front ? s.Top : (s.Cine ? FxLayer.Cine : FxLayer.Behind);
                Particle p = CineKit.C(Fx.Spawn(Art.Puffs[Random.Range(0, 3)], at, px, Random.Range(0.24f, 0.34f), layer, front ? 7 : 3), s.Cine);
                if (p == null)
                {
                    break;
                }
                if (s.Cine && front)
                {
                    p.R.sortingOrder = 1025;   // in front of the lit copy of the unit (Cine 20) and its flash (21)
                }
                p.Moving(new Vector2(-Mathf.Sign(x) * 5f * px, Random.Range(36f, 52f) * px * s.K), 0f, 1.5f);
                // small tongues in front (the unit stays readable), bigger ones behind it
                p.Sized(front ? 0.42f : 0.62f, front ? 0.15f : 0.2f);
                p.Color(Fx.WithA(Color.white, 0.85f), Fx.WithA(pale, 0.8f), Fx.WithA(c, 0.7f), Fx.WithA(Color.Lerp(c, Tiers.Crimson, 0.5f), 0f));
            }
        }

        /// <summary>
        /// The God's beam of light: a column from above the view to its feet (a white core in a gold
        /// shaft, in tiles, so it reads at any zoom), swelling in, shimmering, flaring white and wide
        /// with the shockwave at 'flareAt' seconds, then narrowing to nothing by 'life'.
        /// </summary>
        private static void Beam(Spot s, float top, float flareAt, float life)
        {
            if (!Kit.Visible(s.Body, 0.3f))
            {
                return;
            }
            float px = s.Px;
            Vector2 foot = s.Ground;
            float h = Mathf.Max(3f, top - foot.y);
            Vector2 mid = new Vector2(foot.x, foot.y + h * 0.5f);
            Color gold = Tiers.Color(Tier.God);
            Color pale = Tiers.LightColor(Tier.God);
            // in a full ascension above the veil, under the speed lines rising in it and the lit unit;
            // otherwise behind the unit
            FxLayer layer = s.Cine ? FxLayer.Cine : FxLayer.Behind;
            for (int i = 0; i < 2; i++)
            {
                bool core = i == 1;
                Particle p = CineKit.C(Fx.Spawn(Art.Pixel, mid, 1f, life, layer, core ? 1 : 0), s.Cine);
                if (p == null)
                {
                    return;
                }
                float w0 = (core ? 0.36f : 1.1f) * s.K;
                float phase = core ? 1.7f : 0f;
                p.SX = 0f;
                p.SY = h;
                p.Step = (q, dt) =>
                {
                    float t = q.Age;
                    float grow = Anim.OutCubic(Mathf.Clamp01(t / 0.12f));
                    float flare = t >= flareAt ? Mathf.Clamp01((t - flareAt) / 0.05f) : 0f;
                    float fade = t >= flareAt + 0.05f ? 1f - Anim.InQuad(Mathf.Clamp01((t - flareAt - 0.05f) / Mathf.Max(0.05f, life - flareAt - 0.05f))) : 1f;
                    float shimmer = 1f + 0.1f * Mathf.Sin(t * 64f + phase);
                    float w = w0 * grow * shimmer * (1f + 0.7f * flare) * fade;
                    // whole pixels wide (of the unit's grain): a crisp shaft, never a smear
                    q.SX = w < px * 0.5f ? 0f : Mathf.Max(px, Mathf.Round(w / px) * px);
                    q.Grad = null;
                    Color c = core ? Color.Lerp(pale, Color.white, 0.55f + 0.45f * flare) : Color.Lerp(gold, pale, flare);
                    q.R.color = Fx.WithA(c, (core ? 0.92f : 0.4f + 0.2f * flare) * Mathf.Clamp01(fade * 1.6f));
                };
            }
            // a pool of light where it meets the ground
            Particle pool = CineKit.Flash(foot, px, Mathf.RoundToInt(9f * s.K), flareAt + 0.2f, s.Low, 7, s.Cine,
                Fx.WithA(Color.white, 0.75f), Fx.WithA(pale, 0.55f), Fx.WithA(gold, 0.45f), Fx.WithA(gold, 0.35f), Fx.WithA(gold, 0f));
            if (pool != null)
            {
                pool.SY = Art.Ground;
            }
        }

        /// <summary>A pillar of vertical speed lines rising from the unit to 'top'.</summary>
        private static void Pillar(Spot s, float top, Color c, int n)
        {
            float px = s.Px;
            float bottom = s.Body.y;
            if (top <= bottom)
            {
                top = bottom + 3f * s.K;
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 at = new Vector2(s.Ground.x + Random.Range(-3.5f, 3.5f) * px * s.K, Random.Range(bottom, top));
                if (!Kit.Visible(at, 0.1f))
                {
                    continue;
                }
                Particle p = CineKit.C(Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, Random.Range(0.14f, 0.22f), s.Top, 2), s.Cine);
                if (p == null)
                {
                    break;
                }
                p.Moving(new Vector2(0f, Random.Range(70f, 110f) * px * s.K));
                p.Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
            }
        }

        private static float ViewTop(Spot s, float fallback)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null)
            {
                return s.Body.y + fallback;
            }
            return Mathf.Max(s.Body.y + 2f, cam.transform.position.y + cam.orthographicSize * 1.05f);
        }

        // -------------------------------------------------------------- beats --

        /// <summary>The ceremony's length by tier.</summary>
        internal static float Length(Tier t)
        {
            switch (t)
            {
                case Tier.Novice: return 0.5f;
                case Tier.Adept: return 0.6f;
                case Tier.Expert: return 0.8f;
                case Tier.Master: return 1.2f;
                case Tier.Grandmaster: return 1.8f;
                default: return 3.5f;
            }
        }

        /// <summary>
        /// The ceremony's effects and its mechanics at time 't' (the pose is the move's): one-shot cues
        /// (bits in 'cues') and continuous streams ('stream' is the emitter's countdown).
        /// </summary>
        internal static void Beat(Tier tier, Spot s, float t, float dt, ref int cues, ref float stream)
        {
            float px = s.Px;
            switch (tier)
            {
                case Tier.Novice:
                    if (Cue(ref cues, 0, t, 0f))
                    {
                        UnitFlash(s, Color.white, 0.05f);
                        Color dust = Fx.Lighten(Fx.SoilColor(s.Tile), 0.4f);
                        Ring(s, 0.4f, 0.25f, 0.8f, 2, 1, 0f, Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.5f), Fx.WithA(dust, 0f));
                        CineKit.DustRing(s.Ground, px, s.Tile, 6, 22f, 0.4f, s.K, s.Cine);
                    }
                    if (Cue(ref cues, 1, t, 0.1f))
                    {
                        UnitFlash(s, Color.white, 0.05f);
                    }
                    if (Cue(ref cues, 2, t, 0.12f))
                    {
                        Rising(s, 3, new Color(0.9f, 0.93f, 1f, 1f));
                    }
                    break;

                case Tier.Adept:
                    {
                        Color blue = Tiers.Color(Tier.Adept);
                        if (Cue(ref cues, 0, t, 0.05f))
                        {
                            UnitFlash(s, Color.Lerp(blue, Color.white, 0.3f), 0.08f);
                        }
                        if (Cue(ref cues, 1, t, 0.08f))
                        {
                            Ring(s, 0.4f, 0.3f, 1f, 2, 1, 0f, Color.white, Fx.WithA(blue, 0.9f), Fx.WithA(blue, 0f));
                        }
                        if (Cue(ref cues, 2, t, 0.22f))
                        {
                            CineKit.Glint(s.Head + new Vector2(1.5f * px, 0f), px, 0.24f, Tiers.LightColor(Tier.Adept), 1f, s.Top, s.Cine);
                        }
                        break;
                    }

                case Tier.Expert:
                    {
                        Color yellow = Tiers.Color(Tier.Expert);
                        if (Cue(ref cues, 0, t, 0.1f))
                        {
                            UnitFlash(s, Color.Lerp(yellow, Color.white, 0.25f), 0.08f);
                            Kit.Stop(s.Body, 0.05f);
                            Anim.Sound("event:/SFX/POWERS/Powerup", s.Tile);
                        }
                        if (Cue(ref cues, 1, t, 0.12f))
                        {
                            Ring(s, 0.45f, 0.3f, 1.5f, 2, 1, 0f, Color.white, Fx.WithA(yellow, 0.9f), Fx.WithA(yellow, 0f));
                            ZigZags(s, 6, yellow);
                        }
                        break;
                    }

                case Tier.Master:
                    {
                        Color orange = Tiers.Color(Tier.Master);
                        if (Cue(ref cues, 0, t, 0.15f))
                        {
                            CineKit.DustRing(s.Ground, px, s.Tile, 6, 30f, 0.45f, s.K, s.Cine);
                        }
                        if (t >= 0.15f && t < 0.78f)
                        {
                            stream -= dt;
                            if (stream <= 0f && Kit.Visible(s.Body))
                            {
                                stream = 0.03f;
                                Spiral(s, t, Color.Lerp(orange, Kit.Fire1, Random.value * 0.5f), 0.45f, 2.2f);
                            }
                        }
                        if (Cue(ref cues, 1, t, 0.5f))
                        {
                            UnitFlash(s, Tiers.LightColor(Tier.Master), 0.1f);
                            CineKit.Flash(s.Body, px, Mathf.RoundToInt(9f * s.K), 0.22f, s.Top, 28, s.Cine, Color.white, Fx.WithA(orange, 0.7f), Fx.WithA(orange, 0f));
                            Anim.Sound("event:/SFX/POWERS/Powerup", s.Tile);
                        }
                        if (Cue(ref cues, 2, t, 0.92f))
                        {
                            // the superhero landing: it scorches the ground it lands on
                            Ring(s, 0.5f, 0.4f, 2.5f, 3, 1, 0f, Color.white, Fx.WithA(orange, 0.85f), Fx.WithA(orange, 0f));
                            CineKit.DustRing(s.Ground, px, s.Tile, 12, 60f, 0.55f, s.K, s.Cine);
                            Kit.Crater(s.Ground, px, s.Tile, false, 4f, true);
                            Kit.Ravage(s.Ground, 1f, 1, s.A);
                            Kit.Shake(s.Ground, 0.15f, 0.004f, s.K);
                            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", s.Tile);
                        }
                        break;
                    }

                case Tier.Grandmaster:
                    {
                        Color red = Tiers.Color(Tier.Grandmaster);
                        Color pale = Tiers.LightColor(Tier.Grandmaster);
                        if (Cue(ref cues, 0, t, 0f))
                        {
                            if (Settings.Cinematics >= 1 && !Cinema.Playing && !Cinema.Frozen && Cinema.Rate >= 1f && Fx.OnScreen(s.Body, 0.05f))
                            {
                                Cinema.SlowMo(0.3f, 0.5f);
                            }
                            Anim.Sound("event:/SFX/POWERS/Inspiration", s.Tile);
                            Gather(s, red);
                        }
                        if (t >= 0.3f && t < 1.4f)
                        {
                            stream -= dt;
                            if (stream <= 0f && Kit.Visible(s.Body))
                            {
                                stream = 0.03f;
                                Pillar(s, s.Body.y + 6f * s.K, red, 3);
                                AuraFlames(s, red, pale, t < 1.15f ? 2 : 1);
                                if (Random.value < 0.6f)
                                {
                                    CineKit.Ember(s.Ground + new Vector2(Random.Range(-4f, 4f) * px * s.K, 0f), px, Random.Range(0.6f, 0.9f),
                                        new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(1.6f, 2.6f) * s.K), Color.Lerp(red, Kit.Fire1, Random.value * 0.4f), s.Top, s.Cine);
                                }
                            }
                        }
                        if (Cue(ref cues, 1, t, 0.35f))
                        {
                            // released: a red flash, two rings, and the ground under it cracks and glows,
                            // scorched, pebbles thrown up (a Master's landing only cracks it)
                            UnitFlash(s, pale, 0.1f);
                            CineKit.Flash(s.Body, px, Mathf.RoundToInt(12f * s.K), 0.24f, s.Top, 28, s.Cine, Color.white, Fx.WithA(red, 0.75f), Fx.WithA(red, 0f));
                            Ring(s, 0.6f, 0.4f, 4f, 3, 1, 0f, Color.white, Fx.WithA(red, 0.85f), Fx.WithA(red, 0f));
                            Ring(s, 0.7f, 0.3f, 2.6f, 1, 1, 0.1f, Fx.WithA(pale, 0.8f), Fx.WithA(red, 0f));
                            CineKit.DustRing(s.Ground, px, s.Tile, 12, 60f, 0.55f, s.K, s.Cine);
                            Kit.Crater(s.Ground, px, s.Tile, true, 6f, false, red);
                            Kit.Ravage(s.Ground, Mathf.Clamp(1.5f * s.K, 1f, 3f), 1, s.A);
                            Color soil = Fx.SoilColor(s.Tile);
                            CineKit.Debris(s.Ground, 0f, px, 7, 1.4f * s.K, 4.5f * s.K, s.Cine, soil, Fx.Darken(soil, 0.3f), Fx.TopColor(s.Tile));
                            Kit.Shake(s.Ground, 0.2f, 0.005f, s.K);
                        }
                        if (Cue(ref cues, 2, t, 1.2f))
                        {
                            CineKit.Glint(s.Head + new Vector2(1.5f * px, 0f), px, 0.26f, pale, 1.1f, s.Top, s.Cine);
                        }
                        if (Cue(ref cues, 3, t, 1.6f))
                        {
                            CineKit.LandingPuff(s.Ground, px, s.Tile, s.Cine);
                        }
                        break;
                    }

                default:
                    Ascension(s, t, dt, ref cues, ref stream);
                    break;
            }
        }

        private static readonly Tier[] Ladder = { Tier.Novice, Tier.Adept, Tier.Expert, Tier.Master, Tier.Grandmaster, Tier.God };

        /// <summary>The God's ascension: the six tiers close in on it, a gold pillar, the shockwave.</summary>
        private static void Ascension(Spot s, float t, float dt, ref int cues, ref float stream)
        {
            float px = s.Px;
            Color gold = Tiers.Color(Tier.God);
            Color pale = Tiers.LightColor(Tier.God);
            // 0.20-1.10: six rings, one per tier, contracting into it 0.14 s apart, a flash as each arrives
            for (int i = 0; i < 6; i++)
            {
                Color c = i == 0 ? Color.white : Tiers.Color(Ladder[i]);
                if (Cue(ref cues, i, t, 0.2f + 0.14f * i))
                {
                    Ring(s, 0.2f, 4f, 0.15f, 1, 2, 0f, Fx.WithA(c, 0f), Fx.WithA(c, 0.9f), Fx.WithA(Color.Lerp(c, Color.white, 0.5f), 1f));
                }
                if (Cue(ref cues, 6 + i, t, 0.4f + 0.14f * i))
                {
                    UnitFlash(s, Color.Lerp(c, Color.white, 0.2f), 0.07f);
                }
            }
            // 1.10: silence, a gold ting at the eye
            if (Cue(ref cues, 12, t, 1.1f))
            {
                CineKit.Glint(s.Head + new Vector2(1.5f * px, -0.8f * px), px, 0.26f, gold, 1.1f, s.Top, s.Cine);
            }
            // 1.30: it stands; the beam of light from the sky, the pillar of speed lines in it, the title
            if (Cue(ref cues, 13, t, 1.3f))
            {
                Anim.Sound("event:/SFX/POWERS/Blessing", s.Tile);
                UnitFlash(s, pale, 0.1f);
                Beam(s, ViewTop(s, 8f) + 3f, 0.7f, 1.05f);
                if (Settings.TitleCards && (s.Cine || !Cinema.Playing) && Fx.OnScreen(s.Body, 0.1f))
                {
                    string title = Locale.Get("sakuga_title_ascended");
                    Cinema.Title(title, gold, 2.6f, s.Body, CineKit.TitleSize(title, 1f));
                }
            }
            if (t >= 1.3f && t < 2.6f)
            {
                stream -= dt;
                if (stream <= 0f && Kit.Visible(s.Body))
                {
                    stream = 0.025f;
                    if (t < 2.05f)
                    {
                        Pillar(s, ViewTop(s, 8f), Color.Lerp(gold, pale, Random.value * 0.5f), 3);
                    }
                    Spiral(s, t, Random.value < 0.7f ? gold : pale, 0.5f, 2.6f);
                }
            }
            // 2.00: the shockwave
            if (Cue(ref cues, 14, t, 2f))
            {
                Ring(s, 0.7f, 0.5f, 6f, 3, 1, 0f, Color.white, Fx.WithA(gold, 0.85f), Fx.WithA(gold, 0f));
                Ring(s, 0.6f, 0.4f, 3.8f, 1, 1, 0.08f, Fx.WithA(pale, 0.8f), Fx.WithA(pale, 0f));
                CineKit.DustRing(s.Ground, px, s.Tile, 16, 90f, 0.7f, s.K, s.Cine);
                CineKit.Flash(s.Body, px, Mathf.RoundToInt(11f * s.K), 0.2f, s.Top, 28, s.Cine, Color.white, Fx.WithA(gold, 0.7f), Fx.WithA(gold, 0f));
                Kit.Shake(s.Ground, 0.25f, 0.006f, s.K);
                Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", s.Tile);
                Actor a = s.A;
                if (a != null && a.isAlive())
                {
                    foreach (Actor u in Combat.FoesAround(a, s.Ground, 3f * s.K))
                    {
                        Combat.Launch(u, s.Ground, 2f, 0.8f, Random.value < 0.5f ? -600f : 600f);
                    }
                    a.setHealth(a.getMaxHealth());
                }
                // the ground under it gives: the turf torn off and blasted (the game's explosion
                // pixels), glowing gold fissures, rocks thrown up; the seared ring further out
                Kit.Ravage(s.Ground, Mathf.Clamp(2f * s.K, 1.5f, 4f), 2, a);
                Kit.Crater(s.Ground, px, s.Tile, true, 10f, false, gold);
                Color soil = Fx.SoilColor(s.Tile);
                CineKit.Debris(s.Ground, 0f, px, 10, 2.2f * s.K, 5.5f * s.K, s.Cine, soil, Fx.Darken(soil, 0.3f), Fx.TopColor(s.Tile));
                ScorchRing(s);
                Kit.Stop(s.Ground, 0.15f);
            }
        }

        /// <summary>
        /// The shockwave sears a ring into the ground around the new God, where the pale ring stops
        /// (3.8 tiles times its size, flattened like every ground ring): scorch only (Kit.Ravage
        /// force 1; only the ground right under the God is torn open).
        /// </summary>
        private static void ScorchRing(Spot s)
        {
            float r = Mathf.Clamp(3.8f * s.K, 3f, 8f);
            float ry = r * Art.Ground;
            float around = 2f * Mathf.PI * Mathf.Sqrt((r * r + ry * ry) * 0.5f);
            int n = Mathf.Clamp(Mathf.RoundToInt(around / 1.3f), 12, 32);
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i / (float)n * Mathf.PI * 2f;
                float k = 1f + Random.Range(-0.08f, 0.08f);
                Kit.Ravage(s.Ground + new Vector2(Mathf.Cos(a) * r * k, Mathf.Sin(a) * ry * k), 1f, 1, s.A);
            }
        }

        /// <summary>A one-shot cue: fires once when 't' passes 'at'.</summary>
        private static bool Cue(ref int cues, int bit, float t, float at)
        {
            int m = 1 << bit;
            if (t < at || (cues & m) != 0)
            {
                return false;
            }
            cues |= m;
            return true;
        }

        // ------------------------------------------------------------ rites --

        /// <summary>
        /// Dev: stacks every tier below 'top' onto a unit (as a broken save or another mod would) and lets
        /// the mod tidy it up: the extra tiers close in on it as rings.
        /// </summary>
        internal static string DevStack(Actor a, Tier top)
        {
            if (a == null || !a.isAlive() || top == Tier.None)
            {
                return "no unit";
            }
            // dev only: look at it (the rings close in within half a second)
            Spot s = Of(a);
            World.world?.move_camera?.focusOn(new Vector3(s.Body.x, s.Body.y + 1f, 0f));
            Dev.Watch(s.Body + new Vector2(0f, 1f));
            for (int i = 1; i <= (int)top; i++)
            {
                ActorTrait t = Tiers.Trait((Tier)i);
                if (t != null)
                {
                    a.traits.Add(t);   // straight into the set, past the opposites check
                }
            }
            a.clearTraitCache();
            a.setStatsDirty();
            Fighters.Touch(a);
            return "stacked tiers 1.." + (int)top + " on " + a.getName() + ": now " + Tiers.Of(a);
        }

        /// <summary>A tier lost with no other given in the same frame: it was a demotion.</summary>
        private static void FlushLost()
        {
            Actor a = _lost;
            long id = _lostId;
            _lost = null;
            _lostId = -1;
            if (a != null && a.Sid() == id && a.isAlive())
            {
                Revoke(a, id, Tiers.Of(a));
                Demotion(a);
            }
        }

        /// <summary>Effects-only ceremonies (in game time: they pause with the game and its hit-stops).</summary>
        internal static void Frame()
        {
            if (_lostId >= 0 && Time.frameCount != _lostFrame)
            {
                FlushLost();
            }
            if (World.world == null)
            {
                return;
            }
            float dt = Clock.Dt;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (i >= _pending.Count)
                {
                    continue;
                }
                Pending p = _pending[i];
                Actor a = p.A;
                if (a == null || a.Sid() != p.Id || !a.isAlive())
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (TryPending(p))
                {
                    _pending.Remove(p);
                    continue;
                }
                if (p.Why == "asleep")
                {
                    Wake(a);   // dozed off again before it could stand up
                }
                p.Left -= dt;
                if (p.Left <= 0f && !InKaijuKiller(a))
                {
                    _pending.Remove(p);
                    string why = Why(a, out _);
                    StartRite(a, p.Tier, why);
                }
            }
            if (_rites.Count == 0)
            {
                return;
            }
            for (int i = _rites.Count - 1; i >= 0; i--)
            {
                if (i >= _rites.Count)
                {
                    continue;   // (a shockwave ended a move and a ceremony move took over its unit's rite)
                }
                Rite r = _rites[i];
                Actor a = r.A;
                if (a == null || a.Sid() != r.Id || !a.isAlive() || r.T >= Length(r.Tier))
                {
                    _rites.RemoveAt(i);
                    Live = Mathf.Max(0, Live - 1);
                    if (a != null && a.Sid() == r.Id && a.isAlive() && r.Tier == Tier.God)
                    {
                        a.setHealth(a.getMaxHealth());   // stats have caught up with the new tier by now
                    }
                    continue;
                }
                if (dt <= 0f)
                {
                    continue;
                }
                r.T += dt;
                try
                {
                    Beat(r.Tier, Of(a), r.T, dt, ref r.Cues, ref r.Stream);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] rite: " + e);
                    r.T = 99f;
                }
            }
        }
    }

    /// <summary>
    /// A promotion ceremony played by the unit itself: its pose through the beat (see Ceremonies for
    /// the effects). Only the Combat God's ascension is long, and only it stops the world:
    ///
    ///   0.00       the world freezes, a 55% dark veil (full: on screen, a sensible speed, no other
    ///              cinematic); the unit kneels (squashed to 70%, bowed 10 degrees)
    ///   0.20-1.10  six rings close in on it, 0.14 s apart (white, blue, yellow, orange, red, gold),
    ///              its silhouette flashing each colour as each arrives
    ///   1.10       silence; a gold "ting" at the eye
    ///   1.30       it stands and lifts a tile: a beam of light from above the screen to its feet
    ///              (a white core in a gold shaft, a pool of light on the ground), speed lines
    ///              rising in it, embers spiralling; title card "A COMBAT GOD HAS ASCENDED"
    ///   2.00       the beam flares white and wide and narrows away; a 6-tile gold shockwave:
    ///              enemies pushed 2 tiles, the turf under it torn off and blasted (Kit.Ravage
    ///              force 2, 2 tiles) with glowing gold fissures and rocks thrown up, a ring
    ///              seared into the ground further out (force 1), a 0.15 hit-stop, full health;
    ///              the world moves again
    ///   2.00-3.50  the veil fades as it floats down and lands
    ///
    /// In light mode (off screen, x5+ speed, cinematics light) the same without the freeze and veil.
    /// </summary>
    internal sealed class Ceremony : Move
    {
        private readonly Tier _tier;
        private bool _full;
        private int _cues;
        private float _stream;
        private float _hold;
        private bool _done, _cancelled;
        private Particle _flash, _lit;
        private float _flashLeft;
        private Color _flashColor = Color.white;

        private Ceremony(Actor a, Tier tier) : base(a)
        {
            _tier = tier;
        }

        public override bool Cinematic => _full;

        public override bool Intangible => _tier == Tier.God;

        /// <summary>The tier it celebrates.</summary>
        internal Tier Tier => _tier;

        /// <summary>The unit lost that tier (a demotion): it stops at once, standing, the world let go, no full health.</summary>
        internal void Cancel()
        {
            if (Finished)
            {
                return;
            }
            _cancelled = true;
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Finished = true;
        }

        public static bool Begin(Actor a, Tier tier, string waited = null)
        {
            if (!Moves.CanPerform(a))
            {
                return false;
            }
            Ceremony m = new Ceremony(a, tier);
            if (tier == Tier.God && Cinema.FullAt(a.current_position) && Cinema.TryBegin(m, 2f))
            {
                m._full = true;
            }
            if (!Moves.Start(m))
            {
                if (m._full)
                {
                    Cinema.End(m);
                }
                return false;
            }
            Ceremonies.Live++;
            Ceremonies.DropRites(m.Id, Tier.None);   // an effects-only one still playing for an older tier
            if (m._full)
            {
                Cinema.Hold(m);
                Cinema.Dim(0.55f, Color.black, 0.15f);
                m._lit = CineKit.Copy(m, FxLayer.Cine, 20, false);
            }
            Dev.Watch(m.BodyPos() + new Vector2(0f, 1f));
            if (Dev.On) Dev.Trace("ceremony " + tier + " for " + a.getName() + (m._full ? " (full)" : "") + (waited != null ? " after waiting (" + waited + ")" : ""));
            return true;
        }

        private Ceremonies.Spot Spot()
        {
            return new Ceremonies.Spot
            {
                A = A,
                M = this,
                Ground = Pose.Ground,
                Body = BodyPos(),
                Head = HeadPos(),
                Px = Px,
                K = K,
                Cine = _full
            };
        }

        /// <summary>A silhouette flash over the whole unit.</summary>
        internal bool FlashUnit(Color c, float seconds)
        {
            if (_flash == null || !_flash.Alive)
            {
                _flash = CineKit.Copy(this, _full ? FxLayer.Cine : FxLayer.Over, _full ? 21 : 1, true);
            }
            if (_flash == null)
            {
                return false;
            }
            _flashColor = c;
            _flashLeft = seconds;
            return true;
        }

        public override void Tick(float dt)
        {
            if (_cancelled)
            {
                return;   // (ends this frame: no more beats)
            }
            if (_flashLeft > -0.3f)
            {
                _flashLeft -= dt;
            }
            if (_hold > 0f)
            {
                _hold -= dt;
                // (the seared ground shows on the shockwave's frozen frame: the framework draws it)
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            Pose.Nudge = Vector2.zero;
            float t = T;
            switch (_tier)
            {
                case Tier.Novice:
                    PoseNovice(t);
                    break;
                case Tier.Adept:
                    PoseAdept(t);
                    break;
                case Tier.Expert:
                    PoseExpert(t);
                    break;
                case Tier.Master:
                    PoseMaster(t);
                    break;
                case Tier.Grandmaster:
                    PoseGrandmaster(t);
                    break;
                default:
                    PoseGod(t);
                    break;
            }
            int before = _cues;
            Ceremonies.Beat(_tier, Spot(), t, dt, ref _cues, ref _stream);
            if (_tier == Tier.God && (before & (1 << 14)) == 0 && (_cues & (1 << 14)) != 0)
            {
                // the shockwave: the world moves again, the veil starts to lift
                _hold = 0.15f;
                if (_full)
                {
                    Cinema.Unhold(this);
                    Cinema.Undim(1.5f);
                }
            }
            if (t >= Ceremonies.Length(_tier))
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                Pose.Height = 0f;
                Finished = true;
            }
        }

        // -------------------------------------------------------------- poses --

        private void PoseNovice(float t)
        {
            // a crouch, a pop up onto its toes, a wobble back
            if (t < 0.08f)
            {
                float c = Anim.OutQuad(t / 0.08f);
                Pose.SX = 1f + 0.1f * c;
                Pose.SY = 1f - 0.12f * c;
                Pose.Height = 0f;
            }
            else if (t < 0.2f)
            {
                float k = Anim.OutQuad((t - 0.08f) / 0.12f);
                Pose.SX = Mathf.Lerp(1.1f, 0.9f, k);
                Pose.SY = Mathf.Lerp(0.88f, 1.14f, k);
                Pose.Height = 0.12f * K * Mathf.Sin(Mathf.PI * (t - 0.08f) / 0.12f);
            }
            else
            {
                float amp = Anim.Spring(t - 0.2f, 8f, 18f);
                Pose.SX = 1f - 0.1f * amp;
                Pose.SY = 1f + 0.14f * amp;
                Pose.Height = 0f;
            }
        }

        private void PoseAdept(float t)
        {
            // it straightens and leans back, chin up, then settles
            if (t < 0.06f)
            {
                float c = Anim.OutQuad(t / 0.06f);
                Pose.SX = 1f + 0.08f * c;
                Pose.SY = 1f - 0.1f * c;
                Pose.Rot = 0f;
            }
            else
            {
                float k = Anim.OutCubic(Mathf.Clamp01((t - 0.06f) / 0.14f));
                float back = 1f - Anim.Smooth(0.3f, 0.6f, t);
                float amp = Anim.Spring(t - 0.06f, 7f, 16f);
                Pose.SX = 1f - 0.08f * amp;
                Pose.SY = 1f + 0.12f * amp;
                Pose.Rot = -9f * k * back;
            }
            Pose.Height = 0f;
        }

        private void PoseExpert(float t)
        {
            // a trembling crouch, then it flexes up with the flash
            if (t < 0.1f)
            {
                float c = Anim.OutCubic(t / 0.1f);
                Pose.SX = 1f + 0.12f * c;
                Pose.SY = 1f - 0.15f * c;
                Pose.Rot = 5f * c;
                Pose.Nudge = t > 0.04f ? new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            else
            {
                float k = Anim.OutQuad(Mathf.Clamp01((t - 0.1f) / 0.1f));
                float amp = Anim.Spring(t - 0.1f, 6f, 15f);
                Pose.SX = 1f - 0.12f * amp * k;
                Pose.SY = 1f + 0.18f * amp * k;
                Pose.Rot = Mathf.Lerp(5f, -7f, k) * (1f - Anim.Smooth(0.4f, 0.75f, t));
            }
            Pose.Height = 0f;
        }

        private void PoseMaster(float t)
        {
            float lift = 1f * K;
            if (t < 0.15f)
            {
                // gathers itself
                float c = Anim.OutQuad(t / 0.15f);
                Pose.SX = 1f + 0.12f * c;
                Pose.SY = 1f - 0.16f * c;
                Pose.Height = 0f;
                Pose.Rot = 0f;
            }
            else if (t < 0.6f)
            {
                // rises a tile inside the spiral
                float k = Anim.OutCubic((t - 0.15f) / 0.45f);
                Pose.Height = lift * k;
                Pose.SX = Mathf.Lerp(0.86f, 1f, k);
                Pose.SY = Mathf.Lerp(1.2f, 1.04f, k);
                Pose.Rot = 0f;
            }
            else if (t < 0.78f)
            {
                // hangs there, arms of light around it
                Pose.Height = lift + 0.05f * K * Mathf.Sin((t - 0.6f) * 20f);
                Pose.SX = 1f;
                Pose.SY = 1.04f;
            }
            else if (t < 0.92f)
            {
                // drops
                float k = (t - 0.78f) / 0.14f;
                Pose.Height = lift * (1f - k * k);
                Pose.SX = 0.88f;
                Pose.SY = 1.2f;
                Pose.Rot = 6f * k;
            }
            else
            {
                // the superhero landing: one knee, a fist on the ground, then up
                float b = t - 0.92f;
                Pose.Height = 0f;
                float hold = 1f - Anim.Smooth(0.08f, 0.26f, b);
                float amp = Anim.Spring(Mathf.Max(0f, b - 0.08f), 7f, 15f);
                Pose.SX = Mathf.Lerp(1f + 0.08f * amp, 1.3f, hold);
                Pose.SY = Mathf.Lerp(1f - 0.1f * amp, 0.66f, hold);
                Pose.Rot = 16f * hold;
            }
        }

        private void PoseGrandmaster(float t)
        {
            if (t < 0.3f)
            {
                // the power stance, shaking with it
                float c = Anim.OutCubic(t / 0.3f);
                Pose.SX = 1f + 0.12f * c;
                Pose.SY = 1f - 0.16f * c;
                Pose.Rot = 4f * c;
                Pose.Height = 0f;
                Pose.Nudge = t > 0.1f ? new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            }
            else if (t < 1.35f)
            {
                // released: stands tall, lifted a little off the ground, in the pillar
                float k = Anim.OutCubic(Mathf.Clamp01((t - 0.3f) / 0.18f));
                float amp = Anim.Spring(t - 0.3f, 5f, 13f);
                Pose.SX = 1f - 0.1f * amp;
                Pose.SY = 1.06f + 0.14f * amp;
                Pose.Rot = Mathf.Lerp(4f, -6f, k);
                Pose.Height = 0.3f * K * k + 0.04f * K * Mathf.Sin(t * 9f);
            }
            else
            {
                float k = Mathf.Clamp01((t - 1.35f) / 0.25f);
                Pose.Height = Mathf.Lerp(0.3f * K, 0f, k * k);
                float amp = k >= 1f ? Anim.Spring(t - 1.6f, 9f, 18f) : 0f;
                Pose.SX = 1f + 0.12f * amp;
                Pose.SY = Mathf.Lerp(1.06f, 1f, k) - 0.14f * amp;
                Pose.Rot = Mathf.Lerp(-6f, 0f, k);
            }
        }

        private void PoseGod(float t)
        {
            if (t < 1.3f)
            {
                // kneels: squashed to 70%, bowed 10 degrees; a pulse as each ring arrives
                float k = Anim.OutCubic(Mathf.Clamp01(t / 0.15f));
                float pulse = 0f;
                for (int i = 0; i < 6; i++)
                {
                    float at = 0.4f + 0.14f * i;
                    if (t >= at && t < at + 0.1f)
                    {
                        pulse = Mathf.Sin(Mathf.PI * (t - at) / 0.1f);
                    }
                }
                Pose.SX = Mathf.Lerp(1f, 1.15f, k) - 0.04f * pulse;
                Pose.SY = Mathf.Lerp(1f, 0.7f, k) + 0.06f * pulse;
                Pose.Rot = 10f * k;
                Pose.Height = 0f;
            }
            else if (t < 2f)
            {
                // stands and lifts a tile
                float k = Anim.OutCubic(Mathf.Clamp01((t - 1.3f) / 0.5f));
                float s = Anim.OutQuad(Mathf.Clamp01((t - 1.3f) / 0.2f));
                Pose.SX = Mathf.Lerp(1.15f, 0.94f, s) + (1f - 0.94f) * k;
                Pose.SY = Mathf.Lerp(0.7f, 1.12f, s) - 0.08f * k;
                Pose.Rot = Mathf.Lerp(10f, -4f, s);
                Pose.Height = 1f * K * k + 0.04f * K * Mathf.Sin(t * 7f);
            }
            else
            {
                // floats down through the fading veil, lands softly
                float k = Mathf.Clamp01((t - 2.15f) / 1.15f);
                float e = Anim.InOutSine(k);
                Pose.Height = Mathf.Lerp(1f * K, 0f, e);
                Pose.Rot = Mathf.Lerp(-4f, 0f, e);
                if (k < 1f)
                {
                    Pose.SX = 1f;
                    Pose.SY = 1.04f;
                }
                else
                {
                    if (!_done)
                    {
                        _done = true;
                        CineKit.LandingPuff(Pose.Ground, Px, A.current_tile, _full);
                    }
                    float amp = Anim.Spring(t - 3.3f, 9f, 18f);
                    Pose.SX = 1f + 0.12f * amp;
                    Pose.SY = 1f - 0.14f * amp;
                }
                if (_lit != null && t > 3.4f)
                {
                    // the veil is gone: the unit is drawn by the game again (the copy faded out over it)
                    _lit.Kill();
                    _lit = null;
                }
            }
        }

        // ------------------------------------------------------ glued effects --

        public override void Frame()
        {
            CineKit.StandStill(this);
            if (_lit != null && _lit.Alive)
            {
                // the lit copy above the veil hands over to the real sprite as the veil lifts
                Fx.Glue(_lit, this, Fx.WithA(Color.white, 1f - Anim.Smooth(2.75f, 3.35f, T)));
            }
            if (_flash != null && _flash.Alive)
            {
                // full while it lasts, then gone in a couple of frames
                float a = _flashLeft > 0f ? 0.9f : Mathf.Clamp01(1f + _flashLeft / 0.08f) * 0.9f;
                Fx.Glue(_flash, this, Fx.WithA(_flashColor, a));
            }
        }

        private void Cleanup()
        {
            if (_lit != null)
            {
                _lit.Kill();
                _lit = null;
            }
            if (_flash != null)
            {
                _flash.Kill();
                _flash = null;
            }
            Cinema.End(this);
        }

        public override void Abort()
        {
            Cleanup();
        }

        public override void OnEnd(bool aborted)
        {
            Cleanup();
            Ceremonies.Live = Mathf.Max(0, Ceremonies.Live - 1);
            if (_tier == Tier.God && !aborted && !_cancelled && A.isAlive())
            {
                A.setHealth(A.getMaxHealth());
            }
        }
    }

    /// <summary>Runs the effects-only ceremonies every frame.</summary>
    [HarmonyLib.HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    internal static class CeremonyFramePatch
    {
        private static void Postfix()
        {
            try
            {
                Ceremonies.Frame();
            }
            catch (Exception e)
            {
                SakugaMod.Report("ceremonies", e);
            }
        }
    }
}
