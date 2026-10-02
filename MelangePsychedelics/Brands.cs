using System;
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
    }
}
