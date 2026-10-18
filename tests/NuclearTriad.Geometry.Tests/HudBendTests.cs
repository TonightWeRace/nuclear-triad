using System;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class HudBendTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(25)]
        [InlineData(-140)]
        public void NoChange_LeavesTheGroupAlone(double rotation)
        {
            var (r, sx, sy, shear) = HudBend.Fit(new Linear2(1, 0, 0, 1), rotation);
            Assert.Equal(rotation, r, 9);
            Assert.Equal(1, sx, 9);
            Assert.Equal(1, sy, 9);
            Assert.Equal(0, shear, 9);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(30)]
        [InlineData(-75)]
        public void ARotationAndScaleAlongTheGroupsAxes_IsFitExactly(double rotation)
        {
            // Turn the group 12° more and stretch it 1.3 along its own x and 0.8 along its y.
            Linear2 map = Linear2.Rotation(rotation + 12) * new Linear2(1.3, 0, 0, 0.8) * Linear2.Rotation(-rotation);
            var (r, sx, sy, shear) = HudBend.Fit(map, rotation);
            Assert.Equal(rotation + 12, r, 9);
            Assert.Equal(1.3, sx, 9);
            Assert.Equal(0.8, sy, 9);
            Assert.Equal(0, shear, 9);
        }

        [Fact]
        public void PhysicalScale_IsOneAtTheReferenceMiddle_AndSmallOnAWingFacingTheEye()
        {
            // The dev rig as set up: side screens at 35°, the real eye 800 mm away.
            var panels = Layout.Build(Rig(eye: 800));
            const double centerPxPerMm = 2560 / 697.0, sidePxPerMm = 1440 / 392.0;

            Assert.Equal(1, HudBend.PhysicalScale(panels[1], 0.5, 0.5, centerPxPerMm, panels[1], centerPxPerMm), 9);
            // At 35° the wing all but faces the real eye, from a little further away: barely bigger.
            double middle = HudBend.PhysicalScale(panels[2], 0.5, 0.5, sidePxPerMm, panels[1], centerPxPerMm);
            Assert.InRange(middle, 1.05, 1.15);
            // Seen from game-FOV framing's virtual eye at about 420 mm, the same spot needs much more:
            // why matching the rendering made the HUD jump in size on the wing.
            var virtualPanels = Layout.Build(Rig(eye: 420));
            double rendered = HudBend.PhysicalScale(virtualPanels[2], 0.5, 0.5, sidePxPerMm, virtualPanels[1], centerPxPerMm);
            Assert.InRange(rendered, 1.4, 2.0);
        }

        static LayoutSpec Rig(double eye) => new LayoutSpec
        {
            Center = new ScreenSpec { Name = "center", WidthMm = 697, PixelWidth = 2560, PixelHeight = 1440 },
            Left = { new ScreenSpec { Name = "left", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = 35, GapMm = 50 } },
            Right = { new ScreenSpec { Name = "right", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = 35, GapMm = 75 } },
            EyeDistanceMm = eye,
        };

        [Fact]
        public void AShear_KeepsTheGroupsXAxisExact_AndReportsWhatItDrops()
        {
            // A wing-screen-like map: horizontal shrinks, vertical lines stay vertical, and
            // horizontal lines tilt (a lower-triangular map).
            var map = new Linear2(0.82, 0, 0.21, 1.05);
            var (r, sx, sy, shear) = HudBend.Fit(map, 0);

            // The group's x axis lands exactly where the map sends it.
            double a = r * Math.PI / 180;
            Assert.Equal(0.82, sx * Math.Cos(a), 9);
            Assert.Equal(0.21, sx * Math.Sin(a), 9);
            // Its y axis keeps the map's spread across it; what's left is a shear, reported.
            Assert.Equal(map.Determinant, sx * sy, 9);
            Assert.True(Math.Abs(shear) > 0.1, $"shear {shear}");
        }
    }
}
