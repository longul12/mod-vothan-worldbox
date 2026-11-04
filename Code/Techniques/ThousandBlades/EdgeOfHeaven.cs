using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// EDGE OF HEAVEN (Thousand Blades, God; reactive). The foe's greatest attack arrives (a beam, a
    /// dash, an ultimate) and the God draws once: the whole view flashes, a hairline crosses the frame
    /// and a blade of light stands from the sky to the earth where the attack meets it. The attack is
    /// cut in half: a beam splits round the God in two halves that fly apart and gutter out, a dash
    /// stops dead against the edge, a thrown spear falls in two. Then the cut reaches the foe: a near
    /// one takes it at once; a far one (the beam's shooter) sees a thin line race back along its own
    /// beam into it. The foe's move is broken; it crumples or is blasted away.
    /// It also cuts a great wind-up from afar: a foe gathering a heavy or ultimate blow within 12
    /// tiles sees the edge of heaven fall on it from the sky, splitting what it gathers.
    /// Only the foe's greatest attacks are worth it (beams, ultimates, heavy blows, god abilities):
    /// never a flow strike or a light technique. The cut itself can be answered (counter the counter).
    /// I-III (Gods use it at III): damage (x the user's damage) 4 / 5 / 6.5.
    /// </summary>
    internal sealed class EdgeOfHeavenTech : Technique
    {
        public override string Id => "edge_of_heaven";
        public override Tier Entry => Tier.God;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.ThousandBlades;
        public override Form Forms => Form.Blade | Form.Counter | Form.Ultimate;
        public override Form Answers => Form.Ultimate | Form.Charge | Form.Beam | Form.Dash;
        public override Scale Scale => Scale.Heavy;
        public override float[] ManaCost => new[] { 120f, 150f, 180f };
        public override float[] Cooldown => new[] { 36f, 40f, 44f };

        public static float Mult(int level)
        {
            return level <= 1 ? 4f : level == 2 ? 5f : 6.5f;
        }

        private const float Reach = 12f;

        /// <summary>Is this the foe's greatest kind of attack (worth the edge of heaven)?</summary>
        public static bool Greatest(Move m)
        {
            if (m == null || m.Interruptible || m is Puppet)
            {
                return false;
            }
            Technique t = m.Tech;
            Form f = Reads.FormsOf(m) | (t != null ? t.FormsAt(m.Level) : Form.None);
            if ((f & (Form.Ultimate | Form.Beam)) != 0)
            {
                return true;
            }
            return t != null && (t.GodAbility || t.ScaleAt(m.Level) >= Scale.Heavy);
        }

        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Enemies.Count == 0)
            {
                return null;
            }
            float k = Choreo.KOf(c.Self);
            Actor best = null;
            float bestScore = 0f;
            for (int i = 0; i < c.Enemies.Count && i < 10; i++)
            {
                if (c.EnemyDist[i] > Reach * k + (c.Forced ? 4f : 0f))
                {
                    break;
                }
                Actor v = c.Enemies[i];
                if (!Choreo.Fair(c.Self, v, out float cm, c.Forced))
                {
                    continue;
                }
                float score;
                if (c.Forced)
                {
                    score = v == c.Focus ? 2f : 1f;
                }
                else
                {
                    // a great wind-up within reach: cut it before it is loosed
                    if (!Moves.TryGet(v, out Move mv) || mv.A != v || !mv.Charging || !Greatest(mv))
                    {
                        continue;
                    }
                    score = c.D * 4f + 50f + 15f * Tiers.Power(v) + c.Value(v, c.Expected(v, Mult(level)), cm);
                }
                if (score > bestScore)
                {
                    best = v;
                    bestScore = score;
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
                Score = bestScore,
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            if (a == null || p == null || p.Target == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Edge m = new Edge(a, this, p, Edge.Mode.Fall, null);
            if (m.V == null)
            {
                return false;
            }
            m.Build();
            return m.Start();
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (area || !Reactions.Accept(this, defender, attacker, incoming, level, area))
            {
                return Contact.Hit;
            }
            if (!Greatest(incoming))
            {
                return Contact.Hit;   // a lesser blow is not worth the edge of heaven
            }
            Beam beam = Beams.Of(attacker);
            if (beam != null && (!beam.Firing || beam.M != incoming))
            {
                beam = null;
            }
            Shot shot = Shots.Reaching;
            if (shot != null && (shot.A != attacker || shot.Done))
            {
                shot = null;
            }
            float d = Vector2.Distance(defender.current_position, attacker.current_position);
            bool far = beam != null || shot != null || d > 2.6f * Choreo.KOf(defender);
            Reactions.Free(defender);
            if (!Moves.CanPerform(defender))
            {
                return Contact.Hit;
            }
            Vector2 rel = attacker.current_position - defender.current_position;
            Plan p = new Plan
            {
                Tech = this,
                Level = Mathf.Clamp(level, 1, 3),
                Target = attacker,
                Point = attacker.current_position,
                Dir = rel.sqrMagnitude > 1e-4f ? rel.normalized : Vector2.right,
                Count = 1,
                Data = true   // (an answer reaches like a forced plan: the attacker came to it)
            };
            Edge m = new Edge(defender, this, p, far ? Edge.Mode.Far : Edge.Mode.Near, beam);
            if (m.V == null)
            {
                return Contact.Hit;
            }
            m.Build();
            if (!m.Start())
            {
                return Contact.Hit;
            }
            // the attack is cut in half
            if (beam != null)
            {
                m.Split(beam);
            }
            if (shot != null)
            {
                shot.Cut();
            }
            return Contact.Parried;
        }

        /// <summary>The move: the draw, the attack falling apart round it, the cut reaching the foe, the sheathe.</summary>
        private sealed class Edge : ChoreoMove
        {
            public enum Mode
            {
                /// <summary>The foe is right there (a dash, a blow): it stops dead and takes the cut.</summary>
                Near,
                /// <summary>The foe is away (a beam, a shot): the cut races back to it along the line.</summary>
                Far,
                /// <summary>The foe is winding up: the edge falls on it from the sky.</summary>
                Fall
            }

            private readonly Mode _mode;
            private Vector2 _cut, _to;
            private float _run, _dist;
            private bool _arrived;
            private Hue _blade, _beamHue;
            private Vector2 _beamFrom, _beamDir;
            private bool _split;

            public Edge(Actor a, Technique t, Plan p, Mode mode, Beam beam) : base(a, t, p)
            {
                _mode = mode;
                if (beam != null)
                {
                    _beamHue = beam.Hue;
                    _beamFrom = beam.From;
                    _beamDir = beam.Dir;
                }
            }

            public void Build()
            {
                _blade = Armed ? Hue.As(Element.Steel) : Hue;
                Form show = Form.Blade | Form.Counter | Form.Ultimate;
                // the draw: one motion, nothing touches it
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = 0.05f,
                    To = new Shape(8f, 1.26f, 0.84f, 0f, 0.1f),
                    Ease = Ease.OutExpo,
                    Track = false,
                    Intangible = true,
                    Forms = show,
                    Enter = mm => ((Edge)mm).Draw()
                });
                // the stillness: the attack falls apart; the cut travels
                Add(new Beat
                {
                    Tempo = Tempo.Free,
                    Dur = _mode == Mode.Near ? 0.24f : 0.08f,
                    To = new Shape(6f, 1.2f, 0.86f, 0f, 0.1f),
                    Ease = Ease.OutQuad,
                    Track = false,
                    Forms = show,
                    Every = (mm, k) => ((Edge)mm).Travel(k),
                    Until = mm => ((Edge)mm)._arrived || ((Edge)mm)._mode == Mode.Near,
                    MaxDur = 1f
                });
                // the foe takes the cut
                Do(mm => ((Edge)mm).Land());
                Hold(0.12f, false, Tempo.Impact);
                // the blade slides home
                Add(new Beat
                {
                    Tempo = Tempo.Follow,
                    Dur = 0.2f,
                    To = new Shape(-2f, 1f, 1f, 0f, 0f),
                    Ease = Ease.InOutSine,
                    Track = false,
                    Every = (mm, k) => ((Edge)mm).Sheathe(k)
                });
                At(0.85f, mm => ((Edge)mm).Click());
                Recover(0.3f);
            }

            /// <summary>The draw: the view flashes, a hairline crosses it, a blade of light stands from the sky.</summary>
            private void Draw()
            {
                if (V != null)
                {
                    Face(VG - Stance);
                }
                Vector2 body = BodyPos();
                _cut = body + Dir * Hpx * 0.45f * Px;
                _to = V != null ? VPoint(0.5f) : _cut + Dir * 3f;
                _dist = Vector2.Distance(_cut, _to);
                _run = 0f;
                _arrived = _mode == Mode.Near;
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", A.current_tile, 0.2f);
                Kit.Stop(_cut, 0.07f);
                if (!Vis)
                {
                    return;
                }
                if (Settings.Cinematics > 0)
                {
                    CineKit.ViewFlash(Fx.WithA(Color.white, 0.85f), 0.04f);
                }
                // the hairline of the draw across the frame
                Vector2 side = Dir * 3.2f * Kk;
                Kit.Line(_cut - side, _cut + side, Px, 0.38f, 1, FxLayer.Top, 30, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
                // the edge of heaven: a blade of light from the sky to the earth where the attack is cut
                Vector2 g = Pose.Ground + Dir * Hpx * 0.45f * Px;
                Heaven(g, 0.45f);
                Kit.Swish(this, Art.Arc(Mathf.Clamp(Mathf.RoundToInt(12f * Mathf.Lerp(1f, Kk, 0.6f)), 6, 60), 0.3f, 160f, -20f, 2), 0.12f, new Vector2(3f * Fwd, 1f), Fwd, Hue.Core, Hue.Edge, 6);
                Fx.FocusLines(_cut, Px, Fx.Count(8), 14f * Kk, 26f * Kk, 0.12f);
                Kit.Flash(_cut, Px, Mathf.RoundToInt(5f * Kk), 0.07f, Color.white, TopLayer);
            }

            /// <summary>A vertical blade of light standing from the sky to the ground at 'g'.</summary>
            private void Heaven(Vector2 g, float life)
            {
                float h = 9f * Kk;
                Kit.Line(g, g + new Vector2(0f, h), Px, life, 4, FxLayer.Top, 28, Fx.WithA(Hue.Edge, 0.55f), Fx.WithA(Hue.Rim, 0.3f), Fx.WithA(Hue.Rim, 0f));
                Kit.Line(g, g + new Vector2(0f, h), Px, life * 0.8f, 1, FxLayer.Top, 29, Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Core, 0f));
                Kit.Sparks(g, Px, 4, 40f, 100f, Hue.Edge);
            }

            /// <summary>The foe's beam, cut at the blade: its two halves fly apart round the God and gutter out.</summary>
            public void Split(Beam b)
            {
                if (b == null || _split)
                {
                    return;
                }
                _split = true;
                Vector2 at = Choreo.BodyAt(A, 0.5f);
                float along = Mathf.Max(0.5f, Vector2.Dot(at - b.From, b.Dir));
                b.StopAt(b.From + b.Dir * along, 0.6f);
                b.Collapse();
                if (Vis || Kit.Visible(b.From))
                {
                    Vector2 cut = b.From + b.Dir * along;
                    float w = Mathf.Max(1f, b.Spec.Width * 0.5f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 d = Choreo.Rotate(b.Dir, 14f * s);
                        Vector2 off = Choreo.Perp(b.Dir) * s * w * Px;
                        Kit.Line(cut + off, cut + off + d * 4.5f * Kk, Px, 0.3f, Mathf.RoundToInt(w), FxLayer.Top, 20,
                            Fx.WithA(b.Hue.Core, 0.95f), Fx.WithA(b.Hue.Edge, 0.6f), Fx.WithA(b.Hue.Edge, 0f));
                        Kit.Sparks(cut + off + d * 2f * Kk, Px, 3, 40f, 90f, b.Hue.Edge);
                    }
                    Kit.Flash(cut, Px, Mathf.RoundToInt(6f * Kk), 0.08f, Color.white, FxLayer.Top);
                }
            }

            /// <summary>The cut on its way: along the line back to a far foe, or down from the sky onto a charging one.</summary>
            private void Travel(float k)
            {
                if (_mode == Mode.Near)
                {
                    Pose.Nudge = Choreo.Tremble(BeatT, 0.3f);
                    return;
                }
                if (V != null && VOk)
                {
                    _to = _mode == Mode.Fall ? VG : VPoint(0.5f);
                    _dist = _mode == Mode.Fall ? 9f * Kk : Vector2.Distance(_cut, _to);
                }
                _run = Mathf.Min(_dist, _run + 90f * Mathf.Max(Clock.Dt, 0.004f));
                if (_run >= _dist - 0.01f)
                {
                    _arrived = true;
                }
                if (!Vis && !Kit.Visible(_to))
                {
                    return;
                }
                if (_mode == Mode.Far)
                {
                    Vector2 d = (_to - _cut).sqrMagnitude > 1e-4f ? (_to - _cut).normalized : Dir;
                    Vector2 head = _cut + d * _run;
                    Kit.Line(_cut, head, Px, 0.06f, 1, FxLayer.Top, 26, Color.white, Fx.WithA(Hue.Core, 0.8f));
                    Kit.Sparks(head, Px, 1, 30f, 60f, Hue.Edge);
                }
                else
                {
                    // the edge descends from the sky onto the foe
                    Vector2 top = _to + new Vector2(0f, 9f * Kk);
                    Vector2 head = top + Vector2.down * _run;
                    Kit.Line(top, head, Px, 0.06f, 2, FxLayer.Top, 26, Color.white, Fx.WithA(Hue.Core, 0.8f));
                }
            }

            /// <summary>The cut reaches the foe: its move is broken, it crumples or is blasted away.</summary>
            private void Land()
            {
                if (V == null || !VOk)
                {
                    Lose();
                    return;
                }
                if (!Touch(V))
                {
                    return;   // it answered the answer
                }
                int level = Level;
                bool near = _mode == Mode.Near;
                Vector2 dir = VG - Stance;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Dir;
                bool charging = Moves.Charging(V);
                Hit h = new Hit(Blow.DrawCut, Mult(level))
                {
                    Hurt = near ? (level >= 2 ? Sakuga.Hurt.Crumple : Sakuga.Hurt.Knockdown) : Sakuga.Hurt.Launch,
                    Push = near ? 0.3f : 2.4f,
                    Lift = near ? -1f : 1.4f,
                    Power = 1.8f,
                    Finisher = true
                };
                Vector2 at = VPoint(0.55f);
                Deliver(V, h, ShapeOf(Blow.DrawCut), at, dir);
                // whatever it was doing comes apart
                if (Combat.Living(V))
                {
                    if (charging)
                    {
                        Interrupts.Break(V, A, dir, 3f);
                    }
                    else if (Moves.TryGet(V, out Move vm) && vm.A == V && !(vm is Puppet) && !vm.Interruptible)
                    {
                        Choreo.Break(V, A, dir, near ? 1.5f : 3f);
                    }
                }
                Vector2 g = VG;
                if (Vis || Kit.Visible(g))
                {
                    Heaven(g, 0.35f);
                    Kit.ImpactFrame(at, Px, (0.7f + 0.15f * level) * Kk, Hue.Pal, level >= 3, FxLayer.Behind);
                }
                // the ground under it is split by the edge
                Vector2 perp = Choreo.Perp(dir);
                Ruin.Furrow(A, g - perp * 1.2f * Kk, g + perp * 1.2f * Kk, 0.7f, Scale, Pose.Ground, false);
                SetLink(near ? Link.Down : Link.Launched, V);
            }

            private void Sheathe(float k)
            {
                Vector2 hip = BodyPos() + new Vector2(-Fwd * 0.5f, -1f) * Px;
                TbHiArt.Blade(this, hip, new Vector2(-Fwd, -0.35f), Hpx * 0.6f * (1f - Beat.Apply(Ease.InQuad, k)), _blade);
            }

            private void Click()
            {
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile, 0.1f);
                if (Vis)
                {
                    Fx.Glint(BodyPos() + new Vector2(-Fwd * 1f, -1f) * Px, Px, 0.16f, Color.white, 0.9f);
                }
            }
        }

        // ------------------------------------------------------------------ dev --

        private static Actor _da, _db;
        private static string _kind;
        private static float _t = -1f;
        private static bool _went, _cut;
        private static readonly List<long> _staged = new List<long>();
        private static readonly object _scene = new object();

        /// <summary>
        /// Dev staging: "tbedge KIND [zoom]". A God (B, Thousand Blades) meets A's attack with the edge of
        /// heaven (it always reads it): beam (A fires a Dragon Wave), dash (A's Phantom Rush), charge (A
        /// raises a Moon Splitter 8 tiles away: B cuts it from afar), ultimate (A, a God, draws the
        /// Unmaking Cut). Then e.g. "capture 120 1 900 600 0.2 tb_hi_edge".
        /// </summary>
        [SakugaInit]
        private static void InitDev()
        {
            Dev.Commands["tbedge"] = DevStage;
            // "tbhischool": every Master+ unit the dev commands spawned walks Thousand Blades too (AI test runs;
            // "tbschool" is DrawCounter's, for every tiered unit: both were registered as "tbschool")
            Dev.Commands["tbhischool"] = args =>
            {
                ActorTrait tb = Schools.Trait(School.ThousandBlades);
                int n = 0;
                for (int i = 0; i < Dev.Units.Count && tb != null; i++)
                {
                    Actor u = Dev.Units[i];
                    if (u != null && u.isAlive() && Tiers.Of(u) >= Tier.Master && !u.traits.Contains(tb))
                    {
                        u.addTrait(tb, true);
                        n++;
                    }
                }
                Dev.Log("tbhischool: " + n + " units now walk Thousand Blades");
            };
            Hooks.FrameTick += dt =>
            {
                if (_t < 0f)
                {
                    return;
                }
                try
                {
                    DevTick();
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("tbedge", e);
                    DevEnd();
                }
            };
            Hooks.HoldTechniques.Add(a => _t >= 0f && a != null && _staged.Contains(a.Sid()));
            Hooks.WorldCleared += DevEnd;
        }

        private static void DevStage(string[] args)
        {
            DevEnd();
            _kind = Dev.Arg(args, 1, "beam");
            float zoom = Dev.ArgF(args, 2, 14f);
            WorldTile c = Variety.Meadow(new Vector2(MapBox.width * 0.5f, MapBox.height * 0.5f), 10);
            if (c == null)
            {
                Dev.Log("tbedge: no ground");
                return;
            }
            int gap = _kind == "dash" ? 4 : _kind == "charge" ? 8 : 7;
            _da = Dev.Spawn("orc", World.world.GetTile(c.x - gap / 2, c.y) ?? c, true);
            _db = Dev.Spawn("human", World.world.GetTile(c.x + gap - gap / 2, c.y) ?? c, true);
            if (_da == null || _db == null)
            {
                Dev.Log("tbedge: could not spawn");
                return;
            }
            Progression.Promote(_da, _kind == "ultimate" || _kind == "beam" ? Tier.God : Tier.Grandmaster, false);
            Progression.Promote(_db, Tier.God, false);
            ActorTrait tb = Schools.Trait(School.ThousandBlades);
            if (tb != null)
            {
                _db.addTrait(tb, true);
                if (_kind == "charge" || _kind == "ultimate")
                {
                    _da.addTrait(tb, true);
                }
            }
            ActorTrait ki = Schools.Trait(School.KiArts);
            if (ki != null && _kind == "beam")
            {
                _da.addTrait(ki, true);
            }
            _da.data.favorite = false;
            _db.data.favorite = false;
            _staged.Add(_da.Sid());
            _staged.Add(_db.Sid());
            Director.Claim(_scene, 8f, _da, _db);
            Vector2 mid = (_da.current_position + _db.current_position) * 0.5f;
            World.world.move_camera.focusOn(new Vector3(mid.x, mid.y + 1f, 0f));
            World.world.move_camera.setTargetZoom(zoom);
            Dev.Watch(mid + new Vector2(0f, 1f));
            _t = 0f;
            _went = _cut = false;
            Dev.Log("tbedge " + _kind + ": A " + _da.asset.id + "#" + _da.Sid() + " vs B " + _db.asset.id + "#" + _db.Sid());
        }

        private static void DevTick()
        {
            _t += Time.unscaledDeltaTime;
            if (!Combat.Living(_da) || !Combat.Living(_db))
            {
                if (_t > 1.5f)
                {
                    Dev.Log("tbedge done (one fell)");
                    DevEnd();
                }
                return;
            }
            if (!_went && _t >= 1.5f)
            {
                _went = true;
                Fighter fa = Fighters.Get(_da), fb = Fighters.Get(_db);
                fa.ResetCooldowns();
                fb.ResetCooldowns();
                _da.setMana(_da.getMaxMana());
                _db.setMana(_db.getMaxMana());
                Reactions.DevForce[_db.Sid()] = "edge_of_heaven";
                string tech = _kind == "dash" ? "phantom_rush" : _kind == "charge" ? "moon_splitter" : _kind == "ultimate" ? "unmaking_cut" : "dragon_wave";
                Technique t = Techniques.Get(tech);
                Moves.ReleaseHeld();
                Moves.Hold(_db);
                bool ok = t != null && Brain.Force(_da, t, 3, _db);
                Dev.Log("tbedge: " + tech + " by A: " + ok);
                return;
            }
            if (_went && _kind == "charge" && !_cut && Moves.Charging(_da))
            {
                _cut = true;
                Moves.ReleaseHeld();
                Technique edge = Techniques.Get("edge_of_heaven");
                bool ok = Brain.Force(_db, edge, 3, _da);
                Dev.Log("tbedge: A is charging; the edge falls: " + ok);
            }
            if (_went && _t >= 7f)
            {
                Dev.Log("tbedge done");
                DevEnd();
            }
        }

        private static void DevEnd()
        {
            if (_da != null)
            {
                Reactions.DevForce.Remove(_da.Sid());
            }
            if (_db != null)
            {
                Reactions.DevForce.Remove(_db.Sid());
            }
            _staged.Clear();
            Director.Release(_scene);
            _t = -1f;
            _da = _db = null;
            Moves.ReleaseHeld();
        }
    }
}
