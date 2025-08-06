# PowerShell Scripts for AdminConsole

This folder contains PowerShell scripts used by the AdminConsole application for automated Microsoft Teams management.

## Set-TeamsAppPermissionPolicies.ps1

**Purpose**: Automatically configures Microsoft Teams App permission policies to restrict app availability to specific Azure AD groups.

**Prerequisites**:
- PowerShell 5.1 or later
- MicrosoftTeams PowerShell module (auto-installed if missing)
- Appropriate Azure AD permissions for Teams administration
- Service Principal with Teams.ReadWrite.All permissions

**Usage**:
This script is automatically executed by the C# application when M365 groups are created and converted to Teams. It handles:

1. **Multiple Agent Types**: Creates separate permission policies for each Teams App ID associated with different agent types
2. **Group-based Restrictions**: Restricts each app to only be available to the specific Azure AD group
3. **Automated Policy Management**: Creates, updates, and assigns policies without manual intervention
4. **Error Handling**: Provides detailed logging and graceful error handling

**Parameters**:
- `TenantId`: Azure AD Tenant ID
- `GroupId`: Azure AD Group ID that should have access to the apps
- `GroupName`: Display name of the group (for policy naming)
- `TeamsAppIds`: Array of Teams App IDs to configure policies for
- `PolicyPrefix`: Optional prefix for policy names (default: "AdminConsole")

**Output**:
The script returns JSON results that the C# application parses to determine success/failure status and detailed results.

## Security Notes

- Scripts are executed with restricted permissions using PowerShell execution policies
- Service Principal authentication is used (no interactive login required)
- All operations are logged for audit purposes
- Scripts timeout after 10 minutes to prevent hanging