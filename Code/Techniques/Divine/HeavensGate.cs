using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HEAVEN'S GATE (Divine, God). The God points to the sky over the enemy (Charging: a fast blow
    /// breaks it) and a gate takes shape up there on a bank of cloud: two pillars and an arch of
    /// gold, its doors shut. The God's arm sweeps forward, the doors swing open on a blinding light,
    /// and out of it the weapons of every school rain down on its enemies, slanting from the gate:
    /// crimson fists, cyan wind blades, steel swords that stick quivering in the earth, gold orbs of
    /// ki, amber boulders, violet kunai and green hunting spears, each striking as its school does.
    /// The God is free to fight on while its gate rains. At II and III a last, giant sword of light
    /// falls out of the gate onto the most dangerous of them (a crater, an impact frame) and the
    /// doors close. Unlike Blade Rain (a Thousand Blades storm of swords straight down) it is every
    /// school's motif out of one gate in the sky; unlike Starfall, no fire.
    /// I: 5 tiles, 12 weapons; II: 7 tiles, 22 and the sword; III: 9 tiles, 36, the cinematic.
    /// Damage (x the user's damage) per weapon I 0.4, II 0.5, III 0.6; the giant sword II 2.2, III 2.8.
    /// </summary>
    internal sealed class HeavensGateTech : Technique
    {
        public override string Id => "heavens_gate";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Projectile | Form.Blade | Form.Area | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 150f, 220f, 300f };
        public override float[] Cooldown => new[] { 130f, 145f, 160f };

        public static float Radius(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 7f : 9f;
        }

        public static int Weapons(int level)
        {
            return level <= 1 ? 12 : level == 2 ? 22 : 36;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.4f : level == 2 ? 0.5f : 0.6f;
        }

        public static float Sword(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 2.2f : 2.8f;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 13f : 16f;
        }

        private static readonly List<Actor> _in = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float r = Radius(level) * k;
            Vector2 at;
            Actor center;
            int count;
            if (c.Forced && c.Focus != null)
            {
                center = c.Focus;
                at = center.current_position;
                count = c.CountNear(at, r);
            }
            else
            {
                at = c.Cluster(r * 0.8f, 2f, Range(level) * k, out count, out center);
            }
            if (center == null)
            {
                return null;
            }
            _in.Clear();
            c.CountNear(at, r, _in);
            float threat = DivineArts.ThreatOf(c, _in);
            Actor main = center;
            float best = c.Threat(center);
            for (int i = 0; i < _in.Count; i++)
            {
                float t = c.Threat(_in[i]);
                if (t > best)
                {
                    best = t;
                    main = _in[i];
                }
            }
            if (!c.Forced)
            {
                bool worthy = Tiers.Of(main) >= Tier.Master || best >= 0.3f;
                if (count < (level <= 1 ? 3 : 4) && !worthy || !DivineArts.Warranted(c, main, count, threat))
                {
                    return null;
                }
            }
            // each one under the gate takes a share of the weapons (about two of them, more in a small crowd)
            float each = Mult(level) * Mathf.Clamp(Weapons(level) * 0.7f / Mathf.Max(1, _in.Count), 1f, 3f);
            float score = 0f;
            for (int i = 0; i < _in.Count; i++)
            {
                Actor v = _in[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, each), cm);
            }
            if (level >= 2)
            {
                c.Allowed(main, out float mcm);
                score += c.Value(main, c.Expected(main, Sword(level)), mcm) * 0.6f;
            }
            score *= DivineArts.Affinity(c.Self, Id);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 d = at - c.Pos;
            return new Plan
            {
                Target = main,
                Point = at,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = Mathf.Max(1, _in.Count),
                Score = score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            GateMove m = new GateMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>The gate in the sky (its frame, doors, light, cloud), shared by the move that opens it and the rain that follows.</summary>
    internal sealed class GateSprites
    {
        public Particle Frame, DoorL, DoorR, Light, Halo;
        public readonly List<Particle> Clouds = new List<Particle>(6);
        private readonly List<int> _gens = new List<int>(10);
        public Vector2 Ground;
        public float H, Px, K;
        public bool Built;

        public Vector2 Sky => Ground + new Vector2(0f, H);

        /// <summary>How high over the ground the middle of the gate's opening hangs (where the weapons come out).</summary>
        public float MouthH => H + GateArt.Tall * 0.38f * Px * K;

        /// <summary>The middle of the gate's opening.</summary>
        public Vector2 Mouth => Ground + new Vector2(0f, MouthH);

        public void Build(FxLayer layer)
        {
            if (Built)
            {
                return;
            }
            Built = true;
            float px = Px * K;
            Light = Track(Fx.Spawn(Art.Disk(Mathf.RoundToInt(GateArt.Wide * 0.42f), 1.2f, 0.5f), Mouth, px, 1f, layer, 1));
            Frame = Track(Fx.Spawn(GateArt.Frame(), Sky, px, 1f, layer, 3));
            DoorL = Track(Fx.Spawn(GateArt.Door(false), Sky, px, 1f, layer, 2));
            DoorR = Track(Fx.Spawn(GateArt.Door(true), Sky, px, 1f, layer, 2));
            Halo = Track(Fx.Spawn(Art.Ring(Mathf.RoundToInt(GateArt.Wide * 0.75f), 1, 0.35f), Sky, px, 1f, layer, 0));
            for (int i = 0; i < 5; i++)
            {
                Particle c = Track(Fx.Spawn(Art.Dust[i % Art.Dust.Length][0], Sky + new Vector2((i - 2) * 9f * px, Random.Range(-1f, 1f) * px), px * 2.2f, 1f, layer, 4));
                if (c != null)
                {
                    Clouds.Add(c);
                }
            }
        }

        private Particle Track(Particle p)
        {
            if (p != null)
            {
                p.Held = true;
                _gens.Add(p.Gen);
            }
            return p;
        }

        /// <summary>Draws it: 'alpha' its visibility, 'open' 0 shut .. 1 open, 'light' the glow inside.</summary>
        public void Draw(float alpha, float open, float light, Color gold, Color core, float t)
        {
            if (!Built)
            {
                return;
            }
            Set(Frame, Sky, Fx.WithA(gold, alpha));
            Vector2 doorY = Sky;
            float half = GateArt.Inner * 0.5f * Px * K;
            if (DoorL != null && DoorL.Alive)
            {
                DoorL.SX = Mathf.Max(0.02f, 1f - open);
                Set(DoorL, doorY + new Vector2(-half, 0f), Fx.WithA(Color.Lerp(gold, Color.white, 0.2f), alpha * (1f - 0.4f * open)));
            }
            if (DoorR != null && DoorR.Alive)
            {
                DoorR.SX = Mathf.Max(0.02f, 1f - open);
                Set(DoorR, doorY + new Vector2(half, 0f), Fx.WithA(Color.Lerp(gold, Color.white, 0.2f), alpha * (1f - 0.4f * open)));
            }
            if (Light != null && Light.Alive)
            {
                Light.Size0 = Light.Size1 = 0.6f + 0.5f * light + 0.04f * Mathf.Sin(t * 30f);
                Set(Light, Mouth, Fx.WithA(core, alpha * Mathf.Clamp01(0.15f + light)));
            }
            if (Halo != null && Halo.Alive)
            {
                Halo.Rot += 0f;
                Set(Halo, Sky + new Vector2(0f, GateArt.Tall * 0.5f * Px * K), Fx.WithA(gold, alpha * (0.2f + 0.5f * light)));
            }
            for (int i = 0; i < Clouds.Count; i++)
            {
                Particle c = Clouds[i];
                if (c != null && c.Alive)
                {
                    float drift = Mathf.Sin(t * 1.5f + i * 1.7f) * 2f * Px * K;
                    Set(c, Sky + new Vector2((i - 2) * 9f * Px * K + drift, -1.5f * Px * K), Fx.WithA(Color.white, alpha * 0.85f));
                }
            }
        }

        private static void Set(Particle p, Vector2 at, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = at;
            p.Grad = null;
            p.R.color = c;
        }

        /// <summary>Keeps (or stops keeping) its particles above a cinematic's veil.</summary>
        public void Cine(bool on)
        {
            SetCine(Frame, on);
            SetCine(DoorL, on);
            SetCine(DoorR, on);
            SetCine(Light, on);
            SetCine(Halo, on);
            for (int i = 0; i < Clouds.Count; i++)
            {
                SetCine(Clouds[i], on);
            }
        }

        private static void SetCine(Particle p, bool on)
        {
            if (p != null && p.Alive)
            {
                p.Cine = on;
            }
        }

        public void Kill()
        {
            KillOne(ref Frame);
            KillOne(ref DoorL);
            KillOne(ref DoorR);
            KillOne(ref Light);
            KillOne(ref Halo);
            for (int i = 0; i < Clouds.Count; i++)
            {
                Particle c = Clouds[i];
                KillOne(ref c);
            }
            Clouds.Clear();
            Built = false;
        }

        private static void KillOne(ref Particle p)
        {
            if (p != null && p.Alive)
            {
                p.Kill();
            }
            p = null;
        }
    }

    /// <summary>Heaven's Gate's move: the gate summoned, the doors thrown open; then the rain plays on by itself (GateHazard).</summary>
    internal sealed class GateMove : ChoreoMove
    {
        private readonly GateSprites _gate = new GateSprites();
        private readonly Vector2 _at;
        private readonly float _r;
        private bool _handed;
        private float _lastT, _rayT;
        private Hue _holy;
        private readonly Actor _main;

        public GateMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _at = Combat.Clamp(p.Point);
            _r = HeavensGateTech.Radius(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            _holy = Hue.As(Element.Holy);
            _main = p.Target;
            _gate.Ground = _at;
            _gate.H = (5.6f + 0.5f * Level) * Kk;
            _gate.Px = Px;
            _gate.K = Kk * (0.8f + 0.15f * Level);
            SetVictim(null);
            Face(_at - Pose.Ground);
        }

        private Color Gold => Color.Lerp(_holy.Edge, GodFx.Gold, 0.5f);

        public void Build()
        {
            // the God points to the sky over them; the gate takes shape
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.7f : 0.55f,
                To = new Shape(-12f, 0.92f, 1.14f, 0f, -0.03f),
                Ease = Ease.OutCubic,
                Charging = true,
                Track = false,
                Forms = Form.Projectile | Form.Area | Form.Charge,
                Enter = m => ((GateMove)m).EnterSummon(),
                Every = (m, k) => ((GateMove)m).TickSummon(k)
            });
            // the arm sweeps forward: the doors swing open
            Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.3f,
                To = new Shape(12f, 1.12f, 0.92f, 0f, 0.08f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Projectile | Form.Blade | Form.Area | Form.Ultimate,
                Enter = m => Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile, 0.2f),
                Every = (m, k) => ((GateMove)m).TickOpen(k),
                Exit = m => ((GateMove)m).HandOver()
            });
            Hold(0.2f, false, Tempo.Follow);
            Recover(0.3f);
        }

        private float Dt()
        {
            float dt = BeatT - _lastT;
            if (dt < 0f)
            {
                dt = BeatT;
            }
            _lastT = BeatT;
            return dt;
        }

        private void EnterSummon()
        {
            _lastT = 0f;
            if (Level >= 2)
            {
                DivineArts.Open(this, "heavens_gate", 0.4f, 0.6f, 0.5f);
            }
            Choreo.Sound("event:/SFX/POWERS/DivineMagnet", A.current_tile, 0.2f);
        }

        private void TickSummon(float k)
        {
            float dt = Dt();
            if (k > 0.8f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            }
            if (!Kit.Visible(_gate.Sky, 0.5f) && !Vis)
            {
                return;
            }
            _gate.Build(TopLayer);
            _gate.Cine(Cinematic);
            _gate.Draw(Beat.Apply(Ease.OutQuad, k), 0f, 0.1f * k, Gold, _holy.Core, T);
            _rayT -= dt;
            if (_rayT <= 0f)
            {
                _rayT = 0.05f;
                // a thread of light from its raised hand to the gate
                Vector2 hand = BodyPos() + new Vector2(Fwd * 0.3f, Hpx * 0.8f) * Px;
                Kit.Line(hand, Vector2.Lerp(hand, _gate.Sky, 0.35f + 0.6f * k), Px, 0.08f, 1, TopLayer, 6,
                    Fx.WithA(_holy.Core, 0.8f * k), Fx.WithA(_holy.Edge, 0f));
                ChoreoFx.Motes(_gate.Mouth, Px, 2, 22f * _gate.K, 0.2f, _holy.Core, _holy.Edge, TopLayer);
            }
        }

        private void TickOpen(float k)
        {
            if (!_gate.Built)
            {
                return;
            }
            float open = Beat.Apply(Ease.OutCubic, k);
            _gate.Cine(Cinematic);
            _gate.Draw(1f, open, open, Gold, _holy.Core, T);
            if (k > 0.3f && Kit.Visible(_gate.Mouth, 0.3f) && Random.value < 0.5f)
            {
                // rays pouring out of the opening
                float a = Random.Range(-140f, -40f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Kit.Line(_gate.Mouth, _gate.Mouth + dir * Random.Range(2f, 4f) * Kk, Px, 0.18f, 1, TopLayer, 5,
                    Fx.WithA(Color.white, 0.8f), Fx.WithA(_holy.Core, 0f));
            }
        }

        /// <summary>The doors are open: the rain goes on by itself; the God is free.</summary>
        private void HandOver()
        {
            if (_handed)
            {
                return;
            }
            _handed = true;
            if (Kit.Visible(_gate.Mouth, 0.3f))
            {
                Kit.Flash(_gate.Mouth, Px, Mathf.RoundToInt(10f * _gate.K), 0.15f, Fx.WithA(Color.white, 1f), TopLayer, 12);
            }
            _gate.Cine(false);
            Areas.Add(new GateHazard(this, _gate, _r, _main));
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            if (!_handed)
            {
                _gate.Kill();   // (broken before it opened: the gate fades with it)
            }
        }
    }

    /// <summary>
    /// The rain out of Heaven's Gate: the weapons of every school, slanting down from the gate onto
    /// the God's enemies, each striking as its school does; then the giant sword (II, III) and the
    /// doors closing. Plays on by itself after the God's move; stops early if the God falls.
    /// </summary>
    internal sealed class GateHazard : Hazard
    {
        private enum Kind
        {
            Fist,
            Wind,
            Sword,
            Orb,
            Boulder,
            Kunai,
            Spear
        }

        private sealed class Drop
        {
            public Kind Kind;
            public Vector2 From, To;
            public float T, Fall, Stick;
            public bool Down;
            public Particle Body, Shadow;
            public Color C;
        }

        private readonly GateSprites _gate;
        private readonly float _r, _dur;
        private readonly int _count, _level;
        private readonly List<Drop> _drops = new List<Drop>();
        private readonly Kind[] _order;
        private int _made;
        private float _next;
        private Hue _holy;
        private readonly Actor _main;
        private readonly long _mainId;
        private Drop _sword;
        private bool _swordDone, _closing;
        private float _closeT;

        public GateHazard(ChoreoMove m, GateSprites gate, float r, Actor main) : base(m)
        {
            _gate = gate;
            _r = r;
            _level = Mathf.Clamp(m.Level, 1, 3);
            _count = HeavensGateTech.Weapons(_level);
            _dur = _level <= 1 ? 1.1f : _level == 2 ? 1.4f : 1.7f;
            _holy = Hue.As(Element.Holy);
            _main = main;
            _mainId = main != null ? main.Sid() : -1;
            // every school in turn, in an order of its own each time
            _order = new[] { Kind.Fist, Kind.Wind, Kind.Sword, Kind.Orb, Kind.Boulder, Kind.Kunai, Kind.Spear };
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Kind t = _order[i];
                _order[i] = _order[j];
                _order[j] = t;
            }
        }

        private Color Gold => Color.Lerp(_holy.Edge, GodFx.Gold, 0.5f);

        public override bool Tick(float dt)
        {
            Age += dt;
            bool vis = Kit.Visible(_gate.Ground, 0.8f) || Kit.Visible(_gate.Sky, 0.5f);
            if (!OwnerOk && !_closing)
            {
                _closing = true;   // the God fell: the gate shuts
            }
            // the rain
            _next -= dt;
            while (!_closing && _made < _count && _next <= 0f && Age <= _dur + 0.2f)
            {
                _next += _dur / _count;
                Spawn(_order[_made % _order.Length]);
                _made++;
            }
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                Drop d = _drops[i];
                d.T += dt;
                float k = Mathf.Clamp01(d.T / d.Fall);
                if (vis)
                {
                    Render(d, k);
                }
                if (k >= 1f && !d.Down)
                {
                    d.Down = true;
                    Land(d, vis);
                }
                if (d.Down && d.T > d.Fall + d.Stick)
                {
                    d.Body?.Kill();
                    d.Shadow?.Kill();
                    _drops.RemoveAt(i);
                }
                else if (d.Down && d.Body != null && d.Body.Alive && d.Stick > 0f)
                {
                    float fade = Mathf.Clamp01((d.T - d.Fall) / d.Stick);
                    d.Body.Grad = null;
                    d.Body.R.color = Fx.WithA(d.C, 1f - fade * fade);
                }
            }
            // the giant sword
            if (!_closing && _made >= _count && _level >= 2 && !_swordDone && Age >= _dur + 0.25f)
            {
                _swordDone = true;
                SpawnSword();
            }
            if (_sword != null)
            {
                TickSword(dt, vis);
            }
            bool rainDone = _made >= _count || _closing;
            bool swordDone = _level < 2 || _swordDone && _sword == null || _closing;
            if (rainDone && swordDone && _drops.Count == 0 && !_closing)
            {
                _closing = true;
            }
            // the gate itself: open while it rains, then the doors close and it fades
            if (_closing)
            {
                _closeT += dt;
            }
            float shut = Mathf.Clamp01(_closeT / 0.35f);
            float alpha = 1f - Mathf.Clamp01((_closeT - 0.3f) / 0.4f);
            if (vis)
            {
                _gate.Draw(alpha, 1f - shut, (1f - shut) * (0.8f + 0.2f * Mathf.Sin(Age * 20f)), Gold, _holy.Core, Age);
            }
            return !(_closing && _closeT >= 0.75f && _drops.Count == 0 && _sword == null);
        }

        // -------------------------------------------------------------- the rain --

        private void Spawn(Kind kind)
        {
            Vector2 to = Pick();
            float side = (Random.value - 0.5f) * GateArt.Inner * 0.8f * _gate.Px * _gate.K;
            Drop d = new Drop
            {
                Kind = kind,
                From = _gate.Ground + new Vector2(side, 0f),
                To = to,
                Fall = Random.Range(0.28f, 0.38f) * (kind == Kind.Boulder ? 1.15f : 1f),
                Stick = kind == Kind.Sword || kind == Kind.Spear ? 0.7f : 0.05f,
                C = ColorOf(kind)
            };
            _drops.Add(d);
        }

        private Vector2 Pick()
        {
            Choreo.Foes(A, _gate.Ground, _r, Near);
            if (Near.Count > 0 && Random.value < 0.72f)
            {
                Actor u = Near[Random.Range(0, Near.Count)];
                return u.current_position + Random.insideUnitCircle * 0.25f;
            }
            return Combat.Clamp(_gate.Ground + Random.insideUnitCircle * _r);
        }

        private Color ColorOf(Kind k)
        {
            switch (k)
            {
                case Kind.Fist: return Color.Lerp(Schools.Color(School.IronFist), Color.white, 0.25f);
                case Kind.Wind: return Color.Lerp(Schools.Color(School.TempestLeg), Color.white, 0.3f);
                case Kind.Sword: return Color.Lerp(Schools.Color(School.ThousandBlades), Color.white, 0.2f);
                case Kind.Orb: return Color.Lerp(Schools.Color(School.KiArts), Color.white, 0.25f);
                case Kind.Boulder: return Color.Lerp(Schools.Color(School.EarthBreaker), Color.white, 0.15f);
                case Kind.Kunai: return Color.Lerp(Schools.Color(School.PhantomStep), Color.white, 0.25f);
                default: return Color.Lerp(Schools.Color(School.HuntersWay), Color.white, 0.25f);
            }
        }

        private Sprite SpriteOf(Kind k)
        {
            int s = Mathf.Max(1, Mathf.RoundToInt(K));
            switch (k)
            {
                case Kind.Fist: return ChoreoArt.Fist(5 * s + 1);
                case Kind.Wind: return ChoreoArt.Crescent(6 * s, 2);
                case Kind.Sword: return ChoreoArt.LightSword(11 * s);
                case Kind.Orb: return Art.Disk(3 * s, 1f, 0.25f);
                case Kind.Boulder: return ChoreoArt.Boulder(4 * s, Random.Range(0, 4));
                case Kind.Kunai: return GateArt.Kunai();
                default: return GateArt.Spear();
            }
        }

        /// <summary>Where a falling weapon is: from the gate's mouth down a slant to its spot on the ground.</summary>
        private Vector2 At(Drop d, float k, out Vector2 vel)
        {
            float e = k * k * (0.4f + 0.6f * k);
            float h = _gate.MouthH * (1f - e);
            Vector2 ground = Vector2.Lerp(d.From, d.To, e);
            Vector2 next = Vector2.Lerp(d.From, d.To, Mathf.Min(1f, e + 0.05f));
            float hn = _gate.MouthH * (1f - Mathf.Min(1f, e + 0.05f));
            vel = next + new Vector2(0f, hn) - (ground + new Vector2(0f, h));
            return ground + new Vector2(0f, h);
        }

        private void Render(Drop d, float k)
        {
            if (d.Down && d.Stick <= 0.06f)
            {
                return;
            }
            if (d.Body == null || !d.Body.Alive)
            {
                d.Body = Fx.Spawn(SpriteOf(d.Kind), d.From, Px, 1f, FxLayer.Top, 14);
                if (d.Body == null)
                {
                    return;
                }
                d.Body.Held = true;
                d.Shadow = Fx.Spawn(Art.Disk(Mathf.RoundToInt(2f * K), Art.Ground, 0.4f), d.To, Px, 1f, FxLayer.Ground, 3);
                if (d.Shadow != null)
                {
                    d.Shadow.Held = true;
                }
            }
            if (d.Down)
            {
                return;   // stuck in the ground where it landed
            }
            Vector2 pos = At(d, k, out Vector2 vel);
            float ang = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            d.Body.Pos = pos;
            d.Body.Grad = null;
            d.Body.R.color = Color.Lerp(Color.white, d.C, Mathf.Clamp01(k * 2f));
            switch (d.Kind)
            {
                case Kind.Sword:
                    d.Body.Rot = ang + 90f;
                    break;
                case Kind.Boulder:
                    d.Body.Rot += 9f;
                    break;
                case Kind.Orb:
                    d.Body.Rot = 0f;
                    break;
                default:
                    d.Body.Rot = ang;
                    break;
            }
            if (d.Shadow != null && d.Shadow.Alive)
            {
                d.Shadow.Pos = d.To;
                d.Shadow.Grad = null;
                d.Shadow.R.color = new Color(0f, 0f, 0f, 0.1f + 0.3f * k);
            }
            if (Random.value < 0.5f)
            {
                Kit.Line(pos, pos - vel.normalized * 4f * Px * K, Px, 0.06f, 1, FxLayer.Top, 13, Fx.WithA(d.C, 0.8f), Fx.WithA(d.C, 0f));
            }
        }

        private void Land(Drop d, bool vis)
        {
            d.Shadow?.Kill();
            d.Shadow = null;
            Vector2 at = d.To;
            WorldTile t = Combat.TileAt(at);
            Vector2 from = d.From;
            float mult = HeavensGateTech.Mult(_level);
            float blast = 0.7f;
            Hurt hurt;
            float push;
            string snd;
            switch (d.Kind)
            {
                case Kind.Fist:
                    hurt = Hurt.Knockdown;
                    push = 0.5f;
                    snd = "event:/SFX/OTHER/Punch";
                    break;
                case Kind.Wind:
                    hurt = Hurt.Reel;
                    push = 0.9f;
                    snd = "event:/SFX/HIT/HitFlesh";
                    break;
                case Kind.Sword:
                    hurt = Hurt.Fold;
                    push = 0.2f;
                    snd = "event:/SFX/HIT/HitSwordSword";
                    break;
                case Kind.Orb:
                    hurt = Hurt.Knockdown;
                    push = 0.7f;
                    blast = 0.9f;
                    snd = "event:/SFX/EXPLOSIONS/ExplosionSmall";
                    break;
                case Kind.Boulder:
                    hurt = Hurt.Knockdown;
                    push = 0.8f;
                    blast = 1f;
                    mult *= 1.2f;
                    snd = "event:/SFX/WEAPONS/WeaponRockLand";
                    break;
                case Kind.Kunai:
                    hurt = Hurt.Flinch;
                    push = 0.1f;
                    blast = 0.5f;
                    mult *= 0.8f;
                    snd = "event:/SFX/HIT/HitMetal";
                    break;
                default:
                    hurt = Hurt.Reel;
                    push = 0.6f;
                    snd = "event:/SFX/HIT/HitFlesh";
                    break;
            }
            Choreo.Sound(snd, t, 0.05f);
            Choreo.Foes(A, at, blast, Near, true);
            for (int i = 0; i < Near.Count; i++)
            {
                HitIds.Remove(Near[i].Sid());   // (each weapon strikes anew)
                Strike(Near[i], mult, hurt, from, push, -1f, 1f);
            }
            if (vis)
            {
                switch (d.Kind)
                {
                    case Kind.Fist:
                        Fx.HitSpark(at + new Vector2(0f, 0.3f), Px, 2, Vector2.up, 4);
                        Kit.DustRing(at, Px, t, 6, 30f, 0.4f, K);
                        break;
                    case Kind.Wind:
                        Kit.Line(at - new Vector2(0.5f, 0.2f) * K, at + new Vector2(0.5f, 0.2f) * K, Px, 0.12f, 1, FxLayer.Top, 15, Color.white, Fx.WithA(d.C, 0f));
                        ChoreoFx.HeelDust(at, d.To - d.From, Px, t, K);
                        break;
                    case Kind.Sword:
                    case Kind.Spear:
                        Kit.Sparks(at, Px, 4, 30f, 70f, d.C);
                        Kit.DustRing(at, Px, t, 4, 20f, 0.35f, K);
                        break;
                    case Kind.Orb:
                        Kit.Flash(at + new Vector2(0f, 0.2f), Px, Mathf.RoundToInt(4f * K), 0.1f, Fx.WithA(d.C, 0.95f));
                        Fx.Ring(at, Px, 0.25f, 2f, 9f * K, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(d.C, 0f));
                        break;
                    case Kind.Boulder:
                        Kit.Debris(at, Px, t, 6, K);
                        Kit.Crater(at, Px * K, t, false, 4f, true, d.C);
                        Kit.DustRing(at, Px, t, 8, 36f, 0.5f, K);
                        break;
                    default:
                        Kit.Sparks(at, Px, 3, 40f, 80f, d.C);
                        break;
                }
            }
            if (d.Stick <= 0.06f || !vis)
            {
                d.Body?.Kill();
                d.Body = null;
            }
            else if (d.Body != null && d.Body.Alive)
            {
                // stuck in the earth, slanting the way it fell
                d.Body.Pos = at + new Vector2(0f, d.Kind == Kind.Sword ? 0.05f : 0.1f) * K;
            }
            // every building a weapon lands on takes it; the heavy ones break the ground
            int extra = d.Kind == Kind.Boulder ? -1 : -2;
            Ruin.Blast(A, at, d.Kind == Kind.Boulder ? 0.35f : 0.22f, Scale, A != null && A.isAlive() ? A.current_position : (Vector2?)null, false, extra);
        }

        // ---------------------------------------------------------- the giant sword --

        private void SpawnSword()
        {
            Vector2 to;
            if (_main != null && _main.Sid() == _mainId && Combat.Living(_main) && (_main.current_position - _gate.Ground).sqrMagnitude <= _r * _r * 1.6f)
            {
                to = _main.current_position;
            }
            else
            {
                to = Pick();
            }
            _sword = new Drop { Kind = Kind.Sword, From = _gate.Ground, To = to, Fall = 0.34f, Stick = 1f, C = Color.Lerp(_holy.Core, Color.white, 0.4f) };
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(to), 0.1f);
        }

        private void TickSword(float dt, bool vis)
        {
            Drop d = _sword;
            d.T += dt;
            float k = Mathf.Clamp01(d.T / d.Fall);
            if (vis)
            {
                if (d.Body == null || !d.Body.Alive)
                {
                    d.Body = Fx.Spawn(ChoreoArt.LightSword(Mathf.RoundToInt(34f * K)), d.From, Px * 1.25f, 1f, FxLayer.Top, 16);
                    if (d.Body != null)
                    {
                        d.Body.Held = true;
                    }
                    d.Shadow = Fx.Spawn(Art.Disk(Mathf.RoundToInt(6f * K), Art.Ground, 0.4f), d.To, Px, 1f, FxLayer.Ground, 3);
                    if (d.Shadow != null)
                    {
                        d.Shadow.Held = true;
                    }
                }
                if (!d.Down && d.Body != null)
                {
                    Vector2 pos = At(d, k, out Vector2 vel);
                    d.Body.Pos = pos;
                    d.Body.Rot = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg + 90f;
                    d.Body.Grad = null;
                    d.Body.R.color = d.C;
                    Kit.Line(pos, pos - vel.normalized * 10f * Px * K, Px, 0.08f, 2, FxLayer.Top, 15, Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0f));
                }
                if (d.Shadow != null && d.Shadow.Alive)
                {
                    d.Shadow.Pos = d.To;
                    d.Shadow.Grad = null;
                    d.Shadow.R.color = new Color(0f, 0f, 0f, 0.15f + 0.35f * k);
                }
            }
            if (k >= 1f && !d.Down)
            {
                d.Down = true;
                SwordLands(d, vis);
            }
            if (d.Down)
            {
                float fade = Mathf.Clamp01((d.T - d.Fall) / d.Stick);
                if (d.Body != null && d.Body.Alive)
                {
                    d.Body.Grad = null;
                    d.Body.R.color = Fx.WithA(d.C, 1f - fade * fade);
                }
                if (fade >= 1f)
                {
                    d.Body?.Kill();
                    d.Shadow?.Kill();
                    _sword = null;
                }
            }
        }

        private void SwordLands(Drop d, bool vis)
        {
            d.Shadow?.Kill();
            d.Shadow = null;
            Vector2 at = d.To;
            WorldTile t = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", t, 0.2f);
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", t, 0.1f);
            float mult = HeavensGateTech.Sword(_level);
            Choreo.Foes(A, at, 3f * K, Near, true);
            for (int i = 0; i < Near.Count; i++)
            {
                Actor u = Near[i];
                float dist = Vector2.Distance(u.current_position, at);
                HitIds.Remove(u.Sid());
                if (dist <= 1.3f * K)
                {
                    Strike(u, mult, Combat.Grounded(u) ? Hurt.DriveIn : Hurt.Knockdown, at, 0.3f, -1f, 2f);
                }
                else
                {
                    Strike(u, mult * 0.4f, Hurt.Knockdown, at, 1.2f, -1f, 1.2f);
                }
            }
            if (vis)
            {
                Kit.ImpactFrame(at + new Vector2(0f, 0.4f * K), Px, 1.1f * K, _holy.Pal, false, FxLayer.Top);
                Kit.Crater(at, Px * K * 1.5f, t, false, 6f, false, _holy.Edge);
                Kit.Shockwave(at, Px, 4f, 3f * K / Px, 0.45f, Fx.WithA(_holy.Core, 0.95f), 3);
                Kit.Debris(at, Px, t, 12, K * 1.3f);
                Kit.DustRing(at, Px, t, 16, 60f, 0.7f, K * 1.3f);
                if (d.Body != null && d.Body.Alive)
                {
                    d.Body.Pos = at + new Vector2(0f, 0.1f * K);
                }
            }
            Kit.Stop(at, 0.08f);
            Ruin.Blast(A, at, 0.7f, Scale, A != null && A.isAlive() ? A.current_position : (Vector2?)null, false);
            Ruin.Quake(A, at, Scale, 1.4f, K);
        }

        public override void Kill()
        {
            for (int i = 0; i < _drops.Count; i++)
            {
                _drops[i].Body?.Kill();
                _drops[i].Shadow?.Kill();
            }
            _drops.Clear();
            if (_sword != null)
            {
                _sword.Body?.Kill();
                _sword.Shadow?.Kill();
                _sword = null;
            }
            _gate.Kill();
        }
    }

    /// <summary>
    /// Heaven's Gate's pixel art: the gate's frame (two pillars, an arch, a lintel), its two doors
    /// (each pivoting on its pillar), a kunai and a hunting spear. White and grey, lit from the top
    /// left, tinted by the renderer; cached.
    /// </summary>
    internal static class GateArt
    {
        /// <summary>The frame's size and the opening between the pillars (pixels).</summary>
        public const int Wide = 44, Tall = 58, Inner = 28;

        private static Sprite _frame, _doorL, _doorR, _kunai, _spear;

        public static Sprite Frame()
        {
            if (_frame != null)
            {
                return _frame;
            }
            Canvas c = new Canvas(Wide, Tall);
            int pillar = (Wide - Inner) / 2;   // 8
            int top = 34;                      // where the arch springs
            for (int x = 0; x < Wide; x++)
            {
                for (int y = 0; y < Tall; y++)
                {
                    bool left = x < pillar, right = x >= Wide - pillar;
                    if ((left || right) && y >= 2 && y < top)
                    {
                        int lx = left ? x : x - (Wide - pillar);
                        float v = lx == 0 ? 1f : lx >= pillar - 2 ? 0.62f : 0.82f;
                        if (y == 2 || y == top - 1 || (y - 2) % 8 == 0)
                        {
                            v = Mathf.Min(1f, v + 0.1f);   // bands round the column
                        }
                        c.Set(x, y, v, 1f);
                    }
                    // the plinths
                    if ((left || right) && y < 2)
                    {
                        c.Set(x, y, 0.7f, 1f);
                    }
                }
            }
            // the arch: a band between the pillar tops
            float cx = (Wide - 1) * 0.5f;
            float rOut = Wide * 0.5f, rIn = Inner * 0.5f;
            for (int x = 0; x < Wide; x++)
            {
                for (int y = top; y < Tall; y++)
                {
                    float dx = x - cx, dy = y - top;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= rOut && d >= rIn)
                    {
                        float v = d > rOut - 1.2f ? 1f : d < rIn + 1.2f ? 0.62f : 0.82f;
                        c.Set(x, y, v, 1f);
                    }
                }
            }
            // a keystone and a lintel line
            for (int x = (int)cx - 2; x <= (int)cx + 2; x++)
            {
                for (int y = top + (int)rIn - 1; y < Tall; y++)
                {
                    c.Set(x, y, 1f, 1f);
                }
            }
            _frame = c.ToSprite("sakuga_divine_gate", new Vector2(0.5f, 0f));
            return _frame;
        }

        /// <summary>A door panel: the left one pivots on its left edge, the right one on its right edge.</summary>
        public static Sprite Door(bool right)
        {
            if (right ? _doorR != null : _doorL != null)
            {
                return right ? _doorR : _doorL;
            }
            int w = Inner / 2, h = 34;
            Canvas c = new Canvas(w, h);
            for (int x = 0; x < w; x++)
            {
                for (int y = 2; y < h; y++)
                {
                    bool border = x == 0 || x == w - 1 || y == 2 || y == h - 1;
                    bool panel = (y == 10 || y == 22) && x > 1 && x < w - 2 || (x == 3 || x == w - 4) && y > 4 && y < h - 3;
                    c.Set(x, y, border ? 1f : panel ? 0.66f : 0.84f, 1f);
                }
            }
            // a ring handle by the meeting edge
            int hx = right ? 2 : w - 3;
            c.Set(hx, 16, 0.55f, 1f);
            c.Set(hx, 17, 0.55f, 1f);
            Sprite s = c.ToSprite("sakuga_divine_gate_door" + (right ? "_r" : "_l"), new Vector2(right ? 1f : 0f, 0f));
            if (right)
            {
                _doorR = s;
            }
            else
            {
                _doorL = s;
            }
            return s;
        }

        /// <summary>A kunai pointing right, pivot at its middle.</summary>
        public static Sprite Kunai()
        {
            if (_kunai != null)
            {
                return _kunai;
            }
            Canvas c = new Canvas(9, 5);
            for (int x = 0; x < 9; x++)
            {
                if (x <= 2)
                {
                    c.Set(x, 2, 0.62f, 1f);   // the grip
                }
                else if (x == 3)
                {
                    c.Set(x, 1, 0.8f, 1f);
                    c.Set(x, 2, 0.8f, 1f);
                    c.Set(x, 3, 0.8f, 1f);
                }
                else
                {
                    int half = Mathf.Max(0, (8 - x) / 2);
                    for (int y = 2 - half; y <= 2 + half; y++)
                    {
                        c.Set(x, y, y > 2 ? 1f : 0.85f, 1f);
                    }
                }
            }
            c.Set(0, 1, 0.7f, 1f);
            c.Set(0, 3, 0.7f, 1f);
            _kunai = c.ToSprite("sakuga_divine_kunai", new Vector2(0.5f, 0.5f));
            return _kunai;
        }

        /// <summary>A hunting spear pointing right, pivot at its middle.</summary>
        public static Sprite Spear()
        {
            if (_spear != null)
            {
                return _spear;
            }
            Canvas c = new Canvas(17, 5);
            for (int x = 0; x < 12; x++)
            {
                c.Set(x, 2, 0.7f, 1f);   // the shaft
            }
            for (int x = 12; x < 17; x++)
            {
                int half = Mathf.Max(0, (16 - x) / 2);
                for (int y = 2 - half; y <= 2 + half; y++)
                {
                    c.Set(x, y, y > 2 ? 1f : 0.86f, 1f);
                }
            }
            c.Set(1, 1, 0.9f, 1f);   // fletching
            c.Set(1, 3, 0.9f, 1f);
            c.Set(0, 0, 0.9f, 1f);
            c.Set(0, 4, 0.9f, 1f);
            _spear = c.ToSprite("sakuga_divine_spear", new Vector2(0.5f, 0.5f));
            return _spear;
        }
    }
}
