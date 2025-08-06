# Troubleshooting: App Role Assignment Issues

## Required Microsoft Graph Permissions

For automatic app role assignment to work, your Azure AD application needs these permissions:

### **Application Permissions (Required):**
1. **AppRoleAssignment.ReadWrite.All** - To assign app roles to users
2. **User.Read.All** - To read user information
3. **Directory.Read.All** - To read directory objects

### **How to Add Permissions:**
1. Go to Azure Portal → Azure Active Directory → App registrations
2. Find your AdminConsole app → Click on it
3. Go to "API permissions"
4. Click "Add a permission" → Microsoft Graph → Application permissions
5. Add the required permissions above
6. **IMPORTANT**: Click "Grant admin consent" after adding permissions

## Common Issues:

### **Issue 1: "Insufficient privileges" error**
- **Cause**: Missing AppRoleAssignment.ReadWrite.All permission
- **Solution**: Add the permission and grant admin consent

### **Issue 2: "User not found" error**
- **Cause**: User ID format issue or user doesn't exist yet
- **Solution**: Check logs for user ID format and timing

### **Issue 3: "Application not found" error**
- **Cause**: Wrong Service Principal ID
- **Solution**: Verify Service Principal ID: 8ba6461c-c478-471e-b1f4-81b6a33481b2

### **Issue 4: "AppRole not found" error**
- **Cause**: Wrong Role ID
- **Solution**: Verify OrgAdmin Role ID: 5099e0c0-99b5-41f1-bd9e-ff2301fe3e73

## Debug Steps:

1. **Check Application Logs**: Look for detailed error messages
2. **Verify Permissions**: Ensure all required permissions are granted
3. **Test User ID**: Ensure the user exists and ID is valid GUID
4. **Check Timing**: Role assignment happens after B2B invitation - user must exist

## Testing Role Assignment Manually:

You can test role assignment manually in Azure AD:
1. Azure Portal → Enterprise applications → AdminConsole
2. Users and groups → Add user/group 
3. Select the invited user → Select OrgAdmin role → Assign

If manual assignment works but automatic doesn't, it's a permissions issue.