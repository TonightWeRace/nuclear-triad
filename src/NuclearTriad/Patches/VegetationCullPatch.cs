using System;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;

namespace NuclearTriad.Patches
{
    /// <summary>
    /// Once a frame the game's vegetation renderer turns the main camera's frustum into global
    /// planes that the tree and grass culling compute shaders read, then draws what survives for
    /// every camera. With the rig on, that frustum is only the center screen's, so this postfix
    /// lets the rig replace the planes with ones that hold all three screens.
    ///
    /// It does nothing while no hook is set; the plugin sets one only while
    /// Vegetation.CullAllScreens is on, and the rig only acts while it's built.
    /// </summary>
    [HarmonyPatch(typeof(ShaderGlobalManager), nameof(ShaderGlobalManager.SetCameraPlanes))]
    internal static class VegetationCullPatch
    {
        internal static Action<Camera> Hook;

        static void Postfix(Camera camera) => Hook?.Invoke(camera);
    }
}
