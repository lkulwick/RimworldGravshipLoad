using RimWorld;
using Verse;

namespace Deep_Gravload.SeparateStocks
{
    [DefOf]
    public static class SeparateStockDefOf
    {
        public static JobDef SeparateStockTransfer;

        public static WorkGiverDef SeparateStockTransferer;

        static SeparateStockDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SeparateStockDefOf));
        }
    }
}
