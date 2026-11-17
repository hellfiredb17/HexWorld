using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HawkNet.CoreUx.Client.Services;
using HawkNet.Core.Server;
using HawkNet.Core.Shared;

namespace HawkNet.Desktop.Services;

public sealed class DesktopClientServerDiscovery : IClientServerDiscovery
{
    private const int DefaultHawkNetPort = 7777;
    private readonly TimeSpan _timeout;
    private readonly int? _defaultPort;
    private readonly int _maxConcurrentProbes;

    public DesktopClientServerDiscovery(
        TimeSpan? timeout = null,
        int? port = DefaultHawkNetPort,
        int maxConcurrentProbes = 64)
    {
        _timeout = timeout ?? TimeSpan.FromMilliseconds(750);
        _defaultPort = port;
        _maxConcurrentProbes = Math.Max(1, maxConcurrentProbes);
    }

    public async Task<IReadOnlyList<FoundServer>> SearchAsync(int port, CancellationToken token = default)
	{
		return await SearchAsync(port, TransportProtocol.Tcp, token);
	}

	public async Task<IReadOnlyList<FoundServer>> SearchAsync(
		int port,
		TransportProtocol protocol,
		CancellationToken token = default)
    {
        int scanPort = port > 0 ? port : _defaultPort ?? DefaultHawkNetPort;
        if (scanPort < 1 || scanPort > 65535)
        {
            throw new InvalidOperationException("Discovery port must be between 1 and 65535.");
        }

        var found = new ConcurrentBag<FoundServer>();
        using var semaphore = new SemaphoreSlim(_maxConcurrentProbes);
        IEnumerable<IPAddress> candidates = GetCandidateAddresses();
        Task[] probes = candidates.Select(async address =>
        {
            await semaphore.WaitAsync(token);
            try
            {
				if (await TryConnectAsync(address, scanPort, protocol, token))
                {
                    found.Add(new FoundServer
                    {
                        Host = address.ToString(),
                        Port = scanPort,
                        Name = "HawkNet Server"
                    });
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(probes);
        return found
            .OrderBy(server => server.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Port)
            .ToArray();
    }

	private Task<bool> TryConnectAsync(IPAddress address, int port, TransportProtocol protocol, CancellationToken token)
	{
		return protocol == TransportProtocol.Udp
			? TryDiscoverUdpAsync(address, port, token)
			: TryConnectTcpAsync(address.ToString(), port, token);
	}

	private async Task<bool> TryConnectTcpAsync(string host, int port, CancellationToken token)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_timeout);
        try
        {
            await client.ConnectAsync(host.Trim(), port, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }

        return true;
    }

	private async Task<bool> TryDiscoverUdpAsync(IPAddress address, int port, CancellationToken token)
	{
		using var client = new UdpClient(address.AddressFamily);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
		timeout.CancelAfter(_timeout);
		try
		{
			byte[] request = SignalShared.BuildHeaderOnlyPacket(PacketType.DiscoveryRequest);
			await client.SendAsync(request.AsMemory(), new IPEndPoint(address, port), timeout.Token);
			UdpReceiveResult response = await client.ReceiveAsync(timeout.Token);
			ReadNetworkPacketResult packet = SignalShared.ReadNetworkDatagram(response.Buffer);
			return packet.Success && packet.PacketType == PacketType.DiscoveryResponse;
		}
		catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
		catch (SocketException) { return false; }
	}

    private static IEnumerable<IPAddress> GetCandidateAddresses()
    {
        var candidates = new HashSet<IPAddress>();
        candidates.Add(IPAddress.Loopback);

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address.Address))
                {
                    continue;
                }

                foreach (IPAddress candidate in ExpandIpv4Subnet(address))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<IPAddress> ExpandIpv4Subnet(UnicastIPAddressInformation address)
    {
        byte[] ipBytes = address.Address.GetAddressBytes();
        byte[] maskBytes = address.IPv4Mask?.GetAddressBytes() ?? [255, 255, 255, 0];
        if (ipBytes.Length != 4 || maskBytes.Length != 4)
        {
            yield break;
        }

        uint ip = ToUInt32(ipBytes);
        uint mask = ToUInt32(maskBytes);
        uint network = ip & mask;
        uint broadcast = network | ~mask;
        uint first = network + 1;
        uint last = broadcast - 1;
        if (last < first || last - first > 1024)
        {
            first = ip & 0xFFFFFF00;
            last = first + 255;
        }

        for (uint current = first; current <= last; current++)
        {
            yield return FromUInt32(current);
        }
    }

    private static uint ToUInt32(byte[] bytes)
    {
        return ((uint)bytes[0] << 24)
            | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8)
            | bytes[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress(
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        ]);
    }
}
