using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>What a beam is (Beam / ChoreoMove.Fire). Lengths in tiles, widths in sprite pixels of the shooter.</summary>
    internal sealed class BeamSpec
    {
        /// <summary>How far it reaches (tiles, x the shooter's size).</summary>
        public float Length = 12f;
        /// <summary>Its thickness (sprite pixels).</summary>
        public float Width = 5f;
        /// <summary>How fast its head travels (tiles a second).</summary>
        public float Speed = 45f;
        /// <summary>Seconds it keeps firing once it reached its length.</summary>
        public float Life = 0.6f;
        /// <summary>Damage dealt to one unit standing in it the whole time (x the shooter's damage).</summary>
        public float Mult = 2f;
        /// <summary>Tiles a unit in it is driven along it; it is blasted off at the end.</summary>
        public float Push = 2f;
        /// <summary>The ground along it is torn (base width in tiles; 0: none) at the move's Scale.</summary>
        public float Trench = 0.8f;
        /// <summary>Clash strength on top of the shooter's (tier Might x level x Scale x luck).</summary>
        public float Power = 1f;
        /// <summary>It wins every beam clash (except against another that always wins).</summary>
        public bool AlwaysWins;
        /// <summary>It can meet another beam head-on in a clash.</summary>
        public bool Clashes = true;
        public Element Element = Element.School;
    }

    /// <summary>
    /// BEAMS. A beam of light from the shooter's hands: a bright white core inside its colour and an
    /// outer glow, wobbling, its head racing out to its length, rings spiralling along it, motes
    /// streaming, the ground under it torn into a trench. Units in it are driven along it, hurt
    /// every tenth of a second, and blasted off when it ends (enemies only; each may answer it once).
    ///
    /// BEAM CLASH. Two beams meeting head-on lock: the meeting point (a flaring knot of both colours,
    /// sparks spraying, rings bursting, lightning crackling, the ground under it scorched) pushes back
    /// and forth between them like a tug of war while both shooters strain, then one side overwhelms:
    /// its beam surges through the knot and down the other's, which collapses; the loser is blasted
    /// away (its move broken). The winner is decided when they meet, by power: the shooter's tier
    /// Might, the level, the technique's Scale and some luck (a beam that AlwaysWins wins unless both
    /// do). A clash between Masters or higher slows the world for a moment (when cinematics allow).
    /// </summary>
    internal sealed class Beam : IOwned
    {
        public readonly Actor A;
        public readonly long AId;
        public ChoreoMove M;
        public readonly BeamSpec Spec;
        public Hue Hue;
        public Vector2 From, Dir;
        public float Len, MaxLen, Age, Px, K, Power;
        /// <summary>Firing (its head out, it hurts); then it fades out.</summary>
        public bool Firing = true;
        public bool Done;
        /// <summary>Locked in a clash with this beam.</summary>
        public Beam Clash;
        /// <summary>It won / lost its clash.</summary>
        public bool Won, Lost;

        private float _full = -1f, _fade, _tickT, _ringT, _moteT, _trenchAt, _dustT;
        private readonly Dictionary<long, Actor> _in = new Dictionary<long, Actor>();
        private readonly HashSet<long> _spared = new HashSet<long>();
        private readonly List<long> _gone = new List<long>();
        private Particle _glow, _mid, _core, _head, _muzzle, _light, _rim;
        private float _hitMult;
        private bool _vis;
        /// <summary>Extra life after a won clash (it surges on).</summary>
        private float _surge;
        private Vector2 _stopAt;
        private float _stopFor;

        public Beam(Actor a, ChoreoMove m, Vector2 from, Vector2 dir, BeamSpec spec)
        {
            A = a;
            AId = a.Sid();
            M = m;
            Spec = spec;
            From = from;
            Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right;
            Px = Choreo.PxOf(a);
            K = Choreo.KOf(a);
            MaxLen = spec.Length * Mathf.Lerp(1f, K, 0.5f);
            int level = m != null ? m.Level : 2;
            Hue = m != null ? (spec.Element == Element.School ? m.Hue : m.Hue.As(spec.Element)) : Hue.For(a, School.None, 2, spec.Element);
            TierInfo ti = Tiers.Info(Tiers.Of(a));
            float scale = m == null ? 1f : m.Scale == Scale.Ultimate ? 1.6f : m.Scale == Scale.Heavy ? 1.25f : m.Scale == Scale.Casual ? 0.7f : 1f;
            Power = (ti != null ? ti.Might : 0.9f) * (0.8f + 0.2f * level) * scale * spec.Power;
            // the damage of one tick: the whole beam's worth spread over its ticks
            float ticks = Mathf.Max(1f, (MaxLen / Mathf.Max(5f, spec.Speed) + spec.Life) / 0.1f);
            _hitMult = spec.Mult / ticks;
        }

        private bool OwnerOk => A != null && A.Sid() == AId && A.isAlive();

        /// <summary>Where its head is now.</summary>
        public Vector2 Tip => From + Dir * Len;

        /// <summary>How high above the ground it flies (its root's height over the shooter's feet).</summary>
        public float Lift;

        /// <summary>Its root and head on the ground (where it hits units and tears the ground).</summary>
        public Vector2 GroundFrom => From - new Vector2(0f, Lift);
        public Vector2 GroundTip => Tip - new Vector2(0f, Lift);

        /// <summary>Its thickness in world units (the reach of its hits round its line).</summary>
        public float Reach => Spec.Width * Px * 0.5f * Mathf.Lerp(1f, K, 0.5f) + 0.35f;

        // ------------------------------------------------------------- outside --

        /// <summary>It stops: it fades out from its root to its head.</summary>
        public void Collapse()
        {
            if (Firing)
            {
                Firing = false;
                _fade = 0f;
                ReleaseAll(false);
            }
        }

        /// <summary>Blocked at 'at' for 'seconds' (a barrier, a cut): its head splashes there instead of going on.</summary>
        public void StopAt(Vector2 at, float seconds)
        {
            _stopAt = at;
            _stopFor = seconds;
        }

        /// <summary>
        /// Sent back: it is stopped where 'by' stands and a beam of 'by' (its colours, the same length
        /// left, 'boost' times the power) fires back along it from there. Returns the new beam.
        /// </summary>
        public Beam Reflect(Actor by, float boost = 1.3f)
        {
            if (by == null || !Firing)
            {
                return null;
            }
            Vector2 at = Choreo.BodyAt(by, 0.5f);
            float along = Mathf.Max(1f, Vector2.Dot(at - From, Dir));
            StopAt(From + Dir * along, 0.5f);
            Collapse();
            BeamSpec s = new BeamSpec
            {
                Length = along + 2f,
                Width = Spec.Width * 1.1f,
                Speed = Spec.Speed * 1.2f,
                Life = Spec.Life,
                Mult = Spec.Mult * boost,
                Push = Spec.Push,
                Trench = Spec.Trench,
                Power = Spec.Power * boost,
                Clashes = false
            };
            return Beams.Fire(by, null, at, -Dir, s);
        }

        public void OwnerGone(ChoreoMove m, bool aborted)
        {
            if (m == M)
            {
                M = null;
                Collapse();
            }
        }

        // --------------------------------------------------------------- tick --

        /// <summary>One frame. False once it is spent.</summary>
        public bool Tick(float dt)
        {
            if (Done)
            {
                return false;
            }
            if (Clash != null && Clash.Done)
            {
                Clash = null;   // (the other beam vanished without deciding the clash: this one fires on)
            }
            if (M != null && (M.Ended || M.Finished || M.Broken))
            {
                M = null;
                Collapse();
            }
            if (!OwnerOk && Firing)
            {
                Collapse();
            }
            if (M != null)
            {
                From = M.Muzzle();
            }
            if (OwnerOk)
            {
                Lift = Mathf.Max(0f, From.y - Choreo.Feet(A).y);
            }
            _vis = Kit.Visible(From, 0.5f) || Kit.Visible(Tip, 0.5f) || Kit.Visible(Vector2.Lerp(From, Tip, 0.5f), 0.5f);
            if (dt <= 0f)
            {
                Render(0f);
                return true;
            }
            Age += dt;
            if (!Firing)
            {
                _fade += dt;
                Render(dt);
                if (_fade >= 0.16f)
                {
                    Done = true;
                    Kill();
                    return false;
                }
                return true;
            }
            if (_stopFor > 0f)
            {
                _stopFor -= dt;
            }
            if (Clash == null)
            {
                float goal = _stopFor > 0f ? Mathf.Min(MaxLen, Vector2.Dot(_stopAt - From, Dir)) : MaxLen;
                Len = Mathf.MoveTowards(Len, Mathf.Max(0f, goal), Spec.Speed * K * dt * (_surge > 0f ? 2.5f : 1f));
                if (_full < 0f && Len >= MaxLen - 0.01f)
                {
                    _full = Age;
                }
            }
            Trench();
            _tickT -= dt;
            if (_tickT <= 0f)
            {
                _tickT = 0.1f;
                Hurt();
            }
            Render(dt);
            // it ends when it has fired its life at full length (a clash holds it)
            if (Clash == null)
            {
                if (_surge > 0f)
                {
                    _surge -= dt;
                    if (_surge <= 0f)
                    {
                        Collapse();
                    }
                }
                else if (_full >= 0f && Age - _full >= Spec.Life || Age > 6f)
                {
                    Collapse();
                }
            }
            return true;
        }

        /// <summary>Everyone in it takes a tick: the first touch may be answered; then driven along, and blasted off at the end.</summary>
        private void Hurt()
        {
            if (!OwnerOk || Len < 0.3f)
            {
                return;
            }
            List<Actor> on = Beams.Scratch;
            Vector2 g0 = GroundFrom;
            Choreo.FoesOnLine(A, g0, GroundTip, Reach, on, true, true);
            // nearest along it first: an answer that stops or cuts it (a barrier, a cut) shields those behind
            for (int i = 1; i < on.Count; i++)
            {
                Actor x = on[i];
                float ax = Vector2.Dot(x.current_position - g0, Dir);
                int j = i - 1;
                while (j >= 0 && Vector2.Dot(on[j].current_position - g0, Dir) > ax)
                {
                    on[j + 1] = on[j];
                    j--;
                }
                on[j + 1] = x;
            }
            _gone.Clear();
            foreach (KeyValuePair<long, Actor> kv in _in)
            {
                _gone.Add(kv.Key);
            }
            for (int i = 0; i < on.Count; i++)
            {
                if (!Firing)
                {
                    break;   // an answer cut it: Collapse already let everyone in it go
                }
                Actor u = on[i];
                long id = u.Sid();
                if (_stopFor > 0f && Vector2.Dot(u.current_position - g0, Dir) > Vector2.Dot(_stopAt - From, Dir) + 0.3f + u.stats["size"] * 0.5f)
                {
                    continue;   // behind what blocks it: not reached (one that was in it is blasted off)
                }
                if (_spared.Contains(id) || !Combat.Living(u))
                {
                    continue;
                }
                // (one above the class limits only glances: it rocks in it, never driven along, never blasted off)
                bool full = Choreo.ClassShare(A, u, M, out float cm);
                _gone.Remove(id);
                if (!_in.ContainsKey(id))
                {
                    Contact c = Combat.Reach(M, u, false);
                    if (c != Contact.Hit)
                    {
                        _spared.Add(id);
                        continue;
                    }
                    _in[id] = u;
                    Choreo.Sound("event:/SFX/HIT/HitGeneric", u.current_tile, 0.08f);
                }
                Combat.Hit(A, u, _hitMult * cm, false, false, 0f, M);
                float push = Spec.Push * 0.1f / Mathf.Max(0.2f, Spec.Life + MaxLen / Mathf.Max(5f, Spec.Speed));
                Victims.Hurt(u, A, M, full ? Impact.Of(Sakuga.Hurt.Reel, Dir, Mathf.Max(0.12f, push * 3f), 0.8f) : Impact.Of(Sakuga.Hurt.Stagger, Dir, 0f, 0.5f));
                if (_vis)
                {
                    Vector2 at = Choreo.BodyAt(u, 0.5f);
                    Kit.Sparks(at, Px, 3, 40f, 100f, Hue.Edge);
                    Victims.Flash(u, Hue.Core, 0.05f);
                }
            }
            // those that left it (or it left them): blasted off along it
            for (int i = 0; i < _gone.Count; i++)
            {
                long id = _gone[i];
                if (_in.TryGetValue(id, out Actor u))
                {
                    _in.Remove(id);
                    if (u != null && u.Sid() == id)
                    {
                        Blast(u, 0.6f);
                    }
                }
            }
        }

        /// <summary>A unit leaves the beam: the last of its power throws it along the beam (a killing one dies where it lands).</summary>
        private void Blast(Actor u, float extra)
        {
            // (only an enemy still, as the beam passes over the rest: one in the god's hand, gone aboard,
            // out of reach mid-blink, now a friend, or above the class limits is let go untouched)
            if (u == null || !OwnerOk || !Combat.IsFoe(A, u) || !Choreo.ClassShare(A, u, M, out float cm))
            {
                return;
            }
            HitResult r = Combat.Hit(A, u, Spec.Mult * 0.15f * extra * cm, false, false, 0f, M);
            Impact im = Impact.Of(Sakuga.Hurt.Launch, Dir, Mathf.Max(1.2f, Spec.Push), 1.2f);
            im.Lift = 0.9f + 0.2f * Spec.Push;
            im.Owed = MeteorSlam.AtDeathsDoor(u);
            Victims.Hurt(u, A, M, im);
        }

        /// <summary>Lets everyone in it go (blasted off).</summary>
        private void ReleaseAll(bool blast)
        {
            foreach (KeyValuePair<long, Actor> kv in _in)
            {
                if (kv.Value != null && kv.Value.Sid() == kv.Key)
                {
                    Blast(kv.Value, blast ? 1.2f : 1f);
                }
            }
            _in.Clear();
        }

        /// <summary>Tears the ground along its path as its head passes (a burnt trench, at the move's Scale).</summary>
        private void Trench()
        {
            if (Spec.Trench <= 0f || !OwnerOk)
            {
                return;
            }
            float along = Len;
            if (along - _trenchAt < 1.5f && !(_full >= 0f && _trenchAt < along - 0.2f))
            {
                return;
            }
            Vector2 a = GroundFrom + Dir * _trenchAt;
            Vector2 b = GroundFrom + Dir * along;
            _trenchAt = along;
            // (the shooter's own feet are kept: it never stands in its own trench)
            Ruin.Furrow(A, a, b, Spec.Trench, M != null ? M.Scale : Scale.Heavy, A.current_position);
            if (_vis)
            {
                WorldTile t = Combat.TileAt(b);
                Kit.Debris(b, Px, t, 3, K);
            }
        }

        // -------------------------------------------------------------- clash --

        private float _clashT, _clashDur, _clashS0, _clashDrift, _clashSpark, _clashRing, _clashRumble;
        private Vector2 _clashAt;
        private bool _clashLead, _clashCine, _clashStar;
        private Beam _clashWinner;

        /// <summary>Two beams lock: this one leads the clash (it ticks it).</summary>
        public void BeginClash(Beam other)
        {
            Clash = other;
            other.Clash = this;
            _clashLead = true;
            other._clashLead = false;
            // where the heads meet, measured from this beam's root
            float dist = Vector2.Dot(other.From - From, Dir);
            float s0 = Mathf.Clamp((Len + (dist - other.Len)) * 0.5f, 0.5f, Mathf.Max(0.6f, dist - 0.5f));
            _clashS0 = s0;
            _clashT = 0f;
            float pa = Power * Random.Range(0.85f, 1.15f);
            float pb = other.Power * Random.Range(0.85f, 1.15f);
            bool aw = Spec.AlwaysWins && !other.Spec.AlwaysWins;
            bool bw = other.Spec.AlwaysWins && !Spec.AlwaysWins;
            _clashWinner = aw ? this : bw ? other : pa >= pb ? this : other;
            float close = Mathf.Min(pa, pb) / Mathf.Max(0.01f, Mathf.Max(pa, pb));
            _clashDur = 0.9f + 0.7f * close;
            // the knot is driven most of the way to the loser's hands
            _clashDrift = _clashWinner == this ? (dist - s0) - 1.2f * other.K : -(s0 - 1.2f * K);
            _clashAt = From + Dir * s0;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(_clashAt));
            Choreo.Sound("event:/SFX/POWERS/Powerup", Combat.TileAt(_clashAt), 0.2f);
            if (Kit.Visible(_clashAt))
            {
                Kit.Flash(_clashAt, Px, Mathf.RoundToInt(8f * K), 0.1f, Color.white, FxLayer.Top, 30);
                Kit.ImpactFrame(_clashAt, Px, 0.7f * K, Hue.Pal, false);
                Fx.FocusLines(_clashAt, Px, Fx.Count(14), 18f * K, 34f * K, 0.18f);
            }
            Kit.Stop(_clashAt, 0.07f);
            // a clash of the mighty slows the world for a moment
            if (Tiers.Of(A) >= Tier.Master && Tiers.Of(other.A) >= Tier.Master && Settings.Cinematics > 0 && Cinema.FullAt(_clashAt) && Cinema.TryBegin(this, 6f))
            {
                _clashCine = true;
                Cinema.SlowMo(0.45f, 0.7f);
            }
            if (Dev.On) Dev.Trace("beam clash: " + A.asset.id + "#" + AId + " (power " + pa.ToString("F2") + ") vs " + other.A.asset.id + "#" + other.AId
                + " (power " + pb.ToString("F2") + "): " + (_clashWinner == this ? "the first" : "the second") + " will win in " + _clashDur.ToString("F2") + " s");
        }

        /// <summary>The lead beam ticks the clash for both (after both moved their roots this frame).</summary>
        public void TickClash(float dt)
        {
            Beam o = Clash;
            if (o == null || !_clashLead)
            {
                return;
            }
            if (!Firing || !o.Firing)
            {
                // one of them stopped (its shooter fell, its move broke): the other wins at once
                Resolve(Firing ? this : o);
                return;
            }
            _clashT += dt;
            float k = Mathf.Clamp01(_clashT / _clashDur);
            // tug of war: it swings both ways, then the winner drives it home
            float dist = Vector2.Dot(o.From - From, Dir);
            float swing = Mathf.Sin(_clashT * 7.3f) * 0.55f + Mathf.Sin(_clashT * 12.9f + 1.3f) * 0.25f;
            float s = _clashS0 + swing * K * (1f - k) * 0.9f + _clashDrift * Beat.Apply(Ease.InCubic, Anim.Smooth(0.25f, 1f, k));
            s = Mathf.Clamp(s, 0.4f, Mathf.Max(0.5f, dist - 0.4f));
            _clashAt = From + Dir * s;
            Len = s;
            o.Len = Mathf.Max(0.3f, Vector2.Dot(_clashAt - o.From, o.Dir));
            ClashFx(dt, k);
            // both shooters strain against it
            M?.Strain(dt);
            o.M?.Strain(dt);
            if (k >= 1f)
            {
                Resolve(_clashWinner);
            }
        }

        private void ClashFx(float dt, float k)
        {
            if (!Kit.Visible(_clashAt))
            {
                return;
            }
            Beam o = Clash;
            float px = Px;
            Vector2 perp = Choreo.Perp(Dir);
            _clashSpark -= dt;
            if (_clashSpark <= 0f)
            {
                _clashSpark = 0.03f;
                int n = Fx.Count(4);
                for (int i = 0; i < n; i++)
                {
                    float side = Random.value < 0.5f ? -1f : 1f;
                    Vector2 d = (perp * side * Random.Range(0.6f, 1f) + Dir * Random.Range(-0.6f, 0.6f)).normalized;
                    Color c = Random.value < 0.5f ? Hue.Edge : o.Hue.Edge;
                    Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], _clashAt, px, Random.Range(0.12f, 0.24f), FxLayer.Top, 21);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving(d * Random.Range(70f, 150f) * px * K, Random.Range(0f, 30f) * px, 5f, 160f * px);
                    p.FaceVel = true;
                    p.Color(Color.white, c, Fx.WithA(c, 0f));
                }
                // the knot: two colours flaring over each other, a white star crackling at its heart
                Kit.Flash(_clashAt, px, Mathf.RoundToInt((6f + 3f * Mathf.Sin(_clashT * 30f)) * K), 0.05f, Fx.WithA(Color.Lerp(Hue.Core, o.Hue.Core, Random.value), 0.95f), FxLayer.Top, 26);
                _clashStar = !_clashStar;
                if (_clashStar)
                {
                    Particle star = Fx.Spawn(Art.Burst(Mathf.RoundToInt((7f + 4f * k) * K)), _clashAt, px, 0.05f, FxLayer.Top, 27);
                    if (star != null)
                    {
                        star.Rot = Random.Range(0f, 360f);
                        Color c2 = Random.value < 0.5f ? Hue.Edge : o.Hue.Edge;
                        star.Sized(0.8f, 1.15f).Color(Color.white, Fx.WithA(c2, 0.8f), Fx.WithA(c2, 0f));
                    }
                }
            }
            _clashRing -= dt;
            if (_clashRing <= 0f)
            {
                _clashRing = 0.12f;
                Color c = Random.value < 0.5f ? Hue.Edge : o.Hue.Edge;
                Fx.Ring(_clashAt, px, 0.22f, 4f, (14f + 10f * k) * K, 2, 1, FxLayer.Top, 22, 1.6f, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
                Vector2 g = _clashAt - new Vector2(0f, Lift);
                Kit.DustRing(g, px, Combat.TileAt(g), 6, 34f, 0.4f, K);
                // lightning crackling off the knot
                Particle bolt = Fx.Spawn(ChoreoArt.Bolt(Random.Range(8, 16), Random.Range(0, 8)), _clashAt, px, 0.07f, FxLayer.Top, 24);
                if (bolt != null)
                {
                    bolt.Rot = Random.Range(0f, 360f);
                    bolt.Color(Color.white, Fx.WithA(c, 0.8f), Fx.WithA(c, 0f));
                }
            }
            _clashRumble -= dt;
            if (_clashRumble <= 0f)
            {
                _clashRumble = 0.3f;
                Kit.Shake(_clashAt, 0.3f, 0.004f + 0.006f * k, K);
                Choreo.Sound("event:/SFX/POWERS/Powerup", Combat.TileAt(_clashAt), 0.25f);
            }
        }

        /// <summary>The clash is decided: the winner surges through, the loser's beam collapses and it is blasted away.</summary>
        private void Resolve(Beam winner)
        {
            Beam a = this, b = Clash;
            if (b == null)
            {
                return;
            }
            Beam loser = winner == a ? b : a;
            a.Clash = null;
            b.Clash = null;
            winner.Won = true;
            loser.Lost = true;
            if (_clashCine)
            {
                _clashCine = false;
                Cinema.End(this);
            }
            Vector2 at = _clashAt;
            loser.Collapse();
            // the winner surges on through and past the loser
            float reach = Vector2.Dot(loser.From - winner.From, winner.Dir) + 2.5f * loser.K;
            winner.MaxLen = Mathf.Max(winner.MaxLen, reach);
            winner._surge = 0.45f;
            winner._full = -1f;
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionMiddle", Combat.TileAt(at));
            if (Kit.Visible(at))
            {
                Kit.ImpactFrame(at, Px, 0.9f * K, winner.Hue.Pal, false);
                Kit.Sparks(at, Px, 14, 60f, 160f, winner.Hue.Edge);
                Fx.Ring(at, Px, 0.3f, 6f, 30f * K, 3, 1, FxLayer.Top, 22, 1f, 2.4f, Fx.WithA(Color.white, 1f), Fx.WithA(winner.Hue.Edge, 0.6f), Fx.WithA(winner.Hue.Edge, 0f));
            }
            Kit.Stop(at, 0.1f);
            Kit.Shake(at, 0.4f, 0.016f, K);
            // the loser takes the winner's beam full on
            Actor la = loser.A;
            if (la != null && la.Sid() == loser.AId && Combat.Living(la) && winner.OwnerOk && Combat.IsFoe(winner.A, la))
            {
                // (a class above the winner's limits takes only a glancing blow: its beam is gone, it rocks where it stands)
                bool full = Choreo.ClassShare(winner.A, la, winner.M, out float cm);
                HitResult r = Combat.Hit(winner.A, la, winner.Spec.Mult * 0.6f * cm, false, false, 0f, winner.M);
                if (!full)
                {
                    Victims.Hurt(la, winner.A, winner.M, Impact.Of(Sakuga.Hurt.Stagger, winner.Dir, 0f, 1.2f));
                }
                else if (loser.M != null && !loser.M.Ended)
                {
                    loser.M.BreakCharge(winner.A, winner.Dir, 2.5f + winner.Spec.Push);
                }
                else
                {
                    Impact im = Impact.Of(Sakuga.Hurt.Launch, winner.Dir, 2.5f + winner.Spec.Push, 1.5f);
                    im.Owed = MeteorSlam.AtDeathsDoor(la);
                    Victims.Hurt(la, winner.A, winner.M, im);
                }
                if (full && MeteorSlam.AtDeathsDoor(la))
                {
                    MeteorSlam.Doom(winner.A, la, winner.M);
                }
                winner._spared.Add(loser.AId);
            }
            if (Dev.On) Dev.Trace("beam clash won by " + winner.A.asset.id + "#" + winner.AId + "; " + loser.A.asset.id + "#" + loser.AId + " is overwhelmed");
        }

        // ------------------------------------------------------------- render --

        private void Render(float dt)
        {
            if (!_vis)
            {
                Kill();
                return;
            }
            float px = Px;
            float wk = Mathf.Lerp(1f, K, 0.5f);
            float fade = Firing ? 1f : 1f - Mathf.Clamp01(_fade / 0.16f);
            float wob = 1f + 0.12f * Mathf.Sin(Age * 47f) + 0.06f * Mathf.Sin(Age * 83f);
            float w = Spec.Width * wk * wob * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Age / 0.06f)) * (Firing ? 1f : Mathf.Lerp(1f, 0.2f, 1f - fade));
            Vector2 a = From;
            if (!Firing)
            {
                a = Vector2.Lerp(From, Tip, 1f - fade);   // it fades from its root to its head
            }
            Vector2 b = Tip;
            float len = Vector2.Distance(a, b);
            float ang = Mathf.Atan2(Dir.y, Dir.x) * Mathf.Rad2Deg;
            Vector2 mid = (a + b) * 0.5f;
            // its light on the ground under it
            Vector2 lift = new Vector2(0f, Lift);
            Bar(ref _light, mid - lift, len, Mathf.Max(2f, w * 1.6f), ang, Fx.WithA(Hue.Edge, 0.2f * fade), 2, FxLayer.Ground);
            if (Hue.Level >= 3)
            {
                // a level III beam burns inside a halo of the tier's own colour
                Bar(ref _rim, mid, len, Mathf.Max(3f, w + 5f + 2f * Mathf.Sin(Age * 23f)), ang, Fx.WithA(Hue.Rim, 0.28f * fade), 9);
            }
            Bar(ref _glow, mid, len, Mathf.Max(2f, w + 2f + ((int)(Age * 30f) & 1)), ang, Fx.WithA(Hue.Edge, 0.62f * fade), 10);
            Bar(ref _mid, mid, len, Mathf.Max(1f, w * 0.68f), ang, Fx.WithA(Color.Lerp(Hue.Main, Hue.Edge, 0.25f), 0.95f * fade), 11);
            Bar(ref _core, mid, len, Mathf.Max(1f, Mathf.Round(w * 0.3f)), ang, Fx.WithA(Hue.Core, fade), 12);
            Blob(ref _head, b, Mathf.Max(2f, w * 0.75f), Fx.WithA(Color.Lerp(Hue.Core, Hue.Main, 0.3f), fade), 13);
            Blob(ref _muzzle, From, Mathf.Max(2f, w * (0.65f + 0.1f * Mathf.Sin(Age * 61f))), Fx.WithA(Hue.Core, Firing ? 1f : fade), 13);
            if (!Firing || dt <= 0f || len < 0.2f)
            {
                return;
            }
            // rings spiralling out along it, motes streaming, dust thrown up under it
            _ringT -= dt;
            if (_ringT <= 0f)
            {
                _ringT = 0.07f;
                Particle r = Fx.Spawn(Art.Ring(Mathf.Max(2, Mathf.RoundToInt(w * 0.55f)), 1, 2.3f), From + Dir * 0.3f, px, Mathf.Min(0.35f, len / (Spec.Speed * 0.7f)), FxLayer.Top, 14);
                if (r != null)
                {
                    r.Rot = ang;
                    r.Moving(Dir * Spec.Speed * 0.7f);
                    r.Color(Fx.WithA(Hue.Core, 0.85f), Fx.WithA(Hue.Edge, 0.6f), Fx.WithA(Hue.Edge, 0f));
                }
            }
            _moteT -= dt;
            if (_moteT <= 0f)
            {
                _moteT = 0.03f;
                Vector2 p0 = Vector2.Lerp(a, b, Random.value) + Choreo.Perp(Dir) * Random.Range(-w, w) * 0.7f * px;
                Particle m = Fx.Spawn(Art.Pixel, p0, px, 0.18f, FxLayer.Top, 15);
                m?.Moving(Dir * Random.Range(8f, 20f) + Choreo.Perp(Dir) * Random.Range(-3f, 3f) * px * 10f, 0f, 2f).Sized(1.5f, 1f)
                    .Color(Color.white, Fx.WithA(Hue.Edge, 0.8f), Fx.WithA(Hue.Edge, 0f));
            }
            _dustT -= dt;
            if (_dustT <= 0f)
            {
                _dustT = 0.06f;
                Vector2 g = Vector2.Lerp(a, b, Random.value) - new Vector2(0f, Lift);
                WorldTile t = Combat.TileAt(g);
                if (t != null && t.Type != null && !t.Type.liquid)
                {
                    ChoreoFx.HeelDust(g, Choreo.Perp(Dir) * (Random.value < 0.5f ? -1f : 1f), px, t, K);
                }
            }
        }

        private void Bar(ref Particle p, Vector2 mid, float len, float thick, float ang, Color c, int order, FxLayer layer = FxLayer.Top)
        {
            // a soft stroke with tapering tips and a grain of light along it (the outer layers a dithered glow)
            int kind = thick >= 7f ? 0 : thick >= 2.5f ? 1 : 2;
            Sprite s = PrimFx.BandSprite(kind, order & 1);
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(s, mid, Px, 1f, layer, order);
                if (p == null)
                {
                    return;
                }
                p.Held = true;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            float bpx = Mathf.Max(1f, Mathf.Round(thick)) * Px / PrimFx.BandH;
            p.Pos = mid;
            p.Px = bpx;
            p.SX = Mathf.Max(0.01f, len / (PrimFx.BandW * bpx));
            p.SY = 1f;
            p.Rot = ang;
            p.Grad = null;
            p.R.color = c;
            p.Cine = Cinema.Playing;
        }

        private void Blob(ref Particle p, Vector2 at, float r, Color c, int order)
        {
            int ri = Mathf.Clamp(Mathf.RoundToInt(r), 1, 40);
            Sprite s = Art.Disk(ri, 1f, 0.3f);
            if (p == null || !p.Alive)
            {
                p = Fx.Spawn(s, at, Px, 1f, FxLayer.Top, order);
                if (p == null)
                {
                    return;
                }
                p.Held = true;
            }
            if ((object)p.R.sprite != s)
            {
                p.R.sprite = s;
                p.Frames[0] = s;
            }
            p.Pos = at;
            p.Grad = null;
            p.R.color = c;
            p.Cine = Cinema.Playing;
        }

        /// <summary>
        /// Torn out of the world at once (an error in its tick): spent, holding no one, its clash undone
        /// (the other beam fires on) and the clash's slow motion ended.
        /// </summary>
        public void Abandon()
        {
            Firing = false;
            Done = true;
            _in.Clear();
            if (_clashCine)
            {
                _clashCine = false;
                Cinema.End(this);
            }
            if (Clash != null)
            {
                if (Clash.Clash == this)
                {
                    Clash.Clash = null;
                }
                Clash = null;
            }
            Kill();
        }

        /// <summary>Its particles go back to the pool (it is spent, or off screen).</summary>
        public void Kill()
        {
            Kill(ref _light);
            Kill(ref _rim);
            Kill(ref _glow);
            Kill(ref _mid);
            Kill(ref _core);
            Kill(ref _head);
            Kill(ref _muzzle);
        }

        private static void Kill(ref Particle p)
        {
            if (p != null && p.Alive)
            {
                p.Kill();
            }
            p = null;
        }
    }

    /// <summary>The beams in the world (ticked once per frame after the moves), and their clashes.</summary>
    internal static class Beams
    {
        private static readonly List<Beam> _list = new List<Beam>();
        internal static readonly List<Actor> Scratch = new List<Actor>();

        public static int Count => _list.Count;

        public static IReadOnlyList<Beam> All => _list;

        /// <summary>A beam of 'a' (in move 'm', which owns it: it collapses when the move ends) from 'from' along 'dir'.</summary>
        public static Beam Fire(Actor a, ChoreoMove m, Vector2 from, Vector2 dir, BeamSpec spec)
        {
            if (a == null)
            {
                return null;
            }
            Beam b = new Beam(a, m, from, dir, spec);
            _list.Add(b);
            m?.Own(b);
            return b;
        }

        /// <summary>The beam this unit is firing right now, if any.</summary>
        public static Beam Of(Actor a)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (_list[i].A == a && _list[i].Firing)
                {
                    return _list[i];
                }
            }
            return null;
        }

        /// <summary>A beam of an enemy of 'v' firing at it (its line passing within 'margin' tiles of it), if any.</summary>
        public static Beam Toward(Actor v, float margin = 1f)
        {
            if (v == null)
            {
                return null;
            }
            for (int i = 0; i < _list.Count; i++)
            {
                Beam b = _list[i];
                if (!b.Firing || b.A == v || b.A == null || !Combat.IsFoe(b.A, v))
                {
                    continue;
                }
                Vector2 rel = v.current_position - b.GroundFrom;
                float along = Vector2.Dot(rel, b.Dir);
                if (along < 0f || along > b.MaxLen + 1f)
                {
                    continue;
                }
                if (Mathf.Abs(rel.x * b.Dir.y - rel.y * b.Dir.x) <= b.Reach + margin)
                {
                    return b;
                }
            }
            return null;
        }

        public static void Tick(float dt)
        {
            if (_list.Count == 0)
            {
                return;
            }
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Beam b = _list[i];
                bool alive;
                try
                {
                    alive = b.Tick(dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("choreo beam", e);
                    b.Abandon();
                    alive = false;
                }
                if (!alive)
                {
                    _list.Remove(b);
                }
            }
            if (dt > 0f)
            {
                Clashes(dt);
            }
        }

        /// <summary>Beams meeting head-on lock in a clash; locked ones play it out.</summary>
        private static void Clashes(float dt)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                Beam a = _list[i];
                if (a.Clash != null)
                {
                    a.TickClash(dt);   // (the lead decides it even once it stopped firing: then the other wins)
                    continue;
                }
                if (!a.Firing || !a.Spec.Clashes)
                {
                    continue;
                }
                for (int j = i + 1; j < _list.Count; j++)
                {
                    Beam b = _list[j];
                    if (!b.Firing || !b.Spec.Clashes || b.Clash != null || b.A == a.A || a.A == null || b.A == null || !a.A.isAlive() || !b.A.isAlive())
                    {
                        continue;
                    }
                    if (!Combat.IsFoe(a.A, b.A) && !Combat.IsFoe(b.A, a.A))
                    {
                        continue;
                    }
                    if (Vector2.Dot(a.Dir, b.Dir) > -0.75f)
                    {
                        continue;   // not head on
                    }
                    Vector2 rel = b.From - a.From;
                    float along = Vector2.Dot(rel, a.Dir);
                    float off = Mathf.Abs(rel.x * a.Dir.y - rel.y * a.Dir.x);
                    if (along <= 0.5f || off > a.Reach + b.Reach + 1.2f)
                    {
                        continue;
                    }
                    // their heads have met
                    if (a.Len + b.Len >= along - 0.2f)
                    {
                        a.BeginClash(b);
                        break;
                    }
                }
            }
        }

        public static void Clear()
        {
            foreach (Beam b in _list)
            {
                b.Kill();
            }
            _list.Clear();
        }
    }

    internal partial class ChoreoMove
    {
        private Beam _beam;

        /// <summary>The beam this move fires (null before Fire, or after it is spent).</summary>
        public Beam Beam => _beam;

        /// <summary>
        /// Thrusts the gathered power forward and fires a beam along the plan's direction (or at the
        /// target): the hands snap forward (a flash, the orb spent), the beam races out, and this beat
        /// lasts as long as the beam fires (a clash holds it), the performer braced and pushed back by
        /// the recoil, trembling while it strains in a clash.
        /// </summary>
        public ChoreoMove Fire(BeamSpec spec)
        {
            Beat thrust = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.06f,
                To = new Shape(12f, 1.24f, 0.86f, 0f, 0.1f),
                Ease = Ease.OutExpo,
                Forms = Form.Beam | Form.Ki
            };
            thrust.Enter = m =>
            {
                m.AimBeam();
                m.SpendOrb();
                Vector2 at = m.Muzzle();
                m._beam = Beams.Fire(m.A, m, at, m.Aim, spec);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", m.A.current_tile, 0.2f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.Flash(at, m.Px, Mathf.RoundToInt(5f * m.Kk), 0.08f, Color.white, m.TopLayer);
                    Fx.Ring(at, m.Px, 0.2f, 3f, 14f * m.Kk, 2, 1, m.TopLayer, 6, 1.6f, 2.4f, Fx.WithA(m.Hue.Core, 1f), Fx.WithA(m.Hue.Edge, 0f));
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 40f, 0.5f, m.Kk);
                }
                Ruin.Quake(m.A, m.Pose.Ground, m.Scale, 0.8f, m.Kk);
            };
            Add(thrust);
            Beat hold = new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.2f,
                To = new Shape(8f, 1.16f, 0.9f, 0f, 0.06f),
                Ease = Ease.OutQuad,
                Until = m => m._beam == null || m._beam.Done || !m._beam.Firing,
                MaxDur = 6f,
                Track = false,
                Forms = Form.Beam | Form.Ki
            };
            hold.Every = (m, k) => m.BeamRecoil();
            return Add(hold);
        }

        /// <summary>Points the beam: at the target if there is one, else along the plan's direction.</summary>
        private void AimBeam()
        {
            if (V != null && VOk)
            {
                Vector2 d = VPoint(0.5f) - Muzzle();
                if (d.sqrMagnitude > 0.01f)
                {
                    Aim = d.normalized;
                }
            }
            Face(Aim);
        }

        private float _recoilT, _strain;

        private void BeamRecoil()
        {
            float dt = Clock.Dt;
            _strain = Mathf.MoveTowards(_strain, 0f, dt * 4f);
            // the recoil pushes it back along the ground, slowly
            Vector2 back = Stance - Aim * dt * (0.35f + 0.6f * _strain) * Kk;
            if (Combat.SafeSpot(back))
            {
                Stance = back;
            }
            Pose.Nudge = Choreo.Tremble(BeatT, 0.4f + 0.6f * _strain);
            _recoilT -= dt;
            if (_recoilT <= 0f && Vis)
            {
                _recoilT = 0.08f;
                ChoreoFx.HeelDust(Pose.Ground, -Aim, Px, A.current_tile, Kk);
            }
        }

        /// <summary>Its beam is locked in a clash: it strains (trembles harder, pushed back).</summary>
        public void Strain(float dt)
        {
            _strain = 1f;
        }
    }
}
