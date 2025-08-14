namespace AdminConsole.Models;

/// <summary>
/// Represents the result of a user operation including Azure AD, database, and group changes
/// </summary>
public class UserOperationResult
{
    public string UserEmail { get; set; } = "";
    public bool AzureSuccess { get; set; }
    public bool DatabaseSuccess { get; set; }
    public bool GroupsSuccess { get; set; }
    public string ErrorMessage { get; set; } = "";
}