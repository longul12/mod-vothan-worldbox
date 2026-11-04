using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// KI SHOT. A master with a ranged attack doesn't loose the game's arrow or orb: it draws, and
    /// fires a bolt of its tier's light (an archer a glowing arrow, a gunner a tracer) that flies
    /// true to its target and bursts. It comes on the game's own cadence (it replaces the shot,
    /// FlowStrikes), deals what the shot would have (the tier's Might on top), and the game's own
    /// launch and impact sounds of the weapon go with it.
    ///
    /// Beat sheet (a Master; Grandmasters x0.85, Gods x0.7):
    ///
    ///   DRAW   0.00-0.12  faces the target; an archer leans back drawing the string (a squeeze),
    ///                     a mage lifts its hand (a stretch); light gathers at the bow or the palm:
    ///                     a tier-coloured mote growing round a white core, motes drawn in from
    ///                     round it; a "ting" just before the release
    ///   LOOSE  0.12-0.19  the recoil (the body snaps back and springs upright), a muzzle flash and two
    ///                     sparks; the bolt is away (a fan of two or three when the weapon throws
    ///                     several, a beat apart)
    ///   FLIGHT            the bolt (white core, tier edge, a short trail of its own light) homes on
    ///                     the target's body at 16-30 tiles a second
    ///   BURST             a flash, a ring, sparks in the tier colour, the weapon's impact sound. The
    ///                     target may evade it (Combat.Reach: it flies on past and fades); it can't
    ///                     be parried. A target that died on the way: the bolt bursts on the spot.
    ///
    /// Off screen (or over the flow budget) the bolt is fired at once, without the drawing pose.
    /// A mage still casts its own spells about a third of the time (TryCast). A melee master
    /// whose blows can't get there (water, a wall, fire between them) or whose enemy is knocked
    /// up out of reach throws the same ki from its palm (FlowStrikes.Bolt).
    /// </summary>
    internal sealed class KiShot : Move
    {
        /// <summary>Any technique may cut it short (it is between-techniques fighting).</summary>
        public override bool Interruptible => true;

        /// <summary>A bolt of light (an archer's glowing arrow: Projectile; an orb or a tracer: Ki too), quick.</summary>
        public override Form Forms => _kind == KiBolts.Kind.Arrow ? Form.Projectile | Form.Fast : Form.Ki | Form.Projectile | Form.Fast;

        /// <summary>The one it shoots at.</summary>
        public override Actor Aim => _v;

        private enum Phase
        {
            Draw,
            Loose
        }

        private readonly Palette _pal;
        private readonly float _k, _s;
        private readonly KiBolts.Kind _kind;
        private Actor _v;
        private long _vid;
        private Phase _phase = Phase.Draw;
        private float _pt, _tDraw, _tLoose, _moteT;
        private bool _entered, _glinted;
        private Particle _glow, _core;

        private KiShot(Actor a, Actor v) : base(a)
        {
            Level = 3;
            Tier t = Tiers.Of(a);
            _pal = Palette.For(t, 3);
            _k = K;
            _s = t >= Tier.God ? 0.7f : t == Tier.Grandmaster ? 0.85f : 1f;
            _kind = KindOf(a);
            _v = v;
            _vid = v.Sid();
            _tDraw = 0.12f * _s;
            _tLoose = 0.07f * _s;
            Face();
        }

        // ------------------------------------------------------------- start --

        public static bool Begin(Actor a, Actor v)
        {
            if (a == null || v == null || !v.isAlive() || !Moves.CanPerform(a))
            {
                return false;
            }
            KiShot m = new KiShot(a, v);
            if (!Moves.Start(m))
            {
                return false;
            }
            FlowStrikes.Live++;
            return true;
        }

        /// <summary>
        /// The same shot without the drawing pose (off screen, over budget). Its bolts carry a shot
        /// that is never played (never started) as the move they come from, so the target answers
        /// them exactly as it answers a drawn one (Combat.Reach: a Mirage Step), on screen or off.
        /// </summary>
        public static void FireNow(Actor a, Actor v)
        {
            KiShot by = new KiShot(a, v);
            Fire(a, v, by, Muzzle(a, v), by._pal, by._kind, true);
        }

        /// <summary>
        /// A mage's own spell, cast the way the game casts it (CombatActionLibrary.tryToCastSpell).
        /// False if it has none, can't, or the spell didn't take (then it shoots).
        /// </summary>
        public static bool TryCast(Actor a, Actor v)
        {
            try
            {
                if (!a.hasSpells() || !a.canUseSpells() || a.hasStatus("possessed"))
                {
                    return false;
                }
                AttackData data = new AttackData(a, v.current_tile, v.current_position, a.current_position, v, a.kingdom, AttackType.Weapon, false, true,
                    true, a.getWeaponAsset()?.projectile ?? string.Empty);
                if (!CombatActionLibrary.tryToCastSpell(data))
                {
                    return false;
                }
                a.punchTargetAnimation(v.current_position, true, true);
                a.spendStamina(5);
                a.makeSoundAttack();
                return true;
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("flow spell", e);
                return false;
            }
        }

        private static KiBolts.Kind KindOf(Actor a)
        {
            try
            {
                if (a.hasWeapon())
                {
                    EquipmentAsset w = a.getWeaponAsset();
                    if (w != null && w.projectile == "arrow")
                    {
                        return KiBolts.Kind.Arrow;
                    }
                    if (a.isWeaponFirearm() || w != null && w.projectile == "shotgun_bullet")
                    {
                        return KiBolts.Kind.Tracer;
                    }
                }
            }
            catch
            {
                // an odd weapon: a ki bolt it is
            }
            return KiBolts.Kind.Orb;
        }

        /// <summary>Where the shot leaves from: the bow in front of it, or the raised palm.</summary>
        private static Vector2 Muzzle(Actor a, Actor v)
        {
            float px = FlowStrikes.PxOf(a);
            float h = FlowStrikes.SpriteH(a);
            float fwd = v.current_position.x >= a.current_position.x ? 1f : -1f;
            return a.current_position + new Vector2(fwd * 3f * px, Mathf.Max(0f, a.position_height) + h * 0.55f);
        }

        private Vector2 Hand()
        {
            float fwd = Pose.FaceRight ? 1f : -1f;
            return _kind == KiBolts.Kind.Orb
                ? BodyPos() + new Vector2(fwd * 2.5f, 2.5f) * Px
                : BodyPos() + new Vector2(fwd * 3f, 1f) * Px;
        }

        private void Face()
        {
            if (_v == null)
            {
                return;
            }
            float dx = _v.current_position.x - Pose.Ground.x;
            if (Mathf.Abs(dx) > 0.04f)
            {
                Pose.FaceRight = dx > 0f;
            }
        }

        private bool Visible => Fx.OnScreen(Pose.Ground, 0.3f);

        private void Next(Phase p)
        {
            _phase = p;
            _pt = 0f;
            _entered = false;
        }

        // --------------------------------------------------------------- tick --

        public override void Tick(float dt)
        {
            _pt += dt;
            if (_phase == Phase.Draw)
            {
                Draw(dt);
            }
            else
            {
                Loose();
            }
            if (!Finished && T > 1f)
            {
                Neutral();
                Finished = true;
            }
        }

        private void Draw(float dt)
        {
            if (!_entered)
            {
                _entered = true;
                if (Visible)
                {
                    Vector2 at = Hand();
                    _glow = Fx.Spawn(Art.Disk(2, 1f, 0.3f), at, Px, 1f, FxLayer.Over, 8);
                    if (_glow != null)
                    {
                        _glow.Held = true;
                        _glow.Color(Fx.WithA(_pal.Edge, 0.85f));
                    }
                    _core = Fx.Spawn(Art.Disk(1, 1f, 0f), at, Px, 1f, FxLayer.Over, 9);
                    if (_core != null)
                    {
                        _core.Held = true;
                        _core.Color(Color.white);
                    }
                }
            }
            if (!TargetOk() && !Retarget())
            {
                Drop();
                Neutral();
                Finished = true;   // nothing left to shoot at: the cooldown stands, the shot is never loosed
                return;
            }
            Face();
            float k = Mathf.Clamp01(_pt / _tDraw);
            float e = Anim.OutQuad(k);
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            if (_kind == KiBolts.Kind.Orb)
            {
                // the palm raised: up on its toes
                Pose.Rot = -3f * e;
                Pose.SX = 1f - 0.04f * e;
                Pose.SY = 1f + 0.07f * e;
            }
            else
            {
                // the string drawn: leaning back, squeezed
                Pose.Rot = -6f * e;
                Pose.SX = 1f - 0.06f * e;
                Pose.SY = 1f + 0.04f * e;
            }
            float px = Px;
            Vector2 hand = Hand();
            float grow = Mathf.Lerp(0.5f, 1.15f, e);
            if (_glow != null && _glow.Alive)
            {
                _glow.Pos = hand;
                _glow.Size0 = _glow.Size1 = grow;
            }
            if (_core != null && _core.Alive)
            {
                _core.Pos = hand;
                _core.Size0 = _core.Size1 = grow;
            }
            if (Visible)
            {
                _moteT -= dt;
                if (_moteT <= 0f)
                {
                    _moteT = 0.03f;
                    // a mote of light drawn in to the hand
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 from = hand + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(4f, 6f) * px;
                    Particle m = Fx.Spawn(Art.Pixel, from, px, 0.08f, FxLayer.Over, 7);
                    m?.Moving((hand - from) / 0.08f).Color(Fx.WithA(_pal.Edge, 0f), Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.6f));
                }
                if (!_glinted && k >= 0.7f)
                {
                    _glinted = true;
                    Fx.Glint(hand + new Vector2(0f, 1f * px), px, 0.12f, _pal.Core, 0.7f);
                }
            }
            if (k >= 1f)
            {
                Next(Phase.Loose);
            }
        }

        private void Loose()
        {
            if (!_entered)
            {
                _entered = true;
                Drop();
                if (TargetOk())
                {
                    Vector2 hand = Hand();
                    Fire(A, _v, this, hand, _pal, _kind, true, HeldUp());
                    if (Visible)
                    {
                        float px = Px;
                        Vector2 d = KiBolts.Aim(_v) - hand;
                        d = d.sqrMagnitude > 1e-4f ? d.normalized : new Vector2(Pose.FaceRight ? 1f : -1f, 0f);
                        Kit.Flash(hand, px, Mathf.Max(2, Mathf.RoundToInt(3f * _k)), 0.06f, Fx.WithA(_pal.Core, 0.95f), FxLayer.Top, 22);
                        for (int i = 0; i < 2; i++)
                        {
                            float a = Mathf.Atan2(d.y, d.x) + Random.Range(-0.7f, 0.7f);
                            Particle s = Fx.Spawn(Art.Sparks[0], hand, px, 0.1f, FxLayer.Top, 21);
                            if (s == null)
                            {
                                break;
                            }
                            s.Moving(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(50f, 80f) * px, 0f, 8f);
                            s.FaceVel = true;
                            s.Color(Color.white, _pal.Edge, Fx.WithA(_pal.Edge, 0f));
                        }
                    }
                }
            }
            float k = Mathf.Clamp01(_pt / _tLoose);
            float amp = Anim.Spring(_pt, 12f, 26f);
            Pose.Rot = -10f * amp;
            Pose.SX = 1f + 0.08f * amp;
            Pose.SY = 1f - 0.05f * amp;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
            if (k >= 1f)
            {
                Neutral();
                Finished = true;
            }
        }

        /// <summary>
        /// The shots this draw stands in for: at high speed the draw takes longer in game time
        /// (FlowStrikes.Speed) than the game's own cadence of shots, and this one is worth the ones
        /// it held up (1 up to x3, or whenever the cooldown is longer anyway).
        /// </summary>
        private float HeldUp()
        {
            float cd = 0.5f;
            try
            {
                cd = A.getAttackCooldown();
            }
            catch
            {
                // a unit being recycled: the default stands
            }
            float busy = _tDraw + _tLoose;
            cd = Mathf.Max(0.08f, cd);
            return Mathf.Max(1f, Mathf.Max(cd, busy * FlowStrikes.Speed()) / Mathf.Max(cd, busy));
        }

        private bool TargetOk()
        {
            return _v != null && Combat.Living(_v) && _v.Sid() == _vid && !_v.isInMagnet() && !_v.isInsideSomething() && Combat.IsFoe(A, _v);
        }

        /// <summary>The target is gone before the release: the nearest other enemy in range (never one a set piece holds, as Shoot).</summary>
        private bool Retarget()
        {
            float range = Mathf.Max(2f, A.getAttackRange());
            Actor best = null;
            float bd = float.MaxValue;
            foreach (Actor u in Combat.FoesAround(A, Pose.Ground, range, true, true))
            {
                if (!Combat.Living(u) || Director.InScene(u) || Moves.TryGet(u, out Move um) && um is Duelist)
                {
                    continue;   // (falling already, or in a duel's bout, a team technique, a God's cinematic)
                }
                float d = (u.current_position - Pose.Ground).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = u;
                }
            }
            if (best == null)
            {
                return false;
            }
            _v = best;
            _vid = best.Sid();
            return true;
        }

        private void Drop()
        {
            if (_glow != null && _glow.Alive)
            {
                _glow.Kill();
            }
            if (_core != null && _core.Alive)
            {
                _core.Kill();
            }
            _glow = null;
            _core = null;
        }

        private void Neutral()
        {
            Pose.Rot = 0f;
            Pose.SX = Pose.SY = 1f;
            Pose.Height = 0f;
            Pose.Nudge = Vector2.zero;
        }

        public override void Abort()
        {
            Drop();
        }

        public override void OnEnd(bool aborted)
        {
            Drop();
            FlowStrikes.Live = Mathf.Max(0, FlowStrikes.Live - 1);
        }

        // --------------------------------------------------------------- fire --

        /// <summary>
        /// Looses the shot: one bolt, or a fan of two or three (a beat apart) when the weapon throws
        /// several. Together they deal what the game's projectiles would have ('scale' of them: HeldUp).
        /// </summary>
        private static void Fire(Actor a, Actor v, Move by, Vector2 from, Palette pal, KiBolts.Kind kind, bool sound, float scale = 1f)
        {
            float projectiles = 1f;
            string launch = null, impact = null;
            try
            {
                projectiles = Mathf.Clamp(a.stats["projectiles"], 1f, 6f);
                EquipmentAsset w = a.getWeaponAsset();
                ProjectileAsset pa = w != null && !string.IsNullOrEmpty(w.projectile) ? AssetManager.projectiles.get(w.projectile) : null;
                if (pa != null)
                {
                    launch = pa.sound_launch;
                    impact = pa.sound_impact;
                }
            }
            catch
            {
                // a unit being recycled: plain defaults
            }
            int n = Mathf.Clamp(Mathf.RoundToInt(projectiles), 1, 3);
            float mult = 0.8f * projectiles / n * scale;
            Tier t = Tiers.Of(a);
            float speed = kind == KiBolts.Kind.Tracer ? 30f : kind == KiBolts.Kind.Arrow ? 22f : 16f;
            speed += t >= Tier.God ? 6f : t == Tier.Grandmaster ? 3f : 0f;
            Vector2 aim = KiBolts.Aim(v) - from;
            Vector2 dir = aim.sqrMagnitude > 1e-4f ? aim.normalized : new Vector2(a.flip ? 1f : -1f, 0f);
            for (int i = 0; i < n; i++)
            {
                float spread = n == 1 ? 0f : n == 2 ? (i == 0 ? -7f : 7f) : (i - 1) * 11f;
                KiBolts.Fire(a, v, by, from, FlashStep.Rotate(dir, spread), kind, pal, mult, speed, i * 0.035f, impact);
            }
            if (sound)
            {
                // a palm's ki (a melee master's, or a weapon without a shot sound of its own) whooshes
                FlowStrikes.Sound(!string.IsNullOrEmpty(launch) ? launch : "event:/SFX/UNIQUE/SpawnWhoosh", a.current_tile, 0.05f);
            }
        }
    }

    /// <summary>
    /// Ki bolts in flight: not moves (the shooter is free the moment it looses), ticked once per
    /// frame after the world's step, in move time (they pause with the game).
    /// </summary>
    internal static class KiBolts
    {
        internal enum Kind
        {
            Orb,
            Arrow,
            Tracer
        }

        private sealed class Bolt
        {
            public Actor A, V;
            public long AId, VId;
            public Move By;
            public Kind Kind;
            public Palette Pal;
            public Vector2 Pos, Dir, Target;
            public float Speed, Mult, Delay, Age, Px, K, VH, Fade = -1f;
            public bool Whiffed;
            /// <summary>A God's ki: a bigger, brighter bolt that scorches the ground where it bursts now and then.</summary>
            public bool Big;
            public string Impact;
            public Particle Head, Glow;
            /// <summary>Its head's and glow's frames, made once (respawned from these while the particle budget is full).</summary>
            public Sprite[] HeadFrames, GlowFrames;
            public float SparkT;
        }

        private static readonly List<Bolt> _list = new List<Bolt>();
        private static Sprite _arrow;

        public static int Count => _list.Count;

        /// <summary>Where a bolt flies to: the middle of the target's body as it is drawn.</summary>
        public static Vector2 Aim(Actor v)
        {
            return FlowStrikes.Body(v, 0.45f);
        }

        public static void Fire(Actor a, Actor v, Move by, Vector2 from, Vector2 dir, Kind kind, Palette pal, float mult, float speed, float delay, string impact)
        {
            if (a == null || v == null)
            {
                return;
            }
            float vh = FlowStrikes.SpriteH(v);
            Bolt b = new Bolt
            {
                A = a,
                AId = a.Sid(),
                V = v,
                VId = v.Sid(),
                VH = vh,
                By = by,
                Kind = kind,
                Pal = pal,
                Pos = from,
                Dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right,
                Target = FlowStrikes.Feet(v) + new Vector2(0f, vh * 0.45f),
                Speed = speed,
                Mult = mult,
                Delay = delay,
                Px = FlowStrikes.PxOf(a),
                K = FlowStrikes.KOf(a),
                Impact = impact,
                Big = Tiers.Of(a) >= Tier.God
            };
            if (_list.Count >= MaxFlying)
            {
                // a war full of master archers: past the cap the bolt lands at once (same damage, a burst, no flight)
                b.Pos = b.Target;
                try
                {
                    Burst(b, TargetOk(b));
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("ki bolt", e);
                }
                Drop(b);
                return;
            }
            _list.Add(b);
        }

        /// <summary>Bolts in flight at most (each is cheap, but a huge war of archers would pile them up).</summary>
        private const int MaxFlying = 240;

        public static void Clear()
        {
            foreach (Bolt b in _list)
            {
                Drop(b);
            }
            _list.Clear();
        }

        public static void Tick(float dt)
        {
            if (_list.Count == 0 || dt <= 0f)
            {
                return;
            }
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                if (i >= _list.Count)
                {
                    continue;
                }
                Bolt b = _list[i];
                bool alive;
                try
                {
                    alive = Step(b, dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("ki bolt", e);
                    alive = false;
                }
                if (!alive)
                {
                    Drop(b);
                    _list.RemoveAt(i);
                }
            }
        }

        private static bool TargetOk(Bolt b)
        {
            Actor v = b.V;
            // (one another blow has just brought to nothing is no target: it falls at the game's next death check)
            return v != null && Combat.Living(v) && v.Sid() == b.VId && !v.isInMagnet() && !v.isInsideSomething();
        }

        /// <summary>One frame of flight. False when the bolt is spent.</summary>
        private static bool Step(Bolt b, float dt)
        {
            if (b.Delay > 0f)
            {
                // a fan's later bolts leave a beat after the first
                b.Delay -= dt;
                if (b.Delay > 0f)
                {
                    return true;
                }
            }
            b.Age += dt;
            if (b.Fade >= 0f)
            {
                // spent (it missed, or burst against an evading target): it flies on and fades
                b.Fade -= dt;
                Vector2 was = b.Pos;
                b.Pos += b.Dir * b.Speed * dt;
                Render(b, was, b.Fade / 0.22f);
                return b.Fade > 0f;
            }
            bool targetOk = TargetOk(b);
            if (targetOk)
            {
                b.Target = FlowStrikes.Feet(b.V) + new Vector2(0f, b.VH * 0.45f);   // (its height is taken once, at the shot)
            }
            // home on it: a bolt of ki doesn't miss a target that merely moved
            Vector2 to = b.Target - b.Pos;
            float dist = to.magnitude;
            if (dist > 1e-4f)
            {
                Vector2 want = to / dist;
                float turn = 14f * dt;
                float ang = Vector2.SignedAngle(b.Dir, want) * Mathf.Deg2Rad;
                ang = Mathf.Clamp(ang, -turn, turn);
                b.Dir = FlashStep.Rotate(b.Dir, ang * Mathf.Rad2Deg).normalized;
            }
            float step = b.Speed * dt;
            Vector2 last = b.Pos;
            if (dist <= step + 0.12f * b.K)
            {
                b.Pos = b.Target;
                Render(b, last, 1f);
                return Burst(b, targetOk);
            }
            b.Pos += b.Dir * step;
            Render(b, last, 1f);
            if (b.Age > 1.6f)
            {
                Puff(b, false);
                return false;   // it never got there: it fizzles
            }
            return true;
        }

        /// <summary>The bolt reaches its target: it may be evaded, otherwise it lands.</summary>
        private static bool Burst(Bolt b, bool targetOk)
        {
            Actor a = b.A;
            bool shooterOk = a != null && a.Sid() == b.AId && a.isAlive();
            if (targetOk && shooterOk && !Combat.IsFoe(a, b.V))
            {
                // a blur for a heartbeat (mid-blink), or no longer an enemy: nothing there to hit,
                // it flies on through and fades (no burst on a body it never touched)
                b.Whiffed = true;
                b.Fade = 0.22f;
                return true;
            }
            if (targetOk && shooterOk)
            {
                Contact c = Combat.Reach(b.By, b.V, true);
                if (c == Contact.Evaded)
                {
                    b.Whiffed = true;
                    b.Fade = 0.22f;   // through the afterimage, on past it
                    return true;
                }
                float mult = b.Mult;
                Duel d = Duels.Of(b.V);
                if (d != null && Moves.Busy(a))
                {
                    mult *= FlowStrikes.Comp(a, b.V);   // the duel takes a shooter mid-move for a technique
                }
                HitResult r = Combat.Hit(a, b.V, mult, true, true, 0f, b.By);
                if (r.Landed)
                {
                    try
                    {
                        a.attackTargetActions(b.V, b.V.current_tile);
                    }
                    catch (System.Exception e)
                    {
                        SakugaMod.Report("ki on-hit", e);
                    }
                    a.addExperience(2);
                }
                if (Kit.Visible(b.Pos) && r.Landed)
                {
                    Tier vt = Tiers.Of(b.V);
                    FlashStep.FlashUnit(b.V, vt != Tier.None && !r.Killed ? Tiers.Color(vt) : Color.white, 0.05f);
                    if (r.Crit)
                    {
                        Kit.ImpactFrame(b.Pos, b.Px, 0.3f * b.K, b.Pal, true);
                    }
                }
            }
            Puff(b, true);
            if (b.Big && shooterOk && targetOk && Combat.Grounded(b.V) && Random.value < 0.25f)
            {
                Kit.Ravage(b.V.current_position, 0.5f, 1, a, false);   // a God's ki scorches the ground it bursts on
            }
            if (!string.IsNullOrEmpty(b.Impact))
            {
                FlowStrikes.Sound(b.Impact, Combat.TileAt(b.Pos), 0.05f);
            }
            return false;
        }

        /// <summary>The burst: a flash, a ring, sparks of the tier's colour (a small one when it fizzles).</summary>
        private static void Puff(Bolt b, bool hit)
        {
            Vector2 at = b.Pos;
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = b.Px;
            Color edge = b.Pal.Edge;
            Kit.Flash(at, px, hit ? (b.Big ? 6 : 4) : 2, hit ? 0.07f : 0.05f, Fx.WithA(b.Pal.Core, 0.95f), FxLayer.Top, 24);
            if (!hit)
            {
                return;
            }
            Fx.Ring(at, px, 0.16f, 2f, (b.Big ? 12f : 8f) * b.K, 1, 1, FxLayer.Top, 3, 0.55f, 2.5f, Fx.WithA(b.Pal.Core, 0.9f), Fx.WithA(edge, 0.5f), Fx.WithA(edge, 0f));
            Kit.Sparks(at, px, (b.Kind == Kind.Orb ? 5 : 3) + (b.Big ? 3 : 0), 40f, b.Big ? 120f : 90f, edge);
            Particle star = Fx.Spawn(Art.HitStars[0], at, px, 0.06f, FxLayer.Top, 25);
            if (star != null)
            {
                star.Rot = Random.Range(0f, 90f);
                star.Color(Color.white, Fx.WithA(edge, 0.9f), Fx.WithA(edge, 0f));
            }
        }

        /// <summary>Draws the bolt where it is now and the streak of light it left since 'last'.</summary>
        private static void Render(Bolt b, Vector2 last, float alpha)
        {
            if (!Kit.Visible(b.Pos, 0.2f))
            {
                Drop(b);
                return;
            }
            float px = b.Px;
            float ang = Mathf.Atan2(b.Dir.y, b.Dir.x) * Mathf.Rad2Deg;
            alpha = Mathf.Clamp01(alpha);
            if (b.Head == null || !b.Head.Alive)
            {
                b.HeadFrames ??= new[] { b.Kind == Kind.Arrow ? ArrowSprite() : b.Kind == Kind.Tracer ? Art.Lines[0] : Art.Disk(b.Big ? 2 : 1, 1f, 0f) };
                b.Head = Fx.Spawn(b.HeadFrames, b.Pos, px, 1f, FxLayer.Top, 12);
                if (b.Head != null)
                {
                    b.Head.Held = true;
                }
            }
            if (b.Glow == null || !b.Glow.Alive)
            {
                b.GlowFrames ??= new[] { Art.Disk((b.Kind == Kind.Orb ? 2 : 1) + (b.Big ? 1 : 0), 1f, 0.35f) };
                b.Glow = Fx.Spawn(b.GlowFrames, b.Pos, px, 1f, FxLayer.Top, 11);
                if (b.Glow != null)
                {
                    b.Glow.Held = true;
                }
            }
            if (b.Head != null && b.Head.Alive)
            {
                b.Head.Pos = b.Pos;
                b.Head.Rot = b.Kind == Kind.Arrow ? ang : b.Kind == Kind.Tracer ? ang - 90f : 0f;
                b.Head.Grad = null;
                b.Head.R.color = Fx.WithA(b.Kind == Kind.Arrow ? Color.Lerp(Color.white, b.Pal.Edge, 0.2f) : Color.white, alpha);
            }
            if (b.Glow != null && b.Glow.Alive)
            {
                b.Glow.Pos = b.Pos - b.Dir * (b.Kind == Kind.Arrow ? 3f : 0.5f) * px;
                b.Glow.Grad = null;
                b.Glow.R.color = Fx.WithA(b.Pal.Edge, 0.75f * alpha);
            }
            // the streak: its own light along the way it came, fading fast (a war full of archers
            // keeps its particles modest: fewer streaks, no motes)
            int n = _list.Count;
            if ((b.Pos - last).sqrMagnitude > px * px * 0.25f && (n <= 90 || (Time.frameCount & 1) == 0))
            {
                Kit.Line(last, b.Pos, px, b.Kind == Kind.Tracer ? 0.06f : 0.1f, b.Kind == Kind.Orb ? 2 : 1, FxLayer.Top, 10,
                    Fx.WithA(b.Pal.Core, 0.85f * alpha), Fx.WithA(b.Pal.Edge, 0.6f * alpha), Fx.WithA(b.Pal.Edge, 0f));
            }
            if (b.Kind == Kind.Orb && n <= 40)
            {
                b.SparkT -= Clock.Dt;
                if (b.SparkT <= 0f)
                {
                    b.SparkT = 0.04f;
                    Particle s = Fx.Spawn(Art.Pixel, b.Pos + Random.insideUnitCircle * 1.5f * px, px, 0.14f, FxLayer.Top, 9);
                    s?.Moving(-b.Dir * Random.Range(6f, 14f) * px + Random.insideUnitCircle * 6f * px, 0f, 3f)
                        .Color(Fx.WithA(b.Pal.Core, 0.9f * alpha), Fx.WithA(b.Pal.Edge, 0.6f * alpha), Fx.WithA(b.Pal.Edge, 0f));
                }
            }
        }

        private static void Drop(Bolt b)
        {
            // (after a world clear the pool already took them back: never touch a recycled particle)
            if (b.Head != null && b.Head.Alive)
            {
                b.Head.Kill();
            }
            if (b.Glow != null && b.Glow.Alive)
            {
                b.Glow.Kill();
            }
            b.Head = null;
            b.Glow = null;
        }

        /// <summary>A 9x3 arrow pointing +x: head, shaft, fletching (white; tinted when drawn).</summary>
        private static Sprite ArrowSprite()
        {
            if (_arrow != null)
            {
                return _arrow;
            }
            Canvas c = new Canvas(9, 3);
            for (int x = 1; x <= 6; x++)
            {
                c.Set(x, 1, 0.85f, 1f);
            }
            c.Set(7, 1, 1f, 1f);
            c.Set(8, 1, 1f, 1f);
            c.Set(7, 0, 0.92f, 1f);
            c.Set(7, 2, 0.92f, 1f);
            c.Set(0, 0, 0.7f, 0.9f);
            c.Set(0, 2, 0.7f, 0.9f);
            c.Set(1, 0, 0.7f, 0.9f);
            c.Set(1, 2, 0.7f, 0.9f);
            _arrow = c.ToSprite("sakuga_ki_arrow", new Vector2(0.5f, 0.5f));
            return _arrow;
        }
    }
}
