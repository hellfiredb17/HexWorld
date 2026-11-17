using ClanIronSim.Entity;
using ClanIronSim.HawkNet;
using ClanIronSim.World;
using HawkNet.Core.Server;
using HawkNet.CoreUx.Client.World;
using HawkNet.CoreUx.Server.Services;
using HawkNet.Game.GameState.Match;

namespace ClanIronSim.CoreUx.Server;

public sealed class ClanIronServerWorldMonitorProvider : IServerWorldMonitorProvider
{
    private readonly Dictionary<string, IReadOnlyList<WorldTileSnapshot>> _topology = new(StringComparer.OrdinalIgnoreCase);
    private long _revision;

    public IReadOnlyList<WorldRegionDescriptor> GetRegions(GameHost host) => GetMatch(host)?.Overlord?.World.Map.Regions
        .Select(region => new WorldRegionDescriptor(region.Id, string.IsNullOrWhiteSpace(region.DisplayName) ? region.Id : region.DisplayName))
        .ToArray() ?? Array.Empty<WorldRegionDescriptor>();

    public WorldSnapshot? Capture(GameHost host, string? regionId = null)
    {
        ClanIronSimMatch? match = GetMatch(host);
        MapRegion? region = match?.Overlord?.World.Map.Regions.FirstOrDefault(candidate =>
            string.IsNullOrWhiteSpace(regionId) || string.Equals(candidate.Id, regionId, StringComparison.OrdinalIgnoreCase));
        if (match?.Overlord is null || region is null) return null;

        if (!_topology.TryGetValue(region.Id, out IReadOnlyList<WorldTileSnapshot>? tiles))
        {
            tiles = region.Sections.SelectMany(section => section.Section.Tiles.Select(tile => CreateTile(region, section, tile))).ToArray();
            _topology[region.Id] = tiles;
        }

        return new WorldSnapshot
        {
            RegionId = region.Id,
            RegionName = string.IsNullOrWhiteSpace(region.DisplayName) ? region.Id : region.DisplayName,
            TopologyRevision = 1,
            EntityRevision = Interlocked.Increment(ref _revision),
            Tiles = tiles,
            Entities = match.Overlord.World.Interactables
                .Where(entity => string.Equals(entity.Location.RegionId, region.Id, StringComparison.OrdinalIgnoreCase))
                .Select(CreateEntity).ToArray()
        };
    }

    private static ClanIronSimMatch? GetMatch(GameHost host) =>
        (host.ActiveGameState as MatchState)?.Match as ClanIronSimMatch;

    private static WorldTileSnapshot CreateTile(MapRegion region, PlacedMapSection section, MapTile tile) => new()
    {
        Id = $"{section.Id}:{tile.GridX}:{tile.GridZ}", RegionId = region.Id, PlacedSectionId = section.Id,
        GridX = tile.GridX, GridZ = tile.GridZ, WorldX = tile.WorldX, WorldZ = tile.WorldZ,
        SizeX = tile.SizeX, SizeZ = tile.SizeY, CenterX = tile.WorldCenterX, CenterZ = tile.WorldCenterZ,
        Shape = tile.Shape == MapTileShape.Hex ? WorldTileShape.Hex : WorldTileShape.Rectangle,
        HexOrientation = tile is MapHexTile { Orientation: HexOrientation.FlatTop } ? WorldHexOrientation.FlatTop : WorldHexOrientation.PointyTop,
        MovementCost = tile.MovementCost, IsBlocked = tile.MovementCost < 0,
        IsSpawner = tile.Attributes.Any(attribute => string.Equals(attribute.Id, "player spawner", StringComparison.OrdinalIgnoreCase)
            && string.Equals(attribute.ModifierKey, "spawner", StringComparison.OrdinalIgnoreCase))
    };

    private static WorldEntitySnapshot CreateEntity(InteractableEntity entity)
    {
        NavigatorEntity? navigator = entity as NavigatorEntity;
        var destination = navigator is { PathState.Waypoints.Count: > 0 } ? navigator.PathState.Waypoints[^1].WorldPosition : navigator?.CurrentWorldPosition;
        return new WorldEntitySnapshot
        {
            Id = entity.Id, EntityType = entity.GetType().Name, PlayerId = ParsePlayerId(entity.Id),
            WorldX = navigator?.CurrentWorldPosition.X ?? entity.Location.WorldX,
            WorldZ = navigator?.CurrentWorldPosition.Z ?? entity.Location.WorldZ,
            DestinationWorldX = destination?.X ?? entity.Location.WorldX, DestinationWorldZ = destination?.Z ?? entity.Location.WorldZ,
            VelocityX = navigator?.Velocity.X ?? 0, VelocityZ = navigator?.Velocity.Z ?? 0,
            FaceForwardX = navigator?.FaceForward.X ?? 0, FaceForwardZ = navigator?.FaceForward.Z ?? 0,
            BaseForwardX = navigator?.BaseForward.X ?? 0, BaseForwardZ = navigator?.BaseForward.Z ?? 0,
            RotationVelocity = navigator?.RotationVelocity ?? 0, MaxSpeed = navigator?.MaxSpeed ?? 0,
            MaxRotationSpeed = navigator?.MaxRotationSpeed ?? 0, CurrentPathIndex = navigator?.PathState.CurrentWaypointIndex ?? 0,
            PathWorldPositions = navigator?.PathState.Waypoints.Select(point => new WorldPoint(point.WorldX, point.WorldZ)).ToArray() ?? Array.Empty<WorldPoint>(),
            ServerUpdatedAtUtc = DateTime.UtcNow, MovementStatus = navigator?.PathState.Status.ToString() ?? string.Empty, CanMove = entity.CanMove
        };
    }

    private static int? ParsePlayerId(string id)
    {
        const string prefix = "player_", suffix = "_nav";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(id[prefix.Length..^suffix.Length], out int value) ? value : null;
    }
}
