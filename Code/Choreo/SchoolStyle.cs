using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SCHOOL STYLE: whatever a fighter does with a technique, its school shows, from a Novice's first
    /// jab to a God's catastrophe, so two fighters doing "a dash and a few blows" never look alike:
    ///
    ///   AURA     while the technique plays, the fighter wears its school: iron fists smoulder with
    ///            sparks and heat, tempest legs whip wind crescents round themselves, sword saints flash
    ///            steel glints, ki masters shed gold motes, earth breakers shake pebbles and dust loose,
    ///            phantoms trail violet smoke and afterimages, hunters move in green focus lines
    ///   TRAIL    moving fast (a dash, a leap, a blink), it leaves its school's streak: a red speed blur
    ///            and kicked-up dust, a wind ribbon, a steel cut line, a gold stream, a furrow of
    ///            pebbles, a violet afterimage, a low green blur
    ///   EMBLEM   every blow that lands is stamped with the school's own big mark (Combat.Hit, once per
    ///            blow): a fist print and a shock ring, a sweeping wind crescent, a long cut of light, a
    ///            burst of gold ki, a star of cracks and flying rock, a violet tear, a hunter's mark
    ///   GATHER   a charge gathers in the school's way (ChoreoMove.TickCharge): only ki gathers an orb;
    ///            a fist glows red and sheds sparks, wind spirals in, light runs up a blade, rocks lift
    ///            off the ground, the dark thickens, a mark settles on the prey
    /// The colours are the school's own (Hue.For with the school), strong even at level I.
    /// </summary>
    internal static class SchoolStyle
    {
        private sealed class State
        {
            public School School;
            public Hue Hue;
            public bool HasHue;
            public float Next, Ghost;
            public Vector2 Last;
            public bool Has;
        }

        private static readonly Dictionary<Move, State> _st = new Dictionary<Move, State>();
        private static readonly Dictionary<long, float> _stamped = new Dictionary<long, float>();

        /// <summary>The school a move is shown in: its technique's, or (a technique of no school) the fighter's own.</summary>
        public static School Of(Move m)
        {
            if (m == null || m.A == null)
            {
                return School.None;
            }
            if (m.Tech == null && !(m is Duelist))
            {
                return School.None;
            }
            if (m.Tech != null && m.Tech.School != School.None)
            {
                return m.Tech.School;
            }
            if (!Settings.Schools)
            {
                return School.None;
            }
            List<School> s = Schools.Of(m.A);
            return s.Count == 1 ? s[0] : School.None;
        }

        /// <summary>Every frame of a running move (Moves.Update): its school's aura and trail.</summary>
        public static void Tick(Move m, float dt)
        {
            if (m.Tech == null && !(m is Duelist) || dt <= 0f)
            {
                return;
            }
            if (!_st.TryGetValue(m, out State s))
            {
                s = new State { School = Of(m) };
                if (s.School != School.None)
                {
                    s.Hue = Hue.For(m.A, s.School, Mathf.Clamp(m.Level, 1, 3));
                    s.HasHue = true;
                }
                _st[m] = s;
            }
            if (s.School == School.None || !s.HasHue)
            {
                return;
            }
            Actor a = m.A;
            float px = Choreo.PxOf(a), k = Choreo.KOf(a);
            Vector2 c = Choreo.BodyAt(a, 0.5f);
            Vector2 feet = Choreo.BodyAt(a, 0f);
            if (!Kit.Visible(c, 0.5f))
            {
                s.Last = c;
                s.Has = true;
                return;
            }
            if (s.Has)
            {
                Vector2 step = c - s.Last;
                float speed = step.magnitude / dt;
                // a dash, a leap, a blink, a body flying with its blow: the school's streak behind it
                if (speed > 5f && step.magnitude < 12f * k)
                {
                    Trail(s, a, m, s.Last, c, feet, px, k);
                }
            }
            s.Last = c;
            s.Has = true;
            s.Next -= dt;
            if (s.Next <= 0f)
            {
                s.Next = 0.05f;
                Aura(s, a, m, c, feet, px, k);
            }
        }

        public static void End(Move m)
        {
            _st.Remove(m);
        }

        /// <summary>A school's name for the logs.</summary>
        public static string Name(School s)
        {
            return s == School.None ? "none" : Schools.Id(s);
        }

        // ------------------------------------------------------------------------------------- aura --

        private static void Aura(State s, Actor a, Move m, Vector2 c, Vector2 feet, float px, float k)
        {
            Hue h = s.Hue;
            float half = Choreo.SpritePx(a) * px * 0.5f;
            Vector2 r = new Vector2(Random.Range(-1f, 1f) * half * 0.8f, Random.Range(-1f, 1f) * half);
            // the fighter glows in its school's colour, faint and flickering
            s.Ghost -= 0.05f;
            if (s.Ghost <= 0f)
            {
                s.Ghost = 0.15f;
                Trails.Ghost(a, 0.12f, Fx.WithA(h.Main, 0.45f), Fx.WithA(h.Edge, 0f));
            }
            switch (s.School)
            {
                case School.IronFist:
                    // sparks and heat off its fists and shoulders, rising
                    Kit.Ember(c + r, px, Random.Range(0.25f, 0.45f), new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 28f)) * px, Color.Lerp(h.Main, new Color(1f, 0.6f, 0.2f, 1f), 0.4f));
                    if (Random.value < 0.4f)
                    {
                        Kit.Ember(c + r * 0.5f, px, 0.3f, new Vector2(Random.Range(-10f, 10f), Random.Range(10f, 20f)) * px, Color.white);
                    }
                    break;
                case School.TempestLeg:
                    {
                        // wind crescents whipping round its legs and body
                        int cr = Mathf.Clamp(Mathf.RoundToInt(half / px * 0.9f), 3, 14);
                        Particle p = Fx.Spawn(Art.Arc(cr, 0.5f, 60f, -70f, 1), c + new Vector2(0f, -half * 0.3f), px, 0.18f, FxLayer.Top, 14);
                        if (p != null)
                        {
                            p.Rot = Random.Range(0f, 360f);
                            p.Spin = Random.value < 0.5f ? 1100f : -1100f;
                            p.Sized(0.8f, 1.3f).Color(Fx.WithA(Color.white, 0.85f), Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
                        }
                        if (Random.value < 0.5f)
                        {
                            Particle d = Fx.Spawn(Art.Pixel, feet + new Vector2(Random.Range(-half, half), 0f), px, 0.3f, FxLayer.Top, 12);
                            d?.Moving(new Vector2(Random.Range(-14f, 14f), Random.Range(8f, 20f)) * px, 0f, 1f).Color(Fx.WithA(h.Main, 0.9f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case School.ThousandBlades:
                    // glints of steel on the blade and round the body
                    if (Random.value < 0.6f)
                    {
                        Fx.Glint(c + r, px, 0.14f, Color.white, Random.Range(0.6f, 1f));
                    }
                    if (Random.value < 0.35f)
                    {
                        Vector2 d = Random.insideUnitCircle.normalized;
                        float len = half * Random.Range(0.6f, 1.1f);
                        Kit.Line(c + r - d * len, c + r + d * len, px, 0.08f, 1, FxLayer.Top, 15, Fx.WithA(Color.white, 0.9f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case School.KiArts:
                    // gold motes streaming off it, rising
                    for (int i = 0; i < 2; i++)
                    {
                        Particle p = Fx.Spawn(Art.Pixel, c + r, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 14);
                        p?.Moving(new Vector2(Random.Range(-5f, 5f), Random.Range(10f, 22f)) * px, 0f, 1f).Sized(1.6f, 0.6f)
                            .Color(Color.white, Fx.WithA(h.Core, 0.95f), Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case School.EarthBreaker:
                    {
                        // pebbles hopping off the ground round its feet, dust
                        Color soil = Fx.SoilColor(Combat.TileAt(feet));
                        Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], feet + new Vector2(Random.Range(-half, half), 0f), px, 0.4f, FxLayer.Top, 13);
                        if (p != null)
                        {
                            p.Moving(new Vector2(Random.Range(-10f, 10f), Random.Range(25f, 45f)) * px, 0f, 0.4f, 260f * px);
                            p.Spin = Random.Range(-500f, 500f);
                            p.Color(Color.Lerp(soil, h.Main, 0.35f), soil, Fx.WithA(soil, 0f));
                        }
                        if (Random.value < 0.4f)
                        {
                            Kit.DustRing(feet, px, Combat.TileAt(feet), 2, 12f, 0.35f, k);
                        }
                    }
                    break;
                case School.PhantomStep:
                    {
                        // violet smoke curling off it
                        Color dark = Color.Lerp(h.Main, Color.black, 0.45f);
                        Particle p = Fx.Spawn(Art.Disk(Random.Range(2, 4), 1f, 0.6f), c + r, px, 0.45f, FxLayer.Top, 13);
                        p?.Moving(new Vector2(Random.Range(-6f, 6f), Random.Range(6f, 14f)) * px, 0f, 1f).Sized(0.8f, 1.6f)
                            .Color(Fx.WithA(h.Main, 0.7f), Fx.WithA(dark, 0.55f), Fx.WithA(dark, 0f));
                    }
                    break;
                case School.HuntersWay:
                    {
                        // green focus lines drawn in on it, the hunter's concentration
                        if (Random.value < 0.55f)
                        {
                            Vector2 d = Random.insideUnitCircle.normalized;
                            Kit.Line(c + d * half * 2.2f, c + d * half * 1.3f, px, 0.09f, 1, FxLayer.Top, 14, Fx.WithA(h.Main, 0.9f), Fx.WithA(h.Edge, 0f));
                        }
                        Particle p = Fx.Spawn(Art.Pixel, feet + new Vector2(Random.Range(-half, half), 0f), px, 0.35f, FxLayer.Top, 12);
                        p?.Moving(new Vector2(Random.Range(-8f, 8f), Random.Range(10f, 18f)) * px, 0f, 0.8f, 60f * px).Color(Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------------------------ trail --

        private static void Trail(State s, Actor a, Move m, Vector2 from, Vector2 to, Vector2 feet, float px, float k)
        {
            Hue h = s.Hue;
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f)
            {
                return;
            }
            Vector2 n = d / len;
            Vector2 side = new Vector2(-n.y, n.x);
            switch (s.School)
            {
                case School.IronFist:
                    // a red speed blur, dust kicked up behind
                    Kit.Line(from, to, px, 0.12f, 3, FxLayer.Top, 13, Fx.WithA(h.Main, 0.75f), Fx.WithA(h.Edge, 0f));
                    Kit.Line(from, to, px, 0.09f, 1, FxLayer.Top, 14, Fx.WithA(Color.white, 0.8f), Fx.WithA(h.Main, 0f));
                    if (Random.value < 0.5f)
                    {
                        Kit.DustRing(feet, px, Combat.TileAt(feet), 2, 16f, 0.3f, k);
                    }
                    break;
                case School.TempestLeg:
                    {
                        // two wind ribbons twisting round the line of its flight
                        float w = 2.5f * px * k * Mathf.Sin(Time.time * 30f);
                        Kit.Line(from + side * w, to - side * w, px, 0.16f, 1, FxLayer.Top, 14, Fx.WithA(Color.white, 0.85f), Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
                        Kit.Line(from - side * w, to + side * w, px, 0.16f, 1, FxLayer.Top, 14, Fx.WithA(h.Main, 0.85f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case School.ThousandBlades:
                    // a thin, bright cut line where it passed, and a silver afterimage
                    Kit.Line(from, to, px, 0.2f, 1, FxLayer.Top, 15, Color.white, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Edge, 0f));
                    if (Random.value < 0.5f)
                    {
                        Trails.Ghost(a, 0.16f, Fx.WithA(h.Main, 0.55f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case School.KiArts:
                    {
                        // a gold stream of light
                        Kit.Line(from, to, px, 0.16f, 2, FxLayer.Top, 13, Fx.WithA(h.Core, 0.85f), Fx.WithA(h.Main, 0.5f), Fx.WithA(h.Edge, 0f));
                        Particle p = Fx.Spawn(Art.Pixel, Vector2.Lerp(from, to, Random.value) + side * Random.Range(-2f, 2f) * px, px, 0.35f, FxLayer.Top, 14);
                        p?.Moving(-n * 10f * px + Vector2.up * 6f * px, 0f, 1f).Color(Color.white, Fx.WithA(h.Main, 0.8f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case School.EarthBreaker:
                    {
                        // the ground churned under it: a furrow of dust and pebbles
                        Color soil = Fx.SoilColor(Combat.TileAt(feet));
                        Kit.Line(feet - d, feet, px, 0.6f, 2, FxLayer.Ground, 4, Fx.WithA(Color.Lerp(soil, Color.black, 0.3f), 0.6f), Fx.WithA(soil, 0f));
                        Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], feet, px, 0.4f, FxLayer.Top, 13);
                        if (p != null)
                        {
                            p.Moving((-n * 12f + Vector2.up * 30f) * px, 0f, 0.4f, 260f * px);
                            p.Color(Color.Lerp(soil, Color.white, 0.2f), soil, Fx.WithA(soil, 0f));
                        }
                    }
                    break;
                case School.PhantomStep:
                    // violet afterimages strung along its path
                    Trails.Ghost(a, 0.22f, Fx.WithA(h.Main, 0.7f), Fx.WithA(Color.Lerp(h.Main, Color.black, 0.5f), 0f));
                    break;
                case School.HuntersWay:
                    // a low green blur at its feet, like a beast running down its prey
                    Kit.Line(feet + Vector2.up * px * 2f - d, feet + Vector2.up * px * 2f, px, 0.14f, 2, FxLayer.Top, 13, Fx.WithA(h.Main, 0.75f), Fx.WithA(h.Edge, 0f));
                    Kit.Line(from, to, px, 0.08f, 1, FxLayer.Top, 14, Fx.WithA(Color.white, 0.6f), Fx.WithA(h.Main, 0f));
                    break;
            }
        }

        // ----------------------------------------------------------------------------------- emblem --

        /// <summary>
        /// A technique's blow lands on 'v' (Combat.Hit): it is stamped with the school's emblem, once per
        /// blow (the many small hits of a storm or a tornado share one stamp a beat).
        /// </summary>
        public static void Stamp(Actor a, Actor v, Move by, float mult)
        {
            if (by == null || by.Tech == null && !(by is Duelist) || a == null || v == null || mult < 0.06f)
            {
                return;
            }
            School school = _st.TryGetValue(by, out State s) ? s.School : Of(by);
            if (school == School.None)
            {
                return;
            }
            long id = v.Sid();
            float now = Time.time;
            if (_stamped.TryGetValue(id, out float last) && now - last < 0.12f)
            {
                return;
            }
            _stamped[id] = now;
            if (_stamped.Count > 400)
            {
                _stamped.Clear();
            }
            Vector2 at = Choreo.BodyAt(v, 0.55f);
            if (!Kit.Visible(at, 0.3f))
            {
                return;
            }
            Hue h = s != null && s.HasHue ? s.Hue : Hue.For(a, school, Mathf.Clamp(by.Level, 1, 3));
            Vector2 d = v.current_position - a.current_position;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
            float px = Choreo.PxOf(v), k = Mathf.Max(0.8f, Mathf.Sqrt(Choreo.KOf(v)));
            float w = Mathf.Clamp(0.7f + mult * 0.6f, 0.7f, 1.6f);
            Emblem(school, at, Choreo.BodyAt(v, 0f), d, px, k, w, h);
        }

        public static void Emblem(School school, Vector2 at, Vector2 feet, Vector2 d, float px, float k, float w, Hue h)
        {
            FxLayer layer = FxLayer.Top;
            Vector2 side = new Vector2(-d.y, d.x);
            switch (school)
            {
                case School.IronFist:
                    {
                        // the fist's print flashes on the body, and a ring of force goes out from it
                        Particle f = Fx.Spawn(ChoreoArt.Fist(Mathf.RoundToInt(5f * w * k)), at - d * 2f * px, px, 0.32f, layer, 21);
                        if (f != null)
                        {
                            f.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                            f.Sized(0.8f, 1.35f).Color(Color.white, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                        }
                        Fx.Ring(at, px, 0.32f, 2f, (7f + 5f * w) * k, 2, 1, layer, 19, 0.5f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(h.Main, 0.8f), Fx.WithA(h.Edge, 0f));
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 o = (d + side * Random.Range(-0.5f, 0.5f)).normalized;
                            Kit.Line(at + o * 3f * px, at + o * (9f + 5f * w) * px * k, px, 0.1f, 1, layer, 20, Color.white, Fx.WithA(h.Main, 0.8f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case School.TempestLeg:
                    {
                        // a crescent of wind sweeps through the body along the kick
                        int r = Mathf.Clamp(Mathf.RoundToInt((6f + 3f * w) * k), 4, 20);
                        Particle c = Fx.Spawn(ChoreoArt.Crescent(r, 2), at - d * 3f * px, px, 0.32f, layer, 21);
                        if (c != null)
                        {
                            c.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                            c.Moving(d * 40f * px * k, 0f, 2f);
                            c.Sized(0.8f, 1.3f).Color(Color.white, Fx.WithA(h.Main, 0.9f), Fx.WithA(h.Edge, 0f));
                        }
                        Particle sw = Fx.Spawn(Art.Arc(Mathf.Clamp(r - 1, 3, 18), 0.55f, 40f, -80f, 1), at, px, 0.2f, layer, 20);
                        if (sw != null)
                        {
                            sw.Rot = Random.Range(0f, 360f);
                            sw.Spin = 1200f;
                            sw.Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(h.Main, 0.7f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case School.ThousandBlades:
                    {
                        // a long cut of light across the body, lingering, a dark gap under it and a glint at its end
                        float a = Mathf.Atan2(d.y, d.x) + Mathf.PI * 0.5f + Random.Range(-0.6f, 0.6f);
                        Vector2 ld = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        float half = (8f + 4f * w) * px * k;
                        Kit.Line(at - ld * half, at + ld * half, px, 0.3f, 2, layer, 21, Color.white, Fx.WithA(h.Core, 0.95f), Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
                        Kit.Line(at - ld * half * 0.85f, at + ld * half * 0.85f, px, 0.4f, 1, layer, 20, Fx.WithA(Color.Lerp(h.Main, Color.black, 0.6f), 0.8f), Fx.WithA(h.Dark, 0f));
                        Fx.Glint(at + ld * half, px, 0.2f, Color.white, 1.2f);
                    }
                    break;
                case School.KiArts:
                    {
                        // a burst of gold ki: a swelling sphere, a ring, motes flung off it
                        int r = Mathf.Clamp(Mathf.RoundToInt((4f + 3f * w) * k), 3, 18);
                        // (gold, not white: the ki's own colour from the first frame)
                        Particle b = Fx.Spawn(Art.Disk(r, 1f, 0.5f), at, px, 0.32f, layer, 20);
                        b?.Sized(0.4f, 1.5f).Color(Color.Lerp(Color.white, h.Main, 0.35f), Fx.WithA(h.Main, 0.9f), Fx.WithA(h.Edge, 0.5f), Fx.WithA(h.Edge, 0f));
                        Fx.Ring(at, px, 0.3f, 2f, (8f + 5f * w) * k, 1, 1, layer, 19, 1f, 2.2f, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                        for (int i = 0; i < 6; i++)
                        {
                            Particle m = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.3f, 0.5f), layer, 21);
                            m?.Moving((Random.insideUnitCircle.normalized + d * 0.5f) * Random.Range(30f, 70f) * px * k, 0f, 3f).Sized(1.8f, 0.6f)
                                .Color(Color.white, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case School.EarthBreaker:
                    {
                        // a star of cracks under the body and rock flying
                        Color soil = Fx.SoilColor(Combat.TileAt(feet));
                        Color crack = Fx.WithA(Color.Lerp(soil, Color.black, 0.55f), 0.85f);
                        for (int i = 0; i < 5; i++)
                        {
                            Vector2 o = new Vector2(Mathf.Cos(i * 1.26f + Random.Range(-0.3f, 0.3f)), Mathf.Sin(i * 1.26f) * 0.5f);
                            Kit.Line(feet, feet + o * (6f + 4f * w) * px * k, px, 0.7f, 1, FxLayer.Ground, 5, crack, Fx.WithA(crack, 0f));
                        }
                        for (int i = 0; i < 5; i++)
                        {
                            Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px * 1.2f, Random.Range(0.4f, 0.6f), layer, 20);
                            if (c == null)
                            {
                                break;
                            }
                            c.Moving((Random.insideUnitCircle * 0.8f + d * 0.6f + Vector2.up) * Random.Range(40f, 75f) * px * k, 0f, 0.5f, 260f * px);
                            c.Spin = Random.Range(-600f, 600f);
                            c.Color(Color.Lerp(soil, h.Main, 0.3f), soil, Fx.WithA(soil, 0f));
                        }
                        Kit.DustRing(feet, px, Combat.TileAt(feet), 5, 26f, 0.45f, k);
                    }
                    break;
                case School.PhantomStep:
                    {
                        // a violet tear through the body and the dark collapsing into it
                        float a = Mathf.Atan2(d.y, d.x) + Mathf.PI * 0.5f + Random.Range(-0.5f, 0.5f);
                        Vector2 ld = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        float half = (7f + 3f * w) * px * k;
                        Color dark = Color.Lerp(h.Main, Color.black, 0.55f);
                        Kit.Line(at - ld * half, at + ld * half, px, 0.3f, 2, layer, 21, Fx.WithA(dark, 0.95f), Fx.WithA(h.Main, 0.7f), Fx.WithA(h.Main, 0f));
                        Kit.Line(at - ld * half * 0.7f + side * px, at + ld * half * 0.7f + side * px, px, 0.2f, 1, layer, 22, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                        Fx.Ring(at, px, 0.24f, (9f + 4f * w) * k, 1f, 1, 1, layer, 19, 1f, 2f, Fx.WithA(h.Main, 0f), Fx.WithA(h.Main, 0.85f), Fx.WithA(dark, 0.9f));
                    }
                    break;
                case School.HuntersWay:
                    {
                        // the hunter's mark: a ring and a cross on the quarry, and three claw rakes
                        Fx.Ring(at, px, 0.32f, (7f + 2f * w) * k, (5f + 2f * w) * k, 1, 1, layer, 19, 1f, 1.4f, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0f));
                        float m = (4f + w) * px * k;
                        Kit.Line(at + new Vector2(-m, 0f), at + new Vector2(m, 0f), px, 0.3f, 1, layer, 20, Fx.WithA(Color.white, 0.95f), Fx.WithA(h.Main, 0f));
                        Kit.Line(at + new Vector2(0f, -m), at + new Vector2(0f, m), px, 0.3f, 1, layer, 20, Fx.WithA(Color.white, 0.95f), Fx.WithA(h.Main, 0f));
                        Vector2 rk = new Vector2(Mathf.Cos(-0.9f), Mathf.Sin(-0.9f));
                        for (int i = -1; i <= 1; i++)
                        {
                            Vector2 o = new Vector2(-rk.y, rk.x) * i * 2f * px;
                            Kit.Line(at + o - rk * m * 1.4f, at + o + rk * m * 1.4f, px, 0.22f, 1, layer, 21, Color.white, Fx.WithA(h.Main, 0.9f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
            }
        }

        // ----------------------------------------------------------------------------------- gather --

        /// <summary>Does this school gather its power as an orb of ki (Ki Arts, and techniques of no school)?</summary>
        public static bool Orb(School s)
        {
            return s == School.KiArts || s == School.None;
        }

        /// <summary>
        /// One beat of a charge in a school that gathers no orb ('k' 0..1 through it): the school's own way
        /// of drawing in its power, at 'hand' (the fist or blade), round 'c' (the body), at 'feet'.
        /// </summary>
        public static void Gather(School school, Vector2 hand, Vector2 c, Vector2 feet, float px, float k, float t, Hue h, Actor target)
        {
            float rr = Mathf.Lerp(10f, 4f, t) * px * k;
            switch (school)
            {
                case School.IronFist:
                    {
                        // the fist glows red-hot and sheds sparks; heat ripples off it
                        int r = Mathf.Clamp(Mathf.RoundToInt((1.5f + 2f * t) * k), 1, 8);
                        Fx.Spawn(Art.Disk(r, 1f, 0.3f), hand, px, 0.06f, FxLayer.Top, 22)?.Color(Color.white, Fx.WithA(h.Main, 0.9f));
                        Kit.Ember(hand, px, 0.3f, new Vector2(Random.Range(-14f, 14f), Random.Range(6f, 26f)) * px, Color.Lerp(h.Main, new Color(1f, 0.65f, 0.2f, 1f), 0.4f));
                        if (Random.value < 0.25f)
                        {
                            Fx.Ring(hand, px, 0.18f, 1f, (5f + 4f * t) * k, 1, 1, FxLayer.Top, 20, 1f, 2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(h.Main, 0f));
                        }
                    }
                    break;
                case School.TempestLeg:
                    {
                        // the wind spirals in round its legs, faster and tighter
                        int r = Mathf.Clamp(Mathf.RoundToInt(rr / px), 3, 18);
                        Particle p = Fx.Spawn(Art.Arc(r, 0.5f, 70f, -60f, 1), Vector2.Lerp(feet, c, Random.value), px, 0.12f, FxLayer.Top, 15);
                        if (p != null)
                        {
                            p.Rot = Random.Range(0f, 360f);
                            p.Spin = 1400f;
                            p.Color(Fx.WithA(Color.white, 0.85f), Fx.WithA(h.Main, 0.6f), Fx.WithA(h.Edge, 0f));
                        }
                        Kit.DustRing(feet, px, Combat.TileAt(feet), 2, -16f, 0.3f, k);
                    }
                    break;
                case School.ThousandBlades:
                    {
                        // light runs up the blade: a line of white growing from the hand, glints along it
                        Vector2 tip = hand + new Vector2(0f, (4f + 8f * t) * px * k);
                        Kit.Line(hand, tip, px, 0.06f, 1, FxLayer.Top, 22, Color.white, Fx.WithA(h.Core, 0.9f));
                        if (Random.value < 0.45f)
                        {
                            Fx.Glint(Vector2.Lerp(hand, tip, Random.value), px, 0.12f, Color.white, 0.8f + 0.5f * t);
                        }
                        if (Random.value < 0.2f)
                        {
                            Vector2 d = Random.insideUnitCircle.normalized;
                            Kit.Line(c + d * rr, c + d * rr * 0.6f, px, 0.08f, 1, FxLayer.Top, 15, Fx.WithA(Color.white, 0.85f), Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case School.EarthBreaker:
                    {
                        // rocks lift off the ground round it and hang trembling
                        Color soil = Fx.SoilColor(Combat.TileAt(feet));
                        Vector2 at = feet + new Vector2(Random.Range(-1f, 1f) * 9f, Random.Range(-1f, 1f) * 3f) * px * k;
                        Particle p = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px * Random.Range(1f, 1.6f), 0.35f, FxLayer.Top, 14);
                        if (p != null)
                        {
                            p.Moving(new Vector2(0f, Random.Range(10f, 22f)) * px * k, 0f, 2f);
                            p.Spin = Random.Range(-200f, 200f);
                            p.Color(Color.Lerp(soil, Color.white, 0.2f), soil, Fx.WithA(soil, 0f));
                        }
                        if (Random.value < 0.3f)
                        {
                            Vector2 o = new Vector2(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f)).normalized;
                            Color crack = Fx.WithA(Color.Lerp(soil, Color.black, 0.5f), 0.8f);
                            Kit.Line(feet, feet + o * (4f + 6f * t) * px * k, px, 0.5f, 1, FxLayer.Ground, 5, crack, Fx.WithA(crack, 0f));
                        }
                    }
                    break;
                case School.PhantomStep:
                    {
                        // the dark thickens round it: violet smoke drawn in, the light dimming at its hand
                        Vector2 o = Random.insideUnitCircle.normalized * rr * 1.4f;
                        Color dark = Color.Lerp(h.Main, Color.black, 0.5f);
                        Particle p = Fx.Spawn(Art.Disk(Random.Range(2, 4), 1f, 0.6f), c + o, px, 0.25f, FxLayer.Top, 14);
                        p?.Moving(-o * 3.5f, 0f, 1f).Color(Fx.WithA(dark, 0f), Fx.WithA(dark, 0.7f), Fx.WithA(h.Main, 0f));
                        int r = Mathf.Clamp(Mathf.RoundToInt((1f + 2.5f * t) * k), 1, 8);
                        Fx.Spawn(Art.Disk(r, 1f, 0.2f), hand, px, 0.06f, FxLayer.Top, 22)?.Color(Fx.WithA(Color.Lerp(dark, Color.black, 0.5f), 0.95f));
                    }
                    break;
                case School.HuntersWay:
                    {
                        // a mark settles on the prey: a ring tightening on it, a cross
                        Vector2 at = target != null && target.isAlive() ? Choreo.BodyAt(target, 0.55f) : c + new Vector2(0f, 6f * px);
                        if (Random.value < 0.35f)
                        {
                            float r = Mathf.Lerp(12f, 5f, t) * k;
                            Fx.Ring(at, px, 0.1f, r, r, 1, 1, FxLayer.Top, 19, 1f, 1f, Fx.WithA(h.Main, 0.95f), Fx.WithA(h.Edge, 0.4f));
                            float m = r * px * 0.6f;
                            Kit.Line(at + new Vector2(-m, 0f), at + new Vector2(m, 0f), px, 0.1f, 1, FxLayer.Top, 20, Fx.WithA(Color.white, 0.9f), Fx.WithA(h.Main, 0f));
                            Kit.Line(at + new Vector2(0f, -m), at + new Vector2(0f, m), px, 0.1f, 1, FxLayer.Top, 20, Fx.WithA(Color.white, 0.9f), Fx.WithA(h.Main, 0f));
                        }
                    }
                    break;
            }
        }
    }
}
