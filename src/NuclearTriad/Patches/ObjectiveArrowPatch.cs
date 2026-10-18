using HarmonyLib;
using UnityEngine;

namespace NuclearTriad.Patches
{
    /// <summary>Places an objective marker again, given its overlay, its world point and the direction to it.</summary>
    internal delegate void ObjectiveMarkerHook(ObjectiveOverlay overlay, Vector3 world, Vector3 direction);

    /// <summary>
    /// The objective and waypoint markers carry their own copy of the edge pin, measured from the
    /// window's middle like the shared one (OffScreenArrowPatch.cs). While the hook is set
    /// (Hud.FollowStraightAhead, Rig/StraightAhead.cs), this postfix places the marker and points
    /// its arrow again, measured from where the center screen really draws straight ahead.
    /// </summary>
    [HarmonyPatch(typeof(ObjectiveOverlay), nameof(ObjectiveOverlay.UpdateOverlay))]
    internal static class ObjectiveArrowPatch
    {
        internal static ObjectiveMarkerHook Hook;

        static void Postfix(ObjectiveOverlay __instance, MissionPosition.PositionResult result)
        {
            ObjectiveMarkerHook hook = Hook;
            if (hook != null) hook(__instance, result.Position.ToLocalPosition(), result.Direction);
        }
    }
}
