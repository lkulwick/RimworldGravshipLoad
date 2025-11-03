using HarmonyLib;
using RimWorld;
using SeparateStocks;
using Verse;
using Verse.AI;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(ListerHaulables), "ShouldBeHaulable")]
    public static class Patch_ListerHaulables_ShouldBeHaulable
    {
        private static readonly AccessTools.FieldRef<ListerHaulables, Map> MapField =
            AccessTools.FieldRefAccess<ListerHaulables, Map>("map");

        public static void Postfix(ListerHaulables __instance, Thing t, ref bool __result)
        {
            if (!__result || t == null)
            {
                return;
            }

            Map map = MapField(__instance);
            if (map == null)
            {
                return;
            }

            StockManagerComponent stockManager = map.GetComponent<StockManagerComponent>();
            if (stockManager == null)
            {
                return;
            }

            int stockId = stockManager.GetStockOfThing(t);
            if (stockId == StockConstants.ColonyStockId)
            {
                return;
            }

            if (stockManager.IsThingQueued(t))
            {
                return;
            }

            __result = false;
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
    public static class Patch_HaulAIUtility_PawnCanAutomaticallyHaulFast
    {
        public static void Postfix(Pawn p, Thing t, bool forced, ref bool __result)
        {
            if (!__result || forced || p == null || t == null)
            {
                return;
            }

            Map map = p.Map;
            if (map == null)
            {
                return;
            }

            StockManagerComponent stockManager = map.GetComponent<StockManagerComponent>();
            if (stockManager == null)
            {
                return;
            }

            int stockId = stockManager.GetStockOfThing(t);
            if (stockId == StockConstants.ColonyStockId)
            {
                return;
            }

            if (stockManager.IsThingQueued(t))
            {
                return;
            }

            __result = false;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    public static class Patch_StoreUtility_TryFindBestBetterStoreCellFor
    {
        public static void Postfix(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, ref IntVec3 foundCell, bool needAccurateResult, ref bool __result)
        {
            if (!__result || t == null || map == null)
            {
                return;
            }

            StockManagerComponent stockManager = map.GetComponent<StockManagerComponent>();
            if (stockManager == null)
            {
                return;
            }

            int thingStock = stockManager.GetStockOfThing(t);
            int cellStock = stockManager.GetStockOfCell(foundCell);

            if (thingStock == cellStock)
            {
                return;
            }

            if (thingStock != StockConstants.ColonyStockId && stockManager.IsThingQueued(t))
            {
                return;
            }

            if (carrier != null && carrier.CurJob != null && carrier.CurJob.playerForced)
            {
                return;
            }

            __result = false;
            foundCell = IntVec3.Invalid;
        }
    }
}
