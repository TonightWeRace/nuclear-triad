using System;
using System.Collections.Generic;
using System.Linq;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class EyeReprojectionTests
    {
        // Three 31.5" panels toed in by 40°, 20 mm seam gaps, in a 3 x 2560 px wide window.
        static List<WindowView> ToedInViews(out int windowWidth, out int windowHeight)
        {
            windowWidth = 3 * 2560;
            windowHeight = 1440;
            var spec = new LayoutSpec
            {
                Center = Screen("center"),
                Left = { Screen("left", 40, 20) },
                Right = { Screen("right", 40, 20) },
                EyeDistanceMm = 750,
            };
            List<Panel> panels = Layout.Build(spec);
            var views = new List<WindowView>();
            for (int k = 0; k < 3; k++)
            {
                var full = new ViewportMapping(k / 3.0, 0, 1 / 3.0, 1, 0, 0, 1, 1);
                views.Add(WindowView.From(panels[k].Name, panels[k], full, windowWidth, windowHeight));
            }
            return views;
        }

        static ScreenSpec Screen(string name, double angle = 0, double gap = 0) => new ScreenSpec
        {
            Name = name, WidthMm = 697, PixelWidth = 2560, PixelHeight = 1440, AngleDeg = angle, GapMm = gap,
        };

        [Fact]
        public void WindowPixel_RoundTripsThroughItsRay()
        {
            List<WindowView> views = ToedInViews(out _, out _);
            foreach (WindowView view in views)
            {
                foreach (var (px, py) in new[] { (0.25, 0.1), (0.5, 0.5), (0.9, 0.95) })
                {
                    double x = view.X + px * view.Width, y = view.Y + py * view.Height;
                    Assert.True(view.TryWindowPixel(view.RayThrough(x, y), out double backX, out double backY), view.Name);
                    Assert.Equal(x, backX, 6);
                    Assert.Equal(y, backY, 6);
                }
            }
        }

        [Fact]
        public void GreatCircle_IsStraightOnEachPanelButBendsAtTheSeamInTheWindow()
        {
            List<WindowView> views = ToedInViews(out _, out _);
            // A great circle tilted 10° about the forward axis, sampled across the center and right
            // panels. (Steeper tilts leave the 392 mm tall right panel through its top too soon.)
            var center = new List<(double X, double Y)>();
            var right = new List<(double X, double Y)>();
            for (double az = -20; az <= 80; az += 0.5)
            {
                double a = az * Math.PI / 180, t = 10 * Math.PI / 180;
                var ray = new Vec3(Math.Sin(a) * Math.Cos(t), Math.Sin(a) * Math.Sin(t), Math.Cos(a));
                if (views[1].TryWindowPixel(ray, out double cx, out double cy)) center.Add((cx, cy));
                else if (views[2].TryWindowPixel(ray, out double rx, out double ry)) right.Add((rx, ry));
            }

            Assert.True(center.Count > 10 && right.Count > 10, $"{center.Count} points on the center panel, {right.Count} on the right");
            double centerSlope = Slope(center), rightSlope = Slope(right);
            Assert.True(Residual(center) < 1e-6 && Residual(right) < 1e-6,
                $"a great circle must be straight on a flat panel; residuals {Residual(center)} and {Residual(right)} px, " +
                $"{center.Count} and {right.Count} points, x {center.Min(p => p.X):F0}-{center.Max(p => p.X):F0} and {right.Min(p => p.X):F0}-{right.Max(p => p.X):F0}");
            // On a panel toed in by φ, a circle tilted by t has slope tan(t)·cos(φ): a kink at the
            // seam in raw window pixels, though both pieces lie on one great circle.
            double tilt = 10 * Math.PI / 180, toeIn = 40 * Math.PI / 180;
            Assert.Equal(Math.Tan(tilt), centerSlope, 9);
            Assert.Equal(Math.Tan(tilt) * Math.Cos(toeIn), rightSlope, 9);
        }

        [Fact]
        public void EyeView_ShowsTheSeamAsAGapWithAStraightLineThroughIt()
        {
            List<WindowView> views = ToedInViews(out int width, out int height);
            Vec3 seam = views[1].RayThrough(views[1].X + views[1].Width - 0.5, height / 2.0);
            var eye = new EyeView(seam, 256, 20);
            int[] map = eye.SourceIndices(views, width, height);

            // The 20 mm gap shows as a band of unmapped pixels down the middle.
            int row = eye.Size / 2;
            Assert.Contains(-1, Enumerable.Range(0, eye.Size).Select(i => map[row * eye.Size + i]));
            Assert.True(map[row * eye.Size + 10] >= 0 && map[row * eye.Size + eye.Size - 10] >= 0, "both panels show either side of the gap");

            // Points of a tilted great circle on both sides of the seam lie on one straight line, and
            // they land on both panels.
            var points = new List<(double X, double Y)>();
            int onCenter = 0, onRight = 0;
            for (double az = 15; az <= 35; az += 0.25)
            {
                double a = az * Math.PI / 180, t = 10 * Math.PI / 180;
                var ray = new Vec3(Math.Sin(a) * Math.Cos(t), Math.Sin(a) * Math.Sin(t), Math.Cos(a));
                if (!eye.TryPixel(ray, out double i, out double j) || i < 0 || i >= eye.Size || j < 0 || j >= eye.Size) continue;
                points.Add((i, j));
                if (views[1].TryWindowPixel(ray, out _, out _)) onCenter++;
                if (views[2].TryWindowPixel(ray, out _, out _)) onRight++;
            }
            Assert.True(onCenter > 10 && onRight > 10, $"{onCenter} points on the center panel, {onRight} on the right");
            Assert.True(Residual(points) < 1e-6, $"residual {Residual(points)} px");
        }

        static double Slope(List<(double X, double Y)> p) => Fit(p).slope;

        static double Residual(List<(double X, double Y)> p)
        {
            var (slope, intercept) = Fit(p);
            return p.Max(q => Math.Abs(q.Y - (slope * q.X + intercept)));
        }

        static (double slope, double intercept) Fit(List<(double X, double Y)> p)
        {
            double mx = p.Average(q => q.X), my = p.Average(q => q.Y);
            double sxy = p.Sum(q => (q.X - mx) * (q.Y - my)), sxx = p.Sum(q => (q.X - mx) * (q.X - mx));
            double slope = sxy / sxx;
            return (slope, my - slope * mx);
        }
    }
}
