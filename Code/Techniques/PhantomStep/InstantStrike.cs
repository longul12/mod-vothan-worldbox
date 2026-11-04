using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// HORIZON STEP (Phantom Step, God; id instant_strike). No motion at all. The God stands still for a
    /// breath, a glint in its eye... and in the next frame it is simply behind the target, its back
    /// turned, already at the end of the stroke (a draw cut with a blade, a hand-blade of ki
    /// without), and the target is already falling. Nothing travelled: only a faint white shape is
    /// left where it stood. Then the frame splits: one hairline of white across the whole view at the
    /// target's middle, the picture springing apart along it for an instant (two bright edges, a dark
    /// gap) and snapping shut, the world holding still; the ground splits along the same horizon. It
    /// holds the pose with its back turned, then straightens, slowly, and turns round.
    /// A God's answer to an equal (Scale Heavy: no cinematic, but a global hit-stop and an impact frame).
    ///
    /// Levels (Gods always III; I-II through a bare trait): I x4 and a 6-tile split in the ground;
    /// II x5.5, 10 tiles; III x7, 16 tiles, the black impact frame. It reaches 9 / 11 / 13 tiles.
    /// </summary>
    internal sealed class InstantStrikeTech : Technique
    {
        public override string Id => "instant_strike";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Single;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Teleport | Form.Fast | Form.Ultimate;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 90f, 120f, 160f };
        public override float[] Cooldown => new[] { 30f, 32f, 34f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 9f : level == 2 ? 11f : 13f;
        }

        private static float Mult(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5.5f : 7f;
        }

        /// <summary>How long the ground splits along the horizon (tiles).</summary>
        private static float Split(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 10f : 16f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f);
            if (c.Forced)
            {
                Actor t = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (t == null || !Choreo.Fair(c.Self, t, out _, true) || !PsHiKit.Standing(t) || c.Dist(t) > range + 6f
                    || !PsHiKit.SpotBehind(t, c.Pos, 0.7f * k + t.stats["size"] * 0.4f, out _))
                {
                    return null;
                }
                return new Plan { Target = t, Point = t.current_position, Dir = PsHiKit.DirTo(c.Pos, t.current_position), Count = 1, Score = 1f, Data = true };
            }
            Actor best = null;
            float bestScore = 0f;
            for (int i = -1; i < c.Enemies.Count && i < 10; i++)
            {
                Actor v = i < 0 ? c.Focus : c.Enemies[i];
                if (v == null || i >= 0 && v == c.Focus)
                {
                    continue;
                }
                float d = c.Dist(v);
                if (d > range || !Choreo.Fair(c.Self, v, out float cm) || !PsHiKit.Standing(v))
                {
                    continue;
                }
                // a single stroke of this weight is for one worth it: not a foe an ordinary blow ends
                if (v.getHealth() <= c.Expected(v, 1.5f) * cm || Tiers.Of(v) == Tier.None && Weights.Species(v) < Weight.Brute)
                {
                    continue;
                }
                if (!PsHiKit.SpotBehind(v, c.Pos, 0.7f * k + v.stats["size"] * 0.4f, out _))
                {
                    continue;
                }
                float score = c.Value(v, c.Expected(v, Mult(level)), cm);
                score += 20f * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
                FoeState s = c.State(v);
                if (s.Charging || s.Recovering)
                {
                    score += 25f;   // the instant it is open
                }
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                return null;
            }
            return new Plan { Target = best, Point = best.current_position, Dir = PsHiKit.DirTo(c.Pos, best.current_position), Count = 1, Score = bestScore };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            Form shows = Form.Teleport | Form.Fast;
            // stillness: nothing moves but a glint in its eye
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.2f,
                To = new Shape(-2f, 1f, 1f, 0f, 0f),
                Ease = Ease.InOutSine,
                Forms = shows
            }.On(0.55f, mm =>
            {
                if (mm.Vis)
                {
                    Fx.Glint(mm.HeadPos() + new Vector2(mm.Fwd * 0.6f * mm.Px, -0.8f * mm.Px), mm.Px, 0.16f, Color.white, 0.8f);
                }
            }));
            // ...and it is behind the target, already at the end of the stroke
            m.Do(Strike);
            // it holds the pose, its back turned
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.32f, Forms = shows, Track = false });
            // and straightens, slowly
            m.Add(new Beat
            {
                Tempo = Tempo.Follow,
                Dur = 0.26f,
                To = new Shape(0f, 1f, 1f, 0f, 0f),
                Ease = Ease.InOutSine,
                Forms = shows,
                Track = false,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.Hand(Limb.Blade) + new Vector2(0f, 1f * mm.Px), mm.Px, 0.18f, mm.Hue.Core, 0.9f);
                    }
                    Choreo.Sound("event:/SFX/HIT/HitSwordSword", mm.A.current_tile, 0.2f);
                }
            });
            m.Do(mm =>
            {
                if (mm.V != null && mm.V.Sid() == mm.VId && Combat.Living(mm.V))
                {
                    mm.Face(mm.VG - mm.Stance);
                }
            });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The frame where it is already behind the target and the stroke is done.</summary>
        private static void Strike(ChoreoMove m)
        {
            if (!m.VOk)
            {
                m.Lose();
                return;
            }
            Actor v = m.V;
            Vector2 from = m.Stance;
            if (!PsHiKit.SpotBehind(v, from, m.Stand * 1.1f, out Vector2 to))
            {
                m.Lose();
                return;
            }
            int level = m.Level;
            Vector2 travel = PsHiKit.DirTo(from, to, m.Dir);
            if (m.Vis)
            {
                // all that is left where it stood: a faint white shape
                Fx.Afterimage(m, 0.3f, new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0f));
            }
            m.Stance = to;
            m.Pose.Ground = to;
            // its back to the target, at the end of the stroke
            m.Face(to - m.VG);
            BlowShape s = ChoreoMove.ShapeOf(Blow.DrawCut);
            m.Pose.Rot = s.Hit.Rot;
            m.Pose.SX = s.Hit.SX;
            m.Pose.SY = s.Hit.SY;
            m.Pose.Height = 0f;
            m.Vis = Fx.OnScreen(to, 0.35f);
            m.WatchMe();
            if (!m.Touch(v))
            {
                return;   // it read even this (a God's own answer): the engine plays the overreach
            }
            Vector2 at = Choreo.BodyAt(v, 0.55f);
            Hit h = new Hit(Blow.DrawCut, Mult(level))
            {
                Hurt = Hurt.Crumple,
                Daze = 1.2f,
                Finisher = true,
                Lethal = true,
                Power = 1.6f
            };
            m.Deliver(v, h, s, at, travel);
            Horizon(m, at);
            // the ground splits along the same horizon
            Vector2 g = m.VG;
            float half = Split(level) * 0.5f;
            Ruin.Furrow(m.A, g - new Vector2(half, 0f), g + new Vector2(half, 0f), 0.45f, m.Scale, m.Pose.Ground, false);   // (a clean cut: no fire)
            Ruin.Quake(m.A, g, m.Scale, 1.5f, m.Kk);
        }

        /// <summary>
        /// The frame splits: one hairline of white across the whole view at the target's middle, the
        /// picture springing apart along it (two bright edges, a dark gap) and snapping shut.
        /// </summary>
        private static void Horizon(ChoreoMove m, Vector2 at)
        {
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            float half = 26f + 4f * m.Level;
            Vector2 l = at - new Vector2(half, 0f), r = at + new Vector2(half, 0f);
            FxLayer layer = m.TopLayer;
            // the band of light the instant it happens
            Particle band = Fx.Spawn(Art.Pixel, at, px, 0.07f, layer, 37);
            if (band != null)
            {
                band.SX = 2f * half / px;
                band.SY = 9f;
                band.Color(new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f));
            }
            // the dark gap the picture opens on
            Particle gap = Fx.Spawn(Art.Pixel, at, px, 0.2f, layer, 38);
            if (gap != null)
            {
                gap.SX = 2f * half / px;
                gap.Color(Fx.WithA(m.Hue.Dark, 0.85f), Fx.WithA(m.Hue.Dark, 0.7f), Fx.WithA(m.Hue.Dark, 0f));
                gap.Step = (q, dt) => q.SY = 1f + 5f * Mathf.Sin(Mathf.PI * q.K);
            }
            // its two bright edges spring apart and come back together
            for (int s = -1; s <= 1; s += 2)
            {
                float side = s;
                Particle edge = Kit.Line(l, r, px, 0.2f, 1, layer, 39, Color.white, Fx.WithA(m.Hue.Core, 0.9f), Fx.WithA(m.Hue.Edge, 0f));
                if (edge != null)
                {
                    Vector2 c0 = edge.Pos;
                    edge.Step = (q, dt) => q.Pos = c0 + new Vector2(0f, side * 3f * px * Mathf.Sin(Mathf.PI * q.K));
                }
            }
            // the hairline itself, which stays a moment longer
            Kit.Line(l, r, px, 0.42f, 3, layer, 40, Fx.WithA(m.Hue.Edge, 0.9f), Fx.WithA(m.Hue.Edge, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
            Kit.Line(l, r, px, 0.36f, 1, layer, 41, Color.white, new Color(1f, 1f, 1f, 0.85f), new Color(1f, 1f, 1f, 0f));
            Fx.FocusLines(at, px, Fx.Count(8), 10f * m.Kk, 18f * m.Kk, 0.12f);
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", Combat.TileAt(at));
        }
    }
}
