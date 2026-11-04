using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// INTERRUPTS. "Someone is loading up an energy beam, the other kicks them mid-charge and sends
    /// them flying." A unit whose move is gathering power (Move.Charging: a Meteor Slam's crouch, a
    /// Moonfall Fang's stance, a God's tell, a Choreo wind-up) is open to attack, and a blow that
    /// lands on it may break the charge:
    ///   a technique with Form.Interrupt (the specialists: a snap kick, a ki bullet, an interrupt
    ///   jab, a time skip) always does (a far stronger charger only shrugs off half of them);
    ///   any other technique's blow most of the time, a flow strike nearly as often;
    ///   an ordinary blow sometimes (a Master's swing often, a peasant's rarely), a shot now and then.
    /// The gathered power disperses (a burst of motes in the charger's colours, a ring that breaks,
    /// a flash, a beat of hit-stop), the move ends, and the charger is thrown away from the blow
    /// (a giant only staggers) and dazed a moment: the breaker gets a chain onto it (Launched).
    /// A charge something takes hold of (a grab, a throw) or that is stunned just disperses.
    /// Choreo moves break through the Choreo engine's own Choreo.Break (its burst, its flight inside
    /// the broken move); the one entry point, Break, decides.
    /// </summary>
    internal static class Interrupts
    {
        /// <summary>Charges broken by blows / dispersed / ordinary blows that broke one / divine charges that held (dev statistics).</summary>
        internal static int Broken, Dispersed, ByOrdinary, Warded_;

        /// <summary>
        /// A God invoking a divine technique is warded: no lesser blow breaks it, and another God's
        /// only now and then (the battles of Gods are where those techniques are meant to be seen).
        /// </summary>
        private static bool Warded(Actor victim, Actor by, Move m)
        {
            // a Primordial's power is broken only by another Primordial
            if (Primordials.Any && Primordials.Is(victim) && !Primordials.Is(by))
            {
                return true;
            }
            Technique t = m.Tech;
            if (t == null || t.School != School.None || t.Entry < Tier.God || Tiers.Of(victim) < Tier.God)
            {
                return false;
            }
            return by == null || Tiers.Of(by) < Tier.God || Random.value > 0.3f;
        }

        private struct Pending
        {
            public Actor V;
            public long Id;
            public Vector2 Dir;
            public float Dist, Apex;
            public int Frame;
        }

        private static readonly List<Pending> _launch = new List<Pending>();

        [SakugaInit]
        private static void Init()
        {
            Hooks.HitDealt += OnHit;
            Hooks.FrameTick += Tick;
            Hooks.WorldCleared += () => _launch.Clear();
        }

        // ----------------------------------------------------------- the break --

        /// <summary>
        /// Breaks the charge of 'victim' with a blow of 'by' (null: nobody's) along 'dir', 'force'
        /// about 1 for a jab, 2 a kick, 3 a heavy blow: the one entry point for every interrupt in the
        /// mod. False if it isn't charging.
        /// </summary>
        public static bool Break(Actor victim, Actor by, Vector2 dir, float force)
        {
            if (victim == null || !Moves.TryGet(victim, out Move m) || m.A != victim || !m.Charging)
            {
                return false;
            }
            if (Warded(victim, by, m))
            {
                Warded_++;
                return false;
            }
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = by != null ? victim.current_position - by.current_position : Random.insideUnitCircle;
                if (dir.sqrMagnitude < 1e-4f)
                {
                    dir = Vector2.right;
                }
            }
            dir.Normalize();
            force = Mathf.Clamp(force, 0.5f, 4f);
            Note(victim, by, m);
            if (ChoreoBreak(victim, by, dir, force))
            {
                Broken++;
                Follow(by, victim);
                return true;
            }
            BrokenFx(m, by, dir, force);
            Moves.Cancel(m);
            Broken++;
            // thrown away from the blow (at the end of the frame, unless the blow's own technique throws it)
            float lk = Mathf.Lerp(1f, Mathf.Sqrt(Mathf.Clamp(victim.actor_scale / 0.1f, 0.6f, 4f)), 0.5f);
            _launch.Add(new Pending
            {
                V = victim,
                Id = victim.Sid(),
                Dir = dir,
                Dist = (1.2f + 0.7f * force) * lk,
                Apex = (0.5f + 0.35f * force) * lk,
                Frame = Time.frameCount
            });
            Combat.Daze(victim, 0.35f + 0.12f * force);
            Follow(by, victim);
            if (Dev.On) Dev.Trace("break: " + (by != null ? by.asset.id + "#" + by.Sid() : "-") + " breaks the charge of " + victim.asset.id + "#" + victim.Sid()
                + " (" + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + ", force " + force.ToString("F1") + ")");
            return true;
        }

        /// <summary>
        /// The charge of 'm' simply comes apart (something took hold of the unit, or a stun): the
        /// motes scatter, the move ends, nothing is thrown.
        /// </summary>
        public static void Disperse(Move m, Actor by)
        {
            if (m == null || m.Ended || !m.Charging)
            {
                return;
            }
            Note(m.A, by, m);
            Vector2 dir = by != null ? m.A.current_position - by.current_position : Vector2.up;
            BrokenFx(m, by, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.up, 0.6f);
            Moves.Cancel(m);
            Dispersed++;
            if (Dev.On) Dev.Trace("disperse: the charge of " + m.A.asset.id + "#" + m.A.Sid() + " (" + (m.Tech != null ? m.Tech.Id : m.GetType().Name) + ") comes apart"
                + (by != null ? " under " + by.asset.id + "#" + by.Sid() : ""));
        }

        /// <summary>Remembered: the breaker punishes charges (the victim winds up less near it), the trace shows it broken.</summary>
        private static void Note(Actor victim, Actor by, Move m)
        {
            Reads.Trace t = Reads.Of(victim, true);
            if (t != null)
            {
                t.BrokenAt = Fighter.Now;
            }
            if (by != null && Fighters.TryGet(victim, out Fighter vf))
            {
                FoeNotes n = vf.NotesOn(by);
                n.Interrupted++;
                n.Saw(Form.Interrupt, 1f);
            }
            try
            {
                Hooks.ChargeBroken?.Invoke(victim, by, m);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] charge broken hook: " + e);
            }
        }

        /// <summary>The breaker (free to act) gets a chain onto its victim: a combo follows the break.</summary>
        private static void Follow(Actor by, Actor victim)
        {
            if (by == null || !Fighters.TryGet(by, out Fighter f) || Moves.Committed(by) || !Combat.Living(victim))
            {
                return;
            }
            f.Link = Weights.Heavy(victim) ? Link.Down : Link.Launched;
            f.LinkTarget = victim;
            f.LinkUntil = Fighter.Now + 0.5;
            f.ThinkIn = 0f;
            f.LinkThinkIn = 0f;
        }

        /// <summary>
        /// A Choreo move (the Choreo engine's) breaks through Choreo.Break: its own burst, its own flight
        /// inside the broken move ('force' there is in tiles: a jab's 2 here flies 3). True if it handled it.
        /// </summary>
        private static bool ChoreoBreak(Actor victim, Actor by, Vector2 dir, float force)
        {
            if (!Moves.TryGet(victim, out Move m) || !(m is ChoreoMove))
            {
                return false;
            }
            try
            {
                return Choreo.Break(victim, by, dir, force * 1.5f);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] Choreo.Break: " + e);
                return false;
            }
        }

        // ----------------------------------------------------------- the blows --

        /// <summary>Hooks.HitDealt: a technique's or a flow strike's blow landed: on a charging unit it may break the charge.</summary>
        private static void OnHit(Actor a, Actor v, HitResult r, Move by)
        {
            if (!r.Landed || a == null || v == null || !Moves.TryGet(v, out Move m) || m.A != v || !m.Charging)
            {
                return;
            }
            Form f = Reads.FormsOf(by);
            float chance = (f & Form.Interrupt) != 0 ? 1f : (f & Form.Fast) != 0 ? 0.85f : (f & Form.Heavy) != 0 ? 0.8f : (f & (Form.Strike | Form.Shot)) != 0 ? 0.68f : 0.55f;
            chance *= Odds(a, v, (f & Form.Interrupt) != 0);
            if (by != null && by.Interruptible)
            {
                chance *= 0.9f;   // a flow strike's blow
            }
            if (Random.value >= chance)
            {
                return;
            }
            float force = (f & Form.Heavy) != 0 ? 2.6f : (f & Form.Interrupt) != 0 ? 2f : (f & (Form.Kick | Form.Knee | Form.Head)) != 0 ? 1.8f : 1.3f;
            Vector2 dir = v.current_position - a.current_position;
            Break(v, a, dir, force);
        }

        /// <summary>
        /// MapBox.applyAttack (Reads.Ordinary): an ordinary blow or shot landing on a charging unit
        /// breaks it now and then: a Master's swing often, a peasant's rarely, an arrow seldom.
        /// </summary>
        public static void Ordinary(Actor a, Actor v, bool projectile)
        {
            if (a == null || v == null || !Combat.IsFoe(a, v))
            {
                return;
            }
            Tier ta = Tiers.Of(a);
            float chance = ta >= Tier.God ? 0.7f : ta == Tier.Grandmaster ? 0.6f : ta == Tier.Master ? 0.5f : ta == Tier.Expert ? 0.35f
                : ta == Tier.Adept ? 0.25f : ta == Tier.Novice ? 0.18f : 0.12f;
            chance *= Odds(a, v, false);
            if (projectile)
            {
                chance *= 0.6f;
            }
            try
            {
                // a quick fighter's blows come faster and break more
                chance *= Mathf.Clamp(0.85f + 0.05f * a.stats["attack_speed"], 0.85f, 1.3f);
            }
            catch
            {
                // (stats being rebuilt)
            }
            if (Random.value >= chance)
            {
                return;
            }
            if (Break(v, a, v.current_position - a.current_position, projectile ? 1f : 1.4f))
            {
                ByOrdinary++;
            }
        }

        /// <summary>How the tiers of breaker and charger weigh on it: a far stronger charger shrugs most off.</summary>
        private static float Odds(Actor a, Actor v, bool specialist)
        {
            int diff = (int)Tiers.Of(a) - (int)Tiers.Of(v);
            float k = diff >= 0 ? 1f : diff == -1 ? 0.75f : diff == -2 ? 0.45f : 0.2f;
            if (specialist)
            {
                k = Mathf.Max(k, 0.5f);
            }
            if (Weights.Heavy(v) && !Weights.Heavy(a))
            {
                k *= 0.6f;   // a colossus winding up barely notices a blow
            }
            return k;
        }

        // ------------------------------------------------------------- the look --

        /// <summary>The gathered power bursts apart: motes, a ring that breaks, a flash, a beat of hit-stop.</summary>
        private static void BrokenFx(Move m, Actor by, Vector2 dir, float force)
        {
            Actor v = m.A;
            Vector2 body = m.BodyPos();
            float px = m.Px;
            float k = m.K;
            Anim.Sound("event:/SFX/HIT/HitGeneric", v.current_tile);
            if (!Kit.Visible(body))
            {
                return;
            }
            Palette pal = Palette.For(v, Mathf.Clamp(m.Level, 1, 3));
            Color glow = Color.Lerp(pal.Edge, Color.white, 0.25f);
            // the blow's flash and the stop that sells it
            Kit.Flash(body, px, Mathf.RoundToInt(4f * k), 0.06f, new Color(1f, 1f, 1f, 0.95f));
            Kit.Stop(body, force >= 2f ? 0.07f : 0.045f);
            StillWater.FlashUnit(v, 0.08f, new Color(1f, 1f, 1f, 0.9f));
            // the motes it gathered fly apart (mostly away from the blow)
            int n = Fx.Count(force >= 2f ? 16 : 10);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = (d + dir * 0.9f).normalized;
                Particle p = Fx.Spawn(Art.Disk(Random.Range(1, 3), 1f, 0.3f), body, px, Random.Range(0.25f, 0.45f), FxLayer.Top, 9);
                if (p == null)
                {
                    break;
                }
                p.Moving(d * Random.Range(40f, 120f) * px * k, 0f, 5f);
                p.Sized(1f, 0.2f);
                p.Color(Color.white, glow, Fx.WithA(pal.Edge, 0f));
            }
            Kit.Sparks(body, px, 6, 70f, 150f, glow);
            // the ring it was holding breaks: two halves flung apart, fading
            Sprite[] half = Art.Arc(Mathf.RoundToInt(7f * k), Art.Ground, 0f, 160f, 1);
            Sprite[] other = Art.Arc(Mathf.RoundToInt(7f * k), Art.Ground, 180f, 340f, 1);
            Vector2 side = new Vector2(-dir.y, dir.x);
            Particle h1 = Fx.Spawn(half, m.Pose.Ground, px, 0.3f, FxLayer.Ground, 6);
            if (h1 != null)
            {
                h1.Moving(side * 26f * px * k, 0f, 4f);
                h1.Color(Fx.WithA(glow, 0.95f), Fx.WithA(pal.Edge, 0f));
            }
            Particle h2 = Fx.Spawn(other, m.Pose.Ground, px, 0.3f, FxLayer.Ground, 6);
            if (h2 != null)
            {
                h2.Moving(-side * 26f * px * k, 0f, 4f);
                h2.Color(Fx.WithA(glow, 0.95f), Fx.WithA(pal.Edge, 0f));
            }
            // a crack of white at the point of the blow
            Vector2 hit = body - dir * 2f * px * k;
            for (int i = 0; i < 3; i++)
            {
                float ang = (i - 1) * 40f * Mathf.Deg2Rad;
                Vector2 rd = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                Kit.Line(hit, hit + rd * 6f * px * k, px, 0.08f, 1, FxLayer.Top, 26, Color.white, Fx.WithA(glow, 0f));
            }
            if (force >= 2f)
            {
                Kit.Shake(body, 0.12f, 0.005f, k);
            }
        }

        // ------------------------------------------------------------ the flight --

        private static void Tick(float dt)
        {
            if (_launch.Count == 0)
            {
                return;
            }
            for (int i = _launch.Count - 1; i >= 0; i--)
            {
                Pending p = _launch[i];
                _launch.RemoveAt(i);
                Actor v = p.V;
                if (v == null || v.Sid() != p.Id || !Combat.Living(v) || Moves.Busy(v) || v.under_forces || v.position_height > 0.1f)
                {
                    continue;   // gone, or the blow's own technique already took it or threw it
                }
                if (Weights.Heavy(v))
                {
                    Combat.Stagger(v, 0.5f);
                    continue;
                }
                Combat.LaunchDir(v, p.Dir, p.Dist, p.Apex, Random.value < 0.5f ? -540f : 540f);
            }
        }
    }
}
