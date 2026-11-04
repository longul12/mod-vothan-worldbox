using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SPIRAL ORB (Ki Arts, Adept). It steps in with one palm drawn back at its side, where an orb of
    /// ki spins up (a bright core, a whirl of arcs racing round it, motes drawn in), and drives it
    /// into the foe's body like a drill: the orb grinds there, whirling faster, sparks spraying off
    /// it, the foe held shaking on the spot with every turn, then it bursts and the foe is blasted
    /// away spinning in a ring of ki. A drill, not a charge: it can't be broken, only read (a slip,
    /// a parry meet the palm). I: 4 grinding turns, blasted 2.5 tiles; II: 6, 3 tiles, more light;
    /// III: 8, 3.5 tiles, the burst throws back whoever stands beside the foe and breaks the ground.
    /// Damage (x the user's damage): I 0.6 + 1.4, II 0.8 + 1.9, III 1.1 + 2.5.
    /// </summary>
    internal sealed class SpiralOrbTech : Technique
    {
        public override string Id => "spiral_orb";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Palm | Form.Drill;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 26f, 44f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float GrindTime(int level)
        {
            return level <= 1 ? 0.3f : level == 2 ? 0.4f : 0.5f;
        }

        private static int Turns(int level)
        {
            return level <= 1 ? 4 : level == 2 ? 6 : 8;
        }

        private static float GrindMult(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.8f : 1.1f;
        }

        private static float BlastMult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.9f : 2.5f;
        }

        public static float Total(int level)
        {
            return GrindMult(level) + BlastMult(level);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Total(1), Crowd = 0.06f, Alone = 8f, IntoGuard = 0.9f, Worth = 12f },
            new MeleeWant { Reach = 2.4f, Total = Total(2), Crowd = 0.05f, Alone = 9f, IntoGuard = 0.9f, Worth = 14f },
            new MeleeWant { Reach = 2.6f, Total = Total(3), Crowd = 0.03f, Alone = 10f, IntoGuard = 0.9f, Worth = 18f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            return Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Orb orb = new Orb();
            m.StepIn()
                // GATHER: the palm drawn back at its side; the orb spins up in it
                .PoseTo(0.2f, new Shape(-14f, 0.92f, 1.06f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind).Shows(Forms)
                .OnEnter(mm => orb.Make(mm))
                .Every((mm, k) =>
                {
                    orb.Place(mm, Back(mm), Mathf.Lerp(0.3f, 1f, Beat.Apply(Ease.OutQuad, k)), 900f + 600f * k);
                    if (mm.Vis && Random.value < 0.6f)
                    {
                        ChoreoFx.Motes(Back(mm), mm.Px, 1, 8f, 0.09f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                    if (k > 0.75f)
                    {
                        mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.4f);
                    }
                });
            m.Last.NeedsVictim = true;
            // DRIVE: the palm and the orb in it thrust into the foe (a lunge after one that slid away)
            Beat drive = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(16f, 1.22f, 0.9f, 0f, 0.2f),
                Ease = Ease.OutExpo,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = mm => mm.ApproachSpot(),
                PerTile = 0.04f,
                NeedsVictim = true,
                Forms = Forms
            };
            drive.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Vector2 from = mm.Root(Limb.Palm);
                    Vector2 to = Vector2.Lerp(mm.ContactPoint(0.5f), from, 0.15f);
                    ChoreoFx.Smear(from, to, mm.Px, 0.1f, 2, 2, mm.Hue.Core, mm.Hue.Edge, FxLayer.Over, 7);
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), mm.Dir, mm.Px, 2, 2.5f, 0.08f, Fx.WithA(mm.Hue.Edge, 0.75f), FxLayer.Over);
                    Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -6f, 0.08f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
                }
            };
            drive.Every = (mm, k) => orb.Place(mm, mm.Hand(Limb.Palm), 1f, 1500f);
            drive.On(0.6f, mm => orb.Contact(mm));
            m.Add(drive);
            // GRIND: it bores into the body, whirling faster; the foe is held shaking on the spot
            Beat grind = new Beat
            {
                Tempo = Tempo.Act,
                Dur = GrindTime(level),
                To = new Shape(20f, 1.16f, 0.92f, 0f, 0.16f),
                Ease = Ease.OutQuad,
                Forms = Forms,
                If = mm => orb.Grinding
            };
            grind.Every = (mm, k) => orb.Grind(mm, k);
            m.Add(grind);
            // BLAST: the orb bursts and the foe is blasted away, spinning
            m.Do(mm => orb.Blast(mm)).OnlyIf(mm => orb.Grinding);
            // the palm stays out as the light scatters
            m.PoseTo(0.12f, new Shape(10f, 1.08f, 0.95f, 0f, 0.1f), Ease.OutQuad, Tempo.Follow).Shows(Forms);
            m.Recover(0.25f).OnEnter(mm => orb.Kill(mm, true));
            return m.Start();
        }

        /// <summary>The palm drawn back at its side (where the orb spins up).</summary>
        private static Vector2 Back(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.22f, -m.Hpx * 0.02f) * m.Px;
        }

        /// <summary>The spinning orb (its core, its glow, the arcs whirling round it) and the grind it does.</summary>
        private sealed class Orb
        {
            private Particle _core, _glow;
            private readonly Particle[] _arcs = new Particle[3];
            private float _turn;
            private int _ticks;
            private Vector2 _at;
            public bool Grinding;

            public void Make(ChoreoMove m)
            {
                float r = (2f + 0.5f * m.Level) * m.Kk;
                Vector2 at = Back(m);
                // a gold ball with a white heart, and a whirl of bright arcs racing round it, wider than the ball
                _glow = m.Glue(Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(r * 1.25f)), 1f, 0.35f), at, m.Px, 1f, FxLayer.Over, 8));
                _core = m.Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(r * 0.55f)), 1f, 0.1f), at, m.Px, 1f, FxLayer.Over, 10));
                Sprite[] arc = Art.Arc(Mathf.Max(4, Mathf.RoundToInt(r * 1.9f)), 1f, 0f, 100f, m.Level >= 3 ? 2 : 1);
                for (int i = 0; i < _arcs.Length; i++)
                {
                    _arcs[i] = m.Glue(Fx.Spawn(arc, at, m.Px, 1f, FxLayer.Over, 11));
                }
                Choreo.Sound("event:/SFX/POWERS/Powerup", m.A.current_tile, 0.15f);
            }

            /// <summary>The orb at 'at', 'size' of its full size, whirling 'spin' degrees a second.</summary>
            public void Place(ChoreoMove m, Vector2 at, float size, float spin)
            {
                _at = at;
                _turn += spin * Clock.Dt;
                float pulse = 1f + 0.12f * Mathf.Sin(m.T * 47f);
                Set(_glow, at, size * pulse, Fx.WithA(m.Hue.Main, 0.92f), 0f);
                Set(_core, at, size, Color.white, 0f);
                for (int i = 0; i < _arcs.Length; i++)
                {
                    Set(_arcs[i], at, size * (1f - 0.12f * i), Fx.WithA(i == 1 ? m.Hue.Rim : Color.Lerp(Color.white, m.Hue.Core, 0.3f), 0.95f), _turn + 120f * i);
                }
            }

            private static void Set(Particle p, Vector2 at, float size, Color c, float rot)
            {
                if (p == null || !p.Alive)
                {
                    return;
                }
                p.Pos = at;
                p.Size0 = p.Size1 = size;
                p.Rot = rot;
                p.Grad = null;
                p.R.color = c;
            }

            /// <summary>The palm arrives: the orb meets the body (it may still be answered), or cuts the air.</summary>
            public void Contact(ChoreoMove m)
            {
                if (!m.VOk)
                {
                    Fizzle(m);
                    m.Lose();
                    return;
                }
                float reach = m.Stand + 0.6f * m.Kk + m.V.stats["size"] * 0.5f + 0.3f;
                if (Vector2.Distance(m.Pose.Ground, m.VG) > reach)
                {
                    Fizzle(m);
                    m.Lose();
                    return;
                }
                if (!m.Touch(m.V))
                {
                    return;   // slipped, parried: the move plays its recoil or its whiff
                }
                Grinding = true;
                _ticks = 0;
                Combat.Pin(m.V, GrindTime(m.Level) + 0.5f);
                Choreo.Sound("event:/SFX/HIT/HitGeneric", m.V.current_tile);
                if (m.Vis)
                {
                    Vector2 at = m.ContactPoint(0.5f);
                    Kit.Flash(at, m.Px, Mathf.RoundToInt(3f * m.Kk), 0.05f, Fx.WithA(m.Hue.Core, 1f), m.TopLayer, 24);
                    Fx.Ring(at, m.Px, 0.14f, 2f, 7f * m.Kk, 1, 1, m.TopLayer, 22, 1f, 2.4f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                }
            }

            /// <summary>One frame of the grind: a turn of the drill lands every so often, the foe shaking under it.</summary>
            public void Grind(ChoreoMove m, float k)
            {
                if (!m.VOk)
                {
                    Grinding = false;
                    Fizzle(m);
                    m.Lose();
                    return;
                }
                Combat.Pin(m.V, 0.3f);
                Vector2 at = m.ContactPoint(0.5f);
                Place(m, Vector2.Lerp(m.Hand(Limb.Palm), at, 0.7f), 1f + 0.25f * k, 1800f + 1800f * k);
                m.Pose.Nudge = Choreo.Tremble(m.BeatT, 0.6f);
                int n = Turns(m.Level);
                while (_ticks < n && k >= (_ticks + 1f) / (n + 1f))
                {
                    _ticks++;
                    HitResult r = Combat.Hit(m.A, m.V, GrindMult(m.Level) / n * m.Cm, false, false, 0f, m);
                    m.LastHit = r;
                    Victims.Hurt(m.V, m.A, m, Impact.Of(Hurt.Flinch, m.Dir, 0.03f, 0.6f));
                    if ((_ticks & 1) == 1)
                    {
                        ChoreoFx.BlowSound(Limb.Palm, 0.5f, false, m.V.current_tile);
                    }
                    if (m.Vis)
                    {
                        // sparks thrown off the turning edge, round the drill
                        Vector2 side = m.Side * ((_ticks & 1) == 0 ? 1f : -1f);
                        for (int i = 0; i < 2; i++)
                        {
                            Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, m.Px, Random.Range(0.1f, 0.18f), m.TopLayer, 19);
                            if (p == null)
                            {
                                break;
                            }
                            p.Moving((side + m.Dir * Random.Range(-0.6f, 0.2f)).normalized * Random.Range(60f, 110f) * m.Px * m.Kk, Random.Range(0f, 30f) * m.Px, 6f);
                            p.FaceVel = true;
                            p.Color(Color.white, m.Hue.Edge, Fx.WithA(m.Hue.Edge, 0f));
                        }
                        if ((_ticks & 1) == 0)
                        {
                            Fx.Ring(at, m.Px, 0.12f, 2f, (5f + 1.5f * m.Level) * m.Kk, 1, 1, m.TopLayer, 21, 0.55f, 2f,
                                Fx.WithA(m.Hue.Core, 0.85f), Fx.WithA(m.Hue.Edge, 0f));
                        }
                    }
                }
            }

            /// <summary>The orb bursts: the foe is blasted away spinning in a ring of ki.</summary>
            public void Blast(ChoreoMove m)
            {
                if (!m.VOk)
                {
                    Fizzle(m);
                    m.Lose();
                    return;
                }
                int level = m.Level;
                Actor v = m.V;
                Vector2 at = m.ContactPoint(0.5f);
                Hit h = new Hit(Blow.Palm, BlastMult(level))
                {
                    Hurt = Hurt.Launch,
                    Push = level <= 1 ? 2.5f : level == 2 ? 3f : 3.5f,
                    Lift = level <= 1 ? 1f : level == 2 ? 1.2f : 1.5f,
                    Power = 1.2f + 0.15f * level,
                    Finisher = level >= 2,
                    Crater = level >= 3 ? 0.6f : 0f,
                    Area = level >= 3 ? 1.3f : 0f,
                    AreaMult = 0.5f,
                    Shows = Form.Ki | Form.Palm | Form.Drill
                };
                m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Palm), at, m.Dir);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", v.current_tile, 0.1f);
                Kill(m, false);
                if (!m.Vis)
                {
                    return;
                }
                float px = m.Px, k = m.Kk;
                Kit.Flash(at, px, Mathf.RoundToInt((4f + level) * k), 0.08f, Color.white, m.TopLayer, 26);
                Fx.Ring(at, px, 0.24f, 3f, (10f + 4f * level) * k, 2, 1, m.TopLayer, 23, 1f, 2.2f, Fx.WithA(m.Hue.Core, 1f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Rim, 0f));
                // the whirl flung off the burst: arcs spinning out and away
                Sprite[] arc = Art.Arc(Mathf.RoundToInt((4f + level) * k), 1f, 0f, 120f, 1);
                for (int i = 0; i < 3; i++)
                {
                    float ang = (_turn + 120f * i) * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    Particle p = Fx.Spawn(arc, at, px, 0.26f, m.TopLayer, 22);
                    if (p == null)
                    {
                        break;
                    }
                    p.Rot = _turn + 120f * i;
                    p.Spin = 1400f;
                    p.Moving((d + m.Dir) * 40f * px * k, 0f, 5f).Sized(1f, 1.6f).Color(Color.white, Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Rim, 0f));
                }
                KiFlight.FollowRing(v, m.Hue, px, 0.42f + 0.06f * level, 3f, (9f + 3f * level) * k, 2, true);
            }

            /// <summary>The orb comes apart in the air (a whiff, a lost target): its light scatters.</summary>
            private void Fizzle(ChoreoMove m)
            {
                if (m.Vis)
                {
                    Kit.Flash(_at, m.Px, 2, 0.05f, Fx.WithA(m.Hue.Core, 0.8f));
                    Kit.Sparks(_at, m.Px, 4, 30f, 70f, m.Hue.Edge);
                }
                Kill(m, false);
            }

            public void Kill(ChoreoMove m, bool fade)
            {
                if (fade && _core != null && _core.Alive && m.Vis)
                {
                    Kit.Sparks(_at, m.Px, 2, 20f, 40f, m.Hue.Edge);
                }
                m.Unglue(_core);
                m.Unglue(_glow);
                for (int i = 0; i < _arcs.Length; i++)
                {
                    m.Unglue(_arcs[i]);
                    _arcs[i] = null;
                }
                _core = _glow = null;
            }
        }
    }
}
