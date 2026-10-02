using System;
using System.Collections.Generic;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Building the spoke's models in the game: the shared pipeline's .mesh.json files (scripts/art/MODELS.md: one part per
    /// colour, Unity axes, origin at the bottom centre, front +Z) turned into meshes with copies of a material the game already
    /// renders (a fresh material can render pink under this pipeline). Also the bits every interactable needs: a collider on
    /// a layer the game's interaction search looks at.
    /// </summary>
    internal static class Looks
    {
        private sealed class MeshFile { public float[] size { get; set; } public Part[] parts { get; set; } }
        private sealed class Part { public float[] color { get; set; } public bool metal { get; set; } public float[] v { get; set; } public float[] n { get; set; } public int[] t { get; set; } }

        private static readonly Dictionary<string, MeshFile> _files = new Dictionary<string, MeshFile>();
        private static readonly Dictionary<string, List<(Mesh mesh, Color color, bool metal)>> _meshes = new Dictionary<string, List<(Mesh, Color, bool)>>();
        private static Material _template;
        private static int _layer = -1;
        private static GameObject _prefabRoot;

        /// <summary>Leaving a save: scene materials go with the scene. Meshes and prefab sources are ours and kept.</summary>
        public static void Reset() { _template = null; _layer = -1; }

        /// <summary>The model's size (x, y, z in metres) or null when its file is missing.</summary>
        public static Vector3? Size(string model)
        {
            var f = Load(model);
            if (f?.size == null || f.size.Length < 3) return null;
            return new Vector3(f.size[0], f.size[1], f.size[2]);
        }

        /// <summary>The model built under a new object (origin at its feet), or null when its file is missing.</summary>
        public static GameObject Build(string model, Transform parent, string name)
        {
            var parts = Meshes(model);
            var template = Template();
            if (parts == null || template == null) return null;
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            foreach (var (mesh, color, metal) in parts)
            {
                var go = new GameObject("part");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Tinted(template, color, metal);
            }
            return root;
        }

        /// <summary>A plain coloured box (for props with no model: flakes of venom, a blotter sheet, Randy's crates).</summary>
        public static GameObject Box(Transform parent, string name, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            var template = Template();
            var r = go.GetComponent<MeshRenderer>();
            if (template != null && r != null) r.sharedMaterial = Tinted(template, color, false);
            return go;
        }

        /// <summary>
        /// A source object for S1API product visuals: built once, kept alive and active far below the map (S1API clones it; the
        /// icon capture needs enabled renderers).
        /// </summary>
        public static GameObject PrefabSource(string name, Func<Transform, GameObject> build)
        {
            if (_prefabRoot == null)
            {
                _prefabRoot = new GameObject("MelangePsychedelics prefab sources");
                UnityEngine.Object.DontDestroyOnLoad(_prefabRoot);
                _prefabRoot.transform.position = new Vector3(0f, -20000f, 0f);
            }
            var existing = _prefabRoot.transform.Find(name);
            if (existing != null) return existing.gameObject;
            var go = build(_prefabRoot.transform);
            if (go != null) go.name = name;
            return go;
        }

        /// <summary>Gives a target a box collider on the game's interaction layer, so an S1API prompt can be found by the look raycast.</summary>
        public static BoxCollider Interactable(GameObject target, Vector3 center, Vector3 size)
        {
            var box = target.GetComponent<BoxCollider>();
            if (box == null) box = target.AddComponent<BoxCollider>();   // not ??: a Unity null isn't a C# null
            box.center = center;
            box.size = size;
            target.layer = InteractionLayer();
            return box;
        }

        /// <summary>
        /// The layer for our interactables: Default when the game's interaction search includes it, else the lowest layer it
        /// does include. Read from the game's InteractionManager, since the mask is set in the scene, not in code.
        /// </summary>
        public static int InteractionLayer()
        {
            if (_layer >= 0) return _layer;
            int mask = 1;
            try
            {
                var im = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Interaction.InteractionManager>.Instance;
                if (im != null) mask = im.Interaction_SearchMask.value;
            }
            catch (Exception e) { Mod.Log.Warning("interaction mask not read, using Default: " + e.Message); }
            _layer = 0;
            if ((mask & 1) == 0) for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { _layer = i; break; }
            Mod.Log.Msg($"interactables on layer {_layer} ({LayerMask.LayerToName(_layer)}), search mask {mask:X}");
            return _layer;
        }

        /// <summary>The ground under a point (raycast down from above), or the point itself when nothing is hit.</summary>
        public static Vector3 Ground(Vector3 p)
        {
            try
            {
                if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            }
            catch { }
            return p;
        }

        // ------------------------------------------------------------------ internals

        private static MeshFile Load(string model)
        {
            string resource = $"MelangePsychedelics.{model}.mesh.json";
            if (_files.TryGetValue(resource, out var f)) return f;
            try
            {
                using var stream = typeof(Looks).Assembly.GetManifestResourceStream(resource);
                f = stream == null ? null : System.Text.Json.JsonSerializer.Deserialize<MeshFile>(stream);
            }
            catch (Exception e) { Mod.Log.Warning($"could not read {resource}: {e.Message}"); f = null; }
            if (f == null) Mod.Log.Warning($"model {model} missing; using the plain look");
            _files[resource] = f;
            return f;
        }

        private static List<(Mesh, Color, bool)> Meshes(string model)
        {
            if (_meshes.TryGetValue(model, out var list)) return list;
            var file = Load(model);
            if (file?.parts == null) { _meshes[model] = null; return null; }
            list = new List<(Mesh, Color, bool)>();
            foreach (var p in file.parts)
            {
                if (p?.v == null || p.n == null || p.t == null) continue;
                int nv = p.v.Length / 3;
                var verts = new Vector3[nv]; var norms = new Vector3[nv];
                for (int i = 0; i < nv; i++)
                {
                    verts[i] = new Vector3(p.v[3 * i], p.v[3 * i + 1], p.v[3 * i + 2]);
                    norms[i] = new Vector3(p.n[3 * i], p.n[3 * i + 1], p.n[3 * i + 2]);
                }
                var mesh = new Mesh { name = model };
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = p.t;
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();                      // the game's icon lighting wants tangents on lit meshes
                UnityEngine.Object.DontDestroyOnLoad(mesh);
                var c = p.color != null && p.color.Length >= 3 ? new Color(p.color[0], p.color[1], p.color[2]) : Color.grey;
                list.Add((mesh, c, p.metal));
            }
            _meshes[model] = list;
            return list;
        }

        private static readonly Dictionary<string, Material> _tinted = new Dictionary<string, Material>();

        private static Material Tinted(Material template, Color c, bool metal)
        {
            string key = $"{template.GetInstanceID()}:{c.r:F3},{c.g:F3},{c.b:F3}:{metal}";
            if (_tinted.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(template);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal ? 0.8f : 0f);
            UnityEngine.Object.DontDestroyOnLoad(m);
            _tinted[key] = m;
            return m;
        }

        /// <summary>
        /// A lit material the game renders, to copy: the first scene renderer using the pipeline's Lit shader, else a fresh
        /// one on that shader. Kept across scenes as a copy, since the scene's own goes with it.
        /// </summary>
        private static Material Template()
        {
            if (_template != null) return _template;
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    var mat = r != null ? r.sharedMaterial : null;
                    if (mat?.shader != null && mat.shader.name == "Universal Render Pipeline/Lit")
                    {
                        _template = new Material(mat);
                        break;
                    }
                }
                if (_template == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader != null) _template = new Material(shader);
                }
                if (_template != null) UnityEngine.Object.DontDestroyOnLoad(_template);
            }
            catch (Exception e) { Mod.Log.Warning("no material to copy: " + e.Message); }
            return _template;
        }
    }
}
