using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class PayloadCodecTests
	{
		[Fact]
		public void JsonPayloadCodec_RoundTripsTypedPayload()
		{
			var codec = new JsonPayloadCodec();
			var message = new GameStateChangeResponse
			{
				GameStateId = "match",
				GameStatePayload = [1, 2, 3]
			};

			byte[] payload = codec.Encode(message);
			var decoded = codec.Decode<GameStateChangeResponse>(payload);

			Assert.Equal(message.GameStateId, decoded.GameStateId);
			Assert.Equal(message.GameStatePayload, decoded.GameStatePayload);
		}

		[Fact]
		public void JsonPayloadCodec_TryDecodeReturnsFalseForInvalidPayload()
		{
			var codec = new JsonPayloadCodec();

			bool decoded = codec.TryDecode<GameStateChangeResponse>([1, 2, 3], out var value);

			Assert.False(decoded);
			Assert.Null(value);
		}

		[Fact]
		public void ProtocolHelpers_UseProvidedCodec()
		{
			var codec = new CountingPayloadCodec(new JsonPayloadCodec());
			var response = new HandshakeResponse
			{
				PlayerId = 42,
				ReconnectToken = [9, 8, 7],
				Runtime = new ClientRuntimeManifest
				{
					Services = ["chat_service"],
					GameStates = ["lobby"],
					ServiceModules =
					[
						new RuntimeModuleRequirement
						{
							Id = "chat_service",
							Version = "1",
							Required = true
						}
					],
					GameStateModules =
					[
						new RuntimeModuleRequirement
						{
							Id = "lobby",
							Version = "1",
							Required = true
						}
					],
					InitialGameStateId = "lobby",
					InitialGameStatePayload = [1]
				}
			};

			byte[] payload = HandshakeProtocol.EncodeResponse(response, codec);
			var decoded = HandshakeProtocol.DecodeResponse(payload, codec);

			Assert.Equal(1, codec.EncodeCount);
			Assert.Equal(1, codec.DecodeCount);
			Assert.Equal(response.PlayerId, decoded.PlayerId);
			Assert.Equal(response.ReconnectToken, decoded.ReconnectToken);
			Assert.Equal(response.Runtime.ManifestVersion, decoded.Runtime.ManifestVersion);
			Assert.Equal(response.Runtime.ProtocolVersion, decoded.Runtime.ProtocolVersion);
			Assert.Equal(response.Runtime.Services, decoded.Runtime.Services);
			Assert.Equal(response.Runtime.GameStates, decoded.Runtime.GameStates);
			Assert.Equal(response.Runtime.ServiceModules, decoded.Runtime.ServiceModules);
			Assert.Equal(response.Runtime.GameStateModules, decoded.Runtime.GameStateModules);
			Assert.Equal(response.Runtime.InitialGameStateId, decoded.Runtime.InitialGameStateId);
			Assert.Equal(response.Runtime.InitialGameStatePayload, decoded.Runtime.InitialGameStatePayload);
		}

		private sealed class CountingPayloadCodec : IPayloadCodec
		{
			private readonly IPayloadCodec _inner;

			public int EncodeCount { get; private set; }
			public int DecodeCount { get; private set; }

			public CountingPayloadCodec(IPayloadCodec inner)
			{
				_inner = inner;
			}

			public byte[] Encode<T>(T value)
			{
				EncodeCount++;
				return _inner.Encode(value);
			}

			public T Decode<T>(byte[] payload)
			{
				DecodeCount++;
				return _inner.Decode<T>(payload);
			}

			public bool TryDecode<T>(byte[] payload, out T value)
			{
				return _inner.TryDecode(payload, out value);
			}
		}
	}
}
