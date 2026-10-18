using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>One screen as the user describes it, the way iRacing and Assetto Corsa ask.</summary>
    public sealed class ScreenSpec
    {
        public string Name = "";

        /// <summary>Width of the lit image.</summary>
        public double WidthMm;

        /// <summary>The screen's current resolution. With square pixels it sets the height.</summary>
        public int PixelWidth;
        public int PixelHeight;

        /// <summary>Angle off the center screen's plane; positive turns the screen toward the eye.</summary>
        public double AngleDeg;

        /// <summary>Straight across, from this screen's edge pixel to its inner neighbour's.</summary>
        public double GapMm;

        /// <summary>This screen's middle relative to the center screen's middle, positive higher; 0 centers it.</summary>
        public double VerticalOffsetMm;

        public double HeightMm => WidthMm * PixelHeight / PixelWidth;
    }

    /// <summary>The whole rig: a center screen, any number of screens either side, and the eye.</summary>
    public sealed class LayoutSpec
    {
        public ScreenSpec Center = new ScreenSpec { Name = "center" };

        /// <summary>Screens left of center, inner to outer.</summary>
        public List<ScreenSpec> Left = new List<ScreenSpec>();

        /// <summary>Screens right of center, inner to outer.</summary>
        public List<ScreenSpec> Right = new List<ScreenSpec>();

        /// <summary>Eye to the center screen, perpendicular.</summary>
        public double EyeDistanceMm;

        /// <summary>Eye height above the middle of the center image.</summary>
        public double EyeHeightMm;

        /// <summary>Eye offset right of the middle of the center image.</summary>
        public double EyeSideMm;
    }

    public static class Layout
    {
        /// <summary>Every panel in eye space, left to right.</summary>
        public static List<Panel> Build(LayoutSpec spec)
        {
            ScreenSpec c = spec.Center;
            if (spec.EyeDistanceMm <= 0) throw new ArgumentException("The eye distance must be positive.");

            // The center screen sits square to the eye, its middle shifted by the eye offsets.
            double middleX = -spec.EyeSideMm;
            double bottom = -spec.EyeHeightMm - c.HeightMm / 2;
            double z = spec.EyeDistanceMm;
            var center = new Panel(c.Name,
                new Vec3(middleX - c.WidthMm / 2, bottom, z),
                new Vec3(middleX + c.WidthMm / 2, bottom, z),
                new Vec3(middleX - c.WidthMm / 2, bottom + c.HeightMm, z));

            double middleY = bottom + c.HeightMm / 2;
            List<Panel> left = Chain(spec.Left, center.LowerLeft, -1, middleY);
            left.Reverse();
            var panels = new List<Panel>(left) { center };
            panels.AddRange(Chain(spec.Right, center.LowerRight, +1, middleY));
            return panels;
        }

        /// <summary>
        /// Builds the panels, or says why the spec can't be rendered: the eye has to be in front of
        /// every screen, which a side screen turned past the eye isn't. Settings edited live pass
        /// through such states, so this doesn't throw.
        /// </summary>
        public static bool TryBuild(LayoutSpec spec, out List<Panel> panels, out string problem)
        {
            panels = null;
            problem = Unbuildable(spec);
            if (problem != null) return false;

            List<Panel> built = Build(spec);
            foreach (Panel p in built)
            {
                if (p.DistanceFrom(Vec3.Zero) > 0) continue;
                problem = $"the eye isn't in front of the {p.Name} screen";
                return false;
            }
            panels = built;
            return true;
        }

        // What Build can't make panels from. NaN fails these comparisons too.
        static string Unbuildable(LayoutSpec spec)
        {
            if (!(spec.EyeDistanceMm > 0)) return "the eye distance isn't positive";
            var screens = new List<ScreenSpec> { spec.Center };
            screens.AddRange(spec.Left);
            screens.AddRange(spec.Right);
            foreach (ScreenSpec s in screens)
                if (!(s.WidthMm > 0) || s.PixelWidth <= 0 || s.PixelHeight <= 0)
                    return $"the {s.Name} screen has no size";
            return null;
        }

        /// <summary>
        /// How far a screen's middle sits above the center screen's middle in the Windows display
        /// arrangement, in mm at the given pitch. Desktop y grows downward, so lower is negative.
        /// </summary>
        public static double DesktopVerticalOffsetMm(PixelRect screen, PixelRect center, double mmPerPixel) =>
            ((center.Top + center.Bottom) / 2.0 - (screen.Top + screen.Bottom) / 2.0) * mmPerPixel;

        /// <summary>
        /// In center-band mode, how far the band sits above (+) or below (−) level on a side screen
        /// whose middle is really verticalOffsetMm above the center's. The window covers the center
        /// screen's desktop rows, so the arrangement decides which of the side screen's rows light up.
        /// </summary>
        public static double BandOffsetMm(double verticalOffsetMm, PixelRect screen, PixelRect center, double mmPerPixel) =>
            verticalOffsetMm - DesktopVerticalOffsetMm(screen, center, mmPerPixel);

        /// <summary>
        /// The flat test layout: the Windows desktop arrangement taken as one flat sheet at the
        /// eye distance, at a uniform pixel pitch, with the eye centered on the center screen.
        /// A render of it must come out seamless in a raw capture, so it checks the software path
        /// before any physical measurement exists.
        /// </summary>
        public static LayoutSpec DesktopPlane(PixelRect left, PixelRect center, PixelRect right, double mmPerPixel, double eyeDistanceMm)
        {
            ScreenSpec Screen(string name, PixelRect r) => new ScreenSpec
            {
                Name = name,
                WidthMm = r.Width * mmPerPixel,
                PixelWidth = r.Width,
                PixelHeight = r.Height,
                VerticalOffsetMm = DesktopVerticalOffsetMm(r, center, mmPerPixel),
            };

            ScreenSpec l = Screen("left", left);
            l.GapMm = Math.Max(0, center.Left - left.Right) * mmPerPixel;
            ScreenSpec r = Screen("right", right);
            r.GapMm = Math.Max(0, right.Left - center.Right) * mmPerPixel;
            return new LayoutSpec { Center = Screen("center", center), Left = { l }, Right = { r }, EyeDistanceMm = eyeDistanceMm };
        }

        // Each screen hinges on its inner neighbour's outer edge. The gap is split evenly between
        // the two screens' planes, so the straight-line distance between the edge pixels equals
        // the measured gap; each screen's angle is measured from the center screen's plane.
        static List<Panel> Chain(List<ScreenSpec> screens, Vec3 centerEdge, int side, double centerMiddleY)
        {
            var panels = new List<Panel>();
            var edge = new Vec3(centerEdge.X, 0, centerEdge.Z);
            double innerYaw = 0;
            foreach (ScreenSpec s in screens)
            {
                double yaw = s.AngleDeg * Math.PI / 180;
                double half = s.GapMm / (2 * Math.Cos((yaw - innerYaw) / 2));
                Vec3 innerEdge = edge + Outward(innerYaw, side) * half + Outward(yaw, side) * half;
                Vec3 outerEdge = innerEdge + Outward(yaw, side) * s.WidthMm;

                double bottom = centerMiddleY + s.VerticalOffsetMm - s.HeightMm / 2;
                var innerBottom = new Vec3(innerEdge.X, bottom, innerEdge.Z);
                var outerBottom = new Vec3(outerEdge.X, bottom, outerEdge.Z);
                Vec3 up = Vec3.Up * s.HeightMm;
                panels.Add(side > 0
                    ? new Panel(s.Name, innerBottom, outerBottom, innerBottom + up)
                    : new Panel(s.Name, outerBottom, innerBottom, outerBottom + up));

                edge = outerEdge;
                innerYaw = yaw;
            }
            return panels;
        }

        // Along a screen's face, away from the center. Turning a screen toward the eye brings its
        // outer edge closer, so z shrinks as the angle grows.
        static Vec3 Outward(double yaw, int side) => new Vec3(side * Math.Cos(yaw), 0, -Math.Sin(yaw));
    }
}
