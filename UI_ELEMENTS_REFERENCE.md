# UI Elements Reference - SuperAdmin Role Management

## Quick Location Guide

### 1. InviteUser Page - Role Selector
**File:** `Components/Pages/Admin/InviteUser.razor` (Lines 277-302)
**URL:** `/admin/invite-user`
**Position:** Between "Display Name" field and "Agent Types" section

```
┌─────────────────────────────────────────────────────────────┐
│ Display Name Input Field                                     │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│ ┌──────────────────────────────────────────────────────┐   │
│ │ 👑 User Role *                                        │   │  ← NEW: Role Selector
│ │ ┌──────────────────────────────────────────────────┐ │   │
│ │ │ -- Select Role --                                │ │   │
│ │ │ 👤 Standard User (Read-Only Access)              │ │   │
│ │ │ 👨‍💼 Organization Administrator (Can Manage...)    │ │   │
│ │ └──────────────────────────────────────────────────┘ │   │
│ │                                                       │   │
│ │ Role Capabilities:                                  │   │
│ │ • Standard User: Can view assigned resources...     │   │
│ │ • Organization Admin: Can invite users, manage...   │   │
│ └──────────────────────────────────────────────────────┘   │
│                                                              │
├─────────────────────────────────────────────────────────────┤
│ Agent Types Input                                            │
```

**Styling:**
- Background: Light gray (bg-light)
- Border: Rounded corners (border rounded)
- Padding: Medium (p-3)
- Label Icon: 👑 Crown (text-warning color = orange/yellow)
- Required Indicator: Red asterisk (*)

**Visibility:**
```javascript
@if (currentUserRole == UserRole.SuperAdmin || currentUserRole == UserRole.Developer)
{
    // Role selector shown
}
// Otherwise: NOT DISPLAYED
```

---

## 2. ManageUsers Page - Promotion Action

**File:** `Components/Pages/Admin/ManageUsers.razor` (Lines 1294-1303)
**URL:** `/admin/users`
**Location:** Action dropdown menu (⋮) on user rows

### User List Row Example:
```
┌────────────┬─────────────┬──────────────────────┬────────────┬───────┐
│ Name       │ Email       │ Role                 │ Status     │ (⋮)   │
├────────────┼─────────────┼──────────────────────┼────────────┼───────┤
│ John Doe   │ john@co.com │ [User] (blue badge)  │ Active     │  ⋮    │ ← Click here
└────────────┴─────────────┴──────────────────────┴────────────┴───────┘
```

### Dropdown Menu When User Role = "User":
```
┌─────────────────────────────────────────────────────┐
│ ✅ Deactivate Access                                │
│ (other status actions...)                           │
│ ─────────────────────────────────────────────────────│
│ 👨‍💼 Promote to OrgAdmin         ← NEW ACTION          │
│    Upgrade user to Organization Administrator role   │
└─────────────────────────────────────────────────────┘
```

**Action Properties:**
- Title: "👨‍💼 Promote to OrgAdmin"
- Icon: fas fa-crown
- Color/Type: Warning (orange/yellow)
- Description: "Upgrade user to Organization Administrator role"

**Visibility Conditions:**
```csharp
if (userRole == UserRole.User && currentStatus == "Active")
{
    // Show: Promote to OrgAdmin
}
```

**When Clicked:**
- Opens modal with title: "👨‍💼 Promote User to Organization Administrator"
- Shows user details
- Shows current role (Standard User) and new role (OrgAdmin)
- Shows consequences in orange alert box
- Shows role management policy in yellow alert box
- Provides "Promote to OrgAdmin" and "Cancel" buttons

---

## 3. ManageUsers Page - Revocation Action

**File:** `Components/Pages/Admin/ManageUsers.razor` (Lines 1307-1317)
**URL:** `/admin/users`
**Location:** Action dropdown menu (⋮) on user rows

### User List Row Example:
```
┌────────────┬─────────────┬──────────────────────┬────────────┬───────┐
│ Name       │ Email       │ Role                 │ Status     │ (⋮)   │
├────────────┼─────────────┼──────────────────────┼────────────┼───────┤
│ Jane Smith │ jane@co.com │ [Org Admin] (orange) │ Active     │  ⋮    │ ← Click here
└────────────┴─────────────┴──────────────────────┴────────────┴───────┘
```

### Dropdown Menu When User Role = "OrgAdmin":
```
┌─────────────────────────────────────────────────────┐
│ ✅ Deactivate Access                                │
│ (other status actions...)                           │
│ ─────────────────────────────────────────────────────│
│ 🚫 Revoke Admin Rights          ← NEW ACTION (RED)  │
│    Remove administrator privileges and revert...     │
└─────────────────────────────────────────────────────┘
```

**Action Properties:**
- Title: "🚫 Revoke Admin Rights"
- Icon: fas fa-ban
- Color/Type: Danger (red) - **NEW: More prominent than before**
- Description: "Remove administrator privileges and revert user to standard User role"

**Visibility Conditions:**
```csharp
else if (userRole == UserRole.OrgAdmin)
{
    // Show: Revoke Admin Rights
}
```

**When Clicked:**
- Opens modal with title: "🚫 Revoke Administrator Rights"
- Shows user details
- Shows current role (Organization Administrator) and new role (Standard User)
- Shows consequences in RED DANGER alert box (alert-danger)
- Lists all permissions being PERMANENTLY revoked
- Provides "Revoke Admin Rights" and "Cancel" buttons

---

## Modal Dialogs

### Promotion Confirmation Modal

```
╔══════════════════════════════════════════════════════════════╗
║            👨‍💼 Promote User to Organization Administrator      ║
╠══════════════════════════════════════════════════════════════╣
║                                                              ║
║ You are about to PROMOTE John Doe (john@co.com) to          ║
║ Organization Administrator role.                            ║
║                                                              ║
║ User:            John Doe                                   ║
║ Email:           john@co.com                                ║
║ Current Role:    Standard User                              ║
║ New Role:        Organization Administrator                 ║
║ Organization:    Acme Corp                                  ║
║                                                              ║
║ ┌────────────────────────────────────────────────────────┐  ║
║ │ ℹ️ This action will:                                   │  ║
║ │ • Grant user permission to INVITE NEW USERS...        │  ║
║ │ • Allow user to MANAGE AGENT TYPES...                 │  ║
║ │ • Allow user to MANAGE DATABASE ACCESS...             │  ║
║ │ • Update user's role in AZURE ENTRA ID                │  ║
║ └────────────────────────────────────────────────────────┘  ║
║                                                              ║
║ ┌────────────────────────────────────────────────────────┐  ║
║ │ ⚠️ Role Management Policy:                             │  ║
║ │ • SuperAdmins can ONLY assign Organization...         │  ║
║ │ • Cannot assign Developer or SuperAdmin roles         │  ║
║ │ • Cannot modify their own role                        │  ║
║ │ Ensure this promotion is intentional and necessary.    │  ║
║ └────────────────────────────────────────────────────────┘  ║
║                                                              ║
║          [ Cancel ]  [ Promote to OrgAdmin ]                ║
╚══════════════════════════════════════════════════════════════╝
```

**Color Scheme:**
- Info Alert: Blue (bg-info) - showing actions to be taken
- Warning Alert: Yellow (bg-warning) - showing policy restrictions

---

### Revocation Confirmation Modal

```
╔══════════════════════════════════════════════════════════════╗
║          🚫 Revoke Administrator Rights                      ║
╠══════════════════════════════════════════════════════════════╣
║                                                              ║
║ You are about to REVOKE ADMINISTRATOR RIGHTS from            ║
║ Jane Smith (jane@co.com).                                    ║
║                                                              ║
║ User:            Jane Smith                                  ║
║ Email:           jane@co.com                                 ║
║ Current Role:    Organization Administrator                  ║
║ New Role:        Standard User                               ║
║ Organization:    Acme Corp                                   ║
║                                                              ║
║ ┌────────────────────────────────────────────────────────┐  ║
║ │ ⚠️ This action will PERMANENTLY REVOKE:               │  ║
║ │ • Permission to INVITE NEW USERS                      │  ║
║ │ • Permission to MANAGE USERS AND ROLES                │  ║
║ │ • Permission to MANAGE AGENT TYPES for other users    │  ║
║ │ • Permission to MANAGE DATABASE ACCESS for other...   │  ║
║ │ • Update user's role in AZURE ENTRA ID immediately    │  ║
║ │ • User will retain existing AGENT TYPE ASSIGNMENTS    │  ║
║ │   and DATABASE ACCESS as a standard user              │  ║
║ └────────────────────────────────────────────────────────┘  ║
║                                                              ║
║          [ Cancel ]  [ Revoke Admin Rights ]                ║
╚══════════════════════════════════════════════════════════════╝
```

**Color Scheme:**
- Danger Alert: Red (alert-danger) - showing PERMANENT consequences
- Font Weight: Bold on "PERMANENTLY REVOKE"

---

## Badge Styling

### Role Badges in User List

```
┌─────────────────────────────────────────┐
│ Role Column in User List                │
├─────────────────────────────────────────┤
│                                         │
│ [Developer]        ← bg-success (green) │
│ [Super Admin]      ← bg-danger (red)    │
│ [Org Admin]        ← bg-warning (orange)│ ← NEW OrgAdmin option
│ [User]             ← bg-info (blue)     │
│                                         │
└─────────────────────────────────────────┘
```

---

## Icons Used

| Icon | Meaning | Color | Usage |
|------|---------|-------|-------|
| 👑 | Admin Role | Warning (orange) | Role label in InviteUser |
| 👤 | Standard User | Info (blue) | Standard User option |
| 👨‍💼 | Organization Admin | Warning (orange) | OrgAdmin option, Promotion action |
| 🚫 | Revoke/Ban | Danger (red) | Revocation action |
| ℹ️ | Information | Info (blue) | Info alerts |
| ⚠️ | Warning | Warning (yellow) | Warning alerts |
| ✅ | Success | Success (green) | Success messages |

---

## Form Validation Messages

### InviteUser Page

**Before Role Selection:**
```
Form is INVALID - "Send Invitation" button is DISABLED
Reason: "User Role is required for SuperAdmin/Developer users"
```

**After Role Selection:**
```
Form is VALID - "Send Invitation" button is ENABLED
All required fields filled in
```

---

### Promotion/Revocation

**Self-Attempt Error:**
```
Error Message (red alert):
"❌ Security Policy: You cannot change your own role.
 Please contact another administrator."
```

**Policy Violation Error:**
```
Error Message (red alert):
"❌ Security Policy Violation: SuperAdmins cannot assign Developer role.
 Only Organization Administrator and Standard User roles are allowed."
```

---

## Success Messages

### After Successful Invitation

```
✅ New user invited successfully for john.admin@company.com.
Role: Organization Administrator.
Agent types: [Selected types...].
Database access: [Selected databases...].

The user will receive an email with agent share links
and instructions to join your organization.

✅ Invitation sent! Redirecting to user list...
```

### After Successful Promotion

```
✅ Role updated successfully for John Doe.
Changed from User to Organization Administrator.
```

### After Successful Revocation

```
✅ Role updated successfully for Jane Smith.
Changed from Organization Administrator to User.
```

---

## Conditional Visibility

### SuperAdmin/Developer Login
```
InviteUser Page:
✅ Role selector VISIBLE

ManageUsers Page:
✅ "Promote to OrgAdmin" action VISIBLE for User role users
✅ "Revoke Admin Rights" action VISIBLE for OrgAdmin users
```

### OrgAdmin Login
```
InviteUser Page:
❌ Role selector NOT VISIBLE (regular invite only)

ManageUsers Page:
❌ "Promote to OrgAdmin" action NOT VISIBLE
❌ "Revoke Admin Rights" action NOT VISIBLE
```

### Regular User Login
```
InviteUser Page:
❌ Cannot access (authorization required)

ManageUsers Page:
❌ Cannot access (authorization required)
```

---

## Test Navigation Paths

### To Test Invitations:
```
URL: /admin/invite-user
Expected: Role selector visible (if SuperAdmin)
```

### To Test Promotions:
```
URL: /admin/users
Find: User with "User" role
Action: Click (⋮) → Click "👨‍💼 Promote to OrgAdmin"
Expected: Modal opens with all details
```

### To Test Revocations:
```
URL: /admin/users
Find: User with "Organization Administrator" role
Action: Click (⋮) → Click "🚫 Revoke Admin Rights"
Expected: Modal opens with danger warning
```

---

## Browser Developer Tools Inspection

### To Inspect Role Selector Element:
```javascript
// In browser console:
document.getElementById('userRole')  // Will show the dropdown
document.querySelector('[for="userRole"]')  // Will show the label
```

### To Check Visibility:
```javascript
// Check if element is displayed
const selector = document.getElementById('userRole');
console.log(window.getComputedStyle(selector).display);
// Output: "block" (visible) or "none" (hidden)
```

### To View Applied Classes:
```javascript
// Check Bootstrap classes applied
document.getElementById('userRole').className
// Output: "form-select"
```

---

## CSS Classes Reference

| Element | Bootstrap Classes | Purpose |
|---------|------------------|---------|
| Role Label | `form-label` | Standard form label styling |
| Role Dropdown | `form-select` | Bootstrap select styling |
| Container | `mb-3 p-3 border rounded bg-light` | Spacing, border, light background |
| Icon Label | `fas fa-crown text-warning` | Icon and orange warning color |
| Info Alert | `alert alert-info mb-3` | Blue information alert |
| Warning Alert | `alert alert-warning` | Yellow warning alert |
| Danger Alert | `alert alert-danger mb-3` | Red danger/critical alert |
| Help Text | `form-text` | Small gray help text |
| Success Message | With ✅ emoji | Green success indicator |
| Error Message | With ❌ emoji | Red error indicator |

---

## Responsive Design

### Mobile View (< 768px)
- Role selector maintains full width
- Dropdowns expand to full width
- Modals adapt to screen size
- All functionality remains accessible

### Tablet View (768px - 1024px)
- Comfortable spacing maintained
- Dropdowns responsive
- Modals readable

### Desktop View (> 1024px)
- Optimal layout
- Full feature visibility
- All elements properly aligned

---

## Accessibility Features

- ✅ Proper `<label for="">` associations
- ✅ Required field indicators (red asterisk)
- ✅ Screen reader friendly icons with text labels
- ✅ Keyboard navigable dropdowns
- ✅ Color-coded for meaning (not sole means of identification)
- ✅ Comprehensive error messages
- ✅ Focus states on interactive elements

