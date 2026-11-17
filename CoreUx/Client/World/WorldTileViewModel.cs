using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Client.World;

public sealed class WorldTileViewModel : ObservableObject
{
    private bool _isHovered;
    private bool _isSelected;

    public WorldTileViewModel(WorldTileSnapshot snapshot)
    {
        Id = snapshot.Id;
        RegionId = snapshot.RegionId;
        PlacedSectionId = snapshot.PlacedSectionId;
        GridX = snapshot.GridX;
        GridZ = snapshot.GridZ;
        WorldX = snapshot.WorldX;
        WorldZ = snapshot.WorldZ;
        SizeX = snapshot.SizeX;
        SizeZ = snapshot.SizeZ;
        CenterX = snapshot.CenterX;
        CenterZ = snapshot.CenterZ;
        Shape = snapshot.Shape;
        HexOrientation = snapshot.HexOrientation;
        MovementCost = snapshot.MovementCost;
        IsBlocked = snapshot.IsBlocked;
        IsSpawner = snapshot.IsSpawner;
    }

    public string Id { get; }

    public string RegionId { get; }

    public string PlacedSectionId { get; }

    public int GridX { get; }

    public int GridZ { get; }

    public float WorldX { get; }

    public float WorldZ { get; }

    public float SizeX { get; }

    public float SizeZ { get; }

    public float CenterX { get; }

    public float CenterZ { get; }

    public WorldTileShape Shape { get; }

    public WorldHexOrientation HexOrientation { get; }

    public int MovementCost { get; }

    public bool IsBlocked { get; }

    public bool IsSpawner { get; }

    public bool IsHovered
    {
        get => _isHovered;
        set => SetProperty(ref _isHovered, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DisplayText => $"({GridX}, {GridZ})";
}
