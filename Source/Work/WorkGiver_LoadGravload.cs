using System.Collections.Generic;
using RimWorld;
using SeparateStocks;
using Verse;
using Verse.AI;

namespace Deep_Gravload
{
    public class WorkGiver_LoadGravload : WorkGiver_Scanner
    {
        private static readonly ThingRequest Request = ThingRequest.ForGroup(ThingRequestGroup.HaulableEver);

        public override ThingRequest PotentialWorkThingRequest => Request;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(pawn.Map);
            if (tracker == null)
            {
                return true;
            }

            return !tracker.HasActiveOperations;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(pawn.Map);
            if (tracker == null)
            {
                yield break;
            }

            foreach (Thing thing in tracker.GetPendingThings())
            {
                yield return thing;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(pawn.Map);
            if (tracker == null)
            {
                return false;
            }

            return tracker.CanHandleThing(pawn, t, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(pawn.Map);
            if (tracker == null)
            {
                return null;
            }

            StockJobTicket ticket;
            if (!tracker.TryAssignHaulJob(pawn, t, forced, out ticket))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(DeepGravloadDefOf.Deep_Gravload_LoadCargo, t);
            job.count = ticket.Count;
            job.SetTarget(TargetIndex.B, new LocalTargetInfo(ticket.DestinationCell));
            job.haulOpportunisticDuplicates = false;
            job.ignoreForbidden = forced || !ticket.ToDestinationStock;
            return job;
        }
    }
}
