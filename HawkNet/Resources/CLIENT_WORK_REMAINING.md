# HawkNet Client Work Handoff

## Current Focus

Client v1 and the protocol-v3 UDP delivery pass are complete. The client now supports TCP/UDP connection lifecycle, reconnect tokens, heartbeat, typed routing, chat/player/state services, reliable-ordered and unreliable-sequenced delivery, configuration overrides, Desktop setup controls, and UDP metrics.

Remaining client work is primarily validation at scale and future deployment hardening:

- Run sustained mixed-traffic LAN soak testing with reconnect churn and slow receivers.
- Tune the Desktop world viewer after playtesting.
- Add fragmentation, authenticated NAT rebinding, encryption, and congestion control only when deployment requirements justify them.
- Keep future game behavior in consuming integrations rather than HawkNet client core.

## Important Current Files

- `Core/Client/GameClient.cs`
- `Core/Client/GameClientConfig.cs`
- `Core/Client/ClientEvents.cs`
- `Core/Client/ClientNetworkMessageHandler.cs`
- `Core/Client/ClientChatService.cs`
- `Core/Client/ClientPlayerService.cs`
- `Core/Client/ClientGameState.cs`
- `Core/Client/ClientGameStateManager.cs`
- `Core/Client/ClientChatLobbyState.cs`
- `Core/Client/HawkNetClient.cs`
- `Core/Shared/ChatProtocol.cs`
- `Core/Shared/GameStateProtocol.cs`
- `Tests/ClientServerChatIntegrationTests.cs`
- `Core/Shared/SignalShared.cs`
- `Core/Server/ConnectionManager.cs`
- `Core/Server/ClientConnection.cs`
- `Core/Server/ChatService.cs`
- `Core/Server/ServerEvents.cs`
- `Core/EventDispatcher.cs`
- `Core/Services.cs`
- `Config/game-server.json`
- `Tests/*`

## Server Protocol To Match

The server currently expects:

- TCP and protocol-v3 UDP transports with `ReliableOrdered` and `UnreliableSequenced` delivery.
- Packet framing from `SignalShared`.
- Header-only packets are valid for type-only messages.
- Client sends `PacketType.HandshakeRequest` on a fresh TCP connection.
- Server creates/assigns the session from the handshake request.
- Server replies to a new connection with `PacketType.HandshakeResponse`.
- That handshake response payload is a shared JSON `HandshakeResponse` envelope:
  - `PlayerId`
  - `ReconnectToken`
  - `Runtime`
    - `ManifestVersion`
    - `ProtocolVersion`
    - `Services`
    - `GameStates`
    - `ServiceModules`
    - `GameStateModules`
    - `InitialGameStateId`
    - `InitialGameStatePayload`
- Reconnect requests must send the same payload shape:
  - `playerId + reconnectToken`
- Server replies to a successful reconnect with `PacketType.HandshakeResponse`.
- Heartbeat should use `PacketType.HeartBeat`.
- Heartbeat can be header-only because the server knows the sender from the active TCP connection.
- Chat payloads from the server are structured JSON:
  - `ChatEntry` for a single new message
  - `ChatEntry[]` for current chat history
- Display-name updates use `PacketType.DisplayNameUpdate` with a UTF-8 payload.
- Server-driven game-state transitions use `PacketType.GameStateChange`.
- `GameStateChange` payload is a shared JSON `GameStateChangeResponse` envelope:
  - `GameStateId`
  - `GameStatePayload`

## Client V1 Plan

### Framework Backbone Priorities

HawkNet is intended to become a reusable client/server backbone for plain C#, other game projects, and Unity. Keep the core client code Unity-free and make `HawkNetClient` the framework runtime that external projects compose around.

Near-term priorities:

- Custom app composition: keep app/UI bootstrapping outside the framework while `HawkNetClient` owns internal service startup, default modules, events, and state registration.
- Client transport evolution: TCP and reliable/sequenced UDP are implemented behind `IClientTransport`. Future TLS, WebSocket, and Unity-specific adapters should preserve that boundary.
- Payload abstraction: let each game state or module own its payload parsing through a codec/serializer boundary. JSON can remain the default v1 format, but the client should not assume every future payload is directly decoded inside networking code.
- Optional client modules: keep chat, player data, network message handling, and state management as built-in modules that can be replaced or omitted by a host project or Unity game.
- Unity adapter: add Unity support as a separate adapter layer later, focused on main-thread dispatch, MonoBehaviour lifecycle, ScriptableObject/config loading, and UI binding helpers.
- Scoped services: keep static `Services` for simple app access, but plan an instance-owned service registry so multiple clients, tests, and Unity scenes can run without leaking global state.

How to approach later:

- Keep new transport behavior behind the existing delivery-aware abstraction so game-state code remains independent of TCP/UDP details.
- Start payload work by adding typed encode/decode helpers for shared messages, then migrate chat and game-state payloads before adding binary packing.
- Start Unity work only after the plain C# runtime remains clean; the Unity layer should consume `HawkNetClient`/services rather than making core types depend on Unity.

### 1. Rename Client API

Status: Complete.

Planned:

- Renamed `RelayHub` to `GameClient`.
- Made `GameClient` the top-level client networking object that pairs conceptually with server `GameHost`.
- Updated tests and references.

Open choice:

- No temporary compatibility wrapper was kept. The old `RelayHub` name should be considered removed.

### 2. Add Client Settings JSON

Status: Complete.

Added files:

- `Config/game-client.json`
- `Core/Client/GameClientConfig.cs`

Suggested settings:

```json
{
  "Server": {
    "Host": "127.0.0.1",
    "Port": 7777
  },
  "Networking": {
    "HeartbeatIntervalSeconds": 15,
    "ReconnectEnabled": true,
    "ReceiveBufferSize": 8192,
    "SendBufferSize": 8192,
    "NoDelay": true
  }
}
```

Future settings:

- TLS options after TLS is designed.
- Automatic reconnect policy and retry backoff.
- Diagnostics/logging settings.

### 3. Match Server Protocol

Status: Complete for v1.

Completed:

- Store assigned `PlayerId`.
- Store reconnect token from server confirmation.
- Parse server `HandshakeResponse`.
- Validate the server-sent client runtime manifest version and protocol version.
- Enable client service modules from `Runtime.Services`.
- Enable client game-state modules from `Runtime.GameStates`.
- Store current state from `Runtime.InitialGameStateId`.
- Store raw current state payload from `Runtime.InitialGameStatePayload`.
- Build reconnect payload as `playerId + reconnectToken`.
- Support sending header-only packets through shared `SignalShared.BuildHeaderOnlyPacket`.
- Keep wire format unchanged.
- Fresh connects send a header-only `PacketType.HandshakeRequest` as the server-session trigger.

Tests:

- Client connects and receives/stores player id.
- Client receives/stores reconnect token.
- Client reconnect payload matches server expected size and shape.
- Client receives/stores current game state id and raw state payload.

Completed:

- `HandshakeResponse.Runtime` now carries the server-authored client runtime manifest.
- `ClientRuntimeManifest` includes `ManifestVersion` and `ProtocolVersion`.
- `ClientRuntimeManifest` includes versioned `ServiceModules` and `GameStateModules` requirements.
- Unknown, misplaced, duplicate, or dependency-invalid client modules fail clearly during connect.
- Unsupported service/game-state module versions fail clearly during connect.
- Game-state systems parse their own initial and transition payloads through the payload codec.

### 4. Connection Lifecycle

Status: Complete for v1.

Implemented API shape:

- `ConnectAsync()`
- `DisconnectAsync()`
- `ReconnectAsync()`
- `IsConnected`
- `PlayerId`
- `State`
- `StartAsync()` and `StopAsync()` remain as compatibility aliases for connect/disconnect.

Implemented behavior:

- Track receive-loop task.
- Track heartbeat-loop task.
- Make shutdown idempotent.
- Cancel receive/heartbeat cleanly.
- Close stream/client safely.
- Avoid `ValueTask` for custom async APIs; use `Task`.

Suggested state enum:

- `Disconnected`
- `Connecting`
- `Connected`
- `Reconnecting`
- `Faulted`

Tests:

- `ConnectAsync()` connects to a running `ConnectionManager`.
- `DisconnectAsync()` stops receive and heartbeat loops.
- Calling `DisconnectAsync()` multiple times is safe.
- `ReconnectAsync()` succeeds with a stored token.
- Reconnect failure leaves the client in a clear state.

### 5. Heartbeat

Status: Complete for v1.

Implemented:

- Send `PacketType.HeartBeat` as a header-only packet.
- Do not include player id in heartbeat payload.
- Start heartbeat only after server session assignment/reconnect confirmation.
- Stop heartbeat on disconnect.
- Use client config for heartbeat interval.

Why no player id:

- The server receives heartbeat through a specific TCP stream.
- `ClientConnection` already has the current session once assigned.
- The server can mark activity for that session without payload identity.

Tests:

- Heartbeat keeps server session connected past heartbeat timeout.
- No heartbeat causes server watchdog to disconnect the client.
- Heartbeat stops after disconnect.

Completed tests:

- Heartbeat sends a header-only heartbeat after connection confirmation.

Still needed:

- Add an integration test against `GameHost`/`ConnectionManager` proving the heartbeat keeps the server session alive past watchdog timeout.

### 6. Network Message Handler

Status: Complete for v1.

Implemented:

- Added `ClientNetworkMessageHandler`.
- It subscribes to `GameClient` lifecycle and packet callbacks.
- It dispatches typed client events through shared `IEventDispatcher`.
- It can be started, stopped, and disposed.

Current events:

- `ClientConnectedEvent`
- `ClientDisconnectedEvent`
- `ClientReconnectedEvent`
- `ClientPacketReceivedEvent`
- `ClientGameStateChangeReceivedEvent`
- `ClientChatMessageReceivedEvent`
- `ClientChatHistoryReceivedEvent`
- `ClientDisplayNameChangedEvent`
- `ClientChatLobbyReadyStateReceivedEvent`
- `ClientChatLobbyCountdownReceivedEvent`
- `ClientChatLobbyPlayerStateReceivedEvent`
- `ClientMatchPhaseChangeReceivedEvent`
- `ClientGameStateMessageReceivedEvent`

Tests:

- Raw packets are routed into typed client events.
- `PacketType.GameStateChange` is parsed into `ClientGameStateChangeReceivedEvent`.
- `PacketType.GameStateMessage` is parsed into `ClientGameStateMessageReceivedEvent`.
- Invalid game-state-change payloads are ignored by the parsed event path while still dispatching the raw packet event.
- Handler does not crash the receive loop when one event handler fails.
- Connected events include handshake player id, game state id, and state payload.
- Disconnected events dispatch.
- Stop unsubscribes from `GameClient` callbacks.

### 7. Client Chat Service

Status: Complete for v1.

Implemented:

- Added `ClientChatService`.
- This service should be used by game states/UI instead of direct packet sends.

Responsibilities:

- Send chat messages.
- Parse single `ChatEntry` JSON messages.
- Parse `ChatEntry[]` history snapshots.
- Maintain local chat history.
- Expose chat events to game states/UI through `IEventDispatcher`.

Current API:

- `SendMessageAsync(string message)`
- `GetHistory()`

Current events:

- `ClientChatMessageReceivedEvent`
- `ClientChatHistoryReceivedEvent`

Tests:

- Sends `PacketType.ChatMessage` with UTF-8 payload.
- Parses single `ChatEntry`.
- Parses `ChatEntry[]`.
- Dispatches chat events for history and new messages.
- Ignores non-chat packets and invalid chat payloads.

### 8. Client Player Service / Display Name

Status: Complete for v1.

Implemented:

- Added `ClientPlayerService`.
- Tracks local `ClientPlayerData` with:
  - `PlayerId`
  - `DisplayName`
- Initializes current player from `ClientConnectedEvent`.
- Preserves display name on reconnect for the same player id.
- Resets to the default name when the player id changes.
- `ChangeDisplayNameAsync(...)` updates local player data and dispatches `ClientDisplayNameChangedEvent`.
- `ClientChatService` listens for `ClientDisplayNameChangedEvent` and sends `PacketType.DisplayNameUpdate` to the server.

Current API:

- `ChangeDisplayNameAsync(string displayName)`
- `CurrentPlayer`

Wire behavior:

- Sends `PacketType.DisplayNameUpdate`.
- Payload is UTF-8 display-name text.

Tests:

- Sends the correct packet type and payload.
- Initializes player data from connected/reconnected events.
- Validates empty display names.
- Dispatches `ClientDisplayNameChangedEvent`.
- `ClientChatService` sends `DisplayNameUpdate` when the event is observed.

Future:

- Expand `ClientPlayerService` to own more local player data such as life, inventory, stats, and account identity once those systems exist.

### 9. Nullable / Client Code Cleanup

Status: Complete for current codebase.

Known warnings:

- None.

Completed:

- Cleaned `GameClient` nullable warnings.
- Made `GameClient` events nullable where appropriate.
- Made task/socket fields nullable and lifecycle-safe.
- Remove stale relay terminology from comments.
- Clean `ReuseCancelTokenSource` nullability.

### 10. Client Game-State Manager / App

Status: Complete for v1.

Implemented:

- Added `IClientGameState` and `ClientGameState`.
- Added `ClientGameStateManager`.
- Added `ClientChatLobbyState`.
- Added `HawkNetClient` as the builder-created client runtime.
- `HawkNetClient` creates:
  - `EventDispatcher`
  - `GameClient`
  - `ClientNetworkMessageHandler`
  - `ClientChatService`
  - `ClientPlayerService`
  - `ClientGameStateManager`
- `HawkNetClient` registers these services with its instance service registry.
- `ClientGameStateManager` enters states from:
  - `ClientConnectedEvent`
  - `ClientReconnectedEvent`
  - `ClientGameStateChangeReceivedEvent`
- Default client state registration maps `chat_lobby` to `ClientChatLobbyState`.
- `ClientChatLobbyState` exposes chat history, current player, send chat, change display name, ready-state, server-timed countdown, and `SetReadyAsync(...)` helpers for future UI binding.

Tests:

- Manager enters initial state from connected event.
- Manager transitions from server-driven state-change event.
- Unknown state changes leave current state unchanged.
- Manager updates active state.
- `HawkNetClient.ConnectAsync()` initializes services, connects to a real host, and enters `chat_lobby`.

### 11. Tests And Validation

Status: Client app / chat lobby path complete.

Run after each pass:

```powershell
dotnet build --no-restore
dotnet test --no-restore
```

Current baseline:

- `dotnet build --no-restore` passes.
- `dotnet build --no-restore` has 0 warnings.
- Latest full solution verification passes with `519/519` tests and zero failures.

Completed integration coverage:

- Real `GameHost` and `GameClient` handshake over TCP.
- Client receives a runtime manifest and enters `chat_lobby` as the initial game state.
- Client rejects unsupported runtime manifest versions and protocol versions.
- Client rejects unknown, misplaced, duplicate, or dependency-invalid module manifests.
- `ClientChatService.SendMessageAsync(...)` reaches the server and broadcasts back through client chat events.
- `ClientPlayerService.ChangeDisplayNameAsync(...)` sends a display-name update and later chat uses the updated display name.
- A later client receives existing chat history.

## Suggested Implementation Order

1. Rename `RelayHub` to `GameClient`. Complete.
2. Add client config/settings JSON and settings records. Complete.
3. Implement connection lifecycle with clean cancellation and state. Complete for v1.
4. Store server-assigned `playerId + reconnectToken`. Complete.
5. Implement reconnect using the token payload. Complete.
6. Implement header-only heartbeat. Complete for v1.
7. Add network message handler and client events. Complete for v1.
8. Add `ClientChatService`. Complete for v1.
9. Add client player service and display-name update flow. Complete for v1.
10. Add server-driven `GameStateChange` packet protocol and client parsed event. Complete.
11. Add client game-state manager and client `chat_lobby` state. Complete for v1.
12. Add `HawkNetClient` runtime that creates/registers client services and connects the game client. Complete for v1.
13. Define game-state-specific handshake/state-change payload parsing once client game states need non-empty payloads. Complete for `chat_lobby`.
14. Clean remaining shared nullable warnings and run full tests. Complete.
15. Add generic game-state message parsing for external game-owned payloads. Complete.

## Deferred Until After Basic Client Chat Works

- Rich game-state synchronization beyond generic `GameStateMessage` envelopes.
- Authenticated NAT rebinding, fragmentation/reassembly, encryption, and adaptive congestion control for later internet-facing UDP use.
- Payload/codec abstraction before message packing or binary formats.
- Auth/account identity.
- TLS transport.
- Unity adapter layer.
- Moderation/filter hooks.

## Notes For Another Codex Chat

Start with this file, then inspect:

- `Core/Client/GameClient.cs`
- `Core/Shared/SignalShared.cs`
- `Core/Server/ConnectionManager.cs`
- `Core/Server/ClientConnection.cs`
- `Core/Server/ChatService.cs`

Keep future client work focused on reusable framework seams. Game-specific state interpretation should live in consuming projects or adapters, not in HawkNet core.
