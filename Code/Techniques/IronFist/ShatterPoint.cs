using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SHATTER POINT (Iron Fist, Master). No wind-up worth the name: it steps in, one knuckle
    /// extended, and touches a pressure point (a tiny white flash, a ting). It takes its hand back and
    /// stands still. Glowing crimson cracks run out from the spot over the foe's body (the foe
    /// trembles where it stands, held by the blow), and a beat later the foe bursts from inside: a
    /// flash through every crack, shards of light, the foe crumples (a giant reels). The burst
    /// ignores armour: made for constructs and titans (golems, robots, dragons on the ground, giants),
    /// it is just as deadly to an armoured knight. I (Master): x2.6, four cracks, a beat of 0.55 s.
    /// II: x3.1, six cracks, the burst's shock knocks back whoever stands next to the foe. III: x3.6,
    /// nine cracks running down into the ground, the earth under the foe breaks, a black flash.
    /// </summary>
    internal sealed class ShatterPointTech : Technique
    {
        public override string Id => "shatter_point";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Execute;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Heavy;
        public override Prey Prey => Prey.Construct | Prey.Titan;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 30f, 48f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3.1f : 3.6f;
        }

        /// <summary>How much of a blow gets through this foe's armour (the burst ignores it: x1 / this).</summary>
        public static float Through(Actor v)
        {
            float armor = v != null ? Mathf.Min(v.stats["armor"], 95f) : 0f;
            return Mathf.Max(0.25f, 1f - armor / 100f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Mult(1), Worthy = true, Crowd = 0.06f, Alone = 12f, IntoGuard = 0.8f, Worth = 18f },
            new MeleeWant { Reach = 2.6f, Total = Mult(2), Worthy = true, Crowd = 0.05f, Alone = 14f, IntoGuard = 0.8f, Worth = 22f },
            new MeleeWant { Reach = 2.8f, Total = Mult(3), Worthy = true, Crowd = 0.04f, Alone = 16f, IntoGuard = 0.8f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            // armour means nothing to it: the harder the shell, the more it is worth
            float through = Through(v);
            p.Score *= Mathf.Clamp(1f / through, 1f, 2.5f);
            // the bigger the body, the deeper the cracks run (a giant's health is what it is for)
            if (Weights.Species(v) >= Weight.Brute)
            {
                p.Score += 15f * Mathf.Min(3f, (float)Weights.Species(v) - 1f);
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ShatterPointMove m = ShatterPointMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Shatter Point's move: the touch, the cracks growing over the foe, the delayed burst.</summary>
    internal sealed class ShatterPointMove : ChoreoMove
    {
        private struct Crack
        {
            public float Ang, Len, Bend, Bend2;
            public Particle A, B, C;
        }

        private Crack[] _cracks;
        private Vector2 _spot;
        private float _flickT;
        private bool _marked;
        private Actor _mark;
        private long _markId = -1;

        private ShatterPointMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static ShatterPointMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new ShatterPointMove(a, t, p);
        }

        private static float Delay(int level)
        {
            return level <= 1 ? 0.55f : level == 2 ? 0.5f : 0.45f;
        }

        public void Build()
        {
            int level = Level;
            StepIn();
            // calm: the hand drawn back a hair, one knuckle raised (a glint on it)
            PoseTo(0.13f, new Shape(-5f, 0.96f, 1.05f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind)
                .At(0.7f, mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.Hand(Limb.Fist) + new Vector2(0f, 1f * mm.Px), mm.Px, 0.14f, Color.white, 0.8f);
                    }
                });
            // the touch: a short, exact jab to the pressure point
            Strike(new Hit(Blow.Jab, 0.15f)
            {
                Hurt = Hurt.Flinch,
                Power = 0.45f,
                Lethal = false,
                WindScale = 0.5f,
                ActScale = 0.9f,
                Shows = Form.Punch | Form.Straight | Form.Heavy,
                OnLand = (mm, v, r) => ((ShatterPointMove)mm).Mark(v)
            });
            // it takes its hand back and waits: the cracks run over the foe
            PoseTo(0.12f, new Shape(-3f, 1f, 1.02f, 0f, -0.22f), Ease.OutQuad, Tempo.Follow).OnlyIf(mm => ((ShatterPointMove)mm)._marked);
            Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = Delay(level),
                To = new Shape(-2f, 1f, 1f, 0f, -0.22f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Punch | Form.Heavy,
                If = mm => ((ShatterPointMove)mm)._marked,
                Every = (mm, k) => ((ShatterPointMove)mm).Spread(k)
            });
            // the burst
            Do(mm => ((ShatterPointMove)mm).Burst()).OnlyIf(mm => ((ShatterPointMove)mm)._marked);
            Hold(0.1f, false, Tempo.Impact).OnlyIf(mm => ((ShatterPointMove)mm)._marked);
            Recover(0.28f);
        }

        /// <summary>The knuckle found the point: cracks begin to run from it.</summary>
        private void Mark(Actor v)
        {
            if (v == null || !Combat.Living(v) || v != V)
            {
                return;
            }
            _marked = true;
            _mark = v;
            _markId = v.Sid();
            _spot = ContactPoint(0.6f);
            int n = Level <= 1 ? 4 : Level == 2 ? 6 : 9;
            _cracks = new Crack[n];
            float a0 = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                _cracks[i] = new Crack
                {
                    Ang = a0 + i * 360f / n + Random.Range(-20f, 20f),
                    Len = Random.Range(0.55f, 1f),
                    Bend = Random.Range(25f, 50f) * (Random.value < 0.5f ? -1f : 1f),
                    Bend2 = Random.Range(25f, 55f) * (Random.value < 0.5f ? -1f : 1f)
                };
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", v.current_tile);
            if (Vis)
            {
                Kit.Flash(_spot, VPx, 2, 0.05f, Color.white, TopLayer, 24);
                Fx.Glint(_spot, VPx, 0.18f, Color.white, 1f);
            }
        }

        /// <summary>The cracks grow out over the foe's body (it trembles where it stands, held by the blow).</summary>
        private void Spread(float k)
        {
            if (_mark == null || _mark.Sid() != _markId || !Combat.Living(_mark))
            {
                return;
            }
            Vector2 now = ContactPoint(0.6f);
            if ((now - _spot).sqrMagnitude < 4f)
            {
                _spot = now;
            }
            _flickT -= Clock.Dt;
            if (_flickT <= 0f)
            {
                _flickT = 0.12f - 0.06f * k;
                Victims.Flash(_mark, Color.Lerp(Hue.Edge, Hue.Core, k), 0.03f);
            }
            if (!Vis || _cracks == null)
            {
                return;
            }
            float px = VPx;
            // a crack's reach: within the foe's body (its sprite's height), a giant's cracks run long
            float size = Mathf.Max(VHeight * 0.46f, 3.5f * px);
            float grow = Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k * 1.25f));
            float glow = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(BeatT * 22f));
            // glowing crimson at every level (the Iron Fist's), whiter as the burst nears
            Color c0 = Color.Lerp(Schools.Color(School.IronFist), new Color(1f, 0.93f, 0.8f, 1f), 0.08f + 0.4f * k * glow);
            Color c1 = Fx.WithA(Color.Lerp(c0, Hue.Rim, 0.3f), 0.85f);
            for (int i = 0; i < _cracks.Length; i++)
            {
                Crack cr = _cracks[i];
                // three jagged pieces, each turning off the last
                float len = size * cr.Len * grow / 3f;
                Vector2 p1 = _spot + Dir(cr.Ang) * len;
                Vector2 p2 = p1 + Dir(cr.Ang + cr.Bend) * len;
                Vector2 p3 = p2 + Dir(cr.Ang + cr.Bend + cr.Bend2) * len * 0.8f;
                cr.A = Segment(cr.A, _spot, p1, px, c0);
                cr.B = Segment(cr.B, p1, p2, px, c0);
                cr.C = Segment(cr.C, p2, p3, px, c1);
                _cracks[i] = cr;
            }
            if (Level >= 2 && k > 0.6f && Random.value < 0.3f)
            {
                Kit.Sparks(_spot, px, 1, 20f, 50f, c0);
            }
        }

        private static Vector2 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        /// <summary>One straight piece of a crack, a held particle (one texel wide) re-laid every frame.</summary>
        private Particle Segment(Particle p, Vector2 a, Vector2 b, float px, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < px * 0.5f)
            {
                return p;
            }
            if (p == null || !p.Alive)
            {
                p = Glue(Fx.Spawn(Art.Pixel, a, px, 1f, TopLayer, 23));
                if (p == null)
                {
                    return null;
                }
            }
            p.Pos = (a + b) * 0.5f;
            p.SX = len / px;
            p.SY = 1f;
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Grad = null;
            p.R.color = c;
            return p;
        }

        private void KillCracks()
        {
            if (_cracks == null)
            {
                return;
            }
            for (int i = 0; i < _cracks.Length; i++)
            {
                if (_cracks[i].A != null)
                {
                    Unglue(_cracks[i].A);
                }
                if (_cracks[i].B != null)
                {
                    Unglue(_cracks[i].B);
                }
                if (_cracks[i].C != null)
                {
                    Unglue(_cracks[i].C);
                }
                _cracks[i].A = _cracks[i].B = _cracks[i].C = null;
            }
        }

        /// <summary>The burst from inside: the light through every crack, shards, armour meaning nothing.</summary>
        private void Burst()
        {
            Vector2 at = _spot;
            float size = Mathf.Max(VHeight * 0.46f, 3.5f * VPx);
            if (Vis && _cracks != null)
            {
                // every crack flashes white along its length as it gives
                for (int i = 0; i < _cracks.Length; i++)
                {
                    Vector2 d = Dir(_cracks[i].Ang);
                    Kit.Line(at, at + d * size * _cracks[i].Len, VPx, 0.1f, 2, TopLayer, 25, Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Edge, 0f));
                }
            }
            KillCracks();
            Actor v = _mark;
            if (v == null || v.Sid() != _markId || !Combat.Living(v))
            {
                return;
            }
            int level = Level;
            Combat.Allowed(A, v, out float cm);
            // armour is nothing to it: the blow is sized so that what the armour would take is given back
            float mult = ShatterPointTech.Mult(level) * cm / ShatterPointTech.Through(v);
            HitResult r = Combat.Hit(A, v, mult, true, false, 0f, this);
            LastHit = r;
            if (Combat.Living(v))
            {
                Impact im = Impact.Of(Hurt.Crumple, v.current_position - Pose.Ground, 0f, 1.8f);
                im.Daze = 0.6f + 0.3f * level;
                im.Stop = 0.06f * ImpactK;
                Victims.Hurt(v, A, this, im);
                SetLink(Link.Down, v);
            }
            Victims.Flash(v, Color.white, 0.1f);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", v.current_tile);
            ChoreoFx.BlowSound(Limb.Fist, 1.6f, false, v.current_tile);
            Kit.Stop(at, (0.05f + 0.015f * level) * ImpactK);
            Ruin.Quake(A, at, Scale, 1.2f, Kk);
            Vector2 g = Victims.GroundOf(v);
            if (level >= 2)
            {
                // the shock of it knocks back whoever stands next to the foe
                AreaBlow(g, 1.1f * Mathf.Lerp(1f, Choreo.KOf(v), 0.5f), 0.6f, v, Hurt.Reel, 0.9f, 0.9f);
            }
            if (level >= 3)
            {
                // the cracks run on down into the ground
                Ruin.Blast(A, g, 0.9f, Scale, Pose.Ground);
                if (Vis)
                {
                    Kit.Crater(g, VPx * Mathf.Max(1f, Choreo.KOf(v)), Combat.TileAt(g), true, 5f, true, Hue.Edge);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = Random.value * 360f;
                        Particle f = Fx.Spawn(ChoreoArt.Fissure(Mathf.RoundToInt(8f * Choreo.KOf(v) + Random.Range(0f, 5f)), i), g, VPx, 2.2f, FxLayer.Ground, 3);
                        if (f != null)
                        {
                            f.Rot = a;
                            f.Snap = true;
                            f.Color(Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                        }
                    }
                }
            }
            if (!Vis)
            {
                return;
            }
            float px = VPx;
            Kit.Flash(at, px, Mathf.RoundToInt(5f + 1.5f * level), 0.08f, Fx.WithA(Hue.Core, 1f), TopLayer, 26);
            Fx.Ring(at, px, 0.24f, 2f, 10f + 3f * level, 2, 1, TopLayer, 5, 0.8f, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            int n = Fx.Count(8 + 4 * level);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), at + d * px, px, Random.Range(0.3f, 0.55f), TopLayer, 12);
                if (p == null)
                {
                    break;
                }
                p.Moving(d * Random.Range(40f, 100f) * px, Random.Range(10f, 30f) * px, 2f, 150f * px);
                p.Spin = Random.Range(-600f, 600f);
                p.Color(Color.white, Fx.WithA(Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            Kit.Sparks(at, px, 6 + 2 * level, 60f, 130f, Hue.Edge, TopLayer);
            if (level >= 2 || r.Crit)
            {
                // (behind the bodies: they read as silhouettes against it; a black flash at III)
                Kit.ImpactFrame(at, px, (0.25f + 0.08f * level) * Mathf.Max(1f, Choreo.KOf(v)), Hue.Pal, level >= 3 || r.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
            }
        }

        public override void Abort()
        {
            base.Abort();
            if (_cracks != null)
            {
                for (int i = 0; i < _cracks.Length; i++)
                {
                    _cracks[i].A = _cracks[i].B = _cracks[i].C = null;
                }
            }
        }
    }
}
