# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Standard Workflow
1. First think through the problem, read the codebase for relevant files, and write a plan to todo.md.
2. The plan should have a list of todo items that you can check off as you complete them
3. Before you begin working, check in with me and I will verify the plan.
4. Then, begin working on the todo items, marking them as complete as you go.
5. Please every step of the way just give me a high level explanation of what changes you made
6. Make every task and code change you do as simple as possible. We want to avoid making any massive or complex changes. Every change should impact as little code as possible. Everything is about simplicity.
7. Finally, add a review section to the [todo.md] file with a summary of the changes you made and any other relevant information.
8. DO NOT BE LAZY. NEVER BE LAZY. IF THERE IS A BUG FIND THE ROOT CAUSE AND FIX IT. NO TEMPORARY FIXES. YOU ARE A SENIOR DEVELOPER. NEVER BE LAZY
9. MAKE ALL FIXES AND CODE CHANGES AS SIMPLE AS HUMANLY POSSIBLE. THEY SHOULD ONLY IMPACT NECESSARY CODE RELEVANT TO THE TASK AND NOTHING ELSE. IT SHOULD IMPACT AS LITTLE CODE AS POSSIBLE. YOUR GOAL IS TO NOT INTRODUCE ANY BUGS. IT'S ALL ABOUT SIMPLICITY
10. Please check through all the code you just wrote and make sure it follows security best practices. make sure there are no sensitive information in the front and and there are no vulnerabilities that can be exploited

## Development Commands

### Build and Run
```bash
# Build the application
dotnet build

# Build for release
dotnet build --configuration Release

# Run in development mode
dotnet run

# Run with specific environment
dotnet run --environment Development

# Publish for deployment
dotnet publish -c Release -o ./publish
```

### Database Operations
```bash
# Add new migration after model changes
dotnet ef migrations add <MigrationName>

# Apply pending migrations to database
dotnet ef database update

# Revert to previous migration (only if not applied to production)
dotnet ef database update <PreviousMigrationName>

# Remove last migration (only if not yet applied)
dotnet ef migrations remove

# Generate SQL script for manual application (idempotent)
dotnet ef migrations script --idempotent
```

### Development Debugging
```bash
# Debug endpoints available at:
# https://localhost:7192/debug/agenttypes
# https://localhost:7192/debug/test-permissions
# https://localhost:7192/debug/check-permissions
# https://localhost:7192/debug/test-user-access
```

### SAP HANA Setup (Windows Development)
```powershell
# Extract SAP HANA libraries before building
powershell -ExecutionPolicy Bypass -File extract-hana-libs.ps1

# Verify DLLs are in project root:
# - libadonetHDB.dll
# - libSQLDBCHDB.dll
```

## Project Structure

### Key Directories
- **`/Components`** - Blazor UI components and pages (organized by feature in `/Admin`, `/Shared`)
- **`/Services`** - Business logic layer (~64 service files) including GraphService, OrganizationService, etc.
- **`/Models`** - EF Core entities and view models (OnboardedUser, Organization, DatabaseCredential, etc.)
- **`/Data`** - AdminConsoleDbContext and database configuration
- **`/Migrations`** - Entity Framework Core migration history (18 migrations)
- **`/Middleware`** - Custom middleware (UserAccessValidationMiddleware, DataIsolationMiddleware)
- **`/Authorization`** - Custom authorization handlers (DatabaseRoleHandler)
- **`/Controllers`** - API endpoints and debug controllers
- **`/wwwroot`** - Static assets (CSS, JavaScript, images)
- **`/SQL`** - SQL scripts for database operations
- **`/Scripts`** - PowerShell scripts (extract-hana-libs.ps1, etc.)

### Important Files
- **`Program.cs`** - Application startup, DI configuration (27KB - very large, contains all service registration)
- **`appsettings.json`** - Configuration (⚠️ contains secrets - should move to Key Vault)
- **`appsettings.Development.json`** - Development configuration overrides
- **`AdminConsole.csproj`** - Project configuration and build settings
- **`launchSettings.json`** - Debug profiles (http: 5243, https: 7192)

## Architecture Overview

### Technology Stack
- **Framework**: ASP.NET Core 9.0 Blazor Server
- **Authentication**: Azure AD B2B with Microsoft Identity Web
- **Database**: SQL Server with Entity Framework Core
- **UI**: Blazor Server with Interactive Server Components
- **External Integrations**: Microsoft Graph API, Azure Key Vault, SAP HANA

### Core Architecture Patterns

#### Multi-Tenant B2B Application
- **Tenant Isolation**: Implemented through `DataIsolationService` and middleware
- **Role-Based Access**: Database-driven roles (Developer, SuperAdmin, OrgAdmin, User) with `DatabaseRoleHandler`
- **Organization Management**: Each tenant organization has isolated data and users
- **Cross-Tenant Operations**: Handled by specialized agents for multi-tenant scenarios

#### Security Model
- **Authentication**: Azure AD B2B for external user authentication
- **Authorization**: Custom database-driven authorization policies in `Program.cs`
- **User Access Validation**: `UserAccessValidationMiddleware` validates user permissions on each request
- **Data Isolation**: Organization-scoped data access enforced at middleware level

#### Service Layer Architecture
- **Graph Integration**: `GraphService` handles all Microsoft Graph API operations (users, groups, invitations)
- **User Management**: `SystemUserManagementService` manages console users across tenant and guest contexts
- **Organization Management**: `OrganizationService` handles multi-tenant organization operations
- **Agent/Group Management**: `AgentGroupAssignmentService` and `TeamsGroupService` manage Azure security group assignments

#### Data Models
- **OnboardedUser**: Core user entity with role assignments and organization links
- **Organization**: Tenant organization with settings and user relationships
- **GuestUser**: Wrapper model for backward compatibility, bridges Azure AD users with database entities
- **UserRole Enum**: Developer (3), SuperAdmin (0), OrgAdmin (1), User (2)

### Key Middleware Chain
1. **Authentication** (`UseAuthentication()`)
2. **UserAccessValidationMiddleware** - Validates user permissions
3. **DataIsolationMiddleware** - Enforces tenant data isolation
4. **Authorization** (`UseAuthorization()`)

### Critical Components

#### User Status Determination
The `SystemUserManagementService.DetermineConsoleAppUserStatusAsync()` method implements complex logic for determining user access status across Azure AD and database states:
- **Guest Users**: Checks invitation status ("accepted", "active", "pendingacceptance")
- **Member Users**: Validates against database roles and organization membership
- **Status Types**: Active, Disabled, Revoked, PendingInvitation, InvitationExpired

#### Database Role Authorization
`DatabaseRoleHandler` in `Authorization/` implements custom authorization by:
- Looking up user roles in the database rather than relying on Azure AD claims
- Supporting hierarchical role permissions (e.g., Developer > SuperAdmin > OrgAdmin > User)
- Handling both exact role matches and "allowHigherRoles" scenarios

### Development Considerations

#### SAP HANA Integration
- Requires SAP HANA client drivers installed at `C:\Program Files\sap\hdbclient\`
- Native DLLs are copied during build process via custom MSBuild targets
- Connection handling is in `DatabaseCredentialService`

#### Blazor Server Specifics
- Uses Interactive Server render mode for real-time updates
- SignalR configured with custom timeouts for stability
- Scoped Entity Framework DbContext to avoid concurrency issues

#### Multi-Tenant Data Access
Always use services like `IOrganizationService` and `IDataIsolationService` when accessing cross-tenant data:
```csharp
// CORRECT - uses tenant isolation
var org = await _organizationService.GetByIdAsync(orgId);

// INCORRECT - bypasses tenant checks
var org = await _dbContext.Organizations.FindAsync(orgId);
```

#### User Type Classification
- **Member Users**: Internal tenant users (`UserType = "Member"`)
- **Guest Users**: External B2B users (`UserType = "Guest"`) 
- Status determination logic differs between user types in `SystemUserManagementService`

### Error Handling and Debugging

#### Debug Endpoints
Several `/debug/*` endpoints are available in development for testing:
- `/debug/agenttypes` - View agent type configurations
- `/debug/test-permissions` - Test Microsoft Graph permissions
- `/debug/check-permissions` - Validate Graph API access
- `/debug/test-user-access` - Test user access validation

#### Logging Strategy
- Extensive logging in services with emoji prefixes for easy identification
- Security-sensitive operations are logged with appropriate detail levels
- User access violations and permission issues are logged as warnings/errors

## Common Development Patterns

### Adding a New Blazor Page
1. Create component in `/Components/Pages/` or `/Components/Pages/Admin/`
2. Add `@page "/route"` directive
3. Use `@inherits LayoutComponentBase` for admin pages requiring authorization
4. Inject required services via `@inject` or constructor
5. Add navigation link to parent layout component

### Adding a New Service
1. Create interface in `/Services/I*.cs` (e.g., `INewFeatureService.cs`)
2. Create implementation in `/Services/*.cs`
3. Register in `Program.cs`: `builder.Services.AddScoped<INewFeatureService, NewFeatureService>();`
4. Follow service locator pattern from existing services (GraphService, OrganizationService)
5. Use dependency injection in components or other services

### Working with Multi-Tenant Data
1. Always use `IOrganizationService` or `IDataIsolationService` for cross-org operations
2. Never query `_dbContext.Organizations.FindAsync(id)` directly - use service layer
3. All service methods should enforce organization scoping
4. Trust `DataIsolationMiddleware` to enforce tenant isolation on incoming requests

### User Role Hierarchy
- **Developer (3)** - Unrestricted access to everything
- **SuperAdmin (0)** - Full admin capabilities
- **OrgAdmin (1)** - Organization-level administration
- **User (2)** - Standard user access

Authorization checks in components:
```csharp
[Authorize(Policy = "OrgAdminOrHigher")]  // OrgAdmin, SuperAdmin, Developer
[Authorize(Policy = "SuperAdminOnly")]     // SuperAdmin, Developer
[Authorize(Policy = "DevOnly")]            // Developer only
```

### Database Modifications
1. Modify model in `/Models/`
2. Run `dotnet ef migrations add <DescriptiveName>`
3. Review generated migration in `/Migrations/`
4. Run `dotnet ef database update` to apply locally
5. Commit migration files to source control

### Debugging Tips
- Enable `_logger.LogInformation()` statements before deployment
- Use browser DevTools (F12) to inspect SignalR connections
- Check `/debug/*` endpoints for permission and configuration validation
- Monitor `Console.log()` in browser for JavaScript errors
- Review SQL queries: Enable `EnableSensitiveDataLogging` temporarily in DbContext

## Security Best Practices

### Secrets Management
⚠️ **CRITICAL**: Currently `appsettings.json` contains sensitive information that should NOT be in source control:
- Azure AD ClientSecret
- SQL Server connection strings with credentials
- Key Vault URIs

**Solution**: Move all secrets to Azure Key Vault
```csharp
// In Program.cs
var keyVaultUrl = new Uri(builder.Configuration["KeyVault:VaultUri"]);
var credential = new DefaultAzureCredential();
builder.Configuration.AddAzureKeyVault(keyVaultUrl, credential);
```

### Development Secrets
Use User Secrets for local development instead of appsettings.json:
```bash
dotnet user-secrets init
dotnet user-secrets set "AzureAd:ClientSecret" "your-secret-here"
```

### Input Validation
- Always validate and sanitize user input before processing
- Use Blazor's built-in form validation (`EditForm` with DataAnnotations)
- Validate on server-side as well (client-side can be bypassed)
- Use parameterized queries (EF Core handles this automatically)

### Authorization Checks
- Always use `[Authorize]` attribute on admin components
- Verify user's organization context before returning data
- Log authorization failures for security audit trail
- Use `DatabaseRoleHandler` for role-based access control (not Azure AD claims)

## Performance Optimizations

### SignalR Configuration
- Client timeout: 5 minutes
- Keep-alive interval: 10 seconds
- Maximum receive message size: 64KB
- These are configured for Blazor stability

### Database Query Optimization
- Use `AsNoTracking()` for read-only queries
- Query composite indexes (e.g., `(UserId, DatabaseCredentialId)`)
- Project specific columns instead of fetching entire entities
- Avoid N+1 queries - use `.Include()` for related entities

### Caching
- In-memory caching with 2-5 minute TTLs for frequently accessed data
- Output caching (64MB limit) for static assets
- Brotli + Gzip compression for HTTP responses (40-60% size reduction)

### UI Rendering
- Use `@key` directive for efficient list rendering in Blazor
- Override `ShouldRender()` to prevent unnecessary re-renders
- Lazy-load heavy components when possible
- Call `StateHasChanged()` only when necessary

## Testing Strategy

### Manual Testing
Use available `/debug/*` endpoints for validation:
- `/debug/agenttypes` - Verify agent type configurations
- `/debug/test-permissions` - Test Graph API permissions
- `/debug/check-permissions` - Validate service principal permissions
- `/debug/test-user-access` - Test user authorization validation

### Integration Testing (Recommended)
For future implementation - use xUnit with:
- Test database (separate SQL Server instance)
- Mock Azure AD authentication
- Mock Microsoft Graph API calls
- Test tenant isolation enforcement

### What to Test
1. **Authorization**: Verify role-based access control works
2. **Tenant Isolation**: Ensure users only see their organization's data
3. **Database Operations**: CRUD operations with migrations
4. **External Integrations**: Graph API, Key Vault, SAP HANA connections
5. **User Status Flows**: Guest invitations, member activation, revocation

## Recent Features (January 10, 2025)

### SuperAdmin Role Management - OrgAdmin Invitation & Promotion
**Status:** ✅ Implemented and Tested

SuperAdmins can now:
1. **Invite new users directly as OrgAdmin** via InviteUser.razor role selector
2. **Promote existing Users to OrgAdmin** via ManageUsers.razor role promotion action
3. **Revoke admin rights from OrgAdmins** via ManageUsers.razor revocation action (full admin rights removal)
4. All role changes sync across **both database AND Azure Entra ID app roles**

**Role Assignment Restrictions (Security Policy):**
- ✅ SuperAdmins CAN assign: Organization Administrator, User roles only
- ✅ SuperAdmins CANNOT assign: Developer, SuperAdmin roles
- ✅ Prevents unauthorized privilege escalation
- ✅ Self-modification protection: Cannot change own role

**Key Components:**
- `InviteUser.razor` - Added role dropdown (lines 278-295)
- `ManageUsers.razor` - Added promotion/revocation actions (lines 1285-1317, 1827-1865, 1888-1898)
- `OnboardedUserService.UpdateUserRoleAsync()` - Database role updates with tenant isolation (lines 698-731)
- `UserRole.GetAzureAdAppRole()` - Azure AD app role mapping (lines 49-61)

**Security Features:**
✅ Tenant isolation validation on all operations
✅ SuperAdmin-only authorization checks
✅ Self-modification prevention
✅ Database-first consistency (Azure AD failures handled gracefully)
✅ Comprehensive audit logging
✅ Role assignment policy validation (prevents forbidden role assignment)
✅ Multi-layer security: UI + Backend validation

**Testing:**
- ✅ Scenario 1: SuperAdmin invites new user as OrgAdmin
- ✅ Scenario 2: SuperAdmin promotes User to OrgAdmin
- ✅ Scenario 3: SuperAdmin revokes admin rights from OrgAdmin
- ✅ Scenario 4: Database and Azure AD consistency maintained
- ✅ Scenario 5: Tenant isolation validation enforced
- ✅ Scenario 6: Role assignment policy prevents forbidden assignments
- ✅ Scenario 7: Self-modification prevention blocks self-role changes

**Build Status:** ✅ Success (0 errors, 7 pre-existing warnings)

See bugfix.md for complete feature documentation.

## Conversation Notes
- I can't see the new tables or the columns but you may proceed
- Developer Role should access everything!!
- SuperAdmin role management feature supports both database and Azure Entra ID synchronization