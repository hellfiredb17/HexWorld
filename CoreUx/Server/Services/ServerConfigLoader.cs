using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Server;

namespace HawkNet.CoreUx.Server.Services;

public static class ServerConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static GameHostConfig LoadOrDefault(string? path = null)
    {
        string? resolvedPath = path ?? FindDefaultConfigPath();
        if (resolvedPath is null)
        {
            return new GameHostConfig();
        }
		if (!File.Exists(resolvedPath)) return new GameHostConfig();

        return ServerConfiguration.Load(resolvedPath);
    }

	public static void Save(string path, GameHostConfig config)
	{
		if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Profile path cannot be empty.", nameof(path));
		ServerConfiguration.Validate(config).ThrowIfInvalid();
		string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
	}

    public static string? FindDefaultConfigPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            string localConfig = Path.Combine(directory.FullName, "Config", "game-server.json");
            if (File.Exists(localConfig))
            {
                return localConfig;
            }

            string repositoryConfig = Path.Combine(directory.FullName, "HawkNet", "Config", "game-server.json");
            if (File.Exists(repositoryConfig))
            {
                return repositoryConfig;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
