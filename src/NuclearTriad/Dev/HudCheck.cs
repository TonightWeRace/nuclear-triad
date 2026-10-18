using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearTriad.Rig;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// hud-check: for every unit marker the game is showing, compares where the game drew it with
    /// where the corrected projection puts its unit, per screen, and how far off the game's own
    /// placement would have been. Markers drawn on the corrected spot prove the patch reaches the
    /// HUD's own calls, not only the plugin's.
    /// </summary>
    internal static class HudCheck
    {
        public static string Run(TriadRig rig)
        {
            Camera main = rig.Main;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (main == null || hud == null) return "no rig, or no combat HUD in this scene";
            if (!(AccessTools.Field(typeof(CombatHUD), "markers")?.GetValue(hud) is List<HUDUnitMarker> markers))
                return "couldn't read the combat HUD's marker list";

            // Read as a plain Component: the unit type derives from the game's networking classes,
            // which the plugin keeps clear of, even at compile time.
            var unitField = AccessTools.Field(typeof(HUDUnitMarker), "unit");
            Rect center = main.pixelRect;
            var screens = new Dictionary<string, List<(float Error, float Drift)>>
            {
                ["left"] = new List<(float, float)>(),
                ["center"] = new List<(float, float)>(),
                ["right"] = new List<(float, float)>(),
            };
            foreach (HUDUnitMarker marker in markers)
            {
                if (marker?.image == null || !marker.image.enabled || marker.outdated) continue;
                if (!(unitField?.GetValue(marker) is Component unit) || unit == null) continue;
                Vector3 world = unit.transform.GlobalPosition().ToLocalPosition();
                Vector3 corrected = main.WorldToScreenPoint(world);
                if (corrected.z <= 0) continue;
                // The two-argument overload isn't patched: the game's own answer.
                Vector3 vanilla = main.WorldToScreenPoint(world, Camera.MonoOrStereoscopicEye.Mono);
                Vector3 drawn = marker.image.transform.position;
                string screen = corrected.x < center.xMin ? "left" : corrected.x >= center.xMax ? "right" : "center";
                screens[screen].Add((Distance(drawn, corrected), Distance(corrected, vanilla)));
            }

            return string.Join("; ", screens.Select(s => s.Value.Count == 0
                ? $"{s.Key}: no markers"
                : $"{s.Key}: {s.Value.Count} markers drawn within {s.Value.Max(v => v.Error):F1} px of the corrected spot " +
                  $"(median {Median(s.Value.Select(v => v.Error)):F1}); the game alone would be up to {s.Value.Max(v => v.Drift):F0} px off"));
        }

        static float Distance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.y - b.y).magnitude;

        static float Median(IEnumerable<float> values)
        {
            List<float> sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}
