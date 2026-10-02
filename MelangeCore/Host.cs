namespace Melange.Core
{
    /// <summary>
    /// Who runs game logic. Only the host (or a single-player game) decides and changes things; a co-op client sees the
    /// results through the game's own networking, so nothing happens twice.
    /// </summary>
    public static class Host
    {
        public static bool IsHost
        {
            get { try { return Il2CppFishNet.InstanceFinder.IsServer || Il2CppFishNet.InstanceFinder.IsOffline; } catch { return true; } }
        }
    }
}
