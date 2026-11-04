using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TITAN TOPPLE (Hunter's Way, Master). The hunter's answer to something the size of a hill
    /// (a titan, Crabzilla): it runs at the giant's feet, plants a foot on its shin and runs
    /// straight up its body to its head (dust and scraps of hide kicked off on the way, leaning
    /// into the climb), and at the top brings both fists down on the crown like a hammer: an
    /// impact frame at the skull, the head rocks, and the giant goes over (knocked out cold, a
    /// landed dragon held down) with a great thud: a crater under it, a ring of dust racing out,
    /// everyone small near its feet knocked flat. The hunter springs off and lands beside it.
    /// II: a knee to its chest on the way up and a front flip over the crown before the hammer; a
    /// bigger thud. III: the flip is a double, the hammer a heel that cracks the skull on a black
    /// flash, and the thud tears the ground open. Damage (x the user's damage): I 3.6; II 1.2 +
    /// 3.8; III 1.4 + 4.2 + the fall 1.2. A Master meets a titan only at its last stand (class
    /// limits); a Grandmaster or a God climbs them for sport.
    /// </summary>
    internal sealed class TitanToppleTech : Technique
    {
        public override string Id => "titan_topple";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Aerial | Form.Heavy;
        public override Prey Prey => Prey.Titan | Prey.Kaiju;
        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : Scale.Heavy;
        public override float[] ManaCost => new[] { 24f, 40f, 62f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 3.6f : level == 2 ? 5f : 6.8f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 6f, Total = Total(1), Crowd = 0f, Alone = 12f, IntoGuard = 1f, Worth = 30f },
            new MeleeWant { Reach = 7f, Total = Total(2), Crowd = 0f, Alone = 14f, IntoGuard = 1f, Worth = 40f },
            new MeleeWant { Reach = 8f, Total = Total(3), Crowd = 0f, Alone = 16f, IntoGuard = 1f, Worth = 50f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            // a giant standing on the ground (one in the air is the harpoon's and the dragon-rider's)
            Plan p = BigGame.Pick(c, level, Want[Mathf.Clamp(level, 1, 3) - 1], Prey, false);
            if (p == null || c.Forced)
            {
                return p;
            }
            // the climb needs a body to climb: something at least twice its height
            Wingbreaker.Box b = BigGame.Size(p.Target);
            if (b.Top < 1.6f * Choreo.SpritePx(c.Self) * Choreo.PxOf(c.Self))
            {
                return null;
            }
            // a toppled giant crushes the small ones round its feet
            int near = c.CountNear(p.Target.current_position, b.Half + 1.5f) - 1;
            p.Score += Mathf.Max(0, near) * 8f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            TitanClimb run = new TitanClimb(m, m.V);
            int level = m.Level;
            // to its feet (a run at I, a blur at II and III), a crouch, and up its body
            if (level <= 1)
            {
                m.RunUp(run.Foot, 0.075f);
            }
            else
            {
                m.Dash(run.Foot, 0.026f);
            }
            m.Crouch(0.1f, 1.2f).OnEnter(mm => run.Measure());
            run.AddClimb(level >= 2 ? 0.55f : 0.9f, level <= 1 ? 0.34f : 0.28f);
            if (level >= 2)
            {
                // a knee to its chest on the way up
                run.AddBlow(Blow.Knee, level == 2 ? 1.2f : 1.4f, 0.55f, 0.07f, 0.06f, false);
                run.AddClimb(0.92f, 0.16f);
            }
            run.AddVault(level <= 1 ? 0f : level == 2 ? -360f : -720f, level <= 1 ? 0.5f : 1.1f, level <= 1 ? 0.12f : level == 2 ? 0.24f : 0.32f);
            run.AddHammer(level);
            run.AddSpringOff(level);
            m.Recover(0.32f);
            return m.Start();
        }
    }

    /// <summary>
    /// One Titan Topple in progress: where the giant's feet, chest and crown are, and the beats of
    /// the climb, the vault, the hammer, the fall and the spring off its body.
    /// </summary>
    internal sealed class TitanClimb
    {
        private readonly ChoreoMove _m;
        private readonly Actor _v;
        private readonly long _vid;
        private Wingbreaker.Box _box;
        /// <summary>Which side of the giant it climbs (+1: its right).</summary>
        private float _side = 1f;
        private float _dust;
        private bool _thudded, _hammered;

        public TitanClimb(ChoreoMove m, Actor v)
        {
            _m = m;
            _v = v;
            _vid = v.Sid();
            _box = BigGame.Size(v);
            Vector2 d = m.Stance - v.current_position;
            _side = Mathf.Abs(d.x) > 0.05f ? Mathf.Sign(d.x) : -m.Fwd;
        }

        private bool Ok => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        /// <summary>Still the same unit (alive, or only just killed by the hammer).</summary>
        private bool Same => _v != null && _v.Sid() == _vid;

        /// <summary>Tall enough to be climbed (something no taller than a man is vaulted from the ground).</summary>
        private bool Climbable(ChoreoMove m)
        {
            return Ok && _box.Top >= 1.3f * m.Hpx * m.Px;
        }

        public void Measure()
        {
            if (Ok)
            {
                _box = BigGame.Size(_v);
            }
        }

        /// <summary>Its feet: the side of the giant it came from, at the giant's body (a safe spot near it).</summary>
        public Vector2 Foot(ChoreoMove m)
        {
            if (!Ok)
            {
                return m.Stance;
            }
            Vector2 at = Combat.Clamp(m.VG + new Vector2(_side * (_box.Half * 0.85f + 0.3f * m.Kk), -0.05f));
            if (Combat.SafeSpot(at) || Choreo.Wading(at))
            {
                return at;
            }
            return Combat.FindLanding(at, 2f, out Vector2 q) ? q : m.Stance;
        }

        /// <summary>The point on its body at 'frac' of its height (over its middle, a little towards the climber).</summary>
        private Vector2 BodyGround(float frac)
        {
            float x = _box.HeadX * Mathf.Clamp01(frac * 1.2f);
            return _m.VG + new Vector2(x + _side * _box.Half * Mathf.Lerp(0.85f, 0.1f, frac), -0.05f);
        }

        private float BodyHeight(float frac)
        {
            return Mathf.Lerp(_box.Bottom, _box.Crown, frac);
        }

        private void FaceGiant(ChoreoMove m)
        {
            Vector2 head = m.VG + new Vector2(_box.HeadX, 0f);
            float dx = head.x - m.Stance.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                m.Face(new Vector2(dx, 0f));
            }
        }

        /// <summary>Runs up its body to 'frac' of its height (to the crown at 1) over 'dur', leaning into the climb.</summary>
        public void AddClimb(float frac, float dur)
        {
            ChoreoMove m = _m;
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                To = new Shape(32f, 0.9f, 1.12f, float.NaN),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Aerial | Form.Hunt
            };
            float h0 = 0f, f0 = 0f;
            Vector2 g0 = Vector2.zero;
            // (something no taller than a man is not climbed: the vault takes it from the ground)
            b.If = Climbable;
            b.Enter = mm =>
            {
                h0 = mm.Pose.Height;
                g0 = mm.Stance;
                f0 = Mathf.Clamp01(_box.Crown > 0.01f ? h0 / _box.Crown : 0f);
                FaceGiant(mm);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            };
            b.Every = (mm, k) =>
            {
                if (!Ok)
                {
                    return;
                }
                float e = Beat.Apply(Ease.OutQuad, k);
                float f = Mathf.Lerp(f0, frac, e);
                Vector2 g = Vector2.Lerp(g0, BodyGround(f), e);
                mm.Stance = g;
                mm.Pose.Ground = g;
                mm.Pose.Height = Mathf.Max(h0, BodyHeight(f) - 0.25f * mm.Kk);
                // footfalls on its hide: a bob and a puff kicked off it
                float bob = Mathf.Abs(Mathf.Sin(mm.BeatT * 26f));
                mm.Pose.SY = 1.1f - 0.08f * bob;
                mm.Pose.SX = 0.92f + 0.06f * bob;
                _dust -= Clock.Dt;
                if (_dust <= 0f && mm.Vis)
                {
                    _dust = 0.055f;
                    Vector2 feet = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                    ChoreoFx.HeelDust(feet, new Vector2(-mm.Fwd * 0.4f, -1f), mm.Px, mm.A.current_tile, mm.Kk);
                    if (Random.value < 0.4f)
                    {
                        Kit.Debris(feet, mm.Px, mm.A.current_tile, 1, mm.Kk * 0.6f, 0f);
                    }
                }
                Combat.Pin(_v, 0.4f);
            };
            m.Add(b);
        }

        /// <summary>A blow on its body where it climbs (the knee to the chest): no step, no approach, the giant rocks.</summary>
        public void AddBlow(Blow blow, float mult, float frac, float wind, float act, bool lethal)
        {
            ChoreoMove m = _m;
            BlowShape s = ChoreoMove.ShapeOf(blow);
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = wind, To = s.Wind.With(h: float.NaN, lunge: 0f), Ease = Ease.OutQuad, Track = false, If = Climbable, Forms = Form.Hunt | Form.Aerial | s.Forms });
            Beat act0 = new Beat { Tempo = Tempo.Act, Dur = act, To = s.Hit.With(h: float.NaN, lunge: 0f), Ease = Ease.OutExpo, Track = false, If = Climbable, Forms = Form.Hunt | Form.Aerial | s.Forms };
            act0.On(0.45f, mm =>
            {
                if (!Ok || !mm.Touch(_v))
                {
                    return;
                }
                Vector2 at = mm.BodyPos() + mm.Dir * 3f * mm.Px;
                if (mm.Vis)
                {
                    ChoreoFx.Smear(mm.Root(s.Limb), at, mm.Px, 0.08f, 2, 2, mm.Hue.Core, mm.Hue.Edge);
                }
                mm.Deliver(_v, new Hit(blow, mult) { Lethal = lethal, Power = 1.2f, Hurt = Hurt.Stagger }, s, at, mm.Dir);
            });
            m.Add(act0);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.05f, To = new Shape(20f, 1f, 1.04f, float.NaN), Ease = Ease.OutQuad, Track = false, If = Climbable });
        }

        /// <summary>The vault: off its shoulders, up over the crown (turning 'spin' degrees), to 'over' x size above it.</summary>
        public void AddVault(float spin, float over, float dur)
        {
            ChoreoMove m = _m;
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = dur,
                Spin = spin,
                To = new Shape(-18f, 0.86f, 1.18f, float.NaN),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.025f,
                Forms = Form.Aerial | Form.Hunt | Form.Overhead
            };
            float h0 = 0f;
            Vector2 g0 = Vector2.zero;
            b.If = mm => Ok;
            b.Enter = mm =>
            {
                h0 = mm.Pose.Height;
                g0 = mm.Stance;
                if (mm.Vis)
                {
                    Vector2 feet = mm.Pose.Ground + new Vector2(0f, mm.Pose.Height);
                    Fx.Ring(feet, mm.Px, 0.2f, 2f, 8f * mm.Kk, 1, 1, FxLayer.Top, 4, Art.Ground, 2.2f, Fx.WithA(mm.Hue.Core, 0.9f), Fx.WithA(mm.Hue.Edge, 0f));
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.8f));
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            };
            b.Every = (mm, k) =>
            {
                if (!Ok)
                {
                    return;
                }
                float e = Beat.Apply(Ease.OutCubic, k);
                Vector2 crown = mm.VG + new Vector2(_box.HeadX, -0.05f);
                Vector2 g = Vector2.Lerp(g0, crown, Beat.Apply(Ease.InOutSine, k));
                mm.Stance = g;
                mm.Pose.Ground = g;
                mm.Pose.Height = Mathf.Lerp(h0, _box.Crown + over * mm.Kk, e);
                Combat.Pin(_v, 0.4f);
            };
            m.Add(b);
        }

        /// <summary>
        /// The hammer: both fists (III: a heel) raised high over the crown, and brought down on it: an
        /// impact frame at the skull, the head rocks, the giant goes over with a great thud.
        /// </summary>
        public void AddHammer(int level)
        {
            ChoreoMove m = _m;
            Blow blow = level >= 3 ? Blow.HeelDrop : Blow.AxeKick;
            BlowShape s = ChoreoMove.ShapeOf(blow);
            float mult = level <= 1 ? 3.6f : level == 2 ? 3.8f : 4.2f;
            // raised: a held, trembling peak (the anticipation of the big one)
            Beat wind = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = level <= 1 ? 0.14f : 0.12f,
                To = new Shape(-26f, 0.86f, 1.2f, float.NaN),
                Ease = Ease.OutQuad,
                Track = false,
                Tremble = true,
                If = mm => Ok,
                Forms = Form.Hunt | Form.Overhead | Form.Heavy
            };
            wind.Enter = mm =>
            {
                FaceGiant(mm);
                if (mm.Vis && mm.Level >= 2)
                {
                    Fx.Glint(mm.HeadPos() + new Vector2(0f, 2f * mm.Px), mm.Px, 0.18f, mm.Hue.Core, 1f);
                }
            };
            m.Add(wind);
            Beat act = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.1f,
                To = new Shape(34f, 1.16f, 0.84f, float.NaN),
                Ease = Ease.OutExpo,
                Track = false,
                If = mm => Ok,
                Forms = Form.Hunt | Form.Overhead | Form.Heavy
            };
            float h0 = 0f;
            act.Enter = mm =>
            {
                h0 = mm.Pose.Height;
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(11f * Mathf.Lerp(1f, mm.Kk, 0.6f) * (mm.Level >= 3 ? 1.2f : 1f)), 6, 40);
                    Kit.Swish(mm, Art.Arc(r, 1f, 100f, -80f, mm.Level >= 3 ? 4 : 3), 0.16f, new Vector2(2f * mm.Fwd, 3f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                }
            };
            act.Every = (mm, k) =>
            {
                // it drops onto the crown with the blow
                mm.Pose.Height = Mathf.Lerp(h0, _box.Crown + 0.1f * mm.Kk, Beat.Apply(Ease.InQuad, k));
            };
            act.On(0.55f, mm =>
            {
                if (!Ok || !mm.Touch(_v))
                {
                    return;
                }
                Vector2 skull = mm.VG + new Vector2(_box.HeadX, _box.Crown);
                // (III's hammer leaves it alive for the fall: its own weight on the ground is the last blow)
                HitResult r = mm.Deliver(_v, new Hit(blow, mult) { Lethal = mm.Level >= 3 ? false : (bool?)null, Power = 1.6f, Finisher = true, Hurt = Hurt.Stagger }, s, skull, Vector2.down);
                _hammered = true;
                if (mm.Vis)
                {
                    Fx.Ring(skull, mm.Px, 0.26f, 3f, 18f * mm.Kk, 2, 1, FxLayer.Top, 6, 0.55f, 2.5f, Fx.WithA(Color.white, 0.95f), Fx.WithA(mm.Hue.Edge, 0.6f), Fx.WithA(mm.Hue.Rim, 0f));
                    Kit.Sparks(skull, mm.Px, Fx.Count(8), 50f, 120f, mm.Hue.Edge);
                    Fx.FocusLines(skull, mm.Px, Fx.Count(8), 10f * mm.Kk, 18f * mm.Kk, 0.12f);
                    if (r.Crit || mm.Level >= 3)
                    {
                        Kit.Flash(skull, mm.Px, Mathf.RoundToInt(6f * mm.Kk), 0.08f, Fx.WithA(Color.white, 0.95f), mm.TopLayer);
                    }
                }
            });
            m.Add(act);
            // the follow-through: crouched on its skull as it goes over
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(18f, 1.22f, 0.8f, float.NaN), Ease = Ease.OutExpo, Track = false, If = mm => Ok });
            // over it goes: the giant knocked out cold (a landed dragon held down), the great thud
            m.Do(mm => Fall(mm));
        }

        /// <summary>The giant goes over: knocked out, and the ground takes its weight.</summary>
        private void Fall(ChoreoMove m)
        {
            if (_thudded || !_hammered || !Same)
            {
                return;
            }
            _thudded = true;
            int level = m.Level;
            float down = level <= 1 ? 1.8f : level == 2 ? 2.6f : 3.6f;
            BigGame.Topple(_v, down, m.A, Ruin.Force(m.Tier, m.Scale), 1.2f + 0.3f * level);
            Vector2 at = m.VG;
            float r = (_box.Half + 1.2f) * (level <= 1 ? 1f : level == 2 ? 1.25f : 1.5f);
            // the fall: on III its own weight on the ground hurts it once more (the last blow: it may kill)
            if (level >= 3 && Combat.Living(_v))
            {
                HitResult hr = Combat.Hit(m.A, _v, 1.2f * m.Cm, true, false, 0f, m);
                m.LastHit = hr;
            }
            BigGame.Thud(m, at, r, 1.3f + 0.15f * level, level >= 2, _v);
            m.AreaBlow(at, r, 0.5f + 0.15f * level, _v, Hurt.Knockdown, 0.9f);
            m.SetLink(Link.Down, _v);
        }

        /// <summary>It springs off the falling giant, away from it, and lands beside it.</summary>
        public void AddSpringOff(int level)
        {
            ChoreoMove m = _m;
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.3f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Apex = 0.8f,
                Spin = level >= 2 ? 360f : 0f,
                Dest = mm =>
                {
                    Vector2 want = Combat.Clamp(mm.VG + new Vector2(_side * (_box.Half + 1.4f * mm.Kk), 0f));
                    if (Combat.SafeSpot(want))
                    {
                        return want;
                    }
                    return Combat.FindLanding(want, 3f, out Vector2 q) ? q : mm.Stance;
                },
                To = new Shape(-10f, 0.94f, 1.08f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Ghosts = 0.035f,
                Forms = Form.Aerial
            };
            b.Enter = mm =>
            {
                float dx = mm.Stance.x - mm.VG.x;
                if (Mathf.Abs(dx) > 0.05f)
                {
                    mm.Face(new Vector2(-dx, 0f));
                }
            };
            m.Add(b);
            m.Land(0f);
        }
    }

    /// <summary>
    /// BIG GAME. What the Hunter's Way knows about hunting things far bigger than a man (dragons,
    /// titans, Crabzilla) and about creatures in general, shared by its upper techniques: picking
    /// a prey in reach, measuring a giant (where its back, head and crown are drawn), bringing one
    /// down (a dragon sent into its landing and held there, a giant knocked out cold, a kaiju
    /// rocked), throwing what can be thrown however heavy, and the great thud of a giant meeting
    /// the ground.
    /// </summary>
    internal static class BigGame
    {
        /// <summary>The creatures of the hunt: everything but people, boats and buildings.</summary>
        public const Prey Monsters = Prey.Beast | Prey.Brute | Prey.Undead | Prey.Fiend | Prey.Flyer | Prey.Dragon | Prey.Titan
            | Prey.Kaiju | Prey.Swarm | Prey.Construct;

        public static bool IsDragon(Actor v)
        {
            try
            {
                return v != null && v.getActorComponent<Dragon>() != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCrab(Actor v)
        {
            try
            {
                return v != null && v.getActorComponent<Crabzilla>() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A monster in the hunter's sense: not a person (a mage that is a person stays a person).</summary>
        public static bool Monster(Actor v)
        {
            return v != null && Preys.Monster(v) && (Preys.Of(v) & Monsters) != 0;
        }

        /// <summary>Up in the air: flying, or thrown well off the ground.</summary>
        public static bool Airborne(Actor v)
        {
            return v != null && (v.isFlying() || Combat.HeightOf(v) > 0.6f);
        }

        /// <summary>
        /// How a giant is drawn (Wingbreaker.Measure: feet, middle, top, half width, crown and where its
        /// head is), Crabzilla measured from its shell (it is far bigger than its sprite says).
        /// </summary>
        public static Wingbreaker.Box Size(Actor v)
        {
            if (IsCrab(v))
            {
                // (its shell is measured from its body sprites: at most twice a second per crab)
                long id = v.Sid();
                float now = Time.unscaledTime;
                if (id == _crabId && now - _crabT < 0.5f)
                {
                    return _crabBox;
                }
                Wingbreaker.Box b;
                Vector2 c = v.current_position + new Vector2(0f, 7f);
                Vector2 ext = new Vector2(6.5f, 6.4f);
                try
                {
                    new CrabRig(v).Shell(out c, out ext);
                }
                catch
                {
                    // (the guess stands)
                }
                float root = v.current_position.y;
                b.Bottom = 0f;
                b.Center = Mathf.Max(1f, c.y - root);
                b.Top = Mathf.Max(2f, c.y + ext.y - root);
                b.Half = Mathf.Max(1f, ext.x * 0.8f);
                b.Crown = b.Top * 0.9f;
                b.HeadX = 0f;
                _crabId = id;
                _crabT = now;
                _crabBox = b;
                return b;
            }
            if (IsDragon(v))
            {
                return DragonBox(v);
            }
            return Wingbreaker.Measure(v);
        }

        /// <summary>
        /// A dragon's shape, followed through its landing and its take-off: the game draws those as a
        /// run of frames from its flying pose down to its landed one (Wingbreaker.Measure knows only
        /// the two ends), so a rider or a chain on it would jump by tiles; the shape is blended along
        /// the frames instead.
        /// </summary>
        private static Wingbreaker.Box DragonBox(Actor v)
        {
            Wingbreaker.Box b = Wingbreaker.Measure(v);
            try
            {
                Dragon d = v.getActorComponent<Dragon>();
                SpriteAnimation sa = d != null ? d.spriteAnimation : null;
                if (sa == null || sa.frames == null || sa.frames.Length < 2)
                {
                    return b;
                }
                float k = Mathf.Clamp01(sa.currentFrameIndex / (float)(sa.frames.Length - 1));
                if (d.state == DragonState.Landing)
                {
                    // (0 still flying .. 1 down)
                }
                else if (d.state == DragonState.Up)
                {
                    k = 1f - k;
                }
                else
                {
                    return b;
                }
                float s = Mathf.Max(0.02f, v.actor_scale);
                float lift = Mathf.Max(0f, v.position_height);
                float face = v.flip ? 1f : -1f;
                b.Bottom = lift + Mathf.Lerp(13f, 0f, k) * s;
                b.Center = lift + Mathf.Lerp(27f, 11f, k) * s;
                b.Top = lift + Mathf.Lerp(41f, 22f, k) * s;
                b.Half = 10f * s;
                b.Crown = lift + Mathf.Lerp(33f, 17f, k) * s;
                b.HeadX = face * Mathf.Lerp(6f, 4.5f, k) * s;
            }
            catch
            {
                // (its animation mid-rebuild: the two ends will do)
            }
            return b;
        }

        private static long _crabId = -1;
        private static float _crabT;
        private static Wingbreaker.Box _crabBox;

        /// <summary>Its height in world units (top of its drawing above its feet).</summary>
        public static float Height(Actor v)
        {
            return v == null ? 1f : Size(v).Top;
        }

        // --------------------------------------------------------------- picking --

        /// <summary>
        /// The best prey for a hunter's single-target technique right now: its focus first, then the
        /// nearest few in reach, each checked like Choreo.PickMelee does (fair, allowed by the class
        /// limits, a clear path, not wasted on a foe one ordinary blow ends) and made for 'prey' (a
        /// forced plan takes anything, past the class limits). 'air': the target may be in the air (a
        /// flier); 'minRange': not nearer than this (a throw, a leap). Null if there is none.
        /// </summary>
        public static Plan Pick(Ctx c, int level, MeleeWant w, Prey prey, bool air, float minRange = 0f, bool monsters = false)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, w, prey, air, minRange, monsters, ref best, ref bestScore);
            float k = Choreo.KOf(c.Self);
            float reach = w.Reach * k + 1f;
            for (int i = 0; i < c.Enemies.Count && i < 10; i++)
            {
                if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(reach, 10f) : reach + 3f))
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], w, prey, air, minRange, monsters, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        /// <summary>Fair game for a hunter (Choreo.Fair; a forced plan past the class limits at half damage).</summary>
        public static bool Fair(Ctx c, Actor v, out float cm)
        {
            if (Choreo.Fair(c.Self, v, out cm, c.Forced))
            {
                return true;
            }
            cm = 0.5f;
            return c.Forced && v != null && Combat.Living(v) && Combat.IsFoe(c.Self, v) && Choreo.Hittable(v) && !MeteorSlam.IsDoomed(v);
        }

        private static void Consider(Ctx c, Actor v, MeleeWant w, Prey prey, bool air, float minRange, bool monsters, ref Actor best, ref float bestScore)
        {
            if (v == null || v == best || !Fair(c, v, out float cm))
            {
                return;
            }
            if (!c.Forced)
            {
                if (prey != Prey.None && (Preys.Of(v) & prey) == 0)
                {
                    return;
                }
                if (monsters && !Monster(v))
                {
                    return;
                }
            }
            bool airborne = Airborne(v);
            if (airborne && !air)
            {
                return;
            }
            if (!airborne && v.isInLiquid() && !Choreo.Wading(v.current_position))
            {
                return;
            }
            float k = Choreo.KOf(c.Self);
            bool linkTarget = c.Linked && v == c.LinkTarget;
            float reach = w.Reach * k + v.stats["size"] * 0.5f + (linkTarget ? 0.8f : 0f);
            if (c.Forced)
            {
                reach = Mathf.Max(reach, 10f);
            }
            float d = c.Dist(v);
            if (d > reach || d < minRange && !c.Forced)
            {
                return;
            }
            if (d > 1.2f && !airborne && !Combat.ClearPath(c.Pos, v.current_position))
            {
                return;
            }
            float hp = v.getHealth();
            if (!c.Forced && w.Worthy && hp <= c.Expected(v, 1.2f) * cm)
            {
                return;   // one ordinary blow ends it
            }
            float score = c.Value(v, c.Expected(v, w.Total), cm);
            if (c.Isolated(v, 2f))
            {
                score += w.Alone;
            }
            else if (w.Crowd > 0f)
            {
                int near = c.CountNear(v.current_position, 2f);
                if (near >= 3)
                {
                    score *= Mathf.Max(0.5f, 1f - w.Crowd * (near - 2));
                }
            }
            if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Fiend)
            {
                score += w.Worth * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.3f, 1.5f);
            }
            if (linkTarget)
            {
                score += 6f;
            }
            if (v == c.Focus)
            {
                score += 4f;
            }
            if (!c.Forced)
            {
                int guard = c.Guard(v);
                if (guard > 0)
                {
                    score *= w.IntoGuard;
                }
                else if (guard < 0)
                {
                    score += 10f;
                }
            }
            else
            {
                score = Mathf.Max(score, 1f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        // ---------------------------------------------------------- bringing down --

        /// <summary>
        /// Brings a giant down for 'seconds': a dragon is sent into its landing (if there is ground under
        /// it) and held down there, ploughing a trench ('force', 'width') where it touches down; a
        /// giant the game draws itself is knocked out cold (it tips over); what can't go over (the
        /// player's own Crabzilla) is rocked where it stands.
        /// </summary>
        public static void Topple(Actor v, float seconds, Actor by, int force, float width)
        {
            if (v == null || !Combat.Living(v))
            {
                return;
            }
            if (IsDragon(v))
            {
                WingbreakerGround.Down(v, seconds, by, force, width);
                return;
            }
            if (ControllableUnit.isControllingUnit(v) || IsCrab(v))
            {
                Combat.Stagger(v, Mathf.Min(seconds, 1.6f));
                return;
            }
            if (v.isFlying() && v.asset != null && !v.asset.flying)
            {
                v.setFlying(false);
            }
            Combat.Daze(v, seconds);
            v.startShake(0.35f, 0.15f, true, false);
        }

        /// <summary>
        /// Throws 'v' 'dist' tiles along 'dir', 'apex' tiles high, however heavy it is (the game's own
        /// forces: Combat.LaunchDir without its "too heavy" guard), if the game lets anything move it at
        /// all. Never off the map. False if it can't be moved (Crabzilla, a unit in a move, held).
        /// </summary>
        public static bool Hurl(Actor v, Vector2 dir, float dist, float apex)
        {
            if (v == null || !Combat.Living(v) || v.asset == null || !v.asset.can_be_moved_by_powers || v.is_immovable || v.isInMagnet()
                || v.isInsideSomething() || Moves.Busy(v) || ControllableUnit.isControllingUnit(v) || v.asset.is_boat)
            {
                return false;
            }
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = Vector2.right;
            }
            dir.Normalize();
            apex = Mathf.Max(0.3f, apex);
            dist = Mathf.Max(0f, dist);
            Vector2 p = v.current_position;
            for (int i = 0; i < 8; i++)
            {
                Vector2 land = p + dir * dist;
                if (Toolbox.inMapBorder(ref land) && land.x > 2f && land.y > 2f && land.x < MapBox.width - 3 && land.y < MapBox.height - 3)
                {
                    break;
                }
                dist *= 0.6f;
            }
            float g = Mathf.Min(Mathf.Max(0.2f, v.stats["mass"]) * SimGlobals.m.gravity, 20f);
            float vz = Mathf.Sqrt(0.6f * apex);
            float vxy = 0.25f * dist * g / apex;
            float mul = SimGlobals.m.unit_force_multiplier;
            try
            {
                v.cancelAllBeh();
            }
            catch
            {
                // (an AI mid-rebuild)
            }
            v.addForce(dir.x * vxy * mul, dir.y * vxy * mul, vz * mul, true, true);
            return v.under_forces;
        }

        // ------------------------------------------------------------------ thud --

        /// <summary>
        /// The great thud of something enormous meeting the ground at 'at' ('r' tiles: the ring and the
        /// dust; 'weight' 1 .. 2 the shake): a ring racing out, a wall of dust rolling outward, rocks,
        /// the camera shaken, and with 'dig' a crater at the move's Scale (Ruin). Effects only on
        /// screen; the ground is broken either way. 'giant': whose fall it is (its size scales the dust).
        /// </summary>
        public static void Thud(ChoreoMove m, Vector2 at, float r, float weight, bool dig, Actor giant = null)
        {
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
            float gk = giant != null ? Mathf.Clamp(Choreo.KOf(giant), 1f, 2.4f) : 1f;
            if (Kit.Visible(at, 0.6f))
            {
                float px = Mathf.Max(m.Px, 0.1f);
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.3f);
                Kit.DustRing(at, px, tile, Fx.Count(18), 70f * gk, 0.9f, gk * 1.4f);
                Kit.Debris(at, px, tile, Fx.Count(12), gk * 1.2f);
                Kit.Shockwave(at, px, 4f, Mathf.Max(14f, r / px), 0.5f, Fx.WithA(m.Hue.Edge, 0.85f), 3);
                Fx.Ring(at, px, 0.6f, 4f, r / px * 1.15f, 2, 1, FxLayer.Ground, 5, Art.Ground, 1.6f, Fx.WithA(Color.white, 0.9f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
                // the wall of dust rolling out from under the body
                int n = Fx.Count(14);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at + d * r * 0.3f, px, Random.Range(0.8f, 1.2f), FxLayer.Top, 4);
                    p?.Moving(d * r * Random.Range(0.9f, 1.3f) / 0.9f, Random.Range(3f, 8f) * px, 2.5f).Sized(1.2f * gk, 2.4f * gk)
                        .Color(Fx.WithA(dust, 0.9f), Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0f));
                }
                if (dig)
                {
                    Kit.Crater(at, px * gk, tile, Ruin.Force(m.Tier, m.Scale) >= 3, 7f, false, m.Hue.Edge);
                }
            }
            if (dig)
            {
                Ruin.Blast(m.A, at, Mathf.Max(0.8f, r * 0.45f), m.Scale, m.Pose.Ground);
            }
            Ruin.Quake(m.A, at, m.Scale, weight, Mathf.Max(m.Kk, gk));
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>How many enemies of 'a' that are creatures of 'prey' stand within 'r' of 'at' (into 'into' if given).</summary>
        public static int CountPrey(Ctx c, Vector2 at, float r, Prey prey, List<Actor> into = null, bool monsters = false)
        {
            int n = 0;
            float r2 = r * r;
            for (int i = 0; i < c.Enemies.Count; i++)
            {
                Actor v = c.Enemies[i];
                if ((v.current_position - at).sqrMagnitude > r2 || Moves.Locked(v) || !c.Allowed(v))
                {
                    continue;
                }
                if (prey != Prey.None && (Preys.Of(v) & prey) == 0 || monsters && !Monster(v))
                {
                    continue;
                }
                n++;
                into?.Add(v);
            }
            return n;
        }
    }
}
