using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// The engine's dev commands (they work while the mod's "dev" folder exists; see Dev.cs):
    ///   choreo                  live reactions, shots, beams, hazards, clones, moves
    ///   beamclash [tierA] [tierB] [raceB] [dist] [level]
    ///                           after a 'move dragon_wave ...' demo: a second Ki Arts fighter appears
    ///                           'dist' tiles from the performer and both fire Dragon Waves at each
    ///                           other: a beam clash
    ///   chbreak [delay] [force] the performer starts its technique ('move' demo) on the nearest foe and
    ///                           'delay' seconds in it is broken (Choreo.Break) with 'force'
    ///   chdemo kind             the performer shows one primitive on the nearest foe: orb bullet disc
    ///                           boulder windblade swordwave harpoon lightsword spear volley homing
    ///                           shockwave dome quake pillar spikes cone tornado rain meteors legion
    ///                           mirror flurry suplex toss blink leap airwalk circle slip backhop
    ///                           combo launch knockdown fold spin reel juggle group guard barrier
    ///   chkill [delay] [self]   'delay' s from now the performer's target dies (or, 'self', the performer)
    ///   chfriend [delay]        'delay' s from now the performer's target joins its kingdom (a city
    ///                           taken mid-move): no blow of the move may land on it from then on; its
    ///                           health is logged then and a second and a half later
    /// </summary>
    internal static class ChoreoDev
    {
        private sealed class Later
        {
            public float At;
            public Action Do;
        }

        private static readonly List<Later> _later = new List<Later>();

        public static void Register()
        {
            Dev.Commands["choreo"] = args => Dev.Log("choreo: victims " + Victims.Count + " shots " + Shots.Count + " beams " + Beams.Count + " hazards " + Areas.Count
                + " clones " + Clones.Count + " moves " + Moves.Count + " fx " + Fx.Live);
            Dev.Commands["beamclash"] = BeamClash;
            Dev.Commands["chbreak"] = ChBreak;
            Dev.Commands["chdemo"] = ChDemo;
            // chkill [delay] [what]: 'delay' s from now the performer's target dies (what=target) or the performer itself (what=self)
            Dev.Commands["chkill"] = args =>
            {
                float delay = Dev.ArgF(args, 1, 0.5f);
                string what = Dev.Arg(args, 2, "target");
                After(delay, () =>
                {
                    Actor a = Dev.Performer;
                    if (a == null)
                    {
                        Dev.Log("chkill: no performer");
                        return;
                    }
                    Actor victim = a;
                    if (what != "self")
                    {
                        victim = Moves.TryGet(a, out Move m) && m is ChoreoMove cm ? cm.V : null;
                    }
                    if (victim == null || !victim.isAlive())
                    {
                        Dev.Log("chkill: nothing to kill");
                        return;
                    }
                    Dev.Log("chkill: " + victim.asset.id + "#" + victim.Sid() + " dies (in " + (Moves.TryGet(victim, out Move vm) ? vm.GetType().Name : "no move") + ")");
                    victim.getHit(victim.getHealth() * 10f + 1000f, true, AttackType.Other, null, false, false, false);
                });
            };
            Dev.Commands["chfriend"] = ChFriend;
            Hooks.FrameTick += dt => Tick();
            Hooks.WorldCleared += () => _later.Clear();
        }

        private static void After(float seconds, Action a)
        {
            _later.Add(new Later { At = Time.unscaledTime + seconds, Do = a });
        }

        private static void Tick()
        {
            if (_later.Count == 0)
            {
                return;
            }
            float now = Time.unscaledTime;
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (i >= _later.Count || _later[i].At > now)
                {
                    continue;
                }
                Later l = _later[i];
                _later.RemoveAt(i);
                try
                {
                    l.Do();
                }
                catch (Exception e)
                {
                    Dev.Log("choreo dev: " + e);
                }
            }
        }

        private static Actor NearestFoe(Actor a, float r = 30f)
        {
            Actor best = null;
            float bd = r * r;
            foreach (Actor u in Dev.Units)
            {
                if (u == null || u == a || !Combat.Living(u) || !Combat.IsFoe(a, u))
                {
                    continue;
                }
                float d = (u.current_position - a.current_position).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
            return best;
        }

        // ---------------------------------------------------------- beam clash --

        private static void BeamClash(string[] args)
        {
            Actor a = Dev.Performer;
            if (a == null)
            {
                Dev.Log("beamclash: run 'move dragon_wave ...' first");
                return;
            }
            Tier ta = Dev.ParseTier(Dev.Arg(args, 1, "expert"));
            Tier tb = Dev.ParseTier(Dev.Arg(args, 2, "expert"));
            string raceB = Dev.Arg(args, 3, "orc");
            float dist = Dev.ArgF(args, 4, 9f);
            int level = (int)Dev.ArgF(args, 5, 2f);
            Technique t = Techniques.Get("dragon_wave");
            if (t == null)
            {
                Dev.Log("beamclash: no dragon_wave");
                return;
            }
            Vector2 p = a.current_position + new Vector2(dist, 0f);
            WorldTile tile = Combat.TileAt(p);
            Actor b = tile != null ? Dev.Spawn(raceB, tile, true) : null;
            if (b == null)
            {
                Dev.Log("beamclash: could not spawn " + raceB);
                return;
            }
            if (ta != Tier.None && Tiers.Of(a) != ta)
            {
                Progression.Promote(a, ta, false);
            }
            Progression.Promote(b, tb, false);
            if (b.data != null)
            {
                b.data.favorite = false;
            }
            b.addTrait(Schools.Trait(School.KiArts), true);
            Moves.Hold(a);
            Moves.Hold(b);
            Dev.Watch(Vector2.Lerp(a.current_position, b.current_position, 0.5f) + new Vector2(0f, 1.5f));
            Dev.Log("beamclash: " + a.asset.id + " (" + Tiers.Of(a) + ") vs " + b.asset.id + " (" + tb + ") " + dist + " tiles apart, foes " + Combat.IsFoe(a, b));
            After(1.2f, () =>
            {
                Moves.ReleaseHeld();
                bool okA = Brain.Force(a, t, level, b);
                Dev.Log("beamclash: A fires " + okA);
            });
            After(1.32f, () =>
            {
                bool okB = Brain.Force(b, t, level, a);
                Dev.Log("beamclash: B fires " + okB);
            });
        }

        // -------------------------------------------------------------- break --

        private static void ChBreak(string[] args)
        {
            Actor a = Dev.Performer;
            if (a == null)
            {
                Dev.Log("chbreak: run a 'move' demo first");
                return;
            }
            float delay = Dev.ArgF(args, 1, 0.4f);
            float force = Dev.ArgF(args, 2, 4f);
            After(delay, () =>
            {
                if (!Moves.TryGet(a, out Move m))
                {
                    Dev.Log("chbreak: the performer is in no move");
                    return;
                }
                Actor by = NearestFoe(a) ?? a;
                Vector2 dir = a.current_position - by.current_position;
                bool charging = m.Charging;
                bool broke = Choreo.Break(a, by, dir, force);
                Dev.Log("chbreak: " + m.GetType().Name + " charging " + charging + " forms " + m.Forms + " -> broken " + broke);
            });
        }

        private static void ChFriend(string[] args)
        {
            Actor a = Dev.Performer;
            if (a == null)
            {
                Dev.Log("chfriend: run a 'move' demo first");
                return;
            }
            float delay = Dev.ArgF(args, 1, 0.4f);
            After(delay, () =>
            {
                Actor v = Moves.TryGet(a, out Move m) && m is ChoreoMove cm ? cm.V : null;
                if (v == null || !v.isAlive() || a.kingdom == null)
                {
                    Dev.Log("chfriend: the performer has no target");
                    return;
                }
                long id = v.Sid();
                int hp = v.getHealth();
                v.joinKingdom(a.kingdom);
                Dev.Log("chfriend: " + v.asset.id + "#" + id + " joins the performer's kingdom at hp " + hp + " (foes now: " + a.areFoes(v) + ")");
                After(1.5f, () =>
                {
                    bool same = v.Sid() == id;
                    Dev.Log("chfriend: 1.5 s later " + v.asset.id + "#" + id + (same && v.isAlive() ? " hp " + v.getHealth() + (v.getHealth() >= hp ? " (untouched)" : " (HURT AFTER IT JOINED)") : " is gone"));
                });
            });
        }

        // --------------------------------------------------------------- demo --

        private static void ChDemo(string[] args)
        {
            Actor a = Dev.Performer;
            if (a == null)
            {
                Dev.Log("chdemo: run a 'move' demo first");
                return;
            }
            string kind = Dev.Arg(args, 1, "orb");
            int level = (int)Dev.ArgF(args, 2, 2f);
            Actor v = NearestFoe(a);
            Moves.ReleaseHeld();
            Plan p = new Plan { Target = v, Point = v != null ? v.current_position : a.current_position + Vector2.right * 4f, Dir = Vector2.right, Level = level, Data = true };
            if (v != null)
            {
                Vector2 d = v.current_position - a.current_position;
                p.Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
            }
            ChoreoMove m = ChoreoMove.Make(a, p, null);
            if (m == null)
            {
                Dev.Log("chdemo: the performer can't act");
                return;
            }
            switch (kind)
            {
                case "orb":
                case "bullet":
                case "disc":
                case "boulder":
                case "windblade":
                case "swordwave":
                case "harpoon":
                case "lightsword":
                case "spear":
                case "stone":
                    {
                        ShotKind sk = kind == "orb" ? ShotKind.Orb : kind == "bullet" ? ShotKind.Bullet : kind == "disc" ? ShotKind.Disc : kind == "boulder" ? ShotKind.Boulder
                            : kind == "windblade" ? ShotKind.WindBlade : kind == "swordwave" ? ShotKind.SwordWave : kind == "harpoon" ? ShotKind.Harpoon
                            : kind == "lightsword" ? ShotKind.LightSword : kind == "spear" ? ShotKind.Spear : ShotKind.Stone;
                        ShotSpec s = new ShotSpec { Kind = sk, Mult = 1f, Speed = sk == ShotKind.Boulder ? 9f : 18f };
                        if (sk == ShotKind.Boulder)
                        {
                            s.Flight = Flight.Lob;
                            s.Crater = 1f;
                            s.Area = 1f;
                        }
                        if (sk == ShotKind.Disc)
                        {
                            s.Flight = Flight.Boomerang;
                            s.Range = 7f;
                            s.Pierce = 3;
                        }
                        m.Charge(0.3f, sk == ShotKind.Boulder ? ChargeStyle.Raised : ChargeStyle.Palms, 2f).Shoot(s);
                        break;
                    }
                case "volley":
                    m.Charge(0.25f, ChargeStyle.Palms, 2f).Shoot(new ShotSpec { Kind = ShotKind.Bullet, Mult = 0.3f, Speed = 22f }, 7, 9f, 0.05f);
                    break;
                case "homing":
                    m.Charge(0.3f, ChargeStyle.Raised, 2f).Shoot(new ShotSpec { Kind = ShotKind.Orb, Flight = Flight.Homing, Mult = 0.5f, Speed = 12f, Turn = 7f }, 5, 40f, 0.06f);
                    break;
                case "shockwave":
                    m.Crouch(0.15f).Land(1f).Shockwave(4f, 0.8f);
                    break;
                case "dome":
                    m.Charge(0.4f, ChargeStyle.Stance, 3f).Dome(3f, 1f).Hold(0.4f);
                    break;
                case "quake":
                    m.Crouch(0.15f).Quake(5f, 0.8f, 7).Hold(0.3f);
                    break;
                case "pillar":
                    m.Crouch(0.1f).Pillar(1f).Hold(0.4f);
                    break;
                case "spikes":
                    m.Crouch(0.1f).Do(mm =>
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = i / 8f * Mathf.PI * 2f;
                            Vector2 at = mm.Pose.Ground + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * (1.4f + i % 2 * 0.8f);
                            Areas.Add(new PillarHazard(mm, at, 0.05f * i, 1.2f, 0.4f, 1.6f, true));
                        }
                    }).Hold(0.6f);
                    break;
                case "cone":
                    m.Crouch(0.08f).Cone(0.8f, 40f, 4.5f, 0.5f).Hold(0.5f, true);
                    break;
                case "tornado":
                    m.SpinInPlace(2f, 0.4f).Tornado(3f, 1f, 1.2f).SpinInPlace(4f, 1.1f);
                    break;
                case "rain":
                    m.Charge(0.3f, ChargeStyle.Raised, 2f).Rain(RainKind.Swords, 18, 3f, 0.4f, 1.2f).Hold(1.3f);
                    break;
                case "light":
                    m.Charge(0.3f, ChargeStyle.Raised, 2f).Rain(RainKind.Light, 10, 3f, 0.5f, 1f, 0.9f).Hold(1.1f);
                    break;
                case "meteors":
                    m.Charge(0.3f, ChargeStyle.Raised, 2f).Rain(RainKind.Meteors, 8, 3f, 0.6f, 1f, 1f).Hold(1.2f);
                    break;
                case "legion":
                    m.Legion(mm =>
                    {
                        List<Actor> list = new List<Actor>();
                        foreach (Actor u in Dev.Units)
                        {
                            if (u != null && Combat.Living(u) && Combat.IsFoe(mm.A, u))
                            {
                                list.Add(u);
                            }
                        }
                        return list;
                    }, Blow.Cross, 0.6f).WaitClones(2f);
                    break;
                case "mirror":
                    m.Clones(4, 1.2f, (mm, c, i) => c.Wait(0.4f + i * 0.1f).Shatter(1.2f, 0.4f)).WaitClones(2f);
                    break;
                case "flurry":
                    m.StepIn().Flurry(12, 1.2f, Blow.Jab, Blow.Cross, 0.6f).Strike(new Hit(Blow.Cross, 1f) { Hurt = Hurt.Reel, Push = 1.6f, Finisher = true });
                    break;
                case "suplex":
                    m.Grab().Suplex(1.2f, 1f, 1.2f);
                    break;
                case "toss":
                    m.Grab().Lift(Grip.Overhead, 0.2f).Toss(1f, 4f);
                    break;
                case "blink":
                    m.Blink().Strike(Blow.Elbow, 0.8f);
                    break;
                case "leap":
                    m.Crouch(0.12f).Leap(ChoreoMove.ToTarget, 1.8f, 0.5f, 360f).Land(1f, 1.2f);
                    break;
                case "airwalk":
                    m.AirWalk(2f, 3).Drop(0.15f).Land(0.8f);
                    break;
                case "circle":
                    m.StepIn().Circle().Strike(Blow.Hook, 0.8f);
                    break;
                case "slip":
                    m.StepIn().Slip(0.5f).Strike(Blow.Body, 0.8f);
                    break;
                case "backhop":
                    m.StepIn().BackHop(1f).Strike(Blow.SnapKick, 0.8f);
                    break;
                case "launch":
                    m.StepIn().Strike(Blow.Uppercut, 1f);
                    break;
                case "guard":
                    m.GuardUp(0.9f);
                    break;
                case "barrier":
                    m.GuardUp(0.9f, true, Element.Ki);
                    break;
                case "knockdown":
                    m.StepIn().Strike(Blow.Sweep, 1f);
                    break;
                case "fold":
                    m.StepIn().Strike(Blow.Body, 1f, Hurt.Fold);
                    break;
                case "spin":
                    m.StepIn().Strike(Blow.Hook, 1f);
                    break;
                case "reel":
                    m.StepIn().Strike(Blow.SideKick, 1f);
                    break;
                case "juggle":
                    // popped up, two air jabs keep it hanging, a heel drop drives it into the ground (no fire)
                    m.StepIn().Strike(new Hit(Blow.Uppercut, 0.6f) { Hurt = Hurt.Juggle, Lift = 1.6f }).Rise(1.2f, 0.18f)
                        .Strike(new Hit(Blow.Jab, 0.15f) { Air = true }).Strike(new Hit(Blow.Cross, 0.2f) { Air = true })
                        .Strike(new Hit(Blow.HeelDrop, 0.8f) { Air = true, Hurt = Hurt.DriveIn, Crater = 0.8f, Fire = false }).Land();
                    break;
                case "group":
                    // the hook and the uppercut only at III (one OnlyIf for the whole group)
                    m.StepIn().Strike(Blow.Jab, 0.4f).OnlyIf(mm => mm.Level >= 3, g => g.Strike(Blow.Hook, 0.6f).Strike(Blow.Uppercut, 0.8f)).Strike(Blow.Cross, 0.8f);
                    break;
                default:
                    m.StepIn().Strike(Blow.Jab, 0.4f).Strike(Blow.Cross, 0.8f).Strike(Blow.Hook, 0.8f).Strike(new Hit(Blow.Uppercut, 1.2f) { Finisher = true });
                    break;
            }
            bool ok = m.Start();
            Dev.Log("chdemo " + kind + " " + Techniques.Roman(level) + ": " + ok + (v != null ? " on " + v.asset.id : ""));
        }
    }
}
