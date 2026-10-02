using System;
using System.Collections.Generic;

namespace Melange.Mixers
{
    /// <summary>Why a machine can't start, or that it can.</summary>
    public enum ChainState
    {
        Ready,
        NoProduct,
        /// <summary>The product isn't one of the game's own kinds (v1 mixes vanilla products only).</summary>
        ProductNotVanilla,
        MissingMixer,
        /// <summary>Something in a mixer slot isn't a mixing ingredient the game accepts.</summary>
        InvalidMixer,
        /// <summary>A mixer from another mod (v1 mixes vanilla ingredients only).</summary>
        MixerNotVanilla,
    }

    /// <summary>The verdict on a machine's slots, with the slot (1-based) at fault when there is one.</summary>
    public readonly struct ChainCheck
    {
        public ChainState State { get; }
        public int Slot { get; }
        public bool Ready => State == ChainState.Ready;
        public ChainCheck(ChainState state, int slot = 0) { State = state; Slot = slot; }
    }

    /// <summary>
    /// A machine's mix is the same single mixes one after another, in slot order (decided with the user): slot 1 is the game's
    /// own mixer slot, slots 2-4 are the machine's extra ones. So the result is a fold of the game's single-mix calculation and
    /// equals that many Mk2 passes exactly. Order matters in the game's rules, which is why the slots are numbered.
    /// </summary>
    public static class MixChain
    {
        /// <summary>Applies <paramref name="mixOnce"/> for each mixer in order, starting from the product's effects.</summary>
        public static TEffects Fold<TEffects, TMixer>(TEffects start, IEnumerable<TMixer> mixersInSlotOrder, Func<TEffects, TMixer, TEffects> mixOnce)
        {
            var current = start;
            foreach (var mixer in mixersInSlotOrder) current = mixOnce(current, mixer);
            return current;
        }

        /// <summary>
        /// Can this machine start? Every one of its <paramref name="required"/> mixer slots must hold a valid, vanilla mixer, and
        /// the product must be vanilla. Ids are null or empty for an empty slot.
        /// </summary>
        public static ChainCheck Check(string productId, IReadOnlyList<string> mixerIdsInSlotOrder, int required, Func<string, bool> isMixingIngredient)
        {
            if (string.IsNullOrEmpty(productId)) return new ChainCheck(ChainState.NoProduct);
            if (!SaveRules.IsVanillaId(productId)) return new ChainCheck(ChainState.ProductNotVanilla);
            for (int i = 0; i < required; i++)
            {
                string id = mixerIdsInSlotOrder != null && i < mixerIdsInSlotOrder.Count ? mixerIdsInSlotOrder[i] : null;
                if (string.IsNullOrEmpty(id)) return new ChainCheck(ChainState.MissingMixer, i + 1);
                if (isMixingIngredient != null && !isMixingIngredient(id)) return new ChainCheck(ChainState.InvalidMixer, i + 1);
                if (!SaveRules.IsVanillaId(id)) return new ChainCheck(ChainState.MixerNotVanilla, i + 1);
            }
            return new ChainCheck(ChainState.Ready);
        }

        /// <summary>
        /// The cap to put on the station's own batch size so the game's batch (min of product, slot-1 mixer and the cap) is also
        /// the min over the extra mixer slots: the extra slots' smallest stack, or 0 when one is empty, which also stops the game
        /// and its chemists from starting. Never above the station's own cap.
        /// </summary>
        public static int BatchCap(int stationMax, IReadOnlyList<int> extraQuantities, bool chainReady)
        {
            if (!chainReady) return 0;
            int cap = Math.Max(0, stationMax);
            if (extraQuantities != null)
                foreach (int q in extraQuantities) cap = Math.Min(cap, Math.Max(0, q));
            return cap;
        }

        /// <summary>
        /// What a start may actually take: the batch the game chose, cut to the smallest extra stack (they can change between the
        /// game choosing and the start, e.g. while a chemist works). 0 means the start must be refused.
        /// </summary>
        public static int StartQuantity(int requested, IReadOnlyList<int> extraQuantities)
        {
            int q = Math.Max(0, requested);
            if (extraQuantities != null)
                foreach (int e in extraQuantities) q = Math.Min(q, Math.Max(0, e));
            return q;
        }

        /// <summary>Text for the station's instruction line when it can't start.</summary>
        public static string Explain(ChainCheck check, int required) => check.State switch
        {
            ChainState.NoProduct => "Insert unpackaged product",
            ChainState.ProductNotVanilla => "This mixer takes the base game's products only",
            ChainState.MissingMixer => required == 1 ? "Insert a mixing ingredient" : $"Fill mixer slots 1 to {required} (mixed in that order)",
            ChainState.InvalidMixer => $"Slot {check.Slot} doesn't hold a mixing ingredient",
            ChainState.MixerNotVanilla => $"Slot {check.Slot}: this mixer takes the base game's ingredients only",
            _ => string.Empty,
        };
    }
}
