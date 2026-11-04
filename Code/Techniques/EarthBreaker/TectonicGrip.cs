using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// TECTONIC GRIP (Earth Breaker, Master). It rears up with both fists over its head and drives them
    /// into the earth: cracks race out to every enemy around it, and under each one a hand of stone
    /// claws rises out of the ground and closes on it (held, folded in the grip, unable to act). It
    /// heaves up out of its crouch with its fists clenched, and every stone hand crushes what it holds:
    /// the claws squeeze, rock splinters, and the foes crumple out of them (Down). II squeezes twice;
    /// III three times, and on the last the hands burst apart, flinging the foes up (Launched) and
    /// breaking the ground under each. Before the slam it strides into the middle of the group. A grab:
    /// it beats a guard. The foes in water, in the air or in another's hands are left alone. Giants are
    /// gripped by bigger claws and rocked where they stand. Radius I 3, II 4, III 5 tiles (x size),
    /// up to 6/8/10 hands. Damage to each foe (x the user's damage): I 0.25 + 1.35; II 0.25 + 0.55 +
    /// 1.2; III 0.25 + 0.5 + 0.9 + 0.9.
    /// </summary>
    internal sealed class TectonicGripTech : Technique
    {
        public override string Id => "tectonic_grip";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Area;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Ground | Form.Grab | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 22f, 38f, 60f };
        public override float[] Cooldown => new[] { 11f, 12f, 14f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down || link == Link.Scattered;
        }

        public const float GripMult = 0.25f;

        public static float Radius(int level)
        {
            return EarthKit.L(level, 3f, 4f, 5f);
        }

        public static int MaxHands(int level)
        {
            return EarthKit.L(level, 6, 8, 10);
        }

        /// <summary>Each squeeze of the stone hands (x the user's damage), the last one first crushes, then (III) bursts.</summary>
        public static float PulseMult(int level, int pulse)
        {
            if (level <= 1)
            {
                return 1.35f;
            }
            if (level == 2)
            {
                return pulse <= 1 ? 0.55f : 1.2f;
            }
            return pulse <= 1 ? 0.5f : 0.9f;
        }

        public static float Total(int level)
        {
            float t = GripMult;
            for (int i = 1; i <= level; i++)
            {
                t += PulseMult(level, i);
            }
            return t;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.5f);
            float r = Radius(level) * k;
            // the middle of the nearest group within a few strides: it walks into it and grips all of them
            Vector2 at = c.Cluster(r * 0.7f, 0f, r + (c.Forced ? 5f : 2.5f), out int count, out Actor center);
            if (center == null)
            {
                return null;
            }
            if ((at - c.Pos).sqrMagnitude > 0.8f * 0.8f)
            {
                if (!Combat.ClearPath(c.Pos, at) || !Combat.SafeSpot(at) && !Choreo.Wading(at))
                {
                    at = c.Pos;   // no way in: it grips from where it stands
                }
            }
            _near.Clear();
            c.CountNear(at, r, _near);
            float total = Total(level);
            float score = 0f;
            int n = 0;
            bool worthy = false;
            Actor best = null;
            float bestD = float.MaxValue;
            int max = MaxHands(level);
            for (int i = 0; i < _near.Count && n < max; i++)
            {
                Actor v = _near[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                {
                    continue;
                }
                n++;
                score += c.Value(v, c.Expected(v, total), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 1.4f) * cm)
                {
                    worthy = true;
                }
                float d = (v.current_position - c.Pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                // a crowd's technique: two at least (one when chained), and not for a couple of peasants
                if (n < (c.Linked ? 1 : 2) && !(n == 1 && worthy && c.Linked))
                {
                    return null;
                }
                if (!worthy && n < 3)
                {
                    return null;
                }
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = at,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = c.Forced ? Mathf.Max(score, 1f) : score,
                AreaRadius = r,
                Data = c.Forced
            };
        }

        /// <summary>What the stone hands read from the move: how many squeezes it has given, whether it is still there.</summary>
        internal sealed class Cue
        {
            public int Pulse, Pulses, Hands;
            public float MaxDelay;
            public bool Over;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Cue cue = new Cue { Pulses = level };
            float r = Radius(level) * Mathf.Lerp(1f, m.Kk, 0.5f);
            m.Ended2 = (mm, aborted) => cue.Over = true;
            Vector2 spot = p.Point;
            // strides into the middle of the group (heavy, real steps)
            m.RunUp(mm => Combat.SafeSpot(spot) || Choreo.Wading(spot) ? spot : mm.Stance, 0.075f)
                .OnlyIf(mm => (spot - mm.Stance).sqrMagnitude > 0.8f * 0.8f && Combat.ClearPath(mm.Stance, spot));
            // up: both fists raised high over its head, rising on its toes
            m.PoseTo(0.17f, new Shape(-14f, 0.88f, 1.18f, 0.08f, -0.04f), Ease.OutQuad, Tempo.Wind)
                .At(0.75f, mm =>
                {
                    if (mm.Vis && mm.Level >= 2)
                    {
                        Fx.Glint(mm.HeadPos() + new Vector2(0f, 2f * mm.Px), mm.Px, 0.16f, mm.Hue.Core, 0.9f);
                    }
                });
            // down: both fists into the earth
            m.PoseTo(0.07f, new Shape(36f, 1.32f, 0.7f, 0f, 0.14f), Ease.InQuad, Tempo.Act).Ghosting(0.03f);
            m.Do(mm => Slam(mm, cue, r));
            // the fists stay buried, trembling, while the earth's hands rise and close
            Beat hold = new Beat { Tempo = Tempo.Free, Dur = 0.3f, To = new Shape(40f, 1.36f, 0.68f, 0f, 0.15f), Ease = Ease.OutQuad, Tremble = true, Track = false };
            hold.Enter = mm => mm.BeatDur = Mathf.Clamp(cue.MaxDelay + 0.28f, 0.22f, 0.8f);
            float dustT = 0f;
            hold.Every = (mm, k) =>
            {
                dustT -= Clock.Dt;
                if (dustT <= 0f && mm.Vis)
                {
                    dustT = 0.08f;
                    Vector2 g = mm.Pose.Ground + mm.Dir * 0.32f * mm.Kk;
                    ChoreoFx.HeelDust(g, Random.insideUnitCircle, mm.Px, Combat.TileAt(g), mm.Kk * 0.7f);
                }
            };
            m.Add(hold);
            for (int i = 1; i <= level; i++)
            {
                int pulse = i;
                bool last = i == level;
                bool burst = last && level >= 3;
                // the clench: it heaves up out of the crouch, fists pulled in and squeezed (III's last: arms flung wide)
                Shape clench = burst ? new Shape(-18f, 0.86f, 1.22f, 0.06f, -0.04f) : new Shape(-4f + 3f * i, 1.18f, 0.88f, 0f, 0.04f);
                Beat squeeze = new Beat { Tempo = Tempo.Act, Dur = 0.08f, To = clench, Ease = Ease.OutExpo, Track = false };
                squeeze.Enter = mm => Pulse(mm, cue, pulse);
                m.Add(squeeze);
                if (last)
                {
                    m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.16f, To = clench.With(sx: burst ? 0.9f : 1.12f, sy: burst ? 1.16f : 0.92f), Ease = Ease.OutQuad, Tremble = true, Track = false });
                }
                else
                {
                    // eases off a hair and grips again
                    m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.08f, To = new Shape(10f, 1.24f, 0.82f, 0f, 0.08f), Ease = Ease.OutQuad, Tremble = true, Track = false });
                }
            }
            m.Recover(0.26f);
            return m.Start();
        }

        private static readonly List<Actor> _foes = new List<Actor>();
        private static readonly List<float> _delays = new List<float>();
        private static Vector2 _from;
        private static readonly Comparison<Actor> ByDist = (x, y) => (x.current_position - _from).sqrMagnitude.CompareTo((y.current_position - _from).sqrMagnitude);

        /// <summary>The fists hit the ground: a crack runs to every enemy in reach and a hand of stone rises under it.</summary>
        private static void Slam(ChoreoMove m, Cue cue, float r)
        {
            Vector2 g = m.Pose.Ground + m.Dir * 0.32f * m.Kk;
            WorldTile tile = Combat.TileAt(g);
            float px = m.Px;
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            if (m.Vis)
            {
                Kit.Flash(g + new Vector2(0f, 1.5f * px), px, Mathf.RoundToInt(5f * m.Kk), 0.07f, Fx.WithA(m.Hue.Core, 0.95f));
                Kit.DustRing(g, px, tile, 7, 40f, 0.45f, m.Kk);
                Kit.Debris(g, px, tile, 5, m.Kk);
                Kit.Crater(g, px * m.Kk, tile, m.Level >= 3, 5f, true, m.Hue.Edge);
                Kit.Shockwave(g, px, 3f, Mathf.Max(10f, r / px * 0.8f), 0.4f, Fx.WithA(m.Hue.Edge, 0.85f), 2);
            }
            Ruin.Blast(m.A, g, 0.7f, m.Scale, m.Pose.Ground, false);
            Ruin.Quake(m.A, g, m.Scale, 1.2f, m.Kk);
            m.HoldFor(0.05f * m.ImpactK);
            // who the earth takes: nearest first, the target among them
            _foes.Clear();
            Choreo.Foes(m.A, m.Pose.Ground, r, _foes);
            _from = m.Pose.Ground;
            _foes.Sort(ByDist);
            int max = MaxHands(m.Level);
            _delays.Clear();
            for (int i = _foes.Count - 1; i >= 0; i--)
            {
                Actor u = _foes[i];
                if (!Choreo.Fair(m.A, u, out _, true) || u.isInLiquid() && !Choreo.Wading(u.current_position))
                {
                    _foes.RemoveAt(i);
                }
            }
            if (_foes.Count > max)
            {
                _foes.RemoveRange(max, _foes.Count - max);
            }
            cue.MaxDelay = 0f;
            for (int i = 0; i < _foes.Count; i++)
            {
                float d = Vector2.Distance(g, _foes[i].current_position);
                float delay = 0.04f + d / 15f;
                _delays.Add(delay);
                cue.MaxDelay = Mathf.Max(cue.MaxDelay, delay);
            }
            int level = m.Level;
            for (int i = 0; i < _foes.Count; i++)
            {
                Actor u = _foes[i];
                float delay = _delays[i];
                // the grip holds it until the last squeeze (and a moment over)
                float hold = cue.MaxDelay - delay + 0.36f + 0.16f * level;
                if (m.Vis)
                {
                    EarthKit.Crack(g, u.current_position, px, 1.4f, m.Level >= 2 ? m.Hue.Edge : (Color?)null);
                }
                Areas.Add(new StoneHandHazard(m, u, delay, cue, level, hold));
            }
            cue.Hands = _foes.Count;
            if (Dev.On) Dev.Trace("tectonic_grip " + Techniques.Roman(level) + ": " + cue.Hands + " hands within " + r.ToString("F1"));
        }

        /// <summary>A squeeze: every stone hand crushes what it holds (they read the cue).</summary>
        private static void Pulse(ChoreoMove m, Cue cue, int pulse)
        {
            cue.Pulse = pulse;
            bool last = pulse >= cue.Pulses;
            Choreo.Sound(last ? "event:/SFX/HIT/HitStone" : "event:/SFX/HIT/HitGeneric", m.A.current_tile, 0.05f);
            if (m.Vis)
            {
                // the squeeze shows in its fists: a ring closing on them
                Vector2 at = m.Hand(Limb.Fist);
                Fx.Ring(at, m.Px, 0.16f, (8f + 2f * pulse) * m.Kk, 2f, 2, 1, FxLayer.Over, 6, 1f, 1.6f, Fx.WithA(m.Hue.Edge, 0f), Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Core, 0.9f));
                if (last)
                {
                    Kit.Flash(at, m.Px, Mathf.RoundToInt(3f * m.Kk), 0.06f, Fx.WithA(m.Hue.Core, 0.9f));
                }
            }
            Ruin.Quake(m.A, m.Pose.Ground, m.Scale, last ? 1.2f : 0.7f, m.Kk);
            if (last && cue.Hands > 0)
            {
                Kit.Stop(m.Pose.Ground, (m.Level >= 3 ? 0.05f : 0.035f) * m.ImpactK);
                m.SetLink(m.Level >= 3 ? Link.Launched : Link.Down, m.V);
            }
        }
    }

    /// <summary>
    /// One hand of stone (Tectonic Grip): a glowing crack warns under the foe, four claws of rock burst
    /// out of the ground round its feet and close on it (the grip: it folds, held, unable to act),
    /// tremble while the Earth Breaker holds on, squeeze at each of its clenches (rock splinters, the
    /// foe is crushed), and crumble back into the ground; at III the last squeeze bursts the claws apart
    /// and throws the foe into the air. Hits only enemies; an evaded grip closes on air.
    /// </summary>
    internal sealed class StoneHandHazard : Hazard
    {
        private enum Stage
        {
            Wait,
            Rise,
            Close,
            Hold,
            Crumble
        }

        private readonly Actor _v;
        private readonly long _vid;
        private readonly TectonicGripTech.Cue _cue;
        private readonly int _level;
        private readonly float _delay, _hold;
        private Vector2 _at;
        private readonly float _h, _half;
        private readonly int _w;
        private Stage _stage;
        private float _t, _squeeze, _crumble, _tilt;
        private bool _gripped, _burst;
        private int _done;
        private readonly Particle[] _f = new Particle[4];
        private Particle _warn;
        private readonly Color _stone;

        public StoneHandHazard(ChoreoMove m, Actor v, float delay, TectonicGripTech.Cue cue, int level, float hold) : base(m)
        {
            _v = v;
            _vid = v.Sid();
            _cue = cue;
            _level = level;
            _delay = delay;
            _hold = hold;
            _at = Victims.GroundOf(v);
            float vh = Choreo.SpritePx(v) * Choreo.PxOf(v);
            // the claws stand most of the foe's height (a giant gets a giant's hand)
            _h = Mathf.Clamp(vh * 0.82f, 0.55f * K, 9f);
            _half = Mathf.Clamp(vh * 0.3f, 0.22f * K, 3f) + 0.1f * K;
            _w = Mathf.Clamp(Mathf.RoundToInt(_h / Px * 0.28f), 3, 14);
            _stone = Fx.Lighten(Color.Lerp(EarthKit.Stone(_at), m.Hue.Main, level >= 2 ? 0.16f : 0.08f), 0.12f);
            if (Kit.Visible(_at, 0.4f))
            {
                _warn = Fx.Spawn(Art.SmallCracks[Random.Range(0, Art.SmallCracks.Length)].Glow, _at, Px, delay + 0.08f, FxLayer.Ground, 4);
                _warn?.Color(Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.9f), Fx.WithA(Hue.Core, 1f));
            }
        }

        private bool VictimOk => _v != null && _v.Sid() == _vid && Combat.Living(_v);

        public override bool Tick(float dt)
        {
            Age += dt;
            _t += dt;
            if (Age > _delay + 4f || !OwnerOk && _stage != Stage.Crumble)
            {
                StartCrumble();
            }
            switch (_stage)
            {
                case Stage.Wait:
                    if (VictimOk && !_v.isFlying())
                    {
                        _at = Victims.GroundOf(_v);   // the crack follows it until the earth opens
                    }
                    if (Age >= _delay)
                    {
                        _stage = Stage.Rise;
                        _t = 0f;
                        Erupt();
                    }
                    break;
                case Stage.Rise:
                    if (_t >= 0.1f)
                    {
                        _stage = Stage.Close;
                        _t = 0f;
                        Grip();
                    }
                    break;
                case Stage.Close:
                    if (_t >= 0.09f)
                    {
                        _stage = Stage.Hold;
                        _t = 0f;
                    }
                    break;
                case Stage.Hold:
                    while (_done < _cue.Pulse && _stage == Stage.Hold)
                    {
                        _done++;
                        Squeeze(_done);
                    }
                    if (_stage == Stage.Hold && _cue.Over && _done < _cue.Pulses && _t > 0.3f)
                    {
                        StartCrumble();   // the Earth Breaker let go (it fell, or was broken): the hand crumbles
                    }
                    break;
                case Stage.Crumble:
                    _crumble += dt;
                    break;
            }
            _squeeze = Mathf.Max(0f, _squeeze - dt * 6f);
            Draw();
            return _stage != Stage.Crumble || _crumble < 0.4f;
        }

        private void StartCrumble()
        {
            if (_stage == Stage.Crumble)
            {
                return;
            }
            bool risen = _stage != Stage.Wait;
            _stage = Stage.Crumble;
            _crumble = risen ? 0f : 1f;   // (a hand that never rose just goes)
            if (risen && Kit.Visible(_at, 0.4f) && !_burst)
            {
                Kit.Debris(_at, Px, Combat.TileAt(_at), 4, K * 0.7f, _h * 0.5f);
            }
        }

        /// <summary>The claws break out of the ground.</summary>
        private void Erupt()
        {
            WorldTile tile = Combat.TileAt(_at);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.04f);
            if (Kit.Visible(_at, 0.4f))
            {
                Kit.Debris(_at, Px, tile, 2, K * 0.7f);
                Kit.DustRing(_at, Px, tile, 3, 22f, 0.3f, K * 0.8f);
                Kit.Crater(_at, Px, tile, false, 4f, true);
            }
        }

        /// <summary>The claws close: the grip (an area blow: it may slip it).</summary>
        private void Grip()
        {
            if (!OwnerOk || !VictimOk || !Combat.Grounded(_v) || !Choreo.Fair(A, _v, out float cm, true))
            {
                return;
            }
            Vector2 g = Victims.GroundOf(_v);
            if (Vector2.Distance(g, _at) > _half + 0.6f * K)
            {
                return;   // it stepped out of the hand before it closed
            }
            if (!HitIds.Add(_vid) || Combat.Reach(M, _v, true) != Contact.Hit)
            {
                return;
            }
            _gripped = true;
            Combat.Hit(A, _v, TectonicGripTech.GripMult * cm, false, false, 0f, M);
            Vector2 dir = g - (Vector2)A.current_position;
            Impact im = Impact.Of(Hurt.Fold, dir, 0f, 0.85f);
            im.Hold = _hold;
            im.Daze = 0.2f;
            Victims.Hurt(_v, A, M, im);
            Victims.Flash(_v, Color.white, 0.05f);
            Combat.Pin(_v, _hold + 0.4f);
            if (Kit.Visible(_at, 0.4f))
            {
                Kit.Sparks(Choreo.BodyAt(_v, 0.35f), Px, 3, 30f, 70f, Hue.Edge);
            }
        }

        /// <summary>A squeeze: the claws clench; the foe is crushed (the last squeeze crumples it, or at III bursts it out).</summary>
        private void Squeeze(int pulse)
        {
            _squeeze = 1f;
            bool last = pulse >= _cue.Pulses;
            bool vis = Kit.Visible(_at, 0.4f);
            if (vis)
            {
                Vector2 mid = _at + new Vector2(0f, _h * 0.45f);
                Kit.Sparks(mid, Px, last ? 6 : 4, 40f, 100f, Hue.Edge);
                Kit.Debris(_at, Px, Combat.TileAt(_at), last ? 3 : 1, K * 0.6f, _h * 0.4f);
                Fx.Ring(mid, Px, 0.14f, _h / Px * 0.9f, 2f, 2, 1, FxLayer.Over, 5, 0.9f, 1.4f, Fx.WithA(Hue.Edge, 0f), Fx.WithA(Hue.Edge, 0.75f), Fx.WithA(Hue.Core, 0.9f));
            }
            if (_gripped && OwnerOk && VictimOk)
            {
                Combat.Allowed(A, _v, out float cm);
                Combat.Hit(A, _v, TectonicGripTech.PulseMult(_level, pulse) * cm, false, false, 0f, M);
                Vector2 g = Victims.GroundOf(_v);
                Vector2 dir = g - (Vector2)A.current_position;
                if (vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(_v, 0.5f), Vector2.up, Px, K, last ? 1.5f : 1f, Hue, FxLayer.Top, last);
                }
                if (!last)
                {
                    Impact im = Impact.Of(Hurt.Fold, dir, 0f, 1.1f);
                    im.Hold = Mathf.Max(0.2f, _hold - 0.2f * pulse);
                    im.Daze = 0.2f;
                    Victims.Hurt(_v, A, M, im);
                    Combat.Pin(_v, im.Hold + 0.3f);
                }
                else if (_level >= 3)
                {
                    Impact im = Impact.Of(Hurt.Launch, dir.sqrMagnitude > 1e-4f ? dir : Vector2.up, 0.7f, 1.4f);
                    im.Lift = 2f;
                    im.Daze = 1f;
                    im.Owed = MeteorSlam.AtDeathsDoor(_v);
                    Victims.Hurt(_v, A, M, im);
                }
                else
                {
                    Impact im = Impact.Of(Hurt.Crumple, dir, 0f, 1.3f);
                    im.Daze = 1.1f;
                    im.Owed = MeteorSlam.AtDeathsDoor(_v);
                    Victims.Hurt(_v, A, M, im);
                }
                Victims.Flash(_v, Color.white, 0.06f);
            }
            if (last)
            {
                if (OwnerOk && _gripped)
                {
                    Ruin.Blast(A, _at, _level >= 3 ? 0.8f : 0.55f, Scale, A.current_position, false);
                }
                if (_level >= 3)
                {
                    Burst();
                }
                StartCrumble();
            }
        }

        /// <summary>III: the claws fly apart in pieces.</summary>
        private void Burst()
        {
            _burst = true;
            WorldTile tile = Combat.TileAt(_at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", tile, 0.08f);
            for (int i = 0; i < _f.Length; i++)
            {
                Particle p = _f[i];
                if (p == null || !p.Alive)
                {
                    continue;
                }
                float side = (i & 1) == 0 ? -1f : 1f;
                p.Held = false;
                p.Age = 0f;
                p.Life = Random.Range(0.5f, 0.75f);
                p.Moving(new Vector2(side * Random.Range(20f, 40f), Random.Range(-6f, 6f)) * Px * K, Random.Range(40f, 70f) * Px * K, 0f, 320f * Px * K);
                p.Bounce = 0.25f;
                p.Spin = side * Random.Range(200f, 500f);
                Color c = p.R.color;
                p.Color(c, c, Fx.WithA(c, 0f));
                _f[i] = null;
            }
            if (Kit.Visible(_at, 0.4f))
            {
                Kit.Flash(_at + new Vector2(0f, _h * 0.4f), Px, Mathf.RoundToInt(4f * K), 0.07f, Fx.WithA(Hue.Core, 0.95f));
                Kit.Debris(_at, Px, tile, 8, K, _h * 0.3f);
                EarthKit.Boulders(_at, Px, 3, K, _stone);
            }
        }

        private void Draw()
        {
            if (!Kit.Visible(_at, 0.4f) || _burst || _stage == Stage.Crumble && _crumble >= 0.4f)
            {
                KillFingers();
                return;
            }
            float rise, tilt;
            switch (_stage)
            {
                case Stage.Wait:
                    return;
                case Stage.Rise:
                    rise = Beat.Apply(Ease.OutBack, Mathf.Clamp01(_t / 0.1f));
                    tilt = 4f;
                    break;
                case Stage.Close:
                    rise = 1f;
                    tilt = Mathf.Lerp(4f, 26f, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(_t / 0.09f)));
                    break;
                case Stage.Hold:
                    rise = 1f;
                    tilt = 26f + 12f * _squeeze + ((int)(Age * 40f) % 2 == 0 ? 0.8f : -0.8f);
                    break;
                default:
                    rise = 1f - Beat.Apply(Ease.InQuad, Mathf.Clamp01(_crumble / 0.4f));
                    tilt = Mathf.Lerp(_tilt, 10f, Mathf.Clamp01(_crumble / 0.4f));
                    break;
            }
            if (_stage != Stage.Crumble)
            {
                _tilt = tilt;
            }
            Sprite s = EarthKit.Finger(_w, Mathf.Clamp(Mathf.RoundToInt(_h / Px), 4, 120));
            Color col = Color.Lerp(_stone, Hue.Edge, 0.35f * _squeeze);
            for (int i = 0; i < 4; i++)
            {
                bool back = i < 2;
                float side = (i & 1) == 0 ? -1f : 1f;
                Particle p = _f[i];
                if (p == null || !p.Alive)
                {
                    p = Fx.Spawn(s, _at, Px, 1f, back ? FxLayer.Behind : FxLayer.Over, back ? 2 : 3);
                    if (p == null)
                    {
                        continue;
                    }
                    p.Held = true;
                    _f[i] = p;
                }
                float spread = _half + (back ? 0.06f * K : 0f) - 0.06f * K * _squeeze;
                p.Pos = _at + new Vector2(side * spread, back ? 0.07f * K : -0.09f * K);
                p.SX = (side < 0f ? 1f : -1f) * (1f - 0.08f * _squeeze);
                p.SY = Mathf.Max(0.01f, rise) * (back ? 0.92f : 1f);
                p.Rot = -side * (tilt + (back ? -3f : 0f));
                p.Grad = null;
                p.R.color = back ? Fx.Darken(col, 0.12f) : col;
            }
        }

        private void KillFingers()
        {
            for (int i = 0; i < _f.Length; i++)
            {
                if (_f[i] != null && _f[i].Alive)
                {
                    _f[i].Kill();
                }
                _f[i] = null;
            }
        }

        public override void Kill()
        {
            KillFingers();
            if (_warn != null && _warn.Alive)
            {
                _warn.Kill();
            }
            _warn = null;
        }
    }

    /// <summary>
    /// The stone the upper Earth Breaker techniques raise and throw (Tectonic Grip's claws, the Earth
    /// Dragon's head of rock, Atlas Lift's slab of torn-up ground), the colour of rock at a spot, cracks
    /// and fissures running out of an impact, rocks flung about, and a cue that fires a moment later
    /// (a thrown body's crash) even after the move has ended. Shared by the nine techniques of the
    /// Earth Breaker from Master up.
    /// </summary>
    internal static class EarthKit
    {
        public static float L(int level, float a, float b, float c)
        {
            return level <= 1 ? a : level == 2 ? b : c;
        }

        public static int L(int level, int a, int b, int c)
        {
            return level <= 1 ? a : level == 2 ? b : c;
        }

        /// <summary>Raw rock at a spot: its ground's colour, greyed.</summary>
        public static Color Stone(Vector2 at)
        {
            return Color.Lerp(Fx.SoilColor(Combat.TileAt(at)), new Color(0.62f, 0.58f, 0.54f, 1f), 0.5f);
        }

        /// <summary>A crack running along the ground from 'a' to 'b' (dark; with a glowing vein of 'glow').</summary>
        public static void Crack(Vector2 a, Vector2 b, float px, float life, Color? glow)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.3f || !Kit.Visible(a, 0.5f) && !Kit.Visible(b, 0.5f))
            {
                return;
            }
            int lp = Mathf.Clamp(Mathf.RoundToInt(len / px), 4, 120);
            int v = Random.Range(0, 8);
            float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float sx = len / (lp * px);
            Color dark = Fx.Darken(Fx.SoilColor(Combat.TileAt(a)), 0.55f);
            Particle f = Fx.Spawn(ChoreoArt.Fissure(lp, v), a, px, life + 1.2f, FxLayer.Ground, 2);
            if (f != null)
            {
                f.Rot = rot;
                f.SX = sx;
                f.Color(Fx.WithA(dark, 0.95f), Fx.WithA(dark, 0.9f), Fx.WithA(dark, 0f));
            }
            if (glow.HasValue)
            {
                Particle g = Fx.Spawn(ChoreoArt.Fissure(lp, v), a, px, life, FxLayer.Ground, 3);
                if (g != null)
                {
                    g.Rot = rot;
                    g.SX = sx;
                    g.Color(Fx.WithA(Color.white, 0.9f), Fx.WithA(glow.Value, 0.7f), Fx.WithA(glow.Value, 0f));
                }
            }
        }

        /// <summary>
        /// 'n' fissures race out of 'at' to about 'r' tiles (dark cracks; glowing veins when 'glow'), and the
        /// ground breaks along them at the force 'by's tier and Scale 's' call for (+'extra').
        /// </summary>
        public static void Fissures(Actor by, Vector2 at, float px, float r, int n, Color? glow, Scale s, int extra = 0)
        {
            n = Mathf.Clamp(n, 0, 16);
            if (n == 0 || by == null)
            {
                return;
            }
            float a0 = Random.value * Mathf.PI * 2f;
            Vector2 feet = by.current_position;
            Kit.BeginTrench();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    float ang = a0 + i * Mathf.PI * 2f / n + Random.Range(-0.25f, 0.25f);
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground);
                    Vector2 end = Combat.Clamp(at + d * r * Random.Range(0.7f, 1f));
                    Crack(at, end, px, 2.2f, glow);
                    Ruin.Furrow(by, at, Vector2.Lerp(at, end, 0.85f), 0.6f, s, feet, false, extra);
                }
            }
            finally
            {
                Kit.EndTrench(by);
            }
        }

        /// <summary>Rocks (Choreo boulders) flung up out of a point, tumbling and bouncing.</summary>
        public static void Boulders(Vector2 at, float px, int n, float k, Color c, float speed = 1f)
        {
            if (!Kit.Visible(at, 0.4f))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground);
                Particle p = Fx.Spawn(ChoreoArt.Boulder(Mathf.RoundToInt(Random.Range(1.5f, 3f) * Mathf.Sqrt(k)), Random.Range(0, 4)), at + dir * 2f * px, px,
                    Random.Range(0.9f, 1.4f), FxLayer.Top, 5);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * Random.Range(16f, 40f) * px * k * speed, Random.Range(50f, 95f) * px * k * speed, 0f, 330f * px * k);
                p.Bounce = 0.3f;
                p.Spin = Random.Range(-500f, 500f);
                Color d = Fx.Darken(c, Random.Range(0f, 0.2f));
                p.Color(d, d, d, Fx.WithA(d, 0f));
            }
        }

        // ------------------------------------------------------------------ art --

        private static readonly Dictionary<long, Sprite> _sprites = new Dictionary<long, Sprite>();

        /// <summary>A claw of stone 'w' pixels thick at its foot and 'h' tall, curling towards +x, knuckled; pivot at its foot.</summary>
        public static Sprite Finger(int w, int h)
        {
            w = Mathf.Clamp(w, 2, 30);
            h = Mathf.Clamp(h, 4, 120);
            long key = (1L << 40) | ((long)w << 20) | (long)h;
            if (_sprites.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            float bend = w * 0.9f + 1f;
            int cw = Mathf.CeilToInt(w + bend) + 3, ch = h + 1;
            Canvas c = new Canvas(cw, ch);
            float x0 = 1f + w * 0.5f;
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;
                float cx = x0 + bend * t * t;
                float half = Mathf.Max(0.5f, w * 0.5f * (1f - 0.8f * t));
                bool knuckle = Mathf.Abs(t - 0.36f) < 0.03f || Mathf.Abs(t - 0.68f) < 0.03f;
                for (int x = 0; x < cw; x++)
                {
                    float dx = x + 0.5f - cx;
                    if (Mathf.Abs(dx) > half)
                    {
                        continue;
                    }
                    float v = dx < -half * 0.35f ? 1f : dx > half * 0.4f ? 0.6f : 0.82f;
                    if (knuckle)
                    {
                        v -= 0.2f;
                    }
                    else if (Art.Hash(x, y, 13) < 0.1f)
                    {
                        v -= 0.08f;
                    }
                    c.Set(x, y, v, 1f);
                }
            }
            s = c.ToSprite("sakuga_eb_finger_" + w + "_" + h, new Vector2(x0 / cw, 0.5f / ch));
            _sprites[key] = s;
            return s;
        }

        // the dragon's head (Earth Dragon), in a unit box: x to the snout, y up
        private static readonly Vector2[] Skull =
        {
            new Vector2(0.00f, 0.30f), new Vector2(0.00f, 0.60f), new Vector2(0.12f, 0.78f), new Vector2(0.36f, 0.84f),
            new Vector2(0.62f, 0.72f), new Vector2(0.95f, 0.62f), new Vector2(0.99f, 0.52f), new Vector2(0.90f, 0.46f),
            new Vector2(0.36f, 0.50f), new Vector2(0.20f, 0.40f), new Vector2(0.10f, 0.30f)
        };

        private static readonly Vector2[] Jaw =
        {
            new Vector2(0.22f, 0.40f), new Vector2(0.38f, 0.44f), new Vector2(0.84f, 0.24f), new Vector2(0.88f, 0.13f),
            new Vector2(0.60f, 0.08f), new Vector2(0.28f, 0.16f), new Vector2(0.12f, 0.26f)
        };

        private static readonly Vector2[] Horn =
        {
            new Vector2(0.20f, 0.72f), new Vector2(0.32f, 0.80f), new Vector2(0.03f, 1.00f)
        };

        /// <summary>Where the eye sits in the dragon's head (fraction of the sprite, before the pivot).</summary>
        public static readonly Vector2 HeadEye = new Vector2(0.44f, 0.64f);

        /// <summary>The dragon head's pivot (the base of its neck), as a fraction of the sprite.</summary>
        public static readonly Vector2 HeadPivot = new Vector2(0.08f, 0.28f);

        /// <summary>A dragon's head of rock 'len' pixels long, its jaws open, facing +x; pivot at the base of its neck.</summary>
        public static Sprite DragonHead(int len)
        {
            len = Mathf.Clamp(len, 8, 100);
            long key = (2L << 40) | (long)len;
            if (_sprites.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int cw = len + 2, ch = Mathf.RoundToInt(len * 0.78f) + 2;
            Canvas c = new Canvas(cw, ch);
            float sx = len, sy = ch - 2;
            for (int x = 0; x < cw; x++)
            {
                for (int y = 0; y < ch; y++)
                {
                    float u = (x + 0.5f - 1f) / sx, v = (y + 0.5f - 1f) / sy;
                    bool skull = In(Skull, u, v), jaw = In(Jaw, u, v), horn = In(Horn, u, v);
                    if (!skull && !jaw && !horn)
                    {
                        // teeth: down from the upper jaw, up from the lower one
                        bool tooth = false;
                        if (u > 0.44f && u < 0.9f)
                        {
                            float cell = (u - 0.44f) * len / 3f;
                            float f = cell - Mathf.Floor(cell);
                            float upper = Mathf.Lerp(0.5f, 0.46f, (u - 0.44f) / 0.46f);
                            float lower = Mathf.Lerp(0.42f, 0.25f, (u - 0.44f) / 0.46f);
                            tooth = f < 0.5f && (v < upper && v > upper - 0.07f * (1f - f * 2f) || v > lower && v < lower + 0.07f * (1f - f * 2f));
                        }
                        if (tooth)
                        {
                            c.Set(x, y, 1f, 1f);
                        }
                        continue;
                    }
                    // lit from the top left: the upper edge bright, the underside dark, strata and cracks
                    bool edgeTop = !In(Skull, u - 1.2f / sx, v + 1.2f / sy) && !In(Jaw, u - 1.2f / sx, v + 1.2f / sy) && !In(Horn, u - 1.2f / sx, v + 1.2f / sy);
                    bool edgeLow = !In(Skull, u + 1.2f / sx, v - 1.2f / sy) && !In(Jaw, u + 1.2f / sx, v - 1.2f / sy) && !In(Horn, u + 1.2f / sx, v - 1.2f / sy);
                    float val = edgeTop ? 1f : edgeLow ? 0.58f : 0.8f;
                    if (jaw && !edgeTop)
                    {
                        val -= 0.08f;
                    }
                    if ((y + x / 3) % 5 == 0 && Art.Hash(x, y, 21) < 0.6f && !edgeTop)
                    {
                        val -= 0.12f;
                    }
                    float ex = (u - HeadEye.x) * sx, ey = (v - HeadEye.y) * sy;
                    if (ex * ex + ey * ey <= Mathf.Max(1.2f, len * 0.004f * len))
                    {
                        val = 0.3f;   // the eye socket (its glow is drawn over it)
                    }
                    c.Set(x, y, val, 1f);
                }
            }
            s = c.ToSprite("sakuga_eb_dragonhead_" + len, new Vector2((HeadPivot.x * sx + 1f) / cw, (HeadPivot.y * sy + 1f) / ch));
            _sprites[key] = s;
            return s;
        }

        private static bool In(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if (a.y > y != b.y > y && x < (b.x - a.x) * (y - a.y) / (b.y - a.y + 1e-6f) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// A slab of torn-up earth 'w' pixels wide and 'h' deep: grass on top ('top'), soil with strata
        /// ('soil'), a jagged keel of rock beneath; drawn in its own colours. Pivot at the middle of its
        /// top surface (what stands on it stands there).
        /// </summary>
        public static Sprite Slab(int w, int h, Color top, Color soil)
        {
            w = Mathf.Clamp(w, 8, 160);
            h = Mathf.Clamp(h, 4, 90);
            int tq = Quant(top), sq = Quant(soil);
            long key = (3L << 40) | ((long)w << 28) | ((long)h << 20) | ((long)(tq & 0x3FF) << 10) | (long)(sq & 0x3FF);
            if (_sprites.TryGetValue(key, out Sprite s))
            {
                return s;
            }
            int cw = w + 2, ch = h + 3;
            Canvas c = new Canvas(cw, ch);
            Color rock = new Color(0.56f, 0.52f, 0.5f, 1f);
            int topY = ch - 2;
            for (int x = 1; x <= w; x++)
            {
                float u = (x - 0.5f) / w * 2f - 1f;   // -1 .. 1 across
                // the keel: deepest in the middle, ragged
                float depth = h * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(u)), 0.55f) * (0.78f + 0.22f * Art.Hash(x / 2, 7, 3));
                int bottom = topY - Mathf.Max(2, Mathf.RoundToInt(depth));
                int surface = topY + (Art.Hash(x, 5, 9) < 0.25f ? 1 : 0);
                for (int y = bottom; y <= surface; y++)
                {
                    float fromTop = (surface - y) / (float)Mathf.Max(1, surface - bottom);
                    Color col;
                    if (surface - y <= 1)
                    {
                        col = Fx.Lighten(top, surface - y == 0 ? 0.25f : 0f);   // grass, its top catching the light
                    }
                    else if (fromTop < 0.45f)
                    {
                        col = (y + x / 4) % 4 == 0 ? Fx.Darken(soil, 0.18f) : soil;   // soil with strata
                    }
                    else
                    {
                        col = Color.Lerp(soil, rock, Mathf.Clamp01((fromTop - 0.45f) * 2.5f));
                        if (Art.Hash(x, y, 4) < 0.14f)
                        {
                            col = Fx.Darken(col, 0.2f);
                        }
                    }
                    // lit from the left, the right flank and the underside in shade
                    if (u > 0.55f || y <= bottom + 1)
                    {
                        col = Fx.Darken(col, 0.25f);
                    }
                    else if (u < -0.55f)
                    {
                        col = Fx.Lighten(col, 0.1f);
                    }
                    c.Px[y * cw + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), 255);
                }
            }
            s = c.ToSprite("sakuga_eb_slab_" + w + "_" + h, new Vector2(0.5f, (topY + 0.5f) / ch));
            _sprites[key] = s;
            return s;
        }

        private static int Quant(Color c)
        {
            return (Mathf.Clamp(Mathf.RoundToInt(c.r * 7f), 0, 7) << 6) | (Mathf.Clamp(Mathf.RoundToInt(c.g * 7f), 0, 7) << 3) | Mathf.Clamp(Mathf.RoundToInt(c.b * 7f), 0, 7);
        }

        /// <summary>The colour 'c' quantized the way Slab caches it (so the sprite and its pieces match).</summary>
        public static Color Quantized(Color c)
        {
            return new Color(Mathf.Round(c.r * 7f) / 7f, Mathf.Round(c.g * 7f) / 7f, Mathf.Round(c.b * 7f) / 7f, 1f);
        }
    }

    /// <summary>Something a move sets up to happen a moment later (a thrown body's crash), even if the move has ended by then.</summary>
    internal sealed class CueHazard : Hazard
    {
        private readonly float _at;
        private readonly Action<CueHazard> _do;

        public CueHazard(ChoreoMove m, float delay, Action<CueHazard> a) : base(m)
        {
            _at = delay;
            _do = a;
        }

        /// <summary>Is the one who set it up still alive (the same unit)?</summary>
        public bool Owner => OwnerOk;

        public override bool Tick(float dt)
        {
            Age += dt;
            if (Age < _at)
            {
                return true;
            }
            if (OwnerOk)
            {
                _do(this);
            }
            return false;
        }
    }
}
