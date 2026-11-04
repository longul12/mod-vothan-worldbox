using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// SKY LADDER (Tempest Leg, Expert). The air chain after a launch (it takes Launched: a flying knee, a
    /// crescent moon, an uppercut, a stone pillar, a blast that threw the foe): it crouches and leaps after
    /// the airborne foe (the foe hangs, its flight dying out: anime hang time), and kicks it higher with a
    /// rising kick, then steps up the air after it on a ring of wind and kicks it higher again, climbing
    /// each kick like the rung of a ladder (I three, II four, III five rungs); at the top it flips over the
    /// foe and drives its heel down on it: the foe is spiked head first into the earth (a crater, Down),
    /// and it drops after it and lands beside the hole. It catches any body flying within reach that it
    /// can hold: one of the engine's launched or juggled bodies, or one an older technique threw on the
    /// game's physics; never a flyer, a giant, a body someone else holds, or one over the water.
    /// II: higher, a ring of dust and rock at the crash; III: the top kick and the spike on impact
    /// frames, a glowing crater whose shock knocks down everyone within 1.4 tiles.
    /// Damage (x the user's damage): rungs 3 x 0.35 / 4 x 0.4 / 5 x 0.45, the spike 1.0 / 1.4 / 1.9.
    /// </summary>
    internal sealed class SkyLadderTech : Technique
    {
        public override string Id => "sky_ladder";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Air;
        public override School School => School.TempestLeg;
        public override Form Forms => Form.Kick | Form.Aerial | Form.Flurry;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 32f, 52f };
        public override float[] Cooldown => new[] { 9f, 10f, 12f };

        public override bool Accepts(Link link)
        {
            return link == Link.Launched;
        }

        public static int Rungs(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 4 : 5;
        }

        public static float Rung(int level)
        {
            return level <= 1 ? 0.35f : level == 2 ? 0.4f : 0.45f;
        }

        public static float Spike(int level)
        {
            return level <= 1 ? 1f : level == 2 ? 1.4f : 1.9f;
        }

        public static float Total(int level)
        {
            return Rungs(level) * Rung(level) + Spike(level);
        }

        /// <summary>How high a body may already be for it to go after it (tiles, x its size).</summary>
        public static float MaxHeight(int level)
        {
            return level <= 1 ? 2.8f : level == 2 ? 3.3f : 3.8f;
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            bool linked = c.Linked && c.Link == Link.Launched && c.LinkTarget != null;
            Actor best = null;
            float bestScore = 0f;
            if (c.Forced)
            {
                Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                Consider(c, v, level, k, false, ref best, ref bestScore);
            }
            else if (linked)
            {
                Consider(c, c.LinkTarget, level, k, true, ref best, ref bestScore);
            }
            if (best == null && !c.Forced)
            {
                for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= 4.5f * k + 1f; i++)
                {
                    Consider(c, c.Enemies[i], level, k, linked && c.Enemies[i] == c.LinkTarget, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(1f, bestScore) : bestScore,
                Data = c.Forced
            };
        }

        private static void Consider(Ctx c, Actor v, int level, float k, bool link, ref Actor best, ref float bestScore)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced) || !TempestKicks.CanCatch(v, c.Self))
            {
                return;
            }
            float h = Combat.HeightOf(v);
            // in the air: thrown high enough to be worth the leap (the body our own blow has just sent up a little less)
            bool air = h > 0.45f || link && h > 0.25f;
            if (!air && !c.Forced)
            {
                return;
            }
            if (h > MaxHeight(level) * k)
            {
                return;
            }
            Vector2 g = Victims.GroundOf(v);
            float d = Vector2.Distance(c.Pos, g);
            if (d > (link ? 5f : 4.2f) * k + v.stats["size"] * 0.5f + (c.Forced ? 3f : 0f))
            {
                return;
            }
            if (!Combat.SafeSpot(g) && !Choreo.Wading(g) || d > 1.2f && !Combat.ClearPath(c.Pos, g))
            {
                return;   // it would come down in the water, or a wall stands between
            }
            if (!c.Forced && !link && v.getHealth() <= c.Expected(v, 1.2f) * cm)
            {
                return;
            }
            float score = c.Value(v, c.Expected(v, Total(level)), cm) + (link ? 15f : 8f);
            if (Tiers.Of(v) != Tier.None)
            {
                score += 8f * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            return SkyLadderMove.Begin(a, p, this);
        }
    }

    /// <summary>
    /// The sky ladder's choreography: the catch (and on a body standing on the ground, a rising knee that
    /// pops it up first), the leap, the rungs (a rising kick, the body flying up a rung, a step up the
    /// air after it), the flip over the top, the heel, the spike and the drop.
    /// </summary>
    internal sealed class SkyLadderMove : LegMove
    {
        private const Form Shows = Form.Kick | Form.Aerial | Form.Flurry;

        /// <summary>The kicked body's height (its feet) and ground, as the script sends it.</summary>
        private float _bh;
        private Vector2 _bg;
        private float _brot;
        private int _rung;

        private SkyLadderMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static bool Begin(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || p.Target == null || !Moves.CanPerform(a))
            {
                return false;
            }
            SkyLadderMove m = new SkyLadderMove(a, t, p);
            m.Build(p.Target);
            return m.Start();
        }

        /// <summary>How far in front of the body it kicks from (tiles).</summary>
        private float Gap => 0.42f * Kk;

        /// <summary>How far below the body's feet its own feet are as it kicks (its leg reaches the body's middle).</summary>
        private float Under => Mathf.Clamp(0.85f * Hpx * A.actor_scale - 0.4f * VHeight, 0.15f, 1.6f);

        private float Rise => 0.5f * Kk;

        private void Build(Actor v)
        {
            int level = Level;
            int rungs = SkyLadderTech.Rungs(level);
            if (Combat.HeightOf(v) < 0.3f)
            {
                // standing on the ground (a god's order, a dev demo): a rising knee pops it up first
                StepIn();
                Strike(new Hit(Blow.Knee, 0.3f) { Hurt = Hurt.Juggle, Lift = 1.1f, Push = 0.25f, Lethal = false, Shows = Shows | Form.Rising });
            }
            Do(mm => ((SkyLadderMove)mm).Catch());
            // the crouch: the body hangs, its flight dying out
            Crouch(0.07f, 0.85f).Shows(Shows).OnEnter(mm => ((SkyLadderMove)mm).Hang(0.08f, 25f));
            // the leap after it, to just below and in front of it
            Beat leap = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.16f,
                PerTile = 0.04f,
                Route = Route.Arc,
                PathEase = Ease.OutQuad,
                Dest = mm => ((SkyLadderMove)mm).Spot(),
                Apex = 0.15f,
                To = new Shape(-8f, 0.9f, 1.12f, 0.5f),
                Ease = Ease.OutQuad,
                Ghosts = 0.03f,
                Track = false,
                Forms = Shows
            };
            leap.Enter = mm =>
            {
                SkyLadderMove s = (SkyLadderMove)mm;
                if (!s.HasBody)
                {
                    s.Lose();
                    return;
                }
                s.Hang(0.06f, 20f);
                s.Current.To = new Shape(-8f, 0.9f, 1.12f, Mathf.Max(0.2f, s._bh - s.Under) / s.Kk);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", s.A.current_tile);
                if (s.Vis)
                {
                    Kit.DustRing(s.Stance, s.Px, s.A.current_tile, 8, 36f, 0.45f, s.Kk);
                    Kit.SpeedLines(s.BodyPos(), Vector2.up, s.Px, 3, 3f, 0.14f, Fx.WithA(s.Hue.Edge, 0.75f), s.TopLayer);
                }
            };
            Add(leap);
            for (int i = 0; i < rungs; i++)
            {
                bool top = i == rungs - 1;
                if (i > 0)
                {
                    // a step up the air after it on a ring of wind, as the kicked body flies up a rung
                    Beat step = new Beat
                    {
                        Tempo = Tempo.Travel,
                        Dur = 0.1f,
                        Route = Route.Line,
                        PathEase = Ease.OutQuad,
                        Dest = mm => ((SkyLadderMove)mm).Spot(),
                        To = new Shape(-4f, 0.92f, 1.1f, 0.5f),
                        Ease = Ease.OutQuad,
                        Ghosts = 0.04f,
                        Track = false,
                        Forms = Shows
                    };
                    step.Enter = mm => ((SkyLadderMove)mm).StepUp();
                    Add(step);
                }
                Hit kick = new Hit(Blow.SnapKick, SkyLadderTech.Rung(level))
                {
                    Hurt = Hurt.None,
                    Lethal = false,
                    Power = top ? 1.15f : 0.85f,
                    Finisher = top && level >= 3,
                    Shows = Shows
                };
                // the rising kick: leaning back, the foot sweeping up in front into the body's middle
                Beat act = new Beat
                {
                    Tempo = Tempo.Act,
                    Dur = 0.09f,
                    To = new Shape(-26f, 1.06f, 1.12f, float.NaN, 0.05f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Forms = Shows,
                    Tag = kick
                };
                act.Enter = mm =>
                {
                    SkyLadderMove s = (SkyLadderMove)mm;
                    if (!s.HasBody)
                    {
                        s.Lose();
                        return;
                    }
                    TempestKicks.Arc(s, 8, 1f, -80f, 70f, 2, new Vector2(2f, -1f), s.BeatDur * 1.8f, 7);
                    Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", s.A.current_tile, 0.05f);
                };
                act.On(0.55f, mm => ((SkyLadderMove)mm).Kick(kick));
                Add(act);
            }
            // over the top: a forward flip up and over the body, the heel raised
            Beat over = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.17f,
                Route = Route.Arc,
                PathEase = Ease.InOutSine,
                Dest = mm => ((SkyLadderMove)mm).Past(),
                Apex = 0.3f,
                Spin = 360f,
                To = new Shape(-20f, 0.92f, 1.12f, 1f),
                Ease = Ease.OutQuad,
                Ghosts = 0.025f,
                Track = false,
                Forms = Shows | Form.Overhead,
                If = mm => ((SkyLadderMove)mm).HasBody
            };
            over.Enter = mm =>
            {
                SkyLadderMove s = (SkyLadderMove)mm;
                s.Hang(0.12f, 30f);
                s.Current.To = new Shape(-20f, 0.92f, 1.12f, (s._bh + 0.55f * s.VHeight) / s.Kk);
                TempestKicks.Step(s, 1.2f);
            };
            Add(over);
            Hit spike = new Hit(Blow.HeelDrop, SkyLadderTech.Spike(level))
            {
                Hurt = Hurt.None,
                Lethal = false,
                Power = level >= 3 ? 1.5f : 1.3f,
                Finisher = level >= 2,
                Shows = Shows | Form.Overhead
            };
            // the heel: hammered down on the body
            Beat heel = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.09f,
                To = new Shape(38f, 1.1f, 0.88f, float.NaN, 0.04f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Shows | Form.Overhead,
                Tag = spike,
                If = mm => ((SkyLadderMove)mm).HasBody
            };
            heel.Enter = mm =>
            {
                SkyLadderMove s = (SkyLadderMove)mm;
                s.Face(s.BodyGround - s.Stance);
                TempestKicks.Arc(s, 11, 1f, 100f, -80f, 3, new Vector2(2f, 1f), s.BeatDur * 1.8f, 7);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", s.A.current_tile, 0.05f);
            };
            heel.On(0.55f, mm => ((SkyLadderMove)mm).Heel(spike));
            Add(heel);
            // the body is driven down head first; it drops after it
            Beat fall = new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.13f,
                To = new Shape(14f, 0.88f, 1.16f, 0.6f),
                Ease = Ease.InQuad,
                Ghosts = 0.025f,
                Track = false,
                Forms = Shows | Form.Overhead,
                If = mm => ((SkyLadderMove)mm).HasBody
            };
            fall.Enter = mm => ((SkyLadderMove)mm).Plunge();
            fall.Every = (mm, k) =>
            {
                SkyLadderMove s = (SkyLadderMove)mm;
                if (s.Vis && s.HasBody && k < 0.9f)
                {
                    Kit.SpeedLines(s.Body.P.BodyPos() + new Vector2(0f, 3f * s.Px), Vector2.down, s.Px, 1, 4f, 0.1f, Fx.WithA(s.Hue.Edge, 0.85f), s.TopLayer);
                }
            };
            fall.Exit = mm => ((SkyLadderMove)mm).Crash();
            Add(fall);
            Drop(0.12f, 0f, 8f);
            Land();
            Recover(0.22f);
        }

        // ------------------------------------------------------------- places --

        /// <summary>Where it kicks from: in front of the body, below it (safe ground under it, or where it is).</summary>
        private Vector2 Spot()
        {
            Vector2 g = HasBody ? BodyGround : VG;
            Vector2 d = g - Stance;
            Vector2 back = d.sqrMagnitude > 1e-4f ? -d.normalized : -Dir;
            Vector2 q = Combat.Clamp(g + back * Gap);
            return Combat.SafeSpot(q) || Choreo.Wading(q) ? q : Stance;
        }

        /// <summary>Just past the body (it flips over the top of it).</summary>
        private Vector2 Past()
        {
            Vector2 g = HasBody ? BodyGround : VG;
            Vector2 q = Combat.Clamp(g + Dir * 0.12f * Kk);
            return Combat.SafeSpot(q) || Choreo.Wading(q) ? q : g;
        }

        // ------------------------------------------------------------- the body --

        /// <summary>Takes hold of the airborne body (it hangs from now on: the script moves it).</summary>
        private void Catch()
        {
            if (!VOk || !TakeBody(V))
            {
                if (Dev.On) Dev.Trace("sky ladder: nothing to catch");
                Lose();
                return;
            }
            Struggle = 2.5f;
            _bg = BodyGround;
            _bh = BodyHeight;
            _brot = BodyRot;
            _rung = 0;
            Face(_bg - Stance);
            Combat.Pin(V, 4f);
        }

        /// <summary>The body drifts up a little and turns (its flight dying out), at least to the first rung's height.</summary>
        private void Hang(float up, float turn)
        {
            if (!HasBody)
            {
                return;
            }
            float first = Mathf.Max(0.95f * Kk, Under + 0.45f * Kk);
            _bh = Mathf.Min(Mathf.Max(_bh + up * Kk, first), SkyLadderTech.MaxHeight(Level) * Kk + 0.5f);
            _brot += turn * (Fwd > 0f ? -1f : 1f);
            MoveBody(_bg, _bh, _brot, Ease.OutQuad);
        }

        /// <summary>A rung: the rising kick lands in the body's middle.</summary>
        private void Kick(Hit h)
        {
            if (!HasBody)
            {
                Lose();
                return;
            }
            Actor v = Body.A;
            if (!Touch(v))
            {
                return;
            }
            Vector2 at = Choreo.BodyAt(v, 0.4f);
            Deliver(v, h, ShapeOf(Blow.SnapKick), at, (Dir + Vector2.up).normalized);
            _rung++;
            TempestKicks.Burst(this, at, Vector2.up, h.Power >= 1.1f ? 1.3f : 0.8f);
            if (Vis)
            {
                Fx.HitSpark(at, Px, h.Power >= 1.1f ? 1 : 0, Vector2.up, 2);
            }
            // the body is kicked up a rung (it flies up during the step that follows), turning over
            _bh = Mathf.Min(_bh + Rise, (SkyLadderTech.MaxHeight(Level) + 1.6f) * Kk);
            _bg = Combat.Clamp(_bg + Dir * 0.08f * Kk);
            _brot += 150f * (Fwd > 0f ? -1f : 1f);
        }

        /// <summary>The step after the kicked body: a ring of wind under the feet, the body flies up its rung.</summary>
        private void StepUp()
        {
            if (!HasBody)
            {
                Lose();
                return;
            }
            MoveBody(_bg, _bh, _brot, Ease.OutCubic);
            Current.To = new Shape(-4f, 0.92f, 1.1f, Mathf.Max(0.2f, _bh - Under) / Kk);
            TempestKicks.Step(this);
        }

        /// <summary>The heel lands on the body at the top of the ladder.</summary>
        private void Heel(Hit h)
        {
            if (!HasBody)
            {
                Lose();
                return;
            }
            Actor v = Body.A;
            Vector2 at = Choreo.BodyAt(v, 0.6f);
            Deliver(v, h, ShapeOf(Blow.HeelDrop), at, Vector2.down);
            TempestKicks.Burst(this, at, Vector2.down, Level >= 3 ? 1.8f : 1.3f);
        }

        /// <summary>The body is driven down head first (it falls faster and faster).</summary>
        private void Plunge()
        {
            if (!HasBody)
            {
                return;
            }
            Vector2 g = _bg;
            if (!Combat.SafeSpot(g) && !Choreo.Wading(g) && Combat.FindLanding(g, 2f, out Vector2 q))
            {
                g = q;
            }
            _bg = g;
            Struggle = 0f;
            // (upside down: it lands on its head)
            float flip = Mathf.Round((_brot - 180f) / 360f) * 360f + 180f;
            MoveBody(g, 0f, flip, Ease.InQuad);
        }

        /// <summary>The body hits the ground: a crater, the foe crushed into it (Down).</summary>
        private void Crash()
        {
            if (!HasBody)
            {
                return;
            }
            Actor v = Body.A;
            Vector2 at = BodyGround;
            WorldTile tile = Combat.TileAt(at);
            int level = Level;
            Impact im = Impact.Of(Hurt.DriveIn, Dir, 0f, 1.6f);
            im.Owed = MeteorSlam.AtDeathsDoor(v);
            im.Daze = level <= 1 ? 1.1f : level == 2 ? 1.3f : 1.6f;
            im.Stop = 0.05f * ImpactK;
            im.Crater = level <= 1 ? 0.6f : level == 2 ? 0.9f : 1.2f;
            if (level >= 3)
            {
                im.AreaR = 1.4f * Mathf.Lerp(1f, Kk, 0.5f);
                im.AreaMult = SkyLadderTech.Spike(level) * 0.35f;
            }
            ReleaseBody(im);
            Victims.Flash(v, Color.white, 0.07f);
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            if (Vis)
            {
                Vector2 hit = at + new Vector2(0f, 1.5f * Px);
                ChoreoFx.Impact(hit, Vector2.down, Px, Kk, 1.4f + 0.2f * level, Hue, TopLayer, level >= 2);
                Kit.DustRing(at, Px, tile, 10 + 3 * level, 40f + 5f * level, 0.55f, Kk);
                if (level >= 2)
                {
                    Kit.Debris(at, Px, tile, 4 + 3 * level, Kk);
                    Kit.Shockwave(at, Px, 4f, (1f + 0.45f * level) / Px, 0.32f, Fx.WithA(Hue.Edge, 0.9f), 2);
                }
                if (level >= 3)
                {
                    Kit.ImpactFrame(hit, Px, 0.34f * Kk, Hue.Pal, LastHit.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
                }
            }
            SetLink(Link.Down, v);
            if (Dev.On) Dev.Trace("sky ladder: " + _rung + " rungs, spiked " + v.asset.id + "#" + v.Sid() + " hp " + v.getHealth());
        }
    }

    /// <summary>
    /// Dev staging for the Tempest Leg kicks that need a partner to show (an answer, a broken charge, an
    /// air chain), held still on a clean meadow and cued:
    ///   tl crescent [tier] [attack] [zoom]   A leaps at B (flying_knee, or 'attack'); B answers with Crescent Moon
    ///   tl break [tierA] [tierB] [charge] [zoom]
    ///                                        A winds up (dragon_wave, or 'charge') 5 tiles off; B's Snap Kick breaks it
    ///   tl ladder [level] [opener] [zoom]    A launches B ('opener': knee = Flying Knee, moon = Crescent Moon,
    ///                                        free = thrown on the game's physics, ground = none) and chains Sky Ladder
    ///   tl war [n] [m] [tier] [foe] [foeTier] [zoom]
    ///                                        n Tempest Leg humans of 'tier' (mixed: novice to master) against m 'foe's,
    ///                                        the AI in charge (count the kicks in the log: "tech snap_kick ...")
    /// Then "capture ...". It logs "tl done" after 5 s.
    /// </summary>
    internal static class TempestStage
    {
        private static Actor _a, _b;
        private static string _kind, _arg;
        private static int _level;
        private static float _t = -1f;
        private static bool _went, _second;
        private static int _logged;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["tl"] = Command;
            Hooks.FrameTick += dt =>
            {
                if (_t < 0f)
                {
                    return;
                }
                try
                {
                    Tick();
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("tl stage", e);
                    End();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            // (a clean meadow: the last staging's survivors would join in)
            List<Actor> old = Dev.Units;
            for (int i = 0; i < old.Count; i++)
            {
                Actor u = old[i];
                if (u != null && !u.isRekt() && u.isAlive())
                {
                    u.die(true, AttackType.Other, false, false);
                }
            }
            old.Clear();
            _kind = Dev.Arg(args, 1, "crescent");
            if (_kind == "war")
            {
                War(args);
                return;
            }
            Tier ta, tb;
            float zoom;
            float gap;
            switch (_kind)
            {
                case "break":
                    ta = Dev.ParseTier(Dev.Arg(args, 2, "expert"));
                    tb = Dev.ParseTier(Dev.Arg(args, 3, "expert"));   // (class limits: no lower than the charger)
                    _arg = Dev.Arg(args, 4, "dragon_wave");
                    zoom = Dev.ArgF(args, 5, 12f);
                    gap = 5f;
                    break;
                case "ladder":
                    _level = Mathf.Clamp((int)Dev.ArgF(args, 2, 1f), 1, 3);
                    ta = (Tier)Mathf.Clamp((int)Tier.Expert + _level - 1, 1, 6);
                    tb = Tier.None;
                    _arg = Dev.Arg(args, 3, "knee");
                    zoom = Dev.ArgF(args, 4, 12f);
                    gap = _arg == "knee" ? 4f : 1.5f;
                    break;
                default:
                    tb = Dev.ParseTier(Dev.Arg(args, 2, "expert"));
                    ta = tb;
                    _arg = Dev.Arg(args, 3, "flying_knee");
                    zoom = Dev.ArgF(args, 4, 12f);
                    gap = 3.5f;
                    break;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 8);
            if (c == null)
            {
                Dev.Log("tl: no ground");
                return;
            }
            _a = Dev.Spawn("human", World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _b = Dev.Spawn(_kind == "ladder" ? "skeleton" : "orc", World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("tl: could not spawn");
                return;
            }
            if (ta != Tier.None)
            {
                Progression.Promote(_a, ta, false);
            }
            if (tb != Tier.None)
            {
                Progression.Promote(_b, tb, false);
            }
            ActorTrait school = Schools.Trait(School.TempestLeg);
            if (school != null)
            {
                _a.addTrait(school, true);
                _b.addTrait(school, true);
            }
            _a.data.favorite = false;
            _b.data.favorite = false;
            _staged.Add(_a.Sid());
            _staged.Add(_b.Sid());
            Director.Claim(_scene, 8f, _a, _b);
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1.5f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1.5f));
            _t = 0f;
            _went = _second = false;
            Dev.Log("tl " + _kind + ": " + Tiers.Of(_a) + " #" + _a.Sid() + " vs " + Tiers.Of(_b) + " " + _b.asset.id + " #" + _b.Sid() + " (" + _arg + ")");
        }

        /// <summary>A Tempest Leg band against a crowd, the AI in charge.</summary>
        private static void War(string[] args)
        {
            int n = Mathf.Clamp((int)Dev.ArgF(args, 2, 5f), 1, 30);
            int m = Mathf.Clamp((int)Dev.ArgF(args, 3, 10f), 1, 60);
            string tier = Dev.Arg(args, 4, "mixed");
            string foe = Dev.Arg(args, 5, "orc");
            Tier ft = Dev.ParseTier(Dev.Arg(args, 6, "none"));
            float zoom = Dev.ArgF(args, 7, 20f);
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 12);
            if (c == null)
            {
                Dev.Log("tl: no ground");
                return;
            }
            ActorTrait school = Schools.Trait(School.TempestLeg);
            Tier[] mix = { Tier.Novice, Tier.Adept, Tier.Expert, Tier.Expert, Tier.Master, Tier.Adept, Tier.Grandmaster };
            for (int i = 0; i < n; i++)
            {
                Actor u = Dev.Spawn("human", World.world.GetTile(c.x - 5 - i % 3, c.y - 3 + i / 3 * 2) ?? c, false);
                if (u == null)
                {
                    continue;
                }
                Tier t = tier == "mixed" ? mix[i % mix.Length] : Dev.ParseTier(tier);
                Progression.Promote(u, t == Tier.None ? Tier.Expert : t, false);
                if (school != null)
                {
                    u.addTrait(school, true);
                }
                u.data.favorite = false;
            }
            for (int i = 0; i < m; i++)
            {
                Actor u = Dev.Spawn(foe, World.world.GetTile(c.x + 4 + i % 4, c.y - 4 + i / 4 * 2) ?? c, false);
                if (u != null && ft != Tier.None)
                {
                    Progression.Promote(u, ft, false);
                    u.data.favorite = false;
                }
            }
            World.world.move_camera.focusOn(new Vector3(c.x, c.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(new Vector2(c.x, c.y + 1f));
            Dev.Log("tl war: " + n + " Tempest Leg humans (" + tier + ") vs " + m + " " + foe + " (" + ft + ")");
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > 1.5f)
                {
                    Dev.Log("tl done (one fell)");
                    End();
                }
                return;
            }
            if (!_went && _t >= 1.2f)
            {
                _went = true;
                Go();
                return;
            }
            if (_went && !_second)
            {
                Second();
            }
            if (_went && _t >= 6.2f)
            {
                Dev.Log("tl done");
                End();
            }
        }

        private static void Go()
        {
            Fighters.Get(_a).ResetCooldowns();
            Fighters.Get(_b).ResetCooldowns();
            _a.setMana(_a.getMaxMana());
            _b.setMana(_b.getMaxMana());
            Moves.ReleaseHeld();
            switch (_kind)
            {
                case "break":
                    {
                        Moves.Hold(_b);
                        Technique t = Techniques.Get(_arg);
                        bool ok = t != null && Brain.Force(_a, t, Mathf.Max(1, Techniques.Level(_a, t)), _b);
                        Dev.Log("tl break: " + _arg + " by " + Tiers.Of(_a) + ": " + ok);
                        break;
                    }
                case "ladder":
                    {
                        bool ok;
                        if (_arg != "free")
                        {
                            Moves.Hold(_b);   // (a held unit skips its updates: one thrown on the game's physics must not be)
                        }
                        if (_arg == "free")
                        {
                            Combat.LaunchDir(_b, Vector2.right, 1.5f, 14f, 600f);
                            ok = true;
                        }
                        else if (_arg == "ground")
                        {
                            _second = true;
                            ok = Brain.Force(_a, Techniques.Get("sky_ladder"), _level, _b);
                        }
                        else
                        {
                            Technique t = Techniques.Get(_arg == "moon" ? "crescent_moon_kick" : "flying_knee");
                            ok = Brain.Force(_a, t, _arg == "moon" ? _level : _level + 1, _b);
                        }
                        Dev.Log("tl ladder opener " + _arg + ": " + ok);
                        break;
                    }
                default:
                    {
                        Reactions.DevForce[_b.Sid()] = "crescent_moon_kick";
                        Moves.Hold(_b);
                        Technique t = Techniques.Get(_arg);
                        bool ok = t != null && Brain.Force(_a, t, Mathf.Clamp((int)Tiers.Of(_a) - (int)t.Entry + 1, 1, 3), _b);
                        Dev.Log("tl crescent: " + _arg + " at the defender: " + ok);
                        break;
                    }
            }
        }

        /// <summary>The second actor's cue: the snap kick once the charge is on, the ladder once the body is up.</summary>
        private static void Second()
        {
            if (_kind == "break")
            {
                if (Moves.Charging(_a))
                {
                    _second = true;
                    Technique t = Techniques.Get("snap_kick");
                    bool ok = Brain.Force(_b, t, Mathf.Max(1, Techniques.Level(_b, t)), _a);
                    Dev.Log("tl break: the charge is on; the snap kick: " + ok);
                }
                else if (_t > 3f)
                {
                    _second = true;
                }
                return;
            }
            if (_kind != "ladder")
            {
                _second = true;
                return;
            }
            // the ladder: as soon as the opener let go and the body is in the air
            if (_arg == "free" && _t < 3f && (int)(_t * 10f) != _logged)
            {
                _logged = (int)(_t * 10f);
                Dev.Log("tl ladder: body h " + Combat.HeightOf(_b).ToString("F2") + " forces " + _b.under_forces + " busy " + Moves.Busy(_b)
                    + "; performer free " + Moves.CanPerform(_a) + " busy " + (Moves.TryGet(_a, out Move am) ? am.GetType().Name : "-"));
            }
            if (Moves.CanPerform(_a) && TempestKicks.Up(_b, _arg == "free" ? 1f : 0.3f))
            {
                _second = true;
                bool ok = Brain.Force(_a, Techniques.Get("sky_ladder"), _level, _b);
                Dev.Log("tl ladder: the body is up (h " + Combat.HeightOf(_b).ToString("F2") + "); sky ladder: " + ok);
            }
            else if (_t > 4f)
            {
                _second = true;
                Dev.Log("tl ladder: the body never went up");
            }
        }

        private static void End()
        {
            if (_a != null)
            {
                Reactions.DevForce.Remove(_a.Sid());
            }
            if (_b != null)
            {
                Reactions.DevForce.Remove(_b.Sid());
            }
            _staged.Clear();
            Director.Release(_scene);
            _t = -1f;
            _a = _b = null;
            Moves.ReleaseHeld();
        }
    }
}
