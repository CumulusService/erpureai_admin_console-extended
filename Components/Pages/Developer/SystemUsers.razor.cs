using AdminConsole.Models;
using AdminConsole.Services;
using Microsoft.AspNetCore.Components;

namespace AdminConsole.Components.Pages.Developer;

public partial class SystemUsers : ComponentBase
{
    // Data properties
    private List<SystemUser> tenantUsers = new();
    private List<SystemUser> guestUsers = new();
    private List<SystemUser> systemUsers = new();
    private SystemUserStatistics? statistics;

    // UI state
    private bool isLoading = true;
    private bool isProcessing = false;
    private string domainFilter = "";
    private string customDomain = "";
    private string roleFilter = "";
    private string statusFilter = "";

    // Modal state
    private bool ShowInviteModal = false;
    private SystemUser? selectedUser;
    private bool showPromoteModal = false;
    private bool showDemoteModal = false;
    private bool showDeactivateModal = false;
    private UserRole promoteTargetRole = UserRole.SuperAdmin;

    // Form models
    private InviteSystemUserModel inviteModel = new();
    private string inviteErrorMessage = "";
    private UserCreationResult? inviteResult;
    private Dictionary<string, string> inviteValidationErrors = new();
    
    // Action modal error messages
    private string promoteErrorMessage = "";
    private string demoteErrorMessage = "";
    private string deactivateErrorMessage = "";
    
    // Role selection for demote modal
    private UserRole selectedNewRole = UserRole.User;

    // Computed properties - All users combined with filters applied
    private List<SystemUser> allUsers => FilterAllUsers();

    protected override async Task OnInitializedAsync()
    {
        await RefreshData();
    }

    private async Task RefreshData()
    {
        isLoading = true;
        StateHasChanged();

        try
        {
            Logger.LogInformation("🔄 SystemUsers.RefreshData: Starting to refresh system user data...");
            
            // Load data sequentially to avoid DbContext concurrency issues
            Logger.LogInformation("👥 SystemUsers.RefreshData: Loading tenant users...");
            tenantUsers = await SystemUserService.GetAllTenantUsersAsync();
            Logger.LogInformation("👥 SystemUsers.RefreshData: Loaded {Count} tenant users", tenantUsers.Count);
            
            Logger.LogInformation("👤 SystemUsers.RefreshData: Loading guest users...");
            guestUsers = await SystemUserService.GetAllGuestUsersAsync();
            Logger.LogInformation("👤 SystemUsers.RefreshData: Loaded {Count} guest users", guestUsers.Count);
            
            Logger.LogInformation("📊 SystemUsers.RefreshData: Loading statistics...");
            statistics = await SystemUserService.GetSystemStatisticsAsync();
            
            Logger.LogInformation("✅ SystemUsers.RefreshData: All data loading completed successfully");
            
            // System users are all users with database records
            systemUsers = tenantUsers.Concat(guestUsers).Where(u => u.HasDatabaseRecord).ToList();
            
            Logger.LogInformation("✅ SystemUsers.RefreshData: Successfully loaded {TenantCount} tenant users, {GuestCount} guest users, {SystemCount} system users", 
                tenantUsers.Count, guestUsers.Count, systemUsers.Count);
                
            // Log detailed user information for debugging
            Logger.LogInformation("🔍 SystemUsers.RefreshData: All users combined count: {AllUsersCount}", allUsers.Count);
            
            foreach (var user in allUsers.Take(10)) // Log first 10 users for debugging
            {
                Logger.LogInformation("🔍 SystemUsers.RefreshData: User - Email={Email}, Status={Status}, StatusDisplay={StatusDisplay}, BadgeClass={BadgeClass}, IsEnabled={IsEnabled}, HasDbRecord={HasDbRecord}, Role={Role}",
                    user.Email, user.Status, user.StatusDisplayName, user.StatusBadgeClass, user.IsEnabled, user.HasDatabaseRecord, user.DatabaseRole);
            }
            
            if (allUsers.Count > 10)
            {
                Logger.LogInformation("🔍 SystemUsers.RefreshData: ... and {RemainingCount} more users", allUsers.Count - 10);
            }
            
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "💥 SystemUsers.RefreshData: CRITICAL ERROR refreshing system user data: {ErrorMessage}", ex.Message);
            Logger.LogError("💥 SystemUsers.RefreshData: Full exception details: {FullException}", ex.ToString());
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private void ResetFilters()
    {
        domainFilter = "";
        customDomain = "";
        roleFilter = "";
        statusFilter = "";
        StateHasChanged();
    }

    private List<SystemUser> FilterAllUsers()
    {
        var combinedUsers = tenantUsers.Concat(guestUsers).ToList();
        
        // Apply domain filter
        if (!string.IsNullOrEmpty(domainFilter))
        {
            var filterDomain = domainFilter == "custom" ? customDomain : domainFilter;
            if (!string.IsNullOrEmpty(filterDomain))
            {
                combinedUsers = combinedUsers.Where(u => u.Email.EndsWith($"@{filterDomain}", StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }
        
        // Apply role filter
        if (!string.IsNullOrEmpty(roleFilter))
        {
            if (Enum.TryParse<UserRole>(roleFilter, out var targetRole))
            {
                combinedUsers = combinedUsers.Where(u => u.DatabaseRole == targetRole).ToList();
            }
        }
        
        // Apply status filter
        if (!string.IsNullOrEmpty(statusFilter))
        {
            if (Enum.TryParse<SystemUserStatus>(statusFilter, out var targetStatus))
            {
                combinedUsers = combinedUsers.Where(u => u.Status == targetStatus).ToList();
            }
        }
        
        return combinedUsers.OrderBy(u => u.DisplayName).ToList();
    }

    private async Task OnDomainFilterChange(ChangeEventArgs e)
    {
        domainFilter = e.Value?.ToString() ?? "";
        if (domainFilter != "custom")
        {
            customDomain = "";
        }
        StateHasChanged();
    }
    
    private async Task OnRoleFilterChange(ChangeEventArgs e)
    {
        roleFilter = e.Value?.ToString() ?? "";
        StateHasChanged();
    }
    
    private async Task OnStatusFilterChange(ChangeEventArgs e)
    {
        statusFilter = e.Value?.ToString() ?? "";
        StateHasChanged();
    }

    private async Task ApplyCustomDomainFilter()
    {
        if (domainFilter == "custom" && !string.IsNullOrWhiteSpace(customDomain))
        {
            // Trigger refresh of filtered data
            StateHasChanged();
        }
    }

    #region Invite System User Modal

    private void CloseInviteModal()
    {
        ShowInviteModal = false;
        inviteModel = new InviteSystemUserModel();
        inviteErrorMessage = "";
        inviteResult = null;
        inviteValidationErrors.Clear();
        StateHasChanged();
    }

    private async Task ValidateInviteEmail()
    {
        inviteValidationErrors.Remove("email");
        
        if (string.IsNullOrWhiteSpace(inviteModel.Email))
        {
            inviteValidationErrors["email"] = "Email address is required";
            return;
        }

        // Validate email format
        if (!IsValidEmail(inviteModel.Email))
        {
            inviteValidationErrors["email"] = "Please enter a valid email address";
            return;
        }

        // Validate business domain (block private emails)
        var domainValidation = BusinessDomainValidationService.ValidateBusinessDomain(inviteModel.Email);
        if (!domainValidation.IsValid)
        {
            inviteValidationErrors["email"] = domainValidation.Message;
            return;
        }

        StateHasChanged();
    }

    private async Task SendInvitation()
    {
        // Final validation
        await ValidateInviteEmail();
        
        if (string.IsNullOrWhiteSpace(inviteModel.DisplayName))
        {
            inviteValidationErrors["displayName"] = "Display name is required";
            StateHasChanged();
            return;
        }

        if (inviteValidationErrors.Any())
        {
            inviteErrorMessage = "Please fix the validation errors above";
            StateHasChanged();
            return;
        }

        isProcessing = true;
        inviteErrorMessage = "";
        StateHasChanged();

        try
        {
            inviteResult = await SystemUserService.CreateSystemUserAsync(
                inviteModel.Email, 
                inviteModel.DisplayName, 
                inviteModel.Role);

            if (inviteResult.Success)
            {
                Logger.LogInformation("Successfully sent system user invitation to {Email} with role {Role}", 
                    inviteModel.Email, inviteModel.Role);
                
                // Refresh data to show new user
                await RefreshData();
                
                // Show success and keep modal open briefly
                await Task.Delay(2000);
                CloseInviteModal();
            }
            else
            {
                inviteErrorMessage = string.Join("; ", inviteResult.Errors);
                if (inviteResult.Warnings.Any())
                {
                    inviteErrorMessage += " Warnings: " + string.Join("; ", inviteResult.Warnings);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending system user invitation to {Email}", inviteModel.Email);
            inviteErrorMessage = $"Failed to send invitation: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    #endregion

    #region User Management Actions

    private void ShowPromoteModal(SystemUser user, UserRole targetRole)
    {
        selectedUser = user;
        promoteTargetRole = targetRole;
        promoteErrorMessage = "";
        showPromoteModal = true;
        StateHasChanged();
        Logger.LogInformation("Showing promote modal for {Email} to {Role}", user.Email, targetRole);
    }

    private void ShowDemoteModal(SystemUser user)
    {
        selectedUser = user;
        demoteErrorMessage = "";
        showDemoteModal = true;
        StateHasChanged();
        Logger.LogInformation("Showing demote modal for {Email}", user.Email);
    }

    private void ShowDeactivateModal(SystemUser user)
    {
        selectedUser = user;
        deactivateErrorMessage = "";
        showDeactivateModal = true;
        StateHasChanged();
        Logger.LogInformation("Showing deactivate modal for {Email}", user.Email);
    }

    private async Task ConfirmPromoteUser()
    {
        if (selectedUser == null)
            return;

        try
        {
            isProcessing = true;
            promoteErrorMessage = "";
            StateHasChanged();

            var result = await SystemUserService.PromoteUserAsync(selectedUser.Id, promoteTargetRole);
            
            if (result.Success)
            {
                Logger.LogInformation("Successfully promoted {Email} to {Role}", selectedUser.Email, promoteTargetRole);
                await RefreshData(); // Refresh to show updated roles
                ClosePromoteModal();
            }
            else
            {
                promoteErrorMessage = string.Join("; ", result.Errors);
                Logger.LogWarning("Failed to promote {Email}: {Errors}", selectedUser.Email, promoteErrorMessage);
            }
        }
        catch (Exception ex)
        {
            promoteErrorMessage = $"Error promoting user: {ex.Message}";
            Logger.LogError(ex, "Exception promoting user {Email}", selectedUser.Email);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    private void ClosePromoteModal()
    {
        showPromoteModal = false;
        selectedUser = null;
        promoteErrorMessage = "";
        StateHasChanged();
    }

    private void CloseDemoteModal()
    {
        showDemoteModal = false;
        selectedUser = null;
        demoteErrorMessage = "";
        StateHasChanged();
    }

    private void CloseDeactivateModal()
    {
        showDeactivateModal = false;
        selectedUser = null;
        deactivateErrorMessage = "";
        StateHasChanged();
    }
    
    private async Task ConfirmRoleChange()
    {
        if (selectedUser == null)
        {
            Logger.LogWarning("💥 ConfirmRoleChange: selectedUser is null");
            return;
        }

        try
        {
            Logger.LogInformation("🔄 ConfirmRoleChange: Starting role change for user {Email} (ID: {UserId}) from {CurrentRole} to {NewRole}", 
                selectedUser.Email, selectedUser.Id, selectedUser.DatabaseRole, selectedNewRole);
                
            isProcessing = true;
            demoteErrorMessage = "";
            StateHasChanged();

            var result = await SystemUserService.UpdateUserRoleAsync(selectedUser.Id, selectedNewRole);
            
            Logger.LogInformation("🔄 ConfirmRoleChange: UpdateUserRoleAsync returned: {Result} for user {Email}", result, selectedUser.Email);
            
            if (result)
            {
                Logger.LogInformation("✅ ConfirmRoleChange: Successfully updated role for {Email} to {Role}", selectedUser.Email, selectedNewRole);
                
                Logger.LogInformation("🔄 ConfirmRoleChange: Refreshing data to show updated roles...");
                await RefreshData(); // Refresh to show updated roles
                
                Logger.LogInformation("✅ ConfirmRoleChange: Data refresh completed, closing modal");
                CloseDemoteModal();
            }
            else
            {
                demoteErrorMessage = "Failed to update user role. Please try again.";
                Logger.LogWarning("❌ ConfirmRoleChange: Failed to update role for {Email} - service returned false", selectedUser.Email);
            }
        }
        catch (Exception ex)
        {
            demoteErrorMessage = $"Error updating user role: {ex.Message}";
            Logger.LogError(ex, "💥 ConfirmRoleChange: EXCEPTION updating user role for {Email}: {ErrorMessage}", 
                selectedUser.Email, ex.Message);
            Logger.LogError("💥 ConfirmRoleChange: Full exception details: {FullException}", ex.ToString());
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
            Logger.LogInformation("🏁 ConfirmRoleChange: Process completed for user {Email}", selectedUser?.Email ?? "unknown");
        }
    }
    
    private async Task ConfirmRevokeAccess()
    {
        if (selectedUser == null)
            return;

        try
        {
            isProcessing = true;
            deactivateErrorMessage = "";
            StateHasChanged();

            var result = await SystemUserService.DeactivateSystemUserAsync(selectedUser.Id);
            
            if (result)
            {
                Logger.LogInformation("Successfully revoked access for {Email}", selectedUser.Email);
                await RefreshData(); // Refresh to show updated status
                CloseDeactivateModal();
            }
            else
            {
                deactivateErrorMessage = "Failed to revoke user access. Please try again.";
                Logger.LogWarning("Failed to revoke access for {Email}", selectedUser.Email);
            }
        }
        catch (Exception ex)
        {
            deactivateErrorMessage = $"Error revoking user access: {ex.Message}";
            Logger.LogError(ex, "Exception revoking access for {Email}", selectedUser.Email);
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Helper Methods

    private bool HasInviteValidationError(string field) => inviteValidationErrors.ContainsKey(field);
    
    private string GetInviteValidationError(string field) => 
        inviteValidationErrors.TryGetValue(field, out var error) ? error : "";

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private string GetRoleDescription(UserRole? role) => role switch
    {
        UserRole.Developer => "Master Developer - Full system access and configuration",
        UserRole.SuperAdmin => "Super Administrator - Organization management and user administration",
        UserRole.OrgAdmin => "Organization Administrator - Manage organization users and settings",
        UserRole.User => "Standard User - Basic organization access",
        null => "No system access granted",
        _ => "Unknown role"
    };

    #endregion
}

/// <summary>
/// Model for inviting new system users
/// </summary>
public class InviteSystemUserModel
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.SuperAdmin;
}