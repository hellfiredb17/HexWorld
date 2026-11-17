using ClanIronSim;
using ClanIronSim.CommandLine;
using ClanIronSim.Commands;
using ClanIronSim.HawkNet;
using HawkNet.CommandLine;
using HawkNet.Core.Client;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests;

public sealed class CommandLineExtensionTests
{
    [Fact]
    public void ExtensionsAndCommands_AreDeterministicallyOrdered()
    {
        var later = new FakeExtension("later", 20, new FakeCommand("z", 20));
        var earlier = new FakeExtension("earlier", 10, new FakeCommand("a", 10));

        IReadOnlyList<IHawkNetCliExtension> extensions = CliExtensionRegistry.Resolve([later, earlier]);
        IReadOnlyDictionary<string, IClientConsoleCommand> commands = CliExtensionRegistry.ResolveCommands([], extensions);

        Assert.Equal(["earlier", "later"], extensions.Select(extension => extension.Id));
        Assert.Equal(["a", "z"], commands.Values.Select(command => command.Name));
    }

    [Fact]
    public void DuplicateExtensionAndCommandIds_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => CliExtensionRegistry.Resolve([new FakeExtension("same", 1), new FakeExtension("SAME", 2)]));
        var extensions = CliExtensionRegistry.Resolve([new FakeExtension("one", 1, new FakeCommand("same", 1)), new FakeExtension("two", 2, new FakeCommand("SAME", 2))]);
        Assert.Throws<InvalidOperationException>(() => CliExtensionRegistry.ResolveCommands([], extensions));
    }

    [Fact]
    public void UnknownGameMessage_UsesGenericFallback()
    {
        string output = ClientCliRunner.FormatGameMessage(
            new ClientGameStateMessageReceivedEvent("custom.event", [1, 2, 3]),
            []);

        Assert.Equal("[game] message id=custom.event bytes=3", output);
    }

    [Fact]
    public void ClanIronExtension_ContributesMoveCommandAndInspector()
    {
        var extension = new ClanIronCliExtension();

        Assert.Equal("clan_iron", extension.Id);
        Assert.IsType<ClanIronMoveCommand>(Assert.Single(extension.CreateClientCommands()));
        Assert.IsType<ClanIronGameMessageInspector>(Assert.Single(extension.CreateMessageInspectors()));
    }

    [Fact]
    public void ClanIronMoveCommand_SerializesExpectedGameMessage()
    {
        var codec = new JsonPayloadCodec();

        byte[] payload = ClanIronMoveCommand.CreateMessagePayload(codec, "12.5 -3", out float x, out float z);
        GameStateMessage message = GameStateMessageProtocol.Decode(payload, codec);
        SimCommand command = SimJson.FromJson<SimCommand>(System.Text.Encoding.UTF8.GetString(message.Payload));

        Assert.Equal(12.5f, x);
        Assert.Equal(-3f, z);
        Assert.Equal(ClanIronSimHawkNetMessageIds.SimCommand, message.MessageId);
        Assert.Equal(SimCommandType.PlayerMoveToWorldPosition, command.Type);
        Assert.Equal(12.5f, command.PlayerMoveToWorldPosition?.WorldPosition.X);
        Assert.Equal(-3f, command.PlayerMoveToWorldPosition?.WorldPosition.Z);
    }

    [Fact]
    public void ClanIronInspector_FormatsKnownResponse()
    {
        var response = SimResponse.Empty();
        var gameEvent = new ClientGameStateMessageReceivedEvent(
            ClanIronSimHawkNetMessageIds.SimResponse,
            System.Text.Encoding.UTF8.GetBytes(SimJson.ToJson(response)));

        bool handled = new ClanIronGameMessageInspector().TryFormat(gameEvent, out string message);

        Assert.True(handled);
        Assert.Contains("response type=SimEmpty", message);
    }

    private sealed class FakeExtension(string id, int order, params IClientConsoleCommand[] commands) : IHawkNetCliExtension
    {
        public string Id { get; } = id;
        public int Order { get; } = order;
        public IEnumerable<IClientConsoleCommand> CreateClientCommands() => commands;
    }

    private sealed class FakeCommand(string name, int order) : IClientConsoleCommand
    {
        public string Name { get; } = name;
        public string Usage => Name;
        public string Description => Name;
        public int Order { get; } = order;
        public Task ExecuteAsync(ClientConsoleContext context, string arguments, CancellationToken token) => Task.CompletedTask;
    }
}
