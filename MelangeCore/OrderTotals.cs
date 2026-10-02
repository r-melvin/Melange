using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Shop;

namespace Melange.Core
{
    /// <summary>
    /// The order-total pipeline: discounts or surcharges on whole orders (bulk discounts), the counterpart to the per-item
    /// <see cref="Prices"/>. The hub owns the one patch on each game total: a shop's checkout (Cart.GetPriceSum, used for
    /// both the shown total and the charge), a supplier's phone order (PhoneShopInterface.GetOrderTotal) and the delivery
    /// app (DeliveryShop.GetCartCost). All three are loops, not the one-line methods IL2CPP inlines.
    /// </summary>
    public static class OrderTotals
    {
        private static readonly Dictionary<string, OrderModifier> Modifiers = new Dictionary<string, OrderModifier>();

        public static void Register(OrderModifier modifier) => Modifiers[modifier.Id] = modifier;
        public static void Unregister(string id) => Modifiers.Remove(id);

        internal static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, typeof(Cart), "GetPriceSum", nameof(AfterCart));
            TryPatch(harmony, typeof(PhoneShopInterface), "GetOrderTotal", nameof(AfterSupplier));
            TryPatch(harmony, typeof(DeliveryShop), "GetCartCost", nameof(AfterDelivery));
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, Type type, string method, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(OrderTotals), postfix));
            }
            catch (Exception e) { Core.Log.Warning($"order totals ({type.Name}.{method}) are off: {e.Message}"); }
        }

        private static float Apply(float total, OrderChannel channel, string shop, int units)
        {
            if (Modifiers.Count == 0 || total <= 0f) return total;
            var ordered = new List<OrderModifier>(Modifiers.Values);
            ordered.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            float m = OrderMath.Combined(ordered, channel, shop, units, msg => Core.Log?.Warning(msg));
            return (float)Math.Round(total * m, 2);
        }

        private static void AfterCart(Cart __instance, ref float __result)
        {
            try
            {
                int units = 0;
                var cart = __instance.cartDictionary;
                if (cart != null) foreach (var kv in cart) units += kv.Value;
                __result = Apply(__result, OrderChannel.Shop, __instance.Shop != null ? __instance.Shop.ShopName : "", units);
            }
            catch (Exception e) { Core.Log?.Warning("shop order total: " + e.Message); }
        }

        private static void AfterSupplier(ref float __result, ref int itemCount)
        {
            try { __result = Apply(__result, OrderChannel.Supplier, "", itemCount); }
            catch (Exception e) { Core.Log?.Warning("supplier order total: " + e.Message); }
        }

        private static void AfterDelivery(DeliveryShop __instance, ref float __result)
        {
            try
            {
                int units = 0;
                var entries = __instance.listingEntries;
                if (entries != null) for (int i = 0; i < entries.Count; i++) units += entries[i].SelectedQuantity;
                __result = Apply(__result, OrderChannel.Delivery, "", units);
            }
            catch (Exception e) { Core.Log?.Warning("delivery order total: " + e.Message); }
        }
    }
}
