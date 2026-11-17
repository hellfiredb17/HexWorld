/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Client
{
	public record GameClientConfig
	{
		public GameClientServerSettings Server { get; init; } = new();
		public GameClientNetworkingSettings Networking { get; init; } = new();

		public static GameClientConfig Load(string path)
		{
			if(string.IsNullOrWhiteSpace(path))
			{
				throw new ArgumentException("Config path cannot be empty.", nameof(path));
			}

			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			options.Converters.Add(new JsonStringEnumConverter());
			string json = File.ReadAllText(path);
			return JsonSerializer.Deserialize<GameClientConfig>(json, options) ?? new GameClientConfig();
		}
	}

	public record GameClientServerSettings
	{
		public string Host { get; init; } = "127.0.0.1";
		public int Port { get; init; } = 7777;
	}

	public record GameClientNetworkingSettings
	{
		public TransportProtocol Protocol { get; init; } = TransportProtocol.Tcp;
		public int HeartbeatIntervalSeconds { get; init; } = 15;
		public bool ReconnectEnabled { get; init; } = true;
		public int ReceiveBufferSize { get; init; } = 8192;
		public int SendBufferSize { get; init; } = 8192;
		public bool NoDelay { get; init; } = true;
		public UdpDeliverySettings UdpDelivery { get; init; } = new();
	}
}
