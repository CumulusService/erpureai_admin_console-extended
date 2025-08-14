using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

/// <summary>
/// Service to handle SuperAdmin role migration and assignment
/// Removes hardcoded @erpure.ai domain checks by properly assigning roles in database
/// </summary>
public interface ISuperAdminMigrationService
{
    Task MigrateSuperAdminRolesAsync();
    Task AssignSuperAdminRoleAsync(string email);
    Task<bool> IsSuperAdminByDatabaseAsync(string email);
}

public class SuperAdminMigrationService : ISuperAdminMigrationService
{
    private readonly AdminConsoleDbContext _dbContext;
    private readonly IGraphService _graphService;
    private readonly ILogger<SuperAdminMigrationService> _logger;

    public SuperAdminMigrationService(
        AdminConsoleDbContext dbContext,
        IGraphService graphService,
        ILogger<SuperAdminMigrationService> logger)
    {
        _dbContext = dbContext;
        _graphService = graphService;
        _logger = logger;
    }

    /// <summary>
    /// Migrates existing @erpure.ai domain users to have explicit SuperAdmin role assignment
    /// This removes the need for hardcoded domain checks
    /// </summary>
    public async Task MigrateSuperAdminRolesAsync()
    {
        try
        {
            _logger.LogInformation("Starting SuperAdmin role migration...");

            // Find all existing OnboardedUsers with @erpure.ai domain but without explicit SuperAdmin role
            var erpureUsers = await _dbContext.OnboardedUsers
                .Where(u => u.Email.EndsWith("@erpure.ai") && u.AssignedRole != UserRole.SuperAdmin)
                .ToListAsync();

            _logger.LogInformation("Found {Count} @erpure.ai users to migrate", erpureUsers.Count);

            foreach (var user in erpureUsers)
            {
                _logger.LogInformation("Migrating user {Email} to SuperAdmin role", user.Email);
                user.AssignedRole = UserRole.SuperAdmin;
            }

            if (erpureUsers.Any())
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully migrated {Count} users to SuperAdmin role", erpureUsers.Count);
            }
            else
            {
                _logger.LogInformation("No users found to migrate");
            }

            // Also check for any @erpure.ai users in Azure AD that might not be in OnboardedUsers yet
            await CreateMissingSuperAdminRecordsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate SuperAdmin roles");
            throw;
        }
    }

    /// <summary>
    /// Assigns SuperAdmin role to a specific user by email
    /// </summary>
    public async Task AssignSuperAdminRoleAsync(string email)
    {
        try
        {
            _logger.LogInformation("Assigning SuperAdmin role to {Email}", email);

            var existingUser = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

            if (existingUser != null)
            {
                existingUser.AssignedRole = UserRole.SuperAdmin;
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Updated existing user {Email} to SuperAdmin role", email);
            }
            else
            {
                // Create new OnboardedUser record for SuperAdmin
                var newSuperAdmin = new OnboardedUser
                {
                    OnboardedUserId = Guid.NewGuid(),
                    Name = email.Split('@')[0],
                    Email = email,
                    AssignedRole = UserRole.SuperAdmin,
                    IsActive = true,
                    StateCode = StateCode.Active,
                    StatusCode = StatusCode.Active,
                    CreatedOn = DateTime.UtcNow,
                    ModifiedOn = DateTime.UtcNow,
                    OwnerId = Guid.NewGuid() // Temporary until we have proper ownership
                };

                _dbContext.OnboardedUsers.Add(newSuperAdmin);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Created new SuperAdmin user record for {Email}", email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign SuperAdmin role to {Email}", email);
            throw;
        }
    }

    /// <summary>
    /// Checks if a user has SuperAdmin role in the database (instead of hardcoded domain check)
    /// </summary>
    public async Task<bool> IsSuperAdminByDatabaseAsync(string email)
    {
        try
        {
            var user = await _dbContext.OnboardedUsers
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

            return user?.AssignedRole == UserRole.SuperAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check SuperAdmin status for {Email}", email);
            return false;
        }
    }

    /// <summary>
    /// Creates OnboardedUser records for any @erpure.ai users in Azure AD that don't have database records
    /// </summary>
    private async Task CreateMissingSuperAdminRecordsAsync()
    {
        try
        {
            _logger.LogInformation("Checking for missing @erpure.ai user records in database...");

            // Get all guest users from Azure AD
            var allGuestUsers = await _graphService.GetAllGuestUsersAsync();
            var erpureGuestUsers = allGuestUsers.Where(u => u.Email.EndsWith("@erpure.ai", StringComparison.OrdinalIgnoreCase)).ToList();

            _logger.LogInformation("Found {Count} @erpure.ai guest users in Azure AD", erpureGuestUsers.Count);

            foreach (var guestUser in erpureGuestUsers)
            {
                var existingRecord = await _dbContext.OnboardedUsers
                    .FirstOrDefaultAsync(u => u.Email == guestUser.Email);

                if (existingRecord == null)
                {
                    _logger.LogInformation("Creating OnboardedUser record for missing SuperAdmin: {Email}", guestUser.Email);
                    
                    var newSuperAdmin = new OnboardedUser
                    {
                        OnboardedUserId = Guid.NewGuid(),
                        Name = guestUser.Email.Split('@')[0],
                        Email = guestUser.Email,
                        FullName = guestUser.DisplayName ?? guestUser.Email,
                        AzureObjectId = guestUser.Id,
                        AssignedRole = UserRole.SuperAdmin,
                        IsActive = true,
                        StateCode = StateCode.Active,
                        StatusCode = StatusCode.Active,
                        CreatedOn = DateTime.UtcNow,
                        ModifiedOn = DateTime.UtcNow,
                        OwnerId = Guid.NewGuid() // Temporary until we have proper ownership
                    };

                    _dbContext.OnboardedUsers.Add(newSuperAdmin);
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Completed missing SuperAdmin record creation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create missing SuperAdmin records");
            // Don't rethrow - this is a nice-to-have operation
        }
    }
}