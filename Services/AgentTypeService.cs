using AdminConsole.Data;
using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AdminConsole.Services;

/// <summary>
/// Implementation of agent type service using Entity Framework
/// Provides CRUD operations and business logic for AgentTypeEntity management
/// </summary>
public class AgentTypeService : IAgentTypeService
{
    private readonly AdminConsoleDbContext _context;
    private readonly ILogger<AgentTypeService> _logger;
    private readonly IGraphService _graphService;
    private readonly IOrganizationService _organizationService;
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _serviceProvider;

    private static readonly SemaphoreSlim _tableInitSemaphore = new(1, 1);
    private static bool _tablesInitialized = false;

    public AgentTypeService(
        AdminConsoleDbContext context,
        ILogger<AgentTypeService> logger,
        IGraphService graphService,
        IOrganizationService organizationService,
        IMemoryCache cache,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _logger = logger;
        _graphService = graphService;
        _organizationService = organizationService;
        _cache = cache;
        _serviceProvider = serviceProvider;
    }

    private async Task EnsureTablesExistAsync()
    {
        if (_tablesInitialized) return;
        
        await _tableInitSemaphore.WaitAsync();
        try
        {
            if (_tablesInitialized) return;
            // Check if AgentTypes table exists, create if not
            var tableExists = await _context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AgentTypes')
                BEGIN
                    CREATE TABLE AgentTypes (
                        Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
                        Name nvarchar(100) NOT NULL,
                        DisplayName nvarchar(200) NOT NULL,
                        AgentShareUrl nvarchar(500) NULL,
                        GlobalSecurityGroupId nvarchar(100) NULL,
                        TeamsAppId nvarchar(100) NULL,
                        Description nvarchar(500) NULL,
                        IsActive bit NOT NULL DEFAULT 1,
                        DisplayOrder int NOT NULL DEFAULT 0,
                        CreatedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        ModifiedDate datetime2 NOT NULL DEFAULT GETUTCDATE()
                    );
                    
                    CREATE UNIQUE INDEX IX_AgentTypes_Name ON AgentTypes (Name);
                    
                    -- Insert initial agent types
                    INSERT INTO AgentTypes (Name, DisplayName, Description, IsActive, DisplayOrder, CreatedDate, ModifiedDate)
                    VALUES 
                    ('SBOAgentAppv1', 'SBO Agent App v1', 'SAP Business One Agent Application Version 1', 1, 1, GETUTCDATE(), GETUTCDATE()),
                    ('Sales', 'Sales Agent', 'Sales-focused AI agent for customer interaction', 1, 2, GETUTCDATE(), GETUTCDATE()),
                    ('Admin', 'Admin Agent', 'Administrative AI agent for system management', 1, 3, GETUTCDATE(), GETUTCDATE());
                END
                
                -- Add AgentTypeIds column to OnboardedUsers if it doesn't exist
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'AgentTypeIds')
                BEGIN
                    ALTER TABLE OnboardedUsers ADD AgentTypeIds nvarchar(max) NOT NULL DEFAULT '[]';
                END
                
                -- Create OrganizationTeamsGroups table if it doesn't exist
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'OrganizationTeamsGroups')
                BEGIN
                    CREATE TABLE OrganizationTeamsGroups (
                        Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
                        OrganizationId uniqueidentifier NOT NULL,
                        AgentTypeId uniqueidentifier NOT NULL,
                        TeamsGroupId nvarchar(100) NOT NULL,
                        TeamName nvarchar(255) NOT NULL,
                        TeamUrl nvarchar(500) NULL,
                        Description nvarchar(500) NULL,
                        IsActive bit NOT NULL DEFAULT 1,
                        CreatedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        CreatedBy nvarchar(100) NULL,
                        ModifiedDate datetime2 NOT NULL DEFAULT GETUTCDATE()
                    );
                    
                    CREATE INDEX IX_OrganizationTeamsGroups_OrganizationId ON OrganizationTeamsGroups (OrganizationId);
                    CREATE INDEX IX_OrganizationTeamsGroups_AgentTypeId ON OrganizationTeamsGroups (AgentTypeId);
                    CREATE INDEX IX_OrganizationTeamsGroups_TeamsGroupId ON OrganizationTeamsGroups (TeamsGroupId);
                    CREATE UNIQUE INDEX IX_OrganizationTeamsGroups_OrganizationId_AgentTypeId ON OrganizationTeamsGroups (OrganizationId, AgentTypeId);
                END
                
                -- Create UserAgentTypeGroupAssignments table if it doesn't exist (new feature - additive)
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserAgentTypeGroupAssignments')
                BEGIN
                    CREATE TABLE UserAgentTypeGroupAssignments (
                        Id uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
                        UserId nvarchar(100) NOT NULL,
                        AgentTypeId uniqueidentifier NOT NULL,
                        SecurityGroupId nvarchar(100) NOT NULL,
                        OrganizationId uniqueidentifier NOT NULL,
                        AssignedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        AssignedBy nvarchar(100) NULL,
                        IsActive bit NOT NULL DEFAULT 1,
                        CreatedDate datetime2 NOT NULL DEFAULT GETUTCDATE(),
                        ModifiedDate datetime2 NOT NULL DEFAULT GETUTCDATE()
                    );
                    
                    CREATE INDEX IX_UserAgentTypeGroupAssignments_UserId ON UserAgentTypeGroupAssignments (UserId);
                    CREATE INDEX IX_UserAgentTypeGroupAssignments_AgentTypeId ON UserAgentTypeGroupAssignments (AgentTypeId);
                    CREATE INDEX IX_UserAgentTypeGroupAssignments_OrganizationId ON UserAgentTypeGroupAssignments (OrganizationId);
                    CREATE INDEX IX_UserAgentTypeGroupAssignments_IsActive ON UserAgentTypeGroupAssignments (IsActive);
                END
                
                -- Add new columns to OnboardedUsers table (additive enhancement)
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'IsDeleted')
                BEGIN
                    ALTER TABLE OnboardedUsers ADD IsDeleted bit NOT NULL DEFAULT 0;
                END
                
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'RedirectUri')
                BEGIN
                    ALTER TABLE OnboardedUsers ADD RedirectUri nvarchar(500) NULL;
                END
                
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OnboardedUsers' AND COLUMN_NAME = 'LastInvitationDate')
                BEGIN
                    ALTER TABLE OnboardedUsers ADD LastInvitationDate datetime2 NULL;
                END
                
                -- Add TeamsAppId column to AgentTypes table if it doesn't exist (new Teams App installation feature)
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AgentTypes' AND COLUMN_NAME = 'TeamsAppId')
                BEGIN
                    ALTER TABLE AgentTypes ADD TeamsAppId nvarchar(100) NULL;
                END");
                
            _logger.LogInformation("Database tables ensured successfully (including new agent group assignment features)");
            _tablesInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure database tables exist - tables may already exist or insufficient permissions");
        }
        finally
        {
            _tableInitSemaphore.Release();
        }
    }

    /// <summary>
    /// Invalidates agent types cache to ensure fresh data after CRUD operations
    /// </summary>
    private void InvalidateAgentTypesCache()
    {
        // Remove the main cache key used by NavigationOptimizationService
        // Using the same key as defined in NavigationOptimizationService.AGENT_TYPES_CACHE_KEY
        _cache.Remove("nav_agent_types");
        _logger.LogDebug("🗑️ Invalidated agent types cache");
    }

    public async Task<List<AgentTypeEntity>> GetActiveAgentTypesAsync()
    {
        try
        {
            await EnsureTablesExistAsync();
            return await _context.AgentTypes
                .Where(at => at.IsActive)
                .OrderBy(at => at.DisplayOrder)
                .ThenBy(at => at.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active agent types");
            return new List<AgentTypeEntity>();
        }
    }

    public async Task<List<AgentTypeEntity>> GetAllAgentTypesAsync()
    {
        try
        {
            await EnsureTablesExistAsync();
            return await _context.AgentTypes
                .OrderBy(at => at.DisplayOrder)
                .ThenBy(at => at.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all agent types");
            return new List<AgentTypeEntity>();
        }
    }

    public async Task<List<AgentTypeEntity>> GetAgentTypesByIdsAsync(List<Guid> agentTypeIds)
    {
        try
        {
            await EnsureTablesExistAsync();
            if (!agentTypeIds.Any())
                return new List<AgentTypeEntity>();

            return await _context.AgentTypes
                .Where(at => agentTypeIds.Contains(at.Id) && at.IsActive)
                .OrderBy(at => at.DisplayOrder)
                .ThenBy(at => at.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent types by IDs: {AgentTypeIds}", string.Join(", ", agentTypeIds));
            return new List<AgentTypeEntity>();
        }
    }

    public async Task<AgentTypeEntity?> GetByIdAsync(Guid id)
    {
        try
        {
            await EnsureTablesExistAsync();
            return await _context.AgentTypes
                .FirstOrDefaultAsync(at => at.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent type by ID: {AgentTypeId}", id);
            return null;
        }
    }

    public async Task<AgentTypeEntity> CreateAsync(AgentTypeEntity agentType)
    {
        try
        {
            agentType.Id = Guid.NewGuid();
            agentType.CreatedDate = DateTime.UtcNow;
            agentType.ModifiedDate = DateTime.UtcNow;

            _context.AgentTypes.Add(agentType);
            await _context.SaveChangesAsync();

            // Invalidate cache so UI shows fresh data
            InvalidateAgentTypesCache();

            _logger.LogInformation("✅ Created agent type {Name} with ID {Id} and invalidated cache", agentType.Name, agentType.Id);
            return agentType;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent type {Name}", agentType.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(AgentTypeEntity agentType)
    {
        try
        {
            var existingAgentType = await _context.AgentTypes
                .FirstOrDefaultAsync(at => at.Id == agentType.Id);

            if (existingAgentType == null)
            {
                _logger.LogWarning("Agent type {Id} not found for update", agentType.Id);
                return false;
            }

            // 🎯 DETECT TEAMS APP ID CHANGES before updating
            var oldTeamsAppId = existingAgentType.TeamsAppId;
            var newTeamsAppId = agentType.TeamsAppId;
            var teamsAppIdChanged = !string.Equals(oldTeamsAppId, newTeamsAppId, StringComparison.OrdinalIgnoreCase);

            // 🔐 DETECT AGENT TYPE DEACTIVATION before updating
            var oldIsActive = existingAgentType.IsActive;
            var newIsActive = agentType.IsActive;
            var isBeingDeactivated = oldIsActive && !newIsActive;

            _logger.LogWarning("🔄 *** AGENT TYPE UPDATE *** {Name} (ID: {Id}) - Teams App ID Changed: {TeamsAppChanged} " +
                                 "(Old: '{OldAppId}', New: '{NewAppId}') - *** DEACTIVATION: {IsBeingDeactivated} *** " +
                                 "(Active: {OldActive} → {NewActive})", 
                agentType.Name, agentType.Id, teamsAppIdChanged, 
                oldTeamsAppId ?? "null", newTeamsAppId ?? "null", isBeingDeactivated,
                oldIsActive, newIsActive);

            // Update properties
            existingAgentType.Name = agentType.Name;
            existingAgentType.DisplayName = agentType.DisplayName;
            existingAgentType.Description = agentType.Description;
            existingAgentType.AgentShareUrl = agentType.AgentShareUrl;
            existingAgentType.GlobalSecurityGroupId = agentType.GlobalSecurityGroupId;
            existingAgentType.TeamsAppId = agentType.TeamsAppId;
            existingAgentType.IsActive = agentType.IsActive;
            existingAgentType.RequireSupervisorEmail = agentType.RequireSupervisorEmail;
            existingAgentType.DisplayOrder = agentType.DisplayOrder;
            existingAgentType.ModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Invalidate cache so UI shows fresh data
            InvalidateAgentTypesCache();

            _logger.LogInformation("✅ Updated agent type {Name} with ID {Id} and invalidated cache", agentType.Name, agentType.Id);

            // 🚀 TRIGGER TEAMS APP INSTALL/UNINSTALL if TeamsAppId changed
            if (teamsAppIdChanged)
            {
                _logger.LogInformation("🎯 Teams App ID changed for agent type {Name} - processing affected organizations", 
                    agentType.Name);
                
                await ProcessTeamsAppUpdateForOrganizationsAsync(agentType.Id, oldTeamsAppId, newTeamsAppId);
            }

            // 🔐 REMOVE ALL USERS FROM SECURITY GROUPS if agent type is being deactivated
            if (isBeingDeactivated)
            {
                _logger.LogError("🔐 *** CRITICAL SECURITY ACTION *** AGENT TYPE DEACTIVATION: Agent type '{Name}' is being deactivated - removing ALL users from associated security groups across ALL organizations", 
                    agentType.Name);

                try
                {
                    // Resolve IAgentGroupAssignmentService at runtime to avoid circular dependency
                    var agentGroupAssignmentService = _serviceProvider.GetRequiredService<IAgentGroupAssignmentService>();
                    var removalSuccess = await agentGroupAssignmentService.RemoveAllUsersFromAgentTypeGloballyAsync(agentType.Id, "SYSTEM_AGENT_DEACTIVATION");

                    if (removalSuccess)
                    {
                        _logger.LogInformation("✅ SECURITY COMPLIANCE: Successfully removed all users from security groups for deactivated agent type '{Name}'", 
                            agentType.Name);
                    }
                    else
                    {
                        _logger.LogError("❌ SECURITY RISK: Failed to remove some/all users from security groups for deactivated agent type '{Name}' - manual cleanup may be required", 
                            agentType.Name);
                    }
                }
                catch (Exception removalEx)
                {
                    _logger.LogError(removalEx, "💥 CRITICAL SECURITY ERROR: Exception occurred while removing users from security groups for deactivated agent type '{Name}' - manual cleanup required", 
                        agentType.Name);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent type {Id}", agentType.Id);
            return false;
        }
    }

    /// <summary>
    /// Processes Teams app installation/uninstallation for organizations that have access to the specified agent type
    /// </summary>
    /// <param name="agentTypeId">The agent type ID that was updated</param>
    /// <param name="oldTeamsAppId">The previous Teams App ID (null if none)</param>
    /// <param name="newTeamsAppId">The new Teams App ID (null if removed)</param>
    private async Task ProcessTeamsAppUpdateForOrganizationsAsync(Guid agentTypeId, string? oldTeamsAppId, string? newTeamsAppId)
    {
        try
        {
            _logger.LogInformation("🔍 Finding organizations that have access to agent type {AgentTypeId}", agentTypeId);

            // Find all organizations that have this agent type in their OrganizationAgentTypeIds
            var allOrganizations = await _context.Organizations
                .AsNoTracking() // Force fresh data from database, bypass EF caching
                .Where(o => o.StateCode == StateCode.Active)
                .ToListAsync();

            var affectedOrganizations = allOrganizations
                .Where(org => org.GetOrganizationAgentTypeIds().Contains(agentTypeId))
                .ToList();

            _logger.LogInformation("📊 Found {Count} organizations with access to agent type {AgentTypeId}: {OrgNames}", 
                affectedOrganizations.Count, agentTypeId, 
                string.Join(", ", affectedOrganizations.Select(o => o.Name)));

            foreach (var organization in affectedOrganizations)
            {
                try
                {
                    // Skip organizations without Teams groups
                    if (string.IsNullOrEmpty(organization.M365GroupId))
                    {
                        _logger.LogWarning("⚠️ Organization {OrgName} (ID: {OrgId}) has no M365GroupId - skipping Teams app operations", 
                            organization.Name, organization.OrganizationId);
                        continue;
                    }

                    _logger.LogInformation("🎯 Processing Teams app update for organization {OrgName} (M365GroupId: {GroupId})", 
                        organization.Name, organization.M365GroupId);

                    // Step 1: Uninstall old Teams app if it exists
                    if (!string.IsNullOrEmpty(oldTeamsAppId))
                    {
                        _logger.LogInformation("🗑️ Uninstalling old Teams app {OldAppId} from organization {OrgName}", 
                            oldTeamsAppId, organization.Name);
                        
                        var uninstallSuccess = await _graphService.UninstallTeamsAppAsync(organization.M365GroupId, oldTeamsAppId);
                        
                        if (uninstallSuccess)
                        {
                            _logger.LogInformation("✅ Successfully uninstalled old Teams app {OldAppId} from organization {OrgName}", 
                                oldTeamsAppId, organization.Name);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to uninstall old Teams app {OldAppId} from organization {OrgName} - continuing with new installation", 
                                oldTeamsAppId, organization.Name);
                        }
                    }

                    // Step 2: Install new Teams app if it exists
                    if (!string.IsNullOrEmpty(newTeamsAppId))
                    {
                        _logger.LogInformation("📱 Installing new Teams app {NewAppId} to organization {OrgName}", 
                            newTeamsAppId, organization.Name);
                        
                        var installSuccess = await _graphService.InstallTeamsAppAsync(organization.M365GroupId, newTeamsAppId);
                        
                        if (installSuccess)
                        {
                            _logger.LogInformation("✅ Successfully installed new Teams app {NewAppId} to organization {OrgName}", 
                                newTeamsAppId, organization.Name);
                            
                            // 🔐 AUTOMATICALLY CONFIGURE TEAMS APP PERMISSION POLICIES
                            // This eliminates the need for users to request IT admin approval
                            _logger.LogInformation("🔐 Configuring Teams App permission policies for organization {OrgName} to eliminate approval requests", 
                                organization.Name);
                            
                            try
                            {
                                var policyConfigured = await _graphService.ConfigureTeamsAppPermissionPoliciesAsync(
                                    organization.M365GroupId, 
                                    organization.Name, 
                                    new List<string> { newTeamsAppId });
                                
                                if (policyConfigured)
                                {
                                    _logger.LogInformation("✅ Successfully configured Teams App permission policies for organization {OrgName}", 
                                        organization.Name);
                                    
                                    // 🌐 ADDITIONALLY ATTEMPT TENANT-LEVEL APPROVAL CONFIGURATION
                                    _logger.LogInformation("🌐 Attempting tenant-level app approval configuration to eliminate user approval requests...");
                                    var tenantApprovalConfigured = await _graphService.ConfigureTenantLevelTeamsAppApprovalAsync(newTeamsAppId);
                                    
                                    if (tenantApprovalConfigured)
                                    {
                                        _logger.LogInformation("✅ Tenant-level app approval configured - users should not see approval requests");
                                    }
                                    else
                                    {
                                        _logger.LogWarning("⚠️ Tenant-level approval could not be configured automatically - manual Teams Admin Center configuration may be needed");
                                        _logger.LogInformation("💡 Manual fix: Teams Admin Center > Teams apps > Permission policies > Allow app {AppId}", newTeamsAppId);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Teams App permission policies could not be configured for organization {OrgName} - users may need to request approval", 
                                        organization.Name);
                                }
                            }
                            catch (Exception policyEx)
                            {
                                _logger.LogError(policyEx, "❌ Error configuring Teams App permission policies for organization {OrgName} - continuing with installation", 
                                    organization.Name);
                            }
                        }
                        else
                        {
                            _logger.LogError("❌ Failed to install new Teams app {NewAppId} to organization {OrgName}", 
                                newTeamsAppId, organization.Name);
                        }
                    }
                }
                catch (Exception orgEx)
                {
                    _logger.LogError(orgEx, "❌ Error processing Teams app update for organization {OrgName} (ID: {OrgId})", 
                        organization.Name, organization.OrganizationId);
                }
            }

            _logger.LogInformation("🏁 Completed Teams app update processing for agent type {AgentTypeId} across {Count} organizations", 
                agentTypeId, affectedOrganizations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Critical error during Teams app update processing for agent type {AgentTypeId}", agentTypeId);
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var agentType = await _context.AgentTypes
                .FirstOrDefaultAsync(at => at.Id == id);

            if (agentType == null)
            {
                _logger.LogWarning("Agent type {Id} not found for deletion", id);
                return false;
            }

            // Soft delete
            agentType.IsActive = false;
            agentType.ModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Invalidate cache so UI shows fresh data
            InvalidateAgentTypesCache();

            _logger.LogInformation("✅ Soft deleted agent type {Name} with ID {Id} and invalidated cache", agentType.Name, id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent type {Id}", id);
            return false;
        }
    }

    public async Task<bool> ReorderAsync(List<Guid> orderedIds)
    {
        try
        {
            var agentTypes = await _context.AgentTypes
                .Where(at => orderedIds.Contains(at.Id))
                .ToListAsync();

            for (int i = 0; i < orderedIds.Count; i++)
            {
                var agentType = agentTypes.FirstOrDefault(at => at.Id == orderedIds[i]);
                if (agentType != null)
                {
                    agentType.DisplayOrder = i;
                    agentType.ModifiedDate = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            // Invalidate cache so UI shows fresh data
            InvalidateAgentTypesCache();

            _logger.LogInformation("✅ Reordered {Count} agent types and invalidated cache", orderedIds.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering agent types");
            return false;
        }
    }

    public async Task<List<AgentTypeEntity>> GetAgentTypesWithSecurityGroupsAsync()
    {
        try
        {
            await EnsureTablesExistAsync();
            return await _context.AgentTypes
                .Where(at => at.IsActive && !string.IsNullOrEmpty(at.GlobalSecurityGroupId))
                .OrderBy(at => at.DisplayOrder)
                .ThenBy(at => at.Name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent types with security groups");
            return new List<AgentTypeEntity>();
        }
    }

    public async Task<bool> ValidateSecurityGroupAsync(string groupId)
    {
        try
        {
            if (string.IsNullOrEmpty(groupId))
                return false;

            // Use GraphService to check if the group exists
            var groupExists = await _graphService.GroupExistsAsync(groupId);
            
            _logger.LogInformation("Security group validation for {GroupId}: {Exists}", groupId, groupExists);
            return groupExists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating security group {GroupId}", groupId);
            return false;
        }
    }
}