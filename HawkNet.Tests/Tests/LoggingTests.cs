using HawkNet.Core;
using Xunit;

namespace HawkNet.Tests
{
	public class LoggingTests
	{
		[Fact]
		public void NetLogger_RespectsMinimumLevel()
		{
			var logger = new NetLogger(HawkNetLogLevel.Warning);

			Assert.False(logger.IsEnabled(HawkNetLogLevel.Information));
			Assert.True(logger.IsEnabled(HawkNetLogLevel.Warning));
			Assert.True(logger.IsEnabled(HawkNetLogLevel.Error));
		}

		[Fact]
		public void NullLogger_DisablesAllLevels()
		{
			Assert.False(NullLogger.Instance.IsEnabled(HawkNetLogLevel.Critical));
		}

		[Fact]
		public void Log_WithTag_PrefixesMessageBodyWithTag()
		{
			var logger = new NetLogger();

			string output = CaptureConsoleOutput(() => logger.Log("hello", "ConnectionManager"));

			Assert.Contains("[ConnectionManager]: hello", output);
		}

		[Fact]
		public void Log_WithNullOrWhitespaceTag_OmitsTagPrefix()
		{
			var logger = new NetLogger();

			string nullTagOutput = CaptureConsoleOutput(() => logger.Log("plain", null));
			string whitespaceTagOutput = CaptureConsoleOutput(() => logger.Log("plain", "   "));

			Assert.Contains("plain", nullTagOutput);
			Assert.DoesNotContain("[]:", nullTagOutput);
			Assert.Contains("plain", whitespaceTagOutput);
			Assert.DoesNotContain("[]:", whitespaceTagOutput);
		}

		[Fact]
		public void Error_PrintsCurrentStackTrace()
		{
			var logger = new NetLogger();

			string output = CaptureConsoleOutput(() => logger.Error("failed", "Test"));

			Assert.Contains("[Test]: failed", output);
			Assert.Contains(nameof(Error_PrintsCurrentStackTrace), output);
		}

		[Fact]
		public void Exception_PrintsExceptionDetails()
		{
			var logger = new NetLogger();
			var exception = new InvalidOperationException("boom");

			string output = CaptureConsoleOutput(() => logger.Exception(exception, "failed", "Test"));

			Assert.Contains("[Test]: failed", output);
			Assert.Contains("System.InvalidOperationException", output);
			Assert.Contains("boom", output);
		}

		private static string CaptureConsoleOutput(Action action)
		{
			var originalOut = Console.Out;
			using var writer = new StringWriter();
			try
			{
				Console.SetOut(writer);
				action();
			}
			finally
			{
				Console.SetOut(originalOut);
			}

			return writer.ToString();
		}
	}
}
