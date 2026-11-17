using Avalonia.Controls;
using HawkNet.CoreUx.Common.LayerSelection;
using HawkNet.CoreUx.Common.TextureSelection;
using HawkNet.CoreUx.Common.TileCreation;
using Xunit;

namespace HawkNet.Tests;

public sealed class TileCreatorViewModelTests
{
    private static TileCreatorViewModel Create() => new(new TileCreatorContext(() => new Adapter()));
    [Theory]
    [InlineData(TileModel.Square)]
    [InlineData(TileModel.HexFlat)]
    [InlineData(TileModel.HexPoint)]
    public async Task Models_UpdateEveryPreviewAndRoundTripWithoutLosingLayers(TileModel model)
    {
        using var temp = new TempDirectory();
        string texture = Path.Combine(temp.Path, "surface.png");
        await File.WriteAllBytesAsync(texture, []);
        var requested = new List<TileModel>();
        using var vm = new TileCreatorViewModel(new TileCreatorContext(selected =>
        {
            requested.Add(selected);
            return new Adapter();
        }));
        await vm.InitializeAsync(); await vm.AddAsync(false);
        await vm.UpdateLayerAsync(vm.Layers[1], new(-1, texture, [new("Energy", "5"), new("Energy", "6")]));
        requested.Clear();
        await vm.SelectModelAsync(model);
        if (model != TileModel.HexFlat) Assert.Equal([model, model], requested);
        Assert.Equal(model, vm.CurrentDocument.Model);
        Assert.Equal(texture, vm.Layers[1].TexturePath);
        Assert.Equal(["5", "6"], vm.Layers[1].Attributes.Select(attribute => attribute.Value));
        await vm.AddAsync(true); Assert.Equal(model, requested.Last());
        string path = Path.Combine(temp.Path, "model.tile");
        Assert.True(await vm.SaveAsAsync(path));
        Assert.Contains($"\"Model\": \"{model}\"", await File.ReadAllTextAsync(path));
        requested.Clear(); Assert.True(await vm.LoadAsync(path));
        Assert.Equal([model, model, model], requested);
        Assert.Equal(model, vm.Model);
        Assert.Equal([1, 0, -1], vm.Layers.Select(row => row.LayerNumber));
    }

    [Fact]
    public async Task LegacyDocument_DefaultsToHexFlatAndInvalidModelPreservesState()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "legacy.tile");
        using var vm = new TileCreatorViewModel(new TileCreatorContext(_ => new Adapter()) { InitialModel = TileModel.HexPoint });
        await vm.InitializeAsync(); Assert.Equal(TileModel.HexPoint, vm.Model);
        await File.WriteAllTextAsync(path, "{\"Layers\":[{\"LayerNumber\":0,\"Attributes\":[]}]}");
        Assert.True(await vm.LoadAsync(path)); Assert.Equal(TileModel.HexFlat, vm.Model);
        var original = vm.Layers[0];
        await File.WriteAllTextAsync(path, "{\"Model\":\"Triangle\",\"Layers\":[{\"LayerNumber\":0,\"Attributes\":[]}]}");
        Assert.False(await vm.LoadAsync(path)); Assert.Same(original, vm.Layers[0]);
    }

    [Fact]
    public async Task FailedModelPreview_PreservesOldModelAndLayers()
    {
        using var vm = new TileCreatorViewModel(new TileCreatorContext(model => new Adapter { Reject = () => model == TileModel.Square }));
        await vm.InitializeAsync(); var original = vm.Layers[0];
        Assert.False(await vm.SelectModelAsync(TileModel.Square));
        Assert.Equal(TileModel.HexFlat, vm.Model); Assert.Same(original, vm.Layers[0]);
    }

    [Fact]
    public async Task DefaultAndRemove_AlwaysKeepLayerZero()
    {
        using var vm = Create(); await vm.InitializeAsync();
        Assert.Equal(0, Assert.Single(vm.Layers).LayerNumber);
        Assert.Empty(vm.Layers[0].Attributes); Assert.Null(vm.Layers[0].TexturePath);
        Assert.False(vm.Remove(vm.Layers[0])); Assert.Single(vm.Layers);
    }
    [Fact]
    public async Task AddMoveRemove_RenumberPositionsAndPreserveLayerContents()
    {
        using var vm = Create(); await vm.InitializeAsync();
        var zero = vm.Layers[0];
        await vm.UpdateLayerAsync(zero, new(0, null, [new("Energy", "base")]));
        await vm.AddAsync(true); await vm.AddAsync(false);
        Assert.Equal([1, 0, -1], vm.Layers.Select(row => row.LayerNumber));
        Assert.True(vm.Move(zero, 0)); Assert.Same(zero, vm.Layers[0]); Assert.Equal(1, zero.LayerNumber);
        Assert.Equal("base", zero.Attributes[0].Value);
        Assert.True(vm.Move(zero, 2)); Assert.Equal(-1, zero.LayerNumber);
        vm.Remove(vm.Layers[1]); Assert.Equal([0, -1], vm.Layers.Select(row => row.LayerNumber));
        vm.Remove(vm.Layers[0]); Assert.Equal(0, Assert.Single(vm.Layers).LayerNumber);
    }
    [Fact]
    public async Task ManyLayersAndAttributes_HaveNoFixedLimitAndSnapshotsKeepDuplicates()
    {
        using var vm = Create(); await vm.InitializeAsync();
        for (int i = 0; i < 100; i++) await vm.AddAsync(false);
        var attributes = Enumerable.Range(0, 75).Select(i => new LayerAttributeDefinition("Movement", i.ToString())).ToArray();
        await vm.UpdateLayerAsync(vm.Layers[0], new(0, null, attributes));
        var snapshot = vm.CurrentDocument;
        await vm.UpdateLayerAsync(vm.Layers[0], new(0, null, []));
        Assert.Equal(101, snapshot.Layers.Count); Assert.Equal(75, snapshot.Layers[0].Attributes.Count);
        Assert.Equal("74", snapshot.Layers[0].Attributes[74].Value);
    }
    [Fact]
    public async Task SaveLoad_RoundTripsSignedOrderAttributesAndRelativeTexturePaths()
    {
        using var temp = new TempDirectory();
        string texture = Path.Combine(temp.Path, "grass.png"); await File.WriteAllBytesAsync(texture, []);
        string tile = Path.Combine(temp.Path, "forest.tile");
        using var vm = Create(); await vm.InitializeAsync(); await vm.AddAsync(false);
        await vm.UpdateLayerAsync(vm.Layers[1], new(-1, texture, [new("Energy", "five"), new("Energy", "six")]));
        TileSavedEventArgs? saved = null; vm.Saved += (_, args) => saved = args;
        Assert.True(await vm.SaveAsAsync(tile)); Assert.Equal("forest.tile", vm.HeaderText);
        Assert.Contains("grass.png", await File.ReadAllTextAsync(tile));
        Assert.DoesNotContain(texture.Replace("\\", "\\\\"), await File.ReadAllTextAsync(tile));
        Assert.NotNull(saved); Assert.Equal(texture, saved.Document.Layers[1].TexturePath);
        using var loaded = Create(); await loaded.InitializeAsync(); Assert.True(await loaded.LoadAsync(tile));
        Assert.Equal([0, -1], loaded.Layers.Select(row => row.LayerNumber));
        Assert.Equal(texture, loaded.Layers[1].TexturePath);
        Assert.Equal(["five", "six"], loaded.Layers[1].Attributes.Select(attribute => attribute.Value));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Version\":99,\"Layers\":[]}")]
    [InlineData("{\"Layers\":[{\"LayerNumber\":2,\"Attributes\":[]},{\"LayerNumber\":0,\"Attributes\":[]}]}")]
    [InlineData("{\"Layers\":[{\"LayerNumber\":0,\"Attributes\":null}]}")]
    public async Task InvalidLoad_PreservesExistingState(string json)
    {
        using var temp = new TempDirectory(); string path = Path.Combine(temp.Path, "invalid.tile"); await File.WriteAllTextAsync(path, json);
        using var vm = Create(); await vm.InitializeAsync(); var original = vm.Layers[0];
        Assert.False(await vm.LoadAsync(path)); Assert.Same(original, Assert.Single(vm.Layers)); Assert.Null(vm.FilePath);
        Assert.Contains("could not be loaded", vm.StatusMessage);
    }
    [Fact]
    public async Task MissingTextureLoad_PreservesState()
    {
        using var temp = new TempDirectory(); string path = Path.Combine(temp.Path, "missing.tile");
        await File.WriteAllTextAsync(path, "{\"Layers\":[{\"LayerNumber\":0,\"TexturePath\":\"missing.png\",\"Attributes\":[]}]}");
        using var vm = Create(); await vm.InitializeAsync(); var original = vm.Layers[0];
        Assert.False(await vm.LoadAsync(path)); Assert.Same(original, vm.Layers[0]);
    }
    [Fact]
    public async Task AdapterFailure_PreservesPreviousLayerAndFailedAddDoesNotChangeNumbers()
    {
        bool reject = false;
        using var vm = new TileCreatorViewModel(new TileCreatorContext(() => new Adapter { Reject = () => reject }));
        await vm.InitializeAsync(); reject = true;
        Assert.False(await vm.AddAsync(true)); Assert.Equal(0, Assert.Single(vm.Layers).LayerNumber);
        Assert.False(await vm.UpdateLayerAsync(vm.Layers[0], new(0, null, [new("Energy", "5")])));
        Assert.Empty(vm.Layers[0].Attributes);
    }
    [Fact]
    public async Task FailedSave_DoesNotEstablishFilePath()
    {
        using var temp = new TempDirectory(); using var vm = Create(); await vm.InitializeAsync();
        Assert.False(await vm.SaveAsAsync(Path.Combine(temp.Path, "missing", "tile.tile"))); Assert.Null(vm.FilePath);
    }
    private sealed class Adapter : ITexturePreviewAdapter
    {
        public Control PreviewControl { get; } = new Border();
        public Func<bool>? Reject { get; init; }
        public ValueTask ApplyTextureAsync(string? path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Reject?.Invoke() == true) throw new InvalidOperationException("Preview failed.");
            return ValueTask.CompletedTask;
        }
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hawknet-tile-tests-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
