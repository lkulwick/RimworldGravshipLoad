using System.Collections.Generic;
using Verse;

namespace SeparateStocks
{
    public class StockTransferRequest : IExposable
    {
        public Thing SourceThing;
        public int TotalCount;
        public int LoadedCount;
        public int ReservedCount;
        public bool ToDestinationStock;

        public int RemainingCount
        {
            get
            {
                int remaining = this.TotalCount - this.LoadedCount - this.ReservedCount;
                if (remaining < 0)
                {
                    remaining = 0;
                }

                return remaining;
            }
        }

        public bool Completed => this.LoadedCount >= this.TotalCount;

        public void ExposeData()
        {
            Scribe_References.Look(ref this.SourceThing, "sourceThing");
            Scribe_Values.Look(ref this.TotalCount, "totalCount");
            Scribe_Values.Look(ref this.LoadedCount, "loadedCount");
            Scribe_Values.Look(ref this.ReservedCount, "reservedCount");
            Scribe_Values.Look(ref this.ToDestinationStock, "toDestinationStock");
        }
    }

    public class StockCellReservation : IExposable
    {
        public IntVec3 Cell;
        public int ReservedCount;

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.Cell, "cell");
            Scribe_Values.Look(ref this.ReservedCount, "reservedCount");
        }
    }

    public class StockTransferOperation : IExposable
    {
        public int Id;
        public int SourceStockId;
        public int DestinationStockId;
        public List<StockTransferRequest> Transfers = new List<StockTransferRequest>();
        public List<StockCellReservation> Reservations = new List<StockCellReservation>();

        public bool Completed
        {
            get
            {
                for (int i = 0; i < this.Transfers.Count; i++)
                {
                    if (!this.Transfers[i].Completed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void AddReservation(IntVec3 cell, int count)
        {
            if (count <= 0)
            {
                return;
            }

            StockCellReservation reservation = this.FindReservation(cell);
            if (reservation == null)
            {
                reservation = new StockCellReservation();
                reservation.Cell = cell;
                this.Reservations.Add(reservation);
            }

            reservation.ReservedCount += count;
        }

        public void ReleaseReservation(IntVec3 cell, int count)
        {
            StockCellReservation reservation = this.FindReservation(cell);
            if (reservation == null)
            {
                return;
            }

            reservation.ReservedCount -= count;
            if (reservation.ReservedCount <= 0)
            {
                this.Reservations.Remove(reservation);
            }
        }

        public int GetReserved(IntVec3 cell)
        {
            StockCellReservation reservation = this.FindReservation(cell);
            if (reservation == null)
            {
                return 0;
            }

            return reservation.ReservedCount;
        }

        public void PruneInvalidTransfers()
        {
            for (int i = this.Transfers.Count - 1; i >= 0; i--)
            {
                StockTransferRequest request = this.Transfers[i];
                if (request.SourceThing == null || !request.SourceThing.Spawned)
                {
                    request.LoadedCount = request.TotalCount;
                    request.ReservedCount = 0;
                }

                if (request.Completed)
                {
                    this.Transfers.RemoveAt(i);
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref this.Id, "id");
            Scribe_Values.Look(ref this.SourceStockId, "sourceStockId");
            Scribe_Values.Look(ref this.DestinationStockId, "destinationStockId");
            Scribe_Collections.Look(ref this.Transfers, "transfers", LookMode.Deep);
            Scribe_Collections.Look(ref this.Reservations, "reservations", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (this.Transfers == null)
                {
                    this.Transfers = new List<StockTransferRequest>();
                }

                if (this.Reservations == null)
                {
                    this.Reservations = new List<StockCellReservation>();
                }
            }
        }

        private StockCellReservation FindReservation(IntVec3 cell)
        {
            for (int i = 0; i < this.Reservations.Count; i++)
            {
                StockCellReservation reservation = this.Reservations[i];
                if (reservation.Cell == cell)
                {
                    return reservation;
                }
            }

            return null;
        }
    }

    public class StockJobTicket
    {
        public StockTransferOperation Operation;
        public StockTransferRequest Request;
        public Thing SourceThing;
        public IntVec3 DestinationCell;
        public int Count;
        public bool ToDestinationStock;
    }
}
