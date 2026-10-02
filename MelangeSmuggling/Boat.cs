using System;
using System.Collections.Generic;
using S1API.Interaction;
using UnityEngine;

namespace Melange.Smuggling
{
    /// <summary>
    /// Turnip Night's speedboat: a static model moored at the Docks quay (there are no boats, ships or water AI in the game,
    /// so it is a plain object, not a vehicle), with an interaction on the quay side to load the hold or collect imports.
    /// Sailing is a scripted run out to sea and a hide; returning is a reappearance at the berth. It exists on every peer
    /// (it is only looks and a prompt), but only the host acts on the prompt.
    /// </summary>
    internal static class Boat
    {
        private const string Resource = "MelangeSmuggling.speedboat.mesh.json";
        private const string RootName = "MelangeSmuggling_Speedboat";

        private static GameObject _root, _model, _prompt;
        private static InteractionPrompt _interaction;
        private static Vector3 _berth;
        private static Quaternion _heading;
        private static object _sailing;
        private static MeshFile _file;
        private static bool _fileRead;

        public static bool Placed => _root != null;
        public static Vector3 Position => _berth;

        /// <summary>Leaving a save: the scene and everything in it are gone.</summary>
        public static void Forget()
        {
            if (_sailing != null) { MelonLoader.MelonCoroutines.Stop(_sailing); _sailing = null; }
            try { _interaction?.Dispose(); } catch { }
            _interaction = null; _root = _model = _prompt = null;
        }

        /// <summary>Builds the boat at its berth (once per save), visible when <paramref name="moored"/>.</summary>
        public static void Place(bool moored, Action interacted)
        {
            if (_root != null) { Show(moored); return; }
            var m = Quay.Between(Settings.Berth);
            float quayTop = GroundAt(m.StandX, m.StandZ) ?? -2.5f;
            float water = Settings.Waterline ?? WaterAt(m.BoatX, m.BoatZ, quayTop) ?? quayTop - 1.8f;
            _berth = new Vector3(m.BoatX, water, m.BoatZ);
            _heading = Quaternion.Euler(0f, m.Yaw, 0f);
            Mod.Log.Msg($"boat berth ({_berth.x:0.0}, {_berth.y:0.00}, {_berth.z:0.0}) heading {m.Yaw:0}, quay top {quayTop:0.00}, waterline from {(Settings.Waterline.HasValue ? "settings" : "the scene or a guess")}");

            _root = new GameObject(RootName);
            _root.transform.SetPositionAndRotation(_berth, _heading);
            _model = BuildModel(_root.transform);

            // The prompt sits on the quay edge beside the boat, at the player's height: the game's interaction reach is
            // 4 m, and the deck is below the quay. A trigger, so nobody bumps into it.
            _prompt = new GameObject("MelangeSmuggling_Hold");
            _prompt.transform.SetParent(_root.transform, false);
            var standLocal = _root.transform.InverseTransformPoint(new Vector3(m.StandX, quayTop, m.StandZ));
            _prompt.transform.localPosition = new Vector3(standLocal.x * 0.55f, quayTop - water + 1f, 0f);
            var box = _prompt.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.2f, 2f, 5f);
            _prompt.layer = 0;                                     // Default: assumed to be in the interaction search mask (TESTING.md)
            try
            {
                _interaction = InteractionPrompt.CreateBuilder(_prompt)
                    .WithMessage("Turnip Night's boat")
                    .WithRange(4f)
                    .WithoutAngleLimit()
                    .OnInteractionStarted(() => interacted?.Invoke())
                    .Build();
            }
            catch (Exception e) { Mod.Log.Warning("boat prompt not added: " + e.Message); }
            Show(moored);
        }

        public static string Describe()
        {
            if (_root == null) return "not placed";
            var p = _root.transform.position;
            return $"berth ({_berth.x:0.0},{_berth.y:0.00},{_berth.z:0.0}), now at ({p.x:0.0},{p.y:0.00},{p.z:0.0}), shown {_root.activeSelf}, " +
                   $"model {(_file != null ? "loaded" : "stand-in")}, prompt {(_interaction != null ? "on" : "off")}, sailing {_sailing != null}";
        }

        public static void SetMessage(string text)
        {
            try { _interaction?.SetMessage(text); } catch { }
        }

        public static void Show(bool moored)
        {
            if (_root == null) return;
            if (_sailing != null) return;                       // let a departure finish
            _root.transform.SetPositionAndRotation(_berth, _heading);
            _root.SetActive(moored);
        }

        /// <summary>Casts off: runs out along the quay and away to sea over about 25 seconds, then hides.</summary>
        public static void Sail()
        {
            if (_root == null || !_root.activeSelf) return;
            if (_sailing != null) MelonLoader.MelonCoroutines.Stop(_sailing);
            _sailing = MelonLoader.MelonCoroutines.Start(SailAway());
        }

        private static System.Collections.IEnumerator SailAway()
        {
            var t = _root.transform;
            var start = t.position;
            var forward = _heading * Vector3.forward;
            // out from the quay (the basin's side is the boat's right or left: whichever points to larger x)
            var side = _heading * Vector3.right;
            if (side.x < 0f) side = -side;
            float duration = 25f, elapsed = 0f;
            while (elapsed < duration && _root != null)
            {
                elapsed += Time.deltaTime;
                float k = elapsed / duration;
                float run = 4f * k * k * 60f;                    // speeds up: about 240 m along by the end
                t.position = start + forward * run + side * Mathf.Min(1f, k * 3f) * 6f;
                t.rotation = _heading * Quaternion.Euler(-4f * Mathf.Min(1f, k * 4f), 0f, 0f);   // bow up as she gets going
                yield return null;
            }
            _sailing = null;
            if (_root != null) { _root.SetActive(false); t.SetPositionAndRotation(_berth, _heading); }
        }

        // ---- where the water is ----

        private static float? GroundAt(float x, float z)
        {
            try
            {
                if (Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The water surface over the berth: the top of any renderer named like water whose footprint covers it and that lies
        /// below the quay. The scene's ocean plane (StylizedWater2_Ocean) is the expected match; unverified (TESTING.md).
        /// </summary>
        private static float? WaterAt(float x, float z, float quayTop)
        {
            float? best = null;
            try
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r == null || r.gameObject == null) continue;
                    string n = r.gameObject.name;
                    if (n == null || n.IndexOf("water", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var b = r.bounds;
                    if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                    float top = b.max.y;
                    if (top > quayTop - 0.3f || top < quayTop - 25f) continue;
                    if (best == null || top > best.Value) best = top;
                    Mod.Log.Msg($"water candidate {n} at y {top:0.00}");
                }
            }
            catch (Exception e) { Mod.Log.Warning("looking for the water: " + e.Message); }
            return best;
        }

        // ---- the model ----

        private sealed class MeshFile { public float[] size { get; set; } public Part[] parts { get; set; } }
        private sealed class Part { public float[] color { get; set; } public bool metal { get; set; } public float[] v { get; set; } public float[] n { get; set; } public int[] t { get; set; } }

        private static MeshFile Load()
        {
            if (_fileRead) return _file;
            _fileRead = true;
            try
            {
                using var stream = typeof(Boat).Assembly.GetManifestResourceStream(Resource);
                _file = stream == null ? null : System.Text.Json.JsonSerializer.Deserialize<MeshFile>(stream);
            }
            catch (Exception e) { Mod.Log.Warning($"could not read {Resource}: {e.Message}"); _file = null; }
            return _file;
        }

        /// <summary>
        /// The speedboat from the shared Blender pipeline: one part per colour, Unity axes, origin on the waterline
        /// midships, bow along +Z. Materials are copies of a lit material already in the scene (a fresh material can
        /// render pink under this pipeline). Without the model or a material, a plain box stands in so the berth still works.
        /// </summary>
        private static GameObject BuildModel(Transform parent)
        {
            var model = new GameObject("model");
            model.transform.SetParent(parent, false);
            var file = Load();
            var template = LitTemplate();
            if (file?.parts == null || template == null)
            {
                Mod.Log.Warning($"boat model {(file == null ? "missing" : "has no material to copy")}; a stand-in box is used");
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(model.transform, false);
                box.transform.localScale = new Vector3(2.3f, 1f, 7f);
                box.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                return model;
            }
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
                var mat = new Material(template);
                var c = new Color(p.color[0], p.color[1], p.color[2]);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", p.metal ? 0.8f : 0f);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return model;
        }

        /// <summary>A material on the URP Lit shader, taken from something already loaded, to copy for the boat's colours.</summary>
        private static Material LitTemplate()
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
