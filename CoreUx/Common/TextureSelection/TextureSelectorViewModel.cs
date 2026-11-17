using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.TextureSelection;

internal sealed class TextureSelectorViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TextureSelectorContext _context;
    private string? _filePath;
    private string? _textureDirectory;
    private TextureMaterialItemViewModel? _selectedMaterial;
    private string _statusMessage = "Ready";
    private bool _isBusy;
    private bool _disposed;

    public TextureSelectorViewModel(TextureSelectorContext context)
    {
        _context = context;
        _filePath = NormalizeOptionalPath(context.FilePath);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        Materials.Add(CreateNoneItem());
        SetSelected(Materials[0]);
    }

    public Control PreviewControl => _context.PreviewAdapter.PreviewControl;

    public string PreviewInteractionHint => _context.PreviewInteractionHint ?? string.Empty;

    public bool HasPreviewInteractionHint => !string.IsNullOrWhiteSpace(PreviewInteractionHint);

    public ObservableCollection<TextureMaterialItemViewModel> Materials { get; } = new();

    public RelayCommand CloseCommand { get; }

    public string HeaderText => string.IsNullOrWhiteSpace(_filePath)
        ? "Texture Selector"
        : Path.GetFileName(_filePath);

    public string DirectoryDisplayText => string.IsNullOrWhiteSpace(_textureDirectory)
        ? "No directory selected"
        : _textureDirectory;

    public string? FilePath => _filePath;

    public bool HasFilePath => !string.IsNullOrWhiteSpace(_filePath);

    public string? SelectedTexturePath => _selectedMaterial?.FilePath;

    public TextureSelectionDocument CurrentDocument => CreateDocument();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public event EventHandler? CloseRequested;

    public event EventHandler<TextureSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TextureSelectionSavedEventArgs>? SelectionSaved;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_filePath is not null && File.Exists(_filePath))
        {
            await LoadAsync(_filePath, cancellationToken);
            return;
        }

        string? directory = NormalizeOptionalPath(_context.InitialDirectory);
        string? texturePath = ResolveTexturePath(directory, _context.InitialTexturePath);
        if (directory is null && texturePath is not null)
        {
            directory = Path.GetDirectoryName(texturePath);
        }

        if (directory is not null)
        {
            await PopulateDirectoryAsync(directory, texturePath, cancellationToken);
        }
        else
        {
            await ApplyInitialNoneAsync(cancellationToken);
        }

        if (_filePath is not null && !File.Exists(_filePath))
        {
            StatusMessage = $"Save target '{Path.GetFileName(_filePath)}' does not exist yet.";
        }
    }

    public async Task<bool> ChooseDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        string? normalized = NormalizeOptionalPath(directoryPath);
        if (normalized is null || !Directory.Exists(normalized))
        {
            StatusMessage = "Directory not found.";
            return false;
        }

        return await PopulateDirectoryAsync(normalized, selectedTexturePath: null, cancellationToken);
    }

    public async Task<bool> SelectMaterialAsync(
        TextureMaterialItemViewModel material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (IsBusy || ReferenceEquals(material, _selectedMaterial))
        {
            return true;
        }

        TextureMaterialItemViewModel? previous = _selectedMaterial;
        SetSelected(material);
        IsBusy = true;
        try
        {
            await _context.PreviewAdapter.ApplyTextureAsync(material.FilePath, cancellationToken);
            StatusMessage = material.IsNone
                ? "Material removed from preview."
                : $"Previewing {material.DisplayName}.";
            SelectionChanged?.Invoke(this, new TextureSelectionChangedEventArgs(material.FilePath));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (previous is not null)
            {
                SetSelected(previous);
            }

            StatusMessage = $"Preview update failed: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string? normalized = NormalizeOptionalPath(filePath);
        if (normalized is null)
        {
            StatusMessage = "Material file path is invalid.";
            return false;
        }

        IsBusy = true;
        try
        {
            string json = await File.ReadAllTextAsync(normalized, cancellationToken);
            TextureSelectionDocument document = JsonSerializer.Deserialize<TextureSelectionDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("The material document is empty.");
            if (document.Version != TextureSelectionDocument.CurrentVersion)
            {
                throw new InvalidDataException($"Unsupported material document version {document.Version}.");
            }

            SetFilePath(normalized);
            string? directory = NormalizeOptionalPath(document.TextureDirectory);
            string? selectedPath = ResolveTexturePath(directory, document.SelectedTexture);
            if (directory is null)
            {
                await ReplaceWithNoneAsync(cancellationToken);
                StatusMessage = $"Loaded {Path.GetFileName(normalized)}. No texture directory is selected.";
                return true;
            }

            bool populated = await PopulateDirectoryCoreAsync(directory, selectedPath, cancellationToken);
            if (populated && string.IsNullOrWhiteSpace(StatusMessage))
            {
                StatusMessage = $"Loaded {Path.GetFileName(normalized)}.";
            }

            return populated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Load failed: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_filePath is null)
        {
            StatusMessage = "Choose Save As before saving this material selection.";
            return Task.FromResult(false);
        }

        return SaveAsAsync(_filePath, cancellationToken);
    }

    public async Task<bool> SaveAsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string? normalized = NormalizeOptionalPath(filePath);
        if (normalized is null)
        {
            StatusMessage = "Material file path is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
        {
            normalized += ".material";
        }

        IsBusy = true;
        try
        {
            TextureSelectionDocument document = CreateDocument();
            string json = JsonSerializer.Serialize(document, JsonOptions);
            await File.WriteAllTextAsync(normalized, json, cancellationToken);
            SetFilePath(normalized);
            StatusMessage = $"Saved {Path.GetFileName(normalized)}.";
            SelectionSaved?.Invoke(this, new TextureSelectionSavedEventArgs(normalized, document));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Save failed: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> PopulateDirectoryAsync(
        string directoryPath,
        string? selectedTexturePath,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            return await PopulateDirectoryCoreAsync(directoryPath, selectedTexturePath, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> PopulateDirectoryCoreAsync(
        string directoryPath,
        string? selectedTexturePath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath))
        {
            await ReplaceWithNoneAsync(cancellationToken);
            SetTextureDirectory(directoryPath);
            StatusMessage = $"Texture directory not found: {directoryPath}";
            return false;
        }

        var newItems = new List<TextureMaterialItemViewModel> { CreateNoneItem() };
        int thumbnailFailures = 0;
        try
        {
            string[] paths = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Bitmap? thumbnail = TryLoadThumbnail(path);
                if (thumbnail is null)
                {
                    thumbnailFailures++;
                }

                newItems.Add(new TextureMaterialItemViewModel(Path.GetFileName(path), Path.GetFullPath(path), thumbnail));
            }
        }
        catch
        {
            foreach (TextureMaterialItemViewModel item in newItems)
            {
                item.Dispose();
            }

            throw;
        }

        ReplaceMaterials(newItems);
        SetTextureDirectory(Path.GetFullPath(directoryPath));

        TextureMaterialItemViewModel target = Materials[0];
        if (selectedTexturePath is not null)
        {
            string normalizedSelection = Path.GetFullPath(selectedTexturePath);
            target = Materials.FirstOrDefault(item =>
                item.FilePath is not null && PathComparer.Equals(item.FilePath, normalizedSelection)) ?? Materials[0];
        }

        SetSelected(Materials[0]);
        try
        {
            await _context.PreviewAdapter.ApplyTextureAsync(target.FilePath, cancellationToken);
            SetSelected(target);
            SelectionChanged?.Invoke(this, new TextureSelectionChangedEventArgs(target.FilePath));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Preview update failed: {exception.Message}";
            return false;
        }

        if (selectedTexturePath is not null && target.IsNone)
        {
            StatusMessage = "The saved texture is missing; None is selected.";
        }
        else if (thumbnailFailures > 0)
        {
            StatusMessage = $"Loaded {CountTextureItems(newItems)} textures; {thumbnailFailures} thumbnail(s) could not be read.";
        }
        else
        {
            StatusMessage = $"Loaded {CountTextureItems(newItems)} texture(s).";
        }

        return true;
    }

    private async Task ReplaceWithNoneAsync(CancellationToken cancellationToken)
    {
        ReplaceMaterials(new List<TextureMaterialItemViewModel> { CreateNoneItem() });
        SetTextureDirectory(null);
        SetSelected(Materials[0]);
        try
        {
            await _context.PreviewAdapter.ApplyTextureAsync(null, cancellationToken);
            SelectionChanged?.Invoke(this, new TextureSelectionChangedEventArgs(null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Preview update failed: {exception.Message}";
        }
    }

    private async Task ApplyInitialNoneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.PreviewAdapter.ApplyTextureAsync(null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"Preview update failed: {exception.Message}";
        }
    }

    private TextureSelectionDocument CreateDocument()
    {
        string? selected = SelectedTexturePath;
        if (_textureDirectory is not null && selected is not null)
        {
            string relative = Path.GetRelativePath(_textureDirectory, selected);
            selected = Path.IsPathRooted(relative) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? selected
                : relative;
        }

        return new TextureSelectionDocument
        {
            Version = TextureSelectionDocument.CurrentVersion,
            TextureDirectory = _textureDirectory,
            SelectedTexture = selected
        };
    }

    private static Bitmap? TryLoadThumbnail(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 96);
        }
        catch
        {
            return null;
        }
    }

    private void ReplaceMaterials(IReadOnlyList<TextureMaterialItemViewModel> newItems)
    {
        foreach (TextureMaterialItemViewModel item in Materials)
        {
            item.Dispose();
        }

        Materials.Clear();
        foreach (TextureMaterialItemViewModel item in newItems)
        {
            Materials.Add(item);
        }
    }

    private void SetSelected(TextureMaterialItemViewModel material)
    {
        if (_selectedMaterial is not null)
        {
            _selectedMaterial.IsSelected = false;
        }

        _selectedMaterial = material;
        _selectedMaterial.IsSelected = true;
        OnPropertyChanged(nameof(SelectedTexturePath));
        OnPropertyChanged(nameof(CurrentDocument));
    }

    private void SetFilePath(string? filePath)
    {
        _filePath = filePath;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasFilePath));
    }

    private void SetTextureDirectory(string? directory)
    {
        _textureDirectory = directory;
        OnPropertyChanged(nameof(DirectoryDisplayText));
        OnPropertyChanged(nameof(CurrentDocument));
    }

    private static TextureMaterialItemViewModel CreateNoneItem() => new("None", null, null);

    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
    }

    private static string? ResolveTexturePath(string? directory, string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return null;
        }

        return Path.GetFullPath(Path.IsPathRooted(texturePath)
            ? texturePath
            : Path.Combine(directory ?? string.Empty, texturePath));
    }

    private static int CountTextureItems(IReadOnlyCollection<TextureMaterialItemViewModel> items) => Math.Max(0, items.Count - 1);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TextureMaterialItemViewModel item in Materials)
        {
            item.Dispose();
        }

        Materials.Clear();
    }
}
