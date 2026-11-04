using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// AVALANCHE BARRAGE (Iron Fist, Grandmaster). Feet planted wide, both fists drawn back, then
    /// hundreds of fists: its arms become a blur and a quarter circle in front of it fills with flying
    /// fists (crimson afterimages streaming out over a 90 degree cone). Everyone in the cone is
    /// hammered where it stands and driven back step by step under the storm, the ground torn up under
    /// their heels; the last double straight sends the whole cone flying and rips furrows along it.
    /// An area attack: it can be slipped (a quick step out of the cone), never parried.
    /// I (Grandmaster): 4.5 tiles, 0.7 s of fists, a 3-tile drive. II: 5.25 tiles, 0.85 s, 4 tiles.
    /// III: 6 tiles, a full second, 5 tiles, the last blow launches. Damage on each one in the cone
    /// (x the user's damage): I 1.3 + 1.0, II 1.7 + 1.3, III 2.2 + 1.6 (the storm, then the last blow).
    /// </summary>
    internal sealed class AvalancheBarrageTech : Technique
    {
        public override string Id => "avalanche_barrage";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.IronFist;
        public override Form Forms => Form.Flurry | Form.Punch | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 34f, 56f, 85f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        /// <summary>A crowd scattered or a body thrown into the others: the barrage meets them as they come back.</summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Close;
        }

        /// <summary>Half the cone's angle (degrees): a quarter circle.</summary>
        public const float Half = 45f;

        /// <summary>How far it rushes in before it plants its feet (tiles, x size), and how close to its foe it plants them.</summary>
        public const float Approach = 2.5f, Near = 1.4f;

        public static float Length(int level)
        {
            return level <= 1 ? 4.5f : level == 2 ? 5.25f : 6f;
        }

        public static float StormMult(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.7f : 2.2f;
        }

        public static float LastMult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.3f : 1.6f;
        }

        public static float Drive(int level)
        {
            return level <= 1 ? 3f : level == 2 ? 4f : 5f;
        }

        public static float Duration(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.85f : 1f;
        }

        /// <summary>Is 'p' inside the cone from 'from' along 'dir' ('len' tiles, plus 'size' of a big body)?</summary>
        public static bool InCone(Vector2 from, Vector2 dir, float len, Vector2 p, float size)
        {
            Vector2 rel = p - from;
            float d = rel.magnitude;
            if (d > len + size || d < 0.05f)
            {
                return d < 0.05f;
            }
            return Vector2.Angle(dir, rel) <= Half + 6f + Mathf.Rad2Deg * Mathf.Atan2(size, Mathf.Max(0.3f, d));
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float len = Length(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float total = StormMult(level) + LastMult(level);
            Plan best = null;
            // it may rush a few tiles in before it plants its feet
            float approach = Approach * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f) + (c.Forced ? 4f : 0f);
            for (int i = 0; i < c.Enemies.Count && i < 12; i++)
            {
                float d = c.EnemyDist[i];
                if (d > len + approach)
                {
                    break;
                }
                Actor aim = c.Enemies[i];
                if (d < 0.2f || !Combat.Grounded(aim) || !c.Allowed(aim) || Moves.Locked(aim))
                {
                    continue;
                }
                if (c.ForceTarget != null && aim != c.ForceTarget)
                {
                    continue;
                }
                Vector2 dir = (aim.current_position - c.Pos) / d;
                // where it plants its feet: where it stands, or a stride in front of the foe it rushes
                Vector2 from = c.Pos;
                if (d > Near * Choreo.KOf(c.Self))
                {
                    from = aim.current_position - dir * Near * Choreo.KOf(c.Self);
                    if (!Combat.SafeSpot(from) || !Combat.ClearPath(c.Pos, from))
                    {
                        continue;
                    }
                }
                int n = 0;
                float score = 0f;
                bool worth = false;
                for (int j = 0; j < c.Enemies.Count && c.EnemyDist[j] <= len + approach + 1.5f; j++)
                {
                    Actor v = c.Enemies[j];
                    if (!Combat.Grounded(v) || Moves.Locked(v) || !c.Allowed(v, out float cm))
                    {
                        continue;
                    }
                    if (!InCone(from, dir, len, v.current_position, v.stats["size"] * 0.5f))
                    {
                        continue;
                    }
                    n++;
                    score += c.Value(v, c.Expected(v, total), cm);
                    if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2f) * cm)
                    {
                        worth = true;
                    }
                }
                int need = c.Linked ? 1 : 2;
                if (!c.Forced && (n < need || !worth && n < 3))
                {
                    continue;   // not for one foe, nor for two peasants
                }
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = aim, Point = aim.current_position, Dir = dir, Count = n, Score = score, AreaRadius = len * 0.5f, Data = c.Forced };
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            AvalancheBarrageMove m = AvalancheBarrageMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Avalanche Barrage's move: the storm of fists over the cone, the drive, the last double straight.</summary>
    internal sealed class AvalancheBarrageMove : ChoreoMove
    {
        private Vector2 _cone;
        private float _len, _tickT, _fistT, _jerkT, _dustT;
        private int _ticks, _side;
        /// <summary>Those the storm reached (it slipped: -1) and how many blows each took.</summary>
        private readonly Dictionary<long, int> _taken = new Dictionary<long, int>();
        private static readonly List<Actor> _near = new List<Actor>();

        private AvalancheBarrageMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static AvalancheBarrageMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new AvalancheBarrageMove(a, t, p);
        }

        private int Ticks => Mathf.RoundToInt(AvalancheBarrageTech.Duration(Level) / 0.075f);

        public void Build()
        {
            int level = Level;
            _cone = Aim;
            _len = AvalancheBarrageTech.Length(level) * Mathf.Lerp(1f, Kk, 0.5f);
            Face(_cone);
            // (its foe a few strides away: it rushes in first)
            Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.04f,
                PerTile = 0.035f,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => ((AvalancheBarrageMove)mm).RushTo(),
                Ease = Ease.Linear,
                To = new Shape(14f, 1.14f, 0.9f, 0f, 0f),
                Ghosts = 0.03f,
                Dust = 0.04f,
                Track = true,
                Forms = Form.Dash | Form.Flurry | Form.Punch,
                If = mm => mm.V != null && mm.VOk && Vector2.Distance(mm.Stance, mm.VG) > AvalancheBarrageTech.Near * mm.Kk + 0.6f
            });
            // the plant: wide and low, both fists drawn back, the ground giving under its feet
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.17f,
                To = new Shape(-9f, 1.18f, 0.84f, 0f, -0.1f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Flurry | Form.Punch | Form.Area,
                Enter = mm => ((AvalancheBarrageMove)mm).Plant(),
                Every = (mm, k) =>
                {
                    if (k > 0.6f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.6f);
                    }
                }
            });
            // the storm
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = AvalancheBarrageTech.Duration(level),
                To = new Shape(10f, 1.12f, 0.92f, 0f, 0.1f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Flurry | Form.Punch | Form.Area,
                Every = (mm, k) => ((AvalancheBarrageMove)mm).Storm(k)
            });
            // the last double straight: both fists drawn back, then driven through the cone
            PoseTo(0.07f, new Shape(-10f, 0.92f, 1.08f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Form.Punch | Form.Area | Form.Heavy);
            PoseTo(0.08f, new Shape(20f, 1.32f, 0.8f, 0f, 0.22f), Ease.OutExpo, Tempo.Act).Shows(Form.Punch | Form.Area | Form.Heavy)
                .At(0.35f, mm => ((AvalancheBarrageMove)mm).Last());
            Hold(0.12f, true, Tempo.Impact);
            Recover(0.32f);
        }

        /// <summary>A stride in front of its foe (on solid ground, nothing in the way), or where it stands.</summary>
        private Vector2 RushTo()
        {
            if (V == null)
            {
                return Stance;
            }
            Vector2 d = VG - Stance;
            float dist = d.magnitude;
            if (dist < 0.01f)
            {
                return Stance;
            }
            Vector2 to = Combat.Clamp(VG - d / dist * AvalancheBarrageTech.Near * Kk);
            return Combat.SafeSpot(to) && Combat.ClearPath(Stance, to) ? to : Stance;
        }

        private void Plant()
        {
            // the cone points at its foe as it stands now
            if (V != null && VOk)
            {
                Vector2 d = VG - Stance;
                if (d.sqrMagnitude > 0.04f)
                {
                    _cone = d.normalized;
                    Face(_cone);
                }
            }
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Kit.DustRing(Pose.Ground, px, A.current_tile, 10, 26f, 0.5f, Kk);
            Kit.Crater(Pose.Ground, px * Kk, A.current_tile, false, 3f, true);
            Fx.Ring(Pose.Ground, px, 0.2f, 10f * Kk, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.85f));
        }

        /// <summary>One frame of the storm: the arms a blur, fists flying out over the cone, the foes in it hammered back.</summary>
        private void Storm(float k)
        {
            float dt = Clock.Dt;
            float px = Px;
            // the body jerks with the blows, side to side
            _jerkT -= dt;
            if (_jerkT <= 0f)
            {
                _jerkT = 0.035f;
                _side = -_side == 0 ? 1 : -_side;
                Pose.Rot = 8f + 6f * _side * Random.Range(0.5f, 1f);
                Pose.SX = 1.1f + Random.Range(0f, 0.08f);
                Pose.SY = 0.92f - Random.Range(0f, 0.05f);
                if (Vis)
                {
                    Fx.Ghost(this, Side * _side * 2f * px - Dir * px, _side * 9f, 0.08f, Fx.WithA(Hue.Ghost, 0.5f), Hue.GhostEnd);
                }
            }
            if (Vis)
            {
                _fistT -= dt;
                float every = (Level >= 3 ? 0.01f : 0.013f) / Fx.Detail();
                while (_fistT <= 0f)
                {
                    _fistT += every;
                    Fist(px);
                }
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = 0.06f;
                    // dust and torn turf thrown up along the cone's floor
                    float a = Random.Range(-AvalancheBarrageTech.Half, AvalancheBarrageTech.Half);
                    Vector2 g = Stance + Choreo.Rotate(_cone, a) * Random.Range(0.8f, _len);
                    ChoreoFx.HeelDust(g, Choreo.Rotate(_cone, a), px, Combat.TileAt(g), Kk);
                    if (Random.value < 0.3f)
                    {
                        Kit.Debris(g, px, Combat.TileAt(g), 1, Kk * 0.6f);
                    }
                }
            }
            // the blows land in waves
            _tickT -= dt;
            if (_tickT > 0f)
            {
                return;
            }
            _tickT = 0.075f * ActK;
            _ticks++;
            Choreo.Foes(A, Stance + _cone * _len * 0.5f, _len * 0.75f, _near);
            int per = Mathf.Max(1, Ticks);
            float mult = AvalancheBarrageTech.StormMult(Level) / per;
            float push = AvalancheBarrageTech.Drive(Level) * 0.8f / per;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (!AvalancheBarrageTech.InCone(Stance, _cone, _len, u.current_position, u.stats["size"] * 0.5f))
                {
                    continue;
                }
                long id = u.Sid();
                if (_taken.TryGetValue(id, out int n) && n < 0)
                {
                    continue;   // it slipped out of the storm
                }
                if (!Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                if (!Touch(u, true))
                {
                    _taken[id] = -1;
                    continue;
                }
                _taken[id] = n + 1;
                Combat.Hit(A, u, mult * cm, false, false, 0f, this);
                Vector2 away = u.current_position - Stance;
                Impact im = Impact.Of(Hurt.Reel, away.sqrMagnitude > 1e-4f ? away : _cone, push * Mathf.Lerp(1.15f, 0.8f, away.magnitude / _len), 0.7f);
                Victims.Hurt(u, A, this, im);
                if (Vis)
                {
                    Vector2 at = Choreo.BodyAt(u, Random.Range(0.35f, 0.85f)) + Random.insideUnitCircle * 0.8f * Choreo.PxOf(u);
                    Particle star = Fx.Spawn(Art.HitStars[0], at, px, 0.05f, TopLayer, 20);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 90f);
                        star.Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                    }
                    if ((_ticks & 1) == 0)
                    {
                        Kit.Sparks(at, px, 2, 40f, 80f, Hue.Edge);
                    }
                }
                if ((_ticks & 1) == 0)
                {
                    ChoreoFx.BlowSound(Limb.Fist, 0.6f, false, u.current_tile);
                }
            }
            if ((_ticks & 3) == 0)
            {
                Ruin.Quake(A, Stance + _cone * 1.5f, Scale.Casual, 0.6f, Kk);
            }
        }

        /// <summary>One fist of the storm, flying out along the cone with a crimson streak behind it.</summary>
        private void Fist(float px)
        {
            float a = Random.Range(-AvalancheBarrageTech.Half, AvalancheBarrageTech.Half);
            Vector2 d = Choreo.Rotate(_cone, a);
            Vector2 from = Hand(Limb.Fist) + Side * Random.Range(-2f, 2f) * px + new Vector2(0f, Random.Range(-1.5f, 2f) * px);
            float speed = Random.Range(20f, 28f) * Mathf.Lerp(1f, Kk, 0.5f);
            float life = _len * Random.Range(0.6f, 1f) / speed;
            Particle p = Fx.Spawn(ChoreoArt.Fist(Mathf.RoundToInt(3f * Kk) + (Level >= 3 ? 1 : 0)), from, px, life, FxLayer.Top, 12);
            if (p == null)
            {
                return;
            }
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Moving(d * speed);
            p.Color(Hue.Core, Fx.WithA(Hue.Main, 0.85f), Fx.WithA(Hue.Edge, 0f));
            Kit.Line(from, from - d * 6f * px, px, 0.06f, 1, FxLayer.Top, 11, Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
        }

        /// <summary>The last double straight: the whole cone goes flying, furrows ripped along it.</summary>
        private void Last()
        {
            int level = Level;
            float px = Px;
            Choreo.Foes(A, Stance + _cone * _len * 0.5f, _len * 0.75f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (!AvalancheBarrageTech.InCone(Stance, _cone, _len, u.current_position, u.stats["size"] * 0.5f))
                {
                    continue;
                }
                long id = u.Sid();
                if (_taken.TryGetValue(id, out int n) && n < 0 || !Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                if (!Touch(u, true))
                {
                    _taken[id] = -1;
                    continue;
                }
                // (whoever walked into the cone late takes the storm it missed with the last blow)
                float mult = AvalancheBarrageTech.LastMult(level) + (n == 0 ? AvalancheBarrageTech.StormMult(level) * 0.5f : 0f);
                HitResult r = Combat.Hit(A, u, mult * cm, true, false, 0f, this);
                Vector2 away = u.current_position - Stance;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : _cone;
                if (Combat.Living(u))
                {
                    Impact im = Impact.Of(level >= 3 ? Hurt.Launch : Hurt.Reel, (away + _cone).normalized, AvalancheBarrageTech.Drive(level) * 0.35f + 0.6f, 1.3f);
                    im.Lift = 1.3f;
                    im.Owed = MeteorSlam.AtDeathsDoor(u);
                    Victims.Hurt(u, A, this, im);
                    SetLink(Link.Scattered, null);
                }
                else
                {
                    Victims.Flash(u, Color.white, 0.06f);
                }
                if (Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.55f), away, px, Kk, 1.3f, Hue, TopLayer, true);
                }
                if (Dev.On) Dev.Trace("choreo " + Id + " avalanche last blow x" + (mult * cm).ToString("F2") + " -> " + u.asset.id + "#" + id + " hp " + u.getHealth() + (r.Killed ? " killed" : ""));
            }
            // the ground torn along the cone: a fan of furrows
            int furrows = level <= 1 ? 3 : level == 2 ? 4 : 5;
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i < furrows; i++)
                {
                    float a = Mathf.Lerp(-AvalancheBarrageTech.Half * 0.8f, AvalancheBarrageTech.Half * 0.8f, furrows > 1 ? i / (float)(furrows - 1) : 0.5f);
                    Vector2 d = Choreo.Rotate(_cone, a);
                    Vector2 s0 = Stance + d * 0.9f * Kk;
                    Vector2 s1 = Combat.Clamp(Stance + d * _len * Random.Range(0.75f, 1f));
                    Ruin.Furrow(A, s0, s1, 0.5f, Scale, Pose.Ground, false);
                    if (Vis)
                    {
                        int len = Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(s0, s1) / px), 6, 120);
                        Particle f = Fx.Spawn(ChoreoArt.Fissure(len, i), s0, px, 3.5f, FxLayer.Ground, 2);
                        if (f != null)
                        {
                            f.Rot = Mathf.Atan2(d.y * Art.Ground, d.x) * Mathf.Rad2Deg;
                            f.Snap = true;
                            Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(s0)), 0.55f);
                            f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
                        }
                        Kit.Debris(Vector2.Lerp(s0, s1, 0.6f), px, Combat.TileAt(s1), 3, Kk);
                    }
                }
            }
            finally
            {
                Kit.EndTrench(A);
            }
            Kit.Stop(Hand(Limb.Fist), (0.04f + 0.01f * level) * ImpactK);
            Ruin.Quake(A, Stance + _cone * 2f, Scale, 1.3f, Kk);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
            if (!Vis)
            {
                return;
            }
            // a wall of fists: big smears fanned over the cone
            Vector2 from = Root(Limb.Fist);
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-AvalancheBarrageTech.Half, AvalancheBarrageTech.Half, i / 6f);
                Vector2 d = Choreo.Rotate(_cone, a);
                ChoreoFx.Smear(from, from + d * _len * 0.45f, px, 0.12f, 3, 3, Hue.Core, Hue.Edge, FxLayer.Over, 8);
            }
            Kit.Flash(Hand(Limb.Fist), px, Mathf.RoundToInt(5f * Kk), 0.08f, Fx.WithA(Hue.Core, 1f), TopLayer);
            Kit.SpeedLines(Stance + _cone * _len * 0.4f, _cone, px, 6, 8f * Kk, 0.16f, Fx.WithA(Hue.Edge, 0.85f));
            Kit.DustRing(Pose.Ground, px, A.current_tile, 10, 40f, 0.5f, Kk);
        }
    }
}
