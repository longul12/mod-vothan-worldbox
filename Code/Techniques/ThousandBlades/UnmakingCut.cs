using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// UNMAKING CUT (Thousand Blades, God). The God stands still, its hand on the hilt; the world
    /// darkens and holds its breath. One draw, too fast to see: the world goes WHITE. Against the
    /// white, the God is a black silhouette in its follow-through, and a single black hairline runs
    /// from its blade to the horizon, 40 tiles and more, across everything in its way (the foes on it
    /// frozen as black shapes). Colour returns; the hairline is a thin glowing seam in the dark. The
    /// God slides its blade home, slowly. The guard clicks, and everything on the line is severed at
    /// once: the seam bursts open along its whole length into a trench of light, buildings on it are
    /// split, and the foes on it are thrown apart to either side.
    /// Plays as a cinematic (the white world, the freeze, the title card) when cinematics allow;
    /// otherwise the same cut with a flash instead of the white world.
    /// I: 40 tiles; II: 46; III: 52, a wider, white-hot trench (Gods use it at III).
    /// Damage to each one on the line (x the user's damage): I 6, II 8, III 10.
    /// </summary>
    internal sealed class UnmakingCutTech : Technique
    {
        public override string Id => "unmaking_cut";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Line;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Line | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 200f, 260f, 320f };
        public override float[] Cooldown => new[] { 70f, 80f, 90f };

        public const string TitleKey = "sakuga_title_unmaking_cut";

        // ------------------------------------------------------------------ dev --

        private static float _inkUndim = -1f;

        /// <summary>
        /// Dev check of the white world's layering: "tbink MODE" draws, for 1.5 s, a white sheet on the
        /// left half of the view (FxLayer.Cine 34), a black square and a black silhouette of the latest
        /// performer on it (Cine 40), and a black square over the world on the right (Top 40). MODE 1
        /// also turns the cinematic veil white (0.9), MODE 2 also freezes the world. Capture it whole.
        /// </summary>
        [SakugaInit]
        private static void InitDev()
        {
            Dev.Commands["tbink"] = args =>
            {
                Camera cam = World.world != null ? World.world.camera : null;
                if (cam == null)
                {
                    return;
                }
                int mode = Mathf.RoundToInt(Dev.ArgF(args, 1, 0f));
                float h = cam.orthographicSize;
                Vector3 cp = cam.transform.position;
                Vector2 c = new Vector2(cp.x, cp.y);
                Particle sheet = Fx.Spawn(Art.Pixel, c + new Vector2(-h * cam.aspect * 0.5f, 0f), 1f, 1.5f, FxLayer.Cine, 34);
                if (sheet != null)
                {
                    sheet.SX = h * cam.aspect;
                    sheet.SY = h * 2f;
                    sheet.Color(Color.white);
                }
                Particle sq = Fx.Spawn(Art.Pixel, c + new Vector2(-h * 0.6f, h * 0.3f), 1f, 1.5f, FxLayer.Cine, 40);
                if (sq != null)
                {
                    sq.SX = sq.SY = h * 0.25f;
                    sq.Color(new Color(0.05f, 0.02f, 0.04f, 1f));
                }
                Particle sq2 = Fx.Spawn(Art.Pixel, c + new Vector2(h * 0.6f, h * 0.3f), 1f, 1.5f, FxLayer.Top, 40);
                if (sq2 != null)
                {
                    sq2.SX = sq2.SY = h * 0.25f;
                    sq2.Color(new Color(0.05f, 0.02f, 0.04f, 1f));
                }
                Actor a = Dev.Performer;
                Sprite s = a != null ? Fx.UnitSprite(a) : null;
                if (s != null)
                {
                    Particle sil = Fx.Spawn(s, c + new Vector2(-h * 0.6f, -h * 0.3f), 1f, 1.5f, FxLayer.Cine, 40, true);
                    if (sil != null)
                    {
                        sil.UseEuler = true;
                        sil.Euler = Vector3.zero;
                        sil.SX = sil.SY = h * 0.03f;
                        sil.Color(new Color(0.05f, 0.02f, 0.04f, 1f));
                    }
                }
                if (mode == 1)
                {
                    Cinema.Dim(0.9f, Color.white, 0f, false);
                    _inkUndim = 1.5f;
                }
                if (mode == 2)
                {
                    Cinema.Freeze(1.5f);
                }
                Dev.Log("tbink " + mode + ": silhouettes " + Fx.HasSilhouette);
            };
            Hooks.FrameTick += dt =>
            {
                if (_inkUndim > 0f)
                {
                    _inkUndim -= Time.unscaledDeltaTime;
                    if (_inkUndim <= 0f)
                    {
                        Cinema.Undim(0.1f);
                    }
                }
            };
        }

        public static float Length(int level)
        {
            return level <= 1 ? 40f : level == 2 ? 46f : 52f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 8f : 10f;
        }

        private const float Width = 0.9f;

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            // a crowd lined up, or one foe worth unmaking the world for
            Plan p = Choreo.PickLine(c, this, level, Length(level), Width, Mult(level), 2, true);
            if (p == null)
            {
                Actor v = c.Focus;
                if (v == null || !Choreo.Fair(c.Self, v, out _, c.Forced))
                {
                    return null;
                }
                bool worthy = c.Forced || Tiers.Of(v) >= Tier.Grandmaster || Weights.Species(v) >= Weight.Titan || c.Threat(v) >= 0.8f;
                if (!worthy)
                {
                    return null;
                }
                p = Choreo.PickLine(c, this, level, Length(level), Width, Mult(level), 1, true);
                if (p == null)
                {
                    return null;
                }
            }
            if (!c.Forced && c.Dist(p.Target) < 1.5f)
            {
                return null;   // (a draw needs a breath of room)
            }
            if (!c.Forced && p.Count >= 4)
            {
                p.Score *= 1.2f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Unmake m = new Unmake(a, this, p);
            m.Build();
            return m.Start();
        }

        /// <summary>The move: stillness, the draw (the white world), the seam, the sheathe, the severing.</summary>
        private sealed class Unmake : ChoreoMove
        {
            private static readonly Color Ink = new Color(0.05f, 0.02f, 0.04f, 1f);
            private Vector2 _a, _b, _line;
            private float _len;
            private bool _white;
            private long _reached = -1, _spared = -1;
            private Particle _sil, _hair, _seam, _sheet;
            private readonly List<Particle> _shades = new List<Particle>(24);
            private readonly List<Actor> _on = new List<Actor>(24);
            private Hue _blade;

            public Unmake(Actor a, Technique t, Plan p) : base(a, t, p)
            {
                Face(Aim);
            }

            public void Build()
            {
                int level = Level;
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Line | Form.Ultimate;
                // stillness: the hand on the hilt; the world darkens and holds its breath
                Add(new Beat
                {
                    Tempo = Tempo.Wind,
                    Dur = 0.45f,
                    To = new Shape(-6f, 1.05f, 0.95f, 0f, -0.04f),
                    Ease = Ease.InOutSine,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Unmake)mm).Still(),
                    Every = (mm, k) => ((Unmake)mm).Gather(k)
                });
                // the draw: one instant
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.04f,
                    To = new Shape(10f, 1.28f, 0.84f, 0f, 0.18f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Unmake)mm).Draw()
                });
                // the white world: nothing moves
                Add(new Beat { Tempo = Tempo.Free, Dur = 0.55f, Track = false, Forms = show });
                // colour returns; the seam glows; the blade slides home, slowly
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.48f,
                    To = new Shape(2f, 1.02f, 0.98f, 0f, 0.04f),
                    Ease = Ease.InOutSine,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Unmake)mm).Colour(),
                    Every = (mm, k) => ((Unmake)mm).Seam(k)
                });
                At(0.93f, mm => ((Unmake)mm).Click());
                // the guard clicks: everything on the line is severed
                Add(new Beat
                {
                    Tempo = Tempo.Impact,
                    Dur = 0.2f,
                    To = new Shape(-4f, 0.96f, 1.05f, 0f, 0f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = show,
                    Enter = mm => ((Unmake)mm).Sever()
                });
                Recover(0.4f);
            }

            private void Still()
            {
                BeginCinematic(8f, 0.5f, TitleKey, 0f, 0f);
                Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
                if (Vis)
                {
                    Kit.DustRing(Pose.Ground, Px, A.current_tile, 10, -18f, 0.6f, Kk);
                }
            }

            private void Gather(float k)
            {
                if (!Vis)
                {
                    return;
                }
                if (Random.value < 0.3f)
                {
                    Fx.FocusLines(BodyPos(), Px, Fx.Count(3), 22f * Kk, 40f * Kk, 0.12f);
                }
                if (k > 0.75f && Random.value < 0.25f)
                {
                    Fx.Glint(BodyPos() + new Vector2(Fwd * 2f, -0.5f) * Px, Px, 0.12f, Color.white, 0.7f);
                }
                if (k > 0.6f)
                {
                    Pose.Nudge = Choreo.Tremble(BeatT, 0.3f);
                }
            }

            /// <summary>The draw: the line is fixed, the world goes white, the hairline runs to the horizon.</summary>
            private void Draw()
            {
                // aimed at its foe if it is still roughly where it was
                Vector2 dir = Aim;
                if (V != null && VOk)
                {
                    Vector2 d = VG - Stance;
                    if (d.sqrMagnitude > 0.25f && Vector2.Angle(d, Aim) < 25f)
                    {
                        dir = d.normalized;
                    }
                }
                Face(dir);
                _len = Length(Level) * Mathf.Lerp(1f, Kk, 0.5f);
                _a = Stance + dir * 0.4f * Kk;
                _b = Combat.Clamp(_a + dir * _len);
                _len = Vector2.Distance(_a, _b);
                _line = dir;
                float lift = Hpx * 0.45f * Px;
                Vector2 la = _a + new Vector2(0f, lift), lb = _b + new Vector2(0f, lift);
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
                _white = Cinematic;
                if (_white)
                {
                    // the world goes white (a sheet of white over the whole view, the veil lifted so
                    // nothing lies over it); the God and the line are ink on it
                    CineKit.ViewFlash(Color.white, 0.05f);
                    Cinema.Dim(0f, Color.white, 0.02f, false);
                    _sheet = Glue(Fx.Spawn(Art.Pixel, BodyPos(), 1f, 1f, FxLayer.Cine, 34));
                    if (_sheet != null)
                    {
                        _sheet.Color(new Color(0.98f, 0.98f, 0.96f, 1f));
                        Sheet();
                    }
                    Freeze(0.62f);
                    // (ink: the sprite in its ordinary material tinted black is a solid black shape; the
                    // flat silhouette material comes out half grey)
                    _sil = Glue(CineKit.Copy(this, FxLayer.Cine, 40, false));
                    _hair = CineKit.Line(la, lb, Px, 1f, 2, FxLayer.Cine, 41, true, Ink);
                    if (_hair != null)
                    {
                        Glue(_hair);
                    }
                    Shades();
                }
                else
                {
                    if (Vis && Settings.Cinematics > 0)
                    {
                        CineKit.ViewFlash(Fx.WithA(Color.white, 0.8f), 0.04f);
                    }
                    if (Vis)
                    {
                        Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(12f * Kk), 0.12f, Color.white, TopLayer);
                    }
                    _hair = Kit.Line(la, lb, Px, 1f, 1, FxLayer.Top, 41, Color.white, Fx.WithA(Hue.Core, 0.95f));
                    if (_hair != null)
                    {
                        Glue(_hair);
                    }
                    Kit.Stop(_a, 0.1f);
                }
                if (Vis)
                {
                    CineKit.SpeedLines(BodyPos(), dir, Px, 6, 5f, 0.14f, _white ? Ink : Hue.Edge, _white ? FxLayer.Cine : FxLayer.Top, _white);
                }
                // its foe meets the cut here: it may answer it, even this (slipped, it is spared; cut in
                // half by a greater blade, the line stops at it)
                if (V != null && VOk && OnLine(V))
                {
                    Contact c = Combat.Reach(this, V, false);
                    if (c == Contact.Hit)
                    {
                        _reached = VId;
                    }
                    else
                    {
                        _spared = VId;
                        if (c == Contact.Parried)
                        {
                            _len = Mathf.Clamp(Vector2.Dot(VG - _a, _line) - 0.6f, 0.5f, _len);
                            _b = _a + _line * _len;
                        }
                    }
                }
            }

            private bool OnLine(Actor u)
            {
                Vector2 rel = u.current_position - _a;
                float along = Vector2.Dot(rel, _line);
                if (along < -0.3f || along > _len + 0.3f)
                {
                    return false;
                }
                float off = Mathf.Abs(rel.x * _line.y - rel.y * _line.x);
                return off <= Width + 0.4f + u.stats["size"] * 0.5f;
            }

            /// <summary>The foes on the line, frozen as black shapes against the white.</summary>
            private void Shades()
            {
                Choreo.FoesOnLine(A, _a, _b, Width + 0.3f, _on, true, true);
                for (int i = 0; i < _on.Count && _shades.Count < 24; i++)
                {
                    Actor u = _on[i];
                    if (u == null || !u.is_visible)
                    {
                        continue;
                    }
                    Sprite s = Fx.UnitSprite(u);
                    if (s == null)
                    {
                        continue;
                    }
                    Vector3 cp = u.cur_transform_position;
                    Particle p = Fx.Spawn(s, new Vector2(cp.x, cp.y), 1f, 1f, FxLayer.Cine, 38, false);
                    if (p == null)
                    {
                        break;
                    }
                    p.UseEuler = true;
                    p.Euler = u.current_rotation;
                    p.SX = u.current_scale.x;
                    p.SY = u.current_scale.y;
                    p.Color(Fx.WithA(Ink, 0.92f));
                    Glue(p);
                    _shades.Add(p);
                }
            }

            public override void Frame()
            {
                base.Frame();
                if (_sil != null && _sil.Alive)
                {
                    Fx.Glue(_sil, this, Ink);
                }
                Sheet();
            }

            /// <summary>The white sheet covers the whole view wherever the camera is.</summary>
            private void Sheet()
            {
                Camera cam = World.world != null ? World.world.camera : null;
                if (_sheet == null || !_sheet.Alive || cam == null)
                {
                    return;
                }
                float h = cam.orthographicSize * 2.4f;
                Vector3 cp = cam.transform.position;
                _sheet.Pos = new Vector2(cp.x, cp.y);
                _sheet.Z = 0f;
                _sheet.SX = h * cam.aspect;
                _sheet.SY = h;
            }

            /// <summary>Colour returns: the dark veil, the hairline a glowing seam.</summary>
            private void Colour()
            {
                if (_sil != null)
                {
                    Unglue(_sil);
                    _sil = null;
                }
                if (_sheet != null)
                {
                    Unglue(_sheet);
                    _sheet = null;
                }
                for (int i = 0; i < _shades.Count; i++)
                {
                    Unglue(_shades[i]);
                }
                _shades.Clear();
                if (_hair != null)
                {
                    Unglue(_hair);
                    _hair = null;
                }
                if (_white)
                {
                    Cinema.Dim(0.45f, Color.black, 0.18f, false);
                }
                float lift = Hpx * 0.45f * Px;
                _seam = Glue(CineKit.Line(_a + new Vector2(0f, lift), _b + new Vector2(0f, lift), Px, 1f, 1, _white ? FxLayer.Cine : FxLayer.Top, 41, _white,
                    Hue.Core, Fx.WithA(Hue.Edge, 0.9f)));
                if (Vis)
                {
                    Kit.Line(_a, _b, Px, 1.6f, 1, FxLayer.Ground, 6, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Hue.Edge, 0f));
                }
            }

            private void Seam(float k)
            {
                if (_seam != null && _seam.Alive)
                {
                    _seam.Grad = null;
                    float glow = 0.6f + 0.4f * Mathf.Sin(BeatT * 40f);
                    _seam.R.color = Color.Lerp(Fx.WithA(Hue.Edge, 0.75f), Color.white, glow * (0.4f + 0.6f * k));
                }
                // the blade slides home at its hip
                Vector2 hip = BodyPos() + new Vector2(-Fwd * 0.5f, -1f) * Px;
                TbHiArt.Blade(this, hip, new Vector2(-Fwd, -0.35f), Hpx * 0.6f * (1f - Beat.Apply(Ease.InQuad, k)), _blade);
            }

            private void Click()
            {
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                if (Vis)
                {
                    CineKit.Glint(BodyPos() + new Vector2(-Fwd * 1f, -1f) * Px, Px, 0.18f, Color.white, 1f, _white ? FxLayer.Cine : FxLayer.Top, _white);
                }
            }

            /// <summary>The guard clicks: the seam bursts open along its whole length.</summary>
            private void Sever()
            {
                if (_seam != null)
                {
                    Unglue(_seam);
                    _seam = null;
                }
                EndCinematic();
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", A.current_tile);
                Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile, 0.05f);
                if (Vis)
                {
                    Kit.Flash(_a, Px, Mathf.RoundToInt(9f * Kk), 0.1f, Color.white, TopLayer);
                }
                Kit.Stop(_a, 0.1f);
                Ruin.Quake(A, _a, Scale, 1.6f, Kk);
                Areas.Add(new Severance(this, _a, _line, _len, Mult(Level), Level, _reached, _spared));
            }
        }

        /// <summary>
        /// The severing: from the God outward, in a third of a second, the seam bursts open into a
        /// trench of light; whoever stands on it is thrown apart to its own side; buildings are split.
        /// </summary>
        private sealed class Severance : Hazard
        {
            private readonly Vector2 _from, _dir, _side;
            private readonly float _len, _mult;
            private readonly int _level;
            private readonly long _reached, _spared;
            private float _head, _done;
            private int _segs;

            public Severance(ChoreoMove m, Vector2 from, Vector2 dir, float len, float mult, int level, long reached, long spared) : base(m)
            {
                _reached = reached;
                _spared = spared;
                _from = from;
                _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
                _side = Choreo.Perp(_dir);
                _len = len;
                _mult = mult;
                _level = level;
            }

            public override bool Tick(float dt)
            {
                if (dt <= 0f)
                {
                    return true;
                }
                Age += dt;
                float was = _head;
                _head = Mathf.Min(_len, _head + 150f * dt);
                const float seg = 2f;
                while (_done + 0.01f < _head && (_head - _done >= seg || _head >= _len))
                {
                    float b = Mathf.Min(_len, _done + seg);
                    Open(_from + _dir * _done, _from + _dir * b);
                    _done = b;
                }
                if (OwnerOk && _head > was)
                {
                    Choreo.FoesOnLine(A, _from + _dir * Mathf.Max(0f, was - 0.3f), _from + _dir * _head, Width + 0.3f, Near, true, true);
                    for (int i = 0; i < Near.Count; i++)
                    {
                        Actor u = Near[i];
                        Vector2 rel = u.current_position - _from;
                        float s = Vector2.Dot(rel, _side);
                        float sign = Mathf.Abs(s) < 0.05f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(s);
                        Vector2 on = _from + _dir * Vector2.Dot(rel, _dir);
                        if (Sever(u, on - _side * sign * 0.3f))
                        {
                            if (Kit.Visible(u.current_position))
                            {
                                Vector2 at = Choreo.BodyAt(u, 0.5f);
                                Kit.ImpactFrame(at, Px, 0.5f * K, Hue.Pal, true, FxLayer.Behind);
                                Kit.Line(at - _dir * 0.8f * K, at + _dir * 0.8f * K, Px, 0.12f, 2, FxLayer.Top, 16, Color.white, Fx.WithA(Hue.Edge, 0f));
                            }
                        }
                    }
                }
                return _done < _len - 0.01f;
            }

            /// <summary>
            /// Severs 'u' (once): thrown apart to its own side of the line. The God's foe already met the
            /// cut at the draw (it is not read twice); one that slipped or cut the draw is spared.
            /// </summary>
            private bool Sever(Actor u, Vector2 from)
            {
                long id = u.Sid();
                if (id == _spared || HitIds.Contains(id) || !Choreo.Fair(A, u, out float cm, true))
                {
                    return false;
                }
                HitIds.Add(id);
                if (id != _reached && Combat.Reach(M, u, true) != Contact.Hit)
                {
                    return false;
                }
                Combat.Hit(A, u, _mult * cm, false, false, 0f, M);
                Vector2 away = u.current_position - from;
                Impact im = Impact.Of(Sakuga.Hurt.Launch, away, 2.2f + 0.3f * _level, 2f);
                im.Lift = 1.4f + 0.2f * _level;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, M, im);
                Victims.Flash(u, Color.white, 0.06f);
                if (MeteorSlam.AtDeathsDoor(u))
                {
                    MeteorSlam.Doom(A, u, M);
                }
                return true;
            }

            /// <summary>One length of the seam bursting open.</summary>
            private void Open(Vector2 a, Vector2 b)
            {
                _segs++;
                if (OwnerOk)
                {
                    Ruin.Furrow(A, a, b, 1f + 0.1f * _level, Scale, A.current_position, false);
                }
                Vector2 mid = (a + b) * 0.5f;
                if (_segs % 3 == 1)
                {
                    Kit.Shake(mid, 0.25f, 0.012f, K);
                }
                if (!Kit.Visible(mid, 0.3f))
                {
                    return;
                }
                WorldTile t = Combat.TileAt(mid);
                bool water = t != null && t.Type != null && t.Type.liquid;
                // the burst of light along the seam, then a dark gash cooling in the ground
                Kit.Line(a, b, Px, 0.16f, 5 + _level, FxLayer.Top, 14, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Kit.Line(a + new Vector2(0f, 0.6f * K), b + new Vector2(0f, 0.6f * K), Px, 0.22f, 2, FxLayer.Top, 13, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Rim, 0f));
                if (water)
                {
                    Kit.Splash(mid, Px, t, K);
                    return;
                }
                Color dark = Fx.WithA(Fx.Darken(Fx.SoilColor(t), 0.65f), 0.95f);
                Particle gash = Kit.Line(a, b, Px, 4f, 6, FxLayer.Ground, 4, dark, dark, Fx.WithA(dark, 0f));
                if (gash != null)
                {
                    gash.Snap = true;
                }
                Kit.Line(a, b, Px, 2.2f, 2, FxLayer.Ground, 5, Color.white, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                ChoreoFx.HeelDust(mid + _side * 0.6f * K, _side, Px, t, K * 1.4f);
                ChoreoFx.HeelDust(mid - _side * 0.6f * K, -_side, Px, t, K * 1.4f);
                Kit.Debris(mid, Px, t, 4 + _level, K * 1.2f);
            }
        }
    }
}
