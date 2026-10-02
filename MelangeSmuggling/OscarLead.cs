using System;
using Melange.Core;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Shop;
using GameOscar = Il2CppScheduleOne.NPCs.CharacterClasses.Oscar;

namespace Melange.Smuggling
{
    /// <summary>
    /// The way in. Counts purchases from Oscar through the event his shop already raises on checkout
    /// (<c>ShopInterface.onOrderCompletedWithSpend</c>, invoked by <c>Cart.Buy</c> with the order's total: no patch), and
    /// when the rolled count is reached he passes on Dafydd's number, in a line through the hub's Oscar dialogue service.
    /// A choice on Oscar ("Who's your supplier?") repeats it, or says "keep buying" before then.
    /// </summary>
    /// <remarks>
    /// The checkout runs on the buyer's own machine, so in co-op only the host's purchases are seen (the host decides).
    /// Orders through Oscar's delivery app go through the delivery shop instead and are not expected to fire this (TESTING.md).
    /// </remarks>
    internal static class OscarLead
    {
        private static IntPtr _hookedShop;

        public static void Start()
        {
            OscarDialogue.AddChoice("smuggling.supplier", Lines.OscarChoice, () => Smuggling.State != null, () =>
            {
                var s = Smuggling.State;
                if (s == null) return;
                OscarDialogue.Say(s.Unlocked ? Lines.OscarRepeat : Lines.OscarNotYet);
            });
        }

        public static void Forget() => _hookedShop = IntPtr.Zero;

        /// <summary>Hooks Oscar's shop after a load (the shop is a scene object, new each time).</summary>
        public static void Hook()
        {
            try
            {
                var shop = FindShop();
                if (shop == null) { Mod.Log.Warning("Oscar's shop wasn't found; purchases won't count this session."); return; }
                if (shop.Pointer == _hookedShop) return;
                Il2CppSystem.Action<float> handler = new Action<float>(Spent);
                shop.onOrderCompletedWithSpend = shop.onOrderCompletedWithSpend == null
                    ? handler
                    : Il2CppSystem.Delegate.Combine(shop.onOrderCompletedWithSpend, handler).Cast<Il2CppSystem.Action<float>>();
                _hookedShop = shop.Pointer;
                Mod.Log.Msg($"listening to {shop.ShopName} ({shop.ShopCode}) checkouts");
            }
            catch (Exception e) { Mod.Log.Warning("Oscar's purchases won't count: " + e.Message); }
        }

        private static ShopInterface FindShop()
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null) return null;
            for (int i = 0; i < registry.Count; i++)
            {
                var oscar = registry[i]?.TryCast<GameOscar>();
                if (oscar != null && oscar.ShopInterface != null) return oscar.ShopInterface;
            }
            return null;
        }

        private static void Spent(float spend)
        {
            try { Smuggling.OscarPurchase(spend); }
            catch (Exception e) { Mod.Log.Error("counting an Oscar purchase: " + e); }
        }
    }
}
