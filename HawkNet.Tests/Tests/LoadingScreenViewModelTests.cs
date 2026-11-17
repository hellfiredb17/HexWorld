using HawkNet.CoreUx.Common.ViewModels;
using Xunit;

namespace HawkNet.Tests
{
	public class LoadingScreenViewModelTests
	{
		[Fact]
		public void Constructor_StoresCustomTextAndDefaultsImageToHidden()
		{
			var viewModel = new LoadingScreenViewModel(
				"Loading Match",
				"Preparing the next state.");

			Assert.Equal("Loading Match", viewModel.HeaderText);
			Assert.Equal("Preparing the next state.", viewModel.ContentText);
			Assert.Null(viewModel.ImageKey);
			Assert.False(viewModel.HasImage);
		}
	}
}
