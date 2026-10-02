using MelonLoader;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The player's knobs (MelonPreferences, "MelangePsychedelics" in UserData/MelonPreferences.cfg). Read live and sanitised by
    /// the pure rules, so a typo can't break a save. In co-op the host's values decide: only the host runs the terrariums and
    /// the dosing.
    /// </summary>
    internal static class Settings
    {
        private static MelonPreferences_Entry<int> _dust, _capacity, _breedDays, _starveDays, _nightOpen, _nightHours, _spores;
        private static MelonPreferences_Entry<float> _markup, _badBatch;
        private static MelonPreferences_Entry<bool> _ergot, _pond, _sewer, _stall, _paint;
        private static MelonPreferences_Entry<string> _pondAt;

        public static void Create()
        {
            var c = MelonPreferences.CreateCategory("MelangePsychedelics", "Melange Psychedelics");
            _dust = c.CreateEntry("MilkingsBeforeDust", TerrariumRules.DefaultMilkingsBeforeDust, "Milkings before a toad turns to dust",
                "How many times a toad can be milked before it shrivels to dust (1-100).");
            _capacity = c.CreateEntry("TerrariumCapacity", TerrariumRules.DefaultCapacity, "Toads per terrarium", "2-24.");
            _breedDays = c.CreateEntry("BreedDays", TerrariumRules.DefaultBreedDays, "Days for a fed pair to breed",
                "Days two or more fed toads take to produce a new one (1-30). The Sewer King's teaching takes a day off.");
            _starveDays = c.CreateEntry("StarveDays", TerrariumRules.DefaultStarveDays, "Days a toad survives unfed", "1-30.");
            _markup = c.CreateEntry("BlackMarketMarkup", RandysStall.DefaultMarkup, "Black-market toad markup",
                "Randy's night price as a multiple of a toad's value; each toad sold that night adds 15%.");
            _nightOpen = c.CreateEntry("BlackMarketOpens", 2200, "Black market opens at", "24-hour clock, as the game writes it (2200 = 10 pm).");
            _nightHours = c.CreateEntry("BlackMarketHours", RandysStall.DefaultNightHours, "Black market hours", "1-8.");
            _badBatch = c.CreateEntry("BadBatchOdds", 1f, "Bad-batch odds multiplier", "1 = the standard odds; 0 = never; 2 = twice as likely.");
            _spores = c.CreateEntry("ErgotSporesRank", 6, "Ergot spores unlock rank",
                "The rank (0 Street Rat .. 10 Kingpin) at which Fungal Phil sells ergot spores, at tier I. 6 = Shot Caller.");
            _ergot = c.CreateEntry("ErgotGrowing", true, "Grow ergot in mushroom beds",
                "Off: Fungal Phil sells ergot ready to use instead of spores (the fallback if growing misbehaves in your game).");
            _pond = c.CreateEntry("WildToads", true, "Wild toads at the pond", "Off: no toads, no wildlife officer.");
            _sewer = c.CreateEntry("SewerToads", true, "Toads in the sewer", "Off: none in the sewer.");
            _stall = c.CreateEntry("RandysStall", true, "Randy's stall", "Off: no stall behind Randy's Bait & Tackle.");
            _paint = c.CreateEntry("PaintDesigns", false, "Paint your own blotter designs (experimental)",
                "On: with a spray can in hand, spray a design onto a blotter frame's sheet with the game's graffiti screen; it becomes a " +
                "new design (a brand) for that frame. Untested in game: see TESTING.md. Host only.");
            _pondAt = c.CreateEntry("PondPosition", "", "Pond position override",
                "Leave empty to find the pond by itself. Otherwise \"x,y,z\" of the pond's centre (for a map change).");
        }

        public static TerrariumRules Rules(bool mentor) => TerrariumRules.Sanitised(
            _capacity?.Value ?? TerrariumRules.DefaultCapacity,
            _dust?.Value ?? TerrariumRules.DefaultMilkingsBeforeDust,
            TerrariumRules.DefaultFeedPerToad,
            _breedDays?.Value ?? TerrariumRules.DefaultBreedDays,
            _starveDays?.Value ?? TerrariumRules.DefaultStarveDays,
            mentor);

        public static float Markup => System.Math.Max(1f, _markup?.Value ?? RandysStall.DefaultMarkup);
        public static Window Night => RandysStall.Night(Clock.ToMinutes(_nightOpen?.Value ?? 2200), _nightHours?.Value ?? RandysStall.DefaultNightHours);
        public static double BadBatchOdds => System.Math.Max(0.0, _badBatch?.Value ?? 1f);
        public static int SporesRank => System.Math.Max(0, System.Math.Min(10, _spores?.Value ?? 6));
        public static bool ErgotGrowing => _ergot?.Value ?? true;
        public static bool WildToads => _pond?.Value ?? true;
        public static bool SewerToads => _sewer?.Value ?? true;
        public static bool Stall => _stall?.Value ?? true;
        public static bool PaintDesigns => _paint?.Value ?? false;

        public static bool PondOverride(out UnityEngine.Vector3 at)
        {
            at = default;
            var parts = (_pondAt?.Value ?? "").Split(',');
            if (parts.Length != 3) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out float x)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float y)) return false;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out float z)) return false;
            at = new UnityEngine.Vector3(x, y, z);
            return true;
        }
    }
}
