using RimWorld;
using Verse;

namespace SeparateStocks
{
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
            Scribe_Values.Look(ref this.Priority, "priority", StoragePriority.Unstored, true);
            Scribe_Deep.Look(ref this.Filter, "filter");
        }
    }
}
