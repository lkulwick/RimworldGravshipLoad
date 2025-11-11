# Gravload

Gravload separates your gravship cargo from the everyday clutter of the colony. Mark a stockpile or storage building as "separate stock" and only gravship orders will touch it. Colonists can fill the ship, pull goods back out, and keep emergency supplies safe without micromanaging forbidden items.

## Features
- **Separate-stock switch** - Use the new gizmo on any stockpile or storage building to include or exclude it from the gravship inventory.
- **Simple transfer window** - Queue load (colony -> stock) and unload (stock -> colony) jobs with the same `TransferableOneWay` UI used by caravans.
- **Pending list** - See every stack that pawns are already hauling and cancel individual entries if plans change.
- **Safety mode** - Turn off "Allow pawn auto use and reorganize" so colonists and animals ignore the protected cells unless you give a direct order.

## Getting Started
1. Decide which stockpiles or storage buildings belong to the gravship.
2. Toggle **Separate stock** on those stores (or use the ship's management gizmo).
3. Open **Manage stock** to set load/unload counts. The bottom list shows pending stacks; cancel any that no longer fit.

## Build From Source
1. Make sure RimWorld's assemblies are available at  
   `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed`.
2. Run `dotnet build RimworldGravshipCargo.sln`.
3. Copy `gravload/1.6/Assemblies/Deep_Gravload.dll` (and other changed files) into your RimWorld Mods folder, or symlink the repo for faster testing.

## Compatibility
- Target game version: **RimWorld 1.6**.
- Works with vanilla storage rules. Deep/virtual storage mods may need extra patches so their slot groups are detected.
- Only pawns with the Hauling work type get separate-stock jobs; animals never run them.
- Keep Dev Mode on while testing so Harmony errors pop up immediately.

## Roadmap
- Show separate-stock totals in the resource readout.
- Add more translations and proper Workshop art.
- Publish the Steam page with screenshots and guides.

Feedback and pull requests are welcome. If you hit a bug or have an idea to simplify the workflow, open an issue or PR.
