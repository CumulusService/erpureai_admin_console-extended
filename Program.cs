using AdminConsole;
using AdminConsole.Authorization;
using AdminConsole.Components;
using AdminConsole.Configuration;
using AdminConsole.Middleware;
using AdminConsole.Models;
using AdminConsole.Services;
using AdminConsole.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using AdminConsole.HealthChecks;
using AdminConsole.Hubs;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure AD options
builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection("AzureAd"));

// Add Azure AD B2B Authentication 
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.GetSection("AzureAd").Bind(options);
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
    })
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Add Entity Framework with proper configuration for Blazor Server
builder.Services.AddDbContext<AdminConsoleDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.EnableServiceProviderCaching(false);
    options.EnableSensitiveDataLogging(false);
}, ServiceLifetime.Scoped);

// Add authorization services with database-driven role handlers
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, DatabaseRoleHandler>();

// Add Microsoft Graph Service Client with Azure Identity
builder.Services.AddSingleton<Microsoft.Graph.GraphServiceClient>(serviceProvider =>
{
    var options = new Azure.Identity.ClientSecretCredentialOptions
    {
        AuthorityHost = Azure.Identity.AzureAuthorityHosts.AzurePublicCloud,
    };
    
    var clientSecretCredential = new Azure.Identity.ClientSecretCredential(
        builder.Configuration["AzureAd:TenantId"],
        builder.Configuration["AzureAd:ClientId"],
        builder.Configuration["AzureAd:ClientSecret"],
        options);

    return new Microsoft.Graph.GraphServiceClient(clientSecretCredential);
});

// Add authorization policies for multi-tenant B2B with database-driven role checks
builder.Services.AddAuthorization(options =>
{
    // NEW: Database-driven role policies (replaces hardcoded domain checks)
    options.AddPolicy("SuperAdminOnly", policy => 
        policy.RequireDatabaseRole(UserRole.SuperAdmin, allowHigherRoles: true)); // Allow Developer access
    
    options.AddPolicy("OrgAdminOrHigher", policy => 
        policy.RequireDatabaseRole(UserRole.OrgAdmin, allowHigherRoles: true));
        
    options.AddPolicy("OrgAdminOnly", policy => 
        policy.RequireDatabaseRole(UserRole.OrgAdmin, allowHigherRoles: false));
    
    options.AddPolicy("OrgUserOrHigher", policy => 
        policy.RequireDatabaseRole(UserRole.User, allowHigherRoles: true));

    options.AddPolicy("DevOnly", policy => 
        policy.RequireDatabaseRole(UserRole.Developer, allowHigherRoles: false)); // Developer only - block SuperAdmin
        
    options.AddPolicy("AuthenticatedUser", policy => 
        policy.RequireAuthenticatedUser());
});

// Add Azure services with proper nullable handling
builder.Services.AddSingleton<SecretClient>(provider =>
{
    try
    {
        var keyVaultUri = builder.Configuration["KeyVault:VaultUri"];
        if (string.IsNullOrEmpty(keyVaultUri))
        {
            return null!; // Return null if no Key Vault URI configured
        }
        return new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
    }
    catch (Exception ex)
    {
        var logger = provider.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Failed to create Key Vault SecretClient. App will continue without Key Vault connectivity.");
        return null!; // Return null on connection failure
    }
});

// Dataverse ServiceClient removed - migrated to SQL Server

// Add application services
builder.Services.AddScoped<IGraphService, GraphService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IOrganizationSetupService, OrganizationSetupService>();
builder.Services.AddScoped<IKeyVaultService, KeyVaultService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<ISecurityGroupService, SecurityGroupService>();
builder.Services.AddScoped<ITeamsGroupService, TeamsGroupService>();
builder.Services.AddScoped<IAgentTypeService, AgentTypeService>();
builder.Services.AddScoped<IDataIsolationService, DataIsolationService>();
builder.Services.AddScoped<ITenantIsolationValidator, TenantIsolationValidator>();
builder.Services.AddScoped<IDatabaseCredentialService, DatabaseCredentialService>();
builder.Services.AddScoped<IUserDatabaseAccessService, UserDatabaseAccessService>();
builder.Services.AddScoped<IOnboardedUserService, OnboardedUserService>();
builder.Services.AddScoped<IDatabaseAssignmentCleanupService, DatabaseAssignmentCleanupService>();

// New services for enhanced functionality (additive)
builder.Services.AddScoped<IAgentGroupAssignmentService, AgentGroupAssignmentService>();
builder.Services.AddScoped<IPowerShellExecutionService, PowerShellExecutionService>();
builder.Services.AddScoped<IUnsavedChangesService, UnsavedChangesService>();

// User access validation service for security
builder.Services.AddScoped<IUserAccessValidationService, UserAccessValidationService>();

// Group repair service for fixing stale group IDs during reactivation
builder.Services.AddScoped<IGroupRepairService, GroupRepairService>();

// Enhanced validation and operation tracking services
builder.Services.AddScoped<IStateValidationService, StateValidationService>();
builder.Services.AddScoped<IOperationStatusService, OperationStatusService>();

// Modern modal dialog system
builder.Services.AddScoped<IModalService, ModalService>();

// SuperAdmin role management and migration service
builder.Services.AddScoped<ISuperAdminMigrationService, SuperAdminMigrationService>();

// Business domain validation service for email invitations
builder.Services.AddScoped<IBusinessDomainValidationService, BusinessDomainValidationService>();

// System user management service for Master Developer functionality
builder.Services.AddScoped<ISystemUserManagementService, SystemUserManagementService>();

// Resilience service for external service retry policies and circuit breakers
builder.Services.AddScoped<IResilienceService, ResilienceService>();

// State synchronization validation service for database integrity
builder.Services.AddSingleton<IStateSyncValidationService, StateSyncValidationService>();

// Real-time state update notification service for UI synchronization
builder.Services.AddScoped<IStateUpdateNotificationService, StateUpdateNotificationService>();

// Orphaned resource detection service for database integrity maintenance
builder.Services.AddSingleton<IOrphanedResourceDetectionService, OrphanedResourceDetectionService>();

// Orphaned resource cleanup service for maintenance
// Orphaned resource cleanup service removed (compilation errors)

// Email notification services for user communications
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Add HTTP services for external API calls with proper resource management
builder.Services.AddHttpClient("DefaultHttpClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "AdminConsole/1.0");
});

// Add named HTTP clients for specific services
builder.Services.AddHttpClient("GraphAPI", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.BaseAddress = new Uri("https://graph.microsoft.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "AdminConsole-GraphAPI/1.0");
});

builder.Services.AddHttpClient("KeyVault", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "AdminConsole-KeyVault/1.0");
});

builder.Services.AddHttpContextAccessor();

// Add memory cache for performance with centralized configuration
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheConfigurationService, CacheConfigurationService>();

// Add navigation performance optimization service
builder.Services.AddSingleton<INavigationOptimizationService, NavigationOptimizationService>();

// Add health checks for monitoring external dependencies
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AdminConsoleDbContext>("database")
    .AddCheck<GraphApiHealthCheck>("graph_api")
    .AddCheck<KeyVaultHealthCheck>("key_vault");

// Add MVC and Razor components
builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SignalR for Blazor Server performance and responsiveness
builder.Services.AddSignalR(options =>
{
    // 🚀 Enhanced performance optimizations + extended timeouts for connection testing
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);  // Extended to prevent disconnections during comprehensive connection tests
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);     // More frequent keep-alive during long operations
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);      // Slightly longer handshake for stability
    options.MaximumReceiveMessageSize = 64 * 1024;            // Optimized message size
    options.StreamBufferCapacity = 10;                        // Better buffering for high throughput
    options.MaximumParallelInvocationsPerClient = 6;          // More parallel operations
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Add Blazor authentication services
builder.Services.AddCascadingAuthenticationState();

// 🚀 Enhanced performance optimizations
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/octet-stream",
        "image/svg+xml",
        "application/javascript",
        "text/css",
        "text/html",
        "application/json",
        "text/json"
    });
    opts.EnableForHttps = true;
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = 64 * 1024 * 1024; // 64MB cache limit
    options.UseCaseSensitivePaths = false;
    options.SizeLimit = 200 * 1024 * 1024; // 200MB total cache
});

// Add output caching for static responses
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Cache());
    options.AddPolicy("StaticAssets", builder => 
        builder.Cache()
               .Expire(TimeSpan.FromHours(24))
               .SetVaryByHeader("Accept-Encoding"));
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    // Skip HTTPS redirection in development to avoid port determination issues
    app.UseDeveloperExceptionPage();
}

// 🚀 Performance middleware optimized order
app.UseResponseCompression();
app.UseOutputCache();
app.UseResponseCaching();

// Static file optimizations
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static assets for 1 year with version busting
        if (ctx.File.Name.Contains('.') && 
            (ctx.File.Name.EndsWith(".css") || 
             ctx.File.Name.EndsWith(".js") || 
             ctx.File.Name.EndsWith(".png") || 
             ctx.File.Name.EndsWith(".jpg") ||
             ctx.File.Name.EndsWith(".ico")))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
        }
    }
});

// Add authentication middleware BEFORE authorization
app.UseAuthentication();

// Add user access validation middleware after authentication but before authorization
app.UseMiddleware<UserAccessValidationMiddleware>();

// Add data isolation middleware after authentication but before authorization
app.UseDataIsolation();

app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map Microsoft Identity Web routes
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map SignalR hub for real-time UI updates
app.MapHub<StateUpdateHub>("/stateupdatehub");

// Temporary debug endpoint to check AgentTypes data
app.MapGet("/debug/agenttypes", async (IAgentTypeService agentTypeService) =>
{
    var agentTypes = await agentTypeService.GetAllAgentTypesAsync();
    return Results.Json(agentTypes.Select(at => new 
    {
        at.Id,
        at.Name,
        at.DisplayName,
        at.GlobalSecurityGroupId,
        at.AgentShareUrl,
        at.IsActive
    }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}).RequireAuthorization("SuperAdminOnly");

// Temporary debug endpoint to fix the Admin agent type
app.MapPost("/debug/fix-admin-agent", async (IAgentTypeService agentTypeService) =>
{
    var allAgents = await agentTypeService.GetAllAgentTypesAsync();
    var adminAgent = allAgents.FirstOrDefault(at => at.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    
    if (adminAgent == null)
    {
        return Results.Json(new { 
            success = false, 
            message = "Admin agent type not found",
            availableAgents = allAgents.Select(a => new { a.Id, a.Name }).ToList()
        });
    }

    // Update the Admin agent with a security group ID (using one of the existing ones for now)
    adminAgent.GlobalSecurityGroupId = "9af9d73b-f6af-4b5b-b4aa-88885525b84d"; // Same as ERPureAI agent for testing
    adminAgent.IsActive = true;
    adminAgent.DisplayName = "Admin Access Agent";
    adminAgent.Description = "Administrative access to all organization functions";
    
    var success = await agentTypeService.UpdateAsync(adminAgent);
    
    return Results.Json(new { 
        success, 
        message = success ? "Admin agent type updated successfully" : "Failed to update admin agent type",
        agentType = new {
            adminAgent.Id,
            adminAgent.Name,
            adminAgent.DisplayName,
            adminAgent.GlobalSecurityGroupId,
            adminAgent.IsActive
        }
    });
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint to directly test group assignment and Teams creation
app.MapPost("/debug/test-services", async (IAgentGroupAssignmentService agentService, ITeamsGroupService teamsService, IGraphService graphService) =>
{
    var testUserId = "123eeb9b-2d09-48f7-a8bb-baa215fb8592"; // Your user ID from logs
    var testOrgId = Guid.NewGuid();
    var testAgentTypeIds = new List<Guid> { 
        Guid.Parse("15b4a42c-c51c-4973-bf56-573a937faba9"), // ERPureAI agent
        Guid.Parse("d2959e82-9aad-4b54-8563-3efc34b9b4f2")  // SBO agent
    };
    
    var results = new List<object>();
    
    // Test 1: Agent group assignment
    try 
    {
        var agentResult = await agentService.AssignUserToAgentTypeGroupsAsync(testUserId, testAgentTypeIds, testOrgId, "test-system");
        results.Add(new { test = "AgentGroupAssignment", success = agentResult, error = (string?)null });
    }
    catch (Exception ex)
    {
        results.Add(new { test = "AgentGroupAssignment", success = false, error = ex.Message });
    }
    
    // Test 2: Teams group creation
    try 
    {
        var teamsResult = await teamsService.EnsureOrganizationTeamsGroupAsync(testOrgId, Guid.Parse(testUserId));
        results.Add(new { test = "TeamsGroupCreation", success = teamsResult, error = (string?)null });
    }
    catch (Exception ex)
    {
        results.Add(new { test = "TeamsGroupCreation", success = false, error = ex.Message });
    }
    
    // Test 3: Direct GraphService group assignment
    try 
    {
        var groupResult = await graphService.AddUserToGroupAsync(testUserId, "9af9d73b-f6af-4b5b-b4aa-88885525b84d");
        results.Add(new { test = "DirectGroupAssignment", success = groupResult, error = (string?)null });
    }
    catch (Exception ex)
    {
        results.Add(new { test = "DirectGroupAssignment", success = false, error = ex.Message });
    }
    
    return Results.Json(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}).RequireAuthorization("SuperAdminOnly");

// Simple test endpoint to verify logging works
app.MapGet("/debug/test-logging", (ILogger<Program> logger) =>
{
    logger.LogError(">>> TEST: This is an error level log message");
    logger.LogInformation("This is an info level log message");
    logger.LogWarning("This is a warning level log message");
    
    return Results.Json(new { message = "Logging test completed - check console" });
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint for user access validation
app.MapPost("/debug/test-user-access", async (IUserAccessValidationService accessService, HttpContext context) =>
{
    var userId = context.User.FindFirst("oid")?.Value ?? "";
    var email = context.User.FindFirst("email")?.Value ?? "";
    
    var result = await accessService.ValidateUserAccessAsync(userId, email);
    
    return Results.Json(new { 
        userId = userId,
        email = email,
        hasAccess = result.HasAccess,
        reason = result.Reason,
        userFound = result.User != null,
        organizationFound = result.Organization != null
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint to manually revoke a user
app.MapPost("/debug/revoke-user", async (IUserAccessValidationService accessService, HttpContext context) =>
{
    var userEmail = context.Request.Query["email"].ToString();
    if (string.IsNullOrEmpty(userEmail))
    {
        return Results.BadRequest("Email parameter required");
    }
    
    var revokedBy = context.User.FindFirst("email")?.Value ?? "system";
    var success = await accessService.RevokeUserAccessAsync(userEmail, revokedBy);
    
    return Results.Json(new { 
        success = success,
        userEmail = userEmail,
        revokedBy = revokedBy,
        message = success ? "User access revoked successfully" : "Failed to revoke user access"
    });
}).RequireAuthorization("SuperAdminOnly");

// Orphaned resource cleanup endpoint removed (compilation errors)

// Test endpoint to check Graph permissions
app.MapGet("/debug/check-permissions", async (IGraphService graphService) =>
{
    var permissionStatus = await graphService.CheckUserManagementPermissionsAsync();
    
    return Results.Json(new {
        canDisableUsers = permissionStatus.CanDisableUsers,
        canDeleteUsers = permissionStatus.CanDeleteUsers,
        canManageGroups = permissionStatus.CanManageGroups,
        missingPermissions = permissionStatus.MissingPermissions,
        errorMessages = permissionStatus.ErrorMessages,
        recommendations = new[]
        {
            permissionStatus.CanDisableUsers ? "✅ User disable capability confirmed" : "❌ Cannot disable users - check Azure App Registration permissions",
            "Required permissions: User.ReadWrite.All or Directory.ReadWrite.All",
            "Make sure to grant admin consent for the application"
        }
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint to disable/enable a user account
app.MapPost("/debug/disable-user", async (IGraphService graphService, HttpContext context) =>
{
    var userId = context.Request.Query["userId"].ToString();
    var action = context.Request.Query["action"].ToString().ToLower(); // "disable" or "enable"
    
    if (string.IsNullOrEmpty(userId))
    {
        return Results.BadRequest("userId parameter required");
    }
    
    bool success;
    string message;
    
    if (action == "enable")
    {
        success = await graphService.EnableUserAccountAsync(userId);
        message = success ? "User account enabled successfully" : "Failed to enable user account";
    }
    else
    {
        success = await graphService.DisableUserAccountAsync(userId);
        message = success ? "User account DISABLED in Azure Entra ID - user cannot authenticate" : "Failed to disable user account";
    }
    
    return Results.Json(new {
        success = success,
        userId = userId,
        action = action,
        message = message,
        securityNote = success && action == "disable" ? 
            "🔒 CRITICAL: User is now completely blocked from authentication in Azure Entra ID" :
            success && action == "enable" ?
            "🔓 User account is now enabled and can authenticate" :
            "❌ Operation failed - check logs for details"
    });
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint to diagnose exact permission issue
app.MapPost("/debug/test-permissions", async (IGraphService graphService, ILogger<Program> logger) =>
{
    try
    {
        // Test 1: Can we read groups?
        logger.LogInformation("TEST: Testing group read permissions");
        var groupExists = await graphService.GroupExistsAsync("e63773eb-ab43-4ca3-b461-9000a54be5b3");
        logger.LogInformation("TEST: Group exists check result: {GroupExists}", groupExists);
        
        // Test 2: Can we read users?
        logger.LogInformation("TEST: Testing user read permissions");
        var testUser = await graphService.GetUserByEmailAsync("m.nachman@erpure.ai");
        logger.LogInformation("TEST: User read result: {UserResult}", testUser != null ? "SUCCESS" : "FAILED");
        
        if (testUser != null)
        {
            logger.LogInformation("TEST: User ID: {UserId}", testUser.Id);
            
            // Test 3: Try to add user to group (this is what's failing)
            logger.LogInformation("TEST: Testing group membership assignment");
            try 
            {
                var result = await graphService.AddUserToGroupAsync(testUser.Id, "e63773eb-ab43-4ca3-b461-9000a54be5b3");
                logger.LogInformation("TEST: Group membership result: {Result}", result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TEST: Group membership EXCEPTION: {Message}", ex.Message);
            }
        }
        
        return Results.Json(new { message = "Permission test completed - check application logs for details" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "TEST: CRITICAL ERROR: {Message}", ex.Message);
        return Results.Json(new { error = ex.Message });
    }
}).RequireAuthorization("SuperAdminOnly");

// Test endpoint for business domain validation
app.MapPost("/debug/test-business-domain", (IBusinessDomainValidationService validationService, HttpContext context) =>
{
    var email = context.Request.Query["email"].ToString();
    if (string.IsNullOrEmpty(email))
    {
        return Results.BadRequest("Email parameter required");
    }
    
    var validation = validationService.ValidateBusinessDomain(email);
    
    return Results.Json(new
    {
        email = email,
        isValid = validation.IsValid,
        message = validation.Message,
        domain = validation.Domain,
        failureType = validation.FailureType?.ToString(),
        blockedDomains = validationService.GetBlockedPrivateDomains().Take(10), // Show first 10 for testing
        totalBlockedDomains = validationService.GetBlockedPrivateDomains().Count
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}).RequireAuthorization("SuperAdminOnly");

// Map health check endpoints for monitoring
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                duration = entry.Value.Duration,
                description = entry.Value.Description,
                data = entry.Value.Data,
                exception = entry.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
});

// Simple health check endpoint for load balancers
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Detailed health check endpoint for monitoring systems (requires authentication)
app.MapHealthChecks("/health/detailed").RequireAuthorization("SuperAdminOnly");

// 🚀 Start navigation optimization background preloading
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(5000); // Wait for app to fully start
        var navigationService = app.Services.GetRequiredService<INavigationOptimizationService>();
        await navigationService.PreloadCommonDataAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Navigation preloading failed but app will continue normally");
    }
});

app.Run();
