using HarmonyLib;
using SeparateStocks;
using Verse;
using Verse.AI;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaul))]
    public static class Patch_HaulAIUtility_PawnCanAutomaticallyHaul
    {
        public static void Prefix(Pawn p, Thing t, ref bool __state)
        {
            __state = false;
            if (p == null || t == null)
            {
                return;
            }

            Map map = t.Map ?? p.Map;
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

            SeparateStockContext.PushHaulAllowance();
            __state = true;
        }

        public static void Postfix(bool __state)
        {
            if (__state)
            {
                SeparateStockContext.PopHaulAllowance();
            }
        }
    }
}
