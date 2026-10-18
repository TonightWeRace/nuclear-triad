using HarmonyLib;
using UnityEngine;

namespace NuclearTriad.Patches
{
    /// <summary>Corrects where an off-screen marker is pinned, given the world point and whether the game pinned it.</summary>
    internal delegate void EdgePinHook(Vector3 world, bool pinned, ref Vector3 rayToScreen, ref float arrowAngle);

    /// <summary>
    /// The game pins a marker whose target is off the window to the window's edge, with an arrow
    /// toward it. It measures from the window's middle, as if the view looked straight out of it.
    /// While the hook is set (Hud.FollowStraightAhead, Rig/StraightAhead.cs), this postfix
    /// measures from where the center screen really draws straight ahead instead. Whether the
    /// marker is pinned doesn't change: that only depends on its projected point and the window.
    /// </summary>
    [HarmonyPatch(typeof(HUDFunctions), nameof(HUDFunctions.PinToScreenEdge))]
    internal static class OffScreenArrowPatch
    {
        internal static EdgePinHook Hook;

        static void Postfix(Vector3 coords, ref Vector3 rayToScreen, ref float arrowAngle, bool __result)
        {
            EdgePinHook hook = Hook;
            if (hook != null) hook(coords, __result, ref rayToScreen, ref arrowAngle);
        }
    }
}
