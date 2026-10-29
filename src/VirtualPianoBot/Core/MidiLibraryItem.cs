namespace VirtualPianoBot.Core;

public sealed class MidiLibraryItem : ObservableObject
{
    private bool _isFavorite;
    public MidiLibraryItem(string fullPath, string libraryRoot)
    {
        FullPath = Path.GetFullPath(fullPath);
        Title = Path.GetFileNameWithoutExtension(fullPath);
        FileName = Path.GetFileName(fullPath);
        RelativePath = Path.GetRelativePath(libraryRoot, fullPath);

        try
        {
            var fileInfo = new FileInfo(fullPath);
            FileSizeBytes = fileInfo.Length;
            ModifiedAt = fileInfo.LastWriteTime;
        }
        catch (IOException)
        {
            FileSizeBytes = 0;
            ModifiedAt = DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            FileSizeBytes = 0;
            ModifiedAt = DateTime.MinValue;
        }

        FileSizeLabel = FormatFileSize(FileSizeBytes);
        ModifiedLabel = ModifiedAt == DateTime.MinValue ? "Modified date unavailable" : ModifiedAt.ToString("dd MMM yyyy");
    }

    public string FullPath { get; }
    public string Title { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public long FileSizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string FileSizeLabel { get; }
    public string ModifiedLabel { get; }
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (!SetProperty(ref _isFavorite, value)) return;
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }
    public string FavoriteLabel => IsFavorite ? "★" : "☆";

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.00} GB";
    }
}
