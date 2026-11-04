using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>An exchange that ends a duel (the Final and its variants): did its blow kill?</summary>
    internal interface IFinale
    {
        bool Killed { get; }
    }

    /// <summary>
    /// THE ENDINGS. A duel no longer always ends on the same dash-through: when the end comes
    /// (Duel.FinalDue) one of several finales is chosen by the duelists' schools, tiers and how the
    /// fight stands, never the one a fighter ended its last duel with:
    ///   Final       the dash-through, back to back, the loser falls (the swordsman's and the phantom's)
    ///   LastClash   both gather everything in their school's way and charge: a collision in the middle,
    ///               a crater, both schools' marks bursting into each other; the loser is blown away
    ///   SkyFinish   the winner launches the loser into the air, follows it up, strikes it three times
    ///               up there and spikes it down into a crater
    ///   Overwhelm   a gulf in power: one contemptuous blow and the loser is sent across the world
    ///   Signature   the winner's school's own finishing art: the iron fist's last punch ploughing the
    ///               loser through the land; the tempest's tornado kick; the sword's thousand cuts that
    ///               open as it sheathes; ki's point-blank burst; the earth breaker's grave (leap, slam,
    ///               crater); the phantom's execution from six shadows; the hunter's kill (javelin, pounce)
    /// Each one ends on Duel.FinishingBlow like the Final; Grandmasters and Gods wreck the land far more.
    /// </summary>
    internal static class Finales
    {
        /// <summary>The finale each fighter's last duel ended with (its next leans away from it).</summary>
        private static readonly Dictionary<long, string> _last = new Dictionary<long, string>();

        public static Exchange Choose(Duel d)
        {
            Clash c = d.Roll();
            // the one at the end of its strength is the one who falls (an upset now and then)
            bool lowA = d.HpA < 0.25f, lowB = d.HpB < 0.25f;
            if (lowA != lowB && Random.value < 0.85f)
            {
                c.AWins = lowB;
                if (c.Kind == ClashKind.Even)
                {
                    c.Kind = ClashKind.Win;
                }
            }
            string prev = null;
            _last.TryGetValue(d.IdA, out string pa);
            _last.TryGetValue(d.IdB, out string pb);
            School sa = Schools.PrimaryOf(d.A), sb = Schools.PrimaryOf(d.B);
            School ws = c.AWins ? sa : sb;
            List<KeyValuePair<Exchange, float>> pool = new List<KeyValuePair<Exchange, float>>
            {
                new KeyValuePair<Exchange, float>(new Final(c), ws == School.ThousandBlades || ws == School.PhantomStep ? 1.2f : 0.7f),
                new KeyValuePair<Exchange, float>(new LastClash(c), d.Timed || c.Ratio < 1.6f ? 1.4f : 0.9f),
                new KeyValuePair<Exchange, float>(new SkyFinish(c), ws == School.TempestLeg || ws == School.IronFist || ws == School.KiArts ? 1.3f : 0.8f),
                new KeyValuePair<Exchange, float>(new Signature(c), 1.8f),
            };
            if (c.Ratio >= 2.2f)
            {
                pool.Add(new KeyValuePair<Exchange, float>(new Overwhelm(c), 2.5f));
            }
            float total = 0f;
            List<KeyValuePair<Exchange, float>> ok = new List<KeyValuePair<Exchange, float>>();
            foreach (KeyValuePair<Exchange, float> kv in pool)
            {
                Exchange ex = kv.Key;
                ex.Bind(d);
                string name = Name(ex);
                float w = kv.Value * (name == pa || name == pb ? 0.25f : 1f) * Random.Range(0.8f, 1.2f);
                if (!ex.Plan())
                {
                    continue;
                }
                ok.Add(new KeyValuePair<Exchange, float>(ex, w));
                total += w;
            }
            if (ok.Count == 0)
            {
                return null;
            }
            float r = Random.value * total;
            Exchange pick = ok[ok.Count - 1].Key;
            foreach (KeyValuePair<Exchange, float> kv in ok)
            {
                r -= kv.Value;
                if (r <= 0f)
                {
                    pick = kv.Key;
                    break;
                }
            }
            prev = Name(pick);
            _last[d.IdA] = prev;
            _last[d.IdB] = prev;
            if (_last.Count > 2000)
            {
                _last.Clear();
            }
            Duels.Trace("duel " + d.IdA + "/" + d.IdB + " finale: " + prev);
            return pick;
        }

        public static string Name(Exchange ex)
        {
            return ex is FinaleBase f ? f.Variant : "final";
        }
    }

    /// <summary>What every new finale shares: who falls, the finishing blow, the fall, the winner's last pose.</summary>
    internal abstract class FinaleBase : Exchange, IFinale
    {
        protected readonly Clash C;
        protected Duelist W, Lz;
        protected bool LoserIsA, Struck, KilledIt;
        protected School WS;
        protected Hue WH;
        protected float GatherT;

        protected FinaleBase(Clash c)
        {
            C = c;
            LoserIsA = !c.AWins;
        }

        public override string Id => "final";

        public abstract string Variant { get; }

        public override Form Forms => Form.Ultimate | Form.Strike;

        public override bool Solo => Struck;

        public bool Killed => KilledIt;

        public override void Start()
        {
            Lz = LoserIsA ? P : Q;
            W = LoserIsA ? Q : P;
            WS = SchoolStyle.Of(W);
            WH = Hue.For(W.A, WS == School.None ? School.None : WS, Mathf.Clamp(L, 1, 3));
        }

        /// <summary>Two stances 'gap' tiles apart round where they stand (false: no room).</summary>
        protected bool Places(float gap, out Vector2 sa, out Vector2 sb)
        {
            if (DuelGeo.Stance(A0, B0, gap * K, out sa, out sb))
            {
                return true;
            }
            return DuelGeo.Stance(A0, B0, 0.9f * K, out sa, out sb);
        }

        /// <summary>The blow that ends it (D.FinishingBlow), the loser's flash, the hit-stop.</summary>
        protected void Kill(Vector2 at, float stop = 0.12f)
        {
            if (Struck)
            {
                return;
            }
            HitResult r = D.FinishingBlow(W, Lz);
            Struck = true;
            KilledIt = r.Killed || !Lz.A.hasHealth();
            Color own = Tiers.Of(Lz.A) != Tier.None ? Tiers.Color(Tiers.Of(Lz.A)) : Color.white;
            Lz.Flash(0.12f, own);
            D.Sound("event:/SFX/HIT/HitFlesh");
            D.Sound("event:/SFX/HIT/HitGeneric");
            if (Vis && L >= 2)
            {
                Kit.ImpactFrame(at, Px, L >= 3 ? 0.9f : 0.55f, W.Pal, r.Crit);
                Kit.Shake(at, D.Gods ? 0.45f : 0.22f, D.Gods ? 0.014f : 0.007f);
            }
            if (L >= 3)
            {
                Kit.Stop(at, stop);
            }
            else
            {
                Stop(0.08f);
            }
            if (!KilledIt)
            {
                D.KneelUsed = true;
            }
        }

        /// <summary>The world takes it: a crater at 'at' sized to the tiers (a God's tears the land open round it).</summary>
        protected void Crater(Vector2 at, float tiles, int force, bool cracks = true)
        {
            float big = D.Gods ? 2.2f : D.Grand ? 1.5f : L >= 3 ? 1.15f : 1f;
            Wreck(at, tiles * big, force + (D.Grand ? 1 : 0), W);
            if (!Vis)
            {
                return;
            }
            WorldTile t = Combat.TileAt(at);
            DuelFx.Smash(at, Px, t, true, K * big);
            DuelFx.GroundRing(at, Px, 2f, tiles * big / Px * 1.4f, 0.4f, 3, 1, Color.white, Fx.WithA(WH.Main, 0.7f), Fx.WithA(WH.Edge, 0f));
            if (cracks && L >= 2)
            {
                DuelFx.Fissures(at, Px, t, tiles * big * 1.6f, D.Gods ? 9 : D.Grand ? 7 : 5, WH.Main);
            }
            if (D.Gods)
            {
                CineKit.ViewFlash(Fx.WithA(Color.Lerp(WH.Main, Color.white, 0.5f), 0.5f), 0.25f);
                // the God's last blow splits the land in lines running out from it
                for (int i = 0; i < 3; i++)
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    D.Furrow(at + dir * 0.6f * K, Combat.Clamp(at + dir * Random.Range(5f, 9f) * K), 0.7f, 2, W.A);
                }
            }
        }

        /// <summary>The fallen buckles (the game lays it down once its death is done); a survivor staggers.</summary>
        protected void Fallen(float t)
        {
            if (Lz == null || Lz.Finished)
            {
                return;
            }
            if (KilledIt)
            {
                float k = Mathf.Clamp01(t / 0.25f);
                Lz.Pose.SY = Mathf.Lerp(0.9f, 0.76f, k);
                Lz.Pose.SX = 1.06f;
                Lz.Pose.Rot = Mathf.Lerp(20f, 70f, Anim.OutQuad(k));
                Lz.Pose.Height = Mathf.Max(0f, Lz.Pose.Height - 4f * Time.deltaTime);
            }
            else
            {
                float a = Anim.Spring(t, 4f, 12f);
                Lz.Pose.Rot = -20f * a;
                Lz.Pose.SY = 1f - 0.1f * Mathf.Abs(a);
                Lz.Pose.Height = 0f;
            }
        }

        /// <summary>The winner's last beat: the spring of its stance and a glint.</summary>
        protected void Victor(float t, bool glint)
        {
            if (glint && Vis)
            {
                Fx.Glint(W.BodyPos() + new Vector2(W.Fwd * 2f * W.Px, W.Pose.PivotUp * 1.5f * W.Px), W.Px, 0.28f, W.Pal.Core, 1.15f);
            }
            float amp = Anim.Spring(t, 7f, 14f);
            W.Pose.Rot = 16f * amp;
            W.Pose.SX = 1f + 0.08f * amp;
            W.Pose.SY = 1f - 0.08f * amp;
            W.Pose.Height = Mathf.Max(0f, W.Pose.Height - 6f * Time.deltaTime);
        }

        /// <summary>One beat of gathering power in the school's way at 'm' (0..1 through it).</summary>
        protected void Gather(Duelist m, float k)
        {
            if (!Vis)
            {
                return;
            }
            GatherT -= Time.deltaTime;
            School s = SchoolStyle.Of(m);
            Hue h = Hue.For(m.A, s, Mathf.Clamp(L, 1, 3));
            Vector2 hand = m.BodyPos() + new Vector2(m.Fwd * 2.5f * m.Px, 0f);
            if (SchoolStyle.Orb(s))
            {
                Particle o = Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt((1f + 4f * k) * m.K), 1, 14), 1f, 0.4f), hand, m.Px, 0.05f, FxLayer.Top, 22);
                o?.Color(Color.white, Fx.WithA(h.Main, 0.8f));
                ChoreoFx.Motes(hand, m.Px, 2, 10f, 0.12f, h.Core, h.Edge, FxLayer.Top);
            }
            else
            {
                SchoolStyle.Gather(s, hand, m.BodyPos(), m.Pose.Ground, m.Px, m.K, k, h, null);
            }
            if (Random.value < 0.15f + 0.2f * k)
            {
                Trails.Ghost(m.A, 0.12f, Fx.WithA(h.Main, 0.5f), Fx.WithA(h.Edge, 0f));
            }
        }

        /// <summary>A body's crouch (anticipation) at k.</summary>
        protected static void Crouch(Duelist m, float k, float depth = 1f)
        {
            float e = Anim.OutQuad(Mathf.Clamp01(k));
            m.Pose.Rot = 12f * e * depth;
            m.Pose.SX = 1f + 0.12f * e * depth;
            m.Pose.SY = 1f - 0.16f * e * depth;
            m.Pose.Height = 0f;
        }
    }

    // ================================================================ LAST CLASH ==

    /// <summary>Both gather everything and charge: they collide in the middle, and only one is left standing.</summary>
    internal sealed class LastClash : FinaleBase
    {
        private const int Set = 0, Charge = 1, Rush = 2, Impact = 3, Blown = 4, End = 5;
        private Vector2 _sa, _sb, _mid, _land;
        private Knock _fly, _back;

        public LastClash(Clash c) : base(c)
        {
        }

        public override string Variant => "last clash";

        public override bool Plan()
        {
            if (!Places(3.4f, out _sa, out _sb))
            {
                return false;
            }
            _mid = (_sa + _sb) * 0.5f;
            Vector2 away = (LoserIsA ? _sa - _sb : _sb - _sa).normalized;
            // (not so far the duel loses them: a God's Last Light may yet bring the loser back into it)
            float far = D.Gods ? 6f : D.Grand ? 5f : L >= 3 ? 4.5f : 3.5f;
            if (!DuelGeo.Far(_mid, away, far * 0.7f * K, far * K, 0.9f, out _land))
            {
                _land = LoserIsA ? _sa : _sb;
            }
            return true;
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / 0.2f);
                        Reposition(P, A0, _sa, k, _sb);
                        Reposition(Q, B0, _sb, k, _sa);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(Charge);
                        }
                        break;
                    }
                case Charge:
                    {
                        float dur = Rest(0.9f) + 0.25f;
                        float k = Mathf.Clamp01(Pt / dur);
                        Crouch(P, k);
                        Crouch(Q, k);
                        Gather(P, k);
                        Gather(Q, k);
                        if (Vis && Random.value < 0.25f)
                        {
                            DuelFx.Wind(_sa, _sb, Px, Combat.TileAt(_mid), new Color(1f, 1f, 1f, 0.4f));
                        }
                        if (L >= 2 && Vis && Random.value < 0.1f + 0.2f * k)
                        {
                            // the ground between them can't bear it
                            DuelFx.Fissure(_mid + Random.insideUnitCircle * 0.5f * K, _mid + Random.insideUnitCircle * 1.6f * K, Px, Combat.TileAt(_mid), WH.Main, 1);
                        }
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Next(Rush);
                        }
                        break;
                    }
                case Rush:
                    {
                        float k = Mathf.Clamp01(Pt / 0.13f);
                        DashPose(P, _sa, _mid - (_mid - _sa).normalized * 0.35f * K, k);
                        DashPose(Q, _sb, _mid - (_mid - _sb).normalized * 0.35f * K, k);
                        Trail(P, ref GhostA, dt, 0.015f, 0.2f);
                        Trail(Q, ref GhostB, dt, 0.015f, 0.2f);
                        if (k >= 1f)
                        {
                            Next(Impact);
                        }
                        break;
                    }
                case Impact:
                    {
                        if (Enter())
                        {
                            Vector2 at = Contact();
                            if (Vis)
                            {
                                Hue ha = Hue.For(P.A, SchoolStyle.Of(P), Mathf.Clamp(L, 1, 3)), hb = Hue.For(Q.A, SchoolStyle.Of(Q), Mathf.Clamp(L, 1, 3));
                                DuelFx.Starburst(at, Px, (D.Gods ? 4f : D.Grand ? 2.6f : 1.6f) * K, Color.white, ha.Main, hb.Main, 0.16f);
                                SchoolStyle.Emblem(SchoolStyle.Of(P), at, _mid, Axis(), Px, K * 1.5f, 1.6f, ha);
                                SchoolStyle.Emblem(SchoolStyle.Of(Q), at, _mid, -Axis(), Px, K * 1.5f, 1.6f, hb);
                                DuelFx.AirRing(at, Px, 2f, (D.Gods ? 60f : 30f) * K, 0.35f, 2, Color.white, Fx.WithA(ha.Main, 0.6f), Fx.WithA(hb.Main, 0f));
                            }
                            Crater(_mid, 1.6f, 2);
                            D.Sound("event:/SFX/EXPLOSIONS/ExplosionBig");
                            Stop(0.12f);
                            Kit.Stop(at, L >= 3 ? 0.14f : 0.06f);
                            Vector2 lo = Lz.Pose.Ground;
                            _fly = Knock.Fly(lo, _land, D.Gods ? 0.75f : 0.55f, (D.Gods ? 3.2f : 2f) * K, (LoserIsA ? -1f : 1f) * 600f, 0.2f);
                            Vector2 wo = W.Pose.Ground;
                            _back = Knock.Skid(wo, Combat.Clamp(wo - (_land - lo).normalized * 0.5f * K), 0.3f, -14f, 0.2f);
                        }
                        Next(Blown);
                        break;
                    }
                case Blown:
                    {
                        bool done = _fly.Step(Lz, dt);
                        _back.Step(W, dt);
                        if (Vis && _fly.Moving && Random.value < 0.6f)
                        {
                            Trails.Ghost(Lz.A, 0.14f, Fx.WithA(WH.Main, 0.5f), Fx.WithA(WH.Edge, 0f));
                        }
                        if (_fly.Landed && !Struck)
                        {
                            Crater(_land, 1.1f, 2);
                            Kill(Lz.BodyPos());
                        }
                        if (done || Pt > 1.5f)
                        {
                            Next(End);
                        }
                        break;
                    }
                default:
                    Victor(Pt, Pt < 0.02f);
                    Fallen(Pt);
                    if (Pt >= 0.5f)
                    {
                        Done = true;
                    }
                    break;
            }
        }
    }

    // ================================================================ SKY FINISH ==

    /// <summary>Launched, followed up, struck three times in the air and spiked into a crater.</summary>
    internal sealed class SkyFinish : FinaleBase
    {
        private const int Set = 0, Wind = 1, Launch = 2, Air = 3, Spike = 4, End = 5;
        private Vector2 _sw, _sl;
        private float _apex, _h;
        private int _hits;

        public SkyFinish(Clash c) : base(c)
        {
        }

        public override string Variant => "sky finish";

        public override bool Plan()
        {
            Vector2 sa, sb;
            if (!Places(1.1f, out sa, out sb))
            {
                return false;
            }
            _sw = LoserIsA ? sb : sa;
            _sl = LoserIsA ? sa : sb;
            _apex = (D.Gods ? 6f : D.Grand ? 4.5f : L >= 3 ? 3.5f : 2.5f) * K;
            return true;
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / 0.18f);
                        Reposition(W, W == P ? A0 : B0, _sw, k, _sl);
                        Reposition(Lz, Lz == P ? A0 : B0, _sl, k, _sw);
                        FaceEach();
                        if (k >= 1f)
                        {
                            Next(Wind);
                        }
                        break;
                    }
                case Wind:
                    {
                        float k = Mathf.Clamp01(Pt / (Rest(0.45f) + 0.1f));
                        Crouch(W, k, 1.3f);
                        Gather(W, k);
                        Guard(Lz, T, 1f);
                        if (k >= 1f)
                        {
                            Next(Launch);
                        }
                        break;
                    }
                case Launch:
                    {
                        if (Enter())
                        {
                            Vector2 at = Lz.BodyPos();
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, at, Lz.Pose.Ground, Vector2.up, Px, K * 1.3f, 1.5f, WH);
                                DuelFx.GroundRing(Lz.Pose.Ground, Px, 2f, 18f * K, 0.3f, 2, 1, Color.white, Fx.WithA(WH.Main, 0f));
                            }
                            D.Sound("event:/SFX/HIT/HitGeneric");
                            Wreck(Lz.Pose.Ground, 0.9f, 1, W);
                            Stop(0.06f);
                        }
                        float k = Mathf.Clamp01(Pt / 0.3f);
                        _h = _apex * Anim.OutCubic(k);
                        Lz.Pose.Height = _h;
                        Lz.Pose.Rot = 30f * k * W.Fwd;
                        W.Pose.Rot = -10f;
                        W.Pose.SY = 1.15f;
                        W.Pose.SX = 0.9f;
                        // the winner leaps after it a beat behind
                        W.Pose.Height = _apex * 0.9f * Anim.OutCubic(Mathf.Clamp01((Pt - 0.08f) / 0.3f));
                        Trail(W, ref GhostA, dt, 0.02f, 0.18f);
                        if (k >= 1f)
                        {
                            Next(Air);
                        }
                        break;
                    }
                case Air:
                    {
                        // three blows up there, the body jolting with each
                        float beat = Rest(0.16f) + 0.06f;
                        W.Pose.Height = _apex * 0.95f;
                        Lz.Pose.Height = _apex + 0.15f * K * Mathf.Sin(Pt * 30f);
                        W.Pose.Ground = Vector2.Lerp(W.Pose.Ground, Lz.Pose.Ground - new Vector2(W.Fwd * 0.5f * K, 0f), 0.3f);
                        if (Pt >= beat * (_hits + 1) && _hits < 3)
                        {
                            _hits++;
                            W.Pose.Rot = _hits % 2 == 0 ? 25f : -15f;
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), Lz.Pose.Ground, new Vector2(W.Fwd, Random.Range(-0.4f, 0.4f)), Px, K, 1.1f, WH);
                            }
                            D.Chip(W, Lz, 0.1f, false);
                            D.Sound("event:/SFX/OTHER/Punch");
                            Stop(0.04f);
                        }
                        if (_hits >= 3 && Pt >= beat * 3.5f)
                        {
                            Next(Spike);
                        }
                        break;
                    }
                case Spike:
                    {
                        float k = Mathf.Clamp01(Pt / 0.14f);
                        Lz.Pose.Height = _apex * (1f - Anim.InQuad(k));
                        Lz.Pose.Rot = 160f * k * W.Fwd;
                        W.Pose.Rot = 30f;
                        if (Vis && Random.value < 0.8f)
                        {
                            Trails.Ghost(Lz.A, 0.12f, Fx.WithA(WH.Main, 0.6f), Fx.WithA(WH.Edge, 0f));
                        }
                        if (k >= 1f)
                        {
                            Lz.Pose.Height = 0f;
                            Crater(Lz.Pose.Ground, 1.5f, 3);
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), Lz.Pose.Ground, Vector2.down, Px, K * 1.6f, 1.7f, WH);
                            }
                            Kill(Lz.BodyPos(), 0.16f);
                            Next(End);
                        }
                        break;
                    }
                default:
                    {
                        // the winner comes down after it
                        W.Pose.Height = Mathf.Max(0f, _apex * 0.95f * (1f - Pt / 0.25f));
                        if (W.Pose.Height <= 0f)
                        {
                            Victor(Pt - 0.25f, false);
                        }
                        Fallen(Pt);
                        if (Pt >= 0.7f)
                        {
                            Done = true;
                        }
                        break;
                    }
            }
        }
    }

    // ================================================================= OVERWHELM ==

    /// <summary>A gulf in power: one blow, almost bored, and the loser is sent across the world.</summary>
    internal sealed class Overwhelm : FinaleBase
    {
        private const int Set = 0, Flick = 1, Gone = 2, End = 3;
        private Vector2 _sw, _sl, _land;
        private Knock _fly;

        public Overwhelm(Clash c) : base(c)
        {
        }

        public override string Variant => "overwhelm";

        public override bool Plan()
        {
            Vector2 sa, sb;
            if (!Places(0.9f, out sa, out sb))
            {
                return false;
            }
            _sw = LoserIsA ? sb : sa;
            _sl = LoserIsA ? sa : sb;
            float far = (D.Gods ? 14f : D.Grand ? 10f : 7f) * K;
            if (!DuelGeo.Far(_sl, (_sl - _sw).normalized, far * 0.6f, far, 1f, out _land))
            {
                return false;
            }
            return true;
        }

        protected override void Tick(float dt)
        {
            switch (Ph)
            {
                case Set:
                    {
                        float k = Mathf.Clamp01(Pt / 0.3f);
                        // it walks up, unhurried; the other can't move
                        W.Pose.Ground = Vector2.Lerp(W == P ? A0 : B0, _sw, Anim.InOutSine(k));
                        Reposition(Lz, Lz == P ? A0 : B0, _sl, Mathf.Clamp01(k * 2f), _sw);
                        FaceEach();
                        Guard(Lz, T, 1f, 1.4f);
                        if (k >= 1f)
                        {
                            Next(Flick);
                        }
                        break;
                    }
                case Flick:
                    {
                        if (Enter())
                        {
                            Vector2 at = Lz.BodyPos();
                            if (Vis)
                            {
                                DuelFx.Ting(at, Px, Color.white, 8, (_land - _sl).normalized, 1.3f);
                                SchoolStyle.Emblem(WS, at, Lz.Pose.Ground, (_land - _sl).normalized, Px, K * 1.4f, 1.6f, WH);
                                DuelFx.AirRing(at, Px, 2f, 26f * K, 0.3f, 2, Color.white, Fx.WithA(WH.Main, 0f));
                            }
                            D.Sound("event:/SFX/HIT/HitGeneric");
                            Kit.Stop(at, 0.16f);
                            _fly = Knock.Fly(_sl, _land, D.Gods ? 0.9f : 0.7f, 2.4f * K, (LoserIsA ? -1f : 1f) * 1080f, 0.2f);
                            Kill(at, 0.2f);
                        }
                        W.Pose.Rot = 8f;
                        Next(Gone);
                        break;
                    }
                case Gone:
                    {
                        bool done = _fly.Step(Lz, dt);
                        if (Vis && _fly.Moving && Random.value < 0.5f)
                        {
                            DuelFx.Comet(Lz.BodyPos(), Lz.BodyPos() - (_land - _sl).normalized * 0.6f * K, Px, Color.white, WH.Main);
                        }
                        if (_fly.Landed && Enter())
                        {
                            Crater(_land, 1.2f, 2);
                        }
                        Victor(Pt, false);
                        if (done || Pt > 1.6f)
                        {
                            Next(End);
                        }
                        break;
                    }
                default:
                    Fallen(Pt);
                    if (Pt >= 0.3f)
                    {
                        Done = true;
                    }
                    break;
            }
        }
    }

    // ================================================================= SIGNATURE ==

    /// <summary>The winner's school's own finishing art (see Finales).</summary>
    internal sealed class Signature : FinaleBase
    {
        private const int Set = 0, Ready = 1, Act = 2, Blow = 3, After = 4, End = 5;
        private Vector2 _sw, _sl, _land;
        private Knock _fly;
        private float _gap, _h;
        private int _n;
        private readonly List<Vector2> _spots = new List<Vector2>();

        public Signature(Clash c) : base(c)
        {
        }

        public override string Variant => "signature " + SchoolStyle.Name(WSchool());

        private School WSchool()
        {
            return Schools.PrimaryOf(LoserIsA ? D.B : D.A);
        }

        public override bool Plan()
        {
            School s = WSchool();
            _gap = s == School.ThousandBlades ? 2.6f : s == School.HuntersWay ? 3.2f : s == School.PhantomStep ? 2f : 1.1f;
            Vector2 sa, sb;
            if (!Places(_gap, out sa, out sb))
            {
                return false;
            }
            _sw = LoserIsA ? sb : sa;
            _sl = LoserIsA ? sa : sb;
            float far = (D.Gods ? 9f : D.Grand ? 6f : 4f) * K;
            if (!DuelGeo.Far(_sl, (_sl - _sw).normalized, far * 0.6f, far, 0.8f, out _land))
            {
                _land = _sl;
            }
            return true;
        }

        protected override void Tick(float dt)
        {
            if (Ph == Set)
            {
                float k = Mathf.Clamp01(Pt / 0.2f);
                Reposition(W, W == P ? A0 : B0, _sw, k, _sl);
                Reposition(Lz, Lz == P ? A0 : B0, _sl, k, _sw);
                FaceEach();
                if (k >= 1f)
                {
                    Next(Ready);
                }
                return;
            }
            if (Ph == End)
            {
                Victor(Pt, Pt < 0.02f);
                Fallen(Pt);
                if (Pt >= 0.55f)
                {
                    Done = true;
                }
                return;
            }
            switch (WS)
            {
                case School.IronFist: FistArt(dt); break;
                case School.TempestLeg: TempestArt(dt); break;
                case School.ThousandBlades: BladeArt(dt); break;
                case School.EarthBreaker: EarthArt(dt); break;
                case School.PhantomStep: PhantomArt(dt); break;
                case School.HuntersWay: HuntArt(dt); break;
                default: KiArt(dt); break;
            }
        }

        private float ReadyFor => Rest(0.6f) + 0.2f;

        /// <summary>The last punch: a fist cocked back and loaded, one straight, the loser ploughed through the land.</summary>
        private void FistArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / ReadyFor);
                        Crouch(W, k, 1.2f);
                        W.Pose.Rot = -14f * k;
                        Gather(W, k);
                        Guard(Lz, T, 1f);
                        if (k >= 1f)
                        {
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        float k = Mathf.Clamp01(Pt / 0.08f);
                        W.Pose.Ground = Vector2.Lerp(_sw, _sw + (_sl - _sw) * 0.45f, Anim.OutCubic(k));
                        W.Pose.Rot = 20f;
                        W.Pose.SX = 1.25f;
                        W.Pose.SY = 0.85f;
                        if (k >= 1f)
                        {
                            Vector2 at = Lz.BodyPos();
                            Vector2 d = (_land - _sl).normalized;
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, at, Lz.Pose.Ground, d, Px, K * 2f, 1.8f, WH);
                                for (int i = 0; i < 3; i++)
                                {
                                    DuelFx.AirRing(at + d * i * 0.5f * K, Px, 2f, (14f + 10f * i) * K, 0.3f + 0.1f * i, 2, Color.white, Fx.WithA(WH.Main, 0f));
                                }
                            }
                            D.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall");
                            Kill(at, 0.18f);
                            _fly = Knock.Skid(_sl, _land, D.Gods ? 0.7f : 0.5f, -30f, 0.2f);
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        Vector2 was = Lz.Pose.Ground;
                        bool done = _fly.Step(Lz, dt);
                        // ploughed through the land on its back
                        if (L >= 2 && (Lz.Pose.Ground - was).sqrMagnitude > 1e-6f)
                        {
                            D.Furrow(was, Lz.Pose.Ground, 0.7f, D.Grand ? 2 : 1, W.A);
                            if (Vis)
                            {
                                DuelFx.Plough(Lz.Pose.Ground, (_land - _sl).normalized, Px, Combat.TileAt(Lz.Pose.Ground), K);
                            }
                        }
                        W.Pose.Rot = 20f;
                        if (done || Pt > 1.2f)
                        {
                            Crater(_land, 1f, 2);
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>The tornado kick: it spins up a whirlwind that lifts the loser and hurls it down.</summary>
        private void TempestArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / ReadyFor);
                        W.Pose.Rot = 720f * k * k;
                        W.Pose.SY = 1f - 0.1f * k;
                        Gather(W, k);
                        if (Vis && Random.value < 0.5f)
                        {
                            DuelFx.Swirl(_sl, Px, Combat.TileAt(_sl), 1.4f * K, 3, 0.3f, 600f, WH.Main);
                        }
                        Guard(Lz, T, 1f);
                        if (k >= 1f)
                        {
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        float dur = 0.6f;
                        float k = Mathf.Clamp01(Pt / dur);
                        _h = (D.Gods ? 6f : 3.5f) * K * Anim.OutCubic(k);
                        Lz.Pose.Height = _h;
                        Lz.Pose.Rot = 900f * k;
                        W.Pose.Rot += 1400f * dt;
                        W.Pose.Height = _h * 0.7f;
                        if (Vis && Random.value < 0.7f)
                        {
                            float r = Random.Range(0.4f, 1.4f) * K;
                            DuelFx.AirRing(Lz.Pose.Ground + new Vector2(0f, Random.Range(0f, _h)), Px, r / Px * 0.4f, r / Px, 0.18f, 1, Fx.WithA(Color.white, 0.8f), Fx.WithA(WH.Main, 0f));
                        }
                        if (k >= 1f)
                        {
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), Lz.Pose.Ground, Vector2.down, Px, K * 1.8f, 1.8f, WH);
                            }
                            D.Sound("event:/SFX/OTHER/Punch");
                            Stop(0.08f);
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        float k = Mathf.Clamp01(Pt / 0.14f);
                        Lz.Pose.Height = _h * (1f - Anim.InQuad(k));
                        W.Pose.Height = Mathf.Max(0f, W.Pose.Height - 20f * dt * K);
                        W.Pose.Rot = Mathf.MoveTowards(W.Pose.Rot % 360f, 0f, 2000f * dt);
                        if (k >= 1f)
                        {
                            Lz.Pose.Height = 0f;
                            Crater(Lz.Pose.Ground, 1.4f, 3);
                            Kill(Lz.BodyPos(), 0.16f);
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>The thousand cuts: one draw, it is already behind the loser; the cuts open as it sheathes.</summary>
        private void BladeArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / ReadyFor);
                        Crouch(W, k, 1.4f);
                        Gather(W, k);
                        Guard(Lz, T, 1f);
                        if (k >= 1f)
                        {
                            if (Vis)
                            {
                                DuelFx.Blink(W.Pose.Ground, Px, K, Color.white, WH.Main);
                            }
                            Vector2 behind = _sl + (_sl - _sw).normalized * 1f * K;
                            W.Pose.Ground = Combat.SafeSpot(behind) ? behind : _sl + (_sl - _sw).normalized * 0.4f * K;
                            W.Pose.FaceRight = !W.Pose.FaceRight;   // back turned to it
                            if (Vis)
                            {
                                DuelFx.Blink(W.Pose.Ground, Px, K, Color.white, WH.Main);
                                Kit.Line(_sw, W.Pose.Ground, Px, 0.5f, 2, FxLayer.Top, 20, Color.white, Fx.WithA(WH.Main, 0.8f), Fx.WithA(WH.Edge, 0f));
                            }
                            D.Sound("event:/SFX/HIT/HitSwordSword");
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        // frozen: the cuts appear on the loser one after another, hanging in the air
                        W.Pose.Rot = 18f;
                        int want = Mathf.Min(D.Gods ? 16 : D.Grand ? 12 : 8, 1 + (int)(Pt / 0.035f));
                        while (_n < want)
                        {
                            _n++;
                            if (Vis)
                            {
                                float a = Random.Range(0f, Mathf.PI);
                                Vector2 ld = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                                Vector2 c = Lz.BodyPos() + Random.insideUnitCircle * 0.25f * K;
                                float half = Random.Range(0.5f, 0.9f) * K;
                                Kit.Line(c - ld * half, c + ld * half, Px, 0.9f, 1, FxLayer.Top, 21, Fx.WithA(Color.white, 0.95f), Fx.WithA(WH.Main, 0.7f), Fx.WithA(WH.Edge, 0f));
                            }
                        }
                        if (Pt >= 0.45f)
                        {
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        if (Enter())
                        {
                            // the click of the sheathe: every cut opens at once
                            if (Vis)
                            {
                                Fx.Glint(W.BodyPos(), Px, 0.3f, Color.white, 1.4f);
                                Kit.Flash(Lz.BodyPos(), Px, Mathf.RoundToInt(8f * K), 0.1f, Color.white);
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), Lz.Pose.Ground, (_sl - _sw).normalized, Px, K * 2f, 1.8f, WH);
                            }
                            Kill(Lz.BodyPos(), 0.18f);
                            if (L >= 2)
                            {
                                // the cut runs on across the land: a cross carved through where it stood
                                Vector2 ax = (_sl - _sw).normalized;
                                Vector2 side = new Vector2(-ax.y, ax.x);
                                float len = (D.Gods ? 9f : D.Grand ? 6f : 3f) * K;
                                foreach (Vector2 dir in new[] { (ax + side).normalized, (ax - side).normalized })
                                {
                                    Vector2 a0 = Combat.Clamp(_sl - dir * len * 0.5f), b0 = Combat.Clamp(_sl + dir * len * 0.5f);
                                    D.Furrow(a0, b0, 0.5f, D.Grand ? 2 : 1, W.A);
                                    if (Vis)
                                    {
                                        Kit.Line(a0, b0, Px, 1.2f, 2, FxLayer.Ground, 5, Color.white, Fx.WithA(WH.Main, 0.8f), Fx.WithA(WH.Edge, 0f));
                                    }
                                }
                            }
                        }
                        if (Pt >= 0.2f)
                        {
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>Ki at point-blank range: a palm on the chest, a sun swelling between them, and it goes off.</summary>
        private void KiArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / 0.12f);
                        W.Pose.Ground = Vector2.Lerp(_sw, _sl - (_sl - _sw).normalized * 0.45f * K, Anim.OutCubic(k));
                        if (k >= 1f)
                        {
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        float dur = Rest(0.6f) + 0.2f;
                        float k = Mathf.Clamp01(Pt / dur);
                        W.Pose.Rot = 10f;
                        Vector2 at = (W.BodyPos() + Lz.BodyPos()) * 0.5f;
                        if (Vis)
                        {
                            int r = Mathf.Clamp(Mathf.RoundToInt((1.5f + 7f * k) * K), 1, 30);
                            Fx.Spawn(Art.Disk(r, 1f, 0.35f), at, Px, 0.05f, FxLayer.Top, 22)?.Color(Color.white, Fx.WithA(WH.Main, 0.9f));
                            ChoreoFx.Motes(at, Px, 2, 14f, 0.12f, WH.Core, WH.Edge, FxLayer.Top);
                        }
                        Lz.Pose.Nudge = new Vector2(((int)(Pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
                        if (k >= 1f)
                        {
                            if (Vis)
                            {
                                Fx.Spawn(Art.Disk(Mathf.Clamp(Mathf.RoundToInt(16f * K), 4, 40), 1f, 0.5f), at, Px, 0.4f, FxLayer.Top, 22)?
                                    .Sized(0.5f, 1.8f).Color(Color.white, Fx.WithA(WH.Main, 0.9f), Fx.WithA(WH.Edge, 0f));
                                SchoolStyle.Emblem(WS, at, Lz.Pose.Ground, (_land - _sl).normalized, Px, K * 2f, 1.8f, WH);
                                DuelFx.AirRing(at, Px, 2f, 34f * K, 0.4f, 2, Color.white, Fx.WithA(WH.Main, 0.6f), Fx.WithA(WH.Edge, 0f));
                            }
                            D.Sound("event:/SFX/EXPLOSIONS/ExplosionBig");
                            Crater(Lz.Pose.Ground, 1.2f, 2, false);
                            Kill(at, 0.16f);
                            _fly = Knock.Fly(Lz.Pose.Ground, _land, 0.6f, 2f * K, (LoserIsA ? -1f : 1f) * 540f, 0.2f);
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        bool done = _fly.Step(Lz, dt);
                        if (_fly.Landed && Enter())
                        {
                            Crater(_land, 0.9f, 1);
                        }
                        if (done || Pt > 1.3f)
                        {
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>The earth breaker's grave: it seizes the loser, leaps with it and drives it head first into the land.</summary>
        private void EarthArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / 0.18f);
                        W.Pose.Ground = Vector2.Lerp(_sw, _sl, Anim.OutCubic(k));
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/OTHER/Punch");
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        // lifted overhead, then up they both go
                        float dur = Rest(0.5f) + 0.35f;
                        float k = Mathf.Clamp01(Pt / dur);
                        float lift = Mathf.Clamp01(k / 0.35f);
                        _h = (D.Gods ? 5f : D.Grand ? 3.5f : 2.2f) * K * Anim.OutCubic(Mathf.Clamp01((k - 0.35f) / 0.65f));
                        W.Pose.Height = _h;
                        W.Pose.Ground = _sl;
                        Lz.Pose.Ground = _sl;
                        Lz.Pose.Height = _h + 0.9f * K * lift;
                        Lz.Pose.Rot = 180f * lift;
                        W.Pose.SY = 1f + 0.1f * lift;
                        if (Vis && Pt < 0.05f)
                        {
                            DuelFx.Smash(_sl, Px, Combat.TileAt(_sl), true, K);
                        }
                        if (k >= 1f)
                        {
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        float k = Mathf.Clamp01(Pt / 0.13f);
                        W.Pose.Height = _h * (1f - Anim.InQuad(k));
                        Lz.Pose.Height = W.Pose.Height + 0.9f * K * (1f - k);
                        if (k >= 1f)
                        {
                            W.Pose.Height = 0f;
                            Lz.Pose.Height = 0f;
                            Crater(_sl, 1.8f, 3);
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), _sl, Vector2.down, Px, K * 2f, 1.8f, WH);
                                Spec.Rocks(_sl, Px, 24, 110f, K);
                            }
                            Ruin.Quake(W.A, _sl, Scale.Ultimate, 1.6f, K);
                            Kill(Lz.BodyPos(), 0.18f);
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>The phantom's execution: it fades, its shadows strike from six sides, and it is behind the loser.</summary>
        private void PhantomArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        if (Enter())
                        {
                            if (Vis)
                            {
                                DuelFx.Blink(W.Pose.Ground, Px, K, Color.white, WH.Main);
                            }
                            int n = D.Gods ? 8 : D.Grand ? 7 : 6;
                            for (int i = 0; i < n; i++)
                            {
                                float a = i * Mathf.PI * 2f / n + Random.Range(-0.2f, 0.2f);
                                _spots.Add(_sl + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * 0.9f * K);
                            }
                        }
                        W.Pose.Hidden = true;
                        Guard(Lz, T, 1f, 1.3f);
                        if (Pt >= 0.15f)
                        {
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        float beat = 0.07f;
                        int want = Mathf.Min(_spots.Count, 1 + (int)(Pt / beat));
                        while (_n < want)
                        {
                            Vector2 s = _spots[_n];
                            _n++;
                            // a shadow of it, there for a blink, striking
                            W.Pose.Hidden = false;
                            W.Pose.Ground = s;
                            Face(W, _sl);
                            W.Pose.Rot = 20f;
                            if (Vis)
                            {
                                Fx.Afterimage(W, 0.3f, Fx.WithA(WH.Main, 0.85f), Fx.WithA(Color.Lerp(WH.Main, Color.black, 0.5f), 0f));
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), _sl, (_sl - s).normalized, Px, K, 1.1f, WH);
                            }
                            D.Sound("event:/SFX/OTHER/Punch");
                        }
                        W.Pose.Hidden = true;
                        if (_n >= _spots.Count && Pt >= beat * (_spots.Count + 1))
                        {
                            // it is behind the loser, back turned
                            W.Pose.Hidden = false;
                            Vector2 behind = _sl + (_sl - _sw).normalized * 0.9f * K;
                            W.Pose.Ground = Combat.SafeSpot(behind) ? behind : _sw;
                            Face(W, _sl);
                            W.Pose.FaceRight = !W.Pose.FaceRight;
                            if (Vis)
                            {
                                DuelFx.Blink(W.Pose.Ground, Px, K, Color.white, WH.Main);
                            }
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        if (Pt >= 0.3f && Enter())
                        {
                            if (Vis)
                            {
                                Spec.Blob(Lz.BodyPos(), 0.9f * K, Px, 0.5f, FxLayer.Top, 20, 1f, 0.5f, Fx.WithA(Color.Lerp(WH.Main, Color.black, 0.5f), 0.9f), Fx.WithA(WH.Main, 0.5f), Fx.WithA(WH.Main, 0f));
                            }
                            Kill(Lz.BodyPos(), 0.14f);
                            if (D.Grand)
                            {
                                Crater(_sl, 1f, 2, false);
                            }
                        }
                        if (Pt >= 0.5f)
                        {
                            Next(End);
                        }
                        break;
                    }
            }
        }

        /// <summary>The hunter's kill: a javelin pins the quarry, and the hunter is on it with the last thrust.</summary>
        private void HuntArt(float dt)
        {
            switch (Ph)
            {
                case Ready:
                    {
                        float k = Mathf.Clamp01(Pt / (Rest(0.35f) + 0.15f));
                        Crouch(W, k);
                        W.Pose.Rot = -16f * k;
                        Gather(W, k);
                        Guard(Lz, T, 1f);
                        if (k >= 1f)
                        {
                            D.Sound("event:/SFX/UNIQUE/SpawnWhoosh");
                            Next(Act);
                        }
                        break;
                    }
                case Act:
                    {
                        // the javelin flies
                        float k = Mathf.Clamp01(Pt / 0.12f);
                        Vector2 a = W.BodyPos(), b = Lz.BodyPos();
                        Vector2 at = Vector2.Lerp(a, b, k);
                        if (Vis)
                        {
                            Vector2 d = (b - a).normalized;
                            Kit.Line(at - d * 0.7f * K, at, Px, 0.04f, 1, FxLayer.Top, 21, new Color(0.66f, 0.48f, 0.3f, 1f));
                            Kit.Line(at - d * 0.12f * K, at, Px, 0.04f, 1, FxLayer.Top, 22, Color.white);
                        }
                        W.Pose.Rot = 22f;
                        if (k >= 1f)
                        {
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, b, Lz.Pose.Ground, (b - a).normalized, Px, K * 1.3f, 1.4f, WH);
                            }
                            D.Chip(W, Lz, 0.2f, true);
                            D.Sound("event:/SFX/HIT/HitMetal");
                            _fly = Knock.Skid(_sl, Combat.Clamp(_sl + (_sl - _sw).normalized * 0.6f * K), 0.2f, -20f, 0.1f);
                            Next(Blow);
                        }
                        break;
                    }
                case Blow:
                    {
                        _fly.Step(Lz, dt);
                        // the pounce: a leap onto the pinned quarry
                        float k = Mathf.Clamp01((Pt - 0.12f) / 0.32f);
                        if (k > 0f)
                        {
                            W.Pose.Ground = Vector2.Lerp(_sw, Lz.Pose.Ground - (Lz.Pose.Ground - _sw).normalized * 0.3f * K, k);
                            W.Pose.Height = 2f * K * 4f * k * (1f - k);
                            W.Pose.Rot = 30f * k;
                        }
                        if (k >= 1f && Enter())
                        {
                            W.Pose.Height = 0f;
                            if (Vis)
                            {
                                SchoolStyle.Emblem(WS, Lz.BodyPos(), Lz.Pose.Ground, Vector2.down, Px, K * 1.8f, 1.8f, WH);
                            }
                            Crater(Lz.Pose.Ground, 1.1f, 2);
                            Kill(Lz.BodyPos(), 0.16f);
                        }
                        if (Pt >= 0.6f)
                        {
                            Next(End);
                        }
                        break;
                    }
            }
        }
    }
}
