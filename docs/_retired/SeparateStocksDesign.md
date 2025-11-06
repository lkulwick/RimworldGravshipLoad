# SeparateStocks Architecture Plan

## Overview

We are replacing the gravload-specific storage tracker with a reusable **SeparateStocks** layer that extends RimWorld's vanilla stockpile logic. A _Stock_ is a named collection of stockpile zones and/or storage buildings whose cells are managed together. The vanilla behaviour represents the implicit `Colony` stock. Additional stocks (e.g. `Gravship Cargo`) behave like regular stockpiles for filtering, consumption, and intra-stock hauling, but pawns never shuttle items across stock boundaries unless a mod-driven operation (such as the gravship cargo window) explicitly tells them to do so.

This document captures the data model, core systems, vanilla integration points, and the migration path required to land the refactor while keeping the gravload feature functional.

## Goals

- Provide a generic framework for defining and managing named stocks with minimal gravload-specific logic.
- Preserve vanilla storage UX: players configure allow/deny filters per stockpile/shelf; bills, ingestion, and same-stock hauling continue to work.
- Block autonomous pawn hauling that would move items between different stocks.
- Allow the gravship cargo flow to request load/unload transfers between the `Colony` stock and a `Gravship Cargo` stock built on top of the new framework.
- Persist player-defined stocks in savegames.
- Leave hooks for future toggles like "disallow pawn usage" without wiring them yet.

## Key Concepts & Data Model

### Stock identifiers

- `StockId`: `int` keyed by `StockManagerComponent`. `0` is reserved for the implicit colony stock. Positive IDs are persisted in saves.
- `StockMetadata`: label, optional description, owning entity (e.g. `Building_GravEngine`), and future toggles (e.g. `AllowPawnAutoUse`, default `true`).

### Stock membership

- `StockRecord`: represents one stock. Holds metadata, a list of parents (`ISlotGroupParent` references), cached cell list, cached saved storage settings, and a `NeedsRefresh` flag.
- `StockCellInfo`: cached mapping from `IntVec3` → stock, similar to the existing `ManagedCellInfo`.
- `StockLock`: optional placeholder for future restrictions (e.g. reserved pawn IDs, pending toggles).

### Transfers

- `StockTransferOperation`: successor to `GravloadLoadOperation`. Identified by `int Id`, tracks `SourceStockId` and `DestinationStockId`, and contains transfer requests plus cell reservations.
- `StockTransferRequest`: successor to `GravloadTransferRequest`. Tracks `Thing SourceThing`, `int TotalCount`, `LoadedCount`, `ReservedCount`, and `bool ToDestinationStock`. `RemainingCount` stays unchanged.
- `StockCellReservation`: identical to the old structure but namespaced under SeparateStocks.
- `StockJobTicket`: equivalent to `GravloadJobTicket` with stock terminology.

## StockManagerComponent Responsibilities

Namespace: `SeparateStocks`.

Per-map component that:

1. Manages stock registration, metadata, and cell ownership.
2. Handles parent assignment/unassignment (`AssignParentToStock`, `ClearParentStock`).
3. Tracks cached cells and rebuilds them when parents change.
4. Provides queries:
   - `StockId GetStockOfCell(IntVec3)`
   - `StockId GetStockOfThing(Thing)`
   - `bool CellBelongsToStock(IntVec3, StockId)`
   - `bool StockContainsParent(ISlotGroupParent)`
5. Drives vanilla interop via helper predicates:
   - `bool IsCrossStockMove(IntVec3 from, IntVec3 to)`
   - `bool ShouldSkipHaulable(Thing)`
6. Exposes transfer APIs used by job givers:
   - `GetPendingThings()`
   - `TryAssignHaulJob` / `ReleaseTicket`
   - `TryStartTransferOperation(StockId source, StockId destination, ...)`
7. Serializes stock metadata and operations through `ExposeData` and rebuilds caches on load.

### Cell management

- When a parent is assigned, we snapshot its storage settings (like `ManagedStorageGroup` does), apply `SetDisallowAll`, and mark all cells as belonging to the stock.
- Items placed in a stock cell trigger `NotifyThingEnteredStock`. No global forbidding is performed; instead we rely on haulability patches (see below).
- When removed from a stock, we restore stored settings and unmark cells.

### Migration strategy

- Keep `Deep_Gravload.GravloadMapComponent` as a thin wrapper around the new component during the transition. The wrapper implements `IExposable`, forwards all calls to an internal `SeparateStocks.StockManagerComponent`, and migrates existing save data on `PostLoadInit`.
- Once the wrapper has copied data, the gravload namespace code uses the new public API and the old fields (`originalForbiddenState`, etc.) are discarded.

## Vanilla Integration (Harmony Patches)

1. `ListerHaulables.ShouldBeHaulable` (postfix)  
   - Acquire `StockManagerComponent`.  
   - If the thing's stock is not `Colony` and there is no active transfer request targeting the colony, return `false`.  
   - This keeps separate-stock items out of general hauling without forbidding them.

2. `StoreUtility.TryFindBestBetterStoreCellForWorker` (prefix/wrapper)  
   - Skip candidate cells if they belong to a different stock than the source thing.  
   - Allows intra-stock moves (better shelf within the same stock) while blocking cross-stock auto-hauls.

3. `StoreUtility.TryFindBestBetterNonSlotGroupStorageFor` (similar filter)  
   - Only relevant if future stocks include non-slot parents (containers).

4. `HaulAIUtility.PawnCanAutomaticallyHaulFast` (postfix)  
   - Reject hauling if origin thing is in a non-colony stock and the job is not forced.

5. `WorkGiver_Scanner.PotentialWorkThingsGlobal` or a more targeted hook  
   - Ensure the gravload job giver still sees pending things by consulting the stock manager rather than `listerHaulables`.

6. Gizmo patches (`Zone_Stockpile.GetGizmos`, `Building_Storage.GetGizmos`)  
   - Use new `StockUtility.TryCreateStockToggle` that composes the previous gravload toggle but now wired into `StockManagerComponent`.

## Gravload Integration

- New helper `GravshipStockUtility` (under SeparateStocks or a gravload bridge namespace) that:
  - Creates/retrieves a stock per `Building_GravEngine`.
  - Exposes `GetStockIdForEngine`, `AssignParentToGravshipStock`, `StartGravshipTransfer`.
- `Dialog_GravloadCargo`, `WorkGiver_LoadGravload`, and `JobDriver_LoadGravload` shift to the stock APIs:
  - `Dialog` calls `StockManagerComponent.BuildTransferSelections` equivalent.
  - `WorkGiver` enumerates `StockManagerComponent.GetPendingThings()` filtered by the gravship stock.
  - `JobDriver` updates to reference `StockJobTicket`.
- Existing localization keys remain; only code paths change.

## Impact on Existing Gravload Features & Risks

- **Dialog_GravloadCargo** → swap over to stock APIs; ensure initial counts honour the new stock cell mapping. Regression risk: mismatched `TransferableOneWay` counts if migration misses some items.
- **WorkGiver_LoadGravload & JobDriver_LoadGravload** → rename types and adjust to `StockJobTicket`. Risk: job tickets not released properly if migration path fails; add logging around `ReleaseTicket`.
- **Harmony patches (Building/Zone gizmos)** → move to new utility while keeping the same translation keys. Risk: toggles might be offered on non-player storage if stock detection fails; guard with faction checks.
- **Save compatibility** → old saves rely on `GravloadMapComponent`. Provide wrapper and unit-test `ExposeData` migration. Failure would break saves; include defensive null checks and a one-time migration flag.
- **Hauling filters** → incorrect stock detection could either let pawns raid separate stocks or block legitimate colony hauling. Mitigate with cached lookups and extensive playtesting (load/unload both directions, third-party hauling jobs).
- **Performance** → Additional dictionary lookups occur on haulable scans; cache stock IDs per map tick or use `Thing.Map` keyed dictionaries to avoid repeated map component retrieval.

## Save Data

- `StockManagerComponent` serializes:
  - `List<StockRecord> stocks`
  - `int nextStockId`
  - `List<StockTransferOperation> activeOperations`
- `StockRecord` serializes parent references and saved settings.
- Migration path: during `PostLoadInit`, the gravload wrapper (if data present) builds equivalent `StockRecord` instances and clears legacy fields to avoid double-processing.

## Open Questions & Next Steps

1. **Stock creation UI** – For now we leverage existing gravload toggles to auto-create a stock per engine. Future work: generic stock management dialog.
2. **Pawn auto-use toggle** – Data model includes `AllowPawnAutoUse` but the toggle is not exposed yet; default `true`.
3. **Testing** – Need regression passes for load/unload, gravload job cancellation, and edge cases when stock filters reject requested transfers.
4. **Performance** – Ensure hauling patches are efficient (cache stock lookups, avoid repeated dictionary allocations).

## Implementation Roadmap

1. Introduce SeparateStocks namespace, data classes, and map component.
2. Implement migration wrapper for `GravloadMapComponent`.
3. Refactor gravload systems to consume the new API.
4. Add Harmony patches for vanilla integration.
5. Update dialog/workgiver/jobdriver to new naming.
6. Remove legacy forbid logic once new hauling filters are verified.
7. Manual and automated tests for the gravload scenario described in the user story.
