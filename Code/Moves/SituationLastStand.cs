using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// LAST STAND. A tiered fighter cut off from its side (no ally within 6 tiles) with five or more
    /// enemies within 4 tiles, enough of them to be a real threat to it (their power at least a third
    /// of its own: a Master stands against a mob, a God only against Masters), plants its feet and
    /// burns. While it lasts: +30% damage (ordinary blows and techniques), it thinks twice as fast and
    /// waits half as long between techniques. Its outline burns in its tier colour and embers rise
    /// off it. It ends when fewer than three enemies are left near it (it held: Sakuga XP and a
    /// promotion trial), when an ally reaches it (relieved), when it stops fighting for 4 s, or when it
    /// dies (the light goes out with it). A Master or better on screen is announced.
    /// </summary>
    internal static class SitLastStand
    {
        public const float DamageMult = 1.3f;
        private const float Cooldown = 20f;

        internal sealed class Stand
        {
            public Fighter F;
            public Actor A;
            public long Id;
            public Tier Tier;
            public Palette Pal;
            public double Since, NextCheck, LastFight;
            /// <summary>Until then only its death or the end of the fight ends it (the crowd may thin at once: it still burns a moment).</summary>
            public double Grace;
            public SitAura SitAura;
            public bool Lit;
            public float Ember, Breath;
        }

        private static readonly List<Stand> _stands = new List<Stand>();
        private static readonly Dictionary<long, double> _ready = new Dictionary<long, double>();
        private static readonly List<Actor> _near = new List<Actor>();

        public static int Count => _stands.Count;

        /// <summary>Is this unit making a last stand right now?</summary>
        public static bool In(Actor a)
        {
            if (a == null || _stands.Count == 0)
            {
                return false;
            }
            long id = a.Sid();
            for (int i = 0; i < _stands.Count; i++)
            {
                if (_stands[i].Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- start --

        /// <summary>A fighter looked around (Situations.Look): is this its last stand?</summary>
        public static void Consider(Fighter f, ref Situations.Survey s)
        {
            if (f.Tier == Tier.None || f.Info == null || s.Foes4 < 5 || s.Allies6 > 0)
            {
                return;
            }
            if (s.FoePower4 < 0.35f * f.Info.PR)
            {
                return;   // a God among peasants is annoyed, not desperate
            }
            if (_ready.TryGetValue(f.Id, out double at) && Fighter.Now < at)
            {
                return;
            }
            if (Start(f, s.FoeSum4 / Mathf.Max(1, s.Foes4)) && Dev.On)
            {
                Dev.Trace("situation last stand: " + f.A.asset.id + "#" + f.Id + " (" + f.Tier + ") vs " + s.Foes4 + " (power " + s.FoePower4.ToString("F1") + ")");
            }
        }

        /// <summary>
        /// Staging (Situations.Force): the unit (or the strongest of its side) makes its stand once
        /// three enemies are within 5 tiles of it (at the end of the wait, once any is within 6).
        /// </summary>
        internal static Situations.Stage Stage(Actor a, bool last, out string msg)
        {
            Actor s = Situations.Pick(a, Tier.Novice);
            if (s == null)
            {
                msg = "last stand: its side has no tiered unit";
                return Situations.Stage.Fail;
            }
            Situations.Want(s);
            if (In(s))
            {
                msg = "last stand: " + s.asset.id + "#" + s.Sid() + " is already making one";
                return Situations.Stage.Done;
            }
            int foes = 0;
            Vector2 sum = Vector2.zero, p = s.current_position;
            float nearest = float.MaxValue;
            Situations.Around(p, 6f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == s || !Combat.IsFoe(s, u) || !Situations.Threat(u))
                {
                    continue;
                }
                float d2 = (u.current_position - p).sqrMagnitude;
                nearest = Mathf.Min(nearest, d2);
                if (d2 <= 25f)
                {
                    foes++;
                    sum += u.current_position;
                }
            }
            _near.Clear();
            if (foes < 3 && !(last && nearest < 36f))
            {
                msg = "last stand: " + foes + " enemies within 5 tiles of " + s.asset.id + "#" + s.Sid() + ", waiting for 3";
                return Situations.Stage.Wait;
            }
            if (!Situations.CanAct(s))
            {
                msg = "last stand: " + s.asset.id + "#" + s.Sid() + " is busy";
                return Situations.Stage.Wait;
            }
            Vector2 toward = foes > 0 ? sum / foes : p + new Vector2(s.flip ? 1f : -1f, 0f);
            Situations.Show(p + new Vector2(0f, 1.5f));
            if (!Start(Fighters.Get(s), toward, true))
            {
                msg = "last stand: " + s.asset.id + "#" + s.Sid() + " can't perform now";
                return Situations.Stage.Wait;
            }
            msg = "last stand: " + s.asset.id + "#" + s.Sid() + " (" + Tiers.Of(s) + ") vs " + foes + " within 5 tiles";
            return Situations.Stage.Done;
        }

        /// <summary>Plants it: the stance move, then the burning (false if it can't perform now). 'staged': it burns at least 4 s.</summary>
        internal static bool Start(Fighter f, Vector2 toward, bool staged = false)
        {
            Actor a = f.A;
            double now = Fighter.Now;
            if (f.Tier == Tier.None || In(a) || !Situations.CanAct(a))
            {
                return false;
            }
            Stand st = new Stand
            {
                F = f,
                A = a,
                Id = f.Id,
                Tier = f.Tier,
                Pal = Palette.For(f.Tier, f.Tier >= Tier.Master ? 3 : 2),
                Since = now,
                NextCheck = now + 0.6,
                LastFight = now,
                Grace = now + (staged ? 4.0 : 1.5),
                Ember = Random.Range(0f, 0.1f)
            };
            if (!SitStandMove.Begin(a, st, toward))
            {
                return false;
            }
            _stands.Add(st);
            _ready[f.Id] = now + Cooldown;
            Situations.SetMult(a, DamageMult);
            Dev.Watch(a.current_position + new Vector2(0f, 1.5f));
            return true;
        }

        /// <summary>The stance's flare (called by its move): the light comes on.</summary>
        internal static void Ignite(Stand st, Vector2 feet, float px, float k)
        {
            if (st.Lit || !_stands.Contains(st))
            {
                return;
            }
            st.Lit = true;
            Actor a = st.A;
            Color main = Tiers.Color(st.Tier);
            Color light = Tiers.LightColor(st.Tier);
            if (Settings.Auras)
            {
                st.SitAura = new SitAura(a, Fx.WithA(main, 0.85f), Fx.WithA(light, 0.2f), true, true) { Flame = true };
            }
            Anim.Sound("event:/SFX/POWERS/Powerup", a.current_tile);
            if (Kit.Visible(feet))
            {
                // the flare: a burst of the tier's colour along the ground, a second ring behind it
                Fx.Ring(feet, px, 0.45f, 3f, 24f * k, 2, 1, FxLayer.Ground, 6, Art.Ground, 2.6f,
                    Fx.WithA(st.Pal.Core, 1f), Fx.WithA(main, 0.9f), Fx.WithA(main, 0f));
                Particle inner = Fx.Ring(feet, px, 0.36f, 2f, 14f * k, 1, 1, FxLayer.Ground, 6, Art.Ground, 2f,
                    Fx.WithA(light, 0.85f), Fx.WithA(light, 0f));
                if (inner != null)
                {
                    inner.Delay = 0.06f;
                }
                // embers burst upward from its feet
                int n = Fx.Count(10);
                for (int i = 0; i < n; i++)
                {
                    Vector2 at = feet + new Vector2(Random.Range(-5f, 5f), Random.Range(0f, 3f)) * px;
                    Kit.Ember(at, px, Random.Range(0.6f, 1.1f), new Vector2(Random.Range(-10f, 10f), Random.Range(22f, 42f)) * px * k, main);
                }
                // a Master's feet crack the ground; a Grandmaster's glow (and break it wide open)
                if (st.Tier >= Tier.Master)
                {
                    Kit.Crater(feet, px * k, a.current_tile, st.Tier >= Tier.Grandmaster, 4f, st.Tier < Tier.Grandmaster, main);
                }
            }
            if (st.Tier >= Tier.Master)
            {
                // the ground gives under its resolve, for real (on or off screen): a Master's feet
                // scorch it, a Grandmaster's blast it open, a God's half again as wide
                float r = (st.Tier >= Tier.God ? 3f : st.Tier >= Tier.Grandmaster ? 2f : 1f) * Mathf.Max(1f, k);
                Kit.Ravage(feet, r, st.Tier >= Tier.Grandmaster ? 2 : 1, a);
                Situations.Tip(Locale.Format("sakuga_sit_last_stand", Situations.Name(a)), TierHex(st.Tier), feet);
            }
        }

        internal static string TierHex(Tier t)
        {
            TierInfo info = Tiers.Info(t);
            return info != null ? info.Hex : "#FFE9B0";
        }

        // --------------------------------------------------------------- update --

        public static void Update(float dt, double now)
        {
            for (int i = _stands.Count - 1; i >= 0; i--)
            {
                if (i >= _stands.Count)
                {
                    continue;
                }
                Stand st = _stands[i];
                Actor a = st.A;
                if (a == null || a.Sid() != st.Id || !a.isAlive())
                {
                    End(st, Outcome.Fell);
                    continue;
                }
                // tempo: it thinks twice as often (the Brain counts down once, we count once more)
                st.F.ThinkIn -= dt;
                Situations.Boost(st.F, DamageMult - 1f, 0.6);
                if (a.isFighting())
                {
                    st.LastFight = now;
                }
                Burn(st, dt);
                if (now >= st.NextCheck)
                {
                    st.NextCheck = now + 0.45;
                    Check(st, now);
                }
            }
        }

        /// <summary>The aura breathes, the top of the outline flickers, embers rise.</summary>
        private static void Burn(Stand st, float dt)
        {
            if (!st.Lit)
            {
                return;
            }
            Color main = Tiers.Color(st.Tier);
            if (st.SitAura != null)
            {
                st.SitAura.Keep();
                st.Breath += dt;
                float pulse = 0.5f + 0.5f * Mathf.Sin(st.Breath * 6.5f);
                float flicker = Random.Range(0.85f, 1f);
                st.SitAura.Edge = Fx.WithA(Color.Lerp(main, st.Pal.Core, 0.2f * pulse), (0.6f + 0.25f * pulse) * flicker);
                st.SitAura.Tint = Fx.WithA(Tiers.LightColor(st.Tier), 0.08f + 0.12f * pulse);
            }
            st.Ember -= dt;
            if (st.Ember <= 0f)
            {
                st.Ember = Random.Range(0.12f, 0.22f);
                SitFx.Embers(st.A, 1, main);
            }
        }

        /// <summary>Is it still surrounded and alone?</summary>
        private static void Check(Stand st, double now)
        {
            Actor a = st.A;
            if (Tiers.Of(a) == Tier.None || a.isInMagnet() || a.isInsideSomething() || now - st.LastFight > 4.0)
            {
                End(st, Outcome.Faded);
                return;
            }
            if (now < st.Grace)
            {
                return;   // it has only just planted its feet: the fire burns a moment whatever happens around it
            }
            int foes = 0, allies = 0;
            Vector2 p = a.current_position;
            Situations.Around(p, 6.5f, _near);
            for (int i = 0; i < _near.Count; i++)
            {
                Actor u = _near[i];
                if (u == a)
                {
                    continue;
                }
                float d2 = (u.current_position - p).sqrMagnitude;
                if (Combat.IsFoe(a, u))
                {
                    if (d2 <= 4.5f * 4.5f && Situations.Threat(u))
                    {
                        foes++;
                    }
                }
                else if (d2 <= 36f && Combat.IsAlly(a, u) && !u.isBaby() && Situations.Afield(u))
                {
                    allies++;   // (one sheltering in a house nearby relieves no one)
                }
            }
            _near.Clear();
            if (allies > 0)
            {
                End(st, Outcome.Relieved);
            }
            else if (foes < 3)
            {
                End(st, Outcome.Held);
            }
        }

        private enum Outcome
        {
            Held,
            Relieved,
            Faded,
            Fell
        }

        private static void End(Stand st, Outcome how)
        {
            _stands.Remove(st);
            Situations.ClearMult(st.Id);
            _ready[st.Id] = Fighter.Now + Cooldown;
            if (Dev.On)
            {
                Dev.Trace("situation last stand ends: #" + st.Id + " " + how + " after " + (Fighter.Now - st.Since).ToString("F1") + " s");
            }
            if (how == Outcome.Fell)
            {
                st.SitAura?.End(0.05f);
                return;
            }
            Actor a = st.A;
            double held = Fighter.Now - st.Since;
            if (how == Outcome.Held || how == Outcome.Relieved)
            {
                // the fire flares once more and goes out; a breath of dust at its feet
                if (st.SitAura != null)
                {
                    st.SitAura.Edge = Fx.WithA(st.Pal.Core, 1f);
                    st.SitAura.Tint = Fx.WithA(Color.white, 0.35f);
                }
                Vector2 feet = SitFx.Feet(a);
                float px = SitFx.PxOf(a);
                if (Kit.Visible(feet) && a.is_visible)
                {
                    // the fire draws back into it (a ring closing on its feet), a breath of dust behind its legs
                    Fx.Ring(feet, px, 0.4f, 12f * SitFx.KOf(a), 3f, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.6f,
                        Fx.WithA(Tiers.Color(st.Tier), 0f), Fx.WithA(Tiers.Color(st.Tier), 0.7f), Fx.WithA(Tiers.LightColor(st.Tier), 0.9f));
                    Fx.LandingPuff(feet, px, a.current_tile);
                    SitFx.HeadGlint(a, st.Pal.Core, 0.24f, 1f, new Vector2(a.flip ? 3f : -3f, 1f));
                    SitFx.Embers(a, Fx.Count(4), Tiers.Color(st.Tier), 1.3f);
                }
                if (held >= 3.0)
                {
                    // surviving one's last stand is how legends begin
                    float xp = how == Outcome.Held ? 10f : 4f;
                    Progression.AddSxp(a, xp * Mathf.Max(1, (int)st.Tier));
                    if (how == Outcome.Held)
                    {
                        Progression.Trial(a, 2f, false);
                    }
                }
            }
            st.SitAura?.End(how == Outcome.Faded ? 0.5f : 0.45f);
        }

        /// <summary>A technique of a fighter in its last stand ended: half the usual pause before the next.</summary>
        public static void TechniqueEnded(Actor a)
        {
            if (_stands.Count == 0 || !In(a) || !Fighters.TryGet(a, out Fighter f) || f.Info == null)
            {
                return;
            }
            float gap = f.Info.MinGap / Mathf.Max(0.1f, Settings.Frequency);
            f.LastEnd = Math.Min(f.LastEnd, Fighter.Now - gap * 0.5f);
        }

        public static void Prune(double now)
        {
            List<long> drop = null;
            foreach (KeyValuePair<long, double> kv in _ready)
            {
                if (kv.Value < now)
                {
                    (drop ??= new List<long>()).Add(kv.Key);
                }
            }
            if (drop != null)
            {
                foreach (long id in drop)
                {
                    _ready.Remove(id);
                }
            }
        }

        public static void Clear()
        {
            foreach (Stand st in _stands)
            {
                st.SitAura?.End(0.01f);
            }
            _stands.Clear();
            _ready.Clear();
            _near.Clear();
        }
    }

    /// <summary>
    /// The last-stand stance: the moment it decides not to fall.
    ///
    /// Beat sheet (game seconds):
    ///
    ///   PLANT  0.10  feet set wide: squash, a lean back, dust kicked out from its heels, a 1-pixel
    ///                tremble as it braces; it turns to face the thick of them
    ///   FLARE  0.14  it rises tall (stretch): the ring of its tier colour bursts along the ground
    ///                (2.4 tiles) with a second ring behind it, embers burst upward, its silhouette
    ///                flashes white-hot then takes its colour; a Master cracks the ground under its
    ///                feet (a Grandmaster's cracks glow); the burning outline lights up
    ///   SET    0.16  it settles through a spring overshoot; a glint at its head ("ting")
    ///
    /// A Master or better plants and flares in a heartbeat (Situations.Pace: 0.3 s for a Master,
    /// under a quarter of a second for a God, the flare never under 0.12 s); the flare is also when
    /// its feet scorch (Master) or blast open (Grandmaster, God) the ground for real.
    ///
    /// Then the unit is free again, burning, and the Brain (thinking twice as fast, at once) takes over.
    /// </summary>
    internal sealed class SitStandMove : Move
    {
        private readonly float TPlant;
        private readonly float TFlare;
        private readonly float TSet;

        private readonly SitLastStand.Stand _st;
        private readonly Vector2 _at;
        private readonly float _k;
        private int _phase;
        private float _pt;
        private bool _entered;
        private Particle _glow;
        private Sprite[] _glowFrames;
        private Color _glowColor = SitFx.Clear;

        private SitStandMove(Actor a, SitLastStand.Stand st, Vector2 toward) : base(a)
        {
            _st = st;
            _at = a.current_position;
            _k = K;
            float pace = Situations.Pace(st.Tier);
            TPlant = 0.12f * pace;
            TFlare = Situations.Beat(0.2f, pace, 0.12f);
            TSet = 0.18f * pace;
            if (Mathf.Abs(toward.x - _at.x) > 0.2f)
            {
                Pose.FaceRight = toward.x > _at.x;
            }
        }

        public static bool Begin(Actor a, SitLastStand.Stand st, Vector2 toward)
        {
            if (!Moves.CanPerform(a))
            {
                return false;
            }
            return Moves.Start(new SitStandMove(a, st, toward));
        }

        private bool Visible => Fx.OnScreen(_at, 0.3f);

        private void Next()
        {
            _phase++;
            _pt = 0f;
            _entered = false;
        }

        private bool Enter()
        {
            if (_entered)
            {
                return false;
            }
            _entered = true;
            return true;
        }

        public override void Tick(float dt)
        {
            _pt += dt;
            float px = Px;
            Pose.Ground = _at;
            Pose.Height = 0f;
            switch (_phase)
            {
                case 0:
                    {
                        if (Enter() && Visible)
                        {
                            Kit.DustRing(_at, px, A.current_tile, 7, 30f, 0.45f, _k);
                            StartGlow();
                        }
                        float k = Anim.OutCubic(Mathf.Clamp01(_pt / TPlant));
                        Pose.SX = Mathf.Lerp(1f, 1.18f, k);
                        Pose.SY = Mathf.Lerp(1f, 0.8f, k);
                        Pose.Rot = -6f * k;
                        Pose.Nudge = _pt > 0.04f ? new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f) : Vector2.zero;
                        _glowColor = Fx.WithA(Tiers.LightColor(_st.Tier), 0.35f * k);
                        if (_pt >= TPlant)
                        {
                            Next();
                        }
                        break;
                    }
                case 1:
                    {
                        if (Enter())
                        {
                            Pose.Nudge = Vector2.zero;
                            SitLastStand.Ignite(_st, _at, px, _k);
                        }
                        float k = Anim.OutQuad(Mathf.Clamp01(_pt / TFlare));
                        Pose.SX = Mathf.Lerp(1.18f, 0.9f, k);
                        Pose.SY = Mathf.Lerp(0.8f, 1.14f, k);
                        Pose.Rot = Mathf.Lerp(-6f, 2f, k);
                        // white-hot for a beat, then its own colour
                        _glowColor = _pt < 0.05f ? Fx.WithA(Color.white, 0.9f) : Color.Lerp(Fx.WithA(Color.white, 0.9f), Fx.WithA(Tiers.Color(_st.Tier), 0.55f), Mathf.Clamp01((_pt - 0.05f) / Mathf.Min(0.09f, TFlare - 0.05f)));
                        if (_pt >= TFlare)
                        {
                            Next();
                        }
                        break;
                    }
                default:
                    {
                        if (Enter() && Visible)
                        {
                            Vector2 off = new Vector2((Pose.FaceRight ? 3f : -3f) * px, Pose.PivotUp * 1.5f * px);
                            // it outlives the stance: it rides on the unit, not on this pose
                            SitFx.Ride(Fx.Glint(BodyPos() + off, px, 0.24f, _st.Pal.Core, 1f), this, off);
                        }
                        float amp = Anim.Spring(_pt, 10f, 20f);
                        Pose.SX = 1f - 0.1f * amp;
                        Pose.SY = 1f + 0.14f * amp;
                        Pose.Rot = 2f * amp;
                        _glowColor = Fx.WithA(Tiers.Color(_st.Tier), Mathf.Lerp(0.55f, 0f, Mathf.Clamp01(_pt / TSet)));
                        if (_pt >= TSet)
                        {
                            Pose.SX = Pose.SY = 1f;
                            Pose.Rot = 0f;
                            KillGlow();
                            Finished = true;
                        }
                        break;
                    }
            }
        }

        private void StartGlow()
        {
            if (_glow != null || !Fx.HasSilhouette)
            {
                return;
            }
            Sprite s = Fx.UnitSprite(A);
            _glow = s != null ? Fx.Spawn(s, DrawPos(), 1f, 1f, FxLayer.Over, 3, true) : null;
            if (_glow != null)
            {
                _glowFrames = _glow.Frames;
                _glow.Held = true;
                _glow.UseEuler = true;
                Fx.Glue(_glow, this, SitFx.Clear);
            }
        }

        public override void Frame()
        {
            if (SitFx.Mine(_glow, _glowFrames))
            {
                Fx.Glue(_glow, this, _glowColor);
            }
        }

        private void KillGlow()
        {
            if (SitFx.Mine(_glow, _glowFrames))
            {
                _glow.Kill();
            }
            _glow = null;
        }

        public override void Abort()
        {
            KillGlow();
        }

        public override void OnEnd(bool aborted)
        {
            KillGlow();
            if (!aborted)
            {
                Situations.Resume(A);   // burning, and straight at them
            }
        }
    }
}
