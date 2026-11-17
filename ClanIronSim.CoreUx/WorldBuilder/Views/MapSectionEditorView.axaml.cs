using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClanIronSim.CoreUx.WorldBuilder.ViewModels;

namespace ClanIronSim.CoreUx.WorldBuilder.Views;

public partial class MapSectionEditorView : UserControl
{
    public MapSectionEditorView() => InitializeComponent();

    private async void LoadJsonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MapSectionEditorViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load section JSON", AllowMultiple = false, FileTypeFilter = new[] { JsonType } });
        if (files.Count == 0) return;
        try { await using Stream stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream); vm.LoadJson(await reader.ReadToEndAsync()); }
        catch (Exception exception) { vm.Status = $"Load failed: {exception.Message}"; }
    }

    private async void SaveJsonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MapSectionEditorViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save section JSON", SuggestedFileName = vm.Name.Replace(' ', '-').ToLowerInvariant() + ".json", DefaultExtension = "json", FileTypeChoices = new[] { JsonType } });
        if (file is null) return;
        try { await using Stream stream = await file.OpenWriteAsync(); await using var writer = new StreamWriter(stream); await writer.WriteAsync(vm.ToJson()); vm.Status = "Section JSON saved"; }
        catch (Exception exception) { vm.Status = $"Save failed: {exception.Message}"; }
    }

    private static readonly FilePickerFileType JsonType = new("JSON files") { Patterns = new[] { "*.json" }, MimeTypes = new[] { "application/json" } };
}
