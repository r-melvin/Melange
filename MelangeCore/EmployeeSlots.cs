using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using GameProperty = Il2CppScheduleOne.Property.Property;

namespace Melange.Core
{
    /// <summary>
    /// Extra employee capacity per property, for any spoke (level rewards, the cartel). A bonus is absolute and idempotent:
    /// the property's own capacity is remembered the first time, and capacity becomes that plus the bonus. The game does not
    /// save capacity, so spokes set their bonus again after every load (on <see cref="MainSceneLoaded"/>).
    /// </summary>
    /// <remarks>
    /// Each employee stands at the idle point with its index (Employee.AssignProperty reads EmployeeIdlePoints[index]), so an
    /// index past the array's end breaks assignment: every extra slot gets an idle point, cloned beside the last one.
    /// </remarks>
    public static class EmployeeSlots
    {
        private static readonly Dictionary<string, int> BaseCapacity = new Dictionary<string, int>();
        private static readonly Dictionary<string, Dictionary<string, int>> BonusBySource = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>Sets <paramref name="source"/>'s bonus at a property (by its code). Several spokes' bonuses add up.</summary>
        public static void SetBonus(string source, string propertyCode, int bonus)
        {
            if (!BonusBySource.TryGetValue(source, out var map)) BonusBySource[source] = map = new Dictionary<string, int>();
            map[propertyCode] = Math.Max(0, bonus);
            Apply(propertyCode);
        }

        public static int TotalBonus(string propertyCode)
        {
            int total = 0;
            foreach (var map in BonusBySource.Values)
                if (map.TryGetValue(propertyCode, out int b)) total += b;
            return total;
        }

        /// <summary>Sets capacity at a property to its own capacity plus every source's bonus, adding idle points as needed.</summary>
        public static void Apply(string propertyCode)
        {
            try
            {
                var property = Find(propertyCode);
                if (property == null) return;
                if (!BaseCapacity.TryGetValue(propertyCode, out int baseCap)) BaseCapacity[propertyCode] = baseCap = property.EmployeeCapacity;
                int wanted = baseCap + TotalBonus(propertyCode);
                // never below the employees already there: lowering capacity must not strand anyone
                wanted = Math.Max(wanted, property.Employees != null ? property.Employees.Count : 0);
                EnsureIdlePoints(property, wanted);
                property.EmployeeCapacity = wanted;
            }
            catch (Exception e) { Core.Log?.Warning($"employee slots at {propertyCode}: {e.Message}"); }
        }

        /// <summary>Re-applies every bonus (after a load: the game rebuilt the properties).</summary>
        public static void ApplyAll()
        {
            BaseCapacity.Clear();                                // fresh scene objects: re-read their own capacity
            var codes = new HashSet<string>();
            foreach (var map in BonusBySource.Values) foreach (var code in map.Keys) codes.Add(code);
            foreach (var code in codes) Apply(code);
        }

        /// <summary>Clears every bonus (leaving a save: the next save's bonuses come from its own data).</summary>
        public static void Reset()
        {
            BonusBySource.Clear();
            BaseCapacity.Clear();
        }

        /// <summary>
        /// Can this property take extra employees? Only if it has idle points to copy: every employee needs one to stand at,
        /// and a property without any (the RV) has nowhere to put them.
        /// </summary>
        public static bool CanExtend(GameProperty property)
            => property != null && property.EmployeeIdlePoints != null && property.EmployeeIdlePoints.Length > 0;

        public static GameProperty Find(string propertyCode)
        {
            var all = GameProperty.Properties;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].PropertyCode == propertyCode) return all[i];
            return null;
        }

        private static void EnsureIdlePoints(GameProperty property, int count)
        {
            var points = property.EmployeeIdlePoints;
            int have = points == null ? 0 : points.Length;
            if (have >= count || have == 0) return;             // no template to copy from: leave it to the game's warning
            var grown = new Il2CppReferenceArray<Transform>(count);
            for (int i = 0; i < have; i++) grown[i] = points[i];
            var last = points[have - 1];
            for (int i = have; i < count; i++)
            {
                var clone = UnityEngine.Object.Instantiate(last.gameObject, last.parent).transform;
                clone.name = $"{last.name} (Melange {i - have + 1})";
                clone.position = last.position + last.right * (0.8f * (i - have + 1));   // side by side, not on top of each other
                clone.rotation = last.rotation;
                grown[i] = clone;
            }
            property.EmployeeIdlePoints = grown;
        }
    }
}
