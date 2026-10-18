using UnityEngine;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Puts a piece of a screen-overlay canvas at a screen point. Its place in the canvas is worked
    /// out from where the canvas draws (over the whole window, its pivot on the window's matching
    /// point, at its scale). It never comes from where the canvas's transform sits this
    /// moment, nor from where the piece already is. The game's floating origin carries every root
    /// object a world shift away in the camera manager's LateUpdate, the HUD's canvas included, and
    /// the canvas is back over the window before it draws, so a place taken from the moment lands a
    /// shift off. A place taken from where a piece already is stacks onto any earlier move, which
    /// copies of a moved HUD carry over at a respawn.
    /// </summary>
    internal static class CanvasPlacement
    {
        /// <summary>The screen-overlay canvas a piece draws on, or null.</summary>
        public static Canvas RootOf(Transform piece)
        {
            Canvas own = piece != null ? piece.GetComponentInParent<Canvas>(true) : null;
            Canvas root = own != null ? own.rootCanvas : null;
            return root != null && root.renderMode == RenderMode.ScreenSpaceOverlay ? root : null;
        }

        /// <summary>Where the canvas draws its pivot on the screen.</summary>
        public static Vector3 Origin(Canvas root)
        {
            var rect = (RectTransform)root.transform;
            return new Vector3(rect.pivot.x * Screen.width, rect.pivot.y * Screen.height, 0f);
        }

        /// <summary>
        /// Canvas units to screen pixels: the scale the canvas drives onto its own transform.
        /// Canvas.scaleFactor reads 1 while the canvas is inactive; the transform keeps the scale.
        /// </summary>
        public static float Scale(Canvas root) => root.transform.lossyScale.x;

        /// <summary>Whether At can work out places on this canvas.</summary>
        public static bool Usable(Canvas root) => root != null && Scale(root) > 0f;

        /// <summary>Puts the piece at the screen point on root; without a usable canvas, the way the game would.</summary>
        public static void At(Canvas root, Transform piece, Vector3 screen)
        {
            if (!Usable(root) || piece.parent == null)
            {
                piece.position = screen;
                return;
            }
            Vector3 inCanvas = (screen - Origin(root)) / Scale(root);
            // The parent's place within the canvas doesn't change when the canvas is carried off.
            Matrix4x4 parentInCanvas = root.transform.worldToLocalMatrix * piece.parent.localToWorldMatrix;
            piece.localPosition = parentInCanvas.inverse.MultiplyPoint3x4(inCanvas);
        }
    }
}
