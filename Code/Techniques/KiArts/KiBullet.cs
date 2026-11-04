using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI BULLET (Ki Arts, Novice). The arm comes up and points, a glint at the fingertip, and the
    /// finger flicks: a bead of gold light leaves with a crack and a ring of air, a long thin tracer
    /// behind it, faster than any other shot. It stings (the head snaps back) and pierces in a
    /// spray of sparks; a foe gathering power anywhere in its line has the charge shot out of its
    /// hands (Form.Interrupt: the bullet breaks charges from afar). It also picks flyers and bodies
    /// knocked into the air out of the sky. II: a double flick; III: three, the last one bigger.
    /// Damage per bullet (x the user's damage): I 0.75; II 0.62 each; III 0.55, 0.55, 0.77.
    /// </summary>
    internal sealed class KiBulletTech : Technique
    {
        public override string Id => "ki_bullet";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Projectile | Form.Fast | Form.Interrupt;
        public override Form Answers => Form.Charge;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 5f, 8f, 12f };
        public override float[] Cooldown => new[] { 4f, 3.6f, 3.2f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Launched;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 10f : 12f;
        }

        private static int Count(int level)
        {
            return Mathf.Clamp(level, 1, 3);
        }

        private static float Each(int level)
        {
            return level <= 1 ? 0.75f : level == 2 ? 0.62f : 0.55f;
        }

        /// <summary>Its whole damage on one foe (x the user's damage).</summary>
        public static float Total(int level)
        {
            return level <= 1 ? 0.75f : level == 2 ? 1.24f : 1.87f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 14; i++)
            {
                float d = c.EnemyDist[i];
                if (d > range + (c.Forced ? 4f : 0f))
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (c.ForceTarget != null && v != c.ForceTarget || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                // (it flies over water, never through a wall or a mountain)
                if (d > 1.2f && !Combat.ClearPath(c.Pos, v.current_position, true))
                {
                    continue;
                }
                bool link = c.Linked && v == c.LinkTarget;
                float score;
                if (Moves.Charging(v))
                {
                    // the bullet's purpose: the charge it shoots out of the foe's hands is worth the blow it stops
                    score = c.D * 3f + 30f + 10f * Tiers.Power(v);
                    if (Beams.Of(v) == null && Reads.AimsAt(v, c.Self))
                    {
                        score += 12f;   // gathering it at this fighter
                    }
                }
                else
                {
                    if (!c.Forced && !link)
                    {
                        if (d < 1.6f)
                        {
                            continue;   // point blank: a blow does more
                        }
                        if (v.getHealth() <= c.Expected(v, 0.6f) * cm)
                        {
                            continue;   // an ordinary blow ends it
                        }
                    }
                    score = c.Value(v, c.Expected(v, Total(level)), cm);
                    if (!Combat.Grounded(v))
                    {
                        score += 10f;   // a flyer, a body in the air: out of every fist's reach
                    }
                    if (c.FoeRange(v) > 2.5f && d > 2.5f)
                    {
                        score += 10f;   // an archer or a mage keeping its distance
                    }
                    if (d > 3f)
                    {
                        score += 4f;
                    }
                }
                if (link)
                {
                    score += 6f;
                }
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
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

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            int n = Count(level);
            Form shows = Forms;
            m.Face(p.Dir);
            // POINT: the arm comes up, the other hand to the hip; a glint and a mote at the fingertip
            m.PoseTo(0.09f, new Shape(-4f, 0.94f, 1.06f, 0f, -0.02f), Ease.OutQuad, Tempo.Wind).Shows(shows)
                .OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Vector2 tip = Tip(mm);
                        Fx.Glint(tip, mm.Px, 0.12f, mm.Hue.Core, 0.7f + 0.15f * mm.Level);
                        ChoreoFx.Motes(tip, mm.Px, 3, 7f, 0.09f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                });
            for (int i = 0; i < n; i++)
            {
                bool last = i == n - 1;
                float size = level >= 3 && last ? 1.45f : 1f + 0.08f * (level - 1);
                float mult = Each(level) * (level >= 3 && last ? 1.4f : 1f);
                bool heavy = level >= 3 && last;
                // FLICK: the finger snaps forward, the bullet is away (a crack, a ring of air)
                m.PoseTo(0.045f, new Shape(heavy ? 10f : 7f, heavy ? 1.14f : 1.1f, 0.93f, 0f, heavy ? 0.06f : 0.04f), Ease.OutExpo, Tempo.Act).Shows(shows)
                    .OnEnter(mm => Flick(mm, size, mult, heavy));
                // the recoil kicks the hand back; a heartbeat to aim again
                m.PoseTo(last ? 0.09f : 0.06f, new Shape(heavy ? -9f : -6f, 0.96f, 1.05f, 0f, -0.02f), Ease.OutQuad, Tempo.Follow).Shows(shows);
            }
            // the arm stays out a moment, a wisp of smoke curling off the fingertip
            m.Hold(0.07f, false, Tempo.Follow).Shows(shows).OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    ChoreoFx.Smoke(Tip(mm), mm.Px, 1, 1f, Fx.WithA(mm.Hue.Edge, 0.5f), 0.45f);
                }
            });
            m.Recover(0.16f);
            return m.Start();
        }

        /// <summary>The pointing fingertip (the bullet's muzzle).</summary>
        private static Vector2 Tip(ChoreoMove m)
        {
            return m.Hand(Limb.Palm) + new Vector2(m.Fwd * 1.5f * m.Px, m.Hpx * 0.06f * m.Px);
        }

        /// <summary>One flick: a bullet at the target (or a new one near it, or straight on along the aim).</summary>
        private static void Flick(ChoreoMove m, float size, float mult, bool heavy)
        {
            if (!m.VOk)
            {
                m.Retarget(3f);
            }
            Actor v = m.VOk ? m.V : null;
            if (v != null)
            {
                m.Face(m.VG - m.Stance);
            }
            Vector2 from = Tip(m);
            Vector2 aim = v != null ? m.VPoint(0.5f) - from : m.Aim;
            float dist = v != null ? Vector2.Distance(m.Stance, m.VG) : Range(m.Level);
            ShotSpec s = new ShotSpec
            {
                Kind = ShotKind.Bullet,
                Flight = Flight.Straight,
                Speed = (30f + 3f * m.Level) * (heavy ? 0.9f : 1f),
                Range = dist + 4f,
                Size = size,
                Mult = mult,
                Hurt = heavy ? Hurt.Reel : Hurt.SnapBack,
                Push = heavy ? 0.8f : 0.25f,
                // (a master's heavy bullet bites into the ground under what it strikes)
                Crater = heavy && m.Tier >= Tier.Master ? 0.3f : 0f
            };
            KiFlight.Fire(m, from, aim, v, s, KiFlight.Style.Bullet);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.02f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            float ang = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            Kit.Flash(from, px, Mathf.RoundToInt((heavy ? 4f : 2.5f) * m.Kk), 0.05f, Fx.WithA(m.Hue.Core, 1f), m.TopLayer, 24);
            // a ring of air snapping off the fingertip, across the line of fire
            Particle ring = Fx.Ring(from, px, 0.13f, 1f, (heavy ? 7f : 4.5f) * m.Kk, heavy ? 2 : 1, 1, m.TopLayer, 23, 0.4f, 2.4f,
                Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            if (ring != null)
            {
                ring.Rot = ang + 90f;
            }
            Kit.SpeedLines(from + aim.normalized * 2f * px, aim, px, heavy ? 3 : 2, 1.5f, 0.07f, Fx.WithA(m.Hue.Core, 0.85f), m.TopLayer);
            Kit.Sparks(from, px, heavy ? 3 : 1, 40f, 80f, m.Hue.Edge, m.TopLayer);
        }
    }

    /// <summary>
    /// KI FLIGHT. What the Ki Arts shots of this school add to the engine's projectiles, so a bullet,
    /// a volley's orbs, a homing swarm and a spinning disc never look alike in the air:
    ///   bullet      a long thin tracer under the engine's bright streak, sparks shed behind; it
    ///               pierces: a spray of sparks flies on through the body it hit
    ///   volley      the orbs the engine draws; those that strike the ground throw up a puff of dust
    ///   swarm       a comet tail of motes; it swings wide before it turns (its turn ramps up), weaves
    ///               a little, and when its own foe falls it finds another; it pops in a ring
    ///   disc        a whirring ring round the spinning disc, sparks thrown off its rim; cut lines on
    ///               whatever it slices; a flash and a ring in the thrower's hand when it is caught
    ///   reflected   a shot a barrier sent back: a trail of glints in the reflector's colours
    /// Also rings of ki that follow a body a blow sends sliding (a palm's push, an orb's blast).
    /// Ticked once per frame in move time; draws nothing off screen; forgets everything when the
    /// world is cleared.
    /// </summary>
    internal static class KiFlight
    {
        internal enum Style
        {
            Bullet,
            Volley,
            Swarm,
            Disc,
            Reflected
        }

        private sealed class Track
        {
            public Shot S;
            public Style Style;
            public Hue Hue;
            public int Hits;
            public float LastAge, T, Seed, Px, K, TurnTo;
            public Vector2 Last;
            public bool Lob, Seen;
            /// <summary>Frames in a row its age stood still while time ran (it is gone).</summary>
            public int Stale;
            public Actor Owner;
            public long OwnerId;
            /// <summary>A swarm's foes: where an orb whose foe fell looks for a new one.</summary>
            public List<Actor> Pool;
            public float FxT;
            /// <summary>A bullet's gold halo round the engine's white bead.</summary>
            public Particle Halo;
            /// <summary>
            /// The ground it scorches where it strikes (tiles; 0: none): a master's small shots mark the
            /// ground and break buildings, without the fire a big blast lights.
            /// </summary>
            public float Scorch;
        }

        private static readonly List<Track> _list = new List<Track>();
        private static readonly List<Actor> _near = new List<Actor>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            for (int i = 0; i < _list.Count; i++)
            {
                DropHalo(_list[i]);
            }
            _list.Clear();
        }

        /// <summary>
        /// Looses a shot of move 'm' from 'from' (where it is drawn) along 'dir' at 'target' (null: straight
        /// on along 'dir'), in the move's colours, and dresses it in 'style'. Returns the shot (null if the
        /// engine had no room for it).
        /// </summary>
        public static Shot Fire(ChoreoMove m, Vector2 from, Vector2 dir, Actor target, ShotSpec spec, Style style, Vector2 goal = default, List<Actor> pool = null,
            float scorch = 0f)
        {
            if (m == null || m.A == null)
            {
                return null;
            }
            Shot s = Shots.Fire(m.A, m, from, dir, target, goal, spec, m.Hue);
            Watch(s, m.A, m.Hue, style, pool);
            if (s != null && scorch > 0f && _list.Count > 0 && _list[_list.Count - 1].S == s)
            {
                _list[_list.Count - 1].Scorch = scorch;
            }
            return s;
        }

        /// <summary>Dresses a shot already in flight (a reflected one, a shot fired some other way).</summary>
        public static void Watch(Shot s, Actor owner, Hue hue, Style style, List<Actor> pool = null)
        {
            if (s == null || owner == null || _list.Count >= 160)
            {
                return;
            }
            _list.Add(new Track
            {
                S = s,
                Style = style,
                Hue = hue,
                LastAge = float.NaN,
                Last = s.Pos + new Vector2(0f, s.Z),
                Seed = Random.Range(0f, 10f),
                Px = s.Px,
                K = s.K,
                Lob = s.Spec.Flight == Flight.Lob,
                TurnTo = s.Spec.Turn,
                Owner = owner,
                OwnerId = owner.Sid(),
                Pool = pool
            });
        }

        private static void Tick(float dt)
        {
            if (_list.Count == 0)
            {
                return;
            }
            try
            {
                for (int i = _list.Count - 1; i >= 0; i--)
                {
                    if (i >= _list.Count)
                    {
                        continue;
                    }
                    Track t = _list[i];
                    if (!Step(t, dt))
                    {
                        _list.RemoveAt(i);
                    }
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("ki flight", e);
                Clear();
            }
        }

        /// <summary>One frame of one shot's dressing. False once the shot is gone.</summary>
        private static bool Step(Track t, float dt)
        {
            Shot s = t.S;
            Vector2 at = s.Pos + new Vector2(0f, s.Z);
            bool vis = Fx.OnScreen(at, 0.3f);
            if (s.Hits > t.Hits)
            {
                t.Hits = s.Hits;
                if (vis)
                {
                    HitFx(t, at);
                }
                Scorch(t);
            }
            // gone: spent, caught, landed, turned, or off its course (its age no longer moves on)
            bool gone = s.Done;
            if (!gone && dt > 0f)
            {
                // (twice in a row: a frame of hit-stop between its tick and ours is not its end)
                t.Stale = t.Seen && Mathf.Approximately(s.Age, t.LastAge) ? t.Stale + 1 : 0;
                gone = t.Stale >= 2;
                t.LastAge = s.Age;
                t.Seen = true;
            }
            if (gone)
            {
                if (vis)
                {
                    EndFx(t, at);
                }
                if (t.Lob && s.Hostile)
                {
                    Scorch(t);   // (a lob ends on the ground it struck)
                }
                DropHalo(t);
                return false;
            }
            if (t.Style == Style.Bullet)
            {
                Halo(t, at, vis && s.Hostile && s.Age >= 0f && t.Stale == 0);
            }
            if (dt <= 0f || s.Age < 0f)
            {
                t.Last = at;
                return true;
            }
            t.T += dt;
            if (t.Style == Style.Swarm)
            {
                Seek(t, dt);
            }
            if (vis && s.Hostile && t.Stale == 0)
            {
                TrailFx(t, at, dt);
            }
            t.Last = at;
            return true;
        }

        /// <summary>A master's shot marks the ground where it strikes (no fire: Ruin's force, Kit.Unsparing for buildings).</summary>
        private static void Scorch(Track t)
        {
            Actor o = t.Owner;
            if (t.Scorch <= 0f || o == null || o.Sid() != t.OwnerId || !o.isAlive())
            {
                return;
            }
            Ruin.Blast(o, t.S.Pos, t.Scorch, Scale.Light, o.current_position, false);
        }

        /// <summary>A bullet's halo: a soft disk of its gold round the bright bead, so it reads as light, not a speck.</summary>
        private static void Halo(Track t, Vector2 at, bool show)
        {
            if (!show)
            {
                DropHalo(t);
                return;
            }
            if (t.Halo == null || !t.Halo.Alive)
            {
                t.Halo = Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(2.6f * t.S.Spec.Size)), 1f, 0.85f), at, t.Px, 1f, FxLayer.Top, 10);
                if (t.Halo == null)
                {
                    return;
                }
                t.Halo.Held = true;
            }
            t.Halo.Pos = at;
            t.Halo.Grad = null;
            t.Halo.R.color = Fx.WithA(t.Hue.Main, 0.55f);
        }

        private static void DropHalo(Track t)
        {
            if (t.Halo != null && t.Halo.Alive)
            {
                t.Halo.Kill();
            }
            t.Halo = null;
        }

        // ------------------------------------------------------------- swarm --

        /// <summary>A swarm orb swings wide before it turns in, weaves, and finds another foe when its own falls.</summary>
        private static void Seek(Track t, float dt)
        {
            Shot s = t.S;
            if (!s.Hostile)
            {
                return;
            }
            // the turn ramps up: a wide swing out, then the curve tightens onto the foe
            s.Spec.Turn = Mathf.Lerp(1.2f, t.TurnTo, Mathf.Clamp01(t.T / 0.55f));
            // a weave of its own
            s.Dir = Choreo.Rotate(s.Dir, Mathf.Sin(t.T * 9f + t.Seed) * 55f * dt).normalized;
            Actor v = s.Target;
            bool ok = v != null && v.Sid() == s.TId && Combat.Living(v) && !v.isInMagnet() && !v.isInsideSomething();
            if (ok || t.Owner == null || t.Owner.Sid() != t.OwnerId || !t.Owner.isAlive())
            {
                return;
            }
            Actor best = null;
            float bd = float.MaxValue;
            if (t.Pool != null)
            {
                for (int i = 0; i < t.Pool.Count; i++)
                {
                    Actor u = t.Pool[i];
                    if (u == null || u == v || !Choreo.Fair(t.Owner, u, out _, true))
                    {
                        continue;
                    }
                    float d = (u.current_position - s.Pos).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = u;
                    }
                }
            }
            if (best == null)
            {
                Choreo.Foes(t.Owner, s.Pos, 6f, _near, true);
                for (int i = 0; i < _near.Count; i++)
                {
                    Actor u = _near[i];
                    if (!Choreo.Fair(t.Owner, u, out _, true))
                    {
                        continue;
                    }
                    float d = (u.current_position - s.Pos).sqrMagnitude;
                    if (d < bd)
                    {
                        bd = d;
                        best = u;
                    }
                }
            }
            if (best != null)
            {
                s.Target = best;
                s.TId = best.Sid();
            }
        }

        // ------------------------------------------------------------ the look --

        private static void TrailFx(Track t, Vector2 at, float dt)
        {
            Shot s = t.S;
            float px = t.Px;
            Hue h = t.Hue;
            t.FxT -= dt;
            switch (t.Style)
            {
                case Style.Bullet:
                    // a long faint tracer under the engine's bright streak
                    if ((at - t.Last).sqrMagnitude > px * px)
                    {
                        Kit.Line(t.Last, at, px, 0.16f, 1, FxLayer.Top, 8, Fx.WithA(h.Edge, 0.55f), Fx.WithA(h.Rim, 0.25f), Fx.WithA(h.Rim, 0f));
                    }
                    if (t.FxT <= 0f)
                    {
                        t.FxT = 0.035f;
                        Particle p = Fx.Spawn(Art.Pixel, at, px, 0.16f, FxLayer.Top, 9);
                        p?.Moving(-s.Dir * Random.Range(8f, 18f) * px + Random.insideUnitCircle * 6f * px, 0f, 4f).Color(Color.white, Fx.WithA(h.Edge, 0.7f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case Style.Swarm:
                    // a comet tail of motes that drift and fade
                    if (t.FxT <= 0f && Shots.Count <= 80)
                    {
                        t.FxT = 0.022f;
                        Particle p = Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.2f * s.Spec.Size)), 1f, 0.3f), at + Random.insideUnitCircle * px, px, 0.32f, FxLayer.Top, 9);
                        p?.Moving(-s.Dir * Random.Range(2f, 6f) * px + Random.insideUnitCircle * 5f * px, Random.Range(0f, 4f) * px, 2.5f)
                            .Sized(1.2f, 0.3f).Color(Fx.WithA(h.Core, 0.95f), Fx.WithA(h.Edge, 0.7f), Fx.WithA(h.Rim, 0f));
                    }
                    break;
                case Style.Disc:
                    // the whirr: a thin ring flickering round the spinning disc, sparks thrown off its rim
                    if (t.FxT <= 0f)
                    {
                        t.FxT = 0.045f;
                        int r = Mathf.RoundToInt(4f * s.Spec.Size * Mathf.Lerp(1f, t.K, 0.6f)) + 2;
                        Particle ring = Fx.Spawn(Art.Ring(r, 1, 0.45f), at, px, 0.07f, FxLayer.Top, 11);
                        ring?.Sized(0.9f, 1.15f).Color(Fx.WithA(h.Core, 0.7f), Fx.WithA(h.Edge, 0f));
                        Vector2 side = Choreo.Perp(s.Dir) * (Random.value < 0.5f ? -1f : 1f);
                        Particle sp = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at + side * r * 0.6f * px, px, 0.14f, FxLayer.Top, 10);
                        if (sp != null)
                        {
                            sp.Moving((side * 0.8f - s.Dir * 0.4f) * Random.Range(50f, 90f) * px, 0f, 6f);
                            sp.FaceVel = true;
                            sp.Color(Color.white, h.Edge, Fx.WithA(h.Edge, 0f));
                        }
                    }
                    break;
                case Style.Reflected:
                    if (t.FxT <= 0f)
                    {
                        t.FxT = 0.04f;
                        Fx.Glint(at + Random.insideUnitCircle * 1.5f * px, px, 0.12f, h.Core, 0.5f);
                    }
                    break;
            }
        }

        private static void HitFx(Track t, Vector2 at)
        {
            Shot s = t.S;
            float px = t.Px;
            Hue h = t.Hue;
            switch (t.Style)
            {
                case Style.Bullet:
                    {
                        // it pierces: a narrow spray flies on through the body
                        int n = Fx.Count(4 + Mathf.RoundToInt(2f * s.Spec.Size));
                        float a0 = Mathf.Atan2(s.Dir.y, s.Dir.x);
                        for (int i = 0; i < n; i++)
                        {
                            float a = a0 + Random.Range(-0.35f, 0.35f);
                            Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at, px, Random.Range(0.1f, 0.18f), FxLayer.Top, 19);
                            if (p == null)
                            {
                                break;
                            }
                            p.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(80f, 150f) * px, 0f, 7f);
                            p.FaceVel = true;
                            p.Color(Color.white, h.Edge, Fx.WithA(h.Rim, 0f));
                        }
                        Kit.Line(at, at + s.Dir * 6f * px * s.Spec.Size, px, 0.07f, 1, FxLayer.Top, 20, Color.white, Fx.WithA(h.Core, 0f));
                        break;
                    }
                case Style.Disc:
                    {
                        // cut lines across what it sliced: a white core in a gold edge
                        Vector2 across = Choreo.Rotate(Choreo.Perp(s.Dir), Random.Range(-25f, 25f));
                        float half = (5f + 2f * s.Spec.Size) * px * Mathf.Lerp(1f, t.K, 0.5f);
                        Kit.Line(at - across * half, at + across * half, px, 0.12f, 2, FxLayer.Top, 21, Fx.WithA(h.Edge, 0.95f), Fx.WithA(h.Rim, 0.6f), Fx.WithA(h.Rim, 0f));
                        Kit.Line(at - across * half * 0.8f, at + across * half * 0.8f, px, 0.08f, 1, FxLayer.Top, 22, Color.white, Fx.WithA(h.Core, 0f));
                        Kit.Sparks(at, px, 4, 50f, 110f, h.Edge);
                        break;
                    }
                case Style.Swarm:
                    Fx.Ring(at, px, 0.18f, 2f, 8f * t.K, 1, 1, FxLayer.Top, 18, 1f, 2.2f, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Edge, 0.5f), Fx.WithA(h.Rim, 0f));
                    break;
            }
        }

        private static void EndFx(Track t, Vector2 at)
        {
            Shot s = t.S;
            float px = t.Px;
            Hue h = t.Hue;
            switch (t.Style)
            {
                case Style.Volley:
                    if (t.Lob)
                    {
                        // it struck the ground: a puff of dust and a scorch of light
                        WorldTile tile = Combat.TileAt(s.Pos);
                        Kit.DustRing(s.Pos, px, tile, 5, 22f, 0.4f, t.K);
                        Fx.Ring(s.Pos, px, 0.22f, 2f, 7f * t.K, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(h.Core, 0.8f), Fx.WithA(h.Edge, 0f));
                    }
                    break;
                case Style.Disc:
                    {
                        Actor o = t.Owner;
                        if (s.Returning && o != null && o.Sid() == t.OwnerId && o.isAlive() && (o.current_position - s.Pos).sqrMagnitude < 1.5f * 1.5f)
                        {
                            // caught: a flash and a ring in the thrower's hand
                            Vector2 hand = Choreo.BodyAt(o, 0.62f);
                            Kit.Flash(hand, px, Mathf.RoundToInt(3f * t.K), 0.06f, Fx.WithA(h.Core, 1f));
                            Fx.Ring(hand, px, 0.16f, 2f, 7f * t.K, 1, 1, FxLayer.Top, 20, 0.45f, 2.2f, Fx.WithA(h.Core, 0.9f), Fx.WithA(h.Edge, 0f));
                            Kit.Sparks(hand, px, 3, 30f, 70f, h.Edge);
                            Choreo.Sound("event:/SFX/HIT/HitMetal", o.current_tile, 0.1f);
                        }
                        else if (s.Hostile)
                        {
                            Kit.Flash(at, px, 2, 0.05f, Fx.WithA(h.Core, 0.8f));
                        }
                        break;
                    }
                case Style.Swarm:
                    if (s.Hits == 0)
                    {
                        Kit.Sparks(at, px, 3, 20f, 50f, h.Edge);
                    }
                    break;
            }
        }

        // ------------------------------------------------------- follow rings --

        /// <summary>
        /// A ring of ki round the feet of 'v' that grows from 'r0' to 'r1' pixels over 'life' and goes
        /// where the body goes (it was sent sliding inside it). 'embers': it sheds sparks of light.
        /// </summary>
        public static void FollowRing(Actor v, Hue h, float px, float life, float r0, float r1, int thick, bool embers)
        {
            if (v == null || !Fx.OnScreen(v.current_position, 0.3f))
            {
                return;
            }
            Particle ring = Fx.Ring(Victims.GroundOf(v), px, life, r0, r1, thick, 1, FxLayer.Ground, 6, Art.Ground, 1.8f,
                Fx.WithA(h.Core, 0.95f), Fx.WithA(h.Edge, 0.75f), Fx.WithA(h.Rim, 0f));
            if (ring == null)
            {
                return;
            }
            long id = v.Sid();
            Action<Particle, float> grow = ring.Step;
            float emberT = 0f;
            ring.Step = (q, dt) =>
            {
                grow?.Invoke(q, dt);
                if (v.Sid() != id || !v.isAlive())
                {
                    return;
                }
                q.Pos = Victims.GroundOf(v);
                if (!embers || dt <= 0f)
                {
                    return;
                }
                emberT -= dt;
                if (emberT <= 0f)
                {
                    emberT = 0.03f;
                    Particle e = Fx.Spawn(Art.Pixel, q.Pos + Random.insideUnitCircle * 2f * px, px, 0.3f, FxLayer.Top, 8);
                    e?.Moving(Random.insideUnitCircle * 6f * px, Random.Range(6f, 14f) * px, 2f).Color(Color.white, Fx.WithA(h.Edge, 0.8f), Fx.WithA(h.Rim, 0f));
                }
            };
        }
    }
}
