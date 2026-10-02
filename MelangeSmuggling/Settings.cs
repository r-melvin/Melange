using System.Collections.Generic;
using MelonLoader;

namespace Melange.Smuggling
{
    /// <summary>
    /// The spoke's MelonPreferences (UserData/MelonPreferences.cfg, section MelangeSmuggling). Read when a save loads, so a
    /// change applies from the next load. In co-op only the host's values matter: the host decides everything.
    /// </summary>
    internal static class Settings
    {
        private static MelonPreferences_Entry<int> _unlockMin, _unlockMax, _leadDays, _departure, _post, _return, _interval;
        private static MelonPreferences_Entry<float> _spend, _premium, _routeRisk, _baseRisk, _boatY;
        private static MelonPreferences_Entry<string> _catalogue, _methId;
        private static MelonPreferences_Entry<int> _methCrate, _berth, _tankerUnits;
        private static MelonPreferences_Entry<float> _methPrice;
        private static MelonPreferences_Entry<bool> _tanker;
        private static MelonPreferences_Entry<string> _tankerVehicle;
        private static MelonPreferences_Entry<bool> _pirate;
        private static MelonPreferences_Entry<float> _hatScale;
        private static MelonPreferences_Entry<string> _hatOffset, _patchOffset;

        public static void Create()
        {
            var d = new SmugglingRules();
            var c = MelonPreferences.CreateCategory("MelangeSmuggling", "Melange Smuggling");
            _unlockMin = c.CreateEntry("UnlockMinPurchases", d.UnlockMin, "Oscar: fewest qualifying purchases", "Dafydd's number comes after a number of purchases from Oscar rolled per save between this and the next setting.");
            _unlockMax = c.CreateEntry("UnlockMaxPurchases", d.UnlockMax, "Oscar: most qualifying purchases");
            _spend = c.CreateEntry("QualifyingSpend", d.QualifyingSpend, "Oscar: smallest purchase that counts ($)");
            _post = c.CreateEntry("OrderTime", d.PostTime, "When Dafydd texts an order (24-hour, 800 = 08:00)");
            _departure = c.CreateEntry("DepartureTime", d.DepartureTime, "When the boat sails (24-hour, 200 = 02:00)", "Before noon means the night after the deadline day.");
            _leadDays = c.CreateEntry("LeadDays", d.LeadDays, "Days of grace before the deadline night", "0 = the same night the order is posted.");
            _return = c.CreateEntry("ReturnTime", d.ReturnTime, "When the boat is back in port (24-hour)");
            _interval = c.CreateEntry("OrderIntervalDays", d.OrderIntervalDays, "Days from a sailing to the next order");
            _premium = c.CreateEntry("ExportPremium", d.ExportPremium, "Price per unit as a multiple of the game's market value");
            _baseRisk = c.CreateEntry("BasePoliceRisk", d.BaseRisk, "Police risk per delivery to the boat, before size and curfew");
            _routeRisk = c.CreateEntry("RouteRiskMultiplier", d.RouteRiskMultiplier, "Police risk via the bootleggers' route, as a multiple", "0 = no police risk at all via the route.");
            _catalogue = c.CreateEntry("ImportCatalogue", Imports.DefaultCatalogue, "Imports: id:crate:price, comma-separated", "Item IDs the boat can bring back, how many in a crate, and the price per item before the reputation discount.");
            _methId = c.CreateEntry("MethylamineItemId", "", "Imports: methylamine item ID", "Empty = not offered. Set it to the item ID another mod provides (the cartel spoke's methylamine).");
            _methCrate = c.CreateEntry("MethylamineCrate", 20, "Imports: methylamine per crate");
            _methPrice = c.CreateEntry("MethylaminePrice", 60f, "Imports: methylamine price per item");
            _berth = c.CreateEntry("Berth", 2, "Which gap between the quay's bollards the boat moors in (0-3)");
            _boatY = c.CreateEntry("BoatWaterline", -999f, "Boat waterline height (world y)", "-999 = find the water automatically.");
            _tanker = c.CreateEntry("TankerJob", false, "Experimental: the methylamine tanker job", "Off by default and unproven in game. Needs MethylamineItemId.");
            _tankerVehicle = c.CreateEntry("TankerVehicle", "veeper", "Experimental: the tanker's vehicle code");
            _tankerUnits = c.CreateEntry("TankerMethylamine", 40, "Experimental: methylamine in a stopped tanker");
            _pirate = c.CreateEntry("PirateLook", true, "Dafydd wears a tricorn and an eyepatch", "Off: the black cowboy hat, no eyepatch.");
            _hatScale = c.CreateEntry("PirateHatScale", 1f, "Tricorn size", "A multiple of the size fitted to his head (1 = as fitted).");
            _hatOffset = c.CreateEntry("PirateHatOffset", "", "Tricorn nudge", "\"x,y,z\" metres (right, up, forward from his view) added to where the hat sits. Empty = none.");
            _patchOffset = c.CreateEntry("PirateEyepatchOffset", "", "Eyepatch nudge", "\"x,y,z\" metres (right, up, forward). Empty = none.");
        }

        public static SmugglingRules Rules()
        {
            var r = new SmugglingRules();
            if (_unlockMin == null) return r;
            r.UnlockMin = _unlockMin.Value;
            r.UnlockMax = _unlockMax.Value;
            r.QualifyingSpend = _spend.Value;
            r.PostTime = _post.Value;
            r.DepartureTime = _departure.Value;
            r.LeadDays = _leadDays.Value;
            r.ReturnTime = _return.Value;
            r.OrderIntervalDays = _interval.Value;
            r.ExportPremium = _premium.Value > 0f ? _premium.Value : r.ExportPremium;
            r.BaseRisk = _baseRisk.Value >= 0f ? _baseRisk.Value : r.BaseRisk;
            r.RouteRiskMultiplier = _routeRisk.Value >= 0f ? _routeRisk.Value : 0f;
            return r;
        }

        public static List<ImportOffer> Catalogue()
            => _catalogue == null ? Imports.Parse(Imports.DefaultCatalogue)
                : Imports.Catalogue(_catalogue.Value, _methId.Value, _methCrate.Value, _methPrice.Value);

        public static string MethylamineId => _methId?.Value?.Trim() ?? "";
        public static int Berth => _berth?.Value ?? 2;
        /// <summary>The configured waterline, or null to find the water.</summary>
        public static float? Waterline => _boatY == null || _boatY.Value <= -900f ? (float?)null : _boatY.Value;
        public static bool Tanker => _tanker?.Value ?? false;
        public static string TankerVehicle => string.IsNullOrWhiteSpace(_tankerVehicle?.Value) ? "veeper" : _tankerVehicle.Value.Trim();
        public static int TankerMethylamine => _tankerUnits?.Value ?? 40;
        public static bool PirateLook => _pirate?.Value ?? true;
        public static double PirateHatScale => PirateFit.UserScale(_hatScale?.Value ?? 1f);
        public static (double X, double Y, double Z) PirateHatOffset => PirateFit.ParseOffset(_hatOffset?.Value);
        public static (double X, double Y, double Z) PirateEyepatchOffset => PirateFit.ParseOffset(_patchOffset?.Value);
    }
}
