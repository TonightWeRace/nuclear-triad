using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class DisplayRowTests
    {
        // The dev machine today: portrait side screens moved to line up by eye, the
        // landscape center the primary, and Windows listing the primary first.
        static readonly PixelRect DevCenter = new PixelRect(0, 0, 2560, 1440);
        static readonly PixelRect DevLeft = new PixelRect(-1440, -535, 0, 2025);
        static readonly PixelRect DevRight = new PixelRect(2560, -598, 4000, 1962);

        [Fact]
        public void DevRig_FindsEachScreenWhereverWindowsListsIt()
        {
            AssertRow(1, 0, 2, DevCenter, DevLeft, DevRight);
            AssertRow(0, 1, 2, DevLeft, DevCenter, DevRight);
            AssertRow(2, 0, 1, DevCenter, DevRight, DevLeft);
        }

        [Fact]
        public void ThreeLandscapeScreens_GoLeftToRightWhicheverIsPrimary()
        {
            // Displays numbered left to right, with the left one primary, so the origin is on it.
            var left = new PixelRect(0, 0, 2560, 1440);
            var center = new PixelRect(2560, 0, 5120, 1440);
            var right = new PixelRect(5120, 0, 7680, 1440);
            AssertRow(1, 2, 0, right, left, center);
        }

        [Fact]
        public void ADisplayBelowTheRow_DoesntGetInTheWay()
        {
            var dash = new PixelRect(880, 1440, 1680, 1920);
            AssertRow(1, 0, 3, DevCenter, DevLeft, dash, DevRight);
        }

        [Fact]
        public void FourInARow_IsNotGuessed()
        {
            Assert.Null(DisplayRow.Find(new[]
            {
                new PixelRect(-2560, 0, 0, 1440), new PixelRect(0, 0, 2560, 1440),
                new PixelRect(2560, 0, 5120, 1440), new PixelRect(5120, 0, 7680, 1440),
            }));
        }

        [Fact]
        public void FewerThanThreeDisplays_AreNotGuessed()
        {
            Assert.Null(DisplayRow.Find(new PixelRect[0]));
            Assert.Null(DisplayRow.Find(new[] { new PixelRect(0, 0, 7680, 1440) }));
            Assert.Null(DisplayRow.Find(new[] { DevCenter, DevLeft }));
        }

        [Fact]
        public void DisplaysStackedVertically_AreNotARow()
        {
            Assert.Null(DisplayRow.Find(new[]
            {
                new PixelRect(0, -1440, 2560, 0), DevCenter, new PixelRect(0, 1440, 2560, 2880),
            }));
        }

        [Fact]
        public void TouchingOnlyAtACorner_IsNotBeside()
        {
            // The left display ends where the center's rows begin: no shared row.
            var cornerLeft = new PixelRect(-2560, -1440, 0, 0);
            Assert.Null(DisplayRow.Find(new[] { cornerLeft, DevCenter, new PixelRect(2560, 0, 5120, 1440) }));
        }

        [Fact]
        public void TwoDisplaysOnOneSide_AreNotGuessed()
        {
            var upperLeft = new PixelRect(-1440, -1000, 0, 700);
            var lowerLeft = new PixelRect(-1440, 700, 0, 2400);
            Assert.Null(DisplayRow.Find(new[] { upperLeft, lowerLeft, DevCenter, DevRight }));
        }

        [Fact]
        public void APixelOfRounding_StillTouches_ButARealGapDoesNot()
        {
            AssertRow(0, 1, 2, new PixelRect(-1441, -535, -1, 2025), DevCenter, new PixelRect(2559, -598, 3999, 1962));
            Assert.Null(DisplayRow.Find(new[] { new PixelRect(-1450, -535, -10, 2025), DevCenter, DevRight }));
        }

        [Fact]
        public void InOrder_OnlyLeftCenterRight()
        {
            Assert.True(DisplayRow.InOrder(DevLeft, DevCenter, DevRight));
            // The default trap: Monitor naming the center display as the left screen, and so on.
            Assert.False(DisplayRow.InOrder(DevCenter, DevLeft, DevRight));
            Assert.False(DisplayRow.InOrder(DevLeft, DevRight, DevCenter));
            Assert.False(DisplayRow.InOrder(DevRight, DevCenter, DevLeft));
        }

        static void AssertRow(int left, int center, int right, params PixelRect[] displays)
        {
            var row = DisplayRow.Find(displays);
            Assert.True(row.HasValue, "no row found");
            Assert.Equal(left, row.Value.Left);
            Assert.Equal(center, row.Value.Center);
            Assert.Equal(right, row.Value.Right);
        }
    }
}
