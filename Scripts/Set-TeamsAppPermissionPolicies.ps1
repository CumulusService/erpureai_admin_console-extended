param(
    [Parameter(Mandatory=$true)]
    [string]$TenantId,
    
    [Parameter(Mandatory=$true)]
    [string]$GroupId,
    
    [Parameter(Mandatory=$true)]
    [string]$GroupName,
    
    [Parameter(Mandatory=$true)]
    [string[]]$TeamsAppIds,
    
    [Parameter(Mandatory=$false)]
    [string]$PolicyPrefix = "AdminConsole",
    
    [Parameter(Mandatory=$false)]
    [string]$ClientId,
    
    [Parameter(Mandatory=$false)]
    [string]$ClientSecret
)

Write-Host "Starting Teams App Permission Policy Setup" -ForegroundColor Green
Write-Host "Tenant ID: $TenantId" -ForegroundColor White
Write-Host "Group ID: $GroupId" -ForegroundColor White  
Write-Host "Group Name: $GroupName" -ForegroundColor White
Write-Host "Teams App IDs: $($TeamsAppIds -join ', ')" -ForegroundColor White
Write-Host "Policy Prefix: $PolicyPrefix" -ForegroundColor White

$successfulPolicies = @()
$failedPolicies = @()
$teamsConnected = $false

try {
    # Check Teams module
    Write-Host "Checking Teams PowerShell module..." -ForegroundColor Yellow
    
    $teamsModule = Get-Module -ListAvailable -Name MicrosoftTeams
    if (-not $teamsModule) {
        Write-Host "Installing MicrosoftTeams module..." -ForegroundColor Red
        Install-Module -Name MicrosoftTeams -Force -AllowClobber -Scope CurrentUser
        Write-Host "MicrosoftTeams module installed successfully" -ForegroundColor Green
    } else {
        Write-Host "MicrosoftTeams module found" -ForegroundColor Green
    }

    # Connect to Teams
    Write-Host "Connecting to Microsoft Teams..." -ForegroundColor Yellow
    Import-Module MicrosoftTeams -Force
    
    # Check if we have service principal credentials
    if ($ClientId -and $ClientSecret) {
        Write-Host "Using service principal authentication..." -ForegroundColor Cyan
        $secureClientSecret = ConvertTo-SecureString $ClientSecret -AsPlainText -Force
        $credential = New-Object System.Management.Automation.PSCredential($ClientId, $secureClientSecret)
        Connect-MicrosoftTeams -TenantId $TenantId -ApplicationId $ClientId -Credential $credential -ErrorAction Stop
    } else {
        # Fall back to environment variables or interactive login
        $envClientId = $env:AZURE_CLIENT_ID
        $envClientSecret = $env:AZURE_CLIENT_SECRET
        
        if ($envClientId -and $envClientSecret) {
            Write-Host "Using environment variables for service principal authentication..." -ForegroundColor Cyan
            $secureClientSecret = ConvertTo-SecureString $envClientSecret -AsPlainText -Force
            $credential = New-Object System.Management.Automation.PSCredential($envClientId, $secureClientSecret)
            Connect-MicrosoftTeams -TenantId $TenantId -ApplicationId $envClientId -Credential $credential -ErrorAction Stop
        } else {
            Write-Host "WARNING: No service principal credentials provided. This will require interactive login." -ForegroundColor Yellow
            Connect-MicrosoftTeams -TenantId $TenantId -ErrorAction Stop
        }
    }
    
    $teamsConnected = $true
    Write-Host "Successfully connected to Teams" -ForegroundColor Green

    # Process each Teams App ID
    foreach ($appId in $TeamsAppIds) {
        Write-Host "Processing Teams App: $appId" -ForegroundColor Cyan
        
        $cleanGroupName = $GroupName -replace '[^a-zA-Z0-9]', ''
        $shortAppId = $appId.Substring(0,[Math]::Min(8,$appId.Length))
        $policyName = "$PolicyPrefix-$shortAppId-$cleanGroupName"
        
        try {
            # Check if app exists in tenant catalog
            Write-Host "  Checking if app exists in tenant catalog..." -ForegroundColor White
            $appInfo = Get-CsTeamsApp -Id $appId -ErrorAction SilentlyContinue
            
            if (-not $appInfo) {
                Write-Host "  App $appId not found in tenant catalog" -ForegroundColor Yellow
            } else {
                Write-Host "  App found: $($appInfo.DisplayName)" -ForegroundColor Green
            }

            # Create or update permission policy for this app
            Write-Host "  Creating permission policy: $policyName" -ForegroundColor White
            $existingPolicy = Get-CsTeamsAppPermissionPolicy -Identity $policyName -ErrorAction SilentlyContinue
            
            if ($existingPolicy) {
                Write-Host "  Policy already exists. Updating..." -ForegroundColor Yellow
                Set-CsTeamsAppPermissionPolicy -Identity $policyName -DefaultCatalogAppsType AllowedAppList -DefaultCatalogAppsList $appId -Description "Auto-generated policy for app $appId restricted to group $GroupName (ID: $GroupId)"
                Write-Host "  Updated existing policy: $policyName" -ForegroundColor Green
            } else {
                New-CsTeamsAppPermissionPolicy -Identity $policyName -DefaultCatalogAppsType AllowedAppList -DefaultCatalogAppsList $appId -Description "Auto-generated policy for app $appId restricted to group $GroupName (ID: $GroupId)"
                Write-Host "  Created new policy: $policyName" -ForegroundColor Green
            }

            # Assign policy to the Azure AD group
            Write-Host "  Assigning policy to group $GroupId..." -ForegroundColor White
            New-CsGroupPolicyAssignment -GroupId $GroupId -PolicyType TeamsAppPermissionPolicy -PolicyName $policyName -ErrorAction Stop
            Write-Host "  Successfully assigned policy to group" -ForegroundColor Green
            
            $successfulPolicies += [PSCustomObject]@{
                AppId = $appId
                PolicyName = $policyName
                GroupId = $GroupId
                Status = "Success"
            }
            
        } catch {
            Write-Host "  Failed to process app $appId : $($_.Exception.Message)" -ForegroundColor Red
            $failedPolicies += [PSCustomObject]@{
                AppId = $appId
                PolicyName = $policyName
                GroupId = $GroupId
                Status = "Failed"
                Error = $_.Exception.Message
            }
        }
    }

    # Summary
    Write-Host "SUMMARY" -ForegroundColor Green
    Write-Host "Successful policies: $($successfulPolicies.Count)" -ForegroundColor Green
    Write-Host "Failed policies: $($failedPolicies.Count)" -ForegroundColor Red

    if ($successfulPolicies.Count -gt 0) {
        Write-Host "Successfully configured policies:" -ForegroundColor Green
        foreach ($policy in $successfulPolicies) {
            Write-Host "  - App: $($policy.AppId) -> Policy: $($policy.PolicyName)" -ForegroundColor White
        }
    }

    if ($failedPolicies.Count -gt 0) {
        Write-Host "Failed policies:" -ForegroundColor Yellow
        foreach ($policy in $failedPolicies) {
            Write-Host "  - App: $($policy.AppId) -> Status: $($policy.Status)" -ForegroundColor White
            if ($policy.Error) {
                Write-Host "    Error: $($policy.Error)" -ForegroundColor Red
            }
        }
    }

    # Return results as JSON
    $result = @{
        Success = $true
        TotalApps = $TeamsAppIds.Count
        SuccessfulPolicies = $successfulPolicies.Count
        FailedPolicies = $failedPolicies.Count
        Results = @{
            Successful = $successfulPolicies
            Failed = $failedPolicies
        }
        Message = "Teams App permission policies processed successfully"
    }

    Write-Output "RESULT_JSON_START"
    Write-Output ($result | ConvertTo-Json -Depth 5)
    Write-Output "RESULT_JSON_END"

    Write-Host "Script completed successfully!" -ForegroundColor Green

} catch {
    Write-Host "CRITICAL ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    
    $errorResult = @{
        Success = $false
        Error = $_.Exception.Message
        StackTrace = $_.ScriptStackTrace
        Message = "Teams App permission policy setup failed"
    }
    
    Write-Output "RESULT_JSON_START"
    Write-Output ($errorResult | ConvertTo-Json -Depth 3)
    Write-Output "RESULT_JSON_END"
    
    exit 1
} finally {
    if ($teamsConnected) {
        try {
            Disconnect-MicrosoftTeams -ErrorAction SilentlyContinue
            Write-Host "Disconnected from Teams" -ForegroundColor Gray
        } catch {
            # Ignore disconnect errors
        }
    }
}