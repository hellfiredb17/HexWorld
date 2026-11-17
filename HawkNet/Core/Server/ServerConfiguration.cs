using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using HawkNet.Core.Shared;

namespace HawkNet.Core.Server;

public sealed record ServerConfigValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
	public bool IsValid => Errors.Count == 0;

	public void ThrowIfInvalid()
	{
		if (!IsValid)
		{
			throw new InvalidOperationException("Invalid server configuration: " + string.Join(" ", Errors));
		}
	}
}

public static class ServerConfiguration
{
	private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

	public static GameHostConfig Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Configuration path cannot be empty.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"Server configuration file not found: {path}", path);
		}

		try
		{
			var config = JsonSerializer.Deserialize<GameHostConfig>(File.ReadAllText(path), JsonOptions)
				?? throw new InvalidOperationException("The configuration file contained no server configuration.");
			Validate(config).ThrowIfInvalid();
			return config;
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException($"Server configuration '{path}' is invalid JSON: {exception.Message}", exception);
		}
	}

	public static ServerConfigValidationResult Validate(GameHostConfig config)
	{
		if (config is null) throw new ArgumentNullException(nameof(config));
		var errors = new List<string>();
		var warnings = new List<string>();

		if (!TryResolveBindAddress(config.Server.Host, out _))
			errors.Add($"Server.Host '{config.Server.Host}' must be an IP address or 'localhost'.");
		if (config.Server.Port is < 1 or > 65535)
			errors.Add("Server.Port must be between 1 and 65535.");
		if (config.Server.MaxPlayers <= 0)
			errors.Add("Server.MaxPlayers must be greater than zero.");
		if (config.Server.MaxConcurrentConnections <= 0)
			errors.Add("Server.MaxConcurrentConnections must be greater than zero.");
		if (config.Server.MaxConcurrentConnections < config.Server.MaxPlayers)
			warnings.Add("MaxConcurrentConnections is lower than MaxPlayers; reconnects may be unable to enter.");
		if (config.Server.SessionTimeoutSeconds <= 0 || config.Server.HeartbeatIntervalSeconds <= 0)
			errors.Add("Session and heartbeat intervals must be greater than zero.");
		if (config.Server.HeartbeatTimeoutSeconds < 0)
			errors.Add("HeartbeatTimeoutSeconds cannot be negative.");
		if (config.Server.UseTls)
			errors.Add("TLS is not implemented by the built-in TCP transport.");
		if (config.Networking.Protocol is not TransportProtocol.Tcp and not TransportProtocol.Udp)
			errors.Add($"Transport '{config.Networking.Protocol}' is not supported. Select Tcp or Udp.");
		if (config.Networking.ReceiveBufferSize <= 0 || config.Networking.SendBufferSize <= 0)
			errors.Add("Network buffer sizes must be greater than zero.");
		if (config.Networking.OutboundQueueCapacity <= 0)
			errors.Add("OutboundQueueCapacity must be greater than zero.");
		if (config.Networking.UdpReceiveQueueCapacity <= 0)
			errors.Add("UdpReceiveQueueCapacity must be greater than zero.");
		ValidateUdpDelivery(config.Networking.UdpDelivery, errors);
		if (config.Simulation.TickRateHz <= 0 || config.Simulation.MaxCatchUpTicks <= 0)
			errors.Add("Simulation tick rate and catch-up count must be greater than zero.");
		if (config.Simulation.InboundQueueCapacity <= 0 || config.Simulation.MaxCommandsPerTick <= 0)
			errors.Add("Simulation inbox capacity and commands-per-tick must be greater than zero.");
		if (config.Security.HandshakeTimeoutSeconds <= 0)
			errors.Add("HandshakeTimeoutSeconds must be greater than zero.");
		if (config.Security.MaxCommandPayloadBytes <= 0 || config.Security.MaxCommandPayloadBytes > ushort.MaxValue)
			errors.Add("MaxCommandPayloadBytes must be between 1 and 65535.");
		if (config.Security.MaxGameStateMessageBytes <= 0 || config.Security.MaxGameStateMessageBytes > ushort.MaxValue)
			errors.Add("MaxGameStateMessageBytes must be between 1 and 65535.");
		if (config.Networking.Protocol == TransportProtocol.Udp
			&& (config.Security.MaxCommandPayloadBytes > SignalShared.MaxUdpPayloadLength
				|| config.Security.MaxGameStateMessageBytes > SignalShared.MaxUdpPayloadLength
				|| config.Chat.MaxMessageBytes > SignalShared.MaxUdpPayloadLength))
			warnings.Add($"Configured application payload limits exceed UDP's effective maximum of {SignalShared.MaxUdpPayloadLength} bytes; oversized UDP sends will be rejected.");
		if (config.Security.MaxInvalidPacketsPerConnection <= 0 || config.Security.MaxPacketsPerWindow <= 0 || config.Security.PacketRateWindowSeconds <= 0)
			errors.Add("Security packet limits must be greater than zero.");

		foreach (string value in config.Server.AllowedIPs ?? Array.Empty<string>())
		{
			if (!IpNetwork.TryParse(value, out _))
				errors.Add($"Allowed IP entry '{value}' must be an IP address or CIDR range.");
		}

		if (TryResolveBindAddress(config.Server.Host, out IPAddress? bind)
			&& (IPAddress.Any.Equals(bind) || IPAddress.IPv6Any.Equals(bind))
			&& (config.Server.AllowedIPs?.Length ?? 0) > 0
			&& config.Server.AllowedIPs!.All(IsLoopbackEntry))
		{
			warnings.Add("The server listens on all interfaces but the allowlist permits only loopback clients.");
		}

		return new ServerConfigValidationResult(errors, warnings);
	}

	public static IReadOnlyList<string> ValidateUdpDelivery(UdpDeliverySettings settings, List<string>? errors = null)
	{
		errors ??= new List<string>();
		errors.AddRange(UdpDeliveryConfiguration.Validate(settings));
		return errors;
	}

	public static bool TryResolveBindAddress(string? value, out IPAddress? address)
	{
		if (string.Equals(value?.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
		{
			address = IPAddress.Loopback;
			return true;
		}
		return IPAddress.TryParse(value?.Trim(), out address);
	}

	private static bool IsLoopbackEntry(string value)
	{
		if (!IpNetwork.TryParse(value, out IpNetwork network)) return false;
		IPAddress address = network.Address.IsIPv4MappedToIPv6 ? network.Address.MapToIPv4() : network.Address;
		byte[] bytes = address.GetAddressBytes();
		return address.AddressFamily == AddressFamily.InterNetwork
			? bytes[0] == 127 && network.PrefixLength >= 8
			: IPAddress.IPv6Loopback.Equals(address) && network.PrefixLength == 128;
	}

	private static JsonSerializerOptions CreateJsonOptions()
	{
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true
		};
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}
}

public readonly record struct IpNetwork(IPAddress Address, int PrefixLength)
{
	public static bool TryParse(string? value, out IpNetwork network)
	{
		network = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		string[] parts = value.Trim().Split('/');
		if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out IPAddress? address)) return false;
		int max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
		int prefix = max;
		if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > max)) return false;
		network = new IpNetwork(address, prefix);
		return true;
	}

	public bool Contains(IPAddress candidate)
	{
		if (candidate.IsIPv4MappedToIPv6) candidate = candidate.MapToIPv4();
		IPAddress networkAddress = Address.IsIPv4MappedToIPv6 ? Address.MapToIPv4() : Address;
		if (candidate.AddressFamily != networkAddress.AddressFamily) return false;
		byte[] left = networkAddress.GetAddressBytes();
		byte[] right = candidate.GetAddressBytes();
		int wholeBytes = PrefixLength / 8;
		int remainder = PrefixLength % 8;
		for (int i = 0; i < wholeBytes; i++) if (left[i] != right[i]) return false;
		if (remainder == 0) return true;
		int mask = 0xFF << (8 - remainder);
		return (left[wholeBytes] & mask) == (right[wholeBytes] & mask);
	}
}
