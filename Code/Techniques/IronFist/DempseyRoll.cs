using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// WEAVING ROLL (Iron Fist, Expert; the Dempsey roll). In close, the boxer drops into a crouch and
    /// starts to weave: its upper body rolls out to one side and under, and swings back through the
    /// middle throwing a hook with the whole momentum of the roll, then out to the other side and back
    /// with the other hand, faster and faster: a figure eight traced in the air (a ribbon of its colour
    /// and its afterimages draw the infinity). While it rolls out of line nothing touches it (shots and
    /// blows pass through the weave). Hooks to the head snap the foe's head from side to side, every
    /// third one digs into the body; the last one, harder, spins the foe round (II: on an impact frame;
    /// III: off its feet). 4 / 6 / 8 hooks. Chains from Close. Damage (x the user's damage): I 2.4,
    /// II 3.4, III 4.6 in all (the last hook counts double).
    /// </summary>
    internal sealed class DempseyRollTech : Technique
    {
        public override string Id => "dempsey_roll";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Chain;
        public override School School => School.IronFist;
        public override Form Forms => Form.Dash | Form.Flurry | Form.Evasive | Form.Hook;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 16f, 30f, 50f };
        public override float[] Cooldown => new[] { 8f, 9f, 10f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        public static int Hooks(int level)
        {
            return level <= 1 ? 4 : level == 2 ? 6 : 8;
        }

        public static float Total(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3.4f : 4.6f;
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.4f, Total = Total(1), Crowd = 0.1f, Alone = 8f, IntoGuard = 0.8f, Worth = 14f },
            new MeleeWant { Reach = 2.6f, Total = Total(2), Crowd = 0.08f, Alone = 10f, IntoGuard = 0.8f, Worth = 18f },
            new MeleeWant { Reach = 2.8f, Total = Total(3), Crowd = 0.06f, Alone = 12f, IntoGuard = 0.8f, Worth = 24f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            return IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            WeaveMove m = new WeaveMove(a, this, p);
            if (m.V == null)
            {
                return false;
            }
            int level = m.Level;
            int n = Hooks(level);
            float each = Total(level) / (n + 1);
            const Form weaving = Form.Dash | Form.Evasive | Form.Flurry | Form.Hook;
            const Form hooking = Form.Punch | Form.Hook | Form.Flurry;
            m.StepIn()
                // down into the crouch the roll starts from
                .PoseTo(0.08f, new Shape(10f, 1.12f, 0.86f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind)
                .Shows(weaving)
                .OnEnter(mm => ((WeaveMove)mm).Trailing = true);
            for (int i = 0; i < n; i++)
            {
                float side = (i & 1) == 0 ? 1f : -1f;
                bool last = i == n - 1;
                // THE ROLL: out to one side and under (out of line: nothing touches it), bowing back
                m.Add(new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = Mathf.Max(0.05f, 0.085f - 0.006f * i),
                    To = new Shape(-12f, 1.1f, 0.84f, 0f, -0.2f, side * 0.45f),
                    Ease = Ease.InOutSine,
                    Intangible = true,
                    Ghosts = 0.025f,
                    Forms = weaving,
                    Every = (mm, k) => mm.Pose.Ground -= mm.Dir * (0.08f * mm.Kk * Mathf.Sin(Mathf.PI * k))
                });
                // THE HOOK, swung back through the middle with the roll's momentum
                Hit h;
                if (last)
                {
                    h = new Hit(Blow.Hook, each * 2f)
                    {
                        Hurt = level >= 3 ? Hurt.Knockdown : Hurt.Spin,
                        Push = level >= 3 ? 1.1f : level == 2 ? 1.3f : 0.9f,
                        Daze = 0.4f + 0.2f * level,
                        Power = 1.3f + 0.1f * level,
                        WindScale = 0.7f,
                        Finisher = true,
                        Shows = hooking
                    };
                }
                else
                {
                    bool body = i % 3 == 2;
                    h = new Hit(body ? Blow.Body : Blow.Hook, each)
                    {
                        Hurt = body ? Hurt.Fold : Hurt.SnapBack,
                        Push = body ? 0.06f : 0.14f,
                        Power = 0.9f + 0.04f * i,
                        WindScale = Mathf.Max(0.35f, 0.55f - 0.03f * i),
                        ActScale = 0.85f,
                        Shows = hooking | (body ? Form.Body : Form.High)
                    };
                }
                m.Strike(h);
            }
            m.Recover(0.22f).OnEnter(mm => ((WeaveMove)mm).Trailing = false);
            return m.Start();
        }
    }

    /// <summary>
    /// The weaving roll's move: a ChoreoMove that draws a ribbon of its colour behind its head while it
    /// rolls (the figure eight), on screen only, a short segment a frame (no garbage beyond the particles).
    /// </summary>
    internal sealed class WeaveMove : ChoreoMove
    {
        public bool Trailing;
        private Vector2 _last;
        private bool _has;

        public WeaveMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (!Trailing || Finished || Ended || Broken || !Vis)
            {
                _has = false;
                return;
            }
            Vector2 at = HeadPos() - new Vector2(0f, 0.9f * Px * Kk);
            if (_has && (at - _last).sqrMagnitude > Px * Px * 0.25f)
            {
                Color c = Level >= 2 ? Hue.Edge : Hue.Main;
                // (a bright thread over the bodies, a wider glow of the school's colour under them)
                Kit.Line(_last, at, Px, 0.36f + 0.05f * Level, 1, FxLayer.Over, 4, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(c, 0.65f), Fx.WithA(c, 0f));
                Kit.Line(_last, at, Px, 0.3f + 0.05f * Level, Level >= 3 ? 3 : 2, FxLayer.Behind, 3, Fx.WithA(c, 0.55f), Fx.WithA(c, 0.3f), Fx.WithA(c, 0f));
            }
            _last = at;
            _has = true;
        }
    }
}
