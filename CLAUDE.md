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

# Run in development mode
dotnet run

# Run with specific environment
dotnet run --environment Development
```

### Database Operations
```bash
# Add new migration
dotnet ef migrations add <MigrationName>

# Update database
dotnet ef database update

# Remove last migration (if not applied)
dotnet ef migrations remove
```

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

## Conversation Notes
- I can't see the new tables or the columns but you may proceed