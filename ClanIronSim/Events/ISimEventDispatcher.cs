namespace ClanIronSim.Events;

public interface ISimEventDispatcher
{
    void Dispatch(SimEvent simEvent);
}
