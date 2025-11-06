using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeparateStocks
{
    public class StockManagerComponent : MapComponent
    {
        private List<StockRecord> stocks = new List<StockRecord>();
        private List<StockTransferOperation> activeOperations = new List<StockTransferOperation>();
        private readonly Dictionary<Pawn, StockJobTicket> activeTickets = new Dictionary<Pawn, StockJobTicket>();
        private readonly Dictionary<ISlotGroupParent, int> parentToStock = new Dictionary<ISlotGroupParent, int>();
        private readonly List<Pawn> tmpPawns = new List<Pawn>();

        private int nextStockId = 1;
        private int nextOperationId = 1;
        private bool cellsDirty;
        private int tickCounter;

        public StockManagerComponent(Map map) : base(map)
        {
        }

        public IReadOnlyList<StockRecord> Stocks => this.stocks;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref this.stocks, "stocks", LookMode.Deep);
            Scribe_Collections.Look(ref this.activeOperations, "operations", LookMode.Deep);
            Scribe_Values.Look(ref this.nextStockId, "nextStockId", 1);
            Scribe_Values.Look(ref this.nextOperationId, "nextOperationId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                this.RebuildCaches();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            this.RebuildCaches();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            this.tickCounter++;

            if (this.cellsDirty)
            {
                this.RefreshAllCells();
                this.cellsDirty = false;
            }

            if (this.tickCounter % 250 == 0)
            {
                this.PruneOperations();
            }
        }

        public int CreateStock(StockMetadata metadata)
        {
            StockRecord record = new StockRecord();
            record.StockId = this.nextStockId++;
            if (metadata != null)
            {
                record.Metadata = metadata;
            }

            record.NeedsRefresh = true;
            this.stocks.Add(record);
            this.cellsDirty = true;
            return record.StockId;
        }

        public bool TryGetStock(int stockId, out StockRecord record)
        {
            for (int i = 0; i < this.stocks.Count; i++)
            {
                StockRecord candidate = this.stocks[i];
                if (candidate != null && candidate.StockId == stockId)
                {
                    record = candidate;
                    return true;
                }
            }

            record = null;
            return false;
        }

        public void RegisterParent(int stockId, ISlotGroupParent parent)
        {
            if (parent == null || parent.Map != this.map)
            {
                return;
            }

            StockRecord stock;
            if (!this.TryGetStock(stockId, out stock))
            {
                return;
            }

            if (!stock.ContainsParent(parent))
            {
                stock.AddParent(parent);
            }

            this.parentToStock[parent] = stock.StockId;
            stock.NeedsRefresh = true;
            this.cellsDirty = true;
        }

        public void UnregisterParent(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            int stockId;
            if (!this.parentToStock.TryGetValue(parent, out stockId))
            {
                return;
            }

            this.parentToStock.Remove(parent);

            StockRecord stock;
            if (!this.TryGetStock(stockId, out stock))
            {
                return;
            }

            if (stock.RemoveParent(parent))
            {
                stock.NeedsRefresh = true;
                this.cellsDirty = true;
            }
        }

        public void NotifyParentCellsChanged(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            int stockId;
            if (!this.parentToStock.TryGetValue(parent, out stockId))
            {
                return;
            }

            StockRecord stock;
            if (!this.TryGetStock(stockId, out stock))
            {
                return;
            }

            stock.NeedsRefresh = true;
            this.cellsDirty = true;
        }

        public int GetStockOfCell(IntVec3 cell)
        {
            if (!cell.IsValid || this.map?.haulDestinationManager == null)
            {
                return StockConstants.ColonyStockId;
            }

            SlotGroup slotGroup = this.map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup == null)
            {
                return StockConstants.ColonyStockId;
            }

            if (slotGroup.parent != null && this.parentToStock.TryGetValue(slotGroup.parent, out int stockId))
            {
                return stockId;
            }

            return StockConstants.ColonyStockId;
        }

        public int GetStockOfThing(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map != this.map)
            {
                return StockConstants.ColonyStockId;
            }

            return this.GetStockOfCell(thing.Position);
        }

        public bool IsCellInStock(IntVec3 cell, int stockId)
        {
            return this.GetStockOfCell(cell) == stockId;
        }

        public IEnumerable<Thing> GetPendingThings()
        {
            for (int i = 0; i < this.activeOperations.Count; i++)
            {
                StockTransferOperation operation = this.activeOperations[i];
                if (operation == null || operation.Completed)
                {
                    continue;
                }

                for (int j = 0; j < operation.Transfers.Count; j++)
                {
                    StockTransferRequest request = operation.Transfers[j];
                    if (request.RemainingCount <= 0)
                    {
                        continue;
                    }

                    if (request.SourceThing == null || !request.SourceThing.Spawned || request.SourceThing.Map != this.map)
                    {
                        continue;
                    }

                    yield return request.SourceThing;
                }
            }
        }

        public bool IsThingQueued(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }

            return this.IsThingAlreadyQueued(thing);
        }

        private void RebuildCaches()
        {
            this.parentToStock.Clear();

            for (int i = 0; i < this.stocks.Count; i++)
            {
                StockRecord stock = this.stocks[i];
                if (stock == null)
                {
                    continue;
                }

                stock.RebuildParentCache();
                for (int p = stock.Parents.Count - 1; p >= 0; p--)
                {
                    ISlotGroupParent parent = stock.Parents[p];
                    if (parent == null)
                    {
                        continue;
                    }

                    if (parent.Map != this.map)
                    {
                        continue;
                    }

                    this.parentToStock[parent] = stock.StockId;
                }

                stock.NeedsRefresh = true;
            }

            this.cellsDirty = true;
        }

        private void RefreshAllCells()
        {
            for (int i = 0; i < this.stocks.Count; i++)
            {
                StockRecord stock = this.stocks[i];
                if (stock == null)
                {
                    continue;
                }

                this.RefreshStockCells(stock);
            }
        }

        private void RefreshStockCells(StockRecord stock)
        {
            if (stock == null)
            {
                return;
            }

            stock.RebuildParentCache();
            stock.CachedCells.Clear();

            HashSet<IntVec3> uniqueCells = new HashSet<IntVec3>();

            for (int i = stock.Parents.Count - 1; i >= 0; i--)
            {
                ISlotGroupParent parent = stock.Parents[i];
                if (parent == null || parent.Map != this.map)
                {
                    stock.RemoveParent(parent);
                    continue;
                }

                List<IntVec3> slotCells = parent.AllSlotCellsList();
                for (int j = 0; j < slotCells.Count; j++)
                {
                    IntVec3 cell = slotCells[j];
                    if (uniqueCells.Add(cell))
                    {
                        stock.CachedCells.Add(cell);
                    }
                }
            }

            stock.NeedsRefresh = false;
        }

        public void ForceImmediateRefresh()
        {
            this.RefreshAllCells();
            this.cellsDirty = false;
        }

        public bool HasActiveOperations
        {
            get
            {
                for (int i = 0; i < this.activeOperations.Count; i++)
                {
                    StockTransferOperation operation = this.activeOperations[i];
                    if (operation != null && !operation.Completed)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool HasActiveOperationsForStock(int stockId)
        {
            for (int i = 0; i < this.activeOperations.Count; i++)
            {
                StockTransferOperation operation = this.activeOperations[i];
                if (operation == null || operation.Completed)
                {
                    continue;
                }

                if (operation.SourceStockId == stockId || operation.DestinationStockId == stockId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryStartTransferOperation(int destinationStockId, List<ThingCount> loads, List<ThingCount> unloads, out string failureReason)
        {
            failureReason = null;

            this.PruneOperations();

            bool hasLoads = loads != null && loads.Count > 0;
            bool hasUnloads = unloads != null && unloads.Count > 0;

            if (!hasLoads && !hasUnloads)
            {
                failureReason = "DeepGravload_Error_NoItemsSelected".Translate();
                return false;
            }

            StockRecord destination;
            if (destinationStockId != StockConstants.ColonyStockId)
            {
                if (!this.TryEnsureStockReady(destinationStockId, out destination))
                {
                    failureReason = "DeepGravload_Error_NoManagedCells".Translate();
                    return false;
                }

                if (destination.CachedCells.Count == 0)
                {
                    failureReason = "DeepGravload_Error_NoManagedCells".Translate();
                    return false;
                }
            }
            else
            {
                destination = null;
            }

            StockTransferOperation operation = new StockTransferOperation();
            operation.Id = this.nextOperationId++;
            operation.SourceStockId = StockConstants.ColonyStockId;
            operation.DestinationStockId = destinationStockId;

            List<StockTransferRequest> loadRequests = new List<StockTransferRequest>();

            if (hasLoads)
            {
                for (int i = 0; i < loads.Count; i++)
                {
                    ThingCount selection = loads[i];
                    if (selection.Thing == null || !selection.Thing.Spawned || selection.Count <= 0)
                    {
                        continue;
                    }

                    if (selection.Thing.Map != this.map)
                    {
                        failureReason = "DeepGravload_Error_ItemOffMap".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    if (this.GetStockOfThing(selection.Thing) != StockConstants.ColonyStockId)
                    {
                        failureReason = "DeepGravload_Error_ItemAlreadyManaged".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    if (this.IsThingAlreadyQueued(selection.Thing))
                    {
                        failureReason = "DeepGravload_Error_ItemAlreadyQueued".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    StockTransferRequest request = new StockTransferRequest();
                    request.SourceThing = selection.Thing;
                    int count = selection.Count;
                    if (count > selection.Thing.stackCount)
                    {
                        count = selection.Thing.stackCount;
                    }

                    request.TotalCount = count;
                    request.ToDestinationStock = true;
                    loadRequests.Add(request);
                    operation.Transfers.Add(request);
                }

                if (loadRequests.Count > 0 && destinationStockId != StockConstants.ColonyStockId)
                {
                    if (!this.ValidateCapacity(destinationStockId, loadRequests))
                    {
                        failureReason = "DeepGravload_Error_NotEnoughSpace".Translate();
                        return false;
                    }
                }
            }

            if (hasUnloads)
            {
                for (int i = 0; i < unloads.Count; i++)
                {
                    ThingCount selection = unloads[i];
                    if (selection.Thing == null || !selection.Thing.Spawned || selection.Count <= 0)
                    {
                        continue;
                    }

                    if (selection.Thing.Map != this.map)
                    {
                        failureReason = "DeepGravload_Error_ItemOffMap".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    if (this.GetStockOfThing(selection.Thing) != destinationStockId)
                    {
                        failureReason = "DeepGravload_Error_ItemAlreadyManaged".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    if (this.IsThingAlreadyQueued(selection.Thing))
                    {
                        failureReason = "DeepGravload_Error_ItemAlreadyQueued".Translate(selection.Thing.LabelCap);
                        return false;
                    }

                    StockTransferRequest request = new StockTransferRequest();
                    request.SourceThing = selection.Thing;
                    int count = selection.Count;
                    if (count > selection.Thing.stackCount)
                    {
                        count = selection.Thing.stackCount;
                    }

                    request.TotalCount = count;
                    request.ToDestinationStock = false;
                    operation.Transfers.Add(request);
                }
            }

            if (operation.Transfers.Count == 0)
            {
                failureReason = "DeepGravload_Error_NoValidItems".Translate();
                return false;
            }

            this.activeOperations.Add(operation);
            return true;
        }

        public bool TryAssignHaulJob(Pawn pawn, Thing thing, bool forced, out StockJobTicket ticket)
        {
            SeparateStockContext.PushHaulAllowance();
            try
            {
                return this.TryPrepareJob(pawn, thing, forced, reserve: true, out ticket);
            }
            finally
            {
                SeparateStockContext.PopHaulAllowance();
            }
        }

        public bool CanHandleThing(Pawn pawn, Thing thing, bool forced)
        {
            SeparateStockContext.PushHaulAllowance();
            try
            {
                StockJobTicket ticket;
                return this.TryPrepareJob(pawn, thing, forced, reserve: false, out ticket);
            }
            finally
            {
                SeparateStockContext.PopHaulAllowance();
            }
        }

        public StockJobTicket GetTicket(Pawn pawn)
        {
            StockJobTicket ticket;
            this.activeTickets.TryGetValue(pawn, out ticket);
            return ticket;
        }

        public void ReleaseTicket(Pawn pawn, bool succeeded, int deliveredAmount)
        {
            if (pawn == null)
            {
                return;
            }

            StockJobTicket ticket;
            if (!this.activeTickets.TryGetValue(pawn, out ticket))
            {
                return;
            }

            this.activeTickets.Remove(pawn);

            StockTransferOperation operation = ticket.Operation;
            StockTransferRequest request = ticket.Request;
            if (operation == null || request == null)
            {
                return;
            }

            request.ReservedCount -= ticket.Count;
            if (request.ReservedCount < 0)
            {
                request.ReservedCount = 0;
            }

            operation.ReleaseReservation(ticket.DestinationCell, ticket.Count);

            if (succeeded)
            {
                request.LoadedCount += deliveredAmount;
                if (request.LoadedCount > request.TotalCount)
                {
                    request.LoadedCount = request.TotalCount;
                }
            }

            operation.PruneInvalidTransfers();
            if (operation.Completed)
            {
                this.activeOperations.Remove(operation);
            }
        }

        public void ClearTicketsForPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            this.activeTickets.Remove(pawn);
        }

        public void CancelOperationsForStock(int stockId)
        {
            for (int i = this.activeOperations.Count - 1; i >= 0; i--)
            {
                StockTransferOperation operation = this.activeOperations[i];
                if (operation.SourceStockId == stockId || operation.DestinationStockId == stockId)
                {
                    this.activeOperations.RemoveAt(i);
                }
            }

            this.activeTickets.Clear();
        }

        private bool TryPrepareJob(Pawn pawn, Thing thing, bool forced, bool reserve, out StockJobTicket ticket)
        {
            ticket = null;

            if (pawn == null || thing == null)
            {
                return false;
            }

            StockTransferOperation operation;
            StockTransferRequest request;
            if (!this.TryGetRequest(thing, out operation, out request))
            {
                return false;
            }

            if (request.RemainingCount <= 0)
            {
                return false;
            }

            if (!forced && request.ToDestinationStock && thing.IsForbidden(pawn))
            {
                return false;
            }

            if (!ReservationUtility.CanReserve(pawn, thing, 1, -1, null, forced))
            {
                return false;
            }

            int desired = request.RemainingCount;
            if (desired > thing.stackCount)
            {
                desired = thing.stackCount;
            }

            if (desired <= 0)
            {
                return false;
            }

            if (request.ToDestinationStock)
            {
                IntVec3 cell;
                int capacity;
                if (!this.TryFindDestination(operation, thing, desired, out cell, out capacity))
                {
                    return false;
                }

                if (capacity <= 0)
                {
                    return false;
                }

                if (capacity < desired)
                {
                    desired = capacity;
                }

                if (!ReservationUtility.CanReserve(pawn, new LocalTargetInfo(cell), 1, -1, null, forced))
                {
                    return false;
                }

                ticket = new StockJobTicket
                {
                    Operation = operation,
                    Request = request,
                    SourceThing = thing,
                    DestinationCell = cell,
                    Count = desired,
                    ToDestinationStock = true
                };

                if (reserve)
                {
                    request.ReservedCount += desired;
                    operation.AddReservation(cell, desired);
                    this.activeTickets[pawn] = ticket;
                }

                return true;
            }
            else
            {
                StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
                IntVec3 storeCell;
                SeparateStockContext.PushCrossStockSearch();
                bool found;
                try
                {
                    found = StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, this.map, currentPriority, pawn.Faction, out storeCell);
                }
                finally
                {
                    SeparateStockContext.PopCrossStockSearch();
                }

                if (!found)
                {
                    return false;
                }

                if (this.GetStockOfCell(storeCell) != StockConstants.ColonyStockId)
                {
                    if (!this.TryFindColonyStoreCell(thing, pawn, currentPriority, desired, out storeCell))
                    {
                        return false;
                    }
                }

                int availableSpace = storeCell.GetItemStackSpaceLeftFor(this.map, thing.def);
                if (availableSpace <= 0)
                {
                    return false;
                }

                if (availableSpace < desired)
                {
                    desired = availableSpace;
                }

                if (desired <= 0)
                {
                    return false;
                }

                if (!ReservationUtility.CanReserve(pawn, new LocalTargetInfo(storeCell), 1, -1, null, forced))
                {
                    return false;
                }

                ticket = new StockJobTicket
                {
                    Operation = operation,
                    Request = request,
                    SourceThing = thing,
                    DestinationCell = storeCell,
                    Count = desired,
                    ToDestinationStock = false
                };

                if (reserve)
                {
                    request.ReservedCount += desired;
                    operation.AddReservation(storeCell, desired);
                    this.activeTickets[pawn] = ticket;
                }

                return true;
            }
        }

        private bool TryGetRequest(Thing thing, out StockTransferOperation operation, out StockTransferRequest request)
        {
            operation = null;
            request = null;

            for (int i = 0; i < this.activeOperations.Count; i++)
            {
                StockTransferOperation op = this.activeOperations[i];
                if (op == null || op.Completed)
                {
                    continue;
                }

                for (int j = 0; j < op.Transfers.Count; j++)
                {
                    StockTransferRequest candidate = op.Transfers[j];
                    if (candidate.SourceThing == thing && candidate.RemainingCount > 0)
                    {
                        operation = op;
                        request = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryFindDestination(StockTransferOperation operation, Thing thing, int desiredCount, out IntVec3 cell, out int count)
        {
            cell = IntVec3.Invalid;
            count = 0;

            if (operation == null || thing == null)
            {
                return false;
            }

            return this.TryFindStockCellWithCapacity(operation.DestinationStockId, thing, desiredCount, operation, null, out cell, out count);
        }

        private bool ValidateCapacity(int stockId, List<StockTransferRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return true;
            }

            Dictionary<IntVec3, int> simulated = new Dictionary<IntVec3, int>();

            for (int i = 0; i < requests.Count; i++)
            {
                StockTransferRequest request = requests[i];
                if (request.SourceThing == null || request.TotalCount <= 0)
                {
                    continue;
                }

                int remaining = request.TotalCount;
                while (remaining > 0)
                {
                    IntVec3 cell;
                    int count;
                    if (!this.TryFindStockCellWithCapacity(stockId, request.SourceThing, remaining, null, simulated, out cell, out count))
                    {
                        return false;
                    }

                    if (count <= 0)
                    {
                        return false;
                    }

                    remaining -= count;

                    int stored;
                    if (simulated.TryGetValue(cell, out stored))
                    {
                        simulated[cell] = stored + count;
                    }
                    else
                    {
                        simulated.Add(cell, count);
                    }
                }
            }

            return true;
        }

        private bool TryFindColonyStoreCell(Thing thing, Pawn pawn, StoragePriority currentPriority, int desiredCount, out IntVec3 storeCell)
        {
            storeCell = IntVec3.Invalid;
            if (this.map?.haulDestinationManager == null)
            {
                return false;
            }

            List<SlotGroup> groups = this.map.haulDestinationManager.AllGroupsListInPriorityOrder;
            if (groups == null || groups.Count == 0)
            {
                return false;
            }

            IntVec3 origin = thing.Spawned ? thing.Position : (pawn != null ? pawn.Position : IntVec3.Invalid);
            StoragePriority bestPriority = StoragePriority.Unstored;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < groups.Count; i++)
            {
                SlotGroup group = groups[i];
                StoragePriority priority = group.Settings.Priority;

                if (this.parentToStock.ContainsKey(group.parent))
                {
                    continue;
                }

                if (!group.parent.HaulDestinationEnabled || !group.Settings.AllowedToAccept(thing))
                {
                    continue;
                }

                List<IntVec3> cells = group.CellsList;
                for (int j = 0; j < cells.Count; j++)
                {
                    IntVec3 cell = cells[j];
                    if (!StoreUtility.IsGoodStoreCell(cell, this.map, thing, pawn, pawn?.Faction ?? Faction.OfPlayer))
                    {
                        continue;
                    }

                    int space = cell.GetItemStackSpaceLeftFor(this.map, thing.def);
                    if (space <= 0)
                    {
                        continue;
                    }

                    float distance = origin.IsValid ? (origin - cell).LengthHorizontalSquared : 0f;

                    if (!storeCell.IsValid || (int)priority > (int)bestPriority || ((int)priority == (int)bestPriority && distance < bestDistance))
                    {
                        storeCell = cell;
                        bestPriority = priority;
                        bestDistance = distance;
                    }
                }
            }

            return storeCell.IsValid;
        }

        private bool TryFindStockCellWithCapacity(int stockId, Thing thing, int desiredCount, StockTransferOperation operation, Dictionary<IntVec3, int> simulatedReservations, out IntVec3 cell, out int count)
        {
            cell = IntVec3.Invalid;
            count = 0;

            StockRecord stock;
            if (!this.TryEnsureStockReady(stockId, out stock) || stock == null)
            {
                return false;
            }

            for (int i = 0; i < stock.CachedCells.Count; i++)
            {
                IntVec3 slot = stock.CachedCells[i];
                if (!slot.InBounds(this.map))
                {
                    continue;
                }

                if (!StoreUtility.IsGoodStoreCell(slot, this.map, thing, null, Faction.OfPlayer))
                {
                    continue;
                }

                int capacity = slot.GetItemStackSpaceLeftFor(this.map, thing.def);
                if (capacity <= 0)
                {
                    continue;
                }

                int reserved = 0;
                if (operation != null)
                {
                    reserved += operation.GetReserved(slot);
                }

                if (simulatedReservations != null && simulatedReservations.TryGetValue(slot, out int simulated))
                {
                    reserved += simulated;
                }

                capacity -= reserved;
                if (capacity <= 0)
                {
                    continue;
                }

                cell = slot;
                count = Math.Min(capacity, desiredCount);
                return true;
            }

            return false;
        }

        private bool IsThingAlreadyQueued(Thing thing)
        {
            for (int i = 0; i < this.activeOperations.Count; i++)
            {
                StockTransferOperation operation = this.activeOperations[i];
                if (operation == null || operation.Completed)
                {
                    continue;
                }

                for (int j = 0; j < operation.Transfers.Count; j++)
                {
                    StockTransferRequest request = operation.Transfers[j];
                    if (request.SourceThing == thing && request.RemainingCount > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryEnsureStockReady(int stockId, out StockRecord stock)
        {
            stock = null;

            if (stockId == StockConstants.ColonyStockId)
            {
                return true;
            }

            if (this.cellsDirty)
            {
                this.RefreshAllCells();
                this.cellsDirty = false;
            }

            if (!this.TryGetStock(stockId, out stock))
            {
                return false;
            }

            if (stock.NeedsRefresh)
            {
                this.RefreshStockCells(stock);
            }

            return true;
        }

        private void PruneOperations()
        {
            for (int i = this.activeOperations.Count - 1; i >= 0; i--)
            {
                StockTransferOperation operation = this.activeOperations[i];
                if (operation == null)
                {
                    this.activeOperations.RemoveAt(i);
                    continue;
                }

                operation.PruneInvalidTransfers();
                if (operation.Completed)
                {
                    this.activeOperations.RemoveAt(i);
                }
            }

            this.CleanInactiveTickets();
        }

        private void CleanInactiveTickets()
        {
            this.tmpPawns.Clear();
            foreach (KeyValuePair<Pawn, StockJobTicket> pair in this.activeTickets)
            {
                StockJobTicket ticket = pair.Value;
                if (ticket == null || ticket.Operation == null || ticket.Operation.Completed)
                {
                    this.tmpPawns.Add(pair.Key);
                }
            }

            for (int i = 0; i < this.tmpPawns.Count; i++)
            {
                this.activeTickets.Remove(this.tmpPawns[i]);
            }

            this.tmpPawns.Clear();
        }
    }
}

