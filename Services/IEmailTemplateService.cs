using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for generating email templates with dynamic content
/// Handles HTML email generation with embedded images and branding
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Generates an HTML email for agent assignment notifications
    /// </summary>
    /// <param name="userName">Name of the user receiving the notification</param>
    /// <param name="agentType">Agent type that was assigned</param>
    /// <param name="organizationName">Name of the organization</param>
    /// <returns>Formatted HTML email content</returns>
    Task<string> GenerateAgentAssignmentEmailAsync(
        string userName,
        AgentTypeEntity agentType,
        string organizationName);

    /// <summary>
    /// Gets the base64 encoded logo for email embedding
    /// </summary>
    /// <returns>Base64 encoded image string</returns>
    Task<string> GetEmbeddedLogoAsync();
}