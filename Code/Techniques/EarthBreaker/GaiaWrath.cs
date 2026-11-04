using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GAIA'S WRATH (Earth Breaker, God, Ultimate). The world dims (a cinematic and its title card, when
    /// the settings allow); the God sinks low, palms to the ground, and the land gathers: the earth
    /// trembles, glowing cracks creep out from its feet, pebbles jump. A breath, and it rises tall with
    /// both arms flung to the sky on a spur of rock heaving up under its feet: the dimness lifts and the
    /// land rises with it. A wave races out across 12-16 tiles and stone spears erupt behind it, a
    /// forest of them: one under every enemy in reach (thrown high into the air), and many more all
    /// over the ground, every one breaking the earth it bursts from. At III a colossal spire erupts
    /// under its main foe after the wave. Radius I 12, II 14, III 16 tiles; spears under up to 24 / 32 /
    /// 40 foes. Damage to each foe (x the user's damage): I 2.2, II 2.8, III 3.5 (the spire +1.2); the
    /// spears that rise near one, x0.25 of it.
    /// </summary>
    internal sealed class GaiaWrathTech : Technique
    {
        public override string Id => "gaia_wrath";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Area | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 100f, 145f, 200f };
        public override float[] Cooldown => new[] { 60f, 65f, 70f };

        public static float Radius(int level)
        {
            return EarthKit.L(level, 12f, 14f, 16f);
        }

        public static float Mult(int level)
        {
            return EarthKit.L(level, 2.2f, 2.8f, 3.5f);
        }

        private static float Lift(int level)
        {
            return EarthKit.L(level, 3f, 3.6f, 4.2f);
        }

        private static int MaxFoes(int level)
        {
            return EarthKit.L(level, 24, 32, 40);
        }

        private const float WaveSpeed = 16f;
        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            _near.Clear();
            c.CountNear(c.Pos, r, _near);
            float score = 0f;
            int n = 0;
            Actor best = null;
            float bestD = float.MaxValue;
            int max = MaxFoes(level);
            for (int i = 0; i < _near.Count && n < max; i++)
            {
                Actor v = _near[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                n++;
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
                float d = (v.current_position - c.Pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                // the land rises for an army, or for a foe that threatens it
                int need = level >= 2 ? 4 : 5;
                if (n < need && !(n >= 2 && c.ThreatAround >= 1.5f))
                {
                    return null;
                }
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        /// <summary>The spur of rock under its feet (it rises and sinks with the God).</summary>
        private sealed class Spur
        {
            public Particle P;
            public float H;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            Spur spur = new Spur();
            m.OpenCinematic(8f, 0.5f, "sakuga_title_gaia_wrath");
            // it sinks low, palms to the ground: the land gathers
            Beat gather = new Beat { Tempo = Tempo.Wind, Dur = 0.34f, To = new Shape(8f, 1.16f, 0.84f, 0f, 0.04f), Ease = Ease.OutQuad, Track = false };
            gather.Enter = mm => Choreo.Sound("event:/SFX/NATURE/EarthQuake", mm.A.current_tile, 0.3f);
            float tickT = 0f;
            gather.Every = (mm, k) =>
            {
                tickT -= Clock.Dt;
                if (tickT <= 0f)
                {
                    tickT = 0.06f;
                    Gathering(mm, k);
                }
            };
            m.Add(gather);
            // the land holds its breath
            m.Hold(0.2f, true, Tempo.Wind);
            // it rises tall, arms flung to the sky: the land rises with it
            Beat rise = new Beat { Tempo = Tempo.Act, Dur = 0.12f, To = new Shape(-14f, 0.84f, 1.28f, 0.28f), Ease = Ease.OutBack, Track = false };
            rise.Enter = mm => Erupt(mm, spur);
            rise.Every = (mm, k) => Raise(mm, spur);
            m.Add(rise);
            m.Do(mm => mm.EndCinematic());
            Beat stand = new Beat { Tempo = Tempo.Impact, Dur = 0.4f, To = new Shape(-10f, 0.88f, 1.22f, 0.3f), Ease = Ease.OutQuad, Tremble = true, Track = false };
            stand.Every = (mm, k) => Raise(mm, spur);
            m.Add(stand);
            m.Recover(0.35f).Every((mm, k) => Raise(mm, spur));
            return m.Start();
        }

        /// <summary>The spur under its feet follows how high it stands.</summary>
        private static void Raise(ChoreoMove m, Spur spur)
        {
            Particle p = spur.P;
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = m.Pose.Ground + new Vector2(0f, -0.05f * m.Kk);
            p.SY = Mathf.Clamp01(m.Pose.Height / Mathf.Max(0.05f, spur.H));
        }

        /// <summary>The land gathering: cracks glowing out from its feet, pebbles jumping, the ground rumbling, motes rising into it.</summary>
        private static void Gathering(ChoreoMove m, float k)
        {
            Ruin.Quake(m.A, m.Pose.Ground, Scale.Casual, 0.6f + k, m.Kk);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            float a = Random.value * Mathf.PI * 2f;
            Vector2 g = m.Pose.Ground;
            Vector2 end = g + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Mathf.Lerp(1f, 3.5f, k) * m.Kk;
            EarthKit.Crack(g, end, px, 1.2f, m.Hue.Edge);
            float rr = Radius(m.Level) * Mathf.Lerp(0.2f, 0.6f, k);
            Vector2 q = g + Random.insideUnitCircle * rr;
            Kit.Debris(q, px, Combat.TileAt(q), 1, m.Kk * 0.5f);
            ChoreoFx.Motes(m.BodyPos(), px, 2, 18f * m.Kk, 0.2f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
            if (k > 0.5f)
            {
                Kit.DustRing(g, px, m.A.current_tile, 4, -16f, 0.35f, m.Kk);
            }
        }

        private static readonly List<Actor> _foes = new List<Actor>();
        private static readonly List<Vector2> _spots = new List<Vector2>();
        private static Vector2 _from;
        private static readonly Comparison<Actor> ByDist = (x, y) => (x.current_position - _from).sqrMagnitude.CompareTo((y.current_position - _from).sqrMagnitude);

        /// <summary>The land rises: a spear under every enemy in reach, a forest of them all over the ground, a wave racing out.</summary>
        private static void Erupt(ChoreoMove m, Spur spur)
        {
            int level = m.Level;
            float k = m.Kk, px = m.Px;
            Vector2 c = m.Pose.Ground;
            float r = Radius(level) * Mathf.Lerp(1f, k, 0.5f);
            WorldTile tile = m.A.current_tile;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            Ruin.Blast(m.A, c, 1.4f, m.Scale, c);
            Ruin.Quake(m.A, c, m.Scale, 1.5f, k);
            Kit.Stop(c, 0.06f * m.ImpactK);
            if (m.Vis)
            {
                Kit.ImpactFrame(m.BodyPos(), px, 0.7f * k, m.Hue.Pal, level >= 3, FxLayer.Behind);
                Kit.DustRing(c, px, tile, 24, 80f, 0.7f, k * 1.3f);
                Kit.Debris(c, px, tile, 12, k);
                Fx.Ring(c, px, r / WaveSpeed, 4f, r / px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Edge, 0.75f), Fx.WithA(m.Hue.Rim, 0f));
                Fx.Ring(c, px, r / WaveSpeed * 0.9f, 3f, r / px * 0.9f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1f,
                    Fx.WithA(Fx.Darken(Fx.SoilColor(tile), 0.3f), 0.9f), Fx.WithA(Fx.Darken(Fx.SoilColor(tile), 0.3f), 0f));
                // the spur of rock that lifts it
                spur.H = 0.3f * k;
                int w = Mathf.RoundToInt(m.Hpx * 0.9f);
                int hp = Mathf.Max(4, Mathf.RoundToInt(spur.H / px) + 2);
                spur.P = m.Glue(Fx.Spawn(ChoreoArt.Pillar(w, hp, 1), c, px, 1f, FxLayer.Behind, 4));
                if (spur.P != null)
                {
                    spur.P.SY = 0f;
                    spur.P.Grad = null;
                    spur.P.R.color = EarthKit.Stone(c);
                }
            }
            // a spear under every enemy in reach, nearest first
            _foes.Clear();
            Choreo.Foes(m.A, c, r, _foes);
            _from = c;
            _foes.Sort(ByDist);
            _spots.Clear();
            int max = MaxFoes(level);
            int n = 0;
            float h = EarthKit.L(level, 2.2f, 2.6f, 3f) * k;
            for (int i = 0; i < _foes.Count && n < max; i++)
            {
                Actor u = _foes[i];
                if (!Choreo.Fair(m.A, u, out _, true) || u.isInLiquid() && !Choreo.Wading(u.current_position))
                {
                    continue;
                }
                Vector2 at = u.current_position;
                float d = Vector2.Distance(c, at);
                float delay = 0.04f + d / WaveSpeed;
                // a giant gets a spear its size
                float uh = Choreo.SpritePx(u) * Choreo.PxOf(u);
                PillarHazard ph = new PillarHazard(m, at, delay, Mathf.Max(h, uh * 1.2f), Mult(level), Lift(level), true);
                ph.Stand = Mathf.Max(0.8f, uh * 0.35f);
                Areas.Add(ph);
                Combat.Pin(u, delay + 0.3f);
                _spots.Add(at);
                n++;
            }
            // and the land all around bristles with them
            int deco = Fx.Count(EarthKit.L(level, 24, 34, 46));
            for (int i = 0; i < deco; i++)
            {
                float rho = Mathf.Lerp(1.4f * k, r, Mathf.Sqrt(Random.value));
                float a = Random.value * Mathf.PI * 2f;
                Vector2 at = Combat.Clamp(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rho);
                if (!Combat.SafeSpot(at) || Near(at, 1f))
                {
                    continue;
                }
                _spots.Add(at);
                float delay = 0.04f + rho / WaveSpeed + Random.Range(0f, 0.08f);
                Areas.Add(new PillarHazard(m, at, delay, Random.Range(0.9f, 2f) * k, Mult(level) * 0.25f, 1.2f, true));
            }
            // III: the colossal spire under its main foe, after the wave
            if (level >= 3 && m.VOk && Combat.Grounded(m.V))
            {
                Vector2 at = m.VG;
                float delay = 0.3f + Vector2.Distance(c, at) / WaveSpeed;
                float uh = Choreo.SpritePx(m.V) * Choreo.PxOf(m.V);
                PillarHazard spire = new PillarHazard(m, at, delay, Mathf.Max(4.5f * k, uh * 2f), 1.2f, 4.5f, true);
                spire.Stand = Mathf.Max(1.2f, uh * 0.45f);
                Areas.Add(spire);
            }
            m.SetLink(Link.Scattered, null);
            if (Dev.On) Dev.Trace("gaia_wrath " + Techniques.Roman(level) + ": spears under " + n + " foes, " + (_spots.Count - n) + " more, radius " + r.ToString("F1"));
        }

        private static bool Near(Vector2 p, float r)
        {
            float r2 = r * r;
            for (int i = 0; i < _spots.Count; i++)
            {
                if ((_spots[i] - p).sqrMagnitude < r2)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
