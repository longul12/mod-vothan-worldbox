using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DEMON BANE (Hunter's Way, Master). Made for fiends (demons, evil mages, fire elementals,
    /// tumours, aliens). It steps in with a prayer: one hand raised before its face, the other palm
    /// drawn back to the hip while motes of holy light stream into it and a sigil of light opens
    /// on the ground under its feet; then the radiant palm is driven into the demon's chest. The
    /// palm leaves a holy sigil burning on the fiend, turning and flaring brighter for a heartbeat
    /// while the fiend reels, and the sigil detonates: a column of white-gold light bursts up
    /// through it, the fiend is thrown down, and the ground is scorched in a ring. The light only
    /// harms fiends (about x2.5 the palm). II: the burst rings out and brands other fiends within 1.6
    /// tiles too. III: twin palms (left, then right), the sigil doubles, the column falls from the
    /// sky as a pillar and every fiend within 2.4 tiles is struck; the ground is cratered.
    /// Damage (x the user's damage): I 1.0 + 2.2; II 1.1 + 2.9 (others 1.2); III 0.6 + 1.0 + 4.0
    /// (others 1.6). Chains from Close and Down; leaves Down.
    /// </summary>
    internal sealed class DemonBaneTech : Technique
    {
        public override string Id => "demon_bane";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Hunt | Form.Palm | Form.Heavy;
        public override Prey Prey => Prey.Fiend;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 20f, 34f, 55f };
        public override float[] Cooldown => new[] { 10f, 11f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Palm(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.1f : 1.6f;
        }

        public static float Burst(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 2.9f : 4f;
        }

        /// <summary>Other fiends the burst reaches (tiles) and what it deals them.</summary>
        public static float Reach(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 1.6f : 2.4f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Palm(1) + Burst(1), Crowd = 0.05f, Alone = 10f, IntoGuard = 0.9f, Worth = 16f },
            new MeleeWant { Reach = 2.7f, Total = Palm(2) + Burst(2), Crowd = 0.04f, Alone = 12f, IntoGuard = 0.9f, Worth = 20f },
            new MeleeWant { Reach = 3f, Total = Palm(3) + Burst(3), Crowd = 0.03f, Alone = 14f, IntoGuard = 0.9f, Worth = 26f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = BigGame.Pick(c, level, Want[Mathf.Clamp(level, 1, 3) - 1], Prey.Fiend, false);
            if (p == null || c.Forced)
            {
                return p;
            }
            // II/III: the burst brands the other fiends close to it
            if (level >= 2)
            {
                int near = BigGame.CountPrey(c, p.Target.current_position, Reach(level), Prey.Fiend) - 1;
                p.Score += Mathf.Max(0, near) * (level == 2 ? 14f : 20f);
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            DemonBrand brand = new DemonBrand(m);
            int level = m.Level;
            m.StepIn();
            // the prayer: one hand raised before its face, the palm drawn back, light streaming into it
            m.PoseTo(level <= 1 ? 0.16f : 0.14f, new Shape(-12f, 0.92f, 1.08f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind)
                .OnEnter(mm => brand.Pray(mm))
                .Every((mm, k) => brand.Gather(mm, k));
            if (level >= 3)
            {
                // twin palms: the left first (a quick one), then the right with everything behind it
                m.Strike(new Hit(Blow.Palm, 0.6f) { Hurt = Hurt.Flinch, Push = 0.15f, Lethal = false, WindScale = 0.5f, Shows = Form.Palm | Form.Hunt });
            }
            m.Strike(new Hit(Blow.Palm, Palm(level) - (level >= 3 ? 0.6f : 0f))
            {
                Hurt = Hurt.Reel,
                Push = 0.45f,
                Lethal = false,
                Power = 1.25f,
                Shows = Form.Palm | Form.Hunt | Form.Heavy,
                OnLand = (mm, v, r) => brand.Burn(mm, v)
            });
            // the sigil burns on it, turning and flaring brighter as the fiend reels
            m.Hold(level <= 1 ? 0.2f : 0.16f, false, Tempo.Follow).Every((mm, k) => brand.Glow(mm, k)).OnlyIf(mm => brand.Burning);
            m.Do(mm => brand.Detonate(mm));
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, To = new Shape(-8f, 0.96f, 1.06f, 0f, -0.04f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.26f);
            return m.Start();
        }
    }

    /// <summary>One Demon Bane: the prayer's sigil under its feet, the brand on the fiend, the burst.</summary>
    internal sealed class DemonBrand
    {
        private readonly ChoreoMove _m;
        private readonly Hue _holy;
        private Particle _ground, _sigil, _halo;
        private Actor _v;
        private long _vid = -1;
        private float _mote;
        private static readonly List<Actor> _near = new List<Actor>();

        public DemonBrand(ChoreoMove m)
        {
            _m = m;
            _holy = m.Hue.As(Element.Holy);
        }

        /// <summary>The brand is on a fiend that is still there.</summary>
        public bool Burning => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        /// <summary>The prayer: a sigil of light opens on the ground under its feet.</summary>
        public void Pray(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.1f);
            if (!m.Vis)
            {
                return;
            }
            int r = Mathf.RoundToInt((7f + 2f * m.Level) * m.Kk);
            _ground = m.Glue(Fx.Spawn(ChoreoArt.Sigil(r), m.Pose.Ground, m.Px, 1f, FxLayer.Ground, 4));
            if (_ground != null)
            {
                _ground.Snap = true;
                _ground.Grad = null;
                _ground.R.color = Fx.WithA(_holy.Main, 0f);
            }
        }

        /// <summary>Motes of holy light streaming into the drawn-back palm; the ground sigil brightening.</summary>
        public void Gather(ChoreoMove m, float k)
        {
            if (_ground != null && _ground.Alive)
            {
                _ground.Pos = m.Pose.Ground;
                _ground.R.color = Fx.WithA(Color.Lerp(_holy.Main, _holy.Core, k), 0.85f * Mathf.Clamp01(k * 3f));
            }
            _mote -= Clock.Dt;
            if (_mote <= 0f && m.Vis)
            {
                _mote = 0.035f;
                Vector2 palm = m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.18f, -m.Hpx * 0.05f) * m.Px;
                ChoreoFx.Motes(palm, m.Px, 2 + m.Level, 9f * m.Kk, 0.16f, _holy.Core, _holy.Edge);
            }
        }

        /// <summary>The palm lands: the holy sigil is burned into the fiend.</summary>
        public void Burn(ChoreoMove m, Actor v)
        {
            if (v == null || !Combat.Living(v))
            {
                return;
            }
            _v = v;
            _vid = v.Sid();
            if (_ground != null)
            {
                m.Unglue(_ground);
                _ground = null;
            }
            Choreo.Sound("event:/SFX/HIT/HitMetal", v.current_tile, 0.1f);
            if (!m.Vis)
            {
                return;
            }
            float vk = Mathf.Clamp(Choreo.KOf(v), 0.8f, 2.4f);
            // (a brand on its chest, not a shield in front of it: a little smaller than the body)
            int r = Mathf.Clamp(Mathf.RoundToInt((2.5f + 0.5f * m.Level) * vk), 3, 18);
            Vector2 at = Choreo.BodyAt(v, 0.5f);
            _sigil = m.Glue(Fx.Spawn(ChoreoArt.Hex(r, m.Level >= 3 ? 2 : 1), at, m.Px, 1f, m.TopLayer, 22));
            if (_sigil != null)
            {
                _sigil.Grad = null;
                _sigil.R.color = Fx.WithA(_holy.Core, 0.95f);
            }
            if (m.Level >= 3)
            {
                _halo = m.Glue(Fx.Spawn(ChoreoArt.Hex(Mathf.RoundToInt(r * 1.4f) + 1, 1), at, m.Px, 1f, m.TopLayer, 21));
                if (_halo != null)
                {
                    _halo.Grad = null;
                    _halo.R.color = Fx.WithA(_holy.Edge, 0.8f);
                }
            }
            Kit.Flash(at, m.Px, Mathf.RoundToInt(3f * vk), 0.06f, Fx.WithA(_holy.Core, 0.95f), m.TopLayer);
            Kit.Sparks(at, m.Px, Fx.Count(6), 40f, 90f, _holy.Edge);
        }

        /// <summary>The brand turns and flares on the fiend (it follows it as it reels).</summary>
        public void Glow(ChoreoMove m, float k)
        {
            if (!Burning)
            {
                return;
            }
            Vector2 at = Choreo.BodyAt(_v, 0.5f);
            float pulse = 0.6f + 0.4f * Mathf.Sin(m.BeatT * 40f);
            if (_sigil != null && _sigil.Alive)
            {
                _sigil.Pos = at;
                _sigil.Rot += Clock.Dt * (120f + 360f * k);
                _sigil.SX = _sigil.SY = 1f + 0.25f * k;
                _sigil.R.color = Fx.WithA(Color.Lerp(_holy.Main, Color.white, k), Mathf.Lerp(0.7f, 1f, pulse));
            }
            if (_halo != null && _halo.Alive)
            {
                _halo.Pos = at;
                _halo.Rot -= Clock.Dt * 200f;
                _halo.R.color = Fx.WithA(_holy.Edge, 0.5f + 0.4f * k);
            }
            if (m.Vis && Random.value < 0.5f)
            {
                Particle e = Fx.Spawn(Art.Pixel, at + Random.insideUnitCircle * 4f * m.Px, m.Px, 0.3f, m.TopLayer, 20);
                e?.Moving(new Vector2(Random.Range(-6f, 6f), Random.Range(14f, 28f)) * m.Px, 0f, 1f).Color(_holy.Core, Fx.WithA(_holy.Edge, 0.7f), Fx.WithA(_holy.Edge, 0f));
            }
        }

        /// <summary>The sigil detonates: a column of holy light through the fiend (and II/III the fiends near it).</summary>
        public void Detonate(ChoreoMove m)
        {
            if (_sigil != null)
            {
                m.Unglue(_sigil);
                _sigil = null;
            }
            if (_halo != null)
            {
                m.Unglue(_halo);
                _halo = null;
            }
            if (!Burning)
            {
                return;
            }
            Actor v = _v;
            int level = m.Level;
            Vector2 g = Victims.GroundOf(v);
            Vector2 at = Choreo.BodyAt(v, 0.5f);
            Column(m, g, Choreo.KOf(v), 1f);
            if (m.Touch(v))
            {
                Vector2 dir = g - m.Stance;
                m.Deliver(v, new Hit(Blow.Palm, DemonBaneTech.Burst(level))
                {
                    Hurt = Hurt.Knockdown,
                    Push = 0.6f,
                    Lethal = true,
                    Finisher = true,
                    Power = 1.5f,
                    Daze = 0.6f + 0.3f * level
                }, ChoreoMove.ShapeOf(Blow.Palm), at, dir.sqrMagnitude > 1e-4f ? dir.normalized : m.Dir);
            }
            if (level >= 2)
            {
                // the burst rings out: every other fiend close to it is branded and struck
                float r = DemonBaneTech.Reach(level);
                float mult = level == 2 ? 1.2f : 1.6f;
                Choreo.Foes(m.A, g, r, _near);
                for (int i = 0; i < _near.Count; i++)
                {
                    Actor u = _near[i];
                    if (u == v || !Preys.Is(u, Prey.Fiend) || !Choreo.Fair(m.A, u, out float cm, true) || !m.Touch(u, true))
                    {
                        continue;
                    }
                    Vector2 ug = u.current_position;
                    Column(m, ug, Choreo.KOf(u) * 0.75f, 0.7f);
                    HitResult hr = Combat.Hit(m.A, u, mult * cm, true, false, 0f, m);
                    if (Combat.Living(u))
                    {
                        Victims.Hurt(u, m.A, m, Impact.Of(Hurt.Knockdown, ug - g, 0.5f, 1f));
                    }
                    if (Dev.On) Dev.Trace("demon_bane brands " + u.asset.id + "#" + u.Sid() + " x" + (mult * cm).ToString("F2") + (hr.Killed ? " killed" : ""));
                }
                if (m.Vis)
                {
                    Fx.Ring(g, m.Px, 0.4f, 3f, r / m.Px, 2, 1, FxLayer.Ground, 6, Art.Ground, 1.8f, Fx.WithA(Color.white, 0.95f), Fx.WithA(_holy.Edge, 0.7f), Fx.WithA(_holy.Rim, 0f));
                }
            }
            // the ground under it is scorched (III: blasted)
            Ruin.Blast(m.A, g, level >= 3 ? 1f : 0.6f, level >= 3 ? Scale.Heavy : Scale.Light, m.Pose.Ground, false);
            m.SetLink(Link.Down, v);
        }

        /// <summary>A column of white-gold light through a body at 'g' (III: falling from the sky as a pillar).</summary>
        private void Column(ChoreoMove m, Vector2 g, float vk, float power)
        {
            WorldTile tile = Combat.TileAt(g);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.08f);
            if (!Kit.Visible(g, 0.4f))
            {
                return;
            }
            float px = m.Px;
            int level = m.Level;
            float h = (level >= 3 ? 34f : 18f + 4f * level) * vk * power;
            int thick = Mathf.Clamp(Mathf.RoundToInt((2f + level) * vk * power), 2, 9);
            Vector2 top = g + new Vector2(0f, h * px);
            Kit.Line(g, top, px, 0.32f + 0.06f * level, thick + 2, m.TopLayer, 18, Fx.WithA(_holy.Edge, 0.9f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(_holy.Rim, 0f));
            Kit.Line(g, top, px, 0.26f + 0.05f * level, Mathf.Max(1, thick - 1), m.TopLayer, 19, Color.white, Fx.WithA(_holy.Core, 0.85f), Fx.WithA(_holy.Core, 0f));
            Kit.Flash(g + new Vector2(0f, 4f * px * vk), px, Mathf.RoundToInt((4f + level) * vk), 0.1f, Fx.WithA(_holy.Core, 0.95f), m.TopLayer);
            Fx.Ring(g, px, 0.3f, 2f, (8f + 3f * level) * vk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(_holy.Edge, 0.6f), Fx.WithA(_holy.Edge, 0f));
            int n = Fx.Count(6 + 3 * level);
            for (int i = 0; i < n; i++)
            {
                Vector2 off = new Vector2(Random.Range(-3f, 3f) * vk, Random.Range(0f, h * 0.6f)) * px;
                Particle p = Fx.Spawn(Art.Pixel, g + off, px, Random.Range(0.3f, 0.55f), m.TopLayer, 17);
                p?.Moving(new Vector2(Random.Range(-4f, 4f), Random.Range(20f, 45f)) * px, 0f, 1.5f).Sized(1.5f, 0.8f)
                    .Color(Color.white, Fx.WithA(_holy.Core, 0.9f), Fx.WithA(_holy.Edge, 0f));
            }
            if (level >= 3)
            {
                // the pillar comes down out of the sky onto it: a sigil ring on the ground, a burst of embers
                Particle s = Fx.Spawn(ChoreoArt.Sigil(Mathf.RoundToInt(12f * vk)), g, px, 0.5f, FxLayer.Ground, 5);
                if (s != null)
                {
                    s.Snap = true;
                    s.Sized(0.6f, 1.3f).Color(Fx.WithA(Color.white, 0.95f), Fx.WithA(_holy.Main, 0.8f), Fx.WithA(_holy.Edge, 0f));
                }
                Kit.Sparks(g + new Vector2(0f, 3f * px), px, Fx.Count(10), 50f, 110f, _holy.Edge);
            }
        }
    }
}
