# Gravload Feature Inventory

## Storage Management
- Player shelves and stockpile zones inside a gravship expose a managed-storage toggle that appears only when the parent is player controlled and every slot sits inside the gravship substructure.
- Enabling management snapshots the building or zone storage filter and priority, registers the parent against the engine-specific stock, and tracks the managed parents in a map-level cache.
- Disabling management restores the previously captured storage settings, unregisters the parent from the stock, and refreshes the stock cell cache so logistics reflect the change immediately.
- Each grav engine is paired with its own `SeparateStocks.StockRecord`; the map component maintains bidirectional engine <-> stock mappings and will create the record on demand if one does not already exist.
- Pending managed buildings (for example blueprints that spawn later) are tracked so they are registered automatically once the building appears on the map and still passes the gravship containment checks.
- Managed cells are tracked in a `cellLookup` cache that maps `IntVec3` positions to stocks, letting the mod answer questions such as 'is this cell managed?' or 'which engine owns this thing?'.

## Cargo Operations
- The cargo dialog can queue both load (colony -> gravship) and unload (gravship -> colony) transfers at the same time, producing distinct `ThingCount` selections for each direction.
- `StockManagerComponent.TryStartTransferOperation` validates every selection: items must be spawned on the map, belong to the expected stock, and not already be part of another pending operation.
- Before accepting load requests, the destination stock's capacity is simulated cell by cell, ensuring there is sufficient free or stack-compatible space for every request before the transfer is enqueued.
- Accepted transfers are wrapped in `StockTransferOperation` objects that track per-request state (total, reserved, loaded counts) plus per-cell reservations so multiple pawns can load without collisions.
- Operations can be cancelled per grav engine, and stale transfers are pruned automatically when their source things despawn or all requests complete.
- The stock manager exposes `GetPendingThings`, `CanHandleThing`, and `TryAssignHaulJob` so the dedicated work giver can pull actionable hauling jobs from the active operations queue.
- Job tickets reserve both the source stack and the chosen destination cell, decrement reservations when released, and update loaded counts when a pawn successfully delivers cargo.

## UI and Controls
- Grav engines gain a "Load Cargo" gizmo that opens `Dialog_GravloadCargo`; the command is automatically disabled (with a translated explanation) until at least one managed cell exists for that engine.
- The cargo dialog uses `TransferableOneWayWidget` with custom labels to show colony inventory on the left and gravship inventory on the right, defaulting the ship column to the items already aboard.
- The dialog rebuilds its transferable list every 30 ticks while preserving the player's current load and unload selections, so the UI stays in sync with pawn activity without resetting choices.
- When the player confirms, detailed failure messages are surfaced for common issues (no engine, no selections, item off-map, insufficient space, already queued) so missteps are easy to correct.

## Hauling Behaviour Changes
- Managed-stock items are removed from the colony-wide haulable list (`ListerHaulables.ShouldBeHaulable`) unless they are actively queued for transfer, preventing routine haulers from touching gravship cargo.
- Auto-hauling checks (`HaulAIUtility.PawnCanAutomaticallyHaulFast`) reject jobs that would move items across stocks unless the player explicitly forces the job.
- Storage placement logic (`StoreUtility.TryFindBestBetterStoreCellFor`) filters out destination cells that belong to a different stock, avoiding accidental cross-stock moves during opportunistic hauling.
- A dedicated `WorkGiver_LoadGravload` under the hauling work type surfaces only the items referenced by active operations, and the custom `JobDriver_LoadGravload` handles reservations, stack splitting, placement, and ticket release.
- Autonomous colonist hauling still uses the vanilla pipeline, but gravship stock items are pseudo-forbidden unless a gravload job or player-forced action is in flight, so everyday bills can't poach gravship supplies.

## Save Data & Persistence
- Managed parents, their saved storage settings, and the engine <-> stock mappings are serialized so gravship stock assignments survive save and load cycles.
- `GravloadMapComponent` and `StockManagerComponent` are instantiated lazily on any map that needs them, ensuring saves remain compatible even if map component defs are missing.
- `StockTransferOperation` and `StockTransferRequest` implement `IExposable`, allowing active cargo jobs (including reservations and partial progress) to resume correctly after saving.

## Helper Utilities & Safeguards
- `ManagedStorageUtility` locates grav engines, verifies that all storage slots are within the connected substructure, materializes gizmo enumerables for safe mutation, and notifies parents when settings change.
- Capacity calculations ignore blueprints and frames, respect stack limits, and account for both existing stack counts and in-flight reservations so pawns never overfill a cell during loading.
- Ticket and operation cleanup runs periodically, removing completed jobs and any tickets whose operations finished to keep dictionaries from leaking entries over time.
- `GravloadMapComponent` mirrors vanilla forbid overlays with a subtle question-mark marker so players can spot gravship-owned stacks at a glance, disabling the marker automatically when items leave the stock.
