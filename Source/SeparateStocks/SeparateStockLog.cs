using Verse;

namespace Deep_Gravload.SeparateStocks
{
    public static class SeparateStockLog
    {
        public static void Message(string text)
        {
            if (Prefs.DevMode)
            {
                Log.Message($"[SeparateStock] {text}");
            }
        }

        public static void Warn(string text)
        {
            Log.Warning($"[SeparateStock] {text}");
        }

        public static void Error(string text)
        {
            Log.Error($"[SeparateStock] {text}");
        }
    }
}
