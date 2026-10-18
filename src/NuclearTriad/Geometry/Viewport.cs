using System;
using System.Collections.Generic;
using System.Globalization;

namespace NuclearTriad.Geometry
{
    /// <summary>A rectangle in Windows desktop pixels: origin top left, y down, right and bottom exclusive.</summary>
    public readonly struct PixelRect : IEquatable<PixelRect>
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public PixelRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public PixelRect Intersect(PixelRect other) => new PixelRect(
            Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

        public static PixelRect Union(PixelRect a, PixelRect b) => new PixelRect(
            Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

        /// <summary>
        /// The parts of this rectangle outside the other one, as up to four rectangles: full-width
        /// strips above and below, then the pieces left and right between them.
        /// </summary>
        public List<PixelRect> Minus(PixelRect other)
        {
            var parts = new List<PixelRect>();
            PixelRect overlap = Intersect(other);
            if (overlap.IsEmpty)
            {
                if (!IsEmpty) parts.Add(this);
                return parts;
            }
            if (overlap.Top > Top) parts.Add(new PixelRect(Left, Top, Right, overlap.Top));
            if (overlap.Bottom < Bottom) parts.Add(new PixelRect(Left, overlap.Bottom, Right, Bottom));
            if (overlap.Left > Left) parts.Add(new PixelRect(Left, overlap.Top, overlap.Left, overlap.Bottom));
            if (overlap.Right < Right) parts.Add(new PixelRect(overlap.Right, overlap.Top, Right, overlap.Bottom));
            return parts;
        }

        /// <summary>
        /// Parses "left,top,right,bottom" in whole pixels, as the config holds it. Anything else
        /// gives false and an empty rect, never an exception: the text may be a hand-edited typo.
        /// </summary>
        public static bool TryParse(string text, out PixelRect rect)
        {
            rect = default;
            string[] v = (text ?? "").Split(',');
            if (v.Length != 4 || !Int(v[0], out int left) || !Int(v[1], out int top) ||
                !Int(v[2], out int right) || !Int(v[3], out int bottom))
                return false;
            rect = new PixelRect(left, top, right, bottom);
            return true;
        }

        // Config text means the same in every locale: digits, with an optional leading + or -.
        static bool Int(string text, out int value) =>
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        public bool Equals(PixelRect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object obj) => obj is PixelRect other && Equals(other);

        public override int GetHashCode() => ((Left * 397 ^ Top) * 397 ^ Right) * 397 ^ Bottom;

        public static bool operator ==(PixelRect a, PixelRect b) => a.Equals(b);

        public static bool operator !=(PixelRect a, PixelRect b) => !a.Equals(b);

        public override string ToString() => $"({Left}, {Top})-({Right}, {Bottom})";
    }

    /// <summary>Where one monitor lands in the game window, in the terms Unity and the projection need.</summary>
    public readonly struct ViewportMapping
    {
        // Camera.rect: normalized to the window's client area, origin bottom left.
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        // The part of the monitor the window covers, as fractions of its width and height from its
        // lower left corner; feed these to Panel.Sub.
        public readonly double U0;
        public readonly double V0;
        public readonly double U1;
        public readonly double V1;

        public ViewportMapping(double x, double y, double width, double height, double u0, double v0, double u1, double v1)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
        }

        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool CoversWholeMonitor => U0 == 0 && V0 == 0 && U1 == 1 && V1 == 1;
    }

    public static class Viewport
    {
        /// <summary>Maps a monitor's desktop rect into the game window's client rect (both in Windows pixels).</summary>
        public static ViewportMapping Map(PixelRect monitor, PixelRect client)
        {
            PixelRect visible = monitor.Intersect(client);
            if (visible.IsEmpty || client.IsEmpty) return default;

            double clientW = client.Width, clientH = client.Height;
            double monitorW = monitor.Width, monitorH = monitor.Height;
            return new ViewportMapping(
                (visible.Left - client.Left) / clientW,
                (client.Bottom - visible.Bottom) / clientH,
                visible.Width / clientW,
                visible.Height / clientH,
                (visible.Left - monitor.Left) / monitorW,
                (monitor.Bottom - visible.Bottom) / monitorH,
                (visible.Right - monitor.Left) / monitorW,
                (monitor.Bottom - visible.Top) / monitorH);
        }
    }
}
