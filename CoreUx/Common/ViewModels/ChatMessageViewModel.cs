using Avalonia.Media;

namespace HawkNet.CoreUx.Common.ViewModels;

public sealed class ChatMessageViewModel
{
    private static readonly IBrush AdminBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly string[] PlayerColors =
    [
        "#38BDF8",
        "#34D399",
        "#A78BFA",
        "#F472B6",
        "#FBBF24",
        "#2DD4BF",
        "#FB7185",
        "#60A5FA"
    ];

    public ChatMessageViewModel(int playerId, string displayName, DateTime timestampUtc, string message)
    {
        PlayerId = playerId;
        DisplayName = displayName;
        TimestampUtc = timestampUtc;
        Message = message;
        SenderBrush = CreateSenderBrush(playerId);
    }

    public int PlayerId { get; }

    public string DisplayName { get; }

    public string SenderLabel => PlayerId == 0 ? "Admin" : $"[{PlayerId}][{DisplayName}]";

    public IBrush SenderBrush { get; }

    public DateTime TimestampUtc { get; }

    public string Message { get; }

    public string TimestampText => TimestampUtc.ToLocalTime().ToString("h:mm tt");

    private static IBrush CreateSenderBrush(int playerId)
    {
        if (playerId == 0)
        {
            return AdminBrush;
        }

        int colorIndex = Math.Abs(playerId.GetHashCode()) % PlayerColors.Length;
        return new SolidColorBrush(Color.Parse(PlayerColors[colorIndex]));
    }
}
