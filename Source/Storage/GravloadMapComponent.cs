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

        public GravloadMapComponent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                this.savedSettingsState.Clear();
                foreach (KeyValuePair<ISlotGroupParent, StoredStorageSettings> pair in this.savedSettings)
                {
                    ISlotGroupParent parent = pair.Key;
                    StoredStorageSettings settings = pair.Value;
                    if (parent == null || settings == null)
                    {
                        continue;
                    }

                    ManagedParentState state = new ManagedParentState
                    {
                        Settings = settings
                    };

                    if (parent is Building_Storage building)
                    {
                        state.Building = building;
                    }
                    else if (parent is Zone_Stockpile zone)
                    {
                        state.Zone = zone;
                    }
                    else
                    {
                        continue;
                    }

                    this.savedSettingsState.Add(state);
                }
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

            this.managedParents.Remove(parent);
            this.savedSettings.Remove(parent);
            this.stockManager?.UnregisterParent(parent);
        }

        public void OnStoredThingReceived(ISlotGroupParent parent, Thing thing)
        {
            // No additional handling required; pawn hauling to managed stock is governed by SeparateStocks policies.
        }

        public void OnStoredThingLost(Thing thing)
        {
            // No-op; state is tracked by stock manager and transfer operations.
        }

        public void NotifyItemPlacedInManagedCell(Thing thing)
        {
            // No-op; forbidding is no longer required with SeparateStocks gating.
        }

        public bool IsThingInManagedCell(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }

            int stockId = this.stockManager?.GetStockOfThing(thing) ?? StockConstants.ColonyStockId;
            return stockId != StockConstants.ColonyStockId;
        }

        public bool IsManagedCell(IntVec3 cell)
        {
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

            int stockId;
            if (!this.TryGetStockIdForEngine(engine, out stockId))
            {
                return false;
            }

            return this.stockManager.HasActiveOperationsForStock(stockId);
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

            if (this.stockManager.HasActiveOperationsForStock(stockId))
            {
                failureReason = "DeepGravload_Error_LoadInProgress".Translate();
                return false;
            }

            if (this.stockManager.TryStartTransferOperation(stockId, loadSelections, unloadSelections, out failureReason))
            {
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

            StockRecord record;
            if (!this.stockManager.TryGetStock(stockId, out record) || record == null)
            {
                return false;
            }

            return record.CachedCells.Count > 0;
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
            if (parent == null)
            {
                return;
            }

            Map parentMap = parent.Map;
            if (parentMap != this.map)
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
        }

        private void DisableParent(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            this.stockManager.UnregisterParent(parent);
            this.managedParents.Remove(parent);

            StorageSettings settings = parent.GetStoreSettings();
            if (settings != null && this.savedSettings.TryGetValue(parent, out StoredStorageSettings snapshot) && snapshot != null)
            {
                snapshot.ApplyTo(settings);
                ManagedStorageUtility.NotifyParentSettingsChanged(parent);
            }

            this.savedSettings.Remove(parent);
        }

        private bool TryEnsureStockForEngine(Building_GravEngine engine, out int stockId)
        {
            stockId = StockConstants.ColonyStockId;
            if (engine == null)
            {
                return false;
            }

            if (this.TryGetStockIdForEngine(engine, out stockId))
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
            }
            else
            {
                stockId = existing.StockId;
            }

            this.engineToStockId[engine] = stockId;
            this.stockIdToEngine[stockId] = engine;

            if (existing == null && this.stockManager.TryGetStock(stockId, out StockRecord created))
            {
                created.Metadata.OwnerThing = engine;
            }

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

            StockRecord record = this.FindStockForEngine(engine);
            if (record == null)
            {
                return false;
            }

            stockId = record.StockId;
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
                    if (parent != null)
                    {
                        this.managedParents.Add(parent);
                    }
                }
            }
        }

        private void RestoreSavedSettings()
        {
            this.savedSettings.Clear();
            if (this.savedSettingsState == null)
            {
                this.savedSettingsState = new List<ManagedParentState>();
                return;
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
