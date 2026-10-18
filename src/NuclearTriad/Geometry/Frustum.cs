using System;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// An off-axis frustum in its camera's own frame: the tangents of its four edges at unit
    /// distance, plus the distance from the eye to the panel's plane.
    /// </summary>
    public readonly struct Frustum
    {
        public readonly double Left;
        public readonly double Right;
        public readonly double Bottom;
        public readonly double Top;
        public readonly double DistanceMm;

        public Frustum(double left, double right, double bottom, double top, double distanceMm)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
            DistanceMm = distanceMm;
        }

        public double HorizontalFovDeg => Degrees(Math.Atan(Right) - Math.Atan(Left));

        public double VerticalFovDeg => Degrees(Math.Atan(Top) - Math.Atan(Bottom));

        /// <summary>
        /// The vertical FOV of a symmetric frustum with the same vertical extent, for code that
        /// reads Camera.fieldOfView, such as LOD selection.
        /// </summary>
        public double EquivalentVerticalFovDeg => Degrees(2 * Math.Atan((Top - Bottom) / 2));

        public bool IsSymmetric(double tolerance) =>
            Math.Abs(Left + Right) <= tolerance && Math.Abs(Bottom + Top) <= tolerance;

        /// <summary>
        /// The matrix Unity's Matrix4x4.Frustum builds for these extents (OpenGL convention, view
        /// space looking down −Z), as [row, column]. The plugin calls Unity's own; this exists so
        /// the tests can check the whole chain.
        /// </summary>
        public double[,] Matrix(double near, double far)
        {
            double l = Left * near, r = Right * near, b = Bottom * near, t = Top * near;
            return new[,]
            {
                { 2 * near / (r - l), 0, (r + l) / (r - l), 0 },
                { 0, 2 * near / (t - b), (t + b) / (t - b), 0 },
                { 0, 0, -(far + near) / (far - near), -2 * far * near / (far - near) },
                { 0, 0, -1, 0 },
            };
        }

        public override string ToString() =>
            $"tan l {Left:F4} r {Right:F4} b {Bottom:F4} t {Top:F4} at {DistanceMm:F1} mm ({HorizontalFovDeg:F2}° x {VerticalFovDeg:F2}°)";

        static double Degrees(double radians) => radians * 180 / Math.PI;
    }
}
