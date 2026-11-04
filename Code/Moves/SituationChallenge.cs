using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// CHALLENGE. In a skirmish (three or more of its side and three or more enemies fighting within
    /// 10 tiles), a Master or better steps out of its line and points its weapon at the strongest
    /// enemy within 12 tiles - a tiered Adept+, at least its own tier minus two, 3+ tiles away, free -
    /// and that enemy answers: a thin line of light runs from the challenger's weapon to it, a glint
    /// "ting"s on its head, and the two go for each other for up to 10 s (until they meet, one falls or
    /// they are pulled 20 tiles apart); the duel system takes over when they meet. 35% when it sees
    /// the chance, once a minute per fighter, one challenge every 15 s in the world. Only with duels
    /// on (a challenge is a duel's invitation).
    /// </summary>
    internal static class SitChallenge
    {
        private sealed class Pair
        {
            public Actor A, B;
            public long IdA, IdB;
            public double Until, NextSteer;
            public bool Answered;
        }

        private static readonly List<Pair> _pairs = new List<Pair>();
        private static readonly Dictionary<long, double> _ready = new Dictionary<long, double>();
        private static double _worldReady;

        // ---------------------------------------------------------------- start --

        public static void Consider(Fighter f, ref Situations.Survey s)
        {
            if (!Settings.Duels || f.Tier < Tier.Master || s.Rival == null || s.AlliesFighting10 < 3 || s.FoesFighting10 < 3)
            {
                return;
            }
            double now = Fighter.Now;
            if (now < _worldReady || s.RivalDist < 3f || (int)s.RivalTier < (int)f.Tier - 2)
            {
                return;
            }
            Actor a = f.A, b = s.Rival;
            if (_ready.TryGetValue(f.Id, out double ra) && now < ra || _ready.TryGetValue(b.Sid(), out double rb) && now < rb)
            {
                return;
            }
            if (Random.value > 0.35f)
            {
                _ready[f.Id] = now + 3.0;   // not this time; look again in a while
                return;
            }
            if (Situations.Engaged(a) || Situations.Engaged(b) || SitLastStand.In(a) || !Situations.Free(b) || !Situations.CanAct(a)
                || Situations.OnTieredFoe(a, b) || Situations.OnTieredFoe(b, a))
            {
                return;
            }
            if (Start(a, b, f.Tier, now) && Dev.On)
            {
                Dev.Trace("situation challenge: " + a.asset.id + "#" + f.Id + " (" + f.Tier + ") challenges " + b.asset.id + "#" + b.Sid() + " (" + s.RivalTier + ")");
            }
        }

        private static bool Start(Actor a, Actor b, Tier tier, double now)
        {
            Pair p = new Pair { A = a, B = b, IdA = a.Sid(), IdB = b.Sid(), Until = now + 10.0, NextSteer = now + 0.9 };
            if (!SitChallengeMove.Begin(a, b, p))
            {
                return false;
            }
            _pairs.Add(p);
            Situations.Claim(a, p);
            Situations.Claim(b, p);
            _ready[p.IdA] = now + 60.0;
            _ready[p.IdB] = now + 60.0;
            _worldReady = now + 15.0;
            Situations.Tip(Locale.Format("sakuga_sit_challenge", Situations.Name(a), Situations.Name(b)), SitLastStand.TierHex(tier), a.current_position);
            Spotlight.Fight(a, b, tier);
            Dev.Watch(Vector2.Lerp(a.current_position, b.current_position, 0.5f) + new Vector2(0f, 1.5f));
            return true;
        }

        private static readonly List<Actor> _near = new List<Actor>();

        /// <summary>
        /// Staging (Situations.Force): the strongest of this unit's side (a Master if it has one)
        /// challenges the strongest free enemy (tiered ones first) 3 to 14 tiles away from it, duels
        /// setting or not; it waits until one is in sight and it can perform.
        /// </summary>
        internal static Situations.Stage Stage(Actor a, bool last, out string msg)
        {
            Actor c = Situations.Pick(a, Tier.Master);
            if (c == null)
            {
                msg = "challenge: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(c);
            string who = c.asset.id + "#" + c.Sid();
            if (Situations.Engaged(c) || !Situations.CanAct(c))
            {
                msg = "challenge: " + who + " is busy";
                return Situations.Stage.Wait;
            }
            Actor best = null;
            float bestRank = -1f;
            Vector2 p = c.current_position;
            Situations.Around(p, 14f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == c || !Combat.IsFoe(c, u) || !Situations.Threat(u) || Situations.Engaged(u) || !Situations.Free(u))
                {
                    continue;
                }
                if ((u.current_position - p).sqrMagnitude < 9f)
                {
                    continue;   // too close to step out and point: they are already fighting
                }
                float rank = (int)Tiers.Of(u) * 10f + Combat.HpPct(u);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = u;
                }
            }
            _near.Clear();
            if (best == null)
            {
                msg = "challenge: no free enemy 3 to 14 tiles from " + who;
                return Situations.Stage.Wait;
            }
            Situations.Show(Vector2.Lerp(p, best.current_position, 0.5f) + new Vector2(0f, 1.5f));
            if (!Start(c, best, Tiers.Of(c), Fighter.Now))
            {
                msg = "challenge: " + who + " could not start";
                return Situations.Stage.Wait;
            }
            msg = "challenge: " + who + " (" + Tiers.Of(c) + ") points at " + best.asset.id + "#" + best.Sid() + " (" + Tiers.Of(best) + ")";
            return Situations.Stage.Done;
        }

        /// <summary>The challenger's point (called by its move): the one it points at answers.</summary>
        internal static void Answer(object pair)
        {
            if (!(pair is Pair p) || p.Answered || !_pairs.Contains(p))
            {
                return;
            }
            p.Answered = true;
            Actor b = p.B;
            if (b == null || b.Sid() != p.IdB || !b.isAlive())
            {
                return;
            }
            SitFx.HeadGlint(b, Tiers.LightColor(Tiers.Of(b)), 0.3f, 1.15f, new Vector2(0f, 3f));
            if (Situations.Steer(b, p.A, false))
            {
                Situations.Resume(b);   // it answers at once
            }
        }

        // --------------------------------------------------------------- update --

        public static void Update(double now)
        {
            for (int i = _pairs.Count - 1; i >= 0; i--)
            {
                if (i >= _pairs.Count)
                {
                    continue;
                }
                Pair p = _pairs[i];
                bool a = p.A != null && p.A.Sid() == p.IdA && p.A.isAlive();
                bool b = p.B != null && p.B.Sid() == p.IdB && p.B.isAlive();
                if (!a || !b || now > p.Until)
                {
                    End(p);
                    continue;
                }
                float d2 = (p.A.current_position - p.B.current_position).sqrMagnitude;
                if (d2 > 400f || d2 < 2.2f * 2.2f && p.Answered)
                {
                    End(p);   // pulled apart, or they met: the fight is theirs now
                    continue;
                }
                if (p.Answered && now >= p.NextSteer)
                {
                    p.NextSteer = now + 0.5;
                    Situations.Steer(p.A, p.B, true);
                    Situations.Steer(p.B, p.A, true);
                }
            }
        }

        private static void End(Pair p)
        {
            _pairs.Remove(p);
            Situations.Unclaim(p.IdA, p);
            Situations.Unclaim(p.IdB, p);
        }

        public static void Prune(double now)
        {
            List<long> old = null;
            foreach (KeyValuePair<long, double> kv in _ready)
            {
                if (kv.Value < now)
                {
                    (old ??= new List<long>()).Add(kv.Key);
                }
            }
            if (old != null)
            {
                foreach (long id in old)
                {
                    _ready.Remove(id);
                }
            }
        }

        public static void Clear()
        {
            _pairs.Clear();
            _ready.Clear();
            _worldReady = 0;
        }
    }

    /// <summary>
    /// The challenge: one step forward, and a weapon pointed across the field.
    ///
    /// Beat sheet (game seconds):
    ///
    ///   STEP   0.18  it steps out toward the enemy line (half a tile, less if the ground is bad),
    ///                leaning in, dust kicked from its heel
    ///   PLANT  0.10  plants its front foot: squash
    ///   POINT  0.40  rises tall, leaning toward its opponent, weapon raised at it: a glint at the
    ///                weapon's tip, a thin line of light (its tier colour) runs from the tip toward
    ///                the opponent and fades, a ring ripples out from its feet; the opponent's head
    ///                "ting"s: it answers
    ///   HOLD   0.20  holds the pose a beat, then eases back to stand
    ///
    /// Pointing at someone stronger than itself, its arm trembles (a 1-pixel shake through POINT).
    ///
    /// Challengers are Masters or better, and they don't stand around: the whole challenge takes
    /// about half a second (Situations.Pace; the point never under 0.2 s), then the challenger goes
    /// for its opponent, its Brain looking for the way in at once (and so does the one who answers).
    /// </summary>
    internal sealed class SitChallengeMove : Move
    {
        private readonly float TStep;
        private readonly float TPlant;
        private readonly float TPoint;
        private readonly float THold;

        private readonly Actor _b;
        private readonly long _bId;
        private readonly object _pair;
        private readonly Vector2 _from, _to;
        private readonly float _k, _fwd;
        private readonly Palette _pal;
        private readonly bool _brave;
        private int _phase;
        private float _pt;
        private bool _entered;

        private SitChallengeMove(Actor a, Actor b, object pair) : base(a)
        {
            _b = b;
            _bId = b.Sid();
            _pair = pair;
            _from = a.current_position;
            _k = K;
            Vector2 dir = (b.current_position - _from).normalized;
            Vector2 to = Combat.Clamp(_from + dir * 0.5f * _k);
            _to = Combat.SafeSpot(to) && Combat.ClearPath(_from, to) ? to : _from;
            float dx = b.current_position.x - _from.x;
            _fwd = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : (a.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            Tier t = Tiers.Of(a);
            _pal = Palette.For(t, t >= Tier.Master ? 3 : 2);
            _brave = Tiers.Of(b) > t;
            float pace = Situations.Pace(t);
            TStep = Situations.Beat(0.18f, pace, 0.1f);
            TPlant = 0.1f * pace;
            TPoint = Situations.Beat(0.4f, pace, 0.2f);
            THold = 0.2f * pace;
        }

        public static bool Begin(Actor a, Actor b, object pair)
        {
            return Moves.CanPerform(a) && Moves.Start(new SitChallengeMove(a, b, pair));
        }

        private bool Visible => Fx.OnScreen(_from, 0.3f);

        /// <summary>The weapon's tip: in front of the body, at shoulder height.</summary>
        private Vector2 Tip => BodyPos() + new Vector2(_fwd * 4f * Px * _k, Pose.PivotUp * 0.5f * Px);

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        private void Next()
        {
            _phase++;
            _pt = 0f;
            _entered = false;
        }

        public override void Tick(float dt)
        {
            _pt += dt;
            float px = Px;
            Pose.Height = 0f;
            switch (_phase)
            {
                case 0:
                    {
                        if (Enter() && Visible && _to != _from)
                        {
                            Fx.LandingPuff(_from, px, A.current_tile);
                        }
                        float k = Anim.OutQuad(Mathf.Clamp01(_pt / TStep));
                        Pose.Ground = Vector2.Lerp(_from, _to, k);
                        float lean = Mathf.Sin(Mathf.PI * k);
                        Pose.Rot = 10f * lean;
                        Pose.SX = 1f + 0.06f * lean;
                        Pose.SY = 1f - 0.05f * lean;
                        if (_pt >= TStep)
                        {
                            Next();
                        }
                        break;
                    }
                case 1:
                    {
                        float k = Anim.OutCubic(Mathf.Clamp01(_pt / TPlant));
                        Pose.Ground = _to;
                        Pose.SX = Mathf.Lerp(1f, 1.1f, k);
                        Pose.SY = Mathf.Lerp(1f, 0.88f, k);
                        Pose.Rot = 0f;
                        if (_pt >= TPlant)
                        {
                            Next();
                        }
                        break;
                    }
                case 2:
                    {
                        Pose.Ground = _to;
                        if (Enter())
                        {
                            Point(px);
                        }
                        float k = Anim.OutCubic(Mathf.Clamp01(_pt / 0.12f));
                        Pose.SX = Mathf.Lerp(1.1f, 0.95f, k);
                        Pose.SY = Mathf.Lerp(0.88f, 1.08f, k);
                        Pose.Rot = 12f * k;
                        Pose.Nudge = _brave && _pt > Mathf.Min(0.12f, TPoint * 0.5f) ? new Vector2(((int)(_pt * 40f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                        if (_pt >= TPoint)
                        {
                            Pose.Nudge = Vector2.zero;
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        Pose.Ground = _to;
                        float k = Anim.InOutSine(Mathf.Clamp01(_pt / THold));
                        Pose.SX = Mathf.Lerp(0.95f, 1f, k);
                        Pose.SY = Mathf.Lerp(1.08f, 1f, k);
                        Pose.Rot = Mathf.Lerp(12f, 0f, k);
                        if (_pt >= THold)
                        {
                            Pose.SX = Pose.SY = 1f;
                            Pose.Rot = 0f;
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        /// <summary>The point: tip glint, the line of light toward the opponent, a ring at its feet; the answer.</summary>
        private void Point(float px)
        {
            Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            bool there = _b != null && _b.Sid() == _bId && _b.isAlive();
            if (Visible)
            {
                Vector2 tip = Tip;
                // riding on the unit: it may outlive a God's point by a frame or two
                SitFx.Ride(Fx.Glint(tip, px, 0.3f, _pal.Core, 1.15f), this, tip - BodyPos());
                if (there)
                {
                    // a thin line of light across the field: from the tip, most of the way to it
                    Vector2 target = SitFx.Body(_b);
                    Vector2 end = Vector2.Lerp(tip, target, 0.85f);
                    Kit.Line(tip, end, px, 0.28f, 1, FxLayer.Top, 3, Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                    Kit.SpeedLines(Vector2.Lerp(tip, end, 0.5f), end - tip, px, 3, 2f, 0.2f, Fx.WithA(_pal.Edge, 0.6f));
                }
                Fx.Ring(_to, px, 0.4f, 3f, 18f * _k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                    Fx.WithA(_pal.Core, 0.9f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
            }
            SitChallenge.Answer(_pair);
        }

        public override void OnEnd(bool aborted)
        {
            if (!aborted)
            {
                // it goes for its opponent (the steering keeps it on it)
                Actor b = _b != null && _b.Sid() == _bId && _b.isAlive() ? _b : null;
                if (b != null)
                {
                    Situations.Steer(A, b, false);
                }
                Situations.Resume(A);   // and goes for it now
            }
        }
    }
}
