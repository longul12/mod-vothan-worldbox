using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CELESTIAL LANCE (Ki Arts, God). It raises its hand and a spear of light draws itself into it
    /// out of the air, line by line, motes running down its length; it settles into a thrower's
    /// stance, sights along it at the enemy, draws back trembling, and hurls it: the lance streaks
    /// out in a straight white line faster than the eye, through everyone in its way (each one
    /// pierced, flung aside, a burning seam left in the ground behind it) for 30 tiles and more,
    /// and where it ends (or where a mountain stops it) it bursts into a blossom of light that blows
    /// out the earth. A Mirror Return can catch it and send it home.
    /// I: 30 tiles. II: 34. III: 38, and a moment after the burst the seam it left erupts in light
    /// along its whole length, from the thrower's feet to the crater.
    /// Damage (x the user's damage): each one pierced I 3.2, II 3.8, III 4.5; the burst at the end
    /// 2.6 within 3 tiles; III's eruption 1 along the seam.
    /// </summary>
    internal sealed class CelestialLanceTech : Technique
    {
        public override string Id => "celestial_lance";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Line;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Line | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 120f, 160f, 200f };
        public override float[] Cooldown => new[] { 30f, 32f, 35f };

        public static float Range(int level)
        {
            return level <= 1 ? 30f : level == 2 ? 34f : 38f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 3.2f : level == 2 ? 3.8f : 4.5f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickLine(c, this, level, Range(level), 0.6f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (c.Dist(p.Target) < 2.5f)
            {
                return null;   // a spear is thrown, not stabbed
            }
            // a crowd in a line, or one great foe far off
            p.Score *= p.Count >= 3 ? 1.25f : 1f;
            if (c.Dist(p.Target) > 8f && (Tiers.Of(p.Target) >= Tier.Master || Weights.Species(p.Target) >= Weight.Titan))
            {
                p.Score += 30f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Spear sp = new Spear();
            m.Ended2 = (mm, aborted) => sp.Clear();
            m.Face(p.Dir);
            Form shows = Form.Ki | Form.Projectile | Form.Line | Form.Heavy;
            // the hand goes up and a spear of light draws itself into it
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.22f,
                To = new Shape(-8f, 0.94f, 1.1f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/POWERS/DivineLight", mm.A.current_tile, 0.2f);
                    Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.2f);
                },
                Every = (mm, k) => sp.Form(mm, k)
            });
            // the thrower's stance: it sights along it and draws back
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.15f,
                To = new Shape(-20f, 0.9f, 1.08f, 0f, -0.1f),
                Ease = Ease.OutQuad,
                Tremble = true,
                Forms = shows,
                Every = (mm, k) => sp.Draw(mm, 1f, k)
            }.On(0.7f, mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(sp.Tip, mm.Px, 0.16f, Color.white, 1.1f);
                }
            }));
            // hurled
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.06f,
                To = new Shape(28f, 1.28f, 0.84f, 0f, 0.18f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = shows,
                Enter = mm => sp.Hurl(mm, level)
            });
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.18f, To = new Shape(18f, 1.12f, 0.92f, 0f, 0.12f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>The spear while it is in the thrower's hand.</summary>
        private sealed class Spear
        {
            private Particle _shaft, _glow, _head;
            private float _moteT;
            public Vector2 Tip, Grip;
            private Vector2 _dir;

            private void Place(ChoreoMove m, float back)
            {
                Vector2 aim = m.V != null && m.VOk ? m.VG - m.Stance : m.Aim;
                _dir = aim.sqrMagnitude > 1e-4f ? aim.normalized : new Vector2(m.Fwd, 0f);
                // (held raised over the shoulder, pointing at the foe a little upwards)
                Grip = m.BodyPos() + new Vector2(-m.Fwd * (0.1f + back) * m.Hpx, 0.45f * m.Hpx) * m.Px;
                Tip = Grip + (_dir + new Vector2(0f, 0.12f)).normalized * 1.1f * m.Kk;
            }

            /// <summary>It draws itself from the tip back, line by line, motes running down it.</summary>
            public void Form(ChoreoMove m, float k)
            {
                Place(m, 0f);
                Draw(m, Beat.Apply(Ease.OutQuad, k), 0f);
                if (!m.Vis)
                {
                    return;
                }
                _moteT -= Clock.Dt;
                if (_moteT <= 0f)
                {
                    _moteT = 0.03f;
                    Vector2 at = Vector2.Lerp(Tip, Grip - (Tip - Grip) * 0.6f, Random.value * k);
                    ChoreoFx.Motes(at, m.Px, 2, 10f * m.Kk, 0.12f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
                }
            }

            /// <summary>The spear drawn: 'k' how much of it has formed (from the tip back), 'back' the draw-back.</summary>
            public void Draw(ChoreoMove m, float k, float back)
            {
                Place(m, 0.25f * back);
                if (!m.Vis)
                {
                    return;
                }
                Vector2 butt = Grip - (Tip - Grip) * 0.6f;
                Vector2 from = Vector2.Lerp(Tip, butt, Mathf.Clamp01(k));
                float flick = 0.85f + 0.15f * Mathf.Sin(m.T * 47f);
                KiHi.Bar(ref _glow, from, Tip, m.Px, 3f * Mathf.Lerp(1f, m.Kk, 0.5f), Fx.WithA(m.Hue.Edge, 0.55f * flick), m.TopLayer, 12);
                KiHi.Bar(ref _shaft, from, Tip, m.Px, 1f, Color.Lerp(m.Hue.Core, Color.white, 0.5f), m.TopLayer, 13);
                KiHi.Blob(ref _head, Tip, m.Px, 1.6f * Mathf.Lerp(1f, m.Kk, 0.5f), Fx.WithA(Color.white, flick), m.TopLayer, 14);
            }

            /// <summary>Hurled: it leaves the hand and flies on its own (LanceHazard).</summary>
            public void Hurl(ChoreoMove m, int level)
            {
                Place(m, 0f);
                Vector2 from = Grip;
                Clear();
                Areas.Add(new LanceHazard(m, from, _dir, Range(level) * Mathf.Lerp(1f, m.Kk, 0.5f), Mult(level), 2.6f, level >= 3));
                Choreo.Sound("event:/SFX/WEAPONS/WeaponStartThrow", m.A.current_tile);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile, 0.1f);
                if (m.Vis)
                {
                    Kit.Flash(from, m.Px, Mathf.RoundToInt(5f * m.Kk), 0.08f, Color.white, m.TopLayer);
                    Fx.Ring(from, m.Px, 0.2f, 2f, 12f * m.Kk, 2, 1, m.TopLayer, 6, 0.5f, 2.4f, Fx.WithA(m.Hue.Core, 1f), Fx.WithA(m.Hue.Edge, 0f));
                    Kit.SpeedLines(from + _dir * 1.5f * m.Kk, _dir, m.Px, 4, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.9f), m.TopLayer);
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 44f, 0.5f, m.Kk);
                }
                Kit.Stop(from, 0.04f);
                Ruin.Quake(m.A, m.Pose.Ground, Scale.Light, 1f, m.Kk);
            }

            public void Clear()
            {
                KiHi.Kill(ref _shaft);
                KiHi.Kill(ref _glow);
                KiHi.Kill(ref _head);
            }
        }
    }

    /// <summary>
    /// A lance of light in flight: it streaks along its line at chest height, piercing every enemy
    /// it passes (each may answer it once: an evade lets it fly on, a parry or a catch stops it),
    /// burning a seam into the ground behind it, until its range runs out or a mountain stops it;
    /// there it bursts (a crater, everyone near thrown). An erupting one (III) then sets the whole
    /// seam alight. LanceHazard.Reaching is the lance being answered right now (a Mirror Return
    /// takes it from there).
    /// </summary>
    internal sealed class LanceHazard : Hazard
    {
        /// <summary>The lance whose contact (Combat.Reach) is being answered right now.</summary>
        public static LanceHazard Reaching;

        public Vector2 Pos, Dir;
        public readonly float Mult, BlastMult, Range;
        public readonly bool Erupts;
        public float Traveled;
        /// <summary>Caught (a Mirror Return): it stops where it is, without a burst.</summary>
        public bool Caught;

        private readonly float _z, _speed;
        private int _phase;
        private float _phaseT, _trailT, _furrowAt, _eruptT;
        private int _erupted;
        private Vector2 _end, _lastFurrow;
        private readonly List<Vector2> _seam = new List<Vector2>(24);
        private Particle _shaft, _glow, _head;

        /// <summary>'drawn': where it leaves the hand (its height kept all the way); 'range' tiles.</summary>
        public LanceHazard(ChoreoMove m, Vector2 drawn, Vector2 dir, float range, float mult, float blastMult, bool erupts) : base(m)
        {
            Pos = new Vector2(drawn.x, m.Pose.Ground.y);
            _z = Mathf.Max(0.3f * K, drawn.y - m.Pose.Ground.y);
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            Range = range;
            Mult = mult;
            BlastMult = blastMult;
            Erupts = erupts;
            _speed = 55f * Mathf.Lerp(1f, K, 0.4f);
            _lastFurrow = Pos;
            _seam.Add(Pos);
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            switch (_phase)
            {
                case 0:
                    return Fly(dt);
                case 1:
                    _phaseT += dt;
                    if (_phaseT > 0.14f && Erupts && _seam.Count > 1 && OwnerOk)
                    {
                        _phase = 2;
                        _phaseT = 0f;
                        HitIds.Clear();
                        return true;
                    }
                    return _phaseT < 0.5f;
                default:
                    return Erupt(dt);
            }
        }

        private bool Fly(float dt)
        {
            if (Caught)
            {
                return false;
            }
            Vector2 last = Pos;
            float step = Mathf.Min(_speed * dt, Range - Traveled);
            Vector2 next = Pos + Dir * step;
            // a mountain or a wall stops it; so does the edge of the world
            if (!Inside(next) || Blocked(next))
            {
                Burst(last);
                return true;
            }
            Pos = next;
            Traveled += step;
            if (OwnerOk && step > 0f)
            {
                Pierce(last, Pos);
                if (Caught || Done)
                {
                    KillLance();
                    return false;
                }
                if (_phase != 0)
                {
                    return true;   // (a parry stopped it: it burst there)
                }
            }
            Seam(last);
            Draw(last, dt);
            if (Traveled >= Range - 0.01f)
            {
                Burst(Pos);
            }
            return true;
        }

        private static bool Inside(Vector2 p)
        {
            return p.x >= 1f && p.y >= 1f && p.x < MapBox.width - 1 && p.y < MapBox.height - 1;
        }

        private static bool Blocked(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t != null && t.Type != null && t.Type.block;
        }

        /// <summary>Everyone between 'a' and 'b': each may answer it once; the rest are pierced.</summary>
        private void Pierce(Vector2 a, Vector2 b)
        {
            Choreo.FoesOnLine(A, a, b, 0.45f * Mathf.Lerp(1f, K, 0.5f), Near, true, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                long id = u.Sid();
                if (HitIds.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                {
                    continue;
                }
                HitIds.Add(id);
                LanceHazard was = Reaching;
                Reaching = this;
                Contact c;
                try
                {
                    c = Combat.Reach(M, u, false);
                }
                finally
                {
                    Reaching = was;
                }
                if (Caught)
                {
                    return;   // (taken in someone's palms)
                }
                if (c == Contact.Evaded)
                {
                    continue;
                }
                if (c == Contact.Parried)
                {
                    Burst(u.current_position);
                    return;
                }
                HitResult r = Combat.Hit(A, u, Mult * cm, true, false, 0f, M);
                Vector2 side = Choreo.Perp(Dir) * (Vector2.Dot(u.current_position - Pos, Choreo.Perp(Dir)) >= 0f ? 1f : -1f);
                Impact im = Impact.Of(Hurt.Launch, Dir * 0.7f + side * 0.7f, 2.2f, 1.6f);
                im.Lift = 1.3f;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.07f);
                Vector2 at = Choreo.BodyAt(u, 0.5f);
                if (Kit.Visible(at))
                {
                    ChoreoFx.Impact(at, Dir, Px, K, 1.7f, Hue, FxLayer.Top, true);
                    Kit.Flash(at, Px, Mathf.RoundToInt(4f * K), 0.06f, Color.white);
                }
                Kit.Stop(at, 0.025f);
                Choreo.Sound("event:/SFX/HIT/HitFlesh", u.current_tile);
                Choreo.Sound("event:/SFX/HIT/HitGeneric", u.current_tile, 0.05f);
            }
        }

        /// <summary>The seam it burns into the ground behind it.</summary>
        private void Seam(Vector2 last)
        {
            if (Kit.Visible(Pos, 0.3f) || Kit.Visible(last, 0.3f))
            {
                Kit.Line(last, Pos, Px, 0.9f, 1, FxLayer.Ground, 4, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
            }
            if (Traveled - _furrowAt < 1.6f || !OwnerOk)
            {
                return;
            }
            _furrowAt = Traveled;
            Ruin.Furrow(A, _lastFurrow, Pos, 0.45f, Scale, A.current_position);
            _lastFurrow = Pos;
            if (_seam.Count < 40 && Vector2.Distance(_seam[_seam.Count - 1], Pos) >= 1.9f)
            {
                _seam.Add(Pos);
            }
        }

        private void Draw(Vector2 last, float dt)
        {
            Vector2 head = Pos + new Vector2(0f, _z);
            if (!Kit.Visible(head, 0.4f) && !Kit.Visible(last + new Vector2(0f, _z), 0.4f))
            {
                KillLance();
                return;
            }
            Vector2 tail = head - Dir * 1.8f * K;
            float flick = 0.85f + 0.15f * Mathf.Sin(Age * 53f);
            KiHi.Bar(ref _glow, tail, head, Px, 3f * Mathf.Lerp(1f, K, 0.5f), Fx.WithA(Hue.Edge, 0.6f * flick), FxLayer.Top, 12);
            KiHi.Bar(ref _shaft, tail, head, Px, 1f, Color.Lerp(Hue.Core, Color.white, 0.5f), FxLayer.Top, 13);
            KiHi.Blob(ref _head, head, Px, 1.8f * Mathf.Lerp(1f, K, 0.5f), Color.white, FxLayer.Top, 14);
            _trailT -= dt;
            if (_trailT <= 0f)
            {
                _trailT = 0.02f;
                Kit.Line(tail - Dir * 2.5f * K, tail, Px, 0.14f, 1, FxLayer.Top, 10, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        /// <summary>It ends: a blossom of light, the earth blown out, everyone near thrown.</summary>
        private void Burst(Vector2 at)
        {
            KillLance();
            _phase = 1;
            _phaseT = 0f;
            _end = at;
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            if (Kit.Visible(at, 0.5f))
            {
                Vector2 c = at + new Vector2(0f, _z);
                Kit.ImpactFrame(c, Px, 1.3f * K, Hue.Pal, false);
                Kit.Flash(c, Px, Mathf.RoundToInt(14f * K), 0.14f, Color.white);
                Fx.Ring(at, Px, 0.45f, 4f, 34f * K, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.3f, Fx.WithA(Color.white, 1f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
                // petals of light thrown out of the blossom
                int n = Fx.Count(10);
                for (int i = 0; i < n; i++)
                {
                    float a = (i + Random.value * 0.4f) / n * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Particle p = Fx.Spawn(ChoreoArt.Petal(i), c, Px, Random.Range(0.4f, 0.7f), FxLayer.Top, 15);
                    p?.Moving(d * Random.Range(40f, 90f) * Px * K, Random.Range(10f, 30f) * Px, 3f, 40f * Px).Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                Kit.Crater(at, Px * K * 1.2f, tile, true, 7f, false, Hue.Edge);
                Kit.Debris(at, Px, tile, 14, K);
                Kit.DustRing(at, Px, tile, 16, 64f, 0.6f, K);
            }
            if (!OwnerOk)
            {
                return;
            }
            Kit.Stop(at, 0.06f);
            Ruin.Quake(A, at, Scale, 1.5f, K);
            Ruin.Blast(A, at, 2.2f, Scale, A.current_position);
            HitIds.Clear();   // (those it pierced near the end are caught in the burst too)
            float r = 3f * Mathf.Lerp(1f, K, 0.5f);
            Choreo.Foes(A, at, r, Near, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float d = Mathf.Clamp01(Vector2.Distance(u.current_position, at) / r);
                Strike(u, BlastMult * Mathf.Lerp(1f, 0.6f, d), Hurt.Launch, at, Mathf.Lerp(3f, 1.8f, d), Mathf.Lerp(2.2f, 1.2f, d), 1.5f);
            }
            if (_seam.Count == 0 || Vector2.Distance(_seam[_seam.Count - 1], at) > 0.8f)
            {
                _seam.Add(at);
            }
        }

        /// <summary>III: the seam erupts in light, point by point from the thrower to the crater.</summary>
        private bool Erupt(float dt)
        {
            _eruptT -= dt;
            while (_eruptT <= 0f && _erupted < _seam.Count)
            {
                _eruptT += 0.035f;
                Vector2 at = _seam[_erupted++];
                WorldTile tile = Combat.TileAt(at);
                if (Kit.Visible(at, 0.3f))
                {
                    Kit.Line(at, at + new Vector2(0f, 2.4f * K), Px, 0.22f, 2, FxLayer.Top, 12, Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    Kit.Flash(at + new Vector2(0f, 0.3f * K), Px, Mathf.RoundToInt(5f * K), 0.1f, Fx.WithA(Hue.Core, 0.95f));
                    Kit.DustRing(at, Px, tile, 5, 30f, 0.4f, K);
                }
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.08f);
                if (!OwnerOk)
                {
                    continue;
                }
                Ruin.Blast(A, at, 0.6f, Scale, A.current_position, false);
                Choreo.Foes(A, at, 1.2f * Mathf.Lerp(1f, K, 0.5f), Near);
                for (int i = 0; i < Near.Count; i++)
                {
                    Strike(Near[i], 1f, Hurt.Launch, at, 0.8f, 1.6f, 1.1f);
                }
            }
            return _erupted < _seam.Count;
        }

        private void KillLance()
        {
            KiHi.Kill(ref _shaft);
            KiHi.Kill(ref _glow);
            KiHi.Kill(ref _head);
        }

        public override void Kill()
        {
            KillLance();
        }
    }
}
