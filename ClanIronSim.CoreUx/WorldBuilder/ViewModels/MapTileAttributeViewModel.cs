using HawkNet.CoreUx.Common.Infrastructure;

namespace ClanIronSim.CoreUx.WorldBuilder.ViewModels;

public sealed class MapTileAttributeViewModel : ObservableObject
{
    private string _type = "Movement";
    private int _amount = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(IsMovement));
                OnPropertyChanged(nameof(IsBlocking));
                OnPropertyChanged(nameof(Effect));
            }
        }
    }

    public int Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public bool IsMovement => string.Equals(Type, "Movement", StringComparison.OrdinalIgnoreCase);

    public bool IsBlocking => string.Equals(Type, "Blocking", StringComparison.OrdinalIgnoreCase);

    public string Effect => IsBlocking
        ? "Movement is unavailable."
        : "Changes movement cost to enter this tile.";
}
