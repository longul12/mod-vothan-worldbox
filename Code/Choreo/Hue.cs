using UnityEngine;

namespace Sakuga
{
    /// <summary>What an effect is made of (its colours): the school's own, or a preset.</summary>
    internal enum Element
    {
        /// <summary>The technique's school colour (white for School.None), mixed with the tier.</summary>
        School,
        Ki,
        Fire,
        Wind,
        Stone,
        Steel,
        Shadow,
        Holy,
        Lightning
    }

    /// <summary>
    /// THE PALETTE GRAMMAR. The colours one technique is drawn in, from its school, the user's tier
    /// and the level it is played at, so the school reads in the hue and the tier in the rim:
    ///   level I    white and pale school colour (clean, quick, weak); the tier only as a thin rim
    ///   level II   the school colour on swooshes and trails, the tier's colour on their edges
    ///   level III  white-hot cores, a saturated school colour, the tier's colour burning at the rim
    /// An element preset (fire, ki, wind, stone, steel, shadow, holy, lightning) replaces the school
    /// colour for what is made of that element; dust, rocks and fire keep their natural colours.
    ///   Core      the brightest (the heart of a slash, a beam's core, a flash)
    ///   Main      the body of an effect (swoosh, orb, beam)
    ///   Edge      its outer colour (rims, trails, rings) - school and tier mixed
    ///   Rim       the tier's own colour (an outline, the last ring): the tier at a glance
    ///   Ghost/GhostEnd  afterimages (start, end at alpha 0)
    ///   Dark      deep shade (outlines, the black flash)
    ///   Pal       the same as a tier Palette, for the shared Kit/Fx functions (ImpactFrame...)
    /// </summary>
    internal struct Hue
    {
        public Color Core, Main, Edge, Rim, Ghost, GhostEnd, Dark;
        public Palette Pal;
        public School School;
        public Tier Tier;
        public int Level;

        public static Hue For(Actor a, School school, int level)
        {
            return For(Tiers.Of(a), school, level, Element.School);
        }

        public static Hue For(Actor a, School school, int level, Element e)
        {
            return For(Tiers.Of(a), school, level, e);
        }

        public static Hue For(Tier tier, School school, int level, Element e = Element.School)
        {
            level = Mathf.Clamp(level, 1, 3);
            Color t = Tiers.Color(tier);
            Color tl = Tiers.LightColor(tier);
            Color s = e == Element.School ? (school == School.None ? new Color(1f, 0.96f, 0.86f, 1f) : Schools.Color(school)) : MainOf(e);
            Color se = e == Element.School ? Fx.Darken(s, 0.12f) : EdgeOf(e);
            Hue h = new Hue { School = school, Tier = tier, Level = level, Rim = t, Dark = Tiers.DarkColor(tier) };
            switch (level)
            {
                case 1:
                    // (level I is the school's own colour too, only brighter: a Novice's technique must
                    // read as its school's as surely as a God's)
                    h.Core = Color.white;
                    h.Main = Color.Lerp(Color.white, s, 0.8f);
                    h.Edge = Color.Lerp(se, t, 0.12f);
                    h.Ghost = new Color(1f, 1f, 1f, 0.7f);
                    h.GhostEnd = Fx.WithA(h.Edge, 0f);
                    break;
                case 2:
                    h.Core = Color.Lerp(Color.white, tl, 0.35f);
                    h.Main = Color.Lerp(s, Color.white, 0.1f);
                    h.Edge = Color.Lerp(se, t, 0.35f);
                    h.Ghost = Fx.WithA(Color.Lerp(s, Color.white, 0.45f), 0.8f);
                    h.GhostEnd = Fx.WithA(h.Edge, 0f);
                    break;
                default:
                    h.Core = new Color(1f, 0.99f, 0.94f, 1f);
                    h.Main = Color.Lerp(s, Color.white, 0.12f);
                    h.Edge = Color.Lerp(se, t, 0.5f);
                    h.Ghost = new Color(1f, 1f, 1f, 0.9f);
                    h.GhostEnd = Fx.WithA(Color.Lerp(s, t, 0.5f), 0f);
                    break;
            }
            if (tier == Tier.None)
            {
                h.Rim = new Color(1f, 0.9f, 0.7f, 1f);
            }
            h.Core.a = h.Main.a = h.Edge.a = h.Rim.a = 1f;
            h.Pal = new Palette { Core = h.Core, Edge = h.Edge, Ghost = h.Ghost, GhostEnd = h.GhostEnd, Dark = h.Dark, Tier = tier, Level = level };
            return h;
        }

        /// <summary>The same technique's colours for something made of an element (its rim stays the tier's).</summary>
        public Hue As(Element e)
        {
            return For(Tier, School, Level, e);
        }

        /// <summary>The element's body colour.</summary>
        public static Color MainOf(Element e)
        {
            switch (e)
            {
                case Element.Ki: return new Color(1f, 0.9f, 0.5f, 1f);
                case Element.Fire: return Kit.Fire1;
                case Element.Wind: return new Color(0.84f, 1f, 0.98f, 1f);
                case Element.Stone: return new Color(0.74f, 0.62f, 0.46f, 1f);
                case Element.Steel: return new Color(0.9f, 0.94f, 1f, 1f);
                case Element.Shadow: return new Color(0.56f, 0.38f, 0.82f, 1f);
                case Element.Holy: return new Color(1f, 0.96f, 0.76f, 1f);
                case Element.Lightning: return new Color(0.86f, 0.93f, 1f, 1f);
                default: return Color.white;
            }
        }

        /// <summary>The element's edge colour.</summary>
        public static Color EdgeOf(Element e)
        {
            switch (e)
            {
                case Element.Ki: return new Color(1f, 0.7f, 0.18f, 1f);
                case Element.Fire: return Kit.Fire2;
                case Element.Wind: return new Color(0.5f, 0.86f, 0.94f, 1f);
                case Element.Stone: return new Color(0.48f, 0.36f, 0.24f, 1f);
                case Element.Steel: return new Color(0.66f, 0.74f, 0.88f, 1f);
                case Element.Shadow: return new Color(0.26f, 0.12f, 0.38f, 1f);
                case Element.Holy: return new Color(1f, 0.82f, 0.4f, 1f);
                case Element.Lightning: return new Color(0.5f, 0.66f, 1f, 1f);
                default: return new Color(0.86f, 0.9f, 0.96f, 1f);
            }
        }

        /// <summary>Core, edge, transparent: a gradient for particles (a new array: call it once, not per frame).</summary>
        public Color[] Ramp(float alpha = 1f)
        {
            return new[] { Fx.WithA(Core, alpha), Fx.WithA(Edge, alpha * 0.85f), Fx.WithA(Edge, 0f) };
        }

        /// <summary>The impact frame's light core and rim.</summary>
        public Color ImpactLight => Color.white;

        /// <summary>The black flash's dark core.</summary>
        public static readonly Color ImpactDark = new Color(0.05f, 0.02f, 0.04f, 1f);

        /// <summary>A colour at alpha a.</summary>
        public static Color A(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
