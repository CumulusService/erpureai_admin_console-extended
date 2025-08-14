using AdminConsole.Components.Shared;

namespace AdminConsole.Services;

public class ModalService : IModalService
{
    public event EventHandler<ModalEventArgs>? ModalRequested;

    public async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel")
    {
        var args = new ModalEventArgs
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            Icon = "fas fa-question-circle",
            Type = ModernConfirmationModal.ModalType.Confirmation,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    public async Task<bool> ShowWarningAsync(string title, string message, string confirmText = "Continue", string cancelText = "Cancel")
    {
        var args = new ModalEventArgs
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            Icon = "fas fa-exclamation-triangle",
            Type = ModernConfirmationModal.ModalType.Warning,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    public async Task<bool> ShowDangerAsync(string title, string message, string confirmText = "Delete", string cancelText = "Cancel")
    {
        var args = new ModalEventArgs
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            Icon = "fas fa-exclamation-triangle",
            ConfirmIcon = "fas fa-trash",
            CancelIcon = "fas fa-times",
            Type = ModernConfirmationModal.ModalType.Danger,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    public async Task ShowInfoAsync(string title, string message, string confirmText = "OK")
    {
        var args = new ModalEventArgs
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            Icon = "fas fa-info-circle",
            Type = ModernConfirmationModal.ModalType.Info,
            ShowCancelButton = false
        };

        await ShowModalAsync(args);
    }

    public async Task ShowSuccessAsync(string title, string message, string confirmText = "OK")
    {
        var args = new ModalEventArgs
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            Icon = "fas fa-check-circle",
            Type = ModernConfirmationModal.ModalType.Success,
            ShowCancelButton = false
        };

        await ShowModalAsync(args);
    }

    public async Task<bool> ShowUnsavedChangesAsync(string context = "form", string userRole = "User")
    {
        var contextualMessage = GetUnsavedChangesMessage(context, userRole);
        var args = new ModalEventArgs
        {
            Title = "Unsaved Changes",
            Message = contextualMessage,
            ConfirmText = "Leave Without Saving",
            CancelText = "Stay and Save",
            Icon = "fas fa-exclamation-triangle",
            ConfirmIcon = "fas fa-sign-out-alt",
            CancelIcon = "fas fa-save",
            Type = ModernConfirmationModal.ModalType.Warning,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    public async Task<bool> ShowDeleteConfirmationAsync(string itemName, string itemType = "item")
    {
        var args = new ModalEventArgs
        {
            Title = "Confirm Delete",
            Message = $"Are you sure you want to delete <strong>{itemName}</strong>?<br><br>" +
                     $"This action cannot be undone and the {itemType} will be permanently removed.",
            ConfirmText = "Delete",
            CancelText = "Cancel",
            Icon = "fas fa-exclamation-triangle",
            ConfirmIcon = "fas fa-trash",
            CancelIcon = "fas fa-times",
            Type = ModernConfirmationModal.ModalType.Danger,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    public async Task<bool> ShowNavigationWarningAsync(string fromPage, string toPage)
    {
        var args = new ModalEventArgs
        {
            Title = "Navigation Warning",
            Message = $"You are about to navigate from <strong>{fromPage}</strong> to <strong>{toPage}</strong>.<br><br>" +
                     "Any unsaved changes will be lost. Do you want to continue?",
            ConfirmText = "Continue",
            CancelText = "Stay Here",
            Icon = "fas fa-route",
            ConfirmIcon = "fas fa-arrow-right",
            CancelIcon = "fas fa-times",
            Type = ModernConfirmationModal.ModalType.Warning,
            ShowCancelButton = true
        };

        return await ShowModalAsync(args);
    }

    private async Task<bool> ShowModalAsync(ModalEventArgs args)
    {
        var completionSource = new TaskCompletionSource<bool>();
        args.CompletionSource = completionSource;

        ModalRequested?.Invoke(this, args);

        // Add timeout to prevent hanging
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
        var completedTask = await Task.WhenAny(completionSource.Task, timeoutTask);

        if (completedTask == completionSource.Task)
        {
            return await completionSource.Task;
        }
        else
        {
            // Timeout - assume user wants to cancel/stay
            return false;
        }
    }

    private static string GetUnsavedChangesMessage(string context, string userRole)
    {
        var roleContext = userRole.ToLowerInvariant() switch
        {
            "superadmin" => "admin configuration",
            "orgadmin" => "organization settings",
            "developer" => "agent configuration",
            _ => "form data"
        };

        var actionContext = context.ToLowerInvariant() switch
        {
            "invitation" => "invitation details",
            "usermanagement" => "user management changes",
            "settings" => "settings changes",
            "agenttype" => "agent type configuration",
            "form" => "form changes",
            _ => "unsaved changes"
        };

        return $"You have <strong>{actionContext}</strong> that haven't been saved.<br><br>" +
               $"If you leave now, your {roleContext} will be lost. What would you like to do?";
    }
}