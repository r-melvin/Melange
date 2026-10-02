using System;
using System.Collections.Generic;
using System.Text;

namespace Melange.Mixers
{
    /// <summary>
    /// What a machine remembers between saves, beyond what the game saves for it. The game saves a mixing station's product,
    /// slot-1 mixer and output slots and its running operation; the extra mixer slots and the order they were mixed in are ours.
    /// </summary>
    public sealed class StationRecord
    {
        /// <summary>The extra mixer slots' contents and player filters, in the game's own ItemSet JSON (so the game's loader reads it back).</summary>
        public string Slots;
        /// <summary>The mixers of the running mix in slot order, captured when it started; empty when idle.</summary>
        public List<string> Chain = new List<string>();
    }

    /// <summary>
    /// The rules that keep saves safe with and without the mod (from the plan):
    /// the game's mix recipes stay strictly product + one mixer (its save keeps only two ingredients, so a longer recipe would
    /// reload as "product + first mixer -> final product" and corrupt every plain mix of those two);
    /// the game's MixOperation names only the first mixer (a joined list would leave the operation stuck forever without the mod);
    /// and v1 makes only vanilla products from vanilla ingredients, so what it creates loads without the mod.
    /// </summary>
    public static class SaveRules
    {
        /// <summary>May a mix with this many mixers be written to the game's recipe list? Only a plain single mix.</summary>
        public static bool MayRecordGameRecipe(int mixerCount) => mixerCount == 1;

        /// <summary>The mixer the game's own operation names: always and only the first, a real item the game can resolve.</summary>
        public static string OperationIngredientId(IReadOnlyList<string> chain)
        {
            if (chain == null || chain.Count == 0) throw new ArgumentException("a mix needs at least one mixer", nameof(chain));
            return chain[0];
        }

        /// <summary>
        /// Is this a base-game item ID? S1API namespaces other mods' content ("mod:products/x"), and base-game IDs are plain
        /// lower-case words, so any ':' or '/' marks modded content.
        /// </summary>
        public static bool IsVanillaId(string id) => !string.IsNullOrEmpty(id) && id.IndexOf(':') < 0 && id.IndexOf('/') < 0;

        /// <summary>A chain is usable for a machine if it has exactly that many mixers, none empty.</summary>
        public static bool ChainFits(IReadOnlyList<string> chain, int mixers)
        {
            if (chain == null || chain.Count != mixers) return false;
            foreach (var id in chain) if (string.IsNullOrEmpty(id)) return false;
            return true;
        }

        /// <summary>Records for machines that still exist; a picked-up machine's record would otherwise live in the save for ever.</summary>
        public static Dictionary<string, StationRecord> Prune(IDictionary<string, StationRecord> records, ICollection<string> liveStations)
        {
            var kept = new Dictionary<string, StationRecord>();
            if (records == null) return kept;
            foreach (var kv in records)
                if (kv.Value != null && liveStations != null && liveStations.Contains(kv.Key)) kept[kv.Key] = kv.Value;
            return kept;
        }

        /// <summary>The product ID the game makes from a name (ProductManager.FinishAndNameMix), so ours are the same shape.</summary>
        public static string MixId(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.ToLowerInvariant())
                if (c != ' ' && "()'\":;,.!?".IndexOf(c) < 0) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// <paramref name="id"/>, or it with a number after it if taken. The game itself skips creating a product whose ID exists,
        /// which would leave the mix unnamed; two names can map to one ID ("A.B" and "AB").
        /// </summary>
        public static string UniqueId(string id, Func<string, bool> taken)
        {
            if (taken == null || !taken(id)) return id;
            for (int n = 2; n < 10000; n++)
                if (!taken(id + n)) return id + n;
            return id + Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }

    /// <summary>Shop prices: a multiple of the Mk2's price, rounded to a shop-like number.</summary>
    public static class Pricing
    {
        /// <summary>Used only if the Mk2's own price can't be read (the wiki gives $2,000; unverified in game).</summary>
        public const float AssumedMk2Price = 2000f;

        public static float Price(float mk2Price, MixerTier tier)
        {
            if (mk2Price <= 0f || float.IsNaN(mk2Price)) mk2Price = AssumedMk2Price;
            float raw = mk2Price * tier.PriceMultiplier;
            return (float)(Math.Round(raw / 100.0, MidpointRounding.AwayFromZero) * 100.0);
        }
    }
}
