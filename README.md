# Blob to SQL Server File Movement

A C# .NET console application for reading and managing files from Azure Blob Storage with batch processing capabilities.

## Features

- **Get Files in Specific Folder**: List all files in a specific Azure Blob Storage folder path
- **Batch Processing**: Process large numbers of folders in configurable batches
- **File Type Filtering**: Filters for PDF, DOC, DOCX, XLS, and XLSX files
- **Folder Depth Grouping**: Group files by configurable folder depth levels
- **Interactive Console**: Prompt-based folder path input

## Prerequisites

- .NET 8.0 SDK or later
- Azure Storage Account with Blob Storage
- Connection string or access credentials for Azure Blob Storage

## Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/RajeevPentyala/BlobtoSQLServerFileMovement.git
   cd BlobtoSQLServerFileMovement
   ```

2. **Configure Azure Blob Storage**
   - Copy `appsettings.example.json` to `appsettings.json`
   - Update `appsettings.json` with your Azure credentials:
     ```json
     {
       "AzureBlobStorage": {
         "ConnectionString": "YOUR_CONNECTION_STRING_HERE",
         "ContainerName": "YOUR_CONTAINER_NAME_HERE",
         "FolderDepthLevel": 2,
         "TargetCount": 100,
         "BatchSize": 20
       }
     }
     ```

3. **Build the project**
   ```bash
   dotnet build
   ```

4. **Run the application**
   ```bash
   dotnet run
   ```

## Usage

### Get Files in Specific Folder

When you run the application, it will prompt you to enter a folder path:

```
Enter folder path (e.g., CaseDocs/0770001438/Subpoena/): 
```

Enter your folder path and the application will list all matching files.

### Available Functions

#### `GetFilesInFolder`
Lists all files directly in a specified folder (excludes subfolders).

```csharp
var files = await GetFilesInFolder(connectionString, containerName, "CaseDocs/0770001438/Subpoena/");
```

#### `ListBlobFilesInBatches`
Processes folders in batches with configurable depth and batch size.

```csharp
await ListBlobFilesInBatches(connectionString, containerName, folderDepthLevel, targetCount, batchSize);
```

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `ConnectionString` | Azure Storage connection string | Required |
| `ContainerName` | Blob container name | Required |
| `FolderDepthLevel` | Folder depth for grouping (1=top, 2=second level, etc.) | 2 |
| `TargetCount` | Number of folders to process | 100 |
| `BatchSize` | Folders per batch | 20 |

## File Type Filtering

The application filters for the following file types:
- PDF (`.pdf`)
- Word Documents (`.doc`, `.docx`)
- Excel Spreadsheets (`.xls`, `.xlsx`)

## Security Notes

⚠️ **Important**: Never commit `appsettings.json` with real credentials to the repository. The `.gitignore` file is configured to exclude this file automatically.

## License

MIT License

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
