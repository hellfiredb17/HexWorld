using System.Text.Json;
using System.Text.Json.Serialization;
using ClanIronSim.Entity;
using ClanIronSim.Events;
using ClanIronSim.Player;
using ClanIronSim.SimMath;
using ClanIronSim.World;

namespace ClanIronSim.WorldState;

public sealed class WorldState
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public WorldMap Map { get; set; } = new();

    public List<Entity.Entity> Entities { get; set; } = new();

    public List<InteractableEntity> Interactables { get; set; } = new();

    public PlayerService Players { get; set; } = new();

    public void Update(float deltaTime)
    {
        Update(deltaTime, NullSimEventDispatcher.Instance);
    }

    public void Update(float deltaTime, ISimEventDispatcher events)
    {
        foreach (InteractableEntity interactable in Interactables)
        {
            interactable.Update(deltaTime, events);
        }
    }

    public bool TryGetEntity(string id, out Entity.Entity? entity)
    {
        entity = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        entity = Entities.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return entity is not null;
    }

    public bool TryGetInteractable(string id, out InteractableEntity? interactable)
    {
        interactable = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        interactable = Interactables.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        return interactable is not null;
    }

    public bool TryGetNavigator(string id, out NavigatorEntity? navigator)
    {
        navigator = null;
        if (!TryGetInteractable(id, out InteractableEntity? interactable))
        {
            return false;
        }

        navigator = interactable as NavigatorEntity;
        return navigator is not null;
    }

    public IReadOnlyList<Entity.Entity> GetEntitiesAt(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        return Entities
            .Where(entity => LocationMatches(entity.Location, regionId, placedSectionId, tileGridX, tileGridZ))
            .ToArray();
    }

    public IReadOnlyList<Entity.Entity> GetEntitiesAt(
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return GetEntitiesAt(regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public IReadOnlyList<InteractableEntity> GetInteractablesAt(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        return Interactables
            .Where(entity => LocationMatches(entity.Location, regionId, placedSectionId, tileGridX, tileGridZ))
            .ToArray();
    }

    public IReadOnlyList<InteractableEntity> GetInteractablesAt(
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return GetInteractablesAt(regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public IReadOnlyList<Entity.Entity> GetOccupantsAt(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        return GetEntitiesAt(regionId, placedSectionId, tileGridX, tileGridZ)
            .Concat<Entity.Entity>(GetInteractablesAt(regionId, placedSectionId, tileGridX, tileGridZ))
            .ToArray();
    }

    public IReadOnlyList<Entity.Entity> GetOccupantsAt(
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return GetOccupantsAt(regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool IsTileOccupied(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        return GetOccupantsAt(regionId, placedSectionId, tileGridX, tileGridZ).Count > 0;
    }

    public bool IsTileOccupied(
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return IsTileOccupied(regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool IsTileBlocked(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (!TryResolveTile(regionId, placedSectionId, tileGridX, tileGridZ, out _, out _, out MapTile? tile))
        {
            return true;
        }

        return tile!.MovementCost < 0
            || IsTileOccupied(regionId, placedSectionId, tileGridX, tileGridZ);
    }

    public bool IsTileBlocked(
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return IsTileBlocked(regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool IsTileBlocked(MapLocation location)
    {
        return IsTileBlocked(
            location.RegionId,
            location.PlacedSectionId,
            location.TileGridX,
            location.TileGridZ);
    }

    public bool IsTileBlocked(PlacedMapTile placedTile)
    {
        if (placedTile.Section is null || placedTile.Tile is null)
        {
            return true;
        }

        return IsTileBlocked(
            GetRegionIdForSection(placedTile.Section),
            placedTile.Section.Id,
            placedTile.Tile.GridX,
            placedTile.Tile.GridZ);
    }

    public bool TryWorldToMapLocation(float worldX, float worldZ, out MapLocation location)
    {
        location = default;
        if (!Map.TryGetPlacedTileAtWorldPosition(worldX, worldZ, out PlacedMapTile placedTile))
        {
            return false;
        }

        string regionId = GetRegionIdForSection(placedTile.Section);
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return false;
        }

        location = new MapLocation(
            regionId,
            placedTile.Section.Id,
            placedTile.Section.GridPosition,
            placedTile.Tile.GridPosition,
            placedTile.Tile.WorldCenter);
        return true;
    }

    public bool TryWorldToMapLocation(SimVector2 worldPosition, out MapLocation location)
    {
        return TryWorldToMapLocation(worldPosition.X, worldPosition.Z, out location);
    }

    public bool TryCreateEntity(
        Entity.Entity entity,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (entity is null
            || string.IsNullOrWhiteSpace(entity.Id)
            || ContainsEntityId(entity.Id)
            || !TryCreateLocation(regionId, placedSectionId, tileGridX, tileGridZ, out MapLocation location))
        {
            return false;
        }

        entity.Location = location;
        Entities.Add(entity);
        return true;
    }

    public bool TryCreateEntity(
        Entity.Entity entity,
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return TryCreateEntity(entity, regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool TryCreateInteractable(
        InteractableEntity interactable,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (interactable is null
            || string.IsNullOrWhiteSpace(interactable.Id)
            || ContainsEntityId(interactable.Id)
            || !TryCreateLocation(regionId, placedSectionId, tileGridX, tileGridZ, out MapLocation location))
        {
            return false;
        }

        SetInteractableLocation(interactable, location);
        Interactables.Add(interactable);
        return true;
    }

    public bool TryCreateInteractable(
        InteractableEntity interactable,
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return TryCreateInteractable(interactable, regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool TryPlaceEntity(
        string entityId,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (!TryGetEntity(entityId, out Entity.Entity? entity)
            || !TryCreateLocation(regionId, placedSectionId, tileGridX, tileGridZ, out MapLocation location))
        {
            return false;
        }

        entity!.Location = location;
        return true;
    }

    public bool TryPlaceEntity(
        string entityId,
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return TryPlaceEntity(entityId, regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool TryPlaceInteractable(
        string interactableId,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (!TryGetInteractable(interactableId, out InteractableEntity? interactable)
            || !TryCreateLocation(regionId, placedSectionId, tileGridX, tileGridZ, out MapLocation location))
        {
            return false;
        }

        SetInteractableLocation(interactable!, location);
        return true;
    }

    public bool TryPlaceInteractable(
        string interactableId,
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return TryPlaceInteractable(interactableId, regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool TryMoveInteractable(
        string interactableId,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        if (!TryGetInteractable(interactableId, out InteractableEntity? interactable)
            || !interactable!.CanMove
            || !TryCreateLocation(regionId, placedSectionId, tileGridX, tileGridZ, out MapLocation location))
        {
            return false;
        }

        SetInteractableLocation(interactable, location);
        return true;
    }

    public bool TryMoveInteractable(
        string interactableId,
        string regionId,
        string placedSectionId,
        SimIntVector2 tileGrid)
    {
        return TryMoveInteractable(interactableId, regionId, placedSectionId, tileGrid.X, tileGrid.Z);
    }

    public bool HasDuplicateEntityIds()
    {
        return Entities
            .Select(entity => entity.Id)
            .Concat(Interactables.Select(interactable => interactable.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
    }

    public bool IsEntityLocationValid(Entity.Entity entity)
    {
        return entity is not null && IsLocationValid(entity.Location);
    }

    public bool IsInteractableLocationValid(InteractableEntity interactable)
    {
        return interactable is not null && IsLocationValid(interactable.Location);
    }

    public IReadOnlyList<InteractableEntity> GetInteractablesInRange(
        MapLocation origin,
        int range,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (range < 0 || !TryGetPlacedTile(origin, out PlacedMapTile start))
        {
            return Array.Empty<InteractableEntity>();
        }

        var locations = new HashSet<(string RegionId, string PlacedSectionId, int TileGridX, int TileGridZ)>(
            StringTupleComparer.Instance)
        {
            (origin.RegionId, origin.PlacedSectionId, origin.TileGridX, origin.TileGridZ)
        };
        foreach (PlacedMapTile reachable in Map.GetReachableTiles(start, range, mode))
        {
            locations.Add((
                GetRegionIdForSection(reachable.Section),
                reachable.Section.Id,
                reachable.Tile.GridX,
                reachable.Tile.GridZ));
        }

        return Interactables
            .Where(interactable => locations.Contains((
                interactable.Location.RegionId,
                interactable.Location.PlacedSectionId,
                interactable.Location.TileGridX,
                interactable.Location.TileGridZ)))
            .ToArray();
    }

    public IReadOnlyList<InteractableEntity> GetInteractablesInRange(
        string entityId,
        int range,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (!TryGetEntity(entityId, out Entity.Entity? entity))
        {
            return Array.Empty<InteractableEntity>();
        }

        return GetInteractablesInRange(entity!.Location, range, mode);
    }

    public bool CanInteract(
        string entityId,
        string interactableId,
        int range = 1,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (range < 0
            || !TryGetEntity(entityId, out Entity.Entity? entity)
            || !TryGetInteractable(interactableId, out InteractableEntity? interactable)
            || !IsEntityLocationValid(entity!)
            || !IsInteractableLocationValid(interactable!)
            || !IsInteractableTargetable(interactable!))
        {
            return false;
        }

        return GetInteractablesInRange(entity!.Location, range, mode)
            .Any(candidate => string.Equals(candidate.Id, interactableId, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryFindPath(
        PlacedMapTile start,
        PlacedMapTile goal,
        out IReadOnlyList<PlacedMapTile> path,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        path = Array.Empty<PlacedMapTile>();
        if (!IsPlacedTileInMap(start) || !IsPlacedTileInMap(goal) || IsTileBlocked(goal))
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
            foreach (PlacedMapTile neighbor in Map.GetNeighborTiles(current, mode))
            {
                if (IsTileBlocked(neighbor))
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
        PlacedMapTile start,
        int movementBudget,
        TileNeighborMode mode = TileNeighborMode.Edges)
    {
        if (movementBudget < 0 || !IsPlacedTileInMap(start))
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

            foreach (PlacedMapTile neighbor in Map.GetNeighborTiles(current, mode))
            {
                if (IsTileBlocked(neighbor))
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

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static WorldState FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("World state JSON cannot be empty.", nameof(json));
        }

        return JsonSerializer.Deserialize<WorldState>(json, JsonOptions)
            ?? throw new InvalidOperationException("World state JSON did not contain a state.");
    }

    public async Task SaveAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("World state path cannot be empty.", nameof(path));
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

    public static async Task<WorldState> LoadAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("World state path cannot be empty.", nameof(path));
        }

        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return FromJson(json);
    }

    private static bool LocationMatches(
        MapLocation location,
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ)
    {
        return string.Equals(location.RegionId, regionId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(location.PlacedSectionId, placedSectionId, StringComparison.OrdinalIgnoreCase)
            && location.TileGridX == tileGridX
            && location.TileGridZ == tileGridZ;
    }

    private bool TryCreateLocation(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ,
        out MapLocation location)
    {
        location = default;
        if (string.IsNullOrWhiteSpace(regionId) || string.IsNullOrWhiteSpace(placedSectionId))
        {
            return false;
        }

        if (!TryResolveTile(regionId, placedSectionId, tileGridX, tileGridZ, out MapRegion? region, out PlacedMapSection? section, out MapTile? tile))
        {
            return false;
        }

        location = new MapLocation(
            region!.Id,
            section!.Id,
            section.GridPosition,
            tile!.GridPosition,
            tile.WorldCenter);
        return true;
    }

    private bool TryResolveTile(
        string regionId,
        string placedSectionId,
        int tileGridX,
        int tileGridZ,
        out MapRegion? region,
        out PlacedMapSection? section,
        out MapTile? tile)
    {
        region = null;
        section = null;
        tile = null;

        if (string.IsNullOrWhiteSpace(regionId) || string.IsNullOrWhiteSpace(placedSectionId))
        {
            return false;
        }

        region = Map.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, regionId, StringComparison.OrdinalIgnoreCase));
        if (region is null)
        {
            return false;
        }

        section = region.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, placedSectionId, StringComparison.OrdinalIgnoreCase));
        if (section is null || !section.Section.TryGetTileAtGridPosition(tileGridX, tileGridZ, out tile))
        {
            tile = null;
            return false;
        }

        return tile is not null;
    }

    private bool IsLocationValid(MapLocation location)
    {
        return TryCreateLocation(
            location.RegionId,
            location.PlacedSectionId,
            location.TileGridX,
            location.TileGridZ,
            out MapLocation resolved)
            && resolved.SectionGridX == location.SectionGridX
            && resolved.SectionGridZ == location.SectionGridZ
            && System.Math.Abs(resolved.WorldX - location.WorldX) < PositionTolerance
            && System.Math.Abs(resolved.WorldZ - location.WorldZ) < PositionTolerance;
    }

    private bool ContainsEntityId(string id)
    {
        return Entities.Any(entity => string.Equals(entity.Id, id, StringComparison.OrdinalIgnoreCase))
            || Interactables.Any(interactable => string.Equals(interactable.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetInteractableLocation(InteractableEntity interactable, MapLocation location)
    {
        interactable.Location = location;
        if (interactable is NavigatorEntity navigator)
        {
            navigator.CurrentWorldPosition = location.WorldPosition;
        }
    }

    private bool IsPlacedTileInMap(PlacedMapTile placedTile)
    {
        return placedTile.Section is not null
            && placedTile.Tile is not null
            && Map.Regions.Any(region =>
                region.Sections.Contains(placedTile.Section)
                && placedTile.Section.Section.Tiles.Contains(placedTile.Tile));
    }

    private string GetRegionIdForSection(PlacedMapSection section)
    {
        return Map.Regions.FirstOrDefault(region => region.Sections.Contains(section))?.Id ?? string.Empty;
    }

    private bool TryGetPlacedTile(MapLocation location, out PlacedMapTile placedTile)
    {
        placedTile = default;
        if (!TryResolveTile(
                location.RegionId,
                location.PlacedSectionId,
                location.TileGridX,
                location.TileGridZ,
                out _,
                out PlacedMapSection? section,
                out MapTile? tile)
            || section is null
            || tile is null)
        {
            return false;
        }

        placedTile = new PlacedMapTile(section, tile);
        return true;
    }

    private static bool IsInteractableTargetable(InteractableEntity interactable)
    {
        return interactable.TryGetBooleanAttribute("interaction", "usable", out bool usable) && usable
            || interactable.TryGetBooleanAttribute("targeting", "targetable", out bool targetable) && targetable;
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

    private sealed class StringTupleComparer : IEqualityComparer<(string RegionId, string PlacedSectionId, int TileGridX, int TileGridZ)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals(
            (string RegionId, string PlacedSectionId, int TileGridX, int TileGridZ) x,
            (string RegionId, string PlacedSectionId, int TileGridX, int TileGridZ) y)
        {
            return string.Equals(x.RegionId, y.RegionId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.PlacedSectionId, y.PlacedSectionId, StringComparison.OrdinalIgnoreCase)
                && x.TileGridX == y.TileGridX
                && x.TileGridZ == y.TileGridZ;
        }

        public int GetHashCode((string RegionId, string PlacedSectionId, int TileGridX, int TileGridZ) obj)
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RegionId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PlacedSectionId ?? string.Empty);
                hash = (hash * 397) ^ obj.TileGridX;
                hash = (hash * 397) ^ obj.TileGridZ;
                return hash;
            }
        }
    }

    private const float PositionTolerance = 0.001f;
}
