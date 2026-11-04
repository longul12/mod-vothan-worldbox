using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ORB RELAY (team sync, Master). A spark of ki kindles between the leader's palms and swells
    /// into an orb (Charging: a fast blow breaks it, and the orb bursts in its hands). It lobs it to
    /// a friend, who catches it with a recoil that rings in both their colours: the orb grows, takes
    /// on the catcher's colour, swells again in its hands, and is passed on, each pass in rhythm
    /// (the catcher ready, palms out, the instant the thrower lets go). Every friend who has held it
    /// keeps a thread of its power on it. The last one draws it back, threads from everyone
    /// converging on it, and hurls the grown orb at the foe: a burst that cratered the ground and
    /// throws everyone round it, braided rings of all their colours. Whoever holds it is a target:
    /// break the holder, or kill the one about to catch, and the orb bursts harmlessly, the relay
    /// broken. The last one standing with it hurls it.
    /// I: one pass; II: two; III: three (the orb huge by the end), a world-stopping blast.
    /// Damage (x the hurler's damage): the orb I 1.9, II 2.4, III 3 + 0.45 per pass on the one it
    /// hits; x0.55 of that on everyone in the blast (1.3 tiles + 0.25 per pass).
    /// </summary>
    internal sealed class OrbRelayTech : Technique
    {
        public override string Id => "orb_relay";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Charge | Form.Area | Form.Team;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 34f, 54f, 80f };
        public override float[] Cooldown => new[] { 17f, 18f, 19f };

        private static readonly float[] Shares = { 18f, 28f, 42f };
        private static readonly List<Actor> _near = new List<Actor>();
        private static readonly List<Actor> _sorted = new List<Actor>(4);

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        private static float FinalMult(int level, int passes)
        {
            return (level <= 1 ? 1.9f : level == 2 ? 2.4f : 3f) + 0.45f * passes;
        }

        private static float Blast(int passes)
        {
            return 1.3f + 0.25f * passes;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c) || c.Allies.Count == 0)
            {
                return null;
            }
            int max = Mathf.Clamp(level, 1, 3);
            Plan best = null;
            for (int i = -1; i < c.Enemies.Count && i < 4; i++)
            {
                Actor v = i < 0 ? c.Focus : c.Enemies[i];
                if (v == null || i >= 0 && v == c.Focus)
                {
                    continue;
                }
                if (!Syncs.Fair(c.Self, v, out float cm, c.Forced) || !Combat.Grounded(v) || c.Dist(v) > (c.Forced ? 14f : 11f) || !Syncs.Worthy(c, v))
                {
                    continue;
                }
                List<Actor> partners = Syncs.Pick(c, v, level, max, Share(level), 8f, 12f);
                List<Actor> chain = Chain(c.Self, partners, v.current_position);
                if (chain == null)
                {
                    continue;
                }
                int passes = chain.Count - 1;
                float mult = FinalMult(level, passes);
                float score = c.Value(v, c.Expected(v, mult * 0.85f), cm);
                _near.Clear();
                c.CountNear(v.current_position, Blast(passes), _near);
                for (int j = 0; j < _near.Count; j++)
                {
                    Actor u = _near[j];
                    if (u != v)
                    {
                        c.Allowed(u, out float um);
                        score += c.Value(u, c.Expected(u, mult * 0.5f), um);
                    }
                }
                score += Syncs.Bonus(c, v, partners);
                if (best == null || score > best.Score)
                {
                    chain.RemoveAt(0);
                    Vector2 dir = v.current_position - c.Pos;
                    best = new Plan
                    {
                        Target = v,
                        Point = v.current_position,
                        Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                        Count = _near.Contains(v) ? _near.Count : 1 + _near.Count,   // (CountNear already counts the target)
                        Score = c.Forced ? Mathf.Max(score, 1f) : score,
                        Others = chain,
                        Data = c.Forced,
                        AreaRadius = Blast(passes)
                    };
                }
            }
            return best;
        }

        /// <summary>
        /// The order of the passes (a new list, the leader first), or null: the friends further from the
        /// foe before the nearer ones (the orb travels toward it), each pass at most 7.5 tiles, and the
        /// last one (the hurler) 2-10.5 tiles from the foe with nothing in the way.
        /// </summary>
        private static List<Actor> Chain(Actor lead, List<Actor> partners, Vector2 tp)
        {
            _sorted.Clear();
            _sorted.AddRange(partners);
            _sorted.Sort((x, y) => (y.current_position - tp).sqrMagnitude.CompareTo((x.current_position - tp).sqrMagnitude));
            List<Actor> chain = new List<Actor>(_sorted.Count + 1) { lead };
            for (int i = 0; i < _sorted.Count; i++)
            {
                if (Vector2.Distance(chain[chain.Count - 1].current_position, _sorted[i].current_position) <= 8.5f)
                {
                    chain.Add(_sorted[i]);
                }
            }
            while (chain.Count > 1 && !Throwable(chain[chain.Count - 1], tp))
            {
                chain.RemoveAt(chain.Count - 1);
            }
            return chain.Count >= 2 ? chain : null;
        }

        private static bool Throwable(Actor u, Vector2 tp)
        {
            float d = Vector2.Distance(u.current_position, tp);
            return d >= 2f && d <= 11f && Combat.ClearPath(u.current_position, tp, true);
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            bool forced = p.Data is bool b && b;
            if (v == null || p.Others == null || !Moves.CanPerform(a) || !Combat.Living(v))
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            List<Actor> partners = Syncs.Recheck(a, v, p.Others, 3, Share(level), forced, 10f);
            List<Actor> chain = partners.Count > 0 ? Chain(a, partners, v.current_position) : null;
            if (chain == null)
            {
                Crew.Fail("orb_relay", v);
                return false;
            }
            Vector2 tp = v.current_position;
            TeamSync s = new TeamSync(this, level, forced, v, tp, tp - a.current_position);
            Relay r = new Relay();
            s.Data = r;
            int n = chain.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = tp - chain[i].current_position;
                // (no member takes the foe as its target: nobody works on it until the orb flies)
                Plan mine = new Plan { Target = null, Point = tp, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1 };
                SyncMove m = s.Join(chain[i], mine);
                m.Role = i;
                Script(m, r, i, n);
            }
            s.OnTick = (ss, dt) => r.Tick(ss, dt);
            s.OnFrame = (ss, m) => r.Follow(m);
            s.OnClose = ss => r.KillFx();
            // (a relay needs every link: one friend that can't start calls it off)
            return s.Go(n - 1, Share(level), 7f);
        }

        // ------------------------------------------------------------ script --

        private static void Script(SyncMove m, Relay r, int i, int n)
        {
            TeamSync s = m.S;
            int L = s.Level;
            m.Call();
            if (i == 0)
            {
                // the seed: a spark kindles between its palms and swells into an orb
                m.Do(mm =>
                {
                    r.Holder = m.Slot;
                    r.Size = 0.4f;
                    r.Px = mm.Px;
                    r.Max = (2.4f + 2f * n) * Mathf.Lerp(1f, mm.Kk, 0.6f);
                    r.Glow = mm.Hue.Edge;
                    r.Fed.Add(m.Slot);
                });
                Hold(m, r, 0.42f, 2.2f * Mathf.Lerp(1f, m.Kk, 0.6f), null);
            }
            else
            {
                // ready for it: turned to the one who will pass it, palms out
                m.Do(mm => FaceSlot(mm, i - 1));
                m.PoseTo(0.12f, new Shape(6f, 1.06f, 0.94f, 0f, 0.04f), Ease.OutQuad, Tempo.Free);
                m.MeetWith("pass" + (i - 1), mm => i - 1, 4f);
                m.WaitUntil(mm => r.Holder == m.Slot || r.Spent || s.Failed, 1.6f);
                m.Last.Every = (mm, k) =>
                {
                    if (r.Flying && (r.Pos - mm.Stance).sqrMagnitude > 0.01f)
                    {
                        mm.Face(r.Pos - mm.Stance);
                    }
                };
                m.Unless(mm => r.Holder == mm.Slot && !r.Spent && !s.Failed, "end");
                Catch(m);
                Hold(m, r, 0.28f, 0.8f * Mathf.Lerp(1f, m.Kk, 0.6f), i + 1 < n ? i + 1 : -1);
            }
            // pass it on, or (the last one, or the last one left) hurl it
            m.Unless(mm => NextIn(s, mm.Slot) != null, "hurl");
            m.MeetWith("pass" + i, mm => { SyncMove nx = NextIn(s, mm.Slot); return nx != null ? nx.Slot : -1; }, 4f);
            m.Unless(mm => r.Holder == mm.Slot && !r.Spent && !s.Failed, "end");
            Throw(m, r);
            m.PoseTo(0.14f, new Shape(8f, 1.08f, 0.94f, 0f, 0.06f), Ease.OutQuad, Tempo.Free);
            // it keeps feeding the orb (a thread of its power) until it is hurled
            m.Meet("hurl", 4f);
            m.Goto("end");
            m.Section("hurl");
            m.Unless(mm => r.Holder == mm.Slot && !r.Spent && !s.Failed, "end");
            Hurl(m, r);
            m.Section("end");
            m.Recover(0.3f);
        }

        private static void FaceSlot(ChoreoMove mm, int slot)
        {
            SyncMove o = ((SyncMove)mm).S.At(slot);
            if (o != null && (o.Pose.Ground - mm.Stance).sqrMagnitude > 0.01f)
            {
                mm.Face(o.Pose.Ground - mm.Stance);
            }
        }

        /// <summary>The next member after 'slot' still in the sync (who catches its pass), or null.</summary>
        private static SyncMove NextIn(TeamSync s, int slot)
        {
            for (int i = slot + 1; i < s.Cast.Count; i++)
            {
                if (s.Cast[i].In)
                {
                    return s.Cast[i];
                }
            }
            return null;
        }

        /// <summary>
        /// It holds the orb and it swells ('grow' pixels; Charging: a fast blow now bursts it in its hands),
        /// motes streaming in, rings closing on it; it turns to whoever it passes to next ('faceSlot').
        /// </summary>
        private static void Hold(SyncMove m, Relay r, float dur, float grow, int? faceSlot)
        {
            float s0 = 0f, mote = 0f, ring = 0f;
            Beat b = new Beat
            {
                Tempo = Tempo.Free,
                Dur = dur,
                To = new Shape(4f, 1.06f, 0.94f, 0f),
                Ease = Ease.OutCubic,
                Charging = true,
                Track = false,
                Forms = Form.Ki | Form.Charge | Form.Team
            };
            b.Enter = mm =>
            {
                s0 = r.Size;
                if (faceSlot.HasValue && faceSlot.Value >= 0)
                {
                    FaceSlot(mm, faceSlot.Value);
                }
                Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.25f);
            };
            b.Every = (mm, k) =>
            {
                r.Size = Mathf.Min(r.Max, s0 + grow * Beat.Apply(Ease.OutQuad, k));
                if (k > 0.75f)
                {
                    mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.5f);
                }
                if (!mm.Vis)
                {
                    return;
                }
                float dt = Clock.Dt;
                mote -= dt;
                if (mote <= 0f)
                {
                    mote = 0.04f;
                    ChoreoFx.Motes(r.Pos, mm.Px, 2, 8f + r.Size * 2f, 0.12f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                }
                ring -= dt;
                if (ring <= 0f && k < 0.85f)
                {
                    ring = 0.14f;
                    Fx.Ring(r.Pos, mm.Px, 0.14f, 6f + r.Size * 2f, 1.5f, 1, 1, mm.TopLayer, 5, 1f, 1.6f,
                        Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.7f), Fx.WithA(mm.Hue.Core, 0.9f));
                }
            };
            m.Add(b);
        }

        /// <summary>The catch: the orb lands in its palms with a recoil (it slides back a step, leaning away).</summary>
        private static void Catch(SyncMove m)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.1f,
                Route = Route.Line,
                PathEase = Ease.OutCubic,
                Dest = mm =>
                {
                    Vector2 q = Combat.Clamp(mm.Stance - mm.Dir * 0.16f * mm.Kk);
                    return Combat.SafeSpot(q) ? q : mm.Stance;
                },
                To = new Shape(-12f, 0.92f, 1.08f, 0f, -0.04f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Ki | Form.Team
            };
            b.Enter = mm =>
            {
                if (mm.Vis)
                {
                    ChoreoFx.HeelDust(mm.Stance, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                }
            };
            m.Add(b);
        }

        /// <summary>The pass: both palms thrust toward the next one, the orb lobbed to it.</summary>
        private static void Throw(SyncMove m, Relay r)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.1f,
                To = new Shape(16f, 1.2f, 0.88f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Ki | Form.Projectile | Form.Team
            };
            b.Enter = mm =>
            {
                SyncMove me = (SyncMove)mm;
                SyncMove next = NextIn(me.S, me.Slot);
                if (next != null)
                {
                    FaceSlot(mm, next.Slot);
                }
            };
            b.On(0.4f, mm =>
            {
                SyncMove me = (SyncMove)mm;
                SyncMove next = NextIn(me.S, me.Slot);
                if (next != null && r.Holder == me.Slot && !r.Spent)
                {
                    r.Launch(me, next);
                }
            });
            m.Add(b);
        }

        /// <summary>
        /// The hurl: it draws the orb back (threads from everyone converging on it, the last moment a
        /// fast blow can burst it), then thrusts it at the foe, and holds the follow-through while it flies.
        /// </summary>
        private static void Hurl(SyncMove m, Relay r)
        {
            TeamSync s = m.S;
            int L = s.Level;
            Beat wind = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.16f + 0.02f * L,
                To = new Shape(-16f, 0.94f, 1.08f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Charging = true,
                Track = false,
                Forms = Form.Ki | Form.Charge | Form.Projectile | Form.Team
            };
            wind.Enter = mm =>
            {
                r.Hip = true;
                if (s.VOk)
                {
                    mm.Face(s.VG - mm.Stance);
                }
                else
                {
                    mm.Face(s.Point - mm.Stance);
                }
                s.Union("hurl", r.Pos, 0.9f + 0.15f * r.Fed.Count, true);
            };
            wind.Every = (mm, k) =>
            {
                if (k > 0.6f)
                {
                    mm.Pose.Nudge = Choreo.Tremble(mm.BeatT, 0.7f);
                }
            };
            m.Add(wind);
            Beat thrust = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(20f, 1.26f, 0.86f, 0f, 0.16f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Ki | Form.Projectile | Form.Area | Form.Team
            };
            thrust.On(0.35f, mm => Fire((SyncMove)mm, r));
            m.Add(thrust);
            m.PoseTo(0.16f, new Shape(12f, 1.12f, 0.92f, 0f, 0.1f), Ease.OutQuad, Tempo.Follow);
            // it watches it fly; when it bursts on the foe, the blast
            m.WaitUntil(mm => Spent(r.Thrown), 1.4f);
            m.Last.Every = (mm, k) => Boom((SyncMove)mm, r);
            m.Do(mm => Boom((SyncMove)mm, r));
        }

        /// <summary>The grown orb leaves its hands: a shot of all the relay's power at the foe.</summary>
        private static void Fire(SyncMove m, Relay r)
        {
            TeamSync s = m.S;
            int L = s.Level;
            m.Pass("hurl");
            if (r.Spent || r.Holder != m.Slot)
            {
                return;
            }
            Vector2 hip = r.Pos;
            r.Spent = true;
            r.Hip = false;
            r.KillFx();
            Vector2 from = m.Palms();
            Actor tgt = s.VOk ? s.V : null;
            Vector2 aim = tgt != null ? Choreo.BodyAt(tgt, 0.45f) - from : s.Dir;
            float dist = tgt != null ? Vector2.Distance(from, tgt.current_position) : 8f;
            int passes = Mathf.Max(1, r.Fed.Count - 1);
            float size = Mathf.Clamp(r.Size / (2f * Mathf.Lerp(1f, m.Kk, 0.6f)), 1.2f, 6f);
            ShotSpec spec = new ShotSpec
            {
                Kind = ShotKind.Orb,
                Flight = Flight.Straight,
                Speed = 14f,
                Range = dist + 3f,
                Size = size,
                Mult = FinalMult(L, passes) * (tgt != null ? Mathf.Max(0.5f, Syncs.Share(m.A, tgt)) : 1f),
                Hurt = Hurt.Launch,
                Push = 2f + 0.3f * passes,
                Crater = 0.6f + 0.2f * passes,
                Area = Blast(passes),
                AreaMult = 0.55f,
                Element = Element.School
            };
            r.Thrown = Shots.Fire(m.A, m, from, aim, tgt, Vector2.zero, spec, m.Hue);
            r.ThrownBy = m.A;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile, 0.1f);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                ChoreoFx.Smear(hip, from, m.Px, 0.12f, 3, 2, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
                Kit.Flash(from, m.Px, Mathf.RoundToInt((3f + passes) * m.Kk), 0.08f, Color.white, m.TopLayer);
                Fx.Ring(from, m.Px, 0.2f, 3f, (12f + 3f * passes) * m.Kk, 2, 1, m.TopLayer, 6, 1f, 2.4f, Fx.WithA(m.Hue.Core, 1f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 40f, 0.5f, m.Kk);
            }
            Ruin.Quake(m.A, m.Pose.Ground, Scale.Light, 0.8f, m.Kk);
        }

        /// <summary>
        /// Is the thrown orb spent (burst on someone, fizzled, gone)? A shot that bursts leaves the flight
        /// list without marking itself Done, so its absence from it counts.
        /// </summary>
        private static bool Spent(Shot sh)
        {
            if (sh == null || sh.Done || sh.Hits > 0)
            {
                return true;
            }
            IReadOnlyList<Shot> all = Shots.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == sh)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The orb burst on a foe: the relay's blast (braided rings of every colour, a shockwave, the world stops at III).</summary>
        private static void Boom(SyncMove m, Relay r)
        {
            Shot sh = r.Thrown;
            TeamSync s = m.S;
            if (sh == null || sh.Hits <= 0 || !Spent(sh) || sh.A != r.ThrownBy || s.Happened("boom"))
            {
                return;
            }
            int L = s.Level;
            Vector2 at = sh.Pos + new Vector2(0f, Mathf.Max(0.3f, sh.Z));
            Vector2 g = sh.Pos;
            WorldTile tile = Combat.TileAt(g);
            s.Union("boom", at, 1.3f + 0.25f * r.Fed.Count, false);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", tile, 0.1f);
            if (Kit.Visible(at))
            {
                for (int i = 0; i < s.Cast.Count; i++)
                {
                    SyncMove o = s.Cast[i];
                    if (!o.Dropped)
                    {
                        Kit.Shockwave(g, m.Px, 4f + 3f * i, Blast(r.Fed.Count - 1) / m.Px * (1f + 0.1f * i), 0.4f + 0.05f * i, Fx.WithA(o.Hue.Edge, 0.9f), 2);
                    }
                }
                Kit.DustRing(g, m.Px, tile, 14, 48f, 0.6f, m.Kk);
                Kit.Debris(g, m.Px, tile, 8 + 2 * L, m.Kk);
                if (L >= 2)
                {
                    Kit.ImpactFrame(at, m.Px, (0.32f + 0.08f * L) * m.Kk, m.Hue.Pal, false, FxLayer.Behind);
                }
            }
            Ruin.Quake(m.A, g, Scale.Heavy, 1.3f, m.Kk);
            if (L >= 3)
            {
                Kit.Stop(at, 0.06f * m.ImpactK);
            }
        }

        // ------------------------------------------------------------- the orb --

        /// <summary>The orb as it is passed: who holds it (or where it flies), how big, in whose colour.</summary>
        private sealed class Relay
        {
            public int Holder = -1;
            public bool Flying, Spent, Hip;
            public Vector2 Pos, From, Prev, End;
            public float T, Dur, Arc;
            public float Size, Max = 6f, Px = 0.1f;
            public Color Glow = Color.white;
            public SyncMove To;
            public Shot Thrown;
            public Actor ThrownBy;
            public readonly List<int> Fed = new List<int>(4);
            private float _thread, _trail;
            private Particle _p, _core;

            /// <summary>Thrown from 'from' to 'to' along a lob.</summary>
            public void Launch(SyncMove from, SyncMove to)
            {
                Flying = true;
                Holder = -1;
                To = to;
                From = Pos;
                Prev = Pos;
                T = 0f;
                End = to.Palms();
                float d = Vector2.Distance(From, End);
                Dur = 0.16f + 0.045f * d;
                Arc = Mathf.Clamp(0.22f * d, 0.3f, 1.3f);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", from.A.current_tile);
                if (from.Vis)
                {
                    Kit.Flash(Pos, Px, Mathf.RoundToInt(2f + Size * 0.5f), 0.06f, Fx.WithA(from.Hue.Core, 0.9f));
                }
            }

            /// <summary>Once a frame: the flight, a dropped catch, the threads of those who fed it.</summary>
            public void Tick(TeamSync s, float dt)
            {
                if (Spent)
                {
                    return;
                }
                if (Flying)
                {
                    T += dt;
                    float u = Mathf.Clamp01(T / Mathf.Max(0.05f, Dur));
                    if (To != null && To.In)
                    {
                        End = To.Palms();
                    }
                    Pos = Vector2.Lerp(From, End, Beat.Apply(Ease.InOutSine, u)) + new Vector2(0f, Arc * 4f * u * (1f - u));
                    _trail -= dt;
                    if (_trail <= 0f && Kit.Visible(Pos))
                    {
                        _trail = 0.02f;
                        Kit.Line(Prev, Pos, Px, 0.16f, Mathf.Clamp(Mathf.RoundToInt(Size * 0.6f), 1, 4), FxLayer.Top, 10, Fx.WithA(Color.white, 0.8f), Fx.WithA(Glow, 0.7f), Fx.WithA(Glow, 0f));
                        Prev = Pos;
                    }
                    Draw();
                    if (u >= 1f)
                    {
                        if (To != null && To.In)
                        {
                            Caught(s, To);
                        }
                        else
                        {
                            Burst(s);
                        }
                    }
                    return;
                }
                SyncMove h = s.At(Holder);
                if (Holder >= 0 && (h == null || !h.In))
                {
                    Burst(s);
                    return;
                }
                _thread -= dt;
                if (_thread <= 0f && Kit.Visible(Pos))
                {
                    _thread = 0.08f;
                    for (int i = 0; i < Fed.Count; i++)
                    {
                        SyncMove o = s.At(Fed[i]);
                        if (o != null && o.In && o.Slot != Holder)
                        {
                            Crew.Link(o.Palms(), Pos, o.Px, o.Hue.Edge, 0.1f);
                        }
                    }
                }
            }

            /// <summary>From a member's Frame: the orb in its holder's hands (drawn back at its hip for the hurl).</summary>
            public void Follow(SyncMove m)
            {
                if (Spent || Flying || m.Slot != Holder)
                {
                    return;
                }
                Pos = Hip ? m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.28f, -m.Hpx * 0.06f) * m.Px : m.Palms();
                Px = m.Px;
                Glow = Color.Lerp(Glow, m.Hue.Edge, 0.2f);
                Draw();
            }

            private void Caught(TeamSync s, SyncMove m)
            {
                Flying = false;
                Holder = m.Slot;
                To = null;
                Size = Mathf.Min(Max, Size + 1.3f * Mathf.Lerp(1f, m.Kk, 0.6f));
                Pos = m.Palms();
                if (!Fed.Contains(m.Slot))
                {
                    Fed.Add(m.Slot);
                }
                s.Union("catch" + m.Slot, Pos, 0.7f + 0.12f * Fed.Count, false);
                Choreo.Sound("event:/SFX/HIT/HitGeneric", m.A.current_tile, 0.08f);
                if (m.Vis)
                {
                    Kit.Sparks(Pos, m.Px, 4, 40f, 90f, m.Hue.Edge);
                }
            }

            /// <summary>The relay is broken: the orb bursts where it is (harmless), and everyone stands down.</summary>
            private void Burst(TeamSync s)
            {
                Spent = true;
                s.Failed = true;
                Choreo.BurstAt(Pos, Px, true, null);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(Pos), 0.1f);
                KillFx();
                if (Dev.On) Dev.Trace("orb relay: the orb bursts (the relay is broken)");
            }

            private void Draw()
            {
                bool vis = Kit.Visible(Pos, 0.2f);
                if (!vis)
                {
                    if (_p != null && _p.Alive)
                    {
                        _p.R.enabled = false;
                    }
                    if (_core != null && _core.Alive)
                    {
                        _core.R.enabled = false;
                    }
                    return;
                }
                int rMax = Mathf.Clamp(Mathf.RoundToInt(Max), 2, 40);
                if (_p == null || !_p.Alive)
                {
                    _p = Fx.Spawn(Art.Disk(rMax, 1f, 0.45f), Pos, Px, 1f, FxLayer.Top, 11);
                    if (_p != null)
                    {
                        _p.Held = true;
                    }
                }
                if (_core == null || !_core.Alive)
                {
                    _core = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(rMax * 0.55f)), 1f, 0f), Pos, Px, 1f, FxLayer.Top, 12);
                    if (_core != null)
                    {
                        _core.Held = true;
                    }
                }
                float k = Mathf.Clamp01(Size / Mathf.Max(1f, Max));
                float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 38f);
                if (_p != null)
                {
                    _p.R.enabled = true;
                    _p.Pos = Pos;
                    _p.Px = Px;
                    _p.Size0 = _p.Size1 = Mathf.Max(0.08f, k * pulse);
                    _p.Grad = null;
                    _p.R.color = Fx.WithA(Glow, 0.75f);
                    _p.Cine = Cinema.Playing;
                }
                if (_core != null)
                {
                    _core.R.enabled = true;
                    _core.Pos = Pos;
                    _core.Px = Px;
                    _core.Size0 = _core.Size1 = Mathf.Max(0.08f, k * (1f + 0.06f * Mathf.Sin(Time.time * 51f)));
                    _core.Grad = null;
                    _core.R.color = Color.Lerp(Color.white, Glow, 0.15f);
                    _core.Cine = Cinema.Playing;
                }
            }

            public void KillFx()
            {
                if (_p != null && _p.Alive)
                {
                    _p.Kill();
                }
                if (_core != null && _core.Alive)
                {
                    _core.Kill();
                }
                _p = null;
                _core = null;
            }
        }
    }
}
