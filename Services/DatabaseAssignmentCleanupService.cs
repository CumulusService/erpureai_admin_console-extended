using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Services;

public interface IDatabaseAssignmentCleanupService
{
    Task<List<DatabaseCredential>> GetValidAssignedDatabasesAsync(List<Guid> assignedDatabaseIds, Guid organizationId);
    Task CleanupUserDatabaseAssignmentsAsync(Guid organizationId);
    Task<bool> ValidateAndCleanupUserAssignmentsAsync(Guid userId, Guid organizationId);
}

public class DatabaseAssignmentCleanupService : IDatabaseAssignmentCleanupService
{
    private readonly AdminConsoleDbContext _context;
    private readonly ILogger<DatabaseAssignmentCleanupService> _logger;

    public DatabaseAssignmentCleanupService(AdminConsoleDbContext context, ILogger<DatabaseAssignmentCleanupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Gets only valid assigned databases (filters out stale IDs)
    /// </summary>
    public async Task<List<DatabaseCredential>> GetValidAssignedDatabasesAsync(List<Guid> assignedDatabaseIds, Guid organizationId)
    {
        if (!assignedDatabaseIds.Any())
            return new List<DatabaseCredential>();

        var validDatabases = await _context.DatabaseCredentials
            .Where(d => d.OrganizationId == organizationId && 
                       d.IsActive && 
                       assignedDatabaseIds.Contains(d.Id))
            .ToListAsync();

        return validDatabases;
    }

    /// <summary>
    /// Cleans up all user database assignments in an organization
    /// </summary>
    public async Task CleanupUserDatabaseAssignmentsAsync(Guid organizationId)
    {
        _logger.LogInformation("🧹 Starting database assignment cleanup for organization {OrganizationId}", organizationId);

        // Get valid database IDs
        var validDatabaseIds = await _context.DatabaseCredentials
            .Where(d => d.OrganizationId == organizationId && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync();

        _logger.LogInformation("🧹 Found {ValidCount} valid databases for organization", validDatabaseIds.Count);

        // Get users with database assignments
        var usersToUpdate = await _context.OnboardedUsers
            .Where(u => u.OrganizationLookupId == organizationId && u.AssignedDatabaseIds.Any())
            .ToListAsync();

        int updatedCount = 0;
        int totalCleaned = 0;

        foreach (var user in usersToUpdate)
        {
            var originalCount = user.AssignedDatabaseIds.Count;
            var validAssignments = user.AssignedDatabaseIds.Where(id => validDatabaseIds.Contains(id)).ToList();
            var removedCount = originalCount - validAssignments.Count;

            if (removedCount > 0)
            {
                user.AssignedDatabaseIds = validAssignments;
                updatedCount++;
                totalCleaned += removedCount;

                _logger.LogInformation("🧹 Cleaned user {Email}: {OriginalCount} → {ValidCount} assignments (removed {RemovedCount} stale)", 
                    user.Email, originalCount, validAssignments.Count, removedCount);
            }
        }

        if (updatedCount > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("🧹 ✅ Cleanup complete: Updated {UpdatedUsers} users, removed {TotalCleaned} stale assignments", 
                updatedCount, totalCleaned);
        }
        else
        {
            _logger.LogInformation("🧹 ✅ No cleanup needed - all assignments are valid");
        }
    }

    /// <summary>
    /// Validates and cleans up a specific user's database assignments
    /// </summary>
    public async Task<bool> ValidateAndCleanupUserAssignmentsAsync(Guid userId, Guid organizationId)
    {
        var user = await _context.OnboardedUsers
            .FirstOrDefaultAsync(u => u.OnboardedUserId == userId && u.OrganizationLookupId == organizationId);

        if (user == null || !user.AssignedDatabaseIds.Any())
            return false;

        var validDatabaseIds = await _context.DatabaseCredentials
            .Where(d => d.OrganizationId == organizationId && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync();

        var originalCount = user.AssignedDatabaseIds.Count;
        var validAssignments = user.AssignedDatabaseIds.Where(id => validDatabaseIds.Contains(id)).ToList();
        var removedCount = originalCount - validAssignments.Count;

        if (removedCount > 0)
        {
            user.AssignedDatabaseIds = validAssignments;
            await _context.SaveChangesAsync();

            _logger.LogInformation("🧹 Cleaned user {UserId}: {OriginalCount} → {ValidCount} assignments", 
                userId, originalCount, validAssignments.Count);
            return true;
        }

        return false;
    }
}