using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Deep_Gravload.SeparateStocks.Patches
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class FloatMenuMakerMapPatch
    {
        public static void Prefix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context)
        {
            SeparateStockUtility.PushPlayerFloatMenu();
        }

        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            SeparateStockUtility.PopPlayerFloatMenu();
        }
    }
}
