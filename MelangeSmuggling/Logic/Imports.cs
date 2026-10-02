using System;
using System.Collections.Generic;
using System.Globalization;

namespace Melange.Smuggling
{
    /// <summary>A crate the boat can bring back: an item ID, how many come in a crate, and the price per item before discount.</summary>
    public sealed class ImportOffer
    {
        public string ItemId;
        public int Crate;
        public float UnitPrice;
        public ImportOffer(string itemId, int crate, float unitPrice) { ItemId = itemId; Crate = crate; UnitPrice = unitPrice; }
    }

    /// <summary>
    /// Imports on the return leg. The catalogue is a setting ("id:crate:price, ..."), so other spokes' items can be added
    /// without code; methylamine is its own setting (an item ID the cartel spoke may provide, empty by default, in which
    /// case it simply isn't offered). Better reputation, cheaper crates.
    /// </summary>
    public static class Imports
    {
        public const string DefaultCatalogue = "acid:20:35,phosphorus:20:35,highqualitypseudo:20:70";

        /// <summary>Reads "id:crate:price" entries separated by commas or semicolons; a malformed entry is skipped, not fatal.</summary>
        public static List<ImportOffer> Parse(string spec)
        {
            var list = new List<ImportOffer>();
            if (string.IsNullOrWhiteSpace(spec)) return list;
            foreach (var raw in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Trim().Split(':');
                if (parts.Length != 3) continue;
                string id = parts[0].Trim();
                if (id.Length == 0) continue;
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int crate) || crate <= 0) continue;
                if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float price) || price < 0f) continue;
                if (list.Exists(o => string.Equals(o.ItemId, id, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new ImportOffer(id, crate, price));
            }
            return list;
        }

        /// <summary>The catalogue plus methylamine when an item ID is configured (it replaces a catalogue entry with the same ID).</summary>
        public static List<ImportOffer> Catalogue(string spec, string methylamineId, int methylamineCrate, float methylaminePrice)
        {
            var list = Parse(spec);
            if (!string.IsNullOrWhiteSpace(methylamineId) && methylamineCrate > 0 && methylaminePrice >= 0f)
            {
                string id = methylamineId.Trim();
                list.RemoveAll(o => string.Equals(o.ItemId, id, StringComparison.OrdinalIgnoreCase));
                list.Add(new ImportOffer(id, methylamineCrate, methylaminePrice));
            }
            return list;
        }

        public static float CratePrice(ImportOffer offer, int reputation, SmugglingRules r)
        {
            if (offer == null) return 0f;
            reputation = Math.Max(0, Math.Min(100, reputation));
            float discount = Math.Max(0f, Math.Min(0.9f, r.MaxImportDiscount)) * reputation / 100f;
            return (float)Math.Round(offer.Crate * offer.UnitPrice * (1f - discount), 0);
        }

        /// <summary>Crates can be bought once the player has been paid for a run, while the boat is in.</summary>
        public static bool Open(SmugglingState s, SmugglingRules r)
            => s.Unlocked && s.RunsPaid >= r.ImportsAfterRuns && s.Boat == BoatPhase.Moored;

        /// <summary>Adds a crate to what's ordered for the next return leg (merging with the same item).</summary>
        public static void AddOrdered(SmugglingState s, ImportOffer offer, string name, float paid)
        {
            foreach (var l in s.ImportsOrdered)
                if (string.Equals(l.ItemId, offer.ItemId, StringComparison.OrdinalIgnoreCase)) { l.Quantity += offer.Crate; l.Paid += paid; return; }
            s.ImportsOrdered.Add(new ImportLine { ItemId = offer.ItemId, Name = name, Quantity = offer.Crate, Paid = paid });
        }

        /// <summary>On the return: what was ordered is landed and waits at the boat.</summary>
        public static int Land(SmugglingState s)
        {
            int n = 0;
            foreach (var l in s.ImportsOrdered)
            {
                n += l.Quantity;
                var waiting = s.ImportsWaiting.Find(w => string.Equals(w.ItemId, l.ItemId, StringComparison.OrdinalIgnoreCase));
                if (waiting != null) { waiting.Quantity += l.Quantity; waiting.Paid += l.Paid; }
                else s.ImportsWaiting.Add(l);
            }
            s.ImportsOrdered = new List<ImportLine>();
            return n;
        }

        /// <summary>The player collected <paramref name="taken"/> of a waiting line (as many as fitted); the rest waits.</summary>
        public static void Collected(SmugglingState s, string itemId, int taken)
        {
            var l = s.ImportsWaiting.Find(w => string.Equals(w.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
            if (l == null || taken <= 0) return;
            l.Quantity -= taken;
            if (l.Quantity <= 0) s.ImportsWaiting.Remove(l);
        }
    }
}
