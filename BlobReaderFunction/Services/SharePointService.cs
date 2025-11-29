using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace BlobReaderFunction.Services;

/// <summary>
/// Service for SharePoint operations using Microsoft Graph API
/// </summary>
public class SharePointService
{
    private readonly GraphServiceClient _graphClient;
    private readonly string _siteId;
    private readonly string _driveId;
    private readonly ILogger<SharePointService> _logger;

    public SharePointService(
        GraphServiceClient graphClient,
        string siteId,
        string driveId,
        ILogger<SharePointService> logger)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _siteId = siteId ?? throw new ArgumentNullException(nameof(siteId));
        _driveId = driveId ?? throw new ArgumentNullException(nameof(driveId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("SharePointService initialized. SiteId: {SiteId}, DriveId: {DriveId}", 
            _siteId, _driveId);
    }

    /// <summary>
    /// Ensures that the folder path exists in SharePoint, creating it if necessary
    /// </summary>
    /// <param name="folderPath">Folder path relative to document library root (e.g., "CaseDocs/0770001438/Subpoena")</param>
    public async Task<string> EnsureFolderPathAsync(string folderPath)
    {
        _logger.LogInformation("→ Ensuring folder path exists: '{FolderPath}'", folderPath);

        try
        {
            // Split the folder path into parts
            var folderParts = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (folderParts.Length == 0)
            {
                _logger.LogInformation("  Using root folder (no subfolders specified)");
                return "root";
            }

            _logger.LogInformation("  Folder structure has {Count} level(s): {Levels}", 
                folderParts.Length, string.Join(" > ", folderParts));

            // Build folder path incrementally
            string parentFolderId = "root";
            string currentPath = "";
            
            for (int i = 0; i < folderParts.Length; i++)
            {
                var folderName = folderParts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? folderName : $"{currentPath}/{folderName}";
                
                try
                {
                    _logger.LogDebug("  [{Level}/{Total}] Processing: '{FolderName}'", i + 1, folderParts.Length, folderName);
                    
                    // Try to get existing folder
                    _logger.LogDebug("    → API: GET /drives/{DriveId}/items/{ParentId}/children", _driveId, parentFolderId);
                    var children = await _graphClient.Drives[_driveId]
                        .Items[parentFolderId]
                        .Children
                        .GetAsync();

                    var existingFolder = children?.Value?.FirstOrDefault(item => 
                        item.Folder != null && 
                        item.Name != null &&
                        item.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));

                    if (existingFolder != null && existingFolder.Id != null)
                    {
                        parentFolderId = existingFolder.Id;
                        _logger.LogInformation("    ✓ Folder exists: '{FolderName}' (ID: {FolderId})", folderName, parentFolderId);
                    }
                    else
                    {
                        // Create new folder
                        _logger.LogInformation("    → Creating new folder: '{FolderName}'", folderName);
                        var newFolder = new DriveItem
                        {
                            Name = folderName,
                            Folder = new Folder()
                        };

                        _logger.LogDebug("    → API: POST /drives/{DriveId}/items/{ParentId}/children", _driveId, parentFolderId);
                        var createdFolder = await _graphClient.Drives[_driveId]
                            .Items[parentFolderId]
                            .Children
                            .PostAsync(newFolder);

                        parentFolderId = createdFolder!.Id!;
                        _logger.LogInformation("    ✓ Folder created: '{FolderName}' (ID: {FolderId})", folderName, parentFolderId);
                    }
                }
                catch (Microsoft.Graph.Models.ODataErrors.ODataError graphEx)
                {
                    _logger.LogError("    ❌ Graph API error processing folder '{FolderName}'", folderName);
                    _logger.LogError("       Error Code: {ErrorCode}", graphEx.Error?.Code ?? "Unknown");
                    _logger.LogError("       Error Message: {ErrorMessage}", graphEx.Error?.Message ?? "Unknown");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "    ❌ Error processing folder: '{FolderName}' at path '{CurrentPath}'", folderName, currentPath);
                    throw;
                }
            }

            _logger.LogInformation("  ✓ Complete folder path verified: '{FolderPath}' (ID: {FolderId})", folderPath, parentFolderId);
            return parentFolderId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to ensure folder path: '{FolderPath}'", folderPath);
            throw;
        }
    }

    /// <summary>
    /// Uploads a file to SharePoint
    /// </summary>
    /// <param name="folderPath">Folder path relative to document library root</param>
    /// <param name="fileName">Name of the file</param>
    /// <param name="fileStream">File content stream</param>
    /// <param name="overwrite">Whether to overwrite if file exists</param>
    public async Task UploadFileAsync(string folderPath, string fileName, Stream fileStream, bool overwrite = true)
    {
        var fileSizeKB = fileStream.Length / 1024.0;
        _logger.LogInformation("→ Uploading file: '{FileName}' ({Size:F2} KB)", fileName, fileSizeKB);
        _logger.LogInformation("  Target folder: '{FolderPath}'", string.IsNullOrWhiteSpace(folderPath) ? "[root]" : folderPath);
        _logger.LogInformation("  Overwrite mode: {Overwrite}", overwrite ? "Enabled" : "Disabled");

        try
        {
            // Ensure folder exists and get folder ID
            string folderId = string.IsNullOrWhiteSpace(folderPath) 
                ? "root" 
                : await EnsureFolderPathAsync(folderPath);

            _logger.LogDebug("  Target folder ID: {FolderId}", folderId);

            // Check if file already exists (if overwrite is false)
            if (!overwrite)
            {
                _logger.LogDebug("  Checking if file already exists (overwrite disabled)...");
                var exists = await FileExistsAsync(folderPath, fileName);
                if (exists)
                {
                    _logger.LogWarning("  ❌ File already exists and overwrite is disabled: '{FileName}'", fileName);
                    throw new InvalidOperationException($"File '{fileName}' already exists in folder '{folderPath}'");
                }
                _logger.LogDebug("  ✓ File does not exist, proceeding with upload");
            }

            // Upload file using PUT (for files < 4MB) or create upload session (for larger files)
            var driveItem = new DriveItem
            {
                Name = fileName,
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", overwrite ? "replace" : "fail" }
                }
            };

            // For small files (< 4MB), use simple upload
            _logger.LogDebug("  → API: PUT /drives/{DriveId}/items/{FolderId}/children/{FileName}/content", _driveId, folderId, fileName);
            _logger.LogInformation("  Uploading {Size:F2} KB to SharePoint...", fileSizeKB);
            
            var uploadedItem = await _graphClient.Drives[_driveId]
                .Items[folderId]
                .ItemWithPath(fileName)
                .Content
                .PutAsync(fileStream);

            _logger.LogInformation("  ✓ File uploaded successfully: '{FileName}'", fileName);
            if (uploadedItem?.Id != null)
            {
                _logger.LogDebug("    Item ID: {ItemId}", uploadedItem.Id);
                _logger.LogDebug("    Web URL: {WebUrl}", uploadedItem.WebUrl ?? "N/A");
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError graphEx)
        {
            _logger.LogError("  ❌ Graph API error uploading file: '{FileName}'", fileName);
            _logger.LogError("     Error Code: {ErrorCode}", graphEx.Error?.Code ?? "Unknown");
            _logger.LogError("     Error Message: {ErrorMessage}", graphEx.Error?.Message ?? "Unknown");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "  ❌ Error uploading file: '{FileName}' to folder: '{FolderPath}'", fileName, folderPath);
            throw;
        }
    }

    /// <summary>
    /// Checks if a file exists in SharePoint
    /// </summary>
    /// <param name="folderPath">Folder path relative to document library root</param>
    /// <param name="fileName">Name of the file</param>
    /// <returns>True if file exists, false otherwise</returns>
    public async Task<bool> FileExistsAsync(string folderPath, string fileName)
    {
        try
        {
            _logger.LogDebug("Checking if file exists: {FileName} in folder: {FolderPath}", fileName, folderPath);

            // Get folder ID
            string folderId = string.IsNullOrWhiteSpace(folderPath) 
                ? "root" 
                : await EnsureFolderPathAsync(folderPath);

            // Try to get the file
            try
            {
                var file = await _graphClient.Drives[_driveId]
                    .Items[folderId]
                    .ItemWithPath(fileName)
                    .GetAsync();

                var exists = file != null;
                _logger.LogDebug("File existence check result: {Exists} for {FileName}", exists, fileName);
                return exists;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError)
            {
                // File not found
                _logger.LogDebug("File does not exist: {FileName}", fileName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking file existence: {FileName}", fileName);
            return false;
        }
    }
}
