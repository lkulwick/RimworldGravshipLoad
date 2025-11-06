using HarmonyLib;
using Verse;

namespace Deep_Gravload.SeparateStocks
{
    public sealed class SeparateStockMod : Mod
    {
        public const string HarmonyId = "Deep.Gravload.SeparateStocks";

        public SeparateStockMod(ModContentPack content)
            : base(content)
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            SeparateStockLog.Message("Separate stock mod bootstrap complete.");
        }
    }
}
