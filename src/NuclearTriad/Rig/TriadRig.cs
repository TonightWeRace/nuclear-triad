using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using NuclearTriad.Geometry;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NuclearTriad.Rig
{
    /// <summary>
    /// The three-view camera rig. The game's main camera keeps the center screen, with its rect
    /// shrunk to the center monitor. Each side screen gets a new base camera, parented to the main
    /// camera at zero offset and turned to face its panel, carrying clones of the main camera's
    /// overlay stack (cockpit, post-processing). Every camera, the game's own overlays included,
    /// gets the off-axis frustum of the part of its panel the window covers.
    ///
    /// Building and teardown happen in Update. The layout is applied once per frame, before any
    /// camera renders, and left in place for the whole frame: the HUD projects through the main
    /// camera between renders, and the vegetation cull reads its projection in LateUpdate.
    ///
    /// Geometry settings the rig can't render, such as a side screen turned past the eye, don't
    /// stop it: it keeps the last layout that worked and follows the settings again as soon as
    /// they can be rendered.
    /// </summary>
    internal sealed class TriadRig
    {
        sealed class OverlayPair
        {
            public Camera Source;
            public Camera Clone;
        }

        sealed class SideView
        {
            public string Role;
            public GameObject Root;
            public Camera Camera;
            public readonly List<OverlayPair> Overlays = new List<OverlayPair>();
        }

        /// <summary>One camera as the last layout left it, for status, dumps and capture metadata.</summary>
        internal sealed class ViewState
        {
            public string Name;
            public Camera Camera;
            public Panel Panel;
            public Frustum Frustum;
            public ViewportMapping Viewport;
        }

        readonly ManualLogSource log;
        readonly RigConfig config;
        readonly List<SideView> sides = new List<SideView>();
        readonly List<Camera> gameOverlays = new List<Camera>();
        Camera main;
        UniversalAdditionalCameraData mainData;
        bool built;
        float gameFov;
        int laidOutFrame = -1;
        float nextSlowCopy;
        List<Panel> lastGood;
        string rejected;

        public TriadRig(ManualLogSource log, RigConfig config)
        {
            this.log = log;
            this.config = config;
        }

        public bool Built => built;

        public Camera Main => built ? main : null;

        /// <summary>The FOV the game itself last set on the main camera.</summary>
        public float GameFov => gameFov;

        /// <summary>While the rig is on and its geometry settings can't be rendered, why not; otherwise null.</summary>
        public string Rejected => built ? rejected : null;

        /// <summary>Whether a layout has worked since the game started, which the rig keeps while the settings are rejected.</summary>
        public bool HasLayout => lastGood != null;

        /// <summary>The panels this frame's layout used, left to right; null until the rig has laid out a frame.</summary>
        public IList<Panel> CurrentPanels { get; private set; }

        /// <summary>The views as last laid out, left to right; empty until the rig has laid out a frame.</summary>
        public List<ViewState> Views { get; } = new List<ViewState>();

        /// <summary>Whether the last Track found a mission camera to build on; never in the menus.</summary>
        public bool InMission { get; private set; }

        /// <summary>Whether, at the last Track, Rig.CockpitOnly held the rig back because the view wasn't the cockpit's.</summary>
        public bool OutsideCockpit { get; private set; }

        /// <summary>Builds the rig when a mission camera appears and tears it down when it goes or the rig is switched off.</summary>
        public void Track(bool wanted)
        {
            CameraStateManager manager = SceneSingleton<CameraStateManager>.i;
            Camera camera = manager != null ? manager.mainCamera : null;
            InMission = camera != null;
            OutsideCockpit = InMission && config.CockpitOnly.Value && manager.currentState != manager.cockpitState;
            bool build = wanted && InMission && !OutsideCockpit;

            if (built && (!build || main == null || camera != main)) Teardown();
            if (build && !built) Build(camera);
        }

        void Build(Camera camera)
        {
            main = camera;
            mainData = main.GetComponent<UniversalAdditionalCameraData>();
            gameOverlays.Clear();
            if (mainData != null && mainData.renderType == CameraRenderType.Base && mainData.cameraStack != null)
                gameOverlays.AddRange(mainData.cameraStack.Where(c => c != null));

            sides.Add(BuildSide("left"));
            sides.Add(BuildSide("right"));
            built = true;
            laidOutFrame = -1;
            nextSlowCopy = 0;
            log.LogInfo($"Rig built on '{main.name}', overlays [{string.Join(", ", gameOverlays.Select(o => o.name))}]");
        }

        SideView BuildSide(string role)
        {
            // Built inactive, so no camera renders a frame with default settings.
            var root = new GameObject($"NuclearTriad {role}");
            root.SetActive(false);
            root.layer = main.gameObject.layer;
            root.transform.SetParent(main.transform, false);

            var side = new SideView { Role = role, Root = root, Camera = root.AddComponent<Camera>() };
            UniversalAdditionalCameraData data = root.AddComponent<UniversalAdditionalCameraData>();
            CameraCopy.Settings(main, side.Camera);
            CameraCopy.LayerCullDistances(main, side.Camera);
            side.Camera.depth = main.depth;
            side.Camera.eventMask = 0;
            if (mainData != null) CameraCopy.Urp(mainData, data, CameraRenderType.Base);

            foreach (Camera source in gameOverlays)
            {
                var go = new GameObject($"NuclearTriad {role} {source.name}");
                go.layer = source.gameObject.layer;
                go.transform.SetParent(root.transform, false);
                Camera clone = go.AddComponent<Camera>();
                UniversalAdditionalCameraData cloneData = go.AddComponent<UniversalAdditionalCameraData>();
                CameraCopy.Settings(source, clone);
                CameraCopy.LayerCullDistances(source, clone);
                clone.eventMask = 0;
                UniversalAdditionalCameraData sourceData = source.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) CameraCopy.Urp(sourceData, cloneData, CameraRenderType.Overlay);
                else cloneData.renderType = CameraRenderType.Overlay;
                clone.enabled = source.enabled;
                data.cameraStack.Add(clone);
                side.Overlays.Add(new OverlayPair { Source = source, Clone = clone });
            }

            root.SetActive(true);
            return side;
        }

        /// <summary>Removes the side cameras and gives the game's cameras back exactly as it left them.</summary>
        public void Teardown()
        {
            foreach (SideView side in sides)
                if (side.Root != null) UnityEngine.Object.Destroy(side.Root);
            sides.Clear();

            if (main != null)
            {
                main.rect = new Rect(0, 0, 1, 1);
                main.ResetProjectionMatrix();
                if (gameFov > 0) main.fieldOfView = gameFov;
            }
            foreach (Camera overlay in gameOverlays)
            {
                if (overlay == null) continue;
                overlay.rect = new Rect(0, 0, 1, 1);
                overlay.ResetProjectionMatrix();
            }

            bool wasBuilt = built;
            gameOverlays.Clear();
            Views.Clear();
            CurrentPanels = null;
            main = null;
            mainData = null;
            built = false;
            gameFov = 0;
            if (wasBuilt) log.LogInfo("Rig torn down; the game's cameras are back to vanilla");
        }

        /// <summary>
        /// Lays out every camera for this frame from the screens' desktop rects and the window's
        /// client rect. Runs at most once per frame.
        /// </summary>
        public void ApplyFrame(PixelRect client, PixelRect left, PixelRect center, PixelRect right)
        {
            if (!built || main == null || Time.frameCount == laidOutFrame) return;
            laidOutFrame = Time.frameCount;

            bool slowCopy = Time.unscaledTime >= nextSlowCopy;
            if (slowCopy) nextSlowCopy = Time.unscaledTime + 2f;

            // The game eases fieldOfView toward its target in LateUpdate; read that before the rig
            // writes its own, so toggling off can hand it back and the test layout can follow it.
            gameFov = main.fieldOfView;
            lastLeft = left;
            lastCenter = center;
            lastRight = right;
            haveRects = true;
            List<Panel> panels = Panels(config.ToLayout(left, center, right, gameFov));
            CurrentPanels = panels;
            Views.Clear();
            if (panels == null)
            {
                // Nothing has worked yet, so the game's own camera, untouched so far, carries on alone.
                foreach (SideView side in sides)
                    if (side.Camera != null && side.Camera.enabled) side.Camera.enabled = false;
                return;
            }

            ViewState centerView = LayOutCenter(panels[1], Viewport.Map(center, client));
            float fov = centerView != null ? (float)centerView.Frustum.EquivalentVerticalFovDeg : gameFov;
            ViewState leftView = LayOutSide(sides[0], panels[0], Viewport.Map(left, client), fov, slowCopy);
            ViewState rightView = LayOutSide(sides[1], panels[2], Viewport.Map(right, client), fov, slowCopy);
            foreach (ViewState v in new[] { leftView, centerView, rightView })
                if (v != null) Views.Add(v);
        }

        /// <summary>The left or right side camera, or null when the rig isn't built.</summary>
        public Camera Wing(bool right) => built && sides.Count == 2 ? sides[right ? 1 : 0].Camera : null;

        /// <summary>The side camera whose part of the window holds a screen point, or null.</summary>
        public Camera WingAt(Vector3 screenPoint)
        {
            foreach (bool right in new[] { false, true })
            {
                Camera wing = Wing(right);
                if (wing != null && wing.enabled && wing.pixelRect.Contains(screenPoint)) return wing;
            }
            return null;
        }

        /// <summary>How many HUD screen points the rig has moved onto a side screen or a seam, for status.</summary>
        public long SideAnswers { get; private set; }

        /// <summary>
        /// Where a world point lands in the window, for the game's HUD, which asks the main camera.
        /// That camera only covers the center screen, so a point on a side screen gets that side
        /// camera's answer. A point in a bezel gap is held at the seam until the next screen shows
        /// it, and one beyond a side screen gets that camera's answer, off the window's edge, for
        /// the HUD's off-screen arrows. Everything else, including points behind the eye, keeps
        /// the main camera's answer.
        /// </summary>
        public Vector3 ScreenPoint(Camera camera, Vector3 world, Vector3 answer)
        {
            if (!built || main == null || camera != main || answer.z <= 0 || sides.Count < 2) return answer;
            Rect center = main.pixelRect;
            if (center.Contains(answer)) return answer;

            bool right = answer.x >= center.xMax;
            if (!right && answer.x >= center.xMin) return answer;
            Camera side = sides[right ? 1 : 0].Camera;
            if (side == null || !side.enabled) return answer;

            // The side camera's own answer; the hook ignores it, as it isn't the main camera.
            Vector3 there = side.WorldToScreenPoint(world);
            if (there.z <= 0) return answer;
            SideAnswers++;
            Rect rect = side.pixelRect;
            if (rect.Contains(there)) return there;
            bool inGap = right ? there.x < rect.xMin : there.x >= rect.xMax;
            return inGap ? new Vector3(right ? center.xMax - 0.5f : center.xMin + 0.5f, answer.y, answer.z) : there;
        }

        readonly Vector4[] cullPlanes = new Vector4[6];
        PixelRect lastLeft, lastCenter, lastRight;
        bool haveRects;

        /// <summary>The panels as they physically are, with the real eye at Geometry.EyeDistanceMm; null if they can't be built.</summary>
        public List<Panel> PhysicalPanels() =>
            haveRects && Layout.TryBuild(config.PhysicalLayout(lastLeft, lastCenter, lastRight), out List<Panel> panels, out _) ? panels : null;

        /// <summary>
        /// How much bigger a HUD drawn at a screen point on a wing must be to look as big, from the
        /// real eye, as at the middle of the center screen. Null without a physical layout.
        /// </summary>
        public double? PhysicalSizeAt(Camera wing, Vector3 screenPoint)
        {
            List<Panel> physical = PhysicalPanels();
            ViewState view = Views.FirstOrDefault(v => v.Camera == wing);
            if (physical == null || physical.Count != 3 || view == null || view.Name == "center") return null;
            Rect r = wing.pixelRect;
            double fx = (screenPoint.x - r.x) / r.width, fy = (screenPoint.y - r.y) / r.height;
            ViewportMapping m = view.Viewport;
            int index = view.Name == "left" ? 0 : 2;
            PixelRect monitor = index == 0 ? lastLeft : lastRight;
            return HudBend.PhysicalScale(physical[index], m.U0 + fx * (m.U1 - m.U0), m.V0 + fy * (m.V1 - m.V0),
                monitor.Width / physical[index].Width, physical[1], lastCenter.Width / physical[1].Width);
        }

        /// <summary>
        /// Right after the game sets the vegetation cull planes from the main camera, which covers
        /// only the center screen, replaces them with planes that hold every screen's lit area plus
        /// the margin. Left, right, bottom and top come from the views; the near plane is moved far
        /// behind the eye, and the far plane is the camera's own.
        /// </summary>
        public void WidenVegetationCull(Camera camera)
        {
            if (!built || main == null || camera != main || Views.Count == 0) return;
            var corners = new List<Vec3>();
            foreach (ViewState v in Views)
            {
                Panel lit = v.Panel.Sub(v.Viewport.U0, v.Viewport.V0, v.Viewport.U1, v.Viewport.V1);
                corners.Add(lit.LowerLeft);
                corners.Add(lit.LowerRight);
                corners.Add(lit.UpperLeft);
                corners.Add(lit.UpperRight);
            }
            Vec3?[] normals = CullPlanes.Around(corners, config.VegetationMarginDeg.Value);

            Transform eye = main.transform;
            Vector3 position = eye.position, forward = eye.forward;
            // Where no plane can bound the view, one far behind the eye that everything passes.
            Vector4 Pass() => new Vector4(forward.x, forward.y, forward.z, -Vector3.Dot(forward, position) + 1e6f);
            for (int i = 0; i < 4; i++)
            {
                if (!(normals[i] is Vec3 n))
                {
                    cullPlanes[i] = Pass();
                    continue;
                }
                Vector3 normal = eye.rotation * new Vector3((float)n.X, (float)n.Y, (float)n.Z);
                cullPlanes[i] = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, position));
            }
            cullPlanes[4] = Pass();
            cullPlanes[5] = new Vector4(-forward.x, -forward.y, -forward.z, Vector3.Dot(forward, position) + main.farClipPlane);
            Shader.SetGlobalVectorArray(NuclearOption.Effects.ShaderGlobalManager.ID_Global_FrustumPlanes, cullPlanes);
            WidenedCulls++;
        }

        /// <summary>How many frames the vegetation cull has been widened, for status.</summary>
        public long WidenedCulls { get; private set; }

        // The panels for the current settings; while those can't be rendered, for example half-way
        // through typing a value in F1, the last ones that could. Null until a layout has worked.
        List<Panel> Panels(LayoutSpec spec)
        {
            if (Layout.TryBuild(spec, out List<Panel> panels, out string problem))
            {
                if (rejected != null) log.LogInfo("Geometry settings usable again; the rig follows them");
                rejected = null;
                lastGood = panels;
                return panels;
            }
            if (problem != rejected)
                log.LogWarning($"Geometry settings can't be rendered: {problem}. " +
                               (lastGood != null ? "Keeping the last layout that worked" : "The game's own view stays") + " until they change.");
            rejected = problem;
            return lastGood;
        }

        ViewState LayOutCenter(Panel panel, ViewportMapping viewport)
        {
            if (viewport.IsEmpty) return null;
            Frustum frustum = panel.Sub(viewport.U0, viewport.V0, viewport.U1, viewport.V1).FrustumFrom(Vec3.Zero);
            Rect rect = ToRect(viewport);
            float fov = (float)frustum.EquivalentVerticalFovDeg;

            main.rect = rect;
            main.fieldOfView = fov;
            main.projectionMatrix = Projection(frustum, main.nearClipPlane, main.farClipPlane);
            // URP rescales an overlay's projection whenever its rect differs from its base camera's,
            // so the game's own overlays get the same rect, and the frustum at their own clip planes.
            foreach (Camera overlay in gameOverlays)
            {
                if (overlay == null) continue;
                overlay.rect = rect;
                overlay.projectionMatrix = Projection(frustum, overlay.nearClipPlane, overlay.farClipPlane);
            }
            return new ViewState { Name = "center", Camera = main, Panel = panel, Frustum = frustum, Viewport = viewport };
        }

        ViewState LayOutSide(SideView side, Panel panel, ViewportMapping viewport, float fov, bool slowCopy)
        {
            if (side.Root == null) return null;
            bool visible = !viewport.IsEmpty;
            if (side.Camera.enabled != visible) side.Camera.enabled = visible;
            if (!visible) return null;

            Frustum frustum = panel.Sub(viewport.U0, viewport.V0, viewport.U1, viewport.V1).FrustumFrom(Vec3.Zero);
            Rect rect = ToRect(viewport);
            side.Root.transform.localRotation = Quaternion.LookRotation(ToUnity(panel.Forward), ToUnity(panel.Up));

            CameraCopy.Settings(main, side.Camera);
            if (slowCopy) CameraCopy.LayerCullDistances(main, side.Camera);
            side.Camera.rect = rect;
            // Every camera gets the same fieldOfView value, so LOD choices agree across the seams.
            side.Camera.fieldOfView = fov;
            side.Camera.projectionMatrix = Projection(frustum, side.Camera.nearClipPlane, side.Camera.farClipPlane);

            foreach (OverlayPair pair in side.Overlays)
            {
                if (pair.Source == null || pair.Clone == null) continue;
                if (pair.Clone.enabled != pair.Source.enabled) pair.Clone.enabled = pair.Source.enabled;
                CameraCopy.Settings(pair.Source, pair.Clone);
                if (slowCopy) CameraCopy.LayerCullDistances(pair.Source, pair.Clone);
                pair.Clone.rect = rect;
                pair.Clone.fieldOfView = fov;
                pair.Clone.projectionMatrix = Projection(frustum, pair.Clone.nearClipPlane, pair.Clone.farClipPlane);
            }
            return new ViewState { Name = side.Role, Camera = side.Camera, Panel = panel, Frustum = frustum, Viewport = viewport };
        }

        public string Describe()
        {
            if (!built) return "rig not built";
            var text = new StringBuilder($"rig on '{main.name}', game FOV {gameFov:F2}");
            if (rejected != null)
                text.Append($", settings rejected ({rejected}), {(lastGood != null ? "keeping the last layout that worked" : "game's own view")}");
            foreach (ViewState v in Views)
                text.Append($"; {v.Name}: rect ({v.Viewport.X:F3}, {v.Viewport.Y:F3}, {v.Viewport.Width:F3}, {v.Viewport.Height:F3}), " +
                            $"{v.Frustum.HorizontalFovDeg:F1}° x {v.Frustum.VerticalFovDeg:F1}°");
            return text.ToString();
        }

        static Rect ToRect(ViewportMapping v) => new Rect((float)v.X, (float)v.Y, (float)v.Width, (float)v.Height);

        static Vector3 ToUnity(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        // Only the off-axis shift goes into the projection; the camera's rotation faces the panel.
        static Matrix4x4 Projection(Frustum f, float near, float far) => Matrix4x4.Frustum(
            (float)(f.Left * near), (float)(f.Right * near), (float)(f.Bottom * near), (float)(f.Top * near), near, far);
    }
}
