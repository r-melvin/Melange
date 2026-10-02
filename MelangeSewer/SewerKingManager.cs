using Melange.Core;

namespace Melange.Sewer
{
    /// <summary>
    /// The spared King as the Underground's underboss, on the hub's shared manager role (the same one the cartel spoke's
    /// regional underbosses use). For now he is only recorded: the sewer's production (shroom beds, the psychedelics
    /// spoke's LSD and toads) is run by the spokes that own it, which will find him here by <see cref="ManagerId"/>.
    /// </summary>
    internal sealed class SewerKingManager : Manager
    {
        public const string ManagerId = "sewer.king";

        public override string Id => ManagerId;
        public override string Name => "The Sewer King";
        public override string Site => "sewer";
        /// <summary>He has lived on nothing for forty years; a quarter is what he asks, not what he needs.</summary>
        public override float Cut => 0.25f;
        /// <summary>Loyal while his secret is kept. Threatening it again is a later feature; attacking him ends the deal outright.</summary>
        public override float Loyalty => 1f;

        public override void RunDailyChores(int day) { }
    }
}
