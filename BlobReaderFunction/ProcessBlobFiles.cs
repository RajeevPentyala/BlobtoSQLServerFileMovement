using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BlobReaderFunction.Models;
using BlobReaderFunction.Services;

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

            // Create service and retrieve files
            var serviceLogger = _loggerFactory.CreateLogger<BlobStorageService>();
            var blobService = new BlobStorageService(connectionString, containerName, serviceLogger);

            var files = await blobService.GetFilesInFolderAsync(blobFolderPath);

            _logger.LogInformation("Files retrieved successfully. Count: {FilesCount}", files.Count);

            // Build response
            var response = new BlobResponse
            {
                Status = "Success",
                Message = files.Count > 0 
                    ? $"Successfully retrieved {files.Count} file(s) from folder" 
                    : "Folder is empty or contains no matching files",
                FolderPath = blobFolderPath,
                FilesCount = files.Count
            };

            _logger.LogInformation(
                "Request completed successfully. Status: {Status}, FolderPath: {FolderPath}, FilesCount: {FilesCount}",
                response.Status, response.FolderPath, response.FilesCount);

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
}
