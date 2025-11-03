using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SeparateStocks;

namespace Deep_Gravload
{
    public class JobDriver_LoadGravload : JobDriver
    {
        private const TargetIndex CargoInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B;

        private bool completedSuccessfully;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = this.pawn;
            Thing thing = this.job.GetTarget(CargoInd).Thing;
            if (thing == null)
            {
                return false;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(pawn.Map);

            if (!pawn.Reserve(thing, this.job, this.job.count, 0, null, errorOnFailed))
            {
                tracker?.ReleaseTicket(pawn, false, 0);
                return false;
            }

            IntVec3 cell = this.job.GetTarget(CellInd).Cell;
            if (!cell.IsValid)
            {
                pawn.Map.reservationManager.Release(thing, pawn, this.job);
                tracker?.ReleaseTicket(pawn, false, 0);
                return false;
            }

            if (!pawn.Reserve(cell, this.job, 1, -1, null, errorOnFailed))
            {
                pawn.Map.reservationManager.Release(thing, pawn, this.job);
                tracker?.ReleaseTicket(pawn, false, 0);
                return false;
            }

            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.completedSuccessfully = false;

            this.FailOnDestroyedNullOrForbidden(CargoInd);
            this.FailOn(delegate
            {
                GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(this.pawn.Map);
                if (tracker == null)
                {
                    return true;
                }

                return tracker.GetTicket(this.pawn) == null;
            });

            this.AddFinishAction(delegate
            {
                if (this.completedSuccessfully)
                {
                    return;
                }

                GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(this.pawn.Map);
                if (tracker != null)
                {
                    tracker.ReleaseTicket(this.pawn, false, 0);
                }
            });

            yield return Toils_Goto.GotoThing(CargoInd, PathEndMode.Touch);

            Toil carry = Toils_Haul.StartCarryThing(CargoInd, true, false, false, false);
            carry.AddPreInitAction(delegate
            {
                GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(this.pawn.Map);
                if (tracker == null)
                {
                    return;
                }

                StockJobTicket ticket = tracker.GetTicket(this.pawn);
                if (ticket != null)
                {
                    this.job.count = ticket.Count;
                }
            });
            yield return carry;

            yield return Toils_Goto.GotoCell(CellInd, PathEndMode.Touch);

            Toil deposit = new Toil();
            deposit.initAction = delegate
            {
                Pawn actor = deposit.actor;
                GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(actor.Map);
                StockJobTicket ticket = tracker != null ? tracker.GetTicket(actor) : null;
                if (tracker == null || ticket == null)
                {
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Thing carried = actor.carryTracker.CarriedThing;
                if (carried == null)
                {
                    tracker.ReleaseTicket(actor, false, 0);
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int desired = this.job.count;
                if (desired <= 0 || desired > carried.stackCount)
                {
                    desired = carried.stackCount;
                }

                Thing toPlace = carried;
                if (carried.stackCount > desired)
                {
                    toPlace = carried.SplitOff(desired);
                }

                IntVec3 cell = this.job.GetTarget(CellInd).Cell;
                if (!GenPlace.TryPlaceThing(toPlace, cell, actor.Map, ThingPlaceMode.Direct, out Thing placed))
                {
                    if (toPlace != carried)
                    {
                        carried.TryAbsorbStack(toPlace, false);
                    }

                    tracker.ReleaseTicket(actor, false, 0);
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (tracker != null)
                {
                    Thing managedThing = placed ?? toPlace;
                    tracker.NotifyItemPlacedInManagedCell(managedThing);
                }

                Thing leftover = actor.carryTracker.CarriedThing;
                if (leftover != null && leftover.stackCount > 0)
                {
                    GenPlace.TryPlaceThing(leftover, actor.Position, actor.Map, ThingPlaceMode.Near);
                    actor.carryTracker.innerContainer.Clear();
                }
                else if (actor.carryTracker.CarriedThing == null)
                {
                    actor.carryTracker.innerContainer.Clear();
                }

                tracker.ReleaseTicket(actor, true, desired);
                this.completedSuccessfully = true;
            };
            deposit.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deposit;
        }
    }
}
