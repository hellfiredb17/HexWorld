namespace ClanIronSim.Commands;

public sealed class SimResponse
{
    public SimResponseType Type { get; set; } = SimResponseType.SimEmpty;

    public SimEmptyResponsePayload? SimEmpty { get; set; }

    public RegionStateResponsePayload? RegionState { get; set; }

    public SimFailureResponsePayload? Failure { get; set; }

    public static SimResponse Empty()
    {
        return new SimResponse
        {
            Type = SimResponseType.SimEmpty,
            SimEmpty = new SimEmptyResponsePayload()
        };
    }

    public static SimResponse Failed(string reason)
    {
        return new SimResponse
        {
            Type = SimResponseType.Failed,
            Failure = new SimFailureResponsePayload
            {
                Reason = reason ?? string.Empty
            }
        };
    }
}
