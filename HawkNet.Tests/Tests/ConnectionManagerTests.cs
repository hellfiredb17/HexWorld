using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HawkNet.Core;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using Xunit;

namespace HawkNet.Tests
{
    public class ConnectionManagerTests
    {
        [Fact]
        public async Task StartAsync_ReturnsQuickly_AndAcceptsClientAfterStartup()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);

            var startTask = manager.StartAsync();
            var completed = await Task.WhenAny(startTask, Task.Delay(500));
            Assert.True(completed == startTask, "ConnectionManager.StartAsync should return after startup.");

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            Assert.True(client.Connected);

            client.Close();
            await manager.StopAsync();
        }

        [Fact]
        public async Task StopAsync_StopsAcceptingNewClients()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            await manager.StartAsync();

            await manager.StopAsync();

            using var client = new TcpClient();
            await Assert.ThrowsAnyAsync<SocketException>(
                () => client.ConnectAsync(IPAddress.Loopback, port));
        }

        [Fact]
        public void Constructor_LogsAndRejectsTlsConfiguration()
        {
            var logger = new CapturingLogger();
            var config = CreateConfig(GetAvailablePort(), useTls: true);

            var exception = Assert.Throws<NotSupportedException>(() => new ConnectionManager(config, logger));

            Assert.Contains("TLS", exception.Message);
            Assert.Contains(logger.Warnings, warning => warning.Contains("TLS is configured but is not supported yet"));
        }

        [Fact]
        public async Task GameHost_BeginAsync_ReturnsAfterStartingConnectionManager()
        {
            int port = GetAvailablePort();
            string configPath = CreateTempConfig(port);
            var host = CreateTestHost(configPath);

            try
            {
                var beginTask = host.BeginAsync();
                var completed = await Task.WhenAny(beginTask, Task.Delay(500));
                Assert.True(completed == beginTask, "GameHost.BeginAsync should return after startup.");

                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                Assert.True(client.Connected);
            }
            finally
            {
                await host.StopAsync();
                File.Delete(configPath);
            }
        }

        [Fact]
        public async Task PacketReceived_IncludesAssignedPlayerId()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var reconnectedTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                var payload = Encoding.UTF8.GetBytes("move");
                await SendPacketAsync(stream, PacketType.PlayerInput, payload);

                var result = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, result.PlayerId);
                Assert.Equal(PacketType.PlayerInput, result.PacketType);
                Assert.Equal(payload, result.Payload);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task ReconnectedSession_PacketReceived_UsesOriginalPlayerId()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var connectionTcs = new TaskCompletionSource<int>();
            var reconnectTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId =>
            {
                connectionTcs.TrySetResult(playerId);
            };
            manager.PlayerReconnected += playerId => reconnectTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using (var firstClient = new TcpClient())
                {
                    await firstClient.ConnectAsync(IPAddress.Loopback, port);
                    await SendHandshakeRequestAsync(firstClient.GetStream());
                    int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    firstClient.Close();

                    await WaitForAsync(
                        () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && !snapshot.IsConnected,
                        TimeSpan.FromSeconds(2));

                    using var reconnectClient = new TcpClient();
                    await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                    var reconnectStream = reconnectClient.GetStream();
                    await SendPacketAsync(reconnectStream, PacketType.ReconnectRequest, GetReconnectPayload(manager, playerId));

                    int reconnectedPlayerId = await reconnectTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.Equal(playerId, reconnectedPlayerId);

                    var payload = Encoding.UTF8.GetBytes("after reconnect");
                    await SendPacketAsync(reconnectStream, PacketType.PlayerInput, payload);

                    var result = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Assert.Equal(playerId, result.PlayerId);
                    Assert.Equal(PacketType.PlayerInput, result.PacketType);
                    Assert.Equal(payload, result.Payload);
                }
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task ReconnectRequest_WithOnlyPlayerId_DoesNotReconnectSession()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var connectionTcs = new TaskCompletionSource<int>();
            int connectionEvents = 0;
            manager.PlayerConnected += playerId =>
            {
                Interlocked.Increment(ref connectionEvents);
                connectionTcs.TrySetResult(playerId);
            };

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(firstClient.GetStream());
                int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                firstClient.Close();

                await WaitForAsync(
                    () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && !snapshot.IsConnected,
                    TimeSpan.FromSeconds(2));

                using var reconnectClient = new TcpClient();
                await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                await SendPacketAsync(reconnectClient.GetStream(), PacketType.ReconnectRequest, BitConverter.GetBytes(playerId));

                await Task.Delay(200);
                Assert.Equal(1, connectionEvents);
                Assert.False(manager.IsPlayerConnected(playerId));
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task ReconnectRequest_WithInvalidToken_DoesNotReconnectSession()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var connectionTcs = new TaskCompletionSource<int>();
            int connectionEvents = 0;
            manager.PlayerConnected += playerId =>
            {
                Interlocked.Increment(ref connectionEvents);
                connectionTcs.TrySetResult(playerId);
            };

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(firstClient.GetStream());
                int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                firstClient.Close();

                await WaitForAsync(
                    () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && !snapshot.IsConnected,
                    TimeSpan.FromSeconds(2));

                byte[] invalidPayload = new byte[ConnectionManager.ReconnectPayloadSize];
                Array.Copy(BitConverter.GetBytes(playerId), 0, invalidPayload, 0, sizeof(int));
                invalidPayload[^1] = 1;

                using var reconnectClient = new TcpClient();
                await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                await SendPacketAsync(reconnectClient.GetStream(), PacketType.ReconnectRequest, invalidPayload);

                await Task.Delay(200);
                Assert.Equal(1, connectionEvents);
                Assert.False(manager.IsPlayerConnected(playerId));
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task GameHost_ConnectionConfirmation_IncludesPlayerIdAndReconnectToken()
        {
            int port = GetAvailablePort();
            string configPath = CreateTempConfig(port);
            var host = CreateTestHost(configPath);

            try
            {
                await host.BeginAsync();

                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                await SendHandshakeRequestAsync(stream);

                var confirmation = await SignalShared.ReadNetworkStream(stream).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(confirmation.Success);
                Assert.Equal(PacketType.HandshakeResponse, confirmation.PacketType);
                var handshake = HandshakeProtocol.DecodeResponse(confirmation.Payload);

                int playerId = handshake.PlayerId;
				Assert.True(playerId >= 1000);
                Assert.Equal(ConnectionManager.ReconnectTokenSize, handshake.ReconnectToken.Length);
                Assert.Contains(handshake.ReconnectToken, value => value != 0);
                Assert.Equal("test", handshake.Runtime.InitialGameStateId);
                Assert.Equal(HandshakeProtocol.CurrentRuntimeManifestVersion, handshake.Runtime.ManifestVersion);
                Assert.Equal(HandshakeProtocol.CurrentProtocolVersion, handshake.Runtime.ProtocolVersion);
                Assert.Contains(handshake.Runtime.ServiceModules, module => module.Id == "player_identity" && module.Version == HandshakeProtocol.CurrentModuleVersion);
            }
            finally
            {
                await host.StopAsync();
                File.Delete(configPath);
            }
        }

        [Fact]
        public async Task Heartbeat_UpdatesLastActive_WithoutRaisingPacketReceived()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var reconnectedTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var beforeSnapshot));
                var beforeHeartbeat = beforeSnapshot.LastActive;

                await Task.Delay(20);
                await stream.WriteAsync(SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat));

                await WaitForAsync(
                    () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && snapshot.LastActive > beforeHeartbeat,
                    TimeSpan.FromSeconds(2));
                Assert.False(packetTcs.Task.IsCompleted, "Heartbeat should not raise PacketReceived.");
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task PacketCounters_RecordInboundPacketsByType()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var reconnectedTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                byte[] heartbeatPayload = Encoding.UTF8.GetBytes("PING");
                await SendPacketAsync(stream, PacketType.HeartBeat, heartbeatPayload);

                await WaitForAsync(
                    () => manager.PacketCounters.GetSnapshot().InboundPackets == 2,
                    TimeSpan.FromSeconds(2));

                var totalSnapshot = manager.PacketCounters.GetSnapshot();
                var handshakeSnapshot = manager.PacketCounters.GetSnapshot(PacketType.HandshakeRequest);
                var heartbeatSnapshot = manager.PacketCounters.GetSnapshot(PacketType.HeartBeat);

                Assert.Equal(2, totalSnapshot.InboundPackets);
                Assert.Equal(GetPacketByteCount(Array.Empty<byte>()) + GetPacketByteCount(heartbeatPayload), totalSnapshot.InboundBytes);
                Assert.Equal(1, handshakeSnapshot.InboundPackets);
                Assert.Equal(GetPacketByteCount(Array.Empty<byte>()), handshakeSnapshot.InboundBytes);
                Assert.Equal(1, heartbeatSnapshot.InboundPackets);
                Assert.Equal(GetPacketByteCount(heartbeatPayload), heartbeatSnapshot.InboundBytes);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task PacketCounters_RecordOutboundPacketsByRecipient()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var connectionTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => connectionTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(client.GetStream());
                int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                var dispatcher = new MessageDispatcher(manager);
                byte[] payload = Encoding.UTF8.GetBytes("server hello");
                await dispatcher.SendToPlayerAsync(playerId, PacketType.ChatMessage, payload);

                var totalSnapshot = manager.PacketCounters.GetSnapshot();
                var chatSnapshot = manager.PacketCounters.GetSnapshot(PacketType.ChatMessage);

                Assert.Equal(1, totalSnapshot.OutboundPackets);
                Assert.Equal(GetPacketByteCount(payload), totalSnapshot.OutboundBytes);
                Assert.Equal(1, chatSnapshot.OutboundPackets);
                Assert.Equal(GetPacketByteCount(payload), chatSnapshot.OutboundBytes);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task DisplayNameUpdatePacket_RoutesWithPlayerId()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(IPAddress.Loopback, port);
            var connectedTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId => connectedTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                byte[] payload = Encoding.UTF8.GetBytes("Pilot");
                await SendPacketAsync(stream, PacketType.DisplayNameUpdate, payload);

                var packet = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, packet.PlayerId);
                Assert.Equal(PacketType.DisplayNameUpdate, packet.PacketType);
                Assert.Equal(payload, packet.Payload);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task InvalidPackets_BelowConfiguredLimit_DoNotDisconnectClient()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, maxInvalidPacketsPerConnection: 2));
            var connectedTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId => connectedTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await SendPacketAsync(stream, PacketType.Unknown, Encoding.UTF8.GetBytes("bad"));
                byte[] payload = Encoding.UTF8.GetBytes("still valid");
                await SendPacketAsync(stream, PacketType.PlayerInput, payload);

                var packet = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, packet.PlayerId);
                Assert.Equal(PacketType.PlayerInput, packet.PacketType);
                Assert.Equal(payload, packet.Payload);
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.True(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task InvalidPackets_AtConfiguredLimit_DisconnectClient()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, maxInvalidPacketsPerConnection: 2));
            var connectedTcs = new TaskCompletionSource<int>();
            var connectionLostTcs = new TaskCompletionSource<(int PlayerId, ConnectionCloseReason Reason)>();
            manager.PlayerConnected += playerId => connectedTcs.TrySetResult(playerId);
            manager.ConnectionLost += (playerId, reason) => connectionLostTcs.TrySetResult((playerId, reason));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await SendPacketAsync(stream, PacketType.Unknown, Encoding.UTF8.GetBytes("bad one"));
                await SendPacketAsync(stream, (PacketType)255, Encoding.UTF8.GetBytes("bad two"));

                var lostConnection = await connectionLostTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, lostConnection.PlayerId);
                Assert.Equal(ConnectionCloseReason.TooManyInvalidPackets, lostConnection.Reason);
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.False(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task PacketRateLimit_WhenExceeded_DisconnectsClient()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                maxPacketsPerWindow: 2,
                packetRateWindowSeconds: 60));
            var connectedTcs = new TaskCompletionSource<int>();
            var connectionLostTcs = new TaskCompletionSource<(int PlayerId, ConnectionCloseReason Reason)>();
            manager.PlayerConnected += playerId => connectedTcs.TrySetResult(playerId);
            manager.ConnectionLost += (playerId, reason) => connectionLostTcs.TrySetResult((playerId, reason));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await stream.WriteAsync(SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat));
                await stream.WriteAsync(SignalShared.BuildHeaderOnlyPacket(PacketType.HeartBeat));

                var lostConnection = await connectionLostTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, lostConnection.PlayerId);
                Assert.Equal(ConnectionCloseReason.PacketRateLimitExceeded, lostConnection.Reason);
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.False(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task MalformedPacket_AfterSessionCreated_DisconnectsWithProtocolErrorAndRecordsDiagnostics()
        {
            int port = GetAvailablePort();
            var diagnostics = new ServerDiagnostics(new DiagnosticsSettings());
            var manager = new ConnectionManager(CreateConfig(port), diagnostics: diagnostics);
            var connectedTcs = new TaskCompletionSource<int>();
            var connectionLostTcs = new TaskCompletionSource<(int PlayerId, ConnectionCloseReason Reason)>();
            manager.PlayerConnected += playerId => connectedTcs.TrySetResult(playerId);
            manager.ConnectionLost += (playerId, reason) => connectionLostTcs.TrySetResult((playerId, reason));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await stream.WriteAsync(new byte[] { 0, 0 });

                var lostConnection = await connectionLostTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, lostConnection.PlayerId);
                Assert.Equal(ConnectionCloseReason.ProtocolError, lostConnection.Reason);
                Assert.Equal(1, diagnostics.GetSnapshot().MalformedPacketReads);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task HeartbeatWatchdog_KeepsConnectionAliveWhenHeartbeatResetsDeadline()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 10,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 1));
            var reconnectedTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();

                await SendHandshakeRequestAsync(stream);
                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await Task.Delay(700);
                await SendPacketAsync(stream, PacketType.HeartBeat, Encoding.UTF8.GetBytes("PING"));
                await Task.Delay(700);

                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.True(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task HeartbeatWatchdog_DisconnectsSilentConnectionWithoutRemovingSession()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 10,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 1));
            var reconnectedTcs = new TaskCompletionSource<int>();
            var connectionLostTcs = new TaskCompletionSource<(int PlayerId, ConnectionCloseReason Reason)>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);
            manager.ConnectionLost += (playerId, reason) => connectionLostTcs.TrySetResult((playerId, reason));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(client.GetStream());
                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                var lostConnection = await connectionLostTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

                Assert.Equal(playerId, lostConnection.PlayerId);
                Assert.Equal(ConnectionCloseReason.HeartbeatTimeout, lostConnection.Reason);
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.False(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task HeartbeatWatchdog_DisconnectedSessionIsRemovedAfterSessionTimeout()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 0,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 1));
            var reconnectedTcs = new TaskCompletionSource<int>();
            var disconnectedTcs = new TaskCompletionSource<(int PlayerId, ConnectionCloseReason Reason)>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);
            manager.Disconnection += (playerId, reason) => disconnectedTcs.TrySetResult((playerId, reason));

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(client.GetStream());
                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await WaitForAsync(
                    () => !manager.TryGetSessionSnapshot(playerId, out _),
                    TimeSpan.FromSeconds(4));
                var disconnection = await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(playerId, disconnection.PlayerId);
                Assert.Equal(ConnectionCloseReason.SessionTimeout, disconnection.Reason);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task HeartbeatWatchdog_ReconnectUsesFreshDeadlineAndOldConnectionCannotClearSession()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 10,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 2));
            var firstConnectionTcs = new TaskCompletionSource<int>();
            var reconnectTcs = new TaskCompletionSource<int>();
            var packetTcs = new TaskCompletionSource<(int PlayerId, PacketType PacketType, byte[] Payload)>();
            manager.PlayerConnected += playerId =>
            {
                firstConnectionTcs.TrySetResult(playerId);
            };
            manager.PlayerReconnected += playerId => reconnectTcs.TrySetResult(playerId);
            manager.PacketReceived += (playerId, packetType, payload) =>
                packetTcs.TrySetResult((playerId, packetType, payload));

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(firstClient.GetStream());
                int playerId = await firstConnectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await Task.Delay(500);

                using var reconnectClient = new TcpClient();
                await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                var reconnectStream = reconnectClient.GetStream();
                await SendPacketAsync(reconnectStream, PacketType.ReconnectRequest, GetReconnectPayload(manager, playerId));

                int reconnectedPlayerId = await reconnectTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(playerId, reconnectedPlayerId);

                await Task.Delay(1500);

                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.True(snapshot.IsConnected);

                var payload = Encoding.UTF8.GetBytes("still connected");
                await SendPacketAsync(reconnectStream, PacketType.PlayerInput, payload);
                var result = await packetTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                Assert.Equal(playerId, result.PlayerId);
                Assert.Equal(PacketType.PlayerInput, result.PacketType);
                Assert.Equal(payload, result.Payload);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task ReconnectBeforeSessionTimeout_PreventsTimeoutRemoval()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 0,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 5,
                maxInvalidPacketsPerConnection: 1));
            var connectionTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => connectionTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(firstClient.GetStream());
                int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                byte[] reconnectPayload = GetReconnectPayload(manager, playerId);

                await SendPacketAsync(firstClient.GetStream(), (PacketType)255, Array.Empty<byte>());
                await WaitForAsync(
                    () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && !snapshot.IsConnected,
                    TimeSpan.FromSeconds(2));

                using var reconnectClient = new TcpClient();
                await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                await SendPacketAsync(reconnectClient.GetStream(), PacketType.ReconnectRequest, reconnectPayload);

                await WaitForAsync(
                    () => manager.TryGetSessionSnapshot(playerId, out var snapshot) && snapshot.IsConnected,
                    TimeSpan.FromSeconds(2));

                await Task.Delay(1500);

                Assert.True(manager.TryGetSessionSnapshot(playerId, out var finalSnapshot));
                Assert.True(finalSnapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task ReplacedConnection_CannotRoutePacketsForSession()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(
                port,
                sessionTimeoutSeconds: 10,
                heartbeatIntervalSeconds: 1,
                heartbeatTimeoutSeconds: 10));
            var connectionTcs = new TaskCompletionSource<int>();
            var reconnectTcs = new TaskCompletionSource<int>();
            int routedPackets = 0;
            manager.PlayerConnected += playerId => connectionTcs.TrySetResult(playerId);
            manager.PlayerReconnected += playerId => reconnectTcs.TrySetResult(playerId);
            manager.PacketReceived += (_, _, _) => Interlocked.Increment(ref routedPackets);

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                var firstStream = firstClient.GetStream();
                await SendHandshakeRequestAsync(firstStream);
                int playerId = await connectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                byte[] reconnectPayload = GetReconnectPayload(manager, playerId);

                using var reconnectClient = new TcpClient();
                await reconnectClient.ConnectAsync(IPAddress.Loopback, port);
                await SendPacketAsync(reconnectClient.GetStream(), PacketType.ReconnectRequest, reconnectPayload);
                await reconnectTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                await SendPacketAsync(firstStream, PacketType.PlayerInput, Encoding.UTF8.GetBytes("stale"));
                await Task.Delay(200);

                Assert.Equal(0, Volatile.Read(ref routedPackets));
                Assert.True(manager.TryGetSessionSnapshot(playerId, out var snapshot));
                Assert.True(snapshot.IsConnected);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task NextPlayerIdStart_ConfiguresFirstAssignedPlayerId()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, nextPlayerIdStart: 5000));
            var reconnectedTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(client.GetStream());

                int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(5000, playerId);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task MaxPlayers_RejectsNewSessionsAfterLimit()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, maxPlayers: 1));
            int connectionEvents = 0;
            var firstConnectionTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId =>
            {
                if (Interlocked.Increment(ref connectionEvents) == 1)
                {
                    firstConnectionTcs.TrySetResult(playerId);
                }
            };

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(firstClient.GetStream());
                await firstConnectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

                using var secondClient = new TcpClient();
                await secondClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(secondClient.GetStream());

                await Task.Delay(200);
                Assert.Equal(1, connectionEvents);
                Assert.Equal(1, manager.SessionCount);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task MaxPlayers_ConcurrentHandshakes_DoNotExceedLimit()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, maxPlayers: 1));
            int connectionEvents = 0;
            manager.PlayerConnected += _ => Interlocked.Increment(ref connectionEvents);

            await manager.StartAsync();

            var clients = new List<TcpClient>();
            try
            {
                for(int i = 0; i < 8; i++)
                {
                    var client = new TcpClient();
                    clients.Add(client);
                    await client.ConnectAsync(IPAddress.Loopback, port);
                }

                await Task.WhenAll(clients.Select(client => SendHandshakeRequestAsync(client.GetStream())));

                await WaitForAsync(
                    () => manager.SessionCount == 1 && Volatile.Read(ref connectionEvents) == 1,
                    TimeSpan.FromSeconds(2));

                Assert.Equal(1, manager.SessionCount);
                Assert.Equal(1, Volatile.Read(ref connectionEvents));
            }
            finally
            {
                foreach(var client in clients)
                {
                    client.Dispose();
                }

                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task MaxConcurrentConnections_RejectsConnectionsAfterLimit()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, maxConcurrentConnections: 1));
            var reconnectTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var firstClient = new TcpClient();
                await firstClient.ConnectAsync(IPAddress.Loopback, port);

                using var secondClient = new TcpClient();
                await secondClient.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(secondClient.GetStream());

                await Task.Delay(200);
                Assert.False(reconnectTcs.Task.IsCompleted);
                Assert.Equal(0, manager.SessionCount);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task AllowedIPs_RejectsRemoteAddressNotInAllowList()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, allowedIPs: new[] { "192.0.2.1" }));
            var reconnectTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await SendHandshakeRequestAsync(client.GetStream());

                await Task.Delay(200);
                Assert.False(reconnectTcs.Task.IsCompleted);
                Assert.Equal(0, manager.SessionCount);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task SessionTimeout_UsesConfiguredHeartbeatIntervalForCleanupCadence()
        {
            int port = GetAvailablePort();
            var manager = new ConnectionManager(CreateConfig(port, sessionTimeoutSeconds: 0, heartbeatIntervalSeconds: 1));
            var reconnectedTcs = new TaskCompletionSource<int>();
            manager.PlayerConnected += playerId => reconnectedTcs.TrySetResult(playerId);

            await manager.StartAsync();

            try
            {
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    await SendHandshakeRequestAsync(client.GetStream());
                    int playerId = await reconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    client.Close();

                    await WaitForAsync(
                        () => !manager.TryGetSessionSnapshot(playerId, out _),
                        TimeSpan.FromSeconds(3));
                }
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public void BuildPacket_ReturnsExpectedFormat()
        {
            var relay = new ConnectionManager(IPAddress.Loopback, 0);
            var payload = Encoding.UTF8.GetBytes("hello");
            var packet = SignalShared.BuildPacket(PacketType.ChatMessage, payload);

            ushort length = (ushort)((packet[0] << 8) | packet[1]);
			Assert.Equal((ushort)(SignalShared.HeaderSize + payload.Length), length);
            Assert.Equal((byte)PacketType.ChatMessage, packet[2]);

            var extracted = new byte[payload.Length];
			Array.Copy(packet, SignalShared.LengthPrefixSize + SignalShared.HeaderSize, extracted, 0, payload.Length);
            Assert.Equal(payload, extracted);
        }

        [Fact]
        public async Task MessageDispatcher_SendToPlayerAsync_WritesPacketToClientStream()
        {
            // Arrange: start temporary listener to create a connected pair
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var relay = new ConnectionManager(IPAddress.Loopback, port);

            var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            var serverTcpClient = await listener.AcceptTcpClientAsync();
            await connectTask;
            listener.Stop();

            // Create server-side ClientConnection using the accepted TcpClient
            var serverConnection = new ClientConnection(serverTcpClient, relay);

            // Add session manually
            var session = new PlayerSession
            {
                PlayerId = 1,
                Connection = serverConnection,
                LastActive = DateTime.UtcNow
            };
            relay.RegisterSession(session);

            // Prepare payload and start reader on client side
            var payload = Encoding.UTF8.GetBytes("broadcast test");
            var clientStream = client.GetStream();

            var readTask = Task.Run(async () =>
            {
                byte[] lenBuf = new byte[2];
                await ReadExactAsync(clientStream, lenBuf, 0, 2);
                ushort len = (ushort)((lenBuf[0] << 8) | lenBuf[1]);

                var rest = new byte[len];
                await ReadExactAsync(clientStream, rest, 0, len);
                return rest;
            });

            // Act: send to player via dispatcher
            var dispatcher = new MessageDispatcher(relay);
            await dispatcher.SendToPlayerAsync(1, PacketType.ChatMessage, payload);

			// Assert: verify client received packet type, timestamp, and payload.
            var received = await readTask;
            Assert.Equal((byte)PacketType.ChatMessage, received[0]);

			var recPayload = new byte[received.Length - SignalShared.HeaderSize];
			Array.Copy(received, SignalShared.HeaderSize, recPayload, 0, recPayload.Length);
            Assert.Equal(payload, recPayload);

            // Cleanup
            await serverConnection.DisconnectAsync();
            client.Close();
            serverTcpClient.Close();
        }

        // Helper to read exact number of bytes from NetworkStream
        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total);
                if (read == 0) throw new InvalidOperationException("Remote closed connection while reading.");
                total += read;
            }
        }

        private static async Task SendPacketAsync(NetworkStream stream, PacketType packetType, byte[] payload)
        {
            var packet = SignalShared.BuildPacket(packetType, payload);
            await stream.WriteAsync(packet, 0, packet.Length);
        }

        private static async Task SendHandshakeRequestAsync(NetworkStream stream)
        {
            await stream.WriteAsync(SignalShared.BuildHeaderOnlyPacket(PacketType.HandshakeRequest));
        }

        private static byte[] GetReconnectPayload(ConnectionManager manager, int playerId)
        {
            Assert.True(manager.TryBuildReconnectPayload(playerId, out var payload));
            return payload;
        }

        private static int GetPacketByteCount(byte[] payload)
        {
			return SignalShared.LengthPrefixSize + SignalShared.HeaderSize + payload.Length;
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("Timed out waiting for condition.");
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string CreateTempConfig(int port)
        {
            string path = Path.Combine(Path.GetTempPath(), $"hawknet-test-{Guid.NewGuid():N}.json");
            var config = CreateConfig(port);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(path, JsonSerializer.Serialize(config, options));
            return path;
        }

        private static GameHostConfig CreateConfig(
            int port,
            int maxPlayers = 64,
            int maxConcurrentConnections = 100,
            int sessionTimeoutSeconds = 30,
            int heartbeatIntervalSeconds = 15,
            int heartbeatTimeoutSeconds = 0,
            int nextPlayerIdStart = 1000,
            int maxInvalidPacketsPerConnection = 5,
            int maxPacketsPerWindow = 300,
            int packetRateWindowSeconds = 10,
            bool useTls = false,
            string[]? allowedIPs = null)
        {
            return new GameHostConfig
            {
                Server = new ServerSettings
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = port,
                    MaxPlayers = maxPlayers,
                    MaxConcurrentConnections = maxConcurrentConnections,
                    SessionTimeoutSeconds = sessionTimeoutSeconds,
                    HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
                    HeartbeatTimeoutSeconds = heartbeatTimeoutSeconds,
                    NextPlayerIdStart = nextPlayerIdStart,
                    UseTls = useTls,
                    AllowedIPs = allowedIPs ?? Array.Empty<string>()
                },
                Networking = new NetworkingSettings
                {
                    Protocol = TransportProtocol.Tcp
                },
                GameMode = new GameModeSettings
                {
                    InitialStateId = "test",
                    States =
                    [
                        new GameModeStateSettings
                        {
                            Id = "test"
                        }
                    ]
                },
                Security = new SecuritySettings
                {
                    MaxInvalidPacketsPerConnection = maxInvalidPacketsPerConnection,
                    MaxPacketsPerWindow = maxPacketsPerWindow,
                    PacketRateWindowSeconds = packetRateWindowSeconds
                }
            };
        }

        private static GameHost CreateTestHost(string configPath)
        {
            return HawkNetServer.Create(configPath, builder =>
                builder.AddGameState("test", context => new TestState(context.Events)));
        }

        private sealed class TestState : GameState
        {
            public TestState(IEventDispatcher eventDispatcher)
                : base(eventDispatcher)
            {
            }

            public override void Enter(byte[] transitionPayload)
            {
            }

            public override void Update(float deltaTime)
            {
            }

            public override void Exit()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<string> Warnings { get; } = new();

            public void Log(string message, string? tag = null)
            {
            }

            public void DebugLog(string message, string? tag = null)
            {
            }

            public void Warning(string message, string? tag = null)
            {
                Warnings.Add(message);
            }

            public void Error(string message, string? tag = null)
            {
            }

            public void Exception(Exception exception, string? message = null, string? tag = null)
            {
            }
        }
    }
}
