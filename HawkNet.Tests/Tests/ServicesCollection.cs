using Xunit;

namespace HawkNet.Tests
{
	[CollectionDefinition(Name, DisableParallelization = true)]
	public sealed class ServicesCollection
	{
		public const string Name = "Services registry";
	}
}
