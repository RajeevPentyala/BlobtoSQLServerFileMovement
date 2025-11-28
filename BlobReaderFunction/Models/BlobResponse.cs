namespace BlobReaderFunction.Models;

/// <summary>
/// Response model for the ProcessBlobFiles function
/// </summary>
public class BlobResponse
{
    /// <summary>
    /// Status of the operation: "Success" or "Error"
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Descriptive message about the operation result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The folder path that was processed
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Number of files found in the folder
    /// </summary>
    public int FilesCount { get; set; }
}
