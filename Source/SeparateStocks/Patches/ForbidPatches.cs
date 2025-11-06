using HarmonyLib;
using RimWorld;
using Verse;

namespace Deep_Gravload.SeparateStocks.Patches
{
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), typeof(Thing), typeof(Pawn))]
    public static class ForbidUtilityThingPatch
    {
        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (__result || t == null || pawn == null)
            {
                return;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(t.MapHeld);
            if (manager == null)
            {
                return;
            }

            if (manager.SeparateStock.AllowPawnAutoUse)
            {
                return;
            }

            if (!manager.ThingInSeparateStock(t))
            {
                return;
            }

            var job = pawn.CurJob;
            if (job != null && (job.playerForced || job.def == SeparateStockDefOf.SeparateStockTransfer))
            {
                return;
            }

            __result = true;
        }
    }
}
