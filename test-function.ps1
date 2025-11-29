# Test Azure Function - ProcessBlobFiles with SharePoint Integration
# This script sends a POST request to the locally running Azure Function

$uri = "http://localhost:7071/api/ProcessBlobFiles"
$body = @{
    blobFolderPath = "CaseDocs/0770001438/Subpoena/"
} | ConvertTo-Json

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Azure Function - SharePoint Integration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Sending POST request to: $uri" -ForegroundColor Cyan
Write-Host "Request Body: $body" -ForegroundColor Yellow
Write-Host ""
Write-Host "⏳ Processing... (This may take a minute for SharePoint operations)" -ForegroundColor Yellow
Write-Host ""

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = Invoke-RestMethod -Uri $uri -Method Post -Body $body -ContentType "application/json"
    
    $stopwatch.Stop()
    
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "✅ SUCCESS - Response Received" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Status:          " -NoNewline; Write-Host $response.status -ForegroundColor $(if($response.status -eq "Success"){"Green"}else{"Yellow"})
    Write-Host "Message:         " -NoNewline; Write-Host $response.message -ForegroundColor White
    Write-Host "Folder Path:     " -NoNewline; Write-Host $response.folderPath -ForegroundColor White
    Write-Host ""
    Write-Host "--- File Statistics ---" -ForegroundColor Cyan
    Write-Host "Files Found:     " -NoNewline; Write-Host $response.filesCount -ForegroundColor White
    Write-Host "Files Processed: " -NoNewline; Write-Host $response.filesProcessed -ForegroundColor Green
    Write-Host "Files Failed:    " -NoNewline; Write-Host $response.filesFailed -ForegroundColor $(if($response.filesFailed -eq 0){"Green"}else{"Red"})
    Write-Host ""
    
    if ($response.filesFailed -gt 0 -and $response.failedFiles) {
        Write-Host "--- Failed Files ---" -ForegroundColor Red
        foreach ($failed in $response.failedFiles) {
            Write-Host "  ❌ $($failed.fileName)" -ForegroundColor Red
            Write-Host "     Error: $($failed.errorMessage)" -ForegroundColor Yellow
        }
        Write-Host ""
    }
    
    Write-Host "Execution Time:  " -NoNewline; Write-Host "$($stopwatch.Elapsed.TotalSeconds.ToString('0.00')) seconds" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "✅ Test Completed Successfully!" -ForegroundColor Green
}
catch {
    $stopwatch.Stop()
    
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "❌ ERROR" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Message: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    
    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $reader.BaseStream.Position = 0
            $reader.DiscardBufferedData()
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body:" -ForegroundColor Yellow
            Write-Host $responseBody -ForegroundColor White
        }
        catch {
            Write-Host "Could not read error response body" -ForegroundColor Yellow
        }
    }
    
    Write-Host ""
    Write-Host "Execution Time: $($stopwatch.Elapsed.TotalSeconds.ToString('0.00')) seconds" -ForegroundColor Cyan
}
