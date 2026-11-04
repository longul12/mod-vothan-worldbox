using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Dev staging of the dynamics, so each answer can be watched up close: two fighters on a clean
    /// meadow, held still, then the attacker is made to attack and the defender answers with the
    /// answer under test (Reactions.DevForce: it always reads, and only that answer).
    ///
    ///   stage KIND [tierA] [tierB] [zoom] [raceA] [raceB]
    ///     break      A winds up a Meteor Slam at B; B's Interrupt Jab breaks it mid-charge
    ///     sever      A settles into Severing Flash's stance; B breaks it
    ///     slip       A throws a Heaven Splitter (an uppercut); B slips it and hooks the liver
    ///     duck       A throws a Heaven Splitter (high); B ducks it and sweeps its legs
    ///     parry      B meets A's Heaven Splitter with Still Water
    ///     mirage     B meets it with Mirage Step
    ///     reversal   B slips A's uppercut and hooks; A reverses the hook (A must be a Grandmaster or God)
    /// Then e.g. "capture 120 1 900 700 0.2 f2_slip". It logs "stage done" after 4 s.
    /// </summary>
    internal static class Stage
    {
        private static Actor _a, _b;
        private static string _kind;
        private static float _t = -1f, _wait;
        private static bool _went, _jabbed;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["stage"] = Command;
            Hooks.FrameTick += dt =>
            {
                if (_t >= 0f)
                {
                    try
                    {
                        Tick();
                    }
                    catch (System.Exception e)
                    {
                        SakugaMod.Report("stage", e);
                        End();
                    }
                }
            };
            // the staged pair do nothing on their own (no stray technique spoils the take)
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _kind = Dev.Arg(args, 1, "break");
            Tier ta = Dev.ParseTier(Dev.Arg(args, 2, "master"));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 3, "master"));
            float zoom = Dev.ArgF(args, 4, 12f);
            string ra = Dev.Arg(args, 5, "human"), rb = Dev.Arg(args, 6, "orc");
            if (ta == Tier.None)
            {
                ta = Tier.Master;
            }
            if (tb == Tier.None)
            {
                tb = Tier.Master;
            }
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 8);
            if (c == null)
            {
                Dev.Log("stage: no ground");
                return;
            }
            float gap = _kind == "break" ? 5f : _kind == "sever" ? 3f : 1.4f;
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _b = Dev.Spawn(rb, World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("stage: could not spawn");
                return;
            }
            Progression.Promote(_a, ta, false);
            Progression.Promote(_b, tb, false);
            _a.data.favorite = false;
            _b.data.favorite = false;
            _staged.Add(_a.Sid());
            _staged.Add(_b.Sid());
            Director.Claim(_scene, 7f, _a, _b);   // (no duel claims the pair while it is staged)
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _wait = 1.5f;
            _went = _jabbed = false;
            Dev.Log("stage " + _kind + ": " + ta + " " + ra + " #" + _a.Sid() + " vs " + tb + " " + rb + " #" + _b.Sid());
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > _wait)
                {
                    Dev.Log("stage done (one fell)");
                    End();
                }
                return;
            }
            if (!_went && _t >= _wait)
            {
                _went = true;
                Go();
                return;
            }
            if (_went && (_kind == "break" || _kind == "sever") && !_jabbed && Moves.Charging(_a))
            {
                _jabbed = true;
                Technique jab = Techniques.Get("interrupt_jab");
                Moves.ReleaseHeld();
                bool ok = Brain.Force(_b, jab, Techniques.Level(_b, jab), _a);
                Dev.Log("stage: " + _a.asset.id + " is charging; the jab: " + ok);
            }
            if (_went && _t >= _wait + 4f)
            {
                Dev.Log("stage done");
                End();
            }
        }

        private static void Go()
        {
            Fighter fa = Fighters.Get(_a), fb = Fighters.Get(_b);
            fa.ResetCooldowns();
            fb.ResetCooldowns();
            _a.setMana(_a.getMaxMana());
            _b.setMana(_b.getMaxMana());
            switch (_kind)
            {
                case "slip": Reactions.DevForce[_b.Sid()] = "slip_counter"; break;
                case "duck": Reactions.DevForce[_b.Sid()] = "duck_sweep"; break;
                case "parry": Reactions.DevForce[_b.Sid()] = "still_water"; break;
                case "mirage": Reactions.DevForce[_b.Sid()] = "mirage_step"; break;
                case "reversal":
                    Reactions.DevForce[_b.Sid()] = "slip_counter";
                    Reactions.DevForce[_a.Sid()] = "reversal";
                    break;
            }
            string tech = _kind == "break" ? "meteor_slam" : _kind == "sever" ? "severing_flash" : "heaven_splitter";
            Technique t = Techniques.Get(tech);
            Moves.ReleaseHeld();
            Moves.Hold(_b);   // the defender stands its ground until it answers
            int level = Mathf.Max(1, Techniques.Level(_a, t));
            bool ok = Brain.Force(_a, t, level, _b);
            Dev.Log("stage: " + tech + " " + Techniques.Roman(level) + " by " + _a.asset.id + ": " + ok
                + (ok ? "" : " (can perform " + Moves.CanPerform(_a) + ", move " + (Moves.TryGet(_a, out Move m) ? m.GetType().Name : "-")
                    + ", h " + _a.position_height.ToString("F2") + ", stunned " + _a.hasStatusStunned() + ", forces " + _a.under_forces + ")"));
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
