using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClanIronSim.CoreUx.WorldBuilder.ViewModels;

namespace ClanIronSim.CoreUx.WorldBuilder.Views;

public partial class MapTileEditorView : UserControl
{
    public MapTileEditorView() => InitializeComponent();

    private async void LoadJsonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MapTileEditorViewModel viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load tile JSON", AllowMultiple = false, FileTypeFilter = new[] { JsonType } });
        if (files.Count == 0) return;
        try { await using Stream stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream); viewModel.LoadJson(await reader.ReadToEndAsync()); }
        catch (Exception exception) { viewModel.StatusMessage = $"Load failed: {exception.Message}"; }
    }

    private async void SaveJsonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MapTileEditorViewModel viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save tile JSON", SuggestedFileName = Slug(viewModel.Name) + ".json", DefaultExtension = "json", FileTypeChoices = new[] { JsonType } });
        if (file is null) return;
        try { await using Stream stream = await file.OpenWriteAsync(); await using var writer = new StreamWriter(stream); await writer.WriteAsync(viewModel.ToJson()); viewModel.StatusMessage = "Tile JSON saved"; }
        catch (Exception exception) { viewModel.StatusMessage = $"Save failed: {exception.Message}"; }
    }

    private void ApplyTileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MapTileEditorViewModel viewModel)
        {
            viewModel.ApplyTile();
        }
    }

    private static string Slug(string value) => string.Join('-', value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim().Replace(' ', '-').ToLowerInvariant();
    private static readonly FilePickerFileType JsonType = new("JSON files") { Patterns = new[] { "*.json" }, MimeTypes = new[] { "application/json" } };
}
