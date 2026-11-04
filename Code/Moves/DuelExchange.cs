using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>How a clash between two duelists came out (the duel's clash rule).</summary>
    internal enum ClashKind
    {
        /// <summary>Power ratio under 1.25: both knocked back, sparks, no damage.</summary>
        Even,
        /// <summary>1.25 to 3: the winner hits through at half strength, the loser staggers.</summary>
        Win,
        /// <summary>3 or more: a one-handed ting and the loser is flicked away.</summary>
        Overwhelm
    }

    internal struct Clash
    {
        public ClashKind Kind;
        /// <summary>A (the duel's first fighter) came out on top (meaningless when even).</summary>
        public bool AWins;
        public float Ratio;

        public bool Even => Kind == ClashKind.Even;
    }

    /// <summary>
    /// How one fighter is thrown off a clash: a skid along the ground (leaning back, then a wobble
    /// while it finds its feet), a flight (an arc with a flip, landing on safe ground), or the
    /// winner's follow-through. Destinations are worked out safe before it starts (DuelGeo).
    /// </summary>
    internal struct Knock
    {
        public Vector2 From, To;
        public float Dur, Apex, Spin, Lean, Wobble, T;
        public bool Air, Active, Landed;
        /// <summary>Dust trail timer, and whether its landing puff / skid marks were left.</summary>
        public float DustT;
        public bool Marked;

        public static Knock Skid(Vector2 from, Vector2 to, float dur, float lean, float wobble)
        {
            return new Knock { From = from, To = to, Dur = dur, Lean = lean, Wobble = wobble, Active = true };
        }

        public static Knock Fly(Vector2 from, Vector2 to, float dur, float apex, float spin, float wobble)
        {
            return new Knock { From = from, To = to, Dur = dur, Apex = apex, Spin = spin, Wobble = wobble, Air = true, Active = true };
        }

        public bool Moving => Active && T < Dur && (To - From).sqrMagnitude > 0.0004f;

        /// <summary>Poses m for this frame; true once it is over (landed and steady).</summary>
        public bool Step(Duelist m, float dt)
        {
            if (!Active)
            {
                return true;
            }
            T += dt;
            float k = Mathf.Clamp01(T / Mathf.Max(0.01f, Dur));
            if (Air)
            {
                if (k < 1f)
                {
                    m.Pose.Ground = Vector2.Lerp(From, To, k);
                    m.Pose.Height = Apex * 4f * k * (1f - k);
                    m.Pose.Rot = Spin * Anim.InOutSine(k);
                    float s = Mathf.Sin(Mathf.PI * k);
                    m.Pose.SX = 1f - 0.06f * s;
                    m.Pose.SY = 1f - 0.1f * s;
                }
                else
                {
                    // landed: a crouch that springs back up
                    m.Pose.Ground = To;
                    m.Pose.Height = 0f;
                    m.Pose.Rot = 0f;
                    float amp = Anim.Spring(T - Dur, 8f, 16f);
                    m.Pose.SX = 1f + 0.16f * amp;
                    m.Pose.SY = 1f - 0.2f * amp;
                }
            }
            else
            {
                float e = Anim.OutCubic(k);
                m.Pose.Ground = Vector2.Lerp(From, To, e);
                m.Pose.Height = 0f;
                if (k < 1f)
                {
                    m.Pose.Rot = Lean * (1f - 0.6f * e);
                    m.Pose.SX = 1f + 0.06f * (1f - e);
                    m.Pose.SY = 1f - 0.08f * (1f - e);
                }
                else
                {
                    // finding its feet: the lean springs back through upright
                    float w = T - Dur;
                    m.Pose.Rot = Lean * 0.4f * Anim.Spring(w, Wobble > 0.3f ? 4f : 8f, 14f);
                    float amp = Anim.Spring(w, 9f, 18f);
                    m.Pose.SX = 1f + 0.05f * amp;
                    m.Pose.SY = 1f - 0.06f * amp;
                }
            }
            if (k >= 1f && !Landed)
            {
                Landed = true;
            }
            return T >= Dur + Wobble;
        }
    }

    /// <summary>
    /// One exchange of a duel: a short choreography (0.8-2.5 s) that poses both duelists every
    /// tick. The Duel picks it, Plans it (where everything happens, from where they stand, all on
    /// safe ground), then steps it until Done. Clashes inside it go through the duel's clash rule
    /// (Duel.Resolve), so the same exchange plays out differently every time.
    ///
    /// Shared craft lives here: phase timing, the local hit-stop (both hold their poses and
    /// tremble), facing, guard and dash poses, afterimage trails, the meeting of two blows, the
    /// aftermath of a decisive clash (skids, the hit that goes through, the ting and the flick).
    /// </summary>
    internal abstract class Exchange
    {
        public Duel D;
        /// <summary>A's and B's duelists (Q is null for a fighter winding down alone).</summary>
        public Duelist P, Q;
        public float T, Pt;
        public bool Done;
        protected int Ph;
        private bool _entered;
        /// <summary>Local hit-stop: both hold their poses, trembling against each other.</summary>
        protected float Hold;
        /// <summary>Where they stood when it began.</summary>
        protected Vector2 A0, B0;
        protected Knock KA, KB;
        protected float GhostA, GhostB;
        /// <summary>
        /// Who came out on top, and the chain link it leaves the Brain onto the other (Close: right
        /// there; Down: knocked into the ground): when the bout ends on it, its technique follows at once.
        /// </summary>
        public Duelist Upper;
        public Link Opening;

        public abstract string Id { get; }

        /// <summary>
        /// What kind of fighting it is (both duelists show it while it plays: Duelist.Forms): the
        /// fighters read it and remember it like any attack, and their personas weigh the pick
        /// (a brawler likes a blade lock, a trickster a feint, an out-fighter the chase).
        /// </summary>
        public virtual Form Forms => Form.None;

        /// <summary>A fight (true) or a breath between fights (Circle, Stare-down).</summary>
        public virtual bool Intense => true;

        /// <summary>May it play on with one fighter gone (the Final's winner after the killing blow)?</summary>
        public virtual bool Solo => false;

        /// <summary>How fitting it is right now (0: not at all). The pool picks by these weights.</summary>
        public virtual float Weight()
        {
            return 1f;
        }

        /// <summary>Works out where everything happens from where the two stand now. False: not possible here.</summary>
        public abstract bool Plan();

        /// <summary>The duelists exist now (P, Q set).</summary>
        public virtual void Start()
        {
        }

        protected abstract void Tick(float dt);

        /// <summary>Drop whatever it holds (the duel broke off).</summary>
        public virtual void Abort()
        {
        }

        public void Bind(Duel d)
        {
            D = d;
            A0 = d.PosA;
            B0 = d.PosB;
        }

        /// <summary>Where A ('a') or B stood when it began.</summary>
        public Vector2 Home(bool a)
        {
            return a ? A0 : B0;
        }

        public void Step(float dt)
        {
            if (Hold > 0f)
            {
                Hold -= dt;
                float j = ((int)(Hold * 110f) & 1) == 0 ? 0.5f : -0.5f;
                if (P != null)
                {
                    P.Pose.Nudge = new Vector2(j, 0f);
                }
                if (Q != null)
                {
                    Q.Pose.Nudge = new Vector2(-j, 0f);
                }
                return;
            }
            if (P != null)
            {
                P.Pose.Nudge = Vector2.zero;
            }
            if (Q != null)
            {
                Q.Pose.Nudge = Vector2.zero;
            }
            T += dt;
            Pt += dt;
            Tick(dt);
            // the game's shadow follows this frame's height (it is drawn before the duelist's own Frame)
            P?.Shade();
            Q?.Shade();
        }

        protected void Next(int ph)
        {
            Ph = ph;
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

        // ------------------------------------------------------------ basics --

        protected float K => D.K;

        protected float Px => D.Px;

        /// <summary>The duel's level (I Adepts, II Experts, III Masters and up): the visual grammar.</summary>
        protected int L => D.L;

        protected bool Vis => D.Visible;

        /// <summary>How long the rests of an exchange last (settling, finding the feet, breathers): Masters and up barely stop.</summary>
        protected float Brisk => D.Brisk;

        /// <summary>A rest of 'seconds' (for an Adept), as long as this pair takes it.</summary>
        protected float Rest(float seconds)
        {
            return Mathf.Max(0.09f, seconds * Brisk);
        }

        /// <summary>One of three by the duel's level (I, II, III).</summary>
        protected int ByLevel(int one, int two, int three)
        {
            return L >= 3 ? three : L == 2 ? two : one;
        }

        /// <summary>The world takes the blow (Duel.Wreck: a God's one force harder and wider). 'by': whose blow it is (null: an even clash).</summary>
        protected void Wreck(Vector2 at, float radiusTiles, int force, Duelist by, bool fire = true)
        {
            D.Wreck(at, radiusTiles, force, by != null ? by.A : null, fire);
        }

        /// <summary>Who came out on top and how the other was left (the link the Brain may chain from).</summary>
        protected void Opens(Duelist w, Link link)
        {
            Upper = w;
            Opening = w != null ? link : Link.None;
        }

        /// <summary>A local hit-stop, from level II up (a level I fight never stops).</summary>
        protected void Stop(float seconds)
        {
            if (L >= 2)
            {
                Hold = Mathf.Max(Hold, seconds);
            }
        }

        protected static void Face(Duelist m, Vector2 at)
        {
            float dx = at.x - m.Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.03f)
            {
                m.Pose.FaceRight = dx > 0f;
            }
        }

        protected void FaceEach()
        {
            Face(P, Q.Pose.Ground);
            Face(Q, P.Pose.Ground);
        }

        /// <summary>Guard stance: knees bent, weight forward, breathing.</summary>
        protected static void Guard(Duelist m, float t, float phase, float depth = 1f)
        {
            float b = Mathf.Sin(t * 5.5f + phase);
            m.Pose.SX = 1f + 0.04f * depth;
            m.Pose.SY = 1f - 0.05f * depth + 0.015f * b;
            m.Pose.Rot = 5f * depth;
            m.Pose.Height = 0f;
        }

        /// <summary>Settling back into guard after a beat: a springy crouch that fades.</summary>
        protected static void Settle(Duelist m, float t, float phase)
        {
            Guard(m, t, phase);
            float amp = Anim.Spring(t, 9f, 17f);
            m.Pose.SX += 0.1f * amp;
            m.Pose.SY -= 0.12f * amp;
        }

        /// <summary>
        /// A ground dash at progress k: the first fifth a push-off crouch (anticipation), then the
        /// burst, smeared along its facing and leaning in, easing out as it arrives.
        /// </summary>
        protected static void DashPose(Duelist m, Vector2 from, Vector2 to, float k)
        {
            k = Mathf.Clamp01(k);
            m.Pose.Height = 0f;
            if ((to - from).sqrMagnitude < 0.01f)
            {
                m.Pose.Ground = to;
                m.Pose.SX = 1f;
                m.Pose.SY = 1f;
                m.Pose.Rot = 4f;
                return;
            }
            if (k < 0.2f)
            {
                float c = Anim.OutQuad(k / 0.2f);
                m.Pose.Ground = from;
                m.Pose.SX = 1f + 0.12f * c;
                m.Pose.SY = 1f - 0.12f * c;
                m.Pose.Rot = -4f * c;
                return;
            }
            float u = (k - 0.2f) / 0.8f;
            m.Pose.Ground = Vector2.Lerp(from, to, Anim.OutCubic(u));
            float s = Mathf.Sin(Mathf.PI * u);
            m.Pose.SX = 1f + 0.3f * s;
            m.Pose.SY = 1f - 0.18f * s;
            m.Pose.Rot = 4f + 14f * s;
        }

        /// <summary>
        /// Getting into place: a dash when it closes in on the other ('other': where the other is
        /// going), a quick hop back, leaning away, when it gives ground to make room.
        /// </summary>
        protected static void Reposition(Duelist m, Vector2 from, Vector2 to, float k, Vector2 other)
        {
            Vector2 d = to - from;
            if (d.sqrMagnitude < 0.01f || Vector2.Dot(d, other - from) >= 0f)
            {
                DashPose(m, from, to, k);
                return;
            }
            k = Mathf.Clamp01(k);
            float s = Mathf.Sin(Mathf.PI * k);
            m.Pose.Ground = Vector2.Lerp(from, to, Anim.OutQuad(k));
            m.Pose.Height = 0.12f * m.K * s * Mathf.Min(1f, d.magnitude);
            m.Pose.Rot = -12f * s;
            m.Pose.SX = 1f - 0.05f * s;
            m.Pose.SY = 1f + 0.06f * s;
        }

        /// <summary>Afterimages in the fighter's own colours, one every 'every' seconds.</summary>
        protected void Trail(Duelist m, ref float timer, float dt, float every, float life, float alpha = 1f)
        {
            if (!Vis || m.Pose.Hidden)
            {
                return;
            }
            timer -= dt;
            if (timer > 0f)
            {
                return;
            }
            timer = every;
            Fx.Afterimage(m, life, Fx.WithA(m.Pal.Ghost, m.Pal.Ghost.a * alpha), m.Pal.GhostEnd);
        }

        /// <summary>Between their bodies (where blows meet).</summary>
        protected Vector2 Contact()
        {
            return (P.BodyPos() + Q.BodyPos()) * 0.5f;
        }

        /// <summary>From A toward B along the ground.</summary>
        protected Vector2 Axis()
        {
            Vector2 d = Q.Pose.Ground - P.Pose.Ground;
            return d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(P.Fwd, 0f);
        }

        protected Duelist Winner(Clash c)
        {
            return c.AWins ? P : Q;
        }

        protected Duelist Loser(Clash c)
        {
            return c.AWins ? Q : P;
        }

        /// <summary>Is the fight bare-handed (fists throw grit, blades throw sparks)?</summary>
        protected bool Metal => P.Armed || Q.Armed;

        /// <summary>The crescent of a swing, in the fighter's colours, glued to its body.</summary>
        protected void Slash(Duelist m, int kind, float life)
        {
            if (!Vis)
            {
                return;
            }
            int r = m.Armed ? 10 : 7;
            Sprite[] arc = kind == 0 ? Art.Arc(r, 0.88f, 130f, -50f, 3)
                : kind == 1 ? Art.Arc(r, 0.88f, -130f, 50f, 3)
                : Art.Arc(r + 1, 0.42f, 170f, -20f, 3);
            Kit.Swish(m, arc, life, new Vector2(m.Fwd * 3f, 0f), m.Fwd, m.Pal.Core, m.Pal.Edge);
        }

        /// <summary>
        /// Two blows meeting at 'at': a starburst 'tiles' in radius in both fighters' colours (the
        /// winner's alone when one side won), sparks spraying out of the bind, focus lines (II+),
        /// the clang.
        /// </summary>
        protected void Meet(Vector2 at, float tiles, Clash c, int sparks)
        {
            D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            if (!Vis)
            {
                return;
            }
            Color ea = D.PA.Edge, eb = D.PB.Edge;
            if (!c.Even)
            {
                ea = eb = c.AWins ? D.PA.Edge : D.PB.Edge;
            }
            Color core = L >= 3 ? Kit.Core : Color.white;
            DuelFx.Starburst(at, Px, tiles, core, ea, eb, 0.08f + 0.015f * L);
            DuelFx.Spray(at, Px, sparks, Axis(), Metal, c.Even ? Color.Lerp(ea, eb, 0.5f) : ea);
            if (L >= 2)
            {
                Fx.FocusLines(at, Px, Fx.Count(6 + 2 * L), 12f * K, 24f * K, 0.12f);
            }
        }

        /// <summary>
        /// The shockwave of a decisive clash along the ground between them: none at level I, a
        /// 1.6-tile ring in the clash's colour at II (the ground scorched under it), a 2.6-tile
        /// white-hot one at III (a crater blasted where they stand: Wreck). A parry's shock
        /// ('wreck' false) leaves the ground alone.
        /// </summary>
        protected void Shock(Clash c, bool wreck = true)
        {
            if (L < 2)
            {
                return;
            }
            Vector2 at = (P.Pose.Ground + Q.Pose.Ground) * 0.5f;
            if (wreck)
            {
                Wreck(at, L >= 3 ? 1.2f : 1f, L >= 3 ? 2 : 1, c.Even ? null : Winner(c), false);
            }
            if (!Vis)
            {
                return;
            }
            Color edge = c.Even ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : Winner(c).Pal.Edge;
            Color core = L >= 3 ? Kit.Core : Color.white;
            DuelFx.GroundRing(at, Px, 4f, (L >= 3 ? 26f : 16f) * K, L >= 3 ? 0.42f : 0.32f, L >= 3 ? 2 : 1, 1,
                Fx.WithA(core, 0.95f), Fx.WithA(edge, 0.7f), Fx.WithA(edge, 0f));
        }

        /// <summary>The winner's blow goes through: its crescent, a hit star on the loser, the damage and the flash.</summary>
        protected void Strike(Duelist w, Duelist l, float chip, bool heavy)
        {
            Slash(w, heavy ? 2 : Random.Range(0, 2), 0.16f);
            if (Vis)
            {
                Vector2 at = l.BodyPos() + new Vector2(-l.Fwd * Random.Range(0f, 1.5f), Random.Range(-1f, 1.5f)) * Px;
                Fx.HitSpark(at, Px, heavy ? 2 : 1, new Vector2(-l.Fwd, 0.4f), heavy ? 6 : 4);
            }
            D.Chip(w, l, chip, heavy);
            D.Sound(heavy ? "event:/SFX/HIT/HitGeneric" : "event:/SFX/OTHER/Punch");
        }

        // --------------------------------------------------------- aftermath --

        /// <summary>
        /// Throws the two apart after a decisive clash at 'at', by the clash rule:
        /// even - both skid back 'even' tiles; a win - the winner hits through (chip) and follows
        /// through while the loser skids back 'lose' tiles and staggers; overwhelmed - a one-handed
        /// ting and the loser is flicked 'flick' tiles.
        /// </summary>
        protected void Aftermath(Clash c, Vector2 at, float even, float lose, float chip, float flick = 2f)
        {
            Vector2 axis = Axis();
            Vector2 pa = P.Pose.Ground, pb = Q.Pose.Ground;
            float k = K;
            // how long they wobble finding their feet: masters plant and go
            float b = Brisk;
            switch (c.Kind)
            {
                case ClashKind.Even:
                    KA = Knock.Skid(pa, DuelGeo.Slide(pa, -axis, even * k), 0.32f, -14f, 0.2f * b);
                    KB = Knock.Skid(pb, DuelGeo.Slide(pb, axis, even * k), 0.32f, -14f, 0.2f * b);
                    Opens(null, Link.None);
                    break;
                case ClashKind.Win:
                    {
                        Duelist w = Winner(c), l = Loser(c);
                        Vector2 away = c.AWins ? axis : -axis;
                        Vector2 wg = w.Pose.Ground, lg = l.Pose.Ground;
                        Knock kw = Knock.Skid(wg, DuelGeo.Slide(wg, away, 0.14f * k), 0.24f, 22f, 0.15f * b);
                        Knock kl = Knock.Skid(lg, DuelGeo.Slide(lg, away, lose * k), 0.3f, -24f, 0.6f * Mathf.Max(0.5f, b));
                        Opens(w, Link.Close);
                        if (c.AWins)
                        {
                            KA = kw;
                            KB = kl;
                        }
                        else
                        {
                            KA = kl;
                            KB = kw;
                        }
                        Strike(w, l, chip, false);
                        break;
                    }
                default:
                    {
                        Duelist w = Winner(c), l = Loser(c);
                        Vector2 away = c.AWins ? axis : -axis;
                        Vector2 lg = l.Pose.Ground;
                        Knock kw = Knock.Skid(w.Pose.Ground, w.Pose.Ground, 0.2f, 6f, 0.1f * b);
                        Vector2 land = DuelGeo.Land(lg, away, flick * k);
                        Knock kl = (land - lg).sqrMagnitude > 0.04f
                            ? Knock.Fly(lg, land, 0.42f, 0.55f * k, (l.Pose.FaceRight == (away.x > 0f) ? 360f : -360f), 0.3f * Mathf.Max(0.5f, b))
                            : Knock.Skid(lg, lg, 0.3f, -26f, 0.5f * Mathf.Max(0.5f, b));
                        Opens(w, Link.Down);
                        if (c.AWins)
                        {
                            KA = kw;
                            KB = kl;
                        }
                        else
                        {
                            KA = kl;
                            KB = kw;
                        }
                        // the one-handed deflection: it barely looks
                        DuelFx.Ting(at, Px, w.Pal.Core, 5, -away + Vector2.up * 0.5f, 1.1f);
                        D.Sound("event:/SFX/HIT/HitMetal");
                        D.Chip(w, l, chip * 1.2f, true);
                        break;
                    }
            }
        }

        /// <summary>Steps the aftermath (dust trails, landing puffs, skid marks); true when both have found their feet.</summary>
        protected bool Recoil(float dt)
        {
            bool a = KA.Step(P, dt);
            bool b = KB.Step(Q, dt);
            Dust(P, ref KA, dt);
            Dust(Q, ref KB, dt);
            return a && b;
        }

        private void Dust(Duelist m, ref Knock k, float dt)
        {
            if (!k.Active || m == null)
            {
                return;
            }
            if (k.Air)
            {
                if (k.Landed && !k.Marked)
                {
                    k.Marked = true;
                    DuelFx.Puff(k.To, m.Px, Combat.TileAt(k.To), 3, 14f);
                }
                return;
            }
            if (!k.Moving)
            {
                if (!k.Marked && (k.To - k.From).sqrMagnitude > 0.04f)
                {
                    k.Marked = true;
                    if (L >= 2)
                    {
                        DuelFx.SkidMarks(k.From, k.To, m.Px, Combat.TileAt(k.To), 1.2f);
                    }
                }
                return;
            }
            k.DustT -= dt;
            if (k.DustT <= 0f)
            {
                k.DustT = 0.035f;
                DuelFx.SkidDust(m.Pose.Ground, k.To - k.From, m.Px, Combat.TileAt(m.Pose.Ground));
            }
        }
    }

    // =================================================================== the pool ==

    /// <summary>
    /// BLADE LOCK. They crash together and lock weapons (bare-handed: forearms), trembling,
    /// pushing, sparks spitting from the bind, until it bursts.
    ///
    ///   CLOSE  ~0.14   both dash into the bind; first contact: a small starburst, sparks, the clang
    ///   LOCK  0.5-1.1  leaning in, trembling against each other; the bind drifts back and forth
    ///                  when they are even, steadily onto the weaker one when not (its feet slide,
    ///                  it sinks lower, the other bears down); sparks (fists: grit) spit from the
    ///                  bind; II: both glow in their tier colour, focus lines; III: the ground
    ///                  cracks under the one giving way, embers
    ///   BURST  ~0.3-0.8  the clash rule: even - a ring bursts out of the bind and both skid back;
    ///                  a win - the winner's blade slides through and the loser reels; overwhelmed -
    ///                  a ting and the loser is flicked away
    ///   SETTLE  0.2    both spring back into guard
    /// </summary>
    internal sealed class BladeLock : Exchange
    {
        private const int Close = 0, Lock = 1, Burst = 2, Done_ = 3;

        private Vector2 _sa, _sb, _axis;
        private float _tClose, _lock, _drift, _spark, _lines;
        private bool _cracked;
        private Clash _c;

        public override string Id => "blade_lock";

        public override Form Forms => Form.Blade | Form.Punch | Form.Guard | Form.Heavy;

        public override float Weight()
        {
            float w = 1f;
            if (D.ArmedA && D.ArmedB)
            {
                w *= 1.4f;
            }
            if (D.Ratio < 1.3f)
            {
                w *= 1.3f;   // two equals locked together is the classic
            }
            return w;
        }

        public override bool Plan()
        {
            float gap = (D.ArmedA || D.ArmedB ? 0.62f : 0.5f) * K;
            if (!DuelGeo.Stance(A0, B0, gap, out _sa, out _sb))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.04f;
            // how far the bind may drift onto either fighter without pushing anyone off the ground
            _drift = 0.28f * K;
            if (!Combat.SafeSpot(_sa - _axis * _drift) || !Combat.SafeSpot(_sb + _axis * _drift))
            {
                _drift = 0f;
            }
            return true;
        }

        public override void Start()
        {
            _c = D.Resolve(true);
            // two equals strain for long; a clear winner breaks it sooner
            _lock = _c.Even ? Random.Range(0.8f, 1.1f) : _c.Kind == ClashKind.Win ? Random.Range(0.55f, 0.75f) : Random.Range(0.35f, 0.45f);
            if (D.Flow)
            {
                _lock *= 0.8f;   // masters break a bind sooner
            }
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Close:
                    {
                        if (Enter())
                        {
                            DuelFx.Puff(A0, P.Px, Combat.TileAt(A0));
                            DuelFx.Puff(B0, Q.Px, Combat.TileAt(B0));
                        }
                        float k = Pt / _tClose;
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.025f, 0.13f);
                        Trail(Q, ref GhostB, dt, 0.025f, 0.13f);
                        if (k >= 1f)
                        {
                            // the bind: weapons meet
                            Vector2 at = Contact();
                            Meet(at, 0.32f * K, new Clash { Kind = ClashKind.Even }, 6);
                            Stop(0.05f);
                            Next(Lock);
                        }
                        break;
                    }
                case Lock:
                    LockTick(dt);
                    break;
                case Burst:
                    {
                        if (Enter())
                        {
                            BurstApart();
                        }
                        bool over = Recoil(dt);
                        if (over)
                        {
                            Next(Done_);
                        }
                        break;
                    }
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.7f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void LockTick(float dt)
        {
            float px = Px;
            float k = Mathf.Clamp01(Pt / _lock);
            // the struggle: back and forth between equals, steadily onto the weaker one otherwise
            float drift;
            if (_c.Even)
            {
                drift = _drift * 0.45f * Mathf.Sin(Pt * 7.5f) * (1f - 0.3f * k);
            }
            else
            {
                drift = (_c.AWins ? 1f : -1f) * _drift * Anim.Smooth(0f, 1f, k);
            }
            P.Pose.Ground = _sa + _axis * drift;
            Q.Pose.Ground = _sb + _axis * drift;
            FaceEach();
            float press = Anim.OutQuad(Mathf.Clamp01(Pt / 0.12f));
            float j = ((int)(T * 50f) & 1) == 0 ? 0.5f : -0.5f;
            LockPose(P, press, !_c.Even && !_c.AWins ? k : 0f, !_c.Even && _c.AWins ? k : 0f);
            LockPose(Q, press, !_c.Even && _c.AWins ? k : 0f, !_c.Even && !_c.AWins ? k : 0f);
            P.Pose.Nudge = new Vector2(j, 0f);
            Q.Pose.Nudge = new Vector2(-j, 0f);

            Vector2 at = Contact();
            if (L >= 2)
            {
                float pulse = 0.16f + 0.1f * Mathf.Sin(T * 22f);
                P.Glow = Fx.WithA(P.Pal.Edge, pulse);
                Q.Glow = Fx.WithA(Q.Pal.Edge, pulse);
            }
            if (Vis)
            {
                _spark -= dt;
                if (_spark <= 0f)
                {
                    _spark = Random.Range(0.045f, 0.08f);
                    Vector2 jit = at + new Vector2(0f, Random.Range(-1f, 1f) * px);
                    DuelFx.Spray(jit, px, Random.Range(1, 3), _axis, Metal, Fx.Lighten(Fx.SoilColor(Combat.TileAt(at)), 0.5f));
                }
                if (L >= 2)
                {
                    _lines -= dt;
                    if (_lines <= 0f)
                    {
                        _lines = 0.14f;
                        Fx.FocusLines(at, px, Fx.Count(5), 12f * K, 22f * K, 0.12f);
                    }
                }
                if (!_c.Even && ((int)(Pt * 20f) & 1) == 0)
                {
                    // the one giving way: its feet scrape back
                    Duelist l = Loser(_c);
                    DuelFx.SkidDust(l.Pose.Ground, _c.AWins ? _axis : -_axis, l.Px, Combat.TileAt(l.Pose.Ground), 0.5f);
                }
                if (L >= 3 && Random.value < 0.25f)
                {
                    DuelFx.Embers(at, px, 1, 3f, Color.Lerp(P.Pal.Edge, Q.Pal.Edge, Random.value));
                }
            }
            if (L >= 3 && !_cracked && k > 0.5f)
            {
                // the ground gives under the one giving way (scorched and cracked for real)
                _cracked = true;
                Duelist l = _c.Even ? (Random.value < 0.5f ? P : Q) : Loser(_c);
                Kit.Crater(l.Pose.Ground, px, Combat.TileAt(l.Pose.Ground), false, 3f, true);
                Wreck(l.Pose.Ground, 1f, 1, _c.Even ? null : Winner(_c));
            }
            if (k >= 1f)
            {
                Next(Burst);
            }
        }

        /// <summary>Leaning into the bind; 'give' sinks it (losing), 'bear' leans it over (winning).</summary>
        private static void LockPose(Duelist m, float press, float give, float bear)
        {
            m.Pose.Rot = Mathf.Lerp(4f, 20f, press) - 10f * give + 7f * bear;
            m.Pose.SX = 1f + 0.08f * press + 0.04f * give;
            m.Pose.SY = 1f - 0.08f * press - 0.08f * give;
            m.Pose.Height = 0f;
        }

        private void BurstApart()
        {
            P.Glow = Q.Glow = DuelFx.Clear;
            Vector2 at = Contact();
            if (_c.Even)
            {
                // the bind bursts: a ring out of it (white at I, their colours at II, white-hot and wide at III)
                Meet(at, 0.55f * K, _c, 10);
                if (Vis)
                {
                    Color ring = L >= 2 ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : Color.white;
                    DuelFx.GroundRing((P.Pose.Ground + Q.Pose.Ground) * 0.5f, Px, 3f, (L >= 3 ? 26f : 16f) * K, 0.34f, 2, 1,
                        L >= 3 ? Kit.Core : new Color(1f, 1f, 1f, 0.95f), Fx.WithA(ring, 0.6f), Fx.WithA(ring, 0f));
                }
                Stop(0.06f);
            }
            else
            {
                Meet(at, 0.4f * K, _c, 6);
                Shock(_c);
                Stop(0.05f);
            }
            Aftermath(_c, at, 0.85f, 1.1f, Random.Range(0.35f, 0.55f));
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }

    /// <summary>
    /// FLURRY EXCHANGE. Rapid alternating strikes and parries, faster than the eye: both slide
    /// sideways together as they trade (or the stronger drives the other back), sparks popping
    /// between them high, middle and low.
    ///
    ///   CLOSE  ~0.12   dash into reach
    ///   FLURRY 0.8-1.0 7 (I), 9 (II) or 12 (III) beats: one lunges, the other parries, a spark at a
    ///                  different height each time, ghost fists, speed lines along the slide; the
    ///                  stronger strikes more often; halfway, a clash: equals - a big clang and a
    ///                  hop apart; a win - the winner presses three beats and the last gets
    ///                  through; overwhelmed - the loser is batted away and it ends there
    ///   FINISH ~0.4-0.8  a last heavy pair of blows and the clash rule throws them apart
    ///   SETTLE  0.2
    /// </summary>
    internal sealed class FlurryExchange : Exchange
    {
        private const int Close = 0, Flurry = 1, Finish = 2, End = 3;

        private Vector2 _sa, _sb, _axis, _drift;
        private float _tClose, _beat, _hop, _lines;
        private int _beats, _mid, _done = -1, _press, _pressBeats;
        private bool[] _aLeads;
        private Vector2 _baseA, _baseB;
        private bool _batted;

        public override string Id => "flurry";

        public override Form Forms => Form.Flurry | Form.Fast | Form.Punch | Form.Kick;

        public override float Weight()
        {
            float w = 1.2f;
            if (L >= 2)
            {
                w *= 1.3f;
            }
            if (D.HpA > 0.5f && D.HpB > 0.5f)
            {
                w *= 1.2f;
            }
            return w;
        }

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 0.6f * K, out _sa, out _sb))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.04f;
            // they slide sideways together as they trade; failing that the stronger drives the other back
            float len = Random.Range(0.9f, 1.4f) * K;
            Vector2 side = new Vector2(-_axis.y, _axis.x) * (Random.value < 0.5f ? 1f : -1f);
            Vector2 push = _axis * (D.PowerA >= D.PowerB ? 1f : -1f);
            _drift = Vector2.zero;
            Vector2[] options = { side, -side, push * 0.6f };
            foreach (Vector2 o in options)
            {
                Vector2 v = o * len;
                if (Combat.ClearPath(_sa, _sa + v) && Combat.ClearPath(_sb, _sb + v))
                {
                    _drift = v;
                    break;
                }
            }
            _beats = L <= 1 ? 7 : L == 2 ? 9 : 12;
            _beat = L <= 1 ? 0.12f : L == 2 ? 0.1f : 0.075f;
            // halfway, leaving three beats for a winner to press home
            _mid = Mathf.Min(Mathf.RoundToInt(_beats * 0.55f), _beats - 4);
            // who leads each beat: alternating, the stronger more often
            _aLeads = new bool[_beats + 4];
            float bias = Mathf.Clamp(0.5f + 0.15f * Mathf.Log(Mathf.Max(0.01f, D.PowerA / Mathf.Max(0.01f, D.PowerB)), 2f), 0.25f, 0.75f);
            bool lead = Random.value < bias;
            for (int i = 0; i < _aLeads.Length; i++)
            {
                _aLeads[i] = lead;
                lead = Random.value < (lead ? 1f - bias : bias) * 1.6f ? !lead : lead;
            }
            return true;
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Close:
                    {
                        if (Enter())
                        {
                            DuelFx.Puff(A0, P.Px, Combat.TileAt(A0));
                            DuelFx.Puff(B0, Q.Px, Combat.TileAt(B0));
                        }
                        float k = Pt / _tClose;
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.025f, 0.13f);
                        Trail(Q, ref GhostB, dt, 0.025f, 0.13f);
                        if (k >= 1f)
                        {
                            Next(Flurry);
                        }
                        break;
                    }
                case Flurry:
                    FlurryTick(dt);
                    break;
                case Finish:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.3f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void FlurryTick(float dt)
        {
            float px = Px;
            float total = _beats * _beat;
            float k = Mathf.Clamp01(Pt / total);
            int i = Mathf.Min(_beats - 1, (int)(Pt / _beat));
            float local = Pt - i * _beat;
            // the lunge peaks a third into the beat
            float jab = Mathf.Clamp01(1f - Mathf.Abs(local - 0.35f * _beat) / (0.3f * _beat));
            bool aLeads = _pressBeats > 0 ? _press == 1 : _aLeads[i];
            Duelist att = aLeads ? P : Q, def = aLeads ? Q : P;

            // both slide along together; a clang hops them apart for a moment
            _hop = Mathf.MoveTowards(_hop, 0f, dt * 1.6f);
            _baseA = _sa + _drift * Anim.InOutSine(k) - _axis * _hop * K;
            _baseB = _sb + _drift * Anim.InOutSine(k) + _axis * _hop * K;
            P.Pose.Ground = _baseA;
            Q.Pose.Ground = _baseB;
            FaceEach();
            att.Pose.Ground += new Vector2(att.Fwd * 0.07f * K * jab, 0f);
            def.Pose.Ground -= new Vector2(def.Fwd * 0.03f * K * jab, 0f);
            att.Pose.Rot = 4f + 20f * jab;
            att.Pose.SX = 1f + 0.14f * jab;
            att.Pose.SY = 1f - 0.08f * jab;
            def.Pose.Rot = 4f - 12f * jab;
            def.Pose.SX = 1f - 0.03f * jab;
            def.Pose.SY = 1f + 0.04f * jab;
            P.Pose.Height = Q.Pose.Height = 0f;

            if (Vis)
            {
                // the blur of blows between the strikes
                GhostA -= dt;
                if (GhostA <= 0f)
                {
                    GhostA = 0.03f;
                    Fx.Ghost(att, new Vector2(att.Fwd * Random.Range(0f, 0.1f) * K, Random.Range(-0.04f, 0.04f) * K),
                        Random.Range(-15f, 30f), 0.07f, Fx.WithA(att.Pal.Ghost, 0.45f), att.Pal.GhostEnd);
                }
                _lines -= dt;
                if (_lines <= 0f && _drift.sqrMagnitude > 0.01f)
                {
                    _lines = 0.06f;
                    Vector2 mid = (P.Pose.Ground + Q.Pose.Ground) * 0.5f + new Vector2(0f, 4f * px);
                    Kit.SpeedLines(mid - _drift.normalized * 6f * px, _drift, px, 2, 7f, 0.16f,
                        L >= 2 ? Fx.WithA(Color.Lerp(P.Pal.Edge, Q.Pal.Edge, Random.value), 0.75f) : new Color(1f, 1f, 1f, 0.6f));
                }
            }

            if (i > _done && local >= 0.35f * _beat)
            {
                _done = i;
                Blow(i, att, def);
                if (Ph != Flurry)
                {
                    return;
                }
            }
            if (Pt >= total)
            {
                FinalBlow();
            }
        }

        private void Blow(int i, Duelist att, Duelist def)
        {
            float px = Px;
            // high, middle, low: the eye follows the sparks around
            float h = (i % 3 == 0 ? 2.5f : i % 3 == 1 ? -1f : 0.8f) * px;
            Vector2 at = Contact() + new Vector2(0f, h);
            if (Vis)
            {
                Fx.HitSpark(at, px, 0, new Vector2(-att.Fwd * 0.3f, Random.Range(-0.6f, 1f)), Metal ? 3 : 2);
                if (!Metal && Random.value < 0.5f)
                {
                    DuelFx.Spray(at, px, 1, Axis(), false, Color.white);
                }
            }
            if ((i & 1) == 0)
            {
                D.Sound(Metal ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/OTHER/Punch");
            }
            if (_pressBeats > 0)
            {
                _pressBeats--;
                if (_pressBeats == 0)
                {
                    // the pressing finally gets through
                    Strike(att, def, Random.Range(0.25f, 0.35f), false);
                    Stop(0.04f);
                }
                return;
            }
            if (i != _mid)
            {
                return;
            }
            // halfway: a clash
            Clash c = D.Resolve(false);
            switch (c.Kind)
            {
                case ClashKind.Even:
                    Meet(at, 0.45f * K, c, 8);
                    _hop = 0.25f;
                    Stop(0.05f);
                    break;
                case ClashKind.Win:
                    Meet(at, 0.3f * K, c, 4);
                    _press = c.AWins ? 1 : 2;
                    _pressBeats = 3;
                    break;
                default:
                    // batted away: the flurry ends right there
                    _batted = true;
                    Meet(at, 0.35f * K, c, 4);
                    Aftermath(c, at, 0.7f, 1f, Random.Range(0.4f, 0.55f));
                    Next(Finish);
                    break;
            }
        }

        private void FinalBlow()
        {
            if (_batted)
            {
                return;
            }
            Vector2 at = Contact();
            Clash c = D.Resolve(true);
            // the last pair: both wind up and swing full
            Slash(P, 2, 0.16f);
            Slash(Q, 2, 0.16f);
            Meet(at, (c.Even ? 0.6f : 0.45f) * K, c, 10);
            Shock(c);
            Stop(c.Even ? 0.07f : 0.05f);
            Aftermath(c, at, 0.7f, 1f, Random.Range(0.35f, 0.5f));
            Next(Finish);
        }
    }

    /// <summary>
    /// AERIAL CLASH. Both leap, meet in mid-air two or three times with starbursts, bounce apart,
    /// rush in again, and come down apart.
    ///
    ///   SET     0.14   slide to the takeoff spots, crouch
    ///   LEAP    0.28   both spring up toward the same point (stretched, afterimages, takeoff dust,
    ///                  their shadows left on the ground shrinking)
    ///   CLASH  x2-3    a rush (0.08 s) and a mid-air meeting: a starburst growing with each clash,
    ///                  an air ring, sparks; the loser of each bounces farther and tumbles; each
    ///                  meeting is a little higher or lower and off to one side; at III the last
    ///                  one freezes the world on an impact frame
    ///   DESCEND ~0.4   down to their landing spots: the winner flips, the loser tumbles (overwhelmed:
    ///                  spiked into the ground, a small crater at II+)
    ///   LAND   ~0.3    a skid, dust, the knees take it
    /// </summary>
    internal sealed class AerialClash : Exchange
    {
        private const int Set = 0, Leap = 1, Rush = 2, Bounce = 3, Descend = 4, Land = 5, Fin = 6;

        private Vector2 _sa, _sb, _axis, _mid, _la, _lb;
        private float _h, _tSet;
        private int _n, _i;
        private Vector2 _fa, _fb, _ta, _tb;   // ground from/to of the current leg
        private float _hFa, _hFb, _hTa, _hTb, _dur, _rotA, _rotB;
        private Clash _c;
        private bool _spiked;

        public override string Id => "aerial";

        public override Form Forms => Form.Aerial | Form.Kick | Form.Punch | Form.AntiAir;

        public override float Weight()
        {
            float w = 0.9f * (L <= 1 ? 0.5f : L == 2 ? 1f : 1.5f);
            if (D.KA > 1.5f || D.KB > 1.5f)
            {
                w *= 0.6f;   // big bodies don't fly well
            }
            return w;
        }

        public override bool Plan()
        {
            float dist = (B0 - A0).magnitude;
            float gap = Mathf.Clamp(dist, 1.6f * K, 2.8f * K);
            if (!DuelGeo.Stance(A0, B0, gap, out _sa, out _sb))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            _mid = (_sa + _sb) * 0.5f;
            _h = (L <= 1 ? 1.3f : L == 2 ? 1.8f : 2.3f) * K + (D.TA == Tier.God && D.TB == Tier.God ? 0.4f : 0f);
            _n = L <= 1 ? 2 : L == 2 ? Random.Range(2, 4) : 3;
            // where they come down: apart, on safe ground
            _la = DuelGeo.Land(_mid, -_axis, Random.Range(1.2f, 1.7f) * K);
            _lb = DuelGeo.Land(_mid, _axis, Random.Range(1.2f, 1.7f) * K);
            if ((_la - _mid).sqrMagnitude < 0.25f)
            {
                _la = _sa;
            }
            if ((_lb - _mid).sqrMagnitude < 0.25f)
            {
                _lb = _sb;
            }
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tSet = d > 0.25f ? Mathf.Clamp(0.08f + 0.03f * d, 0.12f, 0.22f) : 0.1f;
            return true;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / _tSet);
                        Reposition(P, A0, _sa, Mathf.Clamp01(k / 0.6f), _sb);
                        Reposition(Q, B0, _sb, Mathf.Clamp01(k / 0.6f), _sa);
                        FaceEach();
                        if (k > 0.6f)
                        {
                            // the crouch before the jump
                            float c = Anim.OutQuad((k - 0.6f) / 0.4f);
                            foreach (Duelist m in new[] { P, Q })
                            {
                                m.Pose.SX = Mathf.Lerp(1f, 1.14f, c);
                                m.Pose.SY = Mathf.Lerp(1f, 0.82f, c);
                                m.Pose.Rot = -6f * c;
                            }
                        }
                        if (k >= 1f)
                        {
                            Next(Leap);
                        }
                        break;
                    }
                case Leap:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Takeoff(_sa, P.Px);
                            Takeoff(_sb, Q.Px);
                            Leg(_sa, _sb, 0f, 0f, ClashSpot(0, -1f), ClashSpot(0, 1f), _h, _h, 0.28f);
                        }
                        Fly(dt, Anim.OutCubic(Mathf.Clamp01(Pt / _dur)), true);
                        if (Pt >= _dur)
                        {
                            _i = 0;
                            Next(Rush);
                        }
                        break;
                    }
                case Rush:
                    {
                        if (Enter())
                        {
                            float hi = _h + (_i == 1 ? 0.3f : _i == 2 ? -0.2f : 0f) * K;
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height, ClashSpot(_i, -1f), ClashSpot(_i, 1f), hi, hi, 0.08f);
                        }
                        Fly(dt, Anim.InQuad(Mathf.Clamp01(Pt / _dur)), true);
                        P.Pose.Rot = 22f;
                        Q.Pose.Rot = 22f;
                        P.Pose.SX = Q.Pose.SX = 1.25f;
                        P.Pose.SY = Q.Pose.SY = 0.84f;
                        if (Pt >= _dur)
                        {
                            Hit();
                            Next(Bounce);
                        }
                        break;
                    }
                case Bounce:
                    {
                        if (Enter())
                        {
                            float wa = _c.Kind == ClashKind.Even ? 0.6f : _c.AWins ? 0.45f : 1f;
                            float wb = _c.Kind == ClashKind.Even ? 0.6f : _c.AWins ? 1f : 0.45f;
                            _rotA = _c.Kind != ClashKind.Even && !_c.AWins ? -200f : -40f;
                            _rotB = _c.Kind != ClashKind.Even && _c.AWins ? -200f : -40f;
                            Vector2 ga = P.Pose.Ground - _axis * wa * K, gb = Q.Pose.Ground + _axis * wb * K;
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height, ga, gb, P.Pose.Height + 0.18f * K, Q.Pose.Height + 0.18f * K, 0.24f);
                        }
                        float k = Mathf.Clamp01(Pt / _dur);
                        Fly(dt, Anim.OutCubic(k), false);
                        P.Pose.Rot = _rotA * Anim.OutQuad(k) * (1f - k) + 6f * k;
                        Q.Pose.Rot = _rotB * Anim.OutQuad(k) * (1f - k) + 6f * k;
                        P.Pose.SX = Q.Pose.SX = 1f;
                        P.Pose.SY = Q.Pose.SY = 1f;
                        if (Pt >= _dur)
                        {
                            _i++;
                            Next(_i < _n && _c.Kind != ClashKind.Overwhelm ? Rush : Descend);
                        }
                        break;
                    }
                case Descend:
                    {
                        if (Enter())
                        {
                            _spiked = _c.Kind == ClashKind.Overwhelm;
                            Leg(P.Pose.Ground, Q.Pose.Ground, P.Pose.Height, Q.Pose.Height, _la, _lb, 0f, 0f, 0.4f);
                            _rotA = P.Pose.Rot;
                            _rotB = Q.Pose.Rot;
                        }
                        float k = Mathf.Clamp01(Pt / 0.4f);
                        // the loser of an overwhelming clash is spiked straight down, faster
                        float kl = _spiked ? Mathf.Clamp01(Pt / 0.22f) : k;
                        float ka = _spiked && !_c.AWins ? kl : k;
                        float kb = _spiked && _c.AWins ? kl : k;
                        P.Pose.Ground = Vector2.Lerp(_fa, _ta, Anim.OutQuad(ka));
                        Q.Pose.Ground = Vector2.Lerp(_fb, _tb, Anim.OutQuad(kb));
                        P.Pose.Height = _hFa * (1f - ka * ka);
                        Q.Pose.Height = _hFb * (1f - kb * kb);
                        P.Pose.Rot = DescentRot(true, ka);
                        Q.Pose.Rot = DescentRot(false, kb);
                        P.Pose.SX = Q.Pose.SX = 1f;
                        P.Pose.SY = Q.Pose.SY = 1f;
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.04f, 0.12f, 0.6f);
                        Trail(Q, ref GhostB, dt, 0.04f, 0.12f, 0.6f);
                        if (_spiked && kl >= 1f && Enter2())
                        {
                            Duelist l = Loser(_c);
                            Vector2 at = l.Pose.Ground;
                            if (L >= 2)
                            {
                                Kit.Crater(at, l.Px, Combat.TileAt(at), L >= 3, 3.5f, true, l == P ? Q.Pal.Edge : P.Pal.Edge);
                            }
                            Wreck(at, 1f, ByLevel(0, 1, 2), Winner(_c));
                            Opens(Winner(_c), Link.Down);
                            Kit.DustRing(at, l.Px, Combat.TileAt(at), 10, 50f, 0.5f);
                            Kit.Shake(at, 0.15f, 0.006f);
                            D.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact");
                        }
                        if (k >= 1f)
                        {
                            Next(Land);
                        }
                        break;
                    }
                case Land:
                    {
                        if (Enter())
                        {
                            DuelFx.Puff(_la, P.Px, Combat.TileAt(_la), 3, 16f);
                            DuelFx.Puff(_lb, Q.Px, Combat.TileAt(_lb), 3, 16f);
                            KA = Knock.Skid(_la, DuelGeo.Slide(_la, -_axis, 0.18f * K), 0.2f, -8f, 0.1f);
                            KB = Knock.Skid(_lb, DuelGeo.Slide(_lb, _axis, 0.18f * K), 0.2f, -8f, 0.1f);
                        }
                        Recoil(dt);
                        FaceEach();
                        // the loser takes it on the knees
                        float hard = Anim.Spring(Pt, 6f, 12f);
                        if (_c.Kind != ClashKind.Even)
                        {
                            Duelist l = Loser(_c);
                            l.Pose.SY = 1f - 0.22f * Mathf.Abs(hard);
                            l.Pose.SX = 1f + 0.12f * Mathf.Abs(hard);
                        }
                        if (Pt >= Mathf.Max(0.22f, 0.32f * Brisk))
                        {
                            Next(Fin);
                        }
                        break;
                    }
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1f);
                    if (Pt >= Rest(0.16f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private bool _enter2;

        private bool Enter2()
        {
            if (_enter2)
            {
                return false;
            }
            _enter2 = true;
            return true;
        }

        /// <summary>Where they meet the i-th time: around the middle, a little to one side each time.</summary>
        private Vector2 ClashSpot(int i, float side)
        {
            float shift = (i == 1 ? 0.22f : i == 2 ? -0.18f : 0f) * K;
            return _mid + _axis * (shift + side * 0.3f * K);
        }

        private void Leg(Vector2 fa, Vector2 fb, float hfa, float hfb, Vector2 ta, Vector2 tb, float hta, float htb, float dur)
        {
            _fa = fa;
            _fb = fb;
            _ta = ta;
            _tb = tb;
            _hFa = hfa;
            _hFb = hfb;
            _hTa = hta;
            _hTb = htb;
            _dur = dur;
        }

        private void Fly(float dt, float e, bool stretch)
        {
            P.Pose.Ground = Vector2.Lerp(_fa, _ta, e);
            Q.Pose.Ground = Vector2.Lerp(_fb, _tb, e);
            P.Pose.Height = Mathf.Lerp(_hFa, _hTa, e);
            Q.Pose.Height = Mathf.Lerp(_hFb, _hTb, e);
            FaceEach();
            if (stretch && Ph == Leap)
            {
                float s = 1f - e;
                P.Pose.SX = Q.Pose.SX = Mathf.Lerp(1f, 0.74f, s);
                P.Pose.SY = Q.Pose.SY = Mathf.Lerp(1f, 1.36f, s);
                P.Pose.Rot = Q.Pose.Rot = Mathf.Lerp(10f, -6f, s);
            }
            Trail(P, ref GhostA, dt, 0.028f, 0.13f);
            Trail(Q, ref GhostB, dt, 0.028f, 0.13f);
        }

        private void Takeoff(Vector2 at, float px)
        {
            WorldTile t = Combat.TileAt(at);
            Kit.DustRing(at, px, t, 8, 45f, 0.45f, K);
            if (Vis)
            {
                DuelFx.GroundRing(at, px, 2f, 11f * K, 0.22f, 2, 1, new Color(1f, 1f, 1f, 0.85f), DuelFx.Clear);
            }
        }

        private void Hit()
        {
            bool last = _i >= _n - 1;
            _c = D.Resolve(last);
            Vector2 at = Contact();
            // each meeting is bigger than the last
            float size = (L <= 1 ? 0.35f : 0.5f) + 0.3f * _i;
            Meet(at, size * K, _c, 8 + 2 * _i);
            if (Vis)
            {
                Color ring = L >= 2 ? (_c.Even ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : Winner(_c).Pal.Edge) : Color.white;
                DuelFx.AirRing(at, Px, 3f, (12f + 5f * _i) * K, 0.22f, 2, new Color(1f, 1f, 1f, 0.95f), Fx.WithA(ring, 0.7f), Fx.WithA(ring, 0f));
            }
            if (last && L >= 3)
            {
                Kit.ImpactFrame(at, Px, 0.6f, _c.Even ? P.Pal : Winner(_c).Pal, false);
                Kit.Stop(at, 0.08f);
                Kit.Shake(at, 0.18f, 0.006f);
            }
            else
            {
                Stop(0.05f);
            }
            if (_c.Kind != ClashKind.Even)
            {
                Duelist w = Winner(_c), l = Loser(_c);
                l.Flash(0.05f, Color.white);
                if (last || _c.Kind == ClashKind.Overwhelm)
                {
                    Strike(w, l, _c.Kind == ClashKind.Overwhelm ? 0.55f : Random.Range(0.35f, 0.5f), _c.Kind == ClashKind.Overwhelm);
                    Opens(w, Link.Close);
                }
            }
        }

        private float DescentRot(bool a, float k)
        {
            bool loser = _c.Kind != ClashKind.Even && (_c.AWins != a);
            float from = a ? _rotA : _rotB;
            if (loser)
            {
                // tumbling down backward, one whole turn, upright again as it lands
                return Mathf.Lerp(from, -360f, Anim.OutQuad(k));
            }
            if (L >= 2)
            {
                return from + 360f * Anim.InOutCubic(Mathf.Clamp01(k / 0.8f));   // a clean flip
            }
            return Mathf.Lerp(from, 0f, k);
        }
    }

    /// <summary>
    /// CHASE. One breaks away along the ground in a blur of afterimages, zig-zagging; the other
    /// follows the same path a beat behind; the runner plants, turns, and they clash.
    /// The runner is the one losing (repositioning) or, between equals, the quicker one leading.
    /// Only along safe ground: every leg is checked clear of water, lava and walls.
    ///
    ///   BREAK  0.10   the runner pushes off (crouch, dust burst); the chaser reacts a beat later
    ///   RUN  2-3 legs a 0.16 s blur per leg, a plant and a turn between legs (dust, a skid); speed
    ///                 lines behind both; the chaser traces the same route
    ///   TURN  ~0.15   the runner plants and turns to meet it
    ///   CLASH ~0.4-0.8  the chaser's lunge meets the runner's turn: the clash rule
    ///   SETTLE 0.2
    /// </summary>
    internal sealed class Chase : Exchange
    {
        private const int Run = 0, Clashing = 1, End = 2;
        private const float Leg = 0.16f, Plant = 0.07f, Delay = 0.12f, ChaserLeg = 0.15f, ChaserPlant = 0.045f;

        private Duelist _r, _ch;
        private Vector2[] _path;       // the runner's route (start + legs)
        private Vector2[] _cpath;      // the chaser's route (its start, then the runner's, ending just short of it)
        private float _tRun, _tChase;
        private float _lineR, _lineC;
        private int _plantedR = -1, _plantedC = -1;
        private bool _runnerIsA;

        public override string Id => "chase";

        public override Form Forms => Form.Dash | Form.Evasive | Form.Fast;

        public override float Weight()
        {
            float w = 0.85f;
            if (Mathf.Abs(D.HpA - D.HpB) > 0.2f)
            {
                w *= 1.5f;   // the one losing breaks away to catch its breath
            }
            return w;
        }

        public override bool Plan()
        {
            // the runner: the one losing, else the quicker tier, else either
            if (Mathf.Abs(D.HpA - D.HpB) > 0.2f)
            {
                _runnerIsA = D.HpA < D.HpB;
            }
            else
            {
                _runnerIsA = D.TA != D.TB ? D.TA > D.TB : Random.value < 0.5f;
            }
            Vector2 r0 = _runnerIsA ? A0 : B0, c0 = _runnerIsA ? B0 : A0;
            Vector2 away = r0 - c0;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(Random.value < 0.5f ? -1f : 1f, 0f);
            int legs = Random.Range(2, 4);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector2[] path = new Vector2[legs + 1];
                path[0] = r0;
                float ang = Mathf.Atan2(away.y, away.x) + Random.Range(-1f, 1f) * (attempt < 2 ? 0.9f : 2.2f);
                float zig = Random.value < 0.5f ? 1f : -1f;
                bool ok = true;
                for (int i = 1; i <= legs && ok; i++)
                {
                    float len = Random.Range(1.2f, 1.8f) * K;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f).normalized;
                    Vector2 to = Combat.Clamp(path[i - 1] + dir * len);
                    ok = Combat.SafeSpot(to) && Combat.ClearPath(path[i - 1], to);
                    path[i] = to;
                    // zig-zag: turn the other way each leg
                    ang += zig * (i % 2 == 1 ? 1f : -1f) * Random.Range(0.6f, 1.1f);
                }
                if (!ok || !Combat.ClearPath(c0, r0))
                {
                    continue;
                }
                _path = path;
                // the chaser follows the same route and stops just short of the runner
                _cpath = new Vector2[legs + 2];
                _cpath[0] = c0;
                for (int i = 0; i <= legs; i++)
                {
                    _cpath[i + 1] = path[i];
                }
                Vector2 last = path[legs], prev = path[legs - 1];
                Vector2 back = (prev - last).sqrMagnitude > 1e-4f ? (prev - last).normalized : -away;
                _cpath[legs + 1] = last + back * 0.62f * K;
                if (!Combat.SafeSpot(_cpath[legs + 1]))
                {
                    _cpath[legs + 1] = Vector2.Lerp(prev, last, 0.6f);
                }
                _tRun = 0.1f + legs * (Leg + Plant);
                _tChase = 0.1f + Delay + (legs + 1) * (ChaserLeg + ChaserPlant);
                return true;
            }
            return false;
        }

        public override void Start()
        {
            _r = _runnerIsA ? P : Q;
            _ch = _runnerIsA ? Q : P;
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Run:
                    {
                        if (Enter())
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Kit.DustRing(_path[0], _r.Px, Combat.TileAt(_path[0]), 6, 40f, 0.4f, K);
                        }
                        Runner(dt);
                        Chaser(dt);
                        if (Pt >= Mathf.Max(_tRun + 0.12f, _tChase))
                        {
                            Clashes();
                        }
                        break;
                    }
                case Clashing:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.1f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private void Runner(float dt)
        {
            float t = Pt - 0.1f;
            int legs = _path.Length - 1;
            if (t < 0f)
            {
                // the push-off crouch
                float c = Anim.OutQuad(Mathf.Clamp01(Pt / 0.1f));
                _r.Pose.Ground = _path[0];
                _r.Pose.SX = Mathf.Lerp(1f, 1.14f, c);
                _r.Pose.SY = Mathf.Lerp(1f, 0.84f, c);
                _r.Pose.Rot = 10f * c;
                Face(_r, _path[1]);
                return;
            }
            int i = Mathf.Min(legs - 1, (int)(t / (Leg + Plant)));
            float local = t - i * (Leg + Plant);
            if (t >= legs * (Leg + Plant))
            {
                // planted at the end, turned to meet the chaser
                _r.Pose.Ground = _path[legs];
                Face(_r, _ch.Pose.Ground);
                float w = t - legs * (Leg + Plant);
                Guard(_r, w, 0f, 1.3f);
                _r.Pose.Rot = Mathf.Lerp(-12f, 8f, Anim.OutQuad(Mathf.Clamp01(w / 0.12f)));
                return;
            }
            Vector2 from = _path[i], to = _path[i + 1];
            if (local < Leg)
            {
                float k = local / Leg;
                Face(_r, to);
                DashPose(_r, from, to, k);
                _r.Pose.SX = 1f + 0.38f * Mathf.Sin(Mathf.PI * k);
                Trail(_r, ref GhostA, dt, 0.02f, 0.2f);
                Lines(_r, ref _lineR, dt, to - from);
            }
            else
            {
                // the plant: skidding to a stop, already turning for the next leg
                _r.Pose.Ground = to;
                float k = (local - Leg) / Plant;
                _r.Pose.Rot = -10f * (1f - k);
                _r.Pose.SX = 1.08f;
                _r.Pose.SY = 0.9f;
                if (_plantedR < i)
                {
                    _plantedR = i;
                    DuelFx.Puff(to, _r.Px, Combat.TileAt(to), 2, 10f);
                }
            }
        }

        private void Chaser(float dt)
        {
            float t = Pt - 0.1f - Delay;
            int legs = _cpath.Length - 1;
            if (t < 0f)
            {
                Face(_ch, _r.Pose.Ground);
                Guard(_ch, Pt, 0.5f, 1.2f);
                _ch.Pose.Ground = _cpath[0];
                return;
            }
            int i = Mathf.Min(legs - 1, (int)(t / (ChaserLeg + ChaserPlant)));
            float local = t - i * (ChaserLeg + ChaserPlant);
            if (t >= legs * (ChaserLeg + ChaserPlant))
            {
                _ch.Pose.Ground = _cpath[legs];
                Face(_ch, _r.Pose.Ground);
                _ch.Pose.Rot = 20f;
                _ch.Pose.SX = 1.16f;
                _ch.Pose.SY = 0.88f;
                return;
            }
            Vector2 from = _cpath[i], to = _cpath[i + 1];
            if ((to - from).sqrMagnitude < 0.0025f)
            {
                _ch.Pose.Ground = to;
                return;
            }
            if (local < ChaserLeg)
            {
                float k = local / ChaserLeg;
                Face(_ch, to);
                DashPose(_ch, from, to, k);
                _ch.Pose.SX = 1f + 0.38f * Mathf.Sin(Mathf.PI * k);
                Trail(_ch, ref GhostB, dt, 0.02f, 0.2f);
                Lines(_ch, ref _lineC, dt, to - from);
            }
            else
            {
                _ch.Pose.Ground = to;
                _ch.Pose.Rot = 6f;
                _ch.Pose.SX = 1.06f;
                _ch.Pose.SY = 0.92f;
                if (_plantedC < i)
                {
                    _plantedC = i;
                    DuelFx.Puff(to, _ch.Px, Combat.TileAt(to), 1, 8f);
                }
            }
        }

        private void Lines(Duelist m, ref float timer, float dt, Vector2 dir)
        {
            if (!Vis)
            {
                return;
            }
            timer -= dt;
            if (timer > 0f)
            {
                return;
            }
            timer = 0.04f;
            Color c = L >= 2 ? Fx.WithA(m.Pal.Edge, 0.7f) : new Color(1f, 1f, 1f, 0.6f);
            Kit.SpeedLines(m.BodyPos() - dir.normalized * 5f * m.Px, dir, m.Px, 2, 5f, 0.14f, c);
        }

        private void Clashes()
        {
            // the chaser's lunge meets the runner's turn
            Vector2 at = Contact();
            Clash c = D.Resolve(true);
            Slash(_ch, Random.Range(0, 3), 0.15f);
            Slash(_r, 1, 0.15f);
            Meet(at, (c.Even ? 0.6f : 0.45f) * K, c, 8);
            if (Vis && L <= 1)
            {
                DuelFx.GroundRing((P.Pose.Ground + Q.Pose.Ground) * 0.5f, Px, 3f, 12f * K, 0.26f, 1, 1, new Color(1f, 1f, 1f, 0.9f), DuelFx.Clear);
            }
            Shock(c);
            Stop(0.06f);
            Aftermath(c, at, 0.8f, 1.1f, Random.Range(0.35f, 0.5f));
            Next(Clashing);
        }
    }

    /// <summary>
    /// GROUND SHAKER. Both leap and strike down at the ground between them at the same moment:
    /// the earth answers with a crater and a shockwave and throws both of them back.
    ///
    ///   CLOSE  ~0.12   slide to 1.3 tiles apart, facing
    ///   RAISE   0.28   both hop, leaning back with arms high; a ring closes in on the spot
    ///                  between them (the telegraph), dust drawn in; III: a glint at the top
    ///   STRIKE  0.08   both swing down and drop: falling crescents
    ///   IMPACT         I: a dust burst; II: a crater and a tinted shockwave; III: a glowing crater,
    ///                  a white-hot 2.5-tile shockwave, embers, a world hit-stop; debris, a dust wall,
    ///                  a shake; anyone else standing close is knocked off its feet (II+)
    ///   LAUNCH ~0.5    both thrown back by the blast (the clash rule decides who flies farther
    ///                  and who takes it on the back), landing, a skid
    ///   SETTLE  0.2
    /// </summary>
    internal sealed class GroundShaker : Exchange
    {
        private const int Close = 0, Raise = 1, Strike_ = 2, Thrown = 3, End = 4;

        private Vector2 _sa, _sb, _axis, _mid;
        private float _tClose;
        private Clash _c;

        public override string Id => "ground_shaker";

        public override Form Forms => Form.Ground | Form.Area | Form.Heavy | Form.Punch;

        public override float Weight()
        {
            float w = 0.8f;
            if (L >= 3 || D.KA > 1.3f || D.KB > 1.3f)
            {
                w *= 1.4f;
            }
            if (L <= 1)
            {
                w *= 0.55f;
            }
            return w;
        }

        public override bool Plan()
        {
            if (!DuelGeo.Stance(A0, B0, 1.3f * K, out _sa, out _sb))
            {
                return false;
            }
            _mid = (_sa + _sb) * 0.5f;
            // it needs solid ground to break
            if (!Combat.SafeSpot(_mid))
            {
                return false;
            }
            _axis = (_sb - _sa).normalized;
            float d = Mathf.Max((_sa - A0).magnitude, (_sb - B0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.06f;
            return true;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Close:
                    {
                        float k = Pt / _tClose;
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        Trail(P, ref GhostA, dt, 0.03f, 0.12f);
                        Trail(Q, ref GhostB, dt, 0.03f, 0.12f);
                        if (k >= 1f)
                        {
                            Next(Raise);
                        }
                        break;
                    }
                case Raise:
                    {
                        if (Enter())
                        {
                            DuelFx.Puff(_sa, P.Px, Combat.TileAt(_sa));
                            DuelFx.Puff(_sb, Q.Px, Combat.TileAt(_sb));
                            if (Vis)
                            {
                                // the telegraph: a ring closing in on the spot
                                Color c = L >= 2 ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : new Color(1f, 0.95f, 0.85f, 1f);
                                Fx.Ring(_mid, px, 0.36f, 16f * K, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                                    Fx.WithA(c, 0f), Fx.WithA(c, 0.8f), Fx.WithA(c, 0.95f));
                                DuelFx.Swirl(_mid, px, Combat.TileAt(_mid), 1f * K, 6, 0.36f, 9f);
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.28f);
                        float up = Anim.OutQuad(k);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = 0.5f * K * up;
                            m.Pose.Rot = -28f * up;
                            m.Pose.SX = Mathf.Lerp(1f, 0.88f, up);
                            m.Pose.SY = Mathf.Lerp(1f, 1.16f, up);
                        }
                        if (L >= 3 && Pt >= 0.18f && Enter2())
                        {
                            Fx.Glint(P.HeadPos() + new Vector2(P.Fwd * 2f * px, 0f), px, 0.18f, P.Pal.Core, 0.9f);
                            Fx.Glint(Q.HeadPos() + new Vector2(Q.Fwd * 2f * px, 0f), px, 0.18f, Q.Pal.Core, 0.9f);
                        }
                        if (k >= 1f)
                        {
                            Next(Strike_);
                        }
                        break;
                    }
                case Strike_:
                    {
                        if (Enter())
                        {
                            Slash(P, 0, 0.14f);
                            Slash(Q, 0, 0.14f);
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                        }
                        float k = Mathf.Clamp01(Pt / 0.08f);
                        foreach (Duelist m in new[] { P, Q })
                        {
                            m.Pose.Height = 0.5f * K * (1f - k * k);
                            m.Pose.Rot = Mathf.Lerp(-28f, 46f, Anim.OutQuad(k));
                            m.Pose.SX = Mathf.Lerp(0.88f, 1.2f, k);
                            m.Pose.SY = Mathf.Lerp(1.16f, 0.8f, k);
                        }
                        if (k >= 1f)
                        {
                            Impact();
                            Next(Thrown);
                        }
                        break;
                    }
                case Thrown:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    if (KA.Air && !KA.Landed || KB.Air && !KB.Landed)
                    {
                        Trail(P, ref GhostA, dt, 0.05f, 0.12f, 0.5f);
                        Trail(Q, ref GhostB, dt, 0.05f, 0.12f, 0.5f);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 1.4f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        private bool _enter2;

        private bool Enter2()
        {
            if (_enter2)
            {
                return false;
            }
            _enter2 = true;
            return true;
        }

        private void Impact()
        {
            float px = Px;
            WorldTile tile = Combat.TileAt(_mid);
            _c = D.Resolve(true);
            Color edge = _c.Even ? Color.Lerp(P.Pal.Edge, Q.Pal.Edge, 0.5f) : Winner(_c).Pal.Edge;
            D.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact");
            D.Sound("event:/SFX/HIT/HitStone");
            if (L >= 3)
            {
                D.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall");
            }
            if (Vis)
            {
                Kit.Flash(_mid + new Vector2(0f, 2f * px), px * K, 8 + 3 * L, 0.08f, L >= 3 ? Kit.Core : Color.white);
                if (L >= 2)
                {
                    Kit.Crater(_mid, px * K, tile, L >= 3, L >= 3 ? 5f : 3.5f, L == 2, edge);
                }
                float ring = (L <= 1 ? 12f : L == 2 ? 18f : 26f) * K;
                Color rc = L >= 3 ? Kit.Core : L == 2 ? edge : Color.white;
                DuelFx.GroundRing(_mid, px, 4f, ring, 0.42f, L >= 2 ? 3 : 2, 1, Fx.WithA(rc, 1f), Fx.WithA(L >= 2 ? edge : Color.white, 0.7f), Fx.WithA(edge, 0f));
                Kit.DustRing(_mid, px, tile, 12 + 4 * L, 70f, 0.75f, K);
                Kit.Debris(_mid, px, tile, 6 + 4 * L, K);
                if (L >= 3)
                {
                    DuelFx.Embers(_mid, px, 10, 5f, edge);
                    Kit.ImpactFrame(_mid + new Vector2(0f, 3f * px), px, 0.7f, _c.Even ? P.Pal : Winner(_c).Pal, false);
                }
                if (L >= 2)
                {
                    Kit.Shake(_mid, 0.25f, 0.008f, K);
                }
            }
            if (L >= 3)
            {
                Kit.Stop(_mid, 0.08f);
            }
            else
            {
                Stop(0.06f);
            }
            KnockBystanders();
            // the earth really answers (a heavy landing scorches it at I, craters it at II, blasts and
            // burns it at III); where they come down is worked out after, on what is left
            Wreck(_mid, ByLevel(1, 3, 4) * 0.5f, ByLevel(1, 2, 3), _c.Even ? null : Winner(_c));

            // the blast throws both back; who flies farther is the clash rule
            float da = 1.6f, db = 1.6f;
            float spinA = -360f, spinB = -360f;
            if (_c.Kind != ClashKind.Even)
            {
                Duelist w = Winner(_c), l = Loser(_c);
                Strike(w, l, _c.Kind == ClashKind.Overwhelm ? 0.6f : Random.Range(0.4f, 0.55f), _c.Kind == ClashKind.Overwhelm);
                Opens(w, Link.Down);
                float lose = _c.Kind == ClashKind.Overwhelm ? 2.4f : 2f, win = _c.Kind == ClashKind.Overwhelm ? 0.25f : 1f;
                if (_c.AWins)
                {
                    da = win;
                    db = lose;
                }
                else
                {
                    da = lose;
                    db = win;
                }
            }
            Vector2 la = DuelGeo.Land(_sa, -_axis, da * K), lb = DuelGeo.Land(_sb, _axis, db * K);
            KA = Launch(_sa, la, da, P.Pose.FaceRight, spinA);
            KB = Launch(_sb, lb, db, Q.Pose.FaceRight, spinB);
        }

        private Knock Launch(Vector2 from, Vector2 to, float dist, bool faceRight, float spin)
        {
            if ((to - from).sqrMagnitude < 0.04f || dist < 0.5f)
            {
                // too heavy to be thrown far: braced in the crater, sliding a little
                return Knock.Skid(from, to, 0.25f, -10f, 0.3f * Mathf.Max(0.5f, Brisk));
            }
            // thrown backward: a backflip (negative rotation leans back, whichever way it faces)
            return Knock.Fly(from, to, 0.3f + 0.1f * dist, (0.45f + 0.15f * dist) * K, spin, 0.3f * Mathf.Max(0.5f, Brisk));
        }

        /// <summary>The shockwave knocks anyone else standing close off its feet (no damage).</summary>
        private void KnockBystanders()
        {
            if (L < 2)
            {
                return;
            }
            float r = 1.6f * K;
            foreach (Actor u in Combat.FoesAround(P.A, _mid, r))
            {
                if (u != Q.A)
                {
                    Combat.Launch(u, _mid, 0.9f, 0.5f, Random.value < 0.5f ? -480f : 480f);
                }
            }
            foreach (Actor u in Combat.FoesAround(Q.A, _mid, r))
            {
                if (u != P.A)
                {
                    Combat.Launch(u, _mid, 0.9f, 0.5f, Random.value < 0.5f ? -480f : 480f);
                }
            }
        }
    }

    /// <summary>
    /// FEINT. One winds up a blow, vanishes, and reappears behind the other, who turns and blocks
    /// just in time: ting. Experts and above only (it takes skill). The stronger roll may turn it
    /// around: a late block lets the blow through, and a far stronger defender parries and flicks
    /// the feinter away.
    ///
    ///   CLOSE  ~0.12   into reach (if needed)
    ///   WINDUP  0.18   the feinter rears back, a glint (the tell); the defender raises its guard
    ///   VANISH  0.12   it's gone: an afterimage stays mid-swing, a puff of dust, ghost silhouettes
    ///                  arc round the defender; the defender flinches at the afterimage
    ///   BEHIND  0.08   it reappears behind, already swinging (focus lines on it); the defender
    ///                  snaps round, a "!" glint over its head
    ///   BLOCK          ting: glint, sparks, a small ring; or the blow gets through; or the parry
    ///   SETTLE ~0.4-0.8
    /// </summary>
    internal sealed class Feint : Exchange
    {
        private const int Close = 0, Windup = 1, Vanish = 2, Behind = 3, After = 4, End = 5;

        private bool _fIsA;
        private Duelist _f, _v;
        private Vector2 _fs, _vs, _behind, _f0, _v0;
        private float _tClose;

        public override string Id => "feint";

        public override Form Forms => Form.Feint | Form.Counter | Form.Fast | Form.Clone;

        public override float Weight()
        {
            if (D.TA < Tier.Expert && D.TB < Tier.Expert)
            {
                return 0f;
            }
            float w = 0.9f;
            if (D.Ratio >= 1.5f)
            {
                w *= 1.4f;   // the stronger one toys with the other
            }
            return w;
        }

        public override bool Plan()
        {
            // the feinter: the higher tier (it has the skill), else either
            _fIsA = D.TA != D.TB ? D.TA > D.TB : Random.value < 0.5f;
            if ((_fIsA ? D.TA : D.TB) < Tier.Expert)
            {
                _fIsA = !_fIsA;
            }
            _f0 = _fIsA ? A0 : B0;
            _v0 = _fIsA ? B0 : A0;
            // a tile apart and reappearing most of a tile behind: at a battle's zoom the vanish and the
            // turn must cover real ground or the two bodies just blur into one
            Vector2 sf, sv;
            if (!DuelGeo.Stance(_f0, _v0, 1f * K, out sf, out sv) && !DuelGeo.Stance(_f0, _v0, 0.75f * K, out sf, out sv))
            {
                return false;
            }
            _fs = sf;
            _vs = sv;
            Vector2 through = _vs - _fs;
            through = through.sqrMagnitude > 1e-4f ? through.normalized : Vector2.right;
            _behind = Combat.Clamp(_vs + through * 0.8f * K);
            if (!Combat.SafeSpot(_behind))
            {
                _behind = Combat.Clamp(_vs + through * 0.55f * K);
                if (!Combat.SafeSpot(_behind))
                {
                    return false;
                }
            }
            float d = Mathf.Max((_fs - _f0).magnitude, (_vs - _v0).magnitude);
            _tClose = d > 0.25f ? Mathf.Clamp(0.08f + 0.035f * d, 0.1f, 0.22f) : 0.04f;
            return true;
        }

        public override void Start()
        {
            _f = _fIsA ? P : Q;
            _v = _fIsA ? Q : P;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case Close:
                    {
                        float k = Pt / _tClose;
                        Reposition(_f, _f0, _fs, k, _vs);
                        Reposition(_v, _v0, _vs, k, _fs);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(Windup);
                        }
                        break;
                    }
                case Windup:
                    {
                        float k = Mathf.Clamp01(Pt / 0.18f);
                        float e = Anim.OutQuad(k);
                        _f.Pose.Ground = _fs;
                        _v.Pose.Ground = _vs;
                        FaceEach();
                        _f.Pose.Rot = -20f * e;
                        _f.Pose.SX = 1f - 0.08f * e;
                        _f.Pose.SY = 1f + 0.08f * e;
                        Guard(_v, T, 0f, 1f + 0.4f * e);
                        _v.Pose.Rot = -4f * e;
                        if (Enter() && Vis)
                        {
                            Vector2 off = new Vector2(-_f.Fwd * 2f * px, 6f * px);
                            Fx.Glint(_f.BodyPos() + off, px, 0.16f, _f.Pal.Core, 0.9f);
                        }
                        if (k >= 1f)
                        {
                            Next(Vanish);
                        }
                        break;
                    }
                case Vanish:
                    {
                        if (Enter())
                        {
                            // the afterimage keeps swinging where it stood
                            _f.Pose.Rot = 14f;
                            _f.Pose.SX = 1.1f;
                            _f.Pose.SY = 0.92f;
                            if (Vis)
                            {
                                Fx.Afterimage(_f, 0.32f, Fx.WithA(_f.Pal.Ghost, 0.85f), Fx.WithA(_f.Pal.Ghost, 0.6f), _f.Pal.GhostEnd);
                                DuelFx.Puff(_fs, _f.Px, Combat.TileAt(_fs), 2, 10f);
                            }
                            _f.Pose.Hidden = true;
                            _f.Blur = true;
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                        }
                        float k = Mathf.Clamp01(Pt / 0.12f);
                        // ghosts arcing round the defender
                        if (Vis && ((int)(Pt / 0.035f)) > ((int)((Pt - dt) / 0.035f)))
                        {
                            Vector2 around = ArcAround(k);
                            _f.Pose.Ground = around;
                            _f.Pose.Hidden = false;
                            Fx.Afterimage(_f, 0.12f, Fx.WithA(_f.Pal.Ghost, 0.5f), _f.Pal.GhostEnd);
                            _f.Pose.Hidden = true;
                        }
                        _f.Pose.Ground = ArcAround(k);
                        // the defender flinches at the afterimage
                        _v.Pose.Ground = _vs;
                        Face(_v, _fs);
                        _v.Pose.Rot = Mathf.Lerp(-4f, 10f, Anim.OutQuad(k));
                        _v.Pose.SX = 1.04f;
                        _v.Pose.SY = 0.94f;
                        if (k >= 1f)
                        {
                            Next(Behind);
                        }
                        break;
                    }
                case Behind:
                    {
                        if (Enter())
                        {
                            _f.Pose.Hidden = false;
                            _f.Blur = false;
                            _f.Pose.Ground = _behind;
                            Face(_f, _vs);
                            _f.Pose.Rot = 24f;
                            _f.Pose.SX = 1.12f;
                            _f.Pose.SY = 0.9f;
                            Slash(_f, 0, 0.14f);
                            if (Vis)
                            {
                                Fx.FocusLines(_f.BodyPos(), px, Fx.Count(6), 10f * K, 18f * K, 0.1f);
                            }
                        }
                        float k = Mathf.Clamp01(Pt / 0.08f);
                        _v.Pose.Ground = _vs;
                        if (k >= 0.35f)
                        {
                            // the snap turn
                            bool was = _v.Pose.FaceRight;
                            Face(_v, _behind);
                            if (was != _v.Pose.FaceRight && Vis)
                            {
                                Vector2 head = _v.HeadPos() + new Vector2(0f, 2f * px);
                                Fx.Glint(head, px, 0.2f, Color.white, 0.8f);
                            }
                            _v.Pose.Rot = -8f;
                            _v.Pose.SX = 0.94f;
                            _v.Pose.SY = 1.06f;
                        }
                        if (k >= 1f)
                        {
                            Block();
                            Next(After);
                        }
                        break;
                    }
                case After:
                    if (Recoil(dt))
                    {
                        Next(End);
                    }
                    break;
                default:
                    Settle(P, Pt, 0f);
                    Settle(Q, Pt, 0.9f);
                    if (Pt >= Rest(0.2f))
                    {
                        Done = true;
                    }
                    break;
            }
        }

        /// <summary>The way round the defender: an arc along the ground (seen only as ghosts), round its near side.</summary>
        private Vector2 ArcAround(float k)
        {
            Vector2 c = _vs;
            Vector2 a = _fs - c, b = _behind - c;
            // angles on the ground ellipse, so the arc starts and ends exactly on the two spots
            Vector2 ea = new Vector2(a.x, a.y / Art.Ground), eb = new Vector2(b.x, b.y / Art.Ground);
            float a0 = Mathf.Atan2(ea.y, ea.x), a1 = Mathf.Atan2(eb.y, eb.x);
            float sweep = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (Mathf.Abs(sweep) > 0.8f * Mathf.PI || Mathf.Abs(sweep) < 0.1f)
            {
                // straight behind: go round the side nearer the viewer (down the screen)
                sweep = Mathf.Cos(a0) < 0f ? Mathf.PI : -Mathf.PI;
            }
            float ang = a0 + sweep * Anim.InOutSine(k);
            float r = Mathf.Lerp(ea.magnitude, eb.magnitude, k);
            return c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * r;
        }

        private void Block()
        {
            float px = Px;
            Vector2 at = Contact();
            // the feinter's roll against the defender's
            Clash c = D.Resolve(true);
            bool fWins = c.AWins == _fIsA;
            if (c.Kind == ClashKind.Even || (c.Kind == ClashKind.Win && !fWins))
            {
                // blocked just in time: ting (at III the parry's shock runs out along the ground)
                DuelFx.Ting(at, px, _v.Pal.Core, 5, new Vector2(_v.Fwd * -0.5f, 0.8f), 1.2f);
                D.Sound("event:/SFX/HIT/HitMetal");
                Shock(new Clash { Kind = ClashKind.Even }, false);
                Stop(0.07f);
                // both hop apart
                Clash even = new Clash { Kind = ClashKind.Even, Ratio = c.Ratio };
                Aftermath(even, at, 0.6f, 0.6f, 0f);
                return;
            }
            // a late block (the blow gets through), or a far stronger defender parries and flicks the feinter away
            Meet(at, 0.4f * K, c, 6);
            Shock(c);
            Stop(0.05f);
            Aftermath(c, at, 0.6f, fWins ? 0.7f : 1f, Random.Range(0.4f, 0.55f));
        }

        public override void Abort()
        {
            if (_f != null)
            {
                _f.Pose.Hidden = false;
                _f.Blur = false;
            }
        }
    }

    /// <summary>
    /// CIRCLE. The breath between exchanges: they circle each other slowly, eyes locked, dust
    /// swirling round their feet, and each catches the light at the end. It chains straight into
    /// the next exchange.
    ///
    ///   0.0-1.1  both step round the middle (60-120 degrees), always facing each other; a footfall
    ///            puff every step; dust motes orbiting between them; II+: a faint tier glow
    ///   0.8/0.9  a glint on each
    /// </summary>
    internal sealed class Circle : Exchange
    {
        private Vector2 _c;
        private float _r, _a0, _sweep, _dur, _step, _swirl, _onto;
        private bool _ga, _gb;

        public override string Id => "circle";

        public override Form Forms => Form.Evasive;

        public override bool Intense => false;

        public override float Weight()
        {
            if (D.Exchanges < 1)
            {
                return 0f;
            }
            float w = 0.75f;
            if (D.Last == "aerial" || D.Last == "ground_shaker" || D.Last == "spiral" || D.Last == "first_clash" || D.Last == "heavens" || D.Last == "sky")
            {
                w *= 1.6f;
            }
            if (Mathf.Min(D.HpA, D.HpB) < 0.4f)
            {
                w *= 0.5f;   // no time to breathe
            }
            if (D.Flow)
            {
                w *= D.Gods ? 0.3f : 0.45f;   // masters hardly ever stop to circle
            }
            return w;
        }

        public override bool Plan()
        {
            _c = (A0 + B0) * 0.5f;
            float half = (B0 - A0).magnitude * 0.5f;
            Vector2 rel = A0 - _c;
            _a0 = rel.sqrMagnitude > 1e-4f ? Mathf.Atan2(rel.y / Art.Ground, rel.x) : Mathf.PI;
            float dir = Random.value < 0.5f ? 1f : -1f;
            float[] radii = { Mathf.Clamp(half, 0.9f * K, 1.4f * K), 1f * K, 0.75f * K };
            for (int attempt = 0; attempt < 6; attempt++)
            {
                _r = radii[attempt % 3];
                _sweep = dir * Random.Range(70f, 120f) * Mathf.Deg2Rad;
                if (Clear())
                {
                    // a breath: shorter the higher the tiers (masters circle once and go)
                    _dur = Random.Range(0.9f, 1.3f) * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.4f, 1f, D.Brisk));
                    // the step onto the circle takes longer the farther it is
                    float walk = Mathf.Max((Spot(0f, 0f) - A0).magnitude, (Spot(0f, Mathf.PI) - B0).magnitude);
                    _onto = Mathf.Clamp(0.2f + 0.25f * walk / K, 0.2f, 0.45f);
                    return true;
                }
                if (attempt == 2)
                {
                    dir = -dir;
                }
            }
            return false;
        }

        private bool Clear()
        {
            // only a step from where they stand: from farther, the slow walk onto the circle would be a
            // glide (another exchange closes the gap with a dash)
            float most = 1f * K;
            if ((Spot(0f, 0f) - A0).sqrMagnitude > most * most || (Spot(0f, Mathf.PI) - B0).sqrMagnitude > most * most)
            {
                return false;
            }
            for (int i = 0; i <= 6; i++)
            {
                float k = i / 6f;
                if (!Combat.SafeSpot(Spot(k, 0f)) || !Combat.SafeSpot(Spot(k, Mathf.PI)))
                {
                    return false;
                }
            }
            // and the walk from where they stand to the circle
            return Combat.ClearPath(A0, Spot(0f, 0f)) && Combat.ClearPath(B0, Spot(0f, Mathf.PI));
        }

        private Vector2 Spot(float k, float offset)
        {
            float a = _a0 + offset + _sweep * k;
            return Combat.Clamp(_c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * _r);
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            float k = Mathf.Clamp01(T / _dur);
            float e = Anim.InOutSine(k);
            // ease onto the circle from where they stood
            float onto = Anim.OutQuad(Mathf.Clamp01(T / Mathf.Max(0.2f, _onto)));
            P.Pose.Ground = Vector2.Lerp(A0, Spot(e, 0f), onto);
            Q.Pose.Ground = Vector2.Lerp(B0, Spot(e, Mathf.PI), onto);
            FaceEach();
            // slow, deliberate steps
            float steps = T * 7f;
            float bob = Mathf.Abs(Mathf.Sin(steps));
            foreach (Duelist m in new[] { P, Q })
            {
                m.Pose.Height = 0.02f * K * bob;
                m.Pose.SX = 1.04f;
                m.Pose.SY = 0.95f + 0.03f * bob;
                m.Pose.Rot = 6f;
            }
            if (L >= 2)
            {
                float g = 0.08f + 0.05f * Mathf.Sin(T * 6f);
                P.Glow = Fx.WithA(P.Pal.Edge, g);
                Q.Glow = Fx.WithA(Q.Pal.Edge, g);
            }
            int s = (int)(steps / Mathf.PI);
            if (s > (int)_step)
            {
                _step = s;
                DuelFx.Puff(P.Pose.Ground, P.Px, Combat.TileAt(P.Pose.Ground), 1, 5f);
                DuelFx.Puff(Q.Pose.Ground, Q.Px, Combat.TileAt(Q.Pose.Ground), 1, 5f);
            }
            _swirl -= dt;
            if (_swirl <= 0f)
            {
                _swirl = 0.3f;
                DuelFx.Swirl(_c, px, Combat.TileAt(_c), _r * 0.8f, 3, 0.9f, _sweep > 0f ? 2.2f : -2.2f);
            }
            if (!_ga && k >= 0.78f)
            {
                _ga = true;
                Glint(P);
            }
            if (!_gb && k >= 0.88f)
            {
                _gb = true;
                Glint(Q);
            }
            if (k >= 1f)
            {
                P.Glow = Q.Glow = DuelFx.Clear;
                Done = true;
            }
        }

        private void Glint(Duelist m)
        {
            if (!Vis)
            {
                return;
            }
            Vector2 off = new Vector2(m.Fwd * 3f * m.Px, m.Pose.PivotUp * 1.4f * m.Px);
            Particle p = Fx.Glint(m.BodyPos() + off, m.Px, 0.22f, m.Pal.Core, 0.9f);
            if (p != null)
            {
                p.Step = (q, d) => q.Pos = m.BodyPos() + off;
            }
        }

        public override void Abort()
        {
            if (P != null)
            {
                P.Glow = DuelFx.Clear;
            }
            if (Q != null)
            {
                Q.Glow = DuelFx.Clear;
            }
        }
    }
}
