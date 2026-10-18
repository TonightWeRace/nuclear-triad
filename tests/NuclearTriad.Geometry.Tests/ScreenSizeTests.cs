using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class ScreenSizeTests
    {
        [Fact]
        public void DevMachine_ReportsAreTakenAsTheyAre()
        {
            // The dev machine's monitors: Windows reports the portrait sides portrait.
            Assert.Equal((697.0, 392.0), ScreenSize.Oriented(697, 392, 2560, 1440));
            Assert.Equal((392.0, 697.0), ScreenSize.Oriented(392, 697, 1440, 2560));
            Assert.Equal(697, ScreenSize.DefaultWidthMm(697, 2560));
            Assert.Equal(392, ScreenSize.DefaultWidthMm(392, 1440));
        }

        [Fact]
        public void AReportTheWrongWayRound_IsTurnedToMatchThePixels()
        {
            Assert.Equal((392.0, 697.0), ScreenSize.Oriented(697, 392, 1440, 2560));
            Assert.Equal((697.0, 392.0), ScreenSize.Oriented(392, 697, 2560, 1440));
        }

        [Fact]
        public void SquareOrUnknown_IsLeftAlone()
        {
            Assert.Equal((500.0, 500.0), ScreenSize.Oriented(500, 500, 1440, 2560));
            Assert.Equal((697.0, 392.0), ScreenSize.Oriented(697, 392, 0, 0));
            Assert.Equal((0.0, 0.0), ScreenSize.Oriented(0, 0, 2560, 1440));
        }

        [Fact]
        public void Ultrawide_StartsAtItsReportedWidth()
        {
            // Alienware AW3425DWM, a 34" 1500R ultrawide: 3440 px over about 797 mm.
            Assert.True(ScreenSize.Plausible(797, 3440));
            Assert.Equal(797, ScreenSize.DefaultWidthMm(797, 3440));
            Assert.Equal(797, ScreenSize.DefaultWidthMm(797.4, 3440));
        }

        [Fact]
        public void NoReport_GuessesFromThePixels()
        {
            Assert.False(ScreenSize.Plausible(0, 3440));
            Assert.Equal(860, ScreenSize.DefaultWidthMm(0, 3440));
            Assert.Equal(640, ScreenSize.DefaultWidthMm(0, 2560));
        }

        [Fact]
        public void AnAspectRatioInPlaceOfASize_IsNotBelieved()
        {
            // Some monitors put 16 x 9 (cm) where the size goes; Windows then says 160 mm.
            Assert.False(ScreenSize.Plausible(160, 3440));
            Assert.Equal(860, ScreenSize.DefaultWidthMm(160, 3440));
            // A TV-sized report for a laptop's pixels is equally wrong.
            Assert.False(ScreenSize.Plausible(1600, 1366));
        }

        [Fact]
        public void NothingKnown_FallsBackToARoundNumber()
        {
            Assert.Equal(600, ScreenSize.DefaultWidthMm(0, 0));
            Assert.Equal(697, ScreenSize.DefaultWidthMm(697, 0));
        }

        [Fact]
        public void Ruler_FitsTheScreen()
        {
            Assert.Equal(300, ScreenSize.RulerMm(797));
            Assert.Equal(300, ScreenSize.RulerMm(697));
            Assert.Equal(200, ScreenSize.RulerMm(392));
            Assert.Equal(100, ScreenSize.RulerMm(250));
        }
    }
}
