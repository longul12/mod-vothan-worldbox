using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// SUN CANNON (Ki Arts, Grandmaster). It plants its feet wide (the ground cracks under them) and
    /// brings both palms together in front of it: between them a small sun kindles, its corona
    /// flaring and turning, motes pouring into it, its light washing the ground orange round its
    /// feet (Charging: a long, visible gather; a fast blow now breaks it). Then it drives both palms
    /// forward and the sun erupts into a massive beam: a white-hot core in a body of fire-gold, rings
    /// of heat rolling down it, the ground melted into a broad trench beneath it; everyone in it is
    /// driven along it and blasted off at its end. It clashes head-on with other beams (Beams).
    /// A Sun Cannon fired back at a beam already coming is drawn in a heartbeat.
    /// I: 16 tiles, 9 px wide. II: 19 tiles, 11 px, and it sweeps a little across the line as it
    /// fires, its head gouging the ground. III: 22 tiles, 13 px, a wider sweep, and where it ends a
    /// sunburst blows the earth out.
    /// Damage to one caught all the way (x the user's damage): I 3.6, II 4.4, III 5.4 (+ the
    /// sunburst 1.4).
    /// </summary>
    internal sealed class SunCannonTech : Technique
    {
        public override string Id => "sun_cannon";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Line;
        public override School School => School.KiArts;
        public override Form Forms => Form.Beam | Form.Charge | Form.Heavy;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 60f, 95f, 140f };
        public override float[] Cooldown => new[] { 18f, 20f, 22f };

        public static float Length(int level)
        {
            return level <= 1 ? 16f : level == 2 ? 19f : 22f;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 3.6f : level == 2 ? 4.4f : 5.4f;
        }

        private static float Width(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 11f : 13f;
        }

        /// <summary>Degrees it sweeps to each side as it fires.</summary>
        private static float Sweep(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 6f : 11f;
        }

        private static float ChargeTime(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.1f : 1.2f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            // a beam already coming at it: answer it with its own (a clash), drawn fast
            Beam incoming = Beams.Toward(c.Self, 1.5f);
            if (incoming != null && incoming.Spec.Clashes && incoming.A != null && Combat.IsFoe(c.Self, incoming.A) && c.Allowed(incoming.A))
            {
                float d = Vector2.Distance(c.Pos, incoming.A.current_position);
                if (d <= Length(level) * 1.2f && d >= 1.5f)
                {
                    Vector2 dir = (incoming.A.current_position - c.Pos).normalized;
                    return new Plan
                    {
                        Target = incoming.A,
                        Point = incoming.A.current_position,
                        Dir = dir,
                        Count = 1,
                        Score = 220f + c.Value(incoming.A, c.Expected(incoming.A, Mult(level))),
                        Data = c.Forced ? (object)true : "answer"
                    };
                }
            }
            Plan p = Choreo.PickLine(c, this, level, Length(level), 1f, Mult(level), 1, true);
            if (p == null || c.Forced)
            {
                return p;
            }
            // never point blank: the long gather would be broken before the sun left its hands
            if (c.Dist(p.Target) < 2.6f)
            {
                return null;
            }
            // a crowd standing in a line is what it is for (and the sweep catches those beside it)
            p.Score *= p.Count >= 3 ? 1.3f : 1f;
            if (level >= 2)
            {
                int beside = c.OnRay(Choreo.Rotate(p.Dir, Sweep(level)), Length(level), 1f) + c.OnRay(Choreo.Rotate(p.Dir, -Sweep(level)), Length(level), 1f);
                p.Score *= 1f + 0.04f * Mathf.Max(0, beside - 2 * p.Count);
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
            Particle corona = null, wash = null;
            float heatT = 0f, gougeT = 0f, swept = 0f, baseAng = 0f, side = Random.value < 0.5f ? -1f : 1f;
            bool aimed = false, locked = false;
            // the feet planted wide: the ground cracks under them
            m.PoseTo(0.1f, new Shape(-3f, 1.16f, 0.86f, 0f, -0.04f), Ease.OutQuad, Tempo.Wind)
                .AsCharge()
                .OnEnter(mm =>
                {
                    Choreo.Sound("event:/SFX/HIT/HitStone", mm.A.current_tile);
                    if (mm.Vis)
                    {
                        WorldTile t = mm.A.current_tile;
                        Kit.Crater(mm.Pose.Ground, mm.Px * mm.Kk, t, false, 3f, true);
                        Kit.DustRing(mm.Pose.Ground, mm.Px, t, 10, 40f, 0.5f, mm.Kk);
                        Kit.Debris(mm.Pose.Ground, mm.Px, t, 4, mm.Kk * 0.7f);
                    }
                });
            // the sun kindles between its palms
            m.Charge(ChargeTime(level) * (answer ? 0.35f : 1f), ChargeStyle.Palms, 3f + 0.6f * level, Element.Fire)
                .Every((mm, k) =>
                {
                    Hue h = mm.Hue.As(Element.Fire);
                    Vector2 at = mm.OrbAt();
                    if (mm.Vis && corona == null)
                    {
                        corona = mm.Glue(Fx.Spawn(Art.Burst(Mathf.Clamp(Mathf.RoundToInt((5f + 1.5f * level) * mm.Kk), 4, 40)), at, mm.Px, 1f, FxLayer.Over, 7));
                        wash = mm.Glue(Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt((12f + 3f * level) * mm.Kk), 3, 60), Art.Ground, 0.6f), mm.Pose.Ground, mm.Px, 1f, FxLayer.Ground, 3));
                    }
                    if (corona != null && corona.Alive)
                    {
                        corona.Pos = at;
                        corona.Rot += 220f * Clock.Dt;
                        corona.Size0 = corona.Size1 = Mathf.Lerp(0.2f, 1f, Beat.Apply(Ease.OutQuad, k)) * (1f + 0.1f * Mathf.Sin(mm.BeatT * 33f));
                        corona.Grad = null;
                        corona.R.color = Fx.WithA(Color.Lerp(h.Edge, h.Core, 0.3f + 0.3f * Mathf.Sin(mm.BeatT * 21f)), 0.5f + 0.4f * k);
                        corona.Cine = mm.Cinematic;
                    }
                    if (wash != null && wash.Alive)
                    {
                        wash.Pos = mm.Pose.Ground + mm.Dir * 0.3f * mm.Kk;
                        wash.Size0 = wash.Size1 = 0.4f + 0.6f * k;
                        wash.Grad = null;
                        wash.R.color = Fx.WithA(h.Edge, 0.18f + 0.22f * k);
                    }
                    if (!mm.Vis)
                    {
                        return;
                    }
                    heatT -= Clock.Dt;
                    if (heatT <= 0f)
                    {
                        heatT = 0.06f;
                        // heat rising off the sun, sparks spitting
                        Kit.Ember(at + Random.insideUnitCircle * 2f * mm.Px, mm.Px, 0.4f, new Vector2(Random.Range(-4f, 4f), Random.Range(8f, 16f)) * mm.Px, h.Edge);
                        if (k > 0.5f && Random.value < 0.4f)
                        {
                            Kit.Sparks(at, mm.Px, 2, 30f, 70f, h.Edge, mm.TopLayer);
                        }
                    }
                });
            m.Fire(new BeamSpec
            {
                Length = Length(level),
                Width = Width(level),
                Speed = 36f,
                Life = level <= 1 ? 0.8f : level == 2 ? 0.95f : 1.1f,
                Mult = Mult(level),
                Push = level <= 1 ? 2.2f : level == 2 ? 2.6f : 3.2f,
                Trench = level <= 1 ? 1f : level == 2 ? 1.2f : 1.4f,
                Power = 1.1f,
                Element = Element.Fire
            });
            // the hold: it braces against the recoil; II and III sweep the sun across the line
            m.Every((mm, k) =>
            {
                Beam b = mm.Beam;
                if (b == null || !b.Firing)
                {
                    return;
                }
                if (!aimed)
                {
                    aimed = true;
                    baseAng = Mathf.Atan2(b.Dir.y, b.Dir.x) * Mathf.Rad2Deg;
                    Choreo.Sound("event:/SFX/POWERS/HeatRayMelts", mm.A.current_tile, 0.3f);
                    Ruin.Quake(mm.A, mm.Pose.Ground, mm.Scale, 1.3f, mm.Kk);
                }
                float amp = Sweep(level);
                if (amp <= 0f || locked)
                {
                    return;
                }
                if (b.Clash != null || b.Won || b.Lost)
                {
                    locked = true;   // a clash holds it where it is
                    return;
                }
                if (b.Len < b.MaxLen * 0.95f)
                {
                    return;   // (the sweep starts once the beam is out)
                }
                swept += Clock.Dt;
                float ang = baseAng + side * amp * Mathf.Sin(swept / Mathf.Max(0.3f, b.Spec.Life) * Mathf.PI * 1.6f);
                float r = ang * Mathf.Deg2Rad;
                b.Dir = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
                // its head gouges the ground as it swings
                gougeT -= Clock.Dt;
                if (gougeT <= 0f)
                {
                    gougeT = 0.12f;
                    Vector2 tip = b.GroundTip;
                    Ruin.Blast(mm.A, tip, 0.7f, Scale.Heavy, mm.Pose.Ground, true, -1);
                    if (Kit.Visible(tip))
                    {
                        Kit.Debris(tip, mm.Px, Combat.TileAt(tip), 3, mm.Kk);
                    }
                }
            });
            if (level >= 3)
            {
                m.Last.Exit = mm => Sunburst(mm);
            }
            m.Recover(0.36f);
            return m.Start();
        }

        /// <summary>III: where the beam ends, the sun bursts: an impact frame, the earth blown out, everyone near thrown.</summary>
        private static void Sunburst(ChoreoMove m)
        {
            Beam b = m.Beam;
            if (b == null || b.Lost || b.Len < b.MaxLen * 0.9f)
            {
                return;   // (cut short: caught, cleaved, or it lost a clash)
            }
            Vector2 at = Combat.Clamp(b.GroundTip);
            WorldTile tile = Combat.TileAt(at);
            Hue h = m.Hue.As(Element.Fire);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            if (Kit.Visible(at, 0.5f))
            {
                float px = m.Px;
                Kit.ImpactFrame(at + new Vector2(0f, 0.6f * m.Kk), px, 1.1f * m.Kk, h.Pal, false);
                Kit.Flash(at + new Vector2(0f, 0.4f * m.Kk), px, Mathf.RoundToInt(16f * m.Kk), 0.14f, Fx.WithA(h.Core, 0.95f));
                Fx.Ring(at, px, 0.4f, 4f, 30f * m.Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f, Fx.WithA(Color.white, 1f), Fx.WithA(h.Edge, 0.7f), Fx.WithA(h.Rim, 0f));
                Kit.Crater(at, px * m.Kk, tile, true, 7f, false, h.Edge);
                Kit.Debris(at, px, tile, 12, m.Kk);
                Kit.DustRing(at, px, tile, 14, 60f, 0.6f, m.Kk);
                for (int i = 0; i < 6; i++)
                {
                    Kit.Ember(at + Random.insideUnitCircle * 6f * px, px, 0.7f, new Vector2(Random.Range(-14f, 14f), Random.Range(14f, 34f)) * px, h.Edge);
                }
            }
            Ruin.Blast(m.A, at, 1.4f, Scale.Heavy, m.Pose.Ground);
            Ruin.Quake(m.A, at, Scale.Heavy, 1.4f, m.Kk);
            Kit.Stop(at, 0.05f);
            m.AreaBlow(at, 2.2f * Mathf.Lerp(1f, m.Kk, 0.5f), 1.4f, null, Hurt.Launch, 1.8f, 1.4f);
        }
    }
}
