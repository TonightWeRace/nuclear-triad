using System;

namespace NuclearTriad.Geometry
{
    /// <summary>How a canvas scaler weighs the screen's width against its height.</summary>
    public enum ScreenMatch
    {
        MatchWidthOrHeight,
        Expand,
        Shrink,
    }

    /// <summary>
    /// The maths for pinning a menu canvas to the center screen while the game window spans
    /// several. A screen-overlay canvas covers the whole window, so a menu anchored to its edges
    /// lands on the outer screens, and a canvas that scales with the window's width grows with
    /// every screen added. Remapping the anchors of a canvas's top-level pieces into the center
    /// screen's share of the window, at the scale the canvas would have on that screen alone, lays
    /// the menu out as it would be on one screen.
    /// </summary>
    public static class MenuFit
    {
        /// <summary>
        /// The scale a canvas that scales with the screen size takes on a screen of the given
        /// size, for its reference resolution and how it matches. With MatchWidthOrHeight, match 0
        /// follows the width, 1 the height, and values between blend the two logarithmically.
        /// Expand takes the smaller of the two ratios, Shrink the larger.
        /// </summary>
        public static double CanvasScale(double width, double height, double referenceWidth, double referenceHeight, ScreenMatch mode, double match)
        {
            double byWidth = width / referenceWidth, byHeight = height / referenceHeight;
            switch (mode)
            {
                case ScreenMatch.Expand:
                    return Math.Min(byWidth, byHeight);
                case ScreenMatch.Shrink:
                    return Math.Max(byWidth, byHeight);
                default:
                    double weighted = Math.Log(byWidth, 2) + (Math.Log(byHeight, 2) - Math.Log(byWidth, 2)) * match;
                    return Math.Pow(2, weighted);
            }
        }

        /// <summary>
        /// An anchor, as a fraction of the window along one axis, moved into the band that starts
        /// at start and spans size, both fractions of the window: 0 lands on the band's start and
        /// 1 on its end.
        /// </summary>
        public static double Into(double anchor, double start, double size) => start + anchor * size;
    }
}
