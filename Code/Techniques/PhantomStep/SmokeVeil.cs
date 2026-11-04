using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SMOKE VEIL (Phantom Step, Expert). Pressed on all sides, it slaps a palm to the ground and
    /// violet smoke bursts out of the earth in a rolling cloud 2.4/3/3.6 tiles wide. Every foe inside
    /// loses sight of it: dazed 0.6/0.9/1.2 s, whatever it was winding up comes apart and whatever it
    /// was swinging is cut off, and it forgets its target (a foe two tiers above the user sees
    /// through the smoke). The user sinks into the cloud, and strikes from within it: its outline
    /// pops out of the smoke beside one foe after another (2/3/4 blows: an elbow, a knee, a hook, a
    /// side kick; III ends with an uppercut that launches), each time a different side, and it is
    /// back where it started as the smoke thins. Fewer foes: more blows on each.
    /// Planned when two or more foes are within the cloud (or one that is winding up, or throwing a
    /// technique at it, or when it is desperate); worth more surrounded. Damage (x the user's damage): 0.7/0.8/0.9 a blow.
    /// </summary>
    internal sealed class SmokeVeilTech : Technique
    {
        public override string Id => "smoke_veil";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Area;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Evasive | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 20f, 34f, 55f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        public static float Radius(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3f : 3.6f;
        }

        public static int Strikes(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 4;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.8f : 0.9f;
        }

        public static float DazeFor(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.2f;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level) * Choreo.KOf(c.Self);
            _near.Clear();
            int n = c.CountNear(c.Pos, r, _near);
            if (n == 0)
            {
                // (forced with nobody in reach: the smoke is thrown down anyway, facing its target)
                Actor f = c.Forced ? c.ForceTarget ?? c.Focus : null;
                if (f == null)
                {
                    return null;
                }
                Vector2 fd = f.current_position - c.Pos;
                return new Plan { Target = f, Point = c.Pos, Dir = fd.sqrMagnitude > 1e-4f ? fd.normalized : Vector2.right, Count = 0, Score = 1f, AreaRadius = r, Data = true };
            }
            int strikes = Strikes(level);
            float share = Mathf.Min(1f, strikes / (float)n);
            float score = 0f;
            bool pressed = false;
            Actor nearest = null;
            float nd = float.MaxValue;
            for (int i = 0; i < _near.Count; i++)
            {
                Actor v = _near[i];
                if (!c.Allowed(v, out float cm))
                {
                    continue;
                }
                score += c.Value(v, c.Expected(v, Mult(level) * Mathf.Max(1f, strikes / (float)n)) * share, cm) + 6f;
                FoeState s = c.State(v);
                if (s.Charging)
                {
                    score += 18f;   // its wind-up comes apart in the smoke
                    pressed = true;
                }
                else if (s.AimsAtMe && s.Busy)
                {
                    score += 8f;   // a technique coming at it: cut off in the smoke
                    pressed = true;
                }
                float d = c.Dist(v);
                if (d < nd)
                {
                    nd = d;
                    nearest = v;
                }
            }
            if (nearest == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                if (n < 2 && !pressed && !c.Desperate)
                {
                    return null;   // one foe and nothing to cut off: not worth the smoke
                }
                if (c.Surrounded)
                {
                    score += 15f;
                }
            }
            Vector2 dir = nearest.current_position - c.Pos;
            return new Plan
            {
                Target = nearest,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        private static readonly Blow[] Blows = { Blow.Elbow, Blow.Knee, Blow.Hook, Blow.SideKick };

        public override bool Begin(Actor a, Plan p)
        {
            PhantomMove m = PhantomMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float r = Radius(level) * m.Kk;
            Veil veil = new Veil();
            m.Home = m.Stance;
            // the palm goes down...
            m.PoseTo(0.08f, new Shape(14f, 1.12f, 0.86f, 0f, 0.02f), Ease.OutQuad, Tempo.Wind).Shows(Form.Evasive | Form.Area);
            // ...and slaps the ground: the smoke bursts out
            m.PoseTo(0.1f, new Shape(26f, 1.24f, 0.76f, 0f, 0.05f), Ease.OutExpo, Tempo.Act).Shows(Form.Evasive | Form.Area)
                .OnEnter(_ => Burst(m, veil, r, level));
            // it sinks into the cloud
            m.Add(new Beat { Tempo = Tempo.Travel, Dur = 0.08f, To = new Shape(0f, 1.3f, 0.6f, 0f), Ease = Ease.InQuad, Intangible = true, Forms = Form.Evasive | Form.Area })
                .OnEnter(_ =>
                {
                    if (m.Vis)
                    {
                        Fx.Afterimage(m, 0.25f, PhantomKit.Illusion(m, 0.7f), PhantomKit.Illusion(m, 0f));
                    }
                });
            m.Add(new Beat { Tempo = Tempo.Travel, Dur = 0.06f, Hidden = true, Intangible = true, Forms = Form.Evasive | Form.Area });
            // out of the smoke beside one foe after another
            int strikes = Strikes(level);
            for (int i = 0; i < strikes; i++)
            {
                bool last = i == strikes - 1;
                Blow b = last && level >= 3 ? Blow.Uppercut : Blows[i % Blows.Length];
                Hit h = new Hit(b, Mult(level))
                {
                    Hurt = last && level >= 3 ? Hurt.Launch : (Hurt?)null,
                    Lift = last && level >= 3 ? 1.8f : -1f,
                    Push = last && level >= 3 ? 1.2f : -1f,
                    Lethal = true,
                    Finisher = last
                };
                if (last && m.Tier >= Tier.Master)
                {
                    PhantomKit.Crack(h, 0.4f);
                }
                Ambush(m, veil, i, h);
            }
            // back where it started as the smoke thins
            Beat back = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.08f,
                Route = Route.Blink,
                Dest = mm => Combat.SafeSpot(m.Home) ? m.Home : mm.Stance,
                Intangible = true,
                Hidden = true,
                Forms = Form.Evasive,
                Track = false
            };
            back.Exit = mm => PopOut(m);
            m.Add(back);
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>Who is in the smoke (with their ids: pooled units get reused) and whom it struck last.</summary>
        private sealed class Veil
        {
            public readonly List<Actor> Foes = new List<Actor>(8);
            public readonly List<long> Ids = new List<long>(8);
            /// <summary>The blow slot being played (-1: none, the slot was skipped).</summary>
            public int Slot = -1;
        }

        /// <summary>The smoke bursts out: every foe inside is dazed, its attack cut off, its target forgotten.</summary>
        private static void Burst(PhantomMove m, Veil veil, float r, int level)
        {
            Vector2 at = m.Stance;
            WorldTile tile = m.A.current_tile;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile);
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", tile, 0.05f);
            if (m.Vis)
            {
                float px = m.Px;
                PhantomKit.Smoke(m, at, r, level <= 1 ? 30 : level == 2 ? 40 : 52, level <= 1 ? 1.3f : level == 2 ? 1.5f : 1.8f);
                Fx.Ring(at, px, 0.35f, 3f, r / px, 2, 1, FxLayer.Ground, 4, Art.Ground, 2.4f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                Kit.DustRing(at, px, tile, 10, 40f, 0.5f, m.Kk);
                Kit.Flash(at + new Vector2(0f, 2f * px), px, Mathf.RoundToInt(5f * m.Kk), 0.08f, PhantomKit.Illusion(m, 0.9f));
            }
            Ruin.Quake(m.A, at, m.Scale, 0.8f, m.Kk);
            float daze = DazeFor(level);
            List<Actor> list = _near;
            Choreo.Foes(m.A, at, r, list, false, true);
            veil.Foes.Clear();
            veil.Ids.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                Actor u = list[i];
                if (!Choreo.Fair(m.A, u, out _, m.Forced) || (int)Tiers.Of(u) - (int)m.Tier >= 2)
                {
                    continue;   // (two tiers above it, a foe sees through smoke)
                }
                // its attack comes to nothing
                if (Moves.TryGet(u, out Move um) && um.A == u && !(um is Puppet) && !um.Cinematic && !um.Ended)
                {
                    if (um.Charging)
                    {
                        Interrupts.Disperse(um, m.A);
                    }
                    else
                    {
                        Choreo.Kill(um);
                    }
                }
                Combat.Daze(u, daze);
                try
                {
                    u.clearAttackTarget();
                }
                catch
                {
                    // (a unit mid-removal)
                }
                if (m.Vis)
                {
                    Kit.Mark(u, new Color(0.82f, 0.8f, 0.9f, 1f), 0.4f + 0.2f * level, 0.7f);
                }
                if (veil.Foes.Count < 8)
                {
                    veil.Foes.Add(u);
                    veil.Ids.Add(u.Sid());
                }
            }
            if (Dev.On) Dev.Trace("smoke veil " + m.Id + ": " + veil.Foes.Count + " foes lost in the smoke (r " + r.ToString("F1") + ")");
        }

        /// <summary>
        /// One blow from the smoke: it pops out beside the next foe in the cloud (a different side each
        /// time), throws 'h', and sinks back in. Skipped if nobody is left.
        /// </summary>
        private static void Ambush(PhantomMove m, Veil veil, int i, Hit h)
        {
            BlowShape s = ChoreoMove.ShapeOf(h.Blow);
            Beat pop = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.07f,
                Route = Route.Blink,
                Dest = mm => Beside(m, i),
                Intangible = true,
                Hidden = true,
                Forms = s.Forms | Form.Evasive,
                If = mm => Next(m, veil, i)
            };
            pop.Exit = mm => PopOut(m);
            m.Add(pop);
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = s.WindDur * 0.7f, To = s.Wind, Ease = Ease.OutQuad, Forms = s.Forms, If = mm => veil.Slot == i });
            Beat act = new Beat { Tempo = Tempo.Act, Dur = s.ActDur, To = s.Hit, Ease = Ease.OutExpo, Forms = s.Forms, If = mm => veil.Slot == i };
            act.Enter = mm => SmearFx(m, s);
            act.On(s.At, mm =>
            {
                if (m.VOk && m.Touch(m.V))
                {
                    m.Deliver(m.V, h, s, m.ContactPoint(s.Contact), m.Dir);
                }
            });
            m.Add(act);
            Shape relax = new Shape(s.Hit.Rot * 0.6f, Mathf.Lerp(s.Hit.SX, 1f, 0.4f), Mathf.Lerp(s.Hit.SY, 1f, 0.4f), 0f, s.Hit.Lunge * 0.8f);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = s.FollowDur * 1.3f, To = relax, Ease = Ease.OutQuad, Forms = s.Forms, Track = false, If = mm => veil.Slot == i });
            // back into the smoke
            Beat sink = new Beat { Tempo = Tempo.Travel, Dur = 0.05f, Hidden = true, Intangible = true, Forms = Form.Evasive, If = mm => veil.Slot == i };
            sink.Enter = mm =>
            {
                if (m.Vis)
                {
                    Fx.Afterimage(m, 0.16f, PhantomKit.Illusion(m, 0.75f), PhantomKit.Illusion(m, 0f));
                    PhantomKit.Smoke(m, m.Stance, 0.4f * m.Kk, 4, 0.5f, 0.6f);
                }
            };
            m.Add(sink);
        }

        /// <summary>The next foe still in the smoke for blow 'i' (round the list), made the target. False: nobody left.</summary>
        private static bool Next(PhantomMove m, Veil veil, int i)
        {
            veil.Slot = -1;
            int n = veil.Foes.Count;
            for (int j = 0; j < n; j++)
            {
                int idx = (i + j) % n;
                Actor u = veil.Foes[idx];
                if (u == null || u.Sid() != veil.Ids[idx] || !Combat.Living(u) || !Choreo.Fair(m.A, u, out _, true) || u.isInMagnet() || u.isInsideSomething())
                {
                    continue;
                }
                if (Vector2.Distance(u.current_position, m.Home) > Radius(m.Level) * m.Kk + 2f)
                {
                    continue;   // it stumbled out of the smoke
                }
                m.SetVictim(u);
                veil.Slot = i;
                return true;
            }
            return false;
        }

        /// <summary>Beside the target, a different side for each blow.</summary>
        private static Vector2 Beside(PhantomMove m, int i)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 rel = m.Home - m.VG;
            float baseAng = rel.sqrMagnitude > 1e-4f ? Mathf.Atan2(rel.y, rel.x) : 0f;
            for (int t = 0; t < 4; t++)
            {
                float ang = baseAng + (i * 2.2f + 0.9f) + t * 1.57f;
                Vector2 q = Combat.Clamp(m.VG + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * m.Stand);
                if (Combat.SafeSpot(q))
                {
                    return q;
                }
            }
            return m.StandSpot(m.Stand);
        }

        /// <summary>It pops out of the smoke: a ring, a puff, a flash of its outline.</summary>
        private static void PopOut(PhantomMove m)
        {
            if (m.V != null && (m.VG - m.Stance).sqrMagnitude > 1e-4f)
            {
                m.Face(m.VG - m.Stance);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
            if (!m.Vis)
            {
                return;
            }
            Fx.Ring(m.Stance, m.Px, 0.18f, 2f, 7f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.2f, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
            Fx.Afterimage(m, 0.1f, Fx.WithA(Color.white, 0.8f), PhantomKit.Illusion(m, 0f));
            PhantomKit.Smoke(m, m.Stance, 0.35f * m.Kk, 4, 0.45f, 0.6f);
        }

        /// <summary>A blow's smear in violet (a streak for straight blows, a swish for arcs).</summary>
        private static void SmearFx(PhantomMove m, BlowShape s)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.05f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            if (s.ArcR > 0)
            {
                int rr = Mathf.Clamp(Mathf.RoundToInt(s.ArcR * Mathf.Lerp(1f, m.Kk, 0.6f)), 4, 60);
                float offY = s.ArcOff.y < -50f ? -m.Pose.PivotUp + 1.5f : s.ArcOff.y;
                Kit.Swish(m, Art.Arc(rr, s.ArcRatio, s.A0, s.A1, s.ArcThick), m.BeatDur * 1.6f + 0.03f, new Vector2(s.ArcOff.x * m.Fwd, offY), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
            }
            else
            {
                Vector2 from = m.Root(s.Limb);
                Vector2 to = m.V != null ? Vector2.Lerp(m.ContactPoint(s.Contact), from, 0.15f) : from + m.Dir * m.Stand;
                ChoreoFx.Smear(from, to, px, m.BeatDur * 1.6f + 0.03f, 1, s.SmearThick, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
            }
        }
    }
}
