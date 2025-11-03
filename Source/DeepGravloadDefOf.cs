using RimWorld;
using Verse;

namespace Deep_Gravload
{
    [DefOf]
    public static class DeepGravloadDefOf
    {
        static DeepGravloadDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DeepGravloadDefOf));
        }

        public static JobDef Deep_Gravload_LoadCargo;
    }
}
