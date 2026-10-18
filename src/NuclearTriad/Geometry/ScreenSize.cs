using System;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// A screen's picture width in millimetres, before anyone has measured it. Windows holds the
    /// size each monitor reports in its EDID, usually the panel's lit area to the millimetre, so a
    /// new install starts from that rather than from a number that suits one rig. The report can
    /// be missing, or nonsense (a few monitors report their aspect ratio there), so it's checked
    /// against the pixel pitch it implies, and a guess stands in when it fails.
    /// </summary>
    public static class ScreenSize
    {
        /// <summary>Picture widths a monitor can have, in mm.</summary>
        public const double MinMm = 100, MaxMm = 3000;
        /// <summary>Pixel pitches real displays have, in mm per pixel: phones to wall-sized TVs.</summary>
        public const double MinPitch = 0.08, MaxPitch = 0.8;
        /// <summary>The pitch of a typical desktop monitor, for a guess from the pixel count alone.</summary>
        public const double GuessPitch = 0.25;
        /// <summary>The guess when neither the monitor's report nor its pixels are known.</summary>
        public const double FallbackMm = 600;

        /// <summary>
        /// The reported size turned the way the screen is mounted. Windows reports a portrait
        /// screen's size portrait here, but the heuristic costs nothing: if the millimetres and
        /// the pixels disagree on which way is wider, the millimetres are swapped.
        /// </summary>
        public static (double WidthMm, double HeightMm) Oriented(double widthMm, double heightMm, int pixelWidth, int pixelHeight)
        {
            if (widthMm <= 0 || heightMm <= 0 || pixelWidth <= 0 || pixelHeight <= 0) return (widthMm, heightMm);
            bool mmWide = widthMm > heightMm, pxWide = pixelWidth > pixelHeight;
            return mmWide == pxWide || widthMm == heightMm || pixelWidth == pixelHeight ? (widthMm, heightMm) : (heightMm, widthMm);
        }

        /// <summary>Whether a reported width could be this screen's: in range, and at a pitch a real display has.</summary>
        public static bool Plausible(double reportedMm, int pixelWidth)
        {
            if (!(reportedMm >= MinMm && reportedMm <= MaxMm)) return false;
            if (pixelWidth <= 0) return true;
            double pitch = reportedMm / pixelWidth;
            return pitch >= MinPitch && pitch <= MaxPitch;
        }

        /// <summary>
        /// The width a fresh setting starts at: the monitor's report when plausible, else a guess
        /// from its pixel count, else a plain fallback.
        /// </summary>
        public static double DefaultWidthMm(double reportedMm, int pixelWidth)
        {
            if (Plausible(reportedMm, pixelWidth)) return Math.Round(reportedMm);
            return pixelWidth > 0 ? Math.Round(pixelWidth * GuessPitch) : FallbackMm;
        }

        /// <summary>
        /// The length of the tuner's on-screen ruler for a screen of this width: a round number that
        /// fits well inside the picture.
        /// </summary>
        public static double RulerMm(double widthMm) => widthMm >= 500 ? 300 : widthMm >= 300 ? 200 : 100;
    }
}
