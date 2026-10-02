using UnityEngine;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;

namespace Melange.Smuggling
{
    /// <summary>
    /// Watches whether the local player is coming the bootleggers' way: last seen on the route (the sewer tunnels, the
    /// canal bed, the canal mouth at the Docks) and whether they've been in a vehicle since. Polled once a second on the
    /// host; the decision is Risk.ViaRoute. Only the host's own deliveries are judged (co-op: the host decides).
    /// </summary>
    internal static class Route
    {
        private static float _lastOnRoute = -1f;
        private static bool _droveSince;

        public static void Forget() { _lastOnRoute = -1f; _droveSince = false; }

        public static void Tick()
        {
            var me = GamePlayer.Local;
            if (me == null) return;
            var p = me.transform.position;
            if (Quay.OnRoute(p.x, p.y, p.z)) { _lastOnRoute = Time.realtimeSinceStartup; _droveSince = false; }
            else if (me.IsInVehicle) _droveSince = true;
        }

        public static float SecondsSinceOnRoute => _lastOnRoute < 0f ? -1f : Time.realtimeSinceStartup - _lastOnRoute;
        public static bool DroveSince => _droveSince;

        /// <summary>
        /// TEST ONLY (the probe's <c>smuggling route walked</c>): as if the player had just come off the route on foot, so the
        /// next delivery within the window counts as via the route without walking the tunnels. Tick keeps it until the player
        /// gets into a vehicle, as it would after a real walk.
        /// </summary>
        public static void PretendWalked() { _lastOnRoute = Time.realtimeSinceStartup; _droveSince = false; }
    }
}
