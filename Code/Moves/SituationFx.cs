using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The visual vocabulary of battle situations, drawn at the pixel size of the unit they belong to:
    ///   - glyph marks that pop over a head: a "!" (alarm, waver, a rescuer's resolve), the anime
    ///     anger vein (vengeance), a rising chevron (a lesson learned),
    ///   - a pillar of light behind a unit (champions, the underdog), falling glitter,
    ///   - a SitAura: a unit's silhouette glued to it while it lives its situation (a 1-pixel outline of
    ///     light and a faint tint), whether or not it is performing a move,
    ///   - a short slow-motion beat that always ends (its own clock on Hooks.FrameTick ends it, not our tick).
    /// Everything checks the screen first and stays tiny in particle count. Nothing shows over a unit
    /// that has vanished mid-blink (Pose.Hidden).
    /// </summary>
    internal static class SitFx
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.26f, 1f);
        public static readonly Color GoldPale = new Color(1f, 0.95f, 0.68f, 1f);
        public static readonly Color GoldDeep = new Color(0.72f, 0.42f, 0.08f, 1f);
        public static readonly Color Grey = new Color(0.72f, 0.74f, 0.78f, 1f);
        public static readonly Color Blood = new Color(0.95f, 0.15f, 0.12f, 1f);
        public static readonly Color BloodDark = new Color(0.34f, 0.02f, 0.05f, 1f);
        public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        private static Sprite _bang, _vein, _chevron;

        /// <summary>A chunky "!" (2 pixels wide, with a dark outline).</summary>
        public static Sprite Bang => _bang != null ? _bang : (_bang = Glyph("sakuga_sit_bang", new[] { "##", "##", "##", "##", "..", "##" }));

        /// <summary>The anime anger vein: four little corners popping around a point.</summary>
        public static Sprite Vein => _vein != null ? _vein : (_vein = Glyph("sakuga_sit_vein", new[] { "##.##", "#...#", ".....", "#...#", "##.##" }));

        /// <summary>An upward chevron: something was learned.</summary>
        public static Sprite Chevron => _chevron != null ? _chevron : (_chevron = Glyph("sakuga_sit_chevron", new[] { "..#..", ".###.", "##.##", "#...#" }));

        /// <summary>
        /// A glyph from rows of '#' (top row first), with a 1-pixel dark outline all around so it
        /// reads on grass, sand and snow alike. Its pivot is the bottom middle (it sits on a head).
        /// </summary>
        private static Sprite Glyph(string name, string[] rows)
        {
            int h = rows.Length, w = rows[0].Length;
            Canvas c = new Canvas(w + 2, h + 2);
            bool Core(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    return false;
                }
                return rows[h - 1 - y][x] == '#';
            }
            for (int y = -1; y <= h; y++)
            {
                for (int x = -1; x <= w; x++)
                {
                    if (Core(x, y))
                    {
                        // a lighter top row: the shine every glyph in the game has
                        c.Set(x + 1, y + 1, Core(x, y + 1) ? 0.9f : 1f, 1f);
                        continue;
                    }
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !edge; dx++)
                        {
                            edge = Core(x + dx, y + dy);
                        }
                    }
                    if (edge)
                    {
                        c.Set(x + 1, y + 1, 0.16f, 0.85f);
                    }
                }
            }
            return c.ToSprite(name, new Vector2(0.5f, 0f));
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>World units per sprite pixel of a unit.</summary>
        public static float PxOf(Actor a)
        {
            return a != null ? Mathf.Clamp(a.actor_scale, 0.04f, 0.4f) : 0.1f;
        }

        /// <summary>The unit's size relative to a human.</summary>
        public static float KOf(Actor a)
        {
            return a != null ? Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f)) : 1f;
        }

        /// <summary>World units from a unit's feet to the top of its head (from its sprite).</summary>
        public static float HeadUp(Actor a)
        {
            Sprite s = Fx.UnitSprite(a);
            float pixels = s != null ? s.rect.height - s.pivot.y : 9f;
            return Mathf.Clamp(pixels, 4f, 60f) * a.actor_scale;
        }

        /// <summary>The top of a unit's head right now (a performer's pose included).</summary>
        public static Vector2 Head(Actor a, float headUp)
        {
            if (Moves.TryGet(a, out Move m))
            {
                return new Vector2(m.Pose.Ground.x, m.Pose.Ground.y + m.Pose.Height + headUp * m.Pose.SY);
            }
            return new Vector2(a.current_position.x, a.current_position.y + a.position_height + headUp);
        }

        /// <summary>Where a unit's feet are right now (a performer's shadow point included).</summary>
        public static Vector2 Feet(Actor a)
        {
            return Moves.TryGet(a, out Move m) ? m.Pose.Ground : a.current_position;
        }

        /// <summary>The middle of a unit's body right now.</summary>
        public static Vector2 Body(Actor a)
        {
            if (Moves.TryGet(a, out Move m))
            {
                return m.BodyPos();
            }
            return new Vector2(a.current_position.x, a.current_position.y + a.position_height + HeadUp(a) * 0.45f);
        }

        /// <summary>Has the unit vanished mid-blink (a performer's Pose.Hidden)? Nothing of it shows then.</summary>
        internal static bool Hidden(Actor a)
        {
            return Moves.TryGet(a, out Move m) && m.Pose.Hidden;
        }

        /// <summary>
        /// Is this pooled particle still the one a holder spawned? A particle that ended (killed by
        /// anyone) goes back to the pool and is handed out again, alive, to someone else: a holder keeps
        /// the Frames array its particle was spawned with (every single-sprite spawn makes a new one)
        /// and touches the particle only while it still carries it.
        /// </summary>
        internal static bool Mine(Particle p, Sprite[] frames)
        {
            return p != null && frames != null && p.Alive && ReferenceEquals(p.Frames, frames);
        }

        /// <summary>
        /// Keeps an effect a situation's move spawned on its unit's body ('off' from the middle of it)
        /// for its whole life, also once that move has ended (the unit springs on into its next move:
        /// the glint goes with it, not left hanging where the pose was), hidden while it is mid-blink.
        /// </summary>
        internal static void Ride(Particle p, Move m, Vector2 off)
        {
            if (p == null || m == null)
            {
                return;
            }
            Actor a = m.A;
            long id = m.Id;
            float pivotUp = m.Pose.PivotUp;
            float size = p.Size0;
            p.Step = (q, d) =>
            {
                if (a.Sid() != id || !a.isAlive())
                {
                    return;   // the body is gone: it fades where it was
                }
                Vector2 body;
                bool hidden = false;
                if (Moves.TryGet(a, out Move cur))
                {
                    body = cur.BodyPos();
                    hidden = cur.Pose.Hidden;
                }
                else
                {
                    // Move.BodyPos of a unit standing as the game draws it
                    body = new Vector2(a.current_position.x, a.current_position.y + a.position_height + pivotUp * a.actor_scale);
                }
                q.Pos = body + off;
                q.Size0 = q.Size1 = hidden ? 0.001f : size;
            };
        }

        // -------------------------------------------------------------- marks --

        /// <summary>
        /// A glyph pops over a unit's head (overshoots, settles, rises 'risePx' and fades), and
        /// follows it. 'throbs': the vein pulses that many times.
        /// </summary>
        public static Particle Mark(Actor a, Sprite glyph, Color c, float life, float risePx = 2f, int throbs = 0)
        {
            if (a == null || glyph == null || !a.isAlive() || !a.is_visible || Hidden(a))
            {
                return null;
            }
            float px = PxOf(a);
            float head = HeadUp(a);
            Vector2 at = Head(a, head);
            if (!Kit.Visible(at))
            {
                return null;
            }
            Particle p = Fx.Spawn(glyph, at + new Vector2(0f, 2f * px), px, life, FxLayer.Top, 12);
            if (p == null)
            {
                return null;
            }
            p.Snap = true;
            p.Color(c, c, c, c, c, Fx.WithA(c, 0f));
            long id = a.Sid();
            Vector2 anchor = at;
            bool hidden = false;
            p.Step = (q, d) =>
            {
                if (a.Sid() == id && a.isAlive())
                {
                    anchor = Head(a, head);
                    hidden = Hidden(a);
                }
                float t = q.Age;
                float s = t < 0.06f ? Mathf.Lerp(0.3f, 1.35f, t / 0.06f) : 1f + 0.35f * Anim.Spring(t - 0.06f, 9f, 22f);
                if (throbs > 0 && t > 0.2f && t < 0.2f + throbs * 0.3f)
                {
                    s += 0.22f * Mathf.Max(0f, Mathf.Sin((t - 0.2f) / 0.3f * Mathf.PI * 2f));
                }
                if (Mathf.Abs(s - 1f) < 0.05f)
                {
                    s = 1f;   // crisp pixels once it has settled
                }
                q.Size0 = q.Size1 = hidden ? 0.001f : s;   // gone with its unit while it blinks
                q.Pos = anchor + new Vector2(0f, (2f + risePx * Anim.OutQuad(q.K)) * px);
            };
            return p;
        }

        /// <summary>A twinkle glint that rides on a unit's head for its life.</summary>
        public static Particle HeadGlint(Actor a, Color c, float life, float size, Vector2 offsetPx)
        {
            if (a == null || !a.isAlive() || !a.is_visible || Hidden(a))
            {
                return null;
            }
            float px = PxOf(a);
            float head = HeadUp(a);
            Vector2 at = Head(a, head) + offsetPx * px;
            if (!Kit.Visible(at))
            {
                return null;
            }
            Particle p = Fx.Glint(at, px, life, c, size);
            if (p == null)
            {
                return null;
            }
            long id = a.Sid();
            p.Step = (q, d) =>
            {
                if (a.Sid() == id && a.isAlive())
                {
                    q.Pos = Head(a, head) + offsetPx * px;
                    q.Size0 = q.Size1 = Hidden(a) ? 0.001f : size;   // gone with its unit while it blinks
                }
            };
            return p;
        }

        // ------------------------------------------------------------- pillars --

        /// <summary>
        /// A pillar of light behind a unit: shoots up from the feet in 0.08 s, then thins and fades.
        /// 'heightPx' in the unit's pixels; drawn behind units so the unit stands in front of it.
        /// </summary>
        public static void Pillar(Vector2 feet, float px, float heightPx, float life, Color core, Color edge, int width = 2)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            Beam(feet, px, heightPx * 0.9f, life * 1.1f, width + 2, FxLayer.Behind, 1, Fx.WithA(edge, 0.4f), Fx.WithA(edge, 0.25f), Fx.WithA(edge, 0f));
            Beam(feet, px, heightPx, life, width, FxLayer.Behind, 2, Color.white, core, Fx.WithA(edge, 0.8f), Fx.WithA(edge, 0f));
        }

        private static void Beam(Vector2 feet, float px, float heightPx, float life, int width, FxLayer layer, int order, params Color[] colors)
        {
            Particle p = Fx.Spawn(Art.Pixel, feet, px, life, layer, order);
            if (p == null)
            {
                return;
            }
            p.Color(colors);
            p.SX = width;
            p.SY = 1f;
            p.Step = (q, d) =>
            {
                float grow = Anim.OutCubic(Mathf.Clamp01(q.Age / 0.08f));
                float h = Mathf.Max(1f, heightPx * grow);
                q.SY = Mathf.Round(h);
                q.SX = Mathf.Max(1f, Mathf.Round(Mathf.Lerp(width, 1f, Anim.Smooth(0.35f, 1f, q.K))));
                q.Pos = feet + new Vector2(0f, q.SY * 0.5f * px);
            };
        }

        /// <summary>Glittering pixels drifting down around a point (a champion's blessing, a victory).</summary>
        public static void Glitter(Vector2 feet, float px, int n, float topPx, Color c)
        {
            if (!Kit.Visible(feet))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = feet + new Vector2(Random.Range(-7f, 7f), Random.Range(topPx * 0.45f, topPx)) * px;
                Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.55f, 0.95f), FxLayer.Top, 9);
                if (p == null)
                {
                    return;
                }
                p.Moving(new Vector2(Random.Range(-3f, 3f), Random.Range(-14f, -7f)) * px, 0f, 1.4f);
                p.Color(Color.white, c, c, Fx.WithA(c, 0f));
                float seed = Random.value * 10f;
                p.Step = (q, d) =>
                {
                    // a twinkle: on, on, off
                    bool on = ((int)(q.Age * 16f + seed) % 3) != 2;
                    q.Size0 = q.Size1 = on ? 1f : 0.001f;
                };
            }
        }

        /// <summary>Embers rising off a unit's body in a colour.</summary>
        public static void Embers(Actor a, int n, Color c, float speed = 1f)
        {
            if (a == null || !a.is_visible || Hidden(a))
            {
                return;
            }
            float px = PxOf(a);
            Vector2 body = Body(a);
            if (!Kit.Visible(body))
            {
                return;
            }
            float head = HeadUp(a);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = body + new Vector2(Random.Range(-3.5f, 3.5f) * px, Random.Range(-0.4f, 0.5f) * head);
                Kit.Ember(at, px, Random.Range(0.45f, 0.85f), new Vector2(Random.Range(-4f, 4f), Random.Range(14f, 26f) * speed) * px, c);
            }
        }

        // -------------------------------------------------------------- slow-mo --

        /// <summary>The slow-motion beat playing now (its cinematic owner) and the real seconds it has left.</summary>
        private static object _beat;
        private static float _beatLeft;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += BeatTick;
        }

        /// <summary>
        /// A short slow-motion beat where the player is looking: the world runs at 'rate' for
        /// 'seconds' (real time). It owns the cinematic only while it plays, and its own clock ends
        /// it (Hooks.FrameTick runs every game frame, even when no fighter is left, and nothing else
        /// can stop it: a held timer particle could be killed by anyone holding a stale reference to
        /// a pooled particle, and the cinematic would stay owned for good).
        /// </summary>
        public static void SlowBeat(Vector2 at, float rate, float seconds)
        {
            if (Cinema.Playing || !Cinema.FullAt(at))
            {
                return;
            }
            object owner = new object();
            if (!Cinema.TryBegin(owner, 4f))
            {
                return;
            }
            _beat = owner;
            _beatLeft = seconds;
            Cinema.SlowMo(rate, seconds);
        }

        /// <summary>Once a game frame, on the cinematic clock (real time, still while paused): the beat ends on time.</summary>
        private static void BeatTick(float dt)
        {
            if (_beat == null)
            {
                return;
            }
            _beatLeft -= Cinema.Dt;
            if (_beatLeft > 0f)
            {
                return;
            }
            object owner = _beat;
            _beat = null;
            try
            {
                Cinema.End(owner);   // (a world cleared meanwhile cleared the cinema: then this is nothing)
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] slow beat: " + e);
            }
        }
    }

    /// <summary>
    /// A unit's glow while it lives a situation (a last stand's burning outline, a champion's
    /// shine, the underdog's flash): copies of its sprite glued to it every frame, wherever it is
    /// drawn (in a move or not). An outline is four flat-colour copies one pixel off in each
    /// direction behind it; a tint is one copy over it. The aura ends by itself if its unit dies
    /// or is recycled, or if its owner stops keeping it (Keep) for a second of world time.
    /// </summary>
    internal sealed class SitAura
    {
        private static readonly Vector2[] Offsets = { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f) };

        public readonly Actor A;
        public readonly long Id;
        /// <summary>Outline colour (alpha = strength) and tint colour (alpha = strength); set them every frame to animate.</summary>
        public Color Edge, Tint;
        /// <summary>The top of the outline licks one pixel higher now and then (flames).</summary>
        public bool Flame;

        private readonly List<Particle> _parts = new List<Particle>(5);
        /// <summary>The Frames each part was spawned with: a pooled part that is someone else's now is left alone (SitFx.Mine).</summary>
        private readonly List<Sprite[]> _frames = new List<Sprite[]>(5);
        private double _kept;
        private bool _ending;
        private float _fade = 0.3f;
        private int _lick;
        private float _lickIn;

        public SitAura(Actor a, Color edge, Color tint, bool outline, bool overlay)
        {
            A = a;
            Id = a.Sid();
            Edge = edge;
            Tint = tint;
            _kept = Fighter.Now;
            if (!Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(a);
            if (s == null)
            {
                return;
            }
            if (outline)
            {
                for (int i = 0; i < Offsets.Length; i++)
                {
                    Add(s, FxLayer.Behind, 1, Offsets[i], false);
                }
            }
            if (overlay)
            {
                Add(s, FxLayer.Over, 1, Vector2.zero, true);
            }
        }

        public bool Alive => !_ending && A.Sid() == Id && A.isAlive();

        /// <summary>The owner still wants it (call every frame it lasts).</summary>
        public void Keep()
        {
            _kept = Fighter.Now;
        }

        /// <summary>World seconds without a Keep before it gives up: one second at x1, longer at high speeds (a slow frame at x20 is seconds of world time).</summary>
        private static double Patience
        {
            get
            {
                float speed = Config.time_scale_asset != null ? Config.time_scale_asset.multiplier : 1f;
                return Math.Max(1.0, speed * 0.5);
            }
        }

        /// <summary>Fades out over 'fade' seconds.</summary>
        public void End(float fade = 0.3f)
        {
            if (_ending)
            {
                return;
            }
            _ending = true;
            _fade = Mathf.Max(0.02f, fade);
            for (int i = 0; i < _parts.Count; i++)
            {
                Particle p = _parts[i];
                if (SitFx.Mine(p, _frames[i]))
                {
                    p.Held = false;
                    p.Life = p.Age + _fade;
                }
            }
        }

        private void Add(Sprite s, FxLayer layer, int order, Vector2 off, bool tint)
        {
            Particle p = Fx.Spawn(s, A.current_position, 1f, 1f, layer, order, true);
            if (p == null)
            {
                return;
            }
            p.Held = true;
            p.UseEuler = true;
            p.Color(Fx.WithA(Color.white, 0f));
            p.Grad = null;
            p.R.color = Fx.WithA(Color.white, 0f);
            p.Step = (q, d) => Step(q, d, off, tint);
            _parts.Add(p);
            _frames.Add(p.Frames);
        }

        private void Step(Particle q, float dt, Vector2 off, bool tint)
        {
            if (A.Sid() != Id || !A.isAlive())
            {
                // the body is gone: so is its light, and the aura is over (a late End must not
                // touch these parts once the pool has handed them to someone else)
                _ending = true;
                q.Kill();
                return;
            }
            if (!_ending && Fighter.Now - _kept > Patience)
            {
                End(0.3f);   // nobody keeps it any more
            }
            Color c = tint ? Tint : Edge;
            if (_ending)
            {
                c.a *= Mathf.Clamp01((q.Life - q.Age) / _fade);
            }
            if (Flame && !tint && off.y > 0f)
            {
                // the top copy flickers between one and two pixels up: the outline burns
                _lickIn -= dt;
                if (_lickIn <= 0f)
                {
                    _lickIn = Random.Range(0.05f, 0.11f);
                    _lick = Random.value < 0.45f ? 1 : 0;
                }
                off.y += _lick;
            }
            Glue(q, A, off, c);
        }

        /// <summary>Puts a silhouette exactly where the unit is drawn this frame, 'offPx' pixels off.</summary>
        public static void Glue(Particle q, Actor a, Vector2 offPx, Color c)
        {
            Sprite s = Fx.UnitSprite(a);
            if (s == null)
            {
                q.R.color = SitFx.Clear;
                return;
            }
            if ((object)q.R.sprite != s)
            {
                q.R.sprite = s;
                q.Frames[0] = s;
            }
            float sc = a.actor_scale;
            Vector2 pos;
            bool hidden = false;
            if (Moves.TryGet(a, out Move m))
            {
                pos = m.DrawPos();
                q.Euler = Fx.UnitEuler(m);
                q.SX = sc * m.Pose.SX;
                q.SY = sc * m.Pose.SY;
                hidden = m.Pose.Hidden;
            }
            else
            {
                pos = a.current_position + a.move_jump_offset + a.shake_offset + new Vector2(0f, a.position_height);
                q.Euler = a.target_angle;
                q.SX = a.current_scale.x;
                q.SY = a.current_scale.y;
            }
            q.Pos = pos + offPx * sc;
            q.Z = 0f;
            q.UseEuler = true;
            q.Grad = null;
            q.R.color = !hidden && a.is_visible && c.a > 0.003f && !a.isInsideSomething() ? c : SitFx.Clear;
        }
    }
}
