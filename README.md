# Gravload

Control the flow of resources between your RimWorld colony and its gravship. Gravload introduces managed stockpiles inside the gravship that stay isolated from everyday hauling, plus a dedicated loading workflow to stage cargo before liftoff.

## Current status
- [x] Rename scaffolding, namespaces, and project metadata to Deep_Gravload
- [x] Implement managed storage toggle and gravship management gizmos
- [x] Block haulers from touching managed stockpiles
- [x] Build gravship loading/unloading dialog and job logic
- [ ] (TODO) Integrate managed counts into resource readout
- [ ] Author translations, art, and Steam description

## Building
1. Ensure the RimWorld Odyssey assemblies live at C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed.
2. Run `dotnet build RimworldGravshipCargo.sln` (debug symbols are disabled by default).
3. Copy the produced DLL from gravload/1.6/Assemblies into your RimWorld Mods folder or link the repository directly.

Keep Dev Mode enabled to spot Harmony errors while features are still under construction.
