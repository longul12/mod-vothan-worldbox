using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SIEGE BREAKER (Hunter's Way, Grandmaster). Made for what is not a creature at all: towers and
    /// the walls and halls of an enemy town. Used when an enemy tower shoots at it, when it is
    /// itself attacking a building, or when it stands in an enemy town with no one at its throat.
    /// It runs to the foot of the building, plants its feet wide (the ground cracking under them)
    /// and raises both fists high, trembling, then brings them down on the foundations like a ram:
    /// the ground splits in lines racing out from the blow, the building shudders, and a beat later
    /// everything around it comes down in a rolling cloud of dust and rubble; enemies nearby are
    /// thrown off their feet. II: a stomp follows that sends a quake through the town (more
    /// buildings, a wider ring); III: it leaps high and dives fist first onto the tower, a crater
    /// where it lands. It respects the "Environment damage" setting: off, it is never used; light,
    /// buildings crack but stand (a quarter of the blow); full, they collapse (Kit.Ravage's rule:
    /// a Grandmaster breaks everything it lands on, its own town's too, but its blow is aimed only at
    /// enemy buildings). Units take I 1.2, II 1.5, III 1.8 (enemies only). The Brain never offers a
    /// building plan (Prey Building matches no unit), so the fighter's own watcher (SiegeWatch)
    /// plays it through Brain.Execute when a building is the thing to break.
    /// </summary>
    internal sealed class SiegeBreakerTech : Technique
    {
        public override string Id => "siege_breaker";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Heavy;
        public override Prey Prey => Prey.Building;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 40f, 62f, 95f };
        public override float[] Cooldown => new[] { 16f, 17f, 18f };

        /// <summary>How far it goes for a building (tiles).</summary>
        public static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8f : 9.5f;
        }

        /// <summary>The ring the collapse brings down (tiles).</summary>
        public static float Blast(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.4f : 4.2f;
        }

        public static float UnitMult(int level)
        {
            return level <= 1 ? 1.2f : level == 2 ? 1.5f : 1.8f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            // (the Brain drops building plans: Tactics reads Prey from units. The AI's way in is SiegeWatch)
            if (!c.Forced)
            {
                return null;
            }
            Plan p = Siege.PlanFor(c.Self, level, true, c.ForcePoint ?? (c.ForceTarget != null ? c.ForceTarget.current_position : c.Pos));
            if (Dev.On) Dev.Trace("siege_breaker forced plan: " + (p != null ? "a building at " + p.Point : "none"));
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            Building b = Siege.At(p.Point);
            if (b == null)
            {
                if (Dev.On) Dev.Trace("siege_breaker: no building at " + p.Point);
                return false;
            }
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                if (Dev.On) Dev.Trace("siege_breaker: the performer can't act now");
                return false;
            }
            Siege s = new Siege(m, b);
            int level = m.Level;
            // to its foot
            m.RunUp(mm => s.Foot(mm), level <= 1 ? 0.075f : 0.06f).OnlyIf(mm => s.Far(mm));
            if (level >= 3)
            {
                // III: up high and down fist first onto it
                m.Crouch(0.12f, 1.3f);
                m.Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.34f,
                    Route = Route.Arc,
                    PathEase = Ease.InOutSine,
                    Apex = 2.6f,
                    Dest = mm => s.Foot(mm),
                    To = new Shape(-20f, 0.86f, 1.18f, 0.9f),
                    Ease = Ease.OutQuad,
                    Ghosts = 0.03f,
                    Track = false,
                    Forms = Form.Aerial | Form.Hunt | Form.Heavy
                });
            }
            // the plant: feet wide, the ground cracking, both fists raised high and trembling
            m.PoseTo(level >= 3 ? 0.1f : 0.18f, new Shape(-24f, 1.04f, 1.12f, float.NaN, -0.06f), Ease.OutQuad, Tempo.Wind)
                .OnEnter(mm => s.Plant(mm))
                .Every((mm, k) => s.Raise(mm, k));
            m.Hold(level >= 3 ? 0.04f : 0.1f, true, Tempo.Wind);
            // the ram: down on the foundations
            Beat ram = new Beat { Tempo = Tempo.Act, Dur = level >= 3 ? 0.12f : 0.09f, To = new Shape(36f, 1.2f, 0.82f, 0f, 0.14f), Ease = Ease.OutExpo, Track = false, Forms = Forms };
            ram.Enter = mm => s.Swing(mm);
            ram.On(0.5f, mm => s.Ram(mm));
            m.Add(ram);
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.18f, To = new Shape(28f, 1.26f, 0.78f, 0f, 0.12f), Ease = Ease.OutExpo, Track = false });
            // a beat later it all comes down
            m.Do(mm => s.Collapse(mm));
            if (level >= 2)
            {
                // II/III: a stomp that sends a quake through the town
                m.PoseTo(0.12f, new Shape(-8f, 0.92f, 1.14f, 0.1f), Ease.OutQuad, Tempo.Wind);
                Beat stomp = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(10f, 1.24f, 0.8f, 0f), Ease = Ease.OutExpo, Track = false, Forms = Form.Hunt | Form.Ground | Form.Heavy };
                stomp.On(0.6f, mm => s.Quake(mm));
                m.Add(stomp);
                m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(6f, 1.12f, 0.9f, 0f), Ease = Ease.OutQuad, Track = false });
            }
            m.Recover(0.3f);
            bool ok = m.Start();
            if (!ok && Dev.On) Dev.Trace("siege_breaker: the move did not start");
            return ok;
        }
    }

    /// <summary>
    /// One Siege Breaker in progress (the building it is aimed at, the blow, the collapse, the
    /// quake), and the finding of enemy buildings (towers first) around a fighter: the buildings
    /// of the map's zones round it, never the whole map.
    /// </summary>
    internal sealed class Siege
    {
        private readonly ChoreoMove _m;
        private readonly Building _b;
        private readonly Vector2 _at;
        private bool _rammed, _collapsed;
        private static readonly List<Building> _ring = new List<Building>();

        public Siege(ChoreoMove m, Building b)
        {
            _m = m;
            _b = b;
            _at = Where(b);
            m.Point = _at;
            Vector2 d = _at - m.Stance;
            if (d.sqrMagnitude > 1e-4f)
            {
                m.Face(d);
            }
        }

        /// <summary>A building's middle on the ground.</summary>
        public static Vector2 Where(Building b)
        {
            return b.current_tile != null ? new Vector2(b.current_tile.posV3.x, b.current_tile.posV3.y) : b.current_position;
        }

        /// <summary>The enemy building on the tile at 'p' (or on a tile next to it).</summary>
        public static Building At(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            if (t == null)
            {
                return null;
            }
            if (t.hasBuilding() && t.building != null && t.building.isAlive())
            {
                return t.building;
            }
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    WorldTile n = World.world.GetTile(t.x + dx, t.y + dy);
                    if (n != null && n.hasBuilding() && n.building != null && n.building.isAlive())
                    {
                        return n.building;
                    }
                }
            }
            return null;
        }

        private bool BOk => _b != null && _b.isAlive() && !_b.isRuin();

        /// <summary>Further from it than a blow's reach.</summary>
        public bool Far(ChoreoMove m)
        {
            return Vector2.Distance(m.Stance, _at) > HalfOf(_b) + 1.1f * m.Kk;
        }

        private static float HalfOf(Building b)
        {
            if (b == null || b.asset == null || b.asset.fundament == null)
            {
                return 1f;
            }
            BuildingFundament f = b.asset.fundament;
            return Mathf.Clamp((f.left + f.right + 1) * 0.5f, 0.5f, 3f);
        }

        /// <summary>Its foot, on the near side (solid ground).</summary>
        public Vector2 Foot(ChoreoMove m)
        {
            Vector2 d = m.Stance - _at;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : -m.Dir;
            Vector2 want = Combat.Clamp(_at + dir * (HalfOf(_b) + 0.6f * m.Kk));
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            return Combat.FindLanding(want, 2.5f, out Vector2 q) ? q : m.Stance;
        }

        /// <summary>Feet planted wide: the ground cracks under them.</summary>
        public void Plant(ChoreoMove m)
        {
            Vector2 d = _at - m.Stance;
            if (d.sqrMagnitude > 1e-4f)
            {
                m.Face(d);
            }
            Choreo.Sound("event:/SFX/HIT/HitStone", m.A.current_tile, 0.1f);
            if (m.Vis)
            {
                Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 34f, 0.5f, m.Kk);
                Kit.Crater(m.Pose.Ground, m.Px * m.Kk, m.A.current_tile, false, 3f, true, m.Hue.Edge);
            }
        }

        /// <summary>Both fists rising high, a glow gathering on them, dust swirling.</summary>
        public void Raise(ChoreoMove m, float k)
        {
            if (!m.Vis || Random.value > 0.35f)
            {
                return;
            }
            Vector2 fists = m.HeadPos() + new Vector2(-m.Fwd * 1.5f, 2f) * m.Px;
            ChoreoFx.Motes(fists, m.Px, 2, 8f * m.Kk, 0.14f, m.Hue.Core, m.Hue.Edge);
            m.Pose.Nudge = Choreo.Tremble(m.BeatT, 0.4f + 0.4f * k);
        }

        /// <summary>The swing: a great arc of the arms over its head.</summary>
        public void Swing(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                int r = Mathf.Clamp(Mathf.RoundToInt(12f * Mathf.Lerp(1f, m.Kk, 0.6f)), 6, 40);
                Kit.Swish(m, Art.Arc(r, 1f, 110f, -60f, m.Level >= 2 ? 4 : 3), 0.18f, new Vector2(2f * m.Fwd, 3f), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
            }
        }

        /// <summary>The fists hit the foundations: lines split the ground, the building shudders.</summary>
        public void Ram(ChoreoMove m)
        {
            if (_rammed)
            {
                return;
            }
            _rammed = true;
            Vector2 hit = m.Pose.Ground + m.Dir * 0.6f * m.Kk;
            WorldTile tile = Combat.TileAt(hit);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.05f);
            if (BOk)
            {
                _b.startShake(0.4f);
            }
            if (m.Vis)
            {
                Vector2 at = hit + new Vector2(0f, 2f * m.Px);
                Kit.ImpactFrame(at, m.Px, (0.3f + 0.08f * m.Level) * m.Kk, m.Hue.Pal, m.Level >= 3, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                ChoreoFx.Impact(at, m.Dir, m.Px, m.Kk, 2f, m.Hue, m.TopLayer, true);
                Kit.Debris(hit, m.Px, tile, Fx.Count(10), m.Kk);
                // the ground splits in lines racing out from the blow
                int n = 3 + m.Level;
                float baseA = Mathf.Atan2(m.Dir.y, m.Dir.x);
                for (int i = 0; i < n; i++)
                {
                    float a = baseA + (i - (n - 1) * 0.5f) * 0.45f + Random.Range(-0.12f, 0.12f);
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                    int len = Mathf.RoundToInt((12f + 4f * m.Level) * m.Kk);
                    Particle f = Fx.Spawn(ChoreoArt.Fissure(len, i & 3), hit + d * len * 0.5f * m.Px, m.Px, 1.4f, FxLayer.Ground, 4);
                    if (f != null)
                    {
                        f.Rot = a * Mathf.Rad2Deg;
                        f.Snap = true;
                        f.Color(Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.7f), Fx.WithA(Color.black, 0f));
                    }
                }
            }
            Kit.Stop(hit, (0.06f + 0.02f * m.Level) * m.ImpactK);
            Ruin.Quake(m.A, hit, m.Scale, 1.4f, m.Kk);
            if (m.Level >= 3)
            {
                Ruin.Blast(m.A, hit, 1.1f, m.Scale, m.Pose.Ground);
            }
        }

        /// <summary>The collapse: the building and everything round it come down in a rolling cloud of dust.</summary>
        public void Collapse(ChoreoMove m)
        {
            if (_collapsed)
            {
                return;
            }
            _collapsed = true;
            float r = SiegeBreakerTech.Blast(m.Level);
            Bring(m, _at, r, 1f);
            m.AreaBlow(_at, r, SiegeBreakerTech.UnitMult(m.Level), null, Hurt.Knockdown, 1f);
        }

        /// <summary>II/III: a stomp sends a quake through the town, further out.</summary>
        public void Quake(ChoreoMove m)
        {
            float r = SiegeBreakerTech.Blast(m.Level) * 1.6f;
            Vector2 at = m.Pose.Ground;
            if (m.Vis)
            {
                Kit.Shockwave(at, m.Px, 4f, r / m.Px, 0.5f, Fx.WithA(m.Hue.Edge, 0.85f), 3);
                Kit.DustRing(at, m.Px, m.A.current_tile, Fx.Count(14), 60f, 0.7f, m.Kk * 1.3f);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", m.A.current_tile);
            Bring(m, at, r, 0.55f);
            Ruin.Quake(m.A, at, m.Scale, 1.2f, m.Kk);
        }

        /// <summary>
        /// Brings down the enemy buildings within 'r' of 'at' (a share 'k' of the blow): the ground's own
        /// damage (Kit.Ravage, the setting's rules) and the blow aimed at the enemy's buildings
        /// (towers and civ buildings: full Environment damage collapses them, light only cracks them).
        /// </summary>
        private void Bring(ChoreoMove m, Vector2 at, float r, float k)
        {
            if (Settings.Destruction <= 0)
            {
                return;
            }
            Ruin.Blast(m.A, at, r * 0.55f * k, m.Scale, m.Pose.Ground, false);
            _ring.Clear();
            Around(m.A, at, r, _ring);
            float share = Settings.Destruction >= 2 ? 1f : 0.25f;
            for (int i = 0; i < _ring.Count; i++)
            {
                Building b = _ring[i];
                if (b == null || !b.isAlive() || b.isRuin())
                {
                    continue;
                }
                float d = Vector2.Distance(Where(b), at);
                float near = 1f - Mathf.Clamp01(d / Mathf.Max(0.5f, r));
                float frac = (b == _b ? 0.7f + 0.15f * m.Level : 0.35f + 0.1f * m.Level) * Mathf.Lerp(0.6f, 1f, near) * k * share;
                float dmg = Mathf.Max(30f, b.getMaxHealth() * frac);
                int hp0 = b.getHealth();
                try
                {
                    b.getHit(dmg, true, AttackType.Other, m.A, false, false, true);
                }
                catch (Exception e)
                {
                    SakugaMod.Report("siege_breaker", e);
                }
                if (Kit.Visible(Where(b), 0.4f))
                {
                    Vector2 w = Where(b);
                    Color dust = Fx.Lighten(Fx.SoilColor(b.current_tile), 0.25f);
                    ChoreoFx.Smoke(w + new Vector2(0f, 2f * m.Px), Mathf.Max(0.1f, m.Px), 6 + 2 * m.Level, 14f, dust, 1.2f);
                    Kit.Debris(w, Mathf.Max(0.1f, m.Px), b.current_tile, Fx.Count(6), 1.2f, 0.5f);
                }
                if (Dev.On) Dev.Trace("siege_breaker hits " + b.asset.id + " for " + dmg.ToString("F0") + " (" + hp0 + " -> " + b.getHealth() + "/" + b.getMaxHealth() + ", " + b.animation_state + ")");
            }
            if (Kit.Visible(at, 0.6f))
            {
                Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(at)), 0.3f);
                ChoreoFx.Smoke(at + new Vector2(0f, 3f * m.Px), Mathf.Max(0.1f, m.Px), Fx.Count(12 + 4 * m.Level), r / Mathf.Max(0.1f, m.Px) * 0.6f, dust, 1.4f);
                Fx.Ring(at, m.Px, 0.55f, 4f, r / m.Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1.6f, Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
            }
        }

        // -------------------------------------------------------------- finding --


        /// <summary>
        /// The living enemy buildings (towers and town buildings) of 'a' within 'r' of 'at': the tiles of
        /// that circle (a few hundred lookups, a few times a second at most per Grandmaster).
        /// </summary>
        public static int Around(Actor a, Vector2 at, float r, List<Building> into)
        {
            into.Clear();
            _seen = 0;
            WorldTile c = Combat.TileAt(at);
            if (a == null || a.kingdom == null || c == null || World.world == null)
            {
                return 0;
            }
            int ri = Mathf.CeilToInt(r);
            float r2 = r * r;
            for (int dx = -ri; dx <= ri; dx++)
            {
                for (int dy = -ri; dy <= ri; dy++)
                {
                    if (dx * dx + dy * dy > r2)
                    {
                        continue;
                    }
                    WorldTile t = World.world.GetTile(c.x + dx, c.y + dy);
                    if (t == null || t.building == null)
                    {
                        continue;
                    }
                    Building b = t.building;
                    if (into.Contains(b))
                    {
                        continue;
                    }
                    _seen++;
                    if (!b.isUsable() || !b.isAnimationState(BuildingAnimationState.Normal) || b.kingdom == null || b.asset == null)
                    {
                        continue;   // (a ruin, one being torn down, one still rising: a blow can't touch those)
                    }
                    if (!(b.asset.tower || b.asset.city_building || b.asset.building_type == BuildingType.Building_Civ))
                    {
                        continue;   // trees, plants and rocks are no siege
                    }
                    if (!a.kingdom.isEnemy(b.kingdom))
                    {
                        continue;
                    }
                    if ((Where(b) - at).sqrMagnitude <= r2)
                    {
                        into.Add(b);
                    }
                }
            }
            return into.Count;
        }

        private static int _seen;

        private static readonly List<Building> _cands = new List<Building>();

        /// <summary>
        /// The best building for a Siege Breaker of 'a' at 'level' near 'near' (towers first, then the
        /// building with the most enemy buildings round it), or null. 'forced': a dev order.
        /// </summary>
        public static Plan PlanFor(Actor a, int level, bool forced, Vector2 near)
        {
            if (a == null || Settings.Destruction <= 0)
            {
                return null;
            }
            float range = forced ? Mathf.Max(SiegeBreakerTech.Range(level), 16f) : SiegeBreakerTech.Range(level);
            Around(a, forced ? near : a.current_position, range, _cands);
            if (_cands.Count == 0)
            {
                if (forced && Dev.On) Dev.Trace("siege_breaker: no enemy building within " + range + " of " + near + " (" + _seen + " buildings seen round it)");
                return null;
            }
            float blast = SiegeBreakerTech.Blast(level);
            Building best = null;
            float bestScore = 0f;
            for (int i = 0; i < _cands.Count; i++)
            {
                Building b = _cands[i];
                Vector2 w = Where(b);
                float d = Vector2.Distance(a.current_position, w);
                if (d > range + 0.5f || !forced && !Combat.ClearPath(a.current_position, w))
                {
                    continue;
                }
                float score = b.asset.tower ? 40f : 12f;
                // what the collapse brings down with it
                for (int j = 0; j < _cands.Count; j++)
                {
                    if (j != i && (Where(_cands[j]) - w).sqrMagnitude <= blast * blast)
                    {
                        score += _cands[j].asset.tower ? 18f : 8f;
                    }
                }
                score -= d * 1.5f;
                if (score > bestScore || best == null)
                {
                    best = b;
                    bestScore = score;
                }
            }
            _cands.Clear();
            if (best == null)
            {
                return null;
            }
            Vector2 at = Where(best);
            Vector2 dir = at - a.current_position;
            return new Plan
            {
                Target = null,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = Mathf.Max(1f, bestScore),
                AreaRadius = blast,
                Data = forced
            };
        }
    }

    /// <summary>
    /// The siege watcher: a Grandmaster or God that knows Siege Breaker breaks enemy towers and towns
    /// when they are the thing to break: an enemy tower within reach, or the building it is itself
    /// attacking, or an enemy town round it while no enemy is at its throat. Looks at most every
    /// 0.7 s per fighter, only for those who know the technique, and plays it through the Brain
    /// (Brain.Execute: mana, cooldown, chains and the dev log as for any technique).
    /// </summary>
    internal static class SiegeWatch
    {
        private static Technique _tech;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FighterTick += Tick;
            Dev.Commands["hw_siege"] = DevTowers;
        }

        private static void Tick(Fighter f, float dt)
        {
            if (f == null || f.Tier < Tier.Grandmaster && f.Tier != Tier.None || Settings.Destruction <= 0)
            {
                return;
            }
            try
            {
                Look(f);
            }
            catch (Exception e)
            {
                SakugaMod.Report("siege watch", e);
            }
        }

        private static void Look(Fighter f)
        {
            _tech ??= Techniques.Get("siege_breaker");
            if (_tech == null || !f.Valid || !f.Elapsed("siege_watch", 0.7f))
            {
                return;
            }
            f.Mark("siege_watch");
            Actor a = f.A;
            int level = Techniques.Level(a, _tech, f.Tier);
            if (level <= 0 || !f.Ready(_tech) || !Mana.Has(a, _tech, level, false) || f.Linked)
            {
                return;
            }
            if (!Moves.CanPerform(a) || Moves.Committed(a) || Director.InScene(a) || f.InScene)
            {
                return;
            }
            float gap = f.Info != null ? f.Info.MinGap : 1f;
            if (Fighter.Now - f.LastEnd < gap + 0.5f)
            {
                return;
            }
            // what calls for it: its own attack on a building, an enemy tower near, or an enemy town while nobody is at its throat
            bool onBuilding = a.has_attack_target && a.attack_target != null && a.attack_target.isBuilding() && a.attack_target.isAlive();
            bool pressed = a.has_attack_target && a.attack_target != null && a.attack_target.isActor()
                && Vector2.Distance(a.current_position, a.attack_target.current_position) < 3f;
            Plan p = Siege.PlanFor(a, level, false, a.current_position);
            if (p == null)
            {
                return;
            }
            Building b = Siege.At(p.Point);
            bool tower = b != null && b.asset != null && b.asset.tower;
            if (!onBuilding && !tower && (pressed || !f.InCombat || p.Score < 20f))
            {
                return;
            }
            p.Tech = _tech;
            p.Level = level;
            p.Data = false;
            Brain.Execute(f, p);
        }

        /// <summary>Dev: "hw_siege [n] [building]": n enemy buildings (watch towers) round the performer's first foe.</summary>
        private static void DevTowers(string[] args)
        {
            Actor perf = Dev.Performer;
            if (perf == null)
            {
                Dev.Log("hw_siege: no performer");
                return;
            }
            Actor foe = null;
            foreach (Actor u in Dev.Units)
            {
                if (u != null && u.isAlive() && Combat.IsFoe(perf, u))
                {
                    foe = u;
                    break;
                }
            }
            if (foe == null || foe.kingdom == null)
            {
                Dev.Log("hw_siege: no foe");
                return;
            }
            int n = Mathf.Clamp((int)Dev.ArgF(args, 1, 2f), 1, 6);
            string id = Dev.Arg(args, 2, "watch_tower_human");
            Kingdom owner = foe.kingdom;
            if (perf.kingdom != null && World.world.kingdoms != null)
            {
                foreach (Kingdom k in World.world.kingdoms.list)
                {
                    if (k != null && k.isAlive() && k != perf.kingdom && perf.kingdom.isEnemy(k))
                    {
                        owner = k;   // a real town's kingdom: its buildings stand as a town's do
                        break;
                    }
                }
            }
            Vector2 c = foe.current_position + new Vector2(2.5f, 0f);
            int made = 0;
            for (int i = 0; i < n; i++)
            {
                // (far enough apart that no footprint overlaps another: a building placed over one takes its tiles)
                Vector2 at = c + new Vector2(1.5f + (i % 3) * 6f, (i / 3) * 6f + (i % 2) * 4f - 2f);
                WorldTile t = Combat.TileAt(at);
                if (t == null)
                {
                    continue;
                }
                Building b = World.world.buildings.addBuilding(id, t, false, false, BuildPlacingType.New);
                if (b == null)
                {
                    continue;
                }
                b.setKingdom(owner);
                made++;
                if (Dev.On) Dev.Trace("hw_siege: " + b.asset.id + " at " + Siege.Where(b) + " usable " + b.isUsable() + " state " + b.animation_state + " kingdom " + (b.kingdom != null ? b.kingdom.name : "-"));
            }
            Dev.Log("hw_siege: " + made + " " + id + " for " + owner.name + " (enemy of the performer: " + (perf.kingdom != null && perf.kingdom.isEnemy(owner)) + ", performer of " + (perf.kingdom != null ? perf.kingdom.name : "-") + ")");
        }
    }
}
