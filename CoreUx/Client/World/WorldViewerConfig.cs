using System.Text.Json;

namespace HawkNet.CoreUx.Client.World;

public sealed record WorldViewerConfig
{
    public int TargetUpdateFramesPerSecond { get; init; } = 60;

    public WorldReconciliationConfig Reconciliation { get; init; } = new();

    public static WorldViewerConfig CreateDefault()
    {
        return new WorldViewerConfig();
    }
}

public sealed record WorldReconciliationConfig
{
    public bool Enabled { get; init; } = true;

    public float IgnoreErrorDistance { get; init; } = 0.15f;

    public float BlendErrorDistance { get; init; } = 0.75f;

    public int BlendMilliseconds { get; init; } = 150;

    public bool SnapOnFailedCanceledOrCompleted { get; init; } = true;
}

public static class WorldViewerConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static WorldViewerConfig LoadOrDefault(string? path = null)
    {
        string? resolvedPath = path ?? FindDefaultConfigPath();
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return WorldViewerConfig.CreateDefault();
        }

        try
        {
            string json = File.ReadAllText(resolvedPath);
            WorldViewerConfig? config = JsonSerializer.Deserialize<WorldViewerConfig>(json, JsonOptions);
            return config is null ? WorldViewerConfig.CreateDefault() : Normalize(config);
        }
        catch
        {
            return WorldViewerConfig.CreateDefault();
        }
    }

    public static string? FindDefaultConfigPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            string localConfig = Path.Combine(directory.FullName, "Config", "world-viewer.json");
            if (File.Exists(localConfig))
            {
                return localConfig;
            }

            string repositoryConfig = Path.Combine(directory.FullName, "CoreUx", "Config", "world-viewer.json");
            if (File.Exists(repositoryConfig))
            {
                return repositoryConfig;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static WorldViewerConfig Normalize(WorldViewerConfig config)
    {
        WorldViewerConfig fallback = WorldViewerConfig.CreateDefault();
        WorldReconciliationConfig reconciliation = config.Reconciliation ?? fallback.Reconciliation;
        WorldReconciliationConfig fallbackReconciliation = fallback.Reconciliation;
        int targetUpdateFramesPerSecond = config.TargetUpdateFramesPerSecond > 0
            ? Math.Clamp(config.TargetUpdateFramesPerSecond, 1, 240)
            : fallback.TargetUpdateFramesPerSecond;

        float ignoreDistance = reconciliation.IgnoreErrorDistance >= 0f
            ? reconciliation.IgnoreErrorDistance
            : fallbackReconciliation.IgnoreErrorDistance;
        float blendDistance = reconciliation.BlendErrorDistance >= ignoreDistance
            ? reconciliation.BlendErrorDistance
            : Math.Max(ignoreDistance, fallbackReconciliation.BlendErrorDistance);

        return config with
        {
            TargetUpdateFramesPerSecond = targetUpdateFramesPerSecond,
            Reconciliation = reconciliation with
            {
                IgnoreErrorDistance = ignoreDistance,
                BlendErrorDistance = blendDistance,
                BlendMilliseconds = reconciliation.BlendMilliseconds > 0
                    ? reconciliation.BlendMilliseconds
                    : fallbackReconciliation.BlendMilliseconds
            }
        };
    }
}
