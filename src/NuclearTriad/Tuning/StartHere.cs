using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace NuclearTriad.Tuning
{
    /// <summary>
    /// The box at the top of F1 (ConfigurationManager) that sends whoever opens it to the tuner
    /// first: its steps get a rig close before any setting is touched by hand. It's the custom
    /// drawer of Geometry.Tuner, so it reads and sets that setting like F1's own checkbox would.
    /// Opening the tuner from it also closes F1: F1's window sits over the middle of the center
    /// screen, on top of the tuner's.
    /// </summary>
    internal static class StartHere
    {
        const string ConfigurationManagerGuid = "com.bepis.bepinex.configurationmanager";

        static GUIStyle titleStyle, textStyle, buttonStyle, hintStyle;

        public static void Draw(ConfigEntryBase tuner, KeyboardShortcut hotkey)
        {
            EnsureStyles();
            bool open = tuner.BoxedValue is bool on && on;
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label("Do this first: open the tuner and follow its steps.", titleStyle);
            GUILayout.Label("In a mission, parked. The steps check each screen's width against a tape measure, then set your eye " +
                            "height and each side screen's vertical position, gap and angle, and that gets the rig close. They need " +
                            "each screen's Monitor, below, to be right: the tuner names the display each one uses.",
                            textStyle);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(open ? "Close the tuner" : "Open the tuner", buttonStyle, GUILayout.Width(200)))
            {
                tuner.BoxedValue = !open;
                if (!open) CloseConfigurationManager();
            }
            if (hotkey.MainKey != KeyCode.None) GUILayout.Label($"or press {hotkey}", hintStyle, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        static void CloseConfigurationManager()
        {
            try
            {
                if (Chainloader.PluginInfos.TryGetValue(ConfigurationManagerGuid, out PluginInfo info) && info.Instance != null)
                    info.Instance.GetType().GetProperty("DisplayingWindow")?.SetValue(info.Instance, false, null);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"Couldn't close F1 for the tuner, so its window may cover the tuner's: {e.Message}");
            }
        }

        static void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 32 };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, fixedHeight = 32 };
        }
    }
}
