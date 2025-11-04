using System.Collections.Generic;
using RimWorld;
using SeparateStocks;
using Verse;
using Verse.AI;

namespace Deep_Gravload
{
    public class GravloadMapComponent : MapComponent
    {
        private StockManagerComponent stockManager;
        private readonly Dictionary<Building_GravEngine, int> engineToStockId = new Dictionary<Building_GravEngine, int>();
        private readonly Dictionary<int, Building_GravEngine> stockIdToEngine = new Dictionary<int, Building_GravEngine>();
        private readonly HashSet<ISlotGroupParent> managedParents = new HashSet<ISlotGroupParent>();
        private readonly Dictionary<ISlotGroupParent, StoredStorageSettings> savedSettings = new Dictionary<ISlotGroupParent, StoredStorageSettings>();
        private List<ManagedParentState> savedSettingsState = new List<ManagedParentState>();
        private readonly HashSet<Building_Storage> pendingManagedBuildings = new HashSet<Building_Storage>();
        private readonly List<Building_Storage> tmpPendingBuildings = new List<Building_Storage>();
        private readonly Dictionary<Thing, OverlayHandle?> overlayHandles = new Dictionary<Thing, OverlayHandle?>();

        public GravloadMapComponent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                this.SerializeManagedParents();
            }

            Scribe_Collections.Look(ref this.savedSettingsState, "managedParentSettings", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                this.ResolveStockManager();
                this.RestoreSavedSettings();
                this.RebuildMappings();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            this.ResolveStockManager();
            this.RestoreSavedSettings();
            this.RebuildMappings();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (this.ProcessPendingManagedBuildings())
            {
                this.stockManager?.ForceImmediateRefresh();
                this.NotifyCargoWindowsDirty();
            }
        }

        public void NotifyManagedBuildingSpawned(Building_Storage storage)
        {
            if (storage == null || storage.Map != this.map)
            {
                return;
            }

            if (!this.pendingManagedBuildings.Contains(storage))
            {
                return;
            }

            if (this.ProcessPendingManagedBuildings())
            {
                this.stockManager?.ForceImmediateRefresh();
                this.NotifyCargoWindowsDirty();
            }
        }

        public bool IsManaged(Building_Storage storage)
        {
            return storage != null && this.managedParents.Contains(storage);
        }

        public bool IsManaged(Zone_Stockpile zone)
        {
            return zone != null && this.managedParents.Contains(zone);
        }

        public void ToggleBuilding(Building_Storage storage)
        {
            if (storage == null || storage.Map != this.map)
            {
                return;
            }

            this.ToggleParent(storage);
        }

        public void ToggleZone(Zone_Stockpile zone)
        {
            if (zone == null || zone.Map != this.map)
            {
                return;
            }

            this.ToggleParent(zone);
        }

        public void NotifyZoneCellsChanged(Zone_Stockpile zone)
        {
            if (zone == null || zone.Map != this.map || !this.managedParents.Contains(zone))
            {
                return;
            }

            this.ResolveStockManager();
            this.stockManager?.NotifyParentCellsChanged(zone);
        }

        public void NotifyParentDestroyed(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            if (!this.managedParents.Contains(parent))
            {
                return;
            }

            this.ResolveStockManager();
            this.ClearOverlayForParent(parent);
            this.stockManager?.UnregisterParent(parent);
            this.managedParents.Remove(parent);

            if (parent is Building_Storage destroyedBuilding)
            {
                this.pendingManagedBuildings.Remove(destroyedBuilding);
            }

            this.savedSettings.Remove(parent);
        }

        public void OnStoredThingReceived(ISlotGroupParent parent, Thing thing)
        {
            this.NotifyCargoWindowsDirty();
            if (thing == null)
            {
                return;
            }

            if (parent != null && this.managedParents.Contains(parent))
            {
                this.EnableOverlay(thing);
            }
        }

        public void OnStoredThingLost(Thing thing)
        {
            this.NotifyCargoWindowsDirty();
            if (thing == null)
            {
                return;
            }

            this.DisableOverlay(thing);
        }

        public void NotifyItemPlacedInManagedCell(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            this.EnableOverlay(thing);
        }

        public bool IsThingInManagedCell(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }

            this.ResolveStockManager();
            int stockId = this.stockManager?.GetStockOfThing(thing) ?? StockConstants.ColonyStockId;
            return stockId != StockConstants.ColonyStockId;
        }

        public bool IsManagedCell(IntVec3 cell)
        {
            this.ResolveStockManager();
            int stockId = this.stockManager?.GetStockOfCell(cell) ?? StockConstants.ColonyStockId;
            return stockId != StockConstants.ColonyStockId;
        }

        public bool TryGetEngineForThing(Thing thing, out Building_GravEngine engine)
        {
            engine = null;
            if (thing == null)
            {
                return false;
            }

            this.ResolveStockManager();
            int stockId = this.stockManager?.GetStockOfThing(thing) ?? StockConstants.ColonyStockId;
            if (stockId == StockConstants.ColonyStockId)
            {
                return false;
            }

            return this.stockIdToEngine.TryGetValue(stockId, out engine) && engine != null && engine.Map == this.map;
        }

        public bool HasActiveOperations => this.stockManager != null && this.stockManager.HasActiveOperations;

        public bool HasActiveOperationsForEngine(Building_GravEngine engine)
        {
            if (engine == null)
            {
                return false;
            }

            if (!this.TryGetStockIdForEngine(engine, out int stockId))
            {
                return false;
            }

            return this.stockManager != null && this.stockManager.HasActiveOperationsForStock(stockId);
        }

        public IEnumerable<Thing> GetPendingThings()
        {
            if (this.stockManager == null)
            {
                yield break;
            }

            foreach (Thing thing in this.stockManager.GetPendingThings())
            {
                yield return thing;
            }
        }

        public bool CanHandleThing(Pawn pawn, Thing thing, bool forced)
        {
            return this.stockManager != null && this.stockManager.CanHandleThing(pawn, thing, forced);
        }

        public bool TryAssignHaulJob(Pawn pawn, Thing thing, bool forced, out StockJobTicket ticket)
        {
            ticket = null;
            return this.stockManager != null && this.stockManager.TryAssignHaulJob(pawn, thing, forced, out ticket);
        }

        public StockJobTicket GetTicket(Pawn pawn)
        {
            return this.stockManager?.GetTicket(pawn);
        }

        public void ReleaseTicket(Pawn pawn, bool succeeded, int deliveredAmount)
        {
            this.stockManager?.ReleaseTicket(pawn, succeeded, deliveredAmount);
        }

        public void ClearTicketsForPawn(Pawn pawn)
        {
            this.stockManager?.ClearTicketsForPawn(pawn);
        }

        public bool TryStartLoadOperation(Building_GravEngine engine, List<ThingCount> loadSelections, List<ThingCount> unloadSelections, out string failureReason)
        {
            failureReason = null;
            if (engine == null)
            {
                failureReason = "DeepGravload_Error_NoEngine".Translate();
                return false;
            }

            if (!this.TryEnsureStockForEngine(engine, out int stockId))
            {
                failureReason = "DeepGravload_Error_NoManagedCells".Translate();
                return false;
            }

            if (!this.HasManagedCellsForEngine(engine))
            {
                failureReason = "DeepGravload_Error_NoManagedCells".Translate();
                return false;
            }

            if (this.stockManager != null && this.stockManager.TryStartTransferOperation(stockId, loadSelections, unloadSelections, out failureReason))
            {
                this.NotifyCargoWindowsDirty();
                return true;
            }

            return false;
        }

        public bool HasManagedCellsForEngine(Building_GravEngine engine)
        {
            if (engine == null || this.stockManager == null)
            {
                return false;
            }

            if (!this.TryEnsureStockForEngine(engine, out int stockId))
            {
                return false;
            }

            StockRecord stock;
            if (!this.stockManager.TryGetStock(stockId, out stock) || stock == null)
            {
                return false;
            }

            return stock.CachedCells.Count > 0;
        }

        public void CancelOperationsForEngine(Building_GravEngine engine)
        {
            if (engine == null || this.stockManager == null)
            {
                return;
            }

            if (this.TryGetStockIdForEngine(engine, out int stockId))
            {
                this.stockManager.CancelOperationsForStock(stockId);
                this.NotifyCargoWindowsDirty();
            }
        }

        private void ToggleParent(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            if (this.managedParents.Contains(parent))
            {
                this.DisableParent(parent);
            }
            else
            {
                this.EnableParent(parent);
            }
        }

        private void EnableParent(ISlotGroupParent parent)
        {
            if (parent == null || parent.Map != this.map)
            {
                return;
            }

            Building_GravEngine engine;
            if (!ManagedStorageUtility.TryFindEngineForParent(parent, this.map, out engine))
            {
                Messages.Message("DeepGravload_CommandLoadCargoNoStorage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!this.TryEnsureStockForEngine(engine, out int stockId))
            {
                Messages.Message("DeepGravload_CommandLoadCargoNoStorage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            StorageSettings settings = parent.GetStoreSettings();
            if (settings != null)
            {
                StoredStorageSettings snapshot = new StoredStorageSettings();
                snapshot.Capture(settings);
                this.savedSettings[parent] = snapshot;
            }

            this.stockManager.RegisterParent(stockId, parent);
            this.managedParents.Add(parent);

            if (parent is Building_Storage building)
            {
                this.pendingManagedBuildings.Remove(building);
            }

            this.stockManager?.ForceImmediateRefresh();
            this.ApplyOverlayForParent(parent);
            this.NotifyCargoWindowsDirty();
        }

        private void DisableParent(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            this.ClearOverlayForParent(parent);
            this.stockManager?.UnregisterParent(parent);
            this.managedParents.Remove(parent);

            if (parent is Building_Storage building)
            {
                this.pendingManagedBuildings.Remove(building);
            }

            StorageSettings settings = parent.GetStoreSettings();
            if (settings != null && this.savedSettings.TryGetValue(parent, out StoredStorageSettings snapshot) && snapshot != null)
            {
                snapshot.ApplyTo(settings);
                ManagedStorageUtility.NotifyParentSettingsChanged(parent);
            }

            this.savedSettings.Remove(parent);
            this.stockManager?.ForceImmediateRefresh();
            this.NotifyCargoWindowsDirty();
        }

        private void SerializeManagedParents()
        {
            this.savedSettingsState.Clear();

            List<ISlotGroupParent> parents = new List<ISlotGroupParent>(this.managedParents);
            foreach (Building_Storage building in this.pendingManagedBuildings)
            {
                if (building != null && !parents.Contains(building))
                {
                    parents.Add(building);
                }
            }

            for (int i = 0; i < parents.Count; i++)
            {
                ISlotGroupParent parent = parents[i];
                if (parent == null)
                {
                    continue;
                }

                ManagedParentState state = new ManagedParentState();
                if (parent is Building_Storage buildingParent)
                {
                    state.Building = buildingParent;
                }
                else if (parent is Zone_Stockpile zoneParent)
                {
                    state.Zone = zoneParent;
                }
                else
                {
                    continue;
                }

                StoredStorageSettings snapshot;
                if (this.savedSettings.TryGetValue(parent, out snapshot))
                {
                    state.Settings = snapshot;
                }

                this.savedSettingsState.Add(state);
            }
        }

        private void RestoreSavedSettings()
        {
            this.savedSettings.Clear();
            if (this.savedSettingsState == null)
            {
                this.savedSettingsState = new List<ManagedParentState>();
            }

            for (int i = 0; i < this.savedSettingsState.Count; i++)
            {
                ManagedParentState state = this.savedSettingsState[i];
                ISlotGroupParent parent = state?.ResolveParent();
                if (parent != null && state.Settings != null)
                {
                    this.savedSettings[parent] = state.Settings;
                }
            }

            this.NotifyCargoWindowsDirty();
        }

        private void ResolveStockManager()
        {
            if (this.stockManager != null)
            {
                return;
            }

            this.stockManager = this.map.GetComponent<StockManagerComponent>();
            if (this.stockManager == null)
            {
                this.stockManager = new StockManagerComponent(this.map);
                this.map.components.Add(this.stockManager);
                this.stockManager.FinalizeInit();
            }
        }

        private void RebuildMappings()
        {
            this.engineToStockId.Clear();
            this.stockIdToEngine.Clear();
            this.managedParents.Clear();
            this.pendingManagedBuildings.Clear();

            if (this.stockManager == null)
            {
                return;
            }

            IReadOnlyList<StockRecord> stocks = this.stockManager.Stocks;
            for (int i = 0; i < stocks.Count; i++)
            {
                StockRecord stock = stocks[i];
                if (stock == null || stock.StockId == StockConstants.ColonyStockId)
                {
                    continue;
                }

                Building_GravEngine engine = stock.Metadata?.OwnerThing as Building_GravEngine;
                if (engine == null || engine.Map != this.map)
                {
                    continue;
                }

                this.engineToStockId[engine] = stock.StockId;
                this.stockIdToEngine[stock.StockId] = engine;

                for (int p = 0; p < stock.Parents.Count; p++)
                {
                    ISlotGroupParent parent = stock.Parents[p];
                    if (parent == null)
                    {
                        continue;
                    }

                    if (parent.Map != this.map)
                    {
                        if (parent is Building_Storage pendingBuilding)
                        {
                            this.pendingManagedBuildings.Add(pendingBuilding);
                        }

                        continue;
                    }

                    this.managedParents.Add(parent);
                }
            }

            if (this.ProcessPendingManagedBuildings())
            {
                this.NotifyCargoWindowsDirty();
            }
        }

        private bool ProcessPendingManagedBuildings()
        {
            if (this.pendingManagedBuildings.Count == 0 || this.stockManager == null)
            {
                return false;
            }

            bool processed = false;
            this.tmpPendingBuildings.Clear();

            foreach (Building_Storage building in this.pendingManagedBuildings)
            {
                if (building == null || building.Destroyed || building.Map != this.map)
                {
                    this.tmpPendingBuildings.Add(building);
                    continue;
                }

                if (!building.Spawned)
                {
                    continue;
                }

                Building_GravEngine engine;
                if (!ManagedStorageUtility.TryFindEngineForParent(building, this.map, out engine))
                {
                    continue;
                }

                if (!this.TryEnsureStockForEngine(engine, out int stockId))
                {
                    continue;
                }

                this.stockManager.RegisterParent(stockId, building);
                this.managedParents.Add(building);
                this.ApplyOverlayForParent(building);
                this.tmpPendingBuildings.Add(building);
                processed = true;
            }

            for (int i = 0; i < this.tmpPendingBuildings.Count; i++)
            {
                Building_Storage building = this.tmpPendingBuildings[i];
                this.pendingManagedBuildings.Remove(building);
            }

            this.tmpPendingBuildings.Clear();
            return processed;
        }

        private void ApplyOverlayForParent(ISlotGroupParent parent)
        {
            if (parent == null || this.map == null)
            {
                return;
            }

            this.ResolveStockManager();
            if (this.stockManager == null)
            {
                return;
            }

            List<IntVec3> cells = parent.AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(this.map);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    if (thing == null || thing.def.category != ThingCategory.Item)
                    {
                        continue;
                    }

                    if (this.stockManager.GetStockOfThing(thing) != StockConstants.ColonyStockId)
                    {
                        this.EnableOverlay(thing);
                    }
                }
            }
        }

        private void ClearOverlayForParent(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            List<IntVec3> cells = parent.AllSlotCellsList();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(this.map);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing thing = things[j];
                    if (thing == null || thing.def.category != ThingCategory.Item)
                    {
                        continue;
                    }

                    this.DisableOverlay(thing);
                }
            }
        }

        private void EnableOverlay(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map != this.map || thing.def.category != ThingCategory.Item)
            {
                return;
            }

            this.ResolveStockManager();
            if (this.stockManager == null)
            {
                return;
            }

            if (this.stockManager.GetStockOfThing(thing) == StockConstants.ColonyStockId)
            {
                this.DisableOverlay(thing);
                return;
            }

            OverlayHandle? existing;
            if (this.overlayHandles.TryGetValue(thing, out existing) && existing.HasValue)
            {
                this.map.overlayDrawer.Disable(thing, ref existing);
            }

            OverlayHandle overlayHandle = this.map.overlayDrawer.Enable(thing, OverlayTypes.QuestionMark);
            this.overlayHandles[thing] = overlayHandle;
        }

        private void DisableOverlay(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            if (!this.overlayHandles.TryGetValue(thing, out OverlayHandle? handle))
            {
                return;
            }

            if (handle.HasValue)
            {
                if (thing.Spawned && thing.Map == this.map)
                {
                    this.map.overlayDrawer.Disable(thing, ref handle);
                }
                else
                {
                    this.map.overlayDrawer.DisposeHandle(thing);
                }
            }

            this.overlayHandles.Remove(thing);
        }

        private bool TryEnsureStockForEngine(Building_GravEngine engine, out int stockId)
        {
            stockId = StockConstants.ColonyStockId;
            if (engine == null)
            {
                return false;
            }

            if (this.engineToStockId.TryGetValue(engine, out stockId))
            {
                return true;
            }

            this.ResolveStockManager();
            if (this.stockManager == null)
            {
                return false;
            }

            StockRecord existing = this.FindStockForEngine(engine);
            if (existing == null)
            {
                StockMetadata metadata = new StockMetadata
                {
                    OwnerThing = engine,
                    Label = engine.LabelCap
                };
                stockId = this.stockManager.CreateStock(metadata);
                if (this.stockManager.TryGetStock(stockId, out StockRecord created) && created != null)
                {
                    created.Metadata.OwnerThing = engine;
                }
            }
            else
            {
                stockId = existing.StockId;
                existing.Metadata.OwnerThing = engine;
            }

            this.engineToStockId[engine] = stockId;
            this.stockIdToEngine[stockId] = engine;
            return true;
        }

        private bool TryGetStockIdForEngine(Building_GravEngine engine, out int stockId)
        {
            stockId = StockConstants.ColonyStockId;
            if (engine == null)
            {
                return false;
            }

            if (this.engineToStockId.TryGetValue(engine, out stockId))
            {
                return true;
            }

            StockRecord stock = this.FindStockForEngine(engine);
            if (stock == null)
            {
                return false;
            }

            stockId = stock.StockId;
            this.engineToStockId[engine] = stockId;
            this.stockIdToEngine[stockId] = engine;
            return true;
        }

        private StockRecord FindStockForEngine(Building_GravEngine engine)
        {
            if (engine == null || this.stockManager == null)
            {
                return null;
            }

            IReadOnlyList<StockRecord> stocks = this.stockManager.Stocks;
            for (int i = 0; i < stocks.Count; i++)
            {
                StockRecord candidate = stocks[i];
                if (candidate == null || candidate.StockId == StockConstants.ColonyStockId)
                {
                    continue;
                }

                if (candidate.Metadata?.OwnerThing == engine)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void NotifyCargoWindowsDirty()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            Dialog_GravloadCargo dialog = Find.WindowStack.WindowOfType<Dialog_GravloadCargo>();
            dialog?.MarkDirty();
        }

        private class ManagedParentState : IExposable
        {
            public Building_Storage Building;
            public Zone_Stockpile Zone;
            public StoredStorageSettings Settings;

            public void ExposeData()
            {
                Scribe_References.Look(ref this.Building, "building");
                Scribe_References.Look(ref this.Zone, "zone");
                Scribe_Deep.Look(ref this.Settings, "settings");
            }

            public ISlotGroupParent ResolveParent()
            {
                if (this.Building != null)
                {
                    return this.Building;
                }

                if (this.Zone != null)
                {
                    return this.Zone;
                }

                return null;
            }
        }
    }
}
