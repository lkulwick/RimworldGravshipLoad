using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Deep_Gravload.SeparateStocks
{
public static class SeparateStockUtility
{
    private static int _playerFloatMenuDepth;

    public static bool PlayerFloatMenuOpen => _playerFloatMenuDepth > 0;

    public static void PushPlayerFloatMenu()
    {
        _playerFloatMenuDepth++;
    }

    public static void PopPlayerFloatMenu()
    {
        if (_playerFloatMenuDepth > 0)
        {
            _playerFloatMenuDepth--;
        }
    }

    public static SeparateStockManager ManagerFor(Map map)
    {
        return SeparateStockManager.TryGet(map);
    }

    public static SeparateStockManager ManagerFor(Thing thing)
    {
        return thing?.MapHeld != null ? SeparateStockManager.TryGet(thing.MapHeld) : null;
    }

    public static bool IsInSeparateStock(this Thing thing)
    {
        var manager = ManagerFor(thing);
        return manager != null && manager.ThingInSeparateStock(thing);
    }

    public static bool IsSeparateStockParent(ISlotGroupParent parent)
    {
        if (parent?.Map == null)
        {
            return false;
        }

        var manager = SeparateStockManager.TryGet(parent.Map);
        return manager != null && manager.ParentInSeparateStock(parent);
    }

    public static IEnumerable<Thing> ColonyThingsForLoad(Map map)
    {
        var manager = ManagerFor(map);
        if (manager == null)
        {
            yield break;
        }

        var things = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
        for (int i = 0; i < things.Count; i++)
        {
            var thing = things[i];
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                continue;
            }
            if (!thing.def.EverStorable(false))
            {
                continue;
            }
            if (manager.ThingInSeparateStock(thing))
            {
                continue;
            }
            if (map.haulDestinationManager.SlotGroupAt(thing.Position) == null)
            {
                continue;
            }
            yield return thing;
        }
    }

    public static IEnumerable<Thing> SeparateStockThings(Map map)
    {
        var manager = ManagerFor(map);
        if (manager == null)
        {
            yield break;
        }

        foreach (var parent in manager.GetSeparateStockParents())
        {
            foreach (var cell in parent.AllSlotCells())
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                var list = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < list.Count; i++)
                {
                    var thing = list[i];
                    if (thing.def.EverStorable(false))
                    {
                        yield return thing;
                    }
                }
            }
        }
    }
}
}
