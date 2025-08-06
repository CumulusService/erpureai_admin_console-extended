namespace AdminConsole.Services;

/// <summary>
/// Service for tracking unsaved form changes and preventing data loss
/// </summary>
public interface IUnsavedChangesService
{
    /// <summary>
    /// Register a form as having unsaved changes
    /// </summary>
    /// <param name="formId">Unique identifier for the form</param>
    void MarkFormAsChanged(string formId);
    
    /// <summary>
    /// Mark a form as saved (no unsaved changes)
    /// </summary>
    /// <param name="formId">Unique identifier for the form</param>
    void MarkFormAsSaved(string formId);
    
    /// <summary>
    /// Check if any forms have unsaved changes
    /// </summary>
    /// <returns>True if there are unsaved changes</returns>
    bool HasUnsavedChanges();
    
    /// <summary>
    /// Check if a specific form has unsaved changes
    /// </summary>
    /// <param name="formId">Unique identifier for the form</param>
    /// <returns>True if the form has unsaved changes</returns>
    bool HasUnsavedChanges(string formId);
    
    /// <summary>
    /// Get list of forms with unsaved changes
    /// </summary>
    /// <returns>List of form IDs with unsaved changes</returns>
    List<string> GetFormsWithUnsavedChanges();
    
    /// <summary>
    /// Clear all unsaved changes tracking
    /// </summary>
    void ClearAll();
    
    /// <summary>
    /// Event fired when unsaved changes status changes
    /// </summary>
    event EventHandler<UnsavedChangesEventArgs>? UnsavedChangesChanged;
}

/// <summary>
/// Event arguments for unsaved changes notifications
/// </summary>
public class UnsavedChangesEventArgs : EventArgs
{
    public string FormId { get; set; } = string.Empty;
    public bool HasUnsavedChanges { get; set; }
}