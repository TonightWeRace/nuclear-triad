using System;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// A flat screen's lit image as a rectangle in eye space, given by three corners as seen from
    /// the eye: lower left, lower right and upper left.
    /// </summary>
    public sealed class Panel
    {
        public Panel(string name, Vec3 lowerLeft, Vec3 lowerRight, Vec3 upperLeft)
        {
            Name = name;
            LowerLeft = lowerLeft;
            LowerRight = lowerRight;
            UpperLeft = upperLeft;
            Right = (lowerRight - lowerLeft).Normalized;
            Up = (upperLeft - lowerLeft).Normalized;
            if (Math.Abs(Vec3.Dot(Right, Up)) > 1e-9)
                throw new ArgumentException($"Panel '{name}': its corners don't form a rectangle.");
            Forward = Vec3.Cross(Right, Up);
        }

        public string Name { get; }
        public Vec3 LowerLeft { get; }
        public Vec3 LowerRight { get; }
        public Vec3 UpperLeft { get; }
        public Vec3 UpperRight => LowerRight + (UpperLeft - LowerLeft);
        public Vec3 Center => (LowerLeft + UpperRight) / 2;
        public double Width => (LowerRight - LowerLeft).Length;
        public double Height => (UpperLeft - LowerLeft).Length;

        // The panel's orientation, which its camera takes as its own: +x along the panel to the
        // right, +y up it, +z through it and away from the eye.
        public Vec3 Right { get; }
        public Vec3 Up { get; }
        public Vec3 Forward { get; }

        /// <summary>How far the eye is in front of the panel's plane: zero or less when it isn't in front.</summary>
        public double DistanceFrom(Vec3 eye) => Vec3.Dot(LowerLeft - eye, Forward);

        /// <summary>
        /// Kooima's generalized perspective projection with the rotation left to the camera
        /// transform: the frustum's extents in the panel's own frame, seen from the eye.
        /// </summary>
        public Frustum FrustumFrom(Vec3 eye)
        {
            Vec3 toLowerLeft = LowerLeft - eye;
            Vec3 toLowerRight = LowerRight - eye;
            Vec3 toUpperLeft = UpperLeft - eye;
            double distance = DistanceFrom(eye);
            if (!(distance > 0))
                throw new InvalidOperationException($"Panel '{Name}': the eye is not in front of it.");
            return new Frustum(
                Vec3.Dot(Right, toLowerLeft) / distance,
                Vec3.Dot(Right, toLowerRight) / distance,
                Vec3.Dot(Up, toLowerLeft) / distance,
                Vec3.Dot(Up, toUpperLeft) / distance,
                distance);
        }

        /// <summary>
        /// The part of the panel from u0 to u1 across and v0 to v1 up, as fractions measured from
        /// its lower left corner. For a viewport that covers only part of a monitor.
        /// </summary>
        public Panel Sub(double u0, double v0, double u1, double v1)
        {
            Vec3 across = LowerRight - LowerLeft;
            Vec3 up = UpperLeft - LowerLeft;
            Vec3 lowerLeft = LowerLeft + across * u0 + up * v0;
            return new Panel(Name, lowerLeft, lowerLeft + across * (u1 - u0), lowerLeft + up * (v1 - v0));
        }
    }
}
