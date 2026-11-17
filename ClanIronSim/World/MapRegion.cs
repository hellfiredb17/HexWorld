using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public sealed class MapRegion
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<PlacedMapSection> Sections { get; set; } = new();

    [JsonIgnore]
    public MapBounds Bounds
    {
        get
        {
            if (Sections.Count == 0)
            {
                return new MapBounds(0, 0, 0, 0);
            }

            return new MapBounds(
                Sections.Min(section => section.WorldBounds.MinX),
                Sections.Min(section => section.WorldBounds.MinZ),
                Sections.Max(section => section.WorldBounds.MaxX),
                Sections.Max(section => section.WorldBounds.MaxZ));
        }
    }

    public void AddSection(MapSection section, int offsetX, int offsetZ)
    {
        AddSection(
            section,
            placedSectionId: string.Empty,
            gridX: 0,
            gridZ: 0,
            offsetX,
            offsetZ);
    }

    public void AddSection(MapSection section, SimIntVector2 offset)
    {
        AddSection(section, offset.X, offset.Z);
    }

    public void AddSection(
        MapSection section,
        string placedSectionId,
        int gridX,
        int gridZ,
        int offsetX,
        int offsetZ)
    {
        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        var placedSection = new PlacedMapSection
        {
            Id = placedSectionId ?? string.Empty,
            GridX = gridX,
            GridZ = gridZ,
            OffsetX = offsetX,
            OffsetZ = offsetZ,
            Section = section.CopyForPlacement(offsetX, offsetZ)
        };

        if (Sections.Any(existing => existing.WorldBounds.Overlaps(placedSection.WorldBounds)))
        {
            throw new InvalidOperationException("Map section overlaps an existing placed section in the region.");
        }

        Sections.Add(placedSection);
    }

    public void AddSection(
        MapSection section,
        string placedSectionId,
        SimIntVector2 gridPosition,
        SimIntVector2 offset)
    {
        AddSection(
            section,
            placedSectionId,
            gridPosition.X,
            gridPosition.Z,
            offset.X,
            offset.Z);
    }

    public bool TryGetSectionAtWorldPosition(float worldX, float worldZ, out PlacedMapSection? section)
    {
        section = Sections.FirstOrDefault(placed => placed.WorldBounds.Contains(worldX, worldZ));
        return section is not null;
    }

    public bool TryGetTileAtWorldPosition(float worldX, float worldZ, out MapTile? tile)
    {
        tile = null;
        if (!TryGetSectionAtWorldPosition(worldX, worldZ, out PlacedMapSection? section))
        {
            return false;
        }

        tile = section!.Section.Tiles.FirstOrDefault(candidate => candidate.ContainsWorldPosition(worldX, worldZ));
        return tile is not null;
    }

    public bool TryWorldToSectionGridPosition(
        float worldX,
        float worldZ,
        out MapSectionGridPosition position)
    {
        position = default;
        if (!TryGetSectionAtWorldPosition(worldX, worldZ, out PlacedMapSection? section))
        {
            return false;
        }

        MapTile? tile = section!.Section.Tiles.FirstOrDefault(candidate => candidate.ContainsWorldPosition(worldX, worldZ));
        if (tile is null)
        {
            return false;
        }

        position = new MapSectionGridPosition(section, tile.GridX, tile.GridZ);
        return true;
    }

    public bool TrySectionGridToWorldPosition(
        PlacedMapSection section,
        int gridX,
        int gridZ,
        out WorldMapPosition position)
    {
        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        position = default;
        MapTile? tile = section.Section.Tiles.FirstOrDefault(candidate =>
            candidate.GridX == gridX
            && candidate.GridZ == gridZ);
        if (tile is null)
        {
            return false;
        }

        position = new WorldMapPosition(tile.WorldCenterX, tile.WorldCenterZ);
        return true;
    }
}
