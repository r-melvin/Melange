using System;
using HarmonyLib;
using Il2CppScheduleOne.NPCs.CharacterClasses;

namespace Melange.Sewer
{
    /// <summary>
    /// The Sewer King no longer attacks on sight. His <c>OnTick</c> (multi-line, so IL2CPP keeps it a real call) does one
    /// thing beyond the base NPC's empty tick: on the server, set him on the first player found in the Sewer Office. A
    /// prefix skips it while the quest says he stays calm. A fight that starts anyway was started by the player; the prefix
    /// reports that, and he stays hostile from then on.
    /// </summary>
    /// <remarks>
    /// The only Melange patch on this method. If a second spoke ever needs it, it moves to the hub.
    /// </remarks>
    internal static class KingPatch
    {
        /// <summary>Set while we ourselves are turning him on the player, so that isn't mistaken for an attack.</summary>
        internal static bool SettingHostile;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(SewerKing), nameof(SewerKing.OnTick))
                    ?? throw new MissingMethodException(nameof(SewerKing), nameof(SewerKing.OnTick));
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(KingPatch), nameof(BeforeTick)));
            }
            catch (Exception e) { Mod.Log.Warning($"the Sewer King keeps his vanilla temper (patch failed): {e.Message}"); }
        }

        private static bool BeforeTick(SewerKing __instance)
        {
            try
            {
                if (__instance == null || __instance.Health == null || __instance.Health.IsDead) return true;
                var quest = Story.Quest;
                if (quest == null) return true;                          // no save data: the game's own behaviour
                bool fighting = __instance.Behaviour != null && __instance.Behaviour.CombatBehaviour != null
                                && __instance.Behaviour.CombatBehaviour.Enabled;
                if (fighting && quest.KingStaysCalm && !SettingHostile && Core.Host.IsHost)
                    Story.OnKingAttacked();
                return !quest.KingStaysCalm;
            }
            catch (Exception e)
            {
                Mod.Log.Warning("King tick: " + e.Message);
                return true;
            }
        }
    }
}
