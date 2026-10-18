using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// What the sky grid and the tuning lines share: an unlit, vertex-coloured material that draws
    /// over everything already drawn by the cameras that render its layer, and line meshes made of
    /// circles around the eye.
    /// </summary>
    internal static class LineDrawing
    {
        static readonly string[] ShaderNames = { "Hidden/Internal-Colored", "Sprites/Default", "UI/Default" };

        public static GameObject CreateObject(string name, int layer, out string shaderName)
        {
            Shader shader = null;
            shaderName = null;
            foreach (string candidate in ShaderNames)
            {
                shader = Shader.Find(candidate);
                if (shader == null) continue;
                shaderName = candidate;
                break;
            }
            if (shader == null) throw new InvalidOperationException($"No unlit shader for lines; tried {string.Join(", ", ShaderNames)}.");

            // Drawn last, over everything the cameras that render its layer have drawn.
            var material = new Material(shader) { renderQueue = 4000, color = Color.white };
            SetIfPresent(material, "_ZTest", (int)CompareFunction.Always);
            SetIfPresent(material, "_ZWrite", 0);
            SetIfPresent(material, "_Cull", (int)CullMode.Off);
            SetIfPresent(material, "_SrcBlend", (int)BlendMode.One);
            SetIfPresent(material, "_DstBlend", (int)BlendMode.Zero);

            var root = new GameObject(name) { layer = layer };
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<MeshFilter>();
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return root;
        }

        static void SetIfPresent(Material material, string property, int value)
        {
            if (material.HasProperty(property)) material.SetInt(property, value);
        }

        /// <summary>Builds a line mesh of circles around the origin, each given as a unit direction for an angle.</summary>
        public sealed class Circles
        {
            readonly float radius;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Color32> colors = new List<Color32>();
            readonly List<int> indices = new List<int>();

            public Circles(float radius)
            {
                this.radius = radius;
            }

            public void Add(Func<float, Vector3> point, Color32 color, int segments = 180)
            {
                int start = vertices.Count;
                for (int i = 0; i < segments; i++)
                {
                    vertices.Add(point(i * 2 * Mathf.PI / segments) * radius);
                    colors.Add(color);
                }
                for (int i = 0; i < segments; i++)
                {
                    indices.Add(start + i);
                    indices.Add(start + (i + 1) % segments);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetIndices(indices, MeshTopology.Lines, 0);
                // Centered on the camera every frame, so its bounds always contain it and it's never culled.
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 4);
                return mesh;
            }
        }
    }
}
