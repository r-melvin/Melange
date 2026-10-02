using System;
using MelonLoader;

namespace Melange.Hydro
{
    /// <summary>
    /// The spoke's settings (MelonPreferences, category MelangeHydro). In co-op every player should use the same values: plants
    /// grow on every player's game, so a different speed on one game shows a different plant until the next time skip.
    /// </summary>
    internal static class Settings
    {
        private static MelonPreferences_Entry<string> _placement;
        private static MelonPreferences_Entry<float> _hydroSpeed, _aeroSpeed, _hydroHours, _aeroHours;
        private static MelonPreferences_Entry<bool> _curing, _extraSites, _pumps, _verbose;

        public static void Create()
        {
            var c = MelonPreferences.CreateCategory("MelangeHydro", "Melange Hydro");
            _placement = c.CreateEntry("Placement", nameof(Hydro.Placement.Individual), "Placement of trays and towers",
                "Individual (default): each growing site is bought and placed on its own. Grouped (experimental, unproven in game): " +
                "a Hydro Tray or Aeroponic Tower places all its sites at once. Change it with no game loaded.");
            _hydroSpeed = c.CreateEntry("HydroSpeed", Units.HydroKind.SpeedFactor, "Hydro growth speed", "Multiplies the Grow Tent's own speed.");
            _aeroSpeed = c.CreateEntry("AeroSpeed", Units.AeroKind.SpeedFactor, "Aero growth speed", "Multiplies the Grow Tent's own speed.");
            _hydroHours = c.CreateEntry("HydroReservoirHours", Units.HydroKind.ReservoirHours, "Hydro reservoir (hours)", "In-game hours a full reservoir lasts.");
            _aeroHours = c.CreateEntry("AeroReservoirHours", Units.AeroKind.ReservoirHours, "Aero reservoir (hours)", "In-game hours a full reservoir lasts.");
            _curing = c.CreateEntry("AeroCuring", true, "Aero curing", "A ripe plant on an aeroponic tower cures in place: up a tier, holds, then back.");
            _extraSites = c.CreateEntry("AeroExtraBudSites", true, "Aero extra bud sites", "Adds bud sites to aero plants so the yield can pass the game's cap.");
            _pumps = c.CreateEntry("Pumps", true, "Pumps", "Pumps top up hydro and aero reservoirs from the property's tap.");
            _verbose = c.CreateEntry("Verbose", false, "Verbose log", "Logs each reservoir top-up, curing step and bud-site change (for testing).");
        }

        public static Placement Placement
            => Enum.TryParse<Placement>(_placement?.Value ?? "", true, out var p) ? p : Hydro.Placement.Individual;

        public static float Speed(HoleKind kind)
        {
            var spec = Units.Spec(kind);
            if (spec == null) return 1f;
            float v = (kind == HoleKind.Aero ? _aeroSpeed : _hydroSpeed)?.Value ?? spec.SpeedFactor;
            return v > 0.1f && v < 10f ? v : spec.SpeedFactor;
        }

        public static float ReservoirHours(HoleKind kind)
        {
            var spec = Units.Spec(kind);
            if (spec == null) return 8f;
            float v = (kind == HoleKind.Aero ? _aeroHours : _hydroHours)?.Value ?? spec.ReservoirHours;
            return v >= 1f && v <= 1000f ? v : spec.ReservoirHours;
        }

        public static bool Curing => _curing?.Value ?? true;
        public static bool ExtraSites => _extraSites?.Value ?? true;
        public static bool Pumps => _pumps?.Value ?? true;
        public static bool Verbose => _verbose?.Value ?? false;
    }
}
