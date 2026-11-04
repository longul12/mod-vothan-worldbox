using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// FLOW STRIKE. A master's ordinary blow: never one swing and a pause, always a short chain
    /// of two to four blows, the fighter darting round its enemy between them, each blow a clear
    /// pose with its swoosh in the tier's colours. It stands in for the game's weapon swing
    /// (FlowStrikes), so it comes as often as those did, deals about what they would have dealt
    /// in that time (the tier's Might on top), and leaves a chain hook so a technique follows.
    ///
    /// Beat sheet, a Master's "slash, side-step, slash, thrust" (game seconds at x1):
    ///
    ///   STEP IN  0.00-0.06  a dash into reach if it isn't already, leaning into it, afterimages
    ///   WIND     0.06-0.09  anticipation: the blade drawn back, a squash, the weight on the back foot
    ///   SLASH    0.09-0.14  snap to the stretched blow (120% x 88%, leaning in), a crescent swoosh in
    ///                       the tier's colours, a smear afterimage; at 0.12 the cut: a slash line
    ///                       across the enemy (white core, tier edge), steel sparks, a red flash
    ///   HOLD     0.14-0.17  the pose held, relaxing
    ///   SIDE     0.17-0.23  a quick slide to the enemy's flank (from its shoulder to its hip side)
    ///   SLASH    0.23-0.31  the same blow from the new angle, mirrored
    ///   PRESS    0.31-0.36  a step through
    ///   THRUST   0.36-0.48  the long lunge (130% wide), a straight streak; the last blow is the only
    ///                       lethal one: a flash disk, a ring, focus lines, a tiny shake, and the enemy
    ///                       is knocked back half a tile (a big one staggers; a crit: a Black Flash)
    ///   RECOVER  0.48-0.55  springs back upright. Chain hook: Close
    ///
    /// The pool (never the same chain twice running; sides, depths and footwork vary every time,
    /// and everything is mirrored by facing):
    ///   armed     slash-slash-thrust, rising slash into a hop over it and a cleave, spin cut and
    ///             thrust, thrust and a run round it into a spin cut, slash and rising slash;
    ///             Grandmasters blink between cuts; a cleave into a running spin cut
    ///   unarmed   jab-cross-hook, knee-elbow, spinning backfist and jab, jab and sweep kick, hook
    ///             and a hop over it into a spinning backfist; Grandmasters run round it between
    ///             cross, hook and knee; Gods blink through a four-blow barrage
    ///   a much bigger enemy (a bear, a dragon on the ground): blows at its legs, round it, then a
    ///             leap to strike at its head (it staggers: chain hook Down)
    ///   a smaller one: a launching kick that sends it up (critters fly comically far): Launched
    /// Grandmasters and Gods move faster (x0.88, x0.78), may add a blow, and their heavy finishers
    /// crack the ground (a God's scorches it). Footwork: a press, a side-step, a hop over the enemy
    /// (a forward flip), a run round its back, or (Grandmaster and up) a blink.
    ///
    /// The first blow is the contact (Combat.Reach): parried, the chain breaks and the fighter
    /// reels back; evaded, it cuts an afterimage and overreaches. An enemy that dies or is taken
    /// mid-chain is swapped for another within 2 tiles (same class or lighter), or the chain ends
    /// on a pose and a glint. An enemy that slips back is followed once. The enemy is never held:
    /// it is pinned (no swings of its own) while the chain lasts.
    ///
    /// EXCHANGE. Two Flow fighters going at each other at the same moment (the other about to
    /// swing within 0.3 s, or answering this chain within its first 0.3 s): the chains meet.
    ///
    ///   ENGAGE   0.07  both slide square to each other round their middle, guard up
    ///   CLASH x3-5     0.10 each: both draw back, lunge, and the blows meet in the middle at head,
    ///                  body or knee height: a two-coloured starburst, sparks grinding out sideways
    ///                  (hot metal from two blades), a "ting" every other one; both rock back and
    ///                  slide. Now and then one gets through (a hit star, a flash, the other driven
    ///                  back). The pair turns round its middle between clashes
    ///   BREAK    0.20  the last, biggest clash: a starburst in both colours, a sonic ring, a
    ///                  fountain of sparks, a shake; both are blown apart sliding (skid marks), or
    ///                  one wins it and the other is thrown back reeling
    ///   SETTLE   0.07  both spring upright, facing each other. Both get chain hooks.
    ///
    /// Everything happens in move time (paused with the game, capped at high speeds).
    /// </summary>
    internal sealed class FlowStrike : Move
    {
        /// <summary>
        /// Any technique (a duel's bout, a puppet) may cut it short between its blows: it is the
        /// fighting between techniques. Never mid-blow (wind-up and strike), mid-flip or mid-blink,
        /// nor while two masters' blades clash: the technique flows out of a landed blow instead of
        /// snapping a pose in half (the Brain waits the few hundredths for the next opening).
        /// </summary>
        public override bool Interruptible
        {
            get
            {
                if (Finished)
                {
                    return true;
                }
                if (Exchanging)
                {
                    return _phase == Phase.Engage || _phase == Phase.Settle;
                }
                switch (_phase)
                {
                    case Phase.Wind:
                    case Phase.Strike:
                        return false;
                    case Phase.Travel:
                        return !(_entered && _tTravel > 0f && (_stepNow == Step.Hop || _stepNow == Step.Blink));
                    default:
                        // (still coming down from a knee, a rising cut or a leap: once it has landed)
                        return Pose.Height <= 0.05f;
                }
            }
        }

        internal enum Blow
        {
            Jab,
            Cross,
            Hook,
            Knee,
            Elbow,
            Backfist,
            Sweep,
            LaunchKick,
            Slash,
            RisingSlash,
            Thrust,
            SpinCut,
            Cleave,
            LegCut,
            LegKick,
            LeapStrike
        }

        internal enum Step
        {
            None,
            Press,
            Side,
            Hop,
            Circle,
            Blink
        }

        private enum Touch
        {
            Punch,
            Cut,
            Kick
        }

        private enum Phase
        {
            Travel,
            Wind,
            Strike,
            Hold,
            Recover,
            Recoil,
            Whiff,
            Engage,
            Clash,
            Break,
            Settle
        }

        /// <summary>One blow's poses and marks.</summary>
        private struct Shape
        {
            public float WRot, WSX, WSY, WH, WBack;
            public float HRot, HSX, HSY, HH, HLunge;
            public bool Spin;
            /// <summary>How far up the enemy's sprite it lands (0 feet .. 1 head).</summary>
            public float Contact;
            public int ArcR, ArcThick;
            public float ArcRatio, A0, A1;
            public Vector2 ArcOff;
            public Touch Touch;
            public float Weight;
            public bool Heavy;
            public float TWind, TStrike;
            /// <summary>The cut's line across the enemy (degrees, for a fighter facing right).</summary>
            public float SlashAngle;
        }

        private struct Beat
        {
            public Blow B;
            public Step S;
            public float Depth;
            public float Mult;
            public bool Last;
        }

        /// <summary>A chain in the pool.</summary>
        private sealed class Tpl
        {
            public readonly string Id;
            public readonly float W;
            public readonly Tier Min;
            public readonly Blow[] B;
            public readonly Step[] S;

            public Tpl(string id, float w, Tier min, Blow[] b, Step[] s)
            {
                Id = id;
                W = w;
                Min = min;
                B = b;
                S = s;
            }
        }

        private static readonly Tpl[] PoolArmed =
        {
            new Tpl("slash_slash_thrust", 1f, Tier.Master, new[] { Blow.Slash, Blow.Slash, Blow.Thrust }, new[] { Step.None, Step.Side, Step.Press }),
            new Tpl("rising_hop_cleave", 1f, Tier.Master, new[] { Blow.RisingSlash, Blow.Cleave }, new[] { Step.None, Step.Hop }),
            new Tpl("spin_thrust", 0.9f, Tier.Master, new[] { Blow.SpinCut, Blow.Thrust }, new[] { Step.None, Step.Press }),
            new Tpl("thrust_round_spin", 0.9f, Tier.Master, new[] { Blow.Thrust, Blow.Slash, Blow.SpinCut }, new[] { Step.None, Step.Circle, Step.Press }),
            new Tpl("slash_rising", 1f, Tier.Master, new[] { Blow.Slash, Blow.RisingSlash }, new[] { Step.None, Step.Side }),
            new Tpl("blink_cuts", 1.1f, Tier.Grandmaster, new[] { Blow.Slash, Blow.Slash, Blow.Thrust }, new[] { Step.None, Step.Blink, Step.Blink }),
            new Tpl("cleave_round_spin", 0.8f, Tier.Grandmaster, new[] { Blow.Slash, Blow.Cleave, Blow.SpinCut }, new[] { Step.None, Step.Hop, Step.Circle })
        };

        private static readonly Tpl[] PoolUnarmed =
        {
            new Tpl("jab_cross_hook", 1.1f, Tier.Master, new[] { Blow.Jab, Blow.Cross, Blow.Hook }, new[] { Step.None, Step.None, Step.Press }),
            new Tpl("knee_elbow", 1f, Tier.Master, new[] { Blow.Knee, Blow.Elbow }, new[] { Step.None, Step.Press }),
            new Tpl("backfist_jab", 0.9f, Tier.Master, new[] { Blow.Backfist, Blow.Jab }, new[] { Step.None, Step.Side }),
            new Tpl("jab_sweep", 0.9f, Tier.Master, new[] { Blow.Jab, Blow.Sweep }, new[] { Step.None, Step.Side }),
            new Tpl("hook_hop_backfist", 1f, Tier.Master, new[] { Blow.Hook, Blow.Backfist }, new[] { Step.None, Step.Hop }),
            new Tpl("cross_round_hook_knee", 1f, Tier.Grandmaster, new[] { Blow.Cross, Blow.Hook, Blow.Knee }, new[] { Step.None, Step.Circle, Step.Press }),
            new Tpl("blink_barrage", 1.1f, Tier.God, new[] { Blow.Jab, Blow.Cross, Blow.Hook, Blow.Elbow }, new[] { Step.None, Step.Blink, Step.Blink, Step.Press })
        };

        private static readonly Tpl[] PoolBig =
        {
            new Tpl("legs_round_leap", 1f, Tier.Master, new[] { Blow.LegCut, Blow.LegCut, Blow.LeapStrike }, new[] { Step.None, Step.Circle, Step.Press }),
            new Tpl("sweep_leap", 1f, Tier.Master, new[] { Blow.Sweep, Blow.LeapStrike }, new[] { Step.None, Step.Side }),
            new Tpl("leg_side_leap", 0.9f, Tier.Master, new[] { Blow.LegCut, Blow.LeapStrike }, new[] { Step.None, Step.Side })
        };

        private static readonly Tpl[] PoolSmall =
        {
            new Tpl("jab_launch", 1f, Tier.Master, new[] { Blow.Jab, Blow.LaunchKick }, new[] { Step.None, Step.Press }),
            new Tpl("launch", 0.7f, Tier.Master, new[] { Blow.LaunchKick }, new[] { Step.None }),
            new Tpl("hook_side_launch", 0.9f, Tier.Master, new[] { Blow.Hook, Blow.LaunchKick }, new[] { Step.None, Step.Side })
        };

        private static readonly List<Tpl> _pick = new List<Tpl>();
        private static readonly List<float> _weights = new List<float>();
        private static readonly List<Blow> _blows = new List<Blow>();
        private static readonly List<Step> _steps = new List<Step>();

        /// <summary>A chain this young can still be answered by the other's own (the two meet).</summary>
        private const float JoinWindow = 0.3f;
        private const float TRecoil = 0.3f;
        private const float TWhiff = 0.26f;

        private const string SndPunch = "event:/SFX/OTHER/Punch";
        private const string SndHit = "event:/SFX/HIT/HitGeneric";
        private const string SndFlesh = "event:/SFX/HIT/HitFlesh";
        private const string SndSwords = "event:/SFX/HIT/HitSwordSword";
        private const string SndMetal = "event:/SFX/HIT/HitMetal";
        private const string SndWhoosh = "event:/SFX/UNIQUE/SpawnWhoosh";

        private static readonly Color White = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Steel = new Color(0.86f, 0.92f, 1f, 1f);

        // ---- the fighter
        private readonly Tier _tier;
        private readonly Palette _pal;
        private readonly bool _armed, _crab;
        private readonly float _k, _lk, _s, _ah;

        // ---- the enemy
        private Actor _v;
        private long _vid;
        private Weight _class;
        private float _vh;
        private bool _big, _small, _critter, _flow, _vGone;
        private float _stand;
        private Vector2 _vg;
        private int _lastSide;

        // ---- the chain
        private readonly List<Beat> _beats = new List<Beat>();
        private int _bi;
        private string _chain;
        private float _planned;

        // ---- the clock
        private Phase _phase = Phase.Travel;
        private float _pt, _hold;
        private bool _entered;
        private Shape _sh;
        private Step _stepNow;
        private Vector2 _from, _to, _spot;
        private float _tTravel, _tWind, _tStrike, _tHold;
        private float _rot0, _sx0 = 1f, _sy0 = 1f, _h0, _leapH, _apex;
        private float _th0, _th1, _cr;
        private bool _hit, _reached, _glintEnd, _faceBase, _landPuff;
        /// <summary>The enemy may still answer this chain with its own (the two meet in an exchange).</summary>
        private bool _joinable = true;
        /// <summary>It became an exchange (the other had its say: no turn is owed).</summary>
        private bool _exchanged;
        private float _turn, _ghostT, _contactAt, _leanSign = 1f;
        private int _misses, _retargets, _chases, _arcVar;
        private Vector2 _lastGround;

        // ---- the exchange (the other fighter is driven by this move)
        private FlowPartner _xp;
        private Actor _xb;
        private long _xbid;
        private Palette _palB;
        private float _kB, _ahB;
        private bool _armedB, _crabB;
        private Vector2 _xMid, _xAxis, _xA0, _xB0, _xA1, _xB1, _xLungeA, _xLungeB;
        private float _gapA, _gapB, _xMultA, _xMultB, _xSlideA, _xSlideB, _xShake, _xDust;
        private float _xRotB0, _xSxB0 = 1f, _xSyB0 = 1f;
        private int _xn, _xi, _xFinal;
        private int[] _xOut;
        private float[] _xH;
        private bool _xContact;

        private FlowStrike(Actor a, Actor v) : base(a)
        {
            Level = 3;
            _tier = Tiers.Of(a);
            _pal = Palette.For(_tier, 3);
            _armed = FlowStrikes.Armed(a);
            _crab = FlashStep.IsCrab(a);
            _k = K;
            _lk = Mathf.Lerp(1f, _k, 0.5f);
            _s = (_tier >= Tier.God ? 0.78f : _tier == Tier.Grandmaster ? 0.88f : 1f) * Random.Range(0.94f, 1.06f);
            _ah = FlowStrikes.SpriteH(a);
            _lastSide = a.flip ? -1 : 1;
            SetVictim(v);
            _lastSide = SideOf(Pose.Ground);
        }

        /// <summary>The enemy this chain is on (null once it is gone).</summary>
        public Actor Victim => _vGone ? null : _v;

        private float Fwd => Pose.FaceRight ? 1f : -1f;

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.35f);

        private bool Exchanging => _xp != null;

        /// <summary>Nothing can touch it mid-blink.</summary>
        public override bool Intangible => _phase == Phase.Travel && _stepNow == Step.Blink && _tTravel > 0f && Pose.Hidden;

        /// <summary>
        /// What the blow it throws now looks like to a reader (a jab Punch|Straight|High, a sweep
        /// Kick|Low|Spin, a cleave Blade|Overhead|Heavy...): always Fast, a Flurry when the chain is
        /// long, a blink between blows Teleport, a hop over the enemy Aerial; an exchange of two
        /// masters is a flurry of blows meeting blows.
        /// </summary>
        public override Form Forms
        {
            get
            {
                if (Exchanging)
                {
                    return (_armed ? Form.Blade : Form.Punch) | Form.Flurry | Form.Fast;
                }
                if (_beats.Count == 0)
                {
                    return Form.Fast | (_armed ? Form.Blade : Form.Punch);
                }
                Beat b = _beats[Mathf.Clamp(_bi, 0, _beats.Count - 1)];
                Form f = BlowForms(b.B, _armed) | Form.Fast;
                if (_beats.Count >= 3)
                {
                    f |= Form.Flurry;
                }
                if (b.S == Step.Blink)
                {
                    f |= Form.Teleport;
                }
                else if (b.S == Step.Hop)
                {
                    f |= Form.Aerial;
                }
                return f;
            }
        }

        /// <summary>The enemy the chain works over.</summary>
        public override Actor Aim => Victim;

        /// <summary>The kind of attack each blow of the pool is.</summary>
        internal static Form BlowForms(Blow b, bool armed)
        {
            switch (b)
            {
                case Blow.Jab: return Form.Punch | Form.Straight | Form.High;
                case Blow.Cross: return Form.Punch | Form.Straight | Form.High;
                case Blow.Hook: return Form.Punch | Form.Hook | Form.High;
                case Blow.Knee: return Form.Knee | Form.Body;
                case Blow.Elbow: return Form.Elbow | Form.High;
                case Blow.Backfist: return Form.Punch | Form.Spin | Form.High;
                case Blow.Sweep: return Form.Kick | Form.Low | Form.Spin;
                case Blow.LaunchKick: return Form.Kick | Form.Rising;
                case Blow.Slash: return Form.Blade;
                case Blow.RisingSlash: return Form.Blade | Form.Rising;
                case Blow.Thrust: return Form.Blade | Form.Straight;
                case Blow.SpinCut: return Form.Blade | Form.Spin;
                case Blow.Cleave: return Form.Blade | Form.Overhead | Form.Heavy;
                case Blow.LegCut: return Form.Blade | Form.Low;
                case Blow.LegKick: return Form.Kick | Form.Low;
                default: return Form.Aerial | Form.Overhead | Form.Heavy | (armed ? Form.Blade : Form.Punch);
            }
        }

        /// <summary>Everything a chain from the pool shows (for the fighter's taste and its memory of the enemy).</summary>
        private static Form ChainForms(Tpl t, bool armed)
        {
            Form f = Form.Fast;
            for (int i = 0; i < t.B.Length; i++)
            {
                f |= BlowForms(t.B[i], armed);
            }
            for (int i = 0; i < t.S.Length; i++)
            {
                if (t.S[i] == Step.Blink)
                {
                    f |= Form.Teleport;
                }
                else if (t.S[i] == Step.Hop)
                {
                    f |= Form.Aerial;
                }
                else if (t.S[i] == Step.Circle || t.S[i] == Step.Side)
                {
                    f |= Form.Evasive;
                }
            }
            return f;
        }

        // -------------------------------------------------------------- start --

        /// <summary>
        /// Starts a chain (or an exchange) of 'a' on 'v'. 'meet': it owes this master its turn, so it
        /// meets it in an exchange whenever one can be played. 'room': the seconds (move time) it
        /// has before something else takes it (a duel's next bout): the chain is cut to fit, down to
        /// a single blow. 'unreachable': false was returned because no blow can get there (water, a
        /// wall in the way): the caller answers otherwise.
        /// </summary>
        public static bool Begin(Actor a, Actor v, FlowStrikes.Mem mem, bool meet, float room, out bool unreachable)
        {
            unreachable = false;
            if (a == null || v == null || !Moves.CanPerform(a) || !v.isAlive() || Moves.Busy(a))
            {
                return false;
            }
            FlowStrike m = new FlowStrike(a, v);
            if (!m.Compose(mem, room))
            {
                return false;
            }
            bool exchange = m.WantsExchange(mem, meet);
            bool reaches = m.FirstReach();
            if (!exchange && !reaches)
            {
                unreachable = true;   // its blows would only cut the air: never a chain of whiffs
                return false;
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            FlowStrikes.Live++;
            if (mem != null)
            {
                mem.ChainStart = Fighter.Now;
                mem.LastVictim = -1;
            }
            if (exchange)
            {
                m.OpenExchange(false, mem);
            }
            if (!m.Exchanging && m._phase == Phase.Travel)
            {
                if (!reaches)
                {
                    m.Lose();   // the exchange couldn't be played and no blow gets there: a pose and a glint
                }
                else if (!m._flow)
                {
                    // the enemy is worked over: no swings of its own while the chain lasts (a master
                    // about to answer it keeps its hands free for the first moments)
                    Pin(v, m._planned + 0.05f);
                }
            }
            if (Dev.On) Dev.Trace("flow " + m.Id + " " + (m.Exchanging ? "exchange with " : m._chain + " on ") + v.asset.id + "#" + v.Sid() + " (" + m._beats.Count + " blows, " + m._planned.ToString("F2") + " s)");
            return true;
        }

        private void SetVictim(Actor v)
        {
            _v = v;
            _vid = v.Sid();
            _vGone = false;
            _class = Weights.Of(v);
            _vg = v.current_position;
            _vh = FlowStrikes.SpriteH(v);
            float ratio = Sizes(A, _ah, v, _vh, out _big, out _small, out _critter);
            _flow = FlowStrikes.IsFlow(v);
            _stand = StandOf(_armed, ratio, _k);
            _reached = false;
        }

        /// <summary>How 'v' measures up against 'a' (sprite heights, species): much bigger, smaller, a critter. Returns v's height over a's.</summary>
        private static float Sizes(Actor a, float ah, Actor v, float vh, out bool big, out bool small, out bool critter)
        {
            float ratio = vh / Mathf.Max(0.05f, ah);
            Weight species = Weights.Species(v);
            critter = species == Weight.Critter;
            big = ratio > 1.35f || species >= Weight.Brute && species > Weights.Species(a);
            small = !big && (critter || ratio < 0.75f);
            return ratio;
        }

        /// <summary>
        /// No swings of its own while the chain works it over (Combat.Pin) for 'seconds' of move
        /// time (the pin runs in game time: at high speed the chain takes longer in it, FlowStrikes.Speed).
        /// Never a unit the player controls: its attacks are the player's, and a master's chains
        /// would lock them out.
        /// </summary>
        private static void Pin(Actor v, float seconds)
        {
            if (v != null && !ControllableUnit.isControllingUnit(v))
            {
                Combat.Pin(v, seconds * FlowStrikes.Speed());
            }
        }

        /// <summary>A blow's length apart (a blade's a little more): the two bodies read as two.</summary>
        private static float StandOf(bool armed, float ratio, float k)
        {
            return ((armed ? 0.66f : 0.58f) + 0.3f * Mathf.Max(0f, ratio - 1f)) * k;
        }

        /// <summary>
        /// Can the first blow get there? From where it would strike (picked the way Travel picks it)
        /// the enemy must be in reach, and that spot no further than a dash.
        /// </summary>
        private bool FirstReach()
        {
            Step st = Step.None;
            Vector2 to = Resolve(ref st, _beats[0]);
            float reach = _stand + 0.55f * _k + 0.25f;
            return Vector2.Distance(to, _vg) <= reach && Vector2.Distance(Pose.Ground, to) <= 3.6f * _k;
        }

        // ------------------------------------------------------------ compose --

        /// <summary>Picks this chain from the pool, fits it into 'room' seconds, and plans its sides, depths and damage.</summary>
        private bool Compose(FlowStrikes.Mem mem, float room)
        {
            Tpl[] pool = _big ? PoolBig : _small ? PoolSmall : _armed ? PoolArmed : PoolUnarmed;
            _pick.Clear();
            _weights.Clear();
            float total = 0f;
            // its persona's taste picks its footwork and blows; what this enemy answered before is avoided
            Persona per = Personas.Of(A);
            FoeNotes notes = Fighters.TryGet(A, out Fighter self) ? self.Recall(_v) : null;
            foreach (Tpl t in pool)
            {
                if (t.Min > _tier)
                {
                    continue;
                }
                Form cf = ChainForms(t, _armed);
                float w = t.W * per.Affinity(cf);
                if (notes != null)
                {
                    w *= 1f - 0.4f * notes.BurnedOf(cf & ~(Form.Fast | Form.Evasive)) * per.Adapt;
                }
                w = Mathf.Max(0.02f, w);
                if (mem != null && t.Id == mem.LastChain)
                {
                    w *= 0.02f;   // never twice running (unless there's nothing else)
                }
                else if (mem != null && t.Id == mem.PrevChain)
                {
                    w *= 0.4f;
                }
                _pick.Add(t);
                _weights.Add(w);
                total += w;
            }
            if (_pick.Count == 0)
            {
                return false;
            }
            Tpl tpl = _pick[0];
            float roll = Random.value * total;
            for (int i = 0; i < _pick.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f)
                {
                    tpl = _pick[i];
                    break;
                }
            }
            _chain = tpl.Id;
            if (mem != null)
            {
                mem.PrevChain = mem.LastChain;
                mem.LastChain = tpl.Id;
            }

            _blows.Clear();
            _steps.Clear();
            for (int i = 0; i < tpl.B.Length; i++)
            {
                _blows.Add(Adapt(tpl.B[i]));
                Step st = tpl.S[i];
                if (_big && st == Step.Hop)
                {
                    st = Step.Circle;   // nobody vaults a bear
                }
                _steps.Add(st);
            }
            // the higher the tier, the more it fits in: Grandmasters and Gods add a blow (a God's blinks in)
            int cap = _tier >= Tier.God ? 4 : _tier == Tier.Grandmaster ? 4 : 3;
            if (!_small && _blows.Count < cap && _tier >= Tier.Grandmaster && Random.value < (_tier >= Tier.God ? 0.6f : 0.35f))
            {
                _blows.Insert(1, _armed && !_big ? Blow.Slash : _big ? (_armed ? Blow.LegCut : Blow.LegKick) : Blow.Jab);
                _steps.Insert(1, _tier >= Tier.God && !_big ? Step.Blink : Step.Side);
            }
            while (_blows.Count > cap)
            {
                _blows.RemoveAt(_blows.Count - 2);
                _steps.RemoveAt(_steps.Count - 2);
            }
            // a duel's bout comes for it soon: the chain ends on its own before (the finisher stays)
            float dash = Mathf.Max(0f, Vector2.Distance(Pose.Ground, _vg) - _stand);
            while (_blows.Count > 1 && Estimate(dash) > room)
            {
                _blows.RemoveAt(_blows.Count - 2);
                _steps.RemoveAt(_steps.Count - 2);
            }

            // depths: in front of / behind the enemy's line, changing with every side-step
            float depth = Mathf.Clamp((Pose.Ground.y - _vg.y) / Mathf.Max(0.2f, _stand), -0.5f, 0.5f);
            IReadOnlyList<Move> all = Moves.All;
            for (int i = 0; i < all.Count; i++)
            {
                // another master already working this enemy from the same side: it takes the other line
                if (all[i] is FlowStrike o && !o._vGone && o._v == _v && Mathf.Abs(o.Pose.Ground.x - _vg.x) > 0.05f
                    && (o.Pose.Ground.x > _vg.x ? 1 : -1) == _lastSide)
                {
                    depth = o.Pose.Ground.y >= _vg.y ? -0.5f : 0.5f;
                    break;
                }
            }
            _beats.Clear();
            for (int i = 0; i < _blows.Count; i++)
            {
                Step st = _steps[i];
                switch (st)
                {
                    case Step.Side:
                        depth = (depth > 0.05f ? -1f : depth < -0.05f ? 1f : Random.value < 0.5f ? -1f : 1f) * Random.Range(0.3f, 0.5f);
                        break;
                    case Step.Hop:
                    case Step.Circle:
                    case Step.Blink:
                        depth = Random.Range(-0.3f, 0.3f);
                        break;
                    default:
                        depth *= 0.6f;
                        break;
                }
                _beats.Add(new Beat { B = _blows[i], S = st, Depth = depth });
            }

            // timing and damage: what its swings would have dealt in this time
            float dur = 0f, wsum = 0f;
            float d0 = Vector2.Distance(Pose.Ground, Spot(_lastSide, _beats[0].Depth));
            for (int i = 0; i < _beats.Count; i++)
            {
                Beat b = _beats[i];
                b.Last = i == _beats.Count - 1;
                Shape sh = ShapeOf(b.B);
                dur += TravelTime(i == 0 ? (d0 > 0.15f ? Step.Press : Step.None) : b.S, i == 0 ? d0 : _stand)
                    + 0.03f * sh.TWind + 0.045f * sh.TStrike + (b.Last ? 0.09f : 0.04f);
                wsum += sh.Weight * (b.Last ? 1.5f : 1f);
                _beats[i] = b;
            }
            _planned = (dur + 0.07f) * _s;
            float mult = FlowStrikes.Total(A, _planned);
            for (int i = 0; i < _beats.Count; i++)
            {
                Beat b = _beats[i];
                b.Mult = mult * ShapeOf(b.B).Weight * (b.Last ? 1.5f : 1f) / Mathf.Max(0.1f, wsum);
                _beats[i] = b;
            }
            _bi = 0;
            return true;
        }

        /// <summary>About how long the blows picked so far take (move time), 'd0' the dash into reach.</summary>
        private float Estimate(float d0)
        {
            float dur = 0f;
            for (int i = 0; i < _blows.Count; i++)
            {
                Shape sh = ShapeOf(_blows[i]);
                bool last = i == _blows.Count - 1;
                dur += TravelTime(i == 0 ? (d0 > 0.15f ? Step.Press : Step.None) : _steps[i], i == 0 ? d0 : _stand)
                    + 0.03f * sh.TWind + 0.045f * sh.TStrike + (last ? 0.09f : 0.04f);
            }
            return (dur + 0.07f) * _s;
        }

        /// <summary>The pool's blows, fitted to what the fighter holds.</summary>
        private Blow Adapt(Blow b)
        {
            if (_armed)
            {
                switch (b)
                {
                    case Blow.Jab: return Blow.Slash;
                    case Blow.Hook: return Blow.RisingSlash;
                    case Blow.LegKick: return Blow.LegCut;
                }
                return b;
            }
            switch (b)
            {
                case Blow.LegCut: return Blow.LegKick;
                case Blow.Slash: return Blow.Cross;
                case Blow.RisingSlash: return Blow.Hook;
                case Blow.Thrust: return Blow.Cross;
                case Blow.SpinCut: return Blow.Backfist;
                case Blow.Cleave: return Blow.Elbow;
            }
            return b;
        }

        private static Shape ShapeOf(Blow b)
        {
            Shape s = new Shape { WSX = 1f, WSY = 1f, HSX = 1f, HSY = 1f, TWind = 1f, TStrike = 1f, Weight = 1f, Contact = 0.6f };
            switch (b)
            {
                case Blow.Jab:
                    S(ref s, -6f, 0.95f, 1.04f, 0f, 0.03f, 14f, 1.18f, 0.9f, 0f, 0.1f);
                    s.Contact = 0.64f; s.Touch = Touch.Punch; s.Weight = 0.7f; s.TWind = 0.8f; s.TStrike = 0.8f;
                    break;
                case Blow.Cross:
                    S(ref s, -12f, 0.9f, 1.06f, 0f, 0.05f, 20f, 1.24f, 0.86f, 0f, 0.16f);
                    s.Contact = 0.62f; s.Touch = Touch.Punch;
                    break;
                case Blow.Hook:
                    S(ref s, -10f, 0.92f, 1.04f, 0f, 0.03f, 16f, 1.16f, 0.9f, 0f, 0.1f);
                    Arc(ref s, 7, 0.45f, 215f, 365f, 2, new Vector2(2f, 1f));
                    s.Contact = 0.64f; s.Touch = Touch.Punch;
                    break;
                case Blow.Knee:
                    S(ref s, -6f, 1.08f, 0.88f, 0f, 0.02f, -8f, 0.9f, 1.16f, 0.16f, 0.12f);
                    Arc(ref s, 5, 0.9f, -70f, 60f, 2, new Vector2(2f, -2f));
                    s.Contact = 0.42f; s.Touch = Touch.Kick;
                    break;
                case Blow.Elbow:
                    S(ref s, -14f, 0.92f, 1.05f, 0f, 0.02f, 24f, 1.12f, 0.9f, 0f, 0.08f);
                    Arc(ref s, 5, 0.8f, 125f, -25f, 2, new Vector2(1f, 2f));
                    s.Contact = 0.72f; s.Touch = Touch.Punch; s.Weight = 1.1f; s.TWind = 0.9f; s.TStrike = 0.9f;
                    break;
                case Blow.Backfist:
                    S(ref s, -4f, 1f, 1f, 0f, 0f, 12f, 1.2f, 0.9f, 0f, 0.12f);
                    Arc(ref s, 8, 0.42f, 200f, 380f, 2, new Vector2(0f, 1f));
                    s.Spin = true; s.Contact = 0.68f; s.Touch = Touch.Punch; s.Weight = 1.2f; s.TStrike = 1.6f;
                    break;
                case Blow.Sweep:
                    S(ref s, -8f, 1.1f, 0.86f, 0f, 0.02f, 30f, 1.32f, 0.7f, 0f, 0.12f);
                    Arc(ref s, 9, 0.32f, 175f, 365f, 2, new Vector2(1f, -99f));   // -99: at its feet
                    s.Contact = 0.12f; s.Touch = Touch.Kick; s.Weight = 1.1f; s.Heavy = true; s.TWind = 1.1f; s.TStrike = 1.2f;
                    break;
                case Blow.LaunchKick:
                    S(ref s, -10f, 1.1f, 0.85f, 0f, 0.03f, -26f, 0.86f, 1.25f, 0.2f, 0.1f);
                    Arc(ref s, 9, 1.1f, -80f, 85f, 3, new Vector2(2f, 0f));
                    s.Contact = 0.36f; s.Touch = Touch.Kick; s.Weight = 1.4f; s.Heavy = true; s.TWind = 1.3f; s.TStrike = 1.2f;
                    break;
                case Blow.Slash:
                    S(ref s, -14f, 0.92f, 1.05f, 0f, 0.03f, 22f, 1.2f, 0.88f, 0f, 0.14f);
                    Arc(ref s, 9, 0.75f, 75f, -55f, 3, new Vector2(2f, 1f));
                    s.Contact = 0.6f; s.Touch = Touch.Cut; s.SlashAngle = -35f;
                    break;
                case Blow.RisingSlash:
                    S(ref s, -6f, 1.1f, 0.86f, 0f, 0.02f, -18f, 0.88f, 1.2f, 0.14f, 0.1f);
                    Arc(ref s, 10, 1f, -70f, 110f, 3, new Vector2(1f, -1f));
                    s.Contact = 0.55f; s.Touch = Touch.Cut; s.Weight = 1.1f; s.TStrike = 1.1f; s.SlashAngle = 55f;
                    break;
                case Blow.Thrust:
                    S(ref s, -10f, 0.88f, 1.06f, 0f, 0.07f, 10f, 1.3f, 0.84f, 0f, 0.22f);
                    s.Contact = 0.55f; s.Touch = Touch.Cut; s.Weight = 1.1f; s.TWind = 1.2f; s.SlashAngle = 0f;
                    break;
                case Blow.SpinCut:
                    S(ref s, 0f, 1f, 1f, 0f, 0f, 8f, 1.16f, 0.9f, 0f, 0.06f);
                    Arc(ref s, 10, 0.42f, 0f, 360f, 2, Vector2.zero);
                    s.Spin = true; s.Contact = 0.5f; s.Touch = Touch.Cut; s.Weight = 1.3f; s.Heavy = true; s.TWind = 0.8f; s.TStrike = 1.6f; s.SlashAngle = 6f;
                    break;
                case Blow.Cleave:
                    S(ref s, -20f, 0.9f, 1.12f, 0.18f, 0f, 34f, 1.12f, 0.86f, 0f, 0.06f);
                    Arc(ref s, 10, 1f, 100f, -70f, 3, new Vector2(2f, 2f));
                    s.Contact = 0.62f; s.Touch = Touch.Cut; s.Weight = 1.4f; s.Heavy = true; s.TWind = 1.2f; s.TStrike = 1.1f; s.SlashAngle = -70f;
                    break;
                case Blow.LegCut:
                    S(ref s, -10f, 1.05f, 0.9f, 0f, 0.03f, 26f, 1.26f, 0.78f, 0f, 0.12f);
                    Arc(ref s, 8, 0.5f, 200f, 350f, 2, new Vector2(2f, -3f));
                    s.Contact = 0.16f; s.Touch = Touch.Cut; s.Weight = 0.9f; s.SlashAngle = -10f;
                    break;
                case Blow.LegKick:
                    S(ref s, -8f, 1.06f, 0.9f, 0f, 0.02f, 24f, 1.24f, 0.8f, 0f, 0.12f);
                    Arc(ref s, 7, 0.5f, 195f, 345f, 2, new Vector2(2f, -3f));
                    s.Contact = 0.16f; s.Touch = Touch.Kick; s.Weight = 0.9f;
                    break;
                case Blow.LeapStrike:
                    S(ref s, -8f, 1.12f, 0.82f, 0f, 0.02f, 30f, 1.1f, 0.9f, 0f, 0.1f);
                    Arc(ref s, 11, 1f, 100f, -60f, 3, new Vector2(2f, 2f));
                    s.Contact = 0.7f; s.Touch = Touch.Cut; s.Weight = 1.6f; s.Heavy = true; s.TWind = 1.3f; s.TStrike = 2.2f; s.SlashAngle = -60f;
                    break;
            }
            return s;
        }

        private static void S(ref Shape s, float wr, float wsx, float wsy, float wh, float wb, float hr, float hsx, float hsy, float hh, float hl)
        {
            s.WRot = wr;
            s.WSX = wsx;
            s.WSY = wsy;
            s.WH = wh;
            s.WBack = wb;
            s.HRot = hr;
            s.HSX = hsx;
            s.HSY = hsy;
            s.HH = hh;
            s.HLunge = hl;
        }

        private static void Arc(ref Shape s, int r, float ratio, float a0, float a1, int thick, Vector2 off)
        {
            s.ArcR = r;
            s.ArcRatio = ratio;
            s.A0 = a0;
            s.A1 = a1;
            s.ArcThick = thick;
            s.ArcOff = off;
        }

        private static float TravelTime(Step st, float d)
        {
            switch (st)
            {
                case Step.Press: return Mathf.Clamp(0.035f + 0.045f * d, 0.045f, 0.14f);
                case Step.Side: return Mathf.Clamp(0.05f + 0.03f * d, 0.06f, 0.1f);
                case Step.Hop: return 0.12f;
                case Step.Circle: return 0.11f;
                case Step.Blink: return 0.065f;
                default: return 0f;
            }
        }

        // ------------------------------------------------------------ helpers --

        /// <summary>Which side of the enemy a point is on (+1 right, -1 left); straight above or below keeps the last.</summary>
        private int SideOf(Vector2 p)
        {
            float dx = p.x - _vg.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                _lastSide = dx > 0f ? 1 : -1;
            }
            return _lastSide;
        }

        /// <summary>Where it strikes from: 'side' of the enemy, 'depth' in front of (-) or behind (+) its line.</summary>
        private Vector2 Spot(int side, float depth)
        {
            return Combat.Clamp(_vg + new Vector2(side * _stand, depth * _stand));
        }

        private Vector2 Dir()
        {
            Vector2 d = _vg - Pose.Ground;
            return d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Fwd, 0f);
        }

        private void Face()
        {
            float dx = _vg.x - Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.04f)
            {
                Pose.FaceRight = dx > 0f;
            }
        }

        /// <summary>A point on the enemy's body, 'frac' of the way up.</summary>
        private Vector2 VBody(float frac)
        {
            if (_vGone || _v == null || _v.Sid() != _vid)
            {
                return BodyPos() + Dir() * _stand;
            }
            return new Vector2(_vg.x, _vg.y + Mathf.Max(0f, _v.position_height) + _vh * frac);
        }

        private Vector2 Hand()
        {
            return BodyPos() + new Vector2(Fwd * (_armed ? 4f : 2.5f), _armed ? 1f : -0.5f) * Px;
        }

        private void Next(Phase p)
        {
            _phase = p;
            _pt = 0f;
            _entered = false;
        }

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        private static float Rot0(float r)
        {
            return Mathf.DeltaAngle(0f, r);
        }

        /// <summary>One pixel of tremble, left and right.</summary>
        private Vector2 Tremble(float t)
        {
            return new Vector2(((int)(t * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (Exchanging)
            {
                TickExchange(dt);
            }
            else
            {
                TickChain(dt);
            }
            if (!Finished && T > 2.6f)
            {
                // never hang on to the unit, whatever happened
                Neutral();
                Finished = true;
            }
        }

        private void TickChain(float dt)
        {
            if (_hold > 0f)
            {
                // the crit's beat: the pose holds, trembling with the blow
                _hold -= dt;
                Pose.Nudge = Tremble(_hold);
                return;
            }
            CheckVictim();
            if (Finished)
            {
                return;
            }
            _pt += dt;
            _turn = Mathf.MoveTowards(_turn, 0f, dt / 0.06f);
            switch (_phase)
            {
                case Phase.Travel:
                    Travel(dt);
                    break;
                case Phase.Wind:
                    Wind();
                    break;
                case Phase.Strike:
                    Strike();
                    break;
                case Phase.Hold:
                    HoldPose();
                    break;
                case Phase.Recoil:
                    Recoil();
                    break;
                case Phase.Whiff:
                    Whiff();
                    break;
                default:
                    Recover();
                    break;
            }
        }

        /// <summary>
        /// Is the enemy still ours? Killed by someone else, picked up by the god hand, recycled,
        /// gone inside, or grabbed into someone else's move: another one takes its place, or the
        /// chain ends on a pose.
        /// </summary>
        private void CheckVictim()
        {
            if (_vGone || _v == null)
            {
                return;
            }
            // (a unit a blow has just brought to nothing is gone too: it only falls at the game's next death check)
            bool ok = Combat.Living(_v) && _v.Sid() == _vid && !_v.isInMagnet() && !_v.isInsideSomething();
            if (ok && Moves.TryGet(_v, out Move vm) && (vm is Puppet || vm is Duelist || vm is FlowPartner))
            {
                ok = false;   // someone else has it in hand
            }
            if (ok)
            {
                _vg = _v.current_position;
                return;
            }
            if (EndTarget == _v)
            {
                EndLink = Link.None;
                EndTarget = null;
            }
            bool landed = _phase == Phase.Hold || _phase == Phase.Strike && _hit;
            if (landed && _bi >= _beats.Count - 1)
            {
                _vGone = true;   // the last blow is in (it may have been the end of it): its follow-through plays out
                return;
            }
            bool mid = landed || _phase == Phase.Travel || _phase == Phase.Wind || _phase == Phase.Strike;
            if (mid && _bi < _beats.Count && Retarget())
            {
                if (!landed)
                {
                    Next(Phase.Travel);   // this blow goes to the new one
                }
                return;
            }
            _vGone = true;
            if (mid)
            {
                Lose();
            }
        }

        /// <summary>Its enemy is gone: the nearest other within 2 tiles, of the same class or lighter.</summary>
        private bool Retarget()
        {
            if (_retargets >= 1)
            {
                return false;
            }
            Actor best = null;
            float bd = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, _vg, 2f))
            {
                if (u == _v || !Combat.Living(u) || Weights.Of(u) > _class || !HundredFists.Hopping(u) || Director.InScene(u))
                {
                    continue;
                }
                float d = (u.current_position - Pose.Ground).sqrMagnitude;
                if (d < bd && Combat.ClearPath(Pose.Ground, u.current_position))
                {
                    bd = d;
                    best = u;
                }
            }
            if (best == null)
            {
                return false;
            }
            _retargets++;
            SetVictim(best);
            if (!_flow)
            {
                Pin(best, Mathf.Max(0.1f, _planned - T) + 0.05f);
            }
            if (Dev.On) Dev.Trace("flow " + Id + " turns on " + best.asset.id + "#" + best.Sid());
            return true;
        }

        /// <summary>Nothing left to hit: a pose and a glint.</summary>
        private void Lose()
        {
            _glintEnd = true;
            EndLink = Link.None;
            EndTarget = null;
            Next(Phase.Recover);
        }

        private void Neutral()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (!Combat.SafeSpot(Pose.Ground) && !Wading(Pose.Ground) && Combat.FindLanding(Pose.Ground, 2f, out Vector2 spot))
            {
                Pose.Ground = spot;
            }
        }

        /// <summary>
        /// The shallows a master fights in (Moves.Wades): a chain that played there ends standing
        /// there, never popped ashore. (Every spot a chain moves to is safe ground: only one that
        /// started in the water ends in it.)
        /// </summary>
        internal static bool Wading(Vector2 p)
        {
            WorldTile t = Combat.TileAt(p);
            return t != null && t.Type != null && !t.Type.lava && !t.Type.block && t.Type.id != null && t.Type.id.Contains("shallow");
        }

        // ------------------------------------------------------------- travel --

        private void Travel(float dt)
        {
            Beat b = _beats[_bi];
            if (Enter())
            {
                _from = Pose.Ground;
                _stepNow = _bi == 0 ? Step.None : b.S;
                _to = Resolve(ref _stepNow, b);
                if (_stepNow == Step.Circle && !PlanCircle())
                {
                    // water, lava or rock beside it both ways round: a side-step instead
                    _stepNow = Step.Side;
                    _to = Resolve(ref _stepNow, b);
                }
                float d = Vector2.Distance(_from, _to);
                if (_stepNow == Step.None)
                {
                    if (d > 0.15f * _k)
                    {
                        _stepNow = Step.Press;   // into reach (or after it, if it was knocked back)
                    }
                    else
                    {
                        _to = _from;   // already there: this blow follows straight on from the last
                        d = 0f;
                    }
                }
                if (d > (_bi == 0 ? 3.6f : 2.6f) * _k)
                {
                    Lose();   // it got away
                    return;
                }
                if (d > 0.9f * _k && _stepNow == Step.Press && _bi > 0)
                {
                    if (_chases >= 1)
                    {
                        Lose();
                        return;
                    }
                    _chases++;   // it slipped back: once, it is followed
                }
                _tTravel = _stepNow == Step.None || d < 0.02f && _stepNow != Step.Blink ? 0f : TravelTime(_stepNow, d) * _s;
                _rot0 = Rot0(Pose.Rot);
                _h0 = Pose.Height;
                _lastGround = _from;
                // stepping in leans into it; stepping back out of a lunge leans back
                _leanSign = Vector2.Dot(_to - _from, _vg - _from) >= 0f ? 1f : -0.6f;
                if (_tTravel > 0f)
                {
                    StartTravel(d);
                }
            }
            float k = _tTravel > 0f ? Mathf.Clamp01(_pt / _tTravel) : 1f;
            bool vis = Visible;
            switch (_stepNow)
            {
                case Step.Hop:
                    {
                        Pose.Ground = Vector2.Lerp(_from, _to, Anim.InOutSine(k));
                        Pose.Height = Mathf.Lerp(_h0, 0f, k) + _apex * 4f * k * (1f - k);
                        Pose.Rot = _crab ? 0f : 360f * Anim.Smooth(0.08f, 0.92f, k);   // a forward flip over it
                        float tuck = Mathf.Sin(Mathf.PI * k);
                        Pose.SX = 1f - 0.08f * tuck;
                        Pose.SY = 1f - 0.12f * tuck;
                        Ghosts(dt, vis, 0.02f, 0.14f);
                        break;
                    }
                case Step.Circle:
                    {
                        Vector2 g = CircleAt(k);
                        float vx = g.x - _lastGround.x;
                        if (!_crab && Mathf.Abs(vx) > 0.002f)
                        {
                            Pose.FaceRight = vx > 0f;   // it runs the way it goes, round the back
                        }
                        _lastGround = g;
                        Pose.Ground = g;
                        Pose.Height = Mathf.Lerp(_h0, 0f, k);
                        Pose.Rot = _crab ? 0f : 12f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.2f));
                        Pose.SX = 1.12f;
                        Pose.SY = 0.92f;
                        Ghosts(dt, vis, 0.018f, 0.12f);
                        break;
                    }
                case Step.Blink:
                    {
                        Pose.Ground = Vector2.Lerp(_from, _to, k);
                        Pose.Height = 0f;
                        Pose.Hidden = k < 1f;
                        Pose.HideShadow = k < 1f;
                        Pose.Rot = 0f;
                        Pose.SX = Pose.SY = 1f;
                        break;
                    }
                case Step.None:
                    break;
                default:
                    {
                        // a press or a side-step: a quick slide, leaning into it
                        Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutQuad(k));
                        float lean = Mathf.Sin(Mathf.PI * k);
                        Pose.Height = Mathf.Lerp(_h0, 0f, Mathf.Clamp01(k * 2f));
                        Face();
                        Pose.Rot = _crab ? 0f : Mathf.Lerp(_rot0, 0f, Mathf.Clamp01(k * 3f)) + (_stepNow == Step.Press ? 12f : 5f) * lean * _leanSign;
                        Pose.SX = 1f + (_stepNow == Step.Press ? 0.14f : 0.1f) * lean;
                        Pose.SY = 1f - 0.08f * lean;
                        Ghosts(dt, vis, 0.026f, 0.1f);
                        break;
                    }
            }
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Pose.Ground = _to;
                Pose.Height = 0f;
                Pose.Hidden = false;
                Pose.HideShadow = false;
                if (_tTravel > 0f)
                {
                    ArriveFx();
                }
                Next(Phase.Wind);
            }
        }

        /// <summary>
        /// Where this blow is struck from, and how it gets there (a flip to the far side that would
        /// land in the water strikes from this side instead; a blocked step stays put).
        /// </summary>
        private Vector2 Resolve(ref Step st, Beat b)
        {
            int cur = SideOf(Pose.Ground);
            bool flips = st == Step.Hop || st == Step.Circle || st == Step.Blink;
            int side = flips ? -cur : cur;
            Vector2 want = Spot(side, b.Depth);
            if (Reachable(want, st))
            {
                return want;
            }
            want = Spot(side, 0f);
            if (Reachable(want, st))
            {
                return want;
            }
            if (flips)
            {
                st = Step.Side;
                want = Spot(cur, -b.Depth);
                if (Reachable(want, st))
                {
                    return want;
                }
            }
            // straight in at it, or strike from where it stands
            Vector2 straight = Combat.Clamp(_vg - Dir() * _stand);
            if (Reachable(straight, Step.Press))
            {
                st = Step.Press;
                return straight;
            }
            st = Step.None;
            return Pose.Ground;
        }

        private bool Reachable(Vector2 p, Step st)
        {
            if (!Combat.SafeSpot(p))
            {
                return false;
            }
            switch (st)
            {
                case Step.Hop:
                case Step.Blink:
                    return true;   // over it, or not there at all on the way
                case Step.Circle:
                    {
                        Vector2 back = _vg + new Vector2(0f, _stand * Art.Ground);
                        return Combat.SafeSpot(back) || Combat.SafeSpot(_vg - new Vector2(0f, _stand * Art.Ground));
                    }
                default:
                    return Combat.ClearPath(Pose.Ground, p);
            }
        }

        private void StartTravel(float d)
        {
            float px = Px;
            bool vis = Visible;
            switch (_stepNow)
            {
                case Step.Hop:
                    _apex = 0.22f * _k + _vh * 0.6f;
                    Pose.FaceRight = _to.x > _from.x;   // it faces the way it flies: it turns on landing
                    FlowStrikes.Sound(SndWhoosh, A.current_tile);
                    if (vis)
                    {
                        FlashStep.HeelDust(_from, _from - _to, px, A.current_tile, _k);
                    }
                    break;
                case Step.Circle:
                    // (its way round was picked with the step: PlanCircle)
                    FlowStrikes.Sound(SndWhoosh, A.current_tile);
                    if (vis)
                    {
                        FlashStep.HeelDust(_from, _from - _vg, px, A.current_tile, _k);
                    }
                    break;
                case Step.Blink:
                    FlowStrikes.Sound(SndWhoosh, A.current_tile);
                    if (vis)
                    {
                        // a white flash where it stood, an afterimage left behind, speed lines on the way
                        Particle flash = Fx.Afterimage(this, 0.07f, new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
                        if (flash != null)
                        {
                            flash.R.sortingOrder += 3;
                        }
                        Fx.Afterimage(this, 0.24f, Fx.WithA(_pal.Ghost, 0.75f), _pal.GhostEnd);
                        Vector2 dir = (_to - _from).sqrMagnitude > 1e-4f ? (_to - _from).normalized : new Vector2(Fwd, 0f);
                        Kit.SpeedLines(Vector2.Lerp(_from, _to, 0.5f) + new Vector2(0f, Pose.PivotUp * A.actor_scale), dir, px, 4, 4f, 0.12f, Fx.WithA(_pal.Edge, 0.85f));
                    }
                    break;
                case Step.Side:
                case Step.Press:
                    if (vis && d > 0.3f)
                    {
                        FlashStep.HeelDust(_from, _from - _to, px, A.current_tile, _k);
                    }
                    break;
            }
        }

        /// <summary>
        /// The run round the enemy (from _from to _to): round its back (up the screen) most of the
        /// time, round its front now and then, whichever way has ground to run on all along (water,
        /// lava or rock that way: the other way round). False if neither way is clear.
        /// </summary>
        private bool PlanCircle()
        {
            _cr = Mathf.Max(_stand, Vector2.Distance(_from, _vg));
            Vector2 a0 = _from - _vg, a1 = _to - _vg;
            _th0 = Mathf.Atan2(a0.y / Art.Ground, a0.x);
            float th1 = Mathf.Atan2(a1.y / Art.Ground, a1.x);
            float delta = Mathf.DeltaAngle(_th0 * Mathf.Rad2Deg, th1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            bool back = Random.value < 0.7f;
            float mid = _th0 + delta * 0.5f;
            if (Mathf.Sin(mid) > 0f != back)
            {
                delta += delta > 0f ? -Mathf.PI * 2f : Mathf.PI * 2f;
            }
            for (int way = 0; way < 2; way++)
            {
                _th1 = _th0 + delta;
                bool clear = true;
                for (int i = 1; i <= 8 && clear; i++)
                {
                    clear = Combat.Passable(CircleAt(i / 9f));
                }
                if (clear)
                {
                    return true;
                }
                delta += delta > 0f ? -Mathf.PI * 2f : Mathf.PI * 2f;   // the other way round
            }
            return false;
        }

        /// <summary>Where the run round the enemy is at 'k' (0..1): eased onto the ring from where it stood, and off it onto its spot.</summary>
        private Vector2 CircleAt(float k)
        {
            float th = Mathf.Lerp(_th0, _th1, Anim.InOutSine(k));
            Vector2 p = _vg + new Vector2(Mathf.Cos(th), Mathf.Sin(th) * Art.Ground) * _cr;
            return Vector2.Lerp(Vector2.Lerp(_from, p, Anim.Smooth(0f, 0.25f, k)), _to, Anim.Smooth(0.7f, 1f, k));
        }

        private void ArriveFx()
        {
            if (!Visible)
            {
                return;
            }
            float px = Px;
            switch (_stepNow)
            {
                case Step.Hop:
                    Fx.LandingPuff(Pose.Ground, px, A.current_tile);
                    break;
                case Step.Blink:
                    {
                        Vector2 dir = (_to - _from).sqrMagnitude > 1e-4f ? (_to - _from).normalized : new Vector2(Fwd, 0f);
                        Fx.Ring(Pose.Ground, px, 0.18f, 2f, 8f * _k, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f, Fx.WithA(_pal.Edge, 0.85f), Fx.WithA(_pal.Edge, 0f));
                        Pose keep = Pose;
                        Pose.Ground -= dir * 0.3f * _k;
                        Fx.Afterimage(this, 0.12f, Fx.WithA(_pal.Core, 0.8f), Fx.WithA(_pal.Edge, 0.5f), _pal.GhostEnd);
                        Pose = keep;
                        break;
                    }
                case Step.Circle:
                    FlashStep.HeelDust(Pose.Ground, Pose.Ground - _vg, px, A.current_tile, _k);
                    break;
            }
        }

        private void Ghosts(float dt, bool vis, float every, float life)
        {
            if (!vis)
            {
                return;
            }
            _ghostT -= dt;
            if (_ghostT <= 0f)
            {
                _ghostT = every;
                Fx.Afterimage(this, life, _pal.Ghost, _pal.GhostEnd);
            }
        }

        // --------------------------------------------------------------- blow --

        private void Wind()
        {
            Beat b = _beats[_bi];
            if (Enter())
            {
                _sh = ShapeOf(b.B);
                bool was = Pose.FaceRight;
                Face();
                if (was != Pose.FaceRight)
                {
                    _turn = 1f;   // it turns round to it: a squash in the turn
                }
                _faceBase = Pose.FaceRight;
                _spot = Pose.Ground;
                _rot0 = _crab ? 0f : Rot0(Pose.Rot);
                _sx0 = Pose.SX;
                _sy0 = Pose.SY;
                _h0 = Pose.Height;
                _tWind = 0.03f * _sh.TWind * _s;
                _tStrike = 0.045f * _sh.TStrike * _s;
                _tHold = (b.Last ? 0.09f : 0.04f) * _s;
                _contactAt = _sh.Spin ? 0.72f : 0.55f;
                _hit = false;
                _landPuff = false;
                _arcVar = Random.value < 0.5f ? 0 : 1;
                if (_sh.Heavy && b.Last && _tier >= Tier.Grandmaster && Visible)
                {
                    Fx.Glint(Hand() + new Vector2(0f, 1.5f * Px), Px, 0.14f, _pal.Core, 0.8f);   // the "ting" before it
                }
            }
            float k = Mathf.Clamp01(_pt / _tWind);
            float e = Anim.OutQuad(k);
            Vector2 back = _spot - Dir() * _sh.WBack * _k;
            Pose.Ground = Combat.SafeSpot(back) ? Vector2.Lerp(_spot, back, e) : _spot;
            Pose.Rot = _crab ? 0f : Mathf.Lerp(_rot0, _sh.WRot, e);
            Pose.SX = Mathf.Lerp(_sx0, _sh.WSX, e) * (1f - 0.35f * _turn);
            Pose.SY = Mathf.Lerp(_sy0, _sh.WSY, e);
            Pose.Height = Mathf.Lerp(_h0, _sh.WH * _k, e);
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Next(Phase.Strike);
            }
        }

        private void Strike()
        {
            Beat b = _beats[_bi];
            if (Enter())
            {
                _from = Pose.Ground;
                Vector2 d = Dir();
                float room = Mathf.Max(0f, Vector2.Distance(_from, _vg) - 0.3f * _k);
                float lunge = Mathf.Min(_sh.HLunge * _k, room);
                _to = _from + d * lunge;
                if (!Combat.SafeSpot(_to))
                {
                    _to = _from;
                }
                _leapH = b.B == Blow.LeapStrike ? Mathf.Max(0.35f * _k, _vh * 0.55f) : _sh.HH * _k;
                if (Visible)
                {
                    StrikeFx(b);
                }
                if (_sh.Heavy || _sh.Spin)
                {
                    FlowStrikes.Sound(SndWhoosh, A.current_tile, 0.05f);
                }
            }
            float k = Mathf.Clamp01(_pt / _tStrike);
            float e = Anim.OutCubic(Mathf.Clamp01(k / _contactAt));
            Pose.Ground = Vector2.Lerp(_from, _to, e);
            Pose.Rot = _crab ? 0f : Mathf.Lerp(_sh.WRot, _sh.HRot, e);
            Pose.SX = Mathf.Lerp(_sh.WSX, _sh.HSX, e);
            Pose.SY = Mathf.Lerp(_sh.WSY, _sh.HSY, e);
            Pose.Height = Mathf.Lerp(_sh.WH * _k, _leapH, b.B == Blow.LeapStrike ? Anim.OutQuad(Mathf.Clamp01(k / _contactAt)) : e);
            if (_sh.Spin)
            {
                // it turns right round on the spot: a 2D turn is the sprite narrowing, flipping, widening
                float t = Mathf.Clamp01(k / _contactAt);
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                Pose.SX *= Mathf.Max(0.2f, Mathf.Abs(c));
                Pose.FaceRight = t > 0.25f && t < 0.75f ? !_faceBase : _faceBase;
            }
            Pose.Nudge = Vector2.zero;
            if (!_hit && k >= _contactAt)
            {
                _hit = true;
                Pose.FaceRight = _faceBase;
                Land(b);
                if (_phase != Phase.Strike || Finished)
                {
                    return;
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Hold);
            }
        }

        private void HoldPose()
        {
            Beat b = _beats[_bi];
            float k = Mathf.Clamp01(_pt / _tHold);
            float relax = (b.Last ? 0.4f : 0.28f) * Anim.OutQuad(k);
            Pose.Rot = _crab ? 0f : _sh.HRot * (1f - relax);
            Pose.SX = Mathf.Lerp(_sh.HSX, 1f, relax);
            Pose.SY = Mathf.Lerp(_sh.HSY, 1f, relax);
            // whatever went up (a knee, a rising cut, a leap) comes down by the end of the hold
            Pose.Height = Mathf.Lerp(_leapH, 0f, Anim.InQuad(k));
            Pose.Nudge = Vector2.zero;
            if (_leapH > 0.2f * _k && !_landPuff && k >= 0.95f)
            {
                _landPuff = true;
                if (Visible)
                {
                    Fx.LandingPuff(Pose.Ground, Px, A.current_tile);
                }
            }
            if (k >= 1f)
            {
                Pose.Height = 0f;
                _bi++;
                Next(_bi >= _beats.Count ? Phase.Recover : Phase.Travel);
            }
        }

        /// <summary>The blow lands (or meets the enemy's answer).</summary>
        private void Land(Beat b)
        {
            if (_vGone || _v == null || !_v.isAlive() || _v.Sid() != _vid)
            {
                if (!b.Last && Retarget())
                {
                    return;   // the next blow goes to the new one
                }
                WhiffFx();
                Lose();
                return;
            }
            float reach = _stand + 0.55f * _k + 0.25f;
            if (Vector2.Distance(Pose.Ground, _v.current_position) > reach)
            {
                // it slipped out of reach: this one cuts the air, the next one follows it
                WhiffFx();
                if (++_misses >= 2)
                {
                    Lose();
                }
                return;
            }
            if (!_reached)
            {
                _reached = true;
                Contact answer = Combat.Reach(this, _v, false);
                if (answer == Sakuga.Contact.Parried)
                {
                    Next(Phase.Recoil);
                    return;
                }
                if (answer == Sakuga.Contact.Evaded)
                {
                    Next(Phase.Whiff);
                    return;
                }
            }
            float mult = b.Mult * FlowStrikes.Comp(A, _v);
            HitResult r = Combat.Hit(A, _v, mult, b.Last, true, 0f, this);
            if (!_flow || T > JoinWindow)
            {
                Pin(_v, Mathf.Max(0.08f, _planned - T) + 0.05f);
            }
            Vector2 at = VBody(_sh.Contact);
            if (Visible)
            {
                BlowFx(b, at, r);
            }
            if (_armed && _sh.Touch == Touch.Cut)
            {
                FlowStrikes.Sound(SndFlesh, _v.current_tile);
            }
            else
            {
                FlowStrikes.Sound(b.Last || _sh.Heavy ? SndHit : SndPunch, _v.current_tile);
            }
            if (b.Last)
            {
                Finisher(b, at, r);
            }
        }

        /// <summary>The last blow: the enemy is knocked back, sent up, or felled.</summary>
        private void Finisher(Beat b, Vector2 at, HitResult r)
        {
            if (r.Landed)
            {
                try
                {
                    A.attackTargetActions(_v, _v.current_tile);   // its weapon's and traits' own on-hit effects
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("flow on-hit", e);
                }
                A.addExperience(2);
            }
            if (r.Crit)
            {
                if (Visible)
                {
                    Kit.ImpactFrame(at, Px, 0.42f * _k, _pal, true);   // the Black Flash
                }
                _hold = 0.05f;
            }
            if (b.B == Blow.Sweep || b.B == Blow.Cleave || b.B == Blow.LeapStrike)
            {
                GroundBlow();
            }
            if (!_v.isAlive() || !_v.hasHealth())
            {
                return;
            }
            // a light move (a flow strike between blows, a ki shot's draw) is no shield: the launch or
            // the stagger cuts it short. The unit the player drives is never stunned or thrown about
            // by a master's ordinary blows (its moves are the player's): it only rocks
            bool driven = ControllableUnit.isControllingUnit(_v);
            bool free = !driven && !Moves.Locked(_v);
            if (driven)
            {
                _v.startShake(0.25f, 0.12f, true, false);
            }
            Vector2 from = Pose.Ground;
            switch (b.B)
            {
                case Blow.LaunchKick:
                    if (free)
                    {
                        bool heavy = Weights.Heavy(_v);
                        float far = _critter ? 1.6f : 1f;
                        Combat.Launch(_v, from, 0.7f * _lk * far, 1.9f * (_critter ? 1.3f : 1f), (Random.value < 0.5f ? -1f : 1f) * 540f * far);
                        SetLink(heavy ? Link.Down : Link.Launched);   // (a heavy one only staggers)
                    }
                    else
                    {
                        SetLink(Link.Close);   // it never left the ground
                    }
                    break;
                case Blow.Sweep:
                case Blow.Cleave:
                case Blow.LeapStrike:
                    if (free)
                    {
                        Combat.Stagger(_v, 0.35f);
                    }
                    SetLink(free ? Link.Down : Link.Close);
                    break;
                default:
                    if (free)
                    {
                        if (_big || Weights.Heavy(_v))
                        {
                            Combat.Stagger(_v, 0.25f);
                        }
                        else
                        {
                            Combat.Launch(_v, from, 0.55f * _lk * (_critter ? 1.5f : 1f), 0.25f, 0f);
                        }
                    }
                    SetLink(Link.Close);
                    break;
            }
        }

        private void SetLink(Link link)
        {
            if (_v != null && _v.isAlive() && _v.hasHealth() && _v.Sid() == _vid)
            {
                EndLink = link;
                EndTarget = _v;
            }
        }

        /// <summary>A heavy finisher meets the ground: dust, and from Grandmasters a crack (a God's scorches it).</summary>
        private void GroundBlow()
        {
            Vector2 feet = _vg;
            WorldTile tile = Combat.TileAt(feet);
            float px = Px;
            if (Visible)
            {
                Kit.DustRing(feet, px, tile, 6, 34f, 0.42f, _k);
                if (_tier >= Tier.Grandmaster)
                {
                    Kit.Crater(feet, px, tile, _tier >= Tier.God, 3.5f, true, _pal.Edge);
                    FlowStrikes.Shake(feet, 0.1f, 0.004f, _k);
                }
            }
            // the ground itself takes it (the "Environment damage" setting): a Grandmaster's heavy
            // blows scorch it now and then, a God's blast small craters into it
            float roll = Random.value;
            if (_tier >= Tier.God && roll < 0.4f)
            {
                Kit.Ravage(feet, 1f, roll < 0.2f ? 2 : 1, A);
            }
            else if (_tier == Tier.Grandmaster && roll < 0.2f)
            {
                Kit.Ravage(feet, 1f, 1, A);
            }
        }

        // ---------------------------------------------------------------- fx --

        /// <summary>The blow's swoosh (or streak) and a smear afterimage, as it starts.</summary>
        private void StrikeFx(Beat b)
        {
            float px = Px;
            float fwd = Fwd;
            Vector2 d = Dir();
            if (_sh.ArcR > 0)
            {
                float offY = _sh.ArcOff.y < -50f ? -Pose.PivotUp + 1.5f : _sh.ArcOff.y;
                Sprite[] arc = Art.Arc(_sh.ArcR + _arcVar, _sh.ArcRatio, _sh.A0, _sh.A1, _sh.ArcThick);
                Kit.Swish(this, arc, (b.Last ? 0.13f : 0.1f) * _s + 0.02f, new Vector2(_sh.ArcOff.x * fwd, offY), fwd, _pal.Core, _pal.Edge, 6);
            }
            else
            {
                // a straight blow: a streak off the fist (or the blade's line along the thrust)
                Vector2 hand = Hand();
                float len = b.B == Blow.Thrust ? 8f : b.B == Blow.Cross ? 6f : 4f;
                Kit.Line(hand, hand + d * len * px, px, 0.06f, 1, FxLayer.Over, 7, White, Fx.WithA(_pal.Edge, 0.8f), Fx.WithA(_pal.Edge, 0f));
                Kit.SpeedLines(hand + d * 2f * px, d, px, b.B == Blow.Jab ? 1 : 2, 2.5f, 0.08f, Fx.WithA(_pal.Edge, 0.75f), FxLayer.Over);
            }
            Fx.Ghost(this, -d * 2f * px, -6f, 0.07f, Fx.WithA(_pal.Ghost, 0.55f), _pal.GhostEnd);
        }

        private void BlowFx(Beat b, Vector2 at, HitResult r)
        {
            float px = Px;
            Vector2 d = Dir();
            Color edge = _pal.Edge;
            // a bare hand's leaping blow lands like a punch, not a cut
            Touch touch = _sh.Touch == Touch.Cut && !_armed ? Touch.Punch : _sh.Touch;
            switch (touch)
            {
                case Touch.Punch:
                    {
                        Particle star = Fx.Spawn(Art.HitStars[b.Last ? 1 : 0], at, px, b.Last ? 0.08f : 0.06f, FxLayer.Top, 20);
                        if (star != null)
                        {
                            star.Rot = Random.Range(0f, 90f);
                            star.Sized(1.1f, 0.7f).Color(White, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
                        }
                        Sparks(at, d, b.Last ? 3 : 2, edge);
                        break;
                    }
                case Touch.Cut:
                    {
                        float ang = _sh.SlashAngle;
                        if (Fwd < 0f)
                        {
                            ang = 180f - ang;
                        }
                        float rad = ang * Mathf.Deg2Rad;
                        Vector2 ld = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                        float half = (b.Last ? 6f : 4.5f) * Mathf.Clamp(_vh / (10f * px), 0.8f, 2.5f) * px;
                        Kit.Line(at - ld * half, at + ld * half, px, 0.09f, 2, FxLayer.Top, 15, Fx.WithA(edge, 0.95f), Fx.WithA(edge, 0.6f), Fx.WithA(edge, 0f));
                        Kit.Line(at - ld * half * 0.85f, at + ld * half * 0.85f, px, 0.06f, 1, FxLayer.Top, 16, White, Fx.WithA(_pal.Core, 0.8f), Fx.WithA(_pal.Core, 0f));
                        Sparks(at, d, 2, Steel);
                        break;
                    }
                default:
                    {
                        Particle star = Fx.Spawn(Art.HitStars[1], at, px, 0.07f, FxLayer.Top, 20);
                        if (star != null)
                        {
                            star.Rot = Random.Range(0f, 90f);
                            star.Sized(1.15f, 0.75f).Color(White, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
                        }
                        Sparks(at, d, 2, edge);
                        if (_sh.Contact < 0.3f)
                        {
                            DuelFx.Puff(_vg, Mathf.Clamp(_v.actor_scale, 0.04f, 0.4f), _v.current_tile, 2, 14f);
                        }
                        break;
                    }
            }
            if (b.Last || _sh.Heavy)
            {
                Tier vt = Tiers.Of(_v);
                FlashStep.FlashUnit(_v, vt != Tier.None && !r.Killed ? Tiers.Color(vt) : White, 0.06f);
            }
            if (b.Last)
            {
                Kit.Flash(at, px, Mathf.Max(3, Mathf.RoundToInt(4f * _k)), 0.07f, Fx.WithA(_pal.Core, 0.95f));
                Fx.Ring(at, px, 0.16f, 2f, 9f * _k, 1, 1, FxLayer.Top, 3, 0.45f, 2.5f, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(edge, 0.5f), Fx.WithA(edge, 0f));
                Fx.FocusLines(at, px, Fx.Count(5), 9f * _k, 16f * _k, 0.09f);
                if (_sh.Heavy)
                {
                    FlowStrikes.Shake(at, 0.1f, 0.0035f, _k);
                }
            }
        }

        /// <summary>A few sparks flying off a blow along 'd'.</summary>
        private void Sparks(Vector2 at, Vector2 d, int n, Color c)
        {
            float px = Px;
            float base0 = Mathf.Atan2(d.y, d.x);
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = base0 + Random.Range(-0.9f, 0.9f);
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, 2)], at, px, Random.Range(0.08f, 0.14f), FxLayer.Top, 19);
                if (p == null)
                {
                    break;
                }
                p.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(55f, 95f) * px, 0f, 8f);
                p.FaceVel = true;
                p.Color(White, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>A blow that meets nothing: a white swish through the air.</summary>
        private void WhiffFx()
        {
            if (!Visible)
            {
                return;
            }
            Particle p = Fx.Spawn(Art.Arc(6, 0.8f, 60f, -60f, 2), Hand() + new Vector2(Fwd * 2f * Px, 0f), Px, 0.12f, FxLayer.Over, 5);
            if (p != null)
            {
                p.SX = Fwd;
                p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
            }
        }

        // ------------------------------------------------------------ endings --

        private void Recover()
        {
            if (Enter())
            {
                _rot0 = _crab ? 0f : Rot0(Pose.Rot);
                _sx0 = Pose.SX;
                _sy0 = Pose.SY;
                _h0 = Pose.Height;
                Pose.Hidden = false;
                Pose.HideShadow = false;
                Pose.Nudge = Vector2.zero;
                if (_glintEnd && Visible)
                {
                    Fx.Glint(Hand() + new Vector2(0f, 1.5f * Px), Px, 0.2f, _pal.Core, 0.9f);
                }
            }
            float dur = (_glintEnd ? 0.12f : 0.07f) * _s;
            float k = Mathf.Clamp01(_pt / dur);
            float amp = Anim.Spring(_pt, 10f, 20f);
            Pose.SX = 1f + (_sx0 - 1f) * amp;
            Pose.SY = 1f + (_sy0 - 1f) * amp;
            Pose.Rot = _rot0 * Mathf.Exp(-14f * _pt);
            Pose.Height = Mathf.Lerp(_h0, 0f, Anim.OutQuad(k));
            if (k >= 1f)
            {
                Neutral();
                Finished = true;
            }
        }

        /// <summary>The first blow was parried: the chain breaks and the fighter reels back.</summary>
        private void Recoil()
        {
            if (Enter())
            {
                _from = Pose.Ground;
                _to = _from - Dir() * 0.38f * _k;
                if (!Combat.SafeSpot(_to) || !Combat.ClearPath(_from, _to))
                {
                    _to = _from;
                }
                EndLink = Link.None;
                EndTarget = null;
                FlowStrikes.Sound(_armed ? SndSwords : SndMetal, A.current_tile);
                if (Visible)
                {
                    Vector2 at = Vector2.Lerp(Hand(), VBody(_sh.Contact > 0f ? _sh.Contact : 0.6f), 0.5f);
                    DuelFx.Ting(at, Px, White, 3, -Dir(), 0.9f);
                    Fx.Afterimage(this, 0.12f, new Color(1f, 1f, 1f, 0.7f), _pal.GhostEnd);
                }
            }
            float k = Mathf.Clamp01(_pt / TRecoil);
            Pose.Ground = Vector2.Lerp(_from, _to, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
            float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.25f));
            Pose.Rot = _crab ? 0f : -20f * lean;
            Pose.SX = 1f - 0.06f * lean;
            Pose.SY = 1f + 0.06f * lean;
            Pose.Height = Mathf.MoveTowards(Pose.Height, 0f, 0.05f);
            Pose.Nudge = k < 0.5f ? Tremble(_pt) : Vector2.zero;
            if (k >= 1f)
            {
                A.startAttackCooldown();
                Neutral();
                Finished = true;
            }
        }

        /// <summary>It wasn't there: the blow cuts an afterimage and the fighter overreaches.</summary>
        private void Whiff()
        {
            if (Enter())
            {
                _from = Pose.Ground;
                _to = _from + Dir() * 0.2f * _k;
                if (!Combat.SafeSpot(_to))
                {
                    _to = _from;
                }
                EndLink = Link.None;
                EndTarget = null;
                FlowStrikes.Sound(SndWhoosh, A.current_tile);
                WhiffFx();
            }
            float k = Mathf.Clamp01(_pt / TWhiff);
            float s = Anim.OutQuad(k);
            Pose.Ground = Vector2.Lerp(_from, _to, s);
            Pose.Rot = _crab ? 0f : Mathf.Lerp(24f, 4f, s);
            Pose.SX = Mathf.Lerp(1.12f, 1f, s);
            Pose.SY = Mathf.Lerp(0.9f, 1f, s);
            Pose.Height = Mathf.MoveTowards(Pose.Height, 0f, 0.05f);
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                A.startAttackCooldown();
                Neutral();
                Finished = true;
            }
        }

        // ----------------------------------------------------------- exchange --

        /// <summary>
        /// Would the enemy have swung at this one right now? Then this isn't a chain on it, it is
        /// an exchange with it.
        /// </summary>
        private bool WantsExchange(FlowStrikes.Mem mem, bool meet)
        {
            if (!_flow || _big || _small || !CanPartner(_v) || !ExchangeFits(_v, out _, out _))
            {
                return false;   // (no ground between them to square up on: water, fire, a wall)
            }
            if (meet)
            {
                return true;   // its turn is owed: they meet
            }
            float chance = Duels.Opponent(A) == _v ? 0.85f : 0.7f;
            if (mem != null && Fighter.Now - mem.LastExchange < 1.5)
            {
                chance *= 0.45f;   // not every meeting: a chain or a technique in between
            }
            if (Random.value >= chance)
            {
                _joinable = false;   // this time it is a chain on it, and it takes it
                return false;
            }
            // it meets this one's blows if it was about to swing anyway (or answers within the window;
            // its swing's timer runs in game time)
            return _v.attack_timer <= JoinWindow * FlowStrikes.Speed();
        }

        /// <summary>A free Flow fighter that could meet this one's blows with its own.</summary>
        private bool CanPartner(Actor b)
        {
            return PartnerOk(A, Pose.Ground, _stand + 1.4f * _k, b);
        }

        private static bool PartnerOk(Actor a, Vector2 at, float within, Actor b)
        {
            if (b == null || !b.isAlive() || !FlowStrikes.IsFlow(b) || !Moves.CanPerform(b) || Moves.Busy(b) || Director.InScene(b) || !b.hasMeleeAttack())
            {
                return false;
            }
            if (!Combat.Grounded(b) || !Combat.IsFoe(b, a) || b.hasStatusStunned() || b.is_unconscious)
            {
                return false;
            }
            if (b.has_attack_target && b.attack_target != null && b.attack_target != a)
            {
                return false;   // it is busy with someone else
            }
            if (Vector2.Distance(at, b.current_position) > within)
            {
                return false;
            }
            // (a duelist whose techniques the duel rations is still free for blades: only its bout,
            // beginning this very moment, takes it)
            return FlowStrikes.Free(b, 0.3f) && FlowStrikes.Budget(b.current_position);
        }

        /// <summary>
        /// Could 'a' and the master 'b' meet in an exchange right now (alike in size, both free, face
        /// to face, both with blows to trade)? Asked before a chain starts.
        /// </summary>
        internal static bool CanMeet(Actor a, Actor b)
        {
            if (a == null || b == null || !a.hasMeleeAttack())
            {
                return false;
            }
            float ratio = Sizes(a, FlowStrikes.SpriteH(a), b, FlowStrikes.SpriteH(b), out bool big, out bool small, out _);
            if (big || small)
            {
                return false;
            }
            float k = FlowStrikes.KOf(a);
            return PartnerOk(a, a.current_position, StandOf(FlowStrikes.Armed(a), ratio, k) + 1.4f * k, b)
                && Fits(a.current_position, k, a.flip ? 1f : -1f, b, out _, out _);
        }

        /// <summary>
        /// The enemy answers this chain with its own within the first moments (it asked to swing
        /// at this fighter): the two chains become one exchange.
        /// </summary>
        internal bool TryJoin(Actor b)
        {
            if (!_joinable || Exchanging || Finished || _vGone || b != _v || b.Sid() != _vid || T > JoinWindow || _phase == Phase.Recoil || _phase == Phase.Whiff
                || _phase == Phase.Recover || _bi >= _beats.Count || _big || _small)
            {
                return false;
            }
            if (_phase == Phase.Travel && _entered && _tTravel > 0f && (_stepNow == Step.Hop || _stepNow == Step.Blink))
            {
                return false;   // never mid-flip or mid-blink (as Interruptible): the other answers once it lands
            }
            if (!CanPartner(b))
            {
                return false;
            }
            return OpenExchange(true, null);
        }

        /// <summary>
        /// Turns this chain into an exchange: the other fighter becomes this move's partner.
        /// Unless it joined on its own, it may first answer the contact (a parry, an evasion).
        /// </summary>
        private bool OpenExchange(bool joined, FlowStrikes.Mem mem)
        {
            Actor b = _v;
            if (b == null || !b.isAlive() || b.Sid() != _vid)
            {
                return false;
            }
            if (!joined)
            {
                _reached = true;
                Contact answer = Combat.Reach(this, b, false);
                if (answer == Sakuga.Contact.Parried)
                {
                    Next(Phase.Recoil);
                    return false;
                }
                if (answer == Sakuga.Contact.Evaded)
                {
                    Next(Phase.Whiff);
                    return false;
                }
                if (!Moves.CanPerform(b))
                {
                    return false;
                }
            }
            if (!PlanExchange(b))
            {
                return false;
            }
            FlowPartner p = new FlowPartner(b, this);
            if (!Moves.Start(p))
            {
                return false;
            }
            FlowStrikes.Live++;
            FlowStrikes.Exchanges++;
            _xp = p;
            _exchanged = true;
            _hold = 0f;
            b.startAttackCooldown();
            Pose.Hidden = false;
            Pose.HideShadow = false;
            (mem ?? FlowStrikes.MemOf(A)).LastExchange = Fighter.Now;
            FlowStrikes.MemOf(b).LastExchange = Fighter.Now;
            EndLink = Link.None;
            EndTarget = null;
            Next(Phase.Engage);
            if (Dev.On) Dev.Trace("flow " + Id + " exchange with " + b.asset.id + "#" + b.Sid() + (joined ? " (it answered)" : "") + ": " + _xn + " clashes");
            return true;
        }

        /// <summary>Where the two square up, how many times they clash, at what heights, and who gets through.</summary>
        /// <summary>Where the two would square up: the middle between them, the axis, and both stances on safe ground?</summary>
        private bool ExchangeFits(Actor b, out Vector2 mid, out Vector2 axis)
        {
            return Fits(Pose.Ground, _k, Fwd, b, out mid, out axis);
        }

        private static bool Fits(Vector2 pa, float ka, float fwd, Actor b, out Vector2 mid, out Vector2 axis)
        {
            Vector2 pb = b.current_position;
            Vector2 d = pb - pa;
            float dist = d.magnitude;
            axis = dist > 0.05f ? d / dist : new Vector2(fwd, 0f);
            if (Mathf.Abs(axis.x) < 0.6f)
            {
                // one above the other on screen: they square up side-on, so the clashes read
                float sx = Mathf.Abs(d.x) > 0.02f ? Mathf.Sign(d.x) : fwd;
                axis = new Vector2(sx * 0.8f, (d.y >= 0f ? 1f : -1f) * 0.6f).normalized;
            }
            mid = (pa + pb) * 0.5f;
            float ga = 0.36f * ka + 0.08f, gb = 0.36f * FlowStrikes.KOf(b) + 0.08f;
            return Combat.SafeSpot(mid) && Combat.SafeSpot(mid - axis * ga) && Combat.SafeSpot(mid + axis * gb);
        }

        private bool PlanExchange(Actor b)
        {
            if (!ExchangeFits(b, out Vector2 mid, out Vector2 axis))
            {
                return false;
            }
            _kB = FlowStrikes.KOf(b);
            _gapA = 0.36f * _k + 0.08f;
            _gapB = 0.36f * _kB + 0.08f;
            _xb = b;
            _xbid = b.Sid();
            _xMid = mid;
            _xAxis = axis;
            _palB = Palette.For(b, 3);
            _armedB = FlowStrikes.Armed(b);
            _crabB = FlashStep.IsCrab(b);
            _ahB = FlowStrikes.SpriteH(b);
            Tier tb = Tiers.Of(b);
            Tier low = tb < _tier ? tb : _tier;
            _xn = low >= Tier.God ? Random.Range(4, 6) : low == Tier.Grandmaster ? Random.Range(3, 5) : 3;
            _xi = 0;
            _xH = new float[_xn];
            _xOut = new int[_xn];
            float[] heights = { 0.78f, 0.52f, 0.3f };
            int lastH = -1;
            float pa0 = Tiers.Power(A), pb0 = Tiers.Power(b);
            float share = pa0 / Mathf.Max(0.01f, pa0 + pb0);
            int maxLand = low >= Tier.God ? 2 : 1;
            int landA = 0, landB = 0;
            for (int i = 0; i < _xn; i++)
            {
                int h = Random.Range(0, 3);
                if (h == lastH)
                {
                    h = (h + 1 + Random.Range(0, 2)) % 3;
                }
                lastH = h;
                _xH[i] = heights[h];
                float roll = Random.value;
                if (roll < 0.44f * share && landA < maxLand)
                {
                    _xOut[i] = 1;
                    landA++;
                }
                else if (roll >= 0.44f * share && roll < 0.44f && landB < maxLand)
                {
                    _xOut[i] = 2;
                    landB++;
                }
            }
            float r = Random.value;
            _xFinal = r < 0.35f * share ? 1 : r < 0.35f ? 2 : 0;
            float dur = (0.07f + 0.1f * _xn + 0.2f + 0.07f) * _s;
            _xMultA = FlowStrikes.Total(A, dur) * 0.5f * FlowStrikes.Comp(A, b);
            _xMultB = FlowStrikes.Total(b, dur) * 0.5f * FlowStrikes.Comp(b, A);
            return true;
        }

        private Vector2 StanceA => _xMid - _xAxis * _gapA;

        private Vector2 StanceB => _xMid + _xAxis * _gapB;

        private void TickExchange(float dt)
        {
            _pt += dt;
            if (!ExValid())
            {
                return;
            }
            _xp.Idle = 0f;
            _xShake -= dt;
            switch (_phase)
            {
                case Phase.Engage:
                    Engage();
                    break;
                case Phase.Clash:
                    ClashBeat(dt);
                    break;
                case Phase.Break:
                    BreakBeat(dt);
                    break;
                case Phase.Settle:
                    SettleBeat();
                    break;
                default:
                    Next(Phase.Settle);
                    break;
            }
        }

        /// <summary>The other one is gone mid-exchange (killed, picked up, taken): this one holds a pose and ends.</summary>
        private bool ExValid()
        {
            if (!_xp.Finished && _xb != null && _xb.isAlive() && _xb.Sid() == _xbid && !_xb.isInMagnet())
            {
                return true;
            }
            if (!_xp.Finished)
            {
                _xp.Let();
            }
            _xp = null;
            _vGone = true;
            EndLink = Link.None;
            EndTarget = null;
            _glintEnd = true;
            Next(Phase.Recover);
            return false;
        }

        /// <summary>Each faces the other.</summary>
        private void FaceEach()
        {
            if (Mathf.Abs(_xAxis.x) > 0.08f)
            {
                Pose.FaceRight = _xAxis.x > 0f;
                _xp.Pose.FaceRight = _xAxis.x < 0f;
            }
        }

        private void Engage()
        {
            if (Enter())
            {
                _xA0 = Pose.Ground;
                _xB0 = _xp.Pose.Ground;
                _rot0 = _crab ? 0f : Rot0(Pose.Rot);
                FaceEach();
                FlowStrikes.Sound(SndWhoosh, A.current_tile);
                if (Visible)
                {
                    DuelFx.Puff(_xA0, Px, A.current_tile, 2, 10f);
                    DuelFx.Puff(_xB0, _xp.Px, _xb.current_tile, 2, 10f);
                }
            }
            float k = Mathf.Clamp01(_pt / (0.07f * _s));
            float e = Anim.OutQuad(k);
            Pose.Ground = Vector2.Lerp(_xA0, StanceA, e);
            Guard(ref Pose, e, _crab, _rot0);
            _xp.Pose.Ground = Vector2.Lerp(_xB0, StanceB, e);
            Guard(ref _xp.Pose, e, _crabB, 0f);
            if (k >= 1f)
            {
                Next(Phase.Clash);
            }
        }

        private static void Guard(ref Pose p, float e, bool crab, float rot0)
        {
            p.Height = Mathf.Lerp(p.Height, 0f, e);
            p.Rot = crab ? 0f : Mathf.Lerp(rot0, 4f, e);
            p.SX = Mathf.Lerp(p.SX, 1.04f, e);
            p.SY = Mathf.Lerp(p.SY, 0.97f, e);
            p.Nudge = Vector2.zero;
            p.Hidden = false;
            p.HideShadow = false;
        }

        private void ClashBeat(float dt)
        {
            float dur = 0.1f * _s;
            if (Enter())
            {
                _xContact = false;
                _xSlideA = _xSlideB = 0f;
                if (_xi > 0 && Random.value < 0.75f)
                {
                    Orbit(Random.Range(12f, 26f) * (Random.value < 0.5f ? -1f : 1f));
                }
                FaceEach();
                // each draws back from where the last clash left it (no pop onto the new stance)
                _xA0 = Pose.Ground;
                _xB0 = _xp.Pose.Ground;
            }
            float k = Mathf.Clamp01(_pt / dur);
            float h = _xH[_xi];
            int o = _xOut[_xi];
            ClashPose(ref Pose, _xA0, StanceA, -1f, k, h, _k, o == 2, _xSlideA, _crab);
            ClashPose(ref _xp.Pose, _xB0, StanceB, 1f, k, h, _kB, o == 1, _xSlideB, _crabB);
            if (_xShake > 0f)
            {
                Pose.Nudge = Tremble(_xShake);
                _xp.Pose.Nudge = -Tremble(_xShake);
            }
            if (!_xContact && k >= 0.5f)
            {
                _xContact = true;
                ExContact(h, o, false);
                if (Finished || _xp == null)
                {
                    return;
                }
            }
            if (k > 0.55f)
            {
                SkidDust(dt, 0.05f);
            }
            if (k >= 1f)
            {
                _xi++;
                // every clash drives them a little further apart
                _gapA += 0.02f * _k + _xSlideA * 0.5f;
                _gapB += 0.02f * _kB + _xSlideB * 0.5f;
                if (!Combat.SafeSpot(StanceA) || !Combat.SafeSpot(StanceB))
                {
                    _gapA -= 0.02f * _k + _xSlideA * 0.5f;
                    _gapB -= 0.02f * _kB + _xSlideB * 0.5f;
                }
                Next(_xi >= _xn ? Phase.Break : Phase.Clash);
            }
        }

        /// <summary>The pair turns round its middle between clashes (if both have ground to stand on there).</summary>
        private void Orbit(float deg)
        {
            Vector2 axis = FlashStep.Rotate(_xAxis, deg);
            // keep it side-on enough to read: never turn into a vertical line
            if (Mathf.Abs(axis.x) < 0.45f)
            {
                return;
            }
            Vector2 sa = _xMid - axis * _gapA, sb = _xMid + axis * _gapB;
            if (Combat.SafeSpot(sa) && Combat.SafeSpot(sb))
            {
                _xAxis = axis.normalized;
            }
        }

        /// <summary>
        /// One fighter's pose in a clash: draw back, lunge at the middle (stretched; low blows crouch,
        /// high ones hop), rock back (further if the other got through).
        /// </summary>
        private void ClashPose(ref Pose p, Vector2 start, Vector2 stance, float sigma, float k, float h, float kx, bool lost, float slide, bool crab)
        {
            Vector2 outward = _xAxis * sigma;
            Vector2 pulled = stance + outward * 0.05f * kx;
            Vector2 lunged = stance - outward * 0.13f * kx;
            float low = h < 0.4f ? 1f : 0f, high = h > 0.7f ? 1f : 0f;
            Vector2 pos;
            float rot, sx, sy, height = 0f;
            if (k < 0.35f)
            {
                float e = Anim.OutQuad(k / 0.35f);
                pos = Vector2.Lerp(start, pulled, e);
                rot = Mathf.Lerp(4f, -10f, e);
                sx = Mathf.Lerp(1.04f, 0.94f, e);
                sy = Mathf.Lerp(0.97f, 1.05f, e);
            }
            else if (k < 0.55f)
            {
                float e = Anim.OutCubic((k - 0.35f) / 0.2f);
                pos = Vector2.Lerp(pulled, lunged, e);
                rot = Mathf.Lerp(-10f, 20f + 10f * low, e);
                sx = Mathf.Lerp(0.94f, 1.22f, e);
                sy = Mathf.Lerp(1.05f, 0.88f - 0.08f * low, e);
                height = 0.08f * kx * high * e;
            }
            else
            {
                float e = Anim.OutQuad((k - 0.55f) / 0.45f);
                Vector2 rest = stance + outward * (0.04f * kx + slide);
                pos = Vector2.Lerp(lunged, rest, e);
                rot = Mathf.Lerp(lost ? -18f : 20f + 10f * low, 3f, e);
                sx = Mathf.Lerp(1.22f, 1.03f, e);
                sy = Mathf.Lerp(0.88f - 0.08f * low, 0.98f, e);
                height = 0.08f * kx * high * (1f - e);
            }
            if (Combat.SafeSpot(pos))
            {
                p.Ground = pos;
            }
            p.Rot = crab ? 0f : rot;
            p.SX = sx;
            p.SY = sy;
            p.Height = height;
            p.Nudge = Vector2.zero;
        }

        /// <summary>Where the blows meet: between the two, at head, body or knee height.</summary>
        private Vector2 ContactAt(float h)
        {
            Vector2 mid = Vector2.Lerp(Pose.Ground, _xp.Pose.Ground, _gapA / Mathf.Max(0.01f, _gapA + _gapB));
            return mid + new Vector2(0f, (_ah + _ahB) * 0.5f * h);
        }

        /// <summary>The blows meet: an even clash, or one of them gets through.</summary>
        private void ExContact(float h, int o, bool final)
        {
            Vector2 c = ContactAt(h);
            float px = Mathf.Min(Px, _xp.Px);
            float k2 = (_k + _kB) * 0.5f;
            bool vis = Kit.Visible(c);
            if (o == 0)
            {
                bool metal = _armed && _armedB;
                if (vis)
                {
                    DuelFx.Starburst(c, px, (final ? 0.42f : 0.22f) * k2, _pal.Core, _pal.Edge, _palB.Edge, final ? 0.1f : 0.07f);
                    DuelFx.Spray(c, px, final ? 7 : 4, _xAxis, metal, _pal.Edge);
                    if (final || (_xi & 1) == 1)
                    {
                        DuelFx.Ting(c + new Vector2(0f, 2f * px), px, White, 2, Vector2.up, final ? 1.1f : 0.8f);
                    }
                }
                FlowStrikes.Sound(metal ? SndSwords : _armed || _armedB ? SndMetal : SndPunch, A.current_tile);
                _xShake = 0.035f;
            }
            else
            {
                bool aWins = o == 1;
                Actor att = aWins ? A : _xb;
                Actor vic = aWins ? _xb : A;
                Move by = aWins ? (Move)this : _xp;
                Palette pal = aWins ? _pal : _palB;
                float mult = (aWins ? _xMultA : _xMultB) * (final ? 1.4f : 1f);
                HitResult r = Combat.Hit(att, vic, mult, final, true, 0f, by);
                Vector2 vb = aWins ? new Vector2(_xp.Pose.Ground.x, _xp.Pose.Ground.y + _ahB * h) : new Vector2(Pose.Ground.x, Pose.Ground.y + _ah * h);
                if (vis)
                {
                    Particle star = Fx.Spawn(Art.HitStars[final ? 2 : 1], vb, px, 0.08f, FxLayer.Top, 20);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 90f);
                        star.Sized(1.15f, 0.75f).Color(White, Fx.WithA(pal.Edge, 0.9f), Fx.WithA(pal.Edge, 0f));
                    }
                    Kit.Sparks(vb, px, 3, 50f, 95f, pal.Edge);
                    Tier vt = Tiers.Of(vic);
                    FlashStep.FlashUnit(vic, vt != Tier.None && !r.Killed ? Tiers.Color(vt) : White, 0.07f);
                    if (r.Crit && final)
                    {
                        Kit.ImpactFrame(vb, px, 0.45f * k2, pal, true);
                    }
                }
                if (r.Landed)
                {
                    try
                    {
                        att.attackTargetActions(vic, vic.current_tile);
                    }
                    catch (System.Exception e)
                    {
                        SakugaMod.Report("flow on-hit", e);
                    }
                }
                if (aWins)
                {
                    _xSlideB = 0.13f * _kB;
                }
                else
                {
                    _xSlideA = 0.13f * _k;
                }
                bool cut = aWins ? _armed : _armedB;
                FlowStrikes.Sound(cut ? SndFlesh : SndPunch, vic.current_tile);
            }
            if (final && vis)
            {
                Color core = Fx.WithA(_pal.Core, 0.95f);
                DuelFx.AirRing(c, px, 3f, 16f * k2, 0.22f, 1, core, Fx.WithA(_pal.Edge, 0.6f), DuelFx.Clear);
                DuelFx.Fountain(c, px, 6, 50f, o == 2 ? _palB.Edge : _pal.Edge);
                FlowStrikes.Shake(c, 0.12f, 0.005f, k2);
            }
            if (final)
            {
                // two great masters' last clash marks the ground between them; two Gods' blasts it
                Tier tb = Tiers.Of(_xb);
                Tier low = tb < _tier ? tb : _tier;
                if (low >= Tier.Grandmaster)
                {
                    WorldTile tile = Combat.TileAt(_xMid);
                    if (vis)
                    {
                        Kit.Crater(_xMid, px, tile, low >= Tier.God, 3.5f, true, _pal.Edge);
                    }
                    if (low >= Tier.God && Random.value < 0.5f)
                    {
                        Kit.Ravage(_xMid, 1f, 2, A);
                    }
                }
            }
        }

        /// <summary>Dust kicked up behind the sliding feet.</summary>
        private void SkidDust(float dt, float every)
        {
            _xDust -= dt;
            if (_xDust > 0f || !Visible)
            {
                return;
            }
            _xDust = every;
            DuelFx.SkidDust(Pose.Ground, -_xAxis, Px, A.current_tile, _k);
            DuelFx.SkidDust(_xp.Pose.Ground, _xAxis, _xp.Px, _xb.current_tile, _kB);
        }

        private void BreakBeat(float dt)
        {
            float dur = 0.2f * _s;
            if (Enter())
            {
                _xContact = false;
                FaceEach();
                Vector2 sa = StanceA, sb = StanceB;
                _xLungeA = sa + _xAxis * 0.16f * _k;
                _xLungeB = sb - _xAxis * 0.16f * _kB;
                _xA1 = SlideTo(sa, -_xAxis, (0.42f + (_xFinal == 2 ? 0.3f : 0f)) * _k);
                _xB1 = SlideTo(sb, _xAxis, (0.42f + (_xFinal == 1 ? 0.3f : 0f)) * _kB);
                _xA0 = Pose.Ground;
                _xB0 = _xp.Pose.Ground;
            }
            float k = Mathf.Clamp01(_pt / dur);
            BreakPose(ref Pose, _xA0, _xLungeA, _xA1, k, _xFinal == 2, _crab);
            BreakPose(ref _xp.Pose, _xB0, _xLungeB, _xB1, k, _xFinal == 1, _crabB);
            if (_xShake > 0f)
            {
                Pose.Nudge = Tremble(_xShake);
                _xp.Pose.Nudge = -Tremble(_xShake);
            }
            if (!_xContact && k >= 0.4f)
            {
                _xContact = true;
                ExContact(0.55f, _xFinal, true);
                if (Finished || _xp == null)
                {
                    return;
                }
            }
            if (k > 0.42f)
            {
                SkidDust(dt, 0.03f);
            }
            if (k >= 1f)
            {
                if (Visible)
                {
                    DuelFx.SkidMarks(_xLungeA, _xA1, Px, A.current_tile, 2.5f);
                    DuelFx.SkidMarks(_xLungeB, _xB1, _xp.Px, _xb.current_tile, 2.5f);
                }
                Next(Phase.Settle);
            }
        }

        /// <summary>Slid 'len' tiles along 'dir' from 'p', stopping short of anything it can't stand on.</summary>
        private static Vector2 SlideTo(Vector2 p, Vector2 dir, float len)
        {
            Vector2 to = p;
            for (float d = 0.1f; d <= len + 0.001f; d += 0.1f)
            {
                Vector2 q = p + dir * d;
                if (!Combat.SafeSpot(q))
                {
                    break;
                }
                to = q;
            }
            return to;
        }

        /// <summary>The last clash: a big wind, the lunge, then blown apart (the loser reeling, leaning far back).</summary>
        private static void BreakPose(ref Pose p, Vector2 start, Vector2 lunge, Vector2 end, float k, bool lost, bool crab)
        {
            float rot, sx, sy;
            if (k < 0.3f)
            {
                float e = Anim.OutQuad(k / 0.3f);
                Vector2 back = start + (start - lunge).normalized * 0.04f;
                p.Ground = Combat.SafeSpot(back) ? Vector2.Lerp(start, back, e) : start;
                rot = Mathf.Lerp(4f, -14f, e);
                sx = Mathf.Lerp(1.04f, 0.9f, e);
                sy = Mathf.Lerp(0.97f, 1.08f, e);
            }
            else if (k < 0.42f)
            {
                float e = Anim.OutCubic((k - 0.3f) / 0.12f);
                p.Ground = Vector2.Lerp(start, lunge, e);
                rot = Mathf.Lerp(-14f, 26f, e);
                sx = Mathf.Lerp(0.9f, 1.28f, e);
                sy = Mathf.Lerp(1.08f, 0.84f, e);
            }
            else
            {
                float e = Anim.OutCubic((k - 0.42f) / 0.58f);
                p.Ground = Vector2.Lerp(lunge, end, e);
                rot = lost ? Mathf.Lerp(-24f, -10f, e) : Mathf.Lerp(26f, -6f, Mathf.Clamp01(e * 1.6f));
                sx = Mathf.Lerp(1.28f, 1.08f, e);
                sy = Mathf.Lerp(0.84f, 0.95f, e);
            }
            p.Rot = crab ? 0f : rot;
            p.SX = sx;
            p.SY = sy;
            p.Height = 0f;
            p.Nudge = Vector2.zero;
        }

        private void SettleBeat()
        {
            if (Enter())
            {
                _rot0 = Rot0(Pose.Rot);
                _sx0 = Pose.SX;
                _sy0 = Pose.SY;
                _xRotB0 = Rot0(_xp.Pose.Rot);
                _xSxB0 = _xp.Pose.SX;
                _xSyB0 = _xp.Pose.SY;
            }
            float dur = 0.07f * _s;
            float k = Mathf.Clamp01(_pt / dur);
            float amp = Anim.Spring(_pt, 10f, 20f);
            float settle = Mathf.Exp(-14f * _pt);
            // both spring upright, facing each other
            Pose.Rot = _crab ? 0f : _rot0 * settle;
            Pose.SX = 1f + (_sx0 - 1f) * amp;
            Pose.SY = 1f + (_sy0 - 1f) * amp;
            Pose.Nudge = Vector2.zero;
            _xp.Pose.Rot = _crabB ? 0f : _xRotB0 * settle;
            _xp.Pose.SX = 1f + (_xSxB0 - 1f) * amp;
            _xp.Pose.SY = 1f + (_xSyB0 - 1f) * amp;
            _xp.Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                if (_xb.isAlive() && _xb.hasHealth())
                {
                    EndLink = Link.Close;
                    EndTarget = _xb;
                }
                _xp.Done(A);
                Neutral();
                Finished = true;
            }
        }

        // -------------------------------------------------------------- abort --

        public override void Abort()
        {
            if (_xp != null && !_xp.Finished)
            {
                _xp.Let();
            }
        }

        public override void OnEnd(bool aborted)
        {
            FlowStrikes.Live = Mathf.Max(0, FlowStrikes.Live - 1);
            if (_xp != null && !_xp.Finished)
            {
                _xp.Let();
            }
            if (!aborted)
            {
                if (Valid)
                {
                    // a plain chain on a master: that master gets its turn next (FlowStrikes.Strike)
                    FlowStrikes.Mem mem = FlowStrikes.MemOf(A);
                    mem.ChainEnd = FlowStrikes.MoveNow;
                    mem.LastVictim = !_exchanged && _flow && !_vGone && _v != null && _v.Sid() == _vid ? _vid : -1;
                }
                FlowStrikes.After(A, EndLink, EndTarget);
            }
        }
    }

    /// <summary>
    /// The other fighter in an exchange: Busy (the game's AI and the Brain leave it alone) and
    /// posed every tick by the FlowStrike that holds it. If that move stops driving it, it
    /// stands up and lets go on its own.
    /// </summary>
    internal sealed class FlowPartner : Move
    {
        /// <summary>As its exchange allows: never while the blades clash, any time once it is let go.</summary>
        public override bool Interruptible => Finished || Owner == null || Owner.Finished || Owner.Interruptible;

        public readonly FlowStrike Owner;
        /// <summary>Seconds since the owner last drove it.</summary>
        internal float Idle;
        private Actor _other;
        private bool _done;

        public FlowPartner(Actor a, FlowStrike owner) : base(a)
        {
            Owner = owner;
            Level = 3;
        }

        public override bool SuperArmor => true;

        public override void Tick(float dt)
        {
            Idle += dt;
            if (!Finished && (Idle > 0.3f || Owner == null || Owner.Finished))
            {
                Let();
            }
        }

        /// <summary>The exchange ended well: it gets its own chain hook on the other.</summary>
        public void Done(Actor other)
        {
            _done = true;
            _other = other;
            Let();
        }

        /// <summary>Back to a plain standing pose on safe ground, handed back to the game.</summary>
        public void Let()
        {
            if (Finished)
            {
                return;
            }
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (!Combat.SafeSpot(Pose.Ground) && !FlowStrike.Wading(Pose.Ground) && Combat.FindLanding(Pose.Ground, 2f, out Vector2 spot))
            {
                Pose.Ground = spot;
            }
            Finished = true;
        }

        public override void OnEnd(bool aborted)
        {
            FlowStrikes.Live = Mathf.Max(0, FlowStrikes.Live - 1);
            if (!aborted)
            {
                FlowStrikes.After(A, _done ? Link.Close : Link.None, _other);
            }
        }
    }
}
