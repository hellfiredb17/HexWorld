using System.Text.Json;
using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public sealed class WorldMap
{
    public const int CurrentGeometryVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public int GeometryVersion { get; set; } = CurrentGeometryVersion;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public MapTileSettings TileSettings { get; set; } = new();

    public List<MapRegion> Regions { get; set; } = new();

    public void AddRegion(MapRegion region)
    {
        if (region is null)
        {
            throw new ArgumentNullException(nameof(region));
        }

        if (Regions.Any(existing => existing.Bounds.Overlaps(region.Bounds)))
        {
            throw new InvalidOperationException("Map region overlaps an existing region.");
        }

        Regions.Add(region);
    }

    public bool TryGetRegionAtWorldPosition(float worldX, float worldZ, out MapRegion? region)
    {
        region = Regions.FirstOrDefault(candidate => candidate.Bounds.Contains(worldX, worldZ));
        return region is not null;
    }

    public bool TryGetRegionAtWorldPosition(SimVector2 worldPosition, out MapRegion? region)
    {
        return TryGetRegionAtWorldPosition(worldPosition.X, worldPosition.Z, out region);
    }

    public bool TryGetSectionAtWorldPosition(float worldX, float worldZ, out PlacedMapSection? section)
    {
        section = null;
        if (!TryGetRegionAtWorldPosition(worldX, worldZ, out MapRegion? region))
        {
            return false;
        }

        return region!.TryGetSectionAtWorldPosition(worldX, worldZ, out section);
    }

    public bool TryGetSectionAtWorldPosition(SimVector2 worldPosition, out PlacedMapSection? section)
    {
        return TryGetSectionAtWorldPosition(worldPosition.X, worldPosition.Z, out section);
    }

    public bool TryGetTileAtWorldPosition(float worldX, float worldZ, out MapTile? tile)
    {
        tile = null;
        if (!TryGetRegionAtWorldPosition(worldX, worldZ, out MapRegion? region))
        {
            return false;
        }

        return region!.TryGetTileAtWorldPosition(worldX, worldZ, out tile);
    }

    public bool TryGetTileAtWorldPosition(SimVector2 worldPosition, out MapTile? tile)
    {
        return TryGetTileAtWorldPosition(worldPosition.X, worldPosition.Z, out tile);
    }

    public bool TryGetPlacedTileAtWorldPosition(float worldX, float worldZ, out PlacedMapTile placedTile)
    {
        placedTile = default;
        if (!TryGetRegionAtWorldPosition(worldX, worldZ, out MapRegion? region)
            || !region!.TryGetSectionAtWorldPosition(worldX, worldZ, out PlacedMapSection? section))
        {
            return false;
        }

        MapTile? tile = section!.Section.Tiles.FirstOrDefault(candidate => candidate.ContainsWorldPosition(worldX, worldZ));
        if (tile is null)
        {
            return false;
        }

        placedTile = new PlacedMapTile(section, tile);
        return true;
    }

    public bool TryGetPlacedTileAtWorldPosition(SimVector2 worldPosition, out PlacedMapTile placedTile)
    {
        return TryGetPlacedTileAtWorldPosition(worldPosition.X, worldPosition.Z, out placedTile);
    }

    public IReadOnlyList<PlacedMapTile> GetNeighborTiles(
        PlacedMapTile placedTile,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (placedTile.Section is null)
        {
            throw new ArgumentException("Placed tile section cannot be null.", nameof(placedTile));
        }

        if (placedTile.Tile is null)
        {
            throw new ArgumentException("Placed tile cannot be null.", nameof(placedTile));
        }

        var neighbors = new List<PlacedMapTile>();
        foreach ((float offsetX, float offsetZ) in GetNeighborCenterOffsets(placedTile.Tile, mode))
        {
            float targetCenterX = placedTile.Tile.WorldCenterX + offsetX;
            float targetCenterZ = placedTile.Tile.WorldCenterZ + offsetZ;
            if (TryGetPlacedTileAtWorldCenter(
                    targetCenterX,
                    targetCenterZ,
                    placedTile.Tile,
                    out PlacedMapTile neighbor))
            {
                neighbors.Add(neighbor);
            }
        }

        return neighbors;
    }

    private bool TryGetPlacedTileAtWorldCenter(
        float worldCenterX,
        float worldCenterZ,
        MapTile excludedTile,
        out PlacedMapTile placedTile)
    {
        foreach (PlacedMapTile candidate in EnumeratePlacedTiles())
        {
            if (!ReferenceEquals(candidate.Tile, excludedTile)
                && System.Math.Abs(candidate.Tile.WorldCenterX - worldCenterX) < PositionTolerance
                && System.Math.Abs(candidate.Tile.WorldCenterZ - worldCenterZ) < PositionTolerance)
            {
                placedTile = candidate;
                return true;
            }
        }

        placedTile = default;
        return false;
    }

    public bool TryFindPath(
        WorldMapPosition start,
        WorldMapPosition goal,
        out IReadOnlyList<PlacedMapTile> path,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        path = Array.Empty<PlacedMapTile>();
        if (!TryGetPlacedTileAtWorldPosition(start.X, start.Z, out PlacedMapTile startTile)
            || !TryGetPlacedTileAtWorldPosition(goal.X, goal.Z, out PlacedMapTile goalTile))
        {
            return false;
        }

        return TryFindPath(startTile, goalTile, out path, mode);
    }

    public bool TryFindPath(
        SimVector2 start,
        SimVector2 goal,
        out IReadOnlyList<PlacedMapTile> path,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        return TryFindPath(new WorldMapPosition(start), new WorldMapPosition(goal), out path, mode);
    }

    public bool TryFindPath(
        PlacedMapTile start,
        PlacedMapTile goal,
        out IReadOnlyList<PlacedMapTile> path,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        path = Array.Empty<PlacedMapTile>();
        if (!ContainsPlacedTile(start) || !ContainsPlacedTile(goal) || goal.Tile.MovementCost < 0)
        {
            return false;
        }

        if (start.Equals(goal))
        {
            path = new[] { start };
            return true;
        }

        var open = new List<PlacedMapTile> { start };
        var cameFrom = new Dictionary<PlacedMapTile, PlacedMapTile>();
        var gScore = new Dictionary<PlacedMapTile, int>
        {
            [start] = 0
        };
        var fScore = new Dictionary<PlacedMapTile, int>
        {
            [start] = GetHeuristicDistance(start.Tile, goal.Tile, mode)
        };

        while (open.Count > 0)
        {
            PlacedMapTile current = open
                .OrderBy(tile => fScore.TryGetValue(tile, out int score) ? score : int.MaxValue)
                .ThenBy(tile => gScore.TryGetValue(tile, out int score) ? score : int.MaxValue)
                .First();
            if (current.Equals(goal))
            {
                path = ReconstructPath(cameFrom, current);
                return true;
            }

            open.Remove(current);
            foreach (PlacedMapTile neighbor in GetNeighborTiles(current, mode))
            {
                if (neighbor.Tile.MovementCost < 0)
                {
                    continue;
                }

                int tentativeGScore = gScore[current] + neighbor.Tile.MovementCost;
                if (!gScore.TryGetValue(neighbor, out int existingGScore)
                    || tentativeGScore < existingGScore)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = tentativeGScore + GetHeuristicDistance(neighbor.Tile, goal.Tile, mode);
                    if (!open.Contains(neighbor))
                    {
                        open.Add(neighbor);
                    }
                }
            }
        }

        return false;
    }

    public IReadOnlyList<PlacedMapTile> GetReachableTiles(
        WorldMapPosition start,
        int movementBudget,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (!TryGetPlacedTileAtWorldPosition(start.X, start.Z, out PlacedMapTile startTile))
        {
            return Array.Empty<PlacedMapTile>();
        }

        return GetReachableTiles(startTile, movementBudget, mode);
    }

    public IReadOnlyList<PlacedMapTile> GetReachableTiles(
        SimVector2 start,
        int movementBudget,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        return GetReachableTiles(new WorldMapPosition(start), movementBudget, mode);
    }

    public IReadOnlyList<PlacedMapTile> GetReachableTiles(
        PlacedMapTile start,
        int movementBudget,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (movementBudget < 0 || !ContainsPlacedTile(start))
        {
            return Array.Empty<PlacedMapTile>();
        }

        var reachable = new List<PlacedMapTile>();
        var costs = new Dictionary<PlacedMapTile, int>
        {
            [start] = 0
        };
        var frontier = new List<PlacedMapTile> { start };

        while (frontier.Count > 0)
        {
            PlacedMapTile current = frontier
                .OrderBy(tile => costs[tile])
                .First();
            frontier.Remove(current);

            foreach (PlacedMapTile neighbor in GetNeighborTiles(current, mode))
            {
                if (neighbor.Tile.MovementCost < 0)
                {
                    continue;
                }

                int cost = costs[current] + neighbor.Tile.MovementCost;
                if (cost > movementBudget)
                {
                    continue;
                }

                if (!costs.TryGetValue(neighbor, out int existingCost) || cost < existingCost)
                {
                    costs[neighbor] = cost;
                    if (!neighbor.Equals(start) && !reachable.Contains(neighbor))
                    {
                        reachable.Add(neighbor);
                    }

                    if (!frontier.Contains(neighbor))
                    {
                        frontier.Add(neighbor);
                    }
                }
            }
        }

        return reachable
            .OrderBy(tile => costs[tile])
            .ThenBy(tile => tile.Section.OffsetZ)
            .ThenBy(tile => tile.Section.OffsetX)
            .ThenBy(tile => tile.Tile.GridZ)
            .ThenBy(tile => tile.Tile.GridX)
            .ToArray();
    }

    public bool TryWorldToSectionGridPosition(
        float worldX,
        float worldZ,
        out MapSectionGridPosition position)
    {
        position = default;
        if (!TryGetRegionAtWorldPosition(worldX, worldZ, out MapRegion? region))
        {
            return false;
        }

        return region!.TryWorldToSectionGridPosition(worldX, worldZ, out position);
    }

    public bool TryWorldToSectionGridPosition(
        SimVector2 worldPosition,
        out MapSectionGridPosition position)
    {
        return TryWorldToSectionGridPosition(worldPosition.X, worldPosition.Z, out position);
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

        MapRegion? region = Regions.FirstOrDefault(candidate => candidate.Sections.Contains(section));
        if (region is null)
        {
            position = default;
            return false;
        }

        return region.TrySectionGridToWorldPosition(section, gridX, gridZ, out position);
    }

    public bool TrySectionGridToWorldPosition(
        PlacedMapSection section,
        SimIntVector2 gridPosition,
        out WorldMapPosition position)
    {
        return TrySectionGridToWorldPosition(section, gridPosition.X, gridPosition.Z, out position);
    }

    private static IReadOnlyList<(float X, float Z)> GetNeighborCenterOffsets(MapTile tile, TileNeighborMode mode)
    {
        if (tile is MapHexTile hexTile)
        {
            return hexTile.Coordinate.Neighbors()
                .Select(neighbor => GetHexCenterOffset(
                    neighbor.Q - hexTile.Coordinate.Q,
                    neighbor.R - hexTile.Coordinate.R,
                    hexTile.SizeX,
                    hexTile.SizeY,
                    hexTile.Orientation))
                .ToArray();
        }

        (int X, int Z)[] rectOffsets = mode switch
        {
            TileNeighborMode.Edges =>
            [
                (0, -1),
                (1, 0),
                (0, 1),
                (-1, 0)
            ],
            TileNeighborMode.EdgesAndCorners =>
            [
                (0, -1),
                (1, -1),
                (1, 0),
                (1, 1),
                (0, 1),
                (-1, 1),
                (-1, 0),
                (-1, -1)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tile neighbor mode.")
        };

        return rectOffsets
            .Select(offset => (offset.X * tile.SizeX, offset.Z * tile.SizeY))
            .ToArray();
    }

    private static (float X, float Z) GetHexCenterOffset(
        int q,
        int r,
        float sizeX,
        float sizeY,
        HexOrientation orientation)
    {
        return orientation switch
            {
                HexOrientation.PointyTop => (q * sizeX * 0.75f, (r + (q * 0.5f)) * sizeY),
                HexOrientation.FlatTop => ((q + (r * 0.5f)) * sizeX, r * sizeY * 0.75f),
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unsupported hex orientation.")
            };
    }

    private IEnumerable<PlacedMapTile> EnumeratePlacedTiles()
    {
        foreach (MapRegion region in Regions)
        {
            foreach (PlacedMapSection section in region.Sections)
            {
                foreach (MapTile tile in section.Section.Tiles)
                {
                    yield return new PlacedMapTile(section, tile);
                }
            }
        }
    }

    private bool ContainsPlacedTile(PlacedMapTile placedTile)
    {
        return placedTile.Section is not null
            && placedTile.Tile is not null
            && Regions.Any(region =>
                region.Sections.Contains(placedTile.Section)
                && placedTile.Section.Section.Tiles.Contains(placedTile.Tile));
    }

    private static IReadOnlyList<PlacedMapTile> ReconstructPath(
        Dictionary<PlacedMapTile, PlacedMapTile> cameFrom,
        PlacedMapTile current)
    {
        var path = new List<PlacedMapTile> { current };
        while (cameFrom.TryGetValue(current, out PlacedMapTile previous))
        {
            current = previous;
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static int GetHeuristicDistance(MapTile from, MapTile to, TileNeighborMode mode)
    {
        if (from is MapHexTile fromHex && to is MapHexTile toHex)
        {
            return fromHex.Coordinate.DistanceTo(toHex.Coordinate);
        }

        int deltaX = System.Math.Abs(from.GridX - to.GridX);
        int deltaZ = System.Math.Abs(from.GridZ - to.GridZ);
        return mode switch
        {
            TileNeighborMode.Edges => deltaX + deltaZ,
            TileNeighborMode.EdgesAndCorners => System.Math.Max(deltaX, deltaZ),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tile neighbor mode.")
        };
    }

    private const float PositionTolerance = 0.001f;

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static WorldMap FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("World map JSON cannot be empty.", nameof(json));
        }

        WorldMap map = JsonSerializer.Deserialize<WorldMap>(json, JsonOptions)
            ?? throw new InvalidOperationException("World map JSON did not contain a map.");
        map.MigrateGeometryVersion();
        return map;
    }

    public async Task SaveAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("World map path cannot be empty.", nameof(path));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ToJson()).ConfigureAwait(false);
    }

    public static async Task<WorldMap> LoadAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("World map path cannot be empty.", nameof(path));
        }

        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return FromJson(json);
    }

    private void MigrateGeometryVersion()
    {
        if (GeometryVersion < 0)
        {
            throw new InvalidOperationException("World map geometry version cannot be negative.");
        }

        if (GeometryVersion > CurrentGeometryVersion)
        {
            throw new InvalidOperationException(
                $"World map geometry version {GeometryVersion} is newer than supported version {CurrentGeometryVersion}.");
        }

        if (GeometryVersion == 0)
        {
            GeometryVersion = CurrentGeometryVersion;
        }
    }
}
