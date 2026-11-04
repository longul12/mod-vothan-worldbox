using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI CHAIN (Ki Arts, Master). It cocks its fist and flings it out: a chain of gold ki links
    /// lashes after the foe, snaking through the air (even after one that is flying, blinking away
    /// or running), and where it strikes the links coil round the body three times and lock tight
    /// (bound: it can't swing). A heartbeat's strain, the chain trembling taut, then the yank: a body
    /// it can haul is ripped off its feet and flies in along the chain, onto the fist that is waiting
    /// for it, a straight that meets it head on. One it can't haul (a giant, a dragon, a flyer high
    /// up, a body held by another) holds fast instead, and the chain hauls the fighter itself in:
    /// it zips along the links, up into the air if it must, and delivers the punch there. An answer to
    /// the slippery: the Brain favours it against airborne, blinking and evasive foes.
    /// I: 7 tiles, the punch knocks it reeling. II: 9 tiles, the punch folds it and a rising knee
    /// launches it. III: 11 tiles, after the punch the chain whips the body up over its head and
    /// slams it into the ground (a crater, the shock knocking down those near).
    /// Damage (x the user's damage): the bind 0.2; I punch 1.7; II punch 1.5 + knee 1; III punch 1.6
    /// + slam 1.8.
    /// </summary>
    internal sealed class KiChainTech : Technique
    {
        public override string Id => "ki_chain";
        public override Tier Entry => Tier.Master;
        public override Role Role => Role.Single;
        public override School School => School.KiArts;
        public override Form Forms => Form.Ki | Form.Grab | Form.AntiAir;
        public override Form Answers => Form.Aerial | Form.Teleport | Form.Evasive;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 18f, 30f, 48f };
        public override float[] Cooldown => new[] { 9f, 10f, 11f };

        /// <summary>A foe sent flying, or one that slipped away: the chain fetches it back.</summary>
        public override bool Accepts(Link link)
        {
            return link == Link.Launched || link == Link.Scattered || link == Link.Thrown;
        }

        public static float Range(int level)
        {
            return level <= 1 ? 7f : level == 2 ? 9f : 11f;
        }

        private const float Bind = 0.2f;

        private static float PunchMult(int level)
        {
            return level <= 1 ? 1.7f : level == 2 ? 1.5f : 1.6f;
        }

        public static float Total(int level)
        {
            return Bind + PunchMult(level) + (level == 2 ? 1f : 0f) + (level >= 3 ? 1.8f : 0f);
        }

        public override Plan Plan(Ctx c, int level)
        {
            float k = Choreo.KOf(c.Self);
            float range = Range(level) * Mathf.Lerp(1f, k, 0.5f) + (c.Forced ? 3f : 0f);
            Actor best = null;
            float bestScore = 0f;
            Consider(c, c.Focus, level, range, ref best, ref bestScore);
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                if (c.EnemyDist[i] > range)
                {
                    break;
                }
                if (c.Enemies[i] != c.Focus)
                {
                    Consider(c, c.Enemies[i], level, range, ref best, ref bestScore);
                }
            }
            if (best == null)
            {
                return null;
            }
            Vector2 dir = best.current_position - c.Pos;
            return new Plan
            {
                Target = best,
                Point = best.current_position,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = c.Forced ? Mathf.Max(bestScore, 1f) : bestScore,
                Data = c.Forced
            };
        }

        private static void Consider(Ctx c, Actor v, int level, float range, ref Actor best, ref float bestScore)
        {
            if (v == null || !Choreo.Fair(c.Self, v, out float cm, c.Forced))
            {
                return;
            }
            float d = c.Dist(v);
            // (at arm's length a chain is no use: a fist does it)
            if (d > range || d < (c.Forced ? 0.8f : 2.2f))
            {
                return;
            }
            bool air = !Combat.Grounded(v);
            bool haul = !air && Choreo.Holdable(v, c.Self);
            if (!air && !Combat.ClearPath(c.Pos, v.current_position))
            {
                return;   // a wall between: the chain can't reach round it
            }
            if (!haul && !Combat.SafeSpot(v.current_position) && !Choreo.Wading(v.current_position))
            {
                return;   // it would have to zip out over water (or onto a cliff) to reach it
            }
            if (!c.Forced && v.getHealth() <= c.Expected(v, 1.2f) * cm)
            {
                return;   // one ordinary blow ends it
            }
            float score = c.Value(v, c.Expected(v, Total(level)), cm);
            if (air)
            {
                score += 20f;   // it reaches what flies
            }
            if (c.State(v).Ranged && d > 3f)
            {
                score += 12f;   // it drags the archer, the mage, in
            }
            if (Tiers.Of(v) != Tier.None || Weights.Species(v) >= Weight.Fiend)
            {
                score += 10f * Mathf.Clamp(Tiers.Power(v) / Mathf.Max(1f, Tiers.Power(c.Self)), 0.2f, 1.5f);
            }
            if (c.Linked && v == c.LinkTarget)
            {
                score += 8f;
            }
            if (score > bestScore)
            {
                best = v;
                bestScore = score;
            }
        }

        public override bool Begin(Actor a, Plan p)
        {
            ChoreoMove m = ChoreoMove.Make(a, p, this);
            if (m == null)
            {
                return false;
            }
            int level = m.Level;
            Chain ch = new Chain();
            m.Ended2 = (mm, aborted) => ch.Clear();
            Form shows = Form.Ki | Form.Grab | Form.AntiAir;
            // the fist cocked back, a glint on it
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.1f,
                To = new Shape(-14f, 0.92f, 1.07f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                NeedsVictim = true,
                Forms = shows,
                Enter = mm =>
                {
                    Choreo.Sound("event:/SFX/POWERS/Powerup", mm.A.current_tile, 0.2f);
                    if (mm.Vis)
                    {
                        Fx.Glint(mm.Hand(Limb.Fist) + new Vector2(0f, 1.5f * mm.Px), mm.Px, 0.14f, mm.Hue.Core, 0.9f);
                        ChoreoFx.Motes(mm.Hand(Limb.Fist), mm.Px, 4, 10f * mm.Kk, 0.1f, mm.Hue.Core, mm.Hue.Edge, mm.TopLayer);
                    }
                }
            });
            // the lash: the fist flung out, the chain racing after the foe
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.05f,
                To = new Shape(18f, 1.22f, 0.88f, 0f, 0.12f),
                Ease = Ease.OutExpo,
                NeedsVictim = true,
                Forms = shows,
                Enter = mm => ch.Lash(mm),
                Every = (mm, k) => ch.Fly(mm),
                Until = mm => ch.Arrived,
                MaxDur = 0.7f,
                Exit = mm =>
                {
                    if (!ch.Bound)
                    {
                        ch.Retract(mm);
                        mm.Lose();
                    }
                }
            });
            // bound: the links coil round it and lock; it braces against the pull
            m.Add(new Beat
            {
                Tempo = Tempo.Wind,
                Dur = 0.17f,
                To = new Shape(-16f, 0.94f, 1.06f, 0f, -0.06f),
                Ease = Ease.OutQuad,
                Forms = shows,
                If = mm => ch.Bound,
                Enter = mm => ch.Lock(mm),
                Every = (mm, k) => ch.Strain(mm, k)
            });
            // the yank: a body it can haul flies in along the chain...
            m.Add(new Beat
            {
                Tempo = Tempo.Act,
                Dur = 0.2f,
                To = new Shape(-24f, 0.9f, 1.12f, 0f, -0.1f),
                Ease = Ease.OutQuad,
                Track = false,
                Forms = shows,
                If = mm => ch.Bound && ch.Haul,
                Enter = mm => ch.BeginHaul(mm),
                Every = (mm, k) => ch.Hauling(mm, k),
                Exit = mm => ch.EndHaul(mm)
            });
            // ...or the chain hauls the fighter to it (up into the air if it must)
            m.Add(new Beat
            {
                Tempo = Tempo.Travel,
                Dur = 0.05f,
                Route = Route.Line,
                PathEase = Ease.InQuad,
                Dest = mm => mm.StandSpot(mm.Stand),
                PerTile = 0.03f,
                Chase = true,
                To = new Shape(16f, 0.86f, 1.16f, 0f),
                Ease = Ease.OutQuad,
                Ghosts = 0.022f,
                Lines = 0.03f,
                Forms = Form.Ki | Form.Dash | Form.AntiAir,
                If = mm => ch.Bound && !ch.Haul,
                Enter = mm => ch.BeginZip(mm),
                Every = (mm, k) => ch.Zipping(mm, k),
                Exit = mm => ch.Retract(mm)
            });
            // the fist that was waiting for it
            m.Strike(new Hit(Blow.Cross, PunchMult(level))
            {
                Hurt = level <= 1 ? Hurt.Reel : Hurt.Fold,
                Push = level <= 1 ? 1.8f : 0.25f,
                Lethal = level >= 3 ? false : (bool?)null,   // (III: the whip and the slam still come)
                Power = 1.25f,
                Finisher = level <= 1,
                Air = true,
                WindScale = 0.6f
            });
            if (level == 2)
            {
                m.Strike(new Hit(Blow.Knee, 1f) { Hurt = Hurt.Launch, Lift = 1.6f, Push = 1.3f, Finisher = true, Air = true });
            }
            if (level >= 3)
            {
                // the chain whips the body up over its head and down into the ground
                m.Do(mm => ch.Regrab(mm));
                m.Lift(Grip.Overhead, 0.16f, new Shape(-14f, 0.92f, 1.12f, float.NaN));
                m.Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0f,
                    If = mm => mm.Held != null,
                    Enter = mm => ch.WhipFx(mm)
                });
                m.Slam(1.8f, 1.1f, 1.5f, false, 0.13f, 0.5f);
            }
            m.Recover(0.26f);
            return m.Start();
        }

        /// <summary>
        /// The chain itself: its links from the fist to its head (snaking while it flies, taut once it
        /// binds), the coils round the foe, and what it does: bind, haul the body in, or haul the
        /// fighter to the body. Particles are the move's (they die with it).
        /// </summary>
        private sealed class Chain
        {
            private const int MaxLinks = 36;
            private const float Speed = 42f;

            public bool Arrived, Bound, Haul;
            private Vector2 _head;
            private float _flyT, _tension, _pulse;
            private readonly Particle[] _links = new Particle[MaxLinks];
            private readonly Particle[] _coils = new Particle[3];
            private Particle _tip;
            private ChoreoMove _m;
            private Victim _held;
            private Vector2 _g0, _goal;
            private float _h0, _ghostT;
            private bool _zipAir;

            /// <summary>The fist is flung out: the head leaves it.</summary>
            public void Lash(ChoreoMove m)
            {
                _m = m;
                _head = m.Hand(Limb.Fist);
                _flyT = 0f;
                _tension = 0f;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile, 0.15f);
                if (m.Vis)
                {
                    Kit.Flash(_head, m.Px, Mathf.RoundToInt(3f * m.Kk), 0.06f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
                    _tip = m.Glue(Fx.Spawn(Art.Disk(Mathf.Max(1, Mathf.RoundToInt(1.6f * m.Kk)), 1f, 0.3f), _head, m.Px, 1f, FxLayer.Top, 14));
                }
            }

            /// <summary>The head races after the foe (it follows it as it moves); there, the foe may answer it.</summary>
            public void Fly(ChoreoMove m)
            {
                _flyT += Clock.Dt;
                if (!m.VOk || _flyT > 0.6f)
                {
                    Arrived = true;   // (not bound: the lash's end lets the chain fall away)
                    return;
                }
                Vector2 goal = FeetOf(m) + new Vector2(0f, m.VHeight * 0.5f);
                _head = Vector2.MoveTowards(_head, goal, Speed * Mathf.Lerp(1f, m.Kk, 0.5f) * Clock.Dt);
                Draw(m, 0.15f + 0.85f * Mathf.Clamp01(_flyT * 4f));
                if ((_head - goal).sqrMagnitude > 0.05f)
                {
                    return;
                }
                Arrived = true;
                if (m.Touch(m.V))
                {
                    Bound = true;
                }
            }

            /// <summary>The links coil round the foe and lock: a small blow, it is pinned where it is.</summary>
            public void Lock(ChoreoMove m)
            {
                Actor v = m.V;
                if (v == null || !m.VOk)
                {
                    m.Lose();
                    return;
                }
                HitResult r = Combat.Hit(m.A, v, Bind * m.Cm, false, false, 0f, m);
                m.LastHit = r;
                Victims.Flash(v, m.Hue.Core, 0.08f);
                Combat.Pin(v, 1.5f);
                bool grounded = Combat.Grounded(v);
                Haul = grounded && Choreo.Holdable(v, m.A);
                if (Haul)
                {
                    _held = Victims.Grab(v, m);
                    Haul = _held != null;
                }
                if (!Haul && !Moves.Busy(v))
                {
                    Combat.Stagger(v, 0.6f);   // bound fast: it can't get away while the chain hauls the fighter in
                }
                Choreo.Sound("event:/SFX/HIT/HitMetal", v.current_tile);
                Choreo.Sound("event:/SFX/OTHER/Punch", v.current_tile, 0.1f);
                if (m.Vis)
                {
                    Vector2 at = m.VPoint(0.5f);
                    Kit.Flash(at, m.Px, Mathf.RoundToInt(4f * m.Kk), 0.07f, Fx.WithA(m.Hue.Core, 0.95f), m.TopLayer);
                    Kit.Sparks(at, m.Px, 5, 40f, 90f, m.Hue.Edge, m.TopLayer);
                    int rx = Mathf.Clamp(Mathf.RoundToInt(m.VHpx * m.VPx / m.Px * 0.42f), 3, 40);
                    for (int i = 0; i < _coils.Length; i++)
                    {
                        _coils[i] = m.Glue(Fx.Spawn(Art.Ring(rx, 1, 0.42f), at, m.Px, 1f, FxLayer.Over, 9));
                    }
                }
                if (_tip != null)
                {
                    m.Unglue(_tip);
                    _tip = null;
                }
                if (Dev.On) Dev.Trace("ki_chain binds " + v.asset.id + "#" + v.Sid() + (Haul ? ": hauls it in" : ": hauls itself to it"));
            }

            /// <summary>Taut and trembling; the coils tighten; a pulse of light runs down the links.</summary>
            public void Strain(ChoreoMove m, float k)
            {
                if (!Ok(m))
                {
                    m.Lose();
                    return;
                }
                _tension = 1f;
                _head = BodyOf(m);
                m.Pose.Nudge = Choreo.Tremble(m.BeatT, 0.6f);
                Coils(m, Beat.Apply(Ease.OutBack, k));
                _pulse = k;
                Draw(m, 1f);
            }

            /// <summary>The yank: the body is ripped off its feet.</summary>
            public void BeginHaul(ChoreoMove m)
            {
                if (_held == null || _held.Done || !Ok(m))
                {
                    Haul = false;
                    m.Lose();
                    return;
                }
                _g0 = _held.P.Pose.Ground;
                _h0 = _held.P.Pose.Height;
                Vector2 front = m.Stance + m.Dir * m.Stand * 1.05f;
                _goal = Combat.SafeSpot(front) || Choreo.Wading(front) ? front : m.Stance;
                float d = Vector2.Distance(_g0, _goal);
                m.BeatDur = Mathf.Clamp(0.08f + d * 0.026f, 0.12f, 0.36f) * m.ActK;
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Kit.SpeedLines(Vector2.Lerp(_g0, _goal, 0.5f) + new Vector2(0f, 0.5f * m.Kk), _goal - _g0, m.Px, 4, 4f, 0.14f, Fx.WithA(m.Hue.Edge, 0.85f));
                    Kit.DustRing(_g0, m.Px, Combat.TileAt(_g0), 6, 30f, 0.4f, m.Kk);
                }
            }

            /// <summary>It flies in along the chain, faster and faster, the links reeled in.</summary>
            public void Hauling(ChoreoMove m, float k)
            {
                if (_held == null || _held.Done || !Combat.Living(m.V))
                {
                    _held = null;
                    m.Lose();
                    return;
                }
                float e = Beat.Apply(Ease.InQuad, k);
                Pose p = _held.P.Pose;
                p.Ground = Vector2.Lerp(_g0, _goal, e);
                float vk = Choreo.KOf(m.V);
                p.Height = Mathf.Lerp(_h0, 0.05f * vk, e) + 0.55f * vk * 4f * e * (1f - e);
                p.Rot = -18f - 30f * Mathf.Sin(Mathf.PI * e);
                p.FaceRight = !m.Pose.FaceRight;
                p.Nudge = Vector2.zero;
                p.HideShadow = false;
                _held.P.Pose = p;
                _head = p.Ground + new Vector2(0f, p.Height + _held.HeightW * 0.5f);
                Coils(m, 1f);
                Draw(m, 1f);
                _ghostT -= Clock.Dt;
                if (m.Vis && _ghostT <= 0f)
                {
                    _ghostT = 0.03f;
                    Trails.Ghost(m.V, 0.12f, Fx.WithA(m.Hue.Ghost, 0.6f), Fx.WithA(m.Hue.Edge, 0f));
                }
            }

            /// <summary>It arrives on the fist: let go of it (the blow takes it from here), the chain shatters into motes.</summary>
            public void EndHaul(ChoreoMove m)
            {
                if (_held != null && !_held.Done)
                {
                    Victim h = _held;
                    _held = null;
                    h.LetGo(Impact.Of(Hurt.Flinch, m.Dir, 0f, 0.7f), m.A, m);
                }
                _held = null;
                Retract(m);
            }

            /// <summary>The chain hauls the fighter instead: it rises to the foe's height on the way.</summary>
            public void BeginZip(ChoreoMove m)
            {
                if (!m.VOk)
                {
                    m.Lose();
                    return;
                }
                float h = Mathf.Clamp(Mathf.Max(Combat.HeightOf(m.V), FeetOf(m).y - m.VG.y), 0f, 6f * m.Kk);
                _zipAir = h > 0.35f;
                if (_zipAir)
                {
                    m.Current.To = new Shape(16f, 0.86f, 1.16f, h / Mathf.Max(0.3f, m.Kk));
                }
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", m.A.current_tile);
                if (m.Vis)
                {
                    Fx.LandingPuff(m.Stance, m.Px, m.A.current_tile);
                }
            }

            public void Zipping(ChoreoMove m, float k)
            {
                if (!m.VOk)
                {
                    return;
                }
                _head = BodyOf(m);
                Coils(m, 1f);
                Draw(m, 1f);
            }

            /// <summary>III: the punched body is taken by the chain again (if it can be held) for the whip.</summary>
            public void Regrab(ChoreoMove m)
            {
                if (m.Held != null || !m.VOk || !Combat.Grounded(m.V) || !Choreo.Holdable(m.V, m.A))
                {
                    return;
                }
                Victim b = Victims.Grab(m.V, m);
                if (b != null)
                {
                    m.Held = b;
                    Combat.Pin(m.V, 2f);
                    Choreo.Sound("event:/SFX/HIT/HitMetal", m.A.current_tile);
                }
            }

            /// <summary>The whip: a streak of the chain's light over its head.</summary>
            public void WhipFx(ChoreoMove m)
            {
                if (!m.Vis)
                {
                    return;
                }
                Kit.Swish(m, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(10f * m.Kk), 4, 40), 0.9f, -20f, 160f, 2), 0.16f, new Vector2(0f, 2f), m.Fwd, m.Hue.Core, m.Hue.Edge, 6);
                ChoreoFx.Motes(m.BodyPos() + new Vector2(0f, m.Hpx * 0.6f * m.Px), m.Px, 6, 12f * m.Kk, 0.12f, m.Hue.Core, m.Hue.Edge, m.TopLayer);
            }

            /// <summary>The chain comes apart: its links burst into motes along its length.</summary>
            public void Retract(ChoreoMove m)
            {
                if (m.Vis)
                {
                    for (int i = 0; i < MaxLinks; i += 3)
                    {
                        Particle l = _links[i];
                        if (l != null && l.Alive && l.R.color.a > 0.05f)
                        {
                            Particle s = Fx.Spawn(Art.Pixel, l.Pos, m.Px, Random.Range(0.15f, 0.3f), FxLayer.Top, 9);
                            s?.Moving(Random.insideUnitCircle * 30f * m.Px, Random.Range(10f, 30f) * m.Px, 3f, 60f * m.Px).Color(Color.white, Fx.WithA(m.Hue.Edge, 0.8f), Fx.WithA(m.Hue.Edge, 0f));
                        }
                    }
                }
                Clear();
            }

            /// <summary>Everything of the chain goes (the move's end or the retract).</summary>
            public void Clear()
            {
                for (int i = 0; i < MaxLinks; i++)
                {
                    Kill(ref _links[i]);
                }
                for (int i = 0; i < _coils.Length; i++)
                {
                    Kill(ref _coils[i]);
                }
                Kill(ref _tip);
                if (_held != null && !_held.Done && _m != null && _held.Holder == _m)
                {
                    // (the move ended with the body still on the chain: it drops where it is)
                    _held.Drop(true);
                }
                _held = null;
            }

            private static void Kill(ref Particle p)
            {
                if (p != null && p.Alive)
                {
                    p.Kill();
                }
                p = null;
            }

            /// <summary>
            /// Is the foe still there? (While the chain holds it, it is in the chain's hands, which the
            /// engine's VOk doesn't count as its own: the body itself is asked.)
            /// </summary>
            private bool Ok(ChoreoMove m)
            {
                if (_held != null)
                {
                    return !_held.Done && m.V != null && m.V.Sid() == m.VId && Combat.Living(m.V) && !m.V.isInMagnet();
                }
                return m.VOk;
            }

            private Vector2 BodyOf(ChoreoMove m)
            {
                return Ok(m) ? FeetOf(m) + new Vector2(0f, m.VHeight * 0.5f) : m.BodyPos() + m.Dir * m.Stand;
            }

            /// <summary>
            /// Where the foe's feet are drawn. A free flyer (a dragon on the wing) is drawn by the game well
            /// above its height (its flying sprites hang high over their pivot): the middle of the sprite as
            /// drawn is found from the sprite itself, and the feet are half its height below that.
            /// </summary>
            private static Vector2 FeetOf(ChoreoMove m)
            {
                Actor v = m.V;
                if (v != null && v.isFlying() && !Moves.Busy(v))
                {
                    Vector3 t = v.cur_transform_position;
                    Vector2 c = new Vector2(t.x, t.y);
                    Sprite sp = Fx.UnitSprite(v);
                    if (sp != null)
                    {
                        float up = (sp.rect.height * 0.5f - sp.pivot.y) / Mathf.Max(1f, sp.pixelsPerUnit) * Mathf.Abs(v.current_scale.y);
                        c.y += Mathf.Clamp(up, -2f, 8f);
                    }
                    else
                    {
                        c.y += m.VHeight * 0.5f;
                    }
                    return c - new Vector2(0f, m.VHeight * 0.5f);
                }
                return v != null ? Choreo.Feet(v) : m.BodyPos();
            }

            /// <summary>The three coils round the foe's body, tightening ('k' 0 loose .. 1 locked).</summary>
            private void Coils(ChoreoMove m, float k)
            {
                if (!Ok(m))
                {
                    return;
                }
                float h = m.VHeight;
                Vector2 feet = FeetOf(m);
                if (_held != null && !_held.Done)
                {
                    Pose hp = _held.P.Pose;
                    feet = hp.Ground + new Vector2(0f, hp.Height);
                }
                for (int i = 0; i < _coils.Length; i++)
                {
                    Particle c = _coils[i];
                    if (c == null || !c.Alive)
                    {
                        continue;
                    }
                    c.Pos = feet + new Vector2(0f, h * (0.3f + 0.2f * i) + Mathf.Sin(m.T * 30f + i) * 0.3f * m.Px);
                    c.Size0 = c.Size1 = Mathf.Lerp(1.8f, 1f, k);
                    c.Grad = null;
                    c.R.color = Fx.WithA(i == 1 ? m.Hue.Core : m.Hue.Edge, 0.95f);
                    c.Cine = m.Cinematic;
                }
            }

            /// <summary>The links from the fist to the head: snaking while slack, a straight trembling line when taut.</summary>
            private void Draw(ChoreoMove m, float alpha)
            {
                if (!m.Vis)
                {
                    return;
                }
                Vector2 from = m.Hand(Limb.Fist);
                Vector2 d = _head - from;
                float len = d.magnitude;
                float px = m.Px;
                float step = 3.6f * px * Mathf.Lerp(1f, m.Kk, 0.5f);
                int n = Mathf.Clamp(Mathf.CeilToInt(len / step), 1, MaxLinks);
                Vector2 dir = len > 1e-4f ? d / len : m.Dir;
                Vector2 side = Choreo.Perp(dir);
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float slack = 1f - _tension;
                for (int i = 0; i < MaxLinks; i++)
                {
                    Particle l = _links[i];
                    if (i >= n)
                    {
                        if (l != null && l.Alive)
                        {
                            l.Grad = null;
                            l.R.color = new Color(1f, 1f, 1f, 0f);
                        }
                        continue;
                    }
                    if (l == null || !l.Alive)
                    {
                        l = m.Glue(Fx.Spawn(ChoreoArt.ChainLink(), from, px, 1f, FxLayer.Top, 11));
                        if (l == null)
                        {
                            break;
                        }
                        _links[i] = l;
                    }
                    float u = (i + 0.5f) / n;
                    // a wave down the slack chain (none at the fist or at the head); a tremble when taut
                    float wave = Mathf.Sin(u * Mathf.PI) * (slack * 2.4f * Mathf.Sin(m.T * 34f - u * 9f) + _tension * 0.35f * Mathf.Sin(m.T * 90f + i));
                    l.Pos = from + d * u + side * wave * px;
                    l.Rot = ang;
                    l.SY = (i & 1) == 0 ? 1f : 0.55f;
                    // a pulse of light running down the taut chain
                    float glow = _tension > 0f ? Mathf.Clamp01(1f - Mathf.Abs(u - _pulse) * 5f) : 0f;
                    l.Grad = null;
                    l.R.color = Fx.WithA(Color.Lerp((i & 1) == 0 ? m.Hue.Edge : m.Hue.Main, Color.white, glow), alpha);
                    l.Cine = m.Cinematic;
                }
                if (_tip != null && _tip.Alive)
                {
                    _tip.Pos = _head;
                    _tip.Grad = null;
                    _tip.R.color = Color.Lerp(m.Hue.Core, Color.white, 0.5f);
                }
            }
        }
    }
}
