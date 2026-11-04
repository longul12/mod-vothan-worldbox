using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// THOUSAND CUTS (Thousand Blades, Master). A breath with the hand on the hilt, then it is gone:
    /// for half a second only its afterimages exist, flickering all round the foe, each one a streak
    /// that passes straight through it, and every pass leaves a cut hanging in the air across the
    /// foe's body. The foe stands frozen in a cage of dozens of fine lines. The swordsman is back
    /// where it started, crouched, sliding the blade home; the lines shimmer; the guard clicks shut,
    /// and every line flashes white at once and shatters into shards of light: the cut lands.
    /// A weapon cuts in steel; bare hands cut with a blade of ki.
    /// I: 14 lines, the foe reels; II: 18 lines in the school's colour, the foe crumples; III: 22
    /// lines, thicker, the burst also cuts foes beside it and scores the ground in a starburst.
    /// Damage (x the user's damage, all at the flash): I 3.4, II 4.6, III 6.2 (+ x0.45 round it at III).
    /// Distinct from Severing Flash (one draw, back to back) and Cross Fang (two cutters, an X):
    /// here nobody is seen to move at all, only the cage of lines.
    /// </summary>
    internal sealed class ThousandCutsTech : Technique
    {
        public override string Id => "thousand_cuts";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Flurry | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 22f, 38f, 60f };
        public override float[] Cooldown => new[] { 10f, 11f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 3.4f : level == 2 ? 4.6f : 6.2f;
        }

        public static int Lines(int level)
        {
            return level <= 1 ? 14 : level == 2 ? 18 : 22;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.6f, Total = Mult(1), Crowd = 0.06f, Alone = 12f, IntoGuard = 0.75f, Worth = 16f },
            new MeleeWant { Reach = 2.8f, Total = Mult(2), Crowd = 0.05f, Alone = 14f, IntoGuard = 0.75f, Worth = 20f },
            new MeleeWant { Reach = 3f, Total = Mult(3), Crowd = 0.04f, Alone = 16f, IntoGuard = 0.75f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            return Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Cuts m = new Cuts(a, this, p);
            if (m.V == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }

        /// <summary>The move: a stance, the blur (the cage of lines grows), the stillness, the click, the flash.</summary>
        private sealed class Cuts : ChoreoMove
        {
            private readonly List<Particle> _lines = new List<Particle>(40);
            private readonly List<Vector2> _la = new List<Vector2>(40), _lb = new List<Vector2>(40);
            private int _total, _made;
            private Vector2 _from, _home, _lastZip;
            private float _zipAng;
            private Hue _blade;

            public Cuts(Actor a, Technique t, Plan p) : base(a, t, p)
            {
            }

            public void Build()
            {
                int level = Level;
                _total = Lines(level);
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Flurry | Form.Fast;
                // the stance: hand on the hilt, leaning back over it
                Add(new Beat { Tempo = Tempo.Wind, Dur = 0.16f, To = new Shape(-10f, 1.08f, 0.93f, 0f, -0.05f), Ease = Ease.OutQuad, Forms = show, NeedsVictim = true });
                At(0.7f, mm => ((Cuts)mm).StanceGlint());
                Hold(0.05f, true, Tempo.Wind);
                // gone: only its afterimages, all round the foe, and the lines they leave
                float blur = level <= 1 ? 0.5f : level == 2 ? 0.58f : 0.66f;
                Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = blur,
                    Hidden = true,
                    HideShadow = true,
                    Intangible = true,
                    NeedsVictim = true,
                    Forms = show,
                    Enter = mm => ((Cuts)mm).EnterBlur(),
                    Every = (mm, k) => ((Cuts)mm).TickBlur(k),
                    Exit = mm => ((Cuts)mm).ExitBlur()
                });
                // back where it began, crouched in the finish, as if it never moved
                Add(new Beat { Tempo = Tempo.Follow, Dur = 0.07f, To = new Shape(16f, 1.16f, 0.84f, 0f, 0.02f), Ease = Ease.OutExpo, Track = false });
                // the stillness: the blade slides home, the cage of lines shimmers
                float still = level <= 1 ? 0.34f : level == 2 ? 0.3f : 0.27f;
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = still,
                    To = new Shape(3f, 1.03f, 0.98f, 0f, 0f),
                    Ease = Ease.InOutSine,
                    Track = false,
                    Every = (mm, k) => ((Cuts)mm).Shimmer(k)
                });
                At(0.86f, mm => ((Cuts)mm).Click());
                // the guard clicks shut: every line flashes at once
                Do(mm => ((Cuts)mm).Flash());
                Hold(0.1f, false, Tempo.Impact);
                Recover(0.28f);
            }

            private void StanceGlint()
            {
                if (Vis)
                {
                    Fx.Glint(Hand(Limb.Blade) + new Vector2(-Fwd * 1f, 1f) * Px, Px, 0.16f, Color.white, 0.9f);
                    Fx.FocusLines(BodyPos(), Px, Fx.Count(6), 12f * Kk, 20f * Kk, 0.12f);
                }
            }

            private void EnterBlur()
            {
                _home = Stance;
                _made = 0;
                _from = BodyPos();
                _lastZip = Stance;
                Vector2 rel = Stance - VG;
                _zipAng = Mathf.Atan2(rel.y / Art.Ground, rel.x);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Vis)
                {
                    // the pop where it stood: a white flash of its shape and a fading ghost
                    Fx.Afterimage(this, 0.06f, new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
                    Fx.Afterimage(this, 0.24f, Fx.WithA(Hue.Ghost, 0.7f), Hue.GhostEnd);
                    ChoreoFx.HeelDust(Stance, Stance - VG, Px, A.current_tile, Kk);
                }
            }

            private void TickBlur(float k)
            {
                // the foe stands frozen in the cage (it can still answer the first pass)
                TbHiArt.Still(V, 0.3f);
                // the passes come faster and faster
                int want = Mathf.Min(_total, Mathf.CeilToInt(_total * Mathf.Pow(k, 0.85f)));
                while (_made < want)
                {
                    int i = _made++;
                    if (!Zip(i))
                    {
                        return;
                    }
                }
            }

            /// <summary>One pass through the foe: an afterimage on the far side, a streak through it, a line left hanging.</summary>
            private bool Zip(int i)
            {
                if (!VOk)
                {
                    if (!Retarget())
                    {
                        Lose();
                        return false;
                    }
                }
                if (i == 0 && !Touch(V))
                {
                    return false;   // answered (parried or slipped): the engine plays the recoil
                }
                // the next pass ends on the far side of the foe, a little off the straight line
                _zipAng += Mathf.PI + Random.Range(-0.85f, 0.85f);
                float r = (0.75f + 0.3f * Random.value) * Mathf.Max(Stand, 0.45f * VHeight + 0.35f * Kk);
                Vector2 p = Combat.Clamp(VG + new Vector2(Mathf.Cos(_zipAng), Mathf.Sin(_zipAng) * Art.Ground) * r);
                if (!Combat.SafeSpot(p) && !Choreo.Wading(p))
                {
                    p = VG + (VG - _lastZip).normalized * 0.2f;
                }
                if (Vis)
                {
                    float h = Random.Range(0f, 0.25f) * Kk;
                    Vector2 a0 = _from;
                    Vector2 b0 = p + new Vector2(0f, h + Pose.PivotUp * A.actor_scale);
                    bool right = VG.x > p.x;
                    // the afterimage where the pass ends, leaning into its motion
                    TbHiArt.GhostAt(this, p, h, right, 18f, 1.14f, 0.9f, 0.13f, FxLayer.Behind, Fx.WithA(Hue.Ghost, 0.75f), Hue.GhostEnd);
                    // the streak of the pass, straight through the foe
                    Kit.Line(a0, b0, Px, 0.07f, 1, FxLayer.Over, 9, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
                    if ((i & 1) == 0)
                    {
                        Kit.SpeedLines(Vector2.Lerp(a0, b0, 0.5f), b0 - a0, Px, 2, 2.5f, 0.08f, Fx.WithA(Hue.Edge, 0.7f));
                    }
                    HangLine(i);
                    _from = b0;
                }
                _lastZip = p;
                if (i % 3 == 0)
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile, 0.05f);
                }
                if (i % 4 == 1)
                {
                    Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile, 0.07f);
                }
                return true;
            }

            /// <summary>A cut left hanging across the foe's body: a hairline at a new angle, through a point on it.</summary>
            private void HangLine(int i)
            {
                // spread over the whole body (a cage round it, not a star through its middle)
                float hgt = Mathf.Max(VHeight, Hpx * Px * 0.9f);
                float wid = hgt * 0.6f;
                Vector2 c = VPoint(Random.Range(0.1f, 0.9f)) + new Vector2(Random.Range(-0.45f, 0.45f) * wid, 0f);
                float ang = Random.Range(0f, Mathf.PI);
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                // longer than the body is tall: thin lines crossing well past it read as a cage of cuts
                float half = Random.Range(0.55f, 0.95f) * hgt + 0.2f * Kk;
                Vector2 a = c - d * half, b = c + d * half;
                int thick = Level >= 3 && i % 6 == 0 ? 2 : 1;
                Particle p = Kit.Line(a, b, Px, 0.16f, thick, TopLayer, 14, Color.white, Fx.WithA(Hue.Edge, 0.7f));
                if (p == null)
                {
                    return;
                }
                Glue(p);
                _lines.Add(p);
                _la.Add(a);
                _lb.Add(b);
                if (i % 2 == 0)
                {
                    Kit.Sparks(c, Px, 1, 30f, 60f, Hue.Edge);
                }
            }

            private void ExitBlur()
            {
                // it is where it started: the blur ends in its finishing crouch
                Stance = _home;
                Pose.Ground = _home;
                if (V != null)
                {
                    Face(VG - Stance);
                }
                if (Vis)
                {
                    Fx.Afterimage(this, 0.1f, Fx.WithA(Hue.Core, 0.8f), Hue.GhostEnd);
                    ChoreoFx.HeelDust(Stance, -Dir, Px, A.current_tile, Kk);
                    Fx.LandingPuff(Stance, Px, A.current_tile);
                }
            }

            /// <summary>The cage trembles in the still air: a few lines catch the light each frame.</summary>
            private void Shimmer(float k)
            {
                TbHiArt.Still(V, 0.3f);
                if (!Vis)
                {
                    return;
                }
                for (int i = 0; i < _lines.Count; i++)
                {
                    Particle p = _lines[i];
                    if (p == null || !p.Alive)
                    {
                        continue;
                    }
                    p.Grad = null;
                    float glow = Random.value < 0.08f ? 1f : 0.45f + 0.25f * Mathf.Sin(BeatT * 31f + i * 1.7f);
                    p.R.color = Color.Lerp(Fx.WithA(Hue.Edge, 0.65f), Fx.WithA(Color.white, 0.95f), glow * k);
                }
                if (!Armed && Random.value < 0.5f)
                {
                    // the blade of ki at its hip, sinking into nothing
                    Vector2 hip = BodyPos() + new Vector2(-Fwd * 0.5f, -1f) * Px;
                    Kit.Line(hip, hip + new Vector2(-Fwd * 4f * (1f - k), -1.5f) * Px, Px, 0.04f, 1, FxLayer.Over, 8, Hue.Core, Fx.WithA(Hue.Edge, 0f));
                }
            }

            private void Click()
            {
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                if (Vis)
                {
                    Fx.Glint(BodyPos() + new Vector2(-Fwd * 1f, -1f) * Px, Px, 0.14f, Color.white, 0.8f);
                }
            }

            /// <summary>Every line flashes at once and shatters; the cut lands.</summary>
            private void Flash()
            {
                if (Vis)
                {
                    for (int i = 0; i < _lines.Count; i++)
                    {
                        Vector2 a = _la[i], b = _lb[i];
                        Kit.Line(a, b, Px, 0.11f, Level >= 3 ? 3 : 2, TopLayer, 16, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                        // the line breaks into shards flying off along it
                        Vector2 d = (b - a).normalized;
                        Vector2 mid = Vector2.Lerp(a, b, Random.Range(0.3f, 0.7f));
                        if (i % 2 == 0)
                        {
                            Particle s = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], mid, Px, Random.Range(0.14f, 0.24f), TopLayer, 15);
                            if (s != null)
                            {
                                s.Moving((Random.value < 0.5f ? d : -d) * Random.Range(50f, 110f) * Px, 0f, 5f);
                                s.FaceVel = true;
                                s.Color(Color.white, _blade.Edge, Fx.WithA(_blade.Edge, 0f));
                            }
                        }
                        Unglue(_lines[i]);
                    }
                }
                _lines.Clear();
                if (!VOk && !Retarget(1.4f))
                {
                    Lose();
                    return;
                }
                if (!Touch(V))
                {
                    return;
                }
                int level = Level;
                Hit h = new Hit(Blow.DrawCut, Mult(level))
                {
                    Hurt = level >= 2 ? Sakuga.Hurt.Crumple : Sakuga.Hurt.Reel,
                    Push = level >= 2 ? 0f : 1f,
                    Power = 1.3f + 0.2f * level,
                    Finisher = true,
                    Area = level >= 3 ? 1.6f : 0f,
                    AreaMult = 0.45f
                };
                Vector2 at = VPoint(0.55f);
                Deliver(V, h, ShapeOf(Blow.DrawCut), at, Dir);
                Vector2 g = VG;
                if (Vis)
                {
                    Kit.Flash(at, Px, Mathf.RoundToInt((6f + 2f * level) * Mathf.Sqrt(Kk)), 0.08f, Color.white, TopLayer, 31);
                    Fx.Ring(at, Px, 0.22f, 3f, (14f + 4f * level) * Kk, 2, 1, TopLayer, 6, 1f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
                    Kit.Sparks(at, Px, 6 + 3 * level, 60f, 150f, _blade.Edge, TopLayer);
                }
                // the ground under the foe takes the cut (a Master's blow breaks whatever it stands on)
                Ruin.Blast(A, g, 0.7f, Scale, Pose.Ground, false);
                if (level >= 3)
                {
                    // III: the cuts run on into the earth, a starburst of scars
                    Kit.BeginTrench();
                    try
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            float ang = Random.Range(0f, Mathf.PI) + i * Mathf.PI / 3f;
                            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                            float len = 1.4f * Kk;
                            Ruin.Furrow(A, g - d * len, g + d * len, 0.5f, Scale, Pose.Ground, false);
                            if (Vis)
                            {
                                Kit.Line(g - d * len, g + d * len, Px, 1.1f, 1, FxLayer.Ground, 5, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                            }
                        }
                    }
                    finally
                    {
                        Kit.EndTrench(A);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Shared bits of the Thousand Blades masters' techniques (tb_hi): silhouettes of the performer
    /// posed elsewhere (a blur's afterimages, a dancer's ghosts), petals of light, a blade of ki.
    /// </summary>
    internal static class TbHiArt
    {
        /// <summary>
        /// A flat-colour silhouette of the performer as if it stood at 'ground' ('height' up, facing
        /// right or not, turned 'rot' degrees, squashed sx/sy), fading over 'life'.
        /// </summary>
        public static Particle GhostAt(ChoreoMove m, Vector2 ground, float height, bool faceRight, float rot, float sx, float sy, float life, FxLayer layer, params Color[] colors)
        {
            Actor a = m.A;
            if (a == null || !Kit.Visible(ground, 0.3f))
            {
                return null;
            }
            Sprite s = Fx.UnitSprite(a);
            if (s == null)
            {
                return null;
            }
            Pose q = m.Pose;
            q.Ground = ground;
            q.Height = height;
            q.FaceRight = faceRight;
            q.Rot = rot;
            q.SX = sx;
            q.SY = sy;
            q.Nudge = Vector2.zero;
            bool flip = a.asset.can_flip;
            Vector2 off = Moves.PivotOffset(q, a.actor_scale, flip);
            Particle p = Fx.Spawn(s, new Vector2(ground.x + off.x, ground.y + off.y + height), 1f, life, layer, 0, true);
            if (p == null)
            {
                return null;
            }
            p.UseEuler = true;
            p.Euler = new Vector3(0f, flip && faceRight ? 180f : 0f, rot);
            p.SX = a.actor_scale * sx;
            p.SY = a.actor_scale * sy;
            p.Color(colors);
            return p;
        }

        /// <summary>
        /// Holds a foe still where it stands for 'seconds' (no walking, no swinging; it can still answer
        /// a technique): a unit in a move of its own, flying or in the player's hand is left alone.
        /// </summary>
        public static void Still(Actor v, float seconds)
        {
            if (v == null || !v.isAlive() || Moves.Busy(v) || v.isFlying() || ControllableUnit.isControllingUnit(v))
            {
                return;
            }
            if (v.attack_timer < seconds)
            {
                v.setActionTimeout(seconds);
            }
            v.makeWait(seconds);
        }

        /// <summary>
        /// Petals of light thrown out of 'at' ('z' world units above its ground point), drifting and
        /// settling on the ground: 'speed' pixels a second outward, 'up' upward.
        /// </summary>
        public static void Petals(Vector2 ground, float z, float px, int n, Hue hue, float speed, float up, float life, FxLayer layer = FxLayer.Top)
        {
            if (!Kit.Visible(ground + new Vector2(0f, z)))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), ground + d * px, px, life * Random.Range(0.75f, 1.2f), layer, 11);
                if (p == null)
                {
                    break;
                }
                p.Z = z;
                p.Moving(d * speed * Random.Range(0.4f, 1f) * px, up * Random.Range(0.3f, 1f) * px, 2.2f, 26f * px);
                p.Bounce = 0.05f;
                p.Spin = Random.Range(-300f, 300f);
                Color c = i % 3 == 0 ? hue.Core : i % 3 == 1 ? hue.Main : hue.Edge;
                p.Color(Color.white, Fx.WithA(c, 0.95f), Fx.WithA(c, 0.8f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>
        /// The blade in its hand this frame (a short-lived line from 'hilt' along 'dir', 'len' sprite
        /// pixels): steel with a glint when it carries a weapon, a flickering blade of ki when it does not.
        /// </summary>
        public static void Blade(ChoreoMove m, Vector2 hilt, Vector2 dir, float len, Hue hue)
        {
            if (!m.Vis || dir.sqrMagnitude < 1e-6f)
            {
                return;
            }
            dir.Normalize();
            float px = m.Px;
            Vector2 tip = hilt + dir * len * px;
            if (m.Armed)
            {
                Kit.Line(hilt, tip, px, 0.035f, 1, FxLayer.Over, 9, Color.white, Fx.WithA(hue.Edge, 0.9f));
            }
            else
            {
                float flick = 0.85f + 0.15f * Random.value;
                Kit.Line(hilt, tip, px, 0.035f, 2, FxLayer.Over, 8, Fx.WithA(hue.Edge, 0.7f * flick), Fx.WithA(hue.Edge, 0.4f));
                Kit.Line(hilt, tip, px, 0.035f, 1, FxLayer.Over, 9, Fx.WithA(hue.Core, flick), Fx.WithA(hue.Core, 0.8f));
            }
        }
    }
}
