using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// STARE-DOWN (Experts and up, and every rematch between rivals). Before anything happens,
    /// they stop and look at each other.
    ///
    ///   WALK  0.5-1.2  met from afar (over 4.4 tiles apart): both walk in, slow and deliberate,
    ///                  eyes locked, a puff at every footfall, until 3.6 tiles separate them
    ///   SETTLE 0.18-0.32  both turn to face each other and plant their feet (too close, or one
    ///                  above the other on screen: a hop back or aside onto side-on marks 2.4+ apart)
    ///   HOLD  0.6-1.2  stillness: deep guard, slow breathing; manga focus lines converge on each in
    ///                  turn; the wind blows between them (speed lines, drifting dust); the crowd
    ///                  parts (untiered units within 2 tiles step back one); a glint on one, then
    ///                  the other; II+: a faint glow in their colours
    /// </summary>
    internal sealed class StareDown : Exchange
    {
        private const int Walk = 0, SettleIn = 1, HoldOn = 2;

        private Vector2 _sa, _sb;
        private float _dur, _focus, _wind, _tWalk, _tSettle = 0.18f, _step;
        private bool _windAtoB, _ga, _gb, _parted, _hop, _walk;

        public override string Id => "stare_down";

        public override Form Forms => Form.Guard;

        public override bool Intense => false;

        public override bool Plan()
        {
            float dist = (B0 - A0).magnitude;
            _sa = A0;
            _sb = B0;
            _hop = _walk = false;
            _tSettle = 0.18f;
            // a stare-down reads side-on, 2.4-3.6 tiles apart: too close, too far, or one above the
            // other on screen (bodies overlapping), and they take their places first
            bool sideOn = Mathf.Abs(B0.x - A0.x) >= Mathf.Abs(B0.y - A0.y) * 1.2f;
            float gap = Mathf.Clamp(dist, 2.4f * K, 3.6f * K);
            if ((dist < 1.6f * K || dist > 4.4f * K || !sideOn) && DuelGeo.Stance(A0, B0, gap, out Vector2 wa, out Vector2 wb))
            {
                float far = Mathf.Max((wa - A0).magnitude, (wb - B0).magnitude);
                if (far > 0.15f)
                {
                    _sa = wa;
                    _sb = wb;
                    if (dist > 4.4f * K)
                    {
                        // met from afar: they walk in, slow and deliberate (masters don't dawdle)
                        _walk = true;
                        _tWalk = Mathf.Clamp(far / (2.2f * K), 0.5f, 1.2f) * (D.Flow ? 0.75f : 1f);
                    }
                    else
                    {
                        // a hop back, or aside, onto their marks
                        _hop = true;
                        _tSettle = Mathf.Clamp(0.16f + 0.08f * far, 0.18f, 0.32f);
                    }
                }
            }
            // the stillness: the strongest read each other in a glance
            _dur = (D.Flow ? Random.Range(0.45f, 0.75f) : Random.Range(0.6f, 1.2f)) + (D.Rivals ? 0.3f : 0f);
            _windAtoB = Random.value < 0.5f;
            Ph = _walk ? Walk : SettleIn;
            return true;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Walk:
                    {
                        // slow, deliberate strides: easing in and out, a bob and a puff at every footfall
                        float k = Mathf.Clamp01(Pt / _tWalk);
                        float e = Anim.InOutSine(k);
                        P.Pose.Ground = Vector2.Lerp(A0, _sa, e);
                        Q.Pose.Ground = Vector2.Lerp(B0, _sb, e);
                        FaceEach();
                        float steps = Pt * 8.5f;
                        float bob = Mathf.Abs(Mathf.Sin(steps));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = 0.025f * K * bob;
                            m.Pose.Rot = 3f + 2f * bob;
                            m.Pose.SX = 1f + 0.02f * (1f - bob);
                            m.Pose.SY = 1f - 0.03f * (1f - bob);
                        }
                        int s = (int)(steps / Mathf.PI);
                        if (s > (int)_step && k < 0.95f)
                        {
                            _step = s;
                            DuelFx.Puff(P.Pose.Ground, P.Px, Combat.TileAt(P.Pose.Ground), 1, 5f);
                            DuelFx.Puff(Q.Pose.Ground, Q.Px, Combat.TileAt(Q.Pose.Ground), 1, 5f);
                        }
                        if (L >= 2)
                        {
                            float g = 0.05f * k;
                            P.Glow = Fx.WithA(P.Pal.Edge, g);
                            Q.Glow = Fx.WithA(Q.Pal.Edge, g);
                        }
                        if (k >= 1f)
                        {
                            Next(SettleIn);
                        }
                        break;
                    }
                case SettleIn:
                    {
                        float k = Mathf.Clamp01(Pt / _tSettle);
                        float e = Anim.OutQuad(k);
                        Vector2 fa = _walk ? _sa : A0, fb = _walk ? _sb : B0;
                        P.Pose.Ground = Vector2.Lerp(fa, _sa, e);
                        Q.Pose.Ground = Vector2.Lerp(fb, _sb, e);
                        float hop = _hop ? 0.14f * K * Mathf.Sin(Mathf.PI * k) : 0f;
                        P.Pose.Height = Q.Pose.Height = hop;
                        FaceEach();
                        float sq = Mathf.Sin(Mathf.PI * k);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = _hop ? -10f * sq : 4f * e;
                            m.Pose.SX = 1f + 0.06f * sq;
                            m.Pose.SY = 1f - 0.08f * sq;
                        }
                        if (k >= 1f)
                        {
                            if (_hop || _walk)
                            {
                                DuelFx.Puff(_sa, P.Px, Combat.TileAt(_sa));
                                DuelFx.Puff(_sb, Q.Px, Combat.TileAt(_sb));
                            }
                            Next(HoldOn);
                        }
                        break;
                    }
                default:
                    {
                        P.Pose.Ground = _sa;
                        Q.Pose.Ground = _sb;
                        FaceEach();
                        Guard(P, T, 0f, 1.25f);
                        Guard(Q, T, 1.9f, 1.25f);
                        if (L >= 2)
                        {
                            float g = 0.07f + 0.05f * Mathf.Sin(T * 4f);
                            P.Glow = Fx.WithA(P.Pal.Edge, g);
                            Q.Glow = Fx.WithA(Q.Pal.Edge, g);
                        }
                        if (!_parted && Pt >= 0.05f)
                        {
                            _parted = true;
                            D.PartCrowd();
                        }
                        if (Vis)
                        {
                            _focus -= dt;
                            if (_focus <= 0f)
                            {
                                _focus = 0.13f;
                                Duelist m = ((int)(Pt / 0.13f) & 1) == 0 ? P : Q;
                                Fx.FocusLines(m.BodyPos(), m.Px, Fx.Count(4), 11f * K, 20f * K, 0.13f);
                            }
                            _wind -= dt;
                            if (_wind <= 0f)
                            {
                                _wind = 0.07f;
                                Vector2 from = _windAtoB ? _sa : _sb, to = _windAtoB ? _sb : _sa;
                                Color line = L >= 2 ? Fx.WithA(Color.Lerp(Color.white, Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f), 0.35f), 0.55f) : new Color(1f, 1f, 1f, 0.5f);
                                DuelFx.Wind(from, to, px, Combat.TileAt((from + to) * 0.5f), line);
                            }
                        }
                        float k = Pt / _dur;
                        if (!_ga && k >= 0.45f)
                        {
                            _ga = true;
                            Shine(P);
                        }
                        if (!_gb && k >= 0.62f)
                        {
                            _gb = true;
                            Shine(Q);
                        }
                        if (k >= 1f)
                        {
                            P.Glow = Q.Glow = DuelFx.Clear;
                            Done = true;
                        }
                        break;
                    }
            }
        }

        private void Shine(Duelist m)
        {
            if (!Vis)
            {
                return;
            }
            // a ting on the weapon side, up by the head
            Vector2 off = new Vector2(m.Fwd * 3f * m.Px, m.Pose.PivotUp * 1.5f * m.Px);
            Particle p = Fx.Glint(m.BodyPos() + off, m.Px, 0.26f, m.Pal.Core, 1.05f);
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = m.BodyPos() + off;
            }
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }

    /// <summary>
    /// FIRST CLASH: the opening blow of every duel.
    ///
    ///   CROUCH  0.06   both drop and push off (dust)
    ///   BLINK 0.1-0.19 both flash-step to the middle (longer from afar): smeared, afterimages, speed lines
    ///   CONTACT        two crescents meet: a 1-tile starburst in both colours, a ring, sparks,
    ///                  focus lines; III: a world hit-stop of 0.1 s, II: the pair holds, I: no stop
    ///   PRESS   0.08   pressed into each other, trembling
    ///   SKID    0.36   both skid back a tile, leaning back, dust trails (II+: skid marks); the one
    ///                  that came off worse slides farther (no damage: it is a greeting)
    ///   SETTLE  0.15   into guard; the better one glints
    /// </summary>
    internal sealed class FirstClash : Exchange
    {
        private const int Crouch = 0, Blink = 1, Press = 2, Skid = 3, SettleIn = 4;

        private Vector2 _sa, _sb, _axis;
        private Clash _c;
        private float _press, _tBlink;

        public override string Id => "first_clash";

        public override Form Forms => Form.Dash | Form.Strike | Form.Heavy;

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 0.64f * K, out _sa, out _sb))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            // a blink across a few tiles: longer the farther they start (from afar it must still read as a dash)
            float far = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tBlink = Mathf.Clamp(0.07f + 0.025f * far, 0.1f, 0.19f);
            return true;
        }

        public override void Start()
        {
            // a test of strength only: no damage, no streak
            _c = D.Roll();
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Crouch:
                    {
                        if (Enter())
                        {
                            DuelFx.Puff(A0, P.Px, Combat.TileAt(A0), 3, 14f);
                            DuelFx.Puff(B0, Q.Px, Combat.TileAt(B0), 3, 14f);
                        }
                        float c = Anim.OutQuad(Mathf.Clamp01(Pt / 0.06f));
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = Mathf.Lerp(1f, 1.14f, c);
                            m.Pose.SY = Mathf.Lerp(1f, 0.84f, c);
                            m.Pose.Rot = 8f * c;
                        }
                        if (Pt >= 0.06f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Next(Blink);
                        }
                        break;
                    }
                case Blink:
                    {
                        float k = Mathf.Clamp01(Pt / _tBlink);
                        DashPose(P, A0, _sa, k);
                        DashPose(Q, B0, _sb, k);
                        P.Pose.SX = Q.Pose.SX = 1f + 0.45f * Mathf.Sin(Mathf.PI * k);
                        P.Pose.SY = Q.Pose.SY = 1f - 0.22f * Mathf.Sin(Mathf.PI * k);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.016f, 0.16f);
                        Trail(Q, ref GhostB, dt, 0.016f, 0.16f);
                        if (Vis && k < 0.8f)
                        {
                            Kit.SpeedLines(P.BodyPos(), _sa - A0, px, 1, 5f, 0.14f, L >= 2 ? Fx.WithA(P.Pal.Edge, 0.7f) : new Color(1f, 1f, 1f, 0.65f));
                            Kit.SpeedLines(Q.BodyPos(), _sb - B0, px, 1, 5f, 0.14f, L >= 2 ? Fx.WithA(Q.Pal.Edge, 0.7f) : new Color(1f, 1f, 1f, 0.65f));
                        }
                        if (k >= 1f)
                        {
                            Contact_();
                            Next(Press);
                        }
                        break;
                    }
                case Press:
                    {
                        float j = ((int)(Pt * 60f) & 1) == 0 ? 0.5f : -0.5f;
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 22f;
                            m.Pose.SX = 0.9f;
                            m.Pose.SY = 1.08f;
                        }
                        P.Pose.Nudge = new Vector2(j, 0f);
                        Q.Pose.Nudge = new Vector2(-j, 0f);
                        _press += dt;
                        if (_press >= 0.08f)
                        {
                            // the one that came off worse slides farther
                            float da = _c.Even ? 1f : _c.AWins ? 0.7f : 1.3f;
                            float db = _c.Even ? 1f : _c.AWins ? 1.3f : 0.7f;
                            KA = Knock.Skid(_sa, DuelGeo.Slide(_sa, -_axis, da * K), 0.36f, -14f, 0.12f);
                            KB = Knock.Skid(_sb, DuelGeo.Slide(_sb, _axis, db * K), 0.36f, -14f, 0.12f);
                            Next(Skid);
                        }
                        break;
                    }
                case Skid:
                    if (Recoil(dt))
                    {
                        Next(SettleIn);
                    }
                    break;
                default:
                    {
                        if (Enter() && Vis)
                        {
                            foreach (Duelist m in new[] { P, Q })
                            {
                                if (!_c.Even && (m == P) != _c.AWins)
                                {
                                    continue;
                                }
                                Vector2 off = new Vector2(m.Fwd * 3f * m.Px, m.Pose.PivotUp * 1.4f * m.Px);
                                Fx.Glint(m.BodyPos() + off, m.Px, 0.22f, m.Pal.Core, 0.9f);
                            }
                        }
                        Settle(P, Pt, 0f);
                        Settle(Q, Pt, 1.2f);
                        if (Pt >= Rest(0.15f))
                        {
                            Done = true;
                        }
                        break;
                    }
            }
        }

        private void Contact_()
        {
            float px = Px;
            Vector2 at = Contact();
            Slash(P, 2, 0.16f);
            Slash(Q, 2, 0.16f);
            D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            D.Sound("event:/SFX/HIT/HitGeneric");
            if (Vis)
            {
                Color core = L >= 3 ? Kit.Core : Color.white;
                // a greeting: both colours, whoever came off better
                DuelFx.Starburst(at, px, 1f * K, core, P.Pal.Edge, Q.Pal.Edge, 0.1f);
                Color ring = L >= 2 ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : Color.white;
                DuelFx.GroundRing((P.Pose.Ground + Q.Pose.Ground) * 0.5f, px, 3f, (L >= 3 ? 26f : 18f) * K, 0.36f, 2, 1,
                    L >= 3 ? Kit.Core : new Color(1f, 1f, 1f, 1f), Fx.WithA(ring, 0.7f), Fx.WithA(ring, 0f));
                DuelFx.Spray(at, px, 10, _axis, Metal, ring);
                DuelFx.Fountain(at, px, 6, 40f, L >= 2 ? ring : DuelFx.Metal);
                Fx.FocusLines(at, px, Fx.Count(12), 12f * K, 26f * K, 0.14f);
                if (L >= 2)
                {
                    Kit.Shake(at, 0.15f, 0.005f);
                }
            }
            // the level grammar: III freezes the world (and scorches the ground under the meeting), II
            // holds the pair, I never stops
            if (L >= 3)
            {
                Kit.Stop(at, 0.1f);
                Hold = 0.06f;   // even when the world hit-stop is resting, the pair holds
                Wreck((P.Pose.Ground + Q.Pose.Ground) * 0.5f, 1f, 1, null);
            }
            else if (L == 2)
            {
                Hold = 0.07f;
            }
        }
    }

    /// <summary>
    /// CLASH SPIRAL: three even clashes in a row, and both go all out. The fourth clash makes
    /// the ground give.
    ///
    ///   GATHER  0.40   both drop into a deep stance, trembling, glowing brighter in their colours;
    ///                  dust spirals in toward the middle, rings close on their feet; III: embers
    ///   RUSH    0.10   both blur into the middle
    ///   CLASH          a 2-tile starburst in both colours, a 3-tile ring, a crater between them
    ///                  (glowing at III; a dust crater at I), sparks thrown out in a spiral, a
    ///                  fountain, debris, a shake; III: a world hit-stop and an impact frame (II: the pair holds)
    ///   LAUNCH ~0.5    both blown back 1.4 tiles in backflips, landing hard, skidding
    ///   BREATHER 1.5   crouched, panting, staring; a glint each
    /// </summary>
    internal sealed class ClashSpiral : Exchange
    {
        private const int Gather = 0, Rush = 1, Launch = 2, Breather = 3;

        private Vector2 _sa, _sb, _axis, _mid, _la, _lb;
        private Vector2 _ga0, _gb0;
        private float _swirl, _ember;
        private bool _glA, _glB;

        public override string Id => "spiral";

        public override Form Forms => Form.Flurry | Form.Spin | Form.Aerial;

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 0.6f * K, out _sa, out _sb))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            _mid = (_sa + _sb) * 0.5f;
            _la = DuelGeo.Land(_sa, -_axis, 1.4f * K);
            _lb = DuelGeo.Land(_sb, _axis, 1.4f * K);
            return true;
        }

        public override void Start()
        {
            _ga0 = P.Pose.Ground;
            _gb0 = Q.Pose.Ground;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Gather:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/POWERS/Powerup");
                            if (Vis)
                            {
                                foreach (Duelist m in new[] { P, Q })
                                {
                                    Color e = m.Pal.Edge;
                                    Fx.Ring(m.Pose.Ground, m.Px, 0.4f, 13f * K, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                                        Fx.WithA(e, 0f), Fx.WithA(e, 0.8f), Fx.WithA(e, 0.95f));
                                }
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.4f);
                        float e2 = Anim.OutQuad(Mathf.Clamp01(k / 0.4f));
                        P.Pose.Ground = _ga0;
                        Q.Pose.Ground = _gb0;
                        FaceEach();
                        float j = ((int)(T * 60f) & 1) == 0 ? 0.5f : -0.5f;
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = Mathf.Lerp(1f, 1.16f, e2);
                            m.Pose.SY = Mathf.Lerp(1f, 0.8f, e2);
                            m.Pose.Rot = 12f * e2;
                            m.Pose.Height = 0f;
                            m.Glow = Fx.WithA(m.Pal.Edge, Mathf.Lerp(0.05f, L >= 2 ? 0.45f : 0.25f, k) * (0.8f + 0.2f * Mathf.Sin(T * 40f)));
                        }
                        if (k > 0.3f)
                        {
                            P.Pose.Nudge = new Vector2(j, 0f);
                            Q.Pose.Nudge = new Vector2(-j, 0f);
                        }
                        _swirl -= dt;
                        if (_swirl <= 0f)
                        {
                            _swirl = 0.1f;
                            DuelFx.Swirl(_mid, px, Combat.TileAt(_mid), 1.3f * K, 4, 0.45f, 7f);
                        }
                        if (L >= 3)
                        {
                            _ember -= dt;
                            if (_ember <= 0f)
                            {
                                _ember = 0.05f;
                                DuelFx.Embers(Random.value < 0.5f ? P.BodyPos() : Q.BodyPos(), px, 1, 4f, Random.value < 0.5f ? P.Pal.Edge : Q.Pal.Edge);
                            }
                        }
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Next(Rush);
                        }
                        break;
                    }
                case Rush:
                    {
                        float k = Mathf.Clamp01(Pt / 0.1f);
                        DashPose(P, _ga0, _sa, k);
                        DashPose(Q, _gb0, _sb, k);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.016f, 0.16f);
                        Trail(Q, ref GhostB, dt, 0.016f, 0.16f);
                        if (k >= 1f)
                        {
                            Clash();
                            Next(Launch);
                        }
                        break;
                    }
                case Launch:
                    {
                        bool over = Recoil(dt);
                        if (!KA.Landed || !KB.Landed)
                        {
                            Trail(P, ref GhostA, dt, 0.04f, 0.14f, 0.7f);
                            Trail(Q, ref GhostB, dt, 0.04f, 0.14f, 0.7f);
                        }
                        if (over)
                        {
                            Next(Breather);
                        }
                        break;
                    }
                default:
                    {
                        // crouched, panting, staring (the strongest catch their breath in a moment)
                        float breath = Mathf.Max(0.6f, 1.5f * Brisk);
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            float pant = Mathf.Abs(Mathf.Sin(T * 9f + (m == P ? 0f : 1.3f)));
                            m.Pose.Rot = 10f;
                            m.Pose.SX = 1.08f + 0.02f * pant;
                            m.Pose.SY = 0.88f + 0.05f * pant;
                            m.Pose.Height = 0f;
                            m.Glow = Fx.WithA(m.Pal.Edge, Mathf.Max(0f, 0.2f * (1f - Pt / 0.6f)));
                        }
                        if (!_glA && Pt >= 0.3f * breath)
                        {
                            _glA = true;
                            Shine(P);
                        }
                        if (!_glB && Pt >= 0.5f * breath)
                        {
                            _glB = true;
                            Shine(Q);
                        }
                        if (L >= 3 && Vis && Random.value < 0.15f)
                        {
                            DuelFx.Embers(_mid, px, 1, 6f, Color.Lerp(P.Pal.Edge, Q.Pal.Edge, Random.value));
                        }
                        if (Pt >= breath)
                        {
                            P.Glow = Q.Glow = DuelFx.Clear;
                            Done = true;
                        }
                        break;
                    }
            }
        }

        private void Clash()
        {
            float px = Px;
            Vector2 at = Contact();
            WorldTile tile = Combat.TileAt(_mid);
            Color ea = P.Pal.Edge, eb = Q.Pal.Edge;
            Color both = Color.Lerp(ea, eb, 0.5f);
            Slash(P, 2, 0.18f);
            Slash(Q, 2, 0.18f);
            D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall");
            if (Vis)
            {
                DuelFx.Starburst(at, px, 2f * K, L >= 3 ? Kit.Core : Color.white, ea, eb, 0.14f);
                DuelFx.GroundRing(_mid, px, 4f, 30f * K, 0.5f, 3, 1, new Color(1f, 1f, 1f, 1f), Fx.WithA(L >= 2 ? both : Color.white, 0.75f), Fx.WithA(both, 0f));
                if (L >= 2)
                {
                    Kit.Crater(_mid, px * K, tile, L >= 3, 5f, false, both);
                }
                Kit.DustRing(_mid, px, tile, 18, 80f, 0.8f, K);
                Kit.Debris(_mid, px, tile, 10, K);
                SpiralSparks(at, px, L >= 2 ? both : DuelFx.Metal);
                DuelFx.Fountain(at, px, 10, 50f, L >= 2 ? both : DuelFx.Metal);
                Fx.FocusLines(at, px, Fx.Count(16), 14f * K, 30f * K, 0.16f);
                Kit.Shake(at, 0.3f, 0.01f, K);
                if (L >= 3)
                {
                    Kit.ImpactFrame(at, px, 1f, P.Pal, false);
                    DuelFx.Embers(_mid, px, 10, 6f, both);
                }
            }
            if (L >= 3)
            {
                Kit.Stop(at, 0.12f);
                Hold = 0.07f;
            }
            else if (L == 2)
            {
                Hold = 0.08f;
            }
            // the ground gives for real (II craters it, III blasts and burns it); where they come down
            // is worked out on what is left
            Wreck(_mid, ByLevel(0, 2, 3) * 0.5f, ByLevel(0, 2, 3), null);
            _la = DuelGeo.Land(_sa, -_axis, 1.4f * K);
            _lb = DuelGeo.Land(_sb, _axis, 1.4f * K);
            // both blown back: backflips
            float wob = 0.3f * Mathf.Max(0.5f, Brisk);
            KA = (_la - _sa).sqrMagnitude > 0.04f ? Knock.Fly(_sa, _la, 0.48f, 0.9f * K, -360f, wob) : Knock.Skid(_sa, _sa, 0.3f, -20f, wob);
            KB = (_lb - _sb).sqrMagnitude > 0.04f ? Knock.Fly(_sb, _lb, 0.48f, 0.9f * K, -360f, wob) : Knock.Skid(_sb, _sb, 0.3f, -20f, wob);
            P.Glow = Q.Glow = DuelFx.Clear;
        }

        /// <summary>Sparks thrown out of the clash on a spiral (their path bends as they fly).</summary>
        private void SpiralSparks(Vector2 at, float px, Color tint)
        {
            int n = Fx.Count(18);
            float turn = Random.value < 0.5f ? 5f : -5f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.5f) / n * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(1, Art.Sparks.Length)], at, px, Random.Range(0.3f, 0.45f), FxLayer.Top, 17);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(70f, 120f) * px, 0f, 2.5f);
                p.FaceVel = true;
                p.Color(Color.white, Color.Lerp(Color.white, tint, 0.5f), Fx.WithA(tint, 0f));
                float w = turn * Random.Range(0.8f, 1.2f);
                p.Step = (q, d) =>
                {
                    float c = Mathf.Cos(w * d), s = Mathf.Sin(w * d);
                    q.Vel = new Vector2(q.Vel.x * c - q.Vel.y * s, q.Vel.x * s + q.Vel.y * c);
                };
            }
        }

        private void Shine(Duelist m)
        {
            if (!Vis)
            {
                return;
            }
            Vector2 off = new Vector2(m.Fwd * 3f * m.Px, m.Pose.PivotUp * 1.3f * m.Px);
            Particle p = Fx.Glint(m.BodyPos() + off, m.Px, 0.24f, m.Pal.Core, 1f);
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = m.BodyPos() + off;
            }
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }

    /// <summary>
    /// THE FINAL EXCHANGE: one of them is near the end. They face each other from afar, the wind
    /// drops, and they dash through each other.
    ///
    ///   SET     0.20   both take their places, 2.6 tiles apart (closer if the ground is short)
    ///   POISE   0.35   low drawing stances, utterly still; a glint each; wind lines; II+: focus
    ///                  lines, III: the glow builds white-hot
    ///   DASH    0.12   both blur through each other: smeared afterimages, a flash where they cross,
    ///                  a slash line left hanging in the air across the middle
    ///   BACK TO BACK 0.6   frozen in follow-through, facing away from each other; dust settling
    ///   FALL           the cut arrives on the loser: a crescent in the winner's colours, a hit star,
    ///                  its own colour flashing; III: an impact frame (a black flash on a critical) and
    ///                  a world hit-stop; the killing blow; it buckles and the game lays it down.
    ///                  Rarely, between near-equals, both drop to one knee instead, both cut, and
    ///                  the duel goes on
    ///   RISE    0.35   the winner straightens (a spring), ting
    /// Where there is no room to dash through, the same beats play face to face.
    /// </summary>
    internal sealed class Final : Exchange, IFinale
    {
        private const int Set = 0, Poise = 1, Dash = 2, BackToBack = 3, Fall = 4, Rise = 5, Kneel = 6;

        private Vector2 _sa, _sb, _ea, _eb, _axis, _mid;
        private bool _through, _kneel, _struck, _killed;
        private bool _loserIsA;
        private Duelist _w, _l;
        private float _tSet, _glow;
        private bool _ga, _gb;

        /// <summary>Who falls, as Finales.Choose already decided it (low health weighing it), or null: roll here.</summary>
        private readonly Clash? _given;

        public Final()
        {
        }

        public Final(Clash c)
        {
            _given = c;
        }

        public override string Id => "final";

        public override Form Forms => Form.Dash | Form.Blade | Form.Straight | Form.Counter | Form.Ultimate;

        /// <summary>After the killing blow the winner finishes alone.</summary>
        public override bool Solo => _struck;

        public bool Killed => _killed;

        public bool Kneeled => _kneel;

        public override bool Plan()
        {
            // who falls: the clash rule's roll, low health weighing it (an upset is possible); the finales'
            // own choice when they picked this one (rolling again here lost their low-health bias)
            Clash c = _given ?? D.Roll();
            _loserIsA = !c.AWins;
            // near-equals sometimes both go down to one knee; two still strong when the duel runs its
            // course usually do (the first decisive cut rarely ends it), and the next one does
            float kneel = c.Ratio < 1.15f ? 0.35f : 0f;
            if (D.Timed && (_loserIsA ? D.HpA : D.HpB) > 0.5f)
            {
                kneel = Mathf.Max(kneel, 0.65f);
            }
            _kneel = !D.KneelUsed && Random.value < kneel;
            // places: far apart for a dash-through, closer if the ground is short, face to face if not even that
            float[] gaps = { 2.6f, 1.9f };
            foreach (float g in gaps)
            {
                if (!DuelGeo.Stance(A0, B0, g * K, out _sa, out _sb))
                {
                    continue;
                }
                _axis = (_sb - _sa).normalized;
                _mid = (_sa + _sb) * 0.5f;
                _ea = Combat.Clamp(_mid + _axis * 0.75f * K);
                _eb = Combat.Clamp(_mid - _axis * 0.75f * K);
                if (Combat.SafeSpot(_ea) && Combat.SafeSpot(_eb) && Combat.ClearPath(_sa, _ea) && Combat.ClearPath(_sb, _eb))
                {
                    _through = true;
                    break;
                }
            }
            if (!_through)
            {
                if (!DuelGeo.Stance(A0, B0, 0.7f * K, out _sa, out _sb))
                {
                    _sa = A0;
                    _sb = B0;
                }
                _axis = (_sb - _sa).sqrMagnitude > 1e-4f ? (_sb - _sa).normalized : Vector2.right;
                _mid = (_sa + _sb) * 0.5f;
                _ea = _sa;
                _eb = _sb;
            }
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tSet = d > 0.2f ? Mathf.Clamp(0.1f + 0.04f * d, 0.14f, 0.26f) : 0.12f;
            return true;
        }

        public override void Start()
        {
            _l = _loserIsA ? P : Q;
            _w = _loserIsA ? Q : P;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Set:
                    {
                        float k = Pt / _tSet;
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(Poise);
                        }
                        break;
                    }
                case Poise:
                    {
                        P.Pose.Ground = _sa;
                        Q.Pose.Ground = _sb;
                        FaceEach();
                        float k = Mathf.Clamp01(Pt / 0.35f);
                        float e = Anim.OutQuad(Mathf.Clamp01(k / 0.4f));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 14f * e;
                            m.Pose.SX = 1f + 0.12f * e;
                            m.Pose.SY = 1f - 0.15f * e;
                            m.Pose.Height = 0f;
                            if (L >= 3)
                            {
                                _glow = Mathf.Lerp(0f, 0.35f, k);
                                m.Glow = Fx.WithA(Color.Lerp(m.Pal.Edge, Color.white, k), _glow);
                            }
                        }
                        if (!_ga && k >= 0.3f)
                        {
                            _ga = true;
                            Shine(P);
                        }
                        if (!_gb && k >= 0.55f)
                        {
                            _gb = true;
                            Shine(Q);
                        }
                        if (Vis && ((int)(Pt / 0.08f)) > ((int)((Pt - dt) / 0.08f)))
                        {
                            DuelFx.Wind(_sa, _sb, px, Combat.TileAt(_mid), new Color(1f, 1f, 1f, 0.45f));
                            if (L >= 2 && Random.value < 0.5f)
                            {
                                Duelist m = Random.value < 0.5f ? P : Q;
                                Fx.FocusLines(m.BodyPos(), m.Px, Fx.Count(4), 11f * K, 20f * K, 0.1f);
                            }
                        }
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Next(Dash);
                        }
                        break;
                    }
                case Dash:
                    {
                        float k = Mathf.Clamp01(Pt / 0.12f);
                        if (_through)
                        {
                            float e = Anim.OutCubic(k);
                            P.Pose.Ground = Vector2.Lerp(_sa, _ea, e);
                            Q.Pose.Ground = Vector2.Lerp(_sb, _eb, e);
                            // facing where they're going
                            P.Pose.FaceRight = _axis.x > 0f || Mathf.Abs(_axis.x) < 0.01f && P.Pose.FaceRight;
                            Q.Pose.FaceRight = !(P.Pose.FaceRight);
                        }
                        float s = Mathf.Sin(Mathf.PI * k);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = 1f + 0.55f * s;
                            m.Pose.SY = 1f - 0.28f * s;
                            m.Pose.Rot = 18f;
                        }
                        Trail(P, ref GhostA, dt, 0.012f, 0.2f);
                        Trail(Q, ref GhostB, dt, 0.012f, 0.2f);
                        if (k >= 0.5f && Enter())
                        {
                            Cross();
                        }
                        if (k >= 1f)
                        {
                            P.Glow = Q.Glow = DuelFx.Clear;
                            Next(BackToBack);
                        }
                        break;
                    }
                case BackToBack:
                    {
                        // frozen in follow-through
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 22f;
                            m.Pose.SX = 1.1f;
                            m.Pose.SY = 0.9f;
                        }
                        if (Vis && Random.value < 0.2f)
                        {
                            DuelFx.Puff(Random.value < 0.5f ? P.Pose.Ground : Q.Pose.Ground, px, Combat.TileAt(_mid), 1, 3f);
                        }
                        if (Pt >= 0.6f)
                        {
                            Next(_kneel ? Kneel : Fall);
                        }
                        break;
                    }
                case Fall:
                    FallTick(dt);
                    break;
                case Kneel:
                    KneelTick();
                    break;
                default:
                    {
                        if (Enter() && Vis)
                        {
                            Vector2 off = new Vector2(_w.Fwd * 3f * _w.Px, _w.Pose.PivotUp * 1.5f * _w.Px);
                            Particle p = Fx.Glint(_w.BodyPos() + off, _w.Px, 0.28f, _w.Pal.Core, 1.15f);
                            if (p != null)
                            {
                                Duelist w = _w;
                                p.Step = (q, d) => q.Pos = w.BodyPos() + off;
                            }
                        }
                        // the winner straightens up
                        float amp = Anim.Spring(Pt, 7f, 14f);
                        _w.Pose.Rot = 22f * amp;
                        _w.Pose.SX = 1f + 0.1f * amp;
                        _w.Pose.SY = 1f - 0.1f * amp;
                        if (!_l.Finished && !_killed)
                        {
                            // it lived through it (something kept it alive): it staggers
                            float a2 = Anim.Spring(Pt, 4f, 12f);
                            _l.Pose.Rot = -20f * a2;
                            _l.Pose.SX = 1f;
                            _l.Pose.SY = 1f - 0.1f * Mathf.Abs(a2);
                        }
                        if (Pt >= 0.4f)
                        {
                            Done = true;
                        }
                        break;
                    }
            }
        }

        /// <summary>Where they cross: a flash, and the cut hanging in the air.</summary>
        private void Cross()
        {
            float px = Px;
            Vector2 at = (P.BodyPos() + Q.BodyPos()) * 0.5f;
            D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            if (!Vis)
            {
                return;
            }
            Kit.Flash(at, px, 6, 0.06f, Color.white);
            Vector2 half = _axis * (_through ? 1.6f : 0.9f) * K;
            Color edge = _w.Pal.Edge;
            Kit.Line(at - half, at + half, px, 0.75f, L >= 2 ? 3 : 2, FxLayer.Top, 3, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0.7f), Fx.WithA(edge, 0f));
            Kit.Line(at - half, at + half, px, 0.6f, 1, FxLayer.Top, 4, Color.white, new Color(1f, 1f, 1f, 0.8f), DuelFx.Clear);
            Kit.SpeedLines(at, _axis, px, 4, 6f, 0.2f, new Color(1f, 1f, 1f, 0.7f));
        }

        private void FallTick(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Strike_();
            }
            _w.Pose.Rot = 22f;
            if (_killed && !_l.Finished)
            {
                // buckling: the game lays it down once its death is done
                float k = Mathf.Clamp01(Pt / 0.25f);
                _l.Pose.SY = Mathf.Lerp(0.9f, 0.78f, k);
                _l.Pose.SX = 1.06f;
                _l.Pose.Rot = Mathf.Lerp(22f, 48f, Anim.OutQuad(k));
            }
            if (Pt >= 0.35f)
            {
                Next(Rise);
            }
        }

        private void Strike_()
        {
            float px = Px;
            Vector2 at = _l.BodyPos();
            // the cut arrives
            if (Vis)
            {
                Sprite[] arc = Art.Arc(_w.Armed ? 11 : 8, 0.7f, 150f, -30f, 3);
                Particle cut = Fx.Spawn(arc, at, _l.Px, 0.2f, FxLayer.Top, 6);
                if (cut != null)
                {
                    cut.SX = _l.Fwd;
                    cut.Color(_w.Pal.Core, _w.Pal.Edge, Fx.WithA(_w.Pal.Edge, 0.5f));
                }
                Fx.HitSpark(at, px, 2, new Vector2(_l.Fwd, 0.3f), 7);
                DuelFx.Spray(at, px, 6, _axis, Metal, _w.Pal.Edge);
            }
            HitResult r = D.FinishingBlow(_w, _l);
            _struck = true;
            _killed = r.Killed || !_l.A.hasHealth();
            Color own = Tiers.Of(_l.A) != Tier.None ? Tiers.Color(Tiers.Of(_l.A)) : Color.white;
            _l.Flash(0.12f, own);
            D.Sound("event:/SFX/HIT/HitFlesh");
            D.Sound("event:/SFX/HIT/HitGeneric");
            if (Vis && L >= 2)
            {
                Kit.ImpactFrame(at, px, L >= 3 ? 0.8f : 0.5f, _w.Pal, r.Crit);
                Kit.Shake(at, 0.2f, 0.007f);
            }
            if (L >= 3)
            {
                Kit.Stop(at, 0.12f);
            }
            else
            {
                Stop(0.08f);
            }
            if (!_killed)
            {
                D.KneelUsed = true;   // it survived what should have ended it
            }
            else if (D.Grand)
            {
                // the last cut of a Grandmaster or a God runs on into the earth past the fallen
                Vector2 away = _l.Pose.Ground - _w.Pose.Ground;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : (_loserIsA ? -_axis : _axis);
                Vector2 from = _l.Pose.Ground + away * 0.4f * K;
                Vector2 to = Combat.Clamp(_l.Pose.Ground + away * (D.Gods ? 7f : 4f) * K);
                D.Furrow(from, to, 0.8f, 2, _w.A);
                DuelFx.Fissure(from, to, px, Combat.TileAt(to), _w.Pal.Edge, 3);
            }
        }

        private void KneelTick()
        {
            float px = Px;
            if (Enter())
            {
                // both cut: both drop to one knee
                D.KneelUsed = true;
                foreach (Duelist m in new[] { P, Q })
                {
                    Duelist o = m == P ? Q : P;
                    if (Vis)
                    {
                        Fx.HitSpark(m.BodyPos(), px, 1, new Vector2(m.Fwd, 0.3f), 4);
                    }
                    D.Chip(o, m, 0.3f, true);
                }
                D.Sound("event:/SFX/HIT/HitGeneric");
                Stop(0.06f);
            }
            float k = Mathf.Clamp01(Pt / 0.12f);
            foreach (Duelist m in new[] { P, Q })
            {
                if (Pt < 0.55f)
                {
                    m.Pose.SY = Mathf.Lerp(0.9f, 0.72f, Anim.OutQuad(k));
                    m.Pose.SX = Mathf.Lerp(1.1f, 1.12f, k);
                    m.Pose.Rot = Mathf.Lerp(22f, -6f, Anim.OutQuad(k));
                }
                else
                {
                    // rising again, turning to face the other
                    Settle(m, Pt - 0.55f, m == P ? 0f : 1f);
                }
            }
            if (Pt >= 0.55f)
            {
                FaceEach();
            }
            if (Pt >= 0.8f)
            {
                Done = true;
            }
        }

        private void Shine(Duelist m)
        {
            if (!Vis)
            {
                return;
            }
            Vector2 off = new Vector2(m.Fwd * 3f * m.Px, m.Pose.PivotUp * 1.4f * m.Px);
            Particle p = Fx.Glint(m.BodyPos() + off, m.Px, 0.22f, m.Pal.Core, 0.95f);
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = m.BodyPos() + off;
            }
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }

    /// <summary>
    /// CLASH OF HEAVENS: two Combat Gods meet. Before the duel proper they take it to the sky.
    ///
    ///   CHARGE   0.30  both crouch, glowing gold; dust and pebbles drawn in; a title card, and the
    ///                  edges of the world darken (when a cinematic may play)
    ///   RISE     0.60  both launch to height 4 (a hot takeoff crater each, rings, debris), afterimages
    ///   CLASH x3       0.4 s apart: a rush and a mid-air meeting, starbursts of 1, 1.5 and 2.5 tiles,
    ///                  rings in the air and on the ground below, sparks, the camera shaking harder;
    ///                  the third freezes the world on an impact frame
    ///   SEPARATE 0.50  they fly apart until 4 tiles separate them
    ///   DESCEND  0.40  down to the ground: a small hot crater each, dust, a shake
    ///   LAND     0.25
    /// No damage: it is how two Gods say hello.
    ///
    /// BACK TO THE SKY (the reprise, once per duel of two Gods, from the pool after a few exchanges):
    /// the same flight without the title card, but now the clashes count. The third decides it: an
    /// even one parts them as before; otherwise the winner's blow goes through and the loser is
    /// spiked out of the sky, tumbling once, into a glowing crater in the winner's colours, and
    /// pushes itself up from one knee while the winner comes down.
    /// </summary>
    internal sealed class Heavens : Exchange
    {
        private const int Charge = 0, RiseUp = 1, Rush = 2, Bounce = 3, Separate = 4, Descend = 5, LandIn = 6;
        private const float SpikeDur = 0.26f;

        private readonly bool _reprise;
        private Vector2 _mid, _axis, _la, _lb;
        private Vector2 _fa, _fb, _ta, _tb;
        private float _hfa, _hfb, _hta, _htb, _dur, _h, _swirl;
        private int _i;
        /// <summary>The reprise's deciding clash, and who was spiked out of the sky by it.</summary>
        private Clash _c;
        private Duelist _lose;
        private Vector2 _sFrom, _sTo;
        private float _sH, _ghostL;
        private bool _spiked;

        public Heavens(bool reprise = false)
        {
            _reprise = reprise;
        }

        public override string Id => _reprise ? "sky" : "heavens";

        public override Form Forms => Form.Aerial | Form.Ki | Form.Heavy;

        public override float Weight()
        {
            // the opener is queued, never picked; the reprise: two Gods, once, when the duel is well under way
            if (!_reprise || !D.Gods || D.SkyUsed || D.Exchanges < 3 || D.T < 10f)
            {
                return 0f;
            }
            return D.T > 18f ? 3f : 1.6f;
        }

        public override bool Plan()
        {
            if ((B0 - A0).sqrMagnitude > 9.5f * 9.5f)
            {
                return false;
            }
            Vector2 d = B0 - A0;
            _axis = Mathf.Abs(d.x) > 0.05f ? new Vector2(Mathf.Sign(d.x), 0f) : Vector2.right;
            _mid = (A0 + B0) * 0.5f;
            _h = 4f * K;
            _la = DuelGeo.Land(_mid, -_axis, 2f * K);
            _lb = DuelGeo.Land(_mid, _axis, 2f * K);
            if ((_la - _mid).sqrMagnitude < 0.25f)
            {
                _la = A0;
            }
            if ((_lb - _mid).sqrMagnitude < 0.25f)
            {
                _lb = B0;
            }
            return true;
        }

        public override void Start()
        {
            if (_reprise)
            {
                D.SkyUsed = true;
            }
            else
            {
                D.Cine(_mid);
            }
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Charge:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/POWERS/Powerup");
                            if (Vis)
                            {
                                foreach (Duelist m in new[] { P, Q })
                                {
                                    Fx.Ring(m.Pose.Ground, m.Px, 0.3f, 16f * K, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                                        Fx.WithA(m.Pal.Edge, 0f), Fx.WithA(m.Pal.Edge, 0.85f), Fx.WithA(Kit.Core, 1f));
                                }
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.3f);
                        P.Pose.Ground = A0;
                        Q.Pose.Ground = B0;
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = Mathf.Lerp(1f, 1.16f, Anim.OutQuad(k));
                            m.Pose.SY = Mathf.Lerp(1f, 0.8f, Anim.OutQuad(k));
                            m.Pose.Rot = -6f * k;
                            m.Glow = Fx.WithA(Color.Lerp(m.Pal.Edge, Color.white, 0.3f), 0.45f * k * (0.8f + 0.2f * Mathf.Sin(T * 45f)));
                        }
                        float j = ((int)(T * 60f) & 1) == 0 ? 0.5f : -0.5f;
                        P.Pose.Nudge = new Vector2(j, 0f);
                        Q.Pose.Nudge = new Vector2(-j, 0f);
                        _swirl -= dt;
                        if (_swirl <= 0f)
                        {
                            _swirl = 0.1f;
                            DuelFx.Swirl(A0, P.Px, Combat.TileAt(A0), 0.8f * K, 3, 0.35f, 8f);
                            DuelFx.Swirl(B0, Q.Px, Combat.TileAt(B0), 0.8f * K, 3, 0.35f, -8f);
                        }
                        if (k >= 1f)
                        {
                            Next(RiseUp);
                        }
                        break;
                    }
                case RiseUp:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Liftoff(A0, P);
                            Liftoff(B0, Q);
                            Leg(A0, B0, 0f, 0f, _mid - _axis * 1.2f * K, _mid + _axis * 1.2f * K, _h, _h, 0.6f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutCubic(k));
                        float s = 1f - k;
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = Mathf.Lerp(1f, 0.72f, s);
                            m.Pose.SY = Mathf.Lerp(1f, 1.4f, s);
                            m.Pose.Rot = Mathf.Lerp(6f, -4f, s);
                        }
                        Trail(P, ref GhostA, dt, 0.025f, 0.18f);
                        Trail(Q, ref GhostB, dt, 0.025f, 0.18f);
                        if (k >= 1f)
                        {
                            _i = 0;
                            Next(Rush);
                        }
                        break;
                    }
                case Rush:
                    {
                        if (Enter())
                        {
                            float hi = _h + (_i == 1 ? 0.35f : _i == 2 ? -0.25f : 0f) * K;
                            float shift = (_i == 1 ? 0.3f : _i == 2 ? -0.25f : 0f) * K;
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height,
                                _mid + _axis * (shift - 0.32f * K), _mid + _axis * (shift + 0.32f * K), hi, hi, 0.1f);
                        }
                        Fly(Anim.InQuad(Mathf.Clamp01(Pt / _dur)));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 24f;
                            m.Pose.SX = 1.3f;
                            m.Pose.SY = 0.82f;
                        }
                        Trail(P, ref GhostA, dt, 0.015f, 0.14f);
                        Trail(Q, ref GhostB, dt, 0.015f, 0.14f);
                        if (Pt >= _dur)
                        {
                            Hit();
                            Next(Bounce);
                        }
                        break;
                    }
                case Bounce:
                    {
                        if (Enter())
                        {
                            // apart, rising a little; the loser of a decided reprise is knocked farther and lower, tumbling
                            float wa = 0.9f, wb = 0.9f, ha = 0.2f, hb = 0.2f;
                            if (_lose != null)
                            {
                                bool aLost = _lose == P;
                                wa = aLost ? 1.3f : 0.6f;
                                wb = aLost ? 0.6f : 1.3f;
                                ha = aLost ? -0.4f : 0.15f;
                                hb = aLost ? 0.15f : -0.4f;
                            }
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height,
                                P.Pose.Ground - _axis * wa * K, Q.Pose.Ground + _axis * wb * K, P.Pose.Height + ha * K, Q.Pose.Height + hb * K, 0.3f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutCubic(k));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = m == _lose ? -200f * Anim.OutQuad(k) : Mathf.Lerp(-30f, 6f, Anim.OutQuad(k));
                            m.Pose.SX = m.Pose.SY = 1f;
                        }
                        if (k >= 1f)
                        {
                            _i++;
                            Next(_i < 3 ? Rush : Separate);
                        }
                        break;
                    }
                case Separate:
                    {
                        if (Enter())
                        {
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height, _la, _lb, _h * 0.55f, _h * 0.55f, 0.5f);
                            if (_lose != null)
                            {
                                _sFrom = _lose.Pose.Ground;
                                _sH = _lose.Pose.Height;
                                _sTo = _lose == P ? _la : _lb;
                            }
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutQuad(k));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = -360f * Anim.InOutCubic(k);   // a backflip away
                            m.Pose.SX = 1f;
                            m.Pose.SY = 1f - 0.12f * Mathf.Sin(Mathf.PI * k);
                        }
                        if (_lose != null)
                        {
                            Spike(Pt, dt);
                        }
                        if (_lose != P)
                        {
                            Trail(P, ref GhostA, dt, 0.03f, 0.14f, 0.7f);
                        }
                        if (_lose != Q)
                        {
                            Trail(Q, ref GhostB, dt, 0.03f, 0.14f, 0.7f);
                        }
                        if (k >= 1f)
                        {
                            Next(Descend);
                        }
                        break;
                    }
                case Descend:
                    {
                        if (Enter())
                        {
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height, _la, _lb, 0f, 0f, 0.4f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        P.Pose.Height = _hfa * (1f - k * k);
                        Q.Pose.Height = _hfb * (1f - k * k);
                        P.Pose.Ground = _la;
                        Q.Pose.Ground = _lb;
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Glow = Fx.WithA(m.Glow, m.Glow.a * 0.96f);
                            if (m == _lose)
                            {
                                // spiked into the ground: on one knee in its crater, pushing itself up
                                float up = Anim.InOutSine(k);
                                m.Pose.Height = 0f;
                                m.Pose.Rot = Mathf.Lerp(-14f, -4f, up);
                                m.Pose.SX = Mathf.Lerp(1.16f, 1.06f, up);
                                m.Pose.SY = Mathf.Lerp(0.76f, 0.9f, up);
                                continue;
                            }
                            m.Pose.Rot = 0f;
                            m.Pose.SX = Mathf.Lerp(1f, 0.86f, k);
                            m.Pose.SY = Mathf.Lerp(1f, 1.2f, k);
                        }
                        if (_lose != P)
                        {
                            Trail(P, ref GhostA, dt, 0.035f, 0.12f, 0.6f);
                        }
                        if (_lose != Q)
                        {
                            Trail(Q, ref GhostB, dt, 0.035f, 0.12f, 0.6f);
                        }
                        if (k >= 1f)
                        {
                            if (_lose != P)
                            {
                                Touchdown(_la, P);
                            }
                            if (_lose != Q)
                            {
                                Touchdown(_lb, Q);
                            }
                            Next(LandIn);
                        }
                        break;
                    }
                default:
                    {
                        Settle(P, Pt, 0f);
                        Settle(Q, Pt, 1f);
                        FaceEach();
                        P.Glow = Q.Glow = DuelFx.Clear;
                        if (Pt >= Rest(0.3f))
                        {
                            D.EndCine();
                            Done = true;
                        }
                        break;
                    }
            }
        }

        private void Leg(Vector2 fa, Vector2 fb, float hfa, float hfb, Vector2 ta, Vector2 tb, float hta, float htb, float dur)
        {
            _fa = fa;
            _fb = fb;
            _ta = ta;
            _tb = tb;
            _hfa = hfa;
            _hfb = hfb;
            _hta = hta;
            _htb = htb;
            _dur = dur;
        }

        private void Fly(float e)
        {
            P.Pose.Ground = Vector2.Lerp(_fa, _ta, e);
            Q.Pose.Ground = Vector2.Lerp(_fb, _tb, e);
            P.Pose.Height = Mathf.Lerp(_hfa, _hta, e);
            Q.Pose.Height = Mathf.Lerp(_hfb, _htb, e);
            FaceEach();
        }

        private void Liftoff(Vector2 at, Duelist m)
        {
            WorldTile t = Combat.TileAt(at);
            Kit.Crater(at, m.Px, t, true, 3.5f, true, m.Pal.Edge);
            Kit.DustRing(at, m.Px, t, 12, 60f, 0.6f, K);
            Kit.Debris(at, m.Px, t, 6, K);
            if (Vis)
            {
                DuelFx.GroundRing(at, m.Px, 3f, 18f * K, 0.3f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(m.Pal.Edge, 0.7f), Fx.WithA(m.Pal.Edge, 0f));
            }
            // a God's takeoff blasts the ground it leaves
            Wreck(at, 0.7f, 2, m);
        }

        private void Touchdown(Vector2 at, Duelist m)
        {
            WorldTile t = Combat.TileAt(at);
            Kit.Crater(at, m.Px, t, true, 3f, true, m.Pal.Edge);
            Kit.DustRing(at, m.Px, t, 10, 55f, 0.55f, K);
            Kit.Shake(at, 0.15f, 0.006f);
            D.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact");
            Wreck(at, 0.7f, 1, m);
        }

        private void Hit()
        {
            float px = Px;
            Vector2 at = Contact();
            float tiles = _i == 0 ? 1f : _i == 1 ? 1.5f : 2.5f;
            Color ea = P.Pal.Edge, eb = Q.Pal.Edge;
            bool decided = false;
            if (_reprise)
            {
                // this time the clashes count, and the third decides it
                _c = D.Resolve(_i == 2);
                decided = _i == 2 && !_c.Even;
                if (decided)
                {
                    ea = eb = Winner(_c).Pal.Edge;
                }
            }
            Slash(P, _i == 1 ? 1 : 2, 0.14f);
            Slash(Q, _i == 1 ? 0 : 2, 0.14f);
            D.Sound("event:/SFX/HIT/HitSwordSword");
            if (_i == 2)
            {
                D.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle");
            }
            if (Vis)
            {
                DuelFx.Starburst(at, px, tiles * K, Kit.Core, ea, eb, 0.1f + 0.03f * _i);
                DuelFx.AirRing(at, px, 4f, (14f + 8f * _i) * K, 0.3f, 2, Color.white, Fx.WithA(Color.Lerp(ea, eb, 0.5f), 0.7f), DuelFx.Clear);
                // the sky's blow lands on the ground below too
                Vector2 below = (P.Pose.Ground + Q.Pose.Ground) * 0.5f;
                DuelFx.GroundRing(below, px, 4f, (12f + 8f * _i) * K, 0.4f, 2, 1, new Color(1f, 1f, 1f, 0.8f), Fx.WithA(Color.Lerp(ea, eb, 0.5f), 0.5f), DuelFx.Clear);
                Kit.DustRing(below, px, Combat.TileAt(below), 6 + 4 * _i, 50f + 20f * _i, 0.6f, K);
                DuelFx.Spray(at, px, 8 + 4 * _i, _axis, true, Color.Lerp(ea, eb, 0.5f));
                DuelFx.Fountain(at, px, 4 + 3 * _i, 45f, Color.Lerp(ea, eb, 0.5f));
                Fx.FocusLines(at, px, Fx.Count(10 + 4 * _i), 14f * K, (26f + 6f * _i) * K, 0.14f);
                Kit.Shake(at, 0.18f + 0.06f * _i, 0.006f + 0.005f * _i);
            }
            if (_i == 2)
            {
                Kit.ImpactFrame(at, px, 1f, decided ? Winner(_c).Pal : P.Pal, false);
                Kit.Stop(at, 0.12f);
                // the third blow's shockwave reaches the ground below and scorches it wide
                Wreck((P.Pose.Ground + Q.Pose.Ground) * 0.5f, 1.4f, 1, decided ? Winner(_c) : null);
            }
            Hold = 0.05f;
            if (decided)
            {
                _lose = Loser(_c);
                Strike(Winner(_c), _lose, _c.Kind == ClashKind.Overwhelm ? 0.75f : Random.Range(0.5f, 0.65f), true);
            }
        }

        /// <summary>The loser of the reprise falls out of the sky: one tumble, a glowing crater in the winner's colours.</summary>
        private void Spike(float t, float dt)
        {
            float ks = Mathf.Clamp01(t / SpikeDur);
            if (ks < 1f)
            {
                _lose.Pose.Ground = Vector2.Lerp(_sFrom, _sTo, Anim.OutQuad(ks));
                _lose.Pose.Height = _sH * (1f - ks * ks);
                _lose.Pose.Rot = Mathf.Lerp(-200f, -360f, Anim.OutQuad(ks));   // the tumble finishes as it hits
                _lose.Pose.SX = 0.92f;
                _lose.Pose.SY = 1.1f;
                Trail(_lose, ref _ghostL, dt, 0.03f, 0.12f, 0.8f);
                return;
            }
            // crushed into the crater it made
            float amp = Anim.Spring(t - SpikeDur, 6f, 14f);
            _lose.Pose.Ground = _sTo;
            _lose.Pose.Height = 0f;
            _lose.Pose.Rot = -14f;
            _lose.Pose.SX = 1.16f + 0.06f * amp;
            _lose.Pose.SY = 0.76f - 0.08f * amp;
            if (_spiked)
            {
                return;
            }
            _spiked = true;
            Duelist w = _lose == P ? Q : P;
            WorldTile tile = Combat.TileAt(_sTo);
            Kit.Crater(_sTo, _lose.Px, tile, true, 5f, false, w.Pal.Edge);
            Wreck(_sTo, 1.2f, 2, w);
            Opens(w, Link.Down);
            Kit.DustRing(_sTo, _lose.Px, tile, 16, 75f, 0.7f, K);
            Kit.Debris(_sTo, _lose.Px, tile, 10, K);
            if (Vis)
            {
                DuelFx.GroundRing(_sTo, _lose.Px, 4f, 24f * K, 0.4f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(w.Pal.Edge, 0.75f), Fx.WithA(w.Pal.Edge, 0f));
                DuelFx.Embers(_sTo, _lose.Px, 8, 5f, w.Pal.Edge);
            }
            Kit.Shake(_sTo, 0.3f, 0.01f, K);
            D.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact");
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall");
        }

        public override void Abort()
        {
            D?.EndCine();
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }

    // ============================================================ catastrophes ==

    /// <summary>
    /// The set pieces of the strongest duels, where the fight leaves the place it stood and wrecks
    /// the world on its way: two Combat Gods at full scale (a body thrown 15-30 tiles, rings of
    /// craters 8 tiles out, force 4), Grandmasters a smaller version (force 3). Every one of them
    /// lands both fighters on safe ground near each other, wherever that now is, and remembers
    /// which way it carried the fight (Duel.Heading) so the next carries it on across the map
    /// rather than back. The world damage is real (Duel.Wreck: Kit.Ravage / Kit.Trench) and lands
    /// on the impact frame; anyone else caught where a catastrophe lands is hurt and thrown.
    /// With "Camera follows god abilities" on, the camera glides after it (Duel.Pan).
    /// </summary>
    internal abstract class Catastrophe : Exchange
    {
        /// <summary>The catastrophes the player has seen named (the first of each between two Gods gets a title card).</summary>
        private static readonly HashSet<string> _named = new HashSet<string>();

        private bool _follow;

        /// <summary>Two Gods: the full scale. Grandmasters: the smaller version.</summary>
        protected bool Mega => D.Gods;

        /// <summary>Does the camera glide after this one (the setting is on and the player was watching)?</summary>
        public bool Follow => _follow;

        protected float Big(float god, float gm)
        {
            return Mega ? god : gm;
        }

        public override float Weight()
        {
            if (!D.Grand)
            {
                return 0f;
            }
            float w = Fit();
            if (w <= 0f)
            {
                return 0f;
            }
            if (D.LastCat == Id)
            {
                w *= 0.25f;   // never the same catastrophe twice running, even with a blade lock between
            }
            return w * (Mega ? 1.6f : 0.55f);
        }

        /// <summary>How fitting it is here and now (terrain, history); 0: not possible.</summary>
        protected abstract float Fit();

        public sealed override void Start()
        {
            D.Cats++;
            D.LastCat = Id;
            _follow = Settings.CameraFollow && (D.Visible || D.A.isFavorite() || D.B.isFavorite());
            // the title card sits across the top of the screen: never over the fight itself (a fight
            // high on a screen the camera won't glide after waits for its name until next time)
            if (Mega && D.Visible && (_follow && !MoveCamera.hasFocusUnit() || ClearOfTitle(D.Mid)) && _named.Add(Id))
            {
                Cinema.Title(Locale.Get("sakuga_duel_" + Id), Tiers.Color(Tier.God), 1.6f, D.Mid, 0.7f);
            }
            Begin();
        }

        /// <summary>Is 'at' low enough on the screen that the fighters above it stay clear of a title card?</summary>
        private static bool ClearOfTitle(Vector2 at)
        {
            Camera cam = World.world != null ? World.world.camera : null;
            if (cam == null)
            {
                return true;
            }
            return cam.WorldToViewportPoint(new Vector3(at.x, at.y, 0f)).y < 0.6f;
        }

        protected virtual void Begin()
        {
        }

        /// <summary>The way the fight should go next: on along its heading (a little astray), else 'fallback'.</summary>
        protected Vector2 Onward(Vector2 fallback)
        {
            Vector2 h = D.Heading;
            if (h.sqrMagnitude < 0.01f)
            {
                return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector2.right;
            }
            float a = Mathf.Atan2(h.y, h.x) + Random.Range(-0.45f, 0.45f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>The fight was carried from 'from' to 'to': that is its heading now.</summary>
        protected void Carry(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            if (d.sqrMagnitude > 4f)
            {
                D.Heading = d.normalized;
            }
        }

        /// <summary>
        /// Everyone else caught where a catastrophe lands: the enemies of either fighter within 'r'
        /// tiles are hurt by it ('mult' of its damage) and thrown; those of a heavier class take a
        /// glancing blow and stagger. 'by' strikes first (null: either).
        /// </summary>
        protected void Quake(Vector2 at, float r, Duelist by, float mult)
        {
            Duelist first = by ?? P, second = first == P ? Q : P;
            HashSet<long> seen = null;
            foreach (Duelist m in new[] { first, second })
            {
                if (m == null || !m.A.isAlive())
                {
                    continue;
                }
                foreach (Actor u in Combat.FoesAround(m.A, at, r))
                {
                    long id = u.Sid();
                    if (id == P.Id || id == Q.Id || !(seen ??= new HashSet<long>()).Add(id))
                    {
                        continue;
                    }
                    if (Combat.Reach(m, u, true) != Sakuga.Contact.Hit)
                    {
                        continue;   // it answered (a Mirage Step out of it, a bodyguard): untouched
                    }
                    if (Combat.Allowed(m.A, u, out float cm))
                    {
                        Combat.Hit(m.A, u, mult * cm, true, true, 0f, m);
                        Combat.Launch(u, at, Random.Range(1f, 1.8f), Random.Range(0.6f, 1f), Random.value < 0.5f ? -600f : 600f);
                    }
                    else
                    {
                        Combat.Hit(m.A, u, mult * 0.5f, true, true, 0f, m);
                        Combat.Stagger(u, 0.4f);
                    }
                }
            }
        }

        /// <summary>A comet's tail behind a body flying through the air (call every tick; 'last' keeps where it was).</summary>
        protected void Tail(Duelist m, ref Vector2 last)
        {
            Vector2 now = m.BodyPos();
            if (last.sqrMagnitude > 0f && !m.Pose.Hidden)
            {
                DuelFx.Comet(last, now, m.Px, Kit.Core, m.Pal.Edge);
            }
            last = now;
        }

        /// <summary>
        /// A body smashed into the ground at 'at' by 'by' (null: by both): the world blasted for real
        /// (force 3, a God's 4), a glowing crater, the impact frame, rings, a dust wall, rocks,
        /// embers and cracks running out; the camera shakes. 'size' scales the picture, 'radius'
        /// the blast (tiles).
        /// </summary>
        protected void Slam(Vector2 at, Duelist by, float size, float radius)
        {
            float px = Px;
            WorldTile t = Combat.TileAt(at);
            Color edge = by != null ? by.Pal.Edge : Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f);
            Wreck(at, radius, 3, by);
            Kit.Crater(at, px * K, t, true, 9f, false, edge);
            if (Kit.Visible(at))
            {
                Vector2 up = new Vector2(0f, 3f * px * K);
                Kit.ImpactFrame(at + up, px, 0.9f * size, by != null ? by.Pal : P.Pal, false);
                DuelFx.Starburst(at + up, px, 1.5f * size * K, Kit.Core, edge, edge, 0.14f);
                DuelFx.GroundRing(at, px, 4f, 34f * size * K, 0.5f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(edge, 0.75f), Fx.WithA(edge, 0f));
                Kit.DustRing(at, px, t, 20, 95f * size, 0.8f, K);
                Kit.Debris(at, px, t, 14, K);
                DuelFx.Embers(at, px, 10, 6f, edge);
                DuelFx.Fissures(at, px, t, 2.2f * size * K, 6, edge);
            }
            Kit.Shake(at, 0.4f, 0.014f, K);
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMeteorite", t);
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", t);
        }

        /// <summary>A takeoff that blasts the ground it leaves (a hot crater, a ring, dust, rocks).</summary>
        protected void Takeoff(Vector2 at, Duelist m)
        {
            WorldTile t = Combat.TileAt(at);
            Kit.Crater(at, m.Px, t, true, 4f, true, m.Pal.Edge);
            Kit.DustRing(at, m.Px, t, 12, 60f, 0.6f, K);
            Kit.Debris(at, m.Px, t, 6, K);
            if (Kit.Visible(at))
            {
                DuelFx.GroundRing(at, m.Px, 3f, 18f * K, 0.3f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(m.Pal.Edge, 0.7f), Fx.WithA(m.Pal.Edge, 0f));
            }
            Wreck(at, 0.7f, 2, m);
        }

        /// <summary>
        /// Getting up out of a crater or a furrow: from flat on its back ('flat') or a crouch, onto its
        /// feet, hopping out to 'to' when the ground it lies on burns; k 0..1.
        /// </summary>
        protected static void GetUp(Duelist m, Vector2 from, Vector2 to, bool flat, float k)
        {
            float up = Anim.OutQuad(Mathf.Clamp01(k / 0.75f));
            m.Pose.Ground = Vector2.Lerp(from, to, up);
            m.Pose.Height = (to - from).sqrMagnitude > 0.04f ? 0.2f * m.K * Mathf.Sin(Mathf.PI * up) : 0f;
            m.Pose.Rot = Mathf.Lerp(flat ? -80f : -18f, 0f, up);
            m.Pose.SX = Mathf.Lerp(flat ? 1f : 1.16f, 1.03f, up);
            m.Pose.SY = Mathf.Lerp(flat ? 1f : 0.84f, 0.96f, up);
            if (k >= 1f)
            {
                m.Pose.Rot = 0f;
                m.Pose.SX = m.Pose.SY = 1f;
            }
        }

        public override void Abort()
        {
            foreach (Duelist m in new[] { P, Q })
            {
                if (m == null)
                {
                    continue;
                }
                m.Pose.Hidden = false;
                m.Blur = false;
                m.Glow = DuelFx.Clear;
            }
        }
    }

    /// <summary>
    /// SKYFALL (catastrophe). One drops under the other's guard and launches it across the world.
    ///
    ///   CLOSE  ~0.15   into reach; the bind (a small starburst)
    ///   WINDUP  0.16   the launcher drops low, glowing white-hot; a glint
    ///   LAUNCH         the blow: an impact frame, a world hit-stop, a starburst and a ring, the
    ///                  ground where it stood blasted; the other is thrown 15-30 tiles (Grandmasters
    ///                  8-14) on a high arc (3-5 tiles up), tumbling, a comet's tail of fire in the
    ///                  launcher's colour behind it, speed lines; between near-equals it rights
    ///                  itself in the air and comes down feet first. The launcher holds its follow-
    ///                  through, then blinks after it: a pillar of light, its afterimage left behind
    ///   CRASH          the body hits the far ground: a crater blasted for real (force 3, a God's 4),
    ///                  the impact frame, rings, a dust wall, rocks, embers, cracks; anyone else
    ///                  there is hurt and thrown
    ///   PLOUGH  0.40   it ploughs on 2-3 tiles on its back (or skidding on its feet), a trench torn
    ///                  for real every tile, clods and dust thrown out to both sides, a smoking
    ///                  fissure left along it and a second crater where it stops
    ///   RISE   ~0.35   the launcher drops out of its blink beside it (the streak of the blink drawn
    ///                  across the world behind it), lands in a crouch; the other gets up out of the
    ///                  furrow (hopping out if it burns). The launcher may chain onto it (Down)
    /// The landing is roomy safe ground well inside the map, chosen along the duel's heading.
    /// </summary>
    internal sealed class Skyfall : Catastrophe
    {
        private const int Close = 0, Windup = 1, Flight = 2, Plough = 3, Rise = 4;

        private Clash _c;
        private bool _aLaunches;
        private Duelist _w, _l;
        /// <summary>Where they start, their stance spots, the flight's direction, the touchdown and the furrow's end.</summary>
        private Vector2 _w0, _v0, _sw, _sv, _dir, _t0, _t1;
        private float _tClose, _tFly, _apex, _tPlough, _ghost, _dust, _lines;
        private Vector2 _lastBody, _seg, _arrive, _rise;
        private bool _gone, _shown;

        public override string Id => "skyfall";

        public override Form Forms => Form.Aerial | Form.Overhead | Form.Throw | Form.Ultimate;

        protected override float Fit()
        {
            return 1.4f;
        }

        public override bool Plan()
        {
            _c = D.Roll();
            _aLaunches = _c.Even ? Random.value < 0.5f : _c.AWins;
            _w0 = _aLaunches ? A0 : B0;
            _v0 = _aLaunches ? B0 : A0;
            Vector2 axis = _v0 - _w0;
            axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector2.right;
            float min = Big(15f, 8f);
            float max = Big(Random.Range(22f, 30f), Random.Range(11f, 14f)) * (_c.Kind == ClashKind.Overwhelm ? 1f : _c.Even ? 0.8f : 0.92f);
            max = Mathf.Max(min + 1f, max);
            // the one launched: where it stands, or the nearest ground it can stand on
            if (Combat.SafeSpot(_v0))
            {
                _sv = _v0;
            }
            else if (!Combat.FindLanding(_v0, 1.5f, out _sv))
            {
                return false;
            }
            if (!DuelGeo.Far(_sv, Onward(axis), min, max, 1.2f, out _t1))
            {
                return false;
            }
            _dir = (_t1 - _sv).normalized;
            // the launcher steps in behind it, opposite the way it will fly
            bool ok = false;
            foreach (float g in new[] { 0.62f, 0.45f })
            {
                Vector2 p = Combat.Clamp(_sv - _dir * g * K);
                if (Combat.SafeSpot(p) && Combat.ClearPath(_w0, p))
                {
                    _sw = p;
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                return false;
            }
            // the furrow it ploughs at the end (shorter where the ground doesn't allow it)
            _t0 = _t1 - _dir * Big(3.2f, 2f) * K;
            if (!Combat.ClearPath(_t0, _t1))
            {
                _t0 = _t1 - _dir * K;
                if (!Combat.ClearPath(_t0, _t1))
                {
                    _t0 = _t1;
                }
            }
            float dist = (_t0 - _sv).magnitude;
            _tFly = Mathf.Clamp(0.5f + 0.022f * dist, 0.75f, 1.2f);
            _apex = (Big(3.2f, 2.3f) + 0.05f * dist) * K;
            _tPlough = (_t1 - _t0).sqrMagnitude > 0.04f ? 0.4f : 0.12f;
            float d = Mathf.Max((_sw - _w0).magnitude, (_sv - _v0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.05f;
            return true;
        }

        protected override void Begin()
        {
            _w = _aLaunches ? P : Q;
            _l = _aLaunches ? Q : P;
            D.Count(_c, true);
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Close:
                    {
                        float k = Pt / _tClose;
                        Reposition(_w, _w0, _sw, k, _sv);
                        Reposition(_l, _v0, _sv, k, _sw);
                        FaceEach();
                        Trail(_w, ref _ghost, dt, 0.025f, 0.13f);
                        if (k >= 1f)
                        {
                            Meet(Contact(), 0.35f * K, new Clash { Kind = ClashKind.Even }, 5);
                            Stop(0.04f);
                            Next(Windup);
                        }
                        break;
                    }
                case Windup:
                    {
                        // the launcher drops under the other's guard, glowing white-hot; the other reels
                        float k = Mathf.Clamp01(Pt / 0.16f);
                        float e = Anim.OutQuad(k);
                        _w.Pose.Ground = _sw;
                        _l.Pose.Ground = _sv;
                        _w.Pose.Height = _l.Pose.Height = 0f;
                        FaceEach();
                        _w.Pose.Rot = -14f * e;
                        _w.Pose.SX = 1f + 0.18f * e;
                        _w.Pose.SY = 1f - 0.2f * e;
                        _l.Pose.Rot = -10f * e;
                        _l.Pose.SX = 1f;
                        _l.Pose.SY = 1f + 0.04f * e;
                        _w.Glow = Fx.WithA(Color.Lerp(_w.Pal.Edge, Color.white, k), 0.4f * k);
                        if (Enter() && Vis)
                        {
                            Fx.Glint(_w.HeadPos() + new Vector2(_w.Fwd * 2f * px, 0f), px, 0.16f, _w.Pal.Core, 0.95f);
                        }
                        if (k >= 1f)
                        {
                            Launch();
                            Next(Flight);
                        }
                        break;
                    }
                case Flight:
                    FlightTick(dt);
                    break;
                case Plough:
                    PloughTick(dt);
                    break;
                default:
                    RiseTick();
                    break;
            }
        }

        /// <summary>The blow that sends it across the world.</summary>
        private void Launch()
        {
            float px = Px;
            Vector2 at = _l.BodyPos();
            Color edge = _w.Pal.Edge;
            _w.Glow = DuelFx.Clear;
            Slash(_w, 1, 0.18f);
            D.Sound("event:/SFX/HIT/HitGeneric");
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionForce");
            if (Vis)
            {
                WorldTile t = Combat.TileAt(_sv);
                Kit.ImpactFrame(at, px, 1.1f, _w.Pal, false);
                DuelFx.Starburst(at, px, 1.6f * K, Kit.Core, edge, edge, 0.14f);
                DuelFx.GroundRing(_sv, px, 4f, 34f * K, 0.45f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(edge, 0.75f), Fx.WithA(edge, 0f));
                Kit.DustRing(_sv, px, t, 18, 85f, 0.7f, K);
                Kit.Debris(_sv, px, t, 10, K);
                Fx.FocusLines(at, px, Fx.Count(16), 14f * K, 30f * K, 0.16f);
                Kit.SpeedLines(at, _dir + Vector2.up, px, 6, 6f, 0.2f, Fx.WithA(edge, 0.8f));
            }
            Kit.Shake(at, 0.35f, 0.012f, K);
            Kit.Stop(at, 0.1f);
            Wreck(_sv, 0.8f, 3, _w);
            D.Chip(_w, _l, _c.Kind == ClashKind.Overwhelm ? 0.6f : _c.Even ? 0.25f : 0.45f, true);
            // it flies facing back the way it came (at the one who hit it)
            if (Mathf.Abs(_dir.x) > 0.05f)
            {
                _l.Pose.FaceRight = _dir.x < 0f;
            }
            _lastBody = _l.BodyPos();
            Opens(_w, Link.Down);
        }

        private void FlightTick(float dt)
        {
            float px = Px;
            float k = Mathf.Clamp01(Pt / _tFly);
            _l.Pose.Ground = Vector2.Lerp(_sv, _t0, k);
            _l.Pose.Height = _apex * 4f * k * (1f - k);
            // tumbling backward (it lands flat on its back), or between near-equals righting itself
            _l.Pose.Rot = !_c.Even ? -800f * Anim.InOutSine(k) : -360f * Anim.OutQuad(Mathf.Clamp01(k / 0.7f));
            _l.Pose.SX = 0.92f;
            _l.Pose.SY = 1.1f;
            Trail(_l, ref _ghost, dt, 0.025f, 0.2f);
            Tail(_l, ref _lastBody);
            _lines -= dt;
            if (Vis && _lines <= 0f)
            {
                _lines = 0.05f;
                Kit.SpeedLines(_l.BodyPos(), _t0 - _sv, px, 2, 5f, 0.16f, Fx.WithA(_w.Pal.Edge, 0.7f));
            }
            if (!_gone)
            {
                // the follow-through, rising with the blow; then it is gone after the other
                float f = Mathf.Clamp01(Pt / 0.14f);
                _w.Pose.Ground = _sw;
                _w.Pose.Height = 0.25f * K * Anim.OutQuad(f);
                _w.Pose.Rot = Mathf.Lerp(-14f, -26f, f);
                _w.Pose.SX = Mathf.Lerp(1.18f, 0.82f, f);
                _w.Pose.SY = Mathf.Lerp(0.8f, 1.28f, f);
                if (Pt >= 0.14f)
                {
                    Vanish();
                }
            }
            else
            {
                // unseen, it travels with the other
                _w.Pose.Ground = _l.Pose.Ground;
                _w.Pose.Height = 0f;
            }
            if (k >= 1f)
            {
                Crash();
                Next(Plough);
            }
        }

        private void Vanish()
        {
            float px = Px;
            _gone = true;
            if (Vis)
            {
                Fx.Afterimage(_w, 0.4f, Fx.WithA(_w.Pal.Ghost, 0.85f), Fx.WithA(_w.Pal.Ghost, 0.5f), _w.Pal.GhostEnd);
                DuelFx.Blink(_sw, px, K, _w.Pal.Core, _w.Pal.Edge);
                Kit.SpeedLines(_w.BodyPos(), _dir, px, 4, 3f, 0.14f, Fx.WithA(_w.Pal.Core, 0.85f));
            }
            _w.Pose.Hidden = true;
            _w.Blur = true;
            _w.Pose.Height = 0f;
            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
        }

        private void Crash()
        {
            _l.Pose.Ground = _t0;
            _l.Pose.Height = 0f;
            _l.Pose.Rot = _c.Even ? -18f : -80f;
            Slam(_t0, _w, 1f, 1.4f);
            Kit.Stop(_t0, 0.08f);
            D.Chip(_w, _l, 0.25f, true);
            Quake(_t0, Big(2.5f, 1.8f), _w, Big(1.2f, 0.8f));
            _seg = _t0;
        }

        private void PloughTick(float dt)
        {
            float px = Px;
            float k = Mathf.Clamp01(Pt / _tPlough);
            Vector2 g = Vector2.Lerp(_t0, _t1, Anim.OutCubic(k));
            _l.Pose.Ground = g;
            _l.Pose.Height = 0f;
            if (_c.Even)
            {
                // skidding on its feet, crouched, a hand in the dirt
                _l.Pose.Rot = -18f;
                _l.Pose.SX = 1.16f;
                _l.Pose.SY = 0.84f;
            }
            else
            {
                _l.Pose.Rot = -80f + 4f * Mathf.Sin(T * 50f);
                _l.Pose.SX = _l.Pose.SY = 1f;
            }
            _w.Pose.Ground = g;
            // the furrow: torn up for real every tile, clods and dust thrown out to both sides
            if ((g - _seg).sqrMagnitude >= 1f)
            {
                Wreck(g, 0.7f, 3, _w);
                _seg = g;
            }
            _dust -= dt;
            if (_dust <= 0f && k < 1f)
            {
                _dust = 0.03f;
                DuelFx.Plough(g, _dir, px, Combat.TileAt(g), K);
            }
            if (k < 1f)
            {
                return;
            }
            if ((_t1 - _t0).sqrMagnitude > 0.04f)
            {
                WorldTile t = Combat.TileAt(_t1);
                DuelFx.Fissure(_t0, _t1, px, t, _w.Pal.Edge, 3);
                Kit.Crater(_t1, px * K, t, true, 6f, true, _w.Pal.Edge);
                Wreck(_t1, 1.1f, 3, _w);
                Kit.Shake(_t1, 0.2f, 0.008f, K);
                Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", t);
            }
            // after the world has given: where each will stand
            _rise = DuelGeo.Footing(_t1);
            _arrive = Arrival(_rise);
            Appear();
            Next(Rise);
        }

        /// <summary>Where the launcher drops out of its blink: beside the other, on ground that holds.</summary>
        private Vector2 Arrival(Vector2 near)
        {
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            Vector2[] tries = { near - _dir * 1.1f * K, near + side * 1.1f * K, near - side * 1.1f * K, near + _dir * 1.1f * K };
            foreach (Vector2 t in tries)
            {
                Vector2 p = Combat.Clamp(t);
                if (Combat.SafeSpot(p))
                {
                    return p;
                }
            }
            return Combat.FindLanding(near + side * 0.8f * K, 1.5f, out Vector2 s) && (s - near).sqrMagnitude > 0.2f ? s : near;
        }

        private void Appear()
        {
            float px = Px;
            _w.Pose.Hidden = false;
            _w.Blur = false;
            _w.Pose.Ground = _arrive;
            _w.Pose.Height = 0.4f * K;
            _w.Pose.Rot = 8f;
            _w.Pose.SX = 0.9f;
            _w.Pose.SY = 1.12f;
            Face(_w, _rise);
            DuelFx.Blink(_arrive, px, K, _w.Pal.Core, _w.Pal.Edge);
            // the streak of its blink, drawn across the world from where it struck
            Vector2 a = _sw + new Vector2(0f, 5f * px * K), b = _arrive + new Vector2(0f, 5f * px * K);
            if (Kit.Visible(a, 0.2f) || Kit.Visible(b, 0.2f) || Kit.Visible((a + b) * 0.5f, 0.2f))
            {
                Kit.Line(a, b, px, 0.32f, 1, FxLayer.Top, 3, Color.white, Fx.WithA(_w.Pal.Edge, 0.8f), Fx.WithA(_w.Pal.Edge, 0f));
            }
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(_arrive));
        }

        private void RiseTick()
        {
            float px = Px;
            float dur = Mathf.Max(0.3f, 0.6f * Brisk + 0.1f);
            float k = Mathf.Clamp01(Pt / dur);
            GetUp(_l, _t1, _rise, !_c.Even, k);
            // the launcher drops out of its blink into a crouch, and straightens
            float drop = Mathf.Clamp01(Pt / 0.1f);
            _w.Pose.Ground = _arrive;
            if (drop < 1f)
            {
                _w.Pose.Height = 0.4f * K * (1f - drop * drop);
            }
            else
            {
                if (!_shown)
                {
                    _shown = true;
                    WorldTile t = Combat.TileAt(_arrive);
                    DuelFx.Puff(_arrive, px, t, 3, 14f);
                    Kit.DustRing(_arrive, px, t, 8, 40f, 0.4f, K);
                }
                Settle(_w, Pt - 0.1f, 0f);
            }
            if (k > 0.55f)
            {
                FaceEach();
            }
            else
            {
                Face(_w, _l.Pose.Ground);
            }
            if (k >= 1f)
            {
                Carry(_sv, _t1);
                Done = true;
            }
        }
    }

    /// <summary>
    /// TECTONIC CLASH (catastrophe). Both leap and strike the ground at the same spot at once, and
    /// the earth breaks in rings around them.
    ///
    ///   CLOSE  ~0.15   1.4 tiles apart
    ///   RISE    0.36   both spring high (1.7 tiles; Grandmasters 1.2), arms raised, glowing; the
    ///                  telegraph: a ring 8 tiles wide (5.5) closing in on the spot, dust drawn in,
    ///                  the ground rumbling, pebbles hopping
    ///   DIVE    0.09   both drive down fist first
    ///   QUAKE          the clash rule decides it: a crater blasted at the centre (force 3, a God's
    ///                  4), the impact frame, a world hit-stop, a starburst in both colours, cracks
    ///                  running out 8 tiles; anyone else within the second ring is hurt and thrown;
    ///                  then three rings of blast craters break out, 0.14 s apart, at 2.5, 5 and 8
    ///                  tiles (1.8, 3.5, 5.5), each with its own shockwave and shake, throwing
    ///                  whoever stands in the wave. Both are blown back in backflips (the loser
    ///                  farther, the winner may chain onto it)
    ///   SETTLE  brisk
    /// It wants solid ground all round to break (not over water or among the mountains).
    /// </summary>
    internal sealed class Tectonic : Catastrophe
    {
        private const int Close = 0, Rise = 1, Dive = 2, Waves = 3, End = 4;

        private Vector2 _sa, _sb, _axis, _mid;
        private float _tClose, _h, _wave, _rot, _rumble;
        private float[] _radii;
        private int[] _counts;
        private int _next;
        private Clash _c;

        public override string Id => "tectonic";

        public override Form Forms => Form.Ground | Form.Area | Form.Heavy | Form.Ultimate;

        protected override float Fit()
        {
            Vector2 mid = (A0 + B0) * 0.5f;
            float r = Big(5f, 3.5f);
            int n = 0;
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                if (Combat.SafeSpot(mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * r))
                {
                    n++;
                }
            }
            float frac = n / 12f;
            return frac < 0.4f ? 0f : 0.6f + frac;
        }

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 1.4f * K, out _sa, out _sb))
            {
                return false;
            }
            _mid = (_sa + _sb) * 0.5f;
            if (!Combat.SafeSpot(_mid))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            _h = Big(1.7f, 1.2f) * K;
            _radii = Mega ? new[] { 2.5f, 5f, 8f } : new[] { 1.8f, 3.5f, 5.5f };
            _counts = Mega ? new[] { 4, 6, 8 } : new[] { 3, 5, 6 };
            _rot = Random.Range(0f, Mathf.PI * 2f);
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.06f;
            return true;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Close:
                    {
                        float k = Pt / _tClose;
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.03f, 0.12f);
                        Trail(Q, ref GhostB, dt, 0.03f, 0.12f);
                        if (k >= 1f)
                        {
                            Next(Rise);
                        }
                        break;
                    }
                case Rise:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/POWERS/Powerup");
                            DuelFx.Puff(_sa, P.Px, Combat.TileAt(_sa), 3, 14f);
                            DuelFx.Puff(_sb, Q.Px, Combat.TileAt(_sb), 3, 14f);
                            if (Kit.Visible(_mid, 0.6f))
                            {
                                // the telegraph: the whole ring that will break, closing in on the spot
                                Color c = Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f);
                                Fx.Ring(_mid, px, 0.36f, Mathf.Min(140f, _radii[2] * K / px), 6f, 1, 2, FxLayer.Ground, 4, Art.Ground, 1.4f,
                                    Fx.WithA(c, 0f), Fx.WithA(c, 0.7f), Fx.WithA(Kit.Core, 0.95f));
                                DuelFx.Swirl(_mid, px, Combat.TileAt(_mid), 1.6f * K, 8, 0.36f, 9f);
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.36f);
                        float up = Anim.OutQuad(k);
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = _h * up;
                            m.Pose.Rot = -30f * up;
                            m.Pose.SX = Mathf.Lerp(1f, 0.86f, up);
                            m.Pose.SY = Mathf.Lerp(1f, 1.18f, up);
                            m.Glow = Fx.WithA(Color.Lerp(m.Pal.Edge, Color.white, 0.3f * k), 0.38f * k);
                        }
                        Trail(P, ref GhostA, dt, 0.04f, 0.14f, 0.7f);
                        Trail(Q, ref GhostB, dt, 0.04f, 0.14f, 0.7f);
                        _rumble -= dt;
                        if (_rumble <= 0f)
                        {
                            // the ground rumbles; pebbles hop where it will break
                            _rumble = 0.08f;
                            Kit.Shake(_mid, 0.08f, 0.003f, K);
                            float a = Random.Range(0f, Mathf.PI * 2f);
                            Vector2 p = _mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Random.Range(1f, _radii[1]) * K;
                            Kit.Debris(p, px, Combat.TileAt(p), 2, 0.5f);
                        }
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Slash(P, 0, 0.12f);
                            Slash(Q, 0, 0.12f);
                            Next(Dive);
                        }
                        break;
                    }
                case Dive:
                    {
                        float k = Mathf.Clamp01(Pt / 0.09f);
                        P.Pose.Ground = Vector2.Lerp(_sa, _mid - _axis * 0.32f * K, k);
                        Q.Pose.Ground = Vector2.Lerp(_sb, _mid + _axis * 0.32f * K, k);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = _h * (1f - k * k);
                            m.Pose.Rot = Mathf.Lerp(-30f, 55f, Anim.OutQuad(k));
                            m.Pose.SX = Mathf.Lerp(0.86f, 1.25f, k);
                            m.Pose.SY = Mathf.Lerp(1.18f, 0.8f, k);
                        }
                        Trail(P, ref GhostA, dt, 0.02f, 0.12f);
                        Trail(Q, ref GhostB, dt, 0.02f, 0.12f);
                        if (k >= 1f)
                        {
                            Impact();
                            Next(Waves);
                        }
                        break;
                    }
                case Waves:
                    {
                        _wave -= dt;
                        if (_next < _radii.Length && _wave <= 0f)
                        {
                            Wave(_next);
                            _next++;
                            _wave = 0.14f;
                        }
                        bool over = Recoil(dt);
                        if (KA.Air && !KA.Landed || KB.Air && !KB.Landed)
                        {
                            Trail(P, ref GhostA, dt, 0.05f, 0.12f, 0.5f);
                            Trail(Q, ref GhostB, dt, 0.05f, 0.12f, 0.5f);
                        }
                        if (over && _next >= _radii.Length)
                        {
                            Next(End);
                        }
                        break;
                    }
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.4f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void Impact()
        {
            float px = Px;
            _c = D.Resolve(true);
            Duelist w = _c.Even ? null : Winner(_c);
            Color edge = w != null ? w.Pal.Edge : Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f);
            WorldTile tile = Combat.TileAt(_mid);
            P.Glow = Q.Glow = DuelFx.Clear;
            // the centre gives first, for real
            Wreck(_mid, Big(2f, 1.5f), 3, w);
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionBig");
            D.Sound("event:/SFX/NATURE/EarthQuake");
            if (Kit.Visible(_mid, 0.5f))
            {
                Vector2 up = new Vector2(0f, 3f * px * K);
                Kit.Crater(_mid, px * K, tile, true, 9f, false, edge);
                Kit.ImpactFrame(_mid + up, px, 1.3f, w != null ? w.Pal : P.Pal, false);
                DuelFx.Starburst(_mid + up, px, 2.5f * K, Kit.Core, P.Pal.Edge, Q.Pal.Edge, 0.16f);
                DuelFx.GroundRing(_mid, px, 4f, Mathf.Min(140f, _radii[0] * K / px), 0.45f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(edge, 0.75f), Fx.WithA(edge, 0f));
                Kit.DustRing(_mid, px, tile, 24, 110f, 0.9f, K);
                Kit.Debris(_mid, px, tile, 18, K);
                DuelFx.Embers(_mid, px, 12, 7f, edge);
                DuelFx.Fissures(_mid, px, tile, _radii[_radii.Length - 1] * K, 8, edge);
            }
            Kit.Shake(_mid, 0.6f, 0.018f, K);
            Kit.Stop(_mid, 0.12f);
            Quake(_mid, _radii[1] * K, w, Big(1.3f, 0.9f));
            // both blown back out of what they broke (past the burning centre): the loser farther,
            // flipping; the winner may follow it up
            float da = Big(3.6f, 2.4f), db = da;
            if (w != null)
            {
                Duelist l = Loser(_c);
                Strike(w, l, _c.Kind == ClashKind.Overwhelm ? 0.65f : 0.5f, true);
                Opens(w, Link.Down);
                float lose = Big(4.4f, 3f), win = Big(3.2f, 2.1f);
                da = _c.AWins ? win : lose;
                db = _c.AWins ? lose : win;
            }
            else
            {
                D.Chip(P, Q, 0.18f, false);
                D.Chip(Q, P, 0.18f, false);
            }
            Vector2 pa = P.Pose.Ground, pb = Q.Pose.Ground;
            Vector2 la = DuelGeo.Land(pa, -_axis, da * K), lb = DuelGeo.Land(pb, _axis, db * K);
            float wob = 0.3f * Mathf.Max(0.5f, Brisk);
            KA = (la - pa).sqrMagnitude > 0.04f ? Knock.Fly(pa, la, 0.36f + 0.08f * da, (0.7f + 0.2f * da) * K, -360f, wob) : Knock.Skid(pa, pa, 0.25f, -12f, wob);
            KB = (lb - pb).sqrMagnitude > 0.04f ? Knock.Fly(pb, lb, 0.36f + 0.08f * db, (0.7f + 0.2f * db) * K, -360f, wob) : Knock.Skid(pb, pb, 0.25f, -12f, wob);
            _next = 0;
            _wave = 0.1f;
        }

        /// <summary>The i-th ring of the quake: blast craters all round it, its shockwave, whoever stands in it thrown.</summary>
        private void Wave(int i)
        {
            float px = Px;
            float r = _radii[i] * K;
            int n = _counts[i];
            Duelist by = _c.Even ? null : Winner(_c);
            Color edge = by != null ? by.Pal.Edge : Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f);
            int force = i < 2 ? 3 : 2;
            for (int j = 0; j < n; j++)
            {
                float a = _rot + i * 0.4f + (j + Random.Range(-0.25f, 0.25f)) / n * Mathf.PI * 2f;
                Vector2 p = _mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * r * Random.Range(0.85f, 1.1f);
                if (!DuelGeo.Inside(p, 1f) || (p - KA.To).sqrMagnitude < 2.25f || (p - KB.To).sqrMagnitude < 2.25f)
                {
                    continue;   // (never right where one of them comes down)
                }
                WorldTile t = Combat.TileAt(p);
                Wreck(p, 1f, force, by);
                Kit.Crater(p, px, t, i < 2, 5f, true, edge);
                Kit.DustRing(p, px, t, i == 2 ? 3 : 5, 45f, 0.5f);
                Kit.Debris(p, px, t, i == 2 ? 2 : 3);
            }
            if (Kit.Visible(_mid, 0.6f))
            {
                float rx = Mathf.Min(140f, r / px);
                DuelFx.GroundRing(_mid, px, rx * 0.75f, rx, 0.35f, 2, 1, Fx.WithA(Kit.Core, 0.9f), Fx.WithA(edge, 0.6f), Fx.WithA(edge, 0f));
            }
            Kit.Shake(_mid, 0.15f, 0.009f - 0.002f * i, K);
            D.Sound(i == 0 ? "event:/SFX/EXPLOSIONS/ExplosionMiddle" : "event:/SFX/HIT/HitStone");
            // whoever stands where the wave passes is thrown (the first quake already hurt the near ones)
            float inner = (i == 0 ? 0f : _radii[i - 1] * K);
            foreach (Duelist m in new[] { P, Q })
            {
                foreach (Actor u in Combat.FoesAround(m.A, _mid, r + 0.8f))
                {
                    if (u.Sid() == P.Id || u.Sid() == Q.Id || (u.current_position - _mid).sqrMagnitude < inner * inner)
                    {
                        continue;
                    }
                    Combat.Launch(u, _mid, Random.Range(0.8f, 1.4f), Random.Range(0.5f, 0.8f), Random.value < 0.5f ? -480f : 480f);
                }
            }
        }
    }

    /// <summary>
    /// SKY BATTLE (catastrophe). Both take it high into the sky, clash again and again, and come
    /// down as two meteors far apart; then they race back and meet on the ground.
    ///
    ///   CHARGE  0.20   crouched, glowing; rings at their feet
    ///   RISE    0.50   both rocket 6.5 tiles up (Grandmasters 4.5): takeoff craters blasted for
    ///                  real, comet tails, afterimages
    ///   CLASH x5 (x4)  0.11 rushes and mid-air meetings drifting across the sky, each starburst
    ///                  bigger (1.2 to 3 tiles), air rings, rings and dust on the ground far below,
    ///                  the shake growing; the clash rule each time (the loser flashes, chipped);
    ///                  the last freezes the world on an impact frame and its shockwave scorches the
    ///                  ground wide below
    ///   PART    0.30   blasted apart and up, backflips
    ///   METEORS ~0.6   both dive head first to landing spots 6-10 tiles (3.5-6) to either side,
    ///                  fire trailing: each a crater blasted for real (force 3, a God's 4), an impact
    ///                  frame, rings, a dust wall, cracks; anyone near hurt and thrown; a superhero
    ///                  landing in it
    ///   BACK    0.30   both blur back toward each other in low leaps over whatever lies between
    ///   CLASH          they meet on the ground: the clash rule decides it, the aftermath throws them
    ///   SETTLE  brisk
    /// Fitting most where the ground is broken (mountains, water, fire about: they go up).
    /// </summary>
    internal sealed class SkyBattle : Catastrophe
    {
        private const int Charge = 0, RiseUp = 1, Rush = 2, Bounce = 3, Part = 4, Fall = 5, Back = 6, Clashing = 7, End = 8;

        private Vector2 _mid, _axis, _la, _lb, _meet, _ma, _mb;
        private Vector2[] _spots;
        private float[] _heights;
        private int _n, _i;
        private float _h, _swirl;
        private Vector2 _fa, _fb, _ta, _tb;
        private float _hfa, _hfb, _hta, _htb, _dur;
        private Clash _c;
        private Duelist _lose;
        private Vector2 _tailA, _tailB;
        private bool _downA, _downB;
        private float _atA, _atB;

        public override string Id => "sky_battle";

        public override Form Forms => Form.Aerial | Form.Flurry | Form.Ki | Form.Heavy;

        protected override float Fit()
        {
            // they take it to the sky most gladly where the ground is broken
            Vector2 mid = (A0 + B0) * 0.5f;
            int bad = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                if (!Combat.SafeSpot(mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * 3f))
                {
                    bad++;
                }
            }
            return 1f + 0.12f * bad;
        }

        public override bool Plan()
        {
            if ((B0 - A0).sqrMagnitude > 9f * 9f)
            {
                return false;
            }
            Vector2 d = B0 - A0;
            _axis = Mathf.Abs(d.x) > 0.05f ? new Vector2(Mathf.Sign(d.x), 0f) : Vector2.right;
            _mid = (A0 + B0) * 0.5f;
            _h = Big(6.5f, 4.5f) * K;
            _n = Mega ? 5 : 4;
            _spots = new Vector2[_n];
            _heights = new float[_n];
            Vector2 s = _mid;
            for (int i = 0; i < _n; i++)
            {
                if (i > 0)
                {
                    // the fight drifts across the sky
                    Vector2 step = new Vector2(Random.Range(1.2f, 2.4f) * (Random.value < 0.5f ? -1f : 1f), Random.Range(-0.8f, 0.8f)) * K;
                    Vector2 t = s + step;
                    if (!DuelGeo.Inside(t, 3f))
                    {
                        t = s - step;
                    }
                    if (DuelGeo.Inside(t, 3f))
                    {
                        s = t;
                    }
                }
                _spots[i] = s;
                _heights[i] = _h + Random.Range(-0.8f, 0.8f) * K;
            }
            Vector2 last = _spots[_n - 1];
            float min = Big(6f, 3.5f), max = Big(10f, 6f);
            if (!DuelGeo.Far(last, -_axis, min, max, 0.9f, out _la) || !DuelGeo.Far(last, _axis, min, max, 0.9f, out _lb))
            {
                return false;
            }
            if ((_lb - _la).sqrMagnitude < min * min * 1.96f)
            {
                return false;   // far apart, or not at all
            }
            // where they meet again: somewhere between their craters
            return Combat.FindLanding(Vector2.Lerp(_la, _lb, Random.Range(0.35f, 0.65f)), 2f, out _meet);
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Charge:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/POWERS/Powerup");
                            if (Vis)
                            {
                                foreach (Duelist m in new[] { P, Q })
                                {
                                    Fx.Ring(m.Pose.Ground, m.Px, 0.2f, 16f * K, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                                        Fx.WithA(m.Pal.Edge, 0f), Fx.WithA(m.Pal.Edge, 0.85f), Fx.WithA(Kit.Core, 1f));
                                }
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.2f);
                        P.Pose.Ground = A0;
                        Q.Pose.Ground = B0;
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = 0f;
                            m.Pose.SX = Mathf.Lerp(1f, 1.16f, Anim.OutQuad(k));
                            m.Pose.SY = Mathf.Lerp(1f, 0.8f, Anim.OutQuad(k));
                            m.Pose.Rot = -6f * k;
                            m.Glow = Fx.WithA(Color.Lerp(m.Pal.Edge, Color.white, 0.3f), 0.45f * k);
                        }
                        float j = ((int)(T * 60f) & 1) == 0 ? 0.5f : -0.5f;
                        P.Pose.Nudge = new Vector2(j, 0f);
                        Q.Pose.Nudge = new Vector2(-j, 0f);
                        _swirl -= dt;
                        if (_swirl <= 0f)
                        {
                            _swirl = 0.1f;
                            DuelFx.Swirl(A0, P.Px, Combat.TileAt(A0), 0.8f * K, 3, 0.3f, 8f);
                            DuelFx.Swirl(B0, Q.Px, Combat.TileAt(B0), 0.8f * K, 3, 0.3f, -8f);
                        }
                        if (k >= 1f)
                        {
                            Next(RiseUp);
                        }
                        break;
                    }
                case RiseUp:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Takeoff(A0, P);
                            Takeoff(B0, Q);
                            Leg(A0, B0, 0f, 0f, _spots[0] - _axis * 1.1f * K, _spots[0] + _axis * 1.1f * K, _heights[0], _heights[0], 0.5f);
                            _tailA = P.BodyPos();
                            _tailB = Q.BodyPos();
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutCubic(k));
                        float s = 1f - k;
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.SX = Mathf.Lerp(1f, 0.72f, s);
                            m.Pose.SY = Mathf.Lerp(1f, 1.4f, s);
                            m.Pose.Rot = Mathf.Lerp(6f, -4f, s);
                        }
                        Tail(P, ref _tailA);
                        Tail(Q, ref _tailB);
                        Trail(P, ref GhostA, dt, 0.025f, 0.18f);
                        Trail(Q, ref GhostB, dt, 0.025f, 0.18f);
                        if (k >= 1f)
                        {
                            _i = 0;
                            Next(Rush);
                        }
                        break;
                    }
                case Rush:
                    {
                        if (Enter())
                        {
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height,
                                _spots[_i] - _axis * 0.32f * K, _spots[_i] + _axis * 0.32f * K, _heights[_i], _heights[_i], 0.11f);
                        }
                        Fly(Anim.InQuad(Mathf.Clamp01(Pt / _dur)));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = 24f;
                            m.Pose.SX = 1.3f;
                            m.Pose.SY = 0.82f;
                        }
                        Trail(P, ref GhostA, dt, 0.015f, 0.14f);
                        Trail(Q, ref GhostB, dt, 0.015f, 0.14f);
                        if (Pt >= _dur)
                        {
                            Hit();
                            Next(Bounce);
                        }
                        break;
                    }
                case Bounce:
                    {
                        if (Enter())
                        {
                            float wa = 0.9f, wb = 0.9f;
                            if (_lose != null)
                            {
                                wa = _lose == P ? 1.3f : 0.6f;
                                wb = _lose == Q ? 1.3f : 0.6f;
                            }
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height,
                                P.Pose.Ground - _axis * wa * K, Q.Pose.Ground + _axis * wb * K, P.Pose.Height + 0.2f * K, Q.Pose.Height + 0.2f * K, 0.22f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutCubic(k));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = m == _lose ? -200f * Anim.OutQuad(k) * (1f - k) : Mathf.Lerp(-30f, 6f, Anim.OutQuad(k));
                            m.Pose.SX = m.Pose.SY = 1f;
                        }
                        if (k >= 1f)
                        {
                            _i++;
                            Next(_i < _n ? Rush : Part);
                        }
                        break;
                    }
                case Part:
                    {
                        if (Enter())
                        {
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height,
                                P.Pose.Ground - _axis * 1.6f * K, Q.Pose.Ground + _axis * 1.6f * K, P.Pose.Height + 0.6f * K, Q.Pose.Height + 0.6f * K, 0.3f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(Anim.OutQuad(k));
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Rot = -360f * Anim.InOutCubic(k);
                            m.Pose.SX = 1f;
                            m.Pose.SY = 1f - 0.12f * Mathf.Sin(Mathf.PI * k);
                        }
                        Trail(P, ref GhostA, dt, 0.03f, 0.14f, 0.7f);
                        Trail(Q, ref GhostB, dt, 0.03f, 0.14f, 0.7f);
                        if (k >= 1f)
                        {
                            Next(Fall);
                        }
                        break;
                    }
                case Fall:
                    {
                        if (Enter())
                        {
                            _fa = P.Pose.Ground;
                            _fb = Q.Pose.Ground;
                            _hfa = P.Pose.Height;
                            _hfb = Q.Pose.Height;
                            _dur = Big(0.6f, 0.5f);
                            _tailA = P.BodyPos();
                            _tailB = Q.BodyPos();
                            D.Sound("event:/SFX/DESTRUCTION/FallMeteorite");
                        }
                        Meteor(P, _fa, _hfa, _la, Mathf.Clamp01(Pt / _dur), ref _tailA, ref _downA, ref _atA, ref GhostA, dt);
                        Meteor(Q, _fb, _hfb, _lb, Mathf.Clamp01((Pt - 0.07f) / _dur), ref _tailB, ref _downB, ref _atB, ref GhostB, dt);
                        if (_downA && _downB && Pt >= _dur + 0.07f + Rest(0.2f))
                        {
                            Next(Back);
                        }
                        break;
                    }
                case Back:
                    {
                        if (Enter())
                        {
                            if (!Combat.SafeSpot(_meet) && Combat.FindLanding(_meet, 2f, out Vector2 m2))
                            {
                                _meet = m2;
                            }
                            Vector2 ax = (_lb - _la).sqrMagnitude > 1e-4f ? (_lb - _la).normalized : _axis;
                            _ma = Combat.Clamp(_meet - ax * 0.31f * K);
                            _mb = Combat.Clamp(_meet + ax * 0.31f * K);
                            if (!Combat.SafeSpot(_ma))
                            {
                                _ma = _meet;
                            }
                            if (!Combat.SafeSpot(_mb))
                            {
                                _mb = _meet;
                            }
                            _fa = P.Pose.Ground;
                            _fb = Q.Pose.Ground;
                            DuelFx.Puff(_fa, P.Px, Combat.TileAt(_fa), 3, 14f);
                            DuelFx.Puff(_fb, Q.Px, Combat.TileAt(_fb), 3, 14f);
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                        }
                        float k = Mathf.Clamp01(Pt / 0.3f);
                        float e = Anim.OutCubic(k);
                        P.Pose.Ground = Vector2.Lerp(_fa, _ma, e);
                        Q.Pose.Ground = Vector2.Lerp(_fb, _mb, e);
                        float hop = 0.6f * K * Mathf.Sin(Mathf.PI * k);
                        FaceEach();
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = hop;
                            m.Pose.Rot = 20f;
                            m.Pose.SX = 1.35f;
                            m.Pose.SY = 0.8f;
                        }
                        Trail(P, ref GhostA, dt, 0.02f, 0.16f);
                        Trail(Q, ref GhostB, dt, 0.02f, 0.16f);
                        if (Vis && ((int)(Pt / 0.05f)) > ((int)((Pt - dt) / 0.05f)))
                        {
                            Kit.SpeedLines(P.BodyPos(), _ma - _fa, px, 2, 5f, 0.14f, Fx.WithA(P.Pal.Edge, 0.7f));
                            Kit.SpeedLines(Q.BodyPos(), _mb - _fb, px, 2, 5f, 0.14f, Fx.WithA(Q.Pal.Edge, 0.7f));
                        }
                        if (k >= 1f)
                        {
                            Ground();
                            Next(Clashing);
                        }
                        break;
                    }
                case Clashing:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.2f);
                    P.Glow = Q.Glow = DuelFx.Clear;
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void Leg(Vector2 fa, Vector2 fb, float hfa, float hfb, Vector2 ta, Vector2 tb, float hta, float htb, float dur)
        {
            _fa = fa;
            _fb = fb;
            _ta = Combat.Clamp(ta);
            _tb = Combat.Clamp(tb);
            _hfa = hfa;
            _hfb = hfb;
            _hta = hta;
            _htb = htb;
            _dur = dur;
        }

        private void Fly(float e)
        {
            P.Pose.Ground = Vector2.Lerp(_fa, _ta, e);
            Q.Pose.Ground = Vector2.Lerp(_fb, _tb, e);
            P.Pose.Height = Mathf.Lerp(_hfa, _hta, e);
            Q.Pose.Height = Mathf.Lerp(_hfb, _htb, e);
            FaceEach();
        }

        /// <summary>A meeting in the sky, bigger every time; the last one shakes the ground far below.</summary>
        private void Hit()
        {
            float px = Px;
            Vector2 at = Contact();
            bool last = _i == _n - 1;
            _c = D.Resolve(false);
            _lose = _c.Even ? null : Loser(_c);
            float tiles = (Mega ? 1.2f + 0.45f * _i : 1f + 0.35f * _i) * K;
            Color ea = P.Pal.Edge, eb = Q.Pal.Edge;
            if (!_c.Even)
            {
                ea = eb = Winner(_c).Pal.Edge;
            }
            Color mix = Color.Lerp(ea, eb, 0.5f);
            Slash(P, _i % 2, 0.14f);
            Slash(Q, (_i + 1) % 2, 0.14f);
            D.Sound("event:/SFX/HIT/HitSwordSword");
            if (last)
            {
                D.Sound("event:/SFX/EXPLOSIONS/ExplosionBig");
            }
            Vector2 below = (P.Pose.Ground + Q.Pose.Ground) * 0.5f;
            if (Vis)
            {
                DuelFx.Starburst(at, px, tiles, Kit.Core, ea, eb, 0.1f + 0.02f * _i);
                DuelFx.AirRing(at, px, 4f, Mathf.Min(140f, (16f + 9f * _i) * K), 0.32f, 2, Color.white, Fx.WithA(mix, 0.7f), DuelFx.Clear);
                DuelFx.GroundRing(below, px, 4f, Mathf.Min(140f, (14f + 10f * _i) * K), 0.4f, 2, 1, new Color(1f, 1f, 1f, 0.8f), Fx.WithA(mix, 0.5f), DuelFx.Clear);
                Kit.DustRing(below, px, Combat.TileAt(below), 6 + 3 * _i, 50f + 20f * _i, 0.6f, K);
                DuelFx.Spray(at, px, 8 + 3 * _i, _axis, true, mix);
                DuelFx.Fountain(at, px, 4 + 2 * _i, 45f, mix);
                Fx.FocusLines(at, px, Fx.Count(10 + 3 * _i), 14f * K, (26f + 5f * _i) * K, 0.14f);
            }
            Kit.Shake(at, 0.16f + 0.05f * _i, 0.005f + 0.003f * _i, K);
            if (last)
            {
                Kit.ImpactFrame(at, px, 1.4f, _c.Even ? P.Pal : Winner(_c).Pal, false);
                Kit.Stop(at, 0.12f);
                // the last blow's shockwave reaches the ground and scorches it wide
                Wreck(below, 2f, 1, _c.Even ? null : Winner(_c));
            }
            Hold = 0.05f;
            if (_lose != null)
            {
                D.Chip(Winner(_c), _lose, 0.12f, false);
            }
        }

        /// <summary>One coming down head first into the far ground; then crouched in its crater.</summary>
        private void Meteor(Duelist m, Vector2 from, float h0, Vector2 to, float k, ref Vector2 tail, ref bool down, ref float at, ref float ghost, float dt)
        {
            if (!down)
            {
                float e = Anim.InQuad(k);
                m.Pose.Ground = Vector2.Lerp(from, to, e);
                m.Pose.Height = h0 * (1f - e);
                if (Mathf.Abs(to.x - from.x) > 0.05f)
                {
                    m.Pose.FaceRight = to.x > from.x;
                }
                // head first along the dive (positive leans forward)
                float run = Mathf.Max(0.1f, (to - from).magnitude);
                m.Pose.Rot = Mathf.Clamp(Mathf.Atan2(h0, run) * Mathf.Rad2Deg, 30f, 75f);
                m.Pose.SX = 0.86f;
                m.Pose.SY = 1.18f;
                Tail(m, ref tail);
                Trail(m, ref ghost, dt, 0.025f, 0.16f);
                if (k >= 1f)
                {
                    down = true;
                    at = Pt;
                    m.Pose.Height = 0f;
                    Slam(to, m, 0.9f, 1.3f);
                    Quake(to, Big(2f, 1.5f), m, Big(1f, 0.7f));
                }
                return;
            }
            // a superhero landing in its own crater, springing up
            float amp = Anim.Spring(Pt - at, 6f, 13f);
            m.Pose.Ground = to;
            m.Pose.Height = 0f;
            m.Pose.Rot = 14f * amp;
            m.Pose.SX = 1f + 0.18f * amp;
            m.Pose.SY = 1f - 0.22f * amp;
        }

        /// <summary>They meet on the ground again: the clash rule decides it.</summary>
        private void Ground()
        {
            Vector2 at = Contact();
            _c = D.Resolve(true);
            P.Pose.Height = Q.Pose.Height = 0f;
            Slash(P, 2, 0.16f);
            Slash(Q, 2, 0.16f);
            Meet(at, (_c.Even ? 0.9f : 0.7f) * K, _c, 12);
            Shock(_c);
            Kit.Stop(at, 0.1f);
            Aftermath(_c, at, 1f, 1.3f, Random.Range(0.4f, 0.55f), 2.4f);
            Carry(_mid, _meet);
        }
    }

    /// <summary>
    /// MOUNTAIN BREAKER (catastrophe). One drives the other through whatever stands in the way:
    /// a forest, a village, a mountain.
    ///
    ///   CLOSE  ~0.15   into reach (the driver comes in behind, on the side away from the obstacles)
    ///   SEIZE   0.16   the driver winds back; then the blow lands: an impact frame, a world
    ///                  hit-stop, a starburst; the ground under it blasted
    ///   DRIVE  ~0.4-1  both travel together at 26 tiles a second (Grandmasters 20), the other bent
    ///                  over the driver's fist, skimming the ground: everything on the way is smashed
    ///                  (splinters and leaves, rock), blasted for real (force 3, a God's 4), mountain
    ///                  tiles carved down to hills (a God's pass through the range), the ground between
    ///                  torn; every obstacle shakes the camera and costs the other a little; dust wakes
    ///                  and speed lines
    ///   SLAM           at the far end, the last blow drives it into the ground: a crater blasted,
    ///                  the impact frame, cracks; anyone near hurt and thrown; the driver skids back
    ///   RISE   ~0.35   the other gets up out of the crater (hopping out if it burns). The driver
    ///                  may chain onto it (Down)
    /// Only where there is something to drive it through (3 or more trees, rocks, mountain tiles or
    /// buildings on an 8-22 tile line, 5-12 for Grandmasters) to open ground beyond, no lava and no
    /// long water on the way.
    /// </summary>
    internal sealed class MountainBreaker : Catastrophe
    {
        private const int Close = 0, Seize = 1, Drive = 2, Rise = 3;

        private Clash _c;
        private bool _dIsA, _scanned;
        private Duelist _d, _v;
        private Vector2 _d0, _v0, _sd, _sv, _dir, _end, _rise, _stop, _routeEnd;
        private float _tClose, _tDrive, _len, _cursor, _chipped, _dust, _lines;
        private int _routeObs, _carved, _smashes;

        public override string Id => "mountain_breaker";

        public override Form Forms => Form.Throw | Form.Grab | Form.Ground | Form.Heavy | Form.Ultimate;

        protected override float Fit()
        {
            Route((A0 + B0) * 0.5f);
            return _routeObs >= 3 ? 1.8f + 0.15f * Mathf.Min(_routeObs, 10) : 0f;
        }

        /// <summary>The line through the most that stands in the way, to open ground beyond it (preferring the duel's heading).</summary>
        private void Route(Vector2 from)
        {
            _scanned = true;
            _routeObs = 0;
            Vector2 basis = D.Heading.sqrMagnitude > 0.01f ? D.Heading : B0 - A0;
            if (basis.sqrMagnitude < 1e-4f)
            {
                basis = Vector2.right;
            }
            float a0 = Mathf.Atan2(basis.y, basis.x);
            float lmax = Big(22f, 12f), lmin = Big(8f, 5f);
            float best = 0f;
            for (int i = 0; i < 10; i++)
            {
                float a = a0 + i * 0.63f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int s = 0; s < 3; s++)
                {
                    Vector2 end = from + dir * Mathf.Lerp(lmax, lmin, s / 2f);
                    if (!DuelGeo.Inside(end, 3f))
                    {
                        continue;
                    }
                    if (!DuelGeo.Roomy(end))
                    {
                        if (!Combat.FindLanding(end, 1.5f, out Vector2 e2) || !DuelGeo.Inside(e2, 3f) || !DuelGeo.Roomy(e2))
                        {
                            continue;
                        }
                        end = e2;
                    }
                    DuelGeo.Scan(from, end, out int rock, out int wood, out int wet, out bool lava);
                    if (!lava && wet <= 2)
                    {
                        int obs = rock + wood;
                        float score = obs + (i == 0 ? 1.5f : 0f);   // on its way: better
                        if (obs >= 3 && score > best)
                        {
                            best = score;
                            _routeEnd = end;
                            _routeObs = obs;
                        }
                    }
                    break;   // the farthest open ground this way is the one
                }
            }
        }

        public override bool Plan()
        {
            if (!_scanned)
            {
                Route((A0 + B0) * 0.5f);
            }
            if (_routeObs < 3)
            {
                return false;
            }
            _c = D.Roll();
            _dIsA = _c.Even ? Random.value < 0.5f : _c.AWins;
            _d0 = _dIsA ? A0 : B0;
            _v0 = _dIsA ? B0 : A0;
            if (Combat.SafeSpot(_v0))
            {
                _sv = _v0;
            }
            else if (!Combat.FindLanding(_v0, 1.5f, out _sv))
            {
                return false;
            }
            _end = _routeEnd;
            Vector2 d = _end - _sv;
            if (d.sqrMagnitude < 16f)
            {
                return false;
            }
            _len = d.magnitude;
            _dir = d / _len;
            // what it will really be driven through, from where it stands
            DuelGeo.Scan(_sv, _end, out int rock, out int wood, out int wet, out bool lava);
            if (lava || wet > 2 || rock + wood < 2)
            {
                return false;
            }
            bool ok = false;
            foreach (float g in new[] { 0.55f, 0.42f })
            {
                Vector2 p = Combat.Clamp(_sv - _dir * g * K);
                if (Combat.SafeSpot(p) && Combat.ClearPath(_d0, p))
                {
                    _sd = p;
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                return false;
            }
            _tDrive = Mathf.Clamp(_len / Big(26f, 20f), 0.35f, 1f);
            float dd = Mathf.Max((_sd - _d0).magnitude, (_sv - _v0).magnitude);
            _tClose = dd > 0.25f ? Mathf.Clamp(0.08f + 0.035f * dd, 0.1f, 0.22f) : 0.05f;
            return true;
        }

        protected override void Begin()
        {
            _d = _dIsA ? P : Q;
            _v = _dIsA ? Q : P;
            D.Count(_c, true);
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Close:
                    {
                        float k = Pt / _tClose;
                        Reposition(_d, _d0, _sd, k, _sv);
                        Reposition(_v, _v0, _sv, k, _sd);
                        FaceEach();
                        Trail(_d, ref GhostA, dt, 0.025f, 0.13f);
                        if (k >= 1f)
                        {
                            Meet(Contact(), 0.35f * K, new Clash { Kind = ClashKind.Even }, 5);
                            Stop(0.04f);
                            Next(Seize);
                        }
                        break;
                    }
                case Seize:
                    {
                        float k = Mathf.Clamp01(Pt / 0.16f);
                        float e = Anim.OutQuad(k);
                        _d.Pose.Ground = _sd;
                        _v.Pose.Ground = _sv;
                        FaceEach();
                        _d.Pose.Rot = -18f * e;
                        _d.Pose.SX = 1f - 0.1f * e;
                        _d.Pose.SY = 1f + 0.1f * e;
                        _d.Glow = Fx.WithA(Color.Lerp(_d.Pal.Edge, Color.white, k), 0.4f * k);
                        Guard(_v, T, 0f, 1.2f);
                        if (k >= 1f)
                        {
                            Ram();
                            Next(Drive);
                        }
                        break;
                    }
                case Drive:
                    DriveTick(dt);
                    break;
                default:
                    RiseTick(dt);
                    break;
            }
        }

        private void Ram()
        {
            float px = Px;
            Vector2 at = _v.BodyPos();
            Color edge = _d.Pal.Edge;
            Slash(_d, 2, 0.16f);
            D.Sound("event:/SFX/HIT/HitGeneric");
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionForce");
            if (Vis)
            {
                Kit.ImpactFrame(at, px, 1f, _d.Pal, false);
                DuelFx.Starburst(at, px, 1.4f * K, Kit.Core, edge, edge, 0.13f);
                Fx.FocusLines(at, px, Fx.Count(14), 14f * K, 28f * K, 0.14f);
            }
            Kit.Shake(at, 0.3f, 0.01f, K);
            Kit.Stop(at, 0.08f);
            Wreck(_sv, 0.8f, 2, _d);
            D.Chip(_d, _v, _c.Kind == ClashKind.Overwhelm ? 0.5f : _c.Even ? 0.25f : 0.4f, true);
            Opens(_d, Link.Down);
            if (Mathf.Abs(_dir.x) > 0.05f)
            {
                _v.Pose.FaceRight = _dir.x < 0f;   // bent backward over the fist, facing the driver
                _d.Pose.FaceRight = _dir.x > 0f;
            }
            _cursor = 0f;
        }

        private void DriveTick(float dt)
        {
            float px = Px;
            float k = Mathf.Clamp01(Pt / _tDrive);
            // a hard start, then flat out to the end
            float s = (k < 0.1f ? 5f * k * k : k - 0.05f) / 0.95f;
            Vector2 g = Vector2.Lerp(_sv, _end, s);
            _v.Pose.Ground = g;
            _v.Pose.Height = (0.12f + 0.04f * Mathf.Sin(T * 45f)) * K;
            _v.Pose.Rot = -50f;
            _v.Pose.SX = 1.1f;
            _v.Pose.SY = 0.9f;
            _d.Pose.Ground = Combat.Clamp(g - _dir * 0.42f * K);
            _d.Pose.Height = 0.1f * K;
            _d.Pose.Rot = 32f;
            _d.Pose.SX = 1.3f;
            _d.Pose.SY = 0.82f;
            _d.Glow = Fx.WithA(_d.Pal.Edge, 0.3f);
            // everything on the way, smashed in turn
            float reach = s * _len;
            while (_cursor + 0.8f <= reach)
            {
                _cursor += 0.8f;
                Through(Vector2.Lerp(_sv, _end, _cursor / _len));
            }
            Trail(_d, ref GhostA, dt, 0.02f, 0.16f);
            Trail(_v, ref GhostB, dt, 0.02f, 0.16f);
            _dust -= dt;
            if (_dust <= 0f)
            {
                _dust = 0.03f;
                DuelFx.Plough(g, _dir, px, Combat.TileAt(g), K);
            }
            _lines -= dt;
            if (Vis && _lines <= 0f)
            {
                _lines = 0.04f;
                Kit.SpeedLines(_v.BodyPos(), _dir, px, 3, 6f, 0.16f, Fx.WithA(_d.Pal.Edge, 0.75f));
            }
            if (k >= 1f)
            {
                Slam_();
                Next(Rise);
            }
        }

        /// <summary>One step of the drive: whatever stands there is smashed through (and the ground between torn).</summary>
        private void Through(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            if (t == null || t.Type == null)
            {
                return;
            }
            bool rock = t.Type.mountains || t.Type.rocks;
            bool stand = DuelGeo.Standing(t);
            if (rock || stand)
            {
                if (_smashes < 8 || (_smashes & 1) == 0)
                {
                    DuelFx.Smash(p, Px, t, rock && !stand, K);   // (a long drive through a forest: every other one)
                }
                bool mountain = t.Type.mountains;
                Wreck(p, 1f, 3, _d);
                if (mountain && _carved < 40)
                {
                    _carved += DuelGeo.Carve(p, Mega ? 1.3f : 0.8f, 40 - _carved);
                }
                if ((_smashes++ & 1) == 0)
                {
                    Anim.Sound(rock ? "event:/SFX/HIT/HitStone" : "event:/SFX/DESTRUCTION/DropSimpleImpact", t);
                }
                Kit.Shake(p, 0.12f, 0.007f, K);
                if (_chipped < 0.4f)
                {
                    _chipped += 0.06f;
                    D.Chip(_d, _v, 0.06f, false);
                }
            }
            else if (((int)(_cursor / 0.8f) & 1) == 0)
            {
                Wreck(p, 0.7f, 2, _d);   // the ground torn by its passing
            }
        }

        private void Slam_()
        {
            _v.Pose.Ground = _end;
            _v.Pose.Height = 0f;
            _v.Pose.Rot = -80f;
            _d.Glow = DuelFx.Clear;
            Slash(_d, 0, 0.16f);
            Slam(_end, _d, 1.2f, 1.5f);
            Kit.Stop(_end, 0.12f);
            D.Chip(_d, _v, 0.3f, true);
            Quake(_end, Big(2.5f, 1.8f), _d, Big(1.2f, 0.8f));
            // after the world has given: where each will stand
            _rise = DuelGeo.Footing(_end);
            Vector2 back = Combat.Clamp(_end - _dir * 1.1f * K);
            _stop = Combat.SafeSpot(back) ? back : DuelGeo.Footing(back);
            Knock k = Knock.Skid(_d.Pose.Ground, _stop, 0.26f, 18f, Rest(0.15f));
            if (_d == P)
            {
                KA = k;
            }
            else
            {
                KB = k;
            }
        }

        private void RiseTick(float dt)
        {
            float dur = Mathf.Max(0.32f, 0.6f * Brisk + 0.12f);
            float k = Mathf.Clamp01(Pt / dur);
            bool skid = Recoil(dt);
            GetUp(_v, _end, _rise, true, k);
            if (k > 0.55f)
            {
                FaceEach();
            }
            if (k >= 1f && skid)
            {
                Carry(_sv, _end);
                Done = true;
            }
        }
    }

    /// <summary>
    /// RELENTLESS CHASE (catastrophe). They race across the land side by side as two streaks of
    /// light, weaving apart and crashing together again and again.
    ///
    ///   RUN  22-34 tiles (Grandmasters 12-18): legs of 3.5-5.5 tiles zig-zagging along the duel's
    ///        heading, each a 0.12 s blur (0.15), the two weaving apart on either side and meeting at
    ///        its end: a clash (a starburst growing with each, a ring, sparks, a hot crack decal; the
    ///        ground blasted for real at every meeting, every other for Grandmasters; the loser of
    ///        each flashes). Over water, a wall or a mountain a leg is one long leap. Afterimages,
    ///        speed lines and a streak of light behind each
    ///   CLASH    at the last meeting: the clash rule decides it, an impact frame, a world hit-stop,
    ///            a crater blasted, the aftermath throws them
    ///   SETTLE   brisk
    /// Every meeting place is roomy safe ground well inside the map.
    /// </summary>
    internal sealed class Relentless : Catastrophe
    {
        private const int Run = 0, Finish = 1, End = 2;
        private const float Gap = 0.3f, Weave = 0.55f;

        private Vector2[] _w;
        private bool[] _leap;
        private Vector2[] _side;
        private bool _leapA, _leapB;
        private int _legs, _met;
        private float _leg, _beat, _sgn, _dust;
        private Vector2 _lastA, _lastB;
        private Clash _c;

        public override string Id => "relentless";

        public override Form Forms => Form.Dash | Form.Flurry | Form.Fast | Form.Heavy;

        protected override float Fit()
        {
            float w = 1.2f;
            if (Mathf.Abs(D.HpA - D.HpB) > 0.2f)
            {
                w *= 1.4f;   // one presses, the other won't be caught
            }
            if (D.LastCat == "tectonic" || D.LastCat == "sky_battle")
            {
                w *= 1.3f;   // after one that stayed put, the fight moves on
            }
            return w;
        }

        public override bool Plan()
        {
            float total = Big(Random.Range(22f, 34f), Random.Range(12f, 18f));
            Vector2 start = (A0 + B0) * 0.5f;
            Vector2 axis = B0 - A0;
            Vector2 perp = axis.sqrMagnitude > 1e-4f ? new Vector2(-axis.y, axis.x).normalized : Vector2.up;
            Vector2 basis = Onward(Random.value < 0.5f ? perp : -perp);
            float a0 = Mathf.Atan2(basis.y, basis.x);
            List<Vector2> pts = new List<Vector2>();
            List<bool> leaps = new List<bool>();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float baseA = a0 + (attempt == 0 ? 0f : ((attempt & 1) == 1 ? 1f : -1f) * ((attempt + 1) / 2) * 0.9f);
                pts.Clear();
                leaps.Clear();
                pts.Add(start);
                float run = 0f, zig = Random.value < 0.5f ? 1f : -1f;
                bool ok = true;
                while (run < total && pts.Count < 12)
                {
                    Vector2 prev = pts[pts.Count - 1];
                    float len = Big(Random.Range(3.5f, 5.5f), Random.Range(2.8f, 4f)) * K;
                    float ang = baseA + zig * Random.Range(0.3f, 0.7f);
                    zig = -zig;
                    Vector2 step = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;
                    Vector2 p = prev + step;
                    if (!DuelGeo.Inside(p, 3f) || !DuelGeo.Roomy(p))
                    {
                        p = prev + step * 0.6f;
                        if (!DuelGeo.Inside(p, 3f) || !DuelGeo.Roomy(p))
                        {
                            ok = false;
                            break;
                        }
                    }
                    bool clear = Combat.ClearPath(prev, p);
                    pts.Add(p);
                    leaps.Add(!clear);
                    run += (p - prev).magnitude;
                }
                if (!ok || run < total * 0.7f || pts.Count < 4)
                {
                    continue;
                }
                _w = pts.ToArray();
                _leap = leaps.ToArray();
                _legs = _w.Length - 1;
                // the lanes: each leg's side, all on the same hand so the weave reads
                _side = new Vector2[_legs];
                Vector2 refSide = Vector2.zero;
                for (int i = 0; i < _legs; i++)
                {
                    Vector2 d = (_w[i + 1] - _w[i]).normalized;
                    Vector2 s = new Vector2(-d.y, d.x);
                    if (i == 0)
                    {
                        refSide = s;
                    }
                    else if (Vector2.Dot(s, refSide) < 0f)
                    {
                        s = -s;
                    }
                    _side[i] = s;
                }
                // A runs on the side it already stands on
                _sgn = Vector2.Dot(A0 - B0, _side[0]) >= 0f ? 1f : -1f;
                _leapA = !Combat.ClearPath(A0, _w[1]);
                _leapB = !Combat.ClearPath(B0, _w[1]);
                _leg = Big(0.12f, 0.15f);
                _beat = 0.05f;
                return true;
            }
            return false;
        }

        protected override void Begin()
        {
            _met = 0;
            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
            Kit.DustRing(A0, P.Px, Combat.TileAt(A0), 6, 40f, 0.4f, K);
            Kit.DustRing(B0, Q.Px, Combat.TileAt(B0), 6, 40f, 0.4f, K);
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Run:
                    RunTick(dt);
                    break;
                case Finish:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.1f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        /// <summary>Which side of meeting place j each stands on (the halfway between the two legs that meet there).</summary>
        private Vector2 MeetSide(int j)
        {
            if (j >= _legs)
            {
                return _side[_legs - 1];
            }
            Vector2 s = _side[j - 1] + _side[j];
            return s.sqrMagnitude > 1e-4f ? s.normalized : _side[j];
        }

        /// <summary>
        /// Where one runs on leg i at progress u: from one meeting place to the next (the first leg
        /// from where it stands), weaving out to its own side and back in.
        /// </summary>
        private Vector2 Lane(int i, float u, bool a)
        {
            float s = (a ? _sgn : -_sgn) * K;
            Vector2 from = i == 0 ? (a ? A0 : B0) : _w[i] + MeetSide(i) * Gap * s;
            Vector2 to = _w[i + 1] + MeetSide(i + 1) * Gap * s;
            return Vector2.Lerp(from, to, Anim.OutQuad(u)) + _side[i] * (Weave - Gap) * s * Mathf.Sin(Mathf.PI * u);
        }

        private void RunTick(float dt)
        {
            float px = Px;
            float slot = _leg + _beat;
            int i = Mathf.Min(_legs - 1, (int)(Pt / slot));
            float local = Pt - i * slot;
            // a meeting at the end of every leg as they arrive (the last one decides it)
            int reached = local >= _leg ? i + 1 : i;
            while (_met < reached && _met < _legs - 1)
            {
                _met++;
                Pass(_met);
            }
            if (local < _leg)
            {
                float u = local / _leg;
                Vector2 pa = Lane(i, u, true), pb = Lane(i, u, false);
                P.Pose.Ground = pa;
                Q.Pose.Ground = pb;
                bool la = i == 0 ? _leapA : _leap[i], lb = i == 0 ? _leapB : _leap[i];
                float arc = Big(1.1f, 0.8f) * K * Mathf.Sin(Mathf.PI * u);
                P.Pose.Height = la ? arc : 0f;
                Q.Pose.Height = lb ? arc : 0f;
                Vector2 dir = _w[i + 1] - _w[i];
                if (Mathf.Abs(dir.x) > 0.05f)
                {
                    P.Pose.FaceRight = Q.Pose.FaceRight = dir.x > 0f;
                }
                foreach (Duelist m in new[] { P, Q })
                {
                    m.Pose.Rot = 18f;
                    m.Pose.SX = 1.45f;
                    m.Pose.SY = 0.78f;
                }
                Trail(P, ref GhostA, dt, 0.018f, 0.16f);
                Trail(Q, ref GhostB, dt, 0.018f, 0.16f);
                Streak(P, ref _lastA);
                Streak(Q, ref _lastB);
                _dust -= dt;
                if (_dust <= 0f)
                {
                    _dust = 0.05f;
                    if (!la)
                    {
                        DuelFx.SkidDust(pa, dir, P.Px, Combat.TileAt(pa));
                    }
                    if (!lb)
                    {
                        DuelFx.SkidDust(pb, dir, Q.Px, Combat.TileAt(pb));
                    }
                    if (Vis)
                    {
                        Kit.SpeedLines((P.BodyPos() + Q.BodyPos()) * 0.5f, dir, px, 2, 7f, 0.14f, Fx.WithA(Color.Lerp(P.Pal.Edge, Q.Pal.Edge, Random.value), 0.75f));
                    }
                }
                return;
            }
            // at the meeting: pressed blade to blade for a beat
            P.Pose.Ground = Lane(i, 1f, true);
            Q.Pose.Ground = Lane(i, 1f, false);
            P.Pose.Height = Q.Pose.Height = 0f;
            FaceEach();
            foreach (Duelist m in new[] { P, Q })
            {
                m.Pose.Rot = 20f;
                m.Pose.SX = 1.1f;
                m.Pose.SY = 0.92f;
            }
            _lastA = P.BodyPos();
            _lastB = Q.BodyPos();
            if (i == _legs - 1)
            {
                Last();
                Next(Finish);
            }
        }

        /// <summary>A streak of light behind a runner (one segment a tick).</summary>
        private void Streak(Duelist m, ref Vector2 last)
        {
            Vector2 now = m.BodyPos();
            if (last.sqrMagnitude > 0f && Vis)
            {
                Kit.Line(last, now, m.Px, 0.22f, 1, FxLayer.Behind, 2, Color.white, Fx.WithA(m.Pal.Edge, 0.85f), Fx.WithA(m.Pal.Edge, 0f));
            }
            last = now;
        }

        /// <summary>A meeting on the way: they crash together and race on.</summary>
        private void Pass(int j)
        {
            float px = Px;
            Vector2 w = _w[j];
            Vector2 at = (Lane(j - 1, 1f, true) + Lane(j - 1, 1f, false)) * 0.5f + new Vector2(0f, 4f * px * K);
            Clash c = D.Resolve(false);
            Color ea = c.Even ? P.Pal.Edge : Winner(c).Pal.Edge, eb = c.Even ? Q.Pal.Edge : ea;
            Color mix = Color.Lerp(ea, eb, 0.5f);
            Slash(P, j % 2, 0.12f);
            Slash(Q, (j + 1) % 2, 0.12f);
            D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            if (Kit.Visible(w))
            {
                DuelFx.Starburst(at, px, (0.5f + 0.07f * j) * K, Kit.Core, ea, eb, 0.1f);
                DuelFx.Spray(at, px, 6, _w[j] - _w[j - 1], Metal, mix);
                DuelFx.GroundRing(w, px, 3f, 18f * K, 0.3f, 2, 1, Fx.WithA(Kit.Core, 0.95f), Fx.WithA(mix, 0.6f), Fx.WithA(mix, 0f));
                Kit.DustRing(w, px, Combat.TileAt(w), 8, 50f, 0.5f, K);
            }
            if (Mega || (j & 1) == 0)
            {
                Kit.Crater(w, px, Combat.TileAt(w), true, 4f, true, mix);
                Wreck(w, 0.8f, 2, c.Even ? null : Winner(c));
            }
            Kit.Shake(w, 0.1f, 0.005f, K);
            Hold = 0.03f;
            if (!c.Even)
            {
                D.Chip(Winner(c), Loser(c), 0.08f, false);
            }
        }

        /// <summary>The last meeting: the clash rule decides it.</summary>
        private void Last()
        {
            float px = Px;
            Vector2 w = _w[_legs];
            Vector2 at = Contact();
            _c = D.Resolve(true);
            Duelist win = _c.Even ? null : Winner(_c);
            Color edge = win != null ? win.Pal.Edge : Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f);
            Slash(P, 2, 0.16f);
            Slash(Q, 2, 0.16f);
            Meet(at, (_c.Even ? 0.95f : 0.75f) * K, _c, 12);
            if (Kit.Visible(w))
            {
                Kit.ImpactFrame(at, px, 1f, win != null ? win.Pal : P.Pal, false);
                DuelFx.GroundRing(w, px, 4f, 30f * K, 0.45f, 3, 1, Fx.WithA(Kit.Core, 1f), Fx.WithA(edge, 0.7f), Fx.WithA(edge, 0f));
                Kit.DustRing(w, px, Combat.TileAt(w), 16, 80f, 0.7f, K);
                Kit.Debris(w, px, Combat.TileAt(w), 10, K);
            }
            Kit.Crater(w, px * K, Combat.TileAt(w), true, 6f, false, edge);
            Wreck(w, 1.2f, 3, win);
            Kit.Shake(w, 0.3f, 0.012f, K);
            Kit.Stop(at, 0.1f);
            D.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle");
            Aftermath(_c, at, 0.9f, 1.3f, Random.Range(0.45f, 0.6f), 2.4f);
            Carry(_w[0], w);
        }
    }

    /// <summary>
    /// A duelist left alone mid-exchange (the other died, was picked up, or broke away): it comes
    /// down from the air if it was up, lands on safe ground, finds its feet, and glints. Safe ground
    /// far off (out over the sea, deep in a range it was driving through), or vanished mid-move: it
    /// blinks there instead, back where the exchange began if nothing nearer holds.
    /// </summary>
    internal sealed class Recover : Exchange
    {
        private Vector2 _from, _to;
        private readonly Vector2 _home;
        private readonly bool _vanished;
        private float _h0, _rot0, _down, _hop;
        private bool _landed, _blink;

        /// <summary>'home': where it stood when the broken exchange began; 'vanished': it was hidden mid-move.</summary>
        public Recover(Duelist survivor, Vector2 home, bool vanished = false)
        {
            P = survivor;
            Q = null;
            _home = home;
            _vanished = vanished;
        }

        public override string Id => "recover";

        public override bool Intense => false;

        public override bool Plan()
        {
            return true;
        }

        public override void Start()
        {
            _from = P.Pose.Ground;
            Vector2 s;
            _to = Combat.SafeSpot(_from) ? _from
                : Combat.FindLanding(_from, 1.5f, out s) || Combat.FindLanding(_from, 6f, out s) ? s
                : Combat.SafeSpot(_home) ? _home
                : DuelGeo.Far(_from, _home - _from, 6f, 14f, Mathf.PI, out s) ? s
                : Combat.Clamp(_from);
            float far = (_to - _from).magnitude;
            _h0 = P.Pose.Height;
            _rot0 = Mathf.DeltaAngle(0f, P.Pose.Rot);
            // down from the air; on the ground a hop when it has to move; a blink when it is far
            _down = _h0 > 0.03f ? Mathf.Clamp(0.15f + 0.08f * _h0, 0.18f, 0.45f) : far > 0.2f ? 0.22f : 0f;
            _hop = _h0 > 0.03f ? 0f : 0.22f * P.K * Mathf.Min(1f, far);
            bool hidden = _vanished || P.Pose.Hidden;
            _blink = far > 2.5f * P.K || hidden && far > 0.2f;
            if (_blink && !hidden && Kit.Visible(_from))
            {
                Fx.Afterimage(P, 0.22f, P.Pal.Ghost, P.Pal.GhostEnd);
            }
            P.Pose.Hidden = _blink;
            P.Blur = false;
            P.Glow = DuelFx.Clear;
        }

        protected override void Tick(float dt)
        {
            if (T < _down)
            {
                float k = T / _down;
                P.Pose.Ground = Vector2.Lerp(_from, _to, k);
                P.Pose.Height = _h0 > 0.03f ? _h0 * (1f - k * k) : _hop * Mathf.Sin(Mathf.PI * k);
                P.Pose.Rot = Mathf.Lerp(_rot0, 0f, Anim.OutQuad(k));
                P.Pose.SX = 1f;
                P.Pose.SY = 1f;
                P.Pose.Hidden = _blink;
                return;
            }
            if (!_landed)
            {
                _landed = true;
                P.Pose.Hidden = false;
                P.Pose.Height = 0f;
                P.Pose.Ground = _to;
                if (_down > 0f)
                {
                    DuelFx.Puff(_to, P.Px, Combat.TileAt(_to), 3, 12f);
                }
                if (Kit.Visible(_to))
                {
                    Vector2 off = new Vector2(P.Fwd * 3f * P.Px, P.Pose.PivotUp * 1.4f * P.Px);
                    Fx.Glint(P.BodyPos() + off, P.Px, 0.22f, P.Pal.Core, 0.9f);
                }
            }
            float t = T - _down;
            float amp = Anim.Spring(t, 8f, 16f);
            P.Pose.Rot = _down > 0f ? 0f : _rot0 * Mathf.Max(0f, 1f - t / 0.2f);
            P.Pose.SX = 1f + 0.12f * amp;
            P.Pose.SY = 1f - 0.14f * amp;
            if (t >= 0.28f)
            {
                P.Pose.Rot = 0f;
                P.Pose.SX = P.Pose.SY = 1f;
                Done = true;
            }
        }
    }
}
