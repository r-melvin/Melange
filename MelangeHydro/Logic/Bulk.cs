using System;
using System.Collections.Generic;

namespace Melange.Hydro
{
    /// <summary>Where one hole stands, for grouping: its grid, its kind and the grid tiles it covers.</summary>
    public sealed class SiteSpot
    {
        /// <summary>The grid it is built on (any key that is equal for the same grid).</summary>
        public string Grid { get; }
        public HoleKind Kind { get; }
        public IReadOnlyList<(int X, int Y)> Tiles { get; }

        public SiteSpot(string grid, HoleKind kind, IReadOnlyList<(int X, int Y)> tiles)
        {
            Grid = grid ?? ""; Kind = kind; Tiles = tiles ?? Array.Empty<(int, int)>();
        }

        /// <summary>
        /// Same grid and kind, and a tile of one shares or borders (not diagonally) a tile of the other. The holes of a grouped
        /// frame share tiles; sections placed side by side border each other: both read as one tray to the player.
        /// </summary>
        public bool Touches(SiteSpot other)
        {
            if (other == null || other.Kind != Kind || other.Grid != Grid) return false;
            foreach (var a in Tiles)
                foreach (var b in other.Tiles)
                    if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= 1) return true;
            return false;
        }
    }

    /// <summary>What a click on a hole does to the rest of its tray, beyond the clicked hole itself (which the game toggles).</summary>
    public sealed class BulkChange
    {
        /// <summary>True: the others are added; false: they are removed.</summary>
        public bool Add { get; }
        /// <summary>Indices of the clicked hole's tray-mates to add or remove, in order; empty when nothing else changes.</summary>
        public IReadOnlyList<int> Others { get; }

        public BulkChange(bool add, IReadOnlyList<int> others) { Add = add; Others = others; }

        public static readonly BulkChange None = new BulkChange(false, Array.Empty<int>());
    }

    /// <summary>
    /// Clipboard bulk assignment (user's direction): every hole is a pot, so assigning a botanist pot by pot through the
    /// game's object selector is a click per hole. Clicking a hole selects or deselects its whole tray (a run of touching
    /// holes of one kind), and one key adds every tray in the property, both within the botanist's pot limit.
    /// </summary>
    /// <remarks>
    /// The game still handles the clicked hole itself (its own toggle, outline and "close when full"); these decide only what
    /// happens to the clicked hole's tray-mates, so one slot is always left for the clicked hole when adding.
    /// </remarks>
    public static class Bulk
    {
        /// <summary>
        /// The run (tray) containing <paramref name="start"/>: every spot reachable through touching spots, nearest first
        /// (breadth-first from the start, ties in list order). The start comes first.
        /// </summary>
        public static List<int> Run(IReadOnlyList<SiteSpot> spots, int start)
        {
            var order = new List<int>();
            if (spots == null || start < 0 || start >= spots.Count || spots[start] == null) return order;
            var seen = new bool[spots.Count];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                order.Add(i);
                for (int j = 0; j < spots.Count; j++)
                {
                    if (seen[j] || spots[j] == null || !spots[i].Touches(spots[j])) continue;
                    seen[j] = true;
                    queue.Enqueue(j);
                }
            }
            return order;
        }

        /// <summary>Every run (tray) among the spots, each in breadth-first order from its first spot in list order.</summary>
        public static List<List<int>> Runs(IReadOnlyList<SiteSpot> spots)
        {
            var runs = new List<List<int>>();
            if (spots == null) return runs;
            var placed = new bool[spots.Count];
            for (int i = 0; i < spots.Count; i++)
            {
                if (placed[i] || spots[i] == null) continue;
                var run = Run(spots, i);
                foreach (int j in run) placed[j] = true;
                runs.Add(run);
            }
            return runs;
        }

        /// <summary>
        /// A click on a hole of a multi-select (the botanist's pot list): what happens to the rest of its tray.
        /// <list type="bullet">
        /// <item>Clicked hole already selected: the game deselects it; its selected tray-mates go too.</item>
        /// <item>Not selected, and the botanist may take it: the game selects it; its eligible, unselected tray-mates are
        /// added nearest first while room is left, one slot kept for the clicked hole. A full list adds nothing.</item>
        /// <item>Not selected and not eligible (his training doesn't cover it): nothing more than the game's own click.</item>
        /// </list>
        /// </summary>
        /// <param name="run">The tray, clicked hole first (as <see cref="Run"/> gives it).</param>
        /// <param name="selected">Whether a hole is in the selection now.</param>
        /// <param name="eligible">Whether the selector would accept the hole and the botanist may tend it.</param>
        /// <param name="count">Holes and pots selected now.</param>
        /// <param name="max">The selection's limit (the botanist's pot limit); single-pick selectors (max 1) are left alone.</param>
        public static BulkChange Click(IReadOnlyList<int> run, Func<int, bool> selected, Func<int, bool> eligible, int count, int max)
        {
            if (run == null || run.Count < 2 || max < 2 || selected == null || eligible == null) return BulkChange.None;
            int clicked = run[0];
            var others = new List<int>();
            if (selected(clicked))
            {
                for (int i = 1; i < run.Count; i++) if (selected(run[i])) others.Add(run[i]);
                return others.Count == 0 ? BulkChange.None : new BulkChange(false, others);
            }
            if (!eligible(clicked)) return BulkChange.None;
            int room = max - count - 1;      // the game adds the clicked hole itself after us
            for (int i = 1; i < run.Count && others.Count < room; i++)
                if (!selected(run[i]) && eligible(run[i])) others.Add(run[i]);
            return others.Count == 0 ? BulkChange.None : new BulkChange(true, others);
        }

        /// <summary>
        /// "Assign every tray": which holes to add, given the property's trays (each already reduced to its eligible,
        /// unselected holes, trays nearest the player first) and the room left. Whole trays first, so none is split while a
        /// whole one still fits; then what room is left goes to the skipped trays in the same order.
        /// </summary>
        public static List<int> FillAll(IReadOnlyList<IReadOnlyList<int>> trays, int room)
        {
            var add = new List<int>();
            if (trays == null || room <= 0) return add;
            var skipped = new List<IReadOnlyList<int>>();
            foreach (var tray in trays)
            {
                if (tray == null || tray.Count == 0) continue;
                if (tray.Count <= room - add.Count) add.AddRange(tray);
                else skipped.Add(tray);
            }
            foreach (var tray in skipped)
                for (int i = 0; i < tray.Count && add.Count < room; i++) add.Add(tray[i]);
            return add;
        }

        /// <summary>
        /// The selector's title with the bulk tools' hint, shown while the botanist's pot list is being picked. The keys are
        /// the game's own (rebindable) Crouch and Reload buttons, named as the game names them.
        /// </summary>
        public static string Hint(string title) => $"{title} [hole: whole tray, Crouch+click: one hole, Reload: every tray]";
    }
}
