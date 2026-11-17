using HawkNet.Core.Client;
using HawkNet.Core.Server;

namespace HawkNet.CommandLine;

public interface IHawkNetCliExtension
{
    string Id { get; }

    int Order => 0;

    void ConfigureServer(IHawkNetServerBuilder builder)
    {
    }

    void ConfigureClient(IHawkNetClientBuilder builder)
    {
    }

    IEnumerable<IClientConsoleCommand> CreateClientCommands() => Array.Empty<IClientConsoleCommand>();

    IEnumerable<IClientGameMessageInspector> CreateMessageInspectors() => Array.Empty<IClientGameMessageInspector>();
}

public interface IClientConsoleCommand
{
    string Name { get; }
    string Usage { get; }
    string Description { get; }
    int Order { get; }
    Task ExecuteAsync(ClientConsoleContext context, string arguments, CancellationToken token);
}

public interface IClientGameMessageInspector
{
    bool TryFormat(ClientGameStateMessageReceivedEvent gameEvent, out string message);
}

public sealed class ClientConsoleContext
{
    private readonly Action _requestShutdown;

    internal ClientConsoleContext(HawkNetClient client, ICliConsole console, Action requestShutdown)
    {
        Client = client;
        Console = console;
        _requestShutdown = requestShutdown;
    }

    public HawkNetClient Client { get; }

    public ICliConsole Console { get; }

    public IReadOnlyDictionary<string, IClientConsoleCommand> Commands { get; internal set; } =
        new Dictionary<string, IClientConsoleCommand>();

    public void RequestShutdown() => _requestShutdown();
}

public sealed class DelegateClientConsoleCommand : IClientConsoleCommand
{
    private readonly Func<ClientConsoleContext, string, CancellationToken, Task> _execute;

    public DelegateClientConsoleCommand(
        string name,
        string usage,
        string description,
        int order,
        Func<ClientConsoleContext, string, CancellationToken, Task> execute)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Command name cannot be empty.", nameof(name)) : name.Trim();
        Usage = usage;
        Description = description;
        Order = order;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public string Name { get; }
    public string Usage { get; }
    public string Description { get; }
    public int Order { get; }

    public Task ExecuteAsync(ClientConsoleContext context, string arguments, CancellationToken token) =>
        _execute(context, arguments, token);
}

public static class CliExtensionRegistry
{
    public static IReadOnlyList<IHawkNetCliExtension> Resolve(IEnumerable<IHawkNetCliExtension>? extensions)
    {
        IHawkNetCliExtension[] resolved = (extensions ?? Array.Empty<IHawkNetCliExtension>())
            .OrderBy(extension => extension.Order)
            .ThenBy(extension => extension.Id, StringComparer.Ordinal)
            .ToArray();
        string? duplicate = resolved.GroupBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate CLI extension id '{duplicate}'.");
        }

        return resolved;
    }

    public static IReadOnlyDictionary<string, IClientConsoleCommand> ResolveCommands(
        IEnumerable<IClientConsoleCommand> builtIns,
        IEnumerable<IHawkNetCliExtension> extensions)
    {
        IClientConsoleCommand[] commands = builtIns
            .Concat(extensions.SelectMany(extension => extension.CreateClientCommands()))
            .OrderBy(command => command.Order)
            .ThenBy(command => command.Name, StringComparer.Ordinal)
            .ToArray();
        string? duplicate = commands.GroupBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate client command name '{duplicate}'.");
        }

        return commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }
}
