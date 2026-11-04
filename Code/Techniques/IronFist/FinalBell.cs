using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// FINAL BELL (Iron Fist, God). The instant the foe commits (its counter, its charge, its ultimate,
    /// its heaviest blow), the God is already there: no wind-up to speak of, one crushing straight. The
    /// world stops on the impact (a frame of white, black at III), and a bell tolls: rings of gold and
    /// crimson roll out from the blow, one, two, three, the ground breaking under the foe. Whatever the
    /// foe was doing breaks (a charge bursts, a move is cut off) and it is knocked out cold: thrown
    /// down and left lying, stunned for seconds. The counter to counters (a God reads three deep),
    /// to wind-ups (it blinks across up to 6-10 tiles to a foe gathering power or throwing its
    /// ultimate) and to the biggest blows. A long cooldown.
    /// I: x4, out cold 2.4 s, a 6-tile blink. II: x5, 3.2 s, 8 tiles. III (a God's): x6, 4 s, 10 tiles.
    /// </summary>
    internal sealed class FinalBellTech : Technique
    {
        public override string Id => "final_bell";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.IronFist;
        public override Form Forms => Form.Punch | Form.Counter | Form.Heavy;
        public override Form Answers => Form.Counter | Form.Ultimate | Form.Heavy | Form.Charge;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 30f, 45f, 60f };
        public override float[] Cooldown => new[] { 40f, 45f, 50f };

        public static float Mult(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6f;
        }

        public static float Out(int level)
        {
            return level <= 1 ? 2.4f : level == 2 ? 3.2f : 4f;
        }

        public static float BlinkRange(int level)
        {
            return level <= 1 ? 6f : level == 2 ? 8f : 10f;
        }

        /// <summary>Is this attacker worth the bell (a God doesn't knock a peasant out cold for a shove)?</summary>
        private static bool Worthy(Actor defender, Actor attacker)
        {
            return Tiers.Of(attacker) >= Tier.Expert || Weights.Species(attacker) >= Weight.Fiend || Threat.Ratio(defender, attacker) >= 0.15f;
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (incoming == null || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!Worthy(defender, attacker) || !Combat.Living(attacker) || !Combat.Grounded(attacker) || !Combat.Allowed(defender, attacker))
            {
                return Contact.Hit;
            }
            float d = Vector2.Distance(defender.current_position, attacker.current_position);
            if (d > BlinkRange(level) * Choreo.KOf(defender) + attacker.stats["size"] * 0.5f)
            {
                return Contact.Hit;   // (a beam from beyond its reach: no answer)
            }
            Reactions.Free(defender);
            FinalBellMove m = FinalBellMove.Answer(defender, attacker, this, level);
            if (m == null)
            {
                return Contact.Hit;
            }
            m.Build();
            if (!m.Start())
            {
                return Contact.Hit;
            }
            // an area blow it was caught in: it is simply gone from where the blow lands (never parried)
            return area ? Contact.Evaded : Contact.Parried;
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null)
            {
                return null;
            }
            float range = BlinkRange(level) * Choreo.KOf(c.Self);
            if (c.Forced)
            {
                Actor f = c.ForceTarget ?? c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
                if (f == null || c.Dist(f) > range + 4f)
                {
                    return null;
                }
                Vector2 fd = f.current_position - c.Pos;
                return new Plan { Target = f, Point = f.current_position, Dir = fd.sqrMagnitude > 1e-4f ? fd.normalized : Vector2.right, Count = 1, Score = 1f, Data = true };
            }
            // planned: a worthy foe in reach is gathering power, or throwing its ultimate: the bell answers it
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && c.EnemyDist[i] <= range; i++)
            {
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out float cm) || !Combat.Grounded(v))
                {
                    continue;
                }
                FoeState s = c.State(v);
                bool winding = s.Charging || (s.Doing & Form.Ultimate) != 0 && s.Busy;
                if (!winding)
                {
                    continue;
                }
                if (c.Threat(v) < 0.25f && Tiers.Of(v) < Tier.Expert)
                {
                    continue;   // a God doesn't ring the bell for a peasant's haymaker
                }
                if (!Combat.FindLanding(v.current_position, 1.5f, out _))
                {
                    continue;
                }
                float score = c.D * 3f + 40f + 15f * Tiers.Power(v) + c.Value(v, c.Expected(v, Mult(level)), cm);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan { Target = best, Point = best.current_position, Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right, Count = 1, Score = bestScore };
        }

        public override bool Begin(Actor a, Plan p)
        {
            FinalBellMove m = FinalBellMove.Create(a, p, this);
            if (m == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }
    }

    /// <summary>The Final Bell's move: (a blink to a foe out of reach,) the instant straight, the toll.</summary>
    internal sealed class FinalBellMove : ChoreoMove
    {
        private bool _tolled;

        private FinalBellMove(Actor a, Technique t, Plan p) : base(a, t, p)
        {
        }

        public static FinalBellMove Create(Actor a, Plan p, Technique t)
        {
            if (a == null || p == null || !Moves.CanPerform(a))
            {
                return null;
            }
            return new FinalBellMove(a, t, p);
        }

        /// <summary>The bell rung in answer to 'attacker' (its blow, its charge, its ultimate).</summary>
        public static FinalBellMove Answer(Actor defender, Actor attacker, Technique t, int level)
        {
            if (defender == null || attacker == null || !Moves.CanPerform(defender))
            {
                return null;
            }
            Vector2 d = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right,
                Count = 1,
                Data = true
            };
            return new FinalBellMove(defender, t, p);
        }

        private const Form Shown = Form.Punch | Form.Counter | Form.Heavy | Form.Straight;

        public void Build()
        {
            // a foe out of reach: a God's step (gone, and there)
            Blink(ToTarget, 0.05f).OnlyIf(mm => mm.V != null && Vector2.Distance(mm.Stance, mm.VG) > mm.Stand + 0.7f * mm.Kk);
            Last.Forms = Form.Teleport | Form.Counter;
            // the instant: barely a wind-up
            PoseTo(0.04f, new Shape(-8f, 0.94f, 1.05f, 0f, -0.05f), Ease.OutQuad, Tempo.Wind).Shows(Shown);
            // the straight
            PoseTo(0.06f, new Shape(22f, 1.32f, 0.82f, 0f, 0.2f), Ease.OutExpo, Tempo.Act).Shows(Shown)
                .OnEnter(mm =>
                {
                    if (mm.Vis)
                    {
                        Vector2 from = mm.Root(Limb.Fist);
                        Vector2 to = mm.V != null ? mm.VPoint(0.62f) : from + mm.Dir * mm.Stand;
                        ChoreoFx.Smear(from, Vector2.Lerp(to, from, 0.1f), mm.Px, 0.1f, 3, 3, mm.Hue.Core, mm.Hue.Edge, FxLayer.Over, 8);
                    }
                })
                .At(0.4f, mm => ((FinalBellMove)mm).Toll());
            // the world stops on it; the bell rings out
            Hold(0.16f, true, Tempo.Impact).Shows(Shown);
            PoseTo(0.12f, new Shape(12f, 1.14f, 0.92f, 0f, 0.16f), Ease.OutQuad, Tempo.Follow).Shows(Shown);
            Recover(0.3f).Shows(Form.Punch | Form.Counter);
        }

        /// <summary>The straight lands: the foe's move breaks, it is knocked out cold; the bell tolls.</summary>
        private void Toll()
        {
            if (_tolled)
            {
                return;
            }
            _tolled = true;
            if (!VOk && !Retarget(1.6f))
            {
                Lose();
                return;
            }
            int level = Level;
            float reach = Stand + 0.9f * Kk + V.stats["size"] * 0.5f + 0.4f;
            if (Vector2.Distance(Pose.Ground, VG) > reach)
            {
                // it moved: the fist is driven after it (a blur of the body)
                Vector2 to = StandSpot(Stand);
                if (Combat.SafeSpot(to) && Vector2.Distance(to, Stance) <= 2.5f * Kk)
                {
                    if (Vis)
                    {
                        Fx.Afterimage(this, 0.12f, Fx.WithA(Hue.Ghost, 0.7f), Hue.GhostEnd);
                    }
                    Stance = to;
                    Pose.Ground = to;
                }
                else
                {
                    Lose();
                    return;
                }
            }
            if (!Touch(V))
            {
                return;   // met by a deeper reader still: its own answer plays
            }
            Actor v = V;
            Vector2 at = ContactPoint(0.62f);
            Combat.Allowed(A, v, out float cm);
            bool charging = Moves.Charging(v);
            bool busy = Moves.TryGet(v, out Move mv) && mv.A == v && !(mv is Puppet) && !mv.Interruptible;
            HitResult r = Combat.Hit(A, v, FinalBellTech.Mult(level) * cm, true, false, 0f, this);
            LastHit = r;
            if (Combat.Living(v))
            {
                float ko = FinalBellTech.Out(level);
                if (charging && Moves.Charging(v))
                {
                    Interrupts.Break(v, A, Dir, 4f);   // the gathered power bursts out of it
                    Choreo.Daze(v, ko);
                }
                else if (busy && Moves.TryGet(v, out Move still) && still == mv && !mv.Ended)
                {
                    Choreo.Break(v, A, Dir, 3.5f + 0.5f * level);   // whatever it was doing is cut off
                    Choreo.Daze(v, ko);
                }
                else
                {
                    Impact im = Impact.Of(Hurt.Launch, Dir, 2.6f + 0.6f * level, 2f);
                    im.Lift = 1.2f + 0.2f * level;
                    im.Daze = ko;
                    im.Stop = 0.08f * ImpactK;
                    im.Owed = MeteorSlam.AtDeathsDoor(v);
                    Victims.Hurt(v, A, this, im);
                }
                SetLink(Link.Down, v);
            }
            Victims.Flash(v, Color.white, 0.12f);
            HoldFor(0.06f * ImpactK);
            Kit.Stop(at, (0.1f + 0.02f * level) * ImpactK);
            Vector2 g = Victims.GroundOf(v);
            Ruin.Blast(A, g, 0.8f + 0.15f * level, Scale, Pose.Ground);
            Ruin.Quake(A, at, Scale, 1.4f, Kk);
            Areas.Add(new FinalBellToll(this, at, g, level));
            ChoreoFx.BlowSound(Limb.Fist, 2f, false, v.current_tile);
            if (Dev.On) Dev.Trace("choreo " + Id + " final bell x" + (FinalBellTech.Mult(level) * cm).ToString("F2") + " -> " + v.asset.id + "#" + v.Sid()
                + " hp " + v.getHealth() + (r.Killed ? " killed" : "") + (charging ? " (charge broken)" : busy ? " (move broken)" : ""));
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Kit.ImpactFrame(at, px, 0.55f * Kk, Hue.Pal, level >= 3 || r.Crit, Cinematic ? FxLayer.Cine : FxLayer.Behind);
            ChoreoFx.Impact(at, Dir, px, Kk, 2f, Hue, TopLayer, true);
            Kit.Sparks(at, px, 10, 70f, 150f, Hue.Edge, TopLayer);
            Kit.Crater(g, VPx * Mathf.Max(1f, Choreo.KOf(v)), Combat.TileAt(g), true, 5f, false, Hue.Edge);
            Kit.DustRing(g, px, Combat.TileAt(g), 14, 56f, 0.6f, Kk);
        }
    }

    /// <summary>The bell's toll: three rings of gold and crimson rolling out from the blow, a beat apart, each with its knell.</summary>
    internal sealed class FinalBellToll : Hazard
    {
        private readonly Vector2 _at, _ground;
        private readonly int _level;
        private readonly Hue _gold;
        private int _rung;

        public FinalBellToll(ChoreoMove m, Vector2 at, Vector2 ground, int level) : base(m)
        {
            _at = at;
            _ground = ground;
            _level = level;
            _gold = m.Hue.As(Element.Holy);
        }

        public override bool Tick(float dt)
        {
            // (it rings on through the hit-stop it started: real time then; nothing while the game is paused)
            Age += dt > 0f ? dt : HitStop.Active || Cinema.Frozen ? Time.unscaledDeltaTime : 0f;
            int due = Age >= 0.44f ? 3 : Age >= 0.22f ? 2 : 1;
            while (_rung < due)
            {
                Ring(_rung++);
            }
            return Age < 0.6f;
        }

        private void Ring(int i)
        {
            Choreo.Sound("event:/SFX/HIT/HitMetal", Combat.TileAt(_at), 0f);
            if (i == 0)
            {
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", Combat.TileAt(_at), 0.05f);
            }
            if (!Kit.Visible(_at, 0.5f))
            {
                return;
            }
            float px = Px;
            float fade = 1f - 0.28f * i;
            FxLayer layer = M != null && M.Cinematic ? FxLayer.Cine : FxLayer.Top;
            Color c0 = i == 1 ? Hue.Edge : _gold.Core;
            // the bell's mouth: a ring standing round the blow, and its shadow rolling over the ground
            Fx.Ring(_at, px, 0.36f + 0.06f * i, 4f, (20f + 6f * _level + 8f * i) * K, 3 - Mathf.Min(i, 2), 1, layer, 7, 1.15f, 2.2f,
                Fx.WithA(Color.white, 0.95f * fade), Fx.WithA(c0, 0.75f * fade), Fx.WithA(Hue.Rim, 0f));
            Fx.Ring(_ground, px, 0.45f + 0.06f * i, 6f, (30f + 8f * _level + 10f * i) * K, 2, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                Fx.WithA(c0, 0.85f * fade), Fx.WithA(Hue.Edge, 0.5f * fade), Fx.WithA(Hue.Rim, 0f));
            if (i == 0)
            {
                Fx.FocusLines(_at, px, Fx.Count(12), 10f * K, 20f * K, 0.16f);
            }
            Fx.Glint(_at + new Vector2(0f, (6f + 2f * i) * px), px, 0.2f, Color.white, 1.2f - 0.2f * i);
        }
    }
}
