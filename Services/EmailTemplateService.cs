using AdminConsole.Models;
using System.Text;

namespace AdminConsole.Services;

/// <summary>
/// Email template service implementation
/// Generates professional HTML emails with embedded branding
/// </summary>
public class EmailTemplateService : IEmailTemplateService
{
    private readonly ILogger<EmailTemplateService> _logger;
    private readonly string _logoPath = @"C:\Users\mn\AdminConsole-Production\wwwroot\company-logo.png";
    private readonly string _botIconPath = @"C:\Users\mn\AdminConsole-Production\wwwroot\4712086.png";
    private string? _cachedLogoBase64;

    public EmailTemplateService(ILogger<EmailTemplateService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GenerateAgentAssignmentEmailAsync(
        string userName,
        AgentTypeEntity agentType,
        string organizationName)
    {
        try
        {
            var logoBase64 = await GetEmbeddedLogoAsync();
            
            var template = new StringBuilder();
            
            template.AppendLine("<!DOCTYPE html>");
            template.AppendLine("<html lang=\"en\">");
            template.AppendLine("<head>");
            template.AppendLine("    <meta charset=\"UTF-8\">");
            template.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            template.AppendLine("    <title>New Agent Assignment - ERPure.AI</title>");
            template.AppendLine("    <style>");
            template.AppendLine("        body { margin: 0; padding: 0; font-family: 'Segoe UI', Arial, sans-serif; background-color: #f5f7fa; }");
            template.AppendLine("        .email-container { max-width: 600px; margin: 0 auto; background-color: #ffffff; }");
            template.AppendLine("        .header { background: linear-gradient(135deg, #2c5aa0 0%, #1e3a8a 100%); color: white; padding: 40px 30px; text-align: center; }");
            template.AppendLine("        .logo { max-width: 200px; height: auto; margin-bottom: 20px; }");
            template.AppendLine("        .header h1 { margin: 0; font-size: 28px; font-weight: 600; }");
            template.AppendLine("        .content { padding: 40px 30px; }");
            template.AppendLine("        .greeting { font-size: 18px; color: #333; margin-bottom: 25px; line-height: 1.5; }");
            template.AppendLine("        .agent-card { background: #f8fafc; border: 2px solid #e2e8f0; border-radius: 12px; padding: 25px; margin: 30px 0; }");
            template.AppendLine("        .agent-name { font-size: 24px; font-weight: 700; color: #1e3a8a; margin: 0 0 10px 0; }");
            template.AppendLine("        .agent-description { font-size: 16px; color: #64748b; line-height: 1.6; margin-bottom: 20px; }");
            template.AppendLine("        .cta-button { display: inline-block; background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white !important; padding: 15px 30px; text-decoration: none !important; border-radius: 8px; font-weight: 600; font-size: 16px; margin: 20px 0; box-shadow: 0 4px 12px rgba(0,0,0,0.15); border: none; cursor: pointer; }");
            template.AppendLine("        .cta-button:hover { transform: translateY(-2px); text-decoration: none !important; color: white !important; }");
            template.AppendLine("        .instructions { background: #eff6ff; border-left: 4px solid #3b82f6; padding: 20px; margin: 25px 0; border-radius: 0 8px 8px 0; }");
            template.AppendLine("        .instructions h3 { margin: 0 0 15px 0; color: #1e40af; font-size: 18px; }");
            template.AppendLine("        .instructions ol { margin: 0; padding-left: 20px; line-height: 1.7; color: #374151; }");
            template.AppendLine("        .footer { background: #1f2937; color: #d1d5db; padding: 30px; text-align: center; font-size: 14px; line-height: 1.6; }");
            template.AppendLine("        .footer a { color: #60a5fa; text-decoration: none; }");
            template.AppendLine("        .divider { height: 1px; background: #e5e7eb; margin: 30px 0; }");
            template.AppendLine("        @media (max-width: 600px) {");
            template.AppendLine("            .email-container { margin: 0; }");
            template.AppendLine("            .content, .header { padding: 25px 20px; }");
            template.AppendLine("            .header h1 { font-size: 24px; }");
            template.AppendLine("            .agent-card { padding: 20px; margin: 20px 0; }");
            template.AppendLine("            .cta-button { display: block; text-align: center; }");
            template.AppendLine("        }");
            template.AppendLine("    </style>");
            template.AppendLine("</head>");
            template.AppendLine("<body>");
            template.AppendLine("    <div class=\"email-container\">");
            
            // Header with logo
            template.AppendLine("        <div class=\"header\">");
            if (!string.IsNullOrEmpty(logoBase64))
            {
                template.AppendLine("            <table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
                template.AppendLine("                <tr>");
                template.AppendLine("                    <td align=\"center\" style=\"padding-bottom: 20px;\">");
                template.AppendLine($"                        <img src=\"data:image/png;base64,{logoBase64}\" alt=\"ERPure.AI Logo\" style=\"max-width: 200px; height: auto; display: block; border: 0; outline: none;\" width=\"200\" height=\"auto\" border=\"0\">");
                template.AppendLine("                    </td>");
                template.AppendLine("                </tr>");
                template.AppendLine("            </table>");
            }
            else
            {
                template.AppendLine("            <div style=\"font-size: 32px; font-weight: bold; color: white; margin-bottom: 20px; text-align: center; font-family: Arial, sans-serif;\">ERPure.AI</div>");
            }
            template.AppendLine("            <h1 style=\"color: white; text-align: center; font-family: Arial, sans-serif; margin: 0;\">🎉 New Agent Assignment!</h1>");
            template.AppendLine("        </div>");
            
            // Main content
            template.AppendLine("        <div class=\"content\">");
            template.AppendLine($"            <p class=\"greeting\">Hi <strong>{userName}</strong>,</p>");
            template.AppendLine($"            <p class=\"greeting\">Great news! You've been assigned a new agent type in <strong>{organizationName}</strong>. This powerful AI agent is now ready to assist you with your daily tasks.</p>");
            
            // Agent details card
            template.AppendLine("            <div class=\"agent-card\">");
            template.AppendLine($"                <h2 class=\"agent-name\">{agentType.DisplayName}</h2>");
            
            if (!string.IsNullOrEmpty(agentType.Description))
            {
                template.AppendLine($"                <p class=\"agent-description\">{agentType.Description}</p>");
            }
            
            // Add agent share URL button if available
            if (!string.IsNullOrEmpty(agentType.AgentShareUrl))
            {
                var botIconBase64 = await GetBotIconAsync();
                var iconHtml = !string.IsNullOrEmpty(botIconBase64) 
                    ? $"<img src=\"data:image/png;base64,{botIconBase64}\" alt=\"Bot Icon\" style=\"width: 20px; height: 20px; margin-right: 8px; vertical-align: middle; display: inline-block; border: 0; outline: none;\" width=\"20\" height=\"20\" border=\"0\">"
                    : "🤖";

                template.AppendLine("                <table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin: 30px 0;\">");
                template.AppendLine("                    <tr>");
                template.AppendLine("                        <td align=\"center\">");
                template.AppendLine($"                            <a href=\"{agentType.AgentShareUrl}\" target=\"_blank\" style=\"display: inline-block; background: #059669; color: white; padding: 15px 30px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; font-family: Arial, sans-serif; border: none;\">");
                template.AppendLine($"                                {iconHtml}<span style=\"color: white; text-decoration: none;\">Click Here to Launch the Agent in Microsoft Teams</span>");
                template.AppendLine("                            </a>");
                template.AppendLine("                        </td>");
                template.AppendLine("                    </tr>");
                template.AppendLine("                </table>");
            }
            
            template.AppendLine("            </div>");
            
            // Instructions
            template.AppendLine("            <div class=\"instructions\">");
            template.AppendLine("                <h3>🔧 Getting Started</h3>");
            template.AppendLine("                <ol>");
            
            if (!string.IsNullOrEmpty(agentType.AgentShareUrl))
            {
                template.AppendLine("                    <li><strong>Click the green button with the bot icon above: \"Click Here to Launch the Agent in Microsoft Teams\"</strong></li>");
                template.AppendLine("                    <li>You'll be redirected to Microsoft Teams in your browser or app</li>");
                template.AppendLine("                    <li>Follow the prompts to add the agent to your Teams environment</li>");
                template.AppendLine("                    <li>Once added, you can start chatting with your new AI agent immediately</li>");
                template.AppendLine("                    <li>The agent will help streamline your workflow and boost productivity</li>");
            }
            else
            {
                template.AppendLine("                    <li>Your agent type has been configured and activated</li>");
                template.AppendLine("                    <li>Access the agent through your organization's designated platform</li>");
                template.AppendLine("                    <li>Contact your administrator if you need help accessing the agent</li>");
            }
            
            template.AppendLine("                </ol>");
            template.AppendLine("            </div>");
            
            template.AppendLine("            <div class=\"divider\"></div>");
            template.AppendLine("            <p style=\"color: #6b7280; font-size: 16px; line-height: 1.6;\">Need help getting started? Our support team is here to assist you. Simply reply to this email or contact your system administrator.</p>");
            template.AppendLine("        </div>");
            
            // Footer
            template.AppendLine("        <div class=\"footer\">");
            template.AppendLine("            <p><strong>ERPure.AI</strong> - Intelligent Business Automation</p>");
            template.AppendLine("            <p>This email was sent from an automated system. Please do not reply directly to this email.</p>");
            template.AppendLine("            <p>© 2025 ERPure.AI. All rights reserved.</p>");
            template.AppendLine("        </div>");
            
            template.AppendLine("    </div>");
            template.AppendLine("</body>");
            template.AppendLine("</html>");

            return template.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📧 Failed to generate agent assignment email template");
            
            // Fallback to simple text-based email
            return GenerateFallbackEmail(userName, agentType, organizationName);
        }
    }

    public async Task<string> GetEmbeddedLogoAsync()
    {
        try
        {
            if (_cachedLogoBase64 != null)
                return _cachedLogoBase64;

            if (!File.Exists(_logoPath))
            {
                _logger.LogWarning("📧 Logo file not found at path: {LogoPath}, using text logo instead", _logoPath);
                return string.Empty;
            }

            var imageBytes = await File.ReadAllBytesAsync(_logoPath);
            
            // Check file size - email clients often have limits on base64 images
            var maxSize = 150000; // 150KB limit for email compatibility
            if (imageBytes.Length > maxSize)
            {
                _logger.LogWarning("📧 Logo file too large ({Size} bytes > {MaxSize} bytes), using text logo instead", 
                    imageBytes.Length, maxSize);
                return string.Empty;
            }
            
            _cachedLogoBase64 = Convert.ToBase64String(imageBytes);
            
            // Validate the base64 string is not empty
            if (string.IsNullOrEmpty(_cachedLogoBase64))
            {
                _logger.LogWarning("📧 Logo file is empty or invalid at {LogoPath}", _logoPath);
                return string.Empty;
            }
            
            _logger.LogInformation("📧 Successfully loaded and cached logo from {LogoPath} ({Size} bytes -> {Base64Length} chars)", 
                _logoPath, imageBytes.Length, _cachedLogoBase64.Length);
            return _cachedLogoBase64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📧 Failed to load logo from {LogoPath}, using text logo instead", _logoPath);
            return string.Empty;
        }
    }

    private string? _cachedBotIconBase64;
    
    public async Task<string> GetBotIconAsync()
    {
        try
        {
            if (_cachedBotIconBase64 != null)
                return _cachedBotIconBase64;

            if (!File.Exists(_botIconPath))
            {
                _logger.LogWarning("📧 Bot icon file not found at path: {BotIconPath}, using emoji instead", _botIconPath);
                return string.Empty;
            }

            var imageBytes = await File.ReadAllBytesAsync(_botIconPath);
            
            // Check file size - smaller limit for icons
            var maxSize = 50000; // 50KB limit for icon compatibility
            if (imageBytes.Length > maxSize)
            {
                _logger.LogWarning("📧 Bot icon file too large ({Size} bytes > {MaxSize} bytes), using emoji instead", 
                    imageBytes.Length, maxSize);
                return string.Empty;
            }
            
            _cachedBotIconBase64 = Convert.ToBase64String(imageBytes);
            
            // Validate the base64 string is not empty
            if (string.IsNullOrEmpty(_cachedBotIconBase64))
            {
                _logger.LogWarning("📧 Bot icon file is empty or invalid at {BotIconPath}", _botIconPath);
                return string.Empty;
            }
            
            _logger.LogInformation("📧 Successfully loaded and cached bot icon from {BotIconPath} ({Size} bytes -> {Base64Length} chars)", 
                _botIconPath, imageBytes.Length, _cachedBotIconBase64.Length);
            return _cachedBotIconBase64;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "📧 Failed to load bot icon from {BotIconPath}, using emoji instead", _botIconPath);
            return string.Empty;
        }
    }

    private string GenerateFallbackEmail(string userName, AgentTypeEntity agentType, string organizationName)
    {
        var fallback = new StringBuilder();
        
        fallback.AppendLine("<html><body style='font-family: Arial, sans-serif;'>");
        fallback.AppendLine($"<h2>New Agent Assignment</h2>");
        fallback.AppendLine($"<p>Hi {userName},</p>");
        fallback.AppendLine($"<p>You have been assigned the agent type <strong>{agentType.DisplayName}</strong> in {organizationName}.</p>");
        
        if (!string.IsNullOrEmpty(agentType.Description))
        {
            fallback.AppendLine($"<p><em>{agentType.Description}</em></p>");
        }
        
        if (!string.IsNullOrEmpty(agentType.AgentShareUrl))
        {
            fallback.AppendLine($"<p><a href='{agentType.AgentShareUrl}' target='_blank'>🤖 Click Here to Launch the Agent in Microsoft Teams</a></p>");
        }
        
        fallback.AppendLine("<p>Best regards,<br>ERPure.AI Team</p>");
        fallback.AppendLine("</body></html>");
        
        return fallback.ToString();
    }
}