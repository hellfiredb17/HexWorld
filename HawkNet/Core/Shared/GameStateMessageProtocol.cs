namespace HawkNet.Core.Shared
{
	public sealed record GameStateMessage
	{
		public string MessageId { get; init; } = string.Empty;
		public byte[] Payload { get; init; } = [];
	}

	public static class GameStateMessageProtocol
	{
		private static readonly IPayloadCodec DefaultCodec = new JsonPayloadCodec();

		public static byte[] Encode(GameStateMessage message)
		{
			return Encode(message, DefaultCodec);
		}

		public static byte[] Encode(GameStateMessage message, IPayloadCodec codec)
		{
			if(message is null)
			{
				throw new ArgumentNullException(nameof(message));
			}

			HawkNet.Core.Guard.NotNull(codec);
			return codec.Encode(message);
		}

		public static GameStateMessage Decode(byte[] payload)
		{
			return Decode(payload, DefaultCodec);
		}

		public static GameStateMessage Decode(byte[] payload, IPayloadCodec codec)
		{
			HawkNet.Core.Guard.NotNull(payload);
			HawkNet.Core.Guard.NotNull(codec);
			return codec.Decode<GameStateMessage>(payload);
		}
	}
}
