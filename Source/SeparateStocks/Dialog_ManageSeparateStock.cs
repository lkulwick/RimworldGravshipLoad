using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload.SeparateStocks
{
public sealed class Dialog_ManageSeparateStock : Window
{
    private readonly Map _map;
    private readonly SeparateStockManager _manager;

    private List<TransferableOneWay> _loadTransferables = new List<TransferableOneWay>();
    private List<TransferableOneWay> _unloadTransferables = new List<TransferableOneWay>();

    private TransferableOneWayWidget _loadWidget;
    private TransferableOneWayWidget _unloadWidget;

    public override Vector2 InitialSize => new Vector2(960f, 680f);

    public Dialog_ManageSeparateStock(Map map)
    {
        _map = map;
        _manager = SeparateStockManager.TryGet(map);
        forcePause = false;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
        doCloseX = true;
        Resize();
    }

    private void Resize()
    {
        BuildTransferables();
        _loadWidget = new TransferableOneWayWidget(_loadTransferables, null, null, "SeparateStock_LoadLabel".Translate(), drawMass: false);
        _unloadWidget = new TransferableOneWayWidget(_unloadTransferables, null, null, "SeparateStock_UnloadLabel".Translate(), drawMass: false);
    }

    private void BuildTransferables()
    {
        _loadTransferables.Clear();
        _unloadTransferables.Clear();

        foreach (var thing in SeparateStockUtility.ColonyThingsForLoad(_map))
        {
            AddToTransferables(thing, _loadTransferables);
        }

        foreach (var thing in SeparateStockUtility.SeparateStockThings(_map))
        {
            AddToTransferables(thing, _unloadTransferables);
        }
    }

    private static void AddToTransferables(Thing thing, List<TransferableOneWay> list)
    {
        if (thing == null || thing.Destroyed)
        {
            return;
        }

        var transferable = TransferableUtility.TransferableMatching(thing, list, TransferAsOneMode.PodsOrCaravanPacking);
        if (transferable == null)
        {
            transferable = new TransferableOneWay();
            list.Add(transferable);
        }
        transferable.things.Add(thing);
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (_manager == null)
        {
            Widgets.Label(inRect, "Separate stock manager missing.");
            return;
        }

        var topRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
        DrawHeader(topRect);

        var listingRect = new Rect(inRect.x, topRect.yMax + 4f, inRect.width, inRect.height - topRect.height - 60f);

        float columnSpacing = 14f;
        float columnWidth = (listingRect.width - columnSpacing) / 2f;
        var loadRect = new Rect(listingRect.x, listingRect.y, columnWidth, listingRect.height);
        var unloadRect = new Rect(loadRect.xMax + columnSpacing, listingRect.y, columnWidth, listingRect.height);

        DrawSection(loadRect, "SeparateStock_LoadLabel".Translate(), _loadWidget);
        DrawSection(unloadRect, "SeparateStock_UnloadLabel".Translate(), _unloadWidget);

        var bottomRect = new Rect(inRect.x, inRect.yMax - 45f, inRect.width, 45f);
        DrawBottomButtons(bottomRect);
    }

    private void DrawHeader(Rect rect)
    {
        Widgets.Label(rect, "SeparateStock_Header".Translate());
        rect.y += 24f;
        rect.height = 24f;
        bool allowUse = _manager.SeparateStock.AllowPawnAutoUse;
        Widgets.CheckboxLabeled(rect, "SeparateStock_AllowAutoUse".Translate(), ref allowUse);
        if (allowUse != _manager.SeparateStock.AllowPawnAutoUse)
        {
            _manager.SeparateStock.AllowPawnAutoUse = allowUse;
            SeparateStockLog.Message($"AllowPawnAutoUse toggled: {allowUse}");
        }
    }

    private static void DrawSection(Rect rect, TaggedString label, TransferableOneWayWidget widget)
    {
        var titleRect = new Rect(rect.x, rect.y, rect.width, 24f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(titleRect, label);
        Text.Anchor = TextAnchor.UpperLeft;

        var listRect = new Rect(rect.x, titleRect.yMax + 2f, rect.width, rect.height - 26f);
        GUI.BeginGroup(listRect);
        try
        {
            var innerRect = new Rect(0f, 0f, listRect.width, listRect.height);
            widget.OnGUI(innerRect, out _);
        }
        finally
        {
            GUI.EndGroup();
        }
    }

    private void DrawBottomButtons(Rect rect)
    {
        float buttonWidth = 160f;
        var acceptRect = new Rect(rect.x + rect.width - buttonWidth, rect.y, buttonWidth, rect.height - 5f);
        var resetRect = new Rect(acceptRect.x - buttonWidth - 10f, rect.y, buttonWidth, rect.height - 5f);
        var cancelRect = new Rect(resetRect.x - buttonWidth - 10f, rect.y, buttonWidth, rect.height - 5f);

        if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
        {
            Close();
        }

        if (Widgets.ButtonText(resetRect, "SeparateStock_Reset".Translate()))
        {
            foreach (var tr in _loadTransferables)
            {
                tr.AdjustTo(0);
            }
            foreach (var tr in _unloadTransferables)
            {
                tr.AdjustTo(0);
            }
        }

        if (Widgets.ButtonText(acceptRect, "AcceptButton".Translate()))
        {
            TryAccept();
        }
    }

    private void TryAccept()
    {
        var createdAny = false;
        var loadRequests = BuildTransferRequests(_loadTransferables, TransferDirection.ColonyToStock);
        if (loadRequests.Count > 0)
        {
            _manager.CreateOperation(TransferDirection.ColonyToStock, loadRequests);
            createdAny = true;
        }

        var unloadRequests = BuildTransferRequests(_unloadTransferables, TransferDirection.StockToColony);
        if (unloadRequests.Count > 0)
        {
            _manager.CreateOperation(TransferDirection.StockToColony, unloadRequests);
            createdAny = true;
        }

        if (!createdAny)
        {
            Messages.Message("SeparateStock_NoTransfersQueued".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        SeparateStockLog.Message("Transfer operations queued from dialog.");
        Close(doCloseSound: true);
    }

    private static List<TransferThing> BuildTransferRequests(List<TransferableOneWay> transferables, TransferDirection direction)
    {
        var result = new List<TransferThing>();
        for (int i = 0; i < transferables.Count; i++)
        {
            var transferable = transferables[i];
            if (transferable.CountToTransfer <= 0)
            {
                continue;
            }

            int remaining = transferable.CountToTransfer;
            foreach (var thing in transferable.things.Where(t => t != null && !t.Destroyed))
            {
                if (remaining <= 0)
                {
                    break;
                }

                int take = Mathf.Min(thing.stackCount, remaining);
                result.Add(new TransferThing(thing, take, direction, 0));
                remaining -= take;
            }
        }

        return result;
    }
}
}
