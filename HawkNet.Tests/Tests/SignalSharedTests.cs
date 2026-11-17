using System.Net;
using System.Net.Sockets;
using HawkNet.Core.Shared;
using Xunit;

namespace HawkNet.Tests
{
	public class SignalSharedTests
	{
		[Fact]
		public void BuildPacket_AllowsEmptyPayload()
		{
			var packet = SignalShared.BuildPacket(PacketType.HeartBeat, Array.Empty<byte>());

			ushort length = (ushort)((packet[0] << 8) | packet[1]);
			Assert.Equal(SignalShared.HeaderSize, length);
			Assert.Equal((byte)PacketType.HeartBeat, packet[SignalShared.LengthPrefixSize]);
			Assert.Equal(SignalShared.LengthPrefixSize + SignalShared.HeaderSize, packet.Length);
		}

		[Fact]
		public void BuildHeaderOnlyPacket_ReturnsPacketTypeWithoutPayload()
		{
			var packet = SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat);

			ushort length = (ushort)((packet[0] << 8) | packet[1]);
			Assert.Equal(SignalShared.HeaderSize, length);
			Assert.Equal((byte)PacketType.HeartBeat, packet[SignalShared.LengthPrefixSize]);
			Assert.Equal(SignalShared.LengthPrefixSize + SignalShared.HeaderSize, packet.Length);
		}

		[Fact]
		public void BuildPacket_ThrowsForNullPayload()
		{
			Assert.Throws<ArgumentNullException>(() => SignalShared.BuildPacket(PacketType.ChatMessage, null!));
		}

		[Fact]
		public void BuildPacket_ThrowsForOversizedPayload()
		{
			var payload = new byte[SignalShared.MaxPayloadLength + 1];

			Assert.Throws<ArgumentOutOfRangeException>(() => SignalShared.BuildPacket(PacketType.ChatMessage, payload));
		}

		[Fact]
		public async Task ReadNetworkStream_ReturnsFailureForZeroLengthPacket()
		{
			await using var pair = await ConnectedNetworkPair.CreateAsync();
			await pair.ClientStream.WriteAsync(new byte[] { 0, 0 });

			var result = await SignalShared.ReadNetworkStream(pair.ServerStream).WaitAsync(TimeSpan.FromSeconds(2));

			Assert.False(result.Success);
			Assert.Equal(PacketType.Unknown, result.PacketType);
			Assert.Empty(result.Payload);
			Assert.Contains("Invalid packet length", result.ErrorMsg);
		}

		[Fact]
		public async Task ReadNetworkStream_ReturnsHeaderOnlyPacketWithEmptyPayload()
		{
			await using var pair = await ConnectedNetworkPair.CreateAsync();
			await pair.ClientStream.WriteAsync(SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat));

			var result = await SignalShared.ReadNetworkStream(pair.ServerStream).WaitAsync(TimeSpan.FromSeconds(2));

			Assert.True(result.Success);
			Assert.Equal(PacketType.HeartBeat, result.PacketType);
			Assert.InRange(result.SentAtUnixMilliseconds,
				DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds(),
				DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeMilliseconds());
			Assert.Empty(result.Payload);
		}

		[Fact]
		public async Task BuildAndReadPacket_PreservesExplicitTimestampAndPayload()
		{
			const long timestamp = 1_725_000_123_456;
			byte[] payload = [10, 20, 30];
			await using var stream = new MemoryStream(SignalShared.BuildPacket(PacketType.PlayerInput, payload, timestamp));

			ReadNetworkPacketResult result = await SignalShared.ReadNetworkStream(stream);

			Assert.True(result.Success);
			Assert.Equal(timestamp, result.SentAtUnixMilliseconds);
			Assert.Equal(payload, result.Payload);
		}

		[Fact]
		public async Task ReadNetworkStream_ReturnsFailureForIncompletePacketBody()
		{
			await using var pair = await ConnectedNetworkPair.CreateAsync();
			await pair.ClientStream.WriteAsync(new byte[] { 0, SignalShared.HeaderSize + 1, (byte)PacketType.ChatMessage });
			pair.Client.Close();

			var result = await SignalShared.ReadNetworkStream(pair.ServerStream).WaitAsync(TimeSpan.FromSeconds(2));

			Assert.False(result.Success);
			Assert.Equal(PacketType.Unknown, result.PacketType);
			Assert.Empty(result.Payload);
			Assert.Contains("Unable to read packet", result.ErrorMsg);
		}

		private sealed class ConnectedNetworkPair : IAsyncDisposable
		{
			public TcpClient Client { get; }
			public TcpClient Server { get; }
			public NetworkStream ClientStream => Client.GetStream();
			public NetworkStream ServerStream => Server.GetStream();

			private ConnectedNetworkPair(TcpClient client, TcpClient server)
			{
				Client = client;
				Server = server;
			}

			public static async Task<ConnectedNetworkPair> CreateAsync()
			{
				var listener = new TcpListener(IPAddress.Loopback, 0);
				listener.Start();
				int port = ((IPEndPoint)listener.LocalEndpoint).Port;

				var client = new TcpClient();
				var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
				var server = await listener.AcceptTcpClientAsync();
				await connectTask;
				listener.Stop();

				return new ConnectedNetworkPair(client, server);
			}

			public ValueTask DisposeAsync()
			{
				Client.Dispose();
				Server.Dispose();
				return ValueTask.CompletedTask;
			}
		}
	}
}
