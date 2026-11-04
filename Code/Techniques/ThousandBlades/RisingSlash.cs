using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// RISING SLASH (Thousand Blades, Novice). The school's launcher. It steps in and sinks over its
    /// front knee, the blade trailing low behind it, then cuts from the ground to the sky: a crescent
    /// of steel sweeps up through the foe and lifts it off its feet (Launched: an air follow-up can
    /// take it). The cut shows on the flying body a heartbeat later.
    ///   I    one rising cut, the foe popped 1.4 tiles up
    ///   II   it springs off the ground into the cut and hangs there with it; the foe flies 2 tiles up,
    ///        and the blade leaves a bright scar on the ground where it left it
    ///   III  a higher spring, a bigger crescent, an impact frame; a crescent moon of light rises off
    ///        the blade after the foe, which flies 2.6 tiles up
    /// Chains from Close (a combo opener into the air) and Down (it scoops a fallen foe up).
    /// Damage (x the user's damage; the tier's Might on top): I 1.3, II 1.6, III 2.1.
    /// </summary>
    internal sealed class RisingSlashTech : Technique
    {
        public override string Id => "rising_slash";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Launcher;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Rising;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 8f, 14f, 22f };
        public override float[] Cooldown => new[] { 5f, 6f, 7f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.3f : level == 2 ? 1.6f : 2.1f;
        }

        private static float Lift(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 2f : 2.6f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.9f, Total = Mult(1), Worthy = false, Crowd = 0.05f, Alone = 5f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2.1f, Total = Mult(2), Worthy = false, Crowd = 0.05f, Alone = 6f, IntoGuard = 0.8f, Worth = 8f },
            new MeleeWant { Reach = 2.3f, Total = Mult(3), Worthy = false, Crowd = 0.04f, Alone = 7f, IntoGuard = 0.8f, Worth = 10f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // not spent on a foe half the cut would finish
            Combat.Allowed(c.Self, p.Target, out float cm);
            if (p.Target.getHealth() <= c.Expected(p.Target, Mult(level) * 0.5f) * cm)
            {
                return null;
            }
            // a body that can't be lifted (a giant, a flyer) only takes the cut: less worth
            if (!Victims.Movable(p.Target, c.Self))
            {
                p.Score *= 0.7f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            TblMove m = TblMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            float hop = lv <= 1 ? 0f : lv == 2 ? 0.14f : 0.24f;
            m.StepIn();
            // SINK: down over the front knee, the blade trailing low behind it
            m.Posture(lv <= 1 ? 0.08f : 0.1f, new Shape(-14f, 1.16f, 0.82f, 0f, -0.05f), Tempo.Wind, -128f, Form.Blade | Form.Rising, true);
            m.OnEnter(mm =>
            {
                if (mm.Vis)
                {
                    ChoreoFx.HeelDust(mm.Pose.Ground, -mm.Dir, mm.Px, mm.A.current_tile, mm.Kk);
                }
            });
            if (hop > 0f)
            {
                // SPRING: it leaves the ground into the cut
                m.Posture(0.05f, new Shape(-6f, 0.9f, 1.14f, hop), Tempo.Act, -104f, Form.Blade | Form.Rising);
                m.OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Kit.DustRing(mm.Pose.Ground, mm.Px, mm.A.current_tile, 6, 30f, 0.4f, mm.Kk);
                    }
                });
            }
            // THE CUT: ground to sky
            m.Cut(new Hit(Blow.RisingSlash, Mult(lv))
            {
                Hurt = Hurt.Launch,
                Lift = Lift(lv),
                Push = lv <= 1 ? 0.6f : lv == 2 ? 0.75f : 0.9f,
                Power = lv <= 1 ? 1f : lv == 2 ? 1.15f : 1.3f,
                Finisher = lv >= 3,
                Air = hop > 0f,
                WindScale = 0.35f,
                Shows = Form.Blade | Form.Rising,
                OnLand = (mm, v, r) => Risen(mm, v, lv)
            });
            m.Recover(0.22f);
            return m.Start();
        }

        /// <summary>The cut landed: it shows on the flying body; II scars the ground, III looses a moon of light upward.</summary>
        private static void Risen(ChoreoMove mm, Actor v, int lv)
        {
            if (!mm.Vis)
            {
                return;
            }
            float px = mm.Px;
            TblFx.CutOn(v, 0.5f, TblFx.Mirror(56f, mm.Fwd), 5f + 1.5f * lv, px, 0.06f, 0.16f, mm.Hue, mm.TopLayer);
            if (lv >= 2)
            {
                Vector2 g = mm.VG;
                TblFx.GroundCut(g - mm.Dir * 0.5f * mm.Kk, g + mm.Dir * 0.35f * mm.Kk, px, 0.9f, mm.Hue);
            }
            if (lv >= 3)
            {
                TblFx.Moon(mm.VPoint(0.45f), px, mm.Kk, mm.Fwd, -70f, 110f, mm.Hue, mm.TopLayer, new Vector2(0f, 34f));
            }
        }
    }

    /// <summary>
    /// THOUSAND BLADES: the move the school's lower techniques are played on (a ChoreoMove that knows
    /// its unit holds a sword). A unit carrying a weapon cuts with it (the engine draws its steel). An
    /// unarmed walker of the school forms a BLADE OF KI: as the technique begins a flash at its hand and
    /// a blade of light grows out of it (a white edge, a paler back, a pointed tip, a glow of the
    /// school and tier around it); it follows every cut of the script (drawn back on the wind-up, swept
    /// through the blow's arc, held out on the follow-through, lowered to a guard between blows), sheds
    /// motes when it sweeps fast, and breaks into motes as the move ends. An iai keeps it sheathed
    /// (Drawn false) until the draw.
    /// Builder extras: Cut (a Strike the blade follows), Posture (a pose with the blade held at an
    /// angle), Sweep (a cut through everyone around a point).
    /// </summary>
    internal sealed class TblMove : ChoreoMove
    {
        /// <summary>The blade's resting angle between cuts (degrees, facing right, counter-clockwise): low and forward.</summary>
        public const float Guard = -35f;

        /// <summary>The unit carries no weapon: a blade of ki forms in its hand.</summary>
        public bool KiBlade;
        /// <summary>The ki blade is out (false: sheathed, before an iai's draw or after it).</summary>
        public bool Drawn = true;
        /// <summary>The blade held at this angle (degrees, facing right, counter-clockwise) instead of following the cuts.</summary>
        public float? BladeAt;
        /// <summary>Set the blade's angle at once (a sweep of its own), not eased.</summary>
        public bool BladeSnap;
        /// <summary>How far the blade reaches (1 as drawn; a thrust's extension a little more).</summary>
        public float Reach = 1f;

        private readonly List<Hit> _cuts = new List<Hit>(4);
        private int _act = -1;
        private BlowShape _last;
        private float _ang = Guard, _ry = 0.85f, _alpha, _grow, _reach = 1f, _rot, _len, _moteT;
        private Vector2 _u = Vector2.right, _hand, _tip;
        private int _frame = -1;
        private Particle _core, _halo;
        private bool _shown, _shed, _made;

        private TblMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            KiBlade = !Armed;
        }

        /// <summary>A new choreography for technique 't' on plan 'p', or null if the unit can't act now.</summary>
        public static new TblMove Make(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new TblMove(a, t, p);
        }

        /// <summary>A reactive answer: 'defender' answers 'attacker' (its target) with 't' at 'level' (reaches like a forced plan).</summary>
        public static TblMove Counter(Actor defender, Actor attacker, Technique t, int level)
        {
            if (defender == null || attacker == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true
            };
            return new TblMove(defender, t, p);
        }

        /// <summary>
        /// An answer to something flying at 'defender' from 'from' (a missile): it faces there, with no target
        /// of its own (the thrower far away is left alone).
        /// </summary>
        public static TblMove Facing(Actor defender, Vector2 from, Technique t, int level)
        {
            if (defender == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = from - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Point = from,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true
            };
            return new TblMove(defender, t, p);
        }

        /// <summary>In its recovery (the blade lowered, the body settling): a punish window, and an answer to a counter may cut it short.</summary>
        public override bool Recovering => !Finished && Current != null && Current.Recovery;

        // ------------------------------------------------------------ builder --

        /// <summary>A Strike whose cut the blade follows (its wind-up, arc and follow-through).</summary>
        public TblMove Cut(Hit h)
        {
            _cuts.Add(h);
            Strike(h);
            return this;
        }

        /// <summary>
        /// A pose of its own over 'dur' (tempo 't'), the blade held at 'blade' degrees (NaN: as the script
        /// would hold it), showing 'forms'; 'needsVictim': the target must still be there.
        /// </summary>
        public TblMove Posture(float dur, Shape to, Tempo t, float blade, Form forms, bool needsVictim = false, Ease e = Ease.OutQuad)
        {
            Beat b = new Beat { Tempo = t, Dur = dur, To = to, Ease = e, Forms = forms, NeedsVictim = needsVictim };
            if (!float.IsNaN(blade))
            {
                b.Enter = mm => ((TblMove)mm).HoldBlade(blade);
                b.Exit = mm => ((TblMove)mm).FreeBlade();
            }
            Add(b);
            return this;
        }

        /// <summary>Holds the blade at 'ang' (eased there).</summary>
        public void HoldBlade(float ang)
        {
            BladeAt = ang;
            BladeSnap = false;
        }

        /// <summary>The blade follows the script again.</summary>
        public void FreeBlade()
        {
            BladeAt = null;
            BladeSnap = false;
        }

        /// <summary>Sets the blade's angle now (a sweep of its own, every frame of a beat).</summary>
        public void SwingBlade(float ang)
        {
            BladeAt = ang;
            BladeSnap = true;
        }

        private static readonly List<Actor> _sweep = new List<Actor>();

        /// <summary>
        /// A cut through everyone of its enemies within 'r' tiles of 'at' (area blows: each may evade it,
        /// none can parry it): x 'mult' (less at the rim), 'hurt' pushed 'push' tiles out from 'at', a hit
        /// spark and a cut line across each body ('cutAng', shown 'cutDelay' s later). Returns how many.
        /// </summary>
        public int Sweep(Vector2 at, float r, float mult, Hurt hurt, float push, float power, float cutAng, float cutDelay)
        {
            Choreo.Foes(A, at, r, _sweep);
            int n = 0;
            Actor first = null;
            float firstD = float.MaxValue;
            for (int i = 0; i < _sweep.Count; i++)
            {
                Actor u = _sweep[i];
                if (u == null || !Choreo.Fair(A, u, out float cm, true) || !Touch(u, true))
                {
                    continue;
                }
                float d = Vector2.Distance(u.current_position, at);
                float near = 1f - Mathf.Clamp01(d / Mathf.Max(0.1f, r));
                HitResult res = Combat.Hit(A, u, mult * cm * Mathf.Lerp(0.75f, 1f, near), true, false, 0f, this);
                LastHit = res;
                Vector2 away = u.current_position - at;
                if (away.sqrMagnitude < 1e-4f)
                {
                    away = Dir;
                }
                away.Normalize();
                if (Combat.Living(u))
                {
                    Impact im = Impact.Of(hurt, away, push * Mathf.Lerp(0.7f, 1f, near), power);
                    im.Daze = hurt == Hurt.Knockdown ? 0.5f : 0f;
                    im.Stop = 0.02f + 0.02f * power;
                    Victims.Hurt(u, A, this, im);
                }
                Victims.Flash(u, Color.white, 0.05f);
                if (Vis)
                {
                    ChoreoFx.Impact(Choreo.BodyAt(u, 0.5f), away, Px, Kk, 0.75f * power, Hue, TopLayer);
                    TblFx.CutOn(u, 0.5f, cutAng + Random.Range(-12f, 12f), 4.5f + power, Px, cutDelay, 0.15f, Hue, TopLayer);
                }
                if (d < firstD)
                {
                    firstD = d;
                    first = u;
                }
                n++;
            }
            if (n > 0)
            {
                ChoreoFx.BlowSound(Limb.Blade, 0.9f + 0.2f * power, Armed, A.current_tile);
                SetLink(hurt == Hurt.Knockdown ? Link.Down : Link.Close, V != null && Combat.Living(V) ? V : first);
                if (power >= 1f)
                {
                    Ruin.Quake(A, at, Scale, 0.6f * power, Kk);
                }
            }
            return n;
        }

        // -------------------------------------------------------------- blade --

        /// <summary>Where the blade's hand and point are this frame (world), for effects of the techniques.</summary>
        public Vector2 BladeHand => _made ? _hand : Hand(Limb.Blade);

        public Vector2 BladeTip => _made ? _tip : Hand(Limb.Blade) + Dir * 3f * Px;

        private float BladePx => Mathf.Clamp(Hpx * 0.68f, 5f, 34f) * (Level >= 3 ? 1.1f : 1f);

        private float ArmPx => Mathf.Clamp(Hpx * 0.22f, 1.5f, 12f);

        public override void Frame()
        {
            base.Frame();
            Settle();   // (once a frame: the blade is followed even off screen and for a unit's own steel)
            if (KiBlade && _core == null && Vis && !Finished)
            {
                MakeBlade();
            }
        }

        private void MakeBlade()
        {
            int len = Mathf.RoundToInt(BladePx);
            Vector2 at = BodyPos();
            _halo = Glue(Fx.Spawn(TblFx.KiHalo(len), at, Px, 1f, FxLayer.Over, 8));
            _core = Glue(Fx.Spawn(TblFx.KiBlade(len), at, Px, 1f, FxLayer.Over, 9));
            if (_halo != null)
            {
                _halo.Step = (q, d) => Place(q, true);
                _halo.R.color = new Color(1f, 1f, 1f, 0f);
            }
            if (_core != null)
            {
                _core.Step = (q, d) => Place(q, false);
                _core.R.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        /// <summary>The blade (or its glow) placed in the hand this frame.</summary>
        private void Place(Particle q, bool halo)
        {
            if (Finished || !Valid)
            {
                q.R.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            Settle();
            q.Pos = _hand;
            q.Z = 0f;
            q.Rot = _rot;
            q.SX = _len;
            q.SY = halo ? 1f + 0.4f * (1f - _grow) : 1f;
            q.Grad = null;
            float a = Pose.Hidden ? 0f : _alpha;
            if (halo)
            {
                float pulse = 0.4f + 0.12f * Mathf.Sin(Time.unscaledTime * 38f + (Id % 97));
                q.R.color = Fx.WithA(Hue.Edge, a * pulse * (Level >= 3 ? 1.25f : 1f));
            }
            else
            {
                q.R.color = Fx.WithA(Color.Lerp(Hue.Core, Hue.Main, 0.25f), a);
            }
        }

        /// <summary>Where the blade points this frame: once per frame, from the beat it is in.</summary>
        private void Settle()
        {
            int f = Time.frameCount;
            if (f == _frame)
            {
                return;
            }
            bool first = _frame < 0;
            float dt = first ? 0f : Clock.Dt;
            _frame = f;
            Beat b = Current;
            bool recovering = b != null && b.Recovery;
            float goal = Guard, ryGoal = 0.85f, reach = 1f;
            bool snap = false;
            if (BladeAt.HasValue && !recovering)
            {
                goal = BladeAt.Value;
                snap = BladeSnap;
            }
            else if (b != null && b.Tag is Hit h)
            {
                int i = _cuts.IndexOf(h);
                if (i >= 0)
                {
                    _act = i;
                }
                BlowShape s = ShapeOf(h.Blow);
                _last = s;
                if (s.Straight)
                {
                    goal = -4f;
                    reach = 1.18f;
                    ryGoal = 1f;
                }
                else
                {
                    float k = Mathf.Clamp01(BeatK / Mathf.Max(0.25f, s.At));
                    goal = Mathf.Lerp(s.A0, s.A1, Beat.Apply(Ease.OutCubic, k));
                    snap = true;
                    ryGoal = Mathf.Lerp(1f, Mathf.Max(0.3f, s.ArcRatio), 0.5f);
                }
            }
            else if (b != null && b.Tempo == Tempo.Wind && b.Chase && b.NeedsVictim)
            {
                // a cut's wind-up: the blade is drawn back to where the next arc starts, and a little past it
                BlowShape s = _act + 1 < _cuts.Count ? ShapeOf(_cuts[_act + 1].Blow) : null;
                if (s != null)
                {
                    goal = s.Straight ? 4f : s.A0 - Mathf.Sign(s.A1 - s.A0) * 24f;
                    ryGoal = s.Straight ? 1f : Mathf.Lerp(1f, Mathf.Max(0.3f, s.ArcRatio), 0.5f);
                }
            }
            else if (b != null && b.Tempo == Tempo.Follow && _last != null && !recovering)
            {
                goal = _last.Straight ? -8f : _last.A1 + Mathf.Sign(_last.A1 - _last.A0) * 8f;
                reach = _last.Straight ? 1.1f : 1f;
                ryGoal = _last.Straight ? 1f : Mathf.Lerp(1f, Mathf.Max(0.3f, _last.ArcRatio), 0.5f);
            }
            else if (recovering)
            {
                goal = Guard - 18f;
            }
            float was = _ang;
            float ease = 1f - Mathf.Exp(-dt * 26f);
            _ang = snap || first ? goal : Mathf.LerpAngle(_ang, goal, ease);
            _ry = first ? ryGoal : Mathf.Lerp(_ry, ryGoal, ease);
            _reach = Mathf.Lerp(_reach, reach * Reach, 1f - Mathf.Exp(-dt * 30f));
            // in, out: drawn with a flash at the hand, sheathed or dissolved in a scatter of motes
            float want = KiBlade && Drawn && !recovering && !Finished ? 1f : 0f;
            if (want > _alpha)
            {
                if (!_shown)
                {
                    _shown = true;
                    _grow = 0f;
                    Drew();
                }
                _alpha = Mathf.Min(1f, _alpha + dt / 0.045f);
                _shed = false;
            }
            else if (want < _alpha)
            {
                if (!_shed && _alpha > 0.5f)
                {
                    _shed = true;
                    if (Vis && _made)
                    {
                        TblFx.Shed(_hand, _tip, Px, Hue, 8);
                    }
                }
                _alpha = Mathf.Max(0f, _alpha - dt / 0.11f);
                if (_alpha <= 0f)
                {
                    _shown = false;
                }
            }
            _grow = Mathf.Min(1f, _grow + dt / 0.08f);
            // the hand and the point in the world: the arm turns with the body's lean
            float w = Fwd > 0f ? _ang : 180f - _ang;
            float lean = Pose.FaceRight && A.asset.can_flip ? -Pose.Rot : Pose.Rot;
            w += lean * 0.6f;
            float rad = w * Mathf.Deg2Rad;
            _u = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad) * _ry);
            float m = Mathf.Max(0.2f, _u.magnitude);
            Vector2 n = _u / m;
            Vector2 c = BodyPos() + new Vector2(Fwd * 0.5f, 0.6f) * Px;
            _hand = c + n * ArmPx * m * Px;
            _rot = Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg;
            _len = m * Mathf.Max(0.05f, Beat.Apply(Ease.OutBack, _grow)) * _reach;
            _tip = _hand + n * BladePx * _len * Px;
            _made = true;
            // a fast sweep sheds motes off its edge
            if (Vis && KiBlade && _alpha > 0.6f && dt > 0f && Mathf.Abs(Mathf.DeltaAngle(was, _ang)) > 9f)
            {
                _moteT -= dt;
                if (_moteT <= 0f)
                {
                    _moteT = 0.025f;
                    TblFx.Mote(Vector2.Lerp(_hand, _tip, Random.Range(0.55f, 1f)), Px, Hue);
                }
            }
        }

        /// <summary>The blade comes out: a flash at the hand and a glint at the point.</summary>
        private void Drew()
        {
            if (!Vis || !KiBlade)
            {
                return;
            }
            Vector2 at = _made ? _hand : Hand(Limb.Blade);
            Kit.Flash(at, Px, Mathf.Max(2, Mathf.RoundToInt(2f * Kk)), 0.06f, Fx.WithA(Hue.Core, 0.95f), TopLayer, 22);
            Fx.Glint(at + new Vector2(0f, Px), Px, 0.14f, Hue.Core, 0.7f);
        }

        public override void OnEnd(bool aborted)
        {
            if (KiBlade && _made && _alpha > 0.3f && Kit.Visible(_hand))
            {
                TblFx.Shed(_hand, _tip, Px, Hue, 8);   // cut off mid-blow: it breaks into motes
            }
            _alpha = 0f;
            base.OnEnd(aborted);
        }
    }

    /// <summary>The Thousand Blades (lower) effects: ki blade sprites, cut lines, glints, spark rings, scars.</summary>
    internal static class TblFx
    {
        private static readonly Dictionary<int, Sprite> _blades = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _halos = new Dictionary<int, Sprite>();

        /// <summary>A cut's angle as authored facing right, turned to the side the unit faces.</summary>
        public static float Mirror(float ang, float fwd)
        {
            return fwd >= 0f ? ang : 180f - ang;
        }

        /// <summary>
        /// A blade of ki 'len' pixels long along +x, pivot at its guard: a white edge, a paler back that
        /// stops short of the point, a three-pixel guard and a short grip behind it.
        /// </summary>
        public static Sprite KiBlade(int len)
        {
            len = Mathf.Clamp(len, 4, 48);
            if (_blades.TryGetValue(len, out Sprite s) && s != null)
            {
                return s;
            }
            int w = len + 3, h = 5;
            Canvas c = new Canvas(w, h);
            c.Set(0, 2, 0.62f, 1f);
            c.Set(1, 1, 0.78f, 1f);
            c.Set(1, 2, 0.95f, 1f);
            c.Set(1, 3, 0.78f, 1f);
            for (int i = 0; i < len + 1; i++)
            {
                int x = 2 + i;
                float t = i / (float)Mathf.Max(1, len);
                c.Set(x, 2, 1f, 1f);
                if (t > 0.08f && t < 0.78f)
                {
                    c.Set(x, 3, 0.72f, 0.55f);
                }
            }
            s = c.ToSprite("sakuga_tbl_kiblade_" + len, new Vector2(1.5f / w, 2.5f / h));
            _blades[len] = s;
            return s;
        }

        /// <summary>The ki blade's glow: a thin soft band around it (same length and pivot), tapering to the point.</summary>
        public static Sprite KiHalo(int len)
        {
            len = Mathf.Clamp(len, 4, 48);
            if (_halos.TryGetValue(len, out Sprite s) && s != null)
            {
                return s;
            }
            int w = len + 4, h = 5;
            Canvas c = new Canvas(w, h);
            for (int i = 0; i <= len + 1; i++)
            {
                int x = 2 + i;
                float t = i / (float)(len + 1);
                float taper = t < 0.75f ? 1f : Mathf.Clamp01(1f - (t - 0.75f) / 0.25f);
                c.Set(x, 2, 1f, 0.7f * Mathf.Max(0.35f, taper));
                if (t > 0.04f)
                {
                    c.Set(x, 1, 0.9f, 0.4f * taper);
                    c.Set(x, 3, 0.9f, 0.4f * taper);
                }
            }
            s = c.ToSprite("sakuga_tbl_kihalo_" + len, new Vector2(1.5f / w, 2.5f / h));
            _halos[len] = s;
            return s;
        }

        /// <summary>
        /// A cut hanging in the air across 'at' ('ang' degrees in the world, 'half' world units each side),
        /// shown after 'delay' seconds: a tier-edged line with a white core that flashes and fades.
        /// </summary>
        public static void CutLine(Vector2 at, float ang, float half, float px, float delay, float life, Hue hue, FxLayer layer)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            float r = ang * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            Particle o = Kit.Line(at - d * half, at + d * half, px, life, 2, layer, 15, Fx.WithA(hue.Edge, 0.95f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(hue.Edge, 0f));
            Particle i = Kit.Line(at - d * half * 0.88f, at + d * half * 0.88f, px, life * 0.8f, 1, layer, 16, Color.white, Fx.WithA(hue.Core, 0.85f), Fx.WithA(hue.Core, 0f));
            if (o != null)
            {
                o.Delay = delay;
            }
            if (i != null)
            {
                i.Delay = delay;
            }
        }

        /// <summary>
        /// A cut line across a body ('frac' of its height up), shown after 'delay' and following the body
        /// (a launched foe carries its cut up with it). 'halfPx': half its length in the attacker's pixels at
        /// a human's size (bigger bodies, longer cuts).
        /// </summary>
        public static void CutOn(Actor v, float frac, float ang, float halfPx, float px, float delay, float life, Hue hue, FxLayer layer)
        {
            if (v == null)
            {
                return;
            }
            Vector2 at = Choreo.BodyAt(v, frac);
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            float half = halfPx * px * Mathf.Clamp(Choreo.SpritePx(v) * Choreo.PxOf(v) / Mathf.Max(0.04f, 10f * px), 0.8f, 2.6f);
            float r = ang * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            Particle o = Kit.Line(at - d * half, at + d * half, px, life, 2, layer, 15, Fx.WithA(hue.Edge, 0.95f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(hue.Edge, 0f));
            Particle i = Kit.Line(at - d * half * 0.88f, at + d * half * 0.88f, px, life * 0.8f, 1, layer, 16, Color.white, Fx.WithA(hue.Core, 0.85f), Fx.WithA(hue.Core, 0f));
            Follow(o, v, frac, delay);
            Follow(i, v, frac, delay);
        }

        private static void Follow(Particle p, Actor v, float frac, float delay)
        {
            if (p == null)
            {
                return;
            }
            p.Delay = delay;
            long id = v.Sid();
            Vector2 off = p.Pos - Choreo.BodyAt(v, frac);
            p.Step = (q, d) =>
            {
                if (v.Sid() == id && v.isAlive())
                {
                    q.Pos = Choreo.BodyAt(v, frac) + off;
                }
            };
        }

        /// <summary>A bright scar the blade leaves on the ground from a to b: white-hot, cooling to the tier's colour, gone in 'life' s.</summary>
        public static void GroundCut(Vector2 a, Vector2 b, float px, float life, Hue hue)
        {
            if (!Fx.OnScreen(a, 0.2f))
            {
                return;
            }
            WorldTile t = Combat.TileAt(a);
            if (t != null && t.Type != null && t.Type.liquid)
            {
                return;
            }
            Color dark = Fx.WithA(Fx.Darken(Fx.SoilColor(t), 0.45f), 0.8f);
            Particle s = Kit.Line(a, b, px, life, 1, FxLayer.Ground, 4, Color.white, Fx.WithA(hue.Edge, 0.9f), dark, Fx.WithA(dark, 0f));
            if (s != null)
            {
                s.Snap = true;
            }
        }

        /// <summary>A steel "ting": a twinkle and two sparks at 'at'.</summary>
        public static void Ting(Vector2 at, float px, Hue hue, float size = 0.8f)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            Fx.Glint(at, px, 0.14f, Color.Lerp(Color.white, hue.Core, 0.3f), size);
            Kit.Sparks(at, px, 2, 30f, 60f, hue.Edge);
        }

        /// <summary>Sparks thrown out all round a point (blades meeting), and a ring of light.</summary>
        public static void SparkRing(Vector2 at, float px, int n, float speed, Hue hue, float k, float ring = 8f)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            n = Fx.Count(n);
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / Mathf.Max(1, n) + Random.Range(-0.15f, 0.15f);
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(Art.Sparks[Random.Range(0, Art.Sparks.Length)], at + dir * px, px, Random.Range(0.12f, 0.22f), FxLayer.Top, 19);
                if (p == null)
                {
                    break;
                }
                p.Moving(dir * speed * px * Random.Range(0.75f, 1.25f) * Mathf.Sqrt(k), 0f, 7f);
                p.FaceVel = true;
                p.Color(Color.white, new Color(1f, 0.92f, 0.6f, 1f), Fx.WithA(hue.Edge, 0f));
            }
            Fx.Ring(at, px, 0.16f, 1.5f, ring * k, 1, 1, FxLayer.Top, 6, 1f, 2.2f, Fx.WithA(Color.white, 0.95f), Fx.WithA(hue.Edge, 0.6f), Fx.WithA(hue.Edge, 0f));
        }

        /// <summary>A crescent of light (an arc a0..a1) loosed from 'at', drifting along 'vel' (pixels a second) and fading.</summary>
        public static void Moon(Vector2 at, float px, float k, float fwd, float a0, float a1, Hue hue, FxLayer layer, Vector2 vel)
        {
            if (!Fx.OnScreen(at, 0.2f))
            {
                return;
            }
            int r = Mathf.Clamp(Mathf.RoundToInt(13f * k), 6, 50);
            Particle p = Fx.Spawn(Art.Arc(r, 1f, a0, a1, 3), at, px, 0.34f, layer, 9);
            if (p != null)
            {
                p.SX = fwd >= 0f ? 1f : -1f;
                p.Moving(vel * px, 0f, 2.5f).Sized(0.9f, 1.25f).Color(Color.white, Fx.WithA(hue.Core, 0.9f), Fx.WithA(hue.Edge, 0.5f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>A mote of light shed off a blade.</summary>
        public static void Mote(Vector2 at, float px, Hue hue)
        {
            Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.14f, 0.24f), FxLayer.Over, 10);
            p?.Moving(Random.insideUnitCircle * 14f * px + new Vector2(0f, 6f * px), 0f, 3f).Color(Color.white, Fx.WithA(hue.Edge, 0.8f), Fx.WithA(hue.Edge, 0f));
        }

        /// <summary>A blade breaking into motes along a..b (a ki blade sheathed, dissolved, cut off).</summary>
        public static void Shed(Vector2 a, Vector2 b, float px, Hue hue, int n)
        {
            if (!Fx.OnScreen(a, 0.2f))
            {
                return;
            }
            n = Fx.Count(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, (i + Random.value) / Mathf.Max(1, n));
                Particle p = Fx.Spawn(Art.Pixel, at, px, Random.Range(0.2f, 0.35f), FxLayer.Over, 10);
                p?.Moving(Random.insideUnitCircle * 10f * px + new Vector2(0f, 9f * px), 0f, 2.5f).Sized(1.2f, 0.8f)
                    .Color(Color.white, Fx.WithA(hue.Core, 0.85f), Fx.WithA(hue.Edge, 0f));
            }
        }

        /// <summary>A long straight streak of light from a to b (a thrust's line, a draw's line): a white core in a glow.</summary>
        public static void Streak(Vector2 a, Vector2 b, float px, float life, int glow, Hue hue, FxLayer layer, float delay = 0f)
        {
            if (!Fx.OnScreen(a, 0.3f) && !Fx.OnScreen(b, 0.3f))
            {
                return;
            }
            Particle o = Kit.Line(a, b, px, life, glow, layer, 14, Fx.WithA(hue.Edge, 0.9f), Fx.WithA(hue.Edge, 0.55f), Fx.WithA(hue.Rim, 0f));
            Particle i = Kit.Line(a, b, px, life * 0.75f, 1, layer, 15, Color.white, Fx.WithA(hue.Core, 0.9f), Fx.WithA(hue.Core, 0f));
            if (delay > 0f)
            {
                if (o != null)
                {
                    o.Delay = delay;
                }
                if (i != null)
                {
                    i.Delay = delay;
                }
            }
        }
    }
}
