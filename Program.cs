using AdminConsole;
using AdminConsole.Components;
using AdminConsole.Configuration;
using AdminConsole.Middleware;
using AdminConsole.Services;
using AdminConsole.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

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

// Add authorization services
builder.Services.AddAuthorizationCore();

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

// Add authorization policies for multi-tenant B2B
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy => 
        policy.RequireAssertion(context => 
        {
            // Skip logging in policy to avoid BuildServiceProvider warning
            ILogger? logger = null;
            
            logger?.LogInformation("SuperAdminOnly policy evaluation - User authenticated: {IsAuth}", 
                context.User.Identity?.IsAuthenticated);
            
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var email = context.User.FindFirst("email")?.Value ?? 
                           context.User.FindFirst("preferred_username")?.Value ?? 
                           context.User.FindFirst("upn")?.Value ?? 
                           context.User.FindFirst("unique_name")?.Value ??
                           context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
                
                logger?.LogInformation("SuperAdminOnly policy - Email found: {Email}", email);
                
                var isErpureUser = email?.EndsWith("@erpure.ai") == true;
                logger?.LogInformation("SuperAdminOnly policy - Is Erpure user: {IsErpure}", isErpureUser);
                
                return isErpureUser;
            }
            
            logger?.LogWarning("SuperAdminOnly policy - User not authenticated");
            return false;
        }));
    
    options.AddPolicy("OrgAdminOrHigher", policy => 
        policy.RequireAssertion(context => 
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return false;
            }
            
            // Check for Azure AD app roles first (preferred method)
            if (context.User.IsInRole("OrgAdmin") || context.User.IsInRole("SuperAdmin"))
            {
                return true;
            }
            
            // Fallback: Super Admins by email domain (for @erpure.ai users)
            var email = context.User.FindFirst("email")?.Value ?? 
                       context.User.FindFirst("preferred_username")?.Value ?? 
                       context.User.FindFirst("upn")?.Value ?? 
                       context.User.FindFirst("unique_name")?.Value ??
                       context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
            
            if (!string.IsNullOrEmpty(email))
            {
                var isSuperAdmin = email.EndsWith("@erpure.ai", StringComparison.OrdinalIgnoreCase);
                if (isSuperAdmin)
                {
                    return true;
                }
            }
            
            // Deny access if no admin role or super admin email
            return false;
        }));
        
    options.AddPolicy("OrgAdminOnly", policy => 
        policy.RequireAssertion(context => 
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var email = context.User.FindFirst("email")?.Value ?? 
                           context.User.FindFirst("preferred_username")?.Value ?? 
                           context.User.FindFirst("upn")?.Value ?? 
                           context.User.FindFirst("unique_name")?.Value ??
                           context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value;
                
                // Only show org admin section for non-erpure.ai users (actual org admins)
                return email != null && !email.EndsWith("@erpure.ai");
            }
            return false;
        }));
    
    options.AddPolicy("DevOnly", policy => 
        policy.RequireRole("DevRole"));
        
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

// New services for enhanced functionality (additive)
builder.Services.AddScoped<IAgentGroupAssignmentService, AgentGroupAssignmentService>();
builder.Services.AddScoped<IPowerShellExecutionService, PowerShellExecutionService>();
builder.Services.AddScoped<IUnsavedChangesService, UnsavedChangesService>();

// Add HTTP services for external API calls
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Add memory cache for performance
builder.Services.AddMemoryCache();

// Add MVC and Razor components
builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SignalR for Blazor Server stability
builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.MaximumReceiveMessageSize = 32 * 1024;
});

// Add Blazor authentication services
builder.Services.AddCascadingAuthenticationState();


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

// Add authentication middleware BEFORE authorization
app.UseAuthentication();

// Add data isolation middleware after authentication but before authorization
// app.UseDataIsolation(); // Temporarily disabled for testing

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
});

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
});

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
});

// Simple test endpoint to verify logging works
app.MapGet("/debug/test-logging", (ILogger<Program> logger) =>
{
    logger.LogError(">>> TEST: This is an error level log message");
    logger.LogInformation("This is an info level log message");
    logger.LogWarning("This is a warning level log message");
    
    return Results.Json(new { message = "Logging test completed - check console" });
});

// Test endpoint to diagnose exact permission issue
app.MapPost("/debug/test-permissions", async (IGraphService graphService) =>
{
    var logPath = "C:\\temp\\permission-test.log";
    Directory.CreateDirectory("C:\\temp");
    
    try
    {
        // Test 1: Can we read groups?
        await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Testing group read permissions\n");
        var groupExists = await graphService.GroupExistsAsync("e63773eb-ab43-4ca3-b461-9000a54be5b3");
        await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Group exists check result: {groupExists}\n");
        
        // Test 2: Can we read users?
        await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Testing user read permissions\n");
        var testUser = await graphService.GetUserByEmailAsync("m.nachman@erpure.ai");
        await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: User read result: {(testUser != null ? "SUCCESS" : "FAILED")}\n");
        
        if (testUser != null)
        {
            await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: User ID: {testUser.Id}\n");
            
            // Test 3: Try to add user to group (this is what's failing)
            await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Testing group membership assignment\n");
            try 
            {
                var result = await graphService.AddUserToGroupAsync(testUser.Id, "e63773eb-ab43-4ca3-b461-9000a54be5b3");
                await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Group membership result: {result}\n");
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: Group membership EXCEPTION: {ex.Message}\n");
            }
        }
        
        return Results.Json(new { message = "Permission test completed - check C:\\temp\\permission-test.log" });
    }
    catch (Exception ex)
    {
        await File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - TEST: CRITICAL ERROR: {ex.Message}\n");
        return Results.Json(new { error = ex.Message });
    }
});

app.Run();
