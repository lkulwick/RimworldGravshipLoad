using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class Patch_Zone_Stockpile_GetGizmos
    {
        public static void Postfix(Zone_Stockpile __instance, ref IEnumerable<Gizmo> __result)
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

        private static void AppendToggleGizmo(Zone_Stockpile zone, GravloadMapComponent tracker, List<Gizmo> gizmos)
        {
            Command_Toggle command = new Command_Toggle();
            command.defaultLabel = "DeepGravload_CommandManageToggle".Translate();
            command.defaultDesc = "DeepGravload_CommandManageToggleDesc".Translate();
            command.icon = TexCommand.ForbidOff;
            command.isActive = delegate
            {
                return tracker.IsManaged(zone);
            };
            command.toggleAction = delegate
            {
                tracker.ToggleZone(zone);
            };
            gizmos.Add(command);
        }
    }

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.Notify_ReceivedThing))]
    public static class Patch_Zone_Stockpile_NotifyReceived
    {
        public static void Postfix(Zone_Stockpile __instance, Thing newItem)
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

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.Notify_LostThing))]
    public static class Patch_Zone_Stockpile_NotifyLost
    {
        public static void Postfix(Zone_Stockpile __instance, Thing newItem)
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

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.AddCell))]
    public static class Patch_Zone_Stockpile_AddCell
    {
        public static void Postfix(Zone_Stockpile __instance)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(__instance?.Map);
            if (tracker == null)
            {
                return;
            }

            tracker.NotifyZoneCellsChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.RemoveCell))]
    public static class Patch_Zone_Stockpile_RemoveCell
    {
        public static void Postfix(Zone_Stockpile __instance)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(__instance?.Map);
            if (tracker == null)
            {
                return;
            }

            tracker.NotifyZoneCellsChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.PostDeregister))]
    public static class Patch_Zone_Stockpile_PostDeregister
    {
        public static void Postfix(Zone_Stockpile __instance)
        {
            Map map = __instance?.Map;
            if (map == null)
            {
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(map);
            tracker?.NotifyParentDestroyed(__instance);
        }
    }
}
