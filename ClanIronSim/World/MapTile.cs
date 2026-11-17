using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

[JsonConverter(typeof(MapTileJsonConverter))]
public abstract class MapTile
{
    public SimIntVector2 GridPosition { get; set; }

    [JsonIgnore]
    public int GridX
    {
        get => GridPosition.X;
        set => GridPosition = new SimIntVector2(value, GridPosition.Z);
    }

    [JsonIgnore]
    public int GridZ
    {
        get => GridPosition.Z;
        set => GridPosition = new SimIntVector2(GridPosition.X, value);
    }

    public MapTileShape Shape { get; set; } = MapTileShape.Rectangle;

    public SimVector2 LocalPosition { get; set; }

    [JsonIgnore]
    public float X
    {
        get => LocalPosition.X;
        set => LocalPosition = new SimVector2(value, LocalPosition.Z);
    }

    [JsonIgnore]
    public float Z
    {
        get => LocalPosition.Z;
        set => LocalPosition = new SimVector2(LocalPosition.X, value);
    }

    public SimVector2 WorldPosition { get; set; }

    [JsonIgnore]
    public float WorldX
    {
        get => WorldPosition.X;
        set => WorldPosition = new SimVector2(value, WorldPosition.Z);
    }

    [JsonIgnore]
    public float WorldZ
    {
        get => WorldPosition.Z;
        set => WorldPosition = new SimVector2(WorldPosition.X, value);
    }

    public SimVector2 Size { get; set; }

    [JsonIgnore]
    public float SizeX
    {
        get => Size.X;
        set => Size = new SimVector2(value, Size.Z);
    }

    [JsonIgnore]
    public float SizeY
    {
        get => Size.Z;
        set => Size = new SimVector2(Size.X, value);
    }

    [JsonIgnore]
    public abstract float WorldCenterX { get; }

    [JsonIgnore]
    public abstract float WorldCenterZ { get; }

    [JsonIgnore]
    public SimVector2 WorldCenter => new(WorldCenterX, WorldCenterZ);

    [JsonIgnore]
    public abstract MapBounds Bounds { get; }

    [JsonIgnore]
    public bool IsSelected { get; private set; }

    [JsonIgnore]
    public int MovementCost
    {
        get
        {
            if (Attributes.Any(attribute => string.Equals(attribute.ModifierKey, BlockingAttributeKey, StringComparison.OrdinalIgnoreCase)))
            {
                return -1;
            }

            return System.Math.Max(
                1,
                1 + Attributes
                    .Where(attribute => string.Equals(attribute.ModifierKey, MovementCostAttributeKey, StringComparison.OrdinalIgnoreCase))
                    .Sum(attribute => attribute.Amount));
        }
    }

    public List<MapAttribute> Attributes { get; set; } = new();

    public void AddAttribute(MapAttribute attribute)
    {
        Attributes.Add(attribute ?? throw new ArgumentNullException(nameof(attribute)));
    }

    public void Select()
    {
        IsSelected = true;
    }

    public void Unselect()
    {
        IsSelected = false;
    }

    public abstract bool ContainsWorldPosition(float worldX, float worldZ);

    internal abstract MapTile CopyForPlacement(int offsetX, int offsetZ);

    private const string BlockingAttributeKey = "blocking";
    private const string MovementCostAttributeKey = "movement";

    protected void CopyBaseTo(MapTile tile, int offsetX, int offsetZ)
    {
        tile.GridPosition = GridPosition;
        tile.Shape = Shape;
        tile.LocalPosition = LocalPosition;
        tile.WorldPosition = new SimVector2(offsetX + X, offsetZ + Z);
        tile.Size = Size;
        tile.Attributes = Attributes.Select(attribute => new MapAttribute
            {
                Id = attribute.Id,
                ModifierKey = attribute.ModifierKey,
                Amount = attribute.Amount
            })
            .ToList();
    }
}
