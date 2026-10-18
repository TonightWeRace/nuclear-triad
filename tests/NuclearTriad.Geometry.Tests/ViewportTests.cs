using System.Globalization;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class ViewportTests
    {
        [Fact]
        public void DevRigBoundingBoxWindow_MapsEachMonitorWhole()
        {
            // The dev machine's arrangement before any change, with the game window
            // spanning the bounding box of all three monitors.
            var client = new PixelRect(-1440, -526, 4000, 2050);
            ViewportMapping left = Viewport.Map(new PixelRect(-1440, -510, 0, 2050), client);
            ViewportMapping center = Viewport.Map(new PixelRect(0, 0, 2560, 1440), client);
            ViewportMapping right = Viewport.Map(new PixelRect(2560, -526, 4000, 2034), client);

            AssertRect(left, 0, 0, 1440 / 5440.0, 2560 / 2576.0);
            AssertRect(center, 1440 / 5440.0, 610 / 2576.0, 2560 / 5440.0, 1440 / 2576.0);
            AssertRect(right, 4000 / 5440.0, 16 / 2576.0, 1440 / 5440.0, 2560 / 2576.0);
            Assert.True(left.CoversWholeMonitor);
            Assert.True(center.CoversWholeMonitor);
            Assert.True(right.CoversWholeMonitor);
        }

        [Fact]
        public void WindowCuttingAMonitor_ShrinksItsPanelToTheVisiblePart()
        {
            // A window only as tall as the center screen cuts 560 px off the top and bottom of a
            // portrait side screen.
            var client = new PixelRect(-1440, 0, 4000, 1440);
            var monitor = new PixelRect(2560, -560, 4000, 2000);
            ViewportMapping m = Viewport.Map(monitor, client);

            Assert.Equal(0, m.U0, 12);
            Assert.Equal(1, m.U1, 12);
            Assert.Equal(560 / 2560.0, m.V0, 12);
            Assert.Equal(2000 / 2560.0, m.V1, 12);
            Assert.False(m.CoversWholeMonitor);

            var panel = new Panel("right", new Vec3(400, -350, 700), new Vec3(700, -350, 450), new Vec3(400, 347, 700));
            Panel visible = panel.Sub(m.U0, m.V0, m.U1, m.V1);
            Assert.Equal(panel.Height * 1440 / 2560, visible.Height, 9);
            Assert.Equal(panel.Width, visible.Width, 9);
            Assert.Equal(panel.LowerLeft.Y + panel.Height * 560 / 2560, visible.LowerLeft.Y, 9);
        }

        [Fact]
        public void DesktopPlane_LaysTheDevRigOutAsOneFlatSheet()
        {
            const double pitch = 697.0 / 2560;
            var left = new PixelRect(-1440, -510, 0, 2050);
            var center = new PixelRect(0, 0, 2560, 1440);
            var right = new PixelRect(2560, -526, 4000, 2034);
            var panels = Layout.Build(Layout.DesktopPlane(left, center, right, pitch, 700));

            // Coplanar, edge to edge, and placed vertically exactly as the desktop places them.
            foreach (Panel p in panels) Assert.Equal(700, p.LowerLeft.Z, 9);
            Assert.Equal(panels[1].LowerLeft.X, panels[0].LowerRight.X, 9);
            Assert.Equal(panels[1].LowerRight.X, panels[2].LowerLeft.X, 9);
            Assert.Equal(panels[1].LowerLeft.Y - 610 * pitch, panels[0].LowerLeft.Y, 9);
            Assert.Equal(panels[1].LowerLeft.Y - 594 * pitch, panels[2].LowerLeft.Y, 9);
            Assert.Equal(2560 * pitch, panels[0].Height, 9);

            // Then every desktop pixel maps to one point of the sheet, at the same pitch everywhere,
            // so the three frusta tile a single planar projection of the whole desktop.
            Frustum c = panels[1].FrustumFrom(Vec3.Zero);
            Frustum r = panels[2].FrustumFrom(Vec3.Zero);
            Assert.Equal(c.Right, r.Left, 9);
            Assert.Equal((r.Top - r.Bottom) / 2560, (c.Top - c.Bottom) / 1440, 12);
        }

        [Fact]
        public void CenterBandWindow_LeavesStripsAboveAndBelowEachPortraitScreen()
        {
            var band = new PixelRect(-1440, 0, 4000, 1440);

            var left = new PixelRect(-1440, -510, 0, 2050).Minus(band);
            Assert.Equal(new[] { new PixelRect(-1440, -510, 0, 0), new PixelRect(-1440, 1440, 0, 2050) }, left);

            var right = new PixelRect(2560, -526, 4000, 2034).Minus(band);
            Assert.Equal(new[] { new PixelRect(2560, -526, 4000, 0), new PixelRect(2560, 1440, 4000, 2034) }, right);

            Assert.Empty(new PixelRect(0, 0, 2560, 1440).Minus(band));
        }

        [Fact]
        public void Minus_CoversEverySideAndTheNoOverlapCase()
        {
            var outer = new PixelRect(0, 0, 100, 100);
            var parts = outer.Minus(new PixelRect(20, 30, 70, 60));
            Assert.Equal(4, parts.Count);
            Assert.Equal(100 * 100 - 50 * 30, Area(parts));

            Assert.Equal(new[] { outer }, outer.Minus(new PixelRect(200, 200, 300, 300)));
            Assert.Empty(outer.Minus(new PixelRect(-10, -10, 110, 110)));
        }

        [Fact]
        public void PixelRect_ParsesItsConfigForm()
        {
            Assert.True(PixelRect.TryParse(" -1440, 0,4000 , 1440", out PixelRect rect));
            Assert.Equal(new PixelRect(-1440, 0, 4000, 1440), rect);
            Assert.True(PixelRect.TryParse("+0,0,2560,1440", out rect));
            Assert.Equal(new PixelRect(0, 0, 2560, 1440), rect);
        }

        // A hand-edited cfg can hold anything. None of it may throw, and none of it is a rect.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1,2,3")]
        [InlineData("1,2,3,4,5")]
        [InlineData("0,0,,1440")]
        [InlineData("0;0;2560;1440")]
        [InlineData("0,0,2560,1440px")]
        [InlineData("0,0,2560.5,1440")]
        [InlineData("0,0,2 560,1440")]
        [InlineData("0,0,2560,99999999999")]
        [InlineData("left,top,right,bottom")]
        public void PixelRect_RefusesAnythingElse(string text)
        {
            Assert.False(PixelRect.TryParse(text, out PixelRect rect));
            Assert.Equal(default(PixelRect), rect);
        }

        [Fact]
        public void PixelRect_ParsesTheSameInEveryLocale()
        {
            // A locale whose minus sign isn't '-' mustn't change what a cfg means.
            CultureInfo saved = CultureInfo.CurrentCulture;
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            odd.NumberFormat.NegativeSign = "~";
            try
            {
                CultureInfo.CurrentCulture = odd;
                Assert.True(PixelRect.TryParse("-1440,0,0,1440", out PixelRect rect));
                Assert.Equal(new PixelRect(-1440, 0, 0, 1440), rect);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        static int Area(System.Collections.Generic.IEnumerable<PixelRect> rects)
        {
            int total = 0;
            foreach (PixelRect r in rects) total += r.Width * r.Height;
            return total;
        }

        [Fact]
        public void MonitorOutsideTheWindow_MapsToNothing()
        {
            ViewportMapping m = Viewport.Map(new PixelRect(5000, 0, 6000, 1000), new PixelRect(0, 0, 2560, 1440));
            Assert.True(m.IsEmpty);
        }

        static void AssertRect(ViewportMapping m, double x, double y, double width, double height)
        {
            Assert.Equal(x, m.X, 12);
            Assert.Equal(y, m.Y, 12);
            Assert.Equal(width, m.Width, 12);
            Assert.Equal(height, m.Height, 12);
        }
    }
}
