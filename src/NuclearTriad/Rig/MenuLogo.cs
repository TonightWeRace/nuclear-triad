using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// The Nuclear Triad mark, small and faint, in the main menu's bottom-right corner, behind
    /// Window.MenuLogo. It's a piece of the game's own main menu panel, drawn after the panel's
    /// other pieces: the settings screen and the popups sit above that panel, so they cover it, and
    /// it never takes a click. The menu pinning keeps the panel on the center screen, so the mark
    /// lands in the center screen's corner. Its image is drawn at runtime from
    /// branding/nuclear-triad-mark-white.svg, which the plugin embeds, at the size it shows (MarkShape).
    /// Off, the piece is gone and the menu is as the game made it.
    /// </summary>
    internal sealed class MenuLogo
    {
        const string PieceName = "Nuclear Triad mark";
        const string Resource = "NuclearTriad.mark-white.svg";
        // In the menu canvas's units, which are 1080 to the screen's height: about a twentieth of it,
        // clear of the corner by less than half that.
        const float HeightUnits = 54f, MarginUnits = 24f;
        const float Opacity = 0.55f;

        readonly ManualLogSource log;
        MarkShape.Box box;
        List<List<(double X, double Y)>> contours;
        GameObject piece;
        RawImage image;
        Texture2D texture;
        float nextLook;
        bool failed;

        public MenuLogo(ManualLogSource log)
        {
            this.log = log;
        }

        public string Status { get; private set; } = "not seen yet";

        /// <summary>Each frame: puts the mark in the main menu when it shows, or takes it out.</summary>
        public void Update(bool on)
        {
            if (failed) return;
            try
            {
                Follow(on);
            }
            catch (Exception e)
            {
                failed = true;
                Remove();
                Status = "stopped after an error; see the log";
                log.LogError($"The main menu's mark stopped, so the menu shows as the game made it. {e}");
            }
        }

        void Follow(bool on)
        {
            if (!on)
            {
                Remove();
                Status = "off";
                return;
            }
            // Gone with the main menu's scene: look for the menu again.
            if (piece == null && Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 1f;
                MainMenu menu = Object.FindAnyObjectByType<MainMenu>();
                if (menu != null && menu.transform is RectTransform panel) Add(panel);
            }
            if (piece == null)
            {
                Status = "waiting for the main menu";
                return;
            }
            Redraw();
        }

        void Add(RectTransform panel)
        {
            if (contours == null)
            {
                using (Stream stream = typeof(MenuLogo).Assembly.GetManifestResourceStream(Resource))
                using (var reader = new StreamReader(stream ?? throw new InvalidOperationException($"The plugin has no {Resource}.")))
                {
                    (box, string path) = MarkShape.Read(reader.ReadToEnd());
                    // Chords of a fortieth of a corner's radius: the corners stay round at any size.
                    contours = MarkShape.Contours(path, 1);
                }
            }
            piece = new GameObject(PieceName, typeof(RectTransform), typeof(RawImage)) { layer = panel.gameObject.layer };
            var rect = (RectTransform)piece.transform;
            rect.SetParent(panel, false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-MarginUnits, MarginUnits);
            rect.sizeDelta = new Vector2((float)(HeightUnits * box.Aspect), HeightUnits);
            image = piece.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, Opacity);
            // Hidden until its image is drawn: an empty RawImage shows as a white box.
            image.enabled = false;
            log.LogInfo($"The mark is in the main menu's bottom-right corner ({PieceName}, under '{panel.name}').");
        }

        // Draws the image at the size the mark shows, so it's sharp, again whenever that changes.
        void Redraw()
        {
            var corners = new Vector3[4];
            ((RectTransform)piece.transform).GetWorldCorners(corners);
            int height = Mathf.RoundToInt(corners[1].y - corners[0].y);
            int width = Mathf.RoundToInt(corners[2].x - corners[1].x);
            if (height < 4 || width < 4)
            {
                image.enabled = false;
                Status = "in the main menu, too small to draw";
                return;
            }
            if (texture == null || texture.width != width || texture.height != height)
            {
                if (texture != null) Object.Destroy(texture);
                texture = Draw(width, height);
            }
            if (image.texture != texture) image.texture = texture;
            image.enabled = true;
            Status = $"in the main menu's corner, {width}x{height} px";
        }

        Texture2D Draw(int width, int height)
        {
            float[] coverage = MarkShape.Coverage(contours, box, width, height);
            var pixels = new Color32[width * height];
            // Coverage runs from the top; a texture's rows run from the bottom.
            for (int row = 0; row < height; row++)
                for (int x = 0; x < width; x++)
                    pixels[(height - 1 - row) * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage[row * width + x] * 255));
            var drawn = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = PieceName, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            drawn.SetPixels32(pixels);
            drawn.Apply(false, true);
            return drawn;
        }

        void Remove()
        {
            if (piece != null) Object.Destroy(piece);
            piece = null;
            image = null;
            if (texture != null) Object.Destroy(texture);
            texture = null;
        }
    }
}
