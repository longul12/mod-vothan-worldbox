using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DRAGON WAVE (Ki Arts, Expert). Cupped hands drawn back to the hip, an orb of gold light swelling
    /// between them (motes streaming in, rings closing, the ground trembling: Charging, a fast blow now
    /// breaks it), then both hands thrust forward and a beam roars out along the line: a white core in
    /// a gold body, rings spiralling down it, a trench torn in the ground beneath. Everyone in it is
    /// driven along it and blasted off at the end. Meeting another beam head-on, the two lock in a
    /// clash (Beams): the stronger overwhelms the other. A Dragon Wave fired back at a beam already
    /// coming is drawn in a heartbeat; one fired after a launch or a throw takes the body in the air.
    /// I: 10 tiles; II: 12, wider and longer, two strands of light coil round it like a dragon's body;
    /// III: 14, a gold flood with a dragon's crest of light at its head.
    /// Damage to one caught all the way (x the user's damage): I 2.2, II 3, III 4.2.
    /// </summary>
    internal sealed class DragonWaveTech : Technique
    {
        public override string Id => "dragon_wave";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Line;
        public override School School => School.KiArts;
        public override Form Forms => Form.Beam | Form.Charge | Form.Ki;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 28f, 50f, 80f };
        public override float[] Cooldown => new[] { 10f, 12f, 14f };

        public override bool Accepts(Link link)
        {
            // (the classic: a blow sends the foe flying, the beam follows it)
            return link == Link.Launched || link == Link.Thrown;
        }

        public static float Length(int level)
        {
            return level <= 1 ? 10f : level == 2 ? 12f : 14f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 2.2f : level == 2 ? 3f : 4.2f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 0.7f : level == 2 ? 0.8f : 0.9f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            // a beam already coming at it: answer it with its own (a clash), drawn fast
            Beam incoming = Beams.Toward(c.Self, 1.5f);
            if (incoming != null && incoming.Spec.Clashes && incoming.Clash == null && incoming.A != null && Combat.IsFoe(c.Self, incoming.A) && c.Allowed(incoming.A))
            {
                float d = Vector2.Distance(c.Pos, incoming.A.current_position);
                if (d <= Length(level) * 1.2f && d >= 1.5f && Combat.ClearPath(c.Pos, c.Pos + (incoming.A.current_position - c.Pos) / d * Mathf.Min(2.5f, d), true))
                {
                    Vector2 dir = (incoming.A.current_position - c.Pos).normalized;
                    return new Plan
                    {
                        Target = incoming.A,
                        Point = incoming.A.current_position,
                        Dir = dir,
                        Count = 1,
                        Score = 200f + c.Value(incoming.A, c.Expected(incoming.A, Mult(level))),
                        Data = c.Forced ? (object)true : "answer"
                    };
                }
            }
            Plan p = Choreo.PickLine(c, this, level, Length(level), 0.6f, Mult(level), 1, true);
            if (p == null)
            {
                return null;
            }
            if (!c.Forced)
            {
                // never point blank: the wind-up would be broken before the beam left its hands
                if (c.Dist(p.Target) < 2f)
                {
                    return null;
                }
                // more value the more it catches; a crowd standing in a line is what it is for
                p.Score *= p.Count >= 3 ? 1.25f : 1f;
            }
            return p;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            bool answer = p.Data is string s && s == "answer";
            m.Face(p.Dir);
            float charge = ChargeTime(level) * (answer ? 0.35f : 1f);
            Particle crest = null;
            m.Charge(charge, ChargeStyle.Hip, level <= 1 ? 2.5f : level == 2 ? 3f : 3.6f, Element.Ki)
                .Fire(new BeamSpec
                {
                    Length = Length(level),
                    Width = level <= 1 ? 4f : level == 2 ? 5f : 6.5f,
                    Speed = 42f,
                    Life = level <= 1 ? 0.45f : level == 2 ? 0.55f : 0.7f,
                    Mult = Mult(level),
                    Push = level <= 1 ? 1.5f : level == 2 ? 2f : 2.6f,
                    Trench = level <= 1 ? 0.6f : level == 2 ? 0.8f : 1f,
                    Element = Element.Ki
                })
                // (while it fires: the dragon's coils and crest, from II)
                .Every((mm, k) =>
                {
                    if (mm.Level >= 2)
                    {
                        Coil(mm, ref crest);
                    }
                })
                .Recover(0.32f)
                .OnEnter(mm =>
                {
                    if (crest != null)
                    {
                        mm.Unglue(crest);
                        crest = null;
                    }
                });
            return m.Start();
        }

        /// <summary>
        /// Two strands of light coiling round the firing beam, flowing down it like a dragon's body
        /// (II and III), and at III a crest of light riding its head.
        /// </summary>
        private static void Coil(ChoreoMove m, ref Particle crest)
        {
            Beam b = m.Beam;
            if (b == null || !b.Firing || b.Len < 0.5f || !Kit.Visible(Vector2.Lerp(b.From, b.Tip, 0.5f), 0.5f))
            {
                if (crest != null)
                {
                    m.Unglue(crest);
                    crest = null;
                }
                return;
            }
            float px = m.Px;
            Vector2 side = Choreo.Perp(b.Dir);
            float amp = b.Spec.Width * px * Mathf.Lerp(1f, m.Kk, 0.5f) * 1.05f;
            // two strands drawn as connected segments along the helix, flowing forward with time
            float step = 0.55f * Mathf.Lerp(1f, m.Kk, 0.5f);
            int n = Mathf.Min(Fx.Count(40), Mathf.CeilToInt(b.Len / step));
            float flow = m.T * 16f;
            for (int strand = 0; strand < 2; strand++)
            {
                float off = strand * Mathf.PI;
                Vector2 last = b.From + side * Mathf.Sin(-flow + off) * amp;
                for (int i = 1; i <= n; i++)
                {
                    float s = Mathf.Min(b.Len, i * step);
                    float ph = s * 1.6f - flow + off;
                    Vector2 p = b.From + b.Dir * s + side * Mathf.Sin(ph) * amp;
                    // (the stretch behind the beam is dimmer: it coils round it in depth)
                    bool front = Mathf.Cos(ph) > 0f;
                    Kit.Line(last, p, px, 0.035f, m.Level >= 3 && front ? 2 : 1, FxLayer.Top, front ? 16 : 9,
                        Fx.WithA(strand == 0 ? Color.white : Color.Lerp(m.Hue.Rim, m.Hue.Core, 0.3f), front ? 1f : 0.45f), Fx.WithA(m.Hue.Edge, 0f));
                    last = p;
                }
            }
            if (m.Level < 3)
            {
                return;
            }
            // the crest: a bow of light riding the beam's head
            if (crest == null || !crest.Alive)
            {
                crest = m.Glue(Fx.Spawn(ChoreoArt.Crescent(Mathf.RoundToInt(b.Spec.Width * 1.3f * Mathf.Lerp(1f, m.Kk, 0.5f)), 2), b.Tip, px, 1f, FxLayer.Top, 17));
            }
            if (crest != null)
            {
                crest.Pos = b.Tip + b.Dir * 1.5f * px;
                crest.Rot = Mathf.Atan2(b.Dir.y, b.Dir.x) * Mathf.Rad2Deg;
                crest.Grad = null;
                crest.Size0 = crest.Size1 = 1f + 0.12f * Mathf.Sin(m.T * 40f);
                crest.R.color = Fx.WithA(Color.Lerp(m.Hue.Core, m.Hue.Rim, 0.25f), 0.95f);
            }
        }
    }
}
