using System;
using System.Collections.Generic;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;

namespace NuclearTriad.Rig
{
    /// <summary>Where the HUD's corner panels go while the window spans several screens.</summary>
    internal enum CornerPlacement
    {
        /// <summary>The center screen's corners, as on a single screen.</summary>
        CenterScreen,

        /// <summary>The window's corners, the outer corners of the side screens, as the game lays them out.</summary>
        OuterCorners,
    }

    /// <summary>
    /// The HUD's corner panels: the overhead map and threat list (bottom left) and weapons,
    /// power and countermeasures (top right), both in the helmet frame, and the damage display
    /// (bottom right) on the HUD's canvas. The game anchors them to the corners of frames as wide
    /// as the window, so a spanned window puts them on the outer corners of the side screens. With
    /// Hud.CornerPanels at CenterScreen, each panel's anchors are remapped into the part of its
    /// parent the center screen covers, worked out from where the canvas draws (CanvasPlacement),
    /// so they sit in the center screen's corners whatever the helmet frame's size. The menu
    /// pinning's markers keep the game's own anchors, so nothing is moved twice; with
    /// OuterCorners, or the window unspanned, the panels go back as the game made them.
    /// </summary>
    internal sealed class HudCorners
    {
        static readonly string[] HelmetPanels = { "LowerLeftPanel", "TopRightPanel" };

        readonly ManualLogSource log;
        readonly List<RectTransform> panels = new List<RectTransform>();
        FlightHud hud;
        bool failed;

        public HudCorners(ManualLogSource log)
        {
            this.log = log;
        }

        public string Status { get; private set; } = "not seen yet";

        /// <summary>Each frame: puts the corner panels in the center screen's corners, or back.</summary>
        public void Update(bool centerScreen, ViewportMapping? center)
        {
            if (failed) return;
            try
            {
                Follow(centerScreen, center);
            }
            catch (Exception e)
            {
                failed = true;
                PutBack();
                Status = "stopped after an error; see the log";
                log.LogError($"Moving the HUD's corner panels stopped; they stay where the game puts them. {e}");
            }
        }

        void Follow(bool centerScreen, ViewportMapping? center)
        {
            FlightHud current = SceneSingleton<FlightHud>.i;
            if (current != hud) Track(current);
            if (!centerScreen || center == null || center.Value.Width <= 0 || center.Value.Width >= 0.999)
            {
                PutBack();
                Status = centerScreen ? "outer corners: the window isn't spanned" : "outer corners";
                return;
            }
            int placed = 0;
            foreach (RectTransform panel in panels)
                if (panel != null && Place(panel, center.Value)) placed++;
            Status = $"{placed} panels in the center screen's corners";
        }

        void Track(FlightHud current)
        {
            PutBack();
            hud = current;
            panels.Clear();
            if (current == null) return;
            if (current.statusAnchor is RectTransform damage) panels.Add(damage);
            if (current.HMDCenter != null)
                foreach (string name in HelmetPanels)
                    if (current.HMDCenter.Find(name) is RectTransform panel) panels.Add(panel);
        }

        // Remaps the panel's anchors into the part of its parent the center screen covers.
        bool Place(RectTransform panel, ViewportMapping center)
        {
            var parent = panel.parent as RectTransform;
            Canvas canvas = CanvasPlacement.RootOf(panel);
            if (parent == null || !CanvasPlacement.Usable(canvas)) return false;
            Rect within = parent.rect;
            if (within.width <= 0 || within.height <= 0) return false;

            // The center screen's corners in screen pixels, then in the parent's own frame.
            Vector2 low = InParent(canvas, parent, new Vector3((float)(center.X * Screen.width), (float)(center.Y * Screen.height), 0f));
            Vector2 high = InParent(canvas, parent, new Vector3((float)((center.X + center.Width) * Screen.width), (float)((center.Y + center.Height) * Screen.height), 0f));
            double x0 = (low.x - within.xMin) / within.width, x1 = (high.x - within.xMin) / within.width;
            double y0 = (low.y - within.yMin) / within.height, y1 = (high.y - within.yMin) / within.height;

            MenuPinning.PinnedPiece mark = panel.GetComponent<MenuPinning.PinnedPiece>();
            if (mark == null) mark = panel.gameObject.AddComponent<MenuPinning.PinnedPiece>();
            if (!mark.Pinned)
            {
                mark.OriginalMin = panel.anchorMin;
                mark.OriginalMax = panel.anchorMax;
                mark.Pinned = true;
            }
            else if (panel.anchorMin != mark.WrittenMin || panel.anchorMax != mark.WrittenMax)
            {
                // The game set anchors of its own since: those are the originals now.
                mark.OriginalMin = panel.anchorMin;
                mark.OriginalMax = panel.anchorMax;
            }
            mark.WrittenMin = new Vector2((float)MenuFit.Into(mark.OriginalMin.x, x0, x1 - x0), (float)MenuFit.Into(mark.OriginalMin.y, y0, y1 - y0));
            mark.WrittenMax = new Vector2((float)MenuFit.Into(mark.OriginalMax.x, x0, x1 - x0), (float)MenuFit.Into(mark.OriginalMax.y, y0, y1 - y0));
            panel.anchorMin = mark.WrittenMin;
            panel.anchorMax = mark.WrittenMax;
            return true;
        }

        // A screen point in a canvas piece's own frame, from where the canvas draws, not where its
        // transform sits this moment (the floating origin carries it).
        static Vector2 InParent(Canvas canvas, RectTransform parent, Vector3 screen)
        {
            Vector3 inCanvas = (screen - CanvasPlacement.Origin(canvas)) / CanvasPlacement.Scale(canvas);
            Matrix4x4 parentInCanvas = canvas.transform.worldToLocalMatrix * parent.localToWorldMatrix;
            Vector3 local = parentInCanvas.inverse.MultiplyPoint3x4(inCanvas);
            return new Vector2(local.x, local.y);
        }

        void PutBack()
        {
            foreach (RectTransform panel in panels)
            {
                if (panel == null) continue;
                MenuPinning.PinnedPiece mark = panel.GetComponent<MenuPinning.PinnedPiece>();
                if (mark == null || !mark.Pinned) continue;
                panel.anchorMin = mark.OriginalMin;
                panel.anchorMax = mark.OriginalMax;
                mark.Pinned = false;
            }
        }
    }
}
