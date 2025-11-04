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

            Toil place = Toils_Haul.PlaceHauledThingInCell(CellInd, null, storageMode: true);
            yield return place;

            Toil finalize = new Toil();
            finalize.initAction = delegate
            {
                Pawn actor = finalize.actor;
                GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(actor.Map);
                IntVec3 cell = actor.CurJob.GetTarget(CellInd).Cell;

                if (tracker != null)
                {
                    List<Thing> things = cell.GetThingList(actor.Map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        tracker.NotifyItemPlacedInManagedCell(things[i]);
                    }

                    tracker.ReleaseTicket(actor, true, this.job.count);
                }

                this.completedSuccessfully = true;
            };
            finalize.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finalize;
        }
    }
}
