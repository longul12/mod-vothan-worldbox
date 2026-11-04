using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>How a performer gathers power (where the orb of light grows, the pose it holds).</summary>
    internal enum ChargeStyle
    {
        /// <summary>Cupped hands drawn back to the hip (a beam): the orb grows behind it, it leans back.</summary>
        Hip,
        /// <summary>One hand raised over its head (a planet orb): the orb grows above it, it stretches tall.</summary>
        Raised,
        /// <summary>A fist cocked back, trembling (a haymaker): a small glow at the fist.</summary>
        Fist,
        /// <summary>The whole body (a stance): crouched, its aura flaring, the glow at its middle.</summary>
        Stance,
        /// <summary>Both palms forward (a barrier, a push): the glow in front of it.</summary>
        Palms
    }

    internal partial class ChoreoMove
    {
        private Particle _orb, _orbCore;
        private ChargeStyle _chargeStyle = ChargeStyle.Hip;
        private float _orbR, _moteT, _ringT, _auraT, _rumbleT;
        private bool _chargeGlint;

        /// <summary>Where the gathered power sits right now (the orb's centre; its hands when there is none).</summary>
        public Vector2 OrbAt()
        {
            float px = Px, h = Hpx;
            switch (_chargeStyle)
            {
                case ChargeStyle.Raised:
                    return BodyPos() + new Vector2(Fwd * 0.5f, h * 0.78f) * px;
                case ChargeStyle.Fist:
                    return BodyPos() + new Vector2(-Fwd * h * 0.22f, h * 0.06f) * px;
                case ChargeStyle.Stance:
                    return BodyPos();
                case ChargeStyle.Palms:
                    return BodyPos() + new Vector2(Fwd * h * 0.36f, h * 0.05f) * px;
                default:
                    return BodyPos() + new Vector2(-Fwd * h * 0.26f, -h * 0.12f) * px;
            }
        }

        /// <summary>Where a beam or a shot leaves from once the power is thrown forward (its hands, thrust out).</summary>
        public Vector2 Muzzle()
        {
            if (_chargeStyle == ChargeStyle.Raised)
            {
                return OrbAt();
            }
            return BodyPos() + new Vector2(Fwd * Hpx * 0.42f, Hpx * 0.02f) * Px;
        }

        /// <summary>The orb's radius now (sprite pixels).</summary>
        public float OrbRadius => _orbR;

        /// <summary>
        /// Gathering power for 'dur' seconds (at Novice pace; the tier tightens it): the performer holds
        /// the 'style' pose, an orb of its colours grows to 'orb' pixels (x size) while motes stream in,
        /// rings close on it and dust swirls at its feet, its aura pulsing; trembling at the end, a
        /// glint just before the release. Charging is true all along: a fast blow now breaks it
        /// (Choreo.Break: the power bursts and the performer is blasted away).
        /// </summary>
        public ChoreoMove Charge(float dur, ChargeStyle style = ChargeStyle.Hip, float orb = 3f, Element e = Element.School)
        {
            Shape pose;
            switch (style)
            {
                case ChargeStyle.Raised:
                    pose = new Shape(-6f, 0.94f, 1.08f, 0f);
                    break;
                case ChargeStyle.Fist:
                    pose = new Shape(-14f, 0.92f, 1.06f, 0f, -0.05f);
                    break;
                case ChargeStyle.Stance:
                    pose = new Shape(6f, 1.12f, 0.88f, 0f);
                    break;
                case ChargeStyle.Palms:
                    pose = new Shape(4f, 1.06f, 0.94f, 0f);
                    break;
                default:
                    pose = new Shape(-12f, 1.06f, 0.92f, 0f, -0.04f);
                    break;
            }
            Beat b = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = dur,
                To = pose,
                Ease = Ease.OutCubic,
                Charging = true,
                Track = true
            };
            b.Enter = m => m.EnterCharge(style, orb, e);
            b.Every = (m, k) => m.TickCharge(k);
            return Add(b);
        }

        private Hue _chargeHue;
        private School _chargeSchool;

        private void EnterCharge(ChargeStyle style, float orb, Element e)
        {
            _chargeStyle = style;
            _chargeHue = e == Element.School ? Hue : Hue.As(e);
            _orbR = orb * Mathf.Lerp(1f, Kk, 0.7f) * (0.85f + 0.15f * Level);
            _moteT = _ringT = _auraT = _rumbleT = 0f;
            _chargeGlint = false;
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 18f, 0.5f, Kk);
            }
            // only ki gathers into an orb; every other school draws its power in its own way (SchoolStyle.Gather)
            _chargeSchool = SchoolStyle.Of(this);
            if (SchoolStyle.Orb(_chargeSchool))
            {
                MakeOrb();
            }
        }

        private void MakeOrb()
        {
            if (_orb != null && _orb.Alive)
            {
                return;
            }
            Vector2 at = OrbAt();
            _orb = Glue(Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(_orbR * 1.6f)), 1f, 0.45f), at, Px, 1f, FxLayer.Over, 8));
            _orbCore = Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(_orbR * 0.7f)), 1f, 0f), at, Px, 1f, FxLayer.Over, 9));
        }

        private void TickCharge(float k)
        {
            float px = Px;
            Vector2 at = OrbAt();
            float dt = Clock.Dt;
            float grow = Mathf.Lerp(0.25f, 1f, Beat.Apply(Ease.OutQuad, k));
            float pulse = 1f + 0.12f * Mathf.Sin(BeatT * 38f);
            if (_orb != null && _orb.Alive)
            {
                _orb.Pos = at;
                _orb.Size0 = _orb.Size1 = grow * pulse;
                _orb.Grad = null;
                _orb.R.color = Fx.WithA(_chargeHue.Edge, 0.55f + 0.25f * k);
                _orb.Cine = Cinematic;
            }
            if (_orbCore != null && _orbCore.Alive)
            {
                _orbCore.Pos = at;
                _orbCore.Size0 = _orbCore.Size1 = grow * (1f + 0.08f * Mathf.Sin(BeatT * 51f));
                _orbCore.Grad = null;
                _orbCore.R.color = Color.Lerp(_chargeHue.Core, Color.white, k);
                _orbCore.Cine = Cinematic;
            }
            // the last fifth trembles with the power held back
            if (k > 0.75f)
            {
                Pose.Nudge = Choreo.Tremble(BeatT, k > 0.9f ? 0.8f : 0.5f);
            }
            if (!Vis)
            {
                return;
            }
            _moteT -= dt;
            if (_moteT <= 0f)
            {
                _moteT = Mathf.Lerp(0.05f, 0.025f, k);
                if (SchoolStyle.Orb(_chargeSchool))
                {
                    ChoreoFx.Motes(at, px, Level >= 3 ? 3 : 2, 9f + _orbR * 2f, 0.12f, _chargeHue.Core, _chargeHue.Edge, TopLayer);
                }
                else
                {
                    SchoolStyle.Gather(_chargeSchool, Hand(Limb.Fist), Choreo.BodyAt(A, 0.5f), Pose.Ground, px, Kk, k, _chargeHue, V != null && VOk ? V : null);
                }
            }
            _ringT -= dt;
            if (_ringT <= 0f && k < 0.9f && SchoolStyle.Orb(_chargeSchool))
            {
                _ringT = 0.16f;
                Fx.Ring(at, px, 0.16f, (10f + _orbR * 2f) * Mathf.Lerp(1f, Kk, 0.5f), 2f, 1, 1, TopLayer, 5, 1f, 1.6f,
                    Fx.WithA(_chargeHue.Edge, 0f), Fx.WithA(_chargeHue.Edge, 0.75f), Fx.WithA(_chargeHue.Core, 0.9f));
                // dust swirling in at its feet
                Kit.DustRing(Pose.Ground, px, A.current_tile, 4, -14f, 0.35f, Kk);
            }
            _auraT -= dt;
            if (_auraT <= 0f)
            {
                _auraT = 0.07f;
                Particle g = Fx.Ghost(this, new Vector2(Random.Range(-1f, 1f), Random.Range(0f, 1.5f)) * px, 0f, 0.14f,
                    Fx.WithA(_chargeHue.Edge, 0.35f + 0.3f * k), Fx.WithA(_chargeHue.Edge, 0f));
                if (g != null)
                {
                    g.Sized(1f, 1.08f);
                }
            }
            if (Level >= 2)
            {
                _rumbleT -= dt;
                if (_rumbleT <= 0f && k > 0.4f)
                {
                    _rumbleT = 0.25f;
                    Ruin.Quake(A, Pose.Ground, Scale.Casual, 0.6f + 0.6f * k, Kk);
                }
            }
            if (!_chargeGlint && k >= 0.85f)
            {
                _chargeGlint = true;
                Fx.Glint(at + new Vector2(0f, _orbR * px), px, 0.16f, Color.white, 1f);
            }
        }

        /// <summary>The gathered orb flies forward with the hands (a beam's muzzle) or bursts away; called by Beam and Shoot.</summary>
        public void SpendOrb()
        {
            if (_orb != null)
            {
                Unglue(_orb);
                _orb = null;
            }
            if (_orbCore != null)
            {
                Unglue(_orbCore);
                _orbCore = null;
            }
        }
    }
}
