using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// Copies what makes a camera render the way it does from a game camera onto a rig camera, so the
    /// rig follows whatever the game or the player's settings change. The rig owns rect, projection
    /// and transform; this never touches them.
    /// </summary>
    internal static class CameraCopy
    {
        static readonly FieldInfo RendererIndex =
            typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo ClearDepth =
            typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Per-frame camera settings: clip planes, clearing, masks and buffers.</summary>
        public static void Settings(Camera from, Camera to)
        {
            if (to.clearFlags != from.clearFlags) to.clearFlags = from.clearFlags;
            if (to.backgroundColor != from.backgroundColor) to.backgroundColor = from.backgroundColor;
            if (to.cullingMask != from.cullingMask) to.cullingMask = from.cullingMask;
            if (to.nearClipPlane != from.nearClipPlane) to.nearClipPlane = from.nearClipPlane;
            if (to.farClipPlane != from.farClipPlane) to.farClipPlane = from.farClipPlane;
            if (to.allowHDR != from.allowHDR) to.allowHDR = from.allowHDR;
            if (to.allowMSAA != from.allowMSAA) to.allowMSAA = from.allowMSAA;
            if (to.allowDynamicResolution != from.allowDynamicResolution) to.allowDynamicResolution = from.allowDynamicResolution;
            if (to.useOcclusionCulling != from.useOcclusionCulling) to.useOcclusionCulling = from.useOcclusionCulling;
            if (to.depthTextureMode != from.depthTextureMode) to.depthTextureMode = from.depthTextureMode;
            if (to.opaqueSortMode != from.opaqueSortMode) to.opaqueSortMode = from.opaqueSortMode;
            if (to.transparencySortMode != from.transparencySortMode) to.transparencySortMode = from.transparencySortMode;
            if (to.targetDisplay != from.targetDisplay) to.targetDisplay = from.targetDisplay;
        }

        /// <summary>Per-layer cull distances. The getter allocates, so this runs on build and now and then.</summary>
        public static void LayerCullDistances(Camera from, Camera to)
        {
            to.layerCullSpherical = from.layerCullSpherical;
            to.layerCullDistances = from.layerCullDistances;
        }

        /// <summary>URP's per-camera data: renderer, post-processing, anti-aliasing, volumes, depth clearing.</summary>
        public static void Urp(UniversalAdditionalCameraData from, UniversalAdditionalCameraData to, CameraRenderType type)
        {
            to.renderType = type;
            to.SetRenderer((int)RendererIndex.GetValue(from));
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.antialiasingQuality = from.antialiasingQuality;
            to.stopNaN = from.stopNaN;
            to.dithering = from.dithering;
            to.renderShadows = from.renderShadows;
            to.requiresDepthOption = from.requiresDepthOption;
            to.requiresColorOption = from.requiresColorOption;
            to.volumeLayerMask = from.volumeLayerMask;
            to.volumeTrigger = from.volumeTrigger;
            to.allowXRRendering = false;
            ClearDepth.SetValue(to, from.clearDepth);
        }
    }
}
