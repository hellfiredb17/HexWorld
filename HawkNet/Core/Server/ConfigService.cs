namespace HawkNet.Core.Server
{
	public interface IConfigService
	{
		GameHostConfig Root { get; }

		TSettings Get<TSettings>()
			where TSettings : class;
	}

	public sealed class ConfigService : IConfigService
	{
		private readonly Dictionary<Type, object> _customSettings = new();
		public ConfigService(GameHostConfig root)
		{
			Root = root ?? throw new ArgumentNullException(nameof(root));
		}

		public GameHostConfig Root { get; }

		public TSettings Get<TSettings>()
			where TSettings : class
		{
			object settings = typeof(TSettings) switch
			{
				Type type when type == typeof(GameHostConfig) => Root,
				Type type when type == typeof(ServerSettings) => Root.Server,
				Type type when type == typeof(NetworkingSettings) => Root.Networking,
				Type type when type == typeof(SimulationSettings) => Root.Simulation,
				Type type when type == typeof(ChatSettings) => Root.Chat,
				Type type when type == typeof(PlayerIdentitySettings) => Root.PlayerIdentity,
				Type type when type == typeof(ChatLobbySettings) => Root.ChatLobby,
				Type type when type == typeof(MatchSettings) => Root.Match,
				Type type when type == typeof(GameModeSettings) => Root.GameMode,
				Type type when type == typeof(SecuritySettings) => Root.Security,
				Type type when type == typeof(DiagnosticsSettings) => Root.Diagnostics,
				Type type when type == typeof(MaintenanceSettings) => Root.Maintenance,
				Type type when type == typeof(ModuleSettings) => Root.Modules,
				_ when _customSettings.TryGetValue(typeof(TSettings), out object? custom) => custom,
				_ => throw new InvalidOperationException($"No server config settings are registered for type '{typeof(TSettings).Name}'.")
			};

			return (TSettings)settings;
		}

		public void Register<TSettings>(TSettings settings) where TSettings : class
		{
			_customSettings[typeof(TSettings)] = settings ?? throw new ArgumentNullException(nameof(settings));
		}
	}
}
