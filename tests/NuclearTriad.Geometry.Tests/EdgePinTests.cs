using System;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class EdgePinTests
    {
        // The dev rig's spanned window, in pixels.
        const double W = 5440, H = 1440;

        [Theory]
        [InlineData(9000, 720)]
        [InlineData(-3000, 2500)]
        [InlineData(2720, -4000)]
        [InlineData(6000, 1500)]
        public void Pin_LandsOnTheWindowsEdge_OnTheRayFromTheCenterTowardThePoint(double px, double py)
        {
            foreach ((double cx, double cy) in new[] { (W / 2, H / 2), (W / 2, H / 2 + 184), (2000.0, 300.0) })
            {
                (double x, double y, double angle) = EdgePin.Pin(px, py, cx, cy, W, H);

                Assert.True(Near(x, 0) || Near(x, W) || Near(y, 0) || Near(y, H), $"({x}, {y}) isn't on the window's edge");
                Assert.InRange(x, -1e-6, W + 1e-6);
                Assert.InRange(y, -1e-6, H + 1e-6);
                Assert.Equal(Math.Atan2(py - cy, px - cx), angle, 12);
                Assert.Equal(angle, Math.Atan2(y - cy, x - cx), 9);
            }
        }

        [Fact]
        public void Pin_FromTheWindowsMiddle_IsTheGamesOwnPin()
        {
            // The game's pin, from the middle: out along the angle to a side edge, unless that
            // passes the top or bottom first.
            double cx = W / 2, cy = H / 2;
            (double x, double y, _) = EdgePin.Pin(9000, 900, cx, cy, W, H);
            Assert.Equal(W, x, 9);
            Assert.Equal(cy + cx * (900 - cy) / (9000 - cx), y, 9);
        }

        [Fact]
        public void Pin_FromAHigherStraightAhead_KeepsALevelTargetsArrowLevel()
        {
            // A target level with straight ahead and far off to the right is pinned at that height,
            // not tilted toward the window's middle.
            double cy = H / 2 + 184;
            (double x, double y, double angle) = EdgePin.Pin(9000, cy, W / 2, cy, W, H);
            Assert.Equal(W, x, 9);
            Assert.Equal(cy, y, 9);
            Assert.Equal(0, angle, 12);
        }

        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
    }
}
