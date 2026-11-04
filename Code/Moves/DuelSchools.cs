using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// Duels fought in the duelists' schools: each school leans toward the exchanges that are its way
    /// of fighting (a swordsman binds blades, a kicker takes it to the air, an earth breaker shakes the
    /// ground, a phantom feints, a hunter chases, a ki artist goes to the sky) and away from the ones
    /// that are not. Two duelists' tastes meet in the middle, so two swordsmen lock blades often and a
    /// swordsman against an earth breaker fights a different duel than two kickers.
    /// </summary>
    internal static class DuelSchools
    {
        private static readonly Dictionary<School, Dictionary<string, float>> Taste = new Dictionary<School, Dictionary<string, float>>
        {
            [School.IronFist] = new Dictionary<string, float>
            {
                ["salvo"] = 0.9f, ["aura_clash"] = 1.5f, ["blitz"] = 1.2f, ["flurry"] = 2.2f, ["blade_lock"] = 1.2f, ["ground_shaker"] = 1.3f, ["relentless"] = 1.6f, ["aerial"] = 0.8f, ["chase"] = 0.8f,
                ["skyfall"] = 1.5f, ["mountain_breaker"] = 1.6f, ["tectonic"] = 1.1f, ["sky_battle"] = 0.8f, ["heavens"] = 0.7f, ["sky"] = 0.7f
            },
            [School.TempestLeg] = new Dictionary<string, float>
            {
                ["salvo"] = 1.3f, ["aura_clash"] = 0.9f, ["blitz"] = 1.7f, ["aerial"] = 2.4f, ["chase"] = 1.4f, ["sky_battle"] = 1.7f, ["sky"] = 1.4f, ["flurry"] = 1.3f, ["blade_lock"] = 0.5f, ["ground_shaker"] = 0.8f,
                ["heavens"] = 1.4f, ["skyfall"] = 1.1f, ["tectonic"] = 0.5f, ["mountain_breaker"] = 0.7f
            },
            [School.ThousandBlades] = new Dictionary<string, float>
            {
                ["salvo"] = 1.2f, ["aura_clash"] = 1f, ["blitz"] = 1.5f, ["blade_lock"] = 2.6f, ["feint"] = 1.5f, ["flurry"] = 1.3f, ["ground_shaker"] = 0.5f, ["aerial"] = 0.8f,
                // (a swordsman's duel is steel on steel: no meteors, no breaking the land with its body)
                ["skyfall"] = 0.35f, ["sky_battle"] = 0.45f, ["heavens"] = 0.5f, ["sky"] = 0.6f, ["tectonic"] = 0.3f, ["mountain_breaker"] = 0.4f
            },
            [School.KiArts] = new Dictionary<string, float>
            {
                ["salvo"] = 1.9f, ["aura_clash"] = 1.7f, ["blitz"] = 0.9f, ["sky"] = 1.8f, ["sky_battle"] = 2f, ["skyfall"] = 1.5f, ["aerial"] = 1.3f, ["blade_lock"] = 0.4f, ["flurry"] = 0.7f, ["ground_shaker"] = 0.8f,
                ["heavens"] = 1.8f, ["tectonic"] = 0.5f, ["mountain_breaker"] = 0.5f
            },
            [School.EarthBreaker] = new Dictionary<string, float>
            {
                ["salvo"] = 1.1f, ["aura_clash"] = 1.5f, ["blitz"] = 0.4f, ["ground_shaker"] = 2.6f, ["tectonic"] = 2.4f, ["mountain_breaker"] = 2.2f, ["aerial"] = 0.5f, ["chase"] = 0.5f, ["feint"] = 0.4f,
                ["sky_battle"] = 0.4f, ["heavens"] = 0.5f, ["sky"] = 0.5f, ["skyfall"] = 0.9f, ["blade_lock"] = 0.6f
            },
            [School.PhantomStep] = new Dictionary<string, float>
            {
                ["salvo"] = 1f, ["aura_clash"] = 0.7f, ["blitz"] = 2.2f, ["feint"] = 2.6f, ["chase"] = 1.8f, ["relentless"] = 1.5f, ["blade_lock"] = 0.5f, ["ground_shaker"] = 0.5f, ["tectonic"] = 0.7f,
                ["skyfall"] = 0.5f, ["sky_battle"] = 0.6f, ["heavens"] = 0.6f, ["mountain_breaker"] = 0.5f
            },
            [School.HuntersWay] = new Dictionary<string, float>
            {
                ["salvo"] = 1.7f, ["aura_clash"] = 0.6f, ["blitz"] = 1f, ["chase"] = 2.4f, ["ground_shaker"] = 1.2f, ["feint"] = 1.1f, ["skyfall"] = 1.4f, ["blade_lock"] = 1.1f, ["sky_battle"] = 0.8f,
                ["heavens"] = 0.6f, ["tectonic"] = 0.8f, ["mountain_breaker"] = 1.1f
            },
        };

        [SakugaInit]
        private static void Init()
        {
            Duels.Weighers.Add(Weigh);
        }

        private static float Weigh(Duel d, Exchange ex)
        {
            if (!Settings.Schools)
            {
                return 1f;
            }
            return Mathf.Sqrt(Of(d.A, ex.Id) * Of(d.B, ex.Id));
        }

        /// <summary>One duelist's taste for an exchange: the mean of its schools' (1 without a school).</summary>
        private static float Of(Actor a, string id)
        {
            List<School> schools = Schools.Of(a);
            if (schools.Count == 0)
            {
                return 1f;
            }
            float sum = 0f;
            foreach (School s in schools)
            {
                sum += Taste.TryGetValue(s, out Dictionary<string, float> t) && t.TryGetValue(id, out float k) ? k : 1f;
            }
            return sum / schools.Count;
        }
    }

    /// <summary>
    /// SIGNATURE: one duelist seizes the moment for its school's own great technique. They square up
    /// a stride and a half apart; the one with the initiative (the stronger, more often) shows its
    /// school's tell (a blade's glint and steel sparks, ki gathering in a glow, the ground cracking
    /// under a stamp, wind tearing round its legs, an afterimage peeling off, a hunter's mark on its
    /// prey, a fist drawn back trembling), the other braces; then the duel lets go and the technique
    /// itself plays (Duel.ReleaseBout forces it): a sword gate between two swordsmen, a fault split
    /// between earth breakers, a pillar of heaven from a ki artist. Duels between schools become the
    /// schools' own fights.
    /// </summary>
    internal sealed class SignatureBout : Exchange
    {
        private bool _wIsA;
        private Duelist _w, _l;
        private Vector2 _ws, _ls, _w0, _l0;
        internal Technique Sig;
        internal int SigLevel;
        internal Actor Winner, Loser;
        private School _school;

        public override string Id => "signature";

        public override Form Forms => Form.Heavy;

        public override float Weight()
        {
            return D.TA >= Tier.Expert || D.TB >= Tier.Expert ? 1.6f : 0f;
        }

        /// <summary>The great techniques of a's own schools it could throw right now (Heavy and up, ready, affordable).</summary>
        /// <summary>The ones it could throw, best first (the release tries them in turn until one really starts).</summary>
        internal readonly List<KeyValuePair<Technique, int>> Candidates = new List<KeyValuePair<Technique, int>>();

        internal static Technique Pick(Actor a, out int level, List<KeyValuePair<Technique, int>> all = null)
        {
            level = 0;
            all?.Clear();
            if (!Fighters.TryGet(a, out Fighter f))
            {
                return null;
            }
            List<School> mine = Schools.Of(a);
            Technique best = null;
            float bw = 0f;
            List<KeyValuePair<float, KeyValuePair<Technique, int>>> ranked = new List<KeyValuePair<float, KeyValuePair<Technique, int>>>();
            foreach (Technique t in Techniques.All)
            {
                if (t.Reactive || t.Team || t.GodAbility || !(mine.Contains(t.School) || t.School == School.None && t.Entry >= Tier.God))
                {
                    continue;
                }
                int lv = Techniques.Level(a, t);
                if (lv <= 0 || !f.Ready(t) || !Mana.Has(a, t, lv, false))
                {
                    continue;
                }
                Scale s = t.ScaleAt(lv);
                if (s < Scale.Heavy)
                {
                    continue;
                }
                float w = (s >= Scale.Ultimate ? 2f : 1f) * Random.Range(0.5f, 1.5f);
                ranked.Add(new KeyValuePair<float, KeyValuePair<Technique, int>>(w, new KeyValuePair<Technique, int>(t, lv)));
                if (w > bw)
                {
                    bw = w;
                    best = t;
                    level = lv;
                }
            }
            if (all != null)
            {
                ranked.Sort((x, y) => y.Key.CompareTo(x.Key));
                foreach (var kv in ranked)
                {
                    all.Add(kv.Value);
                }
            }
            return best;
        }

        public override bool Plan()
        {
            float pa = Mathf.Max(0.1f, D.PowerA), pb = Mathf.Max(0.1f, D.PowerB);
            _wIsA = Random.value < pa / (pa + pb);
            Actor w = _wIsA ? D.A : D.B;
            Sig = Pick(w, out SigLevel, Candidates);
            if (Sig == null)
            {
                _wIsA = !_wIsA;
                w = _wIsA ? D.A : D.B;
                Sig = Pick(w, out SigLevel, Candidates);
                if (Sig == null)
                {
                    return false;
                }
            }
            Winner = w;
            Loser = _wIsA ? D.B : D.A;
            _school = Sig.School != School.None ? Sig.School : (Schools.Of(w).Count > 0 ? Schools.Of(w)[0] : School.None);
            _w0 = _wIsA ? A0 : B0;
            _l0 = _wIsA ? B0 : A0;
            if (!DuelGeo.Stance(_w0, _l0, 1.6f * K, out Vector2 sw, out Vector2 sl))
            {
                return false;
            }
            _ws = sw;
            _ls = sl;
            return true;
        }

        public override void Start()
        {
            _w = _wIsA ? P : Q;
            _l = _wIsA ? Q : P;
        }

        protected override void Tick(float dt)
        {
            float px = Px;
            switch (Ph)
            {
                case 0:
                    {
                        float k = Mathf.Clamp01(Pt / 0.16f);
                        Reposition(_w, _w0, _ws, k, _ls);
                        Reposition(_l, _l0, _ls, k, _ws);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(1);
                        }
                        break;
                    }
                case 1:
                    {
                        float k = Mathf.Clamp01(Pt / 0.45f);
                        _w.Pose.Ground = _ws;
                        _l.Pose.Ground = _ls;
                        FaceEach();
                        _w.Pose.Rot = -10f * k;
                        _w.Pose.SY = 1f - 0.1f * k;
                        _w.Pose.SX = 1f + 0.06f * k;
                        _w.Pose.Nudge = k > 0.5f ? Choreo.Tremble(T, 0.6f) : Vector2.zero;
                        Guard(_l, T, 0f, 1.3f);
                        if (Enter() && Vis)
                        {
                            Tell(px);
                        }
                        if (Vis && ((int)(Pt * 30f) & 3) == 0)
                        {
                            ChoreoFx.Motes(_w.BodyPos(), px, 2, 12f, 0.3f, _w.Pal.Core, _w.Pal.Core, FxLayer.Over);
                        }
                        if (k >= 1f)
                        {
                            Upper = _w;
                            Opening = Sakuga.Link.Close;
                            Done = true;
                        }
                        break;
                    }
            }
        }

        /// <summary>The school's tell: what a fighter of this school does in the breath before its great technique.</summary>
        private void Tell(float px)
        {
            Vector2 body = _w.BodyPos();
            Vector2 feet = _w.Pose.Ground;
            Color c = _w.Pal.Core;
            Fx.Ring(feet, px, 0.45f, 2f, 16f * K, 2, 1, FxLayer.Ground, 4, Art.Ground, 2f, Fx.WithA(Color.white, 0.85f), Fx.WithA(c, 0f));
            switch (_school)
            {
                case School.ThousandBlades:
                    Fx.Glint(body + new Vector2(_w.Fwd * 3f * px, 2f * px), px, 0.25f, Color.white, 1f);
                    Kit.Sparks(body, px, 6, 30f, 80f, new Color(1f, 0.95f, 0.7f, 1f));
                    break;
                case School.KiArts:
                    ChoreoFx.Motes(body, px, 14, 22f * K, 0.4f, Color.white, c, FxLayer.Over);
                    Kit.Flash(body, px, 6, 0.3f, Fx.WithA(c, 0.8f));
                    break;
                case School.EarthBreaker:
                    Kit.Crater(feet, px * K * 0.6f, Combat.TileAt(feet), false, 3f, true, c);
                    Spec.Rocks(feet, px, 6, 50f, K);
                    break;
                case School.TempestLeg:
                    Kit.SpeedLines(feet + new Vector2(0f, 2f * px), Vector2.right * _w.Fwd, px, 5, 6f, 0.3f, Fx.WithA(Color.white, 0.8f));
                    break;
                case School.PhantomStep:
                    Fx.Afterimage(_w, 0.35f, Fx.WithA(c, 0.7f), Fx.WithA(c, 0f));
                    break;
                case School.HuntersWay:
                    {
                        Vector2 at = _l.BodyPos();
                        float m = 3f * px * K;
                        Kit.Line(at + new Vector2(-m, -m), at + new Vector2(m, m), px, 0.4f, 1, FxLayer.Top, 18, Fx.WithA(c, 0.95f), Fx.WithA(c, 0f));
                        Kit.Line(at + new Vector2(-m, m), at + new Vector2(m, -m), px, 0.4f, 1, FxLayer.Top, 18, Fx.WithA(c, 0.95f), Fx.WithA(c, 0f));
                        break;
                    }
                default:
                    Kit.DustRing(feet, px, Combat.TileAt(feet), 6, 26f, 0.4f, K);
                    Fx.Glint(body + new Vector2(_w.Fwd * 2f * px, 1f * px), px, 0.2f, c, 0.9f);
                    break;
            }
            Choreo.Sound("event:/SFX/POWERS/Powerup", Combat.TileAt(feet), 0.1f);
        }
    }
}
