using AdminConsole.Models;

namespace AdminConsole.Services;

public interface IStateValidationService
{
    // Database state validation
    Task<ValidationResult> ValidateUserAgentAssignmentsAsync(string userId, Guid organizationId);
    Task<ValidationResult> ValidateOnboardedUserStateAsync(string azureObjectId);
    Task<ValidationResult> ValidateDatabaseConsistencyAsync(string userId, Guid organizationId);
    
    // Azure AD state validation
    Task<ValidationResult> ValidateAzureGroupMembershipsAsync(string userId, List<string> expectedGroupIds);
    Task<ValidationResult> ValidateGroupExistenceAsync(List<string> groupIds);
    
    // Bidirectional sync validation
    Task<ValidationResult> ValidateUserStateConsistencyAsync(string userId, Guid organizationId);
    Task<ValidationResult> ValidateAndRepairStateAsync(string userId, Guid organizationId, bool performRepair = false);
    
    // Comprehensive validation
    Task<ValidationResult> ValidateCompleteUserStateAsync(string userId, Guid organizationId);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Repairs { get; set; } = new();
    public Dictionary<string, object> ValidationData { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class StateInconsistency
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public bool CanAutoRepair { get; set; }
    public string RepairAction { get; set; } = string.Empty;
}