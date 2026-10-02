using System;
using System.Collections.Generic;
using UnityEngine;

namespace Melange.Mixers
{
    /// <summary>
    /// What a placed machine looks like. With its model embedded (scripts/art/build_mixers.py, the shared Blender pipeline: one
    /// part per colour, already in Unity axes) the Mk2's body is hidden and the model stands in its place, scaled to its width;
    /// without one, the Mk2's own body is tinted in the tier's colour, so the machines work and can be told apart before the
    /// art exists. Only the body changes: the screen, the light, the clock, the discovery box and the item displays are left
    /// alone, and so are the colliders, so using and picking up work as on the Mk2. Materials are copies of the Mk2's own (a
    /// fresh material can render pink under this pipeline).
    /// </summary>
    internal static class Looks
    {
        private const string Marker = "MelangeMixerModel";
        /// <summary>Turn of the model about its vertical axis, if the Mk2's working side turns out not to be its +Z (unchecked in game).</summary>
        private const float ModelYaw = 0f;
        private static readonly HashSet<IntPtr> _dressed = new HashSet<IntPtr>();
        private static readonly Dictionary<string, MeshFile> _files = new Dictionary<string, MeshFile>();

        public static void Reset() => _dressed.Clear();

        public static void Dress(Machine m)
        {
            var st = m.Station;
            if (_dressed.Contains(st.Pointer)) return;
            _dressed.Add(st.Pointer);
            try
            {
                var body = Body(st);
                if (body.Count == 0) return;
                var file = Load($"MelangeMixers.mixer{m.Tier.Mixers}.mesh.json");
                if (file != null && Model(st, body, file)) return;
                Tint(body, m.Tier.Tint);
            }
            catch (Exception e) { Mod.Log.Warning($"{m.Tier.Name}: look not applied: {e.Message}"); }
        }

        /// <summary>The station's own meshes, without the parts that show state or items.</summary>
        private static List<MeshRenderer> Body(Il2CppScheduleOne.ObjectScripts.MixingStation st)
        {
            var skip = new List<Transform>();
            if (st.DiscoveryBox != null) skip.Add(st.DiscoveryBox.transform);
            if (st.ItemContainer != null) skip.Add(st.ItemContainer);
            if (st.Clock != null) skip.Add(st.Clock.transform);
            if (st.Light != null) skip.Add(st.Light.transform);
            if (st.InputVisuals?.ItemContainer != null) skip.Add(st.InputVisuals.ItemContainer);
            if (st.OutputVisuals?.ItemContainer != null) skip.Add(st.OutputVisuals.ItemContainer);
            var list = new List<MeshRenderer>();
            foreach (var r in st.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                bool skipped = false;
                foreach (var s in skip) if (r.transform.IsChildOf(s)) { skipped = true; break; }
                if (skipped || r.GetComponentInParent<Il2CppScheduleOne.Storage.StoredItem>() != null) continue;
                list.Add(r);
            }
            return list;
        }

        private static void Tint(List<MeshRenderer> body, float[] tint)
        {
            var c = new Color(tint[0], tint[1], tint[2]);
            foreach (var r in body)
            {
                var mats = r.materials;                          // the renderer's own copies, not the Mk2's shared ones
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.Lerp(mat.GetColor("_BaseColor"), c, 0.45f));
                    else if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.Lerp(mat.GetColor("_Color"), c, 0.45f));
                }
                r.materials = mats;
            }
        }

        // ------------------------------------------------------------------ model

        private sealed class MeshFile { public float[] size { get; set; } public Part[] parts { get; set; } }
        private sealed class Part { public float[] color { get; set; } public bool metal { get; set; } public float[] v { get; set; } public float[] n { get; set; } public int[] t { get; set; } }

        private static MeshFile Load(string resource)
        {
            if (_files.TryGetValue(resource, out var f)) return f;
            try
            {
                using var stream = typeof(Looks).Assembly.GetManifestResourceStream(resource);
                f = stream == null ? null : System.Text.Json.JsonSerializer.Deserialize<MeshFile>(stream);
            }
            catch (Exception e) { Mod.Log.Warning($"could not read {resource}: {e.Message}"); f = null; }
            _files[resource] = f;
            return f;
        }

        /// <summary>
        /// The model in place of the body: origin at the bottom centre of its footprint, front along +Z (the pipeline's
        /// conventions, scripts/art/MODELS.md). The models are drawn bigger per tier (up to 4 x 2 cells), but a machine is the
        /// Mk2's placed object and keeps the Mk2's footprint, so the model is scaled down to fit inside the Mk2's body rather
        /// than reach into the next cells. Full size needs a placed object of its own per tier (later).
        /// </summary>
        private static bool Model(Il2CppScheduleOne.ObjectScripts.MixingStation st, List<MeshRenderer> body, MeshFile file)
        {
            if (file.parts == null || file.size == null || file.size.Length < 3 || file.size[0] <= 0f) return false;
            var root = st.transform;
            Bounds local = default; bool first = true;
            foreach (var r in body)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; } else local.Encapsulate(p);
                }
            }
            var template = body[0].sharedMaterial;
            if (first || template == null) return false;
            var model = new GameObject(Marker);
            model.transform.SetParent(root, false);
            model.transform.localPosition = new Vector3(local.center.x, local.min.y, local.center.z);
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            float fit = Mathf.Min(local.size.x / file.size[0], file.size[2] > 0f ? local.size.z / file.size[2] : float.MaxValue);
            model.transform.localScale = Vector3.one * fit;
            foreach (var p in file.parts)
            {
                int nv = p.v.Length / 3;
                var verts = new Vector3[nv]; var norms = new Vector3[nv];
                for (int i = 0; i < nv; i++) { verts[i] = new Vector3(p.v[3 * i], p.v[3 * i + 1], p.v[3 * i + 2]); norms[i] = new Vector3(p.n[3 * i], p.n[3 * i + 1], p.n[3 * i + 2]); }
                var mesh = new Mesh();
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = p.t;
                mesh.RecalculateBounds();
                var go = new GameObject("part");
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mat = new Material(template);
                var c = new Color(p.color[0], p.color[1], p.color[2]);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", p.metal ? 0.8f : 0f);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            foreach (var r in body) r.enabled = false;           // hidden, never removed: the colliders and animations stay
            return true;
        }
    }
}
