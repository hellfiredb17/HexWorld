namespace ClanIronSim.Entity.Movement;

public interface INavigatorMovementRuntimeBehavior
{
    void Update(NavigatorEntity navigator, float deltaTime);
}
