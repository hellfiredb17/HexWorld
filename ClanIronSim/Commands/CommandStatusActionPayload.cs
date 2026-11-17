namespace ClanIronSim.Commands;

public sealed class CommandStatusActionPayload
{
    public SimCommandType CommandType { get; set; }

    public string SourceId { get; set; } = string.Empty;

    public CommandStatus Status { get; set; } = CommandStatus.Rejected;

    public string Reason { get; set; } = string.Empty;
}
