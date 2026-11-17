using System.Text.Json;

namespace HawkNet.Core.Shared
{
	public interface IPayloadCodec
	{
		byte[] Encode<T>(T value);
		T Decode<T>(byte[] payload);
		bool TryDecode<T>(byte[] payload, out T value);
	}

	public sealed class JsonPayloadCodec : IPayloadCodec
	{
		private readonly JsonSerializerOptions _options;

		public JsonPayloadCodec(JsonSerializerOptions? options = null)
		{
			_options = options ?? CreateDefaultOptions();
		}

		public byte[] Encode<T>(T value)
		{
			if(value is null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			return JsonSerializer.SerializeToUtf8Bytes(value, _options);
		}

		public T Decode<T>(byte[] payload)
		{
			HawkNet.Core.Guard.NotNull(payload);

			return JsonSerializer.Deserialize<T>(payload, _options)
				?? throw new InvalidOperationException($"Payload for '{typeof(T).Name}' was empty.");
		}

		public bool TryDecode<T>(byte[] payload, out T value)
		{
			value = default!;
			if(payload is null)
			{
				return false;
			}

			try
			{
				var decoded = JsonSerializer.Deserialize<T>(payload, _options);
				if(decoded is null)
				{
					return false;
				}

				value = decoded;
				return true;
			}
			catch(JsonException)
			{
				return false;
			}
		}

		private static JsonSerializerOptions CreateDefaultOptions()
		{
			return new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
		}
	}
}
