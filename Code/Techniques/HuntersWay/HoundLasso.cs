using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HOUND LASSO (Hunter's Way, Expert). For the beast that runs and the flyer that stays out of
    /// reach. The hunter whirls a loop of rope over its head, flings it in an arc (the rope paying
    /// out behind it, sagging) and drops it over the creature; the loop snaps tight round its middle
    /// and the hunter throws its weight back, heels ploughing the dirt: a beast is yanked off its
    /// feet and comes tumbling through the air to crash at the hunter's feet; a flyer is hauled
    /// down out of the sky (a dragon has to land, stunned). The loop is a thrown thing: it can be
    /// dodged, or cut out of the air. II: as the beast lands the hunter stamps on it and pins it.
    /// III: it seizes the beast as it crashes at its feet, swings it round over its head once on the
    /// rope like a hammer and slams it into the ground (a crater, the shock knocking down whoever is
    /// near; a long beast is held there belly-up a heartbeat, then rolls back up, dazed).
    /// Made for beasts and flyers (the Brain uses it on nothing else); what it can't move (a giant
    /// beast) it only staggers. Damage (x the user's damage): I 1; II 1 + 0.8; III 1 + 2.
    /// </summary>
    internal sealed class HoundLassoTech : Technique
    {
        public override string Id => "hound_lasso";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Grab | Form.Projectile | Form.Hunt;
        public override Prey Prey => Prey.Beast | Prey.Flyer;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 28f, 45f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Launched || link == Link.Thrown || link == Link.Scattered;
        }

        private const float Yank = 1f, Stomp = 0.8f, Slam = 2f;

        public static float Total(int level)
        {
            return Yank + (level == 2 ? Stomp : 0f) + (level >= 3 ? Slam : 0f);
        }

        public static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8f : 9f;
        }

        private static readonly System.Func<Ctx, Actor, float, int, float> ValueOf = Value;

        private static float Value(Ctx c, Actor v, float cm, int level)
        {
            bool flying = v.isFlying();
            bool movable = Victims.Movable(v, c.Self) && Choreo.Free(v);
            if (!movable && !flying && !c.Forced)
            {
                return 0f;   // a beast too big to haul in: no
            }
            if (!c.Forced && v.getHealth() <= c.Expected(v, 1f) * cm)
            {
                return 0f;
            }
            float s = c.Value(v, c.Expected(v, Total(level)), cm);
            float d = c.Dist(v);
            if (d > 3.5f)
            {
                s += 10f;   // running, or out of reach: the rope fetches it
            }
            if (flying)
            {
                s += 12f;
            }
            if (c.Linked && v == c.LinkTarget)
            {
                s += 8f;
            }
            return s;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor v = HwLoKit.PickRanged(c, this, level, 2f, Range(level), ValueOf, out float score);
            if (v == null || score <= 0f)
            {
                return null;
            }
            // (it is hauled in to the hunter's feet: solid ground there)
            if (!c.Forced && c.InWater)
            {
                return null;
            }
            Vector2 dir = v.current_position - c.Pos;
            return new Plan
            {
                Target = v,
                Point = v.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = score,
                Data = c.Forced
            };
        }

        /// <summary>The rope and its loop through the beats of one throw.</summary>
        private sealed class Lasso
        {
            public readonly HwLoRope Rope = new HwLoRope();
            public Particle Loop;
            public int Gen, LoopR;
            public Vector2 At, From;
            public float Spin;
            public bool Caught, Dropped, Slammed;

            public bool LoopOk => Loop != null && Loop.Alive && Loop.Gen == Gen;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            HwLoKit.FitStand(m);
            int level = m.Level;
            Lasso l = new Lasso();
            bool wide = false;
            // the loop whirls over its head
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = level <= 1 ? 0.32f : level == 2 ? 0.28f : 0.24f,
                To = new Shape(-10f, 0.94f, 1.1f, 0f, -0.05f),
                Ease = Ease.OutQuad,
                Forms = Form.Grab | Form.Projectile | Form.Hunt,
                NeedsVictim = true,
                Enter = mm => Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f),
                Every = (mm, k) =>
                {
                    Vector2 hand = Hand(mm);
                    float ang = Mathf.PI * 2f * 2f * Beat.Apply(Ease.InQuad, k) * (mm.Fwd > 0f ? 1f : -1f);
                    float r = 3.5f * Mathf.Lerp(1f, mm.Kk, 0.6f) * mm.Px;
                    l.At = hand + new Vector2(Mathf.Cos(ang) * r, 4f * mm.Px + Mathf.Sin(ang) * r * 0.4f);
                    DrawLoop(mm, l, 6, Mathf.Sin(ang * 0.5f));
                    l.Rope.Draw(mm, hand, l.At, 0f, HwLoKit.Hemp, 3);
                }
            });
            // the fling: the arm snaps forward and the loop sails out on its rope
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(22f, 1.2f, 0.86f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Forms = Form.Grab | Form.Projectile | Form.Hunt,
                Enter = mm =>
                {
                    l.From = l.At.sqrMagnitude > 0.01f ? l.At : Hand(mm);
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -6f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.5f), mm.Hue.GhostEnd);
                    }
                },
                Every = (mm, k) =>
                {
                    DrawLoop(mm, l, 6, 0f);
                    l.Rope.Draw(mm, Hand(mm), l.At, 0f, HwLoKit.Hemp, 3);
                }
            });
            // the loop flies in an arc to the creature, the rope paying out behind it
            float speed = level >= 3 ? 24f : 20f;
            m.Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.2f,
                Track = true,
                Forms = Form.Grab | Form.Projectile | Form.Hunt,
                Enter = mm =>
                {
                    Vector2 to = mm.VOk ? mm.VPoint(0.55f) : mm.Point;
                    mm.BeatDur = Mathf.Clamp(Vector2.Distance(l.From, to) / speed, 0.1f, 0.5f);
                },
                Every = (mm, k) =>
                {
                    Vector2 to = mm.VOk ? mm.VPoint(0.55f) : l.At;
                    float e = Beat.Apply(Ease.OutQuad, k);
                    l.At = Vector2.Lerp(l.From, to, e) + new Vector2(0f, Mathf.Sin(Mathf.PI * k) * 0.5f * mm.Kk);
                    l.Spin += Clock.Dt * 14f;
                    DrawLoop(mm, l, 6, Mathf.Sin(l.Spin));
                    l.Rope.Draw(mm, Hand(mm), l.At, 0.35f * mm.Kk * (1f - k * 0.6f), HwLoKit.Hemp, 12);
                },
                Exit = mm => Catch(mm, l)
            });
            // the hunter throws its weight back: the creature comes off its feet (or out of the sky)
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.13f,
                To = new Shape(-30f, 0.9f, 1.14f, 0f, -0.14f),
                Ease = Ease.OutExpo,
                Track = false,
                Dust = 0.04f,
                Forms = Form.Grab | Form.Throw | Form.Hunt,
                If = mm => l.Caught,
                Enter = mm => Haul(mm, l),
                Every = (mm, k) => Taut(mm, l)
            });
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.16f,
                To = new Shape(-18f, 0.94f, 1.08f, 0f, -0.1f),
                Ease = Ease.OutQuad,
                Track = false,
                If = mm => l.Caught,
                Every = (mm, k) =>
                {
                    Taut(mm, l);
                    if (mm.Vis && k < 0.5f)
                    {
                        ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk * 0.7f);
                    }
                }
            });
            if (level == 2)
            {
                // it lands at the hunter's feet: a stamp pins it there
                m.WaitUntil(mm => !mm.VOk || Landed(mm), 0.9f).OnlyIf(mm => l.Caught).Every((mm, k) => Taut(mm, l));
                m.Do(mm => Drop(mm, l));
                m.Strike(new Hit(Blow.Stomp, Stomp) { Hurt = Hurt.Pin, Daze = 1.2f, Finisher = true, Power = 1.2f, Shows = Form.Kick | Form.Low | Form.Hunt });
            }
            else if (level >= 3)
            {
                // it seizes the beast as it crashes at its feet and swings it round over its head on the rope
                m.WaitUntil(mm => !mm.VOk || Landed(mm), 0.9f).OnlyIf(mm => l.Caught).Every((mm, k) => Taut(mm, l));
                m.Do(mm =>
                {
                    if (l.Caught && mm.VOk && Choreo.Holdable(mm.V, mm.A))
                    {
                        wide = HwLoKit.Wide(mm.V);
                        HwLoKit.TakeHold(mm, 0f);
                    }
                    if (mm.Held == null)
                    {
                        Drop(mm, l);
                    }
                });
                Swing(m, l, new Grip(1.5f, 7f, -60f), new Shape(-20f, 0.92f, 1.1f, 0f, -0.04f), 0.09f);
                Swing(m, l, new Grip(-4f, 6f, -150f), new Shape(-8f, 0.94f, 1.08f, 0f, -0.06f), 0.09f);
                Swing(m, l, new Grip(-1f, 11f, -240f), new Shape(-26f, 0.9f, 1.14f, 0.08f, -0.04f), 0.08f);
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.12f,
                    To = new Shape(40f, 1.2f, 0.82f, 0f, 0.24f),
                    Ease = Ease.InQuad,
                    Grip = new Grip(4.6f, -1.4f, -450f),
                    Enter = mm =>
                    {
                        if (wide)
                        {
                            mm.Current.Grip = new Grip(4.4f, -0.6f, -180f);   // (a long beast comes down belly-up)
                        }
                    },
                    Track = false,
                    Ghosts = 0.025f,
                    Forms = Form.Grab | Form.Throw | Form.Overhead | Form.Heavy | Form.Hunt,
                    If = mm => mm.Held != null,
                    Every = (mm, k) =>
                    {
                        Taut(mm, l);
                        if (mm.Vis && k < 0.85f)
                        {
                            Kit.SpeedLines(mm.BodyPos() + mm.Dir * 4f * mm.Px, Vector2.down, mm.Px, 1, 4f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f));
                        }
                    },
                    Exit = mm =>
                    {
                        Drop(mm, l);
                        l.Slammed = true;
                        if (wide)
                        {
                            HwLoKit.SlamHeld(mm, Slam, 0.9f, 1.4f, 0.4f, true);
                        }
                        else
                        {
                            HwLoKit.ThrowDown(mm, Slam, Hurt.Pin, 1f, 2f, 0.9f, 1.4f, 0.4f, true, true);
                        }
                        HwLoKit.HuntRing(mm, mm.VG, 1.6f, 0.3f);
                    }
                });
                m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(32f, 1.22f, 0.8f, 0f, 0.24f), Ease = Ease.OutExpo, Track = false, If = mm => l.Slammed });
                m.Hold(0.1f, true, Tempo.Follow).OnlyIf(mm => mm.Held != null);
                HwLoKit.RollFree(m, 2f, true);
            }
            m.Do(mm => Drop(mm, l));
            m.Recover(0.24f);
            m.Ended2 = (mm, aborted) =>
            {
                l.Rope.Hide(mm);
                if (l.LoopOk)
                {
                    mm.Unglue(l.Loop);
                }
            };
            return m.Start();
        }

        /// <summary>The rope hand: above and in front of its shoulder.</summary>
        private static Vector2 Hand(ChoreoMove m)
        {
            return m.BodyPos() + new Vector2(m.Fwd * 2.5f, 2.5f) * m.Px;
        }

        /// <summary>The loop at l.At: an open ring of rope 'r' pixels across, turning ('turn' -1..1 narrows it).</summary>
        private static void DrawLoop(ChoreoMove m, Lasso l, int r, float turn)
        {
            if (!m.Vis || l.Dropped)
            {
                return;
            }
            r = Mathf.Clamp(Mathf.RoundToInt(r * Mathf.Lerp(1f, m.Kk, 0.6f)), 2, 24);
            if (!l.LoopOk || l.LoopR != r)
            {
                if (l.LoopOk)
                {
                    m.Unglue(l.Loop);
                }
                l.Loop = m.Glue(Fx.Spawn(Art.Ring(r, 1, 0.5f), l.At, m.Px, 30f, FxLayer.Over, 9));
                l.Gen = l.Loop != null ? l.Loop.Gen : 0;
                l.LoopR = r;
            }
            if (!l.LoopOk)
            {
                return;
            }
            l.Loop.Pos = l.At;
            l.Loop.SX = 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(turn * Mathf.PI * 0.5f));
            l.Loop.Color(Color.Lerp(HwLoKit.Hemp, m.Hue.Edge, m.Level >= 3 ? 0.35f : 0.12f));
        }

        /// <summary>The loop reaches the creature: it may answer it (a dodge, a cut); if not, it snaps tight round its middle.</summary>
        private static void Catch(ChoreoMove m, Lasso l)
        {
            if (!m.VOk)
            {
                Drop(m, l);
                HwLoKit.Whiff(m, Limb.Palm);
                m.Lose();
                return;
            }
            if (!m.Touch(m.V))
            {
                Drop(m, l);   // dodged, or cut out of the air
                return;
            }
            l.Caught = true;
            l.At = m.VPoint(0.5f);
            Choreo.Sound("event:/SFX/OTHER/Punch", m.V.current_tile);
            if (m.Vis)
            {
                Fx.HitSpark(l.At, m.Px, 0, m.Dir, 3);
                Fx.Ring(l.At, m.Px, 0.16f, 8f * m.Kk, 3f, 1, 1, FxLayer.Over, 9, 0.5f, 1.6f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            }
            Combat.Pin(m.V, 0.6f);
            if (Dev.On) Dev.Trace("hound_lasso " + m.Id + " ropes " + m.V.asset.id + "#" + m.VId);
        }

        /// <summary>The haul: a beast is yanked off its feet and flies tumbling to the hunter's feet; a flyer is hauled down out of the sky.</summary>
        private static void Haul(ChoreoMove m, Lasso l)
        {
            if (!m.VOk)
            {
                Drop(m, l);
                return;
            }
            Actor v = m.V;
            Vector2 dir = m.Stance - m.VG;
            HitResult r = Combat.Hit(m.A, v, Yank * m.Cm, m.Level <= 1, false, 0f, m);
            m.LastHit = r;
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                ChoreoFx.HeelDust(m.Pose.Ground, -m.Dir, m.Px, m.A.current_tile, m.Kk);
                ChoreoFx.Skid(m.Pose.Ground, m.Pose.Ground - m.Dir * 0.3f * m.Kk, m.Px, m.A.current_tile);
                Kit.SpeedLines(Choreo.BodyAt(v, 0.5f), dir, m.Px, 3, 3f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
            }
            if (!Combat.Living(v))
            {
                Drop(m, l);
                return;
            }
            if (Victims.Movable(v, m.A) && Choreo.Free(v))
            {
                Vector2 to = m.Stance + m.Dir * m.Stand * (m.Level >= 3 ? 1.1f : 1.3f);
                Impact im = Impact.Of(Hurt.Thrown, dir, 0f, 1.1f);
                im.To = to;
                im.Lift = m.Level >= 3 ? 1.4f : 0.9f;
                im.Daze = m.Level >= 2 ? 0.8f : 1.2f;
                im.Owed = m.Level <= 1 && MeteorSlam.AtDeathsDoor(v);
                Victims.Hurt(v, m.A, m, im);
                m.SetLink(Link.Thrown, v);
            }
            else if (v.isFlying())
            {
                // hauled down out of the sky: it has to land, stunned
                WingbreakerGround.Down(v, m.Level <= 1 ? 2.5f : m.Level == 2 ? 3.5f : 4.5f, m.A, 0, 0f);
                m.SetLink(Link.Down, v);
            }
            else
            {
                Combat.Stagger(v, 0.9f);   // too big to haul in: it only staggers on the rope
                m.SetLink(Link.Close, v);
            }
        }

        /// <summary>The rope taut from the hand to the creature's middle, the loop round it.</summary>
        private static void Taut(ChoreoMove m, Lasso l)
        {
            if (!l.Caught || !m.VOk && m.Held == null)
            {
                Drop(m, l);
                return;
            }
            Actor v = m.Held != null ? m.Held.A : m.V;
            l.At = Choreo.BodyAt(v, 0.5f);
            DrawLoop(m, l, Mathf.Clamp(Mathf.RoundToInt(Choreo.SpritePx(v) * 0.3f), 3, 10), 0.6f);
            l.Rope.Draw(m, Hand(m), l.At, 0.04f * m.Kk, HwLoKit.Hemp, 10);
        }

        /// <summary>The rope goes slack and falls away.</summary>
        private static void Drop(ChoreoMove m, Lasso l)
        {
            if (l.Dropped)
            {
                return;
            }
            l.Dropped = true;
            l.Rope.Hide(m);
            if (l.LoopOk)
            {
                Vector2 at = l.Loop.Pos;
                m.Unglue(l.Loop);
                if (m.Vis)
                {
                    Particle fall = Fx.Spawn(Art.Ring(Mathf.Max(2, l.LoopR), 1, 0.5f), at, m.Px, 0.45f, FxLayer.Over, 9);
                    fall?.Moving(new Vector2(0f, -4f * m.Px), 0f, 1f, 30f * m.Px).Color(HwLoKit.Hemp, Fx.WithA(HwLoKit.HempDark, 0f));
                }
            }
            l.Loop = null;
        }

        /// <summary>The hauled body is down on the ground again (or gone).</summary>
        private static bool Landed(ChoreoMove m)
        {
            return m.BeatT > 0.12f && Combat.HeightOf(m.V) <= 0.05f;
        }

        /// <summary>The hauled body has come within the hunter's reach.</summary>
        private static bool Arrived(ChoreoMove m)
        {
            return Vector2.Distance(m.Stance, m.VG) <= m.Stand + 0.5f * m.Kk || Landed(m);
        }

        /// <summary>One quarter of the hammer swing: the body on the rope goes round over its head.</summary>
        private static void Swing(ChoreoMove m, Lasso l, Grip g, Shape pose, float dur)
        {
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = pose,
                Ease = Ease.Linear,
                Grip = g,
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Grab | Form.Throw | Form.Spin | Form.Hunt,
                If = mm => mm.Held != null,
                Enter = mm => Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f),
                Every = (mm, k) =>
                {
                    Taut(mm, l);
                    if (mm.Vis && mm.Held != null && Random.value < 0.5f)
                    {
                        Fx.Afterimage(mm, 0.1f, Fx.WithA(mm.Hue.Ghost, 0.4f), mm.Hue.GhostEnd);
                    }
                }
            });
        }
    }
}
