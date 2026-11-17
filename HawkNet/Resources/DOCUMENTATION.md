# HawkNet Documentation

## Overview

HawkNet is a pure C# client/server framework for building reusable networking foundations across different projects. The core framework owns networking, connection lifecycle, service startup, event routing, game-state registration, and default modules. External projects are expected to compose their own game logic, UI, tools, or Unity integration around the framework runtime.

The framework currently exposes two main runtime entry points:

- `HawkNetServer` for creating and starting a configured server runtime.
- `HawkNetClient` for creating and connecting a configured client runtime.

Both runtimes are configured through builders. The builder pattern is the primary extension point for other projects.

## UDP delivery (protocol v3)

HawkNet protocol v3 replaces the earlier raw UDP datagram format. UDP peers now use a timestamped delivery envelope while the framed application packet retains its own serialization timestamp. Protocol-v2 UDP clients and servers are intentionally incompatible; TCP framing is unchanged.

The built-in delivery modes are:

- `ReliableOrdered` for control messages, chat, state changes, match commands, and other non-replaceable gameplay traffic.
- `UnreliableSequenced` for heartbeats, player input, movement samples, and replaceable snapshots.

Ordering is independent on the `Control`, `Gameplay`, and `Snapshot` channels. `PacketDeliveryPolicy.GetDefault(PacketType)` supplies safe defaults, and the delivery-aware send overloads accept an explicit `DeliveryOptions` override. TCP accepts the same option and continues to provide its normal ordered byte stream.

UDP datagrams are capped at 1,200 bytes and are not fragmented. With the v3 envelope and HawkNet packet framing, the default maximum application payload is `SignalShared.MaxUdpPayloadLength`. Larger domain data must be split by the application or sent over TCP. Discovery remains stateless and outside reliable peer allocation.

Reliable sends complete after bounded-window admission and their initial socket write. Acknowledgements and retries continue internally; retry exhaustion closes the connection as `ReliableDeliveryFailed`. UDP settings and runtime metrics are available through `UdpDeliverySettings`, `IUdpDeliveryMetricsSource`, `GameClient.UdpDeliveryMetrics`, and `ServerMonitorSnapshot.UdpDelivery`.

## Project Shape

`HawkNet` is packaged as a reusable pure C# framework library. The framework project multi-targets `net8.0` and `netstandard2.1` so it can be consumed by modern console/server apps while keeping a path open for Unity and other engines.

Tests live in the separate `HawkNet.Tests` project and reference the framework project. Test packages should stay out of the framework DLL.

Package metadata:

- Package id: `HawkNet`
- Title: `Hawk Net`
- Version: `0.0.0001`
- Author: `Duncan Budinger`

NuGet may normalize the package filename to a SemVer equivalent such as `0.0.1`.

## Command Line Samples

HawkNet includes a generic console host and a game-specific composition project:

- `HawkNet.CommandLine` contains reusable server/client runners, built-in commands, parsing, and extension contracts.
- `ClanIronSim.CommandLine` installs the ClanIron match module, movement command, message inspector, configuration, and world assets.

Both projects can run either the headless server or headless client. Use the ClanIron project for the current game smoke test.

Start the server in the first terminal:

```powershell
cd C:\Users\dunca\Dev\HawkNet
dotnet run --project ClanIronSim.CommandLine -- server
```

Start a client in the second terminal:

```powershell
cd C:\Users\dunca\Dev\HawkNet
dotnet run --project ClanIronSim.CommandLine -- client
```

Additional clients can be started from more terminals with the same client command.

The server command loads `Config/game-server.json`, starts through `HawkNetServer.Create(...)`, applies registered CLI extensions, and logs player lifecycle, chat, match commands, and state transitions.

The client command loads `Config/game-client.json`, connects through `HawkNetClient.Create(...)`, and opens a small command-line interface.

Client commands:

- `say <message>` sends lobby chat.
- `name <display name>` changes the local display name.
- `ready [payload text]` marks the local player ready with an optional opaque payload.
- `unready [payload text]` marks the local player unready.
- `cmd [id] [payload text]` sends a match command. If no id is provided, it uses `ping`.
- `move <worldX> <worldZ>` is contributed by `ClanIronSim.CommandLine`.
- `state` prints the local client, player, lobby, or match snapshot.
- `help` prints available commands.
- `quit` disconnects and exits.

Basic manual flow:

1. Start `dotnet run --project ClanIronSim.CommandLine -- server`.
2. Start one or more `dotnet run --project ClanIronSim.CommandLine -- client` instances.
3. Use `say hello` to verify chat.
4. Use `ready` from each client to trigger the lobby countdown and match transition.
5. Use `cmd ping hello` during match to verify match command routing.
6. Use `quit` to disconnect clients cleanly.

## Server Setup

The server runtime is created through `HawkNetServer.Create(...)`. Internally, the server creates and owns:

- `GameHost`
- `EventDispatcher`
- `ConnectionManager`
- `MessageDispatcher`
- `InboundMessageRouter`
- `PlayerIdentityService`
- `ChatService`
- `GameStateManager`
- `ServerDiagnostics`
- `UpdateLoop`

`GameHost` owns server startup and shutdown. It starts the configured TCP or UDP transport, initializes the active game state, starts diagnostics, and runs the fixed simulation loop.

Server systems communicate through typed events instead of direct coupling. External projects should subscribe to events or register game states through the server builder rather than reaching into connection/session internals.

### Server Builder Example

```csharp
using HawkNet.Core.Server;

var server = HawkNetServer.Create("Config/game-server.json", builder =>
{
    builder.AddGameState("match", context =>
        new MatchState(context.Events));

    builder.Events.Subscribe<PlayerConnectedEvent>(async (gameEvent, token) =>
    {
        Console.WriteLine($"Player connected: {gameEvent.PlayerId}");
        await Task.CompletedTask;
    });
});

await server.BeginAsync();
```

The default server setup still registers `chat_lobby`, so a basic project can start without custom states:

```csharp
var server = HawkNetServer.Create("Config/game-server.json");
await server.BeginAsync();
```

## Client Setup

The client runtime is created through `HawkNetClient.Create(...)`. It replaces the older app-style composition root and keeps framework service initialization inside HawkNet.

Internally, the client runtime creates and owns:

- `EventDispatcher`
- `GameClient`
- `ClientNetworkMessageHandler`
- `ClientChatService`
- `ClientPlayerService`
- `ClientGameStateManager`

`GameClient` remains the lower-level network client. It owns the TCP connection, handshake, reconnect token, receive loop, and heartbeat. `HawkNetClient` is the higher-level framework runtime that initializes core services first, then enables server-requested modules from the handshake runtime manifest.

### Client Builder Example

```csharp
using HawkNet.Core.Client;

var client = HawkNetClient.Create("Config/game-client.json", builder =>
{
    builder.AddGameState("match", context =>
        new MatchClientState(context.Events));

    builder.Events.Subscribe<ClientConnectedEvent>(async (gameEvent, token) =>
    {
        Console.WriteLine($"Connected as player {gameEvent.PlayerId}");
        await Task.CompletedTask;
    });
});

await client.ConnectAsync();
```

The default client setup still registers `chat_lobby`, so a basic client can connect without custom state registration:

```csharp
var client = HawkNetClient.Create("Config/game-client.json");
await client.ConnectAsync();
```

## Builder Contexts

Builder state factories receive runtime contexts. These contexts expose the safe framework surface a project should use.

Server state factories receive `IHawkNetServerContext`, which exposes:

- `Events`
- `Logger`
- `Diagnostics`
- `Services`
- `GetConnectedPlayerIds()`

Client state factories receive `IHawkNetClientContext`, which exposes:

- `Events`
- `Services`
- `Client`
- `ChatService`
- `PlayerService`
- `GameStateManager`

Example server state registration:

```csharp
builder.AddGameState("lobby", context =>
    new LobbyState(
        context.Events,
        context.GetConnectedPlayerIds,
        context.Logger));
```

Example client state registration:

```csharp
builder.AddGameState("lobby", context =>
    new LobbyClientState(
        context.Events,
        context.ChatService,
        context.PlayerService));
```

## Events

Events are the main way projects hook into HawkNet behavior.

Server events include:

- `PlayerConnectedEvent`
- `PlayerReconnectedEvent`
- `PlayerConnectionLostEvent`
- `PlayerDisconnectedEvent`
- `PlayerInputReceivedEvent`
- `ChatMessageLoggedEvent`
- `GameStateTransitionRequestedEvent`
- `GameStateTransitionCompletedEvent`
- `GameStateTransitionFailedEvent`

Client events include:

- `ClientConnectedEvent`
- `ClientReconnectedEvent`
- `ClientDisconnectedEvent`
- `ClientPacketReceivedEvent`
- `ClientChatMessageReceivedEvent`
- `ClientChatHistoryReceivedEvent`
- `ClientDisplayNameChangedEvent`
- `ClientGameStateChangeReceivedEvent`

Example event subscription:

```csharp
builder.Events.Subscribe<ClientChatMessageReceivedEvent>(async (gameEvent, token) =>
{
    Console.WriteLine($"{gameEvent.Entry.DisplayName}: {gameEvent.Entry.Message}");
    await Task.CompletedTask;
});
```

## Protocol Naming

Network payload contract names should make direction clear:

- Client-to-server payload records end in `Request`.
- Server-to-client payload records end in `Response`.
- Runtime events still end in `Event`.
- Internal services, dispatchers, handlers, and packet enum values do not need the request/response suffix unless they are also public network payload contracts.

Examples:

- `ChatLobbyReadyRequest` is sent by the client to the server.
- `GameStateChangeResponse`, `ChatLobbyReadyStateResponse`, and `MatchPhaseChangeResponse` are sent by the server to the client.

## Services

Each runtime owns an instance service registry. This keeps framework services scoped to the server or client that created them.

Example:

```csharp
var chat = client.Services.Get<ClientChatService>();
await chat.SendMessageAsync("Hello from HawkNet.");
```

The existing static `Services` facade remains available for simple or legacy scenarios, but new framework code should prefer the runtime-owned registry.

## Server Config Service

The server registers `IConfigService` through the `core` service module. It owns the loaded `GameHostConfig` instance and provides a single lookup surface for module and state settings.

```csharp
var config = context.Config.Get<MatchSettings>();
```

Known settings records include:

- `GameHostConfig`
- `ServerSettings`
- `NetworkingSettings`
- `SimulationSettings`
- `ChatSettings`
- `PlayerIdentitySettings`
- `ChatLobbySettings`
- `MatchSettings`
- `GameModeSettings`
- `SecuritySettings`
- `DiagnosticsSettings`
- `MaintenanceSettings`
- `ModuleSettings`

Server contexts expose the same service directly as `context.Config`. New server modules and states should prefer `IConfigService` over passing individual settings records through constructors when the settings are framework-owned.

## Modules

HawkNet uses modules as the composition unit for framework features. Built-in defaults are modules, and projects can add their own modules through the builder.

Server modules implement:

```csharp
public interface IHawkNetServerModule
{
    void Configure(IHawkNetServerBuilder builder);
}
```

Client modules implement:

```csharp
public interface IHawkNetClientModule
{
    void Configure(IHawkNetClientBuilder builder);
}
```

Modules can register services, subscribe to events, and register game states.

Server modules are split into service modules and game-state modules in config:

```json
"Modules": {
  "Services": [
    "core",
    "player_identity",
    "diagnostics",
    "chat_service"
  ],
  "GameStates": [
    "chat_lobby"
  ]
}
```

`Modules.Services` controls systems that initialize before game states. `Modules.GameStates` controls which game-state factories are registered. `GameMode.InitialStateId` still controls which registered game state starts first.

Built-in server service module IDs:

- `core`
- `player_identity`
- `diagnostics`
- `chat_service`

Built-in server game-state module IDs:

- `chat_lobby`
- `match`

Built-in client service module IDs:

- `core`
- `player_identity`
- `diagnostics`
- `chat_service`

Built-in client game-state module IDs:

- `chat_lobby`
- `match`

Example server module:

```csharp
public sealed class InventoryServerModule : IHawkNetServerModule
{
    public void Configure(IHawkNetServerBuilder builder)
    {
        builder.AddService(new InventoryService());

        builder.Events.Subscribe<PlayerConnectedEvent>(async (gameEvent, token) =>
        {
            // Load inventory for the connecting player.
            await Task.CompletedTask;
        });
    }
}
```

Example client module:

```csharp
public sealed class InventoryClientModule : IHawkNetClientModule
{
    public void Configure(IHawkNetClientBuilder builder)
    {
        builder.AddService(new InventoryClientService());

        builder.AddGameState("inventory", context =>
            new InventoryClientState(
                context.Events,
                context.Services.Get<InventoryClientService>()));
    }
}
```

Add modules through the runtime builders:

```csharp
var server = HawkNetServer.Create("Config/game-server.json", builder =>
{
    builder.AddModule(new InventoryServerModule());
});
```

```csharp
var client = HawkNetClient.Create("Config/game-client.json", builder =>
{
    builder.AddModule(new InventoryClientModule());
});
```

Use `AddService(...)` for direct service registration and `AddModule(...)` when a feature has multiple registrations or setup steps.

## Runtime Manifest

The server sends a client runtime manifest inside `HandshakeResponse.Runtime` after a fresh connection or successful reconnect. The manifest is server-authored, but the client only enables modules that are already registered in the local client runtime.

```csharp
public sealed record RuntimeModuleRequirement
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = "1";
    public bool Required { get; init; } = true;
}

public sealed record ClientRuntimeManifest
{
    public int ManifestVersion { get; init; } = 1;
    public string ProtocolVersion { get; init; } = "1";
    public string[] Services { get; init; } = [];
    public string[] GameStates { get; init; } = [];
    public RuntimeModuleRequirement[] ServiceModules { get; init; } = [];
    public RuntimeModuleRequirement[] GameStateModules { get; init; } = [];
    public string InitialGameStateId { get; init; } = string.Empty;
    public byte[] InitialGameStatePayload { get; init; } = [];
}
```

`ServiceModules` and `GameStateModules` are the preferred versioned shape. `Services` and `GameStates` remain as simple ID lists for compatibility and readability.

The handshake response shape is:

```csharp
public sealed record HandshakeResponse
{
    public int PlayerId { get; init; }
    public byte[] ReconnectToken { get; init; } = [];
    public ClientRuntimeManifest Runtime { get; init; } = new();
}
```

Client startup order:

1. Core client services start before the network connection.
2. The server sends `HandshakeResponse`.
3. `HawkNetClient` validates `ManifestVersion` and `ProtocolVersion`.
4. Client service module versions from `Runtime.ServiceModules` are validated and enabled.
5. Client game-state module versions from `Runtime.GameStateModules` are validated and enabled.
6. The client enters `Runtime.InitialGameStateId` with `Runtime.InitialGameStatePayload`.

`core` is always local client infrastructure and does not need to be listed in the server-sent service module list. `GameStateChangeResponse` remains the transition packet after connect.

## Current Defaults

HawkNet currently enables these default behaviors:

- TCP transport by default, with UDP selectable in server and client networking settings.
- JSON payloads for existing structured messages.
- Server-side player identity.
- Server-side chat service.
- Server diagnostics.
- Client network message routing.
- Client chat service.
- Client player service.
- Server and client `chat_lobby` game states.

Future framework work should keep these defaults convenient while allowing projects to replace or omit built-in modules when needed.

## Transport Abstraction

HawkNet separates framework networking behavior from the low-level transport implementation.

Current transport contracts:

- `IClientTransport`
- `IServerTransport`
- `ITransportConnection`

Current TCP implementations:

- `TcpClientTransport`
- `TcpServerTransport`
- `TcpTransportConnection`

Current UDP implementations:

- `UdpClientTransport`
- `UdpServerTransport`
- endpoint-backed virtual connections managed by `UdpServerTransport`

`GameClient` depends on `IClientTransport` for connecting, disconnecting, sending packets, and reading packets. `ConnectionManager` depends on `IServerTransport` for starting the listener and accepting transport connections. `ClientConnection` depends on `ITransportConnection` for per-client packet reads and sends.

TCP provides ordered, reliable delivery. UDP carries one complete HawkNet packet per datagram and reuses the same handshake, reconnect, session, allowlist, rate-limit, and bounded-queue layers. UDP discovery is stateless. UDP delivery itself is best-effort: applications must tolerate loss and reordering until reliable/ordered UDP channels are added. TLS, WebSocket, and Unity-specific transports remain future implementations.

## Chat Lobby Ready State

`chat_lobby` supports extensible player readiness. Clients send a ready request with an opaque byte payload, and the server stores and broadcasts that payload without decoding project-specific data.

```csharp
await lobby.SetReadyAsync(
    isReady: true,
    payload: codec.Encode(myLoadout));
```

Server config controls when the lobby should request a transition:

```json
"ChatLobby": {
  "ReadyThresholdPercent": 100,
  "TransitionCountdownSeconds": 5,
  "ReadyTransitionStateId": "match",
  "MaxReadyPayloadBytes": 4096
}
```

The required ready count is rounded up. If readiness drops below the threshold during countdown, the countdown is canceled. When countdown starts, the server sends one countdown message with UTC start/end times; clients count down locally until a cancel, completed, or game-state transition message arrives. When the countdown completes, the lobby dispatches `GameStateTransitionRequestedEvent` using `ReadyTransitionStateId` and includes the current lobby ready state as an opaque transition payload.

## Game State Transitions

Server game states request transitions by destination state id, not by action name. Each configured state owns an allow-list of destination states it may move to:

```json
"GameMode": {
  "InitialStateId": "chat_lobby",
  "States": [
    {
      "Id": "chat_lobby",
      "Transitions": [ "match" ]
    },
    {
      "Id": "match",
      "Transitions": [ "results", "chat_lobby" ]
    }
  ]
}
```

The active state can request a valid destination and pass opaque handoff bytes to the next server state:

```csharp
await RequestTransitionAsync("match", payloadBytes, token);
```

The next server state receives those bytes in `Enter(byte[] transitionPayload)`. Client transition messages are still built separately from the newly active state through `CreatePayloadForPlayer(...)`, so server-to-server handoff payloads do not need to match client payloads.

## Match State

The built-in `match` game state is a reusable phase runner, not one hardcoded gameplay mode. HawkNet game-state IDs remain broad framework states such as `chat_lobby` and `match`; match internals use `PhaseId` values such as `pre_match`, `match`, and `post_match`.

Server config defines the default match phase flow:

```json
"Match": {
  "VariantId": "base",
  "Phases": [
    {
      "PhaseId": "pre_match",
      "Seconds": 5
    },
    {
      "PhaseId": "match",
      "Seconds": 300
    },
    {
      "PhaseId": "post_match",
      "Seconds": 10
    }
  ],
  "CompletedTransitionStateId": ""
}
```

`VariantId` selects the `IGameMatch` implementation used by the `match` state. The built-in default is `base`, which creates and runs `GameMatchBase`. Project modules can register additional variants, such as `arena`, `capture_point`, or `survival`, without changing the outer HawkNet game-state id.

Phase duration rules:

- `0` completes the phase immediately.
- Positive values are timed phase durations in seconds.
- `-1` means the phase is infinite until match-specific logic completes it.

The default v1 phase flow is:

- `pre_match`
- `match`
- `post_match`

The match shell passes an `IGameMatchContext` into `IGameMatch.InitializeAsync(...)`. That context is the expandable surface for events, logging, services, payload codec, chat, player identity, connected-player lookup, and transition helpers.

Match lifecycle methods are async and run through the `MatchState` operation queue. This keeps match lifecycle and update work off the immediate state-call path while preventing overlapping match operations.

Match phases with `Seconds = -1` are manual or infinite phases. Custom match variants can finish the current phase through the match context:

```csharp
await context.CompleteCurrentPhaseAsync(payload: null, token);
```

When the server enters a new match phase, it broadcasts `PacketType.MatchPhaseChange` with:

```csharp
public sealed record MatchPhaseChangeResponse
{
    public string VariantId { get; init; } = string.Empty;
    public string PhaseId { get; init; } = string.Empty;
    public float PhaseSeconds { get; init; }
    public DateTime PhaseStartedAtUtc { get; init; }
    public DateTime PhaseEndsAtUtc { get; init; }
    public MatchPlayer[] Players { get; init; } = [];
}
```

Clients parse that packet into `ClientMatchPhaseChangeReceivedEvent`, and `ClientMatchState` updates its current phase and player snapshot.

Clients send gameplay input through `ClientMatchService` instead of building packets directly:

```csharp
var match = client.Services.Get<ClientMatchService>();
await match.SendCommandAsync("move", codec.Encode(myMoveCommand));
```

The wire contract is:

```csharp
public sealed record MatchCommandRequest
{
    public string CommandId { get; init; } = string.Empty;
    public byte[] Payload { get; init; } = [];
}
```

The server decodes this request and dispatches `MatchCommandReceivedEvent`. `GameMatchBase` subscribes while the match is active and calls `OnMatchCommandAsync(...)`, so custom match variants can decode command payloads with `IPayloadCodec` while HawkNet keeps command payloads opaque.

Match implementation guidance:

- Keep server handoff payloads separate from client-facing match payloads.
- Keep player tracking reusable in `GameMatchBase`.
- Let `GameMatchBase` handle common chat and player connection event hookups.
- Allow concrete match types to add extra subscriptions while still calling base subscription setup.
- Add late connected or reconnected players to the active match.
- Mark dropped players disconnected instead of removing their match record.
- Avoid putting teams, scoring, winners, rounds, objectives, or loadout parsing into the base match.
- Route project-specific player actions through `MatchCommandRequest` with opaque payload bytes.

Match events:

- `MatchCommandReceivedEvent`
- `MatchCommandRejectedEvent`
- `MatchPhaseChangedEvent`
- `MatchPlayerJoinedEvent`
- `MatchPlayerDisconnectedEvent`
- `MatchStartedEvent`
- `MatchEndedEvent`

## Extension Guidelines

When building on HawkNet:

- Use `HawkNetServer.Create(...)` and `HawkNetClient.Create(...)` as the public entry points.
- Register game states through the builder.
- Subscribe to typed events instead of reaching into low-level networking classes.
- Use runtime services through the instance service registry.
- Keep project-specific app startup, UI binding, Unity lifecycle, menus, and scene management outside the HawkNet core.
- Keep HawkNet core code pure C#.

## Hardened Server Runtime

Authenticated client traffic is queued through `IServerExecutionScheduler`. The bounded inbox assigns global and per-player sequence numbers, captures the active state epoch, and is drained before each simulation tick. This prevents connection tasks from mutating authoritative state concurrently and drops messages queued for an exited state. Configure it with `Simulation.InboundQueueCapacity` and `Simulation.MaxCommandsPerTick`; current depth, rejection count, latency, and high-water mark are available from `GameHost.GetMonitorSnapshot()`.

Each client has a bounded, ordered outbound writer queue configured by `Networking.OutboundQueueCapacity`. `MessageDispatcher` now returns `SendResult` or `BroadcastSendResult`, and packet counters record only successful writes. Slow clients that fill their queue are disconnected instead of allowing memory to grow without a bound.

Unauthenticated connections must complete a handshake within `Security.HandshakeTimeoutSeconds`. Packet categories have pre-decode payload limits, and IP allowlists accept individual IPv4/IPv6 addresses or CIDR ranges. The built-in transports support TCP and UDP; TLS configurations fail validation rather than silently changing behavior. UDP payload limits must fit in a single datagram.

`GameHost` instances are one-shot and expose `GameHostLifecycleState`. Create a new host after a stopped or failed host. State implementations may implement `IAsyncGameState`; the server serializes async entry, tick, and exit through the authoritative execution path.

Optional server capabilities are resolved with `TryGet<T>` or `GetRequired<T>`. The match module can run with only the core module; chat, diagnostics, and player identity are optional. Module dependencies are activated in topological order and cycles fail startup. External modules can bind typed settings from `GameHostConfig.ModuleOptions` using `builder.GetOptions(sectionId, defaults)`.

## Server Configuration and CLI

`ServerConfiguration.Load` and `ServerConfiguration.Validate` are the shared fail-fast configuration path. The desktop provides profile load, validation, and save actions. `localhost` resolves to IPv4 loopback, while the allowlist supports CIDR entries such as `192.168.1.0/24`.

The command-line server accepts:

```text
hawknet server --config Config/game-server.json --bind 0.0.0.0 --port 7777 --protocol udp
hawknet client --config Config/game-client.json --host 127.0.0.1 --port 7777 --protocol udp
```

Precedence is command-line arguments, then the corresponding `HAWKNET_SERVER_*` or `HAWKNET_CLIENT_*` environment variables, then the bundled configuration file and record defaults.

## Operator Monitoring Extensions

`ServerMonitorSnapshot` provides lifecycle, effective listen endpoint, active state, sessions, inbox pressure, and diagnostics. CoreUx defines `IServerWorldMonitorProvider` and `IServerInspectorProvider`, allowing a host integration to contribute world snapshots and inspector panels without the generic match runtime depending on a concrete game.

World snapshots carry topology and entity revisions. The desktop refreshes entity state at 10 Hz, reuses unchanged topology, updates entity view models in place, preserves selection, and interpolates rendering locally. Providers may expose multiple regions; the operator can choose a region and search for a player by name or ID to focus its entity on the map.
