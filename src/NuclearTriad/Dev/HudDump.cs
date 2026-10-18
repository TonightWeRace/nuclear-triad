using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-hud: the flight HUD's layout, to check what the flight-HUD bend moves. Where each of
    /// the flight HUD's parts sits in the hierarchy, whether it's inside the anchored group
    /// (HUDCenter), and that group's tree with each object's components. Anything in the tree that
    /// some other script places by screen point would be moved by the bend too.
    /// </summary>
    internal static class HudDump
    {
        public static string Write(string outputDir, string label)
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud == null) return "no flight HUD in this scene";
            Transform center = hud.GetHUDCenter();
            var report = new Report();
            report.Line($"HUDCenter: {PathOf(center)}");
            Part(report, center, "pitchCompassCenter", (Field(hud, "pitchCompassCenter") as GameObject)?.transform);
            Part(report, center, "pitchCompass", (Field(hud, "pitchCompass") as Component)?.transform);
            Part(report, center, "compass", (Field(hud, "compass") as Component)?.transform);
            Part(report, center, "velocityVector", hud.velocityVector != null ? hud.velocityVector.transform : null);
            Part(report, center, "waterline", hud.waterline != null ? hud.waterline.transform : null);
            Part(report, center, "HMDCenter", hud.HMDCenter);
            Part(report, center, "statusAnchor", hud.statusAnchor);
            report.Line("");
            // The pieces that take the window's middle as straight ahead (Rig/StraightAhead.cs).
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Image designator = combat != null ? combat.targetDesignator : null;
            Piece(report, "targetDesignator", designator != null ? designator.transform : null,
                designator != null ? $"enabled {designator.enabled}, color {designator.color}" : "");
            HeadMountedDisplay helmet = SceneSingleton<HeadMountedDisplay>.i;
            report.Line($"HeadMountedDisplay: {PathOf(helmet != null ? helmet.transform : null)}");
            if (helmet != null)
                foreach (string name in new[] { "horizon", "altitude", "speed", "bearing" })
                {
                    var panel = AccessTools.Field(typeof(HeadMountedDisplay), name)?.GetValue(helmet) as Component;
                    Piece(report, $"helmet {name}", panel != null ? panel.transform : null, "");
                }
            report.Line("");
            report.Line("HUDCenter's tree (local position, rotation, scale, components):");
            int count = 0;
            if (center != null) Tree(report, center, 1, ref count);
            report.Line("");
            report.Line("HMDCenter's tree:");
            int helmetCount = 0;
            if (hud.HMDCenter != null) Tree(report, hud.HMDCenter, 1, ref helmetCount);
            return $"{count} objects under HUDCenter, {helmetCount} under HMDCenter -> {report.Save(outputDir, label, "hud")}";
        }

        // Where a piece sits and what holds it, to check the straight-ahead moves.
        static void Piece(Report report, string name, Transform t, string extra)
        {
            if (t == null)
            {
                report.Line($"{name}: (none)");
                return;
            }
            Transform p = t.parent;
            report.Line($"{name}: {PathOf(t)}; active {t.gameObject.activeInHierarchy}{(extra.Length > 0 ? ", " + extra : "")}; " +
                        $"local {t.localPosition.ToString("F1")}, world {t.position.ToString("F1")}, scale {t.localScale.ToString("F2")}; " +
                        $"parent scale {(p != null ? p.lossyScale.ToString("F3") : "-")}, parent rotation {(p != null ? p.eulerAngles.ToString("F1") : "-")}");
        }

        static object Field(FlightHud hud, string name) => AccessTools.Field(typeof(FlightHud), name)?.GetValue(hud);

        static void Part(Report report, Transform center, string name, Transform part) =>
            report.Line($"{name}: {PathOf(part)}{(part != null && center != null && part.IsChildOf(center) ? "  [inside HUDCenter]" : "")}");

        static void Tree(Report report, Transform t, int depth, ref int count)
        {
            count++;
            string components = string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            report.Line(depth, $"{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")}: ({t.localPosition.x:0.#}, {t.localPosition.y:0.#}), " +
                               $"{t.localEulerAngles.z:0.#}°, ({t.localScale.x:0.##}, {t.localScale.y:0.##}) [{components}]");
            if (depth >= 6) return;
            foreach (Transform child in t) Tree(report, child, depth + 1, ref count);
        }

        static string PathOf(Transform t)
        {
            if (t == null) return "(none)";
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
