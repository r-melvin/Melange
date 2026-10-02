using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Levels.Mod), "Melange Levels", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Levels
{
    /// <summary>
    /// Rewards for the late ranks, where the game unlocks nothing: more employees at a property, bulk-order discounts,
    /// underbosses to hire (through the cartel spoke), and from Kingpin on, Prestige to make offers they can't refuse.
    /// </summary>
    public sealed class Mod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static bool Active;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            if (!Core.Core.Require(new System.Version(0, 2, 0), out string problem))
            {
                Log.Error($"Melange Levels {problem}; staying off.");
                return;
            }
            Active = true;
            Rewards.Start();
        }

        public override void OnUpdate()
        {
            if (Active) OfferActions.Tick();
        }
    }
}
