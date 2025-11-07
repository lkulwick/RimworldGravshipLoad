using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload.SeparateStocks.Patches
{
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class ZoneStockpileGizmoPatch
    {
        public static void Postfix(Zone_Stockpile __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Map == null || __instance.Map.IsPlayerHome != true)
            {
                return;
            }

            __result = __result.Concat(SeparateStockGizmoUtility.GetGizmos(__instance));
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.GetGizmos))]
    public static class BuildingStorageGizmoPatch
    {
        public static void Postfix(Building_Storage __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Map == null || __instance.Faction != Faction.OfPlayer)
            {
                return;
            }

            __result = __result.Concat(SeparateStockGizmoUtility.GetGizmos(__instance));
        }
    }

    [StaticConstructorOnStartup]
    internal static class SeparateStockGizmoUtility
    {
        private static readonly Texture2D IconToggleOn = TexButton.ToggleDevPalette;
        private static readonly Texture2D IconToggleOff = TexButton.ToggleLog;

        public static IEnumerable<Gizmo> GetGizmos(ISlotGroupParent parent)
        {
            var map = parent.Map;
            if (map == null)
            {
                yield break;
            }

            var manager = SeparateStockManager.TryGet(map);
            if (manager == null)
            {
                yield break;
            }

            bool inStock = manager.ParentInSeparateStock(parent);

            var toggle = new Command_Toggle
            {
                defaultLabel = "SeparateStock_ToggleLabel".Translate(),
                defaultDesc = "SeparateStock_ToggleDesc".Translate(),
                icon = inStock ? IconToggleOn : IconToggleOff,
                isActive = () => manager.ParentInSeparateStock(parent),
                toggleAction = () => manager.ToggleParentMembership(parent)
            };
            yield return toggle;

            if (inStock)
            {
                yield return new Command_Action
                {
                    defaultLabel = "SeparateStock_ManageLabel".Translate(),
                    defaultDesc = "SeparateStock_ManageDesc".Translate(),
                    icon = TexButton.OpenStatsReport,
                    action = () => Find.WindowStack.Add(new Dialog_ManageSeparateStock(map))
                };
            }
        }
    }
}
