using HarmonyLib;
using UnityEngine;

namespace NuclearTriad.Patches
{
    /// <summary>Corrects one camera answer: the camera asked, the world point, and what the camera said.</summary>
    internal delegate Vector3 ScreenPointHook(Camera camera, Vector3 world, Vector3 answer);

    /// <summary>
    /// The game places every window-HUD marker by asking the main camera where a world point
    /// lands on screen: unit and hit markers, the flight path marker, threat cues, overlays and
    /// the off-screen arrows. With the rig on, the main camera only covers the center screen, so
    /// this postfix lets the rig answer for the side screens through their own cameras.
    ///
    /// It does nothing while no hook is set; the plugin sets one only while Hud.SideScreenMarkers
    /// is on, and the rig only changes answers while it's built, so toggling off is vanilla.
    /// Patching the one-argument overload, which every HUD call site uses; HarmonyX marks a
    /// patched method as not inlinable, and the HUD's code is compiled after the plugin patches.
    /// </summary>
    [HarmonyPatch(typeof(Camera), nameof(Camera.WorldToScreenPoint), typeof(Vector3))]
    internal static class MainCameraScreenPointPatch
    {
        internal static ScreenPointHook Hook;

        static void Postfix(Camera __instance, Vector3 position, ref Vector3 __result)
        {
            ScreenPointHook hook = Hook;
            if (hook != null) __result = hook(__instance, position, __result);
        }
    }
}
