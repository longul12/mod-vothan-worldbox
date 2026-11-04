using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// ARROW CUTTER (Hunter's Way, Adept). A hunter's hand is quicker than an arrow: as a shot reaches
    /// it (the game's own arrows, rocks, snowballs and fireballs, and the mod's stones, ki shots and
    /// thrown blades), the hand snaps out and cuts it out of the air: a white slash across its path,
    /// a ting, sparks, and the two halves of it spin away and fall. Reactive: it answers projectiles
    /// (never beams, never blasts); the read is rolled like any answer's (a shooter far weaker is
    /// easier to read). II: after the cut the hunter stays on guard for a moment (Guard: the free
    /// hand weaving in front of it) and every shot that reaches it then is cut as well, without a
    /// further cost. III: a longer guard that also covers the companions standing next to it. A
    /// hunter at II+ under fire from several shooters raises the guard before the arrows arrive.
    /// No damage of its own: the shot is simply gone.
    /// </summary>
    internal sealed class ArrowCutterTech : Technique
    {
        public override string Id => "arrow_cutter";
        public override Tier Entry => Tier.Adept;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.HuntersWay;
        public override Form Forms => Form.Guard | Form.Fast;
        public override Form Answers => Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 6f, 10f, 14f };
        public override float[] Cooldown => new[] { 5f, 4.5f, 4f };

        /// <summary>How long it stays on guard after the cut (seconds; I: none).</summary>
        public static float Window(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 0.7f : 1.3f;
        }

        private const string Timer = "sakuga_hw_arrowcut";
        private const string GuardLabel = "guard";
        private static Technique _tech;

        // ------------------------------------------------------------- planning --

        public override Plan Plan(Ctx c, int level)
        {
            if (c.Forced)
            {
                Actor f = c.ForceTarget ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (f == null)
                {
                    return null;
                }
                Vector2 fd = f.current_position - c.Pos;
                return new Plan { Target = f, Point = f.current_position, Dir = fd.sqrMagnitude > 1e-4f ? fd.normalized : Vector2.right, Count = 1, Score = 1f, Data = true };
            }
            // under fire from several shooters (one at III): the guard goes up before the arrows arrive
            if (level < 2 || c.Near2 > 0)
            {
                return null;
            }
            int shooters = 0;
            Actor first = null;
            for (int i = 0; i < c.Enemies.Count && i < 14; i++)
            {
                float d = c.EnemyDist[i];
                if (d > 11f)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (d < 2.5f || !c.State(v).Ranged)
                {
                    continue;
                }
                bool atMe = c.State(v).AimsAtMe || v.has_attack_target && v.attack_target != null && v.attack_target.isActor() && v.attack_target.a == c.Self
                    || (Reads.Doing(v) & Form.Projectile) != 0;
                if (!atMe)
                {
                    continue;
                }
                shooters++;
                first ??= v;
            }
            if (first == null || shooters < (level >= 3 ? 1 : 2))
            {
                return null;
            }
            Vector2 dir = first.current_position - c.Pos;
            return new Plan
            {
                Target = first,
                Point = first.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = shooters,
                Score = 18f + 9f * shooters + (c.HpPct < 0.5f ? 10f : 0f),
                Data = false
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            bool forced = p.Data is bool f && f;
            m.SetVictim(null);   // (it holds nobody still: it only turns to face the shooters)
            if (p.Target != null)
            {
                m.Face(p.Target.current_position - a.current_position);
            }
            Build(m, m.Level, null, forced ? Mathf.Max(1.2f, Window(m.Level)) : Window(m.Level));
            return m.Start();
        }

        // ------------------------------------------------------------- the move --

        /// <summary>One raised guard: who, its move, its level, the snap of its hand after a cut.</summary>
        private sealed class Guard
        {
            public Actor A;
            public long Id;
            public ChoreoMove M;
            public int Level;
            public float Kick;
            public float Twirl;
        }

        private static readonly List<Guard> _guards = new List<Guard>();

        /// <summary>
        /// The cut and the guard: the hand snaps out (a slash across 'cutAt', if a shot is being cut
        /// right now), settles, stays on guard 'window' seconds (every shot reaching it then is cut
        /// too), and it recovers.
        /// </summary>
        private static void Build(ChoreoMove m, int level, Vector2? cutAt, float window)
        {
            Guard g = new Guard { A = m.A, Id = m.A.Sid(), M = m, Level = level };
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.06f,
                To = new Shape(16f, 1.14f, 0.9f, 0f, 0.05f),
                Ease = Ease.OutExpo,
                Track = false,
                Forms = Form.Guard | Form.Fast | Form.Counter,
                Enter = mm =>
                {
                    if (cutAt.HasValue)
                    {
                        Slash(mm, cutAt.Value, true);
                    }
                    else if (mm.Vis)
                    {
                        // (a flourish: the hand cuts the air once)
                        Kit.Swish(mm, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(6f * mm.Kk), 4, 30), 0.8f, 120f, -40f, 1), 0.12f, new Vector2(2f, 2f), mm.Fwd, mm.Hue.Core, mm.Hue.Edge, 6);
                        Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
                    }
                }
            });
            m.Add(new Beat { Tempo = Tempo.Follow, Dur = 0.08f, To = new Shape(-4f, 1.04f, 0.96f, 0f, -0.02f), Ease = Ease.OutQuad, Track = false, Forms = Form.Guard | Form.Fast });
            if (window > 0f)
            {
                m.Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = window,
                    To = new Shape(-6f, 1.06f, 0.94f, 0f, -0.03f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = Form.Guard | Form.Fast,
                    Label = GuardLabel,
                    Enter = mm =>
                    {
                        g.Kick = 0f;
                        g.Twirl = 0f;
                        if (!_guards.Contains(g))
                        {
                            _guards.Add(g);
                        }
                        if (mm.Vis)
                        {
                            Kit.Flare(mm.Pose.Ground, mm.Px, mm.Hue.Edge, 10f * mm.Kk, 0.3f);
                        }
                    },
                    Every = (mm, k) =>
                    {
                        // the free hand weaves in front of it; after a cut it snaps out again
                        if (g.Kick > 0f)
                        {
                            mm.Pose.Rot += 20f * g.Kick;
                            mm.Pose.SX *= 1f + 0.12f * g.Kick;
                            mm.Pose.SY *= 1f - 0.08f * g.Kick;
                            g.Kick = Mathf.Max(0f, g.Kick - Clock.Dt * 9f);
                        }
                        else
                        {
                            mm.Pose.Rot += 3f * Mathf.Sin(mm.BeatT * 16f);
                        }
                        g.Twirl -= Clock.Dt;
                        if (g.Twirl <= 0f && mm.Vis)
                        {
                            g.Twirl = 0.22f;
                            Vector2 hand = mm.Hand(Limb.Palm);
                            Fx.Glint(hand + new Vector2(0f, 1f * mm.Px), mm.Px, 0.12f, mm.Hue.Core, 0.6f);
                            Kit.Swish(mm, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(5f * mm.Kk), 4, 24), 0.7f, 140f, -20f, 1), 0.1f, new Vector2(2f, 1f), mm.Fwd,
                                Fx.WithA(mm.Hue.Core, 0.7f), Fx.WithA(mm.Hue.Edge, 0.5f), 5);
                        }
                    },
                    Exit = mm => _guards.Remove(g)
                });
            }
            m.Recover(0.16f);
            m.Ended2 = (mm, aborted) => _guards.Remove(g);
        }

        /// <summary>
        /// The hand cuts a shot at 'at': a smear from the hand to it, a white slash across it, a ting,
        /// sparks, a small flash ('first': the answer's own cut; later ones are the guard's).
        /// </summary>
        private static void Slash(ChoreoMove m, Vector2 at, bool first)
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.03f);
            if (!m.Vis)
            {
                return;
            }
            float px = m.Px;
            Vector2 hand = m.Hand(Limb.Palm);
            Vector2 d = at - hand;
            if (d.sqrMagnitude < 1e-6f)
            {
                d = m.Dir;
            }
            if (d.magnitude < 1.6f * m.Kk)
            {
                ChoreoFx.Smear(m.BodyPos(), at, px, 0.08f, 1, 1, m.Hue.Core, m.Hue.Edge, FxLayer.Over, 7);
            }
            Vector2 n = Choreo.Perp(d.normalized);
            float half = (first ? 5f : 4f) * Mathf.Sqrt(m.Kk) * px;
            Kit.Line(at - n * half + d.normalized * px, at + n * half - d.normalized * px, px, 0.09f, 1, m.TopLayer, 16, Color.white, Fx.WithA(m.Hue.Core, 0.85f), Fx.WithA(m.Hue.Edge, 0f));
            Kit.Sparks(at, px, first ? 5 : 3, 40f, 90f, Color.white);
            Kit.Flash(at, px, 2, 0.05f, Fx.WithA(m.Hue.Core, 0.9f), m.TopLayer, 24);
            Fx.Glint(at, px, 0.12f, Color.white, 0.8f);
        }

        // ------------------------------------------------------------- answers --

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            Form inc = Reads.FormsOf(incoming);
            if ((inc & (Form.Beam | Form.Area)) != 0 || (inc & Form.Projectile) == 0 && Shots.Reaching == null)
            {
                return Contact.Hit;   // no beams, no blasts: only what flies at it
            }
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            Shot s = Shots.Reaching;
            bool shot = s != null && !s.Done && s.Hostile;
            Vector2 body = Choreo.BodyAt(defender, 0.6f);
            Vector2 from = attacker != null ? (Vector2)attacker.current_position - (Vector2)defender.current_position : Vector2.right;
            Vector2 at = shot ? s.Pos + new Vector2(0f, s.Z) : body + (from.sqrMagnitude > 1e-4f ? from.normalized : Vector2.right) * 0.35f * Choreo.KOf(defender);
            Reactions.Free(defender);
            if (!Cut(defender, attacker, at, level))
            {
                return Contact.Hit;
            }
            if (shot)
            {
                s.Cut();
                return Contact.Parried;
            }
            return area ? Contact.Evaded : Contact.Parried;
        }

        /// <summary>Starts the cutter's move on 'a' (facing the shot at 'at'). True if it started.</summary>
        private static bool Cut(Actor a, Actor shooter, Vector2 at, int level)
        {
            _tech ??= Techniques.Get("arrow_cutter");
            ChoreoMove m = shooter != null ? ChoreoMove.Answer(a, shooter, _tech, level) : null;
            if (m == null)
            {
                return false;
            }
            m.SetVictim(null);
            Vector2 d = at - (Vector2)a.current_position;
            if (d.sqrMagnitude > 1e-4f)
            {
                m.Face(d);
            }
            Build(m, level, at, Window(level));
            return m.Start();
        }

        /// <summary>The guard covering 'target' against 'shooter' right now (its own, or at III a companion's beside it).</summary>
        private static Guard Covering(Actor target, Actor shooter)
        {
            for (int i = _guards.Count - 1; i >= 0; i--)
            {
                Guard g = _guards[i];
                if (!Active(g))
                {
                    continue;
                }
                if (shooter != null && shooter.kingdom != null && g.A.kingdom != null && !g.A.areFoes(shooter))
                {
                    continue;
                }
                if (g.A == target)
                {
                    return g;
                }
                if (g.Level >= 3 && Combat.IsAlly(g.A, target) && Vector2.Distance(g.A.current_position, target.current_position) <= 1.4f * Choreo.KOf(g.A))
                {
                    return g;
                }
            }
            return null;
        }

        private static bool Active(Guard g)
        {
            if (g.A == null || g.A.Sid() != g.Id || !g.A.isAlive() || g.M == null || g.M.Ended || g.M.Finished)
            {
                return false;
            }
            Beat b = g.M.Current;
            return b != null && b.Label == GuardLabel && Moves.TryGet(g.A, out Move cur) && cur == g.M;
        }

        /// <summary>A guard cuts another shot: its hand snaps out again.</summary>
        private static void GuardCut(Guard g, Vector2 at)
        {
            g.Kick = 1f;
            Slash(g.M, at, false);
            if (Dev.On) Dev.Trace("arrow_cutter: the guard of " + g.A.asset.id + "#" + g.Id + " cuts another shot");
        }

        // ------------------------------------------------------ shots in flight --

        // the game's projectile being answered right now is cut (not sent back): Projectile.getDeflected
        private static bool _cutNext;
        private static int _cutFrame = -1;
        private static float _cutPx = 0.1f;
        private static Color _cutCore = Color.white, _cutEdge = Color.white;

        /// <summary>Dev ('hwcut 1'): every shot reaching a fighter who knows it is read, at no cost (testing).</summary>
        private static bool _devAlways;

        [SakugaInit]
        private static void Init()
        {
            Shots.Interceptors.Add(Intercept);
            Hooks.BeforeOrdinaryHit.Add(OnGameShot);
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += Clear;
            // hwcut [0|1]: fighters who know Arrow Cutter read every game projectile (no cooldown, no mana)
            Dev.Commands["hwcut"] = args =>
            {
                _devAlways = Dev.Arg(args, 1, "1") != "0";
                Dev.Log("hwcut: " + (_devAlways ? "every shot is read" : "normal reads"));
            };
            // hwteach [tech] [tier]: the performer's foes (dev units) learn a technique at a tier and always answer with it
            Dev.Commands["hwteach"] = args =>
            {
                Technique t = Techniques.Get(Dev.Arg(args, 1, "arrow_cutter"));
                if (t == null || !Enum.TryParse(Dev.Arg(args, 2, "adept"), true, out Tier tier))
                {
                    Dev.Log("hwteach: tech tier");
                    return;
                }
                Actor me = Dev.Performer;
                int n = 0;
                foreach (Actor u in Dev.Units)
                {
                    if (u == null || !u.isAlive() || u == me || me != null && !me.areFoes(u))
                    {
                        continue;
                    }
                    Progression.Promote(u, tier, false);
                    u.addTrait(Techniques.Trait(t), true);
                    if (t.School != School.None && Schools.Trait(t.School) != null)
                    {
                        u.addTrait(Schools.Trait(t.School), true);
                    }
                    Reactions.DevForce[u.Sid()] = t.Id;
                    n++;
                }
                Dev.Log("hwteach: " + n + " foes know " + t.Id + " at " + tier);
            };
        }

        private static void Clear()
        {
            _guards.Clear();
            _cutNext = false;
        }

        private static void Tick(float dt)
        {
            if (_cutNext && Time.frameCount != _cutFrame)
            {
                _cutNext = false;   // (answered, but not by a flying projectile: nothing to cut)
            }
            for (int i = _guards.Count - 1; i >= 0; i--)
            {
                Guard g = _guards[i];
                if (g.A == null || g.A.Sid() != g.Id || !g.A.isAlive() || g.M == null || g.M.Ended)
                {
                    _guards.RemoveAt(i);
                }
            }
        }

        /// <summary>Shots.Interceptors: one of the mod's shots about to touch a guarded unit is cut.</summary>
        private static bool Intercept(Shot s, Actor u)
        {
            if (_guards.Count == 0 || s == null || s.Done || !s.Hostile || u == null)
            {
                return false;
            }
            Guard g = Covering(u, s.A);
            if (g == null)
            {
                return false;
            }
            Vector2 at = s.Pos + new Vector2(0f, s.Z);
            s.Cut();
            GuardCut(g, at);
            return true;
        }

        /// <summary>
        /// Hooks.BeforeOrdinaryHit: one of the game's projectiles (an arrow, a rock, a snowball, a
        /// fireball) reaching a unit. On guard: it is cut. Else a hunter who knows the technique may
        /// read it and cut it out of the air (it pays the mana and the cooldown here).
        /// </summary>
        private static bool OnGameShot(AttackData d, Actor t)
        {
            if (!d.is_projectile || t == null || d.initiator == null || !d.initiator.isActor())
            {
                return false;
            }
            Actor atk = d.initiator.a;
            if (atk == null || atk == t)
            {
                return false;
            }
            Vector2 at = new Vector2(d.hit_position.x, d.hit_position.y + d.hit_position.z);
            Guard g = _guards.Count > 0 ? Covering(t, atk) : null;
            if (g != null)
            {
                GuardCut(g, at);
                MarkCut(g.M);
                return true;
            }
            if (!Fighters.TryGet(t, out Fighter f))
            {
                return false;
            }
            _tech ??= Techniques.Get("arrow_cutter");
            Technique tech = _tech;
            if (tech == null)
            {
                return false;
            }
            int level = Techniques.Level(t, tech, f.Tier);
            bool dev = _devAlways && Dev.On;
            if (level <= 0 || !dev && (!f.Ready(tech) || !Sakuga.Mana.Has(t, tech, level, false) || !f.Elapsed(Timer, 0.25f)) || !Moves.CanPerform(t) || t.hasStatusStunned())
            {
                return false;
            }
            f.Mark(Timer);
            Form forms = Reads.OrdinaryForms(atk, d);
            float match = FormSet.Match(tech.Answers, forms);
            if (match <= 0f)
            {
                return false;
            }
            // (an arrow is a hunter's to cut; a spell's fireball is harder to meet)
            float chance = Reactions.ReadChance(t, f, atk, null, false, match, 1) * ((forms & Form.Ki) != 0 ? 0.5f : 0.85f);
            if (!dev && Random.value >= chance)
            {
                return false;
            }
            if (!Cut(t, atk, at, level))
            {
                return false;
            }
            Sakuga.Mana.Spend(t, tech, level, false);
            f.SetCooldown(tech, tech.CooldownFor(t, level));
            Director.Used(tech, t.current_position);
            Brain.Started.TryGetValue(tech.Id, out int n);
            Brain.Started[tech.Id] = n + 1;
            if (Moves.TryGet(t, out Move nm) && nm is ChoreoMove cm)
            {
                MarkCut(cm);
            }
            else
            {
                MarkCut(null);
            }
            if (Dev.On) Dev.Trace("react arrow_cutter " + Techniques.Roman(level) + " by " + t.asset.id + "#" + t.Sid() + " cuts " + atk.asset.id + "'s " + d.projectile_id);
            try
            {
                Hooks.Answered?.Invoke(t, atk, tech, 0, Contact.Parried);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] answered hook: " + e);
            }
            return true;
        }

        private static void MarkCut(ChoreoMove m)
        {
            _cutNext = true;
            _cutFrame = Time.frameCount;
            _cutPx = m != null ? m.Px : 0.1f;
            _cutCore = m != null ? m.Hue.Core : Color.white;
            _cutEdge = m != null ? m.Hue.Edge : Color.white;
        }

        /// <summary>
        /// Projectile.getDeflected (patched): the projectile just answered was cut, not turned: it
        /// falls in two halves where it was and is gone. False if it wasn't one of ours.
        /// </summary>
        internal static bool TakeCut(Projectile p)
        {
            if (!_cutNext || Time.frameCount != _cutFrame || p == null)
            {
                _cutNext = false;
                return false;
            }
            _cutNext = false;
            Vector2 at = p.getTransformedPositionWithHeight();
            Vector2 dir = p.getCurrentPosition() - p.getStartVector();
            float px = _cutPx;
            if (Kit.Visible(at))
            {
                // the two halves spin away and drop
                Vector2 side = Choreo.Perp(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right);
                for (int s = -1; s <= 1; s += 2)
                {
                    Particle h = Fx.Spawn(Art.Pixel, at + side * s * px, px, 0.55f, FxLayer.Top, 12);
                    if (h == null)
                    {
                        break;
                    }
                    h.SX = 3f;
                    h.SY = 1f;
                    h.Rot = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    h.Spin = s * Random.Range(500f, 800f);
                    h.Moving((side * s * 18f - dir.normalized * 10f) * px, 14f * px, 1f, 80f * px);
                    h.Bounce = 0.25f;
                    h.Color(Color.Lerp(HwLoKit.Hemp, Color.white, 0.2f), HwLoKit.HempDark, Fx.WithA(HwLoKit.HempDark, 0f));
                }
                Kit.Flash(at, px, 2, 0.05f, Fx.WithA(_cutCore, 0.9f), FxLayer.Top, 24);
                Kit.Sparks(at, px, 3, 30f, 70f, _cutEdge);
            }
            p.setState(ProjectileState.ToRemove);
            return true;
        }
    }

    /// <summary>A projectile of the game an Arrow Cutter cut is not sent back: it is gone (ArrowCutterTech.TakeCut).</summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.getDeflected))]
    internal static class ArrowCutterDeflectPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance)
        {
            try
            {
                return !ArrowCutterTech.TakeCut(__instance);
            }
            catch (Exception e)
            {
                SakugaMod.Report("arrow cutter", e);
                return true;
            }
        }
    }
}
