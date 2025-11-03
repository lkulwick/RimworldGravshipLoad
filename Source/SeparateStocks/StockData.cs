using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SeparateStocks
{
    public static class StockConstants
    {
        public const int ColonyStockId = 0;
    }

    public class StockMetadata : IExposable
    {
        public string Label;
        public string Description;
        public bool AllowPawnAutoUse = true;
        public Thing OwnerThing;

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.Label, "label");
            Scribe_Values.Look(ref this.Description, "description");
            Scribe_Values.Look(ref this.AllowPawnAutoUse, "allowPawnAutoUse", true);
            Scribe_References.Look(ref this.OwnerThing, "ownerThing");
        }
    }

    public class StockRecord : IExposable
    {
        public int StockId;
        public StockMetadata Metadata = new StockMetadata();
        [Unsaved(false)]
        public readonly List<RimWorld.ISlotGroupParent> Parents = new List<RimWorld.ISlotGroupParent>();
        public List<Building_Storage> BuildingParents = new List<Building_Storage>();
        public List<Zone_Stockpile> ZoneParents = new List<Zone_Stockpile>();
        public List<IntVec3> CachedCells = new List<IntVec3>();
        public bool NeedsRefresh;

        public Map Map
        {
            get
            {
                for (int i = 0; i < this.Parents.Count; i++)
                {
                    RimWorld.ISlotGroupParent parent = this.Parents[i];
                    if (parent != null)
                    {
                        return parent.Map;
                    }
                }

                return null;
            }
        }

        public void AddParent(RimWorld.ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return;
            }

            if (parent is Building_Storage building)
            {
                if (this.BuildingParents == null)
                {
                    this.BuildingParents = new List<Building_Storage>();
                }

                if (!this.BuildingParents.Contains(building))
                {
                    this.BuildingParents.Add(building);
                }
            }
            else if (parent is Zone_Stockpile zone)
            {
                if (this.ZoneParents == null)
                {
                    this.ZoneParents = new List<Zone_Stockpile>();
                }

                if (!this.ZoneParents.Contains(zone))
                {
                    this.ZoneParents.Add(zone);
                }
            }

            this.RebuildParentCache();
        }

        public bool RemoveParent(RimWorld.ISlotGroupParent parent)
        {
            bool removed = false;

            if (parent is Building_Storage building && this.BuildingParents != null)
            {
                removed |= this.BuildingParents.Remove(building);
            }
            else if (parent is Zone_Stockpile zone && this.ZoneParents != null)
            {
                removed |= this.ZoneParents.Remove(zone);
            }

            if (removed)
            {
                this.RebuildParentCache();
            }

            return removed;
        }

        public bool ContainsParent(RimWorld.ISlotGroupParent parent)
        {
            if (parent is Building_Storage building)
            {
                return this.BuildingParents != null && this.BuildingParents.Contains(building);
            }

            if (parent is Zone_Stockpile zone)
            {
                return this.ZoneParents != null && this.ZoneParents.Contains(zone);
            }

            return false;
        }

        public void RebuildParentCache()
        {
            this.Parents.Clear();

            if (this.BuildingParents == null)
            {
                this.BuildingParents = new List<Building_Storage>();
            }

            if (this.ZoneParents == null)
            {
                this.ZoneParents = new List<Zone_Stockpile>();
            }

            for (int i = this.BuildingParents.Count - 1; i >= 0; i--)
            {
                Building_Storage building = this.BuildingParents[i];
                if (building == null)
                {
                    this.BuildingParents.RemoveAt(i);
                    continue;
                }

                this.Parents.Add(building);
            }

            for (int j = this.ZoneParents.Count - 1; j >= 0; j--)
            {
                Zone_Stockpile zone = this.ZoneParents[j];
                if (zone == null)
                {
                    this.ZoneParents.RemoveAt(j);
                    continue;
                }

                this.Parents.Add(zone);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.StockId, "stockId");
            Scribe_Deep.Look(ref this.Metadata, "metadata");
            Scribe_Collections.Look(ref this.BuildingParents, "buildingParents", LookMode.Reference);
            Scribe_Collections.Look(ref this.ZoneParents, "zoneParents", LookMode.Reference);
            Scribe_Collections.Look(ref this.CachedCells, "cachedCells", LookMode.Value);
            Scribe_Values.Look(ref this.NeedsRefresh, "needsRefresh");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (this.Metadata == null)
                {
                    this.Metadata = new StockMetadata();
                }

                if (this.BuildingParents == null)
                {
                    this.BuildingParents = new List<Building_Storage>();
                }

                if (this.ZoneParents == null)
                {
                    this.ZoneParents = new List<Zone_Stockpile>();
                }

                if (this.CachedCells == null)
                {
                    this.CachedCells = new List<IntVec3>();
                }

                this.RebuildParentCache();
            }
        }
    }

    public class StockCellInfo
    {
        public readonly IntVec3 Cell;
        public readonly StockRecord Stock;

        public StockCellInfo(IntVec3 cell, StockRecord stock)
        {
            this.Cell = cell;
            this.Stock = stock;
        }
    }
}
