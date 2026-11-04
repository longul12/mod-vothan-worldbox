using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BONE SHATTER (Hunter's Way, Adept). A blessed blow for the walking dead. The hunter draws its
    /// fist back to the hip and blesses it: pale holy motes stream into the knuckles and a small
    /// ring of light closes on them; then one straight, heavy punch: on the undead the light bursts
    /// through the body in a white cross, bone splinters spray out of it and it is hurled back
    /// (double damage against the undead); one it finishes bursts apart into bones, its pale ghost
    /// rising out of it and fading. II: a jab sets the corpse up first, and the blessed straight
    /// lands on an impact frame. III: an open palm follows into the reeling body and the blessing
    /// bursts out of it in a ring that sears every undead within two tiles. Made for the undead (the
    /// Brain uses it on nothing else). Damage (x the user's damage, doubled on the undead): I 1.3;
    /// II 0.4 + 1.6; III 0.4 + 1.8 + 0.8 (+ 0.6 on the undead around).
    /// </summary>
    internal sealed class BoneShatterTech : Technique
    {
        public override string Id => "bone_shatter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Heavy | Form.Punch;
        public override Prey Prey => Prey.Undead;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 22f, 36f };
        public override float[] Cooldown => new[] { 6f, 7f, 8f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float Jab = 0.4f, PalmBurst = 0.8f, Nova = 0.6f;

        private static float Straight(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.6f : 1.8f;
        }

        public static float Total(int level)
        {
            return Straight(level) + (level >= 2 ? Jab : 0f) + (level >= 3 ? PalmBurst : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.2f, Total = Total(1) * 2f, Crowd = 0.05f, Alone = 6f, IntoGuard = 0.8f, Worth = 10f },
            new MeleeWant { Reach = 2.4f, Total = Total(2) * 2f, Crowd = 0.04f, Alone = 6f, IntoGuard = 0.8f, Worth = 12f },
            new MeleeWant { Reach = 2.6f, Total = Total(3) * 2f, Crowd = 0.03f, Alone = 6f, IntoGuard = 0.8f, Worth = 16f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = HwLoKit.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // III: the more undead stand round it, the more the blessing sears
            if (level >= 3)
            {
                _near.Clear();
                c.CountNear(p.Target.current_position, 2f, _near);
                for (int i = 0; i < _near.Count; i++)
                {
                    Actor u = _near[i];
                    if (u != p.Target && Preys.Is(u, Prey.Undead) && c.Allowed(u, out float cm))
                    {
                        p.Score += c.Value(u, c.Expected(u, Nova * 2f), cm) * 0.8f;
                    }
                }
            }
            return p;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>The blessing's worth on 'v': double on the undead.</summary>
        private static float Blessed(Actor v)
        {
            return v != null && Preys.Is(v, Prey.Undead) ? 2f : 1f;
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
            Hue holy = m.Hue.As(Element.Holy);
            m.StepIn();
            // the blessing: the fist drawn back to the hip, holy motes streaming into the knuckles
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = level <= 1 ? 0.18f : 0.14f,
                To = new Shape(-10f, 0.94f, 1.06f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Forms = Form.Punch | Form.Heavy | Form.Hunt,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Vector2 fist = mm.BodyPos() + new Vector2(-mm.Fwd * 2f, -1f) * mm.Px;
                        ChoreoFx.Motes(fist, mm.Px, Fx.Count(level >= 2 ? 10 : 7), 9f * mm.Kk, mm.BeatDur, holy.Core, Fx.WithA(holy.Edge, 0f));
                        Fx.Ring(fist, mm.Px, mm.BeatDur, 8f * mm.Kk, 1.5f, 1, 1, FxLayer.Over, 9, 1f, 1.8f, Fx.WithA(holy.Core, 0.9f), Fx.WithA(holy.Edge, 0.4f));
                    }
                    Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
                },
                Every = (mm, k) =>
                {
                    if (mm.Vis && Random.value < 0.5f)
                    {
                        Vector2 fist = mm.BodyPos() + new Vector2(-mm.Fwd * 2f, -1f) * mm.Px;
                        Particle g = Fx.Spawn(Art.Pixel, fist + Random.insideUnitCircle * 1.2f * mm.Px, mm.Px, 0.14f, FxLayer.Over, 10);
                        g?.Moving(new Vector2(0f, 8f * mm.Px), 0f, 2f).Color(Color.white, Fx.WithA(holy.Core, 0.8f), Fx.WithA(holy.Edge, 0f));
                    }
                }
            });
            Hit cross = new Hit(Blow.Cross, Straight(level))
            {
                Hurt = Hurt.Reel,
                Push = level <= 1 ? 1f : 1.3f,
                Power = 1.3f,
                Finisher = level >= 2,
                Shows = Form.Punch | Form.Straight | Form.Heavy | Form.Hunt,
                OnLand = (mm, v, r) => Shatter(mm, v, r, holy, 1f)
            };
            Hit jab = new Hit(Blow.Jab, Jab) { Shows = Form.Punch | Form.Fast | Form.Hunt };
            Hit palm = new Hit(Blow.Palm, PalmBurst)
            {
                Hurt = Hurt.Reel,
                Push = 1.6f,
                Power = 1.25f,
                Finisher = true,
                Shows = Form.Palm | Form.Heavy | Form.Hunt,
                OnLand = (mm, v, r) =>
                {
                    Shatter(mm, v, r, holy, 1.3f);
                    Purge(mm, v, holy);
                }
            };
            // each blow is blessed against what it is about to strike (a retarget may change it)
            m.Do(mm =>
            {
                float b = Blessed(mm.V);
                cross.Mult = Straight(level) * b;
                jab.Mult = Jab * b;
                palm.Mult = PalmBurst * b;
            });
            if (level >= 2)
            {
                m.Strike(jab);
            }
            m.Strike(cross);
            if (level >= 3)
            {
                m.Strike(palm);
            }
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>
        /// The blessing bursts through the body: a white cross of light, a ring, and (on the undead)
        /// bone splinters spraying out; a body it finished comes apart, its pale ghost rising.
        /// </summary>
        private static void Shatter(ChoreoMove m, Actor v, HitResult r, Hue holy, float size)
        {
            if (v == null || !m.Vis)
            {
                return;
            }
            bool undead = Preys.Is(v, Prey.Undead);
            Vector2 at = Choreo.BodyAt(v, 0.55f);
            float px = m.Px;
            float s = size * (0.8f + 0.2f * m.Level) * Mathf.Sqrt(m.Kk);
            Kit.Line(at - new Vector2(0f, 7f * s) * px, at + new Vector2(0f, 7f * s) * px, px, 0.14f, 2, m.TopLayer, 16, Color.white, Fx.WithA(holy.Core, 0.7f), Fx.WithA(holy.Edge, 0f));
            Kit.Line(at - new Vector2(4.5f * s, -1.5f * s) * px, at + new Vector2(4.5f * s, 1.5f * s) * px, px, 0.14f, 2, m.TopLayer, 16, Color.white, Fx.WithA(holy.Core, 0.7f), Fx.WithA(holy.Edge, 0f));
            Fx.Ring(at, px, 0.22f, 2f, 12f * s, 2, 1, m.TopLayer, 8, 1f, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(holy.Core, 0.6f), Fx.WithA(holy.Edge, 0f));
            if (!undead)
            {
                return;
            }
            HwLoKit.Shards(at, m.Dir, px, r.Killed ? 16 : 8, r.Killed ? 70f : 50f, HwLoKit.Bone, Fx.Darken(HwLoKit.Bone, 0.25f), r.Killed ? 0.7f : 0.5f);
            if (r.Killed)
            {
                // it comes apart: the pale ghost of it rises out of the bones and fades
                Particle ghost = Trails.Ghost(v, 0.7f, new Color(0.85f, 0.95f, 1f, 0.6f), new Color(0.85f, 0.95f, 1f, 0f));
                ghost?.Moving(new Vector2(0f, 14f * px), 0f, 1.5f);
                Kit.Flash(at, px, 5, 0.1f, Fx.WithA(Color.white, 0.9f), m.TopLayer, 30);
            }
            Choreo.Sound("event:/SFX/HIT/HitStone", v.current_tile, 0.05f);
        }

        /// <summary>III: the blessing bursts out of the struck body in a ring that sears every undead near it.</summary>
        private static void Purge(ChoreoMove m, Actor v, Hue holy)
        {
            Vector2 at = v != null ? Victims.GroundOf(v) : m.VG;
            float r = 2f * m.Kk;
            if (m.Vis)
            {
                Fx.Ring(at, m.Px, 0.35f, 3f, r / m.Px, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.4f, Fx.WithA(Color.white, 0.95f), Fx.WithA(holy.Core, 0.7f), Fx.WithA(holy.Edge, 0f));
            }
            Choreo.Foes(m.A, at, r, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == v || !Preys.Is(u, Prey.Undead) || !Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                {
                    continue;
                }
                Vector2 rel = u.current_position - at;
                HitResult res = Combat.Hit(m.A, u, Nova * 2f * cm, true, false, 0f, m);
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, m.A, m, Impact.Of(Hurt.Reel, rel, 0.9f, 0.9f));
                }
                if (m.Vis)
                {
                    Vector2 body = Choreo.BodyAt(u, 0.55f);
                    Kit.Flash(body, m.Px, 3, 0.08f, Fx.WithA(holy.Core, 0.9f), m.TopLayer, 25);
                    HwLoKit.Shards(body, rel, m.Px, res.Killed ? 10 : 5, 50f, HwLoKit.Bone, Fx.Darken(HwLoKit.Bone, 0.25f));
                }
            }
        }
    }
}
