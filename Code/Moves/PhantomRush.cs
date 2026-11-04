using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// PHANTOM RUSH. Against enemies spread too far apart for any area move, the unit vanishes,
    /// appears beside each of them in turn for a tenth of a second (a cut, a spark), is back where it
    /// started before anyone understood what happened, and puts its blade away. Then every wound opens
    /// at once.
    ///
    /// Beat sheet (level I, three targets; more targets take a little longer):
    ///
    ///   CROUCH   0.15  a crouch and a tremble; at 0.12 it is gone, an afterimage left standing where
    ///                  it was, a puff of dust and a streak towards the first target
    ///   RUSH     0.45  it appears at each target for 0.10 s (0.15 s a hop): a lunge, a crescent
    ///                  slash, a spark, a mark over the target; a straight speed line joins every
    ///                  appearance to the last, drawing a "constellation"; each spot it leaves keeps
    ///                  an afterimage of the cut. The order is a short path (nearest neighbour), every
    ///                  spot a safe tile beside its target, on the side it came from
    ///   RETURN   0.60  it reappears at the origin, low, after the cut (armed: its back to them)
    ///   SHEATHE  0.30  a slow exhale; a glint at the hilt (or the closing fist)
    ///   BURST    0.90  the click: every target takes its damage at once, flashes white (a tiered one
    ///                  its own colour), a cross of cuts opens on it, and it is knocked back
    ///   RECOVER  0.25
    ///
    /// Level II (bare traits only): five targets, tier-tinted lines and ghosts, a local hit-stop on the
    /// click, cut scars on the ground, a real launch. Level III (Gods): eight targets; the constellation
    /// and the ghosts stay lit until the burst, and the bursts go off in sequence along the path, 0.04 s
    /// apart, like firecrackers, each with a starburst and a glowing scar; then a final ring at the
    /// attacker, an impact frame and a global hit-stop. A rush gets one Black Flash at most: at II on
    /// the first wound that opens on a critical, at III the finale's impact frame itself turns black.
    ///
    /// Dynamic: armed units slash long thin crescents and sheathe with their back turned, unarmed ones
    /// (and beasts) strike short and close a fist facing them; a target that died before its turn is
    /// replaced by another of its class within 2 tiles, or skipped; an evaded cut marks nothing; a
    /// parried one breaks the rush right there (the attacker is thrown out of the blur and staggers),
    /// though the cuts already made still open. The attacker can't be touched while it is a blur.
    ///
    /// The world (Kit.Ravage, on the beat): II scorches the ground under every wound as it opens;
    /// III scorches every spot it appears on (a God's blasts a pit there), and the final ring tears
    /// the ground open in a circle around the attacker (never under its own feet; a God's burns).
    /// Masters and up (Flow) sheathe and recover in about half the time and start no attack
    /// cooldown: the next technique (a Scattered link to the nearest survivor) comes at once.
    /// </summary>
    internal sealed class PhantomRush : Move
    {
        private enum Phase
        {
            Crouch,
            Rush,
            Sheathe,
            Burst,
            Recover,
            Broken
        }

        private sealed class Node
        {
            public Actor V;
            public long Id;
            public Weight W;
            public float Cm;
            /// <summary>Where it was last seen (to look for a replacement if it dies).</summary>
            public Vector2 Last;
            /// <summary>Where the attacker appeared beside it, the cut's direction, and the attacker's body there.</summary>
            public Vector2 Spot, Cut, Point;
            public bool Visited, Marked, Done;
            /// <summary>III: lit until it detonates (held particles, ours to kill).</summary>
            public Particle Ghost, Mark, Line, Glow;
        }

        private const float TCrouch = 0.15f;
        private const float TVanish = 0.12f;
        private const float TRecover = 0.25f;
        private const float TBroken = 0.36f;
        private const float BurstGap = 0.04f;

        private static readonly float[] Turns = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        private static readonly Color Grey = new Color(0.86f, 0.9f, 0.96f, 1f);

        private readonly List<Node> _nodes;
        private readonly Vector2 _origin;
        private readonly Palette _pal;
        private readonly bool _armed;
        private readonly float _k, _slot, _show, _mult;
        /// <summary>A God: its ground marks are one force harder and its final ring wider.</summary>
        private readonly bool _god;
        /// <summary>Masters and up: no attack cooldown at the end (FlowStrikes fights between techniques).</summary>
        private readonly bool _flow;
        /// <summary>How much of the recovery a tier keeps (Wingbreaker.Crisp: 1 below Master).</summary>
        private readonly float _crisp;

        private Phase _phase = Phase.Crouch;
        private float _pt, _nt, _hold, _tSheathe;
        private bool _entered, _vanished, _slashed, _glinted, _final;
        /// <summary>
        /// A wound opened on a critical: the rush gets ONE Black Flash (II: on the first crit; III: the
        /// finale's impact frame turns black). In game a God's eight wounds critted six times and six
        /// black starbursts piled up with the finale's in a third of a second: noise, not a climax.
        /// </summary>
        private bool _crit, _blackDone;
        private int _index = -1, _burst;
        private Vector2 _prevPoint, _prevGround, _back;
        private Node _breaker;
        private Particle _originGhost, _closeLine, _closeGlow;

        /// <summary>Rushes started since the game started (dev log).</summary>
        internal static int Started;

        private PhantomRush(Actor a, List<Node> nodes, int level) : base(a)
        {
            Level = level;
            _nodes = nodes;
            _origin = a.current_position;
            _pal = Palette.For(a, level);
            // a blade (or club) to sheathe; a bow is put away like a fist (it strikes close and short)
            _armed = a.hasWeapon() && !a.hasRangeAttack();
            _k = K;
            _slot = level <= 1 ? 0.15f : level == 2 ? 0.13f : 0.11f;
            _show = level <= 1 ? 0.1f : level == 2 ? 0.09f : 0.08f;
            _mult = Mult(level);
            Tier tier = Tiers.Of(a);
            TierInfo ti = Tiers.Info(tier);
            _god = tier == Tier.God;
            _flow = ti != null && ti.Flow;
            _crisp = Wingbreaker.Crisp(tier);
            // the exhale before the click stays (it is the beat of the move), only shorter for masters
            _tSheathe = (level >= 3 ? 0.32f : 0.3f) * Mathf.Lerp(1f, _crisp, 0.7f);
            float dx = nodes[0].V.current_position.x - _origin.x;
            Pose.FaceRight = Mathf.Abs(dx) > 0.05f ? dx > 0f : a.flip;
        }

        // ------------------------------------------------------------ helpers --

        public static float Range(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 7f : 9f;
        }

        public static int MaxTargets(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 5 : 8;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.5f : level == 2 ? 3.6f : 5.5f;
        }

        /// <summary>Is there a safe tile to appear on right beside this point?</summary>
        public static bool HasSpot(Vector2 p)
        {
            return Combat.SafeSpot(p + new Vector2(0.5f, 0f)) || Combat.SafeSpot(p - new Vector2(0.5f, 0f))
                || Combat.SafeSpot(p + new Vector2(0f, 0.5f)) || Combat.SafeSpot(p - new Vector2(0f, 0.5f)) || Combat.SafeSpot(p);
        }

        /// <summary>Starts a rush through these targets, in this order.</summary>
        public static bool Begin(Actor a, List<Actor> targets, int level, Technique tech)
        {
            if (!Moves.CanPerform(a) || targets == null || targets.Count == 0)
            {
                return false;
            }
            List<Node> nodes = new List<Node>(targets.Count);
            foreach (Actor v in targets)
            {
                if (v == null || v == a || !v.isAlive() || Moves.Locked(v) || !Combat.IsFoe(a, v))
                {
                    continue;
                }
                if (!Combat.Allowed(a, v, out float cm))
                {
                    cm = 0.5f;   // above its class (only a god's order gets here): a glancing cut
                }
                nodes.Add(new Node { V = v, Id = v.Sid(), W = Weights.Of(v), Cm = cm, Last = v.current_position });
                if (nodes.Count >= MaxTargets(level))
                {
                    break;
                }
            }
            if (nodes.Count == 0)
            {
                return false;
            }
            PhantomRush m = new PhantomRush(a, nodes, level) { Tech = tech };
            if (!Moves.Start(m))
            {
                return false;
            }
            Started++;
            Dev.Watch(m._origin + new Vector2(0f, 1.5f));
            if (Dev.On)
            {
                Dev.Trace("phantom rush " + m.Id + " " + Techniques.Roman(level) + " through " + nodes.Count + (m._armed ? " (armed)" : " (unarmed)"));
            }
            return true;
        }

        /// <summary>The attacker can't be touched while it is a blur.</summary>
        public override bool Intangible => _phase == Phase.Rush || (_phase == Phase.Crouch && _vanished);

        private bool Visible(Vector2 at)
        {
            return Kit.Visible(at, 0.4f);
        }

        /// <summary>Middle of a unit as drawn (where cuts land).</summary>
        private static Vector2 BodyOf(Actor v)
        {
            float s = Mathf.Max(0.02f, v.actor_scale);
            float h;
            if (v.asset.has_avatar_prefab || v.asset.ignore_generic_render || !v.asset.has_sprite_renderer)
            {
                h = 11f * s;
            }
            else
            {
                Sprite sp = Fx.UnitSprite(v);
                h = (sp != null ? sp.rect.height : 10f) * 0.4f * s;
            }
            return v.current_position + new Vector2(0f, Mathf.Max(0f, v.position_height) + h);
        }

        /// <summary>
        /// Still a target it can visit: alive with health left (a body cut down earlier this frame stays 'alive'
        /// until the game buries it: it was visited, a slot spent beside a corpse), hostile, on (or near) the
        /// ground, not in someone else's hands.
        /// </summary>
        private bool Visitable(Actor v, long id)
        {
            return v != null && Combat.Living(v) && v.Sid() == id && Combat.IsFoe(A, v) && !Moves.Locked(v) && !v.isFlying()
                && v.position_height <= 1.6f && !v.asset.is_boat;
        }

        /// <summary>Still there for its wound to open (wherever it was thrown since).</summary>
        private static bool Burstable(Actor v, long id)
        {
            return v != null && v.isAlive() && v.Sid() == id && !v.isInsideSomething() && !v.isInMagnet();
        }

        private bool Taken(Actor u)
        {
            long id = u.Sid();
            foreach (Node n in _nodes)
            {
                if (n.Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Its target is gone: another of the same class within 2 tiles takes its place, if there is one.</summary>
        private bool Resolve(Node n)
        {
            if (Visitable(n.V, n.Id))
            {
                n.Last = n.V.current_position;
                return true;
            }
            Actor best = null;
            bool bestSame = false;
            float bestD = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, n.Last, 2f))
            {
                if (Taken(u) || !Combat.Allowed(A, u) || !Visitable(u, u.Sid()))
                {
                    continue;
                }
                bool same = Weights.Of(u) == n.W;
                float d = (u.current_position - n.Last).sqrMagnitude;
                if (best == null || (same && !bestSame) || (same == bestSame && d < bestD))
                {
                    best = u;
                    bestSame = same;
                    bestD = d;
                }
            }
            if (best == null)
            {
                return false;
            }
            n.V = best;
            n.Id = best.Sid();
            n.W = Weights.Of(best);
            Combat.Allowed(A, best, out n.Cm);
            n.Last = best.current_position;
            if (Dev.On)
            {
                Dev.Trace("phantom rush " + Id + " retargets to " + best.asset.id + "#" + n.Id);
            }
            return true;
        }

        /// <summary>A safe tile right beside the target, on the side the attacker comes from (or the nearest side that is safe).</summary>
        private bool SpotFor(Node n, out Vector2 spot)
        {
            Vector2 t = n.V.current_position;
            Vector2 dir = t - _prevGround;
            if (dir.sqrMagnitude < 0.0004f)
            {
                dir = new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
            }
            dir.Normalize();
            // a body's width away: beside it, not on top of it (at 0.42 tiles the two sprites merged
            // into one tangle in game)
            float off = 0.66f * _k + Mathf.Min(1.2f, Mathf.Max(0f, n.V.stats["size"]) * 0.35f);
            // (closer in only when nothing is safe at a body's width: a target on a tiny island)
            for (int ring = 0; ring < 2; ring++)
            {
                float r = ring == 0 ? off : off * 0.6f;
                for (int i = 0; i < Turns.Length; i++)
                {
                    float a = Turns[i] * Mathf.Deg2Rad;
                    Vector2 d = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
                    Vector2 s = Combat.Clamp(t - d * r);
                    if (Combat.SafeSpot(s))
                    {
                        spot = s;
                        return true;
                    }
                }
            }
            spot = Combat.Clamp(t);
            return Combat.SafeSpot(spot);
        }

        /// <summary>Thrown bodies fly by weight: critters comically far, brutes a little, titans not at all.</summary>
        private static float Fly(Actor u)
        {
            switch (Weights.Of(u))
            {
                case Weight.Critter: return 1.7f;
                case Weight.Folk: return 1f;
                case Weight.Brute: return 0.65f;
                case Weight.Fiend: return 0.45f;
                default: return 0.3f;
            }
        }

        /// <summary>Real damage to the world (Kit.Ravage): on or off screen, it is the world, not an effect.</summary>
        private void Ravage(Vector2 at, float radius, int force, string what)
        {
            if (force <= 0)
            {
                return;
            }
            radius *= Mathf.Clamp(_k, 0.75f, 1.6f);
            Kit.Ravage(at, radius, force, A);
            if (Dev.On)
            {
                Dev.Trace("phantom rush " + Id + " ravage " + what + " force " + force + " r " + radius.ToString("F2"));
            }
        }

        /// <summary>
        /// III's final ring: the ground blasted open in a circle around the attacker (6 craters; a
        /// God's 8, wider and burning), never under its own feet: the nearest blasted tile is more
        /// than a tile away, and a God's flames two.
        /// </summary>
        private void RingRavage(float wide)
        {
            int force = _god ? 3 : 2;
            int n = _god ? 8 : 6;
            float size = Mathf.Clamp(_k, 0.75f, 1.6f);
            float ring = 2.6f * wide * size;
            float a0 = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                float a = (a0 + i * 360f / n) * Mathf.Deg2Rad;
                Vector2 p = _origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                if (p.x < 1f || p.y < 1f || p.x >= MapBox.width - 1 || p.y >= MapBox.height - 1)
                {
                    continue;
                }
                // (the ground it stands on is kept: blasted around it, never lit under its feet)
                Kit.Ravage(p, 1.5f, force, A, true, _origin, 1.6f);
            }
            if (Dev.On)
            {
                Dev.Trace("phantom rush " + Id + " ravage ring x" + n + " force " + force + " at " + ring.ToString("F2"));
            }
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            if (Finished)
            {
                return;
            }
            if (_hold > 0f)
            {
                _hold -= dt;
                Pose.Nudge = new Vector2(((int)(_hold * 120f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                return;
            }
            Pose.Nudge = Vector2.zero;
            _pt += dt;
            switch (_phase)
            {
                case Phase.Crouch:
                    Crouch(dt);
                    break;
                case Phase.Rush:
                    Rush(dt);
                    break;
                case Phase.Sheathe:
                    Sheathe(dt);
                    break;
                case Phase.Burst:
                    Burst(dt);
                    break;
                case Phase.Broken:
                    Broken(dt);
                    break;
                default:
                    Recover(dt);
                    break;
            }
        }

        private void Next(Phase p)
        {
            if (Dev.On) Dev.Trace("phantom rush " + Id + " " + _phase + " -> " + p + " at t=" + T.ToString("F3"));
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

        // ------------------------------------------------------------- crouch --

        private void Crouch(float dt)
        {
            float px = Px;
            if (Enter())
            {
                _prevGround = _origin;
                _prevPoint = BodyPos();
                if (Visible(_origin))
                {
                    Kit.DustRing(_origin, px, A.current_tile, 6, 18f, 0.35f, _k);
                    if (Level >= 2)
                    {
                        Fx.Ring(_origin, px, TCrouch, 12f * _k, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f,
                            Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Edge, 0.7f), Fx.WithA(_pal.Core, 0.9f));
                    }
                }
            }
            if (!_vanished)
            {
                float c = Anim.OutCubic(Mathf.Clamp01(_pt / 0.08f));
                Pose.SX = Mathf.Lerp(1f, 1.15f, c);
                Pose.SY = Mathf.Lerp(1f, 0.82f, c);
                Pose.Rot = -6f * c;
                Pose.Nudge = _pt > 0.06f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                if (_pt >= TVanish)
                {
                    Vanish();
                }
            }
            if (_pt >= TCrouch)
            {
                Next(Phase.Rush);
            }
        }

        /// <summary>Gone: an afterimage stays where it stood, a streak points to where it went.</summary>
        private void Vanish()
        {
            float px = Px;
            _vanished = true;
            Pose.Nudge = Vector2.zero;
            Pose.SX = 1.1f;
            Pose.SY = 0.88f;
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (Visible(_origin))
            {
                Particle g = Fx.Afterimage(this, Level >= 3 ? 1f : Level == 2 ? 0.6f : 0.5f, Fx.WithA(_pal.Ghost, 0.85f), Fx.WithA(_pal.Ghost, 0.5f), _pal.GhostEnd);
                if (g != null && Level >= 3)
                {
                    g.Held = true;
                    g.Color(Fx.WithA(_pal.Ghost, 0.6f));
                    _originGhost = g;
                }
                Fx.LandingPuff(_origin, px, A.current_tile);
                Vector2 to = _nodes[0].V != null ? _nodes[0].V.current_position - _origin : new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
                Kit.SpeedLines(BodyPos() + to.normalized * 4f * px, to, px, 4, 3f, 0.14f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.85f));
                Fx.Ring(_origin, px, 0.16f, 2f, 9f * _k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2.5f,
                    Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.9f), Fx.WithA(_pal.Edge, 0f));
            }
            Pose.Hidden = true;
        }

        // --------------------------------------------------------------- rush --

        private void Rush(float dt)
        {
            if (_index < 0)
            {
                NextNode();
                if (_phase != Phase.Rush)
                {
                    return;
                }
            }
            _nt += dt;
            Node n = _nodes[_index];
            if (!Pose.Hidden)
            {
                Lunge(n);
                if (!_slashed && _nt >= _show * 0.3f)
                {
                    Slash(n);
                    if (_phase != Phase.Rush)
                    {
                        return;
                    }
                }
                if (_nt >= _show)
                {
                    Leave(n);
                }
            }
            if (_nt >= _slot)
            {
                NextNode();
            }
        }

        /// <summary>On to the next target that can still be reached; after the last, home.</summary>
        private void NextNode()
        {
            while (true)
            {
                _index++;
                if (_index >= _nodes.Count)
                {
                    Next(Phase.Sheathe);
                    return;
                }
                Node n = _nodes[_index];
                if (!Resolve(n) || !SpotFor(n, out Vector2 spot))
                {
                    continue;
                }
                Appear(n, spot);
                return;
            }
        }

        /// <summary>It appears beside the target, a straight line drawn back to where it last was.</summary>
        private void Appear(Node n, Vector2 spot)
        {
            float px = Px;
            _nt = 0f;
            _slashed = false;
            Pose.Ground = spot;
            Pose.Height = 0f;
            Pose.Hidden = false;
            Vector2 t = n.V.current_position;
            float dx = t.x - spot.x;
            if (Mathf.Abs(dx) > 0.05f)
            {
                Pose.FaceRight = dx > 0f;
            }
            Vector2 cut = t - spot;
            n.Cut = cut.sqrMagnitude > 0.0004f ? cut.normalized : new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
            n.Spot = spot;
            n.Visited = true;
            Pose.Rot = 22f;
            Pose.SX = 1.25f;
            Pose.SY = 0.85f;
            n.Point = BodyPos();
            Vector2 travel = n.Point - _prevPoint;
            ConstellationLine(_prevPoint, n.Point, n);
            if (Visible(n.Point))
            {
                Kit.SpeedLines(n.Point - travel.normalized * 3f * px, travel, px, 3, 3f, 0.12f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.8f));
                Kit.DustRing(spot, px, Combat.TileAt(spot), 4, 16f, 0.3f, _k);
            }
            if ((_index & 1) == 0)
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", Combat.TileAt(spot));
            }
            _prevPoint = n.Point;
            _prevGround = spot;
        }

        /// <summary>The cut's body language: stretched on arrival, cutting through, the follow-through.</summary>
        private void Lunge(Node n)
        {
            float k = Mathf.Clamp01(_nt / _show);
            float arrive = Anim.OutCubic(Mathf.Clamp01(k / 0.3f));
            float follow = Anim.OutQuad(Mathf.Clamp01((k - 0.3f) / 0.7f));
            Pose.SX = Mathf.Lerp(1.25f, 1.08f, arrive) - 0.04f * follow;
            Pose.SY = Mathf.Lerp(0.85f, 0.94f, arrive) + 0.04f * follow;
            Pose.Rot = Mathf.Lerp(22f, 32f, arrive) - 14f * follow;
            Pose.Ground = n.Spot + n.Cut * 0.16f * _k * arrive;
        }

        /// <summary>The cut: it may be answered; a mark is left on what it cut.</summary>
        private void Slash(Node n)
        {
            float px = Px;
            _slashed = true;
            if (Level >= 3)
            {
                // III: every spot it touches down on is scorched (a God's step blasts a pit, but
                // never the ground it comes home to: a pit there could be water by the time it lands)
                bool home = (n.Spot - _origin).sqrMagnitude < 1.5f * 1.5f;
                Ravage(n.Spot, 1f, _god && !home ? 2 : 1, "hop");
            }
            if (!Visitable(n.V, n.Id))
            {
                return;   // gone in the blink of an eye: the blade passes through air
            }
            Contact c = Combat.Reach(this, n.V, false);
            if (c == Contact.Parried)
            {
                _breaker = n;
                Next(Phase.Broken);
                return;
            }
            Vector2 at = BodyOf(n.V);
            bool vis = Visible(at);
            if (vis)
            {
                SlashArc();
            }
            if (c == Contact.Evaded)
            {
                if (vis)
                {
                    Fx.FocusLines(at, px, Fx.Count(4), 8f * _k, 14f * _k, 0.07f);
                }
                return;
            }
            n.Marked = true;
            n.V.startShake(0.12f, 0.08f, true, false);
            Anim.Sound(_armed ? "event:/SFX/HIT/HitMetal" : "event:/SFX/OTHER/Punch", n.V.current_tile);
            if (vis)
            {
                Fx.HitSpark(at, px, Level >= 3 ? 1 : 0, n.Cut, Fx.Count(4));
                if (Level >= 3)
                {
                    // the mark stays lit until it detonates
                    Particle m = Kit.Mark(n.V, _pal.Edge, 1f, 0.8f);
                    if (m != null)
                    {
                        m.Held = true;
                        m.Loop = true;
                        m.Fps = 12f;
                        m.Color(Fx.WithA(_pal.Core, 0.95f));
                        n.Mark = m;
                    }
                }
                else
                {
                    Kit.Mark(n.V, Level >= 2 ? _pal.Edge : Color.white, 0.45f, 0.7f);
                }
            }
        }

        /// <summary>A crescent slash: long and thin with a blade, short and thick with fists and claws.</summary>
        private void SlashArc()
        {
            float face = Pose.FaceRight ? 1f : -1f;
            bool up = (_index & 1) == 0;
            Sprite[] arc = _armed
                ? Art.Arc(12, 0.55f, up ? -40f : 150f, up ? 150f : -40f, 2)
                : Art.Arc(8, 1f, up ? -60f : 120f, up ? 100f : -40f, 3);
            Color core = Level >= 3 ? Color.white : Level == 2 ? _pal.Core : Color.white;
            Color edge = Level >= 2 ? _pal.Edge : Grey;
            Particle p = Kit.Swish(this, arc, 0.13f, new Vector2(face * (_armed ? 2.5f : 1.5f), 0f), face, core, edge, 6);
            if (p != null)
            {
                p.Step = null;   // it stays where the cut was made (the body is about to be somewhere else)
            }
        }

        /// <summary>It is gone again, leaving an afterimage of the cut behind.</summary>
        private void Leave(Node n)
        {
            if (Visible(n.Point))
            {
                if (Level >= 3)
                {
                    Particle g = Fx.Afterimage(this, 1f, Fx.WithA(_pal.Ghost, 0.6f));
                    if (g != null)
                    {
                        g.Held = true;
                        n.Ghost = g;
                    }
                }
                else
                {
                    Fx.Afterimage(this, Level == 2 ? 0.45f : 0.3f, Fx.WithA(_pal.Ghost, 0.75f), _pal.GhostEnd);
                }
            }
            Pose.Hidden = true;
        }

        /// <summary>A straight speed line between two appearances. III: lit (held) until that node bursts.</summary>
        private void ConstellationLine(Vector2 from, Vector2 to, Node n)
        {
            float px = Px;
            if (!Visible(from) && !Visible(to))
            {
                return;
            }
            if (Level >= 3)
            {
                Particle glow = Kit.Line(from, to, px, 1f, 3, FxLayer.Top, 4, Fx.WithA(_pal.Edge, 0.35f));
                Particle line = Kit.Line(from, to, px, 1f, 1, FxLayer.Top, 5, Fx.WithA(_pal.Core, 0.95f));
                if (glow != null)
                {
                    glow.Held = true;
                }
                if (line != null)
                {
                    line.Held = true;
                }
                if (n != null)
                {
                    n.Glow = glow;
                    n.Line = line;
                }
                else
                {
                    _closeGlow = glow;
                    _closeLine = line;
                }
                return;
            }
            if (Level == 2)
            {
                Kit.Line(from, to, px, 0.55f, 1, FxLayer.Top, 5, Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
            }
            else
            {
                Kit.Line(from, to, px, 0.35f, 1, FxLayer.Top, 5, new Color(1f, 1f, 1f, 0.9f), Fx.WithA(Grey, 0.5f), Fx.WithA(Grey, 0f));
            }
        }

        // ------------------------------------------------------------ sheathe --

        /// <summary>Which way it faces while it puts the blade away: armed, its back to the field; unarmed, towards it.</summary>
        private bool SheatheFacing()
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (Node n in _nodes)
            {
                if (n.Visited)
                {
                    sum += n.Spot;
                    count++;
                }
            }
            if (count == 0)
            {
                return Pose.FaceRight;
            }
            float dx = sum.x / count - _origin.x;
            if (Mathf.Abs(dx) < 0.05f)
            {
                return Pose.FaceRight;
            }
            bool toward = dx > 0f;
            return _armed ? !toward : toward;
        }

        private void Sheathe(float dt)
        {
            float px = Px;
            if (Enter())
            {
                // back where it started, as if it had never moved
                Pose.Ground = _origin;
                Pose.Height = 0f;
                Pose.Hidden = false;
                Pose.FaceRight = SheatheFacing();
                Pose.Rot = 14f;
                Pose.SX = 1.16f;
                Pose.SY = 0.84f;
                Vector2 home = BodyPos();
                ConstellationLine(_prevPoint, home, null);
                _originGhost?.Kill();
                _originGhost = null;
                if (Visible(_origin))
                {
                    Vector2 travel = home - _prevPoint;
                    Kit.SpeedLines(home - travel.normalized * 3f * px, travel, px, 3, 3f, 0.12f, Fx.WithA(Level >= 2 ? _pal.Core : Color.white, 0.8f));
                    Fx.LandingPuff(_origin, px, A.current_tile);
                }
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            }
            float k = Mathf.Clamp01(_pt / _tSheathe);
            float e = Anim.OutCubic(k);
            // a slow exhale: low after the cut, rising still
            Pose.SX = Mathf.Lerp(1.16f, 1.03f, e);
            Pose.SY = Mathf.Lerp(0.84f, 0.97f, e);
            Pose.Rot = Mathf.Lerp(14f, 3f, e);
            if (!_glinted && k >= 0.55f)
            {
                _glinted = true;
                if (Visible(_origin))
                {
                    float face = Pose.FaceRight ? 1f : -1f;
                    // the hilt at the hip (armed), the closing fist in front (unarmed)
                    Vector2 off = _armed ? new Vector2(-face * 2f * px, Pose.PivotUp * 0.7f * px) : new Vector2(face * 3f * px, Pose.PivotUp * 1.1f * px);
                    Particle g = Fx.Glint(BodyPos() + off, px, 0.24f, Level >= 2 ? _pal.Core : Color.white, Level >= 3 ? 1.2f : 1f);
                    if (g != null)
                    {
                        g.Step = (q, d) => q.Pos = BodyPos() + off;
                    }
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Burst);
            }
        }

        // -------------------------------------------------------------- burst --

        private void Burst(float dt)
        {
            float px = Px;
            if (Enter())
            {
                // the click
                Anim.Sound(_armed ? "event:/SFX/HIT/HitMetal" : "event:/SFX/OTHER/Punch", A.current_tile);
                Pose.Rot = 0f;
                Pose.SX = 0.96f;
                Pose.SY = 1.05f;
                if (Visible(_origin))
                {
                    float face = Pose.FaceRight ? 1f : -1f;
                    Vector2 hip = BodyPos() + new Vector2((_armed ? -face * 2f : face * 3f) * px, 0f);
                    Kit.Flash(hip, px, 3, 0.07f, Level >= 2 ? _pal.Core : Color.white);
                }
                _burst = 0;
                if (Level < 3)
                {
                    foreach (Node n in _nodes)
                    {
                        Detonate(n);
                    }
                    if (Level == 2)
                    {
                        _hold = 0.05f;
                    }
                }
            }
            if (Level >= 3)
            {
                // firecrackers along the path
                while (_burst < _nodes.Count && _pt >= _burst * BurstGap)
                {
                    Detonate(_nodes[_burst]);
                    _burst++;
                }
                if (_burst < _nodes.Count)
                {
                    return;
                }
                if (!_final)
                {
                    _final = true;
                    Finale();
                    return;
                }
            }
            Next(Phase.Recover);
        }

        /// <summary>A wound opens: damage, a white (or tier) flash, a cross of cuts, a knock (or launch).</summary>
        private void Detonate(Node n)
        {
            float px = Px;
            if (n.Done)
            {
                return;
            }
            n.Done = true;
            // the lit constellation goes out segment by segment, flashing as it goes
            if (n.Line != null)
            {
                Vector2 from = n.Line.Pos, len = new Vector2(Mathf.Cos(n.Line.Rot * Mathf.Deg2Rad), Mathf.Sin(n.Line.Rot * Mathf.Deg2Rad)) * n.Line.SX * px * 0.5f;
                Kit.Line(from - len, from + len, px, 0.12f, 2, FxLayer.Top, 6, Color.white, Fx.WithA(_pal.Edge, 0.8f), Fx.WithA(_pal.Edge, 0f));
            }
            KillNode(n);
            // (cut down by someone else this frame, it has no health left: no wound on the body, no kill credit)
            if (!n.Marked || !Burstable(n.V, n.Id) || !n.V.hasHealth())
            {
                return;
            }
            Actor v = n.V;
            Vector2 at = BodyOf(v);
            bool vis = Visible(at);
            HitResult r = Combat.Hit(A, v, _mult * n.Cm, true, true, 0f, this);
            if (Level == 2 && v.position_height < 0.3f)
            {
                // II: the ground under the wound is scorched with its scar
                Ravage(v.current_position, 1f, 1, "wound");
            }
            Tier vt = Tiers.Of(v);
            FlashUnit(v, vt != Tier.None ? Fx.Lighten(Tiers.Color(vt), 0.3f) : Color.white, 0.14f);
            if (r.Crit && Level >= 2)
            {
                _crit = true;
            }
            if (vis)
            {
                CrossCut(at, n.Cut, px);
                if (Level >= 3)
                {
                    // a firecracker: a crisp white-hot pop (it faded through the God tint into a khaki
                    // smudge on the grass in game); the tier colour lives in the sparks and the scar
                    Particle burst = Fx.Spawn(Art.Bursts[Random.Range(0, Art.Bursts.Length)], at, px * 0.38f * _k, 0.07f, FxLayer.Top, 31);
                    if (burst != null)
                    {
                        burst.Rot = Random.Range(0f, 360f);
                        burst.Sized(0.8f, 1.15f).Color(Color.white, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Core, 0f));
                    }
                    Kit.Flash(at, px, 5, 0.07f, _pal.Core);
                    Kit.Sparks(at, px, 6, 70f * _k, 130f * _k, _pal.Edge);
                    int embers = Fx.Count(3);
                    for (int i = 0; i < embers; i++)
                    {
                        Kit.Ember(at + Random.insideUnitCircle * 3f * px, px, Random.Range(0.5f, 0.9f), new Vector2(Random.Range(-8f, 8f), Random.Range(10f, 25f)) * px, _pal.Edge);
                    }
                }
                else
                {
                    if (Level == 2)
                    {
                        // every wound pops at once: a small flash on each, so the click reads across the field
                        Kit.Flash(at, px, 4, 0.06f, _pal.Core);
                    }
                    Fx.HitSpark(at, px, Level == 2 ? 2 : 1, n.Cut, Fx.Count(Level == 2 ? 6 : 4));
                }
                if (r.Crit && Level >= 2 && !_blackDone && (Level == 2 || _phase == Phase.Broken))
                {
                    // the black flash: once a rush (III keeps it for the finale, unless the rush was broken)
                    _blackDone = true;
                    Kit.ImpactFrame(at, px, 0.6f, _pal, true);
                }
                if (Level >= 2)
                {
                    Scar(v.current_position, n.Cut, px);
                }
            }
            Anim.Sound("event:/SFX/HIT/HitFlesh", v.current_tile);
            if (v.isAlive())
            {
                float dist = (Level <= 1 ? 0.9f : Level == 2 ? 1.5f : 1.8f) * _k * Fly(v);
                float apex = (Level <= 1 ? 0.5f : Level == 2 ? 1.1f : 1.3f) * _k;
                Combat.Launch(v, v.current_position - n.Cut, dist, apex, (n.Cut.x >= 0f ? -1f : 1f) * Random.Range(400f, 700f));
            }
            if (Dev.On)
            {
                Dev.Trace("phantom rush " + Id + " burst x" + (_mult * n.Cm).ToString("F2") + " -> " + v.asset.id + " hp " + v.getHealth() + (r.Crit ? " CRIT" : ""));
            }
        }

        /// <summary>Two cuts crossing over the body, opening for a moment.</summary>
        private void CrossCut(Vector2 at, Vector2 cut, float px)
        {
            float a = Mathf.Atan2(cut.y, cut.x);
            // longer than the body is tall, so the X reads even at level I from far away
            float len = (Level >= 3 ? 8f : Level == 2 ? 7f : 6.5f) * _k * px;
            Color core = Level >= 3 ? Color.white : Level == 2 ? _pal.Core : Color.white;
            Color edge = Level >= 2 ? _pal.Edge : Grey;
            for (int i = 0; i < 2; i++)
            {
                float b = a + (i == 0 ? 0.6f : -0.6f);
                Vector2 d = new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * len;
                Kit.Line(at - d, at + d, px, 0.18f, Level >= 3 ? 2 : 1, FxLayer.Top, 25, core, core, Fx.WithA(edge, 0.85f), Fx.WithA(edge, 0f));
            }
        }

        /// <summary>A cut scar in the ground under the victim (II: dark; III: glowing, cooling).</summary>
        private void Scar(Vector2 g, Vector2 cut, float px)
        {
            WorldTile tile = Combat.TileAt(g);
            if (tile == null || tile.Type == null || tile.Type.liquid)
            {
                return;
            }
            Vector2 d = new Vector2(cut.x, cut.y * Art.Ground).normalized * 5f * _k * px;
            Color dark = Fx.Darken(Fx.SoilColor(tile), 0.55f);
            Particle s = Level >= 3
                ? Kit.Line(g - d, g + d, px, 3f, 1, FxLayer.Ground, 2, Color.white, _pal.Edge, Fx.WithA(Color.Lerp(_pal.Edge, dark, 0.6f), 0.8f), Fx.WithA(dark, 0.6f), Fx.WithA(dark, 0f))
                : Kit.Line(g - d, g + d, px, 1.6f, 1, FxLayer.Ground, 2, Fx.WithA(dark, 0.85f), Fx.WithA(dark, 0.7f), Fx.WithA(dark, 0f));
            if (s != null)
            {
                s.Snap = true;
            }
        }

        /// <summary>III, after the last burst: the closing line goes out, a ring at the attacker, the impact frame, the world holds.</summary>
        private void Finale()
        {
            float px = Px;
            Vector2 last = _origin;
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                Node n = _nodes[i];
                if (n.Marked && Burstable(n.V, n.Id))
                {
                    last = BodyOf(n.V);
                    break;
                }
                if (n.Marked)
                {
                    last = n.Point;
                    break;
                }
            }
            _closeLine?.Kill();
            _closeGlow?.Kill();
            _closeLine = _closeGlow = null;
            // the final ring tears the ground open around it (a God's wider, and burning)
            float wide = _god ? 1.3f : 1f;
            RingRavage(wide);
            if (Visible(_origin))
            {
                Fx.Ring(_origin, px, 0.5f, 4f, 28f * _k * wide, 3, 1, FxLayer.Ground, 6, Art.Ground, 3f,
                    Color.white, Fx.WithA(_pal.Edge, 0.75f), Fx.WithA(_pal.Edge, 0f));
                Particle ring2 = Fx.Ring(_origin, px, 0.42f, 6f, 18f * _k * wide, 1, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(_pal.Core, 0.7f), Fx.WithA(_pal.Edge, 0f));
                if (ring2 != null)
                {
                    ring2.Delay = 0.07f;
                }
                Kit.DustRing(_origin, px, A.current_tile, 12, 60f, 0.6f, _k);
            }
            if (Visible(last))
            {
                // the climax, and the rush's only Black Flash if a wound opened on a critical
                Kit.ImpactFrame(last, px, 0.8f, _pal, _crit);
                Kit.Shake(last, 0.25f, 0.009f, _k);
            }
            Kit.Stop(last, 0.1f);
        }

        // ------------------------------------------------------------ recover --

        private void Recover(float dt)
        {
            float amp = Anim.Spring(_pt, 8f, 16f);
            Pose.SX = 1f + 0.06f * amp;
            Pose.SY = 1f - 0.06f * amp;
            Pose.Rot = 0f;
            Pose.Hidden = false;
            // masters are on to the next one at once (the wounds have opened: nothing left to watch)
            if (_pt >= TRecover * _crisp)
            {
                Finish(false);
            }
        }

        private void Finish(bool staggered)
        {
            Pose.SX = Pose.SY = 1f;
            Pose.Rot = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            KillAll();
            if (staggered)
            {
                Combat.Stagger(A, 0.6f);
            }
            else if (!_flow)
            {
                // (a master never stands and swings between techniques: FlowStrikes takes over at once)
                A.startAttackCooldown();
            }
            EndLink = Link.None;
            EndTarget = null;
            if (!staggered)
            {
                // whoever survived its wound has just been knocked away: a scattered field to chain
                // into (a Flash Step through them, a Crescent Fang...)
                Actor next = Survivor();
                if (next != null)
                {
                    EndLink = Link.Scattered;
                    EndTarget = next;
                }
            }
            Finished = true;
        }

        /// <summary>The nearest target still standing after the burst, if any.</summary>
        private Actor Survivor()
        {
            Actor best = null;
            float bestD = float.MaxValue;
            foreach (Node n in _nodes)
            {
                if (!n.Marked || !Burstable(n.V, n.Id) || !n.V.hasHealth() || !Combat.IsFoe(A, n.V))
                {
                    continue;
                }
                float d = (n.V.current_position - _origin).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = n.V;
                }
            }
            return best;
        }

        // ------------------------------------------------------------- broken --

        /// <summary>Parried mid-rush: thrown out of the blur and onto its heels. The cuts already made still open.</summary>
        private void Broken(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Node n = _breaker;
                Pose.Hidden = false;
                _vanished = false;
                Vector2 cut = n != null ? n.Cut : new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
                _back = Combat.Clamp(Pose.Ground - cut * 0.6f * _k);
                if (!Combat.SafeSpot(_back))
                {
                    _back = Pose.Ground;
                }
                _prevGround = Pose.Ground;
                Anim.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
                Vector2 at = BodyPos() + cut * 3f * px;
                if (Visible(at))
                {
                    Fx.HitSpark(at, px, 1, -cut, Fx.Count(7));
                    Fx.FocusLines(at, px, Fx.Count(8), 10f * _k, 20f * _k, 0.1f);
                }
                _originGhost?.Kill();
                _originGhost = null;
                _closeLine?.Kill();
                _closeGlow?.Kill();
                _closeLine = _closeGlow = null;
            }
            float k = Mathf.Clamp01(_pt / TBroken);
            Pose.Ground = Vector2.Lerp(_prevGround, _back, Anim.OutQuad(k));
            Pose.Height = 0.3f * _k * Mathf.Sin(Mathf.PI * Mathf.Clamp01(k / 0.7f));
            Pose.Rot = Mathf.Lerp(-30f, 0f, Anim.OutCubic(k));
            Pose.SX = Mathf.Lerp(0.9f, 1f, k);
            Pose.SY = Mathf.Lerp(1.1f, 1f, k);
            if (_burst == 0 && k >= 0.5f)
            {
                _burst = _nodes.Count;
                foreach (Node n in _nodes)
                {
                    Detonate(n);
                }
            }
            if (k >= 1f)
            {
                Pose.Height = 0f;
                Finish(true);
            }
        }

        // ------------------------------------------------------------ effects --

        /// <summary>A flat-colour copy of a unit as it is drawn right now, fading: the hit flash of a unit we don't hold.</summary>
        private static void FlashUnit(Actor v, Color c, float life)
        {
            if (v == null || !v.is_visible || !Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(v);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, RenderPos(v), 1f, life, FxLayer.Over, 2, true);
            if (p == null)
            {
                return;
            }
            p.UseEuler = true;
            p.Euler = v.current_rotation;
            p.SX = v.current_scale.x;
            p.SY = v.current_scale.y;
            p.Color(c, Fx.WithA(c, 0.7f), Fx.WithA(c, 0f));
            long id = v.Sid();
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    q.Pos = RenderPos(v);
                    q.Euler = v.current_rotation;
                    q.SX = v.current_scale.x;
                    q.SY = v.current_scale.y;
                }
            };
        }

        private static Vector2 RenderPos(Actor v)
        {
            return new Vector2(v.current_position.x + v.move_jump_offset.x, v.current_position.y + v.move_jump_offset.y + v.position_height);
        }

        private static void KillNode(Node n)
        {
            n.Ghost?.Kill();
            n.Mark?.Kill();
            n.Line?.Kill();
            n.Glow?.Kill();
            n.Ghost = n.Mark = n.Line = n.Glow = null;
        }

        private void KillAll()
        {
            foreach (Node n in _nodes)
            {
                KillNode(n);
            }
            _originGhost?.Kill();
            _closeLine?.Kill();
            _closeGlow?.Kill();
            _originGhost = _closeLine = _closeGlow = null;
        }

        public override void Abort()
        {
            KillAll();
        }

        public override void OnEnd(bool aborted)
        {
            KillAll();
        }
    }

    /// <summary>
    /// Phantom Rush as a technique. Entry: Grandmaster (I); Gods III (II only with bare traits).
    /// The AI uses it where an area move can't help: at least 3 / 3 / 4 enemies in reach that stand
    /// scattered (each more than 2 tiles from the others), the opposite of a crowd. It also follows a
    /// technique that has just scattered a crowd (2 are enough then).
    /// (The first version asked for 3 / 4 / 5 enemies 2.5 tiles apart: in the test battles and the
    /// war that never happened once, so the AI never chose it.)
    /// </summary>
    internal sealed class PhantomRushTech : Technique
    {
        public override string Id => "phantom_rush";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Chain;

        /// <summary>Vanishes and appears beside each far-flung enemy in turn; every wound opens at once.</summary>
        public override Form Forms => Form.Teleport | Form.Dash | Form.Blade | Form.Fast;

        public override Scale Scale => Scale.Heavy;

        public override float[] ManaCost => new[] { 40f, 65f, 100f };
        public override float[] Cooldown => new[] { 16f, 16f, 18f };

        private const float Apart = 2f;

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float range = PhantomRush.Range(level) + (c.Forced ? 4f : 0f);
            int max = PhantomRush.MaxTargets(level);
            int need = c.Forced ? 1 : c.Linked ? 2 : level <= 2 ? 3 : 4;
            // just scattered by a blast: they may still be coming down
            float lift = c.Linked ? 1.6f : 0.35f;
            List<Actor> pick = new List<Actor>(max);
            if (c.Forced && c.ForceTarget != null && c.Dist(c.ForceTarget) <= range && Eligible(c, c.ForceTarget, lift))
            {
                pick.Add(c.ForceTarget);
            }
            // the scattered ones: each more than 2 tiles from every one already picked
            for (int i = 0; i < c.Enemies.Count && pick.Count < max; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (pick.Contains(v) || Near(pick, v, Apart) || !Eligible(c, v, lift))
                {
                    continue;
                }
                pick.Add(v);
            }
            if (pick.Count < need)
            {
                return null;
            }
            // room left: anyone else in reach not standing right on top of one already picked
            for (int i = 0; i < c.Enemies.Count && pick.Count < max; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (pick.Contains(v) || Near(pick, v, 1.2f) || !Eligible(c, v, lift))
                {
                    continue;
                }
                pick.Add(v);
            }
            List<Actor> path = Order(c.Pos, pick);
            float mult = PhantomRush.Mult(level);
            float score = 0f;
            Vector2 sum = Vector2.zero;
            foreach (Actor v in path)
            {
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, mult), cm);
                sum += v.current_position;
            }
            // the right answer: a scattered field no area move can cover
            score += 30f + 8f * Mathf.Max(0, path.Count - need);
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan { Target = path[0], Point = sum / path.Count, Count = path.Count, Score = score, Others = path };
        }

        private static bool Eligible(Ctx c, Actor v, float lift)
        {
            if (v == null || v == c.Self || !v.isAlive() || Moves.Locked(v) || v.asset.is_boat || v.isFlying() || v.position_height > lift)
            {
                return false;
            }
            return c.Allowed(v) && PhantomRush.HasSpot(v.current_position);
        }

        private static bool Near(List<Actor> list, Actor v, float r)
        {
            float r2 = r * r;
            Vector2 p = v.current_position;
            for (int i = 0; i < list.Count; i++)
            {
                if ((list[i].current_position - p).sqrMagnitude <= r2)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A short path through them: always the nearest one not yet visited.</summary>
        private static List<Actor> Order(Vector2 from, List<Actor> set)
        {
            List<Actor> left = new List<Actor>(set);
            List<Actor> path = new List<Actor>(set.Count);
            Vector2 at = from;
            while (left.Count > 0)
            {
                int best = 0;
                float bestD = float.MaxValue;
                for (int i = 0; i < left.Count; i++)
                {
                    float d = (left[i].current_position - at).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                Actor v = left[best];
                left.RemoveAt(best);
                path.Add(v);
                at = v.current_position;
            }
            return path;
        }

        public override bool Begin(Actor a, Plan p)
        {
            List<Actor> targets = p.Others;
            if (targets == null || targets.Count == 0)
            {
                if (p.Target == null)
                {
                    return false;
                }
                targets = new List<Actor> { p.Target };
            }
            return PhantomRush.Begin(a, targets, p.Level, this);
        }
    }
}
