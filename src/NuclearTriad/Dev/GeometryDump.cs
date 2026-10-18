using System;
using System.Collections.Generic;
using System.Linq;
using NuclearTriad.Geometry;
using NuclearTriad.Rig;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-geometry: the panels the current config builds, and the frustum, field of view and
    /// heading each camera would get. Run it after editing the geometry in ConfigurationManager.
    /// </summary>
    internal static class GeometryDump
    {
        public static string Write(RigConfig config, string outputDir, string label, float gameFovDeg)
        {
            var report = new Report();
            List<Win32.Monitor> monitors = Win32.Monitors();
            report.Line(config.Measured.Value ? "Layout: measured screens" : "Layout: desktop-plane test layout (Geometry.Measured is off)");
            report.Line($"Center framing {config.Framing.Value}" +
                        (config.Framing.Value == CenterFraming.GameFov ? $" at the game's {gameFovDeg:F2}° vertical FOV" : "") +
                        $"; eye distance setting {config.EyeDistanceMm.Value} mm, height {config.EyeHeightMm.Value} mm, side {config.EyeSideMm.Value} mm");

            var rects = new Dictionary<string, PixelRect>();
            foreach (RigConfig.Screen s in config.Screens)
            {
                RigConfig.Pick pick = config.Resolve(s, monitors);
                if (pick.Rect == null)
                {
                    report.Line($"{s.Role}: " + (pick.FromArrangement ? "Monitor blank, and Windows' arrangement has no row of three to follow"
                                                                      : $"monitor '{s.Monitor.Value}' not found") +
                                $"; known: {string.Join(", ", monitors.Select(m => $"{m.Device} {m.Bounds}"))}");
                    return $"{s.Role} monitor not found -> {report.Save(outputDir, label, "geometry")}";
                }
                rects[s.Role] = pick.Rect.Value;
            }

            LayoutSpec spec = config.ToLayout(rects["left"], rects["center"], rects["right"], gameFovDeg);
            report.Line($"Eye distance used {spec.EyeDistanceMm:F1} mm");
            if (spec.Left.Count > 0 && spec.Right.Count > 0)
                report.Line($"Side screens' middles vs the center's: left {spec.Left[0].VerticalOffsetMm:F1} mm, right {spec.Right[0].VerticalOffsetMm:F1} mm " +
                            $"({(config.VerticalFromDesktop.Value ? "from the Windows layout" : "from config")})");
            if (!Layout.TryBuild(spec, out List<Panel> panels, out string problem))
            {
                report.Line($"These settings can't be drawn: {problem}");
                return $"settings can't be drawn ({problem}) -> {report.Save(outputDir, label, "geometry")}";
            }
            foreach (Panel p in panels)
            {
                Frustum f = p.FrustumFrom(Vec3.Zero);
                double yaw = Math.Atan2(p.Forward.X, p.Forward.Z) * 180 / Math.PI;
                double pitch = Math.Asin(p.Forward.Y) * 180 / Math.PI;
                report.Line("");
                report.Line($"{p.Name}: desktop rect {rects[p.Name]}, panel {p.Width:F1} x {p.Height:F1} mm, center {p.Center}");
                report.Line(1, $"corners LL {p.LowerLeft} LR {p.LowerRight} UL {p.UpperLeft}");
                report.Line(1, $"camera heading: yaw {yaw:F3}°, pitch {pitch:F3}°");
                report.Line(1, $"frustum {f}");
                report.Line(1, $"equivalent vertical FOV {f.EquivalentVerticalFovDeg:F3}°");
            }

            double total = panels.Select(p => p.FrustumFrom(Vec3.Zero)).Sum(f => f.HorizontalFovDeg);
            return $"{panels.Count} panels, about {total:F1}° wide in total -> {report.Save(outputDir, label, "geometry")}";
        }
    }
}
