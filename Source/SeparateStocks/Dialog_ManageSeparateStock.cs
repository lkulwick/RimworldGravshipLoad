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

    private readonly List<TransferableOneWay> _loadTransferables = new List<TransferableOneWay>();
    private readonly List<TransferableOneWay> _unloadTransferables = new List<TransferableOneWay>();
    private readonly List<TransferThing> _pendingTransfers = new List<TransferThing>();
    private Vector2 _pendingScrollPosition;

    private TransferableOneWayWidget _transferWidget;

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
        _transferWidget = new TransferableOneWayWidget(null, null, null, "SeparateStock_SourceCountTip".Translate(), drawMass: false);
        _transferWidget.AddSection("SeparateStock_LoadLabel".Translate(), _loadTransferables);
        _transferWidget.AddSection("SeparateStock_UnloadLabel".Translate(), _unloadTransferables);
    }

    private void BuildTransferables()
    {
        _loadTransferables.Clear();
        _unloadTransferables.Clear();

        foreach (var thing in SeparateStockUtility.ColonyThingsForLoad(_map))
        {
            AddToTransferables(thing, _loadTransferables, reverse: false);
        }

        foreach (var thing in SeparateStockUtility.SeparateStockThings(_map))
        {
            AddToTransferables(thing, _unloadTransferables, reverse: true);
        }
    }

    private static void AddToTransferables(Thing thing, List<TransferableOneWay> list, bool reverse)
    {
        if (thing == null || thing.Destroyed)
        {
            return;
        }

        var transferable = TransferableUtility.TransferableMatching(thing, list, TransferAsOneMode.PodsOrCaravanPacking);
        if (transferable == null)
        {
            transferable = reverse ? new TransferableOneWayReverse() : new TransferableOneWay();
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

        float pendingHeight = 150f;
        var pendingRect = new Rect(inRect.x, topRect.yMax + 4f, inRect.width, pendingHeight);
        DrawPendingTransfers(pendingRect);

        var widgetRect = new Rect(inRect.x, pendingRect.yMax + 4f, inRect.width, inRect.height - topRect.height - pendingHeight - 60f - 8f);
        _transferWidget.OnGUI(widgetRect, out _);

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

    private void DrawPendingTransfers(Rect rect)
    {
        var headerRect = new Rect(rect.x, rect.y, rect.width, 24f);
        Widgets.Label(headerRect, "SeparateStock_PendingHeader".Translate());

        var listRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - headerRect.height);
        Widgets.DrawMenuSection(listRect);
        var innerRect = listRect.ContractedBy(4f);

        GatherPendingTransfers();
        if (_pendingTransfers.Count == 0)
        {
            Widgets.Label(innerRect, "SeparateStock_PendingEmpty".Translate());
            return;
        }

        const float rowHeight = 28f;
        var viewRect = new Rect(0f, 0f, innerRect.width - 16f, _pendingTransfers.Count * rowHeight);
        Widgets.BeginScrollView(innerRect, ref _pendingScrollPosition, viewRect);
        float curY = 0f;
        for (int i = 0; i < _pendingTransfers.Count; i++)
        {
            var transfer = _pendingTransfers[i];
            var rowRect = new Rect(0f, curY, viewRect.width, rowHeight);
            if (i % 2 == 0)
            {
                Widgets.DrawAltRect(rowRect);
            }
            if (transfer != null)
            {
                DrawPendingTransferRow(rowRect, transfer);
            }
            curY += rowHeight;
        }
        Widgets.EndScrollView();
    }

    private void GatherPendingTransfers()
    {
        _pendingTransfers.Clear();
        if (_manager == null)
        {
            return;
        }

        var operations = _manager.Operations;
        for (int i = 0; i < operations.Count; i++)
        {
            var pending = operations[i].PendingThings;
            for (int j = 0; j < pending.Count; j++)
            {
                var transfer = pending[j];
                if (transfer != null && transfer.RemainingCount > 0)
                {
                    _pendingTransfers.Add(transfer);
                }
            }
        }
    }

    private void DrawPendingTransferRow(Rect rect, TransferThing transfer)
    {
        var textRect = new Rect(rect.x + 4f, rect.y, rect.width - 90f, rect.height);
        var cancelRect = new Rect(rect.xMax - 80f, rect.y + 3f, 76f, rect.height - 6f);

        string dirLabel = transfer.Direction == TransferDirection.ColonyToStock ? "SeparateStock_LoadLabel".Translate() : "SeparateStock_UnloadLabel".Translate();
        string thingLabel = transfer.Thing?.LabelCap ?? "Missing";
        Widgets.Label(textRect, $"{dirLabel}: {thingLabel} x{transfer.RemainingCount}");

        if (Widgets.ButtonText(cancelRect, "SeparateStock_CancelTransfer".Translate()))
        {
            if (_manager.CancelTransfer(transfer))
            {
                GatherPendingTransfers();
            }
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
            ResetTransferCounts();
        }

        if (Widgets.ButtonText(acceptRect, "AcceptButton".Translate()))
        {
            TryAccept();
        }
    }

    private void TryAccept()
    {
        var createdAny = false;
        var loadRequests = BuildTransferRequests(_loadTransferables, TransferDirection.ColonyToStock, fromStock: false);
        if (loadRequests.Count > 0)
        {
            _manager.CreateOperation(TransferDirection.ColonyToStock, loadRequests);
            createdAny = true;
        }

        var unloadRequests = BuildTransferRequests(_unloadTransferables, TransferDirection.StockToColony, fromStock: true);
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

    private void ResetTransferCounts()
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

    private static List<TransferThing> BuildTransferRequests(List<TransferableOneWay> transferables, TransferDirection direction, bool fromStock)
    {
        var result = new List<TransferThing>();
        for (int i = 0; i < transferables.Count; i++)
        {
            var transferable = transferables[i];
            int desired = fromStock ? transferable.CountToTransferToSource : transferable.CountToTransfer;
            if (desired <= 0)
            {
                continue;
            }

            int remaining = desired;
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
