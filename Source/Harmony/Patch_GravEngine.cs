using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(Building_GravEngine), nameof(Building_GravEngine.GetGizmos))]
    public static class Patch_GravEngine_GetGizmos
    {
        public static void Postfix(Building_GravEngine __instance, ref IEnumerable<Gizmo> __result)
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

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(map);
            if (tracker == null)
            {
                return;
            }

            List<Gizmo> gizmos = ManagedStorageUtility.Materialize(__result);
            gizmos.Add(CreateLoadCommand(__instance, tracker));
            __result = gizmos;
        }

        private static Command_Action CreateLoadCommand(Building_GravEngine engine, GravloadMapComponent tracker)
        {
            Command_Action command = new Command_Action();
            command.defaultLabel = "DeepGravload_CommandLoadCargo".Translate();
            command.defaultDesc = "DeepGravload_CommandLoadCargoDesc".Translate();
            command.icon = TexCommand.Install;
            command.action = delegate
            {
                if (!tracker.HasManagedCellsForEngine(engine))
                {
                    Messages.Message("DeepGravload_CommandLoadCargoNoStorage".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                Find.WindowStack.Add(new Dialog_GravloadCargo(engine));
            };

            if (!tracker.HasManagedCellsForEngine(engine))
            {
                command.Disable("DeepGravload_CommandLoadCargoNoStorage".Translate());
            }

            return command;
        }
    }
}
