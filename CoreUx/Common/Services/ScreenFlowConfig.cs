using System.Text.Json;

namespace HawkNet.CoreUx.Common.Services;

public sealed record ScreenFlowConfig
{
    public string InitialView { get; init; } = ScreenKeys.ServerSettings;

    public int DefaultLoadingMilliseconds { get; init; } = 650;

    public Dictionary<string, ScreenTransitionConfig> Transitions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static ScreenFlowConfig CreateDefault()
    {
        return new ScreenFlowConfig
        {
            Transitions = new Dictionary<string, ScreenTransitionConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [TransitionKeys.ServerStarted] = new()
                {
                    From = ScreenKeys.ServerSettings,
                    To = ScreenKeys.ChatLobby,
                    HeaderText = "Starting Server",
                    ContentText = "Preparing the server for the chat lobby."
                },
                [TransitionKeys.StartMatch] = new()
                {
                    From = ScreenKeys.ChatLobby,
                    To = ScreenKeys.Match,
                    HeaderText = "Starting Match",
                    ContentText = "Loading match state and preparing the server dashboard."
                },
                [TransitionKeys.ReturnToLobby] = new()
                {
                    From = ScreenKeys.Match,
                    To = ScreenKeys.ChatLobby,
                    HeaderText = "Returning To Lobby",
                    ContentText = "Closing the match view and refreshing lobby state."
                },
                [TransitionKeys.ClientConnected] = new()
                {
                    From = ScreenKeys.ConnectionSetup,
                    To = ScreenKeys.ChatLobby,
                    HeaderText = "Connecting Client",
                    ContentText = "Opening a connection to the HawkNet server."
                }
            }
        };
    }
}

public sealed record ScreenTransitionConfig
{
    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public string HeaderText { get; init; } = string.Empty;

    public string ContentText { get; init; } = string.Empty;

    public string? ImageKey { get; init; }
}

public static class ScreenKeys
{
    public const string MainMenu = "main_menu";
    public const string ConnectionSetup = "connection_setup";
    public const string ServerSettings = "server_settings";
    public const string ChatLobby = "chat_lobby";
    public const string Match = "match";
}

public static class TransitionKeys
{
    public const string ClientConnected = "client_connected";
    public const string ServerStarted = "server_started";
    public const string StartMatch = "start_match";
    public const string ReturnToLobby = "return_to_lobby";
}

public static class ScreenFlowConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ScreenFlowConfig LoadOrDefault(string? path = null)
    {
        string? resolvedPath = path ?? FindDefaultConfigPath();
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return ScreenFlowConfig.CreateDefault();
        }

        try
        {
            string json = File.ReadAllText(resolvedPath);
            ScreenFlowConfig? config = JsonSerializer.Deserialize<ScreenFlowConfig>(json, JsonOptions);
            return config is null ? ScreenFlowConfig.CreateDefault() : Normalize(config);
        }
        catch
        {
            return ScreenFlowConfig.CreateDefault();
        }
    }

    public static string? FindDefaultConfigPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            string localConfig = Path.Combine(directory.FullName, "Config", "coreux-flow.json");
            if (File.Exists(localConfig))
            {
                return localConfig;
            }

            string repositoryConfig = Path.Combine(directory.FullName, "CoreUx", "Config", "coreux-flow.json");
            if (File.Exists(repositoryConfig))
            {
                return repositoryConfig;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static ScreenFlowConfig Normalize(ScreenFlowConfig config)
    {
        var fallback = ScreenFlowConfig.CreateDefault();
        var transitions = new Dictionary<string, ScreenTransitionConfig>(
            fallback.Transitions,
            StringComparer.OrdinalIgnoreCase);

        foreach (var transition in config.Transitions)
        {
            transitions[transition.Key] = transition.Value;
        }

        return config with
        {
            InitialView = string.IsNullOrWhiteSpace(config.InitialView) ? fallback.InitialView : config.InitialView,
            DefaultLoadingMilliseconds = config.DefaultLoadingMilliseconds > 0
                ? config.DefaultLoadingMilliseconds
                : fallback.DefaultLoadingMilliseconds,
            Transitions = transitions
        };
    }
}
