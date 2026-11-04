using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BLUR DASH. A master doesn't walk up to an enemy a few tiles away: it is suddenly there. A
    /// crouch, then short blinks along the ground (a zig-zag, each 2-4 tiles), reappearing for a
    /// heartbeat between them, and it lands in reach, ready to strike. Read as speed, not as a
    /// teleport: every hop leaves its afterimages and kicks up dust (FlowStrikes starts it, at
    /// most once every 0.6-0.8 s).
    ///
    /// Beat sheet (a Master closing 6 tiles; Grandmasters x0.9, Gods x0.8):
    ///
    ///   CROUCH  0.00-0.05  squash 118% x 80%, leaning in; dust kicked back from the heels
    ///   BLINK   0.05-0.14  one frame stretched wide under a white flash, then gone (nothing can touch
    ///                      it): two afterimages along the way (white heart, tier edge, the later one
    ///                      stronger), tier speed lines, a few low dust specks
    ///   APPEAR  0.14-0.19  out of the blur, stretched and leaning back, skidding, heel dust
    ///   BLINK   0.19-0.28  the next hop (the zig goes the other way; the last one runs straight at it)
    ///   APPEAR  0.28-0.33
    ///   SETTLE  0.33-0.40  springs upright, facing it, in reach
    ///
    /// Hops are up to 3.2 tiles for a Master, 3.6 a Grandmaster, 4 a God; as many as it takes, at most
    /// three (a God four), so a Master closes 6 tiles in two and a God crosses 14 in four. A God's
    /// kick-off scorches the ground it leaves (Kit.Ravage, force 1, no flames).
    /// Experts (a weaker version, now and then): a single hop of up to 3 tiles, in the level II look.
    /// It never lands on a wall, water, lava or fire, or off the map (Combat.SafeSpot/ClearPath):
    /// a hop that can't zig goes straight, one that can't go straight goes shorter, or the dash
    /// ends where it is. If the target is gone, it ends at once.
    /// </summary>
    internal sealed class BlurDash : Move
    {
        /// <summary>
        /// Any technique may cut it short (it is between-techniques fighting), but not while it is a
        /// blur between two spots: it comes out of the blur first (a few hundredths), then the
        /// technique starts from where it appears.
        /// </summary>
        public override bool Interruptible => Finished || _phase != Phase.Blink;

        private enum Phase
        {
            Crouch,
            Blink,
            Appear,
            Settle
        }

        private readonly Actor _v;
        private readonly long _vid;
        private readonly Palette _pal;
        private readonly float _k, _s, _hopMax, _stop;
        private readonly int _maxHops;
        private readonly bool _crab, _expert, _god;

        private Phase _phase = Phase.Crouch;
        private float _pt, _tBlink;
        private bool _entered;
        private Vector2 _from, _to, _dir;
        private int _hops, _zig;
        private float _rot0, _sx0 = 1f, _sy0 = 1f;

        private BlurDash(Actor a, Actor v, Tier tier) : base(a)
        {
            _v = v;
            _vid = v.Sid();
            _expert = tier < Tier.Master;
            _god = tier >= Tier.God;
            Level = _expert ? 2 : 3;
            _pal = Palette.For(tier, Level);
            _k = K;
            _crab = FlashStep.IsCrab(a);
            _s = tier >= Tier.God ? 0.8f : tier == Tier.Grandmaster ? 0.9f : tier == Tier.Master ? 1f : 1.1f;
            _maxHops = tier >= Tier.God ? 4 : tier >= Tier.Master ? 3 : 1;
            _hopMax = (tier >= Tier.God ? 4f : tier == Tier.Grandmaster ? 3.6f : tier == Tier.Master ? 3.2f : 3f) * Mathf.Lerp(1f, _k, 0.5f);
            // land inside its own reach (the game's attack range), never on top of the enemy
            float range = 1f;
            try
            {
                range = a.getAttackRange();
            }
            catch
            {
                // the default stands
            }
            _stop = Mathf.Clamp(range + v.stats["size"] - 0.35f, 0.7f * _k, 2.2f);
            _zig = Random.value < 0.5f ? -1 : 1;
            _dir = (v.current_position - a.current_position).normalized;
            if (!_crab && Mathf.Abs(_dir.x) > 0.12f)
            {
                Pose.FaceRight = _dir.x > 0f;
            }
        }

        /// <summary>Nothing can touch it while it is a blur.</summary>
        public override bool Intangible => _phase == Phase.Blink;

        /// <summary>Closing in as a blur (readers see a dash coming).</summary>
        public override Form Forms => Form.Dash | Form.Fast | Form.Evasive;

        /// <summary>The one it closes on.</summary>
        public override Actor Aim => _v;

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.4f);

        public static bool Begin(Actor a, Actor v, Tier tier)
        {
            if (a == null || v == null || !v.isAlive() || !Moves.CanPerform(a))
            {
                return false;
            }
            BlurDash m = new BlurDash(a, v, tier);
            if (!m.Plan())
            {
                return false;   // no clean first hop: it keeps walking
            }
            if (!Moves.Start(m))
            {
                return false;
            }
            FlowStrikes.Live++;
            if (Dev.On) Dev.Trace("blur " + m.Id + " (" + tier + ") at " + v.asset.id + "#" + v.Sid() + " " + Vector2.Distance(a.current_position, v.current_position).ToString("F1") + " tiles");
            return true;
        }

        private bool TargetOk()
        {
            return _v != null && _v.isAlive() && _v.Sid() == _vid && !_v.isInMagnet() && !_v.isInsideSomething() && Combat.Grounded(_v);
        }

        /// <summary>
        /// The next hop from where it stands: up to its length towards the enemy (a zig to the side
        /// unless it is the last), on safe ground with nothing in the way. False: no hop worth it.
        /// </summary>
        private bool Plan()
        {
            if (!TargetOk() || _hops >= _maxHops)
            {
                return false;
            }
            Vector2 p = Pose.Ground;
            Vector2 t = _v.current_position;
            Vector2 to = t - p;
            float d = to.magnitude;
            float want = d - _stop;
            if (want < 0.9f * _k)
            {
                return false;   // already close enough to strike
            }
            Vector2 u = to / d;
            Vector2 side = new Vector2(-u.y, u.x);
            float len = Mathf.Min(want, _hopMax);
            bool last = want <= _hopMax || _hops == _maxHops - 1;
            float lateral = last ? 0f : _zig * Random.Range(0.35f, 0.7f) * _k;
            for (int tryNo = 0; tryNo < 3; tryNo++)
            {
                Vector2 end = Combat.Clamp(p + u * len + side * lateral);
                if (Combat.SafeSpot(end) && Combat.ClearPath(p, end))
                {
                    _from = p;
                    _to = end;
                    _dir = (end - p).normalized;
                    _zig = -_zig;
                    _tBlink = (0.085f + 0.008f * Mathf.Max(0f, len - 2f)) * _s;
                    return true;
                }
                if (lateral != 0f)
                {
                    lateral = 0f;   // no room for the zig: straight
                }
                else
                {
                    len *= 0.6f;    // something in the way: a shorter hop
                    if (len < 1.2f)
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        private void Next(Phase p)
        {
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

        public override void Tick(float dt)
        {
            _pt += dt;
            switch (_phase)
            {
                case Phase.Crouch:
                    Crouch();
                    break;
                case Phase.Blink:
                    Blink();
                    break;
                case Phase.Appear:
                    Appear();
                    break;
                default:
                    Settle();
                    break;
            }
            if (!Finished && T > 2f)
            {
                Neutral();
                Finished = true;
            }
        }

        private void Crouch()
        {
            float px = Px;
            if (Enter() && Visible)
            {
                FlashStep.HeelDust(Pose.Ground, -_dir, px, A.current_tile, _k);
            }
            float t = (_expert ? 0.06f : 0.045f) * _s;
            float c = Anim.OutCubic(Mathf.Clamp01(_pt / (t * 0.75f)));
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(1f, _crab ? 1.25f : 1.18f, c);
            Pose.SY = Mathf.Lerp(1f, 0.8f, c);
            Pose.Rot = _crab ? 0f : 10f * c;
            Pose.Nudge = Vector2.zero;
            if (_pt >= t)
            {
                Next(Phase.Blink);
            }
        }

        private void Blink()
        {
            if (Enter())
            {
                if (_hops > 0 && !Plan())
                {
                    Next(Phase.Settle);
                    return;
                }
                _hops++;
                if (!_crab && Mathf.Abs(_dir.x) > 0.12f)
                {
                    Pose.FaceRight = _dir.x > 0f;
                }
                FlowStrikes.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);   // (shared with every flow move's whoosh: a war doesn't drown in them)
                if (Visible)
                {
                    BlinkFx();
                }
                if (_god)
                {
                    // a God tears off the ground: the spot it kicks off from is scorched
                    Kit.Ravage(_from, 0.5f, 1, A, false);
                }
            }
            float k = Mathf.Clamp01(_pt / _tBlink);
            Pose.Ground = Vector2.Lerp(_from, _to, k);
            Pose.Hidden = true;
            Pose.HideShadow = true;
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Pose.Ground = _to;
                Next(Phase.Appear);
            }
        }

        /// <summary>
        /// Everything the blur leaves on the way, spawned now and timed to appear as it passes:
        /// the vanish flash and the afterimage it leaves standing, two afterimages along the path,
        /// speed lines, a few dust specks.
        /// </summary>
        private void BlinkFx()
        {
            float px = Px;
            // the vanish frame: the sprite stretched wide under a white flash, an afterimage left behind
            Pose keep = Pose;
            Pose.SX = _crab ? 1.6f : 1.5f;
            Pose.SY = 0.82f;
            Pose.Rot = 0f;
            Pose.Hidden = false;
            Particle flash = Fx.Afterimage(this, 0.06f, new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
            if (flash != null)
            {
                flash.R.sortingOrder += 3;
            }
            Pose = keep;
            Ghost(_from, 0.24f, Fx.WithA(_pal.Ghost, 0.7f), _pal.GhostEnd, 0f);
            Vector2 lift = new Vector2(0f, Pose.PivotUp * A.actor_scale);
            for (int j = 0; j < 2; j++)
            {
                float f = 0.35f + 0.3f * j;
                float a = 0.5f + 0.2f * j;
                Ghost(Vector2.Lerp(_from, _to, f), 0.14f, Fx.WithA(_pal.Core, a), Fx.WithA(_pal.Edge, a * 0.7f), _pal.GhostEnd, f * _tBlink);
            }
            Vector2 side = new Vector2(-_dir.y, _dir.x);
            float ang = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg - 90f;
            int lines = Fx.Count(_expert ? 3 : 4);
            for (int i = 0; i < lines; i++)
            {
                float f = (i + 0.5f) / lines;
                Particle l = Fx.Spawn(Art.Lines[Random.Range(1, 3)], Vector2.Lerp(_from, _to, f) + lift + side * Random.Range(-4f, 4f) * px, px,
                    Random.Range(0.12f, 0.17f), FxLayer.Top, 1);
                if (l == null)
                {
                    break;
                }
                l.Rot = ang;
                l.Moving(_dir * 30f * px);
                l.Delay = f * _tBlink;
                Color c = (i & 1) == 0 && !_expert ? Fx.WithA(_pal.Core, 0.9f) : Fx.WithA(_pal.Edge, 0.85f);
                l.Color(c, Fx.WithA(c, 0f));
            }
            int n = Fx.Count(2);
            for (int i = 0; i < n; i++)
            {
                float f = (i + 0.5f) / n;
                Vector2 g = Vector2.Lerp(_from, _to, f);
                Color dust = Fx.Lighten(Fx.SoilColor(Combat.TileAt(g)), 0.35f);
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 2)], g + side * Random.Range(-1f, 1f) * px, px, Random.Range(0.24f, 0.32f), FxLayer.Ground, 4);
                if (p == null)
                {
                    break;
                }
                p.Moving(-_dir * Random.Range(8f, 14f) * px, Random.Range(1f, 3f) * px, 5f).Sized(0.45f, 0.8f)
                    .Color(Fx.WithA(dust, 0.6f), Fx.WithA(dust, 0.3f), Fx.WithA(dust, 0f));
                p.Delay = f * _tBlink;
            }
        }

        /// <summary>An afterimage of the unit standing somewhere along its path (a dash pose), appearing after 'delay'.</summary>
        private void Ghost(Vector2 ground, float life, Color c0, Color c1, Color c2, float delay)
        {
            Pose keep = Pose;
            Pose.Ground = ground;
            Pose.Height = 0f;
            Pose.Rot = _crab ? 0f : 12f;
            Pose.SX = 1.2f;
            Pose.SY = 0.9f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Particle g = Fx.Afterimage(this, life, c0, c1, c2);
            if (g != null)
            {
                g.Delay = delay;
            }
            Pose = keep;
        }

        private void Ghost(Vector2 ground, float life, Color c0, Color c1, float delay)
        {
            Pose keep = Pose;
            Pose.Ground = ground;
            Pose.Height = 0f;
            Pose.Rot = _crab ? 0f : 8f;
            Pose.SX = 1.2f;
            Pose.SY = 0.8f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Particle g = Fx.Afterimage(this, life, c0, c1);
            if (g != null)
            {
                g.Delay = delay;
            }
            Pose = keep;
        }

        private void Appear()
        {
            float px = Px;
            if (Enter())
            {
                Pose.Hidden = false;
                Pose.HideShadow = false;
                if (Visible)
                {
                    FlashStep.HeelDust(_to, -_dir, px, A.current_tile, _k);
                    Ghost(_to - _dir * 0.3f * _k, 0.12f, Fx.WithA(_pal.Core, 0.85f), Fx.WithA(_pal.Edge, 0.6f), _pal.GhostEnd, 0f);
                }
            }
            float t = 0.05f * _s;
            float k = Mathf.Clamp01(_pt / t);
            float s = Anim.OutQuad(k);
            // out of the blur, still stretched, leaning back against the skid
            Vector2 skid = _to + _dir * 0.12f * _k * Anim.OutCubic(k);
            Pose.Ground = Combat.SafeSpot(skid) ? skid : _to;
            Pose.Height = 0f;
            Pose.SX = Mathf.Lerp(_crab ? 1.35f : 1.28f, 1.04f, s);
            Pose.SY = Mathf.Lerp(0.8f, 0.96f, s);
            Pose.Rot = _crab ? 0f : Mathf.Lerp(-12f, -4f, s);
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                _to = Pose.Ground;
                if (_hops < _maxHops && TargetOk() && Vector2.Distance(Pose.Ground, _v.current_position) - _stop >= 0.9f * _k)
                {
                    Next(Phase.Blink);   // straight into the next hop
                }
                else
                {
                    Next(Phase.Settle);
                }
            }
        }

        private void Settle()
        {
            if (Enter())
            {
                _rot0 = Pose.Rot;
                _sx0 = Pose.SX;
                _sy0 = Pose.SY;
                Pose.Hidden = false;
                Pose.HideShadow = false;
                if (TargetOk() && !_crab)
                {
                    float dx = _v.current_position.x - Pose.Ground.x;
                    if (Mathf.Abs(dx) > 0.04f)
                    {
                        Pose.FaceRight = dx > 0f;
                    }
                }
            }
            float t = 0.07f * _s;
            float amp = Anim.Spring(_pt, 11f, 22f);
            Pose.SX = 1f + (_sx0 - 1f) * amp;
            Pose.SY = 1f + (_sy0 - 1f) * amp;
            Pose.Rot = _rot0 * Mathf.Exp(-14f * _pt);
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            if (_pt >= t)
            {
                Neutral();
                Finished = true;
            }
        }

        private void Neutral()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (!Combat.SafeSpot(Pose.Ground))
            {
                Pose.Ground = Combat.SafeSpot(_from) ? _from : Pose.Ground;
            }
        }

        public override void OnEnd(bool aborted)
        {
            FlowStrikes.Live = Mathf.Max(0, FlowStrikes.Live - 1);
            if (!aborted)
            {
                FlowStrikes.After(A, Link.None, null);   // in reach: the Brain looks at once
            }
        }
    }
}
