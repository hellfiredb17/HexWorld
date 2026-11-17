/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Shared
{
	public sealed record GameStateChangeResponse
	{
		public string GameStateId { get; init; } = string.Empty;
		public byte[] GameStatePayload { get; init; } = Array.Empty<byte>();
	}

	public static class GameStateProtocol
	{
		private static readonly IPayloadCodec DefaultCodec = new JsonPayloadCodec();

		public static byte[] EncodeChange(GameStateChangeResponse message)
		{
			return EncodeChange(message, DefaultCodec);
		}

		public static byte[] EncodeChange(GameStateChangeResponse message, IPayloadCodec codec)
		{
			if(message is null)
			{
				throw new ArgumentNullException(nameof(message));
			}

			HawkNet.Core.Guard.NotNull(codec);
			return codec.Encode(message);
		}

		public static GameStateChangeResponse DecodeChange(byte[] payload)
		{
			return DecodeChange(payload, DefaultCodec);
		}

		public static GameStateChangeResponse DecodeChange(byte[] payload, IPayloadCodec codec)
		{
			if(payload is null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			HawkNet.Core.Guard.NotNull(codec);
			return codec.Decode<GameStateChangeResponse>(payload);
		}
	}
}
