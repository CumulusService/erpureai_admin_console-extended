using AdminConsole.Models;

namespace AdminConsole.Services;

/// <summary>
/// Service for sending email notifications
/// Handles SMTP operations and email templating
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an agent assignment notification email to a user
    /// </summary>
    /// <param name="userEmail">User's email address</param>
    /// <param name="userName">User's full name</param>
    /// <param name="agentType">Agent type that was assigned</param>
    /// <param name="organizationName">Name of the organization</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendAgentAssignmentNotificationAsync(
        string userEmail,
        string userName,
        AgentTypeEntity agentType,
        string organizationName);

    /// <summary>
    /// Sends a generic email with custom content
    /// </summary>
    /// <param name="toEmail">Recipient email address</param>
    /// <param name="subject">Email subject</param>
    /// <param name="htmlBody">HTML content of the email</param>
    /// <param name="fromEmail">Optional custom sender email (defaults to configured sender)</param>
    /// <returns>True if email was sent successfully</returns>
    Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? fromEmail = null);

    /// <summary>
    /// Validates email service configuration
    /// </summary>
    /// <returns>True if email service is properly configured</returns>
    Task<bool> IsConfiguredAsync();
}

/// <summary>
/// Email configuration options for Microsoft Graph API
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";
    
    public string FromEmail { get; set; } = "notification@erpure.ai";
    public string FromName { get; set; } = "ERPure.AI";
    public bool IsEnabled { get; set; } = true;
    
    // Legacy SMTP settings - kept for backwards compatibility but not used with Graph API
    [Obsolete("SMTP settings are no longer used. Email is sent via Microsoft Graph API.")]
    public string SmtpServer { get; set; } = string.Empty;
    [Obsolete("SMTP settings are no longer used. Email is sent via Microsoft Graph API.")]
    public int SmtpPort { get; set; } = 587;
    [Obsolete("SMTP settings are no longer used. Email is sent via Microsoft Graph API.")]
    public bool UseSsl { get; set; } = true;
    [Obsolete("SMTP settings are no longer used. Email is sent via Microsoft Graph API.")]
    public string Username { get; set; } = string.Empty;
    [Obsolete("SMTP settings are no longer used. Email is sent via Microsoft Graph API.")]
    public string Password { get; set; } = string.Empty;
}