# Milestone 2 TODO: World Builder Tooling

Milestone 2 goal: build a ClanIron integration tool for authoring `WorldMap` data.

This tool is for creating world geometry and map building blocks only. It should not manage players, runtime match state, movement tuning, combat, interactions, or `WorldState` snapshot data.

## Handoff Status (2026-09-06)

Completed foundation:

- [x] ClanIron-specific tooling is isolated in `ClanIronSim.CoreUx`; reusable `CoreUx` has no ClanIron dependency.
- [x] The optional `ClanIronWorldBuilderTool` opens from both server and client menus and returns through the generic tool close callback.
- [x] Standalone `tile-editor` and `section-editor` startup modes work without starting a server or client.
- [x] Tile and section editors have local navigation, JSON load/save actions, session apply results, rectangle/hex previews, and a paintable section canvas.
- [x] Tests cover server/client tool discovery and navigation, applied tile/section results, close behavior, and standalone editor modes.

Still required for the milestone:

- [ ] Replace the current editor-demo/session model with a real open `WorldMap` document and dirty/file-path state.
- [ ] Implement complete New/Open/Save/Save As workflows for a whole world.
- [ ] Add the World and Region authoring surfaces and assemble reusable tiles/sections into the document.
- [ ] Make tile geometry world-owned and consistently applied to sections instead of independently edited per tile/section.
- [ ] Add document-level validation, overlap checks, full-world preview, and comprehensive authoring persistence tests.

The checklist below remains the detailed implementation scope for the next World Builder task.

## New Chat Starting Point

Start by reading:

- `ClanIronSim.CoreUx/WorldBuilder/ClanIronWorldBuilderSession.cs`
- `ClanIronSim.CoreUx/WorldBuilder/ViewModels/MapTileEditorViewModel.cs`
- `ClanIronSim.CoreUx/WorldBuilder/ViewModels/MapSectionEditorViewModel.cs`
- `ClanIronSim/World/WorldMap.cs` and related map definition types
- `HawkNet.Tests/Tests/ClanIronWorldBuilderToolTests.cs`

Preserve the existing generic `ICoreUxTool` boundary and keep all ClanIron domain/UI adaptation in `ClanIronSim.CoreUx`. The verified solution baseline before starting this milestone is 519/519 passing tests.

## Current Direction

- [x] Add a World Builder entry point from the main menu.
- [x] Keep game-specific UI/UX views in the `ClanIronSim.CoreUx` integration project.
- [x] Treat this as a separate tool flow for now, even though it initially opens from the current Desktop/CoreUx shell.
- [ ] Load existing `WorldMap` JSON files.
- [ ] Create new `WorldMap` data from scratch.
- [ ] Save edited `WorldMap` JSON files.
- [x] Reuse the game world's existing domain types instead of creating duplicate editor-only world models unless a temporary view model is needed for UI state.

## World Builder Shell

- [x] Create a main World Builder view.
- [ ] Add tabs for:
  - [ ] World
  - [ ] Region
  - [ ] Section
  - [ ] Tile
- [ ] Show current open file/path or unsaved-new-world state.
- [ ] Add commands for:
  - [ ] New world
  - [ ] Open world
  - [ ] Save world
  - [ ] Save world as
- [ ] Add visible validation/status messaging for load/save/edit failures.
- [x] Keep the tool usable without starting a server or client.

## World UI

- [ ] Create a World tab for world-level settings and full-map assembly.
- [ ] Edit world-level metadata:
  - [ ] id
  - [ ] name
  - [ ] description
  - [ ] geometry version display
- [ ] Control global tile geometry from the World tab:
  - [ ] tile shape: rectangle or hex
  - [ ] tile width/size X
  - [ ] tile height/size Z
  - [ ] hex orientation when tile shape is hex
- [ ] Apply world tile geometry consistently to newly created tiles and sections.
- [ ] Show available regions.
- [ ] Add/remove regions from the world.
- [ ] Prevent or clearly flag overlapping regions.
- [ ] Show full-world bounds/preview.
- [ ] Validate world-level region placement, required fields, and tile geometry settings.

## Tile UI

- [ ] Create a Tile tab for defining reusable tile building blocks.
- [ ] Do not control tile shape or tile size here; those are world-level settings.
- [ ] Track tile preset properties needed by existing map data:
  - [ ] display/name or preset id
  - [ ] movement cost
  - [ ] blocking/passable state
  - [ ] attributes
- [ ] Allow creating named tile presets/building blocks.
- [ ] Allow editing existing tile presets.
- [ ] Allow deleting tile presets that are not currently used, or clearly block deletion when used.
- [ ] Ensure tile presets can be applied to rectangle or hex worlds through the active world geometry settings.

## Section UI

- [ ] Create a Section tab for building map sections from tile presets.
- [ ] Allow creating a new section with width/depth settings.
- [ ] Generate section cells using the active world tile shape and size.
- [ ] Show a paintable section grid.
- [ ] Add tile preset selection.
- [ ] Paint selected tile preset onto grid cells.
- [ ] Support marking cells passable/blocking through tile attributes or selected tile preset.
- [ ] Allow selecting a cell and inspecting/editing its tile data.
- [ ] Save section data into the current `WorldMap` structure.
- [ ] Validate that section tiles have valid grid positions, local positions, world geometry, and attributes.

## Region UI

- [ ] Create a Region tab for placing sections together.
- [ ] Allow creating, selecting, renaming, and deleting regions.
- [ ] Show available sections.
- [ ] Place selected sections into the region on a region layout grid/canvas.
- [ ] Move placed sections by grid position or offset.
- [ ] Prevent or clearly flag overlapping placed sections.
- [ ] Show region bounds.
- [ ] Validate placed section ids, grid positions, offsets, and overlap rules.

## Validation And Preview

- [ ] Add validation for missing ids, duplicate ids, invalid dimensions, empty sections, empty regions, overlaps, and invalid tile data.
- [ ] Add a read-only preview using or adapting the existing world map rendering approach where practical.
- [ ] Show blocked/passable tiles distinctly.
- [ ] Show selected tile/section/region details.
- [ ] Keep validation local to world authoring and independent of player/runtime systems.

## Tests

- [ ] Add tests for creating a new `WorldMap`.
- [ ] Add tests for loading and saving `WorldMap` JSON.
- [ ] Add tests for applying world-level tile shape and size to generated sections.
- [ ] Add tests for section grid generation.
- [ ] Add tests for tile painting/edit persistence.
- [ ] Add tests for region section placement and overlap validation.
- [ ] Add tests for world region assembly and overlap validation.
- [ ] Confirm existing server/client movement tests are unaffected.

## Deferred Beyond Milestone 2

- [ ] `WorldState` snapshot export.
- [ ] Player spawn/runtime setup.
- [ ] Interactable behavior.
- [ ] Combat/health/damage systems.
- [ ] Active section simulation updates.
- [ ] Unity-specific tooling.
- [ ] Standalone external editor app packaging.
