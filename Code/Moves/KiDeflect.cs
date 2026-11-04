using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// KI DEFLECTION. A projectile (an arrow, a fireball, an alien's plasma bolt, a bullet...) that
    /// strikes a tiered fighter may be turned aside, more often the higher its tier (Novice 12%, Adept
    /// 22%, Expert 35%, Master 55%, Grandmaster 75%, God 90%; more against a far weaker shooter, at most
    /// 97%), with a one-handed ting. The game sends a turned-aside shot back where it came from, now the
    /// fighter's own. From Master up it goes back CHARGED: faster and bigger, wrapped in a glow of the
    /// fighter's colour that sheds sparks, and where it lands a ki burst hits the shooter's side harder
    /// and wider the higher the tier (Master x1.2, Grandmaster x2 and the ground blasted, God x3.5, the
    /// ground torn open and the camera shaken).
    ///
    /// UNSHAKABLE. Whatever the game does to stun a unit (an explosion's force blast, a stun weapon, a
    /// spell) or to knock it flying when a blow lands, a tiered fighter may shrug off: Novice 10%,
    /// Adept 20%, Expert 30%, Master 40%, Grandmaster 50%, God 60%. A stronger realm bypasses this
    /// resistance. A lower realm cannot control a higher one unless five enemies attack it at close
    /// range and break its guard. It still
    /// takes the damage. A blast is rolled once per fighter (no stun, no throw). The
    /// player's own powers (bombs, Force) still move everyone, and the mod's own techniques stun as they
    /// are choreographed (their stuns come from the mod's frame: Own).
    /// </summary>
    internal static class KiDeflect
    {
        private static readonly float[] DeflectChance = { 0f, 0.12f, 0.22f, 0.35f, 0.55f, 0.75f, 0.9f };
        private static readonly float[] Resist = { 0f, 0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f };
        // a charged return (Master up): speed, size, the burst's blow and radius in tiles
        private static readonly float[] ReturnSpeed = { 1f, 1f, 1f, 1f, 1.35f, 1.6f, 2f };
        private static readonly float[] ReturnSize = { 1f, 1f, 1f, 1f, 1.4f, 1.8f, 2.4f };
        private static readonly float[] BurstMult = { 0f, 0f, 0f, 0f, 1.2f, 2f, 3.5f };
        private static readonly float[] BurstRadius = { 0f, 0f, 0f, 0f, 1.5f, 2.2f, 3.2f };

        private sealed class Charged
        {
            public Actor By;
            public long ById;
            public Tier Tier;
            public Particle Glow;
            public float Age, Trail;
        }

        private static readonly Dictionary<Projectile, Charged> _charged = new Dictionary<Projectile, Charged>();
        private static readonly List<Projectile> _drop = new List<Projectile>();

        // the blast going on right now (a unit's), and who stood firm in it
        private static bool _blast;
        private static Actor _blastBy;
        private static readonly Dictionary<long, bool> _firm = new Dictionary<long, bool>();
        // a stun the game lets through (makeStunned's own status, not rolled twice)
        private static bool _passing;
        private static readonly Dictionary<long, float> _shrugFx = new Dictionary<long, float>();

        /// <summary>Above 0 while the mod's own code runs: its stuns are choreography, never resisted.</summary>
        internal static int Own;

        /// <summary>Shots turned aside / sent back charged / fighters that stood firm (dev log).</summary>
        internal static int Deflected, Returned, StoodFirm;

        [SakugaInit]
        private static void Init()
        {
            Hooks.BeforeOrdinaryHit.Add(OnHit);
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += Clear;
        }

        private static void Clear()
        {
            foreach (Charged c in _charged.Values)
            {
                c.Glow?.Kill();
            }
            _charged.Clear();
            _firm.Clear();
            _blast = false;
            _blastBy = null;
        }

        // ------------------------------------------------------------ deflect --

        /// <summary>Hooks.BeforeOrdinaryHit: a projectile reaching a tiered fighter may be turned aside.</summary>
        private static bool OnHit(AttackData d, Actor t)
        {
            if (!d.is_projectile || t == null || !t.isAlive() || d.initiator == null || !d.initiator.isActor())
            {
                return false;
            }
            Tier tt = Tiers.Of(t);
            if (tt == Tier.None || t.is_unconscious || t.hasStatusStunned() || Moves.Intangible(t)
                || Moves.TryGet(t, out Move m) && m is Puppet)
            {
                return false;   // (in someone's hands: it can't move a hand)
            }
            Actor atk = d.initiator.a;
            if (atk == null || atk == t)
            {
                return false;
            }
            float chance = DeflectChance[(int)tt];
            float pt = Tiers.Power(t), pa = Mathf.Max(0.1f, Tiers.Power(atk));
            if (pt >= 3f * pa)
            {
                chance += 0.1f * Mathf.Log(pt / (3f * pa), 2f);   // a far weaker shooter's shot is easier
            }
            if (UnityEngine.Random.value >= Mathf.Min(0.97f, chance))
            {
                return false;
            }
            Deflected++;
            if (Dev.On) Dev.Trace("ki deflect: " + t.asset.id + "#" + t.Sid() + " (" + tt + ") turns " + atk.asset.id + "'s " + d.projectile_id + " aside (" + Mathf.Min(0.97f, chance).ToString("F2") + ")");
            Vector2 tp = t.current_position;
            Vector2 dir = (Vector2)d.initiator_position - tp;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : new Vector2(t.flip ? 1f : -1f, 0f);
            float px = Mathf.Clamp(t.actor_scale, 0.04f, 0.4f);
            float k = Mathf.Sqrt(Mathf.Clamp(t.actor_scale / 0.1f, 0.6f, 4f));
            Vector2 contact = tp + dir * 0.32f * k + new Vector2(0f, t.position_height + 4f * px * k);
            Presence.Ting(t, contact, dir, px);
            if (tt >= Tier.Master && Kit.Visible(contact))
            {
                Kit.Flash(contact, px, 3 + (int)tt - (int)Tier.Master, 0.12f, Tiers.LightColor(tt), FxLayer.Top, 31);
            }
            return true;   // the game sends the shot back (Projectile.getDeflected)
        }

        /// <summary>The game is about to send a deflected shot back: from Master up it goes faster.</summary>
        internal static void BeforeReturn(Projectile p, long byId)
        {
            Actor by = World.world != null ? World.world.units.get(byId) : null;
            Tier tt = by != null ? Tiers.Of(by) : Tier.None;
            // (a shot already charged keeps its speed when it is turned again: two masters trading it
            // must not speed it up without end; nor one past the charged cap, which goes back uncharged)
            if (tt >= Tier.Master && !_charged.ContainsKey(p) && _charged.Count <= 64)
            {
                p._speed *= ReturnSpeed[(int)tt];
            }
        }

        /// <summary>The shot is on its way back: from Master up, charged (bigger, glowing, a burst where it lands).</summary>
        internal static void AfterReturn(Projectile p, long byId)
        {
            Actor by = World.world != null ? World.world.units.get(byId) : null;
            Tier tt = by != null ? Tiers.Of(by) : Tier.None;
            // turned again: its old charge (glow, burst credit) goes, whoever turned it now
            bool again = _charged.TryGetValue(p, out Charged old);
            if (again)
            {
                old.Glow?.Kill();
                _charged.Remove(p);
            }
            if (tt < Tier.Master || _charged.Count > 64)
            {
                return;
            }
            if (!again)
            {
                // (it grows once: a charged shot turned again keeps its size)
                float size = ReturnSize[(int)tt];
                p._current_scale *= size;
                p._target_scale *= size;
            }
            Charged c = new Charged { By = by, ById = byId, Tier = tt };
            float px = Mathf.Clamp(by.actor_scale, 0.04f, 0.4f);
            c.Glow = Fx.Spawn(Art.Disk(2 + (int)tt - (int)Tier.Master, 1f, 0.6f), p.getTransformedPositionWithHeight(), px, 8f, FxLayer.Top, 7);
            if (c.Glow != null)
            {
                c.Glow.Held = true;
                c.Glow.Snap = true;
                c.Glow.Color(Fx.WithA(Tiers.LightColor(tt), 0.75f));
            }
            _charged[p] = c;
            Returned++;
        }

        /// <summary>A charged shot lands (or hits someone on the way): the ki burst.</summary>
        internal static void Landed(Projectile p)
        {
            if (!_charged.TryGetValue(p, out Charged c))
            {
                return;
            }
            _charged.Remove(p);
            c.Glow?.Kill();
            Burst(c, p.getCurrentPosition());
            if (Dev.On) Dev.Trace("ki deflect: " + c.Tier + "'s charged return lands (" + p.asset.id + ")");
        }

        private static void Burst(Charged c, Vector2 at)
        {
            int t = (int)c.Tier;
            Actor by = c.By != null && c.By.Sid() == c.ById && c.By.isAlive() ? c.By : null;
            float px = by != null ? Mathf.Clamp(by.actor_scale, 0.04f, 0.4f) : 0.1f;
            float r = BurstRadius[t];
            Color light = Tiers.LightColor(c.Tier), main = Tiers.Color(c.Tier);
            if (Kit.Visible(at))
            {
                Kit.Flash(at, px, 4 + 2 * (t - (int)Tier.Master), 0.1f, light, FxLayer.Top, 30);
                Kit.Shockwave(at, px, 3f, r / px, 0.35f, Fx.WithA(main, 0.85f));
                Kit.Sparks(at, px, 6 + 3 * (t - (int)Tier.Master), 20f, 45f, light);
                Kit.Shake(at, 0.2f, 0.004f + 0.004f * (t - (int)Tier.Master));
            }
            if (by == null)
            {
                return;
            }
            if (c.Tier >= Tier.Grandmaster)
            {
                Kit.Ravage(at, r * 0.8f, c.Tier >= Tier.God ? 3 : 2, by);
            }
            else
            {
                Kit.Ravage(at, r * 0.7f, 1, by);
            }
            foreach (Actor v in Combat.FoesAround(by, at, r))
            {
                if (!Combat.Living(v))
                {
                    continue;
                }
                Combat.Hit(by, v, BurstMult[t]);
                if (c.Tier >= Tier.Grandmaster && Combat.Living(v))
                {
                    Combat.Launch(v, at, 0.8f + 0.3f * (t - (int)Tier.Grandmaster), 0.5f, UnityEngine.Random.value < 0.5f ? -360f : 360f);
                }
            }
        }

        /// <summary>Every frame: charged shots' glows follow them and shed sparks; finished ones let go.</summary>
        private static void Tick(float dt)
        {
            if (_charged.Count == 0)
            {
                return;
            }
            _drop.Clear();
            foreach (KeyValuePair<Projectile, Charged> kv in _charged)
            {
                Projectile p = kv.Key;
                Charged c = kv.Value;
                c.Age += Time.unscaledDeltaTime;
                if (p == null || p.isFinished() || p.isTargetReached() || c.Age > 8f)
                {
                    _drop.Add(p);
                    continue;
                }
                Vector2 at = p.getTransformedPositionWithHeight();
                if (c.Glow != null && c.Glow.Alive)
                {
                    c.Glow.Pos = at;
                }
                c.Trail -= dt;
                if (c.Trail <= 0f && dt > 0f)
                {
                    c.Trail = 0.04f;
                    float px = c.Glow != null ? c.Glow.Px : 0.1f;
                    Kit.Ember(at, px, UnityEngine.Random.Range(0.25f, 0.4f), UnityEngine.Random.insideUnitCircle * 6f * px, Tiers.LightColor(c.Tier));
                }
            }
            foreach (Projectile p in _drop)
            {
                if (_charged.TryGetValue(p, out Charged c))
                {
                    c.Glow?.Kill();
                }
                _charged.Remove(p);
            }
        }

        // ------------------------------------------------------- stand firm --

        internal static void BlastBegin(BaseSimObject by)
        {
            _firm.Clear();
            _blast = by != null && by.isActor();   // (a god power's blast moves everyone)
            _blastBy = _blast ? by.a : null;
        }

        internal static void BlastEnd()
        {
            _blast = false;
            _blastBy = null;
            _firm.Clear();
        }

        /// <summary>A unit's force blast is running (its stuns are rolled once per fighter: Firm).</summary>
        internal static bool InBlast => _blast;

        /// <summary>During a unit's blast: does this fighter stand firm (no stun, no throw)? Rolled once per blast.</summary>
        internal static bool Firm(Actor a)
        {
            if (!_blast || a == null)
            {
                return false;
            }
            long id = a.Sid();
            if (_firm.TryGetValue(id, out bool firm))
            {
                return firm;
            }
            Tier t = Tiers.Of(a);
            if (RealmControl.IsLower(_blastBy, a))
            {
                firm = RealmControl.BlocksLower(a, _blastBy);
            }
            else if (RealmControl.IsHigher(_blastBy, a))
            {
                firm = false;
            }
            else
            {
                firm = Own <= 0 && t != Tier.None && UnityEngine.Random.value < Resist[(int)t];
            }
            _firm[id] = firm;
            if (firm)
            {
                StoodFirm++;
                if (Dev.On) Dev.Trace("ki deflect: " + a.asset.id + "#" + id + " (" + t + ") stands firm in a blast");
                ShrugFx(a, t);
            }
            return firm;
        }

        /// <summary>A stun or a knock-back from the game (not a blast's, not the mod's own): does this fighter shrug it off?</summary>
        internal static bool Shrugs(Actor a)
        {
            if (a == null || _passing || !a.isAlive())
            {
                return false;
            }
            Actor attacker = RealmControl.RecentAttacker(a);
            if (RealmControl.IsLower(attacker, a))
            {
                return RealmControl.BlocksLower(a, attacker);
            }
            if (RealmControl.IsHigher(attacker, a))
            {
                return false;
            }
            if (Own > 0)
            {
                return false;
            }
            Tier t = Tiers.Of(a);
            if (t == Tier.None || UnityEngine.Random.value >= Resist[(int)t])
            {
                return false;
            }
            ShrugFx(a, t);
            return true;
        }

        /// <summary>A flare of the tier's colour at its feet (at most every 0.4 s per fighter).</summary>
        private static void ShrugFx(Actor a, Tier t)
        {
            long id = a.Sid();
            float now = Time.unscaledTime;
            if (_shrugFx.TryGetValue(id, out float last) && now - last < 0.4f)
            {
                return;
            }
            if (_shrugFx.Count > 512)
            {
                _shrugFx.Clear();
            }
            _shrugFx[id] = now;
            Vector2 feet = a.current_position;
            if (Kit.Visible(feet))
            {
                float px = Mathf.Clamp(a.actor_scale, 0.04f, 0.4f);
                Kit.Flare(feet, px, Tiers.LightColor(t), 10f, 0.3f);
            }
        }

        internal static void StunBegin()
        {
            _passing = true;
        }

        internal static void StunEnd()
        {
            _passing = false;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.getDeflected))]
    internal static class DeflectReturnPatch
    {
        private static void Prefix(Projectile __instance, AttackDataResult pDataResult)
        {
            try
            {
                KiDeflect.BeforeReturn(__instance, pDataResult.deflected_by_who_id);
            }
            catch (Exception e)
            {
                SakugaMod.Report("deflect return", e);
            }
        }

        private static void Postfix(Projectile __instance, AttackDataResult pDataResult)
        {
            try
            {
                KiDeflect.AfterReturn(__instance, pDataResult.deflected_by_who_id);
            }
            catch (Exception e)
            {
                SakugaMod.Report("deflect return", e);
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "targetReached")]
    internal static class DeflectLandPatch
    {
        private static void Prefix(Projectile __instance)
        {
            try
            {
                KiDeflect.Landed(__instance);
            }
            catch (Exception e)
            {
                SakugaMod.Report("deflect land", e);
            }
        }
    }

    /// <summary>A unit's force blast: who stands firm is decided while it runs (stun and throw skipped for them).</summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.applyForceOnTile))]
    internal static class BlastPatch
    {
        private static void Prefix(BaseSimObject pByWho)
        {
            KiDeflect.BlastBegin(pByWho);
        }

        private static Exception Finalizer(Exception __exception)
        {
            KiDeflect.BlastEnd();
            return __exception;
        }
    }

    /// <summary>The game stunning a unit: a blast's (rolled per blast) or any other (rolled here).</summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.makeStunned))]
    internal static class BlastStunPatch
    {
        private static bool Prefix(Actor __instance)
        {
            // a blast's stun is decided by its one roll per fighter (Firm, which also decides the throw):
            // rolling Shrugs as well gave a second chance, and a fighter thrown but not stunned
            bool resists = KiDeflect.InBlast ? KiDeflect.Firm(__instance) : KiDeflect.Shrugs(__instance);
            if (resists)
            {
                return false;
            }
            KiDeflect.StunBegin();   // (its own "stunned" status below is not rolled again)
            if (Dev.On && KiDeflect.Own == 0 && Tiers.Of(__instance) != Tier.None)
            {
                Dev.Trace("ki deflect: " + __instance.asset.id + "#" + __instance.Sid() + " (" + Tiers.Of(__instance) + ") is stunned by the game");
            }
            return true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            KiDeflect.StunEnd();
            return __exception;
        }
    }

    /// <summary>A "stunned" status put on directly (a stun weapon, a spell).</summary>
    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.addStatusEffect), new[] { typeof(string), typeof(float), typeof(bool) })]
    internal static class StunStatusPatch
    {
        private static bool Prefix(BaseSimObject __instance, string pID, ref bool __result)
        {
            if (pID != "stunned" || __instance == null || !__instance.isActor() || !KiDeflect.Shrugs(__instance.a))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Actor), "calculateForce")]
    internal static class BlastThrowPatch
    {
        private static bool Prefix(Actor __instance)
        {
            return !KiDeflect.Firm(__instance);
        }
    }

    /// <summary>A landed blow throwing its target back (the game's knock-back on a hit): a fighter may stand its ground.</summary>
    [HarmonyPatch(typeof(ai.ActorTool), nameof(ai.ActorTool.applyForceToUnit))]
    internal static class HitThrowPatch
    {
        private static bool Prefix(BaseSimObject pTargetToCheck)
        {
            return pTargetToCheck == null || !pTargetToCheck.isActor() || !pTargetToCheck.isAlive() || !KiDeflect.Shrugs(pTargetToCheck.a);
        }
    }
}
