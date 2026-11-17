using System.Net;
using System.Net.Sockets;
using System.Text;
using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
	public class ServerDiagnosticsTests
	{
		[Fact]
		public void GetSnapshot_IncludesPacketCountsSizesAndTypeBreakdown()
		{
			var packetCounters = new PacketCounters();
			packetCounters.RecordInbound(PacketType.ChatMessage, 10);
			packetCounters.RecordInbound(PacketType.ChatMessage, 20);
			packetCounters.RecordOutbound(PacketType.PlayerInput, 30);
			var diagnostics = new ServerDiagnostics(
				new DiagnosticsSettings(),
				getPacketTotals: packetCounters.GetSnapshot,
				getPacketTypes: packetCounters.GetSnapshotsByType);

			var snapshot = diagnostics.GetSnapshot();
			var chat = snapshot.PacketTypes.Single(packet => packet.PacketType == PacketType.ChatMessage);
			var input = snapshot.PacketTypes.Single(packet => packet.PacketType == PacketType.PlayerInput);

			Assert.Equal(2, snapshot.PacketTotals.InboundPackets);
			Assert.Equal(30, snapshot.PacketTotals.InboundBytes);
			Assert.Equal(15, snapshot.PacketTotals.AverageInboundPacketBytes);
			Assert.Equal(20, snapshot.PacketTotals.MaxInboundPacketBytes);
			Assert.Equal(1, snapshot.PacketTotals.OutboundPackets);
			Assert.Equal(30, snapshot.PacketTotals.OutboundBytes);
			Assert.Equal(30, snapshot.PacketTotals.AverageOutboundPacketBytes);
			Assert.Equal(30, snapshot.PacketTotals.MaxOutboundPacketBytes);
			Assert.Equal(2, chat.InboundPackets);
			Assert.Equal(15, chat.AverageInboundPacketBytes);
			Assert.Equal(20, chat.MaxInboundPacketBytes);
			Assert.Equal(1, input.OutboundPackets);
			Assert.Equal(30, input.MaxOutboundPacketBytes);
		}

		[Fact]
		public async Task ChatService_RecordsAcceptedRejectedSizesAndProcessingTimings()
		{
			var dispatcher = new EventDispatcher();
			var diagnostics = new ServerDiagnostics(new DiagnosticsSettings());
			using var chatService = new ChatService(
				dispatcher,
				new ChatSettings
				{
					MaxMessageBytes = 4,
					MaxMessagesPerWindow = 1,
					MessageRateWindowSeconds = 60
				},
				logger: NullLogger.Instance,
				diagnostics: diagnostics);

			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("ok")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(1, Encoding.UTF8.GetBytes("spam")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(2, Encoding.UTF8.GetBytes("12345")));
			await dispatcher.DispatchAsync(new ChatMessageReceivedEvent(3, Encoding.UTF8.GetBytes("   ")));

			var chat = diagnostics.GetSnapshot().Chat;
			Assert.Equal(1, chat.AcceptedMessages);
			Assert.Equal(1, chat.RateLimitRejectedMessages);
			Assert.Equal(1, chat.ByteLimitRejectedMessages);
			Assert.Equal(1, chat.EmptyMessagesIgnored);
			Assert.Equal(2, chat.PayloadBytes);
			Assert.Equal(2, chat.AveragePayloadBytes);
			Assert.Equal(2, chat.MaxPayloadBytes);
			Assert.Equal(4, chat.Processing.Count);
			Assert.True(chat.Processing.MaxMilliseconds >= 0);
		}

		[Fact]
		public async Task MessageDispatcher_RecordsOutboundSendTiming()
		{
			var manager = new ConnectionManager(IPAddress.Loopback, 0);
			var diagnostics = new ServerDiagnostics(
				new DiagnosticsSettings(),
				getPacketTotals: manager.PacketCounters.GetSnapshot,
				getPacketTypes: manager.PacketCounters.GetSnapshotsByType);
			var dispatcher = new MessageDispatcher(manager, diagnostics: diagnostics);
			await using var pair = await ConnectedClientPair.CreateAsync(manager, playerId: 1);

			await dispatcher.SendToPlayerAsync(1, PacketType.ChatMessage, Encoding.UTF8.GetBytes("hello"));

			var snapshot = diagnostics.GetSnapshot();
			Assert.Equal(1, snapshot.OutboundSend.Count);
			Assert.Equal(1, snapshot.PacketTotals.OutboundPackets);
			Assert.True(snapshot.OutboundSend.MaxMilliseconds >= 0);
		}

		[Fact]
		public void GetSnapshot_IncludesDisconnectReasonCountsAndUpdateLoopMetrics()
		{
			var updateMetrics = new UpdateLoopMetricsSnapshot(
				FixedDelta: 0.02f,
				MaxCatchUpTicks: 3,
				TotalTicks: 7,
				LastTickDurationMilliseconds: 1,
				AverageTickDurationMilliseconds: 2,
				MaxTickDurationMilliseconds: 4,
				CatchUpTicks: 1,
				CatchUpLimitHitCount: 2,
				SkippedCatchUpTicks: 5);
			var diagnostics = new ServerDiagnostics(
				new DiagnosticsSettings(),
				getSessionCount: () => 4,
				getConnectedPlayerCount: () => 2,
				getUpdateLoopMetrics: () => updateMetrics);

			diagnostics.RecordDisconnect(ConnectionCloseReason.HeartbeatTimeout);
			diagnostics.RecordDisconnect(ConnectionCloseReason.HeartbeatTimeout);
			diagnostics.RecordDisconnect(ConnectionCloseReason.SessionTimeout);
			diagnostics.RecordMalformedPacketRead();
			diagnostics.RecordMalformedPacketRead();

			var snapshot = diagnostics.GetSnapshot();
			Assert.Equal(4, snapshot.SessionCount);
			Assert.Equal(2, snapshot.ConnectedPlayerCount);
			Assert.Equal(7, snapshot.UpdateLoop.TotalTicks);
			Assert.Equal(2, snapshot.Disconnects.Single(item => item.Reason == ConnectionCloseReason.HeartbeatTimeout).Count);
			Assert.Equal(1, snapshot.Disconnects.Single(item => item.Reason == ConnectionCloseReason.SessionTimeout).Count);
			Assert.Equal(2, snapshot.MalformedPacketReads);
		}

		[Fact]
		public void DiagnosticsSettings_DefaultsDisableConsoleLoggingAndUseThirtySecondInterval()
		{
			var settings = new DiagnosticsSettings();
			var diagnostics = new ServerDiagnostics(new DiagnosticsSettings { LogIntervalSeconds = -10 });

			Assert.False(settings.EnableConsoleLogging);
			Assert.Equal(30, settings.LogIntervalSeconds);
			Assert.Equal(1, diagnostics.LogIntervalSeconds);
		}

		[Fact]
		public async Task StartAsync_WhenEnabled_LogsDiagnosticsSummary()
		{
			var logger = new CapturingLogger();
			var diagnostics = new ServerDiagnostics(
				new DiagnosticsSettings
				{
					EnableConsoleLogging = true,
					LogIntervalSeconds = 1
				},
				logger,
				getSessionCount: () => 2,
				getConnectedPlayerCount: () => 1);

			await diagnostics.StartAsync();
			try
			{
				await WaitForAsync(
					() => logger.Messages.Any(message => message.Tag == nameof(ServerDiagnostics)),
					TimeSpan.FromSeconds(2));
			}
			finally
			{
				await diagnostics.StopAsync();
			}

			Assert.Contains(logger.Messages, message =>
				message.Tag == nameof(ServerDiagnostics)
				&& message.Message.Contains("Sessions=2")
				&& message.Message.Contains("Connected=1"));
		}

		[Fact]
		public async Task StartAsync_WhenDisabled_DoesNotStartBackgroundLogging()
		{
			var logger = new CapturingLogger();
			var diagnostics = new ServerDiagnostics(
				new DiagnosticsSettings
				{
					EnableConsoleLogging = false,
					LogIntervalSeconds = 1
				},
				logger);

			await diagnostics.StartAsync();
			await Task.Delay(100);
			await diagnostics.StopAsync();

			Assert.Empty(logger.Messages);
		}

		private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
		{
			var start = DateTime.UtcNow;
			while(DateTime.UtcNow - start < timeout)
			{
				if(condition())
				{
					return;
				}

				await Task.Delay(10);
			}

			throw new TimeoutException("Timed out waiting for condition.");
		}

		private sealed class CapturingLogger : ILogger
		{
			private readonly List<(string Message, string? Tag)> _messages = new();

			public IReadOnlyList<(string Message, string? Tag)> Messages => _messages;

			public void Log(string message, string? tag = null)
			{
				_messages.Add((message, tag));
			}

			public void DebugLog(string message, string? tag = null)
			{
			}

			public void Warning(string message, string? tag = null)
			{
			}

			public void Error(string message, string? tag = null)
			{
			}

			public void Exception(Exception exception, string? message = null, string? tag = null)
			{
			}
		}

		private sealed class ConnectedClientPair : IAsyncDisposable
		{
			private readonly TcpClient _serverTcpClient;
			private readonly ClientConnection _serverConnection;

			public TcpClient Client { get; }

			private ConnectedClientPair(
				TcpClient client,
				TcpClient serverTcpClient,
				ClientConnection serverConnection)
			{
				Client = client;
				_serverTcpClient = serverTcpClient;
				_serverConnection = serverConnection;
			}

			public static async Task<ConnectedClientPair> CreateAsync(ConnectionManager connectionManager, int playerId)
			{
				var listener = new TcpListener(IPAddress.Loopback, 0);
				listener.Start();
				int port = ((IPEndPoint)listener.LocalEndpoint).Port;

				var client = new TcpClient();
				var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
				var serverTcpClient = await listener.AcceptTcpClientAsync();
				await connectTask;
				listener.Stop();

				var serverConnection = new ClientConnection(serverTcpClient, connectionManager);
				connectionManager.RegisterSession(new PlayerSession
				{
					PlayerId = playerId,
					Connection = serverConnection,
					LastActive = DateTime.UtcNow
				});

				return new ConnectedClientPair(client, serverTcpClient, serverConnection);
			}

			public async ValueTask DisposeAsync()
			{
				await _serverConnection.DisconnectAsync();
				Client.Close();
				_serverTcpClient.Close();
			}
		}
	}
}
