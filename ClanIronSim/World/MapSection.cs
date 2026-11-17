using System.Text.Json.Serialization;
using ClanIronSim.SimMath;

namespace ClanIronSim.World;

public sealed class MapSection
{
    private static readonly (int X, int Z)[] RectEdgeOffsets =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0)
    ];

    private static readonly (int X, int Z)[] RectEdgeAndCornerOffsets =
    [
        (0, -1),
        (1, -1),
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1)
    ];

    public int Width { get; set; }

    public int Depth { get; set; }

    public List<MapTile> Tiles { get; set; } = new();

    [JsonIgnore]
    public MapBounds Bounds
    {
        get
        {
            if (Tiles.Count == 0)
            {
                return new MapBounds(0, 0, 0, 0);
            }

            return new MapBounds(
                Tiles.Min(tile => tile.Bounds.MinX),
                Tiles.Min(tile => tile.Bounds.MinZ),
                Tiles.Max(tile => tile.Bounds.MaxX),
                Tiles.Max(tile => tile.Bounds.MaxZ));
        }
    }

    public static MapSection CreateGrid(int width, int depth, int tileSizeX, int tileSizeY)
    {
        ValidateGridDimensions(width, depth, tileSizeX, tileSizeY);

        var section = new MapSection
        {
            Width = width,
            Depth = depth
        };

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                section.Tiles.Add(new MapRectTile
                {
                    GridX = x,
                    GridZ = z,
                    X = x * tileSizeX,
                    Z = z * tileSizeY,
                    WorldX = x * tileSizeX,
                    WorldZ = z * tileSizeY,
                    SizeX = tileSizeX,
                    SizeY = tileSizeY
                });
            }
        }

        return section;
    }

    public bool TryGetTileAtGridPosition(int gridX, int gridZ, out MapTile? tile)
    {
        tile = Tiles.FirstOrDefault(candidate =>
            candidate.GridX == gridX
            && candidate.GridZ == gridZ);
        return tile is not null;
    }

    public bool TryGetTileAtGridPosition(SimIntVector2 gridPosition, out MapTile? tile)
    {
        return TryGetTileAtGridPosition(gridPosition.X, gridPosition.Z, out tile);
    }

    public bool TryGetHexTileAtCoordinate(HexCoord coordinate, out MapHexTile? tile)
    {
        tile = Tiles
            .OfType<MapHexTile>()
            .FirstOrDefault(candidate => candidate.Coordinate == coordinate);
        return tile is not null;
    }

    public IReadOnlyList<MapTile> GetNeighborTiles(
        int gridX,
        int gridZ,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (!TryGetTileAtGridPosition(gridX, gridZ, out MapTile? tile))
        {
            return Array.Empty<MapTile>();
        }

        return GetNeighborTiles(tile!, mode);
    }

    public IReadOnlyList<MapTile> GetNeighborTiles(
        MapTile tile,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (tile is null)
        {
            throw new ArgumentNullException(nameof(tile));
        }

        return tile switch
        {
            MapRectTile => GetRectNeighborTiles(tile, mode),
            MapHexTile hexTile => GetHexNeighborTiles(hexTile),
            _ => Array.Empty<MapTile>()
        };
    }

    public bool TryFindPath(
        MapTile start,
        MapTile goal,
        out IReadOnlyList<MapTile> path,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (goal is null)
        {
            throw new ArgumentNullException(nameof(goal));
        }

        path = Array.Empty<MapTile>();
        if (!ContainsTile(start) || !ContainsTile(goal) || goal.MovementCost < 0)
        {
            return false;
        }

        if (ReferenceEquals(start, goal))
        {
            path = new[] { start };
            return true;
        }

        var open = new List<MapTile> { start };
        var cameFrom = new Dictionary<MapTile, MapTile>();
        var gScore = new Dictionary<MapTile, int>
        {
            [start] = 0
        };
        var fScore = new Dictionary<MapTile, int>
        {
            [start] = GetHeuristicDistance(start, goal, mode)
        };

        while (open.Count > 0)
        {
            MapTile current = open
                .OrderBy(tile => fScore.TryGetValue(tile, out int score) ? score : int.MaxValue)
                .ThenBy(tile => gScore.TryGetValue(tile, out int score) ? score : int.MaxValue)
                .First();
            if (ReferenceEquals(current, goal))
            {
                path = ReconstructPath(cameFrom, current);
                return true;
            }

            open.Remove(current);
            foreach (MapTile neighbor in GetNeighborTiles(current, mode))
            {
                if (neighbor.MovementCost < 0)
                {
                    continue;
                }

                int tentativeGScore = gScore[current] + neighbor.MovementCost;
                if (!gScore.TryGetValue(neighbor, out int existingGScore)
                    || tentativeGScore < existingGScore)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = tentativeGScore + GetHeuristicDistance(neighbor, goal, mode);
                    if (!open.Contains(neighbor))
                    {
                        open.Add(neighbor);
                    }
                }
            }
        }

        return false;
    }

    public IReadOnlyList<MapTile> GetReachableTiles(
        MapTile start,
        int movementBudget,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (movementBudget < 0 || !ContainsTile(start))
        {
            return Array.Empty<MapTile>();
        }

        var reachable = new List<MapTile>();
        var costs = new Dictionary<MapTile, int>
        {
            [start] = 0
        };
        var frontier = new List<MapTile> { start };

        while (frontier.Count > 0)
        {
            MapTile current = frontier
                .OrderBy(tile => costs[tile])
                .First();
            frontier.Remove(current);

            foreach (MapTile neighbor in GetNeighborTiles(current, mode))
            {
                if (neighbor.MovementCost < 0)
                {
                    continue;
                }

                int cost = costs[current] + neighbor.MovementCost;
                if (cost > movementBudget)
                {
                    continue;
                }

                if (!costs.TryGetValue(neighbor, out int existingCost) || cost < existingCost)
                {
                    costs[neighbor] = cost;
                    if (!ReferenceEquals(neighbor, start) && !reachable.Contains(neighbor))
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
            .ThenBy(tile => tile.GridZ)
            .ThenBy(tile => tile.GridX)
            .ToArray();
    }

    public static MapSection CreateHexGrid(
        int width,
        int depth,
        int tileSizeX,
        int tileSizeY,
        HexOrientation orientation)
    {
        ValidateGridDimensions(width, depth, tileSizeX, tileSizeY);

        var section = new MapSection
        {
            Width = width,
            Depth = depth
        };

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                HexCoord coordinate = new(x, z);
                (float tileX, float tileZ) = GetHexTileOrigin(coordinate, tileSizeX, tileSizeY, orientation);
                section.Tiles.Add(new MapHexTile
                {
                    GridX = x,
                    GridZ = z,
                    X = tileX,
                    Z = tileZ,
                    WorldX = tileX,
                    WorldZ = tileZ,
                    SizeX = tileSizeX,
                    SizeY = tileSizeY,
                    Orientation = orientation,
                    Coordinate = coordinate
                });
            }
        }

        return section;
    }

    internal MapSection CopyForPlacement(int offsetX, int offsetZ)
    {
        return new MapSection
        {
            Width = Width,
            Depth = Depth,
            Tiles = Tiles
                .Select(tile => tile.CopyForPlacement(offsetX, offsetZ))
                .ToList()
        };
    }

    private IReadOnlyList<MapTile> GetRectNeighborTiles(MapTile tile, TileNeighborMode mode)
    {
        (int X, int Z)[] offsets = mode switch
        {
            TileNeighborMode.Edges => RectEdgeOffsets,
            TileNeighborMode.EdgesAndCorners => RectEdgeAndCornerOffsets,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tile neighbor mode.")
        };

        var neighbors = new List<MapTile>();
        foreach ((int offsetX, int offsetZ) in offsets)
        {
            if (TryGetTileAtGridPosition(tile.GridX + offsetX, tile.GridZ + offsetZ, out MapTile? neighbor))
            {
                neighbors.Add(neighbor!);
            }
        }

        return neighbors;
    }

    private IReadOnlyList<MapTile> GetHexNeighborTiles(MapHexTile tile)
    {
        var neighbors = new List<MapTile>();
        foreach (HexCoord coordinate in tile.Coordinate.Neighbors())
        {
            if (TryGetHexTileAtCoordinate(coordinate, out MapHexTile? neighbor))
            {
                neighbors.Add(neighbor!);
            }
        }

        return neighbors;
    }

    private bool ContainsTile(MapTile tile)
    {
        return Tiles.Contains(tile);
    }

    private static IReadOnlyList<MapTile> ReconstructPath(Dictionary<MapTile, MapTile> cameFrom, MapTile current)
    {
        var path = new List<MapTile> { current };
        while (cameFrom.TryGetValue(current, out MapTile? previous))
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

    private static void ValidateGridDimensions(int width, int depth, int tileSizeX, int tileSizeY)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Section width must be positive.");
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Section depth must be positive.");
        }

        if (tileSizeX <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSizeX), "Tile size X must be positive.");
        }

        if (tileSizeY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSizeY), "Tile size Y must be positive.");
        }
    }

    private static (float X, float Z) GetHexTileOrigin(
        HexCoord coordinate,
        int tileSizeX,
        int tileSizeY,
        HexOrientation orientation)
    {
        return orientation switch
        {
            HexOrientation.PointyTop => (
                coordinate.Q * tileSizeX * 0.75f,
                (coordinate.R + (coordinate.Q * 0.5f)) * tileSizeY),
            HexOrientation.FlatTop => (
                (coordinate.Q + (coordinate.R * 0.5f)) * tileSizeX,
                coordinate.R * tileSizeY * 0.75f),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unsupported hex orientation.")
        };
    }
}
