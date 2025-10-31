using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Deep_Gravload
{
    public class GravloadMapComponent : MapComponent
    {
        private List<ManagedBuildingRecord> buildingRecords = new List<ManagedBuildingRecord>();
        private List<ManagedZoneRecord> zoneRecords = new List<ManagedZoneRecord>();

        private readonly Dictionary<IntVec3, int> managedCellRefCounts = new Dictionary<IntVec3, int>();
        private readonly Dictionary<Thing, bool> trackedForbiddenStates = new Dictionary<Thing, bool>();

        private readonly List<IntVec3> tmpCells = new List<IntVec3>();
        private readonly List<Thing> tmpThings = new List<Thing>();

        private int tickCounter;

        public GravloadMapComponent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref this.buildingRecords, "buildingRecords", LookMode.Deep);
            Scribe_Collections.Look(ref this.zoneRecords, "zoneRecords", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                this.RebuildManagedState();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            this.RebuildManagedState();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            this.tickCounter++;
            if (this.tickCounter % 120 != 0)
            {
                return;
            }

            this.RefreshManagedItems();
        }

        public bool IsManaged(Building_Storage storage)
        {
            if (storage == null)
            {
                return false;
            }

            ManagedBuildingRecord record = this.FindBuildingRecord(storage);
            if (record == null)
            {
                return false;
            }

            return record.IsManaged;
        }

        public bool IsManaged(Zone_Stockpile zone)
        {
            if (zone == null)
            {
                return false;
            }

            ManagedZoneRecord record = this.FindZoneRecord(zone);
            if (record == null)
            {
                return false;
            }

            return record.IsManaged;
        }

        public void ToggleBuilding(Building_Storage storage)
        {
            if (storage == null)
            {
                return;
            }

            if (storage.Map != this.map)
            {
                return;
            }

            ManagedBuildingRecord record = this.GetOrCreateBuildingRecord(storage);
            if (record.IsManaged)
            {
                this.DisableBuilding(record);
            }
            else
            {
                this.EnableBuilding(record);
            }
        }

        public void ToggleZone(Zone_Stockpile zone)
        {
            if (zone == null)
            {
                return;
            }

            if (zone.Map != this.map)
            {
                return;
            }

            ManagedZoneRecord record = this.GetOrCreateZoneRecord(zone);
            if (record.IsManaged)
            {
                this.DisableZone(record);
            }
            else
            {
                this.EnableZone(record);
            }
        }

        public void OnStoredThingReceived(ISlotGroupParent parent, Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            if (!thing.Spawned)
            {
                return;
            }

            if (thing.Faction != Faction.OfPlayer)
            {
                return;
            }

            bool managed = false;
            Building_Storage building = parent as Building_Storage;
            if (building != null)
            {
                managed = this.IsManaged(building);
            }
            else
            {
                Zone_Stockpile zone = parent as Zone_Stockpile;
                if (zone != null)
                {
                    managed = this.IsManaged(zone);
                }
            }

            if (!managed)
            {
                return;
            }

            this.EnsureThingForbidden(thing);
        }

        public void OnStoredThingLost(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            bool originalState;
            if (!this.trackedForbiddenStates.TryGetValue(thing, out originalState))
            {
                return;
            }

            if (!thing.Destroyed)
            {
                ForbidUtility.SetForbidden(thing, originalState, false);
            }

            this.trackedForbiddenStates.Remove(thing);
        }

        private void EnableBuilding(ManagedBuildingRecord record)
        {
            Building_Storage storage = record.Storage;
            if (storage == null)
            {
                return;
            }

            StorageSettings settings = storage.GetStoreSettings();
            if (settings == null)
            {
                return;
            }

            if (record.SavedSettings == null)
            {
                record.SavedSettings = new StoredStorageSettings();
            }

            record.SavedSettings.Capture(settings);
            this.ApplyDisallowAll(settings, storage);

            record.IsManaged = true;

            List<IntVec3> cells = storage.AllSlotCellsList();
            this.AddManagedCells(cells);
            this.EnsureCellsForbidden(cells);
        }

        private void DisableBuilding(ManagedBuildingRecord record)
        {
            Building_Storage storage = record.Storage;
            if (storage == null)
            {
                return;
            }

            record.IsManaged = false;

            StorageSettings settings = storage.GetStoreSettings();
            if (settings != null && record.SavedSettings != null)
            {
                record.SavedSettings.ApplyTo(settings);
            }

            storage.Notify_SettingsChanged();

            List<IntVec3> cells = storage.AllSlotCellsList();
            this.RemoveManagedCells(cells);
            this.RestoreForbiddenStates(cells);
        }

        private void EnableZone(ManagedZoneRecord record)
        {
            Zone_Stockpile zone = record.Zone;
            if (zone == null)
            {
                return;
            }

            StorageSettings settings = zone.GetStoreSettings();
            if (settings == null)
            {
                return;
            }

            if (record.SavedSettings == null)
            {
                record.SavedSettings = new StoredStorageSettings();
            }

            record.SavedSettings.Capture(settings);
            this.ApplyDisallowAll(settings, zone);

            record.IsManaged = true;

            List<IntVec3> cells = zone.AllSlotCellsList();
            this.AddManagedCells(cells);
            this.EnsureCellsForbidden(cells);
        }

        private void DisableZone(ManagedZoneRecord record)
        {
            Zone_Stockpile zone = record.Zone;
            if (zone == null)
            {
                return;
            }

            record.IsManaged = false;

            StorageSettings settings = zone.GetStoreSettings();
            if (settings != null && record.SavedSettings != null)
            {
                record.SavedSettings.ApplyTo(settings);
            }

            zone.Notify_SettingsChanged();

            List<IntVec3> cells = zone.AllSlotCellsList();
            this.RemoveManagedCells(cells);
            this.RestoreForbiddenStates(cells);
        }

        private void ApplyDisallowAll(StorageSettings settings, IStoreSettingsParent parent)
        {
            if (settings == null)
            {
                return;
            }

            settings.filter.SetDisallowAll(null, null);
            if (parent != null)
            {
                parent.Notify_SettingsChanged();
            }
        }

        private void AddManagedCells(List<IntVec3> cells)
        {
            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                int count;
                if (this.managedCellRefCounts.TryGetValue(cell, out count))
                {
                    this.managedCellRefCounts[cell] = count + 1;
                }
                else
                {
                    this.managedCellRefCounts.Add(cell, 1);
                }
            }
        }

        private void RemoveManagedCells(List<IntVec3> cells)
        {
            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                int count;
                if (!this.managedCellRefCounts.TryGetValue(cell, out count))
                {
                    continue;
                }

                count--;
                if (count <= 0)
                {
                    this.managedCellRefCounts.Remove(cell);
                }
                else
                {
                    this.managedCellRefCounts[cell] = count;
                }
            }
        }

        private void EnsureCellsForbidden(List<IntVec3> cells)
        {
            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                List<Thing> thingList = cell.GetThingList(this.map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    Thing thing = thingList[j];
                    if (!thing.Spawned)
                    {
                        continue;
                    }

                    if (thing.Faction != Faction.OfPlayer)
                    {
                        continue;
                    }

                    if (!thing.def.EverStorable(false))
                    {
                        continue;
                    }

                    this.EnsureThingForbidden(thing);
                }
            }
        }

        private void RestoreForbiddenStates(List<IntVec3> cells)
        {
            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                List<Thing> thingList = cell.GetThingList(this.map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    Thing thing = thingList[j];
                    this.RestoreThingForbidden(thing);
                }
            }
        }

        private void RefreshManagedItems()
        {
            if (this.managedCellRefCounts.Count == 0)
            {
                return;
            }

            // TODO deep: Tie managed stock counts into the resource readout when the feature is ready.
            this.tmpCells.Clear();
            foreach (KeyValuePair<IntVec3, int> pair in this.managedCellRefCounts)
            {
                this.tmpCells.Add(pair.Key);
            }

            for (int i = 0; i < this.tmpCells.Count; i++)
            {
                IntVec3 cell = this.tmpCells[i];
                if (!cell.InBounds(this.map))
                {
                    continue;
                }

                List<Thing> thingList = cell.GetThingList(this.map);
                for (int j = 0; j < thingList.Count; j++)
                {
                    Thing thing = thingList[j];
                    if (!thing.Spawned)
                    {
                        continue;
                    }

                    if (thing.Faction != Faction.OfPlayer)
                    {
                        continue;
                    }

                    if (!thing.def.EverStorable(false))
                    {
                        continue;
                    }

                    this.EnsureThingForbidden(thing);
                }
            }
        }

        private void EnsureThingForbidden(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            bool original;
            if (!this.trackedForbiddenStates.TryGetValue(thing, out original))
            {
                original = ForbidUtility.IsForbidden(thing, Faction.OfPlayer);
                this.trackedForbiddenStates.Add(thing, original);
            }

            if (!ForbidUtility.IsForbidden(thing, Faction.OfPlayer))
            {
                ForbidUtility.SetForbidden(thing, true, false);
            }
        }

        private void RestoreThingForbidden(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            bool original;
            if (!this.trackedForbiddenStates.TryGetValue(thing, out original))
            {
                return;
            }

            if (!thing.Destroyed)
            {
                ForbidUtility.SetForbidden(thing, original, false);
            }

            this.trackedForbiddenStates.Remove(thing);
        }

        private ManagedBuildingRecord GetOrCreateBuildingRecord(Building_Storage storage)
        {
            ManagedBuildingRecord record = this.FindBuildingRecord(storage);
            if (record != null)
            {
                return record;
            }

            record = new ManagedBuildingRecord(storage);
            this.buildingRecords.Add(record);
            return record;
        }

        private ManagedZoneRecord GetOrCreateZoneRecord(Zone_Stockpile zone)
        {
            ManagedZoneRecord record = this.FindZoneRecord(zone);
            if (record != null)
            {
                return record;
            }

            record = new ManagedZoneRecord(zone);
            this.zoneRecords.Add(record);
            return record;
        }

        private ManagedBuildingRecord FindBuildingRecord(Building_Storage storage)
        {
            if (storage == null)
            {
                return null;
            }

            for (int i = 0; i < this.buildingRecords.Count; i++)
            {
                ManagedBuildingRecord record = this.buildingRecords[i];
                if (record.Storage == storage)
                {
                    return record;
                }
            }

            return null;
        }

        private ManagedZoneRecord FindZoneRecord(Zone_Stockpile zone)
        {
            if (zone == null)
            {
                return null;
            }

            for (int i = 0; i < this.zoneRecords.Count; i++)
            {
                ManagedZoneRecord record = this.zoneRecords[i];
                if (record.Zone == zone)
                {
                    return record;
                }
            }

            return null;
        }

        private void RebuildManagedState()
        {
            this.managedCellRefCounts.Clear();
            this.trackedForbiddenStates.Clear();

            for (int i = this.buildingRecords.Count - 1; i >= 0; i--)
            {
                ManagedBuildingRecord record = this.buildingRecords[i];
                if (record.Storage == null || record.Storage.Map != this.map)
                {
                    this.buildingRecords.RemoveAt(i);
                    continue;
                }

                if (!record.IsManaged)
                {
                    continue;
                }

                StorageSettings settings = record.Storage.GetStoreSettings();
                if (settings != null)
                {
                    this.ApplyDisallowAll(settings, record.Storage);
                }

                List<IntVec3> cells = record.Storage.AllSlotCellsList();
                this.AddManagedCells(cells);
                this.EnsureCellsForbidden(cells);
            }

            for (int j = this.zoneRecords.Count - 1; j >= 0; j--)
            {
                ManagedZoneRecord record2 = this.zoneRecords[j];
                if (record2.Zone == null || record2.Zone.Map != this.map)
                {
                    this.zoneRecords.RemoveAt(j);
                    continue;
                }

                if (!record2.IsManaged)
                {
                    continue;
                }

                StorageSettings settings2 = record2.Zone.GetStoreSettings();
                if (settings2 != null)
                {
                    this.ApplyDisallowAll(settings2, record2.Zone);
                }

                List<IntVec3> cells2 = record2.Zone.AllSlotCellsList();
                this.AddManagedCells(cells2);
                this.EnsureCellsForbidden(cells2);
            }
        }
    }

    public class ManagedBuildingRecord : IExposable
    {
        public Building_Storage Storage;
        public bool IsManaged;
        public StoredStorageSettings SavedSettings;

        public ManagedBuildingRecord()
        {
        }

        public ManagedBuildingRecord(Building_Storage storage)
        {
            this.Storage = storage;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref this.Storage, "storage");
            Scribe_Values.Look(ref this.IsManaged, "isManaged", false);
            Scribe_Deep.Look(ref this.SavedSettings, "savedSettings");
        }
    }

    public class ManagedZoneRecord : IExposable
    {
        public Zone_Stockpile Zone;
        public bool IsManaged;
        public StoredStorageSettings SavedSettings;

        public ManagedZoneRecord()
        {
        }

        public ManagedZoneRecord(Zone_Stockpile zone)
        {
            this.Zone = zone;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref this.Zone, "zone");
            Scribe_Values.Look(ref this.IsManaged, "isManaged", false);
            Scribe_Deep.Look(ref this.SavedSettings, "savedSettings");
        }
    }

    public class StoredStorageSettings : IExposable
    {
        public StoragePriority Priority;
        public ThingFilter Filter = new ThingFilter();

        public void Capture(StorageSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            this.Priority = settings.Priority;
            if (this.Filter == null)
            {
                this.Filter = new ThingFilter();
            }

            this.Filter.CopyAllowancesFrom(settings.filter);
        }

        public void ApplyTo(StorageSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.Priority = this.Priority;
            if (this.Filter != null)
            {
                settings.filter.CopyAllowancesFrom(this.Filter);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.Priority, "priority", StoragePriority.Unstored);
            Scribe_Deep.Look(ref this.Filter, "filter");
        }
    }
}
