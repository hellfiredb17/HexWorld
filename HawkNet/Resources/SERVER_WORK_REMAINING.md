# HawkNet Server Handoff

## Current State

The server has been refactored into a clearer set of systems:

- `GameHost` owns server startup/shutdown and starts the fixed simulation loop.
- `ConnectionManager` owns transport startup, connection acceptance, sessions, reconnect tokens, heartbeat/session timeout, and connection lifecycle for TCP and UDP.
- `ClientConnection` owns one transport connection, inbound packet reading, session assignment/reconnect handling, heartbeat watchdog deadline, and bounded outbound delivery.
- Connection lifecycle events now include `ConnectionCloseReason` so higher-level systems can distinguish shutdown, heartbeat timeout, session timeout, reconnect failure, and protocol-style disconnects.
- `PacketCounters` tracks total and per-packet-type inbound/outbound packet and byte counts, average size, and max size.
- `ServerDiagnostics` aggregates health, traffic, issue, timing, and chat metrics into snapshots with optional console logging.
- `UpdateLoop` exposes timing metrics for total ticks, tick duration, catch-up ticks, and skipped catch-up ticks.
- `Security.MaxInvalidPacketsPerConnection` controls how many unsupported packets a connection may send before being disconnected.
- `Security.MaxPacketsPerWindow` and `Security.PacketRateWindowSeconds` apply a broad per-connection packet rate limit.
- Malformed packet framing/read failures are counted in diagnostics and close the active session with `ConnectionCloseReason.ProtocolError`.
- `MessageDispatcher` owns outbound server-to-client packet delivery.
- `InboundMessageRouter` owns client-to-server gameplay packet routing.
- `EventDispatcher` replaced the old enum/object `EventBus` with typed async events.
- `PlayerIdentityService` owns current session display names and player identity metadata.
- `ChatService` owns bounded current-session chat history, enforces chat byte/message limits, and broadcasts structured chat payloads.
- `ILogger` and `NetLogger` provide network/server-side logging with levels, config filtering, and optional `[TAG]: ` message prefixes.
- `Services` and `IService` provide shared type-keyed service registration, async readiness lookup, and lifecycle hooks for future server/client service access.
- `GameStateManager` owns registered state factories, current active state lifecycle, and destination-based transitions from `GameMode` config.
- `UpdateLoop` is now a fixed-timestep background loop configured by `Simulation.TickRateHz` and capped by `Simulation.MaxCatchUpTicks`.
- `ChatLobbyState` snapshots current connected players, observes parsed chat messages, tracks ready state with opaque per-player payloads, broadcasts ready/countdown updates, and can request a configured transition after enough players are ready.

Current validation status:

- `dotnet build --no-restore` passes.
- Latest full solution verification passes with 519/519 tests and zero failures.

## Important Files

- `Core/Server/GameHost.cs`
- `Core/Server/ConnectionManager.cs`
- `Core/Server/ClientConnection.cs`
- `Core/Server/MessageDispatcher.cs`
- `Core/Server/InboundMessageRouter.cs`
- `Core/Server/PacketCounters.cs`
- `Core/Server/ServerDiagnostics.cs`
- `Core/EventDispatcher.cs`
- `Core/Server/ServerEvents.cs`
- `Core/Server/ChatService.cs`
- `Core/Server/PlayerIdentityService.cs`
- `Core/Server/GameStateManager.cs`
- `Core/Logging.cs`
- `Core/Services.cs`
- `Core/Server/GameState.cs`
- `Core/Shared/IGameState.cs`
- `Game/GameState/Lobby/ChatLobbyState.cs`
- `Core/UpdateLoop.cs`
- `Core/Server/ServerConfig.cs`
- `Config/game-server.json`
- `Tests/*`

## Completed Server Work

- Renamed `GameServer` to `GameHost`.
- Renamed server relay concept to `ConnectionManager`.
- Added TCP and UDP transport implementations behind the transport abstractions.
- Fixed server lifecycle so `GameHost.BeginAsync()` returns after startup.
- Made `ConnectionManager.StartAsync()` non-blocking and added clean `StopAsync()`.
- Preserved sender identity in server-side packet events: `playerId`, `PacketType`, `payload`.
- Applied core config values:
  - max players
  - max concurrent connections
  - allowed IPs
  - session timeout
  - heartbeat interval
  - next player id start
- Replaced reconnect-by-id with reconnect tokens.
- Added packet length and payload validation in `SignalShared`.
- Added explicit header-only packet helper for type-only messages with empty payloads.
- Added explicit `HandshakeRequest` / `HandshakeResponse` packet types and a shared handshake response envelope.
- Server handshake responses now include player id, reconnect token, and a client runtime manifest with module IDs, manifest/protocol versions, the initial game state id, and raw initial game-state payload bytes.
- Added active connection heartbeat watchdog using per-session deadlines.
- Fixed `UpdateLoop` busy spin and implemented fixed timestep with capped catch-up.
- Added `UpdateLoopMetricsSnapshot` for tick timing and catch-up observability.
- Split outbound message sending into `MessageDispatcher`.
- Added `InboundMessageRouter`.
- Replaced `EventBus<T>` with typed async `EventDispatcher`.
- Added `ChatService` with bounded ring-buffer history.
- Added `Chat.MaxMessageBytes` so oversized chat payloads are rejected before UTF-8 decoding.
- Added chat message rate limiting with `Chat.MaxMessagesPerWindow` and `Chat.MessageRateWindowSeconds`.
- Stubbed generic `IGameState` lifecycle and added an event-driven `GameState` base class.
- Added `ChatLobbyState`.
- Added chat-lobby ready state, bounded ready payload broadcasting, readiness threshold config, server-timed countdown config, and configured transition requests.
- Added match variant/phase configuration notes and defaults for the future reusable `match` game state.
- Wired `GameHost` to enter an active game state, call `Update(deltaTime)` from `UpdateLoop`, and exit/dispose the state on shutdown.
- Moved game-state factory registration behind the `HawkNetServer` builder so states can access current connection snapshots through `IHawkNetServerContext`.
- Added `ChatMessageLoggedEvent` so systems can react to parsed, validated chat entries instead of parsing raw chat payloads themselves.
- Updated `ChatLobbyState.Enter()` to subscribe to connection/chat events and `Exit()` to unsubscribe and clear state.
- Added `PlayerIdentityService` with default display names, configurable display-name length/format, name updates, and final-disconnect cleanup.
- Tightened display-name updates so `TrySetDisplayName` only updates known current-session identities and never creates unknown players.
- Added server-side `PacketType.DisplayNameUpdate` routing through `DisplayNameUpdateReceivedEvent`.
- Added `PlayerDisplayNameChangedEvent`.
- Updated `ChatService` to use `IPlayerIdentityService` for display names instead of hardcoding `Player {id}` internally.
- Added `ILogger`, `NetLogger`, and `NullLogger`.
- Replaced server-side `Console.WriteLine` usage in core server systems with logger calls.
- Cleaned server-side nullable event warnings in `ConnectionManager` and `ClientConnection`.
- Added `GameMode` config with explicit `InitialStateId` and destination allow-list state transitions.
- Added `GameStateManager` for dynamic game state registration, startup, update, shutdown, and destination-based transitions.
- Updated `GameHost` setup so builder-created hosts register state factories instead of requiring derived host subclasses.
- Added shared `Services` registry, `IService`, and `ServiceState` for future server/client service lifecycle and async readiness lookup.
- Tightened `ConnectionManager` session ownership by hiding the mutable session dictionary, making `nextPlayerId` private, exposing session snapshots/counts, and limiting session mutation to internal helpers.
- Added event-driven game state transition requests with completed/failed outcome events handled by `GameHost`.
- Standardized event dispatcher and game-state async APIs on `Task`; remaining `ValueTask` usage is limited to .NET-required async dispose signatures.
- Migrated `EventDispatcher` as the first `IService` implementation while preserving direct construction/use.
- Added `ConnectionCloseReason` and threaded close reasons through `ClientConnection`, `ConnectionManager`, `GameHost`, and typed connection lifecycle events.
- Added `PacketCounters` and wired inbound counting through `ClientConnection` plus outbound counting through `MessageDispatcher`.
- Extended `PacketCounters` with average and max packet size metrics.
- Added `ServerDiagnostics` snapshots for uptime, sessions, traffic, packet sizes, chat stats, timings, disconnect reasons, and update-loop metrics.
- Added optional diagnostics console logging through `Diagnostics.EnableConsoleLogging` and `Diagnostics.LogIntervalSeconds`.
- Added configurable invalid-packet tolerance and disconnect clients with `TooManyInvalidPackets` when they exceed it.
- Added configurable general packet rate limiting and disconnect clients with `PacketRateLimitExceeded` when they exceed it.
- Added malformed packet read diagnostics and clearer protocol-error logging.
- Added unsupported TLS warning and fail-fast guard if `Server.UseTls` is enabled.
- Added server service/game-state module config and validation.
- Added server-authored client runtime manifest in handshake responses.
- Added manifest/protocol version fields for client compatibility checks.
- Added versioned service/game-state module requirements to the client runtime manifest.
- Added server `IConfigService`/`ConfigService` as the shared lookup surface for framework settings.
- Added the first server/client `match` game-state modules, shared `MatchPayload`, server `IGameMatch`/`IGameMatchContext`, `GameMatchBase`, match variant registry, and server `MatchState` phase runner.
- Added async match operation queueing, server match lifecycle events, and `MatchPhaseChange` server-to-client packets.
- Added generic `GameStateMessage` packet/envelope routing so external game projects can own gameplay message IDs and payloads without adding gameplay logic to HawkNet.
- Added a reusable external `ClanIronSim.HawkNet` adapter project as a proof of the generic message lane and match-variant extension model.
- Added a game-neutral `HawkNet.CommandLine` host plus a `ClanIronSim.CommandLine` composition that starts directly in the `clan_iron` match variant and sends/receives generic game-state messages.
- Updated the ClanIron match adapter so temporary connection loss keeps the sim player/navigator alive; reconnects can continue sending movement commands for the same player.

## Current Event Model

Events live in `Core/Server/ServerEvents.cs`.

Current typed events:

- `PlayerConnectedEvent`
- `PlayerReconnectedEvent`
- `PlayerConnectionLostEvent` with `ConnectionCloseReason`
- `PlayerDisconnectedEvent` with `ConnectionCloseReason`
- `DisplayNameUpdateReceivedEvent`
- `PlayerDisplayNameChangedEvent`
- `PlayerInputReceivedEvent`
- `ChatMessageReceivedEvent`
- `ChatMessageLoggedEvent`
- `MatchCommandReceivedEvent`
- `MatchCommandRejectedEvent`
- `GameStateMessageReceivedEvent`
- `MatchPhaseChangedEvent`
- `MatchPlayerJoinedEvent`
- `MatchPlayerDisconnectedEvent`
- `MatchStartedEvent`
- `MatchEndedEvent`
- `GameStateTransitionRequestedEvent`
- `GameStateTransitionCompletedEvent`
- `GameStateTransitionFailedEvent`
- `SendPlayerMessageEvent`
- `BroadcastMessageEvent`

`EventDispatcher` behavior:

- typed events only
- async handlers
- `Task`-based dispatch and handler signatures
- sequential dispatch
- disposable subscriptions
- per-handler exception isolation
- cancellation support

Future systems should subscribe through `IEventDispatcher` instead of adding direct coupling to `GameHost`.

## ChatService Notes

`ChatService` currently:

- subscribes to `ChatMessageReceivedEvent`
- decodes inbound payload as UTF-8 text
- trims whitespace
- ignores empty messages
- trims messages longer than `Chat.MaxMessageLength`
- stores `ChatEntry` in a bounded ring buffer
- resolves display names through `IPlayerIdentityService`
- dispatches `ChatMessageLoggedEvent` after a message is parsed and added to history
- broadcasts new chat as JSON using `BroadcastMessageEvent(PacketType.ChatMessage, payload)`
- sends history on `PlayerConnectedEvent` and `PlayerReconnectedEvent`
- exposes `SendHistoryToPlayerAsync(int playerId)`

## Remaining Work

### Framework Backbone Priorities

HawkNet is intended to become a reusable server/client framework that can be exported into other projects and Unity. Keep the core server code plain C# and make framework extension points deliberate instead of tying systems to one game.

Near-term priorities:

- Transport evolution: TCP and protocol-v3 UDP use shared delivery-aware transport abstractions. UDP provides reliable-ordered and unreliable-sequenced channels without coupling game systems to socket details.
- Payload abstraction: the `IPayloadCodec` boundary and generic `GameStateMessage` lane are in place. Add binary/message-packing work only when a consuming project needs it.
- Optional server modules: keep chat, player identity, diagnostics, and game states as built-in modules that can be replaced or omitted by a host project. Avoid making every project inherit chat/lobby assumptions.
- Client runtime manifest: server handshakes now publish enabled service modules, game-state modules, initial state, and initial payload so the client can enable matching local modules before entering gameplay state.
- Public API cleanup: treat `HawkNetServer.Create`, `GameHost`, `IEventDispatcher`, `IService`, game-state registration, and transition requests as stable extension points. Push packet framing and session mutation behind internal or narrowly scoped APIs.
- Scoped services: `GameHost` now owns an instance registry for framework services while the static `Services` facade remains available for simple/default apps.

How to approach later:

- Keep future transport work behind explicit delivery options and transport implementations rather than embedding socket assumptions in game messages.
- Continue payload work by adding richer game-state payload contracts as new states need them. The codec boundary, `chat_lobby` payload path, and generic game-state message path are in place.
- Continue scoped-service migration by moving more systems to the instance registry where it reduces global coupling.

### 1. Game State Integration

`GameHost` now owns a `GameStateManager`. Config defines a `GameMode` graph with an explicit initial state and per-state destination allow-lists, while `HawkNetServer.Create(...)` builder configuration registers concrete states by stable keys. Systems can request transitions by dispatching `GameStateTransitionRequestedEvent` with a target state id and optional handoff payload; `GameHost` emits completed/failed outcome events. `ChatLobbyState` is the first simple state. It asks `IHawkNetServerContext` for currently connected players when it enters, then subscribes to connection lifecycle events and `ChatMessageLoggedEvent`.

Completed:

- Expanded `match` with the external `ClanIronSim.HawkNet` `clan_iron` variant example.
- Added public-style examples for registering custom `IGameMatch` implementations through an external module.

Needed:

- Keep `GameMatchBase` handling common chat and player connection subscriptions while concrete match types extend behavior.
- Continue improving headless console server/client samples as more projects consume the generic game-message lane.
- Keep match server handoff payloads separate from client-facing match payloads.
- Track late joins/reconnects and mark dropped players disconnected instead of removing them. The reusable match layer and ClanIron adapter now preserve temporary disconnects; continue validating final timeout/removal behavior as persistence rules mature.
- Decide what lobby behavior should do beyond tracking connected players and observing chat activity.
- Add transition diagnostics/history if state flow becomes harder to inspect.

### 2. Player Identity / Display Names

`PlayerIdentityService` now provides the first pass at player display names. It creates default names from config, allows server-side updates for known current-session identities, handles inbound `DisplayNameUpdateReceivedEvent`, dispatches `PlayerDisplayNameChangedEvent`, and removes identity records when a player session finally expires.

Needed:

- Decide whether display names should persist outside the current server session.
- Add stronger validation rules for duplicate names, reserved names, profanity, and impersonation.
- Decide whether identity should be tied to auth/account data later.

### 3. Session Ownership / Thread Safety Cleanup

Initial ownership cleanup is complete:

- `PlayerSession` now exposes properties with internal mutation.
- `ConnectionManager.Sessions` is no longer public.
- `ConnectionManager.nextPlayerId` is private.
- Public callers can query `SessionCount`, `TryGetSessionSnapshot`, `IsPlayerConnected`, and reconnect payloads without owning session mutation.
- Session mutation, reconnect replacement, connection-lost marking, timeout removal, activity updates, and connected-player/connection snapshots now go through a lock-protected `ConnectionManager` boundary.
- `MaxPlayers` session creation is guarded against concurrent first handshakes.
- Session timeout removal is conditional so a session that reconnects or refreshes activity before removal is preserved.
- Replaced/stale connections can no longer refresh activity or route packets for a session they no longer own.
- Added regression tests for concurrent handshakes, reconnect before timeout cleanup, and stale replaced-connection packet routing.

Needed:

- Consider removing the internal session registration helper if tests later move to full connection flows.

### 4. Reliable UDP Status And Follow-ups

Config supports:

```json
"Networking": {
  "Protocol": "Udp"
}
```

TCP and protocol-v3 UDP are implemented. UDP maps remote endpoints to bounded virtual connections and supports stateless desktop discovery.

Completed:

- `ReliableOrdered` and `UnreliableSequenced` modes on Control, Gameplay, and Snapshot channels.
- Sequence/acknowledgement history, duplicate suppression, bounded reorder/send windows, delayed ACKs, adaptive RTO, Karn RTT sampling, capped retries, and `ReliableDeliveryFailed` shutdown.
- A strict 1,200-byte datagram budget, application-packet timestamps, and fresh outer-envelope timestamps for sends, retransmissions, and ACK-only traffic.
- Central packet delivery defaults plus explicit client/server send overrides.
- UDP delivery configuration and validation in bundled JSON, CLI/environment overrides, Desktop server/client setup screens, diagnostics, and client CLI metrics.
- Deterministic impairment tests and real-loopback TCP/UDP integration coverage.

Still needed:

- Run the opt-in sustained LAN soak with 64 mixed-traffic peers, reconnect churn, malformed clients, and deliberately slow receivers.
- Add fragmentation/reassembly only if real game payloads cannot remain inside the 1,200-byte budget.
- Add authenticated NAT endpoint migration before supporting roaming or internet clients.
- Add encryption and adaptive pacing/congestion control in a later public-internet milestone.

### 5. TLS Transport

Config includes:

```json
"Server": {
  "UseTls": false,
  "CertificatePath": ""
}
```

But TLS is not implemented. If `UseTls` is enabled, `ConnectionManager` logs that TLS is unsupported and fails fast instead of starting an unencrypted server.

Needed:

- Design server certificate loading, dev certificate support, and certificate password handling.
- Wrap accepted TCP streams with `SslStream`.
- Decide whether TLS terminates inside HawkNet or in front of it through a proxy/load balancer.

### 6. Logging / Observability

The server now has a lightweight `ILogger` abstraction with `NetLogger` console output. It supports `Log`, `DebugLog`, `Warning`, `Error`, and `Exception`, keeps config filtering, and prefixes tagged messages as `[TAG]: message`.

Completed:

- Added connection close reasons to server-side disconnect logs and typed lifecycle events.
- Added packet counters for total and per-packet-type inbound/outbound packets and bytes.
- Added packet average/max size metrics.
- Added update-loop tick timing metrics, catch-up counts, cap-hit counts, and skipped catch-up tick counts.
- Added `ServerDiagnostics` for aggregate snapshots and optional periodic console summaries.

Needed:

- Add deeper per-service diagnostics if needed, such as event dispatcher handler timing/failure counts.
- Consider structured logs with categories per system.
- Consider adapting to `Microsoft.Extensions.Logging` if HawkNet becomes hosted by a generic host or ASP.NET style runtime.

### 7. Security / Abuse Hardening

Reconnect tokens are implemented, but more hardening is needed.

Completed:

- Added `Security.MaxInvalidPacketsPerConnection`.
- Added `Security.MaxPacketsPerWindow` and `Security.PacketRateWindowSeconds`.
- Unsupported packet types now increment a per-connection invalid-packet counter.
- Connections that exceed the invalid-packet limit are disconnected with `ConnectionCloseReason.TooManyInvalidPackets`.
- Connections that exceed the general packet rate limit are disconnected with `ConnectionCloseReason.PacketRateLimitExceeded`.
- Malformed packet framing/read failures are counted in `ServerDiagnostics.MalformedPacketReads` and close established sessions with `ConnectionCloseReason.ProtocolError`.
- Added `Chat.MaxMessageBytes` and reject oversized chat payloads before UTF-8 decoding.
- Added chat message rate limiting per player and clear rate-limit state on final disconnect.

Needed:

- Auth/account identity layer.
- Better handling for unknown packet types.
- TLS or trusted-proxy termination before public-internet hosting.
- Authenticated endpoint migration and encryption before using UDP for critical commands over untrusted networks; reliable/ordered channels themselves are complete.
- Optional moderation/filter hooks for chat. Keep this last unless chat abuse becomes a near-term problem.

### 8. Nullable / Code Hygiene

Remaining known server/shared cleanup:

Needed:

- Remove stale comments and old terminology.

### 9. ChatService Future Enhancements

Implemented v1 is intentionally simple.

Future options:

- Add message ids.
- Add a ping service using header-only packets.
- Research message packing/batching for multiple logical messages in one packet.
- Add channels/rooms.
- Add system messages.
- Add moderation hooks.
- Add server-side command parsing.
- Add richer history sync packet type instead of overloading `PacketType.ChatMessage`.
- Add binary or versioned payload format if JSON becomes too loose.

### 10. Services Registry Migration

`Services` is now available as shared `Core` infrastructure. `EventDispatcher` is the first pilot service and implements `IEventDispatcher, IService`; most existing systems still use constructor/manual ownership for now.

Needed:

- Avoid moving everything at once; migrate only systems that benefit from shared lookup.
- Add dependency cycle guidance before services start calling `Services.GetAsync<T>()` during initialization.
- Decide whether named service keys are needed later for multiple instances of the same service type.
- Consider scoped service registries later if tests or multiple server instances need isolation beyond the current static reset helper.

## Recommended Next Steps

Suggested order from here:

1. Complete the ClanIron World Builder in its integration project.
2. Expand integration coverage and run sustained LAN soak testing.
3. Plan payload/codec improvements only when a consuming game needs them.
4. Review remaining session lifecycle and public API cleanup.
5. Add auth/account identity, TLS or trusted proxy termination, and UDP internet hardening before public hosting.
6. Add moderation/filter hooks last unless chat abuse becomes urgent.

If continuing in another Codex chat, start by reading this file plus:

- `Core/Server/GameHost.cs`
- `Core/Server/ConnectionManager.cs`
- `Core/Server/ChatService.cs`
- `Core/EventDispatcher.cs`

Then run:

```powershell
dotnet build --no-restore
dotnet test --no-restore
```
