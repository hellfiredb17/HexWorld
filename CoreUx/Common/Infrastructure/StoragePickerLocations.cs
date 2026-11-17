using Avalonia.Platform.Storage;

namespace HawkNet.CoreUx.Common.Infrastructure;

internal static class StoragePickerLocations
{
    public static async Task<IStorageFolder?> ResolveAsync(IStorageProvider storage, params string?[] preferredDirectories)
    {
        foreach (string? directory in preferredDirectories.Append(Environment.CurrentDirectory).Append(AppContext.BaseDirectory))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try
            {
                string fullPath = Path.GetFullPath(directory);
                if (!Directory.Exists(fullPath)) continue;
                IStorageFolder? folder = await storage.TryGetFolderFromPathAsync(fullPath);
                if (folder is not null) return folder;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A removed or inaccessible previous folder should not prevent opening the picker.
            }
        }
        return null;
    }
}
