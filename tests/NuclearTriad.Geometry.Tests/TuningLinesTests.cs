using System;
using System.Collections.Generic;
using System.Linq;
using NuclearTriad.Geometry;
using Xunit;

namespace NuclearTriad.Geometry.Tests
{
    public class TuningLinesTests
    {
        const double W = 697;
        const double D = 420;

        [Fact]
        public void SeamDirections_WithNoGaps_PointAtTheCenterScreensEdges()
        {
            List<Vec3> seams = TuningLines.SeamDirections(Layout.Build(Triple(angle: 15, gap: 0)));

            Assert.Equal(2, seams.Count);
            AssertVec(new Vec3(-W / 2, 0, D).Normalized, seams[0]);
            AssertVec(new Vec3(W / 2, 0, D).Normalized, seams[1]);
        }

        [Fact]
        public void SeamDirections_LieInTheGap_AtEyeHeight()
        {
            List<Panel> panels = Layout.Build(Triple(angle: 15, gap: 60));
            List<Vec3> seams = TuningLines.SeamDirections(panels);

            // Between the two edges as seen from the eye, and level.
            double Azimuth(Vec3 v) => Math.Atan2(v.X, v.Z);
            Assert.InRange(Azimuth(seams[0]), Azimuth(panels[0].LowerRight), Azimuth(panels[1].LowerLeft));
            Assert.InRange(Azimuth(seams[1]), Azimuth(panels[1].LowerRight), Azimuth(panels[2].LowerLeft));
            foreach (Vec3 s in seams) Assert.Equal(0, s.Y, 12);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(-30)]
        [InlineData(0)]
        public void Through_PassesTheSeamAtTheGivenSlope(double slope)
        {
            Vec3 seam = TuningLines.SeamDirections(Layout.Build(Triple(angle: 15, gap: 60)))[1];
            GreatCircle circle = TuningLines.Through(seam, slope);

            AssertVec(seam, circle.At(0));
            foreach (double t in new[] { 0.3, 1.7, -2.2 }) Assert.Equal(1, circle.At(t).Length, 12);
            // Just past the seam: rise over the level run toward the right.
            Vec3 a = circle.At(-1e-6), b = circle.At(1e-6);
            double run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            Assert.Equal(Math.Tan(slope * Math.PI / 180), (b.Y - a.Y) / run, 6);
            Assert.True(Azimuth(b) > Azimuth(a), "rising to the right");
        }

        [Fact]
        public void EachLine_IsStraightOnEveryScreen()
        {
            List<Panel> panels = Layout.Build(Triple(angle: 15, gap: 60));
            var circles = new List<GreatCircle> { TuningLines.EyeLevel };
            foreach (Vec3 seam in TuningLines.SeamDirections(panels))
            {
                circles.Add(TuningLines.Through(seam, 30));
                circles.Add(TuningLines.Through(seam, -30));
            }

            foreach (Panel p in panels)
                foreach (GreatCircle c in circles)
                {
                    // Where the circle's rays hit the panel's plane, in its own 2D frame.
                    var hits = new List<(double X, double Y)>();
                    for (double t = -Math.PI; t < Math.PI; t += 0.01)
                    {
                        Vec3 ray = c.At(t);
                        double along = Vec3.Dot(ray, p.Forward);
                        if (along <= 1e-3) continue;
                        Vec3 hit = ray * (Vec3.Dot(p.LowerLeft, p.Forward) / along) - p.LowerLeft;
                        double x = Vec3.Dot(hit, p.Right), y = Vec3.Dot(hit, p.Up);
                        if (x >= 0 && x <= p.Width && y >= 0 && y <= p.Height) hits.Add((x, y));
                    }
                    if (hits.Count < 3) continue;
                    (double X, double Y) first = hits.First(), last = hits.Last();
                    double length = Math.Sqrt((last.X - first.X) * (last.X - first.X) + (last.Y - first.Y) * (last.Y - first.Y));
                    foreach ((double X, double Y) h in hits)
                    {
                        double off = ((last.X - first.X) * (h.Y - first.Y) - (last.Y - first.Y) * (h.X - first.X)) / length;
                        Assert.True(Math.Abs(off) < 1e-6, $"{p.Name}: {off} mm off the straight line");
                    }
                }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(20, 50)]
        [InlineData(45, 120)]
        public void EyeLevel_LandsOnEachSideScreen_AtEyeHeightLessItsOffset_WhateverItsAngleAndGap(double angle, double gap)
        {
            // So the level line can only show the eye height and the vertical offsets, which the
            // tuner sets with it before the gaps and angles.
            LayoutSpec spec = Triple(angle, gap);
            spec.EyeHeightMm = 60;
            spec.Left[0].VerticalOffsetMm = -5;
            spec.Right[0].VerticalOffsetMm = 10;
            List<Panel> panels = Layout.Build(spec);

            // Eye space has the eye at y = 0: the level line's height above a panel's bottom edge is -bottom.
            Assert.Equal(panels[1].Height / 2 + 60, -panels[1].LowerLeft.Y, 9);
            Assert.Equal(panels[0].Height / 2 + 60 + 5, -panels[0].LowerLeft.Y, 9);
            Assert.Equal(panels[2].Height / 2 + 60 - 10, -panels[2].LowerLeft.Y, 9);
        }

        [Fact]
        public void AroundStraightAhead_RingsTheEyePoint_OnTheCenterScreen()
        {
            LayoutSpec spec = Triple(angle: 15, gap: 60);
            spec.EyeHeightMm = 60;
            spec.EyeSideMm = -20;
            Panel center = Layout.Build(spec)[1];

            foreach (double t in new[] { 0, 1.1, 2.5, 4.0, 5.9 })
            {
                Vec3 p = TuningLines.AroundStraightAhead(2, t);
                Assert.Equal(1, p.Length, 12);
                Assert.Equal(2, Math.Acos(Vec3.Dot(p, Vec3.Forward)) * 180 / Math.PI, 9);
            }

            // Straight ahead meets the center screen, square to the eye at distance D, at the eye's
            // height and side offset from the picture's middle.
            Vec3 fromCorner = Vec3.Forward * D - center.LowerLeft;
            Assert.Equal(W / 2 - 20, Vec3.Dot(fromCorner, center.Right), 9);
            Assert.Equal(center.Height / 2 + 60, Vec3.Dot(fromCorner, center.Up), 9);
            Assert.Equal(0, Vec3.Dot(fromCorner, center.Forward), 9);
        }

        static double Azimuth(Vec3 v) => Math.Atan2(v.X, v.Z);

        static LayoutSpec Triple(double angle, double gap) => new LayoutSpec
        {
            Center = new ScreenSpec { Name = "center", WidthMm = W, PixelWidth = 2560, PixelHeight = 1440 },
            Left = { new ScreenSpec { Name = "left", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = angle, GapMm = gap } },
            Right = { new ScreenSpec { Name = "right", WidthMm = 392, PixelWidth = 1440, PixelHeight = 2560, AngleDeg = angle, GapMm = gap } },
            EyeDistanceMm = D,
        };

        static void AssertVec(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }
    }
}
