namespace ClanIronSim;

public sealed class ClanIronSimulation
{
    public ClanIronSimulation()
        : this(new ClanIronSimulationOptions())
    {
    }

    public ClanIronSimulation(ClanIronSimulationOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ClanIronSimulationOptions Options { get; }
}
