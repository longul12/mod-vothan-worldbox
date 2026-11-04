using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// DANCING STEEL (Thousand Blades, Master). A dance through the enemy line: it glides from foe to
    /// foe on a curving path a hand's breadth off the ground, turning and turning as it goes (no blur:
    /// every turn is seen), arrives beside each one in the middle of a turn and cuts it once with a
    /// full-circle sweep, then glides on. Petals of light swirl from its blade and drift down along
    /// the whole path. The last foe gets the end of the dance: a higher, wider turn that knocks it
    /// down. A weapon cuts in steel; bare hands cut with a blade of ki.
    /// I: 3 foes; II: 4, the last one knocked down; III: 6, the last cut sweeps everyone beside it.
    /// Damage (x the user's damage, one cut each): I 1.4, II 1.8, III 2.3 (the last x1.3).
    /// Distinct from Thousand Steps (afterimages blinking) and the dashes: an unhurried, visible,
    /// spinning glide, petals, one clean cut per foe.
    /// </summary>
    internal sealed class DancingSteelTech : Technique
    {
        public override string Id => "dancing_steel";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Chain;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Dash | Form.Spin;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 24f, 42f, 66f };
        public override float[] Cooldown => new[] { 11f, 12f, 13f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered || link == Link.Close;
        }

        public static int MaxFoes(int level)
        {
            return level <= 1 ? 3 : level == 2 ? 4 : 6;
        }

        public static float Mult(int level)
        {
            return level <= 1 ? 1.4f : level == 2 ? 1.8f : 2.3f;
        }

        private static float Hop(int level)
        {
            return level <= 1 ? 5f : level == 2 ? 6f : 7f;
        }

        private static readonly List<Actor> _chain = new List<Actor>(8);

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0 || c.Linked && !c.Forced && c.F != null && c.F.LastTech == this)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            float hop = Hop(level) * k;
            float reach = hop * 1.6f;
            int max = MaxFoes(level);
            _chain.Clear();
            Vector2 at = c.Pos;
            // a dancer's path: always the nearest next foe it can glide to
            while (_chain.Count < max)
            {
                Actor best = null;
                float bd = float.MaxValue;
                for (int i = 0; i < c.Enemies.Count && i < 16; i++)
                {
                    Actor v = c.Enemies[i];
                    if (c.EnemyDist[i] > (c.Forced ? Mathf.Max(reach, 8f) : reach) || _chain.Contains(v))
                    {
                        continue;
                    }
                    if (!Choreo.Fair(c.Self, v, out _, c.Forced) || !Combat.Grounded(v) || v.isInLiquid() && !Choreo.Wading(v.current_position))
                    {
                        continue;
                    }
                    float d = Vector2.Distance(at, v.current_position);
                    if (d > (c.Forced ? Mathf.Max(hop, 8f) : hop) || d >= bd)
                    {
                        continue;
                    }
                    if (d > 1.2f && !Combat.ClearPath(at, v.current_position))
                    {
                        continue;
                    }
                    best = v;
                    bd = d;
                }
                if (best == null)
                {
                    break;
                }
                _chain.Add(best);
                at = best.current_position;
            }
            int n = _chain.Count;
            if (n == 0)
            {
                return null;
            }
            if (!c.Forced && n < 2 && !c.Linked)
            {
                return null;   // a dance needs partners
            }
            float score = 0f;
            for (int i = 0; i < n; i++)
            {
                Actor v = _chain[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, Mult(level) * (i == n - 1 ? 1.3f : 1f)), cm);
            }
            // gliding through a crowd that is wearing it down is what it is for
            if (c.Surrounded)
            {
                score *= 1.15f;
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            Actor first = _chain[0];
            Vector2 dir = first.current_position - c.Pos;
            return new Plan
            {
                Target = first,
                Point = first.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = n,
                Score = score,
                Others = new List<Actor>(_chain),
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Dance m = new Dance(a, this, p);
            if (!m.Build(p.Others))
            {
                return false;
            }
            return m.Start();
        }

        /// <summary>The move: for each partner a curving, turning glide and one sweeping cut; then the final pose.</summary>
        private sealed class Dance : ChoreoMove
        {
            private readonly List<Actor> _foes = new List<Actor>(8);
            private readonly List<long> _ids = new List<long>(8);
            private readonly bool[] _picked = new bool[8];
            private int _cur = -1;
            private Hue _blade;
            private float _petalT, _curve, _turns, _baseFace;

            public Dance(Actor a, Technique t, Plan p) : base(a, t, p)
            {
            }

            public bool Build(List<Actor> foes)
            {
                if (foes == null || foes.Count == 0)
                {
                    return false;
                }
                for (int i = 0; i < foes.Count && i < 8; i++)
                {
                    if (foes[i] != null)
                    {
                        _foes.Add(foes[i]);
                        _ids.Add(foes[i].Sid());
                    }
                }
                int n = _foes.Count;
                if (n == 0)
                {
                    return false;
                }
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Dash | Form.Spin;
                // a light step into the dance
                Add(new Beat { Tempo = Tempo.Wind, Dur = 0.1f, To = new Shape(-8f, 0.94f, 1.06f, 0f, -0.03f), Ease = Ease.OutQuad, Forms = show });
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    bool last = i == n - 1;
                    // the glide: a curving path, turning all the way
                    Add(new Beat
                    {
                        Tempo = Tempo.Travel,
                        Dur = 0.1f,
                        PerTile = 0.05f,
                        Route = Route.Line,
                        PathEase = Ease.InOutSine,
                        Dest = mm => ((Dance)mm).GlideSpot(),
                        Chase = true,
                        To = new Shape(-6f, 1f, 1f, last ? 0.22f : 0.12f, 0f),
                        Ease = Ease.OutQuad,
                        Ghosts = 0.04f,
                        Forms = show,
                        If = mm => ((Dance)mm).Pick(idx),
                        Enter = mm => ((Dance)mm).EnterGlide(last),
                        Every = (mm, k) => ((Dance)mm).TickGlide(k),
                        Exit = mm => ((Dance)mm).EndTurn()
                    });
                    // the cut: a full turn of the blade round its body
                    Add(new Beat
                    {
                        Tempo = Tempo.Act,
                        Dur = last ? 0.12f : 0.09f,
                        To = new Shape(10f, 1.18f, 0.88f, 0f, 0.1f),
                        Ease = Ease.OutExpo,
                        Forms = show,
                        If = mm => ((Dance)mm).Has(idx),
                        Enter = mm => ((Dance)mm).EnterCut(last),
                        Every = (mm, k) => ((Dance)mm).TurnCut(k)
                    });
                    At(0.55f, mm => ((Dance)mm).Cut(last));
                    Add(new Beat
                    {
                        Tempo = Tempo.Follow,
                        Dur = last ? 0.12f : 0.05f,
                        To = new Shape(4f, 1.04f, 0.98f, last ? 0f : 0.04f, 0.04f),
                        Ease = Ease.OutQuad,
                        Track = false,
                        If = mm => ((Dance)mm).Has(idx),
                        Exit = mm => ((Dance)mm).EndTurn()
                    });
                }
                // the end of the dance: the blade flicked clean, petals settling all round
                Add(new Beat
                {
                    Tempo = Tempo.Follow,
                    Dur = 0.16f,
                    To = new Shape(-8f, 0.94f, 1.08f, 0f, 0f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Enter = mm => ((Dance)mm).Bow()
                });
                Recover(0.26f);
                return true;
            }

            /// <summary>Takes partner 'i' (or the next one still standing): false if nobody is left.</summary>
            private bool Pick(int i)
            {
                for (int j = i; j < _foes.Count; j++)
                {
                    if (_picked[j])
                    {
                        continue;
                    }
                    Actor v = _foes[j];
                    if (v == null || v.Sid() != _ids[j] || !Choreo.Fair(A, v, out _, Forced) || !Combat.Grounded(v))
                    {
                        _picked[j] = true;
                        continue;
                    }
                    Vector2 g = Victims.GroundOf(v);
                    if (Vector2.Distance(Stance, g) > Hop(Level) * 1.8f * Kk + (Forced ? 4f : 0f) || !Combat.ClearPath(Stance, g))
                    {
                        _picked[j] = true;
                        continue;
                    }
                    _picked[j] = true;
                    _cur = j;
                    SetVictim(v);
                    return true;
                }
                _cur = -1;
                return false;
            }

            private bool Has(int i)
            {
                return _cur >= 0 && _cur >= i && V != null && V.Sid() == VId;
            }

            /// <summary>Where the glide ends: beside its partner, on the near side.</summary>
            private Vector2 GlideSpot()
            {
                return V != null ? StandSpot(Stand) : Stance;
            }

            private void EnterGlide(bool last)
            {
                _petalT = 0f;
                _baseFace = Fwd;
                float d = V != null ? Vector2.Distance(Stance, VG) : 1f;
                // the longer the glide, the wider the curve and the more turns
                _curve = Mathf.Clamp(d * 0.16f, 0.15f, 0.9f) * (Random.value < 0.5f ? -1f : 1f);
                _turns = Mathf.Clamp(Mathf.Round(d / 2.2f), 1f, 3f) + (last ? 1f : 0f);
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile, 0.05f);
                if (Vis)
                {
                    Fx.LandingPuff(Stance, Px, A.current_tile);
                }
            }

            private void TickGlide(float k)
            {
                // the path bends: a sideways swing that is zero at both ends
                Vector2 off = Side * _curve * Mathf.Sin(Mathf.PI * k) * Kk;
                Vector2 g = Pose.Ground + off;
                if (Combat.SafeSpot(g) || Choreo.Wading(g))
                {
                    Pose.Ground = g;
                }
                // it turns all the way (a 2D turn: the sprite narrows, flips, widens)
                float t = Beat.Apply(Ease.InOutSine, k) * _turns;
                float c = Mathf.Cos(Mathf.PI * 2f * t);
                Pose.SX *= Mathf.Max(0.22f, Mathf.Abs(c));
                float frac = t - Mathf.Floor(t);
                bool back = frac > 0.25f && frac < 0.75f;
                Pose.FaceRight = back ? _baseFace < 0f : _baseFace > 0f;
                Pose.Rot += 6f * Mathf.Sin(Mathf.PI * 2f * t);
                // the blade held out as it turns, a trail of petals
                Vector2 dir = new Vector2((Pose.FaceRight ? 1f : -1f) * Mathf.Max(0.3f, Mathf.Abs(c)), -0.25f);
                TbHiArt.Blade(this, BodyPos() + new Vector2(0f, 0.5f) * Px, dir, Hpx * 0.55f, _blade);
                _petalT -= Clock.Dt;
                if (_petalT <= 0f && Vis)
                {
                    _petalT = 0.035f;
                    TbHiArt.Petals(Pose.Ground, Pose.Height + Hpx * 0.45f * Px, Px, Level >= 3 ? 2 : 1, Hue, 12f, 10f, 0.9f);
                }
            }

            /// <summary>The turn of a glide or a cut is done: it faces its way again.</summary>
            private void EndTurn()
            {
                Pose.FaceRight = Fwd > 0f;
            }

            private void EnterCut(bool last)
            {
                _baseFace = Fwd;
                if (!Vis)
                {
                    return;
                }
                int r = Mathf.Clamp(Mathf.RoundToInt((last ? 12f : 10f) * Mathf.Lerp(1f, Kk, 0.6f) * (Level >= 3 ? 1.15f : 1f)), 5, 60);
                Kit.Swish(this, Art.Arc(r, 0.42f, 0f, 360f, last || Level >= 3 ? 3 : 2), BeatDur * 2.2f + 0.05f, new Vector2(0f, -1f), Fwd, Hue.Core, Hue.Edge, 6);
                if (last && Level >= 2)
                {
                    Kit.Swish(this, Art.Arc(r + 3, 0.42f, 180f, 540f, 1), BeatDur * 2.6f + 0.05f, new Vector2(0f, -1f), Fwd, Hue.Core, Hue.Rim, 5);
                }
            }

            private void TurnCut(float k)
            {
                float c = Mathf.Cos(Mathf.PI * 2f * Mathf.Clamp01(k / 0.55f));
                Pose.SX *= Mathf.Max(0.25f, Mathf.Abs(c));
                float t = Mathf.Clamp01(k / 0.55f);
                Pose.FaceRight = t > 0.25f && t < 0.75f ? _baseFace < 0f : _baseFace > 0f;
            }

            /// <summary>The blade meets its partner once.</summary>
            private void Cut(bool last)
            {
                Pose.FaceRight = Fwd > 0f;
                if (!VOk || !Touch(V))
                {
                    return;   // (a partner that is gone is skipped; an answered cut ends the dance in the engine's recoil)
                }
                int level = Level;
                bool end = last || _cur == _foes.Count - 1;
                Hit h = new Hit(Blow.SpinCut, Mult(level) * (end ? 1.3f : 1f))
                {
                    Hurt = end && level >= 2 ? Sakuga.Hurt.Knockdown : Sakuga.Hurt.Spin,
                    Push = end ? 0.8f : 0.5f,
                    Lethal = true,
                    Finisher = end && level >= 2,
                    Power = end ? 1.3f : 1f,
                    Area = end && level >= 3 ? 1.5f : 0f,
                    AreaMult = 0.5f
                };
                Vector2 at = VPoint(0.55f);
                Deliver(V, h, ShapeOf(Blow.SpinCut), at, Dir);
                if (Vis)
                {
                    TbHiArt.Petals(VG, VHeight * 0.55f, Px, end ? 14 : 8, Hue, end ? 50f : 34f, 26f, 0.9f);
                    Kit.Sparks(at, Px, 3, 50f, 100f, _blade.Edge);
                }
                if (end)
                {
                    // the end of the dance breaks the ground it lands on (a Master's cut spares nothing)
                    Ruin.Blast(A, VG, 0.6f, Scale, Pose.Ground, false);
                }
            }

            private void Bow()
            {
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile, 0.1f);
                if (!Vis)
                {
                    return;
                }
                // the blade flicked clean, a glint, petals rising and settling round it
                Fx.Glint(Hand(Limb.Blade) + new Vector2(0f, 1.5f * Px), Px, 0.2f, Color.white, 1f);
                TbHiArt.Petals(Pose.Ground, Hpx * 0.8f * Px, Px, Level >= 3 ? 16 : 10, Hue, 22f, 30f, 1.1f);
            }
        }
    }
}
