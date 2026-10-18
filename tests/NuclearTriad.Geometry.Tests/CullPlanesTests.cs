using System;
using System.Collections.Generic;
using System.Linq;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class CullPlanesTests
    {
        static List<Vec3> Corners(IEnumerable<Panel> panels) =>
            panels.SelectMany(p => new[] { p.LowerLeft, p.LowerRight, p.UpperLeft, p.UpperRight }).ToList();

        static Vec3 Direction(double yawDeg, double pitchDeg)
        {
            double yaw = yawDeg * Math.PI / 180, pitch = pitchDeg * Math.PI / 180;
            return new Vec3(Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch));
        }

        static double Yaw(Vec3 v) => Math.Atan2(v.X, v.Z) * 180 / Math.PI;

        [Fact]
        public void HoldsEveryScreen_AndTheMarginPastTheOuterEdges()
        {
            // The dev rig as tuned: 35° toe-in, game-FOV eye distance.
            List<Panel> panels = Layout.Build(Rig(35));
            List<Vec3> corners = Corners(panels);
            Vec3?[] planes = CullPlanes.Around(corners, 10);

            Assert.All(planes, p => Assert.True(p.HasValue));
            foreach (Vec3 c in corners) Assert.True(CullPlanes.Inside(planes, c.Normalized), $"corner {c} culled");

            // Level, out past each outer edge: kept up to the margin, culled beyond it.
            double outerLeft = corners.Min(Yaw), outerRight = corners.Max(Yaw);
            Assert.True(CullPlanes.Inside(planes, Direction(outerLeft - 9.5, 0)));
            Assert.False(CullPlanes.Inside(planes, Direction(outerLeft - 10.5, 0)));
            Assert.True(CullPlanes.Inside(planes, Direction(outerRight + 9.5, 0)));
            Assert.False(CullPlanes.Inside(planes, Direction(outerRight + 10.5, 0)));
            // Straight down and straight behind stay culled.
            Assert.False(CullPlanes.Inside(planes, new Vec3(0, -1, 0)));
            Assert.False(CullPlanes.Inside(planes, new Vec3(0, 0, -1)));
        }

        [Fact]
        public void NoMargin_IsTightOnTheOutermostCorners()
        {
            List<Vec3> corners = Corners(Layout.Build(Rig(15)));
            Vec3?[] planes = CullPlanes.Around(corners, 0);

            foreach (Vec3 c in corners)
            {
                double slack = planes.Where(p => p.HasValue).Min(p => Vec3.Dot(p.Value, c.Normalized));
                Assert.True(slack >= -1e-12, $"corner {c} outside by {slack}");
            }
            // Each plane touches at least one corner.
            foreach (Vec3? plane in planes)
                Assert.Contains(corners, c => Math.Abs(Vec3.Dot(plane.Value, c.Normalized)) < 1e-9);
        }

        [Fact]
        public void AViewTooWideForOnePlanePair_IsLeftUnbounded()
        {
            // 80° toe-in puts the outer edges well behind the eye's sideways line.
            Vec3?[] planes = CullPlanes.Around(Corners(Layout.Build(Rig(80))), 10);

            Assert.Null(planes[0]);
            Assert.Null(planes[1]);
            Assert.Null(planes[2]);
            Assert.Null(planes[3]);
        }

        static LayoutSpec Rig(double angle) => new LayoutSpec
        {
            Center = new ScreenSpec { Name = "center", WidthMm = 697, PixelWidth = 2560, PixelHeight = 1440 },
            Left = { new ScreenSpec { Name = "left", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = angle, GapMm = 50, VerticalOffsetMm = -5.5 } },
            Right = { new ScreenSpec { Name = "right", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = angle, GapMm = 75, VerticalOffsetMm = 9 } },
            EyeDistanceMm = 420,
        };
    }
}
