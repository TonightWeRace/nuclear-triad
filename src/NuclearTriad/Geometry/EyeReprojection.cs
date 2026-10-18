using System;
using System.Collections.Generic;

namespace NuclearTriad.Geometry
{
    /// <summary>
    /// One camera's view as it lands in the game window: the part of its panel it renders, and the
    /// window pixels it renders into (origin bottom left, as Unity's pixels run).
    /// </summary>
    public sealed class WindowView
    {
        public WindowView(string name, Panel visiblePanel, double x, double y, double width, double height)
        {
            Name = name;
            Panel = visiblePanel;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public string Name { get; }
        public Panel Panel { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        /// <summary>The view of a camera from its full panel and its viewport mapping, in a window of the given size.</summary>
        public static WindowView From(string name, Panel panel, ViewportMapping viewport, int windowWidth, int windowHeight) =>
            new WindowView(name, panel.Sub(viewport.U0, viewport.V0, viewport.U1, viewport.V1),
                viewport.X * windowWidth, viewport.Y * windowHeight, viewport.Width * windowWidth, viewport.Height * windowHeight);

        /// <summary>The eye-space direction through a window pixel of this view.</summary>
        public Vec3 RayThrough(double px, double py)
        {
            double a = (px - X) / Width, b = (py - Y) / Height;
            return Panel.LowerLeft + (Panel.LowerRight - Panel.LowerLeft) * a + (Panel.UpperLeft - Panel.LowerLeft) * b;
        }

        /// <summary>Where a ray from the eye crosses this view's panel, as window pixels, if it does.</summary>
        public bool TryWindowPixel(Vec3 ray, out double px, out double py)
        {
            px = py = 0;
            double toward = Vec3.Dot(ray, Panel.Forward);
            if (toward <= 0) return false;
            Vec3 hit = ray * (Vec3.Dot(Panel.LowerLeft, Panel.Forward) / toward) - Panel.LowerLeft;
            double a = Vec3.Dot(hit, Panel.Right) / Panel.Width, b = Vec3.Dot(hit, Panel.Up) / Panel.Height;
            if (a < 0 || a >= 1 || b < 0 || b >= 1) return false;
            px = X + a * Width;
            py = Y + b * Height;
            return true;
        }
    }

    /// <summary>
    /// Re-projects the game window as the eye sees it through the physical panels: a small
    /// rectilinear view centered on a direction, in which every great circle is a straight line.
    /// Where the eye looks between panels (bezels and gaps) there is no pixel to show.
    /// </summary>
    public sealed class EyeView
    {
        readonly Vec3 forward;
        readonly Vec3 right;
        readonly Vec3 up;
        readonly double focal;

        public EyeView(Vec3 direction, int size, double horizontalFovDeg)
        {
            Size = size;
            forward = direction.Normalized;
            // In this left-handed frame, Cross(Up, Forward) is Right and Cross(Forward, Right) is Up.
            right = Vec3.Cross(Vec3.Up, forward).Normalized;
            up = Vec3.Cross(forward, right);
            focal = size / 2.0 / Math.Tan(horizontalFovDeg * Math.PI / 360);
        }

        public int Size { get; }

        /// <summary>The eye-space direction through output pixel (i, j), origin bottom left.</summary>
        public Vec3 Ray(int i, int j) =>
            forward + right * ((i + 0.5 - Size / 2.0) / focal) + up * ((j + 0.5 - Size / 2.0) / focal);

        /// <summary>Where an eye-space direction lands in the output, if in front.</summary>
        public bool TryPixel(Vec3 ray, out double i, out double j)
        {
            i = j = 0;
            double depth = Vec3.Dot(ray, forward);
            if (depth <= 0) return false;
            i = Vec3.Dot(ray, right) / depth * focal + Size / 2.0;
            j = Vec3.Dot(ray, up) / depth * focal + Size / 2.0;
            return true;
        }

        /// <summary>
        /// For each output pixel, the window pixel it shows (bottom-left origin), or -1 where the eye
        /// looks between panels. Row-major from the bottom row up.
        /// </summary>
        public int[] SourceIndices(IList<WindowView> views, int windowWidth, int windowHeight)
        {
            var map = new int[Size * Size];
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int index = -1;
                    Vec3 ray = Ray(i, j);
                    foreach (WindowView view in views)
                    {
                        if (!view.TryWindowPixel(ray, out double px, out double py)) continue;
                        int x = Math.Min(windowWidth - 1, Math.Max(0, (int)px));
                        int y = Math.Min(windowHeight - 1, Math.Max(0, (int)py));
                        index = y * windowWidth + x;
                        break;
                    }
                    map[j * Size + i] = index;
                }
            }
            return map;
        }
    }
}
