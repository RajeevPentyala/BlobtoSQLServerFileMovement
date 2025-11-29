using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BlobReaderFunction.Models;
using BlobReaderFunction.Services;
using Microsoft.Graph;
using Azure.Identity;

namespace BlobReaderFunction;

/// <summary>
/// Azure Function to process blob files from a specified folder
/// </summary>
public class ProcessBlobFiles
{
    private readonly ILogger<ProcessBlobFiles> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ProcessBlobFiles(ILogger<ProcessBlobFiles> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    [Function("ProcessBlobFiles")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("ProcessBlobFiles function triggered via HTTP POST");

        try
        {
            // Read configuration from environment variables
            string? connectionString = Environment.GetEnvironmentVariable("AzureBlobStorage__ConnectionString");
            string? containerName = Environment.GetEnvironmentVariable("AzureBlobStorage__ContainerName");

            _logger.LogInformation("Configuration loaded. Container: {ContainerName}", containerName ?? "NULL");

            // Validate configuration
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogError("Missing configuration: AzureBlobStorage__ConnectionString");
                return new BadRequestObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = "Configuration error: Connection string not found in environment variables",
                    FolderPath = string.Empty,
                    FilesCount = 0
                });
            }

            if (string.IsNullOrWhiteSpace(containerName))
            {
                _logger.LogError("Missing configuration: AzureBlobStorage__ContainerName");
                return new BadRequestObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = "Configuration error: Container name not found in environment variables",
                    FolderPath = string.Empty,
                    FilesCount = 0
                });
            }

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Request body received: {RequestBody}", requestBody);

            // Parse request
            JsonDocument? jsonDoc = null;
            string? blobFolderPath = null;

            try
            {
                jsonDoc = JsonDocument.Parse(requestBody);
                if (jsonDoc.RootElement.TryGetProperty("blobFolderPath", out JsonElement folderPathElement))
                {
                    blobFolderPath = folderPathElement.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid JSON in request body");
                return new BadRequestObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = "Invalid JSON format in request body",
                    FolderPath = string.Empty,
                    FilesCount = 0
                });
            }

            // Validate blobFolderPath parameter
            if (string.IsNullOrWhiteSpace(blobFolderPath))
            {
                _logger.LogWarning("Missing or empty parameter: blobFolderPath");
                return new BadRequestObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = "Missing required parameter: blobFolderPath",
                    FolderPath = string.Empty,
                    FilesCount = 0
                });
            }

            _logger.LogInformation("Processing folder: {BlobFolderPath}", blobFolderPath);

            // Read SharePoint configuration
            string? spSiteUrl = Environment.GetEnvironmentVariable("SharePoint__SiteUrl");
            string? spDocLibrary = Environment.GetEnvironmentVariable("SharePoint__DocumentLibrary");
            string? spClientId = Environment.GetEnvironmentVariable("SharePoint__ClientId");
            string? spClientSecret = Environment.GetEnvironmentVariable("SharePoint__ClientSecret");
            string? spTenantId = Environment.GetEnvironmentVariable("SharePoint__TenantId");

            _logger.LogInformation("SharePoint configuration loaded. Site: {SiteUrl}, Library: {DocumentLibrary}, ClientId: {ClientId}", 
                spSiteUrl ?? "NULL", spDocLibrary ?? "NULL", spClientId ?? "NULL");

            // Validate SharePoint configuration
            if (string.IsNullOrWhiteSpace(spSiteUrl) || string.IsNullOrWhiteSpace(spDocLibrary) ||
                string.IsNullOrWhiteSpace(spClientId) || string.IsNullOrWhiteSpace(spClientSecret) ||
                string.IsNullOrWhiteSpace(spTenantId))
            {
                _logger.LogError("Missing SharePoint configuration in environment variables");
                return new BadRequestObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = "SharePoint configuration error: Missing required configuration in environment variables",
                    FolderPath = blobFolderPath,
                    FilesCount = 0,
                    FilesProcessed = 0,
                    FilesFailed = 0
                });
            }

            // Create Blob Storage service and retrieve files
            var blobServiceLogger = _loggerFactory.CreateLogger<BlobStorageService>();
            var blobService = new BlobStorageService(connectionString, containerName, blobServiceLogger);

            var files = await blobService.GetFilesInFolderAsync(blobFolderPath);

            _logger.LogInformation("Files retrieved from blob storage. Count: {FilesCount}", files.Count);

            if (files.Count == 0)
            {
                _logger.LogInformation("No files found in folder: {BlobFolderPath}", blobFolderPath);
                return new OkObjectResult(new BlobResponse
                {
                    Status = "Success",
                    Message = "Folder is empty or contains no matching files",
                    FolderPath = blobFolderPath,
                    FilesCount = 0,
                    FilesProcessed = 0,
                    FilesFailed = 0
                });
            }

            // Copy files to SharePoint
            _logger.LogInformation("Starting file copy to SharePoint. Total files: {FilesCount}", files.Count);
            
            int filesProcessed = 0;
            int filesFailed = 0;
            var failedFiles = new List<FileProcessingResult>();

            // Initialize SharePoint service (will be created on first file)
            SharePointService? sharePointService = null;

            try
            {
                foreach (var blobFilePath in files)
                {
                    try
                    {
                        _logger.LogInformation("Processing file {Current}/{Total}: {FilePath}", 
                            filesProcessed + filesFailed + 1, files.Count, blobFilePath);

                        // Parse folder path and file name from blob path
                        // Example: "CaseDocs/0770001438/Subpoena/file.pdf"
                        // → Folder: "CaseDocs/0770001438/Subpoena"
                        // → File: "file.pdf"
                        var lastSlashIndex = blobFilePath.LastIndexOf('/');
                        string folderPath = lastSlashIndex > 0 ? blobFilePath.Substring(0, lastSlashIndex) : string.Empty;
                        string fileName = lastSlashIndex > 0 ? blobFilePath.Substring(lastSlashIndex + 1) : blobFilePath;

                        _logger.LogDebug("Parsed - Folder: {FolderPath}, File: {FileName}", folderPath, fileName);

                        // Download file from Blob Storage
                        var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(connectionString);
                        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                        var blobClient = containerClient.GetBlobClient(blobFilePath);

                        _logger.LogDebug("Downloading file from blob storage: {FilePath}", blobFilePath);
                        var blobStream = await blobClient.OpenReadAsync();

                        // Initialize SharePoint service on first file (lazy initialization)
                        if (sharePointService == null)
                        {
                            _logger.LogInformation("---------- Initializing SharePoint Service ----------");
                            var spLogger = _loggerFactory.CreateLogger<SharePointService>();
                            
                            try
                            {
                                // Initialize Graph Client with Service Principal
                                var (graphClient, siteId, driveId) = await InitializeGraphClientAsync(
                                    spSiteUrl, spDocLibrary, spClientId, spClientSecret, spTenantId);
                                
                                sharePointService = new SharePointService(graphClient, siteId, driveId, spLogger);
                                _logger.LogInformation("✓ SharePoint service ready for file operations");
                                _logger.LogInformation("------------------------------------------------------");
                            }
                            catch (Exception initEx)
                            {
                                _logger.LogError(initEx, "FAILED to initialize SharePoint service");
                                _logger.LogError("Cannot proceed with file copy operations");
                                throw; // Re-throw to be caught by outer catch block
                            }
                        }

                        // Ensure folder structure exists in SharePoint
                        if (!string.IsNullOrWhiteSpace(folderPath))
                        {
                            _logger.LogDebug("Ensuring folder structure exists: {FolderPath}", folderPath);
                            await sharePointService.EnsureFolderPathAsync(folderPath);
                            _logger.LogDebug("✓ Folder structure verified/created");
                        }

                        // Upload file to SharePoint (overwrite if exists)
                        _logger.LogInformation("Uploading file to SharePoint: {FileName}", fileName);
                        await sharePointService.UploadFileAsync(folderPath, fileName, blobStream, overwrite: true);

                        filesProcessed++;
                        _logger.LogInformation("✓ File {Processed}/{Total} copied successfully: {FileName}", 
                            filesProcessed, files.Count, fileName);
                    }
                    catch (Azure.Identity.AuthenticationFailedException authEx)
                    {
                        filesFailed++;
                        var errorMsg = $"Authentication Failed: {authEx.Message}";
                        _logger.LogError(authEx, "❌ Authentication error while copying file: {FilePath}", blobFilePath);
                        _logger.LogError("This indicates the Service Principal credentials are invalid or permissions are insufficient");
                        failedFiles.Add(new FileProcessingResult
                        {
                            FileName = blobFilePath,
                            Status = "Failed - Authentication Error",
                            ErrorMessage = errorMsg
                        });
                    }
                    catch (Microsoft.Graph.Models.ODataErrors.ODataError graphEx)
                    {
                        filesFailed++;
                        var errorMsg = $"Graph API Error [{graphEx.Error?.Code}]: {graphEx.Error?.Message}";
                        _logger.LogError(graphEx, "❌ Graph API error while copying file: {FilePath}", blobFilePath);
                        _logger.LogError("Error Code: {ErrorCode}", graphEx.Error?.Code ?? "Unknown");
                        failedFiles.Add(new FileProcessingResult
                        {
                            FileName = blobFilePath,
                            Status = "Failed - Graph API Error",
                            ErrorMessage = errorMsg
                        });
                    }
                    catch (Exception ex)
                    {
                        filesFailed++;
                        var errorMsg = $"{ex.GetType().Name}: {ex.Message}";
                        _logger.LogError(ex, "❌ Failed to copy file: {FilePath}", blobFilePath);
                        _logger.LogError("Error Type: {ErrorType}", ex.GetType().Name);
                        failedFiles.Add(new FileProcessingResult
                        {
                            FileName = blobFilePath,
                            Status = "Failed",
                            ErrorMessage = errorMsg
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during file copy operation");
                return new ObjectResult(new BlobResponse
                {
                    Status = "Error",
                    Message = $"Critical error during file copy: {ex.Message}",
                    FolderPath = blobFolderPath,
                    FilesCount = files.Count,
                    FilesProcessed = filesProcessed,
                    FilesFailed = filesFailed,
                    FailedFiles = failedFiles
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            // Build response
            var response = new BlobResponse
            {
                Status = filesFailed == 0 ? "Success" : (filesProcessed > 0 ? "PartialSuccess" : "Error"),
                Message = filesFailed == 0
                    ? $"Successfully copied {filesProcessed} file(s) to SharePoint"
                    : $"Copied {filesProcessed} of {files.Count} file(s) to SharePoint. {filesFailed} file(s) failed.",
                FolderPath = blobFolderPath,
                FilesCount = files.Count,
                FilesProcessed = filesProcessed,
                FilesFailed = filesFailed,
                FailedFiles = failedFiles
            };

            _logger.LogInformation(
                "Copy operation completed. Status: {Status}, Total: {Total}, Processed: {Processed}, Failed: {Failed}",
                response.Status, response.FilesCount, response.FilesProcessed, response.FilesFailed);

            return new OkObjectResult(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Container or configuration error");
            return new BadRequestObjectResult(new BlobResponse
            {
                Status = "Error",
                Message = $"Container error: {ex.Message}",
                FolderPath = string.Empty,
                FilesCount = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing blob files");
            return new ObjectResult(new BlobResponse
            {
                Status = "Error",
                Message = $"An unexpected error occurred: {ex.Message}",
                FolderPath = string.Empty,
                FilesCount = 0
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    /// <summary>
    /// Initializes Microsoft Graph client and retrieves Site ID and Drive ID
    /// </summary>
    private async Task<(GraphServiceClient graphClient, string siteId, string driveId)> InitializeGraphClientAsync(
        string siteUrl, string documentLibrary, string clientId, string clientSecret, string tenantId)
    {
        try
        {
            _logger.LogInformation("========== Starting SharePoint Authentication ==========");
            _logger.LogInformation("Authentication Method: Service Principal (Client Credentials Flow)");
            _logger.LogInformation("TenantId: {TenantId}", tenantId);
            _logger.LogInformation("ClientId: {ClientId}", clientId);
            _logger.LogInformation("Site URL: {SiteUrl}", siteUrl);
            _logger.LogInformation("Document Library: {DocumentLibrary}", documentLibrary);

            // Create credential using Service Principal (Client Credentials)
            // Use commercial cloud authority explicitly for SharePoint Online
            _logger.LogInformation("Creating ClientSecretCredential for commercial cloud (Azure Public)...");
            var credentialOptions = new ClientSecretCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
            };
            var credential = new ClientSecretCredential(
                tenantId,
                clientId,
                clientSecret,
                credentialOptions
            );
            _logger.LogInformation("ClientSecretCredential created successfully for Azure Public Cloud");

            // Create Graph client
            _logger.LogInformation("Creating GraphServiceClient...");
            var graphClient = new GraphServiceClient(credential);
            _logger.LogInformation("GraphServiceClient created successfully");

            // Parse site URL to get host and site path
            // Example: https://justicedigitalinnovations.sharepoint.com/sites/JDISJCDADataMigration
            // → Host: justicedigitalinnovations.sharepoint.com
            // → Site Path: /sites/JDISJCDADataMigration
            _logger.LogInformation("Parsing SharePoint site URL...");
            var uri = new Uri(siteUrl);
            var host = uri.Host;
            var sitePath = uri.PathAndQuery.TrimEnd('/');
            _logger.LogInformation("Site URL parsed - Host: {Host}, SitePath: {SitePath}", host, sitePath);

            // Get site ID
            _logger.LogInformation("Retrieving SharePoint site information via Graph API...");
            _logger.LogInformation("API Call: GET /sites/{0}:{1}", host, sitePath);
            
            var site = await graphClient.Sites[$"{host}:{sitePath}"].GetAsync();
            
            if (site == null || string.IsNullOrEmpty(site.Id))
            {
                _logger.LogError("Failed to retrieve site information. Site response is null or missing ID.");
                throw new InvalidOperationException("Unable to retrieve SharePoint site information");
            }
            
            var siteId = site.Id;
            _logger.LogInformation("✓ Site retrieved successfully");
            _logger.LogInformation("  - SiteId: {SiteId}", siteId);
            _logger.LogInformation("  - Site Name: {SiteName}", site.DisplayName ?? "N/A");
            _logger.LogInformation("  - Web URL: {WebUrl}", site.WebUrl ?? "N/A");

            // Get drive (document library) ID by name
            _logger.LogInformation("Retrieving document libraries (drives) for site...");
            _logger.LogInformation("API Call: GET /sites/{0}/drives", siteId);
            
            var drives = await graphClient.Sites[siteId].Drives.GetAsync();
            
            if (drives == null || drives.Value == null || !drives.Value.Any())
            {
                _logger.LogError("No drives found in the site. Site may not have any document libraries.");
                throw new InvalidOperationException("No document libraries found in SharePoint site");
            }
            
            _logger.LogInformation("Found {Count} drive(s) in site:", drives.Value.Count);
            foreach (var d in drives.Value)
            {
                _logger.LogInformation("  - Drive: '{Name}' (ID: {Id})", d.Name ?? "Unnamed", d.Id ?? "No ID");
            }
            
            var drive = drives.Value.FirstOrDefault(d => 
                d.Name != null && d.Name.Equals(documentLibrary, StringComparison.OrdinalIgnoreCase));

            if (drive == null)
            {
                _logger.LogError("Document library '{DocumentLibrary}' not found in site", documentLibrary);
                _logger.LogError("Available libraries: {Libraries}", 
                    string.Join(", ", drives.Value.Select(d => $"'{d.Name}'")));
                throw new InvalidOperationException(
                    $"Document library '{documentLibrary}' not found in site. " +
                    $"Available: {string.Join(", ", drives.Value.Select(d => $"'{d.Name}'"))}");
            }

            var driveId = drive.Id!;
            _logger.LogInformation("✓ Document library found successfully");
            _logger.LogInformation("  - DriveId: {DriveId}", driveId);
            _logger.LogInformation("  - Drive Name: {DriveName}", drive.Name);
            _logger.LogInformation("  - Drive Type: {DriveType}", drive.DriveType ?? "N/A");

            _logger.LogInformation("========== SharePoint Authentication Complete ==========");
            _logger.LogInformation("Ready to process files from Blob Storage to SharePoint");

            return (graphClient, siteId, driveId);
        }
        catch (Azure.Identity.AuthenticationFailedException authEx)
        {
            _logger.LogError("========== AUTHENTICATION FAILED ==========");
            _logger.LogError(authEx, "Authentication error with Service Principal");
            _logger.LogError("TenantId: {TenantId}", tenantId);
            _logger.LogError("ClientId: {ClientId}", clientId);
            _logger.LogError("Error Message: {Message}", authEx.Message);
            _logger.LogError("Possible causes:");
            _logger.LogError("  1. Invalid ClientId or ClientSecret");
            _logger.LogError("  2. Service Principal not granted permissions to SharePoint");
            _logger.LogError("  3. Required API permissions: Sites.ReadWrite.All, Files.ReadWrite.All");
            _logger.LogError("  4. Incorrect TenantId");
            _logger.LogError("===========================================");
            throw;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError graphEx)
        {
            _logger.LogError("========== GRAPH API ERROR ==========");
            _logger.LogError(graphEx, "Microsoft Graph API error");
            _logger.LogError("Error Code: {ErrorCode}", graphEx.Error?.Code ?? "Unknown");
            _logger.LogError("Error Message: {ErrorMessage}", graphEx.Error?.Message ?? "Unknown");
            _logger.LogError("Possible causes:");
            _logger.LogError("  1. Site URL is incorrect or inaccessible");
            _logger.LogError("  2. Service Principal lacks permissions");
            _logger.LogError("  3. Document library name is incorrect");
            _logger.LogError("=====================================");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("========== UNEXPECTED ERROR ==========");
            _logger.LogError(ex, "Unexpected error during SharePoint initialization");
            _logger.LogError("Error Type: {ErrorType}", ex.GetType().Name);
            _logger.LogError("Error Message: {Message}", ex.Message);
            if (ex.InnerException != null)
            {
                _logger.LogError("Inner Exception: {InnerMessage}", ex.InnerException.Message);
            }
            _logger.LogError("======================================");
            throw;
        }
    }
}
