using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class MenuFitTests
    {
        [Fact]
        public void CanvasScale_ByWidth_GrowsWithTheSpannedWindow_AsTheBindingsScreenDid()
        {
            // Rewired's control mapper: reference 2110 x 600, matched on width.
            Assert.Equal(5440.0 / 2110, MenuFit.CanvasScale(5440, 1440, 2110, 600, ScreenMatch.MatchWidthOrHeight, 0), 9);
            Assert.Equal(2560.0 / 2110, MenuFit.CanvasScale(2560, 1440, 2110, 600, ScreenMatch.MatchWidthOrHeight, 0), 9);
        }

        [Fact]
        public void CanvasScale_ByHeight_IsTheSameSpannedOrNot()
        {
            Assert.Equal(1440.0 / 1080, MenuFit.CanvasScale(5440, 1440, 1920, 1080, ScreenMatch.MatchWidthOrHeight, 1), 9);
            Assert.Equal(1440.0 / 1080, MenuFit.CanvasScale(2560, 1440, 1920, 1080, ScreenMatch.MatchWidthOrHeight, 1), 9);
        }

        [Fact]
        public void CanvasScale_BlendsWidthAndHeightLogarithmically()
        {
            // Halfway between 2x by width and 0.5x by height is 1x.
            Assert.Equal(1, MenuFit.CanvasScale(2000, 500, 1000, 1000, ScreenMatch.MatchWidthOrHeight, 0.5), 9);
        }

        [Fact]
        public void CanvasScale_ExpandAndShrink_TakeTheSmallerAndLargerRatio()
        {
            // The main menu: reference 1920 x 1080, Expand.
            Assert.Equal(1440.0 / 1080, MenuFit.CanvasScale(5440, 1440, 1920, 1080, ScreenMatch.Expand, 0.25), 9);
            Assert.Equal(5440.0 / 1920, MenuFit.CanvasScale(5440, 1440, 1920, 1080, ScreenMatch.Shrink, 0), 9);
        }

        [Theory]
        [InlineData(0, 0.2647)]
        [InlineData(1, 0.7353)]
        [InlineData(0.5, 0.5)]
        public void Into_PutsTheWindowsEdgesOnTheBandsEdges_AndKeepsTheMiddle(double anchor, double expected)
        {
            // The dev rig's center screen: 1440 to 4000 of a 5440 px window.
            Assert.Equal(expected, MenuFit.Into(anchor, 1440.0 / 5440, 2560.0 / 5440), 4);
        }
    }
}
