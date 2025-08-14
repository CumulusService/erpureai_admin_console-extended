using AdminConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminConsole.Data;

public class AdminConsoleDbContext : DbContext
{
    public AdminConsoleDbContext(DbContextOptions<AdminConsoleDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations { get; set; }
    public DbSet<OnboardedUser> OnboardedUsers { get; set; }
    public DbSet<DatabaseCredential> DatabaseCredentials { get; set; }
    public DbSet<UserDatabaseAssignment> UserDatabaseAssignments { get; set; }
    public DbSet<AgentTypeEntity> AgentTypes { get; set; }
    public DbSet<OrganizationTeamsGroup> OrganizationTeamsGroups { get; set; }
    
    // New table for agent-based group assignments (additive)
    public DbSet<UserAgentTypeGroupAssignment> UserAgentTypeGroupAssignments { get; set; }
    
    // User revocation tracking (additive for security)
    public DbSet<UserRevocationRecord> UserRevocationRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Organization configuration
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.OrganizationId);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.AdminEmail).IsRequired().HasMaxLength(255);
            entity.Property(e => e.KeyVaultUri).HasMaxLength(500);
            entity.Property(e => e.KeyVaultSecretPrefix).HasMaxLength(100);
            entity.Property(e => e.SAPServiceLayerHostname).HasMaxLength(255);
            entity.Property(e => e.SAPAPIGatewayHostname).HasMaxLength(255);
            entity.Property(e => e.SAPBusinessOneWebClientHost).HasMaxLength(255);
            entity.Property(e => e.DocumentCode).HasMaxLength(50);
            entity.Property(e => e.M365GroupId).HasMaxLength(50); // GUID string format
            entity.Property(e => e.DatabaseType).HasConversion<int>();
            entity.Property(e => e.StateCode).HasConversion<int>();
            entity.Property(e => e.StatusCode).HasConversion<int>();
        });

        // OnboardedUser configuration
        modelBuilder.Entity<OnboardedUser>(entity =>
        {
            entity.HasKey(e => e.OnboardedUserId);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(255);
            entity.Property(e => e.StateCode).HasConversion<int>();
            entity.Property(e => e.StatusCode).HasConversion<int>();
            
            // Configure AgentTypes as JSON
            entity.Property(e => e.AgentTypes)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<LegacyAgentType>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<LegacyAgentType>()
                )
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<LegacyAgentType>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));
                
            // Configure AgentTypeIds as JSON for database-driven agent types
            entity.Property(e => e.AgentTypeIds)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<Guid>()
                )
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Guid>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));
                
            // Configure AssignedDatabaseIds as JSON
            entity.Property(e => e.AssignedDatabaseIds)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<Guid>()
                )
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Guid>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            // Remove foreign key relationship due to insufficient permissions
            entity.Ignore(e => e.Organization);
        });

        // DatabaseCredential configuration
        modelBuilder.Entity<DatabaseCredential>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DatabaseName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ConnectionString).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.DatabaseType).HasConversion<int>();
            entity.Property(e => e.OrganizationId).IsRequired();
            
            // SAP Configuration (optional database-level overrides)
            entity.Property(e => e.SAPServiceLayerHostname).HasMaxLength(255);
            entity.Property(e => e.SAPAPIGatewayHostname).HasMaxLength(255);
            entity.Property(e => e.SAPBusinessOneWebClientHost).HasMaxLength(255);
            entity.Property(e => e.DocumentCode).HasMaxLength(50);
        });

        // UserDatabaseAssignment configuration
        modelBuilder.Entity<UserDatabaseAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.DatabaseCredentialId).IsRequired();
            entity.Property(e => e.OrganizationId).IsRequired();
            entity.Property(e => e.AssignedBy).HasMaxLength(255);
            
            // Create composite index for efficient lookups
            entity.HasIndex(e => new { e.UserId, e.DatabaseCredentialId }).IsUnique();
            entity.HasIndex(e => e.OrganizationId);

            // Remove foreign key relationships due to insufficient permissions
            entity.Ignore(e => e.User);
            entity.Ignore(e => e.DatabaseCredential);
        });

        // AgentTypeEntity configuration
        modelBuilder.Entity<AgentTypeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AgentShareUrl).HasMaxLength(500);
            entity.Property(e => e.GlobalSecurityGroupId).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            
            // Create unique index on Name to prevent duplicates
            entity.HasIndex(e => e.Name).IsUnique();
            
            // Note: Navigation properties removed due to database permission constraints
        });

        // OrganizationTeamsGroup configuration
        modelBuilder.Entity<OrganizationTeamsGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TeamsGroupId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TeamName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TeamUrl).HasMaxLength(500);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            
            // Create unique index to prevent duplicate Teams groups per org/agent type
            entity.HasIndex(e => new { e.OrganizationId, e.AgentTypeId }).IsUnique();
            
            // Create index for efficient lookups
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.AgentTypeId);
            entity.HasIndex(e => e.TeamsGroupId);
            
            // Configure relationships - remove foreign keys due to insufficient permissions
            entity.Ignore(e => e.Organization);
            entity.Ignore(e => e.AgentType);
        });

        // UserAgentTypeGroupAssignment configuration (new table - additive)
        modelBuilder.Entity<UserAgentTypeGroupAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SecurityGroupId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AssignedBy).HasMaxLength(100);
            
            // Create indexes for efficient lookups
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.AgentTypeId);
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.IsActive);
            
            // Configure relationships - remove foreign keys due to insufficient permissions
            entity.Ignore(e => e.AgentType);
            entity.Ignore(e => e.Organization);
        });

        // UserRevocationRecord configuration (new table for security tracking)
        modelBuilder.Entity<UserRevocationRecord>(entity =>
        {
            entity.HasKey(e => e.RevocationRecordId);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.UserEmail).IsRequired().HasMaxLength(255);
            entity.Property(e => e.UserDisplayName).HasMaxLength(255);
            entity.Property(e => e.RevokedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.RestoredBy).HasMaxLength(255);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<int>();
            
            // Configure JSON columns
            entity.Property(e => e.SecurityGroupsRemoved).HasColumnType("nvarchar(max)");
            entity.Property(e => e.M365GroupsRemoved).HasColumnType("nvarchar(max)");
            entity.Property(e => e.AppRolesRevoked).HasColumnType("nvarchar(max)");
            entity.Property(e => e.AdditionalDetails).HasColumnType("nvarchar(max)");
            
            // Create indexes for efficient lookups
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.UserEmail);
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RevokedOn);
            
            // Create composite index for finding active revocations
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasIndex(e => new { e.OrganizationId, e.Status });
            
            // Remove foreign key relationship due to insufficient permissions
            entity.Ignore(e => e.Organization);
        });
    }
}