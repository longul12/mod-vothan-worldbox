using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>What a blow is thrown with (where its smear starts, where its spark lands, its sound).</summary>
    internal enum Limb
    {
        Fist,
        Foot,
        Knee,
        Elbow,
        Palm,
        Head,
        Blade,
        Body
    }

    /// <summary>The blows the engine knows how to throw (each with its wind-up, smear, contact and reaction).</summary>
    internal enum Blow
    {
        Jab,
        Cross,
        Hook,
        Uppercut,
        /// <summary>A hook to the liver: the body folds.</summary>
        Body,
        Backfist,
        Elbow,
        Knee,
        Palm,
        Headbutt,
        SnapKick,
        Roundhouse,
        AxeKick,
        Sweep,
        SideKick,
        SpinKick,
        HeelDrop,
        Stomp,
        Slash,
        Cleave,
        RisingSlash,
        DiagSlash,
        Thrust,
        DrawCut,
        SpinCut
    }

    /// <summary>
    /// One blow of a strike: which, how hard (x the user's damage; Combat.Hit adds the tier's Might),
    /// and anything that differs from the blow's defaults. Unset fields keep the blow's own.
    /// </summary>
    internal sealed class Hit
    {
        public Blow Blow;
        public float Mult = 1f;
        /// <summary>The reaction (null: the blow's own, e.g. a cross snaps the head back).</summary>
        public Hurt? Hurt;
        /// <summary>Tiles the target is pushed (-1: the blow's own).</summary>
        public float Push = -1f;
        /// <summary>Launch / juggle height in tiles (-1: the blow's own).</summary>
        public float Lift = -1f;
        public float Daze;
        /// <summary>Scales the blow's visual weight (sparks, flash, hit-stop, shake): 1 as designed.</summary>
        public float Power = 1f;
        /// <summary>The finisher: an impact frame (a black flash on a crit), a global hit-stop, focus lines.</summary>
        public bool Finisher;
        /// <summary>null: lethal only if it is the last blow of the script (earlier blows leave at least 1 health).</summary>
        public bool? Lethal;
        /// <summary>The ground under the target takes it: a crater of this base radius (tiles) at the move's Scale.</summary>
        public float Crater;
        /// <summary>Its crater may set the grass alight (at a Master's force and up); false: it only breaks the ground.</summary>
        public bool Fire = true;
        /// <summary>Others within this many tiles of the target are hit too (x AreaMult) and knocked back.</summary>
        public float Area, AreaMult = 0.5f;
        /// <summary>What it shows while it plays (Form.None: the blow's own).</summary>
        public Form Shows;
        /// <summary>Wind-up and blow lengths, scaled (1 as designed; a feint's quick jab 0.6).</summary>
        public float WindScale = 1f, ActScale = 1f;
        /// <summary>Thrown in the air (a juggle): its poses' heights count from the height it is at, and it stays up.</summary>
        public bool Air;
        /// <summary>
        /// A big cocked wind-up (a haymaker): the wind-up is Charging (a fast blow breaks it), lasts WindScale
        /// times as long (set it: 3-6), and trembles at its end.
        /// </summary>
        public bool Charged;
        /// <summary>Called after it landed (the result, the target).</summary>
        public Action<ChoreoMove, Actor, HitResult> OnLand;

        public Hit(Blow b, float mult)
        {
            Blow = b;
            Mult = mult;
        }
    }

    /// <summary>A blow's shape: its poses, timing, smear, contact point and default reaction.</summary>
    internal sealed class BlowShape
    {
        public Limb Limb;
        public Shape Wind, Hit;
        public float WindDur, ActDur, FollowDur, At = 0.45f, Relax = 0.3f;
        /// <summary>Where it lands on the target (0 feet .. 1 head).</summary>
        public float Contact = 0.6f;
        /// <summary>A straight blow (a streak from shoulder or hip to the target), else an arc.</summary>
        public bool Straight;
        public int SmearHead = 1, SmearThick = 1;
        public int ArcR;
        public float ArcRatio, A0, A1;
        public int ArcThick = 2;
        /// <summary>The arc's centre offset from the body (sprite pixels; y -99: at the feet).</summary>
        public Vector2 ArcOff;
        public bool Spin, Heavy;
        public float Weight = 1f;
        public Hurt Hurt = Hurt.Flinch;
        public float Push, Lift = -1f;
        /// <summary>The push turns this many degrees off the blow's line (a hook knocks it sideways).</summary>
        public float PushTurn;
        public Form Forms;
        /// <summary>Blades: the cut's line across the target (degrees, facing right).</summary>
        public float SlashAngle;
    }

    internal partial class ChoreoMove
    {
        // ------------------------------------------------------------ shapes --

        private static BlowShape[] _shapes;

        /// <summary>The shape of a blow (poses, timing, smear, contact, default reaction).</summary>
        public static BlowShape ShapeOf(Blow b)
        {
            if (_shapes == null)
            {
                BuildShapes();
            }
            return _shapes[(int)b];
        }

        private static BlowShape S(Limb limb, Shape wind, float tWind, Shape hit, float tAct, float tFollow, float contact, float weight, Hurt hurt, float push, Form forms)
        {
            return new BlowShape
            {
                Limb = limb,
                Wind = wind,
                WindDur = tWind,
                Hit = hit,
                ActDur = tAct,
                FollowDur = tFollow,
                Contact = contact,
                Weight = weight,
                Hurt = hurt,
                Push = push,
                Forms = forms
            };
        }

        private static BlowShape Arc(BlowShape s, int r, float ratio, float a0, float a1, int thick, float ox, float oy)
        {
            s.ArcR = r;
            s.ArcRatio = ratio;
            s.A0 = a0;
            s.A1 = a1;
            s.ArcThick = thick;
            s.ArcOff = new Vector2(ox, oy);
            return s;
        }

        private static BlowShape Line(BlowShape s, int head, int thick)
        {
            s.Straight = true;
            s.SmearHead = head;
            s.SmearThick = thick;
            return s;
        }

        private static void BuildShapes()
        {
            BlowShape[] t = new BlowShape[Enum.GetValues(typeof(Blow)).Length];
            t[(int)Blow.Jab] = Line(S(Limb.Fist, new Shape(-5f, 0.95f, 1.04f, 0f, -0.04f), 0.06f, new Shape(12f, 1.16f, 0.9f, 0f, 0.1f), 0.06f, 0.05f,
                0.66f, 0.6f, Hurt.Flinch, 0.08f, Form.Punch | Form.Straight | Form.Fast), 1, 1);
            t[(int)Blow.Cross] = Line(S(Limb.Fist, new Shape(-12f, 0.9f, 1.07f, 0f, -0.07f), 0.09f, new Shape(20f, 1.24f, 0.86f, 0f, 0.17f), 0.07f, 0.07f,
                0.64f, 1f, Hurt.SnapBack, 0.45f, Form.Punch | Form.Straight), 1, 2);
            {
                BlowShape s = Arc(S(Limb.Fist, new Shape(-8f, 0.93f, 1.04f, 0f, -0.03f), 0.08f, new Shape(16f, 1.16f, 0.9f, 0f, 0.1f), 0.07f, 0.07f,
                    0.66f, 1f, Hurt.Spin, 0.4f, Form.Punch | Form.Hook | Form.High), 7, 0.45f, 215f, 365f, 2, 2f, 1f);
                s.PushTurn = -55f;
                s.At = 0.5f;
                t[(int)Blow.Hook] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Fist, new Shape(12f, 1.16f, 0.8f, 0f, 0f), 0.1f, new Shape(-16f, 0.8f, 1.32f, 0.12f, 0.12f), 0.08f, 0.1f,
                    0.74f, 1.3f, Hurt.Launch, 0.6f, Form.Punch | Form.Rising), 9, 1f, -70f, 115f, 2, 1f, -1f);
                s.Heavy = true;
                s.Lift = 1.6f;
                s.Relax = 0.5f;
                t[(int)Blow.Uppercut] = s;
            }
            t[(int)Blow.Body] = Arc(S(Limb.Fist, new Shape(-4f, 1.06f, 0.92f, 0f, -0.03f), 0.08f, new Shape(26f, 1.22f, 0.82f, 0f, 0.14f), 0.07f, 0.08f,
                0.42f, 1f, Hurt.Fold, 0.12f, Form.Punch | Form.Body | Form.Hook), 6, 0.5f, 200f, 350f, 2, 2f, -2f);
            {
                BlowShape s = Arc(S(Limb.Fist, new Shape(-4f, 1f, 1f, 0f, 0f), 0.06f, new Shape(12f, 1.2f, 0.9f, 0f, 0.12f), 0.1f, 0.07f,
                    0.7f, 1.1f, Hurt.SnapBack, 0.5f, Form.Punch | Form.Spin | Form.High), 8, 0.42f, 200f, 380f, 2, 0f, 1f);
                s.Spin = true;
                s.At = 0.7f;
                t[(int)Blow.Backfist] = s;
            }
            t[(int)Blow.Elbow] = Arc(S(Limb.Elbow, new Shape(-14f, 0.92f, 1.05f, 0f, -0.02f), 0.07f, new Shape(24f, 1.12f, 0.9f, 0f, 0.08f), 0.06f, 0.07f,
                0.74f, 1.1f, Hurt.SnapBack, 0.35f, Form.Elbow | Form.High), 5, 0.8f, 125f, -25f, 2, 1f, 2f);
            t[(int)Blow.Knee] = Arc(S(Limb.Knee, new Shape(-6f, 1.08f, 0.88f, 0f, 0f), 0.07f, new Shape(-8f, 0.9f, 1.16f, 0.16f, 0.12f), 0.07f, 0.08f,
                0.45f, 1.1f, Hurt.Fold, 0.18f, Form.Knee | Form.Rising | Form.Body), 5, 0.9f, -70f, 60f, 2, 2f, -2f);
            t[(int)Blow.Palm] = Line(S(Limb.Palm, new Shape(-10f, 0.9f, 1.06f, 0f, -0.08f), 0.1f, new Shape(16f, 1.22f, 0.9f, 0f, 0.2f), 0.07f, 0.08f,
                0.55f, 1.1f, Hurt.Reel, 1.2f, Form.Palm | Form.Straight), 2, 2);
            t[(int)Blow.Headbutt] = Arc(S(Limb.Head, new Shape(-24f, 0.94f, 1.08f, 0f, -0.03f), 0.1f, new Shape(30f, 1.12f, 0.9f, 0f, 0.16f), 0.06f, 0.07f,
                0.86f, 1f, Hurt.SnapBack, 0.4f, Form.Head | Form.Fast), 4, 1f, 90f, -10f, 2, 1f, 3f);
            t[(int)Blow.SnapKick] = Line(S(Limb.Foot, new Shape(-10f, 1.04f, 0.94f, 0.04f, 0f), 0.07f, new Shape(-14f, 1.26f, 0.86f, 0f, 0.14f), 0.06f, 0.07f,
                0.45f, 0.9f, Hurt.Fold, 0.5f, Form.Kick | Form.Fast | Form.Straight | Form.Body), 1, 2);
            {
                BlowShape s = Arc(S(Limb.Foot, new Shape(-6f, 1.02f, 0.96f, 0f, 0f), 0.08f, new Shape(10f, 1.18f, 0.9f, 0f, 0.1f), 0.1f, 0.09f,
                    0.82f, 1.2f, Hurt.Spin, 0.8f, Form.Kick | Form.High | Form.Spin), 10, 0.42f, 190f, 370f, 3, 1f, 2f);
                s.Spin = true;
                s.At = 0.6f;
                s.PushTurn = -40f;
                t[(int)Blow.Roundhouse] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Foot, new Shape(-20f, 0.9f, 1.2f, 0.1f, 0f), 0.12f, new Shape(30f, 1.14f, 0.84f, 0f, 0.1f), 0.08f, 0.1f,
                    0.95f, 1.4f, Hurt.DriveIn, 0f, Form.Kick | Form.Overhead | Form.Heavy), 10, 1f, 100f, -70f, 3, 2f, 3f);
                s.Heavy = true;
                s.At = 0.55f;
                t[(int)Blow.AxeKick] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Foot, new Shape(-8f, 1.1f, 0.86f, 0f, 0f), 0.08f, new Shape(30f, 1.32f, 0.7f, 0f, 0.06f), 0.1f, 0.09f,
                    0.12f, 1f, Hurt.Knockdown, 0.3f, Form.Kick | Form.Low | Form.Spin), 9, 0.32f, 175f, 365f, 2, 1f, -99f);
                s.Spin = true;
                s.At = 0.55f;
                t[(int)Blow.Sweep] = s;
            }
            t[(int)Blow.SideKick] = Line(S(Limb.Foot, new Shape(-12f, 0.94f, 1.04f, 0f, -0.05f), 0.09f, new Shape(-26f, 1.32f, 0.84f, 0f, 0.2f), 0.07f, 0.09f,
                0.52f, 1.2f, Hurt.Reel, 1.4f, Form.Kick | Form.Straight), 2, 2);
            {
                BlowShape s = Line(S(Limb.Foot, new Shape(-4f, 1f, 1f, 0f, 0f), 0.06f, new Shape(-20f, 1.3f, 0.84f, 0f, 0.18f), 0.1f, 0.09f,
                    0.55f, 1.3f, Hurt.Reel, 1.6f, Form.Kick | Form.Spin | Form.Straight), 2, 2);
                s.Spin = true;
                s.At = 0.65f;
                t[(int)Blow.SpinKick] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Foot, new Shape(-20f, 0.9f, 1.2f, 0.5f, 0f), 0.12f, new Shape(40f, 1.1f, 0.86f, 0f, 0.08f), 0.08f, 0.1f,
                    0.9f, 1.5f, Hurt.DriveIn, 0f, Form.Kick | Form.Overhead | Form.Aerial | Form.Heavy), 11, 1f, 100f, -80f, 3, 2f, 3f);
                s.Heavy = true;
                t[(int)Blow.HeelDrop] = s;
            }
            t[(int)Blow.Stomp] = Arc(S(Limb.Foot, new Shape(-10f, 0.92f, 1.12f, 0.14f, 0f), 0.1f, new Shape(8f, 1.2f, 0.82f, 0f, 0.1f), 0.05f, 0.08f,
                0.1f, 1.1f, Hurt.Pin, 0f, Form.Kick | Form.Low | Form.Ground), 6, 1f, 80f, -60f, 2, 2f, -1f);
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(-14f, 0.92f, 1.05f, 0f, -0.03f), 0.09f, new Shape(22f, 1.2f, 0.88f, 0f, 0.14f), 0.07f, 0.08f,
                    0.6f, 1f, Hurt.SnapBack, 0.4f, Form.Blade), 9, 0.75f, 75f, -55f, 3, 2f, 1f);
                s.SlashAngle = -35f;
                t[(int)Blow.Slash] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(-20f, 0.9f, 1.12f, 0.18f, 0f), 0.12f, new Shape(34f, 1.12f, 0.86f, 0f, 0.06f), 0.07f, 0.1f,
                    0.62f, 1.4f, Hurt.Knockdown, 0.3f, Form.Blade | Form.Overhead | Form.Heavy), 10, 1f, 100f, -70f, 3, 2f, 2f);
                s.SlashAngle = -70f;
                s.Heavy = true;
                t[(int)Blow.Cleave] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(-6f, 1.1f, 0.86f, 0.02f, 0f), 0.08f, new Shape(-18f, 0.88f, 1.2f, 0.14f, 0.1f), 0.07f, 0.09f,
                    0.55f, 1.1f, Hurt.Launch, 0.4f, Form.Blade | Form.Rising), 10, 1f, -70f, 110f, 3, 1f, -1f);
                s.SlashAngle = 55f;
                s.Lift = 1.4f;
                t[(int)Blow.RisingSlash] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(-12f, 0.94f, 1.06f, 0f, -0.02f), 0.08f, new Shape(18f, 1.18f, 0.9f, 0f, 0.12f), 0.07f, 0.08f,
                    0.6f, 1f, Hurt.SnapBack, 0.35f, Form.Blade), 9, 0.85f, 120f, -40f, 3, 2f, 2f);
                s.SlashAngle = -50f;
                t[(int)Blow.DiagSlash] = s;
            }
            {
                BlowShape s = Line(S(Limb.Blade, new Shape(-10f, 0.88f, 1.06f, 0f, -0.07f), 0.1f, new Shape(10f, 1.3f, 0.84f, 0f, 0.22f), 0.06f, 0.08f,
                    0.55f, 1.1f, Hurt.Reel, 0.7f, Form.Blade | Form.Straight), 0, 1);
                s.SlashAngle = 0f;
                t[(int)Blow.Thrust] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(8f, 1.12f, 0.86f, 0f, -0.02f), 0.14f, new Shape(6f, 1.3f, 0.82f, 0f, 0.4f), 0.05f, 0.12f,
                    0.55f, 1.4f, Hurt.Crumple, 0f, Form.Blade | Form.Fast), 12, 0.3f, 160f, -20f, 2, 3f, 1f);
                s.SlashAngle = -8f;
                s.At = 0.3f;
                t[(int)Blow.DrawCut] = s;
            }
            {
                BlowShape s = Arc(S(Limb.Blade, new Shape(0f, 1f, 1f, 0f, 0f), 0.05f, new Shape(8f, 1.16f, 0.9f, 0f, 0.06f), 0.1f, 0.08f,
                    0.5f, 1.2f, Hurt.Spin, 0.5f, Form.Blade | Form.Spin), 10, 0.42f, 0f, 360f, 2, 0f, 0f);
                s.Spin = true;
                s.At = 0.72f;
                s.SlashAngle = 6f;
                t[(int)Blow.SpinCut] = s;
            }
            _shapes = t;
        }

        // ------------------------------------------------------------- limbs --

        /// <summary>
        /// Where a blow lands on the target: 'frac' of its height up, but no higher than the performer can
        /// reach from where it is (a fist lands on a giant's shin or belly, not on its head).
        /// </summary>
        public Vector2 ContactPoint(float frac)
        {
            Vector2 p = VPoint(frac);
            Vector2 feet = V != null && V.Sid() == VId ? (Vector2)Choreo.Feet(V) : p;
            float top = Pose.Ground.y + Pose.Height + Hpx * Px * Mathf.Max(1.1f, frac * 1.25f);
            if (p.y > top && p.y - feet.y > 0.01f)
            {
                p.y = Mathf.Max(feet.y, top);
            }
            return p;
        }

        /// <summary>Where a limb is at the end of its reach this frame (world).</summary>
        public Vector2 Hand(Limb l)
        {
            float px = Px, h = Hpx;
            float f = Fwd;
            switch (l)
            {
                case Limb.Foot:
                    return BodyPos() + new Vector2(f * h * 0.3f, -h * 0.22f) * px;
                case Limb.Knee:
                    return BodyPos() + new Vector2(f * h * 0.2f, -h * 0.06f) * px;
                case Limb.Elbow:
                    return BodyPos() + new Vector2(f * h * 0.18f, h * 0.12f) * px;
                case Limb.Palm:
                    return BodyPos() + new Vector2(f * h * 0.3f, h * 0.04f) * px;
                case Limb.Head:
                    return BodyPos() + new Vector2(f * h * 0.08f, h * 0.36f) * px;
                case Limb.Blade:
                    return BodyPos() + new Vector2(f * h * (Armed ? 0.4f : 0.3f), h * 0.1f) * px;
                case Limb.Body:
                    return BodyPos();
                default:
                    return BodyPos() + new Vector2(f * h * 0.25f, -h * 0.05f) * px;
            }
        }

        /// <summary>Where a limb's motion starts (the shoulder for arms, the hip for legs).</summary>
        public Vector2 Root(Limb l)
        {
            float px = Px, h = Hpx;
            switch (l)
            {
                case Limb.Foot:
                case Limb.Knee:
                    return BodyPos() + new Vector2(0f, -h * 0.1f) * px;
                case Limb.Head:
                    return BodyPos() + new Vector2(0f, h * 0.3f) * px;
                default:
                    return BodyPos() + new Vector2(Fwd * h * 0.05f, h * 0.14f) * px;
            }
        }

        // ------------------------------------------------------------ strikes --

        /// <summary>
        /// A blow on the target: the wind-up (stepping into reach as it cocks, if the target slid away),
        /// the blow (the body snaps into the hit pose, the limb's smear, the contact), the
        /// follow-through. 'hurt'/'push': the reaction if not the blow's own. The last blow of the
        /// script is the lethal one (earlier ones leave at least 1 health, so a combo is never cut short
        /// by a body falling mid-way); a killing blow that throws the body lets it die where it lands.
        /// </summary>
        public ChoreoMove Strike(Blow b, float mult, Hurt? hurt = null, float push = -1f)
        {
            return Strike(new Hit(b, mult) { Hurt = hurt, Push = push });
        }

        public ChoreoMove Strike(Hit h)
        {
            BlowShape s = ShapeOf(h.Blow);
            Form shows = h.Shows != Form.None ? h.Shows : s.Forms;
            // the wind-up: anticipation, stepping in as it cocks if the target is out of reach
            Beat wind = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = s.WindDur * h.WindScale,
                To = s.Wind,
                Ease = Ease.OutQuad,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = m => m.ApproachSpot(h.Air),
                PerTile = 0.07f,
                Chase = true,
                NeedsVictim = true,
                Forms = shows,
                Charging = h.Charged
            };
            if (h.Charged)
            {
                wind.Every = (m, k) =>
                {
                    if (k > 0.55f)
                    {
                        m.Pose.Nudge = Choreo.Tremble(m.BeatT, k > 0.85f ? 0.8f : 0.5f);
                    }
                };
            }
            wind.Enter = m =>
            {
                if (h.Air)
                {
                    m.AirBase = m.Pose.Height;
                }
                m.EnterWind(s);
            };
            wind.RelH = h.Air;
            wind.Blow = s;
            if (s.Heavy || h.Finisher)
            {
                wind.On(0.75f, m => m.WindGlint());
            }
            Add(wind);
            // the blow
            Beat act = new Beat
            {
                Tempo = Tempo.Act,
                Dur = s.ActDur * h.ActScale,
                To = s.Hit,
                Ease = Ease.OutExpo,
                Forms = shows,
                Tag = h
            };
            act.Enter = m => m.EnterAct(h, s);
            act.RelH = h.Air;
            act.Blow = s;
            act.On(s.At, m => m.Land(h, s));
            if (s.Spin)
            {
                act.Every = (m, k) => m.SpinTurn(k / Mathf.Max(0.1f, s.At));
            }
            Add(act);
            // the follow-through: the hit pose relaxes a little and whatever went up comes down
            Shape relax = new Shape(s.Hit.Rot * (1f - s.Relax), Mathf.Lerp(s.Hit.SX, 1f, s.Relax), Mathf.Lerp(s.Hit.SY, 1f, s.Relax), 0f, s.Hit.Lunge * 0.85f);
            Beat follow = new Beat { Tempo = Tempo.Follow, Dur = s.FollowDur, To = relax, Ease = Ease.OutQuad, Forms = shows, Track = false, RelH = h.Air, Blow = s };
            follow.Enter = m =>
            {
                m.SpinDone();
                if (m.IsFinal(h))
                {
                    m.BeatDur *= 1.7f;   // the last blow's pose is held: it reads
                }
            };
            return Add(follow);
        }

        /// <summary>Where a blow is thrown from: a blow's length in front of the target (the nearest solid side of it).</summary>
        public Vector2 ApproachSpot()
        {
            return ApproachSpot(false);
        }

        /// <summary>
        /// ApproachSpot; 'air': an air strike (a juggle, a climber on a giant's body) never steps back to a
        /// blow's length when it is already close: it strikes from where it is, and only closes in.
        /// </summary>
        public Vector2 ApproachSpot(bool air)
        {
            if (V == null)
            {
                return Stance;
            }
            float d = Vector2.Distance(Stance, VG);
            float reach = Stand + 0.12f * Kk;
            if (d <= reach && (air || d >= Stand * 0.5f))
            {
                return Stance;   // already in reach: it throws from where it stands
            }
            return StandSpot(Stand);
        }

        private void EnterWind(BlowShape s)
        {
            float d = Vector2.Distance(_g0, _dest);
            if (d > 2.6f * Kk + (Forced ? 3f : 0f))
            {
                if (Dev.On) Dev.Trace("choreo " + Id + ": the target got away (" + d.ToString("F1") + " tiles)");
                Lose();
                return;
            }
            if (d > 0.25f * Kk)
            {
                Current.Ghosts = d > 0.7f ? 0.03f : 0f;
                Current.Dust = 0.05f;
                if (Vis)
                {
                    ChoreoFx.HeelDust(Stance, Stance - _dest, Px, A.current_tile, Kk);
                }
                if (d > 1.2f)
                {
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                }
            }
            else
            {
                Current.Ghosts = 0f;
                Current.Dust = 0f;
            }
            Combat.Pin(V, BeatDur + 0.3f);
        }

        private void WindGlint()
        {
            if (Vis && Level >= 2)
            {
                Fx.Glint(Hand(Limb.Fist) + new Vector2(0f, 1.5f * Px), Px, 0.14f, Hue.Core, 0.8f);
            }
        }

        private float _spinBase;
        private bool _spinning;
        private int _chases;

        private void SpinTurn(float k)
        {
            // it turns right round on the spot: a 2D turn is the sprite narrowing, flipping, widening
            if (!_spinning)
            {
                _spinning = true;
                _spinBase = Fwd;
            }
            float t = Mathf.Clamp01(k);
            float c = Mathf.Cos(Mathf.PI * 2f * t);
            Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
            Pose.FaceRight = t > 0.25f && t < 0.75f ? _spinBase < 0f : _spinBase > 0f;
        }

        private void SpinDone()
        {
            if (_spinning)
            {
                _spinning = false;
                Pose.FaceRight = Fwd > 0f;
            }
        }

        private void EnterAct(Hit h, BlowShape s)
        {
            if (V != null && VOk)
            {
                Combat.Pin(V, BeatDur + 0.3f);
            }
            if (!Vis)
            {
                return;
            }
            float px = Px;
            float life = BeatDur * 1.6f + 0.03f;
            if (s.ArcR > 0)
            {
                float offY = s.ArcOff.y < -50f ? -Pose.PivotUp + 1.5f : s.ArcOff.y;
                int r = Mathf.Clamp(Mathf.RoundToInt(s.ArcR * Mathf.Lerp(1f, Kk, 0.6f) * (Level >= 3 ? 1.15f : 1f)), 4, 60);
                int thick = s.ArcThick + (Level >= 3 && s.ArcThick < 4 ? 1 : 0);
                Kit.Swish(this, Art.Arc(r, s.ArcRatio, s.A0, s.A1, thick), life, new Vector2(s.ArcOff.x * Fwd, offY), Fwd, Hue.Core, Hue.Edge, 6);
            }
            if (s.Straight)
            {
                Vector2 from = Root(s.Limb);
                Vector2 to = V != null ? Vector2.Lerp(ContactPoint(s.Contact), from, 0.15f) : from + Dir * Stand;
                int head = s.SmearHead + (Level >= 3 ? 1 : 0);
                ChoreoFx.Smear(from, to, px, life, Mathf.Min(head, 3), s.SmearThick, Hue.Core, Hue.Edge, FxLayer.Over, 7);
                if (s.Weight >= 1f)
                {
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), Dir, px, 2, 2.5f, 0.08f, Fx.WithA(Hue.Edge, 0.75f), FxLayer.Over);
                }
            }
            if (s.Limb == Limb.Blade && Armed)
            {
                Kit.Line(Hand(Limb.Blade), Hand(Limb.Blade) + Dir * 3f * px, px, 0.06f, 1, FxLayer.Over, 6, Fx.WithA(Color.white, 0.95f), Fx.WithA(Hue.Edge, 0f));
            }
            // the smear frame: the body itself is a blur pulled back along the blow
            Fx.Ghost(this, -Dir * 2f * px, -6f, 0.07f, Fx.WithA(Hue.Ghost, 0.55f), Hue.GhostEnd);
            if (s.Heavy || s.Weight >= 1.2f)
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile, 0.05f);
            }
        }

        /// <summary>Marks a grab's own blows (the grab, a slam, a suplex, a toss) for IsFinal.</summary>
        private static readonly object GrabBlow = new object();

        /// <summary>Is this the script's last blow (no strike, flurry, grab, slam or toss after it that will play)?</summary>
        public bool IsFinal(Hit h)
        {
            bool after = false, found = false;
            for (int i = 0; i < _beats.Count; i++)
            {
                Beat b = _beats[i];
                if (b.Tag == h)
                {
                    after = true;
                    found = true;
                    continue;
                }
                if (after && (b.Tag is Hit || b.Tag is FlurryState || b.Tag == GrabBlow) && Plays(b))
                {
                    return false;
                }
            }
            // a blow of its own making (a clone's, a grab's): final only if the script has no blows left to throw
            return found || !BlowsAhead();
        }

        /// <summary>Has the script blows still to throw after the beat now playing (strikes, flurries, a grab's)?</summary>
        public bool BlowsAhead()
        {
            for (int i = Mathf.Max(0, _bi + 1); i < _beats.Count; i++)
            {
                Beat b = _beats[i];
                if ((b.Tag is Hit || b.Tag is FlurryState || b.Tag == GrabBlow) && Plays(b))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The blow arrives: it lands on the target (or meets its answer, or empty air).</summary>
        private void Land(Hit h, BlowShape s)
        {
            if (!VOk)
            {
                bool again = Retarget();
                WhiffFx(s);
                if (!again)
                {
                    Lose();
                }
                return;
            }
            float reach = Stand + 0.6f * Kk + V.stats["size"] * 0.5f + 0.3f;
            float dist = Vector2.Distance(Pose.Ground, VG);
            if (dist > reach && dist <= reach + 1.2f * Kk && _chases < 2)
            {
                // it slipped back a step: the blow is driven after it (a lunge, a blur of the body)
                _chases++;
                Vector2 to = StandSpot(Stand);
                if (Combat.SafeSpot(to) && Combat.ClearPath(Stance, to))
                {
                    if (Vis)
                    {
                        Fx.Afterimage(this, 0.12f, Fx.WithA(Hue.Ghost, 0.7f), Hue.GhostEnd);
                        Kit.SpeedLines(BodyPos(), to - Stance, Px, 3, 3f, 0.1f, Fx.WithA(Hue.Edge, 0.8f));
                    }
                    Stance = to;
                    Pose.Ground = Stance + Dir * _lunge * Kk;
                    dist = Vector2.Distance(Pose.Ground, VG);
                }
            }
            if (dist > reach)
            {
                // out of reach: this one cuts the air, the next one steps after it
                Audit("whiff", h, s, 0f, dist);
                WhiffFx(s);
                if (++_misses >= 2)
                {
                    Lose();
                }
                return;
            }
            // the limb must really meet the body: a blow thrown at a height the body is not at cuts the air
            // (an air blow on a body that has already fallen, a ground blow at one hanging overhead)
            Vector2 contact = ContactPoint(s.Contact);
            // (what a body can reach: from a little below its feet, a stamp, to a little above its head, a high kick)
            // (heights above each one's own ground: on the map, y is also north and south)
            float bh = Hpx * Px * Pose.SY;
            float ch = contact.y - VG.y;
            float low = Pose.Height - 0.3f * bh, high = Pose.Height + 1.3f * bh;
            float gapY = ch < low ? low - ch : ch > high ? ch - high : 0f;
            if (gapY > 0.15f * Kk)
            {
                Audit("vertwhiff", h, s, gapY, dist);
                WhiffFx(s);
                if (++_misses >= 2)
                {
                    Lose();
                }
                return;
            }
            if (!Touch(V))
            {
                return;
            }
            Audit("hit", h, s, gapY, dist);
            Deliver(V, h, s, contact, Dir);
        }

        /// <summary>
        /// A blow of shape 's' lands on 'v' at 'at' along 'dir': the damage, the reaction, the impact
        /// (scaled to the blow and the tier), the chain it leaves. Used by Strike; techniques may call
        /// it for blows of their own (clones, bespoke hits) after Touch(v) let it through.
        /// </summary>
        public HitResult Deliver(Actor v, Hit h, BlowShape s, Vector2 at, Vector2 dir)
        {
            if (v == null || !Choreo.Hostile(A, v))
            {
                return default;   // (no longer an enemy: its city taken, a peace)
            }
            bool main = v == V;
            bool final = !main || IsFinal(h);
            // the class limits: the target's share was fixed when it was taken, anyone else's is asked now;
            // a class above its limits only takes a glancing blow and rocks where it stands
            float cm;
            bool fair;
            if (main)
            {
                fair = VAllowed;
                cm = Cm;
            }
            else
            {
                fair = Choreo.ClassShare(A, v, this, out cm);
            }
            Hurt hurt = fair ? h.Hurt ?? s.Hurt : Hurt.Stagger;
            float push = (h.Push >= 0f ? h.Push : s.Push) * (hurt == Hurt.Flinch ? 1f : Mathf.Lerp(1f, Kk, 0.5f));
            bool moves = hurt == Hurt.Reel || hurt == Hurt.Spin || hurt == Hurt.Knockdown || hurt == Hurt.Launch || hurt == Hurt.Juggle
                || hurt == Hurt.Thrown || hurt == Hurt.DriveIn || hurt == Hurt.Crumple || hurt == Hurt.SnapBack && push > 0.3f;
            bool lethal = h.Lethal ?? (final && !moves);
            HitResult r = Combat.Hit(A, v, h.Mult * cm, lethal, false, 0f, this);
            LastHit = r;
            // (a body the combo has worn down to the edge dies of the last blow, wherever it lands)
            bool owed = final && !lethal && MeteorSlam.AtDeathsDoor(v);
            if (final && r.Landed)
            {
                try
                {
                    A.attackTargetActions(v, v.current_tile);   // its weapon's and traits' own on-hit effects
                }
                catch (Exception e)
                {
                    SakugaMod.Report("choreo on-hit", e);
                }
            }
            float weight = s.Weight * h.Power * (0.85f + 0.15f * Level) * (h.Finisher ? 1.25f : 1f);
            float hold = (0.012f + 0.028f * weight) * ImpactK * (Level <= 1 ? 0.7f : 1f);
            Vector2 pd = dir;
            if (s.PushTurn != 0f)
            {
                pd = Choreo.Rotate(dir, s.PushTurn * Fwd);
            }
            Impact im = Impact.Of(hurt, pd, push, Mathf.Clamp(weight, 0.3f, 2.2f));
            im.Lift = h.Lift >= 0f ? h.Lift : s.Lift;
            im.Daze = h.Daze;
            im.Owed = owed;
            im.Stop = hold;
            // (no im.Crater: the blow breaks the ground itself below, a driven-in body's crash must not again)
            if (Combat.Living(v) || owed)
            {
                Victims.Hurt(v, A, this, im);
            }
            Tier vt = Tiers.Of(v);
            Victims.Flash(v, vt != Tier.None && !r.Killed && Level >= 2 ? Tiers.Color(vt) : Color.white, 0.04f + 0.02f * Mathf.Min(2f, weight));
            if (main)
            {
                HoldFor(hold);
                WatchMe();
            }
            if (Vis)
            {
                ChoreoFx.Impact(at, dir, Px, Kk, weight, Hue, TopLayer, h.Finisher);
                if (s.Limb == Limb.Blade)
                {
                    CutLines(at, s, weight);
                }
                if (s.Contact < 0.25f || hurt == Hurt.DriveIn)
                {
                    Kit.DustRing(Victims.GroundOf(v), VPx, v.current_tile, 6, 30f, 0.4f, Mathf.Sqrt(Kk));
                }
                // the impact frame: a crit from II, a finisher at III, or a Heavy technique's finisher from II
                if (r.Crit && Level >= 2 || h.Finisher && (Level >= 3 || Level >= 2 && Scale >= Scale.Heavy))
                {
                    // (behind the bodies: they read as silhouettes against it)
                    Kit.ImpactFrame(at, Px, (0.16f + 0.08f * weight) * Kk, Hue.Pal, r.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
            }
            // the world holds its breath only for big blows: a Heavy technique's finisher, or a Master's
            if (h.Finisher && (Scale >= Scale.Heavy && Level >= 2 || Tier >= Tier.Master))
            {
                Kit.Stop(at, (0.025f + 0.02f * weight) * ImpactK);
            }
            if (weight >= 1f || h.Finisher)
            {
                Ruin.Quake(A, at, Scale, weight * (h.Finisher ? 1.3f : 1f), Kk);   // (jabs never shake the camera)
            }
            if (h.Crater > 0f)
            {
                Vector2 g = Victims.GroundOf(v);
                if (Vis)
                {
                    Kit.Crater(g, Px * Kk, Combat.TileAt(g), Ruin.Force(Tier, Scale) >= 3, 5f, h.Crater < 1.2f, Hue.Edge);
                }
                Ruin.Blast(A, g, h.Crater, Scale, Pose.Ground, h.Fire);
            }
            if (h.Area > 0f)
            {
                AreaBlow(Victims.GroundOf(v), h.Area, h.AreaMult * h.Mult, v, Hurt.Reel, 0.6f);
            }
            ChoreoFx.BlowSound(s.Limb, weight, Armed, v.current_tile);
            switch (hurt)
            {
                case Hurt.Launch:
                    SetLink(Link.Launched, v);
                    break;
                case Hurt.Juggle:
                    SetLink(Link.Launched, v);
                    break;
                case Hurt.Knockdown:
                case Hurt.DriveIn:
                case Hurt.Pin:
                case Hurt.Crumple:
                    SetLink(Link.Down, v);
                    break;
                case Hurt.Thrown:
                    SetLink(Link.Thrown, v);
                    break;
                default:
                    SetLink(Link.Close, v);
                    break;
            }
            h.OnLand?.Invoke(this, v, r);
            if (Dev.On) Dev.Trace("choreo " + Id + " " + h.Blow + " x" + (h.Mult * cm).ToString("F2") + " -> " + v.asset.id + " hp " + v.getHealth()
                + (r.Crit ? " CRIT" : "") + (owed ? " (owed)" : "") + " " + hurt);
            return r;
        }

        /// <summary>A blade's cut across the target: a line in the tier's colour with a white core.</summary>
        private void CutLines(Vector2 at, BlowShape s, float weight)
        {
            float ang = s.SlashAngle;
            if (Fwd < 0f)
            {
                ang = 180f - ang;
            }
            float rad = ang * Mathf.Deg2Rad;
            Vector2 ld = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float half = (4.5f + 1.5f * weight) * Mathf.Clamp(VHpx / 10f, 0.8f, 2.5f) * Px;
            Kit.Line(at - ld * half, at + ld * half, Px, 0.09f, 2, TopLayer, 15, Fx.WithA(Hue.Edge, 0.95f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
            Kit.Line(at - ld * half * 0.85f, at + ld * half * 0.85f, Px, 0.06f, 1, TopLayer, 16, Color.white, Fx.WithA(Hue.Core, 0.8f), Fx.WithA(Hue.Core, 0f));
        }

        /// <summary>A blow that meets nothing: a white swish through the air and a whoosh.</summary>
        private void WhiffFx(BlowShape s)
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (!Vis)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), Hand(s.Limb) + new Vector2(Fwd * 2f * Px, 0f), Px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = Fwd;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        private static readonly List<Actor> _area = new List<Actor>();

        /// <summary>
        /// Everyone of the performer's enemies within 'r' tiles of 'at' (but 'skip') is hit by an area blow
        /// of 'mult' (they may evade it; never parry it) and reacts ('hurt', pushed away from 'at'; one
        /// above the class limits only takes a glancing blow and rocks). Returns how many were hit.
        /// </summary>
        public int AreaBlow(Vector2 at, float r, float mult, Actor skip, Hurt hurt, float push, float power = 1f)
        {
            Choreo.Foes(A, at, r, _area);
            int n = 0;
            for (int i = 0; i < _area.Count; i++)
            {
                Actor u = _area[i];
                if (u == skip || !Choreo.FairBlow(A, u, this, out float cm, out bool full, true))
                {
                    continue;
                }
                if (!Touch(u, true))
                {
                    continue;
                }
                float near = 1f - Mathf.Clamp01(Vector2.Distance(u.current_position, at) / Mathf.Max(0.1f, r));
                HitResult res = Combat.Hit(A, u, mult * cm * Mathf.Lerp(0.6f, 1f, near), true, false, 0f, this);
                n++;
                Vector2 away = u.current_position - at;
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, A, this, Impact.Of(full ? hurt : Hurt.Stagger, away, push * Mathf.Lerp(0.6f, 1f, near), power));
                }
                else
                {
                    Victims.Flash(u, Color.white, 0.05f);
                }
                if (Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), away, Px, Kk, 0.7f * power, Hue, TopLayer);
                }
                if (Dev.On) Dev.Trace("choreo " + Id + " area x" + (mult * cm).ToString("F2") + " -> " + u.asset.id + " hp " + u.getHealth() + (res.Killed ? " killed" : ""));
            }
            return n;
        }

        // ------------------------------------------------------------ flurry --

        /// <summary>
        /// A flurry: 'n' blows in 'dur' seconds (at Novice pace), alternating 'a' and 'b', faster and
        /// faster, a fan of smears and body blurs, the target driven back a step at a time and twitching
        /// under every one; 'mult' is the damage of all of them together (none of them lethal: a
        /// finishing Strike usually follows).
        /// </summary>
        public ChoreoMove Flurry(int n, float mult, Blow a = Blow.Jab, Blow b = Blow.Cross, float dur = 0.5f, float drive = 0.06f)
        {
            n = Mathf.Clamp(n, 2, 120);
            FlurryState st = new FlurryState { N = n, Mult = mult / n, A = a, B = b, Drive = drive };
            Beat f = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = new Shape(10f, 1.1f, 0.92f, 0f, 0.08f),
                Ease = Ease.OutQuad,
                NeedsVictim = true,
                Forms = Form.Flurry | ShapeOf(a).Forms | ShapeOf(b).Forms,
                Tag = st
            };
            f.Enter = m =>
            {
                st.I = 0;
                st.Tid = -1;
                if (m.V != null)
                {
                    Combat.Pin(m.V, m.BeatDur + 0.3f);
                }
            };
            f.Every = (m, k) => m.FlurryTick(st, k);
            return Add(f);
        }

        private sealed class FlurryState
        {
            public int N, I;
            public float Mult, Drive;
            public Blow A, B;
            /// <summary>The unit its blows last reached (a new one, after a retarget, may answer the next blow).</summary>
            public long Tid = -1;
        }

        // the body's jerk with the latest flurry blow, held for a few hundredths
        private float _fRot, _fSX = 1f, _fSY = 1f, _fLunge, _fT;

        private void FlurryTick(FlurryState st, float k)
        {
            _fT -= Clock.Dt;
            if (_fT > 0f)
            {
                Pose.Rot = _fRot;
                Pose.SX = _fSX;
                Pose.SY = _fSY;
                _lunge = _fLunge;
            }
            // keep in reach as the target is driven back
            if (V != null && Vector2.Distance(Stance, VG) > Stand + 0.15f * Kk)
            {
                Vector2 to = StandSpot(Stand);
                Stance = Vector2.MoveTowards(Stance, to, Clock.Dt * 3.5f * Kk);
            }
            // blows land faster and faster: the i-th at (i/n)^0.8 of the beat
            while (st.I < st.N && k >= Mathf.Pow(st.I / (float)st.N, 0.8f))
            {
                int i = st.I++;
                if (!VOk)
                {
                    if (!Retarget())
                    {
                        Lose();
                        return;
                    }
                }
                // (the first blow on each unit goes through Touch: it may answer, Hooks.Reached sees it)
                long vid = V.Sid();
                if (vid != st.Tid)
                {
                    if (!Touch(V))
                    {
                        return;
                    }
                    st.Tid = vid;
                }
                FlurryBlow(st, i);
                if (Finished || _answer != 0)
                {
                    return;
                }
            }
        }

        private void FlurryBlow(FlurryState st, int i)
        {
            BlowShape s = ShapeOf((i & 1) == 0 ? st.A : st.B);
            float px = Px;
            // the body jerks with each blow
            float side = (i & 1) == 0 ? 1f : -1f;
            _fRot = Pose.Rot = s.Hit.Rot * Random.Range(0.6f, 1f);
            _fSX = Pose.SX = Mathf.Lerp(1f, s.Hit.SX, Random.Range(0.6f, 1f));
            _fSY = Pose.SY = Mathf.Lerp(1f, s.Hit.SY, Random.Range(0.6f, 1f));
            _fLunge = _lunge = s.Hit.Lunge * Random.Range(0.5f, 1f);
            _fT = 0.035f;
            Vector2 at = VPoint(Random.Range(0.35f, 0.85f)) + new Vector2(Random.Range(-1.5f, 1.5f), 0f) * VPx;
            if (Vis)
            {
                Vector2 from = Root(s.Limb) + Side * side * Random.Range(0.5f, 2f) * px;
                ChoreoFx.Smear(from, at, px, 0.07f, Level >= 3 ? 2 : 1, s.SmearThick, Hue.Core, Hue.Edge, FxLayer.Over, 7);
                Particle star = Fx.Spawn(Art.HitStars[0], at, px, 0.05f, TopLayer, 20);
                if (star != null)
                {
                    star.Rot = Random.Range(0f, 90f);
                    star.Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                if ((i & 1) == 0)
                {
                    Fx.Ghost(this, Side * side * 2f * px - Dir * px, side * 8f, 0.08f, Fx.WithA(Hue.Ghost, 0.5f), Hue.GhostEnd);
                }
                if (i % 3 == 0)
                {
                    Kit.Sparks(at, px, 2, 40f, 80f, Hue.Edge);
                }
            }
            HitResult r = Combat.Hit(A, V, st.Mult * Cm, false, false, 0f, this);
            LastHit = r;
            // (a class above its limits only rocks under them)
            Impact im = Impact.Of(VAllowed ? Hurt.Flinch : Hurt.Stagger, Dir, st.Drive, 0.7f);
            Victims.Hurt(V, A, this, im);
            if ((i & 1) == 0)
            {
                ChoreoFx.BlowSound(s.Limb, 0.6f, Armed, V.current_tile);
            }
            SetLink(Link.Close, V);
        }
    }
}
