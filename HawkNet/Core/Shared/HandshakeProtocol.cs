/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Shared
{
	public sealed record RuntimeModuleRequirement
	{
		public string Id { get; init; } = string.Empty;
		public string Version { get; init; } = HandshakeProtocol.CurrentModuleVersion;
		public bool Required { get; init; } = true;
	}

	public sealed record ClientRuntimeManifest
	{
		public int ManifestVersion { get; init; } = HandshakeProtocol.CurrentRuntimeManifestVersion;
		public string ProtocolVersion { get; init; } = HandshakeProtocol.CurrentProtocolVersion;
		public string[] Services { get; init; } = [];
		public string[] GameStates { get; init; } = [];
		public RuntimeModuleRequirement[] ServiceModules { get; init; } = [];
		public RuntimeModuleRequirement[] GameStateModules { get; init; } = [];
		public string InitialGameStateId { get; init; } = string.Empty;
		public byte[] InitialGameStatePayload { get; init; } = [];
	}

	public sealed record HandshakeResponse
	{
		public int PlayerId { get; init; }
		public byte[] ReconnectToken { get; init; } = Array.Empty<byte>();
		public ClientRuntimeManifest Runtime { get; init; } = new();
	}

	public static class HandshakeProtocol
	{
		public const int CurrentRuntimeManifestVersion = 1;
		public const string CurrentProtocolVersion = "3";
		public const string CurrentModuleVersion = "1";

		private static readonly IPayloadCodec DefaultCodec = new JsonPayloadCodec();

		public static byte[] EncodeResponse(HandshakeResponse response)
		{
			return EncodeResponse(response, DefaultCodec);
		}

		public static byte[] EncodeResponse(HandshakeResponse response, IPayloadCodec codec)
		{
			if(response is null)
			{
				throw new ArgumentNullException(nameof(response));
			}

			HawkNet.Core.Guard.NotNull(codec);
			return codec.Encode(response);
		}

		public static HandshakeResponse DecodeResponse(byte[] payload)
		{
			return DecodeResponse(payload, DefaultCodec);
		}

		public static HandshakeResponse DecodeResponse(byte[] payload, IPayloadCodec codec)
		{
			if(payload is null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			HawkNet.Core.Guard.NotNull(codec);
			return codec.Decode<HandshakeResponse>(payload);
		}
	}
}
