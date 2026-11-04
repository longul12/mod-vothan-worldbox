using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// STONE SKIN (Earth Breaker, Expert; a reactive stance). As a blow arrives the skin turns to
    /// stone in an instant: the blow clangs off (the attacker's own limb jarred, its move broken),
    /// and for 2 / 3 / 4 s it stands braced, grey as rock, facing whoever hits it: blows chip flakes
    /// off it and do 60 / 72 / 85% less damage. It shows Guard: readers answer it with grabs and kicks.
    /// That is its weakness: a grab finds the seams and cracks the shell open (the body is taken as
    /// it is); a kick or a heavy blow does more and cracks it (each one shortens the stance, the
    /// fourth shatters it); a technique made to beat a guard (a roundhouse, an axe kick) shatters it
    /// outright and lands in full. It sheds the stone when no one is left at it; III's shell bursts
    /// outward when it ends, the shards knocking back everyone close (x0.6). Answers Strike, Flurry
    /// and Projectile (and now and then a plain blow from a real threat, or when it is mobbed);
    /// planned on its own when it is mobbed. Only enemies are hurt.
    /// </summary>
    internal sealed class StoneSkinTech : Technique
    {
        public override string Id => "stone_skin";
        public override Tier Entry => Tier.Expert;
        public override Role Role => Role.Defense;
        public override bool Reactive => true;
        public override School School => School.EarthBreaker;
        public override Form Forms => Form.Guard | Form.Body;
        public override Form Answers => Form.Strike | Form.Flurry | Form.Projectile;
        public override Scale Scale => Scale.Light;
        public override float[] ManaCost => new[] { 12f, 20f, 30f };
        public override float[] Cooldown => new[] { 12f, 13f, 14f };

        /// <summary>How long the stance holds (seconds).</summary>
        public static float Duration(int level)
        {
            return level <= 1 ? 2f : level == 2 ? 3f : 4f;
        }

        /// <summary>The share of each blow's damage the stone takes.</summary>
        public static float Reduction(int level)
        {
            return level <= 1 ? 0.6f : level == 2 ? 0.72f : 0.85f;
        }

        [SakugaInit]
        private static void Init()
        {
            Hooks.BeforeDamage.Add(StoneSkin.Shield);
            // (first in line: a blow reaching the stone finds it braced before anyone asks it to answer)
            Combat.ContactHandlers.Insert(0, StoneSkin.Braced);
            Hooks.Reached += StoneSkin.Reached;
            Hooks.HitDealt += StoneSkin.Struck;
            Hooks.HoldTechniques.Add(StoneSkin.Holds);
            Hooks.WorldCleared += StoneSkin.ClearAll;
            // dev: ebskin [level] [tech] [techLevel] [delay]: the performer's nearest foe turns to stone,
            // then (after 'delay' s) the performer is made to use 'tech' on it ("none": nothing)
            Dev.Commands["ebskin"] = args => Later(0.4f, () => DevSkin(args));
            Dev.Commands["ebcounter"] = args => Later(0.4f, () => DevCounter(args));
            Hooks.FrameTick += dt =>
            {
                for (int i = _dev.Count - 1; i >= 0; i--)
                {
                    if (Time.unscaledTime >= _dev[i].Key)
                    {
                        Action a = _dev[i].Value;
                        _dev.RemoveAt(i);
                        a();
                    }
                }
            };
            Hooks.WorldCleared += () => _dev.Clear();
        }

        private static readonly List<KeyValuePair<float, Action>> _dev = new List<KeyValuePair<float, Action>>();

        private static void Later(float s, Action a)
        {
            _dev.Add(new KeyValuePair<float, Action>(Time.unscaledTime + s, a));
        }

        /// <summary>Dev: the performer's nearest foe turns to stone, then the performer is made to use a technique on it.</summary>
        private static void DevSkin(string[] args)
        {
            Actor p = Dev.Performer;
            if (p == null)
            {
                Dev.Log("ebskin: no performer");
                return;
            }
            Choreo.Foes(p, p.current_position, 12f, _tmp, false, true);
            Actor foe = null;
            float best = float.MaxValue;
            foreach (Actor u in _tmp)
            {
                float d = (u.current_position - p.current_position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    foe = u;
                }
            }
            if (foe == null)
            {
                Dev.Log("ebskin: no foe");
                return;
            }
            int level = (int)Dev.ArgF(args, 1, 2f);
            string tid = Dev.Arg(args, 2, "suplex");
            int tl = (int)Dev.ArgF(args, 3, 2f);
            float delay = Dev.ArgF(args, 4, 0.5f);
            Technique me = Techniques.Get("stone_skin");
            StoneSkin m = me != null ? StoneSkin.Make(foe, p, me, level) : null;
            Dev.Log("ebskin: " + foe.asset.id + "#" + foe.Sid() + " turns to stone: " + (m != null && m.Begin()));
            Technique t = Techniques.Get(tid);
            if (t == null)
            {
                return;
            }
            Later(delay, () => Dev.Log("ebskin: " + tid + " on the stone: " + Brain.Force(p, t, tl, foe) + " (skin up " + (StoneSkin.Of(foe) != null) + ")"));
        }

        /// <summary>
        /// Dev: the performer's nearest foes learn 'tech' (a bare trait) and answer only with it, always
        /// reading (Reactions.DevForce): an answer to the performer's own move, staged.
        /// </summary>
        private static void DevCounter(string[] args)
        {
            Actor p = Dev.Performer;
            Technique t = Techniques.Get(Dev.Arg(args, 1, "shoulder_throw"));
            if (p == null || t == null)
            {
                Dev.Log("ebcounter: no performer or technique");
                return;
            }
            Choreo.Foes(p, p.current_position, 12f, _tmp, false, true);
            foreach (Actor u in _tmp)
            {
                u.addTrait(t.TraitId);
                Reactions.DevForce[u.Sid()] = t.Id;
                Dev.Log("ebcounter: " + u.asset.id + "#" + u.Sid() + " answers with " + t.Id);
            }
        }

        public override Contact React(Actor defender, Actor attacker, Move incoming, int level, bool area)
        {
            if (!Reactions.Accept(this, defender, attacker, incoming, level, area) || StoneSkin.Of(defender) != null)
            {
                return Contact.Hit;
            }
            Reactions.Free(defender);
            StoneSkin m = StoneSkin.Make(defender, attacker, this, level);
            if (m == null || !m.Begin())
            {
                return Contact.Hit;
            }
            // (II, III: the attacker's limb rings on the stone)
            if (level >= 2 && !area && attacker != null && Combat.Living(attacker) && Combat.IsFoe(defender, attacker))
            {
                Combat.Stagger(attacker, level >= 3 ? 0.4f : 0.25f);
            }
            return Contact.Parried;
        }

        public override bool ReactOrdinary(Actor defender, Actor attacker, Form forms, int level)
        {
            if (StoneSkin.Of(defender) != null || !Worth(defender, attacker))
            {
                return false;
            }
            StoneSkin m = StoneSkin.Make(defender, attacker, this, level);
            return m != null && m.Begin();
        }

        /// <summary>A plain blow is only worth turning to stone for if it comes from a real threat, or it is mobbed.</summary>
        private static bool Worth(Actor d, Actor atk)
        {
            if (atk == null || d == null)
            {
                return false;
            }
            if (Tiers.Of(atk) != Tier.None || Weights.Species(atk) >= Weight.Brute)
            {
                return true;
            }
            int n = 0;
            List<Actor> near = _tmp;
            Choreo.Foes(d, d.current_position, 1.8f, near);
            n = near.Count;
            return n >= 3;
        }

        private static readonly List<Actor> _tmp = new List<Actor>();

        /// <summary>Forced: it turns to stone at once. In a fight it braces on its own when it is mobbed.</summary>
        public override Plan Plan(Ctx c, int level)
        {
            if (c == null || c.Self == null || StoneSkin.Of(c.Self) != null)
            {
                return null;
            }
            Actor foe = c.Focus ?? (c.Enemies.Count > 0 ? c.Enemies[0] : null);
            if (c.Forced)
            {
                return Make(c, foe, 1f);
            }
            if (c.Near2 < 2)
            {
                return null;
            }
            // how many are at it right now
            int at = 0;
            for (int i = 0; i < c.Enemies.Count && i < 8; i++)
            {
                if (c.EnemyDist[i] > 2.5f)
                {
                    break;
                }
                Actor e = c.Enemies[i];
                if (Reads.AimsAt(e, c.Self) || c.EnemyDist[i] <= 1.6f && (Reads.Doing(e) & (Form.Strike | Form.Flurry)) != 0)
                {
                    at++;
                }
            }
            if (at < 3 && !(at >= 2 && c.HpPct < 0.5f))
            {
                return null;
            }
            return Make(c, foe, c.Raw * (1.1f + 0.45f * at) + (c.Desperate ? 25f : 0f));
        }

        private static Plan Make(Ctx c, Actor foe, float score)
        {
            Vector2 dir = foe != null ? foe.current_position - c.Pos : Vector2.right;
            return new Plan
            {
                Target = foe,
                Point = c.Pos,
                Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right,
                Count = 1,
                Score = Mathf.Max(1f, score),
                Data = c.Forced
            };
        }

        public override bool Begin(Actor a, Plan p)
        {
            StoneSkin m = StoneSkin.Make(a, p.Target, this, p.Level);
            return m != null && m.Begin();
        }
    }

    /// <summary>
    /// The stone skin stance (StoneSkinTech): a Choreo move that hardens, holds braced while blows
    /// chip it (Hooks.BeforeDamage takes their damage down), and sheds the stone. While it holds it
    /// is a light move to a would-be grabber (Choreo.Free: grabs may be planned on it; the grab's
    /// contact cracks the shell open and takes the body) but never to the blows themselves: from the
    /// moment a blow reaches it (it is the first contact handler and the first to see a plain blow)
    /// to the end of that frame it is committed, so it stands like rock (the blow only rocks it) and
    /// answers nothing of its own; Hooks.HoldTechniques keeps its own brain out. It pins no one: its
    /// foes stay free to try their grabs.
    /// </summary>
    internal sealed class StoneSkin : ChoreoMove
    {
        private static readonly Dictionary<long, StoneSkin> _on = new Dictionary<long, StoneSkin>();
        private static bool _inner;

        private readonly float _red;
        private readonly float _dur;
        private bool _stance, _shatter, _seized;
        private int _hitFrame = -1;
        private int _cracks;
        private float _chipT, _flash, _lonely, _scanT, _struckT = -9f;
        private Particle _skin;
        private Actor _foe;
        private long _foeId = -1;
        private readonly Color _stone;

        private StoneSkin(Actor a, Technique t, Plan p, Actor foe) : base(a, t, p)
        {
            _red = StoneSkinTech.Reduction(Level);
            _dur = StoneSkinTech.Duration(Level);
            _stone = EarthFx.Stone(a.current_tile);
            Look(foe);
        }

        /// <summary>A stone skin for 'a' facing 'foe' (null if it can't act now). Begin() it.</summary>
        public static StoneSkin Make(Actor a, Actor foe, Technique t, int level)
        {
            if (a == null || t == null || !Moves.CanPerform(a))
            {
                return null;
            }
            Vector2 d = foe != null ? foe.current_position - a.current_position : Vector2.zero;
            // (no target: nobody is held still by the stance)
            Plan p = new Plan
            {
                Tech = t,
                Level = Mathf.Clamp(level, 1, 3),
                Point = foe != null ? foe.current_position : a.current_position,
                Dir = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(a.flip ? 1f : -1f, 0f),
                Count = 1,
                Data = true
            };
            StoneSkin m = new StoneSkin(a, t, p, foe);
            m.Build();
            return m;
        }

        /// <summary>The skin of 'a' if it stands in one right now.</summary>
        public static StoneSkin Of(Actor a)
        {
            if (a == null || _on.Count == 0 || !_on.TryGetValue(a.Sid(), out StoneSkin m) || m.A != a || m.Ended || m.Finished)
            {
                return null;
            }
            return m;
        }

        /// <summary>It stands braced in stone right now (blows are taken down).</summary>
        public bool Up => _stance && !_shatter && !_seized && !Finished && !Ended;

        /// <summary>
        /// A light move to anything that takes hold of it (a grab cracks the shell open), but not in
        /// the frame a blow lands on it: the blow only rocks the stone.
        /// </summary>
        public override bool Interruptible => base.Interruptible || _stance && !Finished && !Ended && (_shatter || _hitFrame != Time.frameCount);

        public bool Begin()
        {
            if (!Start())
            {
                return false;
            }
            _on[A.Sid()] = this;
            return true;
        }

        private void Build()
        {
            // the skin turns to stone: a snap into a braced crouch, a crackle running over it
            Beat harden = new Beat { Tempo = Tempo.Act, Dur = 0.1f, To = new Shape(-8f, 1.14f, 0.86f, 0f, -0.05f), Ease = Ease.OutExpo, Forms = Form.Guard | Form.Body, Track = false };
            harden.Enter = mm => Harden();
            Add(harden);
            // braced: it holds, facing whoever hits it
            Beat stance = new Beat { Tempo = Tempo.Free, Dur = _dur, To = new Shape(-4f, 1.08f, 0.92f, 0f, -0.03f), Ease = Ease.OutQuad, Forms = Form.Guard | Form.Body, Track = false };
            stance.Enter = mm => _stance = true;
            stance.Every = (mm, k) => Hold(k);
            Add(stance);
            // the stone comes off (III: it bursts outward)
            Beat shed = new Beat { Tempo = Tempo.Act, Dur = 0.14f, To = new Shape(6f, 0.92f, 1.08f, 0f), Ease = Ease.OutCubic, Track = false, Label = "shed" };
            shed.Enter = mm => Shed();
            Add(shed);
            Recover(0.2f);
        }

        // ------------------------------------------------------------------ the look --

        private void Look(Actor foe)
        {
            if (foe != null && foe != A)
            {
                _foe = foe;
                _foeId = foe.Sid();
            }
        }

        private void Harden()
        {
            WorldTile tile = A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            Choreo.Sound("event:/SFX/HIT/HitMetal", tile, 0.1f);
            if (_foe != null && _foe.Sid() == _foeId && _foe.isAlive())
            {
                Face(_foe.current_position - Stance);
            }
            _flash = 0.12f;
            if (Fx.HasSilhouette)
            {
                Sprite s = Fx.UnitSprite(A);
                Particle p = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 1, true) : null;
                if (p != null)
                {
                    p.Held = true;
                    p.UseEuler = true;
                    _skin = Glue(p);
                }
            }
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Vector2 body = BodyPos();
            Kit.Flash(body, px, Mathf.RoundToInt(4f * Kk), 0.07f, Fx.WithA(Hue.Core, 0.9f));
            Fx.Ring(Pose.Ground, px, 0.3f, 2f, 11f * Kk, 2, 1, FxLayer.Ground, 5, Art.Ground, 2.2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(_stone, 0.8f), Fx.WithA(Hue.Edge, 0f));
            Kit.DustRing(Pose.Ground, px, tile, 8, 26f, 0.45f, Kk);
            Kit.Crater(Pose.Ground, px * Kk, tile, false, 3f, true, null);
            EarthFx.Flakes(Pose.Ground, Hpx * px * 0.5f, px, 4, Kk * 0.6f, _stone, Vector2.up);
            // a crackle running over the body
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = body + new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(-0.45f, 0.4f)) * Hpx * px;
                Vector2 b = a + new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)) * px;
                Kit.Line(a, b, px, 0.18f, 1, FxLayer.Over, 8, Fx.WithA(Hue.Core, 0.95f), Fx.WithA(Hue.Edge, 0f));
            }
        }

        public override void Frame()
        {
            base.Frame();
            if (_skin == null || !_skin.Alive)
            {
                return;
            }
            // grey as rock, a slow shimmer; a white flash when struck; darker the more it is cracked
            float pulse = 0.6f + 0.06f * Mathf.Sin(T * 5f);
            Color c = Color.Lerp(_stone, Hue.Edge, Level >= 3 ? 0.18f : 0.08f);
            c = Color.Lerp(c, Fx.Darken(_stone, 0.3f), Mathf.Clamp01(_cracks * 0.22f));
            float a = _stance ? pulse : 0.35f;
            if (_flash > 0f)
            {
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(_flash / 0.08f));
                a = Mathf.Max(a, 0.75f);
            }
            Fx.Glue(_skin, this, Fx.WithA(c, a));
        }

        private void Hold(float k)
        {
            float dt = Clock.Dt;
            _flash -= dt;
            _chipT -= dt;
            if (_shatter || _seized)
            {
                Jump("shed");
                return;
            }
            // it faces whoever is at it
            if (_foe != null && _foe.Sid() == _foeId && _foe.isAlive())
            {
                Vector2 d = _foe.current_position - Stance;
                if (d.sqrMagnitude > 0.01f)
                {
                    Face(d);
                }
            }
            if (_chipT > 0f)
            {
                Pose.Nudge = Choreo.Tremble(_chipT, 0.6f);
            }
            // nobody left at it (none close, nothing striking it, not even from afar): the stone comes off early
            _scanT -= dt;
            if (_scanT <= 0f)
            {
                _scanT = 0.25f;
                Choreo.Foes(A, Stance, 3.5f, _tmp, false, true);
                _lonely = _tmp.Count == 0 && T - _struckT > 0.8f ? _lonely + 0.25f : 0f;
                if (_lonely >= 0.75f && BeatT > 0.6f)
                {
                    Jump("shed");
                }
            }
        }

        private static readonly List<Actor> _tmp = new List<Actor>();

        /// <summary>A blow chips it: flakes off the side it came from, a ting, a pixel's shudder.</summary>
        private void Chip(BaseSimObject by, bool hard)
        {
            Actor atk = by != null && by.isActor() ? by.a : null;
            Look(atk);
            _flash = 0.06f;
            if (_chipT > 0.02f && !hard)
            {
                return;   // (a flurry: one chip every few hundredths)
            }
            _chipT = hard ? 0.12f : 0.07f;
            WorldTile tile = A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitStone", tile, 0.06f);
            if (!Vis)
            {
                return;
            }
            Vector2 away = atk != null ? (Vector2)A.current_position - (Vector2)atk.current_position : Vector2.up;
            float px = Px;
            float z = Hpx * px * Random.Range(0.35f, 0.75f);
            Vector2 at = Pose.Ground + new Vector2(0f, z) - (away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.zero) * Hpx * px * 0.25f;
            EarthFx.Flakes(Pose.Ground, z, px, hard ? 6 : 3, Kk, _stone, -away);
            Kit.Sparks(at, px, hard ? 4 : 2, 30f, 70f, Color.Lerp(_stone, Color.white, 0.5f));
            if (hard)
            {
                // a crack runs over the stone
                Vector2 a = at;
                for (int i = 0; i < 3; i++)
                {
                    Vector2 b = a + new Vector2(Random.Range(-2.5f, 2.5f), Random.Range(-2.5f, 1f)) * px;
                    Kit.Line(a, b, px, 0.5f, 1, FxLayer.Over, 9, Fx.WithA(Fx.Darken(_stone, 0.6f), 0.95f), Fx.WithA(Fx.Darken(_stone, 0.6f), 0f));
                    a = b;
                }
            }
        }

        /// <summary>A kick or a heavy blow: the shell cracks (the stance shorter); the fourth crack shatters it.</summary>
        private void Crack(BaseSimObject by)
        {
            _cracks++;
            if (_cracks >= 4)
            {
                Shatter(by);
                return;
            }
            Chip(by, true);
            if (Current != null && Current.Label == null && _stance)
            {
                BeatDur = Mathf.Max(BeatT + 0.25f, BeatDur - 0.55f);
            }
        }

        /// <summary>A blow made to beat a guard (or the last crack): the shell bursts into pieces.</summary>
        private void Shatter(BaseSimObject by)
        {
            if (_shatter)
            {
                return;
            }
            _shatter = true;
            Burst(by, false);
        }

        /// <summary>A grab finds the seams: the shell cracks open and falls away as the body is taken.</summary>
        private void Seize(Actor by)
        {
            if (_seized)
            {
                return;
            }
            _seized = true;
            Burst(by, false);
            if (Dev.On) Dev.Trace("stone skin of " + A.asset.id + "#" + A.Sid() + " seized by " + (by != null ? by.asset.id + "#" + by.Sid() : "?"));
            Moves.Cancel(this);   // (the grab takes the body this very frame)
        }

        /// <summary>The shell breaking into pieces (a shatter, a seizure): flakes everywhere, a crack, dust.</summary>
        private void Burst(BaseSimObject by, bool outward)
        {
            WorldTile tile = A.current_tile;
            Choreo.Sound("event:/SFX/HIT/HitStone", tile);
            Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", tile, 0.08f);
            if (_skin != null)
            {
                Unglue(_skin);
                _skin = null;
            }
            if (!Vis)
            {
                return;
            }
            float px = Px;
            Actor atk = by != null && by.isActor() ? by.a : null;
            Vector2 away = atk != null ? (Vector2)A.current_position - (Vector2)atk.current_position : Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                EarthFx.Flakes(Pose.Ground, Hpx * px * (0.3f + 0.25f * i), px, outward ? 7 : 5, Kk * (outward ? 1.5f : 1f), _stone, outward ? Random.insideUnitCircle : away);
            }
            Fx.Afterimage(this, 0.25f, Fx.WithA(Color.Lerp(_stone, Color.white, 0.3f), 0.8f), Fx.WithA(_stone, 0f));
            Kit.DustRing(Pose.Ground, px, tile, outward ? 14 : 8, outward ? 46f : 30f, 0.5f, Kk);
            Kit.Flash(BodyPos(), px, Mathf.RoundToInt(4f * Kk), 0.06f, Fx.WithA(Color.Lerp(_stone, Color.white, 0.6f), 0.9f));
        }

        /// <summary>The stance is over: the stone comes off (III: it bursts outward, the shards knocking back those close).</summary>
        private void Shed()
        {
            bool burst = Level >= 3 && !_shatter && !_seized;
            _stance = false;
            if (burst)
            {
                Burst(null, true);
                float r = 1.6f * Mathf.Lerp(1f, Kk, 0.5f);
                AreaBlow(Pose.Ground, r, 0.6f, null, Hurt.Reel, 1f, 1.1f);
                if (Vis)
                {
                    Kit.Shockwave(Pose.Ground, Px, 3f, r / Px, 0.3f, Fx.WithA(_stone, 0.9f), 2);
                }
                Ruin.Quake(A, Pose.Ground, Scale, 0.8f, Kk);
                return;
            }
            if (_skin != null)
            {
                // it simply flakes off and falls
                if (Vis)
                {
                    EarthFx.Flakes(Pose.Ground, Hpx * Px * 0.6f, Px, 4, Kk * 0.5f, _stone, Vector2.down);
                    Fx.Afterimage(this, 0.3f, Fx.WithA(_stone, 0.6f), Fx.WithA(_stone, 0f));
                }
                Unglue(_skin);
                _skin = null;
            }
        }

        private void Gone()
        {
            _stance = false;
            if (_on.TryGetValue(A.Sid(), out StoneSkin m) && m == this)
            {
                _on.Remove(A.Sid());
            }
            if (_skin != null && _skin.Alive)
            {
                _skin.Kill();
            }
            _skin = null;
        }

        public override void Abort()
        {
            base.Abort();
            Gone();
        }

        public override void OnEnd(bool aborted)
        {
            base.OnEnd(aborted);
            Gone();
        }

        // ----------------------------------------------------------------- the hooks --

        /// <summary>
        /// Hooks.BeforeDamage: a blow on the stone. It takes most of it (the rest goes through); a kick
        /// or a heavy blow less, and cracks it; a technique made to beat a guard shatters it and lands
        /// in full.
        /// </summary>
        internal static bool Shield(Actor v, float damage, AttackType type, BaseSimObject attacker)
        {
            if (_inner || _on.Count == 0 || v == null)
            {
                return false;
            }
            StoneSkin m = Of(v);
            if (m == null || !m.Up)
            {
                return false;
            }
            if (type != AttackType.Weapon && type != AttackType.Other && type != AttackType.Explosion)
            {
                return false;   // (fire, poison, divine wrath: the stone is no help)
            }
            m._hitFrame = Time.frameCount;
            m._struckT = m.T;
            Move by = Combat.Landing;
            Form f = by != null && by.A != v ? Reads.FormsOf(by) : Form.None;
            if (by != null && by.A != v && by.Tech != null && (by.Tech.Answers & Form.Guard) != 0)
            {
                m.Shatter(attacker);
                return false;   // the blow made for a guard lands in full
            }
            float red = m._red;
            if ((f & (Form.Kick | Form.Knee | Form.Heavy)) != 0)
            {
                red *= 0.5f;
                m.Crack(attacker);
                if (m._shatter)
                {
                    return false;
                }
            }
            else
            {
                m.Chip(attacker, false);
            }
            float d = damage * (1f - red);
            if (d < 1f)
            {
                return true;
            }
            _inner = true;
            try
            {
                v.getHit(d, false, type, attacker, false, false, true);
            }
            catch (Exception e)
            {
                SakugaMod.Report("stone skin", e);
            }
            finally
            {
                _inner = false;
            }
            return true;
        }

        /// <summary>
        /// Hooks.Reached: a move reached a unit in stone. A grab (a throw) cracks the shell open and
        /// takes the body; any other blow finds rock (this frame the stance stands firm).
        /// </summary>
        internal static void Reached(Move m, Actor v, Contact c)
        {
            if (_on.Count == 0 || m == null || v == null || m.A == v || c != Contact.Hit)
            {
                return;
            }
            StoneSkin s = Of(v);
            if (s == null || !s.Up)
            {
                return;
            }
            if ((Reads.FormsOf(m) & (Form.Grab | Form.Throw)) != 0)
            {
                s.Seize(m.A);
                return;
            }
            s._hitFrame = Time.frameCount;
            s._struckT = s.T;
        }

        /// <summary>
        /// Combat.ContactHandlers (first of them): a move reaches a unit in stone. It is braced: this
        /// frame it stands firm and answers nothing (the stone takes the blow; a grab cracks it open
        /// in Reached). Never answers itself.
        /// </summary>
        internal static Contact Braced(Move m, Actor v, bool area)
        {
            if (_on.Count > 0 && m != null && v != null && m.A != v)
            {
                StoneSkin s = Of(v);
                if (s != null && s._stance)
                {
                    s._hitFrame = Time.frameCount;
                    s._struckT = s.T;
                }
            }
            return Contact.Hit;
        }

        /// <summary>A plain blow or shot of the game is about to land on 'v': if it stands in stone it is braced (StoneSkinBracePatch).</summary>
        internal static void BraceFor(Actor v)
        {
            if (_on.Count > 0 && v != null)
            {
                StoneSkin s = Of(v);
                if (s != null && s._stance)
                {
                    s._hitFrame = Time.frameCount;
                    s._struckT = s.T;
                }
            }
        }

        /// <summary>Hooks.HitDealt: a blow landed on the stone (this frame it stands firm).</summary>
        internal static void Struck(Actor a, Actor v, HitResult r, Move by)
        {
            if (_on.Count == 0 || v == null)
            {
                return;
            }
            StoneSkin s = Of(v);
            if (s != null && s._stance)
            {
                s._hitFrame = Time.frameCount;
                s._struckT = s.T;
            }
        }

        /// <summary>Hooks.HoldTechniques: a unit standing in stone starts nothing else.</summary>
        internal static bool Holds(Actor a)
        {
            StoneSkin s = Of(a);
            return s != null && s.Up;
        }

        internal static void ClearAll()
        {
            _on.Clear();
            _inner = false;
        }
    }

    /// <summary>
    /// A plain blow or shot of the game is about to land (MapBox.applyAttack): before any answer
    /// looks at it (they take their turns in a shuffled order), a unit standing in stone is braced:
    /// it stands firm and answers nothing of its own.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), "applyAttack")]
    internal static class StoneSkinBracePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BaseSimObject pTargetToCheck)
        {
            try
            {
                if (pTargetToCheck != null && pTargetToCheck.isActor())
                {
                    StoneSkin.BraceFor(pTargetToCheck.a);
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("stone skin brace", e);
            }
        }
    }
}
