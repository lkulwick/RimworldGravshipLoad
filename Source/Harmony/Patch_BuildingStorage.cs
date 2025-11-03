using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.GetGizmos))]
    public static class Patch_Building_Storage_GetGizmos
    {
        public static void Postfix(Building_Storage __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null)
            {
                return;
            }

            Map map = __instance.Map;
            if (map == null)
            {
                return;
            }

            if (!ManagedStorageUtility.ShouldOfferManagedToggle(__instance))
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(map);
            if (tracker == null)
            {
                return;
            }

            List<Gizmo> gizmos = ManagedStorageUtility.Materialize(__result);
            AppendToggleGizmo(__instance, tracker, gizmos);
            __result = gizmos;
        }

        private static void AppendToggleGizmo(Building_Storage storage, GravloadMapComponent tracker, List<Gizmo> gizmos)
        {
            Command_Toggle command = new Command_Toggle();
            command.defaultLabel = "DeepGravload_CommandManageToggle".Translate();
            command.defaultDesc = "DeepGravload_CommandManageToggleDesc".Translate();
            command.icon = TexCommand.ForbidOff;
            command.isActive = delegate
            {
                return tracker.IsManaged(storage);
            };
            command.toggleAction = delegate
            {
                tracker.ToggleBuilding(storage);
            };
            gizmos.Add(command);
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Notify_ReceivedThing))]
    public static class Patch_Building_Storage_NotifyReceived
    {
        public static void Postfix(Building_Storage __instance, Thing newItem)
        {
            if (__instance == null)
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(__instance.Map);
            if (tracker == null)
            {
                return;
            }

            tracker.OnStoredThingReceived(__instance, newItem);
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Notify_LostThing))]
    public static class Patch_Building_Storage_NotifyLost
    {
        public static void Postfix(Building_Storage __instance, Thing newItem)
        {
            if (__instance == null)
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(__instance.Map);
            if (tracker == null)
            {
                return;
            }

            tracker.OnStoredThingLost(newItem);
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.SpawnSetup))]
    public static class Patch_Building_Storage_SpawnSetup
    {
        public static void Postfix(Building_Storage __instance)
        {
            if (__instance == null)
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(__instance.Map);
            tracker?.NotifyManagedBuildingSpawned(__instance);
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.DeSpawn))]
    public static class Patch_Building_Storage_DeSpawn
    {
        public static void Postfix(Building_Storage __instance)
        {
            Map map = __instance?.MapHeld;
            if (map == null)
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(map);
            tracker?.NotifyParentDestroyed(__instance);
        }
    }
}
