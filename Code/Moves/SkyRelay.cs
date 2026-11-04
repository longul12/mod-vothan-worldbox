using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SKY RELAY (team). One fighter kicks an enemy into the sky; its friends take turns under
    /// the falling body, each kicking it up and over to the next one around the circle, until
    /// the last one finishes it: a smash into the ground (I), a spike that cracks the earth (II),
    /// or a fireball hurled into its face as it falls (III).
    ///
    /// Beat sheet (game seconds at x1; a relay of n kickers lasts about 1 + 0.55 n s):
    ///
    ///   STEP IN   ~0.1   the leader slides beside the enemy (which can't act until the launch: no
    ///                    shot, no backstep out of reach); the first partner is already moving
    ///   LAUNCH    0.14-0.22  crouch, then a rising kick / a two-handed heave (small enemy or a
    ///                    big leader) / a rising slash (armed leader): the enemy is thrown high
    ///                    into the sky (2-3 tiles; caught even mid-hop)
    ///   RELAY    ~0.55 each  the body arcs 1.5-3 tiles to the next station, tumbling, with
    ///                    afterimages and a thin streak drawing its arc (pale at I, the last
    ///                    striker's colour from II); the partner dashes (or leaps, over walls and water) under its
    ///                    fall, crouches, and meets it with a scissor kick / spinning heel /
    ///                    uppercut / overhead bicycle kick (never the same twice in a row; beasts
    ///                    swipe up); hit star, sparks, the kicker's tier colour from level II, a
    ///                    local hit-stop at II+, then it lands
    ///   FINISH   I  SMASH   0.36  leap above it, a flip, an axe kick: spiked into a dust ring
    ///            II SPIKE         the same, harder: a crater and a ring in the finisher's colour
    ///            III FIREBALL 0.62  the finisher plants its feet beyond the landing spot and
    ///                    conjures a ball of fire (a spinning white core, embers spiralling in)
    ///                    while the enemy is lobbed high toward it, then hurls it: explosion,
    ///                    impact frame, global hit-stop, a glowing crater, embers
    ///   CRASH     0.3    the enemy bounces out of the impact and lies stunned (Down); the ground
    ///                    takes it on the same frame (Kit.Ravage: I scorched, II a crater, III a
    ///                    burning crater; with a God among them a scale bigger, wider and deeper)
    ///   POSE      ~0.4   everyone lands; the finisher's "ting!"; the leader and the finisher may
    ///                    both follow up on the downed enemy (Down)
    ///
    /// Power (Combat.Hit adds each striker's tier Might): the launch and kicks x0.4 / 0.5 / 0.6, the
    /// axe kick x0.5 / 0.6 and the crash x1.5 / 2.8, the fireball x4 and its crash x2 (a III spiked
    /// for want of a throwing spot: the axe kick x0.6 and a crash x5.4, with the III's impact frame
    /// and global hit-stop on the crash).
    /// Tempo: the passes are brisk at every tier and tighten from Expert up (0.93 / 0.78 / 0.74 /
    /// 0.71: the next kicker's leap or the conjuring may start as the last kick lands), landings are
    /// crisper (the tier's Linger: 0.85 / 0.55 / 0.48 / 0.42), a Master or better who has kicked is
    /// let go at once to fight on, and a Master leader has no attack cooldown after it.
    ///
    /// Variety: the route sweeps around the enemy from the leader's side toward where the
    /// partners stand (arcs never cross); the station ring's size and the direction change;
    /// the finisher is the strongest partner, or the leader itself closing the circle when it
    /// outranks them all; level III falls back to the spike when there is no safe spot to throw from.
    ///
    /// Readability: the game draws a body h high h above its ground point, so a station straight
    /// below a friend would put the blow (and the finisher's leap) on top of that friend on screen.
    /// Each station is picked among the safe spots around its place on the ring so that the screen
    /// column its blow takes up is clear of the leader, of the strikers already placed, and of the
    /// partners still waiting; the fireball thrower doesn't plant its feet on a friend either.
    ///
    /// Robustness: every blow before the finish is non-lethal, so the show plays out. A partner
    /// that dies or is taken mid-way is skipped (the arc bends to the next one); with nobody left
    /// the enemy drops. If the enemy dies or is taken, everyone lands and poses. If the leader
    /// dies, every partner and the enemy are let go (in the air they fall).
    /// </summary>
    internal sealed class SkyRelay : Move
    {
        private enum Style
        {
            Rising,
            Heave,
            Slash,
            Scissor,
            Heel,
            Uppercut,
            Overhead,
            Smash,
            Fireball
        }

        private enum Act
        {
            Idle,
            Approach,
            Ready,
            Strike,
            Air,
            Land,
            Watch
        }

        private enum VState
        {
            Free,
            Flight,
            Spike,
            Bounce,
            Fall,
            Done
        }

        /// <summary>One participant (the leader or a partner) and what it is doing.</summary>
        private sealed class Body
        {
            public Actor U;
            public Move M;
            public TeamPartner P;
            public bool Leader;
            public Palette Pal;
            public float K, Px, ClassMult = 1f;
            public Vector2 At;
            public bool Dead;
            public Act Act;
            public float T0, Dur;
            public Vector2 From, To;
            public float HFrom, HUp, RotFrom, RotTo;
            public bool Leap;
            public Leg Leg;
            public float Ghost;
            public bool F1, F2, F3;
            public float Breath;
        }

        /// <summary>One blow of the relay: who strikes, where the enemy is, when.</summary>
        private sealed class Leg
        {
            public Body B;
            public Style Style;
            public bool Final;
            public Vector2 Station;
            public Vector2 Stand;
            public Vector2 In, Out;
            public float Hc;
            public float H;
            public float Contact;
            public float ApproachStart, ApproachDur;
            public bool LeapIn;
            public bool Engaged, Done, Skipped;
        }

        private static readonly Color Smoke = new Color(0.36f, 0.33f, 0.31f, 1f);
        private static readonly Color FireCore = new Color(1f, 0.98f, 0.82f, 1f);

        private readonly Actor _v;
        private readonly long _vid;
        private Puppet _p;
        private bool _vGone;
        private VState _vs = VState.Free;
        private float _vt, _vSpin;
        private Vector2 _fFrom, _fTo;
        private float _fH0, _fH1, _fH, _fT0, _fD;
        private Vector2 _sFrom, _sTo;
        private float _sH0, _sD, _sUp;
        private float _vGhost;
        private Palette _vPal;
        private Particle _shadow;
        private Vector2 _trailAt;
        private bool _trailOn;
        private Body _lastKicker;

        private readonly List<Body> _bodies = new List<Body>();
        private readonly List<Leg> _legs = new List<Leg>();
        private readonly List<Vector3> _standing = new List<Vector3>();
        private Body _lead;
        private int _cur;
        private float _clock, _hold;
        private Vector2 _c, _side;
        private float _length;
        private bool _ending, _parried, _wrapped;
        private float _endAt;
        private Body _finisherBody;
        private bool _vis;

        // tempo: the passes through the air tighten from Master up (the leader calls the pace), and
        // a God among them makes the world damage a scale bigger
        private float _tempo = 1f;
        private bool _god;

        // the fireball (level III)
        private Particle _fbGlow, _fbBody, _fbCore, _fbSwirl;
        private Vector2 _fbPos, _fbFrom, _fbLast;
        private bool _fbOut, _fbFlying;
        private float _fbEmber;

        /// <summary>Relays started / finished since the game started (dev log).</summary>
        internal static int Started, Finished_;

        // ---- power (multiples of the striker's damage; Combat.Hit adds its tier's Might). Each level
        // hits clearly harder: I ~2.8x in all with two friends, II ~4.9x with three, III ~8.4x with four
        // (a fireball and a burning crater); never a sure kill on an enemy of the same tier.

        /// <summary>The launch and every kick of the relay (never lethal: the show plays out).</summary>
        internal static float Kick(int level)
        {
            return level <= 1 ? 0.4f : level == 2 ? 0.5f : 0.6f;
        }

        /// <summary>
        /// The finishing blow when the enemy hits the ground (III: after the fireball; a III that had
        /// nowhere to throw from spikes it instead, and its crash carries the fireball's share: the
        /// finish is x6 either way).
        /// </summary>
        internal static float CrashMult(int level, bool fireball = true)
        {
            return level <= 1 ? 1.5f : level == 2 ? 2.8f : fireball ? 2f : 5.4f;
        }

        internal const float FireballMult = 4f;

        private float KickMult => Kick(Level);

        private SkyRelay(Actor a, Actor v, int level) : base(a)
        {
            Level = level;
            _v = v;
            _vid = v.Sid();
            _c = v.current_position;
            _tempo = Crew.Beat(a);
        }

        // ------------------------------------------------------------- begin --

        /// <summary>
        /// Starts a relay: 'a' launches 'v', the partners (in any order) take turns. False if it
        /// couldn't start (no safe stations, someone busy): nothing is started then.
        /// </summary>
        public static bool Begin(Technique tech, Actor a, Actor v, List<Actor> partners, int level, bool forced, float share)
        {
            if (!Moves.CanPerform(a) || !Combat.CanPuppet(v, a) || partners == null || partners.Count == 0)
            {
                return false;
            }
            SkyRelay m = new SkyRelay(a, v, level) { Tech = tech };
            List<Actor> cast = new List<Actor>(partners) { a, v };
            if (!Director.Claim(m, 12f, cast.ToArray()))
            {
                return false;
            }
            // caught mid-swing or mid-dash: the team takes it over where it stands
            Crew.Seize(v);
            m._c = v.current_position;
            // the partners first (like puppets): whoever can't start is simply left out
            List<TeamPartner> started = new List<TeamPartner>();
            foreach (Actor u in partners)
            {
                TeamPartner pm = new TeamPartner(u, m, Crew.Face(u.current_position, v.current_position, u.flip));
                if (Moves.Start(pm))
                {
                    started.Add(pm);
                }
                else
                {
                    Director.Release(u);
                }
            }
            if (started.Count == 0 || !m.Build(started))
            {
                Undo(m, started);
                Crew.Fail("sky_relay", v);
                return false;
            }
            m.Schedule();
            if (!Moves.Start(m))
            {
                Undo(m, started);
                return false;
            }
            // until the launch the enemy is still free: it must not hop away (an archer's backstep)
            Crew.Pin(v, m._legs[0].Contact + 0.25f);
            // the cast (who actually takes part) is held for the whole show
            cast.Clear();
            cast.Add(a);
            cast.Add(v);
            foreach (Body b in m._bodies)
            {
                if (b.P != null && !b.Dead)
                {
                    cast.Add(b.U);
                }
            }
            Director.Claim(m, m._length + 2f, cast.ToArray());
            if (!forced)
            {
                foreach (Body b in m._bodies)
                {
                    if (b.P != null && !b.Dead)
                    {
                        Crew.Pay(b.U, tech, share, 6f);
                    }
                }
            }
            Started++;
            Dev.Watch(m._c + new Vector2(0f, 1.6f));
            if (Dev.On)
            {
                Dev.Trace("relay " + m.Id + " " + Techniques.Roman(level) + " with " + started.Count + " partners, "
                    + (m._legs.Count - 1) + " legs, finisher " + (m._finisherBody.Leader ? "leader" : "partner") + " "
                    + m._legs[m._legs.Count - 1].Style + ", length " + m._length.ToString("F2")
                    + ", stations moved " + m._moved + ", overlap " + m._overlap.ToString("F2"));
            }
            return true;
        }

        private static void Undo(SkyRelay m, List<TeamPartner> started)
        {
            foreach (TeamPartner pm in started)
            {
                pm.Release(true);
            }
            Director.Release(m);
        }

        /// <summary>
        /// The route: stations on a ring around the enemy, swept from the leader's side toward the
        /// partners, the finisher last; who kicks how; where everyone stands.
        /// </summary>
        private bool Build(List<TeamPartner> partners)
        {
            _lead = NewBody(A, this, null);
            _bodies.Add(_lead);
            List<Body> pb = new List<Body>();
            foreach (TeamPartner pm in partners)
            {
                Body b = NewBody(pm.A, pm, pm);
                _bodies.Add(b);
                pb.Add(b);
            }

            // who finishes: the strongest partner, or the leader closing the circle if it outranks them all
            Body fin = null;
            foreach (Body b in pb)
            {
                if (fin == null || Stronger(b.U, fin.U))
                {
                    fin = b;
                }
            }
            if (Tiers.Of(A) > Tiers.Of(fin.U) && Random.value < 0.75f)
            {
                fin = _lead;
            }

            Vector2 lp = A.current_position;
            float phiL = (lp - _c).sqrMagnitude > 0.01f ? Mathf.Atan2(lp.y - _c.y, lp.x - _c.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);

            // order the kickers around the ring, sweeping toward where most of them stand
            List<Body> order = new List<Body>();
            foreach (Body b in pb)
            {
                if (b != fin)
                {
                    order.Add(b);
                }
            }
            float sum = 0f;
            foreach (Body b in order)
            {
                sum += Mathf.DeltaAngle(phiL, Angle(b.At));
            }
            if (order.Count == 0)
            {
                sum = fin.Leader ? (Random.value < 0.5f ? 1f : -1f) : Mathf.DeltaAngle(phiL, Angle(fin.At));
            }
            float sweep = sum >= 0f ? 1f : -1f;
            order.Sort((x, y) => Offset(x, phiL, sweep).CompareTo(Offset(y, phiL, sweep)));
            order.Add(fin);

            // station angles: evenly paced, near where the kickers stand
            int n = order.Count;
            float first = Mathf.Clamp(order[0].Leader ? 70f : Offset(order[0], phiL, sweep), 55f, 120f);
            float gap = 0f;
            if (n > 1)
            {
                float last = order[n - 1].Leader ? first + 70f * (n - 1) : Offset(order[n - 1], phiL, sweep);
                gap = Mathf.Clamp((last - first) / (n - 1), 56f, 80f);
                if (first + gap * (n - 1) > 320f)
                {
                    gap = Mathf.Max(52f, (320f - first) / (n - 1));
                    first = Mathf.Max(40f, 320f - gap * (n - 1));
                }
            }
            float R = Random.Range(1.72f, 2f);

            // leg 0: the launch (armed: a rising slash; a small enemy or a big leader: a heave; else a rising kick)
            Leg launch = new Leg
            {
                B = _lead,
                Station = _c,
                Style = A.hasWeapon() && Random.value < 0.6f ? Style.Slash
                    : _v.actor_scale < A.actor_scale * 0.8f || _lead.K >= 1.5f || Random.value < 0.25f ? Style.Heave
                    : Style.Rising
            };
            _legs.Add(launch);

            // the leader steps beside the enemy, on its own side (close, but not on top of it)
            _side = (lp - _c).sqrMagnitude > 0.01f ? (lp - _c).normalized : Crew.Dir(phiL);
            launch.Stand = Combat.Clamp(_c + _side * StandOff());
            if (!Combat.SafeSpot(launch.Stand))
            {
                launch.Stand = lp;
            }

            // who will be standing where while the body is passed around: the leader beside the
            // launch spot, every striker near its station after its blow, the others where they
            // wait. Stations are chosen so that no blow is drawn on top of one of them.
            _standing.Clear();
            _standing.Add(new Vector3(launch.Stand.x, launch.Stand.y, _lead.K));
            Vector2 prev = _c;
            for (int i = 0; i < n; i++)
            {
                Body b = order[i];
                float ang = phiL + sweep * (first + gap * i);
                if (!Station(ang, R, prev, b, b == fin, order, i, out Vector2 st))
                {
                    if (b == fin)
                    {
                        return false;
                    }
                    // nowhere safe to stand: this one sits the relay out
                    b.Dead = true;
                    b.P?.Release(true);
                    Director.Release(b.U);
                    continue;
                }
                Leg L = new Leg { B = b, Station = st, Final = b == fin };
                L.In = (st - prev).sqrMagnitude > 0.0001f ? (st - prev).normalized : Crew.Dir(ang);
                _legs.Add(L);
                _standing.Add(new Vector3(st.x, st.y, b.K));
                prev = st;
            }
            if (_legs.Count < 2)
            {
                return false;
            }
            launch.Out = (_legs[1].Station - _c).normalized;
            for (int i = 1; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                L.Out = i + 1 < _legs.Count ? (_legs[i + 1].Station - L.Station).normalized : L.In;
            }

            // how each one strikes
            Style before = launch.Style;
            for (int i = 1; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                if (L.Final)
                {
                    L.Style = Level >= 3 ? Style.Fireball : Style.Smash;
                }
                else
                {
                    L.Style = PickKick(L, before);
                }
                before = L.Style;
                Place(L);
            }
            Leg end = _legs[_legs.Count - 1];
            if (end.Style == Style.Fireball && !ThrowSpot(end))
            {
                end.Style = Style.Smash;   // nowhere safe to throw from: spike it instead
                Place(end);
            }
            _finisherBody = end.B;
            foreach (Body b in _bodies)
            {
                _god |= !b.Dead && Tiers.Of(b.U) == Tier.God;
            }
            launch.In = -_side;
            _lead.M.Pose.FaceRight = Crew.Face(launch.Stand, _c, A.flip);
            return true;
        }

        /// <summary>How far beside the enemy the leader plants its feet for the launch.</summary>
        private float StandOff()
        {
            return 0.5f * _lead.K + 0.12f;
        }

        private Body NewBody(Actor u, Move m, TeamPartner p)
        {
            Combat.Allowed(u, _v, out float cm);
            return new Body
            {
                ClassMult = cm,
                U = u,
                M = m,
                P = p,
                Leader = p == null,
                Pal = Palette.For(u, Level),
                K = Crew.KOf(u),
                Px = Crew.PxOf(u),
                At = u.current_position,
                Act = Act.Idle,
                Breath = Random.Range(0f, 6f)
            };
        }

        private static bool Stronger(Actor x, Actor y)
        {
            int tx = Crew.Rank(x), ty = Crew.Rank(y);
            if (tx != ty)
            {
                return tx > ty;
            }
            return x.stats["damage"] > y.stats["damage"];
        }

        private float Angle(Vector2 p)
        {
            Vector2 d = p - _c;
            return d.sqrMagnitude > 0.0004f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;
        }

        private float Offset(Body b, float phiL, float sweep)
        {
            return Mathf.Repeat((Angle(b.At) - phiL) * sweep, 360f);
        }

        private static readonly float[] Radii = { 1f, 0.86f, 1.14f };
        private static readonly float[] Turns = { 0f, 12f, -12f, 24f, -24f, 36f, -36f };

        /// <summary>
        /// A safe station on the ring at about this angle (nudged in, out or around if the ground is
        /// bad). The game draws a body h high h above its ground point, so a station straight below a
        /// friend would put the blow (and a finisher's leap) right over that friend on screen: of the
        /// safe spots, the one whose blow is drawn clear of everyone standing about wins.
        /// </summary>
        private bool Station(float ang, float r, Vector2 prev, Body b, bool final, List<Body> order, int i, out Vector2 st)
        {
            // the screen column the blow takes up above the station: the enemy at the contact
            // height (and, for the axe kick, the finisher leaping above it)
            float k = b.K;
            float kv = Crew.KOf(_v);
            float hc, top;
            if (final && Level >= 3)
            {
                hc = Mathf.Clamp(1.05f + 0.2f * k, 1.15f, 1.7f);
                top = hc + 1.1f * kv;
            }
            else if (final)
            {
                hc = Mathf.Clamp(1.55f + 0.35f * k, 1.8f, 2.8f);
                top = hc + 0.5f * k + 1.15f * k;
            }
            else
            {
                hc = Mathf.Clamp(0.62f * k + 0.35f, 0.7f, 1.7f);
                top = hc + 1.1f * kv;
            }
            float half = 0.36f * Mathf.Max(kv, final && Level < 3 ? k : kv);
            st = _c;
            float best = float.MaxValue, bestClutter = 0f;
            bool found = false, moved = false;
            foreach (float t in Turns)
            {
                foreach (float f in Radii)
                {
                    float rr = r * f;
                    Vector2 p = _c + Crew.Dir(ang + t) * rr;
                    float chord = Vector2.Distance(p, prev);
                    if (chord < 1.4f || chord > 3.1f || !Combat.SafeSpot(p))
                    {
                        continue;
                    }
                    float clutter = Clutter(p, hc - 0.15f, top, half, b, order, i);
                    float cost = 6f * clutter + 0.012f * Mathf.Abs(t) + 0.5f * Mathf.Abs(rr - r);
                    if (cost < best)
                    {
                        best = cost;
                        bestClutter = clutter;
                        moved = t != 0f || f != 1f;
                        st = p;
                        found = true;
                    }
                }
                if (found && best <= 0.0001f)
                {
                    break;   // clear where it was meant to be: no need to look around
                }
            }
            if (found)
            {
                _moved += moved ? 1 : 0;
                _overlap += bestClutter;
            }
            return found;
        }

        // dev log: stations moved off their place on the ring to keep a blow clear of a friend, and
        // whatever overlap could not be avoided
        private int _moved;
        private float _overlap;

        /// <summary>
        /// How much of the screen column over 'p' (lo..hi above it, 'half' to each side) is covered by
        /// the bodies standing about: the leader, the strikers already placed, and the partners still
        /// waiting for their turn (less: they may be moving off by then). The striker itself is left
        /// out (it rises into that column on purpose).
        /// </summary>
        private float Clutter(Vector2 p, float lo, float hi, float half, Body self, List<Body> order, int i)
        {
            float sum = 0f;
            // (entry 0 is the leader beside the launch spot: it has left it when it closes the circle)
            for (int s = self.Leader ? 1 : 0; s < _standing.Count; s++)
            {
                Vector3 u = _standing[s];
                sum += Cover(p, lo, hi, half, u.x, u.y, u.z);
            }
            for (int j = i + 1; j < order.Count; j++)
            {
                Body w = order[j];
                if (w != self && !w.Leader && !w.Dead)
                {
                    sum += 0.6f * Cover(p, lo, hi, half, w.At.x, w.At.y, w.K);
                }
            }
            return sum;
        }

        /// <summary>
        /// The overlap (on screen) of a column over p with a unit standing at (x, y) of size k, with
        /// a little air around the unit (a blow that only grazes a friend's head still reads as
        /// landing on it).
        /// </summary>
        private static float Cover(Vector2 p, float lo, float hi, float half, float x, float y, float k)
        {
            float bw = 0.36f * k + 0.15f;
            float ox = half + bw - Mathf.Abs(p.x - x);
            if (ox <= 0f)
            {
                return 0f;
            }
            float oy = Mathf.Min(p.y + hi, y + 1.15f * k + 0.25f) - Mathf.Max(p.y + lo, y - 0.35f);
            if (oy <= 0f)
            {
                return 0f;
            }
            return oy * Mathf.Min(1f, ox / (half + bw));
        }

        /// <summary>Which kick: beasts swipe up; a shallow turn invites a bicycle kick; a long throw a spinning heel.</summary>
        private Style PickKick(Leg L, Style before)
        {
            if (L.B.K >= 1.5f)
            {
                return Style.Uppercut;
            }
            float turn = Vector2.Angle(L.In, L.Out);
            float next = 0f;
            int i = _legs.IndexOf(L);
            if (i + 1 < _legs.Count)
            {
                next = Vector2.Distance(L.Station, _legs[i + 1].Station);
            }
            List<Style> pool = new List<Style> { Style.Scissor, Style.Heel, Style.Uppercut };
            if (turn < 70f)
            {
                pool.Add(Style.Overhead);
                pool.Add(Style.Overhead);
            }
            if (next > 2.3f)
            {
                pool.Add(Style.Heel);
            }
            if (Weights.Species(_v) == Weight.Critter)
            {
                pool.Add(Style.Uppercut);
            }
            pool.RemoveAll(s => s == before);
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : Style.Scissor;
        }

        /// <summary>Where the striker stands and how high the enemy is when it is struck.</summary>
        private void Place(Leg L)
        {
            float k = L.B.K;
            Vector2 want;
            switch (L.Style)
            {
                case Style.Overhead:
                    want = L.Station + L.In * 0.3f * k;
                    L.Hc = Mathf.Clamp(0.62f * k + 0.4f, 0.75f, 1.75f);
                    break;
                case Style.Smash:
                    want = L.Station + L.In * 0.4f * k;
                    L.Hc = Mathf.Clamp(1.55f + 0.35f * k, 1.8f, 2.8f);
                    break;
                case Style.Fireball:
                    want = L.Station;   // ThrowSpot moves it
                    L.Hc = Mathf.Clamp(1.05f + 0.2f * k, 1.15f, 1.7f);
                    break;
                default:
                    want = L.Station - L.Out * 0.38f * k;
                    L.Hc = Mathf.Clamp(0.62f * k + 0.35f, 0.7f, 1.7f);
                    break;
            }
            L.Stand = Combat.SafeSpot(want) ? Combat.Clamp(want) : L.Station;
        }

        /// <summary>
        /// The fireball is thrown from beyond the landing spot, facing the falling enemy; of the safe
        /// spots, one where the thrower doesn't stand on (or in front of) a friend.
        /// </summary>
        private bool ThrowSpot(Leg L)
        {
            float k = L.B.K;
            float[] dists = { 1.65f * k, 1.3f * k, 2f * k };
            float[] turns = { 0f, 22f, -22f, 40f, -40f };
            float best = float.MaxValue;
            for (int ti = 0; ti < turns.Length; ti++)
            {
                for (int di = 0; di < dists.Length; di++)
                {
                    Vector2 p = L.Station + Crew.Rotate(L.In, turns[ti]) * dists[di];
                    if (!Combat.SafeSpot(p) || Vector2.Distance(p, _c) <= 0.8f)
                    {
                        continue;
                    }
                    float cover = 0f;
                    // (the last entry is the finisher's own station; entry 0 the leader, gone if it throws)
                    for (int s = L.B.Leader ? 1 : 0; s < _standing.Count - 1; s++)
                    {
                        Vector3 u = _standing[s];
                        cover += Cover(p, -0.1f, 1.15f * k, 0.36f * k, u.x, u.y, u.z);
                    }
                    float cost = 6f * cover + 0.1f * ti + 0.05f * di;
                    if (cost < best)
                    {
                        best = cost;
                        L.Stand = p;
                    }
                }
                if (best <= 0.0001f)
                {
                    break;
                }
            }
            return best < float.MaxValue;
        }

        private static float Lead(Style s)
        {
            switch (s)
            {
                case Style.Rising:
                case Style.Slash:
                    return 0.14f;
                case Style.Heave:
                    return 0.2f;
                case Style.Scissor:
                    return 0.17f;
                case Style.Smash:
                    return 0.36f;
                case Style.Fireball:
                    return 0.62f;
                default:
                    return 0.2f;
            }
        }

        /// <summary>The extra height a blow of this style gives the next arc.</summary>
        private static float Loft(Style s)
        {
            switch (s)
            {
                case Style.Uppercut:
                    return 0.45f;
                case Style.Heave:
                case Style.Overhead:
                    return 0.35f;
                case Style.Rising:
                case Style.Slash:
                    return 0.2f;
                case Style.Heel:
                    return -0.25f;
                default:
                    return 0f;
            }
        }

        /// <summary>Times every blow: flight lengths from the distances, approaches early enough to make it.</summary>
        private void Schedule()
        {
            Leg launch = _legs[0];
            float d0 = Vector2.Distance(A.current_position, launch.Stand);
            launch.ApproachDur = d0 > 0.3f ? Mathf.Clamp(0.06f + 0.04f * d0, 0.08f, 0.24f) : 0.04f;
            launch.ApproachStart = 0f;
            launch.Contact = launch.ApproachDur + Lead(launch.Style);
            float t = launch.Contact;
            Vector2 prev = _c;
            float prevH = 0f;
            Style prevStyle = launch.Style;
            bool first = true;
            for (int i = 1; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                if (L.Skipped || L.B.Dead)
                {
                    L.Skipped = true;
                    continue;
                }
                float dist = Vector2.Distance(prev, L.Station);
                // the launch throws it high into the sky (the opening beat); the kicks after it bat it on
                float pop = first ? 0.75f + 0.2f * Mathf.Min(_lead.K, 1.6f) : 0f;
                float D;
                // the passes are brisk (the next kicker is already under the body when it comes
                // down), and brisker still from Master up (_tempo): a relay of masters is a blur of
                // kicks; the finisher's leap or conjuring may start while the last kick lands
                if (L.Style == Style.Smash)
                {
                    L.H = 0.3f + 0.12f * dist + Mathf.Max(0f, Loft(prevStyle)) * 0.5f + pop * 0.6f;
                    D = Mathf.Max(0.32f, (0.42f + 0.06f * dist + 0.08f * pop) * _tempo);
                }
                else if (L.Style == Style.Fireball)
                {
                    L.H = 1.15f + 0.2f * dist + Mathf.Max(0f, Loft(prevStyle)) + pop * 0.5f;
                    D = Mathf.Max(0.48f, (0.62f + 0.06f * dist + 0.06f * pop) * _tempo);
                }
                else
                {
                    L.H = Mathf.Max(0.6f, 0.9f + 0.25f * dist + Loft(prevStyle)) + pop;
                    D = Mathf.Clamp(0.26f + 0.065f * dist + 0.085f * L.H + 0.04f * Mathf.Abs(L.Hc - prevH), 0.38f, first ? 0.8f : 0.7f) * _tempo;
                }
                first = false;
                Vector2 start = L.B.Leader ? launch.Stand : L.B.At;
                float d = Vector2.Distance(start, L.Stand);
                L.LeapIn = d > 0.25f && (d > 3.2f || !Combat.ClearPath(start, L.Stand) || (L.Style != Style.Fireball && d > 1.6f && Random.value < 0.35f));
                L.ApproachDur = d <= 0.02f ? 0f : L.LeapIn ? 0.26f + 0.035f * d : 0.06f + 0.035f * d;
                float lead = Lead(L.Style);
                float earliest = L.B.Leader ? launch.Contact + 0.36f : 0.04f;
                L.ApproachStart = Mathf.Max(earliest, Mathf.Min(t - 0.12f, t + D - lead - 0.06f - L.ApproachDur));
                L.Contact = Mathf.Max(t + D, L.ApproachStart + L.ApproachDur + 0.06f + lead);
                t = L.Contact;
                prev = L.Station;
                prevH = L.Hc;
                prevStyle = L.Style;
            }
            _length = t + 1.4f;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _vis = Fx.OnScreen(_c, 0.5f);
            Watchdog();
            if (_cur == 0 && !_vGone && !_legs[0].Done)
            {
                Track();
            }
            if (_hold > 0f)
            {
                // hit-stop: everyone holds the pose, shaking with the blow
                _hold -= dt;
                float n = ((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f;
                foreach (Body b in _bodies)
                {
                    if (!b.Dead)
                    {
                        b.M.Pose.Nudge = new Vector2(n, 0f);
                    }
                }
                if (_p != null && !_vGone)
                {
                    _p.Pose.Nudge = new Vector2(-n, 0f);
                }
                return;
            }
            foreach (Body b in _bodies)
            {
                if (!b.Dead)
                {
                    b.M.Pose.Nudge = Vector2.zero;
                }
            }
            if (_p != null && !_vGone)
            {
                _p.Pose.Nudge = Vector2.zero;
            }
            _clock += dt;
            Engage();
            foreach (Body b in _bodies)
            {
                if (!b.Dead)
                {
                    Animate(b, dt);
                }
            }
            LetGo();
            if (!_vGone)
            {
                Victim(dt);
            }
            ArcTrail();
            Shadow();
            Contacts();
            Fireball(dt);
            if (_ending)
            {
                Ending();
            }
            else if (_clock > _length + 3f)
            {
                StartEnding(0f);   // something stalled: wind down
            }
        }

        /// <summary>Until the launch the enemy is free: the leader keeps beside it (its dash bends to follow).</summary>
        private void Track()
        {
            Leg launch = _legs[0];
            Crew.Pin(_v, 0.2f);   // renewed every tick until the launch, whatever the game speed
            Vector2 vp = _v.current_position;
            if ((vp - _c).sqrMagnitude > 2.56f)
            {
                return;   // it got away: the launch will whiff
            }
            Vector2 s = Combat.Clamp(vp + _side * StandOff());
            if (Combat.SafeSpot(s))
            {
                launch.Stand = s;
            }
            launch.Station = vp;
        }

        /// <summary>
        /// A Master or better who has played its part (its kick landed, it is back on its feet, the
        /// finish isn't its) doesn't stand about watching the rest of the relay: it is let go to
        /// fight on at once (the juggled enemy stays the relay's: nobody else may touch it).
        /// </summary>
        private void LetGo()
        {
            foreach (Body b in _bodies)
            {
                if (b.Dead || b.P == null || b.Act != Act.Watch || b == _finisherBody || !Crew.Flow(b.U))
                {
                    continue;
                }
                bool done = true;
                for (int i = 1; i < _legs.Count && done; i++)
                {
                    Leg L = _legs[i];
                    if (L.B == b && !L.Done && !L.Skipped)
                    {
                        done = false;
                    }
                }
                if (!done)
                {
                    continue;
                }
                b.Dead = true;
                b.P.Pose.Nudge = Vector2.zero;
                b.P.Pose.SX = b.P.Pose.SY = 1f;
                b.P.Pose.Rot = 0f;
                b.P.Release(true);
                Director.Release(b.U);
                Crew.Rest(b.U);
                if (Dev.On)
                {
                    Dev.Trace("relay " + Id + " lets " + b.U.asset.id + "#" + b.U.Sid() + " (" + Tiers.Of(b.U) + ") go after its kick at t=" + _clock.ToString("F2"));
                }
            }
        }

        /// <summary>Who is still here? Skips lost partners; winds down when the enemy is gone.</summary>
        private void Watchdog()
        {
            foreach (Body b in _bodies)
            {
                if (b.Dead || b.P == null || !b.P.Gone)
                {
                    continue;
                }
                b.Dead = true;
                if (Dev.On)
                {
                    Dev.Trace("relay " + Id + " lost a partner at t=" + _clock.ToString("F2"));
                }
                for (int i = 1; i < _legs.Count; i++)
                {
                    Leg L = _legs[i];
                    if (L.B == b && !L.Done && !L.Skipped)
                    {
                        L.Skipped = true;
                        if (i == _cur)
                        {
                            Reroute();
                        }
                    }
                }
            }
            if (!_vGone && (!_v.isAlive() || _v.Sid() != _vid || (_p != null && _p.Finished && _vs != VState.Done)))
            {
                _vGone = true;
                if (_p != null && !_p.Finished)
                {
                    _p.Release(_p.Pose.Height <= 0.02f);
                }
                if (Dev.On)
                {
                    Dev.Trace("relay " + Id + " lost the enemy at t=" + _clock.ToString("F2"));
                }
                StartEnding(0.1f);
            }
        }

        /// <summary>Partners set off for their stations when their turn comes near.</summary>
        private void Engage()
        {
            if (_ending)
            {
                return;
            }
            for (int i = 0; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                if (L.Engaged || L.Skipped || L.Done || L.B.Dead)
                {
                    continue;
                }
                Body b = L.B;
                bool busy = b.Act == Act.Approach || b.Act == Act.Strike || b.Act == Act.Air || b.Act == Act.Land;
                if (busy || _clock < L.ApproachStart)
                {
                    continue;
                }
                L.Engaged = true;
                b.Leg = L;
                if (L.ApproachDur > 0f && Vector2.Distance(b.At, L.Stand) > 0.02f)
                {
                    StartAct(b, Act.Approach, L.ApproachDur);
                    b.From = b.At;
                    b.To = L.Stand;
                    b.Leap = L.LeapIn;
                    b.HFrom = 0f;
                    if (_vis && Vector2.Distance(b.From, b.To) > 0.4f)
                    {
                        Fx.LandingPuff(b.At, b.Px, b.U.current_tile);
                    }
                    if (b.Leap || Vector2.Distance(b.From, b.To) > 2f)
                    {
                        Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", b.U.current_tile);
                    }
                }
                else
                {
                    b.At = L.Stand;
                    StartAct(b, Act.Ready, 0f);
                }
            }
            // a strike begins 'lead' before its blow, wherever the striker is
            for (int i = 0; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                if (!L.Engaged || L.Skipped || L.Done || L.B.Dead || L.B.Act == Act.Strike || L.B.Leg != L)
                {
                    continue;
                }
                if (_clock >= L.Contact - Lead(L.Style))
                {
                    Body b = L.B;
                    StartAct(b, Act.Strike, 0f);
                    b.From = b.M.Pose.Ground;
                    b.HFrom = b.M.Pose.Height;
                    b.M.Pose.FaceRight = Facing(L, b);
                    if (L.Style == Style.Fireball)
                    {
                        Conjure(b);
                    }
                }
            }
        }

        private bool Facing(Leg L, Body b)
        {
            switch (L.Style)
            {
                case Style.Overhead:
                case Style.Smash:
                case Style.Fireball:
                    return Crew.Face(L.Stand, L.Stand - L.In, b.M.Pose.FaceRight);
                case Style.Rising:
                case Style.Heave:
                case Style.Slash:
                    return Crew.Face(L.Stand, _c, b.M.Pose.FaceRight);
                default:
                    return Crew.Face(L.Stand, L.Stand + L.Out, b.M.Pose.FaceRight);
            }
        }

        private void StartAct(Body b, Act act, float dur)
        {
            b.Act = act;
            b.T0 = _clock;
            b.Dur = dur;
            b.F1 = b.F2 = b.F3 = false;
        }

        // ------------------------------------------------------------ bodies --

        private void Animate(Body b, float dt)
        {
            switch (b.Act)
            {
                case Act.Approach:
                    Approach(b, dt);
                    break;
                case Act.Ready:
                    Ready(b);
                    break;
                case Act.Strike:
                    Strike(b, dt);
                    break;
                case Act.Air:
                    Air(b, dt);
                    break;
                case Act.Land:
                    Land(b);
                    break;
                default:
                    Stand(b, dt);
                    break;
            }
        }

        /// <summary>Standing by (or after its part): facing the enemy, breathing.</summary>
        private void Stand(Body b, float dt)
        {
            Move m = b.M;
            m.Pose.Ground = b.At;
            m.Pose.Height = 0f;
            m.Pose.Rot = Mathf.MoveTowards(Mathf.DeltaAngle(0f, m.Pose.Rot), 0f, 600f * dt);
            float br = Mathf.Sin(_clock * 6f + b.Breath);
            m.Pose.SX = 1f - 0.02f * br;
            m.Pose.SY = 1f + 0.025f * br;
            Vector2 look = _p != null && !_vGone ? _p.Pose.Ground : _v != null && _v.isAlive() ? _v.current_position : _c;
            m.Pose.FaceRight = Crew.Face(b.At, look, m.Pose.FaceRight);
        }

        /// <summary>A dash (afterimages) or a leap (an arc, a flip on long ones) to its station.</summary>
        private void Approach(Body b, float dt)
        {
            Move m = b.M;
            if (b.Leader && b.Leg == _legs[0] && !_legs[0].Done)
            {
                b.To = _legs[0].Stand;   // the enemy shuffled: the dash follows it
            }
            float k = b.Dur > 0f ? Mathf.Clamp01((_clock - b.T0) / b.Dur) : 1f;
            float dist = Vector2.Distance(b.From, b.To);
            m.Pose.FaceRight = Crew.Face(b.From, b.To, m.Pose.FaceRight);
            if (b.Leap)
            {
                float apex = (0.45f + 0.12f * dist) * b.K;
                m.Pose.Ground = Vector2.Lerp(b.From, b.To, k);
                m.Pose.Height = 4f * apex * k * (1f - k);
                m.Pose.Rot = dist > 2.4f ? 360f * Anim.InOutCubic(Mathf.Clamp01((k - 0.15f) / 0.7f)) : 10f * Mathf.Sin(Mathf.PI * k);
                float s = Mathf.Sin(Mathf.PI * k);
                m.Pose.SX = k < 0.2f ? Mathf.Lerp(0.8f, 1f, k / 0.2f) : 1f - 0.06f * s;
                m.Pose.SY = k < 0.2f ? Mathf.Lerp(1.25f, 1f, k / 0.2f) : 1f - 0.1f * s;
            }
            else
            {
                m.Pose.Ground = Vector2.Lerp(b.From, b.To, Anim.OutQuad(k));
                m.Pose.Height = 0f;
                float lean = Mathf.Sin(Mathf.PI * k);
                m.Pose.Rot = 16f * lean;
                m.Pose.SX = 1f + 0.12f * lean;
                m.Pose.SY = 1f - 0.1f * lean;
            }
            if (_vis && dist > 0.6f)
            {
                b.Ghost -= dt;
                if (b.Ghost <= 0f)
                {
                    b.Ghost = 0.025f;
                    Fx.Afterimage(m, 0.14f, b.Pal.Ghost, b.Pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                b.At = b.To;
                m.Pose.Height = 0f;
                m.Pose.Rot = Mathf.DeltaAngle(0f, m.Pose.Rot);   // a full flip ends upright, not unwinding
                if (b.Leap && _vis)
                {
                    Fx.LandingPuff(b.At, b.Px, b.U.current_tile);
                }
                StartAct(b, Act.Ready, 0f);
            }
        }

        /// <summary>Crouched in a ready stance under the falling enemy.</summary>
        private void Ready(Body b)
        {
            Move m = b.M;
            float k = Anim.OutQuad(Mathf.Clamp01((_clock - b.T0) / 0.08f));
            m.Pose.Ground = b.At;
            m.Pose.Height = 0f;
            m.Pose.Rot = Mathf.Lerp(m.Pose.Rot, -4f, k);
            m.Pose.SX = Mathf.Lerp(1f, 1.08f, k);
            m.Pose.SY = Mathf.Lerp(1f, 0.9f, k);
            if (b.Leg != null)
            {
                m.Pose.FaceRight = Facing(b.Leg, b);
            }
        }

        /// <summary>Falling back to the ground after a blow (a recoil up first if HUp), turning upright.</summary>
        private void Air(Body b, float dt)
        {
            Move m = b.M;
            float k = b.Dur > 0f ? Mathf.Clamp01((_clock - b.T0) / b.Dur) : 1f;
            float up = Anim.OutQuad(Mathf.Clamp01(k / 0.2f));
            float down = b.HUp > 0f ? Anim.InOutSine(Mathf.Clamp01((k - 0.2f) / 0.8f)) : k * k;
            float h0 = b.HUp > 0f ? Mathf.Lerp(b.HFrom, b.HFrom + b.HUp, up) : b.HFrom;
            m.Pose.Height = Mathf.Max(0f, Mathf.Lerp(h0, 0f, down));
            m.Pose.Ground = Vector2.Lerp(b.From, b.To, Anim.InOutSine(k));
            m.Pose.Rot = Mathf.Lerp(b.RotFrom, b.RotTo, Anim.OutCubic(Mathf.Clamp01(k / 0.65f)));
            float tuck = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.65f));
            m.Pose.SX = 1f - 0.05f * tuck;
            m.Pose.SY = 1f - 0.1f * tuck;
            if (_vis && k < 0.6f)
            {
                b.Ghost -= dt;
                if (b.Ghost <= 0f)
                {
                    b.Ghost = 0.045f;
                    Fx.Afterimage(m, 0.12f, Fx.WithA(b.Pal.Ghost, b.Pal.Ghost.a * 0.6f), b.Pal.GhostEnd);
                }
            }
            if (k >= 1f)
            {
                b.At = b.To;
                m.Pose.Height = 0f;
                m.Pose.Rot = 0f;
                StartAct(b, Act.Land, Crisp(b, 0.26f));
                if (_vis)
                {
                    Fx.LandingPuff(b.At, b.Px, Combat.TileAt(b.At));
                }
            }
        }

        /// <summary>A landing's spring (seconds), crisper from Master up: it lands and is ready.</summary>
        private static float Crisp(Body b, float seconds)
        {
            return Mathf.Max(0.1f, seconds * Crew.Pace(b.U));
        }

        private void Land(Body b)
        {
            Move m = b.M;
            float t = _clock - b.T0;
            // the same squash and spring, played faster by a master (it settles within its shorter landing)
            float amp = Anim.Spring(t / Crew.Pace(b.U), 9f, 18f);
            m.Pose.Ground = b.At;
            m.Pose.Height = 0f;
            m.Pose.Rot = 0f;
            m.Pose.SX = 1f + 0.16f * amp;
            m.Pose.SY = 1f - 0.18f * amp;
            if (t >= b.Dur)
            {
                m.Pose.SX = m.Pose.SY = 1f;
                StartAct(b, Act.Watch, 0f);
            }
        }

        /// <summary>From the strike pose into the fall back down.</summary>
        private void Descend(Body b, float rotTo, float hUp, float dur, Vector2 to)
        {
            Move m = b.M;
            StartAct(b, Act.Air, dur);
            b.From = m.Pose.Ground;
            b.To = Combat.SafeSpot(to) ? to : Combat.SafeSpot(b.From) ? b.From : b.Leg != null ? b.Leg.Stand : b.At;
            b.HFrom = m.Pose.Height;
            b.HUp = hUp;
            b.RotFrom = m.Pose.Rot;
            b.RotTo = rotTo;
        }

        // ------------------------------------------------------------ strikes --

        private void Strike(Body b, float dt)
        {
            Leg L = b.Leg;
            if (L == null)
            {
                StartAct(b, Act.Watch, 0f);
                return;
            }
            Move m = b.M;
            float tau = _clock - L.Contact;
            float lead = Lead(L.Style);
            float k0 = Mathf.Clamp01((tau + lead) / Mathf.Max(0.05f, lead * 0.5f));
            float fwd = m.Pose.FaceRight ? 1f : -1f;
            float K = b.K;
            if (L.Style != Style.Smash)
            {
                m.Pose.Ground = Vector2.Lerp(b.From, L.Stand, Anim.OutQuad(k0));
            }
            float hK = Mathf.Max(0.12f, L.Hc - 0.42f * K);
            switch (L.Style)
            {
                case Style.Rising:
                case Style.Slash:
                    if (tau < -0.06f)
                    {
                        Crouch(m, Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.08f)), -6f);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((tau + 0.06f) / 0.2f);
                        m.Pose.SX = Mathf.Lerp(0.86f, 1f, k);
                        m.Pose.SY = Mathf.Lerp(1.24f, 1f, k);
                        m.Pose.Rot = Mathf.Lerp(-6f, -30f, Anim.OutCubic(Mathf.Clamp01(k * 2f))) * (1f - Anim.Smooth(0.55f, 1f, k));
                        m.Pose.Height = 0.3f * K * Mathf.Sin(Mathf.PI * k);
                        if (!b.F1 && tau >= -0.05f)
                        {
                            b.F1 = true;
                            Swish(b, L.Style == Style.Slash ? Art.Arc(12, 1f, -60f, 120f, 3) : Art.Arc(10, 1f, -70f, 115f, 3), 0.16f, new Vector2(fwd, -1f));
                        }
                    }
                    if (tau >= 0.14f)
                    {
                        m.Pose.Height = 0f;
                        b.At = m.Pose.Ground;
                        StartAct(b, Act.Land, Crisp(b, 0.2f));
                    }
                    break;
                case Style.Heave:
                    if (tau < -0.05f)
                    {
                        float c = Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.15f));
                        Crouch(m, c, 20f);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((tau + 0.05f) / 0.21f);
                        m.Pose.Rot = Mathf.Lerp(20f, -28f, Anim.OutCubic(Mathf.Clamp01(k * 2.2f))) * (1f - Anim.Smooth(0.55f, 1f, k));
                        m.Pose.SX = Mathf.Lerp(0.82f, 1f, k);
                        m.Pose.SY = Mathf.Lerp(1.3f, 1f, k);
                        m.Pose.Height = 0.18f * K * Mathf.Sin(Mathf.PI * k);
                        if (!b.F1 && tau >= -0.04f)
                        {
                            b.F1 = true;
                            Swish(b, Art.Arc(11, 1.1f, -40f, 100f, 3), 0.18f, new Vector2(fwd * 1.5f, 0f));
                        }
                    }
                    if (tau >= 0.16f)
                    {
                        m.Pose.Height = 0f;
                        b.At = m.Pose.Ground;
                        StartAct(b, Act.Land, Crisp(b, 0.2f));
                    }
                    break;
                case Style.Scissor:
                    if (tau < -0.1f)
                    {
                        Crouch(m, Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.07f)), -4f);
                    }
                    else if (tau < 0f)
                    {
                        float k = (tau + 0.1f) / 0.1f;
                        m.Pose.Height = hK * Anim.OutQuad(k);
                        m.Pose.SX = Mathf.Lerp(0.82f, 1f, k);
                        m.Pose.SY = Mathf.Lerp(1.24f, 1f, k);
                        m.Pose.Rot = Mathf.Lerp(-10f, 45f, k);
                        Trail(b, dt, 0.025f);
                    }
                    else
                    {
                        float k = Mathf.Clamp01(tau / 0.12f);
                        m.Pose.Height = hK + 0.06f * K * Mathf.Sin(Mathf.PI * k);
                        m.Pose.Rot = 45f + 30f * Anim.OutQuad(k);
                        m.Pose.SX = m.Pose.SY = 1f;
                    }
                    if (!b.F1 && tau >= -0.05f)
                    {
                        b.F1 = true;
                        Swish(b, Art.Arc(9, 0.85f, -60f, 80f, 2), 0.14f, new Vector2(fwd * 1.5f, 0f));
                    }
                    if (!b.F2 && tau >= -0.015f)
                    {
                        b.F2 = true;
                        Swish(b, Art.Arc(9, 0.85f, 110f, -10f, 2), 0.14f, new Vector2(fwd * 1.5f, 0f));
                    }
                    if (tau >= 0.12f)
                    {
                        Descend(b, 0f, 0f, 0.3f, L.Stand + L.Out * 0.25f * K);
                    }
                    break;
                case Style.Heel:
                    if (tau < -0.12f)
                    {
                        Crouch(m, Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.08f)), -15f);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((tau + 0.12f) / 0.2f);
                        m.Pose.Height = hK * Anim.OutQuad(Mathf.Clamp01(k * 1.5f));
                        m.Pose.Rot = -15f + 375f * Anim.InOutCubic(k);
                        float tuck = Mathf.Sin(Mathf.PI * k);
                        m.Pose.SX = 1f - 0.06f * tuck;
                        m.Pose.SY = 1f - 0.14f * tuck;
                        Trail(b, dt, 0.03f);
                    }
                    if (!b.F1 && tau >= -0.1f)
                    {
                        b.F1 = true;
                        Swish(b, Art.Arc(11, 0.45f, 180f, 540f, 3), 0.22f, Vector2.zero);
                        Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", b.U.current_tile);
                    }
                    if (tau >= 0.08f)
                    {
                        Descend(b, 360f, 0f, 0.32f, L.Stand + L.Out * 0.2f * K);
                    }
                    break;
                case Style.Uppercut:
                    if (tau < -0.06f)
                    {
                        float c = Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.12f));
                        m.Pose.SX = Mathf.Lerp(1f, 1.2f, c);
                        m.Pose.SY = Mathf.Lerp(1f, 0.76f, c);
                        m.Pose.Rot = 8f * c;
                        m.Pose.Height = 0f;
                    }
                    else
                    {
                        float k = Mathf.Clamp01((tau + 0.06f) / 0.16f);
                        m.Pose.Height = (hK + 0.15f * K) * Anim.OutCubic(k);
                        m.Pose.SX = Mathf.Lerp(0.74f, 1f, k);
                        m.Pose.SY = Mathf.Lerp(1.36f, 1f, k);
                        m.Pose.Rot = Mathf.Lerp(8f, -24f, Anim.OutCubic(Mathf.Clamp01(k * 2f)));
                        Trail(b, dt, 0.025f);
                    }
                    if (!b.F1 && tau >= -0.05f)
                    {
                        b.F1 = true;
                        Swish(b, Art.Arc(10, 1f, -70f, 115f, 3), 0.16f, new Vector2(fwd, -1f));
                    }
                    if (tau >= 0.1f)
                    {
                        Descend(b, 0f, 0f, 0.32f, L.Stand);
                    }
                    break;
                case Style.Overhead:
                    if (tau < -0.12f)
                    {
                        Crouch(m, Anim.OutQuad(Mathf.Clamp01((tau + lead) / 0.08f)), -6f);
                    }
                    else
                    {
                        float k = Mathf.Clamp01((tau + 0.12f) / 0.2f);
                        m.Pose.Height = (hK + 0.1f * K) * Anim.OutQuad(Mathf.Clamp01(k * 1.6f));
                        m.Pose.Rot = -205f * Anim.InOutCubic(k);
                        float tuck = Mathf.Sin(Mathf.PI * k);
                        m.Pose.SX = 1f - 0.05f * tuck;
                        m.Pose.SY = 1f - 0.12f * tuck;
                        Trail(b, dt, 0.03f);
                    }
                    if (!b.F1 && tau >= -0.05f)
                    {
                        b.F1 = true;
                        Swish(b, Art.Arc(10, 0.9f, 10f, 190f, 3), 0.16f, new Vector2(0f, 2f));
                    }
                    if (tau >= 0.08f)
                    {
                        Descend(b, -360f, 0f, 0.34f, L.Stand);
                    }
                    break;
                case Style.Smash:
                    SmashPose(b, L, tau, dt);
                    break;
                case Style.Fireball:
                    FireballPose(b, L, tau);
                    break;
            }
            if (b.Act == Act.Strike && b.HFrom > 0.02f && k0 < 1f)
            {
                m.Pose.Height = Mathf.Max(m.Pose.Height, b.HFrom * (1f - Anim.OutQuad(k0)));
            }
        }

        private static void Crouch(Move m, float c, float rot)
        {
            m.Pose.SX = Mathf.Lerp(1f, 1.12f, c);
            m.Pose.SY = Mathf.Lerp(1f, 0.84f, c);
            m.Pose.Rot = rot * c;
            m.Pose.Height = 0f;
        }

        /// <summary>I/II finisher: leap above the falling enemy with a flip, then an axe kick straight down.</summary>
        private void SmashPose(Body b, Leg L, float tau, float dt)
        {
            Move m = b.M;
            float K = b.K;
            Vector2 over = L.Station + L.In * 0.12f * K;
            float top = L.Hc + 0.5f * K;
            if (tau < -0.3f)
            {
                m.Pose.Ground = Vector2.Lerp(b.From, L.Stand, Anim.OutQuad(Mathf.Clamp01((tau + 0.36f) / 0.04f)));
                Crouch(m, Anim.OutQuad(Mathf.Clamp01((tau + 0.36f) / 0.06f)), -8f);
                m.Pose.SX = Mathf.Lerp(1f, 1.14f, Mathf.Clamp01((tau + 0.36f) / 0.06f));
                m.Pose.SY = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01((tau + 0.36f) / 0.06f));
            }
            else if (tau < -0.08f)
            {
                float k = (tau + 0.3f) / 0.22f;
                if (!b.F1)
                {
                    b.F1 = true;
                    Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", b.U.current_tile);
                    if (_vis)
                    {
                        Fx.Ring(L.Stand, b.Px, 0.2f, 2f, 12f * K, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.5f,
                            Fx.WithA(b.Pal.Core, 0.85f), Fx.WithA(b.Pal.Edge, 0f));
                        Fx.LandingPuff(L.Stand, b.Px, Combat.TileAt(L.Stand));
                    }
                }
                m.Pose.Ground = Vector2.Lerp(L.Stand, over, Anim.OutQuad(k));
                m.Pose.Height = top * Anim.OutCubic(k);
                m.Pose.Rot = 322f * Anim.InOutCubic(Mathf.Clamp01((k - 0.15f) / 0.85f));
                float s = Anim.OutQuad(Mathf.Clamp01(k / 0.6f));
                m.Pose.SX = Mathf.Lerp(0.78f, 1f, s);
                m.Pose.SY = Mathf.Lerp(1.3f, 1f, s);
                Trail(b, dt, 0.028f);
            }
            else
            {
                float k = Mathf.Clamp01((tau + 0.08f) / 0.14f);
                m.Pose.Ground = over;
                m.Pose.Height = top - 0.2f * K * Anim.OutQuad(k);
                m.Pose.Rot = Mathf.Lerp(322f, 470f, Anim.OutCubic(k));
                m.Pose.SX = m.Pose.SY = 1f;
                if (!b.F2)
                {
                    b.F2 = true;
                    float fwd = m.Pose.FaceRight ? 1f : -1f;
                    Swish(b, Art.Arc(11, 1f, 130f, -65f, 3), 0.16f, new Vector2(fwd * 1.5f, -1f));
                }
            }
            if (tau >= 0.06f)
            {
                // (a master drops out of the axe kick faster: it is on its feet as the enemy bounces)
                Descend(b, 720f, 0.28f * K, Crew.Flow(b.U) ? 0.42f : 0.55f, L.Station + L.In * 0.8f * K);
            }
        }

        /// <summary>III finisher: feet planted, a ball of fire forms at the hands, then the throw.</summary>
        private void FireballPose(Body b, Leg L, float tau)
        {
            Move m = b.M;
            const float throwAt = -0.17f;
            if (tau < throwAt)
            {
                float k = Mathf.Clamp01((tau + 0.62f) / 0.45f);
                float c = Anim.OutQuad(Mathf.Clamp01(k * 3f));
                m.Pose.Height = 0f;
                m.Pose.SX = Mathf.Lerp(1f, 1.1f, c);
                m.Pose.SY = Mathf.Lerp(1f, 0.9f, c);
                m.Pose.Rot = Mathf.Lerp(0f, -14f, c) - 4f * k;
                // the power tremble grows with the ball
                m.Pose.Nudge = k > 0.4f ? new Vector2(((int)(_clock * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                Glow(b, Fx.WithA(Kit.Fire1, 0.45f * Anim.Smooth(0.1f, 1f, k) * (0.8f + 0.2f * Mathf.Sin(_clock * 40f))));
            }
            else
            {
                Vector2 toward = (L.Station - L.Stand).sqrMagnitude > 0.0001f ? (L.Station - L.Stand).normalized : -L.In;
                Vector2 lunge = L.Stand + toward * 0.15f * b.K;
                if (!Combat.SafeSpot(lunge))
                {
                    lunge = L.Stand;
                }
                if (tau < 0f)
                {
                    float k = (tau - throwAt) / -throwAt;
                    m.Pose.Nudge = Vector2.zero;
                    float wind = Anim.OutQuad(Mathf.Clamp01(k / 0.25f));
                    float thrust = Anim.OutCubic(Mathf.Clamp01((k - 0.2f) / 0.5f));
                    m.Pose.Rot = Mathf.Lerp(Mathf.Lerp(-18f, -24f, wind), 22f, thrust);
                    m.Pose.SX = Mathf.Lerp(1.1f, 1.2f, thrust);
                    m.Pose.SY = Mathf.Lerp(0.9f, 0.86f, thrust);
                    m.Pose.Ground = Vector2.Lerp(L.Stand, lunge, thrust);
                    if (!b.F1)
                    {
                        b.F1 = true;
                        Throw(b);
                    }
                    Glow(b, Fx.WithA(Kit.Fire1, 0.35f * (1f - thrust)));
                }
                else
                {
                    // hold the follow-through, then settle (a master holds it for less)
                    float hold = Crew.Flow(b.U) ? 0.2f : 0.32f;
                    float k = Mathf.Clamp01(tau / hold);
                    float s = Anim.Smooth(0.5f, 1f, k);
                    m.Pose.Ground = lunge;
                    m.Pose.Rot = 22f * (1f - s);
                    m.Pose.SX = Mathf.Lerp(1.2f, 1f, s);
                    m.Pose.SY = Mathf.Lerp(0.86f, 1f, s);
                    Glow(b, new Color(1f, 1f, 1f, 0f));
                    if (tau >= hold)
                    {
                        b.At = lunge;
                        StartAct(b, Act.Land, Crisp(b, 0.24f));
                    }
                }
            }
        }

        private void Glow(Body b, Color c)
        {
            if (b.P != null)
            {
                b.P.GlowColor = c;
            }
            else
            {
                _leadGlow.Color = c;
            }
        }

        // the leader's own glow (partners carry theirs)
        private TeamHalo _leadGlowField;
        private TeamHalo _leadGlow => _leadGlowField ??= new TeamHalo(this, true, FxLayer.Over, 0);

        /// <summary>Afterimages on fast motion.</summary>
        private void Trail(Body b, float dt, float every)
        {
            if (!_vis)
            {
                return;
            }
            b.Ghost -= dt;
            if (b.Ghost <= 0f)
            {
                b.Ghost = every;
                Fx.Afterimage(b.M, 0.12f, b.Pal.Ghost, b.Pal.GhostEnd);
            }
        }

        private void Swish(Body b, Sprite[] arc, float life, Vector2 offsetPx)
        {
            if (!_vis)
            {
                return;
            }
            Kit.Swish(b.M, arc, life, offsetPx, b.M.Pose.FaceRight ? 1f : -1f, b.Pal.Core, b.Pal.Edge);
        }

        // ------------------------------------------------------------ victim --

        private Vector2 VBody => _p != null ? _p.BodyPos() : _v.current_position;

        private void SetFlight(Vector2 from, float h0, Leg to, float t0)
        {
            _vs = VState.Flight;
            _fFrom = from;
            _fTo = to.Station;
            _fH0 = h0;
            _fH1 = to.Hc;
            _fH = to.H;
            _fT0 = t0;
            _fD = Mathf.Max(0.12f, to.Contact - t0);
            _vt = 0f;
        }

        private void Victim(float dt)
        {
            if (_p == null || _p.Finished)
            {
                return;
            }
            Pose vp = _p.Pose;
            _vt += dt;
            switch (_vs)
            {
                case VState.Flight:
                    {
                        float u = Mathf.Clamp01((_clock - _fT0) / _fD);
                        vp.Ground = Vector2.Lerp(_fFrom, _fTo, u);
                        vp.Height = Mathf.Max(0f, Mathf.Lerp(_fH0, _fH1, u) + 4f * _fH * u * (1f - u));
                        vp.Rot += _vSpin * dt;
                        _vSpin = Mathf.MoveTowards(_vSpin, Mathf.Sign(_vSpin) * 260f, 900f * dt);
                        float s = Mathf.Clamp01(_vt / 0.14f);
                        vp.SX = Mathf.Lerp(0.84f, 1f, s);
                        vp.SY = Mathf.Lerp(1.2f, 1f, s);
                        if (_vis)
                        {
                            _vGhost -= dt;
                            if (_vGhost <= 0f)
                            {
                                _vGhost = Level >= 2 ? 0.035f : 0.06f;
                                Fx.Afterimage(_p, 0.12f, Fx.WithA(_vPal.Ghost, _vPal.Ghost.a * (Level >= 2 ? 0.75f : 0.4f)), _vPal.GhostEnd);
                            }
                        }
                        break;
                    }
                case VState.Spike:
                    {
                        float k = Mathf.Clamp01(_vt / _sD);
                        vp.Height = _sH0 * (1f - k * k);
                        vp.Ground = Vector2.Lerp(_sFrom, _sTo, k);
                        vp.Rot = Mathf.MoveTowardsAngle(vp.Rot, 180f, 1500f * dt);
                        vp.SX = 0.82f;
                        vp.SY = 1.3f;
                        if (_vis)
                        {
                            Fx.Afterimage(_p, 0.1f, Fx.WithA(_vPal.Ghost, 0.6f), _vPal.GhostEnd);
                        }
                        if (k >= 1f)
                        {
                            vp.Height = 0f;
                            _p.Pose = vp;
                            Crash();
                            _vs = VState.Bounce;
                            _vt = 0f;
                            return;
                        }
                        break;
                    }
                case VState.Bounce:
                    {
                        float k = Mathf.Clamp01(_vt / 0.32f);
                        float big = Level >= 3 ? 0.45f : 0.3f;
                        vp.Height = big * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.75f)) * (1f - 0.3f * k);
                        // it bounces out head first and flops flat, then sits up dazed (handed back
                        // upright: let go lying, the game would stand it up in a single frame)
                        float lying = vp.FaceRight ? -90f : 90f;
                        vp.Rot = k < 0.75f ? Mathf.LerpAngle(180f, lying, Anim.OutQuad(k / 0.75f)) : Mathf.LerpAngle(lying, 0f, Anim.OutQuad((k - 0.75f) / 0.25f));
                        float amp = Anim.Spring(_vt, 10f, 20f);
                        vp.SX = 1f + 0.2f * amp;
                        vp.SY = 1f - 0.25f * amp;
                        if (k >= 1f)
                        {
                            vp.Height = 0f;
                            vp.SX = vp.SY = 1f;
                            _p.Pose = vp;
                            Down(2f);
                            return;
                        }
                        break;
                    }
                case VState.Fall:
                    {
                        float k = Mathf.Clamp01(_vt / _sD);
                        vp.Height = Mathf.Max(0f, _sH0 * (1f - k * k) + 4f * _sUp * k * (1f - k));
                        vp.Ground = Vector2.Lerp(_sFrom, _sTo, k);
                        vp.Rot += _vSpin * dt * (1f - k);
                        if (k > 0.75f)
                        {
                            // it rights itself over the last of the drop and comes down on its feet
                            // (handed back on landing: let go lying, the game would snap it upright)
                            vp.Rot = Mathf.LerpAngle(vp.Rot, 0f, (k - 0.75f) / 0.25f);
                        }
                        if (k >= 1f)
                        {
                            vp.Height = 0f;
                            vp.Rot = 0f;
                            _p.Pose = vp;
                            if (_vis)
                            {
                                Fx.LandingPuff(_sTo, Px, Combat.TileAt(_sTo));
                                Kit.DustRing(_sTo, Px, Combat.TileAt(_sTo), 8, 40f);
                            }
                            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", Combat.TileAt(_sTo));
                            Down(1f);
                            return;
                        }
                        break;
                    }
            }
            _p.Pose = vp;
        }

        /// <summary>
        /// The airborne enemy's own shadow (the game's slides away while it tumbles): it stays on
        /// the ground under it, shrinking and fading with height, so you can read where it comes down.
        /// </summary>
        private void Shadow()
        {
            bool air = !_vGone && _p != null && !_p.Finished && (_vs == VState.Flight || _vs == VState.Spike || _vs == VState.Fall);
            if (!air)
            {
                KillShadow();
                if (_p != null && !_p.Finished)
                {
                    _p.Pose.HideShadow = false;
                }
                return;
            }
            _p.Pose.HideShadow = true;
            float px = Crew.PxOf(_v);
            if (_shadow == null && _vis)
            {
                _shadow = Fx.Spawn(Art.Disk(3, 0.42f, 0.45f), _p.Pose.Ground, px, 1f, FxLayer.Ground, 3);
                if (_shadow != null)
                {
                    _shadow.Held = true;
                    _shadow.Snap = true;
                }
            }
            if (_shadow == null || !_shadow.Alive)
            {
                return;
            }
            float h = Mathf.Clamp01(_p.Pose.Height / 2.6f);
            Sprite sp = Art.Disk(Mathf.Max(1, Mathf.RoundToInt(3.2f * Mathf.Lerp(1f, 0.5f, h) * (_p.Pose.PivotUp / 3.5f))), 0.42f, 0.45f);
            if ((object)_shadow.R.sprite != sp)
            {
                _shadow.R.sprite = sp;
                _shadow.Frames[0] = sp;
            }
            _shadow.Pos = _p.Pose.Ground;
            _shadow.Grad = null;
            _shadow.R.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.36f, 0.14f, h));
        }

        /// <summary>Everyone taking part who is still here (the ground under them is never set alight).</summary>
        private List<Actor> Friends()
        {
            List<Actor> list = new List<Actor>(_bodies.Count);
            foreach (Body b in _bodies)
            {
                if (!b.Dead && b.U != null && b.U.isAlive())
                {
                    list.Add(b.U);
                }
            }
            return list;
        }

        private void KillShadow()
        {
            _shadow?.Kill();
            _shadow = null;
        }

        /// <summary>
        /// The arc the body draws through the air from one kicker to the next: a thin streak left
        /// behind it (pale at I, the last striker's colour from II, wider at III), so every pass
        /// reads as a line from one friend to the other at any zoom.
        /// </summary>
        private void ArcTrail()
        {
            bool air = !_vGone && _p != null && !_p.Finished && (_vs == VState.Flight || _vs == VState.Spike || _vs == VState.Fall);
            if (!air || !_vis)
            {
                _trailOn = false;
                return;
            }
            float px = Crew.PxOf(_v);
            Vector2 at = _p.BodyPos();
            if (!_trailOn)
            {
                _trailOn = true;
                _trailAt = at;
                return;
            }
            if ((at - _trailAt).sqrMagnitude < 2.25f * px * px)
            {
                return;   // less than a pixel and a half: wait for the next stretch
            }
            Color head = Level <= 1 ? new Color(1f, 1f, 1f, 0.5f) : Fx.WithA(Color.Lerp(_vPal.Core, _vPal.Edge, 0.45f), 0.85f);
            Color tail = Level <= 1 ? new Color(0.85f, 0.9f, 1f, 0f) : Fx.WithA(_vPal.Edge, 0f);
            Kit.Line(_trailAt, at, px, Level >= 3 ? 0.34f : 0.26f, Level >= 3 ? 2 : 1, FxLayer.Behind, 1, head, Fx.WithA(head, head.a * 0.55f), tail);
            _trailAt = at;
        }

        /// <summary>The enemy is on the ground and handed back, stunned: the relay is over.</summary>
        private void Down(float stun)
        {
            _vs = VState.Done;
            if (_p != null && !_p.Finished)
            {
                _p.Release(true);
            }
            if (Combat.Living(_v) && _v.Sid() == _vid)
            {
                // (no status flash: the blow's own flash was the hit, a white one now would pop)
                Combat.Daze(_v, stun);
                EndLink = Link.Down;
                EndTarget = _v;
            }
            // a master doesn't linger over a downed enemy: the relay hands straight on
            StartEnding(Crew.Flow(A) ? 0.04f : 0.15f);
        }

        // ------------------------------------------------------------ contacts --

        private void Contacts()
        {
            if (_ending || _cur >= _legs.Count)
            {
                return;
            }
            Leg L = _legs[_cur];
            if (L.Skipped)
            {
                Reroute();
                return;
            }
            if (L.Done || _clock < L.Contact)
            {
                return;
            }
            L.Done = true;
            if (_cur == 0)
            {
                Launch(L);
            }
            else if (L.Final)
            {
                Finish(L);
            }
            else
            {
                Kick(L);
            }
        }

        /// <summary>The next leg whose striker is still here, after index i.</summary>
        private int NextLive(int i)
        {
            for (int j = i; j < _legs.Count; j++)
            {
                if (!_legs[j].Skipped && !_legs[j].B.Dead)
                {
                    return j;
                }
            }
            return -1;
        }

        private void Launch(Leg L)
        {
            Body b = L.B;
            if (_vGone || !Combat.Living(_v) || _v.Sid() != _vid || !Combat.IsFoe(A, _v))
            {
                Whiff("the enemy is gone");
                return;
            }
            float reach = 1.2f * b.K + _v.stats["size"] * 0.5f + 0.5f;
            if (Vector2.Distance(_v.current_position, L.Stand) > reach)
            {
                Whiff("out of reach");
                return;
            }
            Contact c = Combat.Reach(this, _v, false);
            if (c == Contact.Parried)
            {
                Parried(b);
                return;
            }
            if (c == Contact.Evaded)
            {
                Whiff("evaded");
                return;
            }
            // caught even if it is hopping (a backstep, a shove): it leaves from where it is
            if (!Crew.Grabbable(_v, A, out float h0))
            {
                Whiff("can't be thrown (h " + _v.position_height.ToString("F2") + ")");
                return;
            }
            _p = new Puppet(_v, !b.M.Pose.FaceRight, this);
            if (!Moves.Start(_p))
            {
                _p = null;
                Whiff("busy");
                return;
            }
            Vector2 from = _v.current_position;
            _p.Pose.Ground = from;
            _p.Pose.Height = h0;
            _c = from;
            _vSpin = (b.M.Pose.FaceRight ? -1f : 1f) * Random.Range(420f, 640f);
            Blow(b, KickMult, false, 1, Vector2.up + L.Out * 0.4f, 5);
            if (Level >= 3)
            {
                // III: it is kicked off the ground hard enough to scorch the spot (a God's cracks it open)
                Crew.Ravage(from, Crew.Radius(1f, _god), Crew.Force(1, _god), A, Friends());
            }
            if (_vis)
            {
                Fx.LandingPuff(from, Px, _v.current_tile);
                if (L.Style == Style.Slash)
                {
                    Vector2 a0 = from + new Vector2(-(b.M.Pose.FaceRight ? 1f : -1f) * 3f, -1f) * Px;
                    Kit.Line(a0, from + new Vector2((b.M.Pose.FaceRight ? 1f : -1f) * 4f, 12f) * Px, Px, 0.12f, Level >= 3 ? 2 : 1, FxLayer.Top, 4,
                        b.Pal.Core, b.Pal.Edge, Fx.WithA(b.Pal.Edge, 0f));
                }
                int n = Fx.Count(4);
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = from + new Vector2(Random.Range(-3f, 3f), Random.Range(1f, 6f)) * Px;
                    Particle l = Fx.Spawn(Art.Lines[Random.Range(1, 3)], at, Px, 0.16f, FxLayer.Top, 1);
                    l?.Moving(new Vector2(0f, 45f * Px)).Color(Fx.WithA(b.Pal.Core, 0.8f), Fx.WithA(b.Pal.Edge, 0f));
                }
            }
            Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            if (Level >= 2)
            {
                _hold = 0.045f;
            }
            int next = NextLive(1);
            if (next < 0)
            {
                _cur = _legs.Count;
                VictimFall(_legs.Count > 1 ? _legs[1].Station : from, 1.4f);
                return;
            }
            FlyTo(next, from, h0);
        }

        private void Kick(Leg L)
        {
            Body b = L.B;
            if (_vGone || _p == null || _p.Finished)
            {
                return;
            }
            int sparks = L.Style == Style.Heel ? 7 : 5;
            int size = L.Style == Style.Heel || L.Style == Style.Uppercut ? 2 : 1;
            Blow(b, KickMult, false, size, L.Out + Vector2.up * 0.5f, sparks);
            _vSpin = (L.Out.x >= 0f ? -1f : 1f) * Random.Range(560f, 900f);
            Anim.Sound("event:/SFX/OTHER/Punch", b.U.current_tile);
            if (_vis)
            {
                if (L.Style == Style.Heel || L.Style == Style.Uppercut)
                {
                    Fx.FocusLines(VBody, b.Px, Fx.Count(8), 12f * b.K, 22f * b.K, 0.1f);
                }
                if (Level >= 2)
                {
                    Kit.SpeedLines(VBody, L.Out, b.Px, 4, 6f, 0.16f, Fx.WithA(b.Pal.Edge, 0.8f));
                }
            }
            if (Level >= 2)
            {
                _hold = 0.045f;
            }
            int next = NextLive(_cur + 1);
            if (next < 0)
            {
                _cur = _legs.Count;
                VictimFall(L.Station + L.Out * 0.8f, 0.6f);
                return;
            }
            FlyTo(next, L.Station, L.Hc);
        }

        /// <summary>
        /// Sends the enemy at leg j. When legs in between were skipped (their strikers lost), the
        /// arc is re-timed for the real distance: sooner if the striker can make it, never hurrying
        /// one already on its way.
        /// </summary>
        private void FlyTo(int j, Vector2 from, float h0)
        {
            Leg N = _legs[j];
            if (j > _cur + 1)
            {
                float dist = Vector2.Distance(from, N.Station);
                float D = Mathf.Clamp(0.34f + 0.08f * dist, 0.38f, 0.8f) * _tempo;
                if (!N.Engaged)
                {
                    D = Mathf.Max(D, N.ApproachDur + 0.06f + Lead(N.Style));
                }
                Retime(j, D);
                N.H = Mathf.Max(0.45f, 0.6f + 0.2f * dist);
            }
            _cur = j;
            SetFlight(from, h0, N, _clock);
        }

        /// <summary>Moves leg j's blow (and every later one) to 'D' seconds from now.</summary>
        private void Retime(int j, float D)
        {
            Leg N = _legs[j];
            D = Mathf.Min(D, 1.1f);
            float delta = _clock + D - N.Contact;
            if (N.Engaged)
            {
                delta = Mathf.Max(delta, 0f);   // never hurry a striker already on its way
            }
            for (int i = j; i < _legs.Count; i++)
            {
                Leg L = _legs[i];
                L.Contact += delta;
                if (!L.Engaged)
                {
                    L.ApproachStart = Mathf.Max(_clock, Mathf.Min(L.ApproachStart + delta, L.Contact - Lead(L.Style) - 0.06f - L.ApproachDur));
                }
            }
            _length += Mathf.Max(0f, delta);
        }

        private void Finish(Leg L)
        {
            Body b = L.B;
            if (_vGone || _p == null || _p.Finished)
            {
                return;
            }
            Vector2 at = VBody;
            _vPal = b.Pal;
            if (L.Style == Style.Fireball)
            {
                Explode(b, L, at);
                return;
            }
            // the axe kick: a non-lethal blow now, the rest when it hits the ground
            Blow(b, Level >= 2 ? 0.6f : 0.5f, false, 2, new Vector2(0f, -1f), 8);
            Anim.Sound("event:/SFX/OTHER/Punch", b.U.current_tile);
            Anim.Sound("event:/SFX/HIT/HitGeneric", b.U.current_tile);
            if (_vis)
            {
                Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, b.Px * 0.5f * b.K, 0.08f, FxLayer.Top, 31);
                if (burst != null)
                {
                    burst.Rot = Random.Range(0f, 360f);
                    burst.Sized(0.9f, 1.15f).Color(Color.white, Fx.WithA(b.Pal.Core, 0.85f), Fx.WithA(b.Pal.Edge, 0f));
                }
                Fx.FocusLines(at, b.Px, Fx.Count(12), 14f * b.K, 28f * b.K, 0.12f);
            }
            if (Level >= 2)
            {
                _hold = 0.06f;
            }
            Spike(L.Station, 0.14f);
        }

        /// <summary>The fireball meets the falling enemy: explosion, impact frame, the world holds its breath.</summary>
        private void Explode(Body b, Leg L, Vector2 at)
        {
            KillBall();
            float px = b.Px;
            float k = b.K;
            HitResult r = Combat.Hit(b.U.isAlive() ? b.U : A, _v, FireballMult * b.ClassMult, false, true, 0f, this);
            _p.Flash(0.12f, Crew.VictimFlash(_v));
            Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", Combat.TileAt(L.Station));
            Anim.Sound("event:/SFX/POWERS/Fire", Combat.TileAt(L.Station));
            if (_vis)
            {
                Kit.ImpactFrame(at, px, 1.25f * k, b.Pal, r.Crit, FxLayer.Top);
                Particle flash = Fx.Spawn(Art.Disk(Mathf.RoundToInt(13 * k), 0.9f, 0.35f), at, px, 0.14f, FxLayer.Top, 29);
                flash?.Sized(0.7f, 1.3f).Color(Color.white, Fx.WithA(Kit.Fire1, 0.9f), Fx.WithA(Kit.Fire2, 0f));
                Fx.Ring(at, px, 0.3f, 4f, 30f * k, 2, 1, FxLayer.Top, 3, 1f, 2.5f, b.Pal.Core, Fx.WithA(b.Pal.Edge, 0.8f), Fx.WithA(b.Pal.Edge, 0f));
                int puffs = Fx.Count(14);
                for (int i = 0; i < puffs; i++)
                {
                    float a = (i + Random.Range(-0.3f, 0.3f)) / puffs * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Particle p = Fx.Spawn(Art.Puffs[Random.Range(1, 5)], at + dir * 2f * px, px, Random.Range(0.3f, 0.5f), FxLayer.Top, 6);
                    p?.Moving(dir * Random.Range(40f, 80f) * px * k, 6f * px, 3.5f).Sized(0.6f, 1.15f)
                        .Color(Fx.WithA(FireCore, 1f), Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Kit.Fire2, 0.8f), Fx.WithA(Kit.Fire3, 0.5f), Fx.WithA(Smoke, 0f));
                }
                int smoke = Fx.Count(5);
                for (int i = 0; i < smoke; i++)
                {
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(2, 5)], at + Random.insideUnitCircle * 4f * px, px, Random.Range(0.7f, 1f), FxLayer.Top, 4);
                    if (p != null)
                    {
                        p.Moving(Random.insideUnitCircle * 6f * px, 14f * px * k, 1.4f).Sized(0.7f, 1.3f)
                            .Color(Fx.WithA(Smoke, 0f), Fx.WithA(Smoke, 0.55f), Fx.WithA(Smoke, 0f));
                        p.Delay = 0.08f;
                    }
                }
                Kit.Sparks(at, px, 12, 70f * k, 150f * k, Kit.Fire1);
                Kit.Shake(at, 0.3f, 0.011f, k);
            }
            Kit.Stop(at, 0.1f);
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " fireball x" + FireballMult + " -> " + _v.asset.id + " hp " + _v.getHealth() + (r.Crit ? " BLACK FLASH" : ""));
            }
            Spike(L.Station, 0.12f);
        }

        private void Spike(Vector2 to, float dur)
        {
            _vs = VState.Spike;
            _vt = 0f;
            _sFrom = _p.Pose.Ground;
            _sTo = Combat.SafeSpot(to) ? to : _sFrom;
            _sH0 = _p.Pose.Height;
            _sD = dur;
        }

        /// <summary>
        /// Nobody is left to strike it: the enemy finishes its arc ('up' more height) and drops
        /// onto a safe spot.
        /// </summary>
        private void VictimFall(Vector2 to, float up)
        {
            if (_p == null || _p.Finished)
            {
                StartEnding(0.1f);
                return;
            }
            _vs = VState.Fall;
            _vt = 0f;
            _sFrom = _p.Pose.Ground;
            _sTo = Crew.SafeNear(to, Combat.SafeSpot(_sFrom) ? _sFrom : _c, 1.5f);
            _sH0 = _p.Pose.Height;
            _sUp = up;
            _sD = Mathf.Clamp(0.22f + 0.14f * _sH0 + 0.3f * up, 0.25f, 0.85f);
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " nobody left: the enemy drops");
            }
        }

        /// <summary>The striker of the current blow is gone: the arc bends to the next one, or the enemy drops.</summary>
        private void Reroute()
        {
            int j = NextLive(_cur + 1);
            if (_vs == VState.Free)
            {
                // still on its feet (only the leader launches, and its loss aborts the move)
                StartEnding(0f);
                return;
            }
            if (_p == null || _p.Finished || _vs != VState.Flight)
            {
                _cur = j < 0 ? _legs.Count : j;
                return;
            }
            Leg dead = _legs[_cur];
            if (j < 0)
            {
                _cur = _legs.Count;
                VictimFall(dead.Station, 0f);
                return;
            }
            Leg N = _legs[j];
            Vector2 g = _p.Pose.Ground;
            float h = _p.Pose.Height;
            float dist = Vector2.Distance(g, N.Station);
            float D = Mathf.Clamp(0.32f + 0.08f * dist, 0.35f, 0.75f) * _tempo;
            if (!N.Engaged)
            {
                D = Mathf.Max(D, N.ApproachDur + 0.06f + Lead(N.Style));
            }
            Retime(j, D);
            N.H = Mathf.Max(0.3f, 0.35f + 0.15f * dist);
            _cur = j;
            SetFlight(g, h, N, _clock);
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " reroutes to leg " + j + " (" + N.Style + ")");
            }
        }

        private void Whiff(string why)
        {
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " whiffed: " + why);
            }
            // the planners leave this enemy alone for a moment (not the same failed launch every think)
            if (_v != null && _v.isAlive() && _v.Sid() == _vid)
            {
                Crew.Fail("sky_relay", _v, 4f);
            }
            StartEnding(0.25f);
        }

        /// <summary>The enemy parried the launch: the leader is knocked back, the relay breaks.</summary>
        private void Parried(Body b)
        {
            _parried = true;
            Move m = b.M;
            if (_vis)
            {
                Fx.HitSpark(Vector2.Lerp(m.BodyPos(), _v.current_position + new Vector2(0f, 0.4f), 0.5f), Px, 1, -(_c - b.At), 5);
            }
            Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
            Vector2 back = b.At - (_c - b.At).normalized * 0.45f * b.K;
            Descend(b, 0f, 0.15f * b.K, 0.3f, back);
            b.RotFrom = -22f;
            StartEnding(0.3f);
        }

        // ------------------------------------------------------------- blows --

        /// <summary>A blow lands: hit star, sparks, flash, damage, and (II+) a ring in the striker's colour.</summary>
        private void Blow(Body b, float mult, bool lethal, int size, Vector2 away, int sparks)
        {
            if (_vGone || !_v.isAlive())
            {
                return;
            }
            Vector2 at = VBody + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1.5f)) * Px;
            if (_vis)
            {
                Fx.HitSpark(at, b.Px, size, away, sparks);
                if (Level >= 2)
                {
                    Fx.Ring(at, b.Px, 0.14f, 2f, 9f * b.K, 1, 1, FxLayer.Top, 3, 1f, 2.4f, Fx.WithA(b.Pal.Core, 0.95f), Fx.WithA(b.Pal.Edge, 0f));
                }
            }
            _p?.Flash(size >= 2 ? 0.07f : 0.045f, size >= 2 ? Crew.VictimFlash(_v) : (Color?)null);
            _vPal = b.Pal;
            _lastKicker = b;
            Actor by = b.U.isAlive() ? b.U : A;
            HitResult r = Combat.Hit(by, _v, mult * b.ClassMult, lethal, true, 0f, this);
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " blow x" + mult + " by " + by.asset.id + " -> " + _v.asset.id + " hp " + _v.getHealth() + (r.Crit ? " crit" : ""));
            }
        }

        /// <summary>The enemy hits the ground: dust (I), a crater and a tier ring (II), a glowing crater (III); the finishing damage.</summary>
        private void Crash()
        {
            Body b = _finisherBody;
            Actor by = b != null && !b.Dead && b.U.isAlive() ? b.U : A;
            Palette pal = b != null ? b.Pal : Palette.For(A, Level);
            float px = b != null ? b.Px : Px;
            float k = b != null ? b.K : K;
            Vector2 at = _p.Pose.Ground;
            WorldTile tile = Combat.TileAt(at) ?? _v.current_tile;
            Anim.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Anim.Sound("event:/SFX/HIT/HitStone", tile);
            // the ground takes it too, on the same frame: I scorched by the smash, II a crater, III a
            // burning crater blasted by the fireball (a God's is a scale bigger, wider and deeper)
            if (Level <= 1)
            {
                Crew.Ravage(at, Crew.Radius(1f, _god), Crew.Force(1, _god), by, Friends());
            }
            else if (Level == 2)
            {
                Crew.Blast(at, Crew.Radius(1f, _god), Crew.Force(2, _god), by, Friends());
            }
            else
            {
                Crew.Blast(at, Crew.Radius(2f, _god), Crew.Force(3, _god), by, Friends());
            }
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " ground: force " + Crew.Force(Level <= 1 ? 1 : Level, _god) + " r "
                    + Crew.Radius(Level >= 3 ? 2f : 1f, _god).ToString("F1") + (_god ? " (God)" : ""));
            }
            if (_vis)
            {
                Fx.LandingPuff(at, px, tile);
                if (Level <= 1)
                {
                    Kit.DustRing(at, px, tile, 12, 70f, 0.6f, k);
                    Fx.Ring(at, px, 0.28f, 3f, 22f * k, 1, 1, FxLayer.Ground, 6, Art.Ground, 3f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                }
                else if (Level == 2)
                {
                    Kit.Crater(at, px * 0.6f * k, tile, false, 3.5f, true);
                    Kit.Shockwave(at, px, 4f, 34f * k, 0.36f, Fx.WithA(pal.Edge, 0.95f), 2);
                    Kit.DustRing(at, px, tile, 14, 80f, 0.7f, k);
                    Kit.Debris(at, px, tile, 7, k);
                    Kit.Shake(at, 0.16f, 0.006f, k);
                }
                else
                {
                    Kit.Crater(at, px * 0.75f * k, tile, true, 5.5f, false, pal.Edge);
                    Kit.Shockwave(at, px, 6f, 52f * k, 0.42f, new Color(1f, 1f, 1f, 1f), 3);
                    Particle ring = Kit.Shockwave(at, px, 4f, 36f * k, 0.5f, Fx.WithA(pal.Edge, 0.9f), 2);
                    if (ring != null)
                    {
                        ring.Delay = 0.06f;
                    }
                    Kit.DustRing(at, px, tile, 18, 95f, 0.8f, k);
                    Kit.Debris(at, px, tile, 12, k);
                    int embers = Fx.Count(12);
                    for (int i = 0; i < embers; i++)
                    {
                        Kit.Ember(at + Random.insideUnitCircle * 6f * px, px, Random.Range(0.8f, 1.5f), new Vector2(Random.Range(-8f, 8f), Random.Range(15f, 35f)) * px,
                            Random.value < 0.35f ? pal.Edge : (Color?)null);
                    }
                    Kit.Shake(at, 0.25f, _god ? 0.013f : 0.008f, k);
                }
            }
            // (a III with nowhere to throw from was spiked: no fireball came, so the crash is its blow)
            bool spiked3 = Level >= 3 && _legs[_legs.Count - 1].Style != Style.Fireball;
            float mult = CrashMult(Level, !spiked3) * (b != null ? b.ClassMult : 1f);
            // (one already dead, its death not yet counted by the game, isn't killed twice)
            HitResult r = Combat.Living(_v) ? Combat.Hit(by, _v, mult, true, true, 0f, this) : default;
            if (spiked3)
            {
                // the III grammar the fireball would have brought: an impact frame, a global hit-stop
                if (_vis)
                {
                    Kit.ImpactFrame(at + new Vector2(0f, 2f * px), px, 1.25f * k, pal, r.Crit);
                }
                Kit.Stop(at, 0.1f);
            }
            else if (r.Crit && Level == 2 && _vis)
            {
                Kit.ImpactFrame(at + new Vector2(0f, 2f * px), px, 0.8f * k, pal, true);
            }
            if (Dev.On)
            {
                Dev.Trace("relay " + Id + " crash x" + mult + " -> " + _v.asset.id + " hp " + _v.getHealth() + (r.Killed ? " KILLED" : "") + (r.Crit ? " crit" : ""));
            }
            // a jolt for anyone standing too close
            float splash = (Level <= 1 ? 1f : Level == 2 ? 1.3f : 1.8f) * k;
            foreach (Actor u in Combat.FoesAround(by, at, splash))
            {
                if (u == _v)
                {
                    continue;
                }
                if (!Combat.Allowed(by, u, out float cm))
                {
                    Combat.Hit(by, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.3f);
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                Combat.Hit(by, u, (Level >= 3 ? 1.2f : Level == 2 ? 0.8f : 0.6f) * cm, true, true, 0f, this);
                Combat.Launch(u, at, (Level >= 3 ? 1.8f : 1.3f) * k, (Level >= 3 ? 1.3f : 0.9f) * k, Random.value < 0.5f ? -540f : 540f);
            }
        }

        // ----------------------------------------------------------- fireball --

        /// <summary>The ball of fire forms at the finisher's hands.</summary>
        private void Conjure(Body b)
        {
            _fbOut = true;
            _fbFlying = false;
            _fbPos = Hands(b);
            Anim.Sound("event:/SFX/POWERS/Fire", b.U.current_tile);
            if (!_vis)
            {
                return;
            }
            float px = b.Px;
            _fbGlow = Held(Art.Disk(6, 1f, 0.5f), px, FxLayer.Top, 6, Fx.WithA(Kit.Fire2, 0.4f));
            _fbBody = Held(Art.Disk(3, 1f, 0f), px, FxLayer.Top, 7, Kit.Fire1);
            _fbCore = Held(Art.Disk(1, 1f, 0f), px, FxLayer.Top, 8, FireCore);
            _fbSwirl = Held(Art.Arc(4, 1f, 0f, 250f, 1)[3], px, FxLayer.Top, 9, Color.white);
            // a ring of heat closing in on the finisher's feet (not held: it is never touched again)
            Fx.Ring(b.At, px, 0.45f, 16f * b.K, 4f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                Fx.WithA(Kit.Fire2, 0f), Fx.WithA(Kit.Fire1, 0.7f), Fx.WithA(b.Pal.Edge, 0.9f));
        }

        private static Particle Held(Sprite s, float px, FxLayer layer, int order, Color c)
        {
            Particle p = Fx.Spawn(s, Vector2.zero, px, 1f, layer, order);
            if (p != null)
            {
                p.Held = true;
                p.Color(c);
            }
            return p;
        }

        private static Vector2 Hands(Body b)
        {
            Move m = b.M;
            float fwd = m.Pose.FaceRight ? 1f : -1f;
            return m.BodyPos() + new Vector2(fwd * 4f, 1f) * b.Px;
        }

        /// <summary>Let go of the ball: it flies at the falling enemy.</summary>
        private void Throw(Body b)
        {
            _fbFlying = true;
            _fbFrom = _fbPos = Hands(b);
            _fbLast = _fbPos;
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", b.U.current_tile);
            if (_vis)
            {
                Fx.Ring(_fbPos, b.Px, 0.16f, 3f, 12f * b.K, 2, 1, FxLayer.Top, 3, 1f, 2.5f, FireCore, Fx.WithA(Kit.Fire2, 0f));
            }
        }

        /// <summary>The ball: grows and spins at the hands, then flies (homing) with a fire trail.</summary>
        private void Fireball(float dt)
        {
            if (!_fbOut)
            {
                return;
            }
            Leg L = _legs[_legs.Count - 1];
            Body b = L.B;
            if (b.Dead || _vGone)
            {
                KillBall(true);
                return;
            }
            float px = b.Px;
            float tau = _clock - L.Contact;
            float grow = Mathf.Clamp01((tau + 0.62f) / 0.4f);
            int r = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 4f * b.K, Anim.OutQuad(grow))));
            if (_fbFlying)
            {
                float k = Mathf.Clamp01((tau + 0.17f) / 0.17f);
                Vector2 target = VBody;
                Vector2 line = Vector2.Lerp(_fbFrom, target, Mathf.Pow(k, 1.3f));
                _fbPos = line + new Vector2(0f, 0.25f * b.K * Mathf.Sin(Mathf.PI * k));
                if (_vis)
                {
                    FireTrail(px);
                }
                _fbLast = _fbPos;
            }
            else
            {
                _fbPos = Hands(b) + new Vector2(0f, 0.3f * px * Mathf.Sin(_clock * 18f));
                // embers spiral into the ball
                if (_vis)
                {
                    _fbEmber -= dt;
                    if (_fbEmber <= 0f)
                    {
                        _fbEmber = 0.03f;
                        float a = Random.value * Mathf.PI * 2f;
                        Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(7f, 11f) * px * b.K;
                        Particle e = Fx.Spawn(Art.Pixel, _fbPos + off, px, 0.22f, FxLayer.Top, 9);
                        e?.Moving(-off / 0.22f).Color(Fx.WithA(Kit.Fire2, 0f), Kit.Fire1, FireCore);
                    }
                }
            }
            int flick = (Time.frameCount & 2) == 0 ? 0 : 1;
            Place(_fbGlow, Art.Disk(r + 2 + flick, 1f, 0.5f));
            Place(_fbBody, Art.Disk(r + flick, 1f, 0f));
            Place(_fbCore, Art.Disk(Mathf.Max(1, r - 2), 1f, 0f));
            if (_fbSwirl != null && _fbSwirl.Alive)
            {
                _fbSwirl.Pos = _fbPos;
                _fbSwirl.Rot += 900f * dt;
                _fbSwirl.Size0 = _fbSwirl.Size1 = Mathf.Max(0.5f, r / 3.5f);
            }
        }

        private void Place(Particle p, Sprite s)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = _fbPos;
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
        }

        private void FireTrail(float px)
        {
            float step = 1.4f * px / Fx.Detail();
            float dist = Vector2.Distance(_fbLast, _fbPos);
            int n = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 24);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(_fbLast, _fbPos, (i + 1f) / n) + Random.insideUnitCircle * 0.8f * px;
                Particle flame = Fx.Spawn(Art.Puffs[Random.Range(0, 3)], at, px, Random.Range(0.14f, 0.22f), FxLayer.Top, 5);
                flame?.Moving(Random.insideUnitCircle * 4f * px, 5f * px, 2f).Sized(0.8f, 0.4f)
                    .Color(Fx.WithA(Kit.Fire1, 0.95f), Fx.WithA(Kit.Fire2, 0.85f), Fx.WithA(Kit.Fire3, 0.45f), Fx.WithA(Kit.Fire3, 0f));
                if (Random.value < 0.2f)
                {
                    Kit.Ember(at, px, Random.Range(0.4f, 0.7f), Random.insideUnitCircle * 10f * px);
                }
            }
        }

        /// <summary>Drops the ball (exploded, or 'fizzle': it goes out in a puff of smoke).</summary>
        private void KillBall(bool fizzle = false)
        {
            if (fizzle && _fbOut && _fbBody != null && _fbBody.Alive)
            {
                Crew.Fizzle(_fbPos, Px, false);
            }
            _fbOut = false;
            _fbFlying = false;
            _fbGlow?.Kill();
            _fbBody?.Kill();
            _fbCore?.Kill();
            _fbSwirl?.Kill();
            _fbGlow = _fbBody = _fbCore = _fbSwirl = null;
        }

        // ------------------------------------------------------------- ending --

        /// <summary>No more blows: whoever is mid-air comes down, then everyone is let go.</summary>
        private void StartEnding(float delay)
        {
            if (_ending)
            {
                return;
            }
            _ending = true;
            _endAt = _clock + delay;
            if (_fbOut)
            {
                KillBall(true);   // the throw won't come: it goes out
            }
            foreach (Body b in _bodies)
            {
                if (b.Dead)
                {
                    continue;
                }
                b.M.Pose.Nudge = Vector2.zero;
                Glow(b, new Color(1f, 1f, 1f, 0f));
                switch (b.Act)
                {
                    case Act.Idle:
                    case Act.Ready:
                        StartAct(b, Act.Watch, 0f);
                        break;
                    case Act.Strike:
                        if (b.Leg != null && _clock < b.Leg.Contact)
                        {
                            // the blow won't come: back down
                            if (b.M.Pose.Height > 0.02f)
                            {
                                Descend(b, 0f, 0f, 0.3f, b.Leg.Stand);
                            }
                            else
                            {
                                b.At = b.M.Pose.Ground;
                                StartAct(b, Act.Land, Crisp(b, 0.2f));
                            }
                        }
                        break;
                }
            }
        }

        private void Ending()
        {
            if (_clock < _endAt)
            {
                return;
            }
            bool settled = true;
            foreach (Body b in _bodies)
            {
                if (b.Dead)
                {
                    continue;
                }
                if (b.Act == Act.Ready || b.Act == Act.Idle)
                {
                    StartAct(b, Act.Watch, 0f);
                }
                if (b.Act != Act.Watch)
                {
                    settled = false;
                }
            }
            if (!settled && _clock < _endAt + 2f)
            {
                return;
            }
            Wrap();
        }

        private void Wrap()
        {
            if (_wrapped)
            {
                return;
            }
            _wrapped = true;
            if ((_vs == VState.Done || _vGone) && !_parried && _vis)
            {
                Body g = _finisherBody != null && !_finisherBody.Dead ? _finisherBody : _lead;
                Move m = g.M;
                float fwd = m.Pose.FaceRight ? 1f : -1f;
                Fx.Glint(m.BodyPos() + new Vector2(fwd * 3f * g.Px, m.Pose.PivotUp * 1.4f * g.Px), g.Px, 0.26f,
                    Level >= 2 ? g.Pal.Core : new Color(1f, 0.98f, 0.85f, 1f), 1f);
            }
            ReleaseAll();
            if (_vs == VState.Done)
            {
                Finished_++;
                // the finisher who spiked it is right there: the combo passes on to it as well as
                // to the leader (whoever moves first takes it; the other finds its own next move)
                Body fin = _finisherBody;
                if (EndLink == Link.Down && fin != null && !fin.Leader && !fin.Dead && Combat.Living(_v) && _v.Sid() == _vid)
                {
                    if (Crew.Handoff(fin.U, Link.Down, _v) && Dev.On)
                    {
                        Dev.Trace("relay " + Id + " hands Down on to the finisher " + fin.U.asset.id + "#" + fin.U.Sid());
                    }
                }
            }
            Crew.Breathe(A);
            if (!Combat.SafeSpot(Pose.Ground) && Combat.SafeSpot(_lead.At))
            {
                Pose.Ground = _lead.At;
            }
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Finished = true;
        }

        private void ReleaseAll()
        {
            foreach (Body b in _bodies)
            {
                if (b.P != null && !b.P.Finished)
                {
                    b.P.Release(b.P.Pose.Height <= 0.02f);
                }
            }
            if (_p != null && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
        }

        public override void Frame()
        {
            _leadGlowField?.Frame();
        }

        public override void Abort()
        {
            KillBall();
            KillShadow();
            ReleaseAll();
            _leadGlowField?.Kill();
        }

        public override void OnEnd(bool aborted)
        {
            KillBall();
            KillShadow();
            ReleaseAll();
            _leadGlowField?.Kill();
            Director.Release(this);
            foreach (Body b in _bodies)
            {
                if (b.P != null)
                {
                    Crew.Rest(b.U);
                }
            }
            if (_parried && !aborted && A.isAlive())
            {
                Combat.Stagger(A, 0.45f);
            }
        }
    }

    /// <summary>
    /// Sky Relay as a technique. Entry: Novice (a team technique: the Brain skips it when team
    /// techniques are off). The AI starts one on an enemy right next to it that it may throw
    /// around, when enough free friends stand within 5 tiles (one; III: two, so a kick comes
    /// before the fireball; up to 2 / 3 / 5 join); it scores high (it is a show and it hurts, and
    /// more friends make a longer relay), and the director's freshness keeps a battlefield from
    /// seeing it twice in a row. An enemy it failed to launch is left alone for a few seconds.
    /// </summary>
    internal sealed class SkyRelayTech : Technique
    {
        private static readonly float[] Shares = { 6f, 10f, 16f };
        // the finish: the axe kick and the crash (I, II), the fireball and the crash (III)
        private static readonly float[] FinishMult = { 2f, 3.4f, 6f };
        private static readonly List<Actor> _found = new List<Actor>();

        public override string Id => "sky_relay";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Team;

        /// <summary>Allies kick a body round a circle in the sky until the last one finishes it.</summary>
        public override Form Forms => Form.Kick | Form.Aerial | Form.Team | Form.Rising;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Light : Scale.Heavy;

        public override float[] ManaCost => new[] { 15f, 25f, 40f };
        public override float[] Cooldown => new[] { 14f, 14f, 16f };
        public override string Icon => "ui/Icons/sakuga/iconSkyRelay";

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static int MaxPartners(int level)
        {
            return level <= 1 ? 2 : level == 2 ? 3 : 5;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            Actor v = Victim(c);
            if (v == null || !c.Allowed(v, out float cm))
            {
                return null;
            }
            if (!c.Forced && !c.Linked && v.getHealth() <= c.Expected(v, 0.8f * Crew.Plain(c)))
            {
                return null;   // an ordinary blow ends it: no show needed
            }
            float share = Shares[Mathf.Clamp(level - 1, 0, 2)];
            int max = MaxPartners(level);
            List<Actor> partners = _found;
            partners.Clear();
            // tiered friends first (nearest first), then (from level II) anyone willing
            for (int pass = 0; pass < (level >= 2 ? 2 : 1) && partners.Count < max; pass++)
            {
                for (int i = 0; i < c.Allies.Count && partners.Count < max; i++)
                {
                    Actor u = c.Allies[i];
                    if ((u.current_position - c.Pos).sqrMagnitude > 25f)
                    {
                        break;   // nearest first: the rest are further
                    }
                    if ((Tiers.Of(u) != Tier.None) != (pass == 0) || !Crew.Free(c.Self, u) || (!c.Forced && !Crew.CanPay(u, share)) || !Combat.Allowed(u, v))
                    {
                        continue;
                    }
                    partners.Add(u);
                }
            }
            // a relay needs one friend to kick to (III: two, so a kick always comes before the
            // fireball); more friends make a longer relay and a higher score
            int need = c.Forced || level <= 2 ? 1 : 2;
            if (partners.Count < need)
            {
                return null;
            }
            int n = partners.Count;
            float total = SkyRelay.Kick(level) * n + FinishMult[Mathf.Clamp(level - 1, 0, 2)];
            float score = c.Value(v, c.Expected(v, total), cm) + 35f + 8f * n;
            if (!c.Forced && c.CountNear(v.current_position, 2.5f) > 5)
            {
                score *= 0.7f;   // a juggle in a mob gets interrupted
            }
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = score, Others = new List<Actor>(partners), Data = c.Forced };
        }

        /// <summary>The enemy to launch: the focus if it is right there, else the nearest one that is.</summary>
        private static Actor Victim(Ctx c)
        {
            Actor f = c.Focus;
            if (c.Forced)
            {
                Actor v = f;
                if (v == null || !Ok(c, v))
                {
                    v = null;
                    for (int i = 0; i < c.Enemies.Count && v == null; i++)
                    {
                        if (Ok(c, c.Enemies[i]))
                        {
                            v = c.Enemies[i];
                        }
                    }
                }
                return v != null && c.Dist(v) <= 4.5f ? v : null;
            }
            if (f != null && Ok(c, f) && c.Adjacent(f, c.Linked ? 0.8f : 0.3f))
            {
                return f;
            }
            if (c.Linked)
            {
                return null;
            }
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 3f; i++)
            {
                Actor v = c.Enemies[i];
                if (Ok(c, v) && c.Adjacent(v, 0.3f))
                {
                    return v;
                }
            }
            return null;
        }

        private static bool Ok(Ctx c, Actor v)
        {
            return Crew.Target(c.Self, v) && Combat.CanPuppet(v, c.Self) && c.Allowed(v) && !Crew.Failed("sky_relay", v);
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            if (v == null || !Crew.Target(a, v) || !Combat.CanPuppet(v, a) || p.Others == null)
            {
                return false;
            }
            bool forced = p.Data is bool b && b;
            float share = Shares[Mathf.Clamp(p.Level - 1, 0, 2)];
            List<Actor> partners = new List<Actor>();
            foreach (Actor u in p.Others)
            {
                if (partners.Count >= MaxPartners(p.Level))
                {
                    break;
                }
                if (Crew.Free(a, u) && (forced || Crew.CanPay(u, share)) && Combat.Allowed(u, v)
                    && Vector2.Distance(u.current_position, a.current_position) <= 6f)
                {
                    partners.Add(u);
                }
            }
            // (as many friends as the plan needed: a III relay with one left would skip its kick before the fireball)
            int need = forced || p.Level <= 2 ? 1 : 2;
            return partners.Count >= need && SkyRelay.Begin(this, a, v, partners, p.Level, forced, share);
        }
    }
}
