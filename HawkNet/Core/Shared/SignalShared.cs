/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core.Shared
{
	/// <summary>
	/// Specifies the type of network packet used in communication between clients and servers.
	/// </summary>
	/// <remarks>Use this enumeration to identify the purpose of a packet when processing or constructing network
	/// messages. The values correspond to different categories of packets, such as chat messages, player input, heartbeat
	/// signals, and reconnection requests. The <see cref="PacketType.Unknown"/> value indicates an unrecognized or
	/// unspecified packet type.</remarks>
	public enum PacketType : byte
	{
		Unknown = 0,
		ChatMessage = 1,
		PlayerInput = 2,
		HeartBeat = 3,
		ReconnectRequest = 4,
		DisplayNameUpdate = 5,
		HandshakeRequest = 6,
		HandshakeResponse = 7,
		GameStateChange = 8,
		ChatLobbyReadyRequest = 9,
		ChatLobbyReadyState = 10,
		ChatLobbyCountdown = 11,
		ChatLobbyPlayerState = 12,
		MatchPhaseChange = 13,
		MatchCommandRequest = 14,
		GameStateMessage = 15,
		DiscoveryRequest = 16,
		DiscoveryResponse = 17,
	}

	/// <summary>
	/// Represents the result of a network packet read operation, including the outcome, packet type, UTC send timestamp,
	/// and payload data.
	/// </summary>
	/// <remarks>Use this structure to determine whether a network packet was successfully read, identify the type
	/// of packet received, and access its associated payload. The values in this structure are typically populated by
	/// network reading methods and should be checked to ensure successful operation before processing the
	/// payload.</remarks>
	public struct ReadNetworkPacketResult
	{
		public bool Success;
		public PacketType PacketType;
		public long SentAtUnixMilliseconds;
		public byte[] Payload;
		public string ErrorMsg;
	}

	/// <summary>
	/// Provides shared functionality and constants for signal-related operations.
	/// </summary>
	/// <remarks>This static class serves as a central location for members that are common to signal processing or
	/// signaling features. It cannot be instantiated.</remarks>
	public static class SignalShared
	{
		//---- Constants
		//--------------
		public const int LengthPrefixSize = 2;
		public const int PacketTypeSize = 1;
		public const int TimestampSize = sizeof(long);
		public const int HeaderSize = PacketTypeSize + TimestampSize;
		public const int MinPacketLength = HeaderSize;
		public const int MaxPacketLength = ushort.MaxValue;
		public const int MaxPayloadLength = MaxPacketLength - HeaderSize;
		public const int MaxUdpDatagramLength = UdpEnvelopeCodec.DefaultMaxDatagramBytes;
		public const int MaxUdpPayloadLength = UdpEnvelopeCodec.MaxApplicationPayloadBytes;

		/// <summary>
		/// Reads the specified number of bytes from the provided network stream into the buffer, starting at the given
		/// offset.
		/// </summary>
		/// <remarks>This method attempts to read exactly the specified number of bytes unless the end of the stream
		/// is reached. If fewer than the requested number of bytes are available, the method returns 0. The operation is
		/// asynchronous and may complete before all bytes are read if the stream ends early.</remarks>
		/// <param name="stream">The network stream from which bytes are read. Must be readable and open.</param>
		/// <param name="buffer">The byte array that receives the data read from the stream.</param>
		/// <param name="offset">The zero-based index in the buffer at which to begin storing the data read from the stream. Must be non-negative
		/// and less than the length of the buffer.</param>
		/// <param name="count">The exact number of bytes to read from the stream. Must be positive and the sum of offset and count must not
		/// exceed the length of the buffer.</param>
		/// <returns>The total number of bytes read into the buffer. Returns 0 if the end of the stream is reached before any bytes are
		/// read.</returns>
		public static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count)
		{
			int total = 0;
			while (total < count)
			{
				int read = await stream.ReadAsync(buffer, offset + total, count - total);
				if (read == 0)
				{
					return 0;
				}
				total += read;
			}
			return total;
		}

		public static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
		{
			int total = 0;
			while (total < count && !token.IsCancellationRequested)
			{
				int read = await stream.ReadAsync(buffer, offset + total, count - total, cancellationToken: token);
				if (read == 0)
				{
					return 0;
				}
				total += read;
			}
			return total;
		}

		/// <summary>
		/// Reads a packet from the specified network stream asynchronously, extracting the packet type and payload.
		/// </summary>
		/// <remarks>The method expects the stream to provide a packet length prefix (2 bytes) followed by the packet
		/// data. If the stream is disconnected or does not provide the expected data, the result will indicate failure. The
		/// caller should ensure the stream is in a valid state before invoking this method.</remarks>
		/// <param name="stream">The network stream from which to read the packet data. Must be readable and positioned at the start of a packet.</param>
		/// <param name="token">An optional cancellation token that can be used to cancel the read operation.</param>
		/// <returns>A task that represents the asynchronous read operation. The result contains information about the success of the
		/// read, the packet type, and the payload. If the stream is disconnected or the read fails, the result indicates
		/// failure and includes an error message.</returns>
		public static async Task<ReadNetworkPacketResult> ReadNetworkStream(Stream stream, CancellationToken token)
		{
			// Read packet length (2 bytes)
			byte[] lengthBuffer = new byte[LengthPrefixSize];
			int read = await ReadExactAsync(stream, lengthBuffer, 0, LengthPrefixSize, token);
			if (read == 0)
			{
				return CreateFailedReadResult("Unable to read length, returned 0 player disconnected");
			}

			ushort packetLength = (ushort)((lengthBuffer[0] << 8) | lengthBuffer[1]);
			if(!IsValidPacketLength(packetLength, out var errorMsg))
			{
				return CreateFailedReadResult(errorMsg);
			}

			byte[] buffer = new byte[packetLength];
			read = await ReadExactAsync(stream, buffer, 0, packetLength, token);
			if (read == 0)
			{
				return CreateFailedReadResult("Unable to read packet, returned 0 player disconnected");
			}

			// Packet type + UTC send timestamp + payload
			PacketType type = (PacketType)buffer[0];
			long sentAtUnixMilliseconds = ReadInt64BigEndian(buffer, PacketTypeSize);
			byte[] payload = new byte[packetLength - HeaderSize];
			Array.Copy(buffer, HeaderSize, payload, 0, payload.Length);
			return new ReadNetworkPacketResult()
			{
				Success = true,
				PacketType = type,
				SentAtUnixMilliseconds = sentAtUnixMilliseconds,
				Payload = payload,
			};
		}

		public static async Task<ReadNetworkPacketResult> ReadNetworkStream(Stream stream)
		{
			// Read packet length (2 bytes)
			byte[] lengthBuffer = new byte[LengthPrefixSize];
			int read = await ReadExactAsync(stream, lengthBuffer, 0, LengthPrefixSize);
			if (read == 0)
			{
				return CreateFailedReadResult("Unable to read length, returned 0 player disconnected");
			}

			ushort packetLength = (ushort)((lengthBuffer[0] << 8) | lengthBuffer[1]);
			if(!IsValidPacketLength(packetLength, out var errorMsg))
			{
				return CreateFailedReadResult(errorMsg);
			}

			byte[] buffer = new byte[packetLength];
			read = await ReadExactAsync(stream, buffer, 0, packetLength);
			if (read == 0)
			{
				return CreateFailedReadResult("Unable to read packet, returned 0 player disconnected");
			}

			// Packet type + UTC send timestamp + payload
			PacketType type = (PacketType)buffer[0];
			long sentAtUnixMilliseconds = ReadInt64BigEndian(buffer, PacketTypeSize);
			byte[] payload = new byte[packetLength - HeaderSize];
			Array.Copy(buffer, HeaderSize, payload, 0, payload.Length);
			return new ReadNetworkPacketResult()
			{
				Success = true,
				PacketType = type,
				SentAtUnixMilliseconds = sentAtUnixMilliseconds,
				Payload = payload,
			};
		}

		/// <summary>
		/// Builds a packet with the specified type and payload, formatted with a length prefix and packet type header.
		/// </summary>
		/// <remarks>The returned packet format is: [2 bytes length][1 byte packet type][8 bytes UTC Unix milliseconds][payload].
		/// The length prefix specifies the combined length of the header and payload, excluding the length prefix itself.</remarks>
		/// <param name="packetType">The type of packet to include in the packet header. Determines how the payload will be interpreted by the
		/// receiver.</param>
		/// <param name="payload">The payload data to include in the packet. Cannot be null.</param>
		/// <returns>A byte array containing the constructed packet, with a 2-byte length prefix, a 1-byte packet type,
		/// an 8-byte UTC Unix-millisecond timestamp, and the payload data.</returns>
		public static byte[] BuildPacket(PacketType packetType, byte[] payload)
		{
			return BuildPacket(packetType, payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		}

		/// <summary>Parses one complete HawkNet packet carried by a datagram transport.</summary>
		public static ReadNetworkPacketResult ReadNetworkDatagram(byte[] datagram)
		{
			if (datagram is null) throw new ArgumentNullException(nameof(datagram));
			if (datagram.Length < LengthPrefixSize)
				return CreateFailedReadResult("Datagram is shorter than the packet length prefix.");

			ushort packetLength = (ushort)((datagram[0] << 8) | datagram[1]);
			if (!IsValidPacketLength(packetLength, out string errorMsg))
				return CreateFailedReadResult(errorMsg);
			if (datagram.Length != LengthPrefixSize + packetLength)
				return CreateFailedReadResult(
					$"Datagram length {datagram.Length} does not match framed packet length {LengthPrefixSize + packetLength}.");

			byte[] payload = new byte[packetLength - HeaderSize];
			Array.Copy(datagram, LengthPrefixSize + HeaderSize, payload, 0, payload.Length);
			return new ReadNetworkPacketResult
			{
				Success = true,
				PacketType = (PacketType)datagram[LengthPrefixSize],
				SentAtUnixMilliseconds = ReadInt64BigEndian(datagram, LengthPrefixSize + PacketTypeSize),
				Payload = payload,
				ErrorMsg = string.Empty
			};
		}

		/// <summary>Builds a packet using an explicit UTC Unix-millisecond timestamp.</summary>
		public static byte[] BuildPacket(PacketType packetType, byte[] payload, long sentAtUnixMilliseconds)
		{
			if(payload is null)
			{
				throw new ArgumentNullException(nameof(payload));
			}

			if(payload.Length > MaxPayloadLength)
			{
				throw new ArgumentOutOfRangeException(
					nameof(payload),
					payload.Length,
					$"Payload cannot exceed {MaxPayloadLength} bytes.");
			}

			ushort length = (ushort)(HeaderSize + payload.Length); // packet type + timestamp + payload
			byte[] packet = new byte[LengthPrefixSize + length]; // length prefix + packet data
			packet[0] = (byte)(length >> 8);
			packet[1] = (byte)(length & 0xFF);
			packet[LengthPrefixSize] = (byte)packetType;
			WriteInt64BigEndian(packet, LengthPrefixSize + PacketTypeSize, sentAtUnixMilliseconds);
			Array.Copy(payload, 0, packet, LengthPrefixSize + HeaderSize, payload.Length);
			return packet;
		}

		public static byte[] BuildHeaderOnlyPacket(PacketType packetType)
		{
			return BuildPacket(packetType, Array.Empty<byte>());
		}

		private static bool IsValidPacketLength(int packetLength, out string errorMsg)
		{
			if(packetLength < MinPacketLength)
			{
				errorMsg = $"Invalid packet length {packetLength}. Packet length must be at least {MinPacketLength}.";
				return false;
			}

			if(packetLength > MaxPacketLength)
			{
				errorMsg = $"Invalid packet length {packetLength}. Packet length cannot exceed {MaxPacketLength}.";
				return false;
			}

			errorMsg = string.Empty;
			return true;
		}

		private static void WriteInt64BigEndian(byte[] buffer, int offset, long value)
		{
			ulong unsignedValue = unchecked((ulong)value);
			for (int index = 0; index < TimestampSize; index++)
			{
				buffer[offset + index] = (byte)(unsignedValue >> ((TimestampSize - 1 - index) * 8));
			}
		}

		private static long ReadInt64BigEndian(byte[] buffer, int offset)
		{
			ulong value = 0;
			for (int index = 0; index < TimestampSize; index++)
			{
				value = (value << 8) | buffer[offset + index];
			}
			return unchecked((long)value);
		}

		private static ReadNetworkPacketResult CreateFailedReadResult(string errorMsg)
		{
			return new ReadNetworkPacketResult()
			{
				Success = false,
				PacketType = PacketType.Unknown,
				Payload = Array.Empty<byte>(),
				ErrorMsg = errorMsg
			};
		}

	} // end class

} // end namespace
