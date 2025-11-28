using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace BlobReaderFunction.Services;

/// <summary>
/// Service for Azure Blob Storage operations
/// </summary>
public class BlobStorageService
{
    private readonly string _connectionString;
    private readonly string _containerName;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly string[] _allowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };

    public BlobStorageService(string connectionString, string containerName, ILogger<BlobStorageService> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _containerName = containerName ?? throw new ArgumentNullException(nameof(containerName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("BlobStorageService initialized. Container: {ContainerName}", _containerName);
    }

    /// <summary>
    /// Gets all files in a specific folder (no subfolders)
    /// </summary>
    /// <param name="folderPath">Folder path (e.g., "CaseDocs/0770001438/Subpoena/")</param>
    /// <returns>List of full file paths</returns>
    public async Task<List<string>> GetFilesInFolderAsync(string folderPath)
    {
        var files = new List<string>();

        try
        {
            _logger.LogInformation("Starting file retrieval for folder: {FolderPath}", folderPath);

            // Create blob service client
            var blobServiceClient = new BlobServiceClient(_connectionString);
            _logger.LogInformation("BlobServiceClient created successfully");

            // Get container client
            var containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
            _logger.LogInformation("Checking if container exists: {ContainerName}", _containerName);

            // Check if container exists
            if (!await containerClient.ExistsAsync())
            {
                _logger.LogError("Container does not exist: {ContainerName}", _containerName);
                throw new InvalidOperationException($"Container '{_containerName}' does not exist.");
            }

            _logger.LogInformation("Container exists. Starting blob enumeration...");

            // Ensure folder path ends with /
            if (!folderPath.EndsWith("/"))
            {
                folderPath += "/";
                _logger.LogInformation("Added trailing slash to folder path: {FolderPath}", folderPath);
            }

            int scannedCount = 0;
            int matchedCount = 0;

            // Get blobs with prefix filter
            await foreach (var blob in containerClient.GetBlobsAsync(prefix: folderPath))
            {
                scannedCount++;
                string blobName = blob.Name;

                // Check if file is directly in the folder (not in subfolders)
                string relativePath = blobName.Substring(folderPath.Length);

                if (!relativePath.Contains('/'))
                {
                    // File is directly in the folder
                    string ext = Path.GetExtension(blobName).ToLowerInvariant();

                    if (_allowedExtensions.Contains(ext))
                    {
                        matchedCount++;
                        files.Add(blobName);
                        _logger.LogDebug("Matched file {MatchedCount}: {BlobName}", matchedCount, blobName);
                    }
                }
            }

            _logger.LogInformation(
                "File retrieval completed. Scanned: {ScannedCount}, Matched: {MatchedCount}, Folder: {FolderPath}",
                scannedCount, matchedCount, folderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving files from folder: {FolderPath}", folderPath);
            throw;
        }

        return files;
    }
}
