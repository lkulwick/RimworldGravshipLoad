using HarmonyLib;
using RimWorld;
using SeparateStocks;
using Verse;

namespace Deep_Gravload
{
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

            if (SeparateStockContext.AllowCrossStockSearch)
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
