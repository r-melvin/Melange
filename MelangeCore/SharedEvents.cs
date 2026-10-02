namespace Melange.Core
{
    // Events one spoke publishes and others may act on. They live in the hub because spokes never reference each other;
    // a spoke that doesn't care simply doesn't subscribe.

    /// <summary>The levels spoke made another underboss candidate available to hire. The cartel spoke owns underbosses and grows its pool on this.</summary>
    public sealed class UnderbossCandidateUnlocked
    {
        public int Total { get; }
        public UnderbossCandidateUnlocked(int total) => Total = total;
    }

    /// <summary>The bulk-order discount step changed (0 none, then 1-3), from the levels spoke.</summary>
    public sealed class BulkDiscountStepChanged
    {
        public int Step { get; }
        public BulkDiscountStepChanged(int step) => Step = step;
    }

    /// <summary>The player's Prestige changed (earned at Kingpin tiers, spent on offers), from the levels spoke.</summary>
    public sealed class PrestigeChanged
    {
        public int Prestige { get; }
        public PrestigeChanged(int prestige) => Prestige = prestige;
    }

    // From the sewer story spoke. Lasting facts (spared, the deal, defeated, the route) are published when they happen and
    // again after every load, so handlers must be idempotent; GoblinCalmed and SewerKingRevealed are moments, published once.

    /// <summary>Methylamine became available from Shirley (published by the cartel spoke). The sewer quest also starts on this.</summary>
    public sealed class MethylamineUnlocked { }

    /// <summary>The player kept the Sewer King's secret; he teaches toad farming (the psychedelics spoke gives its mentor bonuses on this).</summary>
    public sealed class SewerKingSpared { }

    /// <summary>The Sewer King turned hostile: his name said out loud, or he was attacked (<see cref="Attacked"/>).</summary>
    public sealed class SewerKingRevealed
    {
        public bool Attacked { get; }
        public SewerKingRevealed(bool attacked) => Attacked = attacked;
    }

    /// <summary>The Sewer King is dead. Toad farming is still reachable, the hard way (his journal, the goblin).</summary>
    public sealed class SewerKingDefeated { }

    /// <summary>The goblin took meth from the player and left calmly (once per save).</summary>
    public sealed class GoblinCalmed { }

    /// <summary>The spared King's deal: he runs the Underground as underboss on the hub's manager role, or paid the player off.</summary>
    public sealed class SewerKingDealChosen
    {
        public bool Underboss { get; }
        public SewerKingDealChosen(bool underboss) => Underboss = underboss;
    }

    /// <summary>
    /// The old bootleggers' route from the sewer to the Docks pier is known: shown by the spared King, or read in the dead
    /// one's journal (<see cref="FromJournal"/>). The smuggling spoke can use it as a way to the boat that avoids the streets.
    /// </summary>
    public sealed class BootleggersRouteRevealed
    {
        public bool FromJournal { get; }
        public BootleggersRouteRevealed(bool fromJournal) => FromJournal = fromJournal;
    }
}
