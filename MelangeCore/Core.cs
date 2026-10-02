using System;
using MelonLoader;

[assembly: MelonInfo(typeof(Melange.Core.Core), "Melange Core", "0.3.0", "r-melvin")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Melange.Core
{
    /// <summary>
    /// The hub. Spokes declare <c>[assembly: MelonAdditionalDependencies("MelangeCore")]</c> (the assembly name: MelonLoader matches dependencies by assembly, not by display name) and call
    /// <see cref="Require"/> first thing, so none runs against a hub older than it was built for.
    /// </summary>
    /// <remarks>
    /// The hub owns what must exist once in the game: each Harmony patch on a game method, each injected type, each save
    /// format. Spokes ask the hub for those through its events and services, never patch the same method themselves, and
    /// never reference each other. Paper Trail is not a spoke.
    /// </remarks>
    public sealed class Core : MelonMod
    {
        /// <summary>The hub's version. Additions bump the minor version; anything a spoke could break on bumps the major.</summary>
        public static readonly Version Version = new Version(0, 3, 0);

        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Events.OnHandlerError = message => Log.Error(message);
            GameEvents.Patch(HarmonyInstance);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => GameEvents.SceneInitialized(sceneName);

        /// <summary>
        /// Is this hub new enough for the calling spoke? Same major version and at least the minor it was built against.
        /// When it is not, the spoke should log the returned message and stay off.
        /// </summary>
        public static bool Require(Version builtAgainst, out string problem) => Versioning.IsCompatible(Version, builtAgainst, out problem);
    }
}
