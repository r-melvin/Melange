using System.Collections.Generic;

namespace Melange.Hydro
{
    /// <summary>The two kinds of growing site. Each hole of a tray or tower is one pot of one kind.</summary>
    public enum HoleKind
    {
        None = 0,
        /// <summary>A hole in a Hydro Tray: a nutrient film channel, no soil, a slow reservoir.</summary>
        Hydro = 1,
        /// <summary>A port on an Aeroponic Tower: misted roots, the fastest growth, extra bud sites, cures in place.</summary>
        Aero = 2,
    }

    /// <summary>What each kind of hole does differently from the pot it is cloned from (the game's Grow Tent).</summary>
    public sealed class KindSpec
    {
        public HoleKind Kind { get; }
        /// <summary>
        /// Growth speed on top of the pot's own, applied through the temperature multiplier: Production Expansion Reborn
        /// overwrites the pot's speed field, but nothing else touches the temperature one.
        /// </summary>
        public float SpeedFactor { get; }
        /// <summary>In-game hours a full reservoir lasts (a vanilla pot lasts 6 to 11).</summary>
        public float ReservoirHours { get; }
        /// <summary>The plant's yield multiplier (the Grow Tent's own is 0.667; hydro drops that penalty).</summary>
        public float YieldMultiplier { get; }
        /// <summary>Bud sites added to the plant's final stage, so the yield can pass the game's cap (16 weed, 20 coca).</summary>
        public int ExtraBudSites { get; }
        /// <summary>Whether a fully grown plant cures in place (see <see cref="Curing"/>).</summary>
        public bool Cures { get; }
        /// <summary>The training a botanist needs before he may be assigned this kind of hole.</summary>
        public TrainingLevel Training { get; }

        internal KindSpec(HoleKind kind, float speed, float hours, float yield, int extraSites, bool cures, TrainingLevel training)
        {
            Kind = kind; SpeedFactor = speed; ReservoirHours = hours; YieldMultiplier = yield; ExtraBudSites = extraSites; Cures = cures; Training = training;
        }
    }

    /// <summary>One thing the spoke sells: a single hole (a "section"), a whole frame of holes, the pump or the nutrient.</summary>
    public sealed class UnitSpec
    {
        /// <summary>The item ID, prefixed so it can't collide with the game's or another mod's.</summary>
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        /// <summary>The kind of hole it is (None for the pump and the nutrient).</summary>
        public HoleKind Kind { get; }
        /// <summary>Growing sites it holds: 1 for a section, the frame's hole count for a tray or tower.</summary>
        public int Sites { get; }
        public float Price { get; }
        /// <summary>The rank (as the game numbers them: 8 Underlord, 9 Baron, 10 Kingpin) and tier that unlock it.</summary>
        public int Rank { get; }
        public int RankTier { get; }
        /// <summary>A frame places its other holes itself (the grouped placement, behind a setting until proven in game).</summary>
        public bool IsFrame => Sites > 1;

        internal UnitSpec(string id, string name, string description, HoleKind kind, int sites, float price, int rank, int rankTier)
        {
            Id = id; Name = name; Description = description; Kind = kind; Sites = sites; Price = price; Rank = rank; RankTier = rankTier;
        }
    }

    /// <summary>
    /// The spoke's items and numbers. Every hole is a clone of the game's Grow Tent: it keeps the tent's built-in light, so
    /// hydro needs no grow lights, and it is saved under the tent's own item ID, so without the mod a hole loads as an
    /// ordinary Grow Tent with its plant kept. The grow medium is the game's Extra Long-Life Soil, poured in for the player
    /// with many uses, for the same reason: it survives the mod being removed. Prices are a proposal for the late ranks,
    /// where a day's sales dwarf them.
    /// </summary>
    public static class Units
    {
        public const int Underlord = 8, Baron = 9, Kingpin = 10;
        /// <summary>The vanilla pot every hole is cloned from and saved as.</summary>
        public const string DonorId = "growtent";
        /// <summary>The vanilla soil used as the grow medium (rockwool and clay pebbles, in the item's description).</summary>
        public const string MediumSoilId = "extralonglifesoil";
        /// <summary>Harvests one fill of the medium lasts (Extra Long-Life Soil gives 3).</summary>
        public const int MediumUses = 20;
        /// <summary>The fertiliser the nutrient is cloned from (the game's +0.3 quality additive).</summary>
        public const string FertiliserId = "fertilizer";
        /// <summary>The vanilla item the pump is cloned from: a small floor item that already works with water.</summary>
        public const string PumpDonorId = "potsprinkler";

        public static readonly KindSpec HydroKind = new KindSpec(HoleKind.Hydro, 1.15f, 36f, 1.0f, 0, false, TrainingLevel.Hydroponics);
        public static readonly KindSpec AeroKind = new KindSpec(HoleKind.Aero, 1.35f, 24f, 1.5f, 8, true, TrainingLevel.Aeroponics);

        public static readonly UnitSpec HydroSection = new UnitSpec("melange_hydro_section", "Hydro Tray Section",
            "One growing site of a hydroponic tray: roots in a nutrient channel, a reservoir that lasts a day and a half, " +
            "pre-filled with a grow medium good for 20 harvests. Built-in light. Only botanists trained in hydroponics can tend it.",
            HoleKind.Hydro, 1, 450f, Underlord, 3);
        public static readonly UnitSpec AeroSection = new UnitSpec("melange_aero_section", "Aeroponic Tower Section",
            "One growing site of an aeroponic tower: misted roots, the fastest growth and extra bud sites. A ripe plant cures " +
            "in place for a day, then slips back. Only botanists trained in aeroponics can tend it.",
            HoleKind.Aero, 1, 900f, Baron, 3);
        public static readonly UnitSpec HydroTray = new UnitSpec("melange_hydro_tray", "Hydro Tray",
            "A hydroponic tray with five growing sites, placed as one. Shares one reservoir. Only botanists trained in hydroponics can tend it.",
            HoleKind.Hydro, 5, 2000f, Underlord, 3);
        public static readonly UnitSpec AeroTower = new UnitSpec("melange_aero_tower", "Aeroponic Tower",
            "An aeroponic tower with twelve growing sites, placed as one. Only botanists trained in aeroponics can tend it.",
            HoleKind.Aero, 12, 9500f, Baron, 3);
        public static readonly UnitSpec Pump = new UnitSpec("melange_hydro_pump", "Reservoir Pump",
            "Hose it to the property's tap and it keeps every hydro and aero reservoir nearby topped up. Shares the tap; " +
            "it never blocks it.",
            HoleKind.None, 0, 1200f, Underlord, 3);
        public static readonly UnitSpec Juicer = new UnitSpec("melange_grow_n_juicer", "Grow N Juicer",
            "A premium nutrient for hydro and aero sites. Top-shelf plants, laid back. (+0.15 quality; with fertiliser, Heavenly at harvest.)",
            HoleKind.None, 0, 75f, Kingpin, 3);

        /// <summary>Quality the nutrient adds; with fertiliser (+0.3) on the base 0.5 it passes the Heavenly line (0.9).</summary>
        public const float JuicerQuality = 0.15f;

        /// <summary>The single-site items (always sold) and the frames (sold only when grouped placement is on).</summary>
        public static readonly IReadOnlyList<UnitSpec> Holes = new[] { HydroSection, AeroSection, HydroTray, AeroTower };
        public static readonly IReadOnlyList<UnitSpec> All = new[] { HydroSection, AeroSection, HydroTray, AeroTower, Pump, Juicer };

        public static UnitSpec ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var u in All) if (u.Id == id) return u;
            return null;
        }

        /// <summary>The kind of hole an item ID is, or None (vanilla pots, the pump, anything else).</summary>
        public static HoleKind KindOf(string id) => ById(id)?.Kind ?? HoleKind.None;

        public static KindSpec Spec(HoleKind kind) => kind == HoleKind.Hydro ? HydroKind : kind == HoleKind.Aero ? AeroKind : null;

        /// <summary>The single-site item for a kind: what a frame's other holes are placed as.</summary>
        public static UnitSpec SectionFor(HoleKind kind) => kind == HoleKind.Hydro ? HydroSection : kind == HoleKind.Aero ? AeroSection : null;
    }
}
