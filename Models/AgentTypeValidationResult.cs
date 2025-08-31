namespace AdminConsole.Models;

/// <summary>
/// Result of agent type assignment validation including supervisor email requirements
/// </summary>
public class AgentTypeValidationResult
{
    /// <summary>
    /// Whether the validation passed
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Error message if validation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// List of agent types that require supervisor email but don't have one provided
    /// </summary>
    public List<string> MissingSupervisionAgentTypes { get; set; } = new();
    
    /// <summary>
    /// Create a successful validation result
    /// </summary>
    public static AgentTypeValidationResult Success()
    {
        return new AgentTypeValidationResult { IsValid = true };
    }
    
    /// <summary>
    /// Create a failed validation result with error message
    /// </summary>
    public static AgentTypeValidationResult Failure(string errorMessage, List<string>? missingSupervisors = null)
    {
        return new AgentTypeValidationResult 
        { 
            IsValid = false, 
            ErrorMessage = errorMessage,
            MissingSupervisionAgentTypes = missingSupervisors ?? new List<string>()
        };
    }
}