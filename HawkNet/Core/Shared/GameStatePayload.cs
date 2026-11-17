namespace HawkNet.Core.Shared
{
	public interface IGameStatePayloadProvider
	{
		byte[] CreatePayloadForPlayer(int playerId, IPayloadCodec codec);
	}
}
