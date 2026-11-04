using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// BODYGUARD. A single-target technique is about to reach someone (Combat.Reach, never an area
    /// blow): if a free tiered ally of the victim stands within 2.5 tiles - an Expert or better, no
    /// weaker than the attacker's tier minus one and no weaker than the one it protects, allowed to
    /// fight the attacker's class - it may blink in front of the victim and take the blow on crossed
    /// arms: the technique is Parried (the attacker's move breaks, the attacker staggers back). The
    /// chance grows with the guard's reading of a fight (50% + half its tier's reaction). One guard
    /// per blow, each guard once every 12 s. The victim's own answers (its parries, a duel's
    /// deflection) come first: this handler is registered last. A master's plain strike chain (a
    /// flow strike reaching its victim) is guarded only when the ally is in real danger (40% health
    /// or less), at half the chance. A guard between flow strikes
    /// of its own is free: the guard's blink takes over from them.
    /// </summary>
    internal static class SitBodyguard
    {
        public const float Range = 2.5f;
        /// <summary>While staged (Armed): a guard this far from the one it protects still makes it.</summary>
        public const float ArmedRange = 4f;
        private const float Cooldown = 12f;

        private static readonly Dictionary<long, double> _ready = new Dictionary<long, double>();

        /// <summary>Guards (and parries) since the game started (dev log).</summary>
        internal static int Guarded;

        /// <summary>Staging (Situations.Force): until this world time, a guard in reach always steps in, cooldowns aside.</summary>
        internal static double Armed = -1;

        /// <summary>The contact handler (Combat.ContactHandlers).</summary>
        public static Contact Answer(Move m, Actor v, bool area)
        {
            if (area || !Situations.Enabled || m == null || v == null || Situations.Guards.Count == 0)
            {
                return Contact.Hit;
            }
            Actor atk = m.A;
            // (a victim another blow already killed this frame is past guarding: Combat.Living; a
            // guard that was is ruled out by CanAct)
            if (atk == null || !atk.isAlive() || atk == v || !Combat.Living(v) || Director.InScene(v))
            {
                return Contact.Hit;
            }
            if (Moves.TryGet(v, out Move vm) && vm is Puppet)
            {
                return Contact.Hit;   // already in someone's hands
            }
            if (Duels.InDuel(v) && Duels.Opponent(v) == atk)
            {
                return Contact.Hit;   // a duel is between the two of them: nobody steps in front of its blows
            }
            Tier ta = Tiers.Of(atk);
            Tier tv = Tiers.Of(v);
            double now = Fighter.Now;
            bool armed = now < Armed;
            // a master's plain strike chain (a flow move: no technique) reaches someone many times a
            // fight: a guard steps in front of one only when the ally is in real danger (40% health
            // or less), and half as readily, so guarding stays a moment and not a habit (and a
            // master cutting through a mob isn't parried by every Expert in it)
            bool plain = m.Tech == null;
            if (plain && !armed && Combat.HpPct(v) > 0.4f)
            {
                return Contact.Hit;
            }
            Vector2 vp = v.current_position;
            Fighter best = null;
            float bestD = float.MaxValue;
            List<Fighter> guards = Situations.Guards;
            for (int i = 0; i < guards.Count; i++)
            {
                Fighter g = guards[i];
                Actor ga = g.A;
                if (!g.Valid || ga == v || ga == atk || Duels.InDuel(ga))
                {
                    continue;   // (a duelist keeps to its own fight)
                }
                if (g.Tier < Tier.Expert || (int)g.Tier < (int)ta - 1 || g.Tier < tv)
                {
                    continue;
                }
                float range = (armed ? ArmedRange : Range) * Mathf.Max(1f, SitFx.KOf(ga));
                float d2 = (ga.current_position - vp).sqrMagnitude;
                if (d2 > range * range || d2 >= bestD)
                {
                    continue;
                }
                if (_ready.TryGetValue(g.Id, out double at) && now < at && !armed)
                {
                    continue;
                }
                if (!Combat.IsAlly(v, ga) || !Combat.IsFoe(ga, atk) || !Combat.Allowed(ga, atk))
                {
                    continue;
                }
                if (!Situations.CanAct(ga) || ga.hasStatusStunned())
                {
                    continue;
                }
                best = g;
                bestD = d2;
            }
            if (best == null)
            {
                return Contact.Hit;
            }
            float chance = armed ? 1f : (0.5f + 0.5f * (best.Info != null ? best.Info.React : 0.25f)) * (plain ? 0.5f : 1f);
            if (Random.value > chance)
            {
                _ready[best.Id] = now + 2.0;   // it hesitated: not again on the very next blow
                return Contact.Hit;
            }
            if (!Spot(best.A, v, atk, out Vector2 spot))
            {
                return Contact.Hit;
            }
            if (armed)
            {
                Situations.Show(spot + new Vector2(0f, 1.2f));   // staged: the camera looks before it blinks in
            }
            if (!SitGuardMove.Begin(best.A, atk, spot, best.Tier, ta))
            {
                return Contact.Hit;
            }
            _ready[best.Id] = now + Cooldown;
            Armed = -1;
            Guarded++;
            Dev.Watch(spot + new Vector2(0f, 1.2f));
            Progression.AddSxp(best.A, 3f);
            if (Dev.On)
            {
                Dev.Trace("situation bodyguard: " + best.A.asset.id + "#" + best.Id + " (" + best.Tier + ") guards " + v.asset.id + "#" + v.Sid()
                    + " from " + atk.asset.id + "'s " + (m.Tech != null ? m.Tech.Id : m.GetType().Name));
            }
            return Contact.Parried;
        }

        /// <summary>Where to take the blow: on the line from the victim to its attacker, close to the victim.</summary>
        private static bool Spot(Actor g, Actor v, Actor atk, out Vector2 spot)
        {
            Vector2 vp = v.current_position;
            Vector2 to = atk.current_position - vp;
            float d = to.magnitude;
            Vector2 dir = d > 0.01f ? to / d : new Vector2(v.flip ? 1f : -1f, 0f);
            float gap = Mathf.Clamp(0.5f * SitFx.KOf(v), 0.4f, 1f);
            spot = d < gap * 2.2f ? Vector2.Lerp(vp, atk.current_position, 0.45f) : vp + dir * gap;
            spot = Combat.Clamp(spot);
            // an Expert burns: it doesn't blink into fire (Masters and up don't burn)
            bool fireproof = Tiers.Of(g) >= Tier.Master;
            if (Combat.SafeSpot(spot) && (fireproof || !Situations.Burning(spot)) && Combat.ClearPath(g.current_position, spot))
            {
                return true;
            }
            // a step to either side of the line
            Vector2 side = new Vector2(-dir.y, dir.x) * 0.35f;
            foreach (Vector2 alt in new[] { spot + side, spot - side })
            {
                if (Combat.SafeSpot(alt) && (fireproof || !Situations.Burning(alt)) && Combat.ClearPath(g.current_position, alt))
                {
                    spot = alt;
                    return true;
                }
            }
            return false;
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
            _ready.Clear();
            Armed = -1;
        }
    }

    /// <summary>
    /// The guard: there, between them, arms crossed.
    ///
    /// Beat sheet (game seconds):
    ///
    ///   BLINK  0.05  gone from where it stood: a streak of afterimages and speed lines to the spot
    ///                in front of its ally, stretched along the way it flies, leaning in
    ///   BRACE  0.14  arms crossed before its chest (an X of light), the blow lands on the guard:
    ///                "ting!", sparks thrown back at the attacker, a ring of its tier colour around
    ///                the point of contact, its silhouette flashes white; it trembles with the force
    ///   SKID   0.20  the force shoves it back on its heels, dust trailing from its feet, still
    ///                leaning back into it
    ///   SET    0.16  springs back upright; a small glint over its head
    ///
    /// How hard the blow was for it shows: a guard two tiers above the attacker doesn't budge (no
    /// tremble, a slight lean, no skid: it just stands there); one tier above skids 0.2 tiles; an
    /// equal 0.35 with a tremble; a weaker one is driven back half a tile. It always ends where it
    /// can stand (the skid stops short of a bad tile).
    ///
    /// A Master or better does it all in about a third of a second (Situations.Pace: the brace
    /// never under 0.1 s) and never ends in a guard: it turns on the attacker and, if the attacker
    /// is still in reach on the ground, a chain window (Close) opens onto it, so the parry flows
    /// straight into its counter technique. Below that, it turns on the attacker and its Brain looks
    /// at once. No ground damage: a guard is a parry.
    /// </summary>
    internal sealed class SitGuardMove : Move
    {
        /// <summary>A guard catching the blow meant for another: readers see a Guard (grab it, kick it).</summary>
        public override Form Forms => Form.Guard | Form.Counter | Form.Body;

        private const float TBlink = 0.05f;
        private readonly float TBrace;
        private readonly float TSkid;
        private readonly float TSet;

        private readonly Vector2 _from, _spot, _back, _dir;
        private readonly float _k, _fwd;
        private readonly Palette _pal;
        /// <summary>Unmoved: far stronger than the attacker. Strain: 0 (easy) .. 1 (driven back).</summary>
        private readonly bool _calm;
        private readonly float _strain;
        /// <summary>The one whose blow it took (it turns on it), and whether it fights in one flow (Master+).</summary>
        private readonly Actor _atk;
        private readonly long _atkId;
        private readonly bool _flow;
        private int _phase;
        private float _pt, _dust;
        private bool _entered;
        private Particle _glow;
        private Sprite[] _glowFrames;
        private Color _glowColor = SitFx.Clear;

        private SitGuardMove(Actor g, Actor atk, Vector2 spot, Tier tier, Tier attackerTier) : base(g)
        {
            int gap = (int)tier - (int)attackerTier;
            _calm = gap >= 2;
            _strain = gap >= 2 ? 0f : gap == 1 ? 0.35f : gap == 0 ? 0.7f : 1f;
            float skid = gap >= 2 ? 0f : gap == 1 ? 0.2f : gap == 0 ? 0.35f : 0.5f;
            _atk = atk;
            _atkId = atk.Sid();
            _flow = tier >= Tier.Master;
            float pace = Situations.Pace(tier);
            TBrace = Situations.Beat(0.14f, pace, 0.1f);
            TSkid = 0.2f * pace;
            TSet = 0.16f * pace * (_flow ? 0.7f : 1f);
            _from = g.current_position;
            _spot = spot;
            _k = K;
            Vector2 to = atk.current_position - spot;
            _dir = to.sqrMagnitude > 0.0001f ? to.normalized : new Vector2(g.flip ? 1f : -1f, 0f);
            _fwd = Mathf.Abs(_dir.x) > 0.05f ? Mathf.Sign(_dir.x) : (g.flip ? 1f : -1f);
            Pose.FaceRight = _fwd > 0f;
            _pal = Palette.For(tier, tier >= Tier.Master ? 3 : 2);
            Vector2 back = Combat.Clamp(spot - _dir * skid * _k);
            _back = skid > 0f && Combat.SafeSpot(back) && (tier >= Tier.Master || !Situations.Burning(back)) && Combat.ClearPath(spot, back) ? back : spot;
        }

        public static bool Begin(Actor g, Actor atk, Vector2 spot, Tier tier, Tier attackerTier)
        {
            if (!Moves.CanPerform(g))
            {
                return false;
            }
            return Moves.Start(new SitGuardMove(g, atk, spot, tier, attackerTier));
        }

        private bool Visible => Fx.OnScreen(_spot, 0.3f);

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
            switch (_phase)
            {
                case 0:
                    Blink(dt);
                    break;
                case 1:
                    Brace(dt);
                    break;
                case 2:
                    Skid(dt);
                    break;
                default:
                    Set(dt);
                    break;
            }
        }

        // -------------------------------------------------------------- blink --

        private void Blink(float dt)
        {
            float px = Px;
            Vector2 path = _spot - _from;
            if (Enter())
            {
                Anim.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Visible && path.sqrMagnitude > 0.04f)
                {
                    Fx.LandingPuff(_from, px, A.current_tile);
                    // the streak: where it was, a fading trail of itself along the way
                    Pose.SX = 1.3f;
                    Pose.SY = 0.78f;
                    Pose.Rot = 16f;
                    for (int i = 0; i < 4; i++)
                    {
                        Pose.Ground = Vector2.Lerp(_from, _spot, i / 4f);
                        float a = 0.25f + 0.15f * i;
                        Fx.Afterimage(this, 0.12f + 0.03f * i, new Color(1f, 1f, 1f, a), Fx.WithA(_pal.Edge, 0f));
                    }
                    Kit.SpeedLines(Vector2.Lerp(_from, _spot, 0.5f) + new Vector2(0f, Pose.PivotUp * px), path, px, 4, 4f, 0.14f, new Color(1f, 1f, 1f, 0.7f));
                }
                StartGlow();
            }
            float k = Anim.OutCubic(Mathf.Clamp01(_pt / TBlink));
            Pose.Ground = Vector2.Lerp(_from, _spot, k);
            Pose.Height = 0f;
            Pose.SX = 1.3f;
            Pose.SY = 0.78f;
            Pose.Rot = 16f;
            if (Visible && path.sqrMagnitude > 0.04f)
            {
                Fx.Afterimage(this, 0.1f, new Color(1f, 1f, 1f, 0.7f), Fx.WithA(_pal.Edge, 0f));
            }
            if (_pt >= TBlink)
            {
                Next();
            }
        }

        // -------------------------------------------------------------- brace --

        private void Brace(float dt)
        {
            float px = Px;
            if (Enter())
            {
                Pose.Ground = _spot;
                Anim.Sound("event:/SFX/HIT/HitMetal", A.current_tile);
                if (Visible)
                {
                    Vector2 chest = BodyPos() + new Vector2(_fwd * 2f * px, 0f);
                    Vector2 contact = chest + _dir * 2f * px;
                    // the "ting!"
                    Fx.Glint(contact, px, 0.22f, Color.white, 1.25f);
                    Fx.HitSpark(contact, px, 1, _dir, Fx.Count(6));
                    Fx.Ring(contact, px, 0.18f, 2f, 11f * _k, 1, 1, FxLayer.Top, 3, 1f, 2.5f,
                        Fx.WithA(_pal.Core, 0.95f), Fx.WithA(_pal.Edge, 0.6f), Fx.WithA(_pal.Edge, 0f));
                    // the crossed arms: an X of light before its chest
                    float arm = 3f * px * _k;
                    Kit.Line(chest + new Vector2(-arm, -arm), chest + new Vector2(arm, arm), px, 0.16f, 1, FxLayer.Over, 6, Color.white, _pal.Edge, Fx.WithA(_pal.Edge, 0f));
                    Kit.Line(chest + new Vector2(-arm, arm), chest + new Vector2(arm, -arm), px, 0.16f, 1, FxLayer.Over, 6, Color.white, _pal.Edge, Fx.WithA(_pal.Edge, 0f));
                }
            }
            float k = Anim.OutQuad(Mathf.Clamp01(_pt / 0.05f));
            Pose.Ground = _spot;
            Pose.SX = Mathf.Lerp(1.3f, 1f + 0.12f * Mathf.Max(0.3f, _strain), k);
            Pose.SY = Mathf.Lerp(0.78f, 1f - 0.1f * Mathf.Max(0.3f, _strain), k);
            Pose.Rot = Mathf.Lerp(16f, -4f - 10f * _strain, k);
            // the blow shakes it (not one that is far above the attacker)
            Pose.Nudge = _calm ? Vector2.zero : new Vector2(((int)(_pt * 60f) & 1) == 0 ? 0.5f : -0.5f, 0f);
            _glowColor = _pt < 0.05f ? Fx.WithA(Color.white, 0.9f) : Fx.WithA(_pal.Edge, Mathf.Lerp(0.6f, 0.2f, Mathf.Clamp01((_pt - 0.05f) / Mathf.Max(0.04f, TBrace - 0.05f))));
            if (_pt >= TBrace)
            {
                Pose.Nudge = Vector2.zero;
                Next();
            }
        }

        // --------------------------------------------------------------- skid --

        private void Skid(float dt)
        {
            float px = Px;
            float k = Anim.OutQuad(Mathf.Clamp01(_pt / TSkid));
            float lean = -4f - 10f * _strain;
            Pose.Ground = Vector2.Lerp(_spot, _back, k);
            Pose.SX = Mathf.Lerp(1f + 0.12f * Mathf.Max(0.3f, _strain), 1.03f, k);
            Pose.SY = Mathf.Lerp(1f - 0.1f * Mathf.Max(0.3f, _strain), 0.98f, k);
            Pose.Rot = Mathf.Lerp(lean, lean * 0.4f, k);
            _glowColor = Fx.WithA(_pal.Edge, Mathf.Lerp(0.2f, 0f, k));
            _dust -= dt;
            if (_dust <= 0f && k < 0.85f && Visible && (_back - _spot).sqrMagnitude > 0.001f)
            {
                _dust = 0.045f;
                Vector2 heel = Pose.Ground - _dir * 1.5f * px;
                Particle p = Fx.Spawn(Art.Dust[Random.Range(0, 3)], heel, px, Random.Range(0.3f, 0.45f), FxLayer.Ground, 7);
                Color dust = Fx.Lighten(Fx.SoilColor(A.current_tile), 0.35f);
                p?.Moving(-_dir * Random.Range(6f, 12f) * px, Random.Range(2f, 5f) * px, 4f).Sized(0.5f, 1f)
                    .Color(Fx.WithA(dust, 0.75f), Fx.WithA(dust, 0.4f), Fx.WithA(dust, 0f));
            }
            if (_pt >= TSkid)
            {
                Next();
            }
        }

        // ---------------------------------------------------------------- set --

        private void Set(float dt)
        {
            float px = Px;
            if (Enter())
            {
                KillGlow();
                if (Visible)
                {
                    // it outlives the guard (a Master's counter starts at once): it rides on the unit
                    Vector2 off = new Vector2(-_fwd * 3f * px, Pose.PivotUp * 1.5f * px);
                    SitFx.Ride(Fx.Glint(BodyPos() + off, px, 0.2f, _pal.Core, 0.9f), this, off);
                }
            }
            float amp = Anim.Spring(_pt, 10f, 19f);
            Pose.Ground = _back;
            Pose.SX = 1f + 0.04f * amp;
            Pose.SY = 1f - 0.03f * amp;
            Pose.Rot = (-1.6f - 4f * _strain) * amp;
            if (_pt >= TSet)
            {
                Pose.SX = Pose.SY = 1f;
                Pose.Rot = 0f;
                if (!_flow)
                {
                    A.startAttackCooldown();   // a Master+ never waits on a swing: it counters
                }
                Finished = true;
            }
        }

        // ---------------------------------------------------------------- glow --

        private void StartGlow()
        {
            if (_glow != null || !Fx.HasSilhouette || !Visible)
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
            if (aborted)
            {
                return;
            }
            // it turns on the one whose blow it took (unless it is already on another tiered foe)
            Actor atk = _atk != null && _atk.Sid() == _atkId && _atk.isAlive() ? _atk : null;
            if (atk != null)
            {
                Situations.Steer(A, atk, true);
            }
            Situations.Resume(A);
            if (_flow && atk != null && Combat.Grounded(atk) && Combat.IsFoe(A, atk) && Combat.Allowed(A, atk))
            {
                float reach = 2.2f * Mathf.Max(1f, Mathf.Max(_k, SitFx.KOf(atk)));
                if ((atk.current_position - A.current_position).sqrMagnitude <= reach * reach)
                {
                    Situations.Open(A, Link.Close, atk);   // the parry flows into the counter
                }
            }
        }
    }
}
