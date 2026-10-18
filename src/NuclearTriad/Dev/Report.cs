using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// An indented plain-text report, saved under the output folder with the label, game build and
    /// time in its name so dumps from different runs never collide.
    /// </summary>
    internal sealed class Report
    {
        readonly StringBuilder text = new StringBuilder();

        public void Line(string line) => Line(0, line);

        public void Line(int indent, string line) => text.Append(' ', indent * 2).AppendLine(line);

        public string Save(string outputDir, string label, string kind)
        {
            string dir = Path.Combine(outputDir, "dumps");
            Directory.CreateDirectory(dir);
            string build = VersionGuard.SteamBuild ?? "unknown";
            string path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Safe(label)}-{kind}-b{build}.txt");
            string header =
                $"Nuclear Triad {Plugin.Version} {kind} dump, label '{label}', {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"Game {Application.version}, Unity {Application.unityVersion}, Steam build {build}, frame {Time.frameCount}{Environment.NewLine}{Environment.NewLine}";
            File.WriteAllText(path, header + text);
            return path;
        }

        static string Safe(string label) =>
            new string(label.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
    }

    /// <summary>Formatting shared by the dumps, at a precision that shows off-axis and rect detail.</summary>
    internal static class Format
    {
        public static string Name(UnityEngine.Object o) => o == null ? "none" : $"'{o.name}'";

        public static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        public static string Layers(int mask)
        {
            if (mask == -1) return "everything";
            if (mask == 0) return "nothing";
            var names = Enumerable.Range(0, 32)
                .Where(i => (mask & (1 << i)) != 0)
                .Select(i => { string n = LayerMask.LayerToName(i); return string.IsNullOrEmpty(n) ? $"#{i}" : $"{n}({i})"; });
            return $"0x{mask:X8} [{string.Join(", ", names)}]";
        }

        public static string Rect(Rect r) => $"(x {r.x:F4}, y {r.y:F4}, w {r.width:F4}, h {r.height:F4})";

        public static string Vec(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

        public static string Matrix(Matrix4x4 m) =>
            string.Join(" | ", Enumerable.Range(0, 4).Select(row => $"{m[row, 0]:F5} {m[row, 1]:F5} {m[row, 2]:F5} {m[row, 3]:F5}"));

        public static string Texture(RenderTexture t) =>
            t == null ? "screen" : $"RenderTexture '{t.name}' {t.width}x{t.height} {t.graphicsFormat} depth {t.depth} msaa {t.antiAliasing}";
    }
}
