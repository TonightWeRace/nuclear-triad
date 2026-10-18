using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// A great circle around the eye, as two orthonormal directions: its points are
    /// U·cos t + V·sin t. Any plane through the eye cuts a flat panel in a straight line, so a
    /// great circle draws a straight line on every screen, and it must run straight through each
    /// bezel gap when the geometry matches the rig.
    /// </summary>
    public readonly struct GreatCircle
    {
        public readonly Vec3 U;
        public readonly Vec3 V;

        public GreatCircle(Vec3 u, Vec3 v)
        {
            U = u;
            V = v;
        }

        public Vec3 At(double t) => U * Math.Cos(t) + V * Math.Sin(t);
    }

    /// <summary>
    /// The lines the on-screen tuner draws, in eye space and fixed to the view. The level line at
    /// eye height lands at the eye height setting on every screen, whatever the screens' angles and
    /// gaps. So it shows just two settings. If the eye height is wrong, the line bends at the gaps
    /// of side screens turned in, which sit closer to the eye than the center screen's plane. If a
    /// vertical offset is wrong, it steps there. Lines crossing each gap at a slope show the other
    /// two: a wrong gap steps the rising and falling lines opposite ways, and a wrong angle bends
    /// them opposite ways.
    /// </summary>
    public static class TuningLines
    {
        /// <summary>The level line at eye height, running round the eye.</summary>
        public static GreatCircle EyeLevel => new GreatCircle(Vec3.Forward, Vec3.Right);

        /// <summary>
        /// A point on a small circle round straight ahead, which is where the eye looks square onto
        /// the center screen: the tuner rings the spot on it that the rig takes to be level with
        /// the eyes. t runs once round the circle over 2π.
        /// </summary>
        public static Vec3 AroundStraightAhead(double radiusDeg, double t)
        {
            double r = radiusDeg * Math.PI / 180;
            return Vec3.Forward * Math.Cos(r) + (Vec3.Right * Math.Cos(t) + Vec3.Up * Math.Sin(t)) * Math.Sin(r);
        }

        /// <summary>
        /// The direction from the eye through the middle of each bezel gap at eye height, left to
        /// right: between one panel's right edge and the next panel's left edge.
        /// </summary>
        public static List<Vec3> SeamDirections(IList<Panel> panels)
        {
            var seams = new List<Vec3>();
            for (int i = 0; i + 1 < panels.Count; i++)
            {
                Vec3 middle = (panels[i].LowerRight + panels[i + 1].LowerLeft) / 2;
                seams.Add(new Vec3(middle.X, 0, middle.Z).Normalized);
            }
            return seams;
        }

        /// <summary>
        /// The great circle through a level direction that rises to the right at the given slope
        /// where it passes through it.
        /// </summary>
        public static GreatCircle Through(Vec3 direction, double slopeDeg)
        {
            Vec3 u = direction.Normalized;
            Vec3 right = Vec3.Cross(Vec3.Up, u).Normalized;
            double slope = slopeDeg * Math.PI / 180;
            return new GreatCircle(u, right * Math.Cos(slope) + Vec3.Up * Math.Sin(slope));
        }
    }
}
