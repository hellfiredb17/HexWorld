namespace HawkNet.CoreUx.Common.LayerSelection;

public enum LayerUpdateReason
{
    Update,
    Close
}

public sealed class LayerUpdatedEventArgs : EventArgs
{
    public LayerUpdatedEventArgs(LayerSelectionDefinition layer, LayerUpdateReason reason)
    {
        Layer = layer;
        Reason = reason;
    }

    public LayerSelectionDefinition Layer { get; }

    public LayerUpdateReason Reason { get; }
}
