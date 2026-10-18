using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// The project mark, drawn from its SVG. tools/logo.py writes the mark as one path, filled
    /// even-odd, of straight lines and circular-arc corners. This reads that path, flattens it to
    /// polygons and works out how much of each pixel they cover, so the plugin can draw the mark at
    /// any size with no image file in the repo.
    /// </summary>
    public static class MarkShape
    {
        static readonly Regex Token = new Regex(@"[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");

        /// <summary>The viewBox of an SVG holding one path, and that path's data.</summary>
        public static (Box Box, string PathData) Read(string svg)
        {
            Match box = Regex.Match(svg ?? "", @"viewBox=""([^""]+)""");
            Match path = Regex.Match(svg ?? "", @"\sd=""([^""]+)""");
            if (!box.Success || !path.Success) throw new FormatException("The SVG has no viewBox, or no path.");
            var v = new List<double>();
            foreach (Match m in Token.Matches(box.Groups[1].Value)) v.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
            if (v.Count != 4 || v[2] <= 0 || v[3] <= 0) throw new FormatException($"Bad viewBox '{box.Groups[1].Value}'.");
            return (new Box(v[0], v[1], v[2], v[3]), path.Groups[1].Value);
        }

        /// <summary>
        /// The path's contours as closed polygons, in the path's own coordinates (y down). It takes
        /// the absolute commands M, L, H, V, A and Z, which is all tools/logo.py writes, and throws on
        /// anything else. Arcs become chords no longer than maxStep.
        /// </summary>
        public static List<List<(double X, double Y)>> Contours(string pathData, double maxStep)
        {
            var tokens = new List<string>();
            foreach (Match m in Token.Matches(pathData ?? "")) tokens.Add(m.Value);
            var contours = new List<List<(double X, double Y)>>();
            List<(double X, double Y)> current = null;
            double x = 0, y = 0, startX = 0, startY = 0;
            char command = ' ';
            int i = 0;

            double Next()
            {
                if (i >= tokens.Count || char.IsLetter(tokens[i][0])) throw new FormatException($"Path command {command} is missing a number.");
                return double.Parse(tokens[i++], CultureInfo.InvariantCulture);
            }
            void LineTo(double nx, double ny)
            {
                if (current == null) throw new FormatException("The path draws before its first M.");
                current.Add((nx, ny));
                x = nx;
                y = ny;
            }
            void Close()
            {
                // A contour that came back to its start doesn't need the point twice.
                if (current != null && current.Count > 1 && current[current.Count - 1] == current[0]) current.RemoveAt(current.Count - 1);
                if (current != null && current.Count >= 3) contours.Add(current);
                current = null;
                x = startX;
                y = startY;
            }

            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) command = tokens[i++][0];
                else if (command == 'M') command = 'L';   // pairs after a moveto are lines
                else if (command == ' ' || command == 'Z') throw new FormatException("The path starts with a number.");

                switch (command)
                {
                    case 'M':
                        Close();
                        x = startX = Next();
                        y = startY = Next();
                        current = new List<(double X, double Y)> { (x, y) };
                        break;
                    case 'L': LineTo(Next(), Next()); break;
                    case 'H': LineTo(Next(), y); break;
                    case 'V': LineTo(x, Next()); break;
                    case 'A':
                        double rx = Next(), ry = Next(), rotation = Next(), large = Next(), sweep = Next(), ex = Next(), ey = Next();
                        foreach ((double X, double Y) p in Arc(x, y, rx, ry, rotation, large != 0, sweep != 0, ex, ey, maxStep)) LineTo(p.X, p.Y);
                        break;
                    case 'Z': Close(); break;
                    default: throw new FormatException($"Path command {command} isn't one the mark uses (absolute M, L, H, V, A, Z).");
                }
            }
            Close();
            return contours;
        }

        /// <summary>
        /// The points along an SVG elliptical arc from (x1, y1) to (x2, y2), ending at (x2, y2), as
        /// the SVG spec's conversion from endpoint to center parameters gives them.
        /// </summary>
        public static List<(double X, double Y)> Arc(double x1, double y1, double rx, double ry, double rotationDeg,
                                                    bool largeArc, bool sweep, double x2, double y2, double maxStep)
        {
            var points = new List<(double X, double Y)>();
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx < 1e-12 || ry < 1e-12 || (Math.Abs(x1 - x2) < 1e-12 && Math.Abs(y1 - y2) < 1e-12))
            {
                points.Add((x2, y2));
                return points;
            }
            double phi = rotationDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
            double px = cos * dx + sin * dy, py = -sin * dx + cos * dy;
            // Radii too small to reach are scaled up until they just do.
            double reach = px * px / (rx * rx) + py * py / (ry * ry);
            if (reach > 1)
            {
                rx *= Math.Sqrt(reach);
                ry *= Math.Sqrt(reach);
            }
            double num = rx * rx * ry * ry - rx * rx * py * py - ry * ry * px * px;
            double den = rx * rx * py * py + ry * ry * px * px;
            double coef = (largeArc != sweep ? 1 : -1) * Math.Sqrt(Math.Max(0, num / den));
            double cxp = coef * rx * py / ry, cyp = -coef * ry * px / rx;
            double cx = cos * cxp - sin * cyp + (x1 + x2) / 2, cy = sin * cxp + cos * cyp + (y1 + y2) / 2;
            double start = Math.Atan2((py - cyp) / ry, (px - cxp) / rx);
            double end = Math.Atan2((-py - cyp) / ry, (-px - cxp) / rx);
            double delta = end - start;
            if (sweep && delta < 0) delta += 2 * Math.PI;
            if (!sweep && delta > 0) delta -= 2 * Math.PI;

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) * Math.Max(rx, ry) / Math.Max(maxStep, 1e-9)));
            for (int k = 1; k < steps; k++)
            {
                double a = start + delta * k / steps;
                points.Add((cx + rx * cos * Math.Cos(a) - ry * sin * Math.Sin(a), cy + rx * sin * Math.Cos(a) + ry * cos * Math.Sin(a)));
            }
            points.Add((x2, y2));
            return points;
        }

        /// <summary>
        /// How much of each pixel the contours cover, even-odd, from 0 to 1, for an image of width ×
        /// height pixels with the box scaled to fit it and centered. Row 0 is the top. Each pixel row
        /// is sampled along several lines, each with exact coverage across the row.
        /// </summary>
        public static float[] Coverage(List<List<(double X, double Y)>> contours, Box box, int width, int height, int linesPerRow = 8)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The image needs a size.");
            double scale = Math.Min(width / box.Width, height / box.Height);
            double offsetX = (width - box.Width * scale) / 2 - box.X * scale;
            double offsetY = (height - box.Height * scale) / 2 - box.Y * scale;

            var edges = new List<(double X0, double Y0, double X1, double Y1)>();
            foreach (List<(double X, double Y)> contour in contours)
                for (int k = 0; k < contour.Count; k++)
                {
                    (double X, double Y) a = contour[k], b = contour[(k + 1) % contour.Count];
                    if (a.Y == b.Y) continue;   // flat edges never cross a sampling line
                    edges.Add((a.X * scale + offsetX, a.Y * scale + offsetY, b.X * scale + offsetX, b.Y * scale + offsetY));
                }

            var coverage = new float[width * height];
            var crossings = new List<double>();
            float weight = 1f / linesPerRow;
            for (int row = 0; row < height; row++)
                for (int line = 0; line < linesPerRow; line++)
                {
                    double sy = row + (line + 0.5) / linesPerRow;
                    crossings.Clear();
                    foreach ((double X0, double Y0, double X1, double Y1) e in edges)
                        // Half open, so a line through a vertex counts it once.
                        if ((e.Y0 <= sy && sy < e.Y1) || (e.Y1 <= sy && sy < e.Y0))
                            crossings.Add(e.X0 + (sy - e.Y0) * (e.X1 - e.X0) / (e.Y1 - e.Y0));
                    crossings.Sort();
                    for (int k = 0; k + 1 < crossings.Count; k += 2)
                        Span(coverage, row * width, width, crossings[k], crossings[k + 1], weight);
                }
            for (int k = 0; k < coverage.Length; k++) coverage[k] = Math.Min(1f, coverage[k]);
            return coverage;
        }

        // Adds weight × the part of each pixel [left, right) covers along one sampling line.
        static void Span(float[] coverage, int rowStart, int width, double left, double right, float weight)
        {
            left = Math.Max(0, left);
            right = Math.Min(width, right);
            if (right <= left) return;
            int first = (int)Math.Floor(left), last = Math.Min(width - 1, (int)Math.Floor(right));
            for (int px = first; px <= last; px++)
            {
                double covered = Math.Min(right, px + 1) - Math.Max(left, px);
                if (covered > 0) coverage[rowStart + px] += (float)(covered * weight);
            }
        }

        /// <summary>An SVG viewBox: its top left corner and size, in the path's coordinates.</summary>
        public readonly struct Box
        {
            public readonly double X, Y, Width, Height;

            public Box(double x, double y, double width, double height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public double Aspect => Width / Height;
        }
    }
}
