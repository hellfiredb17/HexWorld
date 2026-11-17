using System.Text;
using ClanIronSim.Commands;
using ClanIronSim.HawkNet;
using ClanIronSim.SimMath;
using HawkNet.CommandLine;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace ClanIronSim.CommandLine;

public sealed class ClanIronCliExtension : IHawkNetCliExtension
{
    public string Id => "clan_iron";

    public void ConfigureServer(IHawkNetServerBuilder builder) => builder.AddModule(new ClanIronSimMatchModule());

    public IEnumerable<IClientConsoleCommand> CreateClientCommands()
    {
        yield return new ClanIronMoveCommand();
    }

    public IEnumerable<IClientGameMessageInspector> CreateMessageInspectors()
    {
        yield return new ClanIronGameMessageInspector();
    }

}

public sealed class ClanIronMoveCommand : IClientConsoleCommand
{
    public string Name => "move";
    public string Usage => "move <worldX> <worldZ>";
    public string Description => "Send a ClanIron movement command";
    public int Order => 100;

    public async Task ExecuteAsync(ClientConsoleContext context, string arguments, CancellationToken token)
    {
        byte[] messagePayload = CreateMessagePayload(context.Client.Services.Get<IPayloadCodec>(), arguments, out float worldX, out float worldZ);
        await context.Client.GameClient.SendPacketAsync(PacketType.GameStateMessage, messagePayload, DeliveryOptions.ReliableGameplay, token);
        context.Console.WriteLine($"[sim] move sent target=({worldX:0.###}, {worldZ:0.###})");
    }

    public static byte[] CreateMessagePayload(IPayloadCodec codec, string arguments, out float worldX, out float worldZ)
    {
        string[] parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !float.TryParse(parts[0], out worldX)
            || !float.TryParse(parts[1], out worldZ))
            throw new ArgumentException("Usage: move <worldX> <worldZ>");

        var command = new SimCommand
        {
            Type = SimCommandType.PlayerMoveToWorldPosition,
            PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
            {
                WorldPosition = new SimVector2(worldX, worldZ)
            }
        };
        return GameStateMessageProtocol.Encode(
            new GameStateMessage
            {
                MessageId = ClanIronSimHawkNetMessageIds.SimCommand,
                Payload = Encoding.UTF8.GetBytes(SimJson.ToJson(command))
            },
            codec);
    }
}

public sealed class ClanIronGameMessageInspector : IClientGameMessageInspector
{
    public bool TryFormat(ClientGameStateMessageReceivedEvent gameEvent, out string message)
    {
        if (string.Equals(gameEvent.MessageId, ClanIronSimHawkNetMessageIds.SimAction, StringComparison.OrdinalIgnoreCase))
        {
            SimAction action = SimJson.FromJson<SimAction>(Encoding.UTF8.GetString(gameEvent.Payload));
            message = action.Move is not null
                ? $"[sim] move status={action.Move.Status} mover={action.Move.MoverId} player={action.Move.PlayerId?.ToString() ?? "-"} current=({action.Move.CurrentWorldPosition.X:0.###}, {action.Move.CurrentWorldPosition.Z:0.###}) destination=({action.Move.DestinationWorldPosition.X:0.###}, {action.Move.DestinationWorldPosition.Z:0.###})"
                : $"[sim] action type={action.Type} target={action.Target} player={action.PlayerId?.ToString() ?? "-"}";
            return true;
        }

        if (string.Equals(gameEvent.MessageId, ClanIronSimHawkNetMessageIds.SimResponse, StringComparison.OrdinalIgnoreCase))
        {
            SimResponse response = SimJson.FromJson<SimResponse>(Encoding.UTF8.GetString(gameEvent.Payload));
            message = $"[sim] response type={response.Type}";
            return true;
        }

        message = string.Empty;
        return false;
    }
}
