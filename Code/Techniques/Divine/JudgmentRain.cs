using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// JUDGMENT RAIN (Divine, God). The God raises one hand to the sky (Charging: a fast blow breaks
    /// it) and a halo of light opens high above the battlefield, motes streaming up from its palm.
    /// Then every one of its enemies in a huge circle is marked: a sigil kindles under each one's
    /// feet, nearest first, a faint line of light above it. The hand comes down like a verdict and
    /// the columns fall, one on each mark in a ripple outward: a white pillar from the sky to the
    /// ground (flyers too), a flash, the foe knocked flat and dazed, the ground scorched under it.
    /// At III the halo flares and a last, great column falls on the most dangerous of them (a
    /// crater, an impact frame). Nothing falls where no enemy stands: no fire, no ally ever touched.
    /// Unlike Starfall (stars and fire on crowds, chosen by the gauge) it is precise, vertical and
    /// cold, and the planner chooses it against an army or a worthy foe with company.
    /// I: 9 tiles, 12 marks; II: 12 tiles, 24 marks; III: 16 tiles, 40 marks and the last judgment.
    /// Damage (x the user's damage) per column: I 1, II 1.25, III 1.5; the last judgment II 1.8, III 2.6.
    /// </summary>
    internal sealed class JudgmentRainTech : Technique
    {
        public override string Id => "judgment_rain";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Area | Form.Charge | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 150f, 220f, 300f };
        public override float[] Cooldown => new[] { 120f, 135f, 150f };

        public static float Radius(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 12f : 16f;
        }

        public static int Marks(int level)
        {
            return level <= 1 ? 12 : level == 2 ? 24 : 40;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.25f : 1.5f;
        }

        public static float Finale(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 1.8f : 2.6f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced && !DivineArts.Ready(c.Self) || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            int max = Marks(level);
            int n = 0;
            float threat = 0f, score = 0f, mainT = -1f;
            Actor main = null;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                if (++n > max)
                {
                    break;
                }
                float t = c.Threat(v);
                threat += t;
                if (t > mainT)
                {
                    mainT = t;
                    main = v;
                }
                score += c.Value(v, c.Expected(v, Mult(level)), cm);
            }
            n = Mathf.Min(n, max);
            if (main == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                bool worthy = Tiers.Of(main) >= Tier.Master || mainT >= 0.3f;
                if (n < (level <= 1 ? 3 : 4) && !worthy || !DivineArts.Warranted(c, main, n, threat))
                {
                    return null;
                }
            }
            if (level >= 2)
            {
                c.Allowed(main, out float mcm);
                score += c.Value(main, c.Expected(main, Finale(level)), mcm) * 0.6f;
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
                Count = n,
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
            JudgmentMove m = new JudgmentMove(a, this, p);
            m.Build();
            if (!m.Start())
            {
                return false;
            }
            DivineArts.Mark(a);
            return true;
        }
    }

    /// <summary>Judgment Rain's move: the call, the marks, the columns, the last judgment.</summary>
    internal sealed class JudgmentMove : ChoreoMove
    {
        private readonly List<Actor> _marks = new List<Actor>(40);
        private readonly List<long> _ids = new List<long>(40);
        private readonly List<Vector2> _last = new List<Vector2>(40);
        private readonly List<Particle> _sigils = new List<Particle>(40);
        private readonly List<int> _sigilGen = new List<int>(40);
        private readonly float _r;
        private readonly int _max;
        private Actor _main;
        private long _mainId = -1;
        private int _shown, _fired;
        private Particle _halo, _haloIn;
        private float _moteT, _lastT;
        private Hue _holy;

        public JudgmentMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _r = JudgmentRainTech.Radius(Level) * Mathf.Lerp(1f, Kk, 0.5f);
            _max = JudgmentRainTech.Marks(Level);
            _holy = Hue.As(Element.Holy);
        }

        /// <summary>Where the halo hangs: high over the God.</summary>
        private Vector2 Sky => Pose.Ground + new Vector2(0f, (4.5f + 0.4f * Level) * Kk);

        public void Build()
        {
            // the call: one hand raised to the sky, a halo opening above the battlefield
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Level >= 3 ? 0.85f : 0.65f,
                To = new Shape(-8f, 0.9f, 1.15f, 0f),
                Ease = Ease.OutCubic,
                Charging = true,
                Forms = Form.Ki | Form.Area | Form.Charge,
                Enter = m => ((JudgmentMove)m).EnterCall(),
                Every = (m, k) => ((JudgmentMove)m).TickCall(k)
            });
            // the marks kindle under every enemy, nearest first
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = Level >= 3 ? 0.42f : 0.32f,
                To = new Shape(-10f, 0.88f, 1.17f, 0f),
                Ease = Ease.InOutSine,
                Tremble = true,
                Forms = Form.Ki | Form.Area | Form.Projectile,
                Enter = m => ((JudgmentMove)m).Gather(),
                Every = (m, k) => ((JudgmentMove)m).TickMarks(k)
            });
            // the verdict: the hand comes down and the columns fall in a ripple outward
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.3f + 0.012f * _max,
                To = new Shape(18f, 1.14f, 0.86f, 0f, 0.08f),
                Ease = Ease.OutExpo,
                Forms = Form.Ki | Form.Projectile | Form.Area | Form.Ultimate,
                Enter = m => Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile),
                Every = (m, k) => ((JudgmentMove)m).TickColumns(k)
            });
            // III: the last judgment on the most dangerous of them
            Add(new Beat
            {
                Tempo = Tempo.Impact,
                Dur = 0.22f,
                To = new Shape(24f, 1.2f, 0.82f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Forms = Form.Ki | Form.Projectile | Form.Ultimate,
                If = m => m.Level >= 2 && ((JudgmentMove)m).MainOk,
                Enter = m => ((JudgmentMove)m).LastJudgment()
            });
            Hold(0.12f, false, Tempo.Follow);
            Recover(0.3f);
        }

        private bool MainOk => _main != null && _main.Sid() == _mainId && Combat.Living(_main);

        // ---------------------------------------------------------------- call --

        private void EnterCall()
        {
            DivineArts.Open(this, "judgment_rain", 0.5f);
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            _lastT = 0f;
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 10, 24f, 0.6f, Kk);
            }
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

        private void TickCall(float k)
        {
            float dt = Dt();
            if (k > 0.8f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.6f);
            }
            if (!Vis)
            {
                return;
            }
            Halo(Mathf.Lerp(0.2f, 1f, Beat.Apply(Ease.OutCubic, k)), 0.35f + 0.5f * k);
            _moteT -= dt;
            if (_moteT <= 0f)
            {
                _moteT = Mathf.Lerp(0.05f, 0.02f, k);
                // light streaming up from the raised palm into the sky
                Vector2 palm = BodyPos() + new Vector2(Fwd * 0.5f, Hpx * 0.85f) * Px;
                int n = Fx.Count(2);
                for (int i = 0; i < n; i++)
                {
                    Particle p = Fx.Spawn(Art.Pixel, palm + new Vector2(Random.Range(-1.5f, 1.5f), 0f) * Px, Px, 0.3f, TopLayer, 8);
                    p?.Moving(new Vector2(Random.Range(-6f, 6f), Random.Range(60f, 110f)) * Px * Kk).Sized(1.6f, 0.8f)
                        .Color(Fx.WithA(_holy.Core, 1f), Fx.WithA(_holy.Edge, 0.7f), Fx.WithA(_holy.Edge, 0f));
                }
            }
        }

        /// <summary>The halo high above: a flat ring of light (and a fainter inner one) at 'size' of its full width.</summary>
        private void Halo(float size, float alpha)
        {
            if (!Vis && !Kit.Visible(Sky, 0.4f))
            {
                return;
            }
            int rx = Mathf.Clamp(Mathf.RoundToInt(_r * 0.42f * size / Px), 4, 140);
            if (_halo == null || !_halo.Alive)
            {
                _halo = Glue(Fx.Spawn(Art.Ring(rx, 2, 0.3f), Sky, Px, 1f, TopLayer, 3));
                _haloIn = Glue(Fx.Spawn(Art.Ring(Mathf.Max(3, rx - 5), 1, 0.3f, 12), Sky, Px, 1f, TopLayer, 4));
            }
            if (_halo != null)
            {
                Sprite s = Art.Ring(rx, 2, 0.3f);
                if ((object)_halo.R.sprite != s)
                {
                    _halo.R.sprite = s;
                    _halo.Frames[0] = s;
                }
                _halo.Pos = Sky;
                _halo.Grad = null;
                _halo.R.color = Fx.WithA(_holy.Core, alpha);
            }
            if (_haloIn != null)
            {
                Sprite s = Art.Ring(Mathf.Max(3, rx - 5), 1, 0.3f, 12);
                if ((object)_haloIn.R.sprite != s)
                {
                    _haloIn.R.sprite = s;
                    _haloIn.Frames[0] = s;
                }
                _haloIn.Pos = Sky;
                _haloIn.Rot += 40f * Clock.Dt;
                _haloIn.Grad = null;
                _haloIn.R.color = Fx.WithA(_holy.Edge, alpha * 0.7f);
            }
        }

        // --------------------------------------------------------------- marks --

        private static readonly List<Actor> _scan = new List<Actor>();
        private static readonly List<KeyValuePair<float, Actor>> _sort = new List<KeyValuePair<float, Actor>>();

        /// <summary>Every enemy in the circle (flyers too), nearest first, up to the level's marks; the most dangerous for the last judgment.</summary>
        private void Gather()
        {
            _lastT = 0f;
            _marks.Clear();
            _ids.Clear();
            _last.Clear();
            Choreo.Foes(A, Pose.Ground, _r, _scan, true, true);
            _sort.Clear();
            for (int i = 0; i < _scan.Count; i++)
            {
                Actor u = _scan[i];
                if (Choreo.Fair(A, u, out _, true))
                {
                    _sort.Add(new KeyValuePair<float, Actor>((u.current_position - Pose.Ground).sqrMagnitude, u));
                }
            }
            _sort.Sort((x, y) => x.Key.CompareTo(y.Key));
            float best = -1f;
            for (int i = 0; i < _sort.Count && _marks.Count < _max; i++)
            {
                Actor u = _sort[i].Value;
                _marks.Add(u);
                _ids.Add(u.Sid());
                _last.Add(u.current_position);
                float t = Threat.Ratio(A, u);
                if (t > best)
                {
                    best = t;
                    _main = u;
                    _mainId = u.Sid();
                }
            }
            _shown = _fired = 0;
            if (Dev.On) Dev.Trace("judgment_rain " + Techniques.Roman(Level) + " marks " + _marks.Count + " within " + _r.ToString("F1"));
        }

        private bool Alive(int i)
        {
            Actor u = _marks[i];
            return u != null && u.Sid() == _ids[i] && Combat.Living(u);
        }

        private void TickMarks(float k)
        {
            Halo(1f, 0.85f + 0.15f * Mathf.Sin(BeatT * 30f));
            int n = _marks.Count;
            while (_shown < n && k >= (_shown + 0.5f) / n)
            {
                Kindle(_shown);
                _shown++;
            }
        }

        /// <summary>A sigil under a mark's feet (it follows it), a faint line of light above it.</summary>
        private void Kindle(int i)
        {
            if (!Alive(i))
            {
                return;
            }
            Actor u = _marks[i];
            Vector2 feet = u.current_position;
            if (!Kit.Visible(feet, 0.2f))
            {
                return;
            }
            float px = Choreo.PxOf(u);
            float uk = Choreo.KOf(u);
            Particle s = Fx.Spawn(ChoreoArt.Sigil(Mathf.RoundToInt(6f * uk + 1f * Level)), feet, px, 1.8f, FxLayer.Ground, 5);
            if (s != null)
            {
                s.Color(Fx.WithA(_holy.Core, 0f), Fx.WithA(_holy.Core, 0.95f), Fx.WithA(_holy.Edge, 0.85f), Fx.WithA(_holy.Edge, 0f));
                s.Cine = Cinematic;
                long id = _ids[i];
                s.Step = (q, d) =>
                {
                    if (u.Sid() == id && u.isAlive())
                    {
                        q.Pos = u.current_position;
                    }
                    q.Rot += 90f * d;
                };
                _sigils.Add(s);
                _sigilGen.Add(s.Gen);
            }
            Kit.Line(feet + new Vector2(0f, 0.3f), feet + new Vector2(0f, 5f * Kk), Px, 0.35f, 1, TopLayer, 4,
                Fx.WithA(_holy.Core, 0f), Fx.WithA(_holy.Core, 0.45f), Fx.WithA(_holy.Edge, 0f));
            Choreo.Sound("event:/SFX/UI/HintHighlight", u.current_tile, 0.08f);
        }

        // ------------------------------------------------------------- columns --

        private void TickColumns(float k)
        {
            Halo(1f, 1f);
            int n = _marks.Count;
            while (_fired < n && k >= (_fired + 0.5f) / n * 0.85f)
            {
                FireColumn(_fired);
                _fired++;
            }
            if (k > 0.97f && _fired < n)
            {
                while (_fired < n)
                {
                    FireColumn(_fired++);
                }
            }
        }

        /// <summary>A column of light falls on mark 'i': the foe (still there) knocked flat and dazed, the ground scorched.</summary>
        private void FireColumn(int i)
        {
            Vector2 ground = _last[i];
            Vector2 body = ground;
            bool alive = Alive(i);
            Actor u = _marks[i];
            if (alive)
            {
                ground = u.current_position;
                body = Choreo.Feet(u);
                _last[i] = ground;
            }
            Column(ground, body, 1f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", Combat.TileAt(ground), 0.07f);
            if (alive)
            {
                Vector2 away = ground - Pose.Ground;
                Impact im = Impact.Of(Hurt.Knockdown, away.sqrMagnitude > 1e-4f ? away : Vector2.up, 0.35f, 1.3f);
                im.Daze = 0.6f + 0.15f * Level;
                HitResult r = DivineArts.Smite(this, u, JudgmentRainTech.Mult(Level), im);
                if (r.Landed)
                {
                    Victims.Flash(u, _holy.Core, 0.08f);
                }
            }
            if (!alive || Combat.Grounded(u))
            {
                Ruin.Blast(A, ground, 0.3f, Scale, Pose.Ground, false, -2);
            }
        }

        /// <summary>A white column from the sky down to 'body' (a flyer's body, else its feet), a flash and a ring on the ground.</summary>
        private void Column(Vector2 ground, Vector2 body, float width)
        {
            if (!Kit.Visible(ground, 0.3f))
            {
                return;
            }
            float px = Px;
            float top = Pose.Ground.y + 12f * Kk;
            float h = Mathf.Max(2f, top - body.y);
            Vector2 mid = new Vector2(body.x, body.y + h * 0.5f);
            Particle glow = Fx.Spawn(Art.Pixel, mid, px, 0.34f, TopLayer, 10);
            if (glow != null)
            {
                glow.SX = (5f + 2f * Level) * width * Kk;
                glow.SY = h / px;
                glow.Sized(1f, 0.4f).Color(Fx.WithA(_holy.Edge, 0.55f), Fx.WithA(_holy.Edge, 0.35f), Fx.WithA(_holy.Edge, 0f));
            }
            Particle core = Fx.Spawn(Art.Pixel, mid, px, 0.26f, TopLayer, 11);
            if (core != null)
            {
                core.SX = (2f + Level) * width * Kk;
                core.SY = h / px;
                core.Sized(1f, 0.2f).Color(Color.white, Fx.WithA(_holy.Core, 0.95f), Fx.WithA(_holy.Core, 0f));
            }
            Kit.Flash(body, px, Mathf.RoundToInt((4f + Level) * width * Kk), 0.1f, Fx.WithA(Color.white, 0.95f), TopLayer, 12);
            Fx.Ring(ground, px, 0.3f, 2f, (9f + 3f * Level) * width * Kk, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(_holy.Rim, 0f));
            Kit.DustRing(ground, px, Combat.TileAt(ground), 5, 28f, 0.4f, Kk * width);
            Kit.Sparks(body, px, 4, 40f, 100f, _holy.Edge, TopLayer);
        }

        // -------------------------------------------------------- the last one --

        private void LastJudgment()
        {
            if (!MainOk)
            {
                return;
            }
            Actor u = _main;
            Vector2 ground = u.current_position;
            Vector2 body = Choreo.Feet(u);
            Column(ground, body, 2.6f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", u.current_tile, 0.2f);
            Choreo.Sound("event:/SFX/POWERS/DivineLight", u.current_tile, 0.2f);
            Vector2 away = ground - Pose.Ground;
            Impact im = Impact.Of(Combat.Grounded(u) ? Hurt.DriveIn : Hurt.Knockdown, away.sqrMagnitude > 1e-4f ? away : Vector2.up, 0.3f, 2f);
            im.Daze = 1.2f;
            im.Crater = 0.8f;
            DivineArts.Smite(this, u, JudgmentRainTech.Finale(Level), im);
            if (Vis || Kit.Visible(ground))
            {
                Kit.ImpactFrame(body + new Vector2(0f, 0.4f * Kk), Px, 0.9f * Kk, _holy.Pal, false, Cinematic ? FxLayer.Cine : FxLayer.Top);
                Kit.Debris(ground, Px, u.current_tile, 10, Kk * 1.2f);
                Kit.Crater(ground, Px * Kk, u.current_tile, false, 6f, false, _holy.Edge);
                Kit.Shockwave(ground, Px, 4f, 40f * Kk, 0.45f, Fx.WithA(_holy.Core, 0.95f), 3);
                if (_halo != null && _halo.Alive)
                {
                    Kit.Flash(Sky, Px, Mathf.RoundToInt(_r * 0.3f / Px), 0.18f, Fx.WithA(_holy.Core, 0.7f), TopLayer, 2);
                }
            }
            Kit.Stop(ground, 0.08f);
            Ruin.Blast(A, ground, 0.6f, Scale, Pose.Ground, false);
            Ruin.Quake(A, ground, Scale, 1.4f, Kk);
            SetLink(Link.Down, u);
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            for (int i = 0; i < _sigils.Count; i++)
            {
                Particle p = _sigils[i];
                if (p != null && p.Alive && p.Gen == _sigilGen[i])
                {
                    p.Life = Mathf.Min(p.Life, p.Age + 0.25f);
                    p.Step = null;
                }
            }
            _sigils.Clear();
            _sigilGen.Clear();
            _marks.Clear();
            _main = null;
        }
    }
}
