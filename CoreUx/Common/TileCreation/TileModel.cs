using System.Text.Json.Serialization;

namespace HawkNet.CoreUx.Common.TileCreation;

[JsonConverter(typeof(JsonStringEnumConverter<TileModel>))]
public enum TileModel
{
    Square,
    HexFlat,
    HexPoint
}
