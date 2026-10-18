using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static NuclearTriad.Dev.Format;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-canvases: every root UI canvas in the loaded scenes, with its render mode, camera and
    /// scaling. Screen Space Overlay canvases span the whole window, so they stretch across all
    /// three screens unless pinned.
    /// </summary>
    internal static class CanvasDump
    {
        public static string Write(string outputDir, string label)
        {
            var report = new Report();
            Canvas[] roots = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(c => c.gameObject.scene.IsValid() && c.isRootCanvas)
                .OrderBy(c => c.renderMode).ThenByDescending(c => c.sortingOrder)
                .ToArray();
            report.Line($"Root canvases in loaded scenes: {roots.Length}");

            foreach (Canvas c in roots)
            {
                report.Line("");
                report.Line($"Canvas '{c.name}' at {HierarchyPath(c.transform)}");
                report.Line(1, $"enabled {c.enabled}, active {c.gameObject.activeInHierarchy}, mode {c.renderMode}, camera {Name(c.worldCamera)}, " +
                               $"plane distance {c.planeDistance}, sorting '{c.sortingLayerName}' {c.sortingOrder}, target display {c.targetDisplay}");
                report.Line(1, $"pixelRect {Rect(c.pixelRect)}, scale factor {c.scaleFactor}, pixel perfect {c.pixelPerfect}, " +
                               $"layer {LayerMask.LayerToName(c.gameObject.layer)}");
                if (c.TryGetComponent(out CanvasScaler s))
                    report.Line(1, $"scaler {s.uiScaleMode}, reference {s.referenceResolution}, match {s.matchWidthOrHeight} ({s.screenMatchMode}), scale {s.scaleFactor}");
                int nested = c.GetComponentsInChildren<Canvas>(true).Length - 1;
                if (nested > 0) report.Line(1, $"nested canvases: {nested}");
            }

            return $"{roots.Length} root canvases -> {report.Save(outputDir, label, "canvases")}";
        }
    }
}
