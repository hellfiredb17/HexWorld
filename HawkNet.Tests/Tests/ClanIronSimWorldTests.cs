using System.Text.Json;
using ClanIronSim;
using ClanIronSim.Commands;
using ClanIronSim.Entity;
using ClanIronSim.Entity.Movement;
using ClanIronSim.Events;
using ClanIronSim.Player;
using ClanIronSim.SimMath;
using ClanIronSim.World;
using ClanIronSim.WorldState;
using Xunit;

namespace HawkNet.Tests
{
    public class ClanIronSimWorldTests
    {
        [Fact]
        public void CreateGrid_CreatesExpectedTileCountAndCoordinates()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 2, tileSizeX: 10, tileSizeY: 20);

            Assert.Equal(3, section.Width);
            Assert.Equal(2, section.Depth);
            Assert.Equal(6, section.Tiles.Count);
            Assert.All(section.Tiles, tile => Assert.IsType<MapRectTile>(tile));
            Assert.Contains(section.Tiles, tile => tile.Shape == MapTileShape.Rectangle && tile.GridX == 0 && tile.GridZ == 0 && tile.X == 0 && tile.Z == 0 && tile.SizeX == 10 && tile.SizeY == 20);
            Assert.Contains(section.Tiles, tile => tile.Shape == MapTileShape.Rectangle && tile.GridX == 2 && tile.GridZ == 1 && tile.X == 20 && tile.Z == 20 && tile.SizeX == 10 && tile.SizeY == 20);
            Assert.Equal(new MapBounds(0, 0, 30, 40), section.Bounds);
        }

        [Fact]
        public void CreateGrid_RejectsInvalidDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateGrid(0, 1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateGrid(1, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateGrid(1, 1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateGrid(1, 1, 1, 0));
        }

        [Fact]
        public void CreateHexGrid_CreatesPointyTopTilesWithAxialCoordinatesAndWorldPositions()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 2,
                depth: 2,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);

            Assert.Equal(2, section.Width);
            Assert.Equal(2, section.Depth);
            Assert.Equal(4, section.Tiles.Count);
            Assert.All(section.Tiles, tile =>
            {
                var hexTile = Assert.IsType<MapHexTile>(tile);
                Assert.Equal(MapTileShape.Hex, hexTile.Shape);
                Assert.Equal(HexOrientation.PointyTop, hexTile.Orientation);
                Assert.Equal(10, hexTile.SizeX);
                Assert.Equal(20, hexTile.SizeY);
            });
            AssertHexTile(section.Tiles[0], gridX: 0, gridZ: 0, coordinate: new HexCoord(0, 0), x: 0f, z: 0f);
            AssertHexTile(section.Tiles[1], gridX: 1, gridZ: 0, coordinate: new HexCoord(1, 0), x: 7.5f, z: 10f);
            AssertHexTile(section.Tiles[2], gridX: 0, gridZ: 1, coordinate: new HexCoord(0, 1), x: 0f, z: 20f);
            AssertHexTile(section.Tiles[3], gridX: 1, gridZ: 1, coordinate: new HexCoord(1, 1), x: 7.5f, z: 30f);
            Assert.Equal(new MapBounds(0, 0, 17.5f, 50), section.Bounds);
        }

        [Fact]
        public void CreateHexGrid_CreatesFlatTopTilesWithAxialCoordinatesAndWorldPositions()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 2,
                depth: 2,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.FlatTop);

            Assert.Equal(4, section.Tiles.Count);
            Assert.All(section.Tiles, tile =>
            {
                var hexTile = Assert.IsType<MapHexTile>(tile);
                Assert.Equal(MapTileShape.Hex, hexTile.Shape);
                Assert.Equal(HexOrientation.FlatTop, hexTile.Orientation);
            });
            AssertHexTile(section.Tiles[0], gridX: 0, gridZ: 0, coordinate: new HexCoord(0, 0), x: 0f, z: 0f);
            AssertHexTile(section.Tiles[1], gridX: 1, gridZ: 0, coordinate: new HexCoord(1, 0), x: 10f, z: 0f);
            AssertHexTile(section.Tiles[2], gridX: 0, gridZ: 1, coordinate: new HexCoord(0, 1), x: 5f, z: 15f);
            AssertHexTile(section.Tiles[3], gridX: 1, gridZ: 1, coordinate: new HexCoord(1, 1), x: 15f, z: 15f);
            Assert.Equal(new MapBounds(0, 0, 25, 35), section.Bounds);
        }

        [Fact]
        public void CreateHexGrid_RejectsInvalidDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateHexGrid(0, 1, 1, 1, HexOrientation.PointyTop));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateHexGrid(1, 0, 1, 1, HexOrientation.PointyTop));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateHexGrid(1, 1, 0, 1, HexOrientation.PointyTop));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapSection.CreateHexGrid(1, 1, 1, 0, HexOrientation.PointyTop));
        }

        [Fact]
        public void TryGetTileAtGridPosition_FindsTilesBySectionGrid()
        {
            MapSection section = MapSection.CreateGrid(width: 2, depth: 2, tileSizeX: 10, tileSizeY: 10);

            Assert.True(section.TryGetTileAtGridPosition(1, 1, out MapTile? tile));
            Assert.NotNull(tile);
            Assert.Equal(1, tile.GridX);
            Assert.Equal(1, tile.GridZ);
            Assert.False(section.TryGetTileAtGridPosition(2, 1, out MapTile? missing));
            Assert.Null(missing);
        }

        [Fact]
        public void GetNeighborTiles_ReturnsRectangleEdgeNeighborsInStableOrder()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 3, tileSizeX: 10, tileSizeY: 10);

            IReadOnlyList<MapTile> centerNeighbors = section.GetNeighborTiles(
                gridX: 1,
                gridZ: 1,
                mode: TileNeighborMode.Edges);
            IReadOnlyList<MapTile> cornerNeighbors = section.GetNeighborTiles(
                gridX: 0,
                gridZ: 0,
                mode: TileNeighborMode.Edges);

            AssertGridPositions(
                centerNeighbors,
                (1, 0),
                (2, 1),
                (1, 2),
                (0, 1));
            AssertGridPositions(
                cornerNeighbors,
                (1, 0),
                (0, 1));
        }

        [Fact]
        public void GetNeighborTiles_ReturnsRectangleEdgeAndCornerNeighborsInStableOrder()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 3, tileSizeX: 10, tileSizeY: 10);

            IReadOnlyList<MapTile> neighbors = section.GetNeighborTiles(
                gridX: 1,
                gridZ: 1,
                mode: TileNeighborMode.EdgesAndCorners);

            AssertGridPositions(
                neighbors,
                (1, 0),
                (2, 0),
                (2, 1),
                (2, 2),
                (1, 2),
                (0, 2),
                (0, 1),
                (0, 0));
        }

        [Fact]
        public void GetNeighborTiles_ReturnsEmptyForMissingGridPosition()
        {
            MapSection section = MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10);

            IReadOnlyList<MapTile> neighbors = section.GetNeighborTiles(gridX: 9, gridZ: 9);

            Assert.Empty(neighbors);
        }

        [Fact]
        public void TryGetHexTileAtCoordinate_FindsHexTilesByAxialCoordinate()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 2,
                depth: 2,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);

            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(1, 1), out MapHexTile? tile));
            Assert.NotNull(tile);
            Assert.Equal(1, tile.GridX);
            Assert.Equal(1, tile.GridZ);
            Assert.False(section.TryGetHexTileAtCoordinate(new HexCoord(-1, 0), out MapHexTile? missing));
            Assert.Null(missing);
        }

        [Fact]
        public void GetNeighborTiles_ReturnsHexAxialNeighborsInStableOrder()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 3,
                depth: 3,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(1, 1), out MapHexTile? tile));

            IReadOnlyList<MapTile> neighbors = section.GetNeighborTiles(tile!, TileNeighborMode.EdgesAndCorners);

            AssertHexCoordinates(
                neighbors,
                new HexCoord(2, 1),
                new HexCoord(2, 0),
                new HexCoord(1, 0),
                new HexCoord(0, 1),
                new HexCoord(0, 2),
                new HexCoord(1, 2));
        }

        [Fact]
        public void MovementCost_CombinesMovementAttributesAndClampsToOne()
        {
            var tile = new MapRectTile();

            Assert.Equal(1, tile.MovementCost);

            tile.AddAttribute(new MapAttribute { Id = "mud", ModifierKey = "movement", Amount = 2 });
            tile.AddAttribute(new MapAttribute { Id = "road", ModifierKey = "movement", Amount = -4 });
            tile.AddAttribute(new MapAttribute { Id = "fire", ModifierKey = "hp", Amount = 10 });

            Assert.Equal(1, tile.MovementCost);

            tile.AddAttribute(new MapAttribute { Id = "hill", ModifierKey = "MOVEMENT", Amount = 3 });

            Assert.Equal(2, tile.MovementCost);
        }

        [Fact]
        public void MovementCost_ReturnsNegativeOneWhenTileHasBlockingAttribute()
        {
            var tile = new MapRectTile();

            tile.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });
            tile.AddAttribute(new MapAttribute { Id = "road", ModifierKey = "movement", Amount = -10 });
            tile.AddAttribute(new MapAttribute { Id = "mud", ModifierKey = "MOVEMENT", Amount = 100 });

            Assert.Equal(-1, tile.MovementCost);
        }

        [Fact]
        public void TryFindPath_ReturnsRectangleEdgePathUsingMovementCosts()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 3, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 1, out MapTile? expensive));
            expensive!.AddAttribute(new MapAttribute { Id = "mud", ModifierKey = "movement", Amount = 10 });
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(0, 2, out MapTile? goal));

            bool found = section.TryFindPath(start!, goal!, out IReadOnlyList<MapTile> path);

            Assert.True(found);
            AssertGridPositions(
                path,
                (0, 0),
                (1, 0),
                (1, 1),
                (1, 2),
                (0, 2));
        }

        [Fact]
        public void TryFindPath_RoutesAroundBlockedRectangleTiles()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 3, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 1, out MapTile? blocked));
            blocked!.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 99 });
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(0, 2, out MapTile? goal));

            bool found = section.TryFindPath(start!, goal!, out IReadOnlyList<MapTile> path);

            Assert.True(found);
            Assert.DoesNotContain(blocked, path);
            AssertGridPositions(
                path,
                (0, 0),
                (1, 0),
                (1, 1),
                (1, 2),
                (0, 2));
        }

        [Fact]
        public void TryFindPath_ReturnsFalseWhenGoalIsBlocked()
        {
            MapSection section = MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(1, 0, out MapTile? goal));
            goal!.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            bool found = section.TryFindPath(start!, goal, out IReadOnlyList<MapTile> path);

            Assert.False(found);
            Assert.Empty(path);
        }

        [Fact]
        public void TryFindPath_UsesRectangleCornerNeighborsWhenRequested()
        {
            MapSection section = MapSection.CreateGrid(width: 2, depth: 2, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(1, 1, out MapTile? goal));

            bool found = section.TryFindPath(
                start!,
                goal!,
                out IReadOnlyList<MapTile> path,
                TileNeighborMode.EdgesAndCorners);

            Assert.True(found);
            AssertGridPositions(path, (0, 0), (1, 1));
        }

        [Fact]
        public void TryFindPath_ReturnsHexPathUsingAxialHeuristic()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 3,
                depth: 3,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(0, 0), out MapHexTile? start));
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(2, 1), out MapHexTile? goal));

            bool found = section.TryFindPath(start!, goal!, out IReadOnlyList<MapTile> path);

            Assert.True(found);
            AssertHexCoordinates(
                path,
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new HexCoord(2, 0),
                new HexCoord(2, 1));
        }

        [Fact]
        public void TryFindPath_SkipsBlockedHexTiles()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 3,
                depth: 3,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(0, 0), out MapHexTile? start));
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(2, 1), out MapHexTile? goal));
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(1, 0), out MapHexTile? blocked));
            blocked!.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            bool found = section.TryFindPath(start!, goal!, out IReadOnlyList<MapTile> path);

            Assert.True(found);
            Assert.DoesNotContain(blocked, path);
            AssertHexCoordinates(
                path,
                new HexCoord(0, 0),
                new HexCoord(0, 1),
                new HexCoord(1, 1),
                new HexCoord(2, 1));
        }

        [Fact]
        public void TryFindPath_ReturnsFalseWhenTilesAreOutsideSection()
        {
            MapSection section = MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10);
            MapTile start = Assert.Single(section.Tiles);
            var outside = new MapRectTile { GridX = 9, GridZ = 9 };

            bool found = section.TryFindPath(start, outside, out IReadOnlyList<MapTile> path);

            Assert.False(found);
            Assert.Empty(path);
        }

        [Fact]
        public void GetReachableTiles_ReturnsTilesWithinMovementBudget()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 1, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(1, 0, out MapTile? middle));
            Assert.True(section.TryGetTileAtGridPosition(2, 0, out MapTile? end));
            middle!.AddAttribute(new MapAttribute { Id = "mud", ModifierKey = "movement", Amount = 1 });

            IReadOnlyList<MapTile> budgetOne = section.GetReachableTiles(start!, movementBudget: 1);
            IReadOnlyList<MapTile> budgetThree = section.GetReachableTiles(start!, movementBudget: 3);

            Assert.Empty(budgetOne);
            AssertGridPositions(budgetThree, (1, 0), (2, 0));
            Assert.DoesNotContain(start!, budgetThree);
            Assert.Equal(2, middle.MovementCost);
            Assert.Equal(1, end!.MovementCost);
        }

        [Fact]
        public void GetReachableTiles_ExcludesBlockedTiles()
        {
            MapSection section = MapSection.CreateGrid(width: 3, depth: 1, tileSizeX: 10, tileSizeY: 10);
            Assert.True(section.TryGetTileAtGridPosition(0, 0, out MapTile? start));
            Assert.True(section.TryGetTileAtGridPosition(1, 0, out MapTile? blocked));
            blocked!.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            IReadOnlyList<MapTile> reachable = section.GetReachableTiles(start!, movementBudget: 3);

            Assert.Empty(reachable);
        }

        [Fact]
        public void GetReachableTiles_UsesHexNeighbors()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 3,
                depth: 3,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.FlatTop);
            Assert.True(section.TryGetHexTileAtCoordinate(new HexCoord(1, 1), out MapHexTile? start));

            IReadOnlyList<MapTile> reachable = section.GetReachableTiles(start!, movementBudget: 1);

            AssertHexCoordinates(
                reachable,
                new HexCoord(1, 0),
                new HexCoord(2, 0),
                new HexCoord(0, 1),
                new HexCoord(2, 1),
                new HexCoord(0, 2),
                new HexCoord(1, 2));
        }

        [Fact]
        public void HexCoord_ProvidesAxialCubeHelpersAndNeighbors()
        {
            var coord = new HexCoord(2, -1);

            Assert.Equal(2, coord.Q);
            Assert.Equal(-1, coord.R);
            Assert.Equal(-1, coord.S);
            Assert.Equal(new HexCoord(3, -1), coord.Neighbor(0));
            Assert.Equal(new HexCoord(3, -2), coord.Neighbor(1));
            Assert.Equal(new HexCoord(2, -2), coord.Neighbor(2));
            Assert.Equal(new HexCoord(1, -1), coord.Neighbor(3));
            Assert.Equal(new HexCoord(1, 0), coord.Neighbor(4));
            Assert.Equal(new HexCoord(2, 0), coord.Neighbor(5));
            Assert.Equal(
                [
                    new HexCoord(3, -1),
                    new HexCoord(3, -2),
                    new HexCoord(2, -2),
                    new HexCoord(1, -1),
                    new HexCoord(1, 0),
                    new HexCoord(2, 0)
                ],
                coord.Neighbors());
            Assert.Equal(4, coord.DistanceTo(new HexCoord(-1, 3)));
            Assert.Equal(new HexCoord(2, -1), coord);
            Assert.True(coord == new HexCoord(2, -1));
            Assert.True(coord != new HexCoord(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => coord.Neighbor(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => coord.Neighbor(6));
        }

        [Fact]
        public void AddSection_StoresPlacementAndCreatesPlacedTileWorldCoordinates()
        {
            MapSection section = MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 5, tileSizeY: 6);
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };

            region.AddSection(section, offsetX: 100, offsetZ: 200);

            PlacedMapSection placed = Assert.Single(region.Sections);
            Assert.Equal(string.Empty, placed.Id);
            Assert.Equal(0, placed.GridX);
            Assert.Equal(0, placed.GridZ);
            Assert.Equal(100, placed.OffsetX);
            Assert.Equal(200, placed.OffsetZ);
            Assert.Equal(new MapBounds(100, 200, 105, 206), region.Bounds);
            MapTile tile = Assert.Single(placed.Section.Tiles);
            Assert.Equal(0, tile.X);
            Assert.Equal(0, tile.Z);
            Assert.Equal(100, tile.WorldX);
            Assert.Equal(200, tile.WorldZ);
            Assert.Equal(102.5f, tile.WorldCenterX);
            Assert.Equal(203f, tile.WorldCenterZ);
            Assert.Equal(0, Assert.Single(section.Tiles).WorldX);
        }

        [Fact]
        public void AddSection_StoresStablePlacedSectionIdentityAndSectionGridCoordinates()
        {
            MapSection section = MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 5, tileSizeY: 6);
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };

            region.AddSection(
                section,
                placedSectionId: "forest-2-3",
                gridX: 2,
                gridZ: 3,
                offsetX: 100,
                offsetZ: 200);

            PlacedMapSection placed = Assert.Single(region.Sections);
            Assert.Equal("forest-2-3", placed.Id);
            Assert.Equal(2, placed.GridX);
            Assert.Equal(3, placed.GridZ);
            Assert.Equal(100, placed.OffsetX);
            Assert.Equal(200, placed.OffsetZ);
            Assert.Equal(102.5f, Assert.Single(placed.Section.Tiles).WorldCenterX);
        }

        [Fact]
        public void AddSection_PreservesHexGridPlacementWithFractionalLocalOrigins()
        {
            MapSection section = MapSection.CreateHexGrid(
                width: 2,
                depth: 1,
                tileSizeX: 10,
                tileSizeY: 20,
                orientation: HexOrientation.PointyTop);
            var region = new MapRegion
            {
                Id = "hex",
                DisplayName = "Hex"
            };

            region.AddSection(section, offsetX: 100, offsetZ: 200);

            MapHexTile tile = Assert.IsType<MapHexTile>(region.Sections.Single().Section.Tiles[1]);
            Assert.Equal(7.5f, tile.X);
            Assert.Equal(10f, tile.Z);
            Assert.Equal(107.5f, tile.WorldX);
            Assert.Equal(210f, tile.WorldZ);
            Assert.Equal(112.5f, tile.WorldCenterX);
            Assert.Equal(220f, tile.WorldCenterZ);
            Assert.Equal(new HexCoord(1, 0), tile.Coordinate);
            Assert.Equal(HexOrientation.PointyTop, tile.Orientation);
        }

        [Fact]
        public void MapLocation_StoresSectionTileAndCachedWorldCoordinates()
        {
            var location = new MapLocation(
                regionId: "forest",
                placedSectionId: "forest-2-3",
                sectionGridX: 2,
                sectionGridZ: 3,
                tileGridX: 4,
                tileGridZ: 5,
                worldX: 42.5f,
                worldZ: 77.25f);

            Assert.Equal("forest", location.RegionId);
            Assert.Equal("forest-2-3", location.PlacedSectionId);
            Assert.Equal(2, location.SectionGridX);
            Assert.Equal(3, location.SectionGridZ);
            Assert.Equal(4, location.TileGridX);
            Assert.Equal(5, location.TileGridZ);
            Assert.Equal(42.5f, location.WorldX);
            Assert.Equal(77.25f, location.WorldZ);
            Assert.Equal(new SimIntVector2(2, 3), location.SectionGrid);
            Assert.Equal(new SimIntVector2(4, 5), location.TileGrid);
            Assert.Equal(new SimVector2(42.5f, 77.25f), location.WorldPosition);
        }

        [Fact]
        public void MapLocation_CanBeCreatedFromSimVectors()
        {
            var location = new MapLocation(
                regionId: "forest",
                placedSectionId: "forest-2-3",
                sectionGrid: new SimIntVector2(2, 3),
                tileGrid: new SimIntVector2(4, 5),
                worldPosition: new SimVector2(42.5f, 77.25f));

            Assert.Equal(2, location.SectionGridX);
            Assert.Equal(3, location.SectionGridZ);
            Assert.Equal(4, location.TileGridX);
            Assert.Equal(5, location.TileGridZ);
            Assert.Equal(42.5f, location.WorldX);
            Assert.Equal(77.25f, location.WorldZ);
        }

        [Fact]
        public void Entity_RoundTripsWithWorldUniqueIdAndLocation()
        {
            var entity = new Entity
            {
                Id = "entity-world-001",
                Location = new MapLocation(
                    regionId: "forest",
                    placedSectionId: "forest-2-3",
                    sectionGridX: 2,
                    sectionGridZ: 3,
                    tileGridX: 4,
                    tileGridZ: 5,
                    worldX: 42.5f,
                    worldZ: 77.25f)
            };

            string json = JsonSerializer.Serialize(entity);
            Entity loaded = JsonSerializer.Deserialize<Entity>(json)!;

            Assert.Equal("entity-world-001", loaded.Id);
            Assert.Equal("forest", loaded.Location.RegionId);
            Assert.Equal("forest-2-3", loaded.Location.PlacedSectionId);
            Assert.Equal(2, loaded.Location.SectionGridX);
            Assert.Equal(3, loaded.Location.SectionGridZ);
            Assert.Equal(4, loaded.Location.TileGridX);
            Assert.Equal(5, loaded.Location.TileGridZ);
            Assert.Equal(42.5f, loaded.Location.WorldX);
            Assert.Equal(77.25f, loaded.Location.WorldZ);
        }

        [Fact]
        public void InteractableEntity_RoundTripsAttributesAndTypedValues()
        {
            var entity = new InteractableEntity
            {
                Id = "entity-rubble-001",
                Location = new MapLocation(
                    regionId: "forest",
                    placedSectionId: "forest-2-3",
                    sectionGridX: 2,
                    sectionGridZ: 3,
                    tileGridX: 4,
                    tileGridZ: 5,
                    worldX: 42.5f,
                    worldZ: 77.25f),
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "hp",
                        Subtype = "current",
                        ValueKind = EntityAttributeValueKind.Number,
                        Value = "12"
                    },
                    new EntityAttribute
                    {
                        Key = "targeting",
                        Subtype = "targetable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    },
                    new EntityAttribute
                    {
                        Key = "rubble",
                        Subtype = "movement",
                        ValueKind = EntityAttributeValueKind.Number,
                        Value = "-1"
                    },
                    new EntityAttribute
                    {
                        Key = "display-name",
                        Subtype = "label",
                        ValueKind = EntityAttributeValueKind.Text,
                        Value = "Old Rubble"
                    }
                ]
            };

            string json = JsonSerializer.Serialize(entity);
            InteractableEntity loaded = JsonSerializer.Deserialize<InteractableEntity>(json)!;

            Assert.Equal("entity-rubble-001", loaded.Id);
            Assert.Equal("forest", loaded.Location.RegionId);
            Assert.Equal(4, loaded.Location.TileGridX);
            Assert.Equal(4, loaded.Attributes.Count);
            Assert.Contains("\"ValueKind\":\"Number\"", json);
            Assert.Contains("\"ValueKind\":\"Boolean\"", json);

            EntityAttribute hp = loaded.Attributes.Single(attribute => attribute.Key == "hp");
            Assert.True(hp.TryGetNumber(out int hpValue));
            Assert.Equal(12, hpValue);

            EntityAttribute targetable = loaded.Attributes.Single(attribute => attribute.Key == "targeting");
            Assert.Equal("targetable", targetable.Subtype);
            Assert.True(targetable.TryGetBoolean(out bool targetableValue));
            Assert.True(targetableValue);

            EntityAttribute rubble = loaded.Attributes.Single(attribute => attribute.Key == "rubble");
            Assert.True(rubble.TryGetNumber(out int movementValue));
            Assert.Equal(-1, movementValue);

            EntityAttribute displayName = loaded.Attributes.Single(attribute => attribute.Key == "display-name");
            Assert.True(displayName.TryGetText(out string textValue));
            Assert.Equal("Old Rubble", textValue);
        }

        [Fact]
        public void SimVector2_StoresValuesAndSupportsMathHelpers()
        {
            var vector = new SimVector2(3, 4);

            Assert.Equal(3, vector.X);
            Assert.Equal(4, vector.Z);
            Assert.Equal(5, vector.Length);
            Assert.Equal(0, SimVector2.Zero.X);
            Assert.Equal(0, SimVector2.Zero.Z);
            Assert.Equal(0, SimVector2.Forward.X);
            Assert.Equal(1, SimVector2.Forward.Z);

            SimVector2 normalized = vector.Normalize();
            Assert.Equal(0.6f, normalized.X, precision: 3);
            Assert.Equal(0.8f, normalized.Z, precision: 3);
            Assert.Equal(11, vector.Dot(new SimVector2(1, 2)));
            Assert.Equal(2, vector.Cross(new SimVector2(1, 2)));
            Assert.Equal(5, vector.DistanceTo(SimVector2.Zero));
            Assert.Equal(90, SimVector2.Forward.AngleToDegrees(new SimVector2(1, 0)), precision: 3);
            Assert.Equal(-90, SimVector2.Forward.SignedAngleToDegrees(new SimVector2(1, 0)), precision: 3);
            SimVector2 rotated = SimVector2.Forward.RotateDegrees(-90);
            Assert.Equal(1, rotated.X, precision: 3);
            Assert.Equal(0, rotated.Z, precision: 3);
            Assert.Equal(new SimVector2(4, 6), vector.Add(new SimVector2(1, 2)));
            Assert.Equal(new SimVector2(2, 2), vector.Subtract(new SimVector2(1, 2)));
            Assert.Equal(new SimVector2(4, 6), vector + new SimVector2(1, 2));
            Assert.Equal(new SimVector2(2, 2), vector - new SimVector2(1, 2));
            Assert.Equal(new SimVector2(6, 8), vector.Multiply(2));
            Assert.Equal(new SimVector2(6, 8), vector * 2);
            Assert.Equal(new SimVector2(6, 8), 2 * vector);
            Assert.Equal(SimVector2.Zero, SimVector2.Zero.Normalize());
        }

        [Fact]
        public void SimMathf_ProvidesSharedScalarMathHelpers()
        {
            Assert.Equal(1, SimMathf.Clamp(2, -1, 1));
            Assert.Equal(0, SimMathf.Clamp01(-1));
            Assert.True(SimMathf.Approximately(1, 1.00001f));
            Assert.Equal(-170, SimMathf.NormalizeAngleDegrees(190));
            Assert.Equal(170, SimMathf.NormalizeAngleDegrees(-190));
            Assert.Equal(3, SimMathf.MoveTowards(0, 10, 3));
            Assert.Equal(10, SimMathf.MoveTowards(8, 10, 3));
            Assert.Throws<ArgumentException>(() => SimMathf.Clamp(0, 1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimMathf.MoveTowards(0, 1, -1));
        }

        [Fact]
        public void SimIntVector2_StoresValuesAndSupportsGridMathHelpers()
        {
            var vector = new SimIntVector2(3, -4);

            Assert.Equal(3, vector.X);
            Assert.Equal(-4, vector.Z);
            Assert.Equal(7, vector.ManhattanLength);
            Assert.Equal(new SimIntVector2(0, 0), SimIntVector2.Zero);
            Assert.Equal(new SimIntVector2(1, 1), SimIntVector2.One);
            Assert.Equal(new SimIntVector2(5, -1), vector.Add(new SimIntVector2(2, 3)));
            Assert.Equal(new SimIntVector2(1, -7), vector.Subtract(new SimIntVector2(2, 3)));
            Assert.Equal(new SimIntVector2(6, -8), vector.Multiply(2));
            Assert.Equal(7, vector.ManhattanDistanceTo(new SimIntVector2(1, 1)));
            Assert.Equal(new SimIntVector2(5, -1), vector + new SimIntVector2(2, 3));
            Assert.Equal(new SimIntVector2(1, -7), vector - new SimIntVector2(2, 3));
            Assert.Equal(new SimIntVector2(6, -8), vector * 2);
            Assert.Equal(new SimIntVector2(6, -8), 2 * vector);
            Assert.True(vector == new SimIntVector2(3, -4));
            Assert.True(vector != new SimIntVector2(3, 4));
        }

        [Fact]
        public void WorldTypes_ExposeVectorConveniencePropertiesWithoutChangingScalarState()
        {
            var tile = new MapRectTile
            {
                GridPosition = new SimIntVector2(2, 3),
                LocalPosition = new SimVector2(20, 30),
                WorldPosition = new SimVector2(120, 230),
                Size = new SimVector2(10, 20)
            };
            var bounds = new MapBounds(120, 230, 130, 250);
            var placedSection = new PlacedMapSection
            {
                GridPosition = new SimIntVector2(4, 5),
                Offset = new SimIntVector2(100, 200),
                Section = new MapSection { Tiles = [tile] }
            };
            var placedTile = new PlacedMapTile(placedSection, tile);

            Assert.Equal(2, tile.GridX);
            Assert.Equal(3, tile.GridZ);
            Assert.Equal(20, tile.X);
            Assert.Equal(30, tile.Z);
            Assert.Equal(120, tile.WorldX);
            Assert.Equal(230, tile.WorldZ);
            Assert.Equal(10, tile.SizeX);
            Assert.Equal(20, tile.SizeY);
            Assert.Equal(new SimVector2(125, 240), tile.WorldCenter);
            Assert.Equal(new SimVector2(120, 230), bounds.Min);
            Assert.Equal(new SimVector2(130, 250), bounds.Max);
            Assert.Equal(new SimVector2(10, 20), bounds.Size);
            Assert.Equal(new SimVector2(125, 240), bounds.Center);
            Assert.True(bounds.Contains(new SimVector2(125, 240)));
            Assert.Equal(new SimIntVector2(4, 5), placedSection.GridPosition);
            Assert.Equal(new SimIntVector2(100, 200), placedSection.Offset);
            Assert.Equal(new SimIntVector2(2, 3), placedTile.GridPosition);
            Assert.Equal(new SimVector2(120, 230), placedTile.WorldPosition);
            Assert.Equal(new SimVector2(125, 240), placedTile.WorldCenter);
        }

        [Fact]
        public void NavigatorEntity_DefaultsToMovementAndRotationTuning()
        {
            var navigator = new NavigatorEntity
            {
                Id = "navigator-001",
                Location = new MapLocation("forest", "forest-0-0", 0, 0, 1, 0, 15, 5)
            };

            Assert.IsAssignableFrom<InteractableEntity>(navigator);
            Assert.Equal(SimVector2.Forward, navigator.FaceForward);
            Assert.Equal(SimVector2.Forward, navigator.BaseForward);
            Assert.Equal(SimVector2.Zero, navigator.Velocity);
            Assert.Equal(0, navigator.RotationVelocity);
            Assert.Equal(1, navigator.MaxSpeed);
            Assert.Equal(1, navigator.MaxRotationSpeed);
            Assert.Equal(1, navigator.DecelerationRate);
            Assert.Equal(1, navigator.Location.TileGridX);
            Assert.True(navigator.CanMove);
            Assert.Equal(SimVector2.Zero, navigator.CurrentWorldPosition);
            Assert.Equal(NavigatorMovementStatus.Idle, navigator.PathState.Status);
            Assert.Equal(NavigatorMovementBehaviorType.Linear, navigator.MovementBehaviorType);
            Assert.IsType<LinearNavigatorMovementRuntimeBehavior>(navigator.MovementRuntimeBehavior);
        }

        [Fact]
        public void InteractableEntity_CanBeInherited()
        {
            var interactable = new TestInteractableEntity
            {
                Id = "derived-interactable"
            };

            Assert.IsAssignableFrom<InteractableEntity>(interactable);
            Assert.Equal("derived-interactable", interactable.Id);
            Assert.False(interactable.CanMove);
            interactable.Update(deltaTime: 0.25f);
        }

        [Fact]
        public void WorldState_UpdateCallsInteractablesWithDeltaTimeInStableOrder()
        {
            var updateOrder = new List<string>();
            var first = new TestUpdatingInteractableEntity("first", updateOrder);
            var navigator = new TestUpdatingNavigatorEntity("navigator", updateOrder);
            var second = new TestUpdatingInteractableEntity("second", updateOrder);
            var state = new WorldState
            {
                Entities =
                [
                    new Entity { Id = "static-entity" }
                ],
                Interactables =
                [
                    first,
                    navigator,
                    second
                ]
            };

            state.Update(0.125f);

            Assert.Equal(["first", "navigator", "second"], updateOrder);
            Assert.Equal(1, first.UpdateCount);
            Assert.Equal(1, navigator.UpdateCount);
            Assert.Equal(1, second.UpdateCount);
            Assert.Equal(0.125f, first.LastDeltaTime);
            Assert.Equal(0.125f, navigator.LastDeltaTime);
            Assert.Equal(0.125f, second.LastDeltaTime);
            Assert.Single(state.Entities);
        }

        [Fact]
        public void NavigatorEntity_CanBeInheritedAndUsesInteractableAttributes()
        {
            var navigator = new TestNavigatorEntity
            {
                Id = "player-001",
                PlayerTag = "local",
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "targeting",
                        Subtype = "targetable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    }
                ]
            };

            Assert.IsAssignableFrom<NavigatorEntity>(navigator);
            Assert.True(navigator.TryGetBooleanAttribute("TARGETING", "TARGETABLE", out bool targetable));
            Assert.True(targetable);
            Assert.Equal("local", navigator.PlayerTag);
        }

        [Fact]
        public void NavigatorEntity_JsonRoundTripPreservesMovementDataAttributesAndLocation()
        {
            var navigator = new NavigatorEntity
            {
                Id = "navigator-001",
                Location = new MapLocation("forest", "forest-0-0", 0, 0, 1, 0, 15, 5),
                FaceForward = new SimVector2(1, 0),
                BaseForward = new SimVector2(0, 1),
                Velocity = new SimVector2(2, 3),
                CurrentWorldPosition = new SimVector2(12, 5),
                RotationVelocity = 0.25f,
                MaxSpeed = 6,
                MaxRotationSpeed = 2,
                DecelerationRate = 3,
                MovementBehaviorType = NavigatorMovementBehaviorType.Steering,
                PathState = new NavigatorPathState
                {
                    Status = NavigatorMovementStatus.Moving,
                    Waypoints =
                    [
                        new MapLocation("forest", "forest-0-0", 0, 0, 1, 0, 15, 5)
                    ],
                    CurrentWaypointIndex = 0,
                    Destination = new MapLocation("forest", "forest-0-0", 0, 0, 1, 0, 15, 5)
                },
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "hp",
                        Subtype = "current",
                        ValueKind = EntityAttributeValueKind.Number,
                        Value = "10"
                    }
                ]
            };

            string json = JsonSerializer.Serialize(navigator);
            NavigatorEntity loaded = JsonSerializer.Deserialize<NavigatorEntity>(json)!;

            Assert.Equal("navigator-001", loaded.Id);
            Assert.Equal(1, loaded.Location.TileGridX);
            Assert.Equal(new SimVector2(1, 0), loaded.FaceForward);
            Assert.Equal(new SimVector2(0, 1), loaded.BaseForward);
            Assert.Equal(new SimVector2(2, 3), loaded.Velocity);
            Assert.Equal(new SimVector2(12, 5), loaded.CurrentWorldPosition);
            Assert.Equal(0.25f, loaded.RotationVelocity);
            Assert.Equal(6, loaded.MaxSpeed);
            Assert.Equal(2, loaded.MaxRotationSpeed);
            Assert.Equal(3, loaded.DecelerationRate);
            Assert.Equal(NavigatorMovementStatus.Moving, loaded.PathState.Status);
            Assert.Equal(0, loaded.PathState.CurrentWaypointIndex);
            Assert.Equal(1, Assert.Single(loaded.PathState.Waypoints).TileGridX);
            Assert.Equal(NavigatorMovementBehaviorType.Steering, loaded.MovementBehaviorType);
            Assert.IsType<SteeringNavigatorMovementRuntimeBehavior>(loaded.MovementRuntimeBehavior);
            Assert.DoesNotContain(nameof(NavigatorEntity.MovementRuntimeBehavior), json);
            Assert.True(loaded.TryGetNumberAttribute("hp", "current", out int hp));
            Assert.Equal(10, hp);
        }

        [Fact]
        public void WorldState_OwnsMapAndSeparateEntityCollections()
        {
            var map = new WorldMap
            {
                Id = "map-001",
                Name = "First Map",
                Description = "Map owned by state"
            };
            map.AddRegion(CreateRegion(
                id: "forest",
                displayName: "Forest",
                width: 1,
                depth: 1,
                tileSizeX: 10,
                tileSizeY: 10,
                offsetX: 0,
                offsetZ: 0));
            var state = new WorldState
            {
                Id = "state-001",
                Name = "First State",
                Map = map,
                Entities =
                [
                    new Entity
                    {
                        Id = "entity-001",
                        Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
                    }
                ],
                Interactables =
                [
                    new InteractableEntity
                    {
                        Id = "interactable-001",
                        Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
                    }
                ]
            };

            Assert.Equal("state-001", state.Id);
            Assert.Equal("map-001", state.Map.Id);
            Assert.Single(state.Entities);
            Assert.Single(state.Interactables);
        }

        [Fact]
        public void WorldState_TryGetEntityAndInteractableFindsById()
        {
            WorldState state = CreateWorldStateWithEntities();
            var navigator = new NavigatorEntity
            {
                Id = "navigator-001",
                Location = CreateEntityLocation(tileGridX: 1, tileGridZ: 0)
            };
            state.Interactables.Add(navigator);

            Assert.True(state.TryGetEntity("ENTITY-001", out Entity? entity));
            Assert.Equal("entity-001", entity!.Id);
            Assert.False(state.TryGetEntity("missing", out Entity? missingEntity));
            Assert.Null(missingEntity);

            Assert.True(state.TryGetInteractable("INTERACTABLE-001", out InteractableEntity? interactable));
            Assert.Equal("interactable-001", interactable!.Id);
            Assert.False(state.TryGetInteractable("missing", out InteractableEntity? missingInteractable));
            Assert.Null(missingInteractable);
            Assert.True(state.TryGetNavigator("NAVIGATOR-001", out NavigatorEntity? foundNavigator));
            Assert.Same(navigator, foundNavigator);
            Assert.False(state.TryGetNavigator("interactable-001", out NavigatorEntity? nonNavigator));
            Assert.Null(nonNavigator);
        }

        [Fact]
        public void WorldState_GetEntitiesAndInteractablesAtReturnsMatchingTileLocations()
        {
            WorldState state = CreateWorldStateWithEntities();

            IReadOnlyList<Entity> entities = state.GetEntitiesAt(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                tileGridX: 0,
                tileGridZ: 0);
            IReadOnlyList<InteractableEntity> interactables = state.GetInteractablesAt(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                tileGridX: 0,
                tileGridZ: 0);

            Entity entity = Assert.Single(entities);
            InteractableEntity interactable = Assert.Single(interactables);
            Assert.Equal("entity-001", entity.Id);
            Assert.Equal("interactable-001", interactable.Id);
            Assert.Empty(state.GetEntitiesAt("forest", "forest-0-0", tileGridX: 9, tileGridZ: 9));
            Assert.Empty(state.GetInteractablesAt("desert", "forest-0-0", tileGridX: 0, tileGridZ: 0));
        }

        [Fact]
        public void WorldState_GetOccupantsAtReturnsEntitiesAndInteractables()
        {
            WorldState state = CreateWorldStateWithEntities();

            IReadOnlyList<Entity> occupants = state.GetOccupantsAt(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                tileGridX: 0,
                tileGridZ: 0);

            Assert.Equal(2, occupants.Count);
            Assert.Contains(occupants, occupant => occupant.Id == "entity-001");
            Assert.Contains(occupants, occupant => occupant.Id == "interactable-001");
            Assert.True(state.IsTileOccupied("forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.False(state.IsTileOccupied("forest", "forest-0-0", tileGridX: 9, tileGridZ: 9));
        }

        [Fact]
        public void WorldState_IsTileBlockedChecksTerrainOccupantsAndMissingTiles()
        {
            WorldState state = CreateEmptyWorldStateWithMap();

            Assert.False(state.IsTileBlocked("forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.False(state.IsTileBlocked("forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));

            Assert.True(state.TryCreateEntity(new Entity { Id = "statue" }, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.IsTileBlocked("forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.IsTileBlocked(state.Entities.Single().Location));

            MapTile terrainTile = Assert.Single(Assert.Single(Assert.Single(state.Map.Regions).Sections).Section.Tiles, tile => tile.GridX == 1);
            terrainTile.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });
            Assert.True(state.IsTileBlocked("forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));

            Assert.True(state.IsTileBlocked("missing", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.IsTileBlocked("forest", "missing", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.IsTileBlocked("forest", "forest-0-0", tileGridX: 9, tileGridZ: 9));
        }

        [Fact]
        public void WorldState_TryFindPathRoutesAroundEntityBlockedTiles()
        {
            WorldState state = CreateWorldStateForPathing();
            PlacedMapSection section = Assert.Single(Assert.Single(state.Map.Regions).Sections);
            var start = new PlacedMapTile(section, section.Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0));
            var goal = new PlacedMapTile(section, section.Section.Tiles.Single(tile => tile.GridX == 2 && tile.GridZ == 0));

            Assert.True(state.TryCreateEntity(new Entity { Id = "blocker" }, "forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));
            bool found = state.TryFindPath(start, goal, out IReadOnlyList<PlacedMapTile> path);

            Assert.True(found);
            AssertPlacedPath(
                path,
                (section, 0, 0),
                (section, 0, 1),
                (section, 1, 1),
                (section, 2, 1),
                (section, 2, 0));
        }

        [Fact]
        public void WorldState_TryFindPathReturnsFalseWhenGoalIsBlockedByOccupant()
        {
            WorldState state = CreateWorldStateForPathing();
            PlacedMapSection section = Assert.Single(Assert.Single(state.Map.Regions).Sections);
            var start = new PlacedMapTile(section, section.Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0));
            var goal = new PlacedMapTile(section, section.Section.Tiles.Single(tile => tile.GridX == 2 && tile.GridZ == 0));

            Assert.True(state.TryCreateInteractable(new InteractableEntity { Id = "crate" }, "forest", "forest-0-0", tileGridX: 2, tileGridZ: 0));
            bool found = state.TryFindPath(start, goal, out IReadOnlyList<PlacedMapTile> path);

            Assert.False(found);
            Assert.Empty(path);
        }

        [Fact]
        public void WorldState_GetReachableTilesExcludesEntityBlockedTilesButAllowsBlockedStart()
        {
            WorldState state = CreateWorldStateForPathing();
            PlacedMapSection section = Assert.Single(Assert.Single(state.Map.Regions).Sections);
            var start = new PlacedMapTile(section, section.Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0));

            Assert.True(state.TryCreateEntity(new Entity { Id = "start-occupant" }, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.TryCreateEntity(new Entity { Id = "blocker" }, "forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));
            IReadOnlyList<PlacedMapTile> reachable = state.GetReachableTiles(start, movementBudget: 2);

            Assert.DoesNotContain(reachable, placed => placed.Tile.GridX == 1 && placed.Tile.GridZ == 0);
            Assert.DoesNotContain(reachable, placed => placed.Equals(start));
            AssertPlacedPath(
                reachable,
                (section, 0, 1),
                (section, 1, 1));
        }

        [Fact]
        public void WorldState_GetInteractablesInRangeReturnsTargetsFromMapLocation()
        {
            WorldState state = CreateWorldStateForPathing();
            Assert.True(state.TryCreateInteractable(
                CreateUsableInteractable("near"),
                "forest",
                "forest-0-0",
                tileGridX: 1,
                tileGridZ: 0));
            Assert.True(state.TryCreateInteractable(
                CreateUsableInteractable("far"),
                "forest",
                "forest-0-0",
                tileGridX: 2,
                tileGridZ: 1));
            var origin = new MapLocation("forest", "forest-0-0", 0, 0, 0, 0, 5, 5);

            IReadOnlyList<InteractableEntity> rangeOne = state.GetInteractablesInRange(origin, range: 1);
            IReadOnlyList<InteractableEntity> rangeThree = state.GetInteractablesInRange(origin, range: 3);

            InteractableEntity near = Assert.Single(rangeOne);
            Assert.Equal("near", near.Id);
            Assert.Equal(["far", "near"], rangeThree.Select(interactable => interactable.Id).OrderBy(id => id).ToArray());
        }

        [Fact]
        public void WorldState_GetInteractablesInRangeReturnsTargetsFromEntityId()
        {
            WorldState state = CreateWorldStateForPathing();
            Assert.True(state.TryCreateEntity(new Entity { Id = "actor" }, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.TryCreateInteractable(
                CreateUsableInteractable("target"),
                "forest",
                "forest-0-0",
                tileGridX: 1,
                tileGridZ: 0));

            IReadOnlyList<InteractableEntity> targets = state.GetInteractablesInRange("actor", range: 1);

            InteractableEntity target = Assert.Single(targets);
            Assert.Equal("target", target.Id);
            Assert.Empty(state.GetInteractablesInRange("missing", range: 1));
            Assert.Empty(state.GetInteractablesInRange("actor", range: -1));
        }

        [Fact]
        public void WorldState_CanInteractChecksRangeValidityAndTargetableAttributes()
        {
            WorldState state = CreateWorldStateForPathing();
            Assert.True(state.TryCreateEntity(new Entity { Id = "actor" }, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.TryCreateInteractable(
                CreateUsableInteractable("usable"),
                "forest",
                "forest-0-0",
                tileGridX: 1,
                tileGridZ: 0));
            Assert.True(state.TryCreateInteractable(
                CreateTargetableInteractable("targetable"),
                "forest",
                "forest-0-0",
                tileGridX: 0,
                tileGridZ: 1));
            Assert.True(state.TryCreateInteractable(
                new InteractableEntity { Id = "plain" },
                "forest",
                "forest-0-0",
                tileGridX: 1,
                tileGridZ: 1));
            Assert.True(state.TryCreateInteractable(
                CreateUsableInteractable("far"),
                "forest",
                "forest-0-0",
                tileGridX: 2,
                tileGridZ: 1));

            Assert.True(state.CanInteract("actor", "usable", range: 1));
            Assert.True(state.CanInteract("actor", "targetable", range: 1));
            Assert.False(state.CanInteract("actor", "plain", range: 2));
            Assert.False(state.CanInteract("actor", "far", range: 1));
            Assert.True(state.CanInteract("actor", "far", range: 3));
            Assert.False(state.CanInteract("missing", "usable", range: 1));
            Assert.False(state.CanInteract("actor", "missing", range: 1));
            Assert.False(state.CanInteract("actor", "usable", range: -1));
        }

        [Fact]
        public void WorldState_JsonRoundTripPreservesMapEntitiesAndInteractableAttributes()
        {
            WorldState state = CreateWorldStateWithEntities();

            string json = state.ToJson();
            WorldState loaded = WorldState.FromJson(json);

            Assert.Equal("state-001", loaded.Id);
            Assert.Equal("map-001", loaded.Map.Id);
            Assert.Single(loaded.Map.Regions);
            Assert.True(loaded.TryGetEntity("entity-001", out Entity? entity));
            Assert.Equal(0, entity!.Location.TileGridX);
            Assert.True(loaded.TryGetInteractable("interactable-001", out InteractableEntity? interactable));

            EntityAttribute rubble = Assert.Single(interactable!.Attributes);
            Assert.Equal(EntityAttributeValueKind.Number, rubble.ValueKind);
            Assert.True(rubble.TryGetNumber(out int movementValue));
            Assert.Equal(-1, movementValue);
            Assert.True(state.Players.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duncan" }));
            Assert.True(state.Players.TryAssignNavigator(42, "navigator-001"));
            json = state.ToJson();
            loaded = WorldState.FromJson(json);

            Assert.True(loaded.Players.TryGetProfile(42, out PlayerProfile? profile));
            Assert.Equal("Duncan", profile!.DisplayName);
            Assert.True(loaded.Players.TryGetRuntime(42, out PlayerRuntime? runtime));
            Assert.Equal("navigator-001", runtime!.NavigatorId);
            Assert.Contains("\"ValueKind\": \"Number\"", json);
            Assert.Contains("\"Players\"", json);
            Assert.Contains("\"Profiles\"", json);
            Assert.Contains("\"Runtimes\"", json);
        }

        [Fact]
        public async Task WorldState_SaveAndLoadAsyncRoundTripsState()
        {
            WorldState state = CreateWorldStateWithEntities();
            Assert.True(state.Players.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duncan" }));
            Assert.True(state.Players.TryAssignNavigator(42, "navigator-001"));
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.worldstate.json");

            try
            {
                await state.SaveAsync(path);

                WorldState loaded = await WorldState.LoadAsync(path);

                Assert.Equal("state-001", loaded.Id);
                Assert.True(loaded.TryGetEntity("entity-001", out Entity? entity));
                Assert.Equal("forest-0-0", entity!.Location.PlacedSectionId);
                Assert.True(loaded.TryGetInteractable("interactable-001", out InteractableEntity? interactable));
                Assert.Single(interactable!.Attributes);
                Assert.True(loaded.Players.TryGetProfile(42, out PlayerProfile? profile));
                Assert.Equal("Duncan", profile!.DisplayName);
                Assert.True(loaded.Players.TryGetNavigatorId(42, out string navigatorId));
                Assert.Equal("navigator-001", navigatorId);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void WorldState_TryCreateEntityAddsValidEntityAndCachesTileCenter()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var entity = new Entity
            {
                Id = "entity-new"
            };

            Assert.True(state.TryCreateEntity(
                entity,
                regionId: "forest",
                placedSectionId: "forest-0-0",
                tileGridX: 1,
                tileGridZ: 0));

            Entity created = Assert.Single(state.Entities);
            Assert.Same(entity, created);
            Assert.Equal("forest", created.Location.RegionId);
            Assert.Equal("forest-0-0", created.Location.PlacedSectionId);
            Assert.Equal(0, created.Location.SectionGridX);
            Assert.Equal(0, created.Location.SectionGridZ);
            Assert.Equal(1, created.Location.TileGridX);
            Assert.Equal(0, created.Location.TileGridZ);
            Assert.Equal(15, created.Location.WorldX);
            Assert.Equal(5, created.Location.WorldZ);
        }

        [Fact]
        public void WorldState_TryCreateEntityRejectsInvalidIdsDuplicatesAndLocations()
        {
            WorldState state = CreateWorldStateWithEntities();

            Assert.False(state.TryCreateEntity(new Entity { Id = string.Empty }, "forest", "forest-0-0", 0, 0));
            Assert.False(state.TryCreateEntity(new Entity { Id = "entity-001" }, "forest", "forest-0-0", 0, 0));
            Assert.False(state.TryCreateEntity(new Entity { Id = "interactable-001" }, "forest", "forest-0-0", 0, 0));
            Assert.False(state.TryCreateEntity(new Entity { Id = "entity-new" }, "missing", "forest-0-0", 0, 0));
            Assert.False(state.TryCreateEntity(new Entity { Id = "entity-new" }, "forest", "missing", 0, 0));
            Assert.False(state.TryCreateEntity(new Entity { Id = "entity-new" }, "forest", "forest-0-0", 9, 9));
            Assert.Equal(2, state.Entities.Count);
        }

        [Fact]
        public void WorldState_TryPlaceEntityUpdatesExistingStaticEntityLocation()
        {
            WorldState state = CreateWorldStateWithEntities();

            Assert.True(state.TryPlaceEntity("entity-001", "forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));
            Assert.True(state.TryGetEntity("entity-001", out Entity? entity));
            Assert.Equal(1, entity!.Location.TileGridX);
            Assert.Equal(15, entity.Location.WorldX);
            Assert.Equal(5, entity.Location.WorldZ);

            Assert.False(state.TryPlaceEntity("missing", "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.False(state.TryPlaceEntity("entity-001", "forest", "missing", tileGridX: 0, tileGridZ: 0));
        }

        [Fact]
        public void WorldState_TryCreateAndPlaceInteractableUpdatesCachedWorldPosition()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var interactable = new InteractableEntity
            {
                Id = "crate"
            };

            Assert.True(state.TryCreateInteractable(interactable, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.Equal(5, interactable.Location.WorldX);
            Assert.Equal(5, interactable.Location.WorldZ);

            Assert.True(state.TryPlaceInteractable("crate", "forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));
            Assert.Equal(1, interactable.Location.TileGridX);
            Assert.Equal(15, interactable.Location.WorldX);
            Assert.Equal(5, interactable.Location.WorldZ);

            Assert.False(state.TryMoveInteractable("crate", "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.Equal(1, interactable.Location.TileGridX);
            Assert.Equal(15, interactable.Location.WorldX);
            Assert.False(state.TryMoveInteractable("missing", "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.False(state.TryMoveInteractable("crate", "forest", "forest-0-0", tileGridX: 9, tileGridZ: 9));
        }

        [Fact]
        public void WorldState_TryMoveInteractableAllowsNavigatorsThroughSharedInteractableList()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var navigator = new NavigatorEntity
            {
                Id = "player"
            };

            Assert.True(state.TryCreateInteractable(navigator, "forest", "forest-0-0", tileGridX: 0, tileGridZ: 0));
            Assert.True(state.TryMoveInteractable("player", "forest", "forest-0-0", tileGridX: 1, tileGridZ: 0));

            InteractableEntity stored = Assert.Single(state.Interactables);
            Assert.Same(navigator, stored);
            Assert.True(stored.CanMove);
            Assert.Equal(1, navigator.Location.TileGridX);
            Assert.Equal(15, navigator.Location.WorldX);
            Assert.Equal(5, navigator.Location.WorldZ);
            Assert.Equal(new SimVector2(15, 5), navigator.CurrentWorldPosition);
        }

        [Fact]
        public void NavigatorEntity_LinearMovementMovesDirectlyAlongWaypointsAndCompletes()
        {
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            MapLocation destination = CreateEntityLocation(tileGridX: 1, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 10
            };

            navigator.SetPath([start, destination]);

            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(1, navigator.PathState.CurrentWaypointIndex);
            Assert.Equal(destination, navigator.PathState.Destination);

            navigator.Update(0.5f);

            Assert.Equal(new SimVector2(10, 5), navigator.CurrentWorldPosition);
            Assert.Equal(1, navigator.BaseForward.X, precision: 3);
            Assert.Equal(0, navigator.BaseForward.Z, precision: 3);
            Assert.Equal(new SimVector2(10, 0), navigator.Velocity);
            Assert.Equal(0, navigator.RotationVelocity);
            Assert.Equal(start, navigator.Location);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);

            navigator.Update(0.5f);

            Assert.Equal(destination, navigator.Location);
            Assert.Equal(destination.WorldPosition, navigator.CurrentWorldPosition);
            Assert.Equal(NavigatorMovementStatus.Completed, navigator.PathState.Status);
            Assert.Equal(SimVector2.Zero, navigator.Velocity);
            Assert.Equal(0, navigator.RotationVelocity);
        }

        [Fact]
        public void NavigatorEntity_SteeringMovementRotatesBeforeMovingForSideTarget()
        {
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            MapLocation destination = CreateEntityLocation(tileGridX: 1, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 10,
                MaxRotationSpeed = 180,
                MovementBehaviorType = NavigatorMovementBehaviorType.Steering
            };

            navigator.SetPath([start, destination]);

            Assert.IsType<SteeringNavigatorMovementRuntimeBehavior>(navigator.MovementRuntimeBehavior);

            navigator.Update(0.5f);

            Assert.Equal(start.WorldPosition, navigator.CurrentWorldPosition);
            Assert.Equal(1, navigator.BaseForward.X, precision: 3);
            Assert.Equal(0, navigator.BaseForward.Z, precision: 3);
            Assert.Equal(SimVector2.Zero, navigator.Velocity);
            Assert.Equal(-180, navigator.RotationVelocity);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);

            navigator.Update(0.5f);

            Assert.Equal(new SimVector2(10, 5), navigator.CurrentWorldPosition);
            Assert.Equal(new SimVector2(10, 0), navigator.Velocity);
            Assert.Equal(start, navigator.Location);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
        }

        [Fact]
        public void NavigatorEntity_SettingMovementBehaviorTypeSwapsRuntimeBehavior()
        {
            var navigator = new NavigatorEntity();

            Assert.IsType<LinearNavigatorMovementRuntimeBehavior>(navigator.MovementRuntimeBehavior);

            navigator.MovementBehaviorType = NavigatorMovementBehaviorType.Steering;

            Assert.IsType<SteeringNavigatorMovementRuntimeBehavior>(navigator.MovementRuntimeBehavior);
        }

        [Fact]
        public void NavigatorEntity_SetPathFailsEmptyPathAndCancelKeepsCurrentWorldPosition()
        {
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            MapLocation destination = CreateEntityLocation(tileGridX: 1, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                BaseForward = new SimVector2(1, 0),
                MaxSpeed = 10
            };

            navigator.SetPath([]);

            Assert.Equal(NavigatorMovementStatus.Failed, navigator.PathState.Status);
            Assert.Equal("Navigator path cannot be empty.", navigator.PathState.FailureReason);

            navigator.SetPath([destination]);
            navigator.Update(0.25f);
            Assert.NotEqual(start.WorldPosition, navigator.CurrentWorldPosition);
            SimVector2 canceledPosition = navigator.CurrentWorldPosition;

            navigator.CancelMovement();

            Assert.Equal(NavigatorMovementStatus.Canceled, navigator.PathState.Status);
            Assert.Equal(start, navigator.Location);
            Assert.Equal(canceledPosition, navigator.CurrentWorldPosition);
            Assert.Equal(SimVector2.Zero, navigator.Velocity);
            Assert.Equal(0, navigator.RotationVelocity);
        }

        [Fact]
        public void NavigatorEntity_ClearPathReturnsToIdle()
        {
            MapLocation destination = CreateEntityLocation(tileGridX: 1, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
            };

            navigator.SetPath([destination]);
            navigator.ClearPath();

            Assert.Equal(NavigatorMovementStatus.Idle, navigator.PathState.Status);
            Assert.Empty(navigator.PathState.Waypoints);
            Assert.Equal(SimVector2.Zero, navigator.Velocity);
            Assert.Equal(0, navigator.RotationVelocity);
        }

        [Fact]
        public void NavigatorEntity_DispatchesMoveEventsForStartedWaypointCompletedAndFailed()
        {
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            MapLocation middle = CreateEntityLocation(tileGridX: 1, tileGridZ: 0);
            MapLocation end = CreateEntityLocation(tileGridX: 2, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 10
            };
            var events = new RecordingSimEventDispatcher();

            navigator.SetPath([start, middle, end], events);

            MoveStatusChangeEvent started = Assert.IsType<MoveStatusChangeEvent>(Assert.Single(events.Events));
            Assert.Equal(SimEventType.MoveStatusChange, started.Type);
            Assert.Equal(MoveStatus.Starting, started.Payload.Status);
            Assert.Equal("player", started.Payload.MoverId);
            Assert.Equal(1, started.Payload.CurrentIndex);
            Assert.Equal([start.WorldPosition, middle.WorldPosition, end.WorldPosition], started.Payload.PathWorldPositions);

            navigator.Update(1f, events);

            Assert.Equal(2, events.Events.Count);
            MoveStatusChangeEvent waypointReached = Assert.IsType<MoveStatusChangeEvent>(events.Events[1]);
            Assert.Equal(MoveStatus.Continuing, waypointReached.Payload.Status);
            Assert.Equal("player", waypointReached.Payload.MoverId);
            Assert.Equal(2, waypointReached.Payload.CurrentIndex);
            Assert.Equal(middle.WorldPosition, waypointReached.Payload.CurrentWorldPosition);

            navigator.Update(1f, events);

            Assert.Equal(3, events.Events.Count);
            MoveStatusChangeEvent completed = Assert.IsType<MoveStatusChangeEvent>(events.Events[2]);
            Assert.Equal(MoveStatus.Completed, completed.Payload.Status);
            Assert.Equal("player", completed.Payload.MoverId);
            Assert.Equal(3, completed.Payload.CurrentIndex);
            Assert.Equal(end.WorldPosition, completed.Payload.CurrentWorldPosition);

            events.Events.Clear();
            navigator.SetPath([start, middle], events);
            events.Events.Clear();
            navigator.Update(0.25f, events);
            SimVector2 canceledPosition = navigator.CurrentWorldPosition;
            events.Events.Clear();
            navigator.CancelMovement(events);

            MoveStatusChangeEvent canceled = Assert.IsType<MoveStatusChangeEvent>(Assert.Single(events.Events));
            Assert.Equal(MoveStatus.Canceled, canceled.Payload.Status);
            Assert.Equal("player", canceled.Payload.MoverId);
            Assert.Equal(canceledPosition, canceled.Payload.CurrentWorldPosition);
            Assert.Equal(NavigatorMovementStatus.Canceled, navigator.PathState.Status);

            events.Events.Clear();
            navigator.SetPath([], events);

            MoveStatusChangeEvent failed = Assert.IsType<MoveStatusChangeEvent>(Assert.Single(events.Events));
            Assert.Equal(MoveStatus.Failed, failed.Payload.Status);
            Assert.Equal("player", failed.Payload.MoverId);
            Assert.Equal("Navigator path cannot be empty.", failed.Payload.FailureReason);
        }

        [Fact]
        public async Task GameOverlord_RunRequestAsyncReturnsRegionStateImmediately()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var crate = new InteractableEntity
            {
                Id = "crate",
                Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0),
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "targeting",
                        Subtype = "targetable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    }
                ]
            };
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = CreateEntityLocation(tileGridX: 1, tileGridZ: 0),
                CurrentWorldPosition = new SimVector2(14, 5),
                Velocity = new SimVector2(1, 0),
                FaceForward = new SimVector2(1, 0),
                BaseForward = new SimVector2(1, 0),
                RotationVelocity = 2,
                PathState = new NavigatorPathState
                {
                    Status = NavigatorMovementStatus.Moving,
                    Destination = CreateEntityLocation(tileGridX: 1, tileGridZ: 0)
                }
            };
            state.Interactables.Add(navigator);
            state.Interactables.Add(crate);
            var overlord = new GameOverlord(state);

            SimResponse response = await overlord.RunRequestAsync(new SimRequest
            {
                Type = SimRequestType.RequestRegionState,
                RequestRegionState = new RequestRegionStatePayload
                {
                    RegionId = "forest"
                }
            });

            Assert.Equal(SimResponseType.RegionState, response.Type);
            RegionStateResponsePayload regionState = Assert.IsType<RegionStateResponsePayload>(response.RegionState);
            Assert.Equal("forest", regionState.RegionId);
            Assert.Equal(["crate", "player-nav"], regionState.Interactables.Select(interactable => interactable.Id).ToArray());

            SimInteractableState crateState = regionState.Interactables[0];
            Assert.False(crateState.CanMove);
            Assert.Equal("InteractableEntity", crateState.EntityType);
            Assert.Equal(CreateEntityLocation(tileGridX: 0, tileGridZ: 0), crateState.Location);
            Assert.Equal(new SimVector2(5, 5), crateState.CurrentWorldPosition);
            Assert.Single(crateState.Attributes);
            Assert.Null(crateState.MovementStatus);

            SimInteractableState navigatorState = regionState.Interactables[1];
            Assert.True(navigatorState.CanMove);
            Assert.Equal("NavigatorEntity", navigatorState.EntityType);
            Assert.Equal(new SimVector2(14, 5), navigatorState.CurrentWorldPosition);
            Assert.Equal(new SimVector2(1, 0), navigatorState.Velocity);
            Assert.Equal(new SimVector2(1, 0), navigatorState.FaceForward);
            Assert.Equal(new SimVector2(1, 0), navigatorState.BaseForward);
            Assert.Equal(2, navigatorState.RotationVelocity);
            Assert.Equal(NavigatorMovementStatus.Moving, navigatorState.MovementStatus);
        }

        [Fact]
        public async Task GameOverlord_RunRequestAsyncReturnsFailureAndEmptyResponses()
        {
            var overlord = new GameOverlord(CreateEmptyWorldStateWithMap());

            SimResponse empty = await overlord.RunRequestAsync(new SimRequest
            {
                Type = SimRequestType.SimEmpty
            });
            SimResponse missingRegion = await overlord.RunRequestAsync(new SimRequest
            {
                Type = SimRequestType.RequestRegionState,
                RequestRegionState = new RequestRegionStatePayload
                {
                    RegionId = "missing"
                }
            });

            Assert.Equal(SimResponseType.SimEmpty, empty.Type);
            Assert.NotNull(empty.SimEmpty);
            Assert.Equal(SimResponseType.Failed, missingRegion.Type);
            Assert.Contains("missing", missingRegion.Failure!.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PlayerService_RegistersRejectsDuplicatesAndRemovesPlayers()
        {
            var service = new PlayerService();
            var profile = new PlayerProfile
            {
                Id = 42,
                DisplayName = "Duncan"
            };

            Assert.True(service.TryRegisterPlayer(profile));
            Assert.False(service.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duplicate" }));
            Assert.True(service.TryGetProfile(42, out PlayerProfile? loadedProfile));
            Assert.Same(profile, loadedProfile);
            Assert.True(service.TryGetRuntime(42, out PlayerRuntime? runtime));
            Assert.Equal(42, runtime!.PlayerId);
            Assert.Equal(string.Empty, runtime.NavigatorId);
            Assert.True(service.TryRemovePlayer(42));
            Assert.False(service.TryGetProfile(42, out _));
            Assert.False(service.TryGetRuntime(42, out _));
            Assert.False(service.TryRemovePlayer(42));
        }

        [Fact]
        public void PlayerService_AssignsUnassignsAndReverseLooksUpNavigator()
        {
            var service = new PlayerService();
            Assert.True(service.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duncan" }));
            Assert.True(service.TryRegisterPlayer(new PlayerProfile { Id = 7, DisplayName = "Other" }));

            Assert.False(service.TryAssignNavigator(99, "player-nav"));
            Assert.False(service.TryAssignNavigator(42, string.Empty));
            Assert.True(service.TryAssignNavigator(42, "player-nav"));
            Assert.True(service.TryGetNavigatorId(42, out string navigatorId));
            Assert.Equal("player-nav", navigatorId);
            Assert.True(service.TryGetPlayerIdForNavigator("PLAYER-NAV", out int playerId));
            Assert.Equal(42, playerId);
            Assert.False(service.TryAssignNavigator(7, "player-nav"));
            Assert.True(service.TryUnassignNavigator(42));
            Assert.False(service.TryGetNavigatorId(42, out _));
            Assert.False(service.TryGetPlayerIdForNavigator("player-nav", out _));
            Assert.False(service.TryUnassignNavigator(42));
        }

        [Fact]
        public void GameOverlord_AssignPlayerNavigatorTracksControlledNavigator()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);

            Assert.False(overlord.AssignPlayerNavigator(42, "player-nav"));
            RegisterTestPlayer(overlord, 42);
            Assert.True(overlord.AssignPlayerNavigator(42, "player-nav"));
            Assert.True(overlord.TryGetPlayerNavigator(42, out NavigatorEntity? controlledNavigator));
            Assert.Same(navigator, controlledNavigator);
            Assert.False(overlord.AssignPlayerNavigator(42, "missing"));
            Assert.True(overlord.UnassignPlayerNavigator(42));
            Assert.False(overlord.TryGetPlayerNavigator(42, out NavigatorEntity? unassignedNavigator));
            Assert.Null(unassignedNavigator);
            Assert.False(overlord.UnassignPlayerNavigator(42));
        }

        [Fact]
        public async Task GameOverlord_MoveCommandUsesAssignedNavigatorWithoutPayloadNavigatorId()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);

            RegisterTestPlayer(overlord, 42);
            Assert.True(overlord.AssignPlayerNavigator(42, "player-nav"));
            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    WorldPosition = new SimVector2(25, 5)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(2, navigator.PathState.Destination.TileGridX);
            SimAction action = Assert.Single(actions);
            Assert.Equal(MoveStatus.Starting, action.Move!.Status);
            Assert.Equal("player-nav", action.Move.MoverId);
            Assert.Equal(42, action.Move.PlayerId);
        }

        [Fact]
        public async Task GameOverlord_MoveCommandIgnoresSpoofedNavigatorIdWhenPlayerIsAssigned()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var controlledNavigator = new NavigatorEntity
            {
                Id = "controlled-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            var spoofedNavigator = new NavigatorEntity
            {
                Id = "spoofed-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            state.Interactables.Add(controlledNavigator);
            state.Interactables.Add(spoofedNavigator);
            var overlord = new GameOverlord(state);

            RegisterTestPlayer(overlord, 42);
            Assert.True(overlord.AssignPlayerNavigator(42, "controlled-nav"));
            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "spoofed-nav",
                    WorldPosition = new SimVector2(25, 5)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(NavigatorMovementStatus.Moving, controlledNavigator.PathState.Status);
            Assert.Equal(NavigatorMovementStatus.Idle, spoofedNavigator.PathState.Status);
            SimAction action = Assert.Single(actions);
            Assert.Equal("controlled-nav", action.Move!.MoverId);
        }

        [Fact]
        public async Task GameOverlord_MoveCommandWithoutAssignmentOrNavigatorIdFails()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var overlord = new GameOverlord(state);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    WorldPosition = new SimVector2(15, 5)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(SimActionTarget.Player, action.Target);
            Assert.Equal(42, action.PlayerId);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Failed, action.Move.Status);
            Assert.Equal(string.Empty, action.Move.MoverId);
            Assert.Contains("assigned navigator", action.Move.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GameOverlord_QueuedMoveCommandRunsDuringUpdateAndSetsNavigatorPath()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(25, 5)
                }
            });

            Assert.Equal(NavigatorMovementStatus.Idle, navigator.PathState.Status);

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(1, overlord.CurrentTick);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(2, navigator.PathState.Destination.TileGridX);
            Assert.Equal(0, navigator.PathState.Destination.TileGridZ);
            Assert.Equal(2, navigator.PathState.Waypoints[^1].TileGridX);
            Assert.Equal(0, navigator.PathState.Waypoints[^1].TileGridZ);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(SimActionTarget.Region, action.Target);
            Assert.Equal("forest", action.RegionId);
            Assert.Equal(42, action.PlayerId);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Starting, action.Move.Status);
            Assert.Equal("player-nav", action.Move.MoverId);
            Assert.Equal(42, action.Move.PlayerId);
            Assert.Equal(1, action.Move.CurrentIndex);
            Assert.Equal(new SimVector2(5, 5), action.Move.CurrentWorldPosition);
            Assert.Equal(new SimVector2(25, 5), action.Move.DestinationWorldPosition);
            Assert.Equal([new SimVector2(5, 5), new SimVector2(15, 5), new SimVector2(25, 5)], action.Move.PathWorldPositions);
            Assert.Equal(SimVector2.Zero, action.Move.Velocity);
            Assert.Equal(SimVector2.Forward, action.Move.FaceForward);
            Assert.Equal(SimVector2.Forward, action.Move.BaseForward);
            Assert.Equal(0, action.Move.RotationVelocity);
            Assert.Equal(0, action.Move.MaxSpeed);
            Assert.Equal(1, action.Move.MaxRotationSpeed);
            Assert.Equal(0, action.Tick);
        }

        [Fact]
        public async Task GameOverlord_UpdateAsyncProcessesCommandsCapturedBeforeWorldUpdateOnly()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "late-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            var overlord = new GameOverlord(state);
            var command = new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 7,
                    NavigatorId = "late-nav",
                    WorldPosition = new SimVector2(15, 5)
                }
            };
            state.Interactables.Add(navigator);
            state.Interactables.Add(new CommandEnqueueingInteractableEntity("enqueuer", overlord, command));

            await overlord.UpdateAsync(0.1f);

            Assert.Equal(1, overlord.CurrentTick);
            Assert.Equal(NavigatorMovementStatus.Idle, navigator.PathState.Status);

            await overlord.UpdateAsync(0.1f);

            Assert.Equal(2, overlord.CurrentTick);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(1, navigator.PathState.Destination.TileGridX);
        }

        [Fact]
        public async Task GameOverlord_InvalidMoveCommandDoesNotSetNavigatorPath()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "missing",
                    WorldPosition = new SimVector2(15, 5)
                }
            });
            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(95, 95)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(1, overlord.CurrentTick);
            Assert.Equal(NavigatorMovementStatus.Idle, navigator.PathState.Status);
            Assert.Equal(2, actions.Count);
            Assert.All(actions, action =>
            {
                Assert.Equal(SimActionType.Move, action.Type);
                Assert.Equal(SimActionTarget.Player, action.Target);
                Assert.Equal(42, action.PlayerId);
                Assert.NotNull(action.Move);
                Assert.Equal(MoveStatus.Failed, action.Move.Status);
            });
            MoveActionPayload firstMove = actions[0].Move!;
            MoveActionPayload secondMove = actions[1].Move!;
            Assert.Equal("missing", firstMove.MoverId);
            Assert.Contains("not found", firstMove.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("player-nav", secondMove.MoverId);
            Assert.Contains("target", secondMove.FailureReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GameOverlord_MalformedMoveCommandEmitsCommandStatusAction()
        {
            var overlord = new GameOverlord(CreateEmptyWorldStateWithMap());

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = null
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.CommandStatus, action.Type);
            Assert.Equal(SimActionTarget.Player, action.Target);
            Assert.Null(action.PlayerId);
            Assert.NotNull(action.CommandStatus);
            Assert.Equal(SimCommandType.PlayerMoveToWorldPosition, action.CommandStatus.CommandType);
            Assert.Equal(string.Empty, action.CommandStatus.SourceId);
            Assert.Equal(CommandStatus.Rejected, action.CommandStatus.Status);
            Assert.Contains("payload", action.CommandStatus.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Null(action.Move);
        }

        [Fact]
        public async Task GameOverlord_UnsupportedCommandTypeEmitsCommandStatusAction()
        {
            var overlord = new GameOverlord(CreateEmptyWorldStateWithMap());
            var unsupportedCommandType = (SimCommandType)999;

            overlord.EnqueueCommand(new SimCommand
            {
                Type = unsupportedCommandType
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.CommandStatus, action.Type);
            Assert.NotNull(action.CommandStatus);
            Assert.Equal(unsupportedCommandType, action.CommandStatus.CommandType);
            Assert.Equal(string.Empty, action.CommandStatus.SourceId);
            Assert.Equal(CommandStatus.Rejected, action.CommandStatus.Status);
            Assert.Contains("unsupported", action.CommandStatus.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GameOverlord_MoveCommandToCurrentTileEmitsCompletedMoveAction()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = start.WorldPosition
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(SimActionTarget.Region, action.Target);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Completed, action.Move.Status);
            Assert.Equal("player-nav", action.Move.MoverId);
            Assert.Equal([start.WorldPosition], action.Move.PathWorldPositions);
            Assert.Equal(start.WorldPosition, action.Move.CurrentWorldPosition);
            Assert.Equal(start.WorldPosition, action.Move.DestinationWorldPosition);
        }

        [Fact]
        public async Task GameOverlord_DispatchedMoveEventsEmitContinuingAndCompletedMoveActions()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 10
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(25, 5)
                }
            });

            IReadOnlyList<SimAction> startActions = await overlord.UpdateAsync(0.1f);
            IReadOnlyList<SimAction> waypointActions = await overlord.UpdateAsync(0.9f);
            IReadOnlyList<SimAction> completedActions = await overlord.UpdateAsync(1f);

            SimAction starting = Assert.Single(startActions);
            Assert.Equal(MoveStatus.Starting, starting.Move!.Status);

            SimAction continuing = Assert.Single(waypointActions);
            Assert.Equal(SimActionType.Move, continuing.Type);
            Assert.Equal(SimActionTarget.Region, continuing.Target);
            Assert.NotNull(continuing.Move);
            Assert.Equal(MoveStatus.Continuing, continuing.Move.Status);
            Assert.Equal("player-nav", continuing.Move.MoverId);
            Assert.Equal(42, continuing.Move.PlayerId);
            Assert.Equal(2, continuing.Move.CurrentIndex);
            Assert.Equal(new SimVector2(15, 5), continuing.Move.CurrentWorldPosition);
            Assert.Equal(new SimVector2(25, 5), continuing.Move.DestinationWorldPosition);

            SimAction completed = Assert.Single(completedActions);
            Assert.Equal(SimActionType.Move, completed.Type);
            Assert.Equal(SimActionTarget.Region, completed.Target);
            Assert.NotNull(completed.Move);
            Assert.Equal(MoveStatus.Completed, completed.Move.Status);
            Assert.Equal("player-nav", completed.Move.MoverId);
            Assert.Equal(42, completed.Move.PlayerId);
            Assert.Equal(3, completed.Move.CurrentIndex);
            Assert.Equal(new SimVector2(25, 5), completed.Move.CurrentWorldPosition);
            Assert.Equal(new SimVector2(25, 5), completed.Move.DestinationWorldPosition);
        }

        [Fact]
        public async Task GameOverlord_ValidMoveCommandWhileMovingEmitsCanceledThenStarting()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(25, 5)
                }
            });
            await overlord.UpdateAsync(0.1f);
            navigator.CurrentWorldPosition = new SimVector2(6, 5);
            SimVector2 midMovePosition = navigator.CurrentWorldPosition;
            Assert.NotEqual(start.WorldPosition, midMovePosition);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(5, 15)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(2, actions.Count);
            MoveActionPayload canceled = actions[0].Move!;
            MoveActionPayload starting = actions[1].Move!;
            Assert.Equal(MoveStatus.Canceled, canceled.Status);
            Assert.Equal("player-nav", canceled.MoverId);
            Assert.Equal(42, canceled.PlayerId);
            Assert.Equal(midMovePosition, canceled.CurrentWorldPosition);
            Assert.Equal([new SimVector2(5, 5), new SimVector2(15, 5), new SimVector2(25, 5)], canceled.PathWorldPositions);
            Assert.Equal(MoveStatus.Starting, starting.Status);
            Assert.Equal("player-nav", starting.MoverId);
            Assert.Equal(42, starting.PlayerId);
            Assert.Equal(midMovePosition, starting.CurrentWorldPosition);
            Assert.Equal(new SimVector2(5, 15), starting.DestinationWorldPosition);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(0, navigator.PathState.Destination.TileGridX);
            Assert.Equal(1, navigator.PathState.Destination.TileGridZ);
        }

        [Fact]
        public async Task GameOverlord_InvalidMoveCommandWhileMovingKeepsCurrentPath()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            var navigator = new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            };
            state.Interactables.Add(navigator);
            var overlord = new GameOverlord(state);
            RegisterTestPlayer(overlord, 42);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(25, 5)
                }
            });
            await overlord.UpdateAsync(0.1f);

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    NavigatorId = "player-nav",
                    WorldPosition = new SimVector2(95, 95)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction failed = Assert.Single(actions);
            Assert.Equal(MoveStatus.Failed, failed.Move!.Status);
            Assert.Equal("player-nav", failed.Move.MoverId);
            Assert.Contains("target", failed.Move.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(NavigatorMovementStatus.Moving, navigator.PathState.Status);
            Assert.Equal(2, navigator.PathState.Destination.TileGridX);
            Assert.Equal(0, navigator.PathState.Destination.TileGridZ);
        }

        [Fact]
        public async Task GameOverlord_UpdateAsyncCallsWorldUpdate()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var updateOrder = new List<string>();
            var updating = new TestUpdatingInteractableEntity("updating", updateOrder);
            state.Interactables.Add(updating);
            var overlord = new GameOverlord(state);

            await overlord.UpdateAsync(0.25f);

            Assert.Equal(1, overlord.CurrentTick);
            Assert.Equal(1, updating.UpdateCount);
            Assert.Equal(0.25f, updating.LastDeltaTime);
            Assert.Equal(["updating"], updateOrder);
        }

        [Fact]
        public async Task GameOverlord_UpdateAsyncReturnsActionsEmittedDuringWorldUpdate()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var overlord = new GameOverlord(state);
            state.Interactables.Add(new ActionEmittingInteractableEntity(
                id: "trap",
                overlord,
                action: new SimAction
                {
                    Type = SimActionType.PlayerHit,
                    Target = SimActionTarget.Player,
                    PlayerId = 42,
                    RegionId = "forest",
                    PlayerHit = new ActionPlayerHitPayload
                    {
                        PlayerId = 42,
                        SourceId = "trap",
                        Damage = 3,
                        RemainingHp = 7,
                        Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
                    }
                }));

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);
            IReadOnlyList<SimAction> nextActions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.PlayerHit, action.Type);
            Assert.Equal(SimActionTarget.Player, action.Target);
            Assert.Equal(0, action.Tick);
            Assert.Equal(42, action.PlayerId);
            Assert.Equal("forest", action.RegionId);
            Assert.NotNull(action.PlayerHit);
            Assert.Equal("trap", action.PlayerHit.SourceId);
            Assert.Equal(3, action.PlayerHit.Damage);
            Assert.Equal(7, action.PlayerHit.RemainingHp);
            Assert.Empty(nextActions);
            Assert.Equal(2, overlord.CurrentTick);
        }

        [Fact]
        public async Task GameOverlord_UpdateAsyncReturnsBroadcastAndRegionActionsInEmissionOrder()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            var overlord = new GameOverlord(state);
            state.Interactables.Add(new ActionEmittingInteractableEntity(
                id: "first",
                overlord,
                action: new SimAction
                {
                    Type = SimActionType.InteractableDestroyed,
                    Target = SimActionTarget.Broadcast,
                    InteractableDestroyed = new ActionInteractableDestroyedPayload
                    {
                        InteractableId = "barrel",
                        DestroyedById = "fire",
                        Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
                    }
                }));
            state.Interactables.Add(new ActionEmittingInteractableEntity(
                id: "second",
                overlord,
                action: new SimAction
                {
                    Type = SimActionType.InteractableDestroyed,
                    Target = SimActionTarget.Region,
                    RegionId = "forest",
                    InteractableDestroyed = new ActionInteractableDestroyedPayload
                    {
                        InteractableId = "crate",
                        DestroyedById = "player-nav",
                        Location = CreateEntityLocation(tileGridX: 1, tileGridZ: 0)
                    }
                }));

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            Assert.Equal(2, actions.Count);
            Assert.Equal(SimActionTarget.Broadcast, actions[0].Target);
            Assert.Equal("barrel", actions[0].InteractableDestroyed!.InteractableId);
            Assert.Equal(0, actions[0].Tick);
            Assert.Equal(SimActionTarget.Region, actions[1].Target);
            Assert.Equal("forest", actions[1].RegionId);
            Assert.Equal("crate", actions[1].InteractableDestroyed!.InteractableId);
            Assert.Equal(0, actions[1].Tick);
        }

        [Fact]
        public void SimAction_JsonRoundTripPreservesRoutingAndPayload()
        {
            var action = new SimAction
            {
                Type = SimActionType.InteractableDestroyed,
                Target = SimActionTarget.Region,
                Tick = 9,
                RegionId = "forest",
                InteractableDestroyed = new ActionInteractableDestroyedPayload
                {
                    InteractableId = "crate",
                    DestroyedById = "player-nav",
                    Location = CreateEntityLocation(tileGridX: 1, tileGridZ: 0)
                }
            };

            string json = SimJson.ToJson(action);
            SimAction loaded = SimJson.FromJson<SimAction>(json);

            Assert.Equal(SimActionType.InteractableDestroyed, loaded.Type);
            Assert.Equal(SimActionTarget.Region, loaded.Target);
            Assert.Equal(9, loaded.Tick);
            Assert.Equal("forest", loaded.RegionId);
            Assert.NotNull(loaded.InteractableDestroyed);
            Assert.Equal("crate", loaded.InteractableDestroyed.InteractableId);
            Assert.Equal("player-nav", loaded.InteractableDestroyed.DestroyedById);
            Assert.Equal(1, loaded.InteractableDestroyed.Location.TileGridX);
            Assert.Contains("\"type\":\"interactableDestroyed\"", json);
            Assert.Contains("\"target\":\"region\"", json);
            Assert.DoesNotContain("\"type\":1", json);
        }

        [Fact]
        public void SimAction_MoveActionJsonRoundTripPreservesMovementState()
        {
            var action = new SimAction
            {
                Type = SimActionType.Move,
                Target = SimActionTarget.Region,
                Tick = 12,
                RegionId = "forest",
                PlayerId = 42,
                Move = new MoveActionPayload
                {
                    Status = MoveStatus.Continuing,
                    MoverId = "player-nav",
                    PlayerId = 42,
                    PathWorldPositions =
                    [
                        new SimVector2(5, 5),
                        new SimVector2(15, 5)
                    ],
                    CurrentIndex = 1,
                    CurrentWorldPosition = new SimVector2(7, 5),
                    DestinationWorldPosition = new SimVector2(15, 5),
                    Velocity = new SimVector2(1, 0),
                    FaceForward = new SimVector2(0, 1),
                    BaseForward = new SimVector2(1, 0),
                    RotationVelocity = 2,
                    MaxSpeed = 6,
                    MaxRotationSpeed = 3,
                    FailureReason = "none"
                }
            };

            string json = SimJson.ToJson(action);
            SimAction loaded = SimJson.FromJson<SimAction>(json);

            Assert.Equal(SimActionType.Move, loaded.Type);
            Assert.Equal(SimActionTarget.Region, loaded.Target);
            Assert.Equal(12, loaded.Tick);
            Assert.Equal("forest", loaded.RegionId);
            Assert.Equal(42, loaded.PlayerId);
            Assert.NotNull(loaded.Move);
            Assert.Equal(MoveStatus.Continuing, loaded.Move.Status);
            Assert.Equal("player-nav", loaded.Move.MoverId);
            Assert.Equal(42, loaded.Move.PlayerId);
            Assert.Equal([new SimVector2(5, 5), new SimVector2(15, 5)], loaded.Move.PathWorldPositions);
            Assert.Equal(1, loaded.Move.CurrentIndex);
            Assert.Equal(new SimVector2(7, 5), loaded.Move.CurrentWorldPosition);
            Assert.Equal(new SimVector2(15, 5), loaded.Move.DestinationWorldPosition);
            Assert.Equal(new SimVector2(1, 0), loaded.Move.Velocity);
            Assert.Equal(new SimVector2(0, 1), loaded.Move.FaceForward);
            Assert.Equal(new SimVector2(1, 0), loaded.Move.BaseForward);
            Assert.Equal(2, loaded.Move.RotationVelocity);
            Assert.Equal(6, loaded.Move.MaxSpeed);
            Assert.Equal(3, loaded.Move.MaxRotationSpeed);
            Assert.Equal("none", loaded.Move.FailureReason);
            Assert.Contains("\"type\":\"move\"", json);
            Assert.Contains("\"status\":\"continuing\"", json);
            Assert.Contains("\"target\":\"region\"", json);
        }

        [Fact]
        public void SimAction_CommandStatusJsonRoundTripPreservesSourceAndStatus()
        {
            var action = new SimAction
            {
                Type = SimActionType.CommandStatus,
                Target = SimActionTarget.Player,
                Tick = 3,
                CommandStatus = new CommandStatusActionPayload
                {
                    CommandType = SimCommandType.PlayerMoveToWorldPosition,
                    SourceId = "player-nav",
                    Status = CommandStatus.Rejected,
                    Reason = "not allowed"
                }
            };

            string json = SimJson.ToJson(action);
            SimAction loaded = SimJson.FromJson<SimAction>(json);

            Assert.Equal(SimActionType.CommandStatus, loaded.Type);
            Assert.Equal(SimActionTarget.Player, loaded.Target);
            Assert.Equal(3, loaded.Tick);
            Assert.NotNull(loaded.CommandStatus);
            Assert.Equal(SimCommandType.PlayerMoveToWorldPosition, loaded.CommandStatus.CommandType);
            Assert.Equal("player-nav", loaded.CommandStatus.SourceId);
            Assert.Equal(CommandStatus.Rejected, loaded.CommandStatus.Status);
            Assert.Equal("not allowed", loaded.CommandStatus.Reason);
            Assert.Contains("\"type\":\"commandStatus\"", json);
            Assert.Contains("\"commandType\":\"playerMoveToWorldPosition\"", json);
            Assert.Contains("\"sourceId\":\"player-nav\"", json);
            Assert.Contains("\"status\":\"rejected\"", json);
        }

        [Fact]
        public void SimJson_RoundTripsRequestsCommandsAndResponsesWithStringEnums()
        {
            var request = new SimRequest
            {
                Type = SimRequestType.RequestRegionState,
                RequestRegionState = new RequestRegionStatePayload
                {
                    RegionId = "forest"
                }
            };
            var command = new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    WorldPosition = new SimVector2(15, 5)
                }
            };
            SimResponse response = SimResponse.Failed("nope");

            string requestJson = SimJson.ToJson(request);
            string commandJson = SimJson.ToJson(command);
            string responseJson = SimJson.ToJson(response);

            SimRequest loadedRequest = SimJson.FromJson<SimRequest>(requestJson);
            SimCommand loadedCommand = SimJson.FromJson<SimCommand>(commandJson);
            SimResponse loadedResponse = SimJson.FromJson<SimResponse>(responseJson);

            Assert.Equal(SimRequestType.RequestRegionState, loadedRequest.Type);
            Assert.Equal("forest", loadedRequest.RequestRegionState!.RegionId);
            Assert.Equal(SimCommandType.PlayerMoveToWorldPosition, loadedCommand.Type);
            Assert.Equal(42, loadedCommand.PlayerMoveToWorldPosition!.PlayerId);
            Assert.Equal(new SimVector2(15, 5), loadedCommand.PlayerMoveToWorldPosition.WorldPosition);
            Assert.Equal(SimResponseType.Failed, loadedResponse.Type);
            Assert.Equal("nope", loadedResponse.Failure!.Reason);
            Assert.Contains("\"type\":\"requestRegionState\"", requestJson);
            Assert.Contains("\"type\":\"playerMoveToWorldPosition\"", commandJson);
            Assert.Contains("\"type\":\"failed\"", responseJson);
        }

        [Fact]
        public void SimJson_ReadsCaseInsensitiveClientJson()
        {
            const string json = """
                {
                  "TYPE": "Move",
                  "TARGET": "Region",
                  "MOVE": {
                    "STATUS": "Completed",
                    "MOVERID": "player-nav"
                  }
                }
                """;

            SimAction action = SimJson.FromJson<SimAction>(json);

            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(SimActionTarget.Region, action.Target);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Completed, action.Move.Status);
            Assert.Equal("player-nav", action.Move.MoverId);
        }

        [Fact]
        public void WorldState_ValidationDetectsDuplicateIdsAndStaleLocations()
        {
            WorldState state = CreateWorldStateWithEntities();

            Assert.False(state.HasDuplicateEntityIds());
            state.Interactables.Add(new InteractableEntity
            {
                Id = "ENTITY-001",
                Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
            });
            Assert.True(state.HasDuplicateEntityIds());

            Assert.True(state.IsEntityLocationValid(state.Entities[0]));
            Assert.True(state.IsInteractableLocationValid(state.Interactables[0]));

            state.Entities[0].Location = new MapLocation(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                sectionGridX: 0,
                sectionGridZ: 0,
                tileGridX: 9,
                tileGridZ: 9,
                worldX: 95,
                worldZ: 95);
            Assert.False(state.IsEntityLocationValid(state.Entities[0]));

            state.Interactables[0].Location = new MapLocation(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                sectionGridX: 0,
                sectionGridZ: 0,
                tileGridX: 0,
                tileGridZ: 0,
                worldX: 6,
                worldZ: 5);
            Assert.False(state.IsInteractableLocationValid(state.Interactables[0]));
        }

        [Fact]
        public void InteractableEntity_TypedAttributeHelpersFindCaseInsensitiveMatches()
        {
            var interactable = new InteractableEntity
            {
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "HP",
                        Subtype = "Current",
                        ValueKind = EntityAttributeValueKind.Number,
                        Value = "7"
                    },
                    new EntityAttribute
                    {
                        Key = "targeting",
                        Subtype = "targetable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    },
                    new EntityAttribute
                    {
                        Key = "display",
                        Subtype = "name",
                        ValueKind = EntityAttributeValueKind.Text,
                        Value = "Crate"
                    }
                ]
            };

            Assert.True(interactable.TryGetAttribute("hp", "current", out EntityAttribute? hp));
            Assert.NotNull(hp);
            Assert.True(interactable.TryGetNumberAttribute("hp", "current", out int hpValue));
            Assert.Equal(7, hpValue);
            Assert.True(interactable.TryGetBooleanAttribute("TARGETING", "TARGETABLE", out bool targetable));
            Assert.True(targetable);
            Assert.True(interactable.TryGetTextAttribute("display", "name", out string name));
            Assert.Equal("Crate", name);

            Assert.False(interactable.TryGetAttribute("missing", "current", out EntityAttribute? missing));
            Assert.Null(missing);
            Assert.False(interactable.TryGetNumberAttribute("targeting", "targetable", out int wrongNumber));
            Assert.Equal(0, wrongNumber);
            Assert.False(interactable.TryGetBooleanAttribute("display", "name", out bool wrongBoolean));
            Assert.False(wrongBoolean);
        }

        [Fact]
        public void HexPointyTopContainment_UsesHexShapeInsteadOfBroadBounds()
        {
            var tile = new MapHexTile
            {
                WorldX = 0,
                WorldZ = 0,
                SizeX = 10,
                SizeY = 20,
                Orientation = HexOrientation.PointyTop
            };

            Assert.True(tile.ContainsWorldPosition(5f, 10f));
            Assert.True(tile.ContainsWorldPosition(5f, 0f));
            Assert.True(tile.ContainsWorldPosition(0f, 5f));
            Assert.True(tile.ContainsWorldPosition(9.999f, 10f));
            Assert.False(tile.ContainsWorldPosition(0f, 0f));
            Assert.False(tile.ContainsWorldPosition(10f, 0f));
            Assert.False(tile.ContainsWorldPosition(5f, 20f));
            Assert.False(tile.ContainsWorldPosition(-0.001f, 10f));
        }

        [Fact]
        public void HexFlatTopContainment_UsesHexShapeInsteadOfBroadBounds()
        {
            var tile = new MapHexTile
            {
                WorldX = 0,
                WorldZ = 0,
                SizeX = 20,
                SizeY = 10,
                Orientation = HexOrientation.FlatTop
            };

            Assert.True(tile.ContainsWorldPosition(10f, 5f));
            Assert.True(tile.ContainsWorldPosition(0f, 5f));
            Assert.True(tile.ContainsWorldPosition(5f, 0f));
            Assert.True(tile.ContainsWorldPosition(10f, 9.999f));
            Assert.False(tile.ContainsWorldPosition(0f, 0f));
            Assert.False(tile.ContainsWorldPosition(20f, 5f));
            Assert.False(tile.ContainsWorldPosition(0f, 10f));
            Assert.False(tile.ContainsWorldPosition(10f, -0.001f));
        }

        [Fact]
        public void JsonRoundTrip_PreservesSectionsTilesAndOrderedAttributes()
        {
            MapSection section = MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 4, tileSizeY: 4);
            MapTile tile = Assert.Single(section.Tiles);
            tile.AddAttribute(new MapAttribute
            {
                Id = "grass",
                ModifierKey = "movement",
                Amount = 1
            });
            tile.AddAttribute(new MapAttribute
            {
                Id = "mud",
                ModifierKey = "movement",
                Amount = -2
            });
            tile.AddAttribute(new MapAttribute
            {
                Id = "fire",
                ModifierKey = "hp",
                Amount = -10
            });
            var map = new WorldMap
            {
                Id = "main-world",
                Name = "Main World",
                Description = "The first Clan Iron world.",
                TileSettings = new MapTileSettings
                {
                    Shape = MapTileShape.Hex,
                    SizeX = 12.5f,
                    SizeY = 15.5f,
                    HexOrientation = HexOrientation.FlatTop
                }
            };
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(section, offsetX: 7, offsetZ: 9);
            map.AddRegion(region);

            WorldMap loaded = WorldMap.FromJson(map.ToJson());

            Assert.Equal(WorldMap.CurrentGeometryVersion, loaded.GeometryVersion);
            Assert.Equal("main-world", loaded.Id);
            Assert.Equal("Main World", loaded.Name);
            Assert.Equal("The first Clan Iron world.", loaded.Description);
            Assert.Equal(MapTileShape.Hex, loaded.TileSettings.Shape);
            Assert.Equal(12.5f, loaded.TileSettings.SizeX);
            Assert.Equal(15.5f, loaded.TileSettings.SizeY);
            Assert.Equal(HexOrientation.FlatTop, loaded.TileSettings.HexOrientation);
            MapRegion loadedRegion = Assert.Single(loaded.Regions);
            Assert.Equal("forest", loadedRegion.Id);
            Assert.Equal("Forest", loadedRegion.DisplayName);
            PlacedMapSection placed = Assert.Single(loadedRegion.Sections);
            Assert.Equal(7, placed.OffsetX);
            Assert.Equal(9, placed.OffsetZ);
            MapTile loadedTile = Assert.Single(placed.Section.Tiles);
            Assert.Equal(["grass", "mud", "fire"], loadedTile.Attributes.Select(attribute => attribute.Id).ToArray());
            Assert.Equal(["movement", "movement", "hp"], loadedTile.Attributes.Select(attribute => attribute.ModifierKey).ToArray());
            Assert.Equal([1, -2, -10], loadedTile.Attributes.Select(attribute => attribute.Amount).ToArray());
        }

        [Fact]
        public void JsonRoundTrip_SerializesHexShapeAndOrientationAsStrings()
        {
            var map = new WorldMap
            {
                Id = "shape-test",
                Name = "Shape Test",
                Description = "Verifies shape enum JSON.",
                TileSettings = new MapTileSettings
                {
                    Shape = MapTileShape.Hex,
                    SizeX = 5,
                    SizeY = 6,
                    HexOrientation = HexOrientation.PointyTop
                }
            };
            var region = new MapRegion
            {
                Id = "hex-region",
                DisplayName = "Hex Region"
            };
            var section = new MapSection
            {
                Width = 1,
                Depth = 1,
                Tiles =
                [
                    new MapHexTile
                    {
                        GridX = 0,
                        GridZ = 0,
                        X = 0,
                        Z = 0,
                        WorldX = 0,
                        WorldZ = 0,
                        SizeX = 5,
                        SizeY = 6,
                        Orientation = HexOrientation.FlatTop,
                        Coordinate = new HexCoord(2, -1)
                    }
                ]
            };
            region.AddSection(section, offsetX: 0, offsetZ: 0);
            map.AddRegion(region);

            string json = map.ToJson();

            Assert.Contains($"\"GeometryVersion\": {WorldMap.CurrentGeometryVersion}", json);
            Assert.Contains("\"Shape\": \"Hex\"", json);
            Assert.Contains("\"HexOrientation\": \"PointyTop\"", json);
            Assert.Contains("\"HexOrientation\": \"FlatTop\"", json);
            Assert.Contains("\"Coordinate\"", json);
            Assert.DoesNotContain("\"Shape\": 1", json);

            WorldMap loaded = WorldMap.FromJson(json);
            Assert.Equal(MapTileShape.Hex, loaded.TileSettings.Shape);
            Assert.Equal(HexOrientation.PointyTop, loaded.TileSettings.HexOrientation);
            var loadedTile = Assert.IsType<MapHexTile>(Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles));
            Assert.Equal(MapTileShape.Hex, loadedTile.Shape);
            Assert.Equal(HexOrientation.FlatTop, loadedTile.Orientation);
            Assert.Equal(new HexCoord(2, -1), loadedTile.Coordinate);
        }

        [Fact]
        public void JsonRoundTrip_SerializesCurrentGeometryVersion()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);

            string json = map.ToJson();
            WorldMap loaded = WorldMap.FromJson(json);

            Assert.Contains($"\"GeometryVersion\": {WorldMap.CurrentGeometryVersion}", json);
            Assert.Equal(WorldMap.CurrentGeometryVersion, loaded.GeometryVersion);
        }

        [Fact]
        public void WorldLookup_FindsSectionTileAndGridPositionFromFloatWorldPosition()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 3, depth: 2, tileSizeX: 10, tileSizeY: 20, offsetX: 100, offsetZ: 200);

            Assert.True(map.TryGetRegionAtWorldPosition(125.5f, 221.25f, out MapRegion? region));
            Assert.NotNull(region);
            Assert.Equal("forest", region.Id);
            Assert.True(map.TryGetSectionAtWorldPosition(125.5f, 221.25f, out PlacedMapSection? section));
            Assert.NotNull(section);
            Assert.Equal(new MapBounds(100, 200, 130, 240), section.WorldBounds);

            Assert.True(map.TryGetTileAtWorldPosition(125.5f, 221.25f, out MapTile? tile));
            Assert.NotNull(tile);
            Assert.Equal(2, tile.GridX);
            Assert.Equal(1, tile.GridZ);
            Assert.Equal(120, tile.WorldX);
            Assert.Equal(220, tile.WorldZ);

            Assert.True(map.TryWorldToSectionGridPosition(125.5f, 221.25f, out MapSectionGridPosition gridPosition));
            Assert.Same(section, gridPosition.Section);
            Assert.Equal(2, gridPosition.GridX);
            Assert.Equal(1, gridPosition.GridZ);
        }

        [Fact]
        public void WorldState_TryWorldToMapLocationReturnsStableGridLocationAndTileCenter()
        {
            WorldState state = CreateEmptyWorldStateWithMap();

            Assert.True(state.TryWorldToMapLocation(15f, 5f, out MapLocation location));

            Assert.Equal("forest", location.RegionId);
            Assert.Equal("forest-0-0", location.PlacedSectionId);
            Assert.Equal(0, location.SectionGridX);
            Assert.Equal(0, location.SectionGridZ);
            Assert.Equal(1, location.TileGridX);
            Assert.Equal(0, location.TileGridZ);
            Assert.Equal(15, location.WorldX);
            Assert.Equal(5, location.WorldZ);
        }

        [Fact]
        public void WorldState_TryWorldToMapLocationUsesTileContainmentAndRejectsMissingPosition()
        {
            WorldState state = CreateWorldStateWithPointyHexMap();

            Assert.True(state.TryWorldToMapLocation(5f, 10f, out MapLocation center));
            Assert.Equal(0, center.TileGridX);
            Assert.Equal(0, center.TileGridZ);
            Assert.Equal(5, center.WorldX);
            Assert.Equal(10, center.WorldZ);

            Assert.False(state.TryWorldToMapLocation(0f, 0f, out MapLocation corner));
            Assert.Equal(default, corner);
            Assert.False(state.TryWorldToMapLocation(100f, 100f, out _));
        }

        [Fact]
        public void SectionGridToWorldPosition_ReturnsTileCenter()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 3, depth: 2, tileSizeX: 10, tileSizeY: 20, offsetX: 100, offsetZ: 200);
            PlacedMapSection section = Assert.Single(Assert.Single(map.Regions).Sections);

            Assert.True(map.TrySectionGridToWorldPosition(section, gridX: 2, gridZ: 1, out WorldMapPosition position));

            Assert.Equal(125f, position.X);
            Assert.Equal(230f, position.Z);
        }

        [Fact]
        public void WorldLookup_UsesMinInclusiveMaxExclusiveBounds()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);

            Assert.True(map.TryGetTileAtWorldPosition(9.999f, 5f, out MapTile? firstTile));
            Assert.Equal(0, firstTile?.GridX);

            Assert.True(map.TryGetTileAtWorldPosition(10f, 5f, out MapTile? secondTile));
            Assert.Equal(1, secondTile?.GridX);

            Assert.False(map.TryGetTileAtWorldPosition(20f, 5f, out _));
        }

        [Fact]
        public void WorldLookup_UsesHexContainmentWhenBroadBoundsOverlap()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "hex",
                DisplayName = "Hex"
            };
            region.AddSection(
                MapSection.CreateHexGrid(
                    width: 2,
                    depth: 1,
                    tileSizeX: 10,
                    tileSizeY: 20,
                    orientation: HexOrientation.PointyTop),
                offsetX: 0,
                offsetZ: 0);
            map.AddRegion(region);

            Assert.True(map.TryGetTileAtWorldPosition(13f, 11f, out MapTile? tile));

            Assert.NotNull(tile);
            Assert.Equal(1, tile.GridX);
            Assert.Equal(new HexCoord(1, 0), Assert.IsType<MapHexTile>(tile).Coordinate);
        }

        [Fact]
        public void TryGetPlacedTileAtWorldPosition_ReturnsSectionAndTile()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 100, offsetZ: 200);

            Assert.True(map.TryGetPlacedTileAtWorldPosition(115f, 205f, out PlacedMapTile placedTile));

            Assert.NotNull(placedTile.Section);
            Assert.Equal(1, placedTile.Tile.GridX);
            Assert.Equal(0, placedTile.Tile.GridZ);
            Assert.Equal(110, placedTile.Tile.WorldX);
            Assert.Equal(200, placedTile.Tile.WorldZ);
        }

        [Fact]
        public void GetNeighborTiles_ReturnsPlacedNeighborsAcrossTouchingRectangleSections()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);
            PlacedMapSection firstSection = region.Sections[0];
            MapTile edgeTile = firstSection.Section.Tiles.Single(tile => tile.GridX == 1 && tile.GridZ == 0);

            IReadOnlyList<PlacedMapTile> neighbors = map.GetNeighborTiles(new PlacedMapTile(firstSection, edgeTile));

            Assert.Equal(2, neighbors.Count);
            Assert.Contains(neighbors, neighbor =>
                ReferenceEquals(neighbor.Section, firstSection)
                && neighbor.Tile.GridX == 0
                && neighbor.Tile.GridZ == 0);
            Assert.Contains(neighbors, neighbor =>
                ReferenceEquals(neighbor.Section, region.Sections[1])
                && neighbor.Tile.GridX == 0
                && neighbor.Tile.GridZ == 0);
        }

        [Fact]
        public void TryFindPath_FindsMapLevelPathAcrossTouchingRectangleSections()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);

            bool found = map.TryFindPath(
                new WorldMapPosition(5f, 5f),
                new WorldMapPosition(35f, 5f),
                out IReadOnlyList<PlacedMapTile> path);

            Assert.True(found);
            AssertPlacedPath(
                path,
                (region.Sections[0], 0, 0),
                (region.Sections[0], 1, 0),
                (region.Sections[1], 0, 0),
                (region.Sections[1], 1, 0));
        }

        [Fact]
        public void TryFindPath_ReturnsFalseWhenWorldPositionsDoNotResolveToTiles()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);

            bool found = map.TryFindPath(
                new WorldMapPosition(5f, 5f),
                new WorldMapPosition(100f, 100f),
                out IReadOnlyList<PlacedMapTile> path);

            Assert.False(found);
            Assert.Empty(path);
        }

        [Fact]
        public void GetReachableTiles_ReturnsMapLevelTilesAcrossTouchingSections()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);

            IReadOnlyList<PlacedMapTile> reachable = map.GetReachableTiles(
                new WorldMapPosition(5f, 5f),
                movementBudget: 3);

            AssertPlacedPath(
                reachable,
                (region.Sections[0], 1, 0),
                (region.Sections[1], 0, 0),
                (region.Sections[1], 1, 0));
        }

        [Fact]
        public void TryFindPath_SkipsBlockedMapLevelTilesAcrossSections()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 2, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 2, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);
            MapTile blocked = region.Sections[1].Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0);
            blocked.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            bool found = map.TryFindPath(
                new WorldMapPosition(15f, 5f),
                new WorldMapPosition(25f, 15f),
                out IReadOnlyList<PlacedMapTile> path);

            Assert.True(found);
            Assert.DoesNotContain(path, placed => ReferenceEquals(placed.Tile, blocked));
            AssertPlacedPath(
                path,
                (region.Sections[0], 1, 0),
                (region.Sections[0], 1, 1),
                (region.Sections[1], 0, 1));
        }

        [Fact]
        public void GetReachableTiles_ExcludesBlockedMapLevelTilesAcrossSections()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);
            region.Sections[1].Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0)
                .AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            IReadOnlyList<PlacedMapTile> reachable = map.GetReachableTiles(
                new WorldMapPosition(15f, 5f),
                movementBudget: 3);

            AssertPlacedPath(reachable, (region.Sections[0], 0, 0));
        }

        [Fact]
        public void GetReachableTiles_UsesPlacedTileStartAndMovementCosts()
        {
            var map = new WorldMap();
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);
            region.AddSection(MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 20, offsetZ: 0);
            map.AddRegion(region);
            MapTile expensiveTile = region.Sections[1].Section.Tiles.Single(tile => tile.GridX == 0 && tile.GridZ == 0);
            expensiveTile.AddAttribute(new MapAttribute { Id = "mud", ModifierKey = "movement", Amount = 2 });
            var start = new PlacedMapTile(region.Sections[0], region.Sections[0].Section.Tiles.Single(tile => tile.GridX == 1 && tile.GridZ == 0));

            IReadOnlyList<PlacedMapTile> budgetTwo = map.GetReachableTiles(start, movementBudget: 2);
            IReadOnlyList<PlacedMapTile> budgetThree = map.GetReachableTiles(start, movementBudget: 3);

            AssertPlacedPath(
                budgetTwo,
                (region.Sections[0], 0, 0));
            AssertPlacedPath(
                budgetThree,
                (region.Sections[0], 0, 0),
                (region.Sections[1], 0, 0));
        }

        [Fact]
        public void GetReachableTiles_ReturnsEmptyForMissingWorldStartOrNegativeBudget()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);
            MapTile tile = Assert.Single(Assert.Single(Assert.Single(map.Regions).Sections).Section.Tiles);
            var placedTile = new PlacedMapTile(Assert.Single(Assert.Single(map.Regions).Sections), tile);

            Assert.Empty(map.GetReachableTiles(new WorldMapPosition(100f, 100f), movementBudget: 1));
            Assert.Empty(map.GetReachableTiles(placedTile, movementBudget: -1));
        }

        [Fact]
        public void RectangleTileJsonRoundTrip_RestoresConcreteTile()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 20, offsetX: 3, offsetZ: 4);

            WorldMap loaded = WorldMap.FromJson(map.ToJson());

            MapTile tile = Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles);
            var rectTile = Assert.IsType<MapRectTile>(tile);
            Assert.Equal(MapTileShape.Rectangle, rectTile.Shape);
            Assert.Equal(10, rectTile.SizeX);
            Assert.Equal(20, rectTile.SizeY);
            Assert.Equal(new MapBounds(3, 4, 13, 24), rectTile.Bounds);
        }

        [Fact]
        public void LegacyWidthDepthJson_LoadsIntoSize()
        {
            const string json = """
            {
              "Id": "legacy",
              "Regions": [
                {
                  "Id": "forest",
                  "Sections": [
                    {
                      "OffsetX": 0,
                      "OffsetZ": 0,
                      "Section": {
                        "Width": 1,
                        "Depth": 1,
                        "Tiles": [
                          {
                            "GridX": 0,
                            "GridZ": 0,
                            "Shape": "Rectangle",
                            "X": 0,
                            "Z": 0,
                            "WorldX": 0,
                            "WorldZ": 0,
                            "Width": 8,
                            "Depth": 9,
                            "Attributes": []
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """;

            WorldMap loaded = WorldMap.FromJson(json);

            Assert.Equal(WorldMap.CurrentGeometryVersion, loaded.GeometryVersion);
            MapTile tile = Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles);
            Assert.IsType<MapRectTile>(tile);
            Assert.Equal(8, tile.SizeX);
            Assert.Equal(9, tile.SizeY);
            Assert.Equal(new MapBounds(0, 0, 8, 9), tile.Bounds);
        }

        [Fact]
        public void JsonRoundTrip_PreservesBlockingAttributesAndMovementCost()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);
            MapTile tile = Assert.Single(Assert.Single(Assert.Single(map.Regions).Sections).Section.Tiles);
            tile.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            WorldMap loaded = WorldMap.FromJson(map.ToJson());

            MapTile loadedTile = Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles);
            Assert.Equal(-1, loadedTile.MovementCost);
            Assert.Contains(loadedTile.Attributes, attribute => attribute.ModifierKey == "blocking");
        }

        [Fact]
        public void LegacyHexShapeJson_LoadsShapeAndOrientation()
        {
            const string json = """
            {
              "Id": "legacy-hex",
              "TileSettings": {
                "Shape": "HexFlatTop",
                "SizeX": 12.5,
                "SizeY": 15.5
              },
              "Regions": [
                {
                  "Id": "hex",
                  "Sections": [
                    {
                      "OffsetX": 0,
                      "OffsetZ": 0,
                      "Section": {
                        "Width": 2,
                        "Depth": 1,
                        "Tiles": [
                          {
                            "GridX": 0,
                            "GridZ": 0,
                            "Shape": "HexPointyTop",
                            "X": 0,
                            "Z": 0,
                            "WorldX": 0,
                            "WorldZ": 0,
                            "SizeX": 10,
                            "SizeY": 11,
                            "Attributes": []
                          },
                          {
                            "GridX": 1,
                            "GridZ": 0,
                            "Shape": "HexFlatTop",
                            "X": 10,
                            "Z": 0,
                            "WorldX": 10,
                            "WorldZ": 0,
                            "SizeX": 12,
                            "SizeY": 13,
                            "Attributes": []
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """;

            WorldMap loaded = WorldMap.FromJson(json);

            Assert.Equal(WorldMap.CurrentGeometryVersion, loaded.GeometryVersion);
            Assert.Equal(MapTileShape.Hex, loaded.TileSettings.Shape);
            Assert.Equal(HexOrientation.FlatTop, loaded.TileSettings.HexOrientation);
            List<MapTile> tiles = Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles;
            var pointyTile = Assert.IsType<MapHexTile>(tiles[0]);
            var flatTile = Assert.IsType<MapHexTile>(tiles[1]);
            Assert.Equal(MapTileShape.Hex, pointyTile.Shape);
            Assert.Equal(HexOrientation.PointyTop, pointyTile.Orientation);
            Assert.Equal(MapTileShape.Hex, flatTile.Shape);
            Assert.Equal(HexOrientation.FlatTop, flatTile.Orientation);
        }

        [Fact]
        public void FutureGeometryVersionJson_IsRejected()
        {
            string json = $$"""
            {
              "GeometryVersion": {{WorldMap.CurrentGeometryVersion + 1}},
              "Id": "future"
            }
            """;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => WorldMap.FromJson(json));
            Assert.Contains("newer than supported version", exception.Message);
        }

        [Theory]
        [InlineData(HexOrientation.PointyTop)]
        [InlineData(HexOrientation.FlatTop)]
        public void HexTileJsonRoundTrip_RestoresConcreteTile(HexOrientation orientation)
        {
            var map = new WorldMap();
            var section = new MapSection
            {
                Width = 1,
                Depth = 1,
                Tiles =
                [
                    new MapHexTile
                    {
                        GridX = 0,
                        GridZ = 0,
                        X = 0,
                        Z = 0,
                        WorldX = 0,
                        WorldZ = 0,
                        SizeX = 12,
                        SizeY = 14,
                        Orientation = orientation,
                        Coordinate = new HexCoord(3, -2)
                    }
                ]
            };
            var region = new MapRegion
            {
                Id = "hex",
                DisplayName = "Hex"
            };
            region.AddSection(section, offsetX: 5, offsetZ: 6);
            map.AddRegion(region);

            WorldMap loaded = WorldMap.FromJson(map.ToJson());

            MapTile tile = Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles);
            var hexTile = Assert.IsType<MapHexTile>(tile);
            Assert.Equal(MapTileShape.Hex, hexTile.Shape);
            Assert.Equal(orientation, hexTile.Orientation);
            Assert.Equal(new HexCoord(3, -2), hexTile.Coordinate);
            Assert.Equal(12, tile.SizeX);
            Assert.Equal(14, tile.SizeY);
            Assert.Equal(new MapBounds(5, 6, 17, 20), tile.Bounds);
        }

        [Fact]
        public void PlacedTiles_PreserveConcreteTypeShapeAndSize()
        {
            var section = new MapSection
            {
                Width = 2,
                Depth = 1,
                Tiles =
                [
                    new MapRectTile
                    {
                        GridX = 0,
                        GridZ = 0,
                        X = 0,
                        Z = 0,
                        WorldX = 0,
                        WorldZ = 0,
                        SizeX = 10,
                        SizeY = 11
                    },
                    new MapHexTile
                    {
                        GridX = 1,
                        GridZ = 0,
                        X = 10,
                        Z = 0,
                        WorldX = 10,
                        WorldZ = 0,
                        SizeX = 12,
                        SizeY = 13,
                        Orientation = HexOrientation.FlatTop,
                        Coordinate = new HexCoord(1, 0)
                    }
                ]
            };
            var region = new MapRegion
            {
                Id = "mixed",
                DisplayName = "Mixed"
            };

            region.AddSection(section, offsetX: 100, offsetZ: 200);

            MapTile rect = region.Sections.Single().Section.Tiles[0];
            Assert.IsType<MapRectTile>(rect);
            Assert.Equal(MapTileShape.Rectangle, rect.Shape);
            Assert.Equal(10, rect.SizeX);
            Assert.Equal(11, rect.SizeY);
            Assert.Equal(100, rect.WorldX);
            Assert.Equal(200, rect.WorldZ);

            MapTile hex = region.Sections.Single().Section.Tiles[1];
            var hexTile = Assert.IsType<MapHexTile>(hex);
            Assert.Equal(MapTileShape.Hex, hexTile.Shape);
            Assert.Equal(HexOrientation.FlatTop, hexTile.Orientation);
            Assert.Equal(new HexCoord(1, 0), hexTile.Coordinate);
            Assert.Equal(12, hexTile.SizeX);
            Assert.Equal(13, hexTile.SizeY);
            Assert.Equal(110, hexTile.WorldX);
            Assert.Equal(200, hexTile.WorldZ);
        }

        [Fact]
        public void AddSection_RejectsOverlappingPlacement()
        {
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 0, offsetZ: 0);

            Assert.Throws<InvalidOperationException>(() =>
                region.AddSection(MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 9, offsetZ: 0));
            region.AddSection(MapSection.CreateGrid(width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10), offsetX: 10, offsetZ: 0);
            Assert.Equal(2, region.Sections.Count);
        }

        [Fact]
        public void AddRegion_RejectsOverlappingRegionBounds()
        {
            var map = new WorldMap();
            map.AddRegion(CreateRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0));

            Assert.Throws<InvalidOperationException>(() =>
                map.AddRegion(CreateRegion("beach", "Beach", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 9, offsetZ: 0)));

            map.AddRegion(CreateRegion("beach", "Beach", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 10, offsetZ: 0));
            Assert.Equal(2, map.Regions.Count);
        }

        [Fact]
        public void TileSelection_TogglesAndDoesNotPersistInJson()
        {
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 1, depth: 1, tileSizeX: 10, tileSizeY: 10, offsetX: 0, offsetZ: 0);
            MapTile tile = Assert.Single(Assert.Single(Assert.Single(map.Regions).Sections).Section.Tiles);

            tile.Select();
            Assert.True(tile.IsSelected);

            WorldMap loaded = WorldMap.FromJson(map.ToJson());
            MapTile loadedTile = Assert.Single(Assert.Single(Assert.Single(loaded.Regions).Sections).Section.Tiles);
            Assert.False(loadedTile.IsSelected);

            tile.Unselect();
            Assert.False(tile.IsSelected);
        }

        [Fact]
        public async Task SaveAndLoadAsync_RoundTripsWorldMapFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"clan-iron-map-{Guid.NewGuid():N}.json");
            WorldMap map = CreateMapWithRegion("forest", "Forest", width: 2, depth: 1, tileSizeX: 3, tileSizeY: 4, offsetX: 11, offsetZ: 12);

            try
            {
                await map.SaveAsync(path);
                WorldMap loaded = await WorldMap.LoadAsync(path);

                MapRegion loadedRegion = Assert.Single(loaded.Regions);
                Assert.Equal("forest", loadedRegion.Id);
                PlacedMapSection placed = Assert.Single(loadedRegion.Sections);
                Assert.Equal(11, placed.OffsetX);
                Assert.Equal(12, placed.OffsetZ);
                Assert.Equal(2, placed.Section.Tiles.Count);
                Assert.DoesNotContain("Players", map.ToJson(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Profiles", map.ToJson(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Runtimes", map.ToJson(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static WorldMap CreateMapWithRegion(
            string id,
            string displayName,
            int width,
            int depth,
            int tileSizeX,
            int tileSizeY,
            int offsetX,
            int offsetZ)
        {
            var map = new WorldMap();
            map.AddRegion(CreateRegion(id, displayName, width, depth, tileSizeX, tileSizeY, offsetX, offsetZ));
            return map;
        }

        private static MapRegion CreateRegion(
            string id,
            string displayName,
            int width,
            int depth,
            int tileSizeX,
            int tileSizeY,
            int offsetX,
            int offsetZ)
        {
            var region = new MapRegion
            {
                Id = id,
                DisplayName = displayName
            };
            region.AddSection(MapSection.CreateGrid(width, depth, tileSizeX, tileSizeY), offsetX, offsetZ);
            return region;
        }

        private static WorldState CreateWorldStateWithEntities()
        {
            WorldState state = CreateEmptyWorldStateWithMap();
            state.Entities =
            [
                new Entity
                {
                    Id = "entity-001",
                    Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0)
                },
                new Entity
                {
                    Id = "entity-002",
                    Location = CreateEntityLocation(tileGridX: 1, tileGridZ: 0)
                }
            ];
            state.Interactables =
            [
                new InteractableEntity
                {
                    Id = "interactable-001",
                    Location = CreateEntityLocation(tileGridX: 0, tileGridZ: 0),
                    Attributes =
                    [
                        new EntityAttribute
                        {
                            Key = "rubble",
                            Subtype = "movement",
                            ValueKind = EntityAttributeValueKind.Number,
                            Value = "-1"
                        }
                    ]
                }
            ];
            return state;
        }

        private static WorldState CreateEmptyWorldStateWithMap()
        {
            var map = new WorldMap
            {
                Id = "map-001",
                Name = "First Map"
            };
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(
                MapSection.CreateGrid(width: 2, depth: 1, tileSizeX: 10, tileSizeY: 10),
                placedSectionId: "forest-0-0",
                gridX: 0,
                gridZ: 0,
                offsetX: 0,
                offsetZ: 0);
            map.AddRegion(region);

            return new WorldState
            {
                Id = "state-001",
                Name = "First State",
                Map = map
            };
        }

        private static WorldState CreateWorldStateForPathing()
        {
            var map = new WorldMap
            {
                Id = "map-pathing",
                Name = "Pathing Map"
            };
            var region = new MapRegion
            {
                Id = "forest",
                DisplayName = "Forest"
            };
            region.AddSection(
                MapSection.CreateGrid(width: 3, depth: 2, tileSizeX: 10, tileSizeY: 10),
                placedSectionId: "forest-0-0",
                gridX: 0,
                gridZ: 0,
                offsetX: 0,
                offsetZ: 0);
            map.AddRegion(region);

            return new WorldState
            {
                Id = "state-pathing",
                Name = "Pathing State",
                Map = map
            };
        }

        private static WorldState CreateWorldStateWithPointyHexMap()
        {
            var map = new WorldMap
            {
                Id = "map-hex",
                Name = "Hex Map"
            };
            var region = new MapRegion
            {
                Id = "hex",
                DisplayName = "Hex"
            };
            region.AddSection(
                MapSection.CreateHexGrid(
                    width: 1,
                    depth: 1,
                    tileSizeX: 10,
                    tileSizeY: 20,
                    orientation: HexOrientation.PointyTop),
                placedSectionId: "hex-0-0",
                gridX: 0,
                gridZ: 0,
                offsetX: 0,
                offsetZ: 0);
            map.AddRegion(region);

            return new WorldState
            {
                Id = "state-hex",
                Name = "Hex State",
                Map = map
            };
        }

        private static InteractableEntity CreateUsableInteractable(string id)
        {
            return new InteractableEntity
            {
                Id = id,
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "interaction",
                        Subtype = "usable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    }
                ]
            };
        }

        private static InteractableEntity CreateTargetableInteractable(string id)
        {
            return new InteractableEntity
            {
                Id = id,
                Attributes =
                [
                    new EntityAttribute
                    {
                        Key = "targeting",
                        Subtype = "targetable",
                        ValueKind = EntityAttributeValueKind.Boolean,
                        Value = "true"
                    }
                ]
            };
        }

        private static MapLocation CreateEntityLocation(int tileGridX, int tileGridZ)
        {
            return new MapLocation(
                regionId: "forest",
                placedSectionId: "forest-0-0",
                sectionGridX: 0,
                sectionGridZ: 0,
                tileGridX,
                tileGridZ,
                worldX: 5 + (tileGridX * 10),
                worldZ: 5 + (tileGridZ * 10));
        }

        private static void RegisterTestPlayer(GameOverlord overlord, int playerId)
        {
            Assert.True(overlord.Players.TryRegisterPlayer(new PlayerProfile
            {
                Id = playerId,
                DisplayName = $"Player {playerId}"
            }));
        }

        private static void AssertHexTile(
            MapTile tile,
            int gridX,
            int gridZ,
            HexCoord coordinate,
            float x,
            float z)
        {
            var hexTile = Assert.IsType<MapHexTile>(tile);
            Assert.Equal(gridX, hexTile.GridX);
            Assert.Equal(gridZ, hexTile.GridZ);
            Assert.Equal(coordinate, hexTile.Coordinate);
            Assert.Equal(x, hexTile.X);
            Assert.Equal(z, hexTile.Z);
            Assert.Equal(x, hexTile.WorldX);
            Assert.Equal(z, hexTile.WorldZ);
        }

        private static void AssertGridPositions(
            IReadOnlyList<MapTile> tiles,
            params (int X, int Z)[] expected)
        {
            Assert.Equal(expected.Length, tiles.Count);
            Assert.Equal(
                expected,
                tiles.Select(tile => (tile.GridX, tile.GridZ)).ToArray());
        }

        private static void AssertHexCoordinates(
            IReadOnlyList<MapTile> tiles,
            params HexCoord[] expected)
        {
            Assert.Equal(expected.Length, tiles.Count);
            Assert.Equal(
                expected,
                tiles.Select(tile => Assert.IsType<MapHexTile>(tile).Coordinate).ToArray());
        }

        private static void AssertPlacedPath(
            IReadOnlyList<PlacedMapTile> path,
            params (PlacedMapSection Section, int GridX, int GridZ)[] expected)
        {
            Assert.Equal(expected.Length, path.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Same(expected[i].Section, path[i].Section);
                Assert.Equal(expected[i].GridX, path[i].Tile.GridX);
                Assert.Equal(expected[i].GridZ, path[i].Tile.GridZ);
            }
        }

        [Fact]
        public async Task GameOverlord_FromWorldStateJsonRestoresPlayerNavigatorAndProcessesMoveCommand()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            state.Interactables.Add(new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            });
            Assert.True(state.Players.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duncan" }));
            Assert.True(state.Players.TryAssignNavigator(42, "player-nav"));

            GameOverlord overlord = GameOverlord.FromWorldStateJson(state.ToJson());

            Assert.True(overlord.TryGetPlayerNavigator(42, out NavigatorEntity? navigator));
            Assert.Equal("player-nav", navigator!.Id);
            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 42,
                    WorldPosition = new SimVector2(25, 5)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(42, action.PlayerId);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Starting, action.Move.Status);
            Assert.Equal("player-nav", action.Move.MoverId);
            Assert.Equal(42, action.Move.PlayerId);
            Assert.Equal(new SimVector2(25, 5), action.Move.DestinationWorldPosition);
        }

        [Fact]
        public async Task GameOverlord_LoadWorldStateAsyncReturnsReadyOverlord()
        {
            WorldState state = CreateWorldStateForPathing();
            MapLocation start = CreateEntityLocation(tileGridX: 0, tileGridZ: 0);
            state.Interactables.Add(new NavigatorEntity
            {
                Id = "player-nav",
                Location = start,
                CurrentWorldPosition = start.WorldPosition,
                MaxSpeed = 0
            });
            Assert.True(state.Players.TryRegisterPlayer(new PlayerProfile { Id = 42, DisplayName = "Duncan" }));
            Assert.True(state.Players.TryAssignNavigator(42, "player-nav"));
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.worldstate.json");

            try
            {
                await state.SaveAsync(path);

                GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(path);

                Assert.True(overlord.Players.TryGetProfile(42, out PlayerProfile? profile));
                Assert.Equal("Duncan", profile!.DisplayName);
                Assert.True(overlord.TryGetPlayerNavigator(42, out NavigatorEntity? navigator));
                Assert.Equal("player-nav", navigator!.Id);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public async Task AuthoredRectIslandWorldState_LoadsExpectedMapOnlySnapshot()
        {
            WorldState state = await WorldState.LoadAsync(GetWorldDataPath("test_rect_island_worldstate.json"));

            Assert.Equal("test rect island", state.Id);
            Assert.Equal("Test Rect Island", state.Name);
            Assert.Empty(state.Entities);
            Assert.Empty(state.Interactables);
            Assert.Empty(state.Players.Profiles);
            Assert.Empty(state.Players.Runtimes);
            Assert.Equal("test rect island map", state.Map.Id);
            Assert.Equal(MapTileShape.Rectangle, state.Map.TileSettings.Shape);
            Assert.Equal(1, state.Map.TileSettings.SizeX);
            Assert.Equal(1, state.Map.TileSettings.SizeY);

            MapRegion region = Assert.Single(state.Map.Regions);
            Assert.Equal("rect island", region.Id);
            Assert.Equal("Rect Island", region.DisplayName);
            PlacedMapSection placedSection = Assert.Single(region.Sections);
            Assert.Equal("rect_island_0_0", placedSection.Id);
            Assert.Equal(new SimIntVector2(0, 0), placedSection.GridPosition);
            Assert.Equal(new SimIntVector2(0, 0), placedSection.Offset);
            Assert.Equal(5, placedSection.Section.Width);
            Assert.Equal(5, placedSection.Section.Depth);
            Assert.Equal(25, placedSection.Section.Tiles.Count);
            Assert.All(placedSection.Section.Tiles, tile => Assert.IsType<MapRectTile>(tile));

            foreach (MapTile tile in placedSection.Section.Tiles)
            {
                bool isBorder = tile.GridX == 0 || tile.GridX == 4 || tile.GridZ == 0 || tile.GridZ == 4;
                Assert.Equal(isBorder ? -1 : 1, tile.MovementCost);
                Assert.Equal(isBorder, state.IsTileBlocked("rect island", "rect_island_0_0", tile.GridPosition));
            }

            MapTile spawner = Assert.Single(placedSection.Section.Tiles, HasPlayerSpawner);
            Assert.Equal(new SimIntVector2(2, 2), spawner.GridPosition);
            Assert.False(state.IsTileBlocked("rect island", "rect_island_0_0", spawner.GridPosition));
            Assert.True(state.TryWorldToMapLocation(new SimVector2(2.5f, 2.5f), out MapLocation center));
            Assert.Equal("rect island", center.RegionId);
            Assert.Equal("rect_island_0_0", center.PlacedSectionId);
            Assert.Equal(new SimIntVector2(2, 2), center.TileGrid);
            Assert.Equal(new SimVector2(2.5f, 2.5f), center.WorldPosition);
        }

        [Fact]
        public async Task AuthoredRectIslandWorldState_LoadsIntoOverlordAndSupportsInnerPathing()
        {
            GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));

            Assert.Empty(overlord.World.Entities);
            Assert.Empty(overlord.World.Interactables);
            Assert.Empty(overlord.Players.Profiles);
            Assert.Empty(overlord.Players.Runtimes);
            Assert.True(overlord.World.Map.TryGetPlacedTileAtWorldPosition(new SimVector2(1.5f, 1.5f), out PlacedMapTile start));
            Assert.True(overlord.World.Map.TryGetPlacedTileAtWorldPosition(new SimVector2(3.5f, 3.5f), out PlacedMapTile goal));
            Assert.True(overlord.World.TryFindPath(start, goal, out IReadOnlyList<PlacedMapTile> innerPath));
            Assert.NotEmpty(innerPath);
            Assert.Equal(1, innerPath[0].Tile.GridX);
            Assert.Equal(1, innerPath[0].Tile.GridZ);
            Assert.Equal(3, innerPath[^1].Tile.GridX);
            Assert.Equal(3, innerPath[^1].Tile.GridZ);

            Assert.True(overlord.World.Map.TryGetPlacedTileAtWorldPosition(new SimVector2(0.5f, 2.5f), out PlacedMapTile blockedGoal));
            Assert.False(overlord.World.TryFindPath(start, blockedGoal, out IReadOnlyList<PlacedMapTile> blockedPath));
            Assert.Empty(blockedPath);
        }

        [Fact]
        public async Task GameOverlord_TryAddPlayerCreatesNavigatorAtOpenSpawner()
        {
            GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));
            var profile = new PlayerProfile { Id = 7, DisplayName = "Scout" };

            Assert.True(overlord.TryAddPlayer(profile, out NavigatorEntity? navigator));

            Assert.NotNull(navigator);
            Assert.Equal("player_7_nav", navigator!.Id);
            Assert.Equal("rect island", navigator.Location.RegionId);
            Assert.Equal("rect_island_0_0", navigator.Location.PlacedSectionId);
            Assert.Equal(new SimIntVector2(2, 2), navigator.Location.TileGrid);
            Assert.Equal(new SimVector2(2.5f, 2.5f), navigator.Location.WorldPosition);
            Assert.Equal(navigator.Location.WorldPosition, navigator.CurrentWorldPosition);
            Assert.True(overlord.Players.TryGetProfile(7, out PlayerProfile? storedProfile));
            Assert.Equal("Scout", storedProfile!.DisplayName);
            Assert.True(overlord.TryGetPlayerNavigator(7, out NavigatorEntity? assignedNavigator));
            Assert.Same(navigator, assignedNavigator);
        }

        [Fact]
        public async Task GameOverlord_TryAddPlayerFailsWithoutMutationForDuplicateOrClosedSpawner()
        {
            GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));
            Assert.True(overlord.TryAddPlayer(new PlayerProfile { Id = 7, DisplayName = "Scout" }, out NavigatorEntity? firstNavigator));

            Assert.False(overlord.TryAddPlayer(new PlayerProfile { Id = 7, DisplayName = "Duplicate" }, out NavigatorEntity? duplicateNavigator));

            Assert.Null(duplicateNavigator);
            Assert.Single(overlord.Players.Profiles);
            Assert.Single(overlord.Players.Runtimes);
            Assert.Single(overlord.World.Interactables);
            Assert.Same(firstNavigator, overlord.World.Interactables[0]);

            GameOverlord closedSpawnOverlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));
            MapTile spawner = GetAuthoredSpawnerTile(closedSpawnOverlord.World);
            spawner.AddAttribute(new MapAttribute { Id = "wall", ModifierKey = "blocking", Amount = 0 });

            Assert.False(closedSpawnOverlord.TryAddPlayer(new PlayerProfile { Id = 8, DisplayName = "Blocked" }, out NavigatorEntity? blockedNavigator));

            Assert.Null(blockedNavigator);
            Assert.Empty(closedSpawnOverlord.Players.Profiles);
            Assert.Empty(closedSpawnOverlord.Players.Runtimes);
            Assert.Empty(closedSpawnOverlord.World.Interactables);
        }

        [Fact]
        public async Task GameOverlord_TryRemovePlayerClearsPlayerRuntimeAndLeavesNavigator()
        {
            GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));
            Assert.True(overlord.TryAddPlayer(new PlayerProfile { Id = 7, DisplayName = "Scout" }, out NavigatorEntity? navigator));

            Assert.True(overlord.TryRemovePlayer(7));

            Assert.False(overlord.Players.TryGetProfile(7, out _));
            Assert.False(overlord.Players.TryGetNavigatorId(7, out _));
            Assert.Single(overlord.World.Interactables);
            Assert.Same(navigator, overlord.World.Interactables[0]);
            Assert.False(overlord.TryRemovePlayer(7));
        }

        [Fact]
        public async Task GameOverlord_LoadedRectIslandCanAddPlayerAndEmitMoveAction()
        {
            GameOverlord overlord = await GameOverlord.LoadWorldStateAsync(GetWorldDataPath("test_rect_island_worldstate.json"));
            Assert.True(overlord.TryAddPlayer(new PlayerProfile { Id = 7, DisplayName = "Scout" }, out NavigatorEntity? navigator));

            overlord.EnqueueCommand(new SimCommand
            {
                Type = SimCommandType.PlayerMoveToWorldPosition,
                PlayerMoveToWorldPosition = new PlayerMoveToWorldPositionCommandPayload
                {
                    PlayerId = 7,
                    WorldPosition = new SimVector2(3.5f, 2.5f)
                }
            });

            IReadOnlyList<SimAction> actions = await overlord.UpdateAsync(0.1f);

            SimAction action = Assert.Single(actions);
            Assert.Equal(SimActionType.Move, action.Type);
            Assert.Equal(SimActionTarget.Region, action.Target);
            Assert.Equal("rect island", action.RegionId);
            Assert.Equal(7, action.PlayerId);
            Assert.NotNull(action.Move);
            Assert.Equal(MoveStatus.Starting, action.Move.Status);
            Assert.Equal(navigator!.Id, action.Move.MoverId);
            Assert.Equal(7, action.Move.PlayerId);
            Assert.Equal(new SimVector2(3.5f, 2.5f), action.Move.DestinationWorldPosition);
            Assert.Equal(
                new[] { new SimVector2(2.5f, 2.5f), new SimVector2(3.5f, 2.5f) },
                action.Move.PathWorldPositions);
        }

        private sealed class TestInteractableEntity : InteractableEntity
        {
        }

        private static string GetWorldDataPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Resources", "WorldData", fileName);
        }

        private static bool HasPlayerSpawner(MapTile tile)
        {
            return tile.Attributes.Any(attribute =>
                string.Equals(attribute.Id, "player spawner", StringComparison.OrdinalIgnoreCase)
                && string.Equals(attribute.ModifierKey, "spawner", StringComparison.OrdinalIgnoreCase));
        }

        private static MapTile GetAuthoredSpawnerTile(WorldState state)
        {
            MapRegion region = Assert.Single(state.Map.Regions);
            PlacedMapSection section = Assert.Single(region.Sections);
            return Assert.Single(section.Section.Tiles, HasPlayerSpawner);
        }

        private sealed class TestUpdatingInteractableEntity : InteractableEntity
        {
            private readonly List<string> _updateOrder;

            public TestUpdatingInteractableEntity(string id, List<string> updateOrder)
            {
                Id = id;
                _updateOrder = updateOrder;
            }

            public int UpdateCount { get; private set; }

            public float LastDeltaTime { get; private set; }

            public override void Update(float deltaTime, ISimEventDispatcher events)
            {
                UpdateCount++;
                LastDeltaTime = deltaTime;
                _updateOrder.Add(Id);
            }
        }

        private sealed class CommandEnqueueingInteractableEntity : InteractableEntity
        {
            private readonly GameOverlord _overlord;
            private readonly SimCommand _command;

            public CommandEnqueueingInteractableEntity(string id, GameOverlord overlord, SimCommand command)
            {
                Id = id;
                _overlord = overlord;
                _command = command;
            }

            public int UpdateCount { get; private set; }

            public override void Update(float deltaTime)
            {
                UpdateCount++;
                if (UpdateCount == 1)
                {
                    _overlord.EnqueueCommand(_command);
                }
            }
        }

        private sealed class RecordingSimEventDispatcher : ISimEventDispatcher
        {
            public List<SimEvent> Events { get; } = new();

            public void Dispatch(SimEvent simEvent)
            {
                Events.Add(simEvent);
            }
        }

        private sealed class ActionEmittingInteractableEntity : InteractableEntity
        {
            private readonly GameOverlord _overlord;
            private readonly SimAction _action;

            public ActionEmittingInteractableEntity(string id, GameOverlord overlord, SimAction action)
            {
                Id = id;
                _overlord = overlord;
                _action = action;
            }

            public int UpdateCount { get; private set; }

            public override void Update(float deltaTime)
            {
                UpdateCount++;
                if (UpdateCount == 1)
                {
                    _overlord.EmitAction(_action);
                }
            }
        }

        private sealed class TestNavigatorEntity : NavigatorEntity
        {
            public string PlayerTag { get; set; } = string.Empty;
        }

        private sealed class TestUpdatingNavigatorEntity : NavigatorEntity
        {
            private readonly List<string> _updateOrder;

            public TestUpdatingNavigatorEntity(string id, List<string> updateOrder)
            {
                Id = id;
                _updateOrder = updateOrder;
            }

            public int UpdateCount { get; private set; }

            public float LastDeltaTime { get; private set; }

            public override void Update(float deltaTime, ISimEventDispatcher events)
            {
                UpdateCount++;
                LastDeltaTime = deltaTime;
                _updateOrder.Add(Id);
            }
        }
    }
}
