using Avalonia.Controls;
using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.TextureSelection;
using Xunit;

namespace HawkNet.Tests;

public sealed class LayerSelectionViewModelTests
{
    [Fact]
    public async Task DefaultState_HasNoAttributesOrTexture()
    {
        var adapter = new RecordingPreviewAdapter();
        var viewModel = CreateViewModel(adapter);

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.Attributes);
        Assert.Equal("0 attributes", viewModel.AttributeCountText);
        Assert.Null(viewModel.SelectedTexturePath);
        Assert.Equal("None", viewModel.MaterialName);
        Assert.Null(adapter.AppliedPaths.Single());
    }

    [Fact]
    public void InitialState_NormalizesTypesAndPreservesOrder()
    {
        var adapter = new RecordingPreviewAdapter();
        var viewModel = CreateViewModel(adapter,
            new LayerAttributeDefinition("energy", "5"),
            new LayerAttributeDefinition("Unknown", "custom"));

        Assert.Equal("Layer 7", viewModel.HeaderText);
        Assert.Equal(["Energy", "Movement"], viewModel.Attributes.Select(attribute => attribute.Type));
        Assert.Equal(["5", "custom"], viewModel.Attributes.Select(attribute => attribute.Value));
        Assert.Equal("2 attributes", viewModel.AttributeCountText);
    }

    [Fact]
    public void AddRemoveAndMove_MaintainOrderedAttributeList()
    {
        var viewModel = CreateViewModel(new RecordingPreviewAdapter(),
            new LayerAttributeDefinition("Movement", "one"),
            new LayerAttributeDefinition("Energy", "two"));

        viewModel.AddAttribute();
        LayerAttributeItemViewModel added = viewModel.Attributes[2];
        added.Type = "HitPoints";
        added.Value = "three";
        Assert.True(viewModel.MoveAttribute(added, 0));
        Assert.True(viewModel.RemoveAttribute(viewModel.Attributes[2]));

        Assert.Equal(["HitPoints", "Movement"], viewModel.Attributes.Select(attribute => attribute.Type));
        Assert.Equal(["three", "one"], viewModel.Attributes.Select(attribute => attribute.Value));
        Assert.Equal("2 attributes", viewModel.AttributeCountText);
    }

    [Fact]
    public void MoveAttribute_CanCrossMultipleSlotsInOneOperation()
    {
        var viewModel = CreateViewModel(new RecordingPreviewAdapter(),
            new LayerAttributeDefinition("Movement", "first"),
            new LayerAttributeDefinition("Energy", "second"),
            new LayerAttributeDefinition("HitPoints", "third"));

        Assert.True(viewModel.MoveAttribute(viewModel.Attributes[0], 2));

        Assert.Equal(["second", "third", "first"], viewModel.Attributes.Select(attribute => attribute.Value));
    }

    [Fact]
    public async Task MaterialSelection_UpdatesPreviewAndCurrentLayer()
    {
        using var temp = new TemporaryDirectory();
        string texture = temp.Touch("forest.png");
        var adapter = new RecordingPreviewAdapter();
        var viewModel = CreateViewModel(adapter);

        Assert.True(await viewModel.ApplyTextureAsync(texture));

        Assert.Equal(Path.GetFullPath(texture), adapter.AppliedPaths.Single());
        Assert.Equal(Path.GetFullPath(texture), viewModel.CurrentLayer.TexturePath);
        Assert.Equal("forest.png", viewModel.MaterialName);
    }

    [Fact]
    public async Task Initialization_AppliesTextureWithoutReplacingDefaultFooterMessage()
    {
        using var temp = new TemporaryDirectory();
        string texture = temp.Touch("forest.png");
        var adapter = new RecordingPreviewAdapter();
        var viewModel = new LayerSelectionViewModel(new LayerSelectionContext(0, adapter, () => new RecordingPreviewAdapter())
        {
            InitialTexturePath = texture
        });

        await viewModel.InitializeAsync();

        Assert.Equal(Path.GetFullPath(texture), adapter.AppliedPaths.Single());
        Assert.Equal("Layer details return when closed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PreviewFailure_RestoresPreviousMaterial()
    {
        using var temp = new TemporaryDirectory();
        string previous = temp.Touch("previous.png");
        string rejected = temp.Touch("rejected.png");
        var adapter = new RecordingPreviewAdapter { RejectedPath = Path.GetFullPath(rejected) };
        var viewModel = CreateViewModel(adapter);
        Assert.True(await viewModel.ApplyTextureAsync(previous));

        Assert.False(await viewModel.ApplyTextureAsync(rejected));

        Assert.Equal(Path.GetFullPath(previous), viewModel.SelectedTexturePath);
        Assert.Contains("preview update failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateAndClose_EmitImmutableOrderedSnapshots()
    {
        var viewModel = CreateViewModel(new RecordingPreviewAdapter(),
            new LayerAttributeDefinition("Movement", "1"));
        var events = new List<LayerUpdatedEventArgs>();
        int closeRequests = 0;
        viewModel.Updated += (_, args) => events.Add(args);
        viewModel.CloseRequested += (_, _) => closeRequests++;

        viewModel.Commit(LayerUpdateReason.Update);
        viewModel.Attributes[0].Value = "changed";
        viewModel.RequestClose();

        Assert.Equal([LayerUpdateReason.Update, LayerUpdateReason.Close], events.Select(item => item.Reason));
        Assert.Equal("1", events[0].Layer.Attributes[0].Value);
        Assert.Equal("changed", events[1].Layer.Attributes[0].Value);
        Assert.Equal(1, closeRequests);
    }

    [Fact]
    public void Context_AcceptsSignedLayersAndRejectsNullDependencies()
    {
        var adapter = new RecordingPreviewAdapter();

        Assert.Equal(-1, new LayerSelectionContext(-1, adapter, () => adapter).LayerNumber);
        Assert.Throws<ArgumentNullException>(() => new LayerSelectionContext(0, null!, () => adapter));
        Assert.Throws<ArgumentNullException>(() => new LayerSelectionContext(0, adapter, null!));
    }

    private static LayerSelectionViewModel CreateViewModel(
        RecordingPreviewAdapter adapter,
        params LayerAttributeDefinition[] attributes) =>
        new(new LayerSelectionContext(7, adapter, () => new RecordingPreviewAdapter())
        {
            InitialAttributes = attributes
        });

    private sealed class RecordingPreviewAdapter : ITexturePreviewAdapter
    {
        public Control PreviewControl { get; } = new Border();

        public List<string?> AppliedPaths { get; } = [];

        public string? RejectedPath { get; init; }

        public ValueTask ApplyTextureAsync(string? texturePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RejectedPath is not null && string.Equals(RejectedPath, texturePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Renderer rejected texture.");
            }

            AppliedPaths.Add(texturePath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hawknet-layer-selector-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Touch(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
