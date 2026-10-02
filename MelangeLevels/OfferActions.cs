using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Shop;

namespace Melange.Levels
{
    /// <summary>What happens when the player spends Prestige on an offer they can't refuse. Host only: offers change the world.</summary>
    internal static class OfferActions
    {
        private const string WarehouseModifier = "levels.warehouse";

        public static void Start()
        {
            Prices.Register(new PriceModifier
            {
                Id = WarehouseModifier,
                Order = 20,
                Multiplier = (shopCode, itemId) =>
                    IsOscarShop(shopCode) && Offers.WarehouseDiscountActive(MelangeLevelsData.Current?.WarehouseDiscountDay ?? -1, Today())
                        ? 1f - Offers.WarehouseDiscountPercent / 100f : 1f,
            });
            // the discount ends with the day it was bought for
            Events.Subscribe<DayPassed>(_ => Prices.Refresh());
        }

        public static int Today()
        {
            try { return NetworkSingleton<TimeManager>.Instance.ElapsedDays; } catch { return 0; }
        }

        /// <summary>The in-game time of day, 24-hour hhmm (e.g. 2130).</summary>
        public static int Now()
        {
            try { return NetworkSingleton<TimeManager>.Instance.CurrentTime; } catch { return 0; }
        }

        /// <summary>Are the police looking away right now (host's view of the save)?</summary>
        public static bool PoliceLenient()
            => Offers.LenientActive(MelangeLevelsData.Current?.LenientUntil ?? -1, Today(), Now());

        /// <summary>Suppliers with a dead drop on its way (what a rush order can hurry).</summary>
        public static List<Supplier> PendingDrops()
        {
            var list = new List<Supplier>();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Supplier>())
                if (s != null && s.MinsUntilDeaddropReady > 1) list.Add(s);
            return list;
        }

        public static bool RushOrder(Supplier supplier)
        {
            if (!Spend(OfferKind.RushOrder) || supplier == null) return false;
            supplier.MinsUntilDeaddropReady = 1;                   // the game completes it on its next minute tick
            Mod.Log.Msg($"offer: rush order from {supplier.FirstName}");
            return true;
        }

        public static bool WarehouseDiscount()
        {
            if (!Spend(OfferKind.WarehouseDiscount)) return false;
            MelangeLevelsData.Current.WarehouseDiscountDay = Today();
            Prices.Refresh();
            Mod.Log.Msg("offer: warehouse discount for today");
            return true;
        }

        public static bool PoliceLookAway()
        {
            if (!Spend(OfferKind.PoliceLookAway)) return false;
            MelangeLevelsData.Current.LenientUntil = Offers.LenientUntil(Today(), Now());
            _lenient = true;
            _nextCheck = 0f;
            try
            {
                var crime = Player.Local?.CrimeData;
                if (crime != null && Offers.DropsPursuit((int)crime.CurrentPursuitLevel))
                {
                    // what sleeping does to a pursuit (PlayerCrimeData.OnSleepStart); pursuing officers then give up
                    crime.SetPursuitLevel(PlayerCrimeData.EPursuitLevel.None);
                    Mod.Log.Msg("offer: the search for you is called off");
                }
            }
            catch (Exception e) { Mod.Log.Warning($"offer: couldn't call off the search: {e.Message}"); }
            Mod.Log.Msg($"offer: the police look away until {Offers.LenientUntilTime / 100:00}:{Offers.LenientUntilTime % 100:00}");
            return true;
        }

        private static bool _lenient;
        private static float _nextCheck;
        private const string CurfewState = "DisobeyingCurfew";

        /// <summary>
        /// Every frame (from <see cref="Mod.OnUpdate"/>), host only. No Harmony patches: while the police look away it keeps
        /// the local player's body-search cooldown at zero (every search path in the game skips a player searched under
        /// 60 s ago) and removes the hard-curfew visual state that officers notice (the game adds it once a night and
        /// won't add it again while it thinks it's applied).
        /// </summary>
        public static void Tick()
        {
            float t = UnityEngine.Time.unscaledTime;
            if (t >= _nextCheck)
            {
                _nextCheck = t + 1f;
                _lenient = MelangeLevelsData.Current != null && Host.IsHost && PoliceLenient();
            }
            if (!_lenient) return;
            try
            {
                var player = Player.Local;
                if (player == null) return;
                if ((Offers.PoliceCovers & Leniency.BodySearches) != 0 && player.CrimeData != null)
                    player.CrimeData.TimeSinceLastBodySearch = 0f;
                if ((Offers.PoliceCovers & Leniency.Curfew) != 0)
                {
                    var curfew = NetworkSingleton<CurfewManager>.Instance;
                    var seen = player.VisualState;
                    if (curfew != null && curfew.IsHardCurfewActive && seen != null && seen.GetState(CurfewState) != null)
                        seen.RemoveState(CurfewState, 0f);
                }
            }
            catch (Exception e) { _lenient = false; Mod.Log.Warning($"police look away: {e.Message}"); }
        }

        private static bool Spend(OfferKind kind)
        {
            var data = MelangeLevelsData.Current;
            if (data == null || !Host.IsHost) return false;
            if (!Offers.Available(kind, data.Prestige, data.LastUsed(kind), Today(), out _)) return false;
            data.Prestige -= Offers.Cost(kind);
            data.OffersLastUsed[kind.ToString()] = Today();
            Events.Publish(new PrestigeChanged(data.Prestige));
            return true;
        }

        private static HashSet<string> _oscarShops;

        /// <summary>
        /// Oscar's shops, by name, once per scene: his store at the warehouse ("Oscar's Store", code "shop") and his
        /// equipment counter ("Oscar's Equipment"). The discount covers both: it's his stock.
        /// </summary>
        public static bool IsOscarShop(string shopCode)
        {
            if (_oscarShops == null)
            {
                _oscarShops = new HashSet<string>();
                var shops = ShopInterface.AllShops;
                for (int i = 0; shops != null && i < shops.Count; i++)
                    if ((shops[i]?.ShopName ?? "").IndexOf("Oscar", StringComparison.OrdinalIgnoreCase) >= 0)
                        _oscarShops.Add(shops[i].ShopCode);
                if (_oscarShops.Count == 0) _oscarShops = null;    // shops not ready yet: look again next time
            }
            return _oscarShops != null && _oscarShops.Contains(shopCode);
        }

        internal static void ForgetScene() { _oscarShops = null; _lenient = false; _nextCheck = 0f; }
    }
}
