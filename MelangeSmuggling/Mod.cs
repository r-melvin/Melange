using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Smuggling.Mod), "Melange Smuggling", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// MelonLoader matches dependencies by assembly name, not by the MelonInfo name
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Smuggling
{
    /// <summary>
    /// Turnip Night's speedboat. Spend big at Oscar's often enough and he passes on the number of Dafydd "Turnip Night"
    /// Seabiscuit, who runs a speedboat out of the Docks quay: he texts huge bulk orders with a deadline, the player loads
    /// the boat before it sails at night (with or without the product), and it pays on sailing and brings imports back.
    /// The bootleggers' route from the sewer spoke gets product to the quay with no police risk.
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
                Log.Error($"Melange Smuggling {problem}; staying off.");
                return;
            }
            Active = true;
            Settings.Create();
            Smuggling.Start();
        }
    }
}
