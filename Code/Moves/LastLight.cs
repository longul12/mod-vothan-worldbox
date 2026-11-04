using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// LAST LIGHT (god ability: the crisis). The blow that should have ended the God never lands.
    /// The world stops and darkens; the God, grey and kneeling, has two heartbeats, opens its eyes
    /// with a gold ting and rises into a pillar of gold light. Time resumes with a burst that
    /// throws its enemies back, and the God fights on Awakened.
    ///
    /// Beat sheet (seconds; full mode: 1.4 s of frozen world):
    ///
    ///   FALL     0.00       the lethal or crisis hit is cancelled (HP stays); the world freezes,
    ///                       a 60% dark veil; the God, redrawn above it drained to ash grey (pale
    ///                       on the dark: the one thing still lit), kneels (squash to 70%, 10
    ///                       degrees forward)
    ///   BEAT     0.25/0.50  two heartbeats: a white flash of the sprite, a jolt, a small dust ring
    ///                       and a faint ring pulse
    ///   TING     0.70       a gold ting at the eye pixel; focus lines converge
    ///   RISE     0.85-1.40  it stands (unsquash, lifts half a tile), the grey burns away to gold:
    ///                       a gold pillar of speed lines to the top of the screen, spiralling
    ///                       embers, a 2-tile gold flash disk behind it
    ///   BURST    1.40       the world resumes; a 6-tile gold ring (7.5 in a god war: its equal
    ///                       stands against it) sweeps out over 0.4 s: every enemy it reaches
    ///                       takes 30 + 2.6xD, is thrown 2-4 tiles and stunned 1.5 s times its
    ///                       tier's Linger (Titans only stagger); the ground it crosses is SCORCHED AND
    ///                       FLATTENED: the whole disc burned, and a force-3 shockwave of craters
    ///                       (rings at 2.4, 3.9 and 5.4 tiles, the inner one without flames) that
    ///                       breaks the trees and buildings in its way, timed to the ring; HP
    ///                       climbs to 60% over a second; AWAKENED 12 s
    ///   SETTLE   1.40-1.82  it drops back to the ground and springs out of the landing, and
    ///                       chains straight on (Scattered, or Down on a lone one)
    ///
    /// Awakened (Godhood): techniques cost no mana, cooldowns are reset then halved, +25% damage
    /// (Fighters.DamageMult), gold afterimages while it moves, an ember aura, a gold puff at the end.
    /// (+20 armor was asked for "if a suitable vanilla status exists": none does without side
    /// effects - shield is +90 and a bubble, uprooting roots the unit - so it is left out.)
    ///
    /// Light mode (off screen, a cinematic already playing, too soon after the last one): the
    /// same beats at world speed, no freeze or veil; a dark overlay on the sprite does the tint.
    /// </summary>
    internal sealed class LastLight : Move
    {
        private const float TBeat1 = 0.25f;
        private const float TBeat2 = 0.50f;
        private const float TTing = 0.70f;
        private const float TRise = 0.85f;
        private const float TBurst = 1.40f;
        private const float TWave = 0.40f;
        // an awakened God lands crisply and goes (it used to settle to 1.95)
        private const float TLand = 1.62f;
        private const float TEnd = 1.82f;   // (just after the ring reaches its edge at 1.80)
        public const float Radius = 6f;
        /// <summary>A god war: the ring, its craters and its light reach this much farther.</summary>
        private const float WarScale = 1.25f;
        public const float AwakenedSeconds = 12f;
        /// <summary>The shockwave's rings of craters (tiles from the God) and how far apart their craters are.</summary>
        private static readonly float[] RuinRings = { 2.4f, 3.9f, 5.4f };
        private const float RuinStep = 2.6f;

        private static readonly Color White = Color.white;
        private static readonly Color Gold = GodFx.Gold;
        private static readonly Color GoldLight = GodFx.GoldLight;
        private static readonly Color Hot = GodFx.WhiteHot;
        /// <summary>The kneeling God's tint above the veil: drained, but the brightest thing in the dark.</summary>
        private static readonly Color Grey = new Color(0.62f, 0.62f, 0.68f, 1f);
        /// <summary>The ash wash over it (a flat overlay): colour gone, shape clear.</summary>
        private static readonly Color Ash = new Color(0.8f, 0.8f, 0.86f, 1f);

        private const int BBeat1 = 1, BBeat2 = 2, BTing = 4, BRise = 8, BBurst = 16;

        private struct Wave
        {
            public Actor U;
            public long Id;
            public float At;
            public float D;
        }

        /// <summary>One crater of the shockwave, opened when the ring reaches it.</summary>
        private struct Ruin
        {
            public Vector2 P;
            public float At;
            public int Force;
        }

        private readonly GodCast _cast;
        private readonly bool _full;
        private bool _cine;
        private readonly Vector2 _at;
        private readonly float _k;
        private int _beats;
        private float _lines, _embers, _ghost;
        private float _lastBeat = -1f;
        private readonly List<Wave> _wave = new List<Wave>();
        private readonly List<Ruin> _ruin = new List<Ruin>();
        private readonly List<Actor> _allies = new List<Actor>();
        private readonly List<Actor> _near = new List<Actor>();
        private readonly HashSet<long> _answered = new HashSet<long>();
        /// <summary>Enemies the ring threw (the chain it leaves), and the last one.</summary>
        private int _thrown;
        private Actor _lastThrown;

        // full mode: the God redrawn above the veil (its own colours, tinted) with a flat overlay on top;
        // light mode: only the overlay, on the unit itself
        private Particle _clone, _shade, _flash;

        private LastLight(Actor a, GodCast cast) : base(a)
        {
            _cast = cast;
            _full = cast.Full;
            _cine = _full;
            _at = a.current_position;
            // a god war (its equal stands against it): the awakening is a quarter again as wide
            _k = Mathf.Clamp(K, 0.8f, 1.5f) * (Godhood.AtWar(a) ? WarScale : 1f);
        }

        public override bool Cinematic => _cine;

        public static bool Start(Actor a, GodCast cast, Technique tech)
        {
            if (!CanRise(a))
            {
                return false;
            }
            LastLight m = new LastLight(a, cast) { Tech = tech, Level = 1 };
            if (!Moves.Start(m))
            {
                return false;
            }
            if (a.hasStatusStunned())
            {
                a.finishStatusEffect("stunned");   // it rises regardless
            }
            m.Open();
            return true;
        }

        /// <summary>
        /// Could this unit ever play Last Light (an ordinary sprite unit, not possessed)? If not, the
        /// killing blow is not cancelled at all.
        /// </summary>
        public static bool CouldRise(Actor a)
        {
            if (a == null || a.asset == null)
            {
                return false;
            }
            ActorAsset s = a.asset;
            if (!s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render || s.special
                || s.skip_fight_logic || !s.can_be_moved_by_powers)
            {
                return false;
            }
            return !ControllableUnit.isControllingUnit(a);
        }

        /// <summary>
        /// Can it rise right now? Like Moves.CanPerform, but a stunned God, or one standing in
        /// shallow water, still rises; one in the air (thrown) or held by a move waits for it to end.
        /// </summary>
        public static bool CanRise(Actor a)
        {
            // (a flow strike or a blur dash is no obstacle: the rise takes it over at once)
            if (!CouldRise(a) || !a.isAlive() || Moves.Locked(a) || a.position_height > 0.05f || a.under_forces
                || a.isInsideSomething() || a.isInMagnet() || a.isFlying() || a.is_immovable)
            {
                return false;
            }
            return true;
        }

        private void Open()
        {
            if (_full)
            {
                Cinema.Hold(_cast);
                Cinema.Dim(0.6f, Color.black, 0.12f);
            }
            Sprite s = Fx.UnitSprite(A);
            if (s != null)
            {
                if (_full)
                {
                    _clone = Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Cine, 25);
                    if (_clone != null)
                    {
                        _clone.Held = true;
                        _clone.UseEuler = true;
                        Fx.Glue(_clone, this, Grey);
                    }
                }
                if (Fx.HasSilhouette)
                {
                    _shade = Overlay(s, 26);
                    _flash = Overlay(s, 27);
                }
            }
            Anim.Sound("event:/SFX/HIT/HitFlesh", A.current_tile);
            Dev.Watch(_at + new Vector2(0f, 1.6f));
        }

        private Particle Overlay(Sprite s, int order)
        {
            Particle p = Fx.Spawn(s, DrawPos(), 1f, 1f, _full ? FxLayer.Cine : FxLayer.Over, _full ? order : order - 25, true);
            if (p == null)
            {
                return null;
            }
            p.Held = true;
            p.UseEuler = true;
            p.Cine = _full;
            Fx.Glue(p, this, Fx.WithA(White, 0f));
            return p;
        }

        // ------------------------------------------------------------ helpers --

        private bool Beat(int b)
        {
            if ((_beats & b) != 0)
            {
                return false;
            }
            _beats |= b;
            return true;
        }

        private bool Visible => Kit.Visible(Pose.Ground, 0.4f);

        private FxLayer Hi => _full ? FxLayer.Cine : FxLayer.Top;

        private FxLayer Back => _full ? FxLayer.Cine : FxLayer.Behind;

        private int N(int n)
        {
            return Fx.Count(_full ? n : Mathf.Max(1, (n * 2 + 2) / 3));
        }

        private float Tex(float tiles)
        {
            return Mathf.Clamp(tiles * _k / Px, 2f, 140f);
        }

        /// <summary>The eye pixel: a little below the top of the head, toward the way it faces.</summary>
        private Vector2 EyePos()
        {
            float fwd = Pose.FaceRight ? 1f : -1f;
            return HeadPos() + new Vector2(fwd * 1f, -1.5f) * Px;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            float t = T;
            if (t < TRise)
            {
                Kneel(t);
            }
            else if (t < TBurst)
            {
                Rise(t, dt);
            }
            else
            {
                Settle(t);
            }
            Sweep(t);
            if (t >= TEnd)
            {
                Finish();
            }
        }

        // -------------------------------------------------------------- kneel --

        private void Kneel(float t)
        {
            float px = Px;
            float k = Anim.OutCubic(Mathf.Clamp01(t / 0.14f));
            Pose.Ground = _at;
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1f, 1.12f, k);
            Pose.SY = Mathf.Lerp(1f, 0.7f, k);
            Pose.Rot = 10f * k;
            Pose.Nudge = t < 0.08f ? new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
            if (t >= TBeat1 && Beat(BBeat1))
            {
                Heartbeat(px);
            }
            if (t >= TBeat2 && Beat(BBeat2))
            {
                Heartbeat(px);
            }
            if (_lastBeat >= 0f)
            {
                // the jolt of a heartbeat through the kneeling body
                float b = t - _lastBeat;
                if (b < 0.1f)
                {
                    Pose.SY += 0.06f * Mathf.Sin(Mathf.PI * b / 0.1f);
                    Pose.SX -= 0.03f * Mathf.Sin(Mathf.PI * b / 0.1f);
                }
            }
            if (t >= TTing && Beat(BTing))
            {
                Anim.Sound("event:/SFX/UI/HintHighlight", A.current_tile);
                if (Visible)
                {
                    int mark = GodFx.Mark();
                    Fx.Glint(EyePos(), px, 0.28f, Gold, 0.9f);
                    Fx.FocusLines(BodyPos(), px, N(12), 14f * _k, 28f * _k, 0.2f);
                    if (_full)
                    {
                        GodFx.Lift(mark);
                    }
                }
            }
        }

        private void Heartbeat(float px)
        {
            _lastBeat = T;
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile);
            if (!Visible)
            {
                return;
            }
            int mark = GodFx.Mark();
            Kit.DustRing(_at, px, A.current_tile, N(6), 28f, 0.4f, _k);
            Fx.Ring(_at, px, 0.3f, 3f, 12f * _k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                Fx.WithA(White, 0.7f), Fx.WithA(White, 0f));
            if (_full)
            {
                GodFx.Cine(mark);
            }
        }

        // --------------------------------------------------------------- rise --

        private void Rise(float t, float dt)
        {
            float px = Px;
            if (Beat(BRise))
            {
                Ignite(px);
            }
            float k = Mathf.Clamp01((t - TRise) / (TBurst - TRise));
            float e = Anim.OutCubic(k);
            Pose.Ground = _at;
            Pose.Height = 0.5f * _k * e;
            Pose.SX = Mathf.Lerp(1.12f, 1f, e) - 0.04f * Mathf.Sin(Mathf.PI * k);
            Pose.SY = Mathf.Lerp(0.7f, 1f, e) + 0.08f * Mathf.Sin(Mathf.PI * k);
            Pose.Rot = Mathf.Lerp(10f, 0f, e);
            Pose.Nudge = Vector2.zero;
            Pose.HideShadow = true;
            if (!Visible)
            {
                return;
            }
            // the pillar: speed lines streaming up from the God to the top of the screen
            _lines -= dt;
            if (_lines <= 0f)
            {
                _lines = 0.03f;
                float top = ScreenTop();
                Vector2 body = BodyPos();
                int n = N(3);
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = new Vector2(body.x + Random.Range(-5f, 5f) * px * _k, Random.Range(body.y, Mathf.Max(body.y + 1f, top)));
                    Particle p = Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, px, Random.Range(0.16f, 0.24f), Hi, 3);
                    p?.Moving(new Vector2(0f, Random.Range(140f, 200f) * px)).Color(Fx.WithA(GoldLight, 0.9f), Fx.WithA(Gold, 0.6f), Fx.WithA(Gold, 0f));
                }
            }
            // embers spiralling up around it
            _embers -= dt;
            if (_embers <= 0f)
            {
                _embers = 0.025f;
                Spiral(px);
            }
            _ghost -= dt;
            if (_ghost <= 0f)
            {
                _ghost = 0.08f;
                Particle g = Fx.Afterimage(this, 0.2f, new Color(1f, 0.92f, 0.6f, 0.45f), Fx.WithA(Gold, 0f));
                if (_full)
                {
                    GodFx.Lift(g);
                }
            }
        }

        /// <summary>0.85: the gold pillar to the top of the screen and the flash disk behind the God.</summary>
        private void Ignite(float px)
        {
            Anim.Sound("event:/SFX/POWERS/Powerup", A.current_tile);
            if (!Visible)
            {
                return;
            }
            int mark = GodFx.Mark();
            Vector2 foot = _at;
            Vector2 top = new Vector2(foot.x, Mathf.Max(foot.y + 4f, ScreenTop()));
            Kit.Line(foot, top, px, 0.62f, 3, Hi, 1, Fx.WithA(Gold, 0f), Fx.WithA(Gold, 0.5f), Fx.WithA(Gold, 0.45f), Fx.WithA(Gold, 0f));
            Kit.Line(foot, top, px, 0.62f, 1, Hi, 2, Fx.WithA(White, 0f), Fx.WithA(Hot, 0.9f), Fx.WithA(GoldLight, 0.7f), Fx.WithA(GoldLight, 0f));
            Particle disk = Kit.Flash(BodyPos(), px, Mathf.RoundToInt(Tex(2f)), 0.6f, Gold, Back, 1);
            disk?.Sized(0.3f, 1.1f).Color(Fx.WithA(GoldLight, 0.8f), Fx.WithA(Gold, 0.5f), Fx.WithA(Gold, 0f));
            Fx.Ring(_at, px, 0.5f, 4f, Tex(1.6f), 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f,
                Fx.WithA(GoldLight, 0.9f), Fx.WithA(Gold, 0f));
            if (_full)
            {
                GodFx.Lift(mark);
            }
            Dev.Watch(_at + new Vector2(0f, 2.5f));
        }

        private void Spiral(float px)
        {
            Vector2 center = BodyPos() - new Vector2(0f, 3f * px);
            float a0 = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(4f, 7f) * px * _k;
            float w = Random.Range(9f, 13f) * (Random.value < 0.5f ? -1f : 1f);
            float rise = Random.Range(26f, 40f) * px;
            Particle p = Fx.Spawn(Art.Pixel, center, px, Random.Range(0.45f, 0.6f), Hi, 9);
            if (p == null)
            {
                return;
            }
            p.Color(Hot, GoldLight, Gold, Fx.WithA(Gold, 0f));
            bool full = _full;
            p.Step = (q, d) =>
            {
                float ang = a0 + w * q.Age;
                float shrink = 1f - 0.5f * q.K;
                float depth = Mathf.Sin(ang);
                q.Pos = center + new Vector2(Mathf.Cos(ang) * r * shrink, depth * r * 0.4f * shrink + rise * q.Age);
                if (full)
                {
                    // the near half of the spiral passes in front of the God's clone (order 25), the far half behind
                    q.R.sortingOrder = depth < 0f ? 1030 : 1009;
                }
            };
        }

        private float ScreenTop()
        {
            Camera cam = World.world != null ? World.world.camera : null;
            return cam != null ? cam.transform.position.y + cam.orthographicSize + 1f : BodyPos().y + 20f;
        }

        // -------------------------------------------------------------- burst --

        private void Settle(float t)
        {
            float px = Px;
            if (Beat(BRise))
            {
                Ignite(px);
            }
            if (Beat(BBurst))
            {
                Burst(px);
            }
            float r = t - TBurst;
            Pose.Ground = _at;
            Pose.Nudge = Vector2.zero;
            Pose.Rot = 0f;
            if (t < TLand)
            {
                // hangs a beat in the burst, then drops back down
                float k = Mathf.Clamp01(r / (TLand - TBurst));
                Pose.Height = 0.5f * _k * (1f - k * k);
                Pose.SX = Mathf.Lerp(0.94f, 0.9f, k);
                Pose.SY = Mathf.Lerp(1.08f, 1.12f, k);
            }
            else
            {
                Pose.Height = 0f;
                Pose.HideShadow = false;
                float amp = Anim.Spring(t - TLand, 12f, 18f);   // (settled by the end: a crisp landing)
                Pose.SX = 1f + 0.16f * amp;
                Pose.SY = 1f - 0.18f * amp;
                if (t - TLand < 0.02f && Visible)
                {
                    Fx.LandingPuff(_at, px, A.current_tile);
                }
            }
        }

        /// <summary>1.40: the world resumes and the gold ring goes out; the God is Awakened.</summary>
        private void Burst(float px)
        {
            if (_full)
            {
                Cinema.End(_cast);   // the world moves again (the veil fades as the ring goes out)
            }
            _cine = false;
            Vector2 at = _at;
            WorldTile tile = A.current_tile;
            float radius = Radius * _k;
            if (Visible)
            {
                Fx.Ring(at, px, TWave, 4f, Tex(Radius), 3, 1, FxLayer.Ground, 6, Art.Ground, 1f,
                    Hot, Fx.WithA(GoldLight, 0.85f), Fx.WithA(Gold, 0.5f), Fx.WithA(Gold, 0f));
                Particle echo = Fx.Ring(at, px, TWave + 0.1f, 3f, Tex(Radius * 0.85f), 1, 1, FxLayer.Ground, 5, Art.Ground, 1.4f,
                    Fx.WithA(GoldLight, 0.6f), Fx.WithA(Gold, 0f));
                if (echo != null)
                {
                    echo.Delay = 0.05f;
                }
                Kit.Flash(BodyPos(), px, Mathf.RoundToInt(Tex(1f)), 0.14f, Hot, FxLayer.Top, 30);
                Kit.DustRing(at, px, tile, N(16), 105f, 0.7f, _k);
                Kit.Sparks(BodyPos(), px, N(12), 90f, 170f, GoldLight);
                int n = N(10);
                for (int i = 0; i < n; i++)
                {
                    Kit.Ember(BodyPos() + Random.insideUnitCircle * 4f * px, px, Random.Range(0.7f, 1.2f),
                        new Vector2(Random.Range(-14f, 14f), Random.Range(12f, 30f)) * px, Random.value < 0.4f ? GoldLight : Gold);
                }
                Kit.Shake(at, 0.3f, 0.01f, _k);
            }
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", tile);
            // everyone the ring will reach, in the order it reaches them
            foreach (Actor u in Combat.FoesAround(A, at, radius, false, true))
            {
                float d = Vector2.Distance(u.current_position, at);
                // the ring grows linearly (ease 1): it reaches a unit at d / radius of its time
                float when = Mathf.Clamp01(d / radius);
                _wave.Add(new Wave { U = u, Id = u.Sid(), At = TBurst + TWave * when, D = d });
            }
            Ruins(at, radius);
            Godhood.Awaken(A, AwakenedSeconds, 0.6f);
            if (Dev.On)
            {
                Dev.Trace("last light " + Id + " burst: " + _wave.Count + " in the ring, hp " + A.getHealth() + "/" + A.getMaxHealth());
            }
        }

        /// <summary>
        /// The ground the ring crosses: the whole disc scorched at once (the burst's heat), and the
        /// shockwave's craters on three rings, opened as the ring reaches them (the inner ring
        /// without flames: the God stands in the middle).
        /// </summary>
        private void Ruins(Vector2 at, float radius)
        {
            Kit.Ravage(at, radius, 1, A);
            Godhood.Gather(A, at, radius + 2.5f, _near, _allies, false);
            _near.Clear();
            float a0 = Random.value * Mathf.PI * 2f;
            for (int ri = 0; ri < RuinRings.Length; ri++)
            {
                float rr = RuinRings[ri] * _k;
                int n = Mathf.Max(5, Mathf.RoundToInt(2f * Mathf.PI * rr / RuinStep));
                for (int i = 0; i < n; i++)
                {
                    // each ring turned half a step from the last, so the craters interlock
                    float ang = a0 + (i + 0.5f * (ri & 1)) / n * Mathf.PI * 2f;
                    Vector2 p = Combat.Clamp(at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr);
                    _ruin.Add(new Ruin { P = p, At = TBurst + TWave * Mathf.Clamp01(rr / radius), Force = ri == 0 ? 2 : 3 });
                }
            }
        }

        /// <summary>The ring reaches them one by one: 30 + 2.6xD, thrown 2-4 tiles, stunned (Titans only stagger).</summary>
        private void Sweep(float t)
        {
            for (int i = _ruin.Count - 1; i >= 0; i--)
            {
                Ruin r = _ruin[i];
                if (t < r.At)
                {
                    continue;
                }
                _ruin.RemoveAt(i);
                GodRuin.Crater(A, r.P, 2f, r.Force, _allies, _at);
            }
            if (_wave.Count == 0)
            {
                return;
            }
            float radius = Radius * _k;
            Vector2 at = _at;
            for (int i = _wave.Count - 1; i >= 0; i--)
            {
                Wave w = _wave[i];
                if (t < w.At)
                {
                    continue;
                }
                _wave.RemoveAt(i);
                Actor u = w.U;
                // (Living: no blow on one another blow just killed, still "alive" until the death check)
                if (!Combat.Living(u) || u.Sid() != w.Id || !Combat.IsFoe(A, u))
                {
                    continue;
                }
                if (!Combat.Allowed(A, u, out float cm))
                {
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.4f);
                    continue;
                }
                if (_answered.Contains(w.Id) || Combat.Reach(this, u, true) != Contact.Hit)
                {
                    _answered.Add(w.Id);
                    continue;
                }
                Combat.Hit(A, u, 2.6f * cm, true, true, 30f * cm, this);
                if (!u.isAlive() || !u.hasHealth() || Moves.Locked(u))
                {
                    continue;   // dead, or held by some move: it is hit but not thrown
                }
                if (Weights.Heavy(u))
                {
                    Combat.Stagger(u, 0.5f);
                    continue;
                }
                float k = Mathf.Clamp01(w.D / radius);
                float dist = Mathf.Lerp(4f, 2f, k) * _k;
                Combat.Launch(u, at, dist, Mathf.Lerp(1.4f, 0.7f, k) * _k, (Random.value < 0.5f ? -1f : 1f) * Random.Range(500f, 800f));
                GodRuin.Shelter(u, at, dist);
                // (a high tier is back on its feet sooner: the fight goes on)
                u.addStatusEffect("stunned", 1.5f * Mathf.Max(0.5f, Tiers.Linger(u)));
                _thrown++;
                _lastThrown = u;
                if (u.is_visible)
                {
                    Fx.HitSpark(u.current_position + new Vector2(0f, u.getHeight() + 0.1f), px: Mathf.Clamp(u.actor_scale, 0.04f, 0.4f), size: 1, away: u.current_position - at, sparks: 3);
                }
            }
        }

        // ------------------------------------------------------- glued effects --

        public override void Frame()
        {
            float t = T;
            // the grey burns away to gold as it rises
            float rise = Mathf.Clamp01((t - TRise) / (TBurst - TRise));
            if (_clone != null && _clone.Alive)
            {
                Color c = Color.Lerp(Grey, new Color(1f, 0.97f, 0.86f, 1f), Anim.Smooth(0f, 0.7f, rise));
                Fx.Glue(_clone, this, t < TBurst ? c : White);
            }
            if (_shade != null && _shade.Alive)
            {
                Color c;
                if (t < TRise)
                {
                    // light mode darkens the sprite itself; full mode washes the tinted clone to ash
                    // (pale on the black veil: drained of colour, but the shape reads)
                    c = _full
                        ? Fx.WithA(Ash, 0.45f * Mathf.Clamp01(t / 0.12f))
                        : Fx.WithA(new Color(0.12f, 0.12f, 0.16f, 1f), 0.6f * Mathf.Clamp01(t / 0.12f));
                }
                else if (t < TBurst)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin((t - TRise) * 30f);
                    c = Fx.WithA(Gold, Mathf.Lerp(0.15f, 0.45f, rise) * (0.7f + 0.3f * pulse));
                    if (_full && t - TRise < 0.15f)
                    {
                        // the ash burns to gold (no pop)
                        c = Color.Lerp(Fx.WithA(Ash, 0.45f), c, (t - TRise) / 0.15f);
                    }
                }
                else
                {
                    c = Fx.WithA(GoldLight, Mathf.Lerp(0.55f, 0f, Mathf.Clamp01((t - TBurst) / 0.4f)));
                }
                Fx.Glue(_shade, this, c);
            }
            if (_flash != null && _flash.Alive)
            {
                float a = 0f;
                if (_lastBeat >= 0f && t < TRise)
                {
                    a = Mathf.Clamp01(1f - (t - _lastBeat) / 0.1f);
                }
                else if (t >= TBurst && t < TBurst + 0.08f)
                {
                    a = 1f;
                }
                Fx.Glue(_flash, this, Fx.WithA(White, 0.9f * a));
            }
        }

        // ------------------------------------------------------------- endings --

        private void Finish()
        {
            if (Finished)
            {
                return;
            }
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.HideShadow = false;
            // whatever the ring has not reached yet still breaks (a frame-starved finish)
            Sweep(TBurst + TWave + 1f);
            // it chains straight on into those the ring threw
            if (_thrown >= 2)
            {
                EndLink = Link.Scattered;
            }
            else if (_thrown == 1 && _lastThrown != null && _lastThrown.isAlive())
            {
                EndLink = Link.Down;
                EndTarget = _lastThrown;
            }
            Cleanup();
            GodRuin.Settle(A, _at);
            GodRuin.AfterMove(A, false);
            Finished = true;
        }

        private void Cleanup()
        {
            _clone?.Kill();
            _shade?.Kill();
            _flash?.Kill();
            _clone = _shade = _flash = null;
            if (_full)
            {
                Cinema.End(_cast);
            }
            _cine = false;
        }

        public override void Abort()
        {
            Cleanup();
            _wave.Clear();
        }

        public override void OnEnd(bool aborted)
        {
            Cleanup();
            _wave.Clear();
            Godhood.Ended(this, _full);
        }
    }

    /// <summary>
    /// Last Light as a technique: a God ability (Entry God, one level). Nothing plans it: Godhood
    /// fires it from the hit that would end the God (or leave it at 15% or less).
    /// </summary>
    internal sealed class LastLightTech : Technique
    {
        public override string Id => "last_light";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.God;

        /// <summary>The God's crisis: it rises in a pillar of gold and the burst throws its enemies back.</summary>
        public override Form Forms => Form.Ki | Form.Area | Form.Ultimate | Form.Counter;

        public override Scale Scale => Scale.Ultimate;

        public override int MaxLevel => 1;
        public override bool GodAbility => true;
        public override float[] ManaCost => new[] { 0f };
        public override float[] Cooldown => new[] { Godhood.CooldownLast };

        public override Plan Plan(Ctx c, int level)
        {
            return Godhood.PlanFor(this, GodKind.LastLight, c);
        }

        public override bool Begin(Actor a, Plan p)
        {
            return Godhood.Begin(this, GodKind.LastLight, a, p);
        }
    }
}
