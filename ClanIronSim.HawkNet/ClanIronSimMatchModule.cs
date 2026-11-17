using HawkNet.Core.Server;

namespace ClanIronSim.HawkNet;

public sealed class ClanIronSimMatchModule : IHawkNetServerModule
{
    private readonly ClanIronSimMatchSettings _settings;

    public ClanIronSimMatchModule()
        : this(new ClanIronSimMatchSettings())
    {
    }

    public ClanIronSimMatchModule(ClanIronSimMatchSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Configure(IHawkNetServerBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }
		ClanIronSimMatchSettings settings = builder.GetOptions("clan_iron", _settings);

        builder.RegisterMatchVariant(
            string.IsNullOrWhiteSpace(settings.VariantId) ? "clan_iron" : settings.VariantId,
            _ => new ClanIronSimMatch(settings));
    }
}
