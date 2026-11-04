using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ATLAS LIFT (Earth Breaker, God). Made for giants and for towns: it drops into a deep squat and
    /// plunges its hands into the earth in front of it; a ring of glowing cracks outlines a slab of
    /// ground (the enemies standing on it held where they stand, the ground shuddering), and with a
    /// heave it tears the whole slab out (the buildings and trees on it torn out with it, a pit left
    /// behind) and lifts it over its head like Atlas holding up the world, trembling, its feet sinking.
    /// It draws it back and hurls it 4-14 tiles at its target (a kaiju, a titan, a crowd), the slab
    /// tumbling through the air with the foes still clinging to it: it crashes down and shatters into
    /// boulders, crushing whatever it lands on (driven into the earth), a crater and fissures, the
    /// riders thrown off, a shockwave knocking down everyone around. A giant on the slab is not lifted:
    /// the ground is torn from under its feet. Slab radius I 1.8, II 2.3, III 2.8 tiles. Damage (x the
    /// user's damage): the crash I 2.4, II 3, III 3.8 (less towards its rim); the riders 0.3 as the ground
    /// gives way and 1 / 1.2 / 1.5 when they are thrown off; the shockwave 0.5.
    /// </summary>
    internal sealed class AtlasLiftTech : Technique
    {
        public override string Id => "atlas_lift";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Grab | Form.Heavy;
        public override Prey Prey => Prey.Kaiju | Prey.Titan | Prey.Building;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 70f, 100f, 140f };
        public override float[] Cooldown => new[] { 35f, 40f, 45f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Scattered;
        }

        public static float SlabR(int level)
        {
            return EarthKit.L(level, 1.8f, 2.3f, 2.8f);
        }

        public static float CrashMult(int level)
        {
            return EarthKit.L(level, 2.4f, 3f, 3.8f);
        }

        public static float RiderMult(int level)
        {
            return EarthKit.L(level, 1f, 1.2f, 1.5f);
        }

        public const float TearMult = 0.3f, ShockMult = 0.5f;
        private const float MinRange = 3.5f, MaxRange = 14f;
        private const int MaxRiders = 6;

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Mathf.Sqrt(Choreo.KOf(c.Self));
            float rs = SlabR(level) * k;
            float crashR = rs + 0.7f;
            Actor best = null;
            float bestScore = 0f;
            int bestN = 0;
            for (int i = 0; i < c.Enemies.Count && i < 12; i++)
            {
                Actor t = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d < (c.Forced ? 2.5f : MinRange) || d > (c.Forced ? MaxRange + 4f : MaxRange))
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, t, out float tcm, c.Forced) || t.isFlying() || !Combat.Grounded(t))
                {
                    continue;
                }
                if (!Combat.SafeSpot(t.current_position) && !Choreo.Wading(t.current_position))
                {
                    continue;
                }
                if (!Source(c.Pos, t.current_position, rs, out Vector2 src))
                {
                    continue;   // no slab of land to tear up here (water, a cliff), or none clear of the target
                }
                // what it lands on
                _near.Clear();
                c.CountNear(t.current_position, crashR, _near);
                if (!_near.Contains(t))
                {
                    _near.Add(t);
                }
                float score = 0f;
                int n = 0;
                bool giant = false;
                for (int j = 0; j < _near.Count; j++)
                {
                    Actor u = _near[j];
                    if (!c.Allowed(u, out float cm))
                    {
                        continue;
                    }
                    score += c.Value(u, c.Expected(u, CrashMult(level)), cm);
                    n++;
                    if (Preys.Is(u, Prey.Kaiju | Prey.Titan) || Weights.Species(u) >= Weight.Titan)
                    {
                        giant = true;
                    }
                }
                // what rides the slab
                _near.Clear();
                c.CountNear(src, rs, _near);
                int riders = 0;
                for (int j = 0; j < _near.Count && riders < MaxRiders; j++)
                {
                    Actor u = _near[j];
                    if (u == t || !c.Allowed(u, out float cm))
                    {
                        continue;
                    }
                    score += c.Value(u, c.Expected(u, TearMult + RiderMult(level)), cm);
                    riders++;
                }
                // the enemy's buildings it tears out, and those it lands on
                int built = EnemyBuildings(c.Self, src, rs) + EnemyBuildings(c.Self, t.current_position, crashR);
                score += Mathf.Min(6, built) * 15f;
                if (!c.Forced && !giant && n < 2 && riders == 0 && built == 0)
                {
                    continue;   // made for giants, towns and crowds: not for one soldier
                }
                if (best == null || score > bestScore)
                {
                    best = t;
                    bestScore = score;
                    bestN = n + riders;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 bd = (best.current_position - c.Pos).normalized;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = bd,
                Count = bestN,
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                AreaRadius = crashR,
                Data = c.Forced
            };
        }

        /// <summary>
        /// Where it tears the slab up: in front of it towards the target, else at one of its sides, else
        /// behind it; solid land, and clear of the target (it is thrown at it, not torn from under it).
        /// </summary>
        public static bool Source(Vector2 pos, Vector2 target, float rs, out Vector2 src)
        {
            Vector2 d = target - pos;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
            Vector2 side = new Vector2(-dir.y, dir.x);
            float off = rs + 0.7f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 way = i == 0 ? dir : i == 1 ? side : i == 2 ? -side : -dir;
                src = Combat.Clamp(pos + way * off);
                if (SolidGround(src, rs) && Vector2.Distance(src, target) > rs + 0.8f)
                {
                    return true;
                }
            }
            src = pos;
            return false;
        }

        /// <summary>Is there land to tear up round 'at' (its middle and four points out to most of 'r': solid, dry ground)?</summary>
        private static bool SolidGround(Vector2 at, float r)
        {
            if (!Combat.SafeSpot(at))
            {
                return false;
            }
            float q = r * 0.6f;
            return Combat.SafeSpot(at + new Vector2(q, 0f)) && Combat.SafeSpot(at - new Vector2(q, 0f)) && Combat.SafeSpot(at + new Vector2(0f, q))
                && Combat.SafeSpot(at - new Vector2(0f, q));
        }

        /// <summary>How many buildings of 'a's enemies stand within 'r' of 'at'.</summary>
        private static int EnemyBuildings(Actor a, Vector2 at, float r)
        {
            if (a == null || a.kingdom == null || World.world == null)
            {
                return 0;
            }
            int n = 0;
            int ri = Mathf.CeilToInt(r);
            WorldTile c = Combat.TileAt(at);
            if (c == null)
            {
                return 0;
            }
            _seen.Clear();
            for (int dx = -ri; dx <= ri; dx++)
            {
                for (int dy = -ri; dy <= ri; dy++)
                {
                    if (dx * dx + dy * dy > r * r)
                    {
                        continue;
                    }
                    WorldTile t = World.world.GetTile(c.x + dx, c.y + dy);
                    Building b = t != null ? t.building : null;
                    if (b == null || !b.isAlive() || b.kingdom == null || _seen.Contains(b))
                    {
                        continue;
                    }
                    _seen.Add(b);
                    if (b.kingdom != a.kingdom && a.kingdom.isEnemy(b.kingdom))
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        private static readonly List<Building> _seen = new List<Building>();

        // ------------------------------------------------------------- the slab --

        /// <summary>The slab of earth the move tears up, lifts and throws, and what rides on it.</summary>
        private sealed class Slab
        {
            public Vector2 Src, G, Dest, From;
            public float R, H, FromH, Over, Rot, Flight, Apex;
            public Particle P;
            public readonly List<Victim> Riders = new List<Victim>();
            public readonly List<Vector2> Offsets = new List<Vector2>();
            public readonly List<Vector2> Starts = new List<Vector2>();
            public readonly List<Particle> Ghosts = new List<Particle>();
            public readonly List<Vector2> GhostOff = new List<Vector2>();
            public bool Lifted, Crashed;
            public Color Soil;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Slab st = new Slab { R = SlabR(level) * Mathf.Sqrt(m.Kk), Dest = p.Point };
            if (!Source(m.Pose.Ground, p.Point, st.R, out st.Src))
            {
                st.Src = Combat.Clamp(m.Pose.Ground + p.Dir * (st.R + 0.7f));
            }
            Vector2 aim = p.Dir;
            // it turns to the ground it tears up (and back to its target before the throw)
            m.Face(st.Src - m.Pose.Ground);
            m.Ended2 = (mm, aborted) => Gone(mm, st);
            // down: a deep squat, its hands plunged into the earth in front of it
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.16f, To = new Shape(22f, 1.18f, 0.82f, 0f, 0.18f), Ease = Ease.OutQuad, Track = false, Forms = Form.Ground | Form.Grab });
            m.Do(mm => Dig(mm, st));
            // it strains: the outline glows, the ground shudders, the riders sway
            Beat strain = new Beat { Tempo = Tempo.Free, Dur = 0.24f, Tremble = true, Track = false, Forms = Form.Ground | Form.Grab | Form.Heavy };
            float rumbleT = 0f;
            strain.Every = (mm, k) =>
            {
                Ride(mm, st, 0f);
                rumbleT -= Clock.Dt;
                if (rumbleT > 0f)
                {
                    return;
                }
                rumbleT = 0.07f;
                Ruin.Quake(mm.A, st.Src, Scale.Casual, 0.8f + k, mm.Kk);
                if (mm.Vis)
                {
                    float an = Random.value * Mathf.PI * 2f;
                    Vector2 q = st.Src + new Vector2(Mathf.Cos(an), Mathf.Sin(an) * Art.Ground) * st.R;
                    ChoreoFx.HeelDust(q, q - st.Src, mm.Px, Combat.TileAt(q), mm.Kk);
                    if (Random.value < 0.5f)
                    {
                        Kit.Debris(q, mm.Px, Combat.TileAt(q), 1, mm.Kk * 0.6f);
                    }
                }
            };
            m.Add(strain);
            // the heave: the slab tears free and rises over its head
            Beat heave = new Beat { Tempo = Tempo.Act, Dur = 0.42f, To = new Shape(-10f, 0.86f, 1.2f, 0f), Ease = Ease.OutCubic, Track = false, Forms = Form.Ground | Form.Grab | Form.Heavy };
            heave.Enter = mm => Tear(mm, st);
            heave.Every = (mm, k) =>
            {
                float e = Beat.Apply(Ease.OutCubic, k);
                Place(mm, st, Vector2.Lerp(st.Src, Overhead(mm), e), Mathf.Lerp(0f, st.Over, e) + 0.6f * Mathf.Sin(Mathf.PI * k) * mm.Kk, 0f);
                Ride(mm, st, k);
                if (mm.Vis && Random.value < 0.35f && st.P != null)
                {
                    Kit.Debris(st.G, mm.Px, Combat.TileAt(st.G), 1, mm.Kk * 0.6f, st.H * 0.6f);
                }
            };
            m.Add(heave);
            // Atlas holds up the world: trembling, its feet sinking
            Beat hold = new Beat { Tempo = Tempo.Impact, Dur = 0.2f, Tremble = true, Track = false };
            hold.Enter = mm =>
            {
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
                if (mm.Vis)
                {
                    Kit.Crater(mm.Pose.Ground, mm.Px * mm.Kk, tile, false, 5f, true, mm.Hue.Edge);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 10, 36f, 0.5f, mm.Kk);
                }
                Ruin.Quake(mm.A, mm.Pose.Ground, Scale.Heavy, 0.9f, mm.Kk);
            };
            hold.Every = (mm, k) =>
            {
                Place(mm, st, Overhead(mm), st.Over + Choreo.Tremble(mm.BeatT, 0.5f).x * mm.Px, 0f);
                Ride(mm, st, 1f);
            };
            m.Add(hold);
            // drawn back...
            Beat wind = new Beat { Tempo = Tempo.Wind, Dur = 0.14f, To = new Shape(-24f, 0.9f, 1.12f, 0f, -0.06f), Ease = Ease.OutQuad, Track = false };
            wind.Enter = mm => mm.Face(mm.VOk ? mm.VG - mm.Pose.Ground : aim);
            wind.Every = (mm, k) =>
            {
                float e = Beat.Apply(Ease.OutQuad, k);
                Place(mm, st, Vector2.Lerp(Overhead(mm), mm.Pose.Ground - mm.Dir * 0.4f * mm.Kk, e), st.Over + 0.2f * e * mm.Kk, -8f * e * mm.Fwd);
                Ride(mm, st, 1f);
            };
            m.Add(wind);
            // ...and hurled
            Beat hurl = new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(28f, 1.24f, 0.84f, 0f, 0.16f), Ease = Ease.OutExpo, Track = false, Forms = Form.Ground | Form.Throw | Form.Heavy };
            hurl.Every = (mm, k) =>
            {
                float e = Beat.Apply(Ease.OutExpo, k);
                Place(mm, st, Vector2.Lerp(mm.Pose.Ground - mm.Dir * 0.4f * mm.Kk, mm.Pose.Ground + mm.Dir * 0.8f * mm.Kk, e), st.Over + 0.2f * mm.Kk, Mathf.Lerp(-8f, 6f, e) * mm.Fwd);
                Ride(mm, st, 1f);
            };
            hurl.Exit = mm => Release(mm, st);
            m.Add(hurl);
            // it watches it fly: the crash
            Beat watch = new Beat { Tempo = Tempo.Free, Dur = 0.6f, To = new Shape(16f, 1.1f, 0.92f, 0f, 0.1f), Ease = Ease.OutQuad, Track = false };
            watch.Enter = mm => mm.BeatDur = st.Lifted ? st.Flight : 0f;
            watch.Every = (mm, k) =>
            {
                if (!st.Lifted || st.Crashed)
                {
                    return;
                }
                float u = Mathf.Clamp01(k);
                Vector2 g = Vector2.Lerp(st.From, st.Dest, Beat.Apply(Ease.InOutSine, u));
                float h = Mathf.Lerp(st.FromH, 0f, u) + st.Apex * 4f * u * (1f - u);
                Place(mm, st, g, h, st.Rot * u);
                Ride(mm, st, 1f);
            };
            watch.Exit = mm => Crash(mm, st);
            m.Add(watch);
            m.Recover(0.35f);
            return m.Start();
        }

        /// <summary>Where the slab sits when it is held over its head.</summary>
        private static Vector2 Overhead(ChoreoMove m)
        {
            return m.Pose.Ground + m.Dir * 0.1f * m.Kk;
        }

        /// <summary>Moves the slab (its top surface's middle over ground 'g', 'h' high, tilted 'rot').</summary>
        private static void Place(ChoreoMove m, Slab st, Vector2 g, float h, float rot)
        {
            st.G = g;
            st.H = Mathf.Max(0f, h);
            Particle p = st.P;
            if (p != null && p.Alive)
            {
                p.Pos = g + new Vector2(0f, st.H);
                p.Rot = rot;
                p.Grad = null;
                p.R.color = Color.white;
            }
            for (int i = 0; i < st.Ghosts.Count; i++)
            {
                Particle q = st.Ghosts[i];
                if (q != null && q.Alive)
                {
                    q.Pos = p != null ? p.Pos + Choreo.Rotate(st.GhostOff[i], rot) : g + st.GhostOff[i] + new Vector2(0f, st.H);
                    q.Rot = rot;
                }
            }
        }

        /// <summary>The riders stand on the slab (blending from where they stood as it starts to rise, 'k').</summary>
        private static void Ride(ChoreoMove m, Slab st, float k)
        {
            float blend = Beat.Apply(Ease.OutQuad, Mathf.Clamp01(k * 3f));
            for (int i = 0; i < st.Riders.Count; i++)
            {
                Victim b = st.Riders[i];
                if (b == null || b.Done || b.Holder != m)
                {
                    continue;
                }
                Pose q = b.P.Pose;
                Vector2 off = st.Offsets[i];
                Vector2 on = new Vector2(st.G.x + off.x * 0.85f, st.G.y + off.y * 0.2f);
                float h = st.H - off.y * 0.2f;
                q.Ground = Vector2.Lerp(st.Starts[i], on, blend);
                q.Height = Mathf.Lerp(0f, Mathf.Max(0f, h), blend);
                q.Rot = (st.P != null && st.P.Alive ? st.P.Rot * (q.FaceRight && b.A.asset.can_flip ? -1f : 1f) : 0f) + 4f * Mathf.Sin(Time.time * 19f + i);
                q.Nudge = Choreo.Tremble(Time.time + i * 0.1f, 0.4f);
                q.SX = q.SY = 1f;
                q.HideShadow = q.Height > 0.08f;
                q.Hidden = false;
                b.P.Pose = q;
            }
        }

        private static readonly List<Actor> _foes = new List<Actor>();

        /// <summary>Its hands bite into the earth: the slab's outline cracks and glows, the foes on it are held.</summary>
        private static void Dig(ChoreoMove m, Slab st)
        {
            float px = m.Px, k = m.Kk;
            Vector2 src = st.Src;
            WorldTile tile = Combat.TileAt(src);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", m.A.current_tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            Ruin.Quake(m.A, src, Scale.Heavy, 1f, k);
            m.HoldFor(0.04f * m.ImpactK);
            if (m.Vis)
            {
                Vector2 hands = m.Pose.Ground + m.Dir * 0.35f * k;
                Kit.DustRing(hands, px, m.A.current_tile, 8, 30f, 0.45f, k);
                Kit.Debris(hands, px, m.A.current_tile, 5, k);
                // the outline: a ring of glowing cracks round the slab
                int n = 12;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    Vector2 p0 = src + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0) * Art.Ground) * st.R;
                    Vector2 p1 = src + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1) * Art.Ground) * st.R;
                    EarthKit.Crack(p0, p1, px, 1.2f, m.Hue.Edge);
                }
                Fx.Ring(src, px, 0.4f, st.R / px * 1.15f, st.R / px, 1, 2, FxLayer.Ground, 6, Art.Ground, 1.5f,
                    Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Core, 0.9f));
            }
            // the enemies standing on it: held where they stand (a giant can't be lifted: the ground goes from under it)
            _foes.Clear();
            Choreo.Foes(m.A, src, st.R, _foes);
            for (int i = 0; i < _foes.Count; i++)
            {
                Actor u = _foes[i];
                if (u == m.V || !Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                Combat.Hit(m.A, u, TearMult * cm, false, false, 0f, m);
                Victims.Flash(u, Color.white, 0.05f);
                Victim b = st.Riders.Count < MaxRiders && Victims.Movable(u, m.A) && Choreo.Free(u) ? Victims.Grab(u, m) : null;
                if (b == null)
                {
                    Victims.Hurt(u, m.A, m, Impact.Of(Hurt.Stagger, u.current_position - src, 0f, 1.3f));
                    continue;
                }
                st.Riders.Add(b);
                st.Offsets.Add(u.current_position - src);
                st.Starts.Add(b.P.Pose.Ground);
                Combat.Pin(u, 3f);
            }
            if (m.VOk)
            {
                Combat.Pin(m.V, 3f);
            }
        }

        private sealed class Snap
        {
            public Building B;
            public Sprite S;
            public Vector2 Pos;
            public Vector3 Scale;
        }

        private static readonly List<Snap> _snaps = new List<Snap>();

        /// <summary>The slab tears free: the pit left behind, the buildings and trees on it torn out with it.</summary>
        private static void Tear(ChoreoMove m, Slab st)
        {
            float px = m.Px, k = m.Kk;
            Vector2 src = st.Src;
            WorldTile tile = Combat.TileAt(src);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.05f);
            // the ground's colours, before it is torn up (the slab is made of them)
            Color top = EarthKit.Quantized(Fx.TopColor(tile));
            st.Soil = EarthKit.Quantized(Fx.SoilColor(tile));
            // what stands on it (to see what the tearing takes along)
            _snaps.Clear();
            if (World.world != null && tile != null)
            {
                int ri = Mathf.CeilToInt(st.R);
                for (int dx = -ri; dx <= ri && _snaps.Count < 10; dx++)
                {
                    for (int dy = -ri; dy <= ri && _snaps.Count < 10; dy++)
                    {
                        if (dx * dx + dy * dy > st.R * st.R)
                        {
                            continue;
                        }
                        WorldTile t = World.world.GetTile(tile.x + dx, tile.y + dy);
                        Building b = t != null ? t.building : null;
                        if (b == null || !b.isAlive() || !b.hasHealth() || Has(b))
                        {
                            continue;
                        }
                        Sprite s = null;
                        try
                        {
                            s = b.getLastColoredSprite();
                        }
                        catch
                        {
                            // (a building that never drew: no picture to take along)
                        }
                        _snaps.Add(new Snap { B = b, S = s, Pos = b.cur_transform_position, Scale = b.getCurrentScale() });
                    }
                }
            }
            Ruin.Blast(m.A, src, st.R / 1.95f, Scale.Heavy, m.Pose.Ground, false);
            // what the tearing broke rides away on the slab (its ruins go with it)
            float hs = st.R * 0.85f;
            for (int i = 0; i < _snaps.Count; i++)
            {
                Snap sn = _snaps[i];
                Building b = sn.B;
                if (b == null || b.isAlive() && b.hasHealth() && !b.isRuin())
                {
                    continue;   // it stood the blast: it stays at the pit's edge
                }
                try
                {
                    if (b.isAlive())
                    {
                        b.startDestroyBuilding();
                    }
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("atlas_lift ruins", e);
                }
                if (sn.S == null || !m.Vis || st.Ghosts.Count >= 8)
                {
                    continue;
                }
                Particle q = m.Glue(Fx.Spawn(sn.S, sn.Pos, 1f, 1f, FxLayer.Behind, 6));
                if (q == null)
                {
                    continue;
                }
                q.SX = sn.Scale.x;
                q.SY = sn.Scale.y;
                Vector2 off = sn.Pos - src;
                st.Ghosts.Add(q);
                st.GhostOff.Add(new Vector2(Mathf.Clamp(off.x * 0.85f, -hs, hs), off.y * 0.15f));
            }
            _snaps.Clear();
            Ruin.Quake(m.A, src, Scale.Heavy, 1.4f, k);
            // the slab, and the pit it leaves
            int wpx = Mathf.Clamp(Mathf.RoundToInt(st.R * 2f / px), 8, 160);
            int hpx = Mathf.Clamp(Mathf.RoundToInt(wpx * 0.42f), 4, 90);
            st.Over = m.Hpx * px * 1.15f + hpx * px * 0.8f;
            st.Rot = (Random.value < 0.5f ? -1f : 1f) * Random.Range(10f, 20f);
            st.P = m.Glue(Fx.Spawn(EarthKit.Slab(wpx, hpx, top, st.Soil), src, px, 1f, FxLayer.Behind, 4));
            st.Lifted = true;
            st.G = src;
            if (!m.Vis)
            {
                return;
            }
            Particle pit = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(st.R / px), 2, 140), Art.Ground, 0.25f), src, px, 7f, FxLayer.Ground, 1);
            Color dark = Fx.Darken(st.Soil, 0.55f);
            pit?.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
            Kit.Crater(src, px * k * 1.4f, tile, false, 7f, false, m.Hue.Edge);
            Kit.DustRing(src, px, tile, 24, 70f, 0.7f, k * 1.2f);
            Kit.Debris(src, px, tile, 14, k);
            EarthKit.Boulders(src, px, 4, k, st.Soil);
        }

        private static bool Has(Building b)
        {
            for (int i = 0; i < _snaps.Count; i++)
            {
                if (_snaps[i].B == b)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>It lets fly: the slab is thrown at the target (where it stands now, on solid ground).</summary>
        private static void Release(ChoreoMove m, Slab st)
        {
            if (!st.Lifted)
            {
                return;
            }
            Vector2 dest = m.VOk ? m.VG : st.Dest;
            if (!Combat.SafeSpot(dest) && !Choreo.Wading(dest) && Combat.FindLanding(dest, 2f, out Vector2 spot))
            {
                dest = spot;
            }
            st.Dest = dest;
            st.From = st.G;
            st.FromH = st.H;
            float d = Vector2.Distance(st.From, dest);
            st.Flight = 0.4f + 0.03f * d;
            st.Apex = (1.2f + 0.12f * d) * m.Kk;
            if (m.VOk)
            {
                Combat.Pin(m.V, st.Flight + 0.3f);
            }
            for (int i = 0; i < st.Riders.Count; i++)
            {
                Victim b = st.Riders[i];
                if (b != null && !b.Done && b.Holder == m)
                {
                    Combat.Pin(b.A, st.Flight + 0.5f);
                }
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                Vector2 at = st.G + new Vector2(0f, st.H);
                Kit.SpeedLines(at, dest - st.From, m.Px, 6, 8f, 0.18f, Fx.WithA(m.Hue.Edge, 0.85f));
                Fx.FocusLines(m.BodyPos(), m.Px, Fx.Count(8), 10f * m.Kk, 18f * m.Kk, 0.12f);
            }
        }

        private static readonly List<Actor> _crushed = new List<Actor>();

        /// <summary>The slab comes down: it shatters, crushing what it lands on; the riders are thrown off; a shockwave.</summary>
        private static void Crash(ChoreoMove m, Slab st)
        {
            if (!st.Lifted || st.Crashed)
            {
                return;
            }
            st.Crashed = true;
            int level = m.Level;
            float px = m.Px, k = m.Kk;
            Vector2 at = st.Dest;
            WorldTile tile = Combat.TileAt(at);
            float crashR = st.R + 0.7f;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            if (st.P != null)
            {
                m.Unglue(st.P);
                st.P = null;
            }
            for (int i = 0; i < st.Ghosts.Count; i++)
            {
                if (st.Ghosts[i] != null)
                {
                    m.Unglue(st.Ghosts[i]);
                }
            }
            st.Ghosts.Clear();
            // the riders are thrown off it (and are not crushed by it too)
            _crushed.Clear();
            for (int i = 0; i < st.Riders.Count; i++)
            {
                Victim b = st.Riders[i];
                if (b == null || b.Done || b.Holder != m)
                {
                    continue;
                }
                Actor u = b.A;
                Vector2 off = st.Offsets[i];
                Vector2 dir = off.sqrMagnitude > 0.01f ? off.normalized : Random.insideUnitCircle.normalized;
                Vector2 want = Combat.Clamp(at + dir * (crashR + 1.2f));
                Vector2 to = Combat.FindLanding(want, 2f, out Vector2 spot) ? spot : at;
                _crushed.Add(u);
                Combat.Allowed(m.A, u, out float cm);
                Combat.Hit(m.A, u, RiderMult(level) * cm, false, false, 0f, m);
                Impact im = Impact.Of(Hurt.Thrown, to - b.P.Pose.Ground, 0f, 1.3f);
                im.To = to;
                im.Lift = 1.2f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                im.Daze = 1.2f;
                b.LetGo(im, m.A, m);
            }
            st.Riders.Clear();
            // what it lands on is crushed
            Choreo.Foes(m.A, at, crashR, _foes);
            for (int i = 0; i < _foes.Count; i++)
            {
                Actor u = _foes[i];
                if (_crushed.Contains(u) || !Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, at);
                bool under = d <= st.R * 0.6f;
                HitResult r = Combat.Hit(m.A, u, CrashMult(level) * cm * Mathf.Lerp(1f, 0.65f, d / crashR), true, false, 0f, m);
                _crushed.Add(u);
                if (Combat.Living(u))
                {
                    Vector2 away = u.current_position - at;
                    Impact im = under ? Impact.Of(Hurt.DriveIn, away, 0f, 1.8f) : Impact.Of(Hurt.Knockdown, away, 1.2f, 1.4f);
                    im.Daze = 1.4f;
                    Victims.Hurt(u, m.A, m, im);
                    if (Weights.Heavy(u))
                    {
                        Combat.Stagger(u, 1.6f);
                    }
                }
                Victims.Flash(u, Color.white, 0.08f);
                if (u == m.V)
                {
                    m.LastHit = r;
                }
            }
            // the shockwave round it
            float outer = crashR + 2.5f;
            Choreo.Foes(m.A, at, outer, _foes);
            for (int i = 0; i < _foes.Count; i++)
            {
                Actor u = _foes[i];
                if (_crushed.Contains(u) || !Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                Combat.Hit(m.A, u, ShockMult * cm, true, false, 0f, m);
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, m.A, m, Impact.Of(Hurt.Knockdown, u.current_position - at, 1f, 1.1f));
                }
            }
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 0.8f * k);
                Kit.ImpactFrame(hit, px, (0.7f + 0.12f * level) * k, m.Hue.Pal, level >= 3, FxLayer.Behind);
                ChoreoFx.Impact(hit, Vector2.down, px, k, 2.2f, m.Hue, m.TopLayer, true);
                Kit.DustRing(at, px, tile, 30, 90f, 0.85f, k * 1.4f);
                Kit.Debris(at, px, tile, 18, k * 1.2f);
                EarthKit.Boulders(at, px, 10 + 3 * level, k * 1.3f, st.Soil, 1.3f);
                Kit.Crater(at, px * k * 1.5f, tile, true, 10f, false, m.Hue.Edge);
                Kit.Shockwave(at, px, st.R / px, outer / px, 0.5f, Fx.WithA(m.Hue.Core, 0.95f), 3);
            }
            Kit.Stop(at, 0.1f * m.ImpactK);
            Ruin.Blast(m.A, at, st.R / 1.95f * 1.2f, Scale.Heavy, m.Pose.Ground);
            Ruin.Quake(m.A, at, Scale.Heavy, 1.6f, k);
            EarthKit.Fissures(m.A, at, px, crashR * 2f, EarthKit.L(level, 6, 8, 10), m.Hue.Edge, Scale.Heavy);
            m.SetLink(Link.Down, m.V);
            if (Dev.On) Dev.Trace("atlas_lift " + Techniques.Roman(level) + " crashes: " + _crushed.Count + " crushed, radius " + crashR.ToString("F1"));
        }

        /// <summary>The move ended: a slab still in the air falls apart where it is (the riders are dropped by the engine).</summary>
        private static void Gone(ChoreoMove m, Slab st)
        {
            if (!st.Lifted || st.Crashed)
            {
                return;
            }
            st.Crashed = true;
            Vector2 at = st.G;
            if (Kit.Visible(at, 0.4f))
            {
                EarthKit.Boulders(at, m.Px, 8, m.Kk, st.Soil);
                Kit.Debris(at, m.Px, Combat.TileAt(at), 10, m.Kk, st.H);
                Kit.DustRing(at, m.Px, Combat.TileAt(at), 12, 40f, 0.5f, m.Kk);
            }
        }
    }
}
