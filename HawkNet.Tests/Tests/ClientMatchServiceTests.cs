using System.Net;
using System.Net.Sockets;
using HawkNet.Core.Client;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientMatchServiceTests
	{
		[Fact]
		public async Task SendCommandAsync_SendsMatchCommandRequestPacket()
		{
			using var listener = StartListener(out int port);
			var codec = new JsonPayloadCodec();
			var client = CreateClient(port, codec);
			var matchService = new ClientMatchService(client, codec);

			var connectTask = client.ConnectAsync();
			using var serverClient = await listener.AcceptTcpClientAsync();
			var stream = serverClient.GetStream();
			await ReadPacketAsync(stream);
			await SendHandshakeResponseAsync(stream, 1, CreateReconnectToken(1), codec);
			await connectTask.WaitAsync(TimeSpan.FromSeconds(2));

			byte[] commandPayload = [9, 8, 7];
			await matchService.SendCommandAsync("move", commandPayload);

			var packet = await ReadPacketAsync(stream);
			Assert.Equal(PacketType.MatchCommandRequest, packet.PacketType);
			var request = codec.Decode<MatchCommandRequest>(packet.Payload);
			Assert.Equal("move", request.CommandId);
			Assert.Equal(commandPayload, request.Payload);

			await client.DisconnectAsync();
		}

		[Fact]
		public async Task SendCommandAsync_RejectsEmptyCommandId()
		{
			var service = new ClientMatchService(new GameClient("127.0.0.1", 1));

			await Assert.ThrowsAsync<ArgumentException>(() =>
				service.SendCommandAsync(" ", Array.Empty<byte>()));
		}

		private static GameClient CreateClient(int port, IPayloadCodec codec)
		{
			return new GameClient(
				new GameClientConfig
				{
					Server = new GameClientServerSettings
					{
						Host = "127.0.0.1",
						Port = port
					}
				},
				codec);
		}

		private static TcpListener StartListener(out int port)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			port = ((IPEndPoint)listener.LocalEndpoint).Port;
			return listener;
		}

		private static async Task<ReadNetworkPacketResult> ReadPacketAsync(NetworkStream stream)
		{
			var result = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
			Assert.True(result.Success, result.ErrorMsg);
			return result;
		}

		private static Task SendHandshakeResponseAsync(
			NetworkStream stream,
			int playerId,
			byte[] reconnectToken,
			IPayloadCodec codec)
		{
			byte[] payload = HandshakeProtocol.EncodeResponse(
				new HandshakeResponse
				{
					PlayerId = playerId,
					ReconnectToken = reconnectToken,
					Runtime = new ClientRuntimeManifest
					{
						Services = ["player_identity", "diagnostics", "chat_service"],
						GameStates = ["match"],
						InitialGameStateId = "match",
						InitialGameStatePayload = Array.Empty<byte>()
					}
				},
				codec);
			byte[] packet = SignalShared.BuildPacket(PacketType.HandshakeResponse, payload);
			return stream.WriteAsync(packet, 0, packet.Length);
		}

		private static byte[] CreateReconnectToken(byte seed)
		{
			return Enumerable.Range(0, ConnectionManager.ReconnectTokenSize)
				.Select(index => (byte)(seed + index))
				.ToArray();
		}
	}
}
