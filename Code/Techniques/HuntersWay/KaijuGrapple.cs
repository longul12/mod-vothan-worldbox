using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KAIJU GRAPPLE (Hunter's Way, God). A tiny figure takes a giant monster head-on: a landed
    /// dragon, a titan, Crabzilla. It blurs in under the giant's head and seizes it (both arms, a
    /// flash and focus lines at the grip), and for a heartbeat the two strain against each other:
    /// the God's feet sink, the ground cracks and caves round them, dust swirls, both tremble.
    /// Then it straightens and lifts: the giant leaves the ground, swung up and over the God's head
    /// in a great arc, and comes down behind it on its back: a crater, a ring of dust racing out,
    /// everyone near knocked flat, the giant knocked out (a dragon held down, stars round its head).
    /// What nothing can lift (the player's Crabzilla) is wrestled down where it stands instead,
    /// driven into the ground in the same ruin. For a real giant threat at its tier (a Heavy
    /// answer: a God flicks a dragon that is no threat to it, and grapples what is). I, II, III: the
    /// throw higher and further, the crash wider. Damage (x the user's damage): the grip 0.8, the
    /// crash I 5.2, II 7.2, III 9.7. Chains from Close and Down; leaves Down.
    /// </summary>
    internal sealed class KaijuGrappleTech : Technique
    {
        public override string Id => "kaiju_grapple";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Single;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Grab | Form.Hunt | Form.Heavy;
        public override Prey Prey => Prey.Kaiju | Prey.Titan | Prey.Dragon;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 120f, 160f, 200f };
        public override float[] Cooldown => new[] { 60f, 60f, 70f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Crash(int level)
        {
            return level <= 1 ? 5.2f : level == 2 ? 7.2f : 9.7f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            MeleeWant w = new MeleeWant
            {
                Reach = 7f + level,
                Total = 0.8f + Crash(level),
                Worthy = true,
                Crowd = 0f,
                Alone = 16f,
                IntoGuard = 1f,
                Worth = 60f
            };
            // a giant standing on the ground (a dragon in the sky is the harpoon's, the rider's or the flick's)
            Plan p = BigGame.Pick(c, level, w, Prey, c.Forced);
            if (p == null || c.Forced)
            {
                return p;
            }
            Actor v = p.Target;
            if (v.isFlying())
            {
                return null;
            }
            // something to grapple: bigger than the God itself
            if (BigGame.Size(v).Top < 1.5f * Choreo.SpritePx(c.Self) * Choreo.PxOf(c.Self))
            {
                return null;
            }
            int near = c.CountNear(v.current_position, BigGame.Size(v).Half + 2f) - 1;
            p.Score += 40f + Mathf.Max(0, near) * 10f;
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            Grapple g = new Grapple(m);
            int level = m.Level;
            // in under its head
            m.Dash(mm => g.Front(mm), 0.022f).OnlyIf(mm => g.Far(mm));
            // the seize: arms out, the grip
            m.PoseTo(0.08f, new Shape(-8f, 0.94f, 1.06f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind).Shows(Form.Grab | Form.Hunt);
            Beat seize = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = new Shape(16f, 1.16f, 0.9f, 0f, 0.16f), Ease = Ease.OutExpo, Forms = Form.Grab | Form.Hunt };
            seize.On(0.5f, mm => g.Seize(mm));
            m.Add(seize);
            // the strain: feet sinking, the ground caving, both trembling
            m.PoseTo(level <= 1 ? 0.4f : 0.34f, new Shape(-14f, 1.18f, 0.84f, 0f, 0.06f), Ease.OutQuad, Tempo.Free)
                .Every((mm, k) => g.Strain(mm, k))
                .OnlyIf(mm => g.Held)
                .Shows(Form.Grab | Form.Hunt | Form.Heavy);
            // the lift: up it goes, over its head
            Beat lift = new Beat { Tempo = Tempo.Act, Dur = 0.16f, To = new Shape(-34f, 0.84f, 1.22f, 0f, -0.04f), Ease = Ease.OutCubic, Forms = Form.Grab | Form.Throw | Form.Hunt | Form.Heavy, Track = false };
            lift.If = mm => g.Held;
            lift.On(0.3f, mm => g.Lift(mm));
            lift.Every = (mm, k) => g.Carry(mm);
            m.Add(lift);
            // the follow-through: arched back after the throw, watching it go over
            m.PoseTo(0.14f, new Shape(-58f, 0.9f, 1.1f, 0f), Ease.OutQuad, Tempo.Follow).Every((mm, k) => g.Watch(mm)).OnlyIf(mm => g.Thrown);
            m.WaitUntil(mm => g.Down, 2.4f).Every((mm, k) => g.Watch(mm)).OnlyIf(mm => g.Thrown);
            m.Do(mm => g.Crash(mm));
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.14f, To = new Shape(-20f, 1.08f, 0.94f, 0f), Ease = Ease.OutQuad, Track = false });
            m.Recover(0.3f);
            return m.Start();
        }
    }

    /// <summary>One Kaiju Grapple: the grip, the strain, the lift and throw, the crash.</summary>
    internal sealed class Grapple
    {
        private readonly ChoreoMove _m;
        private readonly Actor _v;
        private readonly long _vid;
        private Wingbreaker.Box _box;
        private float _side;
        private bool _held, _thrown, _crashed, _up, _wrestled;
        private float _dust, _thrownT;
        private Vector2 _from;
        /// <summary>
        /// A giant the game draws itself (a dragon) does not show the height the game's forces give it:
        /// it is carried through the arc by hand instead (its ground point swung up and over).
        /// </summary>
        private bool _carry;
        private float _carryK, _carryDur, _carryH;
        private Vector2 _carryTo;

        public Grapple(ChoreoMove m)
        {
            _m = m;
            _v = m.V;
            _vid = _v.Sid();
            _box = BigGame.Size(_v);
            Vector2 d = m.Stance - _v.current_position;
            _side = Mathf.Abs(d.x) > 0.05f ? Mathf.Sign(d.x) : -m.Fwd;
        }

        private bool Ok => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        private bool Same => _v != null && _v.Sid() == _vid;

        /// <summary>It has it in its grip.</summary>
        public bool Held => _held && Ok;

        /// <summary>It is in the air, thrown.</summary>
        public bool Thrown => _thrown && Same;

        /// <summary>The thrown giant has come down (or the wait is over).</summary>
        public bool Down
        {
            get
            {
                if (!Ok)
                {
                    return true;
                }
                if (_carry)
                {
                    return _carryK >= 1f;
                }
                if (_v.under_forces || _v.position_height > 0.05f)
                {
                    _up = true;
                    return false;
                }
                return _up || _m.T - _thrownT > 0.5f;
            }
        }

        /// <summary>In front of it, under its head (solid ground).</summary>
        public Vector2 Front(ChoreoMove m)
        {
            Vector2 want = Combat.Clamp(m.VG + new Vector2(_side * (_box.Half * 0.7f + 0.35f * m.Kk), -0.08f));
            if (Combat.SafeSpot(want))
            {
                return want;
            }
            return Combat.FindLanding(want, 2f, out Vector2 q) ? q : m.Stance;
        }

        public bool Far(ChoreoMove m)
        {
            return Vector2.Distance(m.Stance, Front(m)) > 0.4f * m.Kk;
        }

        /// <summary>Where its hands are on the giant: as high up its body as it can reach.</summary>
        private Vector2 GripPoint(ChoreoMove m)
        {
            float reach = m.Hpx * m.Px * 1.1f;
            return m.VG + new Vector2(_side * _box.Half * 0.55f, Mathf.Min(_box.Center, reach));
        }

        private void FaceIt(ChoreoMove m)
        {
            float dx = m.VG.x - m.Stance.x;
            if (Mathf.Abs(dx) > 0.03f)
            {
                m.Face(new Vector2(dx, 0f));
            }
        }

        public void Seize(ChoreoMove m)
        {
            FaceIt(m);
            if (!Ok || !m.Touch(_v))
            {
                return;
            }
            Vector2 at = GripPoint(m);
            HitResult r = Combat.Hit(m.A, _v, 0.8f * m.Cm, false, false, 0f, m);
            m.LastHit = r;
            _held = true;
            Combat.Pin(_v, 2f);
            try
            {
                _v.cancelAllBeh();
            }
            catch
            {
                // (its AI mid-rebuild)
            }
            Victims.Flash(_v, Color.white, 0.06f);
            Choreo.Sound("event:/SFX/OTHER/Punch", _v.current_tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", _v.current_tile, 0.1f);
            if (m.Vis)
            {
                Fx.HitSpark(at, m.Px, 1, -m.Dir, 4);
                Fx.FocusLines(at, m.Px, Fx.Count(8), 10f * m.Kk, 18f * m.Kk, 0.12f);
                Kit.Flash(at, m.Px, 4, 0.06f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
            }
            m.HoldFor(0.06f * m.ImpactK);
            if (Dev.On) Dev.Trace("kaiju_grapple seizes " + _v.asset.id + "#" + _vid + " (box top " + _box.Top.ToString("F1") + ", half " + _box.Half.ToString("F1") + ")");
        }

        /// <summary>The strain: its feet sink, the ground caves, dust swirls, both tremble.</summary>
        public void Strain(ChoreoMove m, float k)
        {
            if (!Ok)
            {
                return;
            }
            FaceIt(m);
            m.Pose.Nudge = Choreo.Tremble(m.BeatT, 0.6f + 0.6f * k);
            Combat.Pin(_v, 1f);
            if (Random.value < 0.25f)
            {
                _v.startShake(0.1f, 0.08f, true, false);
            }
            _dust -= Clock.Dt;
            if (_dust <= 0f)
            {
                _dust = 0.1f;
                if (m.Vis)
                {
                    ChoreoFx.HeelDust(m.Pose.Ground, -m.Dir, m.Px, m.A.current_tile, m.Kk * 1.3f);
                    Fx.Ring(m.Pose.Ground, m.Px, 0.18f, 3f, 9f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(m.Hue.Core, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                    if (Random.value < 0.5f)
                    {
                        Kit.Debris(m.Pose.Ground, m.Px, m.A.current_tile, 2, m.Kk);
                    }
                }
            }
            if (k > 0.5f && !_sunk)
            {
                // the ground gives way under the strain
                _sunk = true;
                Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", m.A.current_tile, 0.1f);
                if (m.Vis)
                {
                    Kit.Crater(m.Pose.Ground, m.Px * m.Kk, m.A.current_tile, false, 4f, true, m.Hue.Edge);
                    Kit.DustRing(m.Pose.Ground, m.Px, m.A.current_tile, 10, 40f, 0.5f, m.Kk);
                }
                Ruin.Blast(m.A, m.Pose.Ground, 0.9f, Scale.Casual, m.Pose.Ground, false);
                Ruin.Quake(m.A, m.Pose.Ground, m.Scale, 0.6f, m.Kk);
            }
        }

        private bool _sunk;

        /// <summary>The lift: up and over its head (a throw by the game's own forces); what can't be lifted is wrestled down.</summary>
        public void Lift(ChoreoMove m)
        {
            if (!Ok)
            {
                return;
            }
            int level = m.Level;
            _from = _v.current_position;
            // over the God's head and down behind it
            Vector2 dir = m.Stance - _from;
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = -m.Dir;
            }
            dir.Normalize();
            float dist = Vector2.Distance(m.Stance, _from) * 2f + _box.Half + 2f + 1.2f * level;
            float apex = 3f + 1.2f * level;
            // a clear landing on dry land, or a shorter throw
            Vector2 land = _from + dir * dist;
            if (!Combat.SafeSpot(Combat.Clamp(land)))
            {
                if (Combat.FindLanding(land, 3f, out Vector2 q))
                {
                    Vector2 d2 = q - _from;
                    dist = d2.magnitude;
                    if (dist > 0.5f)
                    {
                        dir = d2 / dist;
                    }
                }
                else
                {
                    dist *= 0.5f;
                }
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
            bool drawn = _v.asset.has_sprite_renderer && !_v.asset.has_avatar_prefab && !_v.asset.ignore_generic_render;
            if (!drawn && _v.asset.can_be_moved_by_powers && !ControllableUnit.isControllingUnit(_v) && !_v.is_immovable && !_v.isInMagnet())
            {
                // swung through the arc by hand: up over the God's head and down behind it
                _carry = true;
                _carryK = 0f;
                _carryTo = Combat.Clamp(_from + dir * dist);
                _carryDur = 0.5f + 0.03f * dist;
                _carryH = apex * 0.9f;
                _thrown = true;
                _thrownT = m.T;
                m.Face(dir);
                if (m.Vis)
                {
                    Kit.SpeedLines(GripPoint(m), Vector2.up, m.Px, 4, 6f, 0.16f, Fx.WithA(Color.white, 0.85f));
                    Kit.DustRing(_from, Mathf.Max(0.1f, m.Px), Combat.TileAt(_from), 14, 60f, 0.7f, Choreo.KOf(_v));
                }
                return;
            }
            if (BigGame.Hurl(_v, dir, dist, apex))
            {
                _thrown = true;
                _thrownT = m.T;
                m.Face(dir);
                Trails.Follow(_v, 1.6f, Fx.WithA(m.Hue.Ghost, 0.45f));
                if (m.Vis)
                {
                    Kit.SpeedLines(GripPoint(m), Vector2.up, m.Px, 4, 6f, 0.16f, Fx.WithA(Color.white, 0.85f));
                    Kit.DustRing(_from, Mathf.Max(0.1f, m.Px), Combat.TileAt(_from), 14, 60f, 0.7f, Choreo.KOf(_v));
                }
                return;
            }
            // too much even for a God to throw: wrestled down where it stands
            _wrestled = true;
            _thrown = true;
            _thrownT = m.T;
            _up = true;
        }

        /// <summary>A giant carried through the arc: its ground point swung along it this frame.</summary>
        public void Carry(ChoreoMove m)
        {
            if (!_carry || !Ok || _carryK >= 1f)
            {
                return;
            }
            _carryK = Mathf.Clamp01((m.T - _thrownT) / Mathf.Max(0.1f, _carryDur));
            float e = Beat.Apply(Ease.InOutSine, _carryK);
            Vector2 p = Vector2.Lerp(_from, _carryTo, e) + new Vector2(0f, _carryH * 4f * _carryK * (1f - _carryK));
            Toolbox.clampToMap(ref p);
            try
            {
                _v.stopMovement();
                _v.cancelAllBeh();
                _v.makeWait(0.3f);
            }
            catch
            {
                // (its AI mid-rebuild)
            }
            _v.current_position = p;
            _v.position_height = 0f;
            _v.dirty_current_tile = true;
            Combat.Pin(_v, 0.5f);
            if (m.Vis && Random.value < 0.5f)
            {
                Trails.Ghost(_v, 0.16f, Fx.WithA(m.Hue.Ghost, 0.45f), Fx.WithA(m.Hue.Edge, 0f));
            }
            if (_carryK >= 1f)
            {
                _v.current_position = _carryTo;
            }
        }

        /// <summary>While it flies: the God turns to watch it come down.</summary>
        public void Watch(ChoreoMove m)
        {
            Carry(m);
            if (!Same || _wrestled)
            {
                return;
            }
            Vector2 d = _v.current_position - m.Stance;
            if (Mathf.Abs(d.x) > 0.1f)
            {
                m.Face(new Vector2(d.x, 0f));
            }
        }

        /// <summary>The crash: it comes down on its back, the ground breaks, everyone near is thrown down.</summary>
        public void Crash(ChoreoMove m)
        {
            if (_crashed || !_held || !Same)
            {
                return;
            }
            _crashed = true;
            int level = m.Level;
            Vector2 at = _v.current_position;
            if (Combat.Living(_v))
            {
                HitResult r = Combat.Hit(m.A, _v, KaijuGrappleTech.Crash(level) * m.Cm, true, false, 0f, m);
                m.LastHit = r;
                Victims.Flash(_v, Color.white, 0.08f);
                if (Combat.Living(_v))
                {
                    BigGame.Topple(_v, 3f + 0.8f * level, m.A, 0, 0f);
                }
            }
            float r0 = (_box.Half + 2f) * (1f + 0.2f * level);
            if (Kit.Visible(at, 0.6f))
            {
                Kit.ImpactFrame(at + new Vector2(0f, 1.5f), Mathf.Max(0.1f, m.Px), 0.6f + 0.15f * level, m.Hue.Pal, level >= 3, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
            }
            BigGame.Thud(m, at, r0, 1.6f + 0.2f * level, true, _v);
            if (!_wrestled && level >= 2)
            {
                // where it was torn out of the ground, a furrow
                Ruin.Furrow(m.A, _from, at, 1f + 0.3f * level, m.Scale, m.Pose.Ground, false);
            }
            m.AreaBlow(at, r0, 0.8f + 0.25f * level, _v, Hurt.Knockdown, 1.2f);
            Kit.Stop(at, (0.06f + 0.02f * level) * m.ImpactK);
            m.SetLink(Link.Down, _v);
            if (Dev.On) Dev.Trace("kaiju_grapple crash " + _v.asset.id + (_wrestled ? " (wrestled down where it stood)" : " (thrown " + Vector2.Distance(_from, at).ToString("F1") + " tiles)"));
        }
    }
}
