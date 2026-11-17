using Avalonia.Controls;

namespace HawkNet.CoreUx.Common.Services;

/// <summary>
/// Describes an optional UI tool that can be contributed by a game integration.
/// A new control must be returned for every invocation.
/// </summary>
public interface ICoreUxTool
{
    string Id { get; }

    string DisplayName { get; }

    int Order { get; }

    Control CreateView(Action close);
}
