using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SKY HARPOON (Hunter's Way, Master). Made for what flies (fliers, dragons). A harpoon of
    /// light forms in its hand (motes streaming into a barbed head, the arm drawn back, leaning
    /// away), and it hurls it up at the flier on a chain of links: the harpoon curves after it (it
    /// can be dodged, parried, cut or sent back like any shot). It bites: sparks, the chain snaps
    /// taut from the hand to the barb, and the hunter plants its feet and heaves, hand over hand,
    /// the chain flashing at every pull. A small flier's wings give out and it is dragged down out
    /// of the sky along the chain and slammed into the ground at the hunter's feet (a crater, the
    /// ones it lands on thrown aside). A dragon is too big to drag: it is pulled out of its flight
    /// into a crash landing (it ploughs a trench where it comes down), and once it lies there the
    /// hunter whips the chain down and drives it into the earth. A miss lets the chain go slack and
    /// it is reeled in. I: two heaves. II: three, and a bigger crater. III: a ki-forged harpoon,
    /// three heaves and a slam that rings out, then (a small one) a leap and a heel on it as it
    /// lies. Damage (x the user's damage): the bite I 0.5 / II 0.6 / III 0.7, the slam I 2.1 /
    /// II 3.0 / III 3.6, III's heel 1.2. Leaves Down.
    /// </summary>
    internal sealed class SkyHarpoonTech : Technique
    {
        public override string Id => "sky_harpoon";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Air;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Projectile | Form.Hunt | Form.AntiAir;
        public override Prey Prey => Prey.Flyer | Prey.Dragon;
        public override Scale ScaleAt(int level) => level <= 2 ? Scale.Light : Scale.Heavy;
        public override float[] ManaCost => new[] { 18f, 30f, 48f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        public static float Bite(int level)
        {
            return level <= 1 ? 0.5f : level == 2 ? 0.6f : 0.7f;
        }

        public static float Slam(int level)
        {
            return level <= 1 ? 2.1f : level == 2 ? 3f : 3.6f;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 9f : 10f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            MeleeWant w = new MeleeWant
            {
                Reach = Range(level),
                Total = Bite(level) + Slam(level) + (level >= 3 ? 1.2f : 0f),
                Worthy = true,
                Crowd = 0f,
                Alone = 6f,
                IntoGuard = 1f,
                Worth = 18f
            };
            // something up in the air, a throw's length away at least
            Plan p = BigGame.Pick(c, level, w, Prey.Flyer | Prey.Dragon, true, 1.6f);
            if (p == null)
            {
                return null;
            }
            if (!c.Forced && !BigGame.Airborne(p.Target))
            {
                return null;   // a flier on the ground is anyone's: the harpoon is for the sky
            }
            // nothing else of a ground fighter's reaches a flier: this is the answer to the sky
            if (!c.Forced)
            {
                p.Score += p.Target.isFlying() ? 40f : 20f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            HarpoonLine run = new HarpoonLine(m);
            int level = m.Level;
            // the harpoon forms in its hand, the arm drawn back
            m.PoseTo(level <= 1 ? 0.22f : 0.18f, new Shape(-22f, 0.9f, 1.1f, 0f, -0.08f), Ease.OutQuad, Tempo.Wind)
                .OnEnter(mm => run.Form(mm))
                .Every((mm, k) => run.Forming(mm, k));
            // the throw: up at the flier
            Beat throwB = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(26f, 1.22f, 0.86f, 0f, 0.14f), Ease = Ease.OutExpo, Forms = Forms };
            throwB.On(0.35f, mm => run.Loose(mm));
            m.Add(throwB);
            m.PoseTo(0.08f, new Shape(12f, 1.06f, 0.95f, 0f, 0.08f), Ease.OutQuad, Tempo.Follow);
            // it flies: wait for the bite (a miss, a parry, a cut: the chain goes slack and it ends)
            m.WaitUntil(mm => run.Resolved, 1.6f).Every((mm, k) => run.Flying(mm));
            m.Do(mm => run.AfterFlight(mm));
            // the heaves: hand over hand, the chain snapping taut at every pull
            int heaves = level <= 1 ? 2 : 3;
            for (int i = 0; i < heaves; i++)
            {
                int n = i;
                bool last = i == heaves - 1;
                m.PoseTo(0.07f, new Shape(-30f, 1.14f, 0.86f, 0f, -0.12f), Ease.OutExpo, Tempo.Act)
                    .OnEnter(mm => run.Heave(mm, n, last))
                    .Every((mm, k) => run.Taut(mm, 1f - k))
                    .OnlyIf(mm => run.Bitten);
                m.PoseTo(0.11f, new Shape(-8f, 0.98f, 1.03f, 0f, -0.03f), Ease.OutQuad, Tempo.Follow)
                    .Every((mm, k) => run.Taut(mm, 0f))
                    .OnlyIf(mm => run.Bitten);
            }
            // a dragon: held on the chain until it has crashed down, then the chain is whipped down on it
            m.WaitUntil(mm => run.Landed, 1.5f).Every((mm, k) => run.Taut(mm, 0f)).OnlyIf(mm => run.Bitten && run.Giant);
            m.PoseTo(0.12f, new Shape(-26f, 0.88f, 1.14f, 0f, -0.06f), Ease.OutQuad, Tempo.Wind)
                .Every((mm, k) => run.Taut(mm, 0f))
                .OnlyIf(mm => run.Bitten && run.Giant);
            Beat whip = new Beat { Tempo = Tempo.Act, Dur = 0.09f, To = new Shape(32f, 1.2f, 0.84f, 0f, 0.1f), Ease = Ease.OutExpo, Forms = Form.Hunt | Form.Heavy | Form.Overhead };
            whip.If = mm => run.Bitten && run.Giant;
            whip.Every = (mm, k) => run.Taut(mm, 1f - k);
            whip.On(0.5f, mm => run.SlamGiant(mm));
            m.Add(whip);
            // a small one: dragged down along the chain, it crashes at the hunter's feet
            m.WaitUntil(mm => run.Crashed, 1f).Every((mm, k) => run.Taut(mm, 0f)).OnlyIf(mm => run.Bitten && !run.Giant);
            m.Do(mm => run.Reel(mm));
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(10f, 1.1f, 0.92f, 0f, 0.02f), Ease = Ease.OutQuad, Track = false });
            if (level >= 3)
            {
                // III: a leap onto the one it brought down and a heel as it lies
                m.Do(mm =>
                {
                    if (!run.Bitten || run.Giant || !mm.VOk || Vector2.Distance(mm.Stance, mm.VG) > 3f * mm.Kk)
                    {
                        mm.JumpToRecovery();
                    }
                });
                m.Leap(ChoreoMove.ToTarget, 1.2f, 0.26f, 0f, 0.6f);
                m.Strike(new Hit(Blow.HeelDrop, 1.2f) { Hurt = Hurt.DriveIn, Crater = 0.9f, Finisher = true, Lethal = true });
            }
            m.Recover(0.28f);
            return m.Start();
        }
    }

    /// <summary>One Sky Harpoon: the harpoon in its hand, the shot in flight, the chain, the heaves and the slam.</summary>
    internal sealed class HarpoonLine
    {
        private readonly ChoreoMove _m;
        private readonly Actor _v;
        private readonly long _vid;
        private readonly HuntChain _chain;
        private Shot _shot;
        private Particle _held, _barb;
        private bool _loosed, _resolved, _bit, _giant, _thrown, _slammed;
        private float _mote, _thrownAt, _crashIn = 0.5f;
        private Vector2 _slamAt;

        public HarpoonLine(ChoreoMove m)
        {
            _m = m;
            _v = m.V;
            _vid = _v != null ? _v.Sid() : -1;
            _chain = new HuntChain(m);
        }

        private bool VAlive => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        /// <summary>The harpoon has hit, missed or been turned.</summary>
        public bool Resolved => _resolved;

        /// <summary>It bit, and what it bit is still there on the line.</summary>
        public bool Bitten => _bit && VAlive;

        /// <summary>Too big to drag down (a dragon, a giant): it is pulled into a crash landing instead.</summary>
        public bool Giant => _giant;

        /// <summary>The dragon has come down (or the wait is over).</summary>
        public bool Landed => !VAlive || !_v.isFlying();

        /// <summary>The small one it threw down has crashed (or is gone).</summary>
        public bool Crashed => !VAlive || !_thrown || _m.T - _thrownAt > _crashIn;

        /// <summary>Where the barb sits on its body.</summary>
        private Vector2 Pin()
        {
            if (!VAlive)
            {
                return _m.Hand(Limb.Palm);
            }
            if (BigGame.IsDragon(_v) || _v.asset.has_avatar_prefab)
            {
                Wingbreaker.Box b = BigGame.Size(_v);
                return _v.current_position + new Vector2(b.HeadX * 0.4f, b.Center);
            }
            return Choreo.BodyAt(_v, 0.5f);
        }

        /// <summary>The harpoon of light forms in its hand.</summary>
        public void Form(ChoreoMove m)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.1f);
            if (!m.Vis)
            {
                return;
            }
            _held = m.Glue(Fx.Spawn(m.Level >= 3 ? ChoreoArt.LightSword(9) : ChoreoArt.Spike(3, 6), m.Hand(Limb.Palm), m.Px, 1f, FxLayer.Over, 9));
            if (_held != null)
            {
                _held.Grad = null;
                _held.R.color = Fx.WithA(m.Hue.Core, 0f);
            }
        }

        /// <summary>It brightens in the drawn-back hand, motes streaming into it.</summary>
        public void Forming(ChoreoMove m, float k)
        {
            Vector2 hand = m.BodyPos() + new Vector2(-m.Fwd * m.Hpx * 0.2f, m.Hpx * 0.32f) * m.Px;
            if (_held != null && _held.Alive)
            {
                _held.Pos = hand;
                Vector2 aim = VAlive ? Pin() - hand : m.Dir;
                _held.Rot = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;
                _held.R.color = Fx.WithA(Color.Lerp(m.Hue.Main, m.Hue.Core, k), Mathf.Clamp01(k * 2.5f));
            }
            _mote -= Clock.Dt;
            if (_mote <= 0f && m.Vis)
            {
                _mote = 0.04f;
                ChoreoFx.Motes(hand, m.Px, 1 + m.Level, 8f * m.Kk, 0.14f, m.Hue.Core, m.Hue.Edge);
            }
        }

        /// <summary>The throw: the harpoon flies on its chain (a shot of the engine's: it can be dodged, parried, cut or sent back).</summary>
        public void Loose(ChoreoMove m)
        {
            if (_held != null)
            {
                m.Unglue(_held);
                _held = null;
            }
            if (!VAlive)
            {
                _resolved = true;
                return;
            }
            Vector2 from = m.Hand(Limb.Palm) + new Vector2(0f, m.Hpx * 0.2f * m.Px);
            Vector2 to = Pin();
            float d = Vector2.Distance(from, to);
            ShotSpec spec = new ShotSpec
            {
                Kind = ShotKind.Harpoon,
                Flight = Flight.Homing,
                Speed = 26f,
                Turn = 9f,
                Range = d + 6f,
                Size = m.Level >= 3 ? 1.4f : 1.1f,
                Mult = SkyHarpoonTech.Bite(m.Level),
                Hurt = Hurt.None,
                Push = 0f,
                Pierce = 0
            };
            _shot = Shots.Fire(m.A, m, from, to - from, _v, Vector2.zero, spec, m.Hue);
            _loosed = true;
            if (_shot == null)
            {
                _resolved = true;
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            if (m.Vis)
            {
                Kit.SpeedLines(from, to - from, m.Px, 3, 3f, 0.12f, Fx.WithA(m.Hue.Edge, 0.85f));
                Kit.Flash(from, m.Px, 3, 0.05f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer);
            }
        }

        /// <summary>While it flies: did it bite, or is it gone (missed, parried, cut, turned back)?</summary>
        public void Flying(ChoreoMove m)
        {
            if (_resolved || !_loosed)
            {
                return;
            }
            if (_shot == null)
            {
                _resolved = true;
                return;
            }
            if (_shot.Hits > 0 && _shot.A == m.A && VAlive)
            {
                _resolved = true;
                _bit = true;
                Bite(m);
                return;
            }
            bool flying = false;
            var all = Shots.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == _shot)
                {
                    flying = true;
                    break;
                }
            }
            if (!flying || _shot.A != m.A || !_shot.Hostile || !VAlive)
            {
                _resolved = true;   // spent without biting, or someone sent it back: the line goes slack
            }
        }

        /// <summary>The barb is in: sparks, the chain from the hand to it, a beat of the line snapping taut.</summary>
        private void Bite(ChoreoMove m)
        {
            _shot = null;
            _giant = !CanDrag(_v, m.A);
            Vector2 at = Pin();
            Choreo.Sound("event:/SFX/HIT/HitMetal", _v.current_tile);
            if (m.Vis)
            {
                Kit.Sparks(at, m.Px, Fx.Count(7), 50f, 110f, m.Hue.Edge);
                Kit.Flash(at, m.Px, 3, 0.06f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
                _barb = m.Glue(Fx.Spawn(m.Level >= 3 ? ChoreoArt.LightSword(7) : ChoreoArt.Spike(3, 4), at, m.Px, 1f, FxLayer.Over, 11));
                if (_barb != null)
                {
                    _barb.Grad = null;
                    _barb.R.color = m.Hue.Core;
                }
            }
            Victims.Flash(_v, Color.white, 0.06f);
            m.HoldFor(0.05f * m.ImpactK);
            if (Dev.On) Dev.Trace("sky_harpoon bites " + _v.asset.id + "#" + _vid + (_giant ? " (giant: pulled into a landing)" : " (dragged down)"));
        }

        /// <summary>No bite: it ends here (the hunter recovers with a glint).</summary>
        public void AfterFlight(ChoreoMove m)
        {
            if (!_bit)
            {
                _chain.Kill();
                m.Lose();
            }
        }

        /// <summary>Can the chain drag it out of the sky (not a dragon or a giant, nothing the game holds)?</summary>
        private static bool CanDrag(Actor v, Actor by)
        {
            if (v == null || Weights.Heavy(v) || BigGame.IsDragon(v) || v.is_immovable || ControllableUnit.isControllingUnit(v))
            {
                return false;
            }
            ActorAsset s = v.asset;
            return s != null && s.can_be_moved_by_powers && s.has_sprite_renderer && !s.has_avatar_prefab && !s.ignore_generic_render && !s.is_boat
                && v.actor_scale <= Mathf.Max(0.3f, by.actor_scale * 2.4f);
        }

        /// <summary>The chain from its hand to the barb ('pull' 1: snapped taut, flashing; 0: a little slack).</summary>
        public void Taut(ChoreoMove m, float pull)
        {
            if (!Bitten)
            {
                _chain.Kill();
                return;
            }
            Vector2 hand = m.Hand(Limb.Fist);
            Vector2 at = Pin();
            if (_barb != null && _barb.Alive)
            {
                _barb.Pos = at;
                Vector2 d = at - hand;
                _barb.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
            }
            Color c = Color.Lerp(m.Hue.Edge, Color.white, Mathf.Clamp01(pull));
            _chain.Draw(hand, at, (1f - pull) * 0.25f * m.Kk, c, m.Hue.Core);
            Combat.Pin(_v, 0.4f);
        }

        /// <summary>A heave: the line snaps, the flier is wrenched toward it; the last heave brings it down.</summary>
        public void Heave(ChoreoMove m, int i, bool last)
        {
            if (!Bitten)
            {
                return;
            }
            Vector2 at = Pin();
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.06f);
            if (m.Vis)
            {
                Kit.Sparks(at, m.Px, 3, 40f, 80f, m.Hue.Edge);
                ChoreoFx.HeelDust(m.Pose.Ground, -m.Dir, m.Px, m.A.current_tile, m.Kk);
                Kit.SpeedLines(Vector2.Lerp(m.BodyPos(), at, 0.5f), m.BodyPos() - at, m.Px, 2, 3f, 0.1f, Fx.WithA(m.Hue.Core, 0.8f));
            }
            _v.startShake(0.15f, 0.12f, true, true);
            Victims.Flash(_v, Color.white, 0.04f);
            if (_giant)
            {
                if (i == 0)
                {
                    // the dragon is pulled out of its flight: down it goes, ploughing a trench where it lands
                    int force = Ruin.Force(m.Tier, m.Scale);
                    WingbreakerGround.Down(_v, 1.6f + 0.8f * m.Level, m.A, force, 1f + 0.3f * m.Level);
                }
                return;
            }
            if (!last)
            {
                return;
            }
            // the last heave: its wings give out and it is dragged down to the hunter's feet
            if (_v.isFlying())
            {
                _v.setFlying(false);
            }
            _slamAt = m.Ahead(1.2f * m.Kk + _v.stats["size"] * 0.5f);
            // (the engine's throw: 0.32 s and a little more per tile of the arc)
            _crashIn = 0.36f + 0.055f * Vector2.Distance(_v.current_position, _slamAt);
            HitResult r = Combat.Hit(m.A, _v, SkyHarpoonTech.Slam(m.Level) * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            Impact im = Impact.Of(Hurt.Thrown, _slamAt - _v.current_position, 0f, 1.6f);
            im.To = _slamAt;
            im.Owed = MeteorSlam.AtDeathsDoor(_v);
            im.Crater = m.Level >= 2 ? 0.9f : 0.6f;
            im.AreaR = m.Level >= 2 ? 1.3f : 0.9f;
            im.AreaMult = 0.4f * SkyHarpoonTech.Slam(m.Level);
            im.Daze = 1f + 0.3f * m.Level;
            im.Spin = 540f;
            if (Victims.Hurt(_v, m.A, m, im) == null)
            {
                // it can't be dragged after all (held by another, still too high): it is rocked and grounded where it is
                _giant = true;
                BigGame.Topple(_v, 1.2f + 0.4f * m.Level, m.A, 0, 0f);
                _slammed = true;
            }
            _thrown = true;
            _thrownAt = m.T;
            m.SetLink(Link.Down, _v);
        }

        /// <summary>The chain whipped down on a dragon that has crashed: it is driven into the earth.</summary>
        public void SlamGiant(ChoreoMove m)
        {
            if (_slammed || !Bitten)
            {
                return;
            }
            _slammed = true;
            Vector2 at = Pin();
            Vector2 g = _v.current_position;
            if (m.Touch(_v))
            {
                m.Deliver(_v, new Hit(Blow.Palm, SkyHarpoonTech.Slam(m.Level)) { Hurt = Hurt.DriveIn, Lethal = true, Finisher = true, Power = 1.6f, Daze = 1f },
                    ChoreoMove.ShapeOf(Blow.Palm), at, Vector2.down);
            }
            // a pulse runs down the chain and the ground under it bursts
            if (m.Vis)
            {
                Kit.Line(m.Hand(Limb.Fist), at, m.Px, 0.14f, 2, m.TopLayer, 16, Color.white, Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
            }
            BigGame.Thud(m, g, Mathf.Max(1.6f, BigGame.Size(_v).Half + 0.8f), 1.2f + 0.2f * m.Level, m.Level >= 2, _v);
            if (m.Level >= 3)
            {
                m.AreaBlow(g, BigGame.Size(_v).Half + 1.4f, 0.8f, _v, Hurt.Knockdown, 0.9f);
            }
            if (Combat.Living(_v))
            {
                BigGame.Topple(_v, 1.6f + 0.6f * m.Level, m.A, 0, 0f);
            }
            m.SetLink(Link.Down, _v);
        }

        /// <summary>The line is reeled in: the chain and the barb go.</summary>
        public void Reel(ChoreoMove m)
        {
            _chain.Kill();
            if (_barb != null)
            {
                m.Unglue(_barb);
                _barb = null;
            }
            if (_bit && m.Vis)
            {
                Vector2 hand = m.Hand(Limb.Fist);
                Fx.Glint(hand + new Vector2(0f, 1.5f * m.Px), m.Px, 0.14f, m.Hue.Core, 0.8f);
            }
        }
    }

    /// <summary>
    /// A chain of links drawn between two points every frame (a harpoon's line, a lasso), sagging when
    /// slack, made of the move's own held particles (they die with the move).
    /// </summary>
    internal sealed class HuntChain
    {
        private const int Max = 56;
        private readonly ChoreoMove _m;
        private readonly Particle[] _links = new Particle[Max];
        private readonly int[] _gen = new int[Max];

        public HuntChain(ChoreoMove m)
        {
            _m = m;
        }

        public void Draw(Vector2 from, Vector2 to, float sag, Color a, Color b)
        {
            float px = _m.Px;
            Vector2 d = to - from;
            float len = d.magnitude;
            if (!_m.Vis || len < px)
            {
                Hide(0);
                return;
            }
            int n = Mathf.Clamp(Mathf.RoundToInt(len / (px * 3.2f)), 1, Max);
            for (int i = 0; i < n; i++)
            {
                Particle p = _links[i];
                if (p == null || !p.Alive || p.Gen != _gen[i])
                {
                    p = _m.Glue(Fx.Spawn(ChoreoArt.ChainLink(), from, px, 1f, FxLayer.Over, 10));
                    if (p == null)
                    {
                        break;
                    }
                    _links[i] = p;
                    _gen[i] = p.Gen;
                }
                float u = (i + 0.5f) / n;
                Vector2 pos = Vector2.Lerp(from, to, u) + Vector2.down * sag * 4f * u * (1f - u);
                // each link turns along the curve
                float du = 1f / n;
                Vector2 next = Vector2.Lerp(from, to, Mathf.Min(1f, u + du)) + Vector2.down * sag * 4f * Mathf.Min(1f, u + du) * (1f - Mathf.Min(1f, u + du));
                Vector2 t = next - pos;
                p.Pos = pos;
                p.Rot = t.sqrMagnitude > 1e-8f ? Mathf.Atan2(t.y, t.x) * Mathf.Rad2Deg : Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                p.Grad = null;
                p.R.color = Color.Lerp(a, b, (i & 1) == 0 ? 0.35f : 0f);
            }
            Hide(n);
        }

        /// <summary>Hides the links from 'from' on (they stay ready for the next frame).</summary>
        private void Hide(int from)
        {
            for (int i = from; i < Max; i++)
            {
                Particle p = _links[i];
                if (p != null && p.Alive && p.Gen == _gen[i])
                {
                    p.Grad = null;
                    p.R.color = new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        public void Kill()
        {
            for (int i = 0; i < Max; i++)
            {
                Particle p = _links[i];
                if (p != null && p.Alive && p.Gen == _gen[i])
                {
                    _m.Unglue(p);
                }
                _links[i] = null;
            }
        }
    }
}
