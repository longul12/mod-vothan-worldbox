using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SWORD WAVE (Thousand Blades, Expert). The cut leaves the blade. The swordsman draws its blade far
    /// back over its shoulder while light condenses along the edge (a ting at the point), then one huge
    /// flat swing, and a crescent of light flies off the edge at chest height and on along the line,
    /// through everyone in its path (each cut and thrown back), a wake of motes behind it. The engine's
    /// own projectile: others can slip it, beat it aside, cut it in two or send it back (Blade Catch,
    /// Reflect Barrier...).
    ///   I    one crescent, 8 tiles
    ///   II   the backswing looses a second, smaller crescent right behind the first; 10 tiles; the
    ///        ground scored along their path
    ///   III  one great crescent, 12 tiles, that tears a trench along its whole flight (a Heavy blow)
    /// Not from point blank (the blade does that). Damage to each one it passes: I 1.3; II 1.6 + 0.8;
    /// III 2.1.
    /// </summary>
    internal sealed class SwordWaveTech : Technique
    {
        public override string Id => "sword_wave";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Line;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Projectile | Form.Line;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 32f, 52f };
        public override float[] Cooldown => new[] { 8f, 10f, 12f };

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 10f : 12f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.6f : 2.1f;
        }

        private const float Second = 0.8f;

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickLine(c, this, level, Range(level), 0.55f, Mult(level) + (level == 2 ? Second : 0f), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            if (c.Dist(p.Target) < 1.5f)
            {
                return null;   // at point blank the blade itself does this
            }
            if (p.Count >= 3)
            {
                p.Score *= 1.2f;   // a line of them is what it is for
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Blade | Form.Projectile | Form.Line;
            m.Face(p.Dir);
            // GATHER: the blade drawn far back over the shoulder; light condenses along its edge
            Beat wind = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = lv <= 1 ? 0.16f : lv == 2 ? 0.18f : 0.22f,
                To = new Shape(-24f, 0.9f, 1.1f, 0f, -0.1f),
                Ease = Ease.OutQuad,
                Forms = forms
            };
            float moteT = 0f;
            wind.Enter = mm => ((TblMove)mm).HoldBlade(152f);
            wind.Every = (mm, k) =>
            {
                TblMove t = (TblMove)mm;
                if (lv >= 3 && k > 0.6f)
                {
                    mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.5f);
                }
                moteT -= Clock.Dt;
                if (mm.Vis && k > 0.25f && moteT <= 0f)
                {
                    moteT = 0.04f;
                    ChoreoFx.Motes(Vector2.Lerp(t.BladeHand, t.BladeTip, 0.6f), mm.Px, 2 + lv, 9f, 0.12f, mm.Hue.Core, mm.Hue.Edge);
                }
            };
            wind.On(0.8f, mm =>
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
                if (mm.Vis)
                {
                    TblFx.Ting(((TblMove)mm).BladeTip, mm.Px, mm.Hue, 0.8f + 0.15f * lv);
                }
            });
            m.Add(wind);
            // SWING: one huge flat arc; the crescent leaves the edge
            Beat swing = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(26f, 1.22f, 0.86f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Forms = forms
            };
            swing.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt((13f + 2f * lv) * Mathf.Lerp(1f, mm.Kk, 0.6f)), 6, 56);
                    Kit.Swish(mm, Art.Arc(r, 0.55f, 150f, -40f, lv >= 3 ? 4 : 3), 0.16f, new Vector2(mm.Fwd * 2f, 1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                    Kit.SpeedLines(mm.BodyPos() + mm.Dir * 4f * mm.Px, mm.Dir, mm.Px, 4, 4f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.85f), FxLayer.Over);
                }
            };
            swing.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(152f, -40f, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k / 0.5f))));
            swing.On(0.42f, mm => Loose((TblMove)mm, lv, false));
            m.Add(swing);
            float rest = -38f;
            if (lv == 2)
            {
                // II: the backswing looses a second crescent right behind the first
                Beat back = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.08f,
                    To = new Shape(-12f, 0.94f, 1.08f, 0f, 0.08f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = forms
                };
                back.Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.01f);
                    if (mm.Vis)
                    {
                        int r = Mathf.Clamp(Mathf.RoundToInt(13f * Mathf.Lerp(1f, mm.Kk, 0.6f)), 6, 56);
                        Kit.Swish(mm, Art.Arc(r, 0.55f, -40f, 130f, 2), 0.14f, new Vector2(mm.Fwd * 2f, 1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                    }
                };
                back.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(-40f, 130f, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k / 0.5f))));
                back.On(0.45f, mm => Loose((TblMove)mm, lv, true));
                m.Add(back);
                rest = 128f;
            }
            // FOLLOW-THROUGH: held, watching it go
            m.Posture(lv >= 3 ? 0.2f : 0.16f, lv == 2 ? new Shape(-8f, 0.96f, 1.06f, 0f, 0.06f) : new Shape(14f, 1.1f, 0.92f, 0f, 0.1f), Tempo.Follow, rest, forms);
            m.Last.Track = false;
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The crescent leaves the edge: a shot of the engine (others can slip it, beat it aside, cut it, send it back).</summary>
        private static void Loose(TblMove m, int lv, bool second)
        {
            ShotSpec spec = new ShotSpec
            {
                Kind = ShotKind.SwordWave,
                Flight = Flight.Straight,
                Speed = lv <= 1 ? 15f : lv == 2 ? 17f : 19f,
                Range = Range(lv),
                Size = second ? 1.1f : lv <= 1 ? 1.3f : lv == 2 ? 1.5f : 2.1f,
                Mult = second ? Second : Mult(lv),
                Hurt = Hurt.Reel,
                Push = lv <= 1 ? 0.7f : lv == 2 ? 0.9f : 1.1f,
                Pierce = 99,
                Deflectable = true,
                Reflectable = true,
                Cuttable = true,
                Element = Element.School
            };
            Vector2 from = Vector2.Lerp(m.BladeHand, m.BladeTip, 0.7f) + m.Dir * 2f * m.Px;
            Actor target = m.V != null && m.VOk ? m.V : null;
            Vector2 dir = target != null ? m.VPoint(0.5f) - from : m.Aim;
            Shot s = Shots.Fire(m.A, m, from, dir, target, Vector2.zero, spec, m.Hue);
            if (s == null)
            {
                return;
            }
            if (m.Vis)
            {
                Kit.Flash(from, m.Px, 3 + lv, 0.06f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer, 22);
                Kit.Sparks(from, m.Px, 3, 50f, 90f, m.Hue.Edge, m.TopLayer);
            }
            Areas.Add(new TblWaveWake(m, s, second ? 0f : lv == 2 ? 0.3f : lv >= 3 ? 0.5f : 0f));
            if (!second && lv >= 2)
            {
                Ruin.Quake(m.A, from, m.Scale, lv >= 3 ? 0.9f : 0.5f, m.Kk);
            }
        }
    }

    /// <summary>
    /// A sword wave's wake: motes of light streaming off the crescent as it flies, and (with 'furrow')
    /// the ground scored behind it all the way (Ruin.Furrow in steps, at the move's Scale). It follows
    /// the wave only while the wave is still its thrower's: one beaten aside or sent back leaves it.
    /// </summary>
    internal sealed class TblWaveWake : Hazard
    {
        private readonly Shot _shot;
        private readonly float _furrow;
        private Vector2 _last;
        private readonly Vector2 _keep;
        private float _fx;

        public TblWaveWake(ChoreoMove m, Shot s, float furrow) : base(m)
        {
            _shot = s;
            _furrow = furrow;
            _last = s.Pos + s.Dir * 1.8f * m.Kk;   // (never at its own feet, nor its banks of fire)
            _keep = m.Pose.Ground;
        }

        public override bool Tick(float dt)
        {
            Age += dt;
            if (_shot == null || _shot.Done || Age > 4f || !Flying(_shot) || !_shot.Hostile || _shot.A == null || _shot.A.Sid() != AId || !OwnerOk)
            {
                Close();
                return false;
            }
            Vector2 at = _shot.Pos;
            if (_shot.Age < 0f)
            {
                return true;
            }
            if (Kit.Visible(at))
            {
                _fx -= dt;
                if (_fx <= 0f)
                {
                    _fx = 0.03f;
                    Vector2 side = Choreo.Perp(_shot.Dir);
                    float s = _shot.Spec.Size * Mathf.Lerp(1f, K, 0.6f);
                    int n = Fx.Count(2);
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 p = at + new Vector2(0f, _shot.Z) + side * Random.Range(-5f, 5f) * s * Px - _shot.Dir * 2f * Px;
                        Particle q = Fx.Spawn(Art.Pixel, p, Px, Random.Range(0.18f, 0.3f), FxLayer.Top, 9);
                        q?.Moving(-_shot.Dir * Random.Range(8f, 18f) * Px + new Vector2(0f, 4f * Px), 0f, 3f).Sized(1.3f, 0.7f)
                            .Color(Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                    }
                }
            }
            if (_furrow > 0f && Vector2.Dot(at - _last, _shot.Dir) >= 1.5f)
            {
                Score(at);
            }
            return true;
        }

        /// <summary>The ground behind it takes the cut (from the last step to here).</summary>
        private void Score(Vector2 to)
        {
            WorldTile t = Combat.TileAt(to);
            if (t != null && t.Type != null && !t.Type.liquid && Kit.Visible(to))
            {
                TblFx.GroundCut(_last, to, Px, 1.6f, Hue);
            }
            Ruin.Furrow(A, _last, to, _furrow, Scale, _keep);
            _last = to;
        }

        private void Close()
        {
            if (_furrow > 0f && OwnerOk && _shot != null && Vector2.Dot(_shot.Pos - _last, _shot.Dir) > 0.6f)
            {
                Score(_shot.Pos);
            }
        }

        private static bool Flying(Shot s)
        {
            IReadOnlyList<Shot> all = Shots.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == s)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
