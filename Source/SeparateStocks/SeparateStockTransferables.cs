using RimWorld;

namespace Deep_Gravload.SeparateStocks
{
    public sealed class TransferableOneWayReverse : TransferableOneWay
    {
        public override TransferablePositiveCountDirection PositiveCountDirection => TransferablePositiveCountDirection.Source;
    }
}
