using HawkNet.CoreUx.Common.Infrastructure;
using System.Windows.Input;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class MatchPlayerViewModel : ObservableObject
{
    private bool _isConnected;

    public MatchPlayerViewModel(int playerId, string displayName, bool isConnected, Action<int>? select = null)
    {
        PlayerId = playerId;
        DisplayName = displayName;
        _isConnected = isConnected;
        SelectCommand = new RelayCommand(() => select?.Invoke(PlayerId));
    }

    public int PlayerId { get; }

    public string DisplayName { get; }

    public string DisplayLabel => $"[{PlayerId}][{DisplayName}]";

    public ICommand SelectCommand { get; }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionText));
            }
        }
    }

    public string ConnectionText => IsConnected ? "Connected" : "Disconnected";
}
