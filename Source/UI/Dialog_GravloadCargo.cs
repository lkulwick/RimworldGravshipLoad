using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Deep_Gravload
{
    public class Dialog_GravloadCargo : Window
    {
        private readonly Building_GravEngine engine;
        private readonly Map map;
        private readonly List<TransferableOneWay> transferables = new List<TransferableOneWay>();
        private readonly Dictionary<TransferableOneWay, int> initialShipCounts = new Dictionary<TransferableOneWay, int>();
        private readonly HashSet<Thing> shipThings = new HashSet<Thing>();
        private const int AutoRefreshIntervalTicks = 30;
        private int nextRefreshTick;
        private static readonly MethodInfo SetCountToTransferMethod = AccessTools.PropertySetter(typeof(TransferableOneWay), "CountToTransfer");
        private static readonly FieldInfo CountToTransferField = AccessTools.Field(typeof(TransferableOneWay), "countToTransfer");
        private TransferableOneWayWidget widget;

        public Dialog_GravloadCargo(Building_GravEngine engine)
        {
            this.engine = engine;
            this.map = engine.Map;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.doCloseButton = false;
            this.doCloseX = true;
            this.closeOnClickedOutside = true;
            this.closeOnAccept = false;
            this.RebuildTransferables();
        }

        public override Vector2 InitialSize => new Vector2(860f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "DeepGravload_WindowTitle".Translate());
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 90f);
            this.widget.OnGUI(listRect);

            Rect buttonRect = new Rect(inRect.x, inRect.yMax - 40f, 160f, 35f);
            if (Widgets.ButtonText(buttonRect, "DeepGravload_ButtonLoad".Translate()))
            {
                this.TryAccept();
            }

            Rect cancelRect = new Rect(buttonRect.xMax + 10f, buttonRect.y, 160f, 35f);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                this.Close();
            }
        }

        private void RebuildTransferables(Dictionary<string, int> loadSelections = null, Dictionary<string, int> unloadSelections = null)
        {
            this.transferables.Clear();
            this.initialShipCounts.Clear();
            this.shipThings.Clear();

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(this.map);
            if (tracker == null)
            {
                return;
            }

            List<Thing> things = this.map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                bool isShipThing = tracker.TryGetEngineForThing(thing, out Building_GravEngine engineForThing) && engineForThing == this.engine;

                if (!isShipThing && !this.ShouldConsider(thing, tracker))
                {
                    continue;
                }

                TransferableOneWay transferable = TransferableUtility.TransferableMatching(thing, this.transferables, TransferAsOneMode.PodsOrCaravanPacking);
                if (transferable == null)
                {
                    transferable = new TransferableOneWay();
                    transferable.things.Add(thing);
                    this.transferables.Add(transferable);
                }
                else
                {
                    transferable.things.Add(thing);
                }

                if (isShipThing)
                {
                    this.shipThings.Add(thing);

                    int currentCount;
                    if (this.initialShipCounts.TryGetValue(transferable, out currentCount))
                    {
                        this.initialShipCounts[transferable] = currentCount + thing.stackCount;
                    }
                    else
                    {
                        this.initialShipCounts.Add(transferable, thing.stackCount);
                    }
                }
            }

            for (int j = 0; j < this.transferables.Count; j++)
            {
                TransferableOneWay transferableOneWay = this.transferables[j];
                int initialCount;
                if (!this.initialShipCounts.TryGetValue(transferableOneWay, out initialCount))
                {
                    initialCount = 0;
                    this.initialShipCounts.Add(transferableOneWay, 0);
                }

                if (initialCount > transferableOneWay.MaxCount)
                {
                    initialCount = transferableOneWay.MaxCount;
                }

                this.SetTransferableCount(transferableOneWay, initialCount);
            }

            if ((loadSelections != null && loadSelections.Count > 0) || (unloadSelections != null && unloadSelections.Count > 0))
            {
                this.ApplySelections(loadSelections, unloadSelections);
            }

            this.widget = new TransferableOneWayWidget(
                this.transferables,
                "DeepGravload_SourceLabel".Translate(),
                "DeepGravload_DestinationLabel".Translate(),
                "DeepGravload_SourceCountLabel".Translate(),
                false,
                IgnorePawnsInventoryMode.Ignore,
                false,
                () => float.PositiveInfinity,
                0f,
                false,
                null,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false);

            this.ScheduleNextRefresh();
        }

        private void CaptureSelections(out Dictionary<string, int> loadSelections, out Dictionary<string, int> unloadSelections)
        {
            loadSelections = new Dictionary<string, int>();
            unloadSelections = new Dictionary<string, int>();

            for (int i = 0; i < this.transferables.Count; i++)
            {
                TransferableOneWay transferable = this.transferables[i];
                int initialCount;
                this.initialShipCounts.TryGetValue(transferable, out initialCount);

                int targetCount = transferable.CountToTransfer;
                if (targetCount == initialCount)
                {
                    continue;
                }

                if (targetCount > initialCount)
                {
                    int remaining = targetCount - initialCount;
                    for (int j = 0; j < transferable.things.Count && remaining > 0; j++)
                    {
                        Thing thing = transferable.things[j];
                        if (this.shipThings.Contains(thing))
                        {
                            continue;
                        }

                        int take = Mathf.Min(remaining, thing.stackCount);
                        if (take > 0)
                        {
                            loadSelections[thing.ThingID] = take;
                            remaining -= take;
                        }
                    }
                }
                else
                {
                    int remaining = initialCount - targetCount;
                    for (int j = 0; j < transferable.things.Count && remaining > 0; j++)
                    {
                        Thing thing = transferable.things[j];
                        if (!this.shipThings.Contains(thing))
                        {
                            continue;
                        }

                        int take = Mathf.Min(remaining, thing.stackCount);
                        if (take > 0)
                        {
                            unloadSelections[thing.ThingID] = take;
                            remaining -= take;
                        }
                    }
                }
            }
        }

        private void ApplySelections(Dictionary<string, int> loadSelections, Dictionary<string, int> unloadSelections)
        {
            for (int i = 0; i < this.transferables.Count; i++)
            {
                TransferableOneWay transferable = this.transferables[i];
                int initialCount;
                this.initialShipCounts.TryGetValue(transferable, out initialCount);
                int targetCount = initialCount;

                for (int j = 0; j < transferable.things.Count; j++)
                {
                    Thing thing = transferable.things[j];
                    string thingId = thing.ThingID;

                    if (this.shipThings.Contains(thing))
                    {
                        if (unloadSelections != null && unloadSelections.TryGetValue(thingId, out int unloadValue))
                        {
                            int applied = Mathf.Min(unloadValue, thing.stackCount);
                            targetCount -= applied;
                        }
                    }
                    else
                    {
                        if (loadSelections != null && loadSelections.TryGetValue(thingId, out int loadValue))
                        {
                            int applied = Mathf.Min(loadValue, thing.stackCount);
                            targetCount += applied;
                        }
                    }
                }

                targetCount = Mathf.Clamp(targetCount, 0, transferable.MaxCount);
                this.SetTransferableCount(transferable, targetCount);
            }
        }

        private void RefreshTransferablesPreservingSelection()
        {
            Dictionary<string, int> loadSelections;
            Dictionary<string, int> unloadSelections;
            this.CaptureSelections(out loadSelections, out unloadSelections);
            this.RebuildTransferables(loadSelections, unloadSelections);
        }

        private void SetTransferableCount(TransferableOneWay transferable, int value)
        {
            if (SetCountToTransferMethod != null)
            {
                SetCountToTransferMethod.Invoke(transferable, new object[] { value });
            }
            else if (CountToTransferField != null)
            {
                CountToTransferField.SetValue(transferable, value);
            }
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            if (Find.TickManager == null)
            {
                return;
            }

            if (Find.TickManager.TicksGame >= this.nextRefreshTick)
            {
                this.RefreshTransferablesPreservingSelection();
            }
        }

        public void MarkDirty()
        {
            this.nextRefreshTick = 0;
        }

        private void ScheduleNextRefresh()
        {
            if (Find.TickManager != null)
            {
                this.nextRefreshTick = Find.TickManager.TicksGame + AutoRefreshIntervalTicks;
            }
        }

        private bool ShouldConsider(Thing thing, GravloadMapComponent tracker)
        {
            if (thing == null || !thing.Spawned)
            {
                return false;
            }

            if (thing.Map != this.map)
            {
                return false;
            }

            if (!thing.def.EverHaulable)
            {
                return false;
            }

            if (thing.def.category != ThingCategory.Item)
            {
                return false;
            }

            if (tracker.IsThingInManagedCell(thing))
            {
                return false;
            }

            if (thing.IsForbidden(Faction.OfPlayer))
            {
                return false;
            }

            return true;
        }

        private void TryAccept()
        {
            List<ThingCount> loads = new List<ThingCount>();
            List<ThingCount> unloads = new List<ThingCount>();
            this.BuildSelections(loads, unloads);
            if (loads.Count == 0 && unloads.Count == 0)
            {
                Messages.Message("DeepGravload_Error_NoItemsSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            GravloadMapComponent tracker = ManagedStorageUtility.GetTracker(this.map);
            if (tracker == null)
            {
                Messages.Message("DeepGravload_Error_NoEngine".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string failureReason;
            if (!tracker.TryStartLoadOperation(this.engine, loads, unloads, out failureReason))
            {
                if (!failureReason.NullOrEmpty())
                {
                    Messages.Message(failureReason, MessageTypeDefOf.RejectInput, false);
                }
                return;
            }

            this.Close();
        }

        private void BuildSelections(List<ThingCount> loads, List<ThingCount> unloads)
        {
            for (int i = 0; i < this.transferables.Count; i++)
            {
                TransferableOneWay transferable = this.transferables[i];
                int initialCount = 0;
                this.initialShipCounts.TryGetValue(transferable, out initialCount);

                int targetCount = transferable.CountToTransfer;
                if (targetCount == initialCount)
                {
                    continue;
                }

                List<Thing> things = transferable.things;
                if (targetCount > initialCount)
                {
                    int remaining = targetCount - initialCount;
                    for (int j = 0; j < things.Count && remaining > 0; j++)
                    {
                        Thing thing = things[j];
                        if (this.shipThings.Contains(thing))
                        {
                            continue;
                        }

                        int take = remaining;
                        if (take > thing.stackCount)
                        {
                            take = thing.stackCount;
                        }

                        if (take > 0)
                        {
                            loads.Add(new ThingCount(thing, take));
                            remaining -= take;
                        }
                    }
                }
                else
                {
                    int remaining = initialCount - targetCount;
                    for (int j = 0; j < things.Count && remaining > 0; j++)
                    {
                        Thing thing = things[j];
                        if (!this.shipThings.Contains(thing))
                        {
                            continue;
                        }

                        int take = remaining;
                        if (take > thing.stackCount)
                        {
                            take = thing.stackCount;
                        }

                        if (take > 0)
                        {
                            unloads.Add(new ThingCount(thing, take));
                            remaining -= take;
                        }
                    }
                }
            }
        }
    }
}
