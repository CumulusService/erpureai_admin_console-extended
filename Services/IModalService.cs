using AdminConsole.Components.Shared;

namespace AdminConsole.Services;

public interface IModalService
{
    Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel");
    Task<bool> ShowWarningAsync(string title, string message, string confirmText = "Continue", string cancelText = "Cancel");
    Task<bool> ShowDangerAsync(string title, string message, string confirmText = "Delete", string cancelText = "Cancel");
    Task ShowInfoAsync(string title, string message, string confirmText = "OK");
    Task ShowSuccessAsync(string title, string message, string confirmText = "OK");
    
    // Specialized methods
    Task<bool> ShowUnsavedChangesAsync(string context = "form", string userRole = "User");
    Task<bool> ShowDeleteConfirmationAsync(string itemName, string itemType = "item");
    Task<bool> ShowNavigationWarningAsync(string fromPage, string toPage);
    
    // Events for UI components to listen to
    event EventHandler<ModalEventArgs> ModalRequested;
}

public class ModalEventArgs : EventArgs
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string ConfirmText { get; set; } = "OK";
    public string CancelText { get; set; } = "Cancel";
    public string Icon { get; set; } = "";
    public string ConfirmIcon { get; set; } = "";
    public string CancelIcon { get; set; } = "";
    public ModernConfirmationModal.ModalType Type { get; set; } = ModernConfirmationModal.ModalType.Confirmation;
    public ModernConfirmationModal.ModalSize Size { get; set; } = ModernConfirmationModal.ModalSize.Medium;
    public bool ShowCancelButton { get; set; } = true;
    public TaskCompletionSource<bool> CompletionSource { get; set; } = new();
}