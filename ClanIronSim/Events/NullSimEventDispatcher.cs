namespace ClanIronSim.Events;

public sealed class NullSimEventDispatcher : ISimEventDispatcher
{
    public static readonly NullSimEventDispatcher Instance = new();

    private NullSimEventDispatcher()
    {
    }

    public void Dispatch(SimEvent simEvent)
    {
    }
}
