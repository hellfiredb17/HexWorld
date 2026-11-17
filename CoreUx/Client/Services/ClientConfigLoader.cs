using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Client;

namespace HawkNet.CoreUx.Client.Services;

public static class ClientConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static GameClientConfig LoadOrDefault(string? path = null)
    {
        string? resolvedPath = path ?? FindDefaultConfigPath();
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return new GameClientConfig();
        }

        try
        {
            string json = File.ReadAllText(resolvedPath);
            return JsonSerializer.Deserialize<GameClientConfig>(json, JsonOptions) ?? new GameClientConfig();
        }
        catch
        {
            return new GameClientConfig();
        }
    }

    public static async Task SaveAsync(GameClientConfig config, string path, CancellationToken token = default)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(path, json, token);
    }

    public static string? FindDefaultConfigPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            string localConfig = Path.Combine(directory.FullName, "Config", "game-client.json");
            if (File.Exists(localConfig))
            {
                return localConfig;
            }

            string repositoryConfig = Path.Combine(directory.FullName, "HawkNet", "Config", "game-client.json");
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
			AllowTrailingCommas = true,
			WriteIndented = true
		};
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}
}
