using AdminConsole.Models;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Text;

namespace AdminConsole.Services;

/// <summary>
/// Email service implementation using SMTP
/// Handles email sending with professional templates and error handling
/// </summary>
public class EmailService : IEmailService
{
    private readonly EmailOptions _emailOptions;
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<EmailService> _logger;
    private readonly GraphServiceClient _graphServiceClient;

    public EmailService(
        IOptions<EmailOptions> emailOptions,
        IEmailTemplateService templateService,
        ILogger<EmailService> logger,
        GraphServiceClient graphServiceClient)
    {
        _emailOptions = emailOptions.Value;
        _templateService = templateService;
        _logger = logger;
        _graphServiceClient = graphServiceClient;
    }

    public async Task<bool> SendAgentAssignmentNotificationAsync(
        string userEmail,
        string userName,
        AgentTypeEntity agentType,
        string organizationName)
    {
        try
        {
            _logger.LogInformation("📧 Sending agent assignment notification to {UserEmail} for agent type {AgentTypeName}", 
                userEmail, agentType.DisplayName);

            if (!_emailOptions.IsEnabled)
            {
                _logger.LogWarning("📧 Email service is disabled, skipping notification email");
                return true; // Return true to not break the assignment process
            }

            // Generate email content from template
            var subject = $"New Agent Assignment: {agentType.DisplayName}";
            var htmlBody = await _templateService.GenerateAgentAssignmentEmailAsync(
                userName, agentType, organizationName);

            return await SendEmailAsync(userEmail, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📧 Failed to send agent assignment notification to {UserEmail}", userEmail);
            return false; // Don't throw - we don't want email failures to break assignments
        }
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? fromEmail = null)
    {
        try
        {
            if (!_emailOptions.IsEnabled)
            {
                _logger.LogWarning("📧 Email service is disabled, skipping email to {ToEmail}", toEmail);
                return true;
            }

            var senderEmail = fromEmail ?? _emailOptions.FromEmail;
            
            _logger.LogInformation("📧 Sending email via Microsoft Graph API from {FromEmail} to {ToEmail} with subject: {Subject}", 
                senderEmail, toEmail, subject);

            // Create the email message for Graph API
            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlBody
                },
                ToRecipients = new List<Recipient>
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = toEmail
                        }
                    }
                }
            };

            // Send email using Microsoft Graph API
            await _graphServiceClient.Users[senderEmail].SendMail.PostAsync(new()
            {
                Message = message,
                SaveToSentItems = true
            });
            
            _logger.LogInformation("📧 ✅ Successfully sent email via Graph API to {ToEmail} with subject: {Subject}", 
                toEmail, subject);
            
            return true;
        }
        catch (Microsoft.Graph.ServiceException ex)
        {
            _logger.LogError(ex, "📧 ❌ Microsoft Graph API error sending email to {ToEmail}: {GraphError} - {ResponseCode}", 
                toEmail, ex.Message, ex.ResponseStatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📧 ❌ Unexpected error sending email to {ToEmail}", toEmail);
            return false;
        }
    }

    public async Task<bool> IsConfiguredAsync()
    {
        try
        {
            // Check if email service is enabled and we have a valid sender email
            if (!_emailOptions.IsEnabled || string.IsNullOrEmpty(_emailOptions.FromEmail))
            {
                return false;
            }

            // Test Graph API connectivity by checking if the sender mailbox exists
            var user = await _graphServiceClient.Users[_emailOptions.FromEmail].GetAsync();
            return user != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📧 Email service configuration check failed for sender {FromEmail}", _emailOptions.FromEmail);
            return false;
        }
    }
}