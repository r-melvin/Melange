using System;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Melange.Hydro
{
    /// <summary>
    /// What placed items look like. The Grow Tent's (or Pot Sprinkler's) own meshes are hidden, never removed: its colliders,
    /// its built-in light and its plant stay, so placing, using and picking up work as before. The models come from the
    /// shared Blender pipeline (scripts/art/build_hydro.py, build_aero.py), embedded as small mesh files (one part per colour,
    /// already in Unity axes) and built into Unity meshes at runtime; materials are copies of the item's own (a fresh material
    /// can render pink under this pipeline). Without a model file, plainer stand-ins are built from boxes.
    /// </summary>
    /// <remarks>
    /// The plant is moved up into its hole by moving the pot's plant container, which carries the buds and their colliders,
    /// so harvesting by hand still works where the plant is shown.
    /// </remarks>
    internal static class Looks
    {
        private const string Marker = "MelangeHydroModel";
        /// <summary>Plants on a grouped tower are smaller, or twelve full plants would grow through each other.</summary>
        private const float TowerPlantScale = 0.55f;
        private static readonly Color Frame = new Color(0.85f, 0.86f, 0.84f), Channel = new Color(0.16f, 0.17f, 0.18f), Hose = new Color(0.10f, 0.22f, 0.12f);

        private static readonly HashSet<IntPtr> _dressed = new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, float> _nextHide = new Dictionary<IntPtr, float>();
        private static readonly Dictionary<IntPtr, IntPtr> _hosedTo = new Dictionary<IntPtr, IntPtr>();
        private static readonly Dictionary<string, MeshFile> _files = new Dictionary<string, MeshFile>();

        public static void Reset() { _dressed.Clear(); _nextHide.Clear(); _hosedTo.Clear(); }

        // ------------------------------------------------------------------ holes

        public static void DressHole(Hole hole)
        {
            var pot = hole.Pot;
            if (_dressed.Contains(pot.Pointer)) { HideNewParts(pot); return; }
            try
            {
                var pc = pot.PlantContainer;
                if (pc == null) return;
                int index = -1; Hole frame = null;
                if (!hole.Unit.IsFrame && Settings.Placement == Placement.Grouped)
                {
                    index = Grouping.IndexInFrame(hole, out frame);
                    if (frame != null && !_dressed.Contains(frame.Pot.Pointer)) return;   // dress the frame first, then its holes
                }
                var template = Hide(pot);
                if (template == null) return;
                var root = pot.transform;
                var centre = root.InverseTransformPoint(pc.position); centre.y = 0f;
                var model = new GameObject(Marker);
                model.transform.SetParent(root, false);
                model.transform.localPosition = centre;

                if (hole.Unit.IsFrame) DressFrame(hole, model, template, pc);
                else if (frame != null && index > 0) SeatInFrame(hole, frame, index, pc);
                else DressSection(hole, model, template, pc);
                _dressed.Add(pot.Pointer);
            }
            catch (Exception e) { Mod.Log.Warning($"{hole.Unit.Name}: look not applied: {e.Message}"); _dressed.Add(pot.Pointer); }
        }

        /// <summary>A whole tray or tower, its holes in place; the frame's own plant sits in hole 0.</summary>
        private static void DressFrame(Hole hole, GameObject model, Material template, Transform pc)
        {
            bool tower = hole.Kind == HoleKind.Aero;
            if (!Build(model, template, tower ? "aero_tower" : "hydro_tray")) BoxFrame(model, template, tower);
            var holes = Layout.HolesFor(hole.Kind);
            foreach (var (x, y, z) in holes)
            {
                var hm = new GameObject("hole");
                hm.transform.SetParent(model.transform, false);
                hm.transform.localPosition = new Vector3(x, y, z);
                if (!Build(hm, template, "hydro_hole")) Box(hm, template, Channel, new Vector3(0f, -0.03f, 0f), new Vector3(0.14f, 0.06f, 0.14f));
            }
            var (x0, y0, z0) = holes[0];
            pc.position = model.transform.TransformPoint(new Vector3(x0, y0, z0));
            if (tower) pc.localScale = Vector3.one * TowerPlantScale;
        }

        /// <summary>A grouped hole: its own meshes hidden, its plant moved into its hole in the frame, its colliders shrunk to the hole.</summary>
        private static void SeatInFrame(Hole hole, Hole frame, int index, Transform pc)
        {
            var holes = Layout.HolesFor(hole.Kind);
            if (index >= holes.Length) return;
            var frameModel = frame.Pot.transform.Find(Marker);
            if (frameModel == null) return;
            var (x, y, z) = holes[index];
            var at = frameModel.TransformPoint(new Vector3(x, y, z));
            // move the whole pot so its plant lands in the hole: its colliders and interaction point go with it
            hole.Pot.transform.position += at - pc.position;
            if (hole.Kind == HoleKind.Aero) pc.localScale = Vector3.one * TowerPlantScale;
            ShrinkColliders(hole.Pot, pc, 0.25f);
            if (Settings.Verbose) Mod.Log.Msg($"{hole.Unit.Name} {hole.Guid}: hole {index} of {frame.Unit.Name} {frame.Guid}");
        }

        /// <summary>A lone section draws its own share of the frame: a length of tray on legs, or a short column with one port.</summary>
        private static void DressSection(Hole hole, GameObject model, Material template, Transform pc)
        {
            bool aero = hole.Kind == HoleKind.Aero;
            float h = aero ? Layout.AeroSectionPortHeight : Layout.SectionHoleHeight;
            if (aero)
            {
                Box(model, template, Frame, new Vector3(0f, h / 2f, 0f), new Vector3(0.24f, h, 0.24f));
                Box(model, template, Channel, new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f));
            }
            else
            {
                Box(model, template, Frame, new Vector3(0f, h - 0.06f, 0f), new Vector3(0.5f, 0.12f, 0.36f));
                foreach (float sx in new[] { -0.2f, 0.2f })
                    Box(model, template, Channel, new Vector3(sx, (h - 0.12f) / 2f, 0f), new Vector3(0.04f, h - 0.12f, 0.3f));
            }
            var hm = new GameObject("hole");
            hm.transform.SetParent(model.transform, false);
            hm.transform.localPosition = new Vector3(0f, h, 0f);
            if (!Build(hm, template, "hydro_hole")) Box(hm, template, Channel, new Vector3(0f, -0.03f, 0f), new Vector3(0.14f, 0.06f, 0.14f));
            pc.position = model.transform.TransformPoint(new Vector3(0f, h, 0f));
        }

        private static void BoxFrame(GameObject model, Material template, bool tower)
        {
            if (tower)
            {
                Box(model, template, Frame, new Vector3(0f, 1.05f, 0f), new Vector3(0.3f, 2.1f, 0.3f));
                Box(model, template, Channel, new Vector3(0f, 0.1f, 0f), new Vector3(0.79f, 0.2f, 0.79f));
            }
            else
            {
                Box(model, template, Frame, new Vector3(0f, 0.55f, 0f), new Vector3(1.45f, 0.1f, 0.4f));
                foreach (float sx in new[] { -0.65f, 0.65f })
                    Box(model, template, Channel, new Vector3(sx, 0.25f, 0f), new Vector3(0.05f, 0.5f, 0.35f));
            }
        }

        /// <summary>Hides the item's own meshes (not the plant, not our model); returns a material to copy, or null.</summary>
        private static Material Hide(Component root)
        {
            Material template = null;
            var pc = root.TryCast<Pot>()?.PlantContainer;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || Ours(r.transform, root.transform) || (pc != null && r.transform.IsChildOf(pc))) continue;
                if (template == null) template = r.sharedMaterial;
                r.enabled = false;
            }
            return template;
        }

        /// <summary>Additive displays and soil appear on the pot later; hide them too, every few seconds.</summary>
        private static void HideNewParts(Pot pot)
        {
            if (_nextHide.TryGetValue(pot.Pointer, out float t) && Time.unscaledTime < t) return;
            _nextHide[pot.Pointer] = Time.unscaledTime + 5f;
            try { Hide(pot); } catch { }
        }

        private static bool Ours(Transform t, Transform root)
        {
            var model = root.Find(Marker);
            return model != null && t.IsChildOf(model);
        }

        private static void ShrinkColliders(Pot pot, Transform pc, float size)
        {
            foreach (var c in pot.GetComponentsInChildren<BoxCollider>(true))
            {
                if (c == null || c.transform.IsChildOf(pc)) continue;
                var s = c.size;
                var scale = c.transform.lossyScale;
                float sx = size / Mathf.Max(0.001f, Mathf.Abs(scale.x)), sz = size / Mathf.Max(0.001f, Mathf.Abs(scale.z));
                c.size = new Vector3(Mathf.Min(s.x, sx), s.y, Mathf.Min(s.z, sz));
            }
        }

        // ------------------------------------------------------------------ pump

        /// <summary>The pump's model, and its hose to the tap (redrawn if the tap changes).</summary>
        public static void DressPump(BuildableItem item, Tap tap)
        {
            try
            {
                if (!_dressed.Contains(item.Pointer))
                {
                    var template = Hide(item);
                    if (template == null) return;
                    var model = new GameObject(Marker);
                    model.transform.SetParent(item.transform, false);
                    if (!Build(model, template, "pump")) Box(model, template, Frame, new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.3f, 0.29f));
                    _dressed.Add(item.Pointer);
                }
                IntPtr tapPtr = tap == null ? IntPtr.Zero : tap.Pointer;
                if (_hosedTo.TryGetValue(item.Pointer, out var had) && had == tapPtr) return;
                _hosedTo[item.Pointer] = tapPtr;
                var pumpModel = item.transform.Find(Marker);
                var old = pumpModel?.Find("Hose");
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
                if (tap == null || pumpModel == null) return;
                var mat = pumpModel.GetComponentInChildren<MeshRenderer>()?.sharedMaterial;
                if (mat == null) return;
                var (sx, sy, sz) = Layout.PumpSpigot;
                DrawHose(pumpModel, mat, pumpModel.TransformPoint(new Vector3(sx, sy, sz)), tap.transform.position);
            }
            catch (Exception e) { Mod.Log.Warning("pump look: " + e.Message); _dressed.Add(item.Pointer); }
        }

        /// <summary>A hose sagging between two points, as a chain of short dark-green segments.</summary>
        private static void DrawHose(Transform parent, Material template, Vector3 from, Vector3 to)
        {
            var hose = new GameObject("Hose");
            hose.transform.SetParent(parent, false);
            const int segments = 12;
            float sag = Mathf.Min(0.6f, Vector3.Distance(from, to) * 0.15f);
            Vector3 Point(int i)
            {
                float t = (float)i / segments;
                return Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }
            var mat = Tint(template, Hose);
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Point(i), b = Point(i + 1);
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(seg.GetComponent<Collider>());
                seg.transform.SetParent(hose.transform, true);
                seg.transform.position = (a + b) / 2f;
                seg.transform.rotation = Quaternion.LookRotation(b - a);
                seg.transform.localScale = new Vector3(0.03f, 0.03f, (b - a).magnitude + 0.01f);
                seg.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        // ------------------------------------------------------------------ mesh files

        private sealed class MeshFile { public float[] size { get; set; } public Part[] parts { get; set; } }
        private sealed class Part { public float[] color { get; set; } public bool metal { get; set; } public float[] v { get; set; } public float[] n { get; set; } public int[] t { get; set; } }

        private static MeshFile Load(string name)
        {
            if (_files.TryGetValue(name, out var file)) return file;
            try
            {
                using var stream = typeof(Looks).Assembly.GetManifestResourceStream($"MelangeHydro.{name}.mesh.json");
                if (stream == null) Mod.Log.Warning($"the {name} model is not embedded; using a plainer look");
                else file = System.Text.Json.JsonSerializer.Deserialize<MeshFile>(stream);
            }
            catch (Exception e) { Mod.Log.Warning($"could not read the {name} model: {e.Message}"); }
            _files[name] = file;
            return file;
        }

        /// <summary>The model, one child per colour, at the parent's origin (the model's origin is its bottom centre).</summary>
        private static bool Build(GameObject parent, Material template, string name)
        {
            var file = Load(name);
            if (file?.parts == null) return false;
            foreach (var p in file.parts)
            {
                if (p?.v == null || p.n == null || p.t == null || p.color == null) continue;
                int nv = p.v.Length / 3;
                var verts = new Vector3[nv]; var norms = new Vector3[nv];
                for (int i = 0; i < nv; i++)
                {
                    verts[i] = new Vector3(p.v[3 * i], p.v[3 * i + 1], p.v[3 * i + 2]);
                    norms[i] = new Vector3(p.n[3 * i], p.n[3 * i + 1], p.n[3 * i + 2]);
                }
                var mesh = new Mesh();
                if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = p.t;
                mesh.RecalculateBounds();
                var go = new GameObject("part");
                go.transform.SetParent(parent.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mat = Tint(template, new Color(p.color[0], p.color[1], p.color[2]));
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", p.metal ? 0.8f : 0f);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return true;
        }

        private static Material Tint(Material template, Color c)
        {
            var mat = new Material(template);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            return mat;
        }

        private static void Box(GameObject parent, Material template, Color color, Vector3 at, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());     // the item's own colliders do the interacting
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Tint(template, color);
        }
    }
}
