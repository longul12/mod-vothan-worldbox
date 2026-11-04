using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// UNISON BURST (team). Three to ten tiered fighters stop where they stand. Each one's aura
    /// flares in its own tier colour and pours toward the leader in a braided stream; the
    /// leader's aura swells with every arrival until it holds a ball of all their colours, and
    /// then lets it go as one giant energy wave down the longest line of enemies.
    ///
    /// Beat sheet (game seconds at x1; real seconds in a cinematic):
    ///
    ///   GATHER   0.25   everyone stops; partners turn to the leader, the leader to the enemy line.
    ///                   Cinematic (level III with six or more, or a God among them, on screen):
    ///                   the world freezes, a dim veil falls, the cast stays lit, title card
    ///   CHARGE   0.8-1.5 (longer with more partners)  one after another each partner's aura
    ///                   flares (a flare ring, a glow in its tier colour, embers rising) and a comet
    ///                   of its power runs to the ball held up over the leader's head; from then on
    ///                   a steady thread of light ties it to the ball, braided strands flowing along
    ///                   it; every arrival rings the leader in that colour and grows its aura and
    ///                   the ball (overhead, so it never hides the leader and every stream is seen
    ///                   reaching it)
    ///                   0.4 s before the end of the charge the aim is taken and telegraphed (a line
    ///                   of light down the path, a ring where it ends); friends standing in that
    ///                   path leap aside, still pouring, and land just before the shot
    ///   WINDUP   0.16   the leader leans back and brings the ball down in front of its hands, a
    ///                   ring closes on its feet, the ball pulses white
    ///   SHOT     0.13 + 0.42   the world resumes; a flash at the hands, the wave races out 10-16
    ///                   tiles (with a God among them 14-22, a quarter wider): a white core inside
    ///                   the braided colours, strands spiralling along it; everything it reaches is
    ///                   hit (I x3 +0.8 per partner up to x9, II x4 +1 up to x12, III x5 +1.2 up to
    ///                   x15) and hurled along it; the ground under it is torn into a furrow (II:
    ///                   scorched; III: glowing, embers) and really ploughed up as the head passes
    ///                   (Kit.Ravage: I scorched, II blasted, III dug deeper with its banks set
    ///                   alight, a God among them force 4 and thickly burning banks;
    ///                   wider with more fighters, deeper with seven or more), a crater where it
    ///                   ends; the leader slides back from the recoil
    ///   FADE     0.30   the wave thins out; partners drop to a knee (Masters and up straighten
    ///                   and are let go at once to fight on)
    ///   RECOVER  0.36   everyone straightens up; a glint on the leader
    ///
    /// Tempo: from Expert up (the leader's tier) the gathering, charge, hold and fade tighten to
    /// 0.93 / 0.78 / 0.74 / 0.71 and the recovery to the tier's Linger (0.85 / 0.55 / 0.48 / 0.42)
    /// of these; a Master leader has no
    /// attack cooldown after it, and the wave leaves a Scattered link for the next technique.
    ///
    /// Level grammar: I a white wave with thin coloured strands, dust only; II the leader's colour
    /// with everyone's colours braided along it, a scorched furrow with small craters; III a
    /// white-hot core, a glowing seam down the furrow, embers, an impact frame and a global
    /// hit-stop. One impact frame per wave at most besides the shot's (the focus, or the first
    /// critical blow as a Black Flash).
    ///
    /// Variety: aimed down the longest line of enemies (re-aimed at the telegraph if they moved), or
    /// focused on one much stronger enemy (narrower, with an impact frame on it); width and length
    /// grow with the number of fighters; its colours are whoever took part.
    ///
    /// Robustness: a partner lost during the charge just stops pouring (and doesn't count); the
    /// leader's death lets everyone go and lifts the cinematic. The beam never leaves the map.
    /// </summary>
    internal sealed class UnisonBurst : Move
    {
        private enum Phase
        {
            Gather,
            Charge,
            Windup,
            Beam,
            Fade,
            Recover
        }

        private const float TWindup = 0.16f;
        private const float TExtend = 0.13f;

        // the leader sets the pace: from Master up the gathering, the charge, the wave's hold and
        // its fade tighten a little (Crew.Beat), the getting up a lot (Crew.Pace)
        private readonly float TGather, TSustain, TFade, TRecover;
        private const float Flow = 0.3f;

        private sealed class Member
        {
            public Actor U;
            public TeamPartner P;
            public Color Col;
            public float K, Px;
            public bool Dead, Arrived, Flared;
            public float Start, Arrive;
            public float Stream, Ember, Phase, Thread, Trail;
            public Particle Comet;
            // standing in the wave's path: it leaps aside just before the shot
            public bool Dodge, Dodged;
            public float DodgeAt;
            public Vector2 DodgeFrom, DodgeTo;
        }

        /// <summary>How long a partner's leap out of the wave's path takes.</summary>
        private const float TDodge = 0.24f;

        private sealed class Target
        {
            public Actor U;
            public long Id;
            public float Along;
            public bool Done;
        }

        private readonly List<Member> _members = new List<Member>();
        private readonly List<Target> _targets = new List<Target>();
        private List<Color> _braid;
        private Color _mix;
        private Vector2 _dir;
        private float _len, _halfW;
        private bool _cine, _held;
        private readonly Actor _focus;
        private Phase _phase = Phase.Gather;
        private float _pt;
        private bool _entered;
        private float _tCharge;
        private int _arrived;
        private float _power;
        private float _mult;
        private Vector2 _from, _back, _origin;
        private float _head;
        private bool _vis;
        private TeamHalo _glow;
        private readonly TeamHalo _lit;
        private Particle _ballGlow, _ballBody, _ballCore, _ballSwirl;
        private Particle _bOuter, _bInner, _bCore, _bHead;
        private Color _cOuter, _cInner;
        private float _strand, _ember, _ring;
        private int _furrow;
        private bool _endBurst;
        private readonly Palette _pal;

        /// <summary>Unison Bursts started since the game started, and how many were cinematic (dev log).</summary>
        internal static int Started, Cinematics;

        private UnisonBurst(Actor a, int level, Vector2 dir, Actor focus) : base(a)
        {
            Level = level;
            _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : new Vector2(a.flip ? 1f : -1f, 0f);
            _focus = focus;
            _pal = Palette.For(a, level);
            _glow = new TeamHalo(this, true, FxLayer.Over, 0);
            _lit = new TeamHalo(this, false, FxLayer.Cine, 6);
            Pose.FaceRight = Crew.Face(Vector2.zero, _dir, a.flip);
            _beat = Crew.Beat(a);
            TGather = 0.25f * _beat;
            TSustain = 0.42f * _beat;
            TFade = 0.3f * _beat;
            TRecover = 0.36f * Crew.Pace(a);
        }

        private readonly float _beat;

        // ---- the world: the furrow the wave tears (radius in tiles, force 1-4) and how far a friend
        // in its path leaps aside so the fire never takes it
        private float _rr = 1f;
        private int _force;
        private bool _god;
        private float _burnClear;
        private bool _deep;
        private List<Actor> _spare;

        /// <summary>
        /// The furrow's size from who is still pouring: wider with more fighters (and half again with
        /// a God among them), as deep as the level (I scorched, II blasted, III burning; a God's a
        /// scale deeper, force 4), and a second blast down the middle with seven or more (deeper).
        /// </summary>
        private void Ground(int partners)
        {
            int all = partners + 1;
            _rr = Crew.Radius(1f + 0.12f * all, _god);
            _force = Crew.Force(Level, _god);
            _deep = all >= 7 && Level >= 2;
            // a friend who can burn keeps well clear of the burning banks
            _burnClear = _force >= 3 ? Bank + 1.5f : 0f;
        }

        /// <summary>How far from the wave's line its furrow's banks burn (just outside the torn ground).</summary>
        private float Bank => _rr + 0.6f;

        /// <summary>The leader and every partner still pouring (the fire never takes the ground under them).</summary>
        private List<Actor> Spare()
        {
            List<Actor> list = new List<Actor>(_members.Count + 1) { A };
            foreach (Member mb in _members)
            {
                if (!mb.Dead && mb.U.isAlive())
                {
                    list.Add(mb.U);
                }
            }
            return list;
        }

        /// <summary>
        /// The wave's blow on everyone it reaches (a multiple of the leader's damage; Combat.Hit adds
        /// its tier's Might), growing with every partner who poured in and with the level:
        /// I 3 + 0.8 each (at most 9), II 4 + 1 each (12), III 5 + 1.2 each (15).
        /// </summary>
        internal static float WaveMult(int level, int partners)
        {
            return level <= 1 ? Mathf.Min(9f, 3f + 0.8f * partners) : level == 2 ? Mathf.Min(12f, 4f + partners) : Mathf.Min(15f, 5f + 1.2f * partners);
        }

        public override bool Cinematic => _cine;

        /// <summary>
        /// The leader holding the gathering ball (at world speed, not in a cinematic): the biggest
        /// wind-up of all, and the enemy's moment to break it (the partners' streams scatter with it).
        /// </summary>
        public override bool Charging => !_cine && _phase <= Phase.Charge && !Finished;

        private FxLayer Layer => _cine ? FxLayer.Cine : FxLayer.Top;

        /// <summary>
        /// Ground marks (rings at the feet, the telegraph): on the ground, or in a cinematic above the
        /// veil like the rest of the show (at LowOrder: under the lit units).
        /// </summary>
        private FxLayer Low => _cine ? FxLayer.Cine : FxLayer.Ground;

        private int LowOrder(int order)
        {
            return _cine ? 0 : order;
        }

        // ------------------------------------------------------------- begin --

        public static bool Begin(Technique tech, Actor a, List<Actor> partners, int level, Vector2 dir, Actor focus, bool forced, float share)
        {
            if (!Moves.CanPerform(a) || partners == null || partners.Count == 0)
            {
                return false;
            }
            UnisonBurst m = new UnisonBurst(a, level, dir, focus) { Tech = tech };
            List<Actor> cast = new List<Actor>(partners) { a };
            if (!Director.Claim(m, 5f, cast.ToArray()))
            {
                return false;
            }
            foreach (Actor u in partners)
            {
                TeamPartner pm = new TeamPartner(u, m, Crew.Face(u.current_position, a.current_position, u.flip));
                if (!Moves.Start(pm))
                {
                    Director.Release(u);
                    continue;
                }
                m._members.Add(new Member
                {
                    U = u,
                    P = pm,
                    Col = Tiers.Of(u) == Tier.None ? new Color(1f, 0.9f, 0.7f, 1f) : Tiers.Color(Tiers.Of(u)),
                    K = Crew.KOf(u),
                    Px = Crew.PxOf(u),
                    Phase = Random.Range(0f, 6.28f)
                });
            }
            if (m._members.Count == 0)
            {
                Director.Release(m);
                return false;
            }
            m.Setup();
            if (m._cine && !Cinema.TryBegin(m))
            {
                m._cine = false;
            }
            if (!Moves.Start(m))
            {
                if (m._cine)
                {
                    Cinema.End(m);
                }
                m.ReleaseAll();
                Director.Release(m);
                return false;
            }
            cast.Clear();
            cast.Add(a);
            foreach (Member mb in m._members)
            {
                cast.Add(mb.U);
                if (m._cine)
                {
                    // (started before the show knew it was a cinematic: the game's big star over a
                    // favourite partner is hidden too, as it is for the leader)
                    mb.P.HideStar = true;
                }
            }
            Director.Claim(m, m.TGather + m._tCharge + TWindup + TExtend + m.TSustain + m.TFade + m.TRecover + 1.5f, cast.ToArray());
            if (!forced)
            {
                foreach (Member mb in m._members)
                {
                    Crew.Pay(mb.U, tech, share, 12f);
                }
            }
            Started++;
            if (m._cine)
            {
                Cinematics++;
            }
            Dev.Watch(a.current_position + m._dir * 3f + new Vector2(0f, 1f));
            if (Dev.On)
            {
                Dev.Trace("unison " + m.Id + " " + Techniques.Roman(level) + " with " + m._members.Count + " partners, charge " + m._tCharge.ToString("F2")
                    + (m._cine ? " CINEMATIC" : " light") + (focus != null ? " on " + focus.asset.id : ""));
            }
            return true;
        }

        /// <summary>Who pours when, the size of the wave, whether the world stops to watch.</summary>
        private void Setup()
        {
            int n = _members.Count;
            Vector2 at = A.current_position;
            _members.Sort((x, y) => (x.U.current_position - at).sqrMagnitude.CompareTo((y.U.current_position - at).sqrMagnitude));
            _tCharge = Mathf.Clamp(0.65f + 0.09f * n, 0.8f, 1.5f) * _beat;
            float stagger = n > 1 ? (_tCharge - 0.5f) / (n - 1) : 0f;
            for (int i = 0; i < n; i++)
            {
                _members[i].Start = 0.05f + i * stagger;
                _members[i].Arrive = _members[i].Start + Flow;
            }
            List<Actor> all = new List<Actor> { A };
            bool god = Tiers.Of(A) == Tier.God;
            foreach (Member mb in _members)
            {
                all.Add(mb.U);
                god |= Tiers.Of(mb.U) == Tier.God;
            }
            _braid = Crew.Braid(all);
            _god = god;
            Ground(n);
            // the wave's own colour is the leader's (an average of different hues is only mud);
            // everyone else's colour runs along it in the braided strands
            _mix = _braid[0];
            _mix.a = 1f;
            float k = Mathf.Min(K, 1.6f);
            // with a God among them the wave is a God's: 40% longer (up to 22 tiles) and a quarter wider
            _len = Mathf.Clamp(10f + 0.75f * n, 10f, 16f) * (god ? 1.4f : 1f);
            _halfW = 0.5f * (1f + 0.06f * (n + 1)) * k * (_focus != null && Tiers.Power(_focus) >= 2f * Tiers.Power(A) ? 0.8f : 1f) * (god ? 1.25f : 1f);
            _cine = Level >= 3 && (n + 1 >= 6 || god) && Cinema.FullAt(at);
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _vis = Fx.OnScreen(Pose.Ground + _dir * 3f, 0.6f);
            Watchdog();
            _pt += dt;
            switch (_phase)
            {
                case Phase.Gather:
                    Gather();
                    break;
                case Phase.Charge:
                    Charge(dt);
                    break;
                case Phase.Windup:
                    Windup(dt);
                    break;
                case Phase.Beam:
                    BeamPhase(dt);
                    break;
                case Phase.Fade:
                    FadePhase(dt);
                    break;
                default:
                    Recover();
                    break;
            }
            foreach (Member mb in _members)
            {
                DodgeStep(mb);
            }
            Ball(dt);
            if (T > 9f && !Finished)
            {
                End();   // never hold the world (or anyone) hostage
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On)
            {
                Dev.Trace("unison " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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
            foreach (Member mb in _members)
            {
                if (!mb.Dead && mb.P.Gone)
                {
                    mb.Dead = true;
                    if (Dev.On)
                    {
                        Dev.Trace("unison " + Id + " lost a partner in " + _phase);
                    }
                }
            }
        }

        /// <summary>Marks an effect to keep moving while the cinematic holds the world.</summary>
        private Particle C(Particle p)
        {
            if (p != null && _cine)
            {
                p.Cine = true;
            }
            return p;
        }

        private Vector2 Hands()
        {
            return BodyPos() + _dir * 4f * Px;
        }

        /// <summary>The ball's radius in pixels: it grows with every arrival and swells in the windup.</summary>
        private int BallR()
        {
            float pump = _phase == Phase.Windup ? 1f + 0.3f * Mathf.Clamp01(_pt / TWindup) : 1f;
            return Mathf.Clamp(Mathf.RoundToInt((1.5f + 4f * _power) * Mathf.Min(K, 1.6f) * pump), 1, 7);
        }

        /// <summary>
        /// Where everyone's power gathers: held up above the leader's head while it pours in (every
        /// stream is seen reaching it, and it never hides the leader), brought down in front of the
        /// hands in the windup; the wave leaves from there.
        /// </summary>
        private Vector2 BallPos()
        {
            float r = BallR();
            Vector2 over = HeadPos() + new Vector2(0f, (r + 2f) * Px);
            if (_phase == Phase.Gather || _phase == Phase.Charge)
            {
                return over;
            }
            Vector2 front = Hands() + _dir * (r + 1f) * Px;
            if (_phase == Phase.Windup)
            {
                return Vector2.Lerp(over, front, Anim.InOutCubic(Mathf.Clamp01(_pt / TWindup)));
            }
            return front;
        }

        /// <summary>
        /// A partner's power sets off: a bright comet leaves it and runs the stream to the ball,
        /// arriving the moment the arrival ring goes off.
        /// </summary>
        private void Comet(Member mb)
        {
            TeamPartner m = mb.P;
            Vector2 a = m.BodyPos();
            int r = Level >= 2 ? 2 : 1;
            Particle p = C(Fx.Spawn(Art.Disk(r, 1f, 0.25f), a, mb.Px, Flow, Layer, 6));
            if (p == null)
            {
                return;
            }
            p.Color(Color.white, Color.Lerp(mb.Col, Color.white, 0.5f), Color.white);
            float side = Random.value < 0.5f ? -1f : 1f;
            p.Step = (q, d) =>
            {
                Vector2 from = m.Finished ? a : m.BodyPos();
                Vector2 to = BallPos();
                Vector2 dir = to - from;
                Vector2 n = dir.sqrMagnitude > 0.0001f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
                float u = q.K * q.K;
                q.Pos = Vector2.Lerp(from, to, u) + n * side * 2.5f * mb.Px * Mathf.Sin(Mathf.PI * u);
            };
            mb.Comet = p;
            mb.Trail = 0f;
        }

        // ------------------------------------------------------------ phases --

        private void Gather()
        {
            if (Enter())
            {
                if (_cine)
                {
                    Cinema.Hold(this);
                    _held = true;
                    Cinema.Dim(0.5f, new Color(0.02f, 0.01f, 0.04f, 1f), 0.3f);
                    Cinema.Title(TitleText(), Level >= 2 ? Color.Lerp(_pal.Edge, Color.white, 0.25f) : Color.white, 1.9f * _beat, Pose.Ground, 1f);
                    _lit.Color = Color.white;
                    // the leader's aura burns above the veil too
                    _glow.Kill();
                    _glow = new TeamHalo(this, true, FxLayer.Cine, 7);
                    foreach (Member mb in _members)
                    {
                        mb.P.Lit = true;
                        mb.P.GlowAboveVeil(true);
                    }
                }
                Anim.Sound("event:/SFX/POWERS/Powerup", A.current_tile);
            }
            float k = Anim.OutQuad(Mathf.Clamp01(_pt / TGather));
            Pose.SX = Mathf.Lerp(1f, 1.06f, k);
            Pose.SY = Mathf.Lerp(1f, 0.94f, k);
            Pose.Rot = -6f * k;
            foreach (Member mb in _members)
            {
                if (mb.Dead)
                {
                    continue;
                }
                Move m = mb.P;
                m.Pose.FaceRight = Crew.Face(m.Pose.Ground, Pose.Ground, m.Pose.FaceRight);
                m.Pose.SX = Mathf.Lerp(1f, 1.06f, k);
                m.Pose.SY = Mathf.Lerp(1f, 0.94f, k);
            }
            if (_pt >= TGather)
            {
                Next(Phase.Charge);
            }
        }

        private void Charge(float dt)
        {
            float px = Px;
            // the partners: auras, streams
            foreach (Member mb in _members)
            {
                if (mb.Dead)
                {
                    continue;
                }
                TeamPartner m = mb.P;
                bool pouring = _pt >= mb.Start;
                float tr = pouring ? 0.5f : 0f;
                m.Pose.Nudge = pouring && ((int)(_pt * 60f + mb.Phase) & 1) == 0 ? new Vector2(tr, 0f) : Vector2.zero;
                m.Pose.Rot = Mathf.MoveTowards(m.Pose.Rot, pouring ? 10f : 4f, 80f * dt);
                m.Pose.SX = 1.06f;
                m.Pose.SY = 0.94f;
                float a = pouring ? 0.5f + 0.12f * Mathf.Sin(_pt * 30f + mb.Phase) : Mathf.Clamp01((_pt - mb.Start + 0.15f) / 0.15f) * 0.35f;
                m.GlowColor = Fx.WithA(mb.Col, Mathf.Max(0f, a));
                if (!pouring)
                {
                    continue;
                }
                if (!mb.Flared)
                {
                    mb.Flared = true;
                    if (_vis)
                    {
                        C(Fx.Ring(m.Pose.Ground, mb.Px, 0.3f, 3f, 13f * mb.K, 2, 1, Low, LowOrder(6), Art.Ground, 2.4f, Fx.WithA(mb.Col, 0.95f), Fx.WithA(mb.Col, 0f)));
                        Comet(mb);
                    }
                }
                if (!_vis)
                {
                    continue;
                }
                // embers rising off it (its aura burning)
                mb.Ember -= dt;
                if (mb.Ember <= 0f)
                {
                    mb.Ember = 0.045f * Mathf.Max(1f, _members.Count / 4f) / Fx.Detail();
                    Vector2 at = m.Pose.Ground + new Vector2(Random.Range(-3f, 3f), Random.Range(0f, 2f)) * mb.Px;
                    Particle e = C(Fx.Spawn(Art.Pixel, at, mb.Px, Random.Range(0.4f, 0.7f), Layer, 3));
                    e?.Moving(new Vector2(Random.Range(-3f, 3f), Random.Range(14f, 24f)) * mb.Px, 0f, 1f).Sized(Level >= 2 ? 2f : 1f, 1f)
                        .Color(Color.white, mb.Col, Fx.WithA(mb.Col, 0f));
                }
                // the thread: a steady flickering line of light from it to the ball for as long as
                // it pours (every partner's own, all at once: the web of everyone feeding one)
                mb.Thread -= dt;
                if (mb.Thread <= 0f)
                {
                    mb.Thread = 0.05f;
                    Vector2 from = m.BodyPos();
                    Vector2 to = BallPos();
                    float fl = 0.55f + 0.35f * Random.value;
                    C(Kit.Line(from, to, mb.Px, 0.065f, 1, Layer, 2, Fx.WithA(Color.Lerp(mb.Col, Color.white, 0.45f), fl), Fx.WithA(mb.Col, fl * 0.6f)));
                    if (Level >= 2)
                    {
                        C(Kit.Line(from, to, mb.Px, 0.065f, 3, Layer, 1, Fx.WithA(mb.Col, 0.22f * fl), Fx.WithA(mb.Col, 0.1f * fl)));
                    }
                }
                // the stream: two braided strands flowing along it to the ball
                mb.Stream -= dt;
                if (mb.Stream <= 0f)
                {
                    mb.Stream = 0.025f * Mathf.Max(1f, _members.Count / 3f) / Fx.Detail();
                    Strand(mb, 0f);
                    Strand(mb, Mathf.PI);
                }
                // the comet's tail
                if (mb.Comet != null && mb.Comet.Alive)
                {
                    mb.Trail -= dt;
                    if (mb.Trail <= 0f)
                    {
                        mb.Trail = 0.016f;
                        Particle t = C(Fx.Spawn(Art.Pixel, mb.Comet.Pos, mb.Px, 0.16f, Layer, 5));
                        t?.Sized(Level >= 2 ? 3f : 2f, 0.5f).Color(Color.white, mb.Col, Fx.WithA(mb.Col, 0f));
                    }
                }
            }
            // the leader: arrivals, aura
            foreach (Member mb in _members)
            {
                if (!mb.Dead && !mb.Arrived && _pt >= mb.Arrive)
                {
                    mb.Arrived = true;
                    _arrived++;
                    Arrival(mb);
                }
            }
            _power = Mathf.MoveTowards(_power, _members.Count > 0 ? _arrived / (float)_members.Count : 1f, 2.5f * dt);
            Pose.SX = 1.1f;
            Pose.SY = 0.9f;
            Pose.Rot = -10f - 4f * _power;
            Pose.Nudge = _power > 0.3f && ((int)(_pt * 60f) & 1) == 0 ? new Vector2(0.5f, 0f) : Vector2.zero;
            _glow.Color = Fx.WithA(Color.Lerp(_mix, Color.white, 0.2f), (0.15f + 0.45f * _power) * (0.85f + 0.15f * Mathf.Sin(_pt * 36f)));
            if (_vis)
            {
                _ember -= dt;
                if (_ember <= 0f && _power > 0f)
                {
                    _ember = 0.05f / Mathf.Max(0.3f, _power);
                    Color c = _braid[Random.Range(0, _braid.Count)];
                    Vector2 at = Pose.Ground + new Vector2(Random.Range(-4f, 4f), Random.Range(0f, 3f)) * px;
                    Particle e = C(Fx.Spawn(Art.Pixel, at, px, Random.Range(0.4f, 0.8f), Layer, 3));
                    e?.Moving(new Vector2(Random.Range(-4f, 4f), Random.Range(16f, 30f)) * px, 0f, 1f).Color(Color.white, c, Fx.WithA(c, 0f));
                }
            }
            if (!_aimed && _pt >= _tCharge - 0.4f)
            {
                // the aim is taken now, so the telegraph shows where the wave will really go (and
                // friends in its path have time to get out of it)
                _aimed = true;
                Aim(Pose.Ground);
                Telegraph();
                ClearLane();
            }
            if (_pt >= _tCharge)
            {
                Next(Phase.Windup);
            }
        }

        private bool _aimed;

        /// <summary>
        /// Friends standing in the wave's path (between the leader and its end, within its width)
        /// leap aside, to whichever side they already lean to (the other one if that ground is bad),
        /// landing just before the shot: the wave tears past them, never through them. One with
        /// nowhere to go stays put (and the wave only ever hurts enemies).
        /// </summary>
        private void ClearLane()
        {
            Vector2 g = Pose.Ground;
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            float start = Mathf.Max(0f, (_tCharge - _pt) + TWindup - TDodge - 0.04f);
            int dodging = 0;
            foreach (Member mb in _members)
            {
                if (mb.Dead)
                {
                    continue;
                }
                Vector2 p = mb.P.Pose.Ground;
                Vector2 rel = p - g;
                float along = Vector2.Dot(rel, _dir);
                float off = Vector2.Dot(rel, side);
                // out of the wave, and (when the furrow's banks will burn) out of the fire it
                // leaves, unless it is a Master or better (they fight through flames); one that
                // can't get that far at least gets out of the wave (the banks never burn near it)
                float beam = _halfW + 0.42f * mb.K + 0.25f;
                float clear = Mathf.Max(beam, Crew.FireProof(mb.U) ? 0f : _burnClear);
                if (along < 0.3f || along > _len + 0.5f || Mathf.Abs(off) >= clear)
                {
                    continue;
                }
                float s0 = Mathf.Abs(off) > 0.05f ? Mathf.Sign(off) : (Random.value < 0.5f ? -1f : 1f);
                Vector2 to = Vector2.zero;
                bool ok = false;
                for (int far = 0; far < 2 && !ok; far++)
                {
                    float c = far == 0 ? clear : beam;
                    if (far == 1 && (c >= clear || Mathf.Abs(off) >= c))
                    {
                        break;
                    }
                    float s = s0;
                    for (int pass = 0; pass < 2 && !ok; pass++, s = -s)
                    {
                        Vector2 want = Combat.Clamp(g + _dir * along + side * s * c);
                        if (Combat.SafeSpot(want) && Vector2.Distance(want, p) <= 3f)
                        {
                            to = want;
                            ok = true;
                        }
                    }
                }
                if (!ok)
                {
                    continue;
                }
                mb.Dodge = true;
                mb.Dodged = false;
                mb.DodgeAt = T + start;
                mb.DodgeFrom = p;
                mb.DodgeTo = to;
                dodging++;
            }
            if (Dev.On && dodging > 0)
            {
                Dev.Trace("unison " + Id + " clears its lane: " + dodging + " friend(s) leap aside");
            }
        }

        /// <summary>A partner's leap aside: a quick sideways hop, still pouring, a puff where it lands.</summary>
        private void DodgeStep(Member mb)
        {
            if (!mb.Dodge || mb.Dead)
            {
                return;
            }
            float k = (T - mb.DodgeAt) / TDodge;
            if (k <= 0f)
            {
                return;
            }
            Move m = mb.P;
            k = Mathf.Clamp01(k);
            m.Pose.Ground = Vector2.Lerp(mb.DodgeFrom, mb.DodgeTo, Anim.OutQuad(k));
            m.Pose.Height = 4f * 0.3f * mb.K * k * (1f - k);
            if (k < 1f)
            {
                float s = Mathf.Sin(Mathf.PI * k);
                m.Pose.SX = 1f - 0.08f * s;
                m.Pose.SY = 1f + 0.1f * s;
                if (_vis && Time.frameCount % 2 == 0)
                {
                    // a faint streak of its own colour (it is still pouring as it jumps)
                    C(Fx.Afterimage(m, 0.1f, Fx.WithA(mb.Col, 0.45f), Fx.WithA(mb.Col, 0f)));
                }
                return;
            }
            if (!mb.Dodged)
            {
                mb.Dodged = true;
                m.Pose.Height = 0f;
                if (_vis)
                {
                    Fx.LandingPuff(mb.DodgeTo, mb.Px, Combat.TileAt(mb.DodgeTo));
                }
            }
        }

        /// <summary>
        /// The telegraph: a line of light on the ground down the wave's path and a ring where it
        /// will end, 0.55 s before it fires (others can see it coming and get out of the way).
        /// </summary>
        private void Telegraph()
        {
            Vector2 g = Pose.Ground;
            float len = Mathf.Min(_len, EdgeDistance(g, _dir));
            Vector2 a = g + _dir * 0.6f;
            Vector2 b = g + _dir * len;
            if (!Kit.Visible(Vector2.Lerp(a, b, 0.5f), 0.6f))
            {
                return;
            }
            float life = 0.4f + TWindup;
            Particle l = C(Kit.Line(a, b, Px, life, 1, Low, LowOrder(5), Fx.WithA(_mix, 0f), Fx.WithA(_mix, 0.45f), Fx.WithA(Color.white, 0.7f), Fx.WithA(_mix, 0.5f)));
            if (l != null)
            {
                l.Snap = true;
            }
            C(Fx.Ring(b, Px, life, 16f * K, 5f * K, 1, 1, Low, LowOrder(5), Art.Ground, 1.5f, Fx.WithA(_mix, 0f), Fx.WithA(_mix, 0.6f), Fx.WithA(Color.white, 0.8f)));
        }

        /// <summary>One particle of a partner's stream, braiding around the line to the leader's hands.</summary>
        private void Strand(Member mb, float phase)
        {
            TeamPartner m = mb.P;
            Vector2 a = m.BodyPos();
            Particle p = C(Fx.Spawn(Art.Pixel, a, mb.Px, Flow, Layer, 4));
            if (p == null)
            {
                return;
            }
            float amp = Random.Range(2.5f, 4f) * mb.Px;
            float ph = phase + mb.Phase;
            p.Sized(Level >= 2 ? 3f : 2f, 1f).Color(Color.white, mb.Col, Fx.WithA(mb.Col, 0.6f));
            p.Step = (q, d) =>
            {
                Vector2 from = m.Finished ? a : m.BodyPos();
                Vector2 to = BallPos();
                Vector2 dir = to - from;
                Vector2 side = dir.sqrMagnitude > 0.0001f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
                float u = q.K;
                float bow = Mathf.Sin(Mathf.PI * u);
                q.Pos = Vector2.Lerp(from, to, u * u * (3f - 2f * u)) + side * (amp * Mathf.Sin(ph + u * 9f) * bow + 0.6f * amp * bow);
            };
        }

        /// <summary>A partner's power reaches the leader: a ring in its colour, sparks, a bigger aura.</summary>
        private void Arrival(Member mb)
        {
            if (!_vis)
            {
                return;
            }
            float px = Px;
            Vector2 at = BodyPos();
            C(Fx.Ring(at, px, 0.26f, 3f, (10f + 4f * _arrived) * K, 1, 1, Layer, 3, 1f, 2.4f, Color.white, Fx.WithA(mb.Col, 0.9f), Fx.WithA(mb.Col, 0f)));
            int n = Fx.Count(6);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = C(Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], BallPos(), px, Random.Range(0.14f, 0.24f), Layer, 8));
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(40f, 90f) * px, 0f, 6f);
                p.FaceVel = true;
                p.Color(Color.white, mb.Col, Fx.WithA(mb.Col, 0f));
            }
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
        }

        private void Windup(float dt)
        {
            if (Enter() && _vis)
            {
                C(Fx.Ring(Pose.Ground, Px, TWindup, 22f * K, 4f, 1, 2, Low, LowOrder(4), Art.Ground, 1.5f,
                    Fx.WithA(_mix, 0f), Fx.WithA(_mix, 0.8f), Color.white));
            }
            float k = Anim.OutQuad(Mathf.Clamp01(_pt / TWindup));
            Pose.Rot = Mathf.Lerp(-10f - 4f * _power, -22f, k);
            Pose.SX = Mathf.Lerp(1.1f, 1.18f, k);
            Pose.SY = Mathf.Lerp(0.9f, 0.82f, k);
            Pose.Nudge = ((int)(_pt * 80f) & 1) == 0 ? new Vector2(0.5f, 0f) : new Vector2(-0.5f, 0f);
            _glow.Color = Fx.WithA(Color.Lerp(_mix, Color.white, 0.5f + 0.5f * k), 0.6f + 0.3f * k);
            foreach (Member mb in _members)
            {
                if (!mb.Dead)
                {
                    mb.P.GlowColor = Fx.WithA(mb.Col, 0.6f);
                    mb.P.Pose.Rot = Mathf.MoveTowards(mb.P.Pose.Rot, 12f, 80f * dt);
                }
            }
            if (_pt >= TWindup)
            {
                Next(Phase.Beam);
            }
        }

        private void BeamPhase(float dt)
        {
            if (Enter())
            {
                Shot();
            }
            float k = Mathf.Clamp01(_pt / TExtend);
            _head = _len * Anim.OutCubic(k);
            // the leader: thrust forward, sliding back on the recoil
            float s = Mathf.Clamp01(_pt / (TExtend + TSustain));
            Pose.Ground = Vector2.Lerp(_from, _back, Anim.OutQuad(s));
            Pose.Rot = Mathf.Lerp(-22f, 16f, Anim.OutCubic(Mathf.Clamp01(_pt / 0.06f)));
            Pose.SX = 1.2f;
            Pose.SY = 0.85f;
            Pose.Nudge = ((int)(_pt * 60f) & 1) == 0 ? new Vector2(0.5f, 0f) : Vector2.zero;
            _origin = Hands();
            _glow.Color = Fx.WithA(Color.white, Mathf.Lerp(0.9f, 0.35f, s));
            foreach (Member mb in _members)
            {
                if (!mb.Dead)
                {
                    mb.P.GlowColor = Fx.WithA(mb.Col, Mathf.Lerp(0.7f, 0.15f, s));
                    mb.P.Pose.Nudge = Vector2.zero;
                }
            }
            HitAlong();
            Furrow();
            Draw(1f, dt);
            if (k >= 1f && !_endBurst)
            {
                _endBurst = true;
                EndBurst();
            }
            if (_pt >= TExtend + TSustain)
            {
                Next(Phase.Fade);
            }
        }

        /// <summary>The release: the world resumes, a flash at the hands, the wave is born.</summary>
        private void Shot()
        {
            if (_held)
            {
                Cinema.Unhold(this);
                _held = false;
                Cinema.Undim(0.8f);
            }
            KillBall();
            _from = Pose.Ground;
            if (!_aimed)
            {
                _aimed = true;
                Aim(_from);
            }
            Vector2 back = _from - _dir * 0.35f * K;
            _back = Combat.SafeSpot(back) ? back : _from;
            Targets(_from);
            int live = 0;
            foreach (Member mb in _members)
            {
                if (!mb.Dead && mb.Arrived)
                {
                    live++;
                }
            }
            _mult = WaveMult(Level, live);
            Ground(live);
            _origin = Hands();
            Anim.Sound("event:/SFX/POWERS/HeatRayMelts", A.current_tile);
            Anim.Sound(Level >= 3 ? "event:/SFX/EXPLOSIONS/ExplosionBig" : Level == 2 ? "event:/SFX/EXPLOSIONS/ExplosionMiddle" : "event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
            if (_vis)
            {
                if (Level >= 3)
                {
                    Kit.ImpactFrame(_origin, Px, 1.2f * K, _pal, false, Layer);
                }
                Particle f = C(Kit.Flash(_origin, Px, Mathf.RoundToInt(9 * K), 0.12f, Color.white, Layer));
                f?.Color(Color.white, Fx.WithA(_mix, 0.8f), Fx.WithA(_mix, 0f));
                Kit.Shake(_origin, 0.45f, Level >= 3 ? 0.014f : 0.009f, K);
                Kit.DustRing(_from, Px, A.current_tile, 12, 70f, 0.6f, K);
            }
            if (Level >= 3)
            {
                Kit.Stop(_origin, 0.1f);
            }
            int thick = Mathf.Max(3, Mathf.RoundToInt(2f * _halfW / Px));
            Color outer = Level >= 2 ? Fx.WithA(_mix, 0.4f) : new Color(0.9f, 0.94f, 1f, 0.35f);
            Color inner = Level >= 2 ? Fx.WithA(Color.Lerp(_mix, Color.white, 0.3f), 0.85f) : new Color(1f, 1f, 1f, 0.75f);
            _cOuter = outer;
            _cInner = inner;
            _bOuter = Beam(thick, outer, 1);
            _bInner = Beam(Mathf.Max(2, Mathf.RoundToInt(thick * 0.55f)), inner, 2);
            _bCore = Beam(Mathf.Max(1, Mathf.RoundToInt(thick * (Level >= 3 ? 0.26f : 0.18f))), Color.white, 3);
            _bHead = C(Fx.Spawn(Art.Disk(Mathf.Max(2, Mathf.RoundToInt(_halfW / Px * 0.9f)), 1f, 0.4f), _origin, Px, 1f, Layer, 5));
            if (_bHead != null)
            {
                _bHead.Held = true;
                _bHead.Color(Color.white);
            }
            if (Dev.On)
            {
                Dev.Trace("unison " + Id + " fires x" + _mult + " along " + _len.ToString("F1") + " tiles at " + _targets.Count + " targets; furrow force "
                    + _force + " r " + _rr.ToString("F1") + (_deep ? " deep" : "") + (_god ? " (God)" : ""));
            }
        }

        private Particle Beam(int thick, Color c, int order)
        {
            Particle p = C(Kit.Line(_origin, _origin + _dir * Px * 2f, Px, 1f, thick, Layer, order, c));
            if (p != null)
            {
                p.Held = true;
            }
            return p;
        }

        /// <summary>
        /// Takes the final aim when the telegraph goes down (the enemies moved during the charge):
        /// the line with the most of them, or the focus. The wave stops at the map's edge.
        /// </summary>
        private void Aim(Vector2 g)
        {
            _len = Mathf.Min(_len, EdgeDistance(g, _dir));
            List<Actor> foes = Combat.FoesAround(A, g + _dir * _len * 0.5f, _len * 0.5f + 1.5f, false, true);
            if (!_cine && _focus == null && foes.Count > 0)
            {
                Vector2 best = _dir;
                int bestN = Count(foes, g, _dir, _len);
                for (int i = -2; i <= 2; i++)
                {
                    if (i == 0)
                    {
                        continue;
                    }
                    Vector2 d = Crew.Rotate(_dir, i * 10f);
                    float l = Mathf.Min(_len, EdgeDistance(g, d));
                    int n = Count(foes, g, d, l);
                    if (n > bestN)
                    {
                        bestN = n;
                        best = d;
                    }
                }
                _dir = best;
                _len = Mathf.Min(_len, EdgeDistance(g, _dir));
                Pose.FaceRight = Crew.Face(Vector2.zero, _dir, Pose.FaceRight);
            }
            else if (_focus != null && _focus.isAlive() && Combat.IsFoe(A, _focus))
            {
                Vector2 d = _focus.current_position - g;
                if (d.sqrMagnitude > 0.04f)
                {
                    _dir = d.normalized;
                    _len = Mathf.Min(_len, EdgeDistance(g, _dir));
                    Pose.FaceRight = Crew.Face(Vector2.zero, _dir, Pose.FaceRight);
                }
            }
        }

        /// <summary>Who stands in the wave's way when it fires, by distance along it.</summary>
        private void Targets(Vector2 g)
        {
            List<Actor> foes = Combat.FoesAround(A, g + _dir * _len * 0.5f, _len * 0.5f + 1.5f, false, true);
            _targets.Clear();
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            foreach (Actor u in foes)
            {
                Vector2 rel = u.current_position - g;
                float along = Vector2.Dot(rel, _dir);
                if (along < 0.2f || along > _len)
                {
                    continue;
                }
                if (Mathf.Abs(Vector2.Dot(rel, side)) > _halfW + u.stats["size"] * 0.5f)
                {
                    continue;
                }
                _targets.Add(new Target { U = u, Id = u.Sid(), Along = along });
            }
            _targets.Sort((x, y) => x.Along.CompareTo(y.Along));
        }

        private int Count(List<Actor> foes, Vector2 g, Vector2 d, float len)
        {
            Vector2 side = new Vector2(-d.y, d.x);
            int n = 0;
            foreach (Actor u in foes)
            {
                Vector2 rel = u.current_position - g;
                float along = Vector2.Dot(rel, d);
                if (along >= 0.2f && along <= len && Mathf.Abs(Vector2.Dot(rel, side)) <= _halfW + u.stats["size"] * 0.5f)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>How far the wave can run from g along d before leaving the map (with a margin).</summary>
        private static float EdgeDistance(Vector2 g, Vector2 d)
        {
            float t = 99f;
            if (d.x > 0.001f)
            {
                t = Mathf.Min(t, (MapBox.width - 2f - g.x) / d.x);
            }
            else if (d.x < -0.001f)
            {
                t = Mathf.Min(t, (g.x - 1f) / -d.x);
            }
            if (d.y > 0.001f)
            {
                t = Mathf.Min(t, (MapBox.height - 2f - g.y) / d.y);
            }
            else if (d.y < -0.001f)
            {
                t = Mathf.Min(t, (g.y - 1f) / -d.y);
            }
            return Mathf.Max(1f, t);
        }

        /// <summary>Everyone the wave's head has reached is hit and hurled along it.</summary>
        private void HitAlong()
        {
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            foreach (Target t in _targets)
            {
                if (t.Done || t.Along > _head)
                {
                    continue;
                }
                t.Done = true;
                Actor u = t.U;
                if (u == null || u.Sid() != t.Id || !Combat.Living(u) || !Combat.IsFoe(A, u))
                {
                    continue;
                }
                Vector2 at = u.current_position + new Vector2(0f, u.position_height + 0.35f);
                float upx = Crew.PxOf(u);
                if (!Combat.Allowed(A, u, out float cm))
                {
                    // too heavy for this tier: it takes a glancing blow and holds its ground
                    Combat.Hit(A, u, 0.5f, true, true, 0f, this);
                    Combat.Stagger(u, 0.4f);
                    if (_vis)
                    {
                        Fx.HitSpark(at, upx, 1, -_dir, 4);
                    }
                    continue;
                }
                if (Combat.Reach(this, u, true) != Contact.Hit)
                {
                    continue;
                }
                HitResult r = Combat.Hit(A, u, _mult * cm, true, true, 0f, this);
                if (_vis)
                {
                    Fx.HitSpark(at, upx, 2, _dir, 6);
                    // one impact frame per wave: the focus, or the first critical blow (a Black
                    // Flash); a row of them would only black out the beam
                    if (!_impacted && r.Crit && Level >= 2)
                    {
                        _impacted = true;
                        Kit.ImpactFrame(at, upx, 0.8f, _pal, true);
                    }
                    else if (!_impacted && u == _focus)
                    {
                        _impacted = true;
                        Kit.ImpactFrame(at, upx, 1f, _pal, false);
                    }
                }
                if (u.isAlive())
                {
                    float off = Vector2.Dot(u.current_position - _from, side);
                    Vector2 push = (_dir + side * Mathf.Clamp(off, -1f, 1f) * 0.25f).normalized;
                    Combat.Launch(u, u.current_position - push * 0.5f, Random.Range(3f, 4f), Random.Range(2f, 2.6f), Random.value < 0.5f ? -720f : 720f);
                }
                if (Dev.On)
                {
                    Dev.Trace("unison " + Id + " hits " + u.asset.id + " x" + (_mult * cm) + " hp " + u.getHealth() + (r.Crit ? " crit" : ""));
                }
            }
            if (_hitsDone < _targets.Count)
            {
                int n = 0;
                foreach (Target t in _targets)
                {
                    if (t.Done)
                    {
                        n++;
                    }
                }
                _hitsDone = n;
                if (n >= 2)
                {
                    EndLink = Link.Scattered;
                    EndTarget = _focus;
                }
            }
        }

        private int _hitsDone;
        private bool _impacted;

        /// <summary>The ground under the wave is torn open as its head passes (dust; II scorched; III glowing).</summary>
        private void Furrow()
        {
            int upTo = Mathf.FloorToInt(_head);
            float px = Px;
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            Color glow = _braid[0];
            while (_furrow < upTo)
            {
                int i = _furrow++;
                Vector2 a = _from + _dir * (i + 0.6f);
                Vector2 b = a + _dir;
                Vector2 mid = (a + b) * 0.5f;
                WorldTile tile = Combat.TileAt(mid);
                bool water = tile != null && tile.Type != null && tile.Type.liquid;
                if (!water)
                {
                    Tear(i, mid);
                }
                if (!Kit.Visible(mid))
                {
                    continue;
                }
                if (water)
                {
                    if ((i & 1) == 0)
                    {
                        Kit.Splash(mid, px, tile, 0.8f);
                    }
                    continue;
                }
                Color dust = Fx.Lighten(Fx.SoilColor(tile), 0.35f);
                // dust thrown out to both sides
                int n = Fx.Count(2);
                for (int j = 0; j < n; j++)
                {
                    float s = j % 2 == 0 ? 1f : -1f;
                    Particle p = Fx.Spawn(Art.Dust[Random.Range(1, 4)], mid + side * s * _halfW * 0.6f, px, Random.Range(0.5f, 0.8f), FxLayer.Top, 6);
                    p?.Moving((side * s * Random.Range(30f, 55f) + _dir * 15f) * px, Random.Range(4f, 9f) * px, 2.6f).Sized(0.5f, 1.2f)
                        .Color(Fx.WithA(dust, 0.8f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
                }
                if (Level <= 1)
                {
                    continue;
                }
                // the scorched groove: laid in two half-tile pieces of uneven width, each nudged a pixel
                // off the line, so it reads as torn ground and not as a plank; it fades in a few seconds
                Color soil = Fx.Darken(Fx.SoilColor(tile), 0.45f);
                int wide = Mathf.Clamp(Mathf.RoundToInt(_halfW / px * 0.45f), 2, 4);
                for (int h = 0; h < 2; h++)
                {
                    Vector2 p0 = Vector2.Lerp(a, b, h * 0.5f) + side * Random.Range(-1f, 1f) * px;
                    Vector2 p1 = Vector2.Lerp(a, b, h * 0.5f + 0.5f) + side * Random.Range(-1f, 1f) * px;
                    int th = Mathf.Max(1, wide - Random.Range(0, 2));
                    Particle scorch = Kit.Line(p0, p1, px, Level >= 3 ? 3.6f : 2.8f, th, FxLayer.Ground, 2,
                        Fx.WithA(soil, 0.7f), Fx.WithA(soil, 0.5f), Fx.WithA(soil, 0f));
                    if (scorch != null)
                    {
                        scorch.Snap = true;
                    }
                }
                if (Level >= 3)
                {
                    // a white-hot seam down the middle, cooling to dark
                    Particle core = Kit.Line(a, b, px, 2.2f, 1, FxLayer.Ground, 3,
                        new Color(1f, 0.97f, 0.8f, 1f), Fx.WithA(Color.Lerp(Kit.Fire1, glow, 0.5f), 0.95f), Fx.WithA(Color.Lerp(Kit.Fire3, glow, 0.3f), 0.55f),
                        new Color(0.25f, 0.05f, 0.03f, 0.2f), new Color(0.2f, 0.03f, 0.02f, 0f));
                    if (core != null)
                    {
                        core.Snap = true;
                    }
                    if (Random.value < 0.5f)
                    {
                        Kit.Ember(mid + side * Random.Range(-1f, 1f) * _halfW * 0.5f, px, Random.Range(0.8f, 1.4f),
                            new Vector2(Random.Range(-5f, 5f), Random.Range(14f, 28f)) * px, Random.value < 0.4f ? glow : (Color?)null);
                    }
                }
                if (i % 3 == 1)
                {
                    Kit.Crater(mid, px * 0.5f, tile, Level >= 3, Level >= 3 ? 4f : 3f, true, glow);
                    Kit.Debris(mid, px, tile, 3);
                }
            }
        }

        /// <summary>
        /// Real damage to the ground as the wave's head passes, on screen or not: a blast every
        /// furrow-radius tiles down its path (clear of the leader), a second narrow one down the
        /// middle when seven or more poured in. A handful of blasts over the wave's 0.13 s run.
        /// The furrow itself is torn open without flames (each blast's burning rim lay inside the
        /// next ones: the whole strip went up and hid the trench); from force 3 its two banks
        /// catch instead (a God's thickly): a dark scar with burning edges.
        /// </summary>
        private void Tear(int i, Vector2 mid)
        {
            if (_force <= 0 || i + 1.1f < _rr + 0.6f)
            {
                return;
            }
            int step = Mathf.Max(1, Mathf.RoundToInt(_rr));
            if (i % step != 0)
            {
                return;
            }
            _spare ??= Spare();
            Crew.Ravage(mid, _rr, _force, A, _spare, false);
            if (_deep)
            {
                Crew.Ravage(mid, 1f, _force, A, _spare, false);
            }
            if (_force >= 3)
            {
                Vector2 half = _dir * (step * 0.5f);
                Crew.Banks(mid - half, mid + half, Bank, _force >= 4 ? 0.6f : _deep ? 0.45f : 0.36f, A, _spare);
            }
        }

        /// <summary>The wave's far end: a burst where it spends itself (and a crater it blasts there).</summary>
        private void EndBurst()
        {
            Vector2 end = _origin + _dir * _len;
            Crew.Blast(end, _rr + 0.6f, _force, A, _spare ??= Spare());
            if (!Kit.Visible(end))
            {
                return;
            }
            float px = Px;
            Particle f = C(Kit.Flash(end, px, Mathf.RoundToInt(10 * K), 0.14f, Color.white, Layer));
            f?.Color(Color.white, Fx.WithA(_mix, 0.8f), Fx.WithA(_mix, 0f));
            C(Fx.Ring(end, px, 0.3f, 4f, 26f * K, 2, 1, Layer, 3, 1f, 2.5f, Color.white, Fx.WithA(_mix, 0.85f), Fx.WithA(_mix, 0f)));
            Kit.Sparks(end, px, 10, 60f, 140f, _braid[Random.Range(0, _braid.Count)]);
        }

        /// <summary>The wave itself: core, braided band and glow, strands spiralling along it, crackles.</summary>
        private void Draw(float alpha, float dt)
        {
            Vector2 head = _origin + _dir * Mathf.Max(_head, Px * 2f);
            float pulse = 1f + 0.12f * Mathf.Sin(T * 48f);
            int thick = Mathf.Max(3, Mathf.RoundToInt(2f * _halfW / Px));
            Lay(_bOuter, head, thick * pulse * alpha, Fx.WithA(_cOuter, _cOuter.a * alpha));
            Lay(_bInner, head, Mathf.Max(2f, thick * 0.55f * pulse) * alpha, Fx.WithA(_cInner, _cInner.a * alpha));
            Lay(_bCore, head, Mathf.Max(1f, thick * (Level >= 3 ? 0.26f : 0.18f)) * Mathf.Max(0.3f, alpha), Fx.WithA(Color.white, Mathf.Clamp01(alpha * 1.4f)));
            if (_bHead != null && _bHead.Alive)
            {
                _bHead.Pos = head;
                _bHead.R.color = Fx.WithA(Color.white, alpha);
                _bHead.Grad = null;
                _bHead.Size0 = _bHead.Size1 = Mathf.Max(0.3f, alpha) * pulse;
            }
            if (!_vis || alpha < 0.3f)
            {
                return;
            }
            // strands spiralling along the wave, one per colour
            _strand -= dt;
            if (_strand <= 0f)
            {
                _strand = 0.016f / Fx.Detail();
                Vector2 side = new Vector2(-_dir.y, _dir.x);
                for (int i = 0; i < _braid.Count; i++)
                {
                    Color c = _braid[i];
                    float ph = i * Mathf.PI * 2f / _braid.Count;
                    float amp = _halfW * 0.75f;
                    Particle p = C(Fx.Spawn(Art.Pixel, _origin, Px, 0.22f, Layer, 4));
                    if (p == null)
                    {
                        break;
                    }
                    float t0 = T;
                    p.Sized(Level >= 2 ? 2f : 1f, 1f).Color(Color.white, c, Fx.WithA(c, 0f));
                    p.Step = (q, d) =>
                    {
                        float along = q.K * _head;
                        q.Pos = _origin + _dir * along + side * amp * Mathf.Sin(ph + along * 1.6f - t0 * 14f);
                    };
                }
            }
            // crackles at the edges
            _ring -= dt;
            if (_ring <= 0f)
            {
                _ring = 0.04f;
                Vector2 side = new Vector2(-_dir.y, _dir.x);
                float along = Random.Range(0.5f, Mathf.Max(0.6f, _head));
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector2 a = _origin + _dir * along + side * s * _halfW;
                Vector2 b = a + (_dir * Random.Range(0.2f, 0.6f) + side * s * Random.Range(0.1f, 0.4f));
                Color c = _braid[Random.Range(0, _braid.Count)];
                C(Kit.Line(a, b, Px, 0.05f, 1, Layer, 6, Color.white, Fx.WithA(c, 0f)));
            }
        }

        /// <summary>Lays one band of the wave from the hands to its head, 'thick' whole pixels wide.</summary>
        private void Lay(Particle p, Vector2 head, float thick, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            Vector2 d = head - _origin;
            float len = Mathf.Max(p.Px, d.magnitude);
            p.Pos = (_origin + head) * 0.5f;
            p.SX = len / p.Px;
            p.SY = Mathf.Max(1f, Mathf.Round(thick));
            p.Rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            p.Color(c);
        }

        private void FadePhase(float dt)
        {
            float k = Mathf.Clamp01(_pt / TFade);
            if (Enter())
            {
                _lit.Color = new Color(1f, 1f, 1f, 0f);
                foreach (Member mb in _members)
                {
                    if (!mb.Dead)
                    {
                        mb.P.Lit = false;
                    }
                }
            }
            _origin = Hands();
            Draw(1f - k, dt);
            Pose.Rot = Mathf.Lerp(16f, 6f, k);
            Pose.Nudge = Vector2.zero;
            _glow.Color = Fx.WithA(_mix, 0.35f * (1f - k));
            foreach (Member mb in _members)
            {
                if (mb.Dead)
                {
                    continue;
                }
                Move m = mb.P;
                float c = Anim.OutQuad(Mathf.Clamp01(_pt / 0.12f));
                if (Crew.Flow(mb.U))
                {
                    // a master isn't spent: it straightens up, ready to go
                    m.Pose.SX = Mathf.Lerp(1.06f, 1f, c);
                    m.Pose.SY = Mathf.Lerp(0.94f, 1f, c);
                    m.Pose.Rot = Mathf.Lerp(12f, 0f, c);
                }
                else
                {
                    // spent: down on one knee
                    m.Pose.SX = Mathf.Lerp(1.06f, 1.1f, c);
                    m.Pose.SY = Mathf.Lerp(0.94f, 0.84f, c);
                    m.Pose.Rot = Mathf.Lerp(12f, 5f, c);
                }
                mb.P.GlowColor = Fx.WithA(mb.Col, 0.15f * (1f - k));
            }
            if (k >= 1f)
            {
                KillBeam();
                // the masters among the partners are let go as the wave dies and fight straight on
                // (into the scattered enemies); the others get up with the leader
                foreach (Member mb in _members)
                {
                    if (!mb.Dead && Crew.Flow(mb.U))
                    {
                        mb.Dead = true;
                        mb.P.Pose.SX = mb.P.Pose.SY = 1f;
                        mb.P.Pose.Rot = 0f;
                        mb.P.Release(mb.P.Pose.Height <= 0.02f);
                        Director.Release(mb.U);   // out of the set piece: free to fight on now
                        Crew.Rest(mb.U);
                        if (Dev.On)
                        {
                            Dev.Trace("unison " + Id + " lets " + mb.U.asset.id + "#" + mb.U.Sid() + " (" + Tiers.Of(mb.U) + ") go at the fade");
                        }
                    }
                }
                Next(Phase.Recover);
            }
        }

        private void Recover()
        {
            if (Enter())
            {
                if (_vis)
                {
                    float fwd = Pose.FaceRight ? 1f : -1f;
                    Fx.Glint(BodyPos() + new Vector2(fwd * 3f * Px, Pose.PivotUp * 1.4f * Px), Px, 0.26f, Level >= 2 ? _pal.Core : Color.white, 1f);
                }
            }
            // the same spring, played faster by a master (it settles within its shorter recovery)
            float st = _pt / Crew.Pace(A);
            float amp = Anim.Spring(st, 8f, 16f);
            Pose.SX = 1f + 0.14f * amp;
            Pose.SY = 1f - 0.16f * amp;
            Pose.Rot = 6f * Mathf.Exp(-10f * st);
            _glow.Color = new Color(1f, 1f, 1f, 0f);
            foreach (Member mb in _members)
            {
                if (mb.Dead)
                {
                    continue;
                }
                Move m = mb.P;
                float k = Anim.OutQuad(Mathf.Clamp01(_pt / Mathf.Min(0.2f, TRecover)));
                m.Pose.SX = Mathf.Lerp(1.1f, 1f, k);
                m.Pose.SY = Mathf.Lerp(0.84f, 1f, k);
                m.Pose.Rot = Mathf.Lerp(5f, 0f, k);
                mb.P.GlowColor = new Color(1f, 1f, 1f, 0f);
            }
            if (_pt >= TRecover)
            {
                End();
            }
        }

        private void End()
        {
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Nudge = Vector2.zero;
            Crew.Breathe(A);
            ReleaseAll();
            Finished = true;
        }

        // ------------------------------------------------------------ the ball --

        /// <summary>The ball of everyone's power above the leader: grows with each arrival, pulses, spins.</summary>
        private void Ball(float dt)
        {
            if (_phase != Phase.Charge && _phase != Phase.Windup)
            {
                return;
            }
            if (_ballBody == null && _vis && _arrived > 0)
            {
                _ballGlow = Held(Art.Disk(4, 1f, 0.5f), 4, Fx.WithA(_mix, 0.5f));
                _ballBody = Held(Art.Disk(3, 1f, 0f), 5, _braid[0]);
                _ballCore = Held(Art.Disk(1, 1f, 0f), 6, Color.white);
                _ballSwirl = Held(Art.Arc(5, 1f, 0f, 260f, 1)[3], 7, Fx.WithA(Color.white, 0.85f));
            }
            if (_ballBody == null)
            {
                return;
            }
            int r = BallR() + ((Time.frameCount & 2) == 0 ? 0 : 1);
            Vector2 at = BallPos() + new Vector2(0f, 0.4f * Px * Mathf.Sin(T * 16f));
            Color body = _braid[(int)(T * 10f) % _braid.Count];
            Set(_ballGlow, at, Art.Disk(r + 2, 1f, 0.5f), Fx.WithA(_mix, 0.55f));
            Set(_ballBody, at, Art.Disk(r, 1f, 0f), Color.Lerp(body, Color.white, _phase == Phase.Windup ? 0.6f : 0.15f));
            Set(_ballCore, at, Art.Disk(Mathf.Max(1, r - 2), 1f, 0f), Color.white);
            if (_ballSwirl != null && _ballSwirl.Alive)
            {
                _ballSwirl.Pos = at;
                _ballSwirl.Rot += 720f * dt;
                _ballSwirl.Size0 = _ballSwirl.Size1 = Mathf.Max(0.5f, (r + 1) / 5f);
            }
        }

        private Particle Held(Sprite s, int order, Color c)
        {
            Particle p = C(Fx.Spawn(s, BallPos(), Px, 1f, Layer, order));
            if (p != null)
            {
                p.Held = true;
                p.Color(c);
            }
            return p;
        }

        private static void Set(Particle p, Vector2 at, Sprite s, Color c)
        {
            if (p == null || !p.Alive)
            {
                return;
            }
            p.Pos = at;
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Color(c);
        }

        private void KillBall()
        {
            _ballGlow?.Kill();
            _ballBody?.Kill();
            _ballCore?.Kill();
            _ballSwirl?.Kill();
            _ballGlow = _ballBody = _ballCore = _ballSwirl = null;
        }

        private void KillBeam()
        {
            _bOuter?.Kill();
            _bInner?.Kill();
            _bCore?.Kill();
            _bHead?.Kill();
            _bOuter = _bInner = _bCore = _bHead = null;
        }

        private string TitleText()
        {
            string s = Locale.Get("trait_sakuga_unison_burst");
            return string.IsNullOrEmpty(s) || s.StartsWith("trait_") ? "UNISON BURST" : s.ToUpperInvariant();
        }

        // ------------------------------------------------------------ cleanup --

        private void ReleaseAll()
        {
            foreach (Member mb in _members)
            {
                if (mb.P != null && !mb.P.Finished)
                {
                    mb.P.Release(mb.P.Pose.Height <= 0.02f);
                }
            }
        }

        public override void Frame()
        {
            _glow.Frame();
            _lit.Frame();
        }

        public override void Abort()
        {
            KillBall();
            KillBeam();
            _glow.Kill();
            _lit.Kill();
            ReleaseAll();
            if (_cine)
            {
                if (_phase < Phase.Beam)
                {
                    Cinema.ClearTitle();   // (the world is held from the gathering on: any title up is this one's)
                }
                Cinema.End(this);
                _held = false;
            }
        }

        public override void OnEnd(bool aborted)
        {
            Abort();
            Director.Release(this);
            foreach (Member mb in _members)
            {
                Crew.Rest(mb.U);
            }
        }
    }

    /// <summary>
    /// Unison Burst as a technique. Entry: Expert. The AI calls for it when three or more tiered
    /// fighters (Experts and up at level I, any tier from level II) are free within 7 tiles and
    /// either four or more enemies stand in a line or one enemy is at least twice as strong as
    /// the leader. Every partner pays its share.
    /// </summary>
    internal sealed class UnisonBurstTech : Technique
    {
        private static readonly float[] Shares = { 20f, 30f, 45f };
        private static readonly List<Actor> _ray = new List<Actor>();

        public override string Id => "unison_burst";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Team;

        /// <summary>Allies pour their power into the leader, who charges it (break it!) and lets go one giant wave.</summary>
        public override Form Forms => Form.Ki | Form.Beam | Form.Charge | Form.Line | Form.Team | Form.Heavy;

        public override Scale ScaleAt(int level) => level <= 1 ? Scale.Heavy : level == 2 ? Scale.Heavy : Scale.Ultimate;

        public override float[] ManaCost => new[] { 40f, 60f, 90f };
        public override float[] Cooldown => new[] { 30f, 30f, 30f };
        public override string Icon => "ui/Icons/sakuga/iconUnisonBurst";

        public override Plan Plan(Ctx c, int level)
        {
            if (!Crew.MayLead(c))
            {
                return null;
            }
            float share = Shares[Mathf.Clamp(level - 1, 0, 2)];
            List<Actor> partners = null;
            for (int i = 0; i < c.Allies.Count; i++)
            {
                Actor u = c.Allies[i];
                if ((u.current_position - c.Pos).sqrMagnitude > 49f)
                {
                    break;
                }
                Tier t = Tiers.Of(u);
                bool tierOk = t != Tier.None ? (level >= 2 || t >= Tier.Expert || c.Forced) : c.Forced && level >= 2;
                if (!tierOk || !Crew.Free(c.Self, u) || (!c.Forced && !Crew.CanPay(u, share)))
                {
                    continue;
                }
                (partners ??= new List<Actor>(9)).Add(u);
            }
            if (partners == null || partners.Count < (c.Forced ? 1 : 2))
            {
                return null;
            }
            // the strongest join first
            if (partners.Count > 9)
            {
                partners.Sort((x, y) => Crew.Rank(y).CompareTo(Crew.Rank(x)));
                partners.RemoveRange(9, partners.Count - 9);
            }
            int n = partners.Count;
            float k = Mathf.Min(Crew.KOf(c.Self), 1.6f);
            float len = Mathf.Clamp(10f + 0.75f * n, 10f, 16f);
            float halfW = 0.5f * (1f + 0.06f * (n + 1)) * k;
            Vector2 dir = c.BestRay(len, halfW, out int count);
            Actor focus = null;
            if (count < 4 || c.Forced)
            {
                Actor big = c.Biggest(len);
                if (big != null && Combat.Grounded(big) && Tiers.Power(big) >= 2f * Tiers.Power(c.Self))
                {
                    focus = big;
                    dir = (big.current_position - c.Pos).normalized;
                    count = Mathf.Max(1, c.OnRay(dir, len, halfW));
                }
                else if (c.Forced)
                {
                    Actor t = c.ForceTarget ?? c.Target ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                    Vector2 to = t != null ? t.current_position : c.ForcePoint ?? c.Pos + dir;
                    if ((to - c.Pos).sqrMagnitude < 0.01f)
                    {
                        return null;
                    }
                    if (count < 1)
                    {
                        dir = (to - c.Pos).normalized;
                    }
                }
                else if (count < 4)
                {
                    return null;
                }
            }
            if (dir.sqrMagnitude < 0.0001f)
            {
                return null;
            }
            float mult = UnisonBurst.WaveMult(level, n);
            _ray.Clear();
            c.OnRay(dir, len, halfW, _ray);
            float score = 0f;
            foreach (Actor u in _ray)
            {
                c.Allowed(u, out float cm);
                score += c.Value(u, c.Expected(u, mult), cm);
            }
            if (focus != null && !_ray.Contains(focus))
            {
                c.Allowed(focus, out float cm);
                score += c.Value(focus, c.Expected(focus, mult), cm);
            }
            score += 40f + 10f * n;
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = focus ?? (_ray.Count > 0 ? _ray[0] : null),
                Dir = dir,
                Point = c.Pos + dir * len * 0.5f,
                Count = Mathf.Max(count, _ray.Count),
                Score = score,
                Others = partners,
                Data = c.Forced,
                AreaRadius = len * 0.5f
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (p.Others == null)
            {
                return false;
            }
            bool forced = p.Data is bool b && b;
            float share = Shares[Mathf.Clamp(p.Level - 1, 0, 2)];
            List<Actor> partners = new List<Actor>();
            foreach (Actor u in p.Others)
            {
                if (Crew.Free(a, u) && (forced || Crew.CanPay(u, share)) && Vector2.Distance(u.current_position, a.current_position) <= 8f)
                {
                    partners.Add(u);
                }
            }
            Actor focus = p.Target != null && Tiers.Power(p.Target) >= 2f * Tiers.Power(a) ? p.Target : null;
            // (as many as the plan needed: two pouring in besides the leader, unless the player ordered it)
            return partners.Count >= (forced ? 1 : 2) && UnisonBurst.Begin(this, a, partners, p.Level, p.Dir, focus, forced, share);
        }
    }
}
