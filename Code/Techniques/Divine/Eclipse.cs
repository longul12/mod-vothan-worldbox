using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ECLIPSE (Divine, God). The God raises a fist to the sky and the sun goes out: high above it a
    /// black disk slides across a white sun, the corona flares round its rim, and the world darkens
    /// (Charging: a fast blow breaks it). In the dark only the God burns: a black figure ringed in
    /// fire, like the eclipse itself. Then it strikes from the dark, each of its enemies around it
    /// once: it is gone, and a flash of corona light bursts at a foe, then at the next, and the next,
    /// each one left reeling with a burning crescent on it. Back where it began, the moon slides off
    /// the sun: the diamond ring flashes, light returns, and every one of them falls at once.
    /// Unlike Eternal Second (a grey frozen world it walks through) the world goes on in the dark:
    /// its enemies can still answer each blow. I: 7 tiles, 5 foes; II: 9 tiles, 8; III: 12 tiles,
    /// 12 and the cinematic dark (in light mode a local darkness falls round it instead).
    /// Damage (x the user's damage) per foe: the blow I 1, II 1.25, III 1.5; the fall I 0.4, II 0.5, III 0.7.
    /// </summary>
    internal sealed class EclipseTech : Technique
    {
        public override string Id => "eclipse";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Chain;
        public override Form Forms => Form.Teleport | Form.Punch | Form.Kick | Form.Flurry | Form.Area | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 140f, 200f, 260f };
        public override float[] Cooldown => new[] { 110f, 125f, 140f };

        public static float Radius(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 9f : 12f;
        }

        public static int Foes(int level)
        {
            return level <= 1 ? 5 : level == 2 ? 8 : 12;
        }

        public static float Blow(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.25f : 1.5f;
        }

        public static float Fall(int level)
        {
            return level <= 1 ? 0.4f : level == 2 ? 0.5f : 0.7f;
        }

        private static readonly List<KeyValuePair<float, Actor>> _rank = new List<KeyValuePair<float, Actor>>();

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int max = Foes(level);
            float threat = 0f, best = -1f;
            Actor main = null;
            _rank.Clear();
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Combat.Grounded(v) || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                float t = c.Threat(v);
                threat += t;
                if (t > best)
                {
                    best = t;
                    main = v;
                }
                _rank.Add(new KeyValuePair<float, Actor>(c.Value(v, c.Expected(v, Blow(level) + Fall(level)), cm), v));
            }
            if (main == null)
            {
                return null;
            }
            int n = _rank.Count;
            if (!c.Forced)
            {
                bool worthy = Tiers.Of(main) >= Tier.Master || best >= 0.3f;
                if (n < (level <= 1 ? 2 : 3) && !(worthy && n >= 2) || !DivineArts.Warranted(c, main, n, threat))
                {
                    return null;
                }
            }
            // the ones worth the most, as many as it strikes
            _rank.Sort((x, y) => y.Key.CompareTo(x.Key));
            float score = 0f;
            for (int i = 0; i < _rank.Count && i < max; i++)
            {
                score += _rank[i].Key;
            }
            score *= DivineArts.Affinity(c.Self, Id);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 d = main.current_position - c.Pos;
            return new Plan
            {
                Target = main,
                Point = c.Pos,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = Mathf.Min(n, max),
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
            EclipseMove m = new EclipseMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Eclipse's move: the sun going out, the strikes from the dark, the diamond ring.</summary>
    internal sealed class EclipseMove : ChoreoMove
    {
        private static readonly Color Dusk = new Color(0.03f, 0.02f, 0.06f, 1f);

        private readonly float _r;
        private readonly int _max;
        private readonly Vector2 _home;
        private readonly List<Actor> _foes = new List<Actor>(12);
        private readonly List<long> _ids = new List<long>(12);
        private readonly HashSet<long> _landed = new HashSet<long>();
        private readonly List<Particle> _marks = new List<Particle>(12);
        private readonly List<int> _markGen = new List<int>(12);
        private Particle _sun, _moon, _corona, _dark, _rim, _body;
        private float _night, _lastT, _rayT;
        private bool _veil, _released;
        private Hue _holy;
        private static readonly List<Actor> _scan = new List<Actor>();

        public EclipseMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _r = EclipseTech.Radius(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            _max = EclipseTech.Foes(Level);
            _home = Pose.Ground;
            _holy = Hue.As(Element.Holy);
        }

        private Vector2 Sky => _home + new Vector2(0f, (4.6f + 0.3f * Level) * Kk);

        public void Build()
        {
            // the sun goes out
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.7f : 0.55f,
                To = new Shape(-6f, 0.92f, 1.12f, 0f),
                Ease = Ease.OutCubic,
                Charging = true,
                Track = false,
                Forms = Form.Area | Form.Charge | Form.Ultimate,
                Enter = m => ((EclipseMove)m).EnterDark(),
                Every = (m, k) => ((EclipseMove)m).TickDark(k),
                Exit = m => ((EclipseMove)m).Gather()
            });
            // from the dark, each one of them once
            for (int i = 0; i < _max; i++)
            {
                int ix = i;
                Add(new Beat
                {
                    Tempo = Tempo.Travel,
                    Dur = 0.07f,
                    Route = Route.Blink,
                    Dest = m => ((EclipseMove)m).SpotBy(ix),
                    Intangible = true,
                    Track = false,
                    Forms = Form.Teleport | Form.Area | Form.Ultimate,
                    If = m => ((EclipseMove)m).Has(ix),
                    Enter = m => ((EclipseMove)m).Vanish(ix)
                });
                bool kick = i % 3 == 2;
                Beat blow = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.1f,
                    To = kick ? new Shape(-24f, 1.12f, 0.9f, 0.05f, 0.12f) : new Shape(20f, 1.2f, 0.86f, 0f, 0.15f),
                    Ease = Ease.OutExpo,
                    Forms = (kick ? Form.Kick : Form.Punch) | Form.Teleport | Form.Flurry | Form.Ultimate,
                    If = m => ((EclipseMove)m).Has(ix)
                };
                blow.On(0.35f, m => ((EclipseMove)m).StrikeFoe(ix, kick));
                Add(blow);
            }
            // home, and the diamond ring
            Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.08f,
                Route = Route.Blink,
                Dest = m => ((EclipseMove)m).Home(),
                Intangible = true,
                Track = false,
                Forms = Form.Teleport | Form.Area | Form.Ultimate
            });
            Add(new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.3f,
                To = new Shape(10f, 1.1f, 0.92f, 0f, 0.05f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Area | Form.Ultimate,
                Enter = m => ((EclipseMove)m).Release(),
                Every = (m, k) => ((EclipseMove)m).TickDawn(k)
            });
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

        // ----------------------------------------------------------- the dark --

        private void EnterDark()
        {
            _lastT = 0f;
            _veil = DivineArts.Open(this, "eclipse", 0.05f);
            Choreo.Sound("event:/SFX/POWERS/Curse", A.current_tile, 0.3f);
            MakeBody();
        }

        private void TickDark(float k)
        {
            float dt = Dt();
            if (k > 0.8f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            }
            Night(Beat.Apply(Ease.InOutSine, k));
            DrawSun(1f - Beat.Apply(Ease.OutCubic, k), k);
            if (!Vis)
            {
                return;
            }
            _rayT -= dt;
            if (_rayT <= 0f && k > 0.5f)
            {
                _rayT = 0.06f;
                // the corona's streamers
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float r0 = 9f * Px * Kk;
                Kit.Line(Sky + dir * r0, Sky + dir * r0 * Random.Range(1.6f, 2.4f), Px, 0.25f, 1, TopLayer, 3,
                    Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0f));
            }
        }

        /// <summary>The world darkens to 'k' (the cinematic's veil, or a darkness round it in light mode).</summary>
        private void Night(float k)
        {
            _night = k;
            if (_veil)
            {
                // (the cinematic's veil is the night; once it ends the veil lifts by itself)
                if (Cinematic)
                {
                    Cinema.Dim(0.72f * k, Dusk, 0.05f);
                }
                return;
            }
            if (!Kit.Visible(_home, 0.8f))
            {
                return;
            }
            if (_dark == null || !_dark.Alive)
            {
                _dark = Glue(Fx.Spawn(Art.Disk(140, 1f, 0.45f), _home, Px, 1f, FxLayer.Top, 0));
            }
            if (_dark != null)
            {
                float size = (_r + 4f) / (140f * Px);
                _dark.Pos = _home;
                _dark.Size0 = _dark.Size1 = size;
                _dark.Grad = null;
                _dark.R.color = Fx.WithA(Dusk, 0.62f * k);
            }
        }

        /// <summary>The sun high above, the moon sliding over it ('cover' 1 apart .. 0 covered), its corona 'glow'.</summary>
        private void DrawSun(float cover, float glow)
        {
            if (!Kit.Visible(Sky, 0.4f))
            {
                return;
            }
            int sr = Mathf.RoundToInt(8f * Kk + Level);
            if (_sun == null || !_sun.Alive)
            {
                _sun = Glue(Fx.Spawn(Art.Disk(sr, 1f, 0.1f), Sky, Px, 1f, TopLayer, 2));
                _corona = Glue(Fx.Spawn(Art.Ring(sr + 2, 2, 1f), Sky, Px, 1f, TopLayer, 3));
                _moon = Glue(Fx.Spawn(Art.Disk(sr, 1f, 0f), Sky, Px, 1f, TopLayer, 4));
            }
            if (_sun != null)
            {
                _sun.Pos = Sky;
                _sun.Grad = null;
                _sun.R.color = Color.Lerp(_holy.Core, Color.white, 0.4f);
            }
            if (_corona != null)
            {
                _corona.Pos = Sky;
                _corona.Size0 = _corona.Size1 = 1f + 0.35f * glow + 0.05f * Mathf.Sin(T * 20f);
                _corona.Grad = null;
                _corona.R.color = Fx.WithA(_holy.Edge, 0.25f + 0.75f * glow);
            }
            if (_moon != null)
            {
                _moon.Pos = Sky + new Vector2(sr * 2.3f, sr * 0.6f) * Px * cover;
                _moon.Grad = null;
                _moon.R.color = Dusk;
            }
        }

        /// <summary>In the dark it is a black figure ringed in fire (drawn over the dark, under nothing).</summary>
        private void MakeBody()
        {
            Sprite s = Fx.UnitSprite(A);
            if (s == null)
            {
                return;
            }
            _rim = Glue(Fx.Spawn(s, DrawPos(), 1f, 1f, TopLayer, 20, true));
            _body = Glue(Fx.Spawn(s, DrawPos(), 1f, 1f, TopLayer, 21, true));
            if (_rim != null)
            {
                _rim.UseEuler = true;
            }
            if (_body != null)
            {
                _body.UseEuler = true;
            }
        }

        public override void Frame()
        {
            float k = _night;
            float flick = 0.85f + 0.15f * Mathf.Sin(T * 31f);
            Shade(_rim, 1.14f, Fx.WithA(Color.Lerp(_holy.Edge, Hue.Rim, 0.3f), Mathf.Clamp01(k * 1.2f) * flick));
            Shade(_body, 1f, Fx.WithA(Dusk, Mathf.Clamp01(k * 1.1f) * 0.92f));
        }

        private void Shade(Particle p, float scale, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            if (s != null && (object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = DrawPos();
            p.Z = 0f;
            p.Euler = Fx.UnitEuler(this);
            p.SX = A.actor_scale * Pose.SX * scale;
            p.SY = A.actor_scale * Pose.SY * scale;
            p.Grad = null;
            p.R.color = c;
            p.Tr.position = new Vector3(p.Pos.x, p.Pos.y, -0.02f);
            p.Tr.localScale = new Vector3(p.SX, p.SY, 1f);
            p.Tr.eulerAngles = p.Euler;
            p.R.enabled = c.a > 0.003f && !Pose.Hidden && (A.is_visible || Fx.OnScreen(Pose.Ground, 0.1f));
        }

        // -------------------------------------------------------- the strikes --

        /// <summary>Everyone it will strike (on the ground, with a place to stand by them), in a path from one to the next.</summary>
        private void Gather()
        {
            _foes.Clear();
            _ids.Clear();
            Choreo.Foes(A, _home, _r, _scan, false, true);
            Vector2 from = _home;
            while (_foes.Count < _max)
            {
                Actor next = null;
                float bd = float.MaxValue;
                for (int i = 0; i < _scan.Count; i++)
                {
                    Actor u = _scan[i];
                    if (u == null || _foes.Contains(u) || !Choreo.Fair(A, u, out _, true))
                    {
                        continue;
                    }
                    float d = (u.current_position - from).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        next = u;
                    }
                }
                if (next == null)
                {
                    break;
                }
                _scan.Remove(next);
                if (!Combat.FindLanding(next.current_position, 1.2f, out _))
                {
                    continue;   // nowhere to stand by it
                }
                _foes.Add(next);
                _ids.Add(next.Sid());
                from = next.current_position;
            }
            if (Dev.On) Dev.Trace("eclipse " + Techniques.Roman(Level) + " strikes " + _foes.Count + " within " + _r.ToString("F1"));
        }

        public bool Has(int i)
        {
            return i < _foes.Count && _foes[i] != null && _foes[i].Sid() == _ids[i] && Combat.Living(_foes[i]) && Choreo.Hittable(_foes[i]);
        }

        public Vector2 SpotBy(int i)
        {
            if (!Has(i))
            {
                return Stance;
            }
            Vector2 p = _foes[i].current_position;
            Vector2 back = Stance - p;
            back = back.sqrMagnitude > 1e-4f ? back.normalized : -Dir;
            Vector2 want = Combat.Clamp(p + back * 0.62f * Kk);
            if (Combat.SafeSpot(want) || Choreo.Wading(want))
            {
                return want;
            }
            return Combat.FindLanding(want, 1.2f, out Vector2 s) ? s : Stance;
        }

        public Vector2 Home()
        {
            return Combat.SafeSpot(_home) || Choreo.Wading(_home) ? _home : Stance;
        }

        /// <summary>It is gone (a pop of corona light where it stood) and will be by foe 'i'.</summary>
        public void Vanish(int i)
        {
            if (Has(i))
            {
                SetVictim(_foes[i]);
            }
            if (Vis)
            {
                Fx.Ring(Stance, Px, 0.18f, 2f, 9f * Kk, 1, 1, TopLayer, 6, Art.Ground, 2f, Fx.WithA(_holy.Edge, 0.9f), Fx.WithA(_holy.Edge, 0f));
            }
        }

        /// <summary>The blow from the dark on foe 'i': a flash of corona, the foe reeling with a burning crescent on it.</summary>
        public void StrikeFoe(int i, bool kick)
        {
            if (!Has(i))
            {
                return;
            }
            Actor u = _foes[i];
            Vector2 at = Choreo.BodyAt(u, kick ? 0.35f : 0.62f);
            Vector2 dir = u.current_position - Stance;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Dir;
            Face(dir);
            Impact im = Impact.Of(i % 2 == 0 ? Hurt.SnapBack : Hurt.Reel, dir, 0.45f, 1.2f);
            im.Daze = 0.3f;
            HitResult r = DivineArts.Smite(this, u, EclipseTech.Blow(Level), im, true);
            Choreo.Sound(kick ? "event:/SFX/HIT/HitGeneric" : "event:/SFX/OTHER/Punch", u.current_tile);
            if (!r.Landed)
            {
                return;
            }
            _landed.Add(u.Sid());
            if (Kit.Visible(at, 0.2f))
            {
                float px = Px;
                Kit.Flash(at, px, Mathf.RoundToInt(5f * Kk), 0.08f, Fx.WithA(Color.white, 1f), TopLayer, 30);
                Fx.Ring(at, px, 0.22f, 2f, 12f * Kk, 2, 1, TopLayer, 29, 1f, 2.2f, Fx.WithA(_holy.Core, 1f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                ChoreoFx.Impact(at, dir, px, Kk, 1.3f, _holy, TopLayer);
                Kit.SpeedLines(at, dir, px, 3, 4f, 0.1f, Fx.WithA(_holy.Edge, 0.9f), TopLayer);
                // the burning crescent it leaves on the foe
                Particle mark = Fx.Spawn(ChoreoArt.Crescent(Mathf.RoundToInt(5f * Choreo.KOf(u)), 2), at, Choreo.PxOf(u), 3f, TopLayer, 28);
                if (mark != null)
                {
                    mark.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    mark.Color(Fx.WithA(_holy.Core, 1f), Fx.WithA(_holy.Edge, 0.9f));
                    mark.Cine = Cinematic;
                    long id = u.Sid();
                    float up = Choreo.SpritePx(u) * Choreo.PxOf(u) * 0.55f;
                    mark.Step = (q, d) =>
                    {
                        if (u.Sid() == id && u.isAlive())
                        {
                            q.Pos = (Vector2)Choreo.Feet(u) + new Vector2(0f, up);
                        }
                        q.Rot += 120f * d;
                    };
                    _marks.Add(mark);
                    _markGen.Add(mark.Gen);
                }
            }
            if (Combat.Grounded(u))
            {
                Ruin.Blast(A, u.current_position, 0.25f, Scale, Stance, false, -2);
            }
        }

        // ------------------------------------------------------ the diamond ring --

        /// <summary>The moon slides off: the diamond ring, the light back, and every one it struck falls at once.</summary>
        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            _lastT = 0f;
            Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", A.current_tile, 0.2f);
            if (Kit.Visible(Sky, 0.4f))
            {
                Vector2 ring = Sky + new Vector2(-6f, 5f) * Px * Kk;
                Fx.Glint(ring, Px, 0.45f, Color.white, 3f + Level);
                Kit.Flash(ring, Px, Mathf.RoundToInt(12f * Kk), 0.2f, Fx.WithA(Color.white, 1f), TopLayer, 30);
            }
            if (_veil && Cinematic)
            {
                EndCinematic();
            }
            int fell = 0;
            float mult = EclipseTech.Fall(Level);
            for (int i = 0; i < _foes.Count; i++)
            {
                if (!Has(i) || !_landed.Contains(_ids[i]))
                {
                    continue;   // (one that slipped its blow in the dark doesn't fall)
                }
                Actor u = _foes[i];
                Vector2 away = u.current_position - _home;
                Impact im = Impact.Of(Hurt.Knockdown, away.sqrMagnitude > 1e-4f ? away : Vector2.up, 0.8f, 1.5f);
                im.Daze = 0.9f;
                HitResult r = DivineArts.Smite(this, u, mult, im, true);
                if (r.Landed)
                {
                    fell++;
                    if (Kit.Visible(u.current_position, 0.2f))
                    {
                        Vector2 b = Choreo.BodyAt(u, 0.55f);
                        Kit.Flash(b, Choreo.PxOf(u), 5, 0.1f, Fx.WithA(_holy.Core, 0.95f), TopLayer, 25);
                        Kit.Sparks(b, Choreo.PxOf(u), 5, 50f, 110f, _holy.Edge);
                    }
                }
            }
            for (int i = 0; i < _marks.Count; i++)
            {
                Particle p = _marks[i];
                if (p != null && p.Alive && p.Gen == _markGen[i])
                {
                    p.Step = null;
                    p.Life = Mathf.Min(p.Life, p.Age + 0.12f);
                    p.Sized(1f, 2.2f);
                }
            }
            if (fell > 0)
            {
                Kit.Stop(_home, 0.07f);
                Ruin.Quake(A, _home, Scale, 1.2f, Kk);
                SetLink(Link.Scattered, null);
            }
        }

        private void TickDawn(float k)
        {
            float n = 1f - Beat.Apply(Ease.OutQuad, k);
            _night = n;
            Night(n);
            DrawSun(Beat.Apply(Ease.InQuad, k), n);
            if (_sun != null && _sun.Alive)
            {
                _sun.R.color = Fx.WithA(_sun.R.color, n);
                if (_moon != null && _moon.Alive)
                {
                    _moon.R.color = Fx.WithA(Dusk, n);
                }
                if (_corona != null && _corona.Alive)
                {
                    _corona.R.color = Fx.WithA(_holy.Edge, n);
                }
            }
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            for (int i = 0; i < _marks.Count; i++)
            {
                Particle p = _marks[i];
                if (p != null && p.Alive && p.Gen == _markGen[i])
                {
                    p.Step = null;
                    p.Life = Mathf.Min(p.Life, p.Age + 0.2f);
                }
            }
            _marks.Clear();
            _markGen.Clear();
            _foes.Clear();
            _ids.Clear();
            _landed.Clear();
        }
    }
}
