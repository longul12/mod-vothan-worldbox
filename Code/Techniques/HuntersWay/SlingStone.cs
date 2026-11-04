using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SLING STONE (Hunter's Way, Novice). The hunter plants its feet, raises a sling over its head
    /// and whirls it (a stone on a short cord circling faster and faster above its hand, a streak of
    /// light tracing the circle, a whoosh at every turn), then snaps the arm forward: the stone
    /// flies, tumbling, at the flyer (the mod's own projectile: it can be dodged, cut, caught or
    /// sent back). A flyer it strikes is brought down: a dragon's wings give out and it comes down
    /// to land, stunned; whatever else flies is knocked dizzy. II: a quick second whirl and a
    /// second stone. III: a third, heavy stone wrapped in green light that brings the flyer down
    /// longer (a dragon scarring the ground where it lands, from a Master up). Made for
    /// flyers (the Brain uses it on nothing else); a hunter's casual answer to the sky.
    /// Damage (x the user's damage): I 0.9; II 0.8 + 0.8; III 0.8 + 0.8 + 1.3.
    /// </summary>
    internal sealed class SlingStoneTech : Technique
    {
        public override string Id => "sling_stone";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Projectile | Form.Hunt;
        public override Prey Prey => Prey.Flyer;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 16f };
        public override float[] Cooldown => new[] { 4.5f, 5.5f, 6.5f };

        public static float Range(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 7.5f : 9f;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1.6f : 2.9f;
        }

        private static readonly System.Func<Ctx, Actor, float, int, float> ValueOf = Value;

        private static float Value(Ctx c, Actor v, float cm, int level)
        {
            if (!c.Forced && v.getHealth() <= c.Expected(v, 0.5f) * cm)
            {
                return 0f;   // a plain swing does as well
            }
            float s = c.Value(v, c.Expected(v, Total(level)), cm);
            if (v.isFlying() || Combat.HeightOf(v) > 0.6f)
            {
                s += 12f;   // up there, where nothing else of a hunter's reaches
            }
            if (c.Dist(v) > 3f)
            {
                s += 6f;
            }
            return s;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor v = HwLoKit.PickRanged(c, this, level, 1.5f, Range(level), ValueOf, out float score);
            if (v == null || score <= 0f)
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

        /// <summary>What one sling throw carries along its beats: the whirling stone, its cord, the stones loosed.</summary>
        private sealed class Sling
        {
            public Particle Stone;
            public int Gen;
            public readonly HwLoRope Cord = new HwLoRope();
            public readonly List<Shot> Loosed = new List<Shot>(3);
            public float Angle, LastTurn;
            public Vector2 At;

            public bool StoneOk => Stone != null && Stone.Alive && Stone.Gen == Gen;

            /// <summary>Every stone it loosed has landed or flown out.</summary>
            public bool Settled()
            {
                IReadOnlyList<Shot> all = Shots.All;
                for (int i = 0; i < Loosed.Count; i++)
                {
                    Shot s = Loosed[i];
                    if (s.Done)
                    {
                        continue;
                    }
                    for (int j = 0; j < all.Count; j++)
                    {
                        if (all[j] == s)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Sling sl = new Sling();
            // it plants its feet, the free hand pointing at the prey
            m.PoseTo(0.08f, new Shape(-6f, 0.96f, 1.06f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Forms);
            Whirl(m, sl, level <= 1 ? 0.34f : 0.3f, 2f, false);
            Loose(m, sl, Stone(level, level <= 1 ? 0.9f : 0.8f, false));
            if (level >= 2)
            {
                Whirl(m, sl, 0.16f, 1f, false);
                Loose(m, sl, Stone(level, 0.8f, false));
            }
            if (level >= 3)
            {
                Whirl(m, sl, 0.22f, 1.5f, true);
                Loose(m, sl, Stone(level, 1.3f, true));
            }
            // it watches the stone fly
            m.PoseTo(0.1f, new Shape(8f, 1.04f, 0.97f, 0f, 0.06f), Ease.OutQuad, Tempo.Follow);
            m.WaitUntil(mm => sl.Settled(), 0.6f);
            m.Recover(0.2f);
            m.Ended2 = (mm, aborted) => sl.Cord.Hide(mm);
            return m.Start();
        }

        private static ShotSpec Stone(int level, float mult, bool heavy)
        {
            return new ShotSpec
            {
                Kind = ShotKind.Stone,
                Flight = Flight.Straight,
                Speed = heavy ? 26f : level <= 1 ? 24f : 28f,
                Range = Range(level) + 3f,
                Size = heavy ? 1.7f : level >= 2 ? 1.15f : 1f,
                Mult = mult,
                Hurt = heavy ? Hurt.Knockdown : Hurt.Reel,
                Push = heavy ? 1f : 0.5f,
                Deflectable = true,
                Reflectable = true,
                Cuttable = true
            };
        }

        /// <summary>
        /// The sling whirls over its head: the stone circles a flat ellipse above its hand, 'turns'
        /// times in 'dur' seconds, faster and faster, a streak of light tracing it ('heavy': the stone
        /// glows green, the circle wider).
        /// </summary>
        private static void Whirl(ChoreoMove m, Sling sl, float dur, float turns, bool heavy)
        {
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = dur,
                To = new Shape(-10f, 0.94f, 1.1f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                Forms = Form.Projectile | Form.Hunt,
                If = mm => mm.VOk,
                Enter = mm =>
                {
                    sl.Angle = 0f;
                    sl.LastTurn = 0f;
                    if (!sl.StoneOk && mm.Vis)
                    {
                        int r = Mathf.Max(2, Mathf.RoundToInt((heavy ? 2.4f : 1.5f) * Mathf.Lerp(1f, mm.Kk, 0.5f)));
                        sl.Stone = mm.Glue(Fx.Spawn(ChoreoArt.Boulder(r, Random.Range(0, 4)), mm.HeadPos(), mm.Px, 30f, FxLayer.Over, 10));
                        if (sl.Stone != null)
                        {
                            sl.Gen = sl.Stone.Gen;
                        }
                    }
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                },
                Every = (mm, k) =>
                {
                    // the hand above its head, the stone circling it on its cord
                    Vector2 hand = mm.HeadPos() + new Vector2(mm.Fwd * 1.5f, 1f) * mm.Px;
                    sl.Angle = turns * Mathf.PI * 2f * Beat.Apply(Ease.InQuad, k) * (mm.Fwd > 0f ? 1f : -1f);
                    float rr = (heavy ? 7f : 5.5f) * Mathf.Lerp(1f, mm.Kk, 0.6f) * mm.Px;
                    Vector2 at = hand + new Vector2(Mathf.Cos(sl.Angle) * rr, Mathf.Sin(sl.Angle) * rr * 0.42f);
                    sl.At = at;
                    if (Mathf.Abs(sl.Angle) - sl.LastTurn >= Mathf.PI * 2f)
                    {
                        sl.LastTurn += Mathf.PI * 2f;
                        Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile, 0.05f);
                    }
                    if (!mm.Vis)
                    {
                        sl.Cord.Hide(mm);
                        return;
                    }
                    if (sl.StoneOk)
                    {
                        sl.Stone.Pos = at;
                        sl.Stone.Rot += 900f * Clock.Dt;
                        sl.Stone.Color(Color.Lerp(Fx.SoilColor(mm.A.current_tile), heavy ? mm.Hue.Core : Color.white, heavy ? 0.45f : 0.15f));
                    }
                    sl.Cord.Draw(mm, hand, at, 0f, HwLoKit.HempDark, 3);
                    // the streak of light tracing the circle (brighter as it speeds up)
                    Particle t = Fx.Spawn(Art.Pixel, at, mm.Px, 0.1f + 0.06f * k, FxLayer.Over, 7);
                    t?.Sized(heavy ? 1.6f : 1.2f, 0.5f).Color(Fx.WithA(mm.Hue.Core, 0.5f + 0.4f * k), Fx.WithA(mm.Hue.Edge, 0.4f), Fx.WithA(mm.Hue.Edge, 0f));
                    if (heavy && Random.value < 0.4f)
                    {
                        Particle g = Fx.Spawn(Art.Pixel, at + Random.insideUnitCircle * 2f * mm.Px, mm.Px, 0.25f, FxLayer.Top, 9);
                        g?.Moving(Random.insideUnitCircle * 10f * mm.Px, 0f, 3f).Color(mm.Hue.Core, Fx.WithA(mm.Hue.Edge, 0f));
                    }
                }
            });
        }

        /// <summary>The arm snaps forward and the stone flies (the mod's own shot) at the prey.</summary>
        private static void Loose(ChoreoMove m, Sling sl, ShotSpec spec)
        {
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                To = new Shape(20f, 1.2f, 0.88f, 0f, 0.14f),
                Ease = Ease.OutExpo,
                Forms = Form.Projectile | Form.Hunt | Form.Fast,
                If = mm => mm.VOk,
                Enter = mm =>
                {
                    Vector2 from = sl.At.sqrMagnitude > 0.01f ? sl.At : mm.HeadPos();
                    Vector2 aim = mm.VPoint(0.5f) - from;
                    Shot s = Shots.Fire(mm.A, mm, from, aim, mm.V, Vector2.zero, spec, mm.Hue);
                    if (s != null)
                    {
                        sl.Loosed.Add(s);
                    }
                    if (sl.StoneOk)
                    {
                        mm.Unglue(sl.Stone);
                    }
                    sl.Stone = null;
                    sl.Cord.Hide(mm);
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        Kit.Flash(from, mm.Px, 2, 0.05f, Fx.WithA(mm.Hue.Core, 0.9f), mm.TopLayer, 22);
                        Kit.SpeedLines(from, aim, mm.Px, 2, 2f, 0.1f, Fx.WithA(mm.Hue.Edge, 0.8f));
                        Fx.Ghost(mm, -mm.Dir * 2f * mm.Px, -6f, 0.07f, Fx.WithA(mm.Hue.Ghost, 0.5f), mm.Hue.GhostEnd);
                    }
                }
            });
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.07f, To = new Shape(10f, 1.08f, 0.94f, 0f, 0.1f), Ease = Ease.OutQuad, Track = false, If = mm => sl.Loosed.Count > 0 });
        }

        // ---------------------------------------------------- bringing it down --

        private sealed class Downed
        {
            public Actor V, By;
            public long Id, ById;
            public int Level;
            public bool Heavy;
        }

        private static readonly List<Downed> _pending = new List<Downed>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.HitDealt += OnHit;
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += _pending.Clear;
        }

        /// <summary>A stone of a Sling Stone struck something: a flyer is brought down (next frame, after its reaction).</summary>
        private static void OnHit(Actor a, Actor v, HitResult r, Move by)
        {
            if (!(by is ChoreoMove m) || !(m.Tech is SlingStoneTech) || v == null || !r.Landed || _pending.Count > 32)
            {
                return;
            }
            if (!Preys.Is(v, Prey.Flyer) && !v.isFlying())
            {
                return;
            }
            long id = v.Sid();
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].V == v && _pending[i].Id == id)
                {
                    _pending[i].Heavy |= r.Dealt > 0f && m.Level >= 3;
                    return;
                }
            }
            _pending.Add(new Downed { V = v, Id = id, By = a, ById = a != null ? a.Sid() : -1, Level = m.Level, Heavy = m.Level >= 3 });
        }

        private static void Tick(float dt)
        {
            if (_pending.Count == 0)
            {
                return;
            }
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Downed d = _pending[i];
                _pending.RemoveAt(i);
                try
                {
                    BringDown(d);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("sling_stone down", e);
                }
            }
        }

        private static void BringDown(Downed d)
        {
            Actor v = d.V;
            if (v == null || v.Sid() != d.Id || !Combat.Living(v) || v.isInMagnet() || v.isInsideSomething())
            {
                return;
            }
            Actor by = d.By != null && d.By.Sid() == d.ById && d.By.isAlive() ? d.By : null;
            float secs = d.Level <= 1 ? 1.6f : d.Level == 2 ? 2.4f : 3.4f;
            Tier tier = by != null ? Tiers.Of(by) : Tier.None;
            int force = d.Heavy && tier >= Tier.Master && Settings.Destruction > 0 ? 1 : 0;   // (a scar where a dragon touches down; a casual stone breaks no more)
            if (v.isFlying())
            {
                // its wings give out: it comes down to land, stunned (a dragon ploughs where it lands)
                WingbreakerGround.Down(v, secs, by, force, force > 0 ? 0.9f : 0f);
            }
            else if (!Moves.Locked(v))
            {
                Combat.Stagger(v, secs * 0.6f);
            }
            Vector2 head = v.current_position + new Vector2(0f, v.position_height + Choreo.SpritePx(v) * Choreo.PxOf(v));
            if (Kit.Visible(head))
            {
                float px = Choreo.PxOf(v);
                Fx.HitSpark(head, px, d.Heavy ? 1 : 0, Vector2.down, d.Heavy ? 5 : 3);
                Kit.SpeedLines(head, Vector2.down, px, d.Heavy ? 4 : 2, 4f, 0.2f, new Color(1f, 1f, 1f, 0.8f));
            }
            if (Dev.On) Dev.Trace("sling_stone brings " + v.asset.id + "#" + d.Id + " down " + secs.ToString("F1") + " s (flying " + v.isFlying() + ", force " + force + ")");
        }
    }
}
