using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Keeps the game's menus on the center screen while the window spans several.
    /// A screen-overlay canvas covers the whole window, so menus anchored to its edges land on the
    /// outer screens: the main menu's buttons on the far left, the loadout screen's ribbons and
    /// rows across all three. A canvas that scales with the window's width, as Rewired's control
    /// mapper does, grows with every screen added.
    ///
    /// While Window.MenusOnCenter is on and the window is spanned, every root screen-overlay
    /// canvas but the excluded ones has the anchors of its top-level pieces remapped into the
    /// center screen's share of the window (MenuFit). A canvas whose scale would change gets the
    /// scale it would have on that screen alone. Each piece carries a marker holding the anchors
    /// the game gave it, so a copy of a pinned piece made at runtime is pinned from those, never
    /// pinned twice; anchors the game changes itself become the new originals. Canvases are
    /// looked for once a second, inactive ones included, so a menu is pinned before it shows;
    /// pieces added to a pinned canvas are pinned the frame they appear. Off, or with the window
    /// no longer spanned, everything goes back as the game made it.
    /// </summary>
    internal sealed class MenuPinning
    {
        /// <summary>Holds a piece's own anchors; copied with the piece, so copies know them too.</summary>
        internal sealed class PinnedPiece : MonoBehaviour
        {
            public Vector2 OriginalMin, OriginalMax, WrittenMin, WrittenMax;
            public bool Pinned;
        }

        /// <summary>Holds a canvas scaler's own settings while this sets the scale.</summary>
        internal sealed class PinnedScale : MonoBehaviour
        {
            public CanvasScaler.ScaleMode OriginalMode;
            public float OriginalFactor;
            public bool Holding;
        }

        // Left spanning the window: the flight HUD, which the cameras pin to the world, the
        // blackout fade, which should cover every screen, and UnityExplorer's.
        static bool Excluded(Canvas c) => c.name == "HUDCanvas" || c.name == "BlackoutCanvas" || c.name.StartsWith("com.sinai");

        readonly ManualLogSource log;
        readonly List<Canvas> canvases = new List<Canvas>();
        readonly Dictionary<Canvas, int> childCounts = new Dictionary<Canvas, int>();
        ViewportMapping band;
        bool active;
        float nextScan;
        bool failed;

        public MenuPinning(ManualLogSource log)
        {
            this.log = log;
        }

        public string Status { get; private set; } = "not seen yet";

        /// <summary>Each frame: pins menus to the center screen's share of the window, or puts them back.</summary>
        public void Update(bool on, ViewportMapping? center)
        {
            if (failed) return;
            try
            {
                Follow(on, center);
            }
            catch (Exception e)
            {
                failed = true;
                PutBack();
                Status = "stopped after an error; see the log";
                log.LogError($"Pinning menus to the center screen stopped; menus span the window as the game lays them out. {e}");
            }
        }

        void Follow(bool on, ViewportMapping? center)
        {
            if (!on || center == null || center.Value.Width <= 0 || center.Value.Height <= 0 || center.Value.Width >= 0.999)
            {
                if (active) PutBack();
                Status = on ? "off: the window isn't spanned" : "off";
                return;
            }
            bool moved = !active || !Same(center.Value, band);
            band = center.Value;
            active = true;
            if (moved || Time.unscaledTime >= nextScan) Scan();

            int pieces = 0;
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null) continue;
                int count = canvas.transform.childCount;
                bool changed = moved || !childCounts.TryGetValue(canvas, out int last) || last != count;
                childCounts[canvas] = count;
                if (changed) Pin(canvas);
                pieces += count;
            }
            Status = $"{canvases.Count} canvases pinned to the center screen ({pieces} top-level pieces)";
        }

        void Scan()
        {
            nextScan = Time.unscaledTime + 1f;
            var found = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(c => c.gameObject.scene.IsValid() && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && !Excluded(c))
                .ToList();
            foreach (Canvas c in found)
                if (!canvases.Contains(c))
                {
                    canvases.Add(c);
                    childCounts.Remove(c);
                }
            canvases.RemoveAll(c => c == null);
            // Recheck every piece now and then: the game sometimes resets a piece's anchors.
            childCounts.Clear();
        }

        void Pin(Canvas canvas)
        {
            HoldScale(canvas);
            foreach (Transform child in canvas.transform)
                if (child is RectTransform piece) PinPiece(piece);
        }

        void PinPiece(RectTransform piece)
        {
            PinnedPiece mark = piece.GetComponent<PinnedPiece>();
            if (mark == null) mark = piece.gameObject.AddComponent<PinnedPiece>();
            if (!mark.Pinned)
            {
                mark.OriginalMin = piece.anchorMin;
                mark.OriginalMax = piece.anchorMax;
                mark.Pinned = true;
            }
            else if (piece.anchorMin != mark.WrittenMin || piece.anchorMax != mark.WrittenMax)
            {
                // The game set anchors of its own since: those are the originals now.
                mark.OriginalMin = piece.anchorMin;
                mark.OriginalMax = piece.anchorMax;
            }
            mark.WrittenMin = Remap(mark.OriginalMin);
            mark.WrittenMax = Remap(mark.OriginalMax);
            piece.anchorMin = mark.WrittenMin;
            piece.anchorMax = mark.WrittenMax;
        }

        Vector2 Remap(Vector2 anchor) => new Vector2(
            (float)MenuFit.Into(anchor.x, band.X, band.Width),
            (float)MenuFit.Into(anchor.y, band.Y, band.Height));

        // Gives a canvas that scales with the screen size the scale it would have on the center
        // screen alone, when that differs from what it gets across the window.
        void HoldScale(Canvas canvas)
        {
            if (!canvas.TryGetComponent(out CanvasScaler scaler)) return;
            PinnedScale hold = canvas.GetComponent<PinnedScale>();
            bool holding = hold != null && hold.Holding;
            CanvasScaler.ScaleMode mode = holding ? hold.OriginalMode : scaler.uiScaleMode;
            if (mode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

            var match = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? ScreenMatch.Expand
                : scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? ScreenMatch.Shrink
                : ScreenMatch.MatchWidthOrHeight;
            Vector2 reference = scaler.referenceResolution;
            double across = MenuFit.CanvasScale(Screen.width, Screen.height, reference.x, reference.y, match, scaler.matchWidthOrHeight);
            double alone = MenuFit.CanvasScale(band.Width * Screen.width, band.Height * Screen.height, reference.x, reference.y, match, scaler.matchWidthOrHeight);
            if (Math.Abs(alone - across) <= 0.001 * across)
            {
                if (holding) ReleaseScale(scaler, hold);
                return;
            }
            if (hold == null) hold = canvas.gameObject.AddComponent<PinnedScale>();
            if (!hold.Holding)
            {
                hold.OriginalMode = scaler.uiScaleMode;
                hold.OriginalFactor = scaler.scaleFactor;
                hold.Holding = true;
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = (float)alone;
        }

        static void ReleaseScale(CanvasScaler scaler, PinnedScale hold)
        {
            scaler.uiScaleMode = hold.OriginalMode;
            scaler.scaleFactor = hold.OriginalFactor;
            hold.Holding = false;
        }

        // Everything back as the game made it.
        void PutBack()
        {
            active = false;
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null) continue;
                foreach (Transform child in canvas.transform)
                {
                    PinnedPiece mark = child.GetComponent<PinnedPiece>();
                    if (mark == null || !mark.Pinned || !(child is RectTransform piece)) continue;
                    piece.anchorMin = mark.OriginalMin;
                    piece.anchorMax = mark.OriginalMax;
                    mark.Pinned = false;
                }
                PinnedScale hold = canvas.GetComponent<PinnedScale>();
                if (hold != null && hold.Holding && canvas.TryGetComponent(out CanvasScaler scaler)) ReleaseScale(scaler, hold);
            }
            childCounts.Clear();
        }

        static bool Same(ViewportMapping a, ViewportMapping b) =>
            Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Width - b.Width) < 1e-6 && Math.Abs(a.Height - b.Height) < 1e-6;
    }
}
