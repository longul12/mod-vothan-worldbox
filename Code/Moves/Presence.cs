using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// What a strong fighter is, before it does anything.
    ///
    /// OVERWHELM (a master easily beats a novice). An ordinary blow (a swing) on a tiered fighter
    /// three times the attacker's power or more may be turned aside with one hand: 35%, +10% for
    /// every further doubling (at most 85%). A ting at the contact, the attacker flicked 1-1.5 tiles,
    /// a tiny glint on the defender (projectiles: KiDeflect turns them back). A technique
    /// reaching a fighter four times its user's power is parried half the time (the technique
    /// breaks), with the same ting.
    ///
    /// PRESENCE (setting "Tier auras"): entering a fight, Adepts and up flare a ring of their tier
    /// colour at their feet (not while a move holds them); Masters and up glint above the head
    /// every 4 s while fighting (never over a move: its own beats come first); Grandmasters and
    /// Gods radiate PRESSURE: every 3 s a 1-pixel ring of their colour ripples out 3 tiles, and
    /// enemies inside it may falter (frozen 0.6 s, shaking, a bead of sweat): untiered and Novices
    /// 20%, Adepts 10%, Experts and up are immune. Mid-exchange of a duel the pressure waits (the
    /// exchange's rings are the beats there).
    ///
    /// SECOND WIND (Experts and up, at most once per battle). Below 20% health: a flare in its
    /// colour, pulse rings, light rising, embers; every cooldown resets and it hits 20% harder for
    /// 8 s; Masters and up also heal 10%. Always in a duel or against a worthy foe, otherwise only
    /// sometimes (30% Experts ... 85% Gods); never mid-combo while another's move holds it. In a
    /// duel this is the comeback: the Final Exchange waits until the Second Wind is spent.
    /// </summary>
    internal static class Presence
    {
        private sealed class State
        {
            public Fighter F;
            public double Since = -2;
            /// <summary>World time its Second Wind last rose.</summary>
            public double WindAt = -999;
            public float Glint, Pulse;
        }

        /// <summary>World seconds since this fighter's Second Wind rose (a large number if it never did).</summary>
        internal static float SinceWind(Actor a)
        {
            if (a != null && _state.TryGetValue(a.Sid(), out State s) && s.F.A == a)
            {
                return (float)(Fighter.Now - s.WindAt);
            }
            return 999f;
        }

        private static readonly Dictionary<long, State> _state = new Dictionary<long, State>();
        private static float _prune;
        private static int _frame = -1;
        private static float _lastTing;

        [SakugaInit]
        private static void Init()
        {
            Hooks.FighterTick += Tick;
            Hooks.BeforeOrdinaryHit.Add(Deflect);
            Combat.ContactHandlers.Add(Parry);
            Hooks.WorldCleared += () => _state.Clear();
        }

        private static State Of(Fighter f)
        {
            if (_state.TryGetValue(f.Id, out State s) && s.F == f)
            {
                return s;
            }
            s = new State { F = f, Glint = Random.Range(0.5f, 2f), Pulse = Random.Range(0.4f, 1.6f) };
            _state[f.Id] = s;
            return s;
        }

        // -------------------------------------------------------------- tick --

        private static void Tick(Fighter f, float dt)
        {
            try
            {
                if (Time.frameCount != _frame)
                {
                    _frame = Time.frameCount;
                    Prune(dt);
                }
                if (f.Tier == Tier.None)
                {
                    return;
                }
                SecondWind(f);
                if (!Settings.Auras || !f.InCombat || !f.A.isAlive())
                {
                    return;
                }
                State s = Of(f);
                if (f.CombatSince >= 0 && s.Since != f.CombatSince)
                {
                    // a new fight: the flare
                    s.Since = f.CombatSince;
                    if (f.Tier >= Tier.Adept && !Moves.Busy(f.A))
                    {
                        Flare(f);
                    }
                }
                if (f.Tier >= Tier.Master)
                {
                    s.Glint -= dt;
                    if (s.Glint <= 0f && Moves.TryGet(f.A, out Move held))
                    {
                        s.Glint = 1f;   // mid-exchange the duel's own glints are the beats: this one waits
                    }
                    else if (s.Glint <= 0f)
                    {
                        s.Glint = 4f;
                        Kit.Mark(f.A, Tiers.LightColor(f.Tier), 0.3f, 0.9f);
                    }
                }
                if (f.Tier >= Tier.Grandmaster)
                {
                    s.Pulse -= dt;
                    if (s.Pulse <= 0f && Moves.TryGet(f.A, out Move cur) && cur is Duelist)
                    {
                        s.Pulse = 0.5f;   // mid-exchange the duel's own rings are the beats: the pressure waits
                    }
                    else if (s.Pulse <= 0f)
                    {
                        s.Pulse = 3f;
                        Pressure(f);
                    }
                }
            }
            catch (Exception e)
            {
                SakugaMod.Report("presence", e);
            }
        }

        private static void Prune(float dt)
        {
            _prune -= dt;
            if (_prune > 0f)
            {
                return;
            }
            _prune = 10f;
            List<long> drop = null;
            foreach (KeyValuePair<long, State> kv in _state)
            {
                if (!kv.Value.F.Valid)
                {
                    (drop ??= new List<long>()).Add(kv.Key);
                }
            }
            if (drop != null)
            {
                foreach (long id in drop)
                {
                    _state.Remove(id);
                }
            }
        }

        private static float Px(Actor a)
        {
            return Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
        }

        private static float K(Actor a)
        {
            return Mathf.Sqrt(Mathf.Clamp(a.actor_scale / 0.1f, 0.6f, 4f));
        }

        // ---------------------------------------------------------- presence --

        /// <summary>A ring of the tier's colour flaring at the feet as the fight starts.</summary>
        private static void Flare(Fighter f)
        {
            Actor a = f.A;
            Vector2 at = a.current_position;
            if (!Kit.Visible(at))
            {
                return;
            }
            float px = Px(a), k = K(a);
            Color c = Tiers.Color(f.Tier);
            Kit.Flare(at, px, c, (10f + 2f * (int)f.Tier) * k, 0.35f + 0.03f * (int)f.Tier);
            if (f.Tier >= Tier.Expert)
            {
                // a second, finer ring behind it
                Particle p = Fx.Ring(at, px, 0.4f, 2f, (7f + 2f * (int)f.Tier) * k, 1, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                    Fx.WithA(Tiers.LightColor(f.Tier), 0.9f), Fx.WithA(c, 0f));
                if (p != null)
                {
                    p.Delay = 0.08f;
                }
            }
        }

        /// <summary>
        /// The pressure of a Grandmaster or a God: a fine ring ripples out 3 tiles, and the weak
        /// inside it falter.
        /// </summary>
        private static void Pressure(Fighter f)
        {
            Actor a = f.A;
            Vector2 at = a.current_position;
            float px = Px(a), k = K(a);
            float radius = 3f * k;
            if (Kit.Visible(at))
            {
                Color c = Tiers.Color(f.Tier);
                Fx.Ring(at, px, 0.7f, 4f, radius / px, 1, 1, FxLayer.Ground, 5, Art.Ground, 1.5f,
                    Fx.WithA(Tiers.LightColor(f.Tier), 0.85f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
            }
            foreach (Actor u in Combat.FoesAround(a, at, radius))
            {
                Tier t = Tiers.Of(u);
                float chance = t == Tier.None || t == Tier.Novice ? 0.2f : t == Tier.Adept ? 0.1f : 0f;
                if (chance <= 0f || Weights.Heavy(u) || !Combat.Allowed(a, u) || Random.value >= chance)
                {
                    continue;
                }
                // it falters: frozen, shaking, a bead of sweat (Daze: no white flash, nothing struck it)
                Combat.Daze(u, 0.6f);
                u.startShake(0.4f, 0.1f, true, false);
                Sweat(u);
            }
        }

        private static void Sweat(Actor u)
        {
            if (!u.is_visible)
            {
                return;
            }
            float px = Px(u);
            Vector2 head = u.current_position + new Vector2(2f * px * (u.flip ? -1f : 1f), u.position_height + 8f * px);
            Particle p = Fx.Spawn(Art.Pixel, head, px, 0.45f, FxLayer.Top, 9);
            p?.Moving(new Vector2(0f, -6f * px), 0f, 0f, 0f).Sized(1f, 1f).Color(DuelFx.Sweat, DuelFx.Sweat, Fx.WithA(DuelFx.Sweat, 0f));
            Particle q = Fx.Spawn(Art.Pixel, head + new Vector2(0f, 1f * px), px, 0.4f, FxLayer.Top, 9);
            q?.Moving(new Vector2(0f, -6f * px), 0f, 0f, 0f).Color(Color.white, Fx.WithA(DuelFx.Sweat, 0.8f), Fx.WithA(DuelFx.Sweat, 0f));
        }

        // ------------------------------------------------------- second wind --

        /// <summary>
        /// Once per battle, below 20% health, an Expert or better may get its second wind: always in a
        /// duel or against a worthy foe (that is the comeback the story needs), otherwise only
        /// sometimes (more often the higher the tier), so a battle isn't strobing with power-ups.
        /// Never while another's move holds it (mid-flurry, mid-juggle): it comes when it is free
        /// again, if it lives through that.
        /// </summary>
        private static void SecondWind(Fighter f)
        {
            if (f.Tier < Tier.Expert || f.SecondWindUsed || f.CombatSince < 0)
            {
                return;
            }
            Actor a = f.A;
            if (!a.isAlive() || !a.hasHealth() || Combat.HpPct(a) >= 0.2f)
            {
                return;
            }
            if (Moves.TryGet(a, out Move held) && held is Puppet)
            {
                return;
            }
            f.SecondWindUsed = true;
            if (!WorthyFight(a, f.Tier))
            {
                float chance = f.Tier >= Tier.God ? 0.85f : f.Tier == Tier.Grandmaster ? 0.6f : f.Tier == Tier.Master ? 0.45f : 0.3f;
                if (Random.value >= chance)
                {
                    if (Dev.On)
                    {
                        Dev.Trace("second wind: none for " + a.asset.id + "#" + a.Sid() + " (" + f.Tier + ") this battle");
                    }
                    return;
                }
            }
            Of(f).WindAt = Fighter.Now;
            f.ResetCooldowns();
            Situations.Boost(f, 0.2f, 8.0);
            if (f.Tier >= Tier.Master)
            {
                a.restoreHealthPercent(0.1f);
            }
            Anim.Sound("event:/SFX/POWERS/Powerup", a.current_tile);
            WindFx(f);
            if (Dev.On)
            {
                Dev.Trace("second wind: " + a.asset.id + "#" + a.Sid() + " (" + f.Tier + ") hp " + a.getHealth());
            }
        }

        /// <summary>In a duel, or fighting a tiered enemy at least half its own power.</summary>
        private static bool WorthyFight(Actor a, Tier t)
        {
            if (Duels.InDuel(a))
            {
                return true;
            }
            if (!a.has_attack_target || a.attack_target == null || !a.attack_target.isActor())
            {
                return false;
            }
            Actor b = a.attack_target.a;
            return b != null && b.isAlive() && Tiers.Of(b) != Tier.None && Tiers.Power(b) >= 0.5f * Tiers.Info(t).PR;
        }

        private static void WindFx(Fighter f)
        {
            Actor a = f.A;
            Vector2 feet = a.current_position;
            float h = a.position_height;
            if (Moves.TryGet(a, out Move m))
            {
                feet = m.Pose.Ground;
                h = m.Pose.Height;
            }
            if (!Kit.Visible(feet))
            {
                return;
            }
            float px = Px(a), k = K(a);
            Color c = Tiers.Color(f.Tier), light = Tiers.LightColor(f.Tier);
            Vector2 body = feet + new Vector2(0f, h + 4f * px * k);
            Kit.Flare(feet, px, c, 18f * k, 0.45f);
            for (int i = 0; i < 2; i++)
            {
                Particle p = Fx.Ring(feet, px, 0.5f, 3f, (14f + 6f * i) * k, 2, 1, FxLayer.Ground, 5, Art.Ground, 2f,
                    Fx.WithA(light, 0.95f), Fx.WithA(c, 0.6f), Fx.WithA(c, 0f));
                if (p != null)
                {
                    p.Delay = 0.12f * i;
                }
            }
            Kit.Flash(body, px, 6, 0.08f, Fx.WithA(light, 0.9f), FxLayer.Top);
            DuelFx.Rising(body, px, 8, 4f * k, light);
            DuelFx.Embers(body, px, 10, 4f * k, c);
            Fx.Glint(body + new Vector2(3f * px, 5f * px * k), px, 0.3f, Color.white, 1.1f);
        }

        // ---------------------------------------------------------- overwhelm --

        /// <summary>
        /// An ordinary blow on a far stronger tiered fighter: turned aside with one hand (Hooks.BeforeOrdinaryHit).
        /// </summary>
        private static bool Deflect(AttackData d, Actor t)
        {
            // (a projectile is KiDeflect's: it turns shots aside by tier and sends them back)
            if (d.is_projectile || t == null || !t.isAlive() || d.initiator == null || !d.initiator.isActor())
            {
                return false;
            }
            Tier tt = Tiers.Of(t);
            if (tt == Tier.None || t.is_unconscious || t.hasStatusStunned() || Moves.Intangible(t)
                || Moves.TryGet(t, out Move held) && held is Puppet)
            {
                return false;   // (in someone's hands: it can't move a hand, as KiDeflect.OnHit)
            }
            Actor atk = d.initiator.a;
            if (atk == null || atk == t || !atk.isAlive())
            {
                return false;
            }
            float pt = Tiers.Power(t), pa = Tiers.Power(atk);
            if (pt < 3f * pa)
            {
                return false;
            }
            // 35% at three times, +10% per further doubling
            float chance = Mathf.Clamp(0.35f + 0.1f * Mathf.Log(pt / (3f * pa), 2f), 0f, 0.85f);
            if (Random.value >= chance)
            {
                return false;
            }
            Vector2 tp = t.current_position, ap = d.is_projectile ? (Vector2)d.initiator_position : atk.current_position;
            Vector2 dir = ap - tp;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : new Vector2(t.flip ? 1f : -1f, 0f);
            float px = Px(t), k = K(t);
            Vector2 contact = tp + dir * 0.32f * k + new Vector2(0f, t.position_height + 4f * px * k);
            Ting(t, contact, dir, px);
            if (!d.is_projectile && (atk.current_position - tp).sqrMagnitude < 9f)
            {
                // flicked away: a flick of the wrist
                Combat.Launch(atk, tp, Random.Range(1f, 1.5f), Random.Range(0.4f, 0.6f), Random.value < 0.5f ? -420f : 420f);
            }
            return true;
        }

        /// <summary>
        /// A technique reaching a fighter four times its user's power: half the time it is parried
        /// with the same one-handed ting (Combat.ContactHandlers; area blows are never parried).
        /// </summary>
        private static Contact Parry(Move m, Actor v, bool area)
        {
            if (area || m == null || m.A == null || v == null || !v.isAlive() || m is Duelist)
            {
                return Contact.Hit;
            }
            if (Tiers.Of(v) == Tier.None || Moves.Busy(v) || v.is_unconscious || v.hasStatusStunned())
            {
                return Contact.Hit;
            }
            if (Tiers.Power(v) < 4f * Tiers.Power(m.A) || Random.value >= 0.5f)
            {
                return Contact.Hit;
            }
            Vector2 vp = v.current_position;
            Vector2 dir = m.Pose.Ground - vp;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : new Vector2(v.flip ? 1f : -1f, 0f);
            float px = Px(v), k = K(v);
            Vector2 contact = vp + dir * 0.32f * k + new Vector2(0f, v.position_height + 4f * px * k);
            Ting(v, contact, dir, px);
            if (Dev.On)
            {
                Dev.Trace("overwhelm: " + v.asset.id + "#" + v.Sid() + " parries " + m.GetType().Name + " of " + m.A.asset.id);
            }
            return Contact.Parried;
        }

        /// <summary>The one-handed deflection: a spark at the contact and a tiny glint on the defender.</summary>
        internal static void Ting(Actor t, Vector2 contact, Vector2 dir, float px)
        {
            float now = Time.unscaledTime;
            if (now - _lastTing > 0.08f)
            {
                _lastTing = now;
                Anim.Sound("event:/SFX/HIT/HitMetal", t.current_tile);
            }
            if (!Kit.Visible(contact))
            {
                return;
            }
            Tier tt = Tiers.Of(t);
            Fx.HitSpark(contact, px, 0, dir + Vector2.up * 0.4f, 4);
            Fx.Ring(contact, px, 0.14f, 2f, 6f, 1, 1, FxLayer.Top, 3, 1f, 2.5f, new Color(1f, 1f, 1f, 0.9f), DuelFx.Clear);
            Kit.Mark(t, tt != Tier.None ? Tiers.LightColor(tt) : Color.white, 0.2f, 0.65f);
        }
    }
}
