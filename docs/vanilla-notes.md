# Vanilla Storage & Hauling Reference Notes

All paths reference the ILSpy export under `..\_ilspy\AssemblyCSharp`.

## Slot & Destination Management
- **RimWorld/SlotGroup.cs**
  - Wraps an `ISlotGroupParent` (stockpile zone, storage building).
  - `Notify_AddedCell` / `Notify_LostCell` update `Map.haulDestinationManager` and recalc haulable/mergeable lists per cell.
  - `StorageGroup` property surfaces the new 1.6 `StorageGroup` abstraction when the parent implements `IStorageGroupMember`.
- **RimWorld/HaulDestinationManager.cs**
  - Tracks every `IHaulDestination` and derived `SlotGroup` in priority order.
  - `AddHaulDestination` inserts slot groups, calls `SetCellFor` for every cell, and notifies `listerHaulables` / `listerMergeables`.
  - `AllGroupsListInPriorityOrder` (descending) is the list scanned by `StoreUtility.TryFindBestBetterStoreCellFor`.
  - `SlotGroupAt`/`SlotGroupParentAt` resolve the slot group for a cell, enabling quick stock lookups.

## Storage Decisions
- **RimWorld/StoreUtility.cs**
  - `CurrentHaulDestinationOf` returns the parent `IHaulDestination` for a spawned thing.
  - `StoragePriorityAtFor` enforces faction restrictions; returns `StoragePriority.Unstored` when the destination cannot accept the thing.
  - `TryFindBestBetterStoreCellFor` iterates `haulDestinationManager.AllGroupsListInPriorityOrder`, skipping parents that are disabled or belong to another faction. Calls `TryFindBestBetterStoreCellForWorker` to evaluate individual slot cells.
  - `TryFindBestBetterStorageFor` determines whether a better slot group or container exists; used extensively by hauling jobs and the "better storage" loop.
  - Cell validation relies on `NoStorageBlockersIn`, respecting multi-stack rules (`GetMaxItemsAllowedInCell`, reserved stack limits, etc.), so reusing it preserves shelf behaviour from other mods.

## Hauling Behaviour
- **Verse.AI/HaulAIUtility.cs**
  - `PawnCanAutomaticallyHaul`/`PawnCanAutomaticallyHaulFast` gate autonomous hauling jobs (designation checks, forbid logic, reachability).
  - `HaulToStorageJob` chooses between slot and container destinations by calling `StoreUtility.TryFindBestBetterStorageFor`.
  - `HaulToCellStorageJob` determines job counts and opportunistic pickups; it retrieves the target slot group directly from `haulDestinationManager`.
- **RimWorld/ListerHaulables.cs**
  - Maintains the set of haulable things (`Thing` lists per map) consulted by work givers.
  - Calls `ShouldBeHaulable` to decide inclusion, giving us a hook to exclude gravship stock items without forbidding them globally.
- **Capacity Helpers**
  - `StoreUtility.IsGoodStoreCell` plus `IntVec3.GetItemStackSpaceLeftFor` provide the exact filters vanilla uses when judging storage spots, so wrapping them keeps shelf stacking and modded capacity rules intact.

## Consumption & Forbiddable Overlay
- **Verse/CompForbiddable.cs**
  - Drives the red forbid cross overlay, using `CompForbiddable.DrawOverlay`.
  - Overlay is triggered from `ThingWithComps.DrawAt` (Verse/ThingWithComps.cs) when a `Comp` reports `AllowPlayerTarget`.
  - Mimicking this comp or Harmony-patching `Thing.Draw` lets us show a custom icon on gravship stock items without altering core data.

## Implications for the Overhaul
- Slot groups and hauling destinations already cache cell membership and priorities. By tagging vanilla slot groups instead of replacing them, we inherit priority comparisons, multi-stack support, and compatibility with mods that tweak `TryFindBestBetterStoreCellFor`.
- Cross-stock restrictions should be enforced at:
  - `StoreUtility.TryFindBestBetterStoreCellFor` (skip candidate slot groups whose tag differs from the source unless a cargo job is in flight).
  - `HaulAIUtility.PawnCanAutomaticallyHaul` and `ListerHaulables.ShouldBeHaulable` (exclude gravship stock items from autonomous hauling/consumption).
  - Consumption checks that rely on `Thing.IsForbidden` can be emulated by injecting a pseudo-forbid check for tagged items unless the job is `playerForced`.
- Validating gravship capacity via the vanilla helpers keeps behaviour aligned with future RimWorld changes-even when other mods alter storage stack limits.
- Vanilla overlays can be replicated via a lightweight "gravship marker" comp that only handles drawing, leaving interaction logic in the main map component.
