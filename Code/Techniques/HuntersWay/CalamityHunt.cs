using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CALAMITY HUNT (Hunter's Way, God). The hunt of every monster in sight. The God sets its feet,
    /// a ring of the hunter's green closes on them and a mark of light flares over every monster
    /// within reach (the prey are chosen). Then it is gone: a blink, and it is behind the first one,
    /// the killing blow already landing; a blink, the next; one after another in quick succession,
    /// each blow chosen for the creature (a heel driven down on something big, a palm through the
    /// undead, a hook to a beast, a kick that punts a small thing away), a line of light left
    /// between each kill and the next like a hunter's trail drawn on the map. The last kill gets the
    /// impact frame, and the God stands over it. People are never prey; flyers are left to the
    /// harpoon and the flick. I: up to 5 within 9 tiles; II: 7 within 11; III: 9 within 13. Damage
    /// (x the user's damage) per kill: I 2.4, II 3.0, III 3.8, every blow lethal.
    /// </summary>
    internal sealed class CalamityHuntTech : Technique
    {
        public override string Id => "calamity_hunt";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Teleport | Form.Area;
        public override Prey Prey => BigGame.Monsters;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 150f, 200f, 260f };
        public override float[] Cooldown => new[] { 75f, 80f, 90f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Thrown;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 11f : 13f;
        }

        public static int MaxPrey(int level)
        {
            return level <= 1 ? 5 : level == 2 ? 7 : 9;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3f : 3.8f;
        }

        private static readonly List<Actor> _prey = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float r = Radius(level);
            _prey.Clear();
            int max = MaxPrey(level);
            Actor focus = null;
            float big = 0f;
            for (int i = 0; i < c.Enemies.Count && _prey.Count < max; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Prey1(c, v))
                {
                    continue;
                }
                _prey.Add(v);
                if (focus == null || v == c.Focus)
                {
                    focus = v;
                }
                if (Weights.Species(v) >= Weight.Titan)
                {
                    big += 1f;
                }
            }
            int need = c.Linked ? 2 : 3;
            if (_prey.Count < need && !(big > 0f && _prey.Count >= 2) && !c.Forced)
            {
                return null;
            }
            if (_prey.Count == 0)
            {
                if (!c.Forced || c.Enemies.Count == 0)
                {
                    return null;
                }
                _prey.Add(c.ForceTarget ?? c.Enemies[0]);
                focus = _prey[0];
            }
            float mult = Mult(level);
            float score = 0f;
            for (int i = 0; i < _prey.Count; i++)
            {
                Actor v = _prey[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, mult), cm) + 10f;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            List<Actor> others = new List<Actor>(_prey);
            Vector2 dir = focus.current_position - c.Pos;
            return new Plan
            {
                Target = focus,
                Point = focus.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = others.Count,
                Others = others,
                Score = score,
                Data = c.Forced
            };
        }

        /// <summary>A monster on the ground it may hunt (people are never prey; fliers are the harpoon's and the flick's).</summary>
        private static bool Prey1(Ctx c, Actor v)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out _, c.Forced) || !Combat.Grounded(v) || Moves.Locked(v))
            {
                return false;
            }
            if (v.isInLiquid() && !Choreo.Wading(v.current_position))
            {
                return false;
            }
            return c.Forced || BigGame.Monster(v);
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            Hunt hunt = new Hunt(m, p.Others);
            int level = m.Level;
            // the prey are chosen: feet set, a ring closing on them, a mark over every monster
            m.PoseTo(0.14f, new Shape(8f, 1.1f, 0.9f, 0f, -0.02f), Ease.OutQuad, Tempo.Wind).OnEnter(mm => hunt.Mark(mm)).Shows(Form.Hunt | Form.Area);
            m.Hold(level <= 1 ? 0.08f : 0.05f, true, Tempo.Wind);
            int n = Mathf.Min(MaxPrey(level), p.Others != null ? p.Others.Count : 1);
            for (int i = 0; i < n; i++)
            {
                int k = i;
                bool last = i == n - 1;
                m.Do(mm => hunt.Next(mm));
                m.Blink(ChoreoMove.BehindTarget, level <= 1 ? 0.07f : 0.05f);
                hunt.AddKill(k, last);
            }
            m.Do(mm => hunt.End(mm)).Label("calamity_end");
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.16f, To = new Shape(4f, 1.04f, 0.97f, 0f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.26f);
            return m.Start();
        }
    }

    /// <summary>One Calamity Hunt: the marked prey, the blinks, the kill on each, the trail of light.</summary>
    internal sealed class Hunt
    {
        private readonly ChoreoMove _m;
        private readonly List<Actor> _prey = new List<Actor>();
        private readonly List<long> _ids = new List<long>();
        private readonly List<Particle> _marks = new List<Particle>();
        private int _next;
        private bool _any;
        private Vector2 _lastKill;
        private bool _hasLast;

        public Hunt(ChoreoMove m, List<Actor> prey)
        {
            _m = m;
            if (prey != null)
            {
                // the hunting path: always the nearest prey left to where it stands
                Vector2 at = m.Stance;
                List<Actor> left = new List<Actor>(prey);
                while (left.Count > 0)
                {
                    int bi = 0;
                    float bd = float.MaxValue;
                    for (int i = 0; i < left.Count; i++)
                    {
                        float d = (left[i].current_position - at).sqrMagnitude;
                        if (d < bd)
                        {
                            bd = d;
                            bi = i;
                        }
                    }
                    Actor v = left[bi];
                    left.RemoveAt(bi);
                    _prey.Add(v);
                    _ids.Add(v.Sid());
                    at = v.current_position;
                }
            }
        }

        private bool Alive(int i)
        {
            Actor v = _prey[i];
            return v != null && v.Sid() == _ids[i] && Combat.Living(v) && Choreo.Fair(_m.A, v, out _, true) && Combat.Grounded(v);
        }

        /// <summary>A mark of light flares over every chosen prey; the ring closes on its feet.</summary>
        public void Mark(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.1f);
            if (m.Vis)
            {
                Fx.Ring(m.Pose.Ground, m.Px, 0.24f, 16f * m.Kk, 3f, 1, 2, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Core, 0.9f));
                Fx.Glint(m.HeadPos() + new Vector2(m.Fwd * 1.5f, 0f) * m.Px, m.Px, 0.18f, m.Hue.Core, 0.9f);
            }
            for (int i = 0; i < _prey.Count; i++)
            {
                Actor v = _prey[i];
                if (!Alive(i) || !Kit.Visible(v.current_position, 0.3f))
                {
                    continue;
                }
                float vpx = Choreo.PxOf(v);
                float top = BigGame.Size(v).Top;
                Particle p = m.Glue(Fx.Spawn(ChoreoArt.Hex(Mathf.RoundToInt(3f + Mathf.Min(6f, top / Mathf.Max(0.05f, vpx) * 0.08f)), 1),
                    v.current_position + new Vector2(0f, top + 3f * vpx), Mathf.Max(0.08f, vpx), 1f, m.TopLayer, 20));
                if (p == null)
                {
                    continue;
                }
                p.Grad = null;
                p.R.color = Fx.WithA(m.Hue.Core, 0.9f);
                long id = _ids[i];
                float h = top + 3f * vpx;
                p.Step = (q, d) =>
                {
                    if (v.Sid() == id && v.isAlive() && v.hasHealth())
                    {
                        q.Pos = v.current_position + new Vector2(0f, h + Mathf.Sin(q.Age * 8f) * 0.08f);
                    }
                    else
                    {
                        q.R.color = new Color(0f, 0f, 0f, 0f);   // (the prey is taken: its mark goes out)
                    }
                };
                _marks.Add(p);
            }
        }

        /// <summary>The next prey still there (it may have died or fled): the target of the next blink.</summary>
        public void Next(ChoreoMove m)
        {
            while (_next < _prey.Count)
            {
                int i = _next++;
                if (!Alive(i))
                {
                    continue;
                }
                m.SetVictim(_prey[i]);
                return;
            }
            // nobody left to hunt
            if (_any)
            {
                m.Jump("calamity_end");
            }
            else
            {
                m.Lose();
            }
        }

        /// <summary>The kill: a blow chosen for the creature, lethal.</summary>
        public void AddKill(int i, bool last)
        {
            ChoreoMove m = _m;
            m.Add(new Beat { Tempo = Tempo.Wind, Dur = 0.04f, To = new Shape(-10f, 0.94f, 1.06f, 0f, -0.04f), Ease = Ease.OutQuad, Forms = Form.Hunt | Form.Teleport });
            Beat act = new Beat { Tempo = Tempo.Act, Dur = 0.07f, To = new Shape(24f, 1.2f, 0.86f, 0f, 0.14f), Ease = Ease.OutExpo, Forms = Form.Hunt | Form.Teleport | Form.Area };
            act.On(0.5f, mm => Kill(mm, last));
            m.Add(act);
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = last ? 0.12f : 0.04f, To = new Shape(12f, 1.06f, 0.95f, 0f, 0.08f), Ease = Ease.OutQuad, Track = false });
        }

        private void Kill(ChoreoMove m, bool last)
        {
            Actor v = m.V;
            if (!m.VOk || v == null)
            {
                return;
            }
            float reach = m.Stand + 1.2f * m.Kk + v.stats["size"] * 0.5f + 0.4f;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > reach || !m.Touch(v))
            {
                return;
            }
            Prey kind = Preys.Of(v);
            Blow blow;
            Hurt hurt;
            float push;
            if (Weights.Species(v) >= Weight.Fiend || (kind & (Prey.Titan | Prey.Kaiju | Prey.Dragon | Prey.Brute)) != 0)
            {
                blow = Blow.HeelDrop;
                hurt = Hurt.DriveIn;
                push = 0f;
            }
            else if ((kind & Prey.Undead) != 0)
            {
                blow = Blow.Palm;
                hurt = Hurt.Reel;
                push = 1.4f;
            }
            else if ((kind & Prey.Swarm) != 0)
            {
                blow = Blow.SnapKick;
                hurt = Hurt.Launch;
                push = 2.4f;
            }
            else
            {
                blow = (_next & 1) == 0 ? Blow.Hook : Blow.Cross;
                hurt = Hurt.Spin;
                push = 0.8f;
            }
            BlowShape s = ChoreoMove.ShapeOf(blow);
            Vector2 at = m.ContactPoint(s.Contact);
            if (m.Vis)
            {
                ChoreoFx.Smear(m.Root(s.Limb), at, m.Px, 0.08f, 2, 2, m.Hue.Core, m.Hue.Edge);
            }
            m.Deliver(v, new Hit(blow, CalamityHuntTech.Mult(m.Level)) { Hurt = hurt, Push = push, Lethal = true, Finisher = last, Power = last ? 1.6f : 1.1f, Crater = last && blow == Blow.HeelDrop ? 0.9f : 0f },
                s, at, m.Dir);
            _any = true;
            // the trail: a line of light from the last kill to this one
            Vector2 g = Victims.GroundOf(v);
            if (_hasLast && m.Vis)
            {
                Vector2 up = new Vector2(0f, 2f * m.Px);
                Kit.Line(_lastKill + up, g + up, m.Px, 0.7f, 1, FxLayer.Ground, 6, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            }
            _lastKill = g;
            _hasLast = true;
        }

        /// <summary>The hunt is over: the marks go out.</summary>
        public void End(ChoreoMove m)
        {
            for (int i = 0; i < _marks.Count; i++)
            {
                m.Unglue(_marks[i]);
            }
            _marks.Clear();
            if (m.Vis && _any)
            {
                Fx.Glint(m.Hand(Limb.Fist) + new Vector2(0f, 1.5f * m.Px), m.Px, 0.2f, m.Hue.Core, 1f);
            }
        }
    }
}
