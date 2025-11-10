# Simple Testing Guide - 5 Minutes

## Test 1: Invite User as OrgAdmin (2 min)

1. **Go to:** `https://localhost:7192/admin/invite-user`
2. **Look between "Display Name" and "Agent Types"** - you should see:
   ```
   👑 User Role *
   [-- Select Role --]
   ```
3. **Fill in:**
   - Display Name: "Test Admin"
   - Email: "test@company.com"
   - Agent Types: Pick one
   - Database: Pick one
   - **User Role: Select "👨‍💼 Organization Administrator"**
4. **Click "Send Invitation"**
5. **Result:** You should see: `"Role: Organization Administrator"`

---

## Test 2: Promote User to OrgAdmin (2 min)

1. **Go to:** `https://localhost:7192/admin/users`
2. **Find a user with role "User"** (blue badge)
3. **Click the (⋮) three dots menu** on that user's row
4. **Click "👨‍💼 Promote to OrgAdmin"** (orange action)
5. **Click "Promote to OrgAdmin"** in the dialog
6. **Result:** User's badge changes from blue to orange

---

## Test 3: Revoke Admin Rights (1 min)

1. **Go to:** `https://localhost:7192/admin/users`
2. **Find the user you just promoted** (has orange badge)
3. **Click the (⋮) three dots menu** on that user's row
4. **Click "🚫 Revoke Admin Rights"** (RED action)
5. **Click "Revoke Admin Rights"** in the dialog
6. **Result:** User's badge changes from orange back to blue

---

## That's It! ✅

All three features working = **SUCCESS**

If you see any **errors** or **missing UI elements**, check:
- Are you logged in as **SuperAdmin**?
- Did you enable the feature in code?
- Try refreshing the page (Ctrl+F5)

