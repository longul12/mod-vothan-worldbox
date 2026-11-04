using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BOULDER TOSS (Earth Breaker, Adept). It squats and drives its hands into the ground in front of
    /// it, and with a heave (trembling, the ground cracking round its fingers) rips out a boulder,
    /// clods and dust raining off it, leaving a hole. It hoists the rock over its head, holds it there
    /// straining a heartbeat, and hurls it in a high arc at the foe: the boulder tumbles through the
    /// air trailing dust and lands with a crash where the foe stood (it can step out from under it),
    /// knocking down whoever it lands on. Good against archers and mages: it reaches them where they
    /// stand. II rips out a bigger rock (a deeper crater, a wider blast); III tears out a huge one
    /// that shatters on impact, its shards bowling over everyone around. Range 2.5 to 8 / 9.5 / 11
    /// tiles. Damage (x the user's damage) on those it lands on: I 1.8, II 2.5, III 3.4 (+ III's
    /// shards x1 on those around).
    /// </summary>
    internal sealed class BoulderTossTech : Technique
    {
        public override string Id => "boulder_toss";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Projectile | Form.Ground | Form.Heavy;
        public override Scale Scale => Scale.Light;

        public override Scale ScaleAt(int level)
        {
            return level >= 3 ? Scale.Heavy : Scale.Light;
        }

        public override float[] ManaCost => new[] { 14f, 26f, 42f };
        public override float[] Cooldown => new[] { 7f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Thrown || link == Link.Scattered;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.8f : level == 2 ? 2.5f : 3.4f;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 9.5f : 11f;
        }

        /// <summary>The rock's size (1 a man-sized boulder).</summary>
        private static float Size(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.45f : 2.1f;
        }

        private const float MinRange = 2.5f;

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            // it needs ground to tear a rock out of
            if (!c.Forced && (c.InWater || !Combat.SafeSpot(c.Pos)))
            {
                return null;
            }
            if (c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f);
            float mult = Mult(level);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 12; i++)
            {
                Actor v = c.Enemies[i];
                float d = c.EnemyDist[i];
                if (d > (c.Forced ? Mathf.Max(range, 6f) : range))
                {
                    break;
                }
                if (c.ForceTarget != null && v != c.ForceTarget)
                {
                    continue;
                }
                if (d < MinRange && !c.Forced || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || v.isInLiquid()
                    || !c.Forced && MeteorSlam.AtDeathsDoor(v))
                {
                    continue;
                }
                bool linkTarget = c.Linked && v == c.LinkTarget;
                if (!c.Forced && !linkTarget && v.getHealth() <= c.Expected(v, 1.2f) * cm)
                {
                    continue;   // not a rock for what an ordinary blow ends
                }
                float score = c.Value(v, c.Expected(v, mult), cm);
                FoeState s = c.State(v);
                if (s.Ranged)
                {
                    score += 15f;   // an archer or a mage: the rock reaches it where it stands
                }
                if (s.Down || s.Stunned)
                {
                    score += 8f;    // it won't step out from under it
                }
                // others standing round where it lands are caught too
                int near = c.CountNear(v.current_position, 1.2f);
                score += Mathf.Max(0, near - 1) * c.D * 0.6f;
                if (linkTarget)
                {
                    score += 10f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
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
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            // (the move is aimed at the spot, not held on the foe: a foe far away is not pinned while
            // the rock comes up; it is thrown at wherever the foe stands at the moment it lets go)
            Plan aim = new Plan { Tech = p.Tech, Level = p.Level, Point = p.Point, Dir = p.Dir, Count = p.Count, Score = p.Score, Data = p.Data };
            ChoreoMove m = ChoreoMove.Make(a, aim, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Rock rock = new Rock { Size = Size(level), Target = p.Target, TId = p.Target != null ? p.Target.Sid() : -1 };
            m.Face(p.Dir);
            // it squats and drives its hands into the ground
            Beat dig = new Beat { Tempo = Tempo.Wind, Dur = 0.14f, To = new Shape(30f, 1.22f, 0.76f, 0f, 0.08f), Ease = Ease.OutQuad, Forms = Form.Ground | Form.Heavy };
            dig.Enter = mm =>
            {
                rock.Look(mm);
                Dig(mm, rock);
            };
            m.Add(dig);
            // the heave: trembling, the ground cracking round its fingers, the rock tearing loose
            Beat rip = new Beat { Tempo = Tempo.Free, Dur = level <= 1 ? 0.16f : level == 2 ? 0.2f : 0.26f, To = new Shape(18f, 1.14f, 0.86f, 0f, 0.05f), Ease = Ease.OutQuad, Tremble = true, Forms = Form.Ground | Form.Heavy };
            rip.Every = (mm, k) => Rip(mm, rock, k);
            m.Add(rip);
            // up over its head
            Beat hoist = new Beat { Tempo = Tempo.Act, Dur = 0.16f, To = new Shape(-12f, 0.9f, 1.14f, 0f, -0.04f), Ease = Ease.OutBack, Forms = Form.Projectile | Form.Heavy };
            hoist.Every = (mm, k) => rock.Lift = 0.35f + 0.65f * Beat.Apply(Ease.OutCubic, k);
            hoist.Enter = mm =>
            {
                rock.Look(mm);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.06f);
            };
            m.Add(hoist);
            // held up there, straining: the bigger the rock, the longer
            m.Hold(level <= 1 ? 0.08f : level == 2 ? 0.12f : 0.18f, true, Tempo.Wind);
            m.Last.Forms = Form.Projectile | Form.Heavy;
            m.Last.Every = (mm, k) => Strain(mm, rock, k);
            // the hurl
            Beat hurl = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(32f, 1.2f, 0.86f, 0f, 0.14f), Ease = Ease.OutExpo, Forms = Form.Projectile | Form.Heavy };
            hurl.Enter = mm =>
            {
                rock.Look(mm);
                if (mm.Vis)
                {
                    Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -12f, 0.08f, Fx.WithA(mm.Hue.Ghost, 0.55f), mm.Hue.GhostEnd);
                }
            };
            hurl.On(0.35f, mm => Hurl(mm, rock));
            m.Add(hurl);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.14f, To = new Shape(18f, 1.08f, 0.94f, 0f, 0.08f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.24f);
            m.Ended2 = (mm, aborted) => rock.Drop();
            return m.Start();
        }

        /// <summary>The rock in its hands (from the ground to over its head) until it is thrown.</summary>
        private sealed class Rock
        {
            public float Size, Lift, Shake;
            public Vector2 From;
            public Particle P;
            public bool Thrown;
            public Actor Target;
            public long TId;

            /// <summary>The foe it is meant for, if it is still there to throw at.</summary>
            public Actor Foe(ChoreoMove m)
            {
                return Target != null && Target.Sid() == TId && Combat.Living(Target) && !Target.isInMagnet() && !Target.isInsideSomething()
                    && Combat.IsFoe(m.A, Target) ? Target : null;
            }

            /// <summary>Turns to the foe (it may have moved).</summary>
            public void Look(ChoreoMove m)
            {
                Actor v = Foe(m);
                if (v != null)
                {
                    m.Face(v.current_position - m.Stance);
                }
            }

            /// <summary>Its radius in the performer's pixels.</summary>
            public int R(ChoreoMove m)
            {
                return Mathf.Clamp(Mathf.RoundToInt(4f * Size * Mathf.Lerp(1f, m.Kk, 0.6f)), 2, 40);
            }

            /// <summary>Where it is drawn: rising out of the ground in front, then up over the head.</summary>
            public Vector2 At(ChoreoMove m)
            {
                float rr = R(m) * m.Px;
                Vector2 low = From + new Vector2(0f, rr * Mathf.Lerp(-0.6f, 0.9f, Mathf.Clamp01(Lift / 0.35f)));
                Vector2 high = m.HeadPos() + new Vector2(m.Fwd * -0.5f * m.Px, rr * 0.85f);
                float u = Mathf.Clamp01((Lift - 0.35f) / 0.65f);
                Vector2 p = Vector2.Lerp(low, high, Beat.Apply(Ease.InOutSine, u));
                if (Shake > 0f)
                {
                    p += Choreo.Tremble(Time.time, Shake) * m.Px;
                }
                return p;
            }

            public void Drop()
            {
                if (P != null && P.Alive)
                {
                    P.Kill();
                }
                P = null;
            }
        }

        /// <summary>The hands go into the ground in front of it: cracks open round them, dust.</summary>
        private static void Dig(ChoreoMove m, Rock rock)
        {
            rock.From = Combat.Clamp(m.Pose.Ground + m.Dir * 0.45f * m.Kk);
            if (!Combat.SafeSpot(rock.From))
            {
                rock.From = m.Pose.Ground;
            }
            WorldTile tile = Combat.TileAt(rock.From);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.06f);
            if (m.Vis)
            {
                EarthFx.Cracks(rock.From, m.Px, 3, 0.32f * m.Kk * Mathf.Sqrt(rock.Size), m.Hue, tile, 1.2f);
                Kit.DustRing(rock.From, m.Px, tile, 6, 22f, 0.4f, m.Kk);
            }
        }

        /// <summary>The heave: the rock tears up out of the ground, clods falling off it, the ground breaking open.</summary>
        private static void Rip(ChoreoMove m, Rock rock, float k)
        {
            rock.Lift = 0.35f * Beat.Apply(Ease.InQuad, k);
            rock.Shake = 0.6f;
            WorldTile tile = Combat.TileAt(rock.From);
            if (rock.P == null && k > 0.05f && m.Vis)
            {
                Particle p = Fx.Spawn(ChoreoArt.Boulder(rock.R(m), Random.Range(0, 4)), rock.At(m), m.Px, 30f, FxLayer.Over, 12);
                if (p != null)
                {
                    p.Held = true;
                    p.Color(Color.Lerp(Fx.SoilColor(tile), Color.white, 0.15f));
                    p.Rot = Random.Range(-20f, 20f);
                    m.Glue(p);
                    ChoreoMove mm = m;
                    p.Step = (q, dt) => q.Pos = rock.At(mm);
                    rock.P = p;
                    // the hole it leaves
                    Kit.Crater(rock.From, m.Px * Mathf.Lerp(1f, m.Kk, 0.6f), tile, false, 6f, true, null);
                    Kit.Debris(rock.From, m.Px, tile, Mathf.RoundToInt(5 * rock.Size), m.Kk);
                    Kit.DustRing(rock.From, m.Px, tile, 8, 30f, 0.5f, m.Kk);
                    Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.08f);
                }
            }
            if (m.Vis && rock.P != null && Random.value < 0.25f)
            {
                Kit.Debris(rock.From, m.Px, tile, 1, m.Kk * 0.6f, rock.Lift * 1.5f * m.Kk);
            }
            if (k >= 0.99f)
            {
                Ruin.Blast(m.A, rock.From, 0.32f * rock.Size, Scale.Light, m.Pose.Ground, false);
                Ruin.Quake(m.A, rock.From, Scale.Light, 0.6f * rock.Size, m.Kk);
            }
        }

        /// <summary>Held over its head: it trembles, its feet sink a little.</summary>
        private static void Strain(ChoreoMove m, Rock rock, float k)
        {
            rock.Lift = 1f;
            rock.Shake = 0.5f + 0.3f * rock.Size;
            if (m.Vis && rock.Size >= 1.4f && Random.value < 0.15f)
            {
                ChoreoFx.HeelDust(m.Pose.Ground, -m.Dir, m.Px, m.A.current_tile, m.Kk * 0.6f);
            }
        }

        /// <summary>The rock leaves its hands in a high arc at where the foe stands.</summary>
        private static void Hurl(ChoreoMove m, Rock rock)
        {
            if (rock.Thrown)
            {
                return;
            }
            rock.Thrown = true;
            int level = m.Level;
            Vector2 from = rock.P != null ? rock.P.Pos : m.HeadPos() + new Vector2(0f, rock.R(m) * m.Px);
            if (rock.P != null)
            {
                m.Unglue(rock.P);
                rock.P = null;
            }
            Actor v = rock.Foe(m);
            Vector2 goal = v != null ? (Vector2)v.current_position : m.Point;
            float d = Vector2.Distance(m.Stance, goal);
            ShotSpec spec = new ShotSpec
            {
                Kind = ShotKind.Boulder,
                Flight = Flight.Lob,
                Speed = Mathf.Clamp(d * 1.6f, 9f, 17f),
                Apex = Mathf.Clamp(1.2f + d * 0.22f, 1.6f, 3.4f) * Mathf.Lerp(1f, m.Kk, 0.5f),
                Size = rock.Size,
                Mult = Mult(level),
                Hurt = Hurt.Knockdown,
                Push = 0.7f,
                Crater = level <= 1 ? 0.7f : level == 2 ? 1f : 1.4f,
                Area = (level <= 1 ? 0.9f : level == 2 ? 1.1f : 1.3f) * Mathf.Lerp(1f, m.Kk, 0.5f),
                AreaMult = 1f,
                Element = Element.Stone
            };
            Shot s = Shots.Fire(m.A, m, from, goal - from, v, goal, spec, m.Hue.As(Element.Stone));
            if (s != null)
            {
                Areas.Add(new Landing(m, s, v, level));
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                Kit.SpeedLines(from, goal - from, m.Px, 3, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.8f));
                Kit.Debris(m.Pose.Ground, m.Px, m.A.current_tile, 2, m.Kk * 0.5f, 1.4f * m.Kk);
            }
        }

        /// <summary>
        /// Watches the rock in flight: where it comes down the ground shakes, and (II, III) it breaks
        /// apart, its shards bowling over everyone round the landing (enemies only; once each).
        /// </summary>
        private sealed class Landing : Hazard
        {
            private readonly Shot _s;
            private readonly Actor _v;
            private readonly long _vid;
            private readonly int _level;
            private Vector2 _goal;

            /// <summary>How far the rock's fall bends after a foe stepping away (tiles a second): a walk, not a run.</summary>
            private const float Drift = 2.4f;

            public Landing(ChoreoMove m, Shot s, Actor v, int level) : base(m)
            {
                _s = s;
                _v = v;
                _vid = v != null ? v.Sid() : -1;
                _goal = s.Goal;
                _level = level;
                Scale = level >= 3 ? Scale.Heavy : Scale.Light;
            }

            public override bool Tick(float dt)
            {
                Age += dt;
                if (Age > 6f)
                {
                    return false;
                }
                if (Flying())
                {
                    // (it was thrown at where the foe stood: a foe that walks on is followed a little)
                    if (_v != null && _v.Sid() == _vid && Combat.Living(_v) && Combat.Grounded(_v))
                    {
                        _s.Goal = Vector2.MoveTowards(_s.Goal, _v.current_position, Drift * dt);
                    }
                    _goal = _s.Goal;
                    return true;
                }
                // it came down where it was thrown (not reflected, not cut): the landing's aftermath
                if ((_s.Pos - _goal).sqrMagnitude > 0.04f || !OwnerOk)
                {
                    return false;
                }
                WorldTile tile = Combat.TileAt(_goal);
                bool vis = Kit.Visible(_goal, 0.4f);
                if (Dev.On) Dev.Trace("boulder lands " + (_v != null && _v.Sid() == _vid && _v.isAlive() ? Vector2.Distance(_v.current_position, _goal).ToString("F2") + " tiles from its foe" : "(foe gone)"));
                if (vis)
                {
                    EarthFx.Cracks(_goal, Px, _level >= 3 ? 6 : 4, (_level >= 3 ? 1.4f : 0.8f) * K, Hue, tile, 1.8f);
                    Kit.DustRing(_goal, Px, tile, _level >= 3 ? 14 : 8, _level >= 3 ? 50f : 36f, 0.55f, K);
                }
                if (_level >= 2)
                {
                    // it bursts apart
                    float r = (_level >= 3 ? 2.2f : 1.5f) * Mathf.Lerp(1f, K, 0.5f);
                    if (vis)
                    {
                        Color stone = EarthFx.Stone(tile);
                        for (int i = 0; i < (_level >= 3 ? 3 : 2); i++)
                        {
                            EarthFx.Flakes(_goal, 0.3f * K, Px, _level >= 3 ? 8 : 5, K * 1.4f, stone, Random.insideUnitCircle);
                        }
                        Kit.Shockwave(_goal, Px, 4f, r / Px, 0.3f, Fx.WithA(Hue.Edge, 0.85f), 2);
                    }
                    if (_level >= 3)
                    {
                        Choreo.Foes(A, _goal, r, Near);
                        for (int i = 0; i < Near.Count; i++)
                        {
                            Actor u = Near[i];
                            if ((u.current_position - _goal).sqrMagnitude <= 1.3f * 1.3f * K)
                            {
                                continue;   // (under the rock itself: already hit)
                            }
                            Strike(u, Mult(_level) * 0.3f, Hurt.Knockdown, _goal, 0.7f, -1f, 0.9f);
                        }
                    }
                }
                return false;
            }

            private bool Flying()
            {
                if (_s.Done)
                {
                    return false;
                }
                var all = Shots.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == _s)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
