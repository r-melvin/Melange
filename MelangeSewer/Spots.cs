using System;
using UnityEngine;
using GameProperty = Il2CppScheduleOne.Property.Property;

namespace Melange.Sewer
{
    /// <summary>
    /// A thing to find: a small marker in the world, picked up by walking up to it. Proximity, not an interactable, because
    /// a working InteractableObject needs the game's interaction layer and collider set-up, which code can't check; the
    /// quest entry's map marker leads the player there.
    /// </summary>
    internal sealed class Spot
    {
        public const float Reach = 2.5f;

        public readonly string Name;
        public Vector3 Position { get; private set; }
        private GameObject _marker;

        public Spot(string name, Vector3 position)
        {
            Name = name;
            Position = position;
        }

        /// <summary>Shows or hides the marker (made on first show; it belongs to the scene and goes with it).</summary>
        public void Show(bool shown)
        {
            try
            {
                if (shown && _marker == null)
                {
                    _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _marker.name = "Melange Sewer " + Name;
                    var collider = _marker.GetComponent<Collider>();
                    if (collider != null) UnityEngine.Object.Destroy(collider);   // nothing to trip over or stand on
                    _marker.transform.position = Position + Vector3.up * 0.05f;
                    _marker.transform.localScale = new Vector3(0.35f, 0.06f, 0.45f);
                }
                if (_marker != null) _marker.SetActive(shown);
            }
            catch (Exception e) { Mod.Log.Warning($"{Name} marker: {e.Message}"); }
        }

        public bool InReach(Vector3 player) => (player - Position).sqrMagnitude <= Reach * Reach;

        // ---- where things are ----

        /// <summary>The journal: at one of the game's own sewer mushroom spots, known to be on walkable sewer floor.</summary>
        public static Vector3? JournalPlace(Il2CppScheduleOne.Map.SewerManager sm)
        {
            var spots = sm != null && sm.SewerMushrooms != null ? sm.SewerMushrooms.MushroomLocations : null;
            if (spots == null || spots.Count == 0) return null;
            var t = spots[spots.Count / 2];                  // a fixed pick, the same every load
            return t != null ? t.position : (Vector3?)null;
        }

        /// <summary>The founder's plaque: at Hyland Manor's spawn point (outside its door, to be checked in game).</summary>
        public static Vector3? PlaquePlace()
        {
            var all = GameProperty.Properties;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.PropertyName == null || p.PropertyName.IndexOf("Manor", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (p.SpawnPoint != null) return p.SpawnPoint.position;
                if (p.PoI != null) return p.PoI.transform.position;
                return p.transform.position;
            }
            return null;
        }
    }
}
