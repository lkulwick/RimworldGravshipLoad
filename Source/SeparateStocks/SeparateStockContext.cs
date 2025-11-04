using System;

namespace SeparateStocks
{
    public static class SeparateStockContext
    {
        [ThreadStatic]
        private static int allowHaulDepth;

        [ThreadStatic]
        private static int allowCrossStockDepth;

        public static bool AllowSeparateStockHauling => allowHaulDepth > 0;

        public static bool AllowCrossStockSearch => allowCrossStockDepth > 0;

        public static void PushHaulAllowance()
        {
            allowHaulDepth++;
        }

        public static void PopHaulAllowance()
        {
            if (allowHaulDepth > 0)
            {
                allowHaulDepth--;
            }
        }

        public static void PushCrossStockSearch()
        {
            allowCrossStockDepth++;
        }

        public static void PopCrossStockSearch()
        {
            if (allowCrossStockDepth > 0)
            {
                allowCrossStockDepth--;
            }
        }
    }
}
