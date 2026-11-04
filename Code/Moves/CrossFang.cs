using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// CROSS FANG (team). Two allies (III: up to four) on opposite sides of one enemy draw at the
    /// same instant and dash straight through it as blurs. For a heartbeat nothing happens: they
    /// stand back to back on the far side, the cut lines hanging in the air in an X (III: a star),
    /// each in its cutter's own tier colour. Then the enemy bursts.
    ///
    /// Beat sheet (game seconds at x1):
    ///
    ///   TAKE MARKS  0.12-0.4  each one slides (or hops, over walls) to its end of a line through the
    ///                         enemy; they start so that all of them arrive together
    ///   SET     0.30   the draw stance: crouched low, leaning in; a "ting!" glint on every one at the
    ///                  same frame; from II a flare in each one's tier colour at its feet, at III its
    ///                  aura; a 1-pixel tremble at the end
    ///   DASH    0.085  all at once: a bright afterimage left at the start, the body a stretched blur
    ///                  (intangible), speed lines; each cut line grows behind it
    ///   FREEZE  0.30   back to back on the far side, frozen in the follow-through; the X (III: star)
    ///                  hangs in the air, a sheen runs along each line; the enemy trembles
    ///   BURST   0.12   the lines flash and widen; the enemy bursts: every cutter's blow (x2 / x3.2 /
    ///                  x4.6; with three or four cutters each counts x0.82 / x0.68), sparks along the
    ///                  lines, a ring (II: the leader's colour; III: impact frame, global hit-stop,
    ///                  glowing scars burnt into the ground, embers); the ground is really cut along
    ///                  the lines (Kit.Ravage: II scorched grooves, III with the crossing blasted
    ///                  open, a God's blasted grooves round a burning heart); a
    ///                  critical blow from II is a Black Flash; the enemy pops up into the air
    ///                  (Launched: the leader and the strongest cutter may both follow it up)
    ///   SHEATHE 0.40   they straighten up through an overshoot; a "click" glint at each hand
    ///
    /// Tempo: from Expert up (the leader's tier) the stance and the freeze tighten to 0.93 / 0.78 /
    /// 0.74 / 0.71 and the sheathe to the tier's Linger (0.85 / 0.55 / 0.48 / 0.42) of these; a
    /// Master leader has no attack cooldown.
    ///
    /// Variety: the angle of the X follows where the cutters stand, a third or fourth cutter turns
    /// it into a six- or eight-pointed star, the stance and the pop-up scale with the enemy's size;
    /// a heavy enemy that can't be thrown just staggers.
    ///
    /// Robustness: the enemy can't act while they take their marks (no shot, no backstep out of the
    /// cross); it answers once, at the dash (a parry breaks the whole attack: they skid, recoil,
    /// and the leader staggers; an evade leaves them cutting air). Caught mid-hop it is held and
    /// settles where the lines cross; knocked clear of the cross by something else, they cut air
    /// and nobody is hurt at a distance. The cut lines hang behind the units, so the enemy stays
    /// visible trembling at the heart of its X; at the burst they flash over everything. A cutter
    /// lost before the dash just doesn't cut; the enemy lost before the burst means no burst. The
    /// leader's death lets every partner go.
    /// </summary>
    internal sealed class CrossFang : Move
    {
        private enum Phase
        {
            Approach,
            Set,
            Dash,
            Freeze,
            Burst,
            Recover
        }

        private const float TDash = 0.085f;
        private const float TBurst = 0.12f;

        // the draw stance and the frozen heartbeat tighten a little from Expert up (the leader sets
        // the pace); the sheathe is much shorter (0.4 s below Expert, 0.34 / 0.22 / 0.19 / 0.17 s):
        // masters cut, burst, and are already moving on
        private readonly float TSet, TFreeze, TRecover;

        private sealed class Runner
        {
            public Actor U;
            public Move M;
            public TeamPartner P;
            public Palette Pal;
            public float K, Px, ClassMult;
            public Vector2 Home, Start, End, Dir;
            public float Delay, Dur;
            public bool Hop, Dead, Glinted;
            public Particle Edge, Core;
            public int EdgeThick, CoreThick;
            public float Ghost;
        }

        private readonly Actor _v;
        private readonly long _vid;
        private Puppet _p;
        private bool _vGone;
        private Vector2 _t;
        private readonly List<Runner> _runners = new List<Runner>();
        private Phase _phase = Phase.Approach;
        private float _pt;
        private bool _entered;
        private float _tApproach;
        private bool _broken, _whiff, _launchPending, _launched;
        private float _launchDist, _launchApex;
        private float _hold;
        private bool _vis;
        private TeamHalo _leadGlow;

        /// <summary>Cross Fangs started / burst since the game started (dev log).</summary>
        internal static int Started, Burst_;

        /// <summary>
        /// Each cutter's blow at the burst (a multiple of its damage; Combat.Hit adds its tier's
        /// Might): x2 / x3.2 / x4.6, so two cutters deal 4x / 6.4x / 9.2x.
        /// </summary>
        internal static float CutMult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 3.2f : 4.6f;
        }

        /// <summary>
        /// Three or four cutters share one enemy: each cut counts for a little less (a star of four
        /// Masters is devastating, never a sure kill on a Master: 12.5x in all).
        /// </summary>
        internal static float Crowd(int cutters)
        {
            return cutters <= 2 ? 1f : cutters == 3 ? 0.82f : 0.68f;
        }

        private CrossFang(Actor a, Actor v, int level) : base(a)
        {
            Level = level;
            _v = v;
            _vid = v.Sid();
            _t = v.current_position;
            _leadGlow = new TeamHalo(this, true, FxLayer.Over, 0);
            float beat = Crew.Beat(a);
            TSet = 0.3f * beat;
            TFreeze = 0.3f * beat;
            TRecover = 0.4f * Crew.Pace(a);
        }

        public override bool Intangible => _phase == Phase.Dash && !_broken;

        // ------------------------------------------------------------- begin --

        public static bool Begin(Technique tech, Actor a, Actor v, List<Actor> partners, int level, bool forced, float share)
        {
            if (!Moves.CanPerform(a) || partners == null || partners.Count == 0 || !Combat.Grounded(v))
            {
                return false;
            }
            CrossFang m = new CrossFang(a, v, level) { Tech = tech };
            if (!m.Lines(partners))
            {
                Crew.Fail("cross_fang", v);
                return false;
            }
            // only the cutters (and their enemy) are held by the set piece
            List<Actor> cast = new List<Actor> { v };
            foreach (Runner r in m._runners)
            {
                cast.Add(r.U);
            }
            if (!Director.Claim(m, 4f, cast.ToArray()))
            {
                return false;
            }
            // caught mid-swing or mid-dash: the cutters take it over where it stands
            Crew.Seize(v);
            List<TeamPartner> started = new List<TeamPartner>();
            foreach (Runner r in m._runners)
            {
                if (r.P == null)
                {
                    continue;
                }
                if (Moves.Start(r.P))
                {
                    started.Add(r.P);
                }
                else
                {
                    r.Dead = true;
                    Director.Release(r.U);
                }
            }
            if (started.Count == 0 || !Moves.Start(m))
            {
                foreach (TeamPartner p in started)
                {
                    p.Release(true);
                }
                Director.Release(m);
                return false;
            }
            cast.Clear();
            cast.Add(v);
            foreach (Runner r in m._runners)
            {
                if (!r.Dead)
                {
                    cast.Add(r.U);
                }
            }
            Director.Claim(m, m._tApproach + m.TSet + TDash + m.TFreeze + TBurst + m.TRecover + 1.5f, cast.ToArray());
            // the enemy senses it too late: no shot, no backstep out of the cross before the dash
            Crew.Pin(v, m._tApproach + m.TSet + 0.2f);
            if (!forced)
            {
                foreach (Runner r in m._runners)
                {
                    if (r.P != null && !r.Dead)
                    {
                        Crew.Pay(r.U, tech, share, 5f);
                    }
                }
            }
            Started++;
            Dev.Watch(m._t + new Vector2(0f, 1f));
            if (Dev.On)
            {
                Dev.Trace("fang " + m.Id + " " + Techniques.Roman(level) + " with " + started.Count + " partners, approach " + m._tApproach.ToString("F2"));
            }
            return true;
        }

        /// <summary>
        /// The lines through the enemy: an X for two cutters, a star for more, turned to follow where
        /// they stand; each cutter takes the end of a line nearest to it. All ends and paths safe.
        /// </summary>
        private bool Lines(List<Actor> partners)
        {
            List<Actor> who = new List<Actor> { A };
            who.AddRange(partners);
            int n = Mathf.Min(who.Count, 4);
            float vs = _v.stats["size"];
            float kAvg = 0f;
            for (int i = 0; i < n; i++)
            {
                kAvg += Crew.KOf(who[i]);
            }
            kAvg /= n;
            float r = Mathf.Clamp(1.5f + 0.25f * (kAvg - 1f) + 0.3f * vs, 1.45f, 2.3f);
            float phiL = Ang(A.current_position);
            // angles of the line ends each cutter could take
            float[] ends;
            if (n == 2)
            {
                // an X: the partner's end leans toward where it stands, the leader's the other way
                // (lines 80 degrees apart)
                float phiP = Ang(who[1].current_position);
                float side = Mathf.DeltaAngle(phiL + 180f, phiP) >= 0f ? 1f : -1f;
                ends = new[] { phiL - 40f * side, phiL + 180f + 40f * side };
                if (TryEnds(who, n, ends, r, false))
                {
                    return true;
                }
                ends = new[] { phiL + 40f * side, phiL + 180f - 40f * side };
                if (TryEnds(who, n, ends, r, false))
                {
                    return true;
                }
            }
            float step = 180f / n;
            for (int turn = 0; turn < 12; turn++)
            {
                float t0 = phiL + step * 0.5f + turn * 15f;
                ends = new float[n];
                for (int i = 0; i < n; i++)
                {
                    ends[i] = t0 + step * i;
                }
                if (TryEnds(who, n, ends, r, true))
                {
                    return true;
                }
            }
            return false;
        }

        private float Ang(Vector2 p)
        {
            Vector2 d = p - _t;
            return d.sqrMagnitude > 0.0004f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        }

        /// <summary>
        /// Assigns the cutters to lines (each line once, either end: 'flip') minimising how far they
        /// travel; the leader always takes part. False if some end or path is unsafe.
        /// </summary>
        private bool TryEnds(List<Actor> who, int n, float[] lineAngles, float r, bool flip)
        {
            // each line is tested once (both ends safe, a clear path through the enemy), then the
            // cheapest assignment is searched over the travel distances only
            bool[] lineOk = new bool[n];
            float[,,] cost = new float[n, n, 2];
            for (int l = 0; l < n; l++)
            {
                Vector2 s0 = _t + Crew.Dir(lineAngles[l]) * r;
                Vector2 s1 = _t - Crew.Dir(lineAngles[l]) * r;
                lineOk[l] = Combat.SafeSpot(s0) && Combat.SafeSpot(s1) && Combat.ClearPath(s0, s1);
                for (int i = 0; i < n; i++)
                {
                    Vector2 h = who[i].current_position;
                    cost[i, l, 0] = (h - s0).sqrMagnitude;
                    cost[i, l, 1] = (h - s1).sqrMagnitude;
                }
            }
            int[] perm = new int[n];
            for (int i = 0; i < n; i++)
            {
                perm[i] = i;
            }
            float best = float.MaxValue;
            int[] bestPerm = null;
            int bestFlips = 0;
            int flipsMax = flip ? 1 << n : 1;
            do
            {
                for (int f = 0; f < flipsMax; f++)
                {
                    float sum = 0f;
                    bool ok = true;
                    for (int i = 0; i < n; i++)
                    {
                        int l = perm[i];
                        float c = cost[i, l, (f >> i) & 1];
                        if (!lineOk[l] || c > 5.5f * 5.5f)
                        {
                            ok = false;
                            break;
                        }
                        sum += c;
                    }
                    if (ok && sum < best)
                    {
                        best = sum;
                        bestPerm = (int[])perm.Clone();
                        bestFlips = f;
                    }
                }
            }
            while (NextPerm(perm));
            if (bestPerm == null)
            {
                return false;
            }
            _runners.Clear();
            float longest = 0.12f;
            for (int i = 0; i < n; i++)
            {
                Actor u = who[i];
                float ang = lineAngles[bestPerm[i]] + (((bestFlips >> i) & 1) == 1 ? 180f : 0f);
                Vector2 dir = -Crew.Dir(ang);
                Runner run = new Runner
                {
                    U = u,
                    Pal = Palette.For(u, Level),
                    K = Crew.KOf(u),
                    Px = Crew.PxOf(u),
                    Home = u.current_position,
                    Start = _t - dir * r,
                    End = _t + dir * r,
                    Dir = dir
                };
                Combat.Allowed(u, _v, out run.ClassMult);
                float d = Vector2.Distance(run.Home, run.Start);
                run.Hop = d > 0.3f && !Combat.ClearPath(run.Home, run.Start);
                run.Dur = d <= 0.15f ? 0f : run.Hop ? 0.24f + 0.04f * d : 0.08f + 0.045f * d;
                longest = Mathf.Max(longest, run.Dur);
                if (i == 0)
                {
                    run.M = this;
                }
                else
                {
                    run.P = new TeamPartner(u, this, Crew.Face(run.Home, _t, u.flip));
                    run.M = run.P;
                }
                _runners.Add(run);
            }
            // everybody arrives together
            _tApproach = longest + 0.04f;
            foreach (Runner run in _runners)
            {
                run.Delay = _tApproach - 0.04f - run.Dur;
            }
            Pose.FaceRight = Crew.Face(A.current_position, _t, A.flip);
            return true;
        }

        private static bool NextPerm(int[] a)
        {
            int i = a.Length - 2;
            while (i >= 0 && a[i] >= a[i + 1])
            {
                i--;
            }
            if (i < 0)
            {
                return false;
            }
            int j = a.Length - 1;
            while (a[j] <= a[i])
            {
                j--;
            }
            (a[i], a[j]) = (a[j], a[i]);
            System.Array.Reverse(a, i + 1, a.Length - i - 1);
            return true;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _vis = Fx.OnScreen(_t, 0.5f);
            Watchdog();
            if (_hold > 0f)
            {
                _hold -= dt;
                float n = ((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f;
                if (_p != null && !_vGone)
                {
                    _p.Pose.Nudge = new Vector2(n, 0f);
                }
                return;
            }
            _pt += dt;
            switch (_phase)
            {
                case Phase.Approach:
                    Approach(dt);
                    break;
                case Phase.Set:
                    Set(dt);
                    break;
                case Phase.Dash:
                    Dash(dt);
                    break;
                case Phase.Freeze:
                    Freeze(dt);
                    break;
                case Phase.Burst:
                    BurstPhase(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
            Lines();
            if (_launchPending && !_vGone && !Moves.Locked(_v))
            {
                _launchPending = false;
                if (Combat.Living(_v) && _v.Sid() == _vid)
                {
                    Combat.Launch(_v, _t + Random.insideUnitCircle * 0.2f, _launchDist, _launchApex, Random.value < 0.5f ? -620f : 620f);
                }
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On)
            {
                Dev.Trace("fang " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
            }
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

        private void Watchdog()
        {
            foreach (Runner r in _runners)
            {
                if (!r.Dead && r.P != null && r.P.Gone)
                {
                    r.Dead = true;
                    r.Edge?.Kill();
                    r.Core?.Kill();
                    r.Edge = r.Core = null;
                }
            }
            if (!_vGone && (!_v.isAlive() || _v.Sid() != _vid || (_p != null && _p.Finished && _phase < Phase.Burst)
                || (_phase <= Phase.Set && !Combat.IsFoe(A, _v))))
            {
                _vGone = true;
                if (_p != null && !_p.Finished)
                {
                    _p.Release(_p.Pose.Height <= 0.02f);
                }
                if (_phase <= Phase.Set)
                {
                    // nothing to cut: stand down
                    Next(Phase.Recover);
                }
            }
        }

        // ------------------------------------------------------------ phases --

        private void Approach(float dt)
        {
            Track();
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Move m = r.M;
                float k = r.Dur > 0f ? Mathf.Clamp01((_pt - r.Delay) / r.Dur) : 1f;
                if (_pt < r.Delay)
                {
                    m.Pose.Ground = r.Home;
                    m.Pose.FaceRight = Crew.Face(r.Home, _t, m.Pose.FaceRight);
                    continue;
                }
                float dist = Vector2.Distance(r.Home, r.Start);
                if (r.Hop)
                {
                    m.Pose.Ground = Vector2.Lerp(r.Home, r.Start, k);
                    m.Pose.Height = 4f * (0.4f + 0.1f * dist) * r.K * k * (1f - k);
                    m.Pose.Rot = 8f * Mathf.Sin(Mathf.PI * k);
                }
                else
                {
                    m.Pose.Ground = Vector2.Lerp(r.Home, r.Start, Anim.OutQuad(k));
                    m.Pose.Height = 0f;
                    m.Pose.Rot = 14f * Mathf.Sin(Mathf.PI * k);
                }
                m.Pose.FaceRight = k < 1f ? Crew.Face(r.Home, r.Start, m.Pose.FaceRight) : Crew.Face(r.Start, r.End, m.Pose.FaceRight);
                if (_vis && dist > 0.5f && k < 1f)
                {
                    r.Ghost -= dt;
                    if (r.Ghost <= 0f)
                    {
                        r.Ghost = 0.03f;
                        Fx.Afterimage(m, 0.12f, r.Pal.Ghost, r.Pal.GhostEnd);
                    }
                }
                if (k >= 1f && m.Pose.Height > 0f)
                {
                    m.Pose.Height = 0f;
                    if (_vis)
                    {
                        Fx.LandingPuff(r.Start, r.Px, Combat.TileAt(r.Start));
                    }
                }
            }
            if (_pt >= _tApproach)
            {
                Next(Phase.Set);
            }
        }

        /// <summary>The enemy is still free: keep the lines on it while it shuffles.</summary>
        private void Track()
        {
            if (_vGone)
            {
                return;
            }
            Crew.Pin(_v, 0.2f);   // renewed every tick until the dash, whatever the game speed
            Vector2 now = _v.current_position;
            if ((now - _t).sqrMagnitude < 0.0025f || (now - _t).sqrMagnitude > 0.81f)
            {
                return;   // still there, or gone too far (the dash will cut air)
            }
            Vector2 shift = now - _t;
            foreach (Runner r in _runners)
            {
                Vector2 e = r.End + shift;
                if (!Combat.SafeSpot(e) || !Combat.ClearPath(r.Start, e))
                {
                    return;
                }
            }
            _t = now;
            foreach (Runner r in _runners)
            {
                r.End += shift;
                r.Dir = (r.End - r.Start).normalized;
            }
        }

        private void Set(float dt)
        {
            Track();
            float k = Mathf.Clamp01(_pt / TSet);
            float c = Anim.OutCubic(Mathf.Clamp01(_pt / 0.12f));
            bool first = Enter();
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Move m = r.M;
                m.Pose.Ground = r.Start;
                m.Pose.Height = 0f;
                m.Pose.FaceRight = Crew.Face(r.Start, r.End, m.Pose.FaceRight);
                m.Pose.SX = Mathf.Lerp(1f, 1.14f, c);
                m.Pose.SY = Mathf.Lerp(1f, 0.82f, c);
                m.Pose.Rot = Mathf.Lerp(0f, 12f, c);
                m.Pose.Nudge = _pt > TSet * 0.66f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                if (first && _vis && Level >= 2)
                {
                    Kit.Flare(r.Start, r.Px, r.Pal.Edge, 12f * r.K, 0.3f);
                }
                if (Level >= 3)
                {
                    Glow(r, Fx.WithA(r.Pal.Edge, 0.35f * Anim.Smooth(0f, 0.6f, k) * (0.8f + 0.2f * Mathf.Sin(_pt * 50f))));
                }
                if (!r.Glinted && _pt >= 0.13f)
                {
                    r.Glinted = true;
                    if (_vis)
                    {
                        float fwd = m.Pose.FaceRight ? 1f : -1f;
                        Fx.Glint(m.HeadPos() + new Vector2(fwd * 2f * r.Px, 0f), r.Px, 0.2f, Level >= 2 ? r.Pal.Core : Color.white, 0.9f);
                    }
                }
            }
            if (first)
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            if (k >= 1f)
            {
                Next(Phase.Dash);
            }
        }

        private void Dash(float dt)
        {
            if (Enter())
            {
                Contact c = _vGone ? Contact.Evaded : Combat.Reach(this, _v, false);
                if (c == Contact.Parried)
                {
                    _broken = true;
                    Anim.Sound("event:/SFX/HIT/HitSwordSword", A.current_tile);
                    if (_vis)
                    {
                        Fx.HitSpark(_t + new Vector2(0f, 0.4f), Px, 2, Vector2.up, 8);
                        Fx.Ring(_t + new Vector2(0f, 0.4f), Px, 0.16f, 3f, 14f, 1, 1, FxLayer.Top, 3, 1f, 2.5f, Color.white, Fx.WithA(Color.white, 0f));
                    }
                    if (Dev.On)
                    {
                        Dev.Trace("fang " + Id + " parried");
                    }
                }
                else if (c == Contact.Evaded)
                {
                    _whiff = true;
                }
                else if (Vector2.Distance(_v.current_position, _t) > 0.6f + 0.3f * _v.stats["size"])
                {
                    // it slipped out of the cross before they drew (pushed, thrown): they cut air,
                    // and nobody is hurt from a distance
                    _whiff = true;
                    if (Dev.On)
                    {
                        Dev.Trace("fang " + Id + " cuts air: the enemy is " + Vector2.Distance(_v.current_position, _t).ToString("F2") + " off the cross");
                    }
                }
                else if (Crew.Grabbable(_v, A, out float h0))
                {
                    // held where the lines cross (caught even mid-hop; it settles during the freeze)
                    _p = new Puppet(_v, !Pose.FaceRight, this);
                    if (Moves.Start(_p))
                    {
                        _p.Pose.Height = h0;
                    }
                    else
                    {
                        _p = null;
                        Combat.Daze(_v, 0.7f);
                    }
                }
                else
                {
                    // (stunned without the game's white status flash: nothing has struck it yet)
                    Combat.Daze(_v, 0.7f);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                foreach (Runner r in _runners)
                {
                    if (r.Dead)
                    {
                        continue;
                    }
                    r.M.Pose.Nudge = Vector2.zero;
                    if (r.P != null)
                    {
                        r.P.Blur = !_broken;
                    }
                    Glow(r, new Color(1f, 1f, 1f, 0f));
                    if (_vis)
                    {
                        // the afterimage left where it stood
                        Fx.Afterimage(r.M, 0.4f, Fx.WithA(r.Pal.Core, 0.9f), Fx.WithA(r.Pal.Edge, 0.6f), r.Pal.GhostEnd);
                        Kit.DustRing(r.Start, r.Px, Combat.TileAt(r.Start), 6, 45f, 0.4f, r.K);
                        if (!_broken)
                        {
                            // I: a thin grey cut; II: the tier colour; III: a white-hot core in a wide tier edge
                            r.CoreThick = 1;
                            r.EdgeThick = Level >= 3 ? 3 : 2;
                            r.Core = Hold(r, r.CoreThick, r.Pal.Core, 4);
                            r.Edge = Hold(r, r.EdgeThick, Level <= 1 ? Fx.WithA(r.Pal.Edge, 0.6f) : r.Pal.Edge, 3);
                        }
                    }
                }
            }
            float k = Mathf.Clamp01(_pt / TDash);
            Settle(dt);
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Move m = r.M;
                float h = Mathf.Abs(r.Dir.x);
                if (_broken)
                {
                    // stopped short by the parry
                    m.Pose.Ground = Vector2.Lerp(r.Start, Vector2.Lerp(r.Start, _t, 0.55f), Anim.OutQuad(k));
                    m.Pose.Rot = 18f;
                    m.Pose.SX = 1.1f;
                    m.Pose.SY = 0.9f;
                    continue;
                }
                m.Pose.Ground = Vector2.Lerp(r.Start, r.End, k);
                m.Pose.Height = 0f;
                m.Pose.Rot = 20f;
                m.Pose.SX = 1f + 0.7f * h - 0.25f * (1f - h);
                m.Pose.SY = 1f - 0.4f * h + 0.35f * (1f - h);
                if (_vis)
                {
                    Fx.Afterimage(m, 0.1f, r.Pal.Ghost, r.Pal.GhostEnd);
                    Kit.SpeedLines(m.BodyPos(), r.Dir, r.Px, 2, 5f, 0.12f, Fx.WithA(r.Pal.Edge, 0.75f));
                }
            }
            if (k >= 1f)
            {
                foreach (Runner r in _runners)
                {
                    if (r.P != null)
                    {
                        r.P.Blur = false;
                    }
                }
                if (_broken)
                {
                    Recoil();
                    Next(Phase.Recover);
                }
                else
                {
                    Next(Phase.Freeze);
                }
            }
        }

        /// <summary>
        /// A held cut line at the cutter's own pixel size (stretched every frame by Lines). It hangs
        /// among the units, behind them: the enemy stays visible in the middle of its own X, trembling.
        /// </summary>
        private static Particle Hold(Runner r, int thick, Color c, int order)
        {
            Particle p = Kit.Line(r.Start, r.Start + new Vector2(r.Px * 2f, 0f), r.Px, 1f, thick, FxLayer.Behind, order, c);
            if (p != null)
            {
                p.Held = true;
            }
            return p;
        }

        private void Freeze(float dt)
        {
            if (Enter() && _vis)
            {
                // the "shing": a sheen starts along every line
                foreach (Runner r in _runners)
                {
                    if (!r.Dead && Level >= 2)
                    {
                        Particle s = Fx.Spawn(Art.Pixel, r.Start, r.Px, TFreeze * 0.9f, FxLayer.Top, 6);
                        if (s != null)
                        {
                            s.Sized(2f, 1f).Color(Color.white, Fx.WithA(r.Pal.Core, 0.8f), Fx.WithA(r.Pal.Core, 0f));
                            Vector2 a0 = r.Start + new Vector2(0f, r.M.Pose.PivotUp * r.Px), a1 = r.End + new Vector2(0f, r.M.Pose.PivotUp * r.Px);
                            s.Step = (q, d) => q.Pos = Vector2.Lerp(a0, a1, Anim.InOutSine(q.K));
                        }
                    }
                }
            }
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Move m = r.M;
                m.Pose.Ground = r.End;
                m.Pose.Height = 0f;
                m.Pose.FaceRight = Crew.Face(r.Start, r.End, m.Pose.FaceRight);
                float s = Anim.OutCubic(Mathf.Clamp01(_pt / 0.06f));
                m.Pose.Rot = Mathf.Lerp(20f, 14f, s);
                m.Pose.SX = Mathf.Lerp(1.2f, 1.12f, s);
                m.Pose.SY = Mathf.Lerp(0.8f, 0.86f, s);
            }
            Settle(dt);
            if (_p != null && !_vGone && !_p.Finished)
            {
                _p.Pose.Nudge = new Vector2(((int)(_pt * 50f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                _p.Pose.Rot = Mathf.Lerp(0f, -8f, Mathf.Clamp01(_pt / TFreeze));
            }
            if (_pt >= TFreeze)
            {
                Next(Phase.Burst);
            }
        }

        /// <summary>The held enemy settles where the lines cross (out of a hop, onto the cross point).</summary>
        private void Settle(float dt)
        {
            if (_p == null || _vGone || _p.Finished)
            {
                return;
            }
            _p.Pose.Height = Mathf.MoveTowards(_p.Pose.Height, 0f, 6f * dt);
            if (Combat.SafeSpot(_t))
            {
                _p.Pose.Ground = Vector2.MoveTowards(_p.Pose.Ground, _t, 3f * dt);
            }
        }

        private void BurstPhase(float dt)
        {
            if (Enter())
            {
                Burst();
            }
            foreach (Runner r in _runners)
            {
                if (!r.Dead)
                {
                    r.M.Pose.Ground = r.End;
                }
            }
            if (_pt >= TBurst)
            {
                Next(Phase.Recover);
            }
        }

        /// <summary>The enemy bursts: every cutter's blow, sparks along the lines, the pop into the air.</summary>
        private void Burst()
        {
            Vector2 at = _p != null && !_p.Finished ? _p.BodyPos() : _t + new Vector2(0f, 0.4f);
            float px = Px;
            Palette lead = _runners[0].Pal;
            bool hit = !_whiff && !_vGone;
            bool crit = false;
            if (hit)
            {
                Burst_++;
                Anim.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
                Anim.Sound("event:/SFX/OTHER/Punch", A.current_tile);
                int cutters = 0;
                foreach (Runner r in _runners)
                {
                    cutters += r.Dead ? 0 : 1;
                }
                float mult = CutMult(Level) * Crowd(cutters);
                foreach (Runner r in _runners)
                {
                    // (a cut that already killed it ends the burst's blows: the dead aren't cut
                    // again, nor the kill handed to the last cutter)
                    if (r.Dead || !Combat.Living(_v))
                    {
                        continue;
                    }
                    HitResult res = Combat.Hit(r.U, _v, mult * r.ClassMult, true, true, 0f, this);
                    if (res.Crit && Level >= 2 && _vis && !crit)
                    {
                        // one Black Flash per burst (two stacked frames only smear each other)
                        crit = true;
                        Kit.ImpactFrame(at, r.Px, 0.9f, r.Pal, true);
                    }
                    if (Dev.On)
                    {
                        Dev.Trace("fang " + Id + " cut x" + (mult * r.ClassMult) + " by " + r.U.asset.id + " -> " + _v.asset.id + " hp " + _v.getHealth() + (res.Crit ? " crit" : ""));
                    }
                }
                _p?.Flash(0.1f, Crew.VictimFlash(_v));
            }
            if (_vis)
            {
                foreach (Runner r in _runners)
                {
                    if (r.Dead)
                    {
                        continue;
                    }
                    if (hit)
                    {
                        Kit.Sparks(at, r.Px, 3, 60f, 120f, r.Pal.Edge);
                        Fx.HitSpark(at, r.Px, 2, r.Dir, 5);
                        Fx.HitSpark(at, r.Px, 1, -r.Dir, 3);
                        // the cut flashes over everything for a blink (the held line hangs behind the units)
                        Vector2 lift = new Vector2(0f, r.M.Pose.PivotUp * r.Px);
                        Kit.Line(r.Start + lift, r.End + lift, r.Px, 0.08f, r.EdgeThick, FxLayer.Top, 7,
                            Color.white, Fx.WithA(Level >= 2 ? r.Pal.Core : Color.white, 0.85f), Fx.WithA(r.Pal.Edge, 0f));
                    }
                    if (Level >= 3)
                    {
                        Scar(r);
                    }
                }
                if (hit)
                {
                    if (Level >= 3 && !crit)
                    {
                        Kit.ImpactFrame(at, px, 1.05f, lead, false);
                    }
                    if (Level >= 2)
                    {
                        Fx.Ring(at, px, 0.22f, 3f, 20f * K, 2, 1, FxLayer.Top, 3, 1f, 2.5f, lead.Core, Fx.WithA(lead.Edge, 0.8f), Fx.WithA(lead.Edge, 0f));
                    }
                    else
                    {
                        Fx.Ring(at, px, 0.16f, 3f, 12f * K, 1, 1, FxLayer.Top, 3, 1f, 2.5f, Color.white, Fx.WithA(Color.white, 0f));
                    }
                    if (Level >= 3)
                    {
                        Kit.Shockwave(_t, px, 4f, 30f * K, 0.4f, Fx.WithA(lead.Edge, 0.9f));
                        int embers = Fx.Count(10);
                        for (int i = 0; i < embers; i++)
                        {
                            Runner r = _runners[i % _runners.Count];
                            Vector2 along = Vector2.Lerp(r.Start, r.End, Random.value) + new Vector2(0f, Random.Range(0f, 6f) * px);
                            Kit.Ember(along, px, Random.Range(0.6f, 1.2f), new Vector2(Random.Range(-6f, 6f), Random.Range(12f, 26f)) * px, r.Pal.Edge);
                        }
                    }
                    Kit.Shake(at, 0.18f, Level >= 3 ? 0.008f : 0.004f, K);
                }
            }
            if (hit)
            {
                if (Level >= 3)
                {
                    Kit.Stop(at, 0.1f);
                }
                else if (Level == 2)
                {
                    _hold = 0.05f;
                }
            }
            // the cuts bite into the ground on the same frame
            Scars();
            // hand the enemy back and throw it up (next frame, once it is no longer ours)
            if (_p != null && !_p.Finished)
            {
                _p.Release(true);
            }
            if (hit && Combat.Living(_v) && _v.Sid() == _vid)
            {
                if (Combat.CanPuppet(_v, A) || _p != null)
                {
                    float big = Weights.Species(_v) == Weight.Critter ? 1.4f : 1f;
                    _launchPending = true;
                    _launched = true;
                    _launchDist = Random.Range(0.4f, 0.9f) * big;
                    _launchApex = (Level <= 1 ? 0.9f : Level == 2 ? 1.4f : 2f) * big;
                }
                else
                {
                    Combat.Stagger(_v, 0.5f);
                }
                EndLink = _launched ? Link.Launched : Link.Close;
                EndTarget = _v;
            }
        }

        /// <summary>
        /// Real damage to the ground on the burst frame, cut where the blades passed: a thin groove
        /// one tile wide along every line (II scorched, III scorched with the heart where the lines
        /// cross blasted open; a God's lines are blasted grooves and its heart burns, half again as
        /// wide). One blast per tile, short of the cutters' feet. (A blast a tile wide on every
        /// point of a line laid a star of bare, pale ground over the whole cross: it read as a decal,
        /// not as cuts.)
        /// </summary>
        private void Scars()
        {
            if (Level <= 1)
            {
                return;
            }
            List<Actor> friends = new List<Actor>(_runners.Count);
            foreach (Runner r in _runners)
            {
                if (!r.Dead && r.U.isAlive())
                {
                    friends.Add(r.U);
                }
            }
            bool god = Crew.AnyGod(friends);
            int lineForce = god ? 2 : 1;
            int heartForce = Level >= 3 ? Crew.Force(2, god) : 0;
            WorldTile heart = Combat.TileAt(_t);
            List<WorldTile> done = new List<WorldTile>(16);
            if (heart != null && heartForce > 0)
            {
                done.Add(heart);
                Crew.Blast(_t, Crew.Radius(1f, god), heartForce, A, friends);
            }
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Vector2 mid = (r.Start + r.End) * 0.5f;
                float half = Vector2.Distance(r.Start, r.End) * 0.5f - 0.6f;
                int n = Mathf.Max(0, Mathf.FloorToInt(half * 2f));
                for (int i = -n; i <= n; i++)
                {
                    Vector2 at = mid + r.Dir * (i * 0.5f);
                    WorldTile t = Combat.TileAt(at);
                    if (t == null || done.Contains(t))
                    {
                        continue;
                    }
                    done.Add(t);
                    Crew.Ravage(at, 0.5f, lineForce, A, friends, false);
                }
            }
            if (Dev.On)
            {
                Dev.Trace("fang " + Id + " scars " + done.Count + " tile(s), lines force " + lineForce + ", heart force " + heartForce + (god ? " (God)" : ""));
            }
        }

        /// <summary>III: the cut is burnt into the ground along each line, glowing and cooling.</summary>
        private void Scar(Runner r)
        {
            // a hairline that glows white-hot, cools through the cutter's colour to a dark seam and
            // fades; under it a thin scorched groove (2 px), shortened a little at both ends so the
            // star reads as cuts, not as planks laid on the grass
            Vector2 along = (r.End - r.Start) * 0.06f;
            Vector2 s0 = r.Start + along, s1 = r.End - along;
            Particle p = Kit.Line(s0, s1, r.Px, 1.8f, 1, FxLayer.Ground, 3,
                new Color(1f, 0.97f, 0.8f, 1f), Fx.WithA(Color.Lerp(Kit.Fire1, r.Pal.Edge, 0.5f), 0.95f), Fx.WithA(Color.Lerp(Kit.Fire3, r.Pal.Dark, 0.6f), 0.55f),
                new Color(0.15f, 0.04f, 0.03f, 0.25f), new Color(0.15f, 0.04f, 0.03f, 0f));
            if (p != null)
            {
                p.Snap = true;
            }
            WorldTile tile = Combat.TileAt(_t);
            Color soil = Fx.Darken(Fx.SoilColor(tile), 0.5f);
            Particle d = Kit.Line(s0, s1, r.Px, 2.6f, 2, FxLayer.Ground, 2, Fx.WithA(soil, 0.55f), Fx.WithA(soil, 0.4f), Fx.WithA(soil, 0f));
            if (d != null)
            {
                d.Snap = true;
            }
        }

        /// <summary>Parried: they bounce back off the guard.</summary>
        private void Recoil()
        {
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Vector2 back = r.M.Pose.Ground - r.Dir * 0.45f * r.K;
                r.End = Combat.SafeSpot(back) ? back : r.M.Pose.Ground;
                r.Start = r.M.Pose.Ground;
            }
        }

        private void Recover(float dt)
        {
            if (Enter())
            {
                foreach (Runner r in _runners)
                {
                    r.Glinted = false;
                }
            }
            // (knocked back off a parry, they finish the little hop first)
            float k = Mathf.Clamp01(_pt / (_broken ? Mathf.Max(TRecover, 0.28f) : TRecover));
            foreach (Runner r in _runners)
            {
                if (r.Dead)
                {
                    continue;
                }
                Move m = r.M;
                m.Pose.Nudge = Vector2.zero;
                Glow(r, new Color(1f, 1f, 1f, 0f));
                if (_broken)
                {
                    // knocked back off the parry, a little hop
                    float b = Mathf.Clamp01(_pt / 0.24f);
                    m.Pose.Ground = Vector2.Lerp(r.Start, r.End, Anim.OutQuad(b));
                    m.Pose.Height = 0.15f * r.K * Mathf.Sin(Mathf.PI * b);
                    m.Pose.Rot = Mathf.Lerp(-22f, 0f, Anim.OutQuad(b));
                    m.Pose.SX = m.Pose.SY = 1f;
                    continue;
                }
                float amp = Anim.Spring(_pt, 8f, 16f);
                m.Pose.Height = Mathf.MoveTowards(m.Pose.Height, 0f, 4f * dt);
                if (!Combat.SafeSpot(m.Pose.Ground))
                {
                    // stood down in the middle of a hop over bad ground: finish it onto its mark
                    m.Pose.Ground = Vector2.MoveTowards(m.Pose.Ground, r.Start, 8f * dt);
                }
                m.Pose.SX = 1f + 0.12f * amp;
                m.Pose.SY = 1f - 0.14f * amp;
                m.Pose.Rot = Mathf.Lerp(m.Pose.Rot, 0f, Mathf.Clamp01(dt * 12f));
                if (_pt >= 0.1f && !r.Glinted && !_whiff)
                {
                    // the sheathing "click"
                    r.Glinted = true;
                    if (_vis)
                    {
                        float fwd = m.Pose.FaceRight ? 1f : -1f;
                        Fx.Glint(m.BodyPos() + new Vector2(-fwd * 2f * r.Px, 0f), r.Px, 0.16f, Level >= 2 ? r.Pal.Core : Color.white, 0.7f);
                    }
                }
            }
            if (k >= 1f)
            {
                End();
            }
        }

        private void End()
        {
            foreach (Runner r in _runners)
            {
                if (r.P != null && !r.P.Finished)
                {
                    r.P.Release(r.P.Pose.Height <= 0.02f);
                }
            }
            if (_p != null && !_p.Finished)
            {
                _p.Release(true);
            }
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Crew.Breathe(A);
            if (_launchPending && !_vGone && Combat.Living(_v) && !Moves.Locked(_v))
            {
                _launchPending = false;
                Combat.Launch(_v, _t, _launchDist, _launchApex, 620f);
            }
            if (EndLink == Link.Launched && !_vGone && Combat.Living(_v) && _v.Sid() == _vid)
            {
                // the strongest cutter goes after it too (whoever moves first takes it)
                Runner best = null;
                foreach (Runner r in _runners)
                {
                    if (r.P != null && !r.Dead && r.U.isAlive() && (best == null || Crew.Rank(r.U) > Crew.Rank(best.U)))
                    {
                        best = r;
                    }
                }
                if (best != null)
                {
                    if (Crew.Handoff(best.U, Link.Launched, _v) && Dev.On)
                    {
                        Dev.Trace("fang " + Id + " hands Launched on to " + best.U.asset.id + "#" + best.U.Sid());
                    }
                }
            }
            Finished = true;
        }

        // ------------------------------------------------------------- lines --

        /// <summary>Keeps every cut line where it belongs: growing behind the dash, hanging, flashing, fading.</summary>
        private void Lines()
        {
            foreach (Runner r in _runners)
            {
                if (r.Edge == null && r.Core == null)
                {
                    continue;
                }
                if (r.Dead)
                {
                    continue;
                }
                float lift = r.M.Pose.PivotUp * r.Px;
                Vector2 a = r.Start + new Vector2(0f, lift);
                Vector2 b;
                float alpha = 1f;
                float widen = 1f;
                float extra = 0f;
                switch (_phase)
                {
                    case Phase.Dash:
                        b = r.M.Pose.Ground + new Vector2(0f, lift);
                        break;
                    case Phase.Freeze:
                        b = r.End + new Vector2(0f, lift);
                        alpha = 0.85f + 0.15f * Mathf.Sin(_pt * 60f);
                        break;
                    case Phase.Burst:
                        b = r.End + new Vector2(0f, lift);
                        // the cut opens: a pixel or two wider for a moment (not a bar)
                        extra = Mathf.Round(2f * (1f - Mathf.Clamp01(_pt / TBurst)));
                        break;
                    default:
                        b = r.End + new Vector2(0f, lift);
                        alpha = 1f - Mathf.Clamp01(_pt / LinesFade);
                        widen = 1f - 0.5f * Mathf.Clamp01(_pt / LinesFade);
                        break;
                }
                bool flash = _phase == Phase.Burst && _pt < 0.05f;
                Stretch(r.Edge, a, b, r.EdgeThick * widen + extra, flash ? Color.white : Fx.WithA(r.Pal.Edge, (Level <= 1 ? 0.6f : 1f) * alpha));
                Stretch(r.Core, a, b, r.CoreThick, Fx.WithA(flash ? Color.white : r.Pal.Core, alpha));
                if (_phase == Phase.Recover && _pt >= LinesFade)
                {
                    r.Edge?.Kill();
                    r.Core?.Kill();
                    r.Edge = r.Core = null;
                }
            }
        }

        /// <summary>The cut lines fade out over the start of the sheathe (all of it when it is short).</summary>
        private float LinesFade => Mathf.Min(0.22f, TRecover * 0.9f);

        /// <summary>Lays a held line from a to b, 'thick' pixels wide (whole pixels), in colour c.</summary>
        private static void Stretch(Particle p, Vector2 a, Vector2 b, float thick, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Vector2 d = b - a;
            float len = Mathf.Max(p.Px, d.magnitude);
            p.Pos = (a + b) * 0.5f;
            p.SX = len / p.Px;
            p.SY = Mathf.Max(1f, Mathf.Round(thick));
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Color(c);
        }

        private void Glow(Runner r, Color c)
        {
            if (r.P != null)
            {
                r.P.GlowColor = c;
            }
            else
            {
                _leadGlow.Color = c;
            }
        }

        public override void Frame()
        {
            _leadGlow.Frame();
        }

        private void KillLines()
        {
            foreach (Runner r in _runners)
            {
                r.Edge?.Kill();
                r.Core?.Kill();
                r.Edge = r.Core = null;
            }
        }

        public override void Abort()
        {
            KillLines();
            _leadGlow.Kill();
            foreach (Runner r in _runners)
            {
                if (r.P != null && !r.P.Finished)
                {
                    r.P.Release(r.P.Pose.Height <= 0.02f);
                }
            }
            if (_p != null && !_p.Finished)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
        }

        public override void OnEnd(bool aborted)
        {
            Abort();
            Director.Release(this);
            foreach (Runner r in _runners)
            {
                if (r.P != null)
                {
                    Crew.Rest(r.U);
                }
            }
            if (_broken && !aborted && A.isAlive())
            {
                Combat.Stagger(A, 0.45f);
            }
        }
    }

    /// <summary>
    /// Cross Fang as a technique. Entry: Adept. The AI uses it on an enemy it can reach when a free
    /// friend stands within 4 tiles of that enemy on roughly the other side (III: up to three
    /// friends around it, one of them across). Everyone pays its share.
    /// </summary>
    internal sealed class CrossFangTech : Technique
    {
        private static readonly float[] Costs = { 12f, 20f, 30f };

        public override string Id => "cross_fang";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Team;

        /// <summary>Allies dash through one enemy from opposite sides as blurs.</summary>
        public override Form Forms => Form.Blade | Form.Dash | Form.Team | Form.Line | Form.Fast;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Light : level == 2 ? Scale.Heavy : Scale.Heavy;

        public override float[] ManaCost => Costs;
        public override float[] Cooldown => new[] { 10f, 10f, 10f };
        public override string Icon => "ui/Icons/sakuga/iconCrossFang";

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            float share = Costs[Mathf.Clamp(level - 1, 0, 2)];
            Actor v = c.Focus;
            List<Actor> partners = null;
            if (v != null && Ok(c, v))
            {
                partners = Partners(c, v, level, share);
            }
            if (partners == null && !c.Linked && c.ForceTarget == null)
            {
                // another enemy close by may be better placed (a friend already behind it)
                for (int i = 0; i < c.Enemies.Count && i < 6; i++)
                {
                    Actor u = c.Enemies[i];
                    if (u == v || !Ok(c, u))
                    {
                        continue;
                    }
                    partners = Partners(c, u, level, share);
                    if (partners != null)
                    {
                        v = u;
                        break;
                    }
                }
            }
            if (partners == null || v == null)
            {
                return null;
            }
            c.Allowed(v, out float cm);
            if (!c.Forced && !c.Linked && v.getHealth() <= c.Expected(v, Crew.Plain(c)))
            {
                return null;
            }
            int cutters = Mathf.Min(4, 1 + partners.Count);
            float total = CrossFang.CutMult(level) * CrossFang.Crowd(cutters) * cutters;
            float score = c.Value(v, c.Expected(v, total), cm) + 30f + (Tiers.Of(v) != Tier.None ? 15f : 0f);
            return new Plan { Target = v, Point = v.current_position, Count = 1, Score = score, Others = partners, Data = c.Forced };
        }

        private static bool Ok(Ctx c, Actor v)
        {
            if (!Crew.Target(c.Self, v) || !Combat.Grounded(v) || !c.Allowed(v) || Crew.Failed("cross_fang", v))
            {
                return false;
            }
            return c.Dist(v) <= (c.Forced ? 6f : 4.5f);
        }

        /// <summary>
        /// Free friends within 4 tiles of v, the one best placed across from the leader first (III:
        /// up to three, the others anywhere around it); tiered friends first, untiered ones from
        /// level II. "Across" is generous (a friend off to the side slides round to its end of the
        /// X as they take their marks). Null if nobody stands across (a god-power order takes anyone).
        /// </summary>
        private static List<Actor> Partners(Ctx c, Actor v, int level, float share)
        {
            Vector2 vp = v.current_position;
            Vector2 toL = c.Pos - vp;
            if (toL.sqrMagnitude < 0.0025f)
            {
                return null;
            }
            int max = level >= 3 ? 3 : 1;
            float need = level >= 3 ? 70f : 80f;
            Actor across = null;
            float acrossAng = 0f;
            List<Actor> others = null;
            for (int pass = 0; pass < (level >= 2 ? 2 : 1); pass++)
            {
                if (pass == 1 && across != null && (others != null && others.Count + 1 >= max || max == 1))
                {
                    break;   // enough tiered friends: the untiered ones aren't needed
                }
                for (int i = 0; i < c.Allies.Count; i++)
                {
                    Actor u = c.Allies[i];
                    Vector2 toU = u.current_position - vp;
                    if (toU.sqrMagnitude > 16f || (Tiers.Of(u) != Tier.None) != (pass == 0))
                    {
                        continue;
                    }
                    if (!Crew.Free(c.Self, u) || (!c.Forced && !Crew.CanPay(u, share)) || !Combat.Allowed(u, v))
                    {
                        continue;
                    }
                    float ang = Vector2.Angle(toL, toU);
                    // the most-across friend takes the far end (a tiered one over an untiered one)
                    if (ang >= need && (across == null || ang > acrossAng && (pass == 0 || Tiers.Of(across) == Tier.None)))
                    {
                        if (across != null && (level >= 3 || c.Forced))
                        {
                            (others ??= new List<Actor>(3)).Add(across);
                        }
                        across = u;
                        acrossAng = ang;
                    }
                    else if (level >= 3 || c.Forced)
                    {
                        (others ??= new List<Actor>(3)).Add(u);
                    }
                }
            }
            if (across == null && (!c.Forced || others == null))
            {
                return null;
            }
            List<Actor> list = new List<Actor>(max);
            if (across != null)
            {
                list.Add(across);
            }
            if (others != null)
            {
                for (int i = 0; i < others.Count && list.Count < max; i++)
                {
                    list.Add(others[i]);
                }
            }
            return list;
        }

        public override bool Begin(Actor a, Plan p)
        {
            Actor v = p.Target;
            if (v == null || !Crew.Target(a, v) || p.Others == null)
            {
                return false;
            }
            bool forced = p.Data is bool b && b;
            float share = Costs[Mathf.Clamp(p.Level - 1, 0, 2)];
            List<Actor> partners = new List<Actor>();
            foreach (Actor u in p.Others)
            {
                if (Crew.Free(a, u) && (forced || Crew.CanPay(u, share)) && Combat.Allowed(u, v) && Vector2.Distance(u.current_position, v.current_position) <= 5f)
                {
                    partners.Add(u);
                }
            }
            return partners.Count > 0 && CrossFang.Begin(this, a, v, partners, p.Level, forced, share);
        }
    }
}
