using System.Text.Json;
using Avalonia.Controls;
using HawkNet.CoreUx.Common.TextureSelection;
using Xunit;

namespace HawkNet.Tests;

public sealed class TextureSelectorViewModelTests
{
    [Fact]
    public async Task DirectoryScan_FiltersSortsAndKeepsNoneFirst()
    {
        using var temp = new TemporaryDirectory();
        temp.Touch("zeta.webp");
        temp.Touch("Alpha.PNG");
        temp.Touch("middle.jpg");
        temp.Touch("ignored.gif");
        temp.Touch("notes.txt");
        var adapter = new RecordingPreviewAdapter();
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(adapter));

        bool loaded = await viewModel.ChooseDirectoryAsync(temp.Path);

        Assert.True(loaded);
        Assert.Equal(["None", "Alpha.PNG", "middle.jpg", "zeta.webp"], viewModel.Materials.Select(item => item.DisplayName));
        Assert.Null(viewModel.SelectedTexturePath);
        Assert.Null(adapter.AppliedPaths.Single());
    }

    [Fact]
    public async Task SelectingMaterialAndNone_UpdatesPreviewImmediately()
    {
        using var temp = new TemporaryDirectory();
        string texturePath = temp.Touch("forest.png");
        var adapter = new RecordingPreviewAdapter();
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(adapter));
        await viewModel.ChooseDirectoryAsync(temp.Path);

        bool selected = await viewModel.SelectMaterialAsync(viewModel.Materials.Single(item => item.DisplayName == "forest.png"));
        bool removed = await viewModel.SelectMaterialAsync(viewModel.Materials[0]);

        Assert.True(selected);
        Assert.True(removed);
        Assert.Equal([null, System.IO.Path.GetFullPath(texturePath), null], adapter.AppliedPaths);
        Assert.Null(viewModel.SelectedTexturePath);
    }

    [Fact]
    public async Task PreviewFailure_RestoresPreviousSelection()
    {
        using var temp = new TemporaryDirectory();
        string texturePath = temp.Touch("broken.png");
        var adapter = new RecordingPreviewAdapter { RejectedPath = System.IO.Path.GetFullPath(texturePath) };
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(adapter));
        await viewModel.ChooseDirectoryAsync(temp.Path);

        bool selected = await viewModel.SelectMaterialAsync(viewModel.Materials[1]);

        Assert.False(selected);
        Assert.Null(viewModel.SelectedTexturePath);
        Assert.True(viewModel.Materials[0].IsSelected);
        Assert.Contains("Preview update failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsRelativeTexturePath()
    {
        using var temp = new TemporaryDirectory();
        string texturePath = temp.Touch("forest_ground.png");
        string materialPath = System.IO.Path.Combine(temp.Path, "forest_hex.material");
        var firstAdapter = new RecordingPreviewAdapter();
        using (var first = new TextureSelectorViewModel(new TextureSelectorContext(firstAdapter)))
        {
            await first.ChooseDirectoryAsync(temp.Path);
            await first.SelectMaterialAsync(first.Materials[1]);
            Assert.True(await first.SaveAsAsync(materialPath));
            Assert.True(first.HasFilePath);
            Assert.Equal("forest_hex.material", first.HeaderText);
        }

        TextureSelectionDocument? saved = JsonSerializer.Deserialize<TextureSelectionDocument>(await File.ReadAllTextAsync(materialPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(saved);
        Assert.Equal("forest_ground.png", saved.SelectedTexture);

        var secondAdapter = new RecordingPreviewAdapter();
        using var second = new TextureSelectorViewModel(new TextureSelectorContext(secondAdapter) { FilePath = materialPath });
        await second.InitializeAsync();

        Assert.Equal(System.IO.Path.GetFullPath(texturePath), second.SelectedTexturePath);
        Assert.Equal(System.IO.Path.GetFullPath(texturePath), secondAdapter.AppliedPaths.Single());
        Assert.True(second.HasFilePath);
    }

    [Fact]
    public async Task MalformedDocument_ReportsErrorWithoutReplacingCurrentState()
    {
        using var temp = new TemporaryDirectory();
        string malformed = System.IO.Path.Combine(temp.Path, "bad.material");
        await File.WriteAllTextAsync(malformed, "{ definitely-not-json }");
        var adapter = new RecordingPreviewAdapter();
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(adapter));

        bool loaded = await viewModel.LoadAsync(malformed);

        Assert.False(loaded);
        Assert.False(viewModel.HasFilePath);
        Assert.Null(viewModel.SelectedTexturePath);
        Assert.Contains("Load failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MissingSavedTexture_SelectsNoneAndReportsWarning()
    {
        using var temp = new TemporaryDirectory();
        string materialPath = System.IO.Path.Combine(temp.Path, "missing.material");
        await File.WriteAllTextAsync(materialPath, $$"""
        {
          "version": 1,
          "textureDirectory": {{JsonSerializer.Serialize(temp.Path)}},
          "selectedTexture": "gone.png"
        }
        """);
        var adapter = new RecordingPreviewAdapter();
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(adapter));

        bool loaded = await viewModel.LoadAsync(materialPath);

        Assert.True(loaded);
        Assert.Null(viewModel.SelectedTexturePath);
        Assert.Contains("missing", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(adapter.AppliedPaths.Single());
    }

    [Fact]
    public void CloseCommand_RaisesCloseRequest()
    {
        using var viewModel = new TextureSelectorViewModel(new TextureSelectorContext(new RecordingPreviewAdapter()));
        int raised = 0;
        viewModel.CloseRequested += (_, _) => raised++;

        viewModel.CloseCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    private sealed class RecordingPreviewAdapter : ITexturePreviewAdapter
    {
        public Control PreviewControl { get; } = new Border();

        public List<string?> AppliedPaths { get; } = new();

        public string? RejectedPath { get; init; }

        public ValueTask ApplyTextureAsync(string? texturePath, CancellationToken cancellationToken = default)
        {
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hawknet-texture-selector-{Guid.NewGuid():N}");
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
