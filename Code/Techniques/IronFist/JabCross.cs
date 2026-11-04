using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ONE-TWO (Iron Fist, Novice). The boxer's bread and butter: a step in behind the guard, the lead
    /// jab snapped into the face (a small spark, the head twitches), and the rear cross follows it
    /// home with the weight behind it (bigger: the head snaps back and the foe slides back on its
    /// heels). II adds the third punch: a hook to the head that spins the foe half round, or (a guard
    /// up, or one time in three) a hook to the body that folds it. III ends the combination with an
    /// uppercut that launches it (Launched: an air follow-up can take it); after a body hook the
    /// folded head comes down onto it. One time in three a level I or II combination doubles the jab.
    /// Each blow's wind-up steps after a foe the last one drove back. Chains from Close.
    /// Damage (x the user's damage; the tier's Might on top): I 0.45 + 0.95; II + 0.9; III + 1.2.
    /// </summary>
    internal sealed class JabCrossTech : Technique
    {
        public override string Id => "jab_cross";
        public override Tier Entry => Tier.Novice;
        public override Role Role => Role.Single;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Straight | Form.Fast;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 5f, 9f, 14f };
        public override float[] Cooldown => new[] { 3.5f, 4.5f, 5.5f };

        public override bool Accepts(Link link)
        {
            return link == Link.Close;
        }

        private const float Jab = 0.45f, Cross = 0.95f, Hook = 0.9f, Upper = 1.2f;

        public static float Total(int level)
        {
            return Jab + Cross + (level >= 2 ? Hook : 0f) + (level >= 3 ? Upper : 0f);
        }

        private static readonly MeleeWant[] Want =
        {
            new MeleeWant { Reach = 1.8f, Total = Total(1), Worthy = false, Crowd = 0.04f, Alone = 4f, IntoGuard = 0.8f, Worth = 5f },
            new MeleeWant { Reach = 2f, Total = Total(2), Worthy = false, Crowd = 0.04f, Alone = 5f, IntoGuard = 0.8f, Worth = 6f },
            new MeleeWant { Reach = 2.2f, Total = Total(3), Worthy = false, Crowd = 0.04f, Alone = 6f, IntoGuard = 0.8f, Worth = 8f }
        };

        public override Plan Plan(Ctx c, int level)
        {
            Plan p = IronFist.ForcedOn(c) ?? Choreo.PickMelee(c, this, level, Want[Mathf.Clamp(level, 1, 3) - 1]);
            if (p == null || c.Forced)
            {
                return p;
            }
            // cheap, but not wasted on something the jab alone would finish
            Combat.Allowed(c.Self, p.Target, out float cm);
            if (p.Target.getHealth() <= c.Expected(p.Target, Jab) * cm)
            {
                return null;
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
            // the third punch: to the head, or downstairs when the foe keeps its guard up (and now and then: no two alike)
            bool body = level >= 2 && (m.V != null && (Reads.Doing(m.V) & Form.Guard) != 0 || Random.value < 0.33f);
            // a doubled jab now and then (the combination at III is long enough)
            bool doubled = level <= 2 && Random.value < 0.33f;
            m.StepIn();
            if (doubled)
            {
                // two snapping jabs, the second a touch harder: the cross comes behind the second
                m.Strike(new Hit(Blow.Jab, Jab * 0.45f) { Power = 0.8f, ActScale = 0.85f })
                    .Strike(new Hit(Blow.Jab, Jab * 0.55f) { WindScale = 0.7f, ActScale = 0.85f });
            }
            else
            {
                m.Strike(Blow.Jab, Jab);
            }
            m.Strike(new Hit(Blow.Cross, Cross) { Hurt = Hurt.SnapBack, Push = level == 1 ? 0.6f : 0.45f, Power = level == 1 ? 1.1f : 1f });
            if (level >= 2)
            {
                if (body)
                {
                    // a hook to the liver: it folds over the fist (and its chin comes down to meet the uppercut)
                    m.Strike(new Hit(Blow.Body, Hook) { Push = 0.1f, Finisher = level == 2, Power = level == 2 ? 1.15f : 1f, Shows = Form.Punch | Form.Hook | Form.Body });
                }
                else
                {
                    m.Strike(new Hit(Blow.Hook, Hook) { Push = 0.5f, Finisher = level == 2, Power = level == 2 ? 1.15f : 1f });
                }
            }
            if (level >= 3)
            {
                m.Strike(new Hit(Blow.Uppercut, Upper) { Hurt = Hurt.Launch, Push = 1.4f, Lift = body ? 2.3f : 2f, Finisher = true });
            }
            m.Recover(0.2f);
            return m.Start();
        }
    }

    /// <summary>
    /// The Iron Fist school's shared craft: its answers cut the foe's committed swing short so their
    /// own blow throws it (CutShort, Rehurt), they slip to the outside of a swing (Outside), and the
    /// dev staging of the school's counter web (IronFistStage).
    /// </summary>
    internal static class IronFist
    {
        /// <summary>
        /// The foe a counter (or a punish) lands on is caught in a committed move of its own: the swing
        /// it answered, a counter of its own, a move it was just caught recovering from, or one showing
        /// 'answers' (the kind of attack this technique punishes). That move is cut short so the counter's
        /// blow throws the body instead of only rocking it. Never someone's puppet, a cinematic, a light
        /// (interruptible) move, a move the engine broke (it flies already) or a giant's (it only rocks).
        /// 'strict': only the swing it answers or one showing 'answers' (before a blow has landed: a grab
        /// doesn't pull a foe out of an evasion of its own).
        /// </summary>
        public static bool CutShort(Actor foe, Actor by, Move incoming, Form answers, bool strict = false)
        {
            if (foe == null || !Combat.Living(foe) || !Moves.TryGet(foe, out Move m) || m.A != foe || m is Puppet || m.Ended || m.Finished
                || m.Interruptible || m.Cinematic || m is ChoreoMove cm && cm.Broken)
            {
                return false;
            }
            if (!Victims.Movable(foe, by))
            {
                return false;
            }
            bool shows = answers != Form.None && (Reads.FormsOf(m) & answers) != 0;
            bool answered = strict ? m == incoming || shows : m == incoming || m.CounterDepth > 0 || Reads.Recovering(foe) || shows;
            if (!answered)
            {
                return false;
            }
            if (Dev.On) Dev.Trace("iron fist: " + (by != null ? by.asset.id : "?") + " cuts " + foe.asset.id + "#" + foe.Sid() + "'s " + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + " short");
            Choreo.Kill(m);
            return true;
        }

        /// <summary>
        /// A blow of 'm' (its Hit 'h') just landed on 'v' (Hit.OnLand): the foe's committed swing is cut
        /// short if it was one this answers, and the body plays 'im' instead of the blow's own reaction.
        /// Nothing if it is dead, or only rocked (a giant, a body in someone's hands).
        /// </summary>
        public static void Rehurt(ChoreoMove m, Actor v, Hit h, Impact im, Move incoming, Form answers)
        {
            if (m == null || !Combat.Living(v))
            {
                return;
            }
            bool cut = CutShort(v, m.A, incoming, answers);
            if (!cut && Victims.Get(v) == null)
            {
                return;   // it only rocked where it stands: a giant, a body someone else holds
            }
            im.Owed = h != null && m.IsFinal(h) && MeteorSlam.AtDeathsDoor(v);
            Victims.Hurt(v, m.A, m, im);
        }

        /// <summary>
        /// Where a slip to the outside of the foe's swing ends: a blow's length from it, shifted to one side
        /// of the line between them ('side' +1 / -1, flipped if that side is blocked), and never so far
        /// round it that the two bodies stand drawn over one another. On safe ground with a clear step to
        /// it; where it stands if neither side is open (it sways in place). 'from': where the slip set off
        /// (the line is taken from there, so a slip that follows a moving foe doesn't spiral round it).
        /// </summary>
        public static Vector2 Outside(ChoreoMove m, Vector2 from, ref float side, float dist)
        {
            if (m.V == null)
            {
                return m.Stance;
            }
            Vector2 f = m.VG;
            Vector2 d = from - f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(-m.Fwd, 0f);
            float apart = 0.5f * m.Kk + m.V.stats["size"] * 0.25f;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? side : -side;
                Vector2 across = new Vector2(-d.y, d.x) * s;
                Vector2 want = f + d * dist + across * (0.38f * m.Kk);
                if (Mathf.Abs(want.x - f.x) < apart)
                {
                    float sx = Mathf.Abs(from.x - f.x) > 0.05f ? Mathf.Sign(from.x - f.x) : -m.Fwd;
                    want.x = f.x + sx * apart;
                }
                want = Combat.Clamp(want);
                if (Combat.SafeSpot(want) && Combat.ClearPath(m.Stance, want))
                {
                    side = s;
                    return want;
                }
            }
            return m.Stance;
        }

        /// <summary>
        /// A forced plan (a god power, a dev command) on the unit it was pointed at, when there is one in
        /// reach: PickMelee would pick whichever foe in reach scores best. Null otherwise.
        /// </summary>
        public static Plan ForcedOn(Ctx c, float reach = 6f)
        {
            Actor v = c != null && c.Forced ? c.ForceTarget : null;
            if (v == null || !Choreo.Fair(c.Self, v, out _, true) || c.Dist(v) > reach + v.stats["size"] * 0.5f)
            {
                return null;
            }
            Vector2 d = v.current_position - c.Pos;
            return new Plan { Target = v, Point = v.current_position, Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right, Count = 1, Score = 1f, Data = true };
        }

        /// <summary>Can 'd' meet 'atk' where they stand: both on the ground, 'atk' within 'reach' tiles (sizes added)?</summary>
        public static bool InReach(Actor d, Actor atk, float reach)
        {
            if (d == null || atk == null || !Combat.Grounded(atk) || atk.isFlying())
            {
                return false;
            }
            float k = Choreo.KOf(d);
            return Vector2.Distance(d.current_position, atk.current_position) <= reach * k + (atk.stats["size"] + d.stats["size"]) * 0.5f;
        }

        /// <summary>A crimson-white streak (a limb's path drawn on the air), thin, for the arm a counter slips past.</summary>
        public static void Streak(Vector2 a, Vector2 b, float px, float life, Color c)
        {
            Kit.Line(a, b, px, life, 1, FxLayer.Over, 6, Fx.WithA(Color.white, 0.9f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
        }
    }

    /// <summary>
    /// Dev staging of the Iron Fist counter web, so each answer can be watched up close: two fighters on a
    /// clean meadow, held still; then A performs ATTACK on B, and B answers with ANSWER (it always reads,
    /// and only that answer: Reactions.DevForce). An answer that breaks charges (Answers Charge) is thrown
    /// by B the moment A is gathering power (a bolo feint's bait, a haymaker's wind-up).
    ///   ifstage ATTACK ANSWER [tierA] [tierB] [zoom] [raceA] [raceB] [gap]
    /// e.g. "ifstage heaven_splitter liver_shot adept novice", "ifstage jab_cross cross_counter",
    /// "ifstage hundred_fists clinch_knees", "ifstage bolo_feint interrupt_jab expert novice". Logs
    /// "ifstage done" after 5 s.
    /// </summary>
    internal static class IronFistStage
    {
        private static Actor _a, _b;
        private static Technique _atk, _ans;
        private static float _t = -1f, _wait;
        private static bool _went, _thrown;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();
        private static int _place;
        private static WorldTile _center;
        private static float _douse;

        [SakugaInit]
        private static void Init()
        {
            Dev.Commands["ifstage"] = Command;
            // "ifschool [race]": every tiered fighter (of that race) walks the Iron Fist too (battle tests)
            Dev.Commands["ifschool"] = args =>
            {
                string race = Dev.Arg(args, 1, "");
                ActorTrait tr = Schools.Trait(School.IronFist);
                int n = 0;
                foreach (Actor a in World.world.units.getSimpleList())
                {
                    if (tr != null && a != null && a.isAlive() && Tiers.Of(a) != Tier.None && (race == "" || a.asset.id == race))
                    {
                        a.addTrait(tr, true);
                        n++;
                    }
                }
                Dev.Log("ifschool: " + n + " fighters walk the Iron Fist");
            };
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
                    SakugaMod.Report("ifstage", e);
                    End();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += End;
        }

        private static void Command(string[] args)
        {
            End();
            _atk = Techniques.Get(Dev.Arg(args, 1, "jab_cross"));
            _ans = Techniques.Get(Dev.Arg(args, 2, "cross_counter"));
            if (_atk == null || _ans == null)
            {
                Dev.Log("ifstage: unknown technique");
                return;
            }
            Tier ta = Dev.ParseTier(Dev.Arg(args, 3, _atk.Entry.ToString().ToLowerInvariant()));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 4, _ans.Entry.ToString().ToLowerInvariant()));
            ta = ta == Tier.None ? (Tier)Mathf.Max(1, (int)_atk.Entry) : ta;
            tb = tb == Tier.None ? (Tier)Mathf.Max(1, (int)_ans.Entry) : tb;
            float zoom = Dev.ArgF(args, 5, 10f);
            string ra = Dev.Arg(args, 6, "human"), rb = Dev.Arg(args, 7, "orc");
            float gap = Dev.ArgF(args, 8, _atk.Id == "shoulder_charge" ? 4.5f : 1.4f);
            // a fresh meadow each time (four in turn, away from the other dev stages): no fires or bodies of the last take
            _place = (_place + 1) & 3;
            Vector2 at = _place == 0 ? new Vector2(MapBox.width * 0.5f, MapBox.height * 0.25f) : _place == 1 ? new Vector2(MapBox.width * 0.5f, MapBox.height * 0.75f)
                : _place == 2 ? new Vector2(MapBox.width * 0.25f, MapBox.height * 0.5f) : new Vector2(MapBox.width * 0.75f, MapBox.height * 0.5f);
            WorldTile c = Variety.Meadow(at, 8);
            _center = c;
            if (c == null)
            {
                Dev.Log("ifstage: no ground");
                return;
            }
            _a = Dev.Spawn(ra, World.world.GetTile(c.x - Mathf.RoundToInt(gap * 0.5f + 0.5f), c.y) ?? c, true);
            _b = Dev.Spawn(rb, World.world.GetTile(c.x + Mathf.RoundToInt(gap * 0.5f), c.y) ?? c, true);
            if (_a == null || _b == null)
            {
                Dev.Log("ifstage: could not spawn");
                return;
            }
            Progression.Promote(_a, ta, false);
            Progression.Promote(_b, tb, false);
            if (_atk.School != School.None && Schools.Trait(_atk.School) != null)
            {
                _a.addTrait(Schools.Trait(_atk.School), true);
            }
            if (_ans.School != School.None && Schools.Trait(_ans.School) != null)
            {
                _b.addTrait(Schools.Trait(_ans.School), true);
            }
            _a.data.favorite = false;
            _b.data.favorite = false;
            _staged.Add(_a.Sid());
            _staged.Add(_b.Sid());
            Director.Claim(_scene, 8f, _a, _b);
            Vector2 mid = (_a.current_position + _b.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _wait = 1.5f;
            _went = _thrown = false;
            Dev.Log("ifstage " + _atk.Id + " vs " + _ans.Id + ": " + ta + " " + ra + " #" + _a.Sid() + " / " + tb + " " + rb + " #" + _b.Sid());
        }

        private static void Tick()
        {
            _t += Time.unscaledDeltaTime;
            // (the take is about the fighters: fires on the meadow are put out twice a second)
            _douse -= Time.unscaledDeltaTime;
            if (_douse <= 0f && _center != null)
            {
                _douse = 0.5f;
                for (int dx = -10; dx <= 10; dx++)
                {
                    for (int dy = -8; dy <= 10; dy++)
                    {
                        WorldTile t = World.world.GetTile(_center.x + dx, _center.y + dy);
                        if (t != null && t.isOnFire())
                        {
                            t.stopFire();
                        }
                    }
                }
            }
            if (!Combat.Living(_a) || !Combat.Living(_b))
            {
                if (_t > _wait + 0.5f)
                {
                    Dev.Log("ifstage done (one fell)");
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
            // an answer that breaks wind-ups is thrown as A gathers power (B commits)
            if (_went && !_thrown && (_ans.Answers & Form.Charge) != 0 && Moves.Charging(_a))
            {
                _thrown = true;
                Moves.ReleaseHeld();
                int lv = Mathf.Max(1, Techniques.Level(_b, _ans));
                bool ok = Brain.Force(_b, _ans, lv, _a);
                Dev.Log("ifstage: " + _a.asset.id + " is gathering power; " + _ans.Id + " " + Techniques.Roman(lv) + ": " + ok);
            }
            if (_went && _t >= _wait + 5f)
            {
                Dev.Log("ifstage done");
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
            Reactions.DevForce[_b.Sid()] = _ans.Id;
            Moves.ReleaseHeld();
            if ((_ans.Answers & Form.Charge) == 0)
            {
                Moves.Hold(_b);   // the defender stands its ground until it answers
            }
            int level = Mathf.Max(1, Techniques.Level(_a, _atk));
            bool ok = Brain.Force(_a, _atk, level, _b);
            Dev.Log("ifstage: " + _atk.Id + " " + Techniques.Roman(level) + " by " + _a.asset.id + ": " + ok + "; " + _ans.Id + " "
                + Techniques.Roman(Techniques.Level(_b, _ans)) + " ready on " + _b.asset.id);
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
            _center = null;
            _t = -1f;
            _a = _b = null;
            Moves.ReleaseHeld();
        }
    }
}
