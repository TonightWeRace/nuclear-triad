using System;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Spans the game window across the screens. Unity first switches to windowed mode at the target
    /// size; then the window becomes a borderless popup placed exactly on the target rect. Unity
    /// sizes a windowed-mode window as if it still had its title bar and borders, so the window
    /// comes out larger by that frame; the span measures the difference and asks Unity for a
    /// resolution smaller by exactly that much. Once a second it checks the window is still in place
    /// and puts it back if anything moved it, such as the game's Graphics menu.
    ///
    /// Unity saves the screen mode, size and position to the game's PlayerPrefs whenever they
    /// change. So the game's own setting from before the span is kept in our config, and written
    /// back on quit and when the span is turned off, which also covers a session that crashed
    /// while spanned.
    /// </summary>
    internal sealed class WindowSpan
    {
        const int MaxAttempts = 6;

        sealed class ScreenSetting
        {
            public int Width, Height, X, Y;
            public FullScreenMode Mode;

            // Written and read culture-invariant, so the cfg means the same in every locale.
            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", Width, Height, Mode, X, Y);

            /// <summary>
            /// Parses 'width,height,mode,x,y'. Null for anything else, such as a mode Unity doesn't
            /// have or a number that isn't whole, so a bad value counts as blank instead of throwing.
            /// </summary>
            public static ScreenSetting Parse(string text)
            {
                string[] v = (text ?? "").Split(',');
                if (v.Length != 5 || !Enum.IsDefined(typeof(FullScreenMode), v[2].Trim())) return null;
                if (!Int(v[0], out int width) || !Int(v[1], out int height) || !Int(v[3], out int x) || !Int(v[4], out int y)) return null;
                return new ScreenSetting
                {
                    Width = width, Height = height,
                    Mode = (FullScreenMode)Enum.Parse(typeof(FullScreenMode), v[2].Trim()),
                    X = x, Y = y,
                };
            }

            static bool Int(string text, out int value) =>
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        readonly ManualLogSource log;
        readonly ConfigEntry<string> savedOriginal;
        PixelRect? target;
        ScreenSetting original;
        int attempts;
        float nextCheck;
        int settleUntilFrame;
        int requestedWidth;
        int requestedHeight;

        public WindowSpan(ManualLogSource log, ConfigEntry<string> savedOriginal)
        {
            this.log = log;
            this.savedOriginal = savedOriginal;
        }

        public PixelRect? Target => target;

        public bool IsSpanned { get; private set; }

        public void Request(PixelRect? rect)
        {
            if (Nullable.Equals(rect, target)) return;
            target = rect;
            attempts = 0;
            nextCheck = 0;
            IsSpanned = false;
            if (rect == null) Restore();
            else log.LogInfo($"Spanning the window to {rect.Value} ({rect.Value.Width}x{rect.Value.Height})");
        }

        public void Update(IntPtr window)
        {
            if (target == null || !Win32.IsAlive(window)) return;
            if (Time.frameCount < settleUntilFrame || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;

            PixelRect want = target.Value;
            PixelRect client = Win32.ClientRectOnScreen(window);
            bool popup = (Win32.Style(window) & (Win32.WS_CAPTION | Win32.WS_THICKFRAME)) == 0;
            bool placed = client == want && Screen.width == want.Width && Screen.height == want.Height && popup;
            if (placed)
            {
                if (!IsSpanned) log.LogInfo($"Window spans {want}");
                IsSpanned = true;
                attempts = 0;
                return;
            }

            if (IsSpanned) log.LogWarning($"Window moved off its span (client {client}, Unity {Screen.width}x{Screen.height}); putting it back");
            IsSpanned = false;
            if (attempts >= MaxAttempts)
            {
                if (attempts++ == MaxAttempts)
                    log.LogError($"Couldn't span the window to {want}: client {client}, Unity {Screen.width}x{Screen.height}, " +
                                 $"mode {Screen.fullScreenMode}. Run dump-displays; 'span' retries.");
                return;
            }
            attempts++;
            RememberOriginal(want);

            // Unity applies a resolution change at the end of the frame, so each step waits a few
            // frames before the next check.
            nextCheck = 0;
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                RequestResolution(want.Width, want.Height);
                return;
            }
            if (!popup || client.Left != want.Left || client.Top != want.Top)
            {
                Win32.PlaceBorderless(window, want);
                settleUntilFrame = Time.frameCount + 2;
                return;
            }

            // A borderless window in place but the wrong size: Unity sized it for the resolution it
            // was asked for plus a frame it no longer has. Ask for less by exactly that frame.
            int frameWidth = client.Width - requestedWidth, frameHeight = client.Height - requestedHeight;
            if (requestedWidth > 0 && (frameWidth != 0 || frameHeight != 0))
            {
                log.LogInfo($"Unity sized the window {client.Width}x{client.Height} for {requestedWidth}x{requestedHeight}; " +
                            $"allowing for its {frameWidth}x{frameHeight} frame");
                RequestResolution(want.Width - frameWidth, want.Height - frameHeight);
                return;
            }
            Win32.PlaceBorderless(window, want);
            settleUntilFrame = Time.frameCount + 2;
        }

        // The game's own setting, from our config if an earlier session saved it; otherwise from
        // the current screen, unless the game came up already spanned (after a crash), when the
        // best guess is borderless fullscreen at the desktop resolution.
        void RememberOriginal(PixelRect want)
        {
            if (original != null) return;
            original = ScreenSetting.Parse(savedOriginal.Value);
            if (original != null) return;
            // A bad value counts as blank, and is replaced below, so this is said once.
            if (!string.IsNullOrWhiteSpace(savedOriginal.Value))
                log.LogWarning($"{savedOriginal.Definition.Section}.{savedOriginal.Definition.Key} is '{savedOriginal.Value}', not " +
                               "'width,height,mode,x,y', so it counts as blank");

            bool alreadySpanned = Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == want.Width && Screen.height == want.Height;
            original = alreadySpanned
                ? new ScreenSetting { Width = Screen.currentResolution.width, Height = Screen.currentResolution.height, Mode = FullScreenMode.FullScreenWindow }
                : new ScreenSetting { Width = Screen.width, Height = Screen.height, Mode = Screen.fullScreenMode, X = Screen.mainWindowPosition.x, Y = Screen.mainWindowPosition.y };
            savedOriginal.Value = original.ToString();
            log.LogInfo($"Remembered the game's own screen setting: {original}{(alreadySpanned ? " (estimated; the game started spanned)" : "")}");
        }

        void RequestResolution(int width, int height)
        {
            requestedWidth = width;
            requestedHeight = height;
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            settleUntilFrame = Time.frameCount + 3;
        }

        /// <summary>Forget past failures and try again, as the 'span' command does.</summary>
        public void Retry()
        {
            attempts = 0;
            nextCheck = 0;
        }

        /// <summary>Writes the game's own screen setting back to its PlayerPrefs, so the span never becomes the saved setting.</summary>
        public void OnQuitting()
        {
            if (original == null) return;
            PlayerPrefs.SetInt("Screenmanager Fullscreen mode", (int)original.Mode);
            PlayerPrefs.SetInt("Screenmanager Resolution Width", original.Width);
            PlayerPrefs.SetInt("Screenmanager Resolution Height", original.Height);
            PlayerPrefs.SetInt("Screenmanager Window Position X", original.X);
            PlayerPrefs.SetInt("Screenmanager Window Position Y", original.Y);
            PlayerPrefs.Save();
        }

        void Restore()
        {
            if (original == null) return;
            Screen.SetResolution(original.Width, original.Height, original.Mode);
            log.LogInfo($"Window restored to the game's own setting {original}");
            original = null;
            savedOriginal.Value = "";
        }
    }
}
