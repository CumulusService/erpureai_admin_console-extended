param(
    [Parameter(Mandatory=$true)]
    [string]$TenantId,
    
    [Parameter(Mandatory=$true)]
    [string]$ClientId,
    
    [Parameter(Mandatory=$true)]
    [string]$ClientSecret
)

Write-Host "Checking Service Principal Permissions" -ForegroundColor Green
Write-Host "Tenant ID: $TenantId" -ForegroundColor White
Write-Host "Client ID: $ClientId" -ForegroundColor White

try {
    # Install Microsoft Graph module if needed
    $graphModule = Get-Module -ListAvailable -Name Microsoft.Graph.Authentication
    if (-not $graphModule) {
        Write-Host "Installing Microsoft.Graph module..." -ForegroundColor Yellow
        Install-Module -Name Microsoft.Graph -Force -AllowClobber -Scope CurrentUser
    }

    # Connect to Microsoft Graph
    Write-Host "Connecting to Microsoft Graph..." -ForegroundColor Yellow
    Import-Module Microsoft.Graph.Authentication -Force
    Import-Module Microsoft.Graph.Applications -Force
    
    # Use environment variables for authentication
    $env:AZURE_CLIENT_ID = $ClientId
    $env:AZURE_CLIENT_SECRET = $ClientSecret  
    $env:AZURE_TENANT_ID = $TenantId
    
    Connect-MgGraph -EnvironmentVariable -ErrorAction Stop

    Write-Host "Successfully connected to Microsoft Graph" -ForegroundColor Green

    # Get service principal
    Write-Host "Getting service principal information..." -ForegroundColor Yellow
    $servicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$ClientId'"
    
    if ($servicePrincipal) {
        Write-Host "Service Principal found: $($servicePrincipal.DisplayName)" -ForegroundColor Green
        Write-Host "Object ID: $($servicePrincipal.Id)" -ForegroundColor White
        
        # Get app roles (permissions)
        Write-Host "" 
        Write-Host "Current API Permissions:" -ForegroundColor Cyan
        $appRoleAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $servicePrincipal.Id
        
        foreach ($assignment in $appRoleAssignments) {
            $resourceSP = Get-MgServicePrincipal -ServicePrincipalId $assignment.ResourceId
            $appRole = $resourceSP.AppRoles | Where-Object { $_.Id -eq $assignment.AppRoleId }
            
            if ($appRole) {
                $status = if ($resourceSP.DisplayName -eq "Microsoft Graph") { "[GRAPH]" } else { "[OTHER]" }
                Write-Host "$status $($resourceSP.DisplayName): $($appRole.Value)" -ForegroundColor White
            }
        }
        
        # Check for specific Teams permissions
        Write-Host ""
        Write-Host "Checking for required Teams permissions:" -ForegroundColor Cyan
        $requiredPermissions = @(
            "TeamsAppInstallation.ReadWriteForTeam.All",
            "TeamworkAppSettings.ReadWrite.All", 
            "Team.ReadBasic.All",
            "TeamsAppInstallation.ReadWriteSelfForTeam.All",
            "TeamMember.ReadWrite.All"
        )
        
        $graphSP = Get-MgServicePrincipal -Filter "displayName eq 'Microsoft Graph'"
        $currentPermissions = $appRoleAssignments | Where-Object { $_.ResourceId -eq $graphSP.Id }
        
        foreach ($requiredPerm in $requiredPermissions) {
            $appRole = $graphSP.AppRoles | Where-Object { $_.Value -eq $requiredPerm }
            if ($appRole) {
                $hasPermission = $currentPermissions | Where-Object { $_.AppRoleId -eq $appRole.Id }
                $status = if ($hasPermission) { "[GRANTED]" } else { "[MISSING]" }
                Write-Host "$status $requiredPerm" -ForegroundColor $(if ($hasPermission) { "Green" } else { "Red" })
            } else {
                Write-Host "[UNKNOWN] $requiredPerm (permission may not exist)" -ForegroundColor Yellow
            }
        }
        
    } else {
        Write-Host "Service Principal not found!" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "Permission check completed!" -ForegroundColor Green

} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    try {
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    } catch {
        # Ignore disconnect errors
    }
}