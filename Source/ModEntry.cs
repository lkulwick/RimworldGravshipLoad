using HarmonyLib;
using Verse;

namespace Deep_Gravload
{
    public class GravloadMod : Mod
    {
        private const string HarmonyId = "deep.gravload";

        public GravloadMod(ModContentPack content) : base(content)
        {
            Harmony harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
        }
    }
}
