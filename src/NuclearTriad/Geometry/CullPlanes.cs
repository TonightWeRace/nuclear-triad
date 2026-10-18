using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// Planes through the eye that bound every screen's view, plus a margin. The game culls trees
    /// and grass on the GPU against one camera's frustum planes, which with the rig on cover only
    /// the center screen (each kind padded by its own radius, so they pop at different angles),
    /// then draws what's left for every camera. Handing the cull these planes instead keeps
    /// foliage alive across the side screens and a little past their outer edges.
    ///
    /// Left and right bound the yaw (x over z); bottom and top bound the pitch (y over z), as
    /// planes holding the eye's x axis. A plane is only given where the view fits on one side of
    /// it: a view 180° or more wide, or reaching behind the eye, leaves that pair unbounded.
    /// </summary>
    public static class CullPlanes
    {
        /// <summary>
        /// Inward normals, in eye space, of the left, right, bottom and top planes around the
        /// directions to the given points, widened by the margin. Null where no plane can bound them.
        /// </summary>
        public static Vec3?[] Around(IEnumerable<Vec3> points, double marginDeg)
        {
            double margin = marginDeg * Math.PI / 180;
            double yawMin = double.MaxValue, yawMax = double.MinValue;
            double pitchMin = double.MaxValue, pitchMax = double.MinValue;
            bool behind = false, any = false;
            foreach (Vec3 p in points)
            {
                any = true;
                double yaw = Math.Atan2(p.X, p.Z);
                yawMin = Math.Min(yawMin, yaw);
                yawMax = Math.Max(yawMax, yaw);
                if (p.Z <= 0)
                {
                    behind = true;
                    continue;
                }
                double pitch = Math.Atan2(p.Y, p.Z);
                pitchMin = Math.Min(pitchMin, pitch);
                pitchMax = Math.Max(pitchMax, pitch);
            }
            var planes = new Vec3?[4];
            if (!any) return planes;

            yawMin -= margin;
            yawMax += margin;
            if (yawMax - yawMin < Math.PI)
            {
                planes[0] = new Vec3(Math.Cos(yawMin), 0, -Math.Sin(yawMin));
                planes[1] = new Vec3(-Math.Cos(yawMax), 0, Math.Sin(yawMax));
            }
            pitchMin -= margin;
            pitchMax += margin;
            if (!behind && pitchMin > -Math.PI / 2 && pitchMax < Math.PI / 2)
            {
                planes[2] = new Vec3(0, Math.Cos(pitchMin), -Math.Sin(pitchMin));
                planes[3] = new Vec3(0, -Math.Cos(pitchMax), Math.Sin(pitchMax));
            }
            return planes;
        }

        /// <summary>Whether a direction lies inside every plane given.</summary>
        public static bool Inside(Vec3?[] planes, Vec3 direction)
        {
            foreach (Vec3? plane in planes)
                if (plane.HasValue && Vec3.Dot(plane.Value, direction) < 0) return false;
            return true;
        }
    }
}
