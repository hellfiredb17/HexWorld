namespace HawkNet.CoreUx.Client.World;

public sealed class WorldMoveRequestEventArgs : EventArgs
{
    public WorldMoveRequestEventArgs(WorldTileViewModel tile)
    {
        Tile = tile ?? throw new ArgumentNullException(nameof(tile));
    }

    public WorldTileViewModel Tile { get; }

    public WorldPoint Destination => new(Tile.CenterX, Tile.CenterZ);
}
