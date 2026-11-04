using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DRAGONFALL (Hunter's Way, Grandmaster). Made for dragons. It sinks into a deep crouch (a ring
    /// of wind closing on its feet) and leaps, a long arc up to the dragon in the sky, and lands on
    /// its back between the wings: the dragon jolts. It rides it a moment, crouched against the
    /// wind (speed lines streaming past, the dragon carrying it on), II and III hammering blows
    /// into its back as it goes; then it runs up the neck, seizes the head in both arms and wrenches
    /// it round: a crack, an impact frame (III: a black flash), and the dragon's flight dies. They
    /// come down together: the hunter rides it all the way into its crash, and the earth takes them
    /// (a crater, a trench where the dragon ploughs in, a ring of dust, everyone near knocked flat).
    /// The hunter springs off its back and lands beside it while it lies there, held down, stars
    /// round its head. A dragon already on the ground has its head driven into the earth instead.
    /// Damage (x the user's damage): I wrench 2.4 + crash 2.6; II 2 back blows 0.5 + 2.6 + 3.0; III
    /// 3 back blows 0.5 + 3.2 + 3.7. Leaves Down.
    /// </summary>
    internal sealed class DragonfallTech : Technique
    {
        public override string Id => "dragonfall";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Air;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Aerial | Form.Heavy;
        public override Prey Prey => Prey.Dragon;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 45f, 70f, 105f };
        public override float[] Cooldown => new[] { 18f, 18f, 20f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Wrench(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 2.6f : 3.2f;
        }

        public static float Crash(int level)
        {
            return level <= 1 ? 2.6f : level == 2 ? 3f : 3.7f;
        }

        public static int BackBlows(int level)
        {
            return level <= 1 ? 0 : level == 2 ? 2 : 3;
        }

        public static float Total(int level)
        {
            return Wrench(level) + Crash(level) + 0.5f * BackBlows(level);
        }

        public override Plan Plan(Ctx c, int level)
        {
            MeleeWant w = new MeleeWant
            {
                Reach = level <= 1 ? 9f : level == 2 ? 10f : 12f,
                Total = Total(level),
                Worthy = true,
                Crowd = 0f,
                Alone = 10f,
                IntoGuard = 1f,
                Worth = 40f
            };
            Plan p = BigGame.Pick(c, level, w, Prey.Dragon, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            if (!BigGame.IsDragon(v))
            {
                return null;
            }
            // they come down together: there must be ground under it, and its back must be in reach of the leap
            if (!Combat.SafeSpot(v.current_position) || BigGame.Size(v).Crown > DragonRide.MaxLeap(level, Choreo.KOf(c.Self)))
            {
                return null;
            }
            // the ones near where it will crash are caught too
            int near = c.CountNear(v.current_position, 2.5f) - (Combat.Grounded(v) ? 1 : 0);
            p.Score += 30f + Mathf.Max(0, near) * 10f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            DragonRide ride = new DragonRide(m);
            int level = m.Level;
            // the crouch: a ring of wind closing on its feet (II/III: the ground cracks under them)
            m.Crouch(0.14f, 1.4f).OnEnter(mm => ride.Gather(mm));
            ride.AddLeap();
            ride.AddRide(level <= 1 ? 0.3f : 0.22f);
            for (int i = 0; i < DragonfallTech.BackBlows(level); i++)
            {
                ride.AddBackBlow(i);
            }
            ride.AddToHead();
            ride.AddWrench();
            ride.AddFall();
            ride.AddSpringOff();
            m.Recover(0.3f);
            return m.Start();
        }
    }

    /// <summary>One Dragonfall: the leap onto the dragon's back, the ride, the wrench, the fall together.</summary>
    internal sealed class DragonRide
    {
        private readonly ChoreoMove _m;
        private readonly Actor _v;
        private readonly long _vid;
        private Wingbreaker.Box _box;
        private bool _onBack, _wrenched, _crashed;
        private float _lines, _hx;
        /// <summary>It started on the ground (no flight to break: its head is driven into the earth).</summary>
        private readonly bool _landed;

        public DragonRide(ChoreoMove m)
        {
            _m = m;
            _v = m.V;
            _vid = _v.Sid();
            _box = BigGame.Size(_v);
            _hx = _box.HeadX;
            _landed = !_v.isFlying();
        }

        /// <summary>How high its leap reaches (world units, its size counted).</summary>
        public static float MaxLeap(int level, float k)
        {
            return (level <= 1 ? 11f : level == 2 ? 12.5f : 14f) * Mathf.Clamp(k, 0.85f, 1.4f);
        }

        private bool Ok => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        private bool Same => _v != null && _v.Sid() == _vid;

        private void Refresh()
        {
            if (Ok)
            {
                _box = BigGame.Size(_v);
                // (its head swings from side to side as it turns: followed smoothly, never snapped)
                _hx = Mathf.Lerp(_hx, _box.HeadX, Mathf.Clamp01(Clock.Dt * 10f));
            }
        }

        /// <summary>Its back between the wings: the ground point under it and the height above it.</summary>
        private void Back(out Vector2 g, out float h)
        {
            g = _v.current_position + new Vector2(-_hx * 0.35f, -0.06f);
            h = Mathf.Lerp(_box.Center, _box.Crown, 0.55f);
        }

        /// <summary>Its head: the ground point under it and the height of the crown.</summary>
        private void Head(out Vector2 g, out float h)
        {
            g = _v.current_position + new Vector2(_hx * 0.85f, -0.06f);
            h = Mathf.Lerp(_box.Center, _box.Crown, 0.8f);
        }

        private void FaceHead(ChoreoMove m)
        {
            float dx = _hx;
            if (Mathf.Abs(dx) > 0.02f)
            {
                m.Face(new Vector2(Mathf.Sign(dx), 0f));
            }
        }

        public void Gather(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.1f);
            if (!m.Vis)
            {
                return;
            }
            Fx.Ring(m.Pose.Ground, m.Px, 0.2f, 14f * m.Kk, 3f, 1, 2, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Core, 0.9f));
            Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 40f, 0.5f, m.Kk);
            if (m.Level >= 2)
            {
                Kit.Crater(m.Pose.Ground, m.Px * m.Kk, m.A.current_tile, false, 3f, true, m.Hue.Edge);
            }
        }

        /// <summary>The leap: a long arc up onto its back (it follows the dragon in flight).</summary>
        public void AddLeap()
        {
            ChoreoMove m = _m;
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.42f,
                To = new Shape(-10f, 0.88f, 1.16f, float.NaN),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.03f,
                Lines = 0.04f,
                Forms = Form.Aerial | Form.Hunt
            };
            Vector2 g0 = Vector2.zero;
            float h0 = 0f;
            b.If = mm => Ok;
            b.Enter = mm =>
            {
                Refresh();
                g0 = mm.Stance;
                h0 = mm.Pose.Height;
                Back(out Vector2 g, out float h);
                // a longer leap takes longer
                mm.BeatDur += 0.018f * Vector2.Distance(g0, g) * mm.ActK + 0.02f * h;
                FaceHead(mm);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 12, 48f, 0.5f, mm.Kk);
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 4, 3f, 0.16f, Fx.WithA(mm.Hue.Edge, 0.8f));
                }
                if (mm.Level >= 2)
                {
                    Ruin.Blast(mm.A, mm.Stance, 0.6f, Scale.Light, mm.Stance, false);
                }
            };
            b.Every = (mm, k) =>
            {
                if (!Ok)
                {
                    return;
                }
                Refresh();
                Back(out Vector2 g, out float h);
                float e = Beat.Apply(Ease.InOutSine, k);
                Vector2 at = Vector2.Lerp(g0, g, e);
                mm.Stance = at;
                mm.Pose.Ground = at;
                // up past its back and down onto it
                mm.Pose.Height = Mathf.Lerp(h0, h, Beat.Apply(Ease.OutQuad, k)) + 1.4f * mm.Kk * 4f * k * (1f - k);
                // stretched as it leaves, round at the top, stretched again coming down
                float fast = Mathf.Abs(1f - 2f * k);
                mm.Pose.SX = 1f - 0.12f * fast;
                mm.Pose.SY = 1f + 0.18f * fast;
            };
            m.Add(b);
            // the landing on its back: the dragon jolts
            Beat land = new Beat { Tempo = Tempo.Follow, Dur = 0.1f, To = new Shape(8f, 1.24f, 0.78f, float.NaN), Ease = Ease.OutExpo, Track = false, If = mm => Ok };
            land.Enter = mm =>
            {
                _onBack = true;
                Stick(mm);
                Choreo.Sound("event:/SFX/HIT/HitGeneric", _v.current_tile);
                Victims.Flash(_v, Color.white, 0.05f);
                _v.startShake(0.2f, 0.15f, true, true);
                if (mm.Vis)
                {
                    Vector2 feet = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                    Fx.Ring(feet, mm.Px, 0.22f, 3f, 12f * mm.Kk, 2, 1, FxLayer.Top, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(mm.Hue.Edge, 0f));
                    Kit.Sparks(feet, mm.Px, 4, 40f, 80f, mm.Hue.Edge);
                }
            };
            land.Every = (mm, k) => Stick(mm);
            m.Add(land);
        }

        /// <summary>On its back: carried along wherever it flies.</summary>
        private void Stick(ChoreoMove m)
        {
            if (!Ok)
            {
                return;
            }
            Refresh();
            Back(out Vector2 g, out float h);
            m.Stance = g;
            m.Pose.Ground = g;
            m.Pose.Height = h;
            Combat.Pin(_v, 0.5f);
        }

        /// <summary>The ride: crouched against the wind, carried on, speed lines streaming past.</summary>
        public void AddRide(float dur)
        {
            ChoreoMove m = _m;
            Beat b = new Beat { Tempo = Tempo.Follow, Dur = dur, To = new Shape(14f, 1.12f, 0.88f, float.NaN), Ease = Ease.OutQuad, Track = false, If = mm => Ok, Forms = Form.Aerial | Form.Hunt };
            b.Every = (mm, k) =>
            {
                Stick(mm);
                mm.Pose.Nudge = new Vector2(Mathf.Sin(mm.BeatT * 30f) * 0.5f, 0f);
                Wind(mm);
            };
            m.Add(b);
        }

        private void Wind(ChoreoMove m)
        {
            _lines -= Clock.Dt;
            if (_lines > 0f || !m.Vis)
            {
                return;
            }
            _lines = 0.05f;
            Vector2 back = new Vector2(-Mathf.Sign(_hx == 0f ? m.Fwd : _hx), 0f);
            Kit.SpeedLines(m.BodyPos() + back * 4f * m.Px, back, m.Px, 2, 5f, 0.12f, Fx.WithA(Color.white, 0.7f));
        }

        /// <summary>A hammering blow into its back as it flies (II, III).</summary>
        public void AddBackBlow(int i)
        {
            ChoreoMove m = _m;
            Blow blow = (i & 1) == 0 ? Blow.Cross : Blow.Hook;
            BlowShape s = ChoreoMove.ShapeOf(blow);
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.06f, To = new Shape(-16f, 0.92f, 1.1f, float.NaN), Ease = Ease.OutQuad, Track = false, If = mm => Ok, Every = (mm, k) => Stick(mm) });
            Beat act = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(40f, 1.2f, 0.84f, float.NaN), Ease = Ease.OutExpo, Track = false, If = mm => Ok, Forms = Form.Punch | Form.Hunt | Form.Overhead };
            act.Every = (mm, k) => Stick(mm);
            act.On(0.5f, mm =>
            {
                if (!Ok || !mm.Touch(_v))
                {
                    return;
                }
                Vector2 at = mm.Pose.Ground + new Vector2(mm.Fwd * 2f * mm.Px, mm.Pose.Height);
                if (mm.Vis)
                {
                    ChoreoFx.Smear(mm.Root(Limb.Fist), at, mm.Px, 0.07f, 2, 2, mm.Hue.Core, mm.Hue.Edge);
                    Kit.DustRing(at, mm.Px, mm.A.current_tile, 4, 24f, 0.3f, mm.Kk);
                }
                mm.Deliver(_v, new Hit(blow, 0.5f) { Lethal = false, Hurt = Hurt.Stagger, Power = 1.1f }, s, at, Vector2.down);
            });
            m.Add(act);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.06f, To = new Shape(12f, 1.06f, 0.95f, float.NaN), Ease = Ease.OutQuad, Track = false, If = mm => Ok, Every = (mm, k) => Stick(mm) });
        }

        /// <summary>Up its neck to the head, low and fast.</summary>
        public void AddToHead()
        {
            ChoreoMove m = _m;
            Beat b = new Beat { Tempo = Tempo.Travel, Dur = 0.14f, To = new Shape(24f, 1.1f, 0.9f, float.NaN), Ease = Ease.OutQuad, Track = false, Ghosts = 0.025f, If = mm => Ok, Forms = Form.Dash | Form.Hunt };
            b.Every = (mm, k) =>
            {
                if (!Ok)
                {
                    return;
                }
                Refresh();
                Back(out Vector2 g0, out float h0);
                Head(out Vector2 g1, out float h1);
                float e = Beat.Apply(Ease.OutQuad, k);
                Vector2 g = Vector2.Lerp(g0, g1, e);
                mm.Stance = g;
                mm.Pose.Ground = g;
                mm.Pose.Height = Mathf.Lerp(h0, h1, e);
                FaceHead(mm);
                Combat.Pin(_v, 0.5f);
            };
            m.Add(b);
        }

        private void OnHead(ChoreoMove m)
        {
            if (!Ok)
            {
                return;
            }
            Refresh();
            Head(out Vector2 g, out float h);
            m.Stance = g;
            m.Pose.Ground = g;
            m.Pose.Height = h;
            Combat.Pin(_v, 0.5f);
        }

        /// <summary>
        /// The wrench: both arms round its head, the body coiled one way, then wrenched the other: a
        /// crack, an impact frame, and its flight dies (it goes into a crash landing).
        /// </summary>
        public void AddWrench()
        {
            ChoreoMove m = _m;
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.14f, To = new Shape(-48f, 0.9f, 1.12f, float.NaN), Ease = Ease.OutQuad, Track = false, Tremble = true, If = mm => Ok, Every = (mm, k) => OnHead(mm), Forms = Form.Grab | Form.Hunt | Form.Heavy });
            Beat act = new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(96f, 1.14f, 0.88f, float.NaN), Ease = Ease.OutExpo, Track = false, If = mm => Ok, Forms = Form.Grab | Form.Hunt | Form.Heavy };
            act.Enter = mm =>
            {
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(12f * Mathf.Lerp(1f, mm.Kk, 0.6f)), 6, 40);
                    Kit.Swish(mm, Art.Arc(r, 0.6f, 200f, 380f, mm.Level >= 3 ? 4 : 3), 0.16f, new Vector2(0f, 2f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                }
            };
            act.Every = (mm, k) => OnHead(mm);
            act.On(0.5f, mm => Wrench(mm));
            m.Add(act);
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(70f, 1.08f, 0.94f, float.NaN), Ease = Ease.OutQuad, Track = false, If = mm => Ok, Every = (mm, k) => OnHead(mm) });
        }

        private void Wrench(ChoreoMove m)
        {
            if (!Ok || !m.Touch(_v))
            {
                return;
            }
            Head(out Vector2 g, out float h);
            Vector2 at = g + new Vector2(0f, h);
            HitResult r = m.Deliver(_v, new Hit(Blow.Hook, DragonfallTech.Wrench(m.Level)) { Lethal = false, Hurt = Hurt.Stagger, Power = 1.8f, Finisher = true },
                ChoreoMove.ShapeOf(Blow.Hook), at, new Vector2(m.Fwd, -0.4f));
            _wrenched = true;
            Choreo.Sound("event:/SFX/HIT/HitStone", _v.current_tile);
            if (m.Vis)
            {
                Kit.ImpactFrame(at, m.Px, 0.5f * Mathf.Sqrt(m.Kk), m.Hue.Pal, r.Crit || m.Level >= 3, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                Fx.FocusLines(at, m.Px, Fx.Count(9), 10f * m.Kk, 18f * m.Kk, 0.12f);
                Kit.Sparks(at, m.Px, Fx.Count(8), 50f, 120f, m.Hue.Edge);
            }
            // its flight dies: it goes into a crash landing (held down once it is there)
            int force = Ruin.Force(m.Tier, m.Scale);
            WingbreakerGround.Down(_v, 2.6f + 0.8f * m.Level, m.A, _landed ? 0 : force, 1.2f + 0.3f * m.Level);
        }

        /// <summary>They come down together: it rides the dragon into its crash, then the earth takes them.</summary>
        public void AddFall()
        {
            ChoreoMove m = _m;
            // riding it down until it touches the ground (a landed one: its head is driven straight in)
            Beat ride = new Beat { Tempo = Tempo.Free, Dur = 0f, Until = mm => !Ok || !_v.isFlying(), MaxDur = 1.8f, Track = false, If = mm => Ok && _wrenched && !_landed };
            ride.Every = (mm, k) =>
            {
                OnHead(mm);
                mm.Pose.Rot = Mathf.Lerp(mm.Pose.Rot, 30f, Clock.Dt * 8f);
                Wind(mm);
            };
            m.Add(ride);
            m.Do(mm => Crash(mm));
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(20f, 1.24f, 0.78f, float.NaN), Ease = Ease.OutExpo, Track = false, If = mm => Ok, Every = (mm, k) => OnHead(mm) });
        }

        private void Crash(ChoreoMove m)
        {
            if (_crashed || !_wrenched || !Same)
            {
                return;
            }
            _crashed = true;
            Vector2 at = _v.current_position;
            if (Combat.Living(_v))
            {
                HitResult r = Combat.Hit(m.A, _v, DragonfallTech.Crash(m.Level) * m.Cm, true, false, 0f, m);
                m.LastHit = r;
                Victims.Flash(_v, Color.white, 0.08f);
                if (Combat.Living(_v))
                {
                    BigGame.Topple(_v, 2.6f + 0.8f * m.Level, m.A, 0, 0f);
                }
            }
            float r0 = Mathf.Max(2f, _box.Half + 1f) * (1f + 0.15f * m.Level);
            BigGame.Thud(m, at, r0, 1.5f + 0.2f * m.Level, true, _v);
            m.AreaBlow(at, r0, 0.6f + 0.2f * m.Level, _v, Hurt.Knockdown, 1f);
            Kit.Stop(at, (0.05f + 0.02f * m.Level) * m.ImpactK);
            m.SetLink(Link.Down, _v);
        }

        /// <summary>Off its back, down beside it.</summary>
        public void AddSpringOff()
        {
            ChoreoMove m = _m;
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.32f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Apex = 0.9f,
                Spin = -360f,
                Dest = mm =>
                {
                    Vector2 c = Same ? _v.current_position : mm.Stance;
                    float side = Mathf.Abs(_hx) > 0.02f ? -Mathf.Sign(_hx) : -mm.Fwd;
                    Vector2 want = Combat.Clamp(c + new Vector2(side * (_box.Half + 1.3f * mm.Kk), -0.3f));
                    if (Combat.SafeSpot(want))
                    {
                        return want;
                    }
                    return Combat.FindLanding(want, 3f, out Vector2 q) ? q : c;
                },
                To = new Shape(-8f, 0.94f, 1.08f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Aerial
            };
            m.Add(b);
            m.Land(0f);
        }
    }
}
