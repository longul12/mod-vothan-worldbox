using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// A technique as a script of beats. Build it in Begin with the fluent methods (each adds beats:
    /// Strike, Flurry, StepIn, Dash, Blink, Leap, Grab, Charge, Beam, Shoot, Recover...; see CHOREO.md),
    /// then Start() it. The engine plays the beats one after another, easing the performer's pose from
    /// wherever the last beat left it (anticipation flows into the blow, the blow's follow-through into
    /// the next wind-up), scales every beat by the tier's tempo, keeps it facing its target, retargets
    /// once when the target dies or is taken, and answers the classic accidents itself:
    ///   parried    the blow bounces off a guard: it reels back and the move ends
    ///   evaded     it struck an afterimage: it overreaches into empty air and the move ends
    ///   target lost  (killed by another, picked up, gone inside) another foe within reach takes its
    ///              place once, or it jumps to the recovery and ends on a pose with a glint
    ///   broken     a fast blow while it is Charging (Choreo.Break / BreakCharge): the gathered power
    ///              bursts, and the performer is blasted away tumbling, lands, and lies dazed
    /// Its held bodies are let go, its beams collapse, its cinematic ends and its glued effects die
    /// with it, however it ends. Subclass it for bespoke moments (override the hooks), or hang code on
    /// any beat (Do, Every, Cue).
    /// </summary>
    internal partial class ChoreoMove : Move
    {
        // ---------------------------------------------------------------- who --

        public readonly Plan P;
        public readonly Tier Tier;
        public readonly TierInfo Info;
        /// <summary>The technique's school (its colours).</summary>
        public readonly School School;
        /// <summary>The technique's Scale at this level (Technique.ScaleAt: destruction, shake, hit-stop).</summary>
        public readonly Scale Scale;
        /// <summary>Colours (school x tier x level).</summary>
        public Hue Hue;
        /// <summary>The performer's size (1 a human) and sprite height in its own pixels.</summary>
        public readonly float Kk, Hpx;
        /// <summary>Tempo factors for this tier (see Tempo).</summary>
        public readonly float WindK, ActK, RecoverK, ImpactK;
        /// <summary>Carries a weapon (blades draw steel; fists ki).</summary>
        public readonly bool Armed;
        /// <summary>Planned by a god power or a dev command: further reach, fewer conditions.</summary>
        public readonly bool Forced;

        // ------------------------------------------------------------- target --

        /// <summary>The main target (null for a ground-targeted move or once it is lost).</summary>
        public Actor V;
        public long VId = -1;
        /// <summary>Where the target stands (its pose's ground while it is held or reeling).</summary>
        public Vector2 VG;
        /// <summary>The target's sprite height in its pixels, its pixel size, its size class share of damage (Choreo.ClassShare).</summary>
        public float VHpx = 10f, VPx = 0.1f, Cm = 1f;
        /// <summary>
        /// The class limits allow its blows on the target (fixed when it was taken). False: it is a class
        /// above its limits (an answer to a heavier attacker): its blows only glance (Cm = Choreo.Glance,
        /// the target only rocks) and it can't be lifted.
        /// </summary>
        public bool VAllowed { get; private set; } = true;
        /// <summary>Its weight class when taken (a retarget takes one no heavier).</summary>
        public Weight VClass;
        /// <summary>The planned point and direction (ground-targeted moves, lines).</summary>
        public Vector2 Point, Aim;
        /// <summary>Which way it faces the target (unit vector) and its left side.</summary>
        public Vector2 Dir = Vector2.right, Side = Vector2.up;
        /// <summary>+1 facing right, -1 left.</summary>
        public float Fwd = 1f;
        /// <summary>Where it stands (its pose's ground without the lunge).</summary>
        public Vector2 Stance;
        /// <summary>How far from the target it strikes (tiles; a blow's length).</summary>
        public float Stand = 0.6f;

        // ------------------------------------------------------------- script --

        private readonly List<Beat> _beats = new List<Beat>(16);
        private int _bi = -1;
        private Beat _b;
        private float _bt;
        /// <summary>This beat's length (seconds, with the tempo); an Enter hook may change it.</summary>
        public float BeatDur;
        private Shape _s0;
        private float _lunge, _side, _lunge0, _side0;
        private Vector2 _g0, _dest;
        private float _ghostT, _lineT, _dustT;
        private bool _started;
        /// <summary>Time in the current beat, and its progress 0..1.</summary>
        public float BeatT => _bt;
        public float BeatK => BeatDur > 0f ? Mathf.Clamp01(_bt / BeatDur) : 1f;
        public Beat Current => _b;

        // -------------------------------------------------------------- state --

        /// <summary>A local hit-stop: the pose holds, trembling with the blow.</summary>
        private float _hold;
        private int _retargets, _misses;
        private readonly HashSet<long> _reached = new HashSet<long>();
        private Link _link = Link.None;
        private Actor _linkTarget;
        private long _linkTargetId = -1;
        private bool _lost, _cine, _glintEnd;
        private readonly List<Particle> _glued = new List<Particle>(4);
        private readonly List<int> _gluedGen = new List<int>(4);
        /// <summary>Things that live with the move (beams it fires, held bodies) and die with it.</summary>
        private readonly List<IOwned> _owned = new List<IOwned>(2);

        /// <summary>The last blow's result (for hooks).</summary>
        public HitResult LastHit;

        /// <summary>Is the performer on screen (effects are drawn only then)?</summary>
        public bool Vis;

        /// <summary>The height an air strike started at (its poses' heights count from here).</summary>
        public float AirBase;

        protected ChoreoMove(Actor a, Technique t, Plan p) : base(a)
        {
            P = p;
            Tech = t;
            Level = p != null ? Mathf.Clamp(p.Level, 1, 3) : 2;
            Tier = Tiers.Of(a);
            Info = Tiers.Info(Tier);
            School = t != null ? t.School : School.None;
            Scale = t != null ? t.ScaleAt(Level) : Scale.Light;
            Hue = Hue.For(Tier, School, Level);
            Kk = K;
            Hpx = Choreo.SpritePx(a);
            float linger = Mathf.Clamp(Info != null ? Info.Linger : 1f, 0.3f, 1f);
            float size = Mathf.Pow(Mathf.Clamp(Kk, 0.7f, 2f), 0.3f);
            WindK = Mathf.Sqrt(linger) * size;
            RecoverK = linger * size;
            int ti = Mathf.Clamp((int)Tier, 0, 6);
            ActK = (ti <= 1 ? 1f : ti == 2 ? 0.95f : ti == 3 ? 0.9f : ti == 4 ? 0.82f : ti == 5 ? 0.76f : 0.7f) * size;
            ImpactK = ti <= 1 ? 1f : ti == 2 ? 1.05f : ti == 3 ? 1.15f : ti == 4 ? 1.3f : ti == 5 ? 1.45f : 1.6f;
            Armed = Choreo.ArmedOf(a);
            Forced = p != null && p.Data is bool f && f;
            Stance = Pose.Ground;
            Fwd = Pose.FaceRight ? 1f : -1f;
            Dir = new Vector2(Fwd, 0f);
            Side = Choreo.Perp(Dir);
            if (p != null)
            {
                Point = p.Point;
                Aim = p.Dir.sqrMagnitude > 1e-6f ? p.Dir.normalized : Dir;
                if (p.Target != null)
                {
                    SetVictim(p.Target);
                }
                else if ((p.Point - Pose.Ground).sqrMagnitude > 0.01f)
                {
                    Face(p.Point - Pose.Ground);
                }
            }
        }

        /// <summary>
        /// A new choreography for technique 't' on plan 'p' (its target, point, level), or null if the
        /// performer can't act now. Build it, then Start() it.
        /// </summary>
        public static ChoreoMove Make(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new ChoreoMove(a, t, p);
        }

        /// <summary>
        /// A choreography for a reactive technique's answer (React): 'defender' answers 'attacker' (its
        /// target) with technique 't' at 'level'. Null if the defender can't act now. Build it, Start()
        /// it, and return Parried or Evaded from React only if Start() succeeded.
        /// </summary>
        public static ChoreoMove Answer(Actor defender, Actor attacker, Technique t, int level)
        {
            if (defender == null || attacker == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true   // (an answer reaches like a forced plan: the attacker came to it)
            };
            return new ChoreoMove(defender, t, p);
        }

        /// <summary>Starts the move (false if the unit couldn't). Call it last in Begin.</summary>
        public bool Start()
        {
            if (_beats.Count == 0 || !Moves.CanPerform(A))
            {
                return false;
            }
            if (_beats[_beats.Count - 1].Recovery == false && !HasRecovery())
            {
                Recover();
            }
            if (!Moves.Start(this))
            {
                return false;
            }
            _started = true;
            WatchMe();
            if (Dev.On) Dev.Trace("choreo " + (Tech != null ? Tech.Id : GetType().Name) + " " + Techniques.Roman(Level) + " (" + Tier + ") by " + A.asset.id + "#" + Id
                + (V != null ? " on " + V.asset.id + "#" + VId + " hp " + V.getHealth() : "") + ": " + _beats.Count + " beats");
            return true;
        }

        private bool HasRecovery()
        {
            foreach (Beat b in _beats)
            {
                if (b.Recovery)
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------- the move --

        /// <summary>(A blow answered during an untouchable beat makes it solid again: the counter's blow lands while it reels.)</summary>
        public override bool Intangible => !_broken && _answer == 0 && _b != null && _b.Intangible;

        /// <summary>
        /// Once its blow was answered (parried: it reels; evaded: it overreaches) the move has failed: a
        /// counter's blow may take the unit out of it at once (it reacts to that blow, not just flashes).
        /// </summary>
        public override bool Interruptible => _answer != 0 && !_broken;

        public override bool Cinematic => _cine && !Ended;

        public override bool Charging => !_broken && !Finished && _b != null && _b.Charging;

        /// <summary>In its recovery beat, or reeling from an answered blow: a punish window (a counter to a counter may cut it short).</summary>
        public override bool Recovering => !_broken && !Finished && (_answer != 0 || _b != null && _b.Recovery);

        /// <summary>
        /// What it shows: the beat's own Forms (Shows; a feint may show Charge on purpose), else the
        /// technique's at this level (FormsAt) without Charge; Charge whenever it is really charging.
        /// </summary>
        public override Form Forms
        {
            get
            {
                Form f = Tech != null ? Tech.FormsAt(Level) & ~Form.Charge : Form.None;
                if (_b != null && _b.Forms != Form.None)
                {
                    f = _b.Forms;
                }
                if (Charging)
                {
                    f |= Form.Charge;
                }
                return f;
            }
        }

        /// <summary>The layer for effects over everything (above a cinematic's veil while it plays one).</summary>
        public FxLayer TopLayer => _cine ? FxLayer.Cine : FxLayer.Top;

        // -------------------------------------------------------- the builder --

        /// <summary>The beat added last (to adjust it).</summary>
        public Beat Last => _beats.Count > 0 ? _beats[_beats.Count - 1] : null;

        /// <summary>Adds a beat (the fluent methods all come here).</summary>
        public ChoreoMove Add(Beat b)
        {
            _beats.Add(b);
            return this;
        }

        /// <summary>A beat that eases into 'to' over 'dur' seconds (tempo 't').</summary>
        public ChoreoMove PoseTo(float dur, Shape to, Ease e = Ease.OutQuad, Tempo t = Tempo.Free)
        {
            return Add(new Beat { Dur = dur, To = to, Ease = e, Tempo = t });
        }

        /// <summary>Holds the current pose for 'dur' seconds (with a pixel's tremble: 'tremble').</summary>
        public ChoreoMove Hold(float dur, bool tremble = false, Tempo t = Tempo.Free)
        {
            return Add(new Beat { Dur = dur, Tremble = tremble, Tempo = t });
        }

        /// <summary>Runs 'a' at this point of the script (no time passes).</summary>
        public ChoreoMove Do(Action<ChoreoMove> a)
        {
            return Add(new Beat { Dur = 0f, Enter = a });
        }

        /// <summary>Runs 'a' here, even when the target was lost before it (see Beat.Keep).</summary>
        public ChoreoMove KeepDo(Action<ChoreoMove> a)
        {
            return Add(new Beat { Dur = 0f, Enter = a, Keep = true });
        }

        /// <summary>Holds until 'until' is true (at most 'max' seconds).</summary>
        public ChoreoMove WaitUntil(Func<ChoreoMove, bool> until, float max = 3f)
        {
            return Add(new Beat { Dur = 0f, Until = until, MaxDur = max });
        }

        /// <summary>Names the last beat (Jump goes there).</summary>
        public ChoreoMove Label(string name)
        {
            if (Last != null)
            {
                Last.Label = name;
            }
            return this;
        }

        /// <summary>The last beat plays only if 'cond' holds when its turn comes.</summary>
        public ChoreoMove OnlyIf(Func<ChoreoMove, bool> cond)
        {
            if (Last != null)
            {
                Last.If = cond;
            }
            return this;
        }

        /// <summary>
        /// A group of beats that plays only if 'cond' holds when the group's turn comes: 'group' adds them
        /// (m.OnlyIf(m => m.Level >= 3, g => g.Strike(Blow.Hook, 1f).Strike(Blow.Uppercut, 1.4f))). 'cond'
        /// is asked as the group's first beat comes up; false skips the whole group. Groups nest; a beat's
        /// own OnlyIf inside still applies.
        /// </summary>
        public ChoreoMove OnlyIf(Func<ChoreoMove, bool> cond, Action<ChoreoMove> group)
        {
            int first = _beats.Count;
            group?.Invoke(this);
            return OnlyIfFrom(first, cond);
        }

        /// <summary>How many beats the script has so far (OnlyIfFrom's mark).</summary>
        public int BeatCount => _beats.Count;

        /// <summary>
        /// Every beat added since 'first' (a BeatCount taken before them) plays only if 'cond' holds when
        /// the first of them comes up (see OnlyIf with a group).
        /// </summary>
        public ChoreoMove OnlyIfFrom(int first, Func<ChoreoMove, bool> cond)
        {
            int last = _beats.Count - 1;
            if (cond == null || first < 0 || first > last)
            {
                return this;
            }
            BeatGroup g = new BeatGroup { Cond = cond, First = first, Last = last };
            for (int i = first; i <= last; i++)
            {
                Beat b = _beats[i];
                if (b.Group == null)
                {
                    b.Group = g;
                    continue;
                }
                // (already in a group of its own: this one encloses that one)
                BeatGroup x = b.Group;
                while (x.Outer != null && x.Outer != g)
                {
                    x = x.Outer;
                }
                if (x != g)
                {
                    x.Outer = g;
                }
            }
            return this;
        }

        /// <summary>Will beat 'b' play when its turn comes (its own OnlyIf and every group it is in)?</summary>
        private bool Plays(Beat b)
        {
            if (b.If != null && !b.If(this))
            {
                return false;
            }
            for (BeatGroup g = b.Group; g != null; g = g.Outer)
            {
                if (!g.Plays(this))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Runs 'a' every frame of the last beat (k: its progress 0..1).</summary>
        public ChoreoMove Every(Action<ChoreoMove, float> a)
        {
            if (Last != null)
            {
                Action<ChoreoMove, float> was = Last.Every;
                Last.Every = was == null ? a : (m, k) =>
                {
                    was(m, k);
                    a(m, k);
                };
            }
            return this;
        }

        /// <summary>Runs 'a' at point 'at' (0..1) of the last beat.</summary>
        public ChoreoMove At(float at, Action<ChoreoMove> a)
        {
            Last?.On(at, a);
            return this;
        }

        /// <summary>Runs 'a' when the last beat starts.</summary>
        public ChoreoMove OnEnter(Action<ChoreoMove> a)
        {
            if (Last != null)
            {
                Action<ChoreoMove> was = Last.Enter;
                Last.Enter = was == null ? a : m =>
                {
                    was(m);
                    a(m);
                };
            }
            return this;
        }

        /// <summary>The last beat shows these Forms to readers (a feint shows Charge while it winds).</summary>
        public ChoreoMove Shows(Form f)
        {
            if (Last != null)
            {
                Last.Forms = f;
            }
            return this;
        }

        /// <summary>The last beat is a wind-up: a fast blow now breaks it (Choreo.Break).</summary>
        public ChoreoMove AsCharge()
        {
            if (Last != null)
            {
                Last.Charging = true;
            }
            return this;
        }

        /// <summary>Nothing can touch it during the last beat (a blur, a blink).</summary>
        public ChoreoMove Untouchable()
        {
            if (Last != null)
            {
                Last.Intangible = true;
            }
            return this;
        }

        /// <summary>Afterimages every 'every' seconds during the last beat (speed lines too: 'lines').</summary>
        public ChoreoMove Ghosting(float every = 0.03f, bool lines = false)
        {
            if (Last != null)
            {
                Last.Ghosts = every;
                Last.Lines = lines ? Mathf.Max(every, 0.03f) : 0f;
            }
            return this;
        }

        /// <summary>Dust kicked up at its heels during the last beat.</summary>
        public ChoreoMove Dusting(float every = 0.05f)
        {
            if (Last != null)
            {
                Last.Dust = every;
            }
            return this;
        }

        /// <summary>The chain the move leaves when it ends well (the last blow's reaction sets it too).</summary>
        public ChoreoMove Leaves(Link link)
        {
            return Do(m => m.SetLink(link, m.V));
        }

        /// <summary>
        /// The recovery: springs back upright (overshooting a little) at the tier's pace, settles, and the
        /// move ends. Added automatically if a script has none. 'dur': seconds at Novice pace.
        /// </summary>
        public ChoreoMove Recover(float dur = 0.22f)
        {
            Beat b = new Beat { Dur = dur, Tempo = Tempo.Recover, Recovery = true, Track = false };
            b.Enter = m => m.EnterRecover();
            b.Every = (m, k) => m.TickRecover(k);
            b.Exit = m => m.ExitRecover();
            return Add(b);
        }

        // ------------------------------------------------------------ victims --

        /// <summary>Dev captures frame the performer and its target (or its point).</summary>
        public void WatchMe()
        {
            if (!Dev.On)
            {
                return;
            }
            Vector2 other = V != null && V.Sid() == VId ? (Vector2)Choreo.Feet(V) : Point;
            if ((other - Pose.Ground).sqrMagnitude > 400f)
            {
                other = Pose.Ground;
            }
            Dev.Watch(Vector2.Lerp(Pose.Ground, other, 0.5f) + new Vector2(0f, 1.2f * Kk));
        }

        /// <summary>Makes 'v' the target (its size, class share, where it stands, how far to strike from).</summary>
        public void SetVictim(Actor v)
        {
            if (v == null)
            {
                V = null;
                VId = -1;
                return;
            }
            V = v;
            VId = v.Sid();
            VClass = Weights.Of(v);
            // (a class above its limits only takes glancing blows: an answer to a heavier attacker)
            VAllowed = Choreo.ClassShare(A, v, this, out Cm);
            VPx = Choreo.PxOf(v);
            VHpx = Choreo.SpritePx(v);
            VG = Victims.GroundOf(v);
            float ratio = VHpx * VPx / Mathf.Max(0.2f, Hpx * A.actor_scale);
            Stand = ((Armed ? 0.66f : 0.58f) + 0.3f * Mathf.Max(0f, ratio - 1f)) * Kk;
            Face(VG - Stance);
        }

        /// <summary>
        /// Is the target still there to strike (alive, the same unit, still an enemy, not in the god's hand
        /// or someone else's move)? One that stopped being an enemy (its city taken, a peace) is lost.
        /// </summary>
        public bool VOk
        {
            get
            {
                if (V == null || V.Sid() != VId || !Combat.Living(V) || V.isInMagnet() || V.isInsideSomething() || !Choreo.Hostile(A, V))
                {
                    return false;
                }
                return Choreo.Hittable(V) || Held != null && Held.A == V;
            }
        }

        /// <summary>A point on the target's body, 'frac' of its height up (0 feet .. 1 head), where it is drawn now.</summary>
        public Vector2 VPoint(float frac)
        {
            if (V == null || V.Sid() != VId)
            {
                return BodyPos() + Dir * Stand;
            }
            return Choreo.BodyPoint(V, frac, VHpx * VPx);
        }

        /// <summary>The target's height in world units.</summary>
        public float VHeight => VHpx * VPx;

        /// <summary>
        /// Its target is gone: the nearest fair foe within 'r' tiles of where it was, no heavier, takes its
        /// place (once per move). False if there is none.
        /// </summary>
        public bool Retarget(float r = 2.2f)
        {
            if (_retargets >= 1)
            {
                return false;
            }
            Actor best = null;
            float bd = float.MaxValue;
            List<Actor> near = _tmp;
            Choreo.Foes(A, VG, r, near);
            for (int i = 0; i < near.Count; i++)
            {
                Actor u = near[i];
                if (u == V || Weights.Of(u) > VClass || !Choreo.Fair(A, u, out _, Forced))
                {
                    continue;
                }
                float d = (u.current_position - Stance).sqrMagnitude;
                if (d < bd && Combat.ClearPath(Stance, u.current_position))
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
            if (Dev.On) Dev.Trace("choreo " + Id + " retargets " + best.asset.id + "#" + best.Sid());
            SetVictim(best);
            return true;
        }

        private static readonly List<Actor> _tmp = new List<Actor>();

        /// <summary>Nothing left to strike: it jumps to its recovery and ends on a pose with a glint.</summary>
        public void Lose()
        {
            if (_lost)
            {
                return;
            }
            if (Dev.On && Tech != null)
            {
                bool dead = V == null || V.Sid() != VId || !Combat.Living(V);
                Dev.Trace("audit lose " + Tech.Id + " " + (dead ? "dead" : "away d=" + Vector2.Distance(Pose.Ground, VG).ToString("F1")) + " t=" + T.ToString("F2"));
            }
            _lost = true;
            _glintEnd = true;
            _link = Link.None;
            _linkTarget = null;
            _linkTargetId = -1;
            JumpToRecovery();
        }

        /// <summary>The target is lost (no retarget was possible).</summary>
        public bool Lost => _lost;

        /// <summary>Where it strikes from: 'stand' tiles in front of the target (the nearest solid side of it).</summary>
        public Vector2 StandSpot(float stand)
        {
            Vector2 d = VG - Stance;
            Vector2 back = d.sqrMagnitude > 1e-4f ? -d.normalized : -Dir;
            Vector2 s = Combat.Clamp(VG + back * stand);
            if (Combat.SafeSpot(s) || Choreo.Wading(s))
            {
                return s;
            }
            float baseAng = Mathf.Atan2(back.y, back.x);
            for (int i = 1; i <= 4; i++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float a = baseAng + sgn * i * 0.6f;
                    Vector2 q = Combat.Clamp(VG + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * stand);
                    if (Combat.SafeSpot(q))
                    {
                        return q;
                    }
                }
            }
            return Stance;
        }

        /// <summary>
        /// The first time this move reaches 'v': it may answer (parry, evade; Combat.Reach). True if the
        /// blow lands. A parried or evaded main target ends the move in a recoil or a whiff; an area
        /// blow (area: true) just skips one that slipped it.
        /// </summary>
        public bool Touch(Actor v, bool area = false)
        {
            if (v == null || !Combat.Living(v) || !Choreo.Hostile(A, v))
            {
                return false;   // (no longer an enemy: no blow of the move lands on it)
            }
            long id = v.Sid();
            if (_reached.Contains(id))
            {
                return !Moves.Intangible(v);
            }
            _reached.Add(id);
            Contact c = Combat.Reach(this, v, area);
            if (c == Contact.Hit)
            {
                return true;
            }
            if (!area && v == V)
            {
                if (c == Contact.Parried)
                {
                    Answered(true);
                }
                else
                {
                    Answered(false);
                }
            }
            if (Dev.On) Dev.Trace("choreo " + Id + " " + c + " by " + v.asset.id + "#" + id);
            return false;
        }

        // -------------------------------------------------------------- links --

        /// <summary>The chain this move leaves (if it ends well) and on whom.</summary>
        public void SetLink(Link link, Actor on)
        {
            _link = link;
            _linkTarget = on;
            _linkTargetId = on.Sid();
        }

        // -------------------------------------------------------------- owned --

        /// <summary>Keeps a held particle alive with the move: it dies when the move ends.</summary>
        public Particle Glue(Particle p)
        {
            if (p != null)
            {
                p.Held = true;
                p.Cine = p.Cine || _cine;
                _glued.Add(p);
                _gluedGen.Add(p.Gen);
            }
            return p;
        }

        /// <summary>Kills a glued particle now.</summary>
        public void Unglue(Particle p)
        {
            for (int i = _glued.Count - 1; i >= 0; i--)
            {
                if (_glued[i] == p)
                {
                    if (p.Alive && p.Gen == _gluedGen[i])
                    {
                        p.Kill();
                    }
                    _glued.RemoveAt(i);
                    _gluedGen.RemoveAt(i);
                }
            }
        }

        /// <summary>Something that lives with the move (a beam, a held body) and is let go when it ends.</summary>
        public void Own(IOwned o)
        {
            if (o != null && !_owned.Contains(o))
            {
                _owned.Add(o);
            }
        }

        public void Disown(IOwned o)
        {
            _owned.Remove(o);
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            Vis = Fx.OnScreen(Pose.Ground, 0.35f);
            if (Held != null)
            {
                CheckHeld();
                if (Finished)
                {
                    return;
                }
            }
            if (_broken)
            {
                TickBroken(dt);
                return;
            }
            if (_answer != 0)
            {
                TickAnswer(dt);
                return;
            }
            if (_hold > 0f)
            {
                _hold -= dt;
                Pose.Nudge = Choreo.Tremble(_hold);
                if (_hold <= 0f)
                {
                    Pose.Nudge = Vector2.zero;
                }
                return;
            }
            if (_bi < 0)
            {
                NextBeat();
                if (Finished || _b == null)
                {
                    return;
                }
            }
            if (T > 25f)
            {
                Finish();   // never hold on to a unit, whatever happened
                return;
            }
            Beat b = _b;
            Track(b);
            _bt += dt;
            float k = BeatK;
            ApplyPose(b, k);
            if (b.Every != null)
            {
                b.Every(this, k);
                if (Finished || _b != b || _broken || _answer != 0)
                {
                    return;
                }
            }
            if (b.Cues != null)
            {
                for (int i = 0; i < b.Cues.Count; i++)
                {
                    Cue c = b.Cues[i];
                    if (c.Fired || k < c.At)
                    {
                        continue;
                    }
                    c.Fired = true;
                    c.Do(this);
                    if (Finished || _b != b || _broken || _answer != 0)
                    {
                        return;
                    }
                }
            }
            BeatFx(b, dt);
            if (Held != null)
            {
                Held.Attach(this, _grip);
            }
            if (_hold > 0f)
            {
                return;   // (a blow on this frame holds the pose: the beat goes on after the hold)
            }
            bool done = k >= 1f && (b.Until == null || b.Until(this) || _bt >= b.MaxDur);
            if (done)
            {
                NextBeat();
            }
        }

        private void NextBeat()
        {
            if (_b != null && _b.Exit != null)
            {
                Beat was = _b;
                _b.Exit(this);
                if (Finished || _broken || _answer != 0 || _b != was)
                {
                    return;
                }
            }
            int guard = 0;
            while (guard++ < 64)
            {
                _bi++;
                if (_bi >= _beats.Count)
                {
                    Finish();
                    return;
                }
                Beat b = _beats[_bi];
                // (a group is decided at its first beat, whatever that beat's own OnlyIf says)
                if (b.Group != null && SkipGroups(b))
                {
                    continue;
                }
                if (b.If != null && !b.If(this))
                {
                    continue;
                }
                if (b.NeedsVictim && !VOk)
                {
                    if (_lost)
                    {
                        continue;   // lost earlier: its blows are skipped, only the kept effects play on
                    }
                    if (!Retarget())
                    {
                        Lose();
                        return;
                    }
                }
                EnterBeat(b);
                if (Finished || _broken || _answer != 0 || _b != b)
                {
                    return;
                }
                if (b.Dur <= 0f && b.Until == null && b.Every == null && b.Cues == null)
                {
                    // an instant beat: its Enter ran, on to the next one this same frame
                    if (b.Exit != null)
                    {
                        b.Exit(this);
                        if (Finished || _broken || _answer != 0 || _b != b)
                        {
                            return;
                        }
                    }
                    continue;
                }
                return;
            }
        }

        /// <summary>
        /// A beat that opens a group (or several) whose condition fails: the play skips past the end of
        /// the largest such group. True if it skipped.
        /// </summary>
        private bool SkipGroups(Beat b)
        {
            int to = -1;
            for (BeatGroup g = b.Group; g != null; g = g.Outer)
            {
                if (g.First != _bi)
                {
                    continue;
                }
                // (decided here, each time its first beat comes up (a Jump may loop back): IsFinal asks a
                // group already playing what it decided, not its condition again)
                g.State = g.Cond(this) ? 1 : -1;
                if (g.State < 0 && g.Last > to)
                {
                    to = g.Last;
                }
            }
            if (to < 0)
            {
                return false;
            }
            _bi = to;   // (the loop's _bi++ goes on after the group)
            return true;
        }

        private void EnterBeat(Beat b)
        {
            _b = b;
            _bt = 0f;
            _s0 = new Shape(Pose.Rot, Pose.SX, Pose.SY, Pose.Height, _lunge, _side);
            _lunge0 = _lunge;
            _side0 = _side;
            _g0 = Stance;
            _dest = Stance;
            _ghostT = _lineT = _dustT = 0f;
            if (b.Cues != null)
            {
                for (int i = 0; i < b.Cues.Count; i++)
                {
                    b.Cues[i].Fired = false;
                }
            }
            if (b.Grip.HasValue)
            {
                _grip0 = _grip;
            }
            BeatDur = b.Dur * TempoOf(b.Tempo);
            if (b.Route != Route.None && b.Dest != null)
            {
                _dest = b.Dest(this);
                if (b.PerTile > 0f)
                {
                    BeatDur += b.PerTile * Vector2.Distance(_g0, _dest) * TempoOf(b.Tempo);
                }
            }
            Pose.Hidden = b.Hidden;
            Pose.HideShadow = b.HideShadow || b.Hidden;
            b.Enter?.Invoke(this);
        }

        /// <summary>A beat's authored seconds at this tier.</summary>
        public float TempoOf(Tempo t)
        {
            switch (t)
            {
                case Tempo.Wind:
                case Tempo.Follow:
                    return WindK;
                case Tempo.Act:
                case Tempo.Travel:
                    return ActK;
                case Tempo.Recover:
                    return RecoverK;
                case Tempo.Impact:
                    return ImpactK;
                default:
                    return 1f;
            }
        }

        /// <summary>Goes to the beat named 'label' now (false if there is none).</summary>
        public bool Jump(string label)
        {
            for (int i = 0; i < _beats.Count; i++)
            {
                if (_beats[i].Label == label)
                {
                    // (groups from there on are decided anew as they come up again)
                    for (int j = i; j < _beats.Count; j++)
                    {
                        for (BeatGroup g = _beats[j].Group; g != null; g = g.Outer)
                        {
                            if (g.First >= i)
                            {
                                g.State = 0;
                            }
                        }
                    }
                    _bi = i - 1;
                    _b = null;
                    NextBeat();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Skips to the first recovery beat after this one (or ends).</summary>
        public void JumpToRecovery()
        {
            for (int i = Mathf.Max(0, _bi + 1); i < _beats.Count; i++)
            {
                if (_beats[i].Recovery || _beats[i].Keep)
                {
                    _bi = i - 1;
                    _b = null;
                    NextBeat();
                    return;
                }
            }
            if (_b != null && _b.Recovery)
            {
                return;
            }
            Finish();
        }

        /// <summary>Holds the pose for a few hundredths, trembling (the blow's local hit-stop).</summary>
        public void HoldFor(float seconds)
        {
            _hold = Mathf.Max(_hold, seconds);
        }

        // --------------------------------------------------------------- pose --

        private void Track(Beat b)
        {
            if (V != null && V.Sid() == VId && V.isAlive())
            {
                VG = Victims.GroundOf(V);
                // worked over: no swings of its own while the move is on it (it can still answer the move itself)
                if (!b.Recovery && !_lost && !ControllableUnit.isControllingUnit(V))
                {
                    Combat.Pin(V, 0.3f);
                }
            }
            if (!b.Track || V == null || Held != null)
            {
                return;   // (a held body is where its hands put it: it doesn't steer them)
            }
            Vector2 d = VG - Stance;
            if (d.sqrMagnitude > 0.0025f)
            {
                Face(d);
            }
        }

        /// <summary>Turns to face along 'd' (facing flips only when it points clearly left or right).</summary>
        public void Face(Vector2 d)
        {
            if (d.sqrMagnitude < 1e-6f)
            {
                return;
            }
            Dir = d.normalized;
            Side = Choreo.Perp(Dir);
            if (Mathf.Abs(Dir.x) > 0.12f)
            {
                Fwd = Mathf.Sign(Dir.x);
            }
            Pose.FaceRight = Fwd > 0f;
        }

        /// <summary>
        /// A blow's height follows its target's body: an air blow rises with a launched body and comes
        /// down onto one that has fallen, a ground blow at a body hanging in the air jumps to meet it. The
        /// performer's limb is brought level with the point the blow lands on (smoothly, frame by frame).
        /// True when the beat's heights count from AirBase (an air blow, or a body up in the air).
        /// </summary>
        private bool TrackHeight(BlowShape s, bool air)
        {
            if (V == null || !VOk)
            {
                return air;
            }
            float vh = Mathf.Max(0f, Choreo.Feet(V).y - VG.y);
            bool aloft = vh > Mathf.Max(0.3f * VHeight, 0.25f);
            if (!air && !aloft)
            {
                return false;
            }
            // body to body: its middle level with the point the blow lands on, leading a body that rises or
            // falls by its speed (so it never trails half a body under one dropping out of a juggle)
            float dtv = Mathf.Max(1e-3f, Time.deltaTime);
            float vvel = _trackSet ? (vh - _trackVh) / dtv : 0f;
            _trackVh = vh;
            _trackSet = true;
            float want = Mathf.Max(0f, VPoint(s.Contact).y - VG.y - 0.5f * Hpx * Px + Mathf.Clamp(vvel * 0.08f, -0.6f, 0.6f));
            AirBase = Mathf.Lerp(AirBase, want, 0.5f);
            return true;
        }

        // ---------------------------------------------------------------- audit --

        private float _trackVh;
        private bool _trackSet;

        /// <summary>Blows landed / thrown at nothing this move, and the blow the main target died of (dev audit).</summary>
        internal int AuditHits, AuditWhiffs, AuditVert, AuditDiedAt = -1;

        /// <summary>Dev: one line per blow that arrives (how it met the target), for the coherence audit.</summary>
        internal void Audit(string what, Hit h, BlowShape s, float gapY, float dist)
        {
            if (what == "hit")
            {
                AuditHits++;
            }
            else if (what == "vertwhiff")
            {
                AuditVert++;
            }
            else
            {
                AuditWhiffs++;
            }
            if (!Dev.On)
            {
                return;
            }
            float vh = V != null && VOk ? Mathf.Max(0f, Choreo.Feet(V).y - VG.y) : -1f;
            Dev.Trace("audit " + (Tech != null ? Tech.Id : GetType().Name) + " " + h.Blow + (h.Air ? " air" : "") + " " + what
                + " ph=" + Pose.Height.ToString("F2") + " vh=" + vh.ToString("F2") + " gapY=" + gapY.ToString("F2") + " d=" + dist.ToString("F2")
                + " vH=" + VHeight.ToString("F2"));
        }

        private void ApplyPose(Beat b, float k)
        {
            float e = Beat.Apply(b.Ease, k);
            Shape to = b.To;
            Pose.Rot = float.IsNaN(to.Rot) ? _s0.Rot : Mathf.LerpUnclamped(_s0.Rot, to.Rot, e);
            if (b.Spin != 0f)
            {
                Pose.Rot = _s0.Rot + b.Spin * Beat.Apply(Ease.InOutSine, k) + (float.IsNaN(to.Rot) ? 0f : (to.Rot - _s0.Rot) * e);
            }
            Pose.SX = float.IsNaN(to.SX) ? _s0.SX : Mathf.LerpUnclamped(_s0.SX, to.SX, e);
            Pose.SY = float.IsNaN(to.SY) ? _s0.SY : Mathf.LerpUnclamped(_s0.SY, to.SY, e);
            _lunge = float.IsNaN(to.Lunge) ? _lunge0 : Mathf.LerpUnclamped(_lunge0, to.Lunge, e);
            _side = float.IsNaN(to.Side) ? _side0 : Mathf.LerpUnclamped(_side0, to.Side, e);
            bool rel = b.RelH;
            if (b.Blow != null && TrackHeight(b.Blow, b.RelH))
            {
                rel = true;
            }
            float hTo = float.IsNaN(to.H) ? _s0.H : (rel ? AirBase : 0f) + to.H * Kk;
            float h = Mathf.LerpUnclamped(_s0.H, hTo, e);
            if (b.Chase && b.Dest != null && b.Route != Route.None && k < 1f)
            {
                _dest = b.Dest(this);
            }
            switch (b.Route)
            {
                case Route.Line:
                    Stance = Vector2.Lerp(_g0, _dest, Beat.Apply(b.PathEase, k));
                    break;
                case Route.Arc:
                    {
                        float u = Beat.Apply(b.PathEase, k);
                        Stance = Vector2.Lerp(_g0, _dest, u);
                        float endH = float.IsNaN(to.H) ? 0f : (rel ? AirBase : 0f) + to.H * Kk;
                        h = Mathf.Lerp(_s0.H, endH, k) + b.Apex * Kk * 4f * k * (1f - k);
                        break;
                    }
                case Route.Blink:
                    Stance = k < 1f ? _g0 : _dest;
                    Pose.Hidden = k > 0.02f && k < 1f;
                    Pose.HideShadow = Pose.Hidden;
                    break;
                case Route.Orbit:
                    Stance = OrbitAt(k);
                    break;
            }
            Pose.Height = Mathf.Max(0f, h);
            Vector2 g = Stance + Dir * _lunge * Kk + Side * _side * Kk;
            Pose.Ground = Mathf.Abs(_lunge) + Mathf.Abs(_side) < 0.01f || Combat.SafeSpot(g) || Choreo.Wading(g) ? g : Stance;
            Pose.Nudge = b.Tremble ? Choreo.Tremble(_bt) : Vector2.zero;
            if (b.Grip.HasValue)
            {
                _grip = Grip.Lerp(_grip0, b.Grip.Value, e);
            }
        }

        // the orbit of an Orbit beat: round 'OrbitCenter' from where it stood, 'OrbitTurn' radians
        public Vector2 OrbitCenter;
        public float OrbitTurn = Mathf.PI * 2f, OrbitR;

        private Vector2 OrbitAt(float k)
        {
            Vector2 rel = _g0 - OrbitCenter;
            float r = OrbitR > 0f ? OrbitR : Mathf.Max(0.3f, rel.magnitude);
            float th0 = Mathf.Atan2(rel.y / Art.Ground, rel.x);
            float th = th0 + OrbitTurn * Beat.Apply(Ease.InOutSine, k);
            Vector2 p = OrbitCenter + new Vector2(Mathf.Cos(th), Mathf.Sin(th) * Art.Ground) * r;
            return Vector2.Lerp(p, _dest, Anim.Smooth(0.8f, 1f, k));
        }

        // --------------------------------------------------------- beat fx --

        private void BeatFx(Beat b, float dt)
        {
            if (!Vis)
            {
                return;
            }
            float px = Px;
            if (b.Ghosts > 0f && !Pose.Hidden)
            {
                _ghostT -= dt;
                if (_ghostT <= 0f)
                {
                    _ghostT = b.Ghosts;
                    Fx.Afterimage(this, 0.12f, Fx.WithA(Hue.Ghost, Hue.Ghost.a * 0.8f), Fx.WithA(Hue.Edge, 0.45f), Hue.GhostEnd);
                }
            }
            if (b.Lines > 0f && !Pose.Hidden)
            {
                _lineT -= dt;
                Vector2 v = _dest - _g0;
                if (_lineT <= 0f && v.sqrMagnitude > 0.01f)
                {
                    _lineT = b.Lines;
                    Kit.SpeedLines(BodyPos() - v.normalized * 3f * px, v, px, 2, 3f, 0.12f, Fx.WithA(Hue.Edge, 0.8f));
                }
            }
            if (b.Dust > 0f && Pose.Height < 0.1f && !Pose.Hidden)
            {
                _dustT -= dt;
                if (_dustT <= 0f)
                {
                    _dustT = b.Dust;
                    Vector2 back = _g0 - _dest;
                    ChoreoFx.HeelDust(Pose.Ground, back.sqrMagnitude > 1e-4f ? back : -Dir, px, A.current_tile, Kk);
                }
            }
        }

        // ------------------------------------------------------------- recover --

        private float _rotFrom, _sxFrom = 1f, _syFrom = 1f, _hFrom;

        private void EnterRecover()
        {
            _rotFrom = Mathf.DeltaAngle(0f, Pose.Rot);
            _sxFrom = Pose.SX;
            _syFrom = Pose.SY;
            _hFrom = Pose.Height;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (Held != null)
            {
                ReleaseHeld(Hurt.Knockdown, 0.6f);
            }
            SpinDone();
            if (Vis && _glintEnd)
            {
                Fx.Glint(Hand(Limb.Fist) + new Vector2(0f, 1.5f * Px), Px, 0.2f, Hue.Core, 0.9f);
            }
            if (_hFrom > 0.15f * Kk)
            {
                _landPuff = true;
                // still up in the air: it needs the time to come down
                BeatDur = Mathf.Max(BeatDur, (0.14f + 0.07f * _hFrom / Kk) / 0.62f);
            }
        }

        private bool _landPuff;

        private void TickRecover(float k)
        {
            float st = BeatT / Mathf.Max(0.3f, RecoverK);
            float amp = Anim.Spring(st, 9f, 18f);
            Pose.SX = 1f + (_sxFrom - 1f) * amp;
            Pose.SY = 1f + (_syFrom - 1f) * amp;
            Pose.Rot = _rotFrom * Mathf.Exp(-10f * st);
            Pose.Height = Mathf.Lerp(_hFrom, 0f, Beat.Apply(Ease.InQuad, Mathf.Clamp01(k * 1.6f)));
            _lunge = Mathf.Lerp(_lunge, 0f, Mathf.Clamp01(k * 2f));
            _side = Mathf.Lerp(_side, 0f, Mathf.Clamp01(k * 2f));
            if (_landPuff && Pose.Height <= 0.01f)
            {
                _landPuff = false;
                if (Vis)
                {
                    Fx.LandingPuff(Pose.Ground, Px, A.current_tile);
                }
            }
        }

        private void ExitRecover()
        {
            Neutral();
        }

        /// <summary>Upright, as drawn, on safe ground.</summary>
        public void Neutral()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            Stance = Pose.Ground;
            _lunge = _side = 0f;
            if (!Combat.SafeSpot(Pose.Ground) && !Choreo.Wading(Pose.Ground) && Combat.FindLanding(Pose.Ground, 2.5f, out Vector2 spot))
            {
                Pose.Ground = spot;
                Stance = spot;
            }
        }

        // -------------------------------------------------------------- endings --

        /// <summary>Ends the move here, upright, leaving its chain (if it ended well).</summary>
        public void Finish()
        {
            if (Finished)
            {
                return;
            }
            Neutral();
            // (a body the last blow killed may have been recycled into a new unit since: never chain onto that)
            if (!_lost && _link != Link.None && (_linkTarget == null || _linkTarget.Sid() == _linkTargetId && Combat.Living(_linkTarget)))
            {
                EndLink = _link;
                EndTarget = _linkTarget;
            }
            if (!Choreo.Flowing(Tier))
            {
                A.startAttackCooldown();
            }
            Cleanup(false);
            Finished = true;
            if (Dev.On) Dev.Trace("choreo " + Id + " ends" + (EndLink != Link.None ? " leaving " + EndLink : "") + " at t=" + T.ToString("F2"));
        }

        public override void Abort()
        {
            Cleanup(true);
        }

        public override void OnEnd(bool aborted)
        {
            if (Dev.On && Tech != null)
            {
                int planned = 0;
                foreach (Beat bt in _beats)
                {
                    if (bt.Tag is Hit && Plays(bt))
                    {
                        planned++;
                    }
                }
                Dev.Trace("audit end " + Tech.Id + " planned=" + planned + " hits=" + AuditHits + " whiffs=" + AuditWhiffs + " vert=" + AuditVert
                    + " lost=" + (_lost ? 1 : 0) + " aborted=" + (aborted ? 1 : 0) + " t=" + T.ToString("F2"));
            }
            Cleanup(aborted);
            Ended2?.Invoke(this, aborted);
        }

        /// <summary>Called once the move has ended (aborted or not): a technique's own cleanup.</summary>
        public Action<ChoreoMove, bool> Ended2;

        private bool _cleaned;

        private void Cleanup(bool aborted)
        {
            if (_cleaned)
            {
                return;
            }
            _cleaned = true;
            try
            {
                if (Held != null)
                {
                    Victim h = Held;
                    Held = null;
                    h.Drop(aborted || Pose.Height > 0.05f);
                }
                for (int i = _owned.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _owned[i].OwnerGone(this, aborted);
                    }
                    catch (Exception e)
                    {
                        SakugaMod.Report("choreo owned", e);
                    }
                }
                _owned.Clear();
                for (int i = 0; i < _glued.Count; i++)
                {
                    Particle p = _glued[i];
                    if (p != null && p.Alive && p.Gen == _gluedGen[i])
                    {
                        p.Kill();
                    }
                }
                _glued.Clear();
                _gluedGen.Clear();
                if (_cine)
                {
                    _cine = false;
                    Cinema.End(this);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("choreo cleanup", e);
            }
        }
    }

    /// <summary>Something a ChoreoMove owns (a beam, a held body): told when its owner ends.</summary>
    internal interface IOwned
    {
        void OwnerGone(ChoreoMove m, bool aborted);
    }
}
