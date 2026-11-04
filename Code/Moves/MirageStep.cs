using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// MIRAGE STEP. The defender reads the blow before it lands and is simply not there: a
    /// flat-colour copy of it stands where it was and takes the hit, while the real one is already
    /// somewhere else. Level I steps aside; level II is behind the attacker, facing it, with a free
    /// counter; level III leaves three mirages that daze everyone around them and reappears wherever
    /// the next combo is best.
    ///
    /// Beat sheet, level II (game seconds at x1):
    ///
    ///   0.00       a full-alpha silhouette (tier-tinted) stays where it stood; the unit vanishes
    ///              (hidden, intangible) and one speed line points to where it is going
    ///   0.05       it reappears 0.75 tiles behind the attacker (clear of its sprite), facing it: a
    ///              landing spring, heel dust (behind it, never over its legs), a small "!" and a
    ///              glint over the attacker's head
    ///   0.12       the blow lands on the silhouette, which flashes white and bursts into a puff of
    ///              dust and four sparks; the attack whiffs
    ///   0.05-0.32  springs from the landing squash into a ready stance; chain window (Close on the
    ///              attacker: the Brain chains a counter with no thinking delay)
    ///
    ///   Level I    a white silhouette; reappears 2 tiles to the side (further back from area
    ///              attacks), no link.
    ///   Level III  three silhouettes in a triangle: the struck one ripples (the blow passes
    ///              through a mirage) and bursts at 0.32 s; the other two shimmer through the daze
    ///              and flicker out at 0.62 / 0.76 s (they never outstay the exchange); a ring spreads
    ///              3 tiles and every enemy inside stops dead, dazed 0.4 s (no white flash: they
    ///              must not read as mirages; a small grey "?" over up to three of them); the
    ///              unit reappears behind the attacker, or behind the most
    ///              wounded enemy within 4 tiles, which gets the "!", with focus lines and a ground
    ///              ring. Close on whichever it chose.
    ///   Retreat    (proactive, at 20% health or less with two enemies on it) the silhouette just
    ///              fades, and the unit reappears 4 tiles towards its allies, facing the danger.
    ///   Sidestep   (an ordinary melee blow or a flow strike, at half the reaction chance) the I or
    ///              II version.
    ///
    /// By tier: the higher it is, the sooner it is ready to answer: the move ends at 0.32 s below
    /// Expert, 0.27 s for an Expert, 0.18 s for a Master, 0.15 s for Grandmasters and Gods, and the
    /// counter follows at once (the mirages keep their own timing). If the one it is behind is
    /// still mid-technique (a counter can't touch it yet; a flow strike can be cut short), it
    /// holds, coiled, and goes the moment that one is free (at most 0.57 s in all). A
    /// Grandmaster's daze reaches 3.45 tiles, a God's 4.2
    /// and holds 0.5 s. No ground damage: a dodge leaves no mark.
    ///
    /// The silhouettes are particles that look after themselves: they outlive the move and need
    /// nothing from the unit, so a defender that dies or is picked up mid-step leaves nothing behind.
    /// </summary>
    internal sealed class MirageStep : Move
    {
        internal enum Kind
        {
            React,
            Sidestep,
            Retreat
        }

        private const float THide = 0.05f;
        private const float TEnd = 0.32f;
        /// <summary>How much longer than the full beat it waits, coiled, for a busy attacker to be counterable.</summary>
        private const float HoldMax = 0.25f;
        private const float PopAt = 0.12f;
        /// <summary>Level III: the struck mirage bursts, the two others hold a moment longer and dissolve.</summary>
        private const float TrinityPop = 0.32f;
        private const float TrinitySide = 0.62f;
        private const float ConfuseRadius = 3f;
        private const float TrinityReach = 4f;
        private const float BehindReach = 6f;
        private const int MaxDazeMarks = 3;
        /// <summary>Where a glyph sits over a head (sprite pixels above the feet); higher over a stunned one.</summary>
        private const float MarkTop = 13f;
        private const float MarkTopDazed = 15.5f;

        private static readonly float[] Fan = { 0f, 35f, -35f, 70f, -70f };
        private static readonly Color Daze = new Color(0.84f, 0.84f, 0.88f, 1f);
        private static Sprite _bang, _question;

        private readonly Kind _kind;
        private readonly int _variant;
        private readonly Actor _att;
        private readonly long _attId;
        private readonly Vector2 _from;
        private readonly Vector2 _axis;
        private readonly Palette _pal;
        private Vector2 _to;
        private Vector2 _look;
        private bool _hasLook;
        private Actor _link;
        private long _linkId = -1;
        private bool _hidden = true;
        private bool _setup, _landed;
        private float _landT;

        /// <summary>When it is ready again: the higher the tier, the sooner the counter comes.</summary>
        private readonly float _tEnd;
        /// <summary>Level III's daze: how far it reaches and how long it holds (wider and longer for a God).</summary>
        private readonly float _dazeR, _dazeT;

        /// <summary>Mirage Steps since the game started (dev log).</summary>
        internal static int Started;

        private MirageStep(Actor a, Kind kind, int variant, Actor att, Vector2 to, Actor link, int level) : base(a)
        {
            Level = level;
            _kind = kind;
            _variant = variant;
            _att = att;
            _attId = att != null ? att.Sid() : -1L;
            _from = a.current_position;
            _to = to;
            _link = link;
            _linkId = link != null ? link.Sid() : -1L;
            _pal = Palette.For(a, level);
            _axis = Axis(a, att);
            Tier tier = Tiers.Of(a);
            // a Master is out of the blow and ready to answer in under 0.2 s (the mirages run on
            // their own, so nothing of the read is cut short)
            _tEnd = Mathf.Max(THide + 0.1f, TEnd * FlashStep.Tempo(tier));
            _dazeR = ConfuseRadius * (tier >= Tier.God ? 1.4f : tier >= Tier.Grandmaster ? 1.15f : 1f);
            _dazeT = tier >= Tier.God ? 0.5f : 0.4f;
            Pose.Hidden = true;
            Pose.HideShadow = true;
        }

        /// <summary>Nothing can touch it while it is gone.</summary>
        public override bool Intangible => _hidden;

        // ------------------------------------------------------------ helpers --

        private static float KOf(Actor a)
        {
            return Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Where the attacker really is: on the ground under its move, if it is performing one.</summary>
        private static Vector2 GroundOf(Actor att, Vector2 fallback)
        {
            if (att == null)
            {
                return fallback;
            }
            return Moves.TryGet(att, out Move m) ? m.Pose.Ground : att.current_position;
        }

        /// <summary>From the attacker towards the defender (or the way the defender faces, if they overlap).</summary>
        private static Vector2 Axis(Actor def, Actor att)
        {
            Vector2 p = def.current_position;
            Vector2 axis = p - GroundOf(att, p);
            if (axis.sqrMagnitude < 0.0004f)
            {
                axis = new Vector2(def.flip ? -1f : 1f, 0f);
            }
            return axis.normalized;
        }

        private static bool Gone(Actor v, long id)
        {
            return v == null || !v.isAlive() || v.Sid() != id || v.isInMagnet() || v.isInsideSomething();
        }

        // ------------------------------------------------------------- start --

        /// <summary>
        /// The defender steps out of a blow from 'att'. Variant 1 to the side, 2 behind the attacker,
        /// 3 the trinity. False (nothing happens) if it can't move or there is nowhere safe to go.
        /// </summary>
        internal static bool TryStart(Actor def, Actor att, int variant, int level, bool area, Kind kind, Technique tech)
        {
            if (def == null || !Moves.CanPerform(def))
            {
                return false;
            }
            if (!Destination(def, att, variant, area, out Vector2 to, out Actor link))
            {
                return false;
            }
            if (variant == 2 && link == null)
            {
                variant = 1;   // nowhere behind it: a plain sidestep
            }
            MirageStep m = new MirageStep(def, kind, variant, att, to, link, level) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            m.Opening();
            Started++;
            Dev.Watch(Vector2.Lerp(m._from, to, 0.5f) + new Vector2(0f, 1.2f));
            if (Dev.On) Dev.Trace("mirage " + m.Id + " " + kind + " v" + variant + " L" + level + (area ? " area" : "") + " -> " + (link != null ? "behind " + link.asset.id : "aside"));
            return true;
        }

        /// <summary>A blink away from danger (no blow to dodge): 4 tiles towards its allies, facing 'look'.</summary>
        internal static bool Retreat(Actor def, Vector2 to, Vector2 look, int level, Technique tech)
        {
            if (def == null || !Moves.CanPerform(def) || !Combat.SafeSpot(to))
            {
                return false;
            }
            MirageStep m = new MirageStep(def, Kind.Retreat, 1, null, to, null, level) { Tech = tech };
            m._look = look;
            m._hasLook = true;
            if (!Moves.Start(m))
            {
                return false;
            }
            m.Opening();
            Started++;
            Dev.Watch(Vector2.Lerp(m._from, to, 0.5f) + new Vector2(0f, 1.2f));
            return true;
        }

        /// <summary>Where it reappears: behind the attacker (II, III) if there is room, else to the side.</summary>
        private static bool Destination(Actor def, Actor att, int variant, bool area, out Vector2 to, out Actor link)
        {
            link = null;
            Vector2 p = def.current_position;
            Vector2 axis = Axis(def, att);
            float kd = KOf(def);
            if (variant >= 2 && att != null && att.isAlive())
            {
                Vector2 ap = GroundOf(att, p);
                if (Vector2.Distance(p, ap) <= (variant >= 3 ? TrinityReach : BehindReach) && Behind(ap, BackOf(att, ap, p, axis), kd, KOf(att), out to))
                {
                    link = att;
                    return true;
                }
            }
            return Sideways(p, axis, kd, area, out to);
        }

        /// <summary>
        /// Which way "behind the attacker" is: the far side from where the defender stood, unless the
        /// attacker has its back turned on the defender (a dash that already went through, Flash
        /// Step's zanshin): then right at its back, so it has to whirl round to find it.
        /// </summary>
        private static Vector2 BackOf(Actor att, Vector2 ap, Vector2 p, Vector2 axis)
        {
            if (Moves.TryGet(att, out Move m))
            {
                float dx = p.x - ap.x;
                if (Mathf.Abs(dx) > 0.1f && (m.Pose.FaceRight ? dx < 0f : dx > 0f))
                {
                    return axis;
                }
            }
            return -axis;
        }

        /// <summary>
        /// How far behind a unit it reappears: three quarters of a tile between two humans (at half
        /// a tile their sprites overlapped and the "behind you" read was lost), more for big ones.
        /// </summary>
        private static float BackOff(float kd, float ka)
        {
            return 0.45f + 0.15f * (kd + ka);
        }

        /// <summary>Just behind the attacker (at 'ap'), along 'dir' (fanning out if that spot is unsafe).</summary>
        private static bool Behind(Vector2 ap, Vector2 dir, float kd, float ka, out Vector2 to)
        {
            float back = BackOff(kd, ka);
            for (int i = 0; i < Fan.Length; i++)
            {
                Vector2 q = ap + Rotate(dir, Fan[i]) * back;
                if (Combat.SafeSpot(q))
                {
                    to = q;
                    return true;
                }
            }
            to = ap;
            return false;
        }

        /// <summary>Two tiles to one side (a little back from the blow; further back from an area attack).</summary>
        private static bool Sideways(Vector2 p, Vector2 axis, float kd, bool area, out Vector2 to)
        {
            Vector2 perp = new Vector2(-axis.y, axis.x);
            float first = Random.value < 0.5f ? 1f : -1f;
            float len = 2f * Mathf.Clamp(kd, 0.85f, 1.4f);
            float back = area ? 1f : 0.3f;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? first : -first;
                    Vector2 q = pass == 0 ? p + perp * side * len + axis * back
                        : pass == 1 ? p + perp * side * len * 0.65f + axis * back
                        : p + Rotate(axis, side * 30f) * len;
                    if (Combat.SafeSpot(q))
                    {
                        to = q;
                        return true;
                    }
                }
            }
            to = p;
            return false;
        }

        /// <summary>The instant it reads the blow: the silhouette(s) stay, the unit is gone.</summary>
        private void Opening()
        {
            float px = Px;
            if (Fx.OnScreen(_from, 0.4f))
            {
                Color c = Level <= 1 ? Color.white : Color.Lerp(_pal.Edge, Color.white, 0.25f);
                if (_variant >= 3)
                {
                    Trinity(px, c);
                }
                else if (_kind == Kind.Retreat)
                {
                    Decoy(Vector2.zero, Pose.FaceRight, c, 0f, -1f, 0.3f, false);   // nothing to take: it just fades
                }
                else
                {
                    Decoy(Vector2.zero, Pose.FaceRight, c, 0f, PopAt, PopAt, true);  // takes the blow and bursts
                }
            }
            Pose.Ground = _to;   // already there, unseen
            Pose.Hidden = true;
            Pose.HideShadow = true;
        }

        /// <summary>Level III: three mirages in a triangle around where it stood, and the field they daze.</summary>
        private void Trinity(float px, Color c)
        {
            float k = K;
            Vector2 perp = new Vector2(-_axis.y, _axis.x);
            Decoy(Vector2.zero, Pose.FaceRight, c, 0f, PopAt, TrinityPop, true);
            Color side = Fx.WithA(c, 0.8f);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                Vector2 off = perp * s * 0.9f * k + _axis * 0.3f * k;
                Vector2 at = _from + off;
                if (!Combat.SafeSpot(at))
                {
                    continue;   // never a mirage standing in water or a wall
                }
                bool face = _att != null && Mathf.Abs(_att.current_position.x - at.x) > 0.05f ? _att.current_position.x > at.x : Pose.FaceRight;
                // they hold through the daze, then dissolve one after the other (never outstaying the exchange)
                Decoy(off, face, side, 0.02f * (i + 1), -1f, TrinitySide + 0.14f * i, false);
            }
            Fx.Ring(_from, px, 0.5f, 4f, Mathf.Min(140f, _dazeR / px), 1, 1, FxLayer.Ground, 4, Art.Ground, 2f,
                Fx.WithA(c, 0.6f), Fx.WithA(c, 0.25f), Fx.WithA(c, 0f));
        }

        /// <summary>
        /// A flat-colour copy of the unit where it stood ('offset' world units away), facing 'faceRight'.
        /// It shimmers; at 'hitAt' (if >= 0) it is struck: a white flash and a ripple; at 'endAt' it
        /// bursts into dust and sparks ('pop') or fades. It runs on its own: nothing of the move is kept.
        /// </summary>
        private Particle Decoy(Vector2 offset, bool faceRight, Color c, float delay, float hitAt, float endAt, bool pop)
        {
            Pose keep = Pose;
            Pose.Ground = _from;
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            Pose.FaceRight = faceRight;
            Pose.Hidden = false;
            Particle p = Fx.Afterimage(this, endAt + 0.3f, c);
            Pose = keep;
            if (p == null)
            {
                return null;
            }
            p.Pos += offset;
            p.Delay = delay;
            p.Held = true;
            p.Grad = null;
            p.R.color = c;
            float px = Px;
            Vector2 home = p.Pos;
            Vector2 body = _from + offset + new Vector2(0f, Pose.PivotUp * A.actor_scale);
            Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(_from + offset)), 0.35f);
            Color spark = Level <= 1 ? new Color(0.85f, 0.85f, 0.88f, 1f) : _pal.Edge;
            float sx = p.SX, sy = p.SY;
            int seed = Random.Range(0, 7);
            bool ended = false;
            p.Step = (q, d) =>
            {
                float age = q.Age;
                if (!ended)
                {
                    // a mirage: full colour, a faint flicker
                    Color col = Fx.WithA(c, c.a * (0.9f + 0.1f * ((((int)(age * 30f)) + seed) & 1)));
                    Vector2 jitter = Vector2.zero;
                    if (hitAt >= 0f && age >= hitAt)
                    {
                        float h = age - hitAt;
                        if (h < 0.05f)
                        {
                            col = Color.white;   // struck
                        }
                        if (h < 0.12f)
                        {
                            jitter = new Vector2(((int)(age * 60f) & 1) == 0 ? px : -px, 0f);   // the blow passes through it
                        }
                    }
                    q.Pos = home + jitter;
                    q.R.color = col;
                    if (age >= endAt)
                    {
                        ended = true;
                        if (pop)
                        {
                            Burst(body, px, dust, spark);
                        }
                    }
                    return;
                }
                float k = Mathf.Clamp01((age - endAt) / (pop ? 0.1f : 0.2f));
                q.Pos = home;
                if (pop)
                {
                    // swells as it bursts, white, and is gone
                    q.SX = sx * (1f + 0.25f * k);
                    q.SY = sy * (1f + 0.12f * k);
                    q.R.color = Fx.WithA(Color.white, 1f - k);
                }
                else
                {
                    // a mirage flickers out, like heat haze
                    float flick = (((int)(age * 40f)) & 1) == 0 ? 1f : 0.35f;
                    q.R.color = Fx.WithA(c, c.a * (1f - k) * flick);
                }
                if (k >= 1f)
                {
                    q.Kill();
                }
            };
            return p;
        }

        /// <summary>A silhouette bursting: a puff of dust and four sparks.</summary>
        private static void Burst(Vector2 body, float px, Color dust, Color spark)
        {
            if (!Fx.OnScreen(body, 0.3f))
            {
                return;
            }
            // behind the units: the attacker is usually standing right on the mirage it struck,
            // and a puff over it hid its whiff (the sparks stay on top)
            Particle puff = Fx.Spawn(Art.Dust[2], body, px, 0.4f, FxLayer.Behind, 6);
            puff?.Moving(Vector2.zero, 4f * px, 3f).Sized(0.6f, 1.2f).Color(Fx.WithA(dust, 0.85f), Fx.WithA(dust, 0.45f), Fx.WithA(dust, 0f));
            int n = Fx.Count(4);
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.Range(0f, 0.6f)) / n * Mathf.PI * 2f;
                Particle s = Fx.Spawn(Art.Sparks[Random.Range(0, 2)], body, px, Random.Range(0.16f, 0.24f), FxLayer.Top, 8);
                if (s == null)
                {
                    break;
                }
                s.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(55f, 90f) * px, 0f, 6f);
                s.FaceVel = true;
                s.Color(Color.white, spark, Fx.WithA(spark, 0f));
            }
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (!_setup)
            {
                Setup();
            }
            if (T < THide)
            {
                _hidden = true;
                Pose.Hidden = true;
                Pose.HideShadow = true;
                Pose.Ground = _to;
                return;
            }
            if (!_landed)
            {
                Land();
            }
            // the landing spring, settling into a ready stance (a fighter about to counter crouches a little)
            float t = T - _landT;
            float amp = Anim.Spring(t, 10f, 20f);
            bool ready = _variant >= 2 && _kind != Kind.Retreat;
            // the one it reappeared behind is still finishing its blow (a counter can't touch a
            // unit that is mid-move): it stays coiled behind it and goes the moment it is free,
            // never more than a quarter second past the full beat (the chain window covers the rest)
            bool hold = ready && T < TEnd + HoldMax && LinkBusy();
            float stance = !ready ? 1f : hold ? 0.97f : 0.97f + 0.03f * Anim.Smooth(_tEnd - 0.1f, _tEnd, T);
            Pose.Ground = _to;
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.SX = 1f + 0.14f * amp;
            Pose.SY = stance - 0.16f * amp;
            if (T >= _tEnd && !hold)
            {
                Finish();
            }
        }

        /// <summary>The one it will counter is alive and still busy with a move of its own.</summary>
        private bool LinkBusy()
        {
            return !Gone(_link, _linkId) && Moves.Locked(_link);
        }

        /// <summary>The first tick: the trinity's choices and its daze, and the speed line to where it is going.</summary>
        private void Setup()
        {
            _setup = true;
            if (_variant >= 3 && _kind == Kind.React)
            {
                BestSpot();
            }
            Pose.Ground = _to;
            Vector2 mid = Vector2.Lerp(_from, _to, 0.5f);
            // dev captures frame the mirage, where it reappears and the one it fooled
            Dev.Watch((_from + _to + (Gone(_att, _attId) ? mid : GroundOf(_att, mid))) / 3f + new Vector2(0f, 1.2f));
            if (Fx.OnScreen(mid, 0.4f))
            {
                float px = Px;
                Vector2 up = new Vector2(0f, Pose.PivotUp * A.actor_scale);
                Color c = Level <= 1 ? new Color(1f, 1f, 1f, 0.8f) : Fx.WithA(_pal.Edge, 0.85f);
                Kit.Line(_from + up, _to + up, px, 0.1f, 1, FxLayer.Top, 3, c, Fx.WithA(c, 0f));
            }
        }

        /// <summary>
        /// Level III: behind the attacker, or behind the most wounded enemy within 4 tiles if that is
        /// the better opening; then every enemy within 3 tiles of the mirages is dazed.
        /// </summary>
        private void BestSpot()
        {
            // a God's daze reaches further than where it may reappear
            List<Actor> near = Combat.FoesAround(A, _from, Mathf.Max(TrinityReach, _dazeR));
            Actor weakest = null;
            float wHp = 2f;
            float reach2 = TrinityReach * TrinityReach;
            foreach (Actor u in near)
            {
                // (not one the blow that set this off has just felled: alive until the game buries it)
                if (u == _att || !u.hasHealth() || !Combat.Allowed(A, u) || (u.current_position - _from).sqrMagnitude > reach2)
                {
                    continue;
                }
                float hp = Combat.HpPct(u);
                if (hp < wHp)
                {
                    wHp = hp;
                    weakest = u;
                }
            }
            float behindScore = _link != null ? 1f : 0f;
            float weakScore = weakest != null ? 0.55f + 1.2f * (1f - wHp) : 0f;
            if (weakest != null && weakScore > behindScore && BehindOf(weakest, out Vector2 spot) && Vector2.Distance(spot, _from) <= TrinityReach + 0.5f)
            {
                _to = spot;
                _link = weakest;
                _linkId = weakest.Sid();
            }
            // the mirages: everyone close by stops dead, dazed. A small "?" over a few of them
            // (not every head: a row of glyphs would bury the action), never over the one it
            // reappears behind, which gets the "!"
            int marks = 0;
            float r2 = _dazeR * _dazeR;
            foreach (Actor u in near)
            {
                if ((u.current_position - _from).sqrMagnitude > r2 || Gone(u, u.Sid()))
                {
                    continue;
                }
                u.stopMovement();
                if (Combat.Allowed(A, u))
                {
                    // no white flash (makeStunned flashes every dazed unit white on the very frame
                    // the mirages appear: the enemies read as mirages themselves)
                    Combat.Daze(u, _dazeT);
                    // above the game's stun stars, popping one after the other (a row of them
                    // appearing at once read as one dark block)
                    if (marks < MaxDazeMarks && u != _link && Mark(u, Question(), Daze, 0.46f, 2f, MarkTopDazed, 0.04f + 0.06f * marks) != null)
                    {
                        marks++;
                    }
                }
                else
                {
                    u.makeWait(_dazeT);   // too big to daze: it just hesitates
                }
            }
        }

        /// <summary>Half a tile behind an enemy, as seen from where the defender stood.</summary>
        private bool BehindOf(Actor v, out Vector2 spot)
        {
            Vector2 vp = v.current_position;
            Vector2 axis = vp - _from;
            axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : _axis;
            float back = BackOff(K, KOf(v));
            for (int i = 0; i < Fan.Length; i++)
            {
                Vector2 q = vp + Rotate(axis, Fan[i]) * back;
                if (Combat.SafeSpot(q))
                {
                    spot = q;
                    return true;
                }
            }
            spot = vp;
            return false;
        }

        /// <summary>It reappears: facing its target, a spring, a puff; the attacker gets a "!".</summary>
        private void Land()
        {
            _landed = true;
            _landT = T;
            _hidden = false;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            Pose.Ground = _to;
            Vector2? look = null;
            if (!Gone(_link, _linkId))
            {
                look = _link.current_position;
            }
            else if (_hasLook)
            {
                look = _look;
            }
            else if (!Gone(_att, _attId))
            {
                look = _att.current_position;
            }
            if (look.HasValue && Mathf.Abs(look.Value.x - _to.x) > 0.05f)
            {
                Pose.FaceRight = look.Value.x > _to.x;
            }
            if (!Fx.OnScreen(_to, 0.4f))
            {
                return;
            }
            float px = Px;
            FlashStep.HeelDust(_to, _from - _to, px, Combat.TileAt(_to), K);
            if (_variant >= 2 && _kind != Kind.Retreat && !Gone(_link, _linkId))
            {
                // "!": it is behind you. Alone: a glint on the head under it stacked with it
                // into a dagger shape; over a dazed one it sits clear of the game's stun stars
                Mark(_link, Bang(), _pal.Core, 0.4f, 2f, _variant >= 3 ? MarkTopDazed : MarkTop);
            }
            if (_variant >= 3)
            {
                Fx.FocusLines(BodyPos(), px, Fx.Count(8), 9f * K, 18f * K, 0.12f);
                Fx.Ring(_to, px, 0.25f, 2f, 9f * K, 1, 1, FxLayer.Ground, 5, Art.Ground, 2.5f,
                    Fx.WithA(_pal.Edge, 0.9f), Fx.WithA(_pal.Edge, 0f));
            }
        }

        private void Finish()
        {
            Pose.SX = Pose.SY = 1f;
            EndLink = Link.None;
            EndTarget = null;
            if (_variant >= 2 && _kind != Kind.Retreat && !Gone(_link, _linkId) && _link.hasHealth() && Combat.IsFoe(A, _link))
            {
                // a free counter: the Brain chains from here
                EndLink = Link.Close;
                EndTarget = _link;
            }
            Finished = true;
        }

        public override void Abort()
        {
            _hidden = false;
        }

        // ------------------------------------------------------------- marks --

        /// <summary>
        /// A little glyph popping up over a unit's head and following it (a "!", a "?"), its
        /// middle 'top' sprite pixels above the feet, after 'delay' seconds.
        /// </summary>
        private static Particle Mark(Actor v, Sprite glyph, Color c, float life, float rise, float top = 13f, float delay = 0f)
        {
            if (v == null || !v.is_visible || glyph == null)
            {
                return null;
            }
            float px = Mathf.Clamp(v.actor_scale, 0.04f, 0.4f);
            Vector2 at = v.current_position + new Vector2(0f, v.position_height + top * px);
            Particle p = Fx.Spawn(glyph, at, px, life, FxLayer.Top, 12);
            if (p == null)
            {
                return null;
            }
            p.Delay = delay;
            p.Sized(1.25f, 1f);   // pops in a little big and settles
            p.Color(c, c, c, Fx.WithA(c, 0f));
            long id = v.Sid();
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    q.Pos = v.current_position + new Vector2(0f, v.position_height + (top + rise * q.K) * px);
                }
            };
            return p;
        }

        private static Sprite Bang()
        {
            return _bang ??= Glyph(new[] { "#", "#", "#", ".", "#" }, "sakuga_mirage_bang");
        }

        private static Sprite Question()
        {
            return _question ??= Glyph(new[] { "##.", "..#", ".#.", "...", ".#." }, "sakuga_mirage_question");
        }

        /// <summary>A tiny pixel glyph ('#' rows, top first) with a dark one-pixel outline, so it reads on any ground.</summary>
        private static Sprite Glyph(string[] rows, string name)
        {
            int h = rows.Length, w = rows[0].Length;
            Canvas c = new Canvas(w + 2, h + 2);
            bool Fill(int x, int y)
            {
                return x >= 0 && y >= 0 && x < w && y < h && rows[h - 1 - y][x] == '#';
            }
            for (int y = -1; y <= h; y++)
            {
                for (int x = -1; x <= w; x++)
                {
                    if (Fill(x, y))
                    {
                        c.Set(x + 1, y + 1, 1f, 1f);
                    }
                    else if (Fill(x - 1, y) || Fill(x + 1, y) || Fill(x, y - 1) || Fill(x, y + 1))
                    {
                        c.Set(x + 1, y + 1, 0.12f, 0.9f);
                    }
                }
            }
            return c.ToSprite(name, new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>
    /// Mirage Step as a technique. Entry: Expert. Reactive: when an enemy technique (or, at half
    /// the chance and never with the trinity, an ordinary melee blow or a master's flow of blows)
    /// is about to land, the defender may read it and be somewhere else. Chance: the tier's
    /// reaction (Expert .25, Master .45, Grandmaster .6, God .75;
    /// a bare trait .3), -0.15 against a higher tier, +0.1 against a lower one, +0.1 against area
    /// attacks. A read is not a reflex that fires on every blow, though: never two within 3.5 s,
    /// half as likely within 10 s of the last one, half again against the very attacker it just
    /// fooled (it has seen the trick), and a counter thrown from behind by one that has just
    /// mirage-stepped it is the hardest of all to read (x0.35). So two masters trade one evade and
    /// a counter, not a string of whiffs. Planned too, as a last resort: at 20% health or less with
    /// two enemies on it, it blinks 4 tiles towards its allies.
    /// </summary>
    internal sealed class MirageStepTech : Technique
    {
        public override string Id => "mirage_step";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;

        /// <summary>The blow meets a mirage: it is already somewhere else (behind the attacker at II).</summary>
        public override Form Forms => Form.Evasive | Form.Clone | Form.Teleport | Form.Counter;

        /// <summary>Single blows, dashes, lines and areas, shots, counters: almost anything meets a mirage (not a grab).</summary>
        public override Form Answers => Form.Strike | Form.Dash | Form.Line | Form.Area | Form.Projectile | Form.Flurry | Form.Teleport | Form.Counter;

        public override bool Reactive => true;
        public override float[] ManaCost => new[] { 10f, 16f, 24f };
        public override float[] Cooldown => new[] { 9f, 9f, 9f };
        public override string Icon => "ui/Icons/sakuga/iconMirageStep";

        private static readonly float[] Fan = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };
        private static MirageStepTech _instance;

        /// <summary>Never two reads closer than this (world seconds), whatever the tier's cooldowns.</summary>
        private const double MinBetween = 3.5;

        private struct Evade
        {
            public long Att;
            public double At;
        }

        /// <summary>The last read of each defender (by unit id): whom it fooled, and when.</summary>
        private static readonly Dictionary<long, Evade> _evades = new Dictionary<long, Evade>();

        private sealed class StepPlan
        {
            public bool Retreat;
            public Vector2 To, Look;
            public Actor Att;
        }

        /// <summary>The chance that 'def' reads a blow from 'att'.</summary>
        internal static float Chance(Actor def, Actor att, bool area)
        {
            Tier td = Tiers.Of(def);
            TierInfo info = Tiers.Info(td);
            float c = info != null ? info.React : 0.3f;
            Tier ta = Tiers.Of(att);
            if (ta > td)
            {
                c -= 0.15f;
            }
            else if (ta < td)
            {
                c += 0.1f;
            }
            if (area)
            {
                c += 0.1f;
            }
            c *= Personas.Of(def).ReadSkill;
            return Mathf.Clamp(c, 0.05f, 0.95f);
        }

        /// <summary>
        /// How readable the blow still is (0..1) given the last reads: a fighter that has just
        /// mirage-stepped is still catching its breath, an attacker it has already fooled is not
        /// fooled as easily twice, and the counter of one that has just fooled it comes from behind.
        /// </summary>
        internal static float Freshness(Actor def, Actor att)
        {
            double now = Fighter.Now;
            float k = 1f;
            if (_evades.TryGetValue(def.Sid(), out Evade e))
            {
                double since = now - e.At;
                if (since >= 0.0 && since < MinBetween)
                {
                    return 0f;
                }
                if (since >= 0.0 && since < 10.0)
                {
                    k *= 0.5f;
                }
                if (e.Att == att.Sid() && since >= 0.0 && since < 15.0)
                {
                    k *= 0.5f;
                }
            }
            if (_evades.TryGetValue(att.Sid(), out Evade theirs) && theirs.Att == def.Sid() && now - theirs.At >= 0.0 && now - theirs.At < 2.5)
            {
                k *= 0.35f;
            }
            return k;
        }

        private static void Remember(Actor def, Actor att)
        {
            if (_evades.Count > 512)
            {
                _evades.Clear();   // a long session: forget the old reads rather than grow forever
            }
            _evades[def.Sid()] = new Evade { Att = att != null ? att.Sid() : -1L, At = Fighter.Now };
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (defender == null || attacker == null || defender == attacker)
            {
                return Contact.Hit;
            }
            // read (Reactions: tier, persona, memory, a counter's depth; a master's flow of blows is
            // read like an ordinary blow, at half the chance), and never answered with the trinity
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            bool light = incoming != null && incoming.Interruptible;
            // its own rhythm: one that has just mirage-stepped is catching its breath; an attacker it
            // already fooled is not fooled as easily twice
            if (Random.value >= Freshness(defender, attacker))
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            int variant = Mathf.Clamp(level, 1, light ? 2 : 3);
            if (!MirageStep.TryStart(defender, attacker, variant, level, area, light ? MirageStep.Kind.Sidestep : MirageStep.Kind.React, this))
            {
                return Contact.Hit;
            }
            Remember(defender, attacker);
            return Contact.Evaded;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Forced)
            {
                // shown on demand: as if the nearest enemy had just swung at it
                Actor att = c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (att != null && att.isAlive() && Combat.IsFoe(c.Self, att))
                {
                    return new Plan { Target = att, Point = att.current_position, Count = 1, Score = 1f, Data = new StepPlan { Att = att } };
                }
                if (!RetreatSpot(c, 4f, out Vector2 to0, out Vector2 look0, out _))
                {
                    return null;
                }
                return new Plan { Point = to0, Score = 1f, Data = new StepPlan { Retreat = true, To = to0, Look = look0 } };
            }
            if (c.HpPct > 0.2f)
            {
                return null;
            }
            int adj = c.Surrounding(1.75f);
            if (adj < 2 || !RetreatSpot(c, 4f, out Vector2 to, out Vector2 look, out bool allies))
            {
                return null;
            }
            return new Plan
            {
                Point = to,
                Score = c.D * 2f + 12f * adj + (allies ? 10f : 0f),
                Data = new StepPlan { Retreat = true, To = to, Look = look }
            };
        }

        /// <summary>A safe spot 'len' tiles towards its allies (or away from the danger) with fewer than two enemies on it.</summary>
        private static bool RetreatSpot(Ctx c, float len, out Vector2 to, out Vector2 threat, out bool allies)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 3f; i++)
            {
                sum += c.Enemies[i].current_position;
                n++;
            }
            threat = n > 0 ? sum / n : c.Pos + new Vector2(c.Self.flip ? 1f : -1f, 0f);
            Vector2 want = Vector2.zero;
            allies = false;
            foreach (Actor al in c.Allies)
            {
                float d = c.Dist(al);
                if (d >= 2f)
                {
                    want = (al.current_position - c.Pos) / d;
                    allies = true;
                    break;
                }
            }
            if (!allies)
            {
                want = c.Pos - threat;
                if (want.sqrMagnitude < 1e-4f)
                {
                    want = new Vector2(c.Self.flip ? -1f : 1f, 0f);
                }
                want.Normalize();
            }
            for (int i = 0; i < Fan.Length; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    Vector2 dir = Rotate(want, Fan[i]);
                    Vector2 q = c.Pos + dir * (j == 0 ? len : len * 0.7f);
                    if (!Combat.SafeSpot(q) || EnemiesNear(c, q, 1.75f) >= 2)
                    {
                        continue;
                    }
                    to = q;
                    return true;
                }
            }
            to = c.Pos;
            return false;
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static int EnemiesNear(Ctx c, Vector2 p, float r)
        {
            int n = 0;
            float r2 = r * r;
            foreach (Actor u in c.Enemies)
            {
                if ((u.current_position - p).sqrMagnitude <= r2)
                {
                    n++;
                }
            }
            return n;
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (!(p.Data is StepPlan s))
            {
                return false;
            }
            if (s.Retreat)
            {
                return MirageStep.Retreat(a, s.To, s.Look, p.Level, this);
            }
            if (s.Att == null || !s.Att.isAlive())
            {
                return false;
            }
            return MirageStep.TryStart(a, s.Att, Mathf.Clamp(p.Level, 1, 3), p.Level, false, MirageStep.Kind.React, this);
        }

        // ------------------------------------------------------ ordinary blows --

        [SakugaInit]
        private static void Init()
        {
            _instance = Techniques.Get("mirage_step") as MirageStepTech;
            if (_instance != null)
            {
                Hooks.BeforeOrdinaryHit.Add(OnOrdinaryHit);
                Hooks.WorldCleared += () => _evades.Clear();
            }
        }

        /// <summary>
        /// An ordinary melee blow (not a projectile) is about to land on 't': a Mirage Step user that
        /// is ready may sidestep it, at half its reaction chance. Level I steps aside; II and III end
        /// up behind the attacker with a free counter. Called for every blow in the world: cheap exits first.
        /// </summary>
        private static bool OnOrdinaryHit(AttackData d, Actor t)
        {
            try
            {
                MirageStepTech tech = _instance;
                if (tech == null || d.is_projectile || d.initiator == null || !d.initiator.isActor() || !Fighters.TryGet(t, out Fighter f))
                {
                    return false;
                }
                Actor att = d.initiator.a;
                if (att == null || att == t || !att.isAlive())
                {
                    return false;
                }
                int level = Techniques.Level(t, tech, f.Tier);
                if (level <= 0 || !f.Ready(tech) || !global::Sakuga.Mana.Has(t, tech, level, false) || !Moves.CanPerform(t))
                {
                    return false;
                }
                if (Random.value >= Chance(t, att, false) * 0.5f * Freshness(t, att))
                {
                    return false;
                }
                if (!MirageStep.TryStart(t, att, Mathf.Min(level, 2), level, false, MirageStep.Kind.Sidestep, tech))
                {
                    return false;
                }
                Remember(t, att);
                global::Sakuga.Mana.Spend(t, tech, level, false);
                f.SetCooldown(tech, tech.CooldownFor(t, level));
                Director.Used(tech, t.current_position);
                Brain.Started.TryGetValue(tech.Id, out int n);
                Brain.Started[tech.Id] = n + 1;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Sakuga] mirage sidestep: " + e);
                return false;
            }
        }
    }
}
