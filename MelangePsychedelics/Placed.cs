using System;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using UnityEngine;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Psychedelics
{
    /// <summary>
    /// What the terrarium and the blotter frame share: both are clones of the small storage rack (so the game places, saves and
    /// networks them, and handlers can reach their slots), found by a light scan of the owned properties rather than a patch,
    /// with slot filters, a model standing in for the rack, and stored items kept out of sight inside.
    /// </summary>
    internal static class Placed
    {
        public static IEnumerable<BuildableItem> All(string itemId)
        {
            var props = PropertyType.OwnedProperties;
            for (int p = 0; props != null && p < props.Count; p++)
            {
                var bi = props[p]?.BuildableItems;
                for (int i = 0; bi != null && i < bi.Count; i++)
                    if (bi[i] != null && bi[i].ItemInstance?.ID == itemId) yield return bi[i];
            }
        }

        public static StorageEntity Storage(BuildableItem b) => b?.GetComponentInChildren<StorageEntity>(true);

        public static string Guid(BuildableItem b) => b.GUID.ToString();

        /// <summary>Gives every slot the game's own ID filter, so only the listed items go in.</summary>
        public static void Filter(StorageEntity s, params string[] ids)
        {
            var list = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var id in ids) list.Add(id);
            for (int i = 0; i < s.ItemSlots.Count; i++) s.ItemSlots[i].AddFilter(new ItemFilter_ID(list));
        }

        public static int Count(StorageEntity s, string id)
        {
            int n = 0;
            for (int i = 0; i < s.ItemSlots.Count; i++)
            {
                var slot = s.ItemSlots[i];
                if (slot?.ItemInstance != null && slot.ItemInstance.ID == id) n += slot.Quantity;
            }
            return n;
        }

        /// <summary>Takes up to n of an item out of the slots; returns how many it took.</summary>
        public static int Take(StorageEntity s, string id, int n)
        {
            int taken = 0;
            for (int i = 0; i < s.ItemSlots.Count && taken < n; i++)
            {
                var slot = s.ItemSlots[i];
                if (slot?.ItemInstance == null || slot.ItemInstance.ID != id) continue;
                int k = Math.Min(slot.Quantity, n - taken);
                slot.ChangeQuantity(-k);
                taken += k;
            }
            return taken;
        }

        /// <summary>Puts an item in the storage, else in the player's pockets; false when neither has room.</summary>
        public static bool Put(StorageEntity s, ItemInstance inst)
        {
            if (inst == null) return false;
            if (s != null && s.CanItemFit(inst, inst.Quantity)) { s.InsertItem(inst, true); return true; }
            return Items.Give(inst);
        }

        /// <summary>
        /// The model in place of the rack: the rack's meshes hidden (never removed: colliders and the storage interaction
        /// stay), the model scaled to the rack's footprint and stood at the bottom centre. Returns the model and its scale, or
        /// null (the rack keeps its look) when the model is missing.
        /// </summary>
        public static GameObject Dress(BuildableItem b, string model, out float scale)
        {
            scale = 1f;
            var root = b.transform;
            var body = new List<MeshRenderer>();
            foreach (var r in b.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.GetComponentInParent<StoredItem>() == null) body.Add(r);
            var size = Looks.Size(model);
            if (body.Count == 0 || size == null) return null;
            Bounds local = default; bool first = true;
            foreach (var r in body)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (first) { local = new Bounds(p, Vector3.zero); first = false; } else local.Encapsulate(p);
                }
            }
            if (first) return null;
            var go = Looks.Build(model, root, "Melange " + model);
            if (go == null) return null;
            scale = Mathf.Min(local.size.x / size.Value.x, size.Value.z > 0f ? local.size.z / size.Value.z : float.MaxValue);
            if (scale <= 0f || float.IsInfinity(scale)) scale = 1f;
            go.transform.localPosition = new Vector3(local.center.x, local.min.y, local.center.z);
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in body) r.enabled = false;
            return go;
        }

        /// <summary>Stored items show on the rack's shelves; inside a tank or a frame they shouldn't. Run each scan (new items appear).</summary>
        public static void HideStored(BuildableItem b)
        {
            foreach (var si in b.GetComponentsInChildren<StoredItem>(false))
                foreach (var r in si.GetComponentsInChildren<Renderer>(false)) r.enabled = false;
        }

        /// <summary>A prompt target under a model: a child with a box collider on the interaction layer (positions in model units).</summary>
        public static GameObject Target(GameObject model, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(model.transform, false);
            Looks.Interactable(go, center, size);
            return go;
        }

        public static int Day => S1API.GameTime.TimeManager.ElapsedDays;

        // ------------------------------------------------------------------ probes (Probe.cs)

        /// <summary>The placed one nearest the local player (the first when there's no player), or null.</summary>
        public static BuildableItem Nearest(string itemId)
        {
            var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
            BuildableItem best = null; float bestD = float.MaxValue;
            foreach (var b in All(itemId))
            {
                float d = me == null ? 0f : (b.transform.position - me.transform.position).sqrMagnitude;
                if (d < bestD) { best = b; bestD = d; }
            }
            return best;
        }

        /// <summary>
        /// Places one of a grid buildable on the first free tiles of an owned property other than the RV, through the game's
        /// BuildManager (as the build tool would, without taking it from the pockets).
        /// </summary>
        public static string PlaceOnGrid(string itemId)
        {
            var any = Il2CppScheduleOne.Registry.GetItem(itemId);
            if (any == null) return $"{itemId} is not registered";
            var def = any.TryCast<BuildableItemDefinition>();
            if (def == null) return $"{itemId} is not a buildable ({any.GetIl2CppType().FullName})";
            var grid0 = def.BuiltItem?.TryCast<GridItem>();
            if (grid0 == null) return $"{itemId} is not a grid item (built item {(def.BuiltItem == null ? "null" : def.BuiltItem.GetIl2CppType().FullName)})";
            int fx = grid0.FootprintX, fy = grid0.FootprintY;
            Il2CppScheduleOne.Tiles.Grid grid = null; int ox = 0, oy = 0; string where = "";
            var owned = PropertyType.OwnedProperties;
            for (int p = 0; owned != null && p < owned.Count && grid == null; p++)
            {
                var prop = owned[p];
                if (prop?.Grids == null || prop.PropertyCode == "rv") continue;
                for (int g = 0; g < prop.Grids.Count && grid == null; g++)
                {
                    var gr = prop.Grids[g];
                    for (int t = 0; gr?.Tiles != null && t < gr.Tiles.Count && grid == null; t++)
                    {
                        var tile = gr.Tiles[t];
                        bool free = true;
                        for (int dx = 0; dx < fx && free; dx++)
                            for (int dy = 0; dy < fy && free; dy++)
                            {
                                var other = gr.GetTile(new Il2CppScheduleOne.Tiles.Coordinate(tile.x + dx, tile.y + dy));
                                free = other != null && (other.OccupantTiles == null || other.OccupantTiles.Count == 0);
                            }
                        if (free) { grid = gr; ox = tile.x; oy = tile.y; where = prop.PropertyCode; }
                    }
                }
            }
            if (grid == null) return $"no free {fx}x{fy} spot for {itemId} in any owned property but the RV";
            var placed = Il2CppScheduleOne.Building.BuildManager.Instance.CreateGridItem(def.GetDefaultInstance(1), grid, new Vector2(ox, oy), 0, "");
            if (placed == null) return $"CreateGridItem returned null at {where} ({ox},{oy})";
            var at = placed.transform.position;
            return $"placed {itemId} ({fx}x{fy}) at {where} tile ({ox},{oy}), world ({at.x:0.0},{at.y:0.0},{at.z:0.0}), guid {placed.GUID}";
        }
    }
}
