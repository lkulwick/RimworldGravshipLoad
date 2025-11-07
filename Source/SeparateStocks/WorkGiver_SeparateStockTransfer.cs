using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Deep_Gravload.SeparateStocks
{
public class WorkGiver_SeparateStockTransfer : WorkGiver_Scanner
{
    public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        var manager = SeparateStockManager.TryGet(pawn.Map);
        if (manager == null)
        {
            yield break;
        }

        foreach (var transfer in manager.GetPendingTransferThings())
        {
            if (transfer.Thing != null && transfer.Thing.Spawned)
            {
                yield return transfer.Thing;
            }
        }
    }

    public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        if (pawn?.Map == null || t == null || !t.Spawned)
        {
            return false;
        }

        var manager = SeparateStockManager.TryGet(pawn.Map);
        if (manager == null || !manager.TryGetTransferForThing(t, pawn, out var transfer) || transfer.RemainingCount <= 0)
        {
            return false;
        }

        if (!pawn.CanReserve(t, 1, -1, null, forced))
        {
            return false;
        }

        if (transfer.Direction == TransferDirection.ColonyToStock)
        {
            if (!manager.TryFindStorageCellForTransfer(pawn, t, out _, out _))
            {
                return false;
            }
        }

        return true;
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        var manager = SeparateStockManager.TryGet(pawn.Map);
        if (manager == null || !manager.TryGetTransferForThing(t, pawn, out var transfer, includeReserved: true))
        {
            return null;
        }

        var destination = GetDestination(pawn, manager, transfer, t);
        if (!destination.IsValid)
        {
            manager.ReleaseTransfer(transfer);
            return null;
        }

        int capacity = int.MaxValue;
        if (transfer.Direction == TransferDirection.ColonyToStock)
        {
            capacity = destination.Cell.GetItemStackSpaceLeftFor(pawn.Map, t.def);
            if (capacity <= 0)
            {
                Messages.Message("SeparateStock_NoRoom".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                manager.ReleaseTransfer(transfer);
                return null;
            }
        }

        manager.ReserveTransfer(transfer, pawn);
        int carryCount = transfer.RemainingCount;
        carryCount = Mathf.Min(carryCount, t.stackCount);
        if (transfer.Direction == TransferDirection.ColonyToStock)
        {
            carryCount = Mathf.Min(carryCount, capacity);
        }
        if (carryCount <= 0)
        {
            manager.ReleaseTransfer(transfer);
            return null;
        }

        var job = JobMaker.MakeJob(SeparateStockDefOf.SeparateStockTransfer, t, destination);
        job.count = carryCount;
        job.haulMode = transfer.Direction == TransferDirection.ColonyToStock ? HaulMode.ToCellStorage : HaulMode.ToCellNonStorage;
        job.playerForced = forced;
        job.targetQueueB = null;
        job.countQueue = null;
        return job;
    }

    private static LocalTargetInfo GetDestination(Pawn pawn, SeparateStockManager manager, TransferThing transfer, Thing thing)
    {
        if (transfer.Direction == TransferDirection.ColonyToStock)
        {
            if (manager.TryFindStorageCellForTransfer(pawn, thing, out var cell, out _))
            {
                return cell;
            }

            Messages.Message("SeparateStock_NoRoom".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }
        else
        {
            var origin = thing.PositionHeld;
            if (manager.TryGetDropCellOutsideStock(origin, out var drop))
            {
                return drop;
            }
            Messages.Message("SeparateStock_NoRoom".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }

        return LocalTargetInfo.Invalid;
    }
}
}
