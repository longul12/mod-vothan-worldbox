using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    internal partial class ChoreoMove
    {
        /// <summary>The body it holds (a grab), or null.</summary>
        public Victim Held;
        private Grip _grip = Grip.Front, _grip0 = Grip.Front;

        /// <summary>The grip it holds the body with right now.</summary>
        public Grip GripNow => _grip;

        /// <summary>
        /// Reaches out and takes hold of the target (a lunge, arms out; the target may answer: a parry
        /// breaks it, an evade leaves it clutching air). From then on the body follows its hands (Grip)
        /// through every beat until it is slammed, thrown or let go. 'mult': the grab's own small blow.
        /// A body that can't be held (too heavy, flying, another's) ends the move with a glint: check
        /// Choreo.Holdable in Plan.
        /// </summary>
        public ChoreoMove Grab(float mult = 0.15f, Grip? grip = null)
        {
            Grip g = grip ?? Grip.Front;
            Beat wind = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.08f,
                To = new Shape(-6f, 0.94f, 1.05f, 0f, -0.03f),
                Ease = Ease.OutQuad,
                Route = Route.Line,
                PathEase = Ease.OutQuad,
                Dest = m => m.ApproachSpot(),
                PerTile = 0.07f,
                NeedsVictim = true,
                Forms = Form.Grab
            };
            wind.Enter = m => m.EnterWind(ShapeOf(Blow.Palm));
            Add(wind);
            Beat act = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.08f,
                To = new Shape(14f, 1.18f, 0.9f, 0f, 0.16f),
                Ease = Ease.OutExpo,
                Forms = Form.Grab,
                Tag = GrabBlow
            };
            act.On(0.5f, m => m.TakeHold(mult, g));
            Add(act);
            return Add(new Beat { Tempo = Tempo.Follow, Dur = 0.06f, To = new Shape(-4f, 0.96f, 1.06f, 0f, 0.04f), Ease = Ease.OutQuad, Track = false, Forms = Form.Grab | Form.Throw });
        }

        private void TakeHold(float mult, Grip g)
        {
            // the body it reached for is gone: another in reach is taken instead (the reach check below
            // still applies to it), or the grab is lost
            if (!VOk && !Retarget())
            {
                Lose();
                return;
            }
            float reach = Stand + 0.6f * Kk + V.stats["size"] * 0.5f + 0.3f;
            if (Vector2.Distance(Pose.Ground, VG) > reach)
            {
                Lose();
                return;
            }
            if (!Touch(V))
            {
                return;
            }
            if (!VAllowed)
            {
                // a class above its limits: it can't be lifted (a shove that only glances), and the move ends
                Deliver(V, new Hit(Blow.Palm, mult + 0.3f), ShapeOf(Blow.Palm), VPoint(0.55f), Dir);
                Lose();
                return;
            }
            Victim b = Victims.Grab(V, this);
            if (b == null)
            {
                // can't be lifted: a shove instead, and the move ends
                Deliver(V, new Hit(Blow.Palm, mult + 0.3f), ShapeOf(Blow.Palm), VPoint(0.55f), Dir);
                Lose();
                return;
            }
            Held = b;
            _grip = _grip0 = g;
            if (mult > 0f)
            {
                HitResult r = Combat.Hit(A, V, mult * Cm, false, false, 0f, this);
                LastHit = r;
            }
            b.P.Flash(0.06f);
            Combat.Pin(V, 3f);
            Choreo.Sound("event:/SFX/OTHER/Punch", A.current_tile);
            if (Vis)
            {
                Vector2 at = VPoint(0.55f);
                Fx.HitSpark(at, Px, 0, -Dir, 3);
                Fx.FocusLines(at, Px, Fx.Count(6), 9f * Kk, 16f * Kk, 0.1f);
            }
            HoldFor(0.04f * ImpactK);
            if (Dev.On) Dev.Trace("choreo " + Id + " grabs " + V.asset.id + "#" + VId);
        }

        /// <summary>
        /// The held body moves to a new grip over 'dur' (Grip.Overhead: hoisted; Grip.Inverted: upside
        /// down, head first), the performer's pose easing into 'pose' (default: leaning back under the
        /// weight). Nothing if it holds nothing.
        /// </summary>
        public ChoreoMove Lift(Grip to, float dur = 0.2f, Shape? pose = null)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                To = pose ?? new Shape(-10f, 0.94f, 1.1f, float.NaN),
                Ease = Ease.OutCubic,
                Grip = to,
                Track = false,
                Forms = Form.Grab | Form.Throw,
                If = m => m.Held != null
            };
            b.Enter = m => Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile, 0.06f);
            return Add(b);
        }

        /// <summary>
        /// A suplex: it arches over backwards, the held body swinging up over its head in an arc and
        /// down behind it head first: the slam (damage 'mult', a crater of base radius 'crater', its
        /// enemies within 'area' tiles hurt too). The body is left lying (Link.Down). 'fire': the crater
        /// may set the grass alight (false: it only breaks the ground).
        /// </summary>
        public ChoreoMove Suplex(float mult, float crater = 1f, float area = 0f, float dur = 0.3f, bool fire = true)
        {
            // clutched to its chest, it arches over backwards: the grip turns with its body, so the held
            // one swings up over its head and comes down head first behind it
            // (a hoist: it straightens and leans back a little, pulling the body up off its feet)
            Beat lift = new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.12f,
                Ease = Ease.OutQuad,
                To = new Shape(-14f, 0.96f, 1.08f, 0f),
                Grip = new Grip(2.5f, 5f, -18f),
                Track = false,
                Forms = Form.Grab | Form.Throw,
                If = m => m.Held != null
            };
            Add(lift);
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                Ease = Ease.InQuad,
                To = new Shape(-168f, 1.02f, 0.96f, 0.06f),
                Grip = new Grip(3f, 3f, 0f),
                Track = false,
                Ghosts = 0.03f,
                Forms = Form.Grab | Form.Throw | Form.Overhead,
                If = m => m.Held != null,
                Tag = GrabBlow
            };
            b.Exit = m => m.SlamDown(mult, crater, area, true, 0.5f, fire);
            Add(b);
            // it holds the bridge a beat, then rolls back up
            return Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, To = new Shape(-150f, 1.06f, 0.92f, 0f), Ease = Ease.OutQuad, Track = false });
        }

        /// <summary>
        /// Drives the held body into the ground (from the air: it drops with it). 'headFirst': upside
        /// down onto its head (a piledriver), else flat on its back. Damage 'mult' (the script's last
        /// blow kills), a crater of base radius 'crater' (scaled by the move's Scale and tier), its
        /// enemies within 'area' tiles hit (x 'areaMult') and knocked down, an impact frame, a shake.
        /// 'dur': the drop's length when airborne. 'fire': the crater may set the grass alight (false:
        /// it only breaks the ground, an Earth Breaker's slam).
        /// </summary>
        public ChoreoMove Slam(float mult, float crater = 1f, float area = 0f, bool headFirst = true, float dur = 0.16f, float areaMult = 0.5f, bool fire = true)
        {
            Beat b = new Beat
            {
                Tempo = Tempo.Act,
                Dur = dur,
                Ease = Ease.InQuad,
                To = new Shape(18f, 0.86f, 1.18f, 0f),
                Grip = headFirst ? new Grip(3.5f, -1f, 180f) : new Grip(4f, -1f, -90f),
                Track = false,
                Ghosts = 0.025f,
                Forms = Form.Grab | Form.Throw | Form.Overhead | Form.Heavy,
                If = m => m.Held != null,
                Tag = GrabBlow
            };
            b.Every = (m, k) =>
            {
                if (m.Vis && k < 0.9f && m.Pose.Height > 0.2f * m.Kk)
                {
                    Kit.SpeedLines(m.BodyPos() + new Vector2(0f, 3f * m.Px), Vector2.down, m.Px, 1, 4f, 0.1f, Fx.WithA(m.Hue.Edge, 0.8f));
                }
            };
            b.Exit = m => m.SlamDown(mult, crater, area, headFirst, areaMult, fire);
            Add(b);
            // the impact pose, crouched over it
            return Add(new Beat { Tempo = Tempo.Impact, Dur = 0.12f, To = new Shape(14f, 1.24f, 0.78f, 0f), Ease = Ease.OutExpo, Track = false });
        }

        private void SlamDown(float mult, float crater, float area, bool headFirst, float areaMult, bool fire = true)
        {
            Pose.Height = 0f;
            if (Held == null)
            {
                return;
            }
            Victim b = Held;
            Actor v = b.A;
            if (!Choreo.Hostile(A, v))
            {
                // no longer an enemy (its city taken, a peace): it is set down unhurt
                Held = null;
                b.LetGo(Impact.Of(Hurt.Flinch, Dir, 0f, 0.3f), A, this);
                Lose();
                return;
            }
            Hit h = new Hit(Blow.Stomp, mult) { Finisher = true };
            bool final = IsFinal(h);
            HitResult r = Combat.Hit(A, v, mult * Cm, false, false, 0f, this);
            LastHit = r;
            bool owed = MeteorSlam.AtDeathsDoor(v);
            Held = null;
            Impact im = Impact.Of(Hurt.DriveIn, Dir, 0f, 1.6f);
            im.Owed = owed;
            im.Daze = 1.2f;
            im.Stop = 0.05f * ImpactK;
            b.LetGo(im, A, this);
            // (where the body really comes down: set on solid ground, never in water, lava or a mountain)
            Vector2 at = b.P.Pose.Ground;
            WorldTile tile = Combat.TileAt(at);
            Victims.Flash(v, Color.white, 0.07f);
            float weight = 1.6f + 0.2f * Level;
            HoldFor(0.06f * ImpactK);
            if (Vis)
            {
                Vector2 hit = at + new Vector2(0f, 1f * Px);
                Kit.ImpactFrame(hit, Px, (0.32f + 0.08f * Level) * Kk, Hue.Pal, r.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
                ChoreoFx.Impact(hit, Vector2.down, Px, Kk, weight, Hue, TopLayer);
                Kit.DustRing(at, Px, tile, 14, 46f, 0.55f, Kk);
                Kit.Debris(at, Px, tile, 10, Kk);
                Kit.Crater(at, Px * Kk, tile, Ruin.Force(Tier, Scale) >= 3, 7f, crater < 1.2f, Hue.Edge);
                Kit.Shockwave(at, Px, 4f, Mathf.Max(14f, (area > 0f ? area : Ruin.Radius(Tier, Scale, crater)) / Px), 0.35f, Fx.WithA(Hue.Edge, 0.9f), 2);
            }
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            if (crater > 0f)
            {
                Ruin.Blast(A, at, crater, Scale, Pose.Ground, fire);
            }
            // the world holds its breath only for big slams: a Heavy technique's, or a Master's (never a Light one below Master)
            if (Scale >= Scale.Heavy || Tier >= Tier.Master)
            {
                Kit.Stop(at, (0.05f + 0.02f * Level) * ImpactK);
            }
            Ruin.Quake(A, at, Scale, 1.4f, Kk);
            if (area > 0f)
            {
                AreaBlow(at, area, areaMult, v, Hurt.Knockdown, 0.7f);
            }
            SetLink(Link.Down, v);
            if (Dev.On) Dev.Trace("choreo " + Id + " slams " + v.asset.id + " x" + (mult * Cm).ToString("F2") + " hp " + v.getHealth() + (owed ? " (owed)" : "") + (final ? "" : " (not last)"));
        }

        /// <summary>
        /// Throws the held body to 'to' (default: 'dist' tiles along its facing): it flies tumbling along
        /// an arc and crashes there (damage 'mult' at the release, the crash breaks the ground with a
        /// crater of base radius 'crater' and hurts the thrower's enemies within 'area' tiles x 'areaMult').
        /// 'fire': the crater may set the grass alight (false: it only breaks the ground).
        /// </summary>
        public ChoreoMove Toss(float mult, float dist = 3f, Func<ChoreoMove, Vector2> to = null, float crater = 0.8f, float area = 1.2f, float areaMult = 0.5f, bool fire = true)
        {
            Beat wind = new Beat { Tempo = Tempo.Wind, Dur = 0.1f, To = new Shape(-16f, 0.92f, 1.08f, float.NaN), Ease = Ease.OutQuad, Grip = new Grip(-2f, 5f, -30f), Track = false, If = m => m.Held != null };
            Add(wind);
            Beat b = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(22f, 1.2f, 0.88f, float.NaN, 0.12f), Ease = Ease.OutExpo, Grip = new Grip(6f, 4f, 40f), Track = false, If = m => m.Held != null, Tag = GrabBlow };
            b.Exit = m =>
            {
                if (m.Held == null)
                {
                    return;
                }
                Victim h = m.Held;
                Actor v = h.A;
                if (!Choreo.Hostile(m.A, v))
                {
                    // no longer an enemy (its city taken, a peace): it is set down unhurt
                    m.Held = null;
                    h.LetGo(Impact.Of(Hurt.Flinch, m.Dir, 0f, 0.3f), m.A, m);
                    m.Lose();
                    return;
                }
                Vector2 dest = to != null ? to(m) : m.Ahead(dist * Mathf.Lerp(1f, m.Kk, 0.5f) + 0.5f);
                HitResult r = Combat.Hit(m.A, v, mult * m.Cm, false, false, 0f, m);
                m.LastHit = r;
                m.Held = null;
                Impact im = Impact.Of(Hurt.Thrown, dest - h.P.Pose.Ground, 0f, 1.2f);
                im.To = dest;
                im.Owed = MeteorSlam.AtDeathsDoor(v);
                im.Crater = crater;
                im.NoFire = !fire;
                im.AreaR = area;
                im.AreaMult = areaMult * mult;
                im.Daze = 0.8f;
                h.LetGo(im, m.A, m);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.SpeedLines(h.P.BodyPos(), dest - h.P.Pose.Ground, m.Px, 4, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
                }
                m.SetLink(Link.Thrown, v);
            };
            Add(b);
            return Add(new Beat { Tempo = Tempo.Follow, Dur = 0.1f, To = new Shape(12f, 1.08f, 0.94f, 0f, 0.06f), Ease = Ease.OutQuad, Track = false });
        }

        /// <summary>
        /// The held body, checked every frame before anything can use it (Victims.Tick only notices after
        /// the moves ticked: a slam or a throw this frame would still hit it). Gone (the god hand took it,
        /// it died, its actor was recycled): HeldLost. No longer an enemy (its city taken, a peace): it is
        /// set down unhurt and the move goes to its recovery.
        /// </summary>
        private void CheckHeld()
        {
            Victim hb = Held;
            Actor hv = hb.A;
            if (hb.Done || hb.P.Finished || hv == null || hv.Sid() != hb.Id || !hv.isAlive() || hv.isInMagnet() || hv.isInsideSomething())
            {
                HeldLost(hb);
                return;
            }
            if (!Choreo.Hostile(A, hv))
            {
                Held = null;
                hb.LetGo(Impact.Of(Hurt.Flinch, Dir, 0f, 0.3f), A, this);
                if (Dev.On) Dev.Trace("choreo " + Id + " lets go of " + hv.asset.id + "#" + hb.Id + ": no longer an enemy");
                if (!Finished && !_broken && _answer == 0 && !(_b != null && _b.Recovery))
                {
                    Lose();
                }
            }
        }

        /// <summary>
        /// The body it held is gone (killed in its hands, picked up by the god hand): whatever was left to do
        /// with it is skipped, and the move goes to its recovery (in the air: it comes down).
        /// </summary>
        public void HeldLost(Victim b)
        {
            if (Held != b)
            {
                return;
            }
            Held = null;
            if (!Finished && !_broken && _answer == 0 && !(_b != null && _b.Recovery))
            {
                if (Dev.On) Dev.Trace("choreo " + Id + " lost the body it held");
                Lose();
            }
        }

        /// <summary>Lets the held body go (on its feet, or falling) with a reaction ('hurt', pushed 'push' tiles).</summary>
        public void ReleaseHeld(Hurt hurt, float push)
        {
            if (Held == null)
            {
                return;
            }
            Victim h = Held;
            Held = null;
            h.LetGo(Impact.Of(hurt, Dir, push, 1f), A, this);
        }

        /// <summary>Lets the held body go at this point of the script ('hurt', pushed 'push' tiles).</summary>
        public ChoreoMove LetGo(Hurt hurt = Hurt.Reel, float push = 0.5f)
        {
            return Do(m => m.ReleaseHeld(hurt, push));
        }
    }
}
