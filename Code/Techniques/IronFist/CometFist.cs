using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// COMET FIST (Iron Fist, Master). It sinks low, the cocked fist catching fire, and is gone: a
    /// burning streak skimming the ground fist first, like a comet, ploughing a smoking trench behind
    /// it. Everyone on its line is bowled aside as it passes (thrown off the line, tumbling), and the
    /// target at the end of it takes the fist: an explosion of fire and dust, the target blasted far
    /// away, a crater, the bodies near it thrown back. It skids to a stop in its own trench.
    /// A line dash: it can be slipped (quick step, slipstream) and the fist at the end countered
    /// (cross counter, iai counter, a shoulder throw); it is a blur on the way (nothing touches it).
    /// I (Master): 6 tiles. II: 7.5, a wider trench, the blast's shockwave throws everyone near.
    /// III: 9, the trench burns, a bigger crater, the impact frame.
    /// Damage (x the user's damage): those passed I 1.1, II 1.4, III 1.8; the target I 2.4, II 3.2, III 4.2.
    /// </summary>
    internal sealed class CometFistTech : Technique
    {
        public override string Id => "comet_fist";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Line;
        public override School School => School.IronFist;
        public override Form Forms => Form.Dash | Form.Punch | Form.Line | Form.Heavy;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 22f, 38f, 62f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };

        /// <summary>A crowd blown apart, a body thrown into the others: the comet goes through them.</summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Thrown;
        }

        public static float Length(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7.5f : 9f;
        }

        public static float Width(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.7f : 0.8f;
        }

        public static float PassMult(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.4f : 1.8f;
        }

        public static float EndMult(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3.2f : 4.2f;
        }

        private static readonly List<Actor> _ray = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float len = Length(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 4f : 0f);
            float width = Width(level);
            float minD = c.Forced ? 1f : 2.2f;
            Plan best = null;
            for (int i = 0; i < c.Enemies.Count && i < 14; i++)
            {
                Actor v = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > len + v.stats["size"] * 0.5f)
                {
                    break;
                }
                if (d < minD)
                {
                    continue;
                }
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                Vector2 dir = (v.current_position - c.Pos) / d;
                // the streak runs along the ground: no wall, no water, no cliff between it and the target
                Vector2 end = v.current_position - dir * 0.6f * k;
                if (!Combat.ClearPath(c.Pos, end))
                {
                    continue;
                }
                _ray.Clear();
                c.OnRay(dir, Mathf.Max(0.4f, d - 0.5f), width, _ray);
                // forced on a foe: the streak runs to it, or through it to the last one in line behind it
                if (c.ForceTarget != null && v != c.ForceTarget && !_ray.Contains(c.ForceTarget))
                {
                    continue;
                }
                float score = c.Value(v, c.Expected(v, EndMult(level)), cm);
                int passed = 0;
                for (int j = 0; j < _ray.Count; j++)
                {
                    Actor u = _ray[j];
                    if (u == v)
                    {
                        continue;
                    }
                    c.Allowed(u, out float cu);
                    score += c.Value(u, c.Expected(u, PassMult(level)), cu);
                    passed++;
                }
                if (!c.Forced)
                {
                    // not spent on a lone foe one ordinary blow ends
                    if (passed == 0 && v.getHealth() <= c.Expected(v, 1.4f) * cm)
                    {
                        continue;
                    }
                    // a gap closer onto a shooter or a caster keeping its distance
                    if (c.State(v).Ranged && d > 3f)
                    {
                        score += 18f;
                    }
                    if (c.Linked && v == c.LinkTarget)
                    {
                        score += 8f;
                    }
                }
                else
                {
                    score = Mathf.Max(score, 1f);
                }
                if (best == null || score > best.Score)
                {
                    best = new Plan { Target = v, Point = v.current_position, Dir = dir, Count = passed + 1, Score = score, Data = c.Forced };
                }
            }
            return best;
        }

        public override bool Begin(Actor a, Plan p)
        {
            CometFistMove m = CometFistMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Comet Fist's move: the streak (its trail, the bodies it bowls aside, the trench) and the blast at its end.</summary>
    internal sealed class CometFistMove : ChoreoMove
    {
        private Vector2 _from, _end, _prev, _furrowFrom;
        private float _len, _width, _emberT, _smokeT;
        private Particle _head, _headCore;
        private readonly HashSet<long> _passed = new HashSet<long>();
        private static readonly List<Actor> _line = new List<Actor>();

        private CometFistMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static CometFistMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new CometFistMove(a, t, p);
        }

        public void Build()
        {
            int level = Level;
            _len = CometFistTech.Length(level) * Mathf.Lerp(1f, Kk, 0.5f);
            _width = CometFistTech.Width(level);
            Face(Aim);
            // anticipation: low and coiled, the fist drawn back and catching fire
            PoseTo(0.17f, new Shape(-16f, 1.16f, 0.82f, 0f, -0.12f), Ease.OutQuad, Tempo.Wind)
                .OnEnter(mm => ((CometFistMove)mm).Coil())
                .Every((mm, k) => ((CometFistMove)mm).Smolder(k));
            // the streak: fist first, skimming the ground, a burning tail behind it
            Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.04f,
                PerTile = 0.028f,
                Route = Route.Line,
                PathEase = Ease.InQuad,
                Dest = mm => ((CometFistMove)mm).StreakEnd(),
                To = new Shape(58f, 1.34f, 0.76f, 0.24f, 0f),
                Ease = Ease.OutExpo,
                Intangible = true,
                Ghosts = 0.02f,
                Lines = 0.03f,
                Track = false,
                Forms = Form.Dash | Form.Punch | Form.Line | Form.Heavy
            });
            OnEnter(mm => ((CometFistMove)mm).Launch());
            Every((mm, k) => ((CometFistMove)mm).Streak(k));
            // the fist arrives
            Do(mm => ((CometFistMove)mm).Arrive());
            Hold(0.07f, true, Tempo.Impact);
            // it skids on in its own trench, squashed, and straightens
            Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.12f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm => ((CometFistMove)mm).SkidTo(),
                To = new Shape(14f, 1.2f, 0.84f, 0f, 0f),
                Ease = Ease.OutQuad,
                Dust = 0.025f,
                Track = false
            });
            Recover(0.3f);
        }

        // ------------------------------------------------------------ wind-up --

        private void Coil()
        {
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 20f, 0.45f, Kk);
                Fx.Ring(Pose.Ground, Px, 0.22f, 9f * Kk, 2f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.8f));
            }
        }

        /// <summary>The cocked fist smoulders: embers, then flames licking up from it.</summary>
        private void Smolder(float k)
        {
            if (k > 0.6f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, 0.5f);
            }
            if (!Vis)
            {
                return;
            }
            _emberT -= Clock.Dt;
            if (_emberT > 0f)
            {
                return;
            }
            _emberT = Mathf.Lerp(0.05f, 0.02f, k);
            Vector2 fist = BodyPos() + new Vector2(-Fwd * Hpx * 0.22f, Hpx * 0.02f) * Px;
            Kit.Ember(fist + Random.insideUnitCircle * Px, Px, Random.Range(0.18f, 0.3f), new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 26f)) * Px,
                Random.value < 0.5f ? Hue.Edge : Kit.Fire2);
            if (k > 0.5f && Random.value < 0.5f)
            {
                Kit.Flash(fist, Px, Mathf.RoundToInt(2f + 2f * k), 0.05f, Fx.WithA(Hue.Core, 0.8f), FxLayer.Over, 9);
            }
        }

        // ------------------------------------------------------------- streak --

        /// <summary>The end of the streak: a fist's length in front of the target (as far as solid ground goes).</summary>
        private Vector2 StreakEnd()
        {
            Vector2 start = Stance;
            Vector2 want;
            if (V != null && VOk)
            {
                Face(VG - Stance);
                want = StandSpot(Stand);
            }
            else
            {
                want = Combat.Clamp(start + Aim * _len);
            }
            float d = Vector2.Distance(start, want);
            if (d > _len + 1f)
            {
                want = start + (want - start) / d * (_len + 1f);
            }
            for (int i = 0; i < 6; i++)
            {
                if (Combat.SafeSpot(want) && Combat.ClearPath(start, want))
                {
                    return want;
                }
                want = Vector2.Lerp(start, want, 0.7f);
            }
            return start;
        }

        private void Launch()
        {
            _from = Stance;
            _prev = _from;
            _furrowFrom = _from;
            _end = StreakEnd();
            Vector2 d = _end - _from;
            if (d.sqrMagnitude > 1e-4f)
            {
                Face(d);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile, 0.1f);
            Ruin.Quake(A, _from, Scale, 0.8f, Kk);
            if (!Vis)
            {
                return;
            }
            // the take-off: a burst of fire and dust behind it
            Kit.Flash(BodyPos(), Px, Mathf.RoundToInt(5f * Kk), 0.08f, Fx.WithA(Hue.Core, 0.95f));
            Kit.DustRing(_from, Px, A.current_tile, 12, 44f, 0.5f, Kk);
            Fx.Ring(_from, Px, 0.25f, 3f, 16f * Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Rim, 0f));
            ChoreoFx.HeelDust(_from, -Dir, Px, A.current_tile, Kk * 1.3f);
            // the comet's head: a ball of fire on the fist
            _head = Glue(Fx.Spawn(Art.Disk(Mathf.RoundToInt(3f * Kk + Level), 0.85f, 0.45f), Hand(Limb.Fist), Px, 1f, FxLayer.Over, 9));
            _headCore = Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.5f * Kk)), 0.85f, 0f), Hand(Limb.Fist), Px, 1f, FxLayer.Over, 10));
        }

        /// <summary>One frame of the streak: the tail, the bodies on the line bowled aside, the trench behind.</summary>
        private void Streak(float k)
        {
            Vector2 cur = Pose.Ground;
            if ((cur - _prev).sqrMagnitude < 1e-6f)
            {
                return;
            }
            // whoever stands on the line it just swept
            Choreo.FoesOnLine(A, _prev, cur, _width, _line);
            for (int i = 0; i < _line.Count; i++)
            {
                Bowl(_line[i], cur);
            }
            // the trench, a stretch at a time (each building near it struck once per stretch)
            if (Vector2.Distance(_furrowFrom, cur) >= 1.6f || k >= 1f)
            {
                Ruin.Furrow(A, _furrowFrom, cur, Level >= 2 ? 0.75f : 0.6f, Scale, _end, Level >= 3);
                _furrowFrom = cur;
            }
            if (Vis)
            {
                TrailFx(_prev, cur, k);
            }
            _prev = cur;
        }

        /// <summary>A body on the line: the comet bowls it aside (it may slip it: an area blow, never parried).</summary>
        private void Bowl(Actor u, Vector2 cur)
        {
            if (u == null || u == V)
            {
                return;   // (the target takes the fist at the end)
            }
            long id = u.Sid();
            if (_passed.Contains(id) || !Choreo.Fair(A, u, out float cm, Forced))
            {
                return;
            }
            _passed.Add(id);
            if (!Touch(u, true))
            {
                return;
            }
            Vector2 rel = u.current_position - cur;
            // thrown off to the side it stood on (one dead on the line: to alternate sides)
            float off = Vector2.Dot(rel, Side);
            float side = Mathf.Abs(off) > 0.12f ? Mathf.Sign(off) : (_passed.Count & 1) == 0 ? 1f : -1f;
            Vector2 away = (Side * side * 0.85f + Dir * 0.6f).normalized;
            HitResult r = Combat.Hit(A, u, CometFistTech.PassMult(Level) * cm, true, false, 0f, this);
            if (Combat.Living(u))
            {
                Impact im = Impact.Of(Hurt.Launch, away, 1.6f + 0.4f * Level, 1.3f);
                im.Lift = 0.9f + 0.25f * Level;
                im.Owed = MeteorSlam.AtDeathsDoor(u);
                Victims.Hurt(u, A, this, im);
            }
            else
            {
                Victims.Flash(u, Color.white, 0.06f);
            }
            if (Vis)
            {
                ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), away, Px, Kk, 1.2f, Hue, TopLayer);
                Kit.Sparks(Choreo.BodyAt(u, 0.5f), Px, 4, 50f, 100f, Kit.Fire2);
            }
            ChoreoFx.BlowSound(Limb.Body, 1.1f, false, u.current_tile);
            if (Dev.On) Dev.Trace("choreo " + Id + " comet bowls " + u.asset.id + "#" + id + " hp " + u.getHealth() + (r.Killed ? " killed" : ""));
        }

        private void TrailFx(Vector2 a, Vector2 b, float k)
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(b);
            Vector2 lift = new Vector2(0f, Pose.Height + Hpx * 0.3f * px);
            // the burning tail at body height, and the glowing scar it leaves on the ground
            Kit.Line(a + lift, b + lift, px, 0.16f + 0.04f * Level, 2 + (Level >= 3 ? 1 : 0), FxLayer.Behind, 6,
                Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Kit.Fire1, 0.8f), Fx.WithA(Hue.Edge, 0.5f), Fx.WithA(Hue.Edge, 0f));
            Kit.Line(a, b, px, 0.45f + 0.15f * Level, 1, FxLayer.Ground, 3,
                Fx.WithA(Kit.Fire1, 0.9f), Fx.WithA(Kit.Fire2, 0.75f), Fx.WithA(Kit.Fire3, 0.45f), Fx.WithA(Kit.Fire3, 0f));
            // the head of the comet rides the fist
            Vector2 fist = Hand(Limb.Fist);
            if (_head != null && _head.Alive)
            {
                _head.Pos = fist;
                _head.Grad = null;
                _head.R.color = Fx.WithA(Color.Lerp(Kit.Fire2, Hue.Edge, 0.5f), 0.85f);
            }
            if (_headCore != null && _headCore.Alive)
            {
                _headCore.Pos = fist;
                _headCore.Grad = null;
                _headCore.R.color = Color.Lerp(Hue.Core, Color.white, 0.5f);
            }
            // flames and smoke peeling off the tail, earth thrown up to both sides
            _smokeT -= Clock.Dt;
            if (_smokeT <= 0f)
            {
                _smokeT = 0.03f;
                Vector2 back = -Dir;
                for (int i = 0; i < 2; i++)
                {
                    Kit.Ember(fist + back * Random.Range(1f, 4f) * px + Random.insideUnitCircle * px, px, Random.Range(0.18f, 0.32f),
                        (back * Random.Range(20f, 40f) + Random.insideUnitCircle * 10f) * px, i == 0 ? Kit.Fire2 : Hue.Edge);
                }
                ChoreoFx.HeelDust(b, Side, px, tile, Kk * 0.8f);
                ChoreoFx.HeelDust(b, -Side, px, tile, Kk * 0.8f);
                if (Random.value < 0.35f)
                {
                    ChoreoFx.Smoke(b, px, 1, 2f, Kit.Smoke, 0.6f);
                }
                if (Random.value < 0.25f)
                {
                    Kit.Debris(b, px, tile, 2, Kk * 0.6f);
                }
            }
        }

        // ------------------------------------------------------------- impact --

        /// <summary>The fist arrives: the target takes it (it may still answer it), and the end of the line explodes.</summary>
        private void Arrive()
        {
            Vector2 cur = Pose.Ground;
            if (_head != null)
            {
                Unglue(_head);
                _head = null;
            }
            if (_headCore != null)
            {
                Unglue(_headCore);
                _headCore = null;
            }
            // (the last stretch of the line)
            if ((cur - _prev).sqrMagnitude > 1e-6f)
            {
                Choreo.FoesOnLine(A, _prev, cur, _width, _line);
                for (int i = 0; i < _line.Count; i++)
                {
                    Bowl(_line[i], cur);
                }
                _prev = cur;
            }
            int level = Level;
            Vector2 blastAt = cur + Dir * Stand;
            if (V != null && VOk)
            {
                float reach = Stand + 0.9f * Kk + V.stats["size"] * 0.5f + 0.3f;
                if (Vector2.Distance(cur, VG) <= reach)
                {
                    blastAt = VG;
                    if (!Touch(V))
                    {
                        return;   // slipped or turned aside at the last instant: the move ends in its whiff or recoil
                    }
                    Hit h = new Hit(Blow.Cross, CometFistTech.EndMult(level))
                    {
                        Hurt = Hurt.Launch,
                        Push = level <= 1 ? 3f : level == 2 ? 4f : 5f,
                        Lift = level <= 1 ? 1.6f : level == 2 ? 2f : 2.4f,
                        Power = 1.5f,
                        Finisher = true,
                        Lethal = true,
                        Crater = level <= 1 ? 0.9f : level == 2 ? 1.1f : 1.4f
                    };
                    Deliver(V, h, ShapeOf(Blow.Cross), ContactPoint(0.6f), Dir);
                }
            }
            else
            {
                Ruin.Blast(A, blastAt, 0.9f, Scale, Pose.Ground);
            }
            // the blast throws whoever stands round the end of the line (II and III: off their feet)
            float r = (level <= 1 ? 1.2f : level == 2 ? 1.8f : 2.4f) * Mathf.Lerp(1f, Kk, 0.5f);
            AreaBlow(blastAt, r, CometFistTech.EndMult(level) * (V != null && VOk ? 0.45f : 0.6f), V, level >= 2 ? Hurt.Knockdown : Hurt.Reel,
                level >= 2 ? 1.1f : 0.8f, 1.2f);
            Ruin.Quake(A, blastAt, Scale, 1.4f, Kk);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(blastAt));
            if (!Vis)
            {
                return;
            }
            // the comet bursts: fire and earth thrown up in a ring
            float px = Px;
            WorldTile tile = Combat.TileAt(blastAt);
            Kit.Flash(blastAt + new Vector2(0f, 0.4f * Kk), px, Mathf.RoundToInt((4f + level) * Kk), 0.07f, Fx.WithA(Color.white, 0.95f), TopLayer);
            Kit.Flash(blastAt + new Vector2(0f, 0.3f * Kk), px, Mathf.RoundToInt((6f + 1.5f * level) * Kk), 0.12f, Fx.WithA(Kit.Fire2, 0.55f), FxLayer.Over, 9);
            Fx.Ring(blastAt, px, 0.3f, 3f, (14f + 5f * level) * Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(Kit.Fire2, 0.7f), Fx.WithA(Hue.Edge, 0f));
            Kit.DustRing(blastAt, px, tile, 14, 60f, 0.6f, Kk);
            Kit.Debris(blastAt, px, tile, 6 + 3 * level, Kk);
            int n = Fx.Count(8 + 4 * level);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Kit.Ember(blastAt + d * 2f * px, px, Random.Range(0.3f, 0.6f), (d * Random.Range(30f, 70f) + Vector2.up * Random.Range(10f, 40f)) * px,
                    i % 3 == 0 ? Hue.Edge : Kit.Fire2);
            }
            ChoreoFx.Smoke(blastAt, px, 3 + level, 6f, Kit.Smoke, 0.9f);
        }

        /// <summary>The skid after the blow: a little further along the line, never past the target's crater.</summary>
        private Vector2 SkidTo()
        {
            Vector2 q = Combat.Clamp(Stance + Dir * 0.3f * Kk);
            return Combat.SafeSpot(q) && Combat.ClearPath(Stance, q) ? q : Stance;
        }

        public override void Abort()
        {
            base.Abort();
            _head = _headCore = null;
        }
    }

    /// <summary>
    /// Dev probe for the Iron Fist's Master-to-God techniques: "ifhiplans [seconds]" logs, twice a
    /// second, for every fighter that knows one of them, what each of them would score right now (raw,
    /// the Brain's tactics factor, shaped) against the best plan of any other technique it knows.
    /// </summary>
    internal static class IronFistHighDev
    {
        private static readonly string[] Ids =
        {
            "comet_fist", "iron_wall", "shatter_point", "avalanche_barrage", "earthshaker_straight", "rolling_thunder", "heavens_fist", "final_bell",
            "godhand_rush"
        };

        private static float _left, _next;
        private static Ctx _c;

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["ifhiplans"] = args =>
            {
                _left = Dev.ArgF(args, 1, 10f);
                _next = 0f;
                Dev.Log("ifhiplans: probing for " + _left.ToString("F0") + " s");
            };
            Hooks.FrameTick += dt =>
            {
                if (_left <= 0f || !Dev.On)
                {
                    return;
                }
                try
                {
                    Probe(Time.unscaledDeltaTime);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("ifhiplans", e);
                    _left = 0f;
                }
            };
        }

        private static void Probe(float dt)
        {
            _left -= dt;
            _next -= dt;
            if (_next > 0f)
            {
                return;
            }
            _next = 0.5f;
            _c ??= new Ctx();
            IReadOnlyList<Fighter> all = Fighters.All;
            for (int i = 0; i < all.Count; i++)
            {
                Fighter f = all[i];
                if (!f.Valid || f.Tier < Tier.Master || !Moves.CanPerform(f.A))
                {
                    continue;
                }
                string line = null;
                float other = 0f;
                string otherId = "-";
                _c.Build(f);
                if (_c.Enemies.Count == 0)
                {
                    continue;
                }
                foreach (Technique t in Techniques.All)
                {
                    if (t.GodAbility || t.Team)
                    {
                        continue;
                    }
                    int level = Techniques.Level(f.A, t, f.Tier);
                    if (level <= 0)
                    {
                        continue;
                    }
                    bool mine = System.Array.IndexOf(Ids, t.Id) >= 0;
                    Plan p = t.Plan(_c, level);
                    if (p == null || p.Score <= 0f)
                    {
                        if (mine)
                        {
                            line = (line ?? "") + " " + t.Id + ":-" + (f.Ready(t) ? "" : "(cd)");
                        }
                        continue;
                    }
                    p.Tech = t;
                    p.Level = level;
                    float shape = Tactics.Shape(f, _c, t, level, p);
                    float score = p.Score * shape;
                    if (mine)
                    {
                        line = (line ?? "") + " " + t.Id + ":" + p.Score.ToString("F0") + "x" + shape.ToString("F2") + "=" + score.ToString("F0")
                            + (f.Ready(t) ? "" : "(cd)");
                    }
                    else if (f.Ready(t) && score > other)
                    {
                        other = score;
                        otherId = t.Id;
                    }
                }
                if (line != null)
                {
                    Dev.Log("ifhiplans " + f.A.asset.id + "#" + f.A.Sid() + " (" + f.Tier + ", " + _c.Enemies.Count + " foes, near2 " + _c.Near2 + "):" + line
                        + " | best other " + otherId + " " + other.ToString("F0"));
                }
            }
        }
    }
}
