using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ZERO MOMENT (god ability: the colossus or the rival). Time stops. In the dark only two
    /// figures remain lit: the God, white with a gold edge, and its target, deep red. The God
    /// flickers out and strikes twelve times from twelve directions faster and faster, every cut
    /// left hanging in the air, then stands where it started with its back turned and sheathes
    /// its blade. Time snaps back: every cut flashes at once and the target breaks.
    ///
    /// Beat sheet (seconds; full mode: 2.75 s of frozen world, target included):
    ///
    ///   LOCK     0.00       focus lines converge on the target; the world freezes
    ///   VEIL     0.15-0.35  the veil fades to 75% black; the God and the target are redrawn above
    ///                       it as silhouettes (God white with a 1-px gold edge, target deep red;
    ///                       a unit drawn by its own prefab, a dragon, is copied from that prefab's
    ///                       sprite where it is drawn, in the air if it flies; one with no single
    ///                       sprite, Crabzilla, gets a red ring and disk instead); the God sinks
    ///                       into a draw stance
    ///   FLICKER  0.35-0.55  the God flickers and vanishes; a ting where it stood
    ///   STRIKES  0.55-2.05  twelve appearances on a circle around the target (its size + 1 tile,
    ///                       2.5 for a dragon), 137.5 degrees apart, each 0.10 -> 0.06 s, the gaps
    ///                       shrinking 0.18 -> 0.08 s: a lunge smear, a gold crescent, a cut line
    ///                       through the target, sparks; every cut stays as a thin gold line, so
    ///                       the target ends up in a web of cuts; the target does not react
    ///   BACK     2.05-2.45  the God stands at its start, back turned; a sheathe glint; it holds
    ///   SNAP     2.45       the veil snaps off, all twelve cuts flash white at once, the target
    ///                       flashes three times, a 3-tile black-flash starburst, 0.3 s hit-stop;
    ///                       30% of the target's max HP + 12xD (another God: 40% + 3xD, about
    ///                       40% of its life after armour, a crit at most 60%, and only a God
    ///                       already under 45% can be finished: a God is not erased in an instant;
    ///                       the share is not multiplied again by the God's Might, only by the
    ///                       Power setting). THE GROUND
    ///                       SPLITS: every cut becomes a trench through the target's ground (a web
    ///                       of twelve, 5-12 tiles long, Kit.Ravage force 2, every third force 3;
    ///                       against a Grandmaster or God, a god war, 10-18 tiles and every other
    ///                       one burning), opening three a frame, and a struck target's ground
    ///                       blasts into a crater (force 4, 2.5-5 tiles, half again as wide in a
    ///                       god war). Never under the God's own feet, no flames by its own people
    ///                       (Masters and up are fire-immune: the flames stay where they stand)
    ///   RESUME   2.75-3.20  the world resumes: a dust ring at the God's feet, the target (if it
    ///                       lives) reels for 2 s times its tier's Linger (a God 0.8 s; not in
    ///                       water: it wades out) (Down: the God chains straight on), the God
    ///                       hops round to face it
    ///
    /// The target is held as a Puppet when it can be (otherwise the frozen world holds it; in
    /// light mode it keeps moving and the strikes follow it). If it dies or vanishes before the
    /// snap, the God turns on another foe of its class within 2 tiles, or ends in its pose with a
    /// glint. A parry breaks the technique (the God staggers); an evasion leaves the cuts empty.
    /// </summary>
    internal sealed class ZeroMoment : Move
    {
        private const float TVeil = 0.15f;
        private const float TFlicker = 0.35f;
        private const float TGone = 0.55f;
        private const float TBack = 2.05f;
        private const float TGlint = 2.18f;
        private const float TSnap = 2.45f;
        private const float TResume = 2.75f;
        // a God turns and goes (it used to stand to 3.60)
        private const float TEnd = 3.20f;
        private const int Strikes = 12;
        /// <summary>Trenches opened per frame after the snap (the web splits open in a ripple).</summary>
        private const int TrenchesPerTick = 3;
        /// <summary>The most of a God's life the snap takes, crit or not (a God is never erased in an instant).</summary>
        private const float GodCrit = 0.6f;
        /// <summary>A God at or under this share of its life can be finished by the snap.</summary>
        private const float GodFinish = 0.45f;

        private static readonly float[] StrikeAt = new float[Strikes];
        private static readonly float[] StrikeLen = new float[Strikes];

        private static readonly Color White = Color.white;
        private static readonly Color Gold = GodFx.Gold;
        private static readonly Color GoldLight = GodFx.GoldLight;
        private static readonly Color Hot = GodFx.WhiteHot;
        private static readonly Color Red = GodFx.DeepRed;

        private const int BLock = 1, BVeil = 2, BFlick = 4, BVanish = 8, BBack = 16, BGlint = 32, BSnap = 64, BResume = 128;

        static ZeroMoment()
        {
            // the gaps shrink 0.18 -> 0.08 s and each appearance 0.10 -> 0.06 s: it accelerates
            float s = TGone;
            for (int k = 0; k < Strikes; k++)
            {
                StrikeAt[k] = s;
                StrikeLen[k] = Mathf.Lerp(0.10f, 0.06f, k / (Strikes - 1f));
                if (k < Strikes - 1)
                {
                    s += Mathf.Lerp(0.18f, 0.08f, k / (Strikes - 2f));
                }
            }
        }

        private readonly GodCast _cast;
        private readonly bool _full;
        private bool _cine;
        private Actor _v;
        private long _vId;
        private Puppet _p;
        private readonly Vector2 _home;
        private readonly bool _homeFace;
        private float _ring;
        private float _vUp, _vRad;
        private bool _vSprite;
        /// <summary>A target drawn by its own prefab (a dragon): that prefab's renderer.</summary>
        private SpriteRenderer _vAvatar;
        private readonly float _theta0;
        private readonly float _k;
        private readonly Palette _pal;
        private float _t;
        private int _beats;
        private int _strike = -1;
        private bool _shown;
        private Vector2 _spot;
        private Vector2 _lastBody;
        private bool _lost;
        private bool _struck;
        private Contact _contact = Contact.Hit;
        private float _recoil;

        private readonly List<Particle> _cuts = new List<Particle>();
        /// <summary>Each cut's direction and its sideways offset from the target (tiles): the trench it leaves.</summary>
        private readonly List<Vector2> _cutDirs = new List<Vector2>();
        private readonly List<float> _cutOffs = new List<float>();
        /// <summary>The web of trenches still to open (after the snap), through this ground point.</summary>
        private int _trenchNext = -1;
        private Vector2 _ruinAt;
        private float _trenchLen;
        /// <summary>Every n-th trench burns (force 3); the rest are blasted open (force 2).</summary>
        private int _trenchEvery = 3;
        private readonly List<Actor> _allies = new List<Actor>();
        private readonly List<Actor> _near = new List<Actor>();
        private readonly Particle[] _edge = new Particle[4];
        private Particle _white;
        private Particle _vClone, _vRing, _vDisk;

        private ZeroMoment(Actor a, Actor v, GodCast cast) : base(a)
        {
            _cast = cast;
            _full = cast.Full;
            _cine = _full;
            _home = a.current_position;
            _k = Mathf.Clamp(K, 0.8f, 1.5f);
            _pal = Palette.For(Tier.God, 3);
            _theta0 = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dx = v.current_position.x - _home.x;
            Pose.FaceRight = Mathf.Abs(dx) > 0.05f ? dx > 0f : a.flip;
            _homeFace = Pose.FaceRight;
            SetTarget(v);
        }

        public override bool Cinematic => _cine;

        public override bool Intangible => _t >= TFlicker && _t < TBack;

        public static bool Start(Actor a, GodCast cast, Technique tech)
        {
            Actor v = cast.Target;
            // (a foe another move is driving, its own technique or someone's juggle, is left alone)
            if (!Moves.CanPerform(a) || !Combat.Living(v) || v.asset.is_boat || !Combat.IsFoe(a, v) || Moves.Locked(v))
            {
                return false;
            }
            if (!Director.Claim(cast, 4.5f, a, v))
            {
                return false;
            }
            ZeroMoment m = new ZeroMoment(a, v, cast) { Tech = tech, Level = 1 };
            if (!Moves.Start(m))
            {
                Director.Release(cast);
                return false;
            }
            m.Grab(v);
            m.Open();
            return true;
        }

        /// <summary>Holds the target still as a puppet when it can be held (giants just freeze with the world).</summary>
        private void Grab(Actor v)
        {
            if (!Combat.CanPuppet(v, A))
            {
                return;
            }
            Puppet p = new Puppet(v, v.flip, this);
            if (Moves.Start(p))
            {
                _p = p;
            }
        }

        private void SetTarget(Actor v)
        {
            _v = v;
            _vId = v.Sid();
            Sprite s = Fx.UnitSprite(v);
            _vAvatar = s == null ? AvatarOf(v) : null;
            _vSprite = (s != null || _vAvatar != null) && Fx.HasSilhouette;
            float size = Mathf.Max(0.2f, v.stats["size"]);
            if (s != null)
            {
                _vUp = Mathf.Clamp(s.rect.height * 0.36f, 1.5f, 40f) * v.actor_scale;
                _vRad = Mathf.Max(0.45f, s.rect.height * 0.5f * v.actor_scale);
            }
            else
            {
                _vUp = Mathf.Max(0.3f, size * 0.45f);
                _vRad = Mathf.Max(0.6f, size * 0.6f);
            }
            _ring = Mathf.Clamp(size * 0.75f + 1f, 1.25f, 7.5f) * Mathf.Max(1f, _k * 0.9f);
            if (_vAvatar != null && AvatarShown())
            {
                // the prefab's own sprite says how big it is drawn
                Vector3 ext = _vAvatar.bounds.extents;
                _vRad = Mathf.Clamp(Mathf.Max(ext.x, ext.y) * 0.6f, 0.6f, 4.5f);   // (the frame has margins)
                _ring = Mathf.Clamp(Mathf.Max(_ring, _vRad + 0.9f), 1.25f, 7.5f);
            }
            _lastBody = TargetBody();
        }

        /// <summary>The renderer of a unit drawn by its own prefab (a dragon), if it has a single one.</summary>
        private static SpriteRenderer AvatarOf(Actor v)
        {
            try
            {
                if (v == null || v.asset == null || !v.asset.has_avatar_prefab || v.asset.ignore_generic_render || v.avatar == null)
                {
                    return null;
                }
                SpriteRenderer r = v.avatar.GetComponent<SpriteRenderer>();
                return r != null && r.sprite != null ? r : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Is the target's prefab drawn where it is (not parked off the map while unseen)?</summary>
        private bool AvatarShown()
        {
            return _vAvatar != null && _vAvatar.enabled && _vAvatar.sprite != null && _v != null && _v.Sid() == _vId && _v.is_visible;
        }

        private void Open()
        {
            if (_full)
            {
                Cinema.Hold(_cast);
            }
            Anim.Sound("event:/SFX/UI/HintHighlight", A.current_tile);
            Dev.Watch(Vector2.Lerp(BodyPos(), TargetBody(), 0.5f) + new Vector2(0f, 1f));
        }

        // ------------------------------------------------------------ helpers --

        private bool Beat(int b)
        {
            if ((_beats & b) != 0)
            {
                return false;
            }
            _beats |= b;
            return true;
        }

        private bool Visible => Kit.Visible(Pose.Ground, 0.4f) || Kit.Visible(_lastBody, 0.4f);

        private FxLayer Hi => _full ? FxLayer.Cine : FxLayer.Top;

        private int N(int n)
        {
            return Fx.Count(_full ? n : Mathf.Max(1, (n * 2 + 2) / 3));
        }

        private bool Held => _p != null && !_p.Finished && _p.Id == _vId;

        /// <summary>The middle of the target's body this frame (its puppet pose, or where the game draws it).</summary>
        private Vector2 TargetBody()
        {
            if (Held)
            {
                return _p.BodyPos();
            }
            if (_v != null && _v.Sid() == _vId)
            {
                if (AvatarShown())
                {
                    // where the prefab draws it (a flying dragon is far above its shadow)
                    Vector3 c = _vAvatar.bounds.center;
                    return new Vector2(c.x, c.y);
                }
                return _v.current_position + new Vector2(0f, _v.position_height + _vUp);
            }
            return _lastBody;
        }

        private float TargetGroundY()
        {
            if (Held)
            {
                return _p.Pose.Ground.y;
            }
            return _v != null && _v.Sid() == _vId ? _v.current_position.y : _lastBody.y - _vUp;
        }

        private void Ghost(float life, params Color[] colors)
        {
            Particle p = Fx.Afterimage(this, life, colors);
            if (_full)
            {
                GodFx.Lift(p);
            }
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _t += dt;
            // the claim follows the move's own clock (at high game speed the move outlasts any fixed
            // span of world time); Cleanup releases it
            if (!_lost && _v != null && _v.Sid() == _vId)
            {
                Director.Claim(_cast, 4.5f, A, _v);
            }
            else
            {
                Director.Claim(_cast, 4.5f, A);
            }
            Watch();
            if (!_lost && !_struck)
            {
                _lastBody = TargetBody();
            }
            float t = _t;
            if (t < TFlicker)
            {
                Lock(t);
            }
            else if (t < TGone)
            {
                Flicker(t);
            }
            else if (t < TBack)
            {
                Strike(t);
            }
            else if (t < TSnap)
            {
                Back(t);
            }
            else if (t < TResume)
            {
                Snap(t);
            }
            else
            {
                Resume(t);
            }
            if (_t > TSnap)
            {
                OpenTrenches();
            }
            if (t >= TEnd)
            {
                Finish();
            }
        }

        /// <summary>Is the target still there? If not: another of its class within 2 tiles, or no blow.</summary>
        private void Watch()
        {
            // (after the snap the blow is settled, whatever becomes of the target; the tick that
            // reaches the snap still looks, right before the blow)
            if (_lost || _struck || _v == null || (_beats & BSnap) != 0)
            {
                return;
            }
            // (hasHealth: a target another blow just killed is still "alive" until the game's death check)
            bool gone = !_v.isAlive() || !_v.hasHealth() || _v.Sid() != _vId || _v.isInMagnet() || _v.isInsideSomething()
                || (_p != null && _p.Finished);
            if (!gone)
            {
                return;
            }
            if (_p != null && !_p.Finished)
            {
                _p.Release(false);
            }
            _p = null;
            Actor next = _t < TBack ? Successor() : null;
            if (next != null)
            {
                if (Dev.On)
                {
                    Dev.Trace("zero " + Id + " target gone, turns on " + next.asset.id + "#" + next.Sid());
                }
                SetTarget(next);
                Grab(next);
                KillTargetClone();
                if (_full && _t >= TVeil)
                {
                    TargetClone();
                }
                return;
            }
            _lost = true;
            KillTargetClone();
            if (_t < TBack)
            {
                _t = TBack;   // nothing left to cut: back to its spot, a pose and a glint
                Pose.Hidden = false;
            }
        }

        private Actor Successor()
        {
            Weight w = Weights.Of(_v);
            List<Actor> near = new List<Actor>();
            Godhood.Gather(A, _lastBody, 2f + _vRad, near, null, false);
            Actor best = null;
            float bestD = float.MaxValue;
            foreach (Actor u in near)
            {
                // (as Godhood picks a target: never a boat, nor one another move is driving)
                if (Weights.Of(u) != w || u.asset.is_boat || !Combat.Living(u) || !Combat.Allowed(A, u) || Godhood.InGodMove(u) || Moves.Locked(u))
                {
                    continue;
                }
                float d = (u.current_position - _lastBody).sqrMagnitude;
                if (d < bestD && Director.Claim(_cast, 4.5f, u))
                {
                    if (best != null)
                    {
                        Director.Release(best);
                    }
                    best = u;
                    bestD = d;
                }
            }
            return best;
        }

        // --------------------------------------------------------------- lock --

        private void Lock(float t)
        {
            float px = Px;
            if (Beat(BLock) && Visible)
            {
                int mark = GodFx.Mark();
                Fx.FocusLines(TargetBody(), px, N(14), 16f * _k + _vRad / px, 30f * _k + _vRad / px, 0.25f);
                if (_full)
                {
                    GodFx.Lift(mark);
                }
            }
            if (t >= TVeil && Beat(BVeil) && _full)
            {
                Cinema.Dim(0.75f, Color.black, TFlicker - TVeil);
                GodClones();
                TargetClone();
            }
            // the draw stance: low, weight back
            float k = Anim.Smooth(0f, TFlicker, t);
            Pose.Ground = _home;
            Pose.Height = 0f;
            Pose.SX = 1f + 0.06f * k;
            Pose.SY = 1f - 0.08f * k;
            Pose.Rot = -6f * k;
        }

        // ------------------------------------------------------------ flicker --

        private void Flicker(float t)
        {
            float px = Px;
            if (Beat(BFlick) && Visible)
            {
                Ghost(0.16f, new Color(1f, 1f, 1f, 0.75f), Fx.WithA(Gold, 0f));
            }
            float k = (t - TFlicker) / (TGone - TFlicker);
            int f = (int)(t * 60f);
            Pose.Hidden = f % 3 == 0 || (k > 0.5f && (f & 1) == 0);
            Pose.Nudge = new Vector2((f & 1) == 0 ? 0.5f : -0.5f, 0f);
            if (k > 0.85f && Beat(BVanish))
            {
                Vanish(px);
            }
        }

        private void Vanish(float px)
        {
            Pose.Hidden = true;
            Pose.Nudge = Vector2.zero;
            if (!Visible)
            {
                return;
            }
            int mark = GodFx.Mark();
            Fx.Glint(BodyPos() + new Vector2(0f, 1f * px), px, 0.3f, GoldLight, 1.1f);
            Fx.LandingPuff(_home, px, Combat.TileAt(_home));
            if (_full)
            {
                GodFx.Lift(mark);
            }
            Ghost(0.2f, new Color(1f, 1f, 1f, 0.8f), Fx.WithA(GoldLight, 0.4f), Fx.WithA(Gold, 0f));
        }

        // ------------------------------------------------------------ strikes --

        private void Strike(float t)
        {
            if (Beat(BVanish))
            {
                Vanish(Px);
            }
            int k = -1;
            for (int i = Strikes - 1; i >= 0; i--)
            {
                if (t >= StrikeAt[i])
                {
                    k = i;
                    break;
                }
            }
            if (k < 0)
            {
                Pose.Hidden = true;
                return;
            }
            float u = (t - StrikeAt[k]) / StrikeLen[k];
            if (k != _strike)
            {
                _strike = k;
                Appear(k);
            }
            if (u < 1f)
            {
                Lunge(Mathf.Clamp01(u));
            }
            else if (_shown)
            {
                _shown = false;
                if (Visible)
                {
                    Ghost(0.09f, new Color(1f, 1f, 1f, 0.6f), Fx.WithA(Gold, 0f));
                }
                Pose.Hidden = true;
            }
        }

        /// <summary>Strike k: the God is suddenly there, 137.5 degrees on from the last one, and cuts.</summary>
        private void Appear(int k)
        {
            float px = Px;
            Vector2 body = TargetBody();
            float th = _theta0 + k * 137.5f * Mathf.Deg2Rad;
            _spot = body + new Vector2(Mathf.Cos(th), Mathf.Sin(th) * 0.8f) * _ring;
            _shown = true;
            float dx = body.x - _spot.x;
            Pose.FaceRight = Mathf.Abs(dx) > 0.05f ? dx > 0f : !Pose.FaceRight;
            Lunge(0f);
            Anim.Sound((k & 1) == 0 ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitSwordShield", Combat.TileAt(body));
            // the cut: a line through the target along the stroke, a little off true (remembered
            // on or off screen: at the snap the ground splits along every one of them)
            Vector2 dir = (body - _spot).normalized;
            if (dir.sqrMagnitude < 0.5f)
            {
                dir = Vector2.right;
            }
            float tilt = (Random.value < 0.5f ? -1f : 1f) * Random.Range(22f, 48f) * Mathf.Deg2Rad;
            dir = new Vector2(dir.x * Mathf.Cos(tilt) - dir.y * Mathf.Sin(tilt), dir.x * Mathf.Sin(tilt) + dir.y * Mathf.Cos(tilt));
            Vector2 side = new Vector2(-dir.y, dir.x);
            float off = Random.Range(-0.3f, 0.3f);
            _cutDirs.Add(dir);
            _cutOffs.Add(off);
            if (!Visible)
            {
                return;
            }
            float fwd = Pose.FaceRight ? 1f : -1f;
            Ghost(0.12f, new Color(1f, 1f, 1f, 0.9f), Fx.WithA(GoldLight, 0.5f), Fx.WithA(Gold, 0f));
            int mark = GodFx.Mark();
            // the blade's swoosh stays where it was drawn (Kit.Swish would follow the God to its next spot)
            Particle swoosh = Fx.Spawn(Art.Arc(10, 0.85f, 150f, -60f, 3), BodyPos() + new Vector2(fwd * 2f, 1f) * px, px, 0.11f, Hi, 5);
            if (swoosh != null)
            {
                swoosh.SX = fwd;
                swoosh.Color(Hot, Gold, Fx.WithA(Gold, 0.6f));
            }
            Vector2 mid = body + side * off * _vRad;
            float half = Mathf.Max(0.55f, _vRad * 1.3f);
            // the gold crescent sweeping across the target, its chord along the cut
            int ar = Mathf.RoundToInt(Mathf.Clamp(half * 0.9f / px, 6f, 60f));
            Particle arc = Fx.Spawn(Art.Arc(ar, 0.5f, 200f, -20f, 3), mid - side * (ar * 0.25f * px), px, 0.11f, Hi, 29);
            if (arc != null)
            {
                arc.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                arc.SX = Random.value < 0.5f ? -1f : 1f;
                arc.Color(Hot, Fx.WithA(Gold, 0.95f), Fx.WithA(Gold, 0.5f));
            }
            Particle cut = Kit.Line(mid - dir * half, mid + dir * half, px, 1f, 2, Hi, 30, Hot);
            if (cut != null)
            {
                cut.Held = true;
                cut.Cine = _full;
                cut.Step = (q, d) =>
                {
                    // a bright stroke that settles into a thin hanging line
                    float a = q.Age;
                    q.SY = a < 0.08f ? 2f : 1f;
                    q.Grad = null;
                    q.R.color = a < 0.08f ? Hot : Color.Lerp(Fx.WithA(GoldLight, 0.85f), Fx.WithA(Gold, 0.42f), Mathf.Clamp01((a - 0.08f) / 0.3f));
                };
                _cuts.Add(cut);
            }
            Kit.Sparks(body, px, N(5), 70f, 150f, GoldLight, Hi);
            Fx.HitSpark(body + side * Random.Range(-0.2f, 0.2f) * _vRad, px, 1, dir, N(3));
            if (_full)
            {
                GodFx.Lift(mark);
            }
        }

        /// <summary>The appearance itself: a lunge smear dashing a little through its spot.</summary>
        private void Lunge(float u)
        {
            Vector2 body = TargetBody();
            Vector2 at = Vector2.Lerp(_spot, body, 0.22f * Anim.OutQuad(u));
            float lift = PivotWorld();
            float groundY = TargetGroundY();
            Vector2 g;
            float h;
            if (at.y - lift >= groundY)
            {
                g = new Vector2(at.x, groundY);
                h = at.y - lift - groundY;
            }
            else
            {
                g = new Vector2(at.x, at.y - lift);
                h = 0f;
            }
            Pose.Ground = Combat.Clamp(g);
            Pose.Height = Mathf.Max(0f, h);
            Pose.HideShadow = Pose.Height > 0.05f;
            Pose.Hidden = false;
            Pose.Nudge = Vector2.zero;
            Pose.Rot = Mathf.Lerp(18f, 30f, u);
            Pose.SX = Mathf.Lerp(1.25f, 1.08f, u);
            Pose.SY = Mathf.Lerp(0.86f, 0.95f, u);
        }

        /// <summary>World units from the feet to the body's middle (the God's own sprite).</summary>
        private float PivotWorld()
        {
            return Pose.PivotUp * A.actor_scale;
        }

        // --------------------------------------------------------------- back --

        private void Back(float t)
        {
            float px = Px;
            if (Beat(BVanish))
            {
                Vanish(px);
            }
            if (Beat(BBack))
            {
                Pose.Hidden = false;
                Pose.HideShadow = false;
                Pose.Ground = _home;
                Pose.Height = 0f;
                Vector2 body = _lastBody;
                // its back to the target
                Pose.FaceRight = Mathf.Abs(body.x - _home.x) > 0.05f ? body.x < _home.x : !_homeFace;
                if (Visible)
                {
                    Ghost(0.14f, new Color(1f, 1f, 1f, 0.85f), Fx.WithA(Gold, 0f));
                    int mark = GodFx.Mark();
                    Fx.LandingPuff(_home, px, Combat.TileAt(_home));
                    if (_full)
                    {
                        GodFx.Cine(mark);
                    }
                }
            }
            // arrives low, straightens into a still, held stance
            float k = Anim.OutCubic(Mathf.Clamp01((t - TBack) / 0.12f));
            Pose.Ground = _home;
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1.16f, 1.04f, k);
            Pose.SY = Mathf.Lerp(0.82f, 0.97f, k);
            Pose.Rot = Mathf.Lerp(12f, -3f, k);
            Pose.Nudge = Vector2.zero;
            if (t >= TGlint && Beat(BGlint))
            {
                float fwd = Pose.FaceRight ? 1f : -1f;
                Anim.Sound("event:/SFX/HIT/HitSwordShield", A.current_tile);
                if (Visible)
                {
                    // the sheathe: a glint at the hip
                    Particle g = Fx.Glint(BodyPos() + new Vector2(-fwd * 2f, -1f) * px, px, 0.26f, GoldLight, 1f);
                    if (_full)
                    {
                        GodFx.Lift(g);
                    }
                }
            }
        }

        // --------------------------------------------------------------- snap --

        private void Snap(float t)
        {
            if (Beat(BBack))
            {
                Pose.Hidden = false;
                Pose.Ground = _home;
            }
            if (Beat(BSnap))
            {
                DoSnap();
            }
            Pose.Ground = _home;
            Pose.Height = 0f;
            Pose.SX = 1.04f;
            Pose.SY = 0.97f;
            Pose.Rot = -3f;
        }

        private void DoSnap()
        {
            float px = Px;
            Vector2 body = _lastBody;
            WorldTile tile = Combat.TileAt(body);
            if (_full)
            {
                Cinema.Undim(0f);   // the veil snaps off
            }
            KillGodClones();
            // the ground under it, before the blow can take the target away
            _ruinAt = TargetGround();
            Tier vt = _lost ? Tier.None : Tiers.Of(_v);
            // the blow (every cut at once)
            if (!_lost)
            {
                _contact = Combat.Reach(this, _v, false);
                if (_contact == Contact.Hit)
                {
                    int max = Mathf.Max(1, _v.getMaxHealth());
                    // a share of its life (not multiplied again by the God's Might: the share is the
                    // design; the Power setting scales it like every blow) on top of the God's own
                    // strength; another God is not erased in an instant: about 40% of its life after
                    // armour, a crit at most GodCrit of it, and only a God already beaten under half
                    // can be finished by it
                    bool god = vt == Tier.God;
                    float mult = god ? 3f : 12f;
                    float share = god ? 0.4f : 0.3f;
                    int before = _v.getHealth();
                    bool lethal = !god || before <= max * GodFinish;
                    TierInfo ti = Tiers.Info(Tiers.Of(A));
                    float might = Mathf.Max(0.1f, ti != null ? ti.Might : 1f);
                    HitResult r = Combat.Hit(A, _v, mult, lethal, true, share * max / might, this);
                    int refund = 0;
                    if (god && r.Landed && _v.isAlive() && _v.hasHealth())
                    {
                        // (what it really lost: a duel's pacing or Last Light may have taken the blow first)
                        int lost = before - _v.getHealth();
                        int cap = Mathf.RoundToInt(max * GodCrit);
                        if (lost > cap)
                        {
                            refund = lost - cap;
                            _v.restoreHealth(refund);
                        }
                    }
                    _struck = true;
                    if (Dev.On)
                    {
                        Dev.Trace("zero " + Id + " snap on " + _v.asset.id + " dealt " + (r.Dealt - refund).ToString("F0") + (r.Killed ? " (killed)" : "")
                            + (r.Crit ? " (crit)" : "") + (refund > 0 ? " (a God keeps " + (1f - GodCrit).ToString("P0") + ")" : "")
                            + " hp " + _v.getHealth() + "/" + max);
                    }
                }
            }
            bool landed = !_lost && _contact == Contact.Hit;
            // all twelve cuts flash white, then fade (or shatter, if it was parried)
            bool vis = Kit.Visible(body, 0.5f);
            for (int i = 0; i < _cuts.Count; i++)
            {
                Particle c = _cuts[i];
                if (c == null || !c.Alive)
                {
                    continue;
                }
                c.Held = false;
                c.Life = c.Age + 0.34f;
                float born = c.Age;
                c.Step = (q, d) =>
                {
                    float a = q.Age - born;
                    q.SY = a < 0.07f ? 3f : 1f;
                    q.Grad = null;
                    q.R.color = a < 0.07f ? White : Color.Lerp(Fx.WithA(GoldLight, 0.9f), Fx.WithA(Gold, 0f), Mathf.Clamp01((a - 0.07f) / 0.27f));
                };
                if (vis && (i & 1) == 0)
                {
                    Kit.Sparks(c.Pos, px, landed ? 2 : 3, 50f, 120f, landed ? GoldLight : White, Hi);
                }
            }
            if (landed && vis)
            {
                // a 3-tile black flash
                Kit.ImpactFrame(body, px, Mathf.Max(0.3f, 1.5f / (26f * px)), _pal, true, Hi);
                Kit.Flash(body, px, Mathf.RoundToInt(Mathf.Clamp(1.1f / px, 4f, 60f)), 0.1f, White, Hi, 28);
                Fx.FocusLines(body, px, N(12), 20f * _k + _vRad / px, 36f * _k + _vRad / px, 0.2f);
            }
            if (landed)
            {
                Kit.Stop(body, 0.3f);
                Kit.Shake(body, 0.3f, 0.012f, _k);
                Anim.Sound("event:/SFX/EXPLOSIONS/ExplosionLightningStrike", tile);
            }
            if (_contact == Contact.Parried)
            {
                _recoil = 1f;
            }
            // THE GROUND SPLITS along every cut (three trenches a frame from now on), and a struck
            // target's ground blasts into a crater
            BeginRuin(landed, vt);
        }

        /// <summary>Where the target stands on the ground (its shadow, for a flier).</summary>
        private Vector2 TargetGround()
        {
            if (Held)
            {
                return _p.Pose.Ground;
            }
            // (a target lost to the god hand or a building: where it was cut, not where it is now)
            if (!_lost && _v != null && _v.Sid() == _vId && _v.isAlive())
            {
                return _v.current_position;
            }
            return new Vector2(_lastBody.x, _lastBody.y - _vUp);
        }

        /// <summary>
        /// The snap's ruin: a crater under a struck target (force 4 for a God, 2.5-5 tiles), and the
        /// web of trenches queued. Against the God's equal (a Grandmaster or God: a god war) the web
        /// runs twice as far and burns along every other cut, and the crater is half again as wide.
        /// (Masters and up are fire-immune: the flames stay where they stand.)
        /// </summary>
        private void BeginRuin(bool landed, Tier vt)
        {
            if (_cutDirs.Count == 0 && !landed)
            {
                return;
            }
            bool rival = vt >= Tier.Grandmaster;
            _trenchLen = Mathf.Clamp(_ring + 1f + _vRad * 0.5f, 2.5f, 6f);
            if (rival)
            {
                _trenchLen = Mathf.Clamp(_trenchLen * 2f, 5f, 9f);
            }
            _trenchEvery = rival ? 2 : 3;
            Godhood.Gather(A, _ruinAt, _trenchLen + 3f, _near, _allies, false);
            _near.Clear();
            float r = 0f;
            if (landed)
            {
                bool god = Tiers.Of(A) == Tier.God;
                r = Mathf.Clamp(1.8f + _vRad * 0.5f, 2f, 4f) * (god ? 1.25f : 1f) * (rival ? 1.5f : 1f);
                GodRuin.Crater(A, _ruinAt, r, god ? 4 : 3, _allies, _home);
            }
            _trenchNext = 0;
            OpenTrenches();
            if (Dev.On)
            {
                Dev.Trace("zero " + Id + " ground splits: " + _cutDirs.Count + " trenches of " + (_trenchLen * 2f).ToString("F1") + " tiles"
                    + (landed ? ", a crater of " + r.ToString("F1") : "") + (rival ? " (god war: wider, burning)" : ""));
            }
        }

        /// <summary>Opens the next few trenches of the web (each through the target's ground, a little off its centre).</summary>
        private void OpenTrenches()
        {
            if (_trenchNext < 0)
            {
                return;
            }
            int n = 0;
            while (_trenchNext < _cutDirs.Count && n < TrenchesPerTick)
            {
                int i = _trenchNext++;
                Vector2 dir = _cutDirs[i];
                Vector2 side = new Vector2(-dir.y, dir.x);
                // up to 0.6 tiles off the centre (as the cut was off the body): a web, not a star
                Vector2 mid = _ruinAt + side * (_cutOffs[i] * 2f);
                float len = _trenchLen * (0.8f + 0.05f * ((i * 7) % 5));
                int force = i % _trenchEvery == _trenchEvery - 1 ? 3 : 2;
                GodRuin.Trench(A, mid - dir * len, mid + dir * len, 1f, force, _allies, _home, 1.3f);
                n++;
            }
            if (_trenchNext >= _cutDirs.Count)
            {
                _trenchNext = -1;
                _allies.Clear();
            }
        }

        // ------------------------------------------------------------- resume --

        private void Resume(float t)
        {
            float px = Px;
            if (Beat(BSnap))
            {
                DoSnap();
            }
            if (Beat(BResume))
            {
                if (_full)
                {
                    Cinema.Unhold(_cast);
                }
                if (Held)
                {
                    _p.Release(true);
                }
                // (a target the snap killed is still "alive" until the world processes its death)
                bool alive = !_lost && _v != null && _v.isAlive() && _v.hasHealth() && _v.Sid() == _vId;
                if (alive && _struck)
                {
                    // it reels as long as its tier lets it (a God 0.8 s, a Master 1.1 s, a common
                    // foe 2 s: the higher the tier, the sooner the fight goes on); one standing in
                    // water is left to wade out, not held there
                    if (!_v.isInLiquid())
                    {
                        Combat.Stagger(_v, 2f * Mathf.Max(0.4f, Tiers.Linger(_v)));
                    }
                    EndLink = Link.Down;
                    EndTarget = _v;
                }
                KillTargetClone();
                if (Visible)
                {
                    Kit.DustRing(_home, px, Combat.TileAt(_home), N(10), 48f, 0.55f, _k);
                }
            }
            float r = t - TResume;
            Pose.Height = 0f;
            Pose.Ground = _home;
            if (_recoil > 0f)
            {
                // parried: rocked back (and upright again by the end: the stagger follows)
                float k = Anim.OutCubic(Mathf.Clamp01(r / 0.15f));
                Pose.Rot = Mathf.Lerp(-3f, -18f, k) * (1f - Mathf.Clamp01((r - 0.2f) / 0.23f));
                Pose.SX = 1f;
                Pose.SY = 1f;
                Pose.Nudge = r < 0.2f ? new Vector2(((int)(r * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                return;
            }
            // a small hop round to face it again
            float h = Mathf.Clamp01(r / 0.24f);
            Pose.Height = 0.14f * _k * Mathf.Sin(Mathf.PI * h);
            if (h >= 0.5f && !_lost)
            {
                Vector2 body = _lastBody;
                if (Mathf.Abs(body.x - _home.x) > 0.05f)
                {
                    Pose.FaceRight = body.x > _home.x;
                }
            }
            float amp = r > 0.24f ? Anim.Spring(r - 0.24f, 9f, 18f) : 0f;
            Pose.SX = h < 1f ? Mathf.Lerp(1.04f, 0.94f, Mathf.Sin(Mathf.PI * h)) : 1f + 0.1f * amp;
            Pose.SY = h < 1f ? Mathf.Lerp(0.97f, 1.08f, Mathf.Sin(Mathf.PI * h)) : 1f - 0.12f * amp;
            Pose.Rot = 0f;
        }

        // ------------------------------------------------------------- clones --

        /// <summary>The God redrawn above the veil: white, with a one-pixel gold edge.</summary>
        private void GodClones()
        {
            Sprite s = Fx.UnitSprite(A);
            if (s == null || !Fx.HasSilhouette)
            {
                return;
            }
            for (int i = 0; i < 4; i++)
            {
                _edge[i] = Clone(s, 20);
            }
            _white = Clone(s, 21);
        }

        private Particle Clone(Sprite s, int order)
        {
            Particle p = Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Cine, order, true);
            if (p == null)
            {
                return null;
            }
            p.Held = true;
            p.UseEuler = true;
            Fx.Glue(p, this, Fx.WithA(White, 0f));
            return p;
        }

        /// <summary>The target redrawn above the veil in deep red (or ringed in red, if it has no sprite).</summary>
        private void TargetClone()
        {
            float px = Px;
            if (_vSprite)
            {
                Sprite s = _vAvatar != null ? _vAvatar.sprite : Fx.UnitSprite(_v);
                // order 4 above the veil: under the cuts (30) and the sparks (8), over nothing else
                _vClone = s != null ? Fx.Spawn(s, Vector2.zero, 1f, 1f, _full ? FxLayer.Cine : FxLayer.Over, 4, true) : null;
                if (_vClone != null)
                {
                    _vClone.Held = true;
                    _vClone.UseEuler = true;
                    _vClone.Cine = _full;
                    PlaceTargetClone(Fx.WithA(Red, 0f));
                }
                return;
            }
            int rx = Mathf.RoundToInt(Mathf.Clamp(_vRad / px, 3f, 120f));
            Vector2 body = TargetBody();
            _vDisk = Fx.Spawn(Art.Disk(rx, 0.85f, 0.45f), body, px, 1f, _full ? FxLayer.Cine : FxLayer.Over, 3);
            _vRing = Fx.Spawn(Art.Ring(rx + 1, 2, 0.85f), body, px, 1f, _full ? FxLayer.Cine : FxLayer.Over, 4);
            foreach (Particle p in new[] { _vDisk, _vRing })
            {
                if (p == null)
                {
                    continue;
                }
                p.Held = true;
                p.Snap = true;
                p.Cine = _full;
                p.Grad = null;
                p.R.color = Fx.WithA(Red, 0f);
                p.Step = (q, d) => q.Pos = TargetBody();
            }
        }

        private void PlaceTargetClone(Color c)
        {
            if (_vClone == null || !_vClone.Alive)
            {
                return;
            }
            if (Held)
            {
                Fx.Glue(_vClone, _p, c);
                return;
            }
            if (_v == null || _v.Sid() != _vId)
            {
                _vClone.R.enabled = false;
                return;
            }
            if (_vAvatar != null)
            {
                PlaceAvatar(_vClone, c);
                return;
            }
            GodFx.Place(_vClone, _v, Vector2.zero);
            _vClone.Grad = null;
            _vClone.R.color = c;
            _vClone.Tr.position = new Vector3(_vClone.Pos.x, _vClone.Pos.y, -0.02f);
            _vClone.Tr.localScale = new Vector3(_vClone.SX, _vClone.SY, 1f);
            _vClone.Tr.eulerAngles = _vClone.Euler;
            _vClone.R.enabled = c.a > 0.003f;
        }

        /// <summary>A copy of a prefab-drawn target exactly where its prefab draws it (sprite frame, scale, turn).</summary>
        private void PlaceAvatar(Particle p, Color c)
        {
            if (!AvatarShown())
            {
                p.R.enabled = false;
                return;
            }
            SpriteRenderer r = _vAvatar;
            Transform tr = r.transform;
            Sprite s = r.sprite;
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            Vector3 pos = tr.position;
            Vector3 sc = tr.lossyScale;
            p.Pos = new Vector2(pos.x, pos.y);
            p.Z = 0f;
            p.Px = 1f;
            p.Size0 = p.Size1 = 1f;
            p.SX = r.flipX ? -sc.x : sc.x;
            p.SY = r.flipY ? -sc.y : sc.y;
            p.Euler = tr.eulerAngles;
            p.Grad = null;
            p.R.color = c;
            p.Tr.position = new Vector3(p.Pos.x, p.Pos.y, -0.02f);
            p.Tr.localScale = new Vector3(p.SX, p.SY, 1f);
            p.Tr.eulerAngles = p.Euler;
            p.R.enabled = c.a > 0.003f;
        }

        /// <summary>
        /// The target's light this frame: deep red under the veil, then three white flashes at the
        /// snap (also in light mode, where the flash is all there is).
        /// </summary>
        private Color TargetColor(out bool want)
        {
            float t = _t;
            want = false;
            if (_lost)
            {
                return Color.clear;
            }
            if (t >= TSnap && t < TResume)
            {
                want = _struck;
                float s = t - TSnap;
                bool on = s < 0.05f || (s >= 0.1f && s < 0.15f) || (s >= 0.2f && s < 0.25f);
                return on ? Fx.WithA(White, 0.95f) : Fx.WithA(White, 0f);
            }
            if (_full && t >= TVeil && t < TSnap)
            {
                want = true;
                return Fx.WithA(Red, 0.92f * Mathf.Clamp01((t - TVeil) / (TFlicker - TVeil)));
            }
            return Color.clear;
        }

        public override void Frame()
        {
            float a = _full ? Mathf.Clamp01((_t - TVeil) / (TFlicker - TVeil)) : 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 off = i == 0 ? new Vector2(1f, 0f) : i == 1 ? new Vector2(-1f, 0f) : i == 2 ? new Vector2(0f, 1f) : new Vector2(0f, -1f);
                GodFx.GlueOff(_edge[i], this, Fx.WithA(Gold, a), off * A.actor_scale);
            }
            GodFx.GlueOff(_white, this, Fx.WithA(White, a), Vector2.zero);

            Color c = TargetColor(out bool want);
            if (want && _vClone == null && _vRing == null && _t >= TSnap && _t < TResume)
            {
                TargetClone();   // light mode: the snap's flashes need a copy of the target too
            }
            if (_vClone != null)
            {
                PlaceTargetClone(c);
            }
            if (_vRing != null && _vRing.Alive)
            {
                _vRing.R.color = c;
            }
            if (_vDisk != null && _vDisk.Alive)
            {
                _vDisk.R.color = Fx.WithA(c, c.a * 0.4f);
            }
        }

        // ------------------------------------------------------------- endings --

        private void KillGodClones()
        {
            for (int i = 0; i < 4; i++)
            {
                _edge[i]?.Kill();
                _edge[i] = null;
            }
            _white?.Kill();
            _white = null;
        }

        private void KillTargetClone()
        {
            _vClone?.Kill();
            _vRing?.Kill();
            _vDisk?.Kill();
            _vClone = _vRing = _vDisk = null;
        }

        private void Finish()
        {
            if (Finished)
            {
                return;
            }
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Height = 0f;
            Pose.Hidden = false;
            Pose.Nudge = Vector2.zero;
            while (_trenchNext >= 0)
            {
                OpenTrenches();   // (a frame-starved finish still splits the whole web)
            }
            Cleanup();
            GodRuin.Settle(A, _home);
            GodRuin.AfterMove(A);
            Finished = true;
        }

        /// <summary>Everything held goes, the target is let go, the cinematic and the claim end (safe to call twice).</summary>
        private void Cleanup()
        {
            KillGodClones();
            KillTargetClone();
            foreach (Particle c in _cuts)
            {
                if (c != null && c.Alive && c.Held)
                {
                    c.Kill();
                }
            }
            _cuts.Clear();
            if (Held)
            {
                _p.Release(_p.Pose.Height <= 0.02f);
            }
            if (_full)
            {
                Cinema.End(_cast);
            }
            _cine = false;
            Director.Release(_cast);
        }

        public override void Abort()
        {
            Cleanup();
        }

        public override void OnEnd(bool aborted)
        {
            Cleanup();
            Godhood.Ended(this, _full);
            if (!aborted && _recoil > 0f && A.isAlive())
            {
                Combat.Stagger(A, 0.5f);   // the parry broke it
            }
        }
    }

    /// <summary>
    /// Zero Moment as a technique: a God ability (Entry God, one level). The Brain never plans it;
    /// Godhood unleashes it on a colossus, a Grandmaster or God, its sworn rival or a butcher of
    /// its side within 8 tiles.
    /// </summary>
    internal sealed class ZeroMomentTech : Technique
    {
        public override string Id => "zero_moment";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.God;

        /// <summary>Time stops; twelve cuts from twelve directions; time resumes and the target breaks.</summary>
        public override Form Forms => Form.Teleport | Form.Blade | Form.Flurry | Form.Fast | Form.Ultimate;

        public override Scale Scale => Scale.Ultimate;

        public override int MaxLevel => 1;
        public override bool GodAbility => true;
        public override float[] ManaCost => new[] { 0f };
        public override float[] Cooldown => new[] { Godhood.CooldownZero };

        public override Plan Plan(Ctx c, int level)
        {
            return Godhood.PlanFor(this, GodKind.ZeroMoment, c);
        }

        public override bool Begin(Actor a, Plan p)
        {
            return Godhood.Begin(this, GodKind.ZeroMoment, a, p);
        }
    }
}
