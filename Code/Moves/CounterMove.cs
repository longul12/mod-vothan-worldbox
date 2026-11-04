using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// The shared craft of the quick answers (Interrupt Jab, Slip Counter, Duck and Sweep, Reversal):
    /// one opponent ('Foe'), a phase clock, the tier's tempo (TierInfo.Linger: a God's recoveries
    /// are under half a Novice's, its action beats a little quicker), safe stepping, and the answering
    /// blow itself (Strike: Combat.Reach first, so an answer can be answered in turn, then the hit,
    /// the hit star, the flash and the stop). Short moves, read as one clean motion.
    /// </summary>
    internal abstract class CounterMove : Move
    {
        protected readonly Actor Foe;
        protected readonly long FoeId;
        /// <summary>The move it answered (null when it acts on its own).</summary>
        protected readonly Move Incoming;
        protected readonly Palette Pal;
        protected readonly Tier Tr;
        /// <summary>Size vs a human, and its half-way blend (distances).</summary>
        protected readonly float Kk, Lk;
        /// <summary>Recovery beats scale (1 Novice .. 0.42 God) and action beats (its square root).</summary>
        protected readonly float Snap, Tempo;
        protected Vector2 Home;
        protected int Ph;
        protected float Pt;
        private bool _entered;
        /// <summary>The blow landed (and on what), for the chain it leaves.</summary>
        protected bool Landed, Launched;
        /// <summary>The foe was reached (one read per answer: Combat.Reach), and it answered the answer.</summary>
        private bool _reached, _answered;

        protected CounterMove(Actor a, Actor foe, Move incoming, int level, Technique tech) : base(a)
        {
            Tech = tech;
            Level = Mathf.Clamp(level, 1, 3);
            Foe = foe;
            FoeId = foe.Sid();
            Incoming = incoming;
            Pal = Palette.For(a, Level);
            Tr = Tiers.Of(a);
            Kk = K;
            Lk = Mathf.Lerp(1f, Kk, 0.5f);
            Snap = SingleKit.Snap(Tr);
            Tempo = Mathf.Sqrt(Snap);
            Home = a.current_position;
            Face(FoeBody());
        }

        protected float Fwd => Pose.FaceRight ? 1f : -1f;

        protected bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        /// <summary>
        /// The same foe, alive, not taken by the god hand or into a building, and still a foe (Combat.Hostile:
        /// a peace made or a city taken mid-answer leaves a friend nobody throws, stuns or breaks).
        /// </summary>
        protected bool FoeOk()
        {
            return Foe != null && Foe.Sid() == FoeId && Combat.Living(Foe) && !Foe.isInMagnet() && !Foe.isInsideSomething() && Combat.Hostile(A, Foe);
        }

        /// <summary>The middle of the foe's body as it is drawn now.</summary>
        protected Vector2 FoeBody()
        {
            if (Foe == null)
            {
                return Pose.Ground;
            }
            if (Moves.TryGet(Foe, out Move m) && m.A == Foe)
            {
                return m.BodyPos();
            }
            Sprite s = Fx.UnitSprite(Foe);
            float pivot = s != null ? Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) : 3.5f;
            return new Vector2(Foe.current_position.x, Foe.current_position.y + Foe.position_height + pivot * Foe.actor_scale);
        }

        /// <summary>Turns to face a point.</summary>
        protected void Face(Vector2 at)
        {
            float dx = at.x - Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                Pose.FaceRight = dx > 0f;
            }
        }

        protected void Next(int p)
        {
            Ph = p;
            Pt = 0f;
            _entered = false;
        }

        protected bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        /// <summary>A neutral pose at 'g'.</summary>
        protected void Stand(Vector2 g)
        {
            Pose.Ground = g;
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
        }

        /// <summary>
        /// The nearest safe spot to 'want' it can step to from 'from' in a straight line (no walls,
        /// water or lava on the way); 'from' itself if none.
        /// </summary>
        protected static Vector2 StepTo(Vector2 from, Vector2 want)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = Vector2.Lerp(from, want, 1f - i * 0.25f);
                if (Combat.SafeSpot(p) && Combat.ClearPath(from, p))
                {
                    return p;
                }
            }
            return from;
        }

        /// <summary>A spot beside the foe ('side' +1/-1 across the line between them), at 'dist' tiles from it.</summary>
        protected Vector2 Beside(float side, float dist)
        {
            Vector2 f = Foe.current_position;
            Vector2 d = Pose.Ground - f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(-Fwd, 0f);
            Vector2 across = new Vector2(-d.y, d.x) * side;
            return f + (d * 0.55f + across * 0.85f).normalized * dist;
        }

        /// <summary>
        /// Where to stand to strike the foe from 'from': at arm's length on the line to it, at most 'max'
        /// tiles away (safe ground, a clear path); 'from' if it is already in reach or can't get there.
        /// A foe that stumbled past or reeled away is followed by one step, never chased across the field.
        /// </summary>
        protected Vector2 Close(Vector2 from, float max)
        {
            if (!FoeOk())
            {
                return from;
            }
            Vector2 f = Foe.current_position;
            float d = Vector2.Distance(from, f);
            float arm = Reach;
            if (d <= arm + 0.15f)
            {
                return from;
            }
            Vector2 want = from + (f - from) / d * Mathf.Min(d - arm, max);
            return StepTo(from, want);
        }

        /// <summary>Arm's length from the foe on the line towards it.</summary>
        protected float Reach => 0.62f * Kk + (Foe != null ? Foe.stats["size"] * 0.5f : 0f);

        /// <summary>
        /// The answering blow: the foe may answer it in turn (Combat.Reach, once per answer, at its first
        /// blow that connects: a counter to the counter), then it lands (x'mult' of the tier's blow, a
        /// third against a heavier class) with a hit star pointing 'away', a flash over the foe, sparks,
        /// and a stop ('stop' seconds). False if it was answered (then every later blow cuts the air too)
        /// or the foe is gone or out of reach ('reach' extra tiles of slack).
        /// </summary>
        protected bool Strike(float mult, Vector2 away, float stop, int star, float slack = 0.6f)
        {
            if (_answered || !FoeOk() || Vector2.Distance(Pose.Ground, Foe.current_position) > Reach + slack + 0.5f * Kk)
            {
                return false;
            }
            if (!_reached)
            {
                _reached = true;
                if (Combat.Reach(this, Foe, false) != Contact.Hit)
                {
                    _answered = true;
                    return false;   // the answer was answered
                }
            }
            else if (Moves.Intangible(Foe))
            {
                return false;   // (mid-blink: nothing to strike)
            }
            bool allowed = Combat.Allowed(A, Foe, out float cm);
            HitResult r = Combat.Hit(A, Foe, allowed ? mult * cm : mult * 0.35f, true, true, 0f, this);
            Landed = r.Landed;
            Vector2 at = FoeBody();
            Anim.Sound(star >= 2 ? "event:/SFX/HIT/HitGeneric" : "event:/SFX/OTHER/Punch", Foe.current_tile);
            if (Kit.Visible(at))
            {
                float px = Px;
                Fx.HitSpark(at, px, Mathf.Clamp(star, 0, 2), away, 3 + star * 2);
                Tier vt = Tiers.Of(Foe);
                StillWater.FlashUnit(Foe, 0.06f, vt != Tier.None && !r.Killed ? Fx.WithA(Tiers.LightColor(vt), 0.9f) : new Color(1f, 1f, 1f, 0.9f));
                Kit.Sparks(at, px, 3 + star, 50f, 110f, Pal.Edge);
                if (star >= 2)
                {
                    Fx.Ring(at, px, 0.16f, 2f, 10f * Kk, 2, 1, FxLayer.Top, 3, 0.5f, 2.5f, Fx.WithA(Pal.Core, 0.95f), Fx.WithA(Pal.Edge, 0.5f), Fx.WithA(Pal.Edge, 0f));
                    Fx.FocusLines(at, px, Fx.Count(6), 9f * Kk, 16f * Kk, 0.1f);
                }
                if (r.Crit)
                {
                    Kit.ImpactFrame(at, px, 0.38f * Kk, Pal, true);
                }
            }
            if (stop > 0f)
            {
                Kit.Stop(at, stop);
            }
            return true;
        }

        /// <summary>
        /// The foe's own move, if it is still the one this answered (its whiffed or broken attack) or a
        /// counter of its own, is cut short so the answer's blow can throw it; never someone's puppet.
        /// </summary>
        protected void BreakFoeMove()
        {
            if (FoeOk() && Moves.TryGet(Foe, out Move m) && m.A == Foe && !(m is Puppet) && !m.Interruptible && !m.Cinematic
                && (m == Incoming || m.CounterDepth > 0 || Reads.Recovering(Foe)))
            {
                Moves.Cancel(m);
            }
        }

        /// <summary>
        /// Throws the foe away from 'from' (heavy ones stagger), dazed 'daze' seconds. A foe above the
        /// class this fighter may use techniques on (Combat.Allowed) is too heavy to throw: it only rocks.
        /// </summary>
        protected void Throw(Vector2 from, float dist, float apex, float daze, float spin = 540f)
        {
            if (!FoeOk() || !Foe.hasHealth())
            {
                return;
            }
            if (!Combat.Allowed(A, Foe))
            {
                Combat.Stagger(Foe, 0.3f);   // (as Still Water's riposte: its move plays on, nothing flies)
                return;
            }
            BreakFoeMove();
            if (Moves.Locked(Foe) || Weights.Heavy(Foe))
            {
                Combat.Stagger(Foe, Mathf.Max(0.3f, daze));
                return;
            }
            float lk = Lk * SingleKit.Throw(Tr);
            Combat.Launch(Foe, from, dist * lk, apex * Mathf.Sqrt(lk), Random.value < 0.5f ? -spin : spin);
            Launched = apex > 0.9f;
            if (daze > 0f)
            {
                Foe.addStatusEffect("stunned", daze);
            }
        }

        /// <summary>
        /// Springs back to a neutral pose at Home over 'dur' (from a lean of 'rot0' and a squash of
        /// 'sx0' x 'sy0'): an overshoot through upright, then still. True when done.
        /// </summary>
        protected bool Recover(float dur, float rot0, float sx0, float sy0)
        {
            float amp = Anim.Spring(Pt / Mathf.Max(0.05f, Snap), 10f, 16f);
            Pose.Ground = Home;
            Pose.Height = 0f;
            Pose.Rot = rot0 * amp;
            Pose.SX = 1f + (sx0 - 1f) * amp;
            Pose.SY = 1f + (sy0 - 1f) * amp;
            Pose.Nudge = Vector2.zero;
            return Pt >= dur;
        }

        /// <summary>Ends on the chain the answer opened (the Brain follows at once: a counter-attack).</summary>
        protected void Done(Link link)
        {
            Stand(Home);
            if (FoeOk() && Foe.hasHealth() && link != Link.None && Vector2.Distance(Home, Foe.current_position) <= 4f)
            {
                EndLink = link;
                EndTarget = Foe;
            }
            Finished = true;
        }
    }
}
