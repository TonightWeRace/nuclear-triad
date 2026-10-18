using System;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// Where an off-screen marker's arrow goes: on the window's edge, on the line from the point
    /// the view looks straight out of to the marker's projected point.
    /// </summary>
    public static class EdgePin
    {
        /// <summary>
        /// The direction from the view's center (cx, cy) to the point (px, py), in radians
        /// counterclockwise from +x, and where a ray that way from the center leaves the window
        /// [0, width] × [0, height]. Screen coordinates, y up.
        /// </summary>
        public static (double X, double Y, double Angle) Pin(double px, double py, double cx, double cy, double width, double height)
        {
            double angle = Math.Atan2(py - cy, px - cx);
            double dx = Math.Cos(angle), dy = Math.Sin(angle);
            double t = double.PositiveInfinity;
            if (dx > 1e-12) t = Math.Min(t, (width - cx) / dx);
            if (dx < -1e-12) t = Math.Min(t, -cx / dx);
            if (dy > 1e-12) t = Math.Min(t, (height - cy) / dy);
            if (dy < -1e-12) t = Math.Min(t, -cy / dy);
            return (cx + t * dx, cy + t * dy, angle);
        }
    }
}
