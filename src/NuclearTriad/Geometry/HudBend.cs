using System;

namespace NuclearTriad.Geometry
{
    /// <summary>A linear map of the screen plane (y up): x' = Xx·x + Yx·y, y' = Xy·x + Yy·y.</summary>
    public readonly struct Linear2
    {
        public readonly double Xx, Yx, Xy, Yy;

        public Linear2(double xx, double yx, double xy, double yy)
        {
            Xx = xx;
            Yx = yx;
            Xy = xy;
            Yy = yy;
        }

        /// <summary>The map whose columns are the images of the x and y unit vectors.</summary>
        public static Linear2 FromColumns(double xX, double xY, double yX, double yY) => new Linear2(xX, yX, xY, yY);

        public static Linear2 Rotation(double degrees)
        {
            double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Linear2(c, -s, s, c);
        }

        public static Linear2 operator *(Linear2 m, Linear2 n) => new Linear2(
            m.Xx * n.Xx + m.Yx * n.Xy, m.Xx * n.Yx + m.Yx * n.Yy,
            m.Xy * n.Xx + m.Yy * n.Xy, m.Xy * n.Yx + m.Yy * n.Yy);

        public (double X, double Y) Apply(double x, double y) => (Xx * x + Yx * y, Xy * x + Yy * y);

        public double Determinant => Xx * Yy - Yx * Xy;

        public Linear2 Inverse()
        {
            double d = Determinant;
            return new Linear2(Yy / d, -Yx / d, -Xy / d, Xx / d);
        }
    }

    /// <summary>
    /// Bending a flat HUD group onto a wing screen. The game draws its flight HUD for the center
    /// screen's projection; where the group's anchor lands on a wing, the wing's projection there
    /// differs by a local linear map (a stretch, a tilt and a shear). A UI transform holds only a
    /// rotation and a scale along each of its own axes, so the fit keeps the group's own x axis
    /// exact (ladder rungs keep their true slope and width), its y scale exact across the rungs,
    /// and drops the shear.
    /// </summary>
    public static class HudBend
    {
        /// <summary>
        /// For a group drawn at rotationDeg (counter-clockwise), the rotation and scales along its
        /// own axes that come closest to applying map to it, and the shear left out (as a fraction
        /// of its y scale).
        /// </summary>
        public static (double RotationDeg, double ScaleX, double ScaleY, double Shear) Fit(Linear2 map, double rotationDeg)
        {
            // The map in the group's own frame.
            Linear2 local = Linear2.Rotation(-rotationDeg) * map * Linear2.Rotation(rotationDeg);
            (double x1, double y1) = local.Apply(1, 0);
            (double x2, double y2) = local.Apply(0, 1);
            double turn = Math.Atan2(y1, x1);
            double scaleX = Math.Sqrt(x1 * x1 + y1 * y1);
            double c = Math.Cos(turn), s = Math.Sin(turn);
            double scaleY = -x2 * s + y2 * c;
            double shear = x2 * c + y2 * s;
            return (rotationDeg + turn * 180 / Math.PI, scaleX, scaleY, Math.Abs(scaleY) > 1e-12 ? shear / scaleY : double.PositiveInfinity);
        }

        /// <summary>
        /// How much bigger the HUD has to be drawn at a point on a screen to look the same size,
        /// from the eye at the origin, as at the middle of the reference screen: the ratio of
        /// screen pixels per radian of view there (geometric mean over the screen's two axes).
        /// Give it the panels as they physically are, with the real eye, so the HUD keeps its
        /// size as the pilot sees it whatever the framing renders for.
        /// </summary>
        public static double PhysicalScale(Panel screen, double u, double v, double screenPxPerMm, Panel reference, double referencePxPerMm)
        {
            Vec3 point = screen.LowerLeft + (screen.LowerRight - screen.LowerLeft) * u + (screen.UpperLeft - screen.LowerLeft) * v;
            return PixelsPerRadian(screen, point, screenPxPerMm) / PixelsPerRadian(reference, reference.Center, referencePxPerMm);
        }

        static double PixelsPerRadian(Panel panel, Vec3 point, double pxPerMm)
        {
            const double step = 0.5;
            double across = step * pxPerMm / Angle(point, point + panel.Right * step);
            double up = step * pxPerMm / Angle(point, point + panel.Up * step);
            return Math.Sqrt(across * up);
        }

        static double Angle(Vec3 a, Vec3 b) => Math.Atan2(Vec3.Cross(a, b).Length, Vec3.Dot(a, b));
    }
}
