using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.CommandLine;

public sealed record ServerCliOptions(
    string ConfigPath,
    string? Bind,
    int? Port,
    TransportProtocol? Protocol,
    UdpDeliveryCliOptions UdpDelivery,
    IReadOnlyDictionary<string, string> Sources);

public sealed record ClientCliOptions(
    string ConfigPath,
    string? Host,
    int? Port,
    TransportProtocol? Protocol,
    UdpDeliveryCliOptions UdpDelivery,
    IReadOnlyDictionary<string, string> Sources);

public sealed record UdpDeliveryCliOptions(
    int? MaxDatagramBytes,
    int? ReliableWindowSize,
    int? ReorderCapacity,
    int? InitialRetransmitMilliseconds,
    int? MinimumRetransmitMilliseconds,
    int? MaximumRetransmitMilliseconds,
    int? MaximumRetries,
    int? AcknowledgementDelayMilliseconds)
{
    public bool HasOverrides => MaxDatagramBytes.HasValue
        || ReliableWindowSize.HasValue
        || ReorderCapacity.HasValue
        || InitialRetransmitMilliseconds.HasValue
        || MinimumRetransmitMilliseconds.HasValue
        || MaximumRetransmitMilliseconds.HasValue
        || MaximumRetries.HasValue
        || AcknowledgementDelayMilliseconds.HasValue;

    public UdpDeliverySettings ApplyTo(UdpDeliverySettings settings) => settings with
    {
        MaxDatagramBytes = MaxDatagramBytes ?? settings.MaxDatagramBytes,
        ReliableWindowSize = ReliableWindowSize ?? settings.ReliableWindowSize,
        ReorderCapacity = ReorderCapacity ?? settings.ReorderCapacity,
        InitialRetransmitMilliseconds = InitialRetransmitMilliseconds ?? settings.InitialRetransmitMilliseconds,
        MinimumRetransmitMilliseconds = MinimumRetransmitMilliseconds ?? settings.MinimumRetransmitMilliseconds,
        MaximumRetransmitMilliseconds = MaximumRetransmitMilliseconds ?? settings.MaximumRetransmitMilliseconds,
        MaximumRetries = MaximumRetries ?? settings.MaximumRetries,
        AcknowledgementDelayMilliseconds = AcknowledgementDelayMilliseconds ?? settings.AcknowledgementDelayMilliseconds
    };
}

public static class CliOptions
{
    public static ServerCliOptions ParseServer(
        IReadOnlyList<string> args,
        Func<string, string?>? getEnvironment = null,
        string? defaultConfigPath = null)
    {
        getEnvironment ??= Environment.GetEnvironmentVariable;
        Dictionary<string, string> values = ParsePairs(args, ServerOptionNames);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string config = Resolve(values, "config", getEnvironment("HAWKNET_SERVER_CONFIG"),
            defaultConfigPath ?? Path.Combine(AppContext.BaseDirectory, "Config", "game-server.json"), sources);
        string? bind = ResolveOptional(values, "bind", getEnvironment("HAWKNET_SERVER_BIND"), sources);
        int? port = ParsePort(ResolveOptional(values, "port", getEnvironment("HAWKNET_SERVER_PORT"), sources), "server");
        TransportProtocol? protocol = ParseProtocol(ResolveOptional(values, "protocol", getEnvironment("HAWKNET_SERVER_PROTOCOL"), sources));
        UdpDeliveryCliOptions udpDelivery = ParseUdpDelivery(values, "HAWKNET_SERVER", getEnvironment, sources);
        return new ServerCliOptions(config, bind, port, protocol, udpDelivery, sources);
    }

    public static ClientCliOptions ParseClient(
        IReadOnlyList<string> args,
        Func<string, string?>? getEnvironment = null,
        string? defaultConfigPath = null)
    {
        getEnvironment ??= Environment.GetEnvironmentVariable;
        Dictionary<string, string> values = ParsePairs(args, ClientOptionNames);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string config = Resolve(values, "config", getEnvironment("HAWKNET_CLIENT_CONFIG"),
            defaultConfigPath ?? Path.Combine(AppContext.BaseDirectory, "Config", "game-client.json"), sources);
        string? host = ResolveOptional(values, "host", getEnvironment("HAWKNET_CLIENT_HOST"), sources);
        int? port = ParsePort(ResolveOptional(values, "port", getEnvironment("HAWKNET_CLIENT_PORT"), sources), "client");
        TransportProtocol? protocol = ParseProtocol(ResolveOptional(values, "protocol", getEnvironment("HAWKNET_CLIENT_PROTOCOL"), sources));
        UdpDeliveryCliOptions udpDelivery = ParseUdpDelivery(values, "HAWKNET_CLIENT", getEnvironment, sources);
        return new ClientCliOptions(config, host, port, protocol, udpDelivery, sources);
    }

    private static readonly string[] UdpOptionNames =
    [
        "udp-max-datagram", "udp-window", "udp-reorder", "udp-rto-initial",
        "udp-rto-min", "udp-rto-max", "udp-retries", "udp-ack-delay"
    ];

    private static readonly string[] ServerOptionNames = ["config", "bind", "port", "protocol", .. UdpOptionNames];
    private static readonly string[] ClientOptionNames = ["config", "host", "port", "protocol", .. UdpOptionNames];

    private static UdpDeliveryCliOptions ParseUdpDelivery(
        Dictionary<string, string> values,
        string environmentPrefix,
        Func<string, string?> getEnvironment,
        Dictionary<string, string> sources) => new(
            ParseInteger(ResolveOptional(values, "udp-max-datagram", getEnvironment($"{environmentPrefix}_UDP_MAX_DATAGRAM"), sources), "UDP maximum datagram bytes"),
            ParseInteger(ResolveOptional(values, "udp-window", getEnvironment($"{environmentPrefix}_UDP_WINDOW"), sources), "UDP reliable window size"),
            ParseInteger(ResolveOptional(values, "udp-reorder", getEnvironment($"{environmentPrefix}_UDP_REORDER"), sources), "UDP reorder capacity"),
            ParseInteger(ResolveOptional(values, "udp-rto-initial", getEnvironment($"{environmentPrefix}_UDP_RTO_INITIAL"), sources), "UDP initial retransmit milliseconds"),
            ParseInteger(ResolveOptional(values, "udp-rto-min", getEnvironment($"{environmentPrefix}_UDP_RTO_MIN"), sources), "UDP minimum retransmit milliseconds"),
            ParseInteger(ResolveOptional(values, "udp-rto-max", getEnvironment($"{environmentPrefix}_UDP_RTO_MAX"), sources), "UDP maximum retransmit milliseconds"),
            ParseInteger(ResolveOptional(values, "udp-retries", getEnvironment($"{environmentPrefix}_UDP_RETRIES"), sources), "UDP maximum retries"),
            ParseInteger(ResolveOptional(values, "udp-ack-delay", getEnvironment($"{environmentPrefix}_UDP_ACK_DELAY"), sources), "UDP acknowledgement delay milliseconds"));

    private static Dictionary<string, string> ParsePairs(IReadOnlyList<string> args, string[] allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new ArgumentException($"Unknown or incomplete option '{argument}'.");
            }

            string key = argument[2..];
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown option '--{key}'.");
            }

            result[key] = args[++index];
        }

        return result;
    }

    private static string Resolve(Dictionary<string, string> cli, string key, string? environment, string fallback, Dictionary<string, string> sources)
    {
        string value;
        if (cli.TryGetValue(key, out string? cliValue))
        {
            value = cliValue;
            sources[key] = "CLI";
        }
        else if (!string.IsNullOrWhiteSpace(environment))
        {
            value = environment;
            sources[key] = "environment";
        }
        else
        {
            value = fallback;
            sources[key] = "default";
        }

        return value;
    }

    private static string? ResolveOptional(Dictionary<string, string> cli, string key, string? environment, Dictionary<string, string> sources)
    {
        if (cli.TryGetValue(key, out string? cliValue))
        {
            sources[key] = "CLI";
            return cliValue;
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            sources[key] = "environment";
            return environment;
        }

        return null;
    }

    private static int? ParsePort(string? text, string owner)
    {
        if (text is null) return null;
        if (!int.TryParse(text, out int port) || port is < 1 or > 65535)
            throw new ArgumentException($"Invalid {owner} port '{text}'. Expected a number between 1 and 65535.");
        return port;
    }

    private static int? ParseInteger(string? text, string description)
    {
        if (text is null) return null;
        if (!int.TryParse(text, out int value))
            throw new ArgumentException($"Invalid {description} '{text}'. Expected a whole number.");
        return value;
    }

    private static TransportProtocol? ParseProtocol(string? text)
    {
        if (text is null) return null;
        if (!Enum.TryParse(text, true, out TransportProtocol protocol)
            || protocol is not TransportProtocol.Tcp and not TransportProtocol.Udp)
            throw new ArgumentException($"Invalid protocol '{text}'. Select tcp or udp.");
        return protocol;
    }
}
