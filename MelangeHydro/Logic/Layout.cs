using System;
using System.Collections.Generic;

namespace Melange.Hydro
{
    /// <summary>
    /// How trays and towers are placed. Individual (the default, works with what the game is known to do): each hole is
    /// bought and placed on its own, and draws its own share of the frame. Grouped (unproven in game, behind a setting): placing a
    /// tray or tower places all its holes at once, as separate pots on the same tiles, seated in the frame's holes.
    /// </summary>
    public enum Placement { Individual = 0, Grouped = 1 }

    /// <summary>
    /// Where the holes sit on each model (scripts/art/MODELS.md; Unity axes, metres from the model's origin at the bottom
    /// centre), and how the holes of one frame are numbered so every player's game puts the same hole in the same place.
    /// </summary>
    public static class Layout
    {
        /// <summary>The Hydro Tray's five holes along its channel; a hole's origin is the underside of its flange.</summary>
        public static readonly (float X, float Y, float Z)[] TrayHoles =
        {
            (-0.56f, 0.60f, 0f), (-0.28f, 0.60f, 0f), (0f, 0.60f, 0f), (0.28f, 0.60f, 0f), (0.56f, 0.60f, 0f),
        };

        /// <summary>The Aeroponic Tower's twelve ports: four levels of three, each level turned 60 degrees.</summary>
        public static readonly (float X, float Y, float Z)[] TowerPorts =
        {
            (0f, 0.713f, 0.19f), (-0.165f, 0.713f, -0.095f), (0.165f, 0.713f, -0.095f),
            (-0.165f, 1.013f, 0.095f), (0f, 1.013f, -0.19f), (0.165f, 1.013f, 0.095f),
            (0f, 1.313f, 0.19f), (-0.165f, 1.313f, -0.095f), (0.165f, 1.313f, -0.095f),
            (-0.165f, 1.613f, 0.095f), (0f, 1.613f, -0.19f), (0.165f, 1.613f, 0.095f),
        };

        /// <summary>The pump's hose spigot tip (the hose's pump end).</summary>
        public static readonly (float X, float Y, float Z) PumpSpigot = (0.10f, 0.16f, 0.19f);

        /// <summary>Height of the hole's flange when a section stands alone (the tray's channel height, so a row of sections reads as a tray).</summary>
        public const float SectionHoleHeight = 0.60f;
        /// <summary>Height of the single port on a lone aeroponic section (the tower's second level).</summary>
        public const float AeroSectionPortHeight = 1.013f;

        public static (float X, float Y, float Z)[] HolesFor(HoleKind kind) => kind == HoleKind.Aero ? TowerPorts : TrayHoles;

        /// <summary>
        /// Numbers a frame's holes: the frame itself is hole 0, its other holes follow in ordinal order of their GUIDs. Every
        /// player's game sees the same GUIDs (the game sends them), so every game numbers them the same, with no extra data.
        /// </summary>
        public static List<string> Order(string frameGuid, IEnumerable<string> siblingGuids)
        {
            var rest = new List<string>();
            foreach (var g in siblingGuids) if (!string.IsNullOrEmpty(g) && g != frameGuid) rest.Add(g);
            rest.Sort(StringComparer.Ordinal);
            var all = new List<string> { frameGuid };
            all.AddRange(rest);
            return all;
        }

        /// <summary>The hole index of a GUID in a frame (0 for the frame), or -1.</summary>
        public static int IndexOf(string frameGuid, IEnumerable<string> siblingGuids, string guid) => Order(frameGuid, siblingGuids).IndexOf(guid);

        /// <summary>What the hardware stores sell in a placement mode: the sections always, the frames only when grouping is on.</summary>
        public static List<UnitSpec> ForSale(Placement mode)
        {
            var list = new List<UnitSpec>();
            foreach (var u in Units.All)
                if (!u.IsFrame || mode == Placement.Grouped) list.Add(u);
            return list;
        }

        /// <summary>How many more holes a newly placed frame needs (its own site is hole 0).</summary>
        public static int SiblingsToPlace(UnitSpec frame, int alreadyThere)
            => frame == null || !frame.IsFrame ? 0 : Math.Max(0, frame.Sites - 1 - alreadyThere);
    }
}
