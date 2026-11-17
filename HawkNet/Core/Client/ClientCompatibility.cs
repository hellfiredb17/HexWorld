using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public enum ClientCompatibilityFailureReason
	{
		UnsupportedManifestVersion,
		UnsupportedProtocolVersion,
		UnknownServiceModule,
		UnknownGameStateModule,
		ServiceModuleListedAsGameState,
		GameStateModuleListedAsService,
		MissingModuleDependency,
		InitialGameStateNotAvailable,
		DuplicateServiceModule,
		DuplicateGameStateModule,
		UnsupportedServiceModuleVersion,
		UnsupportedGameStateModuleVersion
	}

	public sealed class HawkNetClientCompatibilityException : InvalidOperationException
	{
		public HawkNetClientCompatibilityException(
			ClientCompatibilityFailureReason reason,
			string message,
			ClientRuntimeManifest manifest,
			string? moduleId = null,
			string? dependencyId = null,
			string? requestedVersion = null,
			string? supportedVersion = null)
			: base(message)
		{
			Reason = reason;
			Manifest = manifest;
			ModuleId = moduleId;
			DependencyId = dependencyId;
			RequestedVersion = requestedVersion;
			SupportedVersion = supportedVersion;
		}

		public ClientCompatibilityFailureReason Reason { get; }
		public ClientRuntimeManifest Manifest { get; }
		public string? ModuleId { get; }
		public string? DependencyId { get; }
		public string? RequestedVersion { get; }
		public string? SupportedVersion { get; }
	}
}
