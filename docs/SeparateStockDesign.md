# Separate Stock Feature Design

## Goals
- Allow the player to designate vanilla stockpiles, shelves, and other `ISlotGroupParent` storage as belonging to a special “separate stock”.
- Reuse vanilla hauling/storage behaviour within that stock while preventing autonomous cross-stock moves.
- Gate pawn consumption and bill usage unless a per-stock toggle allows it.
- Provide a shuttle-style load/unload dialog to move items between the colony stock and the separate stock via pawn jobs.
- Keep the code open to supporting multiple stocks in the future.

## High-Level Architecture

### Map Component
- `SeparateStockManager` (map component) stores stocks, currently limited to the implicit colony (`StockId = 0`) plus the managed separate stock (`StockId = 1`).
- Tracks:
  - `List<SeparateStockRecord>` with metadata (`label`, `allowPawnAutoUse`), assigned parents, and cached cell sets.
  - Pending `SeparateStockTransferOperation` objects for load/unload jobs.
- Rebuilds cell caches via vanilla `SlotGroup` notifications; patches add calls into the manager on `Notify_AddedCell`/`Notify_LostCell`.
- Persists all state via `ExposeData`.

### Stock Membership
- Parents referenced by strong typed handles:
  - Zones (`Zone_Stockpile`), buildings (`Building_Storage`), or any other `ISlotGroupParent`.
- Toggling membership snapshots/restores storage settings and refreshes cached cells.
- The manager resolves `Thing` → `stockId` by:
  - Checking `thing.Position` against cached cells when spawned.
  - Tracking `ThingOwner` parents for storage buildings.

### Transfer Operations
- `SeparateStockTransferOperation` contains:
  - `TransferDirection` (`ColonyToStock`, `StockToColony`).
  - `List<PendingTransfer>` with target thing definition, total desired count, and progress counts.
  - `List<SeparateStockJobTicket>` linking pawns/jobs to reserved stacks.
- Operations drive the work giver; completed requests remove themselves.
- Debug logging (via `SeparateStockLog`) prints lifecycle events: creation, job assignment, completion, errors.

### UI
- `Dialog_ManageSeparateStock` clones the transport pod dialog layout:
  - Builds `TransferableOneWay` lists representing current stock contents vs colony inventory.
  - Player chooses load/unload counts, which start a transfer operation.
  - Displays outstanding requests and acknowledges in-progress hauling.
- Gizmos added to member parents:
  - Toggle inclusion in the separate stock.
  - “Manage stock” command (only visible if the parent belongs to the stock).
  - Stock-wide toggle for `allowPawnAutoUse`.

### Hauling Integration
- Harmony patches:
  - `StoreUtility.TryFindBestBetterStoreCellForWorker` & `TryFindBestBetterNonSlotGroupStorageFor` skip destinations whose stock differs from the origin unless a transfer job is active.
  - `HaulAIUtility.PawnCanAutomaticallyHaulFast` aborts when the item belongs to the separate stock and the job is not a transfer ticket.
  - `HaulJobUtility`/`WorkGiver_HaulGeneral` naturally respect the above filters since they rely on `ShouldBeHaulable` and `StoreUtility`.
  - `Zone_Stockpile.GetGizmos` / `Building_Storage.GetGizmos` append the new controls.
- Vanillia hauling within the stock (e.g., move to higher priority shelf inside the bunker) remains unchanged.

### Consumption / Bills
- Common helper `SeparateStockUtility.ShouldBlockPawnUse(pawn, thing)`:
  - Returns `true` when the thing belongs to the separate stock and `allowPawnAutoUse` is false.
- Patched call sites:
  - `FoodUtility.TryFindBestFoodSourceFor`
  - `HealthAIUtility.FindBestMedicine`
  - `WorkGiver_DoBill.TryFindBestIngredientsHelper`
  - Additional ingestion/bill helpers as necessary.
- When blocked, the code falls back to vanilla alternatives and, if nothing else fits, leaves the job to fail gracefully (same as forbidden items).

### Load Jobs
- `WorkGiver_SeparateStockTransfer` enumerates active operations:
  - Reserves source stacks using `Pawn.Reserve`.
  - Requests target cells by calling `SeparateStockManager.TryGetHaulDestinationCell`.
  - Generates a `JobDefOf.HaulToCell` job with an attached `SeparateStockJobTicket`.
- `JobDriver_SeparateStockTransfer` executes:
  - `Toils_Goto` → `Toils_Haul.StartCarryThing` → `Toils_Goto` target cell.
  - On fail to find cell, posts `MessageTypeDefOf.RejectInput` and completes the ticket so the operation can retry or be cancelled.
- Unload jobs call `HaulToCellNonStorage`, but the target cell is chosen outside the stock area (closest walkable non-stock cell). Vanilla haulers will later bring it back to colony storage.

### Debug Logging
- `SeparateStockLog` static helper wraps `Log.Message` with `Prefs.LogVerbose` guard to avoid spam in release builds.
- Logs stock creation/removal, parent toggles, operation lifecycle, job start/finish, errors (missing cells, blocked usage).
- Can be toggled via mod settings later; initially always enabled when `Prefs.DevMode`.

## Extensibility
- Multiple stock support: promote `SeparateStockRecord` list handling (IDs > 1), surface selection in the gizmo, and allow multiple active operations keyed by stock ID.
- Sharing logic with other mods: `SeparateStockManager` exposes a public API (`GetStockForCell`, `IsThingInSeparateStock`, etc.) so other features (e.g., custom shuttles) can reuse it.
- UI enhancements: shared transfer queue widget, access-control modifiers per stock, integration with mod options menu.

## Risks & Mitigations
- **Harmony conflicts**: keep patches minimal and avoid transpilers; use postfix/prefix checks with early returns to stay compatible with other hauling mods.
- **Performance**: cache lookups by storing `Dictionary<IntVec3, StockId>` and `HashSet` per stock; avoid per-tick scanning.
- **Failure cases**: job-driven capacity check ensures pawns report “no place to store” through vanilla messaging when cells fill up; operation remains pending for player adjustment.
- **Save compatibility**: component serializes using stable keys and gracefully rebuilds missing parents on load; unexpected nulls produce warning logs instead of crashing.

