using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Deep_Gravload
{
    public static class ManagedStorageUtility
    {
        private static readonly List<IntVec3> ScratchCells = new List<IntVec3>();
        private static readonly List<Building_GravEngine> ScratchEngines = new List<Building_GravEngine>();

        public static GravloadMapComponent GetTracker(Map map)
        {
            if (map == null)
            {
                return null;
            }

            return map.GetComponent<GravloadMapComponent>();
        }

        public static bool ShouldOfferManagedToggle(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return false;
            }

            Map map = GetParentMap(parent);
            if (map == null)
            {
                return false;
            }

            if (!IsInsideGravship(parent, map))
            {
                return false;
            }

            Building_Storage building = parent as Building_Storage;
            if (building != null)
            {
                return building.Faction == Faction.OfPlayer;
            }

            Zone_Stockpile zone = parent as Zone_Stockpile;
            if (zone != null)
            {
                return map.IsPlayerHome;
            }

            return false;
        }

        public static bool IsInsideGravship(ISlotGroupParent parent)
        {
            if (parent == null)
            {
                return false;
            }

            Map map = GetParentMap(parent);
            if (map == null)
            {
                return false;
            }

            return IsInsideGravship(parent, map);
        }

        public static List<Gizmo> Materialize(IEnumerable<Gizmo> source)
        {
            List<Gizmo> list = new List<Gizmo>();
            if (source == null)
            {
                return list;
            }

            foreach (Gizmo gizmo in source)
            {
                list.Add(gizmo);
            }

            return list;
        }

        private static bool IsInsideGravship(ISlotGroupParent parent, Map map)
        {
            List<IntVec3> cells = GetSlotCells(parent);
            if (cells.Count == 0)
            {
                return false;
            }

            CollectGravEngines(map);
            for (int i = 0; i < ScratchEngines.Count; i++)
            {
                Building_GravEngine engine = ScratchEngines[i];
                if (engine == null)
                {
                    continue;
                }

                HashSet<IntVec3> substructure = engine.AllConnectedSubstructure;
                if (substructure == null || substructure.Count == 0)
                {
                    continue;
                }

                bool allInside = true;
                for (int j = 0; j < cells.Count; j++)
                {
                    if (!substructure.Contains(cells[j]))
                    {
                        allInside = false;
                        break;
                    }
                }

                if (allInside)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<IntVec3> GetSlotCells(ISlotGroupParent parent)
        {
            ScratchCells.Clear();

            Building_Storage building = parent as Building_Storage;
            if (building != null)
            {
                List<IntVec3> cells = building.AllSlotCellsList();
                for (int i = 0; i < cells.Count; i++)
                {
                    ScratchCells.Add(cells[i]);
                }

                return ScratchCells;
            }

            Zone_Stockpile zone = parent as Zone_Stockpile;
            if (zone != null)
            {
                List<IntVec3> cells = zone.AllSlotCellsList();
                for (int i = 0; i < cells.Count; i++)
                {
                    ScratchCells.Add(cells[i]);
                }

                return ScratchCells;
            }

            return ScratchCells;
        }

        private static void CollectGravEngines(Map map)
        {
            ScratchEngines.Clear();
            if (map == null)
            {
                return;
            }

            List<Thing> allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                Building_GravEngine engine = allThings[i] as Building_GravEngine;
                if (engine == null)
                {
                    continue;
                }

                ScratchEngines.Add(engine);
            }
        }

        private static Map GetParentMap(ISlotGroupParent parent)
        {
            Building_Storage building = parent as Building_Storage;
            if (building != null)
            {
                return building.Map;
            }

            Zone_Stockpile zone = parent as Zone_Stockpile;
            if (zone != null)
            {
                return zone.Map;
            }

            return null;
        }
    }
}
