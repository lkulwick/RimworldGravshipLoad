using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload.SeparateStocks
{
public sealed class SeparateStockManager : MapComponent
{
    public const int ColonyStockId = 0;
    public const int SeparateStockId = 1;

    private Dictionary<IntVec3, int> _cellToStock = new Dictionary<IntVec3, int>();
    private List<SeparateStockTransferOperation> _operations = new List<SeparateStockTransferOperation>();

    private SeparateStockRecord _separateStock;

    public SeparateStockManager(Map map)
        : base(map)
    {
        _separateStock = new SeparateStockRecord(this);
    }

    public SeparateStockRecord SeparateStock => _separateStock;

    public IReadOnlyList<SeparateStockTransferOperation> Operations => _operations;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref _separateStock, "separateStock", this);
        Scribe_Collections.Look(ref _operations, "operations", LookMode.Deep, this);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (_operations != null)
            {
                foreach (var operation in _operations)
                {
                    operation?.SetManager(this);
                }
            }
            RebuildCaches();
        }
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        RebuildCaches();
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        if (Find.TickManager.TicksGame % 1800 == 0)
        {
            CleanupMembers();
        }
    }

    private void CleanupMembers()
    {
        if (_separateStock == null)
        {
            return;
        }

        _separateStock.CleanupDestroyedMembers();
    }

    private void RebuildCaches()
    {
        _cellToStock.Clear();
        if (_separateStock == null)
        {
            _separateStock = new SeparateStockRecord(this);
        }
        _separateStock.RebuildCells();
    }

    public void MarkCell(IntVec3 cell, int stockId, bool add)
    {
        if (add)
        {
            _cellToStock[cell] = stockId;
        }
        else
        {
            _cellToStock.Remove(cell);
        }
    }

    public bool CellBelongsToSeparateStock(IntVec3 cell)
    {
        return _cellToStock.TryGetValue(cell, out var stockId) && stockId == SeparateStockId;
    }

    public bool ThingInSeparateStock(Thing thing)
    {
        if (thing == null || thing.Destroyed)
        {
            return false;
        }

        if (thing.Spawned)
        {
            return CellBelongsToSeparateStock(thing.Position);
        }

        if (thing.ParentHolder is Thing holderThing && holderThing is ISlotGroupParent holderParent)
        {
            return _separateStock.ContainsParent(holderParent);
        }

        return false;
    }

    public bool ParentInSeparateStock(ISlotGroupParent parent)
    {
        return _separateStock.ContainsParent(parent);
    }

    public bool ToggleParentMembership(ISlotGroupParent parent)
    {
        if (parent == null)
        {
            return false;
        }

        if (_separateStock.ContainsParent(parent))
        {
            _separateStock.RemoveParent(parent);
            SeparateStockLog.Message($"{ParentLabel(parent)} removed from separate stock.");
            return false;
        }

        _separateStock.AddParent(parent);
        SeparateStockLog.Message($"{ParentLabel(parent)} added to separate stock.");
        return true;
    }

    public bool TryGetDropCellOutsideStock(IntVec3 origin, out IntVec3 dropCell)
    {
        var map = this.map;
        foreach (var cell in GenRadial.RadialCellsAround(origin, 25f, useCenter: true))
        {
            if (!cell.InBounds(map))
            {
                continue;
            }
            if (CellBelongsToSeparateStock(cell))
            {
                continue;
            }
            if (!cell.Standable(map))
            {
                continue;
            }
            if (map.haulDestinationManager.SlotGroupAt(cell) != null)
            {
                continue;
            }

            dropCell = cell;
            return true;
        }

        dropCell = IntVec3.Invalid;
        return false;
    }

    public bool ShouldBlockPawnUse(Pawn pawn, Thing thing)
    {
        if (pawn == null || thing == null)
        {
            return false;
        }

        if (!_separateStock.AllowPawnAutoUse && ThingInSeparateStock(thing))
        {
            var job = pawn.CurJob;
            if (job == null || !job.playerForced)
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<ISlotGroupParent> GetSeparateStockParents()
    {
        return _separateStock.Parents;
    }

    public SeparateStockTransferOperation CreateOperation(TransferDirection direction, List<TransferThing> requests)
    {
        if (requests == null || requests.Count == 0)
        {
            return null;
        }

        if (direction == TransferDirection.StockToColony)
        {
            PerformImmediateUnload(requests);
            return null;
        }

        var op = new SeparateStockTransferOperation(this, direction, requests);
        _operations.Add(op);
        SeparateStockLog.Message($"Created transfer operation {op.Id} ({direction}) with {requests.Count} entries.");
        return op;
    }

    public void RemoveOperation(SeparateStockTransferOperation operation)
    {
        if (_operations.Remove(operation))
        {
            SeparateStockLog.Message($"Removed transfer operation {operation.Id}.");
        }
    }

    public IEnumerable<SeparateStockTransferOperation> ActiveOperations => _operations;

    public IEnumerable<TransferThing> GetPendingTransferThings()
    {
        foreach (var op in _operations)
        {
            foreach (var transfer in op.PendingThings)
            {
                if (transfer != null && transfer.RemainingCount > 0 && !transfer.Reserved)
                {
                    yield return transfer;
                }
            }
        }
    }

    public bool TryGetTransferForThing(Thing thing, out TransferThing transfer)
    {
        return TryGetTransferForThing(thing, null, out transfer, includeReserved: true);
    }

    public bool TryGetTransferForThing(Thing thing, Pawn pawn, out TransferThing transfer, bool includeReserved = false)
    {
        transfer = null;
        if (thing == null)
        {
            return false;
        }

        foreach (var op in _operations)
        {
            foreach (var pending in op.PendingThings)
            {
                if (pending?.Thing != thing || pending.RemainingCount <= 0)
                {
                    continue;
                }

                if (pending.Reserved && (!includeReserved || pending.ReservedBy != pawn))
                {
                    continue;
                }

                transfer = pending;
                return true;
            }
        }

        return false;
    }

    public SeparateStockTransferOperation FindOperation(int id)
    {
        for (int i = 0; i < _operations.Count; i++)
        {
            if (_operations[i].Id == id)
            {
                return _operations[i];
            }
        }

        return null;
    }

    public void NotifyTransferCompleted(TransferThing transfer, int count)
    {
        if (transfer == null)
        {
            return;
        }

        var operation = FindOperation(transfer.OperationId);
        operation?.NotifyThingTransferred(transfer, count);
        ReleaseTransfer(transfer);
    }

    public void ReserveTransfer(TransferThing transfer, Pawn pawn)
    {
        if (transfer == null)
        {
            return;
        }

        transfer.Reserved = true;
        transfer.ReservedBy = pawn;
    }

    public void ReleaseTransfer(TransferThing transfer)
    {
        if (transfer == null)
        {
            return;
        }

        transfer.Reserved = false;
        transfer.ReservedBy = null;
    }

    public bool TryFindStorageCellForTransfer(Pawn pawn, Thing thing, out IntVec3 cell, out ISlotGroupParent parent)
    {
        cell = IntVec3.Invalid;
        parent = null;
        if (thing == null || pawn == null)
        {
            return false;
        }

        var map = this.map;
        var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group?.parent == null || !ParentInSeparateStock(group.parent))
            {
                continue;
            }

            if (!group.parent.HaulDestinationEnabled)
            {
                continue;
            }

            if (!group.Settings.AllowedToAccept(thing))
            {
                continue;
            }

            var cellsList = group.CellsList;
            for (int j = 0; j < cellsList.Count; j++)
            {
                var candidate = cellsList[j];
                if (!CellBelongsToSeparateStock(candidate))
                {
                    continue;
                }
                if (!StoreUtility.IsGoodStoreCell(candidate, map, thing, pawn, pawn.Faction))
                {
                    continue;
                }

                int stackSpace = candidate.GetItemStackSpaceLeftFor(map, thing.def);
                if (stackSpace <= 0)
                {
                    continue;
                }

                cell = candidate;
                parent = group.parent;
                return true;
            }
        }

        return false;
    }

    public bool TryFindStorageCellMatchingStock(Thing thing, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, bool requireSeparateStock, out IntVec3 cell)
    {
        cell = IntVec3.Invalid;
        if (thing == null || map == null)
        {
            return false;
        }

        var reference = carrier?.Position ?? thing.PositionHeld;
        var currentCell = thing.Spawned ? thing.PositionHeld : IntVec3.Invalid;
        var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
        StoragePriority bestPriority = StoragePriority.Unstored;
        float bestDistSquared = float.MaxValue;

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group?.parent == null)
            {
                continue;
            }

            if (ParentInSeparateStock(group.parent) != requireSeparateStock)
            {
                continue;
            }

            if (!group.parent.HaulDestinationEnabled)
            {
                continue;
            }

            var settings = group.Settings;
            var priority = settings.Priority;
            if ((int)priority <= (int)currentPriority)
            {
                break;
            }

            if (!settings.AllowedToAccept(thing))
            {
                continue;
            }

            var cellsList = group.CellsList;
            for (int j = 0; j < cellsList.Count; j++)
            {
                var candidate = cellsList[j];
                if (currentCell.IsValid && candidate == currentCell)
                {
                    continue;
                }
                if (!StoreUtility.IsGoodStoreCell(candidate, map, thing, carrier, faction))
                {
                    continue;
                }

                float dist = reference.IsValid ? candidate.DistanceToSquared(reference) : 0f;
                if (!cell.IsValid || (priority > bestPriority) || (priority == bestPriority && dist < bestDistSquared))
                {
                    cell = candidate;
                    bestPriority = priority;
                    bestDistSquared = dist;
                }
            }
        }

        return cell.IsValid;
    }

    public static SeparateStockManager TryGet(Map map)
    {
        if (map == null)
        {
            return null;
        }

        var existing = map.GetComponent<SeparateStockManager>();
        if (existing != null)
        {
            return existing;
        }

        var manager = new SeparateStockManager(map);
        map.components.Add(manager);
        try
        {
            manager.FinalizeInit();
        }
        catch (Exception ex)
        {
            SeparateStockLog.Error($"Failed to finalize SeparateStockManager: {ex}");
        }
        return manager;
    }

    private static string ParentLabel(ISlotGroupParent parent)
    {
        if (parent is Zone zone)
        {
            return zone.label;
        }

        if (parent is Thing thing)
        {
            return thing.LabelShortCap;
        }

        return parent.ToString();
    }

    private void PerformImmediateUnload(List<TransferThing> transfers)
    {
        if (transfers == null || map == null)
        {
            return;
        }

        bool anyDropped = false;
        bool warnedNoSpace = false;

        for (int i = 0; i < transfers.Count; i++)
        {
            var transfer = transfers[i];
            var thing = transfer?.Thing;
            if (thing == null || thing.MapHeld != map || !thing.Spawned)
            {
                continue;
            }

            int remaining = transfer.RemainingCount;
            while (remaining > 0 && thing.Spawned && thing.stackCount > 0)
            {
                int toMove = Mathf.Min(remaining, thing.stackCount);
                if (!TryGetDropCellOutsideStock(thing.Position, out var dropCell))
                {
                    if (!warnedNoSpace)
                    {
                        Messages.Message("SeparateStock_NoRoom".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                        warnedNoSpace = true;
                    }
                    break;
                }

                var split = thing.SplitOff(toMove);
                GenPlace.TryPlaceThing(split, dropCell, map, ThingPlaceMode.Near);
                remaining -= toMove;
                anyDropped = true;
            }

            transfer.RemainingCount = remaining;
        }

        if (anyDropped)
        {
            SeparateStockLog.Message("Instantly unloaded separate stock items.");
        }
    }
}
}

namespace Deep_Gravload.SeparateStocks
{
public enum TransferDirection : byte
{
    ColonyToStock,
    StockToColony
}

public sealed class TransferThing : IExposable
{
    public Thing Thing;
    public int RemainingCount;
    public TransferDirection Direction;
    public int OperationId;

    [Unsaved(false)]
    public bool Reserved;

    [Unsaved(false)]
    public Pawn ReservedBy;

    public TransferThing()
    {
    }

    public TransferThing(Thing thing, int count, TransferDirection direction, int operationId)
    {
        Thing = thing;
        RemainingCount = count;
        Direction = direction;
        OperationId = operationId;
    }

    public void ExposeData()
    {
        Scribe_References.Look(ref Thing, "thing");
        Scribe_Values.Look(ref RemainingCount, "remaining");
        Scribe_Values.Look(ref OperationId, "operationId");
        Scribe_Values.Look(ref Direction, "direction");
        if (Scribe.mode == LoadSaveMode.PostLoadInit && Thing == null)
        {
            RemainingCount = 0;
        }
    }
}

public sealed class SeparateStockTransferOperation : IExposable
{
    private static int _nextId = 1;

    private SeparateStockManager _manager;

    private List<TransferThing> _pendingThings = new List<TransferThing>();

    public TransferDirection Direction;

    public int Id;

    public SeparateStockTransferOperation()
    {
    }

    public SeparateStockTransferOperation(SeparateStockManager manager)
    {
        _manager = manager;
        Id = _nextId++;
    }

    public SeparateStockTransferOperation(SeparateStockManager manager, TransferDirection direction, List<TransferThing> entries)
    {
        _manager = manager;
        Direction = direction;
        Id = _nextId++;
        _pendingThings = entries ?? new List<TransferThing>();
        foreach (var pendingThing in _pendingThings)
        {
            pendingThing.OperationId = Id;
            pendingThing.Direction = direction;
        }
    }

    public IReadOnlyList<TransferThing> PendingThings => _pendingThings;

    public void NotifyThingTransferred(TransferThing entry, int count)
    {
        if (entry == null || count <= 0)
        {
            return;
        }

        entry.RemainingCount = Math.Max(0, entry.RemainingCount - count);
        if (entry.RemainingCount == 0)
        {
            SeparateStockLog.Message($"Operation {Id}: completed transfer of {entry.Thing?.LabelCap ?? "null"}.");
        }
        CheckCompletion();
    }

    public void NotifyThingDestroyed(Thing thing)
    {
        for (int i = 0; i < _pendingThings.Count; i++)
        {
            if (_pendingThings[i].Thing == thing)
            {
                SeparateStockLog.Warn($"Operation {Id}: pending item {thing.LabelCap} destroyed. Removing from queue.");
                _pendingThings[i].RemainingCount = 0;
            }
        }
        CheckCompletion();
    }

    public void CheckCompletion()
    {
        for (int i = _pendingThings.Count - 1; i >= 0; i--)
        {
            if (_pendingThings[i].RemainingCount <= 0 || _pendingThings[i].Thing == null || _pendingThings[i].Thing.Destroyed)
            {
                _pendingThings.RemoveAt(i);
            }
        }

        if (_pendingThings.Count == 0)
        {
            SeparateStockLog.Message($"Operation {Id} completed.");
            _manager?.RemoveOperation(this);
        }
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id", 0);
        Scribe_Values.Look(ref Direction, "direction");
        Scribe_Collections.Look(ref _pendingThings, "pendingThings", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
        {
            foreach (var pendingThing in _pendingThings)
            {
                if (pendingThing != null)
                {
                    pendingThing.OperationId = Id;
                    pendingThing.Direction = Direction;
                }
            }
        }
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (Id == 0)
            {
                Id = _nextId++;
            }
            else if (Id >= _nextId)
            {
                _nextId = Id + 1;
            }
            if (_manager == null)
            {
                SeparateStockLog.Warn($"Transfer operation {Id} missing manager reference after load.");
            }
        }
    }

    public void SetManager(SeparateStockManager manager)
    {
        _manager = manager;
    }
}

public sealed class SeparateStockRecord : IExposable
{
    private readonly SeparateStockManager _manager;

    private readonly HashSet<IntVec3> _cachedCells = new HashSet<IntVec3>();

    private List<int> _zoneIdsSerialized = new List<int>();
    private List<Zone_Stockpile> _zones = new List<Zone_Stockpile>();
    private List<Thing> _buildings = new List<Thing>();

    public bool AllowPawnAutoUse;

    public SeparateStockRecord(SeparateStockManager manager)
    {
        _manager = manager;
    }

    public IEnumerable<ISlotGroupParent> Parents
    {
        get
        {
            foreach (var zone in _zones)
            {
                if (zone != null)
                {
                    yield return zone;
                }
            }
            foreach (var building in _buildings)
            {
                if (building is ISlotGroupParent parent && building.Spawned)
                {
                    yield return parent;
                }
            }
        }
    }

    public bool ContainsParent(ISlotGroupParent parent)
    {
        if (parent is Zone_Stockpile zone)
        {
            return _zones.Contains(zone);
        }

        if (parent is Building_Storage building)
        {
            return _buildings.Contains(building);
        }

        return false;
    }

    public void AddParent(ISlotGroupParent parent)
    {
        if (parent == null)
        {
            return;
        }

        switch (parent)
        {
            case Zone_Stockpile zone when !_zones.Contains(zone):
                _zones.Add(zone);
                break;
            case Building_Storage building when !_buildings.Contains(building):
                _buildings.Add(building);
                break;
            default:
                return;
        }

        foreach (var cell in parent.AllSlotCells())
        {
            RegisterCell(cell);
        }
    }

    public void RemoveParent(ISlotGroupParent parent)
    {
        if (parent == null)
        {
            return;
        }

        bool removed = false;
        switch (parent)
        {
            case Zone_Stockpile zone:
                removed = _zones.Remove(zone);
                break;
            case Building_Storage building:
                removed = _buildings.Remove(building);
                break;
        }

        if (!removed)
        {
            return;
        }

        foreach (var cell in parent.AllSlotCells())
        {
            ForgetCell(cell);
        }
    }

    public void RegisterCell(IntVec3 cell)
    {
        if (_cachedCells.Add(cell))
        {
            _manager.MarkCell(cell, SeparateStockManager.SeparateStockId, add: true);
        }
    }

    public void ForgetCell(IntVec3 cell)
    {
        if (_cachedCells.Remove(cell))
        {
            _manager.MarkCell(cell, SeparateStockManager.SeparateStockId, add: false);
        }
    }

    public void RebuildCells()
    {
        _cachedCells.Clear();
        foreach (var parent in Parents)
        {
            foreach (var cell in parent.AllSlotCells())
            {
                RegisterCell(cell);
            }
        }
    }

    public void CleanupDestroyedMembers()
    {
        _zones.RemoveAll(z => z == null || z.Map != _manager.map);
        _buildings.RemoveAll(b =>
        {
            if (b == null || b.Destroyed)
            {
                return true;
            }

            var map = b.Map;
            return map != null && map != _manager.map;
        });
        RebuildCells();
    }

    public void ExposeData()
    {
        if (_zoneIdsSerialized == null)
        {
            _zoneIdsSerialized = new List<int>();
        }

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            _zoneIdsSerialized.Clear();
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i] != null)
                {
                    _zoneIdsSerialized.Add(_zones[i].ID);
                }
            }
        }

        Scribe_Collections.Look(ref _zoneIdsSerialized, "zoneIds", LookMode.Value);
        Scribe_Collections.Look(ref _buildings, "buildings", LookMode.Reference);
        Scribe_Values.Look(ref AllowPawnAutoUse, "allowPawnAutoUse", false);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (_zoneIdsSerialized == null)
            {
                _zoneIdsSerialized = new List<int>();
            }
            RestoreZonesFromIds();
            CleanupDestroyedMembers();
        }
    }

    private void RestoreZonesFromIds()
    {
        _zones.Clear();
        if (_zoneIdsSerialized == null || _manager?.map?.zoneManager == null)
        {
            return;
        }

        var allZones = _manager.map.zoneManager.AllZones;
        for (int i = 0; i < _zoneIdsSerialized.Count; i++)
        {
            int targetId = _zoneIdsSerialized[i];
            for (int j = 0; j < allZones.Count; j++)
            {
                if (allZones[j] is Zone_Stockpile stock && stock.ID == targetId)
                {
                    _zones.Add(stock);
                    break;
                }
            }
        }
    }
}
}
