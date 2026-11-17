using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class ChatLobbyMemberViewModel : ObservableObject
{
    private bool _isReady;

    public ChatLobbyMemberViewModel(int playerId, string displayName, bool isReady)
    {
        PlayerId = playerId;
        DisplayName = displayName;
        _isReady = isReady;
    }

    public int PlayerId { get; }

    public string DisplayName { get; }

    public string DisplayLabel => $"[{PlayerId}][{DisplayName}]";

    public string DisplayNameOnly => DisplayName;

    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (SetProperty(ref _isReady, value))
            {
                OnPropertyChanged(nameof(ReadyText));
            }
        }
    }

    public string ReadyText => IsReady ? "Ready" : "Not ready";
}
