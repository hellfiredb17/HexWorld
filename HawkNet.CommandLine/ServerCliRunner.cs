using HawkNet.Core.Server;

namespace HawkNet.CommandLine;

public static class ServerCliRunner
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IEnumerable<IHawkNetCliExtension>? extensions = null,
        ICliConsole? console = null,
        CancellationToken token = default)
    {
        console ??= SystemCliConsole.Instance;
        IReadOnlyList<IHawkNetCliExtension> resolvedExtensions = CliExtensionRegistry.Resolve(extensions);
        ServerCliOptions options = CliOptions.ParseServer(args);
        GameHostConfig config = ServerConfiguration.Load(options.ConfigPath);
        if (!string.IsNullOrWhiteSpace(options.Bind)) config = config with { Server = config.Server with { Host = options.Bind } };
        if (options.Port.HasValue) config = config with { Server = config.Server with { Port = options.Port.Value } };
        if (options.Protocol.HasValue) config = config with { Networking = config.Networking with { Protocol = options.Protocol.Value } };
        if (options.UdpDelivery.HasOverrides)
            config = config with { Networking = config.Networking with { UdpDelivery = options.UdpDelivery.ApplyTo(config.Networking.UdpDelivery) } };
        ServerConfigValidationResult validation = ServerConfiguration.Validate(config);
        validation.ThrowIfInvalid();

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(token);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        console.WriteLine("Starting HawkNet headless server...");
        console.WriteLine($"Config: {Path.GetFullPath(options.ConfigPath)}");
        WriteOverrides(options.Sources, console);
        if (resolvedExtensions.Count > 0) console.WriteLine($"Extensions: {string.Join(", ", resolvedExtensions.Select(extension => extension.Id))}");
        foreach (string warning in validation.Warnings) console.WriteLine($"Warning: {warning}");

        GameHost server = HawkNetServer.Create(config, builder =>
        {
            foreach (IHawkNetCliExtension extension in resolvedExtensions) extension.ConfigureServer(builder);
            RegisterLogging(builder, console);
        });

        try
        {
            await server.BeginAsync();
            string endpoint = server.GetMonitorSnapshot().ListenEndPoint?.ToString() ?? $"{config.Server.Host}:{config.Server.Port}";
            console.WriteLine($"Effective endpoint: {endpoint} ({config.Networking.Protocol})");
            if (config.Networking.Protocol == TransportProtocol.Udp) WriteUdpSettings(config.Networking.UdpDelivery, console);
            console.WriteLine("HawkNet server running. Press Ctrl+C to stop.");
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            console.WriteLine("Stopping HawkNet server...");
            await server.StopAsync();
            console.WriteLine("HawkNet server stopped.");
        }

        return 0;
    }

    private static void WriteOverrides(IReadOnlyDictionary<string, string> sources, ICliConsole console)
    {
        foreach ((string key, string source) in sources.Where(pair => pair.Value != "default"))
            console.WriteLine($"Override: {key} from {source}");
    }

    private static void WriteUdpSettings(HawkNet.Core.Shared.UdpDeliverySettings settings, ICliConsole console)
    {
        console.WriteLine($"UDP delivery: datagram={settings.MaxDatagramBytes} window={settings.ReliableWindowSize} reorder={settings.ReorderCapacity}");
        console.WriteLine($"UDP timing: rto={settings.InitialRetransmitMilliseconds}ms ({settings.MinimumRetransmitMilliseconds}-{settings.MaximumRetransmitMilliseconds}ms) retries={settings.MaximumRetries} ack-delay={settings.AcknowledgementDelayMilliseconds}ms");
    }

    private static void RegisterLogging(IHawkNetServerBuilder builder, ICliConsole console)
    {
        builder.Events.Subscribe<PlayerConnectedEvent>((e, _) => { console.WriteLine($"[player] connected id={e.PlayerId}"); return Task.CompletedTask; });
        builder.Events.Subscribe<PlayerReconnectedEvent>((e, _) => { console.WriteLine($"[player] reconnected id={e.PlayerId}"); return Task.CompletedTask; });
        builder.Events.Subscribe<PlayerDisconnectedEvent>((e, _) => { console.WriteLine($"[player] disconnected id={e.PlayerId} reason={e.Reason}"); return Task.CompletedTask; });
        builder.Events.Subscribe<PlayerConnectionLostEvent>((e, _) => { console.WriteLine($"[player] connection lost id={e.PlayerId} reason={e.Reason}"); return Task.CompletedTask; });
        builder.Events.Subscribe<ChatMessageLoggedEvent>((e, _) => { console.WriteLine($"[chat] {e.Entry.DisplayName}: {e.Entry.Message}"); return Task.CompletedTask; });
        builder.Events.Subscribe<MatchPhaseChangedEvent>((e, _) => { console.WriteLine($"[match] phase={e.PhaseId} players={e.Payload.Players.Length}"); return Task.CompletedTask; });
        builder.Events.Subscribe<MatchCommandReceivedEvent>((e, _) => { console.WriteLine($"[match] command player={e.PlayerId} id={e.CommandId} bytes={e.Payload.Length}"); return Task.CompletedTask; });
        builder.Events.Subscribe<MatchCommandRejectedEvent>((e, _) => { console.WriteLine($"[match] rejected player={e.PlayerId} id={e.CommandId} reason={e.Reason}"); return Task.CompletedTask; });
        builder.Events.Subscribe<GameStateMessageReceivedEvent>((e, _) => { console.WriteLine($"[game] message player={e.PlayerId} id={e.MessageId} bytes={e.Payload.Length}"); return Task.CompletedTask; });
        builder.Events.Subscribe<GameStateTransitionCompletedEvent>((e, _) => { console.WriteLine($"[state] {e.FromStateId} -> {e.ToStateId}"); return Task.CompletedTask; });
        builder.Events.Subscribe<GameStateTransitionFailedEvent>((e, _) => { console.WriteLine($"[state] transition failed from={e.FromStateId ?? "<none>"} to={e.ToStateId} reason={e.Reason}"); return Task.CompletedTask; });
    }
}
