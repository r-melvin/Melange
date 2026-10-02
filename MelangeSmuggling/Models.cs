using System;
using System.Collections.Generic;
using UnityEngine;

namespace Melange.Smuggling
{
    /// <summary>
    /// Models from the shared Blender pipeline (scripts/art), embedded as <c>&lt;name&gt;.mesh.json</c>: one part per colour,
    /// Unity axes, left-handed winding. Built as plain mesh objects with copies of a native game material in each part's colour.
    /// </summary>
    internal static class Models
    {
        private sealed class MeshFile { public float[] size { get; set; } public Part[] parts { get; set; } }
        private sealed class Part { public float[] color { get; set; } public bool metal { get; set; } public float[] v { get; set; } public float[] n { get; set; } public int[] t { get; set; } }

        private static readonly Dictionary<string, MeshFile> _files = new Dictionary<string, MeshFile>();

        /// <summary>Whether the embedded model can be read (it is read once and kept).</summary>
        public static bool Has(string resource) => Load(resource) != null;

        private static MeshFile Load(string resource)
        {
            if (_files.TryGetValue(resource, out var cached)) return cached;
            MeshFile file = null;
            try
            {
                using var stream = typeof(Models).Assembly.GetManifestResourceStream(resource);
                file = stream == null ? null : System.Text.Json.JsonSerializer.Deserialize<MeshFile>(stream);
            }
            catch (Exception e) { Mod.Log.Warning($"could not read {resource}: {e.Message}"); }
            if (file?.parts == null) file = null;
            _files[resource] = file;
            return file;
        }

        /// <summary>
        /// The model as a child of <paramref name="parent"/>, its parts' materials copied from <paramref name="template"/>
        /// (else a URP Lit material already in the scene). Null when the model or a material is missing.
        /// </summary>
        public static GameObject Build(string resource, Transform parent, string name, Material template = null)
        {
            var file = Load(resource);
            template ??= LitTemplate();
            if (file == null || template == null) return null;
            var model = new GameObject(name);
            model.transform.SetParent(parent, false);
            foreach (var p in file.parts)
            {
                if (p?.v == null || p.n == null || p.t == null || p.color == null) continue;
                int nv = p.v.Length / 3;
                var verts = new Vector3[nv]; var norms = new Vector3[nv];
                for (int i = 0; i < nv; i++) { verts[i] = new Vector3(p.v[3 * i], p.v[3 * i + 1], p.v[3 * i + 2]); norms[i] = new Vector3(p.n[3 * i], p.n[3 * i + 1], p.n[3 * i + 2]); }
                var mesh = new Mesh();
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = p.t;
                mesh.RecalculateBounds();
                var go = new GameObject("part");
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Tinted(template, new Color(p.color[0], p.color[1], p.color[2]), p.metal);
            }
            return model;
        }

        /// <summary>A copy of a native material in one flat colour (textures cleared, so the colour is the whole look).</summary>
        public static Material Tinted(Material template, Color c, bool metal)
        {
            var mat = new Material(template);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metal ? 0.8f : 0f);
            return mat;
        }

        /// <summary>Whether a material takes a flat colour the way <see cref="Tinted"/> sets it.</summary>
        public static bool Tintable(Material m) => m != null && (m.HasProperty("_BaseColor") || m.HasProperty("_Color"));

        /// <summary>
        /// A material on the URP Lit shader, taken from something already loaded (a fresh material can render pink under this
        /// pipeline).
        /// </summary>
        public static Material LitTemplate()
        {
            try
            {
                foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (mat == null || mat.shader == null) continue;
                    string n = mat.shader.name;
                    if (n == "Universal Render Pipeline/Lit" || n == "Universal Render Pipeline/Simple Lit") return mat;
                }
            }
            catch (Exception e) { Mod.Log.Warning("no lit material found: " + e.Message); }
            return null;
        }
    }
}
