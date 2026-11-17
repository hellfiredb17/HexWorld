/*
 * Owner: Duncan Budinger
 * Project: HawkNet
 * Created: 11/10/2025 4:35:34 PM
 * Description: Wrapper for cancellation token source. Handles creating, cancel, and dispose
 */

namespace HawkNet.Core
{
	/// <summary>
	/// Allows for reusing a cancellation token source by creating, canceling, and disposing as needed.
	/// </summary>
	public class ReuseCancelTokenSource
	{
		//---- Variables
		//--------------
		private CancellationTokenSource? _tokenSource;

		//---- Properties
		//---------------
		public CancellationTokenSource TokenSource =>
			_tokenSource ?? throw new InvalidOperationException("Cancellation token source has not been created.");

		public CancellationToken Token => _tokenSource?.Token ?? default;

		//---- Public
		//-----------
		public void CreateToken()
		{
			// clean up any old tokens
			CancelAndDispose();

			// create a new token
			_tokenSource = new CancellationTokenSource();
		}

		public void CreateToken(int timeoutMilliseconds)
		{
			// clean up any old tokens
			CancelAndDispose();

			// create a new token
			_tokenSource = new CancellationTokenSource(timeoutMilliseconds);			
		}

		public void Cancel()
		{
			if (_tokenSource is null || _tokenSource.IsCancellationRequested)
			{
				return;
			}
			_tokenSource.Cancel();
		}

		public void Dispose()
		{
			if(_tokenSource is null)
			{
				return;
			}
			_tokenSource.Dispose();
			_tokenSource = null;
		}

		public void CancelAndDispose()
		{
			if (_tokenSource is null)
			{
				return;
			}
			Cancel();
			Dispose();
		}

	} // end class
} // end namespace
