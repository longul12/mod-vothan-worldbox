using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// GODHAND RUSH (Iron Fist, God). A breath, gold kindling in its eyes, and the God is gone: in the
    /// same instant a golden figure of it stands beside every enemy within 8-12 tiles, and golden fists
    /// rain into all of them at once from every side (each figure throws a jab, a cross, a hook). The
    /// figures fade; the God is simply there again, in front of the one that mattered most, and ends
    /// it with a single crushing straight: the foe blasted away, a crater, a golden ring rolling out.
    /// Not a clone army marching out (it never leaves in a line): it is everywhere at once.
    /// I: 6 foes within 8 tiles, two blows each. II: 9 within 10, three blows. III (a God's): 12 within
    /// 12, three blows, a bigger finish. Damage (x the user's damage): each golden blow I 0.5, II 0.6,
    /// III 0.7; the finishing straight I 2.8, II 3.6, III 4.6.
    /// </summary>
    internal sealed class GodhandRushTech : Technique
    {
        public override string Id => "godhand_rush";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Area;
        public override School School => School.IronFist;
        public override Form Forms => Form.Teleport | Form.Flurry | Form.Punch | Form.Area;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 60f, 85f, 110f };
        public override float[] Cooldown => new[] { 45f, 55f, 65f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        public static float Radius(int level)
        {
            return level <= 1 ? 8f : level == 2 ? 10f : 12f;
        }

        public static int MaxTargets(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 9 : 12;
        }

        public static int Blows(int level)
        {
            return level <= 1 ? 2 : 3;
        }

        public static float BlowMult(int level)
        {
            return level <= 1 ? 0.5f : level == 2 ? 0.6f : 0.7f;
        }

        public static float Finisher(int level)
        {
            return level <= 1 ? 2.8f : level == 2 ? 3.6f : 4.6f;
        }

        private static readonly List<Actor> _list = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float r = Radius(level) * Mathf.Lerp(1f, Choreo.KOf(c.Self), 0.4f) + (c.Forced ? 4f : 0f);
            int max = MaxTargets(level);
            _list.Clear();
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= r && _list.Count < max; i++)
            {
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out _, c.Forced) || !Combat.Grounded(v) || Moves.Locked(v))
                {
                    continue;
                }
                if (v.isInLiquid() && !Choreo.Wading(v.current_position) || !Combat.FindLanding(v.current_position, 1.2f, out _))
                {
                    continue;
                }
                _list.Add(v);
            }
            if (_list.Count == 0)
            {
                return null;
            }
            // the one that matters most: its focus if it is among them, else the strongest
            Actor main = c.Focus != null && _list.Contains(c.Focus) ? c.Focus : null;
            if (main == null)
            {
                float best = -1f;
                for (int i = 0; i < _list.Count; i++)
                {
                    float pw = Tiers.Power(_list[i]);
                    if (pw > best)
                    {
                        best = pw;
                        main = _list[i];
                    }
                }
            }
            float blows = Blows(level) * BlowMult(level);
            float score = 0f;
            int worthy = 0;
            for (int i = 0; i < _list.Count; i++)
            {
                Actor v = _list[i];
                c.Allowed(v, out float cm);
                score += c.Value(v, c.Expected(v, blows + (v == main ? Finisher(level) : 0f)), cm);
                if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Brute || v.getHealth() > c.Expected(v, 2f) * cm)
                {
                    worthy++;
                }
            }
            if (!c.Forced)
            {
                int need = c.Linked ? 2 : 3;
                if (_list.Count < need || worthy == 0 && _list.Count < 5)
                {
                    return null;   // everywhere at once is for an army, or for real foes
                }
            }
            else
            {
                score = Mathf.Max(score, 1f);
            }
            Vector2 dir = main.current_position - c.Pos;
            return new Plan
            {
                Target = main,
                Point = main.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = _list.Count,
                Score = score,
                Others = new List<Actor>(_list),
                AreaRadius = r * 0.5f,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            GodhandRushMove m = GodhandRushMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build(p.Others, p.Target);
            return m.Start();
        }
    }

    /// <summary>The Godhand Rush's move: the breath, gone, a golden figure at every foe, back for the finish.</summary>
    internal sealed class GodhandRushMove : ChoreoMove
    {
        private readonly List<Actor> _targets = new List<Actor>(12);
        private readonly List<long> _ids = new List<long>(12);
        private readonly List<Clone> _figures = new List<Clone>(12);
        private Actor _main;
        private long _mainId = -1;
        private Hue _gold;
        private float _fistT, _auraT;
        private Vector2 _home;

        private GodhandRushMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
            _gold = Hue.As(Element.Holy);
        }

        public static GodhandRushMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new GodhandRushMove(a, t, p);
        }

        private const Form Shown = Form.Teleport | Form.Flurry | Form.Punch | Form.Area;

        public void Build(List<Actor> list, Actor main)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !_targets.Contains(list[i]))
                    {
                        _targets.Add(list[i]);
                        _ids.Add(list[i].Sid());
                    }
                }
            }
            if (main != null && !_targets.Contains(main))
            {
                _targets.Insert(0, main);
                _ids.Insert(0, main.Sid());
            }
            _main = main;
            _mainId = main != null ? main.Sid() : -1;
            int level = Level;
            // a breath: gold kindling in its eyes
            PoseTo(0.15f, new Shape(-6f, 0.94f, 1.08f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Shown)
                .OnEnter(mm => ((GodhandRushMove)mm).Breath())
                .Every((mm, k) => ((GodhandRushMove)mm).Aura(k));
            // gone
            Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.04f,
                To = new Shape(12f, 1.2f, 0.84f, 0f, 0.05f),
                Ease = Ease.OutExpo,
                Intangible = true,
                Track = false,
                Forms = Shown,
                Exit = mm => ((GodhandRushMove)mm).Vanish()
            });
            // everywhere at once
            Add(new Beat
            {
                Tempo = Tempo.Free,
                Dur = 0.3f,
                Hidden = true,
                HideShadow = true,
                Intangible = true,
                Track = false,
                Forms = Shown,
                Enter = mm => ((GodhandRushMove)mm).Everywhere(),
                Every = (mm, k) => ((GodhandRushMove)mm).Rain(),
                Until = mm => ((GodhandRushMove)mm).FiguresDone(),
                MaxDur = 1.6f
            });
            At(0.35f, mm => Kit.Stop(mm.Stance, 0.05f * mm.ImpactK));
            // back: in front of the one that mattered most (still unseen until it is there)
            Do(mm => ((GodhandRushMove)mm).Return());
            Last.Hidden = Last.HideShadow = true;
            Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.04f,
                Route = Route.Blink,
                Dest = ToTarget,
                Hidden = true,
                HideShadow = true,
                Intangible = true,
                Track = false,
                Forms = Form.Teleport | Form.Punch,
                Exit = mm => ((GodhandRushMove)mm).Reappear()
            });
            Strike(new Hit(Blow.Cross, GodhandRushTech.Finisher(level))
            {
                Hurt = Hurt.Launch,
                Push = level <= 1 ? 4f : level == 2 ? 5f : 6f,
                Lift = level <= 1 ? 2f : level == 2 ? 2.4f : 2.8f,
                Power = 1.8f,
                Finisher = true,
                Lethal = true,
                Crater = level <= 1 ? 1f : level == 2 ? 1.2f : 1.5f,
                Area = 1.6f,
                AreaMult = 0.4f,
                WindScale = 0.7f,
                Shows = Form.Punch | Form.Straight | Form.Heavy,
                OnLand = (mm, v, r) => ((GodhandRushMove)mm).Finish(v)
            });
            Recover(0.35f);
        }

        private void Breath()
        {
            _home = Stance;
            Choreo.Sound("event:/SFX/POWERS/Powerup", A.current_tile, 0.2f);
            if (Vis)
            {
                Fx.Glint(HeadPos(), Px, 0.2f, _gold.Core, 1.1f);
                Fx.Ring(Pose.Ground, Px, 0.22f, 12f * Kk, 3f, 1, 1, FxLayer.Ground, 4, Art.Ground, 1.6f, Fx.WithA(_gold.Edge, 0f), Fx.WithA(_gold.Edge, 0.85f));
            }
        }

        private void Aura(float k)
        {
            if (!Vis)
            {
                return;
            }
            _auraT -= Clock.Dt;
            if (_auraT <= 0f)
            {
                _auraT = 0.06f;
                Particle g = Fx.Ghost(this, new Vector2(Random.Range(-1f, 1f), Random.Range(0f, 1.5f)) * Px, 0f, 0.14f,
                    Fx.WithA(_gold.Edge, 0.35f + 0.35f * k), Fx.WithA(_gold.Edge, 0f));
                g?.Sized(1f, 1.1f);
            }
        }

        /// <summary>It is gone: a golden pop where it stood.</summary>
        private void Vanish()
        {
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Fx.Afterimage(this, 0.08f, new Color(1f, 1f, 1f, 0.95f), new Color(1f, 1f, 1f, 0f));
            Fx.Afterimage(this, 0.3f, Fx.WithA(_gold.Ghost, 0.8f), Fx.WithA(_gold.Edge, 0f));
            Kit.Flash(BodyPos(), px, Mathf.RoundToInt(4f * Kk), 0.07f, Fx.WithA(_gold.Core, 0.95f), TopLayer);
            Fx.Ring(Pose.Ground, px, 0.24f, 3f, 16f * Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(_gold.Edge, 0.6f), Fx.WithA(_gold.Edge, 0f));
        }

        /// <summary>A golden figure of it beside every foe at once: a jab, a cross (and a hook), and it fades.</summary>
        private void Everywhere()
        {
            int level = Level;
            float mult = GodhandRushTech.BlowMult(level);
            int blows = GodhandRushTech.Blows(level);
            // from here on everything it does is gold: the figures, their rings, their blows' sparks, the finish
            Hue = _gold;
            for (int i = 0; i < _targets.Count; i++)
            {
                Actor v = _targets[i];
                if (v == null || v.Sid() != _ids[i] || !Choreo.Fair(A, v, out _, true))
                {
                    continue;
                }
                Vector2 g = Victims.GroundOf(v);
                Vector2 d = g - _home;
                d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
                // beside it, from any side (never two from the same side in a row)
                float ang = ((i & 1) == 0 ? 0f : 180f) + Random.Range(-60f, 60f);
                float stand = 0.75f * Kk + v.stats["size"] * 0.4f;
                Vector2 at = Combat.Clamp(g - Choreo.Rotate(d, ang) * stand);
                if (!Combat.SafeSpot(at) && !Combat.FindLanding(at, 1.2f, out at))
                {
                    continue;
                }
                Clone c = Sakuga.Clones.Spawn(this, at, true);
                if (c == null)
                {
                    break;
                }
                c.Hue = _gold;
                c.Wait(Random.Range(0f, 0.05f)).Strike(v, Blow.Jab, mult).Strike(v, Blow.Cross, mult);
                if (blows >= 3)
                {
                    c.Strike(v, Blow.Hook, mult);
                }
                c.Vanish(0.12f);
                _figures.Add(c);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            Ruin.Quake(A, _home, Scale, 0.8f, Kk);
            if (_figures.Count == 0)
            {
                BeatDur = 0.05f;   // (nobody left to strike: straight back)
            }
        }

        private bool FiguresDone()
        {
            for (int i = 0; i < _figures.Count; i++)
            {
                if (!_figures[i].Done)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Golden fists raining into every foe from every side while the figures strike.</summary>
        private void Rain()
        {
            _fistT -= Clock.Dt;
            if (_fistT > 0f)
            {
                return;
            }
            _fistT = 0.02f;
            if (_targets.Count == 0)
            {
                return;
            }
            float px = Px;
            for (int n = 0, many = Fx.Count(2); n < many; n++)
            {
                int i = Random.Range(0, _targets.Count);
                Actor v = _targets[i];
                if (v == null || v.Sid() != _ids[i] || !v.isAlive())
                {
                    continue;
                }
                Vector2 body = Choreo.BodyAt(v, Random.Range(0.3f, 0.8f));
                if (!Kit.Visible(body, 0.2f))
                {
                    continue;
                }
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 from = body - dir * Random.Range(0.9f, 1.4f) * Kk;
                float life = 0.07f;
                Particle p = Fx.Spawn(ChoreoArt.Fist(Mathf.RoundToInt(3f * Kk)), from, px, life, FxLayer.Top, 12);
                if (p != null)
                {
                    p.Rot = a * Mathf.Rad2Deg;
                    p.Moving((body - from) / life);
                    p.Color(Color.white, Fx.WithA(_gold.Core, 0.95f), Fx.WithA(_gold.Edge, 0.2f));
                }
                Kit.Line(from, body, px, 0.06f, 1, FxLayer.Top, 11, Fx.WithA(_gold.Edge, 0.6f), Fx.WithA(_gold.Edge, 0f));
                Particle star = Fx.Spawn(Art.HitStars[0], body, px, 0.05f, FxLayer.Top, 20);
                if (star != null)
                {
                    star.Rot = Random.Range(0f, 90f);
                    star.Color(Color.white, Fx.WithA(_gold.Core, 0.9f), Fx.WithA(_gold.Edge, 0f));
                }
            }
        }

        /// <summary>Back: in front of the one that mattered most (or the nearest still standing); nobody left: where it stood.</summary>
        private void Return()
        {
            Actor pick = null;
            if (_main != null && _main.Sid() == _mainId && Choreo.Fair(A, _main, out _, true) && Combat.Grounded(_main))
            {
                pick = _main;
            }
            else
            {
                float best = float.MaxValue;
                for (int i = 0; i < _targets.Count; i++)
                {
                    Actor v = _targets[i];
                    if (v == null || v.Sid() != _ids[i] || !Choreo.Fair(A, v, out _, true) || !Combat.Grounded(v))
                    {
                        continue;
                    }
                    float d = (v.current_position - _home).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        pick = v;
                    }
                }
            }
            if (pick == null)
            {
                Lose();   // it reappears where it stood, a glint on its fist
                return;
            }
            SetVictim(pick);
        }

        /// <summary>There again, in front of it: a golden ring at its feet, its last afterimage.</summary>
        private void Reappear()
        {
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (V != null && (VG - Stance).sqrMagnitude > 1e-4f)
            {
                Face(VG - Stance);
            }
            Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
            if (!Vis)
            {
                return;
            }
            Fx.Ring(Stance, Px, 0.2f, 2f, 10f * Kk, 1, 1, FxLayer.Ground, 4, Art.Ground, 2.5f, Fx.WithA(_gold.Edge, 0.95f), Fx.WithA(_gold.Edge, 0f));
            Fx.Afterimage(this, 0.14f, Fx.WithA(_gold.Core, 0.85f), Fx.WithA(_gold.Edge, 0.5f), Fx.WithA(_gold.Edge, 0f));
        }

        /// <summary>The finishing straight landed: a golden ring rolls out from it.</summary>
        private void Finish(Actor v)
        {
            if (!Vis || v == null)
            {
                return;
            }
            float px = Px;
            Vector2 g = Victims.GroundOf(v);
            Fx.Ring(g, px, 0.4f, 4f, (24f + 4f * Level) * Kk, 3, 1, FxLayer.Ground, 6, Art.Ground, 2.2f,
                Fx.WithA(Color.white, 0.95f), Fx.WithA(_gold.Edge, 0.7f), Fx.WithA(Hue.Rim, 0f));
            Kit.Flash(Choreo.BodyAt(v, 0.5f), px, Mathf.RoundToInt(6f * Kk), 0.1f, Fx.WithA(_gold.Core, 1f), TopLayer);
            Kit.DustRing(g, px, Combat.TileAt(g), 14, 60f, 0.6f, Kk);
        }
    }
}
