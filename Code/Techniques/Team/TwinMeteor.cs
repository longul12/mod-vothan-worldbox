using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TEAM SYNCS: what the eight team syncs share (Twin Meteor, Earth and Sky, Pincer Beams, Shadow
    /// Formation, Hammer and Anvil, Orb Relay, Spiral Assault, Vanguard Charge). A sync is a few
    /// allies, of any school and any tier, acting as one. Each member plays its own part as a real
    /// Choreo move (SyncMove: its own beats and blows, readable Forms, answerable blows, a charge a
    /// fast blow can break), and a TeamSync binds the parts together:
    ///   THE CALL     the leader's hand goes up: a flare at its feet and a thread of light to each
    ///                friend it calls, who answers with a glint and a nod
    ///   RENDEZVOUS   Meet(cue): whoever is ready first holds its ready pose, breathing, until every
    ///                member still in the sync is there too, so the decisive beats start on the same
    ///                frame (members of different tiers move at different tempos: this evens them)
    ///   UNION        the moment the parts become one: a shared flash where their powers meet, rings
    ///                in every member's colour braided together, a thread from each of them to it,
    ///                a glint on every head, a "ting"
    ///   COLOURS      each member wears its own school's colour on its tier's (a crimson Iron Fist
    ///                and a sky-blue Tempest Leg meteor read as two people in one act)
    ///   SAFETY       a member who dies, is picked up, is broken mid-charge or whose blow was
    ///                answered drops out (In is false): nobody waits for it, and every script carries
    ///                on without it (a solo ending where the union needed it). Nothing outlives the
    ///                moves: the sync's own particles are glued to a member's move or killed when the
    ///                last member ends.
    /// The sync keeps its own clock (move time, held while a team-wide impact freeze plays) for
    /// formations that must move in lockstep, and ticks once a frame for what belongs to no single
    /// member (an orb in flight, a beam knot, a formation's sweep).
    /// </summary>
    internal sealed class TeamSync
    {
        public readonly Technique Tech;
        public readonly int Level;
        public readonly bool Forced;
        /// <summary>How big it is at this level (destruction, shake).</summary>
        public readonly Scale Scale;
        /// <summary>The members: [0] is the leader (the one whose Brain called the sync).</summary>
        public readonly List<SyncMove> Cast = new List<SyncMove>(5);
        /// <summary>The shared target (null for a ground-targeted sync).</summary>
        public readonly Actor V;
        public readonly long VId = -1;
        /// <summary>The planned point and direction.</summary>
        public Vector2 Point, Dir;
        /// <summary>The sync's clock: move seconds since it began, held while Freeze runs.</summary>
        public float Clock;
        /// <summary>A team-wide impact freeze: the clock holds this long (formations hold their places with the blow).</summary>
        public float Freeze;
        /// <summary>The sync is broken (an orb burst in someone's hands): the parts that need it are skipped.</summary>
        public bool Failed;
        /// <summary>Who the current sweep has struck (so the team strikes each foe once a pass).</summary>
        public readonly HashSet<long> Struck = new HashSet<long>();
        /// <summary>What the technique keeps for the whole sync.</summary>
        public object Data;
        /// <summary>Called once a frame: the sync's own work.</summary>
        public Action<TeamSync, float> OnTick;
        /// <summary>Called from each member's Frame (its pose applied, right before drawing): what follows a member's hands.</summary>
        public Action<TeamSync, SyncMove> OnFrame;
        /// <summary>Called once, when every member has ended (or was aborted).</summary>
        public Action<TeamSync> OnClose;

        private readonly List<string> _once = new List<string>(8);
        private readonly List<SyncMove> _gone = new List<SyncMove>(5);
        private int _frame = -1;
        private bool _closed;

        public TeamSync(Technique t, int level, bool forced, Actor v, Vector2 point, Vector2 dir)
        {
            Tech = t;
            Level = Mathf.Clamp(level, 1, 3);
            Forced = forced;
            Scale = t != null ? t.ScaleAt(Level) : Scale.Light;
            V = v;
            VId = v != null ? v.Sid() : -1;
            Point = point;
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
        }

        // ------------------------------------------------------------ members --

        /// <summary>The leader's part.</summary>
        public SyncMove Lead => Cast.Count > 0 ? Cast[0] : null;

        /// <summary>A member's part, or null.</summary>
        public SyncMove At(int slot)
        {
            return slot >= 0 && slot < Cast.Count ? Cast[slot] : null;
        }

        /// <summary>The first member still in the sync (the leader if it is): who does the shared work.</summary>
        public SyncMove First
        {
            get
            {
                for (int i = 0; i < Cast.Count; i++)
                {
                    if (Cast[i].In)
                    {
                        return Cast[i];
                    }
                }
                return null;
            }
        }

        /// <summary>How many members are still in it.</summary>
        public int Live
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Cast.Count; i++)
                {
                    if (Cast[i].In)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        /// <summary>Is this slot's member still in the sync?</summary>
        public bool In(int slot)
        {
            SyncMove m = At(slot);
            return m != null && m.In;
        }

        /// <summary>
        /// A part for 'u': a SyncMove on a plan of its own (the shared target, point and direction), in
        /// its own school's colours. Build its script, then Go().
        /// </summary>
        public SyncMove Join(Actor u, Plan p = null)
        {
            Plan q = p ?? new Plan { Tech = Tech, Level = Level, Target = V, Point = Point, Dir = Dir, Count = 1 };
            q.Tech = Tech;
            q.Level = Level;
            q.Data = Forced;
            SyncMove m = new SyncMove(u, Tech, q, this, Cast.Count);
            Cast.Add(m);
            return m;
        }

        /// <summary>
        /// Starts every part: the partners first, then the leader (all on this frame). A partner that
        /// can't start drops out; fewer than 'need' partners, or a leader that can't start, and the
        /// whole sync is called off (the started parts are cut, nothing is paid). Each partner pays its
        /// 'share' of mana and gets 'cooldown' seconds on the technique (the Brain pays the leader).
        /// </summary>
        public bool Go(int need, float share, float cooldown)
        {
            if (Cast.Count == 0)
            {
                return false;
            }
            int started = 0;
            for (int i = 1; i < Cast.Count; i++)
            {
                SyncMove m = Cast[i];
                if (m.Start())
                {
                    started++;
                }
                else
                {
                    m.Dropped = true;
                }
            }
            if (started < need || !Cast[0].Start())
            {
                for (int i = 1; i < Cast.Count; i++)
                {
                    if (!Cast[i].Dropped)
                    {
                        Choreo.Kill(Cast[i]);
                        Cast[i].Dropped = true;
                    }
                }
                Cast[0].Dropped = true;
                if (Dev.On) Dev.Trace("sync " + Tech.Id + ": called off (" + started + " of " + need + " partners started)");
                return false;
            }
            if (!Forced)
            {
                for (int i = 1; i < Cast.Count; i++)
                {
                    if (!Cast[i].Dropped)
                    {
                        Crew.Pay(Cast[i].A, Tech, share, cooldown);
                    }
                }
            }
            Dev.Watch(Vector2.Lerp(Cast[0].Pose.Ground, VOk ? (Vector2)V.current_position : Point, 0.5f) + new Vector2(0f, 1.2f));
            if (Dev.On) Dev.Trace("sync " + Tech.Id + " " + Techniques.Roman(Level) + " led by " + Cast[0].A.asset.id + "#" + Cast[0].Id + " with " + started + " partner(s)"
                + (VOk ? " on " + V.asset.id + "#" + VId : ""));
            return true;
        }

        // ------------------------------------------------------------- target --

        /// <summary>Is the shared target still there (alive, the same unit, not picked up)?</summary>
        public bool VOk => V != null && V.Sid() == VId && Combat.Living(V) && !V.isInMagnet() && !V.isInsideSomething();

        /// <summary>Where the target stands (its pose's ground while a move moves it), or the planned point.</summary>
        public Vector2 VG => VOk ? Victims.GroundOf(V) : Point;

        // ------------------------------------------------------------- cues --

        /// <summary>Has every member still in the sync reached this cue?</summary>
        public bool All(string cue)
        {
            for (int i = 0; i < Cast.Count; i++)
            {
                SyncMove m = Cast[i];
                if (m.In && !m.Passed(cue))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>True the first time it is asked for this key (a shared moment happens once).</summary>
        public bool Once(string key)
        {
            if (_once.Contains(key))
            {
                return false;
            }
            _once.Add(key);
            return true;
        }

        /// <summary>Has this key's moment happened?</summary>
        public bool Happened(string key)
        {
            return _once.Contains(key);
        }

        // ------------------------------------------------------------- ticks --

        /// <summary>Once a frame (the first member to tick calls it): the clock, the technique's own work.</summary>
        public void FrameTick(float dt)
        {
            int f = Time.frameCount;
            if (f == _frame)
            {
                return;
            }
            _frame = f;
            if (Freeze > 0f)
            {
                Freeze -= dt;
            }
            else
            {
                Clock += dt;
            }
            OnTick?.Invoke(this, dt);
        }

        /// <summary>A member ended (or was aborted): when the last one goes, the sync closes.</summary>
        public void Gone(SyncMove m)
        {
            if (!_gone.Contains(m))
            {
                _gone.Add(m);
            }
            if (_closed)
            {
                return;
            }
            for (int i = 0; i < Cast.Count; i++)
            {
                if (!Cast[i].Dropped && !_gone.Contains(Cast[i]))
                {
                    return;
                }
            }
            _closed = true;
            try
            {
                OnClose?.Invoke(this);
            }
            catch (Exception e)
            {
                SakugaMod.Report("sync close " + (Tech != null ? Tech.Id : "?"), e);
            }
        }

        // ---------------------------------------------------------------- fx --

        /// <summary>The call: a flare at the leader's feet, a glint over its raised hand, a thread to each friend.</summary>
        public void Called()
        {
            SyncMove l = Lead;
            if (l == null)
            {
                return;
            }
            Choreo.Sound("event:/SFX/POWERS/Powerup", l.A.current_tile, 0.3f);
            if (!l.Vis)
            {
                return;
            }
            Kit.Flare(l.Pose.Ground, l.Px, l.Hue.Edge, 15f * l.Kk, 0.35f);
            Fx.Glint(l.HeadPos() + new Vector2(l.Fwd * 1.5f * l.Px, 1.5f * l.Px), l.Px, 0.2f, l.Hue.Core, 1f);
            for (int i = 1; i < Cast.Count; i++)
            {
                SyncMove m = Cast[i];
                if (!m.Dropped)
                {
                    Crew.Link(l.HeadPos(), m.HeadPos(), l.Px, m.Hue.Edge, 0.22f);
                }
            }
        }

        /// <summary>
        /// The moment of union (once per cue): a white flash at 'at', a ring in each member's colour
        /// braided out from it, a thread from every member still in it (unless 'threads' is false), a
        /// glint on each head, a ting. 'size' scales it (1: two fighters' worth). False if it had
        /// already happened.
        /// </summary>
        public bool Union(string cue, Vector2 at, float size = 1f, bool threads = true)
        {
            if (!Once(cue))
            {
                return false;
            }
            SyncMove f = First ?? Lead;
            float px = f != null ? f.Px : 0.1f;
            float k = f != null ? f.Kk : 1f;
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(at), 0.12f);
            if (!Kit.Visible(at))
            {
                return true;
            }
            Kit.Flash(at, px, Mathf.Max(3, Mathf.RoundToInt(4.5f * size * k)), 0.09f, Color.white);
            int n = 0;
            for (int i = 0; i < Cast.Count; i++)
            {
                SyncMove m = Cast[i];
                if (!m.In)
                {
                    continue;
                }
                Fx.Ring(at, px, 0.2f + 0.05f * n, 2f + 2f * n, (11f + 5f * n) * size * k, 2, 1, FxLayer.Top, 6, 1f, 2.4f,
                    Fx.WithA(m.Hue.Core, 0.95f), Fx.WithA(m.Hue.Edge, 0.65f), Fx.WithA(m.Hue.Edge, 0f));
                if (threads)
                {
                    Crew.Link(m.BodyPos(), at, m.Px, m.Hue.Edge, 0.16f);
                }
                Fx.Glint(m.HeadPos() + new Vector2(0f, 1.5f * m.Px), m.Px, 0.16f, m.Hue.Core, 0.85f);
                n++;
            }
            return true;
        }

        /// <summary>A glint on every member's head on the same frame (they set together), once per cue.</summary>
        public void Ting(string cue)
        {
            if (!Once(cue))
            {
                return;
            }
            SyncMove l = First;
            if (l != null)
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", l.A.current_tile, 0.12f);
            }
            for (int i = 0; i < Cast.Count; i++)
            {
                SyncMove m = Cast[i];
                if (m.In && m.Vis)
                {
                    Fx.Glint(m.HeadPos() + new Vector2(0f, 1.5f * m.Px), m.Px, 0.16f, m.Hue.Core, 0.9f);
                }
            }
        }

        /// <summary>A member's edge colour, cycling through the cast (braids).</summary>
        public Color Braid(int i)
        {
            if (Cast.Count == 0)
            {
                return Color.white;
            }
            return Cast[((i % Cast.Count) + Cast.Count) % Cast.Count].Hue.Edge;
        }
    }

    /// <summary>
    /// One member's part in a team sync: a Choreo move that knows its sync (rendezvous, union, the
    /// shared clock), wears its own school's colours, shows Team to readers (and Charge only while it
    /// really gathers power), and tells the sync when it ends however it ends.
    /// </summary>
    internal sealed class SyncMove : ChoreoMove
    {
        public readonly TeamSync S;
        /// <summary>Its place in the cast (0: the leader).</summary>
        public readonly int Slot;
        /// <summary>Its stations (where the technique sends it) and its side of a formation (+1/-1).</summary>
        public Vector2 Mark, Mark2;
        public float Side = 1f;
        /// <summary>Its role in the technique (0 by default).</summary>
        public int Role;
        /// <summary>Never started (or called off): not part of the sync.</summary>
        public bool Dropped;
        /// <summary>Whom this member has struck in its current run (a foe is struck once by each runner).</summary>
        public readonly HashSet<long> Mine = new HashSet<long>();

        private readonly List<string> _passed = new List<string>(6);
        private static readonly List<Actor> _area = new List<Actor>();

        public SyncMove(Actor a, Technique t, Plan p, TeamSync s, int slot) : base(a, t, p)
        {
            S = s;
            Slot = slot;
            Hue = Hue.For(Tier, Syncs.SchoolOf(a), Level);
        }

        public bool Leader => Slot == 0;

        /// <summary>Still playing its part: not dropped, ended, broken, answered or lost, alive.</summary>
        public bool In => !Dropped && !Ended && !Finished && !Broken && !Interruptible && !Lost && Valid;

        public bool Passed(string cue)
        {
            return _passed.Contains(cue);
        }

        public void Pass(string cue)
        {
            if (!_passed.Contains(cue))
            {
                _passed.Add(cue);
            }
        }

        public override Form Forms
        {
            get
            {
                Form f = base.Forms;
                if (!Charging)
                {
                    f &= ~Form.Charge;
                }
                return f | Form.Team;
            }
        }

        public override void Tick(float dt)
        {
            S.FrameTick(dt);
            base.Tick(dt);
        }

        public override void Frame()
        {
            base.Frame();
            S.OnFrame?.Invoke(S, this);
        }

        public override void Abort()
        {
            base.Abort();
            S.Gone(this);
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            S.Gone(this);
        }

        // ------------------------------------------------------------ script --

        /// <summary>
        /// The call (the sync's first beat): the leader's hand goes up (TeamSync.Called: a flare, a
        /// thread to each friend); a partner answers with a nod and a glint.
        /// </summary>
        public SyncMove Call()
        {
            if (Leader)
            {
                Beat b = new Beat { Tempo = Tempo.Wind, Dur = 0.13f, To = new Shape(-10f, 0.92f, 1.1f, 0f), Ease = Ease.OutQuad, Forms = Form.Team };
                b.Enter = mm => S.Called();
                Add(b);
            }
            else
            {
                Beat b = new Beat { Tempo = Tempo.Wind, Dur = 0.13f, To = new Shape(8f, 1.06f, 0.95f, 0f), Ease = Ease.OutBack, Forms = Form.Team };
                b.On(0.45f, mm =>
                {
                    if (Vis)
                    {
                        Fx.Glint(HeadPos() + new Vector2(0f, 1.5f * Px), Px, 0.15f, Hue.Core, 0.8f);
                    }
                });
                Add(b);
            }
            return this;
        }

        /// <summary>
        /// A rendezvous: it marks 'cue' reached and holds its pose (breathing) until every member still in
        /// the sync has reached it too (at most 'max' seconds: nobody waits forever for a friend in trouble).
        /// </summary>
        public SyncMove Meet(string cue, float max = 1.2f)
        {
            Beat b = new Beat { Dur = 0f, Tempo = Tempo.Free, MaxDur = max, To = Shape.Keep, Until = mm => S.All(cue) || S.Failed };
            b.Enter = mm => Pass(cue);
            b.Every = (mm, k) =>
            {
                float br = Mathf.Sin(BeatT * 10f);
                Pose.SY *= 1f + 0.02f * br;
                Pose.SX *= 1f - 0.015f * br;
            };
            Add(b);
            return this;
        }

        /// <summary>
        /// A rendezvous of two: it marks 'cue' reached and holds its pose until the member 'other' names
        /// (asked when it gets there) has reached it too, or is out of the sync (at most 'max' seconds).
        /// </summary>
        public SyncMove MeetWith(string cue, Func<SyncMove, int> other, float max = 1.2f)
        {
            int with = -1;
            Beat b = new Beat { Dur = 0f, Tempo = Tempo.Free, MaxDur = max, To = Shape.Keep };
            b.Enter = mm =>
            {
                Pass(cue);
                with = other(this);
            };
            b.Until = mm =>
            {
                SyncMove o = S.At(with);
                return o == null || !o.In || o.Passed(cue) || S.Failed;
            };
            b.Every = (mm, k) =>
            {
                float br = Mathf.Sin(BeatT * 10f);
                Pose.SY *= 1f + 0.02f * br;
                Pose.SX *= 1f - 0.015f * br;
            };
            Add(b);
            return this;
        }

        /// <summary>A named point of the script (Jump and Unless go there).</summary>
        public SyncMove Section(string label)
        {
            Add(new Beat { Dur = 0f, Label = label });
            return this;
        }

        /// <summary>Goes on only if 'cond' holds here; else it jumps to 'elseLabel' (a branch of the script).</summary>
        public SyncMove Unless(Func<SyncMove, bool> cond, string elseLabel)
        {
            Do(mm =>
            {
                if (!cond(this))
                {
                    Jump(elseLabel);
                }
            });
            return this;
        }

        /// <summary>Jumps to 'label' here (the end of a branch).</summary>
        public SyncMove Goto(string label)
        {
            Do(mm => Jump(label));
            return this;
        }

        // ------------------------------------------------------------- blows --

        /// <summary>
        /// A bespoke blow of this member on 'v' (a dart, a sweep, a shared finisher): it must be fair
        /// game, it may answer (Touch), then the engine's Deliver (damage, reaction, impact). Default
        /// result if it didn't land.
        /// </summary>
        public HitResult HitOn(Actor v, Hit h, Vector2 dir, float contact = -1f)
        {
            if (v == null || !Combat.Living(v) || !Syncs.Fair(A, v, out float cm, true))
            {
                return default;
            }
            if (!Touch(v))
            {
                return default;
            }
            if (!Combat.Allowed(A, v, out _))
            {
                h.Mult *= cm;   // (one class too heavy for it alone: the team's share of the blow)
            }
            BlowShape s = ShapeOf(h.Blow);
            Vector2 at = Choreo.BodyAt(v, contact >= 0f ? contact : Mathf.Min(0.8f, s.Contact));
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = Dir;
            }
            dir.Normalize();
            // a body the engine is already flying (launched, juggled) is out of Victims.Hurt's reach in the
            // air (it only rocks it): its reaction is restarted on its own body after the blow
            Victim b = Victims.Get(v);
            bool aloft = b != null && b.Holder == null && Combat.HeightOf(v) > HundredFists.HopMax;
            HitResult r = Deliver(v, h, s, at, dir);
            Hurt hurt = h.Hurt ?? s.Hurt;
            if (aloft && !b.Done && Combat.Living(v) && hurt != Hurt.None && hurt != Hurt.Flinch && hurt != Hurt.Stagger
                && !RealmControl.BlocksLower(v, A))
            {
                Impact im = Impact.Of(hurt, dir, h.Push >= 0f ? h.Push : s.Push, Mathf.Clamp(h.Power * s.Weight, 0.3f, 2.2f));
                im.Lift = h.Lift >= 0f ? h.Lift : s.Lift;
                im.Daze = h.Daze;
                im.Owed = MeteorSlam.AtDeathsDoor(v);
                b.Start(im, A, this);
            }
            return r;
        }

        /// <summary>
        /// An area blow of this member around 'at' (radius 'r', x'mult', weaker at the rim): each foe may
        /// evade it; the first member to strike a foe in this sync's pass moves it ('hurt', pushed away
        /// from 'at'), the others only add their damage and a flash (no reaction fighting another).
        /// 'skip' is left out. Returns how many were hit.
        /// </summary>
        public int Burst(Vector2 at, float r, float mult, Hurt hurt, float push, float power = 1f, Actor skip = null)
        {
            Choreo.Foes(A, at, r, _area);
            int n = 0;
            for (int i = 0; i < _area.Count; i++)
            {
                Actor u = _area[i];
                if (u == skip || !Syncs.Fair(A, u, out float cm, true) || !Touch(u, true))
                {
                    continue;
                }
                float near = 1f - Mathf.Clamp01(Vector2.Distance(u.current_position, at) / Mathf.Max(0.1f, r));
                Combat.Hit(A, u, mult * cm * Mathf.Lerp(0.6f, 1f, near), true, false, 0f, this);
                n++;
                bool first = S.Struck.Add(u.Sid());
                Vector2 away = u.current_position - at;
                if (Combat.Living(u))
                {
                    Victims.Hurt(u, A, this, Impact.Of(first ? hurt : Hurt.None, away, push * Mathf.Lerp(0.6f, 1f, near), power));
                }
                else
                {
                    Victims.Flash(u, Color.white, 0.05f);
                }
                if (Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), away, Px, Kk, 0.7f * power, Hue, TopLayer);
                }
            }
            return n;
        }

        /// <summary>
        /// The look of a bespoke blow of shape 'b' thrown at 'contact' (what the engine's own Strike draws in
        /// its act beat): the limb's arc swish or straight streak with its head, speed lines for a heavy one,
        /// the body's smear frame. Nothing off screen.
        /// </summary>
        public void BlowFx(Blow b, Vector2 contact)
        {
            if (!Vis)
            {
                return;
            }
            BlowShape s = ShapeOf(b);
            float life = 0.08f * ActK * 1.6f + 0.03f;
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
                Vector2 to = Vector2.Lerp(contact, from, 0.15f);
                ChoreoFx.Smear(from, to, Px, life, Mathf.Min(s.SmearHead + (Level >= 3 ? 1 : 0), 3), s.SmearThick, Hue.Core, Hue.Edge, FxLayer.Over, 7);
                if (s.Weight >= 1f)
                {
                    Kit.SpeedLines(Vector2.Lerp(from, to, 0.5f), to - from, Px, 2, 2.5f, 0.08f, Fx.WithA(Hue.Edge, 0.75f), FxLayer.Over);
                }
            }
            Fx.Ghost(this, -Dir * 2f * Px, -6f, 0.07f, Fx.WithA(Hue.Ghost, 0.55f), Hue.GhostEnd);
        }

        /// <summary>A member's everyday blow by its school (a boxer's hook, a kicker's roundhouse, a blade's cut...), 'k' alternating.</summary>
        public Blow BlowOfSchool(int k)
        {
            bool a = (k & 1) == 0;
            if (Armed)
            {
                return a ? Blow.Slash : Blow.DiagSlash;
            }
            switch (Syncs.SchoolOf(A))
            {
                case School.IronFist:
                    return a ? Blow.Hook : Blow.Cross;
                case School.TempestLeg:
                    return a ? Blow.Roundhouse : Blow.SnapKick;
                case School.ThousandBlades:
                    return a ? Blow.Slash : Blow.DiagSlash;
                case School.KiArts:
                    return a ? Blow.Palm : Blow.Jab;
                case School.EarthBreaker:
                    return a ? Blow.Elbow : Blow.Knee;
                case School.PhantomStep:
                    return a ? Blow.Backfist : Blow.SpinKick;
                case School.HuntersWay:
                    return a ? Blow.Knee : Blow.Hook;
                default:
                    return a ? Blow.Jab : Blow.Cross;
            }
        }

        /// <summary>Where its palms are this frame (held out in front of it): what it holds there, catches there.</summary>
        public Vector2 Palms()
        {
            return BodyPos() + new Vector2(Fwd * Hpx * 0.36f, Hpx * 0.05f) * Px;
        }

        /// <summary>The local hit-stop a blow of this weight gives (the engine's measure), for a team-wide freeze.</summary>
        public float HoldOf(float weight)
        {
            return (0.012f + 0.028f * weight) * ImpactK * (Level <= 1 ? 0.7f : 1f);
        }
    }

    /// <summary>
    /// Planning helpers the syncs share: who may join (free friends on their feet, able to pay their
    /// share, allowed to fight the target), whether a foe is worth a sync, and what a sync is worth
    /// beyond its damage (more friends, a foe stronger than the one calling it, a mix of schools).
    /// Also the syncs' dev command.
    /// </summary>
    internal static class Syncs
    {
        private static readonly List<Actor> _pick = new List<Actor>(8);

        /// <summary>The first school a unit walks (its colours in a sync), None if it walks none.</summary>
        public static School SchoolOf(Actor a)
        {
            for (int i = 1; i <= Schools.Count; i++)
            {
                if (Schools.Knows(a, (School)i))
                {
                    return (School)i;
                }
            }
            return School.None;
        }

        /// <summary>
        /// May 'u' join 'leader's sync on 'v' right now: free (Crew.Free: on its feet, not in a technique,
        /// a set piece, a duel or a battle situation), out of the water, able to pay 'share', allowed to
        /// fight 'v' (class limits)?
        /// </summary>
        public static bool Free(Actor leader, Actor u, Actor v, float share, bool forced)
        {
            return Crew.Free(leader, u) && !u.isInLiquid() && (forced || Crew.CanPay(u, share)) && (v == null || Allowed(u, v, out _));
        }

        /// <summary>
        /// Free friends of the leader for a sync on 'v' (null: no target), nearest first: within 'range'
        /// tiles of the leader and 'nearV' of the target; tiered ones first, then (from level II, or
        /// forced) untiered ones who are fighting too. At most 'max'. A shared scratch list: copy it to keep it.
        /// </summary>
        public static List<Actor> Pick(Ctx c, Actor v, int level, int max, float share, float range, float nearV)
        {
            _pick.Clear();
            if (c.Forced)
            {
                range *= 1.6f;   // (a god-power order or a dev demo calls friends from further off)
                nearV *= 1.5f;
            }
            float r2 = range * range, v2 = nearV * nearV;
            int passes = level >= 2 || c.Forced ? 2 : 1;
            for (int pass = 0; pass < passes && _pick.Count < max; pass++)
            {
                for (int i = 0; i < c.Allies.Count && _pick.Count < max; i++)
                {
                    Actor u = c.Allies[i];
                    if ((u.current_position - c.Pos).sqrMagnitude > r2)
                    {
                        break;   // nearest first: the rest are further
                    }
                    bool tiered = Tiers.Of(u) != Tier.None;
                    if (tiered != (pass == 0) || !tiered && !c.Forced && !u.isFighting())
                    {
                        continue;
                    }
                    if (v != null && (u.current_position - v.current_position).sqrMagnitude > v2)
                    {
                        continue;
                    }
                    if (_pick.Contains(u) || !Free(c.Self, u, v, share, c.Forced))
                    {
                        continue;
                    }
                    _pick.Add(u);
                }
            }
            return _pick;
        }

        /// <summary>The planned partners still free to join as the sync begins (a new list).</summary>
        public static List<Actor> Recheck(Actor a, Actor v, List<Actor> others, int max, float share, bool forced, float range)
        {
            List<Actor> list = new List<Actor>(4);
            if (others == null)
            {
                return list;
            }
            for (int i = 0; i < others.Count && list.Count < max; i++)
            {
                Actor u = others[i];
                if (u != null && !list.Contains(u) && Free(a, u, v, share, forced) && Vector2.Distance(u.current_position, a.current_position) <= range)
                {
                    list.Add(u);
                }
            }
            return list;
        }

        /// <summary>
        /// Is 'v' worth a sync (not a foe one ordinary blow ends, nor a plain foot soldier a couple of blows
        /// end; a link or a god-power order always is)?
        /// </summary>
        public static bool Worthy(Ctx c, Actor v)
        {
            if (c.Forced || c.Linked)
            {
                return true;
            }
            float hp = v.getHealth();
            if (hp <= c.Expected(v, 1.4f * Crew.Plain(c)))
            {
                return false;
            }
            return !(Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute && hp <= c.Expected(v, 2.4f));
        }

        /// <summary>
        /// What a sync is worth beyond its damage: friends acting as one (more friends, more), ganging
        /// up on a foe stronger than the one who calls it (weaker fighters team up on a strong one), and
        /// friends of other schools each bringing their own art to it.
        /// </summary>
        public static float Bonus(Ctx c, Actor v, List<Actor> partners)
        {
            float b = 18f + 7f * partners.Count;
            if (v != null)
            {
                float ratio = Tiers.Power(v) / Mathf.Max(0.5f, Tiers.Power(c.Self));
                if (ratio > 1.1f)
                {
                    b += 30f * Mathf.Min(2f, ratio - 1f);
                }
            }
            School mine = SchoolOf(c.Self);
            for (int i = 0; i < partners.Count; i++)
            {
                if (SchoolOf(partners[i]) != mine)
                {
                    b += 8f;
                    break;
                }
            }
            return b;
        }

        /// <summary>
        /// May 'a' take part in a sync on 'v' (the class limits)? What it may fight alone, or, together with
        /// friends, one weight class heavier than that ('cm' 0.75: three-quarters of its damage): weaker
        /// fighters team up on a foe none of them could take on alone (Adepts on a bear, Experts on a
        /// demon, Masters on a dragon).
        /// </summary>
        public static bool Allowed(Actor a, Actor v, out float cm)
        {
            if (Combat.Allowed(a, v, out cm))
            {
                return true;
            }
            cm = 0.75f;
            return a != null && v != null && Weights.Of(v) == Weights.Limit(a) + 1;
        }

        /// <summary>What a member's blows on 'v' are scaled by for the team rule (1 if it may fight it alone).</summary>
        public static float Share(Actor a, Actor v)
        {
            if (Combat.Allowed(a, v, out _))
            {
                return 1f;
            }
            return Allowed(a, v, out float cm) ? cm : 0f;
        }

        /// <summary>Choreo.Fair with the team rule: a foe one class too heavy for 'a' alone is fair game in a sync.</summary>
        public static bool Fair(Actor a, Actor v, out float cm, bool forced)
        {
            if (Choreo.Fair(a, v, out cm, forced))
            {
                return true;
            }
            if (a == null || v == null || !Combat.Living(v) || !Combat.IsFoe(a, v) || MeteorSlam.IsDoomed(v) || !Choreo.Hittable(v))
            {
                return false;
            }
            if (!forced && Director.InScene(v))
            {
                return false;
            }
            return Allowed(a, v, out cm);
        }

        /// <summary>
        /// Choreo.PickMelee, and when it finds nothing: a foe in reach one class too heavy for the leader
        /// alone (the team takes it on), checked the same way (fair, on the ground, out of the water, a
        /// clear path, liftable if 'w.Hold'), its value at the team's share of the damage.
        /// </summary>
        public static Plan PickMelee(Ctx c, Technique t, int level, MeleeWant w)
        {
            Plan p = Choreo.PickMelee(c, t, level, w);
            if (p != null || !Settings.ClassLimits)
            {
                return p;
            }
            Actor best = null;
            float bestScore = 0f;
            float k = Choreo.KOf(c.Self);
            for (int i = -1; i < c.Enemies.Count && i < 6; i++)
            {
                Actor v = i < 0 ? c.Focus : c.Enemies[i];
                if (v == null || i >= 0 && v == c.Focus || c.Allowed(v) || !Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                if (!Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position) || !HundredFists.Hopping(v))
                {
                    continue;
                }
                if (w.Hold && !Choreo.Holdable(v, c.Self))
                {
                    continue;
                }
                float reach = w.Reach * k + v.stats["size"] * 0.5f;
                if (c.Forced)
                {
                    reach = Mathf.Max(reach, 6f);
                }
                float d = c.Dist(v);
                if (d > reach || d > 1.2f && !Combat.ClearPath(c.Pos, v.current_position))
                {
                    continue;
                }
                float score = c.Value(v, c.Expected(v, w.Total) * cm) + w.Worth * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
                if (c.Forced)
                {
                    score = Mathf.Max(score, 1f);
                }
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = bestScore,
                Data = c.Forced
            };
        }

        /// <summary>
        /// Victims.Hurt, but a body the engine is already flying (launched, juggled: high in the air) has
        /// its reaction restarted on its own body: Victims.Hurt only rocks a body that high.
        /// </summary>
        public static Victim Hurt(Actor v, Actor by, Move move, Impact im)
        {
            Victim b = Victims.Get(v);
            if (b != null && b.Holder == null && !b.Done && Combat.Living(v) && Combat.HeightOf(v) > HundredFists.HopMax
                && !RealmControl.BlocksLower(v, by))
            {
                if (im.Dir.sqrMagnitude < 1e-6f)
                {
                    im.Dir = Vector2.right;
                }
                im.Dir.Normalize();
                if (im.Power <= 0f)
                {
                    im.Power = 1f;
                }
                b.Start(im, by, move);
                return b;
            }
            return Victims.Hurt(v, by, move, im);
        }

        /// <summary>Degrees of a direction (a random one for a zero vector).</summary>
        public static float Ang(Vector2 d)
        {
            return d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        }

        /// <summary>A unit's heaviness against another's size, for spacing (1 a human).</summary>
        public static float SizeOf(Actor v)
        {
            return v != null ? Mathf.Clamp(v.stats["size"], 0.3f, 6f) : 1f;
        }

        // ------------------------------------------------------------- dev --

        private sealed class Later
        {
            public float At;
            public Action Do;
        }

        private static readonly List<Later> _later = new List<Later>();

        /// <summary>
        /// syncfault [what] [delay]: 'delay' s from now, in the performer's sync, 'what' happens:
        ///   partner  the first partner dies        lead    the leader dies
        ///   target   the target dies                break   the first partner's move is broken (a blow mid-charge)
        ///   breaklead  the leader's move is broken
        ///   magnet   the first partner is lifted away (as by the god hand: its move is cut off)
        /// </summary>
        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["syncfault"] = args =>
            {
                string what = Dev.Arg(args, 1, "partner");
                float delay = Dev.ArgF(args, 2, 0.5f);
                _later.Add(new Later { At = Time.unscaledTime + delay, Do = () => Fault(what) });
                Dev.Log("syncfault: " + what + " in " + delay.ToString("F2") + " s");
            };
            Hooks.FrameTick += dt =>
            {
                if (_later.Count == 0)
                {
                    return;
                }
                float now = Time.unscaledTime;
                for (int i = _later.Count - 1; i >= 0; i--)
                {
                    if (i >= _later.Count || _later[i].At > now)
                    {
                        continue;
                    }
                    Later l = _later[i];
                    _later.RemoveAt(i);
                    try
                    {
                        l.Do();
                    }
                    catch (Exception e)
                    {
                        Dev.Log("syncfault: " + e);
                    }
                }
            };
            Hooks.WorldCleared += () => _later.Clear();
        }

        private static void Fault(string what)
        {
            Actor a = Dev.Performer;
            if (a == null || !Moves.TryGet(a, out Move m) || !(m is SyncMove sm))
            {
                Dev.Log("syncfault: the performer is in no sync");
                return;
            }
            TeamSync s = sm.S;
            SyncMove p = null;
            for (int i = 1; i < s.Cast.Count; i++)
            {
                if (s.Cast[i].In)
                {
                    p = s.Cast[i];
                    break;
                }
            }
            Actor victim = null;
            switch (what)
            {
                case "lead":
                    victim = a;
                    break;
                case "target":
                    victim = s.VOk ? s.V : null;
                    break;
                case "breaklead":
                    {
                        bool broke = Choreo.Break(a, s.VOk ? s.V : null, a.current_position - (s.VOk ? s.V.current_position : a.current_position + Vector2.left), 2.5f);
                        Dev.Log("syncfault: break the leader " + a.asset.id + "#" + a.Sid() + " (charging " + sm.Charging + "): " + broke);
                        return;
                    }
                case "break":
                    if (p != null)
                    {
                        bool broke = Choreo.Break(p.A, s.VOk ? s.V : a, p.A.current_position - (s.VOk ? s.V.current_position : a.current_position), 2.5f);
                        Dev.Log("syncfault: break " + p.A.asset.id + "#" + p.Id + " (charging " + p.Charging + "): " + broke);
                    }
                    return;
                case "magnet":
                    if (p != null)
                    {
                        Dev.Log("syncfault: " + p.A.asset.id + "#" + p.Id + " is cut out of the sync (as if lifted)");
                        Choreo.Kill(p);
                    }
                    return;
                default:
                    victim = p != null ? p.A : null;
                    break;
            }
            if (victim == null || !victim.isAlive())
            {
                Dev.Log("syncfault: nothing to kill");
                return;
            }
            Dev.Log("syncfault: " + victim.asset.id + "#" + victim.Sid() + " dies");
            victim.getHit(victim.getHealth() * 10f + 1000f, true, AttackType.Other, null, false, false, false);
        }
    }

    /// <summary>
    /// TWIN METEOR (team sync, Adept). Two friends (III: three) crouch on the same frame, glint, and
    /// leap from their own sides of a foe or a knot of foes: mirrored arcs that meet high above it.
    /// At the top they hang a heartbeat side by side, a line of light joining them, then plunge
    /// together in two streaks of their own colours, diving in at an angle so their trails cross
    /// over the target, and land back to back on the same frame: a shared flash, a ring in each
    /// colour, everyone around knocked flat; the one they landed on is crushed between them.
    /// I: plain arcs, a dust ring; II: higher, each flips over on the way up, embers in the trails,
    /// the target is driven into the ground, an impact frame; III: a third meteor when a friend is
    /// free, the highest leap, a crater (it burns from Master up) and a second shockwave.
    /// Damage per meteor (x its own damage): on the target I 1.1, II 1.5, III 2; on the others round
    /// it 0.6 of that.
    /// </summary>
    internal sealed class TwinMeteorTech : Technique
    {
        public override string Id => "twin_meteor";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Team;
        public override Form Forms => Form.Aerial | Form.Overhead | Form.Area | Form.Heavy | Form.Team;
        public override Scale Scale => Scale.Light;
        public override Scale ScaleAt(int level) => level >= 3 ? Scale.Heavy : Scale.Light;
        public override float[] ManaCost => new[] { 14f, 24f, 38f };
        public override float[] Cooldown => new[] { 13f, 14f, 15f };

        private static readonly float[] Shares = { 8f, 13f, 20f };

        public override bool Accepts(Link link)
        {
            return link == Link.Down || link == Link.Thrown;
        }

        private static float Share(int level)
        {
            return Shares[Mathf.Clamp(level, 1, 3) - 1];
        }

        /// <summary>One meteor's blow on the target (x its damage).</summary>
        private static float Mult(int level)
        {
            return level <= 1 ? 1.1f : level == 2 ? 1.5f : 2f;
        }

        private static float Radius(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.7f : 2f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            float k = Mathf.Min(1.3f, Choreo.KOf(c.Self));
            float r = Radius(level);
            Plan p = Choreo.PickArea(c, this, level, r, (c.Forced ? 9f : 6.5f) * k, Mult(level) * 1.6f, 1);
            if (p == null || p.Target == null)
            {
                // nothing it may strike alone: a foe one class too heavy for it, that the two of them take on
                p = Syncs.PickMelee(c, this, level, new MeleeWant { Reach = 6f, Total = Mult(level) * 2f, Worth = 16f });
                if (p == null)
                {
                    return null;
                }
                p.Count = 1;
            }
            Actor v = p.Target;
            if (p.Count <= 1 && !Syncs.Worthy(c, v))
            {
                return null;   // one weak foe: not worth two meteors
            }
            if (!Combat.SafeSpot(p.Point) && !Combat.FindLanding(p.Point, 1f, out _))
            {
                return null;   // never down into the water
            }
            List<Actor> partners = Syncs.Pick(c, v, level, level >= 3 ? 2 : 1, Share(level), 7f, 7.5f);
            if (partners.Count == 0)
            {
                return null;
            }
            p.Score += Syncs.Bonus(c, v, partners);
            p.Others = new List<Actor>(partners);
            p.Data = c.Forced;
            p.AreaRadius = r;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            bool forced = p.Data is bool b && b;
            if (v == null || p.Others == null || !Moves.CanPerform(a))
            {
                return false;
            }
            int level = Mathf.Clamp(p.Level, 1, 3);
            List<Actor> partners = Syncs.Recheck(a, v, p.Others, level >= 3 ? 2 : 1, Share(level), forced, 9f);
            if (partners.Count == 0)
            {
                return false;
            }
            // the centre: the planned knot, or the lone foe where it stands now
            Vector2 c = p.Count <= 1 && Combat.Living(v) ? v.current_position : p.Point;
            List<Actor> who = new List<Actor>(partners.Count + 1) { a };
            who.AddRange(partners);
            int n = who.Count;
            Vector2[] spot = new Vector2[n], hang = new Vector2[n];
            float kAvg = 0f;
            for (int i = 0; i < n; i++)
            {
                kAvg += Choreo.KOf(who[i]);
            }
            kAvg /= n;
            float r = (0.55f + 0.1f * n) * kAvg + 0.3f * Syncs.SizeOf(v);
            if (!Stations(who, c, r, spot, hang))
            {
                Crew.Fail("twin_meteor", v);
                return false;
            }
            TeamSync s = new TeamSync(this, level, forced, v, c, c - a.current_position);
            for (int i = 0; i < n; i++)
            {
                SyncMove m = s.Join(who[i]);
                m.Mark = spot[i];
                m.Mark2 = hang[i];
                Script(m, c);
            }
            return s.Go(1, Share(level), 6f);
        }

        /// <summary>
        /// Landing spots evenly round the centre (the leader's on its own side; each friend takes the free
        /// slot nearest to where it stands), each on safe ground; the hang above a point a little further
        /// out, so the plunge comes in at an angle and the trails cross over the target.
        /// </summary>
        private static bool Stations(List<Actor> who, Vector2 c, float r, Vector2[] spot, Vector2[] hang)
        {
            int n = who.Count;
            float a0 = Syncs.Ang(who[0].current_position - c);
            bool[] taken = new bool[n];
            int[] slot = new int[n];
            taken[0] = true;
            for (int i = 1; i < n; i++)
            {
                float ai = Syncs.Ang(who[i].current_position - c);
                int best = 1;
                float bd = 999f;
                for (int j = 1; j < n; j++)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(ai, a0 + j * 360f / n));
                    if (!taken[j] && d < bd)
                    {
                        bd = d;
                        best = j;
                    }
                }
                taken[best] = true;
                slot[i] = best;
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Crew.Dir(a0 + slot[i] * 360f / n);
                Vector2 q = Combat.Clamp(c + d * r);
                if (!Combat.SafeSpot(q) && !Combat.FindLanding(q, 1f, out q))
                {
                    return false;
                }
                spot[i] = q;
                hang[i] = Combat.Clamp(q + d * 0.85f);
            }
            return true;
        }

        private static void Script(SyncMove m, Vector2 c)
        {
            TeamSync s = m.S;
            int L = s.Level;
            float hang = (L <= 1 ? 2.1f : L == 2 ? 2.6f : 3.1f) * Mathf.Lerp(1f, 1f / m.Kk, 0.5f);
            Vector2 spot = m.Mark, pre = m.Mark2;
            float dist = Vector2.Distance(m.A.current_position, pre);
            m.Call();
            // they crouch together, and glint together
            m.Crouch(0.14f, 1.15f);
            m.Last.Tempo = Tempo.Free;
            m.Meet("crouch", 0.9f);
            m.Do(mm => s.Ting("crouch"));
            // the leap: an arc up to the hang over its own side
            Beat leap = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.28f + 0.028f * dist,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => pre,
                Apex = 0.35f,
                Spin = L >= 2 ? 360f : 0f,
                To = new Shape(-8f, 0.92f, 1.1f, hang),
                Ease = Ease.OutQuad,
                Ghosts = 0.035f,
                Forms = Form.Aerial
            };
            leap.Enter = mm =>
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    Kit.DustRing(mm.Stance, mm.Px, mm.A.current_tile, 10, 40f, 0.5f, mm.Kk);
                    Kit.SpeedLines(mm.BodyPos(), Vector2.up, mm.Px, 3, 3f, 0.14f, Fx.WithA(mm.Hue.Edge, 0.7f));
                }
            };
            leap.Every = (mm, k) =>
            {
                float fast = k < 0.15f ? 1f : Mathf.Clamp01(1f - k * 1.2f);
                mm.Pose.SX *= 1f - 0.1f * fast;
                mm.Pose.SY *= 1f + 0.16f * fast;
                mm.Pose.HideShadow = false;
            };
            m.Add(leap);
            // the hang: side by side at the top, a line of light joining them
            m.Meet("apex", 0.55f);
            m.Do(mm =>
            {
                Vector2 mid = Vector2.zero;
                int n = 0;
                for (int i = 0; i < s.Cast.Count; i++)
                {
                    SyncMove o = s.Cast[i];
                    if (o.In)
                    {
                        mid += o.BodyPos();
                        n++;
                    }
                }
                if (n >= 2)
                {
                    mid /= n;
                    if (s.Union("apex", mid, 0.7f))
                    {
                        for (int i = 0; i < s.Cast.Count; i++)
                        {
                            SyncMove o = s.Cast[i];
                            if (o.In && o.Vis)
                            {
                                Kit.Flash(o.BodyPos(), o.Px, Mathf.RoundToInt(4f * o.Kk), 0.1f, Fx.WithA(o.Hue.Core, 0.9f));
                            }
                        }
                    }
                }
            });
            m.PoseTo(0.08f, new Shape(-16f, 0.9f, 1.12f, hang + 0.15f), Ease.OutQuad, Tempo.Free);
            // the plunge: a streak in its own colour, diving in at an angle onto the centre
            Vector2 trail = Vector2.zero;
            float ember = 0f;
            Beat dive = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.13f + 0.01f * L,
                Route = Route.Line,
                PathEase = Ease.InQuad,
                Dest = mm => spot,
                To = new Shape(30f, 0.84f, 1.22f, 0f),
                Ease = Ease.InQuad,
                Ghosts = 0.018f,
                Lines = 0.03f,
                Track = false,
                Forms = Form.Aerial | Form.Overhead | Form.Heavy
            };
            dive.Enter = mm =>
            {
                trail = mm.BodyPos();
                mm.Face(c - mm.Stance);
                Choreo.Sound("event:/SFX/DESTRUCTION/FallMeteorite", mm.A.current_tile, 0.2f);
            };
            dive.Every = (mm, k) =>
            {
                if (!mm.Vis)
                {
                    return;
                }
                Vector2 now = mm.BodyPos();
                if ((now - trail).sqrMagnitude > 1e-4f)
                {
                    int thick = L >= 3 ? 3 : 2;
                    Kit.Line(trail, now, mm.Px, 0.2f, thick, FxLayer.Behind, 4, Fx.WithA(mm.Hue.Core, 0.95f), Fx.WithA(mm.Hue.Edge, 0.7f), Fx.WithA(mm.Hue.Edge, 0f));
                    trail = now;
                }
                if (L >= 2)
                {
                    ember -= Clock.Dt;
                    if (ember <= 0f)
                    {
                        ember = 0.025f;
                        Kit.Ember(now + Random.insideUnitCircle * 2f * mm.Px, mm.Px, 0.3f, new Vector2(Random.Range(-10f, 10f), Random.Range(10f, 25f)) * mm.Px, mm.Hue.Edge);
                    }
                }
            };
            dive.Exit = mm => Land((SyncMove)mm, c);
            m.Add(dive);
            // the landing: squashed, the follow-through held while the dust flies
            m.PoseTo(0.1f, new Shape(8f, 1.26f, 0.76f, 0f), Ease.OutExpo, Tempo.Free);
            m.Hold(0.12f, false, Tempo.Impact);
            m.Recover(0.26f);
        }

        /// <summary>One meteor comes down: its own blows, and (once) the shared impact where they meet.</summary>
        private static void Land(SyncMove m, Vector2 c)
        {
            TeamSync s = m.S;
            int L = s.Level;
            m.Pose.Height = 0f;
            float mult = Mult(L);
            float r = Radius(L);
            WorldTile tile = Combat.TileAt(c);
            if (m.Vis)
            {
                Fx.LandingPuff(m.Pose.Ground, m.Px, m.A.current_tile);
                Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 8, 36f, 0.45f, m.Kk);
            }
            // the target, crushed between them (the first meteor down moves it; the others add their weight)
            if (s.VOk && Vector2.Distance(s.VG, c) <= r + 0.6f)
            {
                bool first = !s.Struck.Contains(s.VId);
                s.Struck.Add(s.VId);
                Hurt hurt = !first ? Hurt.None : L >= 2 ? Hurt.DriveIn : Hurt.Knockdown;
                m.HitOn(s.V, new Hit(Blow.Stomp, mult) { Hurt = hurt, Push = 0.3f, Lethal = true, Finisher = true, Power = 1.2f + 0.15f * L, Daze = 0.6f }, s.VG - m.Pose.Ground, 0.85f);
            }
            // everyone round it, knocked flat
            m.Burst(c, r, mult * 0.6f, Hurt.Knockdown, 0.8f + 0.2f * L, 1f, s.VOk ? s.V : null);
            if (!s.Once("impact"))
            {
                return;
            }
            // the shared impact: where the meteors meet
            s.Union("land", c + new Vector2(0f, 0.5f * m.Kk), 1.1f + 0.15f * L, false);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.1f);
            if (m.Vis)
            {
                for (int i = 0; i < s.Cast.Count; i++)
                {
                    SyncMove o = s.Cast[i];
                    if (!o.In)
                    {
                        continue;
                    }
                    Kit.Shockwave(c, m.Px, 4f + 3f * i, (r / m.Px) * (1f + 0.12f * i), 0.38f + 0.05f * i, Fx.WithA(o.Hue.Edge, 0.9f), 2);
                }
                Kit.Debris(c, m.Px, tile, 6 + 3 * L, m.Kk);
                Kit.DustRing(c, m.Px, tile, 12 + 2 * L, 46f, 0.6f, m.Kk);
                Kit.Crater(c, m.Px * m.Kk, tile, Ruin.Force(m.Tier, s.Scale) >= 3, 6f, L <= 2, m.Hue.Edge);
                if (L >= 2)
                {
                    Kit.ImpactFrame(c + new Vector2(0f, 0.5f * m.Kk), m.Px, (0.28f + 0.08f * L) * m.Kk, m.Hue.Pal, false, FxLayer.Behind);
                }
            }
            Ruin.Blast(m.A, c, 0.7f + 0.25f * L, s.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, c, s.Scale, 1.1f + 0.15f * L, m.Kk);
            if (L >= 3)
            {
                // the second shockwave: the ground still shuddering throws down whoever was further out
                m.Burst(c, r + 1f, mult * 0.3f, Hurt.Knockdown, 0.6f, 0.8f, s.VOk ? s.V : null);
                Kit.Stop(c, 0.05f * m.ImpactK);
            }
        }
    }
}
