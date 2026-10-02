using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Vehicles.AI;
using UnityEngine;
using GamePlayer = Il2CppScheduleOne.PlayerScripts.Player;

namespace Melange.Smuggling
{
    /// <summary>
    /// EXPERIMENTAL, off by default (setting TankerJob), and needs MethylamineItemId: the methylamine tanker. On a day the
    /// rules pick (Logic/TankerJob.cs), Dafydd tips the player off mid-morning; a couple of minutes later a vehicle leaves
    /// Billy's chemical plant in Westville and drives itself to the Docks plant on the road graph
    /// (<c>VehicleAgent.Navigate</c>, the same call the game's police and commuters use, with no driver NPC). Stop it on
    /// the road (block it, stand by it on foot) and its methylamine goes into your pockets.
    /// </summary>
    /// <remarks>
    /// The seam: <see cref="DayPassed"/> decides, <see cref="Tick"/> runs the job, and everything game-side is in this one
    /// class, so it can be replaced by a better set piece (a real tanker model, a driver, guards) without touching the
    /// boat. The vehicle is spawned as not player-owned, and VehicleManager saves only player-owned vehicles
    /// (GetSaveString, VehicleManager.cs:119-127), so a save mid-job leaves nothing behind; the job itself is not saved
    /// and a reload ends it. There is no tanker model in the game: a van stands in (TankerVehicle setting).
    /// </remarks>
    internal static class Tanker
    {
        private enum Phase { Idle, TipDue, Waiting, Driving, Done }

        private static readonly Vector3 From = new Vector3(-100f, 0f, 80f);     // by Billy's chemical plant, Westville (-103,-4,87)
        private static readonly Vector3 To = new Vector3(-51f, 0f, -74f);       // the Docks chemical plant (Billy's stand point)
        private const float SpawnDelay = 120f, GiveUpAfter = 600f, DespawnDistance = 80f;

        private static Phase _phase;
        private static LandVehicle _van;
        private static float _spawnAt, _startedAt, _stillSince = -1f;
        private static bool _moved;

        public static void Forget()
        {
            _phase = Phase.Idle; _van = null; _stillSince = -1f; _moved = false;
        }

        public static void DayPassed(SmugglingState s, SmugglingRules r, int rank, int day, System.Random rng)
        {
            if (_phase != Phase.Idle) return;
            if (!TankerJob.TipDue(Settings.Tanker, Settings.MethylamineId, s, rank, day, rng.NextDouble(), r)) return;
            if (!Cargo.Exists(Settings.MethylamineId)) { Mod.Log.Warning($"tanker: no item {Settings.MethylamineId}; no job"); return; }
            s.LastTankerTipDay = day;
            _phase = Phase.TipDue;
            Mod.Log.Msg("tanker: a job is due today");
        }

        /// <summary>Probe: the job now, whatever the rules say (the setting and the item are still needed).</summary>
        public static string Force(SmugglingState s)
        {
            if (!Settings.Tanker) return "the TankerJob setting is off";
            if (!Cargo.Exists(Settings.MethylamineId)) return $"no item '{Settings.MethylamineId}' (MethylamineItemId)";
            if (_phase != Phase.Idle) return "a job is already running: " + _phase;
            _spawnAt = Time.realtimeSinceStartup + 5f;
            _phase = Phase.Waiting;
            Dafydd.Instance?.Text(Lines.TankerTip);
            return "tipped; the tanker leaves in 5 s";
        }

        public static void Tick(SmugglingState s)
        {
            switch (_phase)
            {
                case Phase.TipDue:
                    int t = NetworkSingleton<TimeManager>.Instance.CurrentTime;
                    if (t < 1000 || t > 1600) return;
                    Dafydd.Instance?.Text(Lines.TankerTip);
                    _spawnAt = Time.realtimeSinceStartup + SpawnDelay;
                    _phase = Phase.Waiting;
                    break;
                case Phase.Waiting:
                    if (Time.realtimeSinceStartup >= _spawnAt) Spawn();
                    break;
                case Phase.Driving:
                    Drive(s);
                    break;
                case Phase.Done:
                    Cleanup();
                    break;
            }
        }

        private static void Spawn()
        {
            try
            {
                var manager = NetworkSingleton<VehicleManager>.Instance;
                var at = Ground(From) + Vector3.up;
                _van = manager.SpawnAndReturnVehicle(Settings.TankerVehicle, at, Quaternion.LookRotation(To - From), false);
                if (_van == null || _van.Agent == null) { Mod.Log.Warning($"tanker: vehicle {Settings.TankerVehicle} didn't spawn or can't drive"); _phase = Phase.Done; return; }
                var settings = new NavigationSettings { endAtRoad = true, ensureProximityToGraph = true, teleportToGraphIfCalculationFails = true };
                _van.Agent.Flags.ResetFlags();
                _van.Agent.Flags.OverrideSpeed = true;
                _van.Agent.Flags.OverriddenSpeed = 45f;
                _van.Agent.Navigate(Ground(To), settings, null);
                _startedAt = Time.realtimeSinceStartup;
                _moved = false; _stillSince = -1f;
                _phase = Phase.Driving;
                Mod.Log.Msg($"tanker: {Settings.TankerVehicle} on its way from ({at.x:0},{at.z:0})");
            }
            catch (Exception e) { Mod.Log.Warning("tanker: " + e.Message); _phase = Phase.Done; }
        }

        private static void Drive(SmugglingState s)
        {
            if (_van == null) { _phase = Phase.Idle; return; }
            float now = Time.realtimeSinceStartup;
            var pos = _van.transform.position;
            float speed = _van.Speed_Kmh;
            if (speed > 5f) _moved = true;
            if (speed < 1f) { if (_stillSince < 0f) _stillSince = now; }
            else _stillSince = -1f;

            var me = GamePlayer.Local;
            float dist = me != null ? Vector3.Distance(me.transform.position, pos) : float.MaxValue;
            bool onFoot = me != null && !me.IsInVehicle;
            if (TankerJob.Hijacked(_moved, _stillSince < 0f ? 0f : now - _stillSince, dist, onFoot))
            {
                try { _van.Agent.StopNavigating(); } catch { }
                int units = TankerJob.Yield(Settings.TankerMethylamine, s.RouteKnown);
                int given = Cargo.Give(Settings.MethylamineId, units);
                Mod.Log.Msg($"tanker: stopped by the player, {given}/{units} methylamine given");
                Dafydd.Instance?.Text(Lines.TankerTaken);
                if (given < units) Smuggling.Notify("Tanker", $"No room for {units - given} more methylamine.");
                _phase = Phase.Done;
                return;
            }
            bool arrived = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(To.x, 0f, To.z)) < 10f;
            if (arrived || now - _startedAt > GiveUpAfter)
            {
                Mod.Log.Msg($"tanker: {(arrived ? "reached the Docks" : "gave up")} unstopped");
                Dafydd.Instance?.Text(Lines.TankerMissed);
                _phase = Phase.Done;
            }
        }

        /// <summary>The van goes once nobody is looking (the player far enough away), so it never vanishes in front of them.</summary>
        private static void Cleanup()
        {
            if (_van == null) { _phase = Phase.Idle; return; }
            var me = GamePlayer.Local;
            if (me != null && Vector3.Distance(me.transform.position, _van.transform.position) < DespawnDistance) return;
            try
            {
                if (!_van.IsOccupied) { _van.DestroyVehicle(); Mod.Log.Msg("tanker: cleared away"); }
                else return;                                   // the player took it for a spin: wait
            }
            catch (Exception e) { Mod.Log.Warning("tanker cleanup: " + e.Message); }
            _van = null;
            _phase = Phase.Idle;
        }

        private static Vector3 Ground(Vector3 at)
        {
            try
            {
                if (Physics.Raycast(new Vector3(at.x, 40f, at.z), Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            }
            catch { }
            return at;
        }
    }
}
