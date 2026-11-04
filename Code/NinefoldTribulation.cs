using System;
using UnityEngine;

namespace Sakuga
{
    // One trial per world bounds effect work and reserves a single ascension slot.
    internal sealed class NinefoldTribulation : Move
    {
        private static NinefoldTribulation _current;
        private const string Key = TrueGodAscension.Prefix + "trial_";
        private readonly string _species;
        private readonly object _world;
        private readonly float _basis, _strength;
        private int _completed;
        private float _left, _visualIn;
        private bool _warning, _won;
        private Particle _cloud;
        private int _cloudGen;
        private readonly float _px;

        private NinefoldTribulation(Actor actor, bool restore) : base(actor)
        {
            _species = actor.asset.id;
            _world = World.world.map_stats;
            _px = Mathf.Clamp(Px, .08f, .16f);
            actor.data.get(Key + "round", out _completed, 0);
            actor.data.get(Key + "basis", out float basis, actor.getMaxHealth());
            actor.data.get(Key + "strength", out float strength, Settings.TribulationPower / 100f);
            _basis = restore ? basis : actor.getMaxHealth();
            _strength = restore ? strength : Settings.TribulationPower / 100f;
            _left = restore ? 3f : 6f;
            Pose.Height = 0;
        }

        public static bool IsUnderTrial(Actor actor)
        {
            if (actor?.data == null) return false;
            actor.data.get(Key + "active", out bool active, false);
            return active;
        }

        public static void Begin(Actor actor)
        {
            if (_current != null || IsUnderTrial(actor)) return;
            actor.data.set(Key + "round", 0);
            Start(actor, false);
        }

        private static void Start(Actor actor, bool restore)
        {
            actor.stopSleeping();
            actor.clearWait();
            if (actor.ai != null) actor.cancelAllBeh();
            actor.setAttackTarget(null);
            var trial = new NinefoldTribulation(actor, restore);
            if (!Moves.Start(trial)) return;
            _current = trial;
            actor.data.set(Key + "active", true);
            actor.data.set(Key + "life", actor.asset.id);
            actor.data.set(Key + "basis", trial._basis);
            actor.data.set(Key + "strength", trial._strength);
            Notify(actor, restore ? "tiếp tục Cửu Trọng Lôi Kiếp" : "bắt đầu độ Cửu Trọng Lôi Kiếp!");
        }

        public static void TryRestore(Actor actor)
        {
            if (_current != null || !IsUnderTrial(actor)
                || SaveManager.isLoadingSaveAnimationActive()) return;
            actor.data.get(Key + "life", out string life, "");
            actor.data.get(Key + "round", out int round, -1);
            actor.data.get(Key + "basis", out float basis, 0);
            actor.data.get(Key + "strength", out float strength, 0);
            if (!actor.isAlive() || actor.getHealth() <= 0 || actor.asset.id != life
                || Tiers.Of(actor) != Tier.God || Primordials.Is(actor)
                || round < 0 || round > 9 || float.IsNaN(basis) || float.IsInfinity(basis) || basis <= 0
                || float.IsNaN(strength) || float.IsInfinity(strength) || strength < .1f || strength > 5)
            {
                actor.data.set(Key + "active", false);
                return;
            }
            if (Moves.Busy(actor) || actor.isInsideSomething() || actor.isInMagnet()
                || TrueGodAscension.BusyElsewhere(actor)) return;
            Start(actor, true);
        }

        // Called BEFORE Moves.Clear. Never turn loading another map into a failed trial.
        public static void ForgetWorld() { _current = null; }

        public override void Tick(float dt)
        {
            if (!ReferenceEquals(_world, World.world?.map_stats)) { Finished = true; return; }
            if (!Valid || A.getHealth() <= 0 || A.asset.id != _species
                || Tiers.Of(A) != Tier.God || Primordials.Is(A)) { Finished = true; return; }
            if (!Settings.TrueGodGrowth || Settings.ProgressSpeed == 0 || Settings.MaxTier < (int)Tier.God
                || Settings.TrueGodCap <= 0) { Finished = true; return; }
            // The non-interruptible Move freezes AI and physics; defense damage hooks remain active.
            A.setAttackTarget(null);
            _visualIn -= dt;
            if (_visualIn <= 0)
            {
                _visualIn = .65f;
                DrawAmbient();
            }
            _left -= dt;
            if (_left > 0) return;
            if (_completed == TribulationRules.Rounds)
            {
                _won = TribulationRules.Successful(_completed, A.isAlive(), A.getHealth());
                Finished = true;
                return;
            }
            if (!_warning)
            {
                _warning = true;
                _left = _completed == 8 ? 2.5f : 1.2f;
                TribulationArt.Warning(Pose.Ground, _px, _completed + 1, _left);
                return;
            }
            _warning = false;
            _completed++;
            // Update once, before the native damage call: save/load never repeats a paid strike.
            A.data.set(Key + "round", _completed);
            try { TribulationArt.Strike(A, Pose.Ground, _px, _completed); }
            catch (Exception e) { SakugaMod.Report("tribulation effects", e); }
            float damage = TribulationRules.Damage(_completed, _basis, _strength);
            A.getHit(damage, true, AttackType.Other, null, false, false, true);
            _left = _completed == 9 ? 1f : 3.5f;
            Notify(A, "chịu lôi kiếp " + _completed + "/9");
            if (A.getHealth() <= 0 || !A.isAlive()) Finished = true;
        }

        private void DrawAmbient()
        {
            if (!Settings.TribulationEffects) { ReleaseCloud(); return; }
            if (_cloud == null || !_cloud.Alive || _cloud.Gen != _cloudGen)
            {
                _cloud = Fx.Spawn(TribulationArt.Cloud, Pose.Ground + Vector2.up * 11f,
                    _px * 2f, 1f, FxLayer.Top, 35);
                if (_cloud != null)
                {
                    _cloud.Held = true;
                    _cloud.Color(new Color(.20f, .16f, .30f, .94f));
                    _cloudGen = _cloud.Gen;
                }
            }
            if (_cloud != null && _cloud.Gen == _cloudGen)
            {
                _cloud.SX = 1f + _completed * .07f;
                _cloud.SY = .85f + _completed * .025f;
            }
            TribulationArt.Ambient(Pose.Ground, _px, _completed + 1);
        }

        private void ReleaseCloud()
        {
            if (_cloud != null && _cloud.Gen == _cloudGen) _cloud.Kill();
            _cloud = null;
        }

        public override void OnEnd(bool aborted)
        {
            ReleaseCloud();
            if (_current != this) return;
            _current = null;
            if (A == null || A.Sid() != Id || !ReferenceEquals(_world, World.world?.map_stats)) return;
            A.data.set(Key + "active", false);
            A.clearWait();
            if (!A.isAlive() || A.getHealth() <= 0 || A.asset.id != _species) return;
            A.setStatsDirty();
            if (!aborted && _won && Primordials.Trait != null && !Primordials.Is(A))
            {
                A.addTrait(Primordials.Trait, true);
                A.updateStats();
                A.restoreHealth(A.getMaxHealth());
                TribulationArt.Victory(Pose.Ground, _px);
                Notify(A, "vượt Cửu Trọng Lôi Kiếp, chứng đạo Chân Thần!");
            }
            else
            {
                A.data.set(TrueGodAscension.Prefix + "retry", (float)World.world.getCurWorldTime() + 20f * 60f);
                Notify(A, "độ kiếp thất bại, cần chờ 20 năm để thử lại");
            }
        }

        internal static void Notify(Actor actor, string message)
        {
            string text = actor.getName() + " " + message;
            WorldTip.showNow(text, false, "top", 3f, "#B9A0FF");
            Debug.Log("[Sakuga][LoiKiep] " + text);
        }
    }
}
