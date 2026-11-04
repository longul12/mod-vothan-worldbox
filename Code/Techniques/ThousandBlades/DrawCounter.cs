using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// IAI COUNTER (Thousand Blades, Adept). The answer to whatever comes straight at it: a dash, a
    /// lunge, a straight punch. The instant the foe arrives the swordsman (its blade still sheathed: an
    /// unarmed one's ki blade only appears with the draw) sinks a hair, one draw - a step forward past
    /// the foe's line, the blade out and through in a single flash, a white line drawn across the frame -
    /// and the two are back to back. The foe's own run carries it on past, skidding. Not a hair moves.
    /// The blade slides home with a click, and only then the foe falls (the cut opens on its body: it
    /// crumples, Down).
    ///   I    the line, the click, the fall (x2)
    ///   II   a longer, brighter line, an impact frame as it falls (x2.5)
    ///   III  the line burns into the ground and stays as a scar; the fall comes on a black-edged
    ///        impact frame (x3.1, dazed longer)
    /// Reactive (Answers Dash | Punch | Straight): the attacker's blow cuts an afterimage (Evaded). It
    /// meets plain straight blows of the game too. Forced (dev, god power) it steps in and draws on the
    /// nearest foe.
    /// </summary>
    internal sealed class DrawCounterTech : Technique
    {
        public override string Id => "draw_counter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Counter | Form.Fast;
        public override Form Answers => Form.Dash | Form.Punch | Form.Straight;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 10f, 16f, 24f };
        public override float[] Cooldown => new[] { 7f, 6f, 5f };

        public static float Mult(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 2.5f : 3.1f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (Shots.Reaching != null || !Combat.Grounded(attacker))
            {
                return Contact.Hit;   // a draw meets a body that arrives, not a missile or a leap
            }
            if (Vector2.Distance(defender.current_position, attacker.current_position) > 3f + attacker.stats["size"] * 0.5f)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            return Iai(defender, attacker, level, null) ? Contact.Evaded : Contact.Hit;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (!Combat.Grounded(attacker) || Vector2.Distance(defender.current_position, attacker.current_position) > 2.2f + attacker.stats["size"] * 0.5f)
            {
                return false;
            }
            return Iai(defender, attacker, level, null);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (!c.Forced)
            {
                return null;   // an answer, never a plan
            }
            Actor v = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (v == null || c.Dist(v) > 3.5f || !Combat.Grounded(v))
            {
                return null;
            }
            Vector2 d = v.current_position - c.Pos;
            return new Plan { Target = v, Point = v.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1, Score = 1f, Data = true };
        }

        public override bool Begin(Actor a, Plan p)
        {
            return p.Target != null && Iai(a, p.Target, p.Level, p);
        }

        /// <summary>The iai: 'd' draws on 'atk' (an answer; or 'forced' a demonstration that steps in first).</summary>
        private bool Iai(Actor d, Actor atk, int level, Plan forced)
        {
            TblMove m = forced != null ? TblMove.Make(d, forced, this) : TblMove.Counter(d, atk, this, level);
            if (m == null)
            {
                return false;
            }
            int lv = m.Level;
            Form forms = Form.Blade | Form.Counter | Form.Fast;
            m.Drawn = false;
            bool landed = false;
            Vector2 run = -m.Aim;   // the way the foe was coming
            float lineAng = 0f;
            if (forced != null)
            {
                m.StepIn();
            }
            // SETTLE: a hair lower, the hand on the hilt, a glint at the hip
            m.Posture(0.04f, new Shape(10f, 1.1f, 0.86f, 0f, -0.04f), Tempo.Free, -150f, forms, true);
            m.OnEnter(mm =>
            {
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.2f);
                if (mm.Vis)
                {
                    Fx.Glint(mm.BodyPos() + new Vector2(mm.Fwd * 1.5f, -1.5f) * mm.Px, mm.Px, 0.12f, mm.Hue.Core, 0.7f);
                }
            });
            // DRAW: one step past the foe's line, the blade out and through in a single flash
            Beat draw = new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.07f,
                Route = Route.Line,
                PathEase = Ease.OutExpo,
                Dest = DrawTo,
                To = new Shape(6f, 1.3f, 0.82f, 0f, 0f),
                Ease = Ease.OutExpo,
                Intangible = true,
                Ghosts = 0.02f,
                Lines = 0.03f,
                NeedsVictim = true,
                Track = false,
                Forms = forms
            };
            draw.Enter = mm =>
            {
                TblMove t = (TblMove)mm;
                t.Drawn = true;
                if (mm.V != null && mm.V.Sid() == mm.VId && (mm.VG - mm.Stance).sqrMagnitude > 1e-4f)
                {
                    run = (mm.Stance - mm.VG).normalized;   // the foe comes on along this line
                }
                lineAng = TblFx.Mirror(-7f, mm.Fwd);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                if (mm.Vis)
                {
                    int r = Mathf.Clamp(Mathf.RoundToInt(13f * mm.Kk), 6, 50);
                    Kit.Swish(mm, Art.Arc(r, 0.3f, 160f, -20f, lv >= 3 ? 3 : 2), 0.12f, new Vector2(mm.Fwd * 3f, 1f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                }
            };
            draw.Every = (mm, k) => ((TblMove)mm).SwingBlade(Mathf.Lerp(160f, -12f, Beat.Apply(Ease.OutExpo, k)));
            draw.On(0.35f, mm =>
            {
                landed = Draw((TblMove)mm, lv, run, lineAng);
            });
            m.Add(draw);
            // STILLNESS: back to back; nothing moves; the line fades
            m.Posture(lv <= 1 ? 0.32f : lv == 2 ? 0.28f : 0.25f, new Shape(4f, 1.22f, 0.86f, 0f, 0f), Tempo.Follow, -14f, forms);
            m.Last.Track = false;
            // SHEATHE: the blade slides home with a click, and only then the foe falls
            Beat sheathe = new Beat { Tempo = Tempo.Free, Dur = 0.12f, To = new Shape(0f, 0.98f, 1.03f, 0f, 0f), Ease = Ease.OutQuad, Track = false, Forms = forms };
            sheathe.Enter = mm =>
            {
                TblMove t = (TblMove)mm;
                Vector2 hip = mm.BodyPos() + new Vector2(mm.Fwd * 1.5f, -1.5f) * mm.Px;
                t.Drawn = false;
                Choreo.Sound("event:/SFX/HIT/HitMetal", mm.A.current_tile, 0.1f);
                if (mm.Vis)
                {
                    TblFx.Ting(hip, mm.Px, mm.Hue, 0.9f);
                }
                if (landed)
                {
                    Fall(t, lv, run, lineAng);
                }
            };
            m.Add(sheathe);
            m.Recover(0.2f);
            return m.Start();
        }

        /// <summary>The draw's step: forward past the foe's line, a little to its side (they pass each other).</summary>
        private static Vector2 DrawTo(ChoreoMove m)
        {
            Vector2 want = Combat.Clamp(m.Stance + m.Dir * 0.55f * m.Kk + m.Side * 0.32f * m.Kk);
            if (Combat.SafeSpot(want) && Combat.ClearPath(m.Stance, want))
            {
                return want;
            }
            want = Combat.Clamp(m.Stance + m.Dir * 0.4f * m.Kk - m.Side * 0.3f * m.Kk);
            return Combat.SafeSpot(want) && Combat.ClearPath(m.Stance, want) ? want : m.Stance;
        }

        /// <summary>The draw passes through the foe: the line across the frame, and its run carries it on past.</summary>
        private static bool Draw(TblMove m, int lv, Vector2 run, float ang)
        {
            if (!m.VOk)
            {
                m.Lose();
                return false;
            }
            Actor v = m.V;
            float px = m.Px;
            Vector2 body = Choreo.BodyAt(v, 0.5f);
            if (m.Vis)
            {
                float r = ang * Mathf.Deg2Rad;
                Vector2 dl = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
                float len = (2.4f + 0.6f * lv) * m.Kk;
                Vector2 a = body - dl * len * 0.5f, b = body + dl * len * 0.5f;
                TblFx.Streak(a, b, px, 0.36f + 0.06f * lv, lv >= 2 ? 3 : 2, m.Hue, m.TopLayer);
                Kit.Flash(m.BladeTip, px, 3, 0.05f, Fx.WithA(Color.white, 0.95f), m.TopLayer, 22);
                Kit.SpeedLines(body, dl, px, 3, 2f, 0.12f, Fx.WithA(m.Hue.Edge, 0.85f), m.TopLayer);
            }
            if (!m.Touch(v))
            {
                return false;   // it read the draw: the answer to the answer
            }
            Choreo.Sound("event:/SFX/HIT/HitSwordSword", m.A.current_tile);
            Victims.Flash(v, Color.white, 0.05f);
            if (Combat.Living(v))
            {
                // its own run carries it on past, skidding, leaning back
                Impact im = Impact.Of(Hurt.Reel, run, (1.15f + 0.15f * lv) * m.Kk, 1.1f);
                im.Stop = 0.04f;
                Victims.Hurt(v, m.A, m, im);
            }
            m.SetLink(Link.Close, v);
            return true;
        }

        /// <summary>The click of the sheathe: the cut opens on the body and the foe crumples.</summary>
        private static void Fall(TblMove m, int lv, Vector2 run, float ang)
        {
            if (!m.VOk)
            {
                return;
            }
            Actor v = m.V;
            float px = m.Px;
            Hit h = new Hit(Blow.DrawCut, Mult(lv))
            {
                Hurt = Hurt.Crumple,
                Power = 1.1f + 0.15f * lv,
                Daze = 0.5f + 0.35f * lv,
                Finisher = lv >= 2,
                Shows = Form.Blade | Form.Counter | Form.Fast
            };
            Vector2 at = Choreo.BodyAt(v, 0.5f);
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.DrawCut), at, run);
            if (m.Vis)
            {
                TblFx.CutOn(v, 0.5f, ang, 6f + lv, px, 0f, 0.22f, m.Hue, m.TopLayer);
                Kit.Sparks(at, px, 4 + lv, 40f, 90f, m.Hue.Edge, m.TopLayer);
            }
            if (lv >= 3)
            {
                // the line burns into the ground under where they crossed
                Vector2 g = Victims.GroundOf(v);
                Vector2 a = m.Pose.Ground - run * 0.4f * m.Kk, b = g + run * 0.6f * m.Kk;
                TblFx.GroundCut(a, b, px, 1.4f, m.Hue);
                Ruin.Furrow(m.A, a, b, 0.5f, m.Scale, m.Pose.Ground);
            }
        }
    }

    /// <summary>
    /// Dev staging of the Thousand Blades answers (dev only): a defender that walks the school and knows
    /// 'answer' at 'level', an attacker made to throw 'attack' at it; the defender always reads and
    /// answers only with 'answer' (Reactions.DevForce).
    ///   tbstage answer [level] attack [attackLevel] [zoom] [raceD] [raceA]
    /// e.g. "tbstage draw_counter 2 lunge_thrust 1", "tbstage blade_catch 1 sword_wave 1",
    /// "tbstage slip_counter 1 swallow_return 1" (the swallow's second cut catches the slip). Then a
    /// capture (the attack comes 2.8 s after the command, once the promotions' flares are gone). It logs
    /// "tbstage done" after 8.5 s. "tbarm [performer|defender|attacker] [item]" puts a
    /// sword in a hand (the steel instead of the ki blade).
    /// </summary>
    internal static class TblStage
    {
        private static Actor _d, _a;
        private static string _answer;
        private static Technique _attack;
        private static int _attackLevel;
        private static float _t = -1f;
        private static bool _went;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["tbstage"] = Command;
            Dev.Commands["tbarm"] = Arm;
            Dev.Commands["tbschool"] = WalkSchool;
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
                catch (Exception e)
                {
                    SakugaMod.Report("tbstage", e);
                    End();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _answer = Dev.Arg(args, 1, "blade_parry");
            int level = Mathf.Clamp(Mathf.RoundToInt(Dev.ArgF(args, 2, 1f)), 1, 3);
            _attack = Techniques.Get(Dev.Arg(args, 3, "jab_cross"));
            _attackLevel = Mathf.Clamp(Mathf.RoundToInt(Dev.ArgF(args, 4, 1f)), 1, 3);
            float zoom = Dev.ArgF(args, 5, 11f);
            string rd = Dev.Arg(args, 6, "human"), ra = Dev.Arg(args, 7, "orc");
            Technique answer = Techniques.Get(_answer);
            if (answer == null || _attack == null)
            {
                Dev.Log("tbstage: unknown technique (" + _answer + " / " + Dev.Arg(args, 3, "jab_cross") + ")");
                return;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 9);
            if (c == null)
            {
                Dev.Log("tbstage: no ground");
                return;
            }
            float gap = _attack.Role == Role.Line ? 6f : _attack.Role == Role.Mobility ? 3.6f : 1.4f;
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _d = Dev.Spawn(rd, World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _d == null)
            {
                Dev.Log("tbstage: could not spawn");
                return;
            }
            Tier td = (Tier)Mathf.Clamp((int)answer.Entry + level - 1, 1, 6);
            Tier ta = (Tier)Mathf.Clamp(Mathf.Max((int)_attack.Entry + _attackLevel - 1, (int)td), 1, 6);   // (class limits: never below the defender)
            Progression.Promote(_d, td, false);
            Progression.Promote(_a, ta, false);
            if (answer.School != School.None && Schools.Trait(answer.School) != null)
            {
                _d.addTrait(Schools.Trait(answer.School), true);
            }
            if (_attack.School != School.None && Schools.Trait(_attack.School) != null)
            {
                _a.addTrait(Schools.Trait(_attack.School), true);
            }
            _a.data.favorite = false;
            _d.data.favorite = false;
            _staged.Add(_a.Sid());
            _staged.Add(_d.Sid());
            Director.Claim(_scene, 10f, _a, _d);
            Vector2 mid = (_a.current_position + _d.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _went = false;
            Dev.Log("tbstage: " + _answer + " " + Techniques.Roman(level) + " (" + td + " " + rd + " #" + _d.Sid() + ") against "
                + _attack.Id + " " + Techniques.Roman(_attackLevel) + " (" + ta + " " + ra + " #" + _a.Sid() + ")");
        }

        /// <summary>
        /// tbarm [who] [item]: puts a sword in a hand (who: performer (the latest move demo's), defender or
        /// attacker (tbstage's)); item: a weapon id (sword_steel by default). To see the steel, not the ki blade.
        /// </summary>
        private static void Arm(string[] args)
        {
            string who = Dev.Arg(args, 1, "performer");
            Actor u = who == "defender" ? _d : who == "attacker" ? _a : Dev.Performer;
            string item = Dev.Arg(args, 2, "sword_steel");
            if (u == null || !u.isAlive())
            {
                Dev.Log("tbarm: no " + who);
                return;
            }
            try
            {
                u.createNewWeapon(item);
                u.setStatsDirty();
                Dev.Log("tbarm: " + u.asset.id + "#" + u.Sid() + " holds " + item + " (armed " + Choreo.ArmedOf(u) + ")");
            }
            catch (Exception e)
            {
                Dev.Log("tbarm: " + e.Message);
            }
        }

        /// <summary>tbschool [armed]: every tiered demo unit walks Thousand Blades too (a battle where the AI uses the school); 'armed' 1: and holds a sword.</summary>
        private static void WalkSchool(string[] args)
        {
            bool arm = Dev.ArgF(args, 1, 0f) > 0f;
            ActorTrait t = Schools.Trait(Sakuga.School.ThousandBlades);
            int n = 0;
            foreach (Actor u in Dev.Units)
            {
                if (u == null || !u.isAlive() || Tiers.Of(u) == Tier.None || t == null)
                {
                    continue;
                }
                u.addTrait(t, true);
                if (arm && n % 2 == 0)
                {
                    try
                    {
                        u.createNewWeapon("sword_steel");
                        u.setStatsDirty();
                    }
                    catch (Exception)
                    {
                        // (some creatures can't hold one)
                    }
                }
                n++;
            }
            Dev.Log("tbschool: " + n + " fighters walk Thousand Blades" + (arm ? " (every other one armed)" : ""));
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!_went && _t >= 2.8f)
            {
                _went = true;
                Go();
                return;
            }
            if (_t >= 8.5f || _went && (!Combat.Living(_a) || !Combat.Living(_d)) && _t >= 4.6f)
            {
                Dev.Log("tbstage done");
                End();
            }
        }

        private static void Go()
        {
            if (!Combat.Living(_a) || !Combat.Living(_d))
            {
                Dev.Log("tbstage: one of the pair is gone");
                return;
            }
            Fighters.Get(_a).ResetCooldowns();
            Fighters.Get(_d).ResetCooldowns();
            _a.setMana(_a.getMaxMana());
            _d.setMana(_d.getMaxMana());
            Reactions.DevForce[_d.Sid()] = _answer;
            Moves.ReleaseHeld();
            Moves.Hold(_d);   // the defender stands its ground until it answers
            bool ok = Brain.Force(_a, _attack, _attackLevel, _d);
            Dev.Log("tbstage: " + _attack.Id + " started: " + ok);
        }

        private static void End()
        {
            if (_a != null)
            {
                Reactions.DevForce.Remove(_a.Sid());
            }
            if (_d != null)
            {
                Reactions.DevForce.Remove(_d.Sid());
            }
            _staged.Clear();
            Director.Release(_scene);
            if (_t >= 0f)
            {
                Moves.ReleaseHeld();
            }
            _t = -1f;
            _a = _d = null;
        }
    }
}
