# Feature Flow Diagrams - SuperAdmin Role Management

## 1. Invitation Flow with Role Selection

```
SuperAdmin/Developer User
         ↓
    Opens InviteUser
         ↓
   ┌─────────────────────┐
   │ Role Selector SHOWN │
   │ (dropdown visible)  │
   └─────────────────────┘
         ↓
   Select Role:
   • Standard User
   • Organization Administrator
         ↓
   Fill Other Fields:
   • Display Name
   • Email
   • Agent Types
   • Database Access
         ↓
   Click "Send Invitation"
         ↓
   Backend: InvitationService
   ├─ Create user with selected role
   ├─ Create in Azure AD with app role mapping
   └─ Send invitation email
         ↓
   ✅ Success Message
   "Role: [Selected Role]"
         ↓
   Redirect to User List
   ↓
   User appears with correct role badge

═══════════════════════════════════════════════════

OrgAdmin/Regular User
         ↓
    Opens InviteUser
         ↓
   ┌─────────────────────────┐
   │ Role Selector NOT SHOWN │
   │ (defaults to User)      │
   └─────────────────────────┘
         ↓
   Continue with regular invite
```

---

## 2. Promotion Flow

```
SuperAdmin/Developer User
         ↓
    Opens /admin/users
         ↓
    Finds active User
    (role = "User")
         ↓
    Clicks (⋮) menu
         ↓
   👨‍💼 "Promote to OrgAdmin"
    is VISIBLE
         ↓
    Click action
         ↓
   ┌──────────────────────────────┐
   │ Promotion Modal Opens        │
   │ • Shows user details         │
   │ • Current: User              │
   │ • New: OrgAdmin              │
   │ • Shows consequences         │
   │ • Shows policy warnings      │
   └──────────────────────────────┘
         ↓
    Clicks "Promote to OrgAdmin"
         ↓
    Backend: ChangeUserRole()
    ├─ 1. Validates organization access
    ├─ 2. Checks policy: can assign OrgAdmin? ✅
    ├─ 3. Updates database role
    ├─ 4. Gets Azure Object ID
    ├─ 5. Revokes old app role (OrgUser)
    ├─ 6. Assigns new app role (OrgAdmin)
    ├─ 7. Refreshes user list
    └─ 8. Shows success message
         ↓
    ✅ User promoted
    Role badge: User → [Org Admin]
    User can now manage other users

═══════════════════════════════════════════════════

OrgAdmin User (same flow)
         ↓
    Opens /admin/users
         ↓
    Finds User
         ↓
    Clicks (⋮) menu
         ↓
   "Promote to OrgAdmin" action
    is NOT VISIBLE
    (authorization check failed)
```

---

## 3. Revocation Flow

```
SuperAdmin/Developer User
         ↓
    Opens /admin/users
         ↓
    Finds OrgAdmin user
    (role = "Organization Administrator")
         ↓
    Clicks (⋮) menu
         ↓
   🚫 "Revoke Admin Rights"
    is VISIBLE (red danger)
         ↓
    Click action
         ↓
   ┌──────────────────────────────┐
   │ Revocation Modal Opens       │
   │ 🚫 Revoke Administrator      │
   │    Rights                    │
   │ • Shows user details         │
   │ • Current: OrgAdmin          │
   │ • New: User                  │
   │ • Shows RED DANGER alert     │
   │ • Lists PERMANENT revocations│
   └──────────────────────────────┘
         ↓
    Clicks "Revoke Admin Rights"
         ↓
    Backend: ChangeUserRole()
    ├─ 1. Validates organization access
    ├─ 2. Checks policy: can assign User? ✅
    ├─ 3. Updates database role
    ├─ 4. Gets Azure Object ID
    ├─ 5. Revokes old app role (OrgAdmin)
    ├─ 6. Assigns new app role (OrgUser)
    ├─ 7. Refreshes user list
    └─ 8. Shows success message
         ↓
    ✅ Admin rights revoked
    Role badge: [Org Admin] → User
    User CANNOT manage other users
    (Revoke action now hidden for this user)
```

---

## 4. Self-Modification Prevention Flow

```
SuperAdmin opens /admin/users
         ↓
    Finds themselves in list
    (Email matches current user)
         ↓
    Clicks (⋮) menu
         ↓
    Checks: IsCurrentUser(user)?
         ↓
         YES
         ↓
   "Promote to OrgAdmin" action
    is HIDDEN (UI-level prevention)
         ↓
   "Revoke Admin Rights" action
    is HIDDEN (UI-level prevention)
         ↓
    Other actions visible
    (deactivate, etc.)

════════════════════════════════════════════════════

If somehow action is triggered anyway:
         ↓
    ShowPromoteToOrgAdminConfirmation()
         ↓
    Checks: IsCurrentUser(user)?
         ↓
         YES
         ↓
    ❌ Error: "You cannot change your own role"
    (Backend-level prevention)
    ↓
    Logged as security event
```

---

## 5. Role Assignment Policy Validation

```
SuperAdmin attempts to promote user to Developer role
(either via UI bypass or direct API call)
         ↓
    ChangeUserRole(user, UserRole.Developer)
    called
         ↓
    Policy Validation Check:
    ┌─────────────────────────────────┐
    │ if (currentUserRole == SuperAdmin│
    │     && newRole == Developer)    │
    │                                  │
    │    BLOCK!                        │
    └─────────────────────────────────┘
         ↓
    ❌ Error:
    "Security Policy Violation:
     SuperAdmins cannot assign Developer role.
     Only Organization Administrator and
     Standard User roles are allowed."
         ↓
    Security Event Logged:
    "SECURITY: SuperAdmin [email] attempted
     to assign forbidden role Developer to [user]"
         ↓
    Operation ABORTED
    Role NOT changed

════════════════════════════════════════════════════

Allowed Assignments (SuperAdmin):
✅ User → OrgAdmin
✅ OrgAdmin → User
✅ New invitation as User
✅ New invitation as OrgAdmin

Blocked Assignments (SuperAdmin):
❌ User → Developer
❌ User → SuperAdmin
❌ OrgAdmin → Developer
❌ OrgAdmin → SuperAdmin
```

---

## 6. Azure AD Sync Process

```
Database Update Successful
         ↓
    Get Azure Object ID
         ↓
    Azure Object ID Found?
         ├─ YES
         │  ↓
         │ Old App Role: OrgUser
         │ New App Role: OrgAdmin
         │      ↓
         │ Revoke old role from Azure AD
         │      ↓
         │ Assign new role in Azure AD
         │      ↓
         │ Success?
         │  ├─ YES
         │  │  ↓
         │  │ ✅ Complete sync
         │  │ Show success message
         │  │
         │  └─ NO
         │     ↓
         │     ⚠️ Warning: "Failed to sync to Azure AD
         │        but database was updated"
         │     (Database is source of truth)
         │
         └─ NO
            ↓
            ⚠️ Warning: "Could not find user in Azure AD"
            (Attempt to look up from Graph API)
            ↓
            Found?
            ├─ YES: Continue with sync
            └─ NO: Database change persists
                   (No Azure AD sync, but OK)

════════════════════════════════════════════════════

Note: Database is ALWAYS source of truth
If Azure AD sync fails, database change persists
User can still function with new role in system
Azure AD eventually syncs (may need manual reconciliation)
```

---

## 7. Tenant Isolation Validation

```
SuperAdmin initiates role change
for User in Organization A
         ↓
    ChangeUserRole()
         ↓
    Tenant Isolation Check:
    ┌─────────────────────────────────┐
    │ ValidateOrganizationAccessAsync │
    │ (organizationId: OrgA)          │
    └─────────────────────────────────┘
         ↓
    Check: Does current user
    have access to OrgA?
         ├─ YES (SuperAdmin)
         │  ↓
         │ ✅ Allowed to proceed
         │ Role change executed in OrgA
         │
         └─ NO
            ↓
            ❌ BLOCKED
            "Access Denied to Organization"
            Security Event Logged
            Role NOT changed

════════════════════════════════════════════════════

Scenario: User from OrgA tries to access OrgB
         ↓
    Organization isolation enforced
         ↓
    ❌ Cannot see OrgB users
    ❌ Cannot promote users in OrgB
    ❌ Cannot revoke rights in OrgB
    ↓
    Each organization's data completely isolated
```

---

## 8. Complete User Journey: New OrgAdmin

```
Day 1: Invitation
┌─────────────────────────────────────────┐
│ SuperAdmin creates OrgAdmin invitation  │
│ • Uses InviteUser page                  │
│ • Selects "Organization Administrator" │
│ • Fills in email, name, agent types     │
│ • Submits form                          │
│                                         │
│ Backend:                                │
│ • User created with role = OrgAdmin     │
│ • Azure AD user created with OrgAdmin   │
│   app role                              │
│ • Invitation email sent                 │
│                                         │
│ Result: ✅ Invitation pending           │
│         [Org Admin] badge in list       │
└─────────────────────────────────────────┘
         ↓
Day 2: User Accepts Invitation
┌─────────────────────────────────────────┐
│ User clicks link in email               │
│ • Accepts B2B invitation                │
│ • User profile created in Azure AD      │
│                                         │
│ Backend:                                │
│ • User status updated to "Active"       │
│ • User can now log in                   │
│                                         │
│ Result: ✅ Active OrgAdmin              │
└─────────────────────────────────────────┘
         ↓
Day 3: User Logs In
┌─────────────────────────────────────────┐
│ OrgAdmin logs into system               │
│                                         │
│ Backend:                                │
│ • Verifies Azure AD role = OrgAdmin     │
│ • Verifies database role = OrgAdmin     │
│ • Role check passed                     │
│                                         │
│ Result: ✅ Full OrgAdmin access         │
│         • Can invite users              │
│         • Can manage agent types        │
│         • Can manage database access    │
│         • Can view user list            │
└─────────────────────────────────────────┘
         ↓
Day 4: SuperAdmin Revokes Rights
┌─────────────────────────────────────────┐
│ SuperAdmin finds OrgAdmin in list       │
│ • Clicks (⋮) menu                       │
│ • Clicks "🚫 Revoke Admin Rights"       │
│ • Sees comprehensive warning            │
│ • Confirms revocation                   │
│                                         │
│ Backend:                                │
│ • Role updated to User                  │
│ • Azure AD role changed to OrgUser      │
│ • Database updated                      │
│                                         │
│ Result: ✅ Rights revoked immediately   │
│         User loses admin capabilities   │
└─────────────────────────────────────────┘
         ↓
Day 5: Former OrgAdmin Logs In
┌─────────────────────────────────────────┐
│ User logs in again                      │
│                                         │
│ Backend:                                │
│ • Verifies Azure AD role = OrgUser      │
│ • Verifies database role = User         │
│ • Role check passed                     │
│                                         │
│ Result: ✅ Standard user access         │
│         • Cannot invite users           │
│         • Cannot manage roles           │
│         • Can only view assigned data   │
└─────────────────────────────────────────┘
```

---

## 9. Error Scenarios

### Scenario 1: Invalid Role Assignment

```
User attempts: SuperAdmin → Assign Developer role
         ↓
    Policy validation fails
         ↓
    ❌ Error Displayed:
    "Security Policy Violation:
     SuperAdmins cannot assign Developer role.
     Only Organization Administrator and
     Standard User roles are allowed."
         ↓
    Log Entry:
    [ERROR] SECURITY: SuperAdmin user@company.com
            attempted to assign forbidden role Developer
            to target user target@company.com
         ↓
    Result: Operation cancelled, no changes made
```

### Scenario 2: Azure AD Sync Failure

```
Database update successful
Azure AD sync fails
         ↓
    ⚠️ Warning Displayed:
    "Failed to update app role in Azure Entra ID
     for user@company.com, but database was updated"
         ↓
    Log Entry:
    [WARNING] Error updating Azure Entra ID app roles
              for user@company.com
              (Exception details...)
         ↓
    Result: Database persists as source of truth
            User functions with new role
            Azure AD may need manual reconciliation
```

### Scenario 3: Self-Promotion Attempt

```
SuperAdmin tries to promote themselves
         ↓
    Action not visible in UI
    (IsCurrentUser check at UI level)
         ↓
    If somehow triggered:
    ❌ Error: "You cannot change your own role.
              Please contact another administrator."
         ↓
    Log Entry:
    [WARNING] User user@company.com attempted
              to promote themselves - blocked
              for security
         ↓
    Result: Operation blocked, no changes made
```

---

## 10. Security Validation Layers

```
Role Change Request
         ↓
    ┌─────────────────────────────┐
    │ Layer 1: UI Authorization   │
    │ • Check current user role   │
    │ • Check target user role    │
    │ • Check if self             │
    │ • Show/hide actions         │
    └─────────────────────────────┘
         ↓
    ┌─────────────────────────────┐
    │ Layer 2: Confirmation Modal │
    │ • Require explicit action   │
    │ • Show consequences         │
    │ • Prevent accidental change │
    └─────────────────────────────┘
         ↓
    ┌─────────────────────────────┐
    │ Layer 3: Backend Validation │
    │ • Tenant isolation check    │
    │ • Role assignment policy    │
    │ • Self-modification check   │
    │ • Organization access check │
    └─────────────────────────────┘
         ↓
    ┌─────────────────────────────┐
    │ Layer 4: Data Changes       │
    │ • Database update (primary) │
    │ • Azure AD sync (secondary) │
    │ • Audit logging             │
    └─────────────────────────────┘
         ↓
    ✅ Or ❌ Multi-layer validated
```

---

## 11. Database-First Consistency Model

```
Role Change Request
         ↓
    Database Update
    (PRIMARY)
    ├─ Is database update successful?
    │  ├─ YES
    │  │  ↓
    │  │ Continue to Azure AD
    │  │  ↓
    │  │ Azure AD Update
    │  │ (SECONDARY)
    │  │  ├─ Success?
    │  │  │  ├─ YES → ✅ Complete success
    │  │  │  └─ NO → ⚠️ DB OK, Azure AD failed
    │  │  └─ Result: Database is source of truth
    │  │
    │  └─ NO
    │     ↓
    │     ❌ Operation fails
    │     Database unchanged
    │     Azure AD NOT called
         ↓
    Key Principle:
    "Database is ALWAYS source of truth.
     Azure AD sync is best-effort.
     If Azure AD fails, database persists.
     If database fails, nothing happens."
```

---

## 12. Audit Trail

```
Every role change produces logs:

DATABASE UPDATE LOGS:
✅ "Successfully updated user {Email} role
    from {OldRole} to {NewRole}"

AZURE AD SYNC LOGS:
✅ "Successfully removed app role {OldRole}
    from user {AzureObjectId}"
✅ "Successfully assigned app role {NewRole}
    to user {AzureObjectId}"
⚠️ "Failed to update app role in Azure AD
    for {Email}, but database was updated"

SECURITY EVENT LOGS:
❌ "SECURITY: SuperAdmin {CurrentUser} attempted
    to assign forbidden role {ForbiddenRole}
    to user {TargetUser}"
⚠️ "User {Email} attempted to change their
    own role - blocked for security"
```

