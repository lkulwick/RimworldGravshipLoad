using HarmonyLib;
using RimWorld;
using Verse;

namespace Deep_Gravload.SeparateStocks.Patches
{
    [HarmonyPatch(typeof(SlotGroup), nameof(SlotGroup.Notify_AddedCell))]
    public static class SlotGroupAddedCellPatch
    {
        public static void Postfix(SlotGroup __instance, IntVec3 c)
        {
            var parent = __instance?.parent;
            if (parent == null)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(parent.Map);
            if (manager != null && manager.ParentInSeparateStock(parent))
            {
                manager.SeparateStock.RegisterCell(c);
            }
        }
    }

    [HarmonyPatch(typeof(SlotGroup), nameof(SlotGroup.Notify_LostCell))]
    public static class SlotGroupLostCellPatch
    {
        public static void Postfix(SlotGroup __instance, IntVec3 c)
        {
            var parent = __instance?.parent;
            if (parent == null)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(parent.Map);
            if (manager != null && manager.ParentInSeparateStock(parent))
            {
                manager.SeparateStock.ForgetCell(c);
            }
        }
    }
}
