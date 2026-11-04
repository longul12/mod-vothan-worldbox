using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// DESTRUCTION SCALING. Turns a technique's Scale and its user's tier into how hard the world
    /// takes it, so 150 techniques destroy consistently (and the owner asked for MORE of it):
    ///   force (Kit.Ravage)   Novice/Adept rarely dig (a Light blow leaves the ground alone, a Heavy
    ///                        one scorches); Expert 1-2; Master 2-3; Grandmaster 3-4; God 4.
    ///                        Casual -1, Heavy +1, Ultimate +2 (at most 4).
    ///   radius               x1 up to Expert, x1.1 Master, x1.25 Grandmaster, x1.5 God; times
    ///                        0.7 / 1 / 1.3 / 1.8 for Casual / Light / Heavy / Ultimate.
    ///   shake                Kit.Shake strength from the same two numbers (a Novice's jab never
    ///                        shakes the camera; a God's ultimate shakes it hard).
    /// Kit.Ravage / Kit.Trench apply the "Environment damage" setting (0 none, 1 scorch only, 2 full)
    /// and Kit.Unsparing (Master+ at full: every building it lands on breaks, its own town's too;
    /// fire never starts under its own people's roofs). Units are never hurt by the ground: only the
    /// technique's own blows (Combat.Hit, enemies only) hurt units.
    /// </summary>
    internal static class Ruin
    {
        private static readonly int[] TierForce = { 0, 1, 1, 2, 2, 3, 4 };
        private static readonly float[] TierRadius = { 1f, 1f, 1.05f, 1.12f, 1.25f, 1.4f, 1.7f };

        /// <summary>Kit.Ravage force (0 = the ground is left alone) for a blow of this Scale by this tier.</summary>
        public static int Force(Tier t, Scale s)
        {
            int f = TierForce[Mathf.Clamp((int)t, 0, 6)];
            switch (s)
            {
                case Scale.Casual:
                    f--;
                    break;
                case Scale.Heavy:
                    f++;
                    break;
                case Scale.Ultimate:
                    f += 2;
                    break;
            }
            return Mathf.Clamp(f, 0, 4);
        }

        /// <summary>A blast radius in tiles: 'baseR' (as authored for a Light blow by an Expert) scaled by tier and Scale.</summary>
        public static float Radius(Tier t, Scale s, float baseR)
        {
            float k = s == Scale.Casual ? 0.7f : s == Scale.Heavy ? 1.3f : s == Scale.Ultimate ? 1.8f : 1f;
            return baseR * TierRadius[Mathf.Clamp((int)t, 0, 6)] * k;
        }

        /// <summary>Camera shake strength (Kit.Shake) for a blow of this Scale by this tier; 'weight' 0..1.5 is the blow itself.</summary>
        public static float Shake(Tier t, Scale s, float weight = 1f)
        {
            float b = 0.001f + 0.0016f * Mathf.Clamp((int)t, 0, 6);
            float k = s == Scale.Casual ? 0.4f : s == Scale.Heavy ? 1.6f : s == Scale.Ultimate ? 2.6f : 1f;
            return b * k * Mathf.Clamp(weight, 0f, 1.6f);
        }

        /// <summary>
        /// The world takes a blow at 'at': a crater of 'baseR' tiles (scaled), at the force the tier and
        /// Scale call for ('extra' adds or removes some). 'keep': the performer's own feet (scorched
        /// only, never dug or lit). Water and lava are left alone. Returns the force used.
        /// </summary>
        public static int Blast(Actor by, Vector2 at, float baseR, Scale s, Vector2? keep = null, bool fire = true, int extra = 0)
        {
            if (by == null || Settings.Destruction <= 0)
            {
                return 0;
            }
            Tier t = Tiers.Of(by);
            int force = Mathf.Clamp(Force(t, s) + extra, 0, 4);
            if (force <= 0)
            {
                return 0;
            }
            WorldTile c = Combat.TileAt(at);
            if (c == null || c.Type == null || c.Type.liquid || c.Type.lava)
            {
                return 0;
            }
            float r = Mathf.Clamp(Radius(t, s, baseR), 0.6f, 16f);
            Kit.Ravage(at, r, force, by, fire && force >= 3 && Burns(by), keep, keep.HasValue ? SingleKit.KeepR : 0f);
            return force;
        }

        /// <summary>
        /// Does this fighter's way of fighting set the world on fire? Fists (their heat) and earth (its magma)
        /// do; ki scorches with its light, and wind, steel, shadow and the hunt cut and break the ground, but
        /// none of them light fires: a tempest's cyclone or a swordsman's cut never leaves a burning field.
        /// </summary>
        public static bool Burns(Actor by)
        {
            if (!Settings.Schools || by == null)
            {
                return true;
            }
            bool any = false;
            foreach (School s in Schools.Of(by))
            {
                any = true;
                if (s == School.IronFist || s == School.EarthBreaker)
                {
                    return true;
                }
            }
            return !any;
        }

        /// <summary>A trench from a to b ('width' tiles) at the force the tier and Scale call for. Returns the force used.</summary>
        public static int Furrow(Actor by, Vector2 a, Vector2 b, float width, Scale s, Vector2? keep = null, bool fire = true, int extra = 0)
        {
            if (by == null || Settings.Destruction <= 0)
            {
                return 0;
            }
            Tier t = Tiers.Of(by);
            int force = Mathf.Clamp(Force(t, s) + extra, 0, 4);
            if (force <= 0)
            {
                return 0;
            }
            Kit.Trench(a, b, Mathf.Clamp(width * TierRadius[Mathf.Clamp((int)t, 0, 6)], 0.5f, 6f), force, by, fire && force >= 3 && Burns(by), keep,
                keep.HasValue ? SingleKit.KeepR : 0f);
            return force;
        }

        /// <summary>Camera shake for a blow of this Scale by 'by' at 'at' ('weight' 0..1.5), a duration fitting it.</summary>
        public static void Quake(Actor by, Vector2 at, Scale s, float weight = 1f, float k = 1f)
        {
            Tier t = Tiers.Of(by);
            float str = Shake(t, s, weight);
            if (str < 0.003f)
            {
                return;
            }
            float dur = Mathf.Clamp(0.12f + str * 18f, 0.12f, 0.8f);
            Kit.Shake(at, dur, str, k);
        }
    }
}
