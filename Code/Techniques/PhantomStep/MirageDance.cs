using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// MIRAGE DANCE (Phantom Step, Expert). It steps in and spins, and violet copies of itself peel
    /// off the spin; then it dances round the foe in a blur, and the copies wheel round with it, a
    /// carousel of silhouettes on every side. One after another the copies dart in, each with a
    /// different blow (a snap kick, an elbow, a roundhouse, a knee, a hook), the foe jerked this way
    /// and that by blows from every side, and each copy fades as it springs back out; last, the real
    /// one comes in from the front: a roundhouse that spins it (I), a spinning kick that sends it
    /// reeling (II), and at III the copies all flash back into being round it and close in with the
    /// real one's palm, every side at once, and it is launched.
    /// The copies are illusions in its colour; their blows are its own (one answer meets the first).
    /// Damage (x the user's damage): I 3 x 0.5 + 1.0; II 4 x 0.55 + 1.3; III 5 x 0.6 + 1.6.
    /// </summary>
    internal sealed class MirageDanceTech : Technique
    {
        public override string Id => "mirage_dance";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Clone | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override Scale ScaleAt(int level) => level >= 3 ? Scale.Heavy : Scale.Light;
        public override float[] ManaCost => new[] { 22f, 40f, 64f };
        public override float[] Cooldown => new[] { 9f, 11f, 13f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static int Images(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 4 : 5;
        }

        public static float ImageMult(int level)
        {
            return level <= 1 ? 0.5f : level == 2 ? 0.55f : 0.6f;
        }

        public static float Fin(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.3f : 1.6f;
        }

        public static float Total(int level)
        {
            return Images(level) * ImageMult(level) + Fin(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.6f, Total = Total(1), Crowd = 0.06f, Alone = 12f, IntoGuard = 1.2f, Worth = 16f },
            new MeleeWant { Reach = 2.8f, Total = Total(2), Crowd = 0.06f, Alone = 14f, IntoGuard = 1.2f, Worth = 20f },
            new MeleeWant { Reach = 3f, Total = Total(3), Crowd = 0.05f, Alone = 16f, IntoGuard = 1.2f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // room to dance round it
            Vector2 vg = p.Target.current_position;
            float r = Ring(Choreo.KOf(c.Self), p.Target);
            int free = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                if (Combat.SafeSpot(vg + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r))
                {
                    free++;
                }
            }
            if (free < 4)
            {
                return null;
            }
            return p;
        }

        /// <summary>The radius of the dance round a target (world units).</summary>
        public static float Ring(float k, Actor v)
        {
            return 1.35f * k + (v != null ? v.stats["size"] * 0.35f : 0f);
        }

        private static readonly Blow[] Blows = { Blow.SnapKick, Blow.Elbow, Blow.Roundhouse, Blow.Knee, Blow.Hook };

        public override bool Begin(Actor a, Plan p)
        {
            PhantomMove m = PhantomMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            int n = Images(level);
            Dance d = new Dance(m, n, Ring(m.Kk, m.V), ImageMult(level));
            m.PerFrame = (mm, dt) => d.Tick(dt);
            m.Ended2 = (mm, aborted) => d.Clear();
            m.StepIn();
            // the spin: the copies peel off it
            m.SpinInPlace(1f, 0.16f).Shows(Form.Clone | Form.Spin).OnEnter(_ => d.Peel(m.BeatDur));
            // (spinning, it slides out onto the ring it will dance)
            m.Last.Route = Route.Line;
            m.Last.PathEase = Ease.OutQuad;
            m.Last.Dest = mm => d.RingSpot(mm.Stance);
            // the carousel: it dances round the foe, its copies wheeling with it
            m.Circle(Mathf.PI * (level >= 3 ? 1.1f : 0.85f), d.R / m.Kk, level <= 1 ? 0.3f : level == 2 ? 0.34f : 0.38f)
                .Untouchable().Ghosting(0.025f).Shows(Form.Clone | Form.Flurry | Form.Evasive)
                .OnEnter(_ => d.Phase = 2);
            // one after another the copies dart in
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                Blow b = Blows[i % Blows.Length];
                bool lastImage = i == n - 1;
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = level <= 1 ? 0.16f : level == 2 ? 0.15f : 0.14f,
                    To = new Shape(i % 2 == 0 ? -5f : 4f, 1.03f, 0.98f, 0f),
                    Ease = Ease.OutQuad,
                    NeedsVictim = true,
                    Forms = Form.Clone | Form.Flurry | ChoreoMove.ShapeOf(b).Forms,
                    Enter = mm => d.Dart(idx, b, mm.BeatDur, lastImage)
                });
            }
            // the real one, from the front
            m.Do(_ => d.Phase = 3);
            Hit fin;
            if (level <= 1)
            {
                fin = new Hit(Blow.Roundhouse, Fin(1)) { Push = 0.9f, Finisher = true };
            }
            else if (level == 2)
            {
                fin = new Hit(Blow.SpinKick, Fin(2)) { Hurt = Hurt.Reel, Push = 1.6f, Finisher = true };
            }
            else
            {
                fin = new Hit(Blow.Palm, Fin(3)) { Hurt = Hurt.Launch, Push = 2f, Lift = 2f, Finisher = true, WindScale = 1.4f };
                m.Do(_ => d.Converge());
            }
            fin.OnLand = (mm, v, r) => d.Burst();
            if (m.Tier >= Tier.Master)
            {
                PhantomKit.Crack(fin, level >= 3 ? 0.6f : 0.4f);
            }
            m.Strike(fin);
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>One of the dance's copies: a violet silhouette of the dancer the dance steers.</summary>
        private sealed class Image
        {
            public Particle P;
            public int Gen;
            public Vector2 Ground, From, To;
            public float Rot, SX = 1f, SY = 1f, A;
            public bool Right;
            /// <summary>0 hidden, 1 peeling out, 2 on the ring, 3 darting in, 4 springing back out, 5 closing in (III).</summary>
            public int State;
            public float T, Dur;
            public Blow Blow;
            public bool Struck, Last;
            public float GhostT;
        }

        /// <summary>The copies and their dance round the target.</summary>
        private sealed class Dance
        {
            private readonly PhantomMove _m;
            private readonly Image[] _img;
            public readonly float R;
            private readonly float _mult;
            private float _theta, _peelDur = 0.15f, _peelT;
            /// <summary>1 peeling, 2 carousel, 3 the copies come in (and the finisher), 0 not yet.</summary>
            public int Phase;
            private Sprite _sprite;

            public Dance(PhantomMove m, int n, float r, float mult)
            {
                _m = m;
                R = r;
                _mult = mult;
                _img = new Image[n];
                for (int i = 0; i < n; i++)
                {
                    _img[i] = new Image();
                }
                _sprite = Fx.UnitSprite(m.A);
            }

            /// <summary>The angle of a point round the target (on the ground's ellipse).</summary>
            private float AngleOf(Vector2 p)
            {
                Vector2 rel = p - _m.VG;
                return Mathf.Atan2(rel.y / Art.Ground, rel.x);
            }

            /// <summary>The point of the ring at the angle of 'p' (safe ground, or 'p' itself).</summary>
            public Vector2 RingSpot(Vector2 p)
            {
                Vector2 q = Combat.Clamp(OnRing(AngleOf(p)));
                return Combat.SafeSpot(q) && Combat.ClearPath(p, q) ? q : p;
            }

            private Vector2 OnRing(float angle)
            {
                return _m.VG + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * Art.Ground) * R;
            }

            /// <summary>Its slot on the ring: evenly spaced round the target with the dancer.</summary>
            private float Slot(int i)
            {
                return _theta + Mathf.PI * 2f * (i + 1) / (_img.Length + 1);
            }

            public void Peel(float dur)
            {
                Phase = 1;
                _peelDur = Mathf.Max(0.06f, dur);
                _peelT = 0f;
                _theta = AngleOf(_m.Stance);
                for (int i = 0; i < _img.Length; i++)
                {
                    Image g = _img[i];
                    g.State = 1;
                    g.From = _m.Stance;
                    g.Ground = _m.Stance;
                    g.A = 0f;
                }
                if (_m.Vis)
                {
                    Fx.Afterimage(_m, 0.18f, Fx.WithA(_m.Hue.Core, 0.85f), PhantomKit.Illusion(_m, 0f));
                    Kit.DustRing(_m.Stance, _m.Px, _m.A.current_tile, 6, 26f, 0.4f, _m.Kk);
                }
            }

            /// <summary>Copy 'i' darts in and throws 'b' (over 'dur' seconds); the last one spins the target.</summary>
            public void Dart(int i, Blow b, float dur, bool last)
            {
                if (i < 0 || i >= _img.Length)
                {
                    return;
                }
                Image g = _img[i];
                Phase = 3;   // (the carousel stops: the copies hold their places, drifting, and come in one by one)
                g.State = 3;
                g.T = 0f;
                g.Dur = Mathf.Max(0.06f, dur);
                g.Blow = b;
                g.Last = last;
                g.Struck = false;
                g.From = g.Ground;
                Vector2 rel = g.Ground - _m.VG;
                Vector2 dir = rel.sqrMagnitude > 1e-4f ? rel.normalized : -_m.Dir;
                g.To = _m.VG + dir * (_m.Stand * 0.9f);
                g.A = Mathf.Max(g.A, 0.85f);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", _m.A.current_tile, 0.04f);
            }

            /// <summary>III: every copy flashes back into being on the ring and closes in with the finisher.</summary>
            public void Converge()
            {
                _theta = AngleOf(_m.Stance);
                for (int i = 0; i < _img.Length; i++)
                {
                    Image g = _img[i];
                    g.State = 5;
                    g.T = 0f;
                    g.Dur = 0.2f * _m.TempoOf(Tempo.Wind);
                    g.From = OnRing(Slot(i));
                    g.Ground = g.From;
                    Vector2 rel = g.From - _m.VG;
                    g.To = _m.VG + (rel.sqrMagnitude > 1e-4f ? rel.normalized : _m.Dir) * (_m.Stand * 0.8f);
                    g.A = 0.9f;
                    g.Rot = 0f;
                    g.SX = g.SY = 1f;
                    if (_m.Vis)
                    {
                        Fx.Ring(g.From, _m.Px, 0.18f, 2f, 7f * _m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.2f, Fx.WithA(_m.Hue.Core, 0.9f), Fx.WithA(_m.Hue.Edge, 0f));
                    }
                }
            }

            /// <summary>The finisher landed: the copies that closed in burst into motes.</summary>
            public void Burst()
            {
                for (int i = 0; i < _img.Length; i++)
                {
                    Image g = _img[i];
                    if (g.State == 5 && g.A > 0.05f)
                    {
                        Vector2 chest = g.Ground + new Vector2(0f, _m.Pose.PivotUp * _m.A.actor_scale);
                        PhantomKit.Motes(chest, _m.Px, 5, 5f * _m.Kk, 0.3f, _m.Hue.Core, PhantomKit.Illusion(_m, 1f));
                        g.State = 4;
                        g.T = 0f;
                        g.Dur = 0.1f;
                    }
                }
            }

            public void Tick(float dt)
            {
                if (Phase == 0)
                {
                    return;
                }
                bool failed = _m.Interruptible || _m.Lost || !_m.VOk;
                if (Phase == 1)
                {
                    _peelT += dt;
                }
                else if (Phase == 2)
                {
                    _theta = AngleOf(_m.Stance);   // the ring turns with the dancer
                }
                else
                {
                    _theta += 0.7f * dt;
                }
                for (int i = 0; i < _img.Length; i++)
                {
                    Image g = _img[i];
                    if (failed && g.State != 0)
                    {
                        g.State = 4;   // the dance is broken: the copies fade
                        g.Dur = 0.1f;
                    }
                    switch (g.State)
                    {
                        case 1:
                            {
                                float k = Mathf.Clamp01(_peelT / _peelDur);
                                g.Ground = Vector2.Lerp(g.From, OnRing(Slot(i)), Beat.Apply(Ease.OutCubic, k));
                                g.A = 0.85f * Mathf.Clamp01(k * 2f);
                                g.Rot = 14f * Mathf.Sin(Mathf.PI * k);
                                g.SX = 1f + 0.15f * (1f - k);
                                g.SY = 1f - 0.1f * (1f - k);
                                if (k >= 1f)
                                {
                                    g.State = 2;
                                }
                                break;
                            }
                        case 2:
                            g.Ground = Vector2.MoveTowards(g.Ground, OnRing(Slot(i)), dt * 30f * _m.Kk);
                            g.Rot = Mathf.Lerp(g.Rot, Phase == 2 ? 12f : 0f, dt * 12f);
                            g.SX = Mathf.Lerp(g.SX, Phase == 2 ? 1.1f : 1f, dt * 12f);
                            g.SY = Mathf.Lerp(g.SY, Phase == 2 ? 0.92f : 1f, dt * 12f);
                            break;
                        case 3:
                            TickDart(g, dt);
                            break;
                        case 4:
                            g.T += dt;
                            g.A = Mathf.Max(0f, g.A - dt / Mathf.Max(0.05f, g.Dur) * 0.9f);
                            g.SY = Mathf.Lerp(g.SY, 1.3f, dt * 8f);
                            g.SX = Mathf.Lerp(g.SX, 0.6f, dt * 8f);
                            if (g.A <= 0.01f)
                            {
                                g.State = 0;
                            }
                            break;
                        case 5:
                            {
                                g.T += dt;
                                float k = Mathf.Clamp01(g.T / g.Dur);
                                g.Ground = Vector2.Lerp(g.From, g.To, Beat.Apply(Ease.InQuad, k));
                                g.Rot = 18f * k;
                                g.SX = 1f + 0.2f * k;
                                g.SY = 1f - 0.12f * k;
                                break;
                            }
                    }
                    if (g.State != 0)
                    {
                        Vector2 d = _m.VG - g.Ground;
                        if (Mathf.Abs(d.x) > 0.05f)
                        {
                            g.Right = d.x > 0f;
                        }
                    }
                    Render(g, dt);
                }
            }

            private void TickDart(Image g, float dt)
            {
                g.T += dt;
                float k = Mathf.Clamp01(g.T / g.Dur);
                BlowShape s = ChoreoMove.ShapeOf(g.Blow);
                if (k < 0.45f)
                {
                    float u = k / 0.45f;
                    g.Ground = Vector2.Lerp(g.From, g.To, Beat.Apply(Ease.OutQuad, u));
                    g.Rot = Mathf.Lerp(16f * Mathf.Sin(Mathf.PI * u), s.Wind.Rot, u);
                    g.SX = Mathf.Lerp(1.15f, s.Wind.SX, u);
                    g.SY = Mathf.Lerp(0.9f, s.Wind.SY, u);
                }
                else if (k < 0.62f)
                {
                    float u = Beat.Apply(Ease.OutExpo, (k - 0.45f) / 0.17f);
                    g.Ground = g.To;
                    g.Rot = Mathf.Lerp(s.Wind.Rot, s.Hit.Rot, u);
                    g.SX = Mathf.Lerp(s.Wind.SX, s.Hit.SX, u);
                    g.SY = Mathf.Lerp(s.Wind.SY, s.Hit.SY, u);
                }
                if (!g.Struck && k >= 0.52f)
                {
                    // (checked outside the pose phases: a long frame may step right over the blow's window)
                    g.Struck = true;
                    Land(g, s);
                }
                if (k >= 0.62f)
                {
                    // it springs back out and fades
                    float u = (k - 0.62f) / 0.38f;
                    g.Ground = Vector2.Lerp(g.To, g.From, Beat.Apply(Ease.OutQuad, u) * 0.6f);
                    g.Rot = Mathf.Lerp(s.Hit.Rot, -10f, u);
                    g.SX = Mathf.Lerp(s.Hit.SX, 0.8f, u);
                    g.SY = Mathf.Lerp(s.Hit.SY, 1.2f, u);
                    g.A = 0.85f * (1f - u);
                    if (k >= 1f)
                    {
                        g.State = 0;
                        g.A = 0f;
                    }
                }
            }

            /// <summary>A copy's blow lands: the dancer's damage, from that side, through the target's answer.</summary>
            private void Land(Image g, BlowShape s)
            {
                PhantomMove m = _m;
                if (!m.VOk)
                {
                    return;
                }
                Actor v = m.V;
                Vector2 body = g.Ground + new Vector2(0f, m.Pose.PivotUp * m.A.actor_scale);
                Vector2 at = m.VPoint(Mathf.Min(s.Contact, 0.72f));
                if (m.Vis)
                {
                    ChoreoFx.Smear(body, Vector2.Lerp(at, body, 0.12f), m.Px, 0.09f, 1, s.SmearThick, Fx.WithA(m.Hue.Core, 0.95f), PhantomKit.Illusion(m, 0.6f), FxLayer.Over, 7);
                }
                if (!m.Touch(v))
                {
                    return;
                }
                Vector2 dir = m.VG - g.Ground;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : m.Dir;
                Hit h = new Hit(g.Blow, _mult) { Hurt = g.Last ? Hurt.Spin : Hurt.Flinch, Push = g.Last ? 0.3f : 0.12f, Lethal = false, Power = 0.85f };
                m.Deliver(v, h, s, at, dir);
            }

            private void Render(Image g, float dt)
            {
                if (g.State == 0 || g.A <= 0.01f || !_m.Vis || _sprite == null)
                {
                    if (g.P != null && g.P.Alive && g.P.Gen == g.Gen)
                    {
                        g.P.Grad = null;
                        g.P.R.color = Color.clear;   // (the particle system re-enables what it draws: hide it by colour)
                    }
                    return;
                }
                if (g.P == null || !g.P.Alive || g.P.Gen != g.Gen)
                {
                    g.P = Fx.Spawn(_sprite, g.Ground, 1f, 1f, FxLayer.Behind, 4, true);
                    if (g.P == null)
                    {
                        return;
                    }
                    g.Gen = g.P.Gen;
                    g.P.UseEuler = true;
                    _m.Glue(g.P);
                }
                Pose q = _m.Pose;
                q.Rot = g.Rot;
                q.SX = g.SX;
                q.SY = g.SY;
                q.FaceRight = g.Right;
                q.Nudge = Vector2.zero;
                float sc = _m.A.actor_scale;
                Vector2 off = Moves.PivotOffset(q, sc, _m.A.asset.can_flip);
                Particle p = g.P;
                p.Pos = g.Ground + off;
                p.Euler = new Vector3(0f, _m.A.asset.can_flip && g.Right ? 180f : 0f, g.Rot);
                p.SX = sc * g.SX;
                p.SY = sc * g.SY;
                p.Grad = null;
                p.R.color = PhantomKit.Illusion(_m, g.A);
                // a moving copy leaves a faint trail of itself
                if (g.State == 1 || g.State == 3 || g.State == 5 || g.State == 2 && Phase == 2)
                {
                    g.GhostT -= dt;
                    if (g.GhostT <= 0f)
                    {
                        g.GhostT = 0.035f;
                        Particle t = Fx.Spawn(_sprite, p.Pos, 1f, 0.12f, FxLayer.Behind, 0, true);
                        if (t != null)
                        {
                            t.UseEuler = true;
                            t.Euler = p.Euler;
                            t.SX = p.SX;
                            t.SY = p.SY;
                            t.Color(PhantomKit.Illusion(_m, 0.4f * g.A), PhantomKit.Illusion(_m, 0f));
                        }
                    }
                }
            }

            /// <summary>The dance is over: every copy goes (the move's glue kills their sprites too).</summary>
            public void Clear()
            {
                for (int i = 0; i < _img.Length; i++)
                {
                    Image g = _img[i];
                    if (g.P != null && g.P.Alive && g.P.Gen == g.Gen)
                    {
                        g.P.Kill();
                    }
                    g.P = null;
                    g.State = 0;
                }
                Phase = 0;
            }
        }
    }
}
