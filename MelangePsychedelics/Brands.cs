using System;
using System.Collections.Generic;
using Melange.Core;
using S1API.Products;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Designs as brands. When a customer takes LSD, the tab is put down to a batch (first in, first out by quality, see
    /// BatchLedger): a good batch earns its design reputation and, once the design is known or loved, a little loyalty from
    /// the customer; a bad batch is a bad trip, costs the design reputation and the player some of the customer's goodwill.
    /// Runs from S1API's consumption hook for the LSD kind (mixes included), on the host only.
    /// </summary>
    internal static class Brands
    {
        public static void OnNpcTrip(ProductConsumptionContext ctx)
        {
            try
            {
                if (!Host.IsHost) return;
                var data = MelangePsychedelicsData.Current;
                if (data == null || ctx == null) return;
                int tier = (int)ctx.Quality;
                var batch = data.Batches.Attribute(tier);
                if (batch == null) return;                         // LSD from outside the ledger (a console give, an old save)
                var design = data.Designs.Find(batch.DesignId);
                if (design == null) return;
                Trip(design, batch.Id, !batch.Bad, tier, ctx.NPC);
            }
            catch (Exception e) { Mod.Log.Warning("trip: " + e.Message); }
        }

        /// <summary>A trip's outcome on its design (and on the customer, when there is one). Returns the log line.</summary>
        internal static string Trip(Design design, int batchId, bool good, int tier, S1API.Entities.NPC npc)
        {
            var standing = DesignBook.StandingOf(design.Reputation);
            DesignBook.RecordTrip(design, good, tier);
            float delta = DesignBook.RelationshipChange(standing, good);
            if (delta != 0f) npc?.Relationship?.Add(delta);
            string who = npc?.FullName ?? "a customer";
            if (!good) Items.Notify("Bad trip", $"{who} had a bad trip on your {design.Name} tabs.");
            var after = DesignBook.StandingOf(design.Reputation);
            if (after != standing && after == Standing.Loved) Items.Notify("A brand", $"Customers ask for the {design.Name} sheets by name.");
            if (after != standing && after == Standing.Burnt) Items.Notify("A burnt design", $"Word is out about {design.Name}. Time for a new design?");
            string line = $"trip: {who}, batch {batchId} ({(good ? "good" : "BAD")}, {Tier.Name(tier)}), design {design.Id} rep {design.Reputation:F0} ({after}), relationship {delta:+0.00;-0.00;0}";
            Mod.Log.Msg(line);
            return line;
        }

        // ------------------------------------------------------------------ probes (Probe.cs, host only)

        /// <summary>
        /// A sale's consumption with a real customer: one toad or LSD tab (from the pockets when there is one, else made) handed
        /// to the nearest conscious customer through <c>NPC.Behaviour.ConsumeProduct_Server(product)</c>, the call the game's
        /// own handover ends with (Customer.ProcessHandoverServerSide -> Customer.ConsumeProduct). The NPC takes it, the game
        /// applies its effects (ProductItemInstance.ApplyEffectsToNPC), and S1API's hook on that runs OnNpcTrip with the customer,
        /// so the design's reputation and the customer's relationship change for real. With <c>contract</c> and a customer who
        /// has a current contract, it instead goes through Customer.ProcessHandover(contract, [item], handoverByPlayer: false,
        /// giveBonuses: false): the contract completes and the satisfaction/relationship rules run, but as a non-player
        /// handover the game pays nobody (see TESTING.md).
        /// </summary>
        internal static string ProbeSell(MelangePsychedelicsData data, string kind, Design design, bool viaContract)
        {
            bool lsd = kind == "lsd";
            if (!lsd && kind != "toad") return "sell <toad|lsd> [design] [contract]";
            string productId = lsd ? Ids.LsdProduct : Ids.ToadProduct;
            int tier = -1;
            string expect = "";
            if (lsd)
            {
                if (design != null)
                {
                    tier = data.Batches.TierFor(design.Id);
                    if (tier < 0) return $"no batch of {design.Id} would be picked first at any quality (psy dose {design.Id}, or sell the older batches' tabs)";
                }
                if (data.Batches.Batches.Count == 0) expect = " (no batches in the ledger: OnNpcTrip will find none and change nothing)";
            }

            var customer = NearestCustomer(out float dist);
            if (customer == null) return "no conscious customer found";
            var npc = customer.NPC;

            var product = FromPockets(productId, tier, out bool fromPockets) ?? Products.Make(productId, 1, tier < 0 ? Tier.Premium : tier);
            if (product == null) return $"{productId} could not be made";
            int q = (int)product.Quality;
            if (lsd)
            {
                var b = data.Batches.Peek(q);
                var d = b == null ? null : data.Designs.Find(b.DesignId);
                expect = b == null ? $" (no {Tier.Name(q)} batch: OnNpcTrip will change nothing)"
                    : $"; the ledger will put it down to batch {b.Id} ({b.DesignId}, {(b.Bad ? "BAD" : "good")}, {b.Tabs} tabs left), design rep now {d?.Reputation:0}";
            }
            float relBefore = npc.RelationData.RelationDelta;
            string how;
            var contract = customer.CurrentContract;
            if (viaContract && contract != null)
            {
                var items = new Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemInstance>();
                items.Add(product);
                customer.ProcessHandover(contract, items, false, false);
                how = "Customer.ProcessHandover(current contract, handoverByPlayer false, no bonuses): contract completed, no payment";
            }
            else
            {
                npc.Behaviour.ConsumeProduct_Server(product, false);
                how = "NPC.Behaviour.ConsumeProduct_Server" + (viaContract ? " (asked for 'contract' but the customer has none)" : "");
            }
            MelonLoader.MelonCoroutines.Start(Afterwards(npc, relBefore, design ?? (lsd ? data.Designs.Find(data.Batches.Peek(q)?.DesignId) : null)));
            return $"{npc.FullName} ({dist:0} m, relationship {relBefore:0.00}) given 1 {productId} {Tier.Name(q)} " +
                   $"{(fromPockets ? "from the pockets" : "(made: none in the pockets)")} via {how}{expect}; " +
                   "watch for 'trip: <name>' (LSD) and 'PROBE sell after' in ~10 s";
        }

        private static System.Collections.IEnumerator Afterwards(Il2CppScheduleOne.NPCs.NPC npc, float relBefore, Design design)
        {
            float until = UnityEngine.Time.realtimeSinceStartup + 10f;
            while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
            try
            {
                Mod.Log.Msg($"PROBE sell after: {npc.FullName} relationship {relBefore:0.00} -> {npc.RelationData.RelationDelta:0.00}" +
                            (design == null ? "" : $"; design {design.Id} rep {design.Reputation:0} ({DesignBook.StandingOf(design.Reputation)}), {design.GoodTrips} good/{design.BadTrips} bad"));
            }
            catch (Exception e) { Mod.Log.Warning("sell after: " + e.Message); }
        }

        private static Il2CppScheduleOne.Economy.Customer NearestCustomer(out float dist)
        {
            dist = 0f;
            var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
            Il2CppScheduleOne.Economy.Customer best = null;
            float bestD = float.MaxValue;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Economy.Customer>())
            {
                var npc = c?.NPC;
                if (npc == null || !npc.IsConscious || npc.Behaviour == null) continue;
                float d = me == null ? 0f : UnityEngine.Vector3.Distance(npc.transform.position, me.transform.position);
                if (d < bestD) { best = c; bestD = d; }
            }
            dist = bestD;
            return best;
        }

        /// <summary>One item of the product off a pocket stack (at the tier, when one is given), or null.</summary>
        private static Il2CppScheduleOne.Product.ProductItemInstance FromPockets(string productId, int tier, out bool found)
        {
            found = false;
            var slots = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance?.hotbarSlots;
            for (int i = 0; slots != null && i < slots.Count; i++)
            {
                var slot = slots[i];
                var inst = slot?.ItemInstance;
                if (inst == null || slot.Quantity <= 0) continue;
                bool match = inst.ID == productId || (productId == Ids.LsdProduct && Products.LsdMixIds.Contains(inst.ID));
                if (!match || (tier >= 0 && Items.TierOf(inst) != tier)) continue;
                var one = inst.GetCopy(1)?.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
                if (one == null) continue;
                slot.ChangeQuantity(-1);
                found = true;
                return one;
            }
            return null;
        }
    }
}
