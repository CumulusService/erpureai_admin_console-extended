# PowerShell script to upload SAP HANA Client package to Azure Artifacts
# Run this script after setting up your Azure Artifacts feed

param(
    [Parameter(Mandatory=$true)]
    [string]$Organization,
    
    [Parameter(Mandatory=$true)]
    [string]$FeedUrl,
    
    [Parameter(Mandatory=$false)]
    [string]$PackagePath = "LocalNuGetPackages/SAPHANAClient.1.0.0.nupkg"
)

Write-Host "🚀 Uploading SAP HANA Client package to Azure Artifacts..." -ForegroundColor Green

# Verify package exists
if (-not (Test-Path $PackagePath)) {
    Write-Error "Package not found at: $PackagePath"
    Write-Host "Please ensure the NuGet package has been built first by running:" -ForegroundColor Yellow
    Write-Host "  cd NuGet/SAPHANAClient && dotnet pack --configuration Release" -ForegroundColor Yellow
    exit 1
}

Write-Host "📦 Package found: $PackagePath" -ForegroundColor Green

# Install Azure Artifacts Credential Provider if not already installed
Write-Host "🔧 Installing Azure Artifacts Credential Provider..." -ForegroundColor Blue
try {
    $env:NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED = "true"
    iex "& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) }"
    Write-Host "✅ Azure Artifacts Credential Provider installed successfully" -ForegroundColor Green
} catch {
    Write-Warning "Failed to install credential provider automatically. Please install manually."
    Write-Host "Download from: https://github.com/microsoft/artifacts-credprovider" -ForegroundColor Yellow
}

# Add Azure Artifacts feed as NuGet source
Write-Host "🔗 Adding Azure Artifacts feed as NuGet source..." -ForegroundColor Blue
try {
    dotnet nuget add source $FeedUrl --name "AzureArtifacts"
    Write-Host "✅ Azure Artifacts feed added successfully" -ForegroundColor Green
} catch {
    Write-Warning "Feed may already exist or there was an error adding it."
}

# Upload the package
Write-Host "📤 Uploading package to Azure Artifacts..." -ForegroundColor Blue
Write-Host "You may be prompted to authenticate with your Azure DevOps account." -ForegroundColor Yellow

try {
    dotnet nuget push $PackagePath --source "AzureArtifacts" --interactive
    Write-Host "🎉 Package uploaded successfully to Azure Artifacts!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Blue
    Write-Host "1. Add GitHub secrets: AZURE_ARTIFACTS_FEED_URL and AZURE_ARTIFACTS_TOKEN" -ForegroundColor White
    Write-Host "2. Update nuget.config to use Azure Artifacts feed URL" -ForegroundColor White
    Write-Host "3. Update GitHub Actions workflow to use Azure Artifacts authentication" -ForegroundColor White
    Write-Host "4. Test the deployment pipeline" -ForegroundColor White
} catch {
    Write-Error "Failed to upload package. Error: $_"
    Write-Host ""
    Write-Host "Troubleshooting tips:" -ForegroundColor Yellow
    Write-Host "1. Ensure you have push permissions to the Azure Artifacts feed" -ForegroundColor White
    Write-Host "2. Check that your Azure DevOps organization and feed names are correct" -ForegroundColor White
    Write-Host "3. Verify your Azure DevOps account has the required permissions" -ForegroundColor White
    exit 1
}

Write-Host ""
Write-Host "🔍 To verify the upload, go to your Azure DevOps project:" -ForegroundColor Blue
Write-Host "   https://dev.azure.com/$Organization/{project}/_artifacts/feed/sap-hana-packages" -ForegroundColor Cyan