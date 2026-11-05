using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>Plays a recipe's steps on a Choreo move (see Forge for the language).</summary>
    internal static class ForgeInterpreter
    {
        internal static int ShotCount(ForgeStep s,float many)
        {
            float count=s.K("count",1);
            return Mathf.Max(1,Mathf.RoundToInt(count*(count>1 && s.K("fixed",0)<=0?many:1)));
        }
        /// <summary>At load: an unknown step or name is an error (the recipe is rejected, never half played).</summary>
        public static void Check(ForgeStep s)
        {
            switch (s.Op)
            {
                case "strike":
                    Forge.EnumStrict<Blow>(s.S(0, ""));
                    Hurt(s, null);
                    break;
                case "flurry":
                    Forge.EnumStrict<Blow>(s.S(2, "Jab"));
                    Forge.EnumStrict<Blow>(s.S(3, "Cross"));
                    break;
                case "lift":
                    Forge.GripOf(s.S(0, "Front"));
                    break;
                case "letgo":
                    Forge.EnumStrict<Hurt>(s.S(0, "Reel"));
                    break;
                case "shot":
                    Forge.EnumStrict<ShotKind>(s.S(0, "Orb"));
                    Forge.Enum(s.KS("flight", "Straight"), Flight.Straight);
                    if (s.KS("impact", null) != null)
                    {
                        Forge.EnumStrict<ShotImpact>(s.KS("impact", null));
                    }
                    break;
                case "rain":
                    Forge.EnumStrict<RainKind>(s.S(0, "Light"));
                    break;
                case "meteor":
                    Forge.EnumStrict<SkyFall>(s.KS("kind", "Rock"));
                    break;
                case "gate":
                    Forge.EnumStrict<ShotKind>(s.KS("kind", "LightSword"));
                    break;
                case "clones":
                    Forge.EnumStrict<Blow>(s.S(1, "Cross"));
                    break;
                case "legion":
                    Forge.EnumStrict<Blow>(s.S(0, "Cross"));
                    break;
                case "charge":
                    Forge.EnumStrict<ChargeStyle>(s.S(1, "Hip"));
                    break;
                case "leaves":
                    Forge.EnumStrict<Link>(s.S(0, "Close"));
                    break;
                case "step": case "run": case "dash": case "blink": case "leap": case "land": case "crouch": case "slip":
                case "backhop": case "circle": case "rise": case "drop": case "spin": case "airwalk": case "guard": case "hold":
                case "pose": case "grab": case "slam": case "suplex": case "toss": case "beam": case "shockwave": case "dome":
                case "quake": case "pillar": case "cone": case "tornado": case "ghost": case "untouchable": case "dust":
                case "ascharge": case "cinematic": case "recover":
                case "blackhole": case "trench": case "skybeam": case "lightning": case "domain":
                    break;
                default:
                    throw new FormatException("unknown step '" + s.Op + "'");
            }
            // numbers must read
            foreach (string a in s.Args)
            {
                if (a.Length > 0 && (char.IsDigit(a[0]) || a[0] == '-' || a[0] == '.'))
                {
                    Forge.F(a);
                }
            }
            foreach (KeyValuePair<string, string> kv in s.Kw)
            {
                if (kv.Key != "hurt" && kv.Key != "flight" && kv.Key != "element" && kv.Key != "kind" && kv.Key != "impact")
                {
                    Forge.F(kv.Value);
                }
            }
        }

        /// <summary>The damage the script deals at a level (x the user's blow): what Plan weighs it by.</summary>
        public static float Total(List<ForgeStep> steps, int level)
        {
            float d = Forge.Dmg[Mathf.Clamp(level, 1, 3) - 1];
            float sum = 0f;
            foreach (ForgeStep s in steps)
            {
                if (s.From > level)
                {
                    continue;
                }
                switch (s.Op)
                {
                    case "strike": sum += s.A(1, 1f); break;
                    case "flurry": sum += s.A(1, 1f); break;
                    case "slam": case "suplex": case "toss": sum += s.A(0, 1f); break;
                    case "beam": sum += s.A(3, 2f); break;
                    case "shot": sum += s.A(1, 1f) * Mathf.Max(1f, s.K("count", 1f) * 0.6f); break;
                    case "shockwave": case "dome": case "quake": case "tornado": sum += s.A(1, 1f); break;
                    case "pillar": case "cone": sum += s.A(0, 1f); break;
                    case "rain": sum += s.A(3, 0.4f) * Mathf.Max(1f, s.A(1, 12f) * 0.15f); break;   // (12 drops by default, as Build)
                    case "clones": sum += s.A(2, 0.5f); break;
                    case "legion": sum += s.A(1, 0.6f); break;
                    case "land": sum += s.A(2, 0.5f); break;
                    case "meteor": sum += s.A(1, 1f); break;
                    case "trench": sum += s.A(2, 1f); break;   // (len, width, damage: Build reads the damage third)
                    case "blackhole": case "skybeam": case "domain": sum += s.A(1, 1f) * 1.3f; break;
                    case "lightning": sum += s.A(2, 0.5f) * Mathf.Max(1f, s.A(0, 4f) * 0.35f); break;
                    case "gate": sum += s.A(1, 0.4f) * Mathf.Max(1f, s.A(0, 6f) * 0.3f); break;
                }
            }
            return sum * d;
        }

        /// <summary>'scatter=R': the spot is the target's (or the plan's point) moved up to R tiles at random (a barrage of them).</summary>
        private static Func<ChoreoMove, Vector2> Scatter(ForgeStep s)
        {
            float r = s.K("scatter", 0f);
            if (r <= 0f)
            {
                return null;
            }
            return mm => (mm.V != null && mm.VOk ? mm.VG : mm.Point) + UnityEngine.Random.insideUnitCircle * r * mm.Kk;
        }

        public static void Build(ChoreoMove m, ForgedTechnique t, List<ForgeStep> steps)
        {
            int lv = Mathf.Clamp(m.Level, 1, 3);
            float D = Forge.Dmg[lv - 1], S = Forge.Size[lv - 1], N = Forge.Many[lv - 1];
            // a master of the school throws it bigger (its damage grows in Combat.Hit)
            int mastery = Schools.Mastery(m.A, t);
            S *= 1f + 0.07f * mastery;
            N *= 1f + 0.05f * mastery;
            Element el = t.Element;
            bool recovered = false;
            foreach (ForgeStep s in steps)
            {
                if (s.From > lv)
                {
                    continue;
                }
                recovered = false;
                switch (s.Op)
                {
                    case "step": m.StepIn(); break;
                    case "run": m.RunUp(Dest(s, ChoreoMove.ToTarget)); break;
                    case "dash": m.Dash(Dest(s, null)); break;
                    case "blink": m.Blink(Dest(s, null), s.A(0, 0.08f)); break;
                    case "leap": m.Leap(Dest(s, ChoreoMove.ToTarget), s.A(0, 1f), s.A(1, 0.4f), s.A(2, 0f)); break;
                    case "land": m.Land(s.A(0, 0f) * S, s.A(1, 0f) * S, s.A(2, 0.5f) * D, !s.Has("nofire")); break;
                    case "crouch": m.Crouch(s.A(0, 0.12f)); break;
                    case "slip": m.Slip(s.A(0, 0.5f), s.A(1, 0.1f)); break;
                    case "backhop": m.BackHop(s.A(0, 0.8f), s.A(1, 0.2f)); break;
                    case "circle": m.Circle(s.A(0, Mathf.PI)); break;
                    case "rise": m.Rise(s.A(0, 1.5f) * Mathf.Sqrt(S), s.A(1, 0.2f)); break;
                    case "drop": m.Drop(s.A(0, 0.16f)); break;
                    case "spin": m.SpinInPlace(s.A(0, 1f), s.A(1, 0.3f)); break;
                    case "airwalk": m.AirWalk(s.A(0, 2f), (int)s.A(1, 3f)); break;
                    case "guard": m.GuardUp(s.A(0, 0.6f), s.Has("hex"), el); break;
                    case "hold": m.Hold(s.A(0, 0.1f), s.Has("tremble")); break;
                    case "pose":
                        m.PoseTo(s.A(5, 0.12f), new Shape(s.A(0, 0f), s.A(1, 1f), s.A(2, 1f), s.A(3, 0f), s.A(4, 0f)), Ease.OutQuad, Tempo.Wind);
                        break;
                    case "charge":
                        m.Charge(s.A(0, 0.4f), Forge.EnumStrict<ChargeStyle>(s.S(1, "Hip")), s.A(2, 2.5f) * S, Forge.Enum(s.KS("element", null), el));
                        break;
                    case "strike":
                        m.Strike(HitOf(s, D, S));
                        break;
                    case "flurry":
                        m.Flurry(Mathf.RoundToInt(s.A(0, 8f) * N), s.A(1, 1f) * D, Forge.EnumStrict<Blow>(s.S(2, "Jab")), Forge.EnumStrict<Blow>(s.S(3, "Cross")), s.K("dur", 0.5f));
                        break;
                    case "grab": m.Grab(s.A(0, 0.15f) * D); break;
                    case "lift": m.Lift(Forge.GripOf(s.S(0, "Front")), s.A(1, 0.2f)); break;
                    case "slam":
                        m.Slam(s.A(0, 1.5f) * D, s.A(1, 1f) * S, s.A(2, 0f) * S, s.Has("headfirst"), fire: !s.Has("nofire"));
                        break;
                    case "suplex":
                        m.Suplex(s.A(0, 1.5f) * D, s.A(1, 1f) * S, s.A(2, 0f) * S, fire: !s.Has("nofire"));
                        break;
                    case "toss":
                        m.Toss(s.A(0, 1f) * D, s.A(1, 3f) * S, null, s.A(2, 0.8f) * S, s.A(3, 1.2f) * S, fire: !s.Has("nofire"));
                        break;
                    case "letgo": m.LetGo(Forge.EnumStrict<Hurt>(s.S(0, "Reel")), s.A(1, 0.6f)); break;
                    case "beam":
                        m.Fire(new BeamSpec
                        {
                            Length = s.A(0, 10f) * S,
                            Width = s.A(1, 5f),
                            Life = s.A(2, 0.55f),
                            Mult = s.A(3, 2f) * D,
                            Push = s.K("push", 2f),
                            Trench = s.K("trench", 0.8f),
                            AlwaysWins = s.Has("wins"),
                            Element = Forge.Enum(s.KS("element", null), el)
                        });
                        break;
                    case "shot":
                        m.Shoot(new ShotSpec
                        {
                            Kind = Forge.EnumStrict<ShotKind>(s.S(0, "Orb")),
                            Flight = Forge.Enum(s.KS("flight", "Straight"), Flight.Straight),
                            Speed = s.K("speed", 18f),
                            Range = s.K("range", 12f) * S,
                            Size = s.K("size", 1f) * Mathf.Sqrt(S),
                            Mult = s.A(1, 1f) * D,
                            LunarCrescent = s.K("lunar",0)>0,
                            PierceDecay = s.K("decay",1),
                            PierceFloor = s.K("floor",1),
                            Hurt = Forge.Enum(s.KS("hurt", "Reel"), Sakuga.Hurt.Reel),
                            Push = s.K("push", 0.8f),
                            Pierce = (int)s.K("pierce", 0f),
                            Crater = s.K("crater", 0f) * S,
                            Area = s.K("area", 0f) * S,
                            Element = Forge.Enum(s.KS("element", null), el),
                            Impact = Forge.Enum(s.KS("impact", null), ShotImpact.Auto)
                          }, ShotCount(s,N), s.K("spread", 0f), s.K("gap", 0.06f));
                        break;
                    case "shockwave":
                        m.Shockwave(s.A(0, 3f) * S, s.A(1, 0.8f) * D, Forge.Enum(s.KS("hurt", "Knockdown"), Sakuga.Hurt.Knockdown), s.K("push", 0.8f));
                        break;
                    case "dome": m.Dome(s.A(0, 3f) * S, s.A(1, 1f) * D); break;
                    case "quake": m.Quake(s.A(0, 4f) * S, s.A(1, 0.8f) * D, (int)s.K("cracks", 6f)); break;
                    case "pillar": m.Pillar(s.A(0, 1f) * D, s.K("height", 1.6f) * Mathf.Sqrt(S), s.K("lift", 2.2f), 0.18f, s.Has("spike")); break;
                    case "cone": m.Cone(s.A(0, 0.8f) * D, s.A(1, 40f), s.A(2, 4f) * S, s.K("dur", 0.5f), s.K("push", 1.2f), s.Has("blades")); break;
                    case "tornado": m.Tornado(s.A(0, 3f) * S, s.A(1, 1f) * D, s.A(2, 1f), s.A(3, 3f)); break;
                    case "rain":
                        m.Rain(Forge.EnumStrict<RainKind>(s.S(0, "Light")), Mathf.RoundToInt(s.A(1, 12f) * N), s.A(2, 3f) * S, s.A(3, 0.4f) * D, s.K("dur", 1f));
                        break;
                    case "clones":
                        {
                            int n = Mathf.Max(1, Mathf.RoundToInt(s.A(0, 3f) * N));
                            Blow b = Forge.EnumStrict<Blow>(s.S(1, "Cross"));
                            float each = s.A(2, 0.5f) * D / n;
                            m.Clones(n, 1.2f, (mm, c, i) =>
                            {
                                Actor v = mm.V;
                                if (v != null)
                                {
                                    c.Wait(0.07f * i).Strike(v, b, each).Vanish();
                                }
                                else
                                {
                                    c.Vanish();
                                }
                            }).WaitClones();
                        }
                        break;
                    case "legion":
                        {
                            Blow b = Forge.EnumStrict<Blow>(s.S(0, "Cross"));
                            float r = s.K("r", 7f) * S;
                            m.Legion(mm => Combat.FoesAround(mm.A, mm.A.current_position, r), b, s.A(1, 0.6f) * D, (int)(s.K("max", 6f) * N)).WaitClones();
                        }
                        break;
                    case "meteor": m.Meteor(s.A(0, 3f) * S, s.A(1, 1f) * D, s.K("size", 1f) * Mathf.Sqrt(S), s.Has("self") ? (Func<ChoreoMove, Vector2>)(mm => mm.Pose.Ground + mm.Dir * 2.5f * mm.Kk) : Scatter(s), Forge.Enum(s.KS("kind", null), SkyFall.Rock)); break;
                    case "blackhole": m.BlackHole(s.A(0, 3f) * S, s.A(1, 1f) * D, s.K("dur", 1.2f), Scatter(s), s.K("terrain",1)>0); break;
                    case "trench": m.Trench(s.A(0, 8f) * S, s.A(1, 0.8f) * Mathf.Sqrt(S), s.A(2, 1f) * D, s.K("speed", 16f), s.Has("cut")); break;
                    case "skybeam": m.SkyBeam(s.A(0, 2f) * S, s.A(1, 1f) * D, s.K("dur", 0.9f), Scatter(s), s.K("terrain",1)>0); break;
                    case "lightning": m.Lightning(Mathf.RoundToInt(s.A(0, 4f) * N), s.A(1, 4f) * S, s.A(2, 0.5f) * D, s.K("dur", 0.9f)); break;
                    case "gate": m.SwordGate(Mathf.RoundToInt(s.A(0, 6f) * N), s.A(1, 0.4f) * D, s.K("range", 10f) * S, Forge.Enum(s.KS("kind", null), ShotKind.LightSword)); break;
                    case "domain": m.SlashDomain(s.A(0, 3f) * S, s.A(1, 1f) * D, s.K("dur", 1.2f)); break;
                    case "ghost": m.Ghosting(0.03f, s.Has("lines")); break;
                    case "untouchable": m.Untouchable(); break;
                    case "dust": m.Dusting(); break;
                    case "ascharge": m.AsCharge(); break;
                    case "leaves": m.Leaves(Forge.EnumStrict<Link>(s.S(0, "Close"))); break;
                    case "cinematic":
                        if (t.Scale == Scale.Ultimate)
                        {
                            m.OpenCinematic(8f, 0.4f, s.S(0, "sakuga_title_" + t.Id), 0.4f, 0.6f);
                        }
                        break;
                    case "recover":
                        m.Recover(s.A(0, 0.22f));
                        recovered = true;
                        break;
                }
            }
            if (!recovered)
            {
                m.Recover();
            }
        }

        private static Func<ChoreoMove, Vector2> Dest(ForgeStep s, Func<ChoreoMove, Vector2> d)
        {
            if (s.Has("behind"))
            {
                return ChoreoMove.BehindTarget;
            }
            if (s.Has("point"))
            {
                return ChoreoMove.ToPoint;
            }
            if (s.Has("target"))
            {
                return ChoreoMove.ToTarget;
            }
            return d;
        }

        private static Hurt? Hurt(ForgeStep s, Hurt? d)
        {
            string h = s.KS("hurt", null);
            return h == null ? d : Forge.EnumStrict<Hurt>(h);
        }

        private static Hit HitOf(ForgeStep s, float D, float S)
        {
            Hit h = new Hit(Forge.EnumStrict<Blow>(s.S(0, "Jab")), s.A(1, 1f) * D)
            {
                Hurt = Hurt(s, null),
                Push = s.K("push", -1f),
                Lift = s.K("lift", -1f),
                Daze = s.K("daze", 0f),
                Crater = s.K("crater", 0f) * S,
                Area = s.K("area", 0f) * S,
                AreaMult = s.K("areamult", 0.5f),
                Power = s.K("power", 1f),
                Finisher = s.Has("finisher"),
                Air = s.Has("air"),
                Charged = s.Has("charged"),
                Fire = !s.Has("nofire")
            };
            float wind = s.K("wind", 0f);
            if (wind > 0f)
            {
                h.WindScale = wind;
            }
            return h;
        }
    }
}
