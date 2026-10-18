using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// The rig's screens as Windows' display arrangement places them, for Monitor settings left
    /// blank. Windows lines its displays up edge to edge, so three screens side by side show as a
    /// center display with exactly one display beside it on each side: touching that edge and
    /// sharing some of its rows. Only one display may fit. With none, or several (four in a row,
    /// say), the arrangement doesn't say which three are the rig, and nothing is guessed. A display
    /// above or below the row, such as a small dash screen, doesn't get in the way.
    /// </summary>
    public static class DisplayRow
    {
        // In a per-monitor DPI-aware process, as the game is, edges meet exactly. A couple of pixels
        // of slack covers rounding anywhere else; Windows leaves no real gap that narrow.
        const int Slack = 2;

        /// <summary>
        /// The indices of the left, center and right screens among the displays, or null when the
        /// arrangement doesn't hold exactly one row of three.
        /// </summary>
        public static (int Left, int Center, int Right)? Find(IList<PixelRect> displays)
        {
            (int Left, int Center, int Right)? found = null;
            for (int center = 0; center < displays.Count; center++)
            {
                int left = Beside(displays, center, toTheLeft: true), right = Beside(displays, center, toTheLeft: false);
                if (left < 0 || right < 0) continue;
                if (found.HasValue) return null;
                found = (left, center, right);
            }
            return found;
        }

        // The one display touching that side of displays[i] and sharing some of its rows: its index,
        // or −1 when there's none or more than one.
        static int Beside(IList<PixelRect> displays, int i, bool toTheLeft)
        {
            PixelRect d = displays[i];
            int found = -1;
            for (int j = 0; j < displays.Count; j++)
            {
                PixelRect other = displays[j];
                if (j == i || other.IsEmpty) continue;
                bool touches = toTheLeft ? Math.Abs(other.Right - d.Left) <= Slack : Math.Abs(other.Left - d.Right) <= Slack;
                bool sharesRows = other.Top < d.Bottom && d.Top < other.Bottom;
                if (!touches || !sharesRows) continue;
                if (found >= 0) return -1;
                found = j;
            }
            return found;
        }

        /// <summary>Whether the three screens run left to right in the arrangement, judged by their middles.</summary>
        public static bool InOrder(PixelRect left, PixelRect center, PixelRect right) =>
            Middle(left) < Middle(center) && Middle(center) < Middle(right);

        static double Middle(PixelRect r) => (r.Left + r.Right) / 2.0;
    }
}
