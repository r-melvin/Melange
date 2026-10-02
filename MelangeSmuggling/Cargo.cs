using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using UnityEngine;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;

namespace Melange.Smuggling
{
    /// <summary>
    /// Moving goods between the player and the boat. A delivery takes every matching product stack from the player's
    /// pockets and from the trunk of the vehicle they last drove, if it is parked by the boat (huge orders don't fit in
    /// pockets), up to what the hold takes. The hold itself is the save's record, not a game storage: the boat is not a
    /// networked object, so nothing on it can hold real items.
    /// </summary>
    internal static class Cargo
    {
        /// <summary>How close a parked vehicle has to be to the boat for its trunk to count.</summary>
        private const float TrunkReach = 15f;

        public sealed class Pick
        {
            public ItemSlot Slot;
            public int Items;
            public int Units;
            public string ProductId, ProductName;
            public int Quality;
        }

        /// <summary>The stacks a delivery would take now, in pockets-then-trunk order, without touching anything.</summary>
        public static List<Pick> Plan(SmugglingState s, int now, SmugglingRules r)
        {
            var picks = new List<Pick>();
            int pending = 0;
            foreach (var slot in Slots())
            {
                var product = slot?.ItemInstance?.TryCast<ProductItemInstance>();
                var def = product?.Definition?.TryCast<ProductDefinition>();
                if (product == null || def == null || slot.Quantity <= 0) continue;
                int perItem = Math.Max(1, product.Amount);
                int quality = (int)product.Quality;
                int items = Orders.ItemsTaken(s.Order, s, now, def.DrugType.ToString(), quality, perItem, slot.Quantity, r, pending);
                if (items <= 0) continue;
                int units = items * perItem;
                pending += units;
                picks.Add(new Pick { Slot = slot, Items = items, Units = units, ProductId = def.ID, ProductName = def.Name, Quality = quality });
            }
            return picks;
        }

        /// <summary>Takes the planned items out of the slots (they're aboard or seized either way).</summary>
        public static void Take(List<Pick> picks)
        {
            foreach (var p in picks)
            {
                try { p.Slot.ChangeQuantity(-p.Items); }
                catch (Exception e) { Mod.Log.Warning($"couldn't take {p.Items} x {p.ProductName}: {e.Message}"); p.Units = 0; }
            }
        }

        private static IEnumerable<ItemSlot> Slots()
        {
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv != null)
            {
                var slots = inv.GetAllInventorySlots();
                if (slots != null) for (int i = 0; i < slots.Count; i++) yield return slots[i];
            }
            var me = GamePlayer.Local;
            var car = me != null ? me.LastDrivenVehicle : null;
            if (car == null || car.Storage == null || me.IsInVehicle) yield break;
            if (Vector3.Distance(car.transform.position, Boat.Position) > TrunkReach) yield break;
            var trunk = car.Storage.ItemSlots;
            if (trunk != null) for (int i = 0; i < trunk.Count; i++) yield return trunk[i];
        }

        /// <summary>
        /// Puts up to <paramref name="quantity"/> of an item into the player's pockets, a stack at a time while it fits.
        /// Returns how many went in (0 if the item ID is unknown to this game).
        /// </summary>
        public static int Give(string itemId, int quantity)
        {
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            var def = Registry.GetItem(itemId);
            if (inv == null || def == null || quantity <= 0) return 0;
            int stack = Math.Max(1, def.StackLimit);
            int given = 0;
            while (given < quantity)
            {
                int n = Math.Min(stack, quantity - given);
                var item = def.GetDefaultInstance(n);
                if (item == null || !inv.CanItemFitInInventory(item, n)) break;
                inv.AddItemToInventory(item);
                given += n;
            }
            return given;
        }

        /// <summary>How many of an item the local player carries (for the probes).</summary>
        public static int CountOnPlayer(string itemId)
        {
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            return inv == null ? 0 : (int)inv.GetAmountOfItem(itemId);
        }

        /// <summary>Empty pocket (hotbar) slots, the cash slot not counted (for the probes).</summary>
        public static int FreePocketSlots()
        {
            var slots = PlayerSingleton<PlayerInventory>.Instance?.hotbarSlots;
            int n = 0;
            for (int i = 0; slots != null && i < slots.Count; i++) if (slots[i] != null && slots[i].ItemInstance == null) n++;
            return n;
        }

        /// <summary>The item's display name, or its ID when the game doesn't know it.</summary>
        public static string NameOf(string itemId)
        {
            try { var def = Registry.GetItem(itemId); return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : itemId; }
            catch { return itemId; }
        }

        public static bool Exists(string itemId)
        {
            try { return Registry.GetItem(itemId) != null; }
            catch { return false; }
        }

        /// <summary>
        /// What the player makes: each drug type among their discovered products, with the game's average market value
        /// per unit. Orders are only ever for these.
        /// </summary>
        public static List<MarketEntry> Market()
        {
            var sums = new Dictionary<string, (float Total, int Count)>();
            var found = ProductManager.DiscoveredProducts;
            if (found != null)
                for (int i = 0; i < found.Count; i++)
                {
                    var p = found[i];
                    if (p == null || p.MarketValue <= 0f) continue;
                    string type = p.DrugType.ToString();
                    sums.TryGetValue(type, out var acc);
                    sums[type] = (acc.Total + p.MarketValue, acc.Count + 1);
                }
            var list = new List<MarketEntry>();
            foreach (var kv in sums) list.Add(new MarketEntry(kv.Key, kv.Value.Total / kv.Value.Count));
            return list;
        }
    }
}
