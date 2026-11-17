# ClanIronSim TODO

This project is the standalone game simulation library. It must stay free of HawkNet, CoreUx, Desktop, CommandLine, and other project references.

## Next Planning Topics

- World update systems
- Authoritative state model
- Interaction execution model
- Combat/health/damage systems
- Active section update strategy
- World authoring tools
- Hex production geometry

## Player Model

- [x] Create `PlayerProfile` for persistent/common player identity.
- [x] Include common display data such as display name.
- Include game tuning data such as:
  - total HP
  - movement speed
  - rotation speed
  - future combat/stat values
- [x] Create separate `PlayerRuntime` data for current control links.
- [x] Store current controlled navigator ID in `PlayerRuntime`.
- Keep navigator movement state on `NavigatorEntity`:
  - position
  - forward direction
  - is moving
  - current path state
- Add player-owned runtime data later only when it is not already owned by navigator/entity state.

## First Playable World Loop

- [x] Load a `WorldState` snapshot with a simple `WorldMap`.
- [x] Add a user-controlled navigator entity model.
- [x] Place the user-controlled navigator into the world through `GameOverlord.TryAddPlayer(...)`.
- [x] Convert user world-space selection input into `MapLocation`.
- [x] Use `WorldState` pathfinding to validate movement targets.
- [x] Move the user-controlled navigator along a valid path.
- [x] Keep this loop framework-agnostic inside `ClanIronSim`; HawkNet/CoreUx/Desktop can wrap it later.
- [x] Add a separate HawkNet adapter layer that wraps the sim without adding HawkNet references to `ClanIronSim`.
- Defer interaction execution until the interaction system design is clearer.

## Milestone 1 Follow-Ups

- [x] Verify match initialization spawns connected players through `GameOverlord.TryAddPlayer(...)` and skips disconnected payload players.
- [ ] During manual Desktop repro, confirm player spawn log lines appear for each connected match player.
- [ ] Tune default navigator/player movement values after desktop playtesting:
  - movement speed
  - rotation speed
  - arrival tolerance if needed
- [ ] Decide server movement update cadence:
  - keep status/path updates only for M1 unless playtesting needs more frequent corrections
  - avoid broadcasting every sim tick unless debugging proves it is necessary
- [x] Replace old movement status `Pending` with clearer statuses:
  - `Idle` means no movement is active.
  - `Continuing` means a movement node was reached and the path is still active.
- [x] Keep navigator `CurrentWorldPosition` when movement is canceled or replaced so mid-move client movement does not snap back to the last tile center.

## GameOverlord

- [x] Create a `GameOverlord` class as the main bridge between players and `WorldState`.
- [x] Own or coordinate the main game loop update hook.
- [x] Accept and queue commands from players.
- [x] Process queued commands during the next update tick.
- [x] Update world simulation.
- [x] Return emitted `SimAction` items from `UpdateAsync(...)` so host wrappers can send them immediately.
- [x] Load a ready runtime host from a `WorldState` snapshot.
- [x] Add helper APIs for adding/removing players and spawning controlled navigators.
- Coordinate player interactions with map tiles, map sections, interactables, and other players after the interaction model is designed.
- Broadcast major gameplay actions/events as systems are added, such as:
  - [x] player/navigator moved
  - [ ] player hit
  - [ ] player died
  - [ ] interactable used
- Keep this class framework-agnostic. HawkNet/Desktop/CommandLine wrappers can translate its outputs into network payloads later.

## Sim Command / Action Checklist

- [x] Add `SimRequest` for immediate API/query/session-style work.
- [x] Add `SimResponse` so every request returns a response, including empty responses.
- [x] Add `SimCommand` for tick-processed gameplay input.
- [x] Queue commands and process them in server enqueue order during `GameOverlord.UpdateAsync(...)`.
- [x] Add `SimAction` for outgoing gameplay events generated during the sim update.
- [x] Add action routing intent:
  - broadcast
  - region
  - player
- [x] Return each tick's emitted actions from `GameOverlord.UpdateAsync(...)`.
- [x] Add command feedback actions:
  - command accepted
  - command rejected
  - move command failed
- [x] Add movement status actions:
  - move started
  - waypoint reached
  - move completed
  - move canceled
  - move failed
- [x] Add player-to-navigator translation:
  - map `PlayerId` to controlled `NavigatorEntity`
  - stop requiring trusted clients to send both `PlayerId` and `NavigatorId`
- [x] Add a sim event dispatcher/action sink:
  - let systems/entities emit actions without directly depending on `GameOverlord`
  - keep context framework-agnostic
- [ ] Add explicit world update systems as behavior grows:
  - movement system
  - interaction system
  - health/damage system
  - destruction/cleanup system
- [x] Add request/response/command/action JSON options:
  - enum serialization as strings
  - compatibility tests before networking consumes them
- [ ] Add envelope versioning if/when network compatibility requires it.

## World Data And Snapshot Loading

- [x] Use `WorldMap` JSON as reusable map/world data.
- [x] Use `WorldState` JSON as the full game snapshot format.
- [x] Include `PlayerService` profile/runtime data in `WorldState`.
- [x] Load `GameOverlord` from `WorldState` JSON or file.
- [x] Add authored starter snapshot data for Milestone A.
- [x] Store hosted starter data under `Resources/WorldData`.
- [x] Add player spawner tile attributes for join-time navigator spawning.
- [x] Copy starter world data into the command-line host output for manual testing.

## WorldState Expansion

- Keep `WorldMap` focused on geometry, tiles, lookup, and navigation.
- Use `WorldState` as the owner for player profiles/runtimes, entities, interactables, and other live/saveable world data.
- Add a way to update only active sections of the map.
- Active sections should be based on player presence or proximity so the sim does not update map areas where no player is nearby.
- Define how player/world position maps to active `MapSection` update sets.
- Define how interactables are stored, queried, and updated per section.

## Entity Work Checklist

- [x] Add entity placement helpers on `WorldState`:
  - `TryPlaceEntity(...)` for static/base entities.
  - `TryPlaceInteractable(...)` for interactables.
  - Resolve target region, placed section, tile grid, and cached world center through `WorldMap`.
- [x] Add interactable movement helpers:
  - `TryMoveInteractable(...)`.
  - Reject movement to missing regions, sections, or tiles.
  - Refresh cached world position from the destination tile center.
- [x] Add entity/interactable validation:
  - Detect duplicate world-unique IDs.
  - Validate that stored entity locations resolve to real map tiles.
  - Validate that cached world positions match tile centers after placement/movement.
- [x] Add typed attribute query helpers:
  - `TryGetAttribute(key, subtype, out EntityAttribute?)`.
  - Numeric, boolean, and text accessors for common gameplay reads.
  - Shared constants for common keys/subtypes once they stabilize.
- [x] Add entity occupancy queries:
  - Get entities/interactables at a location.
  - Get all occupants at a tile.
  - Keep lookup list-based until a spatial index is needed.
- [x] Add entity-driven movement rules:
  - Terrain blocking stays on `WorldMap`/tiles.
  - Entity or interactable blocking lives in `WorldState`.
  - Add `WorldState` pathing wrappers that use `WorldMap` navigation and apply entity blocking overlays.
- [ ] Add interaction model later:
  - Query interactables in range.
  - Check whether an actor can interact with a target.
  - Design `TryInteract(...)` and an interaction result object after the system design is clearer.
  - Return interaction result data without tying to HawkNet/CoreUx/Desktop.
- Add runtime actor layer later:
  - `ActorEntity` for things that can act.
  - Future `PlayerEntity` and `NpcEntity`.
  - Keep active behavior in systems/services, not directly inside base entity data.

## World Creation Tooling

- [x] Isolate the Avalonia authoring integration in `ClanIronSim.CoreUx` so this simulation library remains UI/framework-free.
- [x] Add standalone and Desktop-hosted prototype tile/section editors with JSON load/save, previews, painting, and session apply results.
- [ ] Create the integrated tile, section, region, and world document workflow.
- [ ] Support authoring reusable map pieces that can be assembled into a full world piece by piece.
- [ ] Support saving/loading complete worlds in addition to the existing individual tile/section prototype actions.
- [ ] Make rectangle/hex geometry world-owned, including tile size, shape, orientation, attributes, and placement offsets.
- [ ] Complete validation, overlap detection, preview, and persistence tests.

Detailed implementation scope and current handoff status live in `../ClanIronSim.CoreUx/Resources/MILESTONE2_WORLD_BUILDER_TODO.md`.

## World Geometry / Hex Readiness

- Keep rectangle and hex geometry APIs covered as shape-specific behavior grows.
- [x] Add map/region-level neighbor and pathing helpers across placed section boundaries.
- [x] Add impassable/blocking terrain rules for movement and pathing.
- Add richer movement cost policies when terrain, units, or interactables need separate cost layers.
- Add shape-specific map generation helpers beyond rectangular section generation.
- Add future JSON migrations when geometry version 2 is needed.

## Open Design Questions

- [x] Decide whether player runtime data lives directly on `Player` or is owned separately by `GameOverlord`.
- [x] Decide whether `WorldState` owns player lists directly or exposes player lookup helpers through a dedicated registry.
- [x] Decide the command format for player input before implementing command handling.
- [x] Decide the broadcast/event shape before host wrappers consume it.
- Decide navigator cleanup/lifecycle policy for disconnected players after persistence rules are clearer.
- Decide richer region subscription/visibility rules for network action targeting.
