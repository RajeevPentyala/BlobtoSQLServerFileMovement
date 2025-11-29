namespace BlobReaderFunction.Models;

/// <summary>
/// Result of processing a single file
/// </summary>
public class FileProcessingResult
{
    /// <summary>
    /// Full path of the file
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Status of the file processing (Success/Failed)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
