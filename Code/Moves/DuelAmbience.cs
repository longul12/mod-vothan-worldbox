using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The world feels a duel of the great. While two Gods fight, the sky over them storms: bolts of
    /// lightning (only light and scorch, the colours of both their schools) strike the land round them,
    /// rocks tear loose and rise into the air and fall again, the ground keeps cracking open in lines
    /// running out from them and wind tears across the field. Two Grandmasters only make the ground
    /// tremble: pebbles hopping, dust driven off in gusts, a crack now and then. Purely the world's
    /// reaction (no damage to anyone; the cracks scorch and dig at most a little).
    /// </summary>
    internal static class DuelAmbience
    {
        private sealed class State
        {
            public float Bolt, Rock, Crack, Gust;
        }

        private static readonly Dictionary<Duel, State> _st = new Dictionary<Duel, State>();

        /// <summary>The world was cleared: its duels (and the weather each one kept) go with it.</summary>
        public static void Clear()
        {
            _st.Clear();
        }

        public static void Tick(Duel d, float dt)
        {
            if (!_st.TryGetValue(d, out State s))
            {
                if (_st.Count > 16)
                {
                    _st.Clear();
                }
                _st[d] = s = new State { Bolt = Random.Range(0.4f, 1.2f), Rock = 0.2f, Crack = Random.Range(1f, 2f), Gust = 0.5f };
            }
            bool gods = d.Gods;
            float px = d.Px, k = d.K;
            Vector2 mid = d.Mid;
            float span = (gods ? 9f : 6f) * k;
            School sa = Schools.PrimaryOf(d.A), sb = Schools.PrimaryOf(d.B);
            Hue ha = Hue.For(d.A, sa, 3), hb = Hue.For(d.B, sb, 3);
            s.Rock -= dt;
            if (s.Rock <= 0f)
            {
                // rocks tearing loose and rising round the fight
                s.Rock = gods ? 0.08f : 0.2f;
                Vector2 at = Combat.Clamp(mid + Random.insideUnitCircle * span);
                Color soil = Fx.SoilColor(Combat.TileAt(at));
                Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at, px * Random.Range(1f, gods ? 2.2f : 1.4f), Random.Range(0.8f, 1.4f), FxLayer.Top, 9);
                if (c != null)
                {
                    c.Moving(new Vector2(Random.Range(-3f, 3f), Random.Range(gods ? 22f : 12f, gods ? 40f : 22f)) * px * k, 0f, 1.6f, gods ? 30f * px : 120f * px);
                    c.Spin = Random.Range(-200f, 200f);
                    c.Color(Color.Lerp(soil, Color.white, 0.15f), soil, Fx.WithA(soil, 0f));
                }
            }
            s.Gust -= dt;
            if (s.Gust <= 0f)
            {
                s.Gust = gods ? 0.25f : 0.5f;
                Vector2 a = Combat.Clamp(mid + Random.insideUnitCircle * span);
                Vector2 dir = new Vector2(Random.value < 0.5f ? -1f : 1f, Random.Range(-0.2f, 0.2f)).normalized;
                Kit.Line(a, a + dir * Random.Range(1.5f, 3f) * k, px, 0.25f, 1, FxLayer.Top, 8, Fx.WithA(Color.white, 0.45f), Fx.WithA(Color.white, 0f));
                Kit.DustRing(a, px, Combat.TileAt(a), 2, 18f, 0.4f, k);
            }
            s.Crack -= dt;
            if (s.Crack <= 0f)
            {
                // the ground keeps splitting in lines running out from the fight
                s.Crack = gods ? Random.Range(0.6f, 1.3f) : Random.Range(1.8f, 3.2f);
                Vector2 dir = Random.insideUnitCircle.normalized;
                Vector2 from = Combat.Clamp(mid + dir * Random.Range(0.5f, 2f) * k);
                Vector2 to = Combat.Clamp(from + dir * Random.Range(2f, gods ? 6f : 3.5f) * k);
                DuelFx.Fissure(from, to, px, Combat.TileAt(from), Random.value < 0.5f ? ha.Main : hb.Main, gods ? 2 : 1);
                if (gods && Settings.Destruction >= 1)
                {
                    Kit.Trench(from, to, 0.6f, 2, null, false, null, 0f, d.A, d.B);
                }
            }
            if (!gods)
            {
                return;
            }
            s.Bolt -= dt;
            if (s.Bolt <= 0f)
            {
                // lightning out of the storm over them, in their schools' colours
                s.Bolt = Random.Range(0.5f, 1.4f);
                Vector2 at = Combat.Clamp(mid + Random.insideUnitCircle * span);
                Color col = Random.value < 0.5f ? ha.Main : hb.Main;
                Vector2 sky = at + new Vector2(Random.Range(-2f, 2f) * k, 16f * k);
                Spec.Bolt(sky, at, px, 0.18f, col, FxLayer.Top, 24);
                Kit.Flash(at, px, Mathf.RoundToInt(5f * k), 0.08f, Color.white);
                DuelFx.GroundRing(at, px, 2f, 10f * k, 0.25f, 2, 1, Color.white, Fx.WithA(col, 0f));
                if (Settings.Destruction >= 1)
                {
                    Kit.Ravage(at, 0.8f, 1, null, false, null, 0f, d.A, d.B);
                }
                if (Random.value < 0.3f)
                {
                    CineKit.ViewFlash(Fx.WithA(Color.white, 0.18f), 0.08f);
                }
                Choreo.Sound("event:/SFX/HIT/HitGeneric", Combat.TileAt(at), 0.1f);
            }
        }
    }
}
