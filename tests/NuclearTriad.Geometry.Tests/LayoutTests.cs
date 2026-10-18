using System;
using System.Linq;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class LayoutTests
    {
        // A 31.5" 16:9 panel's lit width, at a plausible eye distance.
        const double W = 697;
        const double D = 750;

        [Fact]
        public void FlatTripleWithNoGaps_IsOneWideSymmetricFrustumSplitInThree()
        {
            var panels = Layout.Build(Triple(W, angle: 0, gap: 0, D));
            Frustum[] f = panels.Select(p => p.FrustumFrom(Vec3.Zero)).ToArray();
            double halfHeight = W * 1440 / 2560 / 2 / D;

            Assert.Equal(-1.5 * W / D, f[0].Left, 9);
            Assert.Equal(f[0].Right, f[1].Left, 9);
            Assert.Equal(f[1].Right, f[2].Left, 9);
            Assert.Equal(1.5 * W / D, f[2].Right, 9);
            Assert.True(f[1].IsSymmetric(1e-12), f[1].ToString());
            foreach (Frustum x in f)
            {
                Assert.Equal(-halfHeight, x.Bottom, 9);
                Assert.Equal(halfHeight, x.Top, 9);
                Assert.Equal(D, x.DistanceMm, 9);
            }
            foreach (Panel p in panels) AssertVec(Vec3.Forward, p.Forward);
        }

        [Fact]
        public void ToeInOfOnePanelsAngularWidth_GivesSymmetricSideFrusta()
        {
            double angle = 2 * Math.Atan(W / 2 / D) * 180 / Math.PI;
            var panels = Layout.Build(Triple(W, angle, gap: 0, D));
            Frustum center = panels[1].FrustumFrom(Vec3.Zero);

            foreach (Panel side in new[] { panels[0], panels[2] })
            {
                Frustum f = side.FrustumFrom(Vec3.Zero);
                Assert.True(f.IsSymmetric(1e-9), $"{side.Name}: {f}");
                Assert.Equal(center.Right, f.Right, 9);
                Assert.Equal(center.Top, f.Top, 9);
                Assert.Equal(D, f.DistanceMm, 6);
            }
            Assert.Equal(-angle, YawDeg(panels[0].Forward), 9);
            Assert.Equal(angle, YawDeg(panels[2].Forward), 9);
        }

        [Theory]
        [InlineData(0, 25)]
        [InlineData(30, 25)]
        [InlineData(45, 12.5)]
        [InlineData(60, 40)]
        public void Gap_IsTheStraightLineBetweenEdgePixels(double angle, double gap)
        {
            var panels = Layout.Build(Triple(W, angle, gap, D));

            Assert.Equal(gap, (panels[2].LowerLeft - panels[1].LowerRight).Length, 9);
            Assert.Equal(gap, (panels[1].LowerLeft - panels[0].LowerRight).Length, 9);
            Assert.Equal(gap, (panels[2].UpperLeft - panels[1].UpperRight).Length, 9);
        }

        [Fact]
        public void NoGap_SeamEdgesCoincide()
        {
            var panels = Layout.Build(Triple(W, angle: 40, gap: 0, D));

            AssertVec(panels[1].LowerRight, panels[2].LowerLeft);
            AssertVec(panels[1].UpperRight, panels[2].UpperLeft);
            AssertVec(panels[1].LowerLeft, panels[0].LowerRight);
            AssertVec(panels[1].UpperLeft, panels[0].UpperRight);
        }

        [Fact]
        public void SymmetricRig_GivesMirroredSideFrusta()
        {
            var panels = Layout.Build(Triple(W, angle: 35, gap: 18, D));
            Frustum left = panels[0].FrustumFrom(Vec3.Zero);
            Frustum right = panels[2].FrustumFrom(Vec3.Zero);

            Assert.Equal(-right.Right, left.Left, 9);
            Assert.Equal(-right.Left, left.Right, 9);
            Assert.Equal(right.Bottom, left.Bottom, 9);
            Assert.Equal(right.Top, left.Top, 9);
            Assert.Equal(right.DistanceMm, left.DistanceMm, 9);
        }

        [Fact]
        public void OffCenterEye_SkewsTheCenterFrustum()
        {
            LayoutSpec spec = Triple(W, angle: 0, gap: 0, D);
            spec.EyeSideMm = 50;
            spec.EyeHeightMm = 20;
            Frustum c = Layout.Build(spec)[1].FrustumFrom(Vec3.Zero);
            double h = W * 1440 / 2560;

            Assert.Equal((-W / 2 - 50) / D, c.Left, 9);
            Assert.Equal((W / 2 - 50) / D, c.Right, 9);
            Assert.Equal((-h / 2 - 20) / D, c.Bottom, 9);
            Assert.Equal((h / 2 - 20) / D, c.Top, 9);
        }

        [Fact]
        public void PortraitSides_TakeTheirHeightFromTheirPixels_AndCenterOnTheMiddle()
        {
            var spec = new LayoutSpec
            {
                Center = Screen("center", 697, 2560, 1440),
                Left = { Screen("left", 392, 1440, 2560, angle: 40, gap: 20) },
                Right = { Screen("right", 392, 1440, 2560, angle: 40, gap: 20, vertical: 20) },
                EyeDistanceMm = 750,
            };
            var panels = Layout.Build(spec);

            Assert.Equal(392 * 2560.0 / 1440, panels[0].Height, 9);
            Assert.Equal(697 * 1440.0 / 2560, panels[1].Height, 9);
            // An offset of 0 centers a screen on the center screen's middle; positive raises it.
            Assert.Equal(panels[1].Center.Y, panels[0].Center.Y, 9);
            Assert.Equal(panels[1].Center.Y + 20, panels[2].Center.Y, 9);
        }

        [Fact]
        public void DesktopVerticalOffset_ReadsTheWindowsArrangement()
        {
            // The dev rig: both portrait screens sit a little below centered in Windows.
            const double pitch = 697.0 / 2560;
            var center = new PixelRect(0, 0, 2560, 1440);
            Assert.Equal(-50 * pitch, Layout.DesktopVerticalOffsetMm(new PixelRect(-1440, -510, 0, 2050), center, pitch), 9);
            Assert.Equal(-34 * pitch, Layout.DesktopVerticalOffsetMm(new PixelRect(2560, -526, 4000, 2034), center, pitch), 9);
            Assert.Equal(0, Layout.DesktopVerticalOffsetMm(new PixelRect(2560, -560, 4000, 2000), center, pitch), 9);
        }

        [Fact]
        public void BandOffset_ComparesTheArrangementWithTheRealHeight()
        {
            // The dev rig, side screens really +5 mm. In the original arrangement the band sat high
            // on both side screens; after they were moved up in Windows, it sat low.
            const double pitch = 392.0 / 1440;
            var center = new PixelRect(0, 0, 2560, 1440);
            Assert.Equal(18.6, Layout.BandOffsetMm(5, new PixelRect(-1440, -510, 0, 2050), center, pitch), 1);
            Assert.Equal(-7.8, Layout.BandOffsetMm(5, new PixelRect(-1440, -607, 0, 1953), center, pitch), 1);
            Assert.Equal(-7.0, Layout.BandOffsetMm(5, new PixelRect(2560, -604, 4000, 1956), center, pitch), 1);
            // Level when the arrangement matches the real height: top edge at 720 − 5 mm − 1280.
            Assert.Equal(0, Layout.BandOffsetMm(5, new PixelRect(-1440, -578, 0, 1982), center, pitch), 0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(40)]
        public void Projection_PutsEachPanelCornerOnTheMatchingClipCorner(double angle)
        {
            LayoutSpec spec = Triple(W, angle, gap: 15, D);
            spec.EyeSideMm = 30;
            spec.EyeHeightMm = -25;

            foreach (Panel p in Layout.Build(spec))
            {
                double[,] m = p.FrustumFrom(Vec3.Zero).Matrix(0.3, 50000);
                AssertNdc(-1, -1, Project(m, p, p.LowerLeft), p.Name);
                AssertNdc(1, -1, Project(m, p, p.LowerRight), p.Name);
                AssertNdc(-1, 1, Project(m, p, p.UpperLeft), p.Name);
                AssertNdc(1, 1, Project(m, p, p.UpperRight), p.Name);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(+1)]
        public void TryBuild_RejectsASideScreenTurnedPastTheEye(int direction)
        {
            // With no gap a side screen hinges on the center screen's edge, so the eye stays in
            // front of it until it turns past the line from the eye through that edge: at 180°
            // less that line's angle off the center screen toward the eye, or that angle away.
            double edge = Math.Atan(D / (W / 2)) * 180 / Math.PI;
            double limit = direction > 0 ? 180 - edge : -edge;

            Assert.True(Layout.TryBuild(Triple(W, limit - direction * 0.1, gap: 0, D), out var panels, out string problem), problem);
            Assert.Equal(3, panels.Count);
            Assert.False(Layout.TryBuild(Triple(W, limit + direction * 0.1, gap: 0, D), out panels, out problem));
            Assert.Null(panels);
            Assert.Equal("the eye isn't in front of the left screen", problem);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(500)]
        public void TryBuild_TakesASideScreenSquareToTheCenter_AtAnyEyeDistanceAndGap(double gap)
        {
            foreach (double distance in new[] { 1.0, D, 5000 })
                Assert.True(Layout.TryBuild(Triple(W, angle: 90, gap, distance), out _, out string problem), problem);
        }

        [Fact]
        public void TryBuild_ExplainsWhatCantBeBuilt_WithoutThrowing()
        {
            Assert.False(Layout.TryBuild(Triple(W, angle: 15, gap: 50, distance: 0), out _, out string problem));
            Assert.Equal("the eye distance isn't positive", problem);

            LayoutSpec spec = Triple(W, angle: 15, gap: 50, D);
            spec.Right[0].WidthMm = 0;
            Assert.False(Layout.TryBuild(spec, out _, out problem));
            Assert.Equal("the right screen has no size", problem);

            spec = Triple(W, angle: 15, gap: 50, D);
            spec.Left[0].AngleDeg = double.NaN;
            Assert.False(Layout.TryBuild(spec, out _, out problem));
            Assert.Equal("the eye isn't in front of the left screen", problem);
        }

        static LayoutSpec Triple(double width, double angle, double gap, double distance) => new LayoutSpec
        {
            Center = Screen("center", width),
            Left = { Screen("left", width, angle: angle, gap: gap) },
            Right = { Screen("right", width, angle: angle, gap: gap) },
            EyeDistanceMm = distance,
        };

        static ScreenSpec Screen(string name, double width, int pixelWidth = 2560, int pixelHeight = 1440,
            double angle = 0, double gap = 0, double vertical = 0) => new ScreenSpec
        {
            Name = name,
            WidthMm = width,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            AngleDeg = angle,
            GapMm = gap,
            VerticalOffsetMm = vertical,
        };

        static double YawDeg(Vec3 forward) => Math.Atan2(forward.X, forward.Z) * 180 / Math.PI;

        // Into the camera's frame, whose axes are the panel's, then into Unity's view space, which
        // looks down −Z, then through the projection. The eye is at the origin.
        static (double X, double Y) Project(double[,] m, Panel p, Vec3 point)
        {
            double[] view = { Vec3.Dot(point, p.Right), Vec3.Dot(point, p.Up), -Vec3.Dot(point, p.Forward), 1 };
            var clip = new double[4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    clip[r] += m[r, c] * view[c];
            return (clip[0] / clip[3], clip[1] / clip[3]);
        }

        static void AssertNdc(double x, double y, (double X, double Y) actual, string panel)
        {
            Assert.True(Math.Abs(actual.X - x) < 1e-9 && Math.Abs(actual.Y - y) < 1e-9,
                $"{panel}: expected NDC ({x}, {y}), got ({actual.X}, {actual.Y})");
        }

        static void AssertVec(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
    }
}
