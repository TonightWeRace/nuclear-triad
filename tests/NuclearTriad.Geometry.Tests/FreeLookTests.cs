using System;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class FreeLookTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(33, 0)]
        [InlineData(66.6667, 0.1)]
        [InlineData(100, 0.2)]
        [InlineData(165, 0.2)]
        [InlineData(-100, -0.2)]
        public void GameSlide_StartsAt33AndIsFullAt100(double pan, double metres) =>
            Assert.Equal(metres, FreeLook.GameSlide(pan), 4);

        [Fact]
        public void HeldSlide_IsNoneWithinReach_ThenTheGamesRampFromThere()
        {
            Assert.Equal(0, FreeLook.HeldSlide(60, 76, 77), 9);
            Assert.Equal(0, FreeLook.HeldSlide(76, 76, 77), 9);
            Assert.Equal(0.1, FreeLook.HeldSlide(76 + 100 / 3.0, 76, 77), 6);
            Assert.Equal(0.2, FreeLook.HeldSlide(165, 76, 77), 9);
            // Left uses the left reach.
            Assert.Equal(0, FreeLook.HeldSlide(-77, 76, 77), 9);
            Assert.True(FreeLook.HeldSlide(-90, 76, 77) < 0);
        }

        [Fact]
        public void Reach_IsWhereTheNoseLeavesTheOuterEdge()
        {
            // The dev rig at 35°, rendered for game-FOV framing's eye at about 420 mm.
            var panels = Layout.Build(new LayoutSpec
            {
                Center = new ScreenSpec { Name = "center", WidthMm = 697, PixelWidth = 2560, PixelHeight = 1440 },
                Left = { new ScreenSpec { Name = "left", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = 35, GapMm = 50 } },
                Right = { new ScreenSpec { Name = "right", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = 35, GapMm = 75 } },
                EyeDistanceMm = 420,
            });
            var (right, left) = FreeLook.Reach(panels);
            Assert.InRange(right, 74, 78);
            Assert.InRange(left, 75, 79);
        }
    }
}
