using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Deep_Gravload.SeparateStocks
{
public sealed class JobDriver_SeparateStockTransfer : JobDriver_HaulToCell
{
    private SeparateStockManager _manager;
    private TransferThing _transfer;
    private int _plannedCount;

    private bool _forbiddenInitially;

    public override void Notify_Starting()
    {
        base.Notify_Starting();
        _manager = SeparateStockManager.TryGet(pawn.Map);
        _plannedCount = job.count;
        if (_manager != null)
        {
            _manager.TryGetTransferForThing(ToHaul, pawn, out _transfer, includeReserved: true);
        }

        _forbiddenInitially = TargetThingA != null && TargetThingA.IsForbidden(pawn);
        globalFinishActions.Add(condition =>
        {
            if (_transfer == null)
            {
                return;
            }

            if (condition == JobCondition.Succeeded)
            {
                _manager?.NotifyTransferCompleted(_transfer, _plannedCount);
            }
            else
            {
                _manager?.ReleaseTransfer(_transfer);
            }

            _transfer = null;
        });
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOnBurningImmobile(TargetIndex.B);
        this.FailOnForbidden(TargetIndex.B);
        if (!_forbiddenInitially)
        {
            this.FailOnForbidden(TargetIndex.A);
        }

        yield return Toils_General.DoAtomic(delegate
        {
            startTick = Find.TickManager.TicksGame;
        });

        var reserveTargetA = Toils_Reserve.Reserve(TargetIndex.A);
        yield return reserveTargetA;

        var postCarry = Toils_General.Label();
        var checkJumpPostCarry = Toils_Jump.JumpIf(postCarry, delegate
        {
            var carried = pawn.carryTracker.CarriedThing;
            if (carried == null)
            {
                return false;
            }

            return pawn.carryTracker.AvailableStackSpace(ToHaul.def) <= 0 || carried == ToHaul;
        });
        yield return checkJumpPostCarry;

        yield return Toils_General.DoAtomic(delegate
        {
            if (DropCarriedThingIfNotTarget && pawn.IsCarrying())
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            }
        });

        Toil toilGoto = null;
        toilGoto = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch, canGotoSpawnedParent: true)
            .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
            .FailOn(() =>
            {
                var actor = toilGoto.actor;
                var curJob = actor.jobs.curJob;
                if (curJob.haulMode == HaulMode.ToCellStorage)
                {
                    var thing = curJob.GetTarget(TargetIndex.A).Thing;
                    if (!actor.jobs.curJob.GetTarget(TargetIndex.B).Cell.IsValidStorageFor(Map, thing))
                    {
                        return true;
                    }
                }

                return false;
            });

        yield return toilGoto;
        yield return checkJumpPostCarry;

        yield return Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false, subtractNumTakenFromJobCount: true, failIfStackCountLessThanJobCount: false, reserve: true, HaulAIUtility.IsInHaulableInventory(ToHaul));
        yield return postCarry;

        if (job.haulOpportunisticDuplicates)
        {
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveTargetA, TargetIndex.A, TargetIndex.B);
        }

        var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
        yield return carryToCell;
        yield return PossiblyDelay();
        yield return BeforeDrop();
        yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, storageMode: job.haulMode == HaulMode.ToCellStorage);
    }

    private Toil PossiblyDelay()
    {
        var toil = ToilMaker.MakeToil("SeparateStockPossiblyDelay");
        toil.atomicWithPrevious = true;
        toil.tickIntervalAction = delegate
        {
            if (Find.TickManager.TicksGame >= startTick + 30)
            {
                ReadyForNextToil();
            }
        };
        toil.defaultCompleteMode = ToilCompleteMode.Never;
        return toil;
    }
}
}
