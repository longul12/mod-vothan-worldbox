using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// A copy of the performer that moves and strikes on its own (afterimage clones, a legion, mirror
    /// images): its sprite as the performer is drawn, as a coloured silhouette ('Ghostly') or the real
    /// thing a little see-through. Give it steps (DashTo, BlinkTo, Strike, Wait, Vanish, Shatter);
    /// it plays them in order. Its blows are the performer's (damage, Reach, reactions) as long as the
    /// performer lives; when the performer's move ends, its clones fade.
    /// </summary>
    internal sealed class Clone
    {
        private enum Op
        {
            Dash,
            Blink,
            Strike,
            Wait,
            Vanish,
            Shatter
        }

        private struct Step
        {
            public Op Op;
            public Vector2 To;
            public Func<Vector2> Where;
            public float Dur;
            public Actor Target;
            public Blow Blow;
            public float Mult;
            public Hurt? Hurt;
            public float Push;
        }

        public readonly ChoreoMove M;
        public readonly Actor A;
        public readonly long AId;
        public bool Ghostly;
        public Hue Hue;
        public Pose Pose;
        public bool Done;

        private readonly Queue<Step> _steps = new Queue<Step>();
        private Step _cur;
        private bool _has;
        private float _t, _dur;
        private Vector2 _from, _to;
        private Particle _p;
        private Sprite _sprite;
        private float _ghostT, _alpha = 1f;
        private bool _hit, _hitMade;
        // the performer's size and flip as last seen alive (a clone fading out after its performer was
        // disposed or its actor reused still draws as it was)
        private float _scale = 1f;
        private bool _canFlip = true;

        public Clone(ChoreoMove m, Vector2 at, bool ghostly)
        {
            M = m;
            A = m.A;
            AId = A.Sid();
            _scale = A.actor_scale;
            _canFlip = A.asset == null || A.asset.can_flip;
            Ghostly = ghostly;
            Hue = m.Hue;
            Pose = m.Pose;
            Pose.Ground = at;
            Pose.Height = 0f;
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Hidden = false;
            Pose.Nudge = Vector2.zero;
            _sprite = Fx.UnitSprite(A);
            if (Kit.Visible(at))
            {
                Fx.Ring(at, m.Px, 0.2f, 2f, 8f * m.Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f, Fx.WithA(Hue.Edge, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        private float Px => M.Px;
        private float K => M.Kk;

        // -------------------------------------------------------------- steps --

        public Clone DashTo(Vector2 to, float dur = 0.12f)
        {
            _steps.Enqueue(new Step { Op = Op.Dash, To = to, Dur = dur });
            return this;
        }

        /// <summary>Dashes to where 'where' says when the step starts (in front of a moving target).</summary>
        public Clone DashTo(Func<Vector2> where, float dur = 0.12f)
        {
            _steps.Enqueue(new Step { Op = Op.Dash, Where = where, Dur = dur });
            return this;
        }

        public Clone BlinkTo(Vector2 to, float dur = 0.06f)
        {
            _steps.Enqueue(new Step { Op = Op.Blink, To = to, Dur = dur });
            return this;
        }

        /// <summary>Steps in on 'v' and throws 'b' at it (x 'mult'; its reaction 'hurt' if not the blow's own).</summary>
        public Clone Strike(Actor v, Blow b, float mult, Hurt? hurt = null, float push = -1f)
        {
            _steps.Enqueue(new Step { Op = Op.Strike, Target = v, Blow = b, Mult = mult, Hurt = hurt, Push = push, Dur = 0.16f });
            return this;
        }

        public Clone Wait(float dur)
        {
            _steps.Enqueue(new Step { Op = Op.Wait, Dur = dur });
            return this;
        }

        /// <summary>Fades away (a little puff of its colour).</summary>
        public Clone Vanish(float dur = 0.15f)
        {
            _steps.Enqueue(new Step { Op = Op.Vanish, Dur = dur });
            return this;
        }

        /// <summary>Shatters like glass: shards burst out, cutting the performer's enemies within 'r' tiles (x 'mult').</summary>
        public Clone Shatter(float r = 0f, float mult = 0f)
        {
            _steps.Enqueue(new Step { Op = Op.Shatter, Push = r, Mult = mult, Dur = 0.05f });
            return this;
        }

        // --------------------------------------------------------------- tick --

        public bool Tick(float dt)
        {
            if (Done)
            {
                return false;
            }
            bool ownerOk = A != null && A.Sid() == AId && A.isAlive() && !M.Ended;
            if (ownerOk && A.asset != null)
            {
                _scale = A.actor_scale;
                _canFlip = A.asset.can_flip;
            }
            if (!ownerOk && (!_has || _cur.Op != Op.Vanish && _cur.Op != Op.Shatter))
            {
                _steps.Clear();
                _has = true;
                _cur = new Step { Op = Op.Vanish, Dur = 0.12f };
                Begin();
            }
            if (!_has)
            {
                if (_steps.Count == 0)
                {
                    _steps.Enqueue(new Step { Op = Op.Vanish, Dur = 0.15f });
                }
                _cur = _steps.Dequeue();
                _has = true;
                Begin();
            }
            if (dt > 0f)
            {
                _t += dt;
                Play(dt);
            }
            Render();
            if (Done)
            {
                Kill();
                return false;
            }
            if (_t >= _dur && _has)
            {
                End();
                _has = false;
            }
            return true;
        }

        private void Begin()
        {
            _t = 0f;
            _dur = Mathf.Max(0.01f, _cur.Dur * M.ActK);
            _from = Pose.Ground;
            _hit = _hitMade = false;
            switch (_cur.Op)
            {
                case Op.Dash:
                    _to = _cur.Where != null ? _cur.Where() : _cur.To;
                    if (!Combat.SafeSpot(_to))
                    {
                        _to = _from;
                    }
                    Face(_to - _from);
                    break;
                case Op.Blink:
                    _to = Combat.SafeSpot(_cur.To) ? _cur.To : _from;
                    if (Kit.Visible(_from))
                    {
                        Fx.Ring(_from, Px, 0.16f, 6f * K, 1f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(Hue.Edge, 0.9f), Fx.WithA(Hue.Edge, 0f));
                    }
                    break;
                case Op.Strike:
                    {
                        Actor v = _cur.Target;
                        if (v == null || !Combat.Living(v))
                        {
                            _dur = 0.01f;
                            break;
                        }
                        Vector2 g = Victims.GroundOf(v);
                        Vector2 d = g - _from;
                        Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
                        float stand = 0.6f * K;
                        _to = Combat.Clamp(g - dir * stand);
                        if (!Combat.SafeSpot(_to))
                        {
                            _to = _from;
                        }
                        float travel = Vector2.Distance(_from, _to);
                        _dur += Mathf.Min(0.2f, travel * 0.03f) * M.ActK;
                        Face(dir);
                        break;
                    }
            }
        }

        private void Play(float dt)
        {
            float k = Mathf.Clamp01(_t / _dur);
            switch (_cur.Op)
            {
                case Op.Dash:
                    {
                        Pose.Ground = Vector2.Lerp(_from, _to, Beat.Apply(Ease.OutQuad, k));
                        float lean = Mathf.Sin(Mathf.PI * k);
                        Pose.Rot = 16f * lean;
                        Pose.SX = 1f + 0.18f * (1f - k);
                        Pose.SY = 1f - 0.12f * (1f - k);
                        Ghost(dt, 0.03f);
                        break;
                    }
                case Op.Blink:
                    Pose.Ground = k < 1f ? _from : _to;
                    Pose.Hidden = k < 1f;
                    break;
                case Op.Strike:
                    {
                        // a short dash in (the first 40%), the blow at 70%
                        BlowShape s = ChoreoMove.ShapeOf(_cur.Blow);
                        float move = Mathf.Clamp01(k / 0.4f);
                        Pose.Ground = Vector2.Lerp(_from, _to, Beat.Apply(Ease.OutQuad, move));
                        if (k < 0.4f)
                        {
                            Ghost(dt, 0.035f);
                            Pose.Rot = 14f * Mathf.Sin(Mathf.PI * move);
                        }
                        else if (k < 0.6f)
                        {
                            float w = Beat.Apply(Ease.OutQuad, (k - 0.4f) / 0.2f);
                            Pose.Rot = Mathf.Lerp(0f, s.Wind.Rot, w);
                            Pose.SX = Mathf.Lerp(1f, s.Wind.SX, w);
                            Pose.SY = Mathf.Lerp(1f, s.Wind.SY, w);
                        }
                        else
                        {
                            float h = Beat.Apply(Ease.OutExpo, (k - 0.6f) / 0.15f);
                            Pose.Rot = Mathf.Lerp(s.Wind.Rot, s.Hit.Rot, h);
                            Pose.SX = Mathf.Lerp(s.Wind.SX, s.Hit.SX, h);
                            Pose.SY = Mathf.Lerp(s.Wind.SY, s.Hit.SY, h);
                            if (!_hit && k >= 0.7f)
                            {
                                _hit = true;
                                Land(s);
                            }
                        }
                        break;
                    }
                case Op.Vanish:
                    _alpha = 1f - k;
                    Pose.SY = 1f + 0.3f * k;
                    Pose.SX = 1f - 0.4f * k;
                    if (k >= 1f)
                    {
                        Done = true;
                    }
                    break;
                case Op.Shatter:
                    if (!_hitMade)
                    {
                        _hitMade = true;
                        Burst();
                    }
                    Done = true;
                    break;
            }
        }

        private void End()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Hidden = false;
        }

        private void Face(Vector2 d)
        {
            if (Mathf.Abs(d.x) > 0.05f)
            {
                Pose.FaceRight = d.x > 0f;
            }
        }

        /// <summary>The clone's blow lands: the performer's damage, through the target's answer, with its reaction.</summary>
        private void Land(BlowShape s)
        {
            Actor v = _cur.Target;
            if (v == null || !Combat.Living(v) || A == null || A.Sid() != AId || !A.isAlive())
            {
                return;
            }
            if (Vector2.Distance(Pose.Ground, Victims.GroundOf(v)) > 1.6f * K + v.stats["size"] * 0.5f || !Choreo.FairBlow(A, v, M, out _, out _, true))
            {
                return;
            }
            Vector2 d = Victims.GroundOf(v) - Pose.Ground;
            Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
            if (Combat.Reach(M, v, false) != Contact.Hit)
            {
                if (Kit.Visible(Pose.Ground))
                {
                    Kit.Sparks(Choreo.BodyAt(v, 0.6f), Px, 3, 40f, 80f, Hue.Edge);
                }
                return;
            }
            // (a clone's blow kills a bystander outright, but never takes the main target before the script's own finisher)
            Hit h = new Hit(_cur.Blow, _cur.Mult) { Hurt = _cur.Hurt, Push = _cur.Push, Lethal = v != M.V ? true : (bool?)null };
            M.Deliver(v, h, s, Choreo.BodyAt(v, s.Contact), dir);
            if (Kit.Visible(Pose.Ground) && s.ArcR > 0)
            {
                Vector2 body = Pose.Ground + new Vector2(0f, Pose.PivotUp * _scale);
                Particle p = Fx.Spawn(Art.Arc(Mathf.Max(5, Mathf.RoundToInt(s.ArcR * K)), s.ArcRatio, s.A0, s.A1, s.ArcThick), body + new Vector2((Pose.FaceRight ? 1f : -1f) * s.ArcOff.x, s.ArcOff.y > -50f ? s.ArcOff.y : -2f) * Px,
                    Px, 0.12f, FxLayer.Over, 6);
                if (p != null)
                {
                    p.SX = Pose.FaceRight ? 1f : -1f;
                    p.Color(Hue.Core, Hue.Edge, Fx.WithA(Hue.Edge, 0.6f));
                }
            }
        }

        /// <summary>It shatters: glass-like shards fly, a ring, glints; the shards cut enemies nearby.</summary>
        private void Burst()
        {
            Vector2 at = Pose.Ground + new Vector2(0f, Pose.PivotUp * _scale);
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(Pose.Ground));
            if (Kit.Visible(at))
            {
                int n = Fx.Count(12);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Particle p = Fx.Spawn(ChoreoArt.Petal(i & 1), Pose.Ground, Px, Random.Range(0.3f, 0.5f), FxLayer.Top, 12);
                    if (p == null)
                    {
                        break;
                    }
                    p.Moving(d * Random.Range(40f, 90f) * Px, Random.Range(10f, 40f) * Px, 2f, 200f * Px);
                    p.Z = at.y - Pose.Ground.y;   // (they fall to the ground, not to the chest)
                    p.Spin = Random.Range(-600f, 600f);
                    p.Bounce = 0.2f;
                    p.Color(Color.white, Fx.WithA(Hue.Main, 0.9f), Fx.WithA(Hue.Edge, 0f));
                }
                Fx.Ring(at, Px, 0.2f, 2f, 10f * K, 1, 1, FxLayer.Top, 5, 1f, 2.4f, Fx.WithA(Color.white, 0.9f), Fx.WithA(Hue.Edge, 0f));
                Fx.Glint(at + new Vector2(0f, 2f * Px), Px, 0.18f, Color.white, 1f);
            }
            if (_cur.Push > 0f && _cur.Mult > 0f && A != null && A.Sid() == AId && A.isAlive())
            {
                M.AreaBlow(Pose.Ground, _cur.Push, _cur.Mult, null, Sakuga.Hurt.Flinch, 0.2f, 0.7f);
            }
        }

        private void Ghost(float dt, float every)
        {
            _ghostT -= dt;
            if (_ghostT > 0f || !Kit.Visible(Pose.Ground))
            {
                return;
            }
            _ghostT = every;
            if (_sprite == null)
            {
                return;
            }
            Particle p = Fx.Spawn(_sprite, DrawPos(), 1f, 0.12f, FxLayer.Behind, 0, true);
            if (p != null)
            {
                p.UseEuler = true;
                p.Euler = Euler();
                p.SX = _scale * Pose.SX;
                p.SY = _scale * Pose.SY;
                p.Color(Fx.WithA(Hue.Ghost, 0.5f * _alpha), Hue.GhostEnd);
            }
        }

        // ------------------------------------------------------------- render --

        private Vector2 DrawPos()
        {
            Vector2 off = Moves.PivotOffset(Pose, _scale, _canFlip);
            return new Vector2(Pose.Ground.x + off.x, Pose.Ground.y + off.y + Pose.Height);
        }

        private Vector3 Euler()
        {
            return new Vector3(0f, _canFlip && Pose.FaceRight ? 180f : 0f, Pose.Rot);
        }

        private void Render()
        {
            if (Pose.Hidden || !Kit.Visible(Pose.Ground, 0.2f) || _sprite == null)
            {
                if (_p != null && _p.Alive)
                {
                    // (Fx draws every held particle again after this: a body with no alpha draws nothing)
                    _p.Grad = null;
                    _p.R.color = Color.clear;
                }
                return;
            }
            if (_p == null || !_p.Alive)
            {
                _p = Fx.Spawn(_sprite, DrawPos(), 1f, 1f, FxLayer.Behind, 4, Ghostly);
                if (_p == null)
                {
                    return;
                }
                _p.Held = true;
                _p.UseEuler = true;
            }
            _p.Pos = DrawPos();
            _p.Euler = Euler();
            _p.SX = _scale * Pose.SX;
            _p.SY = _scale * Pose.SY;
            _p.Grad = null;
            _p.R.color = Ghostly ? Fx.WithA(Color.Lerp(Hue.Main, Hue.Edge, 0.3f), 0.8f * _alpha) : Fx.WithA(Color.white, 0.82f * _alpha);
        }

        public void Kill()
        {
            if (_p != null && _p.Alive)
            {
                _p.Kill();
            }
            _p = null;
            Done = true;
        }
    }

    /// <summary>The clones in the world.</summary>
    internal static class Clones
    {
        private static readonly List<Clone> _list = new List<Clone>();

        public static int Count => _list.Count;

        /// <summary>A clone of 'm's performer at 'at' (a silhouette in its colours when 'ghostly').</summary>
        public static Clone Spawn(ChoreoMove m, Vector2 at, bool ghostly = true)
        {
            if (m == null || _list.Count > 120)
            {
                return null;
            }
            Clone c = new Clone(m, at, ghostly);
            _list.Add(c);
            return c;
        }

        public static void Tick(float dt)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Clone c = _list[i];
                bool alive;
                try
                {
                    alive = c.Tick(dt);
                }
                catch (Exception e)
                {
                    SakugaMod.Report("choreo clone", e);
                    c.Kill();
                    alive = false;
                }
                if (!alive)
                {
                    _list.Remove(c);
                }
            }
        }

        public static void Clear()
        {
            foreach (Clone c in _list)
            {
                c.Kill();
            }
            _list.Clear();
        }
    }

    internal partial class ChoreoMove
    {
        /// <summary>
        /// Splits off clones: 'n' copies appear round it ('r' tiles), each scripted by 'script' (the move,
        /// the clone, its index). The performer's move goes on (hold it with Hold/WaitClones meanwhile).
        /// </summary>
        public ChoreoMove Clones(int n, float r, Action<ChoreoMove, Clone, int> script, bool ghostly = true)
        {
            return Do(m =>
            {
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 2f;
                    Vector2 at = Combat.Clamp(m.Stance + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * r * m.Kk);
                    if (!Combat.SafeSpot(at))
                    {
                        at = m.Stance;
                    }
                    Clone c = Sakuga.Clones.Spawn(m, at, ghostly);
                    if (c != null)
                    {
                        script(m, c, i);
                        m._clones.Add(c);
                    }
                }
                if (m.Vis)
                {
                    Fx.Afterimage(m, 0.2f, Fx.WithA(m.Hue.Core, 0.9f), m.Hue.GhostEnd);
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                }
            });
        }

        /// <summary>
        /// A legion: one clone for each of these enemies (up to 'max'), each dashing to its own and
        /// throwing 'blow' (x 'mult'), then vanishing.
        /// </summary>
        public ChoreoMove Legion(Func<ChoreoMove, List<Actor>> targets, Blow blow, float mult, int max = 8, bool ghostly = true)
        {
            return Do(m =>
            {
                List<Actor> list = targets(m);
                int n = Mathf.Min(max, list.Count);
                for (int i = 0; i < n; i++)
                {
                    Actor v = list[i];
                    float a = i / (float)Mathf.Max(1, n) * Mathf.PI * 2f;
                    Vector2 at = Combat.Clamp(m.Stance + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * 0.6f * m.Kk);
                    Clone c = Sakuga.Clones.Spawn(m, Combat.SafeSpot(at) ? at : m.Stance, ghostly);
                    if (c == null)
                    {
                        break;
                    }
                    c.Wait(i * 0.04f).Strike(v, blow, mult).Vanish();
                    m._clones.Add(c);
                }
            });
        }

        private readonly List<Clone> _clones = new List<Clone>();

        /// <summary>Holds until every clone it made is gone (at most 'max' seconds).</summary>
        public ChoreoMove WaitClones(float max = 3f)
        {
            return WaitUntil(m =>
            {
                for (int i = 0; i < m._clones.Count; i++)
                {
                    if (!m._clones[i].Done)
                    {
                        return false;
                    }
                }
                return true;
            }, max);
        }
    }
}
