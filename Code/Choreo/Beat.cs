using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>Easing curves a beat moves the pose along.</summary>
    internal enum Ease
    {
        Linear,
        InQuad,
        InCubic,
        OutQuad,
        OutCubic,
        /// <summary>Almost all of the way at once: a snap (the frame a blow lands).</summary>
        OutExpo,
        InOutSine,
        InOutCubic,
        /// <summary>Past the target and back: follow-through.</summary>
        OutBack
    }

    /// <summary>
    /// How the user's tier scales a beat's length (TierInfo.Linger is the mod's one measure of it):
    ///   Wind     anticipation: x sqrt(Linger) (a God winds up in 0.65 of the time)
    ///   Act      the blow itself: x1 Novice .. x0.7 God (higher tiers strike faster)
    ///   Follow   follow-through and holds between blows: like Wind
    ///   Recover  dead time after the move: x Linger (a God's in 0.42 of the time)
    ///   Impact   held impact frames: longer the higher the tier (x1 .. x1.6: their blows land harder)
    ///   Travel   steps and dashes: like Act
    ///   Free     as authored
    /// Every beat also grows a little with the performer's size (a giant moves heavier).
    /// </summary>
    internal enum Tempo
    {
        Free,
        Wind,
        Act,
        Follow,
        Recover,
        Impact,
        Travel
    }

    /// <summary>How a beat moves the performer over the ground.</summary>
    internal enum Route
    {
        /// <summary>It stays where it stands (only lunges and leans).</summary>
        None,
        /// <summary>A straight step, slide or dash (eased by the beat).</summary>
        Line,
        /// <summary>A leap: up to the apex and down, along the line.</summary>
        Arc,
        /// <summary>Gone on the way (hidden, intangible), there at the end.</summary>
        Blink,
        /// <summary>Round the target (or a point), on the ground's ellipse.</summary>
        Orbit
    }

    /// <summary>
    /// A pose target for the performer. Any field left NaN keeps the value the beat started with.
    ///   Rot     degrees, positive leans into the target
    ///   SX, SY  squash and stretch (1 = as drawn)
    ///   H       height above the ground, tiles x the performer's size
    ///   Lunge   offset towards the target, tiles x size (negative: drawn back)
    ///   Side    offset to its left (seen from it facing the target), tiles x size
    /// </summary>
    internal struct Shape
    {
        public float Rot, SX, SY, H, Lunge, Side;

        public Shape(float rot, float sx, float sy, float h = 0f, float lunge = 0f, float side = 0f)
        {
            Rot = rot;
            SX = sx;
            SY = sy;
            H = h;
            Lunge = lunge;
            Side = side;
        }

        /// <summary>Everything as it was.</summary>
        public static Shape Keep => new Shape(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);

        /// <summary>Upright, as drawn, on the ground where it stands.</summary>
        public static Shape Neutral => new Shape(0f, 1f, 1f, 0f, 0f, 0f);

        /// <summary>A crouch (squashed, leaning in): before a leap, a dash, a heavy blow.</summary>
        public static Shape Crouch => new Shape(10f, 1.16f, 0.8f, 0f, 0f, 0f);

        /// <summary>Stretched tall and thin (a launch, a rising blow).</summary>
        public static Shape Tall => new Shape(-6f, 0.84f, 1.24f, float.NaN, float.NaN, float.NaN);

        public Shape With(float? rot = null, float? sx = null, float? sy = null, float? h = null, float? lunge = null, float? side = null)
        {
            Shape s = this;
            if (rot.HasValue) s.Rot = rot.Value;
            if (sx.HasValue) s.SX = sx.Value;
            if (sy.HasValue) s.SY = sy.Value;
            if (h.HasValue) s.H = h.Value;
            if (lunge.HasValue) s.Lunge = lunge.Value;
            if (side.HasValue) s.Side = side.Value;
            return s;
        }
    }

    /// <summary>Something that happens at a point of a beat (0..1): a blow landing, a puff of dust.</summary>
    internal sealed class Cue
    {
        public float At;
        public Action<ChoreoMove> Do;
        public bool Fired;
    }

    /// <summary>
    /// One beat of a ChoreoMove's script: how long it lasts (scaled by the tier's tempo), the pose it
    /// eases into, how it moves over the ground, what it shows to readers (Forms, Charging), and what
    /// happens on the way (cues, a per-frame hook). Built with the ChoreoMove's fluent methods.
    /// </summary>
    internal sealed class Beat
    {
        public string Label;
        public Tempo Tempo = Tempo.Free;
        /// <summary>Seconds as authored (before the tier's tempo).</summary>
        public float Dur = 0.1f;
        public Ease Ease = Ease.OutQuad;
        public Shape To = Shape.Keep;

        public Route Route;
        public Ease PathEase = Ease.OutQuad;
        /// <summary>Where the path leads (asked once, as the beat starts).</summary>
        public Func<ChoreoMove, Vector2> Dest;
        /// <summary>An Arc's height at its top, tiles x size.</summary>
        public float Apex;
        /// <summary>Degrees turned over the beat (a flip: 360; a backflip: -360).</summary>
        public float Spin;
        /// <summary>The ground travel adds to the beat's length: seconds per tile (a dash's pace).</summary>
        public float PerTile;
        /// <summary>The destination is asked again every frame (it follows a target that moves).</summary>
        public bool Chase;
        public float MaxDur = 8f;

        /// <summary>Keeps turning towards the target while it plays.</summary>
        public bool Track = true;
        /// <summary>Its target height counts from the height an air strike started at (ChoreoMove.AirBase), not the ground.</summary>
        public bool RelH;
        /// <summary>A strike's beat: the blow it throws (its height follows the target's body, ChoreoMove.TrackHeight).</summary>
        public BlowShape Blow;
        /// <summary>What it shows (Form.None: the technique's own Forms).</summary>
        public Form Forms;
        public bool Charging, Intangible, Hidden, HideShadow, Tremble;
        /// <summary>The target must still be there when it starts (else retarget or end).</summary>
        public bool NeedsVictim;

        /// <summary>Afterimages every this many seconds (0: none), speed lines, dust at the heels.</summary>
        public float Ghosts, Lines, Dust;

        /// <summary>A held body's grip blends to this over the beat (Grab).</summary>
        public Grip? Grip;

        public Action<ChoreoMove> Enter, Exit;
        public Action<ChoreoMove, float> Every;
        public Func<ChoreoMove, bool> Until;
        public Func<ChoreoMove, bool> If;
        public List<Cue> Cues;

        /// <summary>Marks the move's recovery: a move that loses its target jumps here.</summary>
        public bool Recovery;
        /// <summary>Plays even after the target is lost (a world-scale effect: a shockwave, a meteor): a lost target's jump to recovery stops here.</summary>
        public bool Keep;

        /// <summary>What the beat belongs to (a strike's Hit, a flurry): the engine's own bookkeeping.</summary>
        public object Tag;

        /// <summary>The innermost OnlyIf group it belongs to (ChoreoMove.OnlyIf with a group), or null.</summary>
        public BeatGroup Group;

        public Beat On(float at, Action<ChoreoMove> a)
        {
            Cues ??= new List<Cue>(2);
            Cues.Add(new Cue { At = at, Do = a });
            Cues.Sort((x, y) => x.At.CompareTo(y.At));
            return this;
        }

        public static float Apply(Ease e, float k)
        {
            k = Mathf.Clamp01(k);
            switch (e)
            {
                case Ease.InQuad: return k * k;
                case Ease.InCubic: return k * k * k;
                case Ease.OutQuad: return 1f - (1f - k) * (1f - k);
                case Ease.OutCubic:
                    {
                        float i = 1f - k;
                        return 1f - i * i * i;
                    }
                case Ease.OutExpo: return k >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * k);
                case Ease.InOutSine: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * k);
                case Ease.InOutCubic: return k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
                case Ease.OutBack:
                    {
                        const float c1 = 1.70158f, c3 = c1 + 1f;
                        float x = k - 1f;
                        return 1f + c3 * x * x * x + c1 * x * x;
                    }
                default: return k;
            }
        }
    }

    /// <summary>
    /// Beats 'First'..'Last' of a script that play only if 'Cond' holds as the first of them comes up
    /// (ChoreoMove.OnlyIf with a group); 'Outer' is the group enclosing this one, if any.
    /// </summary>
    internal sealed class BeatGroup
    {
        public Func<ChoreoMove, bool> Cond;
        public int First, Last;
        public BeatGroup Outer;
        /// <summary>Decided as its first beat came up: 1 it plays, -1 it was skipped, 0 not yet.</summary>
        public int State;

        /// <summary>Does it play: as decided, or (not reached yet) would it if it came up now?</summary>
        public bool Plays(ChoreoMove m)
        {
            return State != 0 ? State > 0 : Cond(m);
        }
    }

    /// <summary>
    /// How a held body sits in the performer's hands: where (sprite pixels of the performer, forward
    /// and up from the middle of its body), turned how far (its own Rot: 180 = upside down), facing the
    /// performer (default) or the same way, squashed or stretched.
    /// </summary>
    internal struct Grip
    {
        public float Fwd, Up, Rot, SX, SY;
        public bool Same;

        public Grip(float fwd, float up, float rot = 0f, bool same = false, float sx = 1f, float sy = 1f)
        {
            Fwd = fwd;
            Up = up;
            Rot = rot;
            Same = same;
            SX = sx;
            SY = sy;
        }

        /// <summary>Held at arm's length in front, feet just off the ground.</summary>
        public static Grip Front => new Grip(4f, 1f, -8f);

        /// <summary>Hoisted over its head.</summary>
        public static Grip Overhead => new Grip(0.5f, 9f, -90f);

        /// <summary>Upside down in front, head down (a piledriver's hold).</summary>
        public static Grip Inverted => new Grip(3.5f, 2f, 180f);

        /// <summary>Clutched to its chest (a bear hug).</summary>
        public static Grip Hug => new Grip(2.5f, 0.5f, 0f, false, 0.92f, 1.04f);

        public static Grip Lerp(Grip a, Grip b, float k)
        {
            return new Grip(Mathf.Lerp(a.Fwd, b.Fwd, k), Mathf.Lerp(a.Up, b.Up, k), Mathf.Lerp(a.Rot, b.Rot, k), k < 0.5f ? a.Same : b.Same,
                Mathf.Lerp(a.SX, b.SX, k), Mathf.Lerp(a.SY, b.SY, k));
        }
    }
}
