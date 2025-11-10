# Testing Guide: SuperAdmin Role Management Features

## Overview
This guide provides step-by-step instructions for testing the new SuperAdmin Role Management features including OrgAdmin invitations, user promotion/demotion, and admin rights revocation with strict role assignment restrictions.

---

## UI Elements Location

### 1. InviteUser Page - Role Selector Dropdown
**URL:** `/admin/invite-user`
**Location:** Between "Display Name" field and "Agent Types" section
**File:** `Components/Pages/Admin/InviteUser.razor` (Lines 277-302)

**Visual Elements:**
- **Label:** 👑 "User Role" (with crown icon in warning color)
- **Required Indicator:** Red asterisk (*)
- **Container:** Light gray boxed section with rounded borders (bg-light, border rounded)
- **Dropdown Options:**
  - "-- Select Role --" (placeholder)
  - "👤 Standard User (Read-Only Access)"
  - "👨‍💼 Organization Administrator (Can Manage Users & Database Access)"
- **Help Text:** Shows role capabilities for each option

**Visibility Rule:**
- ✅ **VISIBLE** for: SuperAdmin, Developer roles
- ❌ **HIDDEN** for: OrgAdmin, User roles

**Form Validation:**
- Role selection is **REQUIRED** for SuperAdmin/Developer users
- Form won't submit until role is selected
- Error message: "User Role is required"

---

### 2. ManageUsers Page - Promotion Action
**URL:** `/admin/users`
**Location:** Action dropdown menu (three dots icon) on User role rows
**File:** `Components/Pages/Admin/ManageUsers.razor` (Lines 1294-1303)

**Visual Elements:**
- **Action Title:** "👨‍💼 Promote to OrgAdmin"
- **Icon:** fas fa-crown (crown icon in warning color)
- **Action Type:** Warning (orange/yellow styling)
- **Description:** "Upgrade user to Organization Administrator role"
- **Location in dropdown:** After status actions, before deactivation actions

**When Visible:**
- ✅ User role is "User" (not OrgAdmin or Developer/SuperAdmin)
- ✅ User status is "Active"
- ✅ User is NOT the current user (self-promotion prevented)
- ✅ Current user is SuperAdmin or Developer

**Clicking Action:**
- Opens confirmation modal with detailed information
- Shows consequences of promotion

---

### 3. ManageUsers Page - Revocation Action
**URL:** `/admin/users`
**Location:** Action dropdown menu (three dots icon) on OrgAdmin role rows
**File:** `Components/Pages/Admin/ManageUsers.razor` (Lines 1307-1317)

**Visual Elements:**
- **Action Title:** "🚫 Revoke Admin Rights"
- **Icon:** fas fa-ban (ban icon in danger color)
- **Action Type:** Danger (red styling) - **NEW: More prominent than before**
- **Description:** "Remove administrator privileges and revert user to standard User role"
- **Location in dropdown:** After status actions, replaces old "Demote to User"

**When Visible:**
- ✅ User role is "OrgAdmin"
- ✅ User is NOT the current user (self-revocation prevented)
- ✅ Current user is SuperAdmin or Developer

**Clicking Action:**
- Opens comprehensive confirmation modal
- Shows permanent revocation warning (red alert)
- Lists all permissions being revoked

---

## Test Scenarios

### Test Scenario 1: SuperAdmin Invites New User as OrgAdmin

**Prerequisites:**
- You are logged in as a SuperAdmin user
- Have at least one organization in the system
- Have agent types configured

**Steps:**

1. **Navigate to Invite User page**
   - Go to: `/admin/invite-user`
   - Or: Click "Invite User" in admin navigation menu

2. **Verify Role Selector is Visible**
   - ✅ Look for "User Role" dropdown between "Display Name" and "Agent Types"
   - ✅ Should be in a light gray boxed section
   - ✅ Should have 👑 crown icon
   - ✅ Should show role capabilities help text

3. **Fill in User Details**
   - **Display Name:** "John Admin"
   - **Email:** "john.admin@company.com"
   - **Agent Types:** Select at least one
   - **Database Access:** Select at least one database

4. **Select Role as OrgAdmin**
   - Click the "User Role" dropdown
   - Select "👨‍💼 Organization Administrator (Can Manage Users & Database Access)"
   - Note the description updates to show admin capabilities

5. **Submit Invitation**
   - Click "Send Invitation" button
   - ✅ Form should accept the submission (was blocked before role selection)
   - ✅ Success message should include role info: "Role: Organization Administrator"
   - ✅ User should be redirected to user list

6. **Verify User Created with OrgAdmin Role**
   - Go to `/admin/users`
   - Find "John Admin" in the user list
   - ✅ Role column should show: "Organization Administrator" (orange badge)
   - ✅ User should appear in the list immediately

**Expected Behavior:**
- ✅ User successfully created with OrgAdmin role
- ✅ User receives invitation email
- ✅ User can manage other users and roles (verify in ManageUsers page)

---

### Test Scenario 2: SuperAdmin Promotes Existing User to OrgAdmin

**Prerequisites:**
- You are logged in as a SuperAdmin
- You have at least one "User" role user in the organization
- User must have "Active" status

**Steps:**

1. **Navigate to Manage Users page**
   - Go to: `/admin/users`
   - Or: Click "Manage Users" in admin navigation menu

2. **Find an Active User with "User" Role**
   - Look for a user with role badge "User" (info/blue color)
   - Make sure status shows "Active"
   - Verify it's NOT yourself (self-promotion is blocked)

3. **Open User Actions Dropdown**
   - Click the three-dots menu (⋮) icon at the end of the user row
   - ✅ Should see "👨‍💼 Promote to OrgAdmin" action in orange/yellow

4. **Click Promote Action**
   - Click "👨‍💼 Promote to OrgAdmin"
   - Modal should open with title "👨‍💼 Promote User to Organization Administrator"

5. **Review Confirmation Dialog**
   - ✅ Should show user details (name, email, organization)
   - ✅ "Current Role:" should show "Standard User"
   - ✅ "New Role:" should show "Organization Administrator"
   - ✅ Should show orange info alert about permissions being granted
   - ✅ Should show yellow warning about role management policy
   - ✅ Policy warning should explain:
     - SuperAdmins can ONLY assign OrgAdmin/User roles
     - Cannot assign Developer/SuperAdmin roles
     - Cannot modify own role

6. **Confirm Promotion**
   - Click "Promote to OrgAdmin" button
   - ✅ Dialog closes
   - ✅ Success message appears: "✅ Role updated successfully for [User]. Changed from User to Organization Administrator."
   - ✅ User list refreshes automatically

7. **Verify Role Changed**
   - ✅ User's role badge should now show "Organization Administrator" (orange)
   - ✅ User should now have promotion/revocation options available in dropdown (when clicked by admin)

**Expected Behavior:**
- ✅ User promoted successfully in database
- ✅ User's Azure AD app role updated to "OrgAdmin"
- ✅ User can now manage other users and invite new users

---

### Test Scenario 3: SuperAdmin Revokes Admin Rights from OrgAdmin

**Prerequisites:**
- You are logged in as a SuperAdmin
- You have at least one OrgAdmin user in the organization
- OrgAdmin must NOT be yourself (self-revocation is blocked)

**Steps:**

1. **Navigate to Manage Users page**
   - Go to: `/admin/users`

2. **Find an OrgAdmin User**
   - Look for a user with role badge "Organization Administrator" (orange)
   - Make sure it's NOT yourself

3. **Open User Actions Dropdown**
   - Click the three-dots menu (⋮) icon at the end of the user row
   - ✅ Should see "🚫 Revoke Admin Rights" action in red/danger color
   - ✅ Should NOT see "Promote to OrgAdmin" (only revoke is available for OrgAdmins)

4. **Click Revoke Admin Rights Action**
   - Click "🚫 Revoke Admin Rights"
   - Modal should open with title "🚫 Revoke Administrator Rights"

5. **Review Comprehensive Confirmation Dialog**
   - ✅ Should show user details (name, email, organization)
   - ✅ "Current Role:" should show "Organization Administrator"
   - ✅ "New Role:" should show "Standard User"
   - ✅ Should show RED danger alert (alert-danger) emphasizing PERMANENT revocation
   - ✅ Alert should list all permissions being PERMANENTLY REVOKED:
     - Permission to INVITE NEW USERS
     - Permission to MANAGE USERS AND ROLES
     - Permission to MANAGE AGENT TYPES for other users
     - Permission to MANAGE DATABASE ACCESS for other users
     - Update user's role in AZURE ENTRA ID immediately
     - User will retain existing AGENT TYPE ASSIGNMENTS and DATABASE ACCESS

6. **Confirm Revocation**
   - Click "Revoke Admin Rights" button
   - ✅ Dialog closes
   - ✅ Success message appears: "✅ Role updated successfully for [User]. Changed from Organization Administrator to User."
   - ✅ User list refreshes automatically

7. **Verify Admin Rights Revoked**
   - ✅ User's role badge should now show "User" (info/blue color)
   - ✅ When you open that user's dropdown again, only status actions appear (no promotion/revocation)
   - ✅ User can NO LONGER manage other users or invite new users

**Expected Behavior:**
- ✅ Admin rights successfully revoked in database
- ✅ User's Azure AD app role updated to "OrgUser"
- ✅ User loses all organizational administrative permissions

---

### Test Scenario 4: Prevent Self-Promotion/Self-Revocation

**Prerequisites:**
- You are logged in as a SuperAdmin
- Your user account is in the user list

**Steps:**

1. **Navigate to Manage Users page**
   - Go to: `/admin/users`

2. **Find Your Own User Entry**
   - Look for your email/name in the user list
   - Your role should show "Super Admin" or "Organization Administrator"

3. **Try to Open Actions Dropdown**
   - Click the three-dots menu (⋮) icon on your own row
   - ✅ You should see user management actions (deactivate, etc.)
   - ✅ You should NOT see "Promote to OrgAdmin" or "Revoke Admin Rights" (self-protection)

4. **Attempt Self-Promotion (Try to Bypass)**
   - If somehow the action was visible, click it
   - ✅ Should immediately show error: "❌ Security Policy: You cannot change your own role. Please contact another administrator."

5. **Verify Self-Protection Works**
   - ✅ You cannot promote yourself to higher role
   - ✅ You cannot revoke your own admin rights
   - ✅ Action buttons are not visible for yourself (UI-level prevention)

**Expected Behavior:**
- ✅ Self-modification actions are completely blocked
- ✅ Clear error message if somehow attempted
- ✅ Security logged as warning event

---

### Test Scenario 5: Role Assignment Policy Restrictions

**Prerequisites:**
- You are logged in as a SuperAdmin
- Developer role exists in system (if testing Developer assignments)

**Steps:**

1. **Verify SuperAdmin Cannot Assign Developer Role**
   - Navigate to: `/admin/invite-user`
   - Check the role dropdown
   - ✅ Should only show:
     - "Standard User"
     - "Organization Administrator"
   - ✅ Should NOT show "Developer" role

2. **Verify SuperAdmin Cannot Assign SuperAdmin Role**
   - Same location as above
   - ✅ Should NOT show "SuperAdmin" option
   - ✅ Only OrgAdmin/User roles available

3. **Test Backend Policy Validation (Advanced)**
   - Try to directly call API or use browser developer tools to bypass UI
   - Attempt to assign Developer role to a user via form interception
   - ✅ Should get error: "❌ Security Policy Violation: SuperAdmins cannot assign Developer role. Only Organization Administrator and Standard User roles are allowed."
   - ✅ Should see in browser console/network: Security log with SuperAdmin email and attempted role

4. **Verify Policy Message in Promotion Dialog**
   - Go to: `/admin/users`
   - Find any User to promote
   - Click "Promote to OrgAdmin"
   - In the confirmation dialog, scroll down to yellow warning
   - ✅ Should see:
     ```
     ⚠️ Role Management Policy:
     • SuperAdmins can ONLY assign Organization Administrator or Standard User roles
     • Cannot assign Developer or SuperAdmin roles to other users
     • Cannot modify their own role - requires another administrator
     Ensure this promotion is intentional and necessary.
     ```

**Expected Behavior:**
- ✅ UI prevents access to forbidden roles
- ✅ Backend validates and blocks forbidden role assignments
- ✅ Security policy clearly communicated to SuperAdmin
- ✅ Violations logged for security audit

---

### Test Scenario 6: Database and Azure AD Consistency

**Prerequisites:**
- You are logged in as a SuperAdmin
- Have network access to verify Azure AD changes
- Have admin rights to Azure AD portal (optional, for verification)

**Steps:**

1. **Promote User to OrgAdmin**
   - Go to: `/admin/users`
   - Find a User and promote to OrgAdmin
   - Confirm the action

2. **Verify Database Update**
   - ✅ User appears with new role immediately in user list
   - ✅ Role badge shows "Organization Administrator"

3. **Verify Azure AD Update (Optional but Recommended)**
   - Go to Azure AD admin portal
   - Find the promoted user
   - Check their Enterprise Applications > AdminConsole
   - ✅ App role should be updated to "OrgAdmin"

4. **Test Azure AD Sync Failure Handling**
   - (This requires controlled testing or network interruption)
   - Promote another user
   - If Azure AD sync fails:
     - ✅ Database should still be updated (database is source of truth)
     - ✅ Warning message shown: "Failed to update app role in Azure Entra ID for [User], but database was updated"
     - ✅ User can still function with new role

**Expected Behavior:**
- ✅ Both database and Azure AD updated consistently
- ✅ If one fails, database persists as source of truth
- ✅ No orphaned role states

---

### Test Scenario 7: Tenant Isolation Validation

**Prerequisites:**
- Multiple organizations in system
- You are OrgAdmin of Organization A
- A different organization B exists
- You have SuperAdmin rights for testing

**Steps:**

1. **Verify Tenant Isolation on User List**
   - Log in as SuperAdmin
   - Go to Organization A users: `/admin/users?org=OrgAId`
   - ✅ Should only see Organization A users

2. **Verify Cannot Promote Users from Different Organization**
   - (This requires direct URL manipulation or testing at lower levels)
   - Try to promote a user from Organization B while managing Organization A
   - ✅ Should be blocked by tenant validation
   - ✅ Error message should indicate organization mismatch

3. **Verify Role Updates Respect Organization Scope**
   - Promote user in Organization A
   - ✅ User in Organization B unchanged
   - ✅ Azure AD updates respect organization boundaries

**Expected Behavior:**
- ✅ Tenant isolation enforced on all operations
- ✅ Cannot cross-organization role modifications
- ✅ Each organization's users remain isolated

---

## Non-Visible UI Elements (Hidden Based on Role/Status)

### Elements Hidden for Non-SuperAdmin Users

1. **InviteUser Page - Role Selector**
   - Hidden for: OrgAdmin, User, Developer roles
   - Only visible for: SuperAdmin, Developer

2. **ManageUsers Page - Promotion Action**
   - Hidden for: Regular User users
   - Only visible for: SuperAdmin, Developer managing User role users

3. **ManageUsers Page - Revocation Action**
   - Hidden for: Regular User users
   - Only visible for: SuperAdmin, Developer managing OrgAdmin role users

### Elements Hidden Based on User Status

1. **Promotion Action**
   - Hidden if user status is: Disabled, Inactive, Revoked
   - Only visible if user status is: Active

2. **Revocation Action**
   - Always visible for OrgAdmin users regardless of status
   - (But self-revocation still blocked if user is self)

---

## Troubleshooting

### Issue: Role Selector Not Visible in InviteUser

**Possible Causes:**
- Not logged in as SuperAdmin or Developer
- Browser cache issue
- Page not fully loaded

**Solution:**
- Verify you're logged in as SuperAdmin
- Clear browser cache (Ctrl+F5 or Cmd+Shift+R)
- Check browser developer console for JavaScript errors

---

### Issue: "Promote to OrgAdmin" Action Not Showing

**Possible Causes:**
- User already has OrgAdmin role
- User status is not "Active"
- User is yourself (self-protection)
- Not logged in as SuperAdmin/Developer

**Solution:**
- Select a different User role user
- Verify user status shows "Active"
- Use a different admin account to promote yourself
- Check login credentials

---

### Issue: "Revoke Admin Rights" Shows But Disabled

**Possible Causes:**
- Page is loading (`isLoading` flag is true)
- User is yourself (button visually disabled)

**Solution:**
- Wait for page to finish loading
- Use a different admin to revoke your own rights

---

### Issue: Success Message Shows But Role Not Changed

**Possible Causes:**
- Page cache not refreshed
- Database transaction failed silently
- Network latency

**Solution:**
- Manually refresh page (F5)
- Check browser console for errors
- Check application logs for database errors
- Try the operation again

---

## Manual Testing Checklist

- [ ] **Test 1:** Invite new user as OrgAdmin
  - [ ] Role selector visible for SuperAdmin
  - [ ] Role selector hidden for non-SuperAdmin
  - [ ] OrgAdmin option selectable
  - [ ] Success message shows role
  - [ ] User created with correct role

- [ ] **Test 2:** Promote User to OrgAdmin
  - [ ] Action visible for SuperAdmin
  - [ ] Action visible only for User role users
  - [ ] Confirmation dialog shows correct details
  - [ ] Policy warning visible in dialog
  - [ ] User promoted successfully
  - [ ] Role badge updated immediately

- [ ] **Test 3:** Revoke admin rights
  - [ ] Action visible for OrgAdmin users
  - [ ] Action shows as red (Danger)
  - [ ] Confirmation dialog shows PERMANENT warning
  - [ ] Alert shows all revoked permissions
  - [ ] User revoked successfully
  - [ ] User can no longer manage others

- [ ] **Test 4:** Self-protection
  - [ ] Cannot promote yourself
  - [ ] Cannot revoke your own rights
  - [ ] Error message shows for self-attempts

- [ ] **Test 5:** Policy restrictions
  - [ ] Developer role not in dropdown
  - [ ] SuperAdmin role not in dropdown
  - [ ] Policy warning shown in promotions
  - [ ] Only OrgAdmin/User roles assignable

- [ ] **Test 6:** UI Elements
  - [ ] Role selector in correct location
  - [ ] Crown icon visible in role label
  - [ ] Promotion action shows orange icon
  - [ ] Revocation action shows red ban icon
  - [ ] Dropdown menus appear correctly
  - [ ] Help text displays properly

- [ ] **Test 7:** Database consistency
  - [ ] User list updates after changes
  - [ ] Role badges update correctly
  - [ ] No stale data displayed

---

## Automated Testing (For Development)

Run these test scenarios through xUnit integration tests (future implementation):

```csharp
[Test]
public async Task PromoteUserToOrgAdmin_UpdatesDatabase_AndAzureAD()
{
    // Arrange
    var user = new OnboardedUser { Role = UserRole.User };
    var superAdmin = new OnboardedUser { Role = UserRole.SuperAdmin };

    // Act
    var result = await _userService.UpdateUserRoleAsync(user.Email, orgId, UserRole.OrgAdmin);

    // Assert
    Assert.IsTrue(result);
    Assert.AreEqual(UserRole.OrgAdmin, user.Role);
    // Verify Azure AD was called
}

[Test]
public async Task SuperAdmin_CannotAssignDeveloperRole_ThrowsException()
{
    // Test that policy validation works
}

[Test]
public async Task SelfPromotion_IsBlocked_ReturnsError()
{
    // Test self-protection
}
```

---

## Production Deployment Notes

1. **Monitor Logs for Role Changes**
   - Watch application logs for role assignment attempts
   - Alert on security policy violations

2. **Verify Azure AD Sync**
   - Confirm all promoted users appear with correct roles in Azure AD
   - Check Graph API permissions are correct

3. **Test Rollback Plan**
   - If issues arise, have process to revert roles manually
   - Maintain database backups

4. **User Communication**
   - Inform OrgAdmins about the revocation capability
   - Explain role restrictions to SuperAdmins
   - Provide training on new UI elements

---

## Questions?

Refer to:
- Implementation details: See `bugfix.md`
- Code location: See `CLAUDE.md`
- Architecture: See `CLAUDE.md` architecture sections
