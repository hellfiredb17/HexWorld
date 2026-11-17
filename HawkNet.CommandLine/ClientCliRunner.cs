using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.CommandLine;

public static class ClientCliRunner
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IEnumerable<IHawkNetCliExtension>? extensions = null,
        ICliConsole? console = null,
        CancellationToken token = default)
    {
        console ??= SystemCliConsole.Instance;
        IReadOnlyList<IHawkNetCliExtension> resolvedExtensions = CliExtensionRegistry.Resolve(extensions);
        ClientCliOptions options = CliOptions.ParseClient(args);
        GameClientConfig config = GameClientConfig.Load(options.ConfigPath);
        if (!string.IsNullOrWhiteSpace(options.Host)) config = config with { Server = config.Server with { Host = options.Host } };
        if (options.Port.HasValue) config = config with { Server = config.Server with { Port = options.Port.Value } };
        if (options.Protocol.HasValue) config = config with { Networking = config.Networking with { Protocol = options.Protocol.Value } };
        if (options.UdpDelivery.HasOverrides)
            config = config with { Networking = config.Networking with { UdpDelivery = options.UdpDelivery.ApplyTo(config.Networking.UdpDelivery) } };
        Validate(config);

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(token);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        var knownPlayers = new Dictionary<int, string>();
        IClientGameMessageInspector[] inspectors = resolvedExtensions.SelectMany(extension => extension.CreateMessageInspectors()).ToArray();

        console.WriteLine("Starting HawkNet headless client...");
        console.WriteLine($"Config: {Path.GetFullPath(options.ConfigPath)}");
        foreach ((string key, string source) in options.Sources.Where(pair => pair.Value != "default")) console.WriteLine($"Override: {key} from {source}");
        console.WriteLine($"Effective endpoint: {config.Server.Host}:{config.Server.Port} ({config.Networking.Protocol})");
        if (config.Networking.Protocol == TransportProtocol.Udp)
        {
            var udp = config.Networking.UdpDelivery;
            console.WriteLine($"UDP delivery: datagram={udp.MaxDatagramBytes} window={udp.ReliableWindowSize} reorder={udp.ReorderCapacity}");
            console.WriteLine($"UDP timing: rto={udp.InitialRetransmitMilliseconds}ms ({udp.MinimumRetransmitMilliseconds}-{udp.MaximumRetransmitMilliseconds}ms) retries={udp.MaximumRetries} ack-delay={udp.AcknowledgementDelayMilliseconds}ms");
        }
        if (resolvedExtensions.Count > 0) console.WriteLine($"Extensions: {string.Join(", ", resolvedExtensions.Select(extension => extension.Id))}");

        await using HawkNetClient client = HawkNetClient.Create(config, builder =>
        {
            foreach (IHawkNetCliExtension extension in resolvedExtensions) extension.ConfigureClient(builder);
            RegisterLogging(builder, console, knownPlayers, inspectors);
        });

        try
        {
            await client.ConnectAsync(shutdown.Token);
            console.WriteLine("HawkNet client connected.");
            IReadOnlyDictionary<string, IClientConsoleCommand> commands = CliExtensionRegistry.ResolveCommands(CreateBuiltInCommands(), resolvedExtensions);
            var context = new ClientConsoleContext(client, console, shutdown.Cancel) { Commands = commands };
            PrintHelp(context);
            await RunCommandLoopAsync(context, shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            console.WriteLine("Stopping HawkNet client...");
            await client.ShutdownAsync();
            console.WriteLine("HawkNet client stopped.");
        }

        return 0;
    }

    private static async Task RunCommandLoopAsync(ClientConsoleContext context, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            context.Console.Write("> ");
            string? line = await context.Console.ReadLineAsync(token);
            if (line is null) { context.RequestShutdown(); return; }
            string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (!context.Commands.TryGetValue(parts[0], out IClientConsoleCommand? command))
            {
                context.Console.WriteLine($"Unknown command '{parts[0]}'. Type 'help' for commands.");
                continue;
            }

            try { await command.ExecuteAsync(context, parts.Length > 1 ? parts[1] : string.Empty, token); }
            catch (Exception exception) when (exception is not OperationCanceledException) { context.Console.WriteLine($"[error] {exception.Message}"); }
        }
    }

    public static IReadOnlyList<IClientConsoleCommand> CreateBuiltInCommands() =>
    [
        new DelegateClientConsoleCommand("say", "say <message>", "Send lobby chat", 10, (c, a, t) => c.Client.ChatService.SendMessageAsync(Require(a, "Usage: say <message>"), t)),
        new DelegateClientConsoleCommand("name", "name <display name>", "Change display name", 20, (c, a, t) => c.Client.PlayerService.ChangeDisplayNameAsync(Require(a, "Usage: name <display name>"), t)),
        new DelegateClientConsoleCommand("ready", "ready [payload text]", "Mark ready", 30, (c, a, t) => SetReadyAsync(c, true, a, t)),
        new DelegateClientConsoleCommand("unready", "unready [payload text]", "Mark unready", 40, (c, a, t) => SetReadyAsync(c, false, a, t)),
        new DelegateClientConsoleCommand("cmd", "cmd [id] [payload text]", "Send a match command", 50, SendMatchCommandAsync),
        new DelegateClientConsoleCommand("state", "state", "Print local client state", 60, (c, _, _) => { PrintState(c); return Task.CompletedTask; }),
        new DelegateClientConsoleCommand("udp", "udp", "Print UDP delivery metrics", 70, (c, _, _) => { PrintUdpMetrics(c); return Task.CompletedTask; }),
        new DelegateClientConsoleCommand("help", "help", "Show commands", 900, (c, _, _) => { PrintHelp(c); return Task.CompletedTask; }),
        new DelegateClientConsoleCommand("quit", "quit", "Disconnect and exit", 1000, (c, _, _) => { c.RequestShutdown(); return Task.CompletedTask; }),
        new DelegateClientConsoleCommand("exit", "exit", "Disconnect and exit", 1001, (c, _, _) => { c.RequestShutdown(); return Task.CompletedTask; })
    ];

    private static async Task SetReadyAsync(ClientConsoleContext context, bool ready, string text, CancellationToken token)
    {
        if (context.Client.GameStateManager.ActiveState is not ClientChatLobbyState lobby) { context.Console.WriteLine("[lobby] not currently in chat_lobby"); return; }
        byte[] payload = string.IsNullOrWhiteSpace(text) ? [] : context.Client.Services.Get<IPayloadCodec>().Encode(new HeadlessTextPayload(text.Trim()));
        await lobby.SetReadyAsync(ready, payload, token);
        context.Console.WriteLine(ready ? "[lobby] ready sent" : "[lobby] unready sent");
    }

    private static async Task SendMatchCommandAsync(ClientConsoleContext context, string arguments, CancellationToken token)
    {
        string[] parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string id = parts.Length > 0 ? parts[0] : "ping";
        byte[] payload = parts.Length > 1 ? context.Client.Services.Get<IPayloadCodec>().Encode(new HeadlessTextPayload(parts[1].Trim())) : [];
        await context.Client.Services.Get<ClientMatchService>().SendCommandAsync(id, payload, token);
        context.Console.WriteLine($"[match] command sent id={id} bytes={payload.Length}");
    }

    private static void PrintState(ClientConsoleContext context)
    {
        HawkNetClient client = context.Client;
        context.Console.WriteLine($"[state] low-level={client.GameClient.State} active={client.GameStateManager.ActiveStateId ?? "<none>"}");
        if (client.PlayerService.CurrentPlayer is { } player) context.Console.WriteLine($"[player] id={player.PlayerId} name={player.DisplayName}");
        if (client.GameStateManager.ActiveState is ClientChatLobbyState lobby) context.Console.WriteLine($"[lobby] players={lobby.Players.Count} ready={lobby.ReadyPlayerCount}/{lobby.RequiredReadyPlayerCount}");
        else if (client.GameStateManager.ActiveState is ClientMatchState match) context.Console.WriteLine($"[match] variant={match.VariantId} phase={match.PhaseId} players={match.Players.Count}");
    }

    private static void PrintUdpMetrics(ClientConsoleContext context)
    {
        UdpDeliveryMetricsSnapshot metrics = context.Client.GameClient.UdpDeliveryMetrics;
        context.Console.WriteLine($"[udp] datagrams sent={metrics.SentDatagrams} received={metrics.ReceivedDatagrams} admitted={metrics.ReliableAdmissions} acked={metrics.Acknowledgements}");
        context.Console.WriteLine($"[udp] retransmits={metrics.Retransmissions} exhausted={metrics.RetryExhausted} duplicates={metrics.Duplicates} stale={metrics.StaleSequencedDrops} reordered={metrics.ReorderedPackets} overflow={metrics.ReorderOverflow} superseded={metrics.SupersededSends}");
        context.Console.WriteLine($"[udp] ack-only={metrics.AcknowledgementOnlyDatagrams} window={metrics.CurrentWindowOccupancy}/{metrics.HighWaterWindowOccupancy} srtt={metrics.SmoothedRoundTripMilliseconds:F1}ms rto={metrics.RetransmitTimeoutMilliseconds:F1}ms jitter={metrics.JitterMilliseconds:F1}ms");
    }

    private static void PrintHelp(ClientConsoleContext context)
    {
        context.Console.WriteLine("Commands:");
        foreach (IClientConsoleCommand command in context.Commands.Values.OrderBy(command => command.Order).ThenBy(command => command.Name, StringComparer.Ordinal))
            context.Console.WriteLine($"  {command.Usage,-28} {command.Description}");
    }

    private static string Require(string value, string usage) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(usage) : value.Trim();

    private static void Validate(GameClientConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Server.Host)) throw new InvalidOperationException("Client server host cannot be empty.");
        if (config.Server.Port is < 1 or > 65535) throw new InvalidOperationException("Client server port must be between 1 and 65535.");
        if (config.Networking.Protocol is not TransportProtocol.Tcp and not TransportProtocol.Udp) throw new InvalidOperationException("Client protocol must be Tcp or Udp.");
        if (config.Networking.ReceiveBufferSize <= 0 || config.Networking.SendBufferSize <= 0) throw new InvalidOperationException("Client network buffers must be greater than zero.");
        IReadOnlyList<string> udpErrors = UdpDeliveryConfiguration.Validate(config.Networking.UdpDelivery);
        if (udpErrors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, udpErrors));
    }

    private static void RegisterLogging(IHawkNetClientBuilder builder, ICliConsole console, Dictionary<int, string> players, IClientGameMessageInspector[] inspectors)
    {
        builder.Events.Subscribe<ClientConnectedEvent>((e, _) => { console.WriteLine($"[client] connected player={e.PlayerId} state={e.GameStateId}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientReconnectedEvent>((e, _) => { console.WriteLine($"[client] reconnected player={e.PlayerId} state={e.GameStateId}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientDisconnectedEvent>((_, _) => { console.WriteLine("[client] disconnected"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientChatMessageReceivedEvent>((e, _) => { console.WriteLine($"[chat] {e.Entry.DisplayName}: {e.Entry.Message}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientChatHistoryReceivedEvent>((e, _) => { console.WriteLine($"[chat] history count={e.History.Count}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientChatLobbyPlayerStateReceivedEvent>((e, _) => { LogPlayerChanges(console, players, e.PlayerState.Players); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientMatchPhaseChangeReceivedEvent>((e, _) => { console.WriteLine($"[match] phase={e.PhaseChange.PhaseId} players={e.PhaseChange.Players.Length}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientGameStateMessageReceivedEvent>((e, _) => { console.WriteLine(FormatGameMessage(e, inspectors)); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientCompatibilityFailedEvent>((e, _) => { console.WriteLine($"[error] compatibility failed reason={e.Reason} message={e.Message}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientHandshakeFailedEvent>((e, _) => { console.WriteLine($"[error] handshake failed message={e.Message}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientStateEntryFailedEvent>((e, _) => { console.WriteLine($"[error] state entry failed state={e.GameStateId} message={e.Message}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ClientFaultedEvent>((e, _) => { console.WriteLine($"[error] client faulted message={e.Message}"); return Task.CompletedTask; });
    }

    private static void LogPlayerChanges(ICliConsole console, Dictionary<int, string> known, IReadOnlyCollection<ChatLobbyPlayer> current)
    {
        var next = current.ToDictionary(player => player.PlayerId, player => player.DisplayName);
        foreach ((int id, string name) in next.Where(pair => !known.ContainsKey(pair.Key)).OrderBy(pair => pair.Key)) console.WriteLine($"[lobby] player joined id={id} name={name}");
        foreach ((int id, string name) in known.Where(pair => !next.ContainsKey(pair.Key)).OrderBy(pair => pair.Key)) console.WriteLine($"[lobby] player left id={id} name={name}");
        known.Clear(); foreach ((int id, string name) in next) known[id] = name;
        console.WriteLine($"[lobby] players={known.Count}");
    }

    public static string FormatGameMessage(
        ClientGameStateMessageReceivedEvent gameEvent,
        IEnumerable<IClientGameMessageInspector> inspectors)
    {
        foreach (IClientGameMessageInspector inspector in inspectors)
        {
            if (inspector.TryFormat(gameEvent, out string message)) return message;
        }

        return $"[game] message id={gameEvent.MessageId} bytes={gameEvent.Payload.Length}";
    }
}

public sealed record HeadlessTextPayload(string Text);
