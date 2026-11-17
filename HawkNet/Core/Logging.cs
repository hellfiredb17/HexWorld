/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 */

namespace HawkNet.Core
{
	public enum HawkNetLogLevel
	{
		Trace = 0,
		Debug = 1,
		Information = 2,
		Warning = 3,
		Error = 4,
		Critical = 5,
		None = 6
	}

	public interface ILogger
	{
		void Log(string message, string? tag = null);
		void DebugLog(string message, string? tag = null);
		void Warning(string message, string? tag = null);
		void Error(string message, string? tag = null);
		void Exception(Exception exception, string? message = null, string? tag = null);
	}

	public sealed class NullLogger : ILogger
	{
		public static NullLogger Instance { get; } = new();

		private NullLogger()
		{
		}

		public bool IsEnabled(HawkNetLogLevel level)
		{
			return false;
		}

		public void Log(string message, string? tag = null)
		{
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

	public sealed class NetLogger : ILogger
	{
		private readonly HawkNetLogLevel _minimumLevel;

		public NetLogger(HawkNetLogLevel minimumLevel = HawkNetLogLevel.Information)
		{
			_minimumLevel = minimumLevel;
		}

		public bool IsEnabled(HawkNetLogLevel level)
		{
			return level >= _minimumLevel && _minimumLevel != HawkNetLogLevel.None;
		}

		public void Log(string message, string? tag = null)
		{
			Write(HawkNetLogLevel.Information, message, tag);
		}

		public void DebugLog(string message, string? tag = null)
		{
			Write(HawkNetLogLevel.Debug, message, tag);
		}

		public void Warning(string message, string? tag = null)
		{
			Write(HawkNetLogLevel.Warning, message, tag);
		}

		public void Error(string message, string? tag = null)
		{
			Write(HawkNetLogLevel.Error, message, tag, Environment.StackTrace);
		}

		public void Exception(Exception exception, string? message = null, string? tag = null)
		{
			string output = string.IsNullOrWhiteSpace(message)
				? exception.ToString()
				: $"{message}{Environment.NewLine}{exception}";
			Write(HawkNetLogLevel.Error, output, tag);
		}

		private void Write(HawkNetLogLevel level, string message, string? tag, string? stackTrace = null)
		{
			if(!IsEnabled(level))
			{
				return;
			}

			string timestamp = DateTime.UtcNow.ToString("O");
			string formattedMessage = FormatMessage(message, tag);
			Console.WriteLine($"[{timestamp}] [{level}] {formattedMessage}");
			if(!string.IsNullOrWhiteSpace(stackTrace))
			{
				Console.WriteLine(stackTrace);
			}
		}

		private static string FormatMessage(string message, string? tag)
		{
			if(string.IsNullOrWhiteSpace(tag))
			{
				return message;
			}

			return $"[{tag.Trim()}]: {message}";
		}
	}
}
