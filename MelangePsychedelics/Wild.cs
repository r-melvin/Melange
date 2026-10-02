using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using S1API.Interaction;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Wild toads. At the pond they come out for a few hours most evenings (PondSchedule) on spots round the water, while a
    /// wildlife officer walks his round (WildlifeOfficer): catching one he can see costs a fine, and from the third that night
    /// a police look-in, and he takes the toad. In the sewer, once its story is open (SewerToads), a few sit by the game's own
    /// mushroom spots. Catching needs a toad net in the pockets. The toads are local objects placed the same way on every
    /// peer (the schedule is rolled from the day); a catch is the catching player's own, like their cash and pockets.
    /// </summary>
    internal static class Wild
    {
        /// <summary>The dice seed. A constant, so clients (who don't have the host's save data) roll the same pond.</summary>
        public const long Seed = 0x7A0D;
        private const int RingSpots = 8;
        private const float ScanEvery = 0.5f;

        private static float _next;
        private static bool _pondSearched;
        private static Vector3 _pond;
        private static float _pondRadius;
        private static readonly List<Vector3> _spots = new List<Vector3>();
        private static readonly Dictionary<int, GameObject> _pondToads = new Dictionary<int, GameObject>();
        private static readonly List<GameObject> _sewerToads = new List<GameObject>();
        private static int _shownPondDay = -1, _shownSewerDay = -1, _announced = -1, _offenceWindow = -1;

        public static void Reset()
        {
            _next = 0f; _pondSearched = false; _spots.Clear(); _pondToads.Clear(); _sewerToads.Clear();
            _shownPondDay = _shownSewerDay = _announced = _offenceWindow = -1;
        }

        public static bool SewerUnlocked()
        {
            try { return NetworkSingleton<SewerManager>.Instance?.IsSewerUnlocked ?? false; } catch { return false; }
        }

        private static MelangePsychedelicsData Data => MelangePsychedelicsData.Current;

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanEvery;
            if (Data == null || Il2CppScheduleOne.PlayerScripts.Player.Local == null) return;
            try { Pond(); } catch (Exception e) { Mod.Log.Warning("pond: " + e.Message); _next = Time.unscaledTime + 30f; }
            try { Sewer(); } catch (Exception e) { Mod.Log.Warning("sewer toads: " + e.Message); _next = Time.unscaledTime + 30f; }
        }

        // ------------------------------------------------------------------ the pond

        private static void Pond()
        {
            if (!Settings.WildToads) { ClearPond(); return; }
            if (!_pondSearched) FindPond();
            if (_spots.Count == 0) return;
            int day = Placed.Day, minute = Clock.ToMinutes(S1API.GameTime.TimeManager.CurrentTime);
            var open = PondSchedule.OpenNow(Seed, day, minute, _spots.Count, out _);
            if (open == null) { ClearPond(); return; }
            var data = Data;
            if (data.PondDay != open.Day) { data.PondDay = open.Day; data.PondCaught.Clear(); }
            if (_offenceWindow != open.Day) { _offenceWindow = open.Day; data.PondOffences = 0; }
            if (_shownPondDay != open.Day)
            {
                ClearPond();
                _shownPondDay = open.Day;
                foreach (int spot in open.Spots)
                    if (!data.PondCaught.Contains(spot)) _pondToads[spot] = Toad(_spots[spot], $"pond {spot}", () => CatchAtPond(spot));
                Mod.Log.Msg($"pond: toads out ({open.Spots.Count}) from {Clock.ToHhmm(open.Window.OpenAt):D4} for {open.Window.Minutes} min");
            }
            if (_announced != open.Day && Near(_pond, 80f))
            {
                _announced = open.Day;
                Items.Notify("Toads at the pond", "They're out tonight. So is the wildlife officer, doing his rounds.");
            }
        }

        private static void FindPond()
        {
            _pondSearched = true;
            Vector3 centre; float radius;
            if (Settings.PondOverride(out centre)) radius = 12f;
            else if (!Locate(out centre, out radius)) { Mod.Log.Warning("pond not found (set PondPosition in the preferences); no wild toads at the pond"); return; }
            _pond = centre;
            _pondRadius = radius;
            for (int i = 0; i < RingSpots; i++)
            {
                float a = i * Mathf.PI * 2f / RingSpots;
                var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + 0.8f);
                _spots.Add(Looks.Ground(p));
            }
            Mod.Log.Msg($"pond at {centre} (radius {radius:F1} m), {_spots.Count} toad spots");
        }

        /// <summary>
        /// The pond: an object named after its water material, else the renderer using that material (StylizedWater2_Pond is
        /// a material name in sharedassets1; the object's own name wasn't seen in the data).
        /// </summary>
        private static bool Locate(out Vector3 centre, out float radius)
        {
            centre = default; radius = 0f;
            var go = GameObject.Find("StylizedWater2_Pond");
            Renderer found = go != null ? go.GetComponentInChildren<Renderer>() : null;
            if (found == null)
                foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    var m = r != null ? r.sharedMaterial : null;
                    if (m != null && m.name != null && m.name.IndexOf("Pond", StringComparison.OrdinalIgnoreCase) >= 0) { found = r; break; }
                }
            if (found == null) return false;
            var b = found.bounds;
            centre = b.center;
            radius = Mathf.Max(2f, Mathf.Min(b.extents.x, b.extents.z));
            return true;
        }

        private static void ClearPond()
        {
            foreach (var t in _pondToads.Values) if (t != null) UnityEngine.Object.Destroy(t);
            _pondToads.Clear();
            _shownPondDay = -1;
        }

        private static void CatchAtPond(int spot)
        {
            var data = Data;
            if (data == null || !_pondToads.ContainsKey(spot)) return;
            if (!Items.Has(Ids.ToadNet)) { Items.Notify("Toad", "You need a toad net. Randy sells them."); return; }
            int minute = Clock.ToMinutes(S1API.GameTime.TimeManager.CurrentTime);
            var open = PondSchedule.OpenNow(Seed, Placed.Day, minute, _spots.Count, out int into);
            if (open == null) return;
            Remove(spot);
            data.PondCaught.Add(spot);
            if (WildlifeOfficer.Sees(spot, into, _spots.Count))
            {
                int offence = ++data.PondOffences;
                float fine = Penalty.Fine(offence);
                float paid = Math.Min(fine, S1API.Money.Money.GetCashBalance());
                if (paid > 0f) S1API.Money.Money.ChangeCashBalance(-paid, true, false);
                string more = "";
                if (Penalty.Pursuit(offence))
                {
                    try
                    {
                        var crime = S1API.Entities.Player.Local?.CrimeData;
                        crime?.SetPursuitLevel(S1API.Law.PursuitLevel.Investigating);
                        crime?.RecordLastKnownPosition(true);
                        more = " He's called it in.";
                    }
                    catch (Exception e) { Mod.Log.Warning("pursuit: " + e.Message); }
                }
                Items.Notify("Wildlife officer", $"Caught taking a protected toad: fined ${paid:N0} and the toad's confiscated.{more}");
                Mod.Log.Msg($"pond: caught by the officer at spot {spot} ({into} min in), offence {offence}, fined {paid:N0}");
                return;
            }
            if (!Items.Give(Items.Make(Ids.LiveToad, 1, LiveToads.ItemTier(ToadOrigin.Wild))))
            {
                data.PondCaught.Remove(spot);                      // pockets full: it hops back
                _shownPondDay = -1;
                Items.Notify("Toad", "No room in your pockets.");
                return;
            }
            Mod.Log.Msg($"pond: toad caught at spot {spot}");
        }

        private static void Remove(int spot)
        {
            if (_pondToads.TryGetValue(spot, out var t) && t != null) UnityEngine.Object.Destroy(t);
            _pondToads.Remove(spot);
        }

        // ------------------------------------------------------------------ the sewer

        private static void Sewer()
        {
            var data = Data;
            var route = data.Route(SewerUnlocked());
            if (!Settings.SewerToads || route == SewerRoute.Closed) { ClearSewer(); return; }
            int day = Placed.Day;
            if (data.SewerDay != day) { data.SewerDay = day; data.SewerCaught = 0; }
            if (_shownSewerDay == day) return;
            ClearSewer();
            _shownSewerDay = day;
            var spots = NetworkSingleton<SewerManager>.Instance?.SewerMushrooms?.MushroomLocations;
            if (spots == null || spots.Count == 0) return;
            int n = Math.Max(0, SewerToads.Count(route, Seed, day) - data.SewerCaught);
            foreach (int i in PondSchedule.Pick(Seed, day, 23, n, spots.Count))
            {
                if (spots[i] == null) continue;
                var at = Looks.Ground(spots[i].position + spots[i].right * 0.6f);
                _sewerToads.Add(Toad(at, $"sewer {i}", CatchInSewer));
            }
            if (n > 0) Mod.Log.Msg($"sewer: {n} toad(s) today ({route})");
        }

        private static void ClearSewer()
        {
            foreach (var t in _sewerToads) if (t != null) UnityEngine.Object.Destroy(t);
            _sewerToads.Clear();
            _shownSewerDay = -1;
        }

        private static void CatchInSewer()
        {
            var data = Data;
            if (data == null) return;
            if (!Items.Has(Ids.ToadNet)) { Items.Notify("Toad", "You need a toad net. Randy sells them."); return; }
            if (!Items.Give(Items.Make(Ids.LiveToad, 1, LiveToads.ItemTier(ToadOrigin.Sewer)))) { Items.Notify("Toad", "No room in your pockets."); return; }
            data.SewerCaught++;
            _shownSewerDay = -1;                                    // redraw without it
            Mod.Log.Msg($"sewer: toad caught ({data.SewerCaught} today)");
        }

        // ------------------------------------------------------------------ shared

        /// <summary>A toad on the ground with a "Catch toad" prompt (a slightly bigger box than the toad, so it's easy to look at).</summary>
        private static GameObject Toad(Vector3 at, string name, Action onCatch)
        {
            var root = new GameObject("Melange toad " + name);
            root.transform.position = at;
            root.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            var model = Looks.Build("toad", root.transform, "model");
            if (model == null) Looks.Box(root.transform, "model", new Vector3(0.18f, 0.08f, 0.16f), new Color(0.45f, 0.42f, 0.25f)).transform.localPosition = new Vector3(0f, 0.04f, 0f);
            Looks.Interactable(root, new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, 0.25f, 0.4f));
            InteractionPrompt.CreateBuilder(root).WithMessage("Catch toad").WithRange(2.5f).WithPriority(6)
                .OnInteractionStarted(onCatch).Build();
            return root;
        }

        private static bool Near(Vector3 p, float metres)
        {
            var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
            return me != null && (me.transform.position - p).sqrMagnitude <= metres * metres;
        }
    }
}
