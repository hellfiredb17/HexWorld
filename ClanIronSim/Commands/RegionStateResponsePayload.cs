namespace ClanIronSim.Commands;

public sealed class RegionStateResponsePayload
{
    public string RegionId { get; set; } = string.Empty;

    public List<SimInteractableState> Interactables { get; set; } = new();
}
