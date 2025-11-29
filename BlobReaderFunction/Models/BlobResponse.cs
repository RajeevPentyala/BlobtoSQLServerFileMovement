namespace BlobReaderFunction.Models;

/// <summary>
/// Response model for the ProcessBlobFiles function
/// </summary>
public class BlobResponse
{
    /// <summary>
    /// Status of the operation: "Success", "PartialSuccess", or "Error"
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
    /// Number of files found in the blob folder
    /// </summary>
    public int FilesCount { get; set; }

    /// <summary>
    /// Number of files successfully copied to SharePoint
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Number of files that failed to copy
    /// </summary>
    public int FilesFailed { get; set; }

    /// <summary>
    /// List of files that failed to copy with error messages
    /// </summary>
    public List<FileProcessingResult> FailedFiles { get; set; } = new List<FileProcessingResult>();
}
