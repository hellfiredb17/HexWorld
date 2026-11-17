namespace ClanIronSim.Commands;

public sealed class SimRequest
{
    public SimRequestType Type { get; set; } = SimRequestType.SimEmpty;

    public RequestRegionStatePayload? RequestRegionState { get; set; }
}
