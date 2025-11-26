using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

// ===== EXAMPLE USAGE =====
var config = LoadBlobStorageConfig();

// Example 1: Batch processing (commented out)
// await ListBlobFilesInBatches(config.ConnectionString, config.ContainerName, config.FolderDepthLevel, config.TargetCount, config.BatchSize);

// Example 2: Get files in specific folder
Console.Write("Enter folder path (e.g., CaseDocs/0770001438/Subpoena/): ");
string folderPath = Console.ReadLine() ?? string.Empty;

if (string.IsNullOrWhiteSpace(folderPath))
{
    Console.WriteLine("Error: Folder path cannot be empty.");
    return;
}

Console.WriteLine($"\nSearching for files in '{folderPath}'...\n");
var files = await GetFilesInFolder(config.ConnectionString, config.ContainerName, folderPath);

if (files.Count > 0)
{
    Console.WriteLine($"Files found:\n");
    for (int i = 0; i < files.Count; i++)
    {
        Console.WriteLine($"{i + 1}. {files[i]}");
    }
    Console.WriteLine($"\nTotal: {files.Count} file(s)");
}
else
{
    Console.WriteLine($"No files found in '{folderPath}'");
}

// ============================================================================
// PUBLIC FUNCTIONS - Call these functions as needed
// ============================================================================

/// <summary>
/// Gets all files in a specific folder (no subfolders)
/// </summary>
/// <param name="connectionString">Azure Storage connection string</param>
/// <param name="containerName">Container name</param>
/// <param name="folderPath">Folder path (e.g., "CaseDocs/0770001438/Subpoena/")</param>
/// <returns>List of full file paths</returns>
static async Task<List<string>> GetFilesInFolder(string connectionString, string containerName, string folderPath)
{
    var files = new List<string>();
    string[] allowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
    
    try
    {
        var containerClient = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
        
        if (!await containerClient.ExistsAsync())
        {
            Console.WriteLine($"Error: Container '{containerName}' does not exist.");
            return files;
        }
        
        // Ensure folder path ends with /
        if (!folderPath.EndsWith("/"))
            folderPath += "/";
        
        // Get blobs with prefix filter
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: folderPath))
        {
            string blobName = blob.Name;
            
            // Check if file is directly in the folder (not in subfolders)
            string relativePath = blobName.Substring(folderPath.Length);
            if (!relativePath.Contains('/'))
            {
                // File is directly in the folder
                string ext = Path.GetExtension(blobName).ToLowerInvariant();
                if (allowedExtensions.Contains(ext))
                {
                    files.Add(blobName);
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    
    return files;
}

/// <summary>
/// Main function: Lists blob files with filtering and batch processing
/// </summary>
/// <param name="connectionString">Azure Storage connection string</param>
/// <param name="containerName">Container name to scan</param>
/// <param name="folderDepthLevel">Folder depth level to group by (1=top level, 2=second level, etc.)</param>
/// <param name="targetCount">Number of folders to process</param>
/// <param name="batchSize">Number of folders per batch</param>
static async Task ListBlobFilesInBatches(string connectionString, string containerName, int folderDepthLevel, int targetCount, int batchSize)
{
    Console.WriteLine($"Connecting to Azure Blob Storage...");
    Console.WriteLine($"Container: {containerName}");
    Console.WriteLine($"Folder Depth Level: {folderDepthLevel} | Target Folders: {targetCount} | Batch Size: {batchSize}\n");

    try
    {
        var containerClient = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
        
        if (!await containerClient.ExistsAsync())
        {
            Console.WriteLine($"Error: Container '{containerName}' does not exist.");
            return;
        }

        var folderFiles = await ScanBlobs(containerClient, folderDepthLevel, targetCount);
        await ProcessInBatches(folderFiles, batchSize);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

/// <summary>
/// Scans and groups blob files by folder at specified depth
/// </summary>
static async Task<Dictionary<string, List<FileInfo>>> ScanBlobs(BlobContainerClient containerClient, int folderDepthLevel, int targetCount)
{
    Console.WriteLine("Successfully connected! Scanning blobs...\n");
    Console.WriteLine("Filtering for: PDF, DOC, DOCX, XLS, XLSX files\n");
    Console.WriteLine($"Scanning for {targetCount} folders at depth level {folderDepthLevel}...\n");

    string[] allowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
    var folderFiles = new Dictionary<string, List<FileInfo>>();
    int scannedCount = 0, matchedCount = 0;
    
    await foreach (var blob in containerClient.GetBlobsAsync())
    {
        scannedCount++;
        
        if (scannedCount % 1000 == 0)
            Console.Write($"\rScanned: {scannedCount:N0} | Matched: {matchedCount:N0} | Folders: {folderFiles.Count}");
        
        string ext = Path.GetExtension(blob.Name).ToLowerInvariant();
        
        if (allowedExtensions.Contains(ext))
        {
            matchedCount++;
            string folder = GetFolderPath(blob.Name, folderDepthLevel);
            
            if (!folderFiles.ContainsKey(folder))
            {
                folderFiles[folder] = new List<FileInfo>();
                if (folderFiles.Count <= 20 || folderFiles.Count % 10 == 0)
                    Console.WriteLine($"\n✓ Folder #{folderFiles.Count}: {folder}");
                
                if (folderFiles.Count >= targetCount)
                {
                    Console.WriteLine($"\n✓ Reached target of {targetCount} folders!");
                    break;
                }
            }
            
            folderFiles[folder].Add(new FileInfo { Name = blob.Name, Extension = ext, Depth = blob.Name.Count(c => c == '/') });
        }
        
        if (scannedCount >= 100000)
        {
            Console.WriteLine($"\n\n⚠ Scan limit reached. Found {folderFiles.Count} folders.");
            break;
        }
    }
    
    Console.WriteLine($"\n✓ Scan complete! Scanned: {scannedCount:N0} | Matched: {matchedCount:N0} | Folders: {folderFiles.Count:N0}\n");
    return folderFiles;
}

/// <summary>
/// Processes folders in batches
/// </summary>
static async Task ProcessInBatches(Dictionary<string, List<FileInfo>> folderFiles, int batchSize)
{
    var folders = folderFiles.Keys.ToList();
    int totalFolders = folders.Count, batchNum = 1, totalFiles = 0;
    
    var overallCounts = new Dictionary<string, int> { { ".pdf", 0 }, { ".doc", 0 }, { ".docx", 0 }, { ".xls", 0 }, { ".xlsx", 0 } };
    foreach (var files in folderFiles.Values)
        foreach (var file in files)
            overallCounts[file.Extension]++;
    
    Console.WriteLine("─────────────────────────────────────────────────");
    Console.WriteLine($"Processing {totalFolders} folders in batches of {batchSize}");
    Console.WriteLine("─────────────────────────────────────────────────\n");
    
    for (int i = 0; i < totalFolders; i += batchSize)
    {
        var batch = folders.Skip(i).Take(batchSize).ToList();
        Console.WriteLine($"╔═══ BATCH {batchNum} ═══════════════════════════════════");
        Console.WriteLine($"║ Folders {i + 1} to {Math.Min(i + batchSize, totalFolders)} of {totalFolders}");
        Console.WriteLine($"╚════════════════════════════════════════════════════\n");
        
        var batchCounts = new Dictionary<string, int> { { ".pdf", 0 }, { ".doc", 0 }, { ".docx", 0 }, { ".xls", 0 }, { ".xlsx", 0 } };
        int batchFileCount = 0;
        
        foreach (var folder in batch)
        {
            Console.WriteLine($"📁 Folder: {folder}");
            var files = folderFiles[folder];
            
            foreach (var file in files)
            {
                batchFileCount++; totalFiles++;
                batchCounts[file.Extension]++;
                Console.WriteLine($"   {batchFileCount}. {file.Name}");
                if (file.Depth > 0)
                    Console.WriteLine($"      └─ Depth: {file.Depth} level(s)");
            }
            Console.WriteLine($"   → {files.Count} file(s)\n");
        }
        
        Console.WriteLine("─── Batch Summary ───────────────────────────────");
        Console.WriteLine($"Folders: {batch.Count} | Files: {batchFileCount}");
        Console.WriteLine($"  PDF: {batchCounts[".pdf"]} | DOC: {batchCounts[".doc"]} | DOCX: {batchCounts[".docx"]} | XLS: {batchCounts[".xls"]} | XLSX: {batchCounts[".xlsx"]}");
        Console.WriteLine("─────────────────────────────────────────────────\n");
        
        batchNum++;
        
        if (i + batchSize < totalFolders)
        {
            Console.WriteLine("Press any key for next batch...");
            Console.ReadKey(true);
            Console.WriteLine();
        }
    }
    
    Console.WriteLine("╔═══════════════════════════════════════════════════");
    Console.WriteLine("║ FINAL SUMMARY");
    Console.WriteLine("╚═══════════════════════════════════════════════════");
    Console.WriteLine($"\nTotal folders: {totalFolders} | Total files: {totalFiles}");
    Console.WriteLine($"\nFile Type Summary:");
    Console.WriteLine($"  PDF: {overallCounts[".pdf"]} | DOC: {overallCounts[".doc"]} | DOCX: {overallCounts[".docx"]}");
    Console.WriteLine($"  XLS: {overallCounts[".xls"]} | XLSX: {overallCounts[".xlsx"]}");
    
    await Task.CompletedTask;
}

// ============================================================================
// HELPER FUNCTIONS
// ============================================================================

static string GetFolderPath(string blobPath, int depth)
{
    var parts = blobPath.Split('/');
    return parts.Length <= depth ? (parts.Length > 1 ? string.Join("/", parts.Take(parts.Length - 1)) : "(root)") 
                                  : string.Join("/", parts.Take(depth));
}

static BlobConfig LoadBlobStorageConfig()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();
    
    return new BlobConfig
    {
        ConnectionString = config["AzureBlobStorage:ConnectionString"] ?? throw new Exception("ConnectionString not found"),
        ContainerName = config["AzureBlobStorage:ContainerName"] ?? throw new Exception("ContainerName not found"),
        FolderDepthLevel = int.TryParse(config["AzureBlobStorage:FolderDepthLevel"], out int fdl) ? fdl : 2,
        TargetCount = int.TryParse(config["AzureBlobStorage:TargetCount"], out int tc) ? tc : 100,
        BatchSize = int.TryParse(config["AzureBlobStorage:BatchSize"], out int bs) ? bs : 20
    };
}

// ============================================================================
// DATA CLASSES
// ============================================================================

class BlobConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public int FolderDepthLevel { get; set; } = 2;
    public int TargetCount { get; set; } = 100;
    public int BatchSize { get; set; } = 20;
}

class FileInfo
{
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public int Depth { get; set; }
}
