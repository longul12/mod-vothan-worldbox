using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SNAP KICK (Tempest Leg, Novice). The fastest blow in the game: from up to a skip away it hops in on
    /// the back foot, the knee already chambered, and the front foot snaps straight into the gut and back
    /// before the eye follows it: a white streak from the hip, a hit star, and the foe folds over the foot
    /// and slides back 0.8 tiles. It is the school's interrupt: every kick that lands on a foe gathering
    /// power breaks the charge (Form.Interrupt), and the Brain throws it at a charging foe first (it
    /// answers Charge). II: a quick feeler to the thigh, then the real snap (the foe slides a tile, a ring
    /// of air pressure bursts off the foot). III: the feeler, then a blur of a skip and a snap that blows
    /// the foe 1.6 tiles back on an impact frame, a gust shoving whoever stands right behind it.
    /// Damage (x the user's damage): I 0.9; II 0.35 + 0.85; III 0.35 + 1.25 (+ x0.4 on those behind).
    /// </summary>
    internal sealed class SnapKickTech : Technique
    {
        public override string Id => "snap_kick";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Fast | Form.Interrupt;
        public override Form Answers => Form.Charge;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 5f, 8f, 12f };
        public override float[] Cooldown => new[] { 3.5f, 3.5f, 4f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float Feeler = 0.35f;

        /// <summary>The snap itself (the breaking kick) at this level.</summary>
        public static float Snap(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 0.85f : 1.25f;
        }

        public static float Total(int level)
        {
            return (level >= 2 ? Feeler : 0f) + Snap(level);
        }

        /// <summary>What readers see while it kicks: a fast straight kick to the body that breaks charges.</summary>
        internal const Form Shows = Form.Kick | Form.Fast | Form.Interrupt | Form.Straight | Form.Body;

        /// <summary>How far it skips in at a foe gathering power (tiles, x its size): further the higher the tier.</summary>
        private static float Skip(Tier t, int level)
        {
            float r = t >= Tier.God ? 4.4f : t >= Tier.Grandmaster ? 3.8f : t >= Tier.Master ? 3.3f : t >= Tier.Expert ? 2.9f : 2.6f;
            return r + (level >= 3 ? 0.4f : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.9f, Total = Total(1), Worthy = false, Crowd = 0.04f, Alone = 4f, IntoGuard = 0.8f, Worth = 5f },
            new MeleeWant { Reach = 2.2f, Total = Total(2), Worthy = false, Crowd = 0.04f, Alone = 5f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2.5f, Total = Total(3), Worthy = false, Crowd = 0.04f, Alone = 6f, IntoGuard = 0.8f, Worth = 8f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Breaker(c, level);
            if (p != null)
            {
                return p;
            }
            p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // cheap and quick, but not wasted on something the first kick alone would finish
            Combat.Allowed(c.Self, p.Target, out float cm);
            if (p.Target.getHealth() <= c.Expected(p.Target, level <= 1 ? Snap(1) : Feeler) * cm)
            {
                return null;
            }
            return p;
        }

        /// <summary>A foe winding up within a skip of it: the kick is what breaks it (scored like the mod's interrupts).</summary>
        private Plan Breaker(Ctx c, int level)
        {
            if (c.Forced)
            {
                return null;
            }
            float reach = Skip(c.Tier, level) * Choreo.KOf(c.Self);
            Actor best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= reach + 1.5f; i++)
            {
                Actor v = c.Enemies[i];
                if (!Moves.Charging(v) || !Combat.Grounded(v) || !Choreo.Fair(c.Self, v, out _))
                {
                    continue;
                }
                float d = c.EnemyDist[i] - v.stats["size"] * 0.5f;
                if (d > reach || d > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
                {
                    continue;
                }
                if (d < bd)
                {
                    bd = d;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Combat.Allowed(c.Self, best, out float bcm);
            float worth = c.D * 3f + 30f + 10f * Tiers.Power(best) + 0.5f * c.Value(best, c.Expected(best, Total(level)), bcm);
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = worth,
                Data = false
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            // was the foe gathering power when the kick went in? (then it leaves it lying: Down)
            bool[] broke = { false };
            // the skip: a hop in off the back foot, the knee chambered (III, from far: a blur)
            m.Add(TempestKicks.SkipIn(Shows, level >= 3 ? 0.028f : 0.04f, level >= 3));
            m.Do(mm => broke[0] = mm.V != null && Moves.Charging(mm.V));
            // (no feeler into a wind-up: the one decisive snap breaks it)
            if (level >= 2 && !Moves.Charging(p.Target))
            {
                m.Strike(new Hit(Blow.SnapKick, Feeler) { Hurt = Hurt.Flinch, Push = 0.12f, WindScale = 0.55f, ActScale = 0.8f, Power = 0.75f, Shows = Shows });
            }
            m.Strike(new Hit(Blow.SnapKick, Snap(level))
            {
                Hurt = level >= 3 ? Hurt.Reel : Hurt.Fold,
                Push = level <= 1 ? 0.8f : level == 2 ? 1f : 1.6f,
                WindScale = level >= 2 ? 0.5f : 0.7f,
                ActScale = 0.85f,
                Power = level >= 3 ? 1.35f : 1.05f,
                Finisher = level >= 3,
                Area = level >= 3 ? 1f : 0f,
                AreaMult = 0.4f,
                Shows = Shows,
                OnLand = (mm, v, r) =>
                {
                    if (mm.Level >= 2)
                    {
                        TempestKicks.Burst(mm, mm.VPoint(0.45f), mm.Dir, mm.Level >= 3 ? 1.4f : 1f);
                    }
                    if (broke[0] && Combat.Living(v) && !Moves.Charging(v))
                    {
                        mm.SetLink(Link.Down, v);   // the charge burst and threw it: it lies where it lands
                    }
                }
            });
            m.Recover(0.16f);
            return m.Start();
        }
    }

    /// <summary>
    /// TEMPEST LEG, the school's first half (snap kick .. whirlwind sweep): what its kicks share. A unit is a
    /// single sprite with no leg to swing, so a kick reads through the body and the air around it: it rises
    /// on its toes, leans away from a high kick and into a low one, turns on its heel (the sprite narrows,
    /// flips, widens), hops; the leg is its smear (a crescent swept at the height the foot travels, a streak
    /// from the hip) and the wind it leaves (rings of air pressure, gusts, steps of wind under the feet, dust
    /// whipped off the ground) in sky cyan with the tier at the rim (the move's Hue). It also lets a kick do
    /// what the engine keeps for grabs: hold a body anywhere (scissored between the legs, kicked up the air)
    /// and move one that is already flying.
    /// </summary>
    internal static class TempestKicks
    {
        // -------------------------------------------------------------- movement --

        /// <summary>
        /// A skip in: a low hop off the back foot towards the target's front (only if it is not already in
        /// reach), the knee chambered, a puff where it pushed off and where it lands; 'perTile' seconds a tile
        /// at Novice pace ('blur': afterimages and speed lines).
        /// </summary>
        public static Beat SkipIn(Form shows, float perTile = 0.04f, bool blur = false)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                PerTile = perTile,
                Route = Route.Arc,
                Apex = 0.1f,
                PathEase = Ease.OutQuad,
                Dest = m => m.StandSpot(m.Stand),
                To = new Shape(-8f, 0.93f, 1.08f, 0f),
                Ease = Ease.OutQuad,
                NeedsVictim = true,
                Ghosts = blur ? 0.025f : 0.045f,
                Lines = blur ? 0.03f : 0f,
                Forms = shows | Form.Dash,
                If = m => m.V != null && Vector2.Distance(m.Stance, m.StandSpot(m.Stand)) > 0.3f * m.Kk
            };
            b.Enter = m =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.06f);
                if (m.Vis)
                {
                    ChoreoFx.HeelDust(m.Stance, -m.Dir, m.Px, m.A.current_tile, m.Kk * 0.7f);
                }
            };
            return b;
        }

        /// <summary>
        /// A spin low on the ground ('turns' times over 'dur' seconds at Novice pace): crouched, tilted over
        /// onto its hands, the sprite narrowing and flipping with each half turn (a breakdance windmill),
        /// dust whipped round it at 'r' tiles. 'onTurn' (may be null): called every frame with the angle the
        /// sweeping leg has reached (radians, from its facing, counter-clockwise).
        /// </summary>
        public static Beat LowSpin(float turns, float dur, float r, float tilt, System.Action<ChoreoMove, float> onTurn)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = new Shape(float.NaN, 1.22f, 0.66f, 0f, 0f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = Form.Kick | Form.Low | Form.Spin
            };
            float face = 1f, dustT = 0f;
            b.Enter = m =>
            {
                face = m.Fwd;
                dustT = 0f;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            };
            b.Every = (m, k) =>
            {
                float th = Mathf.PI * 2f * turns * Beat.Apply(Ease.OutQuad, k);
                float c = Mathf.Cos(th);
                m.Pose.SX = 1.22f * Mathf.Max(0.22f, Mathf.Abs(c));
                m.Pose.FaceRight = c >= 0f ? face > 0f : face < 0f;
                m.Pose.Rot = tilt * (0.75f + 0.25f * Mathf.Abs(Mathf.Sin(th * 2f)));
                m.Pose.Nudge = new Vector2(0f, -1f) * Mathf.Clamp01(tilt / 60f);
                dustT -= Clock.Dt;
                if (dustT <= 0f && m.Vis)
                {
                    dustT = 0.055f;
                    // the leg's tip whips the dust round in a spiral
                    Vector2 rim = new Vector2(Mathf.Cos(th) * face, Mathf.Sin(th) * Art.Ground) * r * 0.85f;
                    Vector2 tangent = new Vector2(-Mathf.Sin(th) * face, Mathf.Cos(th) * Art.Ground);
                    ChoreoFx.HeelDust(m.Pose.Ground + rim, tangent, m.Px, m.A.current_tile, m.Kk);
                }
                onTurn?.Invoke(m, th);
            };
            b.Exit = m =>
            {
                m.Pose.FaceRight = m.Fwd > 0f;
                m.Pose.Nudge = Vector2.zero;
            };
            return b;
        }

        // -------------------------------------------------------------- effects --

        /// <summary>
        /// A crescent swept by the foot round the body: radius 'r' sprite pixels (grown with the unit's size and
        /// at level III), from angle 'a0' to 'a1' (degrees, counter-clockwise, as it faces right), 'ratio' tall,
        /// centred 'off' pixels from the middle of its body (forward, up), glued to it for 'life' seconds.
        /// </summary>
        public static Particle Arc(ChoreoMove m, int r, float ratio, float a0, float a1, int thick, Vector2 off, float life, int order = 6)
        {
            if (!m.Vis)
            {
                return null;
            }
            int rr = Mathf.Clamp(Mathf.RoundToInt(r * Mathf.Lerp(1f, m.Kk, 0.6f) * (m.Level >= 3 ? 1.12f : 1f)), 3, 60);
            int th = Mathf.Clamp(thick + (m.Level >= 3 ? 1 : 0), 1, 5);
            Particle p = Kit.Swish(m, Art.Arc(rr, ratio, a0, a1, th), life, new Vector2(off.x * m.Fwd, off.y), m.Fwd, m.Hue.Core, m.Hue.Edge, order);
            if (p != null && m.Cinematic)
            {
                p.Cine = true;
            }
            return p;
        }

        /// <summary>
        /// A ring of air pressure bursting off a kick at 'at': a ring seen edge-on, standing across 'dir', racing
        /// out, speed lines streaming on along the kick ('size' 1 a snap, 2 a big one).
        /// </summary>
        public static void Burst(ChoreoMove m, Vector2 at, Vector2 dir, float size)
        {
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : new Vector2(m.Fwd, 0f);
            Particle ring = Fx.Ring(at + dir * 2f * px, px, 0.16f + 0.04f * size, 1f, (2.5f + 2f * size) * m.Kk, 1, 1, m.TopLayer, 6, 2.4f, 2.4f,
                Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            if (ring != null)
            {
                ring.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                ring.Moving(dir * 26f * px * size);
                ring.Cine = m.Cinematic;
            }
            Kit.SpeedLines(at + dir * 3f * px, dir, px, Mathf.RoundToInt(2f + size * 1.5f), 2.5f * size, 0.1f + 0.03f * size, Fx.WithA(m.Hue.Edge, 0.85f), m.TopLayer);
        }

        /// <summary>A flat ring of wind under its feet in mid-air: a step on the air (and a whoosh).</summary>
        public static void Step(ChoreoMove m, float size = 1f)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.06f);
            if (!m.Vis)
            {
                return;
            }
            Vector2 feet = m.Pose.Ground + new Vector2(0f, m.Pose.Height);
            Particle p = Fx.Ring(feet, m.Px, 0.24f, 2f, 7f * m.Kk * size, 2, 1, m.TopLayer, 4, Art.Ground, 2.2f,
                Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            if (p != null)
            {
                p.Moving(new Vector2(0f, -10f * m.Px));
                p.Cine = m.Cinematic;
            }
            Kit.SpeedLines(feet, Vector2.down, m.Px, 2, 3f, 0.1f, Fx.WithA(m.Hue.Edge, 0.7f), m.TopLayer);
        }

        /// <summary>Dust whipped along the ground by a low kick, fanning out from 'at' along 'dir'.</summary>
        public static void Whip(ChoreoMove m, Vector2 at, Vector2 dir, int n)
        {
            if (!m.Vis)
            {
                return;
            }
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : new Vector2(m.Fwd, 0f);
            Vector2 side = Choreo.Perp(dir);
            WorldTile tile = Combat.TileAt(at);
            Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float s = Random.Range(-1f, 1f);
                Vector2 v = (dir * Random.Range(26f, 50f) + side * s * 16f) * m.Px * m.Kk;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, Art.Dust.Length)], at + side * s * 2f * m.Px, m.Px, Random.Range(0.25f, 0.4f), FxLayer.Top, 6);
                p?.Moving(v, Random.Range(4f, 9f) * m.Px, 5f).Sized(0.35f, 0.75f).Color(Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
        }

        // ----------------------------------------------------------------- bodies --

        /// <summary>
        /// Can 'by' take hold of 'v' wherever it is: on its feet (or in a light move of its own), in one of the
        /// engine's reactions (a launch, a juggle: a body another blow sent flying), or flying free on the
        /// game's physics after an older technique's launch? Never a flyer, a giant, a boat, one held by
        /// someone else, or the player's own unit.
        /// </summary>
        public static bool CanCatch(Actor v, Actor by)
        {
            if (v == null || by == null || v == by || !Combat.Living(v) || v.isFlying() || Weights.Heavy(v) || v.is_immovable
                || v.isInsideSomething() || v.isInMagnet())
            {
                return false;
            }
            ActorAsset s = v.asset;
            if (s == null || !s.can_be_moved_by_powers || !s.has_sprite_renderer || s.is_boat || s.has_avatar_prefab || s.ignore_generic_render)
            {
                return false;
            }
            if (ControllableUnit.isControllingUnit(v) || v.actor_scale > Mathf.Max(0.3f, by.actor_scale * 2.4f))
            {
                return false;
            }
            if (!Moves.TryGet(v, out Move m))
            {
                return true;
            }
            if (m.Interruptible || m.Finished)
            {
                return true;
            }
            Victim b = Victims.Get(v);
            return b != null && b.P == m && b.Holder == null;
        }

        /// <summary>
        /// Takes hold of 'v' for 'holder' wherever it is (CanCatch): its body is the holder's from now on (the
        /// engine's Victim, held). Null if it can't be taken.
        /// </summary>
        public static Victim Catch(Actor v, ChoreoMove holder)
        {
            if (holder == null || !CanCatch(v, holder.A))
            {
                return null;
            }
            Victim b = Victims.Get(v);
            if (b != null)
            {
                if (b.Holder != null && b.Holder != holder)
                {
                    return null;
                }
                b.HoldBy(holder);
                return b;
            }
            // (Victims.Grab only takes a body near the ground: it is made for grabs. A body flying free on the
            // game's physics is brought into its reach for the call, and its puppet given back its height)
            float h = Mathf.Max(0f, v.position_height);
            bool high = h > HundredFists.HopMax;
            if (high)
            {
                v.position_height = 0f;
            }
            b = Victims.Grab(v, holder);
            if (high)
            {
                v.position_height = h;
                if (b != null)
                {
                    b.P.Pose.Height = h;
                }
            }
            return b;
        }

        /// <summary>
        /// A blow's reaction 'im' on a body that is already high in the air: the engine's Victims.Hurt only
        /// moves bodies near the ground and merely rocks one higher up, so this plays it there (one of the
        /// engine's reactions starts over from the pose it is in; a body flying free on the game's physics is
        /// thrown again). Nothing for bodies near the ground (the engine moved them), held ones, flyers, giants.
        /// </summary>
        public static void Aloft(ChoreoMove m, Actor v, Impact im)
        {
            if (v == null || !Combat.Living(v) || v.isFlying() || Weights.Heavy(v) || Combat.HeightOf(v) <= HundredFists.HopMax)
            {
                return;
            }
            Victim b = Victims.Get(v);
            if (b != null)
            {
                if (b.Holder == null)
                {
                    b.Start(im, m.A, m);
                }
                return;
            }
            if (!Moves.Busy(v))
            {
                Choreo.Launch(v, im.Dir, Mathf.Max(0.4f, im.Push), (Random.value < 0.5f ? -1f : 1f) * 720f, im.Lift > 0f ? im.Lift : -1f);
            }
        }

        /// <summary>Is 'v' up in the air (drawn more than 'h' tiles off the ground), and not a flyer?</summary>
        public static bool Up(Actor v, float h)
        {
            return v != null && !v.isFlying() && Combat.HeightOf(v) > h;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>How many of 'a's enemies stand within 'r' tiles of 'at' (any it may fight).</summary>
        public static int FoesNear(Actor a, Vector2 at, float r)
        {
            Choreo.Foes(a, at, r, _near);
            int n = 0;
            for (int i = 0; i < _near.Count; i++)
            {
                if (Choreo.Fair(a, _near[i], out _))
                {
                    n++;
                }
            }
            return n;
        }
    }

    /// <summary>
    /// A Tempest Leg choreography that holds a body somewhere other than in its hands (scissored between its
    /// legs, kicked up the air in front of it): the script places the body itself (MoveBody: from where it
    /// is to where the beat sends it, eased over what is left of the beat). It is the engine's held body
    /// too, so however the move ends the engine lets it go: a recovery drops it, a break or an answer shakes
    /// it off; death, the god hand or a cleared world free it.
    /// </summary>
    internal class LegMove : ChoreoMove
    {
        protected LegMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        /// <summary>The body it holds and places itself (also its Held), or null.</summary>
        public Victim Body;

        private Vector2 _bg0, _bg1;
        private float _bh0, _bh1, _br0, _br1, _bk0, _bump, _wob;
        private Ease _be = Ease.OutCubic;
        private Beat _bb;
        private bool _bface;
        /// <summary>How hard the held body struggles (degrees of wriggle).</summary>
        public float Struggle = 3f;

        /// <summary>Is it holding its body right now (alive, still in its hands)?</summary>
        public bool HasBody => Body != null && !Body.Done && Body.Holder == this && Held == Body;

        /// <summary>Where the held body is drawn: its ground point and its height.</summary>
        public Vector2 BodyGround => HasBody ? Body.P.Pose.Ground : VG;

        public float BodyHeight => HasBody ? Body.P.Pose.Height : 0f;

        public float BodyRot => HasBody ? Body.P.Pose.Rot : 0f;

        /// <summary>Takes hold of 'v' wherever it is (TempestKicks.Catch); false if it can't be held.</summary>
        public bool TakeBody(Actor v)
        {
            Victim b = TempestKicks.Catch(v, this);
            if (b == null)
            {
                return false;
            }
            Body = b;
            Held = b;
            Pose p = b.P.Pose;
            _bg0 = _bg1 = p.Ground;
            _bh0 = _bh1 = p.Height;
            _br0 = _br1 = p.Rot;
            _bump = 0f;
            _bface = p.FaceRight;
            _bb = null;
            Combat.Pin(v, 3f);
            if (Dev.On) Dev.Trace("tempest " + (Tech != null ? Tech.Id : "?") + " takes " + v.asset.id + "#" + v.Sid() + " at h " + p.Height.ToString("F2"));
            return true;
        }

        /// <summary>
        /// The held body eases from where it is now to ground 'g', 'h' tiles up, turned 'rot', over what is
        /// left of the current beat ('lift': tiles it rises over the way, in an arc).
        /// </summary>
        public void MoveBody(Vector2 g, float h, float rot, Ease e = Ease.OutCubic, float lift = 0f)
        {
            if (!HasBody)
            {
                return;
            }
            Pose p = Body.P.Pose;
            _bg0 = p.Ground;
            _bh0 = p.Height;
            _br0 = p.Rot;
            _bg1 = Combat.Clamp(g);
            _bh1 = Mathf.Max(0f, h);
            _br1 = rot;
            _bump = lift;
            _be = e;
            _bb = Current;
            _bk0 = BeatK;
            if (_bb == null || _bk0 >= 0.999f)
            {
                _bb = null;
                PlaceBody();
            }
        }

        /// <summary>The held body faces this way from now on.</summary>
        public void FaceBody(bool right)
        {
            _bface = right;
        }

        /// <summary>Lets the body go: it plays 'im' from the pose it is in (a crash, a toss); the engine moves it from then on.</summary>
        public void ReleaseBody(Impact im)
        {
            Victim b = Body;
            Body = null;
            if (b == null)
            {
                return;
            }
            if (Held == b)
            {
                Held = null;
            }
            if (b.Holder == this && !b.Done)
            {
                b.LetGo(im, A, this);
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            _wob += dt;
            if (HasBody && !Finished && !Broken && !Ended)
            {
                PlaceBody();
            }
        }

        private void PlaceBody()
        {
            float k = 1f;
            if (_bb != null && Current == _bb)
            {
                k = Mathf.Clamp01((BeatK - _bk0) / Mathf.Max(0.001f, 1f - _bk0));
            }
            float u = Beat.Apply(_be, k);
            Pose p = Body.P.Pose;
            p.Ground = Vector2.Lerp(_bg0, _bg1, u);
            p.Height = Mathf.Max(0f, Mathf.LerpUnclamped(_bh0, _bh1, u) + _bump * 4f * k * (1f - k));
            float wr = Struggle * (Mathf.Sin(_wob * 23f) + 0.5f * Mathf.Sin(_wob * 41f + 1f));
            p.Rot = Mathf.LerpUnclamped(_br0, _br1, u) + wr;
            p.SX = 1f + 0.03f * Mathf.Sin(_wob * 31f);
            p.SY = 1f;
            p.Nudge = Vector2.zero;
            p.FaceRight = _bface;
            p.Hidden = false;
            p.HideShadow = p.Height > 0.08f;
            Body.P.Pose = p;
        }
    }
}
