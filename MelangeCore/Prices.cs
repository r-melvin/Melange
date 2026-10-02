using System;
using System.Collections.Generic;
using Il2CppScheduleOne.UI.Shop;

namespace Melange.Core
{
    /// <summary>
    /// The price pipeline: the one writer of shop listing prices for every Melange spoke. Spokes register modifiers; the hub
    /// composes them in order and writes the listing's own OverridePrice/OverriddenPrice, keeping the listing's original
    /// override and putting it back when no modifier applies. Re-applied after each load (listings are scene objects).
    /// </summary>
    /// <remarks>
    /// Never a postfix on ShopListing.Price: on IL2CPP that one-line getter is inlined and the postfix never runs (found in
    /// game by the cartel spoke, which writes these fields instead).
    /// </remarks>
    public static class Prices
    {
        private sealed class Original { public bool Override; public float Price; }
        private static readonly Dictionary<string, PriceModifier> Modifiers = new Dictionary<string, PriceModifier>();
        private static readonly Dictionary<IntPtr, Original> Originals = new Dictionary<IntPtr, Original>();

        /// <summary>Adds or replaces a modifier and re-prices every listing.</summary>
        public static void Register(PriceModifier modifier)
        {
            Modifiers[modifier.Id] = modifier;
            Refresh();
        }

        public static void Unregister(string id)
        {
            if (Modifiers.Remove(id)) Refresh();
        }

        /// <summary>Re-prices every listing, e.g. when a modifier's inputs changed (a monopoly ended, a discount step rose).</summary>
        public static void Refresh()
        {
            try
            {
                var shops = ShopInterface.AllShops;
                if (shops == null) return;
                var ordered = PriceMath.Ordered(Modifiers.Values);
                for (int s = 0; s < shops.Count; s++)
                {
                    var shop = shops[s];
                    if (shop == null || shop.Listings == null) continue;
                    for (int i = 0; i < shop.Listings.Count; i++)
                    {
                        var listing = shop.Listings[i];
                        if (listing == null || listing.Item == null) continue;
                        if (!Originals.TryGetValue(listing.Pointer, out var original))
                            Originals[listing.Pointer] = original = new Original { Override = listing.OverridePrice, Price = listing.OverridePrice ? listing.OverriddenPrice : listing.Item.BasePurchasePrice };
                        float m = PriceMath.Combined(ordered, shop.ShopCode, listing.Item.ID, msg => Core.Log?.Warning(msg));
                        if (Math.Abs(m - 1f) < 0.0001f)
                        {
                            listing.OverridePrice = original.Override;
                            listing.OverriddenPrice = original.Override ? original.Price : listing.OverriddenPrice;
                        }
                        else
                        {
                            listing.OverridePrice = true;
                            listing.OverriddenPrice = PriceMath.Apply(original.Price, m);
                        }
                    }
                }
            }
            catch (Exception e) { Core.Log?.Warning("price pipeline: " + e.Message); }
        }

        /// <summary>A new scene: its listings are new objects, so forget the old originals and price the new ones.</summary>
        internal static void OnSceneLoaded()
        {
            Originals.Clear();
            Refresh();
        }
    }
}
