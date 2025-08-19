using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Implementation of system-level user management for Master Developer users
/// Provides comprehensive user promotion, role assignment, and management capabilities
/// </summary>
public class SystemUserManagementService : ISystemUserManagementService
{
    private readonly IGraphService _graphService;
    private readonly IOnboardedUserService _userService;
    private readonly AdminConsoleDbContext _dbContext;
    private readonly ILogger<SystemUserManagementService> _logger;

    public SystemUserManagementService(
        IGraphService graphService, 
        IOnboardedUserService userService,
        AdminConsoleDbContext dbContext,
        ILogger<SystemUserManagementService> logger)
    {
        _graphService = graphService;
        _userService = userService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<SystemUser>> GetAllTenantUsersAsync()
    {
        try
        {
            _logger.LogInformation("🔍 SystemUserManagementService: Starting GetAllTenantUsersAsync");
            var allTenantUsers = await _graphService.GetAllTenantUsersAsync();
            _logger.LogInformation("🔍 SystemUserManagementService: Retrieved {Count} tenant users from GraphService", allTenantUsers.Count);
            
            // Filter to only show users who are part of organizations in the app
            var filteredTenantUsers = await FilterTenantUsersForOrganizations(allTenantUsers);
            _logger.LogInformation("🔍 SystemUserManagementService: Filtered to {Count} organization-linked tenant users", filteredTenantUsers.Count);
            
            var enrichedUsers = await EnrichWithDatabaseInfoAsync(filteredTenantUsers);
            _logger.LogInformation("✅ SystemUserManagementService: Successfully enriched {Count} tenant users", enrichedUsers.Count);
            return enrichedUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SystemUserManagementService: Error retrieving tenant users - {ErrorMessage}", ex.Message);
            _logger.LogError("❌ SystemUserManagementService: Full exception details: {FullException}", ex.ToString());
            return new List<SystemUser>();
        }
    }

    public async Task<List<SystemUser>> GetAllGuestUsersAsync()
    {
        try
        {
            _logger.LogInformation("🔍 SystemUserManagementService: Starting GetAllGuestUsersAsync");
            var guestUsers = await _graphService.GetAllGuestUsersAsync();
            _logger.LogInformation("🔍 SystemUserManagementService: Retrieved {Count} guest users from GraphService", guestUsers.Count);
            
            var enrichedUsers = await EnrichWithDatabaseInfoAsync(guestUsers);
            _logger.LogInformation("✅ SystemUserManagementService: Successfully enriched {Count} guest users", enrichedUsers.Count);
            return enrichedUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SystemUserManagementService: Error retrieving guest users - {ErrorMessage}", ex.Message);
            _logger.LogError("❌ SystemUserManagementService: Full exception details: {FullException}", ex.ToString());
            return new List<SystemUser>();
        }
    }

    public async Task<List<SystemUser>> GetAllSystemUsersAsync()
    {
        try
        {
            var allUsers = await _graphService.GetAllUsersAsync();
            return await EnrichWithDatabaseInfoAsync(allUsers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all system users");
            return new List<SystemUser>();
        }
    }

    public async Task<List<SystemUser>> GetTenantUsersByDomainAsync(string domain)
    {
        try
        {
            var domainUsers = await _graphService.GetTenantUsersByDomainAsync(domain);
            return await EnrichWithDatabaseInfoAsync(domainUsers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant users for domain {Domain}", domain);
            return new List<SystemUser>();
        }
    }

    public async Task<UserPromotionResult> PromoteUserAsync(string userId, UserRole targetRole, Guid? organizationId = null)
    {
        var result = new UserPromotionResult
        {
            UserId = userId,
            TargetRole = targetRole
        };

        try
        {
            // Get user information from Azure AD
            var azureUser = await _graphService.GetUserByEmailAsync("");
            if (azureUser == null)
            {
                // Try to get user by ID directly from Graph
                var allUsers = await _graphService.GetAllUsersAsync();
                azureUser = allUsers.FirstOrDefault(u => u.Id == userId);
            }

            if (azureUser == null)
            {
                result.Errors.Add("User not found in Azure AD");
                return result;
            }

            result.UserEmail = azureUser.Email;

            // Check if user already has database record (relies on SQL Server case-insensitive collation - avoids ToLower() performance issue)
            var existingUser = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.Email == azureUser.Email);

            if (existingUser != null)
            {
                // Update existing record
                existingUser.AssignedRole = targetRole;
                existingUser.ModifiedOn = DateTime.UtcNow;
                
                if (organizationId.HasValue)
                {
                    existingUser.OwnerId = organizationId.Value;
                }

                result.DatabaseRecordCreated = false;
            }
            else
            {
                // Create new database record
                var ownerId = organizationId ?? Guid.NewGuid(); // Use provided org or create placeholder
                
                var newUser = new OnboardedUser
                {
                    OnboardedUserId = Guid.Parse(userId),
                    Name = azureUser.DisplayName,
                    Email = azureUser.Email,
                    AssignedRole = targetRole,
                    IsActive = true,
                    StateCode = StateCode.Active,
                    StatusCode = StatusCode.Active,
                    CreatedOn = DateTime.UtcNow,
                    ModifiedOn = DateTime.UtcNow,
                    OwnerId = ownerId
                };

                _dbContext.OnboardedUsers.Add(newUser);
                result.DatabaseRecordCreated = true;
            }

            await _dbContext.SaveChangesAsync();

            // Assign Azure AD app role
            var appRoleName = GetAppRoleName(targetRole);
            if (!string.IsNullOrEmpty(appRoleName))
            {
                result.AppRoleAssigned = await _graphService.AssignAppRoleToUserAsync(userId, appRoleName);
                if (!result.AppRoleAssigned)
                {
                    result.Warnings.Add($"Database updated but Azure AD app role '{appRoleName}' assignment failed");
                }
            }

            result.Success = true;
            _logger.LogInformation("Successfully promoted user {Email} to {Role}", azureUser.Email, targetRole);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting user {UserId} to role {Role}", userId, targetRole);
            result.Errors.Add($"Promotion failed: {ex.Message}");
        }

        return result;
    }

    public async Task<UserDemotionResult> DemoteUserAsync(string userId, UserRole? targetRole = null)
    {
        var result = new UserDemotionResult
        {
            UserId = userId
        };

        try
        {
            // Find user in database
            var user = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == Guid.Parse(userId));

            if (user == null)
            {
                result.Errors.Add("User not found in database");
                return result;
            }

            result.UserEmail = user.Email;
            result.PreviousRole = user.GetUserRole();

            if (targetRole.HasValue)
            {
                // Demote to lower role
                user.AssignedRole = targetRole.Value;
                user.ModifiedOn = DateTime.UtcNow;
                result.NewRole = targetRole.Value;
                result.DatabaseRecordUpdated = true;
                
                // Update Azure AD app role
                var oldAppRole = GetAppRoleName(result.PreviousRole.Value);
                var newAppRole = GetAppRoleName(targetRole.Value);
                
                if (!string.IsNullOrEmpty(oldAppRole))
                {
                    await _graphService.RevokeAppRoleFromUserAsync(userId, oldAppRole);
                }
                
                if (!string.IsNullOrEmpty(newAppRole))
                {
                    result.AppRoleRevoked = await _graphService.AssignAppRoleToUserAsync(userId, newAppRole);
                }
            }
            else
            {
                // CRITICAL: Mark as inactive instead of removing (keep for audit trail)
                user.IsActive = false;
                user.StatusCode = StatusCode.Inactive;
                user.ModifiedOn = DateTime.UtcNow;
                result.DatabaseRecordUpdated = true; // Changed from DatabaseRecordRemoved
                
                // Revoke all app roles and disable Entra ID account
                var appRole = GetAppRoleName(result.PreviousRole.Value);
                if (!string.IsNullOrEmpty(appRole))
                {
                    result.AppRoleRevoked = await _graphService.RevokeAppRoleFromUserAsync(userId, appRole);
                }
                
                // Disable Entra ID account to align with database status
                await _graphService.DisableUserAccountAsync(userId);
                
                _logger.LogInformation("User {Email} marked as INACTIVE in database and disabled in Entra ID", user.Email);
            }

            await _dbContext.SaveChangesAsync();
            result.Success = true;
            
            _logger.LogInformation("Successfully demoted user {Email} from {PreviousRole} to {NewRole}", 
                user.Email, result.PreviousRole, targetRole?.ToString() ?? "removed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error demoting user {UserId}", userId);
            result.Errors.Add($"Demotion failed: {ex.Message}");
        }

        return result;
    }

    public async Task<UserCreationResult> CreateSystemUserAsync(string email, string displayName, UserRole role, Guid? organizationId = null)
    {
        var result = new UserCreationResult
        {
            UserEmail = email,
            AssignedRole = role
        };

        try
        {
            // Send B2B invitation
            var inviteResult = await _graphService.InviteGuestUserAsync(email, displayName, "System Administrator", "https://localhost:5243", new List<string>(), true);
            if (!inviteResult.Success)
            {
                result.Errors.AddRange(inviteResult.Errors);
                return result;
            }

            result.UserId = inviteResult.UserId;
            result.InvitationUrl = inviteResult.InvitationUrl;
            result.InvitationSent = true;

            // Create database record
            var ownerId = organizationId ?? Guid.NewGuid();
            var newUser = new OnboardedUser
            {
                OnboardedUserId = Guid.Parse(inviteResult.UserId),
                Name = displayName,
                Email = email,
                AssignedRole = role,
                IsActive = true,
                StateCode = StateCode.Active,
                StatusCode = StatusCode.Active,
                CreatedOn = DateTime.UtcNow,
                ModifiedOn = DateTime.UtcNow,
                OwnerId = ownerId
            };

            _dbContext.OnboardedUsers.Add(newUser);
            await _dbContext.SaveChangesAsync();
            result.DatabaseRecordCreated = true;

            // Note: App role assignment will happen when user accepts invitation
            result.Success = true;
            
            _logger.LogInformation("Successfully created system user {Email} with role {Role}", email, role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating system user {Email} with role {Role}", email, role);
            result.Errors.Add($"User creation failed: {ex.Message}");
        }

        return result;
    }

    public async Task<SystemUserDetails?> GetSystemUserDetailsAsync(string userId)
    {
        try
        {
            // Get user from Azure AD
            var allUsers = await _graphService.GetAllUsersAsync();
            var azureUser = allUsers.FirstOrDefault(u => u.Id == userId);
            
            if (azureUser == null)
            {
                return null;
            }

            // Get database information
            var dbUser = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == Guid.Parse(userId));

            // Get group memberships
            var groupMemberships = await _graphService.GetUserGroupMembershipsAsync(userId);

            var details = new SystemUserDetails
            {
                Id = azureUser.Id,
                Email = azureUser.Email,
                DisplayName = azureUser.DisplayName,
                UserType = azureUser.UserType,
                IsEnabled = azureUser.IsEnabled,
                CreatedDateTime = azureUser.CreatedOn,
                HasDatabaseRecord = dbUser != null,
                DatabaseRole = dbUser?.GetUserRole(),
                OrganizationId = dbUser?.OwnerId,
                OrganizationName = "System User", // TODO: Add Organization navigation property
                DatabaseCreatedOn = dbUser?.CreatedOn,
                GroupMemberships = groupMemberships.Select(g => g.DisplayName ?? g.Id ?? "").ToList()
            };

            // Populate available actions
            PopulateAvailableActions(details);

            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system user details for {UserId}", userId);
            return null;
        }
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, UserRole newRole)
    {
        try
        {
            _logger.LogInformation("🔄 UpdateUserRoleAsync: Starting role update for UserId={UserId} to Role={NewRole}", userId, newRole);
            
            // Validate userId is a valid GUID
            if (!Guid.TryParse(userId, out var userGuid))
            {
                _logger.LogError("❌ UpdateUserRoleAsync: Invalid UserId format - {UserId} is not a valid GUID", userId);
                return false;
            }
            
            var user = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == userGuid);

            if (user == null)
            {
                _logger.LogWarning("❌ UpdateUserRoleAsync: User not found in database for UserId={UserId}", userId);
                return false;
            }
            
            _logger.LogInformation("💾 UpdateUserRoleAsync: Found user - Email={Email}, CurrentRole={CurrentRole}, NewRole={NewRole}", 
                user.Email, user.AssignedRole, newRole);

            var oldRole = user.AssignedRole; // Direct access since it's now non-nullable
            
            // Check if role is actually changing
            if (oldRole == newRole)
            {
                _logger.LogInformation("🔄 UpdateUserRoleAsync: Role unchanged for user {Email} - already {Role}", user.Email, newRole);
                return true; // No change needed
            }
            
            user.AssignedRole = newRole;
            user.ModifiedOn = DateTime.UtcNow;
            
            _logger.LogInformation("💾 UpdateUserRoleAsync: Saving database changes for user {Email}", user.Email);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("✅ UpdateUserRoleAsync: Database updated successfully for user {Email}", user.Email);

            // Update Azure AD app role
            var oldAppRole = GetAppRoleName(oldRole);
            var newAppRole = GetAppRoleName(newRole);
            
            _logger.LogInformation("🌐 UpdateUserRoleAsync: Azure AD role update - Old={OldAppRole}, New={NewAppRole}", oldAppRole, newAppRole);
            
            if (!string.IsNullOrEmpty(oldAppRole))
            {
                _logger.LogInformation("🚫 UpdateUserRoleAsync: Revoking old app role {OldAppRole} from user {Email}", oldAppRole, user.Email);
                var revokeResult = await _graphService.RevokeAppRoleFromUserAsync(userId, oldAppRole);
                _logger.LogInformation("🚫 UpdateUserRoleAsync: Revoke result for {Email}: {RevokeResult}", user.Email, revokeResult);
            }

            if (!string.IsNullOrEmpty(newAppRole))
            {
                _logger.LogInformation("➕ UpdateUserRoleAsync: Assigning new app role {NewAppRole} to user {Email}", newAppRole, user.Email);
                var assignResult = await _graphService.AssignAppRoleToUserAsync(userId, newAppRole);
                _logger.LogInformation("➕ UpdateUserRoleAsync: Assign result for {Email}: {AssignResult}", user.Email, assignResult);
            }

            _logger.LogInformation("✅ UpdateUserRoleAsync: Successfully updated role for user {Email} from {OldRole} to {NewRole}", 
                user.Email, oldRole, newRole);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 UpdateUserRoleAsync: CRITICAL ERROR updating user role for {UserId} to {Role}: {ErrorMessage}", 
                userId, newRole, ex.Message);
            _logger.LogError("💥 UpdateUserRoleAsync: Full exception details: {FullException}", ex.ToString());
            return false;
        }
    }

    public async Task<bool> DeactivateSystemUserAsync(string userId)
    {
        try
        {
            _logger.LogInformation("🔄 DeactivateSystemUserAsync: Starting deactivation for UserId={UserId}", userId);
            
            // CRITICAL: Mark user as inactive in database (DO NOT REMOVE - keep for audit trail)
            var user = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.OnboardedUserId == Guid.Parse(userId));

            if (user != null)
            {
                _logger.LogInformation("💾 DeactivateSystemUserAsync: Marking user {Email} as INACTIVE in database", user.Email);
                
                // Mark as inactive instead of removing
                user.IsActive = false;
                user.StatusCode = StatusCode.Inactive;
                user.ModifiedOn = DateTime.UtcNow;
                
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("✅ DeactivateSystemUserAsync: Database record marked as INACTIVE for user {Email}", user.Email);
            }
            else
            {
                _logger.LogWarning("⚠️ DeactivateSystemUserAsync: No database record found for UserId={UserId}", userId);
            }

            // Disable Azure AD account to align with database status
            _logger.LogInformation("🌐 DeactivateSystemUserAsync: Disabling Entra ID account for UserId={UserId}", userId);
            var azureResult = await _graphService.DisableUserAccountAsync(userId);
            
            if (azureResult)
            {
                _logger.LogInformation("✅ DeactivateSystemUserAsync: Successfully disabled Entra ID account for UserId={UserId}", userId);
            }
            else
            {
                _logger.LogError("❌ DeactivateSystemUserAsync: Failed to disable Entra ID account for UserId={UserId}", userId);
            }
            
            return azureResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error deactivating system user {UserId}: {ErrorMessage}", userId, ex.Message);
            return false;
        }
    }

    public async Task<UserReactivationResult> ReactivateSystemUserAsync(string userId, UserRole role, Guid? organizationId = null)
    {
        try
        {
            // Use existing Graph service reactivation method
            var reactivationResult = await _graphService.ReactivateUserAccessAsync(userId, organizationId ?? Guid.NewGuid());
            
            if (reactivationResult.Success)
            {
                // Update/create database record with specific role
                await UpdateUserRoleAsync(userId, role);
            }
            
            return reactivationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating system user {UserId}", userId);
            return new UserReactivationResult
            {
                Success = false,
                UserId = userId,
                Errors = { $"Reactivation failed: {ex.Message}" }
            };
        }
    }

    public async Task<SystemUserStatistics> GetSystemStatisticsAsync()
    {
        try
        {
            var tenantUsers = await _graphService.GetAllTenantUsersAsync();
            var guestUsers = await _graphService.GetAllGuestUsersAsync();
            
            var dbUsers = await _dbContext.OnboardedUsers.ToListAsync();
            
            return new SystemUserStatistics
            {
                TotalTenantUsers = tenantUsers.Count,
                TotalGuestUsers = guestUsers.Count,
                TotalDatabaseUsers = dbUsers.Count,
                SuperAdminCount = dbUsers.Count(u => u.GetUserRole() == UserRole.SuperAdmin),
                DeveloperCount = dbUsers.Count(u => u.GetUserRole() == UserRole.Developer),
                OrgAdminCount = dbUsers.Count(u => u.GetUserRole() == UserRole.OrgAdmin),
                OrgUserCount = dbUsers.Count(u => u.GetUserRole() == UserRole.User),
                ActiveUsersCount = dbUsers.Count(u => u.IsActive),
                InactiveUsersCount = dbUsers.Count(u => !u.IsActive)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system statistics");
            return new SystemUserStatistics();
        }
    }

    private async Task<List<SystemUser>> EnrichWithDatabaseInfoAsync(List<GuestUser> azureUsers)
    {
        try
        {
            _logger.LogInformation("🔍 EnrichWithDatabaseInfoAsync: Starting enrichment for {Count} Azure users", azureUsers.Count);
            
            // Load related data sequentially to avoid DbContext concurrency issues
            var dbUsers = await _dbContext.OnboardedUsers.ToListAsync();
            var organizations = await _dbContext.Organizations.ToListAsync();
            
            _logger.LogInformation("🔍 EnrichWithDatabaseInfoAsync: Loaded {DbUsersCount} database users and {OrgsCount} organizations", 
                dbUsers.Count, organizations.Count);

        var enrichedUsers = new List<SystemUser>();
        
        foreach (var azureUser in azureUsers)
        {
            var dbUser = dbUsers.FirstOrDefault(du => 
                string.Equals(du.Email, azureUser.Email, StringComparison.OrdinalIgnoreCase) ||
                du.AzureObjectId == azureUser.Id);
                

            // Determine real Console App user status
            var userStatus = await DetermineConsoleAppUserStatusAsync(azureUser, dbUser);
            
            // Get organization information
            var organization = dbUser?.OrganizationLookupId.HasValue == true 
                ? organizations.FirstOrDefault(o => o.OrganizationId == dbUser.OrganizationLookupId.Value)
                : null;

            var enrichedUser = new SystemUser
            {
                Id = azureUser.Id,
                Email = azureUser.Email,
                DisplayName = azureUser.DisplayName,
                UserType = azureUser.UserType,
                IsEnabled = azureUser.IsEnabled,
                CreatedDateTime = azureUser.CreatedOn,
                AzureObjectId = azureUser.Id,
                
                // Enhanced status information
                Status = userStatus,
                InvitationStatus = ParseInvitationStatus(azureUser.InvitationStatus),
                LastSignInDateTime = null, // TODO: Get from Graph API if needed
                InvitationAcceptedDate = null, // TODO: Get from Graph API if needed
                
                // Database information
                HasDatabaseRecord = dbUser != null,
                DatabaseRole = dbUser?.GetUserRole(),
                OrganizationId = dbUser?.OrganizationLookupId,
                OrganizationName = GetOrganizationDisplayName(organization, dbUser),
                DatabaseCreatedOn = dbUser?.CreatedOn,
                
                // Enhanced user context
                AgentTypeIds = dbUser?.AgentTypeIds ?? new List<Guid>(),
                AssignedAppRoles = new List<string>() // Will be populated by Graph API if needed
            };
            
            _logger.LogInformation("📝 EnrichedUser created for {Email}: Status={Status}, StatusDisplay={StatusDisplay}, BadgeClass={BadgeClass}, IsEnabled={IsEnabled}, HasDbRecord={HasDbRecord}",
                enrichedUser.Email, enrichedUser.Status, enrichedUser.StatusDisplayName, enrichedUser.StatusBadgeClass, enrichedUser.IsEnabled, enrichedUser.HasDatabaseRecord);

            enrichedUsers.Add(enrichedUser);
        }

            // Include all users but prioritize those with Console App access
            // Show disabled users if they have database records (so admins can manage them)
            var filteredUsers = enrichedUsers.Where(u => 
                u.HasDatabaseRecord ||  // Always show users with database records (onboarded users)
                u.IsSystemUser ||       // Always show system users (Developer/SuperAdmin)
                u.Status == SystemUserStatus.Active ||  // Show active users
                u.Status == SystemUserStatus.PendingInvitation ||  // Show pending invitations
                u.Status == SystemUserStatus.InvitationExpired     // Show expired invitations
            ).ToList();
            
            _logger.LogInformation("🔍 EnrichWithDatabaseInfoAsync: Filtering logic applied - Original: {OriginalCount}, Filtered: {FilteredCount}", 
                enrichedUsers.Count, filteredUsers.Count);
            
            _logger.LogInformation("✅ EnrichWithDatabaseInfoAsync: Filtered to {Count} users with Console App access", filteredUsers.Count);
            
            // Log final status summary for debugging
            foreach (var user in filteredUsers)
            {
                _logger.LogInformation("🔍 Final User Status - {Email}: Status={Status}, Display={StatusDisplay}, Badge={BadgeClass}",
                    user.Email, user.Status, user.StatusDisplayName, user.StatusBadgeClass);
            }
            
            return filteredUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ EnrichWithDatabaseInfoAsync: Error during enrichment - {ErrorMessage}", ex.Message);
            _logger.LogError("❌ EnrichWithDatabaseInfoAsync: Full exception details: {FullException}", ex.ToString());
            return new List<SystemUser>();
        }
    }

    private async Task<SystemUserStatus> DetermineConsoleAppUserStatusAsync(GuestUser azureUser, OnboardedUser? dbUser)
    {
        try
        {
            _logger.LogInformation("🔍 DetermineConsoleAppUserStatus for {Email}: UserType={UserType}, IsEnabled={IsEnabled}, HasDbRecord={HasDbRecord}, DbActive={DbActive}, DbRole={DbRole}", 
                azureUser.Email, azureUser.UserType, azureUser.IsEnabled, dbUser != null, dbUser?.IsActive, dbUser?.AssignedRole);
            
            
            // CRITICAL: Proper alignment of Entra ID and database status
            // When access is revoked: User should be DISABLED in Entra ID AND INACTIVE in database
            if (!azureUser.IsEnabled)
            {
                if (dbUser != null)
                {
                    if (dbUser.IsActive)
                    {
                        // INCONSISTENT STATE: User disabled in Entra ID but still active in database
                        // This could indicate a partial revocation or system inconsistency
                        _logger.LogError("🚨 INCONSISTENT STATE: User {Email} is DISABLED in Entra ID but has ACTIVE database record (Role: {Role}) - Status: Revoked (needs database cleanup)", 
                            azureUser.Email, dbUser.AssignedRole);
                        return SystemUserStatus.Revoked; // Needs attention - inconsistent state
                    }
                    else
                    {
                        // PROPER REVOKED STATE: User disabled in Entra ID AND inactive in database
                        _logger.LogInformation("✅ User {Email} properly REVOKED - DISABLED in Entra ID and INACTIVE in database (Role: {Role}) - Status: Revoked", 
                            azureUser.Email, dbUser.AssignedRole);
                        return SystemUserStatus.Revoked; // Properly revoked user
                    }
                }
                else
                {
                    // User disabled in Entra ID with no database record - never had system access
                    _logger.LogInformation("ℹ️ User {Email} is DISABLED in Entra ID with no database record - Status: Disabled (never had access)", azureUser.Email);
                    return SystemUserStatus.Disabled; // Never had system access
                }
            }
            
            // Check if user's organization is revoked/inactive
            if (dbUser?.OrganizationLookupId.HasValue == true)
            {
                var organization = await _dbContext.Organizations
                    .FirstOrDefaultAsync(o => o.OrganizationId == dbUser.OrganizationLookupId.Value);
                    
                _logger.LogInformation("🏢 User {Email} organization check: OrgId={OrgId}, OrgActive={OrgActive}", 
                    azureUser.Email, dbUser.OrganizationLookupId.Value, organization?.IsActive);
                    
                if (organization != null && !organization.IsActive)
                {
                    _logger.LogWarning("❌ User {Email} organization {OrgName} is INACTIVE - Status: Revoked", 
                        azureUser.Email, organization.Name);
                    return SystemUserStatus.Revoked; // Organization was revoked
                }
            }
            
            // CRITICAL: Simple and predictable database user record status check
            if (dbUser != null)
            {
                _logger.LogInformation("💾 User {Email} database record: IsActive={IsActive}, StatusCode={StatusCode}, Role={Role}", 
                    azureUser.Email, dbUser.IsActive, dbUser.StatusCode, dbUser.AssignedRole);
                
                // Simple rule: If database record shows user as inactive, they are revoked
                // This assumes proper user management processes set IsActive correctly
                if (!dbUser.IsActive || dbUser.StatusCode == StatusCode.Inactive)
                {
                    _logger.LogWarning("❌ User {Email} database record shows INACTIVE - Status: Revoked", azureUser.Email);
                    return SystemUserStatus.Revoked;
                }
                
                // If database record shows active, and user is enabled in Entra ID, continue with active logic
                _logger.LogInformation("✅ User {Email} database record shows ACTIVE - continuing status determination", azureUser.Email);
            }
            
            // For Guest users, check invitation status
            if (azureUser.UserType == "Guest")
            {
                var invitationStatus = azureUser.InvitationStatus?.ToLower();
                _logger.LogInformation("👤 Guest user {Email} invitation status: {InvitationStatus}", azureUser.Email, invitationStatus);
                
                if (invitationStatus == "accepted" || invitationStatus == "active")
                {
                    // Check if they have Console access (database record or system role)
                    if (dbUser != null || HasSystemRole(azureUser.Email))
                    {
                        _logger.LogInformation("✅ Guest user {Email} has Console access - Status: Active", azureUser.Email);
                        return SystemUserStatus.Active;
                    }
                    _logger.LogWarning("❌ Guest user {Email} accepted invitation but no Console access - Status: Disabled", azureUser.Email);
                    return SystemUserStatus.Disabled; // Accepted invitation but no Console access
                }
                else if (invitationStatus == "pendingacceptance")
                {
                    // Check if invitation is expired (typically 30 days)
                    if (DateTime.UtcNow > azureUser.CreatedOn.AddDays(30))
                    {
                        _logger.LogWarning("⏰ Guest user {Email} invitation EXPIRED - Status: InvitationExpired", azureUser.Email);
                        return SystemUserStatus.InvitationExpired;
                    }
                    _logger.LogInformation("⏳ Guest user {Email} invitation pending - Status: PendingInvitation", azureUser.Email);
                    return SystemUserStatus.PendingInvitation;
                }
                else
                {
                    _logger.LogWarning("❌ Guest user {Email} invitation status unknown or failed - Status: Disabled", azureUser.Email);
                    return SystemUserStatus.Disabled;
                }
            }
            
            // For Member (tenant) users - CRITICAL: Must check Entra ID status first
            if (!azureUser.IsEnabled)
            {
                _logger.LogWarning("❌ Member user {Email} is DISABLED in Entra ID - Status: Disabled", azureUser.Email);
                return SystemUserStatus.Disabled;
            }
            
            // Only show active status if they have Console App access
            var hasSystemRole = HasSystemRole(azureUser.Email);
            var isPartOfActiveOrg = await IsPartOfActiveOrganizationAsync(azureUser.Email);
            
            _logger.LogInformation("🔍 Member user {Email} access check: HasDbRecord={HasDbRecord}, HasSystemRole={HasSystemRole}, IsPartOfActiveOrg={IsPartOfActiveOrg}", 
                azureUser.Email, dbUser != null, hasSystemRole, isPartOfActiveOrg);
            
            if (dbUser != null || hasSystemRole || isPartOfActiveOrg)
            {
                _logger.LogInformation("✅ Member user {Email} has Console access - Status: Active", azureUser.Email);
                
                return SystemUserStatus.Active;
            }
            
            // User exists in tenant but has no Console App access
            _logger.LogWarning("❌ Member user {Email} exists in tenant but no Console App access - Status: Disabled", azureUser.Email);
            
            return SystemUserStatus.Disabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 ERROR determining Console App user status for {Email}: {ErrorMessage}", azureUser.Email, ex.Message);
            return SystemUserStatus.Unknown;
        }
    }

    private InvitationStatus? ParseInvitationStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
            return null;

        return status.ToLower() switch
        {
            "pendingacceptance" => InvitationStatus.PendingAcceptance,
            "accepted" => InvitationStatus.Accepted,
            "failed" => InvitationStatus.Failed,
            "expired" => InvitationStatus.Expired,
            "notinvited" => InvitationStatus.NotInvited,
            _ => InvitationStatus.Unknown
        };
    }

    private string GetOrganizationDisplayName(Organization? organization, OnboardedUser? dbUser)
    {
        if (organization != null)
        {
            return organization.Name;
        }
        
        if (dbUser?.GetUserRole() == UserRole.Developer || dbUser?.GetUserRole() == UserRole.SuperAdmin)
        {
            return "System Administrator";
        }
        
        return dbUser != null ? "Unknown Organization" : "No Database Access";
    }

    private string GetAppRoleName(UserRole role) => role switch
    {
        UserRole.SuperAdmin => "SuperAdmin",
        UserRole.Developer => "DevRole", // Fixed: Match Azure portal app role name
        UserRole.OrgAdmin => "OrgAdmin",
        UserRole.User => "OrgUser", // Correct: Already matches Azure portal
        _ => ""
    };

    private void PopulateAvailableActions(SystemUserDetails details)
    {
        details.AvailableActions.Clear();
        
        if (details.CanPromoteToSuperAdmin)
        {
            details.AvailableActions.Add("Promote to SuperAdmin");
        }
        
        if (details.CanPromoteToDeveloper)
        {
            details.AvailableActions.Add("Promote to Developer");
        }
        
        if (details.CanDemote)
        {
            details.AvailableActions.Add("Demote User");
        }
        
        if (!details.IsEnabled)
        {
            details.AvailableActions.Add("Reactivate Account");
        }
        else if (details.HasDatabaseRecord)
        {
            details.AvailableActions.Add("Deactivate Account");
        }
    }
    
    /// <summary>
    /// Filter tenant users to only show those who are part of organizations in the app
    /// </summary>
    private async Task<List<GuestUser>> FilterTenantUsersForOrganizations(List<GuestUser> tenantUsers)
    {
        try
        {
            // Get all active organizations from database
            var activeOrganizations = await _dbContext.Organizations
                .Where(o => o.IsActive)
                .Select(o => o.Name.ToLower())
                .ToListAsync();
                
            // Get all database users to include system users
            var dbUsers = await _dbContext.OnboardedUsers
                .Where(u => u.IsActive)
                .Select(u => u.Email.ToLower())
                .ToListAsync();
            
            var filteredUsers = new List<GuestUser>();
            
            foreach (var user in tenantUsers)
            {
                var userEmail = user.Email.ToLower();
                var userDomain = userEmail.Contains('@') ? userEmail.Split('@')[1] : "";
                
                // Include if user has database record (system user or onboarded user)
                if (dbUsers.Contains(userEmail))
                {
                    filteredUsers.Add(user);
                    continue;
                }
                
                // Include if user is from erpure.ai domain (system domain)
                if (userDomain == "erpure.ai")
                {
                    filteredUsers.Add(user);
                    continue;
                }
                
                // Include if user's domain matches an active organization
                if (activeOrganizations.Any(org => userDomain.Contains(org.Replace(" ", "").ToLower()) || 
                                                   org.Replace(" ", "").ToLower().Contains(userDomain)))
                {
                    filteredUsers.Add(user);
                }
            }
            
            return filteredUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering tenant users for organizations");
            return tenantUsers; // Return all if filtering fails
        }
    }
    
    /// <summary>
    /// Check if user has a system role (Developer/SuperAdmin)
    /// </summary>
    private bool HasSystemRole(string email)
    {
        try
        {
            var isErpureUser = email.ToLower().EndsWith("@erpure.ai");
            // Debug: Check what's in database for this user
            var dbUser = _dbContext.OnboardedUsers.FirstOrDefault(u => u.Email.ToLower() == email.ToLower());
            _logger.LogInformation("🔍 HasSystemRole DEBUG for {Email}: DbUser exists={DbUserExists}, DbUser.IsActive={DbIsActive}, DbUser.AssignedRole={DbRole}", 
                email, dbUser != null, dbUser?.IsActive, dbUser?.AssignedRole);
            
            var hasSystemDbRole = _dbContext.OnboardedUsers.Any(u => 
                u.Email.ToLower() == email.ToLower() && 
                u.IsActive &&
                (u.AssignedRole == UserRole.Developer || u.AssignedRole == UserRole.SuperAdmin));
                
            _logger.LogInformation("🔍 HasSystemRole for {Email}: IsErpureUser={IsErpureUser}, HasSystemDbRole={HasSystemDbRole}", 
                email, isErpureUser, hasSystemDbRole);
                
            return isErpureUser || hasSystemDbRole;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 ERROR checking system role for {Email}: {ErrorMessage}", email, ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// Check if user is part of an active organization
    /// </summary>
    private async Task<bool> IsPartOfActiveOrganizationAsync(string email)
    {
        try
        {
            var user = await _dbContext.OnboardedUsers
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
                
            return user?.Organization?.IsActive == true;
        }
        catch
        {
            return false;
        }
    }
}