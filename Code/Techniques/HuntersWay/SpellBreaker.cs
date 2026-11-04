using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SPELL BREAKER (Hunter's Way, Expert). The witch-hunter's answer to a caster: it crosses the
    /// distance in one blur (nothing can touch it on the way, spells included) and drives the edge of
    /// its hand into the caster's throat: a magic circle flashes up around the caster and shatters
    /// into shards of light, whatever it was gathering is broken (Interrupt: a wind-up is blown
    /// apart), its spell-shield, if it had one, bursts, and it is silenced (the game's spell
    /// silence: no spells for a while), a small broken sigil hanging over its head until its voice
    /// comes back. II: a knee to the body follows the strike. III: a spinning backfist sends it
    /// reeling and the shattering circle silences every caster within two tiles of it. Made for
    /// casters (the Brain uses it on nothing else); against one gathering power it is the
    /// specialist's interrupt. Damage (x the user's damage): I 1.1; II 1.1 + 0.7; III 1.3 + 0.7 +
    /// 0.9. Silence: I 4 s, II 6 s, III 8 s.
    /// </summary>
    internal sealed class SpellBreakerTech : Technique
    {
        public override string Id => "spell_breaker";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Interrupt | Form.Dash;
        public override Form Answers => Form.Charge;
        public override Prey Prey => Prey.Caster;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 14f, 24f, 38f };
        public override float[] Cooldown => new[] { 8f, 8f, 9f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Thrown || link == Link.Scattered;
        }

        private const float Knee = 0.7f, Backfist = 0.9f;

        private static float Edge(int level)
        {
            return level <= 2 ? 1.1f : 1.3f;
        }

        public static float Total(int level)
        {
            return Edge(level) + (level >= 2 ? Knee : 0f) + (level >= 3 ? Backfist : 0f);
        }

        public static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8.5f : 10f;
        }

        private static float Silence(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 6f : 8f;
        }

        private static readonly System.Func<Ctx, Actor, float, int, float> ValueOf = Value;

        private static float Value(Ctx c, Actor v, float cm, int level)
        {
            bool charging = Moves.Charging(v);
            bool silenced = v.hasStatus("spell_silence");
            if (silenced && !charging && !c.Forced)
            {
                return 0f;   // already mute: no need
            }
            // the dash: no wall, no water on the way
            if (c.Dist(v) > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
            {
                return 0f;
            }
            float s = c.Value(v, c.Expected(v, Total(level)), cm);
            if (charging)
            {
                s += 30f + 10f * Tiers.Power(v);   // break it before it lands
            }
            if (!silenced && v.hasSpells())
            {
                s += 14f;
            }
            if (c.Dist(v) > 3f)
            {
                s += 8f;   // nothing else closes the distance on a caster this fast
            }
            return s;
        }

        public override Plan Plan(Ctx c, int level)
        {
            Actor v = HwLoKit.PickRanged(c, this, level, 0f, Range(level), ValueOf, out float score);
            if (v == null || score <= 0f)
            {
                return null;
            }
            if (!c.Forced && (!Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position)))
            {
                return null;   // (a caster in the air or in the water is out of a dash's reach)
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

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            HwLoKit.FitStand(m);
            int level = m.Level;
            // a crouch, then one blur across the distance
            m.Crouch(0.08f, 0.8f);
            m.Dash(ChoreoMove.ToTarget, level >= 3 ? 0.02f : 0.024f).OnlyIf(mm => mm.V != null && Vector2.Distance(mm.Stance, mm.StandSpot(mm.Stand)) > 0.4f * mm.Kk);
            m.Shows(Form.Dash | Form.Interrupt | Form.Hunt);
            // the edge of the hand into its throat
            m.Strike(new Hit(Blow.Palm, Edge(level))
            {
                Hurt = Hurt.SnapBack,
                Push = 0.35f,
                Power = 1.15f,
                Finisher = level == 1,
                ActScale = 0.85f,
                Shows = Form.Palm | Form.Interrupt | Form.Fast | Form.Hunt,
                OnLand = (mm, v, r) => Break(mm, v, level)
            });
            if (level >= 2)
            {
                m.Strike(new Hit(Blow.Knee, Knee) { Hurt = Hurt.Fold, Power = 1.05f, Finisher = level == 2, Daze = level == 2 ? 0.8f : 0f });
            }
            if (level >= 3)
            {
                m.Strike(new Hit(Blow.Backfist, Backfist) { Hurt = Hurt.Reel, Push = 1.4f, Power = 1.25f, Finisher = true, Daze = 0.6f });
            }
            m.Recover(0.22f);
            return m.Start();
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>
        /// The spell breaks: a magic circle flashes up round the caster and shatters, a gathering is
        /// blown apart, a spell-shield bursts, and it is silenced. III: every caster within two tiles too.
        /// </summary>
        private static void Break(ChoreoMove m, Actor v, int level)
        {
            if (v == null || !Combat.Living(v))
            {
                return;
            }
            Mute(m, v, level, true);
            if (level < 3)
            {
                return;
            }
            Vector2 at = Victims.GroundOf(v);
            Choreo.Foes(m.A, at, 2f * m.Kk, _near, true, true);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u != v && Combat.Living(u) && Preys.Is(u, Prey.Caster) && Combat.Allowed(m.A, u))
                {
                    Mute(m, u, level, false);
                }
            }
            if (m.Vis)
            {
                float r = 2f * m.Kk / m.Px;
                Fx.Ring(at, m.Px, 0.35f, 4f, r, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(m.Hue.Core, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            }
        }

        private static void Mute(ChoreoMove m, Actor v, int level, bool main)
        {
            // a gathering broken apart (the blow itself already breaks most: this makes sure)
            if (Moves.Charging(v))
            {
                Interrupts.Break(v, m.A, v.current_position - m.A.current_position, main ? 1.6f : 1f);
            }
            if (v.hasStatus("shield"))
            {
                v.finishStatusEffect("shield");
            }
            float secs = Silence(level);
            v.addStatusEffect("spell_silence", secs);
            Vector2 at = Choreo.BodyAt(v, 0.5f);
            if (Kit.Visible(at))
            {
                float px = Choreo.PxOf(v);
                float r = Mathf.Clamp(Choreo.SpritePx(v) * 0.9f, 6f, 40f);
                // the magic circle flashes up round it and shatters into shards of light
                Particle sig = Fx.Spawn(ChoreoArt.Sigil(Mathf.RoundToInt(r)), Victims.GroundOf(v), px, 0.22f, FxLayer.Ground, 7);
                sig?.Sized(0.7f, 1.25f).Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                int n = Fx.Count(main ? 12 : 7);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f + Random.value * 0.3f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                    Particle s = Fx.Spawn(Art.Pixel, Victims.GroundOf(v) + d * r * 0.8f * px, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 11);
                    if (s == null)
                    {
                        break;
                    }
                    s.SX = Random.Range(1.5f, 2.5f);
                    s.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    s.Moving(d * Random.Range(30f, 60f) * px, Random.Range(10f, 24f) * px, 2f, 40f * px);
                    s.Color(Color.white, Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                }
                Kit.Flash(at, px, main ? 4 : 3, 0.07f, Fx.WithA(Color.white, 0.9f), FxLayer.Top, 26);
                SilenceMark(v, secs, m.Hue.Core);
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", v.current_tile, 0.05f);
            if (Dev.On) Dev.Trace("spell_breaker " + m.Id + " silences " + v.asset.id + "#" + v.Sid() + " for " + secs + " s (spells " + v.hasSpells() + ")");
        }

        /// <summary>A small broken sigil over its head while it is silenced (it follows it; gone when its voice comes back).</summary>
        private static void SilenceMark(Actor v, float secs, Color c)
        {
            float px = Choreo.PxOf(v);
            long id = v.Sid();
            Particle ring = Fx.Spawn(Art.Ring(3, 1, 1f), v.current_position, px, secs, FxLayer.Top, 12);
            Particle bar = Fx.Spawn(Art.Pixel, v.current_position, px, secs, FxLayer.Top, 13);
            if (ring == null || bar == null)
            {
                ring?.Kill();
                bar?.Kill();
                return;
            }
            Color c0 = Fx.WithA(Color.Lerp(c, Color.white, 0.4f), 0.9f);
            ring.Color(c0, c0, c0, Fx.WithA(c0, 0f));
            bar.Color(c0, c0, c0, Fx.WithA(c0, 0f));
            bar.SX = 7f;
            bar.Rot = -45f;
            System.Action<Particle, float> follow = (q, d) =>
            {
                if (v.Sid() != id || !v.isAlive() || !v.hasStatus("spell_silence"))
                {
                    q.Kill();
                    return;
                }
                q.Pos = v.current_position + new Vector2(0f, Mathf.Max(0f, v.position_height) + (Choreo.SpritePx(v) + 4f) * px);
            };
            ring.Step = follow;
            bar.Step = follow;
        }
    }
}
