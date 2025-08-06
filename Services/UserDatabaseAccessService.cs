using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AdminConsole.Services;

/// <summary>
/// Service for managing user database access assignments using Entity Framework
/// </summary>
public class UserDatabaseAccessService : IUserDatabaseAccessService
{
    private readonly AdminConsoleDbContext _context;
    private readonly IDatabaseCredentialService _databaseCredentialService;
    private readonly IDataIsolationService _dataIsolationService;
    private readonly ILogger<UserDatabaseAccessService> _logger;
    private readonly IMemoryCache _cache;

    public UserDatabaseAccessService(
        AdminConsoleDbContext context,
        IDatabaseCredentialService databaseCredentialService,
        IDataIsolationService dataIsolationService,
        ILogger<UserDatabaseAccessService> logger,
        IMemoryCache cache)
    {
        _context = context;
        _databaseCredentialService = databaseCredentialService;
        _dataIsolationService = dataIsolationService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<bool> AssignDatabaseToUserAsync(Guid userId, Guid databaseCredentialId, Guid organizationId, string assignedBy)
    {
        try
        {
            // Validate that the database credential exists and belongs to the organization
            var dbCredential = await _databaseCredentialService.GetByIdAsync(databaseCredentialId, organizationId);
            if (dbCredential == null)
            {
                _logger.LogWarning("Database credential {DatabaseCredentialId} not found for organization {OrganizationId}", 
                    databaseCredentialId, organizationId);
                return false;
            }

            // Check if assignment already exists
            var existingAssignment = await _context.UserDatabaseAssignments
                .Where(a => a.UserId == userId && 
                           a.DatabaseCredentialId == databaseCredentialId && 
                           a.OrganizationId == organizationId &&
                           a.IsActive)
                .FirstOrDefaultAsync();

            if (existingAssignment != null)
            {
                _logger.LogInformation("Database assignment already exists for user {UserId} and database {DatabaseCredentialId}", 
                    userId, databaseCredentialId);
                return true;
            }

            // Create new assignment
            var assignment = new UserDatabaseAssignment
            {
                UserId = userId,
                DatabaseCredentialId = databaseCredentialId,
                OrganizationId = organizationId,
                AssignedBy = assignedBy,
                AssignedOn = DateTime.UtcNow,
                IsActive = true
            };

            _context.UserDatabaseAssignments.Add(assignment);
            await _context.SaveChangesAsync();
            
            // Invalidate cache
            InvalidateUserCache(userId, organizationId);
            
            _logger.LogInformation("Assigned database {DatabaseCredentialId} to user {UserId} in organization {OrganizationId}", 
                databaseCredentialId, userId, organizationId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning database {DatabaseCredentialId} to user {UserId}", 
                databaseCredentialId, userId);
            return false;
        }
    }

    public async Task<bool> RemoveDatabaseFromUserAsync(Guid userId, Guid databaseCredentialId, Guid organizationId)
    {
        try
        {
            var assignment = await _context.UserDatabaseAssignments
                .Where(a => a.UserId == userId && 
                           a.DatabaseCredentialId == databaseCredentialId && 
                           a.OrganizationId == organizationId &&
                           a.IsActive)
                .FirstOrDefaultAsync();

            if (assignment != null)
            {
                assignment.IsActive = false;
                await _context.SaveChangesAsync();
                
                InvalidateUserCache(userId, organizationId);
                
                _logger.LogInformation("Removed database {DatabaseCredentialId} from user {UserId}", 
                    databaseCredentialId, userId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing database {DatabaseCredentialId} from user {UserId}", 
                databaseCredentialId, userId);
            return false;
        }
    }

    public async Task<List<DatabaseCredential>> GetUserAssignedDatabasesAsync(Guid userId, Guid organizationId)
    {
        try
        {
            var cacheKey = $"user_databases_{userId}_{organizationId}";
            
            if (_cache.TryGetValue(cacheKey, out List<DatabaseCredential>? cachedDatabases))
            {
                return cachedDatabases ?? new List<DatabaseCredential>();
            }

            var assignments = await _context.UserDatabaseAssignments
                .Where(a => a.UserId == userId && 
                           a.OrganizationId == organizationId && 
                           a.IsActive)
                .Include(a => a.DatabaseCredential)
                .Where(a => a.DatabaseCredential != null && a.DatabaseCredential.IsActive)
                .Select(a => a.DatabaseCredential!)
                .ToListAsync();

            // Cache for 5 minutes
            _cache.Set(cacheKey, assignments, TimeSpan.FromMinutes(5));

            return assignments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting assigned databases for user {UserId}", userId);
            return new List<DatabaseCredential>();
        }
    }

    public async Task<List<OnboardedUser>> GetDatabaseUsersAsync(Guid databaseCredentialId, Guid organizationId)
    {
        try
        {
            var assignments = await _context.UserDatabaseAssignments
                .Where(a => a.DatabaseCredentialId == databaseCredentialId && 
                           a.OrganizationId == organizationId && 
                           a.IsActive)
                .Include(a => a.User)
                .Where(a => a.User != null && a.User.StateCode == StateCode.Active)
                .Select(a => a.User!)
                .ToListAsync();

            return assignments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users for database {DatabaseCredentialId}", databaseCredentialId);
            return new List<OnboardedUser>();
        }
    }

    public async Task<bool> UpdateUserDatabaseAssignmentsAsync(Guid userId, List<Guid> databaseCredentialIds, Guid organizationId, string assignedBy)
    {
        try
        {
            // Deactivate all current assignments for this user
            var currentAssignments = await _context.UserDatabaseAssignments
                .Where(a => a.UserId == userId && 
                           a.OrganizationId == organizationId && 
                           a.IsActive)
                .ToListAsync();

            foreach (var assignment in currentAssignments)
            {
                assignment.IsActive = false;
            }

            // Create new assignments
            foreach (var dbId in databaseCredentialIds)
            {
                var newAssignment = new UserDatabaseAssignment
                {
                    UserId = userId,
                    DatabaseCredentialId = dbId,
                    OrganizationId = organizationId,
                    AssignedBy = assignedBy,
                    AssignedOn = DateTime.UtcNow,
                    IsActive = true
                };

                _context.UserDatabaseAssignments.Add(newAssignment);
            }

            await _context.SaveChangesAsync();

            // Invalidate cache
            InvalidateUserCache(userId, organizationId);

            _logger.LogInformation("Updated database assignments for user {UserId} - assigned {Count} databases", 
                userId, databaseCredentialIds.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating database assignments for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UserHasDatabaseAccessAsync(Guid userId, Guid databaseCredentialId, Guid organizationId)
    {
        try
        {
            return await _context.UserDatabaseAssignments
                .AnyAsync(a => a.UserId == userId && 
                              a.DatabaseCredentialId == databaseCredentialId && 
                              a.OrganizationId == organizationId &&
                              a.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking database access for user {UserId} and database {DatabaseCredentialId}", 
                userId, databaseCredentialId);
            return false;
        }
    }

    public async Task<List<UserDatabaseAssignment>> GetOrganizationDatabaseAssignmentsAsync(Guid organizationId)
    {
        try
        {
            return await _context.UserDatabaseAssignments
                .Where(a => a.OrganizationId == organizationId && a.IsActive)
                .Include(a => a.User)
                .Include(a => a.DatabaseCredential)
                .OrderByDescending(a => a.AssignedOn)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database assignments for organization {OrganizationId}", organizationId);
            return new List<UserDatabaseAssignment>();
        }
    }

    private void InvalidateUserCache(Guid userId, Guid organizationId)
    {
        var cacheKey = $"user_databases_{userId}_{organizationId}";
        _cache.Remove(cacheKey);
    }
}