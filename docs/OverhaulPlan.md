# Gravload Storage Overhaul Plan

## Goals
- Reuse vanilla RimWorld storage, hauling, and reservation logic wherever possible.
- Treat gravship-managed shelves/zones as ordinary slot groups for intra-stock shuffles while preventing autonomous colony <-> gravship transfers.
- Preserve player-forced overrides but block routine consumption/bill usage of gravship stock items.
- Provide a lightweight visual indicator for gravship-owned stacks.
- Keep architecture generic enough to support future "separate stock" use cases beyond gravships.

## Phase 1 - Reference & Analysis (in progress)
1. Pull the required vanilla assemblies from `RimWorldWin64_Data/Managed` and decompile (via ILSpy exports when available) the following classes:
   - `SlotGroup`, `SlotGroupManager`, `HaulDestinationManager`, `StoreUtility`, `HaulAIUtility`, `ListerHaulables`.
   - `JobDriver_HaulToCell`, `JobDriver_HaulToContainer`, `WorkGiver_HaulGeneral`.
   - `CompForbiddable`, `ThingWithComps.DrawAt` overlays, and any helpers responsible for the red forbid cross.
2. Document the key methods we intend to hook, noting inputs/outputs and side-effects (dump findings into `docs/vanilla-notes.md`).
3. Identify extension points that let us mark slot groups/cells without deregistering them from vanilla managers.

## Phase 2 - Design Updates
1. Define a "separate stock" tag mechanism (likely a `MapComponent` + `IThingHolder` extension or `Comp`) that associates vanilla slot groups with a `StockId`.
2. Decide how gravship engines map to stock IDs and how multiple parents (shelves/zones) opt in/out.
3. Specify the handshake between the cargo dialog and the new tagging system (queries for eligible items/cells, enqueueing transfers using vanilla jobs).
4. Outline visual overlay strategy (e.g. Harmony postfix on `Thing.DrawAt` or reusable `Comp`).

## Phase 3 - Implementation
1. Refactor `GravloadMapComponent` / `ManagedStorageUtility` to register parents with vanilla managers while attaching our stock tag.
2. Replace bespoke `StockManagerComponent` hauling helpers with wrappers around vanilla `StoreUtility`/`HaulDestinationManager`, retaining only the cross-stock gating logic (capacity checks now ride on `StoreUtility.IsGoodStoreCell` and vanilla stack limits).
3. Patch hauling-related Harmony hooks to:
   - Block automatic jobs that cross stock boundaries.
   - Allow intra-stock better-storage moves driven by vanilla logic.
   - Treat tagged items as forbidden for routine consumption/bills (respecting player-forced requests via ForbidUtility patches and per-pawn tickets).
4. Update the cargo dialog workflow so load/unload requests rely on the new tagging/reservation pipeline.

## Phase 4 - Visual & UX Polish
1. Implement the gravship stock overlay (question-mark overlay drawn via OverlayDrawer for managed stacks).
2. Provide gizmo/feedback updates to inform players when cells/items are part of a separate stock.

## Phase 5 - Validation & Documentation
1. Rebuild the solution (`dotnet build RimworldGravshipCargo.sln`) and resolve compilation issues.
2. Smoke-test in RimWorld dev mode (manual steps documented if automation is infeasible).
3. Update `docs/GravloadFeatures.md` and README excerpts to describe the new behaviour and configuration.
4. Capture risk notes and follow-up tasks for broader "separate stock" scenarios (e.g. survival bunker).

## Deliverables
- Updated C# source integrating vanilla storage logic.
- Harmony patches reflecting the new gating rules.
- Plan + reference docs (`docs/OverhaulPlan.md`, `docs/vanilla-notes.md`).
- Verified build output ready for in-game testing.

