using System;
using System.Collections.Generic;
using UnityEngine;

namespace Melange.Smuggling
{
    /// <summary>
    /// The smuggler's pier at the berth (<see cref="PierLayout"/>): a floating pontoon on the water line between the quay
    /// wall and the boat, and a staircase down the wall face from a landing on the quay top. A static model with box
    /// colliders for the deck, the landing and the stair (one smooth ramp), the handrails, and low invisible guards on the
    /// pontoon's open edges. The colliders take the layer of the quay's own ground collider, so the player stands on them as
    /// on the quay. Built on every peer (it is only scenery); it stays when the boat sails.
    /// </summary>
    internal static class Pier
    {
        private const string Resource = "MelangeSmuggling.pier.mesh.json";
        private const string RootName = "MelangeSmuggling_Pier";
        private const float Step = 0.1f;

        private static GameObject _root;
        private static PierLayout.Placement _at;
        private static float _edge;
        private static string _edgeFrom = "", _model = "";
        private static int _layer;
        private static readonly List<BoxCollider> _colliders = new List<BoxCollider>();

        public static bool Placed => _root != null;

        public static void Forget()
        {
            _root = null; _at = null;
            _colliders.Clear();
        }

        /// <summary>
        /// Builds the pier at a mooring (once per save) and returns where it put it (the boat goes alongside), or null if
        /// it couldn't be built. <paramref name="layer"/> is the quay ground's layer.
        /// </summary>
        public static PierLayout.Placement Place(Quay.Mooring m, float quayTop, float water, int layer)
        {
            if (_root != null) return _at;
            try
            {
                float? measured = MeasureEdge(m, quayTop);
                _edge = PierLayout.Edge(Settings.QuayEdge, measured);
                _edgeFrom = Settings.QuayEdge.HasValue && Math.Abs(_edge - Settings.QuayEdge.Value) < 0.001f ? "settings"
                          : measured.HasValue && Math.Abs(_edge - measured.Value) < 0.001f ? "measured" : "fallback";
                _at = PierLayout.Place(m, _edge, quayTop, water);
                _layer = layer;

                _root = new GameObject(RootName);
                _root.transform.SetPositionAndRotation(new Vector3(_at.X, _at.Y, _at.Z), Quaternion.Euler(0f, _at.Yaw, 0f));
                BuildModel(_root.transform);
                BuildColliders(_root.transform, layer);

                float floatsAt = _at.DeckY - PierLayout.Freeboard;
                Mod.Log.Msg($"pier at ({_at.X:0.0}, {_at.Y:0.00}, {_at.Z:0.0}) yaw {_at.Yaw:0}, wall face {_edge:0.00} m out from the bollards ({_edgeFrom}" +
                            $"{(measured.HasValue ? $", measured {measured.Value:0.00}" : ", not measured")}), deck y {_at.DeckY:0.00}, {_colliders.Count} colliders on layer " +
                            $"{layer} ({SafeLayerName(layer)}), model {_model}");
                if (Math.Abs(floatsAt - water) > 0.3f)
                    Mod.Log.Warning($"the pier's pontoon floats for water at y {floatsAt:0.00} but the boat's waterline is {water:0.00}: the stair is drawn for a " +
                                    $"{PierLayout.Drop + PierLayout.Freeboard:0.0} m drop from the quay top to the water");
                return _at;
            }
            catch (Exception e)
            {
                Mod.Log.Warning("pier not built: " + e.Message);
                if (_root != null) { try { UnityEngine.Object.Destroy(_root); } catch { } }
                Forget();
                return null;
            }
        }

        /// <summary>A world point in the pier's frame (see <see cref="PierLayout"/>).</summary>
        public static Vector3 World(float x, float y, float z)
        {
            var w = _at.World(x, y, z);
            return new Vector3(w.X, w.Y, w.Z);
        }

        public static string Describe()
        {
            if (!Settings.PierEnabled) return "off (PierEnabled)";
            if (_root == null) return "not placed";
            var p = _root.transform.position;
            int live = 0, walk = 0;
            var walkNames = new List<string>();
            var boxes = PierLayout.Boxes();
            for (int i = 0; i < _colliders.Count; i++)
            {
                var c = _colliders[i];
                bool on = c != null && c.enabled && c.gameObject.activeInHierarchy;
                if (on) live++;
                if (on && i < boxes.Count && boxes[i].Walkable) { walk++; walkNames.Add(boxes[i].Name); }
            }
            return $"at ({p.x:0.0},{p.y:0.00},{p.z:0.0}) yaw {_root.transform.eulerAngles.y:0}, shown {_root.activeInHierarchy}, wall face {_edge:0.00} m ({_edgeFrom}), " +
                   $"deck y {_at.DeckY:0.00}, model {_model}; colliders {live}/{_colliders.Count} live on layer {_layer} ({SafeLayerName(_layer)}), " +
                   $"walkable {walk}/3 [{string.Join(", ", walkNames)}]; down onto the pontoon hits {Hit(PierLayout.Outer - 0.7f, PierLayout.Length / 4f)}, " +
                   $"onto the stair hits {Hit(PierLayout.WallGap + PierLayout.StairWidth / 2f, PierLayout.StairHead + PierLayout.Risers * PierLayout.Tread / 2f)}";
        }

        /// <summary>What a ray straight down at a point of the pier's frame hits first (the pier's own collider by name if all is well).</summary>
        private static string Hit(float x, float z)
        {
            try
            {
                var from = World(x, 2f, z);
                if (!Physics.Raycast(from, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) return "nothing";
                var t = hit.collider.transform;
                string owner = t.parent != null && t.parent.parent == _root.transform ? "pier " : "";
                return $"{owner}{hit.collider.gameObject.name} at y {hit.point.y:0.00}";
            }
            catch (Exception e) { return "threw " + e.Message; }
        }

        private static string SafeLayerName(int layer)
        {
            try { var n = LayerMask.LayerToName(layer); return string.IsNullOrEmpty(n) ? "unnamed" : n; } catch { return "?"; }
        }

        // ---- the wall face ----

        /// <summary>
        /// Where the quay ends: ground heights sampled outward from the bollard line every 10 cm at three points along the
        /// berth (the middle and 3 m either side), each line's drop found by <see cref="PierLayout.EdgeFromProfile"/>; the
        /// median of those found.
        /// </summary>
        private static float? MeasureEdge(Quay.Mooring m, float quayTop)
        {
            var found = new List<float>();
            foreach (float along in new[] { -3f, 0f, 3f })
            {
                var heights = new List<float?>();
                for (int i = 0; i <= 50; i++)
                {
                    float s = i * Step;
                    float x = m.MidX + m.AlongX * along + m.OutX * s, z = m.MidZ + m.AlongZ * along + m.OutZ * s;
                    float? h = null;
                    try
                    {
                        if (Physics.Raycast(new Vector3(x, quayTop + 2f, z), Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) h = hit.point.y;
                    }
                    catch { }
                    heights.Add(h);
                }
                var e = PierLayout.EdgeFromProfile(heights, Step, quayTop);
                if (e.HasValue) found.Add(e.Value);
            }
            if (found.Count == 0) return null;
            found.Sort();
            return found[found.Count / 2];
        }

        // ---- the model and the colliders ----

        private static void BuildModel(Transform parent)
        {
            var built = Models.Build(Resource, parent, "model");
            if (built != null) { _model = "loaded"; return; }
            _model = Models.Has(Resource) ? "stand-in (no material to copy)" : "stand-in (model missing)";
            Mod.Log.Warning($"pier model: {_model}; plain boxes stand in");
            // the visible collider boxes, drawn as plain cubes, so nobody walks on invisible stairs
            var model = new GameObject("model");
            model.transform.SetParent(parent, false);
            foreach (var b in PierLayout.Boxes())
            {
                if (!b.Visible) continue;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
                cube.name = b.Name;
                cube.transform.SetParent(model.transform, false);
                float sy = b.Name == "stair" ? 0.1f : b.SY;          // the ramp's collider is thick; draw just its top
                var c = new Vector3(b.CX, b.CY, b.CZ);
                if (b.Name == "stair")
                {
                    var (a, z) = b.TopLine();
                    c = new Vector3(b.CX, (a.Y + z.Y) / 2f, (a.Z + z.Z) / 2f) + Quaternion.Euler(b.Pitch, 0f, 0f) * Vector3.down * (sy / 2f);
                }
                cube.transform.localPosition = c;
                cube.transform.localRotation = Quaternion.Euler(b.Pitch, 0f, 0f);
                cube.transform.localScale = new Vector3(b.SX, sy, b.SZ);
            }
        }

        private static void BuildColliders(Transform parent, int layer)
        {
            _colliders.Clear();
            var holder = new GameObject("colliders");
            holder.transform.SetParent(parent, false);
            holder.layer = layer;
            foreach (var b in PierLayout.Boxes())
            {
                var go = new GameObject(b.Name);
                go.layer = layer;
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = new Vector3(b.CX, b.CY, b.CZ);
                go.transform.localRotation = Quaternion.Euler(b.Pitch, 0f, 0f);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(b.SX, b.SY, b.SZ);
                _colliders.Add(box);
            }
        }
    }
}
