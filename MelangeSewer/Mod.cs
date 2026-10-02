using MelonLoader;
using Melange.Core;

[assembly: MelonInfo(typeof(Melange.Sewer.Mod), "Melange Sewer", "0.1.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]
// MelonLoader matches dependencies by assembly name, not by the MelonInfo name
[assembly: MelonAdditionalDependencies("MelangeCore")]

namespace Melange.Sewer
{
    /// <summary>
    /// The sewer gets a story. Jerry, who sleeps by the canal, gives the quest "Down the Drain" once the player cooks at
    /// scale; Frank repeats the rumours; Oscar stops hinting at keys. The Sewer King is P.P. Hyland, the town's founder,
    /// and no longer attacks on sight: the player can keep his secret (he teaches toad farming, then runs the Underground
    /// or pays them off) or say it out loud and fight him for the old man's money.
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
                Log.Error($"Melange Sewer {problem}; staying off.");
                return;
            }
            Active = true;
            KingPatch.Apply(HarmonyInstance);
            Story.Start();
        }
    }
}
