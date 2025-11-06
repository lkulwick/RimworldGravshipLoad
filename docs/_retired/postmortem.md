# Post Mortem – Separate Stocks Prototype

## What We Built
- Implemented a custom `StockManagerComponent` and gravload map component to tag storage parents and drive the gravship cargo window.
- Added Harmony patches to block cross-stock hauling, prevent pawns from touching gravship stock, and display a question-mark overlay on managed stacks.
- Swapped the cargo job driver over to vanilla hauling to support multi-stack shelves and reused `StoreUtility` helpers for capacity checks.

## What Worked
- The gravship cargo dialog could enqueue load/unload operations and kept overlays in sync when items moved between colony and ship.
- Stewarting overlays and cache rebuilds across save/load prevented save corruption and let us experiment with different integration points.
- Leveraged the RimWorld job pipeline (`PlaceHauledThingInCell`, `StoreUtility.IsGoodStoreCell`) so future work can continue to rely on vanilla behaviour.

## Pain Points
- Pawns entered runaway job loops when placement failed, spamming 10 jobs per tick and flooding the log.
- Autonomous “better storage” reshuffles never re-enabled, so priority changes on gravship shelves were ignored.
- Transfer bookkeeping stayed fragile: duplicate load checks triggered after unloads, unload destinations remained ad hoc, and failures left resources half-managed.
- The code diverged significantly from vanilla, making it difficult to reason about edge cases and update the mod alongside game patches.

## Why We’re Resetting
- Despite incremental fixes, we were still chasing regressions instead of converging on a stable design.
- The architecture drifted toward a full reimplementation of storage logic rather than the lightweight reuse we originally wanted.
- Starting fresh lets us define a minimal integration layer that leans on RimWorld’s systems first and only patches where absolutely necessary.

## Next Steps
- Re-evaluate requirements and sketch a thin tagging system that piggybacks on vanilla slot groups without custom hauling queues.
- Prototype intra-stock priority behaviour early to verify we can honour storage priorities before building new features.
- Keep documentation for future reference (this directory) but treat the upcoming iteration as a clean slate.
