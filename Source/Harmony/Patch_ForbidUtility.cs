using HarmonyLib;
using RimWorld;
using SeparateStocks;
using Verse;

namespace Deep_Gravload
{
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), typeof(Thing), typeof(Pawn))]
    public static class Patch_ForbidUtility_IsForbiddenPawn
    {
        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (__result || t == null || pawn == null)
            {
                return;
            }

            if (SeparateStockContext.AllowSeparateStockHauling || SeparateStockContext.AllowCrossStockSearch)
            {
                return;
            }

            Map map = t.MapHeld ?? pawn.MapHeld;
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

            StockJobTicket ticket = stockManager.GetTicket(pawn);
            if (ticket != null && ticket.SourceThing == t)
            {
                return;
            }

            if (stockManager.IsThingQueued(t))
            {
                return;
            }

            if (pawn.CurJob != null && pawn.CurJob.playerForced)
            {
                return;
            }

            __result = true;
        }
    }

    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), typeof(Thing), typeof(Faction))]
    public static class Patch_ForbidUtility_IsForbiddenFaction
    {
        public static void Postfix(Thing t, Faction faction, ref bool __result)
        {
            if (__result || t == null || faction != Faction.OfPlayer)
            {
                return;
            }

            if (SeparateStockContext.AllowSeparateStockHauling || SeparateStockContext.AllowCrossStockSearch)
            {
                return;
            }

            Map map = t.MapHeld;
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

            __result = true;
        }
    }
}
