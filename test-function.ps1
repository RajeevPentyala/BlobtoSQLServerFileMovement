# Test Azure Function - ProcessBlobFiles
# This script sends a POST request to the locally running Azure Function

$uri = "http://localhost:7071/api/ProcessBlobFiles"
$body = @{
    blobFolderPath = "CaseDocs/0770001438/Subpoena/"
} | ConvertTo-Json

Write-Host "Sending POST request to: $uri" -ForegroundColor Cyan
Write-Host "Request Body: $body" -ForegroundColor Yellow
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $uri -Method Post -Body $body -ContentType "application/json"
    
    Write-Host "✅ Response received:" -ForegroundColor Green
    Write-Host "Status: $($response.status)" -ForegroundColor Green
    Write-Host "Message: $($response.message)" -ForegroundColor White
    Write-Host "Folder Path: $($response.folderPath)" -ForegroundColor White
    Write-Host "Files Count: $($response.filesCount)" -ForegroundColor White
}
catch {
    Write-Host "❌ Error:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response Body: $responseBody" -ForegroundColor Yellow
    }
}
