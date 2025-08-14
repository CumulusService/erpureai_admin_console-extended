# PowerShell script to extract SAP HANA native libraries from NuGet package
Write-Host "Extracting SAP HANA native libraries for Azure deployment..."

$packagePath = "LocalNuGetPackages/SAPHANAClient.1.0.0.nupkg"
if (Test-Path $packagePath) {
    Write-Host "Found SAP HANA NuGet package"
    
    # Create temp directory and extract (nupkg is just a zip file)
    $tempDir = "temp_hana_extract"
    $zipPath = "$tempDir.zip"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Copy-Item $packagePath $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force
    Remove-Item $zipPath
    
    # Copy native libraries
    $nativeLibsPath = "$tempDir/runtimes/win-x64/native"
    if (Test-Path $nativeLibsPath) {
        Copy-Item "$nativeLibsPath/*.dll" -Destination "." -Force
        Write-Host "Copied SAP HANA native libraries"
        Get-ChildItem -Path "." -Filter "*adonet*.dll" | ForEach-Object { Write-Host "  - $($_.Name)" }
        Get-ChildItem -Path "." -Filter "*SQLDB*.dll" | ForEach-Object { Write-Host "  - $($_.Name)" }
    }
    
    # Clean up
    Remove-Item -Path $tempDir -Recurse -Force
} else {
    Write-Host "SAP HANA NuGet package not found"
}

Write-Host "SAP HANA library extraction completed"