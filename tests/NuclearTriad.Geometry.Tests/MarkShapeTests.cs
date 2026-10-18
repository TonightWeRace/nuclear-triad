using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class MarkShapeTests
    {
        [Fact]
        public void Read_TakesTheViewBoxAndThePath()
        {
            (MarkShape.Box box, string path) = MarkShape.Read(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-10 -5 20 10\"><path fill=\"#FFF\" d=\"M0 0L1 0L1 1Z\"/></svg>");

            Assert.Equal((-10.0, -5.0, 20.0, 10.0), (box.X, box.Y, box.Width, box.Height));
            Assert.Equal("M0 0L1 0L1 1Z", path);
        }

        [Theory]
        [InlineData("M0 0l1 0l0 1z")]
        [InlineData("M0 0C1 1 2 2 3 3Z")]
        [InlineData("0 0L1 1Z")]
        [InlineData("L1 1Z")]
        [InlineData("M0 0L1Z")]
        public void Contours_RefusesWhatTheMarkDoesntUse(string path) =>
            Assert.Throws<FormatException>(() => MarkShape.Contours(path, 1));

        [Fact]
        public void Coverage_FillsASquareThatFillsTheImage()
        {
            List<List<(double X, double Y)>> square = MarkShape.Contours("M0 0H10V10H0Z", 1);
            float[] coverage = MarkShape.Coverage(square, new MarkShape.Box(0, 0, 10, 10), 20, 20);

            Assert.Single(square);
            Assert.Equal(4, square[0].Count);
            Assert.All(coverage, c => Assert.Equal(1f, c, 5));
        }

        [Fact]
        public void Coverage_LeavesTheHoleOfAnEvenOddRingEmpty()
        {
            // A 10 × 10 square with a 4 × 4 hole in its middle, at 10 px per unit.
            List<List<(double X, double Y)>> ring = MarkShape.Contours("M0 0L10 0L10 10L0 10ZM3 3L7 3L7 7L3 7Z", 1);
            float[] coverage = MarkShape.Coverage(ring, new MarkShape.Box(0, 0, 10, 10), 100, 100);

            Assert.Equal(0f, coverage[50 * 100 + 50], 5);
            Assert.Equal(1f, coverage[10 * 100 + 10], 5);
            Assert.Equal(1f, coverage[50 * 100 + 15], 5);
            Assert.Equal(100 * 100 - 40 * 40, coverage.Sum(), 1);
        }

        [Fact]
        public void Coverage_ShadesAnEdgeByTheFractionItCovers()
        {
            // The square's right edge falls a quarter of the way into the last column.
            List<List<(double X, double Y)>> square = MarkShape.Contours("M0 0H9.25V10H0Z", 1);
            float[] coverage = MarkShape.Coverage(square, new MarkShape.Box(0, 0, 10, 10), 10, 10);

            Assert.Equal(1f, coverage[5 * 10 + 8], 5);
            Assert.Equal(0.25f, coverage[5 * 10 + 9], 5);
        }

        [Fact]
        public void Arc_DrawsTheHalfCircleTheSweepFlagAsksFor()
        {
            // From (0, 10) to (20, 10), radius 10, sweep 1: clockwise on screen, so over the top,
            // inside the box. At 10 px per unit its area is half of π × 100².
            List<List<(double X, double Y)>> half = MarkShape.Contours("M0 10A10 10 0 0 1 20 10Z", 0.05);
            float[] coverage = MarkShape.Coverage(half, new MarkShape.Box(0, 0, 20, 10), 200, 100);

            Assert.Equal(Math.PI * 100 * 100 / 2, coverage.Sum(), Math.PI * 100 * 100 / 2 * 0.002);
            foreach ((double x, double y) in half[0])
                Assert.InRange(Math.Sqrt((x - 10) * (x - 10) + (y - 10) * (y - 10)), 10 - 1e-9, 10 + 1e-9);
        }

        [Fact]
        public void Arc_ScalesUpRadiiTooSmallToReach()
        {
            List<(double X, double Y)> arc = MarkShape.Arc(0, 0, 1, 1, 0, false, true, 10, 0, 0.1);

            Assert.Equal((10.0, 0.0), arc.Last());
            Assert.All(arc, p => Assert.InRange(Math.Sqrt((p.X - 5) * (p.X - 5) + p.Y * p.Y), 5 - 1e-9, 5 + 1e-9));
        }

        [Fact]
        public void TheMark_ReadsAsTwelveContours_AndCoversItsArea()
        {
            // Six screen halves, with the pitch ladder's four bars and the two tapes cut out of the
            // center screen. Even-odd, a contour inside an odd number of others is a hole, so the
            // area is each contour's own, added or taken away by its depth.
            (MarkShape.Box box, string path) = MarkShape.Read(File.ReadAllText(MarkFile()));
            List<List<(double X, double Y)>> contours = MarkShape.Contours(path, 2);
            const int width = 728, height = 400;
            float[] coverage = MarkShape.Coverage(contours, box, width, height);

            double expected = 0;
            foreach (List<(double X, double Y)> contour in contours)
            {
                int depth = contours.Count(other => other != contour && Inside(contour[0], other));
                expected += (depth % 2 == 0 ? 1 : -1) * Math.Abs(Area(contour));
            }
            double scale = Math.Min(width / box.Width, height / box.Height);

            Assert.Equal(12, contours.Count);
            Assert.Equal(6, contours.Count(c => contours.Count(other => other != c && Inside(c[0], other)) == 1));
            Assert.Equal(1.818, box.Aspect, 3);
            Assert.All(coverage, c => Assert.InRange(c, 0f, 1f));
            Assert.Contains(coverage, c => c == 1f);
            Assert.Contains(coverage, c => c == 0f);
            Assert.Equal(expected, coverage.Sum() / (scale * scale), expected * 0.005);
        }

        static double Area(List<(double X, double Y)> c)
        {
            double sum = 0;
            for (int k = 0; k < c.Count; k++)
            {
                (double X, double Y) a = c[k], b = c[(k + 1) % c.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }

        static bool Inside((double X, double Y) p, List<(double X, double Y)> c)
        {
            bool inside = false;
            for (int k = 0, j = c.Count - 1; k < c.Count; j = k++)
                if ((c[k].Y > p.Y) != (c[j].Y > p.Y) && p.X < (c[j].X - c[k].X) * (p.Y - c[k].Y) / (c[j].Y - c[k].Y) + c[k].X)
                    inside = !inside;
            return inside;
        }

        // branding/nuclear-triad-mark-white.svg, found from wherever the tests run.
        static string MarkFile()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "branding", "nuclear-triad-mark-white.svg");
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("branding/nuclear-triad-mark-white.svg isn't above the test run's folder.");
        }
    }
}
