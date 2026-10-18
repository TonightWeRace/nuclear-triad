using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static NuclearTriad.Dev.Format;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-cameras: every camera in the loaded scenes, enabled or not, with everything the rig
    /// copies or overrides, plus the screen and pipeline settings that frame them.
    /// </summary>
    internal static class CameraDump
    {
        static readonly FieldInfo RendererIndex =
            typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);

        public static string Write(string outputDir, string label)
        {
            var report = new Report();
            WriteScreen(report);
            WritePipeline(report);

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(c => c.gameObject.scene.IsValid())
                .OrderBy(c => c.depth).ThenBy(c => c.name)
                .ToArray();
            report.Line("");
            report.Line($"Cameras in loaded scenes: {cameras.Length}; Camera.main {Name(Camera.main)}");
            foreach (Camera camera in cameras) WriteCamera(report, camera);

            return $"{cameras.Length} cameras -> {report.Save(outputDir, label, "cameras")}";
        }

        static void WriteScreen(Report r)
        {
            r.Line($"Screen {Screen.width}x{Screen.height}, mode {Screen.fullScreenMode}, current resolution {Screen.currentResolution}");
            DisplayInfo window = Screen.mainWindowDisplayInfo;
            r.Line($"Main window at {Screen.mainWindowPosition} on '{window.name}' {window.width}x{window.height}");
            for (int i = 0; i < Display.displays.Length; i++)
            {
                Display d = Display.displays[i];
                r.Line($"Display {i}: system {d.systemWidth}x{d.systemHeight}, rendering {d.renderingWidth}x{d.renderingHeight}, active {d.active}");
            }
            int level = QualitySettings.GetQualityLevel();
            r.Line($"Quality level {level} '{QualitySettings.names[level]}', vSync {QualitySettings.vSyncCount}, target frame rate {Application.targetFrameRate}, " +
                   $"LOD bias {QualitySettings.lodBias}, runInBackground {Application.runInBackground}");
        }

        static void WritePipeline(Report r)
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            r.Line($"Render pipeline asset {Name(asset)} ({asset?.GetType().Name ?? "built-in"})");
            if (asset is UniversalRenderPipelineAsset urp)
            {
                r.Line(1, $"render scale {urp.renderScale}, upscaling {urp.upscalingFilter}, MSAA {urp.msaaSampleCount}, HDR {urp.supportsHDR}, " +
                          $"depth texture {urp.supportsCameraDepthTexture}, opaque texture {urp.supportsCameraOpaqueTexture}");
                r.Line(1, $"shadows: distance {urp.shadowDistance}, cascades {urp.shadowCascadeCount}, main light {urp.mainLightShadowmapResolution}px, " +
                          $"additional lights {urp.supportsAdditionalLightShadows}");
            }
        }

        static void WriteCamera(Report r, Camera c)
        {
            Transform t = c.transform;
            r.Line("");
            r.Line($"Camera '{c.name}' at {HierarchyPath(t)}");
            r.Line(1, $"enabled {c.enabled}, active {c.gameObject.activeInHierarchy}, scene '{c.gameObject.scene.name}', object layer {LayerMask.LayerToName(c.gameObject.layer)}, " +
                      $"type {c.cameraType}, target display {c.targetDisplay}");
            r.Line(1, $"depth {c.depth}, rect {Rect(c.rect)}, pixelRect {Rect(c.pixelRect)}, target {Texture(c.targetTexture)}");
            r.Line(1, $"clear {c.clearFlags}, background {c.backgroundColor}, near {c.nearClipPlane}, far {c.farClipPlane}, fov {c.fieldOfView:F3}, aspect {c.aspect:F4}, " +
                      $"orthographic {c.orthographic}, physical {c.usePhysicalProperties}");
            r.Line(1, $"HDR {c.allowHDR}, MSAA {c.allowMSAA}, dynamic resolution {c.allowDynamicResolution}, occlusion culling {c.useOcclusionCulling}");
            r.Line(1, $"culling mask {Layers(c.cullingMask)}");
            float[] distances = c.layerCullDistances;
            var nonZero = Enumerable.Range(0, distances.Length).Where(i => distances[i] > 0).Select(i => $"{LayerMask.LayerToName(i)}({i})={distances[i]}").ToArray();
            if (nonZero.Length > 0) r.Line(1, $"layer cull distances (spherical {c.layerCullSpherical}): {string.Join(", ", nonZero)}");
            r.Line(1, $"position {Vec(t.position)}, rotation {Vec(t.rotation.eulerAngles)}, local position {Vec(t.localPosition)}, local rotation {Vec(t.localRotation.eulerAngles)}");
            r.Line(1, $"projection ({ProjectionSource(c)}): {Matrix(c.projectionMatrix)}");
            WriteUrp(r, c);
        }

        // A projection that doesn't match the one Unity builds from fov/aspect/clip planes was set explicitly.
        static string ProjectionSource(Camera c)
        {
            if (c.orthographic) return "orthographic";
            Matrix4x4 fromFov = Matrix4x4.Perspective(c.fieldOfView, c.aspect, c.nearClipPlane, c.farClipPlane);
            Matrix4x4 actual = c.projectionMatrix;
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(actual[i] - fromFov[i]) > 1e-4f * Mathf.Max(1f, Mathf.Abs(fromFov[i]))) return "custom";
            return "from fov";
        }

        static void WriteUrp(Report r, Camera c)
        {
            if (!c.TryGetComponent(out UniversalAdditionalCameraData d))
            {
                r.Line(1, "URP data: none");
                return;
            }
            object index = RendererIndex?.GetValue(d);
            r.Line(1, $"URP: {d.renderType}, renderer index {index ?? "?"} ({d.scriptableRenderer?.GetType().Name ?? "none"}), post-processing {d.renderPostProcessing}, " +
                      $"AA {d.antialiasing} ({d.antialiasingQuality}), stop NaN {d.stopNaN}, dithering {d.dithering}");
            r.Line(1, $"URP: depth texture {d.requiresDepthOption}, opaque texture {d.requiresColorOption}, shadows {d.renderShadows}, " +
                      $"volume mask {Layers(d.volumeLayerMask)}, volume trigger {Name(d.volumeTrigger)}");
            if (d.renderType == CameraRenderType.Base)
                r.Line(1, $"URP stack: [{string.Join(", ", (d.cameraStack ?? new System.Collections.Generic.List<Camera>()).Select(Name))}]");
        }
    }
}
