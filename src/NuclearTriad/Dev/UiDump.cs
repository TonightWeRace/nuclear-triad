using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static NuclearTriad.Dev.Format;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-ui: the layout of every active screen-overlay canvas, to plan pinning menus to the
    /// center screen. For each canvas, its scaler and size in canvas units; then its tree, a few
    /// levels deep, with each piece's anchors, pivot, offsets and where it lands across the window
    /// in screen pixels. A piece's span across the window says which screens it covers.
    /// </summary>
    internal static class UiDump
    {
        const int MaxDepth = 4;
        const int MaxChildren = 30;

        public static string Write(string outputDir, string label)
        {
            var report = new Report();
            Canvas[] roots = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(c => c.gameObject.scene.IsValid() && c.isRootCanvas && c.enabled && c.gameObject.activeInHierarchy &&
                            c.renderMode == RenderMode.ScreenSpaceOverlay && !c.name.StartsWith("com.sinai"))
                .OrderByDescending(c => c.sortingOrder)
                .ToArray();
            report.Line($"Window {Screen.width}x{Screen.height}; active overlay canvases: {roots.Length}");
            report.Line("Each line: name, anchors min-max, pivot, position, size (canvas units), then its span across the window in pixels.");

            foreach (Canvas c in roots)
            {
                report.Line("");
                var rect = (RectTransform)c.transform;
                report.Line($"Canvas '{c.name}' at {HierarchyPath(c.transform)}: sorting {c.sortingOrder}, scale {c.transform.lossyScale.x:0.###}, " +
                            $"size {rect.rect.width:0}x{rect.rect.height:0} units");
                if (c.TryGetComponent(out CanvasScaler s))
                    report.Line(1, $"scaler {s.uiScaleMode}, reference {s.referenceResolution}, match {s.matchWidthOrHeight} ({s.screenMatchMode})");
                int count = 0;
                foreach (Transform child in c.transform) Tree(report, child, 1, ref count);
            }
            return $"{roots.Length} canvases -> {report.Save(outputDir, label, "ui")}";
        }

        static void Tree(Report report, Transform t, int depth, ref int count)
        {
            if (count++ > 600) return;
            string line = $"{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")}";
            if (t is RectTransform r)
            {
                var corners = new Vector3[4];
                r.GetWorldCorners(corners);
                line += $": anchors ({r.anchorMin.x:0.##},{r.anchorMin.y:0.##})-({r.anchorMax.x:0.##},{r.anchorMax.y:0.##}), pivot ({r.pivot.x:0.##},{r.pivot.y:0.##}), " +
                        $"pos ({r.anchoredPosition.x:0},{r.anchoredPosition.y:0}), size ({r.sizeDelta.x:0},{r.sizeDelta.y:0}); " +
                        $"x {corners[0].x:0}..{corners[2].x:0}, y {corners[0].y:0}..{corners[2].y:0}";
            }
            string components = string.Join(", ", t.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform) && !(c is CanvasRenderer))
                .Select(c => c.GetType().Name));
            if (components.Length > 0) line += $" [{components}]";
            report.Line(depth, line);
            if (depth >= MaxDepth || !t.gameObject.activeSelf) return;
            int shown = 0;
            foreach (Transform child in t)
            {
                if (shown++ >= MaxChildren)
                {
                    report.Line(depth + 1, $"... {t.childCount - MaxChildren} more");
                    break;
                }
                Tree(report, child, depth + 1, ref count);
            }
        }
    }
}
