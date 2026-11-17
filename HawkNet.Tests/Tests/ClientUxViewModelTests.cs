using HawkNet.Core.Client;
using HawkNet.Core.Shared;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.Services;
using HawkNet.CoreUx.Client.ViewModels;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Common.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class ClientUxViewModelTests
	{
		[Fact]
		public void ConnectionSetup_ToConfig_MapsEditableSettings()
		{
			var viewModel = new ConnectionSetupViewModel(new GameClientConfig(), new MockClientLauncher())
			{
				Host = "localhost",
				Port = 8888,
				Protocol = TransportProtocol.Udp,
				HeartbeatIntervalSeconds = 10,
				ReconnectEnabled = false,
				ReceiveBufferSize = 4096,
				SendBufferSize = 4097,
				UdpMaxDatagramBytes = 1100,
				UdpReliableWindowSize = 24,
				UdpReorderCapacity = 20,
				UdpInitialRetransmitMilliseconds = 300,
				UdpMinimumRetransmitMilliseconds = 120,
				UdpMaximumRetransmitMilliseconds = 1800,
				UdpMaximumRetries = 8,
				UdpAcknowledgementDelayMilliseconds = 25,
				NoDelay = false
			};

			GameClientConfig mapped = viewModel.ToConfig();

			Assert.Equal("localhost", mapped.Server.Host);
			Assert.Equal(8888, mapped.Server.Port);
			Assert.Equal(TransportProtocol.Udp, mapped.Networking.Protocol);
			Assert.Equal(10, mapped.Networking.HeartbeatIntervalSeconds);
			Assert.False(mapped.Networking.ReconnectEnabled);
			Assert.Equal(4096, mapped.Networking.ReceiveBufferSize);
			Assert.Equal(4097, mapped.Networking.SendBufferSize);
			Assert.Equal(1100, mapped.Networking.UdpDelivery.MaxDatagramBytes);
			Assert.Equal(24, mapped.Networking.UdpDelivery.ReliableWindowSize);
			Assert.Equal(20, mapped.Networking.UdpDelivery.ReorderCapacity);
			Assert.Equal(300, mapped.Networking.UdpDelivery.InitialRetransmitMilliseconds);
			Assert.Equal(120, mapped.Networking.UdpDelivery.MinimumRetransmitMilliseconds);
			Assert.Equal(1800, mapped.Networking.UdpDelivery.MaximumRetransmitMilliseconds);
			Assert.Equal(8, mapped.Networking.UdpDelivery.MaximumRetries);
			Assert.Equal(25, mapped.Networking.UdpDelivery.AcknowledgementDelayMilliseconds);
			Assert.False(mapped.Networking.NoDelay);
		}

		[Fact]
		public void ConnectionSetup_LoadsUdpSettingsAndTracksProtocolVisibility()
		{
			var config = new GameClientConfig
			{
				Networking = new GameClientNetworkingSettings
				{
					Protocol = TransportProtocol.Udp,
					UdpDelivery = new UdpDeliverySettings
					{
						MaxDatagramBytes = 1050,
						ReliableWindowSize = 12,
						MaximumRetries = 9
					}
				}
			};

			var viewModel = new ConnectionSetupViewModel(config, new MockClientLauncher());

			Assert.True(viewModel.IsUdpSelected);
			Assert.Equal(1050, viewModel.UdpMaxDatagramBytes);
			Assert.Equal(12, viewModel.UdpReliableWindowSize);
			Assert.Equal(9, viewModel.UdpMaximumRetries);

			viewModel.Protocol = TransportProtocol.Tcp;
			Assert.False(viewModel.IsUdpSelected);
		}

		[Fact]
		public async Task ConnectionSetup_SearchAddsFoundServersWithoutChangingEndpointUntilSelected()
		{
			var discovery = new MockClientServerDiscovery();
			discovery.Results.Add(new FoundServer
			{
				Host = "192.168.1.20",
				Port = 8888,
				Name = "Test Server"
			});
			var viewModel = new ConnectionSetupViewModel(new GameClientConfig(), new MockClientLauncher(), discovery)
			{
				Host = "10.0.0.1",
				Port = 7777,
				ScanPort = 9999,
				Protocol = TransportProtocol.Udp
			};

			viewModel.SearchCommand.Execute(null);

			await WaitForAsync(() => viewModel.FoundServers.Count == 1);
			Assert.Equal(1, discovery.SearchRequestCount);
			Assert.Equal(9999, discovery.LastPort);
			Assert.Equal(TransportProtocol.Udp, discovery.LastProtocol);
			var found = Assert.Single(viewModel.FoundServers);
			Assert.Null(viewModel.SelectedServer);
			Assert.Equal("10.0.0.1", viewModel.Host);
			Assert.Equal(7777, viewModel.Port);

			viewModel.SelectedServer = found;

			Assert.Equal("192.168.1.20", viewModel.Host);
			Assert.Equal(8888, viewModel.Port);

			var second = new FoundServerViewModel(new FoundServer
			{
				Host = "10.0.0.4",
				Port = 7777,
				Name = "Other"
			});
			viewModel.SelectedServer = second;

			Assert.Equal("10.0.0.4", viewModel.Host);
			Assert.Equal(7777, viewModel.Port);
		}

		[Fact]
		public async Task ConnectionSetup_SearchClearsPreviousFoundServers()
		{
			var discovery = new MockClientServerDiscovery();
			discovery.Results.Add(new FoundServer
			{
				Host = "192.168.1.20",
				Port = 7777,
				Name = "First"
			});
			var viewModel = new ConnectionSetupViewModel(new GameClientConfig(), new MockClientLauncher(), discovery);
			viewModel.SearchCommand.Execute(null);
			await WaitForAsync(() => viewModel.FoundServers.Count == 1);

			discovery.Results.Clear();
			discovery.Results.Add(new FoundServer
			{
				Host = "192.168.1.30",
				Port = 7777,
				Name = "Second"
			});
			viewModel.SearchCommand.Execute(null);
			await WaitForAsync(() =>
				viewModel.FoundServers.Count == 1
				&& viewModel.FoundServers[0].Host == "192.168.1.30");

			Assert.DoesNotContain(viewModel.FoundServers, server => server.Host == "192.168.1.20");
			Assert.Null(viewModel.SelectedServer);
		}

		[Fact]
		public async Task ConnectionSetup_SearchButtonCancelsRunningSearch()
		{
			var discovery = new MockClientServerDiscovery();
			var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var searchCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			discovery.SearchOverride = async (_, token) =>
			{
				searchStarted.SetResult();
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(10), token);
				}
				catch (OperationCanceledException)
				{
					searchCanceled.SetResult();
					throw;
				}

				return Array.Empty<FoundServer>();
			};
			var viewModel = new ConnectionSetupViewModel(new GameClientConfig(), new MockClientLauncher(), discovery);

			viewModel.SearchCommand.Execute(null);
			await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
			Assert.True(viewModel.IsSearching);
			Assert.Equal("Stop", viewModel.SearchButtonText);

			viewModel.SearchCommand.Execute(null);

			await searchCanceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await WaitForAsync(() => !viewModel.IsSearching);
			Assert.Equal("Search", viewModel.SearchButtonText);
			Assert.Equal("Search stopped.", viewModel.StatusMessage);
		}

		[Fact]
		public async Task ClientMain_ConnectsAndNavigatesToServerProvidedLobbyState()
		{
			var launcher = new MockClientLauncher();
			var stateManager = new MockClientStateManager();
			launcher.ConnectOverride = (_, _) =>
			{
				stateManager.ChangeState("chat_lobby");
				return Task.CompletedTask;
			};
			var viewModel = new ClientMainViewModel(
				new GameClientConfig(),
				launcher,
				stateManager,
				() => new MockClientChatLobbyRuntime(),
				() => new MockClientMatchRuntime());

			viewModel.ConnectionSetup.ConnectCommand.Execute(null);

			await WaitForCurrentViewModelAsync<ClientChatLobbyViewModel>(viewModel);
			Assert.Equal(1, launcher.ConnectRequestCount);
			Assert.IsType<ClientChatLobbyViewModel>(viewModel.CurrentViewModel);
		}

		[Fact]
		public async Task ClientMain_StateChangeNavigatesFromLobbyToMatchAndBack()
		{
			var launcher = new MockClientLauncher();
			var stateManager = new MockClientStateManager();
			launcher.ConnectOverride = (_, _) =>
			{
				stateManager.ChangeState("chat_lobby");
				return Task.CompletedTask;
			};
			var viewModel = new ClientMainViewModel(
				new GameClientConfig(),
				launcher,
				stateManager,
				() => new MockClientChatLobbyRuntime(),
				() => new MockClientMatchRuntime());
			viewModel.ConnectionSetup.ConnectCommand.Execute(null);
			await WaitForCurrentViewModelAsync<ClientChatLobbyViewModel>(viewModel);

			stateManager.ChangeState("match");
			await WaitForCurrentViewModelAsync<ClientMatchViewModel>(viewModel);

			stateManager.ChangeState("chat_lobby");
			await WaitForCurrentViewModelAsync<ClientChatLobbyViewModel>(viewModel);
		}

		[Fact]
		public async Task ClientMain_UnknownInitialStateReturnsToSetupWithError()
		{
			var launcher = new MockClientLauncher();
			var stateManager = new MockClientStateManager();
			launcher.ConnectOverride = (_, _) =>
			{
				stateManager.ChangeState("unknown");
				return Task.CompletedTask;
			};
			var viewModel = new ClientMainViewModel(
				new GameClientConfig(),
				launcher,
				stateManager,
				() => new MockClientChatLobbyRuntime(),
				() => new MockClientMatchRuntime());

			viewModel.ConnectionSetup.ConnectCommand.Execute(null);

			await WaitForCurrentViewModelAsync<ConnectionSetupViewModel>(viewModel);
			Assert.True(viewModel.HasErrorPopup);
			Assert.NotNull(viewModel.ErrorPopup);
			Assert.Equal("Unknown Client State", viewModel.ErrorPopup.Title);
		}

		[Fact]
		public async Task ClientMain_UnexpectedDisconnectReturnsToSetupWithError()
		{
			var launcher = new MockClientLauncher();
			var stateManager = new MockClientStateManager();
			launcher.ConnectOverride = (_, _) =>
			{
				stateManager.ChangeState("chat_lobby");
				return Task.CompletedTask;
			};
			var viewModel = new ClientMainViewModel(
				new GameClientConfig(),
				launcher,
				stateManager,
				() => new MockClientChatLobbyRuntime(),
				() => new MockClientMatchRuntime());
			viewModel.ConnectionSetup.ConnectCommand.Execute(null);
			await WaitForCurrentViewModelAsync<ClientChatLobbyViewModel>(viewModel);

			stateManager.Disconnect("Lost connection.", isExpected: false);

			await WaitForCurrentViewModelAsync<ConnectionSetupViewModel>(viewModel);
			Assert.True(viewModel.HasErrorPopup);
			Assert.NotNull(viewModel.ErrorPopup);
			Assert.Equal("Client Disconnected", viewModel.ErrorPopup.Title);
			Assert.Equal("Lost connection.", viewModel.ErrorPopup.Message);
		}

		[Fact]
		public async Task ClientLobby_SendsChatNameAndReadyActions()
		{
			var runtime = new MockClientChatLobbyRuntime();
			var viewModel = new ClientChatLobbyViewModel(runtime)
			{
				PendingMessage = "hello",
				DisplayName = "Nova"
			};
			await viewModel.Initialization;

			viewModel.SendMessageCommand.Execute(null);
			viewModel.ChangeDisplayNameCommand.Execute(null);
			viewModel.ToggleReadyCommand.Execute(null);

			await WaitForAsync(() => runtime.SentMessages.Count == 1);
			await WaitForAsync(() => runtime.ChangedNames.Count == 1);
			await WaitForAsync(() => runtime.ReadyRequests.Count == 1);
			Assert.Equal("hello", runtime.SentMessages[0]);
			Assert.Equal("Nova", runtime.ChangedNames[0]);
			Assert.True(runtime.ReadyRequests[0]);
		}

		[Fact]
		public async Task ClientLobby_ShowsLocalPlayerIdAndMemberDisplayNamesOnly()
		{
			var runtime = new MockClientChatLobbyRuntime();
			var viewModel = new ClientChatLobbyViewModel(runtime);
			runtime.ApplySnapshot(new ClientChatLobbySnapshot
			{
				CurrentPlayer = new ClientPlayerData(12, "Nova"),
				Players =
				[
					new ChatLobbyPlayer { PlayerId = 12, DisplayName = "Nova", IsReady = false },
					new ChatLobbyPlayer { PlayerId = 14, DisplayName = "Echo", IsReady = true }
				]
			});

			await viewModel.Initialization;

			Assert.Equal("12", viewModel.PlayerIdText);
			Assert.Contains(viewModel.Members, member => member.DisplayNameOnly == "Echo");
			Assert.DoesNotContain(viewModel.Members, member => member.DisplayNameOnly.Contains("14", StringComparison.Ordinal));
		}

		[Fact]
		public async Task ClientMatch_MapsSnapshotAndSendsCommand()
		{
			var runtime = new MockClientMatchRuntime();
			var viewModel = new ClientMatchViewModel(runtime)
			{
				CommandId = "move",
				CommandPayload = "north"
			};
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				Payload = new MatchPayload
				{
					VariantId = "arena",
					PhaseId = "live",
					Players =
					[
						new MatchPlayer { PlayerId = 7, DisplayName = "Nova", IsConnected = true }
					]
				}
			});

			viewModel.SendCommandCommand.Execute(null);

			await WaitForAsync(() => runtime.SentCommands.Count == 1);
			Assert.Equal("arena", viewModel.VariantId);
			Assert.Equal("live", viewModel.PhaseId);
			Assert.Equal(1, viewModel.PlayerCount);
			Assert.Equal(("move", "north"), runtime.SentCommands[0]);
		}

		[Fact]
		public async Task ClientMatch_WorldSnapshotMapsTilesAndDisplayNames()
		{
			var runtime = new MockClientMatchRuntime();
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				CurrentPlayerId = 7,
				Payload = new MatchPayload
				{
					Players =
					[
						new MatchPlayer { PlayerId = 7, DisplayName = "Nova", IsConnected = true }
					]
				},
				World = CreateWorldSnapshot(entityPlayerId: 7)
			});
			var viewModel = new ClientMatchViewModel(runtime);

			await viewModel.Initialization;

			Assert.Equal("rect island", viewModel.World.RegionId);
			Assert.Equal(2, viewModel.World.Tiles.Count);
			WorldEntityViewModel entity = Assert.Single(viewModel.World.Entities);
			Assert.Equal("Nova", entity.DisplayName);
			Assert.True(entity.IsLocalPlayer);
		}

		[Fact]
		public async Task ClientMatch_MoveActionUpdatesPlayerToken()
		{
			var runtime = new MockClientMatchRuntime();
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				CurrentPlayerId = 7,
				Payload = new MatchPayload
				{
					Players =
					[
						new MatchPlayer { PlayerId = 7, DisplayName = "Nova", IsConnected = true }
					]
				},
				World = CreateWorldSnapshot(entityPlayerId: 7)
			});
			var viewModel = new ClientMatchViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplyEntity(new WorldEntitySnapshot
			{
				Id = "player_7_nav",
				EntityType = "NavigatorEntity",
				PlayerId = 7,
				WorldX = 3.5f,
				WorldZ = 2.5f,
				DestinationWorldX = 4.5f,
				DestinationWorldZ = 2.5f,
				VelocityX = 1f,
				VelocityZ = 0f,
				FaceForwardX = 1f,
				FaceForwardZ = 0f,
				BaseForwardX = 1f,
				BaseForwardZ = 0f,
				RotationVelocity = 0.25f,
				MaxSpeed = 2f,
				MaxRotationSpeed = 3f,
				CurrentPathIndex = 1,
				PathWorldPositions =
				[
					new WorldPoint(3.5f, 2.5f),
					new WorldPoint(4.5f, 2.5f)
				],
				ServerUpdatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
				MovementStatus = "Moving",
				CanMove = true
			});

			WorldEntityViewModel entity = Assert.Single(viewModel.World.Entities);
			Assert.Equal(3.5f, entity.WorldX);
			Assert.Equal(2.5f, entity.WorldZ);
			Assert.Equal(4.5f, entity.DestinationWorldX);
			Assert.Equal(2.5f, entity.DestinationWorldZ);
			Assert.Equal(1f, entity.VelocityX);
			Assert.Equal(0f, entity.VelocityZ);
			Assert.Equal(1f, entity.FaceForwardX);
			Assert.Equal(0f, entity.FaceForwardZ);
			Assert.Equal(1f, entity.BaseForwardX);
			Assert.Equal(0f, entity.BaseForwardZ);
			Assert.Equal(0.25f, entity.RotationVelocity);
			Assert.Equal(2f, entity.MaxSpeed);
			Assert.Equal(3f, entity.MaxRotationSpeed);
			Assert.Equal(1, entity.CurrentPathIndex);
			Assert.Equal(2, entity.PathWorldPositions.Count);
			Assert.Equal(new WorldPoint(4.5f, 2.5f), entity.PathWorldPositions[1]);
			Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), entity.ServerUpdatedAtUtc);
			Assert.Equal("Moving", entity.MovementStatus);
			Assert.Equal("Nova", entity.DisplayName);
		}

		[Fact]
		public void WorldUpdate_MovesRenderPositionBySpeedAndDeltaTime()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f));

			world.Update(0.5f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(1f, entity.RenderWorldX, precision: 3);
			Assert.Equal(0f, entity.RenderWorldZ, precision: 3);
		}

		[Fact]
		public void ClientMatch_WorldUpdateDeltaIsClampedToTwoRenderFrames()
		{
			var config = new WorldViewerConfig
			{
				TargetUpdateFramesPerSecond = 60
			};

			float deltaTime = ClientMatchViewModel.ClampWorldUpdateDelta(1f, config);

			Assert.Equal(1f / 30f, deltaTime, precision: 3);
		}

		[Fact]
		public void WorldUpdate_ClampsToWaypointWithoutOvershoot()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(0.5f, 0f)],
				maxSpeed: 2f));

			world.Update(1f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(0.5f, entity.RenderWorldX, precision: 3);
			Assert.Equal(0f, entity.RenderWorldZ, precision: 3);
		}

		[Fact]
		public void WorldUpdate_DoesNotMoveIdleStatus()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f,
				status: "Idle"));

			world.Update(1f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(0f, entity.RenderWorldX);
			Assert.Equal(0f, entity.RenderWorldZ);
		}

		[Fact]
		public void WorldUpdate_ContinuingWaypointContinuesTowardNextPathNode()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 0f,
				path:
				[
					new WorldPoint(0f, 0f),
					new WorldPoint(1f, 0f),
					new WorldPoint(3f, 0f)
				],
				maxSpeed: 2f,
				status: "Continuing",
				currentPathIndex: 1));

			world.Update(0.5f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(2f, entity.RenderWorldX, precision: 3);
			Assert.Equal(0f, entity.RenderWorldZ, precision: 3);
		}

		[Fact]
		public void WorldUpdate_ContinuingAtLastPathIndexMovesTowardFinalNode()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 0f,
				path:
				[
					new WorldPoint(0f, 0f),
					new WorldPoint(1f, 0f),
					new WorldPoint(3f, 0f)
				],
				maxSpeed: 2f,
				status: "Continuing",
				currentPathIndex: 2));

			world.Update(0.5f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(2f, entity.RenderWorldX, precision: 3);
			Assert.Equal(0f, entity.RenderWorldZ, precision: 3);
		}

		[Fact]
		public void WorldApplyEntity_MediumCorrectionBendsWhileMoving()
		{
			var world = new WorldViewModel(new WorldViewerConfig
			{
				Reconciliation = new WorldReconciliationConfig
				{
					IgnoreErrorDistance = 0.1f,
					BlendErrorDistance = 1f,
					BlendMilliseconds = 100
				}
			});
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 1f));
			world.Update(0.25f);

			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0.25f,
				worldZ: 0.5f,
				path: [new WorldPoint(2f, 0.5f)],
				maxSpeed: 1f));
			world.Update(0.05f);

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.True(entity.RenderWorldZ > 0f);
			Assert.True(entity.RenderWorldX > 0.25f);
			Assert.True(entity.IsBlending);
			Assert.Equal(WorldCorrectionMode.Blend, entity.LastCorrectionMode);
		}

		[Fact]
		public void WorldApplyEntity_LargeCorrectionSnapsToServer()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 1f));
			world.Update(0.25f);

			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 5f,
				worldZ: 5f,
				path: [new WorldPoint(6f, 5f)],
				maxSpeed: 1f));

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(5f, entity.RenderWorldX);
			Assert.Equal(5f, entity.RenderWorldZ);
			Assert.Equal(WorldCorrectionMode.Snap, entity.LastCorrectionMode);
		}

		[Fact]
		public void WorldApplyEntity_TerminalStatusSnapsToServer()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 1f));
			world.Update(0.25f);

			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 1f,
				status: "Completed"));

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(1f, entity.RenderWorldX);
			Assert.Equal(0f, entity.RenderWorldZ);
			Assert.Equal(WorldCorrectionMode.Snap, entity.LastCorrectionMode);
		}

		[Fact]
		public void WorldApplyEntity_SmallCorrectionIsIgnored()
		{
			var world = new WorldViewModel(new WorldViewerConfig
			{
				Reconciliation = new WorldReconciliationConfig
				{
					IgnoreErrorDistance = 0.5f,
					BlendErrorDistance = 1f,
					BlendMilliseconds = 100
				}
			});
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 1f));
			world.Update(0.25f);

			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0.25f,
				worldZ: 0.2f,
				path: [new WorldPoint(2f, 0.2f)],
				maxSpeed: 1f));

			WorldEntityViewModel entity = Assert.Single(world.Entities);
			Assert.Equal(WorldCorrectionMode.Ignore, entity.LastCorrectionMode);
		}

		[Fact]
		public void WorldMovementPaths_LocalScopeShowsOnlyLocalMovingEntity()
		{
			var world = new WorldViewModel
			{
				ShowMovementPaths = true,
				MovementPathScope = WorldMovementPathScope.LocalPlayer
			};
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f,
				id: "player_7_nav",
				playerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 1f,
				path: [new WorldPoint(3f, 1f)],
				maxSpeed: 2f,
				id: "player_8_nav",
				playerId: 8));

			WorldMovementPathOverlay overlay = Assert.Single(world.VisibleMovementPathOverlays);

			Assert.Equal("player_7_nav", overlay.Entity.Id);
			Assert.Equal(new WorldPoint(0f, 0f), overlay.Points[0]);
			Assert.Equal(new WorldPoint(2f, 0f), overlay.CurrentTarget);
			Assert.Equal(new WorldPoint(2f, 0f), overlay.Destination);
		}

		[Fact]
		public void WorldMovementPaths_AllScopeShowsAllMovingEntities()
		{
			var world = new WorldViewModel
			{
				ShowMovementPaths = true,
				MovementPathScope = WorldMovementPathScope.AllMovingEntities
			};
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f,
				id: "player_7_nav",
				playerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 1f,
				path: [new WorldPoint(3f, 1f)],
				maxSpeed: 2f,
				id: "player_8_nav",
				playerId: 8));

			Assert.Equal(["player_7_nav", "player_8_nav"], world.VisibleMovementPathOverlays.Select(overlay => overlay.Entity.Id));
		}

		[Theory]
		[InlineData("Idle")]
		[InlineData("Completed")]
		[InlineData("Canceled")]
		public void WorldMovementPaths_TerminalOrIdleStatusesAreHidden(string movementStatus)
		{
			var world = new WorldViewModel
			{
				ShowMovementPaths = true,
				MovementPathScope = WorldMovementPathScope.AllMovingEntities
			};
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f,
				status: movementStatus));

			Assert.Empty(world.VisibleMovementPathOverlays);
		}

		[Fact]
		public void WorldMovementPaths_ToggleRaisesPropertyChangeAndControlsVisibility()
		{
			var world = new WorldViewModel();
			var changedProperties = new List<string?>();
			world.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f));

			Assert.Empty(world.VisibleMovementPathOverlays);

			world.ShowMovementPaths = true;

			Assert.Single(world.VisibleMovementPathOverlays);
			Assert.Contains(nameof(WorldViewModel.ShowMovementPaths), changedProperties);
			Assert.Contains(nameof(WorldViewModel.VisibleMovementPathOverlays), changedProperties);
		}

		[Fact]
		public void WorldMovementDebugEntities_RespectIndependentTogglesAndScope()
		{
			var world = new WorldViewModel
			{
				MovementPathScope = WorldMovementPathScope.LocalPlayer
			};
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [new WorldPoint(2f, 0f)],
				maxSpeed: 2f,
				id: "player_7_nav",
				playerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 1f,
				path: [new WorldPoint(3f, 1f)],
				maxSpeed: 2f,
				id: "player_8_nav",
				playerId: 8));

			Assert.Empty(world.VisibleMovementDebugEntities);

			world.ShowServerPositionMarkers = true;
			Assert.Equal(["player_7_nav"], world.VisibleMovementDebugEntities.Select(entity => entity.Id));

			world.MovementPathScope = WorldMovementPathScope.AllMovingEntities;
			Assert.Equal(["player_7_nav", "player_8_nav"], world.VisibleMovementDebugEntities.Select(entity => entity.Id));
		}

		[Fact]
		public void WorldMovementDebugEntities_ToggleRaisesPropertyChange()
		{
			var world = new WorldViewModel();
			var changedProperties = new List<string?>();
			world.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

			world.ShowCorrectionLabels = true;

			Assert.Contains(nameof(WorldViewModel.ShowCorrectionLabels), changedProperties);
			Assert.Contains(nameof(WorldViewModel.VisibleMovementDebugEntities), changedProperties);
		}

		[Fact]
		public void WorldMovementPaths_EmptyPathAndOutOfRangeIndexAreSafe()
		{
			var world = new WorldViewModel
			{
				ShowMovementPaths = true,
				MovementPathScope = WorldMovementPathScope.AllMovingEntities
			};
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 0f,
				worldZ: 0f,
				path: [],
				maxSpeed: 2f,
				id: "empty_path",
				playerId: 7));
			world.ApplyEntity(CreateMovingEntitySnapshot(
				worldX: 1f,
				worldZ: 1f,
				path: [new WorldPoint(2f, 1f)],
				maxSpeed: 2f,
				currentPathIndex: 99,
				id: "out_of_range",
				playerId: 8));

			WorldMovementPathOverlay overlay = Assert.Single(world.VisibleMovementPathOverlays);

			Assert.Equal("out_of_range", overlay.Entity.Id);
			Assert.Equal(new WorldPoint(2f, 1f), overlay.CurrentTarget);
			Assert.Equal(new WorldPoint(2f, 1f), overlay.Destination);
		}

		[Fact]
		public async Task ClientMatch_PassableTileClickRaisesMoveRequest()
		{
			var runtime = new MockClientMatchRuntime();
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				World = CreateWorldSnapshot(entityPlayerId: null)
			});
			var viewModel = new ClientMatchViewModel(runtime);
			await viewModel.Initialization;

			viewModel.World.ClickTile(viewModel.World.Tiles.First(tile => !tile.IsBlocked));
			await WaitForAsync(() => runtime.MoveRequests.Count == 1);

			Assert.Equal(new WorldPoint(1.5f, 1.5f), runtime.MoveRequests[0]);
		}

		[Fact]
		public async Task ClientMatch_BlockedTileClickDoesNotRaiseMoveRequest()
		{
			var runtime = new MockClientMatchRuntime();
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				World = CreateWorldSnapshot(entityPlayerId: null)
			});
			var viewModel = new ClientMatchViewModel(runtime);
			await viewModel.Initialization;

			WorldTileViewModel blocked = viewModel.World.Tiles.First(tile => tile.IsBlocked);
			viewModel.World.ClickTile(blocked);
			await Task.Delay(50);

			Assert.Empty(runtime.MoveRequests);
			Assert.True(blocked.IsSelected);
			Assert.Contains("blocked", viewModel.World.StatusText, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void WorldApplySnapshot_PreservesHoveredTileAndEntityById()
		{
			var world = new WorldViewModel();
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));
			WorldTileViewModel hoveredTile = world.Tiles.First(tile => !tile.IsBlocked);
			WorldEntityViewModel hoveredEntity = Assert.Single(world.Entities);

			world.HoverTile(hoveredTile);
			world.HoverEntity(hoveredEntity);
			world.ApplySnapshot(CreateWorldSnapshot(entityPlayerId: 7));

			Assert.NotNull(world.HoveredTile);
			Assert.NotNull(world.HoveredEntity);
			Assert.Equal(hoveredTile.Id, world.HoveredTile!.Id);
			Assert.Equal(hoveredEntity.Id, world.HoveredEntity!.Id);
			Assert.True(world.HoveredTile.IsHovered);
			Assert.True(world.HoveredEntity.IsHovered);
		}

		[Fact]
		public async Task ClientMatch_RequestsWorldStateWhenStaticSnapshotHasNoEntities()
		{
			var runtime = new MockClientMatchRuntime();
			runtime.ApplySnapshot(new ClientMatchSnapshot
			{
				World = CreateWorldSnapshot(entityPlayerId: null)
			});

			var viewModel = new ClientMatchViewModel(runtime);
			await viewModel.Initialization;

			Assert.Contains("rect island", runtime.RequestedRegions);
		}

		[Fact]
		public async Task ClientMatch_PhaseChangeRefreshesPlayersAndWorldState()
		{
			var runtime = new MockClientMatchRuntime();
			var viewModel = new ClientMatchViewModel(runtime);
			await viewModel.Initialization;

			runtime.ApplyPhase(new MatchPhaseChangeResponse
			{
				VariantId = "clan_iron",
				PhaseId = "match",
				PhaseEndsAtUtc = DateTime.UtcNow.AddMinutes(5),
				Players =
				[
					new MatchPlayer { PlayerId = 7, DisplayName = "Nova", IsConnected = true }
				]
			});
			await WaitForAsync(() => runtime.RequestedRegions.Count > 0);

			Assert.Equal("1 / 1", viewModel.PlayerCountText);
			Assert.Contains("rect island", runtime.RequestedRegions);
		}

		[Fact]
		public async Task ClientConfigLoader_SavesAndLoadsJson()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-client-config-{Guid.NewGuid():N}.json");
			var config = new GameClientConfig
			{
				Server = new GameClientServerSettings
				{
					Host = "localhost",
					Port = 9999
				}
			};

			try
			{
				await ClientConfigLoader.SaveAsync(config, path);
				GameClientConfig loaded = ClientConfigLoader.LoadOrDefault(path);

				Assert.Equal("localhost", loaded.Server.Host);
				Assert.Equal(9999, loaded.Server.Port);
			}
			finally
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void WorldViewerConfigLoader_LoadsReconciliationRules()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-world-viewer-{Guid.NewGuid():N}.json");
			try
			{
				File.WriteAllText(path, """
				{
				  "TargetUpdateFramesPerSecond": 120,
				  "Reconciliation": {
				    "Enabled": true,
				    "IgnoreErrorDistance": 0.25,
				    "BlendErrorDistance": 1.25,
				    "BlendMilliseconds": 225,
				    "SnapOnFailedCanceledOrCompleted": false
				  }
				}
				""");

				WorldViewerConfig loaded = WorldViewerConfigLoader.LoadOrDefault(path);

				Assert.Equal(120, loaded.TargetUpdateFramesPerSecond);
				Assert.True(loaded.Reconciliation.Enabled);
				Assert.Equal(0.25f, loaded.Reconciliation.IgnoreErrorDistance);
				Assert.Equal(1.25f, loaded.Reconciliation.BlendErrorDistance);
				Assert.Equal(225, loaded.Reconciliation.BlendMilliseconds);
				Assert.False(loaded.Reconciliation.SnapOnFailedCanceledOrCompleted);
			}
			finally
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void WorldViewerConfigLoader_NormalizesInvalidFrameRate()
		{
			string path = Path.Combine(Path.GetTempPath(), $"hawknet-world-viewer-{Guid.NewGuid():N}.json");
			try
			{
				File.WriteAllText(path, """
				{
				  "TargetUpdateFramesPerSecond": 999,
				  "Reconciliation": {
				    "IgnoreErrorDistance": -1,
				    "BlendErrorDistance": -1,
				    "BlendMilliseconds": 0
				  }
				}
				""");

				WorldViewerConfig loaded = WorldViewerConfigLoader.LoadOrDefault(path);

				Assert.Equal(240, loaded.TargetUpdateFramesPerSecond);
				Assert.Equal(0.15f, loaded.Reconciliation.IgnoreErrorDistance);
				Assert.Equal(0.75f, loaded.Reconciliation.BlendErrorDistance);
				Assert.Equal(150, loaded.Reconciliation.BlendMilliseconds);
			}
			finally
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
		}

		[Fact]
		public void ClientMain_StartAtMainMenu_FindLobbyNavigatesToConnectionSetup()
		{
			var viewModel = new ClientMainViewModel(
				new GameClientConfig(),
				new MockClientLauncher(),
				new MockClientStateManager(),
				() => new MockClientChatLobbyRuntime(),
				() => new MockClientMatchRuntime(),
				startAtMainMenu: true);
			Assert.IsType<MainMenuViewModel>(viewModel.CurrentViewModel);

			viewModel.MainMenu.FindLobbyCommand.Execute(null);

			Assert.Same(viewModel.ConnectionSetup, viewModel.CurrentViewModel);
		}

		private static async Task WaitForCurrentViewModelAsync<T>(ClientMainViewModel viewModel)
		{
			await WaitForAsync(() => viewModel.CurrentViewModel is T);
		}

		private static async Task WaitForAsync(Func<bool> condition)
		{
			var timeoutAt = DateTime.UtcNow.AddSeconds(3);
			while (DateTime.UtcNow < timeoutAt)
			{
				if (condition())
				{
					return;
				}

				await Task.Delay(25);
			}

			Assert.Fail("Timed out waiting for condition.");
		}

		private static WorldSnapshot CreateWorldSnapshot(int? entityPlayerId)
		{
			return new WorldSnapshot
			{
				RegionId = "rect island",
				RegionName = "Rect Island",
				LocalPlayerId = 7,
				Tiles =
				[
					new WorldTileSnapshot
					{
						Id = "rect_island_0_0:1:1",
						RegionId = "rect island",
						PlacedSectionId = "rect_island_0_0",
						GridX = 1,
						GridZ = 1,
						WorldX = 1,
						WorldZ = 1,
						SizeX = 1,
						SizeZ = 1,
						CenterX = 1.5f,
						CenterZ = 1.5f,
						MovementCost = 1
					},
					new WorldTileSnapshot
					{
						Id = "rect_island_0_0:0:0",
						RegionId = "rect island",
						PlacedSectionId = "rect_island_0_0",
						GridX = 0,
						GridZ = 0,
						WorldX = 0,
						WorldZ = 0,
						SizeX = 1,
						SizeZ = 1,
						CenterX = 0.5f,
						CenterZ = 0.5f,
						MovementCost = -1,
						IsBlocked = true
					}
				],
				Entities = entityPlayerId is null
					? Array.Empty<WorldEntitySnapshot>()
					:
					[
						new WorldEntitySnapshot
						{
							Id = $"player_{entityPlayerId}_nav",
							EntityType = "NavigatorEntity",
							PlayerId = entityPlayerId,
							WorldX = 2.5f,
							WorldZ = 2.5f,
							MovementStatus = "Idle",
							CanMove = true
						}
					]
			};
		}

		private static WorldEntitySnapshot CreateMovingEntitySnapshot(
			float worldX,
			float worldZ,
			IReadOnlyList<WorldPoint> path,
			float maxSpeed,
			string status = "Moving",
			int currentPathIndex = 0,
			string id = "player_7_nav",
			int playerId = 7)
		{
			WorldPoint destination = path.Count == 0 ? new WorldPoint(worldX, worldZ) : path[^1];
			return new WorldEntitySnapshot
			{
				Id = id,
				EntityType = "NavigatorEntity",
				PlayerId = playerId,
				WorldX = worldX,
				WorldZ = worldZ,
				DestinationWorldX = destination.X,
				DestinationWorldZ = destination.Z,
				MaxSpeed = maxSpeed,
				MaxRotationSpeed = 1f,
				CurrentPathIndex = currentPathIndex,
				PathWorldPositions = path,
				MovementStatus = status,
				CanMove = true
			};
		}
	}
}
