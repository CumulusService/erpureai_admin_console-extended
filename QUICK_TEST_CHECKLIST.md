# Quick Test Checklist - SuperAdmin Role Management

## Prerequisites
- [ ] SuperAdmin account created and logged in
- [ ] At least 2 active User accounts (different from SuperAdmin)
- [ ] Agent types configured
- [ ] Database access configured
- [ ] Application running at localhost:7192 (or your dev URL)

---

## Test 1: Role Selector Visibility ✅

### What to Check:
- [ ] Navigate to: `/admin/invite-user`
- [ ] Look between "Display Name" and "Agent Types" fields
- [ ] Should see: Light gray box with "👑 User Role" label

### Expected Result:
- ✅ Role selector visible for SuperAdmin
- ✅ Dropdown shows 2 options:
  - "👤 Standard User"
  - "👨‍💼 Organization Administrator"
- ✅ Help text shows role capabilities

### If NOT Visible:
- ❌ Check: Are you logged in as SuperAdmin?
- ❌ Check: Are you using OrgAdmin account? (role selector hidden)
- ❌ Browser issue: Clear cache and reload

---

## Test 2: Invite as OrgAdmin ✅

### What to Do:
1. Go to: `/admin/invite-user`
2. Fill in:
   - Display Name: "Test OrgAdmin"
   - Email: "testadmin@testcompany.com"
   - Select 1+ Agent Types
   - Select 1+ Databases
3. In Role dropdown: Select "👨‍💼 Organization Administrator"
4. Click "Send Invitation"

### Expected Result:
- ✅ Success message includes: "Role: Organization Administrator"
- ✅ Redirects to user list
- ✅ New user appears with "Organization Administrator" badge (orange)

### If Not Working:
- ❌ Role selector not visible? (Not logged in as SuperAdmin)
- ❌ Form won't submit? (Role must be selected)
- ❌ Wrong role shown? (Check form submission payload)

---

## Test 3: Promote User to OrgAdmin ✅

### Prerequisites:
- [ ] At least one User with "User" role exists
- [ ] That user has "Active" status
- [ ] That user is NOT yourself

### What to Do:
1. Go to: `/admin/users`
2. Find a user with blue "User" badge
3. Click the (⋮) three-dots menu at end of row
4. Look for: "👨‍💼 Promote to OrgAdmin" (orange)
5. Click that action
6. Review modal - should show:
   - Current Role: Standard User
   - New Role: Organization Administrator
   - Orange info alert
   - Yellow policy warning
7. Click "Promote to OrgAdmin" button
8. Wait for success message

### Expected Result:
- ✅ Modal opens with correct details
- ✅ Modal shows orange alert about permissions
- ✅ Modal shows yellow warning about policy
- ✅ After confirmation: "✅ Role updated successfully"
- ✅ User list refreshes
- ✅ User now has orange "Organization Administrator" badge

### If Not Working:
- ❌ Action not visible?
  - Check: Is user status "Active"?
  - Check: Does user have "User" role?
  - Check: Is user yourself? (self-promotion blocked)
- ❌ Wrong role badge after?
  - Refresh page (F5)
  - Check browser console for errors

---

## Test 4: Revoke Admin Rights ✅

### Prerequisites:
- [ ] At least one OrgAdmin exists (from Test 3)
- [ ] OrgAdmin is NOT yourself

### What to Do:
1. Go to: `/admin/users`
2. Find the user you just promoted (now has orange badge)
3. Click the (⋮) three-dots menu at end of row
4. Look for: "🚫 Revoke Admin Rights" (RED)
5. Click that action
6. Review modal - should show:
   - Current Role: Organization Administrator
   - New Role: Standard User
   - RED danger alert with "PERMANENTLY REVOKE"
   - List of all permissions being revoked
7. Click "Revoke Admin Rights" button
8. Wait for success message

### Expected Result:
- ✅ Action visible in RED (Danger type)
- ✅ Modal opens with critical warning
- ✅ Modal shows red alert (alert-danger class)
- ✅ After confirmation: "✅ Role updated successfully"
- ✅ User list refreshes
- ✅ User now has blue "User" badge
- ✅ User no longer has revocation action in dropdown

### If Not Working:
- ❌ Action not visible?
  - Check: Does user have "Organization Administrator" role?
  - Check: Is user yourself? (blocked)
- ❌ Modal looks wrong?
  - Should be RED (danger) not orange or blue
  - Should have comprehensive warning text
  - Refresh page and try again

---

## Test 5: Self-Protection ✅

### Prerequisites:
- [ ] You are logged in as SuperAdmin
- [ ] Your user account appears in the user list

### What to Do:
1. Go to: `/admin/users`
2. Find your own user entry (your email)
3. Scroll to the right and look for (⋮) menu
4. Click the three-dots menu
5. Look at available actions

### Expected Result:
- ✅ "Promote to OrgAdmin" action NOT visible
- ✅ "Revoke Admin Rights" action NOT visible
- ✅ You can see other actions (like deactivate)
- ✅ Self-protection working correctly

### If Not Working:
- ❌ Actions are visible?
  - This is a security issue!
  - Check: Is this actually yourself?
  - Verify email matches login
- ❌ Can click them?
  - Should show error: "You cannot change your own role"

---

## Test 6: Role Assignment Policy ✅

### What to Check:
1. Open: `/admin/invite-user`
2. Click role dropdown
3. Count available options:
   - Should see ONLY 2 options
   - Should NOT see "Developer"
   - Should NOT see "SuperAdmin"

### Expected Result:
- ✅ Only "Standard User" and "Organization Administrator" available
- ✅ No hidden "Developer" or "SuperAdmin" options

### Advanced Test (Policy Validation):
1. Open browser Developer Console (F12)
2. Go to: `/admin/users`
3. Promote a user (complete the action)
4. Check Console for logs mentioning role update
5. Should see: Success or security validation logs

### Expected Result:
- ✅ Logs show role update success
- ✅ No policy violation errors in console
- ✅ Backend accepted valid role assignment

---

## Test 7: Form Validation ✅

### What to Do:
1. Go to: `/admin/invite-user`
2. Fill in Display Name and Email
3. DON'T select any Agent Types
4. DON'T select any Databases
5. DON'T select Role
6. Try to click "Send Invitation"

### Expected Result:
- ✅ "Send Invitation" button disabled (grayed out)
- ✅ Form shows validation errors for missing fields
- ✅ When you select role: Button still disabled (other fields needed)
- ✅ Only when ALL required fields filled: Button enabled

### For SuperAdmin Specifically:
- ✅ If you don't select a role: Form invalid (even with other fields filled)
- ✅ Role selection is REQUIRED for SuperAdmin/Developer

---

## Test 8: UI Element Styling ✅

### Visual Checks:
1. **InviteUser Role Selector:**
   - [ ] Light gray background (bg-light)
   - [ ] Has rounded border (border-radius)
   - [ ] Has padding around content (p-3)
   - [ ] Crown icon is orange (👑 text-warning)
   - [ ] Required asterisk is red (*)
   - [ ] Help text is small and gray

2. **ManageUsers Promotion Action:**
   - [ ] Orange color (Warning type)
   - [ ] Has crown icon: 👨‍💼
   - [ ] Clear description text
   - [ ] Proper alignment in dropdown

3. **ManageUsers Revocation Action:**
   - [ ] RED color (Danger type)
   - [ ] Has ban icon: 🚫
   - [ ] Clear description text
   - [ ] More prominent than other actions

4. **Promotion Modal:**
   - [ ] Blue/orange alert for consequences
   - [ ] Yellow alert for policy warning
   - [ ] All text clearly readable
   - [ ] Buttons properly labeled and positioned

5. **Revocation Modal:**
   - [ ] RED/danger alert for consequences
   - [ ] Bold warning text
   - [ ] Clear list of permissions revoked
   - [ ] Buttons properly labeled in red

---

## Test 9: Success Messages ✅

### Invitation Success:
After inviting as OrgAdmin, look for:
```
✅ New user invited successfully for [email].
Role: Organization Administrator.
```

### Promotion Success:
After promoting, look for:
```
✅ Role updated successfully for [name].
Changed from User to Organization Administrator.
```

### Revocation Success:
After revoking, look for:
```
✅ Role updated successfully for [name].
Changed from Organization Administrator to User.
```

### Expected Result:
- ✅ All success messages appear
- ✅ Messages include role information
- ✅ Messages are clear and user-friendly
- ✅ Messages disappear after ~3 seconds

---

## Test 10: Error Handling ✅

### Test: Role Selector Validation Error
1. Go to: `/admin/invite-user` (as SuperAdmin)
2. Fill all fields EXCEPT role
3. Try to submit
4. Expected: Error that role is required

### Test: Self-Promotion Error
1. Find yourself in user list
2. Try to click promote/revoke (should be hidden)
3. If somehow you click it:
4. Expected: "❌ You cannot change your own role"

### Expected Result:
- ✅ Clear error messages
- ✅ Errors show in red
- ✅ Errors don't let you proceed
- ✅ Errors disappear when you fix the issue

---

## Performance Checks ✅

- [ ] Role selector loads instantly
- [ ] Invite form submits within 2 seconds
- [ ] User list loads quickly
- [ ] Promotion modal opens instantly
- [ ] Revocation modal opens instantly
- [ ] Role updates complete within 3 seconds
- [ ] User list refreshes immediately after role change

---

## Browser Compatibility ✅

Test on:
- [ ] Chrome (latest)
- [ ] Firefox (latest)
- [ ] Safari (if available)
- [ ] Edge (latest)

Expected:
- ✅ All features work the same
- ✅ Styling renders correctly
- ✅ Dropdowns functional
- ✅ Modals responsive

---

## Mobile Testing ✅

On mobile browser:
- [ ] Go to: `/admin/invite-user`
- [ ] Role selector still visible
- [ ] Dropdown functions
- [ ] Can select roles
- [ ] Form submits successfully

Expected:
- ✅ Role selector responsive
- ✅ Dropdowns work on touch
- ✅ Modals readable
- ✅ All buttons clickable

---

## Logged-Out User Test ✅

### What to Do:
1. Log out of application
2. Try to go to: `/admin/invite-user`
3. Try to go to: `/admin/users`

### Expected Result:
- ✅ Redirected to login page
- ✅ Cannot access invite/users pages without login
- ✅ Security properly enforced

---

## OrgAdmin User Test ✅

### Prerequisites:
- [ ] You have an OrgAdmin account (not SuperAdmin)
- [ ] Log in as OrgAdmin

### What to Check:
1. Go to: `/admin/invite-user`
   - [ ] Role selector is NOT visible
   - [ ] Regular invite form shows

2. Go to: `/admin/users`
   - [ ] "Promote to OrgAdmin" action NOT visible
   - [ ] "Revoke Admin Rights" action NOT visible
   - [ ] Other user management actions visible

### Expected Result:
- ✅ Role selector completely hidden
- ✅ Promotion/revocation actions completely hidden
- ✅ Only SuperAdmin can manage roles
- ✅ Access control working correctly

---

## Final Verification Checklist

Before declaring tests complete:

- [ ] All 10 test scenarios completed
- [ ] All expected results match actual results
- [ ] No unexpected errors in console
- [ ] No broken UI elements
- [ ] All styling correct
- [ ] All modals working
- [ ] All success messages showing
- [ ] All validation working
- [ ] Self-protection working
- [ ] Permission levels correct
- [ ] Database updates persist
- [ ] User list updates correctly
- [ ] Role badges update correctly
- [ ] Forms validate properly
- [ ] Mobile responsive

---

## Issues Found

List any issues discovered:

1. **Issue:** [Description]
   - **Severity:** High/Medium/Low
   - **Steps to Reproduce:** [Steps]
   - **Expected vs Actual:** [Difference]
   - **Suggested Fix:** [Solution]

2. **Issue:** [Description]
   - **Severity:** High/Medium/Low
   - **Steps to Reproduce:** [Steps]
   - **Expected vs Actual:** [Difference]
   - **Suggested Fix:** [Solution]

---

## Sign-Off

- **Tester Name:** ___________________
- **Date:** ___________________
- **Build Version:** ___________________
- **Overall Status:** ✅ PASS / ⚠️ PASS WITH ISSUES / ❌ FAIL

**Notes:**
[Any additional observations or comments]

