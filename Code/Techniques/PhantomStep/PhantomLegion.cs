using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// PHANTOM LEGION (Phantom Step, Grandmaster). It crosses its arms and its outline shivers, doubling
    /// and redoubling like a cell about to divide... then it splits: six to ten violet copies of itself
    /// peel off its body in a fan, each a slightly different shade, and the legion charges, every copy
    /// at a different enemy, each throwing its own blow (a cross, a snap kick, a hook, a knee...) while
    /// the original takes the strongest foe itself. The copies are its own blows (real damage, only to
    /// enemies: Clone strikes go through Combat.Reach and Combat.Hit); with more copies than foes, the
    /// spare ones gang up on the toughest from other sides. Then they dissolve.
    ///
    /// Levels: I six copies, one blow each (x0.9), dissolving in violet smoke. II eight copies, a
    /// two-blow combination each (x1 + x0.5). III ten copies, two blows (x1.1 + x0.6), and the legion
    /// streams back into the original in a flash (it re-forms from them). The original's own blow:
    /// x1.4 of a copy's, a finisher.
    /// </summary>
    internal sealed class PhantomLegionTech : Technique
    {
        public override string Id => "phantom_legion";
        public override Tier Entry => Tier.Grandmaster;
        public override Role Role => Role.Area;
        public override School School => School.PhantomStep;
        public override Form Forms => Form.Clone | Form.Area;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 45f, 70f, 105f };
        public override float[] Cooldown => new[] { 16f, 17f, 18f };

        public override bool Accepts(Link link)
        {
            return link == Link.Scattered;
        }

        private static int Copies(int level)
        {
            return level <= 1 ? 6 : level == 2 ? 8 : 10;
        }

        private static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 8f : 9f;
        }

        private static float First(int level)
        {
            return level <= 1 ? 0.9f : level == 2 ? 1f : 1.1f;
        }

        private static float Second(int level)
        {
            return level <= 1 ? 0f : level == 2 ? 0.5f : 0.6f;
        }

        private static readonly Blow[] Firsts = { Blow.Cross, Blow.SnapKick, Blow.Hook, Blow.Knee, Blow.Palm, Blow.SideKick, Blow.Elbow, Blow.Uppercut, Blow.Backfist, Blow.Body };
        private static readonly Blow[] Seconds = { Blow.Hook, Blow.Elbow, Blow.Cross, Blow.Uppercut, Blow.SideKick, Blow.Palm, Blow.Knee, Blow.Cross, Blow.SnapKick, Blow.Hook };

        private static readonly List<Actor> _scratch = new List<Actor>();

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float r = Range(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 2f : 0f);
            int copies = Copies(level);
            List<Actor> list = _scratch;
            list.Clear();
            for (int i = 0; i < c.Enemies.Count && i < 24 && list.Count < copies; i++)
            {
                if (c.EnemyDist[i] > r)
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (Choreo.Fair(c.Self, v, out _, c.Forced) && PsHiKit.Standing(v))
                {
                    list.Add(v);
                }
            }
            int need = c.Forced ? 1 : c.Linked ? 2 : 3;
            if (list.Count < need)
            {
                return null;
            }
            // the original takes the strongest it has a clear way to; the copies share the rest
            Actor main = null;
            float mainPower = -1f;
            foreach (Actor v in list)
            {
                float pw = Tiers.Power(v) + v.getHealth() * 0.001f;
                if (pw > mainPower && Combat.ClearPath(c.Pos, v.current_position))
                {
                    mainPower = pw;
                    main = v;
                }
            }
            if (main == null)
            {
                main = list[0];
            }
            float score = 0f;
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                Actor v = list[i];
                c.Allowed(v, out float cm);
                // copies per foe: one each, the spares on the toughest (the original's own)
                int on = copies / n + (i < copies % n ? 1 : 0);
                float mult = on * (First(level) + Second(level)) + (v == main ? 1.4f * First(level) : 0f);
                score += c.Value(v, c.Expected(v, mult), cm);
            }
            if (c.Forced)
            {
                score = Mathf.Max(score, 1f);
            }
            return new Plan
            {
                Target = main,
                Point = c.Pos,
                Dir = PsHiKit.DirTo(c.Pos, main.current_position),
                Count = n,
                Score = score,
                Others = new List<Actor>(list),
                AreaRadius = r * 0.6f,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null || m.V == null)
            {
                return false;
            }
            int level = m.Level;
            int copies = Copies(level);
            List<Actor> foes = new List<Actor>();
            List<long> ids = new List<long>();
            foreach (Actor v in p.Others ?? new List<Actor> { p.Target })
            {
                if (v != null)
                {
                    foes.Add(v);
                    ids.Add(v.Sid());
                }
            }
            // spare copies go to the toughest first (the list is nearest first: sort a copy by power)
            List<Actor> byPower = new List<Actor>(foes);
            byPower.Sort((x, y) => Tiers.Power(y).CompareTo(Tiers.Power(x)));
            Actor[] assign = new Actor[copies];
            for (int i = 0; i < copies; i++)
            {
                assign[i] = i < foes.Count ? foes[i] : byPower[(i - foes.Count) % byPower.Count];
            }
            Form shows = Form.Clone | Form.Area;
            float shiver = 0f;
            // it crosses its arms, and its outline shivers like a cell about to divide
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.2f,
                To = new Shape(-5f, 1.1f, 0.9f, 0f, -0.04f),
                Ease = Ease.OutQuad,
                Forms = shows,
                Tremble = true,
                Enter = mm =>
                {
                    if (mm.Vis)
                    {
                        ChoreoFx.Motes(mm.BodyPos(), mm.Px, 10, 14f * mm.Kk, 0.2f, mm.Hue.Core, mm.Hue.Edge);
                        PsHiKit.Dial(mm.Stance, mm.Px, 4f * mm.Kk, 14f * mm.Kk, 0.4f, mm.Hue.Edge);
                    }
                    for (int i = 0; i < foes.Count; i++)
                    {
                        Combat.Pin(foes[i], 1.2f);   // (they stand and stare)
                    }
                },
                Every = (mm, k) =>
                {
                    shiver -= Clock.Dt;
                    if (shiver <= 0f && mm.Vis)
                    {
                        shiver = 0.045f;
                        float side = Random.value < 0.5f ? -1f : 1f;
                        Fx.Ghost(mm, new Vector2(side * (1.5f + 2.5f * k) * mm.Px, 0f), 0f, 0.08f, Fx.WithA(mm.Hue.Main, 0.7f), Fx.WithA(mm.Hue.Edge, 0f));
                    }
                }
            });
            // the split: the copies peel off its body in a fan and charge
            Vector2 toward = PsHiKit.DirTo(m.Stance, m.VG, m.Dir);
            m.Do(mm =>
            {
                if (mm.Vis)
                {
                    Kit.Flash(mm.BodyPos(), mm.Px, Mathf.RoundToInt(5f * mm.Kk), 0.08f, Fx.WithA(mm.Hue.Core, 0.95f));
                    Fx.Ring(mm.Stance, mm.Px, 0.25f, 2f, 16f * mm.Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.4f, Fx.WithA(mm.Hue.Core, 0.9f), Fx.WithA(mm.Hue.Edge, 0f));
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", mm.A.current_tile);
            });
            m.Clones(copies, 0.2f, (mm, c, i) =>
            {
                Actor v = assign[i];
                // each a slightly different shade of the legion
                float shade = (i % 3) * 0.18f;
                c.Hue.Main = Color.Lerp(mm.Hue.Main, (i & 1) == 0 ? Color.white : mm.Hue.Edge, shade);
                c.Hue.Edge = Color.Lerp(mm.Hue.Edge, mm.Hue.Rim, shade * 0.6f);
                // the fan: out to the sides of the line towards the foes, then the charge
                float spread = (i - (copies - 1) * 0.5f) / Mathf.Max(1f, copies - 1);
                Vector2 fan = mm.Stance + Choreo.Rotate(toward, spread * 150f) * (0.9f + 0.3f * (i & 1)) * mm.Kk;
                c.DashTo(Combat.SafeSpot(fan) ? fan : mm.Stance, 0.09f).Wait(0.025f * i);
                if (v != null && v.Sid() == ids[Mathf.Max(0, foes.IndexOf(v))])
                {
                    c.Strike(v, Firsts[i % Firsts.Length], First(level));
                    if (level >= 2)
                    {
                        c.Wait(0.02f).Strike(v, Seconds[i % Seconds.Length], Second(level));
                    }
                }
                if (level >= 3)
                {
                    // they stream back into the original
                    c.Wait(0.05f + 0.015f * i).DashTo(() => mm.Stance, 0.12f).Vanish(0.06f);
                }
                else
                {
                    c.Vanish(0.18f);
                }
            });
            // the original takes the strongest itself (its own blow: if the legion got there first, it
            // strikes the air and waits for its copies, it doesn't leave them)
            m.Dash(ChoreoMove.ToTarget, 0.024f);
            PsHiKit.Blow(m, Blow.Cross, 0.7f, 1f, mm => Own(mm, level), Form.Clone | Form.Punch, 1.4f);
            m.WaitClones(2.6f);
            m.Do(mm =>
            {
                if (!mm.Vis)
                {
                    return;
                }
                if (mm.Level >= 3)
                {
                    // re-formed: a flash, a ring, the last of them sinking into its body
                    Kit.Flash(mm.BodyPos(), mm.Px, Mathf.RoundToInt(5f * mm.Kk), 0.1f, Fx.WithA(mm.Hue.Core, 0.95f));
                    Fx.Ring(mm.Stance, mm.Px, 0.3f, 14f * mm.Kk, 2f, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f, Fx.WithA(mm.Hue.Edge, 0.9f), Fx.WithA(mm.Hue.Core, 0f));
                    Fx.Afterimage(mm, 0.25f, Fx.WithA(mm.Hue.Core, 0.9f), mm.Hue.GhostEnd);
                }
                else
                {
                    ChoreoFx.Smoke(mm.BodyPos(), mm.Px, 4, 6f, Fx.WithA(mm.Hue.Main, 0.6f), 0.5f);
                }
            });
            m.Recover(0.24f);
            return m.Start();
        }

        /// <summary>The original's blow on the strongest (or on the air, if the legion already finished it).</summary>
        private static void Own(ChoreoMove m, int level)
        {
            if (!m.VOk)
            {
                PsHiKit.Whiff(m);
                return;
            }
            Actor v = m.V;
            if (Vector2.Distance(m.Pose.Ground, m.VG) > m.Stand + 0.8f * m.Kk + v.stats["size"] * 0.5f + 0.4f)
            {
                PsHiKit.Whiff(m);
                return;
            }
            if (!m.Touch(v))
            {
                return;
            }
            Hit h = new Hit(Blow.Cross, 1.4f * First(level)) { Hurt = Hurt.Reel, Push = 1.3f, Finisher = true, Lethal = true, Power = 1.2f };
            m.Deliver(v, h, ChoreoMove.ShapeOf(Blow.Cross), m.ContactPoint(0.64f), PsHiKit.DirTo(m.Stance, m.VG, m.Dir));
        }
    }
}
