using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// WORLD SUPLEX (Earth Breaker, God, Ultimate). It seizes the foe round the waist and hoists it; the
    /// world dims (a cinematic and its title card, when the settings allow), the ground cracks under its
    /// feet as it sinks, and it leaps impossibly high with the foe in its arms, out of sight, leaving a
    /// crater and a column of dust where it stood. A twinkle at the top of the sky; the pair turns over
    /// into the suplex's arch, and falls, a streak of light and embers, a ring closing on the ground
    /// where it will land. The foe is driven head first into the earth: an impact frame, the world
    /// stops, a crater 8-10 tiles across, fissures running out of it, a shockwave throwing down every
    /// enemy within it. It holds the bridge in the crater a heartbeat, the dimness lifts, and it kips
    /// up onto its feet. Leap I 7, II 10, III 13 tiles high. Damage (x the user's damage): 0.2 at the grab,
    /// then I 4, II 5.2, III 6.8 at the slam; the shockwave x1 / 1.3 / 1.6 on everyone else in it.
    /// </summary>
    internal sealed class WorldSuplexTech : Technique
    {
        public override string Id => "world_suplex";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Single;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Grab | Form.Throw | Form.Aerial | Form.Ultimate;
        public override Scale Scale => Scale.Ultimate;
        public override float[] ManaCost => new[] { 90f, 130f, 180f };
        public override float[] Cooldown => new[] { 50f, 55f, 60f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close || link == Link.Down;
        }

        public static float Height(int level)
        {
            return EarthKit.L(level, 7f, 10f, 13f);
        }

        public static float SlamMult(int level)
        {
            return EarthKit.L(level, 4f, 5.2f, 6.8f);
        }

        public static float ShockMult(int level)
        {
            return EarthKit.L(level, 1f, 1.3f, 1.6f);
        }

        /// <summary>The crater's base radius (x the God's and the Ultimate's scaling: 8-10 tiles).</summary>
        public static float CraterBase(int level)
        {
            return EarthKit.L(level, 3f, 3.4f, 3.7f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 2.6f, Total = 0.2f + SlamMult(1), Hold = true, Crowd = 0.04f, Alone = 20f, IntoGuard = 1.3f, Worth = 40f },
            new MeleeWant { Reach = 2.8f, Total = 0.2f + SlamMult(2), Hold = true, Crowd = 0.04f, Alone = 20f, IntoGuard = 1.3f, Worth = 50f },
            new MeleeWant { Reach = 3f, Total = 0.2f + SlamMult(3), Hold = true, Crowd = 0.04f, Alone = 24f, IntoGuard = 1.3f, Worth = 60f }
        };

        private static readonly List<Actor> _near = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // it comes down where it stands: solid ground, not water
            if (!Combat.SafeSpot(c.Pos) && !Choreo.Wading(c.Pos))
            {
                return null;
            }
            // the shockwave catches everyone round the crater
            float r = Ruin.Radius(Tier.God, Scale.Ultimate, CraterBase(level));
            _near.Clear();
            c.CountNear(c.Pos, r, _near);
            int n = 1;
            for (int i = 0; i < _near.Count && n < 8; i++)
            {
                Actor u = _near[i];
                if (u == p.Target || !c.Allowed(u, out float cm))
                {
                    continue;
                }
                p.Score += c.Value(u, c.Expected(u, ShockMult(level)), cm);
                n++;
            }
            p.Count = n;
            return p;
        }

        private static bool Holding(ChoreoMove m)
        {
            return m.Held != null;
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            float h = Height(level) * Mathf.Sqrt(m.Kk);
            m.Dash().OnlyIf(mm => mm.V != null && Vector2.Distance(mm.Stance, mm.VG) > 2.2f * mm.Kk);
            m.StepIn()
                .Grab(0.2f, Grip.Hug)
                // hoisted off its feet, clutched, the suplex's hold
                .Lift(new Grip(2.5f, 5f, -18f), 0.14f, new Shape(-14f, 0.96f, 1.08f, 0f));
            m.OpenCinematic(8f, 0.45f, "sakuga_title_world_suplex").OnlyIf(Holding);
            // it sinks: the ground cracks under its feet
            m.Crouch(0.16f, 1.4f).OnlyIf(Holding).OnEnter(Gather);
            // the leap: impossibly high, out of sight
            Beat rise = new Beat { Tempo = Tempo.Travel, Dur = 0.42f, To = new Shape(-6f, 0.84f, 1.26f, h), Ease = Ease.OutCubic, Ghosts = 0.025f, Track = false, Forms = Form.Grab | Form.Aerial | Form.Ultimate, If = Holding };
            rise.Enter = Launch;
            rise.Every = (mm, k) =>
            {
                if (mm.Vis && k < 0.7f)
                {
                    Kit.SpeedLines(mm.BodyPos() - new Vector2(0f, 2f * mm.Px), Vector2.up, mm.Px, 2, 5f, 0.12f, Fx.WithA(mm.Hue.Edge, 0.85f), mm.TopLayer);
                }
            };
            m.Add(rise);
            // the top of the sky: a twinkle, a ring
            Beat apex = new Beat { Tempo = Tempo.Free, Dur = 0.16f, To = new Shape(-10f, 0.96f, 1.04f, h + 0.15f), Ease = Ease.OutQuad, Track = false, If = Holding };
            apex.Enter = mm =>
            {
                if (mm.Vis)
                {
                    Vector2 at = mm.BodyPos();
                    Fx.Glint(at, mm.Px, 0.3f, Color.white, 2f);
                    Fx.Ring(at, mm.Px, 0.3f, 3f, 18f * mm.Kk, 2, 1, mm.TopLayer, 6, 1f, 2f, Fx.WithA(mm.Hue.Core, 0.95f), Fx.WithA(mm.Hue.Edge, 0.5f), Fx.WithA(mm.Hue.Rim, 0f));
                }
            };
            m.Add(apex);
            // the pair turns over into the arch
            m.Add(new Beat { Tempo = Tempo.Act, Dur = 0.2f, To = new Shape(-168f, 1.02f, 0.96f, h + 0.3f), Grip = new Grip(3f, 3f, 0f), Ease = Ease.InOutSine, Ghosts = 0.03f, Track = false, Forms = Form.Grab | Form.Throw | Form.Aerial | Form.Overhead | Form.Ultimate, If = Holding });
            // ...and falls, a streak of light and embers
            Beat fall = new Beat { Tempo = Tempo.Travel, Dur = 0.34f, To = new Shape(-172f, 0.92f, 1.12f, 0f), Grip = new Grip(3f, 2f, 0f), Ease = Ease.InQuad, Ghosts = 0.02f, Track = false, Forms = Form.Grab | Form.Throw | Form.Overhead | Form.Ultimate, If = Holding };
            bool warned = false;
            fall.Enter = mm => warned = false;
            fall.Every = (mm, k) =>
            {
                if (!mm.Vis)
                {
                    return;
                }
                Vector2 body = mm.BodyPos();
                Kit.Line(body, body + new Vector2(0f, 2.5f * mm.Kk), mm.Px, 0.08f, 2, mm.TopLayer, 10, Fx.WithA(mm.Hue.Core, 0.9f), Fx.WithA(mm.Hue.Edge, 0.6f), Fx.WithA(mm.Hue.Edge, 0f));
                if (Random.value < 0.6f)
                {
                    Kit.Ember(body + Random.insideUnitCircle * 3f * mm.Px, mm.Px, 0.3f, new Vector2(Random.Range(-4f, 4f), 14f) * mm.Px, Color.Lerp(Kit.Fire1, mm.Hue.Edge, 0.4f));
                }
                if (!warned && k > 0.4f)
                {
                    // a ring closing on the ground where it will land
                    warned = true;
                    float r = Ruin.Radius(mm.Tier, mm.Scale, CraterBase(mm.Level)) * 0.6f;
                    Fx.Ring(mm.Pose.Ground, mm.Px, 0.2f, r / mm.Px, 3f, 1, 2, FxLayer.Ground, 6, Art.Ground, 1.4f, Fx.WithA(mm.Hue.Edge, 0f), Fx.WithA(mm.Hue.Edge, 0.8f), Fx.WithA(mm.Hue.Core, 0.95f));
                }
            };
            fall.Exit = SlamDown;
            m.Add(fall);
            // the bridge held in the crater
            m.Add(new Beat { Tempo = Tempo.Impact, Dur = 0.3f, To = new Shape(-150f, 1.06f, 0.92f, 0f), Ease = Ease.OutQuad, Track = false });
            m.Do(mm => mm.EndCinematic());
            // the kip-up
            Beat kip = new Beat { Tempo = Tempo.Act, Dur = 0.2f, To = new Shape(0f, 0.95f, 1.08f, 0f), Ease = Ease.OutBack, Track = false };
            kip.Every = (mm, k) => mm.Pose.Height = 0.3f * mm.Kk * Mathf.Sin(Mathf.PI * k);
            kip.Exit = mm =>
            {
                if (mm.Vis)
                {
                    Fx.LandingPuff(mm.Pose.Ground, mm.Px, mm.A.current_tile);
                }
            };
            m.Add(kip);
            m.Recover(0.3f);
            return m.Start();
        }

        /// <summary>It sinks with the body in its arms: the ground cracks under its feet, dust drawn in.</summary>
        private static void Gather(ChoreoMove m)
        {
            WorldTile tile = m.A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.1f);
            Choreo.Sound("event:/SFX/POWERS/Powerup", tile, 0.3f);
            Ruin.Quake(m.A, m.Pose.Ground, Scale.Heavy, 0.8f, m.Kk);
            if (!m.Vis)
            {
                return;
            }
            Kit.Crater(m.Pose.Ground, m.Px * m.Kk, tile, true, 5f, true, m.Hue.Edge);
            Kit.DustRing(m.Pose.Ground, m.Px, tile, 10, -20f, 0.45f, m.Kk);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                Vector2 end = m.Pose.Ground + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * Art.Ground) * Random.Range(1f, 1.8f) * m.Kk;
                EarthKit.Crack(m.Pose.Ground, end, m.Px, 1.5f, m.Hue.Edge);
            }
        }

        /// <summary>The take-off: a crater where it stood, a column of dust, a ring racing out.</summary>
        private static void Launch(ChoreoMove m)
        {
            WorldTile tile = m.A.current_tile;
            Vector2 g = m.Pose.Ground;
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Ruin.Blast(m.A, g, 1f, Scale.Heavy, null, false);
            Ruin.Quake(m.A, g, Scale.Heavy, 1.2f, m.Kk);
            if (!m.Vis)
            {
                return;
            }
            Kit.Flash(g + new Vector2(0f, 1f * m.Px), m.Px, Mathf.RoundToInt(6f * m.Kk), 0.08f, Fx.WithA(m.Hue.Core, 0.95f));
            Kit.DustRing(g, m.Px, tile, 18, 60f, 0.65f, m.Kk);
            Kit.Shockwave(g, m.Px, 4f, 30f * m.Kk, 0.4f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
            Kit.Debris(g, m.Px, tile, 8, m.Kk);
            for (int i = 0; i < 4; i++)
            {
                Kit.SpeedLines(g + new Vector2(Random.Range(-3f, 3f), 2f + i * 6f) * m.Px, Vector2.up, m.Px, 2, 3f, 0.2f, Fx.WithA(Color.white, 0.8f), m.TopLayer);
            }
        }

        private static readonly List<Actor> _shock = new List<Actor>();

        /// <summary>The foe is driven head first into the earth: the slam, the crater, the fissures, the shockwave.</summary>
        private static void SlamDown(ChoreoMove m)
        {
            m.Pose.Height = 0f;
            if (m.Held == null)
            {
                return;
            }
            int level = m.Level;
            Victim b = m.Held;
            Actor v = b.A;
            Vector2 at = b.P.Pose.Ground;
            HitResult r = Combat.Hit(m.A, v, SlamMult(level) * m.Cm, true, false, 0f, m);
            m.LastHit = r;
            bool owed = MeteorSlam.AtDeathsDoor(v);
            m.Held = null;
            Impact im = Impact.Of(Hurt.DriveIn, m.Dir, 0f, 2f);
            im.Owed = owed;
            im.Daze = 2f;
            im.Stop = 0.1f * m.ImpactK;
            b.LetGo(im, m.A, m);
            Victims.Flash(v, Color.white, 0.1f);
            m.SetLink(Link.Down, v);
            float px = m.Px, k = m.Kk;
            float rad = Ruin.Radius(m.Tier, m.Scale, CraterBase(level));
            WorldTile tile = Combat.TileAt(at);
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionBig", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile);
            Choreo.Sound("event:/SFX/NATURE/EarthQuake", tile, 0.3f);
            if (m.Vis)
            {
                Vector2 hit = at + new Vector2(0f, 1.2f * px);
                Kit.ImpactFrame(hit, px, (0.8f + 0.15f * level) * k, m.Hue.Pal, r.Crit || level >= 3, m.Cinematic ? FxLayer.Cine : FxLayer.Behind);
                ChoreoFx.Impact(hit, Vector2.down, px, k, 2.2f, m.Hue, m.TopLayer, true);
                Kit.Flash(hit, px, Mathf.RoundToInt(12f * k), 0.12f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
                Kit.DustRing(at, px, tile, 30, 95f, 0.85f, k * 1.4f);
                Kit.Debris(at, px, tile, 20, k * 1.3f);
                EarthKit.Boulders(at, px, 8, k * 1.2f, EarthKit.Stone(at), 1.3f);
                Kit.Crater(at, px * k * 1.6f, tile, true, 12f, false, m.Hue.Edge);
                Kit.Shockwave(at, px, 6f, rad / px, 0.55f, Fx.WithA(m.Hue.Core, 0.95f), 4);
                Kit.Shockwave(at, px, 4f, rad / px * 0.7f, 0.4f, Fx.WithA(m.Hue.Edge, 0.9f), 2);
            }
            Kit.Stop(at, 0.12f * m.ImpactK);
            Ruin.Blast(m.A, at, CraterBase(level), m.Scale, m.Pose.Ground);
            Ruin.Quake(m.A, at, m.Scale, 1.6f, k);
            EarthKit.Fissures(m.A, at, px, rad * 0.95f, EarthKit.L(level, 10, 12, 14), m.Hue.Edge, m.Scale);
            m.HoldFor(0.08f * m.ImpactK);
            // the shockwave throws down every enemy in the crater
            Areas.Add(new ShockwaveHazard(m, at, rad, 22f, ShockMult(level), Hurt.Knockdown, 1.6f));
            if (Dev.On) Dev.Trace("world_suplex " + Techniques.Roman(level) + " slams " + v.asset.id + " hp " + v.getHealth() + ", crater " + rad.ToString("F1"));
        }
    }
}
