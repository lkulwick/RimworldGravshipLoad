using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Deep_Gravload.SeparateStocks.Patches
{
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
    public static class HaulAIUtilityPatch
    {
        public static void Postfix(Pawn p, Thing t, bool forced, ref bool __result)
        {
            if (!__result || p?.Map == null || t == null)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(p.Map);
            if (manager == null)
            {
                return;
            }

            if (!manager.SeparateStock.AllowPawnAutoUse && manager.ThingInSeparateStock(t) && !forced)
            {
                if (p.CurJob != null && p.CurJob.def == SeparateStockDefOf.SeparateStockTransfer)
                {
                    return;
                }

                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    public static class StoreUtilityTryFindPatch
    {
        public static void Postfix(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, ref IntVec3 foundCell, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(map);
            if (manager == null)
            {
                return;
            }

            bool cellInSeparate = manager.CellBelongsToSeparateStock(foundCell);
            bool thingInSeparate = manager.ThingInSeparateStock(t);
            if (!thingInSeparate && carrier?.CurJob != null)
            {
                var job = carrier.CurJob;
                var target = job.targetB;
                if (target.IsValid)
                {
                    if (target.HasThing)
                    {
                        if (manager.ThingInSeparateStock(target.Thing))
                        {
                            thingInSeparate = true;
                        }
                    }
                    else if (manager.CellBelongsToSeparateStock(target.Cell))
                    {
                        thingInSeparate = true;
                    }
                }
            }

            if (cellInSeparate == thingInSeparate)
            {
                return;
            }

            if (carrier?.CurJob != null && carrier.CurJob.def == SeparateStockDefOf.SeparateStockTransfer)
            {
                return;
            }

            if (manager.TryFindStorageCellMatchingStock(t, carrier, map, currentPriority, faction, thingInSeparate, out var replacement))
            {
                foundCell = replacement;
                return;
            }

            __result = false;
            foundCell = IntVec3.Invalid;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterNonSlotGroupStorageFor))]
    public static class StoreUtilityNonSlotPatch
    {
        public static void Postfix(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, ref IHaulDestination haulDestination, bool acceptSamePriority, bool requiresDestReservation, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            if (haulDestination is Building building && building is ISlotGroupParent)
            {
                return;
            }

            var manager = SeparateStockManager.TryGet(map);
            if (manager == null || haulDestination == null)
            {
                return;
            }

            if (haulDestination is Thing destThing && manager.ThingInSeparateStock(destThing))
            {
                if (carrier?.CurJob != null && carrier.CurJob.def == SeparateStockDefOf.SeparateStockTransfer)
                {
                    return;
                }

                __result = false;
                haulDestination = null;
            }
        }
    }
}
