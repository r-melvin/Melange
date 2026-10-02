using System;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The drying rack takes any loose product of compat type Shrooms, which is right for Toad (curing the venom is the point)
    /// and wrong for LSD: loose tabs (a sheet broken down at a packaging station) would gain a grade for nothing. This keeps
    /// LSD and its mixes off the rack. IsItemDryable is a static method with a switch, not a one-liner, so IL2CPP keeps it a
    /// real call (unchecked in game: TESTING.md). The hub doesn't hook it; if a second spoke needs it, it belongs in the hub.
    /// </summary>
    internal static class Drying
    {
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(ItemFilter_Dryable), nameof(ItemFilter_Dryable.IsItemDryable), new[] { typeof(ItemInstance) });
                if (target == null) { Mod.Log.Warning("ItemFilter_Dryable.IsItemDryable not found: LSD tabs can go on drying racks"); return; }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Drying), nameof(AfterIsItemDryable)));
            }
            catch (Exception e) { Mod.Log.Warning("drying-rack patch failed: " + e.Message); }
        }

        private static void AfterIsItemDryable(ItemInstance instance, ref bool __result)
        {
            if (!__result || instance == null) return;
            string id = instance.ID;
            if (id == Ids.LsdProduct || (id != null && Products.LsdMixIds.Contains(id))) __result = false;
        }
    }
}
