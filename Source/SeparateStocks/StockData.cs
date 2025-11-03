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
        public List<RimWorld.ISlotGroupParent> Parents = new List<RimWorld.ISlotGroupParent>();
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.StockId, "stockId");
            Scribe_Deep.Look(ref this.Metadata, "metadata");
            Scribe_Collections.Look(ref this.Parents, "parents", LookMode.Reference);
            Scribe_Collections.Look(ref this.CachedCells, "cachedCells", LookMode.Value);
            Scribe_Values.Look(ref this.NeedsRefresh, "needsRefresh");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (this.Metadata == null)
                {
                    this.Metadata = new StockMetadata();
                }

                if (this.Parents == null)
                {
                    this.Parents = new List<RimWorld.ISlotGroupParent>();
                }

                if (this.CachedCells == null)
                {
                    this.CachedCells = new List<IntVec3>();
                }
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
