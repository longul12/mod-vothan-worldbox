using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// METEOR HEEL (Tempest Leg, Master). A deep crouch, and it is gone: one straight leap off the top
    /// of the view (a ring of dust and a column of speed lines where it stood). For a heartbeat there
    /// is nothing up there but a twinkle; on the ground a shadow grows and a ring of its colour closes
    /// on the spot (it follows the foe it is meant for). Then it falls back out of the sky heel first,
    /// standing straight, trailing fire like a meteor, and the heel comes down on the foe's head: the
    /// foe is hammered into the ground (Down), the ground breaks in a crater, and a ring of fire races
    /// out over the ground knocking everyone around flat. Unlike Meteor Slam there is no flip and no
    /// dive: it drops like a stone, upright, on one heel. A Master's blow breaks every building it
    /// lands on (Ruin). I: 8 tiles up, a 2.2-tile wave; II: 10 tiles, a 2.8-tile wave and a wider
    /// crater; III: 12 tiles and a sonic boom as it starts its fall, a 3.4-tile wave, a white-hot crater,
    /// an impact frame. Damage (x the user's damage): the heel I 2.6, II 3.4, III 4.6; the wave I 0.55,
    /// II 0.7, III 0.9 on everyone else.
    /// </summary>
    internal sealed class MeteorHeelTech : Technique
    {
        public override string Id => "meteor_heel";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Area;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Aerial | Form.Overhead | Form.Heavy;
        /// <summary>(the ground takes it as a heavy blow at every level: it falls from the sky)</summary>
        public override Scale Scale => Scale.Heavy;
        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : Scale.Heavy;
        public override float[] ManaCost => new[] { 22f, 38f, 62f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Down || link == Link.Scattered;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 2.8f : 3.4f;
        }

        public static float Heel(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.4f : 4.6f;
        }

        public static float Wave(int level)
        {
            return level <= 1 ? 0.55f : level == 2 ? 0.7f : 0.9f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8f : 9f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * k;
            Plan p = Choreo.PickArea(c, this, level, Radius(level), range, Wave(level), 2);
            if (p != null && p.Target != null && Choreo.Fair(c.Self, p.Target, out float cm, c.Forced) && Combat.Grounded(p.Target))
            {
                // the heel itself lands on the one in the middle: its full value on top of the wave's
                p.Score += c.Value(p.Target, c.Expected(p.Target, Heel(level) - Wave(level)), cm);
            }
            else
            {
                // no crowd: a lone foe worth a fall from the sky
                p = Choreo.PickMelee(c, this, level, new MeleeWant
                {
                    Reach = range, Total = Heel(level), Worthy = true, Crowd = 0f, Alone = 12f, IntoGuard = 1.15f, Worth = 16f
                });
            }
            if (p == null || p.Target == null)
            {
                return null;
            }
            // it comes down on solid ground (never into water or onto a cliff)
            Vector2 at = p.Target.current_position;
            if (!Combat.SafeSpot(at) && !Choreo.Wading(at))
            {
                return null;
            }
            if (!c.Forced && c.Dist(p.Target) > range + p.Target.stats["size"] * 0.5f)
            {
                return null;
            }
            p.AreaRadius = Radius(level);
            return p;
        }

        private sealed class State
        {
            public Vector2 Land, From, Prev;
            public bool Locked;
            public float T, Ring;
            public Particle Shadow, Marker;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float up = level <= 1 ? 8f : level == 2 ? 10f : 12f;
            float hang = level <= 1 ? 0.3f : level == 2 ? 0.34f : 0.4f;
            State st = new State();
            Hue fire = m.Hue.As(Element.Fire);

            // the crouch: it sinks, the ground ring closes on its feet (II+: a glint)
            m.Crouch(0.15f, 1.35f)
                .Shows(Form.Kick | Form.Aerial)
                .OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Ring(mm.Pose.Ground, mm.Px, 0.2f, 15f * mm.Kk, 3f, 1, 2, FxLayer.Ground, 5, Art.Ground, 1.6f,
                            Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.9f), Fx.WithA(mm.Hue.Core, 1f));
                        if (mm.Level >= 2)
                        {
                            Fx.Glint(mm.HeadPos(), mm.Px, 0.16f, mm.Hue.Core, 0.9f);
                        }
                    }
                })
                .Every((mm, kk) => mm.Pose.Nudge = kk > 0.6f ? Choreo.Tremble(mm.BeatT, 0.5f) : Vector2.zero);

            // the leap: straight up, out of sight
            Beat rise = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.2f,
                To = new Shape(-4f, 0.78f, 1.34f, up),
                Ease = Ease.OutCubic,
                Ghosts = 0.03f,
                Track = false,
                Forms = Form.Kick | Form.Aerial
            };
            rise.Enter = mm =>
            {
                st.From = mm.Stance;
                WorldTile tile = mm.A.current_tile;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", tile);
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, tile);
                    Kit.DustRing(mm.Pose.Ground, mm.Px, tile, 12, 48f, 0.55f, mm.Kk);
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.3f, 3f, 18f * mm.Kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f,
                        Fx.WithA(Color.white, 0.95f), Fx.WithA(mm.Hue.Edge, 0.6f), Fx.WithA(mm.Hue.Rim, 0f));
                    if (mm.Level >= 2)
                    {
                        Kit.Crater(mm.Pose.Ground, mm.Px, tile, false, 3f, true);
                    }
                }
                // the take-off scorches the ground it leaps from (Grandmaster and up)
                if (mm.Tier >= Tier.Grandmaster)
                {
                    Ruin.Blast(mm.A, mm.Pose.Ground, 0.7f, Scale.Light, mm.Pose.Ground, false);
                }
            };
            rise.Every = (mm, kk) =>
            {
                st.T -= Clock.Dt;
                if (st.T <= 0f && mm.Vis)
                {
                    st.T = 0.03f;
                    Kit.SpeedLines(mm.BodyPos() - new Vector2(0f, 6f * mm.Px), Vector2.up, mm.Px, 2, 4f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.8f));
                }
            };
            m.Add(rise);

            // gone: up there it is a twinkle; on the ground a shadow grows and a ring closes on the spot
            Beat sky = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = hang,
                Route = Route.Line,
                PathEase = Ease.InOutSine,
                Chase = true,
                Dest = mm => st.Locked ? st.Land : (st.Land = LandSpot(mm, st.From)),
                Hidden = true,
                Intangible = true,
                Track = false,
                Forms = Form.Kick | Form.Aerial | Form.Overhead | Form.Heavy
            };
            sky.Enter = mm =>
            {
                st.Land = LandSpot(mm, st.From);
                if (mm.Vis)
                {
                    Fx.Glint(mm.BodyPos(), mm.Px, 0.22f, mm.Hue.Core, 1.2f);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.08f);
                float px = mm.Px;
                st.Shadow = mm.Glue(Fx.Spawn(Art.Disk(Mathf.RoundToInt(4f * mm.Kk), Art.Ground, 0.4f), st.Land, px, 3f, FxLayer.Ground, 3));
                st.Marker = mm.Glue(Fx.Spawn(Art.Ring(Mathf.RoundToInt(16f * mm.Kk), 1, Art.Ground), st.Land, px, 3f, FxLayer.Ground, 4));
                st.Ring = 0f;
            };
            sky.Every = (mm, kk) =>
            {
                Marker(mm, st, kk * 0.8f);
                st.Ring -= Clock.Dt;
                if (st.Ring <= 0f && mm.Vis)
                {
                    st.Ring = 0.12f;
                    Fx.Ring(st.Land, mm.Px, 0.24f, 20f * mm.Kk, 6f * mm.Kk, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.4f,
                        Fx.WithA(mm.Hue.Rim, 0f), Fx.WithA(mm.Hue.Rim, 0.8f), Fx.WithA(mm.Hue.Core, 0f));
                }
            };
            sky.Exit = mm => st.Locked = true;
            m.Add(sky);

            // the fall: upright, heel first, trailing fire
            Beat fall = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = level <= 1 ? 0.18f : 0.16f,
                To = new Shape(4f, 0.76f, 1.38f, 0f),
                Ease = Ease.InQuad,
                Track = false,
                Forms = Form.Kick | Form.Aerial | Form.Overhead | Form.Heavy
            };
            fall.Enter = mm =>
            {
                st.Locked = true;
                st.Prev = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                if (mm.V != null && mm.VOk)
                {
                    mm.Face(mm.VG - mm.Stance);
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis && mm.Level >= 3)
                {
                    // the sound barrier breaks as it starts down
                    Vector2 at = mm.BodyPos();
                    Fx.Ring(at, mm.Px, 0.26f, 4f, 22f * mm.Kk, 2, 1, FxLayer.Top, 8, 0.4f, 2.2f,
                        Fx.WithA(Color.white, 0.95f), Fx.WithA(fire.Edge, 0.6f), Fx.WithA(fire.Edge, 0f));
                }
            };
            fall.Every = (mm, kk) =>
            {
                Marker(mm, st, 0.8f + 0.2f * kk);
                // the streak of fire the heel leaves behind it, unbroken from the sky down
                Vector2 heel = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                if (mm.Vis && (heel - st.Prev).sqrMagnitude > mm.Px * mm.Px)
                {
                    int w = mm.Level >= 3 ? 4 : mm.Level == 2 ? 3 : 2;
                    Kit.Line(st.Prev, heel, mm.Px, 0.16f, w + 1, FxLayer.Top, 10, Fx.WithA(fire.Main, 0.8f), Fx.WithA(fire.Edge, 0.6f), Fx.WithA(Kit.Smoke, 0f));
                    Kit.Line(st.Prev, heel, mm.Px, 0.1f, Mathf.Max(1, w - 1), FxLayer.Top, 11, Color.white, Fx.WithA(fire.Core, 0.8f), Fx.WithA(fire.Edge, 0f));
                }
                st.Prev = heel;
                st.T -= Clock.Dt;
                if (st.T > 0f || !mm.Vis)
                {
                    return;
                }
                st.T = 0.02f;
                float px = mm.Px;
                Vector2 feet = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                Fx.Afterimage(mm, 0.16f, Fx.WithA(fire.Core, 0.85f), Fx.WithA(fire.Main, 0.6f), Fx.WithA(fire.Edge, 0f));
                Kit.Flash(feet, px, Mathf.Max(2, Mathf.RoundToInt(2f * mm.Kk)), 0.05f, Fx.WithA(fire.Core, 0.95f), FxLayer.Top, 22);
                int n = Fx.Count(mm.Level + 1);
                for (int i = 0; i < n; i++)
                {
                    Kit.Ember(feet + Random.insideUnitCircle * 2f * px, px, Random.Range(0.25f, 0.45f),
                        new Vector2(Random.Range(-12f, 12f), Random.Range(25f, 55f)) * px, mm.Level <= 1 ? Color.Lerp(Color.white, Kit.Fire1, 0.5f) : fire.Edge);
                }
                Kit.SpeedLines(mm.BodyPos() + new Vector2(0f, 6f * px), Vector2.down, px, 1, 4f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.85f));
                if (mm.Level >= 3 && Random.value < 0.5f)
                {
                    ChoreoFx.Smoke(feet + new Vector2(0f, 8f * px), px, 1, 2f, Kit.Smoke, 0.6f);
                }
            };
            fall.Exit = mm => Impact(mm, st, fire);
            m.Add(fall);

            // the landing: crouched over the heel, a beat held, then up
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.16f, To = new Shape(16f, 1.3f, 0.72f, 0f), Ease = Ease.OutExpo, Track = false });
            m.Hold(level >= 3 ? 0.14f : 0.08f, false, Tempo.Follow)
                .Recover(0.3f);
            return m.Start();
        }

        /// <summary>Where it comes down: a short step in front of the foe, on the side it leapt from (solid ground).</summary>
        private static Vector2 LandSpot(ChoreoMove m, Vector2 from)
        {
            Vector2 c = m.V != null && m.VOk ? m.VG : m.Point;
            Vector2 side = from - c;
            side = side.sqrMagnitude > 1e-4f ? side.normalized : -m.Dir;
            Vector2 s = Combat.Clamp(c + side * m.Stand * 0.6f);
            if (Combat.SafeSpot(s) || Choreo.Wading(s))
            {
                return s;
            }
            if (Combat.SafeSpot(c))
            {
                return c;
            }
            return Combat.FindLanding(c, 2.5f, out Vector2 q) ? q : m.Stance;
        }

        /// <summary>The shadow growing and the warning ring closing on the spot (k: 0 just gone .. 1 landing).</summary>
        private static void Marker(ChoreoMove m, State st, float k)
        {
            if (st.Shadow != null && st.Shadow.Alive)
            {
                st.Shadow.Pos = st.Land;
                st.Shadow.Grad = null;
                st.Shadow.Size0 = st.Shadow.Size1 = Mathf.Lerp(0.3f, 1.4f, k);
                st.Shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.12f, 0.45f, k));
            }
            if (st.Marker != null && st.Marker.Alive)
            {
                st.Marker.Pos = st.Land;
                st.Marker.Grad = null;
                st.Marker.Size0 = st.Marker.Size1 = Mathf.Lerp(1.3f, 0.35f, k);
                st.Marker.R.color = Fx.WithA(Color.Lerp(m.Hue.Edge, m.Hue.Rim, k), Mathf.Lerp(0.4f, 0.95f, k));
            }
        }

        /// <summary>The heel arrives: the ground breaks, the ring of fire races out, the heel hammers the foe in.</summary>
        private static void Impact(ChoreoMove m, State st, Hue fire)
        {
            int level = m.Level;
            m.Pose.Height = 0f;
            Vector2 at = m.Pose.Ground;
            WorldTile tile = Combat.TileAt(at);
            float px = m.Px;
            if (st.Shadow != null)
            {
                m.Unglue(st.Shadow);
                st.Shadow = null;
            }
            if (st.Marker != null)
            {
                m.Unglue(st.Marker);
                st.Marker = null;
            }
            float crater = level <= 1 ? 1f : level == 2 ? 1.3f : 1.7f;
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.05f);
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 2f * px);
                if (level >= 2)
                {
                    Kit.ImpactFrame(hit, px, (0.42f + 0.12f * level) * m.Kk, m.Hue.Pal, false, FxLayer.Behind);
                }
                Kit.Swish(m, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(12f * m.Kk), 6, 40), 1f, 100f, -80f, 3), 0.14f, new Vector2(2f * m.Fwd, 3f), m.Fwd, m.Hue.Core, m.Hue.Edge, 7);
                ChoreoFx.Smear(m.BodyPos() + new Vector2(0f, 14f * px * m.Kk), at, px, 0.1f, 2, 2, fire.Core, fire.Edge, FxLayer.Over, 8);
                Kit.Crater(at, px * m.Kk, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 8f, false, fire.Edge);
                Kit.DustRing(at, px, tile, 16, 60f, 0.6f, m.Kk);
                Kit.Debris(at, px, tile, 10 + 3 * level, m.Kk);
                Kit.Flash(hit, px, Mathf.RoundToInt((5f + 2f * level) * m.Kk), 0.08f, Fx.WithA(fire.Core, 0.95f), m.TopLayer, 26);
                int n = Fx.Count(10 + 4 * level);
                for (int i = 0; i < n; i++)
                {
                    float ang = Random.value * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground);
                    Kit.Ember(at + d * 3f * px, px, Random.Range(0.35f, 0.7f), d * Random.Range(40f, 90f) * px + new Vector2(0f, Random.Range(10f, 30f) * px), fire.Edge);
                }
            }
            Ruin.Blast(m.A, at, crater, m.Scale, at);
            Ruin.Quake(m.A, at, m.Scale, 1.5f, m.Kk);
            if (m.Tier >= Tier.Master)
            {
                Kit.Stop(at, (0.04f + 0.02f * level) * m.ImpactK);
            }
            Actor v = m.V != null && m.VOk ? m.V : null;
            // the ring of fire races out over the ground (the foe under the heel is the heel's)
            Areas.Add(new TempestWave(m, at, Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f), Wave(level), 0.7f + 0.25f * level, v, fire, true));
            if (v == null)
            {
                m.SetLink(Link.Scattered, null);
                return;
            }
            float reach = m.Stand + 0.9f * m.Kk + v.stats["size"] * 0.5f + 0.3f;
            if (Vector2.Distance(at, m.VG) > reach)
            {
                return;   // it stepped out from under the heel: only the wave catches it
            }
            if (!m.Touch(v))
            {
                return;
            }
            Hit h = new Hit(Blow.HeelDrop, Heel(level)) { Hurt = Hurt.DriveIn, Finisher = true, Daze = 1f + 0.3f * level, Power = 1.2f };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.HeelDrop), m.ContactPoint(0.92f), Vector2.down * 0.6f + m.Dir * 0.4f);
        }
    }

    /// <summary>
    /// A ring racing out over the ground from a heavy landing (Meteor Heel's ring of fire, Skyfall
    /// Bicycle's crash): everyone it passes is knocked flat (each once; 'skip', the one the blow itself
    /// took, is left to it), dust thrown off its edge, embers too when it is a ring of fire.
    /// </summary>
    internal sealed class TempestWave : Hazard
    {
        private readonly Vector2 _at;
        private readonly float _r, _mult, _push;
        private readonly Hue _hue;
        private readonly bool _fire;
        private float _cur, _emberT;

        private const float Speed = 15f;

        public TempestWave(ChoreoMove m, Vector2 at, float r, float mult, float push, Actor skip, Hue hue, bool fire) : base(m)
        {
            _at = at;
            _r = Mathf.Max(0.5f, r);
            _mult = mult;
            _push = push;
            _hue = hue;
            _fire = fire;
            if (skip != null)
            {
                HitIds.Add(skip.Sid());
            }
            if (Kit.Visible(at, 0.5f))
            {
                float life = _r / Speed;
                Fx.Ring(at, Px, life + 0.12f, 3f, _r / Px, 3, 1, FxLayer.Ground, 6, Art.Ground, 1.2f,
                    Fx.WithA(Color.white, 0.95f), Fx.WithA(hue.Main, 0.85f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                Fx.Ring(at, Px, life * 0.85f + 0.1f, 2f, _r / Px * 0.82f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.4f,
                    Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            float was = _cur;
            _cur = Mathf.Min(_r, _cur + Speed * dt);
            if (OwnerOk)
            {
                Choreo.Foes(A, _at, _cur + 0.3f, Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Actor u = Near[i];
                    float d = Vector2.Distance(u.current_position, _at);
                    if (d >= was - 0.5f)
                    {
                        float f = Mathf.Lerp(1f, 0.6f, d / _r);
                        Strike(u, _mult * f, Hurt.Knockdown, _at, _push * f, 0.35f, 1.1f);
                    }
                }
            }
            _emberT -= dt;
            if (_emberT <= 0f && Kit.Visible(_at, 0.5f))
            {
                _emberT = 0.025f;
                if (_fire)
                {
                    int n = Fx.Count(3);
                    for (int i = 0; i < n; i++)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                        Kit.Ember(_at + dir * _cur, Px, Random.Range(0.25f, 0.45f), dir * Random.Range(10f, 30f) * Px + new Vector2(0f, Random.Range(10f, 25f) * Px), _hue.Edge);
                    }
                }
                float b = Random.value * Mathf.PI * 2f;
                Vector2 g = _at + new Vector2(Mathf.Cos(b), Mathf.Sin(b) * Art.Ground) * _cur;
                ChoreoFx.HeelDust(g, g - _at, Px, Combat.TileAt(g), K);
            }
            return _cur < _r;
        }
    }

    /// <summary>Small things the high Tempest Leg techniques share (the prism's colours, wind rings, foe lists).</summary>
    internal static class TempestHigh
    {
        /// <summary>A reusable list (never kept across frames).</summary>
        public static readonly List<Actor> Tmp = new List<Actor>();

        private static readonly Color[] _prism =
        {
            new Color(1f, 0.32f, 0.36f, 1f),
            new Color(1f, 0.62f, 0.24f, 1f),
            new Color(1f, 0.93f, 0.36f, 1f),
            new Color(0.42f, 0.95f, 0.46f, 1f),
            new Color(0.36f, 0.9f, 1f, 1f),
            new Color(0.4f, 0.52f, 1f, 1f),
            new Color(0.78f, 0.45f, 1f, 1f)
        };

        /// <summary>The i-th colour of the rainbow (red .. violet; wraps).</summary>
        public static Color Prism(int i)
        {
            return _prism[((i % 7) + 7) % 7];
        }

        /// <summary>A flat ring of wind under a foot (a step on the air, a wind cuff).</summary>
        public static void WindRing(Vector2 at, float px, float k, Hue h, float r1 = 7f, float life = 0.22f, FxLayer layer = FxLayer.Top)
        {
            if (!Kit.Visible(at))
            {
                return;
            }
            Fx.Ring(at, px, life, 2f, r1 * k, 1, 1, layer, 4, Art.Ground, 2.2f, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Edge, 0.5f), Fx.WithA(h.Edge, 0f));
        }

        /// <summary>Sorts 'list' into a greedy path from 'from' (nearest first, then nearest to that one...).</summary>
        public static void Path(Vector2 from, List<Actor> list)
        {
            Vector2 at = from;
            for (int i = 0; i < list.Count; i++)
            {
                int best = i;
                float bd = float.MaxValue;
                for (int j = i; j < list.Count; j++)
                {
                    float d = (list[j].current_position - at).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = j;
                    }
                }
                Actor t = list[i];
                list[i] = list[best];
                list[best] = t;
                at = list[i].current_position;
            }
        }
    }
}
