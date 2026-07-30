namespace FileRename;

/// <summary>A row in the preview grid representing one file in the input folder.</summary>
public class FilePreviewItem
{
    public required string FullPath { get; init; }
    public required string OriginalName { get; set; }
    public required string NewName { get; set; }
    public required string Status { get; set; }
}
