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
            ReplaceStorageCommandIfManaged(tracker.IsManaged(__instance), gizmos);
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

        private static void ReplaceStorageCommandIfManaged(bool isManaged, List<Gizmo> gizmos)
        {
            if (!isManaged)
            {
                return;
            }

            TaggedString storageLabel = "CommandOpenStorageSettings".Translate();
            for (int i = 0; i < gizmos.Count; i++)
            {
                Command_Action action = gizmos[i] as Command_Action;
                if (action == null)
                {
                    continue;
                }

                if (!action.defaultLabel.Equals(storageLabel))
                {
                    continue;
                }

                action.defaultDesc = "DeepGravload_StorageLockedDesc".Translate();
                action.Disable("DeepGravload_StorageLockedReason".Translate());
                return;
            }
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
}
